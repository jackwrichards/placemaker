// Placemaker roads layer (s34) — the connectivity guarantee.
//
// Playtest (images 59/60): a "village" on a hill left whole street systems and single streets floating, connected to
// nothing (roads around a ruin were removed by obstacle avoidance; the old rule kept "whole networks" on purpose).
// Now every car component of the plan is linked: to the city (the existing roads the plan attaches to) and to each
// other, by the shortest clean link — no crossing, clear of other roads, around obstacles, inside the area, at a
// sane angle, not too steep, and never onto a main road between its managed junctions. A piece that cannot be linked
// is dropped (Info issue). Then dead ends: short stubs go, long unplanned dead ends become proper cul-de-sacs.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadConnectivity
    {
        public sealed class Options
        {
            public float2[] Boundary = Array.Empty<float2>();
            public List<ExistingRoad> Existing = new List<ExistingRoad>();
            /// <summary>Existing roads no link may cross (highways, ramps, roads the player keeps free).</summary>
            public List<ExistingRoad> Barriers = new List<ExistingRoad>();
            public RoadObstacles Obstacles;
            public Func<float2, float> Height;
            public Func<NetRole, float> HalfWidth = r => 8f;
            /// <summary>Steepest acceptable link (rise/run); links up to 1.3× this are taken when nothing better exists.</summary>
            public float MaxGrade = 0.1f;
            /// <summary>Longest link between two parts of the plan (m).</summary>
            public float MaxLink = 220f;
            /// <summary>Longest link from the plan to an existing road (m).</summary>
            public float MaxCityLink = 160f;
            /// <summary>Access management: may a link join this edge at this point (null = anywhere).</summary>
            public Func<RoadEdge, float2, bool> CanJoin;
            /// <summary>The plan has a perimeter road: no dead end is kept as a stub-out at the outline.</summary>
            public bool Perimeter;
            /// <summary>Dead ends shorter than this (m, junction to tip) are removed; longer ones become cul-de-sacs.</summary>
            public float MinCulDeSac = 40f;
            /// <summary>Role of a new link between two pieces.</summary>
            public Func<RoadEdge, RoadEdge, NetRole> LinkRole = (a, b) => NetRole.Street;
            /// <summary>Drop pieces that cannot be linked (false: leave them, the caller checks — access management trials).</summary>
            public bool Drop = true;
            /// <summary>Links to the city wanted (more for big plans), each this far from the others along the outline.</summary>
            public int CityLinks = 1;
            public float CityLinkSpacing = 220f;
            /// <summary>Second chance before a piece is dropped: longer, steeper (scored), tighter links (set by Ensure).</summary>
            public bool Relaxed;
            public float LinkReach => Relaxed ? MaxLink * 1.6f : MaxLink;
            public float GradeCap => Relaxed ? math.max(0.25f, MaxGrade * 2.5f) : MaxGrade * 1.3f;
            public float AngleMin => Relaxed ? 48f : 60f;
        }

        public sealed class Result
        {
            public int Links, CityLinks, Dropped, Capped, Pruned;
            public float DroppedLength;
            public List<float2> DroppedAt = new List<float2>();
            /// <summary>Existing roads were near but the plan could not be linked to them.</summary>
            public bool CityUnreached;
        }

        private const float JunctionGap = 22f;
        private const float MinLink = 10f;

        public static bool Attached(RoadNode n) => n.Attach >= 0 || n.Anchored;

        public static Result Ensure(RoadNetwork net, Options o, Result res = null)
        {
            res = res ?? new Result();
            for (int round = 0; round < 96; round++)
            {
                var comp = RoadGraph.Components(net, RoadGraph.Car, out int count);
                if (count == 0) break;
                var anchored = new bool[count];
                var length = new float[count];
                for (int n = 0; n < comp.Length; n++)
                    if (comp[n] >= 0 && Attached(net.Nodes[n])) anchored[comp[n]] = true;
                foreach (var e in net.Edges)
                    if (e.N0 >= 0 && RoadGraph.Car(e)) length[comp[e.N0]] += e.Curve.LengthXZ(6);
                bool anyAnchored = false;
                foreach (bool a in anchored) anyAnchored |= a;
                var root = (bool[])anchored.Clone();
                if (!anyAnchored)
                {
                    int big = ArgMax(length);
                    if (o.Existing.Count > 0)
                    {
                        if (LinkToCity(net, o, comp, big)) { res.CityLinks++; continue; }
                        res.CityUnreached = CityNear(net, o, comp, big);
                    }
                    root[big] = true;
                }
                var order = new List<int>();
                for (int c = 0; c < count; c++) if (!root[c]) order.Add(c);
                if (order.Count == 0) break;
                order.Sort((a, b) => length[b].CompareTo(length[a]));
                bool changed = false;
                foreach (int c in order)
                {
                    if (TryLink(net, o, comp, c, k => root[k])) { res.Links++; changed = true; break; }
                    if (o.Existing.Count > 0 && LinkToCity(net, o, comp, c)) { res.CityLinks++; changed = true; break; }
                }
                if (changed) continue;
                // Second chance (a piece is worth a longer or steeper link more than being dropped).
                o.Relaxed = true;
                foreach (int c in order)
                {
                    if (TryLink(net, o, comp, c, k => root[k])) { res.Links++; changed = true; break; }
                    if (o.Existing.Count > 0 && LinkToCity(net, o, comp, c)) { res.CityLinks++; changed = true; break; }
                }
                o.Relaxed = false;
                if (changed) continue;
                // Nothing reaches the network or the city directly: join two loose pieces, they try again as one.
                foreach (int c in order)
                    if (TryLink(net, o, comp, c, k => !root[k] && k != c)) { res.Links++; changed = true; break; }
                if (changed) continue;
                if (!o.Drop) break;
                foreach (var e in net.Edges)
                {
                    if (e.N0 < 0 || !RoadGraph.Car(e) || root[comp[e.N0]]) continue;
                    e.Removed = true;
                    res.Dropped++;
                    res.DroppedLength += e.Curve.LengthXZ(6);
                    res.DroppedAt.Add(e.Curve.Position(0.5f).xz);
                }
                break;
            }
            // Walkways and car-free pieces left hanging off a dropped piece go too.
            DropOrphanPaths(net);
            if (o.Drop) MoreCityLinks(net, o, res);
            return res;
        }

        /// <summary>
        /// A big plan joins the city in more than one place (a single way in is a bottleneck and a long detour): up to
        /// <see cref="Options.CityLinks"/> connections, each <see cref="Options.CityLinkSpacing"/> from the others.
        /// </summary>
        private static void MoreCityLinks(RoadNetwork net, Options o, Result res)
        {
            if (o.Existing.Count == 0 || o.CityLinks <= 1) return;
            for (int k = 0; k < 6; k++)
            {
                var at = new List<float2>();
                foreach (var n in net.Nodes) if (n.Attach >= 0) at.Add(n.Position.xz);
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                int linked = 0;
                for (int i = 0; i < net.Nodes.Count; i++) if (net.Nodes[i].Attach >= 0 && inc[i].Count > 0) linked++;
                if (linked >= o.CityLinks) return;
                var comp = RoadGraph.Components(net, RoadGraph.Car, out _);
                if (!LinkToCity(net, o, comp, -1, q =>
                {
                    foreach (var p in at) if (math.distance(p, q) < o.CityLinkSpacing) return false;
                    return true;
                })) return;
                res.CityLinks++;
            }
        }

        private static int ArgMax(float[] v)
        {
            int best = 0;
            for (int i = 1; i < v.Length; i++) if (v[i] > v[best]) best = i;
            return best;
        }

        /// <summary>
        /// Car-free pieces with a free end (a superblock path cut by an obstacle, a walk whose road went) are removed, chain
        /// by chain: CS2 removes stray path stubs itself, and a path to nowhere is not a walkway.
        /// </summary>
        public static int PruneLoosePaths(RoadNetwork net)
        {
            int removed = 0;
            for (int pass = 0; pass < 64; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Any);
                bool any = false;
                foreach (var e in net.Edges)
                {
                    if (e.N0 < 0 || !e.Alive || e.IsCar) continue;
                    bool free0 = inc[e.N0].Count == 1 && !Attached(net.Nodes[e.N0]);
                    bool free1 = inc[e.N1].Count == 1 && !Attached(net.Nodes[e.N1]);
                    if (!free0 && !free1) continue;
                    e.Removed = true;
                    removed++;
                    any = true;
                }
                if (!any) break;
            }
            return removed;
        }

        /// <summary>Car-free edges that touch no car road any more (directly or through other paths) are removed.</summary>
        public static void DropOrphanPaths(RoadNetwork net)
        {
            var comp = RoadGraph.Components(net, RoadGraph.Any, out int count);
            if (count == 0) return;
            var hasCar = new bool[count];
            foreach (var e in net.Edges)
                if (e.N0 >= 0 && RoadGraph.Car(e)) hasCar[comp[e.N0]] = true;
            foreach (var e in net.Edges)
                if (e.N0 >= 0 && e.Alive && !e.IsCar && !hasCar[comp[e.N0]] && !Attached(net.Nodes[e.N0]) && !Attached(net.Nodes[e.N1])) e.Removed = true;
        }

        private struct Candidate
        {
            public float Score;
            public int U;
            public int Edge;        // target edge (generated) or -1
            public int V;           // target node or -1
            public float T;         // split parameter on the target edge
            public float3 Q;
            public int Existing;    // index into Options.Existing, -1
            public float ExistingT;
        }

        /// <summary>Links component <paramref name="c"/> to a component the predicate accepts by the best clean straight link.</summary>
        private static bool TryLink(RoadNetwork net, Options o, int[] comp, int c, Func<int, bool> target)
        {
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var all = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            var targets = new RoadGraph.EdgeIndex(net, e => RoadGraph.Car(e) && target(comp[e.N0]));
            var best = new Candidate { Score = float.MaxValue, Edge = -1, V = -1, Existing = -1 };
            var near = new HashSet<int>();
            var ignore = new HashSet<int>();
            for (int u = 0; u < net.Nodes.Count; u++)
            {
                if (comp[u] != c || inc[u].Count == 0 || inc[u].Count > 3) continue;
                var nu = net.Nodes[u];
                float2 pu = nu.Position.xz;
                if (!MayBranch(net, o, inc, u)) continue;
                near.Clear();
                targets.Near(pu, o.LinkReach, near);
                foreach (int fi in near)
                {
                    var f = net.Edges[fi];
                    float d = f.Curve.DistanceXZ(pu, out float t);
                    if (d > o.LinkReach || d < MinLink) continue;
                    float len = RoadGraph.Length(f);
                    float s = t * len;
                    int v = -1;
                    if (s < JunctionGap) v = f.N0;
                    else if (len - s < JunctionGap) v = f.N1;
                    if (v >= 0 && (inc[v].Count >= 4)) continue;
                    float3 q = v >= 0 ? net.Nodes[v].Position : f.Curve.Position(t);
                    float dist = math.distance(pu, q.xz);
                    if (dist < MinLink || dist > o.LinkReach) continue;
                    float2 dir = (q.xz - pu) / dist;
                    if (RoadGraph.MinAngleAt(net, inc, u, dir) < o.AngleMin) continue;
                    if (v >= 0)
                    {
                        if (RoadGraph.MinAngleAt(net, inc, v, -dir) < o.AngleMin - 5f) continue;
                    }
                    else
                    {
                        float cos = math.abs(math.dot(dir, f.Curve.TangentXZ(t)));
                        if (cos > 0.5f) continue;   // the link must meet the road at 60–120°
                        if (o.CanJoin != null && !o.CanJoin(f, q.xz)) continue;
                    }
                    ignore.Clear();
                    foreach (int k in inc[u]) ignore.Add(k);
                    ignore.Add(fi);
                    if (v >= 0) foreach (int k in inc[v]) ignore.Add(k);
                    float score = Evaluate(net, o, all, nu.Position, q, ignore, dist, inc[u].Count);
                    if (score >= best.Score) continue;
                    best = new Candidate { Score = score, U = u, Edge = fi, V = v, T = t, Q = q, Existing = -1 };
                }
            }
            if (best.Score == float.MaxValue) return false;
            var eu = inc[best.U].Count > 0 ? net.Edges[inc[best.U][0]] : null;
            var role = o.LinkRole(eu, net.Edges[best.Edge]);
            float3 a = net.Nodes[best.U].Position;
            if (best.V < 0) RoadGraph.Split(net, best.Edge, best.T);
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(a, best.Q), role) { Connector = true });
            net.Weld();
            return true;
        }

        /// <summary>A new link may leave node u: a node on a main road obeys the junction spacing (access management).</summary>
        private static bool MayBranch(RoadNetwork net, Options o, List<int>[] inc, int u)
        {
            if (o.CanJoin == null || inc[u].Count < 2) return true;
            foreach (int ei in inc[u])
                if (RoadAccess.IsMain(net.Edges[ei]) && !o.CanJoin(net.Edges[ei], net.Nodes[u].Position.xz)) return false;
            return true;
        }

        /// <summary>Score of a link a→q (lower is better), float.MaxValue when it is not acceptable.</summary>
        private static float Evaluate(RoadNetwork net, Options o, RoadGraph.EdgeIndex all, float3 a, float3 q, ICollection<int> ignore, float dist, int degreeU)
        {
            var curve = RoadCurve.Straight(a, q);
            var line = RoadGraph.Sample(curve, 4f);
            if (all.Crosses(line, 1.5f, ignore)) return float.MaxValue;
            float hw = o.HalfWidth(NetRole.Street);
            float clearance = all.Clearance(line, hw + 12f, hw * 2f + 4f, ignore);
            if (clearance < hw * 2f + (o.Relaxed ? -2f : 2f)) return float.MaxValue;
            if (o.Obstacles != null && o.Obstacles.Blocked(curve, o.HalfWidth(NetRole.Street) + 1f)) return float.MaxValue;
            if (o.Boundary != null && o.Boundary.Length >= 3)
                foreach (var p in line)
                    if (!Poly.Contains(o.Boundary, p) && Poly.DistanceToBoundary(o.Boundary, p) > 3f) return float.MaxValue;
            if (CrossesExisting(line, o.Existing, -1) || RoadConnections.CrossesAny(line, o.Barriers)) return float.MaxValue;
            float grade = RoadGraph.MaxGrade(curve, o.Height);
            if (grade > o.GradeCap) return float.MaxValue;
            float score = dist;
            if (grade > o.MaxGrade) score *= 1f + 6f * (grade - o.MaxGrade) / o.MaxGrade;
            if (degreeU > 1) score += 30f;   // a dead end continuing is the natural link; a new junction costs
            return score;
        }

        private static bool CrossesExisting(List<float2> line, List<ExistingRoad> existing, int except)
        {
            if (existing == null) return false;
            var pts = new List<float3>();
            for (int r = 0; r < existing.Count; r++)
            {
                if (r == except) continue;
                pts.Clear();
                existing[r].Curve.Flatten(pts, null, 4f, 6f);
                for (int k = 0; k + 1 < pts.Count; k++)
                    for (int i = 0; i + 1 < line.Count; i++)
                        if (Seg.Intersect(line[i], line[i + 1], pts[k].xz, pts[k + 1].xz, out float ta, out _))
                        {
                            // Touching at the link's own end is the join itself.
                            bool atEnd = (i == line.Count - 2 && ta > 0.98f) || (i == 0 && ta < 0.02f);
                            if (!atEnd) return true;
                        }
            }
            return false;
        }

        /// <summary>Every existing road leaving point p makes at least <paramref name="minAngle"/>° with <paramref name="dir"/>.</summary>
        private static bool ClearOfCityArms(List<ExistingRoad> roads, float2 p, float2 dir, float minAngle)
        {
            foreach (var r in roads)
            {
                if (math.distance(r.Curve.A.xz, p) < 1f && RoadGraph.Angle(r.Curve.TangentXZ(0f), dir) < minAngle) return false;
                if (math.distance(r.Curve.D.xz, p) < 1f && RoadGraph.Angle(-r.Curve.TangentXZ(1f), dir) < minAngle) return false;
            }
            return true;
        }

        private static bool CityNear(RoadNetwork net, Options o, int[] comp, int c)
        {
            for (int u = 0; u < net.Nodes.Count; u++)
            {
                if (comp[u] != c) continue;
                foreach (var r in o.Existing)
                    if (r.Curve.DistanceXZ(net.Nodes[u].Position.xz, out _) < o.MaxCityLink) return true;
            }
            return false;
        }

        /// <summary>Links component <paramref name="c"/> to the nearest existing road it can reach cleanly.</summary>
        private static bool LinkToCity(RoadNetwork net, Options o, int[] comp, int c, Func<float2, bool> targetOk = null, int onlyNode = -1, float maxDist = -1f)
        {
            float reach = maxDist > 0f ? maxDist : o.Relaxed ? o.MaxCityLink * 1.5f : o.MaxCityLink;
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var all = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            var best = new Candidate { Score = float.MaxValue, Edge = -1, V = -1, Existing = -1 };
            var ignore = new HashSet<int>();
            for (int u = 0; u < net.Nodes.Count; u++)
            {
                if (onlyNode >= 0 && u != onlyNode) continue;
                if ((c >= 0 && comp[u] != c) || comp[u] < 0 || inc[u].Count == 0 || inc[u].Count > 2 || Attached(net.Nodes[u])) continue;
                var nu = net.Nodes[u];
                float2 pu = nu.Position.xz;
                if (!MayBranch(net, o, inc, u)) continue;
                for (int r = 0; r < o.Existing.Count; r++)
                {
                    var road = o.Existing[r];
                    float d = road.Curve.DistanceXZ(pu, out float t);
                    if (d > reach) continue;
                    float len = road.Curve.LengthXZ(8);
                    if (d < MinLink)
                    {
                        // A dead end stopping a few metres short of a road (its perimeter piece went): it simply joins it.
                        if (inc[u].Count != 1 || (targetOk != null && !targetOk(road.Curve.Position(t).xz))) continue;
                        // (smarter roads) only a street arriving square joins this way (no knife edge onto the road)
                        float2 arriving = -RoadGraph.Leaving(net.Edges[inc[u][0]], u);
                        if (t > 0.01f && t < 0.99f && math.abs(math.dot(arriving, road.Curve.TangentXZ(t))) > 0.57f) continue;
                        if (t * len < 8f) t = 0f; else if ((1f - t) * len < 8f) t = 1f;
                        float3 qs = t <= 0f ? road.Curve.A : t >= 1f ? road.Curve.D : road.Curve.Position(t);
                        if (d < best.Score) best = new Candidate { Score = d, U = u, Q = qs, Existing = r, ExistingT = t, Edge = -1, V = -2 };
                        continue;
                    }
                    if (t * len < 14f) t = 0f; else if ((1f - t) * len < 14f) t = 1f;
                    float3 q = t <= 0f ? road.Curve.A : t >= 1f ? road.Curve.D : road.Curve.Position(t);
                    float dist = math.distance(pu, q.xz);
                    if (dist < MinLink || dist > reach) continue;
                    if (targetOk != null && !targetOk(q.xz)) continue;
                    float2 dir = (q.xz - pu) / dist;
                    if (RoadGraph.MinAngleAt(net, inc, u, dir) < o.AngleMin) continue;
                    if (t > 0f && t < 1f && math.abs(math.dot(dir, road.Curve.TangentXZ(t))) > 0.5f) continue;
                    // (smarter roads) onto a city node: square to every road already there (no knife edge)
                    if ((t <= 0f || t >= 1f) && !ClearOfCityArms(o.Existing, q.xz, -dir, 55f)) continue;
                    ignore.Clear();
                    foreach (int k in inc[u]) ignore.Add(k);
                    var curve = RoadCurve.Straight(nu.Position, q);
                    var line = RoadGraph.Sample(curve, 4f);
                    if (all.Crosses(line, 1.5f, ignore)) continue;
                    if (CrossesExisting(line, o.Existing, r) || RoadConnections.CrossesAny(line, o.Barriers)) continue;
                    float hw = o.HalfWidth(NetRole.Street);
                    if (all.Clearance(line, hw + 12f, hw * 2f + 4f, ignore) < hw * 2f + (o.Relaxed ? -2f : 2f)) continue;
                    if (o.Obstacles != null && o.Obstacles.Blocked(curve, o.HalfWidth(NetRole.Street) + 1f)) continue;
                    float grade = RoadGraph.MaxGrade(curve, o.Height);
                    if (grade > o.GradeCap) continue;
                    float score = dist * (grade > o.MaxGrade ? 1f + 6f * (grade - o.MaxGrade) / o.MaxGrade : 1f) + (inc[u].Count > 1 ? 30f : 0f);
                    if (score >= best.Score) continue;
                    best = new Candidate { Score = score, U = u, Q = q, Existing = r, ExistingT = t, Edge = -1, V = -1 };
                }
            }
            if (best.Score == float.MaxValue) return false;
            if (best.V == -2)
            {
                var node = net.Nodes[best.U];
                foreach (var e in net.Edges)
                {
                    if (e.N0 < 0 || e.Discarded) continue;
                    if (e.N0 == best.U) e.Curve = e.Curve.WithEnds(best.Q, e.Curve.D);
                    if (e.N1 == best.U) e.Curve = e.Curve.WithEnds(e.Curve.A, best.Q);
                }
                node.Position = best.Q;
                node.Attach = o.Existing[best.Existing].Id;
                node.AttachT = best.ExistingT;
                net.Weld();
                return true;
            }
            var eu = net.Edges[inc[best.U][0]];
            float3 a = net.Nodes[best.U].Position;
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(a, best.Q), o.LinkRole(eu, null)) { Connector = true });
            net.Weld();
            int qn = RoadGraph.NodeAt(net, best.Q.xz, 0.1f);
            if (qn >= 0)
            {
                var road = o.Existing[best.Existing];
                net.Nodes[qn].Attach = road.Id;
                net.Nodes[qn].AttachT = best.ExistingT;
                net.Nodes[qn].Position = best.Q;
            }
            return true;
        }

        /// <summary>
        /// Dead ends: a stub shorter than <see cref="Options.MinCulDeSac"/> (junction to tip) goes; a longer dead end that was not
        /// planned as a cul-de-sac gets a turning circle. With no perimeter road, ends on the outline stay as stub-outs.
        /// </summary>
        public static void CleanDeadEnds(RoadNetwork net, Options o, Result res)
        {
            for (int pass = 0; pass < 400; pass++)   // a city link restarts the pass (node indices change)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                bool changed = false;
                for (int n = 0; n < net.Nodes.Count; n++)
                {
                    if (inc[n].Count != 1 || Attached(net.Nodes[n])) continue;
                    var chain = new List<int>();
                    float length = 0f;
                    int cur = n, prev = -1, end = -1;
                    for (int guard = 0; guard < 400; guard++)
                    {
                        int ei = -1;
                        foreach (int k in inc[cur]) if (k != prev) { ei = k; break; }
                        if (ei < 0) { end = cur; break; }
                        chain.Add(ei);
                        length += net.Edges[ei].Curve.LengthXZ(8);
                        int nxt = RoadGraph.Other(net.Edges[ei], cur);
                        if (inc[nxt].Count != 2 || Attached(net.Nodes[nxt])) { end = nxt; break; }
                        prev = ei; cur = nxt;
                    }
                    if (end < 0 || chain.Count == 0) continue;
                    if (inc[end].Count == 1 && !Attached(net.Nodes[end])) continue;   // a loose piece: connectivity's business
                    var tip = net.Edges[chain[0]];
                    // An unplanned dead end near an existing road simply joins it (planned cul-de-sacs stay quiet).
                    if (o.Existing.Count > 0 && !(tip.CapAtEnd && tip.N1 == n))
                    {
                        var comp = RoadGraph.Components(net, RoadGraph.Car, out _);
                        if (LinkToCity(net, o, comp, -1, null, n, 70f)) { res.CityLinks++; changed = true; break; }
                    }
                    bool outline = !o.Perimeter && o.Boundary != null && o.Boundary.Length >= 3
                        && Poly.DistanceToBoundary(o.Boundary, net.Nodes[n].Position.xz) < 6f;
                    // (Smarter roads) No border: a street running out to the edge of the selection with nothing there is no
                    // stub-out looking for a border (the user). A long one becomes a cul-de-sac set back inside the edge
                    // (it serves the land there); a short one goes back to its last junction (a clean edge of the grid).
                    if (outline && length >= OutlineCulDeSac && !(tip.CapAtEnd && tip.N1 == n) && SetBack(net, chain[0], n, OutlineSetback))
                    {
                        changed = true;
                        res.Capped++;
                        break;
                    }
                    if (outline && length < OutlineCulDeSac)
                    {
                        foreach (int k in chain) net.Edges[k].Removed = true;
                        res.Pruned++;
                        changed = true;
                        continue;
                    }
                    if (length < o.MinCulDeSac)
                    {
                        foreach (int k in chain) net.Edges[k].Removed = true;
                        res.Pruned++;
                        changed = true;
                        continue;
                    }
                    if (!(tip.CapAtEnd && tip.N1 == n))
                    {
                        if (tip.N0 == n)
                        {
                            tip.Curve = tip.Curve.Reversed();
                            (tip.N0, tip.N1) = (tip.N1, tip.N0);
                        }
                        tip.CapAtEnd = true;
                        res.Capped++;
                    }
                }
                if (!changed) break;
            }
            // A cap flag on an end that is no longer a dead end does nothing (the planner caps degree-1 ends only).
            JoinFacingTips(net, o, res);
        }

        /// <summary>
        /// (Smarter roads) The last word on dead ends: every car road ending on nothing (not on the city) carries its turning
        /// circle at that end, whatever the passes after the dead-end pass did to its pieces (a short tip merged away, a piece
        /// split or turned). Returns the tips that had lost it.
        /// </summary>
        public static int CapAllDeadEnds(RoadNetwork net)
        {
            int fixedCount = 0;
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc[n].Count != 1 || Attached(net.Nodes[n])) continue;
                var e = net.Edges[inc[n][0]];
                if (e.Role == NetRole.Roundabout) continue;
                if (e.CapAtEnd && e.N1 == n) continue;
                if (e.N0 == n)
                {
                    if (e.CapAtEnd && inc[e.N1].Count == 1) continue;   // a lone piece capped at its other end
                    e.Curve = e.Curve.Reversed();
                    (e.N0, e.N1) = (e.N1, e.N0);
                }
                e.CapAtEnd = true;
                fixedCount++;
            }
            return fixedCount;
        }

        /// <summary>(Smarter roads) A street to the open edge this long (junction to tip) or longer ends in a cul-de-sac there.</summary>
        private const float OutlineCulDeSac = 70f;
        /// <summary>How far inside the edge such a cul-de-sac's tip stops (its turning circle stays inside).</summary>
        private const float OutlineSetback = 16f;

        /// <summary>Shortens the tip edge by <paramref name="back"/> m at dead end <paramref name="n"/> and caps the new tip (welds).</summary>
        private static bool SetBack(RoadNetwork net, int ei, int n, float back)
        {
            var e = net.Edges[ei];
            float len = RoadGraph.Length(e);
            if (len < back + 12f) return false;
            if (e.N0 == n)
            {
                e.Curve = e.Curve.Reversed();
                (e.N0, e.N1) = (e.N1, e.N0);
            }
            float t = RoadGraph.ParamAt(e.Curve, back, fromEnd: true);
            e.Curve.Split(math.clamp(t, 0.01f, 0.99f), out var keep, out _);
            e.Curve = keep;
            e.CapAtEnd = true;
            net.Weld();
            return true;
        }

        /// <summary>
        /// Two cul-de-sacs pointing at each other a few metres apart read as a street someone forgot to finish: join them
        /// (one link, no turning circles) when the link is clean.
        /// </summary>
        private static void JoinFacingTips(RoadNetwork net, Options o, Result res)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                var tips = new List<int>();
                for (int n = 0; n < net.Nodes.Count; n++)
                    if (inc[n].Count == 1 && !Attached(net.Nodes[n])) tips.Add(n);
                var all = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
                bool joined = false;
                for (int i = 0; i < tips.Count && !joined; i++)
                    for (int j = i + 1; j < tips.Count && !joined; j++)
                    {
                        int a = tips[i], b = tips[j];
                        float3 pa = net.Nodes[a].Position, pb = net.Nodes[b].Position;
                        float d = math.distance(pa.xz, pb.xz);
                        if (d < 4f || d > 60f) continue;
                        float2 dir = (pb.xz - pa.xz) / d;
                        // Each tip must point roughly at the other (its street arrives heading that way).
                        if (math.dot(-RoadGraph.Leaving(net.Edges[inc[a][0]], a), dir) < 0.5f) continue;
                        if (math.dot(-RoadGraph.Leaving(net.Edges[inc[b][0]], b), -dir) < 0.5f) continue;
                        var ignore = new HashSet<int> { inc[a][0], inc[b][0] };
                        if (Evaluate(net, o, all, pa, pb, ignore, d, 1) == float.MaxValue) continue;
                        var ea = net.Edges[inc[a][0]];
                        net.Edges.Add(new RoadEdge(RoadCurve.Straight(pa, pb), o.LinkRole(ea, net.Edges[inc[b][0]])) { Connector = true });
                        net.Weld();
                        res.Links++;
                        joined = true;
                    }
                if (!joined) break;
            }
        }
    }
}
