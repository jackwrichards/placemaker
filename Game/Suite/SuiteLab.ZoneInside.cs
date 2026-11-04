// Game runner lab: zoning inside land Outgrow gave to a garden or a yard (the user, 22 Oct: "I'm zoning new areas inside of
// outgrown areas, and they don't spawn in when they should").
//   {"do":"lab","op":"zoneRect","rect":[x0,z0,x1,z1],"zone":"NA Residential Low"}   zones (marquee) the cells in the
//       rectangle (metres from the block's centre; "abs": true for map coordinates), as the zone tool's marquee does
//   {"do":"lab","op":"zoneCells","rect":[x0,z0,x1,z1],"label":"..."}   the zoned cells there: how many are free, occupied
//       (by a building, else by what) or blocked (by what), the buildings standing there, and which gardens cover them

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Game.Neighbours;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private (float2 Lo, float2 Hi) LabRect(ScenarioState st, JObject step)
        {
            var r = (JArray)step["rect"];
            var o = (bool?)step["abs"] == true ? float2.zero : st.S.Block.Center;
            var a = o + new float2((float)r[0], (float)r[1]);
            var c = o + new float2((float)r[2], (float)r[3]);
            return (math.min(a, c), math.max(a, c));
        }

        private IEnumerator LabZoneRect(ScenarioState st, JObject step, JObject rec)
        {
            var (lo, hi) = LabRect(st, step);
            string zoneName = (string)step["zone"] ?? "NA Residential Low";
            string other = zoneName.StartsWith("NA ") ? "EU " + zoneName.Substring(3) : zoneName.StartsWith("EU ") ? "NA " + zoneName.Substring(3) : zoneName;
            if (!_fixture.Prefabs.TryGetValue(zoneName, out var zone) && !_fixture.Prefabs.TryGetValue(other, out zone)) { rec["error"] = "lab: no zone prefab " + zoneName; yield break; }
            var em = _world.EntityManager;
            float3 P(float2 p) => new float3(p.x, _fixture.H(p.x, p.y), p.y);
            int before = _fixture.CountZonedCells();
            yield return ApplyWithFixtureTool(t =>
            {
                var def = t.NewDefinition();
                em.AddComponentData(def, new CreationDefinition { m_Prefab = zone });
                em.AddComponentData(def, new Zoning { m_Position = new Quad3(P(lo), P(new float2(hi.x, lo.y)), P(hi), P(new float2(lo.x, hi.y))), m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee | ZoningFlags.Overwrite });
                em.AddComponent<Updated>(def);
                return 1;
            }, "lab zone rect");
            yield return Frames(40);
            rec["zonedCells"] = _fixture.CountZonedCells() - before;
            Event("build");
            Mod.Log.Info($"Placemaker lab: zoned {zoneName} over ({lo.x:F0},{lo.y:F0})-({hi.x:F0},{hi.y:F0}): {rec["zonedCells"]} cells more");
        }

        private IEnumerator LabZoneCells(ScenarioState st, JObject step, JObject rec)
        {
            yield return Frames(10);
            var (lo, hi) = LabRect(st, step);
            var em = _world.EntityManager;
            var prefabs = _world.GetOrCreateSystemManaged<PrefabSystem>();
            string NameOf(Entity p) { try { return prefabs.GetPrefab<PrefabBase>(p)?.name ?? "?"; } catch (Exception) { return "?"; } }
            bool In(float2 p) => p.x >= lo.x && p.y >= lo.y && p.x <= hi.x && p.y <= hi.y;

            // buildings and their lots there
            var lots = new List<(Entity E, float2[] Lot)>();
            int growables = 0;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    BuildingFootprints.Of(em, e, out var lot, out _);
                    if (lot == null) continue;
                    if (!lot.Any(In) && !In(em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz)) continue;
                    lots.Add((e, lot));
                    if (In(em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz) && em.HasComponent<SpawnableBuildingData>(em.GetComponentData<PrefabRef>(e).m_Prefab)) growables++;
                }
            // the gardens there (Outgrow's)
            var sys = _world.GetExistingSystemManaged<NeighbourPolishSystem>();
            var gardens = sys == null ? new List<(Entity, float2[])>() : sys.Gardens.Where(kv => kv.Value != null && kv.Value.Length >= 3 && em.Exists(kv.Key)).Select(kv => (kv.Key, kv.Value)).ToList();

            int zoned = 0, free = 0, byBuilding = 0, occupiedOther = 0, blocked = 0, inGarden = 0, inGardenFree = 0;
            var occupiers = new Dictionary<string, int>();
            var blockers = new Dictionary<string, int>();
            var objectTree = _world.GetExistingSystemManaged<global::Game.Objects.SearchSystem>().GetStaticSearchTree(true, out var deps);
            deps.Complete();
            var areaTree = _world.GetExistingSystemManaged<global::Game.Areas.SearchSystem>().GetSearchTree(true, out var deps2);
            deps2.Complete();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var block = em.GetComponentData<Block>(e);
                    var cells = em.GetBuffer<Cell>(e, true);
                    for (int y = 0; y < block.m_Size.y; y++)
                        for (int x = 0; x < block.m_Size.x; x++)
                        {
                            var cell = cells[y * block.m_Size.x + x];
                            if (cell.m_Zone.m_Index == 0 || (cell.m_State & CellFlags.Visible) == 0) continue;
                            var p = ZoneUtils.GetCellPosition(block, new int2(x, y)).xz;
                            if (!In(p)) continue;
                            zoned++;
                            bool garden = gardens.Any(g => Poly.Contains(g.Item2, p));
                            if (garden) inGarden++;
                            if ((cell.m_State & CellFlags.Blocked) != 0)
                            {
                                blocked++;
                                var found = new NativeList<Entity>(8, Allocator.Temp);
                                var it = new AreaPointIterator { At = p, Results = found };
                                areaTree.Iterate(ref it);
                                string why = found.Length == 0 ? "a road or net" : string.Join("+", found.AsArray().ToArray().Distinct().Take(2).Select(a => NameOf(em.GetComponentData<PrefabRef>(a).m_Prefab)));
                                found.Dispose();
                                blockers[why] = (blockers.TryGetValue(why, out var c) ? c : 0) + 1;
                                continue;
                            }
                            if ((cell.m_State & CellFlags.Occupied) != 0)
                            {
                                if (lots.Any(l => Poly.Contains(l.Lot, p))) { byBuilding++; continue; }
                                occupiedOther++;
                                var found = new NativeList<Entity>(8, Allocator.Temp);
                                var it = new ObjectBoxIterator { Bounds = new Bounds2(p - 4f, p + 4f), Results = found };
                                objectTree.Iterate(ref it);
                                var names = new List<string>();
                                var near = new List<string>();
                                foreach (var o in found)
                                {
                                    if (!em.Exists(o) || !em.HasComponent<PrefabRef>(o)) continue;
                                    var op = em.GetComponentData<PrefabRef>(o).m_Prefab;
                                    string n = NameOf(op) + (em.HasComponent<PolishPiece>(o) ? " (Outgrow)" : "");
                                    bool sunk = em.HasComponent<global::Game.Objects.Elevation>(o) && em.GetComponentData<global::Game.Objects.Elevation>(o).m_Elevation < 0f;
                                    near.Add(n + (sunk ? " sunk" : ""));
                                    if (!em.HasComponent<ObjectGeometryData>(op) || (em.GetComponentData<ObjectGeometryData>(op).m_Flags & global::Game.Objects.GeometryFlags.OccupyZone) == 0 || sunk) continue;
                                    names.Add(n);
                                }
                                found.Dispose();
                                // nothing that takes cells there: what stands near it (a cell the game has not read again yet)
                                string why = names.Count == 0 ? "? near " + string.Join("/", near.Distinct().Take(3)) : string.Join("+", names.Distinct().Take(2));
                                occupiers[why] = (occupiers.TryGetValue(why, out var c) ? c : 0) + 1;
                                continue;
                            }
                            free++;
                            if (garden) inGardenFree++;
                        }
                }
            rec["zonedCells"] = zoned;
            rec["free"] = free;
            rec["occupiedByBuildings"] = byBuilding;
            rec["occupiedByOther"] = occupiedOther;
            rec["blocked"] = blocked;
            rec["inGardens"] = inGarden;
            rec["inGardensFree"] = inGardenFree;
            rec["growablesStanding"] = growables;
            rec["occupiers"] = JObject.FromObject(occupiers);
            rec["blockers"] = JObject.FromObject(blockers);
            Mod.Log.Info($"Placemaker lab: zone cells ({(string)step["label"] ?? "cells"}) over ({lo.x:F0},{lo.y:F0})-({hi.x:F0},{hi.y:F0}): {zoned} zoned, {free} free, {byBuilding} under buildings, {occupiedOther} occupied by something else "
                + $"({string.Join(", ", occupiers.Select(kv => kv.Value + " " + kv.Key))}), {blocked} blocked ({string.Join(", ", blockers.Select(kv => kv.Value + " " + kv.Key))}); {inGarden} inside Outgrow gardens ({inGardenFree} of them free); {growables} growables standing there");
        }

        /// <summary>{"op":"zoneSink","value":false}: Outgrow lays pieces that take zone cells on the ground (as builds before
        /// 22 Oct did); "value":true sinks them again; "old":true also sets the pieces standing a hair under the ground, as
        /// after a load (NeighbourPolishSystem.SinkOldPieces).</summary>
        private IEnumerator LabZoneSink(JObject step, JObject rec)
        {
            if (step["value"] != null) NeighbourObjectSystem.SinkZonePieces = (bool)step["value"];
            if ((bool?)step["old"] == true) _world.GetExistingSystemManaged<NeighbourPolishSystem>()?.SinkOldPiecesSoon();
            rec["sink"] = NeighbourObjectSystem.SinkZonePieces;
            yield return Frames(20);
        }

        private struct ObjectBoxIterator : Colossal.Collections.INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity e) { if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(e); }
        }

        /// <summary>Areas whose bounds hold a point (their triangles are not tested: what lies near it).</summary>
        private struct AreaPointIterator : Colossal.Collections.INativeQuadTreeIterator<AreaSearchItem, QuadTreeBoundsXZ>
        {
            public float2 At;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, At);
            public void Iterate(QuadTreeBoundsXZ bounds, AreaSearchItem item) { if (MathUtils.Intersect(bounds.m_Bounds.xz, At)) Results.Add(item.m_Area); }
        }
    }
}
