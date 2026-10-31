// Type wave: landfill (industrial). Same invariants as the scrapyard (YardTestKit) plus the mound:
// the active tip has a real terraformed mound with a working face, the capped hill a summit path,
// the compost yard windrows, the recycling yard a skip row, the fly-tip garbage without a gatehouse.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class LandfillTests
    {
        private readonly ITestOutputHelper _out;
        public LandfillTests(ITestOutputHelper output) { _out = output; }

        private const string Id = "landfill";
        private const float Slope = 34f;
        public static IEnumerable<object[]> PresetIds() => TestKit.Gen(Id).Presets.Select(p => new object[] { p.Id });

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectare(string preset)
        {
            var plan = YardTestKit.PlanPreset(Id, preset, YardTestKit.OneHa);
            YardTestKit.AssertSound(plan, YardTestKit.OneHa, Slope);
            Assert.True(plan.Objects.Count(o => o.Role != ObjectRole.Decal) > 20, preset + " placed only " + plan.Objects.Count + " objects on 1 ha");
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansSixHectareLShape(string preset)
        {
            var plan = YardTestKit.PlanPreset(Id, preset, YardTestKit.SixHaL);
            YardTestKit.AssertSound(plan, YardTestKit.SixHaL, Slope);
            Assert.True(plan.Objects.Count(o => o.Role != ObjectRole.Decal) > 80, preset + " placed only " + plan.Objects.Count + " objects on 6 ha");
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministic(string preset)
        {
            var a = YardTestKit.PlanPreset(Id, preset, YardTestKit.SixHaL);
            var b = YardTestKit.PlanPreset(Id, preset, YardTestKit.SixHaL);
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            Assert.Equal(a.Terraform.Count, b.Terraform.Count);
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReserved(string preset)
        {
            var reserved = YardTestKit.Reserved(100, 100);
            var plan = YardTestKit.PlanPreset(Id, preset, YardTestKit.OneHa, null, reserved);
            Assert.False(plan.HasErrors);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsFastOnFiveHectares(string preset)
        {
            YardTestKit.PlanPreset(Id, preset, YardTestKit.FiveHa);   // JIT
            var sw = Stopwatch.StartNew();
            YardTestKit.PlanPreset(Id, preset, YardTestKit.FiveHa);
            sw.Stop();
            _out.WriteLine($"{preset}: {sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{preset} took {sw.ElapsedMilliseconds} ms for 5 ha");
        }

        [Fact]
        public void MoundOnASlopeIsBlendedAtTheOutline()
        {
            var site = TestKit.RoadAlongBottom(250);
            site.HeightAt = p => 0.06f * p.x - 0.04f * p.y;
            foreach (var preset in new[] { "active", "capped", "compost" })
            {
                var plan = YardTestKit.PlanPreset(Id, preset, YardTestKit.FiveHa, site);
                Assert.NotEmpty(plan.Terraform);
                YardTestKit.AssertSound(plan, YardTestKit.FiveHa, Slope, site.HeightAt);
            }
        }

        [Fact]
        public void IrregularOutlineStillPlans()
        {
            var ring = TestKit.Blob(110, 40, 3);
            foreach (var p in TestKit.Gen(Id).Presets)
            {
                var plan = YardTestKit.PlanPreset(Id, p.Id, ring, new SiteContext());
                YardTestKit.AssertSound(plan, ring, Slope);
            }
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var gen = TestKit.Gen(Id);
            var features = Phase1Looks.AllFeatures(gen);   // (Phase 1) add-ons and look settings
            Assert.Contains("skipRow", features);
            var plan = YardTestKit.Compose(YardTestKit.With(Id, features), YardTestKit.FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(YardTestKit.Errors(plan.AllIssues())), YardTestKit.Errors(plan.AllIssues()));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            Assert.Contains(plan.Layers.Single(l => l.GeneratorId == "skipRow").Objects, o => o.Category == AssetCategories.LandfillSkip);
        }

        [Fact]
        public void SchemaKeysAndRecipe()
        {
            YardTestKit.AssertSchemaShape(Id, "industrial");
            YardTestKit.AssertKeys(Id, new[] { "Landfill" });
            YardTestKit.AssertKeys("skipRow", new[] { "Landfill" });
        }

        [Fact]
        public void SubtypesAreVisiblyDifferent()
        {
            AreaPlan P(string preset) => YardTestKit.PlanPreset(Id, preset, YardTestKit.FiveHa);

            var active = P("active");
            Assert.True(active.Facts["terrainMaxRaise"] > 6, "active: a real mound");
            Assert.True(YardTestKit.Count(active, AssetCategories.LandfillGarbage) > 60, "active: garbage on the working face");
            Assert.True(YardTestKit.Count(active, AssetCategories.ScrapMachine) >= 2, "active: compactors");
            Assert.True(YardTestKit.Count(active, AssetCategories.LandfillVent) >= 5, "active: gas wells on the cap");
            Assert.True(active.Nets.Count(n => n.Role == NetRole.Fence) > 4, "active: fence and litter screens");
            Assert.Equal(1.0, active.Facts["lagoon"]);
            // The face is lower than the rest of the mound at the same distance from the centre.
            Assert.True(active.Terraform.Any(t => t.Kind == TerraformKind.Raise));

            var capped = P("capped");
            Assert.True(capped.Facts["terrainMaxRaise"] > 6, "capped: the hill");
            Assert.Equal(0, YardTestKit.Count(capped, AssetCategories.LandfillGarbage));
            Assert.True(capped.Nets.Count(n => n.Role == NetRole.Pathway) >= 10, "capped: summit path");
            Assert.True(TestKit.Count(capped, ObjectRole.Bench) >= 3, "capped: summit benches");
            Assert.True(TestKit.Count(capped, ObjectRole.Bush) > 40, "capped: scrub on the cap");
            Assert.DoesNotContain(capped.Nets, n => n.Role == NetRole.Fence && n.A.x < 0.5f && n.D.x < 0.5f);   // no perimeter fence on the west edge

            var compost = P("compost");
            Assert.True(TestKit.Stat(compost, "Placemaker.Stat.Windrows") >= 6, "compost: windrows");
            Assert.True(YardTestKit.Count(compost, AssetCategories.LandfillPile) > 80, "compost: piles along the rows");

            var recycling = P("recycling");
            Assert.True(YardTestKit.Count(recycling, AssetCategories.LandfillSkip) >= 10, "recycling: public skip row");
            Assert.True(YardTestKit.Count(recycling, AssetCategories.LandfillBale) > 20, "recycling: bale stacks");
            Assert.True(YardTestKit.Count(recycling, AssetCategories.ScrapBarrier) > 20, "recycling: material bays");

            var flytip = P("flytip");
            Assert.True(YardTestKit.Count(flytip, AssetCategories.LandfillGarbage) > 30, "fly-tip: dumps");
            Assert.DoesNotContain(flytip.Objects, o => o.Category == AssetCategories.ScrapCabin);
            Assert.True(TestKit.Count(flytip, ObjectRole.Bush) > TestKit.Count(recycling, ObjectRole.Bush), "fly-tip: scrubby");

            foreach (var p in new[] { active, compost, recycling, flytip })
            {
                Assert.True(TestKit.Count(p, ObjectRole.Decal) > 80, p.GeneratorId + " grime");
                Assert.NotEmpty(p.Paint);
            }
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>();
            foreach (var c in new[] { AssetCategories.LandfillGarbage, AssetCategories.LandfillSkip, AssetCategories.LandfillPile, AssetCategories.LandfillBale,
                         AssetCategories.LandfillVent, AssetCategories.ScrapMachine, AssetCategories.ScrapHeap, AssetCategories.ScrapContainer,
                         AssetCategories.ScrapCabin, AssetCategories.Rock })
                none[c] = 0;
            using (AssetAvailability.Scope(none))
                foreach (var p in TestKit.Gen(Id).Presets)
                {
                    var plan = YardTestKit.PlanPreset(Id, p.Id, YardTestKit.OneHa);
                    Assert.False(plan.HasErrors, p.Id);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    TestKit.AssertSurfacesAccepted(plan);
                    if (p.Id != "capped") Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                }
        }

        [Fact]
        public void TinySiteFallsBackWithoutAMound()
        {
            var ring = TestKit.Rect(60, 30);
            var plan = YardTestKit.PlanPreset(Id, "active", ring, new SiteContext());
            YardTestKit.AssertSound(plan, ring, Slope);
            Assert.Contains(plan.Issues, i => i.Key == Placemaker.Core.Layers.Types.Landfill.LandfillLayer.MoundTooSmallKey);
        }

        [Theory]
        [InlineData("GarbageBags01", AssetCategories.LandfillGarbage)]
        [InlineData("Trash Pile", AssetCategories.LandfillGarbage)]
        [InlineData("Old Mattress", AssetCategories.LandfillGarbage)]
        [InlineData("Dumpster01", AssetCategories.LandfillSkip)]
        [InlineData("Skip Container Yellow", AssetCategories.LandfillSkip)]
        [InlineData("RecyclingContainer01", AssetCategories.LandfillSkip)]
        [InlineData("Bottle Bank Green", AssetCategories.LandfillSkip)]
        [InlineData("SoilPile01", AssetCategories.LandfillPile)]
        [InlineData("Gravel Heap", AssetCategories.LandfillPile)]
        [InlineData("CardboardBale01", AssetCategories.LandfillBale)]
        [InlineData("HayBale02", AssetCategories.LandfillBale)]
        [InlineData("Landfill Gas Well", AssetCategories.LandfillVent)]
        [InlineData("VentPipe01", AssetCategories.LandfillVent)]
        public void NameRulesClassify(string name, string expected)
        {
            var cats = AssetHeuristics.Classify(name, AssetTraits.StaticObject | AssetTraits.HasMesh);
            Assert.Contains(expected, cats);
            Assert.True(AssetHeuristics.AutoEligible(expected, name, AssetTraits.StaticObject | AssetTraits.HasMesh), name);
        }

        [Theory]
        [InlineData("TrashCan01")]
        [InlineData("Litter Bin")]
        [InlineData("EventStage01")]
        [InlineData("CompostBin01")]
        public void NameRulesLeaveOtherPoolsAlone(string name)
        {
            var cats = AssetHeuristics.Classify(name, AssetTraits.StaticObject | AssetTraits.HasMesh);
            Assert.DoesNotContain(cats, c => c.StartsWith("industrial.", StringComparison.Ordinal));
        }
    }
}
