// Placemaker — dynamic decals: fast deterministic noise for the generators (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// Pure arithmetic on integers and floats (no tables shared between threads, no allocation): gradient noise, fBm, domain
// warping and Worley cells. Same input, same output on every machine and thread, so a stroke regenerates to the same
// pixels after a load. Coordinates are world metres divided by the feature size.

using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public static class DecalNoise
    {
        public static uint Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = seed * 0x9E3779B1u ^ (uint)x * 0x85EBCA77u ^ (uint)y * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return h;
            }
        }

        public static uint Hash(uint a, uint b)
        {
            unchecked
            {
                uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return h;
            }
        }

        /// <summary>0..1 from a hash.</summary>
        public static float Unit(uint h) => (h & 0xFFFFFFu) * (1f / 16777216f);

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Grad(uint h, float dx, float dy)
        {
            // 8 unit-ish directions
            switch (h & 7u)
            {
                case 0: return dx + dy;
                case 1: return dx - dy;
                case 2: return -dx + dy;
                case 3: return -dx - dy;
                case 4: return dx * 1.4142f;
                case 5: return -dx * 1.4142f;
                case 6: return dy * 1.4142f;
                default: return -dy * 1.4142f;
            }
        }

        /// <summary>Gradient noise, about −1..1, feature size 1.</summary>
        public static float Perlin(float x, float y, uint seed)
        {
            int ix = (int)math.floor(x), iy = (int)math.floor(y);
            float fx = x - ix, fy = y - iy;
            float u = Fade(fx), v = Fade(fy);
            float a = Grad(Hash(ix, iy, seed), fx, fy);
            float b = Grad(Hash(ix + 1, iy, seed), fx - 1f, fy);
            float c = Grad(Hash(ix, iy + 1, seed), fx, fy - 1f);
            float d = Grad(Hash(ix + 1, iy + 1, seed), fx - 1f, fy - 1f);
            float r = a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
            return r * 0.7071f;
        }

        /// <summary>Value noise 0..1 (smooth), feature size 1.</summary>
        public static float Value(float x, float y, uint seed)
        {
            int ix = (int)math.floor(x), iy = (int)math.floor(y);
            float u = Fade(x - ix), v = Fade(y - iy);
            float a = Unit(Hash(ix, iy, seed)), b = Unit(Hash(ix + 1, iy, seed));
            float c = Unit(Hash(ix, iy + 1, seed)), d = Unit(Hash(ix + 1, iy + 1, seed));
            return math.lerp(math.lerp(a, b, u), math.lerp(c, d, u), v);
        }

        /// <summary>Fractal sum of gradient noise, normalised to about −1..1.</summary>
        public static float Fbm(float x, float y, int octaves, uint seed, float gain = 0.5f, float lacunarity = 2.03f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Perlin(x, y, seed + (uint)i * 1013u);
                norm += amp;
                x = x * lacunarity + 17.3f; y = y * lacunarity - 9.1f;
                amp *= gain;
            }
            return norm > 0f ? sum / norm * 1.35f : 0f;
        }

        /// <summary>Ridged fBm 0..1: sharp crests (veins, ruts, cracks of a dry crust).</summary>
        public static float Ridged(float x, float y, int octaves, uint seed, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - math.abs(Perlin(x, y, seed + (uint)i * 7919u) * 1.4f);
                sum += amp * n * n;
                norm += amp;
                x = x * 2.03f + 5.1f; y = y * 2.03f + 3.7f;
                amp *= gain;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>A random value 0..1 per Worley cell (the cell of the nearest jittered point): paving patchwork, mottling.</summary>
        public static float CellValue(float x, float y, uint seed, float jitter = 0.9f)
        {
            int ix = (int)math.floor(x), iy = (int)math.floor(y);
            float f1 = 9f; uint best = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    uint h = Hash(ix + i, iy + j, seed);
                    float px = ix + i + 0.5f + (Unit(h) - 0.5f) * jitter;
                    float py = iy + j + 0.5f + (Unit(Hash(h, 0x51u)) - 0.5f) * jitter;
                    float d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d < f1) { f1 = d; best = h; }
                }
            return Unit(Hash(best, 0xC311u));
        }

        /// <summary>Worley cells: F1 and F2 distances (cell units) to the nearest jittered points.</summary>
        public static float2 Worley(float x, float y, uint seed, float jitter = 0.9f)
        {
            int ix = (int)math.floor(x), iy = (int)math.floor(y);
            float f1 = 9f, f2 = 9f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    uint h = Hash(ix + i, iy + j, seed);
                    float px = ix + i + 0.5f + (Unit(h) - 0.5f) * jitter;
                    float py = iy + j + 0.5f + (Unit(Hash(h, 0x51u)) - 0.5f) * jitter;
                    float dx = px - x, dy = py - y;
                    float d = dx * dx + dy * dy;
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
            return new float2(math.sqrt(f1), math.sqrt(f2));
        }
    }
}
