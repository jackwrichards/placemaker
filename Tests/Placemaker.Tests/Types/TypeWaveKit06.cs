// Shared checks for builder 6's types (wetland, zen garden): the type-wave definition of done —
// every preset plans 1 ha and a 6 ha L-shape cleanly and deterministically, nothing outside the area,
// on a path or in reserved space, surfaces CS2 keeps, terraform blended at the outline, < 250 ms for
// 5 ha, features compose, every key has English text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests.Types
{
    /// <summary>
    /// (Phase 1) Add-ons that became settings of their host (LookSettings: big trees, rocky areas, water, shore, the neglect
    /// clutter…) are no longer in Features.Of; a type still offers them, as its look control. DesignEditor.SetFeature on a
    /// look sets the setting, and the composer makes the feature child from it, so the feature layer is still planned.
    /// </summary>
    public static class Phase1Looks
    {
        /// <summary>The real add-ons (Features.Of) and the features the type's look settings drive.</summary>
        public static string[] AllFeatures(IAreaGenerator gen) =>
            Features.Of(gen).Select(f => f.Layer).Concat(LookSettings.Of(gen).Select(x => x.feature.Layer)).Distinct().ToArray();

        /// <summary>The type offers the feature: as an add-on, or as its look setting (the control is there).</summary>
        public static bool Offers(IAreaGenerator gen, string layer) =>
            Features.Of(gen).Any(f => f.Layer == layer) || LookSettings.IsLookOf(gen, layer);
    }

    public static class TypeWaveKit06
    {
        public static readonly float3[] OneHa = TestKit.Rect(100, 100);
        /// <summary>L-shape of 6 ha (293.3² · 0.6975).</summary>
        public static readonly float3[] SixHaL = TestKit.LShape(293.3f);
        public static readonly float3[] FiveHa = TestKit.Rect(250, 200);

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static AreaPlan PlanPreset(string id, string preset, float3[] ring, SiteContext site = null, uint seed = 7, ReservedSpace reserved = null)
        {
            var gen = TestKit.Gen(id);
            var p = gen.Presets.Single(x => x.Id == preset);
            var input = TestKit.Input(ring, p.Values.Clone(), seed, null, site);
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

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

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
            foreach (var t in plan.Terraform)
                foreach (var p in t.Ring)
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.1f, $"{plan.GeneratorId}: terraform at {p.xz} outside");
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

        /// <summary>
        /// Terraform never makes a cliff at the outline: the change at a piece's centre is at most half its
        /// distance to the outline (+0.35 m), and water basins stay at least 3 m inside.
        /// </summary>
        public static void AssertTerraformBlended(AreaPlan plan, float3[] ring, Func<float2, float> height)
        {
            var r = Poly.To2D(ring);
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent, "terraform must be flagged permanent");
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float edge = Poly.DistanceToBoundary(r, c);
                float change = math.abs(t.TargetHeight - height(c));
                Assert.True(change <= edge * 0.5f + 0.35f, $"{plan.GeneratorId}: {change:0.00} m change {edge:0.0} m from the outline");
                if (!float.IsNaN(t.WaterLevel) || change > 1f)
                    foreach (var v in t.Ring)
                        Assert.True(Poly.DistanceToBoundary(r, v.xz) >= 3f, $"{plan.GeneratorId}: basin reaches {v.xz}, too close to the outline");
            }
            if (plan.Terraform.Count > 0)
                Assert.Contains(plan.Issues, i => i.Severity == IssueSeverity.Warning && i.Key.EndsWith("Permanent", StringComparison.Ordinal));
        }

        /// <summary>The full clean-plan check for one preset on one outline.</summary>
        public static AreaPlan AssertPresetClean(string id, string preset, float3[] ring, SiteContext site)
        {
            var plan = PlanPreset(id, preset, ring, site);
            Assert.True(string.IsNullOrEmpty(Errors(plan.Issues)), id + "/" + preset + ": " + Errors(plan.Issues));
            Assert.True(plan.Objects.Count > 10, $"{id}/{preset} placed only {plan.Objects.Count} objects");
            AssertInside(plan, ring);
            AssertNothingOnPaths(plan);
            TestKit.AssertSurfacesAccepted(plan);
            AssertTerraformBlended(plan, ring, site.HeightAt);
            Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core));
            return plan;
        }

        public static void AssertDeterministic(string id, string preset, float3[] ring, SiteContext site)
        {
            var a = PlanPreset(id, preset, ring, site);
            var b = PlanPreset(id, preset, ring, site);
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            Assert.Equal(a.Terraform.Count, b.Terraform.Count);
            Assert.Equal(a.Paint.Count, b.Paint.Count);
        }

        public static void AssertRespectsReserved(string id, string preset)
        {
            var reserved = new ReservedSpace();
            reserved.AddDisc(new float2(125, 100), 14f, "Furniture");
            reserved.AddCorridor(new float2(20, 150), new float2(230, 150), 3f, "Street");
            var plan = PlanPreset(id, preset, FiveHa, TestKit.RoadAlongBottom(250), 9, reserved);
            Assert.False(plan.HasErrors, Errors(plan.Issues));
            TestKit.AssertClearOfReserved(plan, reserved);
        }

        public static long TimeFiveHa(string id, string preset)
        {
            PlanPreset(id, preset, FiveHa, TestKit.RoadAlongBottom(250));   // JIT
            var sw = Stopwatch.StartNew();
            PlanPreset(id, preset, FiveHa, TestKit.RoadAlongBottom(250));
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        public static void AssertFeaturesCompose(string id)
        {
            var gen = GeneratorRegistry.Get(id);
            var features = Features.Of(gen).Select(f => f.Layer).ToArray();
            Assert.NotEmpty(features);
            var plan = Compose(With(id, features), FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(Errors(plan.AllIssues())), Errors(plan.AllIssues()));
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
        }

        /// <summary>Every key the type, its features, its source files, its categories and its recipe use has English text.</summary>
        public static void AssertKeys(string id, string sourceFolder, string recipeFile, params string[] categories)
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            var gens = new List<IAreaGenerator> { TestKit.Gen(id) };
            foreach (var f in Features.Of(gens[0]))
            {
                Need(f.LabelKey); Need(f.TooltipKey);
                gens.Add(TestKit.Gen(f.Layer));
            }
            foreach (var g in gens)
            {
                Need(g.NameKey);
                foreach (var c in g.Controls)
                {
                    Need(c.LabelKey); Need(c.TooltipKey);
                    if (c.Group != "Basics") Need("Placemaker.UI.Group." + c.Group);
                    foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                    Assert.True(!lang.TryGetValue(c.LabelKey, out var label) || label.Split(' ').Length <= 3, c.LabelKey + " longer than 3 words");
                }
                foreach (var p in g.Presets) Need(p.LabelKey);
            }
            foreach (var file in Directory.GetFiles(Path.Combine(TestKit.RepoRoot(), "Core", "Layers", "Types", sourceFolder), "*.cs"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                    Need(m.Groups[1].Value);
            foreach (var c in categories) Need(Keys.Category(c));
            using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestKit.RepoRoot(), "Recipes", recipeFile))))
            {
                Need(doc.RootElement.GetProperty("nameKey").GetString());
                Need(doc.RootElement.GetProperty("descriptionKey").GetString());
                Assert.Equal(id, doc.RootElement.GetProperty("root").GetProperty("layer").GetString());
                var icon = doc.RootElement.GetProperty("icon").GetString();
                Assert.StartsWith("Media/Game/Icons/", icon);
            }
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        /// <summary>15–40 controls in 4–8 groups, a subtype choice first, 4–6 presets that each set a different subtype.</summary>
        public static void AssertSchemaShape(string id, string subtypeControl, string group)
        {
            var g = TestKit.Gen(id);
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 40);   // (Phase 1) look settings not counted
            Assert.InRange(g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Where(x => !Placemaker.Core.Layers.Common.GroundSection.IsGround(x)).Distinct().Count(), 4, 8);   // (Phase 1) look settings and the shared Ground section (terrain paint, ground cover, shape) not counted
            var sub = g.Controls.First();
            Assert.Equal(subtypeControl, sub.Id);
            Assert.Equal(ControlKind.Choice, sub.Kind);
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.Equal(g.Presets.Count, g.Presets.Select(p => p.Values.S(subtypeControl, "")).Distinct().Count());
            Assert.Equal(group, GeneratorGroups.Of(g));
            Assert.False(GeneratorRegistry.IsHidden(g));
        }
    }
}
