// (P2-1) Manual touches on parking lots: hand edits are kept in the design (recipe JSON, build receipt), applied after the
// layout, follow the stall they were made on, and are left out with a note when nothing is there any more.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Parking;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ParkingTouchTests
    {
        private static readonly float3[] Rect = ParkingKit.Rect(160, 110);
        private static List<EdgeMarker> Entrance() => new List<EdgeMarker> { ParkingKit.Marker(Rect, 0, 60) };

        private static ParkingRun Run(params HandTouch[] touches) =>
            ParkingKit.Run(Rect, markers: Entrance(), touches: new List<HandTouch>(touches));

        private static float3 W(Layout l, Bay b) { var w = l.Frame.ToWorld(b.Center); return new float3((float)w.x, 0, (float)w.y); }

        private static bool Square(Bay b) => System.Math.Abs(PGeo.Dot(b.Axis, b.RowDir)) < 1e-6;

        /// <summary>A plain 90° stall in the middle of a long row far from the entrance (the automatic accessible group sits at the entrance).</summary>
        private static Bay FarStall(ParkingRun r)
        {
            var e = new double2(Entrance()[0].Position.x, Entrance()[0].Position.z);
            return r.Layout.LiveBays.Where(b => b.Role == BayRole.Normal && Square(b) && b.Kind == BayKind.Inner)
                .OrderByDescending(b => math.distance(r.Layout.Frame.ToWorld(b.Center), e)).Skip(3).First();
        }

        [Fact]
        public void AccessibleTouchMovesTheAccessibleGroup()
        {
            var before = Run();
            int acc = before.Layout.CountRole(BayRole.Disabled);
            Assert.True(acc > 0);
            var at = W(before.Layout, FarStall(before));
            var after = Run(new HandTouch(ParkingIds.TouchAccessible, at));
            // the same number of accessible stalls (the share), now at the touch
            Assert.Equal(acc, after.Layout.CountRole(BayRole.Disabled));
            var near = after.Layout.LiveBays.Where(b => b.Role == BayRole.Disabled)
                .Min(b => math.distance(after.Layout.Frame.ToWorld(b.Center), new double2(at.x, at.z)));
            Assert.True(near < 4, "accessible stall " + near + " m from the touch");
            Assert.Contains(after.Plan.TouchTargets, t => t.Group == "stall" && t.State == "accessible" && t.Touched == ParkingIds.TouchAccessible);
            Assert.False(after.Plan.Facts.ContainsKey("touches.dropped"));
        }

        [Fact]
        public void PlainTouchKeepsAutomaticStallsAway()
        {
            var before = Run();
            var auto = before.Layout.LiveBays.First(b => b.Role == BayRole.Disabled);
            var after = Run(new HandTouch(ParkingIds.TouchPlain, W(before.Layout, auto)));
            var p = before.Layout.Frame.ToWorld(auto.Center);
            Assert.DoesNotContain(after.Layout.LiveBays, b => b.Role == BayRole.Disabled && math.distance(after.Layout.Frame.ToWorld(b.Center), p) < 1);
            Assert.True(after.Layout.CountRole(BayRole.Disabled) > 0, "the group moved elsewhere");
        }

        [Fact]
        public void BlankAndIslandTakeAStallAway()
        {
            var before = Run();
            var far = FarStall(before);
            var blank = Run(new HandTouch(ParkingIds.TouchBlank, W(before.Layout, far)));
            Assert.Equal(before.Stalls - 1, blank.Stalls);
            Assert.Contains(blank.Plan.TouchTargets, t => t.State == "blank");
            var island = Run(new HandTouch(ParkingIds.TouchIsland, W(before.Layout, far)));
            Assert.Equal(before.Stalls - 1, island.Stalls);
            // the island is still offered to the tool (to take it back)
            Assert.Contains(island.Plan.TouchTargets, t => t.State == "island" && t.Touched == ParkingIds.TouchIsland);
        }

        [Fact]
        public void ChargerTouchMakesAPairWithACharger()
        {
            var before = Run();
            var after = Run(new HandTouch(ParkingIds.TouchCharger, W(before.Layout, FarStall(before))));
            var hand = after.Layout.Bays.Where(b => !b.Removed && b.TouchedBy == ParkingIds.TouchCharger).ToList();
            Assert.Equal(2, hand.Count);
            Assert.All(hand, b => Assert.Equal(BayRole.Electric, b.Role));
            Assert.Equal(before.Layout.ElectricPairs.Count, after.Layout.ElectricPairs.Count);   // counted toward the share
        }

        [Fact]
        public void TouchWithNothingThereIsDroppedWithANote()
        {
            var r = Run(new HandTouch(ParkingIds.TouchAccessible, new float3(500, 0, 500)));
            Assert.Equal(1.0, r.Plan.Facts["touches.dropped"]);
            Assert.Contains(r.Plan.Issues, i => i.Key == Touches.DroppedKey);
        }

        [Fact]
        public void PickedEdgeTurnsTheRows()
        {
            // the rows follow the longest (south) edge on their own; the picked west edge turns them a quarter
            var free = Run();
            var r = Run(new HandTouch(ParkingIds.TouchEdge, new float3(0, 0, 55)));
            Assert.InRange(System.Math.Abs(r.Layout.RowAngleDeg - 90), 0, 0.5);
            Assert.NotEqual(free.Layout.RowAngleDeg, r.Layout.RowAngleDeg);
            Assert.Contains(r.Plan.TouchTargets, t => t.Group == "grid" && t.State == "picked");
            Assert.True(r.Stalls > 100);
        }

        [Fact]
        public void GridTurnTurnsTheRows()
        {
            var b = new ParkingLayer().Defaults();
            b.Values[ParkingIds.GridTurn] = "30";
            var r = ParkingKit.Run(Rect, b, Entrance());
            Assert.InRange(r.Layout.RowAngleDeg, 29.5, 30.5);
            Assert.True(r.Stalls > 100);
        }

        [Fact]
        public void TouchesSurviveRecipeJsonAndTheBuildReceipt()
        {
            var root = new RecipeStep { Layer = "parking" };
            root.Touches.Add(new HandTouch(ParkingIds.TouchAccessible, new float3(1, 2, 3)));
            root.Touches.Add(new HandTouch(ParkingIds.TouchEdge, new float3(4, 5, 6), 1.25f, "x"));
            var child = new RecipeStep { Layer = "decals" };
            child.Touches.Add(new HandTouch("thing.kept", new float3(7, 0, 8)) { Version = 9 });   // from a newer Placemaker: carried, never applied
            root.Children.Add(child);

            var json = RecipeJson.ReadStep(RecipeJson.WriteStep(root), 0);
            Assert.Equal(2, json.Touches.Count);
            Assert.Equal(1.25f, json.Touches[1].Yaw, 3);
            Assert.Equal("x", json.Touches[1].Arg);
            Assert.True(float.IsNaN(json.Touches[0].Yaw));
            Assert.Equal(9, json.Children[0].Touches[0].Version);

            var rec = new GroupRecord { GroupId = 1, Recipe = new Recipe { Id = "parking", Root = root } };
            var back = ReceiptCodec.Decode(ReceiptCodec.Encode(rec));
            Assert.Equal(2, back.Recipe.Root.Touches.Count);
            Assert.Equal(ParkingIds.TouchEdge, back.Recipe.Root.Touches[1].Kind);
            Assert.Equal(new float3(4, 5, 6), back.Recipe.Root.Touches[1].Position);
            Assert.Single(back.Recipe.Root.Children[0].Touches);
            Assert.Empty(Touches.Valid(back.Recipe.Root.Children[0].Touches));
            Assert.Equal(root.Touches.Count, root.Clone().Touches.Count);
        }

        [Fact]
        public void TheLotWorksRoundAnExistingBuilding()
        {
            // a 20 x 16 m building in the middle of the lot: no stall or aisle touches it, the rest still parks
            var site = new SiteContext();
            site.Obstacles.Add(new Obstacle { A = new float2(70, 45), B = new float2(90, 45), C = new float2(90, 61), D = new float2(70, 61), Kind = ObstacleKind.Building });
            var free = Run();
            var r = ParkingKit.Run(Rect, site: site, markers: Entrance());
            var lot = new[] { new double2(70, 45), new double2(90, 45), new double2(90, 61), new double2(70, 61) };
            foreach (var b in r.Layout.LiveBays)
                foreach (var q in b.Quad) Assert.False(PGeo.Contains(lot, r.Layout.Frame.ToWorld(q)), "a stall corner on the building");
            foreach (var n in r.Plan.Nets.Where(n => n.Role == NetRole.DriveAisle))
                Assert.True(PGeo.DistPointSeg(new double2(80, 53), new double2(n.A.x, n.A.z), new double2(n.D.x, n.D.z)) > 8, "an aisle through the building");
            Assert.True(r.Stalls > free.Stalls / 2, r.Stalls + " of " + free.Stalls);
            Assert.Contains(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.AroundBuildings");
        }

        [Fact]
        public void ToggleAddsReplacesAndRemoves()
        {
            var list = new List<HandTouch>();
            var t = new HandTouch(ParkingIds.TouchAccessible, new float3(10, 0, 10));
            Assert.True(Touches.Toggle(list, t));
            Assert.Single(list);
            var target = new TouchTarget { Group = "stall", Center = new float3(11, 0, 10), Touched = ParkingIds.TouchAccessible, TouchAt = t.Position };
            Assert.True(Touches.Toggle(list, new HandTouch(ParkingIds.TouchCharger, target.Center), target));
            Assert.Equal(ParkingIds.TouchCharger, list[0].Kind);
            target.Touched = ParkingIds.TouchCharger; target.TouchAt = target.Center;
            Assert.True(Touches.Toggle(list, new HandTouch(ParkingIds.TouchCharger, target.Center), target));
            Assert.Empty(list);
            Touches.Toggle(list, new HandTouch(ParkingIds.TouchEdge, new float3(0, 0, 0)), new TouchTarget(), single: true);
            Touches.Toggle(list, new HandTouch(ParkingIds.TouchEdge, new float3(50, 0, 0)), new TouchTarget(), single: true);
            Assert.Single(list);
            Assert.Equal(50, list[0].Position.x);
        }

        [Fact]
        public void ZoneGridLinesTheRowsUpWithTheBlocksNearby()
        {
            var lot = new List<double2> { new double2(0, 0), new double2(100, 0), new double2(100, 60), new double2(0, 60) };
            // a block of a street turned 30 degrees, just north of the lot (Direction points to its road), and a far one
            float r = math.radians(30);
            var along = new float2(math.cos(r), math.sin(r));
            var near = new ZoneGrid { Position = new float2(50, 80), Direction = new float2(-along.y, along.x), Size = new int2(10, 4) };
            var far = new ZoneGrid { Position = new float2(500, 500), Direction = new float2(1, 0), Size = new int2(20, 6) };
            var dir = LayoutEngine.ZoneGridEdge(lot, new List<ZoneGrid> { far, near });
            Assert.True(dir.HasValue);
            double deg = (System.Math.Atan2(dir.Value.y, dir.Value.x) * 180 / System.Math.PI + 360) % 180;
            Assert.True(System.Math.Abs(deg - 30) < 0.5 || System.Math.Abs(deg - 210 % 180) < 0.5, "rows at " + deg);
            Assert.Null(LayoutEngine.ZoneGridEdge(lot, new List<ZoneGrid> { far }));
        }
    }
}
