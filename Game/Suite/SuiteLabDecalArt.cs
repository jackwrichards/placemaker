// Game runner lab (research, never on for players): textures for dynamic decals generated in code (docs/research/dynamic-decals.md).
// Pure math on Unity.Mathematics noise, safe on a worker thread: no Unity API here. Each kind fills an albedo (sRGB, alpha =
// opacity), a normal map (RG = xy, A = 1: HDRP's UnpackNormalmapRGorAG) and a mask map (R metal, G AO, B 1, A smoothness).
// A texel's position is given in metres in the decal's own plane (local x, local z, the box centre at 0) and, for kinds that
// must join across boxes (the path), in world metres too.

using System;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    internal sealed class LabDecalMaps
    {
        public int W, H;
        public byte[] Albedo, Normal, Mask;   // RGBA32 each, row 0 = v 0
        public double Ms;
        public int Covered;                    // texels with any opacity
    }

    /// <summary>A path as a dense polyline in world metres (x, z) with its arc length at every point.</summary>
    internal sealed class LabCurve
    {
        public float2[] P;
        public float[] S;
        public float Length => S[S.Length - 1];

        public static LabCurve Bezier(float2 a, float2 b, float2 c, float2 d, int n = 600)
        {
            var cv = new LabCurve { P = new float2[n + 1], S = new float[n + 1] };
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                cv.P[i] = u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
                cv.S[i] = i == 0 ? 0f : cv.S[i - 1] + math.distance(cv.P[i - 1], cv.P[i]);
            }
            return cv;
        }

        /// <summary>The point at arc length s.</summary>
        public float2 At(float s)
        {
            int i = Array.BinarySearch(S, s);
            if (i >= 0) return P[i];
            i = ~i;
            if (i <= 0) return P[0];
            if (i >= S.Length) return P[P.Length - 1];
            float f = (s - S[i - 1]) / math.max(1e-5f, S[i] - S[i - 1]);
            return math.lerp(P[i - 1], P[i], f);
        }

        /// <summary>Distance to the curve and the arc length of the nearest point (brute force over a window, good enough for a lab).</summary>
        public void Nearest(float2 q, out float dist, out float s, float sLo = float.MinValue, float sHi = float.MaxValue)
        {
            float best = float.MaxValue; int bi = 0;
            int a0 = Array.BinarySearch(S, sLo), a1 = Array.BinarySearch(S, sHi);
            int lo = math.max(0, (a0 < 0 ? ~a0 : a0) - 1), hi = math.min(P.Length - 1, (a1 < 0 ? ~a1 : a1) + 1);
            if (hi <= lo) { lo = 0; hi = P.Length - 1; }
            s = 0f;
            for (int i = lo; i < hi; i++)
            {
                float2 ab = P[i + 1] - P[i];
                float t = math.saturate(math.dot(q - P[i], ab) / math.max(1e-6f, math.dot(ab, ab)));
                float d2 = math.distancesq(q, P[i] + ab * t);
                if (d2 < best) { best = d2; bi = math.min(i, P.Length - 2); }
            }
            {
                float2 ab = P[bi + 1] - P[bi];
                float t = math.saturate(math.dot(q - P[bi], ab) / math.max(1e-6f, math.dot(ab, ab)));
                dist = math.sqrt(best);
                s = math.lerp(S[bi], S[bi + 1], t);
            }
        }
    }

    internal static class LabDecalArt
    {
        // ───────────── noise helpers ─────────────

        private static float Fbm(float2 p, int oct, float seed)
        {
            float a = 0.5f, sum = 0f;
            p += seed * 17.13f;
            for (int i = 0; i < oct; i++) { sum += a * noise.snoise(p); p = p * 2.03f + 7.7f; a *= 0.5f; }
            return sum;   // about -1..1
        }

        private static float Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }

        private static float3 Srgb(int r, int g, int b) => new float3(r, g, b) / 255f;

        /// <summary>One texel's look before packing.</summary>
        private struct Texel { public float3 Rgb; public float A, Height, Smooth, Ao; }

        // ───────────── kinds ─────────────

        /// <summary>
        /// kind: dirt, cracks, puddle, leaves, path, calib. size: the box's plane size in metres (x, z). world: the box's
        /// centre and yaw (radians, the game's RotateY) for kinds that need world positions. Flip mirrors the mapping when the
        /// shader's v runs the other way (the calibration picture tells).
        /// </summary>
        public static LabDecalMaps Make(string kind, int w, int h, float2 size, int seed, float2 centre, float yaw,
            LabCurve curve = null, float s0 = 0f, float s1 = 0f, float pathWidth = 2.4f, bool flipU = false, bool flipV = false)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var t = new Texel[w * h];
            float cs = math.cos(yaw), sn = math.sin(yaw);
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h; if (flipV) v = 1f - v;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w; if (flipU) u = 1f - u;
                    var p = new float2((u - 0.5f) * size.x, (v - 0.5f) * size.y);
                    // quaternion.RotateY(yaw): local (x, z) -> world (x cos + z sin, -x sin + z cos)
                    var wp = centre + new float2(p.x * cs + p.y * sn, -p.x * sn + p.y * cs);
                    Texel o;
                    switch (kind)
                    {
                        case "dirt": o = Dirt(p, size, seed); break;
                        case "cracks": o = Cracks(p, size, seed); break;
                        case "puddle": o = Puddle(p, size, seed); break;
                        case "path": o = Path(wp, curve, s0, s1, pathWidth, seed); break;
                        case "calib": o = Calib(p, size); break;
                        default: o = default; break;
                    }
                    t[y * w + x] = o;
                }
            }
            if (kind == "leaves") Leaves(t, w, h, size, seed);
            var maps = Pack(t, w, h, size);
            maps.Ms = clock.Elapsed.TotalMilliseconds;
            return maps;
        }

        private static float Radial(float2 p, float2 size)
        {
            var q = p / (size * 0.5f);
            return math.length(q);   // 0 centre, 1 at the box edge (ellipse)
        }

        private static Texel Dirt(float2 p, float2 size, int seed)
        {
            float r = Radial(p, size);
            var warp = new float2(Fbm(p * 0.30f, 3, seed), Fbm(p * 0.30f + 5.2f, 3, seed));
            float shape = Fbm(p * 0.22f + warp * 1.4f, 5, seed + 1) * 0.9f + 0.62f - r * r * 1.25f;
            float a = math.smoothstep(0.0f, 0.42f, shape);
            // breaks: grass growing through, uneven wear
            float mott = math.smoothstep(-0.35f, 0.45f, Fbm(p * 1.6f, 4, seed + 2));
            a *= math.lerp(0.45f, 1f, mott);
            float tone = Fbm(p * 0.9f, 4, seed + 3) * 0.5f + 0.5f;
            var rgb = math.lerp(Srgb(84, 66, 48), Srgb(146, 126, 98), tone);
            // grit: pebbles
            float2 cell = noise.cellular(p * 7f + seed);
            float peb = 1f - math.smoothstep(0.10f, 0.22f, cell.x);
            peb *= math.smoothstep(0.2f, 0.7f, Fbm(p * 0.7f, 2, seed + 4) * 0.5f + 0.5f);
            rgb = math.lerp(rgb, Srgb(150, 146, 138), peb * 0.8f);
            // damp, darker hollows
            float damp = math.smoothstep(0.15f, 0.55f, -Fbm(p * 0.5f + 3.3f, 3, seed + 5));
            rgb *= 1f - damp * 0.35f;
            return new Texel { Rgb = rgb, A = a, Height = Fbm(p * 3f, 3, seed + 6) * 0.25f + peb * 0.6f - damp * 0.2f, Smooth = math.lerp(0.18f, 0.5f, damp), Ao = 1f - damp * 0.3f };
        }

        private static Texel Cracks(float2 p, float2 size, int seed)
        {
            float r = Radial(p, size);
            var warp = new float2(Fbm(p * 0.9f, 3, seed), Fbm(p * 0.9f + 9.1f, 3, seed)) * 0.25f;
            var q = (p + warp) * new float2(1.0f, 1.35f);
            float2 c = noise.cellular(q / 0.85f + seed * 3.1f);
            float edge = c.y - c.x;
            float wv = math.lerp(0.025f, 0.075f, Fbm(p * 0.6f, 2, seed + 1) * 0.5f + 0.5f);
            float block = 1f - math.smoothstep(wv * 0.5f, wv, edge);
            // only where the surface is old: a noisy region fading to the box edge
            float region = math.smoothstep(-0.05f, 0.35f, Fbm(p * 0.28f, 3, seed + 2) + 0.35f - r * r * 0.9f);
            block *= region;
            // one or two long meandering cracks (zero lines of a noise)
            float n = Fbm(p * new float2(0.18f, 0.35f) + warp, 4, seed + 3);
            float longC = 1f - math.smoothstep(0.006f, 0.016f, math.abs(n));
            longC *= math.smoothstep(1.05f, 0.75f, r);
            float crack = math.max(block, longC);
            // a faint grime halo either side
            float halo = (1f - math.smoothstep(0f, 0.20f, edge)) * region * 0.22f + (1f - math.smoothstep(0.0f, 0.06f, math.abs(n))) * 0.15f * math.smoothstep(1.05f, 0.75f, r);
            float a = math.saturate(crack * 0.95f + halo);
            var rgb = math.lerp(Srgb(66, 63, 58), Srgb(18, 16, 15), math.saturate(crack));
            return new Texel { Rgb = rgb, A = a, Height = -crack, Smooth = 0.12f, Ao = 1f - crack * 0.7f };
        }

        private static Texel Puddle(float2 p, float2 size, int seed)
        {
            float r = Radial(p, size);
            var warp = new float2(Fbm(p * 0.5f, 3, seed), Fbm(p * 0.5f + 4.4f, 3, seed));
            float s = Fbm(p * 0.45f + warp * 0.8f, 4, seed + 1) * 0.7f + 0.55f - r * r * 1.35f;
            float water = math.smoothstep(0.06f, 0.10f, s);
            float wet = math.smoothstep(-0.16f, 0.06f, s);
            float a = math.max(water * 0.8f, wet * 0.55f);
            var rgb = math.lerp(Srgb(62, 58, 54), Srgb(44, 46, 48), water);
            float smooth = math.lerp(0.55f, 0.97f, water);
            float hgt = (1f - water) * Fbm(p * 6f, 2, seed + 2) * 0.3f;
            return new Texel { Rgb = rgb, A = a, Height = hgt, Smooth = smooth, Ao = 1f };
        }

        private static Texel Path(float2 wp, LabCurve cv, float s0, float s1, float width, int seed)
        {
            if (cv == null) return default;
            cv.Nearest(wp, out float d, out float s, s0 - 12f, s1 + 12f);
            if (s < s0 - 0.05f || s >= s1 + 0.05f) return default;   // the neighbouring box's texels (5 cm overlap: no bilinear seam at the cut)
            // ends of the path fade out
            float endFade = math.smoothstep(0f, 3f, s) * math.smoothstep(0f, 3f, cv.Length - s);
            float half = width * 0.5f * (1f + 0.18f * noise.snoise(new float2(s * 0.12f, seed)));
            float dn = d + noise.snoise(wp * 0.9f + seed) * 0.28f + noise.snoise(wp * 3.1f + seed) * 0.10f;
            float a = 1f - math.smoothstep(half * 0.65f, half + 0.45f, dn);
            a *= endFade;
            if (a <= 0.001f) return default;
            // which side of the curve (for the ruts): the sign of the cross product with the tangent
            float2 tan = math.normalizesafe(cv.At(s + 0.5f) - cv.At(s - 0.5f));
            float2 nrm = new float2(-tan.y, tan.x);
            float2 foot = cv.At(s);
            float side = math.dot(wp - foot, nrm);
            float rutOff = half * 0.48f;
            float rut = math.max(1f - math.smoothstep(0.10f, 0.28f, math.abs(side - rutOff)), 1f - math.smoothstep(0.10f, 0.28f, math.abs(side + rutOff)));
            float crown = 1f - math.smoothstep(0f, rutOff * 0.7f, math.abs(side));
            float tone = Fbm(wp * 0.8f, 3, seed + 1) * 0.5f + 0.5f;
            var rgb = math.lerp(Srgb(104, 84, 60), Srgb(138, 116, 86), tone);
            rgb = math.lerp(rgb, Srgb(66, 52, 38), rut * 0.85f);
            // mud in the ruts: puddles where the ground dips
            float dip = math.smoothstep(0.35f, 0.6f, noise.snoise(new float2(s * 0.22f, seed + 7f)) * 0.5f + 0.5f);
            float mud = rut * dip;
            rgb = math.lerp(rgb, Srgb(38, 33, 28), mud);
            // tread marks across the ruts
            float tread = rut * (1f - dip) * (0.5f + 0.5f * math.sin(s * 6.2831f / 0.16f));
            // grass breaking through the crown
            float grass = crown * math.smoothstep(0.2f, 0.6f, Fbm(wp * 1.7f, 3, seed + 3) * 0.5f + 0.5f);
            a *= 1f - grass * 0.75f;
            float hgt = -rut * 0.6f + crown * 0.25f + tread * 0.15f + Fbm(wp * 4f, 2, seed + 4) * 0.15f;
            return new Texel { Rgb = rgb, A = math.saturate(math.max(a, mud * endFade * 0.95f)), Height = hgt, Smooth = math.lerp(0.22f, 0.92f, mud), Ao = 1f - rut * 0.25f };
        }

        private static Texel Calib(float2 p, float2 size)
        {
            // +x: a red bar, +z: a blue bar, centre: white disc; the rest clear. Read the picture to learn the mapping.
            var q = p / (size * 0.5f);
            if (math.length(q) < 0.12f) return new Texel { Rgb = new float3(1, 1, 1), A = 1f, Smooth = 0.3f, Ao = 1 };
            if (q.x > 0.15f && q.x < 0.95f && math.abs(q.y) < 0.08f) return new Texel { Rgb = new float3(0.9f, 0.05f, 0.05f), A = 1f, Smooth = 0.3f, Ao = 1 };
            if (q.y > 0.15f && q.y < 0.95f && math.abs(q.x) < 0.08f) return new Texel { Rgb = new float3(0.05f, 0.1f, 0.9f), A = 1f, Smooth = 0.3f, Ao = 1 };
            // a tick at +z end so blue reads as an arrow
            if (q.y > 0.75f && q.y < 0.95f && math.abs(q.x) < 0.25f && math.abs(q.x) > 0.08f && math.abs(q.x) < 1.2f - q.y) return new Texel { Rgb = new float3(0.05f, 0.1f, 0.9f), A = 1f, Smooth = 0.3f, Ao = 1 };
            return default;
        }

        private static void Leaves(Texel[] t, int w, int h, float2 size, int seed)
        {
            var palette = new[] { Srgb(176, 92, 34), Srgb(118, 74, 40), Srgb(188, 146, 52), Srgb(78, 50, 32), Srgb(150, 60, 30), Srgb(120, 110, 50) };
            float tx = size.x / w, tz = size.y / h;
            // humus stain under the clusters first
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new float2((x + 0.5f) / w - 0.5f, (y + 0.5f) / h - 0.5f) * size;
                    float r = Radial(p, size);
                    float cl = math.smoothstep(0.0f, 0.5f, Fbm(p * 0.45f, 3, seed) + 0.45f - r * r);
                    t[y * w + x] = new Texel { Rgb = Srgb(70, 54, 38), A = cl * 0.28f, Height = 0f, Smooth = 0.2f, Ao = 1f - cl * 0.2f };
                }
            int n = (int)(size.x * size.y * 60f);
            for (int i = 0; i < n; i++)
            {
                var c = new float2(Hash(i, 1, seed) - 0.5f, Hash(i, 2, seed) - 0.5f) * size;
                float r = Radial(c, size);
                float dens = math.saturate(Fbm(c * 0.45f, 3, seed) + 0.6f - r * r * 1.1f);
                if (Hash(i, 3, seed) > dens * dens) continue;
                float len = math.lerp(0.06f, 0.13f, Hash(i, 4, seed)), wid = len * math.lerp(0.4f, 0.65f, Hash(i, 5, seed));
                float ang = Hash(i, 6, seed) * 6.2831f;
                var col = palette[(int)(Hash(i, 7, seed) * palette.Length) % palette.Length] * math.lerp(0.8f, 1.15f, Hash(i, 8, seed));
                float2 ax = new float2(math.cos(ang), math.sin(ang));
                int x0 = math.max(0, (int)((c.x - len + size.x * 0.5f) / tx)), x1 = math.min(w - 1, (int)((c.x + len + size.x * 0.5f) / tx));
                int y0 = math.max(0, (int)((c.y - len + size.y * 0.5f) / tz)), y1 = math.min(h - 1, (int)((c.y + len + size.y * 0.5f) / tz));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new float2((x + 0.5f) * tx - size.x * 0.5f, (y + 0.5f) * tz - size.y * 0.5f) - c;
                        float a = math.dot(p, ax) / (len * 0.5f), b = (p.x * -ax.y + p.y * ax.x) / (wid * 0.5f);
                        // a leaf: pointed ellipse
                        float e = a * a + b * b * (1f + 0.6f * a * a);
                        if (e > 1f) continue;
                        float edge = math.smoothstep(1f, 0.75f, e);
                        float rib = 1f - math.smoothstep(0.02f, 0.10f, math.abs(b));
                        ref var o = ref t[y * w + x];
                        var rgb = col * (1f - rib * 0.25f) * math.lerp(0.85f, 1f, edge);
                        o.Rgb = math.lerp(o.Rgb, rgb, edge);
                        o.A = math.max(o.A, edge);
                        o.Height = math.max(o.Height, 0.3f + 0.5f * (1f - e));
                        o.Smooth = 0.3f; o.Ao = 1f;
                    }
            }
        }

        // ───────────── packing ─────────────

        private static LabDecalMaps Pack(Texel[] t, int w, int h, float2 size)
        {
            var m = new LabDecalMaps { W = w, H = h, Albedo = new byte[w * h * 4], Normal = new byte[w * h * 4], Mask = new byte[w * h * 4] };
            float tx = size.x / w, tz = size.y / h;
            const float bump = 0.012f;   // metres of relief for height 1
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, k = i * 4;
                    var o = t[i];
                    if (o.A > 0.004f) m.Covered++;
                    m.Albedo[k] = B(o.Rgb.x); m.Albedo[k + 1] = B(o.Rgb.y); m.Albedo[k + 2] = B(o.Rgb.z); m.Albedo[k + 3] = B(o.A);
                    float hl = t[y * w + math.max(0, x - 1)].Height, hr = t[y * w + math.min(w - 1, x + 1)].Height;
                    float hd = t[math.max(0, y - 1) * w + x].Height, hu = t[math.min(h - 1, y + 1) * w + x].Height;
                    var n = math.normalize(new float3(-(hr - hl) * bump / (2f * tx), -(hu - hd) * bump / (2f * tz), 1f));
                    m.Normal[k] = B(n.x * 0.5f + 0.5f); m.Normal[k + 1] = B(n.y * 0.5f + 0.5f); m.Normal[k + 2] = B(n.z * 0.5f + 0.5f); m.Normal[k + 3] = 255;
                    m.Mask[k] = 0; m.Mask[k + 1] = B(o.Ao <= 0f ? 1f : o.Ao); m.Mask[k + 2] = 255; m.Mask[k + 3] = B(o.Smooth);
                }
            return m;
        }

        private static byte B(float f) => (byte)math.clamp((int)(f * 255f + 0.5f), 0, 255);
    }
}
