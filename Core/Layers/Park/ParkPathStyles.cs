// Path styles for the park (playtest 2, item 3: "paths need character, not minimum spanning"):
//   auto      the ParkManager planner (natural network by area)
//   garden    the same network, meandering: every edge is broken into gentle, irregular bends
//   curvy     the planner's network with strong smooth curves (lighter meander, long wavelength)
//   formal    straight axes: the long axis and the cross axis through the centre, each entrance
//             joined straight to the nearest axis — no curves
//   lakeside  a loop around the pond (needs the pond), entrances joined to it
//   scenic    one route from entrance to entrance that passes every feature spot (playground,
//             picnic, fountain…), meandering between them
// Every style returns a ParkPathGraph so curves, furniture and bins work unchanged.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    public static partial class ParkPathStyles
    {
        public static readonly string[] Styles = { "auto", "garden", "curvy", "formal", "lakeside", "scenic",
            "riverside", "figureEight", "spokeRing", "woodlandTrail", "boardwalk", "promenade", "circuit" };

        /// <summary>Breaks every edge into bends displaced sideways by value noise. <paramref name="ok"/> rejects bad points.</summary>
        public static ParkPathGraph Meander(ParkPathGraph g, IReadOnlyList<float2> gates, float amplitude, float wavelength, uint seed, Func<float2, bool> ok)
        {
            if (g == null || g.IsEmpty) return g;
            // P2-4 (the user, on hills: paths that "double back on each other and snap"): bends used to go on every roadmap link, and
            // on a hill the router's walk is many short links, so each got its own kink: a sawtooth. Now each run between junctions
            // is straightened first (keeping the grade the router won: a leg never gets steeper than the walk it replaces), and
            // only legs long enough for a bend get one, gentler the steeper the leg already is.
            var h = ParkPathPlanner.AmbientHeight;
            var segs = new List<float2>();
            int ci = 0;
            foreach (var chain in Chains(g))
            {
                var legs = Straighten(chain, h, ok);
                for (int i = 1; i < legs.Count; i++)
                {
                    var a = legs[i - 1]; var b = legs[i];
                    float len = math.distance(a, b), amp = amplitude;
                    if (h != null && len > 1f)
                        amp *= math.saturate(1f - math.abs(h(b) - h(a)) / len / TerrainSurvey.WalkGrade);
                    if (len < wavelength * 1.4f || amp < 0.6f) { segs.Add(a); segs.Add(b); continue; }
                    AddMeander(segs, a, b, amp, wavelength, Hashing.Mix(seed, (uint)(ci * 131 + i)), ok);
                }
                ci++;
            }
            var r = ParkPathGraph.FromSegments(segs, gates);
            return r.IsEmpty ? g : r;
        }

        /// <summary>The graph's runs between junctions, gates and dead ends, each as its points in order.</summary>
        internal static List<List<float2>> Chains(ParkPathGraph g)
        {
            var adj = g.Adjacency();
            var used = new bool[g.Edges.Count];
            var chains = new List<List<float2>>();
            bool Stop(int n) => adj[n].Count != 2 || g.Nodes[n].Kind == PathNodeKind.Gate;
            void Walk(int start, int edge)
            {
                var pts = new List<float2> { g.Nodes[start].Position };
                int at = start, e = edge;
                while (true)
                {
                    used[e] = true;
                    int next = g.Edges[e].A == at ? g.Edges[e].B : g.Edges[e].A;
                    pts.Add(g.Nodes[next].Position);
                    at = next;
                    if (Stop(at)) break;
                    int other = adj[at][0] == e ? adj[at][1] : adj[at][0];
                    if (used[other]) break;
                    e = other;
                }
                chains.Add(pts);
            }
            for (int n = 0; n < g.Nodes.Count; n++)
                if (Stop(n)) foreach (int e in adj[n]) if (!used[e]) Walk(n, e);
            for (int e = 0; e < g.Edges.Count; e++) if (!used[e]) Walk(g.Edges[e].A, e);   // closed loops with no junction
            return chains;
        }

        /// <summary>
        /// Douglas-Peucker with a grade rule: a run's kinks under 3.5 m go, but a straight leg never climbs faster than the walk
        /// grade or the steepest piece of the run it replaces (switchbacks the router made to gain height stay, as long legs).
        /// </summary>
        private static List<float2> Straighten(List<float2> pts, Func<float2, float> h, Func<float2, bool> ok)
        {
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            float Grade(float2 a, float2 b) { float d = math.distance(a, b); return h == null || d < 1f ? 0f : math.abs(h(b) - h(a)) / d; }
            void Split(int lo, int hi)
            {
                if (hi - lo < 2) return;
                int worst = -1; float dev = 0f, steepest = 0f;
                for (int k = lo + 1; k < hi; k++)
                {
                    float d = math.sqrt(SegmentIndex.DistSq(pts[k], pts[lo], pts[hi]));
                    if (d > dev) { dev = d; worst = k; }
                }
                for (int k = lo + 1; k <= hi; k++) steepest = math.max(steepest, Grade(pts[k - 1], pts[k]));
                bool fine = dev <= 3.5f && Grade(pts[lo], pts[hi]) <= math.max(TerrainSurvey.WalkGrade, steepest) * 1.05f;
                if (fine && ok != null)
                    for (int s = 1; s < 8 && fine; s++) fine = ok(math.lerp(pts[lo], pts[hi], s / 8f));
                if (fine) return;
                keep[worst] = true;
                Split(lo, worst);
                Split(worst, hi);
            }
            Split(0, pts.Count - 1);
            var o = new List<float2>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) o.Add(pts[i]);
            return o;
        }

        private static void AddMeander(List<float2> segs, float2 a, float2 b, float amplitude, float wavelength, uint seed, Func<float2, bool> ok)
        {
            float len = math.distance(a, b);
            // P2-4: one smooth wave (a bend each side per two wavelengths, as before), sampled every eighth of it, its swing at
            // most 7 % of the wave and fading in and out at the leg's ends. The old way put alternate corners every
            // wavelength: with short waves (botanical gardens: 5.5 m every 16 m) that was a zig-zag on any ground.
            float period = 2f * math.max(Layout.M(8f), wavelength);
            int waves = math.max(1, (int)math.round(len / period));
            float realPeriod = len / waves;
            var dir = (b - a) / math.max(0.001f, len);
            var n = new float2(-dir.y, dir.x);
            float amp = math.min(math.min(amplitude, len * 0.12f), realPeriod * 0.07f);
            int steps = math.max(2, waves * 8);
            float phase = Hashing.Unit(seed) < 0.5f ? 0f : math.PI;
            var prev = a;
            for (int i = 1; i <= steps; i++)
            {
                float2 p;
                if (i == steps) p = b;
                else
                {
                    float t = (float)i / steps, s = t * len;
                    var basePt = math.lerp(a, b, t);
                    float wave = (int)(s / realPeriod), k = 0.6f + 0.4f * Hashing.Unit(Hashing.Mix(seed, (uint)wave));
                    float fade = math.saturate(math.min(s, len - s) / (realPeriod * 0.5f));
                    float off = amp * k * fade * math.sin(2f * math.PI * s / realPeriod + phase);
                    p = basePt + n * off;
                    if (ok != null && !ok(p))
                    {
                        p = basePt + n * off * 0.4f;
                        if (!ok(p)) p = basePt;
                    }
                }
                segs.Add(prev); segs.Add(p);
                prev = p;
            }
        }

        /// <summary>
        /// Long axis + cross axis through the centre, aimed at the entrances (LeisureKit.FrameToward); each
        /// entrance joins an axis straight in from the edge (GateApproach), never sideways along it.
        /// </summary>
        public static ParkPathGraph Formal(LayerRegion region, IReadOnlyList<float2> gates, Func<float2, bool> ok)
        {
            LeisureKit.FrameToward(region, gates, out var centre, out var axis, out var side, out _, out _);
            return Formal(region, gates, centre, axis, side);
        }

        /// <summary>Formal axes for an explicit frame (the formal garden centres its round point on it).</summary>
        public static ParkPathGraph Formal(LayerRegion region, IReadOnlyList<float2> gates, float2 centre, float2 axis, float2 side)
        {
            var axes = new List<(float2 a, float2 b)>();
            if (LeisureKit.Chord(region, centre, axis, 4f, out var a0, out var b0)) axes.Add((a0, b0));
            if (LeisureKit.Chord(region, centre, side, 4f, out var a1, out var b1)) axes.Add((a1, b1));
            if (axes.Count == 0) return ParkPathGraph.Empty();
            var stops = new List<List<float>>();
            foreach (var ax in axes) stops.Add(new List<float> { 0f, 1f });
            // The two axes cross: split both there.
            if (axes.Count == 2 && Intersect(axes[0].a, axes[0].b, axes[1].a, axes[1].b, out float t0, out float t1))
            { stops[0].Add(t0); stops[1].Add(t1); }
            var segs = new List<float2>();
            float approach = math.max(Layout.M(8f, 5f), Layout.EdgeMargin + 4f);
            foreach (var gp in gates)
            {
                var route = GateApproach.Route(region, gp, axes, approach);
                if (route == null) continue;
                var foot = route[route.Count - 1];
                int bestAx = -1; float bestT = 0f, bestD = float.MaxValue;
                for (int i = 0; i < axes.Count; i++)
                {
                    var (a, b) = axes[i];
                    var q = SegmentIndex.ClosestPoint(foot, a, b);
                    float d = math.distance(foot, q);
                    if (d < bestD) { bestD = d; bestAx = i; bestT = math.dot(q - a, b - a) / math.max(1e-4f, math.lengthsq(b - a)); }
                }
                if (bestAx < 0) continue;
                var (aa, bb) = axes[bestAx];
                route[route.Count - 1] = math.lerp(aa, bb, bestT);
                for (int i = 1; i < route.Count; i++)
                    if (math.distance(route[i - 1], route[i]) > 0.5f) { segs.Add(route[i - 1]); segs.Add(route[i]); }
                stops[bestAx].Add(bestT);
            }
            for (int i = 0; i < axes.Count; i++)
            {
                var ts = stops[i]; ts.Sort();
                var (a, b) = axes[i];
                for (int k = 1; k < ts.Count; k++)
                {
                    if (ts[k] - ts[k - 1] < 1e-3f) continue;
                    segs.Add(math.lerp(a, b, ts[k - 1])); segs.Add(math.lerp(a, b, ts[k]));
                }
            }
            return ParkPathGraph.FromSegments(segs, gates);
        }

        /// <summary>A loop around the lake (offset outward), entrances joined to its nearest vertex. Empty when it does not fit.</summary>
        public static ParkPathGraph Lakeside(LayerRegion region, float2[] lake, IReadOnlyList<float2> gates, float offset = 5f)
        {
            if (lake == null || lake.Length < 3) return ParkPathGraph.Empty();
            List<float2[]> rings;
            try { rings = Poly.Offset(lake, -offset); } catch (Exception) { return ParkPathGraph.Empty(); }
            float2[] loop = null; float area = 0f;
            foreach (var r in rings) { float a = Poly.Area(r); if (a > area) { area = a; loop = r; } }
            if (loop == null) return ParkPathGraph.Empty();
            loop = Poly.Resample(loop, Layout.M(14f));
            foreach (var p in loop) if (!region.ContainsDisc(p, 2.5f)) return ParkPathGraph.Empty();
            var segs = new List<float2>();
            for (int i = 0; i < loop.Length; i++) { segs.Add(loop[i]); segs.Add(loop[(i + 1) % loop.Length]); }
            JoinGates(region, segs, gates, 0, gates.Count, (a, b) => !CrossesRing(a, b, lake));
            return ParkPathGraph.FromSegments(segs, gates);
        }

        /// <summary>
        /// Joins gates [from, to) to the segment list (pairs) straight in from the edge (GateApproach, s9): the
        /// host segment is split at the junction so the graph stays connected. Gates already on it are skipped.
        /// </summary>
        public static void JoinGates(LayerRegion region, List<float2> segs, IReadOnlyList<float2> gates, int from, int to, Func<float2, float2, bool> clear = null)
        {
            float approach = math.max(Layout.M(8f, 5f), Layout.EdgeMargin + 4f);
            for (int gi = from; gi < to; gi++)
            {
                var g = gates[gi];
                var spine = new List<(float2 a, float2 b)>();
                for (int i = 0; i + 1 < segs.Count; i += 2) spine.Add((segs[i], segs[i + 1]));
                var route = GateApproach.Route(region, g, spine, approach, clear);
                if (route == null || route.Count < 2) continue;
                var foot = route[route.Count - 1];
                int host = -1; float bd = float.MaxValue;
                for (int i = 0; i < spine.Count; i++)
                {
                    float d = math.sqrt(SegmentIndex.DistSq(foot, spine[i].a, spine[i].b));
                    if (d < bd) { bd = d; host = i; }
                }
                if (host < 0) continue;
                var (a, b) = spine[host];
                foot = SegmentIndex.ClosestPoint(foot, a, b);
                if (math.distance(foot, a) < 1.5f) foot = a;
                else if (math.distance(foot, b) < 1.5f) foot = b;
                else { segs[host * 2 + 1] = foot; segs.Add(foot); segs.Add(b); }
                route[route.Count - 1] = foot;
                for (int i = 1; i < route.Count; i++) { segs.Add(route[i - 1]); segs.Add(route[i]); }
            }
        }

        /// <summary>
        /// One route that passes every destination (rim points), entrance to entrance (or a loop back to a
        /// single entrance); other entrances join the nearest route point. Legs meander.
        /// </summary>
        public static ParkPathGraph Scenic(LayerRegion region, IReadOnlyList<float2> gates, IReadOnlyList<(float2 c, float r)> stops,
            uint seed, Func<float2, bool> ok)
        {
            if (gates.Count == 0 || stops.Count == 0) return ParkPathGraph.Empty();
            var left = new List<(float2 c, float r)>(stops);
            var route = new List<float2> { gates[0] };
            // s9: every entrance on the route starts with a straight approach along its edge normal.
            float approach = math.max(Layout.M(8f, 5f), Layout.EdgeMargin + 4f);
            var cur = gates[0] + GateApproach.Inward(region, gates[0]) * approach;
            if (!region.ContainsDisc(cur, 1f)) cur = gates[0] + GateApproach.Inward(region, gates[0]) * Layout.M(8f);
            route.Add(cur);
            while (left.Count > 0)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < left.Count; i++) { float d = math.distance(cur, left[i].c); if (d < bd) { bd = d; best = i; } }
                var s = left[best]; left.RemoveAt(best);
                var rim = s.c + math.normalizesafe(cur - s.c, new float2(1, 0)) * (s.r + 2.5f);
                if (!region.ContainsDisc(rim, 2f)) continue;
                route.Add(rim); cur = rim;
            }
            if (gates.Count >= 2)
            {
                var last = gates[gates.Count - 1];
                var app = last + GateApproach.Inward(region, last) * approach;
                if (region.ContainsDisc(app, 1f)) route.Add(app);
                route.Add(last);
            }
            else route.Add(route[1]);   // loop back to the entry node
            var segs = new List<float2>();
            for (int i = 1; i < route.Count; i++)
            {
                if (region.CrossesBoundary(route[i - 1], route[i], true)) continue;
                // The approach legs at the entrances stay straight.
                bool straight = i == 1 || (gates.Count >= 2 && i == route.Count - 1);
                if (straight) { segs.Add(route[i - 1]); segs.Add(route[i]); }
                else AddMeander(segs, route[i - 1], route[i], Layout.M(7f), Layout.M(22f), Hashing.Mix(seed, (uint)i), ok);
            }
            JoinGates(region, segs, gates, 1, gates.Count - 1);
            return ParkPathGraph.FromSegments(segs, gates);
        }

        private static bool CrossesRing(float2 a, float2 b, float2[] ring)
        {
            for (int i = 0; i < ring.Length; i++)
                if (Poly.SegmentsIntersect(a, b, ring[i], ring[(i + 1) % ring.Length])) return true;
            return false;
        }

        private static bool Intersect(float2 p, float2 p2, float2 q, float2 q2, out float t, out float u)
        {
            var r = p2 - p; var s = q2 - q;
            float den = Poly.Cross(r, s);
            t = u = 0f;
            if (math.abs(den) < 1e-6f) return false;
            t = Poly.Cross(q - p, s) / den;
            u = Poly.Cross(q - p, r) / den;
            return t > 0.01f && t < 0.99f && u > 0.01f && u < 0.99f;
        }
    }
}
