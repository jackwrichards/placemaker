// rf-squares (docs/refactor/squares.md): the Squares & events types on a city block.
// In the game runner (clean profile, show-31-builds-1016-062611-025f) the squares had empty discs where the fountain or
// statue should be (none installed), and their markets, fairs and bowls were small clusters in big empty sites. These
// pin the fixes: a planted centre when no centrepiece is installed, counts that scale with the site.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.Amphitheatre;
using Placemaker.Core.Layers.Types.Events;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class SquaresTests
    {
        private static float3[] Square(float side) => new[] { new float3(0, 0, 0), new float3(side, 0, 0), new float3(side, 0, side), new float3(0, 0, side) };

        /// <summary>A type as the player gets it: defaults + default features, a preset, suggested entrances.</summary>
        private static ComposedPlan Compose(string type, float3[] ring, string preset = null)
        {
            var gen = GeneratorRegistry.Get(type);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, 1);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            if (preset != null) editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            TestKit.Entered(r, ring, null);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, new SiteContext(), CancellationToken.None);
        }

        /// <summary>The clean game's catalog: trees, bushes, benches, stalls… but no fountain or statue.</summary>
        private static IDisposable NoCentrepieces() => AssetAvailability.Scope(new Dictionary<string, int>
        {
            { AssetCategories.Centerpiece, 0 }, { AssetCategories.CenterpieceFountain, 0 }, { AssetCategories.CenterpieceStatue, 0 },
            { AssetCategories.Tree, 16 }, { AssetCategories.Bush, 11 }, { AssetCategories.Bench, 41 },
        });

        private static AreaPlan Layer(ComposedPlan plan, string type) => plan.Layers.First(l => l.GeneratorId == type);

        [Theory]
        [InlineData("plaza", "fountain")]
        [InlineData("plaza", "civic")]
        [InlineData("plaza", "piazza")]
        [InlineData("plaza", null)]
        [InlineData("marketSquare", "weekly")]
        [InlineData("marketSquare", "farmers")]
        public void WithoutInstalledCentrepiecesTheSquarePlantsItsCentre(string type, string preset)
        {
            ComposedPlan plan;
            using (NoCentrepieces()) plan = Compose(type, Square(110), preset);
            Assert.False(plan.HasErrors);
            var all = plan.Layers.SelectMany(l => l.Objects).ToList();
            Assert.DoesNotContain(all, o => o.Role == ObjectRole.Centerpiece);
            var layer = Layer(plan, type);
            Assert.True(layer.Facts.ContainsKey("specimenCentre"), type + "/" + preset + ": no planted centre");
            var c = new float2(55, 55);
            Assert.Contains(layer.Objects, o => o.Role == ObjectRole.Tree && math.distance(o.Position.xz, c) < 12f);
        }

        [Fact]
        public void WithInstalledCentrepiecesTheFountainSquareKeepsItsFountain()
        {
            var plan = Compose("plaza", Square(110), "fountain");
            Assert.False(Layer(plan, "plaza").Facts.ContainsKey("specimenCentre"));
        }

        /// <summary>A city block as the bucket tool outlines it in the game: a square with 2 m chamfers at the junctions (shorter than ~3.5 m folds a 5.8 m per-vertex inset).</summary>
        private static float3[] Block(float side, float chamfer = 2f) => new[]
        {
            new float3(chamfer, 0, 0), new float3(side - chamfer, 0, 0), new float3(side, 0, chamfer), new float3(side, 0, side - chamfer),
            new float3(side - chamfer, 0, side), new float3(chamfer, 0, side), new float3(0, 0, side - chamfer), new float3(0, 0, chamfer),
        };

        [Theory]
        [InlineData("plaza", "fountain", "plazaTrees")]
        [InlineData("plaza", "green", "plazaTrees")]
        [InlineData("plaza", null, "plazaTrees")]
        [InlineData("marketSquare", "weekly", "ringTrees")]
        public void TheEdgeTreesSurviveABlocksChamferedCorners(string type, string preset, string fact)
        {
            // Game runner (show-7-builds-1016-072905-1abe): no allée on any plaza, no tree ring on any market square: the
            // per-vertex inset folded over on the 8-corner outline of a bucket-filled block.
            var layer = Layer(Compose(type, Block(126f), preset), type);
            Assert.True(layer.Facts.TryGetValue(fact, out var n) && Convert.ToInt32(n) >= 20, $"{type}/{preset}: {fact} = {n}");
        }

        [Fact]
        public void TheGreenSquareHasLawnsWhereverTheEntrancesAre()
        {
            // The suggested access points of a city block sit anywhere on the outline: every quarter parterre is crossed
            // by a walk, so the lawns fill the free ground between the walks instead.
            var plan = Layer(Compose("plaza", Block(126f), "green"), "plaza");
            Assert.True(plan.Facts.TryGetValue("lawns", out var n) && Convert.ToInt32(n) >= 2, "lawns " + n);
            float grass = plan.Surfaces.Where(s => s.Role == SurfaceRole.Grass).Sum(s => Placemaker.Core.Geometry.Poly.Area(Placemaker.Core.Geometry.Poly.To2D(s.Ring)));
            Assert.True(grass > 0.2f * 126f * 126f, "grass " + grass);
        }

        [Fact]
        public void MarketStallsScaleWithTheSquare()
        {
            Assert.Equal(16, MarketSquareLayer.ScaledStalls(16, 2500f));
            Assert.Equal(64, MarketSquareLayer.ScaledStalls(16, 16000f));
            Assert.Equal(64, MarketSquareLayer.ScaledStalls(16, 40000f));   // at most four times
            Assert.Equal(0, MarketSquareLayer.ScaledStalls(0, 16000f));
            int Stalls(float side) => (int)Layer(Compose("marketSquare", Square(side), "weekly"), "marketSquare").Objects.Count(o => o.Category == AssetCategories.MarketStall);
            int small = Stalls(63f), block = Stalls(126f);
            Assert.True(block >= small * 3, $"0.4 ha {small} stalls, 1.6 ha {block}");
        }

        [Fact]
        public void FairStallsAndBowlRowsScaleWithTheSite()
        {
            Assert.Equal(30, FairgroundLayer.ScaledStalls(30, 4000f));
            Assert.Equal(75, FairgroundLayer.ScaledStalls(30, 15000f));
            Assert.Equal(10, AmphitheatreLayer.ScaledRows(10, 5000f));
            Assert.Equal(20, AmphitheatreLayer.ScaledRows(10, 16000f));
            var plan = Layer(Compose("amphitheatre", Square(126), "classic"), "amphitheatre");
            Assert.True(Convert.ToDouble(plan.Facts["rows"]) > 10, "rows " + plan.Facts["rows"]);
            Assert.True(Convert.ToDouble(plan.Facts["bowlHeight"]) <= AmphitheatreLayer.MaxBowlHeight + 0.01, "bowl " + plan.Facts["bowlHeight"]);
        }

        [Fact]
        public void TheShelfShowsOnlyTheKeptSquaresStyles()
        {
            var dir = System.IO.Path.Combine(TestKit.RepoRoot(), "Recipes", Shelf.FolderName);
            var shelf = Shelf.Parse(System.IO.Directory.GetFiles(dir, "*.json").OrderBy(f => f)
                .Select(f => new KeyValuePair<string, string>(System.IO.Path.GetFileName(f), System.IO.File.ReadAllText(f))));
            Assert.Empty(shelf.Warnings);
            bool Hidden(string type, string group, string style) => shelf.IsStyleShelved(type, group, style);
            Assert.True(Hidden("plaza", "squares", "amphitheatre"));
            Assert.True(Hidden("plaza", "squares", "market"));
            Assert.True(Hidden("amphitheatre", "events", "cinema"));
            Assert.True(Hidden("fairground", "events", "funfair"));
            // (2026-10-28) The designed plazas replace the classic compositions (PlazaDesignTests).
            Assert.True(Hidden("plaza", "squares", "fountain"));
            foreach (var d in PlazaDesignTests.Designs) Assert.False(Hidden("plaza", "squares", d), d);
            Assert.True(Hidden("marketSquare", "squares", "weekly"));
            Assert.False(Hidden("marketSquare", "squares", "bigMarket"));
            Assert.True(shelf.IsTypeShelved("fairground", "events"));
            // (2026-10-28) Plazas is its own category again, the plaza type shown in it
            Assert.False(shelf.IsTypeShelved("plaza", "squares"));
            Assert.Equal("plazas", shelf.CategoryOf("plaza", "squares"));
            Assert.False(Hidden("amphitheatre", "events", "classic"));
            Assert.False(shelf.IsTypeShelved("amphitheatre", "events"));
            Assert.Equal("parks", shelf.CategoryOf("amphitheatre", "events"));
        }
    }
}
