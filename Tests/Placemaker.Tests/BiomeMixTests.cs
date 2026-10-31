// Biome mix (docs/runbooks/biomes.md): a site split into natural areas by the player's percentages. Each biome at 100 % is
// what it says (a really rocky place, a real forest, a flower field), a 50/50 mix is two clear areas, the shares come out as
// asked, it is deterministic and quick, and every park style plans it. PM_WRITE_BIOMES=1 draws each plan from above (biome
// colours, paths, trees, bushes, rocks) to .forensics/biomes/ (PM_BIOMES_ONLY=<name part> limits it).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Biomes;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class BiomeMixTests
    {
        private readonly ITestOutputHelper _out;
        public BiomeMixTests(ITestOutputHelper o) { _out = o; }

        private static Func<float2, float> Ground(string site) => site == "slope" ? p => 0.06f * p.y
            : site == "hills" ? (Func<float2, float>)(p => 4f * math.sin(p.x / 45f) * math.cos(p.y / 38f) + 1.5f * math.sin(p.x / 17f + p.y / 23f)) : null;

        /// <summary>A park with the given mix (biome id → share; the others 0) on a w × d block.</summary>
        internal static ComposedPlan Park(string style, float w, float d, string site, (string id, double share)[] mix, double scale = -1, uint seed = 1)
        {
            GroundDesignTests.Lib();
            var gen = GeneratorRegistry.Get("park");
            var editor = new Placemaker.Core.Tool.DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, seed);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            if (style != null) editor.ApplyPreset(r, RecipePaths.Root, style);
            if (mix != null)
            {
                foreach (var (id, _) in ParkLayer.Biomes.Biomes) r.Root.Settings.Values[BiomeMix.ControlIdOf(id)] = 0.0;
                foreach (var (id, share) in mix) r.Root.Settings.Values[BiomeMix.ControlIdOf(id)] = share;
            }
            if (scale >= 0) r.Root.Settings.Values[BiomeMix.ScaleId] = scale;
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = seed * 1000u + (++k);
            var ring = DynamicDecalsTests.Block(w, d);
            var s = DynamicDecalsTests.Streets(w, d, Ground(site));
            TestKit.Entered(r, ring, s);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, s, System.Threading.CancellationToken.None);
        }

        /// <summary>A design with no natural areas (its own lawn only): the type plans as it did before Biome mix.</summary>
        internal static SettingsBag LawnOnly(SettingsBag s)
        {
            foreach (var b in BiomeLibrary.All) s.Values[b.ControlId] = b.Host ? 100.0 : 0.0;
            return s;
        }

        private static AreaPlan Host(ComposedPlan p) => p.Layers.First(l => l.GeneratorId == "park");
        private static double F(AreaPlan h, string k) => h.Facts.TryGetValue(k, out var v) ? v : 0;

        private string Report(string name, ComposedPlan plan, long ms)
        {
            var h = Host(plan);
            var shares = string.Join(" ", h.Facts.Where(f => f.Key.StartsWith(BiomeMix.FactPrefix)).Select(f => f.Key.Substring(6) + " " + f.Value + "%"));
            string line = $"{name}: {ms} ms (biomes {F(h, "biomeMs")}: field {F(h, "biomeMsField")} shape {F(h, "biomeMsShape")} objects {F(h, "biomeMsObjects")}) | {shares} | natural {F(h, "biomeNaturalArea")} m² | trees {F(h, "biomeTrees")} bushes {F(h, "biomeBushes")} crowns {F(h, "biomeCrowns")} boulders {F(h, "biomeBoulders")} stones {F(h, "biomeStones")} logs {F(h, "biomeLogs")} ponds {F(h, "biomePonds")} | raise {F(h, "biomeTerrainRaise")} lower {F(h, "biomeTerrainLower")} | objects {h.Objects.Count} decals {plan.Weathering?.Instances}";
            _out.WriteLine(line);
            if (Environment.GetEnvironmentVariable("PM_WRITE_BIOMES") == "1")
            {
                string only = Environment.GetEnvironmentVariable("PM_BIOMES_ONLY");
                if (string.IsNullOrEmpty(only) || name.Contains(only)) Draw(plan, name);
            }
            return line;
        }

        public static IEnumerable<object[]> Each() { foreach (var b in ParkLayer.Biomes.Biomes) yield return new object[] { b.id }; }

        /// <summary>Each biome at 100 % covers (nearly) all of the natural ground and lays what it is made of.</summary>
        [Theory]
        [MemberData(nameof(Each))]
        public void EachBiomeAtFullIsWhatItSays(string id)
        {
            var sw = Stopwatch.StartNew();
            var plan = Park("natural", 216f, 196f, "flat", new[] { (id, 100.0) });
            sw.Stop();
            var h = Host(plan);
            Report("full-" + id, plan, sw.ElapsedMilliseconds);
            Assert.False(plan.HasErrors);
            double ha = F(h, "biomeNaturalArea") / 10000.0;
            if (id == "lawn") { Assert.False(h.Facts.ContainsKey("biomeMs"), "100 % lawn plans the park as it always was"); return; }
            var d = BiomeLibrary.Get(id);
            Assert.True(F(h, BiomeMix.FactPrefix + id) >= (d.Water ? 55 : d.PathKeep > 0f ? 88 : 95), $"{id} got {F(h, BiomeMix.FactPrefix + id)} %");
            if (d.CrownsPerHa > 0) Assert.True(F(h, "biomeCrowns") >= d.CrownsPerHa * ha * 0.5, "crowns " + F(h, "biomeCrowns"));
            if (d.BouldersPerHa > 20) Assert.True(F(h, "biomeBoulders") >= d.BouldersPerHa * ha * 0.6, "boulders " + F(h, "biomeBoulders"));
            if (d.TreesPerHa >= 100) Assert.True(F(h, "biomeTrees") >= d.TreesPerHa * ha * 0.5, "trees " + F(h, "biomeTrees"));
            if (d.Water) Assert.True(F(h, "biomePonds") >= 1, "no pond");
            if (d.Water) return;   // water keeps off the walks: it never takes the whole site
            if (d.Lift > 1f) Assert.True(F(h, "biomeTerrainRaise") >= 1.2, "rocky ground not lifted: " + F(h, "biomeTerrainRaise"));
            var w = plan.Weathering;
            if (d.Decals.Length > 0) Assert.True(w.ByCause.Keys.Any(c => c.StartsWith("biome:" + id)), "no decals of " + id + ": " + string.Join(",", w.ByCause.Keys));
        }

        /// <summary>The shares come out as asked (within a few points), on flat land and on hills.</summary>
        [Theory]
        [InlineData("flat")]
        [InlineData("hills")]
        public void SharesComeOutAsAsked(string site)
        {
            var mix = new[] { ("lawn", 40.0), ("forest", 25.0), ("rocky", 15.0), ("meadow", 12.0), ("wetland", 8.0) };
            var sw = Stopwatch.StartNew();
            var plan = Park("natural", 216f, 196f, site, mix);
            Report("shares-" + site, plan, sw.ElapsedMilliseconds);
            var h = Host(plan);
            foreach (var (id, share) in mix) Assert.InRange(F(h, BiomeMix.FactPrefix + id), share - 7, share + 7);
        }

        /// <summary>A 50/50 mix at the biggest patch size is two clear areas (few patches).</summary>
        [Fact]
        public void FiftyFiftyIsTwoClearAreas()
        {
            var plan = Park("natural", 216f, 196f, "flat", new[] { ("forest", 50.0), ("meadow", 50.0) }, 100);
            var h = Host(plan);
            Report("fifty", plan, 0);
            int forest = h.Regions.Count(r => r.Tags.Contains(BiomeBuilder.RegionPrefix + "forest") && Poly.Area(Poly.To2D(r.Ring)) > 600f);
            int meadow = h.Regions.Count(r => r.Tags.Contains(BiomeBuilder.RegionPrefix + "meadow") && Poly.Area(Poly.To2D(r.Ring)) > 600f);
            _out.WriteLine($"big areas: forest {forest}, meadow {meadow}");
            // the paths cut the site into rooms, so an area may be a few pieces; never a speckle
            Assert.InRange(forest, 1, 8);
            Assert.InRange(meadow, 1, 8);
        }

        /// <summary>Patch size changes the plan (many small patches .. a few big areas).</summary>
        [Fact]
        public void PatchSizeChangesThePieces()
        {
            int Pieces(double scale) => Host(Park("natural", 216f, 196f, "flat", new[] { ("forest", 50.0), ("meadow", 50.0) }, scale)).Regions.Count(r => r.Tags.Any(t => t.StartsWith(BiomeBuilder.RegionPrefix)));
            int small = Pieces(0), big = Pieces(100);
            _out.WriteLine($"pieces: small patches {small}, big areas {big}");
            Assert.True(small > big * 1.5, $"small {small} vs big {big}");
        }

        [Fact]
        public void Deterministic()
        {
            var a = Park("natural", 146f, 136f, "flat", null);
            var b = Park("natural", 146f, 136f, "flat", null);
            Assert.Equal(Host(a).Objects.Count, Host(b).Objects.Count);
            Assert.Equal(F(Host(a), "biome.forest"), F(Host(b), "biome.forest"));
            for (int i = 0; i < Host(a).Objects.Count; i++) Assert.Equal(Host(a).Objects[i].Position, Host(b).Objects[i].Position);
        }

        /// <summary>Every shown park style plans its mix on a block, a big site and hills, quickly.</summary>
        [Theory]
        [InlineData("natural")] [InlineData("garden")] [InlineData("urban")] [InlineData("picnic")] [InlineData("wild")] [InlineData("neglected")] [InlineData("rocky")] [InlineData("woodland")]
        public void EveryStylePlansItsMix(string style)
        {
            foreach (var (site, w, d) in new[] { ("block", 146f, 136f), ("big", 400f, 300f), ("hills", 260f, 220f) })
            {
                var sw = Stopwatch.StartNew();
                var plan = Park(style, w, d, site == "hills" ? "hills" : "flat", null);
                sw.Stop();
                Report($"{style}-{site}", plan, sw.ElapsedMilliseconds);
                Assert.False(plan.HasErrors);
                Assert.True(F(Host(plan), "biomeMs") < 2500, "biomes took " + F(Host(plan), "biomeMs") + " ms");
            }
        }

        /// <summary>The park's own trees and bushes stay off the natural areas (they plant their own).</summary>
        [Fact]
        public void TheHostKeepsOffTheAreas()
        {
            var plan = Park("natural", 216f, 196f, "flat", new[] { ("lawn", 50.0), ("rocky", 50.0) });
            var h = Host(plan);
            var rocky = h.Regions.Where(r => r.Tags.Contains(BiomeBuilder.RegionPrefix + "rocky")).Select(r => Poly.To2D(r.Ring)).ToList();
            int conifer = 0, broad = 0;
            foreach (var o in h.Objects.Where(o => o.Role == ObjectRole.Tree))
            {
                bool inRock = rocky.Any(r => Poly.Contains(r, o.Position.xz) && Poly.DistanceToBoundary(r, o.Position.xz) > 4f);
                if (!inRock) continue;
                string id = o.Asset.Id ?? "";
                if (id.Contains("Pine") || id.Contains("Spruce") || id.Contains("Birch")) conifer++; else broad++;
            }
            _out.WriteLine($"trees deep in the rocks: biome's {conifer}, others {broad}");
            Assert.True(broad <= 3, "park trees in the rocky ground: " + broad);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        }

        // ───────────── drawing ─────────────

        private static readonly Dictionary<string, float3> Colours = new Dictionary<string, float3>
        {
            ["lawn"] = new float3(0.45f, 0.62f, 0.3f), ["meadow"] = new float3(0.76f, 0.7f, 0.38f), ["flowers"] = new float3(0.78f, 0.45f, 0.7f),
            ["mulch"] = new float3(0.45f, 0.3f, 0.2f), ["glade"] = new float3(0.37f, 0.6f, 0.3f), ["forest"] = new float3(0.18f, 0.4f, 0.22f),
            ["rocky"] = new float3(0.6f, 0.57f, 0.53f), ["heath"] = new float3(0.78f, 0.64f, 0.42f), ["wetland"] = new float3(0.3f, 0.55f, 0.52f),
            ["water"] = new float3(0.25f, 0.5f, 0.75f),
        };

        private void Draw(ComposedPlan plan, string name)
        {
            var h = Host(plan);
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var r in h.Regions) foreach (var p in r.Ring) { lo = math.min(lo, p.xz); hi = math.max(hi, p.xz); }
            foreach (var o in h.Objects) { lo = math.min(lo, o.Position.xz); hi = math.max(hi, o.Position.xz); }
            lo -= 4f; hi += 4f;
            float mpp = 0.25f;
            int W = (int)((hi.x - lo.x) / mpp), H = (int)((hi.y - lo.y) / mpp);
            var px = new float3[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new float3(0.42f, 0.55f, 0.32f);
            void Poly1(float2[] r, float3 c, float a)
            {
                Poly.Bounds(r, out var a0, out var a1);
                int x0 = math.max(0, (int)((a0.x - lo.x) / mpp)), x1 = math.min(W - 1, (int)((a1.x - lo.x) / mpp));
                int y0 = math.max(0, (int)((a0.y - lo.y) / mpp)), y1 = math.min(H - 1, (int)((a1.y - lo.y) / mpp));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        var p = lo + new float2(x + 0.5f, y + 0.5f) * mpp;
                        if (Poly.Contains(r, p)) px[y * W + x] = math.lerp(px[y * W + x], c, a);
                    }
            }
            foreach (var r in h.Regions)
                foreach (var t in r.Tags)
                    if (t.StartsWith(BiomeBuilder.RegionPrefix) && Colours.TryGetValue(t.Substring(BiomeBuilder.RegionPrefix.Length), out var c)) Poly1(Poly.To2D(r.Ring), c, 1f);
            foreach (var r in h.Regions)
                foreach (var t in r.Tags)
                    if (t.StartsWith(BiomeBuilder.HolePrefix)) Poly1(Poly.To2D(r.Ring), new float3(0.45f, 0.62f, 0.3f), 1f);
            foreach (var s in h.Surfaces) if (s.Role == SurfaceRole.Water) Poly1(Poly.To2D(s.Ring), new float3(0.15f, 0.35f, 0.7f), 1f);
            void Dot(float2 p, float r, float3 c)
            {
                int cx = (int)((p.x - lo.x) / mpp), cy = (int)((p.y - lo.y) / mpp), rr = math.max(1, (int)(r / mpp));
                for (int y = cy - rr; y <= cy + rr; y++) for (int x = cx - rr; x <= cx + rr; x++)
                        if (x >= 0 && y >= 0 && x < W && y < H && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= rr * rr) px[y * W + x] = c;
            }
            foreach (var l in plan.Layers)
                foreach (var n in l.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    for (int i = 0; i <= 20; i++)
                    {
                        float t = i / 20f, u = 1 - t;
                        var p = u * u * u * n.A.xz + 3 * u * u * t * n.B.xz + 3 * u * t * t * n.C.xz + t * t * t * n.D.xz;
                        Dot(p, 1.2f, new float3(0.85f, 0.83f, 0.78f));
                    }
                }
            foreach (var o in h.Objects)
            {
                if (o.Role == ObjectRole.Tree) Dot(o.Position.xz, 2.2f, o.Age == PlannedAge.Dead ? new float3(0.4f, 0.3f, 0.2f) : new float3(0.08f, 0.25f, 0.1f));
                else if (o.Role == ObjectRole.Bush) Dot(o.Position.xz, 0.9f, new float3(0.2f, 0.42f, 0.18f));
                else if (o.Category == AssetCategories.Rock) Dot(o.Position.xz, math.max(0.4f, 1.2f * o.Scale), new float3(0.85f, 0.85f, 0.85f));
                else Dot(o.Position.xz, 0.6f, new float3(0.9f, 0.2f, 0.2f));
            }
            var rgb = new byte[W * H * 3];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    var c = math.saturate(px[(H - 1 - y) * W + x]) * 255f;
                    int i = (y * W + x) * 3; rgb[i] = (byte)c.x; rgb[i + 1] = (byte)c.y; rgb[i + 2] = (byte)c.z;
                }
            string dir = Path.Combine(RepoRoot(), ".forensics", "biomes");
            Directory.CreateDirectory(dir);
            DecalLibraryTests.Png(Path.Combine(dir, name + ".png"), W, H, rgb);
        }
    }
}
