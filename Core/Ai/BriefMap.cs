// AI builder (docs/AI-BUILDER.md): the map picture that goes with the site brief, drawn from data (no screenshot).
// Top-down, north up: ground by height with hillshade and contours, outside the selection dimmed, roads, paths, tracks,
// buildings, water, a 10 m grid (darker every 50 m) labelled in the brief's local metres, the selection outlined.
// RGBA bytes, row 0 = south (Unity's texture order), so the game layer only has to encode it.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Ai
{
    public static class BriefMap
    {
        public sealed class Picture
        {
            public int W, H;
            public byte[] Rgba;
            public float MetresPerPixel;
        }

        public static Picture Draw(SiteBriefInput s, int maxSide = 1000, IReadOnlyList<AiPiece> pieces = null, Func<AiPiece, float> radius = null)
        {
            var g = s.Ground;
            float2 o = g.Origin, size = new float2(g.Nx, g.Nz) * g.Cell;
            float mpp = math.max(0.25f, math.cmax(size) / maxSide);
            int w = math.max(2, (int)math.ceil(size.x / mpp)), h = math.max(2, (int)math.ceil(size.y / mpp));
            var px = new float3[w * h];
            var (lo, _) = SiteBrief.Box(s.Ring);

            // ground: bilinear height, colour around the median inside, hillshade, contours
            float Height(float2 p)
            {
                float fx = (p.x - o.x) / g.Cell - 0.5f, fz = (p.y - o.y) / g.Cell - 0.5f;
                int i = (int)math.floor(fx), j = (int)math.floor(fz);
                float tx = fx - i, tz = fz - j;
                return math.lerp(math.lerp(g.HeightAt(i, j), g.HeightAt(i + 1, j), tx), math.lerp(g.HeightAt(i, j + 1), g.HeightAt(i + 1, j + 1), tx), tz);
            }
            float Water(float2 p)
            {
                int i = (int)math.floor((p.x - o.x) / g.Cell), j = (int)math.floor((p.y - o.y) / g.Cell);
                return g.WaterAt(i, j);
            }
            var z = new float[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++) z[j * w + i] = Height(o + new float2((i + 0.5f) * mpp, (j + 0.5f) * mpp));
            var sorted = (float[])z.Clone();
            Array.Sort(sorted);
            float ground = sorted[sorted.Length / 2];
            float range = math.max(1f, math.max(math.abs(sorted[(int)(sorted.Length * 0.99f)] - ground), math.abs(sorted[(int)(sorted.Length * 0.01f)] - ground)));
            float contour = 0.5f;
            foreach (var st in new[] { 0.5f, 1f, 2f, 5f, 10f }) { contour = st; if (2f * range / st <= 30f) break; }
            var light = math.normalize(new float3(-1f, 1.4f, 1f));
            var inside = new bool[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    var p = o + new float2((i + 0.5f) * mpp, (j + 0.5f) * mpp);
                    float v = z[j * w + i];
                    float t = math.clamp((v - ground) / range, -1f, 1f);
                    var neutral = new float3(0.86f, 0.90f, 0.78f);
                    var c = t < 0f ? math.lerp(neutral, new float3(0.25f, 0.45f, 0.75f), math.pow(-t, 0.7f)) : math.lerp(neutral, new float3(0.80f, 0.35f, 0.20f), math.pow(t, 0.7f));
                    float dx = (z[j * w + math.min(w - 1, i + 1)] - z[j * w + math.max(0, i - 1)]) / (2f * mpp);
                    float dz = (z[math.min(h - 1, j + 1) * w + i] - z[math.max(0, j - 1) * w + i]) / (2f * mpp);
                    c *= 0.72f + 0.28f * math.saturate(math.dot(math.normalize(new float3(-dx, 1f, -dz)), light) / light.y * 0.8f);
                    if (i + 1 < w && j + 1 < h)
                    {
                        float B(float hh, float stp) => math.round((hh - ground) / stp);
                        float r = z[j * w + i + 1], a = z[(j + 1) * w + i];
                        if (B(v, contour) != B(r, contour) || B(v, contour) != B(a, contour))
                            c *= B(v, contour * 4f) != B(r, contour * 4f) || B(v, contour * 4f) != B(a, contour * 4f) ? 0.45f : 0.7f;
                    }
                    float wd = Water(p);
                    if (wd > 0.05f) c = math.lerp(c, new float3(0.15f, 0.35f, 0.75f), math.saturate(0.5f + wd / 4f));
                    inside[j * w + i] = Poly.Contains(s.Ring, p);
                    px[j * w + i] = c;
                }

            int2 Px(float2 world) => (int2)math.floor((world - o) / mpp);
            void Set(int x, int y, float3 c, float a = 1f)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                px[y * w + x] = math.lerp(px[y * w + x], c, a);
            }
            void Disc(float2 world, float radiusM, float3 c)
            {
                var q = (world - o) / mpp;
                float r = math.max(0.75f, radiusM / mpp);
                for (int y = (int)(q.y - r); y <= (int)(q.y + r) + 1; y++)
                    for (int x = (int)(q.x - r); x <= (int)(q.x + r) + 1; x++)
                        if (math.lengthsq(new float2(x + 0.5f, y + 0.5f) - q) <= r * r) Set(x, y, c);
            }
            void FillQuad(float2[] quad, float3 c, float a)
            {
                float2 qlo = math.min(math.min(quad[0], quad[1]), math.min(quad[2], quad[3])), qhi = math.max(math.max(quad[0], quad[1]), math.max(quad[2], quad[3]));
                var a0 = Px(qlo); var a1 = Px(qhi);
                for (int y = a0.y; y <= a1.y; y++)
                    for (int x = a0.x; x <= a1.x; x++)
                        if (Poly.Contains(quad, o + new float2((x + 0.5f) * mpp, (y + 0.5f) * mpp))) Set(x, y, c, a);
            }

            // grid every 10 m, darker every 50 m, in the brief's local frame
            for (int x = 0; x < w; x++)
            {
                float lx = o.x + (x + 0.5f) * mpp - lo.x;
                float prev = lx - mpp;
                if (math.floor(lx / 10f) != math.floor(prev / 10f))
                    for (int y = 0; y < h; y++) Set(x, y, 0f, math.floor(lx / 50f) != math.floor(prev / 50f) ? 0.45f : 0.18f);
            }
            for (int y = 0; y < h; y++)
            {
                float lz = o.y + (y + 0.5f) * mpp - lo.y;
                float prev = lz - mpp;
                if (math.floor(lz / 10f) != math.floor(prev / 10f))
                    for (int x = 0; x < w; x++) Set(x, y, 0f, math.floor(lz / 50f) != math.floor(prev / 50f) ? 0.45f : 0.18f);
            }

            // outside the selection: dimmed
            for (int k = 0; k < px.Length; k++) if (!inside[k]) px[k] = math.lerp(px[k], new float3(0.5f), 0.45f);

            foreach (var b in s.Buildings) FillQuad(new[] { b.A, b.B, b.C, b.D }, new float3(0.08f), 0.85f);
            foreach (var n in s.Nets)
            {
                var c = n.Category == NetCategory.Road ? new float3(0.30f) : n.Category == NetCategory.Pathway ? new float3(0.82f, 0.70f, 0.50f) : new float3(0.45f, 0.30f, 0.18f);
                float len = math.distance(n.A.xz, n.B.xz) + math.distance(n.B.xz, n.C.xz) + math.distance(n.C.xz, n.D.xz);
                int steps = math.max(2, (int)math.ceil(len / (mpp * 0.75f)));
                for (int k = 0; k <= steps; k++) Disc(SiteBrief.Bez(n, k / (float)steps).xz, math.max(1f, n.Width * 0.5f), c);
            }

            // what the AI has placed: paths tan, trees green, bushes dark green, everything else orange; ids on all but plants
            var labels = new List<(float2 at, int id)>();
            if (pieces != null)
            {
                foreach (var p in pieces)
                    if (p.Kind == AiPieceKind.Path && p.Points != null)
                    {
                        float half = p.NetRole == NetRole.PathwayWide ? 3f : 2f;
                        for (int k = 0; k + 1 < p.Points.Length; k++)
                        {
                            int steps = math.max(2, (int)math.ceil(math.distance(p.Points[k], p.Points[k + 1]) / (mpp * 0.75f)));
                            for (int t = 0; t <= steps; t++) Disc(math.lerp(p.Points[k], p.Points[k + 1], t / (float)steps), half, new float3(0.93f, 0.82f, 0.60f));
                        }
                        labels.Add((p.Points[0], p.Id));
                    }
                foreach (var p in pieces)
                {
                    if (p.Kind != AiPieceKind.Object) continue;
                    float r = radius != null ? radius(p) : 1f;
                    bool plant = p.Role == ObjectRole.Tree || p.Role == ObjectRole.Bush;
                    var c = p.Role == ObjectRole.Tree ? new float3(0.10f, 0.45f, 0.12f) : p.Role == ObjectRole.Bush ? new float3(0.05f, 0.30f, 0.08f) : new float3(0.95f, 0.50f, 0.05f);
                    Disc(p.Position, math.max(0.6f, plant ? r * 0.6f : r), c);
                    if (!plant) labels.Add((p.Position, p.Id));
                }
            }

            // the selection's outline: white with a dark edge
            for (int k = 0; k < s.Ring.Length; k++)
            {
                var p = (s.Ring[k] - o) / mpp;
                var q = (s.Ring[(k + 1) % s.Ring.Length] - o) / mpp;
                int steps = (int)math.ceil(math.length(q - p)) + 1;
                for (int t = 0; t <= steps; t++)
                {
                    var m = math.lerp(p, q, t / (float)steps);
                    for (int oy = -2; oy <= 2; oy++)
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            bool core = math.abs(ox) <= 1 && math.abs(oy) <= 1;
                            int x = (int)m.x + ox, y = (int)m.y + oy;
                            if (x < 0 || y < 0 || x >= w || y >= h) continue;
                            if (core) px[y * w + x] = 1f;
                            else if (math.any(px[y * w + x] != 1f)) px[y * w + x] = new float3(0.12f);
                        }
                }
            }

            // labels at the 50 m lines: x along the bottom, z along the left
            int scale = math.max(2, math.min(4, w / 250));
            for (float lx = math.ceil((o.x - lo.x) / 50f) * 50f; lx < o.x - lo.x + size.x; lx += 50f)
            {
                int x = (int)((lx + lo.x - o.x) / mpp);
                Text(px, w, h, x + 3, 3, ((int)lx).ToString(), scale);
            }
            for (float lz = math.ceil((o.y - lo.y) / 50f) * 50f; lz < o.y - lo.y + size.y; lz += 50f)
            {
                int y = (int)((lz + lo.y - o.y) / mpp);
                Text(px, w, h, 3, y + 3, ((int)lz).ToString(), scale);
            }

            foreach (var (at, id) in labels)
            {
                var q = Px(at);
                Text(px, w, h, q.x + 2, q.y + 2, id.ToString(), math.max(1, scale - 1));
            }

            var rgba = new byte[w * h * 4];
            for (int k = 0; k < px.Length; k++)
            {
                var c = math.saturate(px[k]);
                rgba[k * 4] = (byte)(c.x * 255f); rgba[k * 4 + 1] = (byte)(c.y * 255f); rgba[k * 4 + 2] = (byte)(c.z * 255f); rgba[k * 4 + 3] = 255;
            }
            return new Picture { W = w, H = h, Rgba = rgba, MetresPerPixel = mpp };
        }

        // a 3 x 5 pixel font for digits and the minus sign, top row first
        private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111",
            ['4'] = "101101111001001", ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001001001001",
            ['8'] = "111101111101111", ['9'] = "111101111001111", ['-'] = "000000111000000",
        };

        /// <summary>Text at (x, y) = its lower-left pixel, white on a dark box.</summary>
        private static void Text(float3[] px, int w, int h, int x0, int y0, string s, int scale)
        {
            int tw = s.Length * 4 * scale + scale, th = 7 * scale;
            for (int y = y0; y < y0 + th; y++)
                for (int x = x0; x < x0 + tw; x++)
                    if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = math.lerp(px[y * w + x], new float3(0.05f), 0.75f);
            for (int ci = 0; ci < s.Length; ci++)
            {
                if (!Glyphs.TryGetValue(s[ci], out var gl)) continue;
                for (int r = 0; r < 5; r++)
                    for (int c = 0; c < 3; c++)
                    {
                        if (gl[r * 3 + c] != '1') continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int x = x0 + scale + ci * 4 * scale + c * scale + sx, y = y0 + scale + (4 - r) * scale + sy;
                                if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = 1f;
                            }
                    }
            }
        }
    }
}
