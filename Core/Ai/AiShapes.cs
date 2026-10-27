// AI builder (docs/AI-BUILDER.md): a smooth curve through a path's points, when the AI asks for one (place_path smooth).
// The AI places every object itself: no generators here (the user, 26 Oct: tools give it the least it needs, the rest is
// the prompt).

using System.Collections.Generic;
using System.Linq;
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

    }
}
