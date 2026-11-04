// Placemaker — what Block paint must not paint over, gathered around one city block.
//
// Read-only, bounded queries on the game's search trees (never a scan of the map), main thread only:
//   * building lots (8 m lot cells from BuildingData, like SiteCapture / SnapCollector),
//   * lot-type areas (other Placemaker areas, parks, extractor lots…) as their node polygons,
//   * zone grid cells: zoned ones (m_Zone != None) always, empty visible ones when the player asked
//     for it. Cells are merged into runs along the block depth to keep the polygon count low,
//   * water: a point predicate (WaterUtils depth) evaluated by Core at brush cell centres.
// Members of the group being edited are skipped, so its own lot does not block repainting it.

using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public sealed class BlockObstacles
    {
        /// <summary>Water deeper than this (m) cannot be painted.</summary>
        public const float MaxWaterDepth = 0.3f;
        private const int MaxObstacles = 4000;

        private readonly EntityManager _em;
        private readonly global::Game.Objects.SearchSystem _objectSearch;
        private readonly global::Game.Areas.SearchSystem _areaSearch;
        private readonly global::Game.Zones.SearchSystem _zoneSearch;
        private readonly WaterSystem _water;

        private readonly World _world;

        public BlockObstacles(EntityManager em, World world)
        {
            _em = em;
            _world = world;
            _objectSearch = world.GetOrCreateSystemManaged<global::Game.Objects.SearchSystem>();
            _areaSearch = world.GetOrCreateSystemManaged<global::Game.Areas.SearchSystem>();
            _zoneSearch = world.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
            _water = world.GetOrCreateSystemManaged<WaterSystem>();
        }

        private struct EntityIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(e);
            }
        }

        private struct AreaIterator : INativeQuadTreeIterator<global::Game.Areas.AreaSearchItem, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, global::Game.Areas.AreaSearchItem item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(item.m_Area);
            }
        }

        private struct ZoneIterator : INativeQuadTreeIterator<Entity, Bounds2>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(Bounds2 bounds) => MathUtils.Intersect(bounds, Bounds);
            public void Iterate(Bounds2 bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds, Bounds)) Results.Add(e);
            }
        }

        /// <summary>
        /// Obstacle polygons (XZ) touching the box [min, max], tagged by kind (the brush rules pick which count). ignoreGroup: Placemaker group whose members are not obstacles (0 = none).
        /// </summary>
        public List<BrushObstacle> Gather(float2 min, float2 max, int ignoreGroup, bool fresh = true)
        {
            // s49: toggling a brush rule / Smart edges re-filters the cached set instead of querying the ECS again.
            float now = UnityEngine.Time.realtimeSinceStartup;
            var cached = _cache.TryGet(min, max, ignoreGroup, now, fresh);
            if (cached != null) return cached;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Read a little more than asked so small moves of the box hit the cache next time.
            float2 gmin = min - 24f, gmax = max + 24f;
            var list = new List<BrushObstacle>();
            var bounds = new Bounds2(gmin, gmax);
            GatherInto(bounds, ignoreGroup, list);
            _cache.Store(gmin, gmax, ignoreGroup, now, list);
            sw.Stop();
            var result = _cache.TryGet(min, max, ignoreGroup, now, false) ?? list;
            LastStats = ObstacleStats.Of(result);
            LastGatherMs = (float)sw.Elapsed.TotalMilliseconds;
            Mod.Log.Info("Placemaker: obstacle set gathered in " + LastGatherMs.ToString("0.0") + " ms (box " + (int)(max.x - min.x) + "x" + (int)(max.y - min.y) + " m, ignoring group " + ignoreGroup + "): " + LastStats);
            return result;
        }

        private readonly ObstacleCache _cache = new ObstacleCache();
        /// <summary>Counts per kind of the last gathered set (debug line).</summary>
        public ObstacleStats LastStats { get; private set; }
        public float LastGatherMs { get; private set; }

        /// <summary>Forget the cached set (the city changed under it).</summary>
        public void Invalidate() => _cache.Clear();

        private void GatherInto(Bounds2 bounds, int ignoreGroup, List<BrushObstacle> list)
        {
            try { Buildings(bounds, ignoreGroup, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint could not read buildings: " + e.Message); }
            try { Lots(bounds, ignoreGroup, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint could not read areas: " + e.Message); }
            try { ZoneCells(bounds, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: block paint could not read zoning: " + e.Message); }
            try { BuildReceipts(bounds, ignoreGroup, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: could not read other builds: " + e.Message); }
        }

        /// <summary>
        /// Lot areas (parks, extractor lots…) and every Placemaker build's outline touching the box, as the brush's "Lots" and
        /// "My builds" rules see them (neighbourhood polish: gardens never grow into them). No log line, no cache.
        /// </summary>
        public List<BrushObstacle> LotsAndBuildsIn(float2 min, float2 max)
        {
            var list = new List<BrushObstacle>();
            var bounds = new Bounds2(min, max);
            // our own owner lots count too: a build just made has its lot before its saved outline
            try { Lots(bounds, 0, list, includeHelpers: true); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: could not read areas: " + e.Message); }
            try { BuildReceipts(bounds, 0, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: could not read other builds: " + e.Message); }
            return list;
        }

        /// <summary>(s15) Building lots / footprints touching the box only (plain Paint, the selection overlay).</summary>
        public List<float2[]> BuildingsIn(float2 min, float2 max, int ignoreGroup)
        {
            var list = new List<BrushObstacle>();
            try { Buildings(new Bounds2(min, max), ignoreGroup, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: could not read buildings: " + e.Message); }
            var rings = new List<float2[]>(list.Count);
            foreach (var o in list) rings.Add(o.Ring);
            return rings;
        }

        /// <summary>
        /// Predicate "under water" for points in the box, or null when the box is dry. Sampled once into
        /// a grid (the water surface data must not be held across frames).
        /// </summary>
        public Func<float2, bool> Water(float2 min, float2 max, float step, bool fresh = true)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!fresh && _waterCache != null && _waterStep == step && _waterMin.Equals(min) && _waterMax.Equals(max) && now - _waterAt < ObstacleCache.MaxAge && now >= _waterAt)
                return _waterCache;
            var wet = WaterRead(min, max, step);
            _waterCache = wet; _waterStep = step; _waterMin = min; _waterMax = max; _waterAt = now;
            return wet;
        }

        private Func<float2, bool> _waterCache;
        private float2 _waterMin, _waterMax;
        private float _waterStep, _waterAt;

        private Func<float2, bool> WaterRead(float2 min, float2 max, float step)
        {
            try
            {
                if (_water == null) return null;
                step = math.max(2f, step);
                int nx = (int)math.ceil((max.x - min.x) / step) + 1, nz = (int)math.ceil((max.y - min.y) / step) + 1;
                if ((long)nx * nz > 250_000) return null;
                var wet = new bool[nx * nz];
                var data = _water.GetSurfacesData(out var deps);
                deps.Complete();
                bool any = false;
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float d = WaterUtils.SampleDepth(ref data, new float3(min.x + (i + 0.5f) * step, 0f, min.y + (j + 0.5f) * step));
                        if (d > MaxWaterDepth) { wet[j * nx + i] = true; any = true; }
                    }
                if (!any) return null;
                return p =>
                {
                    int i = (int)math.floor((p.x - min.x) / step), j = (int)math.floor((p.y - min.y) / step);
                    return i >= 0 && j >= 0 && i < nx && j < nz && wet[j * nx + i];
                };
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: block paint water check failed (" + e.Message + "); assuming dry ground");
                return null;
            }
        }

        private static int GroupOf(Entity e)
        {
            try { return Services.Groups != null ? Services.Groups.GroupOf(e) : 0; }
            catch (Exception) { return 0; }
        }

        // Receipt outlines per group (decoded once; a rebuild replaces the group, so ids never change meaning).
        private readonly Dictionary<int, float2[]> _receiptRings = new Dictionary<int, float2[]>();

        /// <summary>
        /// (s27) Every Placemaker group's outline from its receipt — the footprint even when its owner lot is not
        /// found in the area search tree (old saves, a lot the game dropped). Same kind as the owner lot.
        /// </summary>
        private void BuildReceipts(Bounds2 bounds, int ignoreGroup, List<BrushObstacle> into)
        {
            var groups = Services.Groups;
            if (groups == null) return;
            var live = new HashSet<int>(groups.AllGroups);
            var gone = new List<int>();
            foreach (var id in _receiptRings.Keys) if (!live.Contains(id)) gone.Add(id);
            foreach (var id in gone) _receiptRings.Remove(id);
            foreach (var id in live)
            {
                if (id == ignoreGroup) continue;
                if (!_receiptRings.TryGetValue(id, out var ring))
                {
                    ring = null;
                    try
                    {
                        var r = groups.GetReceipt(id);
                        if (r?.Area?.Ring != null && r.Area.Ring.Length >= 3) ring = r.Area.Ring2D();
                    }
                    catch (Exception) { ring = null; }
                    _receiptRings[id] = ring;
                }
                if (ring == null) continue;
                Poly.Bounds(ring, out var mn, out var mx);
                if (mn.x > bounds.max.x || mn.y > bounds.max.y || mx.x < bounds.min.x || mx.y < bounds.min.y) continue;
                into.Add(new BrushObstacle(BrushObstacleKind.PlacemakerBuild, ring));
            }
        }

        private bool Skip(Entity e, int ignoreGroup)
        {
            if (_em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) return true;
            if (ignoreGroup != 0)
            {
                try { if (Services.Groups != null && Services.Groups.GroupOf(e) == ignoreGroup) return true; }
                catch (Exception) { }
            }
            return false;
        }

        private void Buildings(Bounds2 bounds, int ignoreGroup, List<BrushObstacle> into)
        {
            if (_objectSearch == null) return;
            var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                var it = new EntityIterator { Bounds = bounds, Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxObstacles; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e)) continue;
                    if (!BuildingFootprints.IsBuilding(_em, e) || Skip(e, ignoreGroup)) continue;
                    BuildingFootprints.Of(_em, e, out var lot, out var footprint);
                    // (s27) A building a Placemaker group placed (park companion…) belongs to that build.
                    var kind = GroupOf(e) != 0 ? BrushObstacleKind.PlacemakerBuild : BrushObstacleKind.Building;
                    if (lot != null) into.Add(new BrushObstacle(kind, lot));
                    if (footprint != null) into.Add(new BrushObstacle(kind, footprint));
                }
            }
            finally { results.Dispose(); }
        }

        private void Lots(Bounds2 bounds, int ignoreGroup, List<BrushObstacle> into, bool includeHelpers = false)
        {
            if (_areaSearch == null) return;
            var tree = _areaSearch.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(32, Allocator.Temp);
            try
            {
                var it = new AreaIterator { Bounds = bounds, Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxObstacles; i++)
                {
                    var e = results[i];
                    if (e == Entity.Null || !seen.Add(e)) continue;
                    if (!_em.HasComponent<global::Game.Areas.Lot>(e) || !_em.HasBuffer<global::Game.Areas.Node>(e) || Skip(e, ignoreGroup)) continue;
                    // (s57) Our helper lots (owner "Placemaker Area", "Placemaker Zone Block") are never obstacles: cell-shaped, they can poke
                    // past the build. The build counts through its saved outline (BuildReceipts) only.
                    if (!includeHelpers && IsHelperLot(e)) continue;
                    var nodes = _em.GetBuffer<global::Game.Areas.Node>(e, true);
                    if (nodes.Length < 3) continue;
                    var ring = new float2[nodes.Length];
                    for (int n = 0; n < nodes.Length; n++) ring[n] = nodes[n].m_Position.xz;
                    // (s27) Our owner lot "Placemaker Area" and the zone-block lots are members of their group: they are
                    // "other Placemaker builds", a rule of their own (they used to count as plain lots).
                    into.Add(new BrushObstacle(GroupOf(e) != 0 || IsHelperLot(e) ? BrushObstacleKind.PlacemakerBuild : BrushObstacleKind.Lot, ring));
                }
            }
            finally { results.Dispose(); }
        }

        private global::Placemaker.Game.Groups.OwnerAreaPrefabSystem _ownerPrefabs;

        /// <summary>(s49) Our invisible "Placemaker Zone Block" / "Placemaker Area" lots, even when the group registry does not know them (they are "My builds", never plain Lots).</summary>
        private bool IsHelperLot(Entity e)
        {
            try
            {
                _ownerPrefabs ??= _world.GetExistingSystemManaged<global::Placemaker.Game.Groups.OwnerAreaPrefabSystem>();
                if (_ownerPrefabs == null || !_em.HasComponent<PrefabRef>(e)) return false;
                var prefab = _em.GetComponentData<PrefabRef>(e).m_Prefab;
                return _ownerPrefabs.IsOwnerPrefab(prefab) || _ownerPrefabs.IsBlockerPrefab(prefab);
            }
            catch (Exception) { return false; }
        }

        private void ZoneCells(Bounds2 bounds, List<BrushObstacle> into)
        {
            if (_zoneSearch == null) return;
            var tree = _zoneSearch.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(32, Allocator.Temp);
            try
            {
                var it = new ZoneIterator { Bounds = bounds, Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxObstacles; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e)) continue;
                    if (!_em.HasComponent<global::Game.Zones.Block>(e) || !_em.HasBuffer<global::Game.Zones.Cell>(e)
                        || _em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) continue;
                    var block = _em.GetComponentData<global::Game.Zones.Block>(e);
                    var cells = _em.GetBuffer<global::Game.Zones.Cell>(e, true);
                    int2 size = block.m_Size;
                    if (size.x <= 0 || size.y <= 0 || cells.Length < size.x * size.y) continue;
                    float2 dir = block.m_Direction, right = MathUtils.Right(dir);
                    // Cell index y runs from the road (0) into the block; merge consecutive cells of a column.
                    for (int x = 0; x < size.x; x++)
                    {
                        // Runs of the same kind (zoned / empty); hidden or blocked cells end a run.
                        int start = -1, kind = 0;
                        for (int y = 0; y <= size.y; y++)
                        {
                            int k = y < size.y ? CellKind(cells[x + y * size.x]) : 0;
                            if (k != kind && start >= 0)
                            {
                                var a = global::Game.Zones.ZoneUtils.GetCellPosition(block, new int2(x, start)).xz;
                                var b = global::Game.Zones.ZoneUtils.GetCellPosition(block, new int2(x, y - 1)).xz;
                                // GetCellPosition steps against m_Direction as y grows: a is the road end.
                                float2 front = a + dir * 4f, back = b - dir * 4f;
                                into.Add(new BrushObstacle(kind == 1 ? BrushObstacleKind.ZonedCell : kind == 3 ? BrushObstacleKind.UnbuiltZonedCell : BrushObstacleKind.EmptyZoneCell,
                                    new[] { front + right * 4f, front - right * 4f, back - right * 4f, back + right * 4f }));
                                start = -1;
                            }
                            if (k != 0 && start < 0) start = y;
                            kind = k;
                        }
                    }
                }
            }
            finally { results.Dispose(); }
        }

        /// <summary>0 = not part of the visible grid, 1 = zoned and built on (Occupied), 2 = empty (zoneable), 3 = zoned but vacant.</summary>
        private static int CellKind(global::Game.Zones.Cell cell)
        {
            var s = cell.m_State;
            // Hidden or blocked cells (under a road, a lot, an area) are not part of the visible grid.
            if ((s & global::Game.Zones.CellFlags.Visible) == 0 || (s & global::Game.Zones.CellFlags.Blocked) != 0) return 0;
            if (cell.m_Zone.Equals(global::Game.Zones.ZoneType.None)) return 2;
            // s49: only cells with a building on them (or under construction) are "Zoned"; vacant zoned land is its own kind.
            return (s & global::Game.Zones.CellFlags.Occupied) != 0 ? 1 : 3;
        }
    }
}
