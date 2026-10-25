// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/Zellen/Geometrie.cs and
// Geometry/ParkingGeometry.Polygons.cs helpers, plus new robust helpers for the cell surfaces.
//
// Double-precision polygon helpers used only by the parking layer. Kept private to the layer on
// purpose: Core/Geometry (Poly, SurfaceAcceptance) is the shared float API owned by the geometry
// workstream; these are the exact-ish double versions the layout engine needs internally.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class PGeo
    {
        public const double Eps = 1e-9;

        public static double Cross(double2 a, double2 b) => a.x * b.y - a.y * b.x;
        public static double Dot(double2 a, double2 b) => a.x * b.x + a.y * b.y;
        public static double Len(double2 a) => Math.Sqrt(a.x * a.x + a.y * a.y);
        public static double2 Norm(double2 a) { var l = Len(a); return l > 1e-12 ? a / l : new double2(1, 0); }
        /// <summary>Left normal (CCW rotation by 90°).</summary>
        public static double2 Perp(double2 a) => new double2(-a.y, a.x);
        public static int Mod(int v, int m) { int r = v % m; return r < 0 ? r + m : r; }

        public static double SignedArea(IReadOnlyList<double2> ring)
        {
            double s = 0;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                s += ring[j].x * ring[i].y - ring[i].x * ring[j].y;
            return s * 0.5;
        }

        public static double Area(IReadOnlyList<double2> ring) => Math.Abs(SignedArea(ring));

        public static double Perimeter(IReadOnlyList<double2> ring)
        {
            double p = 0;
            for (int i = 0; i < ring.Count; i++) p += Len(ring[(i + 1) % ring.Count] - ring[i]);
            return p;
        }

        public static double2 Centroid(IReadOnlyList<double2> ring)
        {
            double a = 0, cx = 0, cy = 0;
            var o = ring[0];
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i] - o; var q = ring[(i + 1) % ring.Count] - o;
                double f = Cross(p, q);
                a += f; cx += (p.x + q.x) * f; cy += (p.y + q.y) * f;
            }
            if (Math.Abs(a) < 1e-12)
            {
                var s = new double2(0, 0);
                foreach (var p in ring) s += p;
                return s / Math.Max(1, ring.Count);
            }
            return o + new double2(cx / (3 * a), cy / (3 * a));
        }

        public static double2 Average(IReadOnlyList<double2> pts)
        {
            var s = new double2(0, 0);
            foreach (var p in pts) s += p;
            return s / Math.Max(1, pts.Count);
        }

        /// <summary>Even-odd point in polygon.</summary>
        public static bool Contains(IReadOnlyList<double2> ring, double2 p)
        {
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = ring[i]; var b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        public static double DistPointSeg(double2 p, double2 a, double2 b)
        {
            var ab = b - a;
            double l2 = Dot(ab, ab);
            if (l2 <= 0) return Len(p - a);
            double t = Math.Max(0, Math.Min(1, Dot(p - a, ab) / l2));
            return Len(p - (a + ab * t));
        }

        public static double2 ClosestOnSeg(double2 p, double2 a, double2 b, out double t)
        {
            var ab = b - a;
            double l2 = Dot(ab, ab);
            t = l2 <= 0 ? 0 : Math.Max(0, Math.Min(1, Dot(p - a, ab) / l2));
            return a + ab * t;
        }

        public static double DistToBoundary(IReadOnlyList<double2> ring, double2 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < ring.Count; i++)
                best = Math.Min(best, DistPointSeg(p, ring[i], ring[(i + 1) % ring.Count]));
            return best;
        }

        /// <summary>Removes consecutive duplicates closer than eps (keeps ring closed implicitly).</summary>
        public static List<double2> Dedup(IReadOnlyList<double2> ring, double eps = 1e-6)
        {
            var r = new List<double2>(ring.Count);
            foreach (var p in ring)
                if (r.Count == 0 || Len(p - r[r.Count - 1]) > eps) r.Add(p);
            while (r.Count > 1 && Len(r[0] - r[r.Count - 1]) <= eps) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>Dedup + drop collinear vertices (perpendicular deviation &lt; tol) and zero-area spikes.</summary>
        public static List<double2> Simplify(IReadOnlyList<double2> ring, double minEdge = 1e-6, double tol = 1e-7)
        {
            var r = Dedup(ring, minEdge);
            bool changed = true;
            while (changed && r.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < r.Count && r.Count >= 3; i++)
                {
                    var a = r[Mod(i - 1, r.Count)]; var b = r[i]; var c = r[(i + 1) % r.Count];
                    double ac = Len(c - a);
                    double dev = ac > 1e-12 ? Math.Abs(Cross(c - a, b - a)) / ac : Len(b - a);
                    bool spike = Dot(b - a, c - b) < 0 && dev <= tol * 10;
                    if (Len(b - a) <= minEdge || dev <= tol || spike)
                    {
                        r.RemoveAt(i); i--; changed = true;
                    }
                }
            }
            return r;
        }

        public static List<double2> EnsureCcw(List<double2> ring)
        {
            if (SignedArea(ring) < 0) ring.Reverse();
            return ring;
        }

        /// <summary>Proper or touching intersection of segments ab and cd.</summary>
        public static bool SegmentsIntersect(double2 a, double2 b, double2 c, double2 d, double eps = 1e-9)
        {
            double d1 = Cross(d - c, a - c), d2 = Cross(d - c, b - c);
            double d3 = Cross(b - a, c - a), d4 = Cross(b - a, d - a);
            if (((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) &&
                ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps))) return true;
            if (Math.Abs(d1) <= eps && OnSeg(c, d, a, eps)) return true;
            if (Math.Abs(d2) <= eps && OnSeg(c, d, b, eps)) return true;
            if (Math.Abs(d3) <= eps && OnSeg(a, b, c, eps)) return true;
            if (Math.Abs(d4) <= eps && OnSeg(a, b, d, eps)) return true;
            return false;
        }

        /// <summary>Strict crossing of the open segments (no touching).</summary>
        public static bool SegmentsCross(double2 a, double2 b, double2 c, double2 d, double eps = 1e-9)
        {
            double d1 = Cross(d - c, a - c), d2 = Cross(d - c, b - c);
            double d3 = Cross(b - a, c - a), d4 = Cross(b - a, d - a);
            return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) &&
                   ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
        }

        private static bool OnSeg(double2 a, double2 b, double2 p, double eps) =>
            Math.Min(a.x, b.x) - eps <= p.x && p.x <= Math.Max(a.x, b.x) + eps &&
            Math.Min(a.y, b.y) - eps <= p.y && p.y <= Math.Max(a.y, b.y) + eps;

        /// <summary>Intersection parameters of lines a+t(b-a) and c+u(d-c). False when parallel.</summary>
        public static bool LineLine(double2 a, double2 b, double2 c, double2 d, out double t, out double u)
        {
            var r = b - a; var s = d - c;
            double den = Cross(r, s);
            if (Math.Abs(den) < 1e-14) { t = u = 0; return false; }
            t = Cross(c - a, s) / den;
            u = Cross(c - a, r) / den;
            return true;
        }

        /// <summary>True when no two non-adjacent edges touch. O(n²).</summary>
        public static bool IsSimple(IReadOnlyList<double2> ring)
        {
            int n = ring.Count;
            if (n < 3) return false;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % n];
                if (Len(b - a) < 1e-9) return false;
                for (int j = i + 1; j < n; j++)
                {
                    if ((j + 1) % n == i || j == (i + 1) % n) continue;
                    if (SegmentsIntersect(a, b, ring[j], ring[(j + 1) % n])) return false;
                }
            }
            return true;
        }

        /// <summary>Nearest hit (ray parameter in units of dir) of a ray with the polygon outline, or +inf.</summary>
        public static double RayHit(double2 origin, double2 dir, IReadOnlyList<double2> ring, double minT = 1e-7)
        {
            double best = double.PositiveInfinity;
            var far = origin + dir;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                if (!LineLine(origin, far, a, b, out double t, out double u)) continue;
                if (u < -1e-9 || u > 1 + 1e-9 || t < minT) continue;
                if (t < best) best = t;
            }
            return best;
        }

        // ───────────── interval sets (x ranges inside a polygon) ─────────────

        /// <summary>Inside intervals of the horizontal line y (even-odd), sorted.</summary>
        public static List<double2> IntervalsAt(IReadOnlyList<double2> ring, double y)
        {
            var xs = new List<double>();
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = ring[i]; var b = ring[j];
                if ((a.y > y) != (b.y > y))
                    xs.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
            }
            xs.Sort();
            var r = new List<double2>();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                if (xs[k + 1] > xs[k]) r.Add(new double2(xs[k], xs[k + 1]));
            return r;
        }

        public static List<double2> Intersect(List<double2> a, List<double2> b)
        {
            var r = new List<double2>();
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                double lo = Math.Max(a[i].x, b[j].x), hi = Math.Min(a[i].y, b[j].y);
                if (hi > lo) r.Add(new double2(lo, hi));
                if (a[i].y < b[j].y) i++; else j++;
            }
            return r;
        }

        /// <summary>
        /// x-intervals where the whole vertical extent [y0,y1] is inside the polygon. Between
        /// vertex heights the interval ends move linearly, so sampling just inside y0, y1 and on
        /// both sides of every vertex height in between is exact up to the 1e-7 nudge.
        /// </summary>
        public static List<double2> StripInside(IReadOnlyList<double2> ring, double y0, double y1)
        {
            const double nudge = 1e-7;
            if (y1 - y0 < 4 * nudge) return IntervalsAt(ring, (y0 + y1) / 2);
            var ys = new List<double> { y0 + nudge, y1 - nudge };
            foreach (var p in ring)
                if (p.y > y0 + nudge && p.y < y1 - nudge) { ys.Add(p.y - nudge); ys.Add(p.y + nudge); }
            List<double2> acc = null;
            foreach (var y in ys)
            {
                var iv = IntervalsAt(ring, y);
                acc = acc == null ? iv : Intersect(acc, iv);
                if (acc.Count == 0) break;
            }
            return acc ?? new List<double2>();
        }

        /// <summary>Removes [lo,hi] ranges from an interval list.</summary>
        public static List<double2> Subtract(List<double2> a, IEnumerable<double2> cuts)
        {
            var cur = new List<double2>(a);
            foreach (var c in cuts)
            {
                var next = new List<double2>();
                foreach (var iv in cur)
                {
                    if (c.y <= iv.x || c.x >= iv.y) { next.Add(iv); continue; }
                    if (c.x > iv.x) next.Add(new double2(iv.x, c.x));
                    if (c.y < iv.y) next.Add(new double2(c.y, iv.y));
                }
                cur = next;
            }
            return cur;
        }

        // ───────────── convex polygons ─────────────

        /// <summary>
        /// Splits a convex CCW polygon by the directed line through p with direction d.
        /// left = part on the left (CCW side), right = the rest. Either may be null.
        /// Intersection points are computed from the edge in canonical (lexicographic) endpoint
        /// order so two cells sharing an edge get bit-identical cut points.
        /// </summary>
        public static void SplitConvex(List<double2> poly, double2 p, double2 d, out List<double2> left, out List<double2> right, double eps = 1e-9)
        {
            int n = poly.Count;
            var side = new double[n];
            bool anyL = false, anyR = false;
            double dl = Len(d);
            for (int i = 0; i < n; i++)
            {
                double s = Cross(d, poly[i] - p) / dl;
                if (Math.Abs(s) <= eps) s = 0;
                side[i] = s;
                if (s > 0) anyL = true; else if (s < 0) anyR = true;
            }
            if (!anyR) { left = poly; right = null; return; }
            if (!anyL) { left = null; right = poly; return; }
            left = new List<double2>(n + 2); right = new List<double2>(n + 2);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = poly[i]; var b = poly[j];
                double sa = side[i], sb = side[j];
                if (sa >= 0) left.Add(a);
                if (sa <= 0) right.Add(a);
                if ((sa > 0 && sb < 0) || (sa < 0 && sb > 0))
                {
                    var x = CutPoint(a, b, sa, sb);
                    left.Add(x); right.Add(x);
                }
            }
            if (left.Count < 3) left = null;
            if (right.Count < 3) right = null;
        }

        private static double2 CutPoint(double2 a, double2 b, double sa, double sb)
        {
            // canonical order → identical result for both cells sharing the edge
            bool swap = a.x > b.x || (a.x == b.x && a.y > b.y);
            if (swap) { var t = a; a = b; b = t; var s = sa; sa = sb; sb = s; }
            double f = sa / (sa - sb);
            return a + (b - a) * f;
        }

        /// <summary>Separating-axis overlap test for convex polygons (touching = no overlap).</summary>
        public static bool ConvexOverlap(IReadOnlyList<double2> a, IReadOnlyList<double2> b, double tol = 1e-6)
        {
            return !HasSeparatingAxis(a, b, tol) && !HasSeparatingAxis(b, a, tol);
        }

        private static bool HasSeparatingAxis(IReadOnlyList<double2> a, IReadOnlyList<double2> b, double tol)
        {
            for (int i = 0; i < a.Count; i++)
            {
                var e = a[(i + 1) % a.Count] - a[i];
                if (Len(e) < 1e-12) continue;
                var axis = Norm(Perp(e));
                double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
                foreach (var p in a) { double v = Dot(p, axis); minA = Math.Min(minA, v); maxA = Math.Max(maxA, v); }
                foreach (var p in b) { double v = Dot(p, axis); minB = Math.Min(minB, v); maxB = Math.Max(maxB, v); }
                if (maxA <= minB + tol || maxB <= minA + tol) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the convex polygon q lies inside the simple polygon ring (boundary contact allowed
        /// within tol): all corners inside-or-on, no ring vertex strictly inside q, no proper crossing.
        /// </summary>
        public static bool ConvexInside(IReadOnlyList<double2> q, IReadOnlyList<double2> ring, double tol = 1e-6)
        {
            foreach (var p in q)
                if (!Contains(ring, p) && DistToBoundary(ring, p) > tol) return false;
            foreach (var v in ring)
                if (StrictlyInsideConvex(q, v, tol)) return false;
            for (int i = 0; i < q.Count; i++)
            {
                var a = q[i]; var b = q[(i + 1) % q.Count];
                for (int j = 0; j < ring.Count; j++)
                    if (SegmentsCross(a, b, ring[j], ring[(j + 1) % ring.Count], 1e-9)) return false;
            }
            // midpoints guard against an edge running along outside through a notch
            for (int i = 0; i < q.Count; i++)
            {
                var m = (q[i] + q[(i + 1) % q.Count]) * 0.5;
                if (!Contains(ring, m) && DistToBoundary(ring, m) > tol) return false;
            }
            return true;
        }

        public static bool StrictlyInsideConvex(IReadOnlyList<double2> q, double2 p, double tol)
        {
            double s0 = SignedArea(q) >= 0 ? 1 : -1;
            for (int i = 0; i < q.Count; i++)
            {
                var a = q[i]; var b = q[(i + 1) % q.Count];
                var e = b - a; double l = Len(e);
                if (l < 1e-12) continue;
                if (s0 * Cross(e, p - a) / l <= tol) return false;
            }
            return true;
        }

        public static List<double2> ConvexHull(List<double2> pts)
        {
            var p = new List<double2>(pts);
            p.Sort((u, v) => u.x != v.x ? u.x.CompareTo(v.x) : u.y.CompareTo(v.y));
            if (p.Count < 3) return p;
            var h = new double2[2 * p.Count];
            int k = 0;
            for (int i = 0; i < p.Count; i++)
            {
                while (k >= 2 && Cross(h[k - 1] - h[k - 2], p[i] - h[k - 2]) <= 1e-12) k--;
                h[k++] = p[i];
            }
            for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Cross(h[k - 1] - h[k - 2], p[i] - h[k - 2]) <= 1e-12) k--;
                h[k++] = p[i];
            }
            var r = new List<double2>(k - 1);
            for (int i = 0; i < k - 1; i++) r.Add(h[i]);
            return r;
        }

        // ───────────── triangulation & convex decomposition ─────────────

        /// <summary>
        /// Ear clipping in double precision for a simple CCW ring. Collinear vertices are tolerated.
        /// Returns false if no ear can be found (non-simple input).
        /// </summary>
        public static bool Triangulate(IReadOnlyList<double2> ring, List<int3> tris)
        {
            int n = ring.Count;
            if (n < 3) return false;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            int guard = n * n + 10;
            int k = 0;
            while (idx.Count > 3 && guard-- > 0)
            {
                int m = idx.Count;
                bool clipped = false;
                for (int step = 0; step < m; step++, k++)
                {
                    int ip = idx[Mod(k - 1, m)], ic = idx[Mod(k, m)], inx = idx[Mod(k + 1, m)];
                    var a = ring[ip]; var b = ring[ic]; var c = ring[inx];
                    double cr = Cross(b - a, c - b);
                    if (cr <= 1e-12)
                    {
                        // a degenerate (collinear) vertex can be dropped without a triangle
                        if (Math.Abs(cr) <= 1e-12 && Dot(b - a, c - b) >= 0)
                        {
                            idx.RemoveAt(Mod(k, m)); clipped = true; break;
                        }
                        continue;
                    }
                    bool ear = true;
                    foreach (int j in idx)
                    {
                        if (j == ip || j == ic || j == inx) continue;
                        var p = ring[j];
                        // inside or ON the triangle blocks the ear: a reflex vertex lying exactly on the
                        // diagonal a-c (e.g. an L-shape's inner corner) would otherwise be cut off.
                        if (Len(p - a) < 1e-9 || Len(p - b) < 1e-9 || Len(p - c) < 1e-9) { ear = false; break; }
                        if (Cross(b - a, p - a) >= -1e-9 && Cross(c - b, p - b) >= -1e-9 && Cross(a - c, p - c) >= -1e-9) { ear = false; break; }
                    }
                    if (!ear) continue;
                    tris.Add(new int3(ip, ic, inx));
                    idx.RemoveAt(Mod(k, m));
                    clipped = true;
                    break;
                }
                if (!clipped) return false;
            }
            if (idx.Count == 3)
            {
                var a = ring[idx[0]]; var b = ring[idx[1]]; var c = ring[idx[2]];
                if (Cross(b - a, c - b) > 1e-12) tris.Add(new int3(idx[0], idx[1], idx[2]));
            }
            return true;
        }

        /// <summary>
        /// Convex decomposition (ear clipping + Hertel–Mehlhorn merging). Pieces are CCW and
        /// tile the ring exactly (shared vertices are the ring's own vertices).
        /// </summary>
        public static List<List<double2>> ConvexDecompose(IReadOnlyList<double2> ringIn)
        {
            var ring = Simplify(ringIn);
            EnsureCcw(ring);
            var result = new List<List<double2>>();
            if (ring.Count < 3) return result;
            if (IsConvex(ring)) { result.Add(ring); return result; }
            var tris = new List<int3>();
            if (!Triangulate(ring, tris)) return result;
            // polygons as index lists
            var polys = new List<List<int>>();
            foreach (var t in tris) polys.Add(new List<int> { t.x, t.y, t.z });
            // Merge the first pair (in index order) that shares an edge and stays convex, until none does. (perf) The scan
            // resumes instead of starting over after every merge (cubic in the corners: a 400-corner lot took seconds): the
            // pairs before piece i were all refused and only the merged piece changed, so the next merge is the first of
            // (a, i) for a < i, else the first of (i, b) - the very pair the old full restart found.
            bool TryMerge(int i, int j)
            {
                if (!SharedEdge(polys[i], polys[j], out int ai, out int bj)) return false;
                var u = MergeAt(polys[i], polys[j], ai, bj);
                if (u == null) return false;
                var pts = new List<double2>(u.Count);
                foreach (int q in u) pts.Add(ring[q]);
                if (!IsConvex(pts)) return false;
                polys[i] = u; polys.RemoveAt(j);
                return true;
            }
            for (int i = 0; i < polys.Count;)
            {
                bool merged = false;
                for (int j = i + 1; j < polys.Count && !merged; j++) merged = TryMerge(i, j);
                if (!merged) { i++; continue; }
                for (bool back = true; back;)
                {
                    back = false;
                    for (int a = 0; a < i; a++) if (TryMerge(a, i)) { i = a; back = true; break; }
                }
            }
            foreach (var pl in polys)
            {
                var pts = new List<double2>(pl.Count);
                foreach (int q in pl) pts.Add(ring[q]);
                result.Add(pts);
            }
            return result;
        }

        private static bool SharedEdge(List<int> a, List<int> b, out int ai, out int bj)
        {
            for (ai = 0; ai < a.Count; ai++)
            {
                int a0 = a[ai], a1 = a[(ai + 1) % a.Count];
                for (bj = 0; bj < b.Count; bj++)
                    if (b[bj] == a1 && b[(bj + 1) % b.Count] == a0) return true;
            }
            ai = bj = -1;
            return false;
        }

        private static List<int> MergeAt(List<int> a, List<int> b, int ai, int bj)
        {
            // a has edge (a[ai] -> a[ai+1]); b has the reverse at bj.
            var r = new List<int>(a.Count + b.Count - 2);
            for (int k = 0; k < a.Count; k++)
            {
                r.Add(a[(ai + 1 + k) % a.Count]);
                if (k == a.Count - 1) break;
            }
            // r now starts at a[ai+1] and ends at a[ai]; insert b's vertices after a[ai]
            // b from b[bj+2] .. b[bj-1] (skipping the shared edge endpoints)
            for (int k = 2; k < b.Count; k++) r.Add(b[(bj + k) % b.Count]);
            return r;
        }

        public static bool IsConvex(IReadOnlyList<double2> ring)
        {
            int n = ring.Count;
            if (n < 3) return false;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % n]; var c = ring[(i + 2) % n];
                if (Cross(b - a, c - b) < -1e-10 * Math.Max(1, Len(b - a) * Len(c - b))) return false;
            }
            return SignedArea(ring) > 0;
        }

        public static void Bounds(IReadOnlyList<double2> ring, out double2 min, out double2 max)
        {
            min = new double2(double.MaxValue, double.MaxValue); max = new double2(double.MinValue, double.MinValue);
            foreach (var p in ring) { min = math.min(min, p); max = math.max(max, p); }
        }

        public static double2[] Rect(double2 c, double2 u, double halfAlongU, double halfAcross)
        {
            var w = Perp(u);
            return new[]
            {
                c - u * halfAlongU - w * halfAcross,
                c + u * halfAlongU - w * halfAcross,
                c + u * halfAlongU + w * halfAcross,
                c - u * halfAlongU + w * halfAcross,
            };
        }

        /// <summary>Quad around segment a-b with half width hw (CCW).</summary>
        public static double2[] Corridor(double2 a, double2 b, double hw)
        {
            var u = Norm(b - a);
            var w = Perp(u) * hw;
            return new[] { a - w, b - w, b + w, a + w };
        }
    }
}
