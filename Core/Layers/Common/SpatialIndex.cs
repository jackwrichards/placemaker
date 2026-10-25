// Uniform-grid indexes for the clearance checks every layer runs thousands of times.
// A 5 ha park tests ~30k planting candidates against paths and other plants; linear scans
// (ParkManager's original HasPathClearance/TooCloseToKind) cost O(n·m) and dominated planning
// time. Buckets keep each query local.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    /// <summary>Discs (objects) with an integer kind, queried by neighbourhood.</summary>
    public sealed class DiscIndex
    {
        public struct Item { public float2 P; public float R; public int Kind; }

        private readonly float _cell;
        private readonly CellMap _cells = new CellMap();
        public readonly List<Item> Items = new List<Item>();
        public float MaxRadius { get; private set; }
        // (perf) Discs wider than a cell (a reserved place, a pond) are kept apart and always looked at: one 14 m disc used to
        // widen every search of a park's ~400 000 planting checks from ~20 cells to ~50.
        private readonly List<int> _big = new List<int>();
        private float _smallMax;

        public DiscIndex(float cell = 8f) { _cell = math.max(0.5f, cell); }

        public int Add(float2 p, float r, int kind)
        {
            int id = Items.Count;
            Items.Add(new Item { P = p, R = r, Kind = kind });
            MaxRadius = math.max(MaxRadius, r);
            if (r > _cell) { _big.Add(id); return id; }
            _smallMax = math.max(_smallMax, r);
            var c = Cell(p);
            _cells.Add(c.x, c.y, id);
            return id;
        }

        /// <summary>Indices of items whose centre lies within <paramref name="radius"/> + MaxRadius of p (superset).</summary>
        public void Query(float2 p, float radius, List<int> into)
        {
            into.Clear();
            float reach = radius + _smallMax;
            var lo = Cell(p - reach); var hi = Cell(p + reach);
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                    for (int e = _cells.First(x, y); e >= 0; e = _cells.Next(e)) into.Add(_cells.Item(e));
            into.AddRange(_big);
        }

        /// <summary>True when a disc (p, r) plus <paramref name="gap"/> overlaps any item.</summary>
        public bool Overlaps(float2 p, float r, float gap = 0f)
        {
            foreach (int i in _big)
            {
                var it = Items[i];
                float d = r + it.R + gap;
                if (math.distancesq(p, it.P) < d * d) return true;
            }
            float reach = r + gap + _smallMax;
            var lo = Cell(p - reach); var hi = Cell(p + reach);
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    for (int e = _cells.First(x, y); e >= 0; e = _cells.Next(e))
                    {
                        var it = Items[_cells.Item(e)];
                        float d = r + it.R + gap;
                        if (math.distancesq(p, it.P) < d * d) return true;
                    }
                }
            return false;
        }

        /// <summary>Distance from p to the nearest item rim (negative inside), capped at <paramref name="cap"/>.</summary>
        public float Clearance(float2 p, float cap)
        {
            float best = cap;
            foreach (int i in _big) { var it = Items[i]; best = math.min(best, math.distance(p, it.P) - it.R); }
            float reach = cap + _smallMax;
            var lo = Cell(p - reach); var hi = Cell(p + reach);
            // (perf) Rings of cells outwards from p's cell, stopping once no unseen disc can be nearer: a disc in ring k has its
            // centre more than (k − 1) cells away. Same discs as the whole box up to that point, so the same minimum.
            var pc = Cell(p);
            int rings = math.max(math.max(pc.x - lo.x, hi.x - pc.x), math.max(pc.y - lo.y, hi.y - pc.y));
            for (int k = 0; k <= rings; k++)
            {
                if (k > 0 && best <= (k - 1) * _cell - _smallMax) break;
                int y0 = math.max(lo.y, pc.y - k), y1 = math.min(hi.y, pc.y + k);
                for (int y = y0; y <= y1; y++)
                {
                    int step = y == pc.y - k || y == pc.y + k ? 1 : 2 * k;
                    for (int x = pc.x - k; x <= pc.x + k; x += step)
                    {
                        if (x < lo.x || x > hi.x) continue;
                        for (int e = _cells.First(x, y); e >= 0; e = _cells.Next(e))
                        {
                            var it = Items[_cells.Item(e)];
                            best = math.min(best, math.distance(p, it.P) - it.R);
                        }
                    }
                }
            }
            return best;
        }

        private int2 Cell(float2 p) => (int2)math.floor(p / _cell);
    }

    /// <summary>Segments with half widths (paths, fences, reserved corridors).</summary>
    public sealed class SegmentIndex
    {
        public struct Seg { public float2 A, B; public float HalfWidth; public int Kind; }

        private readonly float _cell;
        private readonly CellMap _cells = new CellMap();
        public readonly List<Seg> Segments = new List<Seg>();
        private int[] _stamp = new int[64];
        private int _stampValue;
        private float _maxHalfWidth;

        public SegmentIndex(float cell = 10f) { _cell = math.max(0.5f, cell); }

        public int Count => Segments.Count;

        public void Add(float2 a, float2 b, float halfWidth, int kind = 0)
        {
            int id = Segments.Count;
            Segments.Add(new Seg { A = a, B = b, HalfWidth = halfWidth, Kind = kind });
            _maxHalfWidth = math.max(_maxHalfWidth, halfWidth);
            if (_stamp.Length < Segments.Count) Array.Resize(ref _stamp, Segments.Count * 2);
            var lo = Cell(math.min(a, b) - halfWidth); var hi = Cell(math.max(a, b) + halfWidth);
            // Long diagonal segments would fill a big rectangle of cells; only cells near the line matter.
            float reach = halfWidth + _cell * 0.75f;
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var centre = (new float2(x, y) + 0.5f) * _cell;
                    if (DistSq(centre, a, b) > reach * reach) continue;
                    _cells.Add(x, y, id);
                }
        }

        /// <summary>True when a disc (p, r) touches any segment corridor (+gap).</summary>
        public bool Overlaps(float2 p, float r, float gap = 0f) => AnyBelow(p, r + gap + 0.001f, r + gap);

        /// <summary>
        /// (perf) Clearance(p, cap) &lt; t for t ≤ cap, stopping at the first segment closer than t (the same segments are looked
        /// at, in the same order, so the answer is exactly Clearance's).
        /// </summary>
        public bool AnyBelow(float2 p, float cap, float t)
        {
            if (Segments.Count == 0) return cap < t;
            unchecked { _stampValue++; }
            if (_stampValue == 0) { Array.Clear(_stamp, 0, _stamp.Length); _stampValue = 1; }
            if (cap < t) return true;
            var lo = Cell(p - cap - _cell); var hi = Cell(p + cap + _cell);
            long cellsWide = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1);
            if (cellsWide > Segments.Count * 4L)
            {
                for (int i = 0; i < Segments.Count; i++) if (Dist(p, i) < t) return true;
                return false;
            }
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    for (int e = _cells.First(x, y); e >= 0; e = _cells.Next(e))
                    {
                        int i = _cells.Item(e);
                        if (_stamp[i] == _stampValue) continue;
                        _stamp[i] = _stampValue;
                        if (Dist(p, i) < t) return true;
                    }
                }
            return false;
        }

        /// <summary>Distance from p to the nearest corridor edge (segment distance − half width), capped.</summary>
        public float Clearance(float2 p, float cap)
        {
            if (Segments.Count == 0) return cap;
            unchecked { _stampValue++; }
            if (_stampValue == 0) { Array.Clear(_stamp, 0, _stamp.Length); _stampValue = 1; }
            float best = cap;
            // Segments are registered in every cell within halfWidth + 0.75 cell of the line, so
            // scanning cells within `cap` of p plus one cell of slack finds every candidate.
            var lo = Cell(p - cap - _cell); var hi = Cell(p + cap + _cell);
            long cellsWide = (long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1);
            // A box wider than the segments are many: every segment (so not only the box's), unless the rings below settle it
            // within as many cells as there are segments (then without the box: the stop rule holds for every segment).
            bool all = cellsWide > Segments.Count * 4L;
            if (all) { lo = new int2(int.MinValue / 2); hi = new int2(int.MaxValue / 2); }
            long budget = all ? Segments.Count : long.MaxValue;
            // (perf) Rings of cells outwards from p's cell, stopping once no unseen segment can be nearer: a segment first met in
            // ring k has its closest point (whose cell it is registered in) in ring k or beyond, so more than (k − 1) cells
            // away. Same segments as the whole box (or all of them) up to that point, so the same minimum.
            var pc = Cell(p);
            long rings = math.max(math.max((long)pc.x - lo.x, (long)hi.x - pc.x), math.max((long)pc.y - lo.y, (long)hi.y - pc.y));
            for (int k = 0; k <= rings; k++)
            {
                if (k > 0 && best <= (k - 1) * _cell - _maxHalfWidth) return best;
                int y0 = math.max(lo.y, pc.y - k), y1 = math.min(hi.y, pc.y + k);
                for (int y = y0; y <= y1; y++)
                {
                    int step = y == pc.y - k || y == pc.y + k ? 1 : 2 * k;   // the ring's top and bottom rows whole, its sides
                    for (int x = pc.x - k; x <= pc.x + k; x += step)
                    {
                        if (x < lo.x || x > hi.x) continue;
                        if (--budget < 0) goto everySegment;
                        for (int e = _cells.First(x, y); e >= 0; e = _cells.Next(e))
                        {
                            int i = _cells.Item(e);
                            if (_stamp[i] == _stampValue) continue;
                            _stamp[i] = _stampValue;
                            best = math.min(best, Dist(p, i));
                        }
                    }
                }
            }
            if (!all) return best;
        everySegment:
            for (int i = 0; i < Segments.Count; i++) best = math.min(best, Dist(p, i));
            return best;
        }

        /// <summary>
        /// (perf) Each segment registered in a cell the straight line a→b passes through, once: every segment that touches the
        /// line is among them (the cell holding the touching point lies within 0.71 cell of both).
        /// </summary>
        public void Along(float2 a, float2 b, List<int> into)
        {
            into.Clear();
            if (Segments.Count == 0) return;
            unchecked { _stampValue++; }
            if (_stampValue == 0) { Array.Clear(_stamp, 0, _stamp.Length); _stampValue = 1; }
            var lo = Cell(math.min(a, b)); var hi = Cell(math.max(a, b));
            float reach = _cell * 0.75f;
            for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    if (lo.x != hi.x && lo.y != hi.y && DistSq((new float2(x, y) + 0.5f) * _cell, a, b) > reach * reach) continue;
                    for (int e = _cells.First(x, y); e >= 0; e = _cells.Next(e))
                    {
                        int i = _cells.Item(e);
                        if (_stamp[i] == _stampValue) continue;
                        _stamp[i] = _stampValue;
                        into.Add(i);
                    }
                }
        }

        /// <summary>Nearest segment index within cap (−1 if none) and the closest point on it.</summary>
        public int Nearest(float2 p, float cap, out float2 closest)
        {
            closest = p;
            int bestId = -1; float best = cap;
            for (int i = 0; i < Segments.Count; i++)
            {
                float d = Dist(p, i);
                if (d < best) { best = d; bestId = i; }
            }
            if (bestId >= 0)
            {
                var s = Segments[bestId];
                closest = ClosestPoint(p, s.A, s.B);
            }
            return bestId;
        }

        private float Dist(float2 p, int i)
        {
            var s = Segments[i];
            return math.sqrt(DistSq(p, s.A, s.B)) - s.HalfWidth;
        }

        public static float DistSq(float2 p, float2 a, float2 b) => math.distancesq(p, ClosestPoint(p, a, b));

        public static float2 ClosestPoint(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float len = math.lengthsq(ab);
            float t = len > 1e-9f ? math.saturate(math.dot(p - a, ab) / len) : 0f;
            return a + ab * t;
        }

        private int2 Cell(float2 p) => (int2)math.floor(p / _cell);
    }

    /// <summary>
    /// (perf) Grid cell → its items, in the order added: an open-addressing table of cells, each a chain of entries in flat
    /// arrays. The Dictionary of Lists it replaces was a third of a park's planning time (most of it in the lookups, slower
    /// still in the game's Mono).
    /// </summary>
    internal sealed class CellMap
    {
        private int[] _kx = new int[64], _ky = new int[64], _head = new int[64], _tail = new int[64];
        private int _used;
        private int[] _item = new int[64], _next = new int[64];
        private int _entries;

        public CellMap() { for (int i = 0; i < _head.Length; i++) _head[i] = -1; }

        public void Add(int x, int y, int item)
        {
            if ((_used + 1) * 2 > _head.Length) Grow();
            int s = Slot(x, y);
            if (_head[s] < 0) { _kx[s] = x; _ky[s] = y; _used++; }
            if (_entries == _item.Length) { Array.Resize(ref _item, _entries * 2); Array.Resize(ref _next, _entries * 2); }
            int e = _entries++;
            _item[e] = item; _next[e] = -1;
            if (_head[s] < 0) _head[s] = e; else _next[_tail[s]] = e;
            _tail[s] = e;
        }

        /// <summary>The cell's first entry, -1 when empty; walk on with Next, read with Item.</summary>
        public int First(int x, int y) => _head[Slot(x, y)];
        public int Next(int e) => _next[e];
        public int Item(int e) => _item[e];

        private int Slot(int x, int y)
        {
            int mask = _head.Length - 1;
            int s = (int)((uint)(x * 73856093 ^ y * 19349663) * 2654435769u >> 7) & mask;
            while (_head[s] >= 0 && (_kx[s] != x || _ky[s] != y)) s = (s + 1) & mask;
            return s;
        }

        private void Grow()
        {
            int[] kx = _kx, ky = _ky, head = _head, tail = _tail;
            int n = head.Length * 2;
            _kx = new int[n]; _ky = new int[n]; _head = new int[n]; _tail = new int[n];
            for (int i = 0; i < n; i++) _head[i] = -1;
            for (int i = 0; i < head.Length; i++)
            {
                if (head[i] < 0) continue;
                int s = Slot(kx[i], ky[i]);
                _kx[s] = kx[i]; _ky[s] = ky[i]; _head[s] = head[i]; _tail[s] = tail[i];
            }
        }
    }
}
