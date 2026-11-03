// Placemaker — what the area tools stop at, as polygons (the area overhaul's one wall source).
//
// Every tool (Points, Paint bucket, Paint area) cuts its outline with the same walls, picked by the shared "Stops at"
// rules (Core BrushRules):
//   * road, path, bike path and track SURFACES up to the curb, from the game's own geometry: each edge's two segments
//     (EdgeGeometry) and its two ends at the junctions (Start/EndNodeGeometry, as Game.Zones.CellBlockJobs blocks zone
//     cells with them) — so junction corners, roundabouts and dead ends' turning circles are exact. The old bucket
//     joined the curbs straight across junctions and skipped the turning circle ("cul-de-sacs don't work");
//   * buildings, lots, other Placemaker builds and zone cells (BlockObstacles);
//   * water (a point test sampled once into a grid, BlockObstacles.Water).
// Bridges and tunnels are not walls, nor are nets inside a building's lot or the group being edited.
// Read-only, bounded queries on the search trees; main thread only. Sets are cached a few seconds per box.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public sealed class AreaWalls
    {
        /// <summary>Seconds a gathered set is reused for boxes inside it (the city keeps growing).</summary>
        public const float MaxAge = 4f;

        private readonly EntityManager _em;
        private readonly RoadNetwork _roads;
        private readonly BlockObstacles _obstacles;
        private readonly List<Entity> _scratch = new List<Entity>();

        private float2 _min, _max;
        private float _at = -100f;
        private int _group = -1;
        private List<BrushObstacle> _set;

        public AreaWalls(EntityManager em, RoadNetwork roads, BlockObstacles obstacles)
        {
            _em = em;
            _roads = roads;
            _obstacles = obstacles;
        }

        public BlockObstacles Obstacles => _obstacles;
        public float LastMs { get; private set; }
        public string LastStats { get; private set; } = "";

        /// <summary>Forget the cached set (a build changed the city).</summary>
        public void Invalidate()
        {
            _set = null;
            _obstacles.Invalidate();
        }

        /// <summary>
        /// Every wall of every kind touching [min, max] (the caller filters by the rules). Reuses the last set while the
        /// box lies inside it and it is younger than MaxAge; otherwise reads a slightly bigger box.
        /// </summary>
        public List<BrushObstacle> All(float2 min, float2 max, int ignoreGroup)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_set != null && _group == ignoreGroup && now - _at < MaxAge && now >= _at && math.all(min >= _min) && math.all(max <= _max))
                return _set;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float2 gmin = min - 30f, gmax = max + 30f;
            var list = new List<BrushObstacle>();
            int nets = 0;
            try { nets = Nets(gmin, gmax, ignoreGroup, list); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: area walls could not read the roads: " + e.Message); }
            list.AddRange(_obstacles.Gather(gmin, gmax, ignoreGroup, true));
            _set = list;
            _min = gmin;
            _max = gmax;
            _at = now;
            _group = ignoreGroup;
            LastMs = (float)sw.Elapsed.TotalMilliseconds;
            LastStats = $"{list.Count} walls ({nets} net pieces) in {(int)(gmax.x - gmin.x)}x{(int)(gmax.y - gmin.y)} m, {LastMs:0.0} ms";
            return list;
        }

        /// <summary>The walls the rules stop at, touching [min, max].</summary>
        public void Counted(float2 min, float2 max, int ignoreGroup, BrushRules rules, List<BrushObstacle> into)
        {
            foreach (var w in All(min, max, ignoreGroup))
            {
                if (w.Ring == null || !rules.Stops(w.Kind)) continue;
                Poly.Bounds(w.Ring, out var a, out var b);
                if (b.x < min.x || b.y < min.y || a.x > max.x || a.y > max.y) continue;
                into.Add(w);
            }
        }

        /// <summary>Water test for the box (null when dry or water is not a stop).</summary>
        public Func<float2, bool> Water(float2 min, float2 max, float step, BrushRules rules) =>
            rules.Stops(BrushObstacleKind.Water) ? _obstacles.Water(min, max, math.max(2f, step), false) : null;

        // ───────────── net surfaces ─────────────

        private int Nets(float2 min, float2 max, int ignoreGroup, List<BrushObstacle> into)
        {
            _roads.Query(min, max, _scratch);
            var seen = new HashSet<Entity>();
            int pieces = 0;
            foreach (var e in _scratch)
            {
                if (!seen.Add(e) || !_em.HasComponent<Edge>(e) || !_em.HasComponent<EdgeGeometry>(e)) continue;
                var kind = _roads.KindOf(e);
                BrushObstacleKind k;
                switch (kind)
                {
                    case FillWall.Roads: k = BrushObstacleKind.Road; break;
                    case FillWall.Paths: k = BrushObstacleKind.Path; break;
                    case FillWall.Bikes: k = BrushObstacleKind.Bike; break;
                    case FillWall.Tracks: k = BrushObstacleKind.Track; break;
                    default: continue;
                }
                if (ignoreGroup != 0 && InGroup(e, ignoreGroup)) continue;
                var g = _em.GetComponentData<EdgeGeometry>(e);
                pieces += Strip(g.m_Start.m_Left, g.m_Start.m_Right, k, into);
                pieces += Strip(g.m_End.m_Left, g.m_End.m_Right, k, into);
                var comp = _em.HasComponent<Composition>(e) ? _em.GetComponentData<Composition>(e) : default;
                if (_em.HasComponent<StartNodeGeometry>(e) && GroundNode(comp.m_StartNode))
                    pieces += NodePiece(_em.GetComponentData<StartNodeGeometry>(e).m_Geometry, k, into);
                if (_em.HasComponent<EndNodeGeometry>(e) && GroundNode(comp.m_EndNode))
                    pieces += NodePiece(_em.GetComponentData<EndNodeGeometry>(e).m_Geometry, k, into);
            }
            return pieces;
        }

        private bool InGroup(Entity e, int group)
        {
            try { return Services.Groups != null && Services.Groups.GroupOf(e) == group; }
            catch (Exception) { return false; }
        }

        /// <summary>A junction end that is not a tunnel or a bridge (its own composition says so).</summary>
        private bool GroundNode(Entity composition)
        {
            if (composition == Entity.Null || !_em.HasComponent<NetCompositionData>(composition)) return true;
            var flags = _em.GetComponentData<NetCompositionData>(composition).m_Flags.m_General;
            return (flags & (CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel)) == 0;
        }

        /// <summary>One junction end, cut like CellBlockJobs: round ends (turning circles, roundabouts) in three strips.</summary>
        private static int NodePiece(EdgeNodeGeometry n, BrushObstacleKind k, List<BrushObstacle> into)
        {
            if (n.m_MiddleRadius > 0f)
            {
                var mid = MathUtils.Lerp(n.m_Right.m_Left, n.m_Right.m_Right, 0.5f);
                mid.d = n.m_Middle.d;
                return Strip(n.m_Left.m_Left, n.m_Left.m_Right, k, into) + Strip(n.m_Right.m_Left, mid, k, into) + Strip(mid, n.m_Right.m_Right, k, into);
            }
            return Strip(n.m_Left.m_Left, n.m_Middle, k, into) + Strip(n.m_Middle, n.m_Right.m_Right, k, into);
        }

        /// <summary>The surface between two side curves (left forward, right back) as one polygon.</summary>
        private static int Strip(Bezier4x3 left, Bezier4x3 right, BrushObstacleKind k, List<BrushObstacle> into)
        {
            var pts = new List<float2>(20);
            Sample(left, true, pts);
            Sample(right, false, pts);
            var ring = Poly.Clean(pts, 0.02f);
            if (ring.Length < 3 || Poly.Area(ring) < 0.05f) return 0;
            foreach (var p in ring) if (!Poly.IsFinite(p)) return 0;
            into.Add(new BrushObstacle(k, Poly.EnsureCcw(ring)));
            return 1;
        }

        private static void Sample(Bezier4x3 c, bool forward, List<float2> into)
        {
            float chord = math.distance(c.a.xz, c.d.xz);
            float hull = math.distance(c.a.xz, c.b.xz) + math.distance(c.b.xz, c.c.xz) + math.distance(c.c.xz, c.d.xz);
            int n = hull - chord < 0.03f ? 1 : math.clamp((int)math.ceil(hull / 3f), 4, 12);
            for (int i = 0; i <= n; i++)
            {
                float t = forward ? (float)i / n : 1f - (float)i / n;
                into.Add(MathUtils.Position(c, t).xz);
            }
        }
    }
}
