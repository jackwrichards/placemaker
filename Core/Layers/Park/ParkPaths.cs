// Turns a planned path graph into net courses. Shared by park and campground.
//
// Curves: each edge becomes one cubic Bézier whose end tangents follow the chain through
// degree-two nodes (Catmull-Rom style), so a bend is one smooth CS2 course instead of
// ParkManager's corner-cut polyline of short straight courses. Junctions and gates keep straight
// tangents so crossings stay readable. A curve that leaves the area or bulges too much falls back
// to a straight segment.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    /// <summary>Path hierarchy tier (ParkPathHierarchy.cs): decides which asset a curve is built with.</summary>
    public enum PathTier { Main = 0, Small = 1, Trail = 2, Bike = 3, Desire = 4, Service = 5 }

    public sealed class PathCurve
    {
        public float2 A, B, C, D;
        public NetRole Role;
        public PathTier Tier;
        public float HalfWidth;
        public PathEdgeKind Kind;
        /// <summary>Gate index at the A / D end, −1 when interior.</summary>
        public int GateA = -1, GateD = -1;
        public float2[] Samples;
        public float Length;

        public float2 Point(float t)
        {
            float u = 1 - t;
            return u * u * u * A + 3 * u * u * t * B + 3 * u * t * t * C + t * t * t * D;
        }

        public float2 Tangent(float t)
        {
            float u = 1 - t;
            var d = 3 * u * u * (B - A) + 6 * u * t * (C - B) + 3 * t * t * (D - C);
            return math.normalizesafe(d, math.normalizesafe(D - A, new float2(1, 0)));
        }

        /// <summary>Point and tangent at arc length s along the sampled polyline.</summary>
        public float2 At(float s, out float2 tangent)
        {
            var samples = Samples;
            if (!ReferenceEquals(_measured, samples)) Measure(samples);
            for (int i = 1; i < samples.Length; i++)
            {
                float len = _pieceLength[i];
                if (s <= len || i == samples.Length - 1)
                {
                    tangent = _pieceTangent[i];
                    return samples[i - 1] + tangent * math.clamp(s, 0f, len);
                }
                s -= len;
            }
            tangent = new float2(1, 0);
            return A;
        }

        // (perf) Each sampled piece's length and direction, worked out once per Samples array (the planting asks ~100 000 times).
        private float2[] _measured;
        private float[] _pieceLength;
        private float2[] _pieceTangent;

        private void Measure(float2[] samples)
        {
            int n = samples?.Length ?? 0;
            _pieceLength = new float[math.max(1, n)];
            _pieceTangent = new float2[math.max(1, n)];
            for (int i = 1; i < n; i++)
            {
                _pieceLength[i] = math.distance(samples[i - 1], samples[i]);
                _pieceTangent[i] = math.normalizesafe(samples[i] - samples[i - 1], new float2(1, 0));
            }
            _measured = samples;
        }
    }

    public static class ParkPaths
    {
        public const int SamplesPerCurve = 8;

        public static NetRole RoleFor(PathEdgeKind kind, string widthMode)
        {
            switch (widthMode)
            {
                case "narrow": return NetRole.Pathway;
                case "wide": return NetRole.PathwayWide;
                default: return kind == PathEdgeKind.Secondary ? NetRole.Pathway : NetRole.PathwayWide;
            }
        }

        public static List<PathCurve> Build(ParkPathGraph graph, LayerRegion region, bool curved, string widthMode)
        {
            var result = new List<PathCurve>();
            if (graph == null || graph.IsEmpty) return result;
            var adj = graph.Adjacency();
            foreach (var e in graph.Edges)
            {
                var a = graph.Nodes[e.A]; var d = graph.Nodes[e.B];
                var pa = a.Position; var pd = d.Position;
                float len = math.distance(pa, pd);
                var dir = math.normalizesafe(pd - pa, new float2(1, 0));
                var ta = dir; var td = dir;
                if (curved)
                {
                    ta = SmoothTangent(graph, adj, e.A, e.B, dir, true);
                    td = SmoothTangent(graph, adj, e.B, e.A, dir, false);
                }
                var role = RoleFor(e.Kind, widthMode);
                var c = new PathCurve
                {
                    A = pa, D = pd, Role = role, Kind = e.Kind, HalfWidth = Composer.DefaultHalfWidth(role),
                    GateA = a.Kind == PathNodeKind.Gate ? a.Gate : -1,
                    GateD = d.Kind == PathNodeKind.Gate ? d.Gate : -1,
                };
                float k = len / 3f * 0.9f;
                c.B = pa + ta * k; c.C = pd - td * k;
                Sample(c);
                if (curved && !CurveOk(c, region, len)) { c.B = math.lerp(pa, pd, 1f / 3f); c.C = math.lerp(pa, pd, 2f / 3f); Sample(c); }
                result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Tangent at <paramref name="node"/> for the edge towards <paramref name="other"/>: through a
        /// degree-two non-gate node it is the chord of its two neighbours, otherwise the edge itself.
        /// </summary>
        private static float2 SmoothTangent(ParkPathGraph g, List<int>[] adj, int node, int other, float2 edgeDir, bool leaving)
        {
            var n = g.Nodes[node];
            if (n.Kind == PathNodeKind.Gate || adj[node].Count != 2) return edgeDir;
            int e0 = adj[node][0], e1 = adj[node][1];
            var first = g.Edges[e0]; var second = g.Edges[e1];
            int n0 = first.A == node ? first.B : first.A;
            int n1 = second.A == node ? second.B : second.A;
            int prev = n0 == other ? n1 : n0;   // the neighbour that is not on this edge
            // Travelling along this edge: leaving the node towards `other`, the chain comes from `prev`.
            var chord = leaving ? g.Nodes[other].Position - g.Nodes[prev].Position
                                : g.Nodes[prev].Position - g.Nodes[other].Position;
            var t = math.normalizesafe(chord, edgeDir);
            // Sharp bends would overshoot; keep the tangent within 60° of the edge direction.
            if (math.dot(t, edgeDir) < 0.5f) t = math.normalizesafe(t + edgeDir, edgeDir);
            return t;
        }

        private static bool CurveOk(PathCurve c, LayerRegion region, float chord)
        {
            float bulge = 0;
            foreach (var p in c.Samples)
            {
                if (!region.Contains(p) && region.BoundaryDistance(p, 0.1f) > 0.05f) return false;
                bulge = math.max(bulge, math.sqrt(SegmentIndex.DistSq(p, c.A, c.D)));
            }
            return bulge <= chord * 0.25f + 0.5f;
        }

        public static void Sample(PathCurve c)
        {
            c.Samples = new float2[SamplesPerCurve + 1];
            c.Length = 0;
            for (int i = 0; i <= SamplesPerCurve; i++)
            {
                c.Samples[i] = c.Point(i / (float)SamplesPerCurve);
                if (i > 0) c.Length += math.distance(c.Samples[i - 1], c.Samples[i]);
            }
        }

        /// <summary>Adds the built footprint of every curve to an index (for clearance and lawn extraction).</summary>
        public static void Index(List<PathCurve> curves, SegmentIndex into, int kind)
        {
            foreach (var c in curves)
                for (int i = 1; i < c.Samples.Length; i++)
                    into.Add(c.Samples[i - 1], c.Samples[i], c.HalfWidth, kind);
        }

        /// <summary>Emits PlannedNets, attaching gate ends to existing roads when known.</summary>
        public static float Emit(LayerContext ctx, List<PathCurve> curves, List<Gate> gates, AssetRef asset) =>
            Emit(ctx, curves, gates, _ => asset);

        /// <summary>Emits PlannedNets with one asset per hierarchy tier.</summary>
        public static float Emit(LayerContext ctx, List<PathCurve> curves, List<Gate> gates, System.Func<PathTier, AssetRef> assetFor)
        {
            float total = 0;
            foreach (var c in curves)
            {
                var net = ctx.CurvedNet(c.A, c.B, c.C, c.D, c.Role, assetFor(c.Tier));
                // Tier-specific automatic pools (bike route / dirt trail); empty pools fall back to pathway.
                if (c.Tier == PathTier.Bike) net.Category = AssetCategories.BikePath;
                else if (c.Tier == PathTier.Trail || c.Tier == PathTier.Service) net.Category = AssetCategories.TrailPath;
                if (c.GateA >= 0 && c.GateA < gates.Count && gates[c.GateA].AttachId >= 0)
                { net.StartAttach = gates[c.GateA].AttachId; net.StartAttachT = gates[c.GateA].AttachT; }
                if (c.GateD >= 0 && c.GateD < gates.Count && gates[c.GateD].AttachId >= 0)
                { net.EndAttach = gates[c.GateD].AttachId; net.EndAttachT = gates[c.GateD].AttachT; }
                total += c.Length;
            }
            return total;
        }
    }
}
