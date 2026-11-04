// s23 decal art pass: see-through tiers, edge classes, deliberate placement (docs/DECALS.md "Art pass").

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Decals;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class DecalArtTests
    {
        [Theory]
        [InlineData(0.1f, 10)]
        [InlineData(0.12f, 10)]
        [InlineData(0.15f, 15)]
        [InlineData(0.25f, 25)]
        [InlineData(0.28f, 25)]
        [InlineData(0.45f, 50)]
        [InlineData(0.8f, 100)]
        [InlineData(1f, 100)]
        public void OpacityQuantisesToTiers(float opacity, int tier) => Assert.Equal(tier, DecalLook.Tier(opacity));

        [Fact]
        public void MostDecalsAreFaintSandStaysStrongMarkingsFull()
        {
            Assert.Equal(0.25f, DecalPools.Opacity(DecalPools.Sand));   // sand dust a touch stronger, still subtle (s28)
            foreach (var c in new[] { DecalPools.Stain, DecalPools.Tyre, AssetCategories.DecalLeaves, AssetCategories.DecalCracks, DecalPools.Moss, DecalPools.Patch })
                Assert.True(DecalPools.Opacity(c) <= 0.15f, c + " " + DecalPools.Opacity(c));
            Assert.Equal(1f, DecalLook.OpacityOf(new PlannedObject { Role = ObjectRole.Decal, Category = AssetCategories.DecalFieldLine }));
            Assert.Equal(1f, DecalLook.OpacityOf(new PlannedObject { Role = ObjectRole.Bench }));
            int faint = DecalPools.All.Count(c => DecalPools.Opacity(c) <= 0.35f);
            Assert.True(faint * 2 > DecalPools.All.Count(), "most pools are ~80 % transparent");
            Assert.All(DecalPools.All, c => Assert.True(DecalPools.Opacity(c) <= 0.25f, c));
        }

        [Theory]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 05 B M Decal", DecalEdge.Soft)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Dust 02 Brown Decal", DecalEdge.Soft)]
        [InlineData("Ground Texture Decal Pack Ground Dirt03 Decal", DecalEdge.Soft)]
        [InlineData("Fallen leaves decals Leaf Dead Leaves2 Yellow light Decal", DecalEdge.Soft)]
        [InlineData("G87 Trash Decals Dirty Pack Leaf G87 Leaf 03 Decal", DecalEdge.Hard)]
        [InlineData("G87 Dirty Roads Stains G87 Dirt Road Transition 04 Decal", DecalEdge.Hard)]
        [InlineData("RealVision Decals Ground Texture118 - Dirt Patch Decal", DecalEdge.Hard)]
        [InlineData("Cracks and Damage Decal Pack RoadAssets Crack - Small 2 Decal", DecalEdge.Shaped)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 42 Track Short Decal", DecalEdge.Shaped)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 46 Wide Decal", DecalEdge.Shaped)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Debris 12 Glass Bottle Green Decal", DecalEdge.Item)]
        public void EdgeClasses(string name, DecalEdge edge) => Assert.Equal(edge, DecalCatalog.Describe(name).Edge);

        [Fact]
        public void ScatterPoolsTakeOnlySoftEdgesPlacedPoolsTakeShapes()
        {
            Assert.False(DecalPools.Eligible(DecalPools.Mud, "G87 Dirty Roads Stains G87 Dirt Road Transition 04 Decal"));
            Assert.True(DecalPools.Eligible(DecalPools.Mud, "G87 Trash Decals Dirty Pack Trash G87 Dust 02 Brown Decal"));
            Assert.False(DecalPools.Eligible(DecalPools.Stain, "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 46 Wide Decal"));
            Assert.True(DecalPools.Eligible(DecalPools.Stain, "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 05 B M Decal"));
            Assert.True(DecalPools.Eligible(DecalPools.Tyre, "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 42 Track Short Decal"));
            var rule = new DecalDressingRules();
            const AssetTraits decal = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.DecalMesh;
            Assert.Equal(false, rule.AutoEligible(AssetCategories.DecalLeaves, "g87 trash decals dirty pack leaf g87 leaf 03 decal", decal));
            Assert.Null(rule.AutoEligible(AssetCategories.DecalLeaves, "fallen leaves decals leaf dead leaves8 brown light decal", decal));
            // A decal that does not project onto terrain is never an automatic ground decal.
            Assert.Equal(false, rule.AutoEligible(AssetCategories.DecalLeaves, "fallen leaves decals leaf dead leaves8 brown light decal", decal | AssetTraits.WallOrHanging));
        }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Park(double amount, uint seed = 7)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), seed);
            Editor.ApplyDefaults(r);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            r.Root.Settings.Values[DressingControls.AmountId] = amount;
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            var ring = TestKit.Rect(120, 100);
            TestKit.Entered(r, ring, new SiteContext());
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, new SiteContext(), CancellationToken.None);
        }

        [Fact]
        public void LawnIsNotScatteredAtRandom()
        {
            var p = Park(300);
            var trees = p.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush).Select(o => o.Position.xz).ToList();
            var litter = p.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Decal && o.Category == AssetCategories.DecalLitter).ToList();
            Assert.True(litter.Count < 12, "no random litter on a lawn park: " + litter.Count);
            var leaves = p.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Decal && o.Category == AssetCategories.DecalLeaves).ToList();
            Assert.All(leaves, o => Assert.True(trees.Any(t => math.distance(t, o.Position.xz) < 10f), "leaves away from any tree at " + o.Position.xz));
        }
    }
}
