using Placemaker.Core.Suite;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests.Suite
{
    public class SuiteLayoutGrowTests
    {
        [Fact]
        public void LandLaidOutLater_MatchesALaunchWithIt_AndNeverTouchesWhatIsLaidOut()
        {
            int n = SuiteLayout.HeightmapSize;
            var map = new ushort[n * n];
            for (int i = 0; i < map.Length; i++) map[i] = (ushort)(9000 + (i % n) / 7 + (i / n) / 5);   // a tilted map

            var launch = new SuiteLayout();
            launch.AddPlot(new SuitePlot { Size = new float2(140, 140), Ground = "hills" });
            var live = launch.Heightmap(map);
            float2 cell = launch.MapExtent / n;
            int2 build = (int2)((new float2(launch.LineX(2) + 70f, launch.LineZ(1) + 60f) - launch.MapOffset) / cell);
            live[build.y * n + build.x] ^= 0x55;   // a standing build's own change to the ground, inside the grid
            var before = (ushort[])live.Clone();
            float2 keepMin = launch.FixtureMin, keepMax = launch.FixtureMax;
            float2 gridMin = launch.GridMin, gridMax = launch.GridMax, p1Min = launch.Plots[0].OuterMin, p1Max = launch.Plots[0].OuterMax;

            launch.AddPlot(new SuitePlot { Size = new float2(400, 900), Ground = "mountains" });   // taller than the grid: the band grows too
            int changed = launch.PatchOutside(live, map, keepMin, keepMax);
            Assert.True(changed > 1000);

            var fresh = launch.Heightmap(map);   // as if both plots were there at launch
            int differ = 0, touchedInside = 0;
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    int i = py * n + px;
                    float x = launch.MapOffset.x + (px + 0.5f) * cell.x, z = launch.MapOffset.y + (py + 0.5f) * cell.y;
                    // laid out: the grid and the first plot (the space between them in their bounding box is the layout's ease)
                    bool inside = x >= gridMin.x && x <= gridMax.x && z >= gridMin.y && z <= gridMax.y || x >= p1Min.x && x <= p1Max.x && z >= p1Min.y && z <= p1Max.y;
                    if (inside) { if (live[i] != before[i]) touchedInside++; }
                    else if (live[i] != fresh[i]) differ++;
                }
            Assert.Equal(0, touchedInside);
            Assert.Equal(0, differ);
        }

        /// <summary>(P2-4) Land laid out later in a second row of plots (inside the first row's bounding box) gets its ground: it was left flat.</summary>
        [Fact]
        public void LandInASecondRowGetsItsGround()
        {
            int n = SuiteLayout.HeightmapSize;
            var map = new ushort[n * n];
            for (int i = 0; i < map.Length; i++) map[i] = 9000;
            var l = new SuiteLayout();
            var live = l.Heightmap(map);
            int rowsBefore = 0;
            for (int k = 0; k < 40; k++)
            {
                float2 keepMin = l.FixtureMin, keepMax = l.FixtureMax;
                var p = l.AddPlot(new SuitePlot { Size = new float2(220, 200), Ground = "mountains" });
                if (!l.OnMap(p)) { l.Plots.Remove(p); break; }
                int changed = l.PatchOutside(live, map, keepMin, keepMax);
                Assert.True(changed > 3000, $"{p.Name} got {changed} texels");
                // inside the plot the ground is what a launch with it would have laid
                var fresh = l.Heightmap(map);
                float2 cell = l.MapExtent / n;
                int wrong = 0;
                for (float z = p.RingMin.y; z <= p.RingMax.y; z += cell.y)
                    for (float x = p.RingMin.x; x <= p.RingMax.x; x += cell.x)
                    {
                        int2 c = (int2)((new float2(x, z) - l.MapOffset) / cell);
                        int i = c.y * n + c.x;
                        if (live[i] != fresh[i]) wrong++;
                    }
                Assert.True(wrong == 0, $"{p.Name}: {wrong} texels of its ground not laid");
                if (p.Center.y < l.Plots[0].Center.y - 100f) rowsBefore++;
            }
            Assert.True(rowsBefore >= 1, "no second row was reached");
        }
    }
}
