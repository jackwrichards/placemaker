// s49: Smart edges rule defaults (Zoned = built-on cells only, "All zoned" separate) and the obstacle cache.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class GeometryObstacleCacheTests
    {
        private static float2[] Sq(float x, float z, float s = 8f) => new[] { new float2(x, z), new float2(x + s, z), new float2(x + s, z + s), new float2(x, z + s) };

        [Fact]
        public void Defaults_StopAtAllZoned_NotTheEmptyGrid()
        {
            // Area overhaul: zoned land is kept off whether built on or not ("it goes onto zoned areas when it shouldn't").
            var r = BrushRules.Defaults();
            Assert.True(r.Stops(BrushObstacleKind.ZonedCell));
            Assert.True(r.Stops(BrushObstacleKind.UnbuiltZonedCell));
            Assert.False(r.Stops(BrushObstacleKind.EmptyZoneCell));
            Assert.True(r.Stops(BrushObstacleKind.PlacemakerBuild));
            Assert.Equal(2, r.ZoningLevel);
            r.SetZoningLevel(1);
            Assert.True(r.Stops(BrushObstacleKind.ZonedCell));
            Assert.False(r.Stops(BrushObstacleKind.UnbuiltZonedCell));
            r.SetZoningLevel(3);
            Assert.True(r.Stops(BrushObstacleKind.EmptyZoneCell));
            r.SetZoningLevel(0);
            Assert.False(r.Stops(BrushObstacleKind.ZonedCell));
        }

        [Fact]
        public void AllZoned_AddsVacantZonedCells()
        {
            var r = BrushRules.Defaults();
            r.AllZoned = true;
            Assert.True(r.Stops(BrushObstacleKind.UnbuiltZonedCell));
            r.Anything = true;
            Assert.False(r.Stops(BrushObstacleKind.UnbuiltZonedCell));
            Assert.False(BrushRules.Defaults().Equals(new BrushRules { AllZoned = false }));
        }

        [Fact]
        public void Build_VacantZonedLandDoesNotFence_ButOccupiedDoes()
        {
            var block = Sq(0, 0, 100f);
            var obstacles = new List<BrushObstacle>
            {
                new BrushObstacle(BrushObstacleKind.UnbuiltZonedCell, Sq(10, 10, 40f)),
                new BrushObstacle(BrushObstacleKind.ZonedCell, Sq(60, 60, 20f)),
            };
            var built = BrushRules.Defaults(); built.SetZoningLevel(1);
            Assert.Single(built.Build(new float2(5, 5), block, obstacles).Obstacles);
            Assert.Equal(2, BrushRules.Defaults().Build(new float2(5, 5), block, obstacles).Obstacles.Count);
        }

        [Fact]
        public void Cache_AnswersSmallerBoxWithSameGroup_AndFiltersToIt()
        {
            var c = new ObstacleCache();
            c.Store(new float2(0, 0), new float2(100, 100), 7, 10f,
                new List<BrushObstacle> { new BrushObstacle(BrushObstacleKind.Lot, Sq(10, 10)), new BrushObstacle(BrushObstacleKind.Lot, Sq(80, 80)) });
            var hit = c.TryGet(new float2(0, 0), new float2(40, 40), 7, 11f, false);
            Assert.NotNull(hit);
            Assert.Single(hit);
            Assert.Null(c.TryGet(new float2(0, 0), new float2(40, 40), 8, 11f, false));      // other group
            Assert.Null(c.TryGet(new float2(0, 0), new float2(200, 40), 7, 11f, false));     // bigger box
            Assert.Null(c.TryGet(new float2(0, 0), new float2(40, 40), 7, 11f, true));       // fresh read wanted
            Assert.Null(c.TryGet(new float2(0, 0), new float2(40, 40), 7, 10f + ObstacleCache.MaxAge + 1f, false)); // too old
        }

        [Fact]
        public void Cache_ReturnsACopy_SoCallersCanAdd()
        {
            var c = new ObstacleCache();
            c.Store(new float2(0, 0), new float2(100, 100), 0, 0f, new List<BrushObstacle> { new BrushObstacle(BrushObstacleKind.Building, Sq(10, 10)) });
            var a = c.TryGet(new float2(0, 0), new float2(50, 50), 0, 1f, false);
            a.Add(new BrushObstacle(BrushObstacleKind.Road, Sq(20, 20)));
            Assert.Single(c.TryGet(new float2(0, 0), new float2(50, 50), 0, 1f, false));
        }

        [Fact]
        public void Stats_CountPerKindAndArea()
        {
            var s = ObstacleStats.Of(new[]
            {
                new BrushObstacle(BrushObstacleKind.ZonedCell, Sq(0, 0, 8f)),
                new BrushObstacle(BrushObstacleKind.UnbuiltZonedCell, Sq(20, 0, 8f)),
                new BrushObstacle(BrushObstacleKind.UnbuiltZonedCell, Sq(40, 0, 8f)),
            });
            Assert.Equal(1, s.Count[(int)BrushObstacleKind.ZonedCell]);
            Assert.Equal(2, s.Count[(int)BrushObstacleKind.UnbuiltZonedCell]);
            Assert.Equal(3, s.Total);
            Assert.Equal(192f, s.TotalArea, 1);
            Assert.Contains("zonedUnbuilt=2", s.ToString());
        }
    }
}
