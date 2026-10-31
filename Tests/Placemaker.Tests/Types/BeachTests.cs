// Beach type: presets, water side detection, carving, styles, features, localisation.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using static Placemaker.Tests.Types.WaterTypesKit;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class BeachTests
    {
        private readonly ITestOutputHelper _out;
        public BeachTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => TestKit.Gen("beach").Presets.Select(p => new object[] { p.Id });
        private static PresetInfo Preset(string id) => TestKit.Gen("beach").Presets.Single(p => p.Id == id);

        /// <summary>The family beach without its carved bay (the bay moves the waterline 24 m inland; these checks measure from the
        /// shore as drawn).</summary>
        private static AreaPlan SandyWithoutBay(SiteContext site)
        {
            var v = Preset("sandy").Values.Clone();
            v.Values["bay"] = 0.0;
            return TestKit.Gen("beach").Plan(TestKit.Input(FiveHa, v, 7, null, site), CancellationToken.None);
        }

        [Fact]
        public void HasFourToSixPresetsAndDeepControls()
        {
            var g = TestKit.Gen("beach");
            Assert.InRange(g.Presets.Count, 4, 14);   // p2-beaches: shingle, pine lake, granite, ravine, marsh, harbour cove, wild
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 84);   // s13: sand blend, sand to water, landings, lifeguard/landing assets; rf-beach: jetties
            Assert.InRange(g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Distinct().Count(), 4, 12);   // p2-beaches: the biome mix
            Assert.Equal("water", GeneratorGroups.Of(g));
            Assert.Contains(g.Controls, c => c.Id == "style" && c.Kind == ControlKind.Choice);
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var (ring, site) in new[] { (OneHa, SeaSouth(0.05f)), (SixHaL, SeaSouth(0.03f)), (OneHa, new SiteContext()) })
            {
                var plan = PlanPreset("beach", Preset(preset), ring, site);
                Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                Assert.True(plan.Objects.Count > 15, preset + " placed only " + plan.Objects.Count);
                Assert.NotEmpty(plan.Nets);
                AssertInside(plan, ring);
                AssertNothingOnPaths(plan);
                TestKit.AssertSurfacesAccepted(plan);
                // p2-beaches: knolls and headlands stand on the shore itself (up to 18 m in from the water)
                var pv = Preset(preset).Values;
                AssertTerrainBlended(plan, ring, site, p => p.y, pv.Num("knolls", 0) > 0 || pv.Num("headlands", 0) > 0 ? 20f : 4f);
                // Deterministic.
                var again = PlanPreset("beach", Preset(preset), ring, site);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(again));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReserved(string preset)
        {
            var reserved = SomeReserved();
            var plan = PlanPreset("beach", Preset(preset), OneHa, SeaSouth(), 7, reserved);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void FastOnFiveHectares()
        {
            PlanPreset("beach", TestKit.Gen("beach").Presets[0], FiveHa, SeaSouth(0.04f));   // JIT (as the other types' timing tests)
            foreach (var p in TestKit.Gen("beach").Presets)
            {
                AreaPlan pl = null;
                long ms = Time(() => pl = PlanPreset("beach", p, FiveHa, SeaSouth(0.04f)));
                _out.WriteLine($"{p.Id}: {ms} ms (terrain {pl.Facts.GetValueOrDefault("msTerrain")}, hinterland {pl.Facts.GetValueOrDefault("msHinterland")}, decals {pl.Facts.GetValueOrDefault("msDecals")}, finish {pl.Facts.GetValueOrDefault("msFinish")})");
                Assert.True(ms < TimingBudget.Ms(450), $"beach/{p.Id} took {ms} ms for 5 ha");   // p2-beaches: it plans the land behind as a Biome mix (the adventure playground's 450 ms, docs/runbooks/biomes.md)
            }
        }

        [Fact]
        public void FindsTheSeaAndLaysOutBandsFromIt()
        {
            var plan = PlanPreset("beach", Preset("sandy"), FiveHa, SeaSouth(0.02f));
            Assert.DoesNotContain(plan.Issues, i => i.Key == "Placemaker.Shore.Issue.NoWater");
            Assert.Equal(250, plan.Facts["shoreLength"], 0);
            // Back path parallel to the water just behind the sand (s20: measured from the real waterline).
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway).ToList();
            float pathZ = (float)plan.Facts["beachDepth"] + Composer.DefaultHalfWidth(NetRole.Pathway) + 0.5f;
            Assert.Contains(paths, n => math.abs(n.A.z - pathZ) < 2f && math.abs(n.D.z - pathZ) < 2f && math.abs(n.A.x - n.D.x) > 5f);
            // Umbrellas and lifeguards on the sand between the water and the path; huts behind it.
            var umbrellas = plan.Objects.Where(o => o.Category == AssetCategories.BeachUmbrella).ToList();
            Assert.NotEmpty(umbrellas);
            Assert.All(umbrellas, o => Assert.InRange(o.Position.z, 2f, pathZ));
            Assert.All(plan.Objects.Where(o => o.Category == AssetCategories.BeachLifeguard), o => Assert.InRange(o.Position.z, 4f, 15f));
            Assert.All(plan.Objects.Where(o => o.Category == AssetCategories.BeachHut), o => Assert.True(o.Position.z > pathZ));
            // Umbrellas face the sea (yaw ≈ π: towards −Z).
            Assert.All(umbrellas, o => Assert.True(math.cos(o.Yaw) < -0.7f, "umbrella yaw " + o.Yaw));
            // Showers at the bottom of the beach accesses.
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.BeachShower);
            Assert.True(plan.Facts["accessPaths"] >= 2);
        }

        [Fact]
        public void WaterSideChoiceAndRoadFallback()
        {
            // No water: away from the road along the bottom → sea at the top.
            var input = TestKit.Input(FiveHa, Preset("sandy").Values.Clone(), 3, null, TestKit.RoadAlongBottom(250));
            var plan = TestKit.Plan("beach", input);
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Shore.Issue.NoWater" && i.Severity == IssueSeverity.Info);
            Assert.All(plan.Objects.Where(o => o.Category == AssetCategories.BeachUmbrella), o => Assert.True(o.Position.z > 200f - 61f));
            // Explicit east.
            var east = Preset("sandy").Values.Clone();
            east.Values["waterSide"] = "east";
            plan = TestKit.Plan("beach", TestKit.Input(FiveHa, east, 3));
            Assert.DoesNotContain(plan.Issues, i => i.Key == "Placemaker.Shore.Issue.NoWater");
            var u = plan.Objects.Where(o => o.Category == AssetCategories.BeachUmbrella).ToList();
            Assert.NotEmpty(u);
            Assert.All(u, o => Assert.True(o.Position.x > 250f - 61f));
        }

        [Fact]
        public void WaterInsideTheAreaIsLeftAlone()
        {
            var site = WaterInside(60f);
            var plan = PlanPreset("beach", Preset("sandy"), FiveHa, site);
            Assert.False(plan.HasErrors);
            Assert.Equal((double)(int)Placemaker.Core.Layers.Types.Beach.ShoreMode.Inside, plan.Facts["shoreMode"]);
            Assert.All(plan.Objects, o => Assert.False(site.WaterDepthAt(o.Position.xz) > 0f, $"{o.Category} in the water at {o.Position.xz}"));
            Assert.All(plan.Nets.Where(n => n.Category != AssetCategories.NetPier), n => Assert.True(n.A.z > 58f && n.D.z > 58f, "net in the water"));   // rf-beach: jetties go out over it
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.BeachUmbrella && o.Position.z < 95f);
        }

        [Fact]
        public void CarvesAGentleSlopeIntoABankAndRaisesDunes()
        {
            var site = SeaSouth(0.2f);   // a 20 % bank rising from the water
            var plan = SandyWithoutBay(site);
            var lowered = plan.Terraform.Where(t => t.Kind == TerraformKind.Lower).ToList();
            var raised = plan.Terraform.Where(t => t.Kind == TerraformKind.Raise).ToList();
            Assert.NotEmpty(lowered);
            // Dunes behind the path: the ground there stands higher than the same bank without dunes.
            var lawnValues = Preset("sandy").Values.Clone();
            lawnValues.Values["backdrop"] = "lawn";
            // (on a gentle shore: on this 20 % bank the cut behind the beach is already at the earthwork limit)
            var gentle = SeaSouth(0.03f);
            var withDunes = TestKit.Gen("beach").Plan(TestKit.Input(FiveHa, Preset("sandy").Values.Clone(), 5, null, gentle), CancellationToken.None);
            var lawn = TestKit.Gen("beach").Plan(TestKit.Input(FiveHa, lawnValues, 5, null, gentle), CancellationToken.None);
            float After(AreaPlan pl, float2 q) { foreach (var t in pl.Terraform) if (Poly.Contains(Poly.To2D(t.Ring), q)) return Placemaker.Core.Build.TerraformSurface.At(t.Ring, q); return gentle.HeightAt(q); }
            float duneZ = (float)withDunes.Facts["beachDepth"] + 16f;
            Assert.True(After(withDunes, new float2(125, duneZ)) > After(lawn, new float2(125, duneZ)) + 0.6f, $"no dune at z {duneZ}");
            // Beach cells sit on a gentle plane: at most ~6 % + slack above the waterline.
            foreach (var t in lowered)
            {
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                if (c.y < 26f && c.x > 30f && c.x < 220f) Assert.True(t.TargetHeight <= 0.06f * c.y + 0.8f + (float)Preset("sandy").Values.Num("berm", 0) * 1.1f,   // p2-beaches: the berm crest stands above the plane
                     $"beach at {c} is {t.TargetHeight:0.00} m high");
            }
            // s20: hollows on the beach face may be filled a little (≤ 0.6 m), never more.
            Assert.All(raised.Where(t => Poly.Centroid(Poly.To2D(t.Ring)).y < (float)plan.Facts["beachDepth"]),
                t => Assert.True(t.TargetHeight <= site.HeightAt(Poly.Centroid(Poly.To2D(t.Ring))) + 0.7f));
            AssertTerrainBlended(plan, FiveHa, site, p => p.y, 4f);
            // Carving off: no lowering.
            var keep = Preset("sandy").Values.Clone();
            keep.Values["carve"] = false;
            var flat = TestKit.Gen("beach").Plan(TestKit.Input(FiveHa, keep, 5, null, site), CancellationToken.None);
            // (s20: level pads under huts and kiosks behind the path may cut a little into the bank; the beach itself is not lowered)
            // (p2-beaches: a sloped patch is measured at its middle: its TargetHeight is its lowest corner)
            foreach (var t in flat.Terraform)
            {
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float mean = t.Ring.Average(v => v.y);
                Assert.False(c.y < (float)flat.Facts["beachDepth"] - 4f && mean < site.HeightAt(c) - 0.15f, $"lowered at {c}: {mean:0.00} m, ground {site.HeightAt(c):0.00} m ({t.Kind}, {t.Ring.Length} corners)");
            }
        }

        [Fact]
        public void StylesLookDifferent()
        {
            AreaPlan P(string preset) => PlanPreset("beach", Preset(preset), FiveHa, SeaSouth(0.03f));
            var rocky = P("rocky");
            Assert.True(rocky.Objects.Count(o => o.Category == AssetCategories.Rock) > 30);
            Assert.True(rocky.Facts["tidePools"] >= 2);
            Assert.Contains(rocky.Terraform, t => t.Kind == TerraformKind.Lower);
            Assert.DoesNotContain(rocky.Surfaces, s => s.Category == AssetCategories.SurfaceSand);
            Assert.NotEmpty(rocky.Paint);

            var volley = P("volleyball");
            Assert.True(volley.Objects.Count(o => o.Category == AssetCategories.BeachVolleyball) >= 3);

            var cove = P("cove");
            Assert.Contains(cove.Objects, o => o.Category == AssetCategories.Rock && o.Position.x < 25f);
            Assert.Contains(cove.Objects, o => o.Category == AssetCategories.Rock && o.Position.x > 225f);
            Assert.True(cove.Facts["firePits"] >= 1, "stone fire rings");   // built from small rocks: no vanilla fire pit
            // rf-beach: wild dunes are dunes (marram grass), not woods
            Assert.True(cove.Objects.Count(o => o.Role == ObjectRole.Bush) > P("sandy").Objects.Count(o => o.Role == ObjectRole.Bush));

            var river = P("river");
            Assert.Contains(river.Objects, o => o.Category == AssetCategories.BeachBoat);
            Assert.Contains(river.Paint, p => p.Layer == TerrainMaterials.Lush);   // rf-beach: the lawn is painted

            var resort = P("resort");
            Assert.DoesNotContain(resort.Objects, o => o.Category == AssetCategories.BeachHut);   // no vanilla beach hut (pool = a 40 m building)
            Assert.Contains(resort.Paint, p => p.Layer == TerrainMaterials.Lush);   // rf-beach: the lawn is painted
            Assert.True(resort.Facts["jetties"] >= 1 && P("sandy").Facts["jetties"] >= 1, "a pier / swimming jetty");
            Assert.True(resort.Objects.Count(o => o.Category == AssetCategories.BeachUmbrella) > P("sandy").Objects.Count(o => o.Category == AssetCategories.BeachUmbrella));

            var sandy = P("sandy");
            // s20: no sand fences by default (vanilla has no timber sand fence; the pool picked brick walls with railings).
            Assert.DoesNotContain(sandy.Nets, n => n.Role == NetRole.Fence);
            Assert.True(sandy.Objects.Count(o => o.Role == ObjectRole.Bush) > 50, "dune grass");
        }

        [Fact]
        public void SandIsPaintedToTheWaterAndBlendsInland()
        {
            // The outline stops 8 m short of the sea (a grassy bank): the sand must still reach the water.
            var site = new SiteContext { WaterDepthAt = p => p.y < -8f ? 2f : 0f, HeightAt = p => p.y < -8f ? -1.5f : 0.06f * p.y };
            var plan = SandyWithoutBay(site);
            Assert.True(plan.Facts["foreshore"] >= 1);
            var sand = plan.Paint.Where(p => p.Layer == TerrainMaterials.Sand && p.GradientFrom != null && p.Strength > 0.99f).ToList();   // the beach field (dunes have their own, lighter one)
            Assert.NotEmpty(sand);
            float At(float2 q) => sand.Max(p => Placemaker.Core.Build.PaintField.StrengthAt(p, q));
            Assert.True(At(new float2(125, -8.5f)) > 0.9f, "sand at the waterline " + At(new float2(125, -8.5f)));
            Assert.True(At(new float2(125, 20)) > 0.9f, "sand on the beach");
            float blend = (float)plan.Facts["sandBlend"], depth = (float)plan.Facts["beachDepth"];
            // s20: the beach depth is measured from the real waterline (8 m in front of the outline here).
            float mid = At(new float2(125, -8f + depth + blend * 0.5f));
            Assert.InRange(mid, 0.15f, 0.85f);                                   // the blend is a gradient, not an edge
            Assert.True(At(new float2(125, -8f + depth + blend + 6f)) < 0.05f, "sand beyond the blend");
            Assert.True(At(new float2(125, -14f)) > 0.9f, "sand under the water");
            Assert.DoesNotContain(plan.Surfaces, s => s.Category == AssetCategories.SurfaceSand);   // no hard-edged sand quad by default
            // The graded slope follows the sand down into the water, never filling the bed up to the surface (0.5 m here).
            Assert.Contains(plan.Terraform, t => Poly.Centroid(Poly.To2D(t.Ring)).y < -8f);
            Assert.All(plan.Terraform.Where(t => Poly.Centroid(Poly.To2D(t.Ring)).y < -9f), t => Assert.True(t.TargetHeight < 0.3f, "bed raised to " + t.TargetHeight));
        }

        [Fact]
        public void AccessesEndOnLandingsAndSetsUseOneModel()
        {
            var plan = PlanPreset("beach", Preset("sandy"), FiveHa, SeaSouth(0.03f));
            Assert.True(plan.Facts["accessPaths"] >= 2);
            Assert.Equal(plan.Facts["accessPaths"], plan.Facts["landings"]);
            var landings = plan.Surfaces.Where(s => s.Role == SurfaceRole.Paving).Select(s => Poly.Centroid(Poly.To2D(s.Ring))).ToList();
            // Every access path ends at a landing (its seaward end touches one).
            // (the accesses drop off the promenade; an entrance connector coming down from the street ends on the promenade)
            // (p2-beaches: a spur on to a jetty's root ends on the jetty, not a landing)
            var pierRoots = plan.Nets.Where(n => n.Category == AssetCategories.NetPier).Select(n => n.A.xz).ToList();
            foreach (var n in plan.Nets.Where(n => n.Role == NetRole.Pathway && n.Category != AssetCategories.NetPier && !pierRoots.Any(r => math.distance(r, n.D.xz) < 0.5f) && math.abs(n.A.x - n.D.x) < 0.5f && n.A.z > n.D.z + 3f && n.A.z < (float)plan.Facts["beachDepth"] + 6f))
                Assert.Contains(landings, c => math.distance(c, n.D.xz) < 3f);
            Assert.Single(plan.Objects.Where(o => o.Category == AssetCategories.BeachUmbrella).Select(o => o.Asset.Id).Distinct());
            Assert.Single(plan.Objects.Where(o => o.Category == AssetCategories.BeachLounger).Select(o => o.Asset.Id).Distinct());
            // Lifeguard posts (vanilla: tall chair) behind the wet sand, facing the water.
            var guards = plan.Objects.Where(o => o.Asset.Id == "TennisUmpireChair01").ToList();
            Assert.Equal(2, guards.Count);
            Assert.All(guards, o => Assert.True(math.cos(o.Yaw) < -0.9f));
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.BeachUmbrella] = 0, [AssetCategories.CafeSeating] = 0, [AssetCategories.BeachLifeguard] = 0, [AssetCategories.Lookout] = 0,
                [AssetCategories.BeachShower] = 0, [AssetCategories.BeachHut] = 0, [AssetCategories.GardenShed] = 0, [AssetCategories.BeachVolleyball] = 0,
                [AssetCategories.Sports] = 0, [AssetCategories.SurfaceSand] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "sandy", "volleyball", "resort" })
                {
                    var plan = PlanPreset("beach", Preset(preset), FiveHa, SeaSouth());
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.DoesNotContain(plan.Surfaces, s => s.Category == AssetCategories.SurfaceSand);
                    Assert.NotEmpty(plan.Nets);
                }
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(TestKit.Gen("beach")).Select(f => f.Layer).ToArray();
            Assert.Contains("beachBar", features);
            var plan = Compose(With("beach", features), FiveHa, SeaSouthRoadNorth(250, 200));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            Assert.Contains(plan.Layers[0].Regions, r => r.Tags.Contains(RegionTags.Core));
            var bar = plan.Layers.Single(l => l.GeneratorId == "beachBar");
            Assert.Contains(bar.Objects, o => o.Category == AssetCategories.Kiosk);
        }

        [Fact]
        public void LabelsRecipeAndClassification()
        {
            AssertLabels("beach");
            AssertLabels("beachBar");
            AssertSourceKeys("Beach");
            AssertRecipe("beach");
            Assert.False(GeneratorRegistry.IsHidden(TestKit.Gen("beach")));
            Assert.True(GeneratorRegistry.IsHidden(TestKit.Gen("beachBar")));
            Assert.Contains(AssetCategories.BeachLounger, Placemaker.Core.Build.AssetHeuristics.Classify("SunLounger01", Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh));
            Assert.Contains(AssetCategories.BeachLifeguard, Placemaker.Core.Build.AssetHeuristics.Classify("LifeguardTower01", Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh));
            var parasol = Placemaker.Core.Build.AssetHeuristics.Classify("BeachUmbrella02", Placemaker.Core.Build.AssetTraits.StaticObject | Placemaker.Core.Build.AssetTraits.HasMesh);
            Assert.Contains(AssetCategories.BeachUmbrella, parasol);
        }
    }
}
