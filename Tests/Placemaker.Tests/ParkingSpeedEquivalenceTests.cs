// (perf) The parking planner's fast helpers give the same answers as the plain whole-ring versions they replace.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ParkingSpeedEquivalenceTests
    {
        /// <summary>A wobbly painted-style outline: a hexagon resampled to n corners, each moved up to ±0.4 m.</summary>
        internal static List<double2> Wobbly(int n, double scale, uint seed)
        {
            var baseRing = new[] { new double2(0, 0), new double2(140, -6), new double2(152, 58), new double2(118, 102), new double2(46, 96), new double2(6, 70) };
            double per = 0;
            for (int i = 0; i < baseRing.Length; i++) per += math.distance(baseRing[i], baseRing[(i + 1) % baseRing.Length]) * scale;
            var rng = new Unity.Mathematics.Random(seed);
            var res = new List<double2>();
            double step = per / n, acc = 0;
            for (int i = 0; i < baseRing.Length; i++)
            {
                var a = baseRing[i] * scale; var b = baseRing[(i + 1) % baseRing.Length] * scale;
                double len = math.distance(a, b);
                var d = (b - a) / len; var nrm = new double2(-d.y, d.x);
                for (double t = acc; t < len; t += step) res.Add(a + d * t + nrm * rng.NextDouble(-0.4, 0.4));
                acc = (acc - len) % step; if (acc < 0) acc += step;
            }
            return res;
        }

        [Theory]
        [InlineData(6, 1.0)]
        [InlineData(60, 0.5)]
        [InlineData(400, 1.0)]
        [InlineData(400, 3.0)]
        public void RingGrid_answers_like_the_whole_ring(int corners, double scale)
        {
            var ring = corners <= 6 ? new List<double2> { new double2(0, 0), new double2(140, -6), new double2(152, 58), new double2(118, 102), new double2(46, 96), new double2(6, 70) } : Wobbly(corners, scale, 7);
            var grid = new RingGrid(ring);
            PGeo.Bounds(ring, out var mn, out var mx);
            var rng = new Unity.Mathematics.Random(99);
            for (int k = 0; k < 4000; k++)
            {
                var p = new double2(rng.NextDouble(mn.x - 20, mx.x + 20), rng.NextDouble(mn.y - 20, mx.y + 20));
                if (k % 4 == 0) { var a = ring[rng.NextInt(ring.Count)]; p = a + new double2(rng.NextDouble(-1, 1), rng.NextDouble(-1, 1)); }
                Assert.Equal(PGeo.Contains(ring, p), grid.Contains(p));
                double d = PGeo.DistToBoundary(ring, p);
                Assert.Equal(d, grid.Dist(p));
                double lim = rng.NextDouble(0, 5);
                double dl = grid.Dist(p, lim);
                if (d < lim) Assert.Equal(d, dl); else Assert.True(dl >= lim);
                var q = PGeo.Rect(p, PGeo.Norm(new double2(rng.NextDouble(-1, 1), rng.NextDouble(-1, 1))), rng.NextDouble(0.5, 6), rng.NextDouble(0.5, 4));
                Assert.Equal(PGeo.ConvexInside(q, ring, 1e-4), grid.ConvexInside(q, 1e-4));
            }
        }

        [Fact]
        public void AreaRings_FirstViolation_by_grid_matches_all_pairs()
        {
            var rng = new Unity.Mathematics.Random(11);
            int invalid = 0, total = 0;
            for (int k = 0; k < 400; k++)
            {
                int corners = k % 4 == 0 ? 30 : k % 4 == 1 ? 60 : k % 4 == 2 ? 120 : 400;
                var ring = Wobbly(corners, rng.NextDouble(0.05, 2.0), (uint)(k + 1));
                var f = new List<float2>();
                foreach (var p in ring) f.Add(new float2((float)p.x, (float)p.y));
                if (k % 5 == 0) f.Insert(f.Count / 2, f[f.Count / 2] + new float2(0.1f, 0.05f));   // a spike now and then
                foreach (float d in new[] { 0.05f, 0.425f, 1.5f })
                {
                    int want = Placemaker.Core.Build.AreaRings.FirstViolationAllPairs(f, d);
                    Assert.Equal(want, Placemaker.Core.Build.AreaRings.FirstViolationGrid(f, d));
                    total++; if (want >= 0) invalid++;
                }
                // the repair (node removal, one violation at a time) comes out the same
                var a = Placemaker.Core.Build.AreaRings.MakeValid(f, 0.375f);
                Assert.True(a == null || Placemaker.Core.Build.AreaRings.FirstViolationAllPairs(a, 0.425f) < 0);
            }
            Assert.InRange(invalid, 1, total - 1);   // both answers occur
        }

        [Fact]
        public void IsSimple_by_grid_matches_all_pairs()
        {
            bool AllPairs(List<double2> ring)
            {
                int n = ring.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % n];
                    if (PGeo.Len(b - a) < 1e-9) return false;
                    for (int j = i + 1; j < n; j++)
                    {
                        if ((j + 1) % n == i || j == (i + 1) % n) continue;
                        if (PGeo.SegmentsIntersect(a, b, ring[j], ring[(j + 1) % n])) return false;
                    }
                }
                return true;
            }
            var rng = new Unity.Mathematics.Random(21);
            int simple = 0;
            for (int k = 0; k < 300; k++)
            {
                var ring = Wobbly(k % 2 == 0 ? 60 : 400, rng.NextDouble(0.1, 2.0), (uint)(k + 3));
                if (k % 3 == 1)
                {
                    // fold a corner over its neighbours, or touch two far edges
                    int i = rng.NextInt(ring.Count);
                    ring[i] = ring[(i + ring.Count / 2) % ring.Count] + new double2(rng.NextDouble(-1, 1), rng.NextDouble(-1, 1));
                }
                if (k % 7 == 3) ring.Insert(5, ring[5]);   // a zero-length edge
                bool want = AllPairs(ring);
                Assert.Equal(want, PGeo.IsSimpleByGrid(ring));
                if (want) simple++;
            }
            Assert.InRange(simple, 1, 299);
        }

        [Fact]
        public void CloseInterval_matches_sampled_distances()
        {
            var rng = new Unity.Mathematics.Random(5);
            for (int k = 0; k < 3000; k++)
            {
                var o = new double2(rng.NextDouble(-10, 10), rng.NextDouble(-10, 10));
                var u = PGeo.Norm(new double2(rng.NextDouble(-1, 1), rng.NextDouble(-1, 1)));
                var a = new double2(rng.NextDouble(-10, 10), rng.NextDouble(-10, 10));
                var b = k % 7 == 0 ? a : new double2(rng.NextDouble(-10, 10), rng.NextDouble(-10, 10));
                double r = rng.NextDouble(0.1, 6);
                bool any = Inset.CloseInterval(o, u, a, b, r, out double lo, out double hi);
                for (double t = -40; t <= 40; t += 0.05)
                {
                    double d = PGeo.DistPointSeg(o + u * t, a, b);
                    if (Math.Abs(d - r) < 1e-6) continue;
                    bool inside = any && t > lo && t < hi;
                    Assert.True((d < r) == inside, $"case {k} t {t} d {d} r {r} [{lo}, {hi}]");
                }
            }
        }
    }
}
