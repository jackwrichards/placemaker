// Placemaker roads layer (smarter roads) — the ground the new streets wear in: painted into the terrain texture, so a new
// district looks lived in instead of stamped on.
//
//   desire paths  worn dirt lines where people would walk a shortcut the streets do not give them: the straight way
//                 between two street points is under a third of the way round (or across to the next city street), it
//                 crosses no road and no building and is not steep. Best shortcuts first, never two side by side.
//   corners       worn ground at junction corners, where people cut across instead of following the kerb.
//   verges        a faint dry-grass strip along the new streets, just past the sidewalk.
//
// "Ground paint": off | subtle (the default) | lived-in (more paths, stronger wear). Paint is permanent (the shared
// warning). Everything is deterministic: positions and the step seed decide.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Paint;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadPaint
    {
        public const string Off = "off", Subtle = "subtle", Lived = "lived";
        public static readonly string[] Values = { Off, Subtle, Lived };

        public sealed class Options
        {
            public string Level = Subtle;
            public float2[] Boundary = Array.Empty<float2>();
            public List<ExistingRoad> Existing = new List<ExistingRoad>();
            public RoadObstacles Obstacles;
            public Func<float2, float> Height;
            public Func<NetRole, float> HalfWidth = r => 8f;
            public uint Seed = 1;
        }

        public sealed class Result
        {
            public int Desire, Corners, Verges;
            /// <summary>The desire lines painted (the road lab draws them).</summary>
            public readonly List<(float2 a, float2 b)> Lines = new List<(float2, float2)>();
        }

        private const float MinShortcut = 25f, MaxShortcut = 150f;
        private const float DetourRatio = 2.6f;

        public static Result Apply(AreaPlan plan, RoadNetwork net, Options o)
        {
            var res = new Result();
            if (o.Level == Off || net.Edges.Count == 0) return res;
            bool lived = o.Level == Lived;
            var kit = new PaintKit(plan.Paint, o.Height, Hashing.Mix(o.Seed, 0xD35Eu)) { MaxPaints = lived ? 320 : 220 };
            Desire(kit, net, o, lived, res);
            Corners(kit, net, o, lived, res);
            Verges(kit, net, o, lived, res);
            if (kit.Count > 0)
            {
                bool warned = false;
                foreach (var i in plan.Issues) warned |= i.Key == TerrainPaint.PermanentKey;
                if (!warned) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = TerrainPaint.PermanentKey });
            }
            return res;
        }

        // ───────────────────────────── desire paths ─────────────────────────────

        private struct Pt
        {
            public float2 P;
            public int A, B;        // the edge's nodes (or A = B = the node)
            public float DA, DB;    // walking distance to them along the edge
            public float Hw;        // half width of the road there
            public int Edge;
        }

        private static void Desire(PaintKit kit, RoadNetwork net, Options o, bool lived, Result res)
        {
            var walk = RoadGraph.Incidence(net, RoadGraph.Any);
            var lengths = RoadGraph.Lengths(net);
            // points along the streets every ~35 m (people leave a street anywhere, not only at junctions)
            var pts = new List<Pt>();
            for (int ei = 0; ei < net.Edges.Count; ei++)
            {
                var e = net.Edges[ei];
                if (!RoadGraph.Car(e)) continue;
                float len = lengths[ei];
                int k = math.max(1, (int)math.round(len / 35f));
                for (int i = 0; i <= k; i++)
                {
                    float s = len * i / k;
                    float t = RoadGraph.ParamAt(e.Curve, s);
                    pts.Add(new Pt { P = e.Curve.Position(t).xz, A = e.N0, B = e.N1, DA = s, DB = len - s, Hw = o.HalfWidth(e.Role), Edge = ei });
                }
            }
            if (pts.Count < 2) return;
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            // walking distances between nodes, from each point's two nodes (capped: a shortcut only matters within a few hundred metres)
            var dist = new Dictionary<int, float[]>();
            float[] From(int node)
            {
                if (!dist.TryGetValue(node, out var d)) dist[node] = d = RoadGraph.Distances(net, walk, node, MaxShortcut * DetourRatio * 1.5f, lengths);
                return d;
            }
            float Walk(Pt a, Pt b)
            {
                if (a.Edge == b.Edge) return math.abs(a.DA - b.DA);
                float best = float.PositiveInfinity;
                var fa = From(a.A); var fb = From(a.B);
                best = math.min(best, a.DA + math.min(fa[b.A] + b.DA, fa[b.B] + b.DB));
                best = math.min(best, a.DB + math.min(fb[b.A] + b.DA, fb[b.B] + b.DB));
                return best;
            }
            var cands = new List<(float gain, Pt a, Pt b)>();
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    var a = pts[i]; var b = pts[j];
                    float s = math.distance(a.P, b.P);
                    if (s < MinShortcut || s > MaxShortcut || a.Edge == b.Edge) continue;
                    float w = Walk(a, b);
                    if (!(w > s * DetourRatio)) continue;
                    cands.Add((math.min(w, s * 8f) - s, a, b));
                }
            cands.Sort((x, y) => y.gain.CompareTo(x.gain));
            int wanted = math.clamp((int)math.round(Poly.Area(o.Boundary) / (lived ? 25000f : 60000f)), lived ? 2 : 1, lived ? 14 : 6);
            var chosen = new List<(float2 a, float2 b)>();
            foreach (var (_, a, b) in cands)
            {
                if (chosen.Count >= wanted) break;
                float2 d = math.normalizesafe(b.P - a.P);
                // from the sidewalk's edge, not the road's centre
                float2 pa = a.P + d * (a.Hw + 0.8f), pb = b.P - d * (b.Hw + 0.8f);
                if (math.distance(pa, pb) < MinShortcut * 0.6f) continue;
                if (!Clear(index, net, o, pa, pb)) continue;
                bool near = false;
                foreach (var (ca, cb) in chosen)
                    if (Seg.Dist(math.lerp(pa, pb, 0.5f), ca, cb) < 40f || Seg.Dist(pa, ca, cb) < 25f || Seg.Dist(pb, ca, cb) < 25f
                        || Seg.Intersect(pa, pb, ca, cb, out _, out _)) { near = true; break; }
                if (near) continue;
                chosen.Add((pa, pb));
            }
            foreach (var (pa, pb) in chosen)
            {
                uint seed = Hashing.Mix(o.Seed, math.hash(pa + pb));
                TrailPaint.DesireLine(kit, pa, pb, lived ? 2.4f : 2.1f, lived ? 0.8f : 0.66f, TerrainMaterials.Dirt, seed);
                res.Desire++;
                res.Lines.Add((pa, pb));
            }
        }

        /// <summary>A shortcut crosses no road (new or existing), no building, stays inside the area and is walkable.</summary>
        private static bool Clear(RoadGraph.EdgeIndex index, RoadNetwork net, Options o, float2 a, float2 b)
        {
            var line = new List<float2>();
            float len = math.distance(a, b);
            int n = math.max(2, (int)math.ceil(len / 3f));
            for (int i = 0; i <= n; i++) line.Add(math.lerp(a, b, (float)i / n));
            if (index.Crosses(line, 0.5f)) return false;
            if (index.Clearance(line, 4f, 12f) < 7f) return false;   // along a road it is a sidewalk, not a shortcut
            if (RoadConnections.CrossesAny(line, o.Existing, 0.5f)) return false;
            if (o.Boundary != null && o.Boundary.Length >= 3)
                foreach (var p in line) if (!Poly.Contains(o.Boundary, p)) return false;
            var c = RoadCurve.Straight(new float3(a.x, 0f, a.y), new float3(b.x, 0f, b.y));
            if (o.Obstacles != null && o.Obstacles.Blocked(c, 2f)) return false;
            if (o.Height != null && RoadGraph.MaxGrade(c, o.Height, 10f, 3f) > 0.2f) return false;
            return true;
        }

        // ───────────────────────────── corners and verges ─────────────────────────────

        private static void Corners(PaintKit kit, RoadNetwork net, Options o, bool lived, Result res)
        {
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc[n].Count < 3) continue;
                float2 c = net.Nodes[n].Position.xz;
                var dirs = new List<(float ang, float hw)>();
                foreach (int ei in inc[n])
                {
                    float2 d = RoadGraph.Leaving(net.Edges[ei], n);
                    dirs.Add((math.atan2(d.y, d.x), o.HalfWidth(net.Edges[ei].Role)));
                }
                dirs.Sort((x, y) => x.ang.CompareTo(y.ang));
                var shapes = new List<PaintShape>();
                for (int i = 0; i < dirs.Count; i++)
                {
                    float a0 = dirs[i].ang, a1 = i + 1 < dirs.Count ? dirs[i + 1].ang : dirs[0].ang + 2f * math.PI;
                    float sector = a1 - a0;
                    if (sector < math.radians(50f) || sector > math.radians(140f)) continue;
                    float mid = a0 + sector * 0.5f;
                    float hw = math.max(dirs[i].hw, dirs[(i + 1) % dirs.Count].hw);
                    // the corner of the two sidewalks, a little beyond (where people cut across)
                    float r = (hw + 1.5f) / math.sin(sector * 0.5f);
                    float2 at = c + new float2(math.cos(mid), math.sin(mid)) * r;
                    if (o.Obstacles != null && o.Obstacles.BlockedAt(at)) continue;
                    if (o.Boundary.Length >= 3 && !Poly.Contains(o.Boundary, at)) continue;
                    shapes.Add(PaintShape.Radial(at, 0f, lived ? 5.5f : 4.5f, Falloff.Smooth, 1.2f, 3f, kit.NextSeed()));
                }
                if (shapes.Count == 0) continue;
                if (kit.Shape(PaintShape.Union(shapes), TerrainMaterials.Dirt, lived ? 0.6f : 0.42f) != null) res.Corners++;
            }
        }

        private static void Verges(PaintKit kit, RoadNetwork net, Options o, bool lived, Result res)
        {
            var strokes = new List<PaintShape>();
            void Flush()
            {
                if (strokes.Count == 0) return;
                if (kit.Shape(PaintShape.Union(strokes), TerrainMaterials.Dry, lived ? 0.46f : 0.32f) != null) res.Verges++;
                strokes.Clear();
            }
            var pts = new List<float3>();
            foreach (var e in net.Edges)
            {
                if (!RoadGraph.Car(e)) continue;
                float len = RoadGraph.Length(e);
                if (len < 20f) continue;
                float hw = o.HalfWidth(e.Role) + 2.2f;
                pts.Clear();
                e.Curve.Flatten(pts, null, 6f, 8f);
                foreach (float side in new[] { 1f, -1f })
                {
                    var line = new List<float2>(pts.Count);
                    for (int i = 0; i < pts.Count; i++)
                    {
                        float t = (float)i / (pts.Count - 1);
                        float2 tan = e.Curve.TangentXZ(t);
                        float2 p = pts[i].xz + new float2(-tan.y, tan.x) * hw * side;
                        if (o.Boundary.Length >= 3 && !Poly.Contains(o.Boundary, p)) continue;
                        line.Add(p);
                    }
                    if (line.Count < 2) continue;
                    // stops short of the junctions (the corners have their own wear), frays along its length
                    strokes.Add(PaintShape.Stroke(line, 3f, 1.4f, 1f, 1f, Falloff.Linear, 6f, 0.18f, 16f, kit.NextSeed()));
                }
                if (strokes.Count >= 16) Flush();
            }
            Flush();
        }
    }
}
