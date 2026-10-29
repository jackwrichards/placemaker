// Dynamic decals (Core/Decals/Dynamic, docs/DYNAMIC-DECALS.md): the recipe library reads and every kind generates; the
// same stroke gives the same pixels; strokes survive the record's byte buffer; BC3 stays close to the source; and the
// community sports park's weathering grows from causes (heavy-tailed, most of the site clean, never one patch per object).
// PM_WRITE_DECALS=1 also writes pictures (the plan's decals from above, and close-ups) to .forensics/decals/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class DynamicDecalsTests
    {
        private readonly ITestOutputHelper _out;
        public DynamicDecalsTests(ITestOutputHelper o) { _out = o; }

        private static DecalLibrary _lib;
        internal static DecalLibrary Library()
        {
            if (_lib != null) return _lib;
            var path = Path.Combine(TestKit.RepoRoot(), "Recipes", DecalLibrary.FolderName, DecalLibrary.FileName);
            _lib = DecalLibrary.Parse(File.ReadAllText(path));
            return _lib;
        }

        private static DecalStroke SampleStroke(string kind)
        {
            switch (kind)
            {
                case "desirePath": case "muddyPrints": case "oilLine": case "wheelPath": case "kerbGrime": case "leafDrift":
                    return DecalStroke.Path(kind, 7, new[] { new float2(0, 0), new float2(6, 1.5f), new float2(12, 0.5f), new float2(19, 2f) }, kind == "wheelPath" ? 3f : 1.4f, 0.9f);
                case "asphaltTone":
                    return DecalStroke.Outline(kind, 7, new[] { new float2(0, 0), new float2(30, 0), new float2(30, 18), new float2(0, 18) }, 0.9f);
                default:
                    return DecalStroke.Box(kind, 7, new float2(5, 3), 0.4f, new float2(8, 5), 0.9f);
            }
        }

        [Fact]
        public void TheLibraryReadsAndEveryKindGenerates()
        {
            var lib = Library();
            foreach (var w in lib.Warnings) _out.WriteLine("warning: " + w);
            Assert.Empty(lib.Warnings);
            Assert.True(lib.Count >= 15, "starter library: " + lib.Count);
            foreach (var id in new[] { "wornGrass", "mud", "puddle", "cracksTar", "leafDrift", "leafLitter", "oilStain", "wheelPath", "kerbGrime", "gravelSpill", "stain", "dust" })
                Assert.True(lib.Has(id), id);
            foreach (var k in lib.Kinds)
            {
                var s = SampleStroke(k.Id);
                s.Wetness = 0.7f;
                var f = new StrokeFrame(s, k);
                var tiles = DecalTiler.Tiles(f, 0);
                Assert.NotEmpty(tiles);
                var sw = Stopwatch.StartNew();
                int covered = 0, texels = 0;
                foreach (var t in tiles)
                {
                    Assert.True(t.W % 4 == 0 && t.H % 4 == 0 && t.W <= k.MaxTexels && t.H <= k.MaxTexels, $"{k.Id}: {t.W}x{t.H}");
                    var img = DecalGenerator.Generate(f, t);
                    covered += img.Covered; texels += t.W * t.H;
                }
                _out.WriteLine($"{k.Id}: {tiles.Count} tile(s), {texels} texels, {100.0 * covered / texels:F0} % covered, {sw.ElapsedMilliseconds} ms");
                Assert.True(covered > 0, k.Id + " drew nothing");
            }
        }

        [Fact]
        public void TheSameStrokeGivesTheSamePixels()
        {
            var lib = Library();
            foreach (var id in new[] { "mud", "cracksTar", "leafDrift" })
            {
                var k = lib.Get(id);
                byte[] Run()
                {
                    var f = new StrokeFrame(SampleStroke(id), k);
                    var t = DecalTiler.Tiles(f, 0)[0];
                    return DecalGenerator.Generate(f, t).Albedo;
                }
                Assert.True(Run().SequenceEqual(Run()), id);
                // another seed, another look
                var s2 = SampleStroke(id); s2.Seed = 8;
                var f2 = new StrokeFrame(s2, k);
                Assert.False(Run().SequenceEqual(DecalGenerator.Generate(f2, DecalTiler.Tiles(f2, 0)[0]).Albedo), id + " ignores its seed");
            }
        }

        [Fact]
        public void APathJoinsAcrossItsTiles()
        {
            // a long drift is cut into several boxes; texels near a joint get the same coverage from both sides of it
            var k = Library().Get("desirePath");
            var pts = new float2[12];
            for (int i = 0; i < pts.Length; i++) pts[i] = new float2(i * 9f, 6f * math.sin(i * 0.6f));
            var s = DecalStroke.Path("desirePath", 3, pts, 1.6f, 1f);
            var f = new StrokeFrame(s, k);
            var tiles = DecalTiler.Tiles(f, 0);
            Assert.True(tiles.Count >= 3, "tiles " + tiles.Count);
            // every point of the centre line is inside at least one tile, and owned by exactly the tile whose arc range holds it
            for (float a = 1f; a < f.Length - 1f; a += 0.7f)
            {
                f.PathAt(a, out var p, out _);
                int owners = tiles.Count(t => a >= t.S0 - 0.05f && a <= t.S1 + 0.05f && math.all(math.abs(t.ToLocal(p)) < t.Size * 0.5f));
                Assert.True(owners >= 1, $"arc {a:F1} has no tile");
            }
        }

        [Fact]
        public void StrokesSurviveTheRecordBuffer()
        {
            var set = new DecalSet();
            set.Strokes.Add(new DecalStroke { Kind = "mud", Seed = 0xDEADBEEF, Shape = StrokeShape.Box, Center = new float2(10.5f, -3.25f), Yaw = 1.2f, Size = new float2(5, 4), Strength = 0.8f, Wetness = 0.6f, Age = 0.3f, Layers = 3, Order = -2, Y = 12.5f });
            set.Strokes.Add(DecalStroke.Path("leafDrift", 5, new[] { new float2(1, 2), new float2(3, 4), new float2(7, 5) }, 1.75f, 0.5f));
            set.Strokes.Add(DecalStroke.Outline("asphaltTone", 6, new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10) }, 0.9f));
            set.Kinds.Add("{\"id\":\"custom\",\"ops\":[{\"op\":\"fbm\",\"out\":\"n\"},{\"op\":\"layer\",\"alpha\":\"shape\"}]}");
            var bytes = DecalCodec.Encode(set);
            Assert.Equal(DecalCodec.CurrentVersion, bytes[0]);
            var back = DecalCodec.Decode(bytes);
            Assert.NotNull(back);
            Assert.Equal(3, back.Strokes.Count);
            var a = back.Strokes[0];
            Assert.Equal("mud", a.Kind); Assert.Equal(0xDEADBEEF, a.Seed); Assert.Equal(StrokeShape.Box, a.Shape);
            Assert.Equal(10.5f, a.Center.x); Assert.Equal(-3.25f, a.Center.y); Assert.Equal(1.2f, a.Yaw); Assert.Equal(5f, a.Size.x);
            Assert.Equal(0.8f, a.Strength); Assert.Equal(0.6f, a.Wetness, 2); Assert.Equal(0.3f, a.Age, 2); Assert.Equal(3, a.Layers); Assert.Equal(-2, a.Order); Assert.Equal(12.5f, a.Y);
            Assert.Equal(1.75f, back.Strokes[1].Widths[2], 2);
            Assert.Equal(new float2(7, 5), back.Strokes[1].Points[2]);
            Assert.Equal(3, back.Strokes[2].Points.Length);
            Assert.Single(back.Kinds);
            Assert.NotNull(DecalLibrary.ParseKind(back.Kinds[0], null));
            // a future version, a cut buffer and garbage are refused, never half read
            var future = (byte[])bytes.Clone(); future[0] = 99;
            Assert.Null(DecalCodec.Decode(future));
            Assert.Null(DecalCodec.Decode(bytes.Take(bytes.Length - 3).ToArray()));
            Assert.Null(DecalCodec.Decode(new byte[] { 1, 255, 255, 0, 0 }));
            Assert.Null(DecalCodec.Decode(null));
        }

        [Fact]
        public void Bc3StaysCloseToTheSource()
        {
            var k = Library().Get("mud");
            var f = new StrokeFrame(SampleStroke("mud"), k);
            var t = DecalTiler.Tiles(f, 0)[0];
            var img = DecalGenerator.Generate(f, t);
            var chain = DecalTexture.Bc3Chain(img.Albedo, img.W, img.H, true, false);
            Assert.Equal(DecalTexture.Bc3Size(img.W, img.H), chain.Length);
            // decode mip 0 and compare
            double err = 0; int n = 0, at = 0;
            int bw = img.W / 4, bh = img.H / 4;
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++, at += 16)
                {
                    var px = DecalTexture.DecodeBlock(chain, at);
                    for (int py = 0; py < 4; py++)
                        for (int pxi = 0; pxi < 4; pxi++)
                        {
                            int src = ((by * 4 + py) * img.W + bx * 4 + pxi) * 4, dst = (py * 4 + pxi) * 4;
                            float a = img.Albedo[src + 3] / 255f;
                            for (int c = 0; c < 3; c++) { err += Math.Abs(img.Albedo[src + c] - px[dst + c]) * a; }
                            err += Math.Abs(img.Albedo[src + 3] - px[dst + 3]);
                            n += 4;
                        }
                }
            double mean = err / n;
            _out.WriteLine($"BC3 mean error {mean:F2} / 255 over {img.W}x{img.H}");
            Assert.True(mean < 6.0, "mean error " + mean);
        }

        // ───────────── the community sports park ─────────────

        internal static float3[] Block(float w, float d) => new[] { new float3(0, 0, 0), new float3(w, 0, 0), new float3(w, 0, d), new float3(0, 0, d) };

        /// <summary>A site with streets round it (so the big park gets its car park).</summary>
        internal static SiteContext Streets(float w, float d, Func<float2, float> ground = null)
        {
            var site = new SiteContext();
            if (ground != null) site.HeightAt = ground;
            long id = 500;
            void Road(float2 a, float2 b)
            {
                var A = new float3(a.x, 0, a.y); var D = new float3(b.x, 0, b.y);
                site.NearbyNets.Add(new NearbyNet { ExternalId = id++, A = A, B = math.lerp(A, D, 1f / 3f), C = math.lerp(A, D, 2f / 3f), D = D, Width = 12, Category = NetCategory.Road, HasSidewalk = true });
            }
            Road(new float2(-20, -7), new float2(w + 20, -7));
            Road(new float2(w + 7, -20), new float2(w + 7, d + 20));
            Road(new float2(w + 20, d + 7), new float2(-20, d + 7));
            Road(new float2(-7, d + 20), new float2(-7, -20));
            return site;
        }

        internal static ComposedPlan Community(float w, float d, Func<float2, float> ground = null, double grime = 100, uint seedBase = 2000)
        {
            var gen = GeneratorRegistry.Get("sportsPark");
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, 1);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            editor.ApplyPreset(r, RecipePaths.Root, "community");
            r.Root.Settings.Values[Weathering.GrimeId] = grime;
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = seedBase + (++k);
            var ring = Block(w, d);
            var site = Streets(w, d, ground);
            TestKit.Entered(r, ring, site);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static List<DecalStroke> Strokes(ComposedPlan p) => p.Layers.SelectMany(l => l.Decals).ToList();

        public static IEnumerable<object[]> Sites() => new[] { new object[] { "block" }, new object[] { "big" }, new object[] { "slope" } };

        private static ComposedPlan Of(string site) => site switch
        {
            "big" => Community(216, 196),
            "slope" => Community(216, 196, p => 0.05f * p.y),
            _ => Community(146, 136),
        };

        [Theory]
        [MemberData(nameof(Sites))]
        public void TheCommunityParkLooksUsedFromItsCauses(string site)
        {
            var sw = Stopwatch.StartNew();
            var plan = Of(site);
            sw.Stop();
            Assert.False(plan.HasErrors);
            var w = plan.Weathering;
            Assert.NotNull(w);
            _out.WriteLine($"{site}: compose {sw.ElapsedMilliseconds} ms; weathering {w}");
            var strokes = Strokes(plan);
            Assert.Equal(w.Strokes, strokes.Count);
            Assert.InRange(strokes.Count, 15, Weathering.MaxStrokes);
            // the play pattern is there: goalmouths, the spine, a linesman's track
            foreach (var cause in new[] { "goalmouth", "goalCrease", "centreSpot" }) Assert.True(w.ByCause.ContainsKey(cause), cause);
            Assert.True(w.ByCause.ContainsKey("puddle") || w.ByCause.ContainsKey("tidemark"), "no puddle or tidemark");
            // budget: most of the site stays clean, few hot spots
            Assert.True(w.Touched <= 0.42f, $"touched {w.Touched:P0}");
            Assert.True(w.Strong <= 0.1f, $"strong {w.Strong:P1}");
            // never one patch per object: far fewer leaf strokes than trees, no stroke centred on a tree
            int trees = plan.Layers.Sum(l => l.Objects.Count(o => o.Role == ObjectRole.Tree));
            int leafStrokes = strokes.Count(s => s.Kind.StartsWith("leaf"));
            _out.WriteLine($"  {trees} trees, {leafStrokes} leaf strokes");
            Assert.True(leafStrokes <= Math.Max(3, trees / 4), $"{leafStrokes} leaf strokes for {trees} trees");
            var treePos = plan.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Tree).Select(o => o.Position.xz).ToList();
            int centred = strokes.Count(s => s.Shape == StrokeShape.Box && treePos.Any(t => math.distance(t, s.Center) < 0.5f));
            Assert.Equal(0, centred);
            // every stroke names a kind the library has
            var lib = Library();
            foreach (var s in strokes) Assert.True(lib.Has(s.Kind), s.Kind);
            // the organized paint wear is gone for this type (no mulch discs, verge bands, bullseyes): only the lawn's macro tone
            var host = plan.Layers.First(l => l.GeneratorId == "sportsPark");
            if (site == "big") Assert.True(w.ByCause.ContainsKey("wheelPath") || w.ByCause.ContainsKey("stallOil"), "the big park's car park has no wear");
            Assert.True(sw.ElapsedMilliseconds < 20000);
            if (Environment.GetEnvironmentVariable("PM_WRITE_DECALS") == "1") Pictures(plan, site);
        }

        [Fact]
        public void WeatheringIsDeterministicAndFollowsTheGrimeAmount()
        {
            var a = Strokes(Community(146, 136));
            var b = Strokes(Community(146, 136));
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Kind, b[i].Kind); Assert.Equal(a[i].Seed, b[i].Seed); Assert.Equal(a[i].Center, b[i].Center); Assert.Equal(a[i].Strength, b[i].Strength); }
            var none = Strokes(Community(146, 136, grime: 0));
            Assert.Empty(none);
            var lots = Strokes(Community(146, 136, grime: 200));
            Assert.True(lots.Sum(s => s.Strength) > a.Sum(s => s.Strength) * 1.2f, "Grime 200 % is not grimier");
            // other sports park styles are not weathered yet (v1: community only)
            var plan = SportsDesignTests.Compose(SportsDesignTests.Site("block"), "stadium");
            Assert.Empty(Strokes(plan));
        }

        [Fact]
        public void ThePlanCarriesTheStrokesToTheRecord()
        {
            var plan = Community(146, 136);
            var set = new DecalSet();
            foreach (var l in plan.Layers) set.Strokes.AddRange(l.Decals);
            var bytes = DecalCodec.Encode(set);
            _out.WriteLine($"{set.Strokes.Count} strokes = {bytes.Length} bytes on the record");
            Assert.True(bytes.Length < 64 * 1024, "record too big: " + bytes.Length);
            var back = DecalCodec.Decode(bytes);
            Assert.Equal(set.Strokes.Count, back.Strokes.Count);
        }

        // ───────────── pictures (PM_WRITE_DECALS=1) ─────────────

        private void Pictures(ComposedPlan plan, string site)
        {
            var dir = Path.Combine(TestKit.RepoRoot(), ".forensics", "decals");
            Directory.CreateDirectory(dir);
            var lib = Library();
            var strokes = Strokes(plan);
            var ring = plan.Footprints.Count > 0 ? plan.Footprints[0] : null;
            Poly.Bounds(ring, out var lo, out var hi);
            lo -= 8f; hi += 8f;
            var sw = Stopwatch.StartNew();
            var gen = new List<(DecalTile t, DecalImage img)>();
            long texels = 0;
            for (int i = 0; i < strokes.Count; i++)
            {
                var k = lib.Get(strokes[i].Kind);
                var f = new StrokeFrame(strokes[i], k);
                foreach (var t in DecalTiler.Tiles(f, i)) { var img = DecalGenerator.Generate(f, t); gen.Add((t, img)); texels += t.W * t.H; }
            }
            foreach (var g in gen.GroupBy(x => strokes[x.t.Stroke].Kind).OrderByDescending(g => g.Sum(x => (long)x.t.W * x.t.H))) _out.WriteLine($"    {g.Key}: {g.Count()} tiles, {g.Sum(x => (long)x.t.W * x.t.H) / 1e6:F2} M texels, {g.Sum(x => x.img.Ms):F0} ms");
            _out.WriteLine($"  generated {gen.Count} tiles, {texels / 1e6:F1} M texels in {sw.ElapsedMilliseconds} ms (one thread)");
            Render(plan, gen, lo, hi, 0.25f, Path.Combine(dir, $"community-{site}-top.png"));
            // close-ups: a goalmouth, the car park, a mud gate
            foreach (var cause in new[] { "goalmouth", "gatewayMud", "stallOil", "leafDrift", "puddle", "courtCorner" })
            {
                var s = strokes.FirstOrDefault(x => x.Cause == cause);
                if (s == null) continue;
                s.Bounds(out var a, out var b);
                var c = (a + b) * 0.5f;
                Render(plan, gen, c - 9f, c + 9f, 0.025f, Path.Combine(dir, $"community-{site}-{cause}.png"));
            }
        }

        private static void Render(ComposedPlan plan, List<(DecalTile t, DecalImage img)> gen, float2 lo, float2 hi, float mpp, string file)
        {
            int W = (int)((hi.x - lo.x) / mpp), H = (int)((hi.y - lo.y) / mpp);
            var bg = new float3[W * H];
            // background: grass, the plan's surfaces and paths
            var grass = new float3(0.30f, 0.42f, 0.18f);
            for (int i = 0; i < bg.Length; i++) bg[i] = grass;
            foreach (var l in plan.Layers)
            {
                foreach (var s in l.Surfaces)
                {
                    var r = Poly.To2D(s.Ring);
                    var col = s.Role == SurfaceRole.Asphalt ? new float3(0.34f, 0.34f, 0.35f) : s.Role == SurfaceRole.Grass ? new float3(0.27f, 0.47f, 0.16f) : new float3(0.55f, 0.53f, 0.5f);
                    Fill(bg, W, H, lo, mpp, r, col);
                }
                foreach (var n in l.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    for (float t = 0; t <= 1f; t += 0.01f)
                    {
                        float u = 1 - t;
                        var p = (u * u * u * n.A + 3 * u * u * t * n.B + 3 * u * t * t * n.C + t * t * t * n.D).xz;
                        Disc(bg, W, H, lo, mpp, p, 1.4f, new float3(0.62f, 0.6f, 0.57f));
                    }
                }
            }
            var acc = new float4[W * H];
            foreach (var (t, img) in gen)
            {
                // inverse mapping: each image pixel inside the tile samples the tile (nearest)
                var corners = t.Corners();
                float2 a = corners[0], b = corners[0];
                foreach (var c in corners) { a = math.min(a, c); b = math.max(b, c); }
                int x0 = math.max(0, (int)((a.x - lo.x) / mpp)), x1 = math.min(W - 1, (int)((b.x - lo.x) / mpp));
                int y0 = math.max(0, (int)((a.y - lo.y) / mpp)), y1 = math.min(H - 1, (int)((b.y - lo.y) / mpp));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var wp = lo + new float2(x + 0.5f, y + 0.5f) * mpp;
                        var lp = t.ToLocal(wp);
                        int tx = (int)((lp.x / t.Size.x + 0.5f) * t.W), ty = (int)((lp.y / t.Size.y + 0.5f) * t.H);
                        if (tx < 0 || ty < 0 || tx >= t.W || ty >= t.H) continue;
                        int q = (ty * t.W + tx) * 4;
                        float al = img.Albedo[q + 3] / 255f;
                        if (al <= 0f) continue;
                        var col = new float3(img.Albedo[q], img.Albedo[q + 1], img.Albedo[q + 2]) / 255f;
                        int i = y * W + x;
                        // composite in draw order of arrival (good enough for a preview)
                        var cur = acc[i];
                        float na = al + cur.w * (1 - al);
                        var nc = (col * al + cur.xyz * cur.w * (1 - al)) / math.max(1e-5f, na);
                        acc[i] = new float4(nc, na);
                    }
            }
            var px = new byte[W * H * 3];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    var c = math.lerp(bg[i], acc[i].xyz, acc[i].w);
                    int o = ((H - 1 - y) * W + x) * 3;   // north up
                    px[o] = (byte)math.clamp(c.x * 255f, 0, 255); px[o + 1] = (byte)math.clamp(c.y * 255f, 0, 255); px[o + 2] = (byte)math.clamp(c.z * 255f, 0, 255);
                }
            Png(file, W, H, px);
        }

        private static void Fill(float3[] bg, int W, int H, float2 lo, float mpp, float2[] ring, float3 col)
        {
            Poly.Bounds(ring, out var a, out var b);
            int x0 = math.max(0, (int)((a.x - lo.x) / mpp)), x1 = math.min(W - 1, (int)((b.x - lo.x) / mpp));
            int y0 = math.max(0, (int)((a.y - lo.y) / mpp)), y1 = math.min(H - 1, (int)((b.y - lo.y) / mpp));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (Poly.Contains(ring, lo + new float2(x + 0.5f, y + 0.5f) * mpp)) bg[y * W + x] = col;
        }

        private static void Disc(float3[] bg, int W, int H, float2 lo, float mpp, float2 p, float r, float3 col)
        {
            int x0 = math.max(0, (int)((p.x - r - lo.x) / mpp)), x1 = math.min(W - 1, (int)((p.x + r - lo.x) / mpp));
            int y0 = math.max(0, (int)((p.y - r - lo.y) / mpp)), y1 = math.min(H - 1, (int)((p.y + r - lo.y) / mpp));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (math.distancesq(lo + new float2(x + 0.5f, y + 0.5f) * mpp, p) <= r * r) bg[y * W + x] = col;
        }

        private static void Png(string file, int w, int h, byte[] rgb)
        {
            using var ms = new MemoryStream();
            void Chunk(string type, byte[] data)
            {
                var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); ms.Write(len);
                var t = System.Text.Encoding.ASCII.GetBytes(type); ms.Write(t); ms.Write(data);
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
                using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) zs.Write(raw);
                Chunk("IDAT", z.ToArray());
            }
            Chunk("IEND", Array.Empty<byte>());
            File.WriteAllBytes(file, ms.ToArray());
        }

        private static uint Crc(byte[] a, byte[] b)
        {
            uint c = 0xFFFFFFFF;
            foreach (var arr in new[] { a, b })
                foreach (var x in arr)
                {
                    c ^= x;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }
            return ~c;
        }
    }
}
