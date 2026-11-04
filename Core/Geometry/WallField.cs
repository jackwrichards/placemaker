// Placemaker — the walls around a click as a grid (the area tools' shared fill engine, AreaFill).
//
// The user (area overhaul): "if an opening is 10 meters across, it should still paint bucket and consider that closed",
// "sometimes it goes inside tiny little cracks", "a distance threshold ... if I paint bucket in an empty flat area".
// Gaps, cracks and a reach are questions about free space, not about a road graph, so the walls (road and path
// surfaces with their junctions and turning circles, buildings, lots, zoning, water, the reach circle) are drawn into
// one grid of cells around the click, and the distance from every free cell to the nearest wall is measured
// (an exact Euclidean distance transform, Felzenszwalb & Huttenlocher). AreaFill grows the region on it; the outline
// itself is then cut exactly from the wall polygons, so the grid only decides which piece, never where its edges lie.
//
// Walls are drawn conservatively: every cell a wall touches is a wall, so a path thinner than a cell never leaks.
// Pure geometry, no engine types.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public sealed class WallField
    {
        /// <summary>Cell states: free land, a wall, the window's border.</summary>
        public const byte Free = 0, Wall = 1, Beyond = 2;

        public readonly float2 Origin;
        public readonly float Cell;
        public readonly int W, H;
        /// <summary>Per cell: Free, Wall or Beyond.</summary>
        public readonly byte[] State;
        /// <summary>Per cell: the wall kind that took it (BrushObstacleKind + 1; 0 none) — what the overlay and the log name.</summary>
        public readonly byte[] Kind;
        /// <summary>Distance (m) from each cell's centre to the nearest wall or border cell (≈ to the wall's edge); 0 on walls.</summary>
        public float[] Dist { get; private set; }

        public WallField(float2 min, float2 max, float cell)
        {
            Cell = cell > 0.05f && !float.IsNaN(cell) ? cell : 1f;
            W = math.max(3, (int)math.ceil((max.x - min.x) / Cell));
            H = math.max(3, (int)math.ceil((max.y - min.y) / Cell));
            Origin = min;
            State = new byte[W * H];
            Kind = new byte[W * H];
            // Beyond: the window's own border (a fill never runs off the grid).
            for (int i = 0; i < W; i++) { State[i] = Beyond; State[(H - 1) * W + i] = Beyond; }
            for (int j = 0; j < H; j++) { State[j * W] = Beyond; State[j * W + W - 1] = Beyond; }
        }

        public float2 Max => Origin + new float2(W, H) * Cell;
        public int Index(int i, int j) => j * W + i;
        public float2 Center(int i, int j) => Origin + new float2(i + 0.5f, j + 0.5f) * Cell;
        public float2 Center(int idx) => Center(idx % W, idx / W);
        public bool InRange(int i, int j) => i >= 0 && j >= 0 && i < W && j < H;
        public int2 CellOf(float2 p) => new int2((int)math.floor((p.x - Origin.x) / Cell), (int)math.floor((p.y - Origin.y) / Cell));
        public bool IsFree(int idx) => State[idx] == Free;

        /// <summary>Marks every cell the polygon touches as a wall of <paramref name="kind"/> (conservative: edges and interior).</summary>
        public void AddWall(IReadOnlyList<float2> ring, byte kind)
        {
            if (ring == null || ring.Count < 3) return;
            Poly.Bounds(ring, out var mn, out var mx);
            if (mx.x < Origin.x || mx.y < Origin.y || mn.x > Max.x || mn.y > Max.y) return;
            int j0 = math.max(0, (int)math.floor((mn.y - Origin.y) / Cell)), j1 = math.min(H - 1, (int)math.floor((mx.y - Origin.y) / Cell));
            int i0 = math.max(0, (int)math.floor((mn.x - Origin.x) / Cell)), i1 = math.min(W - 1, (int)math.floor((mx.x - Origin.x) / Cell));
            if (j1 < j0 || i1 < i0) return;
            // Interior: cells whose centre is inside (even-odd scanline at the row's centre).
            var xs = new List<float>(8);
            int n = ring.Count;
            for (int j = j0; j <= j1; j++)
            {
                float y = Origin.y + (j + 0.5f) * Cell;
                xs.Clear();
                for (int k = 0; k < n; k++)
                {
                    float2 a = ring[k], b = ring[(k + 1) % n];
                    if ((a.y <= y) == (b.y <= y)) continue;
                    xs.Add(a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int a = math.max(i0, (int)math.ceil((xs[k] - Origin.x) / Cell - 0.5f));
                    int b = math.min(i1, (int)math.floor((xs[k + 1] - Origin.x) / Cell - 0.5f));
                    for (int i = a; i <= b; i++) Mark(i, j, kind);
                }
            }
            // Edges: every cell a boundary segment passes through (supercover), so thin walls stay closed.
            for (int k = 0; k < n; k++) MarkSegment(ring[k], ring[(k + 1) % n], kind);
        }

        /// <summary>Marks the cells under water (or any other point test) as walls of <paramref name="kind"/>.</summary>
        public void AddWhere(Func<float2, bool> wall, byte kind)
        {
            if (wall == null) return;
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                    if (State[j * W + i] == Free && wall(Center(i, j))) Mark(i, j, kind);
        }

        private void Mark(int i, int j, byte kind)
        {
            int idx = j * W + i;
            if (State[idx] == Beyond) return;
            State[idx] = Wall;
            if (Kind[idx] == 0) Kind[idx] = kind;
        }

        /// <summary>Supercover traversal of the segment (Amanatides & Woo): every cell it touches.</summary>
        private void MarkSegment(float2 a, float2 b, byte kind)
        {
            float2 pa = (a - Origin) / Cell, pb = (b - Origin) / Cell;
            // Clip to the grid (with a margin) so far-away vertices do not cost a long walk.
            if (!ClipToBox(ref pa, ref pb, new float2(-1f), new float2(W + 1, H + 1))) return;
            int i = (int)math.floor(pa.x), j = (int)math.floor(pa.y);
            int ie = (int)math.floor(pb.x), je = (int)math.floor(pb.y);
            float2 d = pb - pa;
            int si = d.x > 0 ? 1 : d.x < 0 ? -1 : 0, sj = d.y > 0 ? 1 : d.y < 0 ? -1 : 0;
            float tdx = si != 0 ? math.abs(1f / d.x) : float.MaxValue, tdy = sj != 0 ? math.abs(1f / d.y) : float.MaxValue;
            float tx = si > 0 ? (i + 1 - pa.x) * tdx : si < 0 ? (pa.x - i) * tdx : float.MaxValue;
            float ty = sj > 0 ? (j + 1 - pa.y) * tdy : sj < 0 ? (pa.y - j) * tdy : float.MaxValue;
            for (int guard = 0; guard < 4 * (W + H) + 8; guard++)
            {
                if (InRange(i, j)) Mark(i, j, kind);
                if (i == ie && j == je) break;
                if (math.abs(tx - ty) < 1e-6f)
                {
                    // Through a corner: touch both side cells too.
                    if (InRange(i + si, j)) Mark(i + si, j, kind);
                    if (InRange(i, j + sj)) Mark(i, j + sj, kind);
                    i += si; j += sj; tx += tdx; ty += tdy;
                }
                else if (tx < ty) { i += si; tx += tdx; }
                else { j += sj; ty += tdy; }
                if (tx > 1f + tdx && ty > 1f + tdy) break;
            }
        }

        private static bool ClipToBox(ref float2 a, ref float2 b, float2 mn, float2 mx)
        {
            float t0 = 0f, t1 = 1f;
            float2 d = b - a;
            for (int axis = 0; axis < 2; axis++)
            {
                float p = axis == 0 ? d.x : d.y, q0 = axis == 0 ? a.x : a.y;
                float lo = axis == 0 ? mn.x : mn.y, hi = axis == 0 ? mx.x : mx.y;
                if (math.abs(p) < 1e-9f) { if (q0 < lo || q0 > hi) return false; continue; }
                float ta = (lo - q0) / p, tb = (hi - q0) / p;
                if (ta > tb) { var t = ta; ta = tb; tb = t; }
                t0 = math.max(t0, ta); t1 = math.min(t1, tb);
                if (t0 > t1) return false;
            }
            var a0 = a;
            a = a0 + d * t0;
            b = a0 + d * t1;
            return true;
        }

        /// <summary>
        /// Distance (m) of every cell to the nearest non-free cell (walls and beyond), exact Euclidean between cell
        /// centres minus half a cell (so it approximates the distance to the wall's edge). Felzenszwalb–Huttenlocher.
        /// </summary>
        public float[] ComputeDistance()
        {
            int n = W * H;
            var f = SquaredDistance(W, H, 0, 0, W, H, k => State[k] != Free);
            var d = new float[n];
            for (int k = 0; k < n; k++) d[k] = State[k] == Free ? math.max(0.01f, (math.sqrt(f[k]) - 0.5f) * Cell) : 0f;
            Dist = d;
            return d;
        }

        /// <summary>
        /// Squared distance (in cells²) from every cell of the sub-rectangle [i0, i1) × [j0, j1) of a W-wide grid to the
        /// nearest source cell inside it (1e20 when it holds none). The result is indexed like the full grid; cells outside
        /// the rectangle are left at 1e20.
        /// </summary>
        public static float[] SquaredDistance(int w, int h, int i0, int j0, int i1, int j1, Func<int, bool> source)
        {
            const float Inf = 1e20f;
            var f = new float[w * h];
            for (int k = 0; k < f.Length; k++) f[k] = Inf;
            i0 = math.max(0, i0); j0 = math.max(0, j0); i1 = math.min(w, i1); j1 = math.min(h, j1);
            int sw = i1 - i0, sh = j1 - j0;
            if (sw <= 0 || sh <= 0) return f;
            for (int j = j0; j < j1; j++)
                for (int i = i0; i < i1; i++)
                {
                    int k = j * w + i;
                    f[k] = source(k) ? 0f : Inf;
                }
            int m = math.max(sw, sh);
            var col = new float[m];
            var outBuf = new float[m];
            var v = new int[m];
            var z = new float[m + 1];
            for (int i = i0; i < i1; i++)
            {
                for (int j = 0; j < sh; j++) col[j] = f[(j0 + j) * w + i];
                Edt1(col, sh, outBuf, v, z);
                for (int j = 0; j < sh; j++) f[(j0 + j) * w + i] = outBuf[j];
            }
            for (int j = j0; j < j1; j++)
            {
                int row = j * w;
                for (int i = 0; i < sw; i++) col[i] = f[row + i0 + i];
                Edt1(col, sw, outBuf, v, z);
                for (int i = 0; i < sw; i++) f[row + i0 + i] = math.min(Inf, outBuf[i]);
            }
            return f;
        }

        /// <summary>1D squared distance transform of a sampled function (lower envelope of parabolas).</summary>
        private static void Edt1(float[] f, int n, float[] d, int[] v, float[] z)
        {
            const float Inf = 1e20f;
            int k = 0;
            v[0] = 0;
            z[0] = -Inf;
            z[1] = Inf;
            for (int q = 1; q < n; q++)
            {
                int p = v[k];
                float s = ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * (q - p));
                while (s <= z[k])
                {
                    k--;
                    p = v[k];
                    s = ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * (q - p));
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = Inf;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                int p = v[k];
                d[q] = (q - p) * (q - p) + f[p];
            }
        }
    }
}
