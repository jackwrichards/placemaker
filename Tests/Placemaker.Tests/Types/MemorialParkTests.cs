// Type wave (builder 9): memorial park. Invariant checks shared with the adventure playground (TypeChecks09).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class MemorialParkTests
    {
        private const string Id = "memorialPark";
        private readonly ITestOutputHelper _out;
        public MemorialParkTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => TestKit.Gen(Id).Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string id) => TestKit.Gen(Id).Presets.Single(p => p.Id == id);

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void EveryPresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var (name, ring, width) in TypeChecks09.Shapes())
            {
                var site = TypeChecks09.SlopedSite(width);
                var plan = TypeChecks09.PlanPreset(Id, Preset(preset), ring, site);
                TypeChecks09.AssertInvariants(plan, ring, site, name == "1ha" ? 15 : 40);
                Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Centerpiece);
                Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void Deterministic(string preset)
        {
            var ring = TestKit.LShape(293f);
            var a = TypeChecks09.PlanPreset(Id, Preset(preset), ring, TypeChecks09.SlopedSite(293));
            var b = TypeChecks09.PlanPreset(Id, Preset(preset), ring, TypeChecks09.SlopedSite(293));
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void RespectsReservedSpace(string preset)
        {
            var ring = TestKit.Rect(180, 120);
            var reserved = TypeChecks09.SomeReserved(new float2(60, 60));
            var plan = TypeChecks09.PlanPreset(Id, Preset(preset), ring, TestKit.RoadAlongBottom(180), 3, reserved);
            Assert.False(plan.HasErrors);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void FastOnFiveHectares() => TypeChecks09.AssertFast(Id, _out);

        [Fact]
        public void FeaturesCompose() => TypeChecks09.AssertFeaturesCompose(Id);

        [Fact]
        public void SchemaIsDeepTidyAndLocalised()
        {
            TypeChecks09.AssertSchema(Id, 5, 41);   // (parks makeover) + places with character
            TypeChecks09.AssertAddOnKeys("flagCircle");
        }

        [Fact]
        public void ShippedRecipeComposes() => TypeChecks09.AssertShippedRecipe(Id);

        [Fact]
        public void AxialCompositionLeadsToTheMonument()
        {
            var ring = TestKit.Rect(240, 120);
            var plan = TypeChecks09.PlanPreset(Id, Preset("reflection"), ring, TestKit.RoadAlongBottom(240));
            var mon = plan.Objects.Single(o => o.Role == ObjectRole.Centerpiece && o.Category == AssetCategories.MemorialMonument).Position.xz;
            // On the long axis (z = 60), towards one end.
            Assert.True(math.abs(mon.y - 60f) < 3f, "monument off the axis: " + mon);
            Assert.True(math.abs(mon.x - 120f) > 50f, "monument not at the end of the axis: " + mon);
            // Reflecting pool on the axis between the entrance end and the monument, walks either side.
            var water = plan.Surfaces.Where(s => s.Role == SurfaceRole.Water).ToList();
            Assert.Single(water);
            var wc = Poly.Centroid(Poly.To2D(water[0].Ring));
            Assert.True(math.abs(wc.y - 60f) < 1f);
            Assert.True(math.abs(wc.x - 120f) < math.abs(mon.x - 120f));
            Assert.True(plan.Nets.Count(n => n.Role == NetRole.Pathway && math.abs(n.A.z - n.D.z) < 0.01f && math.abs(n.A.z - 60f) > 3f) >= 2, "two walks beside the pool");
            // Walk ends are shared nodes (bit-identical), so CS2 joins them.
            var ends = plan.Nets.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            int joined = ends.Count(e => ends.Count(f => f.x == e.x && f.y == e.y) >= 2);
            Assert.True(joined >= 8, "path ends do not meet at shared nodes");
            // Allées come in symmetric pairs.
            Assert.True(plan.Facts["alleeTrees"] >= 8 && plan.Facts["alleeTrees"] % 2 == 0);
            Assert.True(plan.Facts["rooms"] >= 2);
        }

        [Fact]
        public void PresetsLookDifferent()
        {
            var ring = TestKit.Rect(220, 140);
            AreaPlan P(string id) => TypeChecks09.PlanPreset(Id, Preset(id), ring, TestKit.RoadAlongBottom(220));
            int Cat(AreaPlan p, string cat) => p.Objects.Count(o => o.Category == cat);

            var garden = P("garden");
            Assert.True(garden.Facts["rooms"] >= 2);
            Assert.True(Cat(garden, AssetCategories.Flowerbed) >= 12);
            Assert.Empty(garden.Surfaces.Where(s => s.Role == SurfaceRole.Water));

            var square = P("square");
            Assert.True(square.Nets.Count(n => n.Role == NetRole.PathwayWide) >= 3, "axis and cross axis");
            Assert.True(square.Objects.Count(o => o.Role == ObjectRole.Bench) >= 6);
            Assert.Equal(0, (int)square.Facts["rooms"]);

            var war = P("war");
            Assert.NotEmpty(war.Terraform);
            Assert.True(Cat(war, AssetCategories.MemorialFlag) >= 6);
            Assert.True(Cat(war, AssetCategories.MemorialPlaque) >= 6);
            Assert.Contains(war.Surfaces, s => s.Role == SurfaceRole.Water);

            var sculpture = P("sculpture");
            Assert.True(sculpture.Facts["sculptures"] >= 6, "sculptures: " + sculpture.Facts["sculptures"]);
            Assert.False(sculpture.Facts.ContainsKey("axisLength"));
            Assert.True(Cat(sculpture, AssetCategories.MemorialSculpture) >= 6);

            // Solemn lighting: bollard presets light with low lights only.
            var reflection = P("reflection");
            Assert.True(Cat(reflection, AssetCategories.LampLow) >= 8);
            Assert.DoesNotContain(reflection.Objects, o => o.Role == ObjectRole.Lamp && o.Category == null);
        }

        [Fact]
        public void LawnTreesKeepTheVistaOpen()
        {
            var v = Preset("reflection").Values.Clone();
            v.Values["lawnTrees"] = 80.0;
            v.Values["allees"] = false;
            v.Values["rooms"] = false;
            var plan = TestKit.Gen(Id).Plan(TestKit.Input(TestKit.Rect(240, 120), v, 5, null, TestKit.RoadAlongBottom(240)), CancellationToken.None);
            var trees = plan.Objects.Where(o => o.Role == ObjectRole.Tree).ToList();
            Assert.True(trees.Count > 20);
            Assert.DoesNotContain(trees, t => math.abs(t.Position.z - 60f) < 6f);
        }

        [Fact]
        public void MissingMemorialAssetsDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.MemorialMonument] = 0, [AssetCategories.MemorialSculpture] = 0, [AssetCategories.MemorialPlaque] = 0,
                [AssetCategories.MemorialFlag] = 0, [AssetCategories.LampLow] = 0, [AssetCategories.Hedge] = 0, [AssetCategories.SurfaceWater] = 0,
                [AssetCategories.Grave] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var p in TestKit.Gen(Id).Presets)
                {
                    var plan = TypeChecks09.PlanPreset(Id, p, TestKit.Rect(200, 150), TestKit.RoadAlongBottom(200));
                    Assert.False(plan.HasErrors, p.Id);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.DoesNotContain(plan.Surfaces, s => none.ContainsKey(s.Category ?? ""));
                    // Fallbacks keep the composition: a statue/centrepiece, plain lamps, bushes for hedges.
                    Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Centerpiece);
                }
        }

        [Theory]
        [InlineData("WarMemorial01", AssetCategories.MemorialMonument)]
        [InlineData("Obelisk02", AssetCategories.MemorialMonument)]
        [InlineData("AbstractSculpture01", AssetCategories.MemorialSculpture)]
        [InlineData("StatueOfTheMayor", AssetCategories.MemorialSculpture)]
        [InlineData("MemorialPlaque01", AssetCategories.MemorialPlaque)]
        [InlineData("Wreath01", AssetCategories.MemorialPlaque)]
        [InlineData("FlagPole01", AssetCategories.MemorialFlag)]
        [InlineData("BollardLight01", AssetCategories.LampLow)]
        public void MemorialClassification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            var cats = AssetHeuristics.Classify(name, t);
            Assert.Contains(category, cats);
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name);
            if (category == AssetCategories.MemorialMonument || category == AssetCategories.MemorialSculpture)
            {
                Assert.Contains(AssetCategories.Centerpiece, cats);
                Assert.Contains(AssetCategories.CenterpieceStatue, cats);
            }
            if (category == AssetCategories.LampLow) Assert.Contains(AssetCategories.Lamp, cats);
        }

        [Fact]
        public void GravesAndFountainsKeepTheirCategories()
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(AssetCategories.Grave, AssetHeuristics.Classify("MemorialStone01", t));
            Assert.Contains(AssetCategories.CenterpieceFountain, AssetHeuristics.Classify("MemorialFountain01", t));
            Assert.DoesNotContain(AssetCategories.MemorialMonument, AssetHeuristics.Classify("MonumentSign01", t | AssetTraits.HasSubObjects));
        }
    }
}
