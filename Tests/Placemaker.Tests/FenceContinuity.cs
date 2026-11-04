// Outgrow's fences as a plan leaves them, checked for what the user sees (21 Oct: "fence continuity errors ... crossover
// errors"): two loose ends of fences close to each other (a gap), two fences crossing. The game runner's fenceMap measures
// the same in a city; this measures a plan, offline, so a fix is tried in seconds:
//   * FenceCheck.Of(houses, plan): the fences a plan leaves standing and what is wrong with them;
//   * FenceContinuityReplay: every pass the game wrote (lab op polishPassDump), planned again, summed and drawn:
//       $env:PM_FENCE_PASSES = "<run>\passes"; dotnet test Tests/Placemaker.Tests --filter FenceContinuityReplay
//     report.txt and fence-<pass>.svg go to PM_FENCE_OUT (default: next to the passes).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    internal static class FenceCheck
    {
        public sealed class Bit
        {
            public long House;
            public string Name;
            public bool Piece;
            public float2 A, B;
            public float W;
        }

        public sealed class Result
        {
            public readonly List<Bit> Bits = new List<Bit>();
            public readonly List<(float2 At, float Len, string Kind, Bit P, Bit Q)> Gaps = new List<(float2, float, string, Bit, Bit)>();
            public readonly List<(float2 At, Bit P, Bit Q)> Crossings = new List<(float2, Bit, Bit)>();
            public override string ToString() => $"{Gaps.Count} gaps ({string.Join(", ", Gaps.GroupBy(g => g.Kind).Select(g => $"{g.Count()} {g.Key}"))}), {Crossings.Count} crossings";
        }

        public const float PieceLength = 4f;

        private static float WidthOf(string name) => name != null && name.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0 ? 1.1f : 0.2f;

        /// <summary>The fences standing once the plan is written: every house's lines where the plan leaves them (a neighbour
        /// another pass planned: as it stands), and the closing pieces.</summary>
        public static List<Bit> Drawn(IReadOnlyList<StitchHouse> houses, StitchPlan plan)
        {
            var bits = new List<Bit>();
            foreach (var h in houses)
            {
                if (h.Polished && !h.Edit && h.Standing != null)
                {
                    foreach (var (a, b) in h.Standing) bits.Add(new Bit { House = h.Id, Name = "standing", A = a, B = b, W = 0.2f });
                    continue;
                }
                foreach (var l in h.Lines)
                {
                    if (plan.HideLines.Contains(l.Id)) continue;
                    var (a, b) = plan.MoveLines.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
                    if (math.distance(a, b) < 0.3f) continue;
                    bits.Add(new Bit { House = h.Id, Name = l.Name, A = a, B = b, W = WidthOf(l.Name) });
                }
            }
            foreach (var (house, at, yaw, name) in plan.FencePieces)
            {
                // a 2 m piece finishes a run (FenceResidentialPieceLowShort01)
                var d = new float2(math.sin(yaw), math.cos(yaw)) * ((name != null && name.Contains("Short") ? 2f : PieceLength) * 0.5f);
                bits.Add(new Bit { House = house, Name = name, Piece = true, A = at - d, B = at + d, W = WidthOf(name) });
            }
            return bits;
        }

        /// <summary>Gaps and crossings among the fences, near the gardens this plan edited.</summary>
        public static Result Of(IReadOnlyList<StitchHouse> houses, StitchPlan plan, StitchSettings s)
        {
            var r = new Result();
            r.Bits.AddRange(Drawn(houses, plan));
            // only around what this plan edited
            var edited = houses.Where(h => h.Edit && h.Polished && plan.Plots.ContainsKey(h.Id)).Select(h => plan.Plots[h.Id]).ToList();
            var eLo = edited.Select(p => p.Aggregate(new float2(float.MaxValue), math.min) - 3f).ToList();
            var eHi = edited.Select(p => p.Aggregate(new float2(float.MinValue), math.max) + 3f).ToList();
            bool Near(float2 p)
            {
                for (int i = 0; i < edited.Count; i++)
                    if (p.x >= eLo[i].x && p.y >= eLo[i].y && p.x <= eHi[i].x && p.y <= eHi[i].y && (Poly.Contains(edited[i], p) || Dist(edited[i], p) < 3f)) return true;
                return false;
            }
            // what a fence may end against: a house, shed or paving, a road or another building
            var walls = new List<float2[]>();
            foreach (var h in houses)
            {
                if (h.Footprint != null && h.Footprint.Length >= 3) walls.Add(h.Footprint);
                walls.AddRange(h.Props.Where(p => p != null && p.Length >= 3));
                walls.AddRange(h.Hard.Where(p => p != null && p.Length >= 3));
            }
            var barriers = (s?.Barriers ?? new List<float2[]>()).Where(b => b != null && b.Length >= 3).ToList();
            bool Against(float2 p, float d) => walls.Any(w => Poly.Contains(w, p) || Dist(w, p) < d) || barriers.Any(b => Poly.Contains(b, p) || Dist(b, p) < d);
            bool NearBarrier(float2 p, float d) => barriers.Any(b => Poly.Contains(b, p) || Dist(b, p) < d);

            var bits = r.Bits;
            var loose = new List<(Bit F, float2 P)>();
            foreach (var f in bits)
                foreach (var p in new[] { f.A, f.B })
                {
                    if (!Near(p)) continue;
                    float tol = 0.35f + f.W * 0.5f;
                    bool met = bits.Any(g => g != f && Seg(g.A, g.B, p) <= tol + g.W * 0.5f);
                    if (!met && !Against(p, 0.6f)) loose.Add((f, p));
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
                // two fences coming up to the street side by side: the street front between them is open on purpose
                if (NearBarrier(p, 2.5f) && NearBarrier(q, 2.5f) && math.abs(math.dot(df, gd)) < 0.5f && math.abs(math.dot(dg, gd)) < 0.5f) continue;
                // across a house, a shed or paving: a way through
                var mid = (p + q) * 0.5f;
                if (Against(mid, 0.3f)) continue;
                used.Add(i);
                used.Add(best);
                float par = math.abs(math.dot(df, dg));
                float off = math.abs(math.dot(q - p, new float2(-df.y, df.x)));
                r.Gaps.Add((mid, bestD, par < 0.9f ? "corner" : off < 0.5f ? "inline" : "step", f, g));
            }
            for (int i = 0; i < bits.Count; i++)
                for (int j = i + 1; j < bits.Count; j++)
                {
                    var f = bits[i];
                    var g = bits[j];
                    if (!Hit(f.A, f.B, g.A, g.B, out float tf, out float tg)) continue;
                    var at = math.lerp(f.A, f.B, tf);
                    if (!Near(at)) continue;
                    float lf = math.distance(f.A, f.B), lg = math.distance(g.A, g.B);
                    var d1 = math.normalizesafe(f.B - f.A);
                    var d2 = math.normalizesafe(g.B - g.A);
                    if (math.abs(math.dot(d1, d2)) > 0.97f) continue;
                    if (math.min(tf, 1f - tf) * lf > 1f + g.W * 0.5f && math.min(tg, 1f - tg) * lg > 1f + f.W * 0.5f) r.Crossings.Add((at, f, g));
                }
            return r;
        }

        public static float Seg(float2 a, float2 b, float2 p)
        {
            var ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
            return math.distance(p, a + ab * t);
        }

        public static float Dist(float2[] ring, float2 p)
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

        /// <summary>The plan drawn with its problems: plots green, houses brown, paving grey, roads dark grey, lines and pieces
        /// as they stand (pieces lighter), gaps magenta rings, crossings red crosses.</summary>
        public static string Svg(IReadOnlyList<StitchHouse> houses, StitchPlan plan, StitchSettings s, Result r)
        {
            var pts = houses.SelectMany(h => new[] { h.FrontLeft, h.FrontRight, h.BackLeft, h.BackRight }).Concat(plan.Plots.Values.SelectMany(p => p)).ToList();
            var lo = pts.Aggregate(new float2(float.MaxValue), math.min) - 10f;
            var hi = pts.Aggregate(new float2(float.MinValue), math.max) + 10f;
            // PM_FENCE_AT="x,z" PM_FENCE_SIZE=m: a close look
            var at = Environment.GetEnvironmentVariable("PM_FENCE_AT");
            if (!string.IsNullOrEmpty(at))
            {
                var xz = at.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                float half = float.Parse(Environment.GetEnvironmentVariable("PM_FENCE_SIZE") ?? "80", CultureInfo.InvariantCulture) * 0.5f;
                lo = new float2(xz[0], xz[1]) - half;
                hi = new float2(xz[0], xz[1]) + half;
            }
            float scale = math.min(10f, 1600f / math.cmax(hi - lo));
            string X(float2 p) => ((p.x - lo.x) * scale).ToString("F1", CultureInfo.InvariantCulture);
            string Y(float2 p) => ((hi.y - p.y) * scale).ToString("F1", CultureInfo.InvariantCulture);
            string Ring(IEnumerable<float2> ring) => string.Join(" ", ring.Select(p => X(p) + "," + Y(p)));
            var sb = new StringBuilder();
            int w = (int)((hi.x - lo.x) * scale), h2 = (int)((hi.y - lo.y) * scale);
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h2}\"><rect width=\"{w}\" height=\"{h2}\" fill=\"#e4ecd8\"/>\n");
            foreach (var b in s?.Barriers ?? new List<float2[]>()) if (b != null && b.Length >= 3) sb.Append($"<polygon points=\"{Ring(b)}\" fill=\"#9a9a9a\"/>\n");
            var tints = new[] { "#a5d68a", "#8ac6d6", "#d6c48a", "#c88ad6", "#d68a8a", "#8ad6b0" };
            int tint = 0;
            foreach (var kv in plan.Plots)
            {
                var c = tints[tint++ % tints.Length];
                var m = Poly.Centroid(kv.Value);
                sb.Append($"<polygon points=\"{Ring(kv.Value)}\" fill=\"{c}\" fill-opacity=\"0.45\" stroke=\"{c}\" stroke-width=\"2\"/><text x=\"{X(m)}\" y=\"{Y(m)}\" font-size=\"12\">plot {kv.Key % 1000}</text>\n");
            }
            foreach (var h in houses)
            {
                sb.Append($"<polygon points=\"{Ring(new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft })}\" fill=\"none\" stroke=\"#7a6a50\" stroke-dasharray=\"4 3\"/>\n");
                foreach (var r2 in h.Hard) if (r2 != null && r2.Length >= 3) sb.Append($"<polygon points=\"{Ring(r2)}\" fill=\"#b9b9b9\"/>\n");
                foreach (var r2 in h.Props) if (r2 != null && r2.Length >= 3) sb.Append($"<polygon points=\"{Ring(r2)}\" fill=\"#4a6fa5\"/>\n");
                if (h.Footprint != null) sb.Append($"<polygon points=\"{Ring(h.Footprint)}\" fill=\"#8a5a3a\"/>\n");
                sb.Append($"<text x=\"{X(h.Position)}\" y=\"{Y(h.Position)}\" font-size=\"11\" fill=\"#fff\" text-anchor=\"middle\">{h.Id % 1000}{(h.Edit ? "" : "*")}</text>\n");
            }
            foreach (var b in r.Bits)
            {
                bool hedge = b.Name != null && b.Name.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0;
                string c = b.Name == "standing" ? "#555" : hedge ? (b.Piece ? "#3fae4a" : "#1f6b2a") : (b.Piece ? "#d08a3a" : "#7a4a1a");
                sb.Append($"<line x1=\"{X(b.A)}\" y1=\"{Y(b.A)}\" x2=\"{X(b.B)}\" y2=\"{Y(b.B)}\" stroke=\"{c}\" stroke-width=\"{math.max(1.5f, b.W * scale):F1}\" stroke-opacity=\"0.85\"/>\n");
            }
            foreach (var g in r.Gaps) sb.Append($"<circle cx=\"{X(g.At)}\" cy=\"{Y(g.At)}\" r=\"{math.max(4f, (g.Len / 2 + 0.5f) * scale):F1}\" fill=\"none\" stroke=\"#e000c0\" stroke-width=\"2\"/><text x=\"{X(g.At)}\" y=\"{Y(g.At)}\" font-size=\"10\" fill=\"#a00090\">{g.Kind} {g.Len:F1}</text>\n");
            foreach (var c in r.Crossings) sb.Append($"<circle cx=\"{X(c.At)}\" cy=\"{Y(c.At)}\" r=\"6\" fill=\"#e00000\"/>\n");
            sb.Append("</svg>");
            return sb.ToString();
        }
    }

    public class FenceContinuityReplay
    {
        [Fact]
        public void Replay()
        {
            var input = Environment.GetEnvironmentVariable("PM_FENCE_PASSES");
            if (string.IsNullOrEmpty(input)) return;
            var files = Directory.Exists(input) ? Directory.GetFiles(input, "pass-*.json").OrderBy(f => f).ToArray() : new[] { input };
            var outDir = Environment.GetEnvironmentVariable("PM_FENCE_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Directory.Exists(input) ? input : Path.GetDirectoryName(input);
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            int gaps = 0, crossings = 0, overLots = 0, overPlots = 0;
            var kinds = new Dictionary<string, int>();
            foreach (var file in files)
            {
                var (houses, settings) = StitchDump.Read(File.ReadAllText(file));
                foreach (var kv in (Environment.GetEnvironmentVariable("PM_POLISH_KNOBS") ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kp = kv.Split('=');
                    if (kp.Length == 2) settings.Extra[kp[0].Trim()] = float.Parse(kp[1], CultureInfo.InvariantCulture);
                }
                var plan = YardStitch.Plan(houses, settings);
                var r = FenceCheck.Of(houses, plan, settings);
                gaps += r.Gaps.Count;
                crossings += r.Crossings.Count;
                foreach (var g in r.Gaps) kinds[g.Kind] = (kinds.TryGetValue(g.Kind, out var c) ? c : 0) + 1;
                report.AppendLine($"{Path.GetFileName(file)}: {houses.Count} houses ({houses.Count(h => h.Edit)} edited), {plan.FencePieces.Count} pieces: {r}");
                // PM_FENCE_PROBE="x,z,r": the plots' corners and the pieces near a spot, written out
                var probe = Environment.GetEnvironmentVariable("PM_FENCE_PROBE");
                if (!string.IsNullOrEmpty(probe))
                {
                    var v = probe.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    var at = new float2(v[0], v[1]);
                    foreach (var kv in plan.Plots)
                    {
                        var close = kv.Value.Select((p, i) => (p, i)).Where(x => math.distance(x.p, at) < v[2]).ToList();
                        if (close.Count > 0) report.AppendLine($"  probe plot {kv.Key % 1000}: " + string.Join(" ", close.Select(x => $"#{x.i}({x.p.x:F2},{x.p.y:F2})")));
                    }
                    foreach (var b in r.Bits.Where(b => math.distance((b.A + b.B) * 0.5f, at) < v[2] + 2f))
                        report.AppendLine($"  probe {(b.Piece ? "piece" : "line")} {b.Name} of {b.House % 1000}: ({b.A.x:F2},{b.A.y:F2})-({b.B.x:F2},{b.B.y:F2})");
                }
                // a plan's own gardens over another house's lot, or over each other
                foreach (var a in houses.Where(h => h.Edit && h.Polished && plan.Plots.ContainsKey(h.Id)))
                {
                    var pa = plan.Plots[a.Id];
                    foreach (var b in houses)
                    {
                        if (b == a) continue;
                        float ol = Overlap(pa, new[] { b.BackLeft, b.BackRight, b.FrontRight, b.FrontLeft });
                        if (ol > 2f) { overLots++; report.AppendLine($"  plot of {a.Id % 1000} over the lot of {b.Id % 1000}{(b.Edit ? "" : " (fixed)")}: {ol:F1} m²"); }
                        if (b.Id > a.Id && b.Edit && plan.Plots.TryGetValue(b.Id, out var pb))
                        {
                            float op = Overlap(pa, pb);
                            if (op > 2f) { overPlots++; report.AppendLine($"  plots of {a.Id % 1000} and {b.Id % 1000} over each other: {op:F1} m²"); }
                        }
                    }
                }
                // a neighbour planned again for a garden over a lot here: how much of that is the two lots over each other
                foreach (var id in plan.GardenClashes)
                {
                    var j = houses.First(h => h.Id == id);
                    foreach (var i in houses.Where(h => h.Edit && h.Polished))
                    {
                        var lotI = new[] { i.BackLeft, i.BackRight, i.FrontRight, i.FrontLeft };
                        var lotJ = new[] { j.BackLeft, j.BackRight, j.FrontRight, j.FrontLeft };
                        float gl = Overlap(lotI, j.Garden), ll = Overlap(lotI, lotJ);
                        if (gl > 2f) report.AppendLine($"  clash: {j.Id % 1000}'s garden over {i.Id % 1000}'s lot {gl:F1} m², of which the two lots over each other {ll:F1} m²");
                    }
                }
                foreach (var g in r.Gaps) report.AppendLine($"  gap {g.Kind} {g.Len:F1} m at ({g.At.x:F1}, {g.At.y:F1}): {g.P.Name}{(g.P.Piece ? " piece" : "")} of {g.P.House % 1000} / {g.Q.Name}{(g.Q.Piece ? " piece" : "")} of {g.Q.House % 1000}");
                foreach (var c in r.Crossings) report.AppendLine($"  crossing at ({c.At.x:F1}, {c.At.y:F1}): {c.P.Name}{(c.P.Piece ? " piece" : "")} of {c.P.House % 1000} / {c.Q.Name}{(c.Q.Piece ? " piece" : "")} of {c.Q.House % 1000}");
                if (r.Gaps.Count + r.Crossings.Count > 0) File.WriteAllText(Path.Combine(outDir, "fence-" + Path.GetFileNameWithoutExtension(file) + ".svg"), FenceCheck.Svg(houses, plan, settings, r));
            }
            report.Insert(0, $"{files.Length} passes: {gaps} gaps ({string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}), {crossings} crossings, {overLots} gardens over another lot, {overPlots} over each other\n");
            File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
        }

        private static float Overlap(float2[] a, float2[] b)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3) return 0f;
            return Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(a.ToArray()) }, new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(b.ToArray()) }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area);
        }
    }
}
