// Shore kit (grand park wave, g2): water survey, the "Shore" feature of park and plaza (auto-active
// when the area is partly in water), the shore add-on's treatments, and the park lake upgrade.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Shore;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using static Placemaker.Tests.Types.WaterTypesKit;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class ShoreTests
    {
        private readonly ITestOutputHelper _out;
        public ShoreTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => TestKit.Gen("shore").Presets.Select(p => new object[] { p.Id });
        public static IEnumerable<object[]> PondPresetIds() => TestKit.Gen("pond").Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string gen, string id) => TestKit.Gen(gen).Presets.Single(p => p.Id == id);

        /// <summary>The southern 40 % of a 250 × 200 area is open water (2 m deep).</summary>
        private static SiteContext HalfInWater() => WaterInside(80f);

        private static bool Wet(SiteContext site, float2 p) => site.WaterDepthAt(p) > 0.05f;

        // ───────────── survey ─────────────

        [Fact]
        public void SurveyFindsWaterInsideAlongsideOrNone()
        {
            var region = LayerRegion.From(Poly.To2D(FiveHa));
            var inside = ShoreKit.Survey(HalfInWater(), region, null);
            Assert.True(inside.Inside);
            Assert.InRange(inside.WetFraction, 0.3f, 0.5f);
            var edge = ShoreKit.Survey(SeaSouth(), region, null);
            Assert.False(edge.Inside);
            Assert.True(edge.Edge);
            Assert.True(edge.WetEdgeLength > 200f);
            var dry = ShoreKit.Survey(new SiteContext(), region, null);
            Assert.False(dry.Any);
            Assert.True(float.IsNaN(dry.WaterLevel));
        }

        // ───────────── host feature ─────────────

        [Fact]
        public void ShoreIsOnByDefaultForParkAndPlaza()
        {
            foreach (var host in new[] { "park", "plaza" })
            {
                // (Phase 1) the shore is a setting of the host now (lookShore), "auto" by default, not a default-on add-on.
                var gen = TestKit.Gen(host);
                Assert.DoesNotContain(Features.Of(gen), x => x.Layer == ShoreKit.LayerId);
                Assert.Equal("auto", gen.Controls.Single(c => c.Id == "lookShore").Default);
                var (look, f) = LookSettings.Find(gen, "lookShore");
                Assert.Equal(ShoreKit.LayerId, look.Feature);
                Assert.Equal(ShoreKit.Place, f.Target ?? "");
                Assert.Contains(ShoreKit.Place, TestKit.Gen(host).Emits);
            }
        }

        [Fact]
        public void ParkHalfInWaterGetsAPierAndAWharf()
        {
            var site = HalfInWater();
            var plan = Compose(With("park", "shore"), FiveHa, site);
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var places = park.Regions.Where(r => r.Tags.Contains(ShoreKit.Place)).ToList();
            Assert.NotEmpty(places);
            // The park keeps to the dry land: no tree, bench or path of its own in the water.
            foreach (var o in park.Objects) Assert.False(Wet(site, o.Position.xz), $"park {o.Role} in the water at {o.Position.xz}");
            foreach (var n in park.Nets) Assert.False(Wet(site, Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).xz), "park path in the water");
            // Siblings stay out of the shore place.
            foreach (var l in plan.Layers.Where(l => l.GeneratorId == "bigtrees"))
                foreach (var o in l.Objects) Assert.False(Wet(site, o.Position.xz), "big tree in the water");

            var shore = plan.Layers.Single(l => l.GeneratorId == "shore");
            Assert.Equal(1, shore.Facts["wharves"]);
            Assert.True(shore.Facts["piers"] >= 1);
            Assert.Contains(shore.Terraform, t => t.Kind == TerraformKind.Raise && t.TargetHeight > -0.5f && Wet(site, Poly.Centroid(Poly.To2D(t.Ring))));
            Assert.Contains(shore.Nets, n => !n.FollowTerrain && Wet(site, n.D.xz) && n.D.y > 0f);
            Assert.Contains(shore.Objects, o => o.Category == AssetCategories.WaterfrontMooring);
            Assert.Contains(shore.Objects, o => o.Category == AssetCategories.ShoreBoat && Wet(site, o.Position.xz));
            Assert.Contains(shore.Nets, n => n.Role == NetRole.Fence);   // the promenade either side of the quay
            Assert.Contains(shore.Areas, a => a.Role == AreaRole.PedestrianArea);
            Assert.Contains(shore.Issues, i => i.Key == TerrainShaper.PermanentKey);
            // Things on the quay stand on it, not on the old sea bed.
            foreach (var o in shore.Objects.Where(o => o.Category == AssetCategories.WaterfrontMooring))
                Assert.True(o.Position.y >= 0f, "bollard below the quay");
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.5f);
        }

        [Fact]
        public void PlazaHalfInWaterBecomesAHarbourQuay()
        {
            var site = HalfInWater();
            var plan = Compose(With("plaza", "shore"), FiveHa, site);
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            var shore = plan.Layers.Single(l => l.GeneratorId == "shore");
            Assert.Equal(1, shore.Facts["wharves"]);
            Assert.True(shore.Facts["quayArea"] > 1000);
            Assert.DoesNotContain(shore.Nets, n => n.Role == NetRole.Fence);
            var plaza = plan.Layers.Single(l => l.GeneratorId == "plaza");
            foreach (var s in plaza.Surfaces) Assert.False(Wet(site, Poly.Centroid(Poly.To2D(s.Ring))), "plaza paving over the water");
        }

        [Fact]
        public void AwayFromWaterTheFeatureIsIdleAndChangesNothing()
        {
            foreach (var host in new[] { "park", "plaza" })
            {
                var with = Compose(With(host, "shore"), FiveHa, new SiteContext());
                var without = Compose(With(host), FiveHa, new SiteContext());
                Assert.DoesNotContain(with.Layers, l => l.GeneratorId == "shore");
                Assert.Equal(TestKit.Fingerprint(without.Layers[0]), TestKit.Fingerprint(with.Layers[0]));
            }
        }

        [Fact]
        public void ParkAlongTheSeaGetsAPromenadeBand()
        {
            var site = SeaSouth(0.03f);
            var plan = Compose(With("park", "shore"), FiveHa, site);
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            var shore = plan.Layers.Single(l => l.GeneratorId == "shore");
            Assert.Equal(0, shore.Facts["wharves"]);   // nothing to fill inside the area
            Assert.Contains(shore.Nets, n => n.Role == NetRole.PathwayWide || n.Role == NetRole.Pathway);
            Assert.True(shore.Facts["bollards"] > 5 || shore.Facts["railings"] > 0);
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.5f);
        }

        // ───────────── shore add-on presets ─────────────

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansInWaterAlongWaterAndOnLShapes(string preset)
        {
            foreach (var (ring, site) in new[] { (FiveHa, HalfInWater()), (OneHa, SeaSouth(0.05f)), (SixHaL, WaterInside(60f)) })
            {
                var plan = PlanPreset("shore", Preset("shore", preset), ring, site);
                Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                Assert.True(plan.Objects.Count + plan.Nets.Count > 8, preset + " built only " + (plan.Objects.Count + plan.Nets.Count));
                AssertInside(plan, ring);
                AssertNothingOnPaths(plan);
                TestKit.AssertSurfacesAccepted(plan);
                Assert.All(plan.Terraform, t => Assert.True(t.Permanent));
                var again = PlanPreset("shore", Preset("shore", preset), ring, site);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(again));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReserved(string preset)
        {
            var reserved = new ReservedSpace();
            reserved.AddDisc(new float2(120, 75), 6f, "test");
            reserved.AddCorridor(new float2(20, 90), new float2(230, 95), 2f, "test");
            var plan = PlanPreset("shore", Preset("shore", preset), FiveHa, HalfInWater(), 7, reserved);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void StylesLookDifferent()
        {
            var site = HalfInWater();
            AreaPlan P(string preset) => PlanPreset("shore", Preset("shore", preset), FiveHa, site);
            var harbour = P("harbour");
            Assert.True(harbour.Facts["quayArea"] > 1500);
            Assert.True(harbour.Facts["bollards"] > 10);
            var piers = P("pleasurePier");
            Assert.Equal(2, piers.Facts["piers"]);
            Assert.True(piers.Facts["pierLength"] > 80);
            Assert.Empty(piers.Terraform);
            var boardwalk = P("boardwalk");
            Assert.True(boardwalk.Nets.Count(n => !n.FollowTerrain) >= 3, "deck over the water");
            Assert.True(boardwalk.Facts["reeds"] > 10);
            var beach = P("beach");
            Assert.Contains(beach.Surfaces, s => s.Category == AssetCategories.SurfaceSand);
            Assert.True(beach.Facts["umbrellas"] > 5);
            var reeds = P("reeds");
            Assert.True(reeds.Facts["reeds"] > 40);
            Assert.Contains(reeds.Objects, o => o.Category == AssetCategories.WetlandReed && Wet(site, o.Position.xz));
        }

        [Fact]
        public void NoWaterMeansOneInfoAndNothingBuilt()
        {
            var plan = PlanPreset("shore", Preset("shore", "waterside"), OneHa, new SiteContext());
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.AddOn.Shore.Issue.NoWater" && i.Severity == IssueSeverity.Info);
            Assert.Empty(plan.Objects);
            Assert.Empty(plan.Nets);
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.WaterfrontMooring] = 0, [AssetCategories.Bollard] = 0, [AssetCategories.ShoreBoat] = 0, [AssetCategories.BeachBoat] = 0,
                [AssetCategories.ShoreCargo] = 0, [AssetCategories.LogisticsCrate] = 0, [AssetCategories.WaterfrontNautical] = 0,
                [AssetCategories.WetlandReed] = 0, [AssetCategories.Bush] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "harbour", "reeds" })
                {
                    var plan = PlanPreset("shore", Preset("shore", preset), FiveHa, HalfInWater());
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.NotEmpty(plan.Nets);
                }
        }

        [Fact]
        public void FastOnFiveHectares()
        {
            foreach (var p in TestKit.Gen("shore").Presets)
                foreach (var site in new[] { HalfInWater(), SeaSouth(0.04f) })
                {
                    long ms = Time(() => PlanPreset("shore", p, FiveHa, site));
                    _out.WriteLine($"shore/{p.Id}: {ms} ms");
                    Assert.True(ms < 250, $"shore/{p.Id} took {ms} ms for 5 ha");
                }
            long park = Time(() => Compose(With("park", "shore"), FiveHa, HalfInWater()));
            _out.WriteLine($"park + shore: {park} ms");
        }

        // ───────────── park lake ─────────────

        private static float3[] LakeRing => TestKit.Blob(55f, 48, 3);

        [Theory]
        [MemberData(nameof(PondPresetIds))]
        public void PondPresetsPlanDeterministically(string preset)
        {
            var site = new SiteContext { HeightAt = p => 0.01f * p.x };
            var plan = PlanPreset("pond", Preset("pond", preset), LakeRing, site);
            Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
            Assert.Equal(TerraformKind.Lower, plan.Terraform[0].Kind);
            Assert.False(float.IsNaN(plan.Terraform[0].WaterLevel));
            Assert.All(plan.Terraform, t => Assert.True(t.Permanent));
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Park.Issue.LakePermanent");
            TestKit.AssertSurfacesAccepted(plan);
            AssertNothingOnPaths(plan);
            Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(PlanPreset("pond", Preset("pond", preset), LakeRing, site)));
        }

        [Fact]
        public void IslandLakeHasRaisedPlantedIslandsWithBridges()
        {
            var plan = PlanPreset("pond", Preset("pond", "islandLake"), LakeRing, new SiteContext());
            Assert.Equal(2, plan.Facts["islands"]);
            var islands = plan.Terraform.Where(t => t.Kind == TerraformKind.Raise).ToList();
            Assert.Equal(2, islands.Count);
            float level = plan.Terraform[0].WaterLevel;
            Assert.All(islands, t => Assert.True(t.TargetHeight > level));
            // The water surface leaves the islands dry.
            foreach (var t in islands)
            {
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                Assert.DoesNotContain(plan.Surfaces, s => s.Role == SurfaceRole.Water && Poly.Contains(Poly.To2D(s.Ring), c));
                Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Tree && Poly.Contains(Poly.To2D(t.Ring), o.Position.xz));
            }
            Assert.True(plan.Facts["bridges"] >= 2);
            var decks = plan.Nets.Where(n => !n.FollowTerrain).ToList();
            Assert.True(decks.Count >= 2);
            Assert.All(decks, n => Assert.True(Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).y > level + 0.5f, "arched over the water"));
        }

        [Fact]
        public void GrandLakeHasJetsBoathouseBoatsAndPromenade()
        {
            var plan = PlanPreset("pond", Preset("pond", "grandLake"), TestKit.Blob(75f, 48, 5), new SiteContext());
            Assert.True(plan.Facts["jets"] >= 2);
            Assert.Equal(1, plan.Facts["boathouse"]);
            Assert.True(plan.Facts["boats"] >= 3);
            Assert.Equal(1, plan.Facts["lakePromenade"]);
            Assert.Contains(plan.Surfaces, s => s.Category == AssetCategories.SurfacePaving);
            Assert.True(plan.Nets.Count(n => n.Role == NetRole.Pathway && n.FollowTerrain) >= 10, "walk round the lake");
            Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Bench);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.ShoreBoathouse);
            Assert.True(plan.Objects.Count(o => o.Role == ObjectRole.Centerpiece) >= 2);
        }

        [Fact]
        public void ParkWithUpgradedPondComposes()
        {
            // (Phase 1) the pond is the park's water setting (lookWater); its upgrade is the "lake" level, not a child edited by hand.
            var r = With("park", "pond");
            Assert.Equal("pond", r.Root.Settings.Values["lookWater"]);
            r.Root.Settings.Values["lookWater"] = "lake";
            var plan = Compose(r, TestKit.Rect(320, 260));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            var pond = plan.Layers.Single(l => l.GeneratorId == "pond");
            Assert.True(pond.Facts["islands"] >= 1);
            foreach (var l in plan.Layers) AssertInside(l, TestKit.Rect(320, 260), 1.5f);
        }

        // ───────────── labels & categories ─────────────

        [Fact]
        public void LabelsAndCategories()
        {
            AssertLabels("shore");
            AssertLabels("pond");
            var lang = TestKit.Lang();
            foreach (var key in new[] { "Placemaker.Feature.shore", "Placemaker.Feature.shore.Tooltip", ShoreKit.TooWetKey,
                         Keys.Category(AssetCategories.ShoreBoat), Keys.Category(AssetCategories.ShoreBoathouse), Keys.Category(AssetCategories.ShoreCargo),
                         "Placemaker.Stat.QuayArea", "Placemaker.Stat.Boats", "Placemaker.Stat.Islands", "Placemaker.Stat.Bridges" })
                Assert.True(lang.ContainsKey(key), key);
            var traits = Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh;
            Assert.Contains(AssetCategories.ShoreBoat, Placemaker.Core.Build.AssetHeuristics.Classify("SailBoat01", traits));
            Assert.Contains(AssetCategories.ShoreBoathouse, Placemaker.Core.Build.AssetHeuristics.Classify("Boathouse01", traits));
            Assert.True(GeneratorRegistry.IsHidden(TestKit.Gen("pond")));
        }
    }
}
