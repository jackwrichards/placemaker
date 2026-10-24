// Outgrow benchmark: how each garden came to be (--explain <pass file or passes dir> [--at x,z] [--size m] [--knob NAME=VALUE]):
// one recorded pass replayed with YardStitch.Trace on, every garden near the spot drawn after each step that shapes it, the
// planner's reasons (who is beside, behind or facing whom, where two gardens were split and on what line) and, per house,
// the steps that changed its garden. Writes explain.html (and explain.txt) next to the pass. With a passes dir: the last
// pass that edits a house near the spot.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    internal static class Explain
    {
        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static int Run(List<string> args)
        {
            string input = args[0];
            float2? at = null;
            float size = 140f;
            var knobs = new Dictionary<string, float>();
            for (int i = 1; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case "--at": { var p = args[++i].Split(','); at = new float2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)); break; }
                    case "--size": size = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--knob": { var kv = args[++i].Split('='); knobs[kv[0]] = float.Parse(kv[1], CultureInfo.InvariantCulture); break; }
                }
            }
            string file = input;
            if (Directory.Exists(input))
            {
                file = null;
                foreach (var f in Directory.GetFiles(input, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var (hs, _) = StitchDump.Read(File.ReadAllText(f));
                    if (hs.Any(h => h.Edit && (at == null || math.distance(h.Position, at.Value) < size * 0.5f))) file = f;
                }
                if (file == null) { Console.WriteLine("no pass edits a house there"); return 1; }
            }
            var (houses, settings) = StitchDump.Read(File.ReadAllText(file));
            foreach (var kv in knobs) { settings.Extra ??= new Dictionary<string, float>(); settings.Extra[kv.Key] = kv.Value; }
            // the planner's own order (Plan sorts its houses by id): the traced gardens are indexed so
            var sorted = houses.OrderBy(h => h.Id).ToList();
            var stages = new List<(string Name, float2[][] Plots)>();
            var why = new List<string>();
            YardStitch.Trace = (name, plots) => stages.Add((name, plots));
            YardStitch.TraceWhy = why.Add;
            StitchPlan plan;
            try { plan = YardStitch.Plan(houses, settings); }
            finally { YardStitch.Trace = null; YardStitch.TraceWhy = null; }
            stages.Add(("final", sorted.Select(h => plan.Plots.TryGetValue(h.Id, out var g) ? g : null).ToArray()));

            var centre = at ?? sorted.Where(h => h.Edit).Select(h => h.Position).Aggregate(float2.zero, (a, p) => a + p) / math.max(1, sorted.Count(h => h.Edit));
            var lo = centre - size * 0.5f;
            var hi = centre + size * 0.5f;
            bool InBox(float2 p) => p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y;
            bool In(IReadOnlyList<float2> r) => r != null && r.Any(InBox);
            var near = new HashSet<long>(sorted.Where(h => In(Lot(h))).Select(h => h.Id));
            string Short(long id) => (id % 10000).ToString(CultureInfo.InvariantCulture);

            var txt = new StringBuilder();
            txt.Append($"pass {Path.GetFileName(file)}: {houses.Count} houses ({houses.Count(h => h.Edit)} edited), around {F(centre.x)},{F(centre.y)} ({F(size)} m)\n\n");
            txt.Append("houses here (id: position, facing, lot w x d, edited)\n");
            foreach (var h in sorted.Where(h => near.Contains(h.Id)))
                txt.Append($"  {Short(h.Id)}: {F(h.Position.x)},{F(h.Position.y)} facing {F(math.degrees(math.atan2(h.Forward.x, h.Forward.y)))}°, lot {F(2 * h.HalfLot.x)} x {F(2 * h.HalfLot.y)}, {(h.Edit ? "edited" : "fixed")}\n");
            txt.Append("\nthe planner's reasons (ids shortened to their last 4 digits)\n");
            foreach (var w in why)
            {
                var ids = w.Split(' ').Skip(1).Take(4).Select(x => long.TryParse(x, out var v) ? v : -1).Where(v => v >= 0).ToList();
                if (ids.Count == 0 || !ids.Any(near.Contains)) continue;
                var line = w;
                foreach (var id in ids) line = line.Replace(id.ToString(CultureInfo.InvariantCulture), Short(id));
                txt.Append("  " + line + "\n");
            }
            txt.Append("\neach step that changed a garden here (area m², corners)\n");
            for (int k = 0; k < sorted.Count; k++)
            {
                var h = sorted[k];
                if (!near.Contains(h.Id) || !h.Edit) continue;
                txt.Append($"  {Short(h.Id)}:");
                float2[] was = null;
                foreach (var (name, plots) in stages)
                {
                    var g = k < plots.Length ? plots[k] : null;
                    if (g == null) continue;
                    if (was != null && Same(was, g)) continue;
                    txt.Append($"  {name} {F(Poly.Area(g))}/{g.Length}");
                    was = g;
                }
                txt.Append("\n");
            }
            txt.Append("\nnotes\n");
            foreach (var n in plan.Notes) txt.Append("  " + n + "\n");

            // the pictures: one per step, the gardens that changed in it outlined in black
            var html = new StringBuilder();
            html.Append("<!doctype html><meta charset=utf-8><title>Outgrow explain</title><style>body{font:13px system-ui;margin:12px;background:#fafafa}"
                + ".g{display:grid;grid-template-columns:repeat(auto-fill,minmax(440px,1fr));gap:10px}figure{margin:0;background:#fff;border:1px solid #ddd}"
                + "figcaption{padding:4px 8px;font-weight:600}pre{background:#fff;border:1px solid #ddd;padding:8px;white-space:pre-wrap}</style>\n");
            html.Append($"<h3>{Path.GetFileName(file)} around {F(centre.x)}, {F(centre.y)}</h3><div class=g>\n");
            float2[][] prev = null;
            foreach (var (name, plots) in stages)
            {
                html.Append($"<figure><figcaption>{name}</figcaption>");
                html.Append(Picture(sorted, settings, plots, prev, lo, hi, name == "final" ? plan : null, In));
                html.Append("</figure>\n");
                prev = plots;
            }
            html.Append("</div><pre>" + System.Net.WebUtility.HtmlEncode(txt.ToString()) + "</pre>\n");
            var outDir = Path.GetDirectoryName(Path.GetFullPath(file));
            var stem = Path.GetFileNameWithoutExtension(file);
            File.WriteAllText(Path.Combine(outDir, stem + "-explain.html"), html.ToString());
            File.WriteAllText(Path.Combine(outDir, stem + "-explain.txt"), txt.ToString());
            Console.Write(txt.ToString());
            Console.WriteLine($"\n{Path.Combine(outDir, stem + "-explain.html")}");
            return 0;
        }

        private static float2[] Lot(StitchHouse h) => new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };

        private static bool Same(float2[] a, float2[] b) => a.Length == b.Length && a.Zip(b, (p, q) => math.distancesq(p, q) < 1e-4f).All(x => x);

        private static string Colour(long id)
        {
            ulong z = (ulong)id;
            z ^= z >> 33; z *= 0xff51afd7ed558ccdUL; z ^= z >> 33; z *= 0xc4ceb9fe1a85ec53UL; z ^= z >> 33;
            return $"hsl({z % 360},60%,{50 + (z >> 20) % 20}%)";
        }

        private static string Picture(List<StitchHouse> sorted, StitchSettings settings, float2[][] plots, float2[][] prev, float2 lo, float2 hi, StitchPlan plan, Func<IReadOnlyList<float2>, bool> In)
        {
            var size = hi - lo;
            string X(float2 p) => F(p.x - lo.x);
            string Y(float2 p) => F(hi.y - p.y);
            string Pts(IEnumerable<float2> ring) => string.Join(" ", ring.Select(p => X(p) + "," + Y(p)));
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(size.x)} {F(size.y)}\" width=\"100%\">");
            sb.Append($"<rect width=\"{F(size.x)}\" height=\"{F(size.y)}\" fill=\"#eef2e6\"/>");
            if (settings.Barriers != null)
                for (int k = 0; k < settings.Barriers.Count; k++)
                {
                    var r = settings.Barriers[k];
                    if (!In(r)) continue;
                    int kind = settings.BarrierKinds != null && k < settings.BarrierKinds.Count ? settings.BarrierKinds[k] : 0;
                    sb.Append($"<polygon points=\"{Pts(r)}\" fill=\"{(kind == 0 ? "#9a9a9a" : kind == 2 ? "#9cc3e6" : "#d6d6d6")}\"/>");
                }
            for (int k = 0; k < sorted.Count; k++)
            {
                var h = sorted[k];
                var g = k < plots.Length ? plots[k] : null;
                if (!h.Edit) g = h.Garden;
                if (g == null || !In(g)) continue;
                bool changed = prev != null && h.Edit && (k >= prev.Length || prev[k] == null || !Same(prev[k], g));
                sb.Append(h.Edit
                    ? $"<polygon points=\"{Pts(g)}\" fill=\"{Colour(h.Id)}\" fill-opacity=\"0.5\" stroke=\"{(changed ? "#000" : Colour(h.Id))}\" stroke-width=\"{(changed ? "0.6" : "0.35")}\"/>"
                    : $"<polygon points=\"{Pts(g)}\" fill=\"none\" stroke=\"#6d8f5a\" stroke-width=\"0.3\" stroke-dasharray=\"1,1\"/>");
            }
            foreach (var h in sorted)
            {
                var lot = Lot(h);
                if (!In(lot)) continue;
                sb.Append($"<polygon points=\"{Pts(lot)}\" fill=\"none\" stroke=\"{(h.Edit ? "#333" : "#999")}\" stroke-width=\"0.25\" stroke-dasharray=\"{(h.Edit ? "none" : "1,1")}\"/>");
                if (h.Footprint != null) sb.Append($"<polygon points=\"{Pts(h.Footprint)}\" fill=\"#b9a99a\" fill-opacity=\"0.7\"/>");
                var front = (h.FrontLeft + h.FrontRight) * 0.5f;
                sb.Append($"<line x1=\"{X(front)}\" y1=\"{Y(front)}\" x2=\"{X(front + h.Forward * 3f)}\" y2=\"{Y(front + h.Forward * 3f)}\" stroke=\"#333\" stroke-width=\"0.4\"/>");
                sb.Append($"<text x=\"{X(h.Position)}\" y=\"{Y(h.Position)}\" font-size=\"3.2\" text-anchor=\"middle\" fill=\"#111\">{h.Id % 10000}</text>");
            }
            if (plan != null)
                foreach (var (house, p, yaw, name) in plan.FencePieces)
                {
                    if (!In(new[] { p })) continue;
                    var d = new float2(math.sin(yaw), math.cos(yaw));
                    sb.Append($"<line x1=\"{X(p - d)}\" y1=\"{Y(p - d)}\" x2=\"{X(p + d)}\" y2=\"{Y(p + d)}\" stroke=\"{(name != null && name.Contains("Hedge") ? "#1f5a1f" : "#7a4a1a")}\" stroke-width=\"0.7\"/>");
                }
            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
