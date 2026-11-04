// Placemaker roads layer (s34) — junction and bend geometry.
//
// Playtest: "corners look like a car cut off something". A street that turns at a node without a curve (a polyline
// corner from the generators, a perimeter tie-in, a link added to a dead end) is drawn by CS2 as a sharp, clipped
// node; side streets meeting at shallow angles make long thin corner wedges; junctions a few metres apart overlap.
// Here: (1) junctions closer than the junction gap merge, (2) bends at plain nodes get a real curve (centre-line radius
// by road class), (3) side streets meeting another road at less than ~60° are bent to meet it squarely.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadTopology
    {
        /// <summary>Bends sharper than this (degrees of turn) at a plain node get a fillet.</summary>
        public const float FilletMinTurn = 14f;
        /// <summary>Side streets are bent so they meet other roads at least at this angle (degrees).</summary>
        public const float MinJunctionAngle = 62f;

        /// <summary>Centre-line radius of a bend by road class (m): wide enough for the carriageway not to fold.</summary>
        public static float BendRadius(NetRole role, float halfWidth)
        {
            switch (role)
            {
                case NetRole.Avenue: return math.max(45f, halfWidth * 3.5f);
                case NetRole.Alley: return math.max(10f, halfWidth * 2.5f);
                case NetRole.Pathway: case NetRole.PathwayWide: return 6f;
                default: return math.max(22f, halfWidth * 2.8f);
            }
        }

        /// <summary>
        /// Merges junctions joined by a car edge shorter than <paramref name="gap"/>: the less connected node moves onto the
        /// other (attached nodes never move). Returns the number merged.
        /// </summary>
        public static int MergeCloseJunctions(RoadNetwork net, float gap)
        {
            int merged = 0;
            for (int pass = 0; pass < 6; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                bool any = false;
                foreach (var e in net.Edges)
                {
                    if (!RoadGraph.Car(e) || e.N0 == e.N1) continue;
                    if (inc[e.N0].Count < 3 || inc[e.N1].Count < 3) continue;
                    if (e.Role == NetRole.Roundabout) continue;
                    if (math.distance(e.Curve.A.xz, e.Curve.D.xz) >= gap) continue;
                    var a = net.Nodes[e.N0]; var b = net.Nodes[e.N1];
                    bool fa = RoadConnectivity.Attached(a), fb = RoadConnectivity.Attached(b);
                    if (fa && fb) continue;
                    int keep = fa ? e.N0 : fb ? e.N1 : (inc[e.N0].Count >= inc[e.N1].Count ? e.N0 : e.N1);
                    int drop = keep == e.N0 ? e.N1 : e.N0;
                    float3 kp = net.Nodes[keep].Position;
                    e.Discarded = true;
                    foreach (var o in net.Edges)
                    {
                        if (o.Discarded || o == e) continue;
                        if (o.N0 == drop) { o.Curve = o.Curve.WithEnds(kp, o.Curve.D); o.N0 = keep; }
                        if (o.N1 == drop) { o.Curve = o.Curve.WithEnds(o.Curve.A, kp); o.N1 = keep; }
                        if (o.N0 == o.N1) o.Discarded = true;
                    }
                    net.Nodes[drop].Position = kp;
                    merged++;
                    any = true;
                    break;
                }
                if (!any) break;
                net.DiscardDuplicates();
                net.Weld();
            }
            return merged;
        }

        /// <summary>
        /// Rounds the bends at plain (degree-2, unattached) nodes of car roads: both edges are trimmed by R·tan(θ/2) and joined
        /// by a curve tangent to both. R by class, limited so a trim never eats more than 45 % of an edge.
        /// </summary>
        public static int FilletBends(RoadNetwork net, Func<NetRole, float> halfWidth, float perimeterRadius = -1f, bool carOnly = false)
        {
            int count = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                // carOnly: a bend where only a footpath joins is rounded too; the path then joins the middle of the curve
                var inc = RoadGraph.Incidence(net, carOnly ? (Func<RoadEdge, bool>)RoadGraph.Car : RoadGraph.Any);
                var walks = carOnly ? RoadGraph.Incidence(net, RoadGraph.Any) : null;
                var done = new HashSet<int>();
                bool any = false;
                for (int n = 0; n < net.Nodes.Count; n++)
                {
                    if (inc[n].Count != 2 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                    int i0 = inc[n][0], i1 = inc[n][1];
                    if (done.Contains(i0) || done.Contains(i1)) continue;
                    var e0 = net.Edges[i0]; var e1 = net.Edges[i1];
                    if (e0.IsCar != e1.IsCar || e0.Role == NetRole.Roundabout || e1.Role == NetRole.Roundabout) continue;
                    float2 d0 = RoadGraph.Leaving(e0, n), d1 = RoadGraph.Leaving(e1, n);
                    float turn = 180f - RoadGraph.Angle(d0, d1);
                    if (turn < FilletMinTurn || turn > 150f) continue;
                    var role = Rank(e0.Role) >= Rank(e1.Role) ? e0.Role : e1.Role;
                    float r = BendRadius(role, halfWidth(role));
                    // (s61) A perimeter bend is the Corner rounding setting: 0 = a sharp corner (the outline exactly).
                    if (perimeterRadius >= 0f && (e0.Role == NetRole.PerimeterRoad || e1.Role == NetRole.PerimeterRoad))
                    {
                        if (perimeterRadius < 1f) continue;
                        r = perimeterRadius;
                    }
                    float th = math.radians(turn);
                    r = math.max(r, 8f / th);   // a gentle bend gets a curve of 8 m at least (a shorter one is a crumb)
                    float trim = r * math.tan(th * 0.5f);
                    float l0 = RoadGraph.Length(e0), l1 = RoadGraph.Length(e1);
                    trim = math.min(trim, 0.45f * math.min(l0, l1));
                    trim = math.min(trim, math.min(l0, l1) - 8f);   // never a crumb of street left beside the bend
                    if (trim < 3f) continue;
                    if (trim * th / math.tan(th * 0.5f) < 7.5f) continue;   // nor a bend that is itself a crumb
                    // Cut both edges `trim` from the node; the node-side pieces go, a curve joins the new ends.
                    float3 p0 = Trim(net, i0, n, trim, out float2 t0);
                    float3 p1 = Trim(net, i1, n, trim, out float2 t1);
                    var arc = RoadCurve.Fit(p0, new float3(-t0.x, 0f, -t0.y), new float3(t1.x, 0f, t1.y), p1);
                    var pieces = new List<RoadCurve>();
                    bool pathHere = false;
                    if (walks != null) foreach (int k in walks[n]) if (k != i0 && k != i1) pathHere = true;
                    if (pathHere && arc.LengthXZ(12) < 16f)
                    {
                        // a short bend: the path joins its nearer end (halves would be crumbs of road)
                        foreach (int k in walks[n])
                        {
                            if (k == i0 || k == i1) continue;
                            var w = net.Edges[k];
                            float3 far = w.N0 == n ? w.Curve.D : w.Curve.A;
                            float3 end = math.distance(far.xz, p0.xz) <= math.distance(far.xz, p1.xz) ? p0 : p1;
                            w.Curve = w.N0 == n ? RoadCurve.Straight(end, far) : RoadCurve.Straight(far, end);
                        }
                        pathHere = false;
                    }
                    if (pathHere)
                    {
                        arc.Split(0.5f, out var h0, out var h1);
                        pieces.Add(h0); pieces.Add(h1);
                        float3 mid = h0.D;
                        foreach (int k in walks[n])
                        {
                            if (k == i0 || k == i1) continue;
                            var w = net.Edges[k];
                            w.Curve = w.N0 == n ? RoadCurve.Straight(mid, w.Curve.D) : RoadCurve.Straight(w.Curve.A, mid);
                        }
                    }
                    else pieces.Add(arc);
                    foreach (var piece in pieces)
                        net.Edges.Add(new RoadEdge(piece, role)
                        {
                            AssetOverride = e0.AssetOverride, Category = e0.Category, Walk = e0.Walk, Trail = e0.Trail,
                            // (s52) A bend inside one terrace street stays on that terrace.
                            TerraceStreet = e0.TerraceStreet == e1.TerraceStreet ? e0.TerraceStreet : -1,
                            TerraceLink = e0.TerraceLink && e1.TerraceLink,
                        });
                    done.Add(i0); done.Add(i1);
                    count++;
                    any = true;
                }
                if (!any) break;
                net.Weld();
            }
            return count;
        }

        /// <summary>
        /// Cuts edge <paramref name="ei"/> <paramref name="dist"/> metres from <paramref name="node"/>: the node side is dropped.
        /// Returns the new end point and the edge's direction there, pointing away from the node.
        /// </summary>
        private static float3 Trim(RoadNetwork net, int ei, int node, float dist, out float2 awayDir)
        {
            var e = net.Edges[ei];
            bool atStart = e.N0 == node;
            float t = RoadGraph.ParamAt(e.Curve, dist, fromEnd: !atStart);
            e.Curve.Split(math.clamp(t, 0.001f, 0.999f), out var left, out var right);
            if (atStart)
            {
                e.Curve = right;
                awayDir = right.TangentXZ(0f);
                return right.A;
            }
            e.Curve = left;
            awayDir = -left.TangentXZ(1f);
            return left.D;
        }

        private static int Rank(NetRole r)
        {
            switch (r)
            {
                case NetRole.Avenue: return 5;
                case NetRole.PerimeterRoad: return 4;
                case NetRole.Street: return 3;
                case NetRole.CulDeSac: return 2;
                case NetRole.Alley: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Side streets meeting another road at less than <see cref="MinJunctionAngle"/>: the end of the lesser edge is turned
        /// to the middle of the free angle around the node (its first control point moves), when the free angle allows a
        /// square meeting and the bent curve stays clear of everything else.
        /// </summary>
        public static int SquareJunctions(RoadNetwork net, RoadObstacles obstacles = null, Func<NetRole, float> halfWidth = null)
        {
            int fixedCount = 0;
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc[n].Count < 3) continue;
                var list = inc[n];
                // Directions around the node, sorted by angle.
                var dirs = new List<(float ang, int ei)>();
                foreach (int ei in list)
                {
                    float2 d = RoadGraph.Leaving(net.Edges[ei], n);
                    dirs.Add((math.atan2(d.y, d.x), ei));
                }
                dirs.Sort((a, b) => a.ang.CompareTo(b.ang));
                int m = dirs.Count;
                for (int k = 0; k < m; k++)
                {
                    int ei = dirs[k].ei;
                    var e = net.Edges[ei];
                    if (e.Role == NetRole.Roundabout || e.Role == NetRole.Avenue) continue;
                    // A road passing through the junction (same class on the far side) never bends: only the side street.
                    bool through = false;
                    foreach (var (_, oi) in dirs)
                        if (oi != ei && net.Edges[oi].Role == e.Role
                            && RoadGraph.Angle(RoadGraph.Leaving(e, n), RoadGraph.Leaving(net.Edges[oi], n)) > 140f) through = true;
                    if (through) continue;
                    float prev = dirs[(k - 1 + m) % m].ang, next = dirs[(k + 1) % m].ang, cur = dirs[k].ang;
                    float gapPrev = Wrap(cur - prev), gapNext = Wrap(next - cur);
                    float minGap = math.degrees(math.min(gapPrev, gapNext));
                    if (minGap >= MinJunctionAngle) continue;
                    float free = gapPrev + gapNext;   // the sector between the two neighbours
                    if (math.degrees(free) < 2f * MinJunctionAngle) continue;
                    // The lesser road bends: never a road that outranks both neighbours.
                    int rankNeighbours = math.max(Rank(net.Edges[dirs[(k - 1 + m) % m].ei].Role), Rank(net.Edges[dirs[(k + 1) % m].ei].Role));
                    if (Rank(e.Role) > rankNeighbours) continue;
                    float target = prev + free * 0.5f;
                    float2 want = new float2(math.cos(target), math.sin(target));
                    float len = RoadGraph.Length(e);
                    if (len < 30f) continue;
                    var old = e.Curve;
                    float arm = math.clamp(len * 0.25f, 10f, 24f);
                    bool atStart = e.N0 == n;
                    if (atStart) e.Curve = new RoadCurve(e.Curve.A, e.Curve.A + new float3(want.x, 0f, want.y) * arm, e.Curve.C, e.Curve.D);
                    else e.Curve = new RoadCurve(e.Curve.A, e.Curve.B, e.Curve.D + new float3(want.x, 0f, want.y) * arm, e.Curve.D);
                    var line = RoadGraph.Sample(e.Curve, 3f);
                    var ignore = new HashSet<int>(inc[e.N0]);
                    foreach (int k2 in inc[e.N1]) ignore.Add(k2);
                    // (s61) A street bent round an obstacle stays bent: squaring never puts it back through the building / water.
                    if (index.Crosses(line, 2f, ignore) || SelfFolds(e.Curve) || (obstacles != null && obstacles.Blocked(e.Curve, (halfWidth != null ? halfWidth(e.Role) : 8f) + 1f)))
                    {
                        e.Curve = old;
                        continue;
                    }
                    dirs[k] = (target, ei);
                    fixedCount++;
                }
            }
            return fixedCount;
        }

        /// <summary>(Smarter roads) No two junctions closer than this (m, centre to centre; about 30 m of kerb between two 16 m roads): the user's hard rule.</summary>
        public const float MinJunctionGap = 45f;
        /// <summary>Measuring slack: a junction exactly <see cref="MinJunctionGap"/> away (a mid-block lane on a 90 m block) is fine.</summary>
        public const float GapSlack = 0.5f;
        /// <summary>
        /// A short street connecting two roads (a rung: it is the side street at one of its ends, like a link from the
        /// perimeter to a city road beside it) needs this much between its two junctions; junctions one after another along
        /// a road keep the full <see cref="MinJunctionGap"/>.
        /// </summary>
        public const float RungGap = 30f;

        /// <summary>
        /// The gap two junctions need: the full gap when one road runs straight through both (they follow each other along
        /// it), else the rung gap. <paramref name="armsA"/>/<paramref name="armsB"/>: the directions of the roads leaving each.
        /// </summary>
        public static float GapNeeded(float2 a, List<float2> armsA, float2 b, List<float2> armsB, float full = MinJunctionGap)
        {
            float2 ab = math.normalizesafe(b - a);
            bool Through(List<float2> arms, float2 dir)
            {
                bool on = false, back = false;
                foreach (var d in arms)
                {
                    float c = math.dot(math.normalizesafe(d), dir);
                    if (c > 0.94f) on = true;          // under 20° toward the other junction
                    else if (c < -0.87f) back = true;  // and the road goes on the far side (under 30°)
                }
                return on && back;
            }
            return Through(armsA, ab) && Through(armsB, -ab) ? full : math.min(full, RungGap);
        }

        /// <summary>
        /// Two junctions crowd each other only with road between them (along one road, or a short link): the straight line
        /// from one to the other stays on a road all the way. Two junctions on parallel roads across a strip of land do not.
        /// <paramref name="offRoad"/> gives how far a point is outside the nearest road's edge (negative = on it).
        /// </summary>
        public static bool RoadBetween(float2 a, float2 b, Func<float2, float> offRoad)
        {
            for (int i = 1; i < 6; i++)
                if (offRoad(math.lerp(a, b, i / 6f)) > 2f) return false;
            return true;
        }

        /// <summary>
        /// (Smarter roads) The hard rule: no two junctions closer than <see cref="MinJunctionGap"/> (the plan's own, and where it
        /// joins the city, next to the city's junctions). Where two crowd each other the plan gives up a piece rather than
        /// squeeze it in: at the less important junction, the least important street arm (never the perimeter or a main
        /// road; a diagonal, a link or a side street first; the shortest) goes, when the car network stays in one piece. The
        /// dead-end pass then ends what is left properly. Returns the pieces given up.
        /// </summary>
        public static int EnforceJunctionGap(RoadNetwork net, List<ExistingRoad> existing, Func<NetRole, float> halfWidth, float minGap = MinJunctionGap, float bigRoadGap = 60f)
        {
            // on a big existing road (four lanes and up) its junctions keep the road's own spacing
            float GapFor(float2 a, float2 b, long attachA, long attachB)
            {
                if (existing == null) return minGap;
                foreach (var r in existing)
                {
                    if (r.Rank < RoadRank.Medium) continue;
                    bool onA = r.Id == attachA || r.Curve.DistanceXZ(a, out _) < 1f;
                    bool onB = r.Id == attachB || r.Curve.DistanceXZ(b, out _) < 1f;
                    if (onA && onB) return math.max(minGap, bigRoadGap);
                }
                return minGap;
            }
            int removed = 0;
            var stuck = new List<float2>();   // pairs nothing can be given up at (the network would split): left, the rest still checked
            bool Stuck(float2 a, float2 b)
            {
                for (int k = 0; k + 1 < stuck.Count; k += 2)
                    if ((math.distance(stuck[k], a) < 0.5f && math.distance(stuck[k + 1], b) < 0.5f) || (math.distance(stuck[k], b) < 0.5f && math.distance(stuck[k + 1], a) < 0.5f)) return true;
                return false;
            }
            for (int pass = 0; pass < 96; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                // the city's junctions (their own arms count) near the plan
                var cityJunctions = new List<float2>();
                if (existing != null)
                {
                    var ends = new List<(float2 p, int n)>();
                    foreach (var r in existing)
                        foreach (var p in new[] { r.Curve.A.xz, r.Curve.D.xz })
                        {
                            int k = ends.FindIndex(x => math.distance(x.p, p) < 1f);
                            if (k < 0) ends.Add((p, 1)); else ends[k] = (ends[k].p, ends[k].n + 1);
                        }
                    foreach (var (p, n) in ends) if (n >= 3) cityJunctions.Add(p);
                }
                // the plan's junctions: three car arms or more, or a join onto a city road
                var junctions = new List<int>();
                for (int n = 0; n < net.Nodes.Count; n++)
                {
                    if (inc[n].Count == 0) continue;
                    bool onRing = false;
                    foreach (int ei in inc[n]) onRing |= net.Edges[ei].Role == NetRole.Roundabout;
                    if (onRing) continue;
                    if (inc[n].Count >= 3 || net.Nodes[n].Attach >= 0) junctions.Add(n);
                }
                float OffRoad(float2 p)
                {
                    float best = float.MaxValue;
                    foreach (var e in net.Edges)
                        if (RoadGraph.Car(e)) best = math.min(best, e.Curve.DistanceXZ(p, out _) - halfWidth(e.Role));
                    if (existing != null) foreach (var r in existing) best = math.min(best, r.Curve.DistanceXZ(p, out _) - r.HalfWidth);
                    return best;
                }
                List<float2> Arms(int n)
                {
                    var arms = new List<float2>();
                    foreach (int ei in inc[n]) arms.Add(RoadGraph.Leaving(net.Edges[ei], n));
                    var nd = net.Nodes[n];
                    // a join onto the city: every city road through the point (two pieces of one road meet at its nodes)
                    if (nd.Attach >= 0 && existing != null)
                        foreach (var r in existing)
                        {
                            if (r.Id != nd.Attach && r.Curve.DistanceXZ(nd.Position.xz, out _) > 1f) continue;
                            r.Curve.DistanceXZ(nd.Position.xz, out float t);
                            if (t > 0.01f) arms.Add(-r.Curve.TangentXZ(t));
                            if (t < 0.99f) arms.Add(r.Curve.TangentXZ(t));
                        }
                    return arms;
                }
                bool Linked(int a, int b)
                {
                    foreach (int ei in inc[a]) if (RoadGraph.Other(net.Edges[ei], a) == b) return true;
                    return false;
                }
                List<float2> CityArms(float2 p)
                {
                    var arms = new List<float2>();
                    if (existing != null)
                        foreach (var r in existing)
                        {
                            if (math.distance(r.Curve.A.xz, p) < 1f) arms.Add(r.Curve.TangentXZ(0f));
                            if (math.distance(r.Curve.D.xz, p) < 1f) arms.Add(-r.Curve.TangentXZ(1f));
                        }
                    return arms;
                }
                // the closest crowding pair
                int ja = -1, jb = -1; float worst = 0f, pairNeed = 0f;
                float2 cityAt = default; bool withCity = false;
                for (int i = 0; i < junctions.Count; i++)
                {
                    var ni = net.Nodes[junctions[i]];
                    float2 pi = ni.Position.xz;
                    for (int j = i + 1; j < junctions.Count; j++)
                    {
                        var nj = net.Nodes[junctions[j]];
                        float d = math.distance(pi, nj.Position.xz);
                        if (d <= 0.5f || d >= math.max(minGap, bigRoadGap)) continue;
                        if (!RoadBetween(pi, nj.Position.xz, OffRoad)) continue;
                        // two junctions of ours keep the full gap; a link onto a city road (its two junctions set by the
                        // selection and the city) needs the rung gap
                        float need = GapFor(pi, nj.Position.xz, ni.Attach, nj.Attach);
                        if ((ni.Attach >= 0 || nj.Attach >= 0) && Linked(junctions[i], junctions[j])) need = math.min(need, RungGap);
                        if (d < need - GapSlack && need - d > worst && !Stuck(pi, nj.Position.xz)) { worst = need - d; pairNeed = need; ja = junctions[i]; jb = junctions[j]; withCity = false; }
                    }
                    foreach (var c in cityJunctions)
                    {
                        float d = math.distance(pi, c);
                        if (d <= 1f || d >= math.max(minGap, bigRoadGap)) continue;
                        if (!RoadBetween(pi, c, OffRoad)) continue;
                        float need = GapNeeded(pi, Arms(junctions[i]), c, CityArms(c), GapFor(pi, c, ni.Attach, -1));
                        if (d < need - GapSlack && need - d > worst && !Stuck(pi, c)) { worst = need - d; pairNeed = need; ja = junctions[i]; jb = -1; cityAt = c; withCity = true; }
                    }
                }
                if (ja < 0) break;
                // which junction gives up an arm: the less important one (a city join never moves the city)
                var order = new List<int> { ja };
                if (!withCity && jb >= 0)
                {
                    if (Importance(net, inc, jb) < Importance(net, inc, ja)) order.Insert(0, jb); else order.Add(jb);
                }
                bool done = false;
                foreach (int n in order)
                {
                    var arms = new List<(float rank, int ei)>();
                    foreach (int ei in inc[n])
                    {
                        var e = net.Edges[ei];
                        if (e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue || e.Role == NetRole.Roundabout) continue;
                        // a piece that is itself the short link between the two crowding junctions goes first
                        int other = RoadGraph.Other(e, n);
                        bool between = !withCity && (other == ja || other == jb);
                        // a back lane gives way before any street (never cut a street to keep a lane)
                        float rank = (e.Role == NetRole.Alley ? 0f : 5000f) + (between ? 0f : 1000f) + (e.Connector ? 0f : 200f) + RoadGraph.Length(e);
                        arms.Add((rank, ei));
                    }
                    arms.Sort((x, y) => x.rank.CompareTo(y.rank));
                    foreach (var (_, ei) in arms)
                    {
                        int before = Parts(net);
                        net.Edges[ei].Removed = true;
                        int after = Parts(net);
                        if (after > before) { net.Edges[ei].Removed = false; continue; }
                        removed++;
                        done = true;
                        break;
                    }
                    if (done) break;
                }
                if (!done)
                {
                    // Nothing can go without splitting the network: a junction made by ONE street of ours (a join onto a
                    // road) has that street re-aimed onto the other junction instead: one junction where there were two.
                    foreach (int n in order)
                    {
                        if (inc[n].Count != 1 && !(net.Nodes[n].Attach >= 0 && CountOurs(net, inc, n) == 1)) continue;
                        int ei = -1;
                        foreach (int k in inc[n]) if (net.Edges[k].Role != NetRole.PerimeterRoad && net.Edges[k].Role != NetRole.Avenue) ei = k;
                        if (ei < 0) continue;
                        long tid = -1; float tt = 0f;
                        float3 target = withCity ? CityPoint(existing, cityAt, out tid, out tt) : net.Nodes[n == ja ? jb : ja].Position;
                        if (withCity && tid < 0) continue;
                        if (Reaim(net, inc, ei, n, target, existing))
                        {
                            if (withCity)
                            {
                                net.Weld();
                                int q = RoadGraph.NodeAt(net, target.xz, 0.1f);
                                if (q >= 0) { net.Nodes[q].Attach = tid; net.Nodes[q].AttachT = tt; }
                            }
                            done = true;
                            removed++;
                            break;
                        }
                    }
                }
                if (!done)
                {
                    // Still nothing: a side street's junction slides along the road it meets, away from the other junction,
                    // until the full gap is kept (the street's end moves with it and still meets the road at 60° or more).
                    var slide = new List<int> { ja };
                    if (!withCity && jb >= 0) slide.Add(jb);
                    foreach (int n in slide)
                    {
                        float2 from = n == ja ? (withCity ? cityAt : net.Nodes[jb].Position.xz) : net.Nodes[ja].Position.xz;
                        float shift = pairNeed - math.distance(net.Nodes[n].Position.xz, from) + 1f;
                        bool Clear(float2 p)
                        {
                            foreach (int k in junctions)
                            {
                                if (k == n) continue;
                                float d = math.distance(net.Nodes[k].Position.xz, p);
                                if (d > 0.5f && d < minGap && RoadBetween(p, net.Nodes[k].Position.xz, OffRoad)) return false;
                            }
                            foreach (var c in cityJunctions)
                            {
                                float d = math.distance(c, p);
                                if (d > 1f && d < minGap && RoadBetween(p, c, OffRoad)) return false;
                            }
                            return true;
                        }
                        if (Slide(net, inc, n, from, shift, existing, Clear)) { done = true; removed++; break; }
                    }
                }
                if (!done && !withCity && jb >= 0 && inc[ja].Count == 1 && inc[jb].Count == 1 && net.Nodes[ja].Attach >= 0 && net.Nodes[ja].Attach == net.Nodes[jb].Attach)
                {
                    // two joins of ours onto the same city road: both streets meet it at one junction halfway between
                    done = MeetHalfway(net, inc, ja, jb, existing);
                    if (done) removed++;
                }
                if (!done)
                {
                    // nothing can go or move: leave this pair (the rating reports it), check the others
                    stuck.Add(net.Nodes[ja].Position.xz);
                    stuck.Add(withCity ? cityAt : net.Nodes[jb].Position.xz);
                    continue;
                }
                net.Weld();
            }
            return removed;
        }

        /// <summary>A short piece of street turning more than this (degrees, all its bends together) is a hook.</summary>
        public const float HookTurn = 100f;
        /// <summary>Streets longer than this (junction to junction) may wind as they like: an organic lane curling round.</summary>
        public const float HookChain = 150f;

        /// <summary>How far a curve turns along its length (degrees, absolute changes summed).</summary>
        public static float Turning(RoadCurve c)
        {
            float total = 0f;
            float2 prev = c.TangentXZ(0f);
            for (int i = 1; i <= 16; i++)
            {
                float2 d = c.TangentXZ(i / 16f);
                total += RoadGraph.Angle(prev, d);
                prev = d;
            }
            return total;
        }

        /// <summary>
        /// Hooks: a short street (junction to junction, or to its dead end, under <see cref="HookChain"/>) that turns back on
        /// itself (over <see cref="HookTurn"/>, bends and corners together) — a street that lost its neighbour curling round an
        /// acute corner, a link zig-zagging onto a city road. It becomes one straight piece when that crosses nothing and
        /// meets the roads at both ends at 60° or more; else it is given up when the network stays in one piece (or it leads
        /// nowhere); else it stays.
        /// </summary>
        public static int StraightenHooks(RoadNetwork net, List<ExistingRoad> existing, List<ExistingRoad> barriers, RoadObstacles obstacles, Func<NetRole, float> halfWidth)
        {
            int count = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                bool Real(int n) => inc[n].Count != 2 || RoadConnectivity.Attached(net.Nodes[n]);
                var seen = new HashSet<int>();
                bool any = false;
                for (int s = 0; s < net.Nodes.Count && !any; s++)
                {
                    if (inc[s].Count == 0 || !Real(s)) continue;
                    foreach (int start in inc[s])
                    {
                        if (seen.Contains(start)) continue;
                        var chain = new List<int>();
                        int cur = s, ei = start, t = -1;
                        float len = 0f, turn = 0f;
                        for (int guard = 0; guard < 64; guard++)
                        {
                            var e = net.Edges[ei];
                            chain.Add(ei);
                            len += RoadGraph.Length(e);
                            turn += Turning(e.Curve);
                            int nxt = RoadGraph.Other(e, cur);
                            if (Real(nxt)) { t = nxt; break; }
                            int next = inc[nxt][0] == ei ? inc[nxt][1] : inc[nxt][0];
                            turn += 180f - RoadGraph.Angle(RoadGraph.Leaving(e, nxt), RoadGraph.Leaving(net.Edges[next], nxt));
                            cur = nxt; ei = next;
                        }
                        foreach (int k in chain) seen.Add(k);
                        if (t < 0 || t == s || len > HookChain || turn <= HookTurn) continue;
                        bool fixedRole = false, winding = true;
                        NetRole role = NetRole.Alley;
                        foreach (int k in chain)
                        {
                            var r = net.Edges[k].Role;
                            if (r == NetRole.PerimeterRoad || r == NetRole.Avenue || r == NetRole.Roundabout) fixedRole = true;
                            if (r != NetRole.CulDeSac) winding = false;
                            if (Rank(r) > Rank(role)) role = r;
                        }
                        // the winding lanes of the organic styles (cul-de-sac roads) may curl as they like
                        if (fixedRole || winding) continue;
                        bool dead = (inc[s].Count == 1 && !RoadConnectivity.Attached(net.Nodes[s])) || (inc[t].Count == 1 && !RoadConnectivity.Attached(net.Nodes[t]));
                        if (!dead && Straight(net, inc, chain, s, t, role, existing, barriers, obstacles, halfWidth)) { count++; any = true; break; }
                        // a join onto a city road is only ever straightened, never given up (it may be the area's way out)
                        if (net.Nodes[s].Attach >= 0 || net.Nodes[t].Attach >= 0) continue;
                        int before = Parts(net);
                        foreach (int k in chain) net.Edges[k].Removed = true;
                        int after = Parts(net);
                        if (after > before && !dead) { foreach (int k in chain) net.Edges[k].Removed = false; continue; }
                        count++; any = true; break;
                    }
                }
                if (!any) break;
                net.Weld();
            }
            return count;
        }

        private static bool Straight(RoadNetwork net, List<int>[] inc, List<int> chain, int s, int t, NetRole role, List<ExistingRoad> existing,
            List<ExistingRoad> barriers, RoadObstacles obstacles, Func<NetRole, float> halfWidth)
        {
            float3 a = net.Nodes[s].Position, b = net.Nodes[t].Position;
            if (math.distance(a.xz, b.xz) < 25f) return false;
            var curve = RoadCurve.Straight(a, b);
            var line = RoadGraph.Sample(curve, 3f);
            var ignore = new HashSet<int>(chain);
            foreach (int k in inc[s]) ignore.Add(k);
            foreach (int k in inc[t]) ignore.Add(k);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            if (index.Crosses(line, 2f, ignore)) return false;
            if (RoadConnections.CrossesAny(line, existing, 2f) || RoadConnections.CrossesAny(line, barriers, 2f)) return false;
            if (obstacles != null && obstacles.Blocked(curve, halfWidth(role) + 1f)) return false;
            float2 dir = math.normalizesafe(b.xz - a.xz);
            foreach (var (n, d) in new[] { (s, dir), (t, -dir) })
            {
                foreach (int k in inc[n])
                    if (!chain.Contains(k) && RoadGraph.Angle(RoadGraph.Leaving(net.Edges[k], n), d) < 60f) return false;
                if (net.Nodes[n].Attach >= 0 && existing != null)
                    foreach (var r in existing)
                        if (r.Id == net.Nodes[n].Attach && math.abs(math.dot(r.Curve.TangentXZ(net.Nodes[n].AttachT), d)) > 0.5f) return false;
            }
            foreach (int k in chain) net.Edges[k].Removed = true;
            var first = net.Edges[chain[0]];
            net.Edges.Add(new RoadEdge(curve, role) { AssetOverride = first.AssetOverride, Category = first.Category });
            return true;
        }

        /// <summary>Two roads side by side closer than this (edge to edge, metres) leave a strip no building fits.</summary>
        public const float AlongsideGap = 16f;

        /// <summary>
        /// A piece running alongside another road (ours or the city's, not one it meets) closer than <see cref="AlongsideGap"/>
        /// over most of its length: two of three sample points (a quarter, half, three quarters along) beside a road under 30°.
        /// </summary>
        public static bool RunsAlongside(RoadCurve c, float hw, Func<float2, float2, float> gapTo)
        {
            int hits = 0;
            for (int i = 1; i <= 3; i++)
            {
                float t = i / 4f;
                if (gapTo(c.Position(t).xz, c.TangentXZ(t)) - hw < AlongsideGap) hits++;
            }
            return hits >= 2;
        }

        /// <summary>
        /// Streets of ours running alongside another road too close for a building row between (a street pushed against the
        /// perimeter, a staple round a block's top) are given up when the network stays in one piece; the dead ends that
        /// leaves are then cut back (<see cref="PruneStubsBesideRoads"/>).
        /// </summary>
        public static int GiveUpAlongside(RoadNetwork net, Func<NetRole, float> halfWidth, List<ExistingRoad> existing)
        {
            int count = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                bool any = false;
                for (int i = 0; i < net.Edges.Count && !any; i++)
                {
                    var e = net.Edges[i];
                    if (!RoadGraph.Car(e) || e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue || e.Role == NetRole.Roundabout) continue;
                    if (RoadGraph.Length(e) < 20f) continue;
                    if (RoadConnectivity.Attached(net.Nodes[e.N0]) || RoadConnectivity.Attached(net.Nodes[e.N1])) continue;   // a join onto the city stays
                    float Gap(float2 p, float2 tan)
                    {
                        float best = float.MaxValue;
                        for (int k = 0; k < net.Edges.Count; k++)
                        {
                            var o = net.Edges[k];
                            if (k == i || !RoadGraph.Car(o) || o.N0 == e.N0 || o.N0 == e.N1 || o.N1 == e.N0 || o.N1 == e.N1) continue;
                            float d = o.Curve.DistanceXZ(p, out float t);
                            if (t < 0.01f || t > 0.99f) continue;   // end on: the same street going on, not one beside it
                            if (math.abs(math.dot(o.Curve.TangentXZ(t), tan)) < 0.85f) continue;
                            best = math.min(best, d - halfWidth(o.Role));
                        }
                        if (existing != null)
                            foreach (var r in existing)
                            {
                                float d = r.Curve.DistanceXZ(p, out float t);
                                if (t < 0.01f || t > 0.99f) continue;
                                if (math.abs(math.dot(r.Curve.TangentXZ(t), tan)) < 0.85f) continue;
                                best = math.min(best, d - r.HalfWidth);
                            }
                        return best;
                    }
                    if (!RunsAlongside(e.Curve, halfWidth(e.Role), Gap)) continue;
                    int before = Parts(net);
                    e.Removed = true;
                    int after = Parts(net);
                    if (after > before) { e.Removed = false; continue; }
                    count++;
                    any = true;
                }
                if (!any) break;
                net.Weld();
            }
            return count;
        }

        /// <summary>
        /// The pieces of the car network, a piece no longer joined to the city counting as a thousand: a give-up that splits
        /// the network or cuts it off the city (its last link) shows as more.
        /// </summary>
        internal static int Parts(RoadNetwork net)
        {
            var id = RoadGraph.Components(net, RoadGraph.Car, out int count);
            if (count == 0) return 0;
            var onCity = new bool[count];
            for (int i = 0; i < net.Nodes.Count; i++)
                if (id[i] >= 0 && RoadConnectivity.Attached(net.Nodes[i])) onCity[id[i]] = true;
            int loose = 0;
            foreach (bool b in onCity) if (!b) loose++;
            return count + 1000 * loose;
        }

        /// <summary>
        /// A grid street (not a designed cul-de-sac) stopping a short way (≤ 60 m) before one of our roads, pointing at it,
        /// is carried on to meet it — when it then meets it at 60° or more, a full junction gap from the junctions on it,
        /// crossing nothing, not steeper than the design grade. A street that lost its far end ends on a junction, not in a
        /// dead end beside a road.
        /// </summary>
        public static int ExtendTips(RoadNetwork net, Func<NetRole, float> halfWidth, List<ExistingRoad> existing, List<ExistingRoad> barriers,
            RoadObstacles obstacles, Func<float2, float> height, float maxGrade)
        {
            int count = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                var index = new RoadGraph.EdgeIndex(net, RoadGraph.Car);
                bool any = false;
                for (int n = 0; n < net.Nodes.Count && !any; n++)
                {
                    if (inc[n].Count != 1 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                    var e = net.Edges[inc[n][0]];
                    if (e.Role == NetRole.CulDeSac || e.Role == NetRole.Roundabout) continue;
                    float2 p = net.Nodes[n].Position.xz;
                    float2 dir = -RoadGraph.Leaving(e, n);
                    if (!index.Ray(p, dir, 1f, 60f, new[] { inc[n][0] }, out int hitEdge, out float hd, out float2 hit)) continue;
                    var f = net.Edges[hitEdge];
                    f.Curve.DistanceXZ(hit, out float ht);
                    float fl = RoadGraph.Length(f);
                    if (ht * fl < 10f || (1f - ht) * fl < 10f) continue;   // (a junction at a bend's end would leave a crumb)
                    if (math.abs(math.dot(f.Curve.TangentXZ(ht), dir)) > 0.5f) continue;   // under 60°
                    // clear of the junctions on that road (ours and the city's)
                    bool crowded = false;
                    for (int k = 0; k < net.Nodes.Count && !crowded; k++)
                        if ((inc[k].Count >= 3 || (inc[k].Count > 0 && net.Nodes[k].Attach >= 0)) && math.distance(net.Nodes[k].Position.xz, hit) < MinJunctionGap) crowded = true;
                    if (crowded) continue;
                    float3 q = f.Curve.Position(ht);
                    var curve = RoadCurve.Straight(net.Nodes[n].Position, q);
                    var line = RoadGraph.Sample(curve, 3f);
                    if (RoadConnections.CrossesAny(line, existing, 2f) || RoadConnections.CrossesAny(line, barriers, 2f)) continue;
                    if (obstacles != null && obstacles.Blocked(curve, halfWidth(e.Role) + 1f)) continue;
                    if (height != null && RoadGraph.MaxGrade(curve, height) > maxGrade + 0.005f) continue;
                    RoadGraph.Split(net, hitEdge, ht);
                    net.Edges.Add(new RoadEdge(curve, e.Role) { AssetOverride = e.AssetOverride, Category = e.Category });
                    e.CapAtEnd = false;
                    count++;
                    any = true;
                }
                if (!any) break;
                net.Weld();
            }
            return count;
        }

        /// <summary>Clearance a dead end keeps from any road it does not join (edge to edge, metres): room for a building row.</summary>
        public const float StubClear = 22f;

        /// <summary>
        /// Dead ends stopping right beside another road (ours or the city's) they do not join — a stub left over by a give-up,
        /// a diagonal trimmed short of the edge — are cut back along their own street until clear, or removed when what is
        /// left would be shorter than a useful cul-de-sac.
        /// </summary>
        public static int PruneStubsBesideRoads(RoadNetwork net, Func<NetRole, float> halfWidth, List<ExistingRoad> existing, List<ExistingRoad> barriers = null)
        {
            int count = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                bool any = false;
                for (int n = 0; n < net.Nodes.Count; n++)
                {
                    if (inc[n].Count != 1 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                    // the street back to its junction
                    var chain = new List<int>(); var seq = new List<int> { n };
                    int cur = n, ei = inc[n][0], end = -1;
                    float len = 0f;
                    for (int guard = 0; guard < 64; guard++)
                    {
                        chain.Add(ei);
                        len += RoadGraph.Length(net.Edges[ei]);
                        int nxt = RoadGraph.Other(net.Edges[ei], cur);
                        seq.Add(nxt);
                        if (inc[nxt].Count != 2 || RoadConnectivity.Attached(net.Nodes[nxt])) { end = nxt; break; }
                        ei = inc[nxt][0] == ei ? inc[nxt][1] : inc[nxt][0];
                        cur = nxt;
                    }
                    if (end < 0 || inc[end].Count < 2) continue;   // a loose piece: DropFragments' job
                    if (chain.Exists(k => net.Edges[k].Role == NetRole.PerimeterRoad || net.Edges[k].Role == NetRole.Roundabout)) continue;
                    var own = new HashSet<int>(chain);
                    foreach (int k in inc[end]) own.Add(k);
                    float Clear(float2 p)
                    {
                        float best = float.MaxValue;
                        for (int k = 0; k < net.Edges.Count; k++)
                        {
                            var e = net.Edges[k];
                            if (!RoadGraph.Car(e) || own.Contains(k)) continue;
                            best = math.min(best, e.Curve.DistanceXZ(p, out _) - halfWidth(e.Role));
                        }
                        foreach (var list in new[] { existing, barriers })
                            if (list != null) foreach (var r in list) best = math.min(best, r.Curve.DistanceXZ(p, out _) - r.HalfWidth);
                        return best;
                    }
                    if (Clear(net.Nodes[n].Position.xz) >= StubClear) continue;
                    // walk back from the tip until clear
                    float cut = -1f;
                    for (float s = 4f; s < len - 1f; s += 4f)
                        if (Clear(AlongChain(net, chain, seq, s).xz) >= StubClear + 2f) { cut = s; break; }
                    if (cut < 0f || len - cut < 50f)
                    {
                        foreach (int k in chain) net.Edges[k].Removed = true;
                    }
                    else
                    {
                        float acc = 0f;
                        for (int i = 0; i < chain.Count; i++)
                        {
                            var e = net.Edges[chain[i]];
                            float l = RoadGraph.Length(e);
                            if (acc + l <= cut + 8f) { e.Removed = true; acc += l; continue; }   // (no crumb of street left over)
                            Trim(net, chain[i], seq[i], cut - acc, out _);
                            break;
                        }
                    }
                    count++;
                    any = true;
                }
                if (!any) break;
                net.Weld();
            }
            return count;
        }

        /// <summary>The point <paramref name="s"/> metres along a chain of edges from its first node.</summary>
        private static float3 AlongChain(RoadNetwork net, List<int> chain, List<int> seq, float s)
        {
            float acc = 0f;
            for (int i = 0; i < chain.Count; i++)
            {
                var e = net.Edges[chain[i]];
                float l = RoadGraph.Length(e);
                if (acc + l >= s)
                {
                    float t = RoadGraph.ParamAt(e.Curve, s - acc, fromEnd: e.N0 != seq[i]);
                    return e.Curve.Position(t);
                }
                acc += l;
            }
            return net.Nodes[seq[seq.Count - 1]].Position;
        }

        /// <summary>
        /// (Smarter roads) A junction where two car arms meet at under <paramref name="minAngle"/>° (a knife edge the game draws
        /// as a smear) gives up the lesser of the two arms (not the perimeter, a main road or a ring), when the car network
        /// stays in one piece. Returns the arms given up.
        /// </summary>
        public static int GiveUpKnifeEdges(RoadNetwork net, float minAngle = 40f, List<ExistingRoad> existing = null)
        {
            int removed = 0;
            // against the city's roads too: a plan arm leaving an existing road at a knife edge goes, with the small branch
            // beyond it when that branch hangs on it alone (under 200 m of road)
            if (existing != null && existing.Count > 0)
            {
                var inc0 = RoadGraph.Incidence(net, RoadGraph.Car);
                for (int n = 0; n < net.Nodes.Count; n++)
                {
                    if (net.Nodes[n].Attach < 0 || inc0[n].Count == 0) continue;
                    float2 p = net.Nodes[n].Position.xz;
                    var dirs = new List<float2>();
                    foreach (var r in existing)
                    {
                        float d = r.Curve.DistanceXZ(p, out float t);
                        if (d > 1f) continue;
                        if (t <= 0.001f) dirs.Add(r.Curve.TangentXZ(0f));
                        else if (t >= 0.999f) dirs.Add(-r.Curve.TangentXZ(1f));
                        else { var tan = r.Curve.TangentXZ(t); dirs.Add(tan); dirs.Add(-tan); }
                    }
                    foreach (int ei in inc0[n])
                    {
                        var e = net.Edges[ei];
                        if (!e.Alive || e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue || e.Role == NetRole.Roundabout) continue;
                        float2 leave = RoadGraph.Leaving(e, n);
                        bool knife = false;
                        foreach (var d in dirs) if (RoadGraph.Angle(leave, d) < minAngle) knife = true;
                        if (!knife) continue;
                        int before = Parts(net);
                        e.Removed = true;
                        var comp = RoadGraph.Components(net, RoadGraph.Car, out _);
                        int after = Parts(net);
                        if (after > before)
                        {
                            // the piece cut off: every car edge not in a component that reaches the city
                            var anchored = new HashSet<int>();
                            for (int k = 0; k < net.Nodes.Count; k++) if (comp[k] >= 0 && RoadConnectivity.Attached(net.Nodes[k])) anchored.Add(comp[k]);
                            float loose = 0f;
                            var looseEdges = new List<RoadEdge>();
                            foreach (var f in net.Edges)
                                if (RoadGraph.Car(f) && f.N0 >= 0 && comp[f.N0] >= 0 && !anchored.Contains(comp[f.N0])) { loose += RoadGraph.Length(f); looseEdges.Add(f); }
                            if (loose > 200f || looseEdges.Count == 0) { e.Removed = false; continue; }
                            foreach (var f in looseEdges) f.Removed = true;
                            removed += looseEdges.Count;
                        }
                        removed++;
                    }
                }
                net.Weld();
            }
            for (int pass = 0; pass < 32; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                bool any = false;
                for (int n = 0; n < net.Nodes.Count && !any; n++)
                {
                    if (inc[n].Count < 3) continue;
                    for (int a = 0; a < inc[n].Count && !any; a++)
                        for (int b = a + 1; b < inc[n].Count && !any; b++)
                        {
                            var ea = net.Edges[inc[n][a]]; var eb = net.Edges[inc[n][b]];
                            if (RoadGraph.Angle(RoadGraph.Leaving(ea, n), RoadGraph.Leaving(eb, n)) >= minAngle) continue;
                            if (ea.Role == NetRole.Roundabout || eb.Role == NetRole.Roundabout) continue;
                            // the lesser arm: not the perimeter / a main road, then the shorter
                            // (an avenue's last piece gives way too when nothing else can: never the perimeter)
                            var cands = new List<RoadEdge>();
                            foreach (var e in new[] { ea, eb })
                                if (e.Role != NetRole.PerimeterRoad) cands.Add(e);
                            cands.Sort((x, y) => (Rank(x.Role) * 1000f + RoadGraph.Length(x)).CompareTo(Rank(y.Role) * 1000f + RoadGraph.Length(y)));
                            foreach (var e in cands)
                            {
                                int before = Parts(net);
                                e.Removed = true;
                                int after = Parts(net);
                                if (after > before) { e.Removed = false; continue; }
                                removed++;
                                any = true;
                                break;
                            }
                        }
                }
                if (!any) break;
                net.Weld();
            }
            return removed;
        }

        private static int CountOurs(RoadNetwork net, List<int>[] inc, int n) => inc[n].Count;

        /// <summary>
        /// Slides junction <paramref name="n"/> <paramref name="shift"/> metres away from <paramref name="from"/> along the road
        /// it is on: a T of ours (the side street's end moves, the through road is split at the new point) or one street's join
        /// onto a city road (the join point moves along the city road). The side street must keep meeting the road at 60° or
        /// more, cross nothing, and the new point must be clear of other junctions.
        /// </summary>
        private static bool Slide(RoadNetwork net, List<int>[] inc, int n, float2 from, float shift, List<ExistingRoad> existing, Func<float2, bool> clear)
        {
            var node = net.Nodes[n];
            float2 away = math.normalizesafe(node.Position.xz - from);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            bool Fits(RoadEdge arm, float3 target, float2 roadTan, IEnumerable<int> ignoreAlso)
            {
                var moved = arm.N0 == n ? arm.Curve.WithEnds(target, arm.Curve.D) : arm.Curve.WithEnds(arm.Curve.A, target);
                float2 leave = arm.N0 == n ? moved.TangentXZ(0f) : -moved.TangentXZ(1f);
                if (math.abs(math.dot(leave, roadTan)) > 0.5f || SelfFolds(moved)) return false;
                var line = RoadGraph.Sample(moved, 3f);
                var ignore = new HashSet<int>(inc[n]);
                foreach (int k in inc[RoadGraph.Other(arm, n)]) ignore.Add(k);
                foreach (int k in ignoreAlso) ignore.Add(k);
                if (index.Crosses(line, 2f, ignore)) return false;
                arm.Curve = moved;
                return true;
            }
            if (inc[n].Count == 3 && node.Attach < 0)
            {
                // a T of ours: two arms run through, the third is the side street
                for (int i = 0; i < 3; i++)
                {
                    int armI = inc[n][i], a = inc[n][(i + 1) % 3], b = inc[n][(i + 2) % 3];
                    var arm = net.Edges[armI];
                    if (arm.Role == NetRole.PerimeterRoad || arm.Role == NetRole.Roundabout) continue;
                    if (RoadGraph.Angle(RoadGraph.Leaving(net.Edges[a], n), RoadGraph.Leaving(net.Edges[b], n)) < 150f) continue;
                    if (Rank(arm.Role) > math.min(Rank(net.Edges[a].Role), Rank(net.Edges[b].Role))) continue;
                    int along = math.dot(RoadGraph.Leaving(net.Edges[a], n), away) > 0.5f ? a : math.dot(RoadGraph.Leaving(net.Edges[b], n), away) > 0.5f ? b : -1;
                    if (along < 0) continue;
                    var el = net.Edges[along];
                    if (RoadGraph.Length(el) < shift + 15f) continue;
                    float t = RoadGraph.ParamAt(el.Curve, shift, fromEnd: el.N0 != n);
                    float3 target = el.Curve.Position(t);
                    if (!clear(target.xz)) continue;
                    if (!Fits(arm, target, el.Curve.TangentXZ(t), inc[n])) continue;
                    // the junction itself moves along the road (no sliver of road left behind it)
                    var eo = net.Edges[along == a ? b : a];
                    el.Curve = el.N0 == n ? el.Curve.WithEnds(target, el.Curve.D) : el.Curve.WithEnds(el.Curve.A, target);
                    eo.Curve = eo.N0 == n ? eo.Curve.WithEnds(target, eo.Curve.D) : eo.Curve.WithEnds(eo.Curve.A, target);
                    node.Position = target;
                    return true;
                }
                return false;
            }
            if (inc[n].Count == 1 && node.Attach >= 0 && existing != null)
            {
                // one street's join onto a city road: the join moves along the city road
                int ri = existing.FindIndex(r => r.Id == node.Attach);
                if (ri < 0) return false;
                var road = existing[ri].Curve;
                road.DistanceXZ(node.Position.xz, out float t0);
                float len = math.max(1f, road.LengthXZ(12));
                float dt = shift / len;
                float sign = math.dot(road.Position(math.saturate(t0 + 0.01f)).xz - road.Position(math.saturate(t0 - 0.01f)).xz, away) >= 0f ? 1f : -1f;
                float t1 = t0 + sign * dt;
                if (t1 < 0.02f || t1 > 0.98f) return false;
                float3 target = road.Position(t1);
                if (!clear(target.xz)) return false;
                var arm = net.Edges[inc[n][0]];
                var old = arm.Curve;
                if (!Fits(arm, target, road.TangentXZ(t1), Array.Empty<int>())) return false;
                if (RoadConnections.CrossesAny(RoadGraph.Sample(arm.Curve, 3f), existing, 2f)) { arm.Curve = old; return false; }
                node.Position = target;
                node.AttachT = t1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Two single streets joining the same city road a few metres apart are re-aimed onto one point of it halfway between,
        /// when both then meet it at 45° or more and cross nothing. The new node joins the road there.
        /// </summary>
        private static bool MeetHalfway(RoadNetwork net, List<int>[] inc, int ja, int jb, List<ExistingRoad> existing)
        {
            if (existing == null) return false;
            long id = net.Nodes[ja].Attach;
            int ri = existing.FindIndex(r => r.Id == id);
            if (ri < 0) return false;
            var road = existing[ri].Curve;
            road.DistanceXZ(net.Nodes[ja].Position.xz, out float ta);
            road.DistanceXZ(net.Nodes[jb].Position.xz, out float tb);
            float tm = 0.5f * (ta + tb);
            float3 q = road.Position(tm);
            float2 tan = road.TangentXZ(tm);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            var plans = new List<(RoadEdge e, int n, float3 far)>();
            foreach (int n in new[] { ja, jb })
            {
                var e = net.Edges[inc[n][0]];
                if (e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue) return false;
                int far = RoadGraph.Other(e, n);
                float3 a = net.Nodes[far].Position;
                if (math.distance(a.xz, q.xz) < 12f) return false;
                float2 back = math.normalizesafe(a.xz - q.xz);
                if (math.abs(math.dot(back, tan)) > 0.71f) return false;   // under 45° to the road
                var line = RoadGraph.Sample(RoadCurve.Straight(a, q), 3f);
                var ignore = new HashSet<int>(inc[far]);
                ignore.Add(inc[ja][0]); ignore.Add(inc[jb][0]);
                if (index.Crosses(line, 2f, ignore) || RoadConnections.CrossesAny(line, existing, 2f)) return false;
                plans.Add((e, n, a));
            }
            if (RoadGraph.Angle(plans[0].far.xz - q.xz, plans[1].far.xz - q.xz) < 40f) return false;
            foreach (var (e, n, far) in plans)
                e.Curve = e.N0 == n ? RoadCurve.Straight(q, far) : RoadCurve.Straight(far, q);
            net.Weld();
            int qn = RoadGraph.NodeAt(net, q.xz, 0.1f);
            if (qn >= 0) { net.Nodes[qn].Attach = id; net.Nodes[qn].AttachT = tm; net.Nodes[qn].Position = q; }
            return true;
        }

        /// <summary>The existing node at p: a road ending there (its id, t = 0 / 1) and its position.</summary>
        private static float3 CityPoint(List<ExistingRoad> existing, float2 p, out long id, out float t)
        {
            id = -1; t = 0f;
            if (existing != null)
                foreach (var r in existing)
                {
                    if (math.distance(r.Curve.A.xz, p) < 1f) { id = r.Id; t = 0f; return r.Curve.A; }
                    if (math.distance(r.Curve.D.xz, p) < 1f) { id = r.Id; t = 1f; return r.Curve.D; }
                }
            return new float3(p.x, 0f, p.y);
        }

        /// <summary>
        /// Moves the end of edge <paramref name="ei"/> at node <paramref name="n"/> onto <paramref name="target"/> (a straight piece from
        /// its far end), when the new piece is long enough, crosses nothing and meets the roads there at 40° or more.
        /// </summary>
        private static bool Reaim(RoadNetwork net, List<int>[] inc, int ei, int n, float3 target, List<ExistingRoad> existing)
        {
            var e = net.Edges[ei];
            int far = RoadGraph.Other(e, n);
            float3 a = net.Nodes[far].Position;
            if (math.distance(a.xz, target.xz) < 12f) return false;
            var curve = RoadCurve.Straight(a, target);
            var line = RoadGraph.Sample(curve, 3f);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            var ignore = new HashSet<int>(inc[far]);
            foreach (int k in inc[n]) ignore.Add(k);
            int tn = RoadGraph.NodeAt(net, target.xz, 0.5f);
            if (tn >= 0) foreach (int k in inc[tn]) ignore.Add(k);
            if (index.Crosses(line, 2f, ignore)) return false;
            float2 back = math.normalizesafe(a.xz - target.xz);
            if (tn >= 0 && RoadGraph.MinAngleAt(net, inc, tn, back) < 40f) return false;
            if (existing != null)
                foreach (var r in existing)
                {
                    float d = r.Curve.DistanceXZ(target.xz, out float t);
                    if (d > 1f) continue;
                    if (t <= 0.001f) { if (RoadGraph.Angle(r.Curve.TangentXZ(0f), back) < 40f) return false; }
                    else if (t >= 0.999f) { if (RoadGraph.Angle(-r.Curve.TangentXZ(1f), back) < 40f) return false; }
                    else if (math.abs(math.dot(r.Curve.TangentXZ(t), back)) > 0.77f) return false;
                }
            if (RoadConnections.CrossesAny(line, existing, 2f)) return false;
            e.Curve = e.N0 == n ? RoadCurve.Straight(target, a) : RoadCurve.Straight(a, target);
            return true;
        }

        private static float Importance(RoadNetwork net, List<int>[] inc, int n)
        {
            float k = inc[n].Count * 10f;
            if (net.Nodes[n].Attach >= 0) k += 100f;
            foreach (int ei in inc[n])
                if (net.Edges[ei].Role == NetRole.Avenue || net.Edges[ei].Role == NetRole.PerimeterRoad) k += 20f;
            return k;
        }

        /// <summary>
        /// (Smarter roads) New streets meeting an EXISTING road at less than 65° (a grid crossing a diagonal or winding road):
        /// the street's end turns to meet the road square (a smooth curve leaving the road at 90° and arriving at its far
        /// junction as before) when it is long enough and the bent curve stays clear; a street that cannot bend and meets
        /// the road under 45° is cut back from it (a turning circle) when the network stays in one piece. The existing road
        /// never moves. Returns (bent, cut).
        /// </summary>
        public static (int bent, int cut) SquareAtExisting(RoadNetwork net, List<ExistingRoad> existing, RoadObstacles obstacles, Func<NetRole, float> halfWidth)
        {
            if (existing == null || existing.Count == 0) return (0, 0);
            int bent = 0, cut = 0;
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var index = new RoadGraph.EdgeIndex(net, RoadGraph.Any);
            var toCut = new List<(int edge, int node)>();
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                var node = net.Nodes[n];
                if (node.Attach < 0 || inc[n].Count == 0) continue;
                // the existing road's directions here
                var road = new List<float2>();
                foreach (var r in existing)
                {
                    float d = r.Curve.DistanceXZ(node.Position.xz, out float t);
                    if (d > 1f) continue;
                    if (t <= 0.001f) road.Add(r.Curve.TangentXZ(0f));
                    else if (t >= 0.999f) road.Add(-r.Curve.TangentXZ(1f));
                    else { var tan = r.Curve.TangentXZ(t); road.Add(tan); road.Add(-tan); }
                }
                if (road.Count == 0) continue;
                foreach (int ei in inc[n])
                {
                    var e = net.Edges[ei];
                    if (!e.Alive || e.Role == NetRole.Roundabout || e.Connector) continue;
                    float2 leave = RoadGraph.Leaving(e, n);
                    float worst = 180f;
                    float2 nearest = road[0];
                    foreach (var rd in road)
                    {
                        float a = RoadGraph.Angle(leave, rd);
                        if (a < worst) { worst = a; nearest = rd; }
                    }
                    if (worst >= 65f) continue;
                    // a dead end continued straight is fine (the road goes on)
                    if (road.Count == 1 && RoadGraph.Angle(leave, -road[0]) < 20f) continue;
                    // square to the road, on the street's side
                    float2 nrm = new float2(-nearest.y, nearest.x);
                    if (math.dot(nrm, leave) < 0f) nrm = -nrm;
                    float len = RoadGraph.Length(e);
                    bool atStart = e.N0 == n;
                    var old = e.Curve;
                    // never bent onto another arm's line (two arms leaving the same way would overlap)
                    bool clash = false;
                    foreach (int ej in inc[n])
                        if (ej != ei && net.Edges[ej].Alive && RoadGraph.Angle(RoadGraph.Leaving(net.Edges[ej], n), nrm) < 40f) clash = true;
                    // under 45° the crossing is given up (bending that far makes an S beside the road and a sliver of land);
                    // when it cannot go (the network would split), it is bent square like the others
                    if (worst < 45f)
                    {
                        int before = Parts(net);
                        e.Removed = true;
                        int after = Parts(net);
                        if (after <= before) { cut++; continue; }
                        e.Removed = false;
                    }
                    if (len >= 36f && !clash)
                    {
                        float2 farDir = atStart ? e.Curve.TangentXZ(1f) : -e.Curve.TangentXZ(0f);   // direction of travel at the far end, away from n
                        var fitted = RoadCurve.Fit(node.Position, new float3(nrm.x, 0f, nrm.y), new float3(farDir.x, 0f, farDir.y),
                            atStart ? e.Curve.D : e.Curve.A);
                        e.Curve = atStart ? fitted : fitted.Reversed();
                        var line = RoadGraph.Sample(e.Curve, 3f);
                        var ignore = new HashSet<int>(inc[e.N0]);
                        foreach (int k in inc[e.N1]) ignore.Add(k);
                        bool ok = !index.Crosses(line, 2f, ignore) && !SelfFolds(e.Curve) && Turning(e.Curve) <= HookTurn - 25f
                            && (obstacles == null || !obstacles.Blocked(e.Curve, (halfWidth != null ? halfWidth(e.Role) : 8f) + 1f))
                            && !RoadConnections.CrossesAny(line, existing, 2f);
                        if (ok) { bent++; continue; }
                        e.Curve = old;
                    }
                }
            }
            foreach (var (ei, n) in toCut)
            {
                var e = net.Edges[ei];
                if (!e.Alive) continue;
                int before = Parts(net);
                e.Removed = true;
                int after = Parts(net);
                if (after > before) { e.Removed = false; continue; }
                cut++;
            }
            return (bent, cut);
        }

        /// <summary>
        /// A road that doubles back at a plain node (turning more than <paramref name="minTurn"/>°: a perimeter cut short by
        /// an existing road hooking into a street) loses its shorter side when the network stays in one piece — the
        /// dead-end pass then joins the free end to the city or ends it properly. Returns how many.
        /// </summary>
        public static int RemoveHairpins(RoadNetwork net, float minTurn = 110f, float maxLength = 160f)
        {
            int removed = 0;
            for (int pass = 0; pass < 4; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                int before = Parts(net);
                bool any = false;
                for (int n = 0; n < net.Nodes.Count && !any; n++)
                {
                    if (inc[n].Count != 2 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                    var a = net.Edges[inc[n][0]]; var b = net.Edges[inc[n][1]];
                    float turn = 180f - RoadGraph.Angle(RoadGraph.Leaving(a, n), RoadGraph.Leaving(b, n));
                    if (turn < minTurn) continue;
                    float la = RoadGraph.Length(a), lb = RoadGraph.Length(b);
                    var drop = la <= lb ? a : b;
                    if (math.min(la, lb) > maxLength) continue;
                    drop.Removed = true;
                    int after = Parts(net);
                    if (after > before) { drop.Removed = false; continue; }
                    removed++;
                    any = true;
                }
                if (!any) break;
            }
            return removed;
        }

        private static float Wrap(float a)
        {
            while (a < 0f) a += 2f * math.PI;
            while (a >= 2f * math.PI) a -= 2f * math.PI;
            return a;
        }

        /// <summary>A curve that turns more than 120° in total (a hook) is not a street.</summary>
        private static bool SelfFolds(RoadCurve c)
        {
            float total = 0f;
            float2 prev = c.TangentXZ(0f);
            for (int i = 1; i <= 16; i++)
            {
                float2 d = c.TangentXZ(i / 16f);
                total += RoadGraph.Angle(prev, d);
                prev = d;
            }
            return total > 120f;
        }
    }
}
