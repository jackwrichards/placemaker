// (shops3) The strip mall on the game runner's ordinary grid block (B0-0: 150 x 140 m between centre lines, West and South a
// 4-lane Medium Road, East and North a 2-lane Small Road) planned 46 zone cells and the game zoned 14; the big box 66 and 24
// (run show-6-builds-1016-140401-63cc). Two planner defects, both reproduced here on the block as the runner lays it out:
//
// 1. A lane's zone block crossing another lane's row. The row along the rung ends 8.5 m from the rail crossing it, so an end
//    plot's centre lay nearer the rail than the rung and LaneSides kept the rail's side zoning there. That 48 m block crosses the
//    row; between crossing blocks the game has no depth rule and the older build order wins (CellOverlapJobs.CheckPriority), so
//    the rails, built first, took the rung's cells 48 m in from each end and the depth rule cut the rest ("depth rule ... depths
//    left 0 / right 0" in the log). LaneSides now gives a plot to the road its front runs along.
// 2. Half the plots zoned two rows of four. EdgeZoningAddOn's boundary test read float noise on a slightly askew plot (world
//    coordinates of several hundred metres) as a crossing and cut the quad to one cell ("preview 8, 4, 8, 4" in the log).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class GridBlockZoningTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>
        /// Block B0-0 of the runner's grid (Core/Suite/SuiteLayout): its streets and their neighbours, with the streets' zone blocks
        /// captured (predicted as the game makes them), the gallery's catalog and the game's default layout scale.
        /// </summary>
        internal static SiteContext Site(out SuiteBlock block)
        {
            var layout = new SuiteLayout();
            block = layout.Blocks().First(b => b.I == 0 && b.J == 0);
            var site = new SiteContext { Assets = CommercialS32Tests.Catalog(), LayoutScale = 0.5f };
            var c = block.Center;
            long id = 900;
            foreach (var s in layout.Roads())
            {
                // The block's own streets and the next segment of each line (the junctions' continuing edges).
                if (math.distance(0.5f * (s.A + s.B), c) > 230f) continue;
                var a = new float3(s.A.x, 0, s.A.y); var d = new float3(s.B.x, 0, s.B.y);
                site.NearbyNets.Add(new NearbyNet
                {
                    ExternalId = id++, A = a, B = math.lerp(a, d, 1f / 3f), C = math.lerp(a, d, 2f / 3f), D = d,
                    Width = RoadRoles.Find(s.Prefab).Width, Category = NetCategory.Road, HasSidewalk = true,
                });
            }
            foreach (var g in ZoneLots.SiteGrids(site, null))
            {
                var cg = g; cg.Predicted = false; cg.Existing = false;
                site.ZoneGrids.Add(cg);
            }
            return site;
        }

        /// <summary>
        /// The block's land inside the kerbs. "kerbs": 8 corners, the kerb returns cut off (the outline the Block tool traces in the
        /// game; the 4-lane corner's wider return makes the North street the main frontage there, as in the run: the lot north, the
        /// row south backing onto the 4-lane road, the rails a little askew). "rect": the plain rectangle (the lot south).
        /// </summary>
        internal static float3[] Ring(SuiteBlock b, string kind)
        {
            float Half(string prefab) => RoadRoles.Find(prefab).Width * 0.5f;
            var q = b.Interior(Half);
            float x0 = q[0].x, z0 = q[0].y, x1 = q[2].x, z1 = q[2].y;
            var r = kind == "rect"
                ? new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) }
                : new[]
                {
                    new float2(x0 + 4, z0), new float2(x1 - 2, z0), new float2(x1, z0 + 2), new float2(x1, z1 - 2),
                    new float2(x1 - 2, z1), new float2(x0 + 2, z1), new float2(x0, z1 - 2), new float2(x0, z0 + 4),
                };
            return r.Select(p => new float3(p.x, 0, p.y)).ToArray();
        }

        internal static ComposedPlan Compose(string type, string preset, string ring, out SiteContext site)
        {
            site = Site(out var block);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 1);
            if (!string.IsNullOrEmpty(preset)) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            var outline = Ring(block, ring);
            ShopTestEntrances.Place(r, outline, site, footpath: true);
            return Composer.Compose(r, new AreaShape { Ring = outline, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        /// <summary>
        /// Zoned plot cells that another zone block of the build's own roads crosses (the blocks the game will give them: switched-off
        /// sides have none). The game settles such an overlap by build order (no depth rule between crossing blocks): the plot loses
        /// its cells or grows facing the wrong lane.
        /// </summary>
        internal static List<string> CrossedCells(ComposedPlan plan, SiteContext site)
        {
            var grids = ZoneGuard.PredictedGrids(plan, site);
            var bad = new List<string>();
            foreach (var c in plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned))
            {
                var p = 0.25f * (c.A + c.B + c.C + c.D);
                int own = -1;
                for (int gi = 0; gi < grids.Count && own < 0; gi++)
                    for (int x = 0; x < grids[gi].Size.x && own < 0; x++)
                        for (int y = 0; y < grids[gi].Size.y; y++)
                            if (math.distance(ZoneGuard.CellCentre(grids[gi], x, y), p) < 0.5f) { own = gi; break; }
                if (own < 0) continue;   // a cell of a city street's block (a street plot)
                var d0 = math.normalizesafe(grids[own].Direction);
                for (int gi = 0; gi < grids.Count; gi++)
                {
                    if (gi == own || math.abs(math.dot(d0, math.normalizesafe(grids[gi].Direction))) > 0.7f) continue;
                    bool inside = false;
                    for (int x = 0; x < grids[gi].Size.x && !inside; x++)
                        for (int y = 0; y < grids[gi].Size.y && !inside; y++)
                            if (Poly.Contains(ZoneGuard.CellSquare(grids[gi], x, y, -0.05f), p)) inside = true;
                    if (inside) { bad.Add($"cell at ({p.x:F0}, {p.y:F0}) crossed by the block at ({grids[gi].Position.x:F0}, {grids[gi].Position.y:F0})"); break; }
                }
            }
            return bad;
        }

        /// <summary>
        /// Not the mall district: on this block its middle street's plot lies inside both alleys' bands, whose sides serve the alleys'
        /// own rows (a layout conflict, not a side left on by mistake: 30 crossed cells, before and after the LaneSides fix).
        /// </summary>
        public static IEnumerable<object[]> Cases()
        {
            foreach (var ring in new[] { "kerbs", "rect" })
            {
                yield return new object[] { "stripMall", "classic", ring };
                yield return new object[] { "stripMall", "bigBox", ring };
                yield return new object[] { "mallPark", "", ring };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoOwnLaneBlockCrossesAPlot(string type, string preset, string ring)
        {
            var plan = Compose(type, preset, ring, out var site);
            Assert.Contains(plan.ZoneCells, c => c.State == ZoneCellState.Zoned);
            var bad = CrossedCells(plan, site);
            Assert.True(bad.Count == 0, $"{type}/{preset}/{ring}: {bad.Count} zoned cell(s) under a crossing block of our own lanes, e.g. {string.Join("; ", bad.Take(4))}");
        }

        /// <summary>(shops4) The strip mall's shop lane zones only the side its shops stand on (the lot's side is off).</summary>
        [Theory]
        [InlineData("classic", "kerbs")]
        [InlineData("bigBox", "kerbs")]
        [InlineData("classic", "rect")]
        [InlineData("bigBox", "rect")]
        public void StripMallLaneZonesOnlyItsShopSide(string preset, string ring)
        {
            var plan = Compose("stripMall", preset, ring, out _);
            var host = plan.Layers.First(l => l.GeneratorId == "stripMall");
            Assert.True(host.Facts["stripRows"] >= 1.0);   // (2026-10-28) the back row, and an L's or a U's side rows
            var lanes = host.Nets.Where(n => n.Role == NetRole.Alley && math.distance(n.A.xz, n.D.xz) > 30f).ToList();
            Assert.NotEmpty(lanes);
            foreach (var n in lanes)
            {
                var up = n.Upgrades ?? new string[0];
                Assert.True(up.Contains(LaneSides.LeftOff) != up.Contains(LaneSides.RightOff), "one side off: " + string.Join(",", up));
            }
        }

        /// <summary>Every store plot of the strip mall on the block is zoned its full depth (4 rows classic, 6 big box).</summary>
        [Theory]
        [InlineData("classic", "kerbs", 4)]
        [InlineData("bigBox", "kerbs", 6)]
        [InlineData("classic", "rect", 4)]
        [InlineData("bigBox", "rect", 6)]
        public void StripMallPlotsAreZonedFullDepth(string preset, string ring, int rows)
        {
            var plan = Compose("stripMall", preset, ring, out _);
            var plots = plan.Layers.SelectMany(l => l.Regions).Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
            Assert.NotEmpty(plots);
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).Select(c => 0.25f * (c.A + c.B + c.C + c.D)).ToList();
            var lanes = plan.Layers.SelectMany(l => l.Nets).Where(n => n.Role == NetRole.Alley).Select(n => (n.A.xz, n.D.xz)).ToList();
            int full = 0;
            foreach (var q in plots)
            {
                int n = zoned.Count(p => Poly.Contains(q, p));
                // (rf-shops3) The plot's own depth: a row's end plot by a cut corner of the outline may be planned a cell shallower.
                // Its front is the side along a lane, its depth the side next to it.
                int front = Enumerable.Range(0, q.Length).OrderBy(i => lanes.Min(l => math.sqrt(Placemaker.Core.Layers.Common.SegmentIndex.DistSq(0.5f * (q[i] + q[(i + 1) % q.Length]), l.Item1, l.Item2)))).First();
                int depth = (int)math.round(math.distance(q[(front + 1) % q.Length], q[(front + 2) % q.Length]) / 8f);
                Assert.InRange(depth, 2, rows);
                if (depth == rows) full++;
                // At least two columns, each the plot's full depth.
                Assert.True(n >= 2 * depth && n % depth == 0, $"{preset}/{ring}: plot at {Poly.Centroid(q)} has {n} zoned cells (whole columns of {depth} rows expected)");
            }
            Assert.True(full * 2 >= plots.Count, $"{preset}/{ring}: only {full} of {plots.Count} plots are {rows} rows deep");
        }

        /// <summary>
        /// EdgeZoningAddOn zones a plot its full depth wherever it lies: a 25 x 32 m plot a little askew, far from the origin (float
        /// noise along its collinear sides read as a boundary crossing cut half its quads to one cell).
        /// </summary>
        [Theory]
        [InlineData(0.9f, -600f, -460f)]
        [InlineData(-0.7f, -600f, -460f)]
        [InlineData(0.9f, 1800f, 2400f)]
        [InlineData(3f, -4000f, 900f)]
        [InlineData(0f, -600f, -460f)]
        public void EdgeZoningZonesAnAskewPlotFullDepth(float degrees, float x, float z)
        {
            float a = math.radians(degrees);
            float2 u = new float2(-math.cos(a), -math.sin(a)), v = new float2(math.sin(a), -math.cos(a));   // along the lane, inward
            var o = new float2(x, z);
            int fails = 0;
            for (int k = 0; k < 12; k++)
            {
                var p0 = o + u * (24.6f * k);
                var ring = new[] { p0, p0 + u * 24.6f, p0 + u * 24.6f + v * 32f, p0 + v * 32f };
                var reserved = new ReservedSpace();
                reserved.AddCorridor(p0 - v * 4f - u * 10f, p0 - v * 4f + u * 40f, 4f, nameof(NetRole.Alley));
                var bag = new SettingsBag();
                bag.Values[EdgeZoningAddOn.CutoutSetting] = true;
                var input = new AreaInput
                {
                    Shape = new AreaShape { Ring = ring.Select(p => new float3(p.x, 0, p.y)).ToArray() },
                    Settings = bag, Reserved = reserved, RegionTags = new[] { StreetFrontage.ShopsTag },
                };
                var plan = new EdgeZoningAddOn().Plan(input, CancellationToken.None);
                Assert.NotEmpty(plan.Zoning);
                foreach (var q in plan.Zoning)
                    if (math.distance(q.B.xz, q.C.xz) < 32f) fails++;
            }
            Assert.True(fails == 0, $"{fails} zoning quad(s) cut short of the plot's 32 m depth");
        }
    }
}
