// Type wave builder 13: farmstead (group "farms") + the shared farm add-ons (apiary, farm stand).
// FarmTypeChecks holds the definition-of-done checks shared with OrchardParkTests.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    internal static class FarmTypeChecks
    {
        public static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        public static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        /// <summary>1 ha square and a 6 ha L-shape (concave).</summary>
        public static readonly float3[] OneHa = TestKit.Rect(100, 100);
        public static readonly float3[] SixHaL = TestKit.LShape(293f);
        public static readonly float3[] FiveHa = TestKit.Rect(250, 200);

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

        /// <summary>Reserved space in the middle of the ring: a disc and a corridor crossing it.</summary>
        public static ReservedSpace MiddleReserved(float3[] ring)
        {
            var r2 = Poly.To2D(ring);
            var c = LayerRegion.From(r2).DeepestPoint(out _, null, 16);
            var res = new ReservedSpace();
            res.AddDisc(c, 8f, "test");
            res.AddCorridor(c + new float2(-40, 15), c + new float2(40, 15), 2f, "test");
            return res;
        }

        public static void AssertClearOfReserved(AreaPlan plan, ReservedSpace reserved)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal) continue;
                foreach (var d in reserved.Discs)
                    Assert.True(math.distance(d.Center, o.Position.xz) >= d.Radius, $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} inside reserved disc");
                foreach (var c in reserved.Corridors)
                    Assert.True(math.sqrt(SegmentIndex.DistSq(o.Position.xz, c.A, c.B)) >= c.HalfWidth,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} inside reserved corridor");
            }
        }

        /// <summary>Every preset on 1 ha and 6 ha L: no errors, inside, off paths, clear of Reserved, surfaces accepted.</summary>
        public static void PresetsPlanEverywhere(string id)
        {
            var gen = TestKit.Gen(id);
            Assert.InRange(gen.Presets.Count, 4, 8);
            foreach (var ring in new[] { OneHa, SixHaL })
                foreach (var p in gen.Presets)
                {
                    var input = TestKit.Input(ring, p.Values, 7, null, TestKit.RoadAlongBottom(ring == OneHa ? 100 : 293));
                    input.Reserved = MiddleReserved(ring);
                    var plan = gen.Plan(input, CancellationToken.None);
                    Assert.False(plan.HasErrors, id + "/" + p.Id + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                    Assert.True(plan.Objects.Count > 10, id + "/" + p.Id + " placed only " + plan.Objects.Count);
                    AssertInside(plan, ring);
                    AssertNothingOnPaths(plan);
                    AssertClearOfReserved(plan, input.Reserved);
                    TestKit.AssertSurfacesAccepted(plan);
                    foreach (var a in plan.Areas) Assert.True(SurfaceAcceptance.IsAccepted(Poly.To2D(a.Ring)));
                }
        }

        public static void PresetsLookDifferent(string id)
        {
            var gen = TestKit.Gen(id);
            var prints = new HashSet<string>();
            foreach (var p in gen.Presets)
            {
                var plan = gen.Plan(TestKit.Input(FiveHa, p.Values, 3, null, TestKit.RoadAlongBottom(250)), CancellationToken.None);
                // Signature: counts by category/role, not positions (presets must differ in content, not jitter).
                var sig = string.Join("|", plan.Objects.GroupBy(o => o.Role + "/" + o.Category).OrderBy(g => g.Key).Select(g => g.Key + "=" + (g.Count() / 5)));
                Assert.True(prints.Add(sig), id + ": preset " + p.Id + " looks like another preset");
            }
        }

        public static void Deterministic(string id)
        {
            foreach (var ring in new[] { OneHa, SixHaL })
            {
                var a = Compose(With(id), ring, TestKit.RoadAlongBottom(300));
                var b = Compose(With(id), ring, TestKit.RoadAlongBottom(300));
                Assert.Equal(TestKit.Fingerprint(a.Layers[0]), TestKit.Fingerprint(b.Layers[0]));
            }
        }

        public static void Fast(string id, ITestOutputHelper output)
        {
            var gen = TestKit.Gen(id);
            Compose(With(id), FiveHa, TestKit.RoadAlongBottom(250));   // JIT
            long worst = 0;
            foreach (var p in gen.Presets)
            {
                var r = With(id);
                foreach (var kv in p.Values.Values) r.Root.Settings.Values[kv.Key] = kv.Value;
                var sw = Stopwatch.StartNew();
                Compose(r, FiveHa, TestKit.RoadAlongBottom(250));
                sw.Stop();
                output.WriteLine($"{id}/{p.Id}: {sw.ElapsedMilliseconds} ms");
                worst = Math.Max(worst, sw.ElapsedMilliseconds);
            }
            // (P2-3) the farmstead plans a landscape (biome mix, a ravine, terraces): the adventure playground's 450 ms
            Assert.True(worst < TimingBudget.Ms(id == "farmstead" ? 450 : 250), $"{id} took {worst} ms for 5 ha");
        }

        /// <summary>Terraform never leaves a step at the outline: cells touching it keep the original height.</summary>
        public static void TerraformBlendedAtEdge(string id, params (string k, object v)[] settings)
        {
            var gen = TestKit.Gen(id);
            var site = TestKit.RoadAlongBottom(250);
            site.HeightAt = p => 0.06f * p.x + 0.03f * p.y + 2f * math.sin(p.x / 40f);
            var bag = new SettingsBag();
            foreach (var (k, v) in settings) bag.Values[k] = v;
            var ring = TestKit.Rect(250, 200);
            var plan = gen.Plan(TestKit.Input(ring, bag, 5, null, site), CancellationToken.None);
            Assert.False(plan.HasErrors);
            var r2 = Poly.To2D(ring);
            int checkedCells = 0;
            foreach (var t in plan.Terraform)
            {
                if (!float.IsNaN(t.WaterLevel)) continue;   // a pond basin sits well inside
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float d = Poly.DistanceToBoundary(r2, c);
                if (d > 8f) continue;
                checkedCells++;
                Assert.True(math.abs(t.TargetHeight - site.HeightAt(c)) < 0.2f * math.max(1f, d), $"{id}: terraform at {c} ({d:0.0} m from the edge) changes the ground by {t.TargetHeight - site.HeightAt(c):0.00} m");
            }
            foreach (var t in plan.Terraform)
                foreach (var p in t.Ring)
                    Assert.True(Poly.Contains(r2, p.xz) || Poly.DistanceToBoundary(r2, p.xz) < 0.5f, id + ": terraform outside the area");
        }

        public static void FeaturesCompose(string id)
        {
            var gen = GeneratorRegistry.Get(id);
            var features = Phase1Looks.AllFeatures(gen);   // (Phase 1) add-ons and look settings
            Assert.True(features.Length >= 4, id + " needs features");
            foreach (var ring in new[] { FiveHa, SixHaL })
            {
                var plan = Compose(With(id, features), ring, TestKit.RoadAlongBottom(300));
                Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
                foreach (var l in plan.Layers) AssertInside(l, ring, 1.2f);
                foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
                // Feature add-ons respect what the type built.
                var host = plan.Layers[0];
                var reserved = new ReservedSpace();
                Composer.Reserve(host, reserved);
                foreach (var l in plan.Layers.Skip(1))
                    foreach (var o in l.Objects)
                    {
                        if (o.Role == ObjectRole.Decal) continue;
                        foreach (var c in reserved.Corridors)
                            if (c.Tag == nameof(NetRole.Pathway))
                                Assert.True(math.sqrt(SegmentIndex.DistSq(o.Position.xz, c.A, c.B)) >= c.HalfWidth, $"{l.GeneratorId} {o.Role} on a path of {id}");
                    }
            }
        }

        public static void EveryKeyHasEnglishText(string id, string folder, bool recipe = true)
        {
            var lang = TestKit.Lang();
            var g = TestKit.Gen(id);
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey);
                Need(c.TooltipKey);
                Need("Placemaker.UI.Group." + c.Group);
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                Assert.True(lang.TryGetValue(c.LabelKey, out var label) && label.Split(' ').Length <= 3, c.LabelKey + " label too long");
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g)) { Need(f.LabelKey); Need(f.TooltipKey); }
            var root = TestKit.RepoRoot();
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Core", "Layers", "Types", folder), "*.cs"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                    Need(m.Groups[1].Value);
            foreach (var f in typeof(AssetCategories).GetFields()) Need(Keys.Category((string)f.GetValue(null)));
            if (recipe)
            {
                var r = TestKit.LoadRecipe(id + ".json");
                Need(r.NameKey);
                Assert.Equal(id, r.Root.Layer);
            }
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        public static void RecipeIconExists(string id)
        {
            var json = File.ReadAllText(Path.Combine(TestKit.RepoRoot(), "Recipes", id + ".json"));
            var icon = Regex.Match(json, "\"icon\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            Assert.Equal(TestKit.Gen(id).Icon, icon);
            var game = @"C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\UI";
            if (Directory.Exists(game)) Assert.True(File.Exists(Path.Combine(game, icon.Replace('/', Path.DirectorySeparatorChar))), "icon " + icon + " not in the game UI");
            Assert.Contains("descriptionKey", json);
        }
    }

    [Collection(TimingCollection.Name)]
    public class FarmsteadTests
    {
        private const string Id = "farmstead";
        private readonly ITestOutputHelper _out;
        public FarmsteadTests(ITestOutputHelper output) { _out = output; }

        [Fact] public void PresetsPlanOnOneAndSixHectares() => FarmTypeChecks.PresetsPlanEverywhere(Id);
        [Fact] public void PresetsLookDifferent() => FarmTypeChecks.PresetsLookDifferent(Id);
        [Fact] public void Deterministic() => FarmTypeChecks.Deterministic(Id);
        [Fact] public void FastOnFiveHectares() => FarmTypeChecks.Fast(Id, _out);
        [Fact] public void FeaturesCompose() => FarmTypeChecks.FeaturesCompose(Id);
        [Fact] public void EveryKeyHasEnglishText() => FarmTypeChecks.EveryKeyHasEnglishText(Id, "Farmstead");
        [Fact] public void RecipeShipsWithIcon() => FarmTypeChecks.RecipeIconExists(Id);

        [Fact]
        public void TerraformBlendedAtEdge()
        {
            FarmTypeChecks.TerraformBlendedAtEdge(Id, ("yardAt", "corner"), ("pond", true));
            FarmTypeChecks.TerraformBlendedAtEdge(Id, ("yardAt", "road"), ("yardSize", 20.0));
        }

        [Fact]
        public void IsInTheFarmsGroupWithDeepTidyOptions()
        {
            var g = TestKit.Gen(Id);
            Assert.Equal("farms", GeneratorGroups.Of(g));
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look") && c.Group != Placemaker.Core.Layers.Common.Biomes.BiomeMix.Group), 15, 48);   // (Phase 1) look settings, (P2-3) the landscape mix not counted
            Assert.InRange(g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look") && c.Group != Placemaker.Core.Layers.Common.Biomes.BiomeMix.Group).Select(c => c.Group).Distinct().Count(), 4, 9);   // (Phase 1) look settings not counted
            Assert.Contains(g.Controls, c => c.Id == "subtype" && c.Kind == ControlKind.Choice && c.Tier == ControlTier.Essential);
            Assert.Equal("identity", g.Controls[0].Group);
        }

        private static AreaPlan PlanPreset(string preset, float3[] ring = null)
        {
            var g = TestKit.Gen(Id);
            var p = g.Presets.Single(x => x.Id == preset);
            ring = ring ?? FarmTypeChecks.FiveHa;
            return g.Plan(TestKit.Input(ring, p.Values, 9, null, TestKit.RoadAlongBottom(250)), CancellationToken.None);
        }

        [Fact]
        public void FarmHasYardFieldsTracksAndHedges()
        {
            var plan = PlanPreset("mixed");
            Assert.Contains(plan.Regions, r => r.Tags.Contains(Placemaker.Core.Layers.Types.Farmstead.FarmsteadLayer.YardTag));
            Assert.True(plan.Facts["fields"] >= 3, "fields " + plan.Facts["fields"]);
            Assert.True(plan.Facts["arableFields"] >= 1 && plan.Facts["pastureFields"] >= 1);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.FarmBarn);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.FarmSilo);
            Assert.True(plan.Facts["hedgeItems"] > 50, "hedge items " + plan.Facts["hedgeItems"]);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway);
            // (P2-3) A summer farm: growing crops are the crop surface; bare soil (spring, autumn, fallow) is paint.
            Assert.True(plan.Facts["fieldSurfaces"] >= 2, "field surfaces " + plan.Facts["fieldSurfaces"]);
            Assert.Contains(plan.Surfaces, s => (s.Category ?? "").StartsWith(AssetCategories.SurfaceCrop));
            Assert.True(plan.Facts["cropItems"] <= 600, "crop accents " + plan.Facts["cropItems"]);
            Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
            // Fields are places: features keep out.
            Assert.Contains(plan.Regions, r => r.Tags.Contains(Placemaker.Core.Layers.Types.Farmstead.FarmsteadLayer.FieldPlace));
        }

        [Fact]
        public void RowsFollowTheChosenDirection()
        {
            var g = TestKit.Gen(Id);
            float Angle(string mode)
            {
                var bag = new SettingsBag();
                bag.Values["rowAngle"] = mode; bag.Values["arablePct"] = 100.0; bag.Values["fieldSize"] = 1.0;
                var plan = g.Plan(TestKit.Input(FarmTypeChecks.FiveHa, bag, 4, null, TestKit.RoadAlongBottom(250)), CancellationToken.None);
                // Fields are surfaces (world-mapped textures): the rows show as wheel-rut decals laid along them.
                var ruts = plan.Objects.Where(o => o.Category == AssetCategories.DecalFarm).ToList();
                Assert.NotEmpty(ruts);
                return math.abs(math.sin(ruts[ruts.Count / 2].Yaw));
            }
            // The 250 × 200 rect's long axis is x: along → |x| ≈ 1, across → ≈ 0.
            Assert.True(Angle("along") > 0.9f);
            Assert.True(Angle("across") < 0.1f);
        }

        [Fact]
        public void ArableHasNoPaddocksAndRanchIsFencedPasture()
        {
            var arable = PlanPreset("arable");
            Assert.Equal(0.0, arable.Facts["pastureFields"]);
            Assert.DoesNotContain(arable.Objects, o => o.Role == ObjectRole.AnimalSpawner);
            var ranch = PlanPreset("ranch");
            Assert.Equal(0.0, ranch.Facts["arableFields"]);
            Assert.DoesNotContain(ranch.Objects, o => o.Role == ObjectRole.AnimalSpawner);   // (Phase 1) no animal spawners anywhere (the user)
            Assert.True(ranch.Nets.Count(n => n.Role == NetRole.Fence) > arable.Nets.Count(n => n.Role == NetRole.Fence));
            Assert.True(TestKit.Stat(ranch, "Placemaker.Stat.Paddocks") >= 2);
        }

        [Fact]
        public void PondSitsAtTheLowestGround()
        {
            var g = TestKit.Gen(Id);
            var site = TestKit.RoadAlongBottom(250);
            site.HeightAt = p => 0.05f * p.x;   // lowest along x = 0
            var bag = new SettingsBag();
            bag.Values["pond"] = true; bag.Values["pondAt"] = "lowest"; bag.Values["yardAt"] = "corner";
            var plan = g.Plan(TestKit.Input(FarmTypeChecks.FiveHa, bag, 2, null, site), CancellationToken.None);
            var lake = plan.Terraform.Single(t => !float.IsNaN(t.WaterLevel));
            Assert.True(Poly.Centroid(Poly.To2D(lake.Ring)).x < 125f, "pond not on the low side");
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Park.Issue.LakePermanent");
        }

        [Fact]
        public void MissingFarmAssetsDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.FarmBarn] = 0, [AssetCategories.FarmSilo] = 0, [AssetCategories.FarmMachinery] = 0, [AssetCategories.FarmHay] = 0,
                [AssetCategories.FarmTrough] = 0, [AssetCategories.FarmWindpump] = 0, [AssetCategories.FieldCrop] = 0, [AssetCategories.GardenCrop] = 0,
                [AssetCategories.GardenShed] = 0, [AssetCategories.Shelter] = 0, [AssetCategories.Beehive] = 0, [AssetCategories.MarketStall] = 0,
                [AssetCategories.Kiosk] = 0, [AssetCategories.FruitCrate] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                var plan = FarmTypeChecks.Compose(FarmTypeChecks.With(Id, "apiary", "farmStand"), FarmTypeChecks.FiveHa, TestKit.RoadAlongBottom(250));
                Assert.True(string.IsNullOrEmpty(FarmTypeChecks.Errors(plan)), FarmTypeChecks.Errors(plan));
                Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                foreach (var l in plan.Layers) Assert.DoesNotContain(l.Objects, o => none.ContainsKey(o.Category ?? ""));
                Assert.True(plan.Layers[0].Facts["fields"] > 0);   // still a farm: fields, hedges, tracks
            }
        }

        [Fact]
        public void FarmAssetsAreClassifiedByName()
        {
            List<string> Cat(string name, AssetTraits t = AssetTraits.StaticObject | AssetTraits.HasMesh)
            {
                var r = new List<string>();
                CategoryRules.Classify(name.ToLowerInvariant(), t, r);
                return r;
            }
            Assert.Contains(AssetCategories.FarmSilo, Cat("FarmSilo01"));
            Assert.Contains(AssetCategories.FarmBarn, Cat("RedBarn02"));
            Assert.Contains(AssetCategories.FarmMachinery, Cat("Tractor01"));
            Assert.DoesNotContain(AssetCategories.FarmMachinery, Cat("SnowPlow01"));
            Assert.Contains(AssetCategories.FarmHay, Cat("HayBale01"));
            Assert.Contains(AssetCategories.Beehive, Cat("Beehive01"));
            var tree = Cat("AppleTree01", AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.Plant | AssetTraits.Tree);
            Assert.Contains(AssetCategories.FruitTree, tree);
            Assert.Contains(AssetCategories.Tree, tree);
            Assert.DoesNotContain(AssetCategories.FruitTree, Cat("OakTree01", AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.Plant | AssetTraits.Tree));
            Assert.Equal(false, CategoryRules.AutoEligible(AssetCategories.FarmBarn, "barnadvert01", AssetTraits.StaticObject));
        }

        [Fact]
        public void ApiaryAndFarmStandPlaceIntoFreeSpace()
        {
            foreach (var id in new[] { "apiary", "farmStand" })
            {
                var g = TestKit.Gen(id);
                Assert.Equal(LayerKind.AddOn, g.Kind);
                foreach (var p in g.Presets)
                {
                    var ring = TestKit.Rect(80, 60);
                    var input = TestKit.Input(ring, p.Values, 3, null, TestKit.RoadAlongBottom(80));
                    input.Reserved = FarmTypeChecks.MiddleReserved(ring);
                    var plan = g.Plan(input, CancellationToken.None);
                    Assert.False(plan.HasErrors);
                    Assert.NotEmpty(plan.Objects);
                    FarmTypeChecks.AssertInside(plan, ring);
                    FarmTypeChecks.AssertClearOfReserved(plan, input.Reserved);
                }
                FarmTypeChecks.EveryKeyHasEnglishText(id, "Farmstead", false);
            }
        }
    }
}
