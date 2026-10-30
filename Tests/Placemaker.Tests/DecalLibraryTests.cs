// The shared decal library (Core/Decals/Dynamic/DecalVariants.cs, Recipes/decals/library/*.json; docs/DYNAMIC-DECALS.md,
// "The shared library"): every family reads, every variant bakes deterministically, strips and sheets repeat without a
// seam, chain pieces join, and the whole library fits the city-wide GPU budget.
// PM_BAKE_LIBRARY=1 also renders every variant on four grounds (grass, asphalt, paving, dirt) with the game's light from
// the north-west, and writes .forensics/decals/library/ (PNGs + manifest.json: the browser page's data).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class DecalLibraryTests
    {
        private readonly ITestOutputHelper _out;
        public DecalLibraryTests(ITestOutputHelper o) { _out = o; }

        /// <summary>The city-wide budget for the shared library on the GPU (all variants of every kind at once).</summary>
        // every variant of every family, as if one city drew them all: a city makes only the variants it uses and SharedDecals keeps
        // its own 200 MiB budget at run time (frees the unused ones); a nature city used about 60 MiB (makeover, 2026-10-30)
        public const long BudgetBytes = 320L * 1024 * 1024;

        private static DecalLibrary _lib;
        internal static DecalLibrary Library() =>
            _lib ??= DecalLibrary.LoadFolder(Path.Combine(TestKit.RepoRoot(), "Recipes", DecalLibrary.FolderName));

        private static IEnumerable<DecalKind> Shared(DecalLibrary lib) => lib.Kinds.Where(k => k.Family.Length > 0);

        [Fact]
        public void EveryFamilyReads()
        {
            var lib = Library();
            foreach (var w in lib.Warnings) _out.WriteLine("warning: " + w);
            Assert.Empty(lib.Warnings);
            var shared = Shared(lib).ToList();
            Assert.True(shared.Count >= 10, "shared kinds: " + shared.Count);
            foreach (var k in shared) Assert.False(string.IsNullOrEmpty(k.Name), k.Id + " has no name");
        }

        [Fact]
        public void VariantsAreDeterministicAndDiffer()
        {
            var k = Library().Get("leafOak");
            Assert.NotNull(k);
            var a = DecalVariants.Bake(k, 2); var b = DecalVariants.Bake(k, 2); var c = DecalVariants.Bake(k, 3);
            Assert.Equal(a.Img.Albedo, b.Img.Albedo);
            Assert.NotEqual(a.V.Seed, c.V.Seed);
            Assert.True(a.Img.Covered > 0);
            Assert.True(DecalVariants.TryParseId("leafOak#3", out var kind, out int i) && kind == "leafOak" && i == 3);
        }

        [Fact]
        public void StripsRepeatWithoutASeam()
        {
            foreach (var k in Shared(Library()).Where(k => k.Variants.Tile == VariantTiling.X || k.Variants.Tile == VariantTiling.XY))
            {
                var bv = DecalVariants.Bake(k, 0);
                var img = bv.Img;
                // the jump across the wrap (last column → first) is no bigger than between ordinary neighbours
                double wrap = 0, inner = 0;
                for (int y = 0; y < img.H; y++)
                {
                    wrap += Diff(img, y * img.W + img.W - 1, y * img.W);
                    for (int x = 0; x < img.W - 1; x++) inner += Diff(img, y * img.W + x, y * img.W + x + 1) / (img.W - 1);
                }
                _out.WriteLine($"{k.Id}: wrap {wrap / img.H:F2}, inner {inner / img.H:F2}");
                Assert.True(wrap / img.H <= Math.Max(inner / img.H * 3, inner / img.H + 16), $"{k.Id}: seam {wrap / img.H:F2} vs {inner / img.H:F2}");
            }
        }

        private static double Diff(DecalImage img, int p, int q)
        {
            // what shows: colour weighted by opacity, plus the opacity itself
            double ap = img.Albedo[p * 4 + 3] / 255.0, aq = img.Albedo[q * 4 + 3] / 255.0;
            double d = Math.Abs(ap - aq) * 255;
            for (int c = 0; c < 3; c++) d += Math.Abs(img.Albedo[p * 4 + c] * ap - img.Albedo[q * 4 + c] * aq);
            return d;
        }

        [Fact]
        public void ChainPiecesJoinAtTheMiddleOfTheirEnds()
        {
            foreach (var k in Shared(Library()).Where(k => k.Variants.Tile == VariantTiling.Chain))
                for (int i = 0; i < k.Variants.Count; i++)
                {
                    var img = DecalVariants.Bake(k, i).Img;
                    // the crack reaches both short sides at the middle row (± a few texels)
                    bool At(int x) { for (int y = img.H / 2 - 3; y <= img.H / 2 + 3; y++) if (img.Albedo[(y * img.W + x) * 4 + 3] > 60) return true; return false; }
                    Assert.True(At(0) && At(img.W - 1), $"{k.Id}#{i} does not reach both ends at the middle");
                }
        }

        [Fact]
        public void TheWholeLibraryFitsTheBudget()
        {
            long bytes = 0; int n = 0;
            var byFamily = new System.Collections.Generic.SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var k in Shared(Library()))
                for (int i = 0; i < k.Variants.Count; i++)
                {
                    var v = DecalVariants.Get(k, i);
                    var (w, h) = Pixels(k, v);
                    long b = DecalVariants.GpuBytes(w, h);
                    bytes += b; n++;
                    byFamily[k.Family ?? ""] = (byFamily.TryGetValue(k.Family ?? "", out var f) ? f : 0) + b;
                }
            _out.WriteLine($"{n} variants, {bytes / 1048576.0:F1} MiB on the GPU");
            foreach (var kv in byFamily) _out.WriteLine($"  {kv.Key}: {kv.Value / 1048576.0:F1} MiB");
            Assert.True(bytes <= BudgetBytes, $"library {bytes / 1048576.0:F1} MiB over the {BudgetBytes / 1048576} MiB budget");
        }

        /// <summary>The texture size of a variant without baking it (same rules as DecalVariants.Bake).</summary>
        private static (int, int) Pixels(DecalKind k, DecalVariant v)
        {
            // baking is the truth; a quick bake of tiny kinds is cheap, so just bake when unsure
            var b = DecalVariants.Bake(k, v.Index);
            return (b.Img.W, b.Img.H);
        }

        // ───────────── the browser's pictures (PM_BAKE_LIBRARY=1) ─────────────

        private static readonly string[] Grounds = { "grass", "asphalt", "paving", "dirt" };

        [Fact]
        public void BakeTheBrowser()
        {
            if (Environment.GetEnvironmentVariable("PM_BAKE_LIBRARY") != "1") return;
            string only = Environment.GetEnvironmentVariable("PM_BAKE_ONLY") ?? "";
            var lib = Library();
            var dir = Path.Combine(TestKit.RepoRoot(), ".forensics", "decals", "library");
            Directory.CreateDirectory(Path.Combine(dir, "img"));
            var kinds = Shared(lib).Where(k => only.Length == 0 || only.Split(',').Contains(k.Id) || only.Split(',').Contains(k.Family)).ToList();
            var jobs = kinds.SelectMany(k => Enumerable.Range(0, k.Variants.Count).Select(i => (k, i))).ToList();
            var results = new Dictionary<string, JsonValue>();
            var clock = Stopwatch.StartNew();
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, job =>
            {
                var (k, i) = job;
                var bv = DecalVariants.Bake(k, i);
                BakedVariant next = k.Variants.Tile == VariantTiling.Chain ? DecalVariants.Bake(k, (i + 1) % k.Variants.Count) : null;
                string file = $"img/{k.Id}-{i}.png";
                Sheet(bv, next, Path.Combine(dir, file));
                var v = JsonValue.Obj()
                    .Set("id", bv.V.Id).Set("index", i).Set("image", file)
                    .Set("size", Arr(bv.V.Size.x, bv.V.Size.y)).Set("span", Arr(bv.Span.x, bv.Span.y))
                    .Set("fade", bv.V.Fade.ToString().ToLowerInvariant()).Set("saturation", R(bv.V.Saturation)).Set("strength", R(bv.V.Strength)).Set("wet", R(bv.V.Wetness)).Set("age", R(bv.V.Age))
                    .Set("px", Arr(bv.Img.W, bv.Img.H)).Set("gpuBytes", bv.GpuBytes)
                    .Set("coverage", R(bv.Img.Covered / (double)(bv.Img.W * bv.Img.H))).Set("ms", Math.Round(bv.Img.Ms));
                lock (results) results[bv.V.Id] = v;
            });
            long total = 0;
            var root = JsonValue.Obj().Set("generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Set("generatorVersion", DecalVariants.GeneratorVersion);
            var arr = JsonValue.Arr();
            foreach (var k in kinds.OrderBy(k => k.Family).ThenBy(k => k.Id))
            {
                var kv = Describe(k);
                var vs = JsonValue.Arr();
                for (int i = 0; i < k.Variants.Count; i++)
                {
                    var v = results[DecalVariants.Id(k.Id, i)];
                    total += (long)v["gpuBytes"].AsNumber();
                    vs.Add(v);
                }
                kv.Set("variants", vs);
                arr.Add(kv);
            }
            root.Set("kinds", arr).Set("variantCount", jobs.Count).Set("gpuBytes", total).Set("bakeSeconds", Math.Round(clock.Elapsed.TotalSeconds, 1));
            File.WriteAllText(Path.Combine(dir, only.Length == 0 ? "manifest.json" : "manifest-partial.json"), root.Write(true));
            _out.WriteLine($"{jobs.Count} variants of {kinds.Count} kinds in {clock.Elapsed.TotalSeconds:F1} s, {total / 1048576.0:F1} MiB → {dir}");
        }

        private static JsonValue Arr(params double[] v) { var a = JsonValue.Arr(); foreach (var d in v) a.Add(Math.Round(d, 3)); return a; }
        private static double R(double v) => Math.Round(v, 3);

        // ───────────── settings in plain words, and the knobs a person may suggest values for ─────────────

        private static string Layers(byte m)
        {
            var p = new List<string>();
            if ((m & DecalLayerBits.Terrain) != 0) p.Add("ground, grass and paving");
            if ((m & DecalLayerBits.Roads) != 0) p.Add("paths and roads");
            return string.Join(" and ", p);
        }

        private static string Tiling(VariantTiling t) => t switch
        {
            VariantTiling.X => "a strip that repeats along its length (shown twice end to end)",
            VariantTiling.XY => "a sheet that repeats both ways (shown 2 × 2)",
            VariantTiling.Chain => "pieces that join end to end (shown with the next piece)",
            _ => "a patch with a soft edge",
        };

        private static JsonValue Describe(DecalKind k)
        {
            var o = JsonValue.Parse(k.Json);
            var kv = JsonValue.Obj().Set("id", k.Id).Set("name", k.Name).Set("family", k.Family).Set("tier", k.Tier).Set("about", k.About);
            var plain = JsonValue.Arr();
            var knobs = JsonValue.Arr();
            void Plain(string label, string value) => plain.Add(JsonValue.Obj().Set("label", label).Set("value", value));
            void Knob(string path, string label, JsonValue value, string unit, string help)
            {
                if (value == null || value.IsNull) return;
                knobs.Add(JsonValue.Obj().Set("path", path).Set("label", label).Set("value", value).Set("unit", unit).Set("help", help));
            }
            var sp = k.Variants;
            Plain("What it is", Tiling(sp.Tile));
            Plain("Lies on", Layers(k.Layers));
            Plain("Detail", $"{k.Texel * 100:0.#} cm per pixel");
            Plain("Size", sp.Tile == VariantTiling.None ? $"{sp.Size.x:0.#}–{sp.Size.y:0.#} m long" : $"{sp.Size.x:0.#}–{sp.Size.y:0.#} m per repeat");
            Plain("Variations", sp.Count.ToString());
            Plain("Edge", $"fades over {k.Feather:0.#} m, ragged by {k.Warp:0.#} m");
            var vo = o["variants"];
            Knob("variants.count", "How many variations", vo["count"], "", "more variations = less repetition, more memory");
            Knob("variants.size", "Size (smallest, biggest)", vo["size"], "m", "length of one patch or one repeat");
            Knob("edge.feather", "Edge softness", o["edge"]["feather"], "m", "how far the edge fades out");
            Knob("edge.warp", "Edge raggedness", o["edge"]["warp"], "m", "how far the outline wanders (0 = round)");
            int n = 0;
            foreach (var op in o["ops"].Items)
            {
                string at = $"ops[{n++}]";
                switch (op["op"].AsString(""))
                {
                    case "scatter":
                    {
                        var st = op["stamp"];
                        string pieces = st.IsArray ? string.Join(", ", st.Items.Select(x => x.AsString())) : st.AsString("leaf");
                        Plain("Pieces", pieces);
                        Plain("Pieces per m²", op["density"].AsNumber(2).ToString("0.#", CultureInfo.InvariantCulture));
                        var sz = op["size"];
                        if (sz.IsArray) Plain("Piece size", $"{sz.Items[0].AsNumber() * 100:0.#}–{sz.Items[1].AsNumber() * 100:0.#} cm");
                        Plain("Clumping", $"{op["cluster"].AsNumber(0.7) * 100:0}% of the pieces in clumps");
                        Plain("Shine", Shine(op["smooth"].AsNumber(0.3)));
                        Knob(at + ".density", $"How many {pieces} (per m²)", op["density"], "/m²", "pieces per square metre where it is thickest");
                        Knob(at + ".size", $"Size of {pieces} (smallest, biggest)", op["size"], "m", "length of one piece");
                        Knob(at + ".cluster", "Clumping (0–1)", op["cluster"], "", "share of pieces in clumps; the rest spread evenly");
                        Knob(at + ".palette", $"Colours of {pieces}", op["palette"], "rgb", "each piece picks one");
                        Knob(at + ".smooth", "Shine (0 matt – 1 glossy)", op["smooth"], "", "wet or waxy things are glossy");
                        break;
                    }
                    case "layer": case "paint":
                    {
                        var c = op["color"];
                        Plain("Colour", Hex(c) + (op.Has("color2") ? " to " + Hex(op["color2"]) : ""));
                        Plain("Shine", Shine(op["smooth"].AsNumber(0.25)) + (op.Has("smooth2") ? " (" + Shine(op["smooth2"].AsNumber()) + " where wet)" : ""));
                        Knob(at + ".color", "Colour", c, "rgb", "the main colour of this layer");
                        if (op.Has("color2")) Knob(at + ".color2", "Second colour", op["color2"], "rgb", "blended with the first by a noise");
                        Knob(at + ".alphaK", "Strength (0–1)", op["alphaK"].IsNull ? JsonValue.Num(1) : op["alphaK"], "", "how opaque this layer is");
                        Knob(at + ".smooth", "Shine (0 matt – 1 glossy)", op["smooth"], "", "");
                        break;
                    }
                    case "cracks":
                    {
                        var w = op["width"];
                        Plain("Cracks", (op["chain"].AsBool() ? "one main crack end to end, " : "") + $"{op["seeds"].AsNumber(0.03):0.###} more per m²");
                        if (w.IsArray) Plain("Crack width", $"{w.Items[0].AsNumber() * 1000:0}–{w.Items[1].AsNumber() * 1000:0} mm");
                        Knob(at + ".seeds", "Extra cracks per m²", op["seeds"], "/m²", "");
                        Knob(at + ".width", "Crack width (thinnest, widest)", w, "m", "");
                        Knob(at + ".branch", "Branching (0–1)", op["branch"], "", "how often a crack splits");
                        if (op["chain"].AsBool()) Knob(at + ".wander", "How much the main crack wanders", op["wander"], "m", "");
                        break;
                    }
                }
            }
            var colours = JsonValue.Arr();
            foreach (var op in o["ops"].Items)
            {
                if (op.Has("color")) colours.Add(op["color"]);
                if (op.Has("palette")) foreach (var c in op["palette"].Items) colours.Add(c);
            }
            kv.Set("plain", plain).Set("knobs", knobs).Set("colours", colours).Set("json", JsonValue.Str(o.Write(true)))
              .Set("tile", sp.Tile.ToString().ToLowerInvariant()).Set("texel", k.Texel).Set("layers", Layers(k.Layers));
            return kv;
        }

        private static string Shine(double s) => s < 0.2 ? "matt" : s < 0.4 ? "a little sheen" : s < 0.65 ? "satin" : s < 0.85 ? "glossy" : "mirror wet";
        private static string Hex(JsonValue c) => c.IsArray && c.Count >= 3 ? $"#{(int)c.Items[0].AsNumber():X2}{(int)c.Items[1].AsNumber():X2}{(int)c.Items[2].AsNumber():X2}" : "?";

        // ───────────── rendering: the decal over a ground, lit like the game at midday ─────────────

        private const int PanelMax = 512;

        private static void Sheet(BakedVariant bv, BakedVariant next, string file)
        {
            var img = bv.Img;
            int rx = 1, ry = 1;
            if (bv.V.Tile == VariantTiling.X) rx = 2;
            if (bv.V.Tile == VariantTiling.XY) { rx = 2; ry = 2; }
            int W = img.W * (next != null ? 2 : rx), H = img.H * ry;
            if (next != null) H = Math.Max(img.H, next.Img.H);
            float mpp = bv.Span.x / img.W;
            // the decal (tiled or chained) as one image
            var A = new float[W * H]; var C = new float3[W * H]; var Nn = new float3[W * H]; var S = new float[W * H]; var O = new float[W * H];
            void Put(DecalImage src, int ox, int oy)
            {
                for (int y = 0; y < src.H; y++)
                    for (int x = 0; x < src.W; x++)
                    {
                        int X = ox + x, Y = oy + y;
                        if (X >= W || Y >= H) continue;
                        int i = Y * W + X, q = (y * src.W + x) * 4;
                        A[i] = src.Albedo[q + 3] / 255f;
                        C[i] = new float3(src.Albedo[q], src.Albedo[q + 1], src.Albedo[q + 2]) / 255f;
                        Nn[i] = new float3(src.Normal[q] / 127.5f - 1f, src.Normal[q + 1] / 127.5f - 1f, src.Normal[q + 2] / 127.5f - 1f);
                        S[i] = src.Mask[q + 3] / 255f; O[i] = src.Mask[q + 1] / 255f;
                    }
            }
            if (next != null) { Put(img, 0, (H - img.H) / 2); Put(next.Img, img.W, (H - next.Img.H) / 2); }
            else for (int j = 0; j < ry; j++) for (int i = 0; i < rx; i++) Put(img, i * img.W, j * img.H);
            // downscale factor so the panel is at most PanelMax on its long side
            int f = 1;
            while (Math.Max(W, H) / f > PanelMax * (next != null || rx > 1 ? 1.6 : 1)) f++;
            int pw = W / f, ph = H / f, gap = 6;
            var outRgb = new byte[(pw * 4 + gap * 3) * ph * 3];
            int OW = pw * 4 + gap * 3;
            for (int i = 0; i < outRgb.Length; i++) outRgb[i] = 32;
            var L = math.normalize(new float3(-0.45f, 0.55f, 0.7f));   // sun from the top left, high
            float flat = 0.35f + 0.65f * L.z;
            for (int g = 0; g < 4; g++)
            {
                for (int py = 0; py < ph; py++)
                    for (int px = 0; px < pw; px++)
                    {
                        float3 acc = 0;
                        for (int sy = 0; sy < f; sy++)
                            for (int sx = 0; sx < f; sx++)
                            {
                                int x = px * f + sx, y = py * f + sy, i = y * W + x;
                                // world position for the ground pattern (y flipped: image row 0 = the box's −z edge)
                                float wx = x * mpp, wz = y * mpp;
                                float3 gr = Ground(g, wx, wz, mpp);
                                float a = A[i];
                                float3 col = math.lerp(gr, C[i], a);
                                var n = math.normalizesafe(math.lerp(new float3(0, 0, 1), Nn[i], a), new float3(0, 0, 1));
                                float diff = (0.35f + 0.65f * math.max(0f, math.dot(n, L))) / flat;
                                float sm = math.lerp(0.18f, S[i], a);
                                float ao = math.lerp(1f, O[i], a);
                                float3 lit = col * diff * ao;
                                // sky reflection on glossy texels (water, wet leaves, oil)
                                float spec = math.pow(math.saturate(sm), 5f) * 0.55f;
                                lit = math.lerp(lit, new float3(0.62f, 0.7f, 0.78f) * (0.75f + 0.25f * n.y), spec * a);
                                acc += lit;
                            }
                        acc /= f * f;
                        int o = ((ph - 1 - py) * OW + g * (pw + gap) + px) * 3;
                        outRgb[o] = B(acc.x); outRgb[o + 1] = B(acc.y); outRgb[o + 2] = B(acc.z);
                    }
            }
            Png(file, OW, ph, outRgb);
        }

        private static byte B(float v) => (byte)math.clamp((int)(v * 255f + 0.5f), 0, 255);

        /// <summary>The four test grounds, close to the game's own at midday (sRGB 0..1).</summary>
        private static float3 Ground(int g, float x, float z, float mpp)
        {
            switch (g)
            {
                case 0:   // grass: game lawn green, soft tone patches and fine blades
                {
                    float t = DecalNoise.Fbm(x / 3f, z / 3f, 3, 11u) * 0.06f + DecalNoise.Perlin(x / 0.05f, z / 0.05f, 12u) * 0.05f + DecalNoise.Perlin(x / 0.012f, z / 0.03f, 13u) * 0.04f;
                    return new float3(0.36f, 0.45f, 0.17f) * (1f + t);
                }
                case 1:   // asphalt: dark grey with aggregate speckle
                {
                    float t = DecalNoise.Fbm(x / 4f, z / 4f, 2, 21u) * 0.04f + (DecalNoise.Value(x / 0.012f, z / 0.012f, 22u) - 0.5f) * 0.16f;
                    return new float3(0.31f, 0.31f, 0.32f) * (1f + t);
                }
                case 2:   // paving: pale concrete slabs 0.6 m with dark joints
                {
                    float jx = math.abs(math.frac(x / 0.6f) - 0.5f) * 0.6f, jz = math.abs(math.frac(z / 0.6f) - 0.5f) * 0.6f;
                    float jw = math.max(0.007f, mpp * 0.8f);
                    float joint = math.smoothstep(0.3f - jw, 0.3f - jw * 0.3f, math.max(jx, jz)) * math.saturate(0.02f / mpp);
                    int sx = (int)math.floor(x / 0.6f), sz = (int)math.floor(z / 0.6f);
                    float slab = (DecalNoise.Unit(DecalNoise.Hash(sx, sz, 31u)) - 0.5f) * 0.06f + DecalNoise.Perlin(x / 0.03f, z / 0.03f, 32u) * 0.03f;
                    return math.lerp(new float3(0.64f, 0.62f, 0.58f) * (1f + slab), new float3(0.36f, 0.35f, 0.33f), joint);
                }
                default:  // dirt: brown earth, lumpy
                {
                    float t = DecalNoise.Fbm(x / 1.2f, z / 1.2f, 3, 41u) * 0.08f + (DecalNoise.Value(x / 0.02f, z / 0.02f, 42u) - 0.5f) * 0.12f;
                    return new float3(0.47f, 0.38f, 0.28f) * (1f + t);
                }
            }
        }

        internal static void Png(string file, int w, int h, byte[] rgb)
        {
            using var ms = new MemoryStream();
            void Chunk(string type, byte[] data)
            {
                var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); ms.Write(len);
                var t = Encoding.ASCII.GetBytes(type); ms.Write(t); ms.Write(data);
                uint crc = Crc(t, data); var cb = BitConverter.GetBytes(crc); Array.Reverse(cb); ms.Write(cb);
            }
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            var wb = BitConverter.GetBytes(w); Array.Reverse(wb); Buffer.BlockCopy(wb, 0, ihdr, 0, 4);
            var hb = BitConverter.GetBytes(h); Array.Reverse(hb); Buffer.BlockCopy(hb, 0, ihdr, 4, 4);
            ihdr[8] = 8; ihdr[9] = 2;
            Chunk("IHDR", ihdr);
            var raw = new byte[(w * 3 + 1) * h];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(rgb, y * w * 3, raw, y * (w * 3 + 1) + 1, w * 3);
            using (var z = new MemoryStream())
            {
                using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw);
                Chunk("IDAT", z.ToArray());
            }
            Chunk("IEND", Array.Empty<byte>());
            File.WriteAllBytes(file, ms.ToArray());
        }

        private static uint Crc(byte[] a, byte[] b)
        {
            uint c = 0xFFFFFFFFu;
            void Run(byte[] d) { foreach (var x in d) { c ^= x; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; } }
            Run(a); Run(b);
            return c ^ 0xFFFFFFFFu;
        }
    }
}
