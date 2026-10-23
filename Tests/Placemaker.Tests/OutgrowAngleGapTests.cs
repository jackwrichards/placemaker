using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    /// <summary>
    /// Works on a block with a street cutting one corner at 45 degrees, every reach at its maximum (the user, 22 Oct: "a
    /// nice, clean connection, no jumbledness, no gaps" between neighbouring industrial lots of a type set to Paved): the
    /// land between outgrown lots is somebody's, whatever the angle they stand at (YardStitch.ShareBetween).
    /// PM_GAP_PNG=&lt;folder&gt; draws each plan (GapPicture).
    /// </summary>
    public class OutgrowAngleGapTests
    {
        private readonly ITestOutputHelper _out;
        public OutgrowAngleGapTests(ITestOutputHelper output) => _out = output;

        private static StitchHouse Works(long id, float2 pos, float2 forward, float2 half, Dictionary<string, float> options)
        {
            var h = new StitchHouse { Id = id, Position = pos, Forward = math.normalize(forward), HalfLot = half, Options = options, MainSurface = "Pavement Surface 01" };
            h.Lines.Add(new StitchLine { Id = id * 10 + 1, Name = "FenceIndustrial01", A = h.ToWorld(new float2(half.x - 0.1f, half.y - 0.1f)), B = h.ToWorld(new float2(half.x - 0.1f, -half.y + 0.1f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 2, Name = "FenceIndustrial01", A = h.ToWorld(new float2(-half.x + 0.1f, half.y - 0.1f)), B = h.ToWorld(new float2(-half.x + 0.1f, -half.y + 0.1f)) });
            h.Lines.Add(new StitchLine { Id = id * 10 + 3, Name = "FenceIndustrial01", A = h.ToWorld(new float2(half.x - 0.1f, -half.y + 0.1f)), B = h.ToWorld(new float2(-half.x + 0.1f, -half.y + 0.1f)) });
            return h;
        }

        private static float2[] Road(float2 a, float2 b, float halfWidth)
        {
            var d = math.normalize(b - a);
            var n = new float2(-d.y, d.x) * halfWidth;
            return new[] { a - n, b - n, b + n, a + n };
        }

        private static float2[] Ccw(float2[] r) => Poly.IsCcw(r) ? r : r.Reverse().ToArray();

        private static float2[] LotOf(StitchHouse h) => new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };

        private static float Shared(float2[] a, float2[] b) =>
            Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(a) }, new[] { (IReadOnlyList<float2>)Ccw(b) }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area);

        /// <summary>The user's block: streets all round (Pleasant Street along the bottom, Highland Street cutting the top right
        /// corner at 45 degrees), works of a few sizes on the inside facing each street, their backs and sides meeting at right
        /// angles and at 45 degrees.</summary>
        private static (List<StitchHouse> Houses, List<float2[]> Roads, float2[] Inside) Block(Dictionary<string, float> options)
        {
            var corners = new[] { new float2(-130f, -100f), new float2(130f, -100f), new float2(130f, 40f), new float2(40f, 130f), new float2(-130f, 130f) };
            var roads = new List<float2[]>();
            for (int k = 0; k < corners.Length; k++)
            {
                var p = corners[k];
                var q = corners[(k + 1) % corners.Length];
                var d = math.normalize(q - p);
                roads.Add(Road(p - d * 6f, q + d * 6f, 6f));
            }
            var inside = Poly.Offset(corners, 6f).OrderByDescending(r => Poly.Area(r)).First();
            var room = Poly.Offset(inside, -0.5f).OrderByDescending(r => Poly.Area(r)).First();
            var houses = new List<StitchHouse>();
            float2[] sizes = { new float2(16f, 24f), new float2(24f, 28f), new float2(20f, 20f), new float2(28f, 32f), new float2(16f, 28f) };
            int pick = 0;
            // (Highland Street's works first: the corners go to them)
            foreach (int k in new[] { 2, 0, 1, 3, 4 })
            {
                var p = corners[k];
                var q = corners[(k + 1) % corners.Length];
                var dir = math.normalize(q - p);
                var outward = new float2(dir.y, -dir.x);
                float len = math.distance(p, q);
                for (float s = 6f; s < len - 6f;)
                {
                    var half = sizes[pick++ % sizes.Length];
                    var h = Works(houses.Count + 1, p + dir * (s + half.x) - outward * (6f + half.y), outward, half, options);
                    s += 2f * half.x;
                    var lot = LotOf(h);
                    if (lot.Any(c => !Poly.Contains(room, c)) || houses.Any(o => Shared(lot, LotOf(o)) > 1f) || roads.Any(r => Shared(lot, r) > 1f)) continue;
                    houses.Add(h);
                }
            }
            return (houses, roads, inside);
        }

        /// <summary>Land inside the block, <paramref name="margin"/> in from the kerbs, that is nobody's (no lot, plot or
        /// road), in m², and one spot of it.</summary>
        private static float Bare(IReadOnlyList<StitchHouse> houses, StitchPlan plan, List<float2[]> roads, float2[] inside, float margin, out float2 spot)
        {
            var lots = houses.Select(LotOf).ToList();
            var plots = houses.Select(h => plan.Plots.TryGetValue(h.Id, out var p) ? p : null).Where(p => p != null).ToList();
            var shrunk = Poly.Offset(inside, margin).OrderByDescending(r => Poly.Area(r)).First();
            Poly.Bounds(shrunk, out var lo, out var hi);
            float area = 0f;
            spot = float2.zero;
            for (float x = lo.x; x <= hi.x; x += 1f)
                for (float y = lo.y; y <= hi.y; y += 1f)
                {
                    var p = new float2(x + 0.5f, y + 0.5f);
                    if (!Poly.Contains(shrunk, p) || lots.Any(l => Poly.Contains(l, p)) || plots.Any(l => Poly.Contains(l, p)) || roads.Any(r => Poly.Contains(r, p))) continue;
                    area += 1f;
                    spot = p;
                }
            return area;
        }

        [Theory]
        [InlineData(PolishLooks.FillGarden)]
        [InlineData(PolishLooks.FillCarPark)]
        [InlineData(PolishLooks.FillPaved)]
        public void At_maximum_reach_works_at_an_angle_leave_no_bare_land_between_them(int fill)
        {
            var opts = new Dictionary<string, float>(PolishLooks.Defaults);
            foreach (var k in PolishLooks.ReachKnobs) opts[k] = 500f;
            opts["fill"] = fill;
            var (houses, roads, inside) = Block(opts);
            var plan = YardStitch.Plan(houses, new StitchSettings().WithBarriers(roads));
            // a car park or paving reaches its streets, so the whole block (2 m in from the kerbs: the barrier margin); a
            // garden's front stays at its lot's street edge, so the block away from the streets
            bool hard = fill != PolishLooks.FillGarden;
            float bare = Bare(houses, plan, roads, inside, hard ? 2f : 25f, out var spot);
            _out.WriteLine($"fill {fill}: {houses.Count} works, {bare:F0} m² of the block nobody's (one spot {spot})");
            foreach (var n in plan.Notes.Where(n => !n.Contains(": "))) _out.WriteLine("  " + n);
            var dir = System.Environment.GetEnvironmentVariable("PM_GAP_PNG");
            if (!string.IsNullOrEmpty(dir)) GapPicture.Write(System.IO.Path.Combine(dir, $"block-fill{fill}.png"), houses, plan, roads, 2f, 30f, Poly.Offset(inside, 2f).OrderByDescending(r => Poly.Area(r)).First());
            Assert.True(bare < 60f, $"{bare:F0} m² of the block is nobody's (one spot {spot})");
            // and no two gardens over each other
            var plots = plan.Plots.ToList();
            for (int a = 0; a < plots.Count; a++)
                for (int b = a + 1; b < plots.Count; b++)
                    Assert.True(Shared(plots[a].Value, plots[b].Value) < 2f, $"gardens {plots[a].Key} and {plots[b].Key} overlap");
        }

        [Fact]
        public void Lots_further_apart_than_their_block_corners_reach_share_nothing()
        {
            // "Block corners" (maxFacingGap) is how far apart two lots may stand and still share the land between them
            var opts = new Dictionary<string, float>(PolishLooks.Defaults) { ["fill"] = PolishLooks.FillPaved, ["maxFacingGap"] = 0f, ["openReach"] = 0f };
            var (houses, roads, _) = Block(opts);
            var plan = YardStitch.Plan(houses, new StitchSettings().WithBarriers(roads));
            Assert.DoesNotContain(plan.Notes, n => n.Contains("land between them and a lot at an angle"));
        }

        /// <summary>A pass the game runner wrote (lab op polishPassDump), every polished house in it planned at once, drawn:
        /// PM_GAP_DUMP=&lt;pass-NNN.json&gt; PM_GAP_PNG=&lt;folder&gt; (nothing to do without them).</summary>
        [Fact]
        public void A_dumped_pass_replayed_whole_is_drawn()
        {
            var file = System.Environment.GetEnvironmentVariable("PM_GAP_DUMP");
            var dir = System.Environment.GetEnvironmentVariable("PM_GAP_PNG");
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(dir)) return;
            var input = StitchDump.Read(System.IO.File.ReadAllText(file));
            foreach (var h in input.Houses) if (h.Polished) { h.Edit = true; h.Garden = null; h.Standing = null; }
            var plan = YardStitch.Plan(input.Houses, input.Settings);
            _out.WriteLine($"{System.IO.Path.GetFileName(file)}: {input.Houses.Count} houses ({input.Houses.Count(h => h.Polished)} polished)");
            foreach (var n in plan.Notes.Where(n => !n.Contains(": "))) _out.WriteLine("  " + n);
            GapPicture.Write(System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(file) + ".png"), input.Houses, plan, input.Settings.Barriers?.ToList() ?? new List<float2[]>(), 1.5f);
        }
    }
}
