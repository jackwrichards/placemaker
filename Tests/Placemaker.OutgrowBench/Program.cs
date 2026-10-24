// Outgrow benchmark: replays recorded planner passes (the game runner's lab op polishPassDump writes them to
// <run>\passes\pass-NNN.json), times YardStitch.Plan on each and writes each plan's signature (StitchDump.Signature).
//   dotnet run -c Release --project Tests/Placemaker.OutgrowBench -- <passes dir or file> [options]
//     --repeat N        plans per pass, the fastest counts (default 3)
//     --label NAME      signatures go to <passes dir>\sig-NAME (default "now")
//     --baseline NAME   compare with <passes dir>\sig-NAME: every pass must plan the same (exit 1 when not)
//     --only N          only pass N (pass-NNN.json)
//     --stages          print each pass's stage times (the planner's own "time (ms)" note)
//     --parallel N      also time N passes planned at once (as the game does: up to 3 workers)
//     --knob NAME=VALUE a planner knob for every pass (e.g. nearOnly=0: every house worked out, as before)
//     --edited          signatures of what the game writes only (the edited houses' outputs: StitchDump.EditedSignature)
//     --alloc-types     what the garbage is made of (the runtime's allocation samples, about one per 100 KB, by type)
//     --serve PORT label=pass.json ...   the tuner page (Serve.cs)
//     --svg             each pass drawn to <passes dir>\svg-NAME\pass-NNN.svg (Svg.Draw): roads and lots, each house's lot
//                       and garden (edited ones filled), the fence lines as the plan leaves them, its fence pieces

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Placemaker.Core.Neighbours;

namespace Placemaker.OutgrowBench
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0) { Console.WriteLine("usage: <passes dir or file> [--repeat N] [--label NAME] [--baseline NAME] [--only N] [--stages] [--parallel N]"); return 2; }
            string input = args[0], label = "now", baseline = null;
            int repeat = 3, only = -1, parallel = 0;
            bool stages = false, editAll = false, editedSig = false, allocTypes = false, svg = false;
            var knobs = new Dictionary<string, float>();
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--repeat": repeat = int.Parse(args[++i]); break;
                    case "--label": label = args[++i]; break;
                    case "--baseline": baseline = args[++i]; break;
                    case "--only": only = int.Parse(args[++i]); break;
                    case "--stages": stages = true; break;
                    case "--parallel": parallel = int.Parse(args[++i]); break;
                    case "--edit-all": editAll = true; break;
                    case "--edited": editedSig = true; break;
                    case "--alloc-types": allocTypes = true; break;
                    case "--svg": svg = true; break;
                    case "--knob": { var kv = args[++i].Split('='); knobs[kv[0]] = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture); break; }
                }
            }
            // the neighbours a plan joined: --pairs pass.json knobs [ids...] (Pairs.cs)
            if (args[0] == "--pairs") return Pairs.Run(args[1], args.Skip(3).ToArray(), args[2]);
            // garden shapes scored with two sets of settings: --score A B dir ... (Score.cs)
            if (args[0] == "--front") return Front.Run(args.Skip(1).ToList());
            // how each garden came to be: --explain <pass or dir> [--at x,z] [--size m] (Explain.cs)
            if (args[0] == "--explain") return Explain.Run(args.Skip(1).ToList());
            if (args[0] == "--score") return Score.Run(args[1], args[2], args.Skip(3).ToList());
            // the tuner: --serve PORT label=pass.json ... (Serve.cs)
            if (args[0] == "--serve")
            {
                var list = args.Skip(2).Select(x => { int eq = x.IndexOf('='); return (Label: x.Substring(0, eq), Path: x.Substring(eq + 1)); }).ToList();
                return Serve.Run(int.Parse(args[1]), list, Environment.GetEnvironmentVariable("PM_GRADES") ?? Path.Combine(Environment.CurrentDirectory, ".forensics", "grades"));
            }
            YardStitch.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread;
            var files = Directory.Exists(input) ? Directory.GetFiles(input, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList() : new List<string> { input };
            if (only >= 0) files = files.Where(f => Path.GetFileNameWithoutExtension(f) == $"pass-{only:000}").ToList();
            var dir = Directory.Exists(input) ? input : Path.GetDirectoryName(input);
            var sigDir = Path.Combine(dir, "sig-" + label);
            Directory.CreateDirectory(sigDir);
            var passes = files.Select(f => (Name: Path.GetFileNameWithoutExtension(f), Input: StitchDump.Read(File.ReadAllText(f)))).ToList();
            foreach (var (_, (_, settings)) in passes)
                foreach (var kv in knobs) { settings.Extra ??= new Dictionary<string, float>(); settings.Extra[kv.Key] = kv.Value; }
            // every polished house planned at once (what one pass for the whole neighbourhood would cost)
            if (editAll)
                foreach (var (_, (houses, _)) in passes)
                    foreach (var h in houses)
                        if (h.Polished) { h.Edit = true; h.Garden = null; h.Standing = null; }
            Console.WriteLine($"{passes.Count} passes from {dir}");
            // warm up the JIT on the first pass
            if (passes.Count > 0) YardStitch.Plan(passes[0].Input.Houses, passes[0].Input.Settings);
            using var allocListener = allocTypes ? new AllocListener() : null;
            double total = 0, worst = 0;
            long totalBytes = 0;
            var stageTotals = new Dictionary<string, double>();
            var allocTotals = new Dictionary<string, double>();
            int differ = 0;
            foreach (var (name, (houses, settings)) in passes)
            {
                double best = double.MaxValue;
                long bytes = 0;
                StitchPlan plan = null;
                for (int r = 0; r < Math.Max(1, repeat); r++)
                {
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    var clock = Stopwatch.StartNew();
                    plan = YardStitch.Plan(houses, settings);
                    best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
                    bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                }
                total += best;
                totalBytes += bytes;
                worst = Math.Max(worst, best);
                var sig = editedSig ? StitchDump.EditedSignature(plan, houses) : StitchDump.Signature(plan);
                File.WriteAllText(Path.Combine(sigDir, name + ".txt"), sig);
                if (svg)
                {
                    var svgDir = Path.Combine(dir, "svg-" + label);
                    Directory.CreateDirectory(svgDir);
                    File.WriteAllText(Path.Combine(svgDir, name + ".svg"), Svg.Draw(houses, settings, plan));
                    File.WriteAllText(Path.Combine(svgDir, name + ".txt"), string.Join("\n", plan.Notes));
                }
                string same = "";
                if (baseline != null)
                {
                    var bpath = Path.Combine(dir, "sig-" + baseline, name + ".txt");
                    if (!File.Exists(bpath)) same = "  (no baseline)";
                    else
                    {
                        var want = File.ReadAllText(bpath);
                        if (want == sig) same = "  same";
                        else
                        {
                            differ++;
                            var a = want.Split('\n');
                            var b = sig.Split('\n');
                            int k = 0;
                            while (k < a.Length && k < b.Length && a[k] == b[k]) k++;
                            same = $"  DIFFERS at line {k + 1}:\n      was: {(k < a.Length ? Trim(a[k]) : "(end)")}\n      now: {(k < b.Length ? Trim(b[k]) : "(end)")}";
                        }
                    }
                }
                int edit = houses.Count(h => h.Edit), barriers = settings.Barriers?.Count ?? 0;
                Console.WriteLine($"{name}: {houses.Count} houses ({edit} edited), {barriers} barriers: {best:F1} ms, {bytes / 1048576.0:F1} MB{same}");
                var times = plan.Notes.LastOrDefault(n => n.StartsWith("time (ms)")) ?? "";
                foreach (var part in (plan.Notes.LastOrDefault(n => n.StartsWith("alloc (MB)")) ?? "").Replace("alloc (MB): ", "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(' ');
                    if (kv.Length == 2 && double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mb)) allocTotals[kv[0]] = (allocTotals.TryGetValue(kv[0], out var was) ? was : 0) + mb;
                }
                if (stages) Console.WriteLine("    " + times);
                foreach (var part in times.Replace("time (ms): ", "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(' ');
                    if (kv.Length == 2 && double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms)) stageTotals[kv[0]] = (stageTotals.TryGetValue(kv[0], out var was) ? was : 0) + ms;
                }
            }
            Console.WriteLine("stages (ms, all passes): " + string.Join(", ", stageTotals.Select(kv => $"{kv.Key} {kv.Value:F0}")));
            Console.WriteLine("allocations by stage (MB, all passes): " + string.Join(", ", allocTotals.Select(kv => $"{kv.Key} {kv.Value:F0}")));
            Console.WriteLine($"allocated {totalBytes / 1048576.0:F0} MB in all, {totalBytes / 1048576.0 / Math.Max(1, passes.Count):F1} MB a pass");
            Console.WriteLine($"total {total:F0} ms, worst pass {worst:F0} ms, mean {(passes.Count > 0 ? total / passes.Count : 0):F1} ms");
            if (parallel > 1 && passes.Count > 0)
            {
                var clock = Stopwatch.StartNew();
                Parallel.ForEach(passes, new ParallelOptions { MaxDegreeOfParallelism = parallel }, p => YardStitch.Plan(p.Input.Houses, p.Input.Settings));
                Console.WriteLine($"all passes, {parallel} at once: {clock.Elapsed.TotalMilliseconds:F0} ms");
            }
            if (allocListener != null)
            {
                long all = allocListener.ByType.Values.Sum();
                Console.WriteLine($"garbage by type (sampled, {all / 1048576.0:F0} MB):");
                foreach (var kv in allocListener.ByType.OrderByDescending(k => k.Value).Take(25)) Console.WriteLine($"  {kv.Value / 1048576.0,8:F1} MB  {kv.Key}");
            }
            if (baseline != null) Console.WriteLine(differ == 0 ? "every pass plans the same as " + baseline : $"{differ} passes plan differently from {baseline}");
            return differ == 0 ? 0 : 1;
        }

        private static string Trim(string s) => s.Length > 300 ? s.Substring(0, 300) + "..." : s;

        /// <summary>The runtime's allocation samples (GC AllocationTick: one about every 100 KB, with its type and size), summed
        /// by type.</summary>
        private sealed class AllocListener : System.Diagnostics.Tracing.EventListener
        {
            public readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> ByType = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();

            protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
            {
                if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
            }

            protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
            {
                if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
                int ti = e.PayloadNames.IndexOf("TypeName"), ai = e.PayloadNames.IndexOf("AllocationAmount64");
                if (ai < 0) ai = e.PayloadNames.IndexOf("AllocationAmount");
                string type = ti >= 0 ? e.Payload[ti] as string ?? "?" : "?";
                long amount = ai >= 0 ? Convert.ToInt64(e.Payload[ai]) : 100000;
                ByType.AddOrUpdate(type, amount, (_, v) => v + amount);
            }
        }
    }
}
