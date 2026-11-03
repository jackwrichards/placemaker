// Placemaker — the outline of a set of grid cells as rings (AreaFill's region before the exact cut).
//
// Every cell side between a cell in the set and one outside becomes a directed edge with the set on its left, so
// outer rings run counter-clockwise and holes clockwise. Where two cells only touch at a corner the walk turns left,
// which keeps 4-connected pieces apart: every ring is simple. Collinear corners are dropped.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public static class CellTrace
    {
        /// <summary>Rings (world XZ) around the cells where <paramref name="inSet"/> is true; outer CCW, holes CW.</summary>
        public static List<float2[]> Rings(bool[] inSet, int w, int h, float2 origin, float cell)
        {
            var rings = new List<float2[]>();
            if (inSet == null || w <= 0 || h <= 0) return rings;
            bool In(int i, int j) => i >= 0 && j >= 0 && i < w && j < h && inSet[j * w + i];
            // Outgoing edges per corner vertex (corner (i, j) has id j * (w + 1) + i); each holds up to two (a pinch).
            int vw = w + 1;
            var outA = new Dictionary<int, int>();
            var outB = new Dictionary<int, int>();
            var used = new HashSet<long>();
            void Add(int from, int to)
            {
                if (!outA.ContainsKey(from)) outA[from] = to;
                else outB[from] = to;
            }
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    if (!inSet[j * w + i]) continue;
                    if (!In(i, j - 1)) Add(j * vw + i, j * vw + i + 1);                     // bottom, eastward
                    if (!In(i + 1, j)) Add(j * vw + i + 1, (j + 1) * vw + i + 1);           // right, northward
                    if (!In(i, j + 1)) Add((j + 1) * vw + i + 1, (j + 1) * vw + i);         // top, westward
                    if (!In(i - 1, j)) Add((j + 1) * vw + i, j * vw + i);                   // left, southward
                }
            long Key(int a, int b) => ((long)a << 32) | (uint)b;
            int2 P(int v) => new int2(v % vw, v / vw);
            foreach (var start in new List<int>(outA.Keys))
            {
                foreach (var first in new[] { outA[start], outB.TryGetValue(start, out var fb) ? fb : -1 })
                {
                    if (first < 0 || used.Contains(Key(start, first))) continue;
                    var pts = new List<int2>();
                    int prev = start, cur = first;
                    used.Add(Key(start, first));
                    pts.Add(P(start));
                    for (int guard = 0; guard < 4 * w * h + 8; guard++)
                    {
                        if (cur == start) break;
                        pts.Add(P(cur));
                        int2 din = P(cur) - P(prev);
                        int a = outA.TryGetValue(cur, out var x) ? x : -1;
                        int b = outB.TryGetValue(cur, out var y) ? y : -1;
                        int next = a;
                        if (b >= 0)
                        {
                            // Pinch: prefer the left turn (keeps 4-connected pieces apart).
                            int2 da = P(a) - P(cur), db = P(b) - P(cur);
                            int ca = din.x * da.y - din.y * da.x, cb = din.x * db.y - din.y * db.x;
                            next = cb > ca ? b : a;
                            if (used.Contains(Key(cur, next))) next = next == a ? b : a;
                        }
                        if (next < 0 || used.Contains(Key(cur, next))) break;
                        used.Add(Key(cur, next));
                        prev = cur;
                        cur = next;
                    }
                    if (pts.Count < 4) continue;
                    var ring = new List<float2>(pts.Count);
                    int n = pts.Count;
                    for (int k = 0; k < n; k++)
                    {
                        int2 p0 = pts[(k + n - 1) % n], p1 = pts[k], p2 = pts[(k + 1) % n];
                        int2 d0 = p1 - p0, d1 = p2 - p1;
                        if (d0.x * d1.y - d0.y * d1.x == 0) continue;   // straight on
                        ring.Add(origin + new float2(p1.x, p1.y) * cell);
                    }
                    if (ring.Count >= 3) rings.Add(ring.ToArray());
                }
            }
            return rings;
        }
    }
}
