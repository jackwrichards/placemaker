// Raster extraction of the open pieces of a region (lawns between paths, gaps between pitches).
//
// Why a raster and not exact polygon booleans: path graphs contain dead ends, bridges and
// crossings, and an exact face trace of such graphs yields rings that touch themselves —
// exactly what CS2 drops (RESEARCH-NOTES rule 6) and what a naive offset mangles. A coarse
// grid (1.5–4 m) is robust, bounded (≤ ~60k cells) and precise enough for child layers,
// which still respect ReservedSpace for exact clearance.
//
// Output rings are simple, CCW and hole-free: holes (islands enclosed by a loop) are removed
// by cutting a one-cell slit to the outside, and diagonal "pinch" contacts are broken first.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class FreeSpace
    {
        public const int MaxCells = 60000;

        /// <summary>
        /// Free connected pieces of <paramref name="region"/> where <paramref name="isBlocked"/> is false
        /// at cell centres. Sorted by area, largest first. Pieces smaller than minArea are dropped.
        /// </summary>
        public static List<float2[]> Pieces(LayerRegion region, Func<float2, bool> isBlocked, float cellSize, float minArea,
            CancellationToken cancel)
        {
            var result = new List<float2[]>();
            var size = region.Max - region.Min;
            float cell = math.max(cellSize, math.sqrt(size.x * size.y / MaxCells));
            int nx = math.max(1, (int)math.ceil(size.x / cell)) + 2;   // one padding cell each side
            int ny = math.max(1, (int)math.ceil(size.y / cell)) + 2;
            var origin = region.Min - cell;
            var free = new bool[nx * ny];
            // Cells hug the outline only up to this margin, so staircase corners and the simplified outline
            // (tolerance ≤ 0.75 cell) never poke outside the region.
            float margin = cell * 1.5f;
            for (int y = 1; y < ny - 1; y++)
            {
                if ((y & 15) == 0) cancel.ThrowIfCancellationRequested();
                for (int x = 1; x < nx - 1; x++)
                {
                    var c = origin + (new float2(x, y) + 0.5f) * cell;
                    free[y * nx + x] = region.ContainsDisc(c, margin) && !isBlocked(c);   // ContainsDisc: Contains && BoundaryDistance >= margin
                }
            }

            var label = new int[nx * ny];
            int components = new Grid(nx, ny, cell, origin, 0, 0).Label(free, label);
            // (perf) Each piece is worked on in its own box (its cells' bounds + one empty cell round them; the padding keeps that
            // inside the grid) instead of the whole grid: the same steps in the same order, so the same rings, at a fraction of the
            // cost with many lawns.
            var lo = new int2[components + 1]; var hi = new int2[components + 1];
            for (int k = 1; k <= components; k++) { lo[k] = new int2(int.MaxValue); hi[k] = new int2(int.MinValue); }
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int k = label[y * nx + x];
                    if (k == 0) continue;
                    lo[k] = math.min(lo[k], new int2(x, y)); hi[k] = math.max(hi[k], new int2(x, y));
                }
            var masks = new List<(Grid grid, bool[] mask)>();
            for (int k = 1; k <= components; k++)
            {
                int ox = lo[k].x - 1, oy = lo[k].y - 1, w = hi[k].x - lo[k].x + 3, h = hi[k].y - lo[k].y + 3;
                var g = new Grid(w, h, cell, origin, ox, oy);
                var m = new bool[w * h];
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++) m[y * w + x] = label[(y + oy) * nx + x + ox] == k;
                masks.Add((g, m));
            }

            int guard = 0;
            while (masks.Count > 0 && guard++ < 4000)
            {
                cancel.ThrowIfCancellationRequested();
                var (grid, mask) = masks[masks.Count - 1];
                masks.RemoveAt(masks.Count - 1);
                if (grid.Count(mask) * cell * cell < minArea) continue;
                grid.RemovePinches(mask);
                // Pinch removal or slits may split the piece; process parts separately.
                var parts = grid.Split(mask);
                if (parts.Count > 1) { foreach (var part in parts) masks.Add((grid, part)); continue; }
                if (parts.Count == 0) continue;
                if (grid.CutHoles(mask)) { masks.Add((grid, mask)); continue; }
                var ring = grid.Trace(mask);
                if (ring == null) continue;
                var simplified = Simplify(ring, cell);
                if (simplified.Length >= 3 && Poly.Area(simplified) >= minArea) result.Add(simplified);
            }
            result.Sort((a, b) => Poly.Area(b).CompareTo(Poly.Area(a)));
            return result;
        }

        /// <summary>
        /// Douglas–Peucker on the staircase outline; falls back to the exact staircase whenever a
        /// simplification would self-intersect.
        /// </summary>
        private static float2[] Simplify(List<float2> stair, float cell)
        {
            var exact = Poly.Clean(stair, 0.01f);
            foreach (float tol in new[] { cell * 0.75f, cell * 0.4f })
            {
                var s = DouglasPeuckerClosed(exact, tol);
                var c = Poly.Clean(s, 0.05f);
                if (c.Length >= 3 && Poly.IsSimple(c)) return c;
            }
            return exact;
        }

        /// <summary>Douglas–Peucker on a closed ring (keeps original vertices; may self-intersect — caller checks).</summary>
        public static float2[] DouglasPeuckerClosed(float2[] ring, float tol)
        {
            int n = ring.Length;
            if (n < 5) return ring;
            // Split the closed ring at vertex 0 and the vertex farthest from it.
            int far = 0; float best = -1;
            for (int i = 1; i < n; i++) { float d = math.distancesq(ring[0], ring[i]); if (d > best) { best = d; far = i; } }
            var keep = new bool[n];
            keep[0] = keep[far] = true;
            Dp(ring, 0, far, tol * tol, keep);
            Dp(ring, far, n, tol * tol, keep);
            var r = new List<float2>();
            for (int i = 0; i < n; i++) if (keep[i]) r.Add(ring[i]);
            return r.ToArray();
        }

        private static void Dp(float2[] ring, int a, int b, float tolSq, bool[] keep)
        {
            // b may equal ring.Length (wraps to 0)
            int n = ring.Length;
            var stack = new WorkStack<int2>();
            stack.Push(new int2(a, b));
            while (stack.Count > 0)
            {
                var s = stack.Pop();
                if (s.y - s.x < 2) continue;
                float2 pa = ring[s.x % n], pb = ring[s.y % n];
                int idx = -1; float best = tolSq;
                for (int i = s.x + 1; i < s.y; i++)
                {
                    float d = SegmentIndex.DistSq(ring[i % n], pa, pb);
                    if (d > best) { best = d; idx = i; }
                }
                if (idx < 0) continue;
                keep[idx % n] = true;
                stack.Push(new int2(s.x, idx));
                stack.Push(new int2(idx, s.y));
            }
        }

        private sealed class Grid
        {
            private readonly int _nx, _ny, _ox, _oy;
            private readonly float _cell;
            private readonly float2 _origin;

            /// <summary>A box of nx × ny cells at cell (ox, oy) of the grid whose cell (0, 0) starts at origin.</summary>
            public Grid(int nx, int ny, float cell, float2 origin, int ox, int oy) { _nx = nx; _ny = ny; _cell = cell; _origin = origin; _ox = ox; _oy = oy; }

            public int Count(bool[] m) { int c = 0; foreach (var b in m) if (b) c++; return c; }

            /// <summary>4-connected labelling; returns the number of components.</summary>
            public int Label(bool[] m, int[] label)
            {
                Array.Clear(label, 0, label.Length);
                int next = 0;
                var stack = new WorkStack<int>();
                for (int i = 0; i < m.Length; i++)
                {
                    if (!m[i] || label[i] != 0) continue;
                    next++;
                    label[i] = next; stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int c = stack.Pop();
                        int x = c % _nx, y = c / _nx;
                        if (x > 0) Visit(c - 1);
                        if (x < _nx - 1) Visit(c + 1);
                        if (y > 0) Visit(c - _nx);
                        if (y < _ny - 1) Visit(c + _nx);
                    }
                }
                return next;

                void Visit(int j)
                {
                    if (m[j] && label[j] == 0) { label[j] = next; stack.Push(j); }
                }
            }

            public List<bool[]> Split(bool[] m)
            {
                var label = new int[m.Length];
                int k = Label(m, label);
                var r = new List<bool[]>(k);
                if (k == 1) { r.Add(m); return r; }
                for (int c = 1; c <= k; c++)
                {
                    var part = new bool[m.Length];
                    for (int i = 0; i < m.Length; i++) part[i] = label[i] == c;
                    r.Add(part);
                }
                return r;
            }

            /// <summary>Clears one cell of every 2×2 block where two set cells touch only diagonally.</summary>
            public void RemovePinches(bool[] m)
            {
                bool changed = true;
                int guard = 0;
                while (changed && guard++ < 64)
                {
                    changed = false;
                    for (int y = 0; y < _ny - 1; y++)
                        for (int x = 0; x < _nx - 1; x++)
                        {
                            int i00 = y * _nx + x, i10 = i00 + 1, i01 = i00 + _nx, i11 = i01 + 1;
                            if (m[i00] && m[i11] && !m[i10] && !m[i01])
                            {
                                m[Weaker(m, i00) <= Weaker(m, i11) ? i00 : i11] = false; changed = true;
                            }
                            else if (m[i10] && m[i01] && !m[i00] && !m[i11])
                            {
                                m[Weaker(m, i10) <= Weaker(m, i01) ? i10 : i01] = false; changed = true;
                            }
                        }
                }
            }

            private int Weaker(bool[] m, int i)
            {
                int x = i % _nx, y = i / _nx, c = 0;
                if (x > 0 && m[i - 1]) c++;
                if (x < _nx - 1 && m[i + 1]) c++;
                if (y > 0 && m[i - _nx]) c++;
                if (y < _ny - 1 && m[i + _nx]) c++;
                return c;
            }

            /// <summary>Cuts one slit per hole towards −x until the outside is reached. True when anything was cut.</summary>
            public bool CutHoles(bool[] m)
            {
                var outside = new bool[m.Length];
                var stack = new WorkStack<int>();
                for (int x = 0; x < _nx; x++) { Seed(x); Seed((_ny - 1) * _nx + x); }
                for (int y = 0; y < _ny; y++) { Seed(y * _nx); Seed(y * _nx + _nx - 1); }
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    int x = c % _nx, y = c / _nx;
                    if (x > 0) Seed(c - 1);
                    if (x < _nx - 1) Seed(c + 1);
                    if (y > 0) Seed(c - _nx);
                    if (y < _ny - 1) Seed(c + _nx);
                }
                bool cut = false;
                for (int i = 0; i < m.Length; i++)
                {
                    if (m[i] || outside[i]) continue;
                    // A hole cell. Walk left, clearing set cells, until an outside cell is reached.
                    int y = i / _nx;
                    for (int x = i % _nx - 1; x >= 0; x--)
                    {
                        int j = y * _nx + x;
                        if (outside[j]) break;
                        if (m[j]) { m[j] = false; cut = true; }
                    }
                    // One slit per call; the caller re-labels and re-checks.
                    if (cut) return true;
                }
                return cut;

                void Seed(int j)
                {
                    if (!m[j] && !outside[j]) { outside[j] = true; stack.Push(j); }
                }
            }

            /// <summary>Traces the single outer loop of a hole-free, pinch-free component, CCW.</summary>
            public List<float2> Trace(bool[] m)
            {
                // Directed boundary edges with the set cell on the left (CCW), keyed by start vertex.
                var next = new Dictionary<long, long>();
                long start = -1;
                for (int y = 0; y < _ny; y++)
                    for (int x = 0; x < _nx; x++)
                    {
                        if (!m[y * _nx + x]) continue;
                        // bottom edge: (x,y)->(x+1,y) if below empty
                        if (y == 0 || !m[(y - 1) * _nx + x]) AddEdge(V(x, y), V(x + 1, y));
                        if (x == _nx - 1 || !m[y * _nx + x + 1]) AddEdge(V(x + 1, y), V(x + 1, y + 1));
                        if (y == _ny - 1 || !m[(y + 1) * _nx + x]) AddEdge(V(x + 1, y + 1), V(x, y + 1));
                        if (x == 0 || !m[y * _nx + x - 1]) AddEdge(V(x, y + 1), V(x, y));
                    }
                if (start < 0) return null;
                var ring = new List<float2>();
                long v = start;
                int guard = next.Count + 2;
                do
                {
                    int vx = (int)(v >> 32), vy = (int)(v & 0xFFFFFFFF);
                    ring.Add(_origin + new float2(vx + _ox, vy + _oy) * _cell);
                    if (!next.TryGetValue(v, out v)) return null;
                } while (v != start && guard-- > 0);
                // A pinch-free component without holes has exactly one loop; anything else is a bug guard.
                if (ring.Count != next.Count) return null;
                return ring;

                void AddEdge(long a, long b)
                {
                    if (next.ContainsKey(a)) return;   // cannot happen without pinches; keep first
                    next[a] = b;
                    if (start < 0) start = a;
                }
            }

            private static long V(int x, int y) => ((long)x << 32) | (uint)y;
        }
    }
}
