// Outgrow benchmark: the fences recorded passes leave, checked for what the user sees (--fences <passes dir or file> [--knob
// NAME=VALUE] [--every]): every pass planned again, its fences (the houses' lines where the plan leaves them, a neighbour
// another pass planned as it stands, the closing pieces) measured near the gardens it edited, summed:
//   gaps      two loose fence ends close together (a way through nobody meant)
//   crossings two fences crossing
//   doubles   two fences side by side (within 0.8 m, along each other for 1 m or more)
//   inside    a fence standing inside a garden, away from its edges (left over from where the garden ended before)
//   open      metres of a garden's edge with no fence along it, not the street front, a road, a house or another lot
//   stubs     a fence standing on its own, shorter than 5 m (a lone piece)
//   short     a garden's edges shorter than a short piece (2 m) where it turns more than 25 degrees (a piece sticks out there)
// --every: each pass's own line.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    internal static class Fences
    {
        private sealed class Bit { public long House; public bool Piece; public float2 A, B; public float W; }

        public sealed class Tally
        {
            public int Passes, Pieces, Gaps, Crossings, Doubles, Inside, Stubs, Short;
            public float Open, Fenced;
            /// <summary>Where each problem is (for a picture): gap, double, inside, open, stub, short.</summary>
            public readonly List<(float2 At, string Kind)> Marks = new List<(float2, string)>();
            public readonly List<float> GapLengths = new List<float>();
            public void Add(Tally t) { Passes += t.Passes; Pieces += t.Pieces; Gaps += t.Gaps; Crossings += t.Crossings; Doubles += t.Doubles; Inside += t.Inside; Stubs += t.Stubs; Short += t.Short; Open += t.Open; Fenced += t.Fenced; GapLengths.AddRange(t.GapLengths); }
            public override string ToString() => $"{Passes} passes: {Pieces} pieces, {Fenced:F0} m of fences; gaps {Gaps}, crossings {Crossings}, doubles {Doubles}, inside {Inside}, open {Open:F0} m, stubs {Stubs}, short {Short}" + (GapLengths.Count > 0 ? $" (gaps: {GapLengths.Count(g => g < 1.5f)} under 1.5 m, {GapLengths.Count(g => g >= 1.5f && g < 3f)} 1.5-3 m, {GapLengths.Count(g => g >= 3f)} 3-8 m)" : "");
        }

        public static int Run(List<string> args)
        {
            string input = args[0];
            var knobs = new Dictionary<string, float>();
            bool every = false;
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i] == "--knob") { var kv = args[++i].Split('='); knobs[kv[0]] = float.Parse(kv[1], CultureInfo.InvariantCulture); }
                else if (args[i] == "--every") every = true;
            }
            var files = Directory.Exists(input) ? Directory.GetFiles(input, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList() : new List<string> { input };
            var all = new Tally();
            foreach (var f in files)
            {
                var (houses, settings) = StitchDump.Read(File.ReadAllText(f));
                foreach (var kv in knobs) { settings.Extra ??= new Dictionary<string, float>(); settings.Extra[kv.Key] = kv.Value; }
                var plan = YardStitch.Plan(houses, settings);
                var t = Check(houses, plan, settings);
                if (every) Console.WriteLine($"{Path.GetFileName(f)}: {t}");
                all.Add(t);
            }
            Console.WriteLine(all);
            return 0;
        }

        private static float W(string name) => name != null && name.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0 ? 1.1f : 0.2f;

        public static Tally Check(IReadOnlyList<StitchHouse> houses, StitchPlan plan, StitchSettings s)
        {
            var t = new Tally { Passes = 1, Pieces = plan.FencePieces.Count };
            var bits = new List<Bit>();
            foreach (var h in houses)
            {
                if (h.Polished && !h.Edit && h.Standing != null)
                {
                    foreach (var (a, b) in h.Standing) bits.Add(new Bit { House = h.Id, A = a, B = b, W = 0.2f });
                    continue;
                }
                foreach (var l in h.Lines)
                {
                    if (plan.HideLines.Contains(l.Id)) continue;
                    var (a, b) = plan.MoveLines.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
                    if (math.distance(a, b) < 0.3f) continue;
                    bits.Add(new Bit { House = h.Id, A = a, B = b, W = W(l.Name) });
                }
            }
            foreach (var (house, at, yaw, name) in plan.FencePieces)
            {
                var d = new float2(math.sin(yaw), math.cos(yaw)) * ((name != null && name.Contains("Short") ? 2f : 4f) * 0.5f);
                bits.Add(new Bit { House = house, Piece = true, A = at - d, B = at + d, W = W(name) });
            }
            var gardens = houses.Where(h => h.Edit && h.Polished && plan.Plots.ContainsKey(h.Id)).Select(h => (H: h, P: plan.Plots[h.Id])).ToList();
            var gLo = gardens.Select(g => g.P.Aggregate(new float2(float.MaxValue), math.min) - 3f).ToList();
            var gHi = gardens.Select(g => g.P.Aggregate(new float2(float.MinValue), math.max) + 3f).ToList();
            bool Near(float2 p)
            {
                for (int i = 0; i < gardens.Count; i++)
                    if (p.x >= gLo[i].x && p.y >= gLo[i].y && p.x <= gHi[i].x && p.y <= gHi[i].y && (Poly.Contains(gardens[i].P, p) || Dist(gardens[i].P, p) < 3f)) return true;
                return false;
            }
            var walls = new List<float2[]>();
            foreach (var h in houses)
            {
                if (h.Footprint != null && h.Footprint.Length >= 3) walls.Add(h.Footprint);
                walls.AddRange(h.Props.Where(p => p != null && p.Length >= 3));
                walls.AddRange(h.Hard.Where(p => p != null && p.Length >= 3));
            }
            var barriers = (s?.Barriers ?? new List<float2[]>()).Where(b => b != null && b.Length >= 3).ToList();
            var barLo = barriers.Select(b => b.Aggregate(new float2(float.MaxValue), math.min)).ToList();
            var barHi = barriers.Select(b => b.Aggregate(new float2(float.MinValue), math.max)).ToList();
            bool NearBarrier(float2 p, float d)
            {
                for (int k = 0; k < barriers.Count; k++)
                    if (p.x >= barLo[k].x - d && p.y >= barLo[k].y - d && p.x <= barHi[k].x + d && p.y <= barHi[k].y + d && (Poly.Contains(barriers[k], p) || Dist(barriers[k], p) < d)) return true;
                return false;
            }
            bool Against(float2 p, float d) => walls.Any(w => Poly.Contains(w, p) || Dist(w, p) < d) || NearBarrier(p, d);
            var mine = bits.Where(b => Near((b.A + b.B) * 0.5f)).ToList();
            t.Fenced = mine.Sum(b => math.distance(b.A, b.B));

            // gaps: loose ends close together
            var loose = new List<(Bit F, float2 P)>();
            foreach (var f in mine)
                foreach (var p in new[] { f.A, f.B })
                {
                    float tol = 0.35f + f.W * 0.5f;
                    if (bits.Any(g => g != f && Seg(g.A, g.B, p) <= tol + g.W * 0.5f)) continue;
                    if (!Against(p, 0.6f)) loose.Add((f, p));
                }
            var used = new HashSet<int>();
            for (int i = 0; i < loose.Count; i++)
            {
                if (used.Contains(i)) continue;
                int best = -1;
                float bestD = 8f;
                for (int j = 0; j < loose.Count; j++)
                {
                    if (j == i || used.Contains(j) || loose[j].F == loose[i].F) continue;
                    float d = math.distance(loose[i].P, loose[j].P);
                    if (d < bestD) { bestD = d; best = j; }
                }
                if (best < 0 || bestD < 0.5f) continue;
                var (f, p) = loose[i];
                var (g, q) = loose[best];
                var df = math.normalizesafe(f.B - f.A);
                var dg = math.normalizesafe(g.B - g.A);
                var gd = math.normalizesafe(q - p);
                if (NearBarrier(p, 2.5f) && NearBarrier(q, 2.5f) && math.abs(math.dot(df, gd)) < 0.5f && math.abs(math.dot(dg, gd)) < 0.5f) continue;
                if (Against((p + q) * 0.5f, 0.3f)) continue;
                used.Add(i);
                used.Add(best);
                t.Gaps++;
                t.GapLengths.Add(bestD);
                t.Marks.Add(((p + q) * 0.5f, "gap"));
            }
            // crossings and doubles
            for (int i = 0; i < mine.Count; i++)
                for (int j = i + 1; j < mine.Count; j++)
                {
                    var f = mine[i];
                    var g = mine[j];
                    var d1 = math.normalizesafe(f.B - f.A);
                    var d2 = math.normalizesafe(g.B - g.A);
                    float lf = math.distance(f.A, f.B), lg = math.distance(g.A, g.B);
                    if (math.abs(math.dot(d1, d2)) > 0.97f)
                    {
                        // side by side: across within 0.8 m (past their widths' touching), along each other 1 m or more
                        var nrm = new float2(-d1.y, d1.x);
                        float off = math.abs(math.dot(g.A - f.A, nrm));
                        if (off > 0.8f + (f.W + g.W) * 0.25f) continue;
                        float a0 = math.dot(g.A - f.A, d1), a1 = math.dot(g.B - f.A, d1);
                        float ov = math.min(lf, math.max(a0, a1)) - math.max(0f, math.min(a0, a1));
                        // (pieces of one run end to end overlap a little: that is a run, not a double)
                        if (ov >= 1f && !(f.Piece && g.Piece && f.House == g.House && off < 0.05f && ov < 1.5f)) { t.Doubles++; t.Marks.Add(((g.A + g.B) * 0.5f, "double")); }
                        continue;
                    }
                    if (!Hit(f.A, f.B, g.A, g.B, out float tf, out float tg)) continue;
                    if (math.min(tf, 1f - tf) * lf > 1f + g.W * 0.5f && math.min(tg, 1f - tg) * lg > 1f + f.W * 0.5f) { t.Crossings++; t.Marks.Add((math.lerp(f.A, f.B, tf), "crossing")); }
                }
            // inside a garden, away from its edges
            foreach (var b in mine)
            {
                var mid = (b.A + b.B) * 0.5f;
                foreach (var (h, p) in gardens)
                    if (Poly.Contains(p, mid) && Dist(p, mid) > 1.5f && Dist(p, b.A) > 1.5f && Dist(p, b.B) > 1.5f) { t.Inside++; t.Marks.Add((mid, "inside")); break; }
            }
            // stubs: fences standing on their own, shorter than 5 m
            var group = Enumerable.Range(0, mine.Count).ToArray();
            int Find(int x) { while (group[x] != x) x = group[x] = group[group[x]]; return x; }
            for (int i = 0; i < mine.Count; i++)
                for (int j = i + 1; j < mine.Count; j++)
                {
                    var f = mine[i];
                    var g = mine[j];
                    float tol = 0.35f + (f.W + g.W) * 0.5f;
                    if (Seg(g.A, g.B, f.A) <= tol || Seg(g.A, g.B, f.B) <= tol || Seg(f.A, f.B, g.A) <= tol || Seg(f.A, f.B, g.B) <= tol) group[Find(i)] = Find(j);
                }
            foreach (var comp in Enumerable.Range(0, mine.Count).GroupBy(Find))
            {
                float len = comp.Sum(k => math.distance(mine[k].A, mine[k].B));
                if (len >= 5f) continue;
                // (one against a house, a road or a wall at both ends is a short fence on purpose)
                var ends = comp.SelectMany(k => new[] { mine[k].A, mine[k].B }).ToList();
                if (ends.Count(p => Against(p, 0.6f)) >= 2) continue;
                t.Stubs++;
                t.Marks.Add((ends[0], "stub"));
            }
            // open edges: a garden's edge with no fence along it (0.5 m samples), not the street front, a road, a house or a lot
            foreach (var (h, p) in gardens)
                for (int k = 0; k < p.Length; k++)
                {
                    var a = p[k];
                    var b = p[(k + 1) % p.Length];
                    float len = math.distance(a, b);
                    if (len < 0.01f) continue;
                    var dir = (b - a) / len;
                    int steps = (int)math.ceil(len / 0.5f);
                    for (int q = 0; q < steps; q++)
                    {
                        var at = a + dir * ((q + 0.5f) * len / steps);
                        var l = h.ToLocal(at);
                        if (l.y >= h.HalfLot.y - 1.2f) continue;   // the street front
                        if (Against(at, 1.5f)) continue;
                        if (houses.Any(o => o != h && math.distance(o.Position, at) < math.length(o.HalfLot) + 2f && Inside(o, at, 1f))) continue;
                        bool fenced = bits.Any(f => Seg(f.A, f.B, at) < 1.2f && math.abs(math.dot(math.normalizesafe(f.B - f.A), dir)) > 0.8f);
                        if (!fenced) { t.Open += len / steps; t.Marks.Add((at, "open")); }
                    }
                }
            // short edges where the outline turns
            foreach (var (h, p) in gardens)
                for (int k = 0; k < p.Length; k++)
                {
                    var a = p[k];
                    var b = p[(k + 1) % p.Length];
                    var c = p[(k + 2) % p.Length];
                    var z = p[(k + p.Length - 1) % p.Length];
                    float len = math.distance(a, b);
                    if (len >= 2f || len < 0.05f) continue;
                    // (the street front and edges along a road or house: never fenced)
                    if (h.ToLocal((a + b) * 0.5f).y >= h.HalfLot.y - 1.2f || Against((a + b) * 0.5f, 1.5f)) continue;
                    var din = math.normalizesafe(a - z);
                    var dmid = math.normalizesafe(b - a);
                    var dout = math.normalizesafe(c - b);
                    if (math.dot(din, dmid) < math.cos(math.radians(25f)) || math.dot(dmid, dout) < math.cos(math.radians(25f))) { t.Short++; t.Marks.Add(((a + b) * 0.5f, "short")); }
                }
            return t;
        }

        private static bool Inside(StitchHouse h, float2 p, float grow)
        {
            var l = h.ToLocal(p);
            return math.abs(l.x) <= h.HalfLot.x + grow && math.abs(l.y) <= h.HalfLot.y + grow;
        }

        private static float Seg(float2 a, float2 b, float2 p)
        {
            var ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
            return math.distance(p, a + ab * t);
        }

        private static float Dist(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++) best = math.min(best, Seg(ring[i], ring[(i + 1) % ring.Length], p));
            return best;
        }

        private static bool Hit(float2 p0, float2 p1, float2 q0, float2 q1, out float tp, out float tq)
        {
            tp = tq = 0f;
            var r = p1 - p0;
            var s = q1 - q0;
            float den = r.x * s.y - r.y * s.x;
            if (math.abs(den) < 1e-6f) return false;
            var w = q0 - p0;
            tp = (w.x * s.y - w.y * s.x) / den;
            tq = (w.x * r.y - w.y * r.x) / den;
            return tp >= 0f && tp <= 1f && tq >= 0f && tq <= 1f;
        }
    }
}
