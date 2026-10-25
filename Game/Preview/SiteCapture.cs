// Placemaker — main-thread snapshot of the world around the selected area.
//
// Generators run on a worker thread (PreviewWorker) and must never touch ECS or the terrain
// height data (both main-thread only). This copies what they need into plain arrays: a height
// grid (idea from Grid Road Generator's HeightSnapshot), a coarser water-depth grid, nearby nets
// as Bézier curves with widths, building lots as obstacles, and the asset info map. The result
// (SiteContext) is immutable after capture and therefore safe to read from any thread.

using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Preview
{
    public sealed class SiteCapture
    {
        /// <summary>Margin around the area: nets and obstacles this close still matter (entrances, frontage).</summary>
        public const float Margin = 60f;
        private const int MaxGrid = 256;
        private const int MaxNets = 600;
        private const int MaxObstacles = 6000;   // s15: every building near the area must be protected
        private const int MaxZoneBlocks = 4000;

        private readonly EntityManager _em;
        private readonly TerrainSystem _terrain;
        private readonly WaterSystem _water;
        private readonly global::Game.Net.SearchSystem _netSearch;
        private readonly global::Game.Objects.SearchSystem _objectSearch;
        private readonly global::Game.Zones.SearchSystem _zoneSearch;
        private readonly CityConfigurationSystem _cityConfig;
        private readonly World _world;

        private Dictionary<string, AssetInfo> _assets = new Dictionary<string, AssetInfo>();
        private int _assetsVersion = -1;
        private bool _assetsReady;

        public SiteCapture(EntityManager em, World world)
        {
            _em = em;
            _terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
            _water = world.GetOrCreateSystemManaged<WaterSystem>();
            _netSearch = world.GetOrCreateSystemManaged<global::Game.Net.SearchSystem>();
            _objectSearch = world.GetOrCreateSystemManaged<global::Game.Objects.SearchSystem>();
            _zoneSearch = world.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
            _cityConfig = world.GetOrCreateSystemManaged<CityConfigurationSystem>();
            _world = world;
        }

        /// <summary>True once the asset catalog delivered its info map (prices, footprints).</summary>
        public bool AssetsReady => _assetsReady;

        /// <summary>Asset info from the catalog, refreshed when its version changes. Never null.</summary>
        public Dictionary<string, AssetInfo> Assets()
        {
            var cat = Services.Assets;
            if (cat == null) { _assetsReady = false; return _assets; }
            try
            {
                if (cat.Ready && (cat.Version != _assetsVersion || !_assetsReady))
                {
                    _assets = cat.InfoMap() ?? new Dictionary<string, AssetInfo>();
                    _assetsVersion = cat.Version;
                    _assetsReady = true;
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: asset catalog InfoMap failed: " + e.Message);
            }
            return _assets;
        }

        public float SampleHeight(float2 p)
        {
            var data = _terrain.GetHeightData();
            return TerrainUtils.SampleHeight(ref data, new float3(p.x, 0, p.y));
        }

        public SiteContext Capture(AreaShape shape)
        {
            var site = new SiteContext();
            if (shape?.Ring == null || shape.Ring.Length < 3) return site;
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            foreach (var p in shape.Ring) { min = math.min(min, p.xz); max = math.max(max, p.xz); }
            min -= Margin;
            max += Margin;

            site.HeightAt = HeightGrid(min, max);
            site.WaterDepthAt = WaterGrid(min, max);
            CaptureNets(min, max, site.NearbyNets);
            CaptureObstacles(min, max, site.Obstacles);
            CaptureZoneBlocks(min, max, site.ZoneGrids);
            try { site.LeftHandTraffic = _cityConfig != null && _cityConfig.leftHandTraffic; } catch (Exception) { }
            // Copy: the worker must not see the catalog's dictionary mutate under it.
            site.Assets = AssetsCopy();
            // s33: the map's terrain paint materials (planners remap their paint intents to these slots).
            site.Terrain = TerrainPaletteReader.Current(_world);
            return site;
        }

        // (perf) The planners only read it: one copy per catalog version, shared by every capture (each copied the whole catalog).
        private Dictionary<string, AssetInfo> _assetsCopy;
        private int _assetsCopyVersion = -1;

        private Dictionary<string, AssetInfo> AssetsCopy()
        {
            var live = Assets();
            if (_assetsCopy == null || _assetsCopyVersion != _assetsVersion || !_assetsReady)
            {
                _assetsCopy = new Dictionary<string, AssetInfo>(live);
                _assetsCopyVersion = _assetsReady ? _assetsVersion : -1;
            }
            return _assetsCopy;
        }

        /// <summary>Nets around a ring (for attachments of drawn/painted outlines) without the full capture.</summary>
        public List<NearbyNet> NetsAround(float3[] ring, float margin = 20f)
        {
            var list = new List<NearbyNet>();
            if (ring == null || ring.Length == 0) return list;
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            foreach (var p in ring) { min = math.min(min, p.xz); max = math.max(max, p.xz); }
            min -= margin; max += margin;
            // s15: one outline rebuild asks for the same box up to three times (attachments, keep-off-roads,
            // marker follow); each query walks the net tree and reads lane buffers. Reuse within a frame.
            int frame = UnityEngine.Time.frameCount;
            if (_netsCache != null && frame == _netsFrame && math.all(min == _netsMin) && math.all(max == _netsMax))
                return new List<NearbyNet>(_netsCache);
            CaptureNets(min, max, list);
            _netsCache = new List<NearbyNet>(list);
            _netsFrame = frame;
            _netsMin = min;
            _netsMax = max;
            return list;
        }

        private List<NearbyNet> _netsCache;
        private int _netsFrame = -1;
        private float2 _netsMin, _netsMax;

        private Func<float2, float> HeightGrid(float2 min, float2 max)
        {
            float cell = math.max(4f, math.cmax(max - min) / MaxGrid);
            int nx = (int)math.ceil((max.x - min.x) / cell) + 1, nz = (int)math.ceil((max.y - min.y) / cell) + 1;
            var heights = new float[nx * nz];
            var data = _terrain.GetHeightData();
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    heights[j * nx + i] = TerrainUtils.SampleHeight(ref data, new float3(min.x + i * cell, 0f, min.y + j * cell));
            return Bilinear(heights, nx, nz, min, cell);
        }

        private Func<float2, float> WaterGrid(float2 min, float2 max)
        {
            try
            {
                // s20: 8 m (was 16 m): the waterline is read from this grid; at 16 m bilinear depth put it up to a cell off.
                float cell = math.max(8f, math.cmax(max - min) / (MaxGrid * 3 / 4));
                int nx = (int)math.ceil((max.x - min.x) / cell) + 1, nz = (int)math.ceil((max.y - min.y) / cell) + 1;
                var depth = new float[nx * nz];
                var data = _water.GetSurfacesData(out var deps);
                deps.Complete();
                bool any = false;
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float d = WaterUtils.SampleDepth(ref data, new float3(min.x + i * cell, 0f, min.y + j * cell));
                        depth[j * nx + i] = d;
                        any |= d > 0.01f;
                    }
                if (!any) return _ => 0f;
                return Bilinear(depth, nx, nz, min, cell);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: water snapshot failed (" + e.Message + "); assuming dry ground");
                return _ => 0f;
            }
        }

        private static Func<float2, float> Bilinear(float[] v, int nx, int nz, float2 origin, float cell)
        {
            return q =>
            {
                float fx = math.clamp((q.x - origin.x) / cell, 0f, nx - 1.001f), fz = math.clamp((q.y - origin.y) / cell, 0f, nz - 1.001f);
                int i = (int)fx, j = (int)fz;
                float tx = fx - i, tz = fz - j;
                int i1 = math.min(i + 1, nx - 1), j1 = math.min(j + 1, nz - 1);
                float a = v[j * nx + i], b = v[j * nx + i1], c = v[j1 * nx + i], d = v[j1 * nx + i1];
                return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), tz);
            };
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

        private void CaptureNets(float2 min, float2 max, List<NearbyNet> into)
        {
            if (_netSearch == null) return;
            var tree = _netSearch.GetNetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                var it = new EntityIterator { Bounds = new Bounds2(min, max), Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxNets; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e) || !_em.HasComponent<Edge>(e) || !_em.HasComponent<Curve>(e)) continue;
                    if (_em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) continue;
                    var c = _em.GetComponentData<Curve>(e).m_Bezier;
                    var net = new NearbyNet
                    {
                        ExternalId = EntityIds.Pack(e),
                        A = c.a, B = c.b, C = c.c, D = c.d,
                        Width = 8f,
                        Category = NetCategory.Other,
                    };
                    if (_em.HasComponent<Composition>(e))
                    {
                        var comp = _em.GetComponentData<Composition>(e).m_Edge;
                        if (comp != Entity.Null && _em.HasComponent<NetCompositionData>(comp))
                        {
                            var data = _em.GetComponentData<NetCompositionData>(comp);
                            net.Width = data.m_Width;
                            net.HasSidewalk = ((data.m_Flags.m_Left | data.m_Flags.m_Right) & (CompositionFlags.Side.Sidewalk | CompositionFlags.Side.WideSidewalk)) != 0;
                            // Elevated or tunnelled nets do not touch the ground area.
                            if ((data.m_Flags.m_General & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) != 0) continue;
                        }
                    }
                    var prefab = _em.HasComponent<PrefabRef>(e) ? _em.GetComponentData<PrefabRef>(e).m_Prefab : Entity.Null;
                    if (_em.HasComponent<Road>(e))
                    {
                        net.Category = NetCategory.Road;
                        if (prefab != Entity.Null && _em.HasComponent<RoadData>(prefab))
                        {
                            var road = _em.GetComponentData<RoadData>(prefab);
                            var flags = road.m_Flags;
                            net.IsOneWay = (flags & (global::Game.Prefabs.RoadFlags.DefaultIsForward | global::Game.Prefabs.RoadFlags.DefaultIsBackward)) != 0;
                            // Highways and their ramps: the roads type never links to them (RoadConnections.Connectable).
                            net.IsHighway = (flags & global::Game.Prefabs.RoadFlags.UseHighwayRules) != 0;
                            net.SpeedLimit = math.isfinite(road.m_SpeedLimit) ? road.m_SpeedLimit * 3.6f : 0f;   // stored in m/s
                        }
                    }
                    else if (prefab != Entity.Null && _em.HasComponent<PathwayData>(prefab)) net.Category = NetCategory.Pathway;
                    else if (_em.HasComponent<TrainTrack>(e) || _em.HasComponent<TramTrack>(e) || _em.HasComponent<SubwayTrack>(e)) net.Category = NetCategory.Track;
                    if (net.Category != NetCategory.Track) net.HasBikeLane = HasBikeLane(e);
                    into.Add(net);
                }
            }
            finally { results.Dispose(); }
        }

        /// <summary>
        /// A bike-only lane (bike path, road bike lane) or a pedestrian lane that allows bicycles
        /// (pathway with bike lanes): park entrances facing it become bike entrances.
        /// </summary>
        private bool HasBikeLane(Entity edge)
        {
            if (!_em.HasBuffer<global::Game.Net.SubLane>(edge)) return false;
            var lanes = _em.GetBuffer<global::Game.Net.SubLane>(edge, true);
            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i].m_SubLane;
                if (lane == Entity.Null) continue;
                if (_em.HasComponent<global::Game.Net.PedestrianLane>(lane) &&
                    (_em.GetComponentData<global::Game.Net.PedestrianLane>(lane).m_Flags & global::Game.Net.PedestrianLaneFlags.AllowBicycle) != 0)
                    return true;
                if (!_em.HasComponent<PrefabRef>(lane)) continue;
                var lp = _em.GetComponentData<PrefabRef>(lane).m_Prefab;
                if (lp != Entity.Null && _em.HasComponent<NetLaneData>(lp) &&
                    (_em.GetComponentData<NetLaneData>(lp).m_Flags & global::Game.Prefabs.LaneFlags.BicyclesOnly) != 0)
                    return true;
            }
            return false;
        }

        private struct BlockIterator : INativeQuadTreeIterator<Entity, Bounds2>
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
        /// (s19) Zone blocks of existing roads around the area (Game.Zones.Block): ZoneGuard snaps the build's zoning
        /// to their cells and blocks the others under the build. Geometry only (cells are 8 m, see Core ZoneGuard).
        /// </summary>
        private void CaptureZoneBlocks(float2 min, float2 max, List<ZoneGrid> into) => CaptureZoneBlocks(_em, _zoneSearch, min, max, into);

        /// <summary>(s26) Same capture for the build (live blocks after Apply: split edges have new blocks).</summary>
        public static void CaptureZoneBlocks(EntityManager _em, global::Game.Zones.SearchSystem _zoneSearch, float2 min, float2 max, List<ZoneGrid> into)
        {
            if (_zoneSearch == null) return;
            var tree = _zoneSearch.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                var it = new BlockIterator { Bounds = new Bounds2(min, max), Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxZoneBlocks; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e) || !_em.HasComponent<global::Game.Zones.Block>(e)) continue;
                    if (_em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) continue;
                    var b = _em.GetComponentData<global::Game.Zones.Block>(e);
                    // (s22) Per-cell state: hidden cells (overlapping blocks on curves) and cells under roads must
                    // never be zoned or drawn (they put a cutout across Juniper Street in playtest s22).
                    byte[] cells = null;
                    if (_em.HasBuffer<global::Game.Zones.Cell>(e))
                    {
                        var buf = _em.GetBuffer<global::Game.Zones.Cell>(e, true);
                        int n = b.m_Size.x * b.m_Size.y;
                        if (n > 0 && buf.Length >= n)
                        {
                            cells = new byte[n];
                            for (int c = 0; c < n; c++)
                            {
                                var s = buf[c].m_State;
                                byte f = 0;
                                if ((s & global::Game.Zones.CellFlags.Visible) != 0) f |= ZoneGrid.CellVisible;
                                if ((s & global::Game.Zones.CellFlags.Blocked) != 0) f |= ZoneGrid.CellBlocked;
                                cells[c] = f;
                            }
                        }
                    }
                    bool ours = false;
                    if (_em.HasComponent<global::Game.Common.Owner>(e))
                    {
                        var owner = _em.GetComponentData<global::Game.Common.Owner>(e).m_Owner;
                        ours = owner != Entity.Null && _em.Exists(owner) && _em.HasComponent<Placemaker.Game.Groups.PlacemakerMember>(owner);
                    }
                    into.Add(new ZoneGrid { Position = b.m_Position.xz, Direction = b.m_Direction, Size = b.m_Size, Cells = cells, Ours = ours });
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Placemaker: zone block capture failed: " + ex.Message);
            }
            finally { results.Dispose(); }
        }

        private void CaptureObstacles(float2 min, float2 max, List<Obstacle> into)
        {
            if (_objectSearch == null) return;
            var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                var it = new EntityIterator { Bounds = new Bounds2(min, max), Results = results };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length && into.Count < MaxObstacles; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e)) continue;
                    // (s15) Lots AND, where larger, collision footprints of buildings and extensions: the build
                    // safety pass (Core/Build/SiteSafety) keeps everything off them.
                    if (!BuildingFootprints.IsBuilding(_em, e)) continue;
                    BuildingFootprints.Of(_em, e, out var lot, out var footprint);
                    if (lot != null) into.Add(new Obstacle { A = lot[0], B = lot[1], C = lot[2], D = lot[3], Kind = ObstacleKind.Building });
                    if (footprint != null) into.Add(new Obstacle { A = footprint[0], B = footprint[1], C = footprint[2], D = footprint[3], Kind = ObstacleKind.Building });
                }
            }
            finally { results.Dispose(); }
        }
    }
}
