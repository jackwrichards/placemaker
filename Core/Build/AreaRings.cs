// Placemaker — the game's own area shape rules, so we never hand CS2 a ring it rejects (GPL-3.0).
//
// Decompiled from Game.Areas.ValidationHelpers.ValidateArea (CS2 1.6.2f1), see
// docs/RESEARCH-NOTES-ADDENDUM.md. A complete ring is rejected with ErrorType.InvalidShape when
//   (1) for an edge (a,b), the next node c or the previous node z lies closer than d to the edge
//       (unconditional) → every edge must be ≥ d long, and an acute corner fails when the shorter
//       adjacent edge × sin(angle) < d;
//   (2) two non-adjacent edges come closer than d (plus a quad test we model conservatively);
//   (3) the ring cannot be triangulated (AreaFlags.NoTriangles).
// d = AreaGeometryData.m_SnapDistance × 0.5, and AreaInitializeSystem sets m_SnapDistance from the
// area TYPE: Lot 8, District 32, MapTile 64, Space 1, Surface 0.75 → d = 4 / 16 / 32 / 0.5 / 0.375 m.
// A closing duplicate of the first node is stripped by GenerateAreasSystem (sets AreaFlags.Complete).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class AreaRings
    {
        /// <summary>Game min node distance for surfaces (snap 0.75 × 0.5).</summary>
        public const float SurfaceMinNodeDistance = 0.375f;
        /// <summary>Space areas (pedestrian/hangaround): snap 1 × 0.5.</summary>
        public const float SpaceMinNodeDistance = 0.5f;
        /// <summary>Lots: snap 8 × 0.5.</summary>
        public const float LotMinNodeDistance = 4f;
        /// <summary>Safety margin on top of the game's distance (float noise, terrain-projected nodes).</summary>
        public const float Margin = 0.05f;

        /// <summary>Game rule d for a prefab's AreaGeometryData.m_SnapDistance.</summary>
        public static float MinNodeDistanceForSnap(float snapDistance) => snapDistance * 0.5f;

        /// <summary>
        /// Index of the first node involved in an InvalidShape violation, or -1 when the ring passes
        /// rules (1) and (2). Conservative: rule (2) is applied without the game's extra quad test.
        /// </summary>
        public static int FirstViolation(IReadOnlyList<float2> ring, float minNodeDistance)
        {
            int n = ring?.Count ?? 0;
            if (n < 3) return 0;
            // (perf) Rule 2 asks every pair of edges: quadratic in the nodes (a painted parking lot's rings have up to 240,
            // and MakeValid asks again after every node it removes). Larger rings find the edge pairs closer than d by grid
            // cell instead; the answer (the same node) does not change.
            return n > 24 ? FirstViolationGrid(ring, minNodeDistance) : FirstViolationAllPairs(ring, minNodeDistance);
        }

        /// <summary>FirstViolation by checking every pair of edges (the reference; small rings).</summary>
        internal static int FirstViolationAllPairs(IReadOnlyList<float2> ring, float minNodeDistance)
        {
            int n = ring?.Count ?? 0;
            if (n < 3) return 0;
            float d = minNodeDistance;
            for (int i = 0; i < n; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % n];
                float2 next = ring[(i + 2) % n], prev = ring[(i - 1 + n) % n];
                // Rule 1: the nodes right before and after the edge.
                {
                    if (DistancePointSegment(next, a, b) < d) return (i + 2) % n;
                    if (DistancePointSegment(prev, a, b) < d) return (i - 1 + n) % n;
                }
                // Rule 2: non-adjacent edges.
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;   // adjacent through the closing edge
                    float2 c = ring[j], e = ring[(j + 1) % n];
                    if (DistanceSegmentSegment(a, b, c, e) < d) return j;
                }
            }
            return -1;
        }

        /// <summary>
        /// FirstViolation with the edges filed by grid cell (cells at least d wide): for edge i, only the edges whose box
        /// comes within d of its box can break rule 2, and the smallest such j is the one the all-pairs scan returns first.
        /// </summary>
        internal static int FirstViolationGrid(IReadOnlyList<float2> ring, float minNodeDistance)
        {
            int n = ring.Count;
            float d = minNodeDistance;
            float2 mn = ring[0], mx = ring[0];
            for (int i = 1; i < n; i++) { mn = math.min(mn, ring[i]); mx = math.max(mx, ring[i]); }
            float pad = d + 1e-3f;
            // about as many cells as nodes (small tables: MakeValid asks after every node it removes)
            float span = math.max(mx.x - mn.x, mx.y - mn.y);
            float cell = math.max(math.max(2f * pad, 0.5f), math.max(span / 128f, math.sqrt(math.max(1e-6f, (mx.x - mn.x) * (mx.y - mn.y)) / n)));
            int nx = (int)((mx.x - mn.x) / cell) + 1, ny = (int)((mx.y - mn.y) / cell) + 1;
            int Cx(float x) => math.clamp((int)math.floor((x - mn.x) / cell), 0, nx - 1);
            int Cy(float y) => math.clamp((int)math.floor((y - mn.y) / cell), 0, ny - 1);
            // edges by every cell their box touches (flat tables: start[c] .. start[c + 1] in items)
            var start = new int[nx * ny + 1];
            for (int e = 0; e < n; e++)
            {
                float2 a = ring[e], b = ring[(e + 1) % n];
                for (int y = Cy(math.min(a.y, b.y)); y <= Cy(math.max(a.y, b.y)); y++)
                    for (int x = Cx(math.min(a.x, b.x)); x <= Cx(math.max(a.x, b.x)); x++) start[x + y * nx + 1]++;
            }
            for (int c = 1; c < start.Length; c++) start[c] += start[c - 1];
            var fill = (int[])start.Clone();
            var items = new int[start[start.Length - 1]];
            for (int e = 0; e < n; e++)
            {
                float2 a = ring[e], b = ring[(e + 1) % n];
                for (int y = Cy(math.min(a.y, b.y)); y <= Cy(math.max(a.y, b.y)); y++)
                    for (int x = Cx(math.min(a.x, b.x)); x <= Cx(math.max(a.x, b.x)); x++) items[fill[x + y * nx]++] = e;
            }
            for (int i = 0; i < n; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % n];
                float2 next = ring[(i + 2) % n], prev = ring[(i - 1 + n) % n];
                // Rule 1: the nodes right before and after the edge.
                if (DistancePointSegment(next, a, b) < d) return (i + 2) % n;
                if (DistancePointSegment(prev, a, b) < d) return (i - 1 + n) % n;
                // Rule 2: the first non-adjacent edge j > i + 1 closer than d.
                int best = int.MaxValue;
                int x0 = Cx(math.min(a.x, b.x) - pad), x1 = Cx(math.max(a.x, b.x) + pad);
                int y0 = Cy(math.min(a.y, b.y) - pad), y1 = Cy(math.max(a.y, b.y) + pad);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int c = x + y * nx;
                        for (int k = start[c]; k < start[c + 1]; k++)
                        {
                            int j = items[k];
                            if (j < i + 2 || j >= best || (i == 0 && j == n - 1)) continue;
                            if (DistanceSegmentSegment(a, b, ring[j], ring[(j + 1) % n]) < d) best = j;
                        }
                    }
                if (best != int.MaxValue) return best;
            }
            return -1;
        }

        public static bool IsValid(IReadOnlyList<float2> ring, float minNodeDistance) => FirstViolation(ring, minNodeDistance) < 0;

        /// <summary>
        /// Repairs a ring for the game's shape rules by removing offending nodes (never adds area),
        /// after dropping near-duplicate and collinear nodes. Returns null when fewer than 3 nodes
        /// survive or the result is not simple. The input orientation is kept.
        /// </summary>
        public static float2[] MakeValid(IReadOnlyList<float2> ring, float minNodeDistance)
        {
            if (ring == null || ring.Count < 3) return null;
            float d = minNodeDistance + Margin;
            var pts = new List<float2>(ring.Count);
            foreach (var p in ring)
            {
                if (!math.all(math.isfinite(p))) return null;
                if (pts.Count > 0 && math.distance(pts[pts.Count - 1], p) < d) continue;
                pts.Add(p);
            }
            while (pts.Count > 3 && math.distance(pts[0], pts[pts.Count - 1]) < d) pts.RemoveAt(pts.Count - 1);
            RemoveCollinear(pts, 1e-3f);
            int guard = pts.Count * 2 + 8;
            while (pts.Count >= 3 && guard-- > 0)
            {
                int bad = FirstViolation(pts, d);
                if (bad < 0) break;
                pts.RemoveAt(bad);
                RemoveCollinear(pts, 1e-3f);
            }
            if (pts.Count < 3 || FirstViolation(pts, d) >= 0) return null;
            var result = pts.ToArray();
            if (!Poly.IsSimple(result) || Poly.Area(result) < d * d) return null;
            return result;
        }

        /// <summary>
        /// Surface ring for the game: shape rules (d = 0.375 m) AND triangulation acceptance
        /// (SurfaceAcceptance). Returns one or more rings (a split may be needed) — empty when nothing
        /// sensible remains.
        /// </summary>
        public static List<float2[]> PrepareSurface(IReadOnlyList<float2> ring, float minNodeDistance)
        {
            var result = new List<float2[]>();
            if (ring == null || ring.Count < 3) return result;
            var direct = MakeValid(ring, minNodeDistance);
            if (direct != null && SurfaceAcceptance.IsAccepted(direct) && Poly.Area(direct) >= Poly.Area(ring) * 0.9f)
            {
                result.Add(direct);
                return result;
            }
            List<float2[]> pieces;
            try { pieces = SurfaceAcceptance.MakeAcceptable2D(ring); }
            catch (Exception) { pieces = new List<float2[]>(); }
            foreach (var piece in pieces)
            {
                var v = MakeValid(piece, minNodeDistance);
                if (v != null && SurfaceAcceptance.IsAccepted(v)) result.Add(v);
            }
            if (result.Count == 0 && direct != null && SurfaceAcceptance.IsAccepted(direct)) result.Add(direct);
            return result;
        }

        public static float2[] ConvexHull(IReadOnlyList<float2> points)
        {
            var pts = new List<float2>();
            foreach (var p in points) if (math.all(math.isfinite(p))) pts.Add(p);
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (pts.Count < 3) return pts.ToArray();
            var hull = new float2[pts.Count * 2];
            int k = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                while (k >= 2 && Poly.Cross(hull[k - 1] - hull[k - 2], pts[i] - hull[k - 2]) <= 0) k--;
                hull[k++] = pts[i];
            }
            for (int i = pts.Count - 2, t = k + 1; i >= 0; i--)
            {
                while (k >= t && Poly.Cross(hull[k - 1] - hull[k - 2], pts[i] - hull[k - 2]) <= 0) k--;
                hull[k++] = pts[i];
            }
            var r = new float2[math.max(0, k - 1)];
            Array.Copy(hull, r, r.Length);
            return r;   // CCW
        }

        private static void RemoveCollinear(List<float2> pts, float tolerance)
        {
            bool changed = true;
            while (changed && pts.Count > 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count > 3; i++)
                {
                    float2 a = pts[(i - 1 + pts.Count) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                    float2 ab = b - a, bc = c - b;
                    float la = math.length(ab), lc = math.length(bc);
                    if (la < 1e-6f || lc < 1e-6f) { pts.RemoveAt(i); changed = true; break; }
                    // Straight continuation only (a reversal is a spike, handled by the shape rules).
                    if (math.abs(Poly.Cross(ab, bc)) / (la * lc) < tolerance && math.dot(ab, bc) > 0) { pts.RemoveAt(i); changed = true; break; }
                }
            }
        }

        public static float DistancePointSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.lengthsq(ab);
            float t = len2 > 1e-12f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            return math.distance(p, a + ab * t);
        }

        public static float DistanceSegmentSegment(float2 a, float2 b, float2 c, float2 d)
        {
            if (Poly.SegmentsIntersect(a, b, c, d)) return 0f;
            return math.min(math.min(DistancePointSegment(a, c, d), DistancePointSegment(b, c, d)),
                            math.min(DistancePointSegment(c, a, b), DistancePointSegment(d, a, b)));
        }
    }
}
