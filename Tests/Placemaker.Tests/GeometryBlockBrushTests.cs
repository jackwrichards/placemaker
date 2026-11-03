// BlockBrush ("Block paint" mode): a brush fenced to one city block, avoiding obstacles (owner: geometry).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class GeometryBlockBrushTests
    {
        // A 200 × 120 m block (curb outline), corner at the origin.
        private static readonly float2[] Block = { new float2(0, 0), new float2(200, 0), new float2(200, 120), new float2(0, 120) };

        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        private static BrushMask Paint(BlockBrush bb)
        {
            var m = new BrushMask(bb.CellSize);
            bb.Attach(m);
            return m;
        }

        private static float OverlapArea(float2[] ring, float2[] other) =>
            Poly.Intersection(ring, other).Sum(r => r.Area);

        private static void AssertInsideBlock(float2[] ring, float2[] block)
        {
            foreach (var p in ring)
                Assert.True(Poly.Contains(block, p) || Poly.DistanceToBoundary(block, p) < 0.05f, $"{p} outside the block");
        }

        [Fact]
        public void Stamps_NeverLeaveTheBlock()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            // Cursor drifts over the road below the block and beyond its right end.
            m.Stamp(new float2(100, -10), 40f, false);
            m.Stroke(new float2(100, -10), new float2(260, 60), 40f, false);
            Assert.False(m.IsEmpty);
            foreach (var (a, b) in m.FillRuns(1))
            {
                float2 c = (a + b) * 0.5f;
                Assert.True(Poly.Contains(Block, c), $"cell at {c} outside the block");
            }
            // Erasing is never fenced.
            m.Stamp(new float2(150, 30), 500f, true);
            Assert.True(m.IsEmpty);
        }

        [Fact]
        public void Limit_WithAnotherCellSizeIsIgnored()
        {
            var bb = new BlockBrush(Block, null, 4f);
            var m = new BrushMask(2f);
            bb.Attach(m);
            Assert.Null(m.Limit);
            m.Stamp(new float2(-50, -50), 10f, false);
            Assert.False(m.IsEmpty);
        }

        [Fact]
        public void Outline_HugsTheCurbExactly()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            m.Stamp(new float2(60, 15), 35f, false);           // reaches over the bottom curb (z = 0)
            m.Stroke(new float2(60, 15), new float2(120, 15), 35f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            AssertInsideBlock(o.Ring, Block);
            Assert.True(o.TouchesCurb);
            // The bottom edge lies exactly on the curb line over (nearly) the whole painted width.
            var onCurb = o.Ring.Where(p => math.abs(p.y) < 0.01f).Select(p => p.x).ToList();
            Assert.True(onCurb.Count >= 2, G.Dump(o.Ring));
            Assert.True(onCurb.Max() - onCurb.Min() > 100f, $"curb frontage only {onCurb.Max() - onCurb.Min():F1} m: {G.Dump(o.Ring)}");
            Assert.True(SurfaceAcceptance.IsAccepted(o.Ring));
        }

        [Fact]
        public void Outline_SmallGapToTheCurbIsClosed()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            // Brush edge stops ~3 m short of the left curb (x = 0): close enough to mean "up to the road".
            m.Stamp(new float2(41, 60), 40f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.True(o.Ring.Count(p => math.abs(p.x) < 0.01f) >= 2, G.Dump(o.Ring));
        }

        [Fact]
        public void Outline_FreeEdgesKeepTheBrushSize()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 30f, false);         // well away from every curb
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            Assert.False(o.TouchesCurb);
            float circle = math.PI * 30f * 30f;
            Assert.InRange(Poly.Area(o.Ring), circle * 0.9f, circle * 1.08f);
        }

        [Fact]
        public void ZonedCells_AreAvoidedAndTheEdgeSnapsToThem()
        {
            // Zone grid along the bottom road: 25 cells of 8 m, 6 deep (48 m), as single-cell obstacles.
            var cells = new List<float2[]>();
            for (int i = 0; i < 25; i++)
                for (int d = 0; d < 6; d++)
                    cells.Add(Rect(i * 8, d * 8, i * 8 + 8, d * 8 + 8));
            var bb = new BlockBrush(Block, cells);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 45f, false);         // overlaps the zoned strip below z = 48
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            var zone = Rect(0, 0, 200, 48);
            Assert.True(OverlapArea(o.Ring, zone) < 0.5f, $"overlaps zoning by {OverlapArea(o.Ring, zone):F1} m²");
            // The cut follows the zone line exactly.
            Assert.True(o.Ring.Count(p => math.abs(p.y - 48f) < 0.01f) >= 2, G.Dump(o.Ring));
            Assert.True(SurfaceAcceptance.IsAccepted(o.Ring));
        }

        [Fact]
        public void EnclosedObstacle_IsCutOutWithoutAHole()
        {
            var building = Rect(90, 50, 110, 70);
            var bb = new BlockBrush(Block, new[] { building });
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 40f, false);
            Assert.False(m.IsPaintedAt(new float2(100, 60)));
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.True(o.OpenedAroundObstacle);
            G.AssertSimpleCcw(o.Ring);
            Assert.True(OverlapArea(o.Ring, building) < 0.5f);
            float expected = math.PI * 40f * 40f - 400f;
            Assert.InRange(Poly.Area(o.Ring), expected * 0.8f, expected * 1.08f);
        }

        [Fact]
        public void PaintedGaps_AreFilledLikePaintMode()
        {
            var bb = new BlockBrush(Block);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 45f, false);
            m.Stamp(new float2(100, 60), 10f, true);           // an erased spot inside: not an obstacle
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.False(o.OpenedAroundObstacle);
            Assert.True(Poly.Contains(o.Ring, new float2(100, 60)));
        }

        [Fact]
        public void ObstacleSplit_KeepsTheLargestPiece()
        {
            // A lot cuts the block from curb to curb; paint on both sides.
            var lot = Rect(95, -5, 105, 125);
            var bb = new BlockBrush(Block, new[] { lot });
            var m = Paint(bb);
            m.Stamp(new float2(60, 60), 30f, false);
            m.Stroke(new float2(60, 60), new float2(140, 60), 30f, false);
            m.Stamp(new float2(150, 60), 40f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            Assert.True(OverlapArea(o.Ring, lot) < 0.5f);
            Assert.True(o.Ring.All(p => p.x >= 104.9f) || o.Ring.All(p => p.x <= 95.1f));
        }

        [Fact]
        public void NarrowUnpaintedStripsFarAway_AreNotSweptIn()
        {
            // Two lots with a 4 m alley between them, far from the paint.
            var a = Rect(150, 10, 170, 110);
            var b = Rect(174, 10, 194, 110);
            var bb = new BlockBrush(Block, new[] { a, b });
            var m = Paint(bb);
            m.Stamp(new float2(50, 60), 30f, false);
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.True(o.Ring.All(p => p.x < 100f));
        }

        [Fact]
        public void BlockedPoints_AreNotPainted()
        {
            var bb = new BlockBrush(Block, null, BrushMask.DefaultCellSize, p => p.x > 150f);   // water on the right
            var m = Paint(bb);
            m.Stamp(new float2(150, 60), 50f, false);
            Assert.False(m.IsPaintedAt(new float2(170, 60)));
            Assert.True(m.IsPaintedAt(new float2(130, 60)));
        }

        [Fact]
        public void FullyBlockedBlock_HasNothingToPaint()
        {
            var bb = new BlockBrush(Block, new[] { Rect(-10, -10, 210, 130) });
            Assert.True(bb.Full);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 80f, false);
            Assert.True(m.IsEmpty);
            Assert.Null(bb.Outline(m));
        }

        [Fact]
        public void BadInput_DoesNotThrow()
        {
            var bb = new BlockBrush(new[] { new float2(0, 0), new float2(1, 0) }, new[] { new[] { new float2(float.NaN, 0), new float2(1, 1), new float2(0, 1) } });
            Assert.True(bb.Full);
            Assert.Null(bb.Outline(new BrushMask()));
            Assert.Null(bb.Outline(null));
        }

        [Fact]
        public void IrregularBlock_WithManyObstacles_StaysSimpleAndFast()
        {
            // Curved-ish block (octagon-like, 300 m across) with a ring of zoned cells along every side.
            var block = new List<float2>();
            for (int i = 0; i < 24; i++)
            {
                float a = i * 2f * math.PI / 24f;
                block.Add(new float2(150 + 150 * math.cos(a), 150 + 120 * math.sin(a)));
            }
            var obstacles = new List<float2[]>();
            for (int i = 0; i < 24; i++)
            {
                float2 p = block[i], q = block[(i + 1) % 24];
                float2 dir = math.normalize(q - p), inward = new float2(-dir.y, dir.x);
                int n = (int)(math.distance(p, q) / 8f);
                for (int k = 0; k < n; k++)
                {
                    float2 s = p + dir * (k * 8f);
                    obstacles.Add(new[] { s, s + dir * 8f, s + dir * 8f + inward * 16f, s + inward * 16f });
                }
            }
            var sw = Stopwatch.StartNew();
            var bb = new BlockBrush(block, obstacles);
            var m = Paint(bb);
            m.Stamp(new float2(150, 150), 60f, false);
            m.Stroke(new float2(150, 150), new float2(260, 160), 60f, false);
            var o = bb.Outline(m);
            sw.Stop();
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            AssertInsideBlock(o.Ring, block.ToArray());
            foreach (var ob in obstacles) Assert.True(OverlapArea(o.Ring, ob) < 0.5f);
            Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds} ms");

            // Re-outlining (what happens every few frames while painting) reuses the cached clip regions.
            sw.Restart();
            m.Stroke(new float2(260, 160), new float2(200, 220), 40f, false);
            Assert.NotNull(bb.Outline(m));
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 1500, $"re-outline took {sw.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void Trim_RemovesPaintOutsideANewFence()
        {
            var m = new BrushMask();
            m.Stamp(new float2(100, 60), 80f, false);         // unfenced paint over the whole block and beyond
            var bb = new BlockBrush(Block, new[] { Rect(0, 0, 200, 40) });
            Assert.True(bb.Trim(m) > 0);
            foreach (var (a, b) in m.FillRuns(1))
            {
                float2 c = (a + b) * 0.5f;
                Assert.True(bb.CanPaintAt(c), $"cell at {c} not paintable");
            }
        }

        // ───────────── open-land fallback (no closed block) ─────────────

        private static Placemaker.Core.NearbyNet Road(float2 a, float2 b, float width = 12f) => new Placemaker.Core.NearbyNet
        {
            A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, 0, math.lerp(a, b, 1f / 3f).y),
            C = new float3(math.lerp(a, b, 2f / 3f).x, 0, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, 0, b.y),
            Width = width, Category = Placemaker.Core.NetCategory.Road,
        };

        private static List<IReadOnlyList<float2>> Footprints(params Placemaker.Core.NearbyNet[] nets) =>
            Placemaker.Core.Tool.AreaClip.Corridors(nets, new float2(-1000), new float2(1000), 0f);

        [Fact]
        public void Open_PaintAlongAStraightRoad_NeverCrossesAndSnapsToItsEdge()
        {
            var road = Footprints(Road(new float2(-400, 0), new float2(400, 0)));   // edges at z = ±6
            var bb = BlockBrush.Open(new float2(-100, 12), road);
            Assert.True(bb.IsOpen);
            Assert.False(bb.Full);
            var m = Paint(bb);
            m.Stamp(new float2(-100, 12), 15f, false);
            m.Stroke(new float2(-100, 12), new float2(100, 12), 15f, false);   // brush reaches z = -3
            Assert.False(m.IsPaintedAt(new float2(0, 0)));
            Assert.False(m.IsPaintedAt(new float2(0, -2)));
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            Assert.True(o.Ring.All(p => p.y >= 6f - 0.05f), G.Dump(o.Ring));
            foreach (var c in road) Assert.True(OverlapArea(o.Ring, c.ToArray()) < 0.5f);
            var onEdge = o.Ring.Where(p => math.abs(p.y - 6f) < 0.05f).Select(p => p.x).ToList();
            Assert.True(onEdge.Count >= 2 && onEdge.Max() - onEdge.Min() > 150f, G.Dump(o.Ring));
            Assert.True(SurfaceAcceptance.IsAccepted(o.Ring));
        }

        [Fact]
        public void Open_StrokeDraggedAcrossARoad_StaysOnItsStartingSide()
        {
            var road = Footprints(Road(new float2(-400, 0), new float2(400, 0)));
            var bb = BlockBrush.Open(new float2(0, 40), road);
            var m = Paint(bb);
            m.Stamp(new float2(0, 40), 10f, false);
            m.Stroke(new float2(0, 40), new float2(0, -40), 10f, false);
            m.Stroke(new float2(0, -40), new float2(60, -40), 10f, false);
            Assert.False(m.IsPaintedAt(new float2(0, -40)));
            Assert.False(m.IsPaintedAt(new float2(60, -40)));
            Assert.True(m.IsPaintedAt(new float2(0, 30)));
            var o = bb.Outline(m, 50f);
            Assert.NotNull(o);
            Assert.True(o.Ring.All(p => p.y > 5.9f), G.Dump(o.Ring));
        }

        [Fact]
        public void Open_DeadEndRoad_PaintWrapsAroundItsEnd()
        {
            var road = Footprints(Road(new float2(0, -400), new float2(0, 100)));   // dead end at z = 100
            var bb = BlockBrush.Open(new float2(25, 50), road);
            var m = Paint(bb);
            m.Stamp(new float2(25, 50), 15f, false);
            m.Stroke(new float2(25, 50), new float2(0, 135), 15f, false);
            m.Stroke(new float2(0, 135), new float2(-25, 50), 15f, false);
            Assert.True(m.IsPaintedAt(new float2(-25, 50)));        // reached around the end: same side
            Assert.False(m.IsPaintedAt(new float2(0, 90)));         // never on the road
            var o = bb.Outline(m);
            Assert.NotNull(o);
            G.AssertSimpleCcw(o.Ring);
            foreach (var c in road) Assert.True(OverlapArea(o.Ring, c.ToArray()) < 0.5f, $"overlaps road by {OverlapArea(o.Ring, c.ToArray()):F1} m²");
            Assert.Contains(o.Ring, p => p.x < -10f);
            Assert.Contains(o.Ring, p => p.x > 10f);
        }

        [Fact]
        public void Open_StartingOnTheRoad_HasNothingToPaint()
        {
            var bb = BlockBrush.Open(new float2(0, 0), Footprints(Road(new float2(-400, 0), new float2(400, 0))));
            Assert.True(bb.Full);
        }

        [Fact]
        public void Open_WithoutRoads_AvoidsTheSameObstaclesAsABlock()
        {
            var lot = Rect(-10, -10, 10, 10);
            var bb = BlockBrush.Open(new float2(-40, 0), new[] { lot }, BrushMask.DefaultCellSize, p => p.x > 60f);
            var m = Paint(bb);
            m.Stamp(new float2(0, 0), 50f, false);
            Assert.False(m.IsPaintedAt(new float2(0, 0)));
            Assert.False(m.IsPaintedAt(new float2(70, 0)));
            var o = bb.Outline(m);
            Assert.NotNull(o);
            Assert.True(OverlapArea(o.Ring, lot) < 0.5f);
        }

        // ───────────── brush rules (player toggles) ─────────────

        private static List<BrushObstacle> Kinded()
        {
            return new List<BrushObstacle>
            {
                new BrushObstacle(BrushObstacleKind.Building, Rect(20, 20, 40, 40)),
                new BrushObstacle(BrushObstacleKind.Lot, Rect(60, 20, 80, 40)),
                new BrushObstacle(BrushObstacleKind.ZonedCell, Rect(100, 20, 120, 40)),
                new BrushObstacle(BrushObstacleKind.EmptyZoneCell, Rect(140, 20, 160, 40)),
            };
        }

        private static readonly (BrushObstacleKind kind, float2 probe)[] Probes =
        {
            (BrushObstacleKind.Building, new float2(30, 30)), (BrushObstacleKind.Lot, new float2(70, 30)),
            (BrushObstacleKind.ZonedCell, new float2(110, 30)), (BrushObstacleKind.EmptyZoneCell, new float2(150, 30)),
        };

        private static BrushMask PaintStrip(BlockBrush bb)
        {
            var m = Paint(bb);
            m.Stamp(new float2(10, 30), 20f, false);
            m.Stroke(new float2(10, 30), new float2(185, 30), 20f, false);
            return m;
        }

        [Fact]
        public void Rules_EachObstacleToggleDecidesWhetherItIsPainted()
        {
            foreach (var (kind, probe) in Probes)
            {
                foreach (bool on in new[] { true, false })
                {
                    var rules = new BrushRules { Buildings = false, Lots = false, Zoned = false, EmptyZones = false, Water = false };
                    switch (kind)
                    {
                        case BrushObstacleKind.Building: rules.Buildings = on; break;
                        case BrushObstacleKind.Lot: rules.Lots = on; break;
                        case BrushObstacleKind.ZonedCell: rules.Zoned = on; break;
                        case BrushObstacleKind.EmptyZoneCell: rules.EmptyZones = on; break;
                    }
                    var bb = rules.Build(new float2(10, 30), Block, Kinded());
                    var m = PaintStrip(bb);
                    Assert.Equal(!on, m.IsPaintedAt(probe));
                    foreach (var (other, p2) in Probes) if (other != kind) Assert.True(m.IsPaintedAt(p2), $"{other} blocked while only {kind} is on");
                    var o = bb.Outline(m);
                    Assert.NotNull(o);
                    Assert.Equal(!on, Poly.Contains(o.Ring, probe));
                }
            }
        }

        [Fact]
        public void Rules_DefaultsStopAtEverythingButEmptyZoneCells()
        {
            var bb = BrushRules.Defaults().Build(new float2(10, 30), Block, Kinded());
            var m = PaintStrip(bb);
            Assert.False(m.IsPaintedAt(new float2(30, 30)));
            Assert.False(m.IsPaintedAt(new float2(70, 30)));
            Assert.False(m.IsPaintedAt(new float2(110, 30)));
            Assert.True(m.IsPaintedAt(new float2(150, 30)));
        }

        [Fact]
        public void Rules_WaterToggle()
        {
            Func<float2, bool> wet = p => p.x > 150f;
            foreach (bool on in new[] { true, false })
            {
                var bb = new BrushRules { Water = on }.Build(new float2(100, 60), Block, null, wet);
                var m = Paint(bb);
                m.Stamp(new float2(150, 60), 40f, false);
                Assert.Equal(!on, m.IsPaintedAt(new float2(170, 60)));
            }
        }

        [Fact]
        public void Rules_RoadsToggle_InOpenLand()
        {
            var road = Footprints(Road(new float2(-400, 0), new float2(400, 0)))
                .Select(r => new BrushObstacle(BrushObstacleKind.Road, r.ToArray())).ToList();
            foreach (bool on in new[] { true, false })
            {
                var bb = new BrushRules { Roads = on }.Build(new float2(0, 40), null, road);
                var m = Paint(bb);
                m.Stamp(new float2(0, 40), 10f, false);
                m.Stroke(new float2(0, 40), new float2(0, -40), 10f, false);
                Assert.Equal(!on, m.IsPaintedAt(new float2(0, 0)));
                Assert.Equal(!on, m.IsPaintedAt(new float2(0, -40)));
            }
        }

        [Fact]
        public void Rules_StayInBlockOff_LetsTheBrushLeaveTheBlock()
        {
            var on = new BrushRules().Build(new float2(100, 60), Block, null);
            var mOn = Paint(on);
            mOn.Stamp(new float2(190, 60), 30f, false);
            Assert.False(mOn.IsPaintedAt(new float2(210, 60)));

            // Lock off and roads off (no road footprints given anyway): only obstacles limit it.
            var off = new BrushRules { StayInBlock = false, Roads = false }.Build(new float2(100, 60), Block, Kinded());
            Assert.False(off.Unconstrained);
            var mOff = Paint(off);
            mOff.Stamp(new float2(190, 60), 30f, false);
            Assert.True(mOff.IsPaintedAt(new float2(210, 60)));
            mOff.Stamp(new float2(30, 30), 5f, false);
            Assert.False(mOff.IsPaintedAt(new float2(30, 30)));   // the building still stops it
        }

        [Fact]
        public void Rules_SnapZero_LeavesTheGapToTheCurb()
        {
            foreach (float snap in new[] { 0f, 6f })
            {
                var bb = new BrushRules { Snap = snap }.Build(new float2(43, 60), Block, null);
                var m = Paint(bb);
                m.Stamp(new float2(41, 60), 40f, false);
                var o = bb.Outline(m);
                Assert.NotNull(o);
                bool onCurb = o.Ring.Count(p => math.abs(p.x) < 0.01f) >= 2;
                Assert.Equal(snap > 0f, onCurb);
            }
        }

        [Fact]
        public void Rules_SmoothingChangesTheEdge()
        {
            var sharp = new BrushRules { Smoothing = 0 }.Build(new float2(100, 60), Block, null);
            var soft = new BrushRules { Smoothing = 6 }.Build(new float2(100, 60), Block, null);
            var m1 = Paint(sharp); m1.Stamp(new float2(100, 60), 25f, false);
            var m2 = Paint(soft); m2.Stamp(new float2(100, 60), 25f, false);
            Assert.True(Poly.Area(sharp.Outline(m1).Ring) > Poly.Area(soft.Outline(m2).Ring) + 20f);
        }

        [Fact]
        public void Rules_AllOff_IsPlainPaint()
        {
            var rules = new BrushRules { Roads = false, Paths = false, Bikes = false, Tracks = false, Buildings = false, Builds = false, Lots = false, Zoned = false, AllZoned = false, EmptyZones = false, Water = false, StayInBlock = false, Smoothing = 6 };
            Assert.True(rules.AllOff);
            var road = Footprints(Road(new float2(-400, 0), new float2(400, 0))).Select(r => new BrushObstacle(BrushObstacleKind.Road, r.ToArray()));
            var bb = rules.Build(new float2(10, 30), Block, Kinded().Concat(road), p => true);
            Assert.True(bb.Unconstrained);
            Assert.False(bb.Full);

            var fenced = PaintStrip(bb);
            fenced.Stroke(new float2(185, 30), new float2(185, -500), 20f, false);   // across the road and out of any box
            var plain = new BrushMask();
            plain.Stamp(new float2(10, 30), 20f, false);
            plain.Stroke(new float2(10, 30), new float2(185, 30), 20f, false);
            plain.Stroke(new float2(185, 30), new float2(185, -500), 20f, false);
            Assert.Equal(plain.PaintedCellCount, fenced.PaintedCellCount);

            var expected = Poly.To2D(plain.Outline());
            var got = bb.Outline(fenced).Ring;
            Assert.Equal(expected.Length, got.Length);
            for (int i = 0; i < got.Length; i++) Assert.True(math.distance(expected[i], got[i]) < 1e-4f);
        }

        [Fact]
        public void Rules_JsonFromThePanel_MergesAndClamps()
        {
            var cur = BrushRules.Defaults();
            var r = Placemaker.Core.Tool.UiModels.ParseBrushRules("{\"roads\":false,\"snap\":99,\"smoothing\":-3}", cur);
            Assert.False(r.Roads);
            Assert.True(r.Buildings);
            Assert.Equal(BrushRules.MaxSnap, r.Snap);
            Assert.Equal(0, r.Smoothing);
            Assert.Equal(cur, Placemaker.Core.Tool.UiModels.ParseBrushRules("not json", cur));
            var json = Placemaker.Core.Tool.UiModels.BrushRulesJson(r).ToString();
            Assert.Equal(r, Placemaker.Core.Tool.UiModels.ParseBrushRules(json, BrushRules.Defaults()));
        }

        // ───────────── s27: other Placemaker builds, "Paint over anything", rules for Paint / Draw ─────────────

        private static readonly float2[] OtherBuild = Rect(120, 0, 200, 120);   // a park built earlier, right part of the block

        [Fact]
        public void Rules_OtherBuildsStopTheBlockPaintBrush()
        {
            var obstacles = new[] { new BrushObstacle(BrushObstacleKind.PlacemakerBuild, OtherBuild) };
            foreach (bool on in new[] { true, false })
            {
                var bb = new BrushRules { Builds = on }.Build(new float2(60, 60), Block, obstacles);
                var m = Paint(bb);
                m.Stamp(new float2(100, 60), 60f, false);
                Assert.Equal(!on, m.IsPaintedAt(new float2(150, 60)));
                var o = bb.Outline(m);
                Assert.NotNull(o);
                Assert.Equal(!on, Poly.Contains(o.Ring, new float2(150, 60)));
                if (on)
                {
                    Assert.True(OverlapArea(o.Ring, OtherBuild) < 0.5f);
                    // The edge snaps onto the other build's outline (x = 120), like onto a curb.
                    Assert.True(o.Ring.Count(p => math.abs(p.x - 120f) < 0.01f) >= 2, G.Dump(o.Ring));
                }
            }
        }

        [Fact]
        public void Rules_OtherBuildsAreIndependentOfLots()
        {
            var obstacles = new[] { new BrushObstacle(BrushObstacleKind.PlacemakerBuild, OtherBuild), new BrushObstacle(BrushObstacleKind.Lot, Rect(20, 20, 40, 40)) };
            var bb = new BrushRules { Lots = false }.Build(new float2(60, 60), Block, obstacles);
            var m = Paint(bb);
            m.Stamp(new float2(100, 60), 90f, false);
            Assert.True(m.IsPaintedAt(new float2(30, 30)));
            Assert.False(m.IsPaintedAt(new float2(150, 60)));
            Assert.True(BrushRules.Defaults().Builds);
            Assert.True(BrushRules.Defaults().Stops(BrushObstacleKind.PlacemakerBuild));
        }

        [Fact]
        public void Rules_PaintOverAnything_TurnsEveryStopOff()
        {
            var r = new BrushRules { Anything = true };   // every individual toggle still at its default (on)
            foreach (BrushObstacleKind k in Enum.GetValues(typeof(BrushObstacleKind))) Assert.False(r.Stops(k));
            Assert.False(r.WaterOn);
            Assert.False(r.LockOn);
            Assert.True(r.AllOff);

            var obstacles = Kinded().Concat(new[] { new BrushObstacle(BrushObstacleKind.PlacemakerBuild, OtherBuild) });
            var bb = r.Build(new float2(10, 30), Block, obstacles, p => true);
            Assert.True(bb.Unconstrained);
            var m = Paint(bb);
            m.Stamp(new float2(150, 60), 30f, false);
            m.Stamp(new float2(250, 60), 30f, false);   // outside the block too
            Assert.True(m.IsPaintedAt(new float2(150, 60)));
            Assert.True(m.IsPaintedAt(new float2(250, 60)));

            var ring = Rect(0, 0, 200, 120);
            Assert.Same(ring, r.Cut(ring, obstacles, out int touched));
            Assert.Equal(0, touched);
        }

        [Fact]
        public void Rules_Cut_PlainPaintKeepsOffWhatTheRulesName()
        {
            var painted = Rect(0, 0, 200, 120);
            var obstacles = new[]
            {
                new BrushObstacle(BrushObstacleKind.PlacemakerBuild, OtherBuild),
                new BrushObstacle(BrushObstacleKind.ZonedCell, Rect(0, 0, 40, 16)),
            };
            var cut = BrushRules.Defaults().Cut(painted, obstacles, out int touched);
            Assert.NotNull(cut);
            Assert.Equal(2, touched);
            G.AssertSimpleCcw(cut);
            Assert.True(OverlapArea(cut, OtherBuild) < 0.5f);
            Assert.True(OverlapArea(cut, Rect(0, 0, 40, 16)) < 0.5f);

            var noBuilds = new BrushRules { Builds = false }.Cut(painted, obstacles, out _);
            Assert.True(OverlapArea(noBuilds, OtherBuild) > 9000f);
            Assert.True(OverlapArea(noBuilds, Rect(0, 0, 40, 16)) < 0.5f);
        }

        [Fact]
        public void Rules_Cut_DrawDefaultOnlyKeepsOffOtherBuilds()
        {
            var drawn = Rect(0, 0, 200, 120);
            var obstacles = new[]
            {
                new BrushObstacle(BrushObstacleKind.PlacemakerBuild, OtherBuild),
                new BrushObstacle(BrushObstacleKind.ZonedCell, Rect(0, 0, 40, 16)),
            };
            // What the tool passes for Draw / Roads / Block without "clip to free land".
            Func<BrushObstacleKind, bool> buildsOnly = k => k == BrushObstacleKind.PlacemakerBuild;
            var cut = BrushRules.Defaults().Cut(drawn, obstacles, out _, 0.5f, 100f, buildsOnly);
            Assert.True(OverlapArea(cut, OtherBuild) < 0.5f);
            Assert.True(OverlapArea(cut, Rect(0, 0, 40, 16)) > 600f);

            // Entirely on another build: nothing left, touched says why.
            Assert.Null(BrushRules.Defaults().Cut(Rect(130, 10, 190, 110), obstacles, out int touched, 0.5f, 100f, buildsOnly));
            Assert.True(touched > 0);
        }

        [Fact]
        public void Rules_JsonCarriesTheS27Fields()
        {
            var r = Placemaker.Core.Tool.UiModels.ParseBrushRules("{\"builds\":false,\"anything\":true,\"drawClip\":true}", BrushRules.Defaults());
            Assert.False(r.Builds);
            Assert.True(r.Anything);
            Assert.True(r.DrawClip);
            var back = Placemaker.Core.Tool.UiModels.ParseBrushRules(Placemaker.Core.Tool.UiModels.BrushRulesJson(r).ToString(), BrushRules.Defaults());
            Assert.Equal(r, back);
            Assert.NotEqual(BrushRules.Defaults(), back);
        }

        [Fact]
        public void BlockPaintMode_RoundTripsAndComparesFaces()
        {
            Assert.Equal("blockPaint", Placemaker.Core.Tool.SelectionModes.Id(Placemaker.Core.Tool.SelectionMode.BlockPaint));
            Assert.True(Placemaker.Core.Tool.SelectionModes.TryParse("blockPaint", out var mode));
            Assert.Equal(Placemaker.Core.Tool.SelectionMode.BlockPaint, mode);
            Assert.Equal(Placemaker.Core.AreaSource.Painted, Placemaker.Core.Tool.SelectionModes.Source(mode));

            var a = new List<Placemaker.Core.Tool.HalfEdge> { new(1, 10, 11), new(2, 11, 12), new(3, 12, 10) };
            var rotated = new List<Placemaker.Core.Tool.HalfEdge> { a[1], a[2], a[0] };
            var other = new List<Placemaker.Core.Tool.HalfEdge> { a[0].Reversed, a[1], a[2] };
            Assert.True(Placemaker.Core.Tool.BlockPaintState.SameFace(a, rotated));
            Assert.False(Placemaker.Core.Tool.BlockPaintState.SameFace(a, other));
            Assert.False(Placemaker.Core.Tool.BlockPaintState.SameFace(a, new List<Placemaker.Core.Tool.HalfEdge>()));
        }
    }
}
