// The paved squares' designed ground (Core/Decals/Dynamic/PlazaGround.cs, the plazas makeover 2026-10-29,
// docs/runbooks/plazas.md): every shown style of every plaza type gets instances from its causes within the budget, the
// Wear choice scales them, it is deterministic and quick. PM_WRITE_GROUND=1 renders each plan from above and close looks
// to .forensics/ground-plazas/ (PM_GROUND_ONLY=plaza-grand,marketSquare-weekly limits it).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Decals;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class PlazaGroundTests
    {
        private readonly ITestOutputHelper _out;
        public PlazaGroundTests(ITestOutputHelper o) { _out = o; }

        public static readonly (string type, string style)[] Shown =
        {
            ("plaza", "grand"), ("plaza", "rooms"), ("plaza", "railed"), ("plaza", "terraces"), ("plaza", "modern"), ("plaza", "piazza"),
            ("plaza", "sunkenPlaza"), ("plaza", "marketHall"), ("plaza", "dining"), ("plaza", "grandSteps"), ("plaza", "grungy"),
            ("marketSquare", "bigMarket"), ("marketSquare", "weekly"), ("amphitheatre", "theatre"), ("amphitheatre", "classic"),
            ("transitPlaza", "busPlaza"), ("fairground", "fairground"),
        };

        internal static ComposedPlan Compose(string type, string preset, float w, float d, string wear = null, Func<float2, float> ground = null)
        {
            GroundDesignTests.Lib();
            var gen = GeneratorRegistry.Get(type);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, 1);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            if (preset != null && gen.Presets != null && gen.Presets.Any(p => p.Id == preset)) editor.ApplyPreset(r, RecipePaths.Root, preset);
            if (wear != null) r.Root.Settings.Values[DressingControls.StyleId] = wear;
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 3000u + (++k);
            var ring = DynamicDecalsTests.Block(w, d);
            var s = DynamicDecalsTests.Streets(w, d, ground);
            TestKit.Entered(r, ring, s);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, s, CancellationToken.None);
        }

        private static List<DecalInstance> Instances(ComposedPlan p) => p.Layers.SelectMany(l => l.DecalInstances).ToList();

        private static bool Wanted(string name)
        {
            var only = Environment.GetEnvironmentVariable("PM_GROUND_ONLY");
            return string.IsNullOrEmpty(only) || only.Split(',').Any(o => name.StartsWith(o, StringComparison.Ordinal));
        }

        public static IEnumerable<object[]> Cases() => Shown.Select(s => new object[] { s.type, s.style });

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryShownStyleGetsItsGround(string type, string style)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var plan = Compose(type, style, 146f, 136f);
            var list = Instances(plan);
            var causes = list.GroupBy(x => (x.Cause ?? "").Split(':')[0]).ToDictionary(g => g.Key, g => g.Count());
            _out.WriteLine($"{type}-{style}: {list.Count} instances in {sw.ElapsedMilliseconds} ms; {plan.Weathering}");
            Assert.True(list.Count > 80, $"{type}-{style}: only {list.Count}");
            Assert.True(list.Count <= Weathering.MaxInstances);
            Assert.Contains("tone", causes.Keys);
            Assert.True(causes.Keys.Count(c => c != "tone") >= 3, string.Join(",", causes.Keys));
            string name = $"{type}-{style}";
            if (Environment.GetEnvironmentVariable("PM_WRITE_GROUND") == "1" && Wanted(name))
                GroundDesignTests.PictureOf(plan, name, new[] { "feet:lane", "feet:cafe", "joints:spill", "joints:weeds", "leaves", "water:puddle", "neglect:skate", "feet:gate", "age:crack", "walls:moss" }, "ground-plazas");
        }

        [Fact]
        public void WearScalesTheCausesAndTheGroundIsDeterministic()
        {
            int Count(string wear) => Instances(Compose("plaza", "piazza", 146f, 136f, wear)).Count(x => !(x.Cause ?? "").StartsWith("tone", StringComparison.Ordinal));
            int clean = Count("clean"), lived = Count("livedIn"), gritty = Count("gritty");
            _out.WriteLine($"clean {clean}, lived-in {lived}, gritty {gritty}");
            Assert.True(clean < lived && clean < gritty, $"{clean} {lived} {gritty}");
            var a = Instances(Compose("plaza", "piazza", 146f, 136f));
            var b = Instances(Compose("plaza", "piazza", 146f, 136f));
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Kind, b[i].Kind); Assert.Equal(a[i].P, b[i].P); }
        }

        [Fact]
        public void TinyAndHugeSquaresStayWithinBudget()
        {
            foreach (var (w, d) in new[] { (24f, 22f), (420f, 380f) })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var plan = Compose("plaza", "modern", w, d);
                var list = Instances(plan);
                _out.WriteLine($"{w} x {d}: {list.Count} instances, whole plan {sw.ElapsedMilliseconds} ms; {plan.Weathering}");
                Assert.True(list.Count <= Weathering.MaxInstances);
                Assert.True(list.Count <= Math.Max(150f, w * d / 10000f * Weathering.PerHectare) * PlazaGround.BudgetFactor * 1.15f + 20, $"{list.Count}");
            }
        }
    }
}
