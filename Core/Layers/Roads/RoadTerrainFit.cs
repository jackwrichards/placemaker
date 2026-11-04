// Placemaker roads layer (smarter roads) — a grid that gives way to the hill. Real grids are not perfect on hills, and
// that is where they get interesting: the planner lets the land break the grid, only where the land asks for it.
//
//   bend          interior junctions move a little (at most a fifth of the spacing) where that takes the streets around
//                 them under the design grade; flat land moves nothing.
//   stair streets a street still far too steep for cars (over 1.5x the design grade) becomes a footpath (steps up the
//                 hill), when the car network stays in one piece: people walk straight up, cars go round by the gentle streets.
//   hill parks    a block steeper than a building plot takes (over 13 % on average) is tagged "green": a hillside park,
//                 not lots on a slope.
// Grid character "strict" turns all of it off (the plain grid, whatever the ground).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadTerrainFit
    {
        /// <summary>Average slope above which a block is better a park than lots.</summary>
        public const float HillParkSlope = 0.13f;

        /// <summary>
        /// Moves interior junctions (and plain bends) up to <paramref name="maxMove"/> m where it brings the streets meeting there
        /// under the design grade, never closing a junction angle under 70° nor leaving the area. Returns the nodes moved.
        /// </summary>
        public static int Bend(RoadNetwork net, Func<float2, float> height, float maxGrade, float maxMove, float2[] boundary)
        {
            if (height == null || !(maxMove > 1f)) return 0;
            net.Weld();
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            int n = net.Nodes.Count;
            var p0 = new float2[n];
            var movable = new bool[n];
            for (int i = 0; i < n; i++)
            {
                p0[i] = net.Nodes[i].Position.xz;
                if (inc[i].Count < 2 || RoadConnectivity.Attached(net.Nodes[i]) || net.IsCorner(net.Nodes[i])) continue;
                if (boundary.Length >= 3 && Poly.DistanceToBoundary(boundary, p0[i]) < 10f) continue;
                bool straight = true;
                foreach (int ei in inc[i]) straight &= net.Edges[ei].Curve.IsStraight(0.2f) && net.Edges[ei].Role != NetRole.Roundabout;
                movable[i] = straight;
            }
            float target = maxGrade * 0.9f;
            float2 Pos(int k) => net.Nodes[k].Position.xz;
            float Cost(int k, float2 p)
            {
                float c = 0f;
                var dirs = new List<float2>(4);
                foreach (int ei in inc[k])
                {
                    var e = net.Edges[ei];
                    int o = e.N0 == k ? e.N1 : e.N0;
                    float2 q = Pos(o);
                    float len = math.distance(p, q);
                    if (len < 12f) return float.MaxValue;
                    var curve = RoadCurve.Straight(new float3(p.x, 0f, p.y), new float3(q.x, 0f, q.y));
                    float g = RoadGraph.MaxGrade(curve, height, 16f, 4f);
                    if (g > target) c += (g - target) * (g - target) * 40000f;
                    dirs.Add(math.normalizesafe(q - p));
                }
                for (int a = 0; a < dirs.Count; a++)
                    for (int b = a + 1; b < dirs.Count; b++)
                    {
                        float ang = RoadGraph.Angle(dirs[a], dirs[b]);
                        if (ang < 70f) c += (70f - ang) * (70f - ang) * 0.5f;
                    }
                c += math.distancesq(p, p0[k]) * 0.02f;
                return c;
            }
            int moved = 0;
            var tried = new HashSet<int>();
            for (int pass = 0; pass < 4; pass++)
            {
                bool any = false;
                for (int k = 0; k < n; k++)
                {
                    if (!movable[k]) continue;
                    float2 cur = Pos(k);
                    float best = Cost(k, cur);
                    if (best < 1f) continue;   // nothing steep here
                    float2 bestP = cur;
                    foreach (float r in new[] { 3f, 6f, 10f, 14f, maxMove })
                    {
                        if (r > maxMove) continue;
                        for (int d = 0; d < 8; d++)
                        {
                            float a = d * math.PI / 4f;
                            float2 p = cur + new float2(math.cos(a), math.sin(a)) * r;
                            if (math.distance(p, p0[k]) > maxMove) continue;
                            if (boundary.Length >= 3 && (!Poly.Contains(boundary, p) || Poly.DistanceToBoundary(boundary, p) < 10f)) continue;
                            float c = Cost(k, p);
                            if (c < best - 1f) { best = c; bestP = p; }
                        }
                    }
                    if (math.all(bestP == cur)) continue;
                    var node = net.Nodes[k];
                    node.Position = new float3(bestP.x, node.Position.y, bestP.y);
                    foreach (int ei in inc[k])
                    {
                        var e = net.Edges[ei];
                        e.Curve = RoadCurve.Straight(e.N0 == k ? node.Position : e.Curve.A, e.N1 == k ? node.Position : e.Curve.D);
                    }
                    if (tried.Add(k)) moved++;
                    any = true;
                }
                if (!any) break;
            }
            net.Weld();
            return moved;
        }

        /// <summary>
        /// Streets still steeper than 1.5× the design grade (and 4 points over it) become footpaths — steps up the hill — one
        /// piece at a time, steepest first, when the car network stays in one piece. Main roads, the perimeter and links stay.
        /// Returns the pieces turned into paths.
        /// </summary>
        public static int StairStreets(RoadNetwork net, Func<float2, float> height, float maxGrade)
        {
            if (height == null) return 0;
            float limit = math.max(maxGrade * 1.5f, maxGrade + 0.04f);
            var steep = new List<(float g, int e)>();
            for (int i = 0; i < net.Edges.Count; i++)
            {
                var e = net.Edges[i];
                if (!RoadGraph.Car(e) || e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue || e.Role == NetRole.Roundabout || e.Connector) continue;
                if (net.Nodes[e.N0].Attach >= 0 || net.Nodes[e.N1].Attach >= 0) continue;
                float g = RoadGraph.MaxGrade(e.Curve, height, 16f, 4f);
                if (g > limit) steep.Add((g, i));
            }
            steep.Sort((a, b) => b.g.CompareTo(a.g));
            int made = 0;
            RoadGraph.Components(net, RoadGraph.Car, out int before);
            foreach (var (_, i) in steep)
            {
                var e = net.Edges[i];
                var role = e.Role;
                e.Role = NetRole.Pathway;
                e.Walk = true;
                RoadGraph.Components(net, RoadGraph.Car, out int after);
                if (after > before) { e.Role = role; e.Walk = false; continue; }
                e.CapAtEnd = false;
                made++;
            }
            return made;
        }

        /// <summary>Mean slope of the ground inside a block (sampled on a grid).</summary>
        public static float MeanSlope(float2[] ring, Func<float2, float> height)
        {
            if (height == null || ring == null || ring.Length < 3) return 0f;
            Poly.Bounds(ring, out float2 mn, out float2 mx);
            float step = math.max(8f, math.cmax(mx - mn) / 12f);
            float sum = 0f; int count = 0;
            for (float x = mn.x + step * 0.5f; x < mx.x; x += step)
                for (float z = mn.y + step * 0.5f; z < mx.y; z += step)
                {
                    var p = new float2(x, z);
                    if (!Poly.Contains(ring, p)) continue;
                    float gx = (height(p + new float2(4f, 0f)) - height(p - new float2(4f, 0f))) / 8f;
                    float gz = (height(p + new float2(0f, 4f)) - height(p - new float2(0f, 4f))) / 8f;
                    sum += math.sqrt(gx * gx + gz * gz);
                    count++;
                }
            return count > 0 ? sum / count : 0f;
        }
    }
}
