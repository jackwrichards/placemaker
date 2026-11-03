// (shops2) The kept Shops & squares types on a big block (four times the test block, streets all round): a strip mall is rows
// of stores round lots (not a park), Downtown mixes parking blocks in, the big market fills the square.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Commercial;
using Placemaker.Core.Tool;
using Xunit;

namespace Placemaker.Tests
{
    public class ShopsKeepersTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Big(string type, string preset, float w = 300f, float d = 280f)
        {
            var ring = TestKit.Rect(w, d);
            var site = ZoningCutoutTests.Streets(ring);
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            // (rf-shops3) A car entrance mid main street and a footpath beside it (what a player places).
            ShopTestEntrances.Place(r, ring, site, footpath: true);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        // (rf-shops3) The centre: a ring drive round the lot, rows outside it facing in (rungs with rows on deep sites). Its planted
        // frontage along the main street (12 m), the spine with its plaza, the planted strips beside the ring and the lots' planted
        // edges along the rows (Mall.PlotClear) take a bigger share than the old rows-round-lots layout (0.8): measured 0.79, 0.76
        // and, on the small block, 0.69.
        [Theory]
        [InlineData(300f, 280f, 1, 0.75f)]   // (shops4) one row for now: rows across a big lot come back as a variation
        [InlineData(360f, 110f, 1, 0.75f)]
        [InlineData(150f, 140f, 1, 0.65f)]
        public void StripMall_IsRowsOfStoresRoundLots_NotAPark(float w, float d, int minRows, float minShare)
        {
            var plan = Big("stripMall", "classic", w, d);
            var host = plan.Layers.First();
            Assert.Equal(1.0, host.Facts.GetValueOrDefault(CommercialLayer.LotFact));
            Assert.True(host.Facts.TryGetValue(StripMallLayer.RowsFact, out var rows) && rows >= minRows, "rows " + rows);
            var share = CommercialIrregularSiteTests.Measure(plan, Poly.To2D(TestKit.Rect(w, d)));
            Assert.True(share.Commercial >= minShare, $"commercial share {share.Commercial:0.00}");
            Assert.True(share.Lots >= share.Plots * 0.8f, $"lots {share.Lots:0} plots {share.Plots:0}");
            Assert.DoesNotContain(plan.Issues.Concat(host.Issues), i => i.Key == "Placemaker.Issue.CutoutNotZonable");
        }

        [Fact]
        public void Downtown_MixesParkingBlocksIn()
        {
            var host = Big("mallDistrict", "dense").Layers.First();
            // (rf-shops3) The lane grid inside the ring: on this block three rails and three rung levels, and "some" parking courts
            // take every third block of the grid ((rail + 2 x level) % 3 == 1): one court (the old composition's grid held two).
            Assert.Equal(1.0, host.Facts.GetValueOrDefault(MallDistrictLayer.RingFact));
            Assert.True(host.Facts.TryGetValue("parkingBlocks", out var n) && n >= 1, "parking blocks " + n);
            Assert.True(host.Facts.GetValueOrDefault("lots") >= n, "lots " + host.Facts.GetValueOrDefault("lots"));
        }

        [Fact]
        public void BigMarket_FillsTheSquare()
        {
            var host = Big("marketSquare", "bigMarket").Layers.First();
            int stalls = host.Objects.Count(o => o.Category == AssetCategories.MarketStall);
            Assert.True(stalls >= 400, "stalls " + stalls);
            // rf-shops3 (the market quarter): food courts only where side streets meet the main streets, at least 70 m apart (shops2
            // had one at every other crossing: 4+); the blocks between get planting islands and café groves instead.
            Assert.True(host.Facts.TryGetValue("foodCourts", out var courts) && courts >= 2, "food courts " + courts);
            Assert.True(host.Facts["plantingIslands"] + host.Facts["cafeGroves"] >= 6, "islands and groves");
        }
    }
}
