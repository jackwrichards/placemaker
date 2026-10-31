// Definition-of-done checks shared by the golf and lookout-hill type tests (builder t04): every preset
// plans a 1 ha square and a 6 ha L-shape cleanly, deterministically and fast; nothing outside the area,
// on a path or in Reserved; surfaces accepted; terraform blended at the outline; features compose; the
// schema is well formed and fully localised; the shipped recipe loads.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types.T04
{
    public abstract class T04TypeTestBase
    {
        protected readonly ITestOutputHelper Out;
        protected T04TypeTestBase(ITestOutputHelper output) { Out = output; }

        protected abstract string LayerId { get; }
        protected abstract string ExpectedGroup { get; }
        protected IAreaGenerator Gen => TestKit.Gen(LayerId);

        protected static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>6 ha L-shape (size² · 0.6975 = 60 000 m²).</summary>
        protected static readonly float3[] SixHaL = TestKit.LShape(293.3f);
        protected static readonly float3[] OneHa = TestKit.Rect(100, 100);
        protected static readonly float3[] FiveHa = TestKit.Rect(250, 200);

        protected Recipe WithPreset(string preset, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(Gen, 11);
            if (preset != null)
                foreach (var kv in Gen.Presets.Single(p => p.Id == preset).Values.Values) r.Root.Settings.Values[kv.Key] = kv.Value;
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f);
            return r;
        }

        protected static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);

        protected static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        protected AreaPlan Layer(ComposedPlan p) => p.Layers.First(l => l.GeneratorId == LayerId);

        // ───────────── invariants ─────────────

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

        public static void AssertNothingOnPaths(AreaPlan plan)
        {
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.AnimalSpawner) continue;
                foreach (var n in paths)
                {
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        /// <summary>Within 15 m of the outline every terraform change stays gentle (blended back to the original ground).</summary>
        public static void AssertTerraformBlended(AreaPlan plan, float3[] ring, Func<float2, float> ground)
        {
            var r = Poly.To2D(ring);
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent);
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float edge = Poly.DistanceToBoundary(r, c);
                foreach (var v in t.Ring) Assert.True(Poly.Contains(r, v.xz) || Poly.DistanceToBoundary(r, v.xz) < 0.1f, "terraform outside the area");
                if (edge > 15f) continue;
                float change = math.abs(t.TargetHeight - ground(c));
                if (!float.IsNaN(t.WaterLevel)) continue;   // ponds sit deep inside by construction (checked separately)
                Assert.True(change <= edge * 0.3f + 0.35f, $"{plan.GeneratorId}: {change:0.00} m change {edge:0.0} m from the edge (cliff)");
            }
            if (plan.Terraform.Count > 0) Assert.Contains(plan.Issues, i => i.Severity == IssueSeverity.Warning && (i.Key == TerrainShaper.PermanentKey || i.Key == "Placemaker.Park.Issue.LakePermanent"));
        }

        protected void AssertClean(AreaPlan plan, float3[] ring, SiteContext site, string what)
        {
            Assert.False(plan.HasErrors, what + ": " + string.Join(",", plan.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
            AssertInside(plan, ring);
            AssertNothingOnPaths(plan);
            TestKit.AssertSurfacesAccepted(plan);
            AssertTerraformBlended(plan, ring, site.HeightAt);
        }

        // ───────────── definition of done ─────────────

        [Fact]
        public void HasFourToSixPresetsAndTidyControls()
        {
            Assert.InRange(Gen.Presets.Count, 4, 6);
            var real = Gen.Controls.Where(c => c.Kind != ControlKind.Markers && !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).ToList();
            Assert.InRange(real.Count, 15, 40);
            Assert.InRange(real.Select(c => c.Group).Distinct().Count(), 4, 8);
            Assert.Contains(Gen.Controls, c => c.Id == "subtype" && c.Kind == ControlKind.Choice);
            Assert.Equal(ExpectedGroup, GeneratorGroups.Of(Gen));
            // Presets differ in their subtype or in at least three values (not just a density tweak).
            var ps = Gen.Presets.ToList();
            for (int i = 0; i < ps.Count; i++)
                for (int j = i + 1; j < ps.Count; j++)
                {
                    int diff = ps[i].Values.Values.Count(kv => !Equals(kv.Value, ps[j].Values.Values.TryGetValue(kv.Key, out var o) ? o : null) && !(kv.Value is string[]));
                    Assert.True(diff >= 3, $"{ps[i].Id} and {ps[j].Id} are too similar");
                }
        }

        [Fact]
        public void EveryPresetPlansOneHectareAndSixHectareL()
        {
            foreach (var p in Gen.Presets)
                foreach (var (ring, site, name) in new[] { (OneHa, TestKit.RoadAlongBottom(100), "1 ha"), (SixHaL, TestKit.RoadAlongBottom(293), "6 ha L") })
                {
                    var plan = Compose(WithPreset(p.Id), ring, site);
                    Assert.True(string.IsNullOrEmpty(Errors(plan)), p.Id + "/" + name + ": " + Errors(plan));
                    var layer = Layer(plan);
                    AssertClean(layer, ring, site, p.Id + "/" + name);
                    Assert.True(layer.Objects.Count >= 5, $"{p.Id}/{name}: only {layer.Objects.Count} objects");
                    Assert.Contains(layer.Regions, r => r.Tags.Contains(RegionTags.Core));
                    Out.WriteLine($"{p.Id}/{name}: {layer.Objects.Count} objects, {layer.Nets.Count} nets, {layer.Surfaces.Count} surfaces, {layer.Terraform.Count} terraform");
                }
        }

        [Fact]
        public void EveryPresetIsDeterministic()
        {
            foreach (var p in Gen.Presets)
            {
                var a = Layer(Compose(WithPreset(p.Id), SixHaL, TestKit.RoadAlongBottom(293)));
                var b = Layer(Compose(WithPreset(p.Id), SixHaL, TestKit.RoadAlongBottom(293)));
                Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            }
        }

        [Fact]
        public void EveryPresetIsFastOnFiveHectares()
        {
            foreach (var p in Gen.Presets)
            {
                Compose(WithPreset(p.Id), FiveHa, TestKit.RoadAlongBottom(250));   // JIT
                long best = long.MaxValue;
                for (int k = 0; k < 2; k++)
                {
                    var sw = Stopwatch.StartNew();
                    Compose(WithPreset(p.Id), FiveHa, TestKit.RoadAlongBottom(250));
                    best = Math.Min(best, sw.ElapsedMilliseconds);
                }
                Out.WriteLine($"{p.Id}: {best} ms");
                Assert.True(best < TimingBudget.Ms(250), $"{p.Id} took {best} ms for 5 ha");
            }
        }

        [Fact]
        public void RespectsReservedSpaceAndSlopedGround()
        {
            var ring = TestKit.Rect(220, 180);
            var site = new SiteContext { HeightAt = p => 0.03f * p.x + 0.01f * p.y };
            foreach (var p in Gen.Presets)
            {
                var input = TestKit.Input(ring, p.Values, 7, null, site);
                input.Reserved.AddDisc(new float2(110, 90), 14f, "monument");
                input.Reserved.AddCorridor(new float2(0, 40), new float2(220, 40), 3f, nameof(NetRole.Pathway));
                var plan = Gen.Plan(input, CancellationToken.None);
                AssertClean(plan, ring, site, p.Id + " reserved");
                TestKit.AssertClearOfReserved(plan, input.Reserved);
                foreach (var n in plan.Nets.Where(n => n.Role != NetRole.Fence))
                    for (int k = 0; k <= 8; k++)
                    {
                        var q = Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz;
                        Assert.True(math.distance(q, new float2(110, 90)) >= 14f, $"{p.Id}: {n.Role} crosses the reserved disc at {q}");
                    }
            }
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(Gen).Select(f => f.Layer).ToArray();
            Assert.NotEmpty(features);
            foreach (var preset in Gen.Presets.Select(p => p.Id).Take(2))
            {
                var plan = Compose(WithPreset(preset, features), FiveHa, TestKit.RoadAlongBottom(250));
                Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
                foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);
                foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            }
        }

        [Fact]
        public void EveryKeyHasEnglishText()
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            var gens = new List<IAreaGenerator> { Gen };
            foreach (var f in Features.Of(Gen)) { Need(f.LabelKey); Need(f.TooltipKey); gens.Add(TestKit.Gen(f.Layer)); }
            foreach (var g in gens)
            {
                Need(g.NameKey);
                foreach (var c in g.Controls)
                {
                    Need(c.LabelKey); Need(c.TooltipKey);
                    if (!string.IsNullOrEmpty(c.Group)) Need("Placemaker.UI.Group." + c.Group);
                    foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                }
                foreach (var p in g.Presets) Need(p.LabelKey);
            }
            var recipe = TestKit.LoadRecipe(LayerId + ".json");
            Need(recipe.NameKey);
            foreach (var f in typeof(AssetCategories).GetFields()) Need(Keys.Category((string)f.GetValue(null)));
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
            // Labels stay short (≤ 3 words).
            foreach (var c in Gen.Controls) Assert.True(lang[c.LabelKey].Split(' ').Length <= 3, c.LabelKey + " label too long");
        }

        [Fact]
        public void SchemaRandomizeCancellationAndRecipe()
        {
            var schema = new ParkSchemaTests();
            schema.SchemaIsWellFormed(LayerId);
            schema.PresetsAreValidAgainstSchema(LayerId);
            schema.RandomizeStaysInRangeAndRespectsLocked(LayerId);
            schema.CancellationIsHonoured(LayerId);
            schema.ShippedRecipesLoadAndCompose(LayerId + ".json");
            foreach (var f in Features.Of(Gen))
            {
                schema.SchemaIsWellFormed(f.Layer);
                schema.RandomizeStaysInRangeAndRespectsLocked(f.Layer);
            }
        }
    }
}
