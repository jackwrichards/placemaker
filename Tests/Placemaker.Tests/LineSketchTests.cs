// (P2-1) Sketch lines (Core/Tool/LineSketch): open polylines drawn on the world, saved as hand edits; parking turns them
// into drive aisles and walkways through the rows.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Layers.Parking;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class LineSketchTests
    {
        [Fact]
        public void SavedFormRoundTrips()
        {
            var pts = new[] { new float2(1.25f, -3.5f), new float2(40f, 12.75f), new float2(41.5f, 60f) };
            var back = LineSketch.Decode(LineSketch.Encode(pts));
            Assert.Equal(3, back.Length);
            for (int i = 0; i < 3; i++) Assert.True(math.distance(pts[i], back[i]) < 0.01f);
            Assert.Empty(LineSketch.Decode("1,2"));        // one point is not a line
            Assert.Empty(LineSketch.Decode("1,2;x,4"));    // not numbers
            Assert.Empty(LineSketch.Decode(""));
        }

        [Fact]
        public void SnapsToCornersSidesAndSquareAngles()
        {
            var snaps = new SketchSnaps { Reach = 2f };
            snaps.AddRing(new[] { new float2(0, 0), new float2(100, 0), new float2(100, 60), new float2(0, 60) });
            var s = new LineSketch();
            // a corner within reach
            var p = s.Snap(new float2(1.2f, 0.9f), snaps, false, out var how);
            Assert.Equal(SketchSnap.Point, how);
            Assert.True(math.distance(p, new float2(0, 0)) < 1e-4f);
            // onto a side
            p = s.Snap(new float2(50f, 1.4f), snaps, false, out how);
            Assert.Equal(SketchSnap.Segment, how);
            Assert.True(math.abs(p.y) < 1e-4f);
            // square with the side from the first point: a little off 90° becomes 90°
            s.Add(new float3(50, 0, 0));
            p = s.Snap(new float2(52f, 30f), snaps, false, out how);
            Assert.Equal(SketchSnap.Angle, how);
            Assert.True(math.abs(p.x - 50f) < 0.01f, "x " + p.x);
            // Shift: as clicked
            p = s.Snap(new float2(52f, 30f), snaps, true, out how);
            Assert.Equal(SketchSnap.None, how);
            // finish: a hand edit with the points
            s.Add(new float3(50, 0, 60));
            var t = s.Finish(ParkingIds.LineAisle);
            Assert.True(t.HasValue);
            Assert.Equal(2, LineSketch.Decode(t.Value.Arg).Length);
            Assert.False(s.Drawing);
            Assert.Equal(0, LineSketch.Hit(new List<HandTouch> { t.Value }, new float2(50.8f, 30), 1.5f, k => k.StartsWith("line.")));
        }

        private static readonly float3[] Rect = ParkingKit.Rect(160, 110);
        private static List<EdgeMarker> Entrance() => new List<EdgeMarker> { ParkingKit.Marker(Rect, 0, 60) };

        private static HandTouch Line(string kind, params float2[] pts) => new HandTouch(kind, new float3(pts[0].x, 0, pts[0].y), float.NaN, LineSketch.Encode(pts));

        [Fact]
        public void DrawnAisleCutsThroughTheRowsAndJoinsTheLanesItCrosses()
        {
            var before = ParkingKit.Run(Rect, markers: Entrance());
            // across the rows, corner to corner of the inner grid
            var a = new float2(30, 15); var b = new float2(130, 95);
            var after = ParkingKit.Run(Rect, markers: Entrance(), touches: new List<HandTouch> { Line(ParkingIds.LineAisle, a, b) });
            Assert.True(after.Layout.SketchCut > 0);
            Assert.True(after.Stalls < before.Stalls, $"{after.Stalls} stalls, were {before.Stalls}");
            Assert.Contains(after.Layout.LiveLines, l => l.Kind == LineKind.Sketch);
            // its nets are split where it crosses the aisles: inner ends on the drawn line, shared with another net
            var drive = after.Plan.Nets.Where(n => n.Role == NetRole.DriveAisle).ToList();
            bool OnLine(float3 p) => LineSketch.DistPointSeg(p.xz, a, b) < 0.05f && math.distance(p.xz, a) > 1 && math.distance(p.xz, b) > 1;
            int junctions = drive.Count(n => OnLine(n.A) && drive.Count(m => !ReferenceEquals(m, n) && (math.distance(m.A, n.A) < 0.01f || math.distance(m.D, n.A) < 0.01f)) >= 2);
            Assert.True(junctions >= 2, "junctions on the drawn aisle: " + junctions);
            // no stall left on it
            foreach (var bay in after.Layout.LiveBays)
            {
                var c = after.Layout.Frame.ToWorld(bay.Center);
                Assert.True(LineSketch.DistPointSeg(new float2((float)c.x, (float)c.y), a, b) > 1.5f);
            }
        }

        [Fact]
        public void DrawnWalkwayIsAFootpathThroughTheRows()
        {
            var a = new float2(80, 2); var b = new float2(80, 108);
            var r = ParkingKit.Run(Rect, markers: Entrance(), touches: new List<HandTouch> { Line(ParkingIds.LineWalk, a, b) });
            Assert.Contains(r.Layout.LiveLines, l => l.Kind == LineKind.Walk);
            Assert.True(r.Layout.SketchCut > 0);
            Assert.Contains(r.Plan.Nets, n => n.Role == NetRole.Pathway && LineSketch.DistPointSeg(n.A.xz, a, b) < 0.05f);
            // and it survives the build receipt like every hand edit
            Assert.False(r.Plan.Facts.ContainsKey("touches.dropped"));
        }

        [Fact]
        public void SlowPathsAndTheInvisibleStallCountAsOwn()
        {
            // the vanilla-only rule for automatic assets must not swap them for their vanilla fallbacks
            Assert.True(Placemaker.Core.Build.AutoAssets.IsOwn(ParkingAssets.Drive(7).Id));
            Assert.True(Placemaker.Core.Build.AutoAssets.IsOwn(ParkingAssets.OneWay(4).Id));
            Assert.True(Placemaker.Core.Build.AutoAssets.IsOwn(ParkingAssets.InvisibleBay));
        }
    }
}
