// Type wave: scrapyard (industrial). Every preset plans on 1 ha and 6 ha L-shapes, deterministic,
// inside the area, clear of paths and Reserved, accepted surfaces, terraform blended at the edge,
// fast, features compose, every key in en-US, subtypes visibly different, graceful degradation,
// and the category name rules. YardTestKit is shared with LandfillTests.

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
    /// <summary>Shared checks for the industrial yard types.</summary>
    public static class YardTestKit
    {
        public static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>1 ha square with a road along the bottom.</summary>
        public static readonly float3[] OneHa = TestKit.Rect(100, 100);

        /// <summary>6 ha L-shape (size² · (1 − 0.55²) = 60 000 m²).</summary>
        public static readonly float3[] SixHaL = TestKit.LShape(293.3f);

        public static readonly float3[] FiveHa = TestKit.Rect(250, 200);

        public static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        public static string Errors(IEnumerable<PlanIssue> issues) =>
            string.Join("; ", issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        public static AreaPlan PlanPreset(string id, string preset, float3[] ring, SiteContext site = null, ReservedSpace reserved = null, uint seed = 7)
        {
            var gen = TestKit.Gen(id);
            var p = gen.Presets.Single(x => x.Id == preset);
            var input = TestKit.Input(ring, p.Values.Clone(), seed, null, site ?? TestKit.RoadAlongBottom(ring.Max(v => v.x)));
            if (reserved != null) input.Reserved = reserved;
            return gen.Plan(input, CancellationToken.None);
        }

        public static void AssertInside(AreaPlan plan, float3[] ring)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < 0.05f,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {n.Role} end {p.xz} outside");
        }

        /// <summary>No object (decals excepted) on a path of the same plan.</summary>
        public static void AssertNothingOnPaths(AreaPlan plan)
        {
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal) continue;
                foreach (var n in paths)
                {
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f, $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        /// <summary>
        /// Every terraform cell is within the max slope of the original ground, measured from the
        /// outline (no cliff at the border), and cells at the outline barely change.
        /// </summary>
        public static void AssertTerraformBlended(AreaPlan plan, float3[] ring, Func<float2, float> height, float maxSlopePct)
        {
            var r = Poly.To2D(ring);
            float slope = maxSlopePct / 100f;
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent, "terraform must be flagged permanent");
                var cell = Poly.To2D(t.Ring);
                var c = Poly.Centroid(cell);
                float half = 0f;
                foreach (var v in cell) half = math.max(half, math.distance(v, c));
                float d = Poly.DistanceToBoundary(r, c);
                float change = math.abs(t.TargetHeight - height(c));
                Assert.True(change <= slope * (d + half) + 0.3f, $"{plan.GeneratorId}: cell at {c} changes {change:0.0} m only {d:0.0} m from the outline");
            }
            if (plan.Terraform.Count > 0) Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey);
        }

        public static void AssertSound(AreaPlan plan, float3[] ring, float maxSlopePct, Func<float2, float> height = null)
        {
            Assert.True(string.IsNullOrEmpty(Errors(plan.Issues)), Errors(plan.Issues));
            AssertInside(plan, ring);
            AssertNothingOnPaths(plan);
            TestKit.AssertSurfacesAccepted(plan);
            AssertTerraformBlended(plan, ring, height ?? (_ => 0f), maxSlopePct);
            foreach (var o in plan.Objects)
                if (o.Role == ObjectRole.Furniture) Assert.False(string.IsNullOrEmpty(o.Category), plan.GeneratorId + ": furniture without a category");
        }

        /// <summary>A path corridor across the area and a disc — what a parent/sibling layer might have reserved.</summary>
        public static ReservedSpace Reserved(float w, float h)
        {
            var r = new ReservedSpace();
            r.AddCorridor(new float2(0, h * 0.5f), new float2(w, h * 0.55f), 2f, nameof(NetRole.Pathway));
            r.AddDisc(new float2(w * 0.3f, h * 0.3f), 9f, "Centerpiece");
            r.AddDisc(new float2(w * 0.7f, h * 0.75f), 6f, "Tree");
            return r;
        }

        public static void AssertKeys(string id, IEnumerable<string> sourceDirs)
        {
            var lang = TestKit.Lang();
            var g = TestKit.Gen(id);
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey); Need(c.TooltipKey);
                if (c.Group != "Basics") Need("Placemaker.UI.Group." + c.Group);
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                if (lang.TryGetValue(c.LabelKey, out var label)) Assert.True(label.Split(' ').Length <= 3, id + "." + c.Id + " label too long: " + label);
                if (!string.IsNullOrEmpty(c.VisibleWhen)) Assert.Contains(g.Controls, o => o.Id == c.VisibleWhen.Split('=')[0]);
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g))
            {
                Need(f.LabelKey); Need(f.TooltipKey);
                var fg = TestKit.Gen(f.Layer);
                Need(fg.NameKey);
                foreach (var c in fg.Controls) { Need(c.LabelKey); Need(c.TooltipKey); foreach (var o in c.Options) Need(o.LabelKey); }
                foreach (var p in fg.Presets) Need(p.LabelKey);
            }
            var root = TestKit.RepoRoot();
            foreach (var dir in sourceDirs)
                foreach (var file in Directory.GetFiles(Path.Combine(root, "Core", "Layers", "Types", dir), "*.cs"))
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                        Need(m.Groups[1].Value);
            foreach (var f in typeof(AssetCategories).GetFields())
            {
                var cat = (string)f.GetValue(null);
                if (cat.StartsWith("industrial.", StringComparison.Ordinal)) Need(Keys.Category(cat));
            }
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        public static void AssertSchemaShape(string id, string group)
        {
            var g = TestKit.Gen(id);
            Assert.Equal(group, GeneratorGroups.Of(g));
            Assert.Equal(LayerKind.Fill, g.Kind);
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 40);   // (Phase 1) look settings not counted
            int groups = g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Where(x => !Placemaker.Core.Layers.Common.GroundSection.IsGround(x)).Distinct().Count();   // (Phase 1) look settings and the shared Ground section (terrain paint, ground cover, shape) not counted
            Assert.InRange(groups, 4, 8);
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.Equal(g.Controls.Count, g.Controls.Select(c => c.Id).Distinct().Count());
            Assert.Equal(ControlTier.Essential, g.Controls[0].Tier);
            var style = g.Controls.Single(c => c.Id == "style");
            Assert.Equal(ControlKind.Choice, style.Kind);
            // Every preset is a different subtype.
            Assert.Equal(g.Presets.Count, g.Presets.Select(p => p.Values.S("style", "")).Distinct().Count());
            var recipe = TestKit.LoadRecipe(id + ".json");
            Assert.Equal(id, recipe.Root.Layer);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey));
        }

        public static int Count(AreaPlan plan, string category) => plan.Objects.Count(o => o.Category == category);
    }

    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class ScrapyardTests
    {
        private readonly ITestOutputHelper _out;
        public ScrapyardTests(ITestOutputHelper output) { _out = output; }

        private const string Id = "scrapyard";
        private const float Slope = 45f;
        public static IEnumerable<object[]> PresetIds() => TestKit.Gen(Id).Presets.Select(p => new object[] { p.Id });

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectare(string preset)
        {
            var plan = YardTestKit.PlanPreset(Id, preset, YardTestKit.OneHa);
            YardTestKit.AssertSound(plan, YardTestKit.OneHa, Slope);
            Assert.True(plan.Objects.Count(o => o.Role != ObjectRole.Decal) > 25, preset + " placed only " + plan.Objects.Count + " objects on 1 ha");
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansSixHectareLShape(string preset)
        {
            var plan = YardTestKit.PlanPreset(Id, preset, YardTestKit.SixHaL);
            YardTestKit.AssertSound(plan, YardTestKit.SixHaL, Slope);
            Assert.True(plan.Objects.Count(o => o.Role != ObjectRole.Decal) > 150, preset + " placed only " + plan.Objects.Count + " objects on 6 ha");
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
        public void LevelsASlopedSiteBlendedAtTheOutline()
        {
            var site = TestKit.RoadAlongBottom(100);
            site.HeightAt = p => 0.08f * p.x + 0.03f * p.y;
            var plan = YardTestKit.PlanPreset(Id, "scrapyard", YardTestKit.OneHa, site);
            Assert.NotEmpty(plan.Terraform);
            YardTestKit.AssertSound(plan, YardTestKit.OneHa, Slope, site.HeightAt);
        }

        [Fact]
        public void IrregularOutlineStillPlans()
        {
            var ring = TestKit.Blob(90, 40, 3);
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
            Assert.Contains("junkPiles", features);
            Assert.Contains("skipRow", features);
            var plan = YardTestKit.Compose(YardTestKit.With(Id, features), YardTestKit.FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(YardTestKit.Errors(plan.AllIssues())), YardTestKit.Errors(plan.AllIssues()));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            var junk = plan.Layers.Single(l => l.GeneratorId == "junkPiles");
            Assert.Contains(junk.Objects, o => o.Category == AssetCategories.ScrapHeap || o.Category == AssetCategories.ScrapTire);
            // Feature layers stay clear of what the yard placed (the composer reserves it).
            var yard = plan.Layers.Single(l => l.GeneratorId == Id);
            foreach (var l in plan.Layers.Where(l => l.GeneratorId == "junkPiles" || l.GeneratorId == "skipRow"))
                foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal))
                    foreach (var n in yard.Nets.Where(n => n.Role == NetRole.Pathway))
                        Assert.True(TestKit.DistanceToCurve(o.Position.xz, n) > 1.5f);
        }

        [Fact]
        public void SchemaKeysAndRecipe()
        {
            YardTestKit.AssertSchemaShape(Id, "industrial");
            YardTestKit.AssertKeys(Id, new[] { "Scrapyard" });
            YardTestKit.AssertKeys("junkPiles", new[] { "Scrapyard" });
        }

        [Fact]
        public void SubtypesAreVisiblyDifferent()
        {
            AreaPlan P(string preset) => YardTestKit.PlanPreset(Id, preset, YardTestKit.FiveHa);
            var yard = P("scrapyard");
            Assert.True(YardTestKit.Count(yard, AssetCategories.ScrapBarrier) > 30, "scrapyard: bay walls");
            Assert.True(TestKit.Stat(yard, "Placemaker.Stat.Bays") >= 6, "scrapyard: bays");
            Assert.True(yard.Facts["terrainMaxRaise"] > 2.5, "scrapyard: the scrap mountain stands on a mound");
            Assert.True(YardTestKit.Count(yard, AssetCategories.ScrapMachine) >= 1);

            var depot = P("containers");
            Assert.True(YardTestKit.Count(depot, AssetCategories.ScrapContainer) > 80, "container depot: blocks");
            Assert.True(YardTestKit.Count(depot, AssetCategories.ScrapContainer) > 5 * YardTestKit.Count(yard, AssetCategories.ScrapContainer));

            var cars = P("cars");
            Assert.True(YardTestKit.Count(cars, AssetCategories.ScrapWreck) > 120, "car graveyard: rows of wrecks");
            Assert.Contains(cars.Nets, n => n.Role == NetRole.Pathway);
            Assert.True(TestKit.Count(cars, ObjectRole.Tree) > TestKit.Count(depot, ObjectRole.Tree), "car graveyard is overgrown");

            var tires = P("tires");
            Assert.True(YardTestKit.Count(tires, AssetCategories.ScrapTire) > 150, "tyre dump: tyre mountains");
            Assert.True(tires.Nets.Count(n => n.Role == NetRole.Fence) != yard.Nets.Count(n => n.Role == NetRole.Fence), "tyre dump has a broken fence");

            var salvage = P("salvage");
            Assert.True(YardTestKit.Count(salvage, AssetCategories.ScrapPallet) + YardTestKit.Count(salvage, AssetCategories.ScrapBarrel) > 80, "salvage: racks and rows");
            Assert.True(salvage.Nets.Count(n => n.Role == NetRole.Pathway) >= 4, "salvage: walkable aisles");

            // Gritty details everywhere.
            foreach (var p in new[] { yard, depot, cars, tires, salvage })
            {
                Assert.True(TestKit.Count(p, ObjectRole.Decal) > 100, p.GeneratorId + " grime");
                Assert.NotEmpty(p.Paint);
                Assert.Contains(p.Objects, o => o.Category == AssetCategories.ScrapCabin);
            }
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>();
            foreach (var c in new[] { AssetCategories.ScrapHeap, AssetCategories.ScrapWreck, AssetCategories.ScrapTire, AssetCategories.ScrapContainer,
                         AssetCategories.ScrapPallet, AssetCategories.ScrapBarrel, AssetCategories.ScrapMachine, AssetCategories.ScrapCabin,
                         AssetCategories.ScrapBarrier, AssetCategories.LandfillSkip, AssetCategories.LandfillGarbage })
                none[c] = 0;
            using (AssetAvailability.Scope(none))
                foreach (var p in TestKit.Gen(Id).Presets)
                {
                    var plan = YardTestKit.PlanPreset(Id, p.Id, YardTestKit.OneHa);
                    Assert.False(plan.HasErrors, p.Id);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    TestKit.AssertSurfacesAccepted(plan);
                }
        }

        [Theory]
        [InlineData("ShippingContainer01", AssetCategories.ScrapContainer)]
        [InlineData("Cargo Container 40ft Red", AssetCategories.ScrapContainer)]
        [InlineData("CarWreck02", AssetCategories.ScrapWreck)]
        [InlineData("Burnt Car", AssetCategories.ScrapWreck)]
        [InlineData("TirePile01", AssetCategories.ScrapTire)]
        [InlineData("Tyre Stack", AssetCategories.ScrapTire)]
        [InlineData("Pallet02", AssetCategories.ScrapPallet)]
        [InlineData("WoodenCrate01", AssetCategories.ScrapPallet)]
        [InlineData("OilBarrel01", AssetCategories.ScrapBarrel)]
        [InlineData("Gas Cylinder Rack", AssetCategories.ScrapBarrel)]
        [InlineData("ScrapMetalPile01", AssetCategories.ScrapHeap)]
        [InlineData("Forklift01", AssetCategories.ScrapMachine)]
        [InlineData("Portacabin01", AssetCategories.ScrapCabin)]
        [InlineData("ConcreteBarrier01", AssetCategories.ScrapBarrier)]
        [InlineData("TrafficCone01", AssetCategories.ScrapBarrier)]
        public void NameRulesClassify(string name, string expected)
        {
            var cats = AssetHeuristics.Classify(name, AssetTraits.StaticObject | AssetTraits.HasMesh);
            Assert.Contains(expected, cats);
            Assert.True(AssetHeuristics.AutoEligible(expected, name, AssetTraits.StaticObject | AssetTraits.HasMesh), name);
        }

        [Theory]
        [InlineData("DogParkTireJump", AssetCategories.DogAgility)]
        [InlineData("RainBarrel01", AssetCategories.GardenShed)]
        public void NameRulesLeaveOtherPoolsAlone(string name, string expected)
        {
            var cats = AssetHeuristics.Classify(name, AssetTraits.StaticObject | AssetTraits.HasMesh);
            Assert.Contains(expected, cats);
            Assert.DoesNotContain(cats, c => c.StartsWith("industrial.", StringComparison.Ordinal));
        }

        [Fact]
        public void HiddenFeatureLayersStayOutOfTheMakeRow()
        {
            Assert.True(GeneratorRegistry.IsHidden(TestKit.Gen("junkPiles")));
            Assert.False(GeneratorRegistry.IsHidden(TestKit.Gen(Id)));
        }
    }
}
