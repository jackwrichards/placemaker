// Type wave: construction site (industrial) — presets, structure, terrain, safety lines, features, lang.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
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
    public class ConstructionSiteTests
    {
        private const string Id = "constructionSite";
        private readonly ITestOutputHelper _out;
        public ConstructionSiteTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => GeneratorRegistry.Get(Id).Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string id) => GeneratorRegistry.Get(Id).Presets.Single(p => p.Id == id);

        [Fact]
        public void SchemaIsDeepButTidy()
        {
            var g = GeneratorRegistry.Get(Id);
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id)), 15, 40);
            Assert.InRange(g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id)).Select(c => c.Group).Distinct().Count(), 4, 9);
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.Contains(g.Controls, c => c.Id == "subtype" && c.Kind == ControlKind.Choice && c.Tier == ControlTier.Essential);
            Assert.Equal("industrial", GeneratorGroups.Of(g));
            // Presets cover every subtype.
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
                site.HeightAt = p => 0.03f * p.x + 0.01f * p.y;   // gentle slope
                var plan = K.Compose(K.WithPreset(Id, Preset(preset)), ring, site);
                Assert.True(string.IsNullOrEmpty(K.Errors(plan)), preset + "/" + name + ": " + K.Errors(plan));
                var layer = plan.Layers[0];
                Assert.True(layer.Objects.Count(o => o.Role != ObjectRole.Decal) >= 12, $"{preset}/{name}: only {layer.Objects.Count} objects");
                K.AssertInside(layer, ring);
                K.AssertNothingOnPaths(layer);
                TestKit.AssertSurfacesAccepted(layer);
                K.AssertBlended(layer, ring, site, 80f);
                Assert.Contains(layer.Regions, r => r.Tags.Contains(RegionTags.Core));
                _out.WriteLine($"{preset}/{name}: {layer.Objects.Count} objects, {layer.Terraform.Count} terraform, {layer.Surfaces.Count} surfaces, {layer.Paint.Count} paint");
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministicAndFast(string preset)
        {
            var ring = TestKit.Rect(250, 200);
            var a = K.Compose(K.WithPreset(Id, Preset(preset)), ring, K.Road(ring));
            var sw = Stopwatch.StartNew();
            var b = K.Compose(K.WithPreset(Id, Preset(preset)), ring, K.Road(ring));
            sw.Stop();
            Assert.Equal(TestKit.Fingerprint(a.Layers[0]), TestKit.Fingerprint(b.Layers[0]));
            _out.WriteLine($"{preset}: {sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{preset} took {sw.ElapsedMilliseconds} ms for 5 ha");
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void RespectsReservedSpace(string preset)
        {
            var ring = TestKit.Rect(160, 120);
            var g = GeneratorRegistry.Get(Id);
            var input = TestKit.Input(ring, Preset(preset).Values.Clone(), 7, null, K.Road(ring));
            input.Reserved.AddDisc(new float2(80, 60), 9f, "Furniture");
            input.Reserved.AddCorridor(new float2(20, 100), new float2(140, 100), 2f, "Pathway");
            var plan = g.Plan(input, CancellationToken.None);
            Assert.False(plan.HasErrors);
            K.AssertClearOfReserved(plan, input.Reserved);
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(GeneratorRegistry.Get(Id)).Select(f => f.Layer).ToArray();
            Assert.Contains("spoilHeaps", features);
            Assert.Contains("workLights", features);
            var ring = TestKit.LShape(250);
            var plan = K.Compose(K.WithPreset(Id, Preset("earthworks"), features), ring, K.Road(ring));
            Assert.True(string.IsNullOrEmpty(K.Errors(plan)), K.Errors(plan));
            foreach (var l in plan.Layers) K.AssertInside(l, ring, 1.2f);
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            // Features keep out of the works (a reserved place).
            var works = plan.Layers[0].Regions.FirstOrDefault(r => r.Key == "place:works");
            Assert.NotNull(works);
            var wr = Poly.To2D(works.Ring);
            foreach (var l in plan.Layers.Skip(1))
                foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal))
                    Assert.False(Poly.Contains(wr, o.Position.xz) && Poly.DistanceToBoundary(wr, o.Position.xz) > 1.5f, l.GeneratorId + " placed in the works");
        }

        [Fact]
        public void SubtypesAreVisiblyDifferent()
        {
            var ring = TestKit.Rect(220, 160);
            AreaPlan P(string preset) => K.Compose(K.WithPreset(Id, Preset(preset)), ring, K.Road(ring)).Layers[0];
            var foundation = P("foundation");
            var roadworks = P("roadworks");
            var compound = P("compound");
            var demolition = P("demolition");
            var earthworks = P("earthworks");
            // Foundation: a lowered dig ringed by barriers, a crane.
            Assert.Contains(foundation.Terraform, t => t.Kind == TerraformKind.Lower);
            Assert.True(foundation.Objects.Count(o => o.Category == AssetCategories.ConstructionBarrier) >= 20);
            Assert.Contains(foundation.Objects, o => o.Category == AssetCategories.ConstructionCrane);
            // Road works: long coned strips with asphalt/gravel sections and signs.
            Assert.True(roadworks.Objects.Count(o => o.Category == AssetCategories.ConstructionCone) >= 40);
            Assert.True(roadworks.Surfaces.Select(s => s.Category).Distinct().Count() >= 2);
            Assert.Contains(roadworks.Objects, o => o.Category == AssetCategories.ConstructionSign);
            // Compound: many cabins, containers and stacks, no dig.
            Assert.True(compound.Objects.Count(o => o.Category == AssetCategories.ConstructionCabin) >= 6);
            Assert.True(compound.Objects.Count(o => o.Category == AssetCategories.ConstructionMaterial) >= 20);
            Assert.Contains(compound.Objects, o => o.Category == AssetCategories.ConstructionContainer);
            // Demolition: raised rubble mounds covered in rubble, skips.
            Assert.Contains(demolition.Terraform, t => t.Kind == TerraformKind.Raise);
            Assert.True(demolition.Objects.Count(o => o.Category == AssetCategories.ConstructionRubble) >= 20);
            Assert.Contains(demolition.Objects, o => o.Category == AssetCategories.ConstructionSkip);
            // Earthworks: both cut and fill, and a haul loop.
            Assert.Contains(earthworks.Terraform, t => t.Kind == TerraformKind.Lower);
            Assert.Contains(earthworks.Terraform, t => t.Kind == TerraformKind.Raise);
            Assert.True(earthworks.Objects.Count(o => o.Category == AssetCategories.ConstructionMachine) >= 4);
            // Everyone: mud paint and a fence with a gate (issue #2: no decals from decal packs any more).
            foreach (var p in new[] { foundation, roadworks, compound, demolition, earthworks })
            {
                Assert.NotEmpty(p.Paint);
                Assert.Contains(p.Nets, n => n.Role == NetRole.Fence);
            }
        }

        [Fact]
        public void FoundationPitHasARampAndTheRequestedDepth()
        {
            var ring = TestKit.Rect(200, 150);
            var plan = K.Compose(K.WithPreset(Id, Preset("foundation")), ring, K.Road(ring)).Layers[0];
            float deepest = plan.Terraform.Min(t => t.TargetHeight);
            Assert.InRange(deepest, -6.01f, -5f);
            // The ramp: cells at intermediate depths.
            Assert.Contains(plan.Terraform, t => t.TargetHeight < -1f && t.TargetHeight > -5f);
            Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey);
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.ConstructionMachine] = 0, [AssetCategories.ConstructionCrane] = 0, [AssetCategories.ConstructionCabin] = 0,
                [AssetCategories.GardenShed] = 0, [AssetCategories.Shelter] = 0, [AssetCategories.ConstructionCone] = 0, [AssetCategories.Bollard] = 0,
                [AssetCategories.ConstructionScaffold] = 0, [AssetCategories.ConstructionMaterial] = 0, [AssetCategories.QuarryPlant] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "foundation", "roadworks", "compound" })
                {
                    var ring = TestKit.Rect(200, 150);
                    var plan = K.Compose(K.WithPreset(Id, Preset(preset)), ring, K.Road(ring));
                    Assert.True(string.IsNullOrEmpty(K.Errors(plan)), K.Errors(plan));
                    Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    // (rundown) machines are Placemaker's own vehicle props (made by the game layer, never an installed asset): they stay
                    Assert.DoesNotContain(plan.Layers[0].Objects, o => none.ContainsKey(o.Category ?? "") && !Placemaker.Core.Build.AutoAssets.IsOwn(o.Asset.Id));
                    Assert.NotEmpty(plan.Layers[0].Objects);
                }
        }

        [Fact]
        public void EveryKeyHasEnglishTextAndTheRecipeComposes()
        {
            var missing = K.MissingKeys(GeneratorRegistry.Get(Id), "Core/Layers/Types/ConstructionSite");
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing));
            var recipe = TestKit.LoadRecipe("constructionSite.json");
            Assert.Equal(Id, recipe.Id);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey));
            var plan = Composer.Compose(recipe, new AreaShape { Ring = TestKit.Rect(220, 160) }, TestKit.RoadAlongBottom(220), CancellationToken.None);
            Assert.False(plan.HasErrors, K.Errors(plan));
        }

        [Theory]
        [InlineData("TrafficCone01", AssetCategories.ConstructionCone)]
        [InlineData("ConstructionBarrier02", AssetCategories.ConstructionBarrier)]
        [InlineData("Excavator01", AssetCategories.ConstructionMachine)]
        [InlineData("TowerCrane01", AssetCategories.ConstructionCrane)]
        [InlineData("PortableToilet01", AssetCategories.ConstructionToilet)]
        [InlineData("SiteOffice01", AssetCategories.ConstructionCabin)]
        [InlineData("PalletStack03", AssetCategories.ConstructionMaterial)]
        [InlineData("GravelPile01", AssetCategories.ConstructionPile)]
        [InlineData("RubblePile02", AssetCategories.ConstructionRubble)]
        [InlineData("Dumpster01", AssetCategories.ConstructionSkip)]
        [InlineData("Scaffolding01", AssetCategories.ConstructionScaffold)]
        [InlineData("RoadworkSign01", AssetCategories.ConstructionSign)]
        [InlineData("StoneCrusher01", AssetCategories.QuarryPlant)]
        public void ConstructionClassification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(category, AssetHeuristics.Classify(name, t));
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name + " should be automatic for " + category);
        }

        [Fact]
        public void ClassificationKeepsOtherCategoriesAndExclusions()
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.DoesNotContain(AssetCategories.ConstructionCone, AssetHeuristics.Classify("PineCone01", t));
            Assert.DoesNotContain(AssetCategories.ConstructionCrane, AssetHeuristics.Classify("HarborCrane01", t));
            Assert.DoesNotContain(AssetCategories.ConstructionPile, AssetHeuristics.Classify("Pillow01", t));
            // Rules only add: a barrier is still a fence for the general classifier.
            Assert.Contains(AssetCategories.Fence, AssetHeuristics.Classify("ConstructionBarrier02", t));
        }
    }
}
