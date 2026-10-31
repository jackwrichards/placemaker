// Placemaker — world snap targets for draw mode (curbs, building sides, other areas, zone grid).
//
// Port of ParkingLotTool's ParkingLotSnapping.Targets.cs (KruemelGames, GPL-3.0). Scoring lives in
// Core (SnapResolver); this only reads the game's search trees around the cursor — bounded
// queries, never a scan of the whole map.

using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public sealed class SnapCollector
    {
        private const float RoadSearchRadius = 48f;

        private readonly EntityManager _em;
        private readonly global::Game.Net.SearchSystem _netSearch;
        private readonly global::Game.Objects.SearchSystem _objectSearch;
        private readonly global::Game.Areas.SearchSystem _areaSearch;
        private readonly global::Game.Zones.SearchSystem _zoneSearch;
        private readonly TerrainSystem _terrain;
        private readonly HashSet<Entity> _seen = new HashSet<Entity>();

        public SnapCollector(EntityManager em, World world)
        {
            _em = em;
            _netSearch = world.GetOrCreateSystemManaged<global::Game.Net.SearchSystem>();
            _objectSearch = world.GetOrCreateSystemManaged<global::Game.Objects.SearchSystem>();
            _areaSearch = world.GetOrCreateSystemManaged<global::Game.Areas.SearchSystem>();
            _zoneSearch = world.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
            _terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
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

        /// <summary>Offers every world target within reach of the resolver's raw point.</summary>
        public void Collect(SnapResolver r, Snap mask)
        {
            if ((mask & Snap.NetSide) != 0) RoadEdges(r);
            if ((mask & Snap.ObjectSide) != 0) ObjectSides(r);
            if ((mask & Snap.ExistingGeometry) != 0) AreaEdges(r);
            if ((mask & Snap.ZoneGrid) != 0) ZoneGrid(r);
        }

        private float Height(float2 p)
        {
            var data = _terrain.GetHeightData();
            return TerrainUtils.SampleHeight(ref data, new float3(p.x, 0, p.y));
        }

        private void RoadEdges(SnapResolver r)
        {
            if (_netSearch == null) return;
            var raw = r.Raw;
            var tree = _netSearch.GetNetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                var it = new EntityIterator { Bounds = new Bounds2(raw.xz - RoadSearchRadius, raw.xz + RoadSearchRadius), Results = results };
                tree.Iterate(ref it);
                _seen.Clear();
                for (int i = 0; i < results.Length; i++)
                {
                    var e = results[i];
                    if (!_seen.Add(e)) continue;
                    if (!_em.HasComponent<Edge>(e) || !_em.HasComponent<Road>(e) || _em.HasComponent<Owner>(e)
                        || _em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) continue;
                    var comp = _em.HasComponent<Composition>(e) ? _em.GetComponentData<Composition>(e) : default;
                    if (Snappable(comp.m_Edge) && _em.HasComponent<EdgeGeometry>(e))
                    {
                        var g = _em.GetComponentData<EdgeGeometry>(e);
                        Curve(r, g.m_Start.m_Left); Curve(r, g.m_Start.m_Right);
                        Curve(r, g.m_End.m_Left); Curve(r, g.m_End.m_Right);
                    }
                    if (_em.HasComponent<StartNodeGeometry>(e) && Snappable(comp.m_StartNode))
                        NodeGeometry(r, _em.GetComponentData<StartNodeGeometry>(e).m_Geometry);
                    if (_em.HasComponent<EndNodeGeometry>(e) && Snappable(comp.m_EndNode))
                        NodeGeometry(r, _em.GetComponentData<EndNodeGeometry>(e).m_Geometry);
                }
            }
            finally { results.Dispose(); }
        }

        // Tunnels are not curbs the player can see.
        private bool Snappable(Entity composition) =>
            composition == Entity.Null || !_em.HasComponent<NetCompositionData>(composition)
            || (_em.GetComponentData<NetCompositionData>(composition).m_Flags.m_General & CompositionFlags.General.Tunnel) == 0;

        private static void Curve(SnapResolver r, Bezier4x3 c)
        {
            var raw = r.Raw;
            if (!math.all(math.isfinite(c.a)) || !math.all(math.isfinite(c.d)) || math.distancesq(c.a, c.d) < 0.01f) return;
            if (MathUtils.Distance(c.xz, raw.xz, out float t) >= r.SnapDistance) return;
            var dir = math.normalizesafe(MathUtils.Tangent(c, t).xz);
            if (math.lengthsq(dir) < 0.5f) return;
            r.Offer(SnapResolver.LevelNet, SnapKind.RoadEdge, MathUtils.Position(c, t), dir);
        }

        // Intersection corners: PLT snaps to the straight continuation of the node's curb curves.
        private static void NodeCurve(SnapResolver r, Bezier4x3 c)
        {
            var raw = r.Raw;
            var dir = MathUtils.StartTangent(c);
            dir = MathUtils.Normalize(dir, dir.xz);
            dir.y = math.clamp(dir.y, -1f, 1f);
            var end = c.a + dir * math.dot(c.d - c.a, dir);
            if (!math.all(math.isfinite(end)) || math.distancesq(c.a, end) < 0.01f) return;
            var line = new Line3.Segment(c.a, end);
            if (MathUtils.Distance(line.xz, raw.xz, out float t) >= r.SnapDistance) return;
            var axis = math.normalizesafe(dir.xz);
            if (math.lengthsq(axis) < 0.5f) return;
            r.Offer(SnapResolver.LevelNet, SnapKind.RoadEdge, MathUtils.Position(line, t), axis);
        }

        private static void NodeGeometry(SnapResolver r, EdgeNodeGeometry g)
        {
            NodeCurve(r, g.m_Left.m_Left);
            NodeCurve(r, g.m_Right.m_Right);
            if (g.m_MiddleRadius > 0f)
            {
                NodeCurve(r, g.m_Left.m_Right);
                NodeCurve(r, g.m_Right.m_Left);
            }
        }

        private void AreaEdges(SnapResolver r)
        {
            if (_areaSearch == null) return;
            var raw = r.Raw;
            var tree = _areaSearch.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                var it = new AreaIterator { Bounds = new Bounds2(raw.xz - r.SnapDistance, raw.xz + r.SnapDistance), Results = results };
                tree.Iterate(ref it);
                _seen.Clear();
                for (int i = 0; i < results.Length; i++)
                {
                    var e = results[i];
                    if (e == Entity.Null || !_seen.Add(e)) continue;
                    if (!_em.HasComponent<global::Game.Areas.Lot>(e) || _em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) continue;
                    if (!_em.HasBuffer<global::Game.Areas.Node>(e)) continue;
                    var nodes = _em.GetBuffer<global::Game.Areas.Node>(e, true);
                    if (nodes.Length < 3) continue;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var from = nodes[n].m_Position;
                        var to = nodes[(n + 1) % nodes.Length].m_Position;
                        var line = new Line3.Segment(from, to);
                        if (MathUtils.Distance(line.xz, raw.xz, out float t) >= r.SnapDistance) continue;
                        var dir = math.normalizesafe(to.xz - from.xz);
                        if (math.lengthsq(dir) < 0.5f) continue;
                        float toStart = math.distance(from.xz, raw.xz), toEnd = math.distance(to.xz, raw.xz);
                        float3 pos = toStart <= r.SnapDistance && toStart <= toEnd ? from
                                   : toEnd <= r.SnapDistance ? to
                                   : MathUtils.Position(line, t);
                        r.Offer(SnapResolver.LevelArea, SnapKind.AreaEdge, pos, dir);
                    }
                }
            }
            finally { results.Dispose(); }
        }

        private void ObjectSides(SnapResolver r)
        {
            if (_objectSearch == null) return;
            var raw = r.Raw;
            var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                var it = new EntityIterator { Bounds = new Bounds2(raw.xz - r.SnapDistance, raw.xz + r.SnapDistance), Results = results };
                tree.Iterate(ref it);
                _seen.Clear();
                for (int i = 0; i < results.Length; i++)
                {
                    var e = results[i];
                    if (!_seen.Add(e)) continue;
                    if (!_em.HasComponent<global::Game.Objects.Transform>(e) || !_em.HasComponent<PrefabRef>(e) || _em.HasComponent<Owner>(e)
                        || _em.HasComponent<Temp>(e) || _em.HasComponent<Deleted>(e)) continue;
                    var prefab = _em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!_em.HasComponent<ObjectGeometryData>(prefab)) continue;
                    var geometry = _em.GetComponentData<ObjectGeometryData>(prefab);
                    if ((geometry.m_Flags & global::Game.Objects.GeometryFlags.Circular) != 0) continue;
                    var bounds = geometry.m_Bounds;
                    if (_em.HasComponent<BuildingData>(prefab))
                    {
                        // Buildings snap to their lot (8 m cells), not the mesh: that is what lines up with zoning.
                        var lot = (float2)_em.GetComponentData<BuildingData>(prefab).m_LotSize;
                        bounds.min.xz = lot * -4f;
                        bounds.max.xz = lot * 4f;
                    }
                    var tr = _em.GetComponentData<global::Game.Objects.Transform>(e);
                    var q = global::Game.Objects.ObjectUtils.CalculateBaseCorners(tr.m_Position, tr.m_Rotation, bounds);
                    Side(r, q.a, q.b); Side(r, q.b, q.c); Side(r, q.c, q.d); Side(r, q.d, q.a);
                }
            }
            finally { results.Dispose(); }
        }

        private void Side(SnapResolver r, float3 a, float3 b)
        {
            var raw = r.Raw;
            var line = new Line2(a.xz, b.xz);
            if (math.distancesq(line.a, line.b) < 0.01f) return;
            if (MathUtils.Distance(line, raw.xz, out float t) >= r.SnapDistance) return;
            var dir = math.normalizesafe(line.b - line.a);
            if (math.lengthsq(dir) < 0.5f) return;
            var flat = MathUtils.Position(line, t);
            r.Offer(SnapResolver.LevelObject, SnapKind.ObjectSide, new float3(flat.x, Height(flat), flat.y), dir);
        }

        /// <summary>
        /// (P2-1) The zone grid's snap level and reach. Default: a visible cell under the cursor, below roads and areas. With the
        /// rows on the zone grid (parking "Zone grid"), the tool sets the level of roads and a reach of 64 m: away from any
        /// zoned cell a corner still snaps to the extended grid of the nearest street's zone block.
        /// </summary>
        public float ZoneLevel = SnapResolver.LevelZoneGrid, ZoneReach;

        private void ZoneGrid(SnapResolver r)
        {
            if (_zoneSearch == null) return;
            var raw = r.Raw;
            float radius = math.max(8f * 1.4142136f + r.SnapDistance, ZoneReach);
            float visibleRadius = 8f * 1.4142136f + r.SnapDistance;
            float farDistance = float.MaxValue;
            float3 farPos = float3.zero;
            float2 farDir = float2.zero;
            var tree = _zoneSearch.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(8, Allocator.Temp);
            float bestDistance = radius;
            float3 cellPos = float3.zero;
            float2 cellDir = float2.zero;
            try
            {
                var it = new ZoneIterator { Bounds = new Bounds2(raw.xz - radius, raw.xz + radius), Results = results };
                tree.Iterate(ref it);
                for (int i = 0; i < results.Length; i++)
                {
                    var e = results[i];
                    if (!_em.HasComponent<global::Game.Zones.Block>(e) || !_em.HasBuffer<global::Game.Zones.Cell>(e)) continue;
                    var block = _em.GetComponentData<global::Game.Zones.Block>(e);
                    var cells = _em.GetBuffer<global::Game.Zones.Cell>(e, true);
                    var index = math.clamp(global::Game.Zones.ZoneUtils.GetCellIndex(block, raw.xz), 0, block.m_Size - 1);
                    if (index.x < 0 || index.y < 0 || index.x >= block.m_Size.x || index.y >= block.m_Size.y) continue;
                    var cell = cells[index.x + index.y * block.m_Size.x];
                    var pos = global::Game.Zones.ZoneUtils.GetCellPosition(block, index);
                    float d = math.distance(pos.xz, raw.xz);
                    if (ZoneReach > 0 && d < farDistance && d < ZoneReach) { farDistance = d; farPos = pos; farDir = block.m_Direction; }
                    if ((cell.m_State & global::Game.Zones.CellFlags.Visible) == 0) continue;
                    if (d >= bestDistance || d >= visibleRadius) continue;
                    bestDistance = d;
                    cellPos = pos;
                    cellDir = block.m_Direction;
                }
            }
            finally { results.Dispose(); }
            if (math.lengthsq(cellDir) < 0.5f) { cellPos = farPos; cellDir = farDir; }
            if (math.lengthsq(cellDir) < 0.5f) return;
            var across = MathUtils.Right(cellDir);
            var delta = raw.xz - cellPos.xz;
            // Cell centres sit 4 m off the cell edges: snap to the 8 m grid lines.
            float along = MathUtils.Snap(math.dot(delta, cellDir), 8f, 4f);
            float side = MathUtils.Snap(math.dot(delta, across), 8f, 4f);
            var snapped = cellPos.xz + cellDir * along + across * side;
            var world = new float3(snapped.x, Height(snapped), snapped.y);
            r.Offer(ZoneLevel, SnapKind.ZoneGrid, world, cellDir, true);
            r.Offer(ZoneLevel, SnapKind.ZoneGrid, world, across, true);
        }
    }
}
