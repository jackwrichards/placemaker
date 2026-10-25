// (perf) --bench: where planning time goes, per type × preset × size. Composes every gallery job one at a time (no
// drawing, no parallelism, so the times are not disturbed), BenchRepeat times each (the fastest run is kept, the first
// one pays the JIT), and writes <out>/bench.csv (one row per job: total, assets, and every Composer / layer step from
// PlanClock) plus <out>/bench-steps.csv (each step's time summed over all jobs). Runs on .NET: the game's Mono is slower
// (about 2–4×), the shares are what matter.
//   dotnet run -c Release --project Tests/Placemaker.Gallery -- --bench --out .forensics/bench/plan [--areas 1,5,12] [--only park,parking] [--no-features]

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core;

namespace Placemaker.Gallery
{
    public static class Bench
    {
        public static int Run(List<Job> jobs, Catalog catalog, string outDir)
        {
            var rows = new List<(Job job, Result res)>();
            var steps = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < jobs.Count; i++)
            {
                Result best = null;
                for (int k = 0; k < Math.Max(1, Renderer.BenchRepeat); k++)
                {
                    var j = jobs[i];
                    var copy = new Job { Type = j.Type, Variant = j.Variant, Title = j.Title, Recipe = j.Recipe?.Clone(), TargetArea = j.TargetArea, Shape = j.Shape, Site = j.Site, Shore = j.Shore, Slope = j.Slope, RoadSite = j.RoadSite, Map = j.Map, Custom = j.Custom };
                    var r = Renderer.Run(copy, catalog, outDir);
                    if (best == null || r.Ms < best.Ms) best = r;
                }
                rows.Add((jobs[i], best));
                if (best.Plan != null) foreach (var kv in Flatten(best.Plan.Timings, "")) steps[kv.Key] = (steps.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
                Console.WriteLine($"  {jobs[i].Type}/{jobs[i].Variant} {best.Area / 10000:0.#} ha: {best.Ms:0} ms{(best.Failure != null ? " FAILED " + best.Failure : "")} | {PlanTimings.Describe(best.Plan)}");
            }

            var csv = new StringBuilder("type,variant,ha,totalMs,stepsMs,afterMs,objects,nets,sig,failure,steps\n");
            foreach (var (job, res) in rows.OrderByDescending(r => r.res.Ms))
            {
                double sum = res.Plan?.Timings.Sum(kv => kv.Value) ?? 0;
                csv.Append(string.Join(",", job.Type, job.Variant, F(res.Area / 10000), F(res.Ms), F(sum), F(res.Ms - sum),
                    res.Plan?.Layers.Sum(l => l?.Objects.Count ?? 0) ?? 0, res.Plan?.Layers.Sum(l => l?.Nets.Count ?? 0) ?? 0,
                    Signature(res.Plan), Q(res.Failure ?? ""), Q(PlanTimings.Describe(res.Plan)))).Append('\n');
            }
            File.WriteAllText(Path.Combine(outDir, "bench.csv"), csv.ToString());
            var sc = new StringBuilder("step,totalMs\n");
            foreach (var kv in steps.OrderByDescending(kv => kv.Value)) sc.Append(Q(kv.Key)).Append(',').Append(F(kv.Value)).Append('\n');
            File.WriteAllText(Path.Combine(outDir, "bench-steps.csv"), sc.ToString());
            Console.WriteLine($"Bench: {rows.Count} jobs, {rows.Sum(r => r.res.Ms) / 1000:0.0} s planning → {Path.Combine(outDir, "bench.csv")}");
            return 0;
        }

        /// <summary>"park" and "park/paths" etc.: each step under its parents' names (the inner steps PlanClock kept in brackets).</summary>
        private static IEnumerable<KeyValuePair<string, double>> Flatten(List<KeyValuePair<string, double>> timings, string prefix)
        {
            foreach (var kv in timings)
            {
                int b = kv.Key.IndexOf(" [", StringComparison.Ordinal);
                string name = b < 0 ? kv.Key : kv.Key.Substring(0, b);
                yield return new KeyValuePair<string, double>(prefix + name, kv.Value);
            }
        }

        /// <summary>A hash of everything the plan builds (objects, nets, surfaces, areas, terraform, decals, zoning): equal before
        /// and after a speed-up = the same design.</summary>
        public static string Signature(ComposedPlan plan)
        {
            if (plan == null) return "-";
            ulong h = 14695981039346656037UL;
            void S(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } }
            string R(float v) => v.ToString("R", CultureInfo.InvariantCulture);
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var o in l.Objects) S($"O{(int)o.Role}:{R(o.Position.x)},{R(o.Position.y)},{R(o.Position.z)},{R(o.Yaw)},{o.Asset.Id},{o.Variation};");
                foreach (var n in l.Nets) S($"N{(int)n.Role}:{R(n.A.x)},{R(n.A.z)},{R(n.B.x)},{R(n.C.z)},{R(n.D.x)},{R(n.D.z)};");
                foreach (var s in l.Surfaces) { S("S"); foreach (var p in s.Ring) S($"{R(p.x)},{R(p.z)} "); }
                foreach (var a in l.Areas) { S("A"); foreach (var p in a.Ring) S($"{R(p.x)},{R(p.z)} "); }
                S("T" + l.Terraform.Count + "Z" + l.Zoning.Count);
            }
            return h.ToString("x16");
        }

        private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        private static string Q(string s) => "\"" + s.Replace("\"", "'") + "\"";
    }
}
