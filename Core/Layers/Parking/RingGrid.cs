// (perf) A ring's edges by grid cell, so the point-in-polygon and distance-to-outline questions the parking planner
// asks thousands of times cost a few edges instead of every corner of a painted outline (150-450 corners). Every
// answer is the same as PGeo's whole-ring version: the grid only skips edges that cannot change it.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    /// <summary>(perf) Segments by grid cell, for "any segment closer than r" (same answer as asking every segment).</summary>
    internal sealed class SegmentGrid
    {
        private readonly List<(double2 A, double2 B)> _segs;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly double _size;

        public SegmentGrid(List<(double2 A, double2 B)> segs, double size)
        {
            _segs = segs; _size = size;
            for (int i = 0; i < segs.Count; i++)
            {
                var mn = math.min(segs[i].A, segs[i].B); var mx = math.max(segs[i].A, segs[i].B);
                for (long x = C(mn.x); x <= C(mx.x); x++)
                    for (long y = C(mn.y); y <= C(mx.y); y++)
                    {
                        long k = (x << 32) ^ (y & 0xffffffffL);
                        if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
        }

        private long C(double v) => (long)Math.Floor(v / _size);

        public bool AnyWithin(double2 p, double r)
        {
            for (long x = C(p.x - r); x <= C(p.x + r); x++)
                for (long y = C(p.y - r); y <= C(p.y + r); y++)
                    if (_cells.TryGetValue((x << 32) ^ (y & 0xffffffffL), out var l))
                        foreach (int i in l)
                            if (PGeo.DistPointSeg(p, _segs[i].A, _segs[i].B) < r) return true;
            return false;
        }
    }

    internal sealed class RingGrid
    {
        private readonly double2[] _p;
        private readonly int _n;
        private readonly double2 _min;
        private readonly double _cell, _inv;
        private readonly int _nx, _ny;
        // CSR tables: cell (ix + iy * nx) → edges whose bounding box touches it; row iy → edges whose y range touches it
        private readonly int[] _cellStart, _cellItems, _rowStart, _rowItems;

        public IReadOnlyList<double2> Ring => _p;
        public int Count => _n;

        public RingGrid(IReadOnlyList<double2> ring, double cell = 0)
        {
            _n = ring?.Count ?? 0;
            _p = new double2[_n];
            for (int i = 0; i < _n; i++) _p[i] = ring[i];
            if (_n == 0) { _cellStart = _rowStart = new int[2]; _cellItems = _rowItems = Array.Empty<int>(); _nx = _ny = 1; _cell = _inv = 1; return; }
            PGeo.Bounds(_p, out var mn, out var mx);
            double w = Math.Max(1e-6, mx.x - mn.x), h = Math.Max(1e-6, mx.y - mn.y);
            if (cell <= 0) cell = Math.Sqrt(w * h / Math.Max(4, _n));
            cell = Math.Max(cell, Math.Max(w, h) / 160);
            cell = Math.Max(cell, 0.25);
            _cell = cell; _inv = 1 / cell;
            _min = mn;
            _nx = Math.Max(1, (int)Math.Floor(w * _inv) + 1);
            _ny = Math.Max(1, (int)Math.Floor(h * _inv) + 1);

            var cellCount = new int[_nx * _ny + 1];
            var rowCount = new int[_ny + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    for (int i = 1; i < cellCount.Length; i++) cellCount[i] += cellCount[i - 1];
                    for (int i = 1; i < rowCount.Length; i++) rowCount[i] += rowCount[i - 1];
                    _cellItems = new int[cellCount[cellCount.Length - 1]];
                    _rowItems = new int[rowCount[rowCount.Length - 1]];
                    // counts were kept one slot up: after the running sum, slot c is where cell c starts
                    _cellStart = (int[])cellCount.Clone();
                    _rowStart = (int[])rowCount.Clone();
                }
                for (int e = 0; e < _n; e++)
                {
                    var a = _p[e]; var b = _p[(e + 1) % _n];
                    int x0 = Cx(Math.Min(a.x, b.x)), x1 = Cx(Math.Max(a.x, b.x));
                    int y0 = Cy(Math.Min(a.y, b.y)), y1 = Cy(Math.Max(a.y, b.y));
                    for (int y = y0; y <= y1; y++)
                    {
                        if (pass == 0) rowCount[y + 1]++; else _rowItems[rowCount[y]++] = e;
                        for (int x = x0; x <= x1; x++)
                        {
                            int c = x + y * _nx;
                            if (pass == 0) cellCount[c + 1]++; else _cellItems[cellCount[c]++] = e;
                        }
                    }
                }
            }
        }

        private int Cx(double x) { int i = (int)Math.Floor((x - _min.x) * _inv); return i < 0 ? 0 : i >= _nx ? _nx - 1 : i; }
        private int Cy(double y) { int i = (int)Math.Floor((y - _min.y) * _inv); return i < 0 ? 0 : i >= _ny ? _ny - 1 : i; }

        /// <summary>Even-odd point in polygon, exactly PGeo.Contains.</summary>
        public bool Contains(double2 p)
        {
            if (_n < 3) return false;
            double fy = (p.y - _min.y) * _inv;
            if (fy < -1e-9 || fy > _ny + 1e-9) return false;
            int row = Cy(p.y);
            bool inside = false;
            for (int k = _rowStart[row], end = _rowStart[row + 1]; k < end; k++)
            {
                int i = _rowItems[k];
                // PGeo.Contains walks (a = ring[i], b = ring[i - 1]); the crossing test is symmetric in a and b except for
                // the division, so use the same order
                int j = i + 1 == _n ? 0 : i + 1;
                var a = _p[j]; var b = _p[i];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Exactly PGeo.IntervalsAt: the inside intervals of the horizontal line y (only the edges crossing that row).</summary>
        public List<double2> IntervalsAt(double y)
        {
            var r = new List<double2>();
            if (_n < 3) return r;
            double fy = (y - _min.y) * _inv;
            if (fy < -1e-9 || fy > _ny + 1e-9) return r;
            int row = Cy(y);
            var xs = new List<double>();
            for (int k = _rowStart[row], end = _rowStart[row + 1]; k < end; k++)
            {
                int i = _rowItems[k];
                int j = i + 1 == _n ? 0 : i + 1;
                var a = _p[j]; var b = _p[i];
                if ((a.y > y) != (b.y > y))
                    xs.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                if (xs[k + 1] > xs[k]) r.Add(new double2(xs[k], xs[k + 1]));
            return r;
        }

        /// <summary>Exactly PGeo.StripInside: x-intervals where the whole vertical extent [y0, y1] is inside.</summary>
        public List<double2> StripInside(double y0, double y1)
        {
            const double nudge = 1e-7;
            if (y1 - y0 < 4 * nudge) return IntervalsAt((y0 + y1) / 2);
            var ys = new List<double> { y0 + nudge, y1 - nudge };
            foreach (var p in _p)
                if (p.y > y0 + nudge && p.y < y1 - nudge) { ys.Add(p.y - nudge); ys.Add(p.y + nudge); }
            List<double2> acc = null;
            foreach (var y in ys)
            {
                var iv = IntervalsAt(y);
                acc = acc == null ? iv : PGeo.Intersect(acc, iv);
                if (acc.Count == 0) break;
            }
            return acc ?? new List<double2>();
        }

        /// <summary>Exactly "no edge of the ring properly crosses segment a-b" (PGeo.SegmentsCross with eps).</summary>
        public bool AnyCross(double2 a, double2 b, double eps)
        {
            bool hit = false;
            var mn = math.min(a, b); var mx = math.max(a, b);
            Edges(mn, mx, i =>
            {
                if (PGeo.SegmentsCross(a, b, _p[i], _p[i + 1 == _n ? 0 : i + 1], eps)) { hit = true; return false; }
                return true;
            });
            return hit;
        }

        /// <summary>Distance to the outline, exactly PGeo.DistToBoundary.</summary>
        public double Dist(double2 p) => Dist(p, double.MaxValue);

        /// <summary>
        /// Distance to the outline when it is below <paramref name="limit"/>; otherwise some value ≥ limit (cheap far
        /// from the outline). Exactly PGeo.DistToBoundary whenever that is below the limit.
        /// </summary>
        public double Dist(double2 p, double limit)
        {
            if (_n == 0) return double.MaxValue;
            double fx = (p.x - _min.x) * _inv, fy = (p.y - _min.y) * _inv;
            int cx = (int)Math.Floor(fx), cy = (int)Math.Floor(fy);
            double best = double.MaxValue;
            int maxRing = Math.Max(_nx, _ny) + Math.Max(Math.Abs(cx), Math.Abs(cy)) + 1;
            for (int r = 0; r <= maxRing; r++)
            {
                // every cell of ring r is at least (r - 1) cells from p
                double floor = (r - 1) * _cell;
                if (floor >= best || floor >= limit) break;
                int xa = cx - r, xb = cx + r, ya = cy - r, yb = cy + r;
                bool any = false;
                for (int y = Math.Max(0, ya); y <= Math.Min(_ny - 1, yb); y++)
                {
                    bool edgeRow = y == ya || y == yb;
                    int step = edgeRow ? 1 : xb - xa;
                    if (step <= 0) step = 1;
                    for (int x = xa; x <= xb; x += step)
                    {
                        if (x < 0 || x >= _nx) continue;
                        any = true;
                        int c = x + y * _nx;
                        for (int k = _cellStart[c], end = _cellStart[c + 1]; k < end; k++)
                        {
                            int i = _cellItems[k];
                            double d = PGeo.DistPointSeg(p, _p[i], _p[i + 1 == _n ? 0 : i + 1]);
                            if (d < best) best = d;
                        }
                    }
                }
                if (!any && (xa < 0 && xb >= _nx && ya < 0 && yb >= _ny)) break;
            }
            return best;
        }

        /// <summary>Calls f for every edge whose bounding box may touch the box [mn, mx] (an edge can come more than once).</summary>
        public void Edges(double2 mn, double2 mx, Func<int, bool> f)
        {
            if (_n == 0) return;
            if (mx.x < _min.x - _cell || mx.y < _min.y - _cell || mn.x > _min.x + (_nx + 1) * _cell || mn.y > _min.y + (_ny + 1) * _cell) return;
            int x0 = Cx(mn.x), x1 = Cx(mx.x), y0 = Cy(mn.y), y1 = Cy(mx.y);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = x + y * _nx;
                    for (int k = _cellStart[c], end = _cellStart[c + 1]; k < end; k++)
                        if (!f(_cellItems[k])) return;
                }
        }

        /// <summary>
        /// Exactly PGeo.ConvexInside(q, ring, tol): the convex polygon q lies inside the ring (boundary contact within tol).
        /// </summary>
        public bool ConvexInside(IReadOnlyList<double2> q, double tol = 1e-6)
        {
            foreach (var p in q)
                if (!Contains(p) && Dist(p, tol + 1e-9) > tol) return false;
            PGeo.Bounds(q, out var mn, out var mx);
            var pad = new double2(tol + 1e-9, tol + 1e-9);
            bool ok = true;
            // no ring vertex strictly inside q, no ring edge properly crossing an edge of q: both only near q's box
            Edges(mn - pad, mx + pad, i =>
            {
                var v = _p[i];
                if (v.x >= mn.x - pad.x && v.x <= mx.x + pad.x && v.y >= mn.y - pad.y && v.y <= mx.y + pad.y && PGeo.StrictlyInsideConvex(q, v, tol)) { ok = false; return false; }
                var w = _p[i + 1 == _n ? 0 : i + 1];
                for (int k = 0; k < q.Count; k++)
                    if (PGeo.SegmentsCross(q[k], q[(k + 1) % q.Count], v, w, 1e-9)) { ok = false; return false; }
                return true;
            });
            if (!ok) return false;
            for (int i = 0; i < q.Count; i++)
            {
                var m = (q[i] + q[(i + 1) % q.Count]) * 0.5;
                if (!Contains(m) && Dist(m, tol + 1e-9) > tol) return false;
            }
            return true;
        }
    }
}
