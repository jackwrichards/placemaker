// Type wave: abandonedLot (and the shared checks used by the urbanRuins tests). Definition of done:
// every preset plans a 1 ha and a 6 ha L-shape, deterministic, nothing outside / on paths / in
// Reserved, surfaces accepted, terraform blended at the edge, < 250 ms for 5 ha, features compose,
// every key has English text; plus the decay composition itself and graceful degradation.

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
    /// <summary>Shared fixtures and invariant checks for the decay types.</summary>
    public static class RuinsChecks
    {
        public static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>1 ha and 6 ha L-shapes (area = 0.6975·size²).</summary>
        public static float3[] L1 => TestKit.LShape(119.7f);
        public static float3[] L6 => TestKit.LShape(293.3f);
        public static float3[] FiveHa => TestKit.Rect(250, 200);

        /// <summary>Gently sloped ground with a road along the bottom (terraform targets must follow the ground).</summary>
        public static SiteContext Site(float w = 300f)
        {
            var site = TestKit.RoadAlongBottom(w);
            site.HeightAt = p => 20f + 0.03f * p.x + 0.02f * p.y;
            return site;
        }

        public static IEnumerable<object[]> PresetCases(string id) =>
            GeneratorRegistry.Get(id).Presets.SelectMany(p => new[] { new object[] { p.Id, 1 }, new object[] { p.Id, 6 } });

        public static AreaPlan PlanPreset(string id, string preset, float3[] ring, uint seed = 7, ReservedSpace reserved = null)
        {
            var gen = TestKit.Gen(id);
            var p = gen.Presets.Single(x => x.Id == preset);
            var input = TestKit.Input(ring, p.Values.Clone(), seed, null, Site());
            if (reserved != null) input.Reserved = reserved;
            return gen.Plan(input, CancellationToken.None);
        }

        public static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, Site(), CancellationToken.None);

        public static string Errors(IEnumerable<PlanIssue> issues) =>
            string.Join("; ", issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        /// <summary>Every invariant of the definition of done that one plan can show.</summary>
        public static void AssertSound(AreaPlan plan, float3[] ring, string label)
        {
            Assert.True(Errors(plan.Issues).Length == 0, label + ": " + Errors(plan.Issues));
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < 0.05f, $"{label}: {o.Role}/{o.Category} at {o.Position.xz} outside");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{label}: {n.Role} end {p.xz} outside");
            // Nothing (decals excepted) on a path.
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal) continue;
                foreach (var n in plan.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f, $"{label}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a path");
                }
            }
            TestKit.AssertSurfacesAccepted(plan);
            AssertTerraformBlended(plan, ring, label);
            foreach (var s in plan.Surfaces) foreach (var p in s.Ring)
                Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.3f, $"{label}: {s.Role} surface vertex {p.xz} outside");
        }

        /// <summary>
        /// Terraform returns to the original ground towards the outline: a footprint whose centre lies d m
        /// inside may change the ground by at most 0.5·d + 0.3 m, and every footprint lies inside.
        /// </summary>
        public static void AssertTerraformBlended(AreaPlan plan, float3[] ring, string label)
        {
            var r = Poly.To2D(ring);
            var site = Site();
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent, label + ": terraform not flagged permanent");
                var fp = Poly.To2D(t.Ring);
                foreach (var p in fp) Assert.True(Poly.Contains(r, p) || Poly.DistanceToBoundary(r, p) < 0.3f, $"{label}: terraform vertex {p} outside");
                var c = Poly.Centroid(fp);
                float d = Poly.Contains(r, c) ? Poly.DistanceToBoundary(r, c) : 0f;
                float change = math.abs(t.TargetHeight - site.HeightAt(c));
                Assert.True(change <= 0.5f * d + 0.3f, $"{label}: terraform changes {change:0.00} m only {d:0.0} m from the edge");
            }
            if (plan.Terraform.Count > 0) Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey);
        }

        public static void AssertClearOfReserved(AreaPlan plan, ReservedSpace reserved, string label)
        {
            foreach (var o in plan.Objects)
            {
                float r = TestKit.FootprintFor(o.Role) - 0.05f;
                foreach (var d in reserved.Discs)
                    Assert.True(math.distance(d.Center, o.Position.xz) >= d.Radius + r, $"{label}: {o.Role}/{o.Category} at {o.Position.xz} in reserved disc");
                foreach (var c in reserved.Corridors)
                    Assert.True(math.sqrt(SegmentIndex.DistSq(o.Position.xz, c.A, c.B)) >= c.HalfWidth + r, $"{label}: {o.Role}/{o.Category} at {o.Position.xz} in reserved corridor");
            }
        }

        public static ReservedSpace SomeReserved(float3[] ring)
        {
            var c = Poly.Centroid(Poly.To2D(ring));
            var res = new ReservedSpace();
            res.AddDisc(c, 12f, "Centerpiece");
            res.AddCorridor(c + new float2(-60f, 25f), c + new float2(60f, 25f), 2.5f, nameof(NetRole.PathwayWide));
            return res;
        }

        public static string Fingerprint(AreaPlan p)
        {
            var sb = new System.Text.StringBuilder(TestKit.Fingerprint(p));
            foreach (var t in p.Terraform) sb.Append($"T{t.TargetHeight:R}:{t.Ring[0].x:R},{t.Ring[0].z:R};");
            foreach (var t in p.Paint) sb.Append($"P{t.Layer}:{t.Ring[0].x:R},{t.Ring[0].z:R};");
            return sb.ToString();
        }

        public static double Fact(AreaPlan p, string key) => p.Facts.TryGetValue(key, out var v) ? v : 0;

        /// <summary>Every label/tooltip/option/preset key of the layer, plus every literal key in the given source folders.</summary>
        public static void AssertLang(string id, params string[] sourceDirs)
        {
            var lang = TestKit.Lang();
            var g = TestKit.Gen(id);
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey); Need(c.TooltipKey);
                Need("Placemaker.UI.Group." + c.Group);
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                if (lang.TryGetValue(c.LabelKey, out var label)) Assert.True(label.Split(' ').Length <= 3, id + "." + c.Id + " label too long: " + label);
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g)) { Need(f.LabelKey); Need(f.TooltipKey); }
            foreach (var dir in sourceDirs)
                foreach (var file in Directory.GetFiles(Path.Combine(TestKit.RepoRoot(), dir), "*.cs"))
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                        Need(m.Groups[1].Value);
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        public static void AssertRecipe(string id)
        {
            var recipe = TestKit.LoadRecipe(id + ".json");
            Assert.Equal(id, recipe.Root.Layer);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey), recipe.NameKey);
            var json = File.ReadAllText(Path.Combine(TestKit.RepoRoot(), "Recipes", id + ".json"));
            var desc = Regex.Match(json, "\"descriptionKey\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            Assert.True(TestKit.Lang().ContainsKey(desc), desc);
            Assert.Equal(TestKit.Gen(id).Icon, recipe.Icon);
        }
    }

    public class AbandonedLotTests
    {
        private const string Id = "abandonedLot";
        public static IEnumerable<object[]> Cases() => RuinsChecks.PresetCases(Id);

        [Fact]
        public void IsAnIndustrialTypeWithFiveSubtypesAndFeatures()
        {
            var gen = TestKit.Gen(Id);
            Assert.Equal("industrial", GeneratorGroups.Of(gen));
            Assert.Equal(LayerKind.Fill, gen.Kind);
            Assert.InRange(gen.Presets.Count, 4, 8);   // (rundown) + old car park, burnt-out lot, vacant lot
            Assert.InRange(gen.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 40);   // (Phase 1) look settings not counted
            var subtype = gen.Controls.Single(c => c.Id == "subtype");
            Assert.Equal(gen.Presets.Select(p => p.Id), subtype.Options.Select(o => o.Value));
            foreach (var p in gen.Presets) Assert.Equal(p.Id, p.Values.S("subtype", ""));
            var groups = gen.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Distinct().ToList();   // (Phase 1) look settings not counted
            Assert.InRange(groups.Count(g => g != "entrances" && g != "shops"), 4, 8);
            Assert.True(Phase1Looks.Offers(gen, "debris"));   // (Phase 1) a look setting now
            Assert.Contains(Features.Of(gen), f => f.Layer == "dirtJumps");
            Assert.True(Phase1Looks.Offers(gen, "rocks"));   // (Phase 1) a look setting now (lookRockyAreas)
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryPresetPlansSoundOnLShapes(string preset, int hectares)
        {
            var ring = hectares == 1 ? RuinsChecks.L1 : RuinsChecks.L6;
            var plan = RuinsChecks.PlanPreset(Id, preset, ring);
            RuinsChecks.AssertSound(plan, ring, preset + "/" + hectares + "ha");
            Assert.True(plan.Objects.Count(o => o.Role != ObjectRole.Decal) > 15 * hectares, preset + " placed only " + plan.Objects.Count);
            Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryPresetIsDeterministic(string preset, int hectares)
        {
            var ring = hectares == 1 ? RuinsChecks.L1 : RuinsChecks.L6;
            Assert.Equal(RuinsChecks.Fingerprint(RuinsChecks.PlanPreset(Id, preset, ring)), RuinsChecks.Fingerprint(RuinsChecks.PlanPreset(Id, preset, ring)));
        }

        [Fact]
        public void SeedsChangeTheLayout()
        {
            Assert.NotEqual(RuinsChecks.Fingerprint(RuinsChecks.PlanPreset(Id, "overgrown", RuinsChecks.L6, 1)),
                RuinsChecks.Fingerprint(RuinsChecks.PlanPreset(Id, "overgrown", RuinsChecks.L6, 2)));
        }

        [Theory]
        [InlineData("overgrown")]
        [InlineData("rubbleField")]
        [InlineData("demolished")]
        [InlineData("squatterCamp")]
        [InlineData("fencedWasteland")]
        public void RespectsReservedSpace(string preset)
        {
            var ring = RuinsChecks.L6;
            var reserved = RuinsChecks.SomeReserved(ring);
            var plan = RuinsChecks.PlanPreset(Id, preset, ring, 3, reserved);
            RuinsChecks.AssertSound(plan, ring, preset);
            RuinsChecks.AssertClearOfReserved(plan, reserved, preset);
        }

        [Fact]
        public void SubtypesAreGenuinelyDifferent()
        {
            var ring = RuinsChecks.L6;
            var p = TestKit.Gen(Id).Presets.ToDictionary(x => x.Id, x => RuinsChecks.PlanPreset(Id, x.Id, ring));
            double F(string preset, string fact) => RuinsChecks.Fact(p[preset], fact);
            // Overgrown: green. Rubble field: piles and mounds. Demolished: foundations in a row. Camp: shelters. Wasteland: intact fence + gravel.
            Assert.True(F("overgrown", "weeds") + F("overgrown", "youngTrees") > 2 * (F("rubbleField", "weeds") + F("rubbleField", "youngTrees")));
            // (rundown) the rubble field is a few big tipped mounds now (each heap 1.3-1.8x), not a carpet of small chunks
            Assert.True(F("rubbleField", "rubblePiles") + F("rubbleField", "rubbleChunks") > F("overgrown", "rubblePiles") + F("overgrown", "rubbleChunks"));
            Assert.True(F("rubbleField", "moundCells") > 0);
            Assert.True(F("demolished", "foundations") >= 2);
            Assert.True(F("squatterCamp", "shelters") >= 4);
            Assert.Equal(0, F("overgrown", "shelters"));
            Assert.Contains(p["fencedWasteland"].Surfaces, s => s.Category == AssetCategories.SurfaceNatural);
            float Fence(AreaPlan a) => a.Nets.Where(n => n.Role == NetRole.Fence).Sum(n => math.distance(n.A.xz, n.D.xz));
            Assert.True(Fence(p["fencedWasteland"]) > 1.5f * Fence(p["overgrown"]), "wasteland fence should be intact, the overgrown one broken");
            // Foundations are broken slabs, with walls and weeds in the cracks.
            Assert.True(F("overgrown", "crackWeeds") > 0);
            Assert.True(p["overgrown"].Surfaces.Count(s => s.Category == AssetCategories.SurfacePaving) >= 3, "slab should break into shards");
            Assert.True(F("demolished", "walls") > 0);
            // Every pair of presets differs.
            var prints = p.Values.Select(RuinsChecks.Fingerprint).ToList();
            Assert.Equal(prints.Count, prints.Distinct().Count());
        }

        [Fact]
        public void RubbleThinsOutFromTheCollapsePoint()
        {
            var ring = RuinsChecks.FiveHa;
            var gen = TestKit.Gen(Id);
            var settings = gen.Presets.Single(p => p.Id == "overgrown").Values.Clone();
            settings.Values["rubble"] = 80.0; settings.Values["focus"] = "centre"; settings.Values["foundations"] = 0.0;
            var plan = gen.Plan(TestKit.Input(ring, settings, 5, null, RuinsChecks.Site()), CancellationToken.None);
            var focus = LayerRegion.From(Poly.To2D(ring)).DeepestPoint(out _, null, 16);
            var rubble = plan.Objects.Where(o => o.Category == AssetCategories.RuinsRubble).Select(o => (d: math.distance(o.Position.xz, focus), s: o.Scale)).ToList();
            Assert.True(rubble.Count > 40, rubble.Count + " rubble items");
            float R = rubble.Max(x => x.d);
            // Density per m² in the inner third of the reach is far higher than in the outer third…
            double inner = rubble.Count(x => x.d < R / 3f) / (math.PI * R * R / 9f);
            double outer = rubble.Count(x => x.d > 2f * R / 3f) / (math.PI * R * R * 5f / 9f);
            Assert.True(inner > 3 * outer, $"inner {inner:0.0000}/m², outer {outer:0.0000}/m²");
            // …and the big piles are near the collapse point, small chunks further out.
            Assert.True(rubble.Where(x => x.d < R / 3f).Average(x => x.s) > rubble.Where(x => x.d > R / 2f).Average(x => x.s));
        }

        [Fact]
        public void OvergrowthComesFromTheEdges()
        {
            var ring = RuinsChecks.FiveHa;
            var plan = RuinsChecks.PlanPreset(Id, "overgrown", ring);
            var r = Poly.To2D(ring);
            var weeds = plan.Objects.Where(o => o.Role == ObjectRole.Bush).Select(o => Poly.DistanceToBoundary(r, o.Position.xz)).ToList();
            Assert.True(weeds.Count > 100);
            // Band 0–20 m holds far more than its share of the 250×200 area (≈ 30 %).
            Assert.True(weeds.Count(d => d < 20f) > 0.45 * weeds.Count, $"{weeds.Count(d => d < 20f)} of {weeds.Count} near the edge");
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var gen = TestKit.Gen(Id);
            var features = Phase1Looks.AllFeatures(gen);   // (Phase 1) add-ons and look settings
            var plan = RuinsChecks.Compose(RuinsChecks.With(Id, features), RuinsChecks.FiveHa);
            Assert.True(RuinsChecks.Errors(plan.AllIssues()).Length == 0, RuinsChecks.Errors(plan.AllIssues()));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            var debris = plan.Layers.First(l => l.GeneratorId == "debris");
            Assert.Contains(debris.Objects, o => o.Category == AssetCategories.RuinsJunk);
            var jumps = plan.Layers.First(l => l.GeneratorId == "dirtJumps");
            Assert.True(RuinsChecks.Fact(jumps, "jumps") > 0);
            foreach (var l in plan.Layers.Where(l => l.GeneratorId == "debris" || l.GeneratorId == "dirtJumps"))
                RuinsChecks.AssertSound(l, RuinsChecks.FiveHa, l.GeneratorId);
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.RuinsRubble] = 0, [AssetCategories.RuinsJunk] = 0, [AssetCategories.RuinsWall] = 0, [AssetCategories.RuinsSlab] = 0,
                [AssetCategories.RuinsShelter] = 0, [AssetCategories.RuinsWeeds] = 0, [AssetCategories.Rock] = 0, [AssetCategories.CampingPitch] = 0,
                [AssetCategories.Camping] = 0, [AssetCategories.GardenShed] = 0, [AssetCategories.CampingFire] = 0, [AssetCategories.DecalGraffiti] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var preset in new[] { "squatterCamp", "demolished", "rubbleField" })
                {
                    var plan = RuinsChecks.PlanPreset(Id, preset, RuinsChecks.L6);
                    RuinsChecks.AssertSound(plan, RuinsChecks.L6, preset);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? ""));
                    Assert.True(plan.Objects.Count > 20, "still builds overgrowth with plain bushes");
                }
        }

        [Fact]
        public void EveryKeyHasEnglishTextAndTheRecipeShips()
        {
            RuinsChecks.AssertLang(Id, "Core/Layers/Types/AbandonedLot");
            RuinsChecks.AssertLang("debris");
            RuinsChecks.AssertRecipe(Id);
            var lang = TestKit.Lang();
            foreach (var c in new[] { AssetCategories.RuinsRubble, AssetCategories.RuinsJunk, AssetCategories.RuinsSlab, AssetCategories.RuinsPillar,
                AssetCategories.RuinsWall, AssetCategories.RuinsWeeds, AssetCategories.RuinsShelter })
                Assert.True(lang.ContainsKey(Keys.Category(c)), c);
        }

        [Theory]
        [InlineData("RubblePile01", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsRubble)]
        [InlineData("ConstructionDebris02", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsRubble)]
        [InlineData("TirePile01", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsJunk)]
        [InlineData("OilBarrel01", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsJunk)]
        [InlineData("ConcreteBlock01", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsSlab)]
        [InlineData("ConcretePipe02", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsSlab)]
        [InlineData("BridgePillar01", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsPillar)]
        [InlineData("BrickWall01", AssetTraits.StaticObject | AssetTraits.HasMesh, AssetCategories.RuinsWall)]
        [InlineData("WildBush02", AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.Plant, AssetCategories.RuinsWeeds)]
        public void RulesClassifyDecayProps(string name, AssetTraits traits, string expected)
        {
            var cats = AssetHeuristics.Classify(name, traits);
            Assert.Contains(expected, cats);
            Assert.True(AssetHeuristics.AutoEligible(expected, name, traits), name + " should be automatic for " + expected);
        }

        [Fact]
        public void RulesStayAdditiveAndSkipLampsAndSigns()
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh;
            Assert.DoesNotContain(AssetCategories.RuinsWall, AssetHeuristics.Classify("WallLamp01", t | AssetTraits.StreetLight));
            Assert.DoesNotContain(AssetCategories.RuinsJunk, AssetHeuristics.Classify("TrashBin01", t));
            Assert.Contains(AssetCategories.Bin, AssetHeuristics.Classify("TrashBin01", t));
            // A concrete barrier is a ledge for us and still a fence for everyone else.
            var barrier = AssetHeuristics.Classify("ConcreteBarrier01", t);
            Assert.Contains(AssetCategories.RuinsSlab, barrier);
            Assert.Contains(AssetCategories.Fence, barrier);
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.RuinsJunk, "RandomJunkPlaceholder", t));
        }
    }

    [Collection(TimingCollection.Name)]
    public class RuinsTimingTests
    {
        private readonly ITestOutputHelper _out;
        public RuinsTimingTests(ITestOutputHelper output) { _out = output; }

        [Theory]
        [InlineData("abandonedLot")]
        [InlineData("urbanRuins")]
        public void EveryPresetIsFastOnFiveHectares(string id)
        {
            var gen = TestKit.Gen(id);
            foreach (var p in gen.Presets)
            {
                RuinsChecks.PlanPreset(id, p.Id, RuinsChecks.FiveHa);   // JIT
                var sw = Stopwatch.StartNew();
                RuinsChecks.PlanPreset(id, p.Id, RuinsChecks.FiveHa, 9);
                sw.Stop();
                _out.WriteLine($"{id}/{p.Id}: {sw.ElapsedMilliseconds} ms");
                Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{id}/{p.Id} took {sw.ElapsedMilliseconds} ms for 5 ha");
            }
        }
    }
}
