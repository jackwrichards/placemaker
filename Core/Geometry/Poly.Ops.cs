// Placemaker — polygon operations built on the planar arrangement: booleans, normalisation,
// robust offset, splitting by a polyline, clipping segments, opening holes.
//
// Offset method (same idea as Clipper's offsetter, written from scratch): every edge is shifted by
// the distance; where the shifted edges overlap (convex corner of an inset) the raw path runs back
// through the original vertex, where they leave a gap (reflex corner of an inset) a miter / round /
// square join is inserted. The raw path self-intersects wherever the shape collapses or pinches;
// keeping the faces with positive winding yields the exact offset, split into as many rings as it
// falls apart into. A final distance check drops any face closer to the input than the distance.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public static partial class Poly
    {
        // ───────────────────────────── booleans ─────────────────────────────

        internal static bool Inside(int w, FillRule rule) =>
            rule == FillRule.EvenOdd ? (w & 1) != 0 : rule == FillRule.Positive ? w > 0 : w != 0;

        /// <summary>
        /// General boolean of two ring sets (any orientation, self-intersections allowed; the fill
        /// rule decides inside). Result: simple regions (outer CCW, holes CW), largest first.
        /// </summary>
        public static List<Region> Boolean(IEnumerable<IReadOnlyList<float2>> subject, IEnumerable<IReadOnlyList<float2>> clip,
            BoolOp op, FillRule rule = FillRule.NonZero)
        {
            var subj = subject != null ? new List<IReadOnlyList<float2>>(subject) : new List<IReadOnlyList<float2>>();
            var clp = clip != null ? new List<IReadOnlyList<float2>>(clip) : new List<IReadOnlyList<float2>>();
            var all = new List<IReadOnlyList<float2>>(subj);
            all.AddRange(clp);
            var arr = Arrangement.For(all);
            foreach (var r in subj) arr.AddRing(r, 0);
            foreach (var r in clp) arr.AddRing(r, 1);
            var regions = arr.Extract((wa, wb) =>
            {
                bool a = Inside(wa, rule), b = Inside(wb, rule);
                switch (op)
                {
                    case BoolOp.Union: return a || b;
                    case BoolOp.Intersection: return a && b;
                    case BoolOp.Difference: return a && !b;
                    default: return a != b;
                }
            });
            arr.Release();
            return regions;
        }

        public static List<Region> Union(IReadOnlyList<float2> a, IReadOnlyList<float2> b) => Boolean(new[] { a }, new[] { b }, BoolOp.Union);
        public static List<Region> Intersection(IReadOnlyList<float2> a, IReadOnlyList<float2> b) => Boolean(new[] { a }, new[] { b }, BoolOp.Intersection);
        public static List<Region> Difference(IReadOnlyList<float2> a, IReadOnlyList<float2> b) => Boolean(new[] { a }, new[] { b }, BoolOp.Difference);
        public static List<Region> Xor(IReadOnlyList<float2> a, IReadOnlyList<float2> b) => Boolean(new[] { a }, new[] { b }, BoolOp.Xor);

        public static List<Region> Boolean(IEnumerable<Region> subject, IEnumerable<Region> clip, BoolOp op) =>
            Boolean(RingsOf(subject), RingsOf(clip), op, FillRule.NonZero);

        /// <summary>Union of many rings/regions at once (e.g. merging block faces or painted islands).</summary>
        public static List<Region> UnionAll(IEnumerable<IReadOnlyList<float2>> rings) => Boolean(rings, null, BoolOp.Union, FillRule.NonZero);

        private static List<IReadOnlyList<float2>> RingsOf(IEnumerable<Region> regions)
        {
            var list = new List<IReadOnlyList<float2>>();
            if (regions == null) return list;
            foreach (var r in regions)
            {
                if (r == null) continue;
                var n = r.Normalized();
                foreach (var ring in n.Rings()) list.Add(ring);
            }
            return list;
        }

        /// <summary>
        /// Resolves self-intersections / self-touching of a ring into simple regions using the fill rule.
        /// A figure-eight gives two regions; a ring looping around twice gives one.
        /// </summary>
        public static List<Region> Normalize(IReadOnlyList<float2> ring, FillRule rule = FillRule.NonZero) =>
            Boolean(new[] { ring }, null, BoolOp.Union, rule);

        // ───────────────────────────── offset ─────────────────────────────

        /// <summary>
        /// Inward offset (negative distance = outward). May return several rings when the shape
        /// pinches apart, or none when it vanishes. Rings are CCW and simple. Reflex corners get a
        /// miter (limit 4, squared beyond). Holes an outward offset might enclose are filled — use
        /// <see cref="Offset(Region,float,OffsetJoin,float,float)"/> to keep them.
        /// </summary>
        public static List<float2[]> Offset(IReadOnlyList<float2> ring, float inward)
        {
            var result = new List<float2[]>();
            var c = Clean(ring);
            if (c.Length < 3) return result;
            foreach (var r in Offset(new Region(c), inward)) result.Add(r.Outer);
            return result;
        }

        /// <summary>Offset with join control (rings may be returned with holes).</summary>
        public static List<Region> Offset(IReadOnlyList<float2> ring, float inward, OffsetJoin join, float miterLimit = 4f, float arcTolerance = 0.05f)
        {
            var c = Clean(ring);
            if (c.Length < 3) return new List<Region>();
            return Offset(new Region(c), inward, join, miterLimit, arcTolerance);
        }

        /// <summary>
        /// Robust offset of a region with holes. inward &gt; 0 shrinks (holes grow), &lt; 0 grows.
        /// Result regions are simple, outer CCW / holes CW, largest first; empty when it vanishes.
        /// </summary>
        public static List<Region> Offset(Region region, float inward, OffsetJoin join = OffsetJoin.Miter, float miterLimit = 4f, float arcTolerance = 0.05f)
        {
            return Offset(new[] { region }, inward, join, miterLimit, arcTolerance);
        }

        public static List<Region> Offset(IReadOnlyList<Region> regions, float inward, OffsetJoin join = OffsetJoin.Miter, float miterLimit = 4f, float arcTolerance = 0.05f)
        {
            var rings = RingsOf(regions);
            var result = new List<Region>();
            if (rings.Count == 0 || float.IsNaN(inward) || float.IsInfinity(inward)) return result;
            if (math.abs(inward) < 1e-6f) return Boolean(rings, null, BoolOp.Union, FillRule.NonZero);

            // Bounds grow by the offset when growing.
            double2 mn = new double2(double.MaxValue), mx = new double2(double.MinValue);
            int count = 0;
            foreach (var r in rings) { count += r.Count; foreach (var p in r) { mn = math.min(mn, (double2)p); mx = math.max(mx, (double2)p); } }
            double grow = math.max(0, -inward) * math.max(miterLimit, 1.5f) + 1;
            var arr = Arrangement.Rent(mn - grow, mx + grow, count * 3);
            double d = inward;
            var cleanedLocal = new List<double2[]>();
            foreach (var r in rings)
            {
                var c = CleanLocal(r, 1e-4f);
                if (c.Count < 3) continue;
                var local = new double2[c.Count];
                for (int i = 0; i < c.Count; i++) local[i] = arr.ToLocal(c[i]);
                cleanedLocal.Add(local);
                arr.AddRingLocal(RawOffset(local, d, join, math.max(1f, miterLimit), math.max(1e-3f, arcTolerance)), 0);
            }
            var candidates = arr.Extract((wa, wb) => wa > 0);

            // Guard against spurious faces: every outer vertex of a true offset lies at ≥ |d| from the input.
            double tol = math.max(2e-3, math.abs(d) * 2e-3) + (join == OffsetJoin.Round ? arcTolerance : 0);
            double minDist = math.abs(d) - tol;
            foreach (var reg in candidates)
            {
                bool ok = true;
                foreach (var p in reg.Outer)
                {
                    double2 q = arr.ToLocal(p);
                    if (MinDistSq(cleanedLocal, q) < minDist * minDist) { ok = false; break; }
                }
                if (ok) result.Add(reg);
            }
            arr.Release();
            return result;
        }

        private static double MinDistSq(List<double2[]> rings, double2 q)
        {
            double best = double.MaxValue;
            foreach (var r in rings)
                for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
                {
                    double2 a = r[j], b = r[i];
                    // Cheap reject on the bounding box distance.
                    double dx = math.max(math.max(math.min(a.x, b.x) - q.x, q.x - math.max(a.x, b.x)), 0);
                    double dy = math.max(math.max(math.min(a.y, b.y) - q.y, q.y - math.max(a.y, b.y)), 0);
                    if (dx * dx + dy * dy >= best) continue;
                    best = math.min(best, DistSqToSegmentD(q, a, b));
                }
            return best;
        }

        /// <summary>Raw (self-intersecting) offset path of one ring, left side positive.</summary>
        private static List<double2> RawOffset(double2[] r, double d, OffsetJoin join, double miterLimit, double arcTol)
        {
            int n = r.Length;
            var o = new List<double2>(n * 3);
            for (int i = 0; i < n; i++)
            {
                double2 prev = r[(i - 1 + n) % n], cur = r[i], next = r[(i + 1) % n];
                double2 d1 = math.normalize(cur - prev), d2 = math.normalize(next - cur);
                double2 n1 = new double2(-d1.y, d1.x), n2 = new double2(-d2.y, d2.x);
                double sin = CrossD(d1, d2), cos = math.dot(d1, d2);
                double2 p1 = cur + n1 * d, p2 = cur + n2 * d;
                if (math.abs(sin) < 1e-9 && cos > 0)
                {
                    o.Add((p1 + p2) * 0.5);
                    continue;
                }
                bool overlap = sin * d > 0 || (math.abs(sin) < 1e-9 && d > 0);
                if (overlap)
                {
                    // Shifted edges overlap: run back through the vertex; the loop this makes has
                    // winding ≤ 0 and vanishes in the positive fill.
                    o.Add(p1); o.Add(cur); o.Add(p2);
                    continue;
                }
                switch (join)
                {
                    case OffsetJoin.Round:
                        AddArc(o, cur, n1, n2, d, arcTol);
                        break;
                    case OffsetJoin.Square:
                        AddSquare(o, cur, p1, p2, d1, d2, n1, n2, math.abs(d));
                        break;
                    default:
                        double k = 1 + math.dot(n1, n2);
                        if (k > 2.0 / (miterLimit * miterLimit))
                            o.Add(cur + (n1 + n2) * (d / k));
                        else
                            AddSquare(o, cur, p1, p2, d1, d2, n1, n2, math.abs(d) * miterLimit);
                        break;
                }
            }
            return o;
        }

        private static void AddSquare(List<double2> o, double2 cur, double2 p1, double2 p2, double2 d1, double2 d2, double2 n1, double2 n2, double reach)
        {
            double2 bis = n1 + n2;
            double bl = math.length(bis);
            double2 p1n = p1 - cur;
            // Gap side direction: towards the shifted points; a full fold-back uses the incoming direction.
            bis = bl > 1e-9 ? bis / bl * math.sign(math.dot(bis, p1n)) : d1;
            double c1 = math.dot(d1, bis), c2 = math.dot(-d2, bis);
            if (c1 <= 1e-9 || c2 <= 1e-9) { o.Add(p1); o.Add(p2); return; }
            double s1 = (reach - math.dot(p1 - cur, bis)) / c1, s2 = (reach - math.dot(p2 - cur, bis)) / c2;
            o.Add(p1 + d1 * math.max(0, s1));
            o.Add(p2 - d2 * math.max(0, s2));
        }

        private static void AddArc(List<double2> o, double2 cur, double2 n1, double2 n2, double d, double tol)
        {
            double r = math.abs(d);
            double sweep = math.atan2(CrossD(n1, n2), math.dot(n1, n2));
            double step = tol >= r ? math.PI / 4 : 2 * math.acos(1 - tol / r);
            step = math.clamp(step, math.PI / 180, math.PI / 4);
            int steps = math.max(1, (int)math.ceil(math.abs(sweep) / step));
            for (int k = 0; k <= steps; k++)
            {
                double a = sweep * k / steps, c = math.cos(a), s = math.sin(a);
                double2 nn = new double2(n1.x * c - n1.y * s, n1.x * s + n1.y * c);
                o.Add(cur + nn * d);
            }
        }

        // ───────────────────────────── split ─────────────────────────────

        /// <summary>
        /// Splits a ring by a polyline (the player's divider line). Returns every part (2+ when the
        /// line crosses the ring, the cleaned ring alone when it does not). Each end of the line is
        /// first extended by extend metres so a line drawn slightly short of the edge still cuts.
        /// Dangling bits of the line inside a part are ignored. Parts are simple CCW rings without
        /// holes (a closed loop drawn inside the ring makes an island part and a part around it,
        /// which is opened with a seam), largest first.
        /// </summary>
        public static List<float2[]> Split(IReadOnlyList<float2> ring, IReadOnlyList<float2> polyline, float extend = 0f)
        {
            var result = new List<float2[]>();
            var c = Clean(ring, 1e-3f);
            if (c.Length < 3) return result;
            foreach (var reg in Split(new Region(c), polyline, extend))
            {
                if (!reg.HasHoles) result.Add(reg.Outer);
                else result.AddRange(CutHoles(reg));
            }
            return result;
        }

        /// <summary>Splits a region (holes allowed) by a polyline; parts keep their holes.</summary>
        public static List<Region> Split(Region region, IReadOnlyList<float2> polyline, float extend = 0f)
        {
            var rings = RingsOf(new[] { region });
            if (rings.Count == 0) return new List<Region>();
            var line = ExtendPolyline(polyline, extend);
            var all = new List<IReadOnlyList<float2>>(rings);
            if (line != null) all.Add(line);
            var arr = Arrangement.For(all);
            foreach (var r in rings) arr.AddRing(r, 0);
            if (line != null) arr.AddPolyline(line);
            var parts = arr.Extract((wa, wb) => wa != 0, splitByCuts: true);
            arr.Release();
            return parts;
        }

        /// <summary>Splits a ring by an infinite line through point along direction. Part 0 is the left side.</summary>
        public static List<float2[]> SplitByLine(IReadOnlyList<float2> ring, float2 point, float2 direction)
        {
            var parts = Split(ring, InfiniteLine(ring, point, direction));
            float2 dir = math.normalizesafe(direction, new float2(1, 0));
            // Left of the line first, then by area (deterministic order for the split layer's part:0 / part:1).
            parts.Sort((a, b) =>
            {
                bool la = Cross(dir, Centroid(a) - point) > 0, lb = Cross(dir, Centroid(b) - point) > 0;
                if (la != lb) return la ? -1 : 1;
                return Area(b).CompareTo(Area(a));
            });
            return parts;
        }

        /// <summary>
        /// Splits a ring with a straight line of the given direction, placed so the part left of the
        /// line holds ratio (0..1) of the area. Returns [left, right] (fewer if the ring is degenerate;
        /// a side may consist of several pieces for non-convex rings — they are all returned, left first).
        /// </summary>
        public static List<float2[]> SplitByRatio(IReadOnlyList<float2> ring, float2 direction, float ratio)
        {
            var c = Clean(ring, 1e-3f);
            var result = new List<float2[]>();
            if (c.Length < 3) return result;
            ratio = math.saturate(ratio);
            float2 dir = math.normalizesafe(direction, new float2(1, 0));
            float2 nrm = new float2(-dir.y, dir.x);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in c) { float s = math.dot(p, nrm); lo = math.min(lo, s); hi = math.max(hi, s); }
            if (ratio <= 0 || ratio >= 1 || hi - lo < 1e-3f) { result.Add(c); return result; }
            double total = math.abs(SignedAreaD(c));
            // Bisection on the line's offset along its left normal.
            float a = lo, b = hi;
            for (int it = 0; it < 40; it++)
            {
                float m = 0.5f * (a + b);
                double left = LeftArea(c, dir, nrm * m);
                // Moving the line along its left normal shrinks the left side.
                if (left / total > ratio) a = m; else b = m;
            }
            float off = 0.5f * (a + b);
            return SplitByLine(c, nrm * off, dir);
        }

        private static double LeftArea(float2[] ring, float2 dir, float2 point)
        {
            // Half-plane clip (Sutherland–Hodgman) is exact for area even on non-convex rings.
            var outPts = new List<double2>();
            int n = ring.Length;
            double2 o = point, dd = dir;
            for (int i = 0; i < n; i++)
            {
                double2 p = (double2)ring[i] - o, q = (double2)ring[(i + 1) % n] - o;
                double sp = CrossD(dd, p), sq = CrossD(dd, q);
                if (sp >= 0) outPts.Add(p);
                if ((sp >= 0) != (sq >= 0)) outPts.Add(p + (q - p) * (sp / (sp - sq)));
            }
            double a = 0;
            for (int i = 0, j = outPts.Count - 1; i < outPts.Count; j = i++) a += CrossD(outPts[j], outPts[i]);
            return math.abs(a * 0.5);
        }

        private static float2[] InfiniteLine(IReadOnlyList<float2> ring, float2 point, float2 direction)
        {
            Bounds(ring, out var mn, out var mx);
            float reach = math.length(mx - mn) + math.length(point - (mn + mx) * 0.5f) + 10f;
            float2 dir = math.normalizesafe(direction, new float2(1, 0));
            return new[] { point - dir * reach, point + dir * reach };
        }

        private static float2[] ExtendPolyline(IReadOnlyList<float2> line, float extend)
        {
            if (line == null) return null;
            var pts = new List<float2>(line.Count);
            foreach (var p in line)
                if (IsFinite(p) && (pts.Count == 0 || math.distancesq(pts[pts.Count - 1], p) > 1e-10f)) pts.Add(p);
            if (pts.Count < 2) return null;
            if (extend > 0)
            {
                pts[0] = pts[0] + math.normalizesafe(pts[0] - pts[1]) * extend;
                int k = pts.Count - 1;
                pts[k] = pts[k] + math.normalizesafe(pts[k] - pts[k - 1]) * extend;
            }
            return pts.ToArray();
        }

        // ───────────────────────────── clipping ─────────────────────────────

        /// <summary>Parameter intervals (t0, t1) ⊂ [0,1] of segment ab that lie inside the ring (even-odd).</summary>
        public static List<float2> ClipSegmentIntervals(IReadOnlyList<float2> ring, float2 a, float2 b) => ClipSegmentIntervals(ring, a, b, new List<float2>());

        // the crossing parameters ClipSegmentIntervals sorts, kept per thread
        [ThreadStatic] private static List<double> s_clipTs;

        /// <summary>ClipSegmentIntervals adding to a list of the caller's.</summary>
        public static List<float2> ClipSegmentIntervals(IReadOnlyList<float2> ring, float2 a, float2 b, List<float2> result)
        {
            if (ring == null || ring.Count < 3) return result;
            var ts = s_clipTs ??= new List<double>();
            ts.Clear();
            ts.Add(0);
            ts.Add(1);
            double2 pa = a, r = (double2)b - a;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                double2 c = ring[i], s = (double2)ring[(i + 1) % n] - c;
                double den = CrossD(r, s);
                if (den == 0) continue;
                double t = CrossD(c - pa, s) / den, u = CrossD(c - pa, r) / den;
                if (t > 0 && t < 1 && u >= 0 && u <= 1) ts.Add(t);
            }
            ts.Sort();
            double2 start = default;
            bool open = false;
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                if (ts[k + 1] - ts[k] < 1e-9) continue;
                bool inside = ContainsD(ring, pa + r * ((ts[k] + ts[k + 1]) * 0.5));
                if (inside && !open) { start = new double2(ts[k], 0); open = true; }
                if (!inside && open) { result.Add(new float2((float)start.x, (float)ts[k])); open = false; }
            }
            if (open) result.Add(new float2((float)start.x, 1f));
            return result;
        }

        /// <summary>Pieces of segment ab inside the ring.</summary>
        public static List<(float2 a, float2 b)> ClipSegmentToPolygon(IReadOnlyList<float2> ring, float2 a, float2 b)
        {
            var result = new List<(float2, float2)>();
            foreach (var iv in ClipSegmentIntervals(ring, a, b))
                result.Add((math.lerp(a, b, iv.x), math.lerp(a, b, iv.y)));
            return result;
        }

        /// <summary>Pieces of segment ab inside the region (outside its holes).</summary>
        public static List<(float2 a, float2 b)> ClipSegmentToPolygon(Region region, float2 a, float2 b)
        {
            var result = new List<(float2, float2)>();
            // Even-odd over all rings = inside outer and outside holes.
            var ts = new List<float> { 0, 1 };
            foreach (var ring in region.Rings())
                foreach (var iv in ClipSegmentIntervals(ring, a, b)) { ts.Add(iv.x); ts.Add(iv.y); }
            ts.Sort();
            float start = 0; bool open = false;
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                if (ts[k + 1] - ts[k] < 1e-7f) continue;
                bool inside = region.Contains(math.lerp(a, b, (ts[k] + ts[k + 1]) * 0.5f));
                if (inside && !open) { start = ts[k]; open = true; }
                if (!inside && open) { result.Add((math.lerp(a, b, start), math.lerp(a, b, ts[k]))); open = false; }
            }
            if (open) result.Add((math.lerp(a, b, start), b));
            return result;
        }

        /// <summary>Pieces of an open polyline inside the ring, each as its own polyline.</summary>
        public static List<float2[]> ClipPolylineToPolygon(IReadOnlyList<float2> ring, IReadOnlyList<float2> polyline)
        {
            var result = new List<float2[]>();
            if (polyline == null || polyline.Count < 2) return result;
            List<float2> cur = null;
            for (int i = 0; i + 1 < polyline.Count; i++)
            {
                float2 a = polyline[i], b = polyline[i + 1];
                var ivs = ClipSegmentIntervals(ring, a, b);
                for (int k = 0; k < ivs.Count; k++)
                {
                    float2 p = math.lerp(a, b, ivs[k].x), q = math.lerp(a, b, ivs[k].y);
                    bool continues = cur != null && ivs[k].x <= 1e-6f && math.distancesq(cur[cur.Count - 1], p) < 1e-8f;
                    if (!continues)
                    {
                        if (cur != null && cur.Count >= 2) result.Add(cur.ToArray());
                        cur = new List<float2> { p };
                    }
                    cur.Add(q);
                    if (ivs[k].y < 1f - 1e-6f) { result.Add(cur.ToArray()); cur = null; }
                }
                if (ivs.Count == 0 && cur != null) { if (cur.Count >= 2) result.Add(cur.ToArray()); cur = null; }
            }
            if (cur != null && cur.Count >= 2) result.Add(cur.ToArray());
            return result;
        }

        // ───────────────────────────── holes ─────────────────────────────

        /// <summary>
        /// Opens every hole of a region with straight seams so the area is covered by hole-free
        /// simple CCW rings that share their seam edges exactly (no material lost). Each hole gets two
        /// horizontal seams (from its leftmost and rightmost crossing to the nearest boundary); if
        /// that fails to make progress a full line through the hole is used. corridorWidth &gt; 0
        /// instead removes a strip of that width along one seam (material lost, fewer pieces).
        /// Idea after ParkingLotTool's Lochtrennung (KruemelGames, GPL-3.0): open a ring along a
        /// shortest connection between hole and outer boundary.
        /// </summary>
        public static List<float2[]> CutHoles(Region region, float corridorWidth = 0f)
        {
            var result = new List<float2[]>();
            if (region == null || region.Outer == null || region.Outer.Length < 3) return result;
            var pending = new ListQueue<Region>();
            if (!region.HasHoles) { result.Add(EnsureCcw(region.Outer)); return result; }
            foreach (var r in Boolean(new[] { region }, null, BoolOp.Union)) pending.Enqueue(r);
            int guard = 64 + 8 * region.Holes.Count;
            while (pending.Count > 0 && guard-- > 0)
            {
                var r = pending.Dequeue();
                if (!r.HasHoles) { result.Add(r.Outer); continue; }
                var pieces = OpenOneHole(r, corridorWidth);
                if (pieces == null) continue; // could not be opened: dropped rather than emitted with a hole
                foreach (var p in pieces) pending.Enqueue(p);
            }
            result.Sort((a, b) => Area(b).CompareTo(Area(a)));
            return result;
        }

        private static List<Region> OpenOneHole(Region r, float corridor)
        {
            int holesBefore = r.Holes.Count;
            // Largest hole first: its seams are the most valuable.
            float2[] hole = r.Holes[0];
            foreach (var h in r.Holes) if (Area(h) > Area(hole)) hole = h;
            Bounds(hole, out var hmin, out var hmax);
            foreach (float frac in new[] { 0.5f, 0.37f, 0.63f, 0.21f, 0.79f })
            {
                float y = math.lerp(hmin.y, hmax.y, frac);
                y = AvoidVertices(r, y, (hmax.y - hmin.y) * 0.01f);
                if (!HoleSpanAt(hole, y, out float xl, out float xr)) continue;
                float2 pl = new float2(xl, y), pr = new float2(xr, y);
                // Seam = the horizontal chord between the nearest OUTER boundary crossings on both sides:
                // it separates the region even when it passes through other holes on the way.
                float2 hitL = RayHit(r.Outer, pl, new float2(-1, 0)), hitR = RayHit(r.Outer, pr, new float2(1, 0));
                List<Region> parts;
                if (corridor > 0)
                {
                    float2 a = hitL, b = pl;
                    if (math.distance(hitR, pr) < math.distance(hitL, pl)) { a = pr; b = hitR; }
                    var strip = StripAround(a, b, corridor);
                    parts = Boolean(new[] { r }, new[] { new Region(strip) }, BoolOp.Difference);
                }
                else parts = Split(r, new[] { hitL, hitR }, extend: 0.01f);
                if (HolesTotal(parts) < holesBefore) return parts;
                // Fallback: a line across the whole region at this height.
                Bounds(r.Outer, out var omin, out var omax);
                parts = Split(r, new[] { new float2(omin.x - 1, y), new float2(omax.x + 1, y) });
                if (HolesTotal(parts) < holesBefore) return parts;
            }
            return null;
        }

        private static int HolesTotal(List<Region> parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Holes.Count;
            return n;
        }

        private static float AvoidVertices(Region r, float y, float nudge)
        {
            nudge = math.max(nudge, 1e-3f);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                bool clash = false;
                foreach (var ring in r.Rings())
                    foreach (var p in ring)
                        if (math.abs(p.y - y) < 1e-4f) { clash = true; break; }
                if (!clash) return y;
                y += nudge * (attempt % 2 == 0 ? 1 : -2);
            }
            return y;
        }

        private static bool HoleSpanAt(float2[] hole, float y, out float xl, out float xr)
        {
            xl = float.MaxValue; xr = float.MinValue;
            int n = hole.Length;
            for (int i = 0; i < n; i++)
            {
                float2 a = hole[i], b = hole[(i + 1) % n];
                if ((a.y > y) == (b.y > y)) continue;
                float x = a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y);
                xl = math.min(xl, x); xr = math.max(xr, x);
            }
            return xl < xr;
        }

        private static float2 RayHit(float2[] ring, float2 from, float2 dir)
        {
            double best = double.MaxValue;
            double2 o = from, dd = dir;
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                double2 a = ring[i], s = (double2)ring[(i + 1) % n] - a;
                double den = CrossD(dd, s);
                if (den == 0) continue;
                double t = CrossD(a - o, s) / den, u = CrossD(a - o, dd) / den;
                if (u < 0 || u > 1 || t <= 1e-4) continue;
                if (t < best) best = t;
            }
            if (best == double.MaxValue) return from;
            return (float2)(o + dd * best);
        }

        private static float2[] StripAround(float2 a, float2 b, float width)
        {
            float2 dir = math.normalizesafe(b - a, new float2(1, 0)), n = new float2(-dir.y, dir.x) * (width * 0.5f);
            float2 a2 = a - dir * width, b2 = b + dir * width;
            return new[] { a2 - n, b2 - n, b2 + n, a2 + n };
        }
    }
}
