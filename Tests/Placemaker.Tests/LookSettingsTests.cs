// Phase 1 (2026-10-30): looks are settings, not add-ons (Core/Layers/Common/LookSettings.cs); no animal spawns; the size guide;
// the controls a plan never reads (Core/Tool/ControlsShown.Unused).

using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Xunit;

namespace Placemaker.Tests
{
    public class LookSettingsTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Compose(Recipe r, float w = 220f, float h = 170f) =>
            Composer.Compose(r, new AreaShape { Ring = TestKit.Rect(w, h), Source = AreaSource.Drawn }, TestKit.RoadAlongBottom(w), CancellationToken.None);

        private static Recipe Park(params (string id, object v)[] values)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 11);
            Editor.AddDefaultFeatures(r, () => 5);
            foreach (var (id, v) in values) r.Root.Settings.Values[id] = v;
            return TestKit.Entered(r, TestKit.Rect(220, 170), TestKit.RoadAlongBottom(220));
        }

        [Fact]
        public void LookFeaturesAreSettingsNotAddOns()
        {
            var park = GeneratorRegistry.Get("park");
            foreach (var layer in new[] { "bigtrees", "flowerbeds", "pond", "rocks", "terrain", "ground", "shore", "terraceGarden" })
                Assert.DoesNotContain(Features.Of(park), f => f.Layer == layer);
            foreach (var id in new[] { "lookBigTrees", "lookFlowerbeds", "lookWater", "lookRockyAreas", "lookTerrainShape", "lookGroundPatches", "lookShore", "lookTerraceGarden" })
                Assert.Contains(park.Controls, c => c.Id == id);
            // the pond's own option (water size) shows under the setting, only while there is water
            var lake = park.Controls.First(c => c.Id == "lakeSize");
            Assert.Equal("water", lake.Group);
            Assert.Equal("lookWater!=none", lake.VisibleWhen);
            // real facilities stay add-ons
            Assert.Contains(Features.Of(park), f => f.Layer == "playground");
            Assert.Contains(Features.Of(park), f => f.Layer == "restrooms");
        }

        [Fact]
        public void TheSettingMakesTheFeatureChildAtComposeTime()
        {
            var none = Compose(Park(("lookWater", "none")));
            Assert.DoesNotContain(none.Layers, l => l.GeneratorId == "pond");
            var pond = Compose(Park(("lookWater", "pond")));
            var lake = Compose(Park(("lookWater", "lake")));
            Assert.Contains(pond.Layers, l => l.GeneratorId == "pond");
            Assert.Contains(lake.Layers, l => l.GeneratorId == "pond");
            // a lake is more water than a pond
            double Water(ComposedPlan p) => p.Layers.Where(l => l.GeneratorId == "pond").SelectMany(l => l.Terraform).Sum(t => Pricing.PolygonArea(t.Ring));
            Assert.True(Water(lake) > Water(pond), $"lake {Water(lake):0} m² vs pond {Water(pond):0} m²");
            // the design itself keeps no child
            Assert.DoesNotContain(Park(("lookWater", "lake")).Root.Children, c => c.Layer == "pond");
            // amounts: none / few / many big trees
            int Trees(ComposedPlan p) => p.Layers.Where(l => l.GeneratorId == "bigtrees").Sum(l => l.Objects.Count);
            Assert.Equal(0, Trees(Compose(Park(("lookBigTrees", "none")))));
            Assert.True(Trees(Compose(Park(("lookBigTrees", "many")))) > Trees(Compose(Park(("lookBigTrees", "few")))));
        }

        [Fact]
        public void SavedDesignsWithTheOldAddOnStillBuildAsBefore()
        {
            // a design saved before Phase 1: a big-trees child with its own density, no setting value
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 11);
            r.Root.Settings.Values.Remove("lookBigTrees");
            var child = new RecipeStep { Layer = "bigtrees", Seed = 3 };
            child.Settings.Values["density"] = 6.0;
            r.Root.Children.Add(child);
            var (look, feature) = LookSettings.Find(GeneratorRegistry.Get("park"), "lookBigTrees");
            Assert.Equal("some", LookSettings.ShownValue(look, feature, r.Root));
            var kids = LookSettings.Children(GeneratorRegistry.Get("park"), r.Root);
            Assert.Same(child, kids.Single(k => k.Layer == "bigtrees"));   // built exactly as saved
            // once the player sets it, the value decides
            r.Root.Settings.Values["lookBigTrees"] = "none";
            Assert.DoesNotContain(LookSettings.Children(GeneratorRegistry.Get("park"), r.Root), k => k.Layer == "bigtrees");
            // a saved design whose child was turned off reads as off
            var off = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 11);
            off.Root.Settings.Values.Remove("lookBigTrees");
            Assert.Equal("none", LookSettings.ShownValue(look, feature, off.Root));
        }

        [Fact]
        public void StylesPresetTheirLooks()
        {
            var park = GeneratorRegistry.Get("park");
            Assert.Equal("lake", park.Presets.First(p => p.Id == "landscape").Values.S("lookWater", ""));
            Assert.Equal("none", park.Presets.First(p => p.Id == "woodland").Values.S("lookBigTrees", ""));
            Assert.DoesNotContain(park.Presets.SelectMany(p => p.Features), x => LookSettings.IsLook(x.TrimStart('-')));
            // the old way of turning one on still works (tests, the AI builder, scenarios): it sets the setting
            var r = Editor.SingleLayerRecipe(park, 11);
            Assert.True(Editor.SetFeature(r, "0", "pond", true, 1));
            Assert.Equal("pond", r.Root.Settings.Values["lookWater"]);
        }

        [Fact]
        public void NoAnimalSpawnsAnywhere()
        {
            foreach (var id in new[] { "park", "natureReserve", "wetland", "hikingTrail", "animalPark", "farmstead" })
            {
                var g = GeneratorRegistry.Get(id);
                var r = Editor.SingleLayerRecipe(g, 3);
                foreach (var p in g.Presets) foreach (var kv in p.Values.Values) if (kv.Key == "animals") r.Root.Settings.Values[kv.Key] = true;
                r.Root.Settings.Values["animals"] = true;
                var plan = Compose(TestKit.Entered(r, TestKit.Rect(220, 170), TestKit.RoadAlongBottom(220)));
                Assert.DoesNotContain(plan.Layers.SelectMany(l => l.Objects), o => o.Role == ObjectRole.AnimalSpawner);
                Assert.DoesNotContain(g.Controls, c => c.Id == "animals" && ControlsShown.IsShown(g, c));
            }
            Assert.DoesNotContain(Features.Of(GeneratorRegistry.Get("wetland")), f => f.Layer == "wildlifeHabitat");
        }

        [Fact]
        public void TooBigWarnsButNeverBlocks()
        {
            var plaza = GeneratorRegistry.Get("plaza");
            Assert.Null(SizeGuide.Warning(plaza, 10_000));
            var w = SizeGuide.Warning(plaza, 60_000);
            Assert.NotNull(w);
            Assert.Equal(SizeGuide.TooBigKey, w.Key);
            Assert.Equal(2.5, w.Args[0]);
            Assert.Null(SizeGuide.Warning(GeneratorRegistry.Get("park"), 100_000));
            Assert.NotNull(SizeGuide.Warning(GeneratorRegistry.Get("park"), 200_000));
        }

        [Fact]
        public void ControlsTheStyleNeverReadsAreGreyedOut()
        {
            // the amphitheatre's stone theatre lays out its own seating: rows, span and rise do nothing there
            var g = GeneratorRegistry.Get("amphitheatre");
            var r = Editor.SingleLayerRecipe(g, 3);
            r.Root.Settings.Values["style"] = "theatre";
            var unused = ControlsShown.Unused(g, Compose(TestKit.Entered(r, TestKit.Rect(220, 170), TestKit.RoadAlongBottom(220))), "0");
            Assert.Contains("rows", unused);
            Assert.DoesNotContain("style", unused);
            r.Root.Settings.Values["style"] = "classic";
            var classic = ControlsShown.Unused(g, Compose(TestKit.Entered(r, TestKit.Rect(220, 170), TestKit.RoadAlongBottom(220))), "0");
            Assert.DoesNotContain("rows", classic);
        }

        [Fact]
        public void EveryTypeHasOneGroundSectionWithItsDecalSwitch()
        {
            foreach (var g in GeneratorRegistry.All.Where(x => x.Kind == LayerKind.Fill && !GeneratorRegistry.IsHidden(x) && x is LayerBase))
            {
                var decals = g.Controls.FirstOrDefault(c => c.Id == Placemaker.Core.Decals.DressingControls.GroundDecalsId);
                Assert.True(decals != null, g.Id + " has no Decals switch");
                Assert.Equal(GroundSection.Paint, decals.Group);
            }
            // the nature reserve's forest floor sits with the rest of the terrain paint
            Assert.Equal(GroundSection.Paint, GeneratorRegistry.Get("natureReserve").Controls.First(c => c.Id == "forestFloor").Group);
        }
    }
}
