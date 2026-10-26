// Placemaker's own asset choices are vanilla (the user, 26 Oct: "everything should be vanilla only by default, but nothing
// should stop the user from switching to a non-vanilla thing"): automatic pools hold vanilla items only, a hard-coded pack
// name falls through to its vanilla fallback, and a modded asset is used only when the player picked it.
using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Xunit;

namespace Placemaker.Tests
{
    public class VanillaAssetTests
    {
        private static readonly HashSet<string> Vanilla = new HashSet<string>(StringComparer.Ordinal) { "Pavement Surface 01", "Tiles Surface 01", "Bench01" };
        private static readonly HashSet<string> Installed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Pavement Surface 01", "Tiles Surface 01", "Bench01", "G87 Urban Surfaces Paving Pack Tiles G87 Paving Slab A Grey Surface", "ModBench",
        };

        private static AutoAssetSource Source(Func<string, IReadOnlyList<string>> pool = null) => new AutoAssetSource
        {
            Pool = pool ?? (c => c == AssetCategories.Bench ? new[] { "Bench01" } : Array.Empty<string>()),
            Exists = n => n != null && Installed.Contains(n),
            Vanilla = n => n != null && Vanilla.Contains(n),
        };

        private static ComposedPlan PlanWith(AssetRef surface, AssetRef bench)
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan { GeneratorId = "plaza", StepPath = "0" };
            l.Surfaces.Add(new PlannedSurface { Role = SurfaceRole.Paving, Asset = surface });
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Bench, Asset = bench });
            plan.Layers.Add(l);
            return plan;
        }

        [Fact]
        public void PoolsHoldVanillaItemsOnly()
        {
            var pool = AutoPoolRanking.Build(AssetCategories.Decal, new[] { new PoolCandidate("Slum Sand Dry 01", false), new PoolCandidate("ModDecal", false) });
            Assert.Empty(pool);   // vanilla has nothing for it: left out, never a mod's
            var fences = AutoPoolRanking.Build(AssetCategories.Fence, new[] { new PoolCandidate("ModNetFence", false, true), new PoolCandidate("Fence01", true, true) });
            Assert.Equal(new[] { "Fence01" }, fences);
        }

        [Fact]
        public void AHardCodedPackNameFallsThroughToVanilla()
        {
            var plan = PlanWith(new AssetRef("G87 Urban Surfaces Paving Pack Tiles G87 Paving Slab A Grey Surface", "Tiles Surface 01"), new AssetRef(""));
            AutoAssets.Resolve(plan, Source());
            Assert.Equal("Tiles Surface 01", plan.Layers[0].Surfaces[0].Asset.Id);
            Assert.Equal("Bench01", plan.Layers[0].Objects[0].Asset.Id);
        }

        [Fact]
        public void AModdedAssetThePlayerPickedStays()
        {
            var plan = PlanWith(new AssetRef("G87 Urban Surfaces Paving Pack Tiles G87 Paving Slab A Grey Surface", "Tiles Surface 01"), new AssetRef("ModBench"));
            plan.Picks.Add("G87 Urban Surfaces Paving Pack Tiles G87 Paving Slab A Grey Surface");
            plan.Picks.Add("ModBench");
            AutoAssets.Resolve(plan, Source());
            Assert.Equal("G87 Urban Surfaces Paving Pack Tiles G87 Paving Slab A Grey Surface", plan.Layers[0].Surfaces[0].Asset.Id);
            Assert.Equal("ModBench", plan.Layers[0].Objects[0].Asset.Id);
        }

        [Fact]
        public void TheRecipeSettingsAreThePlayersPicks()
        {
            var root = new RecipeStep { Layer = "plaza" };
            root.Settings.Values["paving"] = "ModPaving";
            root.Settings.Values["benches"] = new[] { "ModBench", "Bench01" };
            root.Settings.Values["furniture"] = 4.0;
            var child = new RecipeStep { Layer = "playground" };
            child.Settings.Values["surface"] = "ModRubber";
            root.Children.Add(child);
            var picks = new HashSet<string>(StringComparer.Ordinal);
            Composer.CollectPicks(root, picks);
            Assert.Equal(new[] { "Bench01", "ModBench", "ModPaving", "ModRubber" }, picks.OrderBy(x => x, StringComparer.Ordinal));
        }
    }
}
