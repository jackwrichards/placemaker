// Placemaker road lab: the roads planner on synthetic land, rated and drawn, without the game.
//
//   dotnet run -c Release --project Tests/Placemaker.RoadLab -- --out docs/roadlab/after [--compare docs/roadlab/before]
//       [--cases city,highway] [--variants default,village] [--label "after"]
//
// Every case (Cases.cs) × variant (default = the roads type's defaults, else a preset id; "+id=value" appends setting
// overrides, e.g. default+perimeterCornerRadius=40) is planned with the REAL Core (RoadsLayer.Plan), rated
// (RoadsRating) and drawn: <out>/<case>~<variant>.svg, <out>/ratings.json, <out>/index.html. With --compare, every card
// shows the earlier run's picture and numbers next to this one's.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;

namespace Placemaker.RoadLab
{
    public static class Program
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static readonly string[] DefaultVariants = { "default" };
        /// <summary>Cases that also run a few other designs (winding, cul-de-sacs, downtown with avenues).</summary>
        public static readonly Dictionary<string, string[]> ExtraVariants = new Dictionary<string, string[]>
        {
            ["open"] = new[] { "default+perimeterRoad=off", "default+gridCharacter=lively" },
            ["city"] = new[] { "downtown", "village", "suburb", "default+perimeterRoad=off", "default+gridCharacter=lively" },
            ["hill"] = new[] { "village", "terraced-grid", "default+perimeterRoad=off" },
            ["irregular"] = new[] { "village", "suburb", "default+gridCharacter=lively" },
            ["everything"] = new[] { "downtown", "village", "default+gridCharacter=lively" },
            ["avenue"] = new[] { "downtown" },
            ["highway"] = new[] { "default+perimeterRoad=off" },
            ["desire"] = new[] { "default+gridCharacter=strict", "default+gridCharacter=lively" },
            ["bighill"] = new[] { "default+gridCharacter=strict", "default+gridCharacter=lively" },
        };

        public sealed class Entry
        {
            public string Case { get; set; }
            public string Variant { get; set; }
            public double Ms { get; set; }
            public Dictionary<string, double> Numbers { get; set; }
            public string Svg { get; set; }
        }

        public static int Main(string[] args)
        {
            string outDir = null, compare = null, label = null;
            string[] cases = Cases.All, variants = null;
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--out": outDir = next; i++; break;
                    case "--compare": compare = next; i++; break;
                    case "--cases": cases = next.Split(','); i++; break;
                    case "--variants": variants = next.Split(','); i++; break;
                    case "--label": label = next; i++; break;
                    case "--trace": Trace = true; break;
                    case "--plans": PlansDir = next; i++; break;
                    case "--dump": Dump = true; break;
                    case "--zoom": Svg.Zoom = next.Split(',').Select(x => float.Parse(x, C)).ToArray(); i++; break;
                }
            }
            string repo = FindRepo();
            outDir ??= Path.Combine(repo, "docs", "roadlab", "latest");
            if (!Path.IsPathRooted(outDir)) outDir = Path.GetFullPath(Path.Combine(repo, outDir));
            if (compare != null && !Path.IsPathRooted(compare)) compare = Path.GetFullPath(Path.Combine(repo, compare));
            Directory.CreateDirectory(outDir);

            var jobs = new List<(string c, string v)>();
            foreach (var c in cases)
            {
                var vs = variants ?? DefaultVariants.Concat(ExtraVariants.TryGetValue(c, out var extra) ? extra : Array.Empty<string>()).ToArray();
                foreach (var v in vs) jobs.Add((c, v));
            }
            Dictionary<string, Entry> before = null;
            if (compare != null && File.Exists(Path.Combine(compare, "ratings.json")))
                before = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Path.Combine(compare, "ratings.json"))).ToDictionary(e => e.Case + "~" + e.Variant);

            Console.WriteLine($"Road lab: {jobs.Count} layouts → {outDir}");
            var entries = new Entry[jobs.Count];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                var (cid, variant) = jobs[i];
                entries[i] = RunOne(cid, variant, outDir, before);
                var n = entries[i].Numbers;
                string delta = before != null && before.TryGetValue(cid + "~" + variant, out var b) ? string.Format(C, " (was {0:0})", b.Numbers["total"]) : "";
                Console.WriteLine(string.Format(C, "  {0,-24} total {1,3:0}{2} | grades {3:0} junctions {4:0} segments {5:0} perimeter {6:0} links {7:0} fit {8:0} | crowded {10:0} slivers {11:0} acute {12:0} kinks {13:0} hooks {14:0} stubs {15:0} along {16:0} xing {17:0} | {9:0} ms",
                    cid + "~" + variant, n["total"], delta, n["grades"], n["junctions"], n["segments"], n["perimeter"], n["links"], n["fit"], entries[i].Ms, n["crowdedJunctions"], n["slivers"], n["acuteJunctions"], n["kinks"], n["hooks"], n["strayStubs"], n["alongside"], n["selfCrossings"]));
            });
            File.WriteAllText(Path.Combine(outDir, "ratings.json"), JsonSerializer.Serialize(entries.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(outDir, "index.html"), Index(entries, before, compare, outDir, label));
            double mean = entries.Average(e => e.Numbers["total"]);
            string wasMean = before != null ? string.Format(C, " (was {0:0.0})", entries.Where(e => before.ContainsKey(e.Case + "~" + e.Variant)).Select(e => before[e.Case + "~" + e.Variant].Numbers["total"]).DefaultIfEmpty(0).Average()) : "";
            Console.WriteLine(string.Format(C, "Mean total {0:0.0}{1}. Done in {2:0.0}s. Open {3}", mean, wasMean, sw.Elapsed.TotalSeconds, Path.Combine(outDir, "index.html")));
            return 0;
        }

        public static bool Trace;
        /// <summary>--plans DIR: rate and draw the plans another build wrote there (PlanText) instead of planning.</summary>
        public static string PlansDir;
        /// <summary>--dump: also write each plan as text (PlanText) next to its picture.</summary>
        public static bool Dump;

        public static SettingsBag SettingsFor(RoadsLayer layer, string variant)
        {
            var parts = variant.Split('+');
            SettingsBag s;
            if (parts[0] == "default") s = layer.Defaults();
            else
            {
                var p = layer.Presets.FirstOrDefault(x => x.Id == parts[0]) ?? throw new ArgumentException("unknown preset " + parts[0]);
                s = p.Values.Clone();
            }
            foreach (var kv in parts.Skip(1))
            {
                var bits = kv.Split('=');
                if (bits.Length != 2) continue;
                object v = double.TryParse(bits[1], NumberStyles.Float, C, out double d) ? d : bits[1] == "true" ? true : bits[1] == "false" ? false : (object)bits[1];
                s.Values[bits[0]] = v;
            }
            return s;
        }

        private static Entry RunOne(string cid, string variant, string outDir, Dictionary<string, Entry> before)
        {
            var lc = Cases.Make(cid);
            var layer = new RoadsLayer();
            var settings = SettingsFor(layer, variant);
            var site = new SiteContext { HeightAt = lc.Height, NearbyNets = lc.Nets, Obstacles = lc.Obstacles };
            var input = new AreaInput { Shape = new AreaShape { Ring = lc.Ring, Source = lc.Source }, Site = site, Settings = settings, Seed = 1 };
            var sw = Stopwatch.StartNew();
            AreaPlan plan;
            var trace = new StringBuilder();
            if (Trace) RoadLayoutSearch.Trace = s => trace.AppendLine("    " + s);
            try
            {
                if (PlansDir != null)
                {
                    string pf = Path.Combine(PlansDir, Safe(cid + "~" + variant) + ".plan.txt");
                    plan = File.Exists(pf) ? PlanText.Read(pf) : new AreaPlan();
                }
                else plan = layer.Plan(input, CancellationToken.None);
            }
            catch (Exception e)
            {
                Console.WriteLine("  " + cid + "~" + variant + " FAILED: " + e);
                plan = new AreaPlan();
            }
            if (Dump) PlanText.Write(Path.Combine(outDir, Safe(cid + "~" + variant) + ".plan.txt"), plan, lc.Ring);
            double ms = sw.Elapsed.TotalMilliseconds;
            RoadLayoutSearch.Trace = null;
            if (trace.Length > 0) Console.Write("  " + cid + "~" + variant + " trace:" + Environment.NewLine + trace);
            var rating = RoadsPlanner.Rate(plan, input, settings, layer.Controls);
            RoadsRating was = null;
            if (before != null && before.TryGetValue(cid + "~" + variant, out var b))
                was = new RoadsRating
                {
                    Total = (float)b.Numbers["total"], Grades = (float)b.Numbers["grades"], Junctions = (float)b.Numbers["junctions"],
                    Segments = (float)b.Numbers["segments"], Perimeter = (float)b.Numbers["perimeter"], Links = (float)b.Numbers["links"], Fit = (float)b.Numbers["fit"],
                    Directness = b.Numbers.TryGetValue("directness", out var bd) ? (float)bd : 100f,
                };
            if (Trace)
            {
                // every planned net (role, ends, attach) for reading the plan in detail
                var dump = new StringBuilder();
                foreach (var n in plan.Nets)
                    dump.AppendLine(string.Format(C, "{0,-14} ({1:0.0},{2:0.0}) -> ({3:0.0},{4:0.0}) attach {5}/{6} len {7:0}", n.Role, n.A.x, n.A.z, n.D.x, n.D.z,
                        n.StartAttach, n.EndAttach, new RoadCurve(n.A, n.B, n.C, n.D).LengthXZ(8)));
                foreach (var m in RoadsPlanner.Rate(plan, input, settings, layer.Controls).Marks) dump.AppendLine(string.Format(C, "mark {0} at ({1:0.0},{2:0.0}) {3:0.0}", m.Kind, m.At.x, m.At.y, m.Value));
                File.WriteAllText(Path.Combine(outDir, Safe(cid + "~" + variant) + ".nets.txt"), dump.ToString());
                // every candidate layout the planner compared, drawn and rated on its own
                var vs = RoadsPlanner.Variants(input, layer.Controls, CancellationToken.None);
                for (int k = 0; k < vs.Count && vs.Count > 1; k++)
                {
                    var vp = RoadsPlanner.PlanCore(input, layer.Controls, CancellationToken.None, vs[k]);
                    var vr = RoadsPlanner.Rate(vp, input, vs[k].Settings, layer.Controls);
                    Console.WriteLine(string.Format(C, "    candidate {0} ({1}): total {2:0.0} | {3}", k, vs[k].Name, vr.Total, vr));
                    File.WriteAllText(Path.Combine(outDir, Safe(cid + "~" + variant) + "~cand" + k + ".svg"), Svg.Draw(lc, variant + " / candidate " + vs[k].Name, vp, vr, null, 0));
                }
            }
            string file = Safe(cid + "~" + variant) + ".svg";
            File.WriteAllText(Path.Combine(outDir, file), Svg.Draw(lc, variant, plan, rating, was, ms));
            return new Entry { Case = cid, Variant = variant, Ms = Math.Round(ms, 1), Numbers = Round(rating.Numbers()), Svg = file };
        }

        public static RoadsRating Rate(AreaPlan plan, LabCase lc, SettingsBag settings)
        {
            var pattern = settings.S(RoadsIds.Pattern, "grid");
            bool perimeter = pattern == "terraces" ? settings.S(RoadsIds.PerimeterRoad, "auto") == "on" : RoadsPlanner.PerimeterWanted(settings.S(RoadsIds.PerimeterRoad, "auto"), lc.Source);
            var o = new RoadsRating.Options
            {
                MaxGrade = (float)settings.Num(RoadsIds.MaxGrade, 10) / 100f,
                JunctionSpacing = (float)settings.Num(RoadsIds.JunctionSpacing, 120),
                Perimeter = perimeter,
                Height = plan.HeightAfter ?? lc.Height,
            };
            return RoadsRating.Measure(plan, lc.Ring.Select(p => p.xz).ToArray(), new SiteContext { HeightAt = lc.Height, NearbyNets = lc.Nets }, o);
        }

        private static Dictionary<string, double> Round(Dictionary<string, double> d) => d.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2));

        private static string Index(Entry[] entries, Dictionary<string, Entry> before, string compareDir, string outDir, string label)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=\"utf-8\"><title>Road lab</title><style>body{background:#0e1316;color:#e6e6e6;font:14px Segoe UI,Arial,sans-serif;margin:16px}"
                + "table{border-collapse:collapse;margin:8px 0 20px}td,th{padding:3px 8px;border-bottom:1px solid #2a3338;text-align:right}th{color:#9fb0ba}td:first-child,th:first-child{text-align:left}"
                + ".card{margin:18px 0 34px}.pics{display:flex;gap:10px;flex-wrap:wrap}.pics img{width:min(100%,780px);border:1px solid #2a3338}.up{color:#7ee081}.down{color:#ff7b6b}h2{margin:4px 0}</style>");
            sb.AppendFormat("<h1>Road lab{0}</h1>", label != null ? " — " + Svg.Esc(label) : "");
            double mean = entries.Average(e => e.Numbers["total"]);
            sb.AppendFormat(C, "<p>{0} layouts, mean total <b>{1:0.0}</b>", entries.Length, mean);
            if (before != null)
            {
                var common = entries.Where(e => before.ContainsKey(e.Case + "~" + e.Variant)).ToList();
                if (common.Count > 0) sb.AppendFormat(C, " (before: {0:0.0})", common.Average(e => before[e.Case + "~" + e.Variant].Numbers["total"]));
            }
            sb.Append("</p><table><tr><th>layout</th><th>total</th><th>grades</th><th>junctions</th><th>segments</th><th>perimeter</th><th>links</th><th>fit</th><th>directness</th><th>ms</th></tr>");
            string Cell(Entry e, string key)
            {
                double v = e.Numbers[key];
                if (before != null && before.TryGetValue(e.Case + "~" + e.Variant, out var b) && b.Numbers.TryGetValue(key, out double w) && Math.Abs(v - w) >= 0.5)
                    return string.Format(C, "<td>{0:0} <span class=\"{1}\">({2}{3:0})</span></td>", v, v > w ? "up" : "down", v > w ? "+" : "", v - w);
                return string.Format(C, "<td>{0:0}</td>", v);
            }
            foreach (var e in entries)
            {
                sb.AppendFormat("<tr><td><a href=\"#{0}\" style=\"color:#9fd3ff\">{1}</a></td>", Safe(e.Case + "~" + e.Variant), e.Case + " · " + e.Variant);
                foreach (var k in new[] { "total", "grades", "junctions", "segments", "perimeter", "links", "fit", "directness" }) sb.Append(Cell(e, k));
                sb.AppendFormat(C, "<td>{0:0}</td></tr>", e.Ms);
            }
            sb.Append("</table>");
            string rel = compareDir != null ? Path.GetRelativePath(outDir, compareDir).Replace('\\', '/') : null;
            foreach (var e in entries)
            {
                string key = e.Case + "~" + e.Variant;
                sb.AppendFormat("<div class=\"card\" id=\"{0}\"><h2>{1}</h2><div class=\"pics\">", Safe(key), Svg.Esc(e.Case + " · " + e.Variant));
                if (before != null && before.TryGetValue(key, out var b)) sb.AppendFormat("<div><div>before</div><img src=\"{0}/{1}\"></div>", rel, b.Svg);
                sb.AppendFormat("<div><div>{0}</div><img src=\"{1}\"></div></div>", before != null ? "after" : "", e.Svg);
                sb.Append("<details><summary>numbers</summary><table><tr><th>metric</th><th>value</th>");
                if (before != null) sb.Append("<th>before</th>");
                sb.Append("</tr>");
                foreach (var kv in e.Numbers)
                {
                    sb.AppendFormat(C, "<tr><td>{0}</td><td>{1:0.##}</td>", kv.Key, kv.Value);
                    if (before != null) sb.AppendFormat(C, "<td>{0}</td>", before.TryGetValue(key, out var bb) && bb.Numbers.TryGetValue(kv.Key, out double w) ? w.ToString("0.##", C) : "");
                    sb.Append("</tr>");
                }
                sb.Append("</table></details></div>");
            }
            return sb.ToString();
        }

        public static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '~' ? ch : '_');
            return sb.ToString();
        }

        private static string FindRepo()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
