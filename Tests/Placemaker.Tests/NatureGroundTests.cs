// The nature types' designed ground (Core/Decals/Dynamic/NatureGround.cs, docs/runbooks/nature.md): every style gets instances
// from its causes within budget on a block, a big site, a slope and hills; rills follow the real flow; hollows hold marsh; the
// plan stays deterministic and planning stays quick on a huge site.
// PM_WRITE_GROUND=1 renders each style's plan from above (hill-shaded, ponds blue) to .forensics/ground/nature-*.png;
// PM_GROUND_ONLY=wetland/reedMarsh,lookoutHill limits it.

using System;
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
    public class NatureGroundTests
    {
        private readonly ITestOutputHelper _out;
        public NatureGroundTests(ITestOutputHelper o) { _out = o; }

        public static readonly (string type, string style)[] Styles =
        {
            ("wetland", "wetlandPark"), ("wetland", "waterGarden"), ("wetland", "reedMarsh"), ("wetland", "rainGarden"), ("wetland", "birdReserve"), ("wetland", "lakeland"),
            ("lookoutHill", "hilltop"), ("lookoutHill", "sledding"), ("lookoutHill", "terraces"), ("lookoutHill", "kiteHill"),
            ("natureReserve", "forest"), ("natureReserve", "heath"),
            ("hikingTrail", "forestTrail"), ("hikingTrail", "meadowWalk"), ("hikingTrail", "ridgeHike"), ("hikingTrail", "canyonTrail"),
            ("greenwayPark", "leafy"), ("campground", "forest"),
        };

        internal static Func<float2, float> Ground(string site) => site switch
        {
            "slope" => p => 0.05f * p.y,
            "hills" => p => 6f * math.sin(p.x / 45f) * math.cos(p.y / 38f) + 0.03f * p.x,
            _ => null,
        };

        internal static ComposedPlan Plan(string type, string preset, float w, float d, Func<float2, float> ground, uint seed = 3000)
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

        private static bool Wanted(string type, string style)
        {
            var only = Environment.GetEnvironmentVariable("PM_GROUND_ONLY");
            return string.IsNullOrEmpty(only) || only.Split(',').Any(o => o == type || o == type + "/" + style);
        }

        [Fact]
        public void EveryStyleGetsItsGroundFromCauses()
        {
            bool write = Environment.GetEnvironmentVariable("PM_WRITE_GROUND") == "1";
            foreach (var (type, style) in Styles)
            {
                if (write && !Wanted(type, style)) continue;
                foreach (var site in write ? new[] { "big", "hills" } : new[] { "block", "big", "hills" })
                {
                    float w = site == "block" ? 146f : 216f, d = site == "block" ? 136f : 196f;
                    var sw = Stopwatch.StartNew();
                    var plan = Plan(type, style, w, d, Ground(site));
                    var list = Instances(plan);
                    var wr = plan.Weathering;
                    _out.WriteLine($"{type}/{style} {site}: plan {sw.ElapsedMilliseconds} ms; trees {string.Join("+", plan.Layers.Select(l => l.GeneratorId + ":" + l.Objects.Count(o => o.Role == ObjectRole.Tree)))}, bushes {plan.Layers.Sum(l => l.Objects.Count(o => o.Role == ObjectRole.Bush))}, rocks {plan.Layers.Sum(l => l.Objects.Count(o => o.Category == "rock"))}; {string.Join(" ", plan.Layers.SelectMany(l => l.Facts).Where(kv => kv.Key.StartsWith("grove")).Select(kv => kv.Key + "=" + kv.Value.ToString("F3")))}; {wr}");
                    Assert.NotNull(wr);
                    Assert.True(list.Count > 80, $"{type}/{style} {site}: only {list.Count} instances");
                    Assert.True(list.Count <= Weathering.MaxInstances);
                    Assert.All(list, x => Assert.True(Poly.Contains(Poly.To2D(DynamicDecalsTests.Block(w, d)), x.P) || Poly.DistanceToBoundary(Poly.To2D(DynamicDecalsTests.Block(w, d)), x.P) < 8f, $"{x.Kind} outside at {x.P}"));
                    if (site == "hills" && type != "wetland") Assert.True(wr.ByCause.ContainsKey("water:rill") || wr.ByCause.ContainsKey("water:bed") || wr.ByCause.ContainsKey("water:washout") || wr.ByCause.ContainsKey("water:fan"), $"{type}/{style} hills: no runoff ({wr})");
                    if (type == "wetland") Assert.True(wr.ByCause.ContainsKey("water:bank"), $"{type}/{style}: no banks");
                    if (write) Picture(plan, Ground(site), $"nature-{type}-{style}-{site}");
                }
            }
        }

        /// <summary>The climb meets the summit plaza head on: no piece near the top turns more than 50° from the one before.</summary>
        [Theory]
        [InlineData("hilltop", 216f, 196f)]
        [InlineData("hilltop", 146f, 136f)]
        [InlineData("kiteHill", 216f, 196f)]
        [InlineData("sledding", 216f, 196f)]
        [InlineData("terraces", 216f, 196f)]
        public void ClimbArrivesHeadOn(string style, float w, float d)
        {
            var plan = Plan("lookoutHill", style, w, d, null);
            var l = plan.Layers.First(x => x.GeneratorId == "lookoutHill");
            var summit = new float2((float)l.Facts["summitX"], (float)l.Facts["summitZ"]);
            var pts = new List<float2>();
            // the climb's pieces in order, sampled
            var nets = l.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            var near = nets.Where(n => math.distance(n.D.xz, summit) < 40f || math.distance(n.A.xz, summit) < 40f).ToList();
            float worst = 0f;
            foreach (var n in near)
            {
                for (int k = 0; k <= 8; k++) pts.Add(Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz);
            }
            // turn between consecutive pieces that meet
            foreach (var a in near)
                foreach (var b in near)
                {
                    if (a == b || math.distance(a.D.xz, b.A.xz) > 0.5f) continue;
                    // a junction (the second entrance's spur joins here) may meet at any angle
                    if (nets.Count(n => math.distance(n.A.xz, a.D.xz) < 0.5f || math.distance(n.D.xz, a.D.xz) < 0.5f) > 2) continue;
                    var ta = math.normalizesafe(a.D.xz - Composer.Bezier(a.A, a.B, a.C, a.D, 0.9f).xz);
                    var tb = math.normalizesafe(Composer.Bezier(b.A, b.B, b.C, b.D, 0.1f).xz - b.A.xz);
                    worst = math.max(worst, math.degrees(math.acos(math.clamp(math.dot(ta, tb), -1f, 1f))));
                }
            // the tightest bend inside the pieces (radius from the turn over each 1/8 of a piece)
            float tight = float.MaxValue;
            foreach (var n in near)
                for (int k = 1; k < 8; k++)
                {
                    var p0 = Composer.Bezier(n.A, n.B, n.C, n.D, (k - 1) / 8f).xz; var p1 = Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz; var p2 = Composer.Bezier(n.A, n.B, n.C, n.D, (k + 1) / 8f).xz;
                    float ang = math.acos(math.clamp(math.dot(math.normalizesafe(p1 - p0), math.normalizesafe(p2 - p1)), -1f, 1f));
                    if (ang > 1e-3f) tight = math.min(tight, (math.distance(p0, p1) + math.distance(p1, p2)) * 0.5f / ang);
                }
            _out.WriteLine($"{style}: tightest bend near the top r = {tight:F1} m, arrival {l.Facts.GetValueOrDefault("arrival0")}");
            Assert.True(tight > 6f, $"{style}: a {tight:F1} m bend near the top");
            foreach (var n in near) _out.WriteLine($"piece {n.A.xz} -> {n.D.xz} (to summit {math.distance(n.D.xz, summit):F1} m)");
            _out.WriteLine($"{style} {w}x{d}: worst turn near the top {worst:F0}°");
            Assert.True(worst < 50f, $"{style}: a {worst:F0}° turn near the top");
        }

        /// <summary>The nature family's share of the GPU if every variant were made (the game makes only what a city uses).</summary>
        [Fact]
        public void NatureFamilyStaysLean()
        {
            long total = 0;
            foreach (var k in GroundDesignTests.Lib().Kinds.Where(k => k.Family == "nature"))
            {
                long b = 0;
                for (int i = 0; i < k.Variants.Count; i++) { var img = DecalVariants.Bake(k, i).Img; b += DecalVariants.GpuBytes(img.W, img.H); }
                _out.WriteLine($"{k.Id}: {k.Variants.Count} variants, {b / 1048576.0:F2} MiB");
                total += b;
            }
            _out.WriteLine($"nature family: {total / 1048576.0:F1} MiB");
            Assert.True(total < 32L * 1048576, $"nature family {total / 1048576.0:F1} MiB");
        }

        [Fact]
        public void SameSeedSameGround()
        {
            var a = Instances(Plan("lookoutHill", "hilltop", 216, 196, Ground("hills")));
            var b = Instances(Plan("lookoutHill", "hilltop", 216, 196, Ground("hills")));
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Kind, b[i].Kind); Assert.Equal(a[i].P, b[i].P); }
        }

        [Fact]
        public void HugeSiteStaysWithinBudgetAndQuick()
        {
            foreach (var (type, style) in new[] { ("natureReserve", "forest"), ("hikingTrail", "ridgeHike"), ("wetland", "reedMarsh") })
            {
                var sw = Stopwatch.StartNew();
                var plan = Plan(type, style, 700, 600, Ground("hills"));
                var wr = plan.Weathering;
                _out.WriteLine($"{type}/{style} 700x600: plan {sw.ElapsedMilliseconds} ms; {wr}");
                Assert.True(wr.Instances <= Weathering.MaxInstances);
                Assert.True(wr.Ms < 4000, $"ground {wr.Ms:F0} ms");
            }
        }

        // ───────────── pictures ─────────────

        private void Picture(ComposedPlan plan, Func<float2, float> ground, string name)
        {
            var lib = GroundDesignTests.Lib();
            var list = Instances(plan);
            var ids = list.Select(d => d.Kind + "#" + d.Variant).Distinct().ToList();
            Parallel.ForEach(ids, id =>
            {
                if (GroundDesignTests._baked.ContainsKey(id)) return;
                DecalVariants.TryParseId(id, out var kind, out int v);
                GroundDesignTests._baked[id] = DecalVariants.Bake(lib.Get(kind), v);
            });
            var dir = Path.Combine(TestKit.RepoRoot(), ".forensics", "ground");
            Directory.CreateDirectory(dir);
            var ring = plan.Footprints.Count > 0 ? plan.Footprints[0] : null;
            Poly.Bounds(ring, out var lo, out var hi);
            Render(plan, list, ground, lo - 4f, hi + 4f, 0.1f, Path.Combine(dir, name + "-top.png"));
            foreach (var cause in new[] { "water:rill", "water:bed", "water:marsh", "water:bank", "rock:scree", "rock:apron", "feet:roots", "feet:animal", "camp:ash", "ground:heath", "leaves" })
            {
                var d = list.FirstOrDefault(x => x.Cause == cause);
                if (d.Kind == null) continue;
                Render(plan, list, ground, d.P - 14f, d.P + 14f, 0.025f, Path.Combine(dir, $"{name}-{cause.Replace(':', '-')}.png"));
            }
        }

        private static void Render(ComposedPlan plan, List<DecalInstance> list, Func<float2, float> ground, float2 lo, float2 hi, float mpp, string file)
        {
            var lib = GroundDesignTests.Lib();
            int W = (int)((hi.x - lo.x) / mpp), H = (int)((hi.y - lo.y) / mpp);
            var px = new float3[W * H];
            var hAfter = plan.Layers.FirstOrDefault(l => l.HeightAfter != null)?.HeightAfter;
            Func<float2, float> h;
            try { h = Placemaker.Core.Build.SiteHarmony.FinalGround(plan, ground ?? (p => 0f)); } catch { h = ground ?? (p => 0f); }
            var grass = new float3(0.33f, 0.43f, 0.2f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = lo + new float2(x + 0.5f, y + 0.5f) * mpp;
                    float e = mpp * 6f;
                    float dx = h(p + new float2(e, 0)) - h(p - new float2(e, 0)), dz = h(p + new float2(0, e)) - h(p - new float2(0, e));
                    float shade = math.clamp(1f - (dx * 0.6f + dz * 0.8f) / (2f * e) * 1.5f, 0.55f, 1.3f);
                    px[y * W + x] = grass * shade;
                }
            foreach (var l in plan.Layers)
            {
                foreach (var s in l.Surfaces) GroundDesignTests.Fill(px, W, H, lo, mpp, Poly.To2D(s.Ring), s.Role == SurfaceRole.Paving || s.Role == SurfaceRole.Plaza ? new float3(0.58f, 0.56f, 0.52f) : s.Role == SurfaceRole.Water ? new float3(0.22f, 0.32f, 0.36f) : new float3(0.55f, 0.5f, 0.42f));
                foreach (var r in l.Regions) if (r.Tags != null && r.Tags.Contains(NatureGround.WaterTag)) GroundDesignTests.Fill(px, W, H, lo, mpp, Poly.To2D(r.Ring), new float3(0.22f, 0.32f, 0.36f));
            }
            var order = list.Select((d, i) => (d, i, k: lib.Get(d.Kind))).Where(x => x.k != null).OrderBy(x => x.k.Order).ThenBy(x => x.i).ToList();
            foreach (var x in order) if ((x.k.Layers & DecalLayerBits.Terrain) != 0) GroundDesignTests.Draw(px, W, H, lo, mpp, x.d, x.k);
            foreach (var l in plan.Layers)
                foreach (var n in l.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    float hw = n.Role == NetRole.PathwayWide ? 2.5f : 1.5f;
                    for (float t = 0; t <= 1f; t += 0.004f)
                    {
                        float u = 1 - t;
                        var p = (u * u * u * n.A + 3 * u * u * t * n.B + 3 * u * t * t * n.C + t * t * t * n.D).xz;
                        GroundDesignTests.Disc(px, W, H, lo, mpp, p, hw, n.FollowTerrain ? new float3(0.66f, 0.64f, 0.6f) : new float3(0.55f, 0.42f, 0.3f));
                    }
                }
            foreach (var x in order) if ((x.k.Layers & DecalLayerBits.Roads) != 0) GroundDesignTests.Draw(px, W, H, lo, mpp, x.d, x.k, onlyOver: new float3(0.66f, 0.64f, 0.6f));
            foreach (var l in plan.Layers)
                foreach (var o in l.Objects)
                {
                    var id = o.Asset.Id ?? "";
                    if (o.Role == ObjectRole.Tree) GroundDesignTests.Disc(px, W, H, lo, mpp, o.Position.xz, 0.5f, new float3(0.2f, 0.15f, 0.1f));
                    else if (o.Role == ObjectRole.Bush) GroundDesignTests.Disc(px, W, H, lo, mpp, o.Position.xz, 0.35f, new float3(0.15f, 0.3f, 0.12f));
                    else if (id.StartsWith("Boulder") || id.StartsWith("Rock")) GroundDesignTests.Disc(px, W, H, lo, mpp, o.Position.xz, id.StartsWith("Boulder") ? 2f : 0.5f, new float3(0.55f, 0.55f, 0.53f));
                    else if (id.StartsWith("Log")) GroundDesignTests.Disc(px, W, H, lo, mpp, o.Position.xz, 0.6f, new float3(0.4f, 0.28f, 0.18f));
                    else if (o.Role != ObjectRole.AnimalSpawner) GroundDesignTests.Disc(px, W, H, lo, mpp, o.Position.xz, 0.6f, new float3(0.8f, 0.3f, 0.2f));
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
    }
}
