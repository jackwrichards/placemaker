// Type wave: quarry (industrial) — terraced pit, spiral haul road, the flooded "lake dig", presets,
// terrain blending, water containment, features, lang.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using K = Placemaker.Tests.Types.WorkSiteTestKit;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class QuarryTests
    {
        private const string Id = "quarry";
        private readonly ITestOutputHelper _out;
        public QuarryTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => GeneratorRegistry.Get(Id).Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string id) => GeneratorRegistry.Get(Id).Presets.Single(p => p.Id == id);

        private static AreaPlan Plan(string preset, float3[] ring, SiteContext site = null, params (string k, object v)[] extra)
        {
            var r = K.WithPreset(Id, Preset(preset));
            foreach (var (k, v) in extra) r.Root.Settings.Values[k] = v;
            var plan = K.Compose(r, ring, site ?? K.Road(ring));
            Assert.True(string.IsNullOrEmpty(K.Errors(plan)), K.Errors(plan));
            return plan.Layers[0];
        }

        [Fact]
        public void SchemaIsDeepButTidy()
        {
            var g = GeneratorRegistry.Get(Id);
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 40);   // (Phase 1) look settings not counted
            Assert.InRange(g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Distinct().Count(), 4, 9);   // (Phase 1) look settings not counted
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.Equal("industrial", GeneratorGroups.Of(g));
            Assert.Equal(g.Controls.Single(c => c.Id == "subtype").Options.Select(o => o.Value).OrderBy(x => x),
                g.Presets.Select(p => p.Values.S("subtype", "")).Distinct().OrderBy(x => x));
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneAndSixHectares(string preset)
        {
            foreach (var (name, ring) in K.Shapes())
            {
                var site = K.Road(ring);
                site.HeightAt = p => 0.04f * p.x - 0.02f * p.y;
                var layer = Plan(preset, ring, site);
                Assert.True(layer.Objects.Count(o => o.Role != ObjectRole.Decal) >= 8, $"{preset}/{name}: only {layer.Objects.Count} objects");
                K.AssertInside(layer, ring);
                K.AssertNothingOnPaths(layer);
                TestKit.AssertSurfacesAccepted(layer);
                K.AssertBlended(layer, ring, site, (float)Preset(preset).Values.Num("maxSlope", 60));
                Assert.NotEmpty(layer.Terraform);
                Assert.Contains(layer.Regions, r => r.Key == "place:pit");
                Assert.Contains(layer.Regions, r => r.Tags.Contains(RegionTags.Core));
                _out.WriteLine($"{preset}/{name}: {layer.Objects.Count} objects, {layer.Terraform.Count} terraform, {layer.Surfaces.Count} surfaces, " +
                    $"benches {layer.Facts.GetValueOrDefault("benches")}, lake {layer.Facts.GetValueOrDefault("lakeArea"):0}");
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministicAndFast(string preset)
        {
            var ring = TestKit.Rect(250, 200);
            var a = Plan(preset, ring);
            var sw = Stopwatch.StartNew();
            var b = Plan(preset, ring);
            sw.Stop();
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            Assert.Equal(a.Terraform.Count, b.Terraform.Count);
            _out.WriteLine($"{preset}: {sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{preset} took {sw.ElapsedMilliseconds} ms for 5 ha");
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void RespectsReservedSpace(string preset)
        {
            var ring = TestKit.Rect(200, 160);
            var input = TestKit.Input(ring, Preset(preset).Values.Clone(), 7, null, K.Road(ring));
            input.Reserved.AddDisc(new float2(15, 80), 6f, "Furniture");
            input.Reserved.AddCorridor(new float2(185, 20), new float2(185, 140), 2f, "Pathway");
            var plan = GeneratorRegistry.Get(Id).Plan(input, CancellationToken.None);
            Assert.False(plan.HasErrors);
            K.AssertClearOfReserved(plan, input.Reserved);
        }

        [Fact]
        public void BenchesStepDownAndTheHaulRoadRamps()
        {
            var ring = TestKit.Rect(260, 200);
            var layer = Plan("openPit", ring, null, ("pitShape", "follow"));
            int levels = (int)layer.Facts["benches"];
            Assert.True(levels >= 3, "benches " + levels);
            // Bench floors at multiples of the bench height below the rim (6 m), and ramp cells in between.
            var heights = layer.Terraform.Where(t => float.IsNaN(t.WaterLevel)).Select(t => -t.TargetHeight).ToList();
            for (int k = 1; k <= levels; k++) Assert.Contains(heights, h => math.abs(h - 6f * k) < 0.05f);
            Assert.True(heights.Count(h => math.abs(h / 6f - math.round(h / 6f)) > 0.1f) >= 3, "no ramp cells");
            Assert.InRange(heights.Max(), 6f * levels - 0.05f, 6f * levels + 0.05f);
            // The haul road surface reaches into the pit.
            var pit = Poly.To2D(layer.Regions.Single(r => r.Key == "place:pit").Ring);
            Assert.Contains(layer.Surfaces, s => s.Ring.Count(p => Poly.Contains(pit, p.xz)) > 3);
        }

        [Theory]
        [InlineData("lakeDig")]
        [InlineData("flooded")]
        public void FloodedPitHoldsItsWaterBelowEveryBank(string preset)
        {
            foreach (var (name, ring) in K.Shapes())
            {
                var site = K.Road(ring);
                site.HeightAt = p => 0.05f * p.x + 0.02f * p.y;   // sloped site: the low rim decides
                var layer = Plan(preset, ring, site);
                var lake = layer.Terraform.SingleOrDefault(t => !float.IsNaN(t.WaterLevel));
                Assert.True(lake != null, preset + "/" + name + " has no water: " + string.Join(",", layer.Issues.Select(i => i.Key)) + " " + string.Join(",", layer.Facts.Select(f => f.Key + "=" + f.Value)));
                Assert.True(lake.Permanent);
                Assert.True(lake.WaterLevel - lake.TargetHeight >= 1f, "too shallow");
                Assert.Contains(layer.Issues, i => i.Key == "Placemaker.Quarry.Issue.LakePermanent" && i.Severity == IssueSeverity.Warning);
                // Never above the original ground anywhere on the pit rim (no spill over the outline side).
                var pit = Poly.To2D(layer.Regions.Single(r => r.Key == "place:pit").Ring);
                foreach (var p in Poly.Resample(pit, 4f)) Assert.True(lake.WaterLevel < site.HeightAt(p) - 0.3f, $"{preset}/{name}: water {lake.WaterLevel:0.0} above rim {site.HeightAt(p):0.0}");
                // Every pit cell shallower than the water lies inside the lake footprint or above the level.
                Assert.True(layer.Facts["lakeArea"] > 200, "lake area");
                Assert.True(TestKit.Stat(layer, Keys.StatLakeArea) > 200);
            }
        }

        [Fact]
        public void LakeDigLooksLikeAnUglyWorkingLake()
        {
            var ring = TestKit.LShape(293);
            var dig = Plan("lakeDig", ring);
            var flooded = Plan("flooded", ring);
            // Working: machines, stockpiles, mud everywhere; old flooded quarry: lookout, overgrowth, no machines.
            Assert.True(dig.Objects.Count(o => o.Category == AssetCategories.ConstructionMachine) >= 5);
            // Art pass: automatic stockpiles are real 9 × 15 m bulk heaps, one per two of the old truck-load cones.
            Assert.True(dig.Objects.Count(o => o.Category == AssetCategories.ConstructionPile) >= 4);
            Assert.True(dig.Paint.Count >= 4);
            Assert.DoesNotContain(flooded.Objects, o => o.Category == AssetCategories.ConstructionMachine);
            Assert.True(flooded.Facts.ContainsKey("lookout"));
            Assert.Contains(flooded.Nets, n => n.Role == NetRole.Pathway);
            Assert.True(flooded.Objects.Count(o => o.Role == ObjectRole.Tree) > 20);
            Assert.True(flooded.Objects.Count(o => o.Category == AssetCategories.Rock) > 10);
            // Both: warnings (rim fence + signs).
            foreach (var p in new[] { dig, flooded })
            {
                Assert.True(p.Nets.Count(n => n.Role == NetRole.Fence) > 10);
                Assert.Contains(p.Objects, o => o.Category == AssetCategories.ConstructionSign);
            }
        }

        [Fact]
        public void SubtypesAreVisiblyDifferent()
        {
            var ring = TestKit.Rect(240, 200);
            var plans = Preset("openPit") == null ? null : GeneratorRegistry.Get(Id).Presets.ToDictionary(p => p.Id, p => Plan(p.Id, ring));
            float Depth(AreaPlan p) => p.Terraform.Where(t => float.IsNaN(t.WaterLevel)).Max(t => -t.TargetHeight);
            Assert.True(Depth(plans["openPit"]) > Depth(plans["gravelPit"]) + 5f);
            Assert.True(Depth(plans["gravelPit"]) > Depth(plans["sandPit"]));
            Assert.True(plans["gravelPit"].Objects.Count(o => o.Category == AssetCategories.ConstructionPile) > plans["openPit"].Objects.Count(o => o.Category == AssetCategories.ConstructionPile));
            Assert.Contains(plans["openPit"].Objects, o => o.Category == AssetCategories.QuarryPlant);
            Assert.True(plans["abandoned"].Objects.Count(o => o.Role == ObjectRole.Tree) > plans["flooded"].Objects.Count(o => o.Role == ObjectRole.Tree) / 2);
            Assert.DoesNotContain(plans["abandoned"].Terraform, t => !float.IsNaN(t.WaterLevel));
            Assert.Contains(plans["lakeDig"].Terraform, t => !float.IsNaN(t.WaterLevel));
            // Fingerprints all differ.
            Assert.Equal(plans.Count, plans.Values.Select(TestKit.Fingerprint).Distinct().Count());
        }

        [Fact]
        public void NarrowAreasDegradeGracefully()
        {
            var ring = TestKit.Rect(220, 12);   // too narrow for a pit inside a 14 m rim
            var layer = Plan("openPit", ring);
            Assert.Contains(layer.Issues, i => i.Key == "Placemaker.Quarry.Issue.NoRoomForPit" && i.Severity == IssueSeverity.Info);
            var few = Plan("openPit", TestKit.Rect(110, 90), null, ("levels", 6.0));
            Assert.Contains(few.Issues, i => i.Key == "Placemaker.Quarry.Issue.FewerBenches");
        }

        [Fact]
        public void EveryFeatureComposesAndKeepsOutOfThePit()
        {
            var features = Features.Of(GeneratorRegistry.Get(Id)).Select(f => f.Layer).ToArray();
            var ring = TestKit.Rect(260, 220);
            var plan = K.Compose(K.WithPreset(Id, Preset("gravelPit"), features), ring, K.Road(ring));
            Assert.True(string.IsNullOrEmpty(K.Errors(plan)), K.Errors(plan));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            foreach (var l in plan.Layers) K.AssertInside(l, ring, 1.2f);
            var pit = Poly.To2D(plan.Layers[0].Regions.Single(r => r.Key == "place:pit").Ring);
            foreach (var l in plan.Layers.Skip(1))
                foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal))
                    Assert.False(Poly.Contains(pit, o.Position.xz) && Poly.DistanceToBoundary(pit, o.Position.xz) > 1.5f, l.GeneratorId + " placed in the pit");
        }

        [Fact]
        public void EveryKeyHasEnglishTextAndTheRecipeComposes()
        {
            var missing = K.MissingKeys(GeneratorRegistry.Get(Id), "Core/Layers/Types/Quarry");
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing));
            var recipe = TestKit.LoadRecipe("quarry.json");
            Assert.Equal(Id, recipe.Id);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey));
            var plan = Composer.Compose(recipe, new AreaShape { Ring = TestKit.Rect(220, 160) }, TestKit.RoadAlongBottom(220), CancellationToken.None);
            Assert.False(plan.HasErrors, K.Errors(plan));
        }
    }
}
