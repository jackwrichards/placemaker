// The Zoning tool's "Level the lots" (Core/Layers/Types/Zoning/LotLevel): on sloped land every zoned lot is a level pad at
// the height it meets its road, banks blend into the land around, the earthwork limit holds (steeper lots shallower or
// empty), flat land and "off" change nothing. On the game runner's grid block B0-0 (GridBlockZoningTests.Site) tilted.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class PolishLevelTests
    {
        private readonly ITestOutputHelper _out;
        public PolishLevelTests(ITestOutputHelper o) { _out = o; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Plan(float grade, out SiteContext site, params (string Id, object Value)[] values)
        {
            site = GridBlockZoningTests.Site(out var block);
            // rising to the north (and a little to the east)
            site.HeightAt = p => 100f + grade * p.y + 0.2f * grade * p.x;
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("polishGarden"), 1);
            Editor.SetValue(r, RecipePaths.Root, "zoneArea", true, false);
            foreach (var (id, v) in values) Editor.SetValue(r, RecipePaths.Root, id, v, false);
            var ring = GridBlockZoningTests.Ring(block, "rect");
            var h = site.HeightAt;
            ring = ring.Select(p => new float3(p.x, h(p.xz), p.z)).ToArray();
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static PlannedZoning[] Cells(ComposedPlan plan) => plan.Layers.SelectMany(l => l.Zoning).ToArray();
        private static List<PlannedTerraform> Terraform(ComposedPlan plan) => plan.Layers.SelectMany(l => l.Terraform).ToList();

        /// <summary>The ground the build leaves (the build's own raster).</summary>
        private static Func<float2, float> Built(ComposedPlan plan, SiteContext site)
        {
            var items = new List<(float2[] ring, float target, int layer)>();
            var vh = new List<float[]>();
            foreach (var t in Terraform(plan))
            {
                items.Add((t.Ring.Select(v => v.xz).ToArray(), t.TargetHeight, 0));
                vh.Add(TerraformSurface.VertexHeights(t));
            }
            if (items.Count == 0) return site.HeightAt;
            var raster = TerraformRaster.Build(items, 1.75f, 4f, 1 << 22, vh, site.HeightAt);
            var original = site.HeightAt;
            return p => raster.TrySample(p, original, out float hh) ? hh : original(p);
        }

        private static double Fact(ComposedPlan plan, string key)
        {
            foreach (var l in plan.Layers) if (l.Facts.TryGetValue(key, out double v)) return v;
            return -1;
        }

        [Fact]
        public void FlatLandAndOffChangeNothing()
        {
            Assert.NotEmpty(Cells(Plan(0f, out _)));
            Assert.Empty(Terraform(Plan(0f, out _)));
            var off = Plan(0.08f, out _, ("levelLots", false));
            Assert.NotEmpty(Cells(off));
            Assert.Empty(Terraform(off));
        }

        [Fact]
        public void LotsOnASlopeLieLevelAcross()
        {
            var plan = Plan(0.08f, out var site);
            var cells = Cells(plan);
            Assert.NotEmpty(cells);
            Assert.NotEmpty(Terraform(plan));
            var built = Built(plan, site);
            int pts = 0, tilted = 0;
            foreach (var c in cells)
            {
                var q = new[] { c.A.xz, c.B.xz, c.C.xz, c.D.xz };
                var m = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
                // a lot is level across (towards its road) and follows its road along: one axis of every cell is level
                float2 u = math.normalizesafe(q[1] - q[0]), v = math.normalizesafe(q[3] - q[0]);
                float su = math.abs(built(m + u * 2.5f) - built(m - u * 2.5f)) / 5f, sv = math.abs(built(m + v * 2.5f) - built(m - v * 2.5f)) / 5f;
                pts++;
                if (math.min(su, sv) > 0.04f) tilted++;
            }
            _out.WriteLine($"{cells.Length} cells, {tilted} tilted; levelled {Fact(plan, "polishLevelLots")}, cut {Fact(plan, "polishLevelCut"):0} fill {Fact(plan, "polishLevelFill"):0} m³, max {Fact(plan, "polishLevelMaxCut"):0.0}/{Fact(plan, "polishLevelMaxFill"):0.0} m, paints {Fact(plan, "polishLevelPaints")}, walls {Fact(plan, "polishLevelWalls")} ({Fact(plan, "polishLevelWallLength"):0} m), too steep {Fact(plan, "polishLevelTooSteep")}, lots {Fact(plan, "polishLevelLotsAll")} lost {Fact(plan, "polishLevelLotsLost")} shorter {Fact(plan, "polishLevelLotsShorter")}");
            Assert.True(tilted <= pts * 0.05, $"{tilted} of {pts} zoned cells not level across");
            Assert.True(Fact(plan, "polishLevelMaxCut") <= 4.05 && Fact(plan, "polishLevelMaxFill") <= 4.05);
            // banks around the pads no steeper than the setting (+ the raster's rounding), no curtains
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            foreach (var t in Terraform(plan)) foreach (var p in t.Ring) { mn = math.min(mn, p.xz); mx = math.max(mx, p.xz); }
            var cur = TerrainMetrics.Curtains(built, site.HeightAt, mn - 6f, mx + 6f);
            _out.WriteLine(cur.ToString());
            Assert.Equal(0, cur.Count);
        }

        [Fact]
        public void SteepLotsAreShallowerOrEmpty()
        {
            var gentle = Cells(Plan(0.05f, out _, ("levelEarth", 2.0))).Length;
            var steep = Plan(0.25f, out _, ("levelEarth", 2.0));
            _out.WriteLine($"gentle {gentle} cells, steep {Cells(steep).Length} cells, too steep {Fact(steep, "polishLevelTooSteep")}");
            Assert.True(Cells(steep).Length < gentle);
            Assert.True(Fact(steep, "polishLevelMaxCut") <= 2.05 && Fact(steep, "polishLevelMaxFill") <= 2.05);
        }

        /// <summary>A ring road (16 m) around a 220 x 130 m piece of land rising 10 % to the north, zoned with the Suburb's leafy style.</summary>
        private static ComposedPlan RingSlope(out SiteContext site, params (string Id, object Value)[] values)
        {
            site = new SiteContext { HeightAt = p => 100f + 0.10f * p.y, Assets = CommercialS32Tests.Catalog() };
            var corners = new[] { new float2(-120, -75), new float2(120, -75), new float2(120, 75), new float2(-120, 75) };
            for (int i = 0; i < 4; i++)
            {
                var a2 = corners[i]; var d2 = corners[(i + 1) % 4];
                var a = new float3(a2.x, site.HeightAt(a2), a2.y); var d = new float3(d2.x, site.HeightAt(d2), d2.y);
                site.NearbyNets.Add(new NearbyNet { ExternalId = i + 1, A = a, B = math.lerp(a, d, 1f / 3f), C = math.lerp(a, d, 2f / 3f), D = d, Width = 16f, Category = NetCategory.Road, HasSidewalk = true });
            }
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var cg = g; cg.Predicted = false; cg.Existing = false; site.ZoneGrids.Add(cg); }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("zoningSuburb"), 1);
            Editor.ApplyPreset(r, RecipePaths.Root, "suburbLeafy");
            foreach (var (id, v) in values) Editor.SetValue(r, RecipePaths.Root, id, v, false);
            var h = site.HeightAt;
            var ring = new[] { new float3(-110, 0, -65), new float3(110, 0, -65), new float3(110, 0, 65), new float3(-110, 0, 65) }.Select(p => new float3(p.x, h(p.xz), p.z)).ToArray();
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        [Fact]
        public void WallsStandBehindLotsCutIntoTheHill()
        {
            // walls are off by default now (the user dislikes the quays): switched on here
            var plan = RingSlope(out var site, ("levelWalls", true));
            var quays = plan.Layers.SelectMany(l => l.Nets).Where(n => n.Category == AssetCategories.NetQuay).ToList();
            _out.WriteLine($"cells {Cells(plan).Length}, walls {Fact(plan, "polishLevelWalls")} ({Fact(plan, "polishLevelWallLength"):0} m), quays {quays.Count}, lots {Fact(plan, "polishLevelLotsAll")} lost {Fact(plan, "polishLevelLotsLost")}");
            Assert.True(Fact(plan, "polishLevelWalls") >= 1);
            Assert.NotEmpty(quays);
            Assert.All(quays, n => Assert.False(n.FollowTerrain));
            // the lots stay level; no curtains away from the walls' ends
            var built = Built(plan, site);
            int tilted = 0;
            foreach (var c in Cells(plan))
            {
                var q = new[] { c.A.xz, c.B.xz, c.C.xz, c.D.xz };
                var m = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
                float2 u = math.normalizesafe(q[1] - q[0]), v = math.normalizesafe(q[3] - q[0]);
                float su = math.abs(built(m + u * 2.5f) - built(m - u * 2.5f)) / 5f, sv = math.abs(built(m + v * 2.5f) - built(m - v * 2.5f)) / 5f;
                if (math.min(su, sv) > 0.04f) tilted++;
            }
            Assert.True(tilted <= Cells(plan).Length * 0.05, $"{tilted} zoned cells not level");
            var ends = quays.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            var nets = quays.Select(n => (n.A, n.B, n.C, n.D, 4f, TerrainMetrics.SeatQuay)).ToList();
            var off = TerrainMetrics.OffNets(nets);
            float2 mn = new float2(-140, -95), mx = new float2(140, 95);
            var cur = TerrainMetrics.Curtains(built, site.HeightAt, mn, mx, TerrainMetrics.Texel, p => off(p) && ends.All(e => math.distance(e, p) > 10f));
            _out.WriteLine(cur.ToString());
            Assert.Equal(0, cur.Count);
            // walls off: banks, no quays
            var banks = RingSlope(out _, ("levelWalls", false));
            Assert.DoesNotContain(banks.Layers.SelectMany(l => l.Nets), n => n.Category == AssetCategories.NetQuay);
        }
    }
}
