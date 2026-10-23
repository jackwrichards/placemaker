// Placemaker — planar arrangement: the one robust engine behind booleans, offsets, self-intersection
// removal, polygon splitting and hole cutting.
//
// Why one engine: every "polygon with problems" case (self-touching rings, pinching offsets, split
// lines, overlapping operands) reduces to the same question — cut all segments at all crossings,
// build the planar map, and decide per face whether it is inside. Doing that once, carefully, beats
// five special-case clippers.
//
// Method: segments are snapped to a vertex grid (tolerance eps, ~4 float ulps at the input's
// magnitude), split at every crossing / near-incidence in repeated sweep rounds until stable
// (a snap-rounding style fixpoint), merged into undirected edges carrying a winding delta per
// operand, then faces are traced with a half-edge walk. Each face's winding number is measured at
// an interior sample point (ray-cast against the arrangement itself, so snapping can never make the
// classification inconsistent). Output rings are re-traced along the boundary between selected and
// unselected faces and split at repeated vertices, so every emitted ring is simple.
//
// All math is double relative to a local origin: world coordinates in CS2 reach ±7 km where float
// has only ~0.5 mm resolution.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    internal sealed class Arrangement
    {
        private struct Edge
        {
            public int U, V;
            public int DA, DB;   // winding change from right to left of U→V, per operand
            public bool Cut;     // a split line (no winding change) that separates output pieces
        }

        private double2 _origin;
        private double _eps, _eps2, _cell;
        private readonly Buf<double2> _pts = new Buf<double2>(64);
        // vertices by snap cell: the cell's first and last vertex, each vertex's next in its cell (-1: the last), in the order
        // they were made (one table and one list, not a list per cell: the allocations were a big part of every boolean)
        private readonly GridMap _grid = new GridMap();
        private readonly Buf<int> _nextInCell = new Buf<int>(64);
        private Buf<Edge> _edges = new Buf<Edge>(64);
        // the edges' other buffer: merging, splitting and pruning write the new list into it and the two swap
        private Buf<Edge> _spare = new Buf<Edge>(64);
        private readonly List<int> _starts = new List<int>();

        // Built state (its arrays kept with the instance and filled again by every build: Rent)
        private bool _built;
        private int[] _cycleOf;          // half-edge → cycle
        private int _cycleCount;
        private int[] _cycleStart;       // cycle c: its half-edges in order are _cycleFlat[_cycleStart[c] .. _cycleStart[c + 1])
        private int[] _cycleFlat;
        private int[] _windA, _windB;    // winding of the region left of each cycle
        private int[] _outStart;         // vertex v: its outgoing half-edges sorted by angle (CCW) are _outFlat[_outStart[v] .. _outStart[v + 1])
        private int[] _outFlat;
        private int[] _posInOut;         // half-edge → its index among its origin's outgoing half-edges
        // (flat arrays, not a list per vertex and an array per face: in big booleans the allocations and the collector's work
        // they caused were most of the time)

        /// <summary>A growing array read directly (List<T> is a call per element in the game's runtime).</summary>
        private sealed class Buf<T>
        {
            public T[] A;
            public int N;
            public Buf(int capacity) { A = new T[System.Math.Max(4, capacity)]; }
            public void Clear() => N = 0;
            public void Add(T x)
            {
                if (N == A.Length) System.Array.Resize(ref A, A.Length * 2);
                A[N++] = x;
            }
        }

        public double2 Origin => _origin;
        public double Eps => _eps;

        // working arrays every build needs only while it runs, kept per thread and grown as needed: a boolean makes dozens of
        // them, and a plan makes thousands of booleans (in the game every one is the garbage collector's, which stops every
        // thread, the game's frame too). Never used by two builds at once: a build never starts another.
        private sealed class Scratch
        {
            public double[] MinX = new double[64];
            public int[] Order = new int[64];
            public int[] Active = new int[64];
            public double[] ActiveMaxX = new double[64], ActiveMinY = new double[64], ActiveMaxY = new double[64];
            public List<int>[] Splits = new List<int>[64];
            public readonly EdgeMap Merge = new EdgeMap();
            public int[] Deg = new int[64], IncStart = new int[65], IncFlat = new int[128], Fill = new int[64], Parent = new int[64];
            public bool[] Alive = new bool[64], Done = new bool[64];
            public double[] Angle = new double[128], Area = new double[64];
            public bool[] Sel = new bool[64], Boundary = new bool[128], Used = new bool[128], RingAlive = new bool[64];
            public int[] RingPrev = new int[64], RingNext = new int[64];
            public readonly List<int> Verts = new List<int>();
            public readonly Dictionary<int, int> RepeatAt = new Dictionary<int, int>();
            public readonly MinXOrder ByMinX = new MinXOrder();
            public readonly ByAngle ByAngle = new ByAngle();
            // (what a build or an extraction makes and drops again, reused: a list per split edge, the split points' order, a
            // ring's points while it is traced, the loops found)
            public readonly List<List<int>> SplitLists = new List<List<int>>();
            public int SplitUsed;
            public readonly List<KeyValuePair<double, int>> Ts = new List<KeyValuePair<double, int>>();
            public readonly ListStack<int> Stack = new ListStack<int>();
            public readonly ListQueue<int> Queue = new ListQueue<int>();
            public readonly Dictionary<int, int> OuterOf = new Dictionary<int, int>();
            public readonly List<double2> Local = new List<double2>();
            public readonly List<double2> RingPts = new List<double2>(), RingKept = new List<double2>();
            public readonly List<float2> RingOut = new List<float2>();
            public readonly List<List<int>> LoopLists = new List<List<int>>();
            public int LoopUsed;
            public readonly List<List<int>> Loops = new List<List<int>>();
            public readonly List<KeyValuePair<double, float2[]>> Outers = new List<KeyValuePair<double, float2[]>>();
            public readonly List<float2[]> Holes = new List<float2[]>();

            public List<int> SplitList()
            {
                if (SplitUsed == SplitLists.Count) SplitLists.Add(new List<int>(2));
                var l = SplitLists[SplitUsed++];
                l.Clear();
                return l;
            }

            public List<int> LoopList()
            {
                if (LoopUsed == LoopLists.Count) LoopLists.Add(new List<int>());
                var l = LoopLists[LoopUsed++];
                l.Clear();
                return l;
            }

            public static T[] Fit<T>(ref T[] a, int n)
            {
                if (a == null || a.Length < n) a = new T[a == null ? math.max(n, 64) : math.max(n, a.Length * 2)];
                return a;
            }
        }

        [ThreadStatic] private static Scratch s_scratch;
        private static Scratch Work => s_scratch ?? (s_scratch = new Scratch());

        /// <summary>
        /// Edge (its two vertices as one key) → its place in the merged list: an open-addressed table whose slots are made empty
        /// by a stamp, not by clearing (a dictionary per merge, or one cleared for every merge, was a quarter of every boolean).
        /// </summary>
        private sealed class EdgeMap
        {
            private long[] _keys = new long[64];
            private int[] _values = new int[64];
            private int[] _stamps = new int[64];
            private int _stamp, _mask;

            /// <summary>Empty, with room for <paramref name="count"/> keys (a table at most half full).</summary>
            public void Reset(int count)
            {
                int size = 64;
                while (size < count * 2) size *= 2;
                if (_keys.Length < size) { _keys = new long[size]; _values = new int[size]; _stamps = new int[size]; _stamp = 0; }
                _mask = size - 1;
                if (++_stamp == int.MaxValue) { Array.Clear(_stamps, 0, _stamps.Length); _stamp = 1; }
            }

            private int Slot(long key)
            {
                ulong h = (ulong)key * 0x9E3779B97F4A7C15UL;
                return (int)(h >> 40) & _mask;
            }

            public bool TryGetValue(long key, out int value)
            {
                for (int s = Slot(key); _stamps[s] == _stamp; s = (s + 1) & _mask)
                    if (_keys[s] == key) { value = _values[s]; return true; }
                value = 0;
                return false;
            }

            /// <summary>Adds a key known not to be in the table.</summary>
            public void Add(long key, int value)
            {
                int s = Slot(key);
                while (_stamps[s] == _stamp) s = (s + 1) & _mask;
                _stamps[s] = _stamp;
                _keys[s] = key;
                _values[s] = value;
            }
        }

        /// <summary>Edge indices by their smallest x (the sweep's order).</summary>
        private sealed class MinXOrder : IComparer<int>
        {
            public double[] MinX;
            public int Compare(int a, int b) => MinX[a].CompareTo(MinX[b]);
        }

        /// <summary>
        /// Snap cell → its first and last vertex: an open-addressed table emptied by a stamp, grown when half full (a
        /// dictionary per boolean was a twentieth of a plan's garbage). Only looked up, never walked: its order is nobody's.
        /// </summary>
        private sealed class GridMap
        {
            private long[] _keys = new long[64];
            private int[] _heads = new int[64], _tails = new int[64], _stamps = new int[64];
            private int _stamp, _count;

            public void Reset(int count)
            {
                int size = 64;
                while (size < count * 2) size *= 2;
                if (_keys.Length < size) { _keys = new long[size]; _heads = new int[size]; _tails = new int[size]; _stamps = new int[size]; _stamp = 0; }
                if (++_stamp == int.MaxValue) { Array.Clear(_stamps, 0, _stamps.Length); _stamp = 1; }
                _count = 0;
            }

            private int Slot(long key) => (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 40) & (_keys.Length - 1);

            public bool TryGetValue(long key, out (int Head, int Tail) cell)
            {
                int mask = _keys.Length - 1;
                for (int i = Slot(key); _stamps[i] == _stamp; i = (i + 1) & mask)
                    if (_keys[i] == key) { cell = (_heads[i], _tails[i]); return true; }
                cell = default;
                return false;
            }

            public (int Head, int Tail) this[long key]
            {
                set
                {
                    int mask = _keys.Length - 1;
                    int i = Slot(key);
                    for (; _stamps[i] == _stamp; i = (i + 1) & mask)
                        if (_keys[i] == key) { _heads[i] = value.Head; _tails[i] = value.Tail; return; }
                    if ((_count + 1) * 2 > _keys.Length) { Grow(); this[key] = value; return; }
                    _stamps[i] = _stamp; _keys[i] = key; _heads[i] = value.Head; _tails[i] = value.Tail;
                    _count++;
                }
            }

            private void Grow()
            {
                var keys = _keys; var heads = _heads; var tails = _tails; var stamps = _stamps; int stamp = _stamp;
                int size = keys.Length * 2;
                _keys = new long[size]; _heads = new int[size]; _tails = new int[size]; _stamps = new int[size];
                _stamp = 1;
                _count = 0;
                for (int i = 0; i < keys.Length; i++) if (stamps[i] == stamp) this[keys[i]] = (heads[i], tails[i]);
            }
        }

        // the arrangements this thread has finished with (Release), kept with their arrays for the next boolean
        [ThreadStatic] private static List<Arrangement> s_free;

        private Arrangement() { }

        /// <summary>An empty arrangement for this box (one this thread finished with, its arrays kept). Release it when its
        /// regions are out.</summary>
        /// <param name="capacity">How many vertices the input has (the tables are made that big at once).</param>
        public static Arrangement Rent(double2 min, double2 max, int capacity = 0)
        {
            var free = s_free ??= new List<Arrangement>();
            Arrangement a;
            if (free.Count > 0) { a = free[free.Count - 1]; free.RemoveAt(free.Count - 1); }
            else a = new Arrangement();
            a.Init(min, max, capacity);
            return a;
        }

        /// <summary>Done with: the regions it gave are the caller's (new arrays), its own arrays go to the next one.</summary>
        public void Release()
        {
            var free = s_free ??= new List<Arrangement>();
            if (free.Count < 16) free.Add(this);
        }

        private void Init(double2 min, double2 max, int capacity)
        {
            capacity = math.max(capacity, 4);
            _pts.Clear();
            _grid.Reset(capacity);
            _nextInCell.Clear();
            _edges.Clear();
            _spare.Clear();
            _built = false;
            _cycleCount = 0;
            _origin = (min + max) * 0.5;
            double extent = math.max(math.cmax(math.abs(min)), math.cmax(math.abs(max)));
            // ~4 float ulps at the input's magnitude, never below 0.1 mm.
            _eps = math.max(1e-4, extent * 2.4e-7);
            _eps2 = _eps * _eps;
            _cell = _eps * 2;
        }

        public static Arrangement For(IEnumerable<IReadOnlyList<float2>> rings)
        {
            double2 mn = new double2(double.MaxValue), mx = new double2(double.MinValue);
            bool any = false;
            int count = 0;
            foreach (var r in rings)
            {
                if (r == null) continue;
                count += r.Count;
                // (by index: a foreach over an array seen as a list made an enumerator each time)
                for (int i = 0; i < r.Count; i++)
                {
                    var p = r[i];
                    if (!Poly.IsFinite(p)) continue;
                    mn = math.min(mn, (double2)p); mx = math.max(mx, (double2)p); any = true;
                }
            }
            if (!any) { mn = double2.zero; mx = double2.zero; }
            return Rent(mn, mx, count + count / 4);
        }

        public double2 ToLocal(float2 p) => (double2)p - _origin;
        public float2 ToWorld(double2 p) => new float2((float)(p.x + _origin.x), (float)(p.y + _origin.y));

        // ───────────────────────────── input ─────────────────────────────

        private static long Key(long ix, long iy) => (ix << 32) ^ (iy & 0xffffffffL);

        public int VertexLocal(double2 p)
        {
            long ix = (long)math.floor(p.x / _cell), iy = (long)math.floor(p.y / _cell);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (!_grid.TryGetValue(Key(ix + dx, iy + dy), out var cell)) continue;
                    for (int v = cell.Head; v >= 0; v = _nextInCell.A[v])
                    {
                        // (math.distancesq(_pts[v], p) written out)
                        double ex = p.x - _pts.A[v].x, ey = p.y - _pts.A[v].y;
                        if (ex * ex + ey * ey <= _eps2) return v;
                    }
                }
            int id = _pts.N;
            _pts.Add(p);
            _nextInCell.Add(-1);
            long k = Key(ix, iy);
            if (_grid.TryGetValue(k, out var c)) { _nextInCell.A[c.Tail] = id; _grid[k] = (c.Head, id); }
            else _grid[k] = (id, id);
            return id;
        }

        public void AddRing(IReadOnlyList<float2> ring, int operand, int weight = 1)
        {
            if (ring == null || ring.Count < 3) return;
            var local = Work.Local;
            local.Clear();
            for (int i = 0; i < ring.Count; i++) if (Poly.IsFinite(ring[i])) local.Add(ToLocal(ring[i]));
            AddRingLocal(local, operand, weight);
        }

        public void AddRingLocal(IReadOnlyList<double2> ring, int operand, int weight = 1)
        {
            if (ring == null || ring.Count < 2) return;
            int n = ring.Count;
            int first = VertexLocal(ring[0]), prev = first;
            for (int i = 1; i <= n; i++)
            {
                int v = i == n ? first : VertexLocal(ring[i]);
                AddEdge(prev, v, operand == 0 ? weight : 0, operand == 1 ? weight : 0, false);
                prev = v;
            }
        }

        public void AddPolyline(IReadOnlyList<float2> line)
        {
            if (line == null || line.Count < 2) return;
            int prev = VertexLocal(ToLocal(line[0]));
            for (int i = 1; i < line.Count; i++)
            {
                if (!Poly.IsFinite(line[i])) continue;
                int v = VertexLocal(ToLocal(line[i]));
                AddEdge(prev, v, 0, 0, true);
                prev = v;
            }
        }

        public void AddSegmentLocal(double2 a, double2 b) => AddEdge(VertexLocal(a), VertexLocal(b), 0, 0, true);

        private void AddEdge(int u, int v, int da, int db, bool cut)
        {
            if (u == v) return;
            _edges.Add(new Edge { U = u, V = v, DA = da, DB = db, Cut = cut });
            _built = false;
        }

        // ───────────────────────────── build ─────────────────────────────

        public void Build()
        {
            if (_built) return;
            MergeDuplicates();
            for (int round = 0; round < 8; round++)
                if (!SplitRound()) break;
            // Edges whose windings cancel (shared boundaries of a union) carry no information.
            {
                int kept = 0;
                for (int k = 0; k < _edges.N; k++) { var e = _edges.A[k]; if (!(e.DA == 0 && e.DB == 0 && !e.Cut)) _edges.A[kept++] = e; }
                _edges.N = kept;
            }
            PruneDangling();
            BuildFaces();
            _built = true;
        }

        private void MergeDuplicates()
        {
            var map = Work.Merge;
            map.Reset(_edges.N);
            var merged = _spare;
            merged.Clear();
            for (int q = 0; q < _edges.N; q++)
            {
                var e = _edges.A[q];
                if (e.U == e.V) continue;
                if (e.U > e.V) { int t = e.U; e.U = e.V; e.V = t; e.DA = -e.DA; e.DB = -e.DB; }
                long k = ((long)e.U << 32) | (uint)e.V;
                if (map.TryGetValue(k, out int idx))
                {
                    var m = merged.A[idx];
                    m.DA += e.DA; m.DB += e.DB; m.Cut |= e.Cut;
                    merged.A[idx] = m;
                }
                else
                {
                    map.Add(k, merged.N);
                    merged.Add(e);
                }
            }
            _spare = _edges;
            _edges = merged;
        }

        /// <summary>One sweep: split every edge at every crossing / vertex lying on it. True if anything changed.</summary>
        private bool SplitRound()
        {
            int n = _edges.N;
            var w = Work;
            var minX = Scratch.Fit(ref w.MinX, n);
            var order = Scratch.Fit(ref w.Order, n);
            for (int i = 0; i < n; i++)
            {
                minX[i] = math.min(_pts.A[_edges.A[i].U].x, _pts.A[_edges.A[i].V].x);
                order[i] = i;
            }
            w.ByMinX.MinX = minX;
            Array.Sort(order, 0, n, w.ByMinX);
            var splits = Scratch.Fit(ref w.Splits, n);
            Array.Clear(splits, 0, n);
            w.SplitUsed = 0;
            // the edges the sweep line crosses, with what the scan asks of each (its far x, its y range) beside it: the same
            // list in the same order as before, read without going through the edge and its two points every time
            var active = Scratch.Fit(ref w.Active, n);
            var activeMaxX = Scratch.Fit(ref w.ActiveMaxX, n);
            var activeMinY = Scratch.Fit(ref w.ActiveMinY, n);
            var activeMaxY = Scratch.Fit(ref w.ActiveMaxY, n);
            int activeCount = 0;
            // the smallest far x among the active edges: while the sweep is left of it, none ends (the scan for ended ones
            // removed nothing and is not made)
            double soonest = double.MaxValue;
            bool any = false;
            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                double x0 = minX[i] - _eps;
                if (soonest < x0)
                {
                    soonest = double.MaxValue;
                    for (int k = activeCount - 1; k >= 0; k--)
                    {
                        if (activeMaxX[k] < x0)
                        {
                            int last = --activeCount;
                            active[k] = active[last];
                            activeMaxX[k] = activeMaxX[last];
                            activeMinY[k] = activeMinY[last];
                            activeMaxY[k] = activeMaxY[last];
                            if (k < activeCount && activeMaxX[k] < soonest) soonest = activeMaxX[k];
                        }
                        else if (activeMaxX[k] < soonest) soonest = activeMaxX[k];
                    }
                }
                var e = _edges.A[i];
                double2 a = _pts.A[e.U], b = _pts.A[e.V];
                double ey0 = math.min(a.y, b.y) - _eps, ey1 = math.max(a.y, b.y) + _eps;
                // an edge whose two ends both lie clearly on one side of this one's line (further than eps, with a margin for
                // rounding) neither touches nor crosses it: Intersect would find nothing (most pairs the boxes let through)
                double abx = b.x - a.x, aby = b.y - a.y;
                double clear2 = _eps2 * (abx * abx + aby * aby) * 1.0201;
                for (int k = 0; k < activeCount; k++)
                {
                    if (activeMaxY[k] < ey0 || activeMinY[k] > ey1) continue;
                    int j = active[k];
                    var f = _edges.A[j];
                    double2 c = _pts.A[f.U], d = _pts.A[f.V];
                    double sc = abx * (c.y - a.y) - aby * (c.x - a.x), sd = abx * (d.y - a.y) - aby * (d.x - a.x);
                    if ((sc > 0) == (sd > 0) && sc * sc > clear2 && sd * sd > clear2) continue;
                    any |= Intersect(i, e, a, b, j, f, c, d, splits);
                }
                active[activeCount] = i;
                activeMaxX[activeCount] = math.max(a.x, b.x);
                if (activeMaxX[activeCount] < soonest) soonest = activeMaxX[activeCount];
                activeMinY[activeCount] = math.min(a.y, b.y);
                activeMaxY[activeCount] = math.max(a.y, b.y);
                activeCount++;
            }
            if (!any) { Array.Clear(splits, 0, n); return false; }

            var result = _spare;
            result.Clear();
            var ts = w.Ts;
            for (int i = 0; i < n; i++)
            {
                var e = _edges.A[i];
                var s = splits[i];
                if (s == null) { result.Add(e); continue; }
                double2 a = _pts.A[e.U], ab = _pts.A[e.V] - a;
                double len2 = math.max(math.lengthsq(ab), 1e-300);
                ts.Clear();
                foreach (int v in s) ts.Add(new KeyValuePair<double, int>(math.dot(_pts.A[v] - a, ab) / len2, v));
                ts.Sort((p, q) => p.Key.CompareTo(q.Key));
                int prev = e.U;
                foreach (var kv in ts)
                {
                    if (kv.Value == prev || kv.Value == e.V) continue;
                    result.Add(new Edge { U = prev, V = kv.Value, DA = e.DA, DB = e.DB, Cut = e.Cut });
                    prev = kv.Value;
                }
                if (prev != e.V) result.Add(new Edge { U = prev, V = e.V, DA = e.DA, DB = e.DB, Cut = e.Cut });
            }
            Array.Clear(splits, 0, n);
            _spare = _edges;
            _edges = result;
            MergeDuplicates();
            return true;
        }

        private bool Intersect(int i, Edge e, double2 a, double2 b, int j, Edge f, double2 c, double2 d, List<int>[] splits)
        {
            bool any = false;
            // Vertices of one edge lying on the other (T-junctions, collinear overlaps).
            if (f.U != e.U && f.U != e.V && NearInterior(c, a, b)) { AddSplit(splits, i, f.U); any = true; }
            if (f.V != e.U && f.V != e.V && NearInterior(d, a, b)) { AddSplit(splits, i, f.V); any = true; }
            if (e.U != f.U && e.U != f.V && NearInterior(a, c, d)) { AddSplit(splits, j, e.U); any = true; }
            if (e.V != f.U && e.V != f.V && NearInterior(b, c, d)) { AddSplit(splits, j, e.V); any = true; }
            if (e.U == f.U || e.U == f.V || e.V == f.U || e.V == f.V) return any;

            // Proper crossing. (The vector maths written out, the same operations in the same order: in the game's runtime each
            // vector operation was a call, and this runs for every pair of edges a boolean meets)
            double rx = b.x - a.x, ry = b.y - a.y, sx = d.x - c.x, sy = d.y - c.y;
            double den = rx * sy - ry * sx;
            if (den == 0) return any;
            double cax = c.x - a.x, cay = c.y - a.y;
            double t = (cax * sy - cay * sx) / den, u = (cax * ry - cay * rx) / den;
            if (t <= 0 || t >= 1 || u <= 0 || u >= 1) return any;
            double px = a.x + rx * t, py = a.y + ry * t;
            // Crossings within eps of an endpoint were handled as incidences above.
            if (DistSq(px, py, a) <= _eps2 || DistSq(px, py, b) <= _eps2 || DistSq(px, py, c) <= _eps2 || DistSq(px, py, d) <= _eps2) return any;
            int x = VertexLocal(new double2(px, py));
            if (x != e.U && x != e.V) { AddSplit(splits, i, x); any = true; }
            if (x != f.U && x != f.V) { AddSplit(splits, j, x); any = true; }
            return any;
        }

        private bool NearInterior(double2 p, double2 a, double2 b)
        {
            double abx = b.x - a.x, aby = b.y - a.y;
            double len2 = abx * abx + aby * aby;
            if (len2 <= 0) return false;
            double t = ((p.x - a.x) * abx + (p.y - a.y) * aby) / len2;
            if (t <= 0 || t >= 1) return false;
            if (DistSq(p.x, p.y, a) <= _eps2 || DistSq(p.x, p.y, b) <= _eps2) return false;
            double qx = a.x + abx * t, qy = a.y + aby * t;
            double dx = qx - p.x, dy = qy - p.y;
            return dx * dx + dy * dy <= _eps2;
        }

        /// <summary>math.distancesq((px, py), q), written out (q minus p, squared, the same operations).</summary>
        private static double DistSq(double px, double py, double2 q)
        {
            double dx = q.x - px, dy = q.y - py;
            return dx * dx + dy * dy;
        }

        private static void AddSplit(List<int>[] splits, int edge, int v)
        {
            var l = splits[edge] ?? (splits[edge] = Work.SplitList());
            if (!l.Contains(v)) l.Add(v);
        }

        private void PruneDangling()
        {
            int nv = _pts.N, ne = _edges.N;
            var w = Work;
            var deg = Scratch.Fit(ref w.Deg, nv);
            Array.Clear(deg, 0, nv);
            for (int q = 0; q < ne; q++) { deg[_edges.A[q].U]++; deg[_edges.A[q].V]++; }
            var alive = Scratch.Fit(ref w.Alive, ne);
            // each vertex's edges in the order they come (one flat table, not a list per vertex)
            var incStart = Scratch.Fit(ref w.IncStart, nv + 1);
            var incFlat = Scratch.Fit(ref w.IncFlat, 2 * ne);
            var fill = Scratch.Fit(ref w.Fill, nv);
            incStart[0] = 0;
            for (int v = 0; v < nv; v++) { incStart[v + 1] = incStart[v] + deg[v]; fill[v] = 0; }
            for (int k = 0; k < ne; k++)
            {
                alive[k] = true;
                int u = _edges.A[k].U, x = _edges.A[k].V;
                incFlat[incStart[u] + fill[u]++] = k;
                incFlat[incStart[x] + fill[x]++] = k;
            }
            var stack = w.Stack;
            stack.Clear();
            for (int v = 0; v < nv; v++) if (deg[v] == 1) stack.Push(v);
            bool removed = false;
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                if (deg[v] != 1) continue;
                for (int q = incStart[v]; q < incStart[v + 1]; q++)
                {
                    int k = incFlat[q];
                    if (!alive[k]) continue;
                    alive[k] = false; removed = true;
                    int o = _edges.A[k].U == v ? _edges.A[k].V : _edges.A[k].U;
                    deg[v]--; deg[o]--;
                    if (deg[o] == 1) stack.Push(o);
                    break;
                }
            }
            if (!removed) return;
            var kept = _spare;
            kept.Clear();
            for (int k = 0; k < ne; k++) if (alive[k]) kept.Add(_edges.A[k]);
            _spare = _edges;
            _edges = kept;
        }

        private int Org(int h) => (h & 1) == 0 ? _edges.A[h >> 1].U : _edges.A[h >> 1].V;
        private int Dst(int h) => (h & 1) == 0 ? _edges.A[h >> 1].V : _edges.A[h >> 1].U;

        /// <summary>Half-edges by angle, ties by index: a total order, so every sort gives the one same result.</summary>
        private sealed class ByAngle : IComparer<int>
        {
            public double[] Angle;
            public int Compare(int p, int q) => Angle[p] != Angle[q] ? Angle[p].CompareTo(Angle[q]) : p.CompareTo(q);
        }

        private void BuildFaces()
        {
            int nh = _edges.N * 2, nv = _pts.N;
            var w = Work;
            var angle = Scratch.Fit(ref w.Angle, nh);
            Scratch.Fit(ref _outStart, nv + 1);
            Array.Clear(_outStart, 0, nv + 1);
            for (int h = 0; h < nh; h++)
            {
                double2 dvec = _pts.A[Dst(h)] - _pts.A[Org(h)];
                angle[h] = math.atan2(dvec.y, dvec.x);
                _outStart[Org(h) + 1]++;
            }
            for (int v = 0; v < nv; v++) _outStart[v + 1] += _outStart[v];
            Scratch.Fit(ref _outFlat, nh);
            var fill = Scratch.Fit(ref w.Fill, nv);
            Array.Clear(fill, 0, nv);
            for (int h = 0; h < nh; h++) { int o = Org(h); _outFlat[_outStart[o] + fill[o]++] = h; }
            Scratch.Fit(ref _posInOut, nh);
            var byAngle = w.ByAngle;
            byAngle.Angle = angle;
            for (int v = 0; v < nv; v++)
            {
                int s = _outStart[v], count = _outStart[v + 1] - s;
                // (by angle, ties by index: a total order, so any sort gives the one result. A vertex has a few edges: sorted in
                // place; Array.Sort with a comparer made a delegate each time, a vertex at a time)
                if (count > 16) Array.Sort(_outFlat, s, count, byAngle);
                else
                    for (int i = s + 1; i < s + count; i++)
                    {
                        int x = _outFlat[i], j = i - 1;
                        while (j >= s && byAngle.Compare(_outFlat[j], x) > 0) { _outFlat[j + 1] = _outFlat[j]; j--; }
                        _outFlat[j + 1] = x;
                    }
                for (int k = 0; k < count; k++) _posInOut[_outFlat[s + k]] = k;
            }

            Scratch.Fit(ref _cycleOf, nh);
            for (int h = 0; h < nh; h++) _cycleOf[h] = -1;
            Scratch.Fit(ref _cycleFlat, nh);
            var starts = _starts;
            starts.Clear();
            int at = 0;
            _cycleCount = 0;
            for (int h0 = 0; h0 < nh; h0++)
            {
                if (_cycleOf[h0] >= 0) continue;
                starts.Add(at);
                int h = h0, guard = nh + 1;
                while (_cycleOf[h] < 0 && guard-- > 0)
                {
                    _cycleOf[h] = _cycleCount;
                    _cycleFlat[at++] = h;
                    h = NextInFace(h);
                }
                _cycleCount++;
            }
            starts.Add(at);
            Scratch.Fit(ref _cycleStart, starts.Count);
            starts.CopyTo(_cycleStart);

            // Windings: measured once per connected component (at its outer boundary, whose left side
            // is the face containing the component), then propagated across edges:
            // winding(right of h) = winding(left of h) − delta(h). O(E) instead of O(F·E).
            int nc = _cycleCount;
            Scratch.Fit(ref _windA, nc);
            Scratch.Fit(ref _windB, nc);
            Array.Clear(_windA, 0, nc);
            Array.Clear(_windB, 0, nc);
            var parent = Scratch.Fit(ref w.Parent, nv);
            for (int i = 0; i < nv; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int q = 0; q < _edges.N; q++) { int a = Find(_edges.A[q].U), b = Find(_edges.A[q].V); if (a != b) parent[a] = b; }
            var area = Scratch.Fit(ref w.Area, nc);
            var outerOf = w.OuterOf;
            outerOf.Clear();
            for (int c = 0; c < nc; c++)
            {
                double a = 0;
                for (int k = _cycleStart[c]; k < _cycleStart[c + 1]; k++) { int h = _cycleFlat[k]; a += Cross(_pts.A[Org(h)], _pts.A[Dst(h)]); }
                area[c] = a;
                int comp = Find(Org(_cycleFlat[_cycleStart[c]]));
                if (!outerOf.TryGetValue(comp, out int cur0) || a < area[cur0]) outerOf[comp] = c;
            }
            var done = Scratch.Fit(ref w.Done, nc);
            Array.Clear(done, 0, nc);
            var queue = w.Queue;
            queue.Clear();
            foreach (var kv in outerOf)
            {
                int c0 = kv.Value;
                Winding(SampleLeft(c0), out _windA[c0], out _windB[c0]);
                done[c0] = true;
                queue.Enqueue(c0);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    for (int kh = _cycleStart[c]; kh < _cycleStart[c + 1]; kh++)
                    {
                        int h = _cycleFlat[kh];
                        int c2 = _cycleOf[h ^ 1];
                        if (done[c2]) continue;
                        var e = _edges.A[h >> 1];
                        int sgn = (h & 1) == 0 ? 1 : -1;
                        _windA[c2] = _windA[c] - sgn * e.DA;
                        _windB[c2] = _windB[c] - sgn * e.DB;
                        done[c2] = true;
                        queue.Enqueue(c2);
                    }
                }
            }
        }

        /// <summary>Next half-edge around the face on the left: the outgoing edge clockwise-adjacent to the twin.</summary>
        private int NextInFace(int h)
        {
            int twin = h ^ 1;
            int v = Dst(h), s = _outStart[v], count = _outStart[v + 1] - s;
            int k = _posInOut[twin];
            return _outFlat[s + (k - 1 + count) % count];
        }

        /// <summary>A point strictly inside the region on the left of the cycle (between its longest edge and the next edge hit).</summary>
        private double2 SampleLeft(int cycle)
        {
            int best = _cycleFlat[_cycleStart[cycle]];
            double bestLen = -1;
            for (int k0 = _cycleStart[cycle]; k0 < _cycleStart[cycle + 1]; k0++)
            {
                int h = _cycleFlat[k0];
                double l = math.distancesq(_pts.A[Org(h)], _pts.A[Dst(h)]);
                if (l > bestLen) { bestLen = l; best = h; }
            }
            double2 a = _pts.A[Org(best)], b = _pts.A[Dst(best)];
            double len = math.sqrt(bestLen);
            // (the vector maths written out, the same operations in the same order)
            double mx = (a.x + b.x) * 0.5, my = (a.y + b.y) * 0.5;
            double nx = -(b.y - a.y) / len, ny = (b.x - a.x) / len;
            double tMin = len;
            int own = best >> 1;
            for (int k = 0; k < _edges.N; k++)
            {
                if (k == own) continue;
                double2 p = _pts.A[_edges.A[k].U], q = _pts.A[_edges.A[k].V];
                double sx = q.x - p.x, sy = q.y - p.y;
                double den = nx * sy - ny * sx;
                if (den == 0) continue;
                double pmx = p.x - mx, pmy = p.y - my;
                double t = (pmx * sy - pmy * sx) / den, u = (pmx * ny - pmy * nx) / den;
                if (u < 0 || u > 1 || t <= 0) continue;
                if (t < tMin) tMin = t;
            }
            return new double2(mx + nx * (tMin * 0.5), my + ny * (tMin * 0.5));
        }

        private void Winding(double2 p, out int wa, out int wb)
        {
            wa = 0; wb = 0;
            for (int q = 0; q < _edges.N; q++)
            {
                var e = _edges.A[q];
                if (e.DA == 0 && e.DB == 0) continue;
                double2 a = _pts.A[e.U], b = _pts.A[e.V];
                if ((a.y > p.y) == (b.y > p.y)) continue;
                double x = a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y);
                if (x <= p.x) continue;
                int sgn = b.y > a.y ? 1 : -1;
                wa += sgn * e.DA; wb += sgn * e.DB;
            }
        }

        private static double Cross(double2 a, double2 b) => a.x * b.y - a.y * b.x;

        // ───────────────────────────── output ─────────────────────────────

        /// <summary>
        /// Regions where inside(windingA, windingB) holds. With splitByCuts, polyline edges separate
        /// pieces (each face becomes its own region); otherwise adjacent inside faces merge.
        /// </summary>
        public List<Region> Extract(Func<int, int, bool> inside, bool splitByCuts = false)
        {
            Build();
            int nh = _edges.N * 2;
            var regions = new List<Region>();
            if (nh == 0) return regions;
            var w = Work;
            var sel = Scratch.Fit(ref w.Sel, _cycleCount);
            for (int c = 0; c < _cycleCount; c++) sel[c] = inside(_windA[c], _windB[c]);

            var boundary = Scratch.Fit(ref w.Boundary, nh);
            Array.Clear(boundary, 0, nh);
            for (int h = 0; h < nh; h++)
            {
                int c = _cycleOf[h], ct = _cycleOf[h ^ 1];
                if (!sel[c]) continue;
                if (!sel[ct]) boundary[h] = true;
                else if (splitByCuts && _edges.A[h >> 1].Cut && c != ct) boundary[h] = true;
            }

            var used = Scratch.Fit(ref w.Used, nh);
            Array.Clear(used, 0, nh);
            var loops = w.Loops;
            loops.Clear();
            w.LoopUsed = 0;
            var verts = w.Verts;
            for (int h0 = 0; h0 < nh; h0++)
            {
                if (!boundary[h0] || used[h0]) continue;
                verts.Clear();
                int h = h0, guard = nh + 1;
                while (!used[h] && guard-- > 0)
                {
                    used[h] = true;
                    verts.Add(Org(h));
                    h = NextBoundary(h, boundary);
                    if (h < 0) break;
                }
                SplitAtRepeats(verts, loops);
            }

            var outers = w.Outers;
            var holes = w.Holes;
            outers.Clear();
            holes.Clear();
            foreach (var loop in loops)
            {
                var ring = ToWorldRing(loop, out double area);
                if (ring == null) continue;
                if (area > 0) outers.Add(new KeyValuePair<double, float2[]>(area, ring));
                else holes.Add(ring);
            }
            outers.Sort((p, q) => q.Key.CompareTo(p.Key));
            foreach (var o in outers) regions.Add(new Region(o.Value));
            if (holes.Count > 0) AssignHoles(regions, outers, holes);
            outers.Clear();
            holes.Clear();
            loops.Clear();
            return regions;
        }

        private int NextBoundary(int h, bool[] boundary)
        {
            int v = Dst(h), s = _outStart[v], count = _outStart[v + 1] - s;
            int k = _posInOut[h ^ 1];
            for (int step = 1; step <= count; step++)
            {
                int g = _outFlat[s + ((k - step) % count + count) % count];
                if (boundary[g]) return g;
            }
            return -1;
        }

        private static void SplitAtRepeats(List<int> verts, List<List<int>> loops)
        {
            var w = Work;
            var stack = w.LoopList();
            var pos = w.RepeatAt;
            pos.Clear();
            foreach (int v in verts)
            {
                if (pos.TryGetValue(v, out int p))
                {
                    var sub = w.LoopList();
                    for (int k = p; k < stack.Count; k++) sub.Add(stack[k]);
                    for (int k = p + 1; k < stack.Count; k++) pos.Remove(stack[k]);
                    stack.RemoveRange(p + 1, stack.Count - p - 1);
                    if (sub.Count >= 3) loops.Add(sub);
                }
                else
                {
                    pos[v] = stack.Count;
                    stack.Add(v);
                }
            }
            if (stack.Count >= 3) loops.Add(stack);
        }

        private float2[] ToWorldRing(List<int> loop, out double area)
        {
            area = 0;
            var work = Work;
            var pts = work.RingPts;
            pts.Clear();
            foreach (int v in loop) pts.Add(_pts.A[v]);
            // Drop straight-through vertices introduced by splitting (keeps rings minimal). Linked list
            // with a work stack: O(n) instead of repeated passes.
            int n = pts.Count;
            var prev = Scratch.Fit(ref work.RingPrev, n);
            var next = Scratch.Fit(ref work.RingNext, n);
            var alive = Scratch.Fit(ref work.RingAlive, n);
            var stack = work.Stack;
            stack.Clear();
            for (int i = 0; i < n; i++) { prev[i] = (i - 1 + n) % n; next[i] = (i + 1) % n; alive[i] = true; stack.Push(i); }
            int count = n;
            while (stack.Count > 0 && count > 3)
            {
                int i = stack.Pop();
                if (!alive[i]) continue;
                double2 a = pts[prev[i]], b = pts[i], c = pts[next[i]];
                double2 ac = c - a;
                double lac = math.length(ac);
                if (lac <= 0) continue;
                if (math.abs(Cross(b - a, ac)) / lac > _eps * 0.25 || math.dot(b - a, c - b) <= 0) continue;
                alive[i] = false; count--;
                next[prev[i]] = next[i]; prev[next[i]] = prev[i];
                stack.Push(prev[i]); stack.Push(next[i]);
            }
            if (count < n)
            {
                var kept = work.RingKept;
                kept.Clear();
                int s0 = 0;
                while (!alive[s0]) s0++;
                int k = s0;
                do { kept.Add(pts[k]); k = next[k]; } while (k != s0);
                pts = kept;
            }
            var ring = work.RingOut;
            ring.Clear();
            foreach (var p in pts)
            {
                var w = ToWorld(p);
                if (ring.Count > 0 && math.all(ring[ring.Count - 1] == w)) continue;
                ring.Add(w);
            }
            while (ring.Count > 1 && math.all(ring[0] == ring[ring.Count - 1])) ring.RemoveAt(ring.Count - 1);
            if (ring.Count < 3) return null;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++) area += Cross(pts[j], pts[i]);
            area *= 0.5;
            if (math.abs(area) < 1e-7) return null;
            return ring.ToArray();
        }

        private static void AssignHoles(List<Region> regions, List<KeyValuePair<double, float2[]>> outers, List<float2[]> holes)
        {
            foreach (var h in holes)
            {
                // Probe just left of the hole's longest edge: that side is material, inside the owning outer.
                int best = 0; float bl = -1;
                for (int i = 0; i < h.Length; i++)
                {
                    float l = math.distancesq(h[i], h[(i + 1) % h.Length]);
                    if (l > bl) { bl = l; best = i; }
                }
                double2 a = h[best], b = h[(best + 1) % h.Length];
                double len = math.sqrt(bl);
                double2 probe = (a + b) * 0.5 + new double2(-(b.y - a.y), b.x - a.x) / len * math.min(1e-3, len * 0.01);
                int owner = -1;
                double ownerArea = double.MaxValue;
                for (int k = 0; k < outers.Count; k++)
                {
                    if (outers[k].Key >= ownerArea) continue;
                    if (Poly.ContainsD(outers[k].Value, probe)) { owner = k; ownerArea = outers[k].Key; }
                }
                if (owner >= 0) regions[owner].Holes.Add(h);
            }
        }
    }
}
