// Placemaker neighbourhood polish — the water's edge from the game's water samples (GPL-3.0).
//
// The game's water is sampled on a 4 m grid around a patch of town (wet: deeper than BlockObstacles.MaxWaterDepth). Merged
// as it comes, its outline is a staircase of 4 m steps, and a garden cut back along it got a fence of Lego bricks along
// the shore. Each outline is walked every 2 m and smoothed (a moving average over about 10 m, three times), so the shore
// is a smooth line however it runs; the planner then keeps every garden its look's water gap off it (YardStitch).

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static class WaterEdge
    {
        /// <summary>
        /// The water's outlines from its samples: <paramref name="wet"/> holds <paramref name="nx"/> × <paramref name="nz"/>
        /// cells of <paramref name="step"/> metres from <paramref name="lo"/> (row by row), the first <paramref name="cx"/> ×
        /// <paramref name="cz"/> of them looked at. Each a smooth ring (counter-clockwise), at least 8 m² of water.
        /// </summary>
        public static List<float2[]> Outlines(byte[] wet, int nx, int nz, float2 lo, float step, int cx, int cz)
        {
            var water = new List<float2[]>();
            if (wet == null) return water;
            bool any = false;
            foreach (var w in wet) if (w != 0) { any = true; break; }
            if (!any) return water;
            // runs of wet cells per row, then one union
            var rects = new List<IReadOnlyList<float2>>();
            bool Wet(int i, int j) => i >= 0 && j >= 0 && i < nx && j < nz && wet[j * nx + i] != 0;
            for (int j = 0; j < cz; j++)
            {
                int run = -1;
                for (int i = 0; i <= cx; i++)
                {
                    bool w = i < cx && Wet(i, j);
                    if (w && run < 0) run = i;
                    if (!w && run >= 0)
                    {
                        float x0 = lo.x + run * step, x1 = lo.x + i * step, z0 = lo.y + j * step, z1 = z0 + step;
                        rects.Add(new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) });
                        run = -1;
                    }
                }
            }
            if (rects.Count == 0) return water;
            var smooth = new List<IReadOnlyList<float2>>();
            foreach (var r in Poly.UnionAll(rects))
                if (r.Outer != null && r.Outer.Length >= 3 && r.Area >= 8f) smooth.Add(Smooth(r.Outer, step));
            // smoothing can fold a narrow neck over itself: one more union makes every ring simple again
            foreach (var r in Poly.UnionAll(smooth))
                if (r.Outer != null && r.Outer.Length >= 3 && r.Area >= 8f) water.Add(r.Outer);
            return water;
        }

        /// <summary>A staircase outline as a smooth line: points every half step, averaged over about 2.5 steps, three times,
        /// then the points that add nothing dropped.</summary>
        public static float2[] Smooth(IReadOnlyList<float2> ring, float step)
        {
            var dense = Poly.Resample(ring, math.max(0.5f, step * 0.5f));
            if (dense.Length < 8) return Poly.IsCcw(dense) ? dense : Reverse(dense);
            var soft = Poly.SmoothLoop(dense, 3, 2);
            var simple = Poly.Simplify(soft, step * 0.08f);
            return Poly.IsCcw(simple) ? simple : Reverse(simple);
        }

        private static float2[] Reverse(float2[] r)
        {
            var o = new float2[r.Length];
            for (int i = 0; i < r.Length; i++) o[i] = r[r.Length - 1 - i];
            return o;
        }
    }
}
