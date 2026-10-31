// Schema-level checks for every layer of the park/plaza/split/add-on workstream: localisation,
// presets, defaults, randomisation and the shipped recipes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ParkSchemaTests
    {
        public static readonly string[] Layers =
            { "park", "plaza", "split", "campground", "fountain", "playground", "restrooms", "picnic", "bigtrees", "flowerbeds",
              "terrain", "decals", "shops", "rocks", "kiosk",
              "botanicalGarden", "sportsPark", "dogPark", "picnicGrounds", "natureReserve", "formalGarden", "communityGarden", "marketSquare",
              "skatePark", "cemeteryGrounds", "greenwayPark" };

        public static IEnumerable<object[]> LayerIds() => Layers.Select(l => new object[] { l });

        [Theory]
        [MemberData(nameof(LayerIds))]
        public void EveryKeyHasEnglishText(string id)
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
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryLiteralKeyInSourceHasEnglishText()
        {
            var lang = TestKit.Lang();
            var root = TestKit.RepoRoot();
            var missing = new List<string>();
            foreach (var dir in new[] { "Park", "Plaza", "Split", "AddOns", "Common", "Leisure" })
                foreach (var file in Directory.GetFiles(Path.Combine(root, "Core", "Layers", dir), "*.cs"))
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                        if (!lang.ContainsKey(m.Groups[1].Value)) missing.Add(m.Groups[1].Value + " (" + Path.GetFileName(file) + ")");
            foreach (var f in typeof(AssetCategories).GetFields())
                if (!lang.ContainsKey(Keys.Category((string)f.GetValue(null)))) missing.Add(Keys.Category((string)f.GetValue(null)));
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }

        [Theory]
        [MemberData(nameof(LayerIds))]
        public void SchemaIsWellFormed(string id)
        {
            var g = TestKit.Gen(id);
            Assert.Equal(g.Controls.Count, g.Controls.Select(c => c.Id).Distinct().Count());
            var defaults = g.Defaults();
            foreach (var c in g.Controls)
            {
                switch (c.Kind)
                {
                    case ControlKind.Slider:
                        Assert.True(c.Min < c.Max && c.Step > 0, c.Id);
                        double v = defaults.Num(c.Id, double.NaN);
                        Assert.InRange(v, c.Min, c.Max);
                        Assert.False(string.IsNullOrEmpty(c.Unit), c.Id + " has no unit");
                        break;
                    case ControlKind.Choice:
                        Assert.Contains(defaults.S(c.Id, null), c.Options.Select(o => o.Value));
                        break;
                    case ControlKind.Asset:
                    case ControlKind.AssetMulti:
                        Assert.False(string.IsNullOrEmpty(c.AssetCategory), c.Id);
                        break;
                    case ControlKind.Markers:
                        Assert.NotEmpty(c.MarkerKinds);
                        break;
                }
                if (!string.IsNullOrEmpty(c.VisibleWhen))
                    Assert.NotNull(g.Controls.FirstOrDefault(o => o.Id == c.VisibleWhen.Split(new[] { "!=", "=" }, StringSplitOptions.None)[0]));
            }
        }

        [Theory]
        [MemberData(nameof(LayerIds))]
        public void PresetsAreValidAgainstSchema(string id)
        {
            var g = TestKit.Gen(id);
            foreach (var p in g.Presets)
            {
                foreach (var kv in p.Values.Values)
                {
                    var c = g.Controls.FirstOrDefault(x => x.Id == kv.Key);
                    Assert.True(c != null, $"{id}/{p.Id}: unknown control {kv.Key}");
                    AssertValueFits(c, kv.Value, $"{id}/{p.Id}");
                }
                var plan = g.Plan(TestKit.Input(TestKit.Rect(120, 90), p.Values), CancellationToken.None);
                Assert.False(plan.HasErrors, $"{id}/{p.Id} fails to plan");
            }
        }

        [Theory]
        [MemberData(nameof(LayerIds))]
        public void RandomizeStaysInRangeAndRespectsLocked(string id)
        {
            var g = TestKit.Gen(id);
            var current = g.Defaults();
            // Lock every control with a non-default-looking value.
            foreach (var c in g.Controls)
            {
                if (c.Kind == ControlKind.Slider) current.Values[c.Id] = c.Max;
                if (c.Kind == ControlKind.Choice) current.Values[c.Id] = c.Options.Last().Value;
                if (c.Kind == ControlKind.Toggle) current.Values[c.Id] = !(bool)c.Default;
                if (c.Kind == ControlKind.Arrangement) current.Values[c.Id] = new[] { "lamp" };
            }
            var all = new HashSet<string>(g.Controls.Select(c => c.Id));
            for (uint seed = 1; seed < 25; seed++)
            {
                var locked = g.Randomize(current, seed, all);
                Assert.True(locked.ContentEquals(current), $"{id}: Randomize changed a locked control (seed {seed})");
                var free = g.Randomize(current, seed, new HashSet<string>());
                foreach (var kv in free.Values)
                {
                    var c = g.Controls.FirstOrDefault(x => x.Id == kv.Key);
                    if (c != null) AssertValueFits(c, kv.Value, id + " randomized");
                }
                Assert.True(free.ContentEquals(g.Randomize(current, seed, new HashSet<string>())), "Randomize is not deterministic");
                // Locking one control never changes what the others roll.
                foreach (var c in g.Controls)
                {
                    var one = g.Randomize(current, seed, new HashSet<string> { c.Id });
                    foreach (var other in g.Controls)
                    {
                        if (other.Id == c.Id || !free.Values.ContainsKey(other.Id)) continue;
                        Assert.True(SameValue(free.Values[other.Id], one.Values[other.Id]), $"{id}: locking {c.Id} changed {other.Id}");
                    }
                    Assert.True(SameValue(current.Values.TryGetValue(c.Id, out var cv) ? cv : null, one.Values.TryGetValue(c.Id, out var ov) ? ov : null));
                }
            }
        }

        [Theory]
        [MemberData(nameof(LayerIds))]
        public void CancellationIsHonoured(string id)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => TestKit.Gen(id).Plan(TestKit.Input(TestKit.Rect(150, 120)), cts.Token));
        }

        [Fact]
        public void TinySubRegionWarnsInsteadOfBlockingTheRecipe()
        {
            var input = TestKit.Input(TestKit.Rect(8, 8));
            input.RegionTags = new[] { RegionTags.Green, "green:4" };
            var plan = TestKit.Plan("park", input);
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.Issues, i => i.Key == Keys.AreaTooSmall && i.Severity == IssueSeverity.Warning);
            Assert.True(TestKit.Plan("park", TestKit.Input(TestKit.Rect(8, 8))).HasErrors);
        }

        private static bool SameValue(object a, object b) =>
            a is string[] x && b is string[] y ? x.SequenceEqual(y) : Equals(a, b);

        private static void AssertValueFits(ControlSchema c, object value, string where)
        {
            switch (c.Kind)
            {
                case ControlKind.Slider:
                    double d = Convert.ToDouble(value);
                    Assert.InRange(d, c.Min, c.Max);
                    break;
                case ControlKind.Toggle: Assert.IsType<bool>(value); break;
                case ControlKind.Choice: Assert.Contains((string)value, c.Options.Select(o => o.Value)); break;
                case ControlKind.Asset: Assert.IsType<string>(value); break;
                case ControlKind.AssetMulti:
                case ControlKind.Arrangement:
                    Assert.IsType<string[]>(value);
                    if (c.Kind == ControlKind.Arrangement) Assert.InRange(((string[])value).Length, 0, 5);
                    break;
                default: Assert.Fail($"{where}: control {c.Id} of kind {c.Kind} cannot carry a value"); break;
            }
        }

        // ───────────── shipped recipes ─────────────

        public static IEnumerable<object[]> RecipeFiles() =>
            new[] { "park.json", "plaza.json", "split-park-plaza.json", "park-with-playground.json", "campground.json",
                "botanicalGarden.json", "sportsPark.json", "dogPark.json", "picnicGrounds.json", "natureReserve.json", "formalGarden.json",
                "communityGarden.json", "marketSquare.json", "cemeteryGrounds.json", "greenwayPark.json" }.Select(f => new object[] { f });

        [Theory]
        [MemberData(nameof(RecipeFiles))]
        public void ShippedRecipesLoadAndCompose(string file)
        {
            var recipe = TestKit.LoadRecipe(file);
            Assert.Equal(Path.GetFileNameWithoutExtension(file), recipe.Id);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey), "missing recipe name " + recipe.NameKey);
            Validate(recipe.Root, null);
            var area = new AreaShape { Ring = TestKit.Rect(220, 160) };
            var plan = Composer.Compose(recipe, area, TestKit.RoadAlongBottom(220), CancellationToken.None);
            Assert.False(plan.HasErrors, string.Join("; ", plan.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args))));
            Assert.True(plan.Layers.Count >= 1 + CountSteps(recipe.Root) - 1, "a child step found no region to run in");
        }

        private static int CountSteps(RecipeStep s) => 1 + s.Children.Sum(CountSteps);

        private static void Validate(RecipeStep step, IAreaGenerator parent)
        {
            var g = GeneratorRegistry.Get(step.Layer);
            Assert.True(g != null, "unknown layer " + step.Layer);
            foreach (var kv in step.Settings.Values)
            {
                var c = g.Controls.FirstOrDefault(x => x.Id == kv.Key);
                Assert.True(c != null, $"{step.Layer}: unknown setting {kv.Key}");
                AssertValueFits(c, kv.Value, step.Layer);
            }
            if (parent != null && !string.IsNullOrEmpty(step.Target))
                Assert.Contains(step.Target, parent.Emits);
            foreach (var ch in step.Children) Validate(ch, g);
        }
    }
}
