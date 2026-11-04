// Placemaker roads layer (s34) — the walking network and the small roads: walkways, back lanes, gravel tips.
//
// Playtest (image 63): the player drew the missing footpaths by hand — cul-de-sac bulbs to each other and to the
// next street. A designer does that from the start: every cul-de-sac gets a footpath to the nearest street or bulb
// it saves a long walk to, long blocks get a mid-block cut-through, side streets cut back from a main road keep a
// path to it, and grids can get back lanes (alleys or gravel lanes) through the middle of the blocks.
//
// Walkways join the roads at real nodes (a road edge is split there) — never free ends (CS2 removes stray path
// stubs) and never onto a turning circle (they leave the cul-de-sac at its neck). No walkway crosses a road, a lot
// or another walkway, comes close to another road along its length, or climbs more than 8 % (vanilla has no stairs
// net). Style: paved paths, dirt/gravel trails (a trail net when the playset has one, the footpath otherwise, with a
// worn-ground paint corridor), or a mix.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadWalkways
    {
        public const float MaxWalkGrade = 0.08f;

        public sealed class Options
        {
            /// <summary>0..1: how many walkways (0 = none).</summary>
            public float Density = 0.5f;
            /// <summary>"paved" | "trail" | "mixed".</summary>
            public string Style = "paved";
            public uint Seed = 1;
            public float2[] Boundary = Array.Empty<float2>();
            public Func<float2, float> Height;
            public RoadObstacles Obstacles;
            public Func<NetRole, float> HalfWidth = r => 8f;
            public float MaxGrade = MaxWalkGrade;
            /// <summary>Cul-de-sac turning circle radius by road width (the walk leaves at the neck, outside it).</summary>
            public Func<float, float> CapRadius = w => math.max(w * 0.8f, w * 0.5f + 4f);
            public List<RoadAccess.Cut> AccessCuts = new List<RoadAccess.Cut>();
        }

        public sealed class Result
        {
            public int CulDeSacLinks, CutThroughs, AccessLinks, Trails;
            public float Length;
        }

        private sealed class Plan
        {
            public float3 A, B;
            public int EdgeA = -1, EdgeB = -1;   // edge to split (or -1 = existing node)
            public float TA, TB;
            public float2 StartDir;
            public string Kind;
        }

        /// <summary>Adds the walkways to the network (role Pathway, Walk = true).</summary>
        public static Result Build(RoadNetwork net, Options o)
        {
            var res = new Result();
            if (!(o.Density > 0.001f)) return res;
            var rng = new Unity.Mathematics.Random(o.Seed == 0 ? 0x9E3779B9u : o.Seed);

            // 1) Side streets cut back from a main road keep their short cut on foot.
            foreach (var cut in o.AccessCuts)
            {
                if (AddWalk(net, o, cut.From, cut.To, "access", ref rng, res)) res.AccessLinks++;
            }

            // 2) Cul-de-sac links: from the neck of each bulb to the street / bulb that saves the longest walk.
            float minRatio = math.lerp(4.5f, 1.8f, math.saturate(o.Density));
            float maxLen = math.lerp(90f, 170f, math.saturate(o.Density));
            var caps = CapTips(net);
            foreach (var tip in caps)
            {
                if (TryCulDeSacLink(net, o, tip, minRatio, maxLen, ref rng, res)) res.CulDeSacLinks++;
            }

            // 3) Mid-block cut-throughs in long blocks.
            float minBlock = math.lerp(420f, 170f, math.saturate(o.Density));
            CutThroughs(net, o, minBlock, ref rng, res);
            return res;
        }

        // ───────────────────────── cul-de-sac links ─────────────────────────

        private struct Tip
        {
            public float3 Position;
            public float2 Forward;   // direction the cul-de-sac points (into the bulb)
            public float Width;
        }

        private static List<Tip> CapTips(RoadNetwork net)
        {
            var list = new List<Tip>();
            var deg = RoadGraph.Degrees(net, RoadGraph.Any);
            foreach (var e in net.Edges)
            {
                if (!RoadGraph.Car(e) || !e.CapAtEnd || deg[e.N1] != 1 || RoadConnectivity.Attached(net.Nodes[e.N1])) continue;
                list.Add(new Tip { Position = e.Curve.D, Forward = e.Curve.TangentXZ(1f), Width = 0f });
            }
            list.Sort((a, b) => a.Position.x != b.Position.x ? a.Position.x.CompareTo(b.Position.x) : a.Position.z.CompareTo(b.Position.z));
            return list;
        }

        private static bool TryCulDeSacLink(RoadNetwork net, Options o, Tip tip, float minRatio, float maxLen, ref Unity.Mathematics.Random rng, Result res)
        {
            int tipNode = RoadGraph.NodeAt(net, tip.Position.xz, 0.1f);
            if (tipNode < 0) return false;
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            if (inc[tipNode].Count != 1) return false;
            int capEdge = inc[tipNode][0];
            var ce = net.Edges[capEdge];
            float width = 2f * o.HalfWidth(ce.Role);
            float r = o.CapRadius(width);
            float capLen = RoadGraph.Length(ce);
            float neckDist = r + 3f;
            if (capLen < neckDist + 8f) return false;
            // The walk leaves the cul-de-sac at its neck, square to the street, on the side facing the target.
            float tNeck = RoadGraph.ParamAt(ce.Curve, neckDist, fromEnd: true);
            float3 neck = ce.Curve.Position(tNeck);
            float2 along = ce.Curve.TangentXZ(tNeck);

            var lengths = RoadGraph.Lengths(net);
            var dist = RoadGraph.Distances(net, inc, tipNode, 4000f, lengths);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            var near = new HashSet<int>();
            index.Near(neck.xz, maxLen, near);
            Plan best = null;
            float bestScore = float.MaxValue;
            foreach (int fi in near)
            {
                var f = net.Edges[fi];
                if (!RoadGraph.Car(f) || fi == capEdge) continue;
                float d = f.Curve.DistanceXZ(neck.xz, out float t);
                if (d < 20f || d > maxLen) continue;
                float len = lengths[fi];
                float s = t * len;
                // Network distance from the tip to that point on the road.
                float viaRoad = math.min(dist[f.N0] + s, dist[f.N1] + (len - s)) - neckDist;
                if (float.IsInfinity(viaRoad) || viaRoad / d < minRatio) continue;
                // Joins: away from the edge's junctions (no walk node on top of a junction) — else the junction itself.
                int v = -1;
                if (s < 14f) v = f.N0; else if (len - s < 14f) v = f.N1;
                float3 q = v >= 0 ? net.Nodes[v].Position : f.Curve.Position(t);
                if (v >= 0 && inc[v].Count >= 4) continue;
                // Never onto a dead end (it would lose its turning circle) or into another bulb.
                if (v >= 0 && inc[v].Count == 1) continue;
                if (NearDeadEnd(net, inc, q.xz, r + 6f)) continue;
                float2 dir = math.normalizesafe(q.xz - neck.xz);
                float2 side = math.dot(dir, new float2(-along.y, along.x)) >= 0f ? new float2(-along.y, along.x) : new float2(along.y, -along.x);
                var plan = new Plan { A = neck, B = q, EdgeA = capEdge, TA = tNeck, EdgeB = v >= 0 ? -1 : fi, TB = t, StartDir = side, Kind = "culdesac" };
                if (!Acceptable(net, o, plan, index, tip.Position.xz, r, out var curve)) continue;
                float score = math.distance(neck.xz, q.xz) / math.min(viaRoad / d, 8f);
                if (score < bestScore) { bestScore = score; best = plan; }
            }
            if (best == null) return false;
            return Commit(net, o, best, ref rng, res);
        }

        private static bool NearDeadEnd(RoadNetwork net, List<int>[] inc, float2 p, float radius)
        {
            for (int n = 0; n < net.Nodes.Count; n++)
                if (inc[n].Count == 1 && math.distance(net.Nodes[n].Position.xz, p) < radius) return true;
            return false;
        }

        // ───────────────────────── mid-block cut-throughs ─────────────────────────

        private static void CutThroughs(RoadNetwork net, Options o, float minBlock, ref Unity.Mathematics.Random rng, Result res)
        {
            // Chains of car edges between junctions, longest first.
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var chains = Chains(net, inc);
            chains.Sort((a, b) => b.length.CompareTo(a.length));
            var used = new List<float2>();
            foreach (var ch in chains)
            {
                if (ch.length < minBlock) break;
                // Middle of the chain.
                float half = ch.length * 0.5f, acc = 0f;
                int ei = -1; float t = 0.5f;
                foreach (int k in ch.edges)
                {
                    float l = RoadGraph.Length(net.Edges[k]);
                    if (acc + l >= half) { ei = k; t = RoadGraph.ParamAt(net.Edges[k].Curve, half - acc); break; }
                    acc += l;
                }
                if (ei < 0) continue;
                var e = net.Edges[ei];
                if (RoadAccess.IsMain(e)) continue;
                float3 p = e.Curve.Position(t);
                bool tooNear = false;
                foreach (var u in used) if (math.distance(u, p.xz) < 70f) { tooNear = true; break; }
                if (tooNear) continue;
                float2 tan = e.Curve.TangentXZ(t);
                var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
                Plan best = null;
                float bestLen = float.MaxValue;
                var lengths = RoadGraph.Lengths(net);
                foreach (float sgn in new[] { 1f, -1f })
                {
                    float2 nrm = new float2(-tan.y, tan.x) * sgn;
                    if (!index.Ray(p.xz, nrm, 8f, 190f, new[] { ei }, out int hitEdge, out float hd, out float2 hit)) continue;
                    var f = net.Edges[hitEdge];
                    if (!RoadGraph.Car(f) || RoadAccess.IsMain(f) || hd < 40f) continue;
                    float ht;
                    f.Curve.DistanceXZ(hit, out ht);
                    float fl = lengths[hitEdge];
                    if (ht * fl < 20f || (1f - ht) * fl < 20f) continue;
                    if (math.abs(math.dot(nrm, f.Curve.TangentXZ(ht))) > 0.5f) continue;
                    // Worth it only when the way round is much longer.
                    // The way round by road, leaving P in either direction.
                    var d0 = RoadGraph.Distances(net, inc, e.N0, 4000f, lengths);
                    var d1 = RoadGraph.Distances(net, inc, e.N1, 4000f, lengths);
                    float sA = t * lengths[ei], sB = lengths[ei] - sA;
                    float viaRoad = math.min(
                        math.min(d0[f.N0] + ht * fl, d0[f.N1] + (1f - ht) * fl) + sA,
                        math.min(d1[f.N0] + ht * fl, d1[f.N1] + (1f - ht) * fl) + sB);
                    if (float.IsInfinity(viaRoad) || viaRoad < 2.2f * hd) continue;
                    var plan = new Plan { A = p, B = f.Curve.Position(ht), EdgeA = ei, TA = t, EdgeB = hitEdge, TB = ht, Kind = "cut" };
                    if (!Acceptable(net, o, plan, index, default, 0f, out _)) continue;
                    if (hd < bestLen) { bestLen = hd; best = plan; }
                }
                if (best == null) continue;
                if (Commit(net, o, best, ref rng, res))
                {
                    res.CutThroughs++;
                    used.Add(best.A.xz);
                    used.Add(best.B.xz);
                    inc = RoadGraph.Incidence(net, RoadGraph.Car);
                }
            }
        }

        public struct Chain
        {
            public List<int> edges;
            public float length;
        }

        /// <summary>Runs of car edges through plain (degree-2) nodes, between junctions / ends.</summary>
        public static List<Chain> Chains(RoadNetwork net, List<int>[] inc)
        {
            var seen = new HashSet<int>();
            var list = new List<Chain>();
            for (int i = 0; i < net.Edges.Count; i++)
            {
                var e = net.Edges[i];
                if (!RoadGraph.Car(e) || seen.Contains(i)) continue;
                var edges = new List<int> { i };
                seen.Add(i);
                // extend both ways through degree-2 nodes with the same role
                foreach (int startNode in new[] { e.N0, e.N1 })
                {
                    int cur = startNode, prev = i;
                    for (int guard = 0; guard < 200; guard++)
                    {
                        if (inc[cur].Count != 2 || RoadConnectivity.Attached(net.Nodes[cur])) break;
                        int nx = inc[cur][0] == prev ? inc[cur][1] : inc[cur][0];
                        if (seen.Contains(nx) || net.Edges[nx].Role != e.Role) break;
                        seen.Add(nx);
                        if (startNode == e.N0) edges.Insert(0, nx); else edges.Add(nx);
                        cur = RoadGraph.Other(net.Edges[nx], cur);
                        prev = nx;
                    }
                }
                float len = 0f;
                foreach (int k in edges) len += RoadGraph.Length(net.Edges[k]);
                list.Add(new Chain { edges = edges, length = len });
            }
            return list;
        }

        // ───────────────────────── checks and commit ─────────────────────────

        private static bool AddWalk(RoadNetwork net, Options o, float3 from, float3 to, string kind, ref Unity.Mathematics.Random rng, Result res)
        {
            int a = RoadGraph.NodeAt(net, from.xz, 0.2f), b = RoadGraph.NodeAt(net, to.xz, 0.2f);
            var plan = new Plan { A = from, B = to, Kind = kind };
            if (b < 0)
            {
                // The junction node went (a bend was rounded there): join the main road at the nearest point instead.
                int fe = -1; float bd = 20f, bt = 0f;
                for (int i = 0; i < net.Edges.Count; i++)
                {
                    if (!RoadGraph.Car(net.Edges[i])) continue;
                    float d = net.Edges[i].Curve.DistanceXZ(to.xz, out float t);
                    if (d < bd && t > 0.02f && t < 0.98f) { bd = d; fe = i; bt = t; }
                }
                if (fe < 0) return false;
                plan.EdgeB = fe; plan.TB = bt; plan.B = net.Edges[fe].Curve.Position(bt);
            }
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            if (a >= 0 && inc[a].Count == 1 && net.Edges[inc[a][0]].CapAtEnd)
            {
                // The cut street ends in a cul-de-sac: leave from its neck.
                var ce = net.Edges[inc[a][0]];
                float r = o.CapRadius(2f * o.HalfWidth(ce.Role));
                float l = RoadGraph.Length(ce);
                if (l < r + 11f) return false;
                float tn = RoadGraph.ParamAt(ce.Curve, r + 3f, fromEnd: true);
                plan.A = ce.Curve.Position(tn);
                plan.EdgeA = inc[a][0];
                plan.TA = tn;
                float2 along = ce.Curve.TangentXZ(tn);
                float2 dir = math.normalizesafe(to.xz - plan.A.xz);
                plan.StartDir = math.dot(dir, new float2(-along.y, along.x)) >= 0f ? new float2(-along.y, along.x) : new float2(along.y, -along.x);
            }
            else if (a < 0) return false;
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            if (!Acceptable(net, o, plan, index, a >= 0 ? net.Nodes[a].Position.xz : default, a >= 0 ? o.CapRadius(16f) : 0f, out _)) return false;
            return Commit(net, o, plan, ref rng, res);
        }

        private static RoadCurve CurveOf(Plan p)
        {
            if (math.lengthsq(p.StartDir) > 0.5f)
            {
                float d = math.distance(p.A.xz, p.B.xz);
                // A short square start (5–8 m) then straight on: reads as a path leaving the kerb.
                float2 chord = math.normalizesafe(p.B.xz - p.A.xz);
                if (math.dot(chord, p.StartDir) > 0.97f) return RoadCurve.Straight(p.A, p.B);
                return RoadGraph.Link(p.A, p.B, p.StartDir, chord);
            }
            return RoadCurve.Straight(p.A, p.B);
        }

        private static bool Acceptable(RoadNetwork net, Options o, Plan p, RoadGraph.EdgeIndex index, float2 bulb, float bulbRadius, out RoadCurve curve)
        {
            curve = CurveOf(p);
            float len = curve.LengthXZ(12);
            if (len < 12f || len > 220f) return false;
            var line = RoadGraph.Sample(curve, 2.5f);
            var ignore = new HashSet<int>();
            if (p.EdgeA >= 0) ignore.Add(p.EdgeA);
            if (p.EdgeB >= 0) ignore.Add(p.EdgeB);
            int na = p.EdgeA < 0 ? RoadGraph.NodeAt(net, p.A.xz, 0.2f) : -1, nb = p.EdgeB < 0 ? RoadGraph.NodeAt(net, p.B.xz, 0.2f) : -1;
            var incAll = RoadGraph.Incidence(net, RoadGraph.Any);
            if (na >= 0) foreach (int k in incAll[na]) ignore.Add(k);
            if (nb >= 0) foreach (int k in incAll[nb]) ignore.Add(k);
            if (index.Crosses(line, 1f, ignore)) return false;
            // Along its length a walk keeps clear of other roads (it is not a sidewalk).
            float hw = o.HalfWidth(NetRole.Street);
            if (index.Clearance(line, hw + 8f, hw + 6f, ignore) < hw + 4f) return false;
            if (bulbRadius > 0f)
                foreach (var q in line)
                    if (math.distance(q, bulb) < bulbRadius + 1.5f) return false;
            if (o.Obstacles != null && o.Obstacles.Blocked(curve, 2.5f)) return false;
            if (o.Boundary != null && o.Boundary.Length >= 3)
                foreach (var q in line)
                    if (!Poly.Contains(o.Boundary, q) && Poly.DistanceToBoundary(o.Boundary, q) > 2f) return false;
            if (RoadGraph.MaxGrade(curve, o.Height, 8f, 2.5f) > o.MaxGrade) return false;
            return true;
        }

        /// <summary>A join closer than this to an end of the road edge uses that end node (no crumb of road is left).</summary>
        private const float MinPiece = 9f;

        /// <summary>Snaps a join near an edge end onto the end node; returns false when the edge must be split.</summary>
        private static bool SnapJoin(RoadNetwork net, int edge, float t, ref float3 point)
        {
            var e = net.Edges[edge];
            float len = RoadGraph.Length(e);
            float s = ArcTo(e.Curve, t);
            if (s < MinPiece) { point = e.Curve.A; return true; }
            if (len - s < MinPiece) { point = e.Curve.D; return true; }
            return false;
        }

        private static float ArcTo(RoadCurve c, float t)
        {
            float len = 0f;
            float2 prev = c.A.xz;
            for (int i = 1; i <= 24; i++)
            {
                float2 q = c.Position(t * i / 24f).xz;
                len += math.distance(prev, q);
                prev = q;
            }
            return len;
        }

        private static bool Commit(RoadNetwork net, Options o, Plan p, ref Unity.Mathematics.Random rng, Result res)
        {
            // Joins: split the road there, or use its end node when the join is within a few metres of it.
            if (p.EdgeB >= 0 && SnapJoin(net, p.EdgeB, p.TB, ref p.B)) p.EdgeB = -1;
            if (p.EdgeA >= 0 && SnapJoin(net, p.EdgeA, p.TA, ref p.A)) p.EdgeA = -1;
            var curve = CurveOf(p);
            // Split the roads at the joins first (B before A when both split the same edge would shift t; they never do).
            if (p.EdgeB >= 0) RoadGraph.Split(net, p.EdgeB, p.TB);
            if (p.EdgeA >= 0) RoadGraph.Split(net, p.EdgeA, p.TA);
            bool trail = o.Style == "trail" || (o.Style == "mixed" && rng.NextFloat() < 0.5f);
            var walk = new RoadEdge(curve, NetRole.Pathway) { Walk = true, Trail = trail, Category = trail ? AssetCategories.TrailPath : "" };
            net.Edges.Add(walk);
            net.Weld();
            res.Length += curve.LengthXZ(12);
            if (trail) res.Trails++;
            return true;
        }

        // ───────────────────────── back lanes ─────────────────────────

        public sealed class LaneOptions
        {
            /// <summary>0..1: share of eligible blocks that get a lane.</summary>
            public float Density;
            /// <summary>"alley" | "gravel" | "mixed".</summary>
            public string Style = "alley";
            public uint Seed = 1;
            public float2[] Boundary = Array.Empty<float2>();
            public Func<float2, float> Height;
            public RoadObstacles Obstacles;
            public Func<NetRole, float> HalfWidth = r => 8f;
            public float MaxGrade = 0.1f;
            /// <summary>0..1: share of the blocks without a lane that get a mid-block footpath instead ("even in grids").</summary>
            public float WalkDensity;
            public string WalkStyle = "paved";
        }

        /// <summary>
        /// Back lanes through the middle of blocks: between two roughly parallel roads 60–190 m apart, joining each at a T
        /// (midpoint of the road between its junctions), all lanes of a layout running the same way (the network's main axis),
        /// never onto a main road. Lanes are role Alley; gravel style sets the Gravel Road.
        /// </summary>
        public static int Lanes(RoadNetwork net, LaneOptions o)
        {
            if (!(o.Density > 0.001f) && !(o.WalkDensity > 0.001f)) return 0;
            var rng = new Unity.Mathematics.Random(o.Seed == 0 ? 0x7F4A7C15u : o.Seed);
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            float2 axis = MainAxis(net, o.Height);
            var chains = Chains(net, inc);
            // Candidates: chains running ACROSS the axis (lanes run along it, from one cross street to the next).
            var cands = new List<(int edge, float t, float3 p, float2 nrm)>();
            foreach (var ch in chains)
            {
                if (ch.length < 50f || ch.length > 230f || ch.edges.Count > 10) continue;
                float half = ch.length * 0.5f, acc = 0f;
                int ei = -1; float t = 0.5f;
                foreach (int k in ch.edges)
                {
                    float l = RoadGraph.Length(net.Edges[k]);
                    if (acc + l >= half) { ei = k; t = RoadGraph.ParamAt(net.Edges[k].Curve, half - acc); break; }
                    acc += l;
                }
                if (ei < 0) continue;
                var e = net.Edges[ei];
                if (RoadAccess.IsMain(e) || e.Role == NetRole.Roundabout || e.Role == NetRole.Alley) continue;
                float2 tan = e.Curve.TangentXZ(t);
                if (math.abs(math.dot(tan, axis)) > 0.5f) continue;   // runs along the axis: not a cross street
                cands.Add((ei, t, e.Curve.Position(t), new float2(-tan.y, tan.x)));
            }
            cands.Sort((a, b) => a.p.x != b.p.x ? a.p.x.CompareTo(b.p.x) : a.p.z.CompareTo(b.p.z));
            int added = 0;
            var joined = new List<float2>();
            foreach (var c in cands)
            {
                bool lane = rng.NextFloat() < o.Density;
                bool path = !lane && rng.NextFloat() < o.WalkDensity;
                if (!lane && !path) continue;
                var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
                int ei = RoadGraph.NodeAt(net, c.p.xz, 0.05f) >= 0 ? -1 : FindEdge(net, c.p.xz);
                if (ei < 0) continue;
                var e = net.Edges[ei];
                e.Curve.DistanceXZ(c.p.xz, out float t);
                foreach (float sgn in new[] { 1f, -1f })
                {
                    float2 nrm = c.nrm * sgn;
                    if (!index.Ray(c.p.xz, nrm, 8f, 200f, new[] { ei }, out int hitEdge, out float hd, out float2 hit)) continue;
                    if (hd < 60f || hd > 195f) continue;
                    var f = net.Edges[hitEdge];
                    if (!RoadGraph.Car(f) || RoadAccess.IsMain(f) || f.Role == NetRole.Roundabout) continue;
                    f.Curve.DistanceXZ(hit, out float ht);
                    float fl = RoadGraph.Length(f);
                    if (ht * fl < 22f || (1f - ht) * fl < 22f) continue;
                    if (math.abs(math.dot(nrm, f.Curve.TangentXZ(ht))) > 0.5f) continue;   // the far road must be parallel (square to the lane)
                    bool dup = false;
                    foreach (var j in joined) if (math.distance(j, c.p.xz) < 25f && Near(joined, hit, 25f)) { dup = true; break; }
                    if (dup) continue;
                    float3 b = f.Curve.Position(ht);
                    // (Smarter roads) both ends a full junction gap from every other junction, or no lane (never squeezed in)
                    if (!ClearOfJunctions(net, c.p.xz) || !ClearOfJunctions(net, b.xz)) continue;
                    var curve = RoadCurve.Straight(c.p, b);
                    var line = RoadGraph.Sample(curve, 3f);
                    var ignore = new HashSet<int> { ei, hitEdge };
                    if (index.Crosses(line, 1f, ignore)) continue;
                    float hw = o.HalfWidth(NetRole.Street);
                    if (index.Clearance(line, hw + 10f, hw + 8f, ignore) < hw + 6f) continue;
                    if (o.Obstacles != null && o.Obstacles.Blocked(curve, o.HalfWidth(NetRole.Alley) + 1f)) continue;
                    if (RoadGraph.MaxGrade(curve, o.Height) > (lane ? o.MaxGrade : math.min(o.MaxGrade, MaxWalkGrade))) continue;
                    RoadGraph.Split(net, hitEdge, ht);
                    RoadGraph.Split(net, ei, t);
                    if (lane)
                    {
                        bool gravel = o.Style == "gravel" || (o.Style == "mixed" && rng.NextFloat() < 0.5f);
                        net.Edges.Add(new RoadEdge(curve, NetRole.Alley) { AssetOverride = gravel ? RoadRoles.Gravel : "" });
                    }
                    else
                    {
                        bool trail = o.WalkStyle == "trail" || (o.WalkStyle == "mixed" && rng.NextFloat() < 0.5f);
                        net.Edges.Add(new RoadEdge(curve, NetRole.Pathway) { Walk = true, Trail = trail, Category = trail ? AssetCategories.TrailPath : "" });
                    }
                    net.Weld();
                    joined.Add(c.p.xz); joined.Add(b.xz);
                    if (lane) added++;
                    break;
                }
            }
            return added;
        }

        /// <summary>No junction (three car arms or more, or a join onto a city road) within the junction gap of p.</summary>
        private static bool ClearOfJunctions(RoadNetwork net, float2 p)
        {
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc[n].Count < 3 && !(inc[n].Count > 0 && net.Nodes[n].Attach >= 0)) continue;
                float d = math.distance(net.Nodes[n].Position.xz, p);
                if (d > 0.5f && d < RoadTopology.MinJunctionGap - RoadTopology.GapSlack) return false;
            }
            return true;
        }

        private static bool Near(List<float2> pts, float2 p, float r)
        {
            foreach (var q in pts) if (math.distance(q, p) < r) return true;
            return false;
        }

        private static int FindEdge(RoadNetwork net, float2 p)
        {
            int best = -1; float bd = 0.5f;
            for (int i = 0; i < net.Edges.Count; i++)
            {
                var e = net.Edges[i];
                if (!RoadGraph.Car(e)) continue;
                float d = e.Curve.DistanceXZ(p, out _);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// The direction the lanes run: the grid orientation of the car network (quadrupled-angle average, so the two
        /// families of a grid reinforce), then whichever of the two families has more road length.
        /// </summary>
        public static float2 MainAxis(RoadNetwork net, Func<float2, float> height = null)
        {
            // Chains (junction to junction) rather than edges: a street laid onto a contour is many short bent pieces.
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var chords = new List<(float2 a, float2 b)>();
            foreach (var ch in Chains(net, inc))
            {
                var ends = new Dictionary<int, int>();
                foreach (int k in ch.edges)
                {
                    var e = net.Edges[k];
                    ends[e.N0] = ends.TryGetValue(e.N0, out int c0) ? c0 + 1 : 1;
                    ends[e.N1] = ends.TryGetValue(e.N1, out int c1) ? c1 + 1 : 1;
                }
                var term = new List<float2>();
                foreach (var kv in ends) if (kv.Value == 1) term.Add(net.Nodes[kv.Key].Position.xz);
                if (term.Count == 2) chords.Add((term[0], term[1]));
            }
            float2 acc = float2.zero;
            foreach (var (a, b) in chords)
            {
                float2 d = b - a;
                float l = math.length(d);
                if (l < 1f) continue;
                float ang4 = math.atan2(d.y, d.x) * 4f;
                acc += new float2(math.cos(ang4), math.sin(ang4)) * l;
            }
            float ang = math.lengthsq(acc) < 1e-6f ? 0f : math.atan2(acc.y, acc.x) * 0.25f;
            float2 u = new float2(math.cos(ang), math.sin(ang)), v = new float2(-u.y, u.x);
            float lu = 0f, lv = 0f, gu = 0f, gv = 0f;
            foreach (var (a, b) in chords)
            {
                float2 d = b - a;
                float l = math.length(d);
                if (l < 1f) continue;
                float cu = math.abs(math.dot(d / l, u)), cv = math.abs(math.dot(d / l, v));
                float g = height != null ? math.abs(height(b) - height(a)) : 0f;
                if (cu > 0.85f) { lu += l; gu += g; } else if (cv > 0.85f) { lv += l; gv += g; }
            }
            // On a slope the lanes run along the contours (the family that climbs least), else along the longer family.
            if (height != null && lu > 1f && lv > 1f)
            {
                float mu = gu / lu, mv = gv / lv;
                if (math.abs(mu - mv) > 0.015f) return mu < mv ? u : v;
            }
            return lu >= lv ? u : v;
        }

        // ───────────────────────── gravel tips ─────────────────────────

        /// <summary>
        /// Cul-de-sacs whose street ends in gravel: the dead-end run from its last junction to the bulb ("gravel": every
        /// one, "mixed": about half). Returns how many.
        /// </summary>
        public static int GravelTips(RoadNetwork net, string style, uint seed)
        {
            if (style != "gravel" && style != "mixed") return 0;
            var rng = new Unity.Mathematics.Random(seed == 0 ? 0x2545F491u : seed);
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            int count = 0;
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc[n].Count != 1 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                var tipEdge = net.Edges[inc[n][0]];
                if (!tipEdge.CapAtEnd || tipEdge.N1 != n || RoadAccess.IsMain(tipEdge)) continue;
                if (style == "mixed" && rng.NextFloat() < 0.5f) continue;
                int cur = n, prev = -1;
                for (int guard = 0; guard < 50; guard++)
                {
                    int ei = -1;
                    foreach (int k in inc[cur]) if (k != prev) { ei = k; break; }
                    if (ei < 0) break;
                    var e = net.Edges[ei];
                    if (RoadAccess.IsMain(e)) break;
                    e.AssetOverride = RoadRoles.Gravel;
                    int nxt = RoadGraph.Other(e, cur);
                    if (inc[nxt].Count != 2) break;
                    prev = ei; cur = nxt;
                }
                count++;
            }
            return count;
        }
    }
}
