// Placemaker — dynamic decals: mip chains and BC3 (DXT5) block compression on worker threads (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// The spike compressed on the main thread with Texture2D.Compress (2-21 ms a decal: a stutter). Here the whole chain is
// built and compressed off the main thread; the game only copies the finished bytes into the texture (well under a
// millisecond). Layout: every mip level of the full chain, largest first, 16 bytes per 4x4 block, as Unity expects for
// TextureFormat.DXT5 with mipChain = true.

using System;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public static class DecalTexture
    {
        public static int MipCount(int w, int h)
        {
            int m = 1, s = math.max(w, h);
            while (s > 1) { s >>= 1; m++; }
            return m;
        }

        /// <summary>Bytes of a BC3 chain of <paramref name="w"/> x <paramref name="h"/> with all mips.</summary>
        public static int Bc3Size(int w, int h)
        {
            int total = 0, mips = MipCount(w, h);
            for (int i = 0; i < mips; i++)
            {
                total += math.max(1, (w + 3) / 4) * math.max(1, (h + 3) / 4) * 16;
                w = math.max(1, w >> 1); h = math.max(1, h >> 1);
            }
            return total;
        }

        /// <summary>
        /// The BC3 chain of an RGBA32 image. <paramref name="alphaWeighted"/>: colour averaged by alpha in the mips (albedo,
        /// so transparent texels never darken the edges); <paramref name="normal"/>: RGB renormalised as a vector.
        /// </summary>
        public static byte[] Bc3Chain(byte[] rgba, int w, int h, bool alphaWeighted, bool normal)
        {
            var output = new byte[Bc3Size(w, h)];
            int at = 0;
            var level = rgba;
            int lw = w, lh = h, mips = MipCount(w, h);
            for (int m = 0; m < mips; m++)
            {
                at = EncodeLevel(level, lw, lh, output, at);
                if (m == mips - 1) break;
                int nw = math.max(1, lw >> 1), nh = math.max(1, lh >> 1);
                level = Down(level, lw, lh, nw, nh, alphaWeighted, normal);
                lw = nw; lh = nh;
            }
            return output;
        }

        private static byte[] Down(byte[] src, int w, int h, int nw, int nh, bool alphaWeighted, bool normal)
        {
            var dst = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0, wsum = 0;
                    int cnt = 0;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = math.min(w - 1, x * 2 + dx), sy = math.min(h - 1, y * 2 + dy);
                            int i = (sy * w + sx) * 4;
                            float aa = src[i + 3] / 255f;
                            float wt = alphaWeighted ? aa + 1e-3f : 1f;
                            r += src[i] * wt; g += src[i + 1] * wt; b += src[i + 2] * wt; wsum += wt;
                            a += src[i + 3]; cnt++;
                        }
                    int o = (y * nw + x) * 4;
                    if (normal)
                    {
                        var n = new float3(r / wsum / 127.5f - 1f, g / wsum / 127.5f - 1f, b / wsum / 127.5f - 1f);
                        n = math.normalizesafe(n, new float3(0f, 0f, 1f));
                        dst[o] = (byte)math.clamp((int)((n.x * 0.5f + 0.5f) * 255f + 0.5f), 0, 255);
                        dst[o + 1] = (byte)math.clamp((int)((n.y * 0.5f + 0.5f) * 255f + 0.5f), 0, 255);
                        dst[o + 2] = (byte)math.clamp((int)((n.z * 0.5f + 0.5f) * 255f + 0.5f), 0, 255);
                    }
                    else
                    {
                        dst[o] = (byte)math.clamp((int)(r / wsum + 0.5f), 0, 255);
                        dst[o + 1] = (byte)math.clamp((int)(g / wsum + 0.5f), 0, 255);
                        dst[o + 2] = (byte)math.clamp((int)(b / wsum + 0.5f), 0, 255);
                    }
                    dst[o + 3] = (byte)math.clamp((int)(a / cnt + 0.5f), 0, 255);
                }
            return dst;
        }

        private static int EncodeLevel(byte[] img, int w, int h, byte[] output, int at)
        {
            int bw = math.max(1, (w + 3) / 4), bh = math.max(1, (h + 3) / 4);
            var block = new byte[64];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int sx = math.min(w - 1, bx * 4 + px), sy = math.min(h - 1, by * 4 + py);
                            Buffer.BlockCopy(img, (sy * w + sx) * 4, block, (py * 4 + px) * 4, 4);
                        }
                    EncodeBlock(block, output, at);
                    at += 16;
                }
            return at;
        }

        /// <summary>One 4x4 RGBA block → 16 bytes of BC3 (8 alpha, 8 colour; the colour block is always in 4-colour mode in BC3).</summary>
        public static void EncodeBlock(byte[] px, byte[] o, int at)
        {
            // alpha: 8 interpolated levels between max and min
            int amin = 255, amax = 0;
            for (int i = 0; i < 16; i++) { int a = px[i * 4 + 3]; if (a < amin) amin = a; if (a > amax) amax = a; }
            o[at] = (byte)amax; o[at + 1] = (byte)amin;
            ulong abits = 0;
            if (amax > amin)
            {
                for (int i = 0; i < 16; i++)
                {
                    int a = px[i * 4 + 3];
                    // index along max→min: 0 = max, 1 = min, 2..7 = (6-k)/7 max + k/7 min
                    int t = (int)math.round((amax - a) * 7f / (amax - amin));   // 0..7 from max to min
                    int idx = t == 0 ? 0 : t == 7 ? 1 : t + 1;
                    abits |= (ulong)idx << (3 * i);
                }
            }
            for (int b = 0; b < 6; b++) o[at + 2 + b] = (byte)(abits >> (8 * b));

            // colour: endpoints from the bounding box (inset), 4 colours along the axis
            int r0 = 255, g0 = 255, b0 = 255, r1 = 0, g1 = 0, b1 = 0;
            for (int i = 0; i < 16; i++)
            {
                int r = px[i * 4], g = px[i * 4 + 1], bb = px[i * 4 + 2];
                if (r < r0) r0 = r; if (g < g0) g0 = g; if (bb < b0) b0 = bb;
                if (r > r1) r1 = r; if (g > g1) g1 = g; if (bb > b1) b1 = bb;
            }
            int ir = (r1 - r0) >> 4, ig = (g1 - g0) >> 4, ib = (b1 - b0) >> 4;
            r0 += ir; g0 += ig; b0 += ib; r1 -= ir; g1 -= ig; b1 -= ib;
            // choose the diagonal of the box that fits the block best (the box's main diagonal, or flipped in g or b)
            int bestFlip = 0; float bestErr = float.MaxValue;
            for (int flip = 0; flip < 4; flip++)
            {
                float3 lo = new float3(r0, (flip & 1) != 0 ? g1 : g0, (flip & 2) != 0 ? b1 : b0);
                float3 hi = new float3(r1, (flip & 1) != 0 ? g0 : g1, (flip & 2) != 0 ? b0 : b1);
                float3 ax = hi - lo; float l2 = math.max(1e-3f, math.dot(ax, ax));
                float err = 0f;
                for (int i = 0; i < 16; i++)
                {
                    var c = new float3(px[i * 4], px[i * 4 + 1], px[i * 4 + 2]);
                    float t = math.saturate(math.dot(c - lo, ax) / l2);
                    err += math.distancesq(c, lo + ax * t);
                }
                if (err < bestErr) { bestErr = err; bestFlip = flip; }
            }
            int cr0 = r1, cg0 = (bestFlip & 1) != 0 ? g0 : g1, cb0 = (bestFlip & 2) != 0 ? b0 : b1;   // "max" end
            int cr1 = r0, cg1 = (bestFlip & 1) != 0 ? g1 : g0, cb1 = (bestFlip & 2) != 0 ? b1 : b0;   // "min" end
            ushort c0 = To565(cr0, cg0, cb0), c1 = To565(cr1, cg1, cb1);
            if (c0 < c1) { var tmp = c0; c0 = c1; c1 = tmp; }
            var p0 = From565(c0); var p1 = From565(c1);
            var pal = new[] { p0, p1, (p0 * 2f + p1) / 3f, (p0 + p1 * 2f) / 3f };
            uint cbits = 0;
            if (c0 != c1)
                for (int i = 0; i < 16; i++)
                {
                    var c = new float3(px[i * 4], px[i * 4 + 1], px[i * 4 + 2]);
                    int best = 0; float bd = float.MaxValue;
                    for (int k = 0; k < 4; k++) { float d = math.distancesq(c, pal[k]); if (d < bd) { bd = d; best = k; } }
                    cbits |= (uint)best << (2 * i);
                }
            o[at + 8] = (byte)c0; o[at + 9] = (byte)(c0 >> 8);
            o[at + 10] = (byte)c1; o[at + 11] = (byte)(c1 >> 8);
            o[at + 12] = (byte)cbits; o[at + 13] = (byte)(cbits >> 8); o[at + 14] = (byte)(cbits >> 16); o[at + 15] = (byte)(cbits >> 24);
        }

        private static ushort To565(int r, int g, int b) =>
            (ushort)(((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255));

        private static float3 From565(ushort c)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            return new float3((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
        }

        /// <summary>Decodes one BC3 block back to RGBA (tests).</summary>
        public static byte[] DecodeBlock(byte[] o, int at)
        {
            var px = new byte[64];
            int a0 = o[at], a1 = o[at + 1];
            var al = new int[8];
            al[0] = a0; al[1] = a1;
            if (a0 > a1) for (int k = 1; k <= 6; k++) al[k + 1] = ((7 - k) * a0 + k * a1) / 7;
            else { for (int k = 1; k <= 4; k++) al[k + 1] = ((5 - k) * a0 + k * a1) / 5; al[6] = 0; al[7] = 255; }
            ulong abits = 0;
            for (int b = 0; b < 6; b++) abits |= (ulong)o[at + 2 + b] << (8 * b);
            ushort c0 = (ushort)(o[at + 8] | o[at + 9] << 8), c1 = (ushort)(o[at + 10] | o[at + 11] << 8);
            var p0 = From565(c0); var p1 = From565(c1);
            var pal = new[] { p0, p1, (p0 * 2f + p1) / 3f, (p0 + p1 * 2f) / 3f };
            uint cbits = (uint)(o[at + 12] | o[at + 13] << 8 | o[at + 14] << 16 | o[at + 15] << 24);
            for (int i = 0; i < 16; i++)
            {
                var c = pal[(cbits >> (2 * i)) & 3];
                px[i * 4] = (byte)c.x; px[i * 4 + 1] = (byte)c.y; px[i * 4 + 2] = (byte)c.z;
                px[i * 4 + 3] = (byte)al[(int)((abits >> (3 * i)) & 7)];
            }
            return px;
        }
    }
}
