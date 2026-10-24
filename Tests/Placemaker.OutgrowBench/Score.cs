// Outgrow benchmark: the shapes gardens come out in, scored (--score A B dir...): every recorded pass planned with two sets of
// settings, and for the gardens each edits: bends (corners), concave corners (puzzle pieces), length of edges at a slant to
// the lot, empty pockets left between gardens (land inside the patch that no garden, lot, road or building holds), and the
// time. The worst passes by each are listed, to be drawn (--svg --only N).
//   --score "sideReach=0,capNarrow=1" "capNarrow=0" <passes dir> [<passes dir> ...]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    internal static class Score
    {
        public sealed class Shapes
        {
            public int Gardens, Bends, Concave;
            public float SlantM, PocketM2, Ms;
        }

        public static Dictionary<string, float> ParseKnobs(string text)
        {
            var d = new Dictionary<string, float>();
            foreach (var kv in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = kv.Split('=');
                if (p.Length == 2) d[p[0].Trim()] = float.Parse(p[1], CultureInfo.InvariantCulture);
            }
            return d;
        }

        /// <summary>Settings for the planner and, for a look's own settings, on every house that has a look (YardStitch.K).</summary>
        public static void Apply(List<StitchHouse> houses, StitchSettings settings, Dictionary<string, float> knobs)
        {
            // (editAll=1: every polished house planned at once, as a whole area planned again; not a planner setting)
            if (knobs.TryGetValue("editAll", out var all) && all > 0.5f)
                foreach (var h in houses)
                    if (h.Polished) { h.Edit = true; h.Garden = null; h.Standing = null; }
            foreach (var kv in knobs)
            {
                if (kv.Key == "editAll") continue;
                settings.Extra ??= new Dictionary<string, float>();
                settings.Extra[kv.Key] = kv.Value;
                if (!PolishLooks.Defaults.ContainsKey(kv.Key)) continue;
                foreach (var h in houses)
                    if (h.Options != null)
                    {
                        h.Options = new Dictionary<string, float>(h.Options);
                        h.Options[kv.Key] = kv.Value;
                    }
            }
        }

        public static Shapes Measure(List<StitchHouse> houses, StitchSettings settings, StitchPlan plan, double ms)
        {
            var r = new Shapes { Ms = (float)ms };
            var all = new List<IReadOnlyList<float2>>();
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var h in houses)
            {
                var lot = new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };
                all.Add(Poly.EnsureCcw(lot));
                var g = h.Edit && plan.Plots.TryGetValue(h.Id, out var p) ? p : h.Garden;
                if (g != null && g.Length >= 3) all.Add(Poly.EnsureCcw(g.ToArray()));
                if (!h.Edit || !plan.Plots.TryGetValue(h.Id, out var plot) || plot == null || plot.Length < 3) continue;
                r.Gardens++;
                foreach (var q in plot) { lo = math.min(lo, q); hi = math.max(hi, q); }
                // corners that turn more than 10°, concave ones, and edges at a slant (more than 5° off the lot's axes)
                var ring = Poly.EnsureCcw(plot.ToArray());
                int n = ring.Length;
                var v = math.normalizesafe(h.Forward);
                var u = new float2(v.y, -v.x);
                for (int k = 0; k < n; k++)
                {
                    var a = ring[(k + n - 1) % n];
                    var b = ring[k];
                    var c = ring[(k + 1) % n];
                    var d1 = math.normalizesafe(b - a);
                    var d2 = math.normalizesafe(c - b);
                    float cross = d1.x * d2.y - d1.y * d2.x;
                    float turn = math.degrees(math.acos(math.clamp(math.dot(d1, d2), -1f, 1f)));
                    if (turn > 10f) { r.Bends++; if (cross < 0f) r.Concave++; }
                    float len = math.distance(b, c);
                    if (len < 0.5f) continue;
                    float au = math.abs(math.dot(d2, u)), av = math.abs(math.dot(d2, v));
                    if (math.max(au, av) < 0.9962f) r.SlantM += len;
                }
            }
            if (r.Gardens == 0) return r;
            // empty pockets: land inside the edited gardens' bounds held by nothing (no garden, lot, road, building, water)
            if (settings.Barriers != null)
                for (int k = 0; k < settings.Barriers.Count; k++)
                {
                    var b = settings.Barriers[k];
                    if (b == null || b.Length < 3) continue;
                    Poly.Bounds(b, out var blo, out var bhi);
                    if (bhi.x < lo.x || blo.x > hi.x || bhi.y < lo.y || blo.y > hi.y) continue;
                    all.Add(Poly.EnsureCcw(b.ToArray()));
                }
            var box = new[] { lo, new float2(hi.x, lo.y), hi, new float2(lo.x, hi.y) };
            var held = Poly.Boolean(new[] { (IReadOnlyList<float2>)box }, all, BoolOp.Difference, FillRule.NonZero);
            // only pockets: what is left inside, not the open land around the edge of the box
            foreach (var reg in held)
            {
                if (reg.Area < 4f) continue;
                bool edge = reg.Outer.Any(p => p.x <= lo.x + 0.5f || p.x >= hi.x - 0.5f || p.y <= lo.y + 0.5f || p.y >= hi.y - 0.5f);
                if (!edge) r.PocketM2 += reg.Area;
            }
            return r;
        }

        public static int Run(string a, string b, List<string> dirs)
        {
            var ka = ParseKnobs(a);
            var kb = ParseKnobs(b);
            var rows = new List<(string Name, Shapes A, Shapes B)>();
            foreach (var dir in dirs)
                foreach (var f in Directory.GetFiles(dir, "pass-*.json").OrderBy(x => x, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd('/', '\\'))) + "/" + Path.GetFileNameWithoutExtension(f);
                    var text = File.ReadAllText(f);
                    Shapes Plan(Dictionary<string, float> knobs)
                    {
                        var (houses, settings) = StitchDump.Read(text);
                        Apply(houses, settings, knobs);
                        StitchPlan plan = null;
                        double best = double.MaxValue;
                        for (int k = 0; k < 2; k++)
                        {
                            var (hk, sk) = k == 0 ? (houses, settings) : StitchDump.Read(text);
                            if (k > 0) Apply(hk, sk, knobs);
                            var clock = Stopwatch.StartNew();
                            plan = YardStitch.Plan(hk, sk);
                            best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
                            if (k == 0) { houses = hk; settings = sk; }
                        }
                        return Measure(houses, settings, plan, best);
                    }
                    rows.Add((name, Plan(ka), Plan(kb)));
                }
            void Sum(string label, Func<Shapes, float> f) =>
                Console.WriteLine($"{label}: A {rows.Sum(x => f(x.A)):F0}  B {rows.Sum(x => f(x.B)):F0}");
            Console.WriteLine($"{rows.Count} passes; A = \"{a}\", B = \"{b}\"");
            Sum("gardens", x => x.Gardens);
            Sum("bends", x => x.Bends);
            Sum("concave corners", x => x.Concave);
            Sum("slanted edges (m)", x => x.SlantM);
            Sum("empty pockets (m2)", x => x.PocketM2);
            Sum("time (ms)", x => x.Ms);
            void Worst(string label, Func<Shapes, float> f)
            {
                Console.WriteLine($"worst B by {label}: " + string.Join(", ", rows.OrderByDescending(x => f(x.B)).Take(6).Select(x => $"{x.Name} {f(x.B):F0} (A {f(x.A):F0})")));
                Console.WriteLine($"most worse B-A by {label}: " + string.Join(", ", rows.OrderByDescending(x => f(x.B) - f(x.A)).Take(6).Select(x => $"{x.Name} {f(x.B) - f(x.A):+0;-0}")));
            }
            Worst("concave corners", x => x.Concave);
            Worst("slanted edges", x => x.SlantM);
            Worst("empty pockets", x => x.PocketM2);
            Worst("time", x => x.Ms);
            return 0;
        }
    }
}
