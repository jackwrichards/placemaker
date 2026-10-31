// Switchbacks (P2-4 round 3; the lead: "real switchbacks that follow the contour, with no wavy grading"). On a mountain the
// roadmap router gains height by zig-zagging between its grid nodes: Vs every 10-20 m, legs at whatever grade the nodes
// gave, and the site harmony then cut and filled the walk into a wavy profile. A real mountain walk is traced: long legs
// that climb at one gentle grade along the contour, joined by round hairpin turns. Every run of a walk that has to gain
// more height than the walk grade allows is traced that way here, whichever style laid it.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    public static partial class ParkPathStyles
    {
        /// <summary>The grade a traced leg climbs at (under the walk grade, so the leg's own bumps stay under it).</summary>
        public const float SwitchbackGrade = 0.065f;
        private const float HairpinRadius = 5.5f, TraceStep = 2f;
        private static readonly (float grade, float legMax)[] Tries =
            { (SwitchbackGrade, float.MaxValue), (SwitchbackGrade, 30f), (SwitchbackGrade, 18f), (TerrainSurvey.WalkGrade, 26f), (TerrainSurvey.WalkGrade, 14f) };
        [ThreadStatic] private static float _g, _legMax;
        [ThreadStatic] private static IReadOnlyList<float2> _gates;
        /// <summary>Tests: the walks as the styles laid them, to compare.</summary>
        [ThreadStatic] public static bool SwitchbacksOff;
        [ThreadStatic] public static List<string> TraceLog;

        /// <summary>
        /// Retraces every run of the walks that climbs faster than the walk grade as legs along the contour at
        /// <see cref="SwitchbackGrade"/> joined by hairpins. <paramref name="ok"/> rejects points a walk may not take.
        /// <paramref name="traced"/>: how many runs were traced; <paramref name="hairpins"/>: their turns.
        /// </summary>
        public static ParkPathGraph Switchbacks(ParkPathGraph g, IReadOnlyList<float2> gates, Func<float2, float> h, Func<float2, bool> ok,
            out int traced, out int hairpins)
        {
            traced = hairpins = 0;
            if (g == null || g.IsEmpty || h == null || SwitchbacksOff) return g;
            _gates = gates;
            var chains = Chains(g);
            var runs = new List<List<float2>>();
            foreach (var c in chains) runs.AddRange(SplitAtCrests(c, h));
            // every run's segments, so a traced run keeps off the others
            var others = new List<(float2 a, float2 b, int run)>();
            for (int r = 0; r < runs.Count; r++)
                for (int i = 1; i < runs[r].Count; i++) others.Add((runs[r][i - 1], runs[r][i], r));
            var lines = new List<List<float2>>(runs);
            bool any = false;
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                List<float2> line = null;
                if (!NeedsTrace(run, h)) TraceLog?.Add($"RUN {r} len {Length(run):0} dh {math.abs(h(run[run.Count - 1]) - h(run[0])):0.0} skip");
                if (NeedsTrace(run, h))
                {
                    // up from the bottom or down from the top, first leg either way: the shortest trace that reaches the other end.
                    // Long legs across the whole corridor first; where those dead-end, a tighter ladder of shorter legs, then the
                    // walk grade itself.
                    int turns = 0;
                    foreach (var (grade, legMax) in Tries)
                    {
                        _g = grade; _legMax = legMax;
                        foreach (bool down in new[] { false, true })
                            foreach (bool flip in new[] { false, true })
                            {
                                var t = TraceRun(run, h, ok, others, r, down, flip, out int tt);
                                if (t != null && (line == null || Length(t) < Length(line))) { line = t; turns = tt; }
                            }
                        if (line != null) break;
                    }
                    if (line != null)
                    {
                        // never at the cost of the network: every walk still joined (and every entrance on it)
                        lines[r] = line;
                        if (math.distancesq(line[0], run[0]) > 1e-4f || math.distancesq(line[line.Count - 1], run[run.Count - 1]) > 1e-4f
                            || !KeepsJunctions(g, Build(lines, gates))) { lines[r] = run; line = null; }
                    }
                    if (TraceLog != null && line != null)
                    {
                        float st = 0f, all = 0f;
                        var rs = Resample(line, 4f);
                        for (int i = 1; i < rs.Count; i++) { float d = math.distance(rs[i - 1], rs[i]); all += d; if (d > 0.5f && math.abs(h(rs[i]) - h(rs[i - 1])) / d > 0.10f) st += d; }
                        TraceLog.Add($"  traced line {all:0} m, steep {st:0} m");
                    }
                    if (TraceLog != null) TraceLog.Add($"RUN {r} len {Length(run):0} dh {math.abs(h(run[run.Count - 1]) - h(run[0])):0.0} {(line != null ? "TRACED" : "failed")} {run[0]}->{run[run.Count - 1]}");
                    if (line != null)
                    {
                        traced++; hairpins += turns; any = true;
                        // later runs keep off this one as traced
                        others.RemoveAll(o => o.run == r);
                        for (int i = 1; i < line.Count; i++) others.Add((line[i - 1], line[i], r));
                    }
                }
            }
            if (!any) return g;
            var res = Build(lines, gates);
            return res.IsEmpty ? g : res;
        }

        private static ParkPathGraph Build(List<List<float2>> lines, IReadOnlyList<float2> gates)
        {
            var segs = new List<float2>();
            foreach (var pts in lines) for (int i = 1; i < pts.Count; i++) { segs.Add(pts[i - 1]); segs.Add(pts[i]); }
            return ParkPathGraph.FromSegments(segs, gates);
        }

        /// <summary>A run that climbs more than 5 m and has a 12 m stretch steeper than the walk grade.</summary>
        private static bool NeedsTrace(List<float2> run, Func<float2, float> h)
        {
            if (run.Count < 2) return false;
            if (math.abs(h(run[run.Count - 1]) - h(run[0])) < 5f) return false;   // a bump (rugged land) is graded, not wound
            var p = Resample(run, 12f);
            for (int i = 1; i < p.Count; i++)
            {
                float d = math.distance(p[i - 1], p[i]);
                if (d > 4f && math.abs(h(p[i]) - h(p[i - 1])) / d > TerrainSurvey.WalkGrade * 1.25f) return true;
            }
            return false;
        }

        /// <summary>
        /// Splits a run where it tops a crest or bottoms a dip (over 3 m either way; a straight link across a valley is first
        /// given a point every 4 m), so each piece only climbs.
        /// </summary>
        private static List<List<float2>> SplitAtCrests(List<float2> pts, Func<float2, float> h)
        {
            var res = new List<List<float2>>();
            var dense = new List<float2> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                int n = math.max(1, (int)(math.distance(pts[i - 1], pts[i]) / 4f));
                for (int k = 1; k <= n; k++) dense.Add(math.lerp(pts[i - 1], pts[i], k / (float)n));
            }
            pts = dense;
            if (pts.Count < 3) { res.Add(pts); return res; }
            int start = 0, ext = 0; float hs = h(pts[0]); int dir = 0; float he = hs;
            for (int i = 1; i < pts.Count; i++)
            {
                float hi = h(pts[i]);
                if (dir == 0) { if (math.abs(hi - hs) > 0.5f) { dir = hi > hs ? 1 : -1; ext = i; he = hi; } continue; }
                if ((hi - he) * dir > 0f) { ext = i; he = hi; continue; }
                // turned back by more than 3 m from the extreme: cut there
                // (never into a piece too short to wind: a crest near a junction stays inside the run)
                if ((he - hi) * dir > 3f && ext - start >= 6 && pts.Count - 1 - ext >= 6)
                {
                    res.Add(pts.GetRange(start, ext - start + 1));
                    start = ext; hs = he; dir = -dir; ext = i; he = hi;
                }
            }
            res.Add(pts.GetRange(start, pts.Count - start));
            return res;
        }

        /// <summary>
        /// Moves a run's end <paramref name="i"/> along the run (<paramref name="step"/>: +1 from the start, -1 from the end) to the
        /// first point a walk may take, at least 6 m in (an entrance on the outline), inserting it into the run.
        /// </summary>
        private static bool Inward(List<float2> pts, ref int i, int step, Func<float2, bool> ok, float minIn = 6f)
        {
            float walked = 0f;
            for (int k = i; k + step >= 0 && k + step < pts.Count; k += step)
            {
                var a = pts[k]; var b = pts[k + step];
                float len = math.distance(a, b);
                for (float t = 1f; t <= len; t += 1f)
                {
                    var p = math.lerp(a, b, t / len);
                    if (walked + t < minIn || ok != null && !ok(p)) continue;
                    if (step > 0) { pts.Insert(k + 1, p); i = k + 1; }
                    else { pts.Insert(k, p); i = k; }
                    return true;
                }
                walked += len;
            }
            return false;
        }

        /// <summary>
        /// Every junction, entrance and end of the walks as it was (same place, same number of walks meeting): a trace never drops a
        /// piece of the network (a dead end the graph prunes) or splits it.
        /// </summary>
        private static bool KeepsJunctions(ParkPathGraph before, ParkPathGraph after)
        {
            if (after.IsEmpty || Components(after) > Components(before)) return false;
            var adjB = before.Adjacency(); var adjA = after.Adjacency();
            for (int i = 0; i < before.Nodes.Count; i++)
            {
                if (adjB[i].Count == 2 && before.Nodes[i].Kind != PathNodeKind.Gate) continue;
                var p = before.Nodes[i].Position;
                int found = -1;
                for (int j = 0; j < after.Nodes.Count; j++) if (math.distancesq(after.Nodes[j].Position, p) < 0.25f) { found = j; break; }
                if (found < 0 || adjA[found].Count != adjB[i].Count) return false;
            }
            return true;
        }

        private static int Components(ParkPathGraph g)
        {
            var adj = g.Adjacency();
            var seen = new bool[g.Nodes.Count];
            int n = 0;
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                if (seen[i] || adj[i].Count == 0) continue;
                n++;
                var stack = new Stack<int>(); stack.Push(i); seen[i] = true;
                while (stack.Count > 0)
                {
                    int a = stack.Pop();
                    foreach (int e in adj[a])
                    {
                        int b = g.Edges[e].A == a ? g.Edges[e].B : g.Edges[e].A;
                        if (!seen[b]) { seen[b] = true; stack.Push(b); }
                    }
                }
            }
            return n;
        }

        private static float Length(List<float2> l) { float d = 0f; for (int i = 1; i < l.Count; i++) d += math.distance(l[i - 1], l[i]); return d; }

        private static List<float2> Resample(List<float2> pts, float step)
        {
            var o = new List<float2> { pts[0] };
            float carry = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                float len = math.distance(a, b), t = step - carry;
                while (t <= len) { o.Add(math.lerp(a, b, t / len)); t += step; }
                carry = len - (t - step);
            }
            if (math.distance(o[o.Count - 1], pts[pts.Count - 1]) > 0.5f) o.Add(pts[pts.Count - 1]);
            return o;
        }

        /// <summary>
        /// Traces a climbing run from its low end to its high end: legs at <see cref="SwitchbackGrade"/> (or the retry grade) across the slope (the
        /// contour direction tilted uphill just enough), each ending at the corridor's side, a wall of what it may not cross or a
        /// path, then a hairpin round to the uphill side and back; straight to the top once the top is within the grade. A run that
        /// starts or ends at an entrance keeps its straight approach. Null when the trace cannot reach the top.
        /// </summary>
        private static List<float2> TraceRun(List<float2> run, Func<float2, float> h, Func<float2, bool> ok, List<(float2 a, float2 b, int run)> others, int self,
            bool down, bool flip, out int turns)
        {
            turns = 0;
            // from the bottom up (or the top down)
            bool rev = (h(run[run.Count - 1]) < h(run[0])) != down;
            var pts = new List<float2>(run);
            if (rev) pts.Reverse();
            // the fixed ends: a junction, or an entrance's approach (its first leg); a trace starts and ends where a walk may go
            // (an entrance keeps its first 16 m straight in: the entrance's own approach, which later steps square up to the street)
            bool AtGate(float2 p) { if (_gates != null) foreach (var gp in _gates) if (math.distancesq(gp, p) < 1f) return true; return false; }
            int i0 = 0;
            if (AtGate(pts[0])) { if (!Inward(pts, ref i0, 1, null, 16f)) return null; }
            else if (pts.Count > 2 && math.distance(pts[0], pts[1]) < 14f) i0 = 1;
            int i1 = pts.Count - 1;
            if (AtGate(pts[i1])) { if (!Inward(pts, ref i1, -1, null, 16f)) return null; }
            else if (pts.Count > 3 && i1 - 1 > i0 && math.distance(pts[i1], pts[i1 - 1]) < 14f) i1--;
            if (ok != null && !ok(pts[i0])) { if (!Inward(pts, ref i0, 1, ok)) { TraceLog?.Add("start"); return null; } i1++; }
            if (ok != null && !ok(pts[i1]) && !Inward(pts, ref i1, -1, ok)) { TraceLog?.Add("end"); return null; }
            if (i1 <= i0) return null;
            float2 P = pts[i0], Q = pts[i1];
            float hq = h(Q);
            var axis = Q - P; float span = math.length(axis);
            if (span < 16f) { TraceLog?.Add("short"); return null; }   // a short piece is graded, not wound into a curl
            axis /= span;
            var side = new float2(-axis.y, axis.x);
            // the corridor: wide enough for real legs, never wider than the run is long
            float half = math.clamp(span * 0.45f, 14f, 32f);
            var line = new List<float2>();
            int arcFrom = -1;
            for (int i = 0; i <= i0; i++) line.Add(pts[i]);
            var x = P;
            float2 Grad(float2 p)
            {
                const float e = 2f;
                return new float2(h(p + new float2(e, 0f)) - h(p - new float2(e, 0f)), h(p + new float2(0f, e)) - h(p - new float2(0f, e))) / (2f * e);
            }
            bool Gentle(float2 a, float2 b)
            {
                int n = math.max(1, (int)math.ceil(math.distance(a, b) / 4f));
                float seg = math.distance(a, b) / n, prev = h(a);
                for (int k = 1; k <= n; k++)
                {
                    float hk = h(math.lerp(a, b, k / (float)n));
                    if (seg > 0.5f && math.abs(hk - prev) / seg > TerrainSurvey.WalkGrade * 1.25f) return false;
                    prev = hk;
                }
                return true;
            }
            bool Clear(float2 a, float2 b)
            {
                if (ok != null && !ok(b)) return false;
                foreach (var (oa0, ob0, r) in others)
                {
                    if (r == self) continue;
                    // a walk leaving the run's own ends: only crossing it away from the shared end counts
                    var oa = oa0; var ob = ob0;
                    bool touches = false;
                    foreach (var e in new[] { P, Q })
                    {
                        float l = math.distance(oa, ob);
                        if (l < 0.1f) continue;
                        if (math.distancesq(oa, e) < 1f) { touches = true; oa = l > 4f ? oa + (ob - oa) * (4f / l) : ob; }
                        else if (math.distancesq(ob, e) < 1f) { touches = true; ob = l > 4f ? ob + (oa - ob) * (4f / l) : oa; }
                    }
                    if (touches) { if (math.distancesq(oa, ob) > 0.01f && Poly.SegmentsIntersect(a, b, oa, ob)) return false; continue; }
                    bool nearEnd = math.distancesq(b, P) < 100f || math.distancesq(b, Q) < 100f;   // round a junction only a crossing counts
                    if (Poly.SegmentsIntersect(a, b, oa, ob) || !nearEnd && SegmentIndex.DistSq(b, oa, ob) < 49f) return false;
                }
                // never back over the traced line (two legs closer than a hairpin; the turn just made aside)
                for (int k = 1; k + 2 < line.Count; k++)
                {
                    if (arcFrom >= 0 && k >= arcFrom - 1 && k <= arcFrom + 7) continue;
                    if (Poly.SegmentsIntersect(a, b, line[k - 1], line[k])) return false;
                    if (k + 8 < line.Count && SegmentIndex.DistSq(b, line[k - 1], line[k]) < (HairpinRadius * 1.4f) * (HairpinRadius * 1.4f)) return false;
                }
                return true;
            }
            // the first leg heads across the slope toward the top's side
            var g0 = Grad(x);
            var c0 = new float2(-g0.y, g0.x);
            float sigma = (math.dot(c0, Q - P) >= 0f ? 1f : -1f) * (flip ? -1f : 1f);
            int legSteps = 0;
            for (int step = 0; step < 900; step++)
            {
                float dq = math.distance(x, Q), rise = hq - h(x);
                // the top within reach (or reached): straight there when the ground on the way is never steeper than a walk
                if (dq < 4f || math.abs(rise) <= _g * 1.15f * dq && Gentle(x, Q))
                {
                    line.Add(Q);
                    for (int i = i1 + 1; i < pts.Count; i++) line.Add(pts[i]);
                    if (rev) line.Reverse();
                    return line;
                }
                var gr = Grad(x); float m = math.length(gr);
                float2 dir;
                if (m <= _g && Gentle(x, x + math.normalizesafe(Q - x) * math.min(dq, 12f))) dir = math.normalizesafe(Q - x);
                else
                {
                    // climb at the leg grade while the top is above; at its height run level along the contour toward it
                    var f = gr / math.max(m, 1e-4f);
                    float want = rise > 1f ? _g : rise < -1f ? -_g : 0f;
                    var c0n = new float2(-f.y, f.x);
                    if (math.abs(rise) <= 1f) sigma = math.dot(c0n, Q - x) >= 0f ? 1f : -1f;
                    var c = c0n * sigma;
                    float s = math.clamp(want / math.max(m, _g), -1f, 1f);
                    dir = c * math.sqrt(1f - s * s) + f * s;
                }
                var nx = x + dir * TraceStep;
                float lat = math.dot(nx - P, side), along = math.dot(nx - P, axis);
                // out of the corridor (a step heading back into it is fine: a hairpin may have swung out)
                float lat0 = math.dot(x - P, side), along0 = math.dot(x - P, axis);
                bool outside = math.abs(lat) > half && math.abs(lat) > math.abs(lat0) + 0.2f
                    || along < -half * 0.5f && along < along0 || along > span + half * 0.5f && along > along0;
                if (!outside && legSteps * TraceStep < _legMax && Clear(x, nx))
                {
                    x = nx; line.Add(x); legSteps++;
                    continue;
                }
                // the leg ends: a hairpin round to the uphill side, unless the leg was too short to turn (a dead end)
                if (legSteps < 6 || m <= _g) { TraceLog?.Add($"dead end at {x} legSteps {legSteps} m {m:0.00} outside {outside} turns {turns} from {P} to {Q}"); return null; }
                // the turn bulges ahead of the leg: it starts a few steps back from what ended it
                int back = math.min(3, legSteps - 4);
                if (back > 0) { line.RemoveRange(line.Count - back, back); x = line[line.Count - 1]; legSteps -= back; }
                var f2 = gr / m;
                var left = new float2(-dir.y, dir.x);
                // round toward the other end's height: uphill climbing, downhill descending
                var up = (math.dot(left, f2) >= 0f) != (hq < h(x)) ? left : -left;
                float turnSign = Poly.Cross(dir, up) > 0f ? 1f : -1f;   // sweep from the heading toward the uphill side
                List<float2> arc = null;
                foreach (float rad in new[] { HairpinRadius, HairpinRadius * 0.7f })
                {
                    var centre = x + up * rad;
                    float a0 = math.atan2(x.y - centre.y, x.x - centre.x);
                    arc = new List<float2>();
                    for (int k = 1; k <= 6 && arc != null; k++)
                    {
                        float a = a0 + turnSign * math.PI * k / 6f;
                        var p = centre + new float2(math.cos(a), math.sin(a)) * rad;
                        if (ok != null && !ok(p)) arc = null; else arc.Add(p);
                    }
                    if (arc != null) break;
                }
                if (arc == null) { TraceLog?.Add("arc"); return null; }
                arcFrom = line.Count;
                line.AddRange(arc);
                x = arc[arc.Count - 1];
                sigma = -sigma; legSteps = 0; turns++;
                if (turns > 40) { TraceLog?.Add("turns"); return null; }
            }
            TraceLog?.Add("steps");
            return null;
        }
    }
}
