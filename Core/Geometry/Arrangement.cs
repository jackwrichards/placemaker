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

        private readonly double2 _origin;
        private readonly double _eps, _eps2, _cell;
        private readonly List<double2> _pts;
        // vertices by snap cell: the cell's first and last vertex, each vertex's next in its cell (-1: the last), in the order
        // they were made (one table and one list, not a list per cell: the allocations were a big part of every boolean)
        private readonly Dictionary<long, (int Head, int Tail)> _grid;
        private readonly List<int> _nextInCell;
        private List<Edge> _edges;

        // Built state
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
            public readonly Dictionary<long, int> Merge = new Dictionary<long, int>();
            public int[] Deg = new int[64], IncStart = new int[65], IncFlat = new int[128], Fill = new int[64], Parent = new int[64];
            public bool[] Alive = new bool[64], Done = new bool[64];
            public double[] Angle = new double[128], Area = new double[64];
            public bool[] Sel = new bool[64], Boundary = new bool[128], Used = new bool[128], RingAlive = new bool[64];
            public int[] RingPrev = new int[64], RingNext = new int[64];
            public readonly List<int> Verts = new List<int>();
            public readonly Dictionary<int, int> RepeatAt = new Dictionary<int, int>();
            public readonly MinXOrder ByMinX = new MinXOrder();
            public readonly ByAngle ByAngle = new ByAngle();

            public static T[] Fit<T>(ref T[] a, int n)
            {
                if (a.Length < n) a = new T[math.max(n, a.Length * 2)];
                return a;
            }
        }

        [ThreadStatic] private static Scratch s_scratch;
        private static Scratch Work => s_scratch ?? (s_scratch = new Scratch());

        /// <summary>Edge indices by their smallest x (the sweep's order).</summary>
        private sealed class MinXOrder : IComparer<int>
        {
            public double[] MinX;
            public int Compare(int a, int b) => MinX[a].CompareTo(MinX[b]);
        }

        /// <param name="capacity">How many vertices the input has (the tables are made that big at once).</param>
        public Arrangement(double2 min, double2 max, int capacity = 0)
        {
            capacity = math.max(capacity, 4);
            _pts = new List<double2>(capacity);
            _grid = new Dictionary<long, (int, int)>(capacity);
            _nextInCell = new List<int>(capacity);
            _edges = new List<Edge>(capacity);
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
                foreach (var p in r)
                {
                    if (!Poly.IsFinite(p)) continue;
                    mn = math.min(mn, (double2)p); mx = math.max(mx, (double2)p); any = true;
                }
            }
            if (!any) { mn = double2.zero; mx = double2.zero; }
            return new Arrangement(mn, mx, count + count / 4);
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
                    for (int v = cell.Head; v >= 0; v = _nextInCell[v])
                        if (math.distancesq(_pts[v], p) <= _eps2) return v;
                }
            int id = _pts.Count;
            _pts.Add(p);
            _nextInCell.Add(-1);
            long k = Key(ix, iy);
            if (_grid.TryGetValue(k, out var c)) { _nextInCell[c.Tail] = id; _grid[k] = (c.Head, id); }
            else _grid[k] = (id, id);
            return id;
        }

        public void AddRing(IReadOnlyList<float2> ring, int operand, int weight = 1)
        {
            if (ring == null || ring.Count < 3) return;
            var local = new List<double2>(ring.Count);
            foreach (var p in ring) if (Poly.IsFinite(p)) local.Add(ToLocal(p));
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
            _edges.RemoveAll(e => e.DA == 0 && e.DB == 0 && !e.Cut);
            PruneDangling();
            BuildFaces();
            _built = true;
        }

        private void MergeDuplicates()
        {
            var map = Work.Merge;
            map.Clear();
            var merged = new List<Edge>(_edges.Count);
            foreach (var e0 in _edges)
            {
                var e = e0;
                if (e.U == e.V) continue;
                if (e.U > e.V) { int t = e.U; e.U = e.V; e.V = t; e.DA = -e.DA; e.DB = -e.DB; }
                long k = ((long)e.U << 32) | (uint)e.V;
                if (map.TryGetValue(k, out int idx))
                {
                    var m = merged[idx];
                    m.DA += e.DA; m.DB += e.DB; m.Cut |= e.Cut;
                    merged[idx] = m;
                }
                else
                {
                    map[k] = merged.Count;
                    merged.Add(e);
                }
            }
            _edges = merged;
        }

        /// <summary>One sweep: split every edge at every crossing / vertex lying on it. True if anything changed.</summary>
        private bool SplitRound()
        {
            int n = _edges.Count;
            var w = Work;
            var minX = Scratch.Fit(ref w.MinX, n);
            var order = Scratch.Fit(ref w.Order, n);
            for (int i = 0; i < n; i++)
            {
                minX[i] = math.min(_pts[_edges[i].U].x, _pts[_edges[i].V].x);
                order[i] = i;
            }
            w.ByMinX.MinX = minX;
            Array.Sort(order, 0, n, w.ByMinX);
            var splits = Scratch.Fit(ref w.Splits, n);
            Array.Clear(splits, 0, n);
            // the edges the sweep line crosses, with what the scan asks of each (its far x, its y range) beside it: the same
            // list in the same order as before, read without going through the edge and its two points every time
            var active = Scratch.Fit(ref w.Active, n);
            var activeMaxX = Scratch.Fit(ref w.ActiveMaxX, n);
            var activeMinY = Scratch.Fit(ref w.ActiveMinY, n);
            var activeMaxY = Scratch.Fit(ref w.ActiveMaxY, n);
            int activeCount = 0;
            bool any = false;
            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                double x0 = minX[i] - _eps;
                for (int k = activeCount - 1; k >= 0; k--)
                {
                    if (activeMaxX[k] < x0)
                    {
                        int last = --activeCount;
                        active[k] = active[last];
                        activeMaxX[k] = activeMaxX[last];
                        activeMinY[k] = activeMinY[last];
                        activeMaxY[k] = activeMaxY[last];
                    }
                }
                var e = _edges[i];
                double2 a = _pts[e.U], b = _pts[e.V];
                double ey0 = math.min(a.y, b.y) - _eps, ey1 = math.max(a.y, b.y) + _eps;
                for (int k = 0; k < activeCount; k++)
                {
                    if (activeMaxY[k] < ey0 || activeMinY[k] > ey1) continue;
                    int j = active[k];
                    var f = _edges[j];
                    double2 c = _pts[f.U], d = _pts[f.V];
                    any |= Intersect(i, e, a, b, j, f, c, d, splits);
                }
                active[activeCount] = i;
                activeMaxX[activeCount] = math.max(a.x, b.x);
                activeMinY[activeCount] = math.min(a.y, b.y);
                activeMaxY[activeCount] = math.max(a.y, b.y);
                activeCount++;
            }
            if (!any) { Array.Clear(splits, 0, n); return false; }

            var result = new List<Edge>(n + 16);
            for (int i = 0; i < n; i++)
            {
                var e = _edges[i];
                var s = splits[i];
                if (s == null) { result.Add(e); continue; }
                double2 a = _pts[e.U], ab = _pts[e.V] - a;
                double len2 = math.max(math.lengthsq(ab), 1e-300);
                var ts = new List<KeyValuePair<double, int>>(s.Count);
                foreach (int v in s) ts.Add(new KeyValuePair<double, int>(math.dot(_pts[v] - a, ab) / len2, v));
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

            // Proper crossing.
            double2 r = b - a, s = d - c;
            double den = Cross(r, s);
            if (den == 0) return any;
            double t = Cross(c - a, s) / den, u = Cross(c - a, r) / den;
            if (t <= 0 || t >= 1 || u <= 0 || u >= 1) return any;
            double2 p = a + r * t;
            // Crossings within eps of an endpoint were handled as incidences above.
            if (math.distancesq(p, a) <= _eps2 || math.distancesq(p, b) <= _eps2 ||
                math.distancesq(p, c) <= _eps2 || math.distancesq(p, d) <= _eps2) return any;
            int x = VertexLocal(p);
            if (x != e.U && x != e.V) { AddSplit(splits, i, x); any = true; }
            if (x != f.U && x != f.V) { AddSplit(splits, j, x); any = true; }
            return any;
        }

        private bool NearInterior(double2 p, double2 a, double2 b)
        {
            double2 ab = b - a;
            double len2 = math.lengthsq(ab);
            if (len2 <= 0) return false;
            double t = math.dot(p - a, ab) / len2;
            if (t <= 0 || t >= 1) return false;
            if (math.distancesq(p, a) <= _eps2 || math.distancesq(p, b) <= _eps2) return false;
            return math.distancesq(p, a + ab * t) <= _eps2;
        }

        private static void AddSplit(List<int>[] splits, int edge, int v)
        {
            var l = splits[edge] ?? (splits[edge] = new List<int>(2));
            if (!l.Contains(v)) l.Add(v);
        }

        private void PruneDangling()
        {
            int nv = _pts.Count, ne = _edges.Count;
            var w = Work;
            var deg = Scratch.Fit(ref w.Deg, nv);
            Array.Clear(deg, 0, nv);
            foreach (var e in _edges) { deg[e.U]++; deg[e.V]++; }
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
                int u = _edges[k].U, x = _edges[k].V;
                incFlat[incStart[u] + fill[u]++] = k;
                incFlat[incStart[x] + fill[x]++] = k;
            }
            var stack = new ListStack<int>();
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
                    int o = _edges[k].U == v ? _edges[k].V : _edges[k].U;
                    deg[v]--; deg[o]--;
                    if (deg[o] == 1) stack.Push(o);
                    break;
                }
            }
            if (!removed) return;
            var kept = new List<Edge>(ne);
            for (int k = 0; k < ne; k++) if (alive[k]) kept.Add(_edges[k]);
            _edges = kept;
        }

        private int Org(int h) => (h & 1) == 0 ? _edges[h >> 1].U : _edges[h >> 1].V;
        private int Dst(int h) => (h & 1) == 0 ? _edges[h >> 1].V : _edges[h >> 1].U;

        /// <summary>Half-edges by angle, ties by index: a total order, so every sort gives the one same result.</summary>
        private sealed class ByAngle : IComparer<int>
        {
            public double[] Angle;
            public int Compare(int p, int q) => Angle[p] != Angle[q] ? Angle[p].CompareTo(Angle[q]) : p.CompareTo(q);
        }

        private void BuildFaces()
        {
            int nh = _edges.Count * 2, nv = _pts.Count;
            var w = Work;
            var angle = Scratch.Fit(ref w.Angle, nh);
            _outStart = new int[nv + 1];
            for (int h = 0; h < nh; h++)
            {
                double2 dvec = _pts[Dst(h)] - _pts[Org(h)];
                angle[h] = math.atan2(dvec.y, dvec.x);
                _outStart[Org(h) + 1]++;
            }
            for (int v = 0; v < nv; v++) _outStart[v + 1] += _outStart[v];
            _outFlat = new int[nh];
            var fill = Scratch.Fit(ref w.Fill, nv);
            Array.Clear(fill, 0, nv);
            for (int h = 0; h < nh; h++) { int o = Org(h); _outFlat[_outStart[o] + fill[o]++] = h; }
            _posInOut = new int[nh];
            var byAngle = w.ByAngle;
            byAngle.Angle = angle;
            for (int v = 0; v < nv; v++)
            {
                int s = _outStart[v], count = _outStart[v + 1] - s;
                if (count > 1) Array.Sort(_outFlat, s, count, byAngle);
                for (int k = 0; k < count; k++) _posInOut[_outFlat[s + k]] = k;
            }

            _cycleOf = new int[nh];
            for (int h = 0; h < nh; h++) _cycleOf[h] = -1;
            _cycleFlat = new int[nh];
            var starts = new List<int>(nh / 2 + 2);
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
            _cycleStart = starts.ToArray();

            // Windings: measured once per connected component (at its outer boundary, whose left side
            // is the face containing the component), then propagated across edges:
            // winding(right of h) = winding(left of h) − delta(h). O(E) instead of O(F·E).
            int nc = _cycleCount;
            _windA = new int[nc];
            _windB = new int[nc];
            var parent = Scratch.Fit(ref w.Parent, nv);
            for (int i = 0; i < nv; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var e in _edges) { int a = Find(e.U), b = Find(e.V); if (a != b) parent[a] = b; }
            var area = Scratch.Fit(ref w.Area, nc);
            var outerOf = new Dictionary<int, int>();
            for (int c = 0; c < nc; c++)
            {
                double a = 0;
                for (int k = _cycleStart[c]; k < _cycleStart[c + 1]; k++) { int h = _cycleFlat[k]; a += Cross(_pts[Org(h)], _pts[Dst(h)]); }
                area[c] = a;
                int comp = Find(Org(_cycleFlat[_cycleStart[c]]));
                if (!outerOf.TryGetValue(comp, out int cur0) || a < area[cur0]) outerOf[comp] = c;
            }
            var done = Scratch.Fit(ref w.Done, nc);
            Array.Clear(done, 0, nc);
            var queue = new ListQueue<int>();
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
                        var e = _edges[h >> 1];
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
                double l = math.distancesq(_pts[Org(h)], _pts[Dst(h)]);
                if (l > bestLen) { bestLen = l; best = h; }
            }
            double2 a = _pts[Org(best)], b = _pts[Dst(best)];
            double len = math.sqrt(bestLen);
            double2 m = (a + b) * 0.5, n = new double2(-(b.y - a.y), b.x - a.x) / len;
            double tMin = len;
            int own = best >> 1;
            for (int k = 0; k < _edges.Count; k++)
            {
                if (k == own) continue;
                double2 p = _pts[_edges[k].U], q = _pts[_edges[k].V], s = q - p;
                double den = Cross(n, s);
                if (den == 0) continue;
                double t = Cross(p - m, s) / den, u = Cross(p - m, n) / den;
                if (u < 0 || u > 1 || t <= 0) continue;
                if (t < tMin) tMin = t;
            }
            return m + n * (tMin * 0.5);
        }

        private void Winding(double2 p, out int wa, out int wb)
        {
            wa = 0; wb = 0;
            foreach (var e in _edges)
            {
                if (e.DA == 0 && e.DB == 0) continue;
                double2 a = _pts[e.U], b = _pts[e.V];
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
            int nh = _edges.Count * 2;
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
                else if (splitByCuts && _edges[h >> 1].Cut && c != ct) boundary[h] = true;
            }

            var used = Scratch.Fit(ref w.Used, nh);
            Array.Clear(used, 0, nh);
            var loops = new List<List<int>>();
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

            var outers = new List<KeyValuePair<double, float2[]>>();
            var holes = new List<float2[]>();
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
            var stack = new List<int>(verts.Count);
            var pos = Work.RepeatAt;
            pos.Clear();
            foreach (int v in verts)
            {
                if (pos.TryGetValue(v, out int p))
                {
                    var sub = stack.GetRange(p, stack.Count - p);
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
            var pts = new List<double2>(loop.Count);
            foreach (int v in loop) pts.Add(_pts[v]);
            // Drop straight-through vertices introduced by splitting (keeps rings minimal). Linked list
            // with a work stack: O(n) instead of repeated passes.
            int n = pts.Count;
            var work = Work;
            var prev = Scratch.Fit(ref work.RingPrev, n);
            var next = Scratch.Fit(ref work.RingNext, n);
            var alive = Scratch.Fit(ref work.RingAlive, n);
            var stack = new ListStack<int>(n);
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
                var kept = new List<double2>(count);
                int s0 = 0;
                while (!alive[s0]) s0++;
                int k = s0;
                do { kept.Add(pts[k]); k = next[k]; } while (k != s0);
                pts = kept;
            }
            var ring = new List<float2>(pts.Count);
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
