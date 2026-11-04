// Area overhaul: the shared fill (Core/Geometry/AreaFill) — exact corners, gaps closed below the gap setting, cracks
// left out, the reach in open land, turning circles kept out, a click on a wall, narrow places.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class GeometryAreaFillTests
    {
        private readonly ITestOutputHelper _out;
        public GeometryAreaFillTests(ITestOutputHelper output) { _out = output; }

        private static float2[] Rect(float x0, float y0, float x1, float y1) =>
            new[] { new float2(x0, y0), new float2(x1, y0), new float2(x1, y1), new float2(x0, y1) };

        private static BrushObstacle Road(float2[] ring) => new BrushObstacle(BrushObstacleKind.Road, ring);

        /// <summary>A ring of 12 m roads around the inner rectangle [0,100] × [0,80].</summary>
        private static List<BrushObstacle> Block(float w = 100f, float h = 80f) => new List<BrushObstacle>
        {
            Road(Rect(-12, -12, w + 12, 0)), Road(Rect(-12, h, w + 12, h + 12)),
            Road(Rect(-12, 0, 0, h)), Road(Rect(w, 0, w + 12, h)),
        };

        private static WallQuery Q(List<BrushObstacle> walls) => (mn, mx, into) => into.AddRange(walls);

        private FillResult Fill(List<BrushObstacle> walls, float2 seed, float gap = 10f, float reach = 500f)
        {
            var r = AreaFill.Fill(seed, Q(walls), null, new FillOptions { Gap = gap, Reach = reach });
            _out.WriteLine(r.ToString());
            return r;
        }

        private static bool HasCorner(float2[] ring, float2 p, float tol = 0.05f)
        {
            foreach (var q in ring) if (math.distance(p, q) <= tol) return true;
            return false;
        }

        [Fact]
        public void Block_FillsToTheCurbs_WithExactCorners()
        {
            var r = Fill(Block(), new float2(30, 30));
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.Equal(8000f, Poly.Area(r.Ring), 0);   // ±0.5 m² … the exact cut, not the grid
            foreach (var c in Rect(0, 0, 100, 80)) Assert.True(HasCorner(r.Ring, c), "corner " + c);
            Assert.Empty(r.Doors);
            Assert.False(r.HitReach);
        }

        [Fact]
        public void ClickNearTheCurb_StillFillsTheBlock()
        {
            var r = Fill(Block(), new float2(0.6f, 40));
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.Equal(8000f, Poly.Area(r.Ring), 0);
        }

        [Fact]
        public void ClickOnARoad_IsOnWall()
        {
            var r = Fill(Block(), new float2(-6, 40));
            Assert.Equal(FillStatus.OnWall, r.Status);
        }

        /// <summary>A 2 m wall across the block at x = 50 with an opening of <paramref name="opening"/> m in the middle.</summary>
        private static List<BrushObstacle> Split(float opening)
        {
            var w = Block();
            float a = 40f - opening * 0.5f, b = 40f + opening * 0.5f;
            w.Add(new BrushObstacle(BrushObstacleKind.Path, Rect(49, -1, 51, a)));
            w.Add(new BrushObstacle(BrushObstacleKind.Path, Rect(49, b, 51, 81)));
            return w;
        }

        [Fact]
        public void AnOpeningNarrowerThanTheGap_IsClosed()
        {
            var r = Fill(Split(8f), new float2(20, 40), gap: 10f);
            Assert.Equal(FillStatus.Ok, r.Status);
            float area = Poly.Area(r.Ring);
            // The west half (49 × 80 = 3920) and at most half the opening's depth.
            Assert.InRange(area, 3900f, 3960f);
            Assert.Single(r.Doors);
            Assert.False(Poly.Contains(r.Ring, new float2(70, 40)));
        }

        [Fact]
        public void AnOpeningWiderThanTheGap_LetsTheFillThrough()
        {
            var r = Fill(Split(14f), new float2(20, 40), gap: 10f);
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.True(Poly.Contains(r.Ring, new float2(70, 40)));
            Assert.Equal(8000f - 2f * 66f, Poly.Area(r.Ring), 0);
        }

        [Fact]
        public void GapOff_LetsTheFillThroughAnyOpening()
        {
            var r = Fill(Split(3f), new float2(20, 40), gap: 0f);
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.True(Poly.Contains(r.Ring, new float2(70, 40)));
        }

        [Fact]
        public void ACrackNarrowerThanTheGap_IsLeftOut()
        {
            // A 4 m wide, 30 m deep slot between two lots on the north side.
            var w = Block(100, 80);
            w.Add(new BrushObstacle(BrushObstacleKind.Building, Rect(10, 50, 48, 80)));
            w.Add(new BrushObstacle(BrushObstacleKind.Building, Rect(52, 50, 90, 80)));
            var r = Fill(w, new float2(50, 20), gap: 10f);
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.False(Poly.Contains(r.Ring, new float2(50, 70)), "the crack's far end");
            // The 10 m wide gaps beside the lots (x 0..10 and 90..100) are as wide as the gap: kept.
            Assert.True(Poly.Contains(r.Ring, new float2(5, 70)));
            var all = Fill(w, new float2(50, 20), gap: 0f);
            Assert.True(Poly.Contains(all.Ring, new float2(50, 70)), "gap off: the crack is filled");
        }

        [Fact]
        public void SixtyDegreeCorners_AreFilledToTheTip()
        {
            // A triangle room (equilateral, side 120) inside a big wall square.
            var tri = new[] { new float2(0, 0), new float2(120, 0), new float2(60, 103.923f) };
            var outer = Rect(-30, -30, 150, 135);
            // Walls: the square minus the triangle, as three quads around it.
            var w = new List<BrushObstacle>
            {
                Road(new[] { outer[0], outer[1], tri[1], tri[0] }),
                Road(new[] { outer[1], outer[2], tri[2], tri[1] }),
                Road(new[] { outer[2], outer[3], outer[0], tri[0], tri[2] }),
            };
            var r = Fill(w, new float2(60, 30), gap: 10f);
            Assert.Equal(FillStatus.Ok, r.Status);
            foreach (var c in tri) { float best = 1e9f; foreach (var q in r.Ring) best = math.min(best, math.distance(q, c)); _out.WriteLine($"corner {c}: nearest ring point {best:F2} m"); }
            Assert.InRange(Poly.Area(r.Ring), Poly.Area(tri) - 3f, Poly.Area(tri) + 1f);
            foreach (var c in tri) Assert.True(HasCorner(r.Ring, c, 0.1f), "corner " + c);
        }

        [Fact]
        public void OpenLand_FillsTheReach()
        {
            var r = Fill(new List<BrushObstacle>(), new float2(500, 500), gap: 10f, reach: 60f);
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.True(r.HitReach);
            Assert.InRange(Poly.Area(r.Ring), math.PI * 60f * 60f * 0.97f, math.PI * 60f * 60f * 1.01f);
        }

        [Fact]
        public void BigReach_GrowsTheWindow()
        {
            var r = Fill(new List<BrushObstacle>(), new float2(0, 0), gap: 10f, reach: 400f);
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.True(r.Attempts > 1);
            Assert.InRange(Poly.Area(r.Ring), math.PI * 400f * 400f * 0.97f, math.PI * 400f * 400f * 1.01f);
        }

        [Fact]
        public void TurningCircle_IsKeptOut()
        {
            // A dead-end road from the south curb into the block, ending in a turning circle of radius 12.
            var w = Block(160, 120);
            w.Add(Road(Rect(74, -1, 86, 60)));
            w.Add(Road(AreaFill.Circle(new float2(80, 60), 12f, 32)));
            var r = Fill(w, new float2(30, 90), gap: 10f);
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.False(Poly.Contains(r.Ring, new float2(80, 60)));
            Assert.False(Poly.Contains(r.Ring, new float2(80, 70)), "inside the circle's far side");
            Assert.True(Poly.Contains(r.Ring, new float2(80, 75)), "just past the circle");
            Assert.True(Poly.Contains(r.Ring, new float2(130, 20)), "the block wraps round the dead end");
            float expect = 160f * 120f - 12f * 61f - math.PI * 144f + 12f * 12f * 0.5f;   // roughly, the stem's end overlaps the circle
            Assert.InRange(Poly.Area(r.Ring), expect - 120f, expect + 120f);
        }

        [Fact]
        public void NarrowPlace_UsesItsOwnWidth()
        {
            // A closed strip 6 m wide and 60 m long: narrower than the gap all round.
            var w = new List<BrushObstacle> { Road(Rect(-20, -20, 80, 0)), Road(Rect(-20, 6, 80, 26)), Road(Rect(-20, 0, 0, 6)), Road(Rect(60, 0, 80, 6)) };
            var r = Fill(w, new float2(30, 3), gap: 10f);
            Assert.Equal(FillStatus.Ok, r.Status);
            Assert.Equal(360f, Poly.Area(r.Ring), 0);
        }
    }
}
