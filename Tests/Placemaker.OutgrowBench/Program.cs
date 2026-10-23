// Outgrow benchmark: replays recorded planner passes (the game runner's lab op polishPassDump writes them to
// <run>\passes\pass-NNN.json), times YardStitch.Plan on each and writes each plan's signature (StitchDump.Signature).
//   dotnet run -c Release --project Tests/Placemaker.OutgrowBench -- <passes dir or file> [options]
//     --repeat N        plans per pass, the fastest counts (default 3)
//     --label NAME      signatures go to <passes dir>\sig-NAME (default "now")
//     --baseline NAME   compare with <passes dir>\sig-NAME: every pass must plan the same (exit 1 when not)
//     --only N          only pass N (pass-NNN.json)
//     --stages          print each pass's stage times (the planner's own "time (ms)" note)
//     --parallel N      also time N passes planned at once (as the game does: up to 3 workers)

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
            bool stages = false, editAll = false;
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
                }
            }
            YardStitch.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread;
            var files = Directory.Exists(input) ? Directory.GetFiles(input, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList() : new List<string> { input };
            if (only >= 0) files = files.Where(f => Path.GetFileNameWithoutExtension(f) == $"pass-{only:000}").ToList();
            var dir = Directory.Exists(input) ? input : Path.GetDirectoryName(input);
            var sigDir = Path.Combine(dir, "sig-" + label);
            Directory.CreateDirectory(sigDir);
            var passes = files.Select(f => (Name: Path.GetFileNameWithoutExtension(f), Input: StitchDump.Read(File.ReadAllText(f)))).ToList();
            // every polished house planned at once (what one pass for the whole neighbourhood would cost)
            if (editAll)
                foreach (var (_, (houses, _)) in passes)
                    foreach (var h in houses)
                        if (h.Polished) { h.Edit = true; h.Garden = null; h.Standing = null; }
            Console.WriteLine($"{passes.Count} passes from {dir}");
            // warm up the JIT on the first pass
            if (passes.Count > 0) YardStitch.Plan(passes[0].Input.Houses, passes[0].Input.Settings);
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
                var sig = StitchDump.Signature(plan);
                File.WriteAllText(Path.Combine(sigDir, name + ".txt"), sig);
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
            if (baseline != null) Console.WriteLine(differ == 0 ? "every pass plans the same as " + baseline : $"{differ} passes plan differently from {baseline}");
            return differ == 0 ? 0 : 1;
        }

        private static string Trim(string s) => s.Length > 300 ? s.Substring(0, 300) + "..." : s;
    }
}
