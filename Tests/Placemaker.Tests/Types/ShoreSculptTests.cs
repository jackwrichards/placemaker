// s20: terrain-aware water types. Sloped shore sites (a curved coast, a game-like coarse water snapshot), and the
// definition of done from the playtests: the shoreline is found from the terrain, sand is graded and painted into
// the water, terraform is smooth (no terraces, no local peaks), objects sit on the final ground and never on steep
// ground, paths never loop or cross themselves, fences only along real drops, quays follow the water's edge,
// cliffs stay natural (and say so), earthworks stay within limits.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Beach;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    /// <summary>Shore test sites: the sea to the south of a gently curving coast, land rising inland.</summary>
    public static class ShoreSites
    {
        public const float Level = 0f;
        public static readonly string[] Kinds = { "flat", "bank", "headland", "cliff", "curved" };

        public static float Coast(float x) => 4f * math.sin(x / 41f) + 2f * math.sin(x / 17f + 1.3f);

        /// <summary>(s30) A lake bay (playtests 54–57): one big curve with a real waterline's jagged 10–25 m wiggles on it.</summary>
        public static float CurvedCoast(float x) => 22f * math.sin(math.PI * math.saturate(x / 250f)) - 10f + 1.6f * math.sin(x / 4.1f) + 1.1f * math.sin(x / 2.3f + 0.7f) + 0.6f * math.sin(x / 1.3f);

        public static float CoastOf(string kind, float x) => kind == "curved" ? CurvedCoast(x) : Coast(x);

        private static float S(float e0, float e1, float x)
        {
            float t = math.saturate((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public static Func<float2, float> Ground(string kind, float width = 250f) => p =>
        {
            float u = p.y - CoastOf(kind, p.x);
            float low = u >= 0f ? Level + 0.45f + 0.014f * u : math.max(Level - 5f, Level + 0.45f + 0.09f * u);
            float bank = u >= 0f ? Level + 0.4f + 6.5f * S(3f, 30f, u) + 0.012f * u : math.max(Level - 6f, Level + 0.4f + 0.16f * u);
            float cliff = u >= 0f ? Level + 0.5f + 13f * S(0f, 9f, u) + 0.02f * u : math.max(Level - 8f, Level + 0.5f + 0.45f * u);
            // (s30) curved lake shore: a 3.5 m bank over 25 m with lumps (the hillside behind playtest 56's promenade).
            float curved = u >= 0f ? Level + 0.35f + 3.5f * S(2f, 26f, u) + 0.03f * u + 0.45f * math.sin(p.x / 13f) * math.sin(p.y / 11f) * S(1f, 12f, u) : math.max(Level - 5f, Level + 0.35f + 0.14f * u);
            switch (kind)
            {
                case "bank": return bank;
                case "cliff": return cliff;
                case "curved": return curved;
                case "headland": return math.lerp(low, cliff, S(width * 0.42f, width * 0.62f, p.x));
                default: return low;
            }
        };

        /// <summary>The game's water snapshot: depth sampled on an 8 m grid, bilinear (its zero line is blurred).</summary>
        public static Func<float2, float> Depth(Func<float2, float> ground, string kind = null)
        {
            const float cell = 8f;
            var min = new float2(-120f, -120f);
            int nx = 80, ny = 80;
            var d = new float[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    var p = min + new float2(i, j) * cell;
                    d[j * nx + i] = p.y - CoastOf(kind, p.x) < 6f ? math.max(0f, Level - ground(p)) : 0f;
                }
            return q =>
            {
                float fx = math.clamp((q.x - min.x) / cell, 0f, nx - 1.001f), fy = math.clamp((q.y - min.y) / cell, 0f, ny - 1.001f);
                int i = (int)fx, j = (int)fy;
                float tx = fx - i, ty = fy - j;
                return math.lerp(math.lerp(d[j * nx + i], d[j * nx + i + 1], tx), math.lerp(d[(j + 1) * nx + i], d[(j + 1) * nx + i + 1], tx), ty);
            };
        }

        public static SiteContext Site(string kind)
        {
            var g = Ground(kind);
            return new SiteContext { HeightAt = g, WaterDepthAt = Depth(g, kind) };
        }

        /// <summary>The real waterline z at x (bisection on ground = level).</summary>
        public static float WaterZ(string kind, float x)
        {
            var g = Ground(kind);
            float lo = -40f, hi = 40f;
            for (int i = 0; i < 40; i++)
            {
                float m = (lo + hi) * 0.5f;
                if (g(new float2(x, m)) < Level) lo = m; else hi = m;
            }
            return (lo + hi) * 0.5f;
        }

        /// <summary>5 ha rect over the shore: z0 &lt; 0 includes water (Inside), z0 &gt; 0 stops short of it (Edge mode with a gap).</summary>
        public static float3[] Ring(float z0 = -14f) => TestKit.Rect(250, 200, 0, z0);

        /// <summary>The ground the build leaves: the plan's terraform through the real raster (sloped patches included).</summary>
        public static Func<float2, float> Final(AreaPlan plan, Func<float2, float> original)
        {
            var items = new List<(float2[] ring, float target, int layer)>();
            var vh = new List<float[]>();
            foreach (var t in plan.Terraform)
            {
                if (t?.Ring == null || t.Ring.Length < 3 || !math.isfinite(t.TargetHeight)) continue;
                items.Add((t.Ring.Select(v => v.xz).ToArray(), t.TargetHeight, 0));
                vh.Add(TerraformSurface.VertexHeights(t));
            }
            if (items.Count == 0) return original;
            var raster = TerraformRaster.Build(items, 1f, 4f, 1 << 22, vh, original);
            return p => raster.TrySample(p, original, out float h) ? h : original(p);
        }

        public static AreaPlan Plan(string layer, string preset, string kind, float z0 = -14f, uint seed = 5)
        {
            var gen = TestKit.Gen(layer);
            var values = gen.Presets.Single(p => p.Id == preset).Values.Clone();
            var ring = Ring(z0);
            var input = TestKit.Input(ring, values, seed, null, Site(kind));
            // One entrance on the landward (top) edge, as a player would place it at the road.
            input.Markers.Add(new EdgeMarker { Kind = EntranceKinds.Pedestrian, RingSegment = 2, RingT = 0.5f, Position = (ring[2] + ring[3]) * 0.5f });
            return gen.Plan(input, CancellationToken.None);
        }


        /// <summary>(s30) Net tuples for TerrainMetrics (kind: terrain-following, quay, other deck).</summary>
        public static List<(float3 a, float3 b, float3 c, float3 d, float halfWidth, int kind)> NetTuples(AreaPlan plan) =>
            plan.Nets.Where(n => n != null && n.Role != NetRole.Fence)
                .Select(n => (n.A, n.B, n.C, n.D, Composer.DefaultHalfWidth(n.Role),
                    n.FollowTerrain ? TerrainMetrics.SeatFollow : n.Category == AssetCategories.NetQuay ? TerrainMetrics.SeatQuay : TerrainMetrics.SeatDeck)).ToList();

        /// <summary>(s30) False within half a metre of any net's width (its own cut, wall or deck covers the ground there); cached on a 1 m grid.</summary>
        public static Func<float2, bool> OffNets(AreaPlan plan)
        {
            var raw = TerrainMetrics.OffNets(NetTuples(plan));
            var min = new float2(-60f, -80f);
            int nx = 380, ny = 320;
            var grid = new bool[nx * ny];
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) grid[j * nx + i] = raw(min + new float2(i, j));
            return p =>
            {
                int i = (int)math.round(p.x - min.x), j = (int)math.round(p.y - min.y);
                return i < 0 || j < 0 || i >= nx || j >= ny ? raw(p) : grid[j * nx + i];
            };
        }
        public static IEnumerable<float2> Samples(PlannedNet n, int count = 16)
        {
            for (int i = 0; i <= count; i++) yield return Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)count).xz;
        }
    }

    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class ShoreSculptTests
    {
        private readonly ITestOutputHelper _out;
        public ShoreSculptTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var layer in new[] { "beach", "waterfront" })
                foreach (var p in TestKit.Gen(layer).Presets)
                    foreach (var kind in new[] { "flat", "bank", "headland", "curved" })
                        yield return new object[] { layer, p.Id, kind };
        }

        private static bool IsPath(PlannedNet n) => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide;

        // ───────────── primitives ─────────────

        [Fact]
        public void MeanValueInterpolationIsExactAndContinuous()
        {
            var a = new[] { new float3(0, 1, 0), new float3(4, 2, 0), new float3(4, 5, 4), new float3(0, 3, 4) };
            var b = new[] { new float3(4, 2, 0), new float3(8, 0, 0), new float3(8, 1, 4), new float3(4, 5, 4) };
            foreach (var v in a) Assert.Equal(v.y, TerraformSurface.At(a, v.xz), 3);
            // Linear along an edge, and the same on the shared edge of both quads.
            for (float t = 0.1f; t < 1f; t += 0.2f)
            {
                var p = math.lerp(new float2(4, 0), new float2(4, 4), t);
                Assert.Equal(math.lerp(2f, 5f, t), TerraformSurface.At(a, p), 2);
                Assert.Equal(TerraformSurface.At(a, p), TerraformSurface.At(b, p), 2);
            }
            // A plane is reproduced exactly inside.
            Func<float2, float> plane = q => 1f + 0.2f * q.x - 0.1f * q.y;
            var quad = new[] { new float3(0, plane(new float2(0, 0)), 0), new float3(6, plane(new float2(6, 0)), 0), new float3(6, plane(new float2(6, 6)), 6), new float3(0, plane(new float2(0, 6)), 6) };
            Assert.Equal(plane(new float2(2.3f, 4.1f)), TerraformSurface.At(quad, new float2(2.3f, 4.1f)), 3);
        }

        [Fact]
        public void SlopedGridRastersWithoutSteps()
        {
            // A sculpt shaped to a plane: the raster's desired ground follows the plane within 2 cm (flat cells made 4 m steps).
            var ctx = TestKit.Gen("beach");
            var sculpt = new Sculpt(_ => 0f, new float2(0, 0), new float2(40, 40), 4f, _ => true);
            sculpt.Shape((p, h0, t) => 0.15f * p.x);
            var plan = new AreaPlan();
            var lc = new LayerContext { Plan = plan, Input = TestKit.Input(TestKit.Rect(40, 40)) };
            sculpt.Emit(lc, null);
            Assert.All(plan.Terraform, t => Assert.True(t.Sloped));
            var final = ShoreSites.Final(plan, _ => 0f);
            for (float x = 3f; x < 37f; x += 0.7f)
                Assert.True(math.abs(final(new float2(x, 20f)) - 0.15f * x) < 0.05f, $"at x {x}: {final(new float2(x, 20f)):0.000} vs {0.15f * x:0.000}");
        }

        [Fact]
        public void OffsetLinesNeverLoop()
        {
            // A tight zig-zag folded by a wide offset makes swallowtail loops; RemoveLoops cuts them out.
            var pts = new List<float2>();
            for (int i = 0; i <= 40; i++) pts.Add(new float2(i * 2f, (i % 10 < 5 ? i % 10 : 10 - i % 10) * 6f));
            var off = new List<float2>();
            for (int i = 0; i < pts.Count; i++)
            {
                var t = math.normalizesafe(pts[math.min(pts.Count - 1, i + 1)] - pts[math.max(0, i - 1)]);
                off.Add(pts[i] + new float2(-t.y, t.x) * 9f);
            }
            Assert.True(Shoreline.SelfIntersections(off) > 0, "fixture should fold");
            Assert.Equal(0, Shoreline.SelfIntersections(Shoreline.RemoveLoops(off)));
        }

        // ───────────── the shoreline ─────────────

        [Theory]
        [InlineData("flat")]
        [InlineData("bank")]
        public void FindsTheRealWaterlineFromTheTerrain(string kind)
        {
            // Outline drawn 12 m short of the water, coarse 8 m water snapshot: the waterline comes from the heights.
            var ring = ShoreSites.Ring(12f);
            var input = TestKit.Input(ring, TestKit.Gen("beach").Defaults(), 3, null, ShoreSites.Site(kind));
            var plan = new AreaPlan();
            var ctx = new LayerContext { Plan = plan, Input = input, LayerRegion = LayerRegion.From(ring), Settings = input.Settings };
            var shore = Shoreline.Analyze(ctx, ctx.LayerRegion, "auto");
            Assert.NotNull(shore);
            Assert.True(shore.LevelTrusted);
            Assert.Equal(ShoreSites.Level, shore.WaterLevel, 1);
            float worst = 0f;
            for (float s = 10f; s < shore.Length - 10f; s += 5f)
            {
                var w = shore.WaterAt(s, out _);
                worst = math.max(worst, math.abs(w.y - ShoreSites.WaterZ(kind, w.x)));
            }
            _out.WriteLine($"{kind}: waterline error {worst:0.00} m, cliff share {shore.CliffShare:0.00}");
            Assert.True(worst < 1.5f, $"waterline off by {worst:0.00} m");
            Assert.True(shore.CliffShare < 0.05f);
        }

        // ───────────── the definition of done, every preset × site ─────────────

        [Theory]
        [MemberData(nameof(Cases))]
        public void PresetIsTerrainAware(string layer, string preset, string kind)
        {
            var site = ShoreSites.Site(kind);
            var plan = ShoreSites.Plan(layer, preset, kind);
            Assert.False(plan.HasErrors, string.Join(",", plan.Issues.Select(i => i.Key)));
            var original = site.HeightAt;
            var final = ShoreSites.Final(plan, original);
            var survey = new TerrainSurvey(original, new float2(-20, -40), new float2(270, 210), 2f);
            string tag = $"{layer}/{preset}/{kind}";

            // 1. Objects stand on the final ground (nothing floating or buried).
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal) continue;
                float g = final(o.Position.xz);
                Assert.True(math.abs(o.Position.y - g) < 0.3f, $"{tag}: {o.Role}/{o.Category} at {o.Position.xz} planned at {o.Position.y:0.00}, ground {g:0.00}");
            }

            // 2. Furniture never on steep ground (trees, bushes and rocks may stand on slopes, not on cliffs).
            foreach (var o in plan.Objects)
            {
                bool natural = o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush || o.Category == AssetCategories.Rock || o.Role == ObjectRole.Decal;
                float grade = TerrainSurvey.Grade(final, o.Position.xz, 1f);
                if (natural) Assert.True(survey.GradeAt(o.Position.xz) < 0.6f || math.abs(final(o.Position.xz) - original(o.Position.xz)) > 0.2f, $"{tag}: {o.Role} on a cliff face at {o.Position.xz}");
                else Assert.True(grade <= 0.16f, $"{tag}: {o.Role}/{o.Category} at {o.Position.xz} on a {grade:P0} slope");
            }

            // 3. Paths: no self-intersection, no two paths crossing away from a junction, walkable grades, never on a cliff face.
            var paths = plan.Nets.Where(IsPath).ToList();
            var decks = plan.Nets.Where(n => n.Category == AssetCategories.NetQuay || n.Category == AssetCategories.NetPier).ToList();
            foreach (var n in paths)
            {
                var pts = ShoreSites.Samples(n).ToList();
                Assert.Equal(0, Shoreline.SelfIntersections(pts));
                if (!n.FollowTerrain) continue;
                for (int i = 1; i < pts.Count; i++)
                {
                    float run = math.distance(pts[i - 1], pts[i]);
                    if (run < 0.5f) continue;
                    // Where a path joins a quay or a pier it stands on that deck, not on the ground under it.
                    if (decks.Any(d => ShoreSites.Samples(d, 12).Any(q => math.distance(q, pts[i]) < 4.5f))) continue;
                    float g = math.abs(final(pts[i]) - final(pts[i - 1])) / run;
                    Assert.True(g <= 0.2f, $"{tag}: path at {pts[i]} climbs {g:P0}");
                    Assert.True(survey.GradeAt(pts[i]) < 0.6f || math.abs(final(pts[i]) - original(pts[i])) > 0.3f, $"{tag}: path on a cliff face at {pts[i]}");
                }
            }
            for (int a = 0; a < paths.Count; a++)
                for (int b = a + 1; b < paths.Count; b++)
                {
                    var pa = ShoreSites.Samples(paths[a], 12).ToList(); var pb = ShoreSites.Samples(paths[b], 12).ToList();
                    for (int i = 1; i < pa.Count; i++)
                        for (int j = 1; j < pb.Count; j++)
                        {
                            if (!Poly.SegmentIntersection(pa[i - 1], pa[i], pb[j - 1], pb[j], out var x, out _, out _)) continue;
                            bool atNode = new[] { paths[a].A.xz, paths[a].D.xz, paths[b].A.xz, paths[b].D.xz }.Any(e => math.distance(e, x) < 1.5f);
                            Assert.True(atNode, $"{tag}: paths cross at {x} without a junction");
                        }
                }

            // 4. Fences only along real drops (> 1 m within 3.5 m of the fence).
            foreach (var f in plan.Nets.Where(n => n.Role == NetRole.Fence))
            {
                var mid = Composer.Bezier(f.A, f.B, f.C, f.D, 0.5f).xz;
                var dir = math.normalizesafe(f.D.xz - f.A.xz);
                var nrm = new float2(-dir.y, dir.x);
                float h = final(mid);
                float drop = math.max(h - math.min(final(mid + nrm * 3.5f), final(mid + nrm * 2f)), h - math.min(final(mid - nrm * 3.5f), final(mid - nrm * 2f)));
                Assert.True(drop >= 1f, $"{tag}: fence at {mid} with only {drop:0.00} m drop");
            }

            // 5. Terraform is smooth: no stepped artefacts (slope change per metre) and no local peaks, within the earthwork limits.
            //    (s30) Not under a net: a quay wall or a walk cut into a hillside is a deliberate step its own net covers.
            var offNets = ShoreSites.OffNets(plan);
            int steps = 0, peaks = 0;
            float worstCurv = 0f; float2 worstAt = 0f;
            for (float z = -30f; z < 190f; z += 1f)
                for (float x = 5f; x < 245f; x += 1f)
                {
                    var p = new float2(x, z);
                    float h = final(p);
                    if (math.abs(h - original(p)) < 0.08f || !offNets(p) || h < ShoreSites.Level - 0.5f) continue;
                    foreach (var d in new[] { new float2(1, 0), new float2(0, 1) })
                    {
                        float c = math.abs(final(p + d) - 2f * h + final(p - d));
                        float c0 = math.abs(original(p + d) - 2f * original(p) + original(p - d));
                        float k = c - c0;
                        if (k > 0.35f) steps++;
                        if (k > worstCurv) { worstCurv = k; worstAt = p; }
                    }
                    if (((int)x % 2 == 0) && ((int)math.abs(z) % 2 == 0))
                    {
                        bool peak = true;
                        for (int a = -1; a <= 1 && peak; a++)
                            for (int b = -1; b <= 1; b++)
                                if ((a != 0 || b != 0) && final(p + new float2(a, b) * 2f) > h - 0.3f) { peak = false; break; }
                        if (peak) peaks++;
                    }
                }
            _out.WriteLine($"{tag}: worst slope change {worstCurv:0.000}/m, stepped samples {steps}, peaks {peaks}, cut {plan.Facts.GetValueOrDefault("earthMaxCut"):0.0} fill {plan.Facts.GetValueOrDefault("earthMaxFill"):0.0}");
            Assert.True(steps <= 6, $"{tag}: {steps} stepped samples (worst {worstCurv:0.00}/m at {worstAt})");
            Assert.Equal(0, peaks);
            // s30: the waterfront walk stays low near the water and cuts the hillside behind (graded or walled): up to 6.5 m on the 6.5 m bank.
            Assert.True(plan.Facts.GetValueOrDefault("earthMaxCut") <= (layer == "waterfront" ? 6.6 : 6.0), $"{tag}: cut {plan.Facts["earthMaxCut"]:0.00} m");
            // p2-beaches: landforms (dunes, islands, knolls, a bluff) declare their own fill limit (formsFill); the 2.5 m cap holds without them
            double fillCap = math.max(2.51, plan.Facts.GetValueOrDefault("formsFill") + 0.01);
            Assert.True(plan.Facts.GetValueOrDefault("earthMaxFill") <= fillCap, $"{tag}: fill {plan.Facts["earthMaxFill"]:0.00} m (cap {fillCap:0.00})");
            Assert.All(plan.Terraform, t => Assert.True(t.Sloped || t.Kind == TerraformKind.Level, $"{tag}: a flat {t.Kind} patch"));
        }

        [Theory]
        [InlineData("waterfront", "harbor")]
        [InlineData("waterfront", "pier")]
        [InlineData("waterfront", "riverSteps")]
        public void QuayFollowsTheWaterEdge(string layer, string preset)
        {
            foreach (var kind in new[] { "flat", "bank" })
            {
                var plan = ShoreSites.Plan(layer, preset, kind);
                var quays = plan.Nets.Where(n => n.Category == AssetCategories.NetQuay).ToList();
                Assert.NotEmpty(quays);
                float hw = preset == "harbor" ? 4f : 1.5f;
                foreach (var n in quays)
                    foreach (var p in ShoreSites.Samples(n, 8))
                    {
                        if (p.x < 15f || p.x > 235f) continue;
                        float off = p.y - ShoreSites.WaterZ(kind, p.x);
                        // The deck's sea side on the waterline: its centre half a width inland (± 2 m).
                        // s30: the quay is one smooth curve (σ ≥ 14 m, radius ≥ 40 m), so it rides over the waterline's small wiggles.
                        Assert.True(math.abs(off - (hw - 0.4f)) < 2.7f, $"{preset}/{kind}: quay at {p} is {off:0.0} m from the water");
                        Assert.False(n.FollowTerrain);
                        Assert.True(n.A.y > ShoreSites.Level + 0.5f, "deck above the water");
                    }
                Assert.True(plan.Facts["edgeLength"] > 150f, $"{preset}/{kind}: edge only {plan.Facts["edgeLength"]:0} m");
            }
        }

        [Theory]
        [InlineData("sandy")]
        [InlineData("resort")]
        [InlineData("cove")]
        public void SandIsGradedAndPaintedIntoTheWater(string preset)
        {
            foreach (var (kind, z0) in new[] { ("flat", -14f), ("bank", -14f), ("flat", 10f), ("bank", 10f) })
            {
                var site = ShoreSites.Site(kind);
                var plan = ShoreSites.Plan("beach", preset, kind, z0);
                var stamps = PaintRaster.Build(plan.Paint.Where(p => p.Layer == TerrainMaterials.Sand).ToList(), new PaintRasterOptions { Cell = 1f, Align = 1f, TileCells = 2048 });
                float At(float2 p) { float v = 0f; foreach (var s in stamps) v = math.max(v, s.Sample(p)); return v; }
                var final = ShoreSites.Final(plan, site.HeightAt);
                for (float x = 40f; x <= 210f; x += 34f)
                {
                    float wz = ShoreSites.WaterZ(kind, x);
                    // Full sand at the waterline and well under the water.
                    foreach (float under in new[] { 0f, 4f, 10f })
                        Assert.True(At(new float2(x, wz - under)) > 0.85f, $"{preset}/{kind}/{z0}: sand {At(new float2(x, wz - under)):0.00} at {under} m under the water (x {x})");
                    // The beach face runs gently down into the water (graded, not a step at the waterline).
                    float g = (final(new float2(x, wz + 6f)) - final(new float2(x, wz - 6f))) / 12f;
                    Assert.InRange(g, 0f, 0.13f);
                }
            }
        }

        [Fact]
        public void CliffShoreStaysNaturalAndSaysSo()
        {
            foreach (var layer in new[] { "beach", "waterfront" })
                foreach (var p in TestKit.Gen(layer).Presets)
                {
                    var site = ShoreSites.Site("cliff");
                    var plan = ShoreSites.Plan(layer, p.Id, "cliff");
                    var tag = layer + "/" + p.Id;
                    Assert.Contains(plan.Issues, i => i.Key == ShoreGround.CliffKey && i.Severity == IssueSeverity.Warning);
                    var survey = new TerrainSurvey(site.HeightAt, new float2(-20, -40), new float2(270, 210), 2f);
                    // Nothing on the face: no terraform, no furniture, no paths where the original ground is a cliff.
                    foreach (var t in plan.Terraform)
                        Assert.True(survey.GradeAt(Poly.Centroid(Poly.To2D(t.Ring))) < 0.6f, $"{tag}: terraform on the cliff face");
                    foreach (var o in plan.Objects) Assert.True(survey.GradeAt(o.Position.xz) < 0.6f, $"{tag}: {o.Role} on the cliff face at {o.Position.xz}");
                    foreach (var n in plan.Nets.Where(IsPath))
                        foreach (var q in ShoreSites.Samples(n, 8)) Assert.True(survey.GradeAt(q) < 0.6f, $"{tag}: path on the cliff face at {q}");
                    Assert.Empty(plan.Paint.Where(pt => pt.Layer == TerrainMaterials.Sand));
                    // A cliff-top walk with lookouts instead.
                    Assert.Contains(plan.Nets, IsPath);
                    Assert.True(plan.Facts["lookouts"] >= 1, tag + ": no lookout");
                }
        }

        [Fact]
        public void HeadlandBuildsOnTheLowShoreAndLeavesTheCliff()
        {
            var site = ShoreSites.Site("headland");
            var survey = new TerrainSurvey(site.HeightAt, new float2(-20, -40), new float2(270, 210), 2f);
            foreach (var (layer, preset) in new[] { ("beach", "sandy"), ("beach", "resort"), ("waterfront", "promenade"), ("waterfront", "harbor") })
            {
                var plan = ShoreSites.Plan(layer, preset, "headland");
                Assert.Contains(plan.Issues, i => i.Key == ShoreGround.CliffKey);
                Assert.InRange(plan.Facts["shoreCliffShare"], 0.2, 0.7);
                // The low (west) shore is used.
                Assert.Contains(plan.Objects, o => o.Position.x < 90f && o.Role != ObjectRole.Tree && o.Role != ObjectRole.Bush);
                foreach (var o in plan.Objects) Assert.True(survey.GradeAt(o.Position.xz) < 0.6f, $"{layer}/{preset}: {o.Role} on the cliff at {o.Position.xz}");
            }
        }

        [Fact]
        public void FastOnFiveHectareSlopes()
        {
            foreach (var layer in new[] { "beach", "waterfront" })
                foreach (var p in TestKit.Gen(layer).Presets)
                    foreach (var kind in new[] { "bank", "headland" })
                    {
                        long ms = WaterTypesKit.Time(() => ShoreSites.Plan(layer, p.Id, kind));
                        _out.WriteLine($"{layer}/{p.Id}/{kind}: {ms} ms");
                        Assert.True(ms < TimingBudget.Ms(250), $"{layer}/{p.Id}/{kind} took {ms} ms");
                    }
        }
    }
}
