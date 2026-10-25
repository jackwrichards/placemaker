// Placemaker — park stats of a build: computed with the Core estimator, stored in the receipt, and the part
// the game can use without a park building applied to the owner lot (GPL-3.0). docs/PARK-STATS.md.
//
// What reaches the game (decompiled CS2 1.6.2f1):
//  * Attractiveness → a vanilla Game.Buildings.AttractivenessProvider on the owner lot. TourismSystem sums
//    AttractivenessProvider of EVERY non-temp entity into the city attractiveness (Σ a²/10000 → sigmoid), so the
//    build counts for tourism like a vanilla park. AttractionSystem (which would reset the value from the prefab's
//    AttractionData) only updates entities with an UpdateFrame shared component — lots have none, so our value
//    stays. ResourceAvailabilitySystem finds no pathfinding target on a lot (no spawn locations / sub lanes), so
//    there is no local land-value effect; CitizenPathfindSetup only targets Buildings. The value is multiplied
//    once, at build time, by the vanilla terrain bonus (forest/shore/height) at the outline's centre, and taken
//    as fully maintained (vanilla: × 0.8 + 0.2 × maintenance).
//  * Leisure, coverage (entertainment → wellbeing) and upkeep need a real park building (pathfinding from a road
//    connection, ServiceCoverageSystem, ParkAISystem): stored and shown, not applied (docs/PARK-STATS.md §5).
// Save safety: AttractivenessProvider is a vanilla, serialised component. Without the mod the owner lot stays
// (obsolete prefab, RESEARCH-NOTES 40) and keeps counting for tourism; with the mod, removing the build removes it.

using System;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Groups;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Setting "Park attractiveness counts for tourism" (default on).</summary>
        public static bool ApplyParkStats = true;

        /// <summary>Park stats of the current request (null when not a park type). Never throws.</summary>
        private ParkStatsResult ComputeParkStats()
        {
            try
            {
                var outline = BuildOutline();
                double area = outline != null ? Math.Abs(Poly.SignedArea(outline)) : 0;
                var result = ParkStatsEstimator.Estimate(ParkStatsInput.From(_request.Plan, area, _request.Recipe), _request.Recipe?.ParkStats);
                return result.Applicable ? result : null;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: park stats could not be computed (build kept): " + e.Message);
                return null;
            }
        }

        /// <summary>FinishRecord: store the stats in the receipt, show attractiveness in the info panel, apply it.</summary>
        private void FinishParkStats(GroupRecord rec)
        {
            if (LooseBuild) return;
            long t0 = Now;
            var stats = ComputeParkStats();
            Sub("Park stats: compute", t0);
            if (stats == null) return;
            rec.ParkStats = ParkStatsRecord.Of(stats, _request.Recipe?.ParkStats);
            double attr = stats.ValueOf(ParkStatIds.Attractiveness);
            rec.Stats.Add(new PlanStat("Placemaker.Stat.Attractiveness", attr, "count"));
            if (!ApplyParkStats) return;
            try
            {
                var owner = Entity.Null;
                if (_record != Entity.Null && EntityManager.Exists(_record)) owner = EntityManager.GetComponentData<PlacemakerGroup>(_record).Owner;
                long ta = Now;
                int applied = ApplyAttractiveness(EntityManager, owner, attr, TerrainBonus(owner));
                Sub("Park stats: attractiveness", ta);
                if (applied > 0) rec.ParkStats.Applied[ParkStatIds.InGameAttractiveness] = applied;
                if (applied >= 0) Mod.Log.Info($"Placemaker: group {_groupId} attractiveness {applied} (suggested {stats.Get(ParkStatIds.Attractiveness).Suggested}).");
                else Mod.Log.Info($"Placemaker: group {_groupId} has no owner lot; its park attractiveness ({attr}) is stored but not applied.");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: applying park attractiveness failed (build kept): " + e.Message);
            }
            t0 = Now;
            RequestParkCompanion(rec, stats);
            Sub("Park stats: companion", t0);
        }

        /// <summary>
        /// (s17) The companion park building at the best road frontage (Economy/ParkCompanionSystem). The lot keeps
        /// attractiveness until the companion exists (it then takes over); without a frontage the lot keeps it for good.
        /// </summary>
        private void RequestParkCompanion(GroupRecord rec, ParkStatsResult stats)
        {
            try
            {
                var companions = World.GetOrCreateSystemManaged<Economy.ParkCompanionSystem>();
                if (!companions.Available) return;
                var outline = BuildOutline();
                if (outline == null) return;
                var ring3 = new float3[outline.Length];
                for (int i = 0; i < outline.Length; i++) ring3[i] = new float3(outline[i].x, 0f, outline[i].y);
                var nets = new Preview.SiteCapture(EntityManager, World).NetsAround(ring3, 30f);
                var objects = new System.Collections.Generic.List<float2>();
                var paths = new System.Collections.Generic.List<(float2, float2, float)>();
                ParkCompanionPlanner.Obstacles(_request.Plan, objects, paths);
                var site = ParkCompanionPlanner.Find(outline, nets, objects, paths);
                ParkStatsEstimator.WithCompanion(stats, true, site);
                if (!site.Found)
                {
                    NoteSkip("Placemaker.Build.NoRoadEdge", null, IssueSeverity.Info, 0);
                    Mod.Log.Info($"Placemaker: group {_groupId} has no road frontage for a park companion; only attractiveness applies.");
                    return;
                }
                string name = ParkStatsEstimator.KeyOf(stats).Name;
                companions.Request(_groupId, name, site.Position, site.Yaw);
                rec.ParkStats.Applied[ParkStatIds.Companion] = 0;   // requested; 1 once it exists
                Mod.Log.Info($"Placemaker: group {_groupId} park companion '{name}' requested at {site.Position}.");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: park companion not requested (build kept, attractiveness stays on the lot): " + e.Message);
            }
        }

        /// <summary>
        /// Sets the vanilla AttractivenessProvider on an owner lot (adds it when missing; 0 removes it).
        /// Returns the value set, or -1 when the entity cannot carry it. Main thread only.
        /// </summary>
        internal static int ApplyAttractiveness(EntityManager em, Entity owner, double attractiveness, double terrainBonusPercent)
        {
            if (owner == Entity.Null || !em.Exists(owner) || em.HasComponent<Deleted>(owner) || em.HasComponent<Temp>(owner)) return -1;
            // Only lots: an entity with an UpdateFrame would be rewritten by AttractionSystem from its prefab.
            if (!em.HasComponent<global::Game.Areas.Area>(owner) || em.HasComponent(owner, ComponentType.ReadOnly<UpdateFrame>())) return -1;
            int value = (int)Math.Round(Math.Max(0, attractiveness) * (1 + 0.01 * Math.Max(0, terrainBonusPercent)));
            if (value <= 0)
            {
                if (em.HasComponent<AttractivenessProvider>(owner)) em.RemoveComponent<AttractivenessProvider>(owner);
                return 0;
            }
            if (!em.HasComponent<AttractivenessProvider>(owner)) em.AddComponent<AttractivenessProvider>(owner);   // add first (RESEARCH-NOTES 14)
            em.SetComponentData(owner, new AttractivenessProvider { m_Attractiveness = value });
            return value;
        }

        /// <summary>Vanilla terrain bonus in % (forest + shore + height) at the owner lot's centre; 0 when unknown.</summary>
        private double TerrainBonus(Entity owner)
        {
            try
            {
                var outline = BuildOutline();
                if (outline == null) return 0;
                float2 c = Poly.Centroid(outline);
                var system = World.GetExistingSystemManaged<TerrainAttractivenessSystem>();
                var query = GetEntityQuery(ComponentType.ReadOnly<AttractivenessParameterData>());
                if (system == null || query.IsEmptyIgnoreFilter) return 0;
                var data = system.GetData(true, out JobHandle deps);
                deps.Complete();
                var heights = _terrain.GetHeightData(true);
                float bonus = TerrainAttractivenessSystem.EvaluateAttractiveness(new float3(c.x, 0f, c.y), data, heights,
                    query.GetSingleton<AttractivenessParameterData>(), default);
                system.AddReader(default);
                return math.isfinite(bonus) ? math.clamp(bonus, 0f, 100f) : 0;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: terrain attractiveness bonus unavailable: " + e.Message);
                return 0;
            }
        }
    }
}
