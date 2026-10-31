// Type wave (builder 9): adventure playground. Shared invariant checks for builder 9's types live in
// TypeChecks09 (also used by MemorialParkTests).

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

namespace Placemaker.Tests.Types
{
    public static class TypeChecks09
    {
        public static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>1 ha square and a 6 ha L-shape (both with a road along the bottom edge).</summary>
        public static IEnumerable<(string name, float3[] ring, float width)> Shapes()
        {
            yield return ("1ha", TestKit.Rect(100, 100), 100f);
            yield return ("6haL", TestKit.LShape(293f), 293f);
        }

        public static SiteContext SlopedSite(float width)
        {
            var site = TestKit.RoadAlongBottom(width);
            site.HeightAt = p => 0.02f * p.x + 0.01f * p.y;
            return site;
        }

        public static AreaPlan PlanPreset(string layer, PresetInfo preset, float3[] ring, SiteContext site, uint seed = 7, ReservedSpace reserved = null)
        {
            var input = TestKit.Input(ring, preset.Values.Clone(), seed, null, site);
            if (reserved != null) input.Reserved = reserved;
            return TestKit.Gen(layer).Plan(input, CancellationToken.None);
        }

        public static string Errors(IEnumerable<PlanIssue> issues) =>
            string.Join("; ", issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        public static void AssertInside(AreaPlan plan, float3[] ring, float tolerance = 0.05f)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < tolerance,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {n.Role} end {p.xz} outside");
            foreach (var s in plan.Surfaces)
                foreach (var p in s.Ring)
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {s.Role} surface point {p.xz} outside");
        }

        /// <summary>No placed object (decals, spawners excepted) sits on a path of the same plan.</summary>
        public static void AssertNothingOnPaths(AreaPlan plan)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.AnimalSpawner) continue;
                foreach (var n in plan.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        /// <summary>Every terraform is anchored: the change never exceeds ~30 % of the distance to the outline.</summary>
        public static void AssertTerraformBlended(AreaPlan plan, float3[] ring, SiteContext site)
        {
            var r = Poly.To2D(ring);
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent);
                foreach (var v in t.Ring) Assert.True(Poly.Contains(r, v.xz) || Poly.DistanceToBoundary(r, v.xz) < 0.1f, "terraform outside the area");
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float edge = Poly.DistanceToBoundary(r, c);
                float change = math.abs(t.TargetHeight - site.HeightAt(c));
                Assert.True(change < edge * 0.3f + 0.3f, $"{plan.GeneratorId}: {change:0.00} m change {edge:0.0} m from the edge (cliff)");
            }
            if (plan.Terraform.Count > 0) Assert.Contains(plan.Issues, i => i.Severity == IssueSeverity.Warning);
        }

        public static void AssertInvariants(AreaPlan plan, float3[] ring, SiteContext site, int minObjects)
        {
            Assert.True(string.IsNullOrEmpty(Errors(plan.Issues)), Errors(plan.Issues));
            Assert.True(plan.Objects.Count >= minObjects, $"{plan.GeneratorId} placed only {plan.Objects.Count} objects");
            AssertInside(plan, ring);
            AssertNothingOnPaths(plan);
            TestKit.AssertSurfacesAccepted(plan);
            AssertTerraformBlended(plan, ring, site);
        }

        public static ReservedSpace SomeReserved(float2 centre)
        {
            var r = new ReservedSpace();
            r.AddDisc(centre, 9f, "Centerpiece");
            r.AddCorridor(centre + new float2(-40, 15), centre + new float2(40, 15), 2.5f, "Other");
            return r;
        }

        public static void AssertSchema(string layer, int minPresets = 4, int maxControls = 40)
        {
            var g = TestKit.Gen(layer);
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey); Need(c.TooltipKey);
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                if (c.Group != "Basics") Need("Placemaker.UI.Group." + c.Group);
                if (lang.TryGetValue(c.LabelKey, out var label)) Assert.True(label.Split(' ').Length <= 3, layer + "." + c.Id + " label too long: " + label);
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g)) { Need(f.LabelKey); Need(f.TooltipKey); }
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing));
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look") && c.Group != Placemaker.Core.Layers.Common.Biomes.BiomeMix.Group), 15, maxControls);   // (Phase 1) look settings not counted; (Biome mix) the mix is one control
            Assert.InRange(g.Presets.Count, minPresets, 6);
            var groups = g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look") && c.Group != Placemaker.Core.Layers.Common.Biomes.BiomeMix.Group).Select(c => c.Group).Where(x => x != "entrances" && x != "shops").Distinct().Count();   // (Phase 1) look settings not counted
            Assert.InRange(groups, 4, 8);
            Assert.Equal("parks", GeneratorGroups.Of(g));
            Assert.Contains(g.Controls, c => c.Id == "style" && c.Kind == ControlKind.Choice && c.Tier == ControlTier.Essential);
            foreach (var p in g.Presets) Assert.True(p.Values.Values.ContainsKey("style"), p.Id + " sets no style");
        }

        public static void AssertAddOnKeys(string layer)
        {
            var g = TestKit.Gen(layer);
            var lang = TestKit.Lang();
            var keys = new List<string> { g.NameKey };
            foreach (var c in g.Controls) { keys.Add(c.LabelKey); keys.Add(c.TooltipKey); keys.Add("Placemaker.UI.Group." + c.Group); foreach (var o in c.Options) keys.Add(o.LabelKey); }
            foreach (var p in g.Presets) keys.Add(p.LabelKey);
            var missing = keys.Where(k => !string.IsNullOrEmpty(k) && !lang.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing));
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);

        public static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static void AssertFast(string layer, ITestOutputHelper output, int budget = 250)
        {
            var gen = TestKit.Gen(layer);
            var ring = TestKit.Rect(250, 200);
            foreach (var p in gen.Presets)
            {
                PlanPreset(layer, p, ring, TestKit.RoadAlongBottom(250));   // JIT / warm-up
                var sw = Stopwatch.StartNew();
                PlanPreset(layer, p, ring, TestKit.RoadAlongBottom(250));
                sw.Stop();
                output?.WriteLine($"{layer}/{p.Id}: {sw.ElapsedMilliseconds} ms");
                Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(budget), $"{layer}/{p.Id} took {sw.ElapsedMilliseconds} ms for 5 ha");
            }
        }

        public static void AssertFeaturesCompose(string layer)
        {
            var gen = TestKit.Gen(layer);
            var features = Features.Of(gen).Select(f => f.Layer).ToArray();
            Assert.NotEmpty(features);
            var ring = TestKit.Rect(250, 200);
            var plan = Compose(With(layer, features), ring, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(Errors(plan.AllIssues())), Errors(plan.AllIssues()));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            foreach (var l in plan.Layers)
            {
                foreach (var o in l.Objects)
                    Assert.True(Poly.Contains(Poly.To2D(ring), o.Position.xz) || Poly.DistanceToBoundary(Poly.To2D(ring), o.Position.xz) < 1.2f, l.GeneratorId + " object outside");
                TestKit.AssertSurfacesAccepted(l);
            }
            // Features never place on the parent's paths.
            var parent = plan.Layers.Single(l => l.GeneratorId == layer);
            foreach (var l in plan.Layers.Where(l => l.GeneratorId != layer && l.GeneratorId != "decals" && l.GeneratorId != "edgeZoning"))
                AssertNothingOnPaths(new AreaPlan { GeneratorId = l.GeneratorId, Objects = l.Objects, Nets = parent.Nets });
        }

        public static void AssertShippedRecipe(string id)
        {
            var recipe = TestKit.LoadRecipe(id + ".json");
            Assert.Equal(id, recipe.Id);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey), recipe.NameKey);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey + ".Description"), recipe.NameKey + ".Description");
            Assert.Equal(id, recipe.Root.Layer);
            var plan = Compose(recipe, TestKit.Rect(220, 160), TestKit.RoadAlongBottom(220));
            Assert.False(plan.HasErrors, Errors(plan.AllIssues()));
        }
    }

    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class AdventurePlaygroundTests
    {
        private const string Id = "adventurePlayground";
        private readonly ITestOutputHelper _out;
        public AdventurePlaygroundTests(ITestOutputHelper output) { _out = output; }

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
                Assert.True(plan.Facts["playZones"] >= 1, preset + "/" + name + ": no play zone");
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
            var ring = TestKit.Rect(160, 140);
            var reserved = TypeChecks09.SomeReserved(new float2(80, 70));
            var plan = TypeChecks09.PlanPreset(Id, Preset(preset), ring, TestKit.RoadAlongBottom(160), 3, reserved);
            Assert.False(plan.HasErrors);
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void FastOnFiveHectares() => TypeChecks09.AssertFast(Id, _out, 450);   // (Biome mix) it plans a landscape between its zones now (forest, rocks, glade: ~150 ms of 5 ha)

        [Fact]
        public void FeaturesCompose() => TypeChecks09.AssertFeaturesCompose(Id);

        [Fact]
        public void SchemaIsDeepTidyAndLocalised()
        {
            TypeChecks09.AssertSchema(Id, 5);
            TypeChecks09.AssertAddOnKeys("kickabout");
        }

        [Fact]
        public void ShippedRecipeComposes() => TypeChecks09.AssertShippedRecipe(Id);

        [Fact]
        public void PresetsLookDifferent()
        {
            var ring = TestKit.Rect(200, 150);
            AreaPlan P(string id) => TypeChecks09.PlanPreset(Id, Preset(id), ring, TestKit.RoadAlongBottom(200));
            int Cat(AreaPlan p, string cat) => p.Objects.Count(o => o.Category == cat);

            var adventure = P("adventure");
            Assert.True(Cat(adventure, AssetCategories.PlaySwing) > 0 && Cat(adventure, AssetCategories.PlaySlide) > 0 && Cat(adventure, AssetCategories.PlayClimb) > 0, $"swing {Cat(adventure, AssetCategories.PlaySwing)} slide {Cat(adventure, AssetCategories.PlaySlide)} climb {Cat(adventure, AssetCategories.PlayClimb)}");
            Assert.True(Cat(adventure, AssetCategories.PlayToddler) > 0, "toddler items");
            Assert.True(adventure.Facts["playZones"] >= 4, "toddler, junior, teen, water, nature zones");

            var toddler = P("toddler");
            Assert.True(Cat(toddler, AssetCategories.PlayToddler) >= 4, "toddler items " + Cat(toddler, AssetCategories.PlayToddler));
            Assert.Equal(0, Cat(toddler, AssetCategories.PlaySwing));
            Assert.Contains(toddler.Nets, n => n.Role == NetRole.Fence);

            var water = P("water");
            // Art pass (a1): small fountains on the splash pad, spaced by their real footprint; never the agricultural "water.jets".
            Assert.True(Cat(water, AssetCategories.CenterpieceFountain) >= 3, "splash fountains " + Cat(water, AssetCategories.CenterpieceFountain));
            Assert.Equal(0, Cat(water, AssetCategories.WaterJets));
            Assert.True(water.Surfaces.Count(s => s.Role == SurfaceRole.Water) >= 3, "splash pad plus stepping pools");

            var nature = P("nature");
            Assert.True(Cat(nature, AssetCategories.PlayNature) >= 3, "logs (whole ~10 m trunks) " + Cat(nature, AssetCategories.PlayNature));
            Assert.True(Cat(nature, AssetCategories.Rock) >= 6, "rocks " + Cat(nature, AssetCategories.Rock));
            Assert.NotEmpty(nature.Terraform);
            Assert.True(nature.Objects.Count(o => o.Role == ObjectRole.Tree) > adventure.Objects.Count(o => o.Role == ObjectRole.Tree), "nature trees");

            var jungle = P("jungle");
            Assert.True(Cat(jungle, AssetCategories.PlayClimb) > Cat(adventure, AssetCategories.PlayClimb), $"climb {Cat(jungle, AssetCategories.PlayClimb)} vs {Cat(adventure, AssetCategories.PlayClimb)}");
            // (parks makeover) the wild places shape the ground; the jungle itself does not
            var jv = Preset("jungle").Values.Clone(); jv.Values["moods"] = false; BiomeMixTests.LawnOnly(jv);
            Assert.Empty(TestKit.Gen(Id).Plan(TestKit.Input(ring, jv, 7, null, TestKit.RoadAlongBottom(200)), System.Threading.CancellationToken.None).Terraform);
        }

        [Fact]
        public void EquipmentKeepsTheSafetyClearance()
        {
            var v = Preset("adventure").Values.Clone();
            v.Values["clearance"] = 3.0;
            var plan = TestKit.Gen(Id).Plan(TestKit.Input(TestKit.Rect(200, 150), v, 4, null, TestKit.RoadAlongBottom(200)), CancellationToken.None);
            var play = plan.Objects.Where(o => o.Category != null && o.Category.StartsWith("play.") && o.Category != AssetCategories.PlayNature).Select(o => o.Position.xz).ToList();
            Assert.True(play.Count > 6);
            for (int i = 0; i < play.Count; i++)
                for (int j = i + 1; j < play.Count; j++)
                    Assert.True(math.distance(play[i], play[j]) >= 2f * 1.6f + 1.5f - 0.05f, $"equipment {play[i]} and {play[j]} too close");
        }

        [Fact]
        public void SightlinesStayFreeOfTrees()
        {
            var v = Preset("adventure").Values.Clone();
            v.Values["treeDensity"] = 150.0;
            v.Values["shade"] = "none";
            var plan = TestKit.Gen(Id).Plan(TestKit.Input(TestKit.Rect(200, 150), v, 9, null, TestKit.RoadAlongBottom(200)), CancellationToken.None);
            Assert.True(plan.Facts["sightlines"] >= 3);
            var off = v.Clone(); off.Values["sightlines"] = false;
            var planOff = TestKit.Gen(Id).Plan(TestKit.Input(TestKit.Rect(200, 150), off, 9, null, TestKit.RoadAlongBottom(200)), CancellationToken.None);
            Assert.True(planOff.Objects.Count(o => o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush) >=
                        plan.Objects.Count(o => o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush));
        }

        [Fact]
        public void MissingPlayAssetsDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.Playground] = 0, [AssetCategories.PlayClimb] = 0, [AssetCategories.PlaySwing] = 0, [AssetCategories.PlaySlide] = 0,
                [AssetCategories.PlayToddler] = 0, [AssetCategories.PlayNature] = 0, [AssetCategories.WaterJets] = 0, [AssetCategories.CenterpieceFountain] = 0,
                [AssetCategories.SportsFitness] = 0, [AssetCategories.SurfaceSand] = 0, [AssetCategories.SurfaceWater] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var p in TestKit.Gen(Id).Presets)
                {
                    var plan = TypeChecks09.PlanPreset(Id, p, TestKit.Rect(200, 150), TestKit.RoadAlongBottom(200));
                    Assert.False(plan.HasErrors, p.Id);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.DoesNotContain(plan.Surfaces, s => none.ContainsKey(s.Category ?? ""));
                    foreach (var grp in plan.Issues.Where(i => i.Key == Keys.NoAssetsIssue).GroupBy(i => (string)i.Args[0])) Assert.Single(grp);
                }
        }

        [Theory]
        [InlineData("ClimbingFrame01", AssetCategories.PlayClimb)]
        [InlineData("JungleGym02", AssetCategories.PlayClimb)]
        [InlineData("PlaygroundSwing01", AssetCategories.PlaySwing)]
        [InlineData("PlaygroundSlide01", AssetCategories.PlaySlide)]
        [InlineData("SpringRider01", AssetCategories.PlayToddler)]
        [InlineData("Sandbox01", AssetCategories.PlayToddler)]
        [InlineData("BalanceLog01", AssetCategories.PlayNature)]
        [InlineData("TreeStump02", AssetCategories.PlayNature)]
        public void PlayClassification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            var cats = AssetHeuristics.Classify(name, t);
            Assert.Contains(category, cats);
            if (category != AssetCategories.PlayNature) Assert.Contains(AssetCategories.Playground, cats);
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name);
        }

        [Fact]
        public void PlayRulesLeaveOtherThingsAlone()
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.DoesNotContain(AssetCategories.PlayNature, AssetHeuristics.Classify("CompanyLogoSign01", t));
            Assert.DoesNotContain(AssetCategories.PlaySlide, AssetHeuristics.Classify("LandslideRocks01", t));
            Assert.DoesNotContain(AssetCategories.PlayClimb, AssetHeuristics.Classify("DogParkClimbRamp", t));
            Assert.Contains(AssetCategories.Tree, AssetHeuristics.Classify("OakTree01", t | AssetTraits.Plant | AssetTraits.Tree));
        }
    }
}
