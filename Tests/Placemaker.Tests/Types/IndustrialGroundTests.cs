// Industrial category, the marks of work (Core/Layers/Types/Industrial/YardWear.cs, docs/runbooks/industrial.md): every
// industrial style gets library instances from its own causes within the budget, deterministically and fast, on a block, a
// big yard, a tiny plot and a huge one. PM_WRITE_GROUND=1 (PM_GROUND_ONLY=<type> or <type>/<style>) renders each plan from
// above to .forensics/ground/industrial/ with the real variants and close-ups by cause.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    public class IndustrialGroundTests
    {
        private readonly ITestOutputHelper _out;
        public IndustrialGroundTests(ITestOutputHelper o) { _out = o; }

        public static readonly string[] Types = { "containerYard", "carYard", "junkYard", "industrialWorks", "logisticsYard", "scrapyard", "landfill", "quarry" };

        public static IEnumerable<object[]> All() =>
            Types.SelectMany(t => TestKit.Gen(t).Presets.Select(p => new object[] { t, p.Id }));

        private static AreaPlan Plan(string type, string preset, float w, float d)
        {
            GroundDesignTests.Lib();
            return YardTestKit.PlanPreset(type, preset, TestKit.Rect(w, d), TestKit.RoadAlongBottom(w));
        }

        [Theory]
        [MemberData(nameof(All))]
        public void EveryStyleGetsTheMarksOfItsWork(string type, string preset)
        {
            var sw = Stopwatch.StartNew();
            var plan = Plan(type, preset, 220, 200);
            double ms = sw.Elapsed.TotalMilliseconds;
            var list = plan.DecalInstances;
            var byCause = list.GroupBy(x => x.Cause).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count());
            _out.WriteLine($"{type}/{preset}: {list.Count} instances, wear {plan.Facts.GetValueOrDefault("yardWearMs")} ms, plan {ms:F0} ms: {string.Join(", ", byCause)}");
            // a capped tip is grassed over: its gate, its fence and little else
            Assert.True(list.Count > (preset == "capped" ? 10 : 40),$"{type}/{preset}: only {list.Count} marks");
            Assert.True(list.Count <= 4.4f * 260f * 1.1f, $"{type}/{preset}: {list.Count} over the budget");
            Assert.Contains(list, x => x.Cause.StartsWith("wheels") || x.Cause.StartsWith("gate"));
            if (Environment.GetEnvironmentVariable("PM_WRITE_GROUND") == "1" && Wanted(type, preset)) Picture(plan, $"{type}-{preset}");
        }

        [Fact]
        public void MarksAreDeterministic()
        {
            var a = Plan("carYard", "salvage", 160, 140).DecalInstances;
            var b = Plan("carYard", "salvage", 160, 140).DecalInstances;
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Kind, b[i].Kind); Assert.Equal(a[i].P, b[i].P); Assert.Equal(a[i].Variant, b[i].Variant); }
        }

        [Theory]
        [InlineData("junkYard", "sorted", 45f, 40f)]
        [InlineData("containerYard", "terminal", 600f, 560f)]
        [InlineData("carYard", "crusher", 600f, 560f)]
        public void TinyAndHugeYardsStayWithinTheirBudget(string type, string preset, float w, float d)
        {
            var plan = Plan(type, preset, w, d);
            float ha = w * d / 10000f;
            int cap = (int)math.clamp(ha * 260f, 120f, 7000f);
            _out.WriteLine($"{type}/{preset} {w}x{d}: {plan.DecalInstances.Count} instances (cap {cap}), {plan.Facts.GetValueOrDefault("yardWearMs")} ms, density {plan.Facts.GetValueOrDefault("decalDensityPct")} %");
            Assert.True(plan.DecalInstances.Count <= cap * 1.05f + 10);
            Assert.True(plan.Facts.GetValueOrDefault("yardWearMs") < 1500);
        }

        // ───────────── pictures ─────────────

        private static bool Wanted(string type, string preset)
        {
            var only = Environment.GetEnvironmentVariable("PM_GROUND_ONLY");
            return string.IsNullOrEmpty(only) || only.Split(',').Any(o => o == type || o == type + "/" + preset);
        }

        private void Picture(AreaPlan plan, string name)
        {
            var lib = GroundDesignTests.Lib();
            var list = plan.DecalInstances.ToList();
            var ids = list.Select(d => d.Kind + "#" + d.Variant).Distinct().ToList();
            Parallel.ForEach(ids, id =>
            {
                if (GroundDesignTests._baked.ContainsKey(id)) return;
                DecalVariants.TryParseId(id, out var kind, out int v);
                GroundDesignTests._baked[id] = DecalVariants.Bake(lib.Get(kind), v);
            });
            var dir = Path.Combine(TestKit.RepoRoot(), ".forensics", "ground", "industrial");
            Directory.CreateDirectory(dir);
            var cp = new ComposedPlan();
            cp.Layers.Add(plan);
            var dirt = new float3(0.47f, 0.4f, 0.31f);
            GroundDesignTests.Render(cp, list, new float2(-4f), new float2(224f, 204f), 0.1f, Path.Combine(dir, name + "-top.png"), true, dirt);
            var seen = new HashSet<string>();
            foreach (var d in list)
            {
                string c = d.Cause.Split(':')[0];
                if (!seen.Add(c)) continue;
                GroundDesignTests.Render(cp, list, d.P - 12f, d.P + 12f, 0.025f, Path.Combine(dir, $"{name}-{c}.png"), true, dirt);
            }
        }
    }
}
