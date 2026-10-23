// Placemaker neighbourhood polish — the barriers around a pass, kept by map cell (GPL-3.0).
//
// Every pass needs the roads, paths, tracks, decks, other buildings' lots, lot areas and Placemaker builds around its houses
// (BarriersIn). Reading them from the game's search trees for every pass cost about 10 ms of the frame each time, and a town
// outgrown at once makes dozens of passes over the same streets. They are read once per 128 m cell and kept until something
// in or near the cell is built, removed or changed (a road, a building, a lot area); what can change without that (a house
// still being built, the houses the pass plans itself) is looked at again for each pass.

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core.Neighbours;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        private const float BarrierCellSize = 128f;

        /// <summary>One thing gardens keep off: its rings, what it is, which belong together, how high (decks), its bounds.</summary>
        private sealed class BarrierItem
        {
            public Entity E;
            public int Category;   // 0 roads, paths, tracks and decks; 1 buildings and pillars; 2 lot areas and builds
            public float2 Lo, Hi;
            public List<float2[]> Rings = new List<float2[]>();
            public List<int> Kinds = new List<int>();
            public List<long> Groups = new List<long>();
            public List<float> Heights = new List<float>();
            /// <summary>A house (a growable building): left out of a pass that plans it, unless it is still being built.</summary>
            public bool House;
            public int Stamp;
        }

        // every cell read: what lies in it (each thing in every cell its bounds touch)
        private readonly Dictionary<long, List<BarrierItem>> _barrierCells = new Dictionary<long, List<BarrierItem>>();
        // the roads and buildings read, by entity (a cell read again finds them as they are); lot areas and builds by outline
        private readonly Dictionary<Entity, BarrierItem> _barrierOf = new Dictionary<Entity, BarrierItem>();
        private readonly Dictionary<(int, float2, float2), BarrierItem> _lotBarrierOf = new Dictionary<(int, float2, float2), BarrierItem>();
        private int _barrierStamp;
        private EntityQuery _changedBarriers;

        private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        private void CreateBarrierCacheQuery()
        {
            // a road, lot area, surface or building the game changed (its geometry, a building finished): its cells read again
            _changedBarriers = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Updated>() },
                Any = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<global::Game.Areas.Lot>(), ComponentType.ReadOnly<global::Game.Areas.Surface>(), ComponentType.ReadOnly<global::Game.Buildings.Building>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PolishPiece>() },
            });
        }

        /// <summary>The cells something built, removed or changed this frame lies in are read again when next asked for.</summary>
        private void DropChangedBarrierCells()
        {
            if (_barrierCells.Count == 0) return;
            foreach (var query in new[] { _newNearby, _goneNearby, _changedBarriers })
            {
                if (query.IsEmptyIgnoreFilter) continue;
                using (var all = query.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                    {
                        // a house's own lawn, bed or path (ours among them) is no barrier: nothing to read again
                        if (EntityManager.HasComponent<Owner>(e))
                        {
                            var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                            if (owner != Entity.Null && EntityManager.HasComponent<global::Game.Buildings.Building>(owner) && IsGrowable(owner)) continue;
                        }
                        if (_barrierOf.TryGetValue(e, out var was)) { DropBarrierCells(was.Lo, was.Hi); _barrierOf.Remove(e); }
                        if (BoundsOf(e, out var lo, out var hi)) DropBarrierCells(lo, hi);
                    }
            }
        }

        /// <summary>Where an entity lies, roughly (a building: around its place, as far as a lot reaches).</summary>
        private bool BoundsOf(Entity e, out float2 lo, out float2 hi)
        {
            lo = hi = default;
            if (!EntityManager.Exists(e)) return false;
            if (EntityManager.HasComponent<global::Game.Buildings.Building>(e) && EntityManager.HasComponent<global::Game.Objects.Transform>(e))
            {
                var p = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                lo = p - 80f;
                hi = p + 80f;
                return true;
            }
            if (EntityManager.HasComponent<Curve>(e))
            {
                var b = MathUtils.Bounds(EntityManager.GetComponentData<Curve>(e).m_Bezier);
                lo = b.min.xz - 20f;
                hi = b.max.xz + 20f;
                return true;
            }
            if (EntityManager.HasBuffer<global::Game.Areas.Node>(e))
            {
                var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
                if (nodes.Length == 0) return false;
                lo = new float2(float.MaxValue);
                hi = new float2(float.MinValue);
                for (int k = 0; k < nodes.Length; k++) { lo = math.min(lo, nodes[k].m_Position.xz); hi = math.max(hi, nodes[k].m_Position.xz); }
                lo -= 1f;
                hi += 1f;
                return true;
            }
            return false;
        }

        private void DropBarrierCells(float2 lo, float2 hi)
        {
            int x0 = (int)math.floor(lo.x / BarrierCellSize), x1 = (int)math.floor(hi.x / BarrierCellSize), y0 = (int)math.floor(lo.y / BarrierCellSize), y1 = (int)math.floor(hi.y / BarrierCellSize);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 4096) { ForgetBarriers(); return; }
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    _barrierCells.Remove(CellKey(x, y));
        }

        /// <summary>Every cell read again when next asked for (another city, the polish switched on).</summary>
        private void ForgetBarriers()
        {
            _barrierCells.Clear();
            _barrierOf.Clear();
            _lotBarrierOf.Clear();
        }

        /// <summary>What lies in one cell, read from the game's search trees (the rules of the barriers as before).</summary>
        private List<BarrierItem> ReadBarrierCell(int cx, int cy)
        {
            var cell = new List<BarrierItem>();
            var min = new float2(cx, cy) * BarrierCellSize;
            var max = min + BarrierCellSize;
            _netSearch ??= World.GetExistingSystemManaged<global::Game.Net.SearchSystem>();
            _objectSearch ??= World.GetExistingSystemManaged<global::Game.Objects.SearchSystem>();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            var partClock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_netSearch != null)
                {
                    var tree = _netSearch.GetNetSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BoundsIterator { Bounds = new Bounds2(min, max), Results = results };
                    tree.Iterate(ref it);
                    var seen = new HashSet<Entity>();
                    for (int i = 0; i < results.Length; i++)
                    {
                        var e = results[i];
                        if (!seen.Add(e)) continue;
                        if (_barrierOf.TryGetValue(e, out var known)) { cell.Add(known); continue; }
                        if (!EntityManager.HasComponent<Edge>(e) || !EntityManager.HasComponent<Curve>(e) || !EntityManager.HasComponent<Composition>(e)) continue;
                        if (EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                        var comp = EntityManager.GetComponentData<Composition>(e).m_Edge;
                        if (comp == Entity.Null || !EntityManager.HasComponent<NetCompositionData>(comp)) continue;
                        var data = EntityManager.GetComponentData<NetCompositionData>(comp);
                        // a tunnel: the ground above it is open
                        if ((data.m_Flags.m_General & CompositionFlags.General.Tunnel) != 0 || data.m_Width < 1f) continue;
                        // walked, driven or ridden on: roads, paths, tracks (not pipes or power lines)
                        var prefab = EntityManager.GetComponentData<global::Game.Prefabs.PrefabRef>(e).m_Prefab;
                        bool ground = EntityManager.HasComponent<Road>(e) || EntityManager.HasComponent<global::Game.Prefabs.PathwayData>(prefab)
                            || EntityManager.HasComponent<TrainTrack>(e) || EntityManager.HasComponent<TramTrack>(e);
                        if (!ground) continue;
                        var item = new BarrierItem { E = e, Category = 0 };
                        if (tree.TryGet(e, out var tb)) { item.Lo = tb.m_Bounds.min.xz; item.Hi = tb.m_Bounds.max.xz; }
                        else { var b = MathUtils.Bounds(EntityManager.GetComponentData<Curve>(e).m_Bezier); item.Lo = b.min.xz; item.Hi = b.max.xz; }
                        // elevated (an overpass, a bridge): its deck in slices with their heights; else its ground as the game draws it
                        if ((data.m_Flags.m_General & CompositionFlags.General.Elevated) != 0)
                        {
                            var deck = DeckRings(e);
                            for (int k = 0; k < deck.Rings.Count; k++) { item.Rings.Add(deck.Rings[k]); item.Kinds.Add(YardStitch.DeckKind); item.Groups.Add(Id(e)); item.Heights.Add(deck.Heights[k]); }
                        }
                        else
                            foreach (var ring in RoadRings(e, data.m_Width)) { item.Rings.Add(ring); item.Kinds.Add(0); item.Groups.Add(Id(e)); item.Heights.Add(0f); }
                        _barrierOf[e] = item;
                        cell.Add(item);
                    }
                }
                results.Clear();
                BarrierPartMs[0] += partClock.Elapsed.TotalMilliseconds; partClock.Restart();
                if (_objectSearch != null)
                {
                    var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BuildingIterator
                    {
                        Bounds = new Bounds2(min, max), Results = results,
                        Buildings = GetComponentLookup<global::Game.Buildings.Building>(true), Extensions = GetComponentLookup<global::Game.Buildings.Extension>(true),
                        Pillars = GetComponentLookup<global::Game.Objects.Pillar>(true),
                    };
                    tree.Iterate(ref it);
                    var seen = new HashSet<Entity>();
                    for (int i = 0; i < results.Length; i++)
                    {
                        var e = results[i];
                        if (!seen.Add(e)) continue;
                        if (_barrierOf.TryGetValue(e, out var known)) { cell.Add(known); continue; }
                        if (EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                        var item = new BarrierItem { E = e, Category = 1 };
                        if (tree.TryGet(e, out var tb)) { item.Lo = tb.m_Bounds.min.xz; item.Hi = tb.m_Bounds.max.xz; }
                        // a pillar: nothing grows into it, whatever the deck above
                        if (EntityManager.HasComponent<global::Game.Objects.Pillar>(e))
                        {
                            var box = ObjectBox(e);
                            if (box == null) continue;
                            item.Rings.Add(box); item.Kinds.Add(YardStitch.DeckKind); item.Groups.Add(0L); item.Heights.Add(0f);
                        }
                        else
                        {
                            if (!BuildingFootprints.IsBuilding(EntityManager, e)) continue;
                            BuildingFootprints.Of(EntityManager, e, out var lot, out _);
                            if (lot == null || lot.Length < 3) continue;
                            // houses are the plan's own; a house still being built, or one the pass does not have, is its lot
                            item.House = IsGrowable(e);
                            item.Rings.Add(lot); item.Kinds.Add(1); item.Groups.Add(0L); item.Heights.Add(0f);
                        }
                        _barrierOf[e] = item;
                        cell.Add(item);
                    }
                }
            }
            finally { results.Dispose(); }
            BarrierPartMs[1] += partClock.Elapsed.TotalMilliseconds; partClock.Restart();
            // lot areas and Placemaker builds (their saved outlines): one item per outline however many cells it is read in
            try
            {
                _obstacles ??= new BlockObstacles(EntityManager, World);
                foreach (var o in _obstacles.LotsAndBuildsIn(min, max))
                {
                    if (o.Ring == null || o.Ring.Length < 3) continue;
                    var key = (o.Ring.Length, o.Ring[0], o.Ring[o.Ring.Length / 2]);
                    if (!_lotBarrierOf.TryGetValue(key, out var item))
                    {
                        item = new BarrierItem { E = Entity.Null, Category = 2 };
                        Placemaker.Core.Geometry.Poly.Bounds(o.Ring, out item.Lo, out item.Hi);
                        item.Rings.Add(o.Ring); item.Kinds.Add(1); item.Groups.Add(0L); item.Heights.Add(0f);
                        _lotBarrierOf[key] = item;
                    }
                    cell.Add(item);
                }
            }
            catch (System.Exception e) { if (_errors++ < 5) Mod.Log.Warn("Placemaker neighbourhood polish: could not read lots or builds: " + e.Message); }
            BarrierPartMs[2] += partClock.Elapsed.TotalMilliseconds;
            return cell;
        }

        /// <summary>The surfaces of the streets, paths and tracks on the ground in a box that belong to no building (a house's
        /// own driveway or a shop's car park lane lies in its lot), from the cells.</summary>
        private List<float2[]> StreetRingsIn(float2 min, float2 max)
        {
            var list = new List<float2[]>();
            int x0 = (int)math.floor(min.x / BarrierCellSize), x1 = (int)math.floor(max.x / BarrierCellSize), y0 = (int)math.floor(min.y / BarrierCellSize), y1 = (int)math.floor(max.y / BarrierCellSize);
            int stamp = ++_barrierStamp;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    long key = CellKey(x, y);
                    if (!_barrierCells.TryGetValue(key, out var cell)) _barrierCells[key] = cell = ReadBarrierCell(x, y);
                    foreach (var item in cell)
                    {
                        if (item.Category != 0 || item.Stamp == stamp) continue;
                        item.Stamp = stamp;
                        if (item.Hi.x < min.x || item.Lo.x > max.x || item.Hi.y < min.y || item.Lo.y > max.y) continue;
                        if (item.E == Entity.Null || !EntityManager.Exists(item.E) || EntityManager.HasComponent<Owner>(item.E)) continue;
                        for (int k = 0; k < item.Rings.Count; k++) if (item.Kinds[k] == 0) list.Add(item.Rings[k]);
                    }
                }
            return list;
        }

        /// <summary>The barriers in a box from the cells (read the first time they are asked for): roads, paths, tracks and decks
        /// first, then buildings and pillars, then lot areas and builds, each in the cells' order. A house the pass plans is
        /// left out (unless it is still being built); groups of one get numbers no entity has, as before.</summary>
        private List<float2[]> CachedBarriersIn(float2 min, float2 max, List<int> kinds, List<long> groups, HashSet<Entity> planned, List<float> heights)
        {
            var list = new List<float2[]>();
            int x0 = (int)math.floor(min.x / BarrierCellSize), x1 = (int)math.floor(max.x / BarrierCellSize), y0 = (int)math.floor(min.y / BarrierCellSize), y1 = (int)math.floor(max.y / BarrierCellSize);
            var cells = new List<List<BarrierItem>>();
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    long key = CellKey(x, y);
                    if (!_barrierCells.TryGetValue(key, out var cell)) _barrierCells[key] = cell = ReadBarrierCell(x, y);
                    cells.Add(cell);
                }
            long single = long.MinValue;
            for (int category = 0; category < 3; category++)
            {
                int stamp = ++_barrierStamp;
                foreach (var cell in cells)
                    foreach (var item in cell)
                    {
                        if (item.Category != category || item.Stamp == stamp) continue;
                        item.Stamp = stamp;
                        if (item.Hi.x < min.x || item.Lo.x > max.x || item.Hi.y < min.y || item.Lo.y > max.y) continue;
                        if (item.House && (planned == null || planned.Contains(item.E)) && !EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(item.E)) continue;
                        for (int k = 0; k < item.Rings.Count && list.Count < MaxBarriers; k++)
                        {
                            list.Add(item.Rings[k]);
                            kinds?.Add(item.Kinds[k]);
                            // a road's pieces belong together (its entity); everything else is a group of its own
                            groups?.Add(category == 0 ? item.Groups[k] : single++);
                            heights?.Add(item.Heights[k]);
                        }
                    }
            }
            if (list.Count >= MaxBarriers && s_barrierCapLogged++ < 5)
                Mod.Log.Warn($"Placemaker neighbourhood polish: more than {MaxBarriers} barriers around a pass ({max.x - min.x:F0} x {max.y - min.y:F0} m): the rest left out");
            return list;
        }
    }
}
