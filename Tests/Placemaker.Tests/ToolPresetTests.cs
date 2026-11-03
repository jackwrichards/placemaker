using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Xunit;

namespace Placemaker.Tests
{
    public class ToolPresetTests
    {
        /// <summary>Fill type with built-in presets "big" (fake generator) and one DefaultOn + one optional feature.</summary>
        private sealed class Host : IAreaGenerator, IHasFeatures
        {
            private readonly ToolFakeGenerator _inner = new ToolFakeGenerator { Id = "host" };
            public IReadOnlyList<FeatureInfo> Features { get; } = new List<FeatureInfo>
            {
                new FeatureInfo { Layer = "kid", Target = "core", DefaultOn = true },
                new FeatureInfo { Layer = "pond", Target = "" },
            };
            public string Id => "host";
            public string NameKey => "N.Host";
            public string Icon => "";
            public LayerKind Kind => LayerKind.Fill;
            public string[] Accepts => _inner.Accepts;
            public string[] Emits => _inner.Emits;
            public int Version => 1;
            public IReadOnlyList<ControlSchema> Controls => _inner.Controls;
            public IReadOnlyList<PresetInfo> Presets => _inner.Presets;
            public SettingsBag Defaults() => _inner.Defaults();
            public float MinArea => 10;
            public float MaxArea => 1e7f;
            public Readiness CheckReady(AreaInput input) => new Readiness();
            public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();
            public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current.Clone();
            public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();
        }

        private readonly Dictionary<string, IAreaGenerator> _gens = new Dictionary<string, IAreaGenerator>
        {
            ["host"] = new Host(),
            ["kid"] = new ToolFakeGenerator { Id = "kid", Kind = LayerKind.AddOn },
            ["pond"] = new ToolFakeGenerator { Id = "pond", Kind = LayerKind.AddOn },
            ["apiary"] = new ToolFakeGenerator { Id = "apiary" },
        };

        private DateTime _now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        private DesignEditor Editor() => new DesignEditor(id => id != null && _gens.TryGetValue(id, out var g) ? g : null);
        private PresetStore Store(DesignEditor e = null) => new PresetStore(e ?? Editor(), () => _now);

        private RecipeStep Fresh(DesignEditor e)
        {
            var r = e.SingleLayerRecipe(_gens["host"], 3);
            e.AddDefaultFeatures(r, () => 9);
            return r.Root;
        }

        [Fact]
        public void BuiltInApplyIsDefaultsPlusValuesAndDefaultFeatures()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            step.Settings.Values["trees"] = false;
            Assert.True(e.SetFeature(new Recipe { Root = step }, "0", "pond", true, 4));
            step.Markers.Add(new EdgeMarker { Kind = "entrance" });

            Assert.True(s.Apply(step, "big", () => 77));
            Assert.Equal(90.0, step.Settings.Num("size", 0));
            Assert.Equal("wild", step.Settings.S("style", ""));
            Assert.True(step.Settings.B("trees", false));              // back to default
            Assert.Contains("size", step.Locked);
            Assert.Single(step.Children);                              // pond off, kid (DefaultOn) on
            Assert.Equal("kid", step.Children[0].Layer);
            Assert.Equal(9u, step.Children[0].Seed);                   // feature that stayed keeps its seed
            Assert.Single(step.Markers);                               // markers stay
            Assert.False(s.IsModified(step, "big"));
            Assert.False(s.Apply(step, "big"));                        // idempotent
            Assert.False(s.Apply(step, "nope"));
        }

        [Fact]
        public void ModifiedTracksSettingsFeaturesAndFeatureSettings()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            s.Apply(step, "big");
            var r = new Recipe { Root = step };

            step.Settings.Values["size"] = 40.0;
            Assert.True(s.IsModified(step, "big"));
            step.Settings.Values["size"] = 90;                         // int vs double: same value
            Assert.False(s.IsModified(step, "big"));

            e.SetFeature(r, "0", "pond", true, 1);
            Assert.True(s.IsModified(step, "big"));
            e.SetFeature(r, "0", "pond", false, 1);
            Assert.False(s.IsModified(step, "big"));

            step.Children[0].Settings.Values["size"] = 11.0;
            Assert.True(s.IsModified(step, "big"));
            step.Children[0].Settings.Values["size"] = 50.0;
            step.Seed = 999;                                           // seeds/locks never count
            step.Locked.Clear();
            Assert.False(s.IsModified(step, "big"));
            Assert.True(s.IsModified(step, "unknown"));
        }

        [Fact]
        public void SaveOverBuiltInCreatesOverrideAndResetRestoresIt()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            s.Apply(step, "big");
            step.Settings.Values["size"] = 70.0;
            e.SetFeature(new Recipe { Root = step }, "0", "pond", true, 1);
            Assert.True(s.IsModified(step, "big"));

            var o = s.Save(step, "big");
            Assert.NotNull(o);
            Assert.True(o.IsOverride);
            Assert.False(s.IsModified(step, "big"));
            var views = s.ViewsFor(_gens["host"]);
            Assert.Equal(new[] { PresetStore.StandardId, "big" }, views.ConvertAll(v => v.Id).ToArray());
            Assert.True(views[1].Overridden);
            Assert.False(views[1].Custom);
            Assert.Equal("P.Big", views[1].Name);
            Assert.False(views[0].Overridden);

            // Applying the overridden built-in reproduces the saved step, features included.
            var other = Fresh(e);
            Assert.True(s.Apply(other, "big", () => 5));
            Assert.Equal(70.0, other.Settings.Num("size", 0));
            Assert.Equal(2, other.Children.Count);
            Assert.False(s.IsCustom("host", "big"));
            Assert.False(s.Rename("host", "big", "x"));

            Assert.True(s.Reset("host", "big"));
            Assert.False(s.ViewsFor(_gens["host"])[1].Overridden);
            Assert.True(s.IsModified(step, "big"));
            Assert.False(s.Reset("host", "big"));
            Assert.False(s.Delete("host", "big"));                     // plain built-in: nothing to delete
            Assert.Null(s.Save(step, "nope"));
        }

        [Fact]
        public void SaveAsRenameDeleteCustom()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            s.Apply(step, "big");
            step.Settings.Values["style"] = "formal";

            var a = s.SaveAs(step, "  Shady grove  ", "big");
            Assert.Equal("custom-shady-grove", a.Id);
            Assert.Equal("Shady grove", a.Name);
            Assert.Equal("big", a.BasedOn);
            Assert.False(s.IsModified(step, a.Id));
            _now = _now.AddMinutes(1);
            var b = s.SaveAs(step, "Shady grove!", a.Id);              // based on a custom → its origin
            Assert.Equal("custom-shady-grove-2", b.Id);
            Assert.Equal("big", b.BasedOn);
            _now = _now.AddMinutes(1);
            Assert.Equal("My preset", s.SaveAs(step, "   ").Name);

            var views = s.ViewsFor(_gens["host"]);
            Assert.Equal(new[] { PresetStore.StandardId, "big", a.Id, b.Id, "custom-my-preset" }, views.ConvertAll(v => v.Id).ToArray());
            Assert.True(views[2].Custom);
            Assert.Equal("big", views[2].BasedOn);
            Assert.Equal("Shady grove", views[2].Name);

            // Save over a custom overwrites it.
            step.Settings.Values["size"] = 20.0;
            Assert.True(s.IsModified(step, a.Id));
            Assert.Same(a, s.Save(step, a.Id));
            Assert.False(a.IsOverride);
            Assert.False(s.IsModified(step, a.Id));

            Assert.True(s.Rename("host", a.Id, "Deep shade"));
            Assert.Equal("Deep shade", s.Find("host", a.Id).Name);
            Assert.True(s.Delete("host", a.Id));
            Assert.Null(s.Find("host", a.Id));
            Assert.False(s.Exists("host", a.Id));

            var active = s.Active(step, "host", b.Id);
            Assert.Equal(b.Id, active.Id);
            Assert.True(active.CanSave);
            Assert.True(active.Modified);
            var gone = s.Active(step, "host", a.Id);                 // deleted → the type's Standard, changed
            Assert.Equal(PresetStore.StandardId, gone.Id);
            Assert.True(gone.Modified);
            Assert.True(gone.CanSave);
        }

        // ───────────── Standard (the old "Default": the type's own design, now a style like the others) ─────────────

        [Fact]
        public void AFreshTypeIsOnStandardUnchangedAndItIsListedFirst()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            foreach (var none in new[] { "", null, "nope" })
            {
                var a = s.Active(step, "host", none);                 // no style named (old saves, built groups) → Standard
                Assert.Equal(PresetStore.StandardId, a.Id);
                Assert.Equal("host", a.Layer);
                Assert.False(a.Modified);
                Assert.True(a.CanSave);
            }
            Assert.Equal(PresetStore.StandardId, s.Active(step, "other", "big").Id);   // another type's style → this type's Standard
            var views = s.ViewsFor(_gens["host"]);
            Assert.Equal(PresetStore.StandardId, views[0].Id);
            Assert.Equal(PresetStore.StandardNameKey, views[0].Name);
            Assert.False(views[0].Custom);
            Assert.True(s.Exists("host", PresetStore.StandardId));
            Assert.True(s.IsTypeStandard("host", PresetStore.StandardId));
            Assert.NotNull(s.BuiltIn("host", PresetStore.StandardId));
            Assert.False(s.IsCustom("host", PresetStore.StandardId));
        }

        [Fact]
        public void StandardCanBeReselectedAfterAnotherStyle()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            Assert.True(s.Apply(step, "big", () => 7));
            Assert.True(s.IsModified(step, PresetStore.StandardId));
            Assert.True(s.Apply(step, PresetStore.StandardId, () => 8));      // back to the type's own design
            Assert.Equal(50.0, step.Settings.Num("size", 0));
            Assert.Equal("formal", step.Settings.S("style", ""));
            Assert.Empty(step.Locked);                                        // as picking the type: nothing locked (Shuffle varies it)
            Assert.Single(step.Children);
            Assert.Equal("kid", step.Children[0].Layer);                      // DefaultOn feature
            Assert.False(s.IsModified(step, PresetStore.StandardId));
            Assert.False(s.Apply(step, PresetStore.StandardId));              // idempotent
            step.Settings.Values["size"] = 20.0;
            Assert.True(s.Active(step, "host", PresetStore.StandardId).Modified);
        }

        [Fact]
        public void StandardIsTheTypesRecipeWhenOneIsGiven()
        {
            var e = Editor();
            var recipe = Fresh(e);
            recipe.Settings.Values["size"] = 70.0;                           // the type's recipe file sets its own values
            recipe.Locked.Add("trees");
            var s = new PresetStore(e, () => _now, layer => layer == "host" ? recipe : null);
            var std = s.Resolve("host", PresetStore.StandardId);
            Assert.Equal(70.0, std.Settings.Num("size", 0));
            Assert.Contains("trees", std.Locked);
            Assert.Single(std.Children);
            var picked = recipe.Clone();                                      // picking the type: the recipe as it is
            Assert.False(s.IsModified(picked, PresetStore.StandardId));
            Assert.Equal(50.0, Store(e).Resolve("host", PresetStore.StandardId).Settings.Num("size", 0));   // without: the defaults
        }

        [Fact]
        public void StandardCanBeSavedOverAndResetLikeOtherBuiltIns()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            step.Settings.Values["size"] = 33.0;
            var o = s.Save(step, PresetStore.StandardId);
            Assert.NotNull(o);
            Assert.True(o.IsOverride);
            Assert.True(s.ViewsFor(_gens["host"])[0].Overridden);
            Assert.Single(s.ViewsFor(_gens["host"]), v => v.Id == PresetStore.StandardId);   // the override is not listed as a custom
            Assert.False(s.IsModified(step, PresetStore.StandardId));
            var other = Fresh(e);
            Assert.True(s.Apply(other, PresetStore.StandardId));
            Assert.Equal(33.0, other.Settings.Num("size", 0));
            Assert.False(s.Rename("host", PresetStore.StandardId, "x"));      // built-in: no rename
            Assert.True(s.Reset("host", PresetStore.StandardId));
            Assert.False(s.ViewsFor(_gens["host"])[0].Overridden);
            Assert.False(s.Delete("host", PresetStore.StandardId));           // built-in: nothing to delete
            var c = s.SaveAs(step, "From standard", PresetStore.StandardId);
            Assert.Equal(PresetStore.StandardId, c.BasedOn);

            // survives presets.json
            s.Save(step, PresetStore.StandardId);
            var s2 = Store(e);
            s2.Load(s.Write().Write(true));
            Assert.True(s2.Find("host", PresetStore.StandardId).IsOverride);
            Assert.False(s2.IsCustom("host", PresetStore.StandardId));
        }

        [Fact]
        public void AShippedStandardIsUsedAndOnlyFillTypesHaveOne()
        {
            var gens = new Dictionary<string, IAreaGenerator>(_gens) { ["lot"] = new ShippedStandard() };
            var e = new DesignEditor(id => id != null && gens.TryGetValue(id, out var g) ? g : null);
            var s = Store(e);
            var views = s.ViewsFor(gens["lot"]);
            Assert.Equal(new[] { PresetStore.StandardId, "tight" }, views.ConvertAll(v => v.Id).ToArray());   // moved first, not doubled
            Assert.Equal("P.Standard", views[0].Name);
            Assert.False(s.IsTypeStandard("lot", PresetStore.StandardId));
            Assert.Equal(80.0, s.Resolve("lot", PresetStore.StandardId).Settings.Num("size", 0));

            Assert.False(s.Exists("kid", PresetStore.StandardId));            // an add-on has no styles
            Assert.DoesNotContain(s.ViewsFor(gens["kid"]), v => v.Id == PresetStore.StandardId);
            Assert.Equal("", s.Active(new RecipeStep { Layer = "kid", Settings = e.DefaultsFor("kid") }, "", "").Id);
        }

        /// <summary>A Fill type that ships its own "standard" preset (like parking) after another one.</summary>
        private sealed class ShippedStandard : IAreaGenerator
        {
            private readonly ToolFakeGenerator _inner = new ToolFakeGenerator { Id = "lot" };
            public string Id => "lot";
            public string NameKey => "N.Lot";
            public string Icon => "";
            public LayerKind Kind => LayerKind.Fill;
            public string[] Accepts => _inner.Accepts;
            public string[] Emits => _inner.Emits;
            public int Version => 1;
            public IReadOnlyList<ControlSchema> Controls => _inner.Controls;
            public IReadOnlyList<PresetInfo> Presets
            {
                get
                {
                    var tight = new PresetInfo { Id = "tight", LabelKey = "P.Tight" };
                    tight.Values.Values["size"] = 20.0;
                    var std = new PresetInfo { Id = PresetStore.StandardId, LabelKey = "P.Standard" };
                    std.Values.Values["size"] = 80.0;
                    return new List<PresetInfo> { tight, std };
                }
            }
            public SettingsBag Defaults() => _inner.Defaults();
            public float MinArea => 10;
            public float MaxArea => 1e7f;
            public Readiness CheckReady(AreaInput input) => new Readiness();
            public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();
            public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current.Clone();
            public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();
        }

        [Fact]
        public void EveryShownTypeStartsOnItsStandardUnchanged()
        {
            // The real types: picking one (its recipe, here the one-layer recipe, plus its DefaultOn features) is its Standard,
            // also for a type that ships its own "standard" (parking).
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var s = new PresetStore(editor, () => _now);
            var changed = new List<string>();
            int n = 0;
            foreach (var g in GeneratorRegistry.All)
            {
                if (g == null || g.Kind != LayerKind.Fill || GeneratorRegistry.IsHidden(g)) continue;
                var r = editor.SingleLayerRecipe(g, 3);
                editor.AddDefaultFeatures(r, () => 1);
                var a = s.Active(r.Root, "", "");
                Assert.Equal(PresetStore.StandardId, a.Id);
                Assert.Equal(PresetStore.StandardId, s.ViewsFor(g)[0].Id);
                if (a.Modified) changed.Add(g.Id);
                n++;
            }
            Assert.True(n > 10);
            Assert.Empty(changed);
        }

        [Fact]
        public void EveryShippedTypeRecipeIsItsStandardUnchanged()
        {
            // As the game picks a type: its recipe file (values over the defaults) plus its DefaultOn features; the store's
            // Standard is that recipe (the tool hands it the type's recipe), or the type's own "standard" (parking's file).
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var roots = new Dictionary<string, RecipeStep>();
            foreach (var file in System.IO.Directory.GetFiles(System.IO.Path.Combine(TestKit.RepoRoot(), "Recipes"), "*.json"))
            {
                var r = RecipeJson.Parse(System.IO.File.ReadAllText(file)).Recipe;
                var g = GeneratorRegistry.Get(r.Root?.Layer);
                if (g == null || g.Kind != LayerKind.Fill || r.Root.Children.Count > 0 || r.Id != g.Id) continue;
                editor.ApplyDefaults(r);
                roots[g.Id] = r.Root;
            }
            Assert.Contains("parking", roots.Keys);
            Assert.Contains("campground", roots.Keys);
            var s = new PresetStore(editor, () => _now, layer => roots.TryGetValue(layer, out var b) ? b : null);
            var changed = new List<string>();
            foreach (var kv in roots)
            {
                var picked = new Recipe { Root = kv.Value.Clone() };
                editor.AddDefaultFeatures(picked, () => 1);
                var a = s.Active(picked.Root, "", "");
                Assert.Equal(PresetStore.StandardId, a.Id);
                if (a.Modified) changed.Add(kv.Key);
            }
            Assert.Empty(changed);
        }

        [Fact]
        public void CaptureDropsMarkersSeedsAndFreeFormChildren()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            step.Markers.Add(new EdgeMarker { Kind = "entrance" });
            step.Children.Add(new RecipeStep { Layer = "apiary", Target = "block", Seed = 4 });
            step.Children[0].Markers.Add(new EdgeMarker { Kind = "x" });
            var p = s.SaveAs(step, "Mine");
            Assert.Empty(p.Step.Markers);
            Assert.Equal(0u, p.Step.Seed);
            Assert.Single(p.Step.Children);
            Assert.Equal("kid", p.Step.Children[0].Layer);
            Assert.Empty(p.Step.Children[0].Markers);

            // Applying keeps the free-form child of the target step.
            var other = Fresh(e);
            other.Children.Add(new RecipeStep { Layer = "apiary", Target = "block" });
            s.Apply(other, p.Id);
            Assert.Equal(2, other.Children.Count);
            Assert.Equal("apiary", other.Children[1].Layer);
        }

        [Fact]
        public void JsonRoundTripIsReadableAndStable()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            s.Apply(step, "big");
            step.Settings.Values["benches"] = new[] { "b1", "b2" };
            s.Save(step, "big");
            s.SaveAs(step, "Shady grove", "big");

            string text = s.Write().Write(true);
            Assert.Contains("\"formatVersion\": 1", text);
            Assert.Contains("\"layer\": \"host\"", text);
            Assert.Contains("\"basedOn\": \"big\"", text);
            Assert.Contains("\"created\": \"2026-10-13T12:00:00Z\"", text);

            var s2 = Store(e);
            s2.Load(text);
            Assert.Equal(2, s2.All.Count);
            var c = s2.Find("host", "custom-shady-grove");
            Assert.Equal("Shady grove", c.Name);
            Assert.Equal("big", c.BasedOn);
            Assert.Equal(_now, c.Created);
            Assert.True(s2.Find("host", "big").IsOverride);
            Assert.False(s2.IsModified(step, "big"));
            Assert.False(s2.IsModified(step, c.Id));
            Assert.Equal(new[] { "b1", "b2" }, s2.Find("host", "big").Step.Settings.L("benches"));
            Assert.Equal(text, s2.Write().Write(true));
        }

        [Fact]
        public void ReadToleratesUnknownFieldsJunkAndAliases()
        {
            const string text = @"{
              ""formatVersion"": 3, ""somethingNew"": { ""a"": 1 },
              ""presets"": [
                { ""layer"": ""honeyHives"", ""id"": ""custom-bees"", ""name"": ""Bees"", ""extra"": true,
                  ""step"": { ""layer"": ""honeyHives"", ""settings"": { ""count"": 4, ""layout"": ""circle"" }, ""locked"": [""count""],
                              ""markers"": [ { ""kind"": ""entrance"", ""segment"": 0, ""t"": 0.5, ""position"": [1,2,3] } ], ""seed"": 12 } },
                { ""id"": ""no-layer"" },
                42,
                { ""layer"": ""host"", ""id"": ""custom-bees"", ""name"": ""Dup layer ok"" },
                { ""layer"": ""host"", ""id"": ""custom-bees"", ""name"": ""Dup ignored"" }
              ]
            }";
            var list = PresetStore.Read(JsonValue.Parse(text), out int v);
            Assert.Equal(3, v);
            Assert.Equal(2, list.Count);
            var bees = list[0];
            Assert.Equal("apiary", bees.Layer);
            Assert.Equal("apiary", bees.Step.Layer);
            Assert.Equal(4.0, bees.Step.Settings.Num("hives", 0));
            Assert.Equal("ring", bees.Step.Settings.S("layout", ""));
            Assert.Contains("hives", bees.Step.Locked);
            Assert.Empty(bees.Step.Markers);
            Assert.Equal(0u, bees.Step.Seed);
            Assert.Equal("Dup layer ok", list[1].Name);

            var s = Store();
            s.Load(text);
            Assert.NotNull(s.Find("honeyHives", "custom-bees"));      // lookups accept the retired id too
            Assert.Equal(3, s.LoadedFormatVersion);

            Assert.Throws<FormatException>(() => PresetStore.Read(JsonValue.Parse("[1,2]"), out _));
            Assert.Throws<FormatException>(() => PresetStore.Read(JsonValue.Parse("{\"presets\": 5}"), out _));
            Assert.Empty(PresetStore.Read(JsonValue.Parse("{}"), out _));
        }

        [Fact]
        public void StaleSettingIdsAreDroppedOnApply()
        {
            var e = Editor();
            var s = Store(e);
            s.Load("{\"presets\":[{\"layer\":\"host\",\"id\":\"custom-old\",\"name\":\"Old\",\"step\":{\"settings\":{\"size\":500,\"gone\":1},\"children\":[{\"layer\":\"pond\",\"settings\":{\"size\":12}},{\"layer\":\"nofeature\"}]}}]}");
            var step = Fresh(e);
            Assert.True(s.Apply(step, "custom-old"));
            Assert.Equal(100.0, step.Settings.Num("size", 0));         // clamped to the control range
            Assert.False(step.Settings.Values.ContainsKey("gone"));
            Assert.True(step.Settings.B("trees", false));              // new control → default
            Assert.Single(step.Children);
            Assert.Equal("pond", step.Children[0].Layer);
            Assert.Equal(12.0, step.Children[0].Settings.Num("size", 0));
            Assert.False(s.IsModified(step, "custom-old"));
        }

        [Fact]
        public void UiModelsPublishPresetViewsAndActivePreset()
        {
            var e = Editor();
            var s = Store(e);
            var step = Fresh(e);
            s.Save(step, "big");
            s.SaveAs(step, "Mine", "big");
            var cat = UiModels.Catalog(new[] { _gens["host"] }, new RecipeListEntry[0], g => s.ViewsFor(g));
            var presets = cat["layers"].Items[0]["presets"];
            Assert.Equal(3, presets.Count);
            Assert.Equal(PresetStore.StandardId, presets.Items[0]["id"].AsString());
            Assert.Equal(PresetStore.StandardNameKey, presets.Items[0]["name"].AsString());
            Assert.True(presets.Items[1]["overridden"].AsBool());
            Assert.True(presets.Items[2]["custom"].AsBool());
            Assert.Equal("Mine", presets.Items[2]["name"].AsString());
            Assert.Equal("big", presets.Items[2]["basedOn"].AsString());

            var r = new Recipe { Id = "host", Root = step };
            var d = UiModels.Design(r, "host", false, "0", null, null, id => _gens.TryGetValue(id ?? "", out var g) ? g : null,
                s.Active(step, "host", "custom-mine"), "C:/x/presets.json");
            Assert.Equal("custom-mine", d["activePreset"]["id"].AsString());
            Assert.Equal("host", d["activePreset"]["layer"].AsString());
            Assert.False(d["activePreset"]["modified"].AsBool(true));
            Assert.True(d["activePreset"]["canSave"].AsBool());
            Assert.Equal("C:/x/presets.json", d["presetsFile"].AsString());
        }
    }
}
