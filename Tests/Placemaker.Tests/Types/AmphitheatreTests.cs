// Type wave (events): amphitheatre — presets on 1 ha squares and 6 ha L-shapes, determinism,
// invariants (inside, off paths, clear of Reserved, surfaces accepted, terraform blended to the
// original ground at the outline, also on a slope), speed, features, localisation, and the bowl
// itself (rows step up away from the stage, seats face it, straight cinema rows, a full ring round
// a bandstand, features keep out of the seating).

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class AmphitheatreTests
    {
        private const string Id = "amphitheatre";
        private readonly ITestOutputHelper _out;
        public AmphitheatreTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => GeneratorRegistry.Get(Id).Presets.Select(p => new object[] { p.Id });
        /// <summary>(the plazas makeover) The old bowl's presets; the stone theatre is a designed square (DesignedSquaresTests).</summary>
        public static IEnumerable<object[]> BowlPresetIds() => GeneratorRegistry.Get(Id).Presets.Where(p => p.Id != "theatre").Select(p => new object[] { p.Id });
        private static SettingsBag Preset(string id) => GeneratorRegistry.Get(Id).Presets.Single(p => p.Id == id).Values;

        private static SiteContext Sloped() { var s = TestKit.RoadAlongBottom(300); s.HeightAt = p => 0.03f * p.x + 0.01f * p.y; return s; }

        [Theory]
        [MemberData(nameof(BowlPresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var ring in new[] { EventsKit.OneHa, EventsKit.SixHaL })
                foreach (var site in new[] { TestKit.RoadAlongBottom(300), Sloped() })
                {
                    var plan = EventsKit.Plan(Id, ring, Preset(preset), site);
                    Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                    Assert.True(plan.Objects.Count > 10, preset + " placed only " + plan.Objects.Count + " objects");
                    Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);
                    Assert.True(plan.Facts["rows"] >= 1, preset);
                    EventsKit.AssertInside(plan, ring);
                    EventsKit.AssertNothingOnPaths(plan);
                    TestKit.AssertSurfacesAccepted(plan);
                    EventsKit.AssertTerrainBlended(plan, ring, site);
                    Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
                    Assert.Contains(plan.Regions, r => r.Tags.Contains("place:bowl"));
                    if (Preset(preset).S("terrain", "") != "slope") Assert.NotEmpty(plan.Terraform);
                }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministic(string preset)
        {
            var a = EventsKit.Plan(Id, EventsKit.SixHaL, Preset(preset), Sloped());
            var b = EventsKit.Plan(Id, EventsKit.SixHaL, Preset(preset), Sloped());
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            Assert.Equal(a.Terraform.Count, b.Terraform.Count);
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsFastOnFiveHectares(string preset)
        {
            EventsKit.Plan(Id, EventsKit.FiveHa, Preset(preset), Sloped());   // JIT
            var sw = Stopwatch.StartNew();
            EventsKit.Plan(Id, EventsKit.FiveHa, Preset(preset), Sloped());
            sw.Stop();
            _out.WriteLine($"{preset}: {sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{preset} took {sw.ElapsedMilliseconds} ms for 5 ha");
        }

        [Theory]
        [MemberData(nameof(BowlPresetIds))]
        public void PresetKeepsClearOfReserved(string preset)
        {
            var res = EventsKit.SomeReserved(new float2(170, 130));
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset(preset), null, res);
            Assert.False(plan.HasErrors);
            TestKit.AssertClearOfReserved(plan, res);
            EventsKit.AssertClearOfReservedPolygons(plan, res);
        }

        [Fact]
        public void EveryFeatureComposesAndKeepsOutOfTheBowl()
        {
            var gen = GeneratorRegistry.Get(Id);
            var features = Features.Of(gen).Select(f => f.Layer).ToArray();
            Assert.Contains("lightTowers", features);
            foreach (var preset in new[] { "classic", "bandstand" })
            {
                var plan = EventsKit.Compose(EventsKit.With(Id, Preset(preset), features), EventsKit.FiveHa, TestKit.RoadAlongBottom(250));
                Assert.True(string.IsNullOrEmpty(EventsKit.Errors(plan)), EventsKit.Errors(plan));
                foreach (var l in plan.Layers) EventsKit.AssertInside(l, EventsKit.FiveHa, 1.2f);
                foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
                var bowls = plan.Layers[0].Regions.Where(r => r.Tags.Contains("place:bowl")).Select(r => Poly.To2D(r.Ring)).ToList();
                Assert.NotEmpty(bowls);
                foreach (var l in plan.Layers.Skip(1))
                    foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal))
                        Assert.False(bowls.Any(b => Poly.Contains(b, o.Position.xz)), $"{l.GeneratorId} put {o.Role}/{o.Category} into the seating");
            }
        }

        [Fact]
        public void LabelsSchemaAndRecipe()
        {
            EventsKit.AssertLocalised(Id);
            EventsKit.AssertSchema(Id, "events");
            EventsKit.AssertRecipe("amphitheatre.json", Id);
        }

        [Fact]
        public void DugBowlStepsUpFromTheStageAndSeatsFaceIt()
        {
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("classic"), new SiteContext());
            var stage = plan.Objects.Single(o => o.Category == AssetCategories.Stage);
            var bowl = plan.Regions.Where(r => r.Tags.Contains("place:bowl")).Select(r => Poly.To2D(r.Ring)).ToList();
            var benches = plan.Objects.Where(o => o.Role == ObjectRole.Bench && bowl.Any(b => Poly.Contains(b, o.Position.xz))).ToList();
            Assert.True(benches.Count >= 60, "benches " + benches.Count);
            Assert.True(plan.Facts["rows"] >= 8);
            // Lower near the stage, back at ground level at the top; steps never exceed the rise.
            var byDist = benches.OrderBy(b => math.distance(b.Position.xz, stage.Position.xz)).ToList();
            Assert.True(byDist.First().Position.y < -2f, "front row at " + byDist.First().Position.y);
            Assert.True(byDist.Last().Position.y > byDist.First().Position.y + 2f);
            Assert.True(stage.Position.y < byDist.First().Position.y + 0.01f);
            // The bowl was dug (lowered cells) and the pit floor is about rows · rise deep.
            Assert.Contains(plan.Terraform, t => t.Kind == TerraformKind.Lower);
            Assert.InRange(plan.Facts["terrainMaxLower"], 3.5, 5.0);
            // Seats face the focal point: their facing passes close to the stage.
            int facing = 0;
            foreach (var b in benches)
            {
                var f = new float2(math.sin(b.Yaw), math.cos(b.Yaw));
                if (math.dot(f, math.normalize(stage.Position.xz - b.Position.xz)) > 0.97f) facing++;
            }
            Assert.True(facing >= benches.Count * 0.95f, facing + " of " + benches.Count);
        }

        [Fact]
        public void BuiltBowlRisesAndBlendsBackOnASlope()
        {
            var site = Sloped();
            var s = Preset("classic").Clone();
            s.Values["terrain"] = "build";
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, s, site);
            Assert.Contains(plan.Terraform, t => t.Kind == TerraformKind.Raise);
            EventsKit.AssertTerrainBlended(plan, EventsKit.FiveHa, site);
            Assert.True(plan.Facts["bowlHeight"] > 4f);
        }

        [Fact]
        public void AutoPutsTheStageAtTheLowSideOfASlope()
        {
            var site = new SiteContext { HeightAt = p => 0.06f * p.x };   // 15 m across 250 m: rising to +x
            var s = Preset("classic").Clone();
            s.Values["terrain"] = "slope";
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, s, site);
            Assert.Empty(plan.Terraform);
            var stage = plan.Objects.Single(o => o.Category == AssetCategories.Stage);
            var seats = plan.Objects.Where(o => o.Role == ObjectRole.Bench).ToList();
            Assert.True(seats.Average(b => b.Position.x) > stage.Position.x + 10f, "seats should climb the slope");
        }

        [Fact]
        public void CinemaHasStraightRowsFacingTheScreen()
        {
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("cinema"));
            var screen = plan.Objects.Single(o => o.Category == AssetCategories.EventScreen);
            var chairs = plan.Objects.Where(o => o.Category == AssetCategories.EventDeckchair).ToList();
            Assert.True(chairs.Count >= 60, "chairs " + chairs.Count);
            Assert.True(chairs.All(c => math.abs(Placemaker.Core.Layers.Types.Events.EventKit.Wrap(c.Yaw - chairs[0].Yaw)) < 1e-3f), "rows are not straight");
            var f = new float2(math.sin(chairs[0].Yaw), math.cos(chairs[0].Yaw));
            Assert.True(math.dot(f, math.normalize(screen.Position.xz - chairs.Average2())) > 0.95f);
            Assert.Contains(plan.Terraform, t => t.Kind == TerraformKind.Raise);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Kiosk);
        }

        [Fact]
        public void BandstandSitsInTheMiddleOfAFullRing()
        {
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("bandstand"));
            var stand = plan.Objects.Single(o => o.Category == AssetCategories.Stage);
            var chairs = plan.Objects.Where(o => o.Category == AssetCategories.EventDeckchair).ToList();
            Assert.True(chairs.Count > 30);
            var sectors = chairs.Select(c => (int)((math.atan2(c.Position.z - stand.Position.z, c.Position.x - stand.Position.x) + math.PI) / (math.PI / 4f))).Distinct().Count();
            Assert.True(sectors >= 8, "seats cover " + sectors + " of 8 sectors");
            Assert.Equal(2, plan.Regions.Count(r => r.Tags.Contains("place:bowl")));
        }

        [Fact]
        public void MeadowHasBarrierTentAndToiletsAndReportsFewerRowsWhenSmall()
        {
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("meadow"));
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Fence);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.EventTent);
            Assert.True(plan.Objects.Count(o => o.Category == AssetCategories.EventToilet) >= 6);
            Assert.True(plan.Objects.Count(o => o.Category == AssetCategories.EventLightTower) >= 3);
            Assert.True(plan.Stats.Single(s => s.Key == "Placemaker.Stat.Capacity").Value > 1000);
            var small = EventsKit.Plan(Id, EventsKit.OneHa, Preset("meadow"));
            Assert.Contains(small.Issues, i => i.Key == "Placemaker.Amphitheatre.Issue.FewerRows" && i.Severity == IssueSeverity.Info);
        }

        [Fact]
        public void NoRoomFallsBackToALawnStage()
        {
            var thin = TestKit.Rect(120, 9);
            var plan = EventsKit.Plan(Id, thin, Preset("classic"));
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Amphitheatre.Issue.NoRoom");
            EventsKit.AssertInside(plan, thin);
        }

        [Fact]
        public void MissingAssetsDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.Stage] = 0, [AssetCategories.Shelter] = 0, [AssetCategories.EventScreen] = 0, [AssetCategories.EventDeckchair] = 0,
                [AssetCategories.EventToilet] = 0, [AssetCategories.EventTent] = 0, [AssetCategories.EventLightTower] = 0, [AssetCategories.EventFoodTruck] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var p in GeneratorRegistry.Get(Id).Presets)
                {
                    var plan = EventsKit.Plan(Id, EventsKit.FiveHa, p.Values);
                    Assert.False(plan.HasErrors);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.True(plan.Objects.Count > 10, p.Id);
                }
        }
    }

    internal static class Float2Ext
    {
        public static float2 Average2(this IEnumerable<PlannedObject> objs)
        {
            float2 s = 0; int n = 0;
            foreach (var o in objs) { s += o.Position.xz; n++; }
            return n == 0 ? s : s / n;
        }
    }
}
