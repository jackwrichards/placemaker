// Placemaker — painted area selection ("Paint" mode).
//
// Adapted from Grid Road Generator (Ti4goc), Core/BrushMask.cs: tiled bool cells, round /
// rotated-square stamps, gap-free strokes, incrementally maintained edge cells for a cheap live
// boundary preview, fill runs for the filled preview, and the grid → smooth outline pipeline.
// Added here: several islands and holes (Outlines), configurable cell size, typed snapshots,
// polygon painting, and outlines that are validated simple (each ring and between rings).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    /// <summary>
    /// Brush-painted area on a square cell grid (XZ, cells of <see cref="CellSize"/> metres; cell (i, j)
    /// covers [i·s, (i+1)·s) × [j·s, (j+1)·s)). Not thread-safe: paint on one thread, snapshot with
    /// <see cref="Save"/> to hand the state to a worker.
    /// </summary>
    public sealed class BrushMask
    {
        public const float DefaultCellSize = 4f;
        public const float MinDiameter = 1f;
        public const float MaxDiameter = 1000f;
        public const float DefaultDiameter = 150f;

        // 64 × 64 cell tiles (arrays): far faster to query than a key set — a 400 m stamp touches
        // ~8000 cells every painting frame.
        private const int TileBits = 6;
        private const int TileSize = 1 << TileBits;
        /// <summary>Max cells merged into one RawBoundary segment (still follows terrain when draped).</summary>
        private const int MaxRun = 12;

        private readonly Dictionary<long, bool[]> _tiles = new Dictionary<long, bool[]>();
        private long _lastTileKey = long.MinValue;
        private bool[] _lastTile;
        private int _count;
        /// <summary>Painted cells with an unpainted 4-neighbour, kept up to date per stamp: the live preview walks only these.</summary>
        private readonly HashSet<long> _edgeCells = new HashSet<long>();
        private readonly List<int2> _changed = new List<int2>();

        public BrushMask(float cellSize = DefaultCellSize)
        {
            CellSize = cellSize > 0.01f && !float.IsNaN(cellSize) ? cellSize : DefaultCellSize;
        }

        public float CellSize { get; }
        /// <summary>Incremented on every change (outline / drawing caches key on it).</summary>
        public int Version { get; private set; }
        public bool IsEmpty => _count == 0;
        public int PaintedCellCount => _count;
        /// <summary>Painted area (m²).</summary>
        public float Area => _count * CellSize * CellSize;

        private BrushMask _limit;

        /// <summary>
        /// Optional fence (Block paint): when set, cells can only be painted where the limit mask is
        /// painted; erasing is never limited. Must use the same cell size (otherwise it is ignored).
        /// Snapshots and Restore do not carry it — the owner re-attaches it.
        /// </summary>
        public BrushMask Limit
        {
            get => _limit;
            set => _limit = value == null || value == this || math.abs(value.CellSize - CellSize) > 1e-6f ? null : value;
        }

        // ───────────────────────────── cells ─────────────────────────────

        private static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
        private static int2 FromKey(long k) => new int2((int)(k >> 32), (int)(uint)k);

        private bool[] Tile(int i, int j, bool create)
        {
            long key = Key(i >> TileBits, j >> TileBits);
            if (key == _lastTileKey) return _lastTile;
            if (!_tiles.TryGetValue(key, out bool[] tile))
            {
                if (!create) return null;
                _tiles[key] = tile = new bool[TileSize * TileSize];
            }
            _lastTileKey = key;
            _lastTile = tile;
            return tile;
        }

        /// <summary>True if cell (i, j) is painted.</summary>
        public bool IsPainted(int i, int j)
        {
            bool[] tile = Tile(i, j, false);
            return tile != null && tile[((j & (TileSize - 1)) << TileBits) | (i & (TileSize - 1))];
        }

        /// <summary>True if the cell containing world point p (XZ) is painted.</summary>
        public bool IsPaintedAt(float2 p) => IsPainted((int)math.floor(p.x / CellSize), (int)math.floor(p.y / CellSize));

        private bool Set(int i, int j, bool painted)
        {
            if (painted && _limit != null && !_limit.IsPainted(i, j)) return false;
            bool[] tile = Tile(i, j, painted);
            if (tile == null) return false;
            int index = ((j & (TileSize - 1)) << TileBits) | (i & (TileSize - 1));
            if (tile[index] == painted) return false;
            tile[index] = painted;
            _count += painted ? 1 : -1;
            return true;
        }

        private IEnumerable<int2> PaintedCells()
        {
            foreach (var pair in _tiles)
            {
                int2 origin = FromKey(pair.Key) * TileSize;
                bool[] tile = pair.Value;
                for (int index = 0; index < tile.Length; index++)
                    if (tile[index]) yield return origin + new int2(index & (TileSize - 1), index >> TileBits);
            }
        }

        // ───────────────────────────── undo ─────────────────────────────

        /// <summary>Immutable copy of the painted state (undo/redo, hand-off to a worker thread).</summary>
        public sealed class Snapshot
        {
            internal Dictionary<long, bool[]> Tiles;
            internal int Count;
            internal long[] EdgeCells;
            internal float CellSize;
        }

        public Snapshot Save()
        {
            var copy = new Dictionary<long, bool[]>(_tiles.Count);
            foreach (var pair in _tiles) copy[pair.Key] = (bool[])pair.Value.Clone();
            var edges = new long[_edgeCells.Count];
            _edgeCells.CopyTo(edges);
            return new Snapshot { Tiles = copy, Count = _count, EdgeCells = edges, CellSize = CellSize };
        }

        /// <summary>Restores a snapshot taken from a mask with the same cell size (others are ignored).</summary>
        public void Restore(Snapshot state)
        {
            if (state == null || math.abs(state.CellSize - CellSize) > 1e-6f) return;
            _tiles.Clear();
            foreach (var pair in state.Tiles) _tiles[pair.Key] = (bool[])pair.Value.Clone();
            _count = state.Count;
            _edgeCells.Clear();
            _edgeCells.UnionWith(state.EdgeCells);
            _lastTileKey = long.MinValue;
            _lastTile = null;
            Version++;
        }

        /// <summary>A new mask holding the snapshot (for worker threads: never shares arrays).</summary>
        public static BrushMask FromSnapshot(Snapshot state)
        {
            var m = new BrushMask(state?.CellSize ?? DefaultCellSize);
            m.Restore(state);
            return m;
        }

        public void Clear()
        {
            if (_count == 0 && _tiles.Count == 0) return;
            _tiles.Clear();
            _lastTileKey = long.MinValue;
            _lastTile = null;
            _count = 0;
            _edgeCells.Clear();
            Version++;
        }

        // ───────────────────────────── painting ─────────────────────────────

        /// <summary>
        /// Stamp (centre XZ, radius = half side for squares): paints or erases the cells whose centre
        /// is inside. Squares are world-aligned unless angleDegrees is set. A tiny brush still takes
        /// the cell under the cursor (like the game's 1 m brush).
        /// </summary>
        public void Stamp(float2 centre, float radius, bool erase, bool square = false, float angleDegrees = 0f)
        {
            if (!Poly.IsFinite(centre) || float.IsNaN(radius)) return;
            float cell = CellSize;
            radius = math.clamp(radius, 0.5f * cell, MaxDiameter);
            if (square && math.abs(angleDegrees % 90f) > 0.01f)
            {
                StampRotatedSquare(centre, radius, erase, math.radians(angleDegrees));
                return;
            }
            int2 min = (int2)math.floor((centre - radius) / cell), max = (int2)math.floor((centre + radius) / cell);
            _changed.Clear();
            float2 local = centre / cell - 0.5f;
            float r2 = radius * radius / (cell * cell);
            for (int i = min.x; i <= max.x; i++)
            {
                // Column extent of the stamp: no per-cell test.
                float dx = i - local.x, rest = r2 - dx * dx;
                if (!square && rest < 0f) continue;
                if (square && math.abs(dx) > radius / cell) continue;
                float half = square ? radius / cell : math.sqrt(rest);
                int j0 = math.max(min.y, (int)math.ceil(local.y - half)), j1 = math.min(max.y, (int)math.floor(local.y + half));
                for (int j = j0; j <= j1; j++)
                    if (Set(i, j, !erase)) _changed.Add(new int2(i, j));
            }
            CommitChanges();
        }

        private void StampRotatedSquare(float2 centre, float radius, bool erase, float angle)
        {
            _changed.Clear();
            float cell = CellSize;
            float2 axisX = new float2(math.cos(angle), math.sin(angle)), axisZ = new float2(-axisX.y, axisX.x);
            float reach = radius * math.SQRT2;
            int2 min = (int2)math.floor((centre - reach) / cell), max = (int2)math.floor((centre + reach) / cell);
            for (int i = min.x; i <= max.x; i++)
                for (int j = min.y; j <= max.y; j++)
                {
                    float2 d = (new float2(i, j) + 0.5f) * cell - centre;
                    if (math.abs(math.dot(d, axisX)) > radius || math.abs(math.dot(d, axisZ)) > radius) continue;
                    if (Set(i, j, !erase)) _changed.Add(new int2(i, j));
                }
            CommitChanges();
        }

        /// <summary>
        /// Stroke from `from` (already stamped last frame) to `to`: stamps close enough that a fast
        /// mouse leaves no gaps.
        /// </summary>
        /// <returns>
        /// False when <paramref name="to"/> is still within half a cell of <paramref name="from"/> (nothing stamped): the caller
        /// must then keep <paramref name="from"/> as the stroke's last point. Moving it on anyway made a slow drag (a few dm a
        /// frame) paint nothing at all after the first stamp.
        /// </returns>
        public bool Stroke(float2 from, float2 to, float radius, bool erase, bool square = false, float angleDegrees = 0f)
        {
            float length = math.distance(from, to);
            if (!(length >= 0.5f * CellSize)) return false;
            int steps = math.max(1, (int)math.ceil(length / math.max(radius * 0.25f, CellSize)));
            steps = math.min(steps, 4096);
            for (int k = 1; k <= steps; k++)
                Stamp(math.lerp(from, to, (float)k / steps), radius, erase, square, angleDegrees);
            return true;
        }

        /// <summary>Paints (or erases) every cell whose centre lies inside the ring (e.g. a picked block).</summary>
        public void PaintPolygon(IReadOnlyList<float2> ring, bool erase = false)
        {
            if (ring == null || ring.Count < 3) return;
            _changed.Clear();
            float cell = CellSize;
            Poly.Bounds(ring, out var mn, out var mx);
            int j0 = (int)math.floor(mn.y / cell), j1 = (int)math.floor(mx.y / cell);
            var xs = new List<float>();
            for (int j = j0; j <= j1; j++)
            {
                float y = (j + 0.5f) * cell;
                xs.Clear();
                for (int k = 0; k < ring.Count; k++)
                {
                    float2 a = ring[k], b = ring[(k + 1) % ring.Count];
                    if ((a.y > y) != (b.y > y)) xs.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int i0 = (int)math.ceil(xs[k] / cell - 0.5f), i1 = (int)math.floor(xs[k + 1] / cell - 0.5f);
                    for (int i = i0; i <= i1; i++)
                        if (Set(i, j, !erase)) _changed.Add(new int2(i, j));
                }
            }
            CommitChanges();
        }

        /// <summary>Erases every painted cell whose centre satisfies the predicate; returns how many were erased.</summary>
        public int EraseWhere(Func<float2, bool> atCellCentre)
        {
            if (atCellCentre == null || _count == 0) return 0;
            var hits = new List<int2>();
            foreach (int2 c in PaintedCells())
                if (atCellCentre(((float2)c + 0.5f) * CellSize)) hits.Add(c);
            _changed.Clear();
            foreach (int2 c in hits)
                if (Set(c.x, c.y, false)) _changed.Add(c);
            CommitChanges();
            return hits.Count;
        }

        /// <summary>
        /// A new mask (same cell size) with the painted cells 4-connected to the cell under seed;
        /// empty when that cell is not painted. maxCells bounds the flood (the rest is left out).
        /// </summary>
        public BrushMask ConnectedComponent(float2 seed, int maxCells = 4_000_000)
        {
            var result = new BrushMask(CellSize);
            if (!Poly.IsFinite(seed)) return result;
            int2 start = (int2)math.floor(seed / CellSize);
            if (!IsPainted(start.x, start.y)) return result;
            var seen = new HashSet<long> { Key(start.x, start.y) };
            var stack = new List<int2> { start };
            result._changed.Clear();
            while (stack.Count > 0 && seen.Count <= maxCells)
            {
                int2 c = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (result.Set(c.x, c.y, true)) result._changed.Add(c);
                Visit(c.x + 1, c.y); Visit(c.x - 1, c.y); Visit(c.x, c.y + 1); Visit(c.x, c.y - 1);
            }
            result.CommitChanges();
            return result;

            void Visit(int i, int j)
            {
                if (IsPainted(i, j) && seen.Add(Key(i, j))) stack.Add(new int2(i, j));
            }
        }

        private void CommitChanges()
        {
            if (_changed.Count == 0) return;
            Version++;
            foreach (int2 c in _changed)
            {
                UpdateEdge(c.x, c.y);
                UpdateEdge(c.x + 1, c.y);
                UpdateEdge(c.x - 1, c.y);
                UpdateEdge(c.x, c.y + 1);
                UpdateEdge(c.x, c.y - 1);
            }
        }

        private void UpdateEdge(int i, int j)
        {
            bool edge = IsPainted(i, j) && (!IsPainted(i + 1, j) || !IsPainted(i - 1, j) || !IsPainted(i, j + 1) || !IsPainted(i, j - 1));
            if (edge) _edgeCells.Add(Key(i, j)); else _edgeCells.Remove(Key(i, j));
        }

        // ───────────────────────────── preview ─────────────────────────────

        /// <summary>
        /// Raw cell boundary segments (XZ) for the live preview while painting: aligned sides merged
        /// (at most MaxRun cells), built from the edge cells only.
        /// </summary>
        public List<(float2 a, float2 b)> RawBoundary()
        {
            var horizontal = new Dictionary<int, List<int>>();
            var vertical = new Dictionary<int, List<int>>();
            void Add(Dictionary<int, List<int>> lines, int line, int at)
            {
                if (!lines.TryGetValue(line, out var list)) lines[line] = list = new List<int>();
                list.Add(at);
            }
            foreach (long k in _edgeCells)
            {
                int2 c = FromKey(k);
                if (!IsPainted(c.x, c.y - 1)) Add(horizontal, 2 * c.y, c.x);
                if (!IsPainted(c.x, c.y + 1)) Add(horizontal, 2 * (c.y + 1) + 1, c.x);
                if (!IsPainted(c.x - 1, c.y)) Add(vertical, 2 * c.x, c.y);
                if (!IsPainted(c.x + 1, c.y)) Add(vertical, 2 * (c.x + 1) + 1, c.y);
            }
            var result = new List<(float2, float2)>();
            float cell = CellSize;
            void Merge(Dictionary<int, List<int>> lines, bool isHorizontal)
            {
                foreach (var pair in lines)
                {
                    // Key = 2 × line (+1 by side, so opposite borders on the same line never merge).
                    int line = pair.Key >> 1;
                    List<int> at = pair.Value;
                    at.Sort();
                    int start = 0;
                    for (int n = 1; n <= at.Count; n++)
                    {
                        if (n < at.Count && at[n] == at[n - 1] + 1 && n - start < MaxRun) continue;
                        float from = at[start] * cell, to = (at[n - 1] + 1) * cell, fixedAt = line * cell;
                        result.Add(isHorizontal ? (new float2(from, fixedAt), new float2(to, fixedAt)) : (new float2(fixedAt, from), new float2(fixedAt, to)));
                        start = n;
                    }
                }
            }
            Merge(horizontal, true);
            Merge(vertical, false);
            return result;
        }

        /// <summary>
        /// Filled preview: horizontal runs (XZ, through cell-row centres) of painted cells, at most
        /// maxCells long, read straight from the tiles. Drawn as projected lines of width CellSize they
        /// cover the painted area exactly. rowStep &gt; 1 (big areas): every rowStep-th row, draw it
        /// rowStep × CellSize wide.
        /// </summary>
        public List<(float2 a, float2 b)> FillRuns(int maxCells = 16, int rowStep = 1)
        {
            var result = new List<(float2, float2)>();
            rowStep = math.max(1, rowStep);
            maxCells = math.max(1, maxCells);
            float cell = CellSize;
            foreach (var pair in _tiles)
            {
                int2 origin = FromKey(pair.Key) * TileSize;
                bool[] tile = pair.Value;
                for (int row = 0; row < TileSize; row++)
                {
                    if (((origin.y + row) % rowStep + rowStep) % rowStep != 0) continue;
                    int start = -1;
                    for (int col = 0; col <= TileSize; col++)
                    {
                        bool painted = col < TileSize && tile[(row << TileBits) | col];
                        if (painted && start < 0) start = col;
                        if (start >= 0 && (!painted || col - start >= maxCells))
                        {
                            float z = (origin.y + row + 0.5f * rowStep) * cell;
                            result.Add((new float2((origin.x + start) * cell, z), new float2((origin.x + col) * cell, z)));
                            start = painted ? col : -1;
                        }
                    }
                }
            }
            return result;
        }

        // ───────────────────────────── outlines ─────────────────────────────

        /// <summary>
        /// Smoothed outline of the largest painted island with its holes filled, y = 0; empty when
        /// nothing usable (or less than minArea m²). The ring is simple and counter-clockwise.
        /// (Grid Road Generator behaviour.)
        /// </summary>
        public List<float3> Outline(float minArea = 100f)
        {
            var result = new List<float3>();
            var regions = Outlines(keepIslands: false, keepHoles: false, minIslandArea: minArea);
            if (regions.Count == 0) return result;
            foreach (var p in regions[0].Outer) result.Add(new float3(p.x, 0f, p.y));
            return result;
        }

        /// <summary>
        /// Smoothed outlines of the painted area as regions (outer CCW + holes CW), largest first.
        /// keepIslands = false keeps only the largest island; keepHoles = false fills holes. Islands
        /// below minIslandArea and holes below minHoleArea (default: 4 cells) are dropped / filled.
        /// Every ring is simple and rings of a region never cross. smoothPasses caps the moving-average
        /// passes (6 = Paint mode; fewer keeps small strokes closer to the painted cells).
        /// </summary>
        public List<Region> Outlines(bool keepIslands = true, bool keepHoles = true, float minIslandArea = 0f, float minHoleArea = -1f, int smoothPasses = 6)
        {
            var regions = new List<Region>();
            if (_count == 0) return regions;
            float cell = CellSize, cellArea = cell * cell;
            if (minHoleArea < 0) minHoleArea = 4 * cellArea;

            // Local grid with a two-cell margin.
            var painted = new List<int2>(PaintedCells());
            int2 min = new int2(int.MaxValue), max = new int2(int.MinValue);
            foreach (int2 c in painted) { min = math.min(min, c); max = math.max(max, c); }
            min -= 2; max += 2;
            int nx = max.x - min.x + 1, nz = max.y - min.y + 1;
            if ((long)nx * nz > 50_000_000L) return regions; // absurd extents: refuse rather than allocate gigabytes
            var grid = new bool[nx * nz];
            foreach (int2 c in painted) { int2 q = c - min; grid[q.y * nx + q.x] = true; }

            // Islands (4-neighbourhood).
            var label = new int[nx * nz];
            var sizes = new List<int> { 0 };
            var stack = new List<int>();
            for (int s = 0; s < grid.Length; s++)
            {
                if (!grid[s] || label[s] != 0) continue;
                int id = sizes.Count, size = 0;
                sizes.Add(0);
                stack.Add(s); label[s] = id;
                while (stack.Count > 0)
                {
                    int q = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    size++;
                    int qx = q % nx, qz = q / nx;
                    if (qx > 0) Visit(q - 1); if (qx < nx - 1) Visit(q + 1);
                    if (qz > 0) Visit(q - nx); if (qz < nz - 1) Visit(q + nx);
                    void Visit(int n2) { if (grid[n2] && label[n2] == 0) { label[n2] = id; stack.Add(n2); } }
                }
                sizes[id] = size;
            }
            int best = 1;
            for (int id = 1; id < sizes.Count; id++) if (sizes[id] > sizes[best]) best = id;
            for (int s = 0; s < grid.Length; s++)
            {
                if (!grid[s]) continue;
                int id = label[s];
                bool keep = keepIslands ? sizes[id] * cellArea >= minIslandArea : id == best;
                if (!keep) grid[s] = false;
            }

            FillPinches(grid, nx, nz);

            // Holes: unpainted components the outside does not reach.
            var outside = new bool[nx * nz];
            FloodUnpainted(grid, outside, nx, nz, 0);
            var holeLabel = new int[nx * nz];
            for (int s = 0; s < grid.Length; s++)
            {
                if (grid[s] || outside[s] || holeLabel[s] != 0) continue;
                var cells = new List<int>();
                stack.Add(s); holeLabel[s] = 1;
                while (stack.Count > 0)
                {
                    int q = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    cells.Add(q);
                    int qx = q % nx, qz = q / nx;
                    if (qx > 0) Visit(q - 1); if (qx < nx - 1) Visit(q + 1);
                    if (qz > 0) Visit(q - nx); if (qz < nz - 1) Visit(q + nx);
                    void Visit(int n2) { if (!grid[n2] && !outside[n2] && holeLabel[n2] == 0) { holeLabel[n2] = 1; stack.Add(n2); } }
                }
                if (!keepHoles || cells.Count * cellArea < minHoleArea)
                    foreach (int q in cells) grid[q] = true;
            }
            FillPinches(grid, nx, nz);

            // Directed cell borders, interior on the left (x right, z up ⇒ outer rings CCW, holes CW).
            bool In(int x, int z) => x >= 0 && z >= 0 && x < nx && z < nz && grid[z * nx + x];
            var outgoing = new Dictionary<long, List<int2>>();
            void AddEdge(int2 a, int2 b)
            {
                long key = Key(a.x, a.y);
                if (!outgoing.TryGetValue(key, out var list)) outgoing[key] = list = new List<int2>(1);
                list.Add(b);
            }
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    if (!In(x, z)) continue;
                    if (!In(x, z - 1)) AddEdge(new int2(x, z), new int2(x + 1, z));
                    if (!In(x + 1, z)) AddEdge(new int2(x + 1, z), new int2(x + 1, z + 1));
                    if (!In(x, z + 1)) AddEdge(new int2(x + 1, z + 1), new int2(x, z + 1));
                    if (!In(x - 1, z)) AddEdge(new int2(x, z + 1), new int2(x, z));
                }

            var outers = new List<List<float2>>();
            var holes = new List<List<float2>>();
            while (outgoing.Count > 0)
            {
                long startKey = 0;
                foreach (var k in outgoing.Keys) { startKey = k; break; }
                int2 start = FromKey(startKey), cur = start, dir = default;
                var loop = new List<int2>();
                for (int guard = 0; guard < 4 * nx * nz + 4; guard++)
                {
                    long key = Key(cur.x, cur.y);
                    if (!outgoing.TryGetValue(key, out var options) || options.Count == 0) break;
                    int2 chosen = options[0];
                    if (options.Count > 1)
                    {
                        // Defensive: pinches were filled, but prefer the left turn to stay on one island.
                        int2 left = new int2(-dir.y, dir.x);
                        foreach (int2 o in options) if (math.all(o - cur == left)) chosen = o;
                    }
                    options.Remove(chosen);
                    if (options.Count == 0) outgoing.Remove(key);
                    loop.Add(cur);
                    dir = chosen - cur;
                    cur = chosen;
                    if (math.all(cur == start)) break;
                }
                if (loop.Count < 4) continue;
                var corners = new List<float2>();
                for (int i = 0; i < loop.Count; i++)
                {
                    int2 a = loop[(i + loop.Count - 1) % loop.Count], b = loop[i], c = loop[(i + 1) % loop.Count];
                    int2 d1 = b - a, d2 = c - b;
                    if (d1.x * d2.y - d1.y * d2.x != 0) corners.Add((float2)(b + min) * cell);
                }
                if (corners.Count < 4) continue;
                if (Poly.SignedAreaD(corners) > 0) outers.Add(corners); else holes.Add(corners);
            }

            // Group holes under their outers (raw grid rings: exact, no shared vertices).
            outers.Sort((a, b) => Poly.SignedAreaD(b).CompareTo(Poly.SignedAreaD(a)));
            var holeLists = new List<List<float2>>[outers.Count];
            for (int k = 0; k < outers.Count; k++) holeLists[k] = new List<List<float2>>();
            foreach (var h in holes)
            {
                int owner = -1; double ownerArea = double.MaxValue;
                // Probe: centre of the cell left of the hole's first edge (painted side).
                float2 e0 = h[0], e1 = h[1];
                float2 probe = (e0 + e1) * 0.5f + new float2(-(e1 - e0).y, (e1 - e0).x) * (0.5f * cell / math.max(math.distance(e0, e1), 1e-6f));
                for (int k = 0; k < outers.Count; k++)
                {
                    double a = Poly.SignedAreaD(outers[k]);
                    if (a < ownerArea && Poly.Contains(outers[k], probe)) { owner = k; ownerArea = a; }
                }
                if (owner >= 0) holeLists[owner].Add(h);
            }

            for (int k = 0; k < outers.Count; k++)
            {
                if (Poly.SignedAreaD(outers[k]) < minIslandArea) continue;
                var reg = SmoothRegion(outers[k], holeLists[k], cell, smoothPasses);
                if (reg != null) regions.Add(reg);
            }
            regions.Sort((a, b) => b.Area.CompareTo(a.Area));
            return regions;
        }

        private static void FillPinches(bool[] grid, int nx, int nz)
        {
            // Two cells touching only by a corner (either painted or unpainted diagonal): fill one, so
            // every boundary vertex has exactly one way out and rings never touch themselves.
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int z = 0; z + 1 < nz; z++)
                    for (int x = 0; x + 1 < nx; x++)
                    {
                        int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
                        if (grid[a] && grid[d] && !grid[b] && !grid[c]) { grid[b] = true; changed = true; }
                        else if (grid[b] && grid[c] && !grid[a] && !grid[d]) { grid[a] = true; changed = true; }
                    }
            }
        }

        private static void FloodUnpainted(bool[] grid, bool[] mark, int nx, int nz, int seed)
        {
            var stack = new List<int> { seed };
            mark[seed] = true;
            while (stack.Count > 0)
            {
                int q = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                int qx = q % nx, qz = q / nx;
                if (qx > 0) Visit(q - 1); if (qx < nx - 1) Visit(q + 1);
                if (qz > 0) Visit(q - nx); if (qz < nz - 1) Visit(q + nx);
                void Visit(int n2) { if (!grid[n2] && !mark[n2]) { mark[n2] = true; stack.Add(n2); } }
            }
        }

        /// <summary>
        /// Regular resample + moving-average smoothing + Douglas–Peucker removes the grid's stair
        /// steps; smoothing is reduced (6, 4, 2, 0 passes) until every ring is simple and no two
        /// rings cross.
        /// </summary>
        private static Region SmoothRegion(List<float2> outer, List<List<float2>> holes, float cell, int maxPasses = 6)
        {
            for (int passes = math.clamp(maxPasses, 0, 12); passes >= 0; passes -= 2)
            {
                var o = SmoothRing(outer, passes, cell);
                if (o == null) continue;
                var reg = new Region(Poly.EnsureCcw(o));
                bool ok = true;
                foreach (var h in holes)
                {
                    var hs = SmoothRing(h, passes, cell);
                    if (hs == null) continue; // a hole that smooths away is filled
                    hs = Poly.EnsureCw(hs);
                    if (!Poly.Contains(reg.Outer, hs[0]) || RingsCross(reg.Outer, hs)) { ok = false; break; }
                    foreach (var other in reg.Holes) if (RingsCross(other, hs) || Poly.Contains(other, hs[0])) { ok = false; break; }
                    if (!ok) break;
                    reg.Holes.Add(hs);
                }
                if (ok) return reg;
            }
            return null;
        }

        private static float2[] SmoothRing(List<float2> corners, int passes, float cell)
        {
            var even = Poly.Resample(corners, cell);
            if (even.Length < 4) return null;
            var smooth = Poly.SmoothLoop(even, passes, 3);
            var simple = Poly.Simplify(smooth, 0.15f * cell);
            if (simple.Length < 3 || !Poly.IsSimple(simple) || math.abs(Poly.SignedAreaD(simple)) < cell * cell * 0.5) return null;
            return simple;
        }

        private static bool RingsCross(float2[] a, float2[] b)
        {
            Poly.Bounds(a, out var amin, out var amax);
            Poly.Bounds(b, out var bmin, out var bmax);
            if (math.any(amax < bmin) || math.any(bmax < amin)) return false;
            for (int i = 0; i < a.Length; i++)
            {
                float2 p = a[i], q = a[(i + 1) % a.Length];
                float2 lo = math.min(p, q), hi = math.max(p, q);
                if (math.any(hi < bmin) || math.any(lo > bmax)) continue;
                for (int j = 0; j < b.Length; j++)
                {
                    float2 r = b[j], s = b[(j + 1) % b.Length];
                    if (math.max(r.x, s.x) < lo.x || math.min(r.x, s.x) > hi.x || math.max(r.y, s.y) < lo.y || math.min(r.y, s.y) > hi.y) continue;
                    if (Poly.SegmentsIntersect(p, q, r, s)) return true;
                }
            }
            return false;
        }
    }
}
