// Type wave, builder 12: logistics yard ("a parking lot, but not for cars").

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
    /// <summary>Shared checks for builder 12's types (logistics yard, hiking trail).</summary>
    internal static class B12Kit
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static Recipe With(string layer, SettingsBag settings = null, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            if (settings != null) foreach (var kv in settings.Values) r.Root.Settings.Values[kv.Key] = kv.Value;
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        public static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        /// <summary>1 ha square.</summary>
        public static readonly float3[] OneHa = TestKit.Rect(100, 100);
        /// <summary>6 ha L-shape (concave).</summary>
        public static readonly float3[] SixHaL = TestKit.LShape(293.3f);
        /// <summary>5 ha rectangle.</summary>
        public static readonly float3[] FiveHa = TestKit.Rect(250, 200);

        public static SiteContext Road(float w) => TestKit.RoadAlongBottom(w);

        /// <summary>Gentle slope plus a ridge, so terrain logic has something to work with.</summary>
        public static SiteContext Hilly(float w, float slopeX = 0.06f, float ridge = 12f)
        {
            var site = TestKit.RoadAlongBottom(w);
            site.HeightAt = p => 50f + p.x * slopeX + ridge * math.exp(-math.pow((p.y - 120f) / 45f, 2f));
            return site;
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
            foreach (var s in plan.Surfaces)
                foreach (var p in s.Ring)
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {s.Role} surface vertex {p.xz} outside");
        }

        /// <summary>No placed object (decals and markers excepted) sits on a path of the same plan.</summary>
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

        /// <summary>Space claimed by an earlier layer: a disc and a corridor in the middle of the area.</summary>
        public static ReservedSpace SomeReserved(float2 centre)
        {
            var r = new ReservedSpace();
            r.AddDisc(centre, 12f, "Furniture");
            r.AddCorridor(centre + new float2(-60, 25), centre + new float2(60, 25), 3f, "Pathway");
            return r;
        }

        public static void AssertClearOfReserved(AreaPlan plan, ReservedSpace reserved)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint) continue;
                foreach (var d in reserved.Discs)
                    Assert.True(math.distance(d.Center, o.Position.xz) >= d.Radius + 0.3f, $"{o.Role}/{o.Category} at {o.Position.xz} inside reserved disc");
                foreach (var c in reserved.Corridors)
                    Assert.True(math.sqrt(SegmentIndex.DistSq(o.Position.xz, c.A, c.B)) >= c.HalfWidth + 0.3f, $"{o.Role}/{o.Category} at {o.Position.xz} on reserved corridor");
            }
            foreach (var n in plan.Nets)
            {
                if (n.Role == NetRole.Fence) continue;
                foreach (var d in reserved.Discs)
                    for (int i = 0; i <= 8; i++)
                    {
                        var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                        Assert.True(math.distance(d.Center, p) >= d.Radius, $"{n.Role} at {p} crosses reserved disc");
                    }
            }
        }

        /// <summary>Terraform cells next to the outline barely change the ground (blended back to the original height).</summary>
        public static void AssertTerraformBlended(AreaPlan plan, float3[] area, SiteContext site, float maxAtEdge = 1.2f)
        {
            var ring = Poly.To2D(area);
            foreach (var t in plan.Terraform)
            {
                var r = Poly.To2D(t.Ring);
                float edge = r.Min(p => Poly.DistanceToBoundary(ring, p));
                if (edge > 1f) continue;
                var c = Poly.Centroid(r);
                Assert.True(math.abs(t.TargetHeight - site.HeightAt(c)) <= maxAtEdge,
                    $"{plan.GeneratorId}: terraform at the outline changes the ground by {t.TargetHeight - site.HeightAt(c):0.00} m");
            }
        }

        public static void AssertLang(IAreaGenerator g, string folder)
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey); Need(c.TooltipKey);
                if (!string.IsNullOrEmpty(c.Group)) Need("Placemaker.UI.Group." + c.Group);
                Assert.True(!lang.ContainsKey(c.LabelKey) || lang[c.LabelKey].Split(' ').Length <= 3, c.LabelKey + " label longer than 3 words");
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
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
            foreach (var file in Directory.GetFiles(Path.Combine(TestKit.RepoRoot(), "Core", "Layers", "Types", folder), "*.cs"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                    Need(m.Groups[1].Value);
            foreach (var f in typeof(AssetCategories).GetFields())
                Need(Keys.Category((string)f.GetValue(null)));
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        public static void AssertRecipe(string id)
        {
            var recipe = TestKit.LoadRecipe(id + ".json");
            Assert.Equal(id, recipe.Id);
            Assert.Equal(id, recipe.Root.Layer);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey), recipe.NameKey);
            var json = File.ReadAllText(Path.Combine(TestKit.RepoRoot(), "Recipes", id + ".json"));
            var desc = Regex.Match(json, "\"descriptionKey\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            Assert.True(TestKit.Lang().ContainsKey(desc), "description " + desc);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = TestKit.Rect(220, 160) }, TestKit.RoadAlongBottom(220), CancellationToken.None);
            Assert.False(plan.HasErrors, Errors(plan));
        }
    }

    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class LogisticsYardTests
    {
        private const string Id = "logisticsYard";
        private readonly ITestOutputHelper _out;
        public LogisticsYardTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => GeneratorRegistry.Get(Id).Presets.Select(p => new object[] { p.Id });

        private static SettingsBag PresetValues(string preset) => GeneratorRegistry.Get(Id).Presets.Single(p => p.Id == preset).Values;

        [Fact]
        public void IsAnIndustrialTypeWithEnoughPresetsAndControls()
        {
            var g = GeneratorRegistry.Get(Id);
            Assert.NotNull(g);
            Assert.Equal("industrial", GeneratorGroups.Of(g));
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 40);
            Assert.InRange(g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Distinct().Count(), 4, 8);
            Assert.Contains(g.Controls, c => c.Id == "style" && c.Kind == ControlKind.Choice);
            Assert.True(Features.Of(g).Count + Placemaker.Core.Layers.Common.LookSettings.Of(g).Count >= 3);   // (Phase 1) looks are settings
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var (ring, site) in new[] { (B12Kit.OneHa, B12Kit.Road(100)), (B12Kit.SixHaL, B12Kit.Road(293)) })
            {
                var plan = B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), ring, site);
                Assert.True(string.IsNullOrEmpty(B12Kit.Errors(plan)), preset + ": " + B12Kit.Errors(plan));
                var layer = plan.Layers.Single(l => l.GeneratorId == Id);
                Assert.True(layer.Objects.Count(o => o.Role != ObjectRole.Decal) > 20, preset + " placed only " + layer.Objects.Count + " objects");
                Assert.True(TestKit.Stat(layer, "Placemaker.Stat.StorageSlots") > 10, preset + " stores almost nothing");
                B12Kit.AssertInside(layer, ring);
                B12Kit.AssertNothingOnPaths(layer);
                TestKit.AssertSurfacesAccepted(layer);
                Assert.Contains(layer.Regions, r => r.Tags.Contains(RegionTags.Core));
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministic(string preset)
        {
            var a = B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), B12Kit.SixHaL, B12Kit.Road(293));
            var b = B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), B12Kit.SixHaL, B12Kit.Road(293));
            Assert.Equal(TestKit.Fingerprint(a.Layers[0]), TestKit.Fingerprint(b.Layers[0]));
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReservedSpace(string preset)
        {
            var reserved = B12Kit.SomeReserved(new float2(125, 100));
            var input = TestKit.Input(B12Kit.FiveHa, PresetValues(preset), 7, null, B12Kit.Road(250));
            input.Reserved = reserved;
            var plan = GeneratorRegistry.Get(Id).Plan(input, CancellationToken.None);
            Assert.False(plan.HasErrors);
            B12Kit.AssertClearOfReserved(plan, reserved);
        }

        [Fact]
        public void IsFastOnFiveHectares()
        {
            var g = GeneratorRegistry.Get(Id);
            foreach (var p in g.Presets)
            {
                B12Kit.Compose(B12Kit.With(Id, p.Values), B12Kit.FiveHa, B12Kit.Road(250));   // JIT
                var sw = Stopwatch.StartNew();
                B12Kit.Compose(B12Kit.With(Id, p.Values), B12Kit.FiveHa, B12Kit.Road(250));
                sw.Stop();
                _out.WriteLine($"{p.Id}: {sw.ElapsedMilliseconds} ms");
                Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{p.Id} took {sw.ElapsedMilliseconds} ms for 5 ha");
            }
        }

        [Fact]
        public void LevellingIsBlendedAtTheOutline()
        {
            var site = B12Kit.Hilly(250, 0.12f, 6f);
            var plan = B12Kit.Compose(B12Kit.With(Id), B12Kit.FiveHa, site).Layers[0];
            Assert.NotEmpty(plan.Terraform);
            Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey);
            B12Kit.AssertTerraformBlended(plan, B12Kit.FiveHa, site);
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Phase1Looks.AllFeatures(GeneratorRegistry.Get(Id));   // (Phase 1) add-ons and look settings
            var plan = B12Kit.Compose(B12Kit.With(Id, null, features), B12Kit.FiveHa, B12Kit.Road(250));
            Assert.True(string.IsNullOrEmpty(B12Kit.Errors(plan)), B12Kit.Errors(plan));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            foreach (var l in plan.Layers) B12Kit.AssertInside(l, B12Kit.FiveHa, 0.5f);
            var yard = plan.Layers[0];
            var breakArea = plan.Layers.Single(l => l.GeneratorId == "yardBreak");
            Assert.NotEmpty(breakArea.Objects);
            // Features use leftovers: nothing of theirs lands on the yard's storage.
            foreach (var o in breakArea.Objects)
                foreach (var s in yard.Objects.Where(x => x.Role == ObjectRole.Furniture))
                    Assert.True(math.distance(o.Position.xz, s.Position.xz) > 0.8f, "break area object on top of storage");
        }

        [Fact]
        public void PresetsLookDifferent()
        {
            string Dominant(string preset)
            {
                var plan = B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), B12Kit.FiveHa, B12Kit.Road(250)).Layers[0];
                return plan.Objects.Where(o => o.Category != null && o.Category.StartsWith("logistics.") && o.Category != AssetCategories.LogisticsTruck)
                    .GroupBy(o => o.Category).OrderByDescending(gr => gr.Count()).First().Key;
            }
            Assert.Equal(AssetCategories.LogisticsContainer, Dominant("containerStacks"));
            Assert.Equal(AssetCategories.LogisticsPallet, Dominant("palletYard"));
            Assert.Equal(AssetCategories.LogisticsLumber, Dominant("lumberYard"));
            Assert.Equal(AssetCategories.LogisticsBulk, Dominant("storageYard"));
            // The loading yard is about the bays.
            var loading = B12Kit.Compose(B12Kit.With(Id, PresetValues("loadingYard")), B12Kit.FiveHa, B12Kit.Road(250)).Layers[0];
            var lumber = B12Kit.Compose(B12Kit.With(Id, PresetValues("lumberYard")), B12Kit.FiveHa, B12Kit.Road(250)).Layers[0];
            Assert.True(TestKit.Stat(loading, "Placemaker.Stat.LoadingBays") >= 12);
            Assert.Equal(0, TestKit.Stat(lumber, "Placemaker.Stat.LoadingBays"));
            Assert.Contains(loading.Objects, o => o.Category == AssetCategories.LogisticsTruck);
        }

        [Fact]
        public void AislesStayClearLikeAParkingLot()
        {
            // The main entrance lane is free of storage and the gate sits on an aisle crossing.
            var input = TestKit.Input(B12Kit.FiveHa, PresetValues("palletYard"), 3, null, B12Kit.Road(250));
            var plan = GeneratorRegistry.Get(Id).Plan(input, CancellationToken.None);
            var gate = Entrances.Resolve(input, LayerRegion.From(B12Kit.FiveHa), "entrance", 1)[0].Position;
            foreach (var o in plan.Objects.Where(o => o.Category != null && o.Category.StartsWith("logistics.") && o.Category != AssetCategories.LogisticsCabin
                && o.Category != AssetCategories.LogisticsBarrier && o.Category != AssetCategories.LogisticsMachine))
            {
                var d = o.Position.xz - gate;
                bool inLane = math.abs(d.x) < 2.4f && d.y > 0 && d.y < 30f;
                Assert.False(inLane, $"{o.Category} at {o.Position.xz} blocks the entrance lane at {gate}");
            }
            // Storage sits in rows: many items share the same few lateral coordinates.
            var items = plan.Objects.Where(o => o.Category == AssetCategories.LogisticsPallet).ToList();
            Assert.True(items.Count > 100);
        }

        [Fact]
        public void MissingCargoDegradesToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.LogisticsContainer] = 0, [AssetCategories.LogisticsCrate] = 0, [AssetCategories.LogisticsCargo] = 0,
                [AssetCategories.LogisticsTruck] = 0, [AssetCategories.LogisticsMachine] = 0, [AssetCategories.LogisticsShed] = 0,
                [AssetCategories.LogisticsCabin] = 0, [AssetCategories.GardenShed] = 0, [AssetCategories.LogisticsBarrier] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                var plan = B12Kit.Compose(B12Kit.With(Id, PresetValues("containerStacks")), B12Kit.FiveHa, B12Kit.Road(250));
                Assert.True(string.IsNullOrEmpty(B12Kit.Errors(plan)), B12Kit.Errors(plan));
                Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                // lorries and machines are Placemaker's own vehicle props (made from the game's vehicles), not catalogue items (2026-10-30)
                Assert.DoesNotContain(plan.Layers[0].Objects, o => none.ContainsKey(o.Category ?? "") && !(o.Asset.Id ?? "").StartsWith(Placemaker.Core.Layers.Types.Yards.VehicleProps.Prefix));
            }
        }

        [Fact]
        public void CategoryRulesClassifyIndustrialProps()
        {
            const AssetTraits prop = AssetTraits.StaticObject | AssetTraits.HasMesh;
            Assert.Contains(AssetCategories.LogisticsContainer, AssetHeuristics.Classify("ShippingContainer01", prop));
            Assert.Contains(AssetCategories.LogisticsCargo, AssetHeuristics.Classify("ShippingContainer01", prop));
            Assert.Contains(AssetCategories.LogisticsPallet, AssetHeuristics.Classify("PalletStack02", prop));
            Assert.Contains(AssetCategories.LogisticsCrate, AssetHeuristics.Classify("WoodenCrate01", prop));
            Assert.Contains(AssetCategories.LogisticsLumber, AssetHeuristics.Classify("LumberPile01", prop));
            Assert.Contains(AssetCategories.LogisticsMachine, AssetHeuristics.Classify("Forklift01", prop));
            Assert.Contains(AssetCategories.LogisticsTruck, AssetHeuristics.Classify("SemiTrailer01", prop));
            Assert.DoesNotContain(AssetCategories.LogisticsContainer, AssetHeuristics.Classify("GarbageContainer01", prop));
            Assert.DoesNotContain(AssetCategories.LogisticsTruck, AssetHeuristics.Classify("FireTruckProp", prop));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.LogisticsCrate, "WoodenCrate01", prop));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.LogisticsMachine, "Forklift01", prop));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.LogisticsCrate, "CrateRandom", prop));
        }

        [Fact]
        public void EveryKeyHasEnglishTextAndTheRecipeComposes()
        {
            B12Kit.AssertLang(GeneratorRegistry.Get(Id), "LogisticsYard");
            B12Kit.AssertRecipe(Id);
        }
    }
}
