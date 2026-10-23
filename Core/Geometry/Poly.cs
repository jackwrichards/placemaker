// Placemaker — polygon toolkit used by every layer.
//
// The public API of Poly / SurfaceAcceptance / EarClip is used by other workstreams: signatures and
// behaviour contracts are stable, new members are added freely. Conventions everywhere:
//   * rings are implicitly closed (last point != first), XZ plane as float2 (x right, z up),
//   * counter-clockwise = positive area = outer ring; clockwise = hole,
//   * nothing throws on bad geometry: degenerate input gives an empty result.
//
// Poly is split over several files: Poly.cs (basics, Clean, IsSimple), Poly.Ops.cs (booleans, offset,
// split, clip, holes), Poly.Shape.cs (simplify, smooth, resample, fill runs, pole of inaccessibility).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public static partial class Poly
    {
        // ───────────────────────────── measures ─────────────────────────────

        /// <summary>Shoelace area, positive for counter-clockwise rings (XZ plane, x right, z up).</summary>
        public static float SignedArea(IReadOnlyList<float2> ring) => (float)SignedAreaD(ring);

        /// <summary>Signed area in double, relative to the first point (exact enough at ±7 km world coordinates).</summary>
        public static double SignedAreaD(IReadOnlyList<float2> ring)
        {
            if (ring is float2[] arr) return SignedAreaD(arr);
            if (ring == null || ring.Count < 3) return 0;
            double2 o = ring[0];
            double a = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double2 p = (double2)ring[j] - o, q = (double2)ring[i] - o;
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5;
        }

        /// <summary>The same for an array (read directly, not through the list interface: these run millions of times a plan).</summary>
        public static double SignedAreaD(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return 0;
            double2 o = ring[0];
            double a = 0;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                double2 p = (double2)ring[j] - o, q = (double2)ring[i] - o;
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5;
        }

        public static float Area(IReadOnlyList<float2> ring) => math.abs(SignedArea(ring));

        public static float Perimeter(IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count < 2) return 0;
            float p = 0;
            for (int i = 0; i < ring.Count; i++) p += math.distance(ring[i], ring[(i + 1) % ring.Count]);
            return p;
        }

        public static float Perimeter(IReadOnlyList<float3> ring)
        {
            if (ring == null || ring.Count < 2) return 0;
            float p = 0;
            for (int i = 0; i < ring.Count; i++) p += math.distance(ring[i].xz, ring[(i + 1) % ring.Count].xz);
            return p;
        }

        /// <summary>Length of the shortest edge (0 for fewer than 2 points).</summary>
        public static float MinEdgeLength(IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count < 2) return 0;
            float m = float.MaxValue;
            for (int i = 0; i < ring.Count; i++) m = math.min(m, math.distance(ring[i], ring[(i + 1) % ring.Count]));
            return m;
        }

        /// <summary>Mean width 2A/P (m): a 1 m wide strip of any length gives ~1. Hairlines are near 0.</summary>
        public static float MeanWidth(IReadOnlyList<float2> ring)
        {
            float p = Perimeter(ring);
            return p > 0 ? 2f * Area(ring) / p : 0f;
        }

        // ───────────────────────────── orientation ─────────────────────────────

        public static bool IsCcw(IReadOnlyList<float2> ring) => SignedAreaD(ring) > 0;

        public static float2[] EnsureCcw(float2[] ring)
        {
            if (ring == null) return Array.Empty<float2>();
            if (SignedAreaD(ring) >= 0) return ring;
            var r = (float2[])ring.Clone();
            Array.Reverse(r);
            return r;
        }

        public static float3[] EnsureCcw(float3[] ring)
        {
            if (ring == null) return Array.Empty<float3>();
            if (SignedAreaD(To2D(ring)) >= 0) return ring;
            var r = (float3[])ring.Clone();
            Array.Reverse(r);
            return r;
        }

        public static float2[] EnsureCw(float2[] ring)
        {
            if (ring == null) return Array.Empty<float2>();
            if (SignedAreaD(ring) <= 0) return ring;
            var r = (float2[])ring.Clone();
            Array.Reverse(r);
            return r;
        }

        public static float2[] Reversed(IReadOnlyList<float2> ring)
        {
            var r = new float2[ring.Count];
            for (int i = 0; i < r.Length; i++) r[i] = ring[ring.Count - 1 - i];
            return r;
        }

        /// <summary>True when vertex i turns right (interior angle &gt; 180°) in a CCW ring.</summary>
        public static bool IsReflex(IReadOnlyList<float2> ring, int i)
        {
            int n = ring.Count;
            double2 a = ring[(i - 1 + n) % n], b = ring[i], c = ring[(i + 1) % n];
            return CrossD(b - a, c - b) < 0;
        }

        /// <summary>True when the ring is convex in either orientation (straight vertices allowed, turns below 1e-6 rad ignored).</summary>
        public static bool IsConvex(IReadOnlyList<float2> ring)
        {
            int n = ring.Count;
            if (n < 3) return false;
            int sign = 0;
            for (int i = 0; i < n; i++)
            {
                double2 a = ring[(i - 1 + n) % n], b = ring[i], c = ring[(i + 1) % n];
                double cr = CrossD(b - a, c - b);
                if (math.abs(cr) <= 1e-6 * math.length(b - a) * math.length(c - b)) continue; // straight up to float noise
                int s = cr > 0 ? 1 : -1;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return sign != 0;
        }

        // ───────────────────────────── conversion ─────────────────────────────

        public static float2[] To2D(IReadOnlyList<float3> ring)
        {
            var r = new float2[ring.Count];
            for (int i = 0; i < r.Length; i++) r[i] = ring[i].xz;
            return r;
        }

        /// <summary>Lift a 2D ring to 3D using a height sampler (null sampler = height 0).</summary>
        public static float3[] To3D(IReadOnlyList<float2> ring, Func<float2, float> heightAt)
        {
            var r = new float3[ring.Count];
            for (int i = 0; i < r.Length; i++) r[i] = new float3(ring[i].x, heightAt != null ? heightAt(ring[i]) : 0f, ring[i].y);
            return r;
        }

        internal static bool IsFinite(float2 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y);

        // ───────────────────────────── queries ─────────────────────────────

        /// <summary>Even-odd point in polygon (boundary points may go either way).</summary>
        public static bool Contains(IReadOnlyList<float2> ring, float2 p) => ContainsD(ring, p);

        public static bool Contains(float2[] ring, float2 p) => ContainsD(ring, p);

        /// <summary>The same for an array (read directly, not through the list interface).</summary>
        internal static bool ContainsD(float2[] ring, double2 p)
        {
            bool inside = false;
            int n = ring.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double2 a = ring[i], b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        internal static bool ContainsD(IReadOnlyList<float2> ring, double2 p)
        {
            if (ring is float2[] arr) return ContainsD(arr, p);
            bool inside = false;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double2 a = ring[i], b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Winding number of the ring around p (+1 inside a CCW ring, −1 inside a CW ring).</summary>
        public static int WindingNumber(IReadOnlyList<float2> ring, float2 p)
        {
            int w = 0, n = ring.Count;
            double2 q = p;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double2 a = ring[j], b = ring[i];
                if (a.y <= q.y)
                {
                    if (b.y > q.y && CrossD(b - a, q - a) > 0) w++;
                }
                else if (b.y <= q.y && CrossD(b - a, q - a) < 0) w--;
            }
            return w;
        }

        public static float2 Centroid(IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count == 0) return default;
            double2 o = ring[0];
            double cx = 0, cy = 0, a = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                double2 p = (double2)ring[j] - o, q = (double2)ring[i] - o;
                double f = p.x * q.y - q.x * p.y;
                a += f; cx += (p.x + q.x) * f; cy += (p.y + q.y) * f;
            }
            if (math.abs(a) < 1e-12)
            {
                double2 s = double2.zero;
                foreach (var p in ring) s += (double2)p;
                return (float2)(s / ring.Count);
            }
            return new float2((float)(cx / (3 * a) + o.x), (float)(cy / (3 * a) + o.y));
        }

        public static void Bounds(IReadOnlyList<float2> ring, out float2 min, out float2 max)
        {
            if (ring is float2[] arr) { Bounds(arr, out min, out max); return; }
            min = new float2(float.MaxValue); max = new float2(float.MinValue);
            foreach (var p in ring) { min = math.min(min, p); max = math.max(max, p); }
        }

        /// <summary>The same for an array (no enumerator made).</summary>
        public static void Bounds(float2[] ring, out float2 min, out float2 max)
        {
            min = new float2(float.MaxValue); max = new float2(float.MinValue);
            for (int i = 0; i < ring.Length; i++) { min = math.min(min, ring[i]); max = math.max(max, ring[i]); }
        }

        public static float DistanceToBoundary(IReadOnlyList<float2> ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
                best = math.min(best, math.sqrt(ReservedSpace.DistSqToSegment(p, ring[i], ring[(i + 1) % ring.Count])));
            return best;
        }

        /// <summary>Closest point on the ring's boundary; segment = index of edge (ring[i] → ring[i+1]), t along it.</summary>
        public static float2 ClosestPointOnBoundary(IReadOnlyList<float2> ring, float2 p, out int segment, out float t)
        {
            segment = -1; t = 0;
            float best = float.MaxValue;
            float2 bestP = p;
            for (int i = 0; i < ring.Count; i++)
            {
                float2 a = ring[i], ab = ring[(i + 1) % ring.Count] - a;
                float l = math.lengthsq(ab);
                float tt = l > 1e-12f ? math.saturate(math.dot(p - a, ab) / l) : 0f;
                float2 q = a + ab * tt;
                float d = math.distancesq(p, q);
                if (d < best) { best = d; bestP = q; segment = i; t = tt; }
            }
            return bestP;
        }

        // ───────────────────────────── segments ─────────────────────────────

        public static bool SegmentsIntersect(float2 p1, float2 p2, float2 p3, float2 p4)
        {
            float d1 = Cross(p4 - p3, p1 - p3), d2 = Cross(p4 - p3, p2 - p3);
            float d3 = Cross(p2 - p1, p3 - p1), d4 = Cross(p2 - p1, p4 - p1);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            const float eps = 1e-6f;
            if (math.abs(d1) < eps && OnSegment(p3, p4, p1)) return true;
            if (math.abs(d2) < eps && OnSegment(p3, p4, p2)) return true;
            if (math.abs(d3) < eps && OnSegment(p1, p2, p3)) return true;
            if (math.abs(d4) < eps && OnSegment(p1, p2, p4)) return true;
            return false;
        }

        public static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        internal static double CrossD(double2 a, double2 b) => a.x * b.y - a.y * b.x;

        private static bool OnSegment(float2 a, float2 b, float2 p) =>
            math.min(a.x, b.x) - 1e-6f <= p.x && p.x <= math.max(a.x, b.x) + 1e-6f &&
            math.min(a.y, b.y) - 1e-6f <= p.y && p.y <= math.max(a.y, b.y) + 1e-6f;

        /// <summary>Intersection of segments ab and cd; false when parallel or not crossing.</summary>
        public static bool SegmentIntersection(float2 a, float2 b, float2 c, float2 d, out float2 point, out float tAB, out float tCD)
        {
            double2 r = (double2)b - a, s = (double2)d - c;
            double den = CrossD(r, s);
            point = default; tAB = 0; tCD = 0;
            if (den == 0) return false;
            double2 ac = (double2)c - a;
            double t = CrossD(ac, s) / den, u = CrossD(ac, r) / den;
            if (t < 0 || t > 1 || u < 0 || u > 1) return false;
            tAB = (float)t; tCD = (float)u;
            point = (float2)((double2)a + r * t);
            return true;
        }

        /// <summary>Squared distance from p to segment ab, in double (stable at world coordinates).</summary>
        internal static double DistSqToSegmentD(double2 p, double2 a, double2 b)
        {
            double2 ab = b - a;
            double l = math.lengthsq(ab);
            double t = l > 0 ? math.clamp(math.dot(p - a, ab) / l, 0, 1) : 0;
            return math.distancesq(p, a + ab * t);
        }

        // ───────────────────────────── simplicity ─────────────────────────────

        /// <summary>
        /// True when no two non-adjacent edges intersect or touch, no edge has zero length, adjacent
        /// edges do not fold back onto each other and the area is not zero. Sweep over x: about
        /// O(n log n) for ordinary rings (O(n²) only for pathological all-overlapping inputs).
        /// </summary>
        // IsSimple's working arrays, kept per thread (it runs for every outline a plan prepares: in a city its arrays and
        // the sort's comparer were a tenth of a plan's garbage)
        private sealed class SimpleScratch
        {
            public double2[] P = new double2[64];
            public double[] MinX = new double[64];
            public int[] Order = new int[64];
            public readonly List<int> Active = new List<int>();
            public readonly ByMinX Cmp = new ByMinX();
        }

        private sealed class ByMinX : IComparer<int>
        {
            public double[] MinX;
            public int Compare(int x, int y) => MinX[x].CompareTo(MinX[y]);
        }

        [ThreadStatic] private static SimpleScratch s_simple;

        public static bool IsSimple(IReadOnlyList<float2> ring)
        {
            if (ring == null) return false;
            int n = ring.Count;
            if (n < 3) return false;
            double2 o = ring[0];
            var w = s_simple ??= new SimpleScratch();
            if (w.P.Length < n) { w.P = new double2[n * 2]; w.MinX = new double[n * 2]; w.Order = new int[n * 2]; }
            var p = w.P;
            for (int i = 0; i < n; i++)
            {
                if (!IsFinite(ring[i])) return false;
                p[i] = (double2)ring[i] - o;
            }
            for (int i = 0; i < n; i++)
            {
                double2 a = p[(i - 1 + n) % n], b = p[i], c = p[(i + 1) % n];
                if (math.all(b == c)) return false;
                // Fold-back of adjacent edges (zero-width spike).
                double2 ab = b - a, bc = c - b;
                if (math.dot(ab, bc) < 0 && math.abs(CrossD(ab, bc)) <= 1e-12 * math.length(ab) * math.length(bc)) return false;
            }
            double area = 0;
            for (int i = 0, j = n - 1; i < n; j = i++) area += CrossD(p[j], p[i]);
            if (area == 0) return false;
            if (n == 3) return true;

            var minX = w.MinX;
            var order = w.Order;
            for (int i = 0; i < n; i++) { minX[i] = math.min(p[i].x, p[(i + 1) % n].x); order[i] = i; }
            w.Cmp.MinX = minX;
            Array.Sort(order, 0, n, w.Cmp);
            const double touch = 1e-6;
            var active = w.Active;
            active.Clear();
            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                double2 a = p[i], b = p[(i + 1) % n];
                for (int k = active.Count - 1; k >= 0; k--)
                {
                    int j = active[k];
                    if (math.max(p[j].x, p[(j + 1) % n].x) < minX[i] - touch)
                    {
                        active[k] = active[active.Count - 1];
                        active.RemoveAt(active.Count - 1);
                    }
                }
                double y0 = math.min(a.y, b.y) - touch, y1 = math.max(a.y, b.y) + touch;
                foreach (int j in active)
                {
                    if (j == (i + 1) % n || i == (j + 1) % n) continue;
                    double2 c = p[j], d = p[(j + 1) % n];
                    if (math.max(c.y, d.y) < y0 || math.min(c.y, d.y) > y1) continue;
                    if (SegmentsTouchD(a, b, c, d, touch)) return false;
                }
                active.Add(i);
            }
            return true;
        }

        internal static bool SegmentsTouchD(double2 a, double2 b, double2 c, double2 d, double tol)
        {
            double d1 = CrossD(b - a, c - a), d2 = CrossD(b - a, d - a);
            double d3 = CrossD(d - c, a - c), d4 = CrossD(d - c, b - c);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            double t2 = tol * tol;
            return DistSqToSegmentD(c, a, b) <= t2 || DistSqToSegmentD(d, a, b) <= t2 ||
                   DistSqToSegmentD(a, c, d) <= t2 || DistSqToSegmentD(b, c, d) <= t2;
        }

        // ───────────────────────────── cleaning ─────────────────────────────

        /// <summary>
        /// Removes duplicate/near-duplicate points (&lt; minEdge), collinear points and zero-area spikes,
        /// and resolves self-touching / self-intersecting rings (largest resulting piece is kept).
        /// Returns a simple CCW ring or an empty array if nothing valid remains.
        /// Use <see cref="CleanAll"/> to keep every piece (and holes).
        /// </summary>
        public static float2[] Clean(IReadOnlyList<float2> ring, float minEdge = 0.05f)
        {
            var pts = CleanLocal(ring, minEdge);
            if (pts.Count < 3) return Array.Empty<float2>();
            if (!IsSimple(pts))
            {
                var regions = Normalize(pts, FillRule.NonZero);
                pts = regions.Count > 0 ? CleanLocal(regions[0].Outer, minEdge) : new List<float2>();
                if (pts.Count < 3 || !IsSimple(pts)) return Array.Empty<float2>();
            }
            if (math.abs(SignedAreaD(pts)) < 1e-3) return Array.Empty<float2>();
            return EnsureCcw(pts.ToArray());
        }

        /// <summary>
        /// Like <see cref="Clean"/> but keeps every piece a self-intersecting ring falls apart into,
        /// and the holes it encloses (non-zero winding). Regions are ordered by area, largest first.
        /// </summary>
        public static List<Region> CleanAll(IReadOnlyList<float2> ring, float minEdge = 0.05f)
        {
            var result = new List<Region>();
            var pts = CleanLocal(ring, minEdge);
            if (pts.Count < 3) return result;
            if (IsSimple(pts))
            {
                if (math.abs(SignedAreaD(pts)) >= 1e-3) result.Add(new Region(EnsureCcw(pts.ToArray())));
                return result;
            }
            foreach (var r in Normalize(pts, FillRule.NonZero))
            {
                var outer = CleanLocal(r.Outer, minEdge);
                if (outer.Count < 3 || !IsSimple(outer) || math.abs(SignedAreaD(outer)) < 1e-3) continue;
                var reg = new Region(EnsureCcw(outer.ToArray()));
                foreach (var h in r.Holes)
                {
                    var hc = CleanLocal(h, minEdge);
                    if (hc.Count >= 3 && IsSimple(hc) && math.abs(SignedAreaD(hc)) >= 1e-3) reg.Holes.Add(EnsureCw(hc.ToArray()));
                }
                result.Add(reg);
            }
            return result;
        }

        /// <summary>
        /// Local clean-up on a linked list (amortised O(n)): a vertex goes when it is closer than
        /// minEdge to its predecessor, or when the triangle (prev, v, next) is thinner than the flat
        /// tolerance — that one test removes collinear points, needles and fold-back spikes alike.
        /// </summary>
        internal static List<float2> CleanLocal(IReadOnlyList<float2> ring, float minEdge)
        {
            var src = new List<float2>(ring?.Count ?? 0);
            if (ring != null) foreach (var p in ring) if (IsFinite(p)) src.Add(p);
            while (src.Count > 1 && math.all(src[0] == src[src.Count - 1])) src.RemoveAt(src.Count - 1);
            int n = src.Count;
            if (n < 3) return src;
            minEdge = math.max(minEdge, 0f);
            double minEdge2 = (double)minEdge * minEdge;
            double flatTol = math.clamp(minEdge * 0.1, 1e-4, 0.02);
            double2 o = src[0];
            var pts = new double2[n];
            for (int i = 0; i < n; i++) pts[i] = (double2)src[i] - o;
            var prev = new int[n];
            var next = new int[n];
            var alive = new bool[n];
            var stack = new ListStack<int>(n);
            for (int i = 0; i < n; i++)
            {
                prev[i] = (i - 1 + n) % n; next[i] = (i + 1) % n; alive[i] = true;
                stack.Push(n - 1 - i);
            }
            int count = n, guard = 8 * n + 16;
            while (stack.Count > 0 && count >= 3 && guard-- > 0)
            {
                int i = stack.Pop();
                if (!alive[i]) continue;
                int a = prev[i], c = next[i];
                double2 pa = pts[a], pb = pts[i], pc = pts[c];
                bool remove = math.distancesq(pa, pb) < minEdge2 || math.all(pa == pb);
                if (!remove)
                {
                    double lab = math.distance(pa, pb), lbc = math.distance(pb, pc), lca = math.distance(pc, pa);
                    double longest = math.max(lab, math.max(lbc, lca));
                    double area2 = math.abs(CrossD(pb - pa, pc - pa));
                    if (longest <= 0 || area2 / longest < flatTol) remove = true;
                }
                if (!remove) continue;
                alive[i] = false; count--;
                next[a] = c; prev[c] = a;
                stack.Push(c); stack.Push(a);
            }
            var result = new List<float2>(count);
            if (count < 3) return result;
            int start = 0;
            while (!alive[start]) start++;
            int k = start;
            do { result.Add(src[k]); k = next[k]; } while (k != start && result.Count <= n);
            return result;
        }

        /// <summary>Removes only (almost exactly) collinear vertices: perpendicular deviation ≤ tolerance (m).</summary>
        public static float2[] RemoveCollinear(IReadOnlyList<float2> ring, float tolerance = 1e-4f)
        {
            var pts = new List<float2>(ring);
            bool changed = true;
            while (changed && pts.Count > 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count > 3; i++)
                {
                    double2 a = pts[(i - 1 + pts.Count) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                    double lac = math.distance(a, c);
                    bool dup = math.all(a == b);
                    if (dup || (lac > 0 && math.abs(CrossD(b - a, c - a)) / lac <= tolerance && math.dot(b - a, c - b) >= 0))
                    {
                        pts.RemoveAt(i); i--; changed = true;
                    }
                }
            }
            return pts.ToArray();
        }
    }
}
