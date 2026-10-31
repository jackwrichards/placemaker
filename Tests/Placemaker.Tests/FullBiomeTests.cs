// P2-8 "full means full" (the user, 2026-10-31: "even when I turn it to full meadow, I still get some trees"): a biome at
// 100 % owns the whole natural ground. Every tree a plan builds (the host's own planting, big trees, a picnic's shade trees,
// every layer) is one of that biome's own; a meadow or a flower field has none at all.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Layers.Common.Biomes;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Types.AdventurePlayground;
using Placemaker.Core.Layers.Types.MemorialPark;
using Placemaker.Core.Tool;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class FullBiomeTests
    {
        private readonly ITestOutputHelper _out;
        public FullBiomeTests(ITestOutputHelper o) { _out = o; }

        private static BiomeOffer OfferOf(string type) => type switch
        {
            "park" => ParkLayer.Biomes,
            "botanicalGarden" => BotanicalGardenLayer.Biomes,
            "memorialPark" => MemorialParkLayer.Biomes,
            "adventurePlayground" => AdventurePlaygroundLayer.Biomes,
            _ => throw new ArgumentException(type),
        };

        /// <summary>A type with the given mix (the others 0) on a w × d block.</summary>
        internal static ComposedPlan Plan(string type, string style, float w, float d, string site, (string id, double share)[] mix, uint seed = 1)
        {
            if (type == "park") return BiomeMixTests.Park(style, w, d, site, mix, -1, seed);
            GroundDesignTests.Lib();
            var gen = GeneratorRegistry.Get(type);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, seed);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            if (style != null) editor.ApplyPreset(r, RecipePaths.Root, style);
            foreach (var (id, _) in OfferOf(type).Biomes) r.Root.Settings.Values[BiomeMix.ControlIdOf(id)] = 0.0;
            foreach (var (id, share) in mix) r.Root.Settings.Values[BiomeMix.ControlIdOf(id)] = share;
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = seed * 1000u + (++k);
            var ring = DynamicDecalsTests.Block(w, d);
            var s = DynamicDecalsTests.Streets(w, d, site == "hills" ? (Func<Unity.Mathematics.float2, float>)(p => 4f * Unity.Mathematics.math.sin(p.x / 45f) * Unity.Mathematics.math.cos(p.y / 38f)) : null);
            TestKit.Entered(r, ring, s);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, s, System.Threading.CancellationToken.None);
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var type in new[] { "park", "botanicalGarden", "memorialPark", "adventurePlayground" })
                foreach (var (id, _) in OfferOf(type).Biomes)
                {
                    var b = BiomeLibrary.Get(id);
                    if (b == null || b.Host) continue;
                    yield return new object[] { type, id };
                }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void AFullBiomePlantsOnlyItsOwnTrees(string type, string id)
        {
            var def = BiomeLibrary.Get(id);
            foreach (var site in new[] { "flat", "hills" })
            {
                var plan = Plan(type, null, 216f, 196f, site, new[] { (id, 100.0) });
                Assert.False(plan.HasErrors);
                var own = new HashSet<string>(def.Trees ?? Array.Empty<string>());
                var strays = new List<string>();
                int trees = 0;
                foreach (var l in plan.Layers)
                    foreach (var o in l.Objects.Where(o => o.Role == ObjectRole.Tree))
                    {
                        trees++;
                        if (def.TreesPerHa <= 0f || !own.Contains(o.Asset.Id ?? "")) strays.Add(l.GeneratorId + ":" + (string.IsNullOrEmpty(o.Asset.Id) ? "auto" : o.Asset.Id));
                    }
                var host = plan.Layers.First(l => l.GeneratorId == type);
                double got = host.Facts.TryGetValue(BiomeMix.FactPrefix + id, out var g) ? g : 0;
                _out.WriteLine($"{type} {id} {site}: {id} {got} % | trees {trees}, strays {strays.Count} {string.Join(",", strays.GroupBy(x => x).Select(x => x.Key + "x" + x.Count()))}");
                // memorial parks keep their axis rows (the memorial's architecture, its own setting)
                if (type == "memorialPark") strays.RemoveAll(s => s.StartsWith("memorialPark:"));
                Assert.True(strays.Count == 0, $"{type} at 100 % {id} ({site}) plants {strays.Count} trees that are not the biome's: {string.Join(",", strays.Distinct())}");
            }
        }
    }
}
