// A cleaned, counter-clockwise planning polygon with the cached queries layers need:
// containment, boundary clearance, perimeter walking, principal axis, deepest point, and
// boundary strips. Everything is 2D (x, z) — heights come from SiteContext.HeightAt.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public sealed class LayerRegion
    {
        public readonly float2[] Ring;
        public readonly float Area;
        public readonly float Perimeter;
        public readonly float2 Min, Max;
        private readonly SegmentIndex _edges;
        private readonly float[] _cumulative;   // perimeter length at the start of each edge

        private LayerRegion(float2[] ring)
        {
            Ring = ring;
            Area = Poly.Area(ring);
            Poly.Bounds(ring, out Min, out Max);
            float cell = math.clamp(math.cmax(Max - Min) / 24f, 4f, 40f);
            _edges = new SegmentIndex(cell);
            _cumulative = new float[ring.Length + 1];
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                _edges.Add(a, b, 0f, i);
                _cumulative[i + 1] = _cumulative[i] + math.distance(a, b);
            }
            Perimeter = _cumulative[ring.Length];
        }

        /// <summary>Builds a region from any ring; null when nothing valid remains (degenerate, self-intersecting).</summary>
        public static LayerRegion From(IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count < 3) return null;
            foreach (var p in ring) if (!math.all(math.isfinite(p))) return null;
            var clean = Poly.Clean(ring, 0.05f);
            if (clean.Length < 3 || !Poly.IsSimple(clean)) return null;
            return new LayerRegion(clean);
        }

        public static LayerRegion From(float3[] ring) => ring == null ? null : From(Poly.To2D(ring));

        public bool Contains(float2 p)
        {
            if (p.x < Min.x || p.y < Min.y || p.x > Max.x || p.y > Max.y) return false;
            if (_inside == null) BuildInsideGrid();
            int cx = math.clamp((int)((p.x - Min.x) / _insideCell.x), 0, _insideNx - 1);
            int cy = math.clamp((int)((p.y - Min.y) / _insideCell.y), 0, _insideNy - 1);
            byte state = _inside[cy * _insideNx + cx];
            if (state != 2) return state == 1;
            // (perf) Exactly Poly.Contains(Ring, p), looking only at the edges that cross p's row.
            return _bands.Contains(p);
        }

        private RingBands _bands;

        // Containment is the hottest query (every planting candidate, every raster cell). A coarse
        // grid classifies cells as fully outside (0), fully inside (1) or touching the outline (2);
        // only boundary cells pay for the exact O(n) even-odd test. Painted outlines have 100+ vertices.
        private byte[] _inside;
        private int _insideNx, _insideNy;
        private float2 _insideCell;

        private void BuildInsideGrid()
        {
            var size = math.max(Max - Min, new float2(0.01f));
            float cell = math.max(0.5f, math.sqrt(size.x * size.y / 4096f));
            int nx = math.clamp((int)math.ceil(size.x / cell), 1, 256), ny = math.clamp((int)math.ceil(size.y / cell), 1, 256);
            var cs = size / new float2(nx, ny);
            var grid = new byte[nx * ny];
            float halfDiag = math.length(cs) * 0.5f + 1e-3f;
            for (int i = 0; i < Ring.Length; i++)
            {
                var a = Ring[i]; var b = Ring[(i + 1) % Ring.Length];
                int x0 = math.clamp((int)((math.min(a.x, b.x) - Min.x) / cs.x) - 1, 0, nx - 1);
                int x1 = math.clamp((int)((math.max(a.x, b.x) - Min.x) / cs.x) + 1, 0, nx - 1);
                int y0 = math.clamp((int)((math.min(a.y, b.y) - Min.y) / cs.y) - 1, 0, ny - 1);
                int y1 = math.clamp((int)((math.max(a.y, b.y) - Min.y) / cs.y) + 1, 0, ny - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var c = Min + (new float2(x, y) + 0.5f) * cs;
                        if (SegmentIndex.DistSq(c, a, b) <= halfDiag * halfDiag) grid[y * nx + x] = 2;
                    }
            }
            var crossings = new List<float>();
            for (int y = 0; y < ny; y++)
            {
                float py = Min.y + (y + 0.5f) * cs.y;
                crossings.Clear();
                for (int i = 0, j = Ring.Length - 1; i < Ring.Length; j = i++)
                {
                    var a = Ring[i]; var b = Ring[j];
                    if ((a.y > py) != (b.y > py)) crossings.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                crossings.Sort();
                int k = 0;
                for (int x = 0; x < nx; x++)
                {
                    float px = Min.x + (x + 0.5f) * cs.x;
                    while (k < crossings.Count && crossings[k] <= px) k++;
                    if (grid[y * nx + x] != 2) grid[y * nx + x] = (byte)((k & 1) == 1 ? 1 : 0);
                }
            }
            _insideNx = nx; _insideNy = ny; _insideCell = cs;
            _bands = new RingBands(Ring);
            System.Threading.Volatile.Write(ref _inside, grid);
        }

        /// <summary>
        /// True when segment a→b crosses the outline (proper crossings only). With
        /// <paramref name="allowTouchAtA"/>, crossings at a (an entrance on the outline) are ignored.
        /// </summary>
        public bool CrossesBoundary(float2 a, float2 b, bool allowTouchAtA)
        {
            const double eps = 0.0001;
            var lo = math.min(a, b); var hi = math.max(a, b);
            // (perf) Only the edges registered along a→b: an edge that crosses it touches a cell the line passes through.
            _edges.Along(a, b, _along);
            foreach (int i in _along)
            {
                var c = Ring[i]; var d = Ring[(i + 1) % Ring.Length];
                if (math.max(c.x, d.x) < lo.x || math.min(c.x, d.x) > hi.x || math.max(c.y, d.y) < lo.y || math.min(c.y, d.y) > hi.y) continue;
                double abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
                bool proper = ((abC > eps && abD < -eps) || (abC < -eps && abD > eps)) && ((cdA > eps && cdB < -eps) || (cdA < -eps && cdB > eps));
                if (!proper) continue;
                if (allowTouchAtA && SegmentIndex.DistSq(a, c, d) < 0.0025f) continue;
                return true;
            }
            return false;
        }

        private readonly List<int> _along = new List<int>();

        private static double Cross(float2 a, float2 b, float2 c) => ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        /// <summary>Distance to the outline, capped (cheap for small caps).</summary>
        public float BoundaryDistance(float2 p, float cap = float.MaxValue)
        {
            if (cap == float.MaxValue) cap = math.cmax(Max - Min) + 1f;
            return _edges.Clearance(p, cap);
        }

        /// <summary>Inside and at least <paramref name="margin"/> from the outline.</summary>
        public bool ContainsDisc(float2 p, float margin) =>
            Contains(p) && (margin <= 0f || !_edges.AnyBelow(p, margin + 0.01f, margin));

        public float2 Centroid => Poly.Centroid(Ring);

        /// <summary>Point at arc length <paramref name="along"/> (wraps), with the edge's unit tangent and inward normal.</summary>
        public float2 PointAt(float along, out float2 tangent, out float2 inward, out int segment)
        {
            along = Perimeter > 0f ? ((along % Perimeter) + Perimeter) % Perimeter : 0f;
            int lo = 0, hi = Ring.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_cumulative[mid] <= along) lo = mid; else hi = mid - 1;
            }
            segment = lo;
            var a = Ring[lo]; var b = Ring[(lo + 1) % Ring.Length];
            float len = math.max(1e-5f, _cumulative[lo + 1] - _cumulative[lo]);
            tangent = (b - a) / len;
            inward = new float2(-tangent.y, tangent.x);   // CCW ring: interior on the left
            return a + tangent * math.min(len, along - _cumulative[lo]);
        }

        public float AlongOf(int segment, float t) =>
            _cumulative[segment] + (_cumulative[segment + 1] - _cumulative[segment]) * math.saturate(t);

        /// <summary>Closest outline point: segment, parameter and distance.</summary>
        public float2 ClosestOnBoundary(float2 p, out int segment, out float t)
        {
            segment = 0; t = 0; float best = float.MaxValue; float2 bestP = Ring[0];
            for (int i = 0; i < Ring.Length; i++)
            {
                var a = Ring[i]; var b = Ring[(i + 1) % Ring.Length];
                var ab = b - a; float len = math.lengthsq(ab);
                float tt = len > 1e-9f ? math.saturate(math.dot(p - a, ab) / len) : 0f;
                var q = a + ab * tt;
                float d = math.distancesq(p, q);
                if (d < best) { best = d; segment = i; t = tt; bestP = q; }
            }
            return bestP;
        }

        /// <summary>Unit inward normal of a ring edge.</summary>
        public float2 InwardNormal(int segment)
        {
            var a = Ring[segment]; var b = Ring[(segment + 1) % Ring.Length];
            var d = math.normalizesafe(b - a, new float2(1, 0));
            return new float2(-d.y, d.x);
        }

        /// <summary>Principal axis through the area centroid (long axis first) and the spans along both axes.</summary>
        /// <summary>
        /// The outline's dominant edge direction modulo 90° (radians), from the length-weighted mean of the
        /// edge angles ×4. <paramref name="coherence"/> is 1 for a rectilinear outline, ~0 for a round one.
        /// </summary>
        public bool DominantEdgeAngle(out float angle, out float coherence)
        {
            double c4 = 0, s4 = 0, total = 0;
            for (int i = 0; i < Ring.Length; i++)
            {
                var d = Ring[(i + 1) % Ring.Length] - Ring[i];
                double len = Math.Sqrt(d.x * d.x + d.y * d.y);
                if (len < 1e-4) continue;
                double th = Math.Atan2(d.y, d.x) * 4;
                c4 += len * Math.Cos(th); s4 += len * Math.Sin(th); total += len;
            }
            angle = 0f; coherence = 0f;
            if (total <= 0) return false;
            coherence = (float)(Math.Sqrt(c4 * c4 + s4 * s4) / total);
            angle = (float)(Math.Atan2(s4, c4) / 4);
            return true;
        }

        public void PrincipalAxes(out float2 center, out float2 major, out float majorSpan, out float minorSpan)
        {
            center = Centroid;
            // Second moments of the boundary samples; vertex-only moments are biased by dense
            // vertex runs (painted outlines), so sample the perimeter evenly instead.
            int samples = math.clamp((int)(Perimeter / 2f), 16, 512);
            double xx = 0, xy = 0, yy = 0;
            for (int i = 0; i < samples; i++)
            {
                var p = PointAt(Perimeter * i / samples, out _, out _, out _) - center;
                xx += p.x * p.x; xy += p.x * p.y; yy += p.y * p.y;
            }
            float angle = 0.5f * (float)Math.Atan2(2 * xy, xx - yy);
            // s9: square-ish outlines have no principal axis (the moments are isotropic, so the angle is
            // float noise — formal axes came out at 45°). Snap to the outline's dominant edge direction
            // when the moments are nearly isotropic or already close to it.
            double tr = xx + yy;
            double aniso = tr > 1e-9 ? Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy) / tr : 0;
            if (DominantEdgeAngle(out float edgeAngle, out float coherence) && coherence > 0.35f)
            {
                // Nearest of the two edge directions (mod 180°) to the moment axis.
                float best = edgeAngle, bestDiff = float.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    float cand = edgeAngle + k * math.PI * 0.5f;
                    float diff = math.abs(math.atan2(math.sin(2f * (cand - angle)), math.cos(2f * (cand - angle)))) * 0.5f;
                    if (diff < bestDiff) { bestDiff = diff; best = cand; }
                }
                if (aniso < 0.12 || bestDiff < math.radians(20f)) angle = best;
            }
            major = new float2(math.cos(angle), math.sin(angle));
            var minor = new float2(-major.y, major.x);
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            foreach (var v in Ring)
            {
                var d = v - center;
                float a = math.dot(d, major), b = math.dot(d, minor);
                minA = math.min(minA, a); maxA = math.max(maxA, a);
                minB = math.min(minB, b); maxB = math.max(maxB, b);
            }
            majorSpan = maxA - minA; minorSpan = maxB - minB;
            if (minorSpan > majorSpan)
            {
                major = minor;
                var s = majorSpan; majorSpan = minorSpan; minorSpan = s;
            }
        }

        /// <summary>
        /// Approximate pole of inaccessibility: the interior point farthest from the outline
        /// (and from anything <paramref name="extraClearance"/> reports). Coarse grid + two refinements.
        /// </summary>
        public float2 DeepestPoint(out float clearance, Func<float2, float, float> extraClearance = null, int grid = 24)
        {
            float2 best = Centroid; clearance = -1f;
            float2 lo = Min, hi = Max;
            float2 step = (hi - lo) / grid;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y <= grid; y++)
                    for (int x = 0; x <= grid; x++)
                    {
                        var p = lo + step * new float2(x, y);
                        if (!Contains(p)) continue;
                        float c = BoundaryDistance(p);
                        if (extraClearance != null) c = math.min(c, extraClearance(p, c));
                        if (c > clearance) { clearance = c; best = p; }
                    }
                // Zoom into the best cell's neighbourhood.
                var half = step * 1.5f;
                lo = best - half; hi = best + half; step = (hi - lo) / grid;
            }
            if (clearance < 0f)
            {
                // Pathological slivers: fall back to the midpoint of the longest edge, nudged inside.
                int longest = 0; float len = 0;
                for (int i = 0; i < Ring.Length; i++)
                {
                    float l = math.distance(Ring[i], Ring[(i + 1) % Ring.Length]);
                    if (l > len) { len = l; longest = i; }
                }
                best = math.lerp(Ring[longest], Ring[(longest + 1) % Ring.Length], 0.5f) + InwardNormal(longest) * 0.05f;
                clearance = 0f;
            }
            return best;
        }

        /// <summary>
        /// (rf-squares) A robust inward offset of the whole outline for loops along it (tree rings, promenades): the
        /// largest ring of a true polygon offset (Poly.Offset), CCW and simple, or null when nothing is left. Unlike
        /// <see cref="InsetVertices"/> it survives short edges: a bucket-filled city block has chamfered corners a few
        /// metres long, which a per-vertex miter folds over itself (the plaza and market tree rings then vanished).
        /// </summary>
        public float2[] InsetRing(float distance)
        {
            float2[] best = null;
            float bestArea = 0f;
            foreach (var r in Poly.Offset(Ring, distance))
            {
                if (r == null || r.Length < 3) continue;
                float a = math.abs(Poly.SignedArea(r));
                if (a > bestArea) { bestArea = a; best = r; }
            }
            return best != null && Poly.SignedArea(best) < 0f ? Poly.EnsureCcw(best) : best;
        }

        /// <summary>
        /// Mitred inward offset of every vertex (index-aligned with Ring). Reflex corners use the
        /// bisector; very sharp convex corners are clamped so the miter cannot shoot across the shape.
        /// </summary>
        public float2[] InsetVertices(float distance, float miterLimit = 3f)
        {
            int n = Ring.Length;
            var r = new float2[n];
            for (int i = 0; i < n; i++)
            {
                float2 a = Ring[(i - 1 + n) % n], b = Ring[i], c = Ring[(i + 1) % n];
                var n1 = math.normalizesafe(new float2(-(b - a).y, (b - a).x));
                var n2 = math.normalizesafe(new float2(-(c - b).y, (c - b).x));
                var bis = math.normalizesafe(n1 + n2, n2);
                float cos = math.max(1f / miterLimit, math.dot(bis, n2));
                r[i] = b + bis * (distance / cos);
            }
            return r;
        }

        /// <summary>
        /// Splits the boundary belt of the given depth into a few C-shaped strips (areas cannot have
        /// holes, so a closed ring band is never emitted). Strips that would self-intersect at tight
        /// corners are subdivided, and dropped only at single-edge level.
        /// </summary>
        public List<float2[]> BoundaryStrips(float depth, int pieces, System.Threading.CancellationToken cancel = default)
        {
            var result = new List<float2[]>();
            int n = Ring.Length;
            if (n < 3 || depth <= 0.2f) return result;
            var inset = InsetVertices(depth, 2.5f);
            // The mitred inset is only trustworthy when it is itself simple and every vertex really lies
            // about `depth` inside. Jagged painted outlines fail this; they get the raster belt instead
            // (testing strips piece by piece on them cost O(n²) per piece and produced hundreds of slivers).
            bool geometric = Poly.SignedArea(inset) > 0f && Poly.IsSimple(inset);
            for (int i = 0; i < n && geometric; i++)
                geometric = Contains(inset[i]) && BoundaryDistance(inset[i], depth) >= depth * 0.9f;
            if (!geometric) return RasterStrips(depth, cancel);
            pieces = math.clamp(pieces, 2, n);
            // Break points at vertices closest to equal perimeter fractions.
            var breaks = new List<int>();
            for (int k = 0; k < pieces; k++)
            {
                float target = Perimeter * k / pieces;
                int bestV = 0; float bestD = float.MaxValue;
                for (int v = 0; v < n; v++)
                {
                    float d = math.abs(_cumulative[v] - target);
                    if (d < bestD) { bestD = d; bestV = v; }
                }
                if (!breaks.Contains(bestV)) breaks.Add(bestV);
            }
            breaks.Sort();
            if (breaks.Count < 2) { breaks.Clear(); breaks.Add(0); breaks.Add(n / 2); }
            for (int k = 0; k < breaks.Count; k++)
            {
                int from = breaks[k], to = breaks[(k + 1) % breaks.Count];
                if (to <= from) to += n;
                AddStrip(result, inset, from, to);
            }
            return result.Count <= pieces * 2 ? result : RasterStrips(depth, cancel);
        }

        /// <summary>Belt as raster pieces (hole-free, so a closed band comes back as one C shape).</summary>
        private List<float2[]> RasterStrips(float depth, System.Threading.CancellationToken cancel)
        {
            float cell = math.clamp(depth / 5f, 0.5f, 2f);
            return FreeSpace.Pieces(this, p => BoundaryDistance(p, depth + 0.01f) >= depth, cell, depth * depth, cancel);
        }

        private void AddStrip(List<float2[]> into, float2[] inset, int from, int to)
        {
            int n = Ring.Length;
            var pts = new List<float2>();
            for (int i = from; i <= to; i++) pts.Add(Ring[i % n]);
            for (int i = to; i >= from; i--) pts.Add(inset[i % n]);
            var clean = Poly.Clean(pts, 0.05f);
            bool ok = clean.Length >= 3 && Poly.IsSimple(clean);
            if (ok)
            {
                // The inner side must stay inside the region, otherwise the strip leaks outside.
                for (int i = from; i <= to && ok; i++)
                    ok = Contains(inset[i % n]);
            }
            if (ok) { into.Add(clean); return; }
            if (to - from <= 1) return;
            int mid = (from + to) / 2;
            AddStrip(into, inset, from, mid);
            AddStrip(into, inset, mid, to);
        }

        /// <summary>Regular polygon approximating a circle, CCW.</summary>
        public static float2[] Circle(float2 c, float r, int sides = 24, float phase = 0f)
        {
            var pts = new float2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = phase + i * 2f * math.PI / sides;
                pts[i] = c + new float2(math.cos(a), math.sin(a)) * r;
            }
            return pts;
        }

        /// <summary>Oriented rectangle, CCW.</summary>
        public static float2[] Rect(float2 c, float2 axis, float halfLength, float halfWidth)
        {
            var u = math.normalizesafe(axis, new float2(1, 0)) * halfLength;
            var v = new float2(-u.y, u.x) / math.max(1e-5f, halfLength) * halfWidth;
            return new[] { c - u - v, c + u - v, c + u + v, c - u + v };
        }
    }
}
