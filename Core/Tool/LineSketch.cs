// Sketch lines: open polylines the player draws on the world (P2-1; docs/AREA-TOOLS.md, "Sketch lines"). The outline
// tools only make closed areas (PolygonEditor); a sketch line says "an aisle goes here", "a walkway runs here". It is saved
// as a hand edit (Core/Touches.cs): the kind is the layer's ("line.aisle"), Position the first point, Arg the points, so it
// survives Edit, rebuilds and reloads like every hand edit. Each layer decides what a line means (parking: a drive aisle or
// a walkway through the rows).
//
// Snapping, in order (Shift = none): onto a point within reach (the line's own points, the outline's corners, the ends of
// the plan's paths and roads); onto a segment within reach (the outline's sides, the plan's paths and roads); the direction
// to parallel or square with the nearest plan line or outline side within a few degrees, else to 15° steps. Pure.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    /// <summary>What a sketch point snaps to (world XZ). The tool fills it from the outline and the latest plan.</summary>
    public sealed class SketchSnaps
    {
        public readonly List<float2> Points = new List<float2>();
        public readonly List<(float2 A, float2 B)> Segments = new List<(float2, float2)>();
        /// <summary>How far a snap reaches (metres; the tool scales it with the camera).</summary>
        public float Reach = 2.5f;

        public void AddRing(IReadOnlyList<float2> ring)
        {
            if (ring == null) return;
            for (int i = 0; i < ring.Count; i++)
            {
                Points.Add(ring[i]);
                Segments.Add((ring[i], ring[(i + 1) % ring.Count]));
            }
        }

        public void AddSegment(float2 a, float2 b)
        {
            if (math.distance(a, b) < 0.1f) return;
            Points.Add(a); Points.Add(b);
            Segments.Add((a, b));
        }
    }

    public enum SketchSnap { None = 0, Point = 1, Segment = 2, Angle = 3 }

    /// <summary>The line being drawn: its points so far.</summary>
    public sealed class LineSketch
    {
        public const int MaxPoints = 64;
        /// <summary>Shorter steps are not added (a double click on the last point finishes instead).</summary>
        public const float MinStep = 1f;
        private const float AngleSnapDeg = 6f;

        private readonly List<float3> _points = new List<float3>();
        public IReadOnlyList<float3> Points => _points;
        public bool Drawing => _points.Count > 0;
        public bool CanFinish => _points.Count >= 2;

        public void Clear() => _points.Clear();

        /// <summary>Adds a point; false when it is the last one again (the caller may finish) or the line is full.</summary>
        public bool Add(float3 p)
        {
            if (_points.Count >= MaxPoints) return false;
            if (_points.Count > 0 && math.distance(_points[_points.Count - 1].xz, p.xz) < MinStep) return false;
            _points.Add(p);
            return true;
        }

        public bool RemoveLast()
        {
            if (_points.Count == 0) return false;
            _points.RemoveAt(_points.Count - 1);
            return true;
        }

        /// <summary>The finished line as a hand edit of <paramref name="kind"/>; null when it has fewer than two points.</summary>
        public HandTouch? Finish(string kind)
        {
            if (_points.Count < 2) return null;
            var pts = new float2[_points.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = _points[i].xz;
            var t = new HandTouch(kind, _points[0], float.NaN, Encode(pts));
            _points.Clear();
            return t;
        }

        /// <summary>
        /// Where a click at <paramref name="cursor"/> puts the next point (see the file head). <paramref name="free"/> (Shift): no snapping.
        /// </summary>
        public float2 Snap(float2 cursor, SketchSnaps snaps, bool free, out SketchSnap how)
        {
            how = SketchSnap.None;
            if (free || snaps == null) return cursor;
            float reach = math.max(0.5f, snaps.Reach);
            // 1. a point: our own first, then the outline's corners and the plan's ends
            float best = reach;
            float2 hit = cursor;
            foreach (var p in _points)
            {
                float d = math.distance(p.xz, cursor);
                if (d < best) { best = d; hit = p.xz; how = SketchSnap.Point; }
            }
            foreach (var p in snaps.Points)
            {
                float d = math.distance(p, cursor);
                if (d < best) { best = d; hit = p; how = SketchSnap.Point; }
            }
            if (how == SketchSnap.Point) return hit;
            // 2. the direction from the last point: parallel or square with the nearest line, else 15° steps
            float2 dirSnapped = cursor;
            bool angled = false;
            if (_points.Count > 0)
            {
                var last = _points[_points.Count - 1].xz;
                var v = cursor - last;
                float len = math.length(v);
                if (len > MinStep)
                {
                    float ang = math.atan2(v.y, v.x);
                    float bestDiff = math.radians(AngleSnapDeg);
                    float target = float.NaN;
                    float nearest = float.MaxValue;
                    foreach (var s in snaps.Segments)
                    {
                        float d = DistPointSeg(last, s.A, s.B);
                        if (d > 40f || d > nearest + 1e-3f) continue;
                        var sd = s.B - s.A;
                        if (math.lengthsq(sd) < 1e-4f) continue;
                        float sa = math.atan2(sd.y, sd.x);
                        for (int q = 0; q < 4; q++)
                        {
                            float cand = sa + q * math.PI / 2;
                            float diff = math.abs(Wrap(ang - cand));
                            if (diff < bestDiff || d < nearest - 1e-3f && diff < math.radians(AngleSnapDeg)) { bestDiff = diff; target = cand; nearest = d; }
                        }
                    }
                    if (float.IsNaN(target))
                    {
                        float step = math.radians(15f);
                        target = math.round(ang / step) * step;
                    }
                    dirSnapped = last + new float2(math.cos(target), math.sin(target)) * len;
                    angled = true;
                }
            }
            // 3. a segment: onto the nearest within reach (along the snapped direction when it crosses one nearby)
            var probe = angled ? dirSnapped : cursor;
            best = reach;
            bool onSeg = false;
            foreach (var s in snaps.Segments)
            {
                var foot = Closest(probe, s.A, s.B);
                float d = math.distance(foot, probe);
                if (d < best) { best = d; hit = foot; onSeg = true; }
            }
            if (onSeg && angled && _points.Count > 0)
            {
                // keep the snapped direction: where it meets that segment, when that is near
                var last = _points[_points.Count - 1].xz;
                foreach (var s in snaps.Segments)
                    if (LineHit(last, dirSnapped, s.A, s.B, out var x) && math.distance(x, dirSnapped) < reach) { hit = x; break; }
            }
            if (onSeg) { how = SketchSnap.Segment; return hit; }
            if (angled) { how = SketchSnap.Angle; return dirSnapped; }
            return cursor;
        }

        // ───────────── saved form ─────────────

        /// <summary>"x,z;x,z;…" (metres, 2 decimals, invariant).</summary>
        public static string Encode(IReadOnlyList<float2> pts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(pts[i].x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append(pts[i].y.ToString("0.##", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>The points of a saved line; empty when it is not one (fewer than two points or not numbers).</summary>
        public static float2[] Decode(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return Array.Empty<float2>();
            var parts = arg.Split(';');
            var r = new List<float2>(parts.Length);
            foreach (var p in parts)
            {
                var xy = p.Split(',');
                if (xy.Length != 2
                    || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                    || !math.isfinite(x) || !math.isfinite(z)) return Array.Empty<float2>();
                r.Add(new float2(x, z));
            }
            return r.Count >= 2 ? r.ToArray() : Array.Empty<float2>();
        }

        /// <summary>Distance from <paramref name="p"/> to a saved line (float.MaxValue when it has no points).</summary>
        public static float Distance(IReadOnlyList<float2> line, float2 p)
        {
            float best = float.MaxValue;
            if (line == null) return best;
            for (int i = 0; i + 1 < line.Count; i++) best = math.min(best, DistPointSeg(p, line[i], line[i + 1]));
            return best;
        }

        /// <summary>Index of the saved line of a sketch kind (<paramref name="isLine"/>) nearest <paramref name="p"/> within reach, or -1.</summary>
        public static int Hit(IReadOnlyList<HandTouch> touches, float2 p, float reach, Func<string, bool> isLine)
        {
            int found = -1;
            float best = reach;
            if (touches == null) return -1;
            for (int i = 0; i < touches.Count; i++)
            {
                if (isLine != null && !isLine(touches[i].Kind)) continue;
                float d = Distance(Decode(touches[i].Arg), p);
                if (d < best) { best = d; found = i; }
            }
            return found;
        }

        // ───────────── geometry ─────────────

        private static float Wrap(float a)
        {
            while (a > math.PI) a -= 2 * math.PI;
            while (a < -math.PI) a += 2 * math.PI;
            return a;
        }

        public static float2 Closest(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float l2 = math.lengthsq(ab);
            if (l2 < 1e-8f) return a;
            float t = math.clamp(math.dot(p - a, ab) / l2, 0f, 1f);
            return a + ab * t;
        }

        public static float DistPointSeg(float2 p, float2 a, float2 b) => math.distance(p, Closest(p, a, b));

        /// <summary>Where the ray from <paramref name="from"/> through <paramref name="through"/> meets segment a–b.</summary>
        private static bool LineHit(float2 from, float2 through, float2 a, float2 b, out float2 x)
        {
            x = default;
            var r = through - from; var s = b - a;
            float den = r.x * s.y - r.y * s.x;
            if (math.abs(den) < 1e-6f) return false;
            var q = a - from;
            float t = (q.x * s.y - q.y * s.x) / den, u = (q.x * r.y - q.y * r.x) / den;
            if (t < 0 || u < 0 || u > 1) return false;
            x = from + r * t;
            return true;
        }
    }
}
