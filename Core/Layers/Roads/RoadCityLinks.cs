// Placemaker roads layer (smarter roads) — the joins between the new network and the city, placed the way a designer would.
//
// Candidates come from both sides:
//   continue   a street inside the area runs on, straight through the perimeter, to the first road it meets (or to a
//              junction of that road a few metres off its line);
//   reach in   an existing street ending at the area, or a T whose free side faces it, runs on straight into the plan, onto
//              the junction where an inside street meets the perimeter when one is near (a clean four-way crossing).
// Every candidate is checked (no crossing anything, clear of other roads and buildings, not too steep, square at both ends,
// never onto a highway or a ramp, never within 35 m of a junction it does not land on, the junction spacing on big roads)
// and scored: short, in line with a street inside, onto an existing junction, square. The best are taken one by one, each
// far enough from the others along the outline, up to one per ~350 m of outline. What is left unconnected falls back to
// the connectivity pass (RoadConnectivity).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadCityLinks
    {
        public sealed class Options
        {
            public float2[] Boundary = Array.Empty<float2>();
            /// <summary>Roads the plan may join.</summary>
            public List<ExistingRoad> Existing = new List<ExistingRoad>();
            /// <summary>Roads no link may cross.</summary>
            public List<ExistingRoad> Barriers = new List<ExistingRoad>();
            public RoadObstacles Obstacles;
            public Func<float2, float> Height;
            public Func<NetRole, float> HalfWidth = r => 8f;
            public float MaxGrade = 0.1f;
            /// <summary>Junction spacing wanted on big roads (m).</summary>
            public float JunctionSpacing = 120f;
            /// <summary>Longest link (m).</summary>
            public float Reach = 140f;
            /// <summary>Links wanted (0: one per ~350 m of outline, 1–8).</summary>
            public int Wanted;
            /// <summary>Smallest distance between two links' ends on the plan side (m).</summary>
            public float Spacing = 110f;
        }

        public sealed class Result
        {
            public int Links, OnJunction, Continued, Candidates;
            public readonly List<float2> At = new List<float2>();
        }

        private sealed class XNode
        {
            public float2 P;
            public readonly List<(float2 dir, int road, bool start)> Out = new List<(float2, int, bool)>();
            public bool Junction => Out.Count >= 3;
        }

        private sealed class Cand
        {
            public float Cost;
            // plan side: a node, or a point on an edge
            public int PlanNode = -1, PlanEdge = -1;
            public float PlanT;
            public float3 P;
            // city side: an existing node (index into nodes), or a point on a road
            public int CityNode = -1, CityRoad = -1;
            public float CityT;
            public float3 Q;
            public bool Continued, OnJunction;
        }

        private const float MinLength = 6f;
        private const float JunctionClear = RoadTopology.MinJunctionGap;
        private const float PlanJunctionClear = 30f;

        public static Result Plan(RoadNetwork net, Options o)
        {
            var res = new Result();
            if (o.Existing == null || o.Existing.Count == 0 || o.Boundary == null || o.Boundary.Length < 3) return res;
            var nodes = Nodes(o.Existing);
            int wanted = o.Wanted > 0 ? o.Wanted : math.clamp((int)math.round(Poly.Perimeter(o.Boundary) / 350f), 1, 8);
            // the plan's existing joins count (streets already ending on a city road)
            var taken = new List<float2>();
            {
                var inc0 = RoadGraph.Incidence(net, RoadGraph.Car);
                for (int n = 0; n < net.Nodes.Count; n++)
                    if (net.Nodes[n].Attach >= 0 && inc0[n].Count > 0) taken.Add(net.Nodes[n].Position.xz);
            }
            var cityTaken = new List<float2>(taken);
            // Beyond the links wanted, a street that runs straight on into an existing junction (a clean four-way crossing
            // with the city's own street) still joins, every ~90 m: a grid inside a city stays as permeable as the city.
            int perfectMax = math.clamp((int)math.round(Poly.Perimeter(o.Boundary) / 180f), wanted, 16);
            for (int guard = 0; guard < 24; guard++)
            {
                bool extra = CountSpread(taken, o.Spacing) >= wanted;
                if (extra && res.Links + CountSpread(taken, o.Spacing) >= wanted + perfectMax) break;
                var cands = Candidates(net, o, nodes);
                res.Candidates = math.max(res.Candidates, cands.Count);
                cands.Sort((a, b) => a.Cost.CompareTo(b.Cost));
                Cand pick = null;
                foreach (var c in cands)
                {
                    // beyond the links wanted: a perfect continuation, or the first join on a stretch of outline with none
                    if (extra && !(c.Continued && c.OnJunction))
                    {
                        bool lonely = true;
                        foreach (var t in taken) if (math.distance(t, c.P.xz) < 220f) { lonely = false; break; }
                        if (!lonely) continue;
                    }
                    float spacing = extra ? math.min(o.Spacing, 90f) : o.Spacing;
                    bool far = true;
                    foreach (var t in taken) if (math.distance(t, c.P.xz) < spacing || math.distance(t, c.Q.xz) < spacing) { far = false; break; }
                    if (!far) continue;
                    foreach (var t in cityTaken) if (math.distance(t, c.Q.xz) < JunctionClear) { far = false; break; }
                    if (!far) continue;
                    pick = c;
                    break;
                }
                if (pick == null) break;
                Apply(net, o, nodes, pick);
                taken.Add(pick.P.xz);
                cityTaken.Add(pick.Q.xz);
                res.Links++;
                res.At.Add(pick.Q.xz);
                if (pick.OnJunction) res.OnJunction++;
                if (pick.Continued) res.Continued++;
            }
            return res;
        }

        /// <summary>Joins already spread out (one per <paramref name="spacing"/>): what counts against the links wanted.</summary>
        private static int CountSpread(List<float2> at, float spacing)
        {
            var kept = new List<float2>();
            foreach (var p in at)
            {
                bool near = false;
                foreach (var k in kept) if (math.distance(k, p) < spacing) { near = true; break; }
                if (!near) kept.Add(p);
            }
            return kept.Count;
        }

        private static List<XNode> Nodes(List<ExistingRoad> roads)
        {
            var nodes = new List<XNode>();
            int At(float2 p)
            {
                for (int i = 0; i < nodes.Count; i++) if (math.distance(nodes[i].P, p) <= 1f) return i;
                nodes.Add(new XNode { P = p });
                return nodes.Count - 1;
            }
            for (int r = 0; r < roads.Count; r++)
            {
                var c = roads[r].Curve;
                nodes[At(c.A.xz)].Out.Add((c.TangentXZ(0f), r, true));
                nodes[At(c.D.xz)].Out.Add((-c.TangentXZ(1f), r, false));
            }
            return nodes;
        }

        // ───────────────────────────── candidates ─────────────────────────────

        private static List<Cand> Candidates(RoadNetwork net, Options o, List<XNode> nodes)
        {
            var list = new List<Cand>();
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var all = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            var cars = new RoadGraph.EdgeIndex(net, RoadGraph.Car);
            var road = RoadLines(o.Existing);
            float2 centre = Poly.Centroid(o.Boundary);

            // (1) a street inside continues straight out through the perimeter
            for (int u = 0; u < net.Nodes.Count; u++)
            {
                if (inc[u].Count == 0 || inc[u].Count > 3 || RoadConnectivity.Attached(net.Nodes[u])) continue;
                float2 pu = net.Nodes[u].Position.xz;
                if (Poly.DistanceToBoundary(o.Boundary, pu) > 12f) continue;
                foreach (int ei in inc[u])
                {
                    var e = net.Edges[ei];
                    if (e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Roundabout) continue;
                    float2 dir = -RoadGraph.Leaving(e, u);      // the street's direction arriving here, continued
                    if (Poly.Contains(o.Boundary, pu + dir * 6f) && Poly.DistanceToBoundary(o.Boundary, pu + dir * 6f) > 3f) continue;   // points inward
                    if (!Ray(road, pu + dir * 1f, dir, o.Reach, out int ri, out float rt, out float d)) continue;
                    var c = new Cand { PlanNode = u, P = net.Nodes[u].Position, Continued = true };
                    if (!CityEnd(o, nodes, ri, rt, pu, dir, d, c)) continue;
                    if (Check(net, o, inc, all, c, out float cost)) { c.Cost = cost; list.Add(c); }
                }
            }

            // (2) an existing street ending at the area (or a T with its free side facing it) reaches in
            for (int k = 0; k < nodes.Count; k++)
            {
                var n = nodes[k];
                if (n.Out.Count == 0 || n.Out.Count > 3) continue;
                if (Poly.Contains(o.Boundary, n.P) || Poly.DistanceToBoundary(o.Boundary, n.P) > o.Reach) continue;
                foreach (var (ldir, ri, _) in n.Out)
                {
                    if (o.Existing[ri].Rank == RoadRank.Highway) continue;
                    float2 dir = -ldir;   // the street continued beyond this node
                    bool free = true;
                    foreach (var (d2, r2, _) in n.Out) if (r2 != ri && math.dot(d2, dir) > 0.94f) free = false;
                    if (!free) continue;
                    if (n.Out.Count == 2) continue;   // a bend of one road: no side to reach in from
                    if (!cars.Ray(n.P + dir * 1f, dir, 1f, o.Reach, null, out int fe, out float fd, out float2 hit)) continue;
                    var f = net.Edges[fe];
                    f.Curve.DistanceXZ(hit, out float ft);
                    var c = new Cand { CityNode = k, Q = new float3(n.P.x, 0f, n.P.y), OnJunction = n.Out.Count >= 2 };
                    c.Q.y = HeightOfNode(o, n);
                    // land on the plan's junction near the hit (an inside street meeting the perimeter: a four-way crossing)
                    int best = -1; float bd = math.min(18f, math.max(4f, fd * 0.18f));   // at most ~10 degrees off the street's line
                    foreach (int end in new[] { f.N0, f.N1 })
                    {
                        float dd = math.distance(net.Nodes[end].Position.xz, hit);
                        if (dd < bd && inc[end].Count >= 2 && inc[end].Count <= 3 && !RoadConnectivity.Attached(net.Nodes[end])) { bd = dd; best = end; }
                    }
                    if (best >= 0)
                    {
                        c.PlanNode = best;
                        c.P = net.Nodes[best].Position;
                        // it continues an inside street when that street leaves the node straight on
                        foreach (int ej in inc[best])
                            if (net.Edges[ej].Role != NetRole.PerimeterRoad && RoadGraph.Angle(RoadGraph.Leaving(net.Edges[ej], best), math.normalizesafe(net.Nodes[best].Position.xz - n.P)) < 4f) c.Continued = true;
                    }
                    else
                    {
                        c.PlanEdge = fe; c.PlanT = ft; c.P = f.Curve.Position(ft);
                    }
                    if (Check(net, o, inc, all, c, out float cost)) { c.Cost = cost; list.Add(c); }
                }
            }
            return list;
        }

        private static float HeightOfNode(Options o, XNode n)
        {
            var (_, r, start) = n.Out[0];
            return start ? o.Existing[r].Curve.A.y : o.Existing[r].Curve.D.y;
        }

        private static List<List<float2>> RoadLines(List<ExistingRoad> roads)
        {
            var lines = new List<List<float2>>();
            var pts = new List<float3>();
            foreach (var r in roads)
            {
                pts.Clear();
                r.Curve.Flatten(pts, null, 3f, 4f);
                var l = new List<float2>(pts.Count);
                foreach (var p in pts) l.Add(p.xz);
                lines.Add(l);
            }
            return lines;
        }

        /// <summary>First existing road hit by the ray: road index, its curve parameter, the distance.</summary>
        private static bool Ray(List<List<float2>> lines, float2 p, float2 dir, float reach, out int road, out float t, out float dist)
        {
            road = -1; t = 0f; dist = float.MaxValue;
            float2 q = p + dir * reach;
            for (int r = 0; r < lines.Count; r++)
            {
                var l = lines[r];
                for (int k = 0; k + 1 < l.Count; k++)
                {
                    if (!Seg.Intersect(p, q, l[k], l[k + 1], out float ta, out float tb)) continue;
                    float d = ta * reach;
                    if (d >= dist) continue;
                    dist = d; road = r;
                    t = (k + tb) / (l.Count - 1);
                }
            }
            return road >= 0;
        }

        /// <summary>
        /// The city end of a link continuing a street: an existing node of the road it meets when one is within 18 m of the
        /// line (a junction, or an end), else the point on the road (refining the parameter on the curve).
        /// </summary>
        private static bool CityEnd(Options o, List<XNode> nodes, int ri, float approxT, float2 from, float2 dir, float dist, Cand c)
        {
            var r = o.Existing[ri];
            if (r.Rank == RoadRank.Highway) return false;
            float2 hit = from + dir * dist;
            r.Curve.DistanceXZ(hit, out float t);
            int best = -1; float bd = math.min(18f, math.max(4f, dist * 0.18f));   // at most ~10 degrees off the street's line
            for (int k = 0; k < nodes.Count; k++)
            {
                bool onRoad = false;
                foreach (var (_, rr, _) in nodes[k].Out) if (rr == ri) onRoad = true;
                if (!onRoad) continue;
                float d = math.distance(nodes[k].P, hit);
                if (d < bd) { bd = d; best = k; }
            }
            if (best >= 0)
            {
                c.CityNode = best;
                c.Q = new float3(nodes[best].P.x, HeightOfNode(o, nodes[best]), nodes[best].P.y);
                c.OnJunction = nodes[best].Out.Count >= 2;
                // a link bent off the street's line (to reach the junction) no longer continues it
                if (bd > math.max(1.5f, dist * 0.07f)) c.Continued = false;
                return true;
            }
            c.CityRoad = ri;
            c.CityT = t;
            c.Q = r.Curve.Position(t);
            return true;
        }

        /// <summary>Is the link acceptable, and its cost (lower is better).</summary>
        private static bool Check(RoadNetwork net, Options o, List<int>[] inc, RoadGraph.EdgeIndex all, Cand c, out float cost)
        {
            cost = float.MaxValue;
            float2 a = c.P.xz, b = c.Q.xz;
            float len = math.distance(a, b);
            if (len < MinLength || len > o.Reach) return false;
            float2 dir = (b - a) / len;
            var curve = RoadCurve.Straight(c.P, c.Q);
            var line = RoadGraph.Sample(curve, 3f);
            // nothing crossed: the plan (away from the link's own start), the city (away from its end), the barriers
            var ignore = new HashSet<int>();
            if (c.PlanNode >= 0) foreach (int k in inc[c.PlanNode]) ignore.Add(k);
            if (c.PlanEdge >= 0) ignore.Add(c.PlanEdge);
            // (no end skip: a street end a metre short of the perimeter must not send its link across it)
            if (all.Crosses(line, 0.05f, ignore)) return false;
            if (RoadConnections.CrossesAny(line, o.Barriers, 0.5f)) return false;
            if (CrossesCity(line, o.Existing, c)) return false;
            float hw = o.HalfWidth(NetRole.Street);
            if (all.Clearance(line, hw + 10f, hw * 2f + 4f, ignore) < hw * 2f + 2f) return false;
            if (o.Obstacles != null && o.Obstacles.Blocked(curve, hw + 1f)) return false;
            // the link runs outside the area (a link inside is a street, the generators' business)
            int inside = 0;
            foreach (var p in line) if (Poly.Contains(o.Boundary, p) && Poly.DistanceToBoundary(o.Boundary, p) > 4f) inside++;
            if (inside > line.Count / 3) return false;
            float grade = RoadGraph.MaxGrade(curve, o.Height);
            if (grade > o.MaxGrade * 1.3f) return false;
            // square at the plan end
            float planAngle = 90f;
            if (c.PlanNode >= 0)
            {
                if (inc[c.PlanNode].Count >= 4) return false;
                planAngle = RoadGraph.MinAngleAt(net, inc, c.PlanNode, dir);
                if (planAngle < 60f) return false;
            }
            else
            {
                var f = net.Edges[c.PlanEdge];
                float fl = RoadGraph.Length(f);
                if (c.PlanT * fl < PlanJunctionClear || (1f - c.PlanT) * fl < PlanJunctionClear)
                {
                    // too close to a node of the plan: only a plain bend may take it (handled as a node candidate)
                    return false;
                }
                planAngle = 90f - math.degrees(math.asin(math.saturate(math.abs(math.dot(dir, f.Curve.TangentXZ(c.PlanT))))));
                if (planAngle < 65f) return false;
            }
            // square at the city end; never crowding a junction it does not land on
            float cityAngle;
            var nodesNear = new List<float2>();
            if (c.CityNode >= 0)
            {
                cityAngle = 180f;
                // the existing node's directions (a dead end continued straight is the ideal)
                float2 back = -dir;
                var dirs = DirsAt(o.Existing, b);
                if (dirs.Count >= 4) return false;
                foreach (var d in dirs) cityAngle = math.min(cityAngle, RoadGraph.Angle(d, back));
                if (cityAngle < 55f) return false;
            }
            else
            {
                var r = o.Existing[c.CityRoad];
                float2 tan = r.Curve.TangentXZ(c.CityT);
                cityAngle = 90f - math.degrees(math.asin(math.saturate(math.abs(math.dot(dir, tan)))));
                if (cityAngle < 65f) return false;
                // clear of the junctions on that road (and every junction near), the spacing on big roads
                float clear = r.Rank >= RoadRank.Medium ? math.max(JunctionClear, o.JunctionSpacing * 0.5f) : JunctionClear;
                foreach (var x in o.Existing)
                    foreach (var p in new[] { x.Curve.A.xz, x.Curve.D.xz })
                    {
                        float dd = math.distance(p, b);
                        if (dd > 0.5f && dd < clear && DirsAt(o.Existing, p).Count >= 3) return false;
                        if (dd < 12f) return false;   // a few metres off a node: should have landed on it
                    }
            }
            // cost: short, in line with a street inside, onto an existing junction, square, gentle
            cost = len;
            if (c.Continued) cost -= 60f;
            if (c.OnJunction) cost -= 40f;
            cost += 1.2f * (90f - math.min(90f, planAngle)) + 1.2f * (90f - math.min(90f, cityAngle));
            if (grade > o.MaxGrade) cost += len * 6f * (grade - o.MaxGrade) / o.MaxGrade;
            if (c.CityRoad >= 0 && o.Existing[c.CityRoad].Rank >= RoadRank.Major) cost += 25f;   // a local link onto a major road costs
            return true;
        }

        /// <summary>Directions of the existing roads leaving point p (their ends at p, or both ways along one passing it).</summary>
        private static List<float2> DirsAt(List<ExistingRoad> roads, float2 p)
        {
            var dirs = new List<float2>();
            foreach (var r in roads)
            {
                if (math.distance(r.Curve.A.xz, p) < 1f) { dirs.Add(r.Curve.TangentXZ(0f)); continue; }
                if (math.distance(r.Curve.D.xz, p) < 1f) { dirs.Add(-r.Curve.TangentXZ(1f)); continue; }
                float d = r.Curve.DistanceXZ(p, out float t);
                if (d < 1f) { var tan = r.Curve.TangentXZ(t); dirs.Add(tan); dirs.Add(-tan); }
            }
            return dirs;
        }

        /// <summary>The link crosses an existing road anywhere but at its own city end.</summary>
        private static bool CrossesCity(List<float2> line, List<ExistingRoad> roads, Cand c)
        {
            var pts = new List<float3>();
            float2 end = line[line.Count - 1], start = line[0];
            foreach (var r in roads)
            {
                pts.Clear();
                r.Curve.Flatten(pts, null, 3f, 5f);
                for (int k = 0; k + 1 < pts.Count; k++)
                    for (int i = 0; i + 1 < line.Count; i++)
                        if (Seg.Intersect(line[i], line[i + 1], pts[k].xz, pts[k + 1].xz, out float ta, out _))
                        {
                            float2 hit = math.lerp(line[i], line[i + 1], ta);
                            if (math.distance(hit, end) > 1.5f && math.distance(hit, start) > 1.5f) return true;
                        }
            }
            return false;
        }

        // ───────────────────────────── apply ─────────────────────────────

        private static void Apply(RoadNetwork net, Options o, List<XNode> nodes, Cand c)
        {
            float3 p = c.P;
            if (c.PlanNode < 0)
            {
                RoadGraph.Split(net, c.PlanEdge, c.PlanT);
                net.Weld();
                int n = RoadGraph.NodeAt(net, c.P.xz, 0.5f);
                if (n >= 0) p = net.Nodes[n].Position;
            }
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(p, c.Q), NetRole.Street) { Connector = true });
            net.Weld();
            int q = RoadGraph.NodeAt(net, c.Q.xz, 0.1f);
            if (q < 0) return;
            var node = net.Nodes[q];
            if (c.CityNode >= 0)
            {
                var (_, ri, start) = nodes[c.CityNode].Out[0];
                node.Attach = o.Existing[ri].Id;
                node.AttachT = start ? 0f : 1f;
                node.Position = start ? o.Existing[ri].Curve.A : o.Existing[ri].Curve.D;
            }
            else
            {
                node.Attach = o.Existing[c.CityRoad].Id;
                node.AttachT = c.CityT;
                node.Position = o.Existing[c.CityRoad].Curve.Position(c.CityT);
            }
            foreach (var e in net.Edges)
            {
                if (e.Discarded) continue;
                if (e.N0 == q) e.Curve = e.Curve.WithEnds(node.Position, e.Curve.D);
                if (e.N1 == q) e.Curve = e.Curve.WithEnds(e.Curve.A, node.Position);
            }
        }
    }
}
