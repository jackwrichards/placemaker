// Placemaker — dynamic decals: the shared library's variants (docs/DYNAMIC-DECALS.md, "The shared library"). GPL-3.0.
//
// Every look is made once, in a handful of variations, and shared by every build in the city: a build subscribes to an
// entry by id ("leafOak#3") and places instances of it (moved, turned, scaled, tinted, faded). Nothing is generated per
// build. A kind says in its JSON how many variants it has and how they differ:
//
//   "variants": {
//     "count": 8,                  how many (1 .. 32)
//     "size": [2.5, 4],            length in metres (the box's x), spread over the variants
//     "aspect": [0.7, 1],          width / length (or "width": [m, m] for strips)
//     "strength": [0.8, 1], "wet": [0, 1], "age": [0.2, 0.8],   the stroke knobs, spread too
//     "tile": "none"               none = a stamp with a soft edge; x = a strip that repeats seamlessly along x (edge
//                                  strips, kerb grime); xy = a sheet that repeats both ways (tone); chain = pieces that
//                                  join end to end at the middle of their short sides (crack runs)
//   }
//
// A variant is deterministic from the kind's id and its index (the seed is a hash of both), so the same library gives the
// same pixels on every machine, and a disk cache can be checked by key.

using System;
using Unity.Mathematics;
using Placemaker.Core.Tool;

namespace Placemaker.Core.Decals.Dynamic
{
    public enum VariantTiling : byte { None = 0, X = 1, XY = 2, Chain = 3 }

    public sealed class VariantSpec
    {
        public int Count = 6;
        public float2 Size = new float2(3f, 3f);
        public float2 Aspect = new float2(1f, 1f);
        /// <summary>Width in metres (strips); NaN = from <see cref="Aspect"/>.</summary>
        public float2 Width = new float2(float.NaN, float.NaN);
        public float2 Strength = new float2(1f, 1f), Wet = new float2(0f, 0f), Age = new float2(0.5f, 0.5f);
        public VariantTiling Tile;
        /// <summary>Share of the length cross-faded to make a repeating strip or sheet seamless.</summary>
        public float Overlap = 0.25f;

        public static VariantSpec Parse(JsonValue v)
        {
            var s = new VariantSpec();
            if (v == null || !v.IsObject) return s;
            s.Count = math.clamp((int)v["count"].AsNumber(6), 1, 32);
            s.Size = R(v["size"], s.Size);
            s.Aspect = R(v["aspect"], s.Aspect);
            s.Width = R(v["width"], s.Width);
            s.Strength = R(v["strength"], s.Strength);
            s.Wet = R(v["wet"], s.Wet);
            s.Age = R(v["age"], s.Age);
            s.Overlap = math.clamp((float)v["overlap"].AsNumber(0.25), 0.05f, 0.5f);
            switch (v["tile"].AsString("none").ToLowerInvariant())
            {
                case "x": case "strip": s.Tile = VariantTiling.X; break;
                case "xy": case "sheet": s.Tile = VariantTiling.XY; break;
                case "chain": s.Tile = VariantTiling.Chain; break;
                default: s.Tile = VariantTiling.None; break;
            }
            return s;
        }

        private static float2 R(JsonValue v, float2 def) =>
            v.IsArray && v.Count >= 2 ? new float2((float)v.Items[0].AsNumber(), (float)v.Items[1].AsNumber())
            : v.Kind == JsonKind.Number ? new float2((float)v.AsNumber()) : def;
    }

    /// <summary>One entry of the shared library: a kind made once with these values.</summary>
    public struct DecalVariant
    {
        public string Kind;
        public int Index;
        public uint Seed;
        /// <summary>Length (x) and width (z) in metres of the footprint (strips and sheets: one repeat).</summary>
        public float2 Size;
        public float Strength, Wetness, Age;
        public VariantTiling Tile;

        public string Id => DecalVariants.Id(Kind, Index);
    }

    /// <summary>A variant's pixels and the box they cover (centred on the origin, yaw 0).</summary>
    public sealed class BakedVariant
    {
        public DecalVariant V;
        public DecalImage Img;
        /// <summary>Metres the texture spans (x, z): the footprint plus its soft margin; one repeat for strips and sheets.</summary>
        public float2 Span;
        public long GpuBytes => DecalVariants.GpuBytes(Img.W, Img.H);
    }

    public static class DecalVariants
    {
        /// <summary>Bump when the generator or baking changes pixels: disk caches key on it.</summary>
        public const int GeneratorVersion = 2;

        public static string Id(string kind, int index) => kind + "#" + index;

        public static bool TryParseId(string id, out string kind, out int index)
        {
            kind = id; index = 0;
            if (string.IsNullOrEmpty(id)) return false;
            int h = id.LastIndexOf('#');
            if (h < 0) return true;
            kind = id.Substring(0, h);
            return int.TryParse(id.Substring(h + 1), out index) && index >= 0;
        }

        /// <summary>FNV-1a of a string (stable across runtimes, unlike string.GetHashCode).</summary>
        public static uint Fnv(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in s ?? "") { h ^= c; h *= 16777619u; }
                return h;
            }
        }

        /// <summary>Spreads a range over the variants: a different low-discrepancy order per knob, so sizes, strengths
        /// and wetness don't all rise together.</summary>
        private static float Spread(float2 r, int i, int n, float offset) =>
            n <= 1 || r.x == r.y ? r.x : math.lerp(r.x, r.y, math.frac(0.5f / n + i * 0.6180339f + offset));

        public static DecalVariant Get(DecalKind k, int index)
        {
            var sp = k.Variants;
            int n = sp.Count;
            index = math.clamp(index, 0, n - 1);
            float len = Spread(sp.Size, index, n, 0f);
            float wid = !float.IsNaN(sp.Width.x) ? Spread(sp.Width, index, n, 0.31f) : len * Spread(sp.Aspect, index, n, 0.37f);
            return new DecalVariant
            {
                Kind = k.Id, Index = index, Seed = DecalNoise.Hash(Fnv(k.Id), (uint)index * 2654435761u + 17u),
                Size = new float2(len, math.max(0.05f, wid)),
                Strength = Spread(sp.Strength, index, n, 0.11f), Wetness = Spread(sp.Wet, index, n, 0.53f), Age = Spread(sp.Age, index, n, 0.71f),
                Tile = sp.Tile,
            };
        }

        /// <summary>The disk cache key of a variant: the kind's recipe text, the index and the generator version.</summary>
        public static string CacheKey(DecalKind k, int index) =>
            $"{k.Id}-{index}-{Fnv(k.Json + "|" + GeneratorVersion):x8}";

        /// <summary>GPU bytes of one variant: three BC3 maps with mips.</summary>
        public static long GpuBytes(int w, int h) => 3L * DecalTexture.Bc3Size(w, h);

        private static int Round4(float v, int lo, int hi) => math.clamp((int)math.round(v / 4f) * 4, lo, hi);

        /// <summary>Makes a variant's pixels (pure, deterministic; worker threads).</summary>
        public static BakedVariant Bake(DecalKind k, int index)
        {
            var v = Get(k, index);
            var s = new DecalStroke { Kind = k.Id, Seed = v.Seed, Strength = v.Strength, Wetness = v.Wetness, Age = v.Age };
            float texel = k.Texel;
            float pad = k.Warp * 1.3f + 0.3f;
            float maxSide = math.min(DecalTiler.MaxTileMetres, k.MaxTexels * texel);
            switch (v.Tile)
            {
                case VariantTiling.None:
                {
                    var size = math.min(v.Size, new float2(maxSide - 2f * pad));
                    size = math.max(size, new float2(0.1f));
                    s.Shape = StrokeShape.Box; s.Center = float2.zero; s.Yaw = 0f; s.Size = size;
                    v.Size = size;
                    var f = new StrokeFrame(s, k);
                    var span = size + 2f * pad;
                    var t = new DecalTile { Center = float2.zero, Yaw = 0f, Size = span, W = Round4(span.x / texel, 8, k.MaxTexels), H = Round4(span.y / texel, 8, k.MaxTexels) };
                    return new BakedVariant { V = v, Img = DecalGenerator.Generate(f, t), Span = span };
                }
                case VariantTiling.X:
                case VariantTiling.Chain:
                {
                    bool chain = v.Tile == VariantTiling.Chain;
                    float L = math.min(v.Size.x, maxSide * (chain ? 1f : 0.75f));
                    int wl = Round4(L / texel, 8, chain ? k.MaxTexels : (int)(k.MaxTexels * 0.75f));
                    float tx = L / wl;
                    int wo = chain ? 0 : math.max(4, (int)math.round(wl * k.Variants.Overlap));
                    float across = math.min(v.Size.y + 2f * pad, maxSide);
                    int h = Round4(across / texel, 8, k.MaxTexels);
                    float reach = L * 0.6f + 2f + pad;
                    s.Shape = StrokeShape.Path;
                    s.Points = new[] { new float2(-L * 0.5f - reach, 0f), new float2(L * 0.5f + reach, 0f) };
                    s.Widths = new[] { v.Size.y, v.Size.y };
                    var f = new StrokeFrame(s, k);
                    if (chain) f.SetChain(new float2(-L * 0.5f, 0f), new float2(L * 0.5f, 0f));
                    else f.SetPeriod(new float2(-L * 0.5f, 0f), new float2(L, 0f));
                    var t = new DecalTile { Center = new float2(wo * tx * 0.5f, 0f), Yaw = 0f, Size = new float2((wl + wo) * tx, across), W = wl + wo, H = h };
                    var img = DecalGenerator.Generate(f, t, wo, 0);
                    v.Size = new float2(L, v.Size.y);
                    return new BakedVariant { V = v, Img = img, Span = new float2(L, across) };
                }
                default:   // XY sheet
                {
                    float Lx = math.min(v.Size.x, maxSide * 0.75f), Lz = math.min(v.Size.y, maxSide * 0.75f);
                    int wl = Round4(Lx / texel, 8, (int)(k.MaxTexels * 0.75f)), hl = Round4(Lz / texel, 8, (int)(k.MaxTexels * 0.75f));
                    float tx = Lx / wl, tz = Lz / hl;
                    int wo = math.max(4, (int)math.round(wl * k.Variants.Overlap)), ho = math.max(4, (int)math.round(hl * k.Variants.Overlap));
                    float ex = Lx + 4f + pad, ez = Lz + 4f + pad;
                    s.Shape = StrokeShape.Outline;
                    s.Points = new[] { new float2(-ex, -ez), new float2(ex, -ez), new float2(ex, ez), new float2(-ex, ez) };
                    var f = new StrokeFrame(s, k);
                    f.SetPeriod(new float2(-Lx * 0.5f, -Lz * 0.5f), new float2(Lx, Lz));
                    var t = new DecalTile { Center = new float2(wo * tx * 0.5f, ho * tz * 0.5f), Yaw = 0f, Size = new float2((wl + wo) * tx, (hl + ho) * tz), W = wl + wo, H = hl + ho };
                    var img = DecalGenerator.Generate(f, t, wo, ho);
                    v.Size = new float2(Lx, Lz);
                    return new BakedVariant { V = v, Img = img, Span = new float2(Lx, Lz) };
                }
            }
        }
    }
}
