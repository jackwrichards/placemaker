// Placemaker roads layer (s61) — the perimeter always closes.
//
// Playtest: the perimeter ended in two open ends with cul-de-sac stubs where an obstacle (or a crowded junction) had
// removed its pieces. A perimeter road is a ring: after the other passes every free end of it is joined to the other
// free end across the gap (bending around what is in the way), or — when no clean way round exists — to the nearest
// junction of the network; only when neither is possible the end is reported (with where and why).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadPerimeter
    {
        public sealed class Result
        {
            public int Closed, Joined;
            /// <summary>Free perimeter ends that could not be joined.</summary>
            public readonly List<float2> OpenAt = new List<float2>();
        }

        private const float MaxGap = 300f;
        private const float MaxFallback = 110f;

        public static Result Close(RoadNetwork net, RoadObstacles obstacles, float2[] boundary, Func<NetRole, float> halfWidth, List<ExistingRoad> existing = null,
            List<ExistingRoad> barriers = null)
        {
            var res = new Result();
            for (int round = 0; round < 8; round++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                var open = new List<int>();
                for (int n = 0; n < net.Nodes.Count; n++)
                {
                    if (inc[n].Count != 1 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                    if (net.Edges[inc[n][0]].Role != NetRole.PerimeterRoad) continue;
                    if (NearExisting(existing, net.Nodes[n].Position.xz)) continue;
                    open.Add(n);
                }
                if (open.Count == 0) break;
                var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
                bool changed = false;
                foreach (int u in open)
                {
                    if (TryJoin(net, obstacles, boundary, halfWidth, index, inc, open, u, res, barriers)) { changed = true; break; }
                }
                if (!changed) break;
                net.Weld();
            }
            // Whatever is still open: report it.
            var inc2 = RoadGraph.Incidence(net, RoadGraph.Car);
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc2[n].Count != 1 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                if (net.Edges[inc2[n][0]].Role != NetRole.PerimeterRoad) continue;
                if (NearExisting(existing, net.Nodes[n].Position.xz)) continue;
                res.OpenAt.Add(net.Nodes[n].Position.xz);
            }
            return res;
        }

        /// <summary>An end next to an existing road is where the city road takes over the outline (the dead-end pass joins it): not a gap.</summary>
        private static bool NearExisting(List<ExistingRoad> existing, float2 p)
        {
            if (existing == null) return false;
            foreach (var r in existing) if (r.Curve.DistanceXZ(p, out _) < r.HalfWidth + 70f) return true;
            return false;
        }

        private static bool TryJoin(RoadNetwork net, RoadObstacles obstacles, float2[] boundary, Func<NetRole, float> halfWidth,
            RoadGraph.EdgeIndex index, List<int>[] inc, List<int> open, int u, Result res, List<ExistingRoad> barriers)
        {
            var nu = net.Nodes[u];
            int eu = inc[u][0];
            float2 outU = -RoadGraph.Leaving(net.Edges[eu], u);
            float hw = halfWidth(NetRole.PerimeterRoad) + 1f;
            var cands = new List<(float score, int node, bool open)>();
            for (int v = 0; v < net.Nodes.Count; v++)
            {
                if (v == u || inc[v].Count == 0) continue;
                float d = math.distance(net.Nodes[v].Position.xz, nu.Position.xz);
                if (d < 6f || d > MaxGap) continue;
                if (!open.Contains(v) && d > MaxFallback) continue;
                bool isOpen = open.Contains(v);
                // The other free end of the ring first; then any junction / bend of the network.
                if (!isOpen && inc[v].Count < 2) continue;
                if (inc[v].Contains(eu)) continue;
                cands.Add((isOpen ? d * 0.5f : d, v, isOpen));
            }
            cands.Sort((a, b) => a.score.CompareTo(b.score));
            int tried = 0;
            foreach (var (_, v, isOpen) in cands)
            {
                if (tried++ >= 24) break;
                var nv = net.Nodes[v];
                float2 outV = isOpen ? -RoadGraph.Leaving(net.Edges[inc[v][0]], v) : float2.zero;
                var ignore = new HashSet<int>(inc[u]);
                foreach (int k in inc[v]) ignore.Add(k);
                foreach (var curve in Curves(nu.Position, nv.Position, outU, outV, isOpen))
                {
                    if (obstacles != null && obstacles.Blocked(curve, hw)) continue;
                    var line = RoadGraph.Sample(curve, 3f);
                    bool inside = true;
                    foreach (var p in line)
                        if (!Poly.Contains(boundary, p) && Poly.DistanceToBoundary(boundary, p) > 1.5f) { inside = false; break; }
                    if (!inside) continue;
                    if (index.Crosses(line, 3f, ignore)) continue;
                    if (RoadConnections.CrossesAny(line, barriers)) continue;
                    if (!isOpen && RoadGraph.MinAngleAt(net, inc, v, -curve.TangentXZ(1f)) < 55f) continue;
                    net.Edges.Add(new RoadEdge(curve, isOpen ? NetRole.PerimeterRoad : NetRole.Street));
                    if (isOpen) res.Closed++; else res.Joined++;
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<RoadCurve> Curves(float3 a, float3 b, float2 outA, float2 outB, bool smoothEnd)
        {
            // Continue the ring's own direction out of the free end (and into the other end), then a plain straight link.
            if (smoothEnd) yield return RoadGraph.Link(a, b, outA, -outB);
            else yield return RoadGraph.Link(a, b, outA);
            yield return RoadCurve.Straight(a, b);
        }
    }
}
