// Outgrow benchmark in the game's own runtime (Unity's Mono, run by tools/monohost): replays recorded planner passes (the game
// runner's lab op polishPassDump), times YardStitch.Plan on each, writes each plan's signature (StitchDump.Signature).
//   tools/monohost/bin/monohost.exe <out>/Placemaker.OutgrowBench.Mono.exe <passes dir or file> [options]
//     --repeat N        plans per pass, the fastest counts (default 1)
//     --label NAME      signatures go to <passes dir>/sig-NAME (default "mono")
//     --baseline NAME   compare with <passes dir>/sig-NAME (made in Mono too: .NET plans a few passes differently)
//     --only N          only pass N
//     --parallel N      also time N passes planned at once
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Placemaker.Core.Neighbours;

namespace Placemaker.OutgrowBench.Mono
{
public static class Program
{
    public static int Main(string[] args)
    {
        string input = args[0], label = "mono", baseline = null;
        int repeat = 1, only = -1, parallel = 0;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repeat": repeat = int.Parse(args[++i]); break;
                case "--label": label = args[++i]; break;
                case "--baseline": baseline = args[++i]; break;
                case "--only": only = int.Parse(args[++i]); break;
                case "--parallel": parallel = int.Parse(args[++i]); break;
            }
        }
        var files = Directory.Exists(input) ? Directory.GetFiles(input, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList() : new List<string> { input };
        if (only >= 0) files = files.Where(f => Path.GetFileNameWithoutExtension(f) == $"pass-{only:000}").ToList();
        var dir = Directory.Exists(input) ? input : Path.GetDirectoryName(input);
        var sigDir = Path.Combine(dir, "sig-" + label);
        Directory.CreateDirectory(sigDir);
        var passes = files.Select(f => (Name: Path.GetFileNameWithoutExtension(f), Input: StitchDump.Read(File.ReadAllText(f)))).ToList();
        Console.WriteLine($"{passes.Count} passes from {dir} (runtime: {Type.GetType("Mono.Runtime") != null})");
        if (passes.Count > 0) YardStitch.Plan(passes[0].Input.Houses, passes[0].Input.Settings);
        double total = 0, worst = 0;
        int differ = 0;
        var stageTotals = new Dictionary<string, double>();
        int gc0 = GC.CollectionCount(0);
        foreach (var (name, (houses, settings)) in passes)
        {
            double best = double.MaxValue;
            StitchPlan plan = null;
            for (int r = 0; r < Math.Max(1, repeat); r++)
            {
                var clock = Stopwatch.StartNew();
                plan = YardStitch.Plan(houses, settings);
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            total += best;
            worst = Math.Max(worst, best);
            var sig = StitchDump.Signature(plan);
            File.WriteAllText(Path.Combine(sigDir, name + ".txt"), sig);
            string same = "";
            if (baseline != null)
            {
                var bpath = Path.Combine(dir, "sig-" + baseline, name + ".txt");
                if (!File.Exists(bpath)) same = "  (no baseline)";
                else if (File.ReadAllText(bpath) == sig) same = "  same";
                else { differ++; same = "  DIFFERS"; }
            }
            Console.WriteLine($"{name}: {houses.Count} houses ({houses.Count(h => h.Edit)} edited): {best:F1} ms{same}");
            var times = plan.Notes.LastOrDefault(n => n.StartsWith("time (ms)")) ?? "";
            foreach (var part in times.Replace("time (ms): ", "").Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(' ');
                if (kv.Length == 2 && double.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms)) stageTotals[kv[0]] = (stageTotals.TryGetValue(kv[0], out var was) ? was : 0) + ms;
            }
        }
        Console.WriteLine("stages (ms, all passes): " + string.Join(", ", stageTotals.Select(kv => $"{kv.Key} {kv.Value:F0}")));
        Console.WriteLine($"gen0 collections: {GC.CollectionCount(0) - gc0}");
        Console.WriteLine($"total {total:F0} ms, worst pass {worst:F0} ms, mean {(passes.Count > 0 ? total / passes.Count : 0):F1} ms");
        if (parallel > 1 && passes.Count > 0)
        {
            var clock = Stopwatch.StartNew();
            System.Threading.Tasks.Parallel.ForEach(passes, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = parallel }, p => YardStitch.Plan(p.Input.Houses, p.Input.Settings));
            Console.WriteLine($"all passes, {parallel} at once: {clock.Elapsed.TotalMilliseconds:F0} ms");
        }
        if (baseline != null) Console.WriteLine(differ == 0 ? "every pass plans the same as " + baseline : $"{differ} passes plan differently from {baseline}");
        return differ == 0 ? 0 : 1;
    }
}
}
