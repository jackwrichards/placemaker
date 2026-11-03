using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ToolPolygonEditorTests
    {
        private static PolygonEditor Square(float size = 100)
        {
            var e = new PolygonEditor();
            foreach (var p in ToolTestData.Square(size)) Assert.True(e.AddPoint(p));
            Assert.True(e.Close());
            return e;
        }

        [Fact]
        public void DrawAndCloseGivesCcwRing()
        {
            var e = new PolygonEditor();
            // Drawn clockwise on purpose.
            e.AddPoint(new float3(0, 0, 0));
            e.AddPoint(new float3(0, 0, 50));
            e.AddPoint(new float3(50, 0, 50));
            e.AddPoint(new float3(50, 0, 0));
            Assert.Null(e.CheckClose());
            Assert.True(e.Close());
            var ring = e.RingCcw();
            Assert.Equal(4, ring.Length);
            Assert.True(Poly.SignedArea(Poly.To2D(ring)) > 0);
            Assert.Equal(2500f, e.Area(), 1);
            Assert.Equal(200f, e.Perimeter(), 1);
            Assert.True(e.IsValid);
        }

        [Fact]
        public void AddingACrossingSegmentIsRefused()
        {
            var e = new PolygonEditor();
            e.AddPoint(new float3(0, 0, 0));
            e.AddPoint(new float3(100, 0, 0));
            e.AddPoint(new float3(100, 0, 100));
            Assert.Equal("Placemaker.Problem.Crossing", e.CheckAdd(new float2(50, -50)));
            Assert.False(e.AddPoint(new float3(50, 0, -50)));
            Assert.Equal("Placemaker.Problem.TooClose", e.CheckAdd(new float2(100.1f, 100)));
        }

        [Fact]
        public void CannotCloseWithTwoPointsOrWhenCrossing()
        {
            var e = new PolygonEditor();
            e.AddPoint(new float3(0, 0, 0));
            e.AddPoint(new float3(10, 0, 0));
            Assert.Equal("Placemaker.Problem.NeedThreePoints", e.CheckClose());
            Assert.False(e.Close());
        }

        [Fact]
        public void RemoveLastWhileOpen()
        {
            var e = new PolygonEditor();
            e.AddPoint(new float3(0, 0, 0));
            e.AddPoint(new float3(10, 0, 0));
            Assert.True(e.RemoveLast());
            Assert.Equal(1, e.Count);
            var closed = Square();
            Assert.False(closed.RemoveLast());
        }

        [Fact]
        public void RemovePointKeepsAtLeastThreeAndStaysSimple()
        {
            var e = Square();
            Assert.True(e.RemovePoint(0));
            Assert.Equal(3, e.Count);
            Assert.False(e.RemovePoint(0));
        }

        [Fact]
        public void RemovingAPointThatWouldCauseCrossingIsRefused()
        {
            // Square with a spike reaching in from the left: dropping corner 2 would make the
            // diagonal (100,0)-(0,100) cut through the spike.
            var e = new PolygonEditor();
            e.AddPoint(new float3(0, 0, 0));
            e.AddPoint(new float3(100, 0, 0));
            e.AddPoint(new float3(100, 0, 100));
            e.AddPoint(new float3(0, 0, 100));
            e.AddPoint(new float3(0, 0, 60));
            e.AddPoint(new float3(80, 0, 50));
            e.AddPoint(new float3(0, 0, 40));
            Assert.True(e.Close());
            Assert.False(e.RemovePoint(2));
            Assert.Equal(7, e.Count);
            Assert.True(e.IsValid);
            Assert.True(e.RemovePoint(5));        // removing the spike tip is fine
        }

        [Fact]
        public void MovePointStopsBeforeSelfIntersection()
        {
            var e = Square();
            var from = e.Points[2];
            // Dragging corner 2 across edge 0 would invert the square.
            var applied = e.MovePoint(2, new float3(50, 0, -50), from);
            Assert.True(e.IsValid);
            Assert.True(applied.z > -50);
            Assert.True(applied.z < 100);
        }

        [Fact]
        public void MovePointFreeWhenValid()
        {
            var e = Square();
            var target = new float3(120, 0, 130);
            Assert.Equal(target, e.MovePoint(2, target, e.Points[2]));
        }

        [Fact]
        public void InsertPointOnEdge()
        {
            var e = Square();
            int idx = e.InsertPoint(0, new float3(50, 0, 0));
            Assert.Equal(1, idx);
            Assert.Equal(5, e.Count);
            Assert.Equal(-1, e.InsertPoint(0, new float3(0.1f, 0, 0)));   // too close to a corner
        }

        [Fact]
        public void HitTests()
        {
            var e = Square();
            Assert.Equal(2, e.HitPoint(new float2(98, 99), 5));
            Assert.Equal(-1, e.HitPoint(new float2(50, 50), 5));
            Assert.Equal(3, e.HitEdge(new float2(1, 50), 5, out float t, out float2 foot));
            Assert.Equal(0.5f, t, 2);
            Assert.Equal(0f, foot.x, 3);
        }

        [Fact]
        public void EdgeDragParallelAndClampedAtCollapse()
        {
            var e = Square();
            Assert.True(e.BeginEdgeDrag(0));           // bottom edge (0,0)-(100,0), normal points +z (inward)
            float applied = e.DragEdge(-20, false);   // outward
            Assert.Equal(-20f, applied, 3);
            Assert.Equal(-20f, e.Points[0].z, 3);
            Assert.Equal(-20f, e.Points[1].z, 3);
            applied = e.DragEdge(150, false);          // would cross the top edge
            Assert.True(applied < 100);
            Assert.True(e.IsValid);
            e.EndEdgeDrag();
        }

        [Fact]
        public void EdgeDragSlideKeepsNeighbourDirections()
        {
            // Trapezoid: slanted sides must keep their direction when the top edge slides.
            var e = new PolygonEditor();
            e.AddPoint(new float3(0, 0, 0));
            e.AddPoint(new float3(100, 0, 0));
            e.AddPoint(new float3(80, 0, 50));
            e.AddPoint(new float3(20, 0, 50));
            e.Close();
            e.BeginEdgeDrag(2);                         // top edge (80,50)->(20,50), normal points -z
            e.DragEdge(-10, true);                       // push outward (+z)
            var p2 = e.Points[2];
            // Neighbour edge 1 goes from (100,0) towards (80,50): direction (-20,50); new corner stays on that line.
            float cross = (p2.x - 100) * 50 - (p2.z - 0) * (-20);
            Assert.Equal(0f, cross, 2);
            Assert.Equal(60f, p2.z, 2);
            e.CancelEdgeDrag();
            Assert.Equal(50f, e.Points[2].z, 3);
        }

        [Fact]
        public void SnapshotRestore()
        {
            var e = Square();
            var s = e.Save();
            e.Clear();
            Assert.True(e.IsEmpty);
            e.Restore(s);
            Assert.True(e.Closed);
            Assert.Equal(4, e.Count);
        }

        [Fact]
        public void RoadNodes_FollowTheCorners_ThroughEditsAndUndo()
        {
            // Area overhaul: corners remember the road node they were snapped to (two in a row follow the road).
            var e = new PolygonEditor();
            e.AddPoint(new float3(0, 0, 0), default, 11L);
            e.AddPoint(new float3(100, 0, 0), default, 12L);
            e.AddPoint(new float3(100, 0, 80), default);
            e.AddPoint(new float3(0, 0, 80), default, 14L);
            Assert.True(e.Close());
            Assert.Equal(new long[] { 11, 12, 0, 14 }, e.Nodes);
            Assert.Equal(1, e.InsertPoint(0, new float3(50, 0, 0)));
            Assert.Equal(new long[] { 11, 0, 12, 0, 14 }, e.Nodes);
            var s = e.Save();
            Assert.True(e.RemovePoint(1));
            e.SetNode(1, 0L);
            Assert.Equal(new long[] { 11, 0, 0, 14 }, e.Nodes);
            e.Restore(s);
            Assert.Equal(new long[] { 11, 0, 12, 0, 14 }, e.Nodes);
            // An edge dragged away leaves its nodes.
            Assert.True(e.BeginEdgeDrag(4));
            e.DragEdge(-5f, false);
            e.EndEdgeDrag();
            Assert.Equal(0L, e.Nodes[4]);
            Assert.Equal(0L, e.Nodes[0]);
            Assert.Equal(12L, e.Nodes[2]);
        }
    }
}
