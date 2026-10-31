// Type wave: urbanRuins ("skate park with broken-down highway") and the dirt-jumps feature.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class UrbanRuinsTests
    {
        private const string Id = "urbanRuins";
        public static IEnumerable<object[]> Cases() => RuinsChecks.PresetCases(Id);

        [Fact]
        public void IsASportsTypeWithFiveSubtypesAndFeatures()
        {
            var gen = TestKit.Gen(Id);
            Assert.Equal("sports", GeneratorGroups.Of(gen));
            Assert.InRange(gen.Presets.Count, 4, 6);
            Assert.InRange(gen.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 40);   // (Phase 1) look settings not counted
            var subtype = gen.Controls.Single(c => c.Id == "subtype");
            Assert.Equal(gen.Presets.Select(p => p.Id), subtype.Options.Select(o => o.Value));
            foreach (var p in gen.Presets) Assert.Equal(p.Id, p.Values.S("subtype", ""));
            Assert.InRange(gen.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Distinct().Count(g => g != "entrances" && g != "shops"), 4, 8);   // (Phase 1) look settings not counted
            Assert.Contains(Features.Of(gen), f => f.Layer == "dirtJumps");
            Assert.True(Phase1Looks.Offers(gen, "debris"));   // (Phase 1) a look setting now (lookDebris)
            Assert.Contains(Features.Of(gen), f => f.Layer == "restrooms");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryPresetPlansSoundOnLShapes(string preset, int hectares)
        {
            var ring = hectares == 1 ? RuinsChecks.L1 : RuinsChecks.L6;
            var plan = RuinsChecks.PlanPreset(Id, preset, ring);
            RuinsChecks.AssertSound(plan, ring, preset + "/" + hectares + "ha");
            Assert.True(plan.Objects.Count(o => o.Role != ObjectRole.Decal) > 15 * hectares, preset + " placed only " + plan.Objects.Count);
            Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryPresetIsDeterministic(string preset, int hectares)
        {
            var ring = hectares == 1 ? RuinsChecks.L1 : RuinsChecks.L6;
            Assert.Equal(RuinsChecks.Fingerprint(RuinsChecks.PlanPreset(Id, preset, ring)), RuinsChecks.Fingerprint(RuinsChecks.PlanPreset(Id, preset, ring)));
        }

        [Theory]
        [InlineData("diySpot")]
        [InlineData("highwayRuins")]
        [InlineData("graffitiPark")]
        [InlineData("parkour")]
        [InlineData("dirtJumps")]
        public void RespectsReservedSpace(string preset)
        {
            var ring = RuinsChecks.L6;
            var reserved = RuinsChecks.SomeReserved(ring);
            var plan = RuinsChecks.PlanPreset(Id, preset, ring, 3, reserved);
            RuinsChecks.AssertSound(plan, ring, preset);
            RuinsChecks.AssertClearOfReserved(plan, reserved, preset);
        }

        [Fact]
        public void SubtypesAreGenuinelyDifferent()
        {
            var ring = RuinsChecks.L6;
            var p = TestKit.Gen(Id).Presets.ToDictionary(x => x.Id, x => RuinsChecks.PlanPreset(Id, x.Id, ring));
            double F(string preset, string fact) => RuinsChecks.Fact(p[preset], fact);
            // DIY spot: pads under standing spans with skate lines, ledges and a bowl.
            Assert.True(F("diySpot", "pads") >= 1);
            Assert.True(F("diySpot", "skate") >= 6);
            Assert.True(F("diySpot", "ledges") >= 2);
            Assert.Equal(1, F("diySpot", "bowl"));
            Assert.Contains(p["diySpot"].Objects, o => o.Category == AssetCategories.SportsSkate);
            // Broken highway: most spans down, fallen slabs, on-ramp terraform, markings on the fallen carriageway.
            Assert.True(F("highwayRuins", "collapsedSpans") > F("diySpot", "collapsedSpans"));
            Assert.True(F("highwayRuins", "fallenSlabs") > 0);
            Assert.True(F("highwayRuins", "markings") > 0);
            Assert.Contains(p["highwayRuins"].Terraform, t => t.Kind == TerraformKind.Raise);
            // Graffiti park: walls and the most graffiti. Parkour: a block field on gravel. Dirt jumps: jump lines (terraform).
            Assert.True(F("graffitiPark", "walls") >= 4);
            Assert.True(F("graffitiPark", "graffiti") > F("parkour", "graffiti"));
            Assert.True(F("parkour", "blocks") >= 12);
            Assert.Equal(0, F("parkour", "pillars"));
            Assert.Contains(p["parkour"].Surfaces, s => s.Category == AssetCategories.SurfaceNatural);
            Assert.True(F("dirtJumps", "jumps") >= 4);
            Assert.True(p["dirtJumps"].Terraform.Count >= 8);
            Assert.True(p["dirtJumps"].Paint.Count > 0);
            // Pillars stand along the highway; beside fallen spans they are stumps.
            Assert.True(F("highwayRuins", "pillars") >= 4);
            Assert.Contains(p["highwayRuins"].Objects, o => o.Category == AssetCategories.RuinsPillar && o.Scale < 0.9f);
            var prints = p.Values.Select(RuinsChecks.Fingerprint).ToList();
            Assert.Equal(prints.Count, prints.Distinct().Count());
        }

        [Fact]
        public void SkateLinesFaceInwardAtThePadEnds()
        {
            var plan = RuinsChecks.PlanPreset(Id, "diySpot", RuinsChecks.FiveHa);
            var ramps = plan.Objects.Where(o => o.Category == AssetCategories.SportsSkate).ToList();
            Assert.True(ramps.Count >= 4);
            // Ramps sit on the pads (paving surfaces), never on the gravel or grass.
            var pads = plan.Surfaces.Where(s => s.Category == AssetCategories.SurfacePaving).Select(s => Poly.To2D(s.Ring)).ToList();
            foreach (var r in ramps) Assert.Contains(pads, pad => Poly.Contains(pad, r.Position.xz));
        }

        [Fact]
        public void DirtJumpsFeatureShapesBlendedMounds()
        {
            var gen = TestKit.Gen("dirtJumps");
            foreach (var preset in gen.Presets)
            {
                var input = TestKit.Input(RuinsChecks.L6, preset.Values.Clone(), 4, null, RuinsChecks.Site());
                input.RegionTags = new[] { RegionTags.Green };
                var plan = gen.Plan(input, System.Threading.CancellationToken.None);
                RuinsChecks.AssertSound(plan, RuinsChecks.L6, "dirtJumps/" + preset.Id);
                Assert.True(RuinsChecks.Fact(plan, "jumps") >= 2, preset.Id);
                Assert.All(plan.Terraform, t => Assert.Equal(TerraformKind.Raise, t.Kind));
            }
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var gen = TestKit.Gen(Id);
            var features = Features.Of(gen).Select(f => f.Layer).ToArray();
            var plan = RuinsChecks.Compose(RuinsChecks.With(Id, features), RuinsChecks.FiveHa);
            Assert.True(RuinsChecks.Errors(plan.AllIssues()).Length == 0, RuinsChecks.Errors(plan.AllIssues()));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            foreach (var l in plan.Layers.Where(l => l.GeneratorId == "debris" || l.GeneratorId == "dirtJumps"))
                RuinsChecks.AssertSound(l, RuinsChecks.FiveHa, l.GeneratorId);
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.SportsSkate] = 0, [AssetCategories.RuinsPillar] = 0, [AssetCategories.RuinsSlab] = 0, [AssetCategories.RuinsWall] = 0,
                [AssetCategories.RuinsRubble] = 0, [AssetCategories.Rock] = 0, [AssetCategories.RuinsWeeds] = 0, [AssetCategories.DecalGraffiti] = 0,
                [AssetCategories.DecalMarkings] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "diySpot", "highwayRuins", "parkour", "graffitiPark" })
                {
                    var plan = RuinsChecks.PlanPreset(Id, preset, RuinsChecks.L6);
                    RuinsChecks.AssertSound(plan, RuinsChecks.L6, preset);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.True(plan.Objects.Count > 20, "still builds benches, lamps and overgrowth");
                }
        }

        [Fact]
        public void NarrowAreasSkipTheHighwayGracefully()
        {
            var strip = TestKit.Rect(40, 30);
            foreach (var p in TestKit.Gen(Id).Presets)
            {
                var plan = RuinsChecks.PlanPreset(Id, p.Id, strip);
                RuinsChecks.AssertSound(plan, strip, p.Id);
            }
        }

        [Fact]
        public void EveryKeyHasEnglishTextAndTheRecipeShips()
        {
            RuinsChecks.AssertLang(Id, "Core/Layers/Types/UrbanRuins");
            RuinsChecks.AssertLang("dirtJumps");
            RuinsChecks.AssertRecipe(Id);
        }
    }
}
