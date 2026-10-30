// The parks makeover (docs/runbooks/parks.md): every park style gets places with moods and a designed ground; every seed
// gets several distinct moods; planning stays quick; deterministic. PM_WRITE_GROUND=1 renders the plans from above to
// .forensics/ground/ (PM_GROUND_ONLY=park-natural limits it).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class ParkGroundTests
    {
        private readonly ITestOutputHelper _out;
        public ParkGroundTests(ITestOutputHelper o) { _out = o; }

        internal static ComposedPlan Plan(string type, string preset, float w, float d, Func<float2, float> ground, uint seed = 1)
        {
            GroundDesignTests.Lib();
            var gen = GeneratorRegistry.Get(type);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, seed);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            if (preset != null) editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = seed * 1000u + (++k);
            var ring = DynamicDecalsTests.Block(w, d);
            var s = DynamicDecalsTests.Streets(w, d, ground);
            TestKit.Entered(r, ring, s);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, s, CancellationToken.None);
        }

        private static Func<float2, float> Ground(string site) => site == "slope" ? p => 0.05f * p.y : site == "hills" ? (Func<float2, float>)(p => 2.5f * math.sin(p.x / 40f) * math.cos(p.y / 33f)) : null;
        private static float2 Size(string site) => site == "block" ? new float2(146f, 136f) : new float2(216f, 196f);

        public static IEnumerable<object[]> Cases()
        {
            foreach (var st in new[] { "natural", "garden", "urban", "picnic", "wild", "neglected" })
                foreach (var site in new[] { "block", "big", "slope" }) yield return new object[] { "park", st, site };
            foreach (var t in new[] { ("botanicalGarden", "flowerShow"), ("botanicalGarden", "shrubbery"), ("memorialPark", "garden"), ("memorialPark", "war"), ("memorialPark", "reflection"), ("memorialPark", "sculpture"),
                         ("adventurePlayground", "adventure"), ("adventurePlayground", "nature"), ("zenGarden", "karesansui"), ("zenGarden", "stroll") })
                yield return new object[] { t.Item1, t.Item2, "block" };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryStyleGetsMoodsAndADesignedGround(string type, string style, string site)
        {
            var sw = Stopwatch.StartNew();
            var sz = Size(site);
            var plan = Plan(type, style, sz.x, sz.y, Ground(site));
            sw.Stop();
            Assert.False(plan.HasErrors, string.Join("; ", plan.Layers.SelectMany(l => l.Issues).Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
            var w = plan.Weathering;
            Assert.NotNull(w);
            var host = plan.Layers.First(l => l.GeneratorId == type);
            var moods = host.Facts.Where(f => f.Key.StartsWith("mood.")).Select(f => f.Key + "=" + f.Value).ToList();
            _out.WriteLine($"{type}/{style}/{site}: compose {sw.ElapsedMilliseconds} ms; moods [{string.Join(", ", moods)}] rocks {Fact(host, "moodRocks")} plants {Fact(host, "moodPlants")} terrain {Fact(host, "moodTerrainCells")} cells");
            _out.WriteLine($"  {w}");
            Assert.InRange(w.Instances, 200, Weathering.MaxInstances);
            Assert.True(w.Ms < 4000, $"weathering {w.Ms:F0} ms");
            if (type == "park" && style != "urban")
            {
                Assert.True(moods.Count >= 2, "a park has several distinct moods: " + string.Join(", ", moods));
                Assert.True(w.ByCause.Keys.Any(c => c.StartsWith("mood:")), "no mood ground");
            }
            var lib = GroundDesignTests.Lib();
            foreach (var d in plan.Layers.SelectMany(l => l.DecalInstances)) Assert.True(lib.Get(d.Kind) != null, d.Kind);
            if (Environment.GetEnvironmentVariable("PM_WRITE_GROUND") == "1" && Wanted($"{type}-{style}") && site != "slope")
                new GroundDesignTests(_out).Picture(plan, $"{type}-{style}-{site}");
        }

        [Fact]
        public void EverySeedHasDistinctMoods()
        {
            for (uint seed = 1; seed <= 8; seed++)
            {
                var plan = Plan("park", "natural", 180f, 160f, null, seed);
                var host = plan.Layers.First(l => l.GeneratorId == "park");
                var moods = host.Facts.Where(f => f.Key.StartsWith("mood.")).ToList();
                _out.WriteLine($"seed {seed}: {string.Join(", ", moods.Select(m => m.Key + "=" + m.Value))}");
                Assert.True(moods.Count >= 2, $"seed {seed}: one mood only");
                Assert.True(moods.All(m => m.Value <= 2), $"seed {seed}: a mood three times");
            }
        }

        [Theory]
        [InlineData(24f, 24f)]
        [InlineData(600f, 600f)]
        public void TinyAndHugeParksStayQuick(float w, float d)
        {
            var sw = Stopwatch.StartNew();
            var plan = Plan("park", "natural", w, d, p => 3f * math.sin(p.x / 60f) * math.cos(p.y / 50f));
            sw.Stop();
            var host = plan.Layers.First(l => l.GeneratorId == "park");
            _out.WriteLine($"{w} x {d}: compose {sw.ElapsedMilliseconds} ms; {plan.Weathering}; moods {Fact(host, "moodPlaces")} terrain {Fact(host, "moodTerrainCells")} cells");
            Assert.False(plan.HasErrors);
            Assert.True(sw.ElapsedMilliseconds < 12000, $"compose {sw.ElapsedMilliseconds} ms");
            Assert.True(plan.Weathering == null || plan.Weathering.Instances <= Weathering.MaxInstances);
        }

        [Fact]
        public void ParkGroundIsDeterministic()
        {
            var a = Plan("park", "natural", 180f, 160f, p => 0.03f * p.x);
            var b = Plan("park", "natural", 180f, 160f, p => 0.03f * p.x);
            var ia = a.Layers.SelectMany(l => l.DecalInstances).ToList();
            var ib = b.Layers.SelectMany(l => l.DecalInstances).ToList();
            Assert.Equal(ia.Count, ib.Count);
            for (int i = 0; i < ia.Count; i++) { Assert.Equal(ia[i].Kind, ib[i].Kind); Assert.Equal(ia[i].P, ib[i].P); }
        }

        private static double Fact(AreaPlan p, string k) => p.Facts.TryGetValue(k, out var v) ? v : 0;

        private static bool Wanted(string id)
        {
            var only = Environment.GetEnvironmentVariable("PM_GROUND_ONLY");
            return string.IsNullOrEmpty(only) || only.Split(',').Any(o => id.StartsWith(o, StringComparison.Ordinal));
        }
    }
}
