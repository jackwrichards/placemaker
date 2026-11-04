// Lookout hill type (builder t04): landform, climbing path, summit, variants, degradation, classification.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types.T04
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class LookoutHillTests : T04TypeTestBase
    {
        public LookoutHillTests(ITestOutputHelper output) : base(output) { }
        protected override string LayerId => "lookoutHill";
        protected override string ExpectedGroup => "nature";

        private AreaPlan PlanPreset(string preset, float3[] ring, SiteContext site, params (string k, object v)[] extra)
        {
            var r = WithPreset(preset);
            foreach (var (k, v) in extra) r.Root.Settings.Values[k] = v;
            var plan = Compose(r, ring, site);
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            return Layer(plan);
        }

        private static float2 Summit(AreaPlan plan) => new float2((float)plan.Facts["summitX"], (float)plan.Facts["summitZ"]);

        [Fact]
        public void HillRisesToItsHeightAndBlendsAtTheOutline()
        {
            var site = new SiteContext { HeightAt = p => 0.02f * p.x };
            var plan = PlanPreset("hilltop", SixHaL, site);
            Out.WriteLine($"peak {plan.Facts["peak"]:0.0} m, {plan.Terraform.Count} cells");
            Assert.True(plan.Facts["peak"] >= 18f * 0.8f, "peak " + plan.Facts["peak"]);
            Assert.All(plan.Terraform, t => Assert.Equal(TerraformKind.Raise, t.Kind));
            // (rf-nature) One continuous slope: sloped patches sharing their corners, never a staircase of levelled cells.
            Assert.All(plan.Terraform, t => Assert.True(t.Sloped));
            // The summit is the highest planned point and lies well inside.
            var summit = Summit(plan);
            var top = plan.Terraform.OrderByDescending(t => t.TargetHeight - site.HeightAt(Poly.Centroid(Poly.To2D(t.Ring)))).First();
            Assert.True(math.distance(Poly.Centroid(Poly.To2D(top.Ring)), summit) < 40f);
            AssertTerraformBlended(plan, SixHaL, site.HeightAt);
            // Everything stands on the raised ground.
            // No vanilla lookout tower (the pool is 32-56 m buildings): the "tower" is the tall open gazebo.
            var tower = plan.Objects.Single(o => o.Category == AssetCategories.Shelter && (o.Asset.Fallbacks ?? new string[0]).Contains("Gazebo03"));
            Assert.True(tower.Position.y - site.HeightAt(tower.Position.xz) > 10f);
        }

        [Theory]
        [InlineData("spiral")]
        [InlineData("switchback")]
        public void PathClimbsToTheSummitAtAWalkableGradeWithoutCrossingItself(string style)
        {
            var site = new SiteContext();
            var plan = PlanPreset("hilltop", SixHaL, site, ("pathStyle", style), ("grade", 9.0));
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            Assert.NotEmpty(paths);
            Assert.DoesNotContain(plan.Issues, i => i.Key == "Placemaker.LookoutHill.Issue.PathShort");
            var summit = Summit(plan);
            Assert.Contains(paths, n => math.distance(n.D.xz, summit) < 15f);
            float rise = 0f, run = 0f;
            foreach (var n in paths)
            {
                float len = math.distance(n.A.xz, n.D.xz);
                rise += math.abs(n.D.y - n.A.y); run += len;
                if (len > 3f) Assert.True(math.abs(n.D.y - n.A.y) / len < 0.45f, $"{style}: {math.abs(n.D.y - n.A.y) / len:P0} piece");
            }
            Assert.True(rise / run < 0.16f, $"{style}: average grade {rise / run:P0}");
            for (int i = 0; i < paths.Count; i++)
                for (int j = i + 2; j < paths.Count; j++)
                {
                    if (math.distance(paths[i].D.xz, paths[j].A.xz) < 0.01f || math.distance(paths[i].A.xz, paths[j].D.xz) < 0.01f) continue;
                    Assert.False(Poly.SegmentsIntersect(paths[i].A.xz, paths[i].D.xz, paths[j].A.xz, paths[j].D.xz), $"{style}: path crosses itself");
                }
        }

        [Theory]
        [InlineData("hilltop", false)]
        [InlineData("sledding", false)]
        [InlineData("viewpointMound", false)]
        [InlineData("terraces", false)]
        [InlineData("kiteHill", false)]
        [InlineData("hilltop", true)]
        [InlineData("sledding", true)]
        [InlineData("terraces", true)]
        [InlineData("kiteHill", true)]
        public void PathStartsOnTheStreetAndWalksStraightOntoThePlaza(string preset, bool cityBlock)
        {
            // (rf-nature) The user's "weird roads": the climb ended loose short of the pavement, and near the top it
            // hooked round the reserved plaza (an S-bend or a loop) instead of walking onto it.
            // The 6 ha L, and a 150 x 140 m city block (the game runner's block, where the hilltop path hooked).
            var ring = cityBlock ? TestKit.Rect(150, 140) : SixHaL;
            var road = TestKit.RoadAlongBottom(cityBlock ? 150 : 293);
            var composed = Compose(TestKit.Entered(WithPreset(preset), ring, road), ring, road);
            Assert.True(string.IsNullOrEmpty(Errors(composed)), Errors(composed));
            var plan = Layer(composed);
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            Assert.NotEmpty(paths);
            Assert.Contains(paths, n => TestKit.MeetsStreet(n, road));   // at the kerb: the game joins it (StreetJoins)
            var summit = Summit(plan);
            // The last 25 m of the climb (walking back from the piece that ends nearest the summit) only ever gets
            // closer to the summit: it walks onto the plaza instead of hooking round it.
            int k = 0;
            for (int i = 1; i < paths.Count; i++)
                if (math.distance(paths[i].D.xz, summit) < math.distance(paths[k].D.xz, summit)) k = i;
            float walked = 0f, worst = 0f;
            float2 prev = paths[k].D.xz;
            float nearest = math.distance(prev, summit);
            for (int i = k; i >= 0 && walked < 25f; i--)
            {
                if (i < k && math.distance(paths[i].D.xz, paths[i + 1].A.xz) > 0.01f) break;
                for (int s = 7; s >= 0 && walked < 25f; s--)
                {
                    var q = Composer.Bezier(paths[i].A, paths[i].B, paths[i].C, paths[i].D, s / 8f).xz;
                    walked += math.distance(q, prev);
                    float d = math.distance(q, summit);
                    worst = math.max(worst, nearest - d);   // walking back, the path came nearer than a later point
                    nearest = math.max(nearest, d);
                    prev = q;
                }
            }
            Out.WriteLine($"{preset}: {paths.Count} pieces, the last {walked:0} m: worst step away from the summit {worst:0.0} m");
            Assert.True(worst < 2f, $"{preset}: the path hooks round the plaza ({worst:0.0} m away again)");
            // No sharp corner where two pieces of the climb meet (a turn back on the spot left an acute node the game dug
            // a 7 m pit beside, on the sledding hill): consecutive pieces turn by less than 100 degrees.
            float sharpest = 0f;
            for (int i = 0; i + 1 < paths.Count; i++)
            {
                if (math.distance(paths[i].D.xz, paths[i + 1].A.xz) > 0.01f) continue;
                var din = math.normalizesafe(paths[i].D.xz - paths[i].C.xz, math.normalizesafe(paths[i].D.xz - paths[i].A.xz));
                var dout = math.normalizesafe(paths[i + 1].B.xz - paths[i + 1].A.xz, math.normalizesafe(paths[i + 1].D.xz - paths[i + 1].A.xz));
                sharpest = math.max(sharpest, math.degrees(math.acos(math.clamp(math.dot(din, dout), -1f, 1f))));
            }
            Out.WriteLine($"{preset}: sharpest turn {sharpest:0} deg");
            Assert.True(sharpest < 100f, $"{preset}: a {sharpest:0} deg corner in the climb");
        }

        [Fact]
        public void TreesStayBelowTheTreeLineAndTheSummitIsOpen()
        {
            var site = new SiteContext();
            var plan = PlanPreset("hilltop", SixHaL, site, ("treeLine", 50.0), ("treeDensity", 200.0));
            float peak = (float)plan.Facts["peak"];
            var trees = plan.Objects.Where(o => o.Role == ObjectRole.Tree).ToList();
            Assert.True(trees.Count > 50);
            Assert.All(trees, t => Assert.True(t.Position.y <= peak * 0.5f + 0.3f, $"tree at {t.Position.y:0.0} m above the tree line"));
            var summit = Summit(plan);
            Assert.True(plan.Objects.Count(o => o.Role == ObjectRole.Bench && math.distance(o.Position.xz, summit) < 12f) >= 3);
            Assert.Contains(plan.Regions, r => r.Tags.Contains("summit"));
        }

        [Fact]
        public void SleddingHillKeepsAnOpenRunWithARunOut()
        {
            var plan = PlanPreset("sledding", SixHaL, TestKit.RoadAlongBottom(293));
            Assert.True(plan.Facts.ContainsKey("runLength") && plan.Facts["runLength"] > 40f, "no proper run");
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.HillBale);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Shelter);   // warming hut / pavilion on top
        }

        [Fact]
        public void TerracesStepAndCarryBenchesOnEveryLevel()
        {
            var plan = PlanPreset("terraces", SixHaL, new SiteContext());
            Assert.True(plan.Facts["terraceBenches"] >= 3, "terrace benches " + plan.Facts["terraceBenches"]);
            // (rf-nature) Narrow paths: the 8 m wide walk cut walls into the steps and read as a road up the hill.
            Assert.DoesNotContain(plan.Nets, n => n.Role == NetRole.PathwayWide);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway);
            Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Lamp);
        }

        [Fact]
        public void KiteHillIsOpenWithKites()
        {
            // Art pass a3: no vanilla kite exists, so the flanks get kite-flyers' picnic blankets; the cairn is stones.
            var kite = PlanPreset("kiteHill", SixHaL, new SiteContext());
            var hill = PlanPreset("hilltop", SixHaL, new SiteContext());
            Assert.True(kite.Objects.Count(o => o.Category == AssetCategories.Picnic && (o.Asset.Id ?? "").StartsWith("PicnicBlanket")) >= 3);
            Assert.True(TestKit.Count(kite, ObjectRole.Tree) < TestKit.Count(hill, ObjectRole.Tree));
            Assert.Contains(kite.Objects, o => o.Category == AssetCategories.Rock && (o.Asset.Fallbacks ?? new string[0]).Contains("Boulder03"));   // cairn
        }

        [Fact]
        public void PresetsLookDifferent()
        {
            var peaks = Gen.Presets.ToDictionary(p => p.Id, p => (float)PlanPreset(p.Id, SixHaL, new SiteContext()).Facts["peak"]);
            Assert.True(peaks["viewpointMound"] < peaks["hilltop"] * 0.7f);
        }

        [Fact]
        public void ViewpointFeaturePlacesSpotsOnTheSummit()
        {
            var plan = Compose(WithPreset("viewpointMound", "viewpoint"), FiveHa, TestKit.RoadAlongBottom(250));
            var view = plan.Layers.Single(l => l.GeneratorId == "viewpoint");
            Assert.Contains(view.Objects, o => o.Role == ObjectRole.Bench);
            var summit = Summit(Layer(plan));
            Assert.All(view.Objects, o => Assert.True(math.distance(o.Position.xz, summit) < 20f));
        }

        [Fact]
        public void MissingHillCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.HillTelescope] = 0, [AssetCategories.HillBale] = 0, [AssetCategories.HillKite] = 0, [AssetCategories.HillBoard] = 0,
                [AssetCategories.Lookout] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var p in Gen.Presets)
                {
                    var plan = Compose(WithPreset(p.Id), FiveHa, TestKit.RoadAlongBottom(250));
                    Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
                    Assert.DoesNotContain(Layer(plan).Objects, o => none.ContainsKey(o.Category ?? ""));
                }
        }

        [Theory]
        [InlineData("CoinTelescope01", AssetCategories.HillTelescope)]
        [InlineData("TouristBinoculars", AssetCategories.HillTelescope)]
        [InlineData("HayBale01", AssetCategories.HillBale)]
        [InlineData("Windsock01", AssetCategories.HillKite)]
        [InlineData("InfoBoard02", AssetCategories.HillBoard)]
        public void HillClassification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(category, AssetHeuristics.Classify(name, t));
            Assert.True(AssetHeuristics.AutoEligible(category, name, t));
        }
    }
}
