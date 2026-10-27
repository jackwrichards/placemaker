// AI builder (docs/AI-BUILDER.md): shape_ground with a soft edge (feather) and along a line (channels, ditches, berms,
// embankments, beach shelves). A plain shape is one terraform ring with a hard edge; a feathered or line shape becomes a
// field: the target height inside the shape (its level, the plane through its corner heights, or the height along the
// line), blended back to the ground over the feather outside it, emitted as permanent terraform cells like the shore
// shaper's (Core/Layers/Types/Beach/ShoreTerrain). Pure: the ground before is given.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Ai
{
    public static class AiTerrain
    {
        private const float MinChange = 0.12f;
        private const int MaxCells = 4000;

        /// <summary>A feathered or line ground piece needs the field (else it is one ring as before).</summary>
        public static bool IsField(AiPiece p) => p.Kind == AiPieceKind.Ground && (p.LinePoints != null || p.Feather > 0.05f);

        /// <summary>The polygon a line of the given width covers (for drawing, checks and the box): left side out, right side back.</summary>
        public static float2[] LineRing(float2[] pts, float width)
        {
            if (pts == null || pts.Length < 2) return null;
            float h = math.max(0.5f, width * 0.5f);
            var left = new List<float2>(); var right = new List<float2>();
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[math.max(0, i - 1)]; var b = pts[math.min(pts.Length - 1, i + 1)];
                var d = math.normalizesafe(b - a, new float2(1, 0));
                var n = new float2(-d.y, d.x);
                left.Add(pts[i] + n * h); right.Add(pts[i] - n * h);
            }
            right.Reverse();
            left.AddRange(right);
            var r = left.ToArray();
            if (Poly.SignedArea(r) < 0f) Array.Reverse(r);
            return r;
        }

        /// <summary>The target height inside the shape and the distance outside its core (0 inside).</summary>
        public static void Core(AiPiece p, float2 q, out float target, out float outside)
        {
            if (p.LinePoints != null)
            {
                var pts = p.LinePoints;
                float best = float.MaxValue; int bk = 0; float bt = 0f;
                for (int k = 0; k + 1 < pts.Length; k++)
                {
                    var ab = pts[k + 1] - pts[k]; float l2 = math.lengthsq(ab);
                    float t = l2 < 1e-6f ? 0f : math.saturate(math.dot(q - pts[k], ab) / l2);
                    float d = math.distance(q, pts[k] + ab * t);
                    if (d < best) { best = d; bk = k; bt = t; }
                }
                target = p.LineHeights != null && p.LineHeights.Length == pts.Length ? math.lerp(p.LineHeights[bk], p.LineHeights[bk + 1], bt) : p.Height;
                outside = math.max(0f, best - p.LineWidth * 0.5f);
                return;
            }
            target = p.CornerHeights != null && p.CornerHeights.Length == p.Ring.Length ? Plane(p, q) : p.Height;
            outside = Poly.Contains(p.Ring, q) ? 0f : Poly.DistanceToBoundary(p.Ring, q);
            if (outside > 0f && p.CornerHeights != null && p.CornerHeights.Length == p.Ring.Length)
                target = Plane(p, Closest(p.Ring, q));
        }

        /// <summary>The least-squares plane through the corner heights (a rect's four corners: its exact slope).</summary>
        private static float Plane(AiPiece p, float2 q)
        {
            var r = p.Ring; var h = p.CornerHeights;
            int n = r.Length;
            float2 c = 0; float hc = 0;
            for (int i = 0; i < n; i++) { c += r[i]; hc += h[i]; }
            c /= n; hc /= n;
            float sxx = 0, sxz = 0, szz = 0, sxh = 0, szh = 0;
            for (int i = 0; i < n; i++)
            {
                var d = r[i] - c; float dh = h[i] - hc;
                sxx += d.x * d.x; sxz += d.x * d.y; szz += d.y * d.y; sxh += d.x * dh; szh += d.y * dh;
            }
            float det = sxx * szz - sxz * sxz;
            if (math.abs(det) < 1e-4f) return hc;
            float a = (sxh * szz - szh * sxz) / det, b = (szh * sxx - sxh * sxz) / det;
            var e = q - c;
            return hc + a * e.x + b * e.y;
        }

        private static float2 Closest(float2[] ring, float2 q)
        {
            float best = float.MaxValue; float2 at = ring[0];
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                var ab = b - a; float l2 = math.lengthsq(ab);
                float t = l2 < 1e-6f ? 0f : math.saturate(math.dot(q - a, ab) / l2);
                var p = a + ab * t;
                float d = math.distancesq(q, p);
                if (d < best) { best = d; at = p; }
            }
            return at;
        }

        /// <summary>The shaped height at q (the ground where the piece does not reach or its mode forbids).</summary>
        public static float HeightAt(AiPiece p, float2 q, float ground)
        {
            Core(p, q, out float t, out float e);
            float f = math.max(0f, p.Feather);
            if (e > f + 1e-3f || (e > 0f && f <= 0.05f)) return ground;
            float w = e <= 0f ? 1f : Smooth(1f - e / f);
            float h = math.lerp(ground, t, w);
            if (p.Shape == TerraformKind.Raise) return math.max(ground, h);
            if (p.Shape == TerraformKind.Lower) return math.min(ground, h);
            return h;
        }

        private static float Smooth(float x) { x = math.saturate(x); return x * x * (3f - 2f * x); }

        /// <summary>The box the piece touches: its core plus the feather.</summary>
        public static void Box(AiPiece p, out float2 lo, out float2 hi)
        {
            lo = new float2(float.MaxValue); hi = new float2(float.MinValue);
            var pts = p.LinePoints ?? p.Ring;
            foreach (var q in pts) { lo = math.min(lo, q); hi = math.max(hi, q); }
            float grow = math.max(0f, p.Feather) + (p.LinePoints != null ? p.LineWidth * 0.5f : 0f) + 1f;
            lo -= grow; hi += grow;
        }

        /// <summary>
        /// The piece as permanent terraform: its core as whole shapes (the area at its level or slope; a line as one sloped quad
        /// per segment), last so they win, and the feather band around it as cells (squares about 3 m, larger for big shapes),
        /// each to the blended height at its centre. Cells alone were smoothed by the game's 3.5 m terrain grid: a pond floor
        /// ended 1.3 m above its target and got no water (27 Oct). <paramref name="deepest"/> is the core (it carries a pond's water).
        /// </summary>
        public static List<PlannedTerraform> Cells(AiPiece p, Func<float2, float> ground, out PlannedTerraform deepest)
        {
            deepest = null;
            var list = new List<PlannedTerraform>();
            var core = CoreShapes(p);
            Box(p, out var lo, out var hi);
            float cell = math.max(3f, math.sqrt((hi.x - lo.x) * (hi.y - lo.y) / MaxCells));
            float low = float.MaxValue;
            for (float y = lo.y + cell * 0.5f; y < hi.y; y += cell)
                for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                {
                    var c = new float2(x, y);
                    Core(p, c, out _, out float e);
                    if (e <= 0f) continue;   // the core shapes cover it
                    float g = ground(c);
                    float h = HeightAt(p, c, g);
                    if (math.abs(h - g) < MinChange) continue;
                    float r = cell * 0.5f + 0.2f;
                    var ring = new[] { c + new float2(-r, -r), c + new float2(r, -r), c + new float2(r, r), c + new float2(-r, r) };
                    var t = new PlannedTerraform
                    {
                        Ring = Array.ConvertAll(ring, q => new float3(q.x, h, q.y)),
                        Kind = h > g ? TerraformKind.Raise : TerraformKind.Lower, TargetHeight = h, Permanent = true,
                    };
                    list.Add(t);
                }
            list.AddRange(core);
            foreach (var t in core) if (t.TargetHeight < low) { low = t.TargetHeight; deepest = t; }
            return list;
        }

        /// <summary>The core: the area at its height or through its corner heights; a line as sloped quads, one per segment.</summary>
        private static List<PlannedTerraform> CoreShapes(AiPiece p)
        {
            var list = new List<PlannedTerraform>();
            if (p.LinePoints != null)
            {
                var pts = p.LinePoints;
                float hw = math.max(0.5f, p.LineWidth * 0.5f);
                for (int k = 0; k + 1 < pts.Length; k++)
                {
                    var d = math.normalizesafe(pts[k + 1] - pts[k], new float2(1, 0));
                    var n = new float2(-d.y, d.x) * hw;
                    // a little past each end so the quads of a bent line overlap at the joints
                    var a = pts[k] - d * math.min(hw, 2f); var b = pts[k + 1] + d * math.min(hw, 2f);
                    float ha = p.LineHeights != null ? p.LineHeights[k] : p.Height, hb = p.LineHeights != null ? p.LineHeights[k + 1] : p.Height;
                    var ring = new[] { new float3(a.x - n.x, ha, a.y - n.y), new float3(b.x - n.x, hb, b.y - n.y), new float3(b.x + n.x, hb, b.y + n.y), new float3(a.x + n.x, ha, a.y + n.y) };
                    list.Add(new PlannedTerraform { Ring = ring, Kind = p.Shape, TargetHeight = (ha + hb) * 0.5f, Sloped = math.abs(ha - hb) > 0.01f, Permanent = true });
                }
                return list;
            }
            bool sloped = p.CornerHeights != null && p.CornerHeights.Length == p.Ring.Length;
            var r = new float3[p.Ring.Length];
            for (int i = 0; i < r.Length; i++) r[i] = new float3(p.Ring[i].x, sloped ? p.CornerHeights[i] : p.Height, p.Ring[i].y);
            float avg = p.Height;
            if (sloped) { avg = 0f; foreach (var h in p.CornerHeights) avg += h; avg /= p.CornerHeights.Length; }
            list.Add(new PlannedTerraform { Ring = r, Kind = p.Shape, TargetHeight = avg, Sloped = sloped, Permanent = true });
            return list;
        }

        /// <summary>For the tool's answer: the steepest slope (%) the shape makes where it blends into the ground.</summary>
        public static float SteepestEdge(AiPiece p, Func<float2, float> ground)
        {
            Box(p, out var lo, out var hi);
            float step = math.max(2f, math.cmax(hi - lo) / 40f), worst = 0f;
            for (float y = lo.y; y < hi.y; y += step)
                for (float x = lo.x; x < hi.x; x += step)
                {
                    var a = new float2(x, y);
                    float ha = HeightAt(p, a, ground(a));
                    foreach (var d in new[] { new float2(step, 0), new float2(0, step) })
                    {
                        var b = a + d;
                        float hb = HeightAt(p, b, ground(b));
                        // only the slope the shaping made (the ground's own slope is not ours)
                        float made = math.abs((ha - hb) - (ground(a) - ground(b)));
                        if (made > 0.05f) worst = math.max(worst, math.abs(ha - hb) / step);
                    }
                }
            return worst * 100f;
        }
    }
}
