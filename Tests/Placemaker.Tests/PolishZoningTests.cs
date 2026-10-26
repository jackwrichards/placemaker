// The Zoning category's Zoning tool (Core/Layers/Types/Zoning/PolishLookLayer): the blocks in the area zoned as lots that
// start at their road (a zone behind another zone can never grow anything), each zone's lots in its width and depth ranges,
// the zones split by their shares, and the road rule giving the blocks of wide roads their own zone. On the game runner's
// grid block B0-0 (GridBlockZoningTests.Site: West and South a 4-lane Medium Road, East and North a 2-lane Small Road).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class PolishZoningTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Plan(out SiteContext site, params (string Id, object Value)[] values)
        {
            site = GridBlockZoningTests.Site(out var block);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("polishGarden"), 1);
            foreach (var (id, v) in values) Editor.SetValue(r, RecipePaths.Root, id, v, false);   // false: the value it already had
            var ring = GridBlockZoningTests.Ring(block, "rect");
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static ComposedPlan Plan(params (string Id, object Value)[] values) => Plan(out _, values);

        private static PlannedZoning[] Cells(ComposedPlan plan) => plan.Layers.SelectMany(l => l.Zoning).ToArray();

        private static float2 Mid(PlannedZoning c) => ((c.A + c.C) * 0.5f).xz;

        /// <summary>The zone at each cell of each block (null: not zoned), by matching the planned cells' middles.</summary>
        private static List<(ZoneGrid Grid, string[,] Zone)> ByBlock(ComposedPlan plan, SiteContext site)
        {
            var at = new Dictionary<(int, int), string>();
            foreach (var c in Cells(plan)) at[((int)math.round(Mid(c).x * 2f), (int)math.round(Mid(c).y * 2f))] = c.ZoneId;
            var list = new List<(ZoneGrid, string[,])>();
            foreach (var g in site.ZoneGrids)
            {
                var z = new string[g.Size.x, g.Size.y];
                for (int x = 0; x < g.Size.x; x++)
                    for (int y = 0; y < g.Size.y; y++)
                    {
                        var p = ZoneGuard.CellCentre(g, x, y);
                        at.TryGetValue(((int)math.round(p.x * 2f), (int)math.round(p.y * 2f)), out z[x, y]);
                    }
                list.Add((g, z));
            }
            return list;
        }

        [Fact]
        public void AnOldOnlyEditStyleZonesTheEmptyLandToo()
        {
            // "Only edit existing" is gone (the user, 26 Oct): an old style's zoneArea false zones as true does
            Assert.Equal(Cells(Plan(("zoneArea", true))).Length, Cells(Plan(("zoneArea", false))).Length);
            Assert.NotEmpty(Cells(Plan(("zoneArea", false))));
        }

        [Fact]
        public void OneZoneSixDeepZonesTheBlocks()
        {
            var cells = Cells(Plan(("zoneArea", true), ("depthMinA", 6.0), ("depthMaxA", 6.0)));
            Assert.True(cells.Length > 100, $"{cells.Length} cells");
            Assert.All(cells, c => Assert.Equal(ZoneKinds.PrefabId(ZoneKinds.ResidentialLow), c.ZoneId));
            // one zone per spot, never two on one cell
            Assert.Equal(cells.Length, cells.Select(c => math.round(Mid(c) * 2f)).Distinct().Count());
        }

        [Theory]
        [InlineData("mixed")]
        [InlineData("clumps")]
        [InlineData("even")]
        public void EveryZonedCellReachesItsRoadThroughItsOwnLot(string pattern)
        {
            var plan = Plan(out var site, ("zoneArea", true), ("shareA", 50.0), ("shareB", 50.0), ("zonePattern", pattern));
            Assert.NotEmpty(Cells(plan));
            int checkedCells = 0;
            foreach (var (g, z) in ByBlock(plan, site))
                for (int x = 0; x < g.Size.x; x++)
                    for (int y = 1; y < g.Size.y; y++)
                    {
                        if (z[x, y] == null) continue;
                        // the cell in front of it (towards the road) belongs to the same lot: same zone
                        Assert.True(z[x, y - 1] == z[x, y], $"{pattern}: a {z[x, y]} cell at row {y} behind {(z[x, y - 1] ?? "nothing")}");
                        checkedCells++;
                    }
            Assert.True(checkedCells > 20, $"{checkedCells} cells behind the first row");
        }

        [Fact]
        public void LotsKeepToTheirDepth()
        {
            var plan = Plan(out var site, ("zoneArea", true), ("depthMinA", 3.0), ("depthMaxA", 3.0));
            int full = 0;
            foreach (var (g, z) in ByBlock(plan, site))
                for (int x = 0; x < g.Size.x; x++)
                {
                    int depth = 0;
                    while (depth < g.Size.y && z[x, depth] != null) depth++;
                    Assert.True(depth <= 3, $"a column zoned {depth} deep");
                    for (int y = depth; y < g.Size.y; y++) Assert.Null(z[x, y]);
                    if (depth == 3) full++;
                }
            Assert.True(full > 10, $"{full} columns zoned 3 deep");
        }

        [Theory]
        [InlineData("mixed")]
        [InlineData("clumps")]
        [InlineData("even")]
        public void SharesSplitTheCells(string pattern)
        {
            var cells = Cells(Plan(("zoneArea", true), ("shareA", 50.0), ("shareB", 50.0), ("zoneB", ZoneKinds.CommercialLow), ("zonePattern", pattern), ("zoneGrain", 1.0)));
            int homes = cells.Count(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.ResidentialLow));
            int shops = cells.Count(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.CommercialLow));
            Assert.Equal(cells.Length, homes + shops);
            float share = homes / (float)cells.Length;
            Assert.True(share > 0.15f && share < 0.85f, $"{pattern}: {homes} homes, {shops} shops");
        }

        [Fact]
        public void EachZoneGoesOnlyOnTheRoadsItsWidthsAllow()
        {
            float medium = RoadRoles.Find(SuiteLayout.MediumRoad).Width, small = RoadRoles.Find(SuiteLayout.SmallRoad).Width;
            Assert.True(medium > small);
            double cut = (medium + small) * 0.5;
            // homes on the small streets only, offices on the big roads only
            var plan = Plan(out var site, ("zoneArea", true), ("shareA", 50.0), ("roadMaxA", cut), ("shareB", 50.0), ("zoneB", ZoneKinds.OfficeLow), ("roadMinB", cut));
            var cells = Cells(plan);
            var office = cells.Where(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.OfficeLow)).ToArray();
            var homes = cells.Where(c => c.ZoneId == ZoneKinds.PrefabId(ZoneKinds.ResidentialLow)).ToArray();
            Assert.NotEmpty(office);
            Assert.NotEmpty(homes);
            // every block's zoned cells are allowed on its road (the road nearest its front middle, as the planner reads it)
            float RoadAt(float2 p) => site.NearbyNets.Where(n => n.Category == NetCategory.Road)
                .OrderBy(n => Enumerable.Range(0, 17).Select(k => { float t = k / 16f, u = 1f - t; var q = u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D; return math.distance(q.xz, p); }).Min())
                .First().Width;
            foreach (var (g, zz) in ByBlock(plan, site))
            {
                float road = RoadAt(ZoneGuard.CellCentre(g, g.Size.x / 2, 0));
                foreach (var id in zz)
                {
                    if (id == ZoneKinds.PrefabId(ZoneKinds.OfficeLow)) Assert.True(road >= cut, "an office block on a " + road + " m road");
                    if (id == ZoneKinds.PrefabId(ZoneKinds.ResidentialLow)) Assert.True(road <= cut, "a home block on a " + road + " m road");
                }
            }
        }

        [Theory]
        [InlineData("mixed")]
        [InlineData("clumps")]
        [InlineData("even")]
        public void NoLotIsSmallerThanItsZonesSmallest(string pattern)
        {
            // shops at least 5 wide and 4 deep among homes: never a strip of shops narrower or shallower than that
            var shop = ZoneKinds.PrefabId(ZoneKinds.CommercialLow);
            var plan = Plan(out var site, ("zoneArea", true), ("zonePattern", pattern), ("shareA", 50.0), ("shareB", 50.0), ("zoneB", ZoneKinds.CommercialLow),
                ("widthMinB", 5.0), ("widthMaxB", 6.0), ("depthMinB", 4.0), ("depthMaxB", 4.0));
            int shops = 0;
            foreach (var (g, zz) in ByBlock(plan, site))
            {
                int x = 0;
                while (x < g.Size.x)
                {
                    if (zz[x, 0] != shop) { x++; continue; }
                    int start = x;
                    while (x < g.Size.x && zz[x, 0] == shop)
                    {
                        int depth = 0;
                        while (depth < g.Size.y && zz[x, depth] == shop) depth++;
                        Assert.True(depth >= 4, $"{pattern}: a shop column {depth} deep");
                        x++;
                    }
                    Assert.True(x - start >= 5, $"{pattern}: a strip of shops {x - start} wide");
                    shops++;
                }
            }
            Assert.True(shops > 0, "some shops");
        }

        [Fact]
        public void ShareOfTheLandLeavesTheRestEmpty()
        {
            int full = Cells(Plan(("zoneArea", true), ("shareA", 100.0))).Length;
            int part = Cells(Plan(("zoneArea", true), ("shareA", 40.0))).Length;
            float ratio = part / (float)full;
            Assert.True(ratio > 0.15f && ratio < 0.65f, $"40 % of the land zoned {part} of {full} cells");
        }

        [Fact]
        public void ShapeOffCountsNoBuildingsToPolish()
        {
            var plan = Plan(("shapeLots", false));
            Assert.DoesNotContain(plan.Layers.SelectMany(l => l.Stats), st => st.Key == "Placemaker.Polish.Stat.Buildings");
            Assert.NotEmpty(Cells(plan));
        }
    }
}
