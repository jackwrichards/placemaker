// Points (Draw mode), area overhaul (2026-10-17). The user: "road nodes is fine, but I think road nodes should kind of
// be like points ... I think points should just snap to road nodes as well, so we can get rid of the road nodes one",
// "and then all that avoid stuff, points should have all that avoid stuff as well. Stops at, keep off, whatever".
//   * A corner placed over a junction or a dead end snaps to its road node (Snap, SnapKind.RoadNode; Alt places
//     freely). Two such corners in a row follow the road between them (shortest route, if it is not a long detour).
//   * The outline is then cut by the shared stops (Game/Selection/AreaWalls): with Roads on, an outline along road
//     centre lines lands exactly on the curbs, as Road nodes did; buildings, lots, zoning, my builds, paths, tracks
//     and water are kept off the same way. The biggest piece is kept; what was left out is shaded grey.

using System;
using System.Collections.Generic;
using Game.Net;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>A route along the roads may be at most this much longer than the straight line (else straight).</summary>
        private const float RouteDetour = 1.5f;

        private readonly List<Entity> _nodeScratch = new List<Entity>();

        /// <summary>
        /// The road node whose junction (or dead end) the point is over: within the half-width of its widest road, at least
        /// the handle radius. Only nodes of ground-level car roads (the ones Road nodes could click).
        /// </summary>
        private (long id, float3 pos) RoadNodeAt(float2 p, float handleRadius)
        {
            try
            {
                float reach = math.max(6f, handleRadius);
                _roads.Query(p - 40f, p + 40f, _nodeScratch);
                long best = 0;
                float3 bestPos = default;
                float bestScore = 1f;
                foreach (var e in _nodeScratch)
                {
                    if (!EntityManager.HasComponent<Node>(e) || !EntityManager.HasBuffer<ConnectedEdge>(e)) continue;
                    float half = 0f;
                    var edges = EntityManager.GetBuffer<ConnectedEdge>(e, true);
                    for (int i = 0; i < edges.Length; i++)
                    {
                        var ed = edges[i].m_Edge;
                        if (!_roads.IsBoundaryEdge(ed)) continue;
                        var eg = EntityManager.GetComponentData<Edge>(ed);
                        if (eg.m_Start != e && eg.m_End != e) continue;   // a side connection, not a junction of this road
                        half = math.max(half, _roads.RoadWidth(ed) * 0.5f);
                    }
                    if (half <= 0f) continue;
                    var pos = EntityManager.GetComponentData<Node>(e).m_Position;
                    float r = math.max(reach, half);
                    float score = math.distance(pos.xz, p) / r;
                    if (score < bestScore) { bestScore = score; best = EntityIds.Pack(e); bestPos = pos; }
                }
                return (best, bestPos);
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Placemaker: road node snap failed: " + ex.Message);
                return (0, default);
            }
        }

        private readonly Dictionary<long, float3[]> _routes = new Dictionary<long, float3[]>();

        /// <summary>
        /// The road between two snapped corners as a centre line (both ends included), or null when they are not joined by
        /// roads or the route is a long detour. Cached per pair for the session of one outline.
        /// </summary>
        public float3[] RouteBetween(long a, long b)
        {
            if (a == 0 || b == 0 || a == b) return null;
            long key = a * 1000003L ^ b;
            if (_routes.TryGetValue(key, out var cached)) return cached;
            float3[] r = null;
            try
            {
                var pa = _roads.NodePosition(a);
                var pb = _roads.NodePosition(b);
                float straight = math.distance(pa.xz, pb.xz);
                var path = new List<HalfEdge>();
                if (RoadGraphAlgorithms.ShortestPath(_roads, a, b, MaxPathVisited, path, null, null, straight * RouteDetour + 30f, out float len) && path.Count > 0)
                {
                    var pieces = new List<IReadOnlyList<float3>>();
                    foreach (var h in path)
                    {
                        if (!_roads.TryCurve(h.Edge, out var c, out var edge)) { pieces.Add(new[] { _roads.NodePosition(h.From), _roads.NodePosition(h.To) }); continue; }
                        bool forward = EntityManager.GetComponentData<Edge>(edge).m_Start == EntityIds.Unpack(h.From);
                        pieces.Add(RoadNetwork.SampleCurve(c, forward, 6));
                    }
                    var line = new List<float3>();
                    foreach (var piece in pieces)
                        foreach (var q in piece)
                            if (line.Count == 0 || math.distance(line[line.Count - 1].xz, q.xz) > 0.05f) line.Add(q);
                    if (line.Count >= 2) r = line.ToArray();
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: route between road nodes failed: " + e.Message); }
            if (_routes.Count > 500) _routes.Clear();
            _routes[key] = r;
            return r;
        }

        /// <summary>The drawn outline with every stretch between two snapped corners following its road (CCW, simple), or the plain corners.</summary>
        private float3[] RoutedRing()
        {
            var plain = _draw.RingCcw();
            if (plain.Length < 3) return plain;
            var pts = _draw.Points;
            var nodes = _draw.Nodes;
            int n = pts.Count;
            bool any = false;
            var ring = new List<float3>(n * 2);
            for (int i = 0; i < n; i++)
            {
                ring.Add(pts[i]);
                int j = (i + 1) % n;
                long a = i < nodes.Count ? nodes[i] : 0, b = j < nodes.Count ? nodes[j] : 0;
                var route = RouteBetween(a, b);
                if (route == null) continue;
                for (int k = 1; k + 1 < route.Length; k++) ring.Add(route[k]);
                any = true;
            }
            if (!any) return plain;
            var ring2 = new float2[ring.Count];
            for (int i = 0; i < ring2.Length; i++) ring2[i] = ring[i].xz;
            var clean = Poly.Clean(ring2, 0.2f);
            if (clean.Length < 3 || !Poly.IsSimple(clean)) return plain;   // routes that cross each other: the corners as drawn
            return Poly.EnsureCcw(Poly.To3D(clean, p => _site.SampleHeight(p)));
        }

        /// <summary>The routed stretches of the outline being drawn (overlay), one polyline per edge index (null = straight).</summary>
        public float3[] RouteOfEdge(int i)
        {
            var nodes = _draw.Nodes;
            int n = _draw.Count;
            if (i < 0 || i >= n) return null;
            int j = (i + 1) % n;
            if (!_draw.Closed && j == 0) return null;
            return RouteBetween(i < nodes.Count ? nodes[i] : 0, j < nodes.Count ? nodes[j] : 0);
        }

        // ───────────── the stops ─────────────

        private List<float3[]> _leftOut;

        /// <summary>
        /// Points (and old Road nodes snapshots): the outline minus every stop the rules name, the biggest piece kept, holes
        /// opened with a corridor. The stops it touched are shaded red, the parts left out grey.
        /// </summary>
        private AreaShape CutByStops(AreaShape s)
        {
            _clipCoveredAll = false;
            _leftOut = null;
            var shown = new List<float3[]>();
            try
            {
                if (s?.Ring == null || s.Ring.Length < 3) return s;
                if (_mode != SelectionMode.Draw && _mode != SelectionMode.Perimeter) return s;
                var rules = BrushRules.ForOutline();
                var ring = s.Ring2D();
                Poly.Bounds(ring, out var mn, out var mx);
                var walls = new List<BrushObstacle>();
                Walls.Counted(mn - 1f, mx + 1f, _editingGroup, rules, walls);
                var touching = new List<IReadOnlyList<float2>>();
                foreach (var w in walls)
                {
                    if (!ObstacleCut.Overlaps(ring, w.Ring)) continue;
                    touching.Add(w.Ring);
                    if (!IsNet(w.Kind) && shown.Count < 400) shown.Add(Poly.To3D(w.Ring, p => _site.SampleHeight(p)));
                }
                if (touching.Count == 0) return s;
                List<Region> pieces;
                try { pieces = Poly.Boolean(new IReadOnlyList<float2>[] { ring }, touching, BoolOp.Difference); }
                catch (Exception e) { Mod.Log.Warn("Placemaker: stops cut failed: " + e.Message); return s; }
                pieces.RemoveAll(p => p?.Outer == null || p.Outer.Length < 3 || p.Area < FillWalls.MinPieceArea);
                if (pieces.Count == 0) { _clipCoveredAll = true; return null; }
                pieces.Sort((a, b) => b.Area.CompareTo(a.Area));
                var kept = FillWalls.PickPiece(new List<Region> { pieces[0] }, Poly.Centroid(pieces[0].Outer), out _);
                if (kept == null || Poly.Area(kept) < 100f) { _clipCoveredAll = true; return null; }
                // What was left out (grey): the drawn outline minus the kept piece.
                try
                {
                    var away = Poly.Boolean(new IReadOnlyList<float2>[] { ring }, new IReadOnlyList<float2>[] { kept }, BoolOp.Difference);
                    _leftOut = new List<float3[]>();
                    foreach (var r in away)
                        if (r.Area > 1f) _leftOut.Add(Poly.To3D(r.Outer, p => _site.SampleHeight(p)));
                }
                catch (Exception) { _leftOut = null; }
                var shape = new AreaShape { Ring = Poly.To3D(kept, p => _site.SampleHeight(p)), Source = s.Source };
                return shape;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: keeping the outline off the stops failed: " + e.Message);
                return s;
            }
            finally
            {
                if (shown.Count > 0 || _clipShown.Count > 0) { _clipShown = shown; _clipShownVersion++; }
            }
        }

        /// <summary>How many corners sit on road nodes (legend).</summary>
        public int CornersOnNodes
        {
            get
            {
                int c = 0;
                foreach (var id in _draw.Nodes) if (id != 0) c++;
                return c;
            }
        }
    }
}
