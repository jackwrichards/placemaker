// Placemaker neighbourhood polish — buildings the game rebuilt and pieces the game took away (GPL-3.0).
//
// "Outgrown but empty" (the lens shows a building outgrown, it has no garden, yard or car park, or what it had went later;
// painting over it again fixes it). The polish planned a building again only when one of its fence lanes was made or
// moved (CollectChanged) or something was built near it, and the game takes a building's pieces away without either
// (decompiled 1.6.2f1):
//   * Game.Objects.SubObjectSystem rebuilds a building's sub-objects whenever the building is Updated and on every
//     RentersUpdated event (a household or a company moving in or out): FillOldSubObjectsBuffer collects every object the
//     building owns and RemoveUnusedOldSubObjects deletes each one its prefab does not use again. Our bushes, trees, fence
//     and hedge pieces, cars, bins and bay marks are the building's own objects, so a company moving into a shop took its
//     car park's cars, bins and trees (the paving stayed), households moving into apartments took theirs.
//   * Game.Simulation.BuildingConstructionSystem.UpdatePrefab, at the end of construction and at every level-up (the next
//     model swapped in, almost at once), deletes every sub-area of the building and makes the new model's: our paving, grass
//     strips, beds, paths and lawns go too. A building with fence lanes was planned again (its lanes come back); one without
//     (many shops, offices and apartment blocks) never was. A new building without fence lanes was not planned at all once
//     it was finished (only when another building started near it later).
// So: a polished building the game finished or gave another model is planned again (fences or not), surfaces of ours the
// game deleted from a standing building plan it again, and objects of ours it deleted are made again where they stood (the
// same kind, spot, angle and look), next frame, without a plan.

using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        private EntityQuery _rebuiltLots, _lostPieces, _standingPieces, _lostLawns;
        private bool _sinkPending;
        // how often a house was refilled lately (frames in which the game took pieces of it): one whose pieces the game takes
        // away frame after frame (it never should: renters come and go a few times) stops being refilled for a while, so it
        // can never cost every frame
        private readonly Dictionary<Entity, (int Since, int Times, int Last)> _remade = new Dictionary<Entity, (int, int, int)>();
        private const int RemadeWindow = 600, RemadeMax = 30;

        /// <summary>For the game runner: polished buildings planned again because the game rebuilt them, our surfaces and
        /// objects the game deleted from standing buildings, and the objects made again in place.</summary>
        public static int TotalRebuilt, TotalLostAreas, TotalLostObjects, TotalRemade;
        private int _lostLogged;

        // after a city loads: its outgrown buildings looked at once, a few hundred a frame, and the ones left empty by the
        // above (before it was fixed) planned again, behind the player's own work
        private List<Entity> _healQueue;
        private bool _healPending;
        private int _healLooked, _healFound, _healOverRoads;
        // (each looked at for a garden over a road too: a few dozen a frame keep it under 10 ms)
        private const int HealPerFrame = 60;
        /// <summary>For the game runner: outgrown buildings found empty after a load and planned again.</summary>
        public static int TotalHealed;

        private void StartHeal()
        {
            _healPending = true;
            _healQueue = null;
            _healLooked = _healFound = _healOverRoads = 0;
            _replannedForMisplaced.Clear();
            // (pieces judged in the city before: a new city's entities can have the same numbers)
            _keptHidden.Clear();
            _placedAt.Clear();
            _shownTimes.Clear();
            _sinkPending = true;
        }

        /// <summary>After a load: our pieces an older build laid on the ground that take zone cells (a car park's stall marks)
        /// set a hair under it, as new ones are (NeighbourObjectSystem.ZoneSink), so land zoned under them grows buildings.</summary>
        public void SinkOldPiecesSoon() => _sinkPending = true;

        private void SinkOldPieces()
        {
            if (!_sinkPending) return;
            _sinkPending = false;
            int sunk = 0;
            var takes = new Dictionary<Entity, bool>();
            using (var all = _standingPieces.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!takes.TryGetValue(prefab, out var t)) takes[prefab] = t = NeighbourObjectSystem.TakesZone(EntityManager, prefab);
                    if (!t) continue;
                    if (EntityManager.HasComponent<global::Game.Objects.Elevation>(e))
                    {
                        var el = EntityManager.GetComponentData<global::Game.Objects.Elevation>(e);
                        if (el.m_Elevation < 0f) continue;
                        el.m_Elevation = NeighbourObjectSystem.ZoneSink;
                        EntityManager.SetComponentData(e, el);
                    }
                    else EntityManager.AddComponentData(e, new global::Game.Objects.Elevation(NeighbourObjectSystem.ZoneSink, 0));
                    // the game reads the cells under it again (CellCheckSystem: updated objects' bounds)
                    EntityManager.AddComponent<Updated>(e);
                    sunk++;
                }
            if (sunk > 0) Mod.Log.Info($"Placemaker neighbourhood polish: {sunk} car park marks and other pieces that kept zoned land from growing set a hair under the ground");
        }

        /// <summary>A few hundred outgrown buildings of the city just loaded: the ones with nothing they should have
        /// (EmptyReason: never planned, land past the lot with nothing on it, a car park look with no paving) planned again.</summary>
        private void HealEmpty()
        {
            if (!_healPending) return;
            if (_healQueue == null)
            {
                _healQueue = new List<Entity>();
                using (var all = _flaggedLots.ToEntityArray(Allocator.Temp))
                    foreach (var e in all) if (EntityManager.GetComponentData<PolishedLot>(e).Yes) _healQueue.Add(e);
                return;
            }
            int n = math.min(HealPerFrame, _healQueue.Count);
            for (int i = 0; i < n; i++)
            {
                var e = _healQueue[i];
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || !IsGrowable(e)) continue;
                _healLooked++;
                // a garden planned over a road (a build before 22 Oct cut a patch's barriers off at 4 000 in a dense town: gardens
                // grew over the streets left out) is planned again too
                bool overRoad = GardenOverRoad(e);
                if (overRoad) _healOverRoads++;
                else if (EmptyReason(AuditOf(e)) == null) continue;
                else _healFound++;
                _described.Remove(e);
                if (_dirty.Add(e)) { DirtyBy[7]++; TotalHealed++; }
            }
            _healQueue.RemoveRange(0, n);
            if (_healQueue.Count > 0) return;
            _healPending = false;
            _healQueue = null;
            Mod.Log.Info($"Placemaker neighbourhood polish: {_healLooked} outgrown buildings looked at after the load, {_healFound} had nothing they should have (no garden, yard or car park), {_healOverRoads} a garden over a road: planned again");
        }

        /// <summary>The house's saved garden lies over a street, path or track (10 m² or more; its own driveway in its lot is no
        /// street).</summary>
        private bool GardenOverRoad(Entity house)
        {
            var garden = GardenOf(house);
            if (garden == null || garden.Length < 3) return false;
            Placemaker.Core.Geometry.Poly.Bounds(garden, out var lo, out var hi);
            var rings = StreetRingsIn(lo, hi);
            float2[] ccw = null;
            float area = 0f;
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i];
                Placemaker.Core.Geometry.Poly.Bounds(r, out var rlo, out var rhi);
                if (rhi.x < lo.x || rlo.x > hi.x || rhi.y < lo.y || rlo.y > hi.y || !Overlap(garden, r)) continue;
                ccw ??= Placemaker.Core.Geometry.Poly.EnsureCcw(garden);
                foreach (var part in Placemaker.Core.Geometry.Poly.Boolean(new[] { (IReadOnlyList<float2>)ccw }, new[] { (IReadOnlyList<float2>)Placemaker.Core.Geometry.Poly.EnsureCcw(r) },
                    Placemaker.Core.Geometry.BoolOp.Intersection, Placemaker.Core.Geometry.FillRule.NonZero)) area += part.Area;
                if (area >= 10f) return true;
            }
            return false;
        }

        /// <summary>Two rings overlap: an edge of one crosses an edge of the other, or one holds a corner of the other.</summary>
        private static bool Overlap(float2[] a, float2[] b)
        {
            if (Placemaker.Core.Geometry.Poly.Contains(b, a[0]) || Placemaker.Core.Geometry.Poly.Contains(a, b[0])) return true;
            for (int i = 0; i < a.Length; i++)
            {
                var p = a[i];
                var q = a[(i + 1) % a.Length];
                for (int j = 0; j < b.Length; j++)
                {
                    var c = b[j];
                    var d = b[(j + 1) % b.Length];
                    float d1 = Side(c, d, p), d2 = Side(c, d, q), d3 = Side(p, q, c), d4 = Side(p, q, d);
                    if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return true;
                }
            }
            return false;
        }

        private static float Side(float2 a, float2 b, float2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        private void CreateRebuiltQueries()
        {
            // a polished building the game changed this frame (built, levelled up, moved, rebuilt)
            _standingPieces = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PolishPiece>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Objects.Transform>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _rebuiltLots = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<PolishedLot>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            // a house's own lawns Outgrow grew, deleted this frame (a level-up makes the lot's surfaces again)
            _lostLawns = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Areas.Area>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PolishLawnOriginal>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PolishPiece>() },
            });
            // our pieces deleted this frame (by us: NeighbourDeleteSystem.Marked; else by the game)
            _lostPieces = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PolishPiece>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
        }

        /// <summary>Polished buildings the game rebuilt and pieces it took away (CollectChanged, also with the polish off: then
        /// they are only put in line, planned once it is on).</summary>
        private void CollectRebuilt()
        {
            if (!_rebuiltLots.IsEmptyIgnoreFilter)
                using (var lots = _rebuiltLots.ToEntityArray(Allocator.Temp))
                    foreach (var e in lots)
                    {
                        PolishedUpdated++;
                        if (!IsYes(e) || EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(e) || !IsGrowable(e)) continue;
                        var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                        // planned with this model already: nothing new (its objects, if the game took them, come back below).
                        // Never planned: a building finished this session (it waited in _first since it was flagged); after a
                        // load a polished building is as it was saved, and its pieces the game deletes say so below
                        if (_prefabSeen.TryGetValue(e, out var seen) ? seen == prefab : !_first.Contains(e)) continue;
                        // levelled up in a city that plans nothing for that: it keeps its garden
                        if (KeepLevelUp(e)) continue;
                        _described.Remove(e);
                        if (_dirty.Add(e)) { DirtyBy[5]++; TotalRebuilt++; }
                    }
            // a kept level-up's old fences no new one took the place of: in pieces where they stood
            LayOldFences();
            // a kept level-up's own lawn as Outgrow grew it: what lay past the lot made again as a garden lawn
            if (!_lostLawns.IsEmptyIgnoreFilter && _keptLevelUps.Count > 0)
                using (var lawns = _lostLawns.ToEntityArray(Allocator.Temp))
                    foreach (var lawn in lawns)
                    {
                        var owner = EntityManager.GetComponentData<Owner>(lawn).m_Owner;
                        if (owner != Entity.Null && EntityManager.Exists(owner) && !EntityManager.HasComponent<Deleted>(owner) && IsYes(owner)) KeepSurface(lawn, owner, PolishPieceKind.Garden);
                    }
            if (_lostPieces.IsEmptyIgnoreFilter) return;
            List<(Entity Owner, Entity Prefab, float2 At, float Yaw, int Seed)> objects = null;
            using (var pieces = _lostPieces.ToEntityArray(Allocator.Temp))
                foreach (var p in pieces)
                {
                    var owner = EntityManager.GetComponentData<Owner>(p).m_Owner;
                    StandingChanged(owner);
                    if (NeighbourDeleteSystem.Marked.Contains(p)) continue;   // ours: a plan no longer wanted it, or the lot was put back
                    if (owner == Entity.Null || !EntityManager.Exists(owner) || EntityManager.HasComponent<Deleted>(owner) || !EntityManager.HasComponent<global::Game.Buildings.Building>(owner) || !IsYes(owner)) continue;
                    if (EntityManager.HasComponent<global::Game.Areas.Area>(p))
                    {
                        // a surface: the building was rebuilt (another model, its lot made again): planned again; a level-up
                        // kept without a plan makes it again where it stood
                        TotalLostAreas++;
                        if (KeepSurface(p, owner, EntityManager.GetComponentData<PolishPiece>(p).Kind)) continue;
                        _described.Remove(owner);
                        if (_dirty.Add(owner)) DirtyBy[6]++;
                        continue;
                    }
                    if (!EntityManager.HasComponent<global::Game.Objects.Transform>(p)) continue;
                    TotalLostObjects++;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(p);
                    // the look it had (a parked car's colour: a works' fleet shares one), else by its spot
                    int look = EntityManager.HasComponent<PseudoRandomSeed>(p) ? EntityManager.GetComponentData<PseudoRandomSeed>(p).m_Seed : 0;
                    (objects ??= new List<(Entity, Entity, float2, float, int)>()).Add((owner, EntityManager.GetComponentData<PrefabRef>(p).m_Prefab, t.m_Position.xz, PieceIdentity.YawOf(t.m_Rotation), look));
                }
            if (objects == null) return;
            int remade = 0, owners = 0;
            Entity last = Entity.Null;
            foreach (var (owner, prefab, at, yaw, look) in objects)
            {
                // planned again (another model than it was planned with, already waiting, the polish off): its plan makes what
                // it wants. A building with the model it was planned with (people or a company moved in or out, the game marked
                // it Updated, a level-up kept without a plan) is refilled where its pieces stood
                if (!FeatureEnabled || _dirty.Contains(owner) || (EntityManager.HasComponent<Updated>(owner) && !SameModel(owner)))
                {
                    _described.Remove(owner);
                    if (_dirty.Add(owner)) DirtyBy[6]++;
                    continue;
                }
                // a level-up kept without a plan: what stood inside the lot is the new model's business
                if (InsideKeptLot(owner, at)) continue;
                if (owner != last) { owners++; last = owner; }
                var (since, times, lastFrame) = _remade.TryGetValue(owner, out var r) && LotFrame - r.Since < RemadeWindow ? r : (LotFrame, 0, -1);
                if (lastFrame != LotFrame)
                {
                    if (times >= RemadeMax) continue;
                    _remade[owner] = (since, times + 1, LotFrame);
                }
                var name = NameOf(prefab);
                bool piece = name.IndexOf("Piece", System.StringComparison.OrdinalIgnoreCase) >= 0
                    && (name.IndexOf("Fence", System.StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Hedge", System.StringComparison.OrdinalIgnoreCase) >= 0);
                // the look it was made with: a fence or hedge piece one look for the whole garden (FinishPlan), the rest its own
                NeighbourObjectSystem.Queue.Add(new NeighbourObjectSystem.Request
                {
                    Name = name, Prefab = prefab, Position = at, Yaw = yaw, Owner = owner,
                    Seed = piece ? PieceSeed(owner, name) : look,
                });
                remade++;
            }
            TotalRemade += remade;
            if (_remade.Count > 4096) _remade.Clear();
            if (remade > 0 && _lostLogged++ < 20)
                Mod.Log.Info($"Placemaker neighbourhood polish: the game took {remade} pieces from {owners} outgrown buildings (people or a company moved in or out): made again where they stood");
        }
    }
}
