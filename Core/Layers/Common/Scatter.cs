// Natural-looking point scattering, merged from ParkingLotTool (KruemelGames, GPL-3.0),
// Geometry/ParkingVegetation.cs:
//   * jittered search grid only over cells inside the outline (work scales with area, not bbox),
//   * visit cells in a coprime-stride permutation so acceptance never forms row bands,
//   * min-distance test through a spatial hash,
//   * edge-distance probability ramp and value-noise clustering ("soft groups of varying size
//     instead of identical single throws").
// Generalised: callers supply the acceptance probability and the clearance test, so the same
// sampler drives park solitaires, big trees, campground woods and flowerbed spots.

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class Scatter
    {
        /// <summary>
        /// Candidate points over the region on a jittered grid of pitch <paramref name="cell"/>, in a
        /// deterministic pseudo-random visiting order. At most <paramref name="maxCandidates"/>.
        /// </summary>
        public static List<float2> Candidates(LayerRegion region, float cell, uint seed, int maxCandidates, CancellationToken cancel) =>
            Candidates(region, cell, seed, maxCandidates, cancel, false);

        /// <summary>
        /// (nature makeover) <paramref name="shuffle"/>: visit the cells in a true seeded shuffle. The coprime stride walks the
        /// row-major cell list as an arithmetic progression, so a caller that stops early (it has planted enough) keeps only
        /// the first diagonal bands: the heath's bushes and the ridge hike's trees stood in parallel rows.
        /// </summary>
        public static List<float2> Candidates(LayerRegion region, float cell, uint seed, int maxCandidates, CancellationToken cancel, bool shuffle)
        {
            var result = new List<float2>();
            var size = region.Max - region.Min;
            cell = math.max(cell, math.sqrt(math.max(1f, size.x * size.y) / math.max(1, maxCandidates)));
            int nx = math.max(1, (int)math.ceil(size.x / cell)), ny = math.max(1, (int)math.ceil(size.y / cell));
            var cells = new List<int2>();
            var crossings = new List<float>();
            var ring = region.Ring;
            for (int iy = 0; iy < ny; iy++)
            {
                if ((iy & 31) == 0) cancel.ThrowIfCancellationRequested();
                float py = region.Min.y + (iy + 0.5f) * cell;
                crossings.Clear();
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    var a = ring[i]; var b = ring[j];
                    if ((a.y > py) != (b.y > py)) crossings.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                if (crossings.Count < 2) continue;
                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int from = math.max(0, (int)math.floor((crossings[k] - region.Min.x) / cell));
                    int to = math.min(nx - 1, (int)math.floor((crossings[k + 1] - region.Min.x) / cell));
                    for (int ix = from; ix <= to; ix++) cells.Add(new int2(ix, iy));
                }
            }
            long total = cells.Count;
            if (total == 0) return result;
            if (shuffle)
            {
                for (int i = cells.Count - 1; i > 0; i--)
                {
                    int j = (int)(Hashing.Mix(seed, i, 0x5F1) % (uint)(i + 1));
                    var tmp = cells[i]; cells[i] = cells[j]; cells[j] = tmp;
                }
                for (int s = 0; s < cells.Count && result.Count < maxCandidates; s++)
                {
                    var c = cells[s];
                    uint h = Hashing.Mix(seed, c.x, c.y);
                    var p = region.Min + (new float2(c.x, c.y) + new float2(Hashing.Unit(h), Hashing.Unit(Hashing.Mix(h)))) * cell;
                    if (region.Contains(p)) result.Add(p);
                }
                return result;
            }
            long stride = 65537;
            while (Gcd(stride, total) != 1) stride += 2;
            for (long s = 0; s < total && result.Count < maxCandidates; s++)
            {
                var c = cells[(int)(s * stride % total)];
                uint h = Hashing.Mix(seed, c.x, c.y);
                var p = region.Min + (new float2(c.x, c.y) + new float2(Hashing.Unit(h), Hashing.Unit(Hashing.Mix(h)))) * cell;
                if (region.Contains(p)) result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// PLT's edge ramp: plants that read as "lining" (bushes) prefer the edge of a green area,
        /// tall ones (trees) prefer its interior. Returns a probability multiplier.
        /// </summary>
        public static float EdgeRamp(float edgeDistance, bool tall, float rampLength = 4f)
        {
            float t = math.saturate(edgeDistance / math.max(0.1f, rampLength));
            return tall ? math.lerp(0.3f, 1f, t) : math.lerp(1f, 0.7f, t);
        }

        /// <summary>PLT's clustering multiplier from value noise; <paramref name="scale"/> = typical group size.</summary>
        public static float Clustering(float2 p, float scale, uint seed, float floor = 0.35f) =>
            math.lerp(floor, 1f, ValueNoise.Clumps(p, scale, seed));

        private static long Gcd(long a, long b) { while (b != 0) { long t = a % b; a = b; b = t; } return a; }
    }
}
