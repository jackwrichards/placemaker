// Type wave (events): fairground — presets on 1 ha squares and 6 ha L-shapes, determinism, the
// usual invariants (inside, off paths, clear of Reserved, surfaces accepted, terrain blended),
// speed, features, localisation, and the fairground's own structure (stage facing an open field,
// stalls lining the streets, landmark / rides / trucks per preset).

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
    /// <summary>Shared checks for the events types (fairground, amphitheatre).</summary>
    public static class EventsKit
    {
        public static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static float3[] OneHa => TestKit.Rect(100, 100);
        /// <summary>≈ 6 ha L-shape (293² · 0.6975).</summary>
        public static float3[] SixHaL => TestKit.LShape(293);
        public static float3[] FiveHa => TestKit.Rect(250, 200);

        public static Recipe With(string layer, SettingsBag preset = null, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            if (preset != null) foreach (var kv in preset.Values) r.Root.Settings.Values[kv.Key] = kv.Value;
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        public static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        public static AreaPlan Plan(string id, float3[] ring, SettingsBag settings, SiteContext site = null, ReservedSpace reserved = null, uint seed = 5)
        {
            var input = TestKit.Input(ring, settings?.Clone(), seed, null, site ?? TestKit.RoadAlongBottom(300));
            if (reserved != null) input.Reserved = reserved;
            return TestKit.Gen(id).Plan(input, CancellationToken.None);
        }

        public static void AssertInside(AreaPlan plan, float3[] ring, float tolerance = 0.05f)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < tolerance,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {n.Role} end {p.xz} outside");
            foreach (var t in plan.Terraform)
                foreach (var v in t.Ring) Assert.True(Poly.Contains(r, v.xz) || Poly.DistanceToBoundary(r, v.xz) < 0.05f, "terraform outside the area");
            foreach (var p in plan.Paint)
                foreach (var v in p.Ring) Assert.True(Poly.Contains(r, v.xz) || Poly.DistanceToBoundary(r, v.xz) < 0.05f, "paint outside the area");
        }

        /// <summary>No object (decals and markers excepted) sits on a path of the same plan.</summary>
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

        /// <summary>Terraform never makes a cliff at the outline: change ≤ 30 % of the distance to it (plus a cell's slack).</summary>
        public static void AssertTerrainBlended(AreaPlan plan, float3[] ring, SiteContext site)
        {
            var r = Poly.To2D(ring);
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent);
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float edge = Poly.DistanceToBoundary(r, c);
                float change = math.abs(t.TargetHeight - site.HeightAt(c));
                Assert.True(change < edge * 0.3f + 0.3f, $"{plan.GeneratorId}: {change:0.00} m change {edge:0.0} m from the edge (cliff)");
            }
            if (plan.Terraform.Count > 0) Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey && i.Severity == IssueSeverity.Warning);
        }

        public static ReservedSpace SomeReserved(float2 c)
        {
            var res = new ReservedSpace();
            res.AddDisc(c, 6f, "Centerpiece");
            res.AddCorridor(c + new float2(-40, 15), c + new float2(40, 15), 2f, "Street");
            res.Polygons.Add(new[] { c + new float2(-30, -30), c + new float2(-18, -30), c + new float2(-18, -18), c + new float2(-30, -18) });
            return res;
        }

        public static void AssertClearOfReservedPolygons(AreaPlan plan, ReservedSpace res)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal) continue;
                foreach (var poly in res.Polygons)
                    Assert.False(Poly.Contains(poly, o.Position.xz), $"{o.Role}/{o.Category} at {o.Position.xz} inside a reserved polygon");
            }
        }

        public static void AssertLocalised(string id)
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
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g))
            {
                Need(f.LabelKey); Need(f.TooltipKey);
                var fg = GeneratorRegistry.Get(f.Layer);
                Assert.NotNull(fg);
                Need(fg.NameKey);
                foreach (var c in fg.Controls) { Need(c.LabelKey); Need(c.TooltipKey); foreach (var o in c.Options) Need(o.LabelKey); }
            }
            foreach (var cat in new[]
            {
                AssetCategories.EventRide, AssetCategories.EventTent, AssetCategories.EventFoodTruck, AssetCategories.EventToilet, AssetCategories.EventLightTower,
                AssetCategories.EventBarrier, AssetCategories.EventBackstage, AssetCategories.EventScreen, AssetCategories.EventSpeaker,
                AssetCategories.EventFestoon, AssetCategories.EventChristmas, AssetCategories.EventDeckchair,
            }) Need(Keys.Category(cat));
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        public static void AssertSchema(string id, string group)
        {
            var g = TestKit.Gen(id);
            Assert.Equal(group, GeneratorGroups.Of(g));
            Assert.InRange(g.Presets.Count, 4, 6);
            // s12: the street-shop options (amount, layout, depth) sit under their feature toggle, not in the main list.
            Assert.InRange(g.Controls.Count(c => System.Array.IndexOf(Placemaker.Core.Layers.Common.LayerBase.ShopOptions, c.Id) < 0 && !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 48);   // s18: + the ground palette (chooser, hidden base, field and four role overrides) (Phase 1: look settings not counted)
            var groups = g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Where(x => !Placemaker.Core.Layers.Common.GroundSection.IsGround(x)).Distinct().ToList();   // (Phase 1) look settings and the shared Ground section (terrain paint, ground cover, shape) not counted
            Assert.InRange(groups.Count, 4, 8);
            Assert.Equal(g.Controls.Count, g.Controls.Select(c => c.Id).Distinct().Count());
            Assert.Contains(g.Controls, c => c.Id == "style" && c.Kind == ControlKind.Choice);
            Assert.Equal(ControlTier.Essential, g.Controls[0].Tier);
            var defaults = g.Defaults();
            foreach (var c in g.Controls)
                if (c.Kind == ControlKind.Slider)
                {
                    Assert.InRange(defaults.Num(c.Id, double.NaN), c.Min, c.Max);
                    Assert.False(string.IsNullOrEmpty(c.Unit), c.Id);
                    foreach (var p in g.Presets) Assert.InRange(p.Values.Num(c.Id, double.NaN), c.Min, c.Max);
                }
                else if (c.Kind == ControlKind.Choice)
                    foreach (var p in g.Presets) Assert.Contains(p.Values.S(c.Id, null), c.Options.Select(o => o.Value));
            // Presets look different: they differ in style and in at least three other controls.
            var ps = g.Presets.ToList();
            for (int i = 0; i < ps.Count; i++)
                for (int j = i + 1; j < ps.Count; j++)
                {
                    int diff = g.Controls.Count(c => !Equals(ps[i].Values.Values.TryGetValue(c.Id, out var a) ? a : null, ps[j].Values.Values.TryGetValue(c.Id, out var b) ? b : null));
                    Assert.True(diff >= 4, ps[i].Id + " vs " + ps[j].Id + " differ in " + diff + " controls");
                }
        }

        public static void AssertRecipe(string file, string layer)
        {
            var recipe = TestKit.LoadRecipe(file);
            Assert.Equal(layer, recipe.Root.Layer);
            var lang = TestKit.Lang();
            Assert.True(lang.ContainsKey(recipe.NameKey), recipe.NameKey);
            Assert.True(lang.ContainsKey(recipe.NameKey + ".Description"));
            Assert.False(string.IsNullOrEmpty(recipe.Icon));
            var icons = System.IO.Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\UI", recipe.Icon.Replace('/', '\\'));
            if (System.IO.Directory.Exists(@"C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\UI"))
                Assert.True(System.IO.File.Exists(icons), "icon " + recipe.Icon + " not in the game's UI media");
        }
    }

    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class FairgroundTests
    {
        private const string Id = "fairground";
        private readonly ITestOutputHelper _out;
        public FairgroundTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => GeneratorRegistry.Get(Id).Presets.Select(p => new object[] { p.Id });
        /// <summary>(the plazas makeover) The street-and-field presets; the summer fair and the Christmas market are designed squares (DesignedSquaresTests).</summary>
        public static IEnumerable<object[]> FieldPresetIds() => GeneratorRegistry.Get(Id).Presets.Where(p => p.Id != "fairground" && p.Id != "christmas").Select(p => new object[] { p.Id });
        private static SettingsBag Preset(string id) => GeneratorRegistry.Get(Id).Presets.Single(p => p.Id == id).Values;

        [Theory]
        [MemberData(nameof(FieldPresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var ring in new[] { EventsKit.OneHa, EventsKit.SixHaL })
            {
                var site = TestKit.RoadAlongBottom(300);
                var plan = EventsKit.Plan(Id, ring, Preset(preset), site);
                Assert.False(plan.HasErrors, preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                Assert.True(plan.Objects.Count > 15, preset + " placed only " + plan.Objects.Count + " objects");
                Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);
                EventsKit.AssertInside(plan, ring);
                EventsKit.AssertNothingOnPaths(plan);
                TestKit.AssertSurfacesAccepted(plan);
                EventsKit.AssertTerrainBlended(plan, ring, site);
                Assert.True(plan.Facts["stalls"] > 0, preset + " has no stalls");
                Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministic(string preset)
        {
            var a = EventsKit.Plan(Id, EventsKit.SixHaL, Preset(preset));
            var b = EventsKit.Plan(Id, EventsKit.SixHaL, Preset(preset));
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsFastOnFiveHectares(string preset)
        {
            EventsKit.Plan(Id, EventsKit.FiveHa, Preset(preset));   // JIT
            var sw = Stopwatch.StartNew();
            EventsKit.Plan(Id, EventsKit.FiveHa, Preset(preset));
            sw.Stop();
            _out.WriteLine($"{preset}: {sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{preset} took {sw.ElapsedMilliseconds} ms for 5 ha");
        }

        [Theory]
        [MemberData(nameof(FieldPresetIds))]
        public void PresetKeepsClearOfReserved(string preset)
        {
            var res = EventsKit.SomeReserved(new float2(140, 120));
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset(preset), null, res);
            Assert.False(plan.HasErrors);
            TestKit.AssertClearOfReserved(plan, res);
            EventsKit.AssertClearOfReservedPolygons(plan, res);
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var gen = GeneratorRegistry.Get(Id);
            var features = Phase1Looks.AllFeatures(gen);   // (Phase 1) add-ons and look settings
            Assert.Contains("lightTowers", features);
            Assert.Contains("festivalCamping", features);
            foreach (var preset in new[] { "festival", "christmas" })
            {
                var plan = EventsKit.Compose(EventsKit.With(Id, Preset(preset), features), EventsKit.FiveHa, TestKit.RoadAlongBottom(250));
                Assert.True(string.IsNullOrEmpty(EventsKit.Errors(plan)), EventsKit.Errors(plan));
                foreach (var l in plan.Layers) EventsKit.AssertInside(l, EventsKit.FiveHa, 1.2f);
                foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
                // Features keep out of the crowd field.
                var field = plan.Layers[0].Regions.FirstOrDefault(r => r.Key == "field");
                if (field != null)
                {
                    var ring = Poly.To2D(field.Ring);
                    foreach (var l in plan.Layers.Skip(1))
                        foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal))
                            Assert.False(Poly.Contains(ring, o.Position.xz), $"{l.GeneratorId} put {o.Role}/{o.Category} in the crowd field");
                }
                Assert.True(plan.Layers.Single(l => l.GeneratorId == "festivalCamping").Objects.Count(o => o.Category == AssetCategories.CampingPitch) > 5);
                Assert.True(plan.Layers.Single(l => l.GeneratorId == "lightTowers").Objects.Count > 0);
            }
        }

        [Fact]
        public void LabelsSchemaAndRecipe()
        {
            EventsKit.AssertLocalised(Id);
            EventsKit.AssertSchema(Id, "events");
            EventsKit.AssertRecipe("fairground.json", Id);
        }

        [Fact]
        public void StageFacesAnOpenCrowdField()
        {
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("festival"));
            var stage = plan.Objects.Single(o => o.Category == AssetCategories.Stage);
            var field = plan.Regions.Single(r => r.Key == "field");
            Assert.Contains("place:field", field.Tags);
            var ring = Poly.To2D(field.Ring);
            var fc = Poly.Centroid(ring);
            // Facing: the stage's yaw points at the field.
            var facing = new float2(math.sin(stage.Yaw), math.cos(stage.Yaw));
            Assert.True(math.dot(facing, math.normalize(fc - stage.Position.xz)) > 0.95f);
            // Only the front-of-house tent stands in the field.
            var inField = plan.Objects.Where(o => o.Role != ObjectRole.Decal && Poly.Contains(ring, o.Position.xz)).ToList();
            Assert.True(inField.All(o => o.Category == AssetCategories.EventTent || o.Category == AssetCategories.Shelter), string.Join(",", inField.Select(o => o.Category)));
            Assert.True(Poly.Area(ring) > EventsKit.FiveHa.Length * 0 + 0.2f * 50000f, "field too small: " + Poly.Area(ring));
            // Backstage is fenced, there is a crowd barrier, the site is fenced.
            Assert.True(plan.Nets.Count(n => n.Role == NetRole.Fence) > 10);
        }

        [Fact]
        public void StallsLineTheStreetsFacingThem()
        {
            // (the plazas makeover) the fairground preset is the designed fair (its midway: DesignedSquaresTests); the funfair keeps the streets
            var plan = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("funfair"));
            var stalls = plan.Objects.Where(o => o.Category == AssetCategories.MarketStall).ToList();
            Assert.True(stalls.Count >= 10);
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            int facing = 0;
            foreach (var s in stalls)
            {
                float d = paths.Min(n => TestKit.DistanceToCurve(s.Position.xz, n));
                Assert.True(d < 9f, "stall " + d + " m from any street");
                // Faces a street within reach (at junctions the nearest one may be the crossing street).
                var f = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                if (paths.Any(n => TestKit.DistanceToCurve(s.Position.xz, n) < 9f &&
                    math.dot(f, math.normalizesafe(SegmentIndex.ClosestPoint(s.Position.xz, n.A.xz, n.D.xz) - s.Position.xz)) > 0.9f)) facing++;
            }
            Assert.True(facing >= stalls.Count * 0.9f, facing + " of " + stalls.Count + " face their street");
        }

        [Fact]
        public void PresetsHaveTheirSignatures()
        {
            var funfair = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("funfair"));
            Assert.True(funfair.Objects.Count(o => o.Category == AssetCategories.EventRide) >= 8);
            Assert.DoesNotContain(funfair.Objects, o => o.Category == AssetCategories.Stage);
            var xmas = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("christmas"));
            Assert.Contains(xmas.Objects, o => o.Category == AssetCategories.EventChristmas);
            Assert.True(xmas.Objects.Count(o => o.Category == AssetCategories.MarketStall) >= 30);
            Assert.DoesNotContain(xmas.Nets, n => n.Role == NetRole.Fence && math.distance(n.A.xz, n.D.xz) > 30f);
            var trucks = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("foodTrucks"));
            Assert.True(trucks.Objects.Count(o => o.Category == AssetCategories.EventFoodTruck) >= 10);
            // Art pass a2: dining tables are table sets (seating.cafe); the picnic pool holds grills and blankets.
            Assert.True(trucks.Objects.Count(o => o.Category == AssetCategories.CafeSeating || o.Category == AssetCategories.Picnic) >= 20);
            var fest = EventsKit.Plan(Id, EventsKit.FiveHa, Preset("festival"));
            Assert.True(fest.Objects.Count(o => o.Category == AssetCategories.EventToilet) >= 16);
            Assert.True(fest.Objects.Count(o => o.Category == AssetCategories.EventTent) >= 10);
            Assert.NotEmpty(fest.Paint);
            Assert.Contains(fest.Issues, i => i.Key == TerrainPaint.PermanentKey);
        }

        [Fact]
        public void MissingEventAssetsFallBackOrDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.EventRide] = 0, [AssetCategories.EventTent] = 0, [AssetCategories.EventFoodTruck] = 0, [AssetCategories.EventToilet] = 0,
                [AssetCategories.EventLightTower] = 0, [AssetCategories.EventChristmas] = 0, [AssetCategories.EventFestoon] = 0, [AssetCategories.EventBackstage] = 0,
                [AssetCategories.Playground] = 0, [AssetCategories.Stage] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var p in GeneratorRegistry.Get(Id).Presets)
                {
                    var plan = EventsKit.Plan(Id, EventsKit.FiveHa, p.Values);
                    Assert.False(plan.HasErrors);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.True(plan.Objects.Count > 15, p.Id);
                    if (p.Id == "funfair") Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                }
        }

        [Fact]
        public void ClassifiesEventProps()
        {
            var r = new List<string>();
            new EventCategoryRules().Classify("foodtruck01", AssetTraits.StaticObject | AssetTraits.HasMesh, r);
            Assert.Contains(AssetCategories.EventFoodTruck, r);
            Assert.Contains(AssetCategories.Kiosk, r);
            r.Clear();
            new EventCategoryRules().Classify("portabletoilet02", AssetTraits.StaticObject, r);
            Assert.Equal(new[] { AssetCategories.EventToilet, AssetCategories.Restroom }, r);
            r.Clear();
            new EventCategoryRules().Classify("carousel01", AssetTraits.StaticObject, r);
            Assert.Equal(new[] { AssetCategories.EventRide }, r);
            r.Clear();
            new EventCategoryRules().Classify("park bench 01", AssetTraits.StaticObject, r);
            Assert.Empty(r);
            Assert.True(new EventCategoryRules().AutoEligible(AssetCategories.EventScreen, "billboard screen", AssetTraits.StaticObject) == false);
            Assert.Null(new EventCategoryRules().AutoEligible(AssetCategories.Bench, "bench", AssetTraits.StaticObject));
        }
    }
}
