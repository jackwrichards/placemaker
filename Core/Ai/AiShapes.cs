// AI builder (docs/AI-BUILDER.md): the geometry behind two of the agent's tools. A smooth curve through a path's points
// (place_path smooth), and a scatter (scatter_objects): points inside an area at least a spacing apart, optionally in
// clumps driven by a noise field. The AI sets every parameter and gets back every object placed, so it knows exactly
// what happened (the user, 26 Oct: bulk tools are fine when the AI knows exactly what they do). Pure and seeded.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Ai
{
    public static class AiShapes
    {
        /// <summary>Catmull-Rom through the points (world xz), one point every <paramref name="step"/> metres; ends kept.</summary>
        public static float2[] Smooth(float2[] pts, float step = 4f)
        {
            if (pts == null || pts.Length < 3) return pts;
            bool loop = math.distance(pts[0], pts[pts.Length - 1]) < 1f;
            var p = loop ? pts.Take(pts.Length - 1).ToArray() : pts;
            int n = p.Length;
            float2 At(int i) => loop ? p[((i % n) + n) % n] : p[math.clamp(i, 0, n - 1)];
            var outPts = new List<float2>();
            int segs = loop ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                float2 p0 = At(i - 1), p1 = At(i), p2 = At(i + 1), p3 = At(i + 2);
                int k = math.max(1, (int)math.ceil(math.distance(p1, p2) / step));
                for (int j = 0; j < k; j++)
                {
                    float t = j / (float)k, t2 = t * t, t3 = t2 * t;
                    outPts.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            outPts.Add(loop ? outPts[0] : p[n - 1]);
            return outPts.ToArray();
        }

        private static float Hash(int x, int y, uint seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263) ^ (seed * 2246822519u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
        }

        /// <summary>Smooth value noise in 0..1, features about <paramref name="scale"/> metres across.</summary>
        public static float Noise(float2 p, float scale, uint seed)
        {
            var q = p / math.max(1f, scale);
            int2 i = (int2)math.floor(q);
            var f = q - math.floor(q);
            f = f * f * (3f - 2f * f);
            float a = Hash(i.x, i.y, seed), b = Hash(i.x + 1, i.y, seed), c = Hash(i.x, i.y + 1, seed), d = Hash(i.x + 1, i.y + 1, seed);
            return math.lerp(math.lerp(a, b, f.x), math.lerp(c, d, f.x), f.y);
        }

        /// <summary>
        /// Up to <paramref name="count"/> points inside <paramref name="area"/> (world xz), at least <paramref name="spacing"/>
        /// apart (dart throwing). <paramref name="clumps"/> 0..1: how strongly they gather where a noise field (features about
        /// 5 spacings across) is high, leaving glades between. <paramref name="accept"/> has the last word on each point.
        /// </summary>
        public static List<float2> Scatter(float2[] area, int count, float spacing, float clumps, uint seed, Func<float2, bool> accept)
        {
            var result = new List<float2>();
            if (area == null || area.Length < 3 || count <= 0) return result;
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var p in area) { lo = math.min(lo, p); hi = math.max(hi, p); }
            var rng = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            float cell = math.max(0.5f, spacing / math.SQRT2);
            var grid = new Dictionary<long, float2>();
            long Key(int2 c) => ((long)c.x << 32) ^ (uint)c.y;
            float noiseScale = math.max(12f, spacing * 5f);
            int tries = count * 60;
            for (int t = 0; t < tries && result.Count < count; t++)
            {
                var p = rng.NextFloat2(lo, hi);
                if (!Poly.Contains(area, p)) continue;
                if (clumps > 0f && rng.NextFloat() > math.saturate(math.pow(Noise(p, noiseScale, seed), 1f + 3f * clumps) * (1f + 3f * clumps))) continue;
                var c = (int2)math.floor(p / cell);
                bool near = false;
                for (int dy = -2; dy <= 2 && !near; dy++)
                    for (int dx = -2; dx <= 2 && !near; dx++)
                        if (grid.TryGetValue(Key(c + new int2(dx, dy)), out var q) && math.distancesq(p, q) < spacing * spacing) near = true;
                if (near || (accept != null && !accept(p))) continue;
                grid[Key(c)] = p;
                result.Add(p);
            }
            return result;
        }

        /// <summary>A circle as a ring (world xz).</summary>
        public static float2[] Circle(float2 c, float r, int n = 32) =>
            Enumerable.Range(0, n).Select(i => c + r * new float2(math.cos(2f * math.PI * i / n), math.sin(2f * math.PI * i / n))).ToArray();
    }
}
