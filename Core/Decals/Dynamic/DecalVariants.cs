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
//
// End caps (strips and crack chains): index 100 + i is variant i fading out toward its +x end, 200 + i fading out toward
// its −x end. The same pixels as variant i times a ragged ramp, so a cap joins its strip seamlessly on the open side and a
// run of strip pieces never stops in a hard cut (Brushes.EdgeStrip, CracksAlong).

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Placemaker.Core.Tool;

namespace Placemaker.Core.Decals.Dynamic
{
    public enum VariantTiling : byte { None = 0, X = 1, XY = 2, Chain = 3 }

    /// <summary>How a patch fades out (its coverage, so scatters thin out and layers fade alike): soft all round, along its
    /// length (thick at −x), from one side (thick at −z), a hollow middle (round a trunk), broken into islands, or a long
    /// wide fade.</summary>
    public enum VariantFade : byte { Soft = 0, Linear = 1, Side = 2, Ring = 3, Islands = 4, Wide = 5 }

    public sealed class VariantSpec
    {
        public int Count = 6;
        public float2 Size = new float2(3f, 3f);
        public float2 Aspect = new float2(1f, 1f);
        /// <summary>Width in metres (strips); NaN = from <see cref="Aspect"/>.</summary>
        public float2 Width = new float2(float.NaN, float.NaN);
        public float2 Strength = new float2(1f, 1f), Wet = new float2(0f, 0f), Age = new float2(0.5f, 0.5f);
        public VariantTiling Tile;
        /// <summary>The fades the variants cycle through ("fades": ["soft", "linear", …]); empty = soft.</summary>
        public VariantFade[] Fades = Array.Empty<VariantFade>();
        /// <summary>The colour levels the variants cycle through ("saturations": [1, 0.8]): full colour and a step toned down,
        /// so a builder picks the variant that suits the place; empty = full colour.</summary>
        public float[] Saturations = Array.Empty<float>();
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
            var fades = new System.Collections.Generic.List<VariantFade>();
            foreach (var f in v["fades"].Items)
                switch (f.AsString("").ToLowerInvariant())
                {
                    case "linear": fades.Add(VariantFade.Linear); break;
                    case "side": fades.Add(VariantFade.Side); break;
                    case "ring": fades.Add(VariantFade.Ring); break;
                    case "islands": fades.Add(VariantFade.Islands); break;
                    case "wide": fades.Add(VariantFade.Wide); break;
                    default: fades.Add(VariantFade.Soft); break;
                }
            s.Fades = fades.ToArray();
            var sats = new System.Collections.Generic.List<float>();
            foreach (var x in v["saturations"].Items) sats.Add(math.clamp((float)x.AsNumber(1), 0f, 1f));
            s.Saturations = sats.ToArray();
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
        public VariantFade Fade;
        public float Saturation;

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
        public const int GeneratorVersion = 7;   // 6: the mask's blue follows the coverage (DecalGenerator); 7: linear albedo (SharedDecals)
        /// <summary>Index offsets of a strip's end caps: CapEnd + i fades out toward +x, CapStart + i toward −x.</summary>
        public const int CapEnd = 100, CapStart = 200;

        /// <summary>True for an end-cap index of a strip or chain kind.</summary>
        public static bool IsCap(DecalKind k, int index) => index >= CapEnd && index < CapStart + 100 && k != null && (k.Variants.Tile == VariantTiling.X || k.Variants.Tile == VariantTiling.Chain);

        /// <summary>The plain variant a (cap) index is made from, within the kind's count.</summary>
        public static int BaseIndex(DecalKind k, int index) => math.clamp(index >= CapEnd ? index % 100 : index, 0, math.max(0, k.Variants.Count - 1));

        /// <summary>A valid index of the kind: caps kept for strips and chains, everything else clamped to the plain variants.</summary>
        public static int Normalize(DecalKind k, int index) => IsCap(k, index) ? (index >= CapStart ? CapStart : CapEnd) + BaseIndex(k, index) : BaseIndex(k, index);

        /// <summary>
        /// Metres the variant's texture spans (x, z) without making it: the box an instance draws at scale 1. Patches: the
        /// footprint plus its soft margin; strips and chains: one repeat by the width with its margin; sheets: one repeat.
        /// The same numbers <see cref="Bake"/> gives.
        /// </summary>
        public static float2 SpanOf(DecalKind k, int index)
        {
            var v = Get(k, BaseIndex(k, index));
            float pad = k.Warp * 1.3f + 0.3f;
            float maxSide = math.min(DecalTiler.MaxTileMetres, k.MaxTexels * k.Texel);
            switch (v.Tile)
            {
                case VariantTiling.None:
                {
                    var size = math.max(math.min(v.Size, new float2(maxSide - 2f * pad)), new float2(0.1f));
                    return size + 2f * pad;
                }
                case VariantTiling.X:
                case VariantTiling.Chain:
                {
                    bool chain = v.Tile == VariantTiling.Chain;
                    float L = math.min(v.Size.x, maxSide * (chain ? 1f : 0.75f));
                    return new float2(L, math.min(v.Size.y + 2f * pad, maxSide));
                }
                default:
                    return new float2(math.min(v.Size.x, maxSide * 0.75f), math.min(v.Size.y, maxSide * 0.75f));
            }
        }

        /// <summary>The variant's own footprint (x, z) in metres: patches without their soft margin, strips the drawn width.</summary>
        public static float2 FootprintOf(DecalKind k, int index)
        {
            var v = Get(k, BaseIndex(k, index));
            var span = SpanOf(k, index);
            return v.Tile == VariantTiling.None ? math.max(span - 2f * (k.Warp * 1.3f + 0.3f), new float2(0.1f)) : new float2(span.x, math.min(span.y, v.Size.y));
        }

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
            index = BaseIndex(k, index);
            float len = Spread(sp.Size, index, n, 0f);
            float wid = !float.IsNaN(sp.Width.x) ? Spread(sp.Width, index, n, 0.31f) : len * Spread(sp.Aspect, index, n, 0.37f);
            return new DecalVariant
            {
                Kind = k.Id, Index = index, Seed = DecalNoise.Hash(Fnv(k.Id), (uint)index * 2654435761u + 17u),
                Size = new float2(len, math.max(0.05f, wid)),
                Strength = Spread(sp.Strength, index, n, 0.11f), Wetness = Spread(sp.Wet, index, n, 0.53f), Age = Spread(sp.Age, index, n, 0.71f),
                Tile = sp.Tile,
                Fade = sp.Fades.Length > 0 ? sp.Fades[index % sp.Fades.Length] : VariantFade.Soft,
                // colour levels alternate with the index, so every size and fade comes at each level
                Saturation = sp.Saturations.Length > 0 ? sp.Saturations[index % sp.Saturations.Length] : 1f,
            };
        }

        /// <summary>The disk cache key of a variant: the kind's recipe text, the index and the generator version.</summary>
        public static string CacheKey(DecalKind k, int index) =>
            $"{k.Id}-{index}-{Fnv(k.Json + "|" + GeneratorVersion):x8}";

        /// <summary>
        /// The disk cache's files that no current recipe can use: a kind that is gone, or a hash from an older recipe or
        /// generator (every recipe change leaves its old variants behind: 916 MB of them on the developer's machine,
        /// 2026-10-30). Only names of the cache's own form (kind-index-hash.bc3) are ever returned.
        /// </summary>
        public static List<string> StaleCacheFiles(IEnumerable<string> fileNames, DecalLibrary lib)
        {
            var stale = new List<string>();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in fileNames)
            {
                if (name == null || !name.EndsWith(".bc3", StringComparison.OrdinalIgnoreCase)) continue;
                string stem = name.Substring(0, name.Length - 4);
                int b = stem.LastIndexOf('-'); if (b <= 0) continue;
                int a = stem.LastIndexOf('-', b - 1); if (a <= 0) continue;
                if (!int.TryParse(stem.Substring(a + 1, b - a - 1), out _)) continue;
                string kind = stem.Substring(0, a), hash = stem.Substring(b + 1);
                if (!hashes.TryGetValue(kind, out var want))
                {
                    var k = lib?.Get(kind);
                    hashes[kind] = want = k == null ? null : Fnv(k.Json + "|" + GeneratorVersion).ToString("x8");
                }
                if (want == null || !string.Equals(hash, want, StringComparison.OrdinalIgnoreCase)) stale.Add(name);
            }
            return stale;
        }

        /// <summary>GPU bytes of one variant: three BC3 maps with mips.</summary>
        public static long GpuBytes(int w, int h) => 3L * DecalTexture.Bc3Size(w, h);

        private static int Round4(float v, int lo, int hi) => math.clamp((int)math.round(v / 4f) * 4, lo, hi);

        /// <summary>Makes a variant's pixels (pure, deterministic; worker threads).</summary>
        public static BakedVariant Bake(DecalKind k, int index)
        {
            if (IsCap(k, index))
            {
                var plain = Bake(k, BaseIndex(k, index));
                Cap(plain, index >= CapStart ? -1 : 1);
                plain.V.Index = Normalize(k, index);
                return plain;
            }
            var v = Get(k, index);
            var s = new DecalStroke { Kind = k.Id, Seed = v.Seed, Strength = v.Strength, Wetness = v.Wetness, Age = v.Age, Saturation = v.Saturation };
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
                    var f = new StrokeFrame(s, k) { Fade = v.Fade };
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

        /// <summary>
        /// Fades a strip's pixels out toward one end (dir +1: +x, −1: −x): full for the first 20 % from the open end, gone
        /// before the last 8 %, the line ragged across the strip (never a straight cut). Opacity lives in the albedo's alpha
        /// and the mask map's blue (what the decal shader blends normals and smoothness by).
        /// </summary>
        private static void Cap(BakedVariant b, int dir)
        {
            var img = b.Img;
            int W = img.W, H = img.H;
            uint seed = DecalNoise.Hash(b.V.Seed, dir > 0 ? 0xCA9Eu : 0xCA9Fu);
            for (int y = 0; y < H; y++)
            {
                float vy = (y + 0.5f) / H;
                float wob = 0.10f * DecalNoise.Fbm(vy * 3f, 0.5f, 3, seed) + 0.05f * DecalNoise.Perlin(vy * 9f, 1.5f, seed + 1u);
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;                    // 0 at −x, 1 at +x
                    float t = dir > 0 ? u : 1f - u;              // 0 at the open end, 1 at the faded end
                    float r = 1f - math.smoothstep(0.2f + wob, 0.92f + wob * 0.5f, t);
                    int q = (y * W + x) * 4;
                    img.Albedo[q + 3] = (byte)math.clamp((int)(img.Albedo[q + 3] * r + 0.5f), 0, 255);
                    img.Mask[q + 2] = (byte)math.clamp((int)(img.Mask[q + 2] * r + 0.5f), 0, 255);
                }
            }
        }
    }
}
