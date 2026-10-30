// The rundown category's grounds (docs/runbooks/rundown.md): every style of abandoned lot, construction site and urban
// ruins gets library instances from its own causes, within its budget, deterministic, on a block, a big site and a slope.
// PM_WRITE_GROUND=1 renders each plan from above (decals, surfaces, objects as dots) to .forensics/ground/rundown-*.png;
// PM_GROUND_ONLY=abandonedLot-overgrown,... limits it.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class RundownGroundTests
    {
        private readonly ITestOutputHelper _out;
        public RundownGroundTests(ITestOutputHelper o) { _out = o; }

        public static IEnumerable<object[]> Styles()
        {
            foreach (var s in new[] { "overgrown", "rubbleField", "demolished", "squatterCamp", "fencedWasteland", "brokenCarPark", "burntOut", "emptyLot" }) yield return new object[] { "abandonedLot", s };
            foreach (var s in new[] { "foundation", "roadworks", "compound", "demolition", "earthworks" }) yield return new object[] { "constructionSite", s };
            foreach (var s in new[] { "diySpot", "highwayRuins", "parkour", "dirtJumps", "buildingRuins" }) yield return new object[] { "urbanRuins", s };
        }

        internal static ComposedPlan Plan(string type, string preset, float w, float d, Func<float2, float> ground = null, uint seed = 3000u)
        {
            GroundDesignTests.Lib();
            var gen = GeneratorRegistry.Get(type);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, 1);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = seed + (++k);
            var ring = DynamicDecalsTests.Block(w, d);
            var s = DynamicDecalsTests.Streets(w, d, ground);
            TestKit.Entered(r, ring, s);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, s, CancellationToken.None);
        }

        private static List<DecalInstance> Instances(ComposedPlan p) => p.Layers.SelectMany(l => l.DecalInstances).ToList();

        [Theory]
        [MemberData(nameof(Styles))]
        public void EveryRundownStyleGetsADesignedGround(string type, string style)
        {
            foreach (var (site, w, d, g) in new (string, float, float, Func<float2, float>)[] { ("block", 146f, 136f, null), ("big", 216f, 196f, null), ("slope", 146f, 136f, p => 0.05f * p.y) })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var plan = Plan(type, style, w, d, g);
                var list = Instances(plan);
                float ha = w * d / 10000f;
                var causes = list.GroupBy(x => x.Cause).OrderByDescending(x => x.Count()).Select(x => $"{x.Key} {x.Count()}");
                _out.WriteLine($"{type}-{style}-{site}: {list.Count} instances ({list.Count / ha:F0}/ha) in {sw.ElapsedMilliseconds} ms: {string.Join(", ", causes)}");
                Assert.True(list.Count > 60 * ha, $"{type}-{style}-{site}: only {list.Count} instances");
                Assert.True(list.Count <= Weathering.MaxInstances, $"{type}-{style}-{site}: {list.Count} instances");
                if (Environment.GetEnvironmentVariable("PM_WRITE_GROUND") == "1" && Wanted($"{type}-{style}") && site != "slope") Picture(plan, $"rundown-{type}-{style}-{site}");
            }
        }

        /// <summary>The rundown family's share of the shared GPU budget (every variant baked once), kept lean.</summary>
        [Fact]
        public void RundownFamilyStaysLean()
        {
            var lib = GroundDesignTests.Lib();
            long bytes = 0; int n = 0;
            foreach (var id in new[] { "rubbleSpread", "brickDust", "crackWeeds", "weedTufts", "burntPatch", "fadedLine", "soilSpill", "stagnantWater", "oilStainOld" })
            {
                var k = lib.Get(id);
                Assert.NotNull(k);
                Assert.Equal("rundown", k.Family);
                for (int i = 0; i < k.Variants.Count; i++) { var b = DecalVariants.Bake(k, i); bytes += DecalVariants.GpuBytes(b.Img.W, b.Img.H); n++; }
            }
            _out.WriteLine($"rundown family: {n} variants, {bytes / 1048576.0:F1} MiB on the GPU");
            Assert.True(bytes <= 14L * 1024 * 1024, $"rundown family {bytes / 1048576.0:F1} MiB");
        }

        /// <summary>Construction sites plan real machines: Placemaker's own vehicle props (the old Prop_ names never placed).</summary>
        [Theory]
        [InlineData("foundation")]
        [InlineData("demolition")]
        [InlineData("earthworks")]
        [InlineData("compound")]
        public void ConstructionSitesPlanOurOwnMachines(string style)
        {
            var plan = Plan("constructionSite", style, 150f, 140f);
            var machines = plan.Layers.SelectMany(l => l.Objects).Where(o => o.Category == AssetCategories.ConstructionMachine).ToList();
            foreach (var m in machines) _out.WriteLine($"{style}: {m.Asset.Id} | {string.Join("|", m.Asset.Fallbacks ?? new string[0])}");
            Assert.NotEmpty(machines);
            Assert.All(machines, m => Assert.DoesNotContain("Prop_", m.Asset.Id + string.Join("", m.Asset.Fallbacks ?? new string[0])));
            Assert.Contains(machines, m => (m.Asset.Id ?? "").StartsWith("Placemaker Vehicle ", StringComparison.Ordinal));
            if (style == "foundation" || style == "earthworks")
            {
                // spoil banked beside the dig (the coordinator, round 7: "a big empty dirt field")
                int spoil = plan.Layers.SelectMany(l => l.Objects).Count(o => o.Category == AssetCategories.ConstructionPile && o.Scale >= 1.4f);
                _out.WriteLine($"{style}: {spoil} spoil heaps");
                Assert.True(spoil >= 2, $"{style}: {spoil} spoil heaps");
            }
        }

        /// <summary>The narrowed AutoAssets fix from makeover-industrial (4c97d9ff): only our vehicle props pass the vanilla rule.</summary>
        [Fact]
        public void OnlyOurVehiclePropsCountAsVanilla()
        {
            Assert.True(Placemaker.Core.Build.AutoAssets.IsOwn("Placemaker Vehicle Car 3"));
            Assert.True(Placemaker.Core.Build.AutoAssets.IsOwn(Placemaker.Core.Layers.Types.Yards.VehicleProps.Loader(1)));
            Assert.False(Placemaker.Core.Build.AutoAssets.IsOwn("Placemaker Overlay (Some Pack Surface)"));
            Assert.False(Placemaker.Core.Build.AutoAssets.IsOwn("Placemaker Decal Copy"));
            Assert.False(Placemaker.Core.Build.AutoAssets.IsOwn("Prop_FrontendLoader01"));
            Assert.False(Placemaker.Core.Build.AutoAssets.IsOwn(null));
        }

        /// <summary>The lot is painted bare earth over its whole core (the coordinator, round 7: "lawn everywhere").</summary>
        [Fact]
        public void LotsArePaintedEarthAllOver()
        {
            var plan = Plan("abandonedLot", "rubbleField", 146f, 136f);
            var paints = plan.Layers.SelectMany(l => l.Paint).ToList();
            foreach (var p in paints) _out.WriteLine($"layer {p.Layer} strength {p.Strength:F2} knock {p.Knockout} erase {p.Erase} area {Poly.Area(Poly.To2D(p.Ring)):F0}");
            var stamps = PaintRaster.Build(paints, new PaintRasterOptions { Cell = 1.75f, Align = 3.5f });
            Assert.True(PaintRaster.Sample(stamps, 3, new float2(73f, 68f)) > 0.8f, "the middle of the lot is earth");
        }

        [Fact]
        public void RundownGroundIsDeterministic()
        {
            var a = Instances(Plan("abandonedLot", "squatterCamp", 120f, 100f));
            var b = Instances(Plan("abandonedLot", "squatterCamp", 120f, 100f));
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Kind, b[i].Kind); Assert.Equal(a[i].P, b[i].P); }
        }

        [Theory]
        [InlineData("abandonedLot", "brokenCarPark", 24f, 22f)]
        [InlineData("abandonedLot", "overgrown", 700f, 600f)]
        [InlineData("constructionSite", "demolition", 600f, 500f)]
        public void TinyAndHugeSitesStayWithinTheirBudget(string type, string style, float w, float d)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var list = Instances(Plan(type, style, w, d));
            _out.WriteLine($"{type}-{style} {w}x{d}: {list.Count} instances, whole plan {sw.ElapsedMilliseconds} ms");
            Assert.True(list.Count <= Weathering.MaxInstances);
        }

        // ───────────── pictures ─────────────

        private static bool Wanted(string id)
        {
            var only = Environment.GetEnvironmentVariable("PM_GROUND_ONLY");
            return string.IsNullOrEmpty(only) || only.Split(',').Any(o => id.StartsWith(o, StringComparison.Ordinal));
        }

        private static readonly ConcurrentDictionary<string, BakedVariant> _baked = new ConcurrentDictionary<string, BakedVariant>();

        private void Picture(ComposedPlan plan, string name)
        {
            var lib = GroundDesignTests.Lib();
            var list = Instances(plan);
            var ids = list.Select(d => d.Kind + "#" + d.Variant).Distinct().ToList();
            Parallel.ForEach(ids, id =>
            {
                if (_baked.ContainsKey(id)) return;
                DecalVariants.TryParseId(id, out var kind, out int v);
                _baked[id] = DecalVariants.Bake(lib.Get(kind), v);
            });
            var dir = Path.Combine(TestKit.RepoRoot(), ".forensics", "ground");
            Directory.CreateDirectory(dir);
            var ring = plan.Footprints[0];
            Poly.Bounds(ring, out var lo, out var hi);
            Render(plan, list, lo - 3f, hi + 3f, 0.1f, Path.Combine(dir, name + "-top.png"));
            var c = (lo + hi) * 0.5f;
            Render(plan, list, c - 22f, c + 22f, 0.03f, Path.Combine(dir, name + "-mid.png"));
        }

        private static void Render(ComposedPlan plan, List<DecalInstance> list, float2 lo, float2 hi, float mpp, string file)
        {
            var lib = GroundDesignTests.Lib();
            int W = (int)((hi.x - lo.x) / mpp), H = (int)((hi.y - lo.y) / mpp);
            var px = new float3[W * H];
            var grass = new float3(0.36f, 0.44f, 0.22f);
            for (int i = 0; i < px.Length; i++) px[i] = grass;
            foreach (var l in plan.Layers)
                foreach (var s in l.Surfaces)
                {
                    var col = s.Role == SurfaceRole.Asphalt ? new float3(0.34f, 0.34f, 0.35f) : s.Role == SurfaceRole.Ground ? new float3(0.56f, 0.53f, 0.48f) : new float3(0.55f, 0.55f, 0.53f);
                    Fill(px, W, H, lo, mpp, Poly.To2D(s.Ring), col);
                }
            var order = list.Select((d, i) => (d, i, k: lib.Get(d.Kind))).Where(x => x.k != null).OrderBy(x => x.k.Order).ThenBy(x => x.i).ToList();
            foreach (var x in order) Draw(px, W, H, lo, mpp, x.d);
            foreach (var l in plan.Layers)
                foreach (var o in l.Objects)
                {
                    var col = o.Role == ObjectRole.Tree ? new float3(0.12f, 0.25f, 0.1f) : o.Role == ObjectRole.Bush ? new float3(0.2f, 0.36f, 0.16f) : new float3(0.85f, 0.2f, 0.6f);
                    float r = o.Role == ObjectRole.Tree ? 1.6f : o.Role == ObjectRole.Bush ? 0.8f : 0.6f;
                    Disc(px, W, H, lo, mpp, o.Position.xz, r, col);
                }
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

        private static void Draw(float3[] px, int W, int H, float2 lo, float mpp, DecalInstance d)
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
                    int qq = (ty * img.W + tx) * 4;
                    float a = img.Albedo[qq + 3] / 255f * d.Opacity;
                    if (a <= 0.002f) continue;
                    px[i] = math.lerp(px[i], new float3(img.Albedo[qq], img.Albedo[qq + 1], img.Albedo[qq + 2]) / 255f, a);
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
