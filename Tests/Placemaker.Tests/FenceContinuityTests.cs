// Outgrow's fences as the player sees them (the user, 21 Oct: "fence continuity errors ... crossover errors", gardens
// through each other on curves): no gaps where fences should meet, no fence across another, no garden over another
// house's lot. FenceCheck (FenceContinuity.cs) finds what the game runner's fenceMap finds, on a plan.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class FenceContinuityTests
    {
        private static StitchHouse House(long id, float2 pos, float2 forward, float2 half, string right = "FenceResidentialHigh01", string left = "HedgeLow01")
        {
            var h = new StitchHouse { Id = id, Position = pos, Forward = math.normalize(forward), HalfLot = half, Options = new Dictionary<string, float>(PolishLooks.Defaults) };
            h.Lines.Add(new StitchLine { Id = id * 10 + 1, Name = right, A = h.ToWorld(new float2(half.x - 0.1f, half.y - 0.1f)), B = h.ToWorld(new float2(half.x - 0.1f, -half.y + 0.1f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 2, Name = left, A = h.ToWorld(new float2(-half.x + 0.7f, half.y - 0.1f)), B = h.ToWorld(new float2(-half.x + 0.7f, -half.y * 0.5f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 3, Name = right, A = h.ToWorld(new float2(half.x - 0.1f, -half.y + 0.1f)), B = h.ToWorld(new float2(-half.x + 0.1f, -half.y + 0.1f)) });
            return h;
        }

        private static float2[] Lot(StitchHouse h) => new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };

        private static float Overlap(float2[] a, float2[] b) =>
            Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(a.ToArray()) }, new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(b.ToArray()) }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area);

        private static float2[] Circle(float2 c, float r, int n = 24) =>
            Enumerable.Range(0, n).Select(k => c + r * new float2(math.cos(k * 2f * math.PI / n), math.sin(k * 2f * math.PI / n))).ToArray();

        [Fact]
        public void A_big_garden_is_closed_all_round_with_no_gap_at_its_corners()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f), new float2(8f, 24f));
            a.Options["openReach"] = 40f;
            var s = new StitchSettings();
            var plan = YardStitch.Plan(new[] { a }, s);
            var r = FenceCheck.Of(new[] { a }, plan, s);
            Assert.True(r.Gaps.Count == 0 && r.Crossings.Count == 0, r.ToString() + ": " + string.Join("; ", r.Gaps.Select(g => $"{g.Kind} {g.Len:F1} at {g.At}")));
            // each back corner of the garden has a fence end on it (the fence turns there)
            var plot = plan.Plots[1];
            var ends = FenceCheck.Drawn(new[] { a }, plan).SelectMany(b => new[] { b.A, b.B }).ToList();
            foreach (var corner in plot.Where(p => p.y < -30f))
                Assert.True(ends.Any(e => math.distance(e, corner) < 0.3f), $"no fence end at the corner {corner}");
        }

        [Fact]
        public void A_garden_along_a_turning_circle_follows_it_with_no_gaps()
        {
            // a turning circle (another street's) behind the house: the garden reaches it and hugs its curve
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f), new float2(8f, 24f));
            var circle = Circle(new float2(4f, -52f), 14f);
            var s = new StitchSettings().WithBarriers(new List<float2[]> { circle });
            var plan = YardStitch.Plan(new[] { a }, s);
            Assert.True(plan.Plots.TryGetValue(1, out var plot), "the garden grows to the circle");
            var r = FenceCheck.Of(new[] { a }, plan, s);
            Assert.True(r.Gaps.Count == 0 && r.Crossings.Count == 0, r.ToString() + ": " + string.Join("; ", r.Gaps.Select(g => $"{g.Kind} {g.Len:F1} at {g.At}")));
            // no piece stands on the circle
            foreach (var p in plan.FencePieces) Assert.False(Poly.Contains(circle, p.Position), $"a piece on the road at {p.Position}");
        }

        [Fact]
        public void A_closing_run_stops_at_a_driveway_instead_of_leaving_a_whole_piece_out()
        {
            var a = House(1, new float2(0f, 0f), new float2(0f, 1f), new float2(8f, 24f));
            a.Options["openReach"] = 30f;
            // a drive across the garden's left side, 5 m wide, out to the left
            a.Hard.Add(new[] { new float2(-60f, -10f), new float2(-6f, -10f), new float2(-6f, -5f), new float2(-60f, -5f) });
            var s = new StitchSettings();
            var plan = YardStitch.Plan(new[] { a }, s);
            var plot = plan.Plots[1];
            float left = plot.Min(p => p.x);
            // the pieces along the left side stop at the drive (30 cm in from its edges) on both sides of it, never on it
            var side = plan.FencePieces.Where(p => math.abs(p.Position.x - left) < 0.5f).ToList();
            Assert.NotEmpty(side);
            var spans = side.Select(p => { var d = new float2(math.sin(p.Yaw), math.cos(p.Yaw)) * ((p.Name.Contains("Short") ? 2f : 4f) * 0.5f); return (Lo: math.min(p.Position.y - d.y, p.Position.y + d.y), Hi: math.max(p.Position.y - d.y, p.Position.y + d.y)); }).ToList();
            Assert.DoesNotContain(spans, x => x.Lo < -6f && x.Hi > -9f);
            Assert.Contains(spans, x => x.Hi > -10.6f && x.Hi < -9.2f);
            Assert.Contains(spans, x => x.Lo < -4.4f && x.Lo > -5.8f);
        }

        [Fact]
        public void Houses_at_an_angle_on_a_curve_never_get_gardens_over_each_others_lots()
        {
            // three houses on the outside of a curve in the user's city (Brecon): the middle line between the small one and
            // the big one behind it ran 92 m² into the big one's lot, and the next pass planned that garden again, for ever
            var big = House(611, new float2(1495.02f, -936.25f), new float2(0.86f, -0.5f), new float2(16f, 24f));
            var small = House(203, new float2(1519.94f, -914.66f), new float2(0.99f, -0.11f), new float2(12f, 8f));
            var deep = House(803, new float2(1500.47f, -891.51f), new float2(0.94f, 0.35f), new float2(8f, 24f));
            var houses = new[] { big, small, deep };
            var s = new StitchSettings();
            var plan = YardStitch.Plan(houses, s);
            foreach (var h in houses)
            {
                if (!plan.Plots.TryGetValue(h.Id, out var plot)) continue;
                foreach (var o in houses.Where(o => o != h))
                    Assert.True(Overlap(plot, Lot(o)) < 1f, $"{h.Id}'s garden lies {Overlap(plot, Lot(o)):F0} m² over {o.Id}'s lot");
                foreach (var o in houses.Where(o => o.Id > h.Id && plan.Plots.ContainsKey(o.Id)))
                    Assert.True(Overlap(plot, plan.Plots[o.Id]) < 1f, $"the gardens of {h.Id} and {o.Id} lie {Overlap(plot, plan.Plots[o.Id]):F0} m² over each other");
            }
        }

        [Fact]
        public void Where_the_street_curves_away_car_parks_reach_it_in_front_of_their_lots_and_gardens_do_not()
        {
            // three lots in a row on a curve: the street touches the middle one's front and curves away from the outer ones
            // (the user's car parks on a curve: the triangles between their fronts and the kerb were nobody's)
            var kerb = Enumerable.Range(0, 41).Select(k => { float x = -40f + 2f * k; return new float2(x, 24.2f + x * x / 120f); }).ToList();
            var road = kerb.Concat(new[] { new float2(40f, 60f), new float2(-40f, 60f) }).ToArray();
            foreach (bool carPark in new[] { true, false })
            {
                var row = new[] { -16f, 0f, 16f }.Select((x, k) => House(k + 1, new float2(x, 0f), new float2(0f, 1f), new float2(8f, 24f))).ToArray();
                foreach (var h in row) h.Options["fill"] = carPark ? PolishLooks.FillCarPark : PolishLooks.FillGarden;
                var s = new StitchSettings().WithBarriers(new List<float2[]> { road });
                var plan = YardStitch.Plan(row, s);
                Assert.True(plan.Plots.TryGetValue(3, out var plot), "the outer lot is given land");
                Assert.True(Overlap(plot, road) < 0.5f, $"its land lies {Overlap(plot, road):F1} m² on the street");
                // in front of the outer lot's front line (y 24; the kerb 26-29 m there): a car park reaches the kerb, a garden
                // keeps to its lot's front line (its side fences carried forward met the neighbours' with gaps)
                float front = plot.Where(p => p.x > 9f).Select(p => p.y).DefaultIfEmpty(0f).Max();
                if (carPark) Assert.True(front > 25.5f, $"the car park stops at {front:F1} m in front (the lot's front line is at 24): " + string.Join(" ", plot.Select(p => $"({p.x:F0},{p.y:F0})")));
                else Assert.True(front < 24.5f, $"the garden reaches {front:F1} m in front of its lot");
                // no fence runs along the street
                Assert.DoesNotContain(plan.FencePieces, p => p.Position.y > 24.5f && math.abs(math.sin(p.Yaw)) > 0.7f);
            }
        }

        [Fact]
        public void A_short_piece_finishes_a_fence_run_where_the_kind_has_one()
        {
            Assert.Equal("FenceResidentialPieceLowShort01", YardStitch.ShortPiece("FenceResidentialPieceLow01"));
            Assert.Equal("NA_FenceResidentialPieceHighShort03", YardStitch.ShortPiece("NA_FenceResidentialPieceHigh03"));
            Assert.Null(YardStitch.ShortPiece("HedgePieceLow01"));
        }
    }
}
