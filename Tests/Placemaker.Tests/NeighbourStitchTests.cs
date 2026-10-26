using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class NeighbourStitchTests
    {
        // A 2x6 house (16 x 48 m lot) facing `forward`, with a fence on its right edge, a hedge on its left edge, a back
        // fence and a lawn covering the back half of the lot (as NA_ResidentialLow01 grows them).
        private static StitchHouse House(long id, float2 pos, float2 forward, string right = "FenceResidentialHigh01", string left = "HedgeLow01")
        {
            var h = new StitchHouse { Id = id, Position = pos, Forward = math.normalize(forward), HalfLot = new float2(8f, 24f) };
            h.Lines.Add(new StitchLine { Id = id * 10 + 1, Name = right, A = h.ToWorld(new float2(7.9f, 23.9f)), B = h.ToWorld(new float2(7.9f, -23.9f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 2, Name = left, A = h.ToWorld(new float2(-7.3f, 23.9f)), B = h.ToWorld(new float2(-7.3f, -12.8f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 3, Name = right, A = h.ToWorld(new float2(7.9f, -23.9f)), B = h.ToWorld(new float2(-7.9f, -23.9f)) });
            h.Lawns.Add(new StitchLawn { Id = id * 10 + 5, Ring = new[] { h.ToWorld(new float2(-7f, -23f)), h.ToWorld(new float2(7.4f, -23f)), h.ToWorld(new float2(7.4f, 0f)), h.ToWorld(new float2(-7f, 0f)) } });
            return h;
        }

        [Fact]
        public void Straight_street_neighbours_keep_one_border_line_and_move_nothing()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(16f, 0f), new float2(0f, 1f));
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            // a's right fence and b's left hedge share the edge: the hedge stays, the fence goes
            Assert.Contains(11L, plan.HideLines);
            Assert.DoesNotContain(22L, plan.HideLines);
            Assert.Equal(22L, plan.Pairs[0].KeptLine);
            Assert.Empty(plan.MoveLines);
            Assert.Empty(plan.Lawns);
        }

        [Fact]
        public void Curve_wedge_gets_one_line_on_the_bisector_back_fences_meet_it_and_lawns_grow_to_it()
        {
            // outside of a curve: houses face the road's centre, backs fan out (front edge on radius 40, back on 88)
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;   // facing the centre, a's right is counter-clockwise
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            Assert.True(math.dot(b.Position - a.Position, a.Right) > 0f);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            var pair = plan.Pairs[0];
            Assert.True(pair.BackGap > pair.FrontGap + 15f, $"a wedge: back gap {pair.BackGap} vs front {pair.FrontGap}");
            // the hedge (b's left) is kept and laid on the bisector; a's right fence goes
            Assert.Equal(22L, pair.KeptLine);
            Assert.Contains(11L, plan.HideLines);
            var kept = plan.MoveLines[22L];
            Assert.True(math.distance(kept.A, pair.Front) < 0.2f);
            // b's hedge stopped at z -12.8 of its 24 m half depth: it ends part way down the border, on it
            Assert.True(DistanceToSegment(kept.B, pair.Front, pair.Back) < 0.05f);
            // a's back fence now ends at the border's back point
            var back = plan.MoveLines[13L];
            Assert.True(math.distance(back.A, pair.Back) < 0.01f);
            // both lawns reach the shared side, so both grew into their half of the wedge
            Assert.True(plan.Lawns.ContainsKey(15L) && plan.Lawns.ContainsKey(25L), string.Join(" | ", plan.Notes) + " || lawns " + string.Join(",", plan.Lawns.Keys) + " new " + plan.NewLawns.Count + " beds " + plan.Beds.Count);
            Assert.True(plan.LawnAddedM2 > 50f, $"added {plan.LawnAddedM2} m²");
            Assert.True(plan.Lawns.Values.All(r => SurfaceAcceptance.IsAccepted(r)));
            // no lawn crosses the border: a's grown lawn stays on a's side of it
            float sideOfA = Side(pair.Front, pair.Back, a.Position);
            foreach (var p in plan.Lawns[15L]) Assert.True(Side(pair.Front, pair.Back, p) * math.sign(sideOfA) > -0.5f, "a's lawn crosses the border");
        }

        [Fact]
        public void Inside_of_a_curve_wedges_open_to_the_street_and_are_stitched_too()
        {
            // inside of a curve: houses face away from the centre, fronts on radius 82, backs on 34: the gap is widest at the street
            float2 At(float angle) => 58f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 - 0.42f;   // facing outward, a's right is clockwise
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            Assert.True(math.dot(b.Position - a.Position, a.Right) > 0f);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            var pair = plan.Pairs[0];
            Assert.True(pair.FrontGap > pair.BackGap, $"front {pair.FrontGap} back {pair.BackGap}");
            Assert.Equal(22L, pair.KeptLine);
            Assert.Contains(11L, plan.HideLines);
            Assert.True(plan.MoveLines.ContainsKey(22L));
            // the lawns sit at the back, where the lots almost meet: whatever they gain stays on their own side
            foreach (var r in plan.Lawns.Values) Assert.True(SurfaceAcceptance.IsAccepted(r));
        }

        [Fact]
        public void A_shallow_house_beside_a_deep_one_is_its_neighbour()
        {
            // both lots start at the street (z = 24 front edge); b is only 16 m deep, so its centre sits 16 m nearer the street
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = new StitchHouse { Id = 2, Position = new float2(20f, 16f), Forward = new float2(0f, 1f), HalfLot = new float2(8f, 8f) };
            b.Lines.Add(new StitchLine { Id = 21, Name = "HedgeHigh01", A = b.ToWorld(new float2(-7.9f, 7.9f)), B = b.ToWorld(new float2(-7.9f, -7.9f)) });
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Single(plan.Pairs);
            Assert.Equal(21L, plan.Pairs[0].KeptLine);
            // a's fence leaves the shared side: b's garden (made as deep as a's) had no back line, so the freed fence closes it
            Assert.DoesNotContain(11L, plan.HideLines);
            var reused = plan.MoveLines[11L];
            Assert.True(math.abs(b.ToLocal(reused.A).y - b.ToLocal(reused.B).y) < 0.05f && b.ToLocal(reused.A).y < -b.HalfLot.y, $"a's fence at {reused.A} {reused.B}");
        }

        [Fact]
        public void A_lawn_fills_its_plot_up_to_the_shared_fence_and_never_covers_the_neighbours_driveway()
        {
            // a wedge (as on the outside of a curve); b's lawn sits on its far side, and b's driveway runs along the shared side
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            b.Lawns[0].Ring = new[] { b.ToWorld(new float2(2f, -23f)), b.ToWorld(new float2(7.4f, -23f)), b.ToWorld(new float2(7.4f, 0f)), b.ToWorld(new float2(2f, 0f)) };
            var drive = new[] { b.ToWorld(new float2(-8f, 24f)), b.ToWorld(new float2(-4f, 24f)), b.ToWorld(new float2(-4f, -6f)), b.ToWorld(new float2(-8f, -6f)) };
            b.Hard.Add(drive);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.True(plan.Lawns.ContainsKey(15L), "a's lawn grows");
            // b's lawn fills its own plot too, around its driveway
            Assert.True(plan.Lawns.ContainsKey(25L));
            Assert.False(Poly.Contains(plan.Lawns[25L], b.ToWorld(new float2(-6f, -2f))), "b's lawn covers its driveway");
            float sideOfB = Side(plan.Pairs[0].Front, plan.Pairs[0].Back, b.Position);
            foreach (var p in plan.Lawns[25L]) Assert.True(Side(plan.Pairs[0].Front, plan.Pairs[0].Back, p) * math.sign(sideOfB) > -0.5f, "b's lawn crosses the shared fence");
            var grown = plan.Lawns[15L];
            var pair = plan.Pairs[0];
            // it reaches the shared fence half way down the wedge…
            var onBorder = math.lerp(pair.Front, pair.Back, 0.75f);
            var aEdge = a.ToWorld(new float2(8f, -12f));
            Assert.True(Poly.Contains(grown, math.lerp(aEdge, onBorder, 0.9f)), "a's lawn should reach the shared fence");
            // …never past it, and never over b's driveway
            float sideOfA = Side(pair.Front, pair.Back, a.Position);
            foreach (var p in grown) Assert.True(Side(pair.Front, pair.Back, p) * math.sign(sideOfA) > -0.5f, "a's lawn crosses the shared fence");
            Assert.False(Poly.Contains(grown, b.ToWorld(new float2(-6f, -2f))), "a's lawn covers b's driveway");
            Assert.True(SurfaceAcceptance.IsAccepted(grown));
        }

        [Fact]
        public void Extended_gardens_get_beds_along_their_fences_with_bushes_on_them_clear_of_the_house_and_paving()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float c = math.radians(90f);
            var houses = new[] { House(1, At(c + 0.45f), Facing(c + 0.45f)), House(2, At(c), Facing(c)), House(3, At(c - 0.45f), Facing(c - 0.45f)) };
            foreach (var h in houses)
            {
                h.Footprint = new[] { h.ToWorld(new float2(-5f, 2f)), h.ToWorld(new float2(5f, 2f)), h.ToWorld(new float2(5f, 14f)), h.ToWorld(new float2(-5f, 14f)) };
                h.Hard.Add(new[] { h.ToWorld(new float2(-8f, 24f)), h.ToWorld(new float2(-4f, 24f)), h.ToWorld(new float2(-4f, 2f)), h.ToWorld(new float2(-8f, 2f)) });
            }
            var settings = MiddleLine();
            settings.Extra["features"] = 0f;   // beds and their bushes only (features: their own test)
            settings.Extra["rockChance"] = 0f;
            var plan = YardStitch.Plan(houses, settings);
            Assert.NotEmpty(plan.Beds);
            Assert.NotEmpty(plan.Bushes);
            foreach (var (house, ring) in plan.Beds)
            {
                Assert.True(SurfaceAcceptance.IsAccepted(ring));
                var plot = plan.Plots[house];
                foreach (var p in ring) Assert.True(Poly.Contains(plot, p) || DistanceToSegment(p, plot[0], plot[1]) < 0.3f || plot.Select((q, k) => DistanceToSegment(p, q, plot[(k + 1) % plot.Length])).Min() < 0.3f, $"bed of {house} leaves its garden");
                // never on the house or its driveway, never under the lawn
                foreach (var h in houses)
                {
                    Assert.Empty(Poly.Intersection(Ccw(ring), Ccw(h.Footprint)).Where(r => r.Area > 0.05f));
                    Assert.Empty(Poly.Intersection(Ccw(ring), Ccw(h.Hard[0])).Where(r => r.Area > 0.05f));
                }
                foreach (var kv2 in plan.Lawns) { var ov = Poly.Intersection(Ccw(ring), Ccw(kv2.Value)).Sum(r => r.Area); Assert.True(ov < 0.2f, $"bed of {house} overlaps lawn {kv2.Key} by {ov:F2} m², lawns {string.Join(",", plan.Lawns.Keys)}; {string.Join(" | ", plan.Notes)}"); }
            }
            // every bush stands on a bed of its own house, and there are only a few per garden
            foreach (var (house, p, _, kind) in plan.Bushes)
            {
                Assert.Contains(plan.Beds, bed => bed.House == house && Poly.Contains(bed.Ring, p));
                Assert.InRange(kind, 0, YardStitch.BushKinds.Length - 1);
            }
            Assert.True(plan.Bushes.GroupBy(b => b.House).All(g => g.Count() <= 14));
            // the same houses plant the same garden every time
            var again = YardStitch.Plan(houses, settings);
            Assert.Equal(plan.Bushes.Select(b => b.Position), again.Bushes.Select(b => b.Position));
        }

        private static float2[] Ccw(float2[] r) => Poly.IsCcw(r) ? r : r.Reverse().ToArray();

        /// <summary>Gaps shared on the middle line (the stepped split and walkways have their own tests).</summary>
        private static float2[] Strip(float x0, float z0, float x1, float z1) => new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        [Fact]
        public void Back_to_back_across_a_road_never_join_and_each_garden_stops_short_of_the_road()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(0f, -60f), new float2(0f, -1f));
            Assert.Contains(YardStitch.Plan(new[] { a, b }, MiddleLine()).Pairs, p => p.Rear);
            var s = MiddleLine().WithBarriers(new List<float2[]> { Strip(-60f, -34f, 60f, -26f) });
            var plan = YardStitch.Plan(new[] { a, b }, s);
            Assert.DoesNotContain(plan.Pairs, p => p.Rear);
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "a reaches out to the road behind it");
            Assert.True(pa.Min(p => p.y) > -26f + 0.5f && pa.Min(p => p.y) < -24.5f, $"a stops just short of the road: {pa.Min(p => p.y)}");
            Assert.True(plan.Plots.TryGetValue(2, out var pb));
            Assert.True(pb.Max(p => p.y) < -34f - 0.5f, $"b stops just short of the road: {pb.Max(p => p.y)}");
        }

        [Fact]
        public void Side_neighbours_across_a_path_never_join()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(30f, 0f), new float2(0f, 1f));
            Assert.Single(YardStitch.Plan(new[] { a, b }, MiddleLine()).Pairs);
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine().WithBarriers(new List<float2[]> { Strip(12f, -40f, 18f, 40f) }));
            Assert.Empty(plan.Pairs);
            Assert.True(plan.Plots.TryGetValue(1, out var pa) && pa.Max(p => p.x) < 12f - 0.5f, "a's garden stops short of the path");
            Assert.True(plan.Plots.TryGetValue(2, out var pb) && pb.Min(p => p.x) > 18f + 0.5f, "b's garden stops short of the path");
            // a's right fence follows the path
            Assert.True(plan.MoveLines.TryGetValue(11L, out var fence) && math.abs(fence.A.x - 11.2f) < 0.3f && math.abs(fence.B.x - 11.2f) < 0.3f, "a's fence runs along the path");
        }

        [Fact]
        public void A_garden_hugs_a_diagonal_road_near_it_and_never_crosses_it()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            // a road running away at 45 degrees from near a's front right corner
            var road = new[] { new float2(12f, 24f), new float2(18f, 24f), new float2(58f, -16f), new float2(52f, -16f) };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine().WithBarriers(new List<float2[]> { road }));
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "a reaches out to the road");
            Assert.True(pa.Max(p => p.x) > 14f, $"a fills the wedge towards the road: {pa.Max(p => p.x)}");
            foreach (var p in pa) Assert.False(Poly.Contains(road, p), $"a plot corner {p} lies on the road");
            Assert.False(Overlap(pa, road), "the garden overlaps the road");
        }

        [Theory]
        [InlineData(20f)]
        [InlineData(30f)]
        [InlineData(40f)]
        public void Back_gardens_reach_a_street_behind_them_at_an_angle_with_no_grass_triangle_left(float degrees)
        {
            // the user's row (22 Oct): long back gardens down to a second street at an angle. Each garden narrows toward its
            // slanted end and was cut square there as a spike: a grass triangle between every back fence and the sidewalk
            float tan = math.tan(math.radians(degrees));
            var row = new[] { -24f, -8f, 8f, 24f }.Select((x, k) => House(k + 1, new float2(x, 0f), new float2(0f, 1f))).ToArray();
            // the near kerb 6 m behind the left lot's back, falling away to the right; the street 14 m wide
            float2 Kerb(float x) => new float2(x, -30f - (x + 32f) * tan);
            var down = new float2(0f, -14f / math.cos(math.radians(degrees)));
            var road = new[] { Kerb(-80f) + down, Kerb(80f) + down, Kerb(80f), Kerb(-80f) };
            var s = MiddleLine().WithBarriers(new List<float2[]> { road });
            var plan = YardStitch.Plan(row, s);
            foreach (var h in row)
            {
                Assert.True(plan.Plots.TryGetValue(h.Id, out var plot), $"{h.Id} is given a garden");
                Assert.False(Overlap(plot, road), $"{h.Id}'s garden lies on the street");
                foreach (var o in row.Where(o => o.Id > h.Id && plan.Plots.ContainsKey(o.Id)))
                    Assert.False(Overlap(plot, plan.Plots[o.Id]), $"the gardens of {h.Id} and {o.Id} overlap");
            }
            // every bit of the kerb behind the row has a garden right beside it (the street's margin, 0.8 m, nothing more)
            for (float x = -31.5f; x <= 31.5f; x += 0.5f)
            {
                var p = Kerb(x);
                float d = plan.Plots.Values.Min(r => Poly.Contains(r, p) ? 0f : DistanceToRing(r, p));
                Assert.True(d < 1f, $"{degrees}°: the kerb at x {x:F1} is {d:F1} m from the nearest garden ({string.Join(" | ", plan.Notes)})");
            }
            var r = FenceCheck.Of(row, plan, s);
            Assert.True(r.Gaps.Count == 0 && r.Crossings.Count == 0, $"{degrees}°: " + r + ": " + string.Join("; ", r.Gaps.Select(g => $"{g.Kind} {g.Len:F1} at {g.At}")));
            // and the gardens' fences run along the sidewalk, all the way
            var drawn = FenceCheck.Drawn(row, plan);
            for (float x = -30f; x <= 30f; x += 1f)
            {
                var p = Kerb(x);
                Assert.True(drawn.Any(b => DistanceToSegment(p, b.A, b.B) < 1.5f), $"{degrees}°: no fence along the kerb at x {x:F0}");
            }
        }

        [Theory]
        [InlineData(24f)]
        [InlineData(16f)]
        public void Behind_a_walkway_two_gardens_meet_on_one_fence_instead_of_running_side_by_side(float bHalfDepth)
        {
            // the user (22 Oct, the back street): a walkway between two houses, and behind it the two gardens' hedges ran on side
            // by side down to the street, a grass strip between ("who would want two fences running parallel to each other?")
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(21f, 24f - bHalfDepth), new float2(0f, 1f));
            b.HalfLot = new float2(8f, bHalfDepth);
            var s = MiddleLine();
            s.Extra["alleys"] = 1f;
            s.Extra["alleyChance"] = 100f;
            // their street, and a street behind them both: their gardens reach it
            s = s.WithBarriers(new List<float2[]> { Strip(-60f, 24.5f, 80f, 38f), Strip(-60f, -74f, 80f, -60f) });
            var houses = new[] { a, b };
            var plan = YardStitch.Plan(houses, s);
            Assert.Contains(plan.Notes, x => x.Contains("a walkway between them"));
            float end = 24f - 2f * bHalfDepth;   // the walkway ends at the shallower lot's back
            // the walkway beside the houses stays (the strip between the lots, x 8 to 13)
            Assert.DoesNotContain(plan.Plots.Values, r => Poly.Contains(r, new float2(10.5f, end + 4f)));
            // behind it the strip is the two gardens', split down the middle
            Assert.True(Poly.Contains(plan.Plots[1], new float2(9.5f, end - 6f)), "the left half behind the walkway is not 1's");
            Assert.True(Poly.Contains(plan.Plots[2], new float2(11.5f, end - 6f)), "the right half behind the walkway is not 2's");
            var drawn = FenceCheck.Drawn(houses, plan);
            // no fence runs on along either lot's edge behind the walkway: one fence down the middle
            foreach (var bit in drawn)
                foreach (float x in new[] { 8f, 13f })
                    if (math.abs(bit.A.x - x) < 0.8f && math.abs(bit.B.x - x) < 0.8f && math.abs(bit.A.y - bit.B.y) > 0.5f)
                        Assert.True(math.min(bit.A.y, bit.B.y) > end - 1.5f, $"a fence on x {x} runs on behind the walkway: {bit.A} {bit.B} ({bit.Name})");
            for (float y = end - 3f; y > -55f; y -= 1f)
                Assert.True(drawn.Any(bit => DistanceToSegment(new float2(10.5f, y), bit.A, bit.B) < 0.8f), $"no fence on the middle line at y {y:F0}");
            // and one across the walkway's end
            for (float x = 9f; x <= 12f; x += 1f)
                Assert.True(drawn.Any(bit => DistanceToSegment(new float2(x, end), bit.A, bit.B) < 0.8f), $"the walkway's end is open at x {x:F0}");
            var r = FenceCheck.Of(houses, plan, s);
            Assert.True(r.Gaps.Count == 0 && r.Crossings.Count == 0, r + ": " + string.Join("; ", r.Gaps.Select(g => $"{g.Kind} {g.Len:F1} at {g.At}")));
        }

        [Fact]
        public void A_lone_garden_balloons_into_open_land_as_far_as_its_look_lets_it_but_never_onto_a_road()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 60f };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "the garden grows");
            Assert.True(pa.Min(p => p.x) < -8f - 55f && pa.Max(p => p.x) > 8f + 55f && pa.Min(p => p.y) < -24f - 55f, "60 m into the open land on both sides and at the back");
            Assert.True(pa.Max(p => p.y) <= 24.01f, "never past its street edge");
            // a road 20 m to its right: it stops there
            var plan2 = YardStitch.Plan(new[] { a }, MiddleLine().WithBarriers(new List<float2[]> { Strip(28f, -200f, 36f, 200f) }));
            Assert.True(plan2.Plots.TryGetValue(1, out var pb) && pb.Max(p => p.x) < 28f - 0.5f, "stops short of the road");
        }

        [Fact]
        public void A_garden_keeps_out_of_the_crack_under_a_low_deck_and_grows_under_a_high_one_but_no_tree()
        {
            // the user: "going under an elevated road is totally fine. It's just when it crams itself in a little crack, that's
            // bad"; "a huge tree doesn't belong under a highway, even if it's a really tall highway". A deck 20 m to the
            // house's right (x 28 to 40), a ground road beyond it (x 56 to 64) that the garden reaches for
            var deck = Strip(28f, -200f, 40f, 200f);
            var road = Strip(56f, -200f, 64f, 200f);
            StitchPlan Plan(float deckHeight, float fill, float? clearance = null, float2[] pillar = null)
            {
                var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
                a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 60f, ["fill"] = fill };
                if (clearance != null) a.Options["deckClearance"] = clearance.Value;
                var barriers = new List<float2[]> { deck, road };
                var kinds = new List<int> { YardStitch.DeckKind, 0 };
                var heights = new List<float> { deckHeight, 0f };
                if (pillar != null) { barriers.Add(pillar); kinds.Add(YardStitch.DeckKind); heights.Add(0f); }
                return YardStitch.Plan(new[] { a }, MiddleLine().WithBarriers(barriers, kinds, null, heights));
            }
            bool IsTree(int kind) => YardStitch.BushKinds[kind].Contains("Tree");
            const float gap = 0.8f + 1.5f;   // the barrier margin and the deck gap
            Assert.Equal(7f, PolishLooks.Defaults["deckClearance"]);
            foreach (float fill in new[] { PolishLooks.FillGarden, PolishLooks.FillCarPark })
            {
                // 3 m up (a ramp's foot): a crack. The garden reaches the deck's gap and stops; nothing of it under the deck
                var low = Plan(3f, fill);
                Assert.True(low.Plots.TryGetValue(1, out var pa), "the garden grows");
                float reach = pa.Max(p => p.x);
                Assert.True(reach < 28f - gap + 0.05f && reach > 28f - gap - 1f, $"fill {fill}: the garden stops a deck gap short of the crack ({reach:F2})");
                Assert.DoesNotContain(low.Bushes, b => b.Position.x > 28f - gap + 0.05f);
                Assert.DoesNotContain(low.FencePieces, f => f.Position.x > 28f - gap + 0.05f);
                foreach (var ring in low.Lawns.Values.Concat(low.NewLawns.Select(l => l.Ring)).Concat(low.Beds.Select(b => b.Ring)).Concat(low.Paths.Select(p => p.Ring)))
                    Assert.True(ring.Max(p => p.x) < 28f - gap + 0.05f, $"fill {fill}: a surface reaches {ring.Max(p => p.x):F2}");
                // 10 m up (an overpass): room under it. The garden grows under it to the ground road beyond, as if it were not
                // there, but no tree stands under it nor within a crown of it
                var high = Plan(10f, fill);
                Assert.True(high.Plots.TryGetValue(1, out var ph) && ph.Max(p => p.x) > 50f, $"fill {fill}: under a high deck the garden reaches the road beyond ({ph?.Max(p => p.x):F1})");
                Assert.DoesNotContain(high.Bushes, b => IsTree(b.Kind) && b.Position.x > 28f - 3f && b.Position.x < 40f + 3f);
                // the look asks more room: the same deck is a crack
                var strict = Plan(10f, fill, 12f);
                Assert.True(strict.Plots.TryGetValue(1, out var ps) && ps.Max(p => p.x) < 28f - gap + 0.05f, $"fill {fill}: 12 m asked, a 10 m deck is a crack ({ps?.Max(p => p.x):F1})");
            }
            // a car park yard plants trees: some stand where they would without the deck, none by it
            var open = Plan(10f, PolishLooks.FillCarPark, 0f);
            var noDeck = YardStitch.Plan(new[] { WithOptions(House(1, new float2(0f, 0f), new float2(0f, 1f)), PolishLooks.FillCarPark) }, MiddleLine().WithBarriers(new List<float2[]> { road }));
            Assert.Contains(noDeck.Bushes, b => IsTree(b.Kind) && b.Position.x > 25f && b.Position.x < 43f);
            Assert.DoesNotContain(open.Bushes, b => IsTree(b.Kind) && b.Position.x > 25f && b.Position.x < 43f);
            // a pillar under the high deck: nothing grows into it
            var pillar = Strip(33f, -30f, 35f, -28f);
            var withPillar = Plan(10f, PolishLooks.FillGarden, null, pillar);
            Assert.True(withPillar.Plots.TryGetValue(1, out var pp) && !Poly.Contains(pp, new float2(34f, -29f)), "the garden keeps out of the pillar");
            Assert.DoesNotContain(withPillar.FencePieces, f => Poly.Contains(pillar, f.Position));
            Assert.DoesNotContain(withPillar.Bushes, b => Poly.Contains(pillar, b.Position));
        }

        private static StitchHouse WithOptions(StitchHouse h, float fill)
        {
            h.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 60f, ["fill"] = fill };
            return h;
        }

        [Fact]
        public void A_big_garden_is_closed_all_round_with_pieces_of_its_own_fence_kind_but_its_street_front_stays_open()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 40f };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.NotEmpty(plan.FencePieces);
            // nothing runs along the street front (a side's run reaches the street line, as the house's own side fences do)
            Assert.All(plan.FencePieces, p => Assert.True(a.ToLocal(p.Position).y < a.HalfLot.y - 0.9f && (a.ToLocal(p.Position).y < a.HalfLot.y - 2.1f || math.abs(math.dot(new float2(math.sin(p.Yaw), math.cos(p.Yaw)), a.Right)) < 0.5f), $"a piece on the street front at {p.Position}"));
            // its fence kind: the right fence and back fence are FenceResidentialHigh01
            Assert.Contains(plan.FencePieces, p => p.Name == "FenceResidentialPieceHigh01");
            // pieces on both sides and along the back
            Assert.Contains(plan.FencePieces, p => a.ToLocal(p.Position).x < -40f);
            Assert.Contains(plan.FencePieces, p => a.ToLocal(p.Position).x > 40f);
            Assert.Contains(plan.FencePieces, p => a.ToLocal(p.Position).y < -60f);
        }

        [Fact]
        public void At_maximum_reach_houses_facing_every_way_split_the_land_and_no_two_gardens_overlap()
        {
            var opts = new Dictionary<string, float>(PolishLooks.Defaults);
            foreach (var k in PolishLooks.ReachKnobs) opts[k] = 500f;
            var houses = new List<StitchHouse>();
            // round the edge of a block: some face north, some east, some south, some west, far apart
            float2[] at = { new float2(0f, 0f), new float2(90f, 10f), new float2(170f, 60f), new float2(160f, 160f), new float2(60f, 190f), new float2(-40f, 120f) };
            float2[] fwd = { new float2(0f, -1f), new float2(0f, -1f), new float2(1f, 0f), new float2(0f, 1f), new float2(0f, 1f), new float2(-1f, 0f) };
            for (int k = 0; k < at.Length; k++) { var h = House(k + 1, at[k], fwd[k]); h.Options = opts; houses.Add(h); }
            var plan = YardStitch.Plan(houses, MiddleLine());
            var plots = plan.Plots.ToList();
            Assert.True(plots.Count >= 4, "the gardens grow");
            for (int a = 0; a < plots.Count; a++)
                for (int b = a + 1; b < plots.Count; b++)
                {
                    float shared = Poly.Boolean(new[] { (IReadOnlyList<float2>)plots[a].Value }, new[] { (IReadOnlyList<float2>)plots[b].Value }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area);
                    Assert.True(shared < 2f, $"gardens {plots[a].Key} and {plots[b].Key} overlap by {shared:F0} m²");
                }
        }

        [Fact]
        public void On_the_inside_of_a_curve_sprawling_gardens_end_before_they_turn_into_spikes()
        {
            // the Sprawling style's reach (the game runner's arc: long thin hedge spikes towards the curve's centre)
            var opts = new Dictionary<string, float>(PolishLooks.Defaults)
            {
                ["sideReach"] = 250f, ["maxFrontGap"] = 200f, ["maxBackGap"] = 300f, ["maxRearGap"] = 150f, ["maxFacingGap"] = 120f,
                ["maxDeepen"] = 150f, ["hugReach"] = 150f, ["openReach"] = 80f,
            };
            // lot centres on a circle of 80 m, facing out (the street outside them): their backs close in towards the centre
            var houses = new List<StitchHouse>();
            for (int k = 0; k < 5; k++)
            {
                float a = math.radians(60f) + k * 0.22f;
                var dir = new float2(math.cos(a), math.sin(a));
                var h = House(k + 1, dir * 80f, dir);
                h.Options = opts;
                houses.Add(h);
            }
            var plan = YardStitch.Plan(houses, MiddleLine());
            Assert.NotEmpty(plan.Plots);
            // 0.22 rad apart, a garden between two middle lines is 8 m wide 36 m from the centre: none reaches far past that (the
            // two at the ends have open land beside them: they grow into it)
            foreach (var (id, ring) in plan.Plots.Where(kv => kv.Key >= 2 && kv.Key <= 4))
                foreach (var p in ring)
                    Assert.True(math.length(p) > 28f, $"garden {id} runs out to {math.length(p):F0} m from the centre ({string.Join(" | ", plan.Notes)})");
        }

        [Fact]
        public void A_garden_written_by_another_pass_is_kept_off_and_one_over_a_new_lot_is_planned_again()
        {
            // houses grown one after another: a's garden (written by an earlier pass, big) reaches over where b now stands
            var opts = new Dictionary<string, float>(PolishLooks.Defaults);
            foreach (var k in PolishLooks.ReachKnobs) opts[k] = 150f;
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(60f, 0f), new float2(0f, 1f));
            a.Options = b.Options = opts;
            a.Edit = false;
            a.Garden = new[] { new float2(-8f, 24f), new float2(70f, 24f), new float2(70f, -90f), new float2(-8f, -90f) };
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.True(plan.Plots.TryGetValue(2, out var plotB), "b's garden is planned");
            float shared = Poly.Boolean(new[] { (IReadOnlyList<float2>)plotB }, new[] { (IReadOnlyList<float2>)a.Garden }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area);
            // b keeps only its own lot out of a's garden (16 x 48 m, the part a's garden covers: x 52..68 of a's 70)
            Assert.True(shared <= 16f * 48f + 2f, $"b's garden takes {shared:F0} m² of a's");
            Assert.Contains(1L, plan.GardenClashes);
        }

        [Fact]
        public void A_garden_written_by_another_pass_next_door_but_off_the_lot_is_no_clash()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(40f, 0f), new float2(0f, 1f));
            a.Edit = false;
            a.Garden = new[] { new float2(-8f, 24f), new float2(20f, 24f), new float2(20f, -40f), new float2(-8f, -40f) };
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Empty(plan.GardenClashes);
            if (plan.Plots.TryGetValue(2, out var plotB))
            {
                float shared = Poly.Boolean(new[] { (IReadOnlyList<float2>)plotB }, new[] { (IReadOnlyList<float2>)a.Garden }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area);
                Assert.True(shared < 1f, $"b's garden takes {shared:F0} m² of a's");
            }
        }

        private static bool Overlap(float2[] a, float2[] b) =>
            Poly.Boolean(new[] { (IReadOnlyList<float2>)a }, new[] { (IReadOnlyList<float2>)b }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area) > 0.05f;

        private static StitchSettings MiddleLine()
        {
            var s = new StitchSettings();
            s.Extra["steps"] = 0f;
            s.Extra["alleys"] = 0f;
            return s;
        }

        [Fact]
        public void A_wedge_can_be_split_front_and_back_with_fences_on_the_houses_own_lines_and_a_bush_row_for_the_step()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            var steps = new StitchSettings();
            steps.Extra["stepChance"] = 100f;
            var plan = YardStitch.Plan(new[] { a, b }, steps);
            Assert.Contains(plan.Notes, n => n.Contains("the gap split"));
            // both side lines stay on their own lot lines (only shortened), so no diagonal fence
            foreach (var (h, id, x) in new[] { (a, 11L, 8f), (b, 22L, -8f) })
            {
                var m = plan.MoveLines[id];
                Assert.True(math.abs(h.ToLocal(m.A).x - x) < 0.05f && math.abs(h.ToLocal(m.B).x - x) < 0.05f, $"line {id} left its lot line: {m.A} {m.B}");
                Assert.True(math.distance(m.A, m.B) > 8f && math.distance(m.A, m.B) < 40f, $"line {id} is {math.distance(m.A, m.B):F1} m");
            }
            // the two gardens do not overlap, and together cover the wedge
            var pa = plan.Plots[1];
            var pb = plan.Plots[2];
            Assert.True(Poly.Intersection(pa, pb).Sum(r => r.Area) < 1f);
            // the step is planted: a bed with bushes close together
            Assert.NotEmpty(plan.Beds);
            Assert.True(plan.Bushes.Count >= 3);
        }


        [Fact]
        public void Two_deep_lots_meet_behind_a_shallow_one_between_them()
        {
            // a straight street: 48 m deep, 16 m deep, 48 m deep, touching, street edges in line (z = 24 in world)
            var l = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var m = new StitchHouse { Id = 2, Position = new float2(16f, 16f), Forward = new float2(0f, 1f), HalfLot = new float2(8f, 8f) };
            m.Lines.Add(new StitchLine { Id = 21, Name = "FenceResidentialHigh01", A = m.ToWorld(new float2(-7.9f, 7.9f)), B = m.ToWorld(new float2(-7.9f, -7.9f)) });
            m.Lines.Add(new StitchLine { Id = 23, Name = "FenceResidentialHigh01", A = m.ToWorld(new float2(7.9f, -7.9f)), B = m.ToWorld(new float2(-7.9f, -7.9f)) });
            var r = House(3, new float2(32f, 0f), new float2(0f, 1f));
            var plan = YardStitch.Plan(new[] { l, m, r }, MiddleLine());
            Assert.Contains(plan.Notes, x => x.Contains("meet behind 2"));
            // the land behind the shallow lot (y 8 down to -24) is the deep neighbours': left half to 1, right half to 3
            Assert.True(Poly.Contains(plan.Plots[1], new float2(12f, -10f)), "left half behind the shallow lot is not 1's");
            Assert.True(Poly.Contains(plan.Plots[3], new float2(20f, -10f)), "right half behind the shallow lot is not 3's");
            // a line down the middle behind it
            Assert.Contains(plan.MoveLines.Values, mv => math.abs(mv.A.x - 16f) < 0.05f && math.abs(mv.B.x - 16f) < 0.05f && math.abs(mv.A.y - mv.B.y) > 20f);
            // no line runs across the land behind it from street to back on the old lot edges (x = 8 or 24 below y = 8)
            foreach (var mv in plan.MoveLines.Values.Concat(new[] { l, m, r }.SelectMany(h => h.Lines).Where(x => !plan.HideLines.Contains(x.Id) && !plan.MoveLines.ContainsKey(x.Id)).Select(x => (x.A, x.B))))
                foreach (float x in new[] { 8f, 24f })
                    if (math.abs(mv.A.x - x) < 1.2f && math.abs(mv.B.x - x) < 1.2f) Assert.True(math.min(mv.A.y, mv.B.y) > 7f, $"a line on x {x} runs on behind the shallow lot: {mv.A} {mv.B}");
        }

        [Fact]
        public void Big_new_garden_land_gets_a_feature_inside_its_own_garden_clear_of_the_house_and_paving()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float c = math.radians(90f);
            var houses = new[] { House(1, At(c + 0.55f), Facing(c + 0.55f)), House(2, At(c), Facing(c)), House(3, At(c - 0.55f), Facing(c - 0.55f)) };
            foreach (var h in houses)
            {
                h.Footprint = new[] { h.ToWorld(new float2(-5f, 2f)), h.ToWorld(new float2(5f, 2f)), h.ToWorld(new float2(5f, 14f)), h.ToWorld(new float2(-5f, 14f)) };
                h.Hard.Add(new[] { h.ToWorld(new float2(-8f, 24f)), h.ToWorld(new float2(-4f, 24f)), h.ToWorld(new float2(-4f, 2f)), h.ToWorld(new float2(-8f, 2f)) });
            }
            var settings = MiddleLine();
            settings.Extra["featureChance"] = 100f;
            var plan = YardStitch.Plan(houses, settings);
            var named = new[] { "rock garden", "sand pit", "wild corner", "bench nook" };
            Assert.Contains(plan.Notes, x => named.Any(x.Contains));
            // every object stands in its own house's garden, never on a house or a driveway
            foreach (var (house, p, _, _) in plan.Bushes)
            {
                Assert.True(Poly.Contains(plan.Plots[house], p), $"an object of {house} outside its garden");
                foreach (var h in houses)
                {
                    Assert.False(Poly.Contains(h.Footprint, p), $"an object on house {h.Id}");
                    Assert.False(Poly.Contains(h.Hard[0], p), $"an object on the driveway of {h.Id}");
                }
            }
            Assert.True(plan.Paths.All(pt => SurfaceAcceptance.IsAccepted(pt.Ring)));
        }

        [Fact]
        public void A_border_line_that_would_run_through_a_house_stops_at_the_house()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            // b's house is wider than its lot on a's side: it reaches over the border
            b.Footprint = new[] { b.ToWorld(new float2(-14f, 10f)), b.ToWorld(new float2(6f, 10f)), b.ToWorld(new float2(6f, -10f)), b.ToWorld(new float2(-14f, -10f)) };
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            // the hedge is still kept on the border, but no piece of it lies inside b's house
            Assert.True(plan.MoveLines.ContainsKey(22L));
            var kept = plan.MoveLines[22L];
            float inside = Poly.ClipSegmentToPolygon(b.Footprint, kept.A, kept.B).Sum(x => math.distance(x.a, x.b));
            Assert.True(inside < 0.05f, $"{inside} m of the hedge inside the house");
            Assert.Contains(plan.Notes, n => n.Contains("stop at a house"));
        }

        [Fact]
        public void Back_to_back_houses_share_one_back_line_on_the_middle_line_and_lawns_reach_it()
        {
            // a faces south to its street, b faces north to the other street; 8 m between their back edges
            var a = House(1, new float2(0f, 0f), new float2(0f, -1f), right: "FenceResidentialHigh01");
            var b = House(2, new float2(0f, 56f), new float2(0f, 1f), right: "HedgeHigh01");
            // lawns along the back edges
            a.Lawns[0].Ring = new[] { a.ToWorld(new float2(-7f, -23f)), a.ToWorld(new float2(7f, -23f)), a.ToWorld(new float2(7f, -10f)), a.ToWorld(new float2(-7f, -10f)) };
            b.Lawns[0].Ring = new[] { b.ToWorld(new float2(-7f, -23f)), b.ToWorld(new float2(7f, -23f)), b.ToWorld(new float2(7f, -10f)), b.ToWorld(new float2(-7f, -10f)) };
            var noPlanting = new StitchSettings();
            noPlanting.Extra["plant"] = 0f;   // fences and lawns only (beds along the fences: their own test)
            var plan = YardStitch.Plan(new[] { a, b }, noPlanting);
            Assert.Single(plan.Pairs, p => p.Rear);
            // both back lines go onto the middle line (z = 28); b's is a hedge (its "right" kind), a's a fence: the hedge stays
            Assert.Contains(13L, plan.HideLines);
            var kept = plan.MoveLines[23L];
            Assert.True(math.abs(kept.A.y - 28f) < 0.01f && math.abs(kept.B.y - 28f) < 0.01f, $"kept back line at {kept.A} {kept.B}");
            // both lawns reach the middle line
            Assert.True(plan.Lawns[15L].Max(p => p.y) > 27.5f);
            Assert.True(plan.Lawns[25L].Min(p => p.y) < 28.5f);
            // a's right fence ends at its back corner: it now reaches the middle line too
            Assert.True(math.abs(plan.MoveLines[11L].B.y - 28f) < 0.01f);
        }

        [Fact]
        public void Back_to_back_rows_of_different_widths_meet_on_one_middle_line_with_no_doubled_fence()
        {
            // a city block's middle: three 16 m lots facing south, two 24 m lots facing north behind them, 10 m between backs
            var houses = new List<StitchHouse>();
            for (int i = 0; i < 3; i++) houses.Add(House(i + 1, new float2(i * 16f, 0f), new float2(0f, -1f)));
            for (int i = 0; i < 2; i++)
            {
                var h = new StitchHouse { Id = i + 4, Position = new float2(4f + i * 24f, 58f), Forward = new float2(0f, 1f), HalfLot = new float2(12f, 24f) };
                h.Lines.Add(new StitchLine { Id = (i + 4) * 10 + 3, Name = "HedgeHigh01", A = h.ToWorld(new float2(11.9f, -23.9f)), B = h.ToWorld(new float2(-11.9f, -23.9f)) });
                h.Lines.Add(new StitchLine { Id = (i + 4) * 10 + 1, Name = "FenceResidentialHigh01", A = h.ToWorld(new float2(11.9f, 23.9f)), B = h.ToWorld(new float2(11.9f, -23.9f)) });
                houses.Add(h);
            }
            var plan = YardStitch.Plan(houses);
            Assert.True(plan.Pairs.Count(p => p.Rear) >= 2);
            // every back line still drawn lies on the middle line (y = 29)
            var backs = new List<(float2 A, float2 B)>();
            foreach (var h in houses)
                foreach (var l in h.Lines.Where(l => l.Id % 10 == 3 && !plan.HideLines.Contains(l.Id)))
                {
                    var m = plan.MoveLines.TryGetValue(l.Id, out var mv) ? mv : (l.A, l.B);
                    Assert.True(math.abs(m.A.y - 29f) < 0.05f && math.abs(m.B.y - 29f) < 0.05f, $"back line {l.Id} at {m.A} {m.B}");
                    backs.Add(m);
                }
            // no two of them overlap (one fence or hedge along any stretch of the middle line)
            for (int i = 0; i < backs.Count; i++)
                for (int j = i + 1; j < backs.Count; j++)
                {
                    float o = math.min(math.max(backs[i].A.x, backs[i].B.x), math.max(backs[j].A.x, backs[j].B.x)) - math.max(math.min(backs[i].A.x, backs[i].B.x), math.min(backs[j].A.x, backs[j].B.x));
                    Assert.True(o < 0.6f, $"two back lines overlap by {o:F1} m");
                }
            // and together they cover the shared stretch (the hedges win over the fences)
            Assert.True(backs.Sum(b => math.distance(b.A, b.B)) > 44f, $"back lines cover {backs.Sum(b => math.distance(b.A, b.B)):F1} m");
        }

        [Fact]
        public void Houses_inside_a_small_circle_get_slices_that_stop_at_a_shared_green()
        {
            // eight houses inside a ring road, facing out to it, backs close to the centre (front edges on radius 54, backs on 6)
            var houses = new List<StitchHouse>();
            for (int i = 0; i < 8; i++)
            {
                float ang = i * math.PI * 2f / 8f;
                var dir = new float2(math.cos(ang), math.sin(ang));
                var h = House(i + 1, dir * 30f, dir);
                // back gardens reaching from the house to (past) where the ring line will be
                h.Lawns[0].Ring = new[] { h.ToWorld(new float2(-7f, -12f)), h.ToWorld(new float2(7f, -12f)), h.ToWorld(new float2(7f, 2f)), h.ToWorld(new float2(-7f, 2f)) };
                houses.Add(h);
            }
            var plan = YardStitch.Plan(houses);
            Assert.Equal(8, plan.Pairs.Count(p => !p.Rear));
            // the slices stop at one ring line, half the street's radius out (27 m along each house's axis): no long thin
            // points, a shared green inside
            foreach (var p in plan.Pairs.Where(p => !p.Rear)) Assert.True(math.abs(math.length(p.Back) - 27f / math.cos(math.radians(22.5f))) < 1f, $"shared fence ends at radius {math.length(p.Back)}");
            Assert.Contains(plan.Notes, n => n.Contains("shared green"));
            Assert.Single(plan.Commons);
            // each back fence lies on the ring line, from one shared fence to the next
            for (int i = 1; i <= 8; i++)
            {
                var m = plan.MoveLines[i * 10L + 3];
                var h = houses[i - 1];
                Assert.True(math.abs(math.dot(m.A, h.Forward) - 27f) < 0.05f && math.abs(math.dot(m.B, h.Forward) - 27f) < 0.05f, $"back fence of {i} at {m.A} {m.B}");
                Assert.True(math.distance(m.A, m.B) > 20f, $"back fence of {i} only {math.distance(m.A, m.B):F1} m");
            }
            // lawns stay in their slice and stop at the ring line
            Assert.True(plan.Lawns.Count >= 6, $"{plan.Lawns.Count} lawns grew");
            foreach (var kv in plan.Lawns)
            {
                var h = houses.Single(x => x.Id * 10 + 5 == kv.Key);
                foreach (var p in kv.Value)
                {
                    float ang = math.degrees(math.abs(math.atan2(math.dot(p, h.Right), math.dot(p, h.Forward))));
                    Assert.True(ang < 22.5f + 2f, $"lawn of {h.Id} leaves its slice ({ang:F1}°)");
                    Assert.True(math.dot(p, h.Forward) > 26.5f, $"lawn of {h.Id} crosses the ring line");
                }
            }
            Assert.True(plan.Lawns.Values.All(r => SurfaceAcceptance.IsAccepted(r)));
        }

        [Fact]
        public void A_big_middle_keeps_its_back_fences_joined_up_around_a_shared_green()
        {
            // shallow lots (16 m deep) on a big ring: the borders run on 8 m, the back fences join their ends, the middle is shared
            var houses = new List<StitchHouse>();
            for (int i = 0; i < 10; i++)
            {
                float ang = i * math.PI * 2f / 10f;
                var dir = new float2(math.cos(ang), math.sin(ang));
                var h = new StitchHouse { Id = i + 1, Position = dir * 58f, Forward = dir, HalfLot = new float2(8f, 8f) };
                h.Lines.Add(new StitchLine { Id = (i + 1) * 10 + 1, Name = "HedgeHigh01", A = h.ToWorld(new float2(7.9f, 7.9f)), B = h.ToWorld(new float2(7.9f, -7.9f)) });
                h.Lines.Add(new StitchLine { Id = (i + 1) * 10 + 3, Name = "FenceResidentialHigh01", A = h.ToWorld(new float2(7.9f, -7.9f)), B = h.ToWorld(new float2(-7.9f, -7.9f)) });
                houses.Add(h);
            }
            var plan = YardStitch.Plan(houses);
            Assert.Contains(plan.Notes, n => n.Contains("shared green"));
            // every back fence moves onto the ring line (half the street's radius: 33 m) and reaches both shared fences, so
            // together they close the ring
            for (int i = 1; i <= 10; i++)
            {
                Assert.True(plan.MoveLines.ContainsKey(i * 10L + 3), $"back fence of {i} not joined");
                var m = plan.MoveLines[i * 10L + 3];
                var h = houses[i - 1];
                Assert.True(math.abs(math.dot(m.A, h.Forward) - 33f) < 0.05f && math.abs(math.dot(m.B, h.Forward) - 33f) < 0.05f, $"back fence of {i} at {m.A} {m.B}");
            }
            foreach (var p in plan.Pairs.Where(p => !p.Rear)) Assert.True(plan.MoveLines.Values.Count(m => math.distance(m.A, p.Back) < 0.05f || math.distance(m.B, p.Back) < 0.05f) >= 2, "a shared fence's back end meets no back fence");
            Assert.Single(plan.Commons);
            Assert.True(Poly.Area(plan.Commons[0]) > 3000f, $"shared green {Poly.Area(plan.Commons[0]):F0} m²");
        }

        [Fact]
        public void A_house_behind_another_is_not_its_side_neighbour()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(12f, -40f), new float2(0f, 1f));
            Assert.Empty(YardStitch.Plan(new[] { a, b }).Pairs);
        }

        [Fact]
        public void Houses_across_the_street_are_never_a_pair()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(16f, 70f), new float2(0f, -1f));
            Assert.Empty(YardStitch.Plan(new[] { a, b }).Pairs);
        }

        [Fact]
        public void A_row_of_three_stitches_both_gaps_of_the_middle_house()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float c = math.radians(90f);
            var houses = new[] { House(1, At(c + 0.45f), Facing(c + 0.45f)), House(2, At(c), Facing(c)), House(3, At(c - 0.45f), Facing(c - 0.45f)) };
            var plan = YardStitch.Plan(houses, MiddleLine());
            Assert.Equal(2, plan.Pairs.Count);
            // the middle house's back fence meets the border on both ends
            var back = plan.MoveLines[23L];
            Assert.True(math.distance(back.A, plan.Pairs.Single(p => p.Left == 2).Back) < 0.01f);
            Assert.True(math.distance(back.B, plan.Pairs.Single(p => p.Right == 2).Back) < 0.01f);
        }

        // ───────────── yes meets no (the player's per-lot flag) ─────────────

        /// <summary>A wedge on the outside of a curve: a (polished) on the left, b (unpolished) on the right.</summary>
        private static (StitchHouse A, StitchHouse B) Wedge()
        {
            float2 At(float angle) => 64f * new float2(math.cos(angle), math.sin(angle));
            float2 Facing(float angle) => -new float2(math.cos(angle), math.sin(angle));
            float a0 = math.radians(90f), a1 = a0 + 0.45f;
            var a = House(1, At(a0), Facing(a0));
            var b = House(2, At(a1), Facing(a1));
            b.Polished = false;
            return (a, b);
        }

        private static void AssertUntouched(StitchPlan plan, StitchHouse h)
        {
            foreach (var l in h.Lines)
            {
                Assert.DoesNotContain(l.Id, plan.HideLines);
                Assert.False(plan.MoveLines.ContainsKey(l.Id), $"unpolished {h.Id}'s line {l.Id} moved");
            }
            foreach (var l in h.Lawns) Assert.False(plan.Lawns.ContainsKey(l.Id), $"unpolished {h.Id}'s lawn changed");
            Assert.DoesNotContain(plan.NewLawns, x => x.House == h.Id);
            Assert.DoesNotContain(plan.Beds, x => x.House == h.Id);
            Assert.DoesNotContain(plan.Bushes, x => x.House == h.Id);
            Assert.DoesNotContain(plan.Paths, x => x.House == h.Id);
            Assert.False(plan.Plots.ContainsKey(h.Id));
        }

        /// <summary>How far p lies inside h's lot (negative: outside).</summary>
        private static float InsideLot(StitchHouse h, float2 p)
        {
            var l = h.ToLocal(p);
            return math.min(h.HalfLot.x - math.abs(l.x), h.HalfLot.y - math.abs(l.y));
        }

        [Fact]
        public void A_polished_house_beside_an_unpolished_one_takes_the_whole_gap_and_leans_on_its_fence()
        {
            var (a, b) = Wedge();
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            var pair = Assert.Single(plan.Pairs);
            Assert.True(pair.Edge);
            AssertUntouched(plan, b);
            // b's left hedge is the border: a's own right fence (its duplicate) goes
            Assert.Contains(11L, plan.HideLines);
            Assert.Equal(22L, pair.KeptLine);
            // a's garden reaches b's left lot edge (not the middle line), and never into b's lot
            var plot = plan.Plots[1];
            Assert.True(plot.Max(p => b.ToLocal(p).x) > -b.HalfLot.x - 0.05f, "a's plot stops short of b's edge");
            Assert.True(plot.All(p => InsideLot(b, p) < 0.05f), "a's plot enters b's lot");
            var lawn = plan.Lawns[15L];
            Assert.True(lawn.Any(p => math.abs(b.ToLocal(p).x + b.HalfLot.x) < 0.6f), "a's lawn does not reach b's edge");
            Assert.True(lawn.All(p => InsideLot(b, p) < 0.05f), "a's lawn enters b's lot");
            // a's back fence runs on to b's edge
            var back = plan.MoveLines[13L];
            Assert.True(new[] { back.A, back.B }.Any(p => math.abs(b.ToLocal(p).x + b.HalfLot.x) < 0.1f), $"a's back fence ends at {back.A} {back.B}");
            Assert.Contains(plan.Notes, n => n.Contains("leans on"));
        }

        [Fact]
        public void When_the_unpolished_lot_has_no_fence_on_that_side_the_polished_ones_line_goes_on_its_edge()
        {
            var (a, b) = Wedge();
            b.Lines.RemoveAll(l => l.Id == 22L);   // b has no left line
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            AssertUntouched(plan, b);
            Assert.DoesNotContain(11L, plan.HideLines);
            var m = plan.MoveLines[11L];
            foreach (var p in new[] { m.A, m.B })
                Assert.True(math.abs(b.ToLocal(p).x + b.HalfLot.x) < 0.05f, $"a's fence end {p} is not on b's left edge (local x {b.ToLocal(p).x:F2})");
            Assert.True(math.distance(m.A, m.B) > 30f, $"a's fence on the edge is only {math.distance(m.A, m.B):F1} m");
        }

        [Fact]
        public void The_unpolished_house_on_the_left_is_the_edge_too()
        {
            var (a, b) = Wedge();
            a.Polished = false;
            b.Polished = true;
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            AssertUntouched(plan, a);
            // a's right fence is the border: b's left hedge (its duplicate) goes, b's garden reaches a's right edge
            Assert.Contains(22L, plan.HideLines);
            Assert.Equal(11L, plan.Pairs[0].KeptLine);
            Assert.True(plan.Plots[2].Min(p => a.ToLocal(p).x) < a.HalfLot.x + 0.05f);
            Assert.True(plan.Plots[2].All(p => InsideLot(a, p) < 0.05f));
        }

        [Fact]
        public void Two_unpolished_neighbours_are_left_alone()
        {
            var (a, b) = Wedge();
            a.Polished = false;
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.Empty(plan.Pairs);
            Assert.Empty(plan.HideLines);
            Assert.Empty(plan.MoveLines);
            Assert.Empty(plan.Lawns);
            Assert.Empty(plan.Paints);
            AssertUntouched(plan, a);
            AssertUntouched(plan, b);
        }

        [Fact]
        public void Nothing_is_planted_or_painted_on_an_unpolished_lot()
        {
            var (a, b) = Wedge();
            var c = House(3, 64f * new float2(math.cos(math.radians(90f) - 0.45f), math.sin(math.radians(90f) - 0.45f)), -new float2(math.cos(math.radians(90f) - 0.45f), math.sin(math.radians(90f) - 0.45f)));
            var houses = new[] { c, a, b };
            foreach (var h in houses) h.Footprint = new[] { h.ToWorld(new float2(-5f, 2f)), h.ToWorld(new float2(5f, 2f)), h.ToWorld(new float2(5f, 14f)), h.ToWorld(new float2(-5f, 14f)) };
            var plan = YardStitch.Plan(houses, MiddleLine());
            AssertUntouched(plan, b);
            // ground paint never reaches into b's lot (a soft edge may touch it)
            foreach (var paint in plan.Paints)
            {
                var ring = paint.Ring.Select(p => p.xz).ToArray();
                Assert.True(ring.Count(p => InsideLot(b, p) > 1f) == 0, $"paint layer {paint.Layer} reaches into the unpolished lot");
            }
            foreach (var (_, p, _, _) in plan.Bushes) Assert.True(InsideLot(b, p) < 0f);
            foreach (var (_, ring) in plan.Beds) Assert.True(ring.All(p => InsideLot(b, p) < 0.05f));
        }

        [Fact]
        public void A_polished_house_back_to_back_with_an_unpolished_one_reaches_its_back_edge_and_uses_its_back_fence()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, -1f), right: "FenceResidentialHigh01");
            var b = House(2, new float2(0f, 56f), new float2(0f, 1f), right: "HedgeHigh01");
            b.Polished = false;
            a.Lawns[0].Ring = new[] { a.ToWorld(new float2(-7f, -23f)), a.ToWorld(new float2(7f, -23f)), a.ToWorld(new float2(7f, -10f)), a.ToWorld(new float2(-7f, -10f)) };
            var noPlanting = new StitchSettings();
            noPlanting.Extra["plant"] = 0f;
            var plan = YardStitch.Plan(new[] { a, b }, noPlanting);
            AssertUntouched(plan, b);
            Assert.Single(plan.Pairs, p => p.Rear && p.Edge);
            // a's garden reaches b's back edge (z = 32), not the middle line (28); b's back hedge closes it, so a's back fence goes
            Assert.True(plan.Lawns[15L].Max(p => p.y) > 31.4f, $"a's lawn reaches {plan.Lawns[15L].Max(p => p.y):F1}");
            Assert.True(plan.Lawns[15L].Max(p => p.y) < 32.05f);
            Assert.True(plan.HideLines.Contains(13L) || (plan.MoveLines.TryGetValue(13L, out var m13) && math.distance(m13.A, m13.B) < 2.5f), "a's back fence doubles b's hedge");
        }

        private static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.dot(ab, ab));
            return math.distance(p, a + ab * t);
        }

        private static float Side(float2 a, float2 b, float2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        [Fact]
        public void A_garden_never_grows_up_a_cliff()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 60f, ["maxRise"] = 0f };
            // flat, then a cliff (150 %) from 20 m to the house's right; a 40 % slope on its left (gardens may climb that)
            var s = MiddleLine();
            s.Ground = p => p.x > 20f ? (p.x - 20f) * 1.5f : p.x < -20f ? (-20f - p.x) * 0.4f : 0f;
            var plan = YardStitch.Plan(new[] { a }, s);
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "the garden grows");
            Assert.True(pa.Max(p => p.x) <= 21.5f && pa.Max(p => p.x) >= 16f, $"stops at the foot of the cliff: {pa.Max(p => p.x):0.0}");
            Assert.True(pa.Min(p => p.x) < -8f - 55f, "the flat side still grows its full reach");
            // no limit: up the cliff as before
            a.Options["maxSlope"] = 0f;
            var free = YardStitch.Plan(new[] { a }, s);
            Assert.True(free.Plots[1].Max(p => p.x) > 8f + 55f);
        }

        [Fact]
        public void A_garden_stops_where_the_land_climbs_too_far_above_its_house()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 60f, ["maxSlope"] = 0f, ["maxRise"] = 4f };
            // a 10 % rise behind the house: 4 m above it 40 m back
            var s = MiddleLine();
            s.Ground = p => math.max(0f, -p.y) * 0.1f;
            var plan = YardStitch.Plan(new[] { a }, s);
            Assert.True(plan.Plots.TryGetValue(1, out var pa));
            float back = pa.Min(p => p.y);
            Assert.True(back >= -43f && back <= -36f, $"the back stops about 40 m behind the house: {back:0.0}");
            Assert.True(pa.Min(p => p.x) < -8f - 55f && pa.Max(p => p.x) > 8f + 55f, "the level sides grow their full reach");
        }

        // a's garden was written by an earlier pass (to the middle line at x = 20); b has no hedge of its own on that side
        private static (StitchHouse A, StitchHouse B) PlannedEarlierNextTo()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            var b = House(2, new float2(40f, 0f), new float2(0f, 1f));
            b.Lines.RemoveAll(l => l.Id == 22);
            a.Edit = false;
            a.Garden = new[] { new float2(-8f, 24f), new float2(20f, 24f), new float2(20f, -24f), new float2(-8f, -24f) };
            return (a, b);
        }

        [Fact]
        public void A_shared_edge_is_closed_against_the_neighbours_fence_where_it_really_stands()
        {
            // the neighbour planned earlier left its right fence on its own lot edge (x = 8), not on the middle line: this plan
            // never moves it, so b closes the middle line itself (it used to count a's fence as moved there: a gap)
            var (a, b) = PlannedEarlierNextTo();
            a.Standing = new List<(float2 A, float2 B)> { (new float2(8f, 24f), new float2(8f, -24f)), (new float2(8f, -24f), new float2(-8f, -24f)) };
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.True(plan.Plots.TryGetValue(2, out var pb) && pb.Min(p => p.x) < 20.5f, "b reaches the middle line");
            Assert.Contains(plan.FencePieces, p => p.House == 2 && math.abs(p.Position.x - 20f) < 1f && p.Position.y < 20f);
        }

        [Fact]
        public void A_shared_edge_already_fenced_by_the_neighbour_gets_no_second_fence()
        {
            // the neighbour planned earlier put its fence on the middle line: b adds nothing along it, and its own line there
            // (were it moved onto it) gives way
            var (a, b) = PlannedEarlierNextTo();
            a.Standing = new List<(float2 A, float2 B)> { (new float2(20f, 24f), new float2(20f, -24f)), (new float2(20f, -24f), new float2(-8f, -24f)) };
            var plan = YardStitch.Plan(new[] { a, b }, MiddleLine());
            Assert.DoesNotContain(plan.FencePieces, p => p.House == 2 && math.abs(p.Position.x - 20f) < 1f);
            foreach (var kv in plan.MoveLines.Where(m => m.Key / 10 == 2))
                Assert.False(math.abs(kv.Value.A.x - 20f) < 0.6f && math.abs(kv.Value.B.x - 20f) < 0.6f, $"b's line {kv.Key} doubles a's fence on the middle line");
        }

        [Fact]
        public void A_lawn_reaches_the_fence_of_a_big_garden_by_default()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 60f, ["paintedGardens"] = 0f };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "the garden grows");
            var lawns = plan.Lawns.Values.Concat(plan.NewLawns.Where(l => l.House == 1).Select(l => l.Ring)).ToList();
            Assert.NotEmpty(lawns);
            float lawnArea = lawns.Sum(r => Poly.Area(r));
            float land = Poly.Area(pa);
            // the whole garden but the house's own lot corners it keeps (the lawn there is the house's own)
            Assert.True(lawnArea > land * 0.85f, $"lawn {lawnArea:F0} m² of a {land:F0} m² garden");
            Assert.True(lawns.Max(r => r.Max(p => p.x)) > pa.Max(p => p.x) - 1f, "the lawn reaches the side fence");
            Assert.True(lawns.Min(r => r.Min(p => p.y)) < pa.Min(p => p.y) + 1f, "the lawn reaches the back fence");
        }

        [Fact]
        public void A_paved_type_fills_its_ground_up_to_the_fence_whatever_its_lawn_reach()
        {
            // a type that paves (a shop or a works alike): one slab to the fence
            foreach (var (zone, options) in new[] { ("shop", new Dictionary<string, float> { ["fill"] = PolishLooks.FillPaved }), ("industry", new Dictionary<string, float> { ["fill"] = PolishLooks.FillPaved }) })
            {
                var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
                a.MainSurface = "Concrete Surface 01";
                a.Zone = zone;
                a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 40f, ["lawnReach"] = 10f };
                foreach (var kv in options) a.Options[kv.Key] = kv.Value;
                var plan = YardStitch.Plan(new[] { a }, MiddleLine());
                Assert.True(plan.Plots.TryGetValue(1, out var pa), "the yard grows at the back");
                var paving = plan.Paths.Where(p => p.House == 1 && p.Surface == "Concrete Surface 01").ToList();
                Assert.NotEmpty(paving);
                Assert.True(paving.Min(p => p.Ring.Min(q => q.y)) < pa.Min(p => p.y) + 1f, $"{zone}: the paving reaches the back fence ({paving.Min(p => p.Ring.Min(q => q.y)):F1} vs {pa.Min(p => p.y):F1})");
                Assert.DoesNotContain(plan.Bushes, b => b.Kind >= 29);
            }
        }

        private static float2[] Box(float2 c, float2 half, float yaw)
        {
            var rx = new float2(math.cos(yaw), -math.sin(yaw)) * half.x;
            var rz = new float2(math.sin(yaw), math.cos(yaw)) * half.y;
            return new[] { c - rx - rz, c + rx - rz, c + rx + rz, c - rx + rz };
        }

        [Fact]
        public void A_shops_big_back_lot_gets_green_edges_a_service_corner_an_overflow_car_park_and_green_ground_further_out()
        {
            // a 5x4 shop (40 x 32 m lot), its car park in front, its building at the back of the lot; 50 m of land behind it
            var a = new StitchHouse { Id = 1, Position = new float2(0f, 0f), Forward = new float2(0f, 1f), HalfLot = new float2(20f, 16f), MainSurface = "Concrete Surface 01", Zone = "shop" };
            a.Footprint = new[] { a.ToWorld(new float2(-14f, -14f)), a.ToWorld(new float2(14f, -14f)), a.ToWorld(new float2(14f, 2f)), a.ToWorld(new float2(-14f, 2f)) };
            a.Hard.Add(new[] { a.ToWorld(new float2(-19f, 3f)), a.ToWorld(new float2(19f, 3f)), a.ToWorld(new float2(19f, 15.5f)), a.ToWorld(new float2(-19f, 15.5f)) });
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 50f, ["fill"] = PolishLooks.FillCarPark, ["parkWorn"] = 0f, ["parkReach"] = 32f };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.True(plan.Plots.TryGetValue(1, out var plot), "the yard grows");
            var lot = new[] { a.BackLeft, a.BackRight, a.FrontRight, a.FrontLeft };
            string Name(int k) => YardStitch.BushKinds[k];
            var mine = plan.Bushes.Where(b => b.House == 1).ToList();

            // a grass strip inside the fence, street trees on it
            Assert.Contains(plan.Paths, p => p.House == 1 && p.Surface == "Grass Surface 01");
            Assert.Contains(mine, b => Name(b.Kind).Contains("Tree"));
            // the service corner: bins and yard stock, by the building's back
            var bins = mine.Where(b => Name(b.Kind).StartsWith("TrashContainer")).ToList();
            Assert.True(bins.Count == 2, $"{bins.Count} bins: " + string.Join(" | ", plan.Notes.Where(n => n.StartsWith("1:"))));
            Assert.Contains(mine, b => Name(b.Kind).StartsWith("Pallet"));
            Assert.Contains(mine, b => Name(b.Kind).StartsWith("CardboardBoxPile"));
            // ...in one corner, not a row along the whole back of the lot
            var service = mine.Where(b => b.Kind == 17 || b.Kind == 18 || (b.Kind >= 29 && b.Kind < 40)).ToList();
            float spread = service.Max(x => service.Max(y => math.distance(x.Position, y.Position)));
            Assert.True(spread < 20f, $"the service corner spreads over {spread:F1} m");
            Assert.True(service.All(x => math.abs(x.Position.x) > 3f) && (service.All(x => x.Position.x > 0f) || service.All(x => x.Position.x < 0f)), "the service corner sits to one side");
            // the overflow car park: marked bays, most with a parked car or van, each car on a bay
            var bays = mine.Where(b => Name(b.Kind) == "ParkingLotDecal01").ToList();
            var cars = mine.Where(b => Name(b.Kind).StartsWith("Placemaker Vehicle")).ToList();
            Assert.True(bays.Count >= 8, $"{bays.Count} bays");
            Assert.True(cars.Count >= bays.Count / 3 && cars.Count < bays.Count, $"{cars.Count} cars on {bays.Count} bays");
            foreach (var c in cars) Assert.Contains(bays, b => math.distance(b.Position, c.Position) < 0.5f);
            // a car's nose to the aisle, its bay's mark the other way (the mark's closed end at the back, as the parking lots lay it)
            foreach (var c in cars)
            {
                var bay = bays.First(b => math.distance(b.Position, c.Position) < 0.5f);
                Assert.True(math.abs(math.cos(bay.Yaw - c.Yaw) + 1f) < 1e-3f, $"bay mark at {bay.Position} turned {math.degrees(bay.Yaw - c.Yaw):F0}° from its car");
            }
            // neighbours' marks share a side line (3.2 m apart, the mark's width)
            Assert.Contains(bays, b => bays.Any(o => math.abs(math.distance(b.Position, o.Position) - 3.2f) < 0.02f));
            // nothing on the shop's own lot or outside its plot, and nothing on top of anything else
            var placed = mine.Where(b => b.Kind >= 29 && !Name(b.Kind).StartsWith("Placemaker Vehicle") && !Name(b.Kind).Contains("Tree")).ToList();
            foreach (var b in placed)
            {
                var size = Name(b.Kind) == "ParkingLotDecal01" ? new float2(1.6f, 3.1f) : new float2(0.5f, 0.5f);
                var box = Box(b.Position, size * 0.95f, b.Yaw);
                Assert.True(box.All(p => Poly.Contains(plot, p)), $"{Name(b.Kind)} at {b.Position} outside the plot");
                Assert.False(Overlap(box, lot), $"{Name(b.Kind)} at {b.Position} on the shop's lot");
            }
            for (int i = 0; i < bays.Count; i++)
                for (int j = i + 1; j < bays.Count; j++)
                    Assert.False(Overlap(Box(bays[i].Position, new float2(1.6f, 3f) * 0.95f, bays[i].Yaw), Box(bays[j].Position, new float2(1.6f, 3f) * 0.95f, bays[j].Yaw)), $"bays at {bays[i].Position} and {bays[j].Position} overlap");
            foreach (var bin in bins)
                Assert.DoesNotContain(bays, b => Overlap(Box(b.Position, new float2(1.6f, 3f), b.Yaw), Box(bin.Position, new float2(1.3f, 0.6f), bin.Yaw)));
            // further out: no concrete, painted green
            var farPoint = new float2(0f, -16f - 42f);
            Assert.True(Poly.Contains(plot, farPoint), $"the plot reaches 42 m behind the lot ({plot.Min(p => p.y):F1})");
            Assert.DoesNotContain(plan.Paths, p => p.House == 1 && p.Surface == "Concrete Surface 01" && Poly.Contains(p.Ring, farPoint));
            Assert.Contains(plan.Paints, p => Poly.Contains(p.Ring.Select(q => q.xz).ToArray(), farPoint));
            Assert.Contains(plan.Paths, p => p.House == 1 && p.Surface == "Concrete Surface 01");
            Assert.Contains(plan.Notes, n => n.Contains("shop yard"));

            // the same land plans the same yard (no flicker)
            var again = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.Equal(plan.Bushes.Select(b => (b.Kind, b.Position, b.Yaw)), again.Bushes.Select(b => (b.Kind, b.Position, b.Yaw)));
        }

        // A row of shops along a street (40 x 32 m lots, car park in front) back to back with a row of offices: every lot a
        // car park look, as Global outgrow gives them by default.
        private static List<StitchHouse> ShopsAndOffices()
        {
            var list = new List<StitchHouse>();
            for (int k = 0; k < 4; k++)
            {
                var shop = new StitchHouse { Id = 1 + k, Position = new float2(k * 40f, 0f), Forward = new float2(0f, 1f), HalfLot = new float2(20f, 16f), MainSurface = "Concrete Surface 01", Zone = "shop", Theme = "tidy" };
                shop.Footprint = new[] { shop.ToWorld(new float2(-14f, -14f)), shop.ToWorld(new float2(14f, -14f)), shop.ToWorld(new float2(14f, 2f)), shop.ToWorld(new float2(-14f, 2f)) };
                shop.Hard.Add(new[] { shop.ToWorld(new float2(-19f, 3f)), shop.ToWorld(new float2(19f, 3f)), shop.ToWorld(new float2(19f, 15.5f)), shop.ToWorld(new float2(-19f, 15.5f)) });
                shop.Options = PolishLooks.BuiltIn.Single(l => l.Id == "shops").Values();
                list.Add(shop);
                var office = new StitchHouse { Id = 11 + k, Position = new float2(k * 40f, -16f - 22f - 16f), Forward = new float2(0f, -1f), HalfLot = new float2(20f, 16f), MainSurface = "Pavement Surface 01", Zone = "office", Theme = "tidy" };
                office.Footprint = new[] { office.ToWorld(new float2(-12f, -12f)), office.ToWorld(new float2(12f, -12f)), office.ToWorld(new float2(12f, 4f)), office.ToWorld(new float2(-12f, 4f)) };
                office.Options = PolishLooks.BuiltIn.Single(l => l.Id == "offices").Values();
                list.Add(office);
            }
            return list;
        }

        /// <summary>A house as a later pass sees it: edited, or fixed with the garden an earlier plan wrote (its lot when that
        /// plan gave it none), as the game layer hands them over.</summary>
        private static StitchHouse Planned(StitchHouse h, bool edit, StitchPlan earlier) => new StitchHouse
        {
            Id = h.Id, Position = h.Position, Forward = h.Forward, HalfLot = h.HalfLot, Lines = h.Lines, Lawns = h.Lawns, Hard = h.Hard, Footprint = h.Footprint, Props = h.Props,
            MainSurface = h.MainSurface, Zone = h.Zone, Theme = h.Theme, Options = h.Options, Edit = edit,
            Garden = edit ? null : earlier.Plots.TryGetValue(h.Id, out var g) ? g : new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft },
            // its closing pieces as they stand (NeighbourPolishSystem.StandingOf)
            Standing = edit ? null : earlier.FencePieces.Where(p => p.House == h.Id).Select(p => { var d = new float2(math.sin(p.Yaw), math.cos(p.Yaw)) * 1.95f; return (p.Position - d, p.Position + d); }).ToList(),
        };

        private static string Diff(List<string> a, List<string> b) =>
            "only before: " + string.Join(", ", a.Except(b)) + " | only after: " + string.Join(", ", b.Except(a));

        private static List<string> PiecesOf(StitchPlan plan, long id) =>
            plan.Paths.Where(p => p.House == id).Select(p => $"{p.Surface} {Poly.Area(p.Ring):F0}")
                .Concat(plan.Bushes.Where(b => b.House == id).Select(b => $"{YardStitch.BushKinds[b.Kind]} {b.Position.x:F1},{b.Position.y:F1}"))
                .Concat(plan.Beds.Where(b => b.House == id).Select(b => $"bed {Poly.Area(b.Ring):F0}"))
                .Concat(plan.FencePieces.Where(p => p.House == id).Select(p => $"{p.Name} {p.Position.x:F1},{p.Position.y:F1}"))
                .Concat(plan.NewLawns.Where(l => l.House == id).Select(l => $"lawn {Poly.Area(l.Ring):F0}")).OrderBy(x => x).ToList();

        [Fact]
        public void A_shop_re_planned_as_a_neighbour_of_another_pass_keeps_its_yard()
        {
            // its own pass: every lot edited (Outgrow all, the brush); the yards are written and kept with the lots
            var all = ShopsAndOffices();
            var first = YardStitch.Plan(all, MiddleLine());
            var yard = PiecesOf(first, 2);
            Assert.True(yard.Count > 10, "shop 2 gets a yard: " + string.Join(" | ", first.Notes.Where(n => n.StartsWith("2:"))));
            // then a pass for shop 1 next door: shop 2 is in its core (re-planned), the rest are fixed with their gardens, in
            // another order (the game gathers a pass's houses from a set)
            var second = YardStitch.Plan(all.AsEnumerable().Reverse().Select(h => Planned(h, h.Id == 1 || h.Id == 2, first)).ToList(), MiddleLine());
            var again = PiecesOf(second, 2);
            Assert.True(yard.SequenceEqual(again), Diff(yard, again));
            var one = PiecesOf(second, 1);
            Assert.True(PiecesOf(first, 1).SequenceEqual(one), Diff(PiecesOf(first, 1), one));
        }

        private static float DistanceToRing(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++) best = math.min(best, DistanceToSegment(p, ring[i], ring[(i + 1) % ring.Length]));
            return best;
        }

        [Fact]
        public void A_staircase_shore_becomes_a_smooth_line()
        {
            // water sampled every 4 m along a diagonal shore: wet where the cell's middle lies past x + z = 244
            const int n = 60;
            var wet = new byte[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++) wet[j * n + i] = (byte)(4 * i + 2 + 4 * j + 2 > 244 ? 1 : 0);
            var rings = WaterEdge.Outlines(wet, n, n, float2.zero, 4f, n, n);
            Assert.Single(rings);
            Assert.True(Poly.IsCcw(rings[0]));
            // along the shore (away from the sampled box's edges) the outline is one straight line, not 4 m steps
            var shore = Poly.Resample(rings[0], 1f).Where(p => p.x > 30f && p.y > 30f && p.x < 210f && p.y < 210f).ToList();
            Assert.True(shore.Count > 100, $"{shore.Count} shore points");
            float mean = shore.Average(p => p.x + p.y);
            float worst = shore.Max(p => math.abs(p.x + p.y - mean)) / math.sqrt(2f);
            Assert.True(worst < 0.3f, $"the shore wanders {worst:F2} m off a straight line (the 4 m staircase: 1.4 m)");
            Assert.True(math.abs(mean - 246f) < 2f, $"the shore lies where the water is ({mean:F1})");
        }

        [Fact]
        public void A_garden_keeps_its_water_gap_off_the_water()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 80f };
            // a lake 50 m behind the lot's back
            var lake = Strip(-300f, -400f, 300f, -74f);
            var withLake = MiddleLine().WithBarriers(new List<float2[]> { lake }, new List<int> { 2 });
            var plan = YardStitch.Plan(new[] { a }, withLake);
            Assert.True(plan.Plots.TryGetValue(1, out var pa), "the garden grows");
            foreach (var p in pa) Assert.True(!Poly.Contains(lake, p) && DistanceToRing(lake, p) > 29.5f, $"a plot corner {p} is {DistanceToRing(lake, p):F1} m from the water");
            Assert.True(pa.Min(p => p.y) < -42f, $"it still grows toward the water, up to its gap ({pa.Min(p => p.y):F1})");
            // a look with a smaller gap comes nearer
            a.Options["waterGap"] = 10f;
            var near = YardStitch.Plan(new[] { a }, withLake);
            Assert.True(near.Plots.TryGetValue(1, out var pn) && pn.Min(p => p.y) < -60f && pn.All(p => DistanceToRing(lake, p) > 9.5f), $"10 m gap: {pn?.Min(p => p.y):F1}");
        }

        [Fact]
        public void A_car_park_yard_takes_its_style_from_its_type_whatever_the_building()
        {
            string Name(int k) => YardStitch.BushKinds[k];
            (StitchPlan Plan, string Note) Yard(string zone, float style = 0f)
            {
                var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
                a.Zone = zone;
                a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 50f, ["fill"] = PolishLooks.FillCarPark, ["parkStyle"] = style };
                var plan = YardStitch.Plan(new[] { a }, MiddleLine());
                return (plan, plan.Notes.FirstOrDefault(n => n.Contains("shop yard")) ?? "");
            }
            var works = Yard("industry", PolishLooks.ParkIndustrial);
            Assert.Contains("(industrial,", works.Note);
            Assert.Contains(works.Plan.Paths, p => p.House == 1 && p.Surface == "Concrete Surface 01");
            Assert.DoesNotContain(works.Plan.Paths, p => p.Surface == "Grass Surface 01");
            Assert.DoesNotContain(works.Plan.Bushes, b => Name(b.Kind).EndsWith("Tree01"));
            // the same style on any building; an old "by the building" (0) is everyday
            Assert.Contains("(clean,", Yard("industry", PolishLooks.ParkClean).Note);
            Assert.Contains("(clean,", Yard("home", PolishLooks.ParkClean).Note);
            Assert.Contains("(industrial,", Yard("home", PolishLooks.ParkIndustrial).Note);
            Assert.Contains("(everyday,", Yard("office").Note);
            Assert.Contains("(everyday,", Yard("industry").Note);
            // the bigger homes, offices and works park by default
            foreach (var id in new[] { "apartments", "towers", "mixed", "offices", "officeTowers", "industry", "shops", "bigShops" })
                Assert.Equal(PolishLooks.FillCarPark, PolishLooks.BuiltIn.Single(l => l.Id == id).Values()["fill"]);
            Assert.Equal(PolishLooks.FillGarden, PolishLooks.BuiltIn.Single(l => l.Id == "houses").Values()["fill"]);
            // the planting styles are gardens, whatever building they are given to; each car park type names its style
            foreach (var id in new[] { "garden", "natural", "tidy", "yard", "meadow", "rustic", "coastal", "manicured" })
                Assert.Equal(PolishLooks.FillGarden, PolishLooks.BuiltIn.Single(l => l.Id == id).Values()["fill"]);
            Assert.Equal(PolishLooks.ParkIndustrial, PolishLooks.BuiltIn.Single(l => l.Id == "industry").Values()["parkStyle"]);
            Assert.Equal(PolishLooks.ParkClean, PolishLooks.BuiltIn.Single(l => l.Id == "offices").Values()["parkStyle"]);
            Assert.Equal(PolishLooks.ParkEveryday, PolishLooks.BuiltIn.Single(l => l.Id == "shops").Values()["parkStyle"]);
            // an old type saved with "by the building" reads as a garden and an everyday car park
            var old = new PolishLook { Id = "my-old", Custom = true, Theme = "garden", Knobs = { ["fill"] = PolishLooks.FillAuto, ["parkStyle"] = PolishLooks.ParkAuto } };
            Assert.Equal(PolishLooks.FillGarden, old.Values()["fill"]);
            Assert.Equal(PolishLooks.ParkEveryday, old.Values()["parkStyle"]);
        }

        [Fact]
        public void A_car_park_yards_edge_and_land_further_out_follow_its_type()
        {
            string Name(int k) => YardStitch.BushKinds[k];
            StitchPlan Yard(string zone, Dictionary<string, float> extra)
            {
                var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
                a.Zone = zone;
                a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 120f, ["fill"] = PolishLooks.FillCarPark, ["parkReach"] = 20f };
                foreach (var kv in extra) a.Options[kv.Key] = kv.Value;
                return YardStitch.Plan(new[] { a }, MiddleLine());
            }
            // a works: worn edge (dirt painted out from the paving), waste ground further out, no trees at all
            var works = Yard("industry", new Dictionary<string, float> { ["parkStyle"] = PolishLooks.ParkIndustrial });
            string note = works.Notes.First(n => n.Contains("shop yard"));
            Assert.Contains("worn edge", note);
            Assert.Contains("waste ground", note);
            Assert.Contains(works.Paints, p => p.Layer == 3);
            Assert.DoesNotContain(works.Bushes, b => Name(b.Kind).EndsWith("Tree01"));
            Assert.Contains(works.Bushes, b => Name(b.Kind).StartsWith("RubblePile") || Name(b.Kind).StartsWith("Barrel"));
            // no worn edge: a grass strip
            var grassy = Yard("shop", new Dictionary<string, float> { ["parkWorn"] = 0f });
            Assert.Contains("grass edge", grassy.Notes.First(n => n.Contains("shop yard")));
            Assert.Contains(grassy.Paths, p => p.Surface == "Grass Surface 01");
            // always worn: no grass strip
            var worn = Yard("shop", new Dictionary<string, float> { ["parkWorn"] = 100f });
            Assert.Contains("worn edge", worn.Notes.First(n => n.Contains("shop yard")));
            Assert.DoesNotContain(worn.Paths, p => p.Surface == "Grass Surface 01");
            // the land further out takes the planting style's things: tidy benches and flower beds, natural logs
            var tidy = YardStitch.Plan(new[] { WithTheme(Yard("office", new Dictionary<string, float>()), "tidy") }, MiddleLine());
            Assert.Contains(tidy.Bushes, b => Name(b.Kind).StartsWith("GardenBench") || Name(b.Kind) == "Flowerbed01");
            var natural = YardStitch.Plan(new[] { WithTheme(Yard("office", new Dictionary<string, float>()), "natural") }, MiddleLine());
            Assert.Contains(natural.Bushes, b => Name(b.Kind).StartsWith("Log"));
            // no cars at 0 %
            var empty = Yard("shop", new Dictionary<string, float> { ["parkCars"] = 0f, ["parkReach"] = 200f });
            Assert.Contains(empty.Bushes, b => Name(b.Kind) == "ParkingLotDecal01");
            Assert.DoesNotContain(empty.Bushes, b => Name(b.Kind).StartsWith("Placemaker Vehicle"));
        }

        /// <summary>The house the last plan was made for, again, with this planting style.</summary>
        private static StitchHouse WithTheme(StitchPlan _, string theme)
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Zone = "office";
            a.Theme = theme;
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 120f, ["fill"] = PolishLooks.FillCarPark, ["parkReach"] = 20f };
            return a;
        }

        [Fact]
        public void Any_look_goes_on_any_building_a_house_can_have_a_car_park_and_a_shop_a_garden()
        {
            string Name(int k) => YardStitch.BushKinds[k];
            // a house whose type makes its land a clean car park yard: paving, marked bays, parked cars; only bins in its corner
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f));
            a.Zone = "home";
            a.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 50f, ["fill"] = PolishLooks.FillCarPark, ["parkStyle"] = PolishLooks.ParkClean };
            var plan = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.Contains(plan.Paths, p => p.House == 1 && p.Surface == "Pavement Surface 01");
            Assert.Contains(plan.Bushes, b => b.House == 1 && Name(b.Kind) == "ParkingLotDecal01");
            Assert.Contains(plan.Bushes, b => b.House == 1 && Name(b.Kind).StartsWith("Placemaker Vehicle"));
            Assert.DoesNotContain(plan.Bushes, b => b.House == 1 && (Name(b.Kind).StartsWith("Pallet") || Name(b.Kind).StartsWith("CardboardBoxPile") || Name(b.Kind).StartsWith("GasBottle")));
            Assert.False(plan.Lawns.ContainsKey(15L), "its own lawn is not grown over the car park");
            Assert.Empty(plan.Beds);

            // the same house as paving: one slab to the fence, no car park
            a.Options["fill"] = PolishLooks.FillPaved;
            var paved = YardStitch.Plan(new[] { a }, MiddleLine());
            Assert.Contains(paved.Paths, p => p.House == 1 && p.Surface == "Pavement Surface 01");
            Assert.DoesNotContain(paved.Bushes, b => b.Kind >= 29);

            // a shop whose look makes its land a garden: lawn, no paving past its lot, no car park
            var b2 = House(2, new float2(0f, 0f), new float2(0f, 1f));
            b2.MainSurface = "Concrete Surface 01";
            b2.Zone = "shop";
            b2.Options = new Dictionary<string, float>(PolishLooks.Defaults) { ["openReach"] = 50f, ["fill"] = PolishLooks.FillGarden, ["paintedGardens"] = 0f };
            var garden = YardStitch.Plan(new[] { b2 }, MiddleLine());
            Assert.DoesNotContain(garden.Paths, p => p.House == 2 && p.Surface == "Concrete Surface 01");
            Assert.True(garden.Lawns.ContainsKey(25L) || garden.NewLawns.Any(l => l.House == 2), "the shop's land is a lawn: " + string.Join(" | ", garden.Notes));
            Assert.DoesNotContain(garden.Bushes, x => x.Kind >= 29);
            // an old "by the building" (fill 0) is a garden now: the type alone decides, never the building
            b2.Options["fill"] = PolishLooks.FillAuto;
            var auto = YardStitch.Plan(new[] { b2 }, MiddleLine());
            Assert.DoesNotContain(auto.Bushes, x => x.Kind >= 29);
        }

        [Fact]
        public void A_built_in_look_the_player_changed_is_kept_beside_their_own_looks()
        {
            var mine = new PolishLook { Id = "my-big", Name = "Big", Custom = true, Theme = "tidy", Color = "#123456", Knobs = { ["sideReach"] = 200f } };
            var garden = new PolishLook { Id = "garden", Name = "Placemaker.UI.Painter.Theme.garden", Edited = true, Theme = "natural", Knobs = { ["fill"] = PolishLooks.FillCarPark, ["waterGap"] = 60f } };
            var json = PolishLooks.Write(new[] { mine }, new[] { garden });
            var own = PolishLooks.Read(json);
            Assert.Single(own);
            Assert.Equal("my-big", own[0].Id);
            Assert.Equal("#123456", own[0].Color);
            Assert.Equal("#123456", PolishLooks.ColorOf(own[0]));
            Assert.Equal(PolishLooks.DefaultColors["houses"], PolishLooks.ColorOf(PolishLooks.BuiltIn.Single(l => l.Id == "houses")));
            var edits = PolishLooks.ReadEdits(json);
            Assert.Single(edits);
            Assert.True(edits[0].Edited && !edits[0].Custom);
            Assert.Equal("garden", edits[0].Id);
            Assert.Equal("Placemaker.UI.Painter.Theme.garden", edits[0].Name);
            Assert.Equal("natural", edits[0].Theme);
            Assert.Equal(PolishLooks.FillCarPark, edits[0].Values()["fill"]);
            Assert.Equal(60f, edits[0].Values()["waterGap"]);
            // an edit of a look that is not built in is never read
            Assert.Empty(PolishLooks.ReadEdits("{\"edits\":[{\"id\":\"my-big\",\"theme\":\"tidy\",\"knobs\":{}}]}"));
            // a file without edits (older) reads as before
            Assert.Empty(PolishLooks.ReadEdits(PolishLooks.Write(new[] { mine })));
        }

        [Fact]
        public void Every_zone_type_gets_a_look_of_its_own()
        {
            Assert.Equal("houses", PolishLooks.DefaultZoneLook("residential low", "home"));
            Assert.Equal("rows", PolishLooks.DefaultZoneLook("residential medium row", "home"));
            Assert.Equal("apartments", PolishLooks.DefaultZoneLook("residential medium", "home"));
            Assert.Equal("apartments", PolishLooks.DefaultZoneLook("residential lowrent", "home"));
            Assert.Equal("towers", PolishLooks.DefaultZoneLook("residential high", "home"));
            Assert.Equal("mixed", PolishLooks.DefaultZoneLook("residential mixed", "home"));
            Assert.Equal("shops", PolishLooks.DefaultZoneLook("commercial low", "shop"));
            Assert.Equal("bigShops", PolishLooks.DefaultZoneLook("commercial high", "shop"));
            Assert.Equal("offices", PolishLooks.DefaultZoneLook("office low", "office"));
            Assert.Equal("officeTowers", PolishLooks.DefaultZoneLook("office high", "office"));
            Assert.Equal("industry", PolishLooks.DefaultZoneLook("industrial manufacturing", "industry"));
            Assert.Equal("houses", PolishLooks.DefaultZoneLook("", "home"));
            // every one of them is a built-in look, and says what its land becomes
            foreach (var id in PolishLooks.ZoneLookIds)
            {
                var look = PolishLooks.BuiltIn.Single(l => l.Id == id);
                Assert.True(look.Values()["fill"] >= PolishLooks.FillGarden, id);
            }
        }
    }
}
