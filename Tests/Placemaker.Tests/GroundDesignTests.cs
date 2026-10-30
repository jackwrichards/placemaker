// The designed ground (docs/DECALS-PLAYBOOK.md section 3, docs/runbooks/sportsPark.md): library instances survive the
// record (codec v2, v1 still read), strip end caps, the brushes' placement rules (inside their area, no grid, neighbours
// differ, strips end to end with faded caps on the right side), and every sports park style's ground from its causes.
// PM_WRITE_GROUND=1 renders each style's plan from above with the real variants to .forensics/ground/ (a quick look
// before the game runner); PM_GROUND_ONLY=community limits it to some styles.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public class GroundDesignTests
    {
        private readonly ITestOutputHelper _out;
        public GroundDesignTests(ITestOutputHelper o) { _out = o; }

        private static DecalLibrary _lib;
        internal static DecalLibrary Lib()
        {
            if (_lib != null) return _lib;
            _lib = DecalLibrary.LoadFolder(Path.Combine(TestKit.RepoRoot(), "Recipes", DecalLibrary.FolderName));
            DecalLibrary.Current = _lib;
            return _lib;
        }

        public static readonly string[] Styles = { "community", "stadium", "tennisClub", "neighbourhood", "arena", "centreCourt", "hillside", "cageCourts", "athletics" };

        internal static ComposedPlan Park(string preset, string site, double grime = 100) =>
            Park(preset, site == "block" ? 146f : 216f, site == "block" ? 136f : 196f, site == "slope" ? p => 0.05f * p.y : (Func<float2, float>)null, grime);

        internal static ComposedPlan Park(string preset, float w, float d, Func<float2, float> ground, double grime = 100)
        {
            Lib();
            var gen = GeneratorRegistry.Get("sportsPark");
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, 1);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            editor.ApplyPreset(r, RecipePaths.Root, preset);
            r.Root.Settings.Values[Weathering.GrimeId] = grime;
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 2000u + (++k);
            var ring = DynamicDecalsTests.Block(w, d);
            var s = DynamicDecalsTests.Streets(w, d, ground);
            TestKit.Entered(r, ring, s);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, s, CancellationToken.None);
        }

        private static List<DecalInstance> Instances(ComposedPlan p) => p.Layers.SelectMany(l => l.DecalInstances).ToList();

        // ───────────── the record ─────────────

        [Fact]
        public void InstancesSurviveTheRecordAndV1StillReads()
        {
            var set = new DecalSet();
            set.Strokes.Add(DecalStroke.Box("mud", 3, new float2(1, 2), 0.3f, new float2(4, 3), 0.8f));
            set.Instances.Add(new DecalInstance("leafOak", 3, new float2(120.25f, -40.5f), 1.25f, new float2(1.2f, 0.9f), 0.75f));
            set.Instances.Add(new DecalInstance("pathEdgeWear", DecalVariants.CapStart + 2, new float2(5, 6), -0.5f, new float2(1.05f, 1.1f), 1f));
            set.Instances.Add(new DecalInstance("leafOak", 1, new float2(7, 8), 7f, new float2(1, 1), 0.5f));
            var bytes = DecalCodec.Encode(set);
            Assert.Equal(2, bytes[0]);
            var back = DecalCodec.Decode(bytes);
            Assert.NotNull(back);
            Assert.Single(back.Strokes);
            Assert.Equal(3, back.Instances.Count);
            var a = back.Instances[0];
            Assert.Equal("leafOak", a.Kind); Assert.Equal(3, a.Variant);
            Assert.Equal(120.25f, a.P.x); Assert.Equal(-40.5f, a.P.y);
            Assert.Equal(1.25f, a.Yaw, 3); Assert.Equal(1.2f, a.Scale.x, 3); Assert.Equal(0.9f, a.Scale.y, 3); Assert.Equal(0.75f, a.Opacity, 2);
            Assert.Equal(DecalVariants.CapStart + 2, back.Instances[1].Variant);
            Assert.Equal(7f - 2f * math.PI, back.Instances[2].Yaw, 3);   // yaw kept in 0..2π
            // 18 bytes an instance, each kind's name once
            var none = new DecalSet(); none.Strokes.Add(set.Strokes[0]);
            Assert.Equal(3 * 18 + 2 + "leafOak".Length + 2 + "pathEdgeWear".Length, bytes.Length - DecalCodec.Encode(none).Length);
            // a v1 record (strokes only, no instance block) still reads
            var v1 = DecalCodec.Encode(none).Take(DecalCodec.Encode(none).Length - 6).ToArray();
            v1[0] = 1;
            var old = DecalCodec.Decode(v1);
            Assert.NotNull(old);
            Assert.Single(old.Strokes);
            Assert.Empty(old.Instances);
            // garbage and cut buffers are refused
            Assert.Null(DecalCodec.Decode(bytes.Take(bytes.Length - 2).ToArray()));
            var bad = (byte[])bytes.Clone(); bad[0] = 3;
            Assert.Null(DecalCodec.Decode(bad));
        }

        // ───────────── end caps ─────────────

        [Fact]
        public void StripCapsFadeOneEndAndMatchTheStripAtTheOther()
        {
            var k = Lib().Get("pathEdgeWear");
            var plain = DecalVariants.Bake(k, 2);
            var end = DecalVariants.Bake(k, DecalVariants.CapEnd + 2);
            var start = DecalVariants.Bake(k, DecalVariants.CapStart + 2);
            Assert.Equal(plain.Span, end.Span);
            Assert.Equal(DecalVariants.SpanOf(k, 2), plain.Span);
            Assert.Equal(DecalVariants.SpanOf(k, DecalVariants.CapEnd + 2), end.Span);
            int W = plain.Img.W, H = plain.Img.H;
            double Col(DecalImage img, int x) { double s = 0; for (int y = 0; y < H; y++) s += img.Albedo[(y * W + x) * 4 + 3]; return s; }
            // the open end equals the strip; the faded end is (nearly) gone
            Assert.Equal(Col(plain.Img, 1), Col(end.Img, 1));
            Assert.Equal(Col(plain.Img, W - 2), Col(start.Img, W - 2));
            Assert.True(Col(end.Img, W - 2) < Col(plain.Img, W - 2) * 0.1 + 1, "the end cap does not fade toward +x");
            Assert.True(Col(start.Img, 1) < Col(plain.Img, 1) * 0.1 + 1, "the start cap does not fade toward −x");
            // caps exist for strips and chains only
            Assert.True(DecalVariants.IsCap(k, DecalVariants.CapEnd));
            Assert.False(DecalVariants.IsCap(Lib().Get("leafOak"), DecalVariants.CapEnd));
            Assert.Equal(Lib().Get("leafOak").Variants.Count - 1, DecalVariants.Normalize(Lib().Get("leafOak"), DecalVariants.CapEnd + 50));
        }

        [Fact]
        public void SpanOfMatchesTheBake()
        {
            foreach (var id in new[] { "leafOak", "barePatch", "pathEdgeWear", "crackAsphalt", "lawnTone", "lawnTonePatch", "mowingStripes" })
            {
                var k = Lib().Get(id);
                Assert.NotNull(k);
                var b = DecalVariants.Bake(k, 1);
                var s = DecalVariants.SpanOf(k, 1);
                Assert.True(math.all(math.abs(b.Span - s) < 1e-3f), $"{id}: bake {b.Span} span {s}");
            }
        }

        // ───────────── brushes ─────────────

        private static float2[] Square(float s) => new[] { new float2(0, 0), new float2(s, 0), new float2(s, s), new float2(0, s) };

        [Fact]
        public void ScatterStaysInItsAreaClumpsAndNeverRepeatsANeighbour()
        {
            var ctx = new BrushContext(Lib(), 77);
            var area = BrushArea.Of(Square(120), new[] { new[] { new float2(40, 40), new float2(80, 40), new float2(80, 80), new float2(40, 80) } });
            int n = Brushes.Scatter(ctx, area, "leafOak", 0.6f, new ScatterOptions { Cause = "t" });
            _out.WriteLine($"{n} leaf patches");
            Assert.InRange(n, 40, 600);
            Assert.All(ctx.Out, d => Assert.True(area.Contains(d.P), $"outside: {d}"));
            // neighbours closer than half a patch never share a variant
            var k = Lib().Get("leafOak");
            int same = 0, close = 0;
            for (int i = 0; i < ctx.Out.Count; i++)
                for (int j = i + 1; j < ctx.Out.Count; j++)
                    if (math.distance(ctx.Out[i].P, ctx.Out[j].P) < 2.5f) { close++; if (ctx.Out[i].Variant == ctx.Out[j].Variant) same++; }
            _out.WriteLine($"{close} close pairs, {same} share a variant");
            Assert.True(same <= close / 6 + 1, $"{same} of {close} close neighbours share a variant");
            // no grid: nearest-neighbour distances vary a lot (clumps and gaps)
            var nn = ctx.Out.Select(a => ctx.Out.Where(b => !b.Equals(a)).Min(b => math.distance(a.P, b.P))).ToList();
            double mean = nn.Average(), sd = Math.Sqrt(nn.Average(x => (x - mean) * (x - mean)));
            _out.WriteLine($"nearest neighbour {mean:F1} m ± {sd:F1}");
            Assert.True(sd / mean > 0.35, $"too even: cv {sd / mean:F2}");
            // turns, scales and opacities spread as the playbook says
            Assert.True(ctx.Out.Select(d => (int)(d.Yaw * 4)).Distinct().Count() > 12);
            Assert.All(ctx.Out, d => { Assert.InRange(d.Scale.x, 0.45f, 1.6f); Assert.InRange(d.Opacity, 0.3f, 1f); });
            // deterministic
            var again = new BrushContext(Lib(), 77);
            Brushes.Scatter(again, area, "leafOak", 0.6f, new ScatterOptions { Cause = "t" });
            Assert.Equal(ctx.Out.Select(d => d.ToString()), again.Out.Select(d => d.ToString()));
        }

        [Fact]
        public void ScatterFollowsItsCause()
        {
            var ctx = new BrushContext(Lib(), 5);
            Brushes.Scatter(ctx, BrushArea.Of(Square(100)), "barePatch", 0.4f, new ScatterOptions { Weight = p => p.x < 30f ? 1f : 0.05f });
            int west = ctx.Out.Count(d => d.P.x < 30f), east = ctx.Out.Count - west;
            _out.WriteLine($"west {west}, east {east}");
            Assert.True(west > east * 2, $"west {west} east {east}");
        }

        [Fact]
        public void ToneCoversTheWholeAreaWithoutAPattern()
        {
            var ctx = new BrushContext(Lib(), 9);
            var area = BrushArea.Of(Square(140));
            int n = Brushes.Tone(ctx, area, "lawnTonePatch", new float2(0.4f, 0.7f));
            var k = Lib().Get("lawnTonePatch");
            int bare = 0, all = 0;
            for (float x = 2; x < 140; x += 4)
                for (float z = 2; z < 140; z += 4)
                {
                    all++;
                    var p = new float2(x, z);
                    if (!ctx.Out.Any(d => math.distance(d.P, p) < math.cmin(DecalVariants.FootprintOf(k, d.Variant) * d.Scale) * 0.5f)) bare++;
                }
            _out.WriteLine($"{n} tone patches, {bare} of {all} points uncovered");
            Assert.True(bare < all * 0.05f, $"{bare} of {all} points untouched");
            Assert.True(ctx.Out.Select(d => d.Variant).Distinct().Count() >= 5);
        }

        [Fact]
        public void EdgeStripsJoinEndToEndWithCapsOnTheRightSide()
        {
            var lib = Lib();
            var k = lib.Get("pathEdgeWear");
            var line = new[] { new float2(0, 0), new float2(60, 0), new float2(60, 50) };   // one sharp corner
            foreach (int side in new[] { 1, -1 })
            {
                var ctx = new BrushContext(lib, 11);
                Brushes.EdgeStrip(ctx, line, "pathEdgeWear", new StripOptions { Side = side, Gaps = 0f });
                var first = ctx.Out.Where(d => d.P.y < 1f && d.P.x < 59f).OrderBy(d => d.P.x).ToList();
                Assert.True(first.Count >= 2);
                // caps: the run starts fading toward its start and ends fading toward its end
                Assert.True(DecalVariants.IsCap(k, first[0].Variant) && DecalVariants.IsCap(k, first[first.Count - 1].Variant));
                bool startFadesMinusX = first[0].Variant >= DecalVariants.CapStart;
                // local +x of the first piece
                var xd = new float2(math.cos(first[0].Yaw), -math.sin(first[0].Yaw));
                // the faded end must lie toward the run's start (x = 0)
                float fadedEnd = startFadesMinusX ? -1f : 1f;
                Assert.True(math.dot(xd * fadedEnd, new float2(-1, 0)) > 0.9f, $"side {side}: the start cap fades the wrong way");
                // the heavy side (+z) is where Side asks: +1 left of travel (+y here), −1 right
                var zd = new float2(math.sin(first[0].Yaw), math.cos(first[0].Yaw));
                Assert.True(zd.y * side > 0.9f, $"side {side}: heavy side {zd}");
                // the pieces of a run join end to end
                for (int i = 1; i < first.Count; i++)
                {
                    float a = first[i - 1].P.x + DecalVariants.SpanOf(k, first[i - 1].Variant).x * first[i - 1].Scale.x * 0.5f;
                    float b = first[i].P.x - DecalVariants.SpanOf(k, first[i].Variant).x * first[i].Scale.x * 0.5f;
                    Assert.True(math.abs(a - b) < 0.05f, $"gap {b - a:F2} m between pieces {i - 1} and {i}");
                    Assert.Equal(DecalVariants.BaseIndex(k, first[0].Variant), DecalVariants.BaseIndex(k, first[i].Variant));
                }
                // the corner cuts the line: a second stretch with its own caps
                Assert.Contains(ctx.Out, d => d.P.x > 59f && d.P.y > 5f);
            }
        }

        [Fact]
        public void CrackChainsMixVariantsAndBreakIntoRuns()
        {
            var ctx = new BrushContext(Lib(), 13);
            Brushes.CracksAlong(ctx, new[] { new float2(0, 0), new float2(200, 10) }, "crackAsphalt");
            _out.WriteLine(string.Join(" ", ctx.Out.Select(d => d.Variant)));
            Assert.True(ctx.Out.Count >= 4);
            Assert.True(ctx.Out.Select(d => DecalVariants.BaseIndex(Lib().Get("crackAsphalt"), d.Variant)).Distinct().Count() >= 3);
            Assert.True(ctx.Out.Count(d => DecalVariants.IsCap(Lib().Get("crackAsphalt"), d.Variant)) >= 4, "runs end in caps");
        }

        [Fact]
        public void FillStaysInTheHollowAndAtMakesASkewedSpot()
        {
            var ctx = new BrushContext(Lib(), 17);
            var hollow = new[] { new float2(0, 0), new float2(8, -1), new float2(10, 2), new float2(6, 4), new float2(1, 3) };
            int n = Brushes.Fill(ctx, hollow, "puddlePiece", new float2(0.7f, 1f));
            Assert.InRange(n, 1, 12);
            Assert.All(ctx.Out, d => Assert.True(Poly.Contains(hollow, d.P)));
            var at = new BrushContext(Lib(), 17);
            Brushes.At(at, new float2(50, 50), new float2(1, 0), "barePatch", 12f, new float2(0.7f, 1f), 3);
            Assert.Equal(4, at.Out.Count);
            var k = Lib().Get("barePatch");
            Assert.InRange(DecalVariants.FootprintOf(k, at.Out[0].Variant).x * at.Out[0].Scale.x, 11.9f, 12.1f);
            Assert.True(at.Out.Skip(1).Average(d => d.P.x) > 50f, "satellites are not thrown along dir");
        }

        // ───────────── every sports park style ─────────────

        public static IEnumerable<object[]> StyleSites()
        {
            foreach (var s in Styles) foreach (var site in new[] { "block", "big", "slope" }) yield return new object[] { s, site };
        }

        [Theory]
        [MemberData(nameof(StyleSites))]
        public void EverySportsParkStyleGetsADesignedGround(string style, string site)
        {
            var sw = Stopwatch.StartNew();
            var plan = Park(style, site);
            sw.Stop();
            Assert.False(plan.HasErrors);
            var w = plan.Weathering;
            Assert.NotNull(w);
            var list = Instances(plan);
            _out.WriteLine($"{style}/{site}: compose {sw.ElapsedMilliseconds} ms; {w}");
            Assert.Equal(w.Instances, list.Count);
            Assert.InRange(list.Count, 400, Weathering.MaxInstances);
            // tone everywhere; seams, leaves and play where there are paths, trees and fields
            Assert.True(w.ByCause.ContainsKey("tone:lawn"), "no lawn tone");
            if (plan.Layers.Any(l => l.Objects.Any(o => o.Role == ObjectRole.Tree))) Assert.True(w.ByCause.ContainsKey("leaves"), "no leaves under the trees");
            if (plan.Layers.Any(l => l.Nets.Any(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide))) Assert.True(w.ByCause.ContainsKey("seam:path"), "no worn path edges");
            // most of the site is touched by a cause, yet few places are strong
            Assert.True(w.Strong <= 0.25f, $"strong {w.Strong:P1}");
            // every instance names a kind and a variant the library has; nothing is centred on a trunk
            var lib = Lib();
            foreach (var d in list)
            {
                var k = lib.Get(d.Kind);
                Assert.True(k != null, d.Kind);
                Assert.Equal(DecalVariants.Normalize(k, d.Variant), d.Variant);
            }
            var trunks = plan.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Tree).Select(o => o.Position.xz).ToList();
            Assert.True(list.Count(d => d.Kind.StartsWith("leaf") && trunks.Any(t => math.distance(t, d.P) < 0.3f)) <= Math.Max(3, trunks.Count / 8), "leaves stamped on trunks");
            // the record stays small
            var set = new DecalSet(); set.Instances.AddRange(list);
            int bytes = DecalCodec.Encode(set).Length;
            _out.WriteLine($"  {bytes / 1024} KiB on the record, weathering {w.Ms:F0} ms");
            Assert.True(bytes < 200 * 1024);
            Assert.True(w.Ms < 4000, $"weathering took {w.Ms:F0} ms");
            if (Environment.GetEnvironmentVariable("PM_WRITE_GROUND") == "1" && Wanted(style) && site != "slope") Picture(plan, $"{style}-{site}");
        }

        [Fact]
        public void GroundIsDeterministicAndFollowsTheGrimeAmount()
        {
            var a = Instances(Park("community", "block"));
            var b = Instances(Park("community", "block"));
            Assert.Equal(a.Select(d => d.ToString()), b.Select(d => d.ToString()));
            var none = Instances(Park("community", "block", grime: 0));
            Assert.True(none.Count > 0 && none.All(d => d.Cause.StartsWith("tone")), "Grime 0 leaves only the tone");
            var lots = Instances(Park("community", "block", grime: 200));
            float Sum(List<DecalInstance> l) => l.Where(d => !d.Cause.StartsWith("tone")).Sum(d => d.Opacity * d.Scale.x * d.Scale.y);
            _out.WriteLine($"100 %: {a.Count}, 0 %: {none.Count}, 200 %: {lots.Count}");
            Assert.True(Sum(lots) > Sum(a) * 1.3f, "Grime 200 % is not grimier");
        }

        // ───────────── tiny and huge selections ─────────────

        [Theory]
        [InlineData("community", 20f, 20f)]
        [InlineData("community", 45f, 30f)]
        [InlineData("arena", 30f, 25f)]
        [InlineData("community", 600f, 600f)]
        [InlineData("community", 1000f, 1000f)]
        [InlineData("cageCourts", 700f, 500f)]
        public void TinyAndHugeSitesStayWithinTheirBudget(string style, float w, float d)
        {
            var sw = Stopwatch.StartNew();
            var plan = Park(style, w, d, null);
            sw.Stop();
            var wr = plan.Weathering;
            Assert.NotNull(wr);
            var list = Instances(plan);
            float ha = w * d / 10000f;
            int cap = (int)Math.Clamp(ha * Weathering.PerHectare, 150f, Weathering.MaxInstances);
            _out.WriteLine($"{style} {w}x{d} ({ha:F1} ha): compose {sw.ElapsedMilliseconds} ms, weathering {wr.Ms:F0} ms, {list.Count} instances (cap {cap}), density {wr.Density:P0}"); _out.WriteLine("  " + wr);
            Assert.InRange(list.Count, 1, cap);
            Assert.True(wr.Ms < 15000, $"weathering took {wr.Ms:F0} ms");
            var set = new DecalSet(); set.Instances.AddRange(list);
            Assert.NotNull(DecalCodec.Decode(DecalCodec.Encode(set)));
            // a thinned site keeps every cause: none of them is cut off by the budget
            if (ha > 20f) foreach (var c in new[] { "tone:lawn", "leaves" }) Assert.True(wr.ByCause.ContainsKey(c), c);
        }

        // ───────────── pictures (PM_WRITE_GROUND=1) ─────────────

        private static bool Wanted(string style)
        {
            var only = Environment.GetEnvironmentVariable("PM_GROUND_ONLY");
            return string.IsNullOrEmpty(only) || only.Split(',').Contains(style);
        }

        private static readonly ConcurrentDictionary<string, BakedVariant> _baked = new ConcurrentDictionary<string, BakedVariant>();

        internal void Picture(ComposedPlan plan, string name)
        {
            var lib = Lib();
            var list = Instances(plan);
            var sw = Stopwatch.StartNew();
            var ids = list.Select(d => d.Kind + "#" + d.Variant).Distinct().ToList();
            Parallel.ForEach(ids, id =>
            {
                if (_baked.ContainsKey(id)) return;
                DecalVariants.TryParseId(id, out var kind, out int v);
                _baked[id] = DecalVariants.Bake(lib.Get(kind), v);
            });
            _out.WriteLine($"  baked {ids.Count} variants in {sw.ElapsedMilliseconds} ms");
            var dir = Path.Combine(TestKit.RepoRoot(), ".forensics", "ground");
            Directory.CreateDirectory(dir);
            var ring = plan.Footprints.Count > 0 ? plan.Footprints[0] : null;
            Poly.Bounds(ring, out var lo, out var hi);
            Render(plan, list, lo - 4f, hi + 4f, 0.1f, Path.Combine(dir, name + "-top.png"));
            // close looks at a few causes
            foreach (var cause in new[] { "play:goalmouth", "flaw:baldGoalmouth", "leaves:drift", "seam:path", "play:courtCorner", "water:puddle", "feet:gate", "leaves" })
            {
                var d = list.FirstOrDefault(x => x.Cause == cause);
                if (d.Kind == null) continue;
                Render(plan, list, d.P - 14f, d.P + 14f, 0.025f, Path.Combine(dir, $"{name}-{cause.Replace(':', '-')}.png"));
            }
        }

        private static void Render(ComposedPlan plan, List<DecalInstance> list, float2 lo, float2 hi, float mpp, string file)
        {
            var lib = Lib();
            int W = (int)((hi.x - lo.x) / mpp), H = (int)((hi.y - lo.y) / mpp);
            var px = new float3[W * H];
            // the painted lawn, the surfaces, then decals on terrain, the paths, decals on roads
            var grass = new float3(0.33f, 0.43f, 0.2f);
            for (int i = 0; i < px.Length; i++) px[i] = grass;
            foreach (var l in plan.Layers)
                foreach (var s in l.Surfaces)
                {
                    var r = Poly.To2D(s.Ring);
                    string cat = (s.Category ?? "") + (s.Asset.Id ?? "");
                    var col = s.Role == SurfaceRole.Asphalt ? (cat.Contains("dirt", StringComparison.OrdinalIgnoreCase) ? new float3(0.72f, 0.42f, 0.28f) : new float3(0.32f, 0.33f, 0.34f))
                        : s.Role == SurfaceRole.Grass ? new float3(0.29f, 0.47f, 0.17f) : s.Role == SurfaceRole.Ground ? new float3(0.6f, 0.55f, 0.45f) : new float3(0.58f, 0.56f, 0.52f);
                    Fill(px, W, H, lo, mpp, r, col);
                }
            var order = list.Select((d, i) => (d, i, k: lib.Get(d.Kind))).Where(x => x.k != null).OrderBy(x => x.k.Order).ThenBy(x => x.i).ToList();
            foreach (var x in order) if ((x.k.Layers & DecalLayerBits.Terrain) != 0) Draw(px, W, H, lo, mpp, x.d, x.k);
            foreach (var l in plan.Layers)
                foreach (var n in l.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide && n.Role != NetRole.DriveAisle) continue;
                    float hw = n.Role == NetRole.DriveAisle ? 3f : n.Role == NetRole.PathwayWide ? 2.5f : 1.5f;
                    for (float t = 0; t <= 1f; t += 0.004f)
                    {
                        float u = 1 - t;
                        var p = (u * u * u * n.A + 3 * u * u * t * n.B + 3 * u * t * t * n.C + t * t * t * n.D).xz;
                        Disc(px, W, H, lo, mpp, p, hw, n.Role == NetRole.DriveAisle ? new float3(0.3f, 0.3f, 0.31f) : new float3(0.66f, 0.64f, 0.6f));
                    }
                }
            // decals that lie on roads too are drawn again over the paths
            foreach (var x in order) if ((x.k.Layers & DecalLayerBits.Roads) != 0) Draw(px, W, H, lo, mpp, x.d, x.k, onlyOver: new float3(0.66f, 0.64f, 0.6f));
            // trees as dark dots (where the trunks are)
            foreach (var l in plan.Layers) foreach (var o in l.Objects) if (o.Role == ObjectRole.Tree) Disc(px, W, H, lo, mpp, o.Position.xz, 0.5f, new float3(0.2f, 0.15f, 0.1f));
            var rgb = new byte[W * H * 3];
            for (int y = 0; y < H; y++)
                for (int xx = 0; xx < W; xx++)
                {
                    var c = px[y * W + xx];
                    int o = ((H - 1 - y) * W + xx) * 3;
                    rgb[o] = (byte)math.clamp(c.x * 255f, 0, 255); rgb[o + 1] = (byte)math.clamp(c.y * 255f, 0, 255); rgb[o + 2] = (byte)math.clamp(c.z * 255f, 0, 255);
                }
            DecalLibraryTests.Png(file, W, H, rgb);
        }

        private static void Draw(float3[] px, int W, int H, float2 lo, float mpp, DecalInstance d, DecalKind k, float3? onlyOver = null)
        {
            if (!_baked.TryGetValue(d.Kind + "#" + d.Variant, out var b)) return;
            var size = b.Span * d.Scale;
            var he = d.HalfExtent(b.Span);
            int x0 = math.max(0, (int)((d.P.x - he.x - lo.x) / mpp)), x1 = math.min(W - 1, (int)((d.P.x + he.x - lo.x) / mpp));
            int y0 = math.max(0, (int)((d.P.y - he.y - lo.y) / mpp)), y1 = math.min(H - 1, (int)((d.P.y + he.y - lo.y) / mpp));
            float c = math.cos(d.Yaw), s = math.sin(d.Yaw);
            var img = b.Img;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = lo + new float2(x + 0.5f, y + 0.5f) * mpp - d.P;
                    var l = new float2(q.x * c - q.y * s, q.x * s + q.y * c);
                    int tx = (int)((l.x / size.x + 0.5f) * img.W), ty = (int)((l.y / size.y + 0.5f) * img.H);
                    if (tx < 0 || ty < 0 || tx >= img.W || ty >= img.H) continue;
                    int i = y * W + x;
                    if (onlyOver.HasValue && math.distance(px[i], onlyOver.Value) > 0.02f) continue;
                    int qq = (ty * img.W + tx) * 4;
                    float a = img.Albedo[qq + 3] / 255f * d.Opacity;
                    if (a <= 0.002f) continue;
                    var col = new float3(img.Albedo[qq], img.Albedo[qq + 1], img.Albedo[qq + 2]) / 255f;
                    px[i] = math.lerp(px[i], col, a);
                }
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
    }
}
