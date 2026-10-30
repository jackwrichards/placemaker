// s33: terrain paint toolkit (Core/Paint): shapes, kit, raster integration, palette, lawn defaults.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Decals;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Paint;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class PaintToolkitTests
    {
        private readonly ITestOutputHelper _out;
        public PaintToolkitTests(ITestOutputHelper output) { _out = output; }

        private static float2[] Square(float x0, float y0, float x1, float y1) => new[] { new float2(x0, y0), new float2(x1, y0), new float2(x1, y1), new float2(x0, y1) };

        private static readonly PaintRasterOptions Fine = new PaintRasterOptions { Cell = 0.5f, Align = 0.5f, TileCells = 2048, MaxStamps = 64 };

        // ───────────────────────── primitives ─────────────────────────

        [Fact]
        public void PolygonFillsInsideWithAFeatherOfTheGivenWidth()
        {
            var s = PaintShape.Polygon(Square(0, 0, 20, 20), 2f);
            Assert.Equal(1f, s.Sample(new float2(10, 10)), 3);
            Assert.Equal(1f, s.Sample(new float2(2.05f, 10)), 2);
            Assert.Equal(0f, s.Sample(new float2(-0.5f, 10)), 3);
            Assert.Equal(0f, s.Sample(new float2(0.0f, 10)), 2);
            float prev = -1f;
            for (float d = 0.05f; d <= 2.0f; d += 0.15f)
            {
                float v = s.Sample(new float2(d, 10));
                Assert.True(v >= prev - 1e-5f, $"feather not monotonic at {d}");
                Assert.InRange(v, 0f, 1f);
                prev = v;
            }
            Assert.InRange(s.Sample(new float2(1f, 10)), 0.35f, 0.65f);   // half way through the feather ≈ half
            Assert.True(s.Bounded);
            Assert.Equal(new float2(0, 0), s.Min);
            Assert.Equal(new float2(20, 20), s.Max);
        }

        [Fact]
        public void PolygonNoiseOnlyPullsTheEdgeInward()
        {
            var plain = PaintShape.Polygon(Square(0, 0, 40, 40), 2f);
            var noisy = PaintShape.Polygon(Square(0, 0, 40, 40), 2f, 1.5f, 6f, 7u);
            for (float x = -3f; x < 43f; x += 0.7f)
                for (float y = -3f; y < 43f; y += 0.9f)
                {
                    var p = new float2(x, y);
                    Assert.True(noisy.Sample(p) <= plain.Sample(p) + 1e-4f, $"noise painted more at {p}");
                    if (x < 0 || y < 0 || x > 40 || y > 40) Assert.Equal(0f, noisy.Sample(p));
                }
            // Deep inside it is full whatever the noise.
            Assert.Equal(1f, noisy.Sample(new float2(20, 20)), 3);
        }

        [Fact]
        public void StrokeHasWidthFeatherAndAlongPathGradient()
        {
            var line = new[] { new float2(0, 0), new float2(50, 0) };
            var s = PaintShape.Stroke(line, 4f, 1f);
            Assert.Equal(1f, s.Sample(new float2(25, 0)), 3);
            Assert.Equal(1f, s.Sample(new float2(25, 0.95f)), 2);
            Assert.InRange(s.Sample(new float2(25, 1.5f)), 0.2f, 0.8f);
            Assert.Equal(0f, s.Sample(new float2(25, 2.05f)), 3);
            // Start → end strength, monotonic along the line.
            var g = PaintShape.Stroke(line, 4f, 1f, start: 1f, end: 0.1f);
            float prev = 2f;
            for (float x = 0.5f; x < 50f; x += 2f)
            {
                float v = g.Sample(new float2(x, 0));
                Assert.True(v <= prev + 1e-5f, $"along gradient rose at {x}");
                prev = v;
            }
            Assert.InRange(g.Sample(new float2(0.5f, 0)), 0.95f, 1f);
            Assert.InRange(g.Sample(new float2(49.5f, 0)), 0.1f, 0.14f);
            // Faded ends: a trail petering out.
            var f = PaintShape.Stroke(line, 4f, 1f, endFade: 6f);
            Assert.True(f.Sample(new float2(0.2f, 0)) < 0.05f);
            Assert.True(f.Sample(new float2(49.8f, 0)) < 0.05f);
            Assert.Equal(1f, f.Sample(new float2(25, 0)), 3);
        }

        [Fact]
        public void StrokeWobbleVariesTheWidthButStaysNearIt()
        {
            var line = new[] { new float2(0, 0), new float2(80, 0) };
            var s = PaintShape.Stroke(line, 4f, 0.6f, wobble: 0.4f, wobbleScale: 6f, seed: 3u);
            var widths = new List<float>();
            for (float x = 5f; x < 75f; x += 1f)
            {
                float w = 0f;
                for (float y = 0f; y < 4f; y += 0.05f) if (s.Sample(new float2(x, y)) > 0.5f) w = y;
                widths.Add(w);
                Assert.Equal(0f, s.Sample(new float2(x, 2f * 1.4f + 0.6f)));   // never beyond (1 + wobble) × half
            }
            Assert.True(widths.Max() - widths.Min() > 0.3f, "wobble did not vary the width");
        }

        [Theory]
        [InlineData(Falloff.Linear)]
        [InlineData(Falloff.Smooth)]
        [InlineData(Falloff.Fast)]
        [InlineData(Falloff.Hold)]
        [InlineData(Falloff.Sphere)]
        public void RadialGradientIsFullInsideAndMonotonic(Falloff f)
        {
            var s = PaintShape.Radial(new float2(10, 10), 2f, 6f, f);
            Assert.Equal(1f, s.Sample(new float2(10, 10)), 3);
            Assert.Equal(1f, s.Sample(new float2(11.9f, 10)), 3);
            Assert.Equal(0f, s.Sample(new float2(16.1f, 10)), 3);
            float prev = 2f;
            for (float d = 0f; d < 7f; d += 0.25f)
            {
                float v = s.Sample(new float2(10 + d, 10));
                Assert.True(v <= prev + 1e-5f, $"{f}: rose at {d}");
                prev = v;
            }
            if (f == Falloff.Sphere) Assert.Equal(math.sqrt(0.75f), s.Sample(new float2(14, 10)), 3);
            if (f == Falloff.Linear) Assert.Equal(0.5f, s.Sample(new float2(14, 10)), 3);
        }

        [Fact]
        public void RadialEllipseStretchesAlongItsAngle()
        {
            var s = PaintShape.Radial(float2.zero, 0f, 4f, Falloff.Linear, aspect: 2f, angle: 0f);
            Assert.True(s.Sample(new float2(6f, 0f)) > 0f, "major axis along +x");
            Assert.Equal(0f, s.Sample(new float2(0f, 4.5f)), 3);
        }

        [Fact]
        public void LinearGradientRunsFromStartToEndAndClamps()
        {
            var s = PaintShape.Linear(new float2(0, 0), new float2(10, 0), Falloff.Linear, 1f, 0.2f);
            Assert.Equal(1f, s.Sample(new float2(-5, 3)), 3);
            Assert.Equal(0.2f, s.Sample(new float2(15, -3)), 3);
            Assert.Equal(0.6f, s.Sample(new float2(5, 100)), 3);
            Assert.False(s.Bounded);
        }

        [Theory]
        [InlineData(0.2f)]
        [InlineData(0.5f)]
        [InlineData(0.8f)]
        public void NoiseCoverageIsAShareOfTheGround(float coverage)
        {
            var s = PaintShape.Noise(6f, coverage, 0.05f, 99u);
            int on = 0, n = 0;
            for (float x = 0; x < 300; x += 1.3f)
                for (float y = 0; y < 300; y += 1.3f) { n++; if (s.Sample(new float2(x, y)) > 0.5f) on++; }
            float share = on / (float)n;
            _out.WriteLine($"coverage {coverage}: {share:0.000}");
            Assert.InRange(share, coverage - 0.12f, coverage + 0.12f);
            Assert.Equal(0f, PaintShape.Noise(6f, 0f, 0.1f, 1u).Sample(new float2(3, 4)));
            Assert.Equal(1f, PaintShape.Noise(6f, 1f, 0.1f, 1u).Sample(new float2(3, 4)));
        }

        [Fact]
        public void CombinatorsMaskUniteAndSubtract()
        {
            var a = PaintShape.Polygon(Square(0, 0, 20, 20), 0.5f);
            var b = PaintShape.Polygon(Square(10, 0, 30, 20), 0.5f);
            var mul = a.Mul(b);
            Assert.Equal(new float2(10, 0), mul.Min);
            Assert.Equal(new float2(20, 20), mul.Max);
            Assert.Equal(1f, mul.Sample(new float2(15, 10)), 3);
            Assert.Equal(0f, mul.Sample(new float2(5, 10)), 3);
            var or = a.Or(b);
            Assert.Equal(1f, or.Sample(new float2(5, 10)), 3);
            Assert.Equal(1f, or.Sample(new float2(25, 10)), 3);
            var sub = a.Subtract(b);
            Assert.Equal(1f, sub.Sample(new float2(5, 10)), 3);
            Assert.Equal(0f, sub.Sample(new float2(15, 10)), 3);
            Assert.Equal(0.5f, a.Scale(0.5f).Sample(new float2(5, 5)), 3);
            Assert.Equal(0f, a.Invert().Sample(new float2(5, 5)), 3);
            var many = PaintShape.Union(Enumerable.Range(0, 300).Select(i => PaintShape.Radial(new float2(i * 7f, 0), 1f, 2f)).ToList());
            Assert.Equal(1f, many.Sample(new float2(7f * 123, 0)), 3);
            Assert.Equal(0f, many.Sample(new float2(7f * 123 + 3.5f, 0)), 3);
        }

        [Fact]
        public void BlendSplitsAreaIntoComplementaryWeights()
        {
            var area = PaintShape.Polygon(Square(0, 0, 40, 20), 1f);
            var t = PaintShape.Linear(new float2(0, 0), new float2(40, 0), Falloff.Linear, 0f, 1f);
            PaintShape.Split(area, t, out var a, out var b);
            for (float x = 2; x < 38; x += 3)
            {
                var p = new float2(x, 10);
                Assert.Equal(area.Sample(p), a.Sample(p) + b.Sample(p), 3);
            }
            Assert.True(a.Sample(new float2(3, 10)) > b.Sample(new float2(3, 10)));
            Assert.True(a.Sample(new float2(37, 10)) < b.Sample(new float2(37, 10)));
        }

        [Fact]
        public void ShapesAreDeterministicAndThreadSafe()
        {
            PaintShape Make(uint seed) => PaintShape.Polygon(Square(0, 0, 60, 60), 3f, 1f, 6f, seed)
                .Mul(PaintShape.Noise(5f, 0.4f, 0.3f, seed))
                .Or(PaintShape.Stroke(new[] { new float2(0, 0), new float2(60, 60) }, 3f, 1f, wobble: 0.3f, seed: seed));
            var a = Make(5u); var b = Make(5u); var c = Make(6u);
            var pts = new List<float2>();
            for (float x = 0; x < 60; x += 0.9f) for (float y = 0; y < 60; y += 1.1f) pts.Add(new float2(x, y));
            var va = pts.Select(a.Sample).ToArray();
            Assert.Equal(va, pts.Select(b.Sample).ToArray());
            Assert.NotEqual(va, pts.Select(c.Sample).ToArray());
            var par = new float[pts.Count];
            Parallel.For(0, pts.Count, i => par[i] = a.Sample(pts[i]));
            Assert.Equal(va, par);
        }

        // ───────────────────────── kit + raster ─────────────────────────

        [Fact]
        public void KitEmitsShapePaintsThatRasteriseTruthfully()
        {
            var list = new List<PlannedPaint>();
            var kit = new PaintKit(list, _ => 5f, 42u);
            var fill = kit.Fill(Square(0, 0, 30, 30), TerrainMaterials.Dirt, 0.6f, 2f, 0f);
            var radial = kit.Radial(new float2(50, 15), TerrainMaterials.Lush, 0.8f, 2f, 6f);
            var stroke = kit.Stroke(new[] { new float2(0, 50), new float2(40, 50) }, TerrainMaterials.Sand, 1f, 4f, 1f, 1f, 0.2f);
            Assert.NotNull(fill); Assert.NotNull(radial); Assert.NotNull(stroke);
            Assert.All(list, p => Assert.True(p.Ring.Length >= 3 && p.Ring.All(v => v.y == 5f)));
            var stamps = PaintRaster.Build(list, Fine);
            Assert.Equal(0.6f, PaintRaster.Sample(stamps, TerrainMaterials.Dirt, new float2(15, 15)), 2);
            Assert.Equal(0f, PaintRaster.Sample(stamps, TerrainMaterials.Dirt, new float2(35, 15)), 2);
            Assert.Equal(0.8f, PaintRaster.Sample(stamps, TerrainMaterials.Lush, new float2(50, 15)), 2);
            Assert.True(PaintRaster.Sample(stamps, TerrainMaterials.Sand, new float2(5, 50)) > PaintRaster.Sample(stamps, TerrainMaterials.Sand, new float2(35, 50)));
            // Exact reference = raster at cell centres.
            Assert.Equal(PaintField.StrengthAt(radial, new float2(53.25f, 15.25f)), PaintRaster.Sample(stamps, TerrainMaterials.Lush, new float2(53.25f, 15.25f)), 2);
        }

        [Fact]
        public void KitMaskClipsEverythingItPaints()
        {
            var list = new List<PlannedPaint>();
            var kit = new PaintKit(list, null, 1u) { Mask = PaintShape.Polygon(Square(0, 0, 20, 40), 0.5f) };
            kit.Fill(Square(0, 0, 40, 40), TerrainMaterials.Dry, 1f, 1f, 0f);
            var stamps = PaintRaster.Build(list, Fine);
            Assert.Equal(1f, PaintRaster.Sample(stamps, TerrainMaterials.Dry, new float2(10, 20)), 2);
            Assert.Equal(0f, PaintRaster.Sample(stamps, TerrainMaterials.Dry, new float2(30, 20)), 2);
        }

        [Fact]
        public void KitBudgetAndQuantisationFloor()
        {
            var list = new List<PlannedPaint>();
            var kit = new PaintKit(list, null, 1u) { MaxPaints = 5 };
            for (int i = 0; i < 9; i++) kit.Radial(new float2(i * 10, 0), TerrainMaterials.Dirt, 0.5f, 1f, 3f);
            Assert.Equal(5, list.Count);
            Assert.Equal(5, kit.Count);
            Assert.Equal(4, kit.Dropped);
            // Below one 4-bit step (with dither) nothing is painted, and nothing is counted.
            var faint = new PaintKit(new List<PlannedPaint>(), null, 1u);
            Assert.Null(faint.Radial(float2.zero, TerrainMaterials.Dirt, 0.02f, 1f, 3f));
            // Amount scales; MaxStrength caps.
            var k2 = new PaintKit(new List<PlannedPaint>(), null, 1u) { Amount = 2f, MaxStrength = 0.7f };
            Assert.Equal(0.7f, k2.Radial(float2.zero, TerrainMaterials.Dirt, 0.5f, 1f, 3f).Strength, 3);
        }

        [Fact]
        public void KnockoutWeakensOtherMaterialsOnly()
        {
            var list = new List<PlannedPaint>();
            var kit = new PaintKit(list, null, 1u);
            kit.Fill(Square(0, 0, 40, 40), TerrainMaterials.Lush, 0.5f, 1f, 0f);
            kit.Stroke(new[] { new float2(0, 20), new float2(40, 20) }, TerrainMaterials.Dirt, 1f, 4f, 0.5f, knockout: true);
            var stamps = PaintRaster.Build(list, Fine);
            Assert.Equal(0f, PaintRaster.Sample(stamps, TerrainMaterials.Lush, new float2(20, 20)), 2);       // under the path
            Assert.Equal(0.5f, PaintRaster.Sample(stamps, TerrainMaterials.Lush, new float2(20, 30)), 2);     // lawn elsewhere
            Assert.Equal(1f, PaintRaster.Sample(stamps, TerrainMaterials.Dirt, new float2(20, 20)), 2);       // the path itself
            // Without the flag both weights stay (the splatmap keeps one weight per material).
            list[1].Knockout = false;
            var mixed = PaintRaster.Build(list, Fine);
            Assert.Equal(0.5f, PaintRaster.Sample(mixed, TerrainMaterials.Lush, new float2(20, 20)), 2);
        }

        [Fact]
        public void ErasesRunFirstAndKeepTheirMaterial()
        {
            var list = new List<PlannedPaint>();
            var kit = new PaintKit(list, null, 1u);
            kit.Fill(Square(0, 0, 20, 20), TerrainMaterials.Dirt, 0.8f, 1f, 0f);
            kit.Erase(Square(0, 0, 20, 20), TerrainMaterials.Lush, 1f, 1f);
            kit.Erase(Square(30, 0, 50, 20), TerrainMaterials.Erase, 1f, 1f);
            var stamps = PaintRaster.Build(list, Fine);
            Assert.True(stamps[0].Erase && stamps[0].Layer == TerrainMaterials.Erase, "clear-all first");
            Assert.True(stamps[1].Erase && stamps[1].Layer == TerrainMaterials.Lush, "then per-material erases");
            Assert.False(stamps.Last().Erase);
            Assert.Equal(TerrainMaterials.Dirt, stamps.Last().Layer);
            // An erase never reads as paint.
            Assert.Equal(0f, PaintRaster.Sample(stamps, TerrainMaterials.Lush, new float2(10, 10)), 3);
        }

        [Fact]
        public void ShapePaintsSurviveLotCutting()
        {
            var p = new PlannedPaint { Ring = Poly.To3D(Square(0, 0, 40, 40), _ => 0f), Layer = TerrainMaterials.Dirt, Strength = 1f, Shape = PaintShape.Polygon(Square(0, 0, 40, 40), 1f) };
            var cut = p.WithRing(p.Ring);
            cut.Shape = p.Shape.Subtract(PaintShape.Union(new[] { PaintShape.Polygon(Square(10, 10, 20, 20), 0.25f) }));
            var stamps = PaintRaster.Build(new[] { cut }, Fine);
            Assert.Equal(0f, PaintRaster.Sample(stamps, TerrainMaterials.Dirt, new float2(15, 15)), 2);
            Assert.Equal(1f, PaintRaster.Sample(stamps, TerrainMaterials.Dirt, new float2(30, 30)), 2);
        }

        [Fact]
        public void HugeShapeFieldsStayWithinTheStampBudget()
        {
            var list = new List<PlannedPaint>();
            var kit = new PaintKit(list, null, 1u);
            kit.Speckle(Square(0, 0, 3000, 2000), TerrainMaterials.Dry, 0.4f, 12f, 0.3f);
            var o = new PaintRasterOptions { Cell = 1.75f, Align = 3.5f, TileCells = 512, MaxStamps = 24 };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stamps = PaintRaster.Build(list, o);
            Assert.InRange(stamps.Count, 1, 24);
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(6000), sw.ElapsedMilliseconds + " ms");
        }

        // ───────────────────────── palette ─────────────────────────

        [Fact]
        public void VanillaPaletteIsTheIdentityAndMatchesTheCensus()
        {
            var v = TerrainPalette.Vanilla();
            Assert.True(v.IsVanilla);
            for (int i = 0; i <= 4; i++) Assert.Equal(i, v.Remap(i));
            // Forest floor is the darkest slot (mulch), sand the lightest; base grass is greener than dry grass.
            float L(int s) => TerrainPalette.Lab(v.Slots[s].Color).x;
            Assert.True(L(2) < L(1) && L(2) < L(3) && L(2) < L(4));
            Assert.True(L(4) > L(3) && L(4) > L(1));
            Assert.Equal(2, v.Nearest(new float3(55, 48, 36) / 255f));     // dark mulch → forest floor
            Assert.Equal(TerrainMaterials.Lush, v.SlotFor(GroundTone.Lush));
            Assert.Equal(0f, v.Mismatch(GroundTone.Dirt), 2);
        }

        private static TerrainPalette Custom(params (string tex, int r, int g, int b)[] slots)
        {
            var p = new TerrainPalette { Name = "Custom" };
            for (int i = 0; i < 4; i++)
                p.Slots[i + 1] = new TerrainSlot { Index = i + 1, Texture = slots[i].tex, Color = new float3(slots[i].r, slots[i].g, slots[i].b) / 255f, Measured = true };
            return p;
        }

        [Fact]
        public void IntentsFollowTextureNamesOnACustomMap()
        {
            // An arid map maker put sand first and dirt last.
            var p = Custom(("Sand_arid_01_Basecolor", 161, 133, 120), ("Grass_fertile_arid_01_Basecolor", 104, 94, 72),
                ("Grass_dry_arid_01_Basecolor", 144, 114, 91), ("Rocky_dirt_tropic_01_Basecolor", 92, 84, 71));
            Assert.False(p.IsVanilla);
            Assert.Equal(1, p.SlotFor(GroundTone.Sand));
            Assert.Equal(2, p.SlotFor(GroundTone.Lush));
            Assert.Equal(3, p.SlotFor(GroundTone.Dry));
            Assert.Equal(4, p.SlotFor(GroundTone.Dirt));
            _out.WriteLine(p.Describe());
        }

        [Fact]
        public void UnnamedSlotsFallBackToTheClosestColour()
        {
            // Vanilla colours shuffled into other slots, no names (a mod texture, or the name could not be read).
            var p = Custom(("", 119, 112, 102), ("", 97, 89, 78), ("", 59, 62, 39), ("", 79, 79, 64));
            Assert.Equal(1, p.SlotFor(GroundTone.Sand));
            Assert.Equal(2, p.SlotFor(GroundTone.Dirt));
            Assert.Equal(3, p.SlotFor(GroundTone.Lush));
            Assert.Equal(4, p.SlotFor(GroundTone.Dry));
            var plan = new ComposedPlan();
            var l = new AreaPlan();
            l.Paint.Add(new PlannedPaint { Ring = Poly.To3D(Square(0, 0, 5, 5), _ => 0f), Layer = TerrainMaterials.Dirt, Strength = 1f });
            l.Paint.Add(new PlannedPaint { Ring = Poly.To3D(Square(0, 0, 5, 5), _ => 0f), Layer = TerrainMaterials.Erase, Strength = 1f });
            plan.Layers.Add(l);
            TerrainPalette.Remap(plan, p);
            Assert.Equal(2, l.Paint[0].Layer);
            Assert.Equal(0, l.Paint[1].Layer);
            Assert.Equal(TerrainPalette.Vanilla().Slots[3].Color, TerrainPalette.Known.First(k => k.Name == "Light_dirt_temperate_01_Basecolor").Color);
            Assert.Equal(new float3(162, 134, 116) / 255f, TerrainPalette.KnownColor("dirt_arid_01_basecolor").Value);
        }

        // ───────────────────────── lawn ─────────────────────────

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan Compose(string layer, uint seed, Action<Recipe> edit = null, string preset = null)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), seed);
            Editor.ApplyDefaults(r);
            if (preset != null) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            edit?.Invoke(r);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            var ring = TestKit.Rect(120, 100);
            var site = new SiteContext();
            TestKit.Entered(r, ring, site);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static double LawnSurfaceShare(ComposedPlan p) =>
            p.Layers.SelectMany(l => l.Surfaces).Where(s => s.Role == SurfaceRole.Grass).Sum(s => Poly.Area(Poly.To2D(s.Ring))) / (120.0 * 100.0);

        [Theory]
        [InlineData("park", "painted")]
        [InlineData("picnicGrounds", "painted")]
        [InlineData("dogPark", "painted")]
        [InlineData("greenwayPark", "painted")]
        [InlineData("adventurePlayground", "painted")]
        [InlineData("animalPark", "painted")]
        [InlineData("formalGarden", "surface")]
        [InlineData("memorialPark", "painted")]   // (parks makeover) painted terrain with the parks' decal ground
        [InlineData("cemeteryGrounds", "surface")]
        [InlineData("botanicalGarden", "painted")]
        [InlineData("sportsPark", "surface")]
        [InlineData("fairground", "surface")]
        public void LawnDefaultsPerType(string type, string expected)
        {
            var gen = GeneratorRegistry.Get(type);
            var c = gen.Controls.FirstOrDefault(x => x.Id == DressingControls.LawnId);
            Assert.NotNull(c);
            Assert.Equal(expected, c.Default);
            Assert.Equal(DressingControls.PaintGroup, c.Group);
            Assert.Equal(new[] { Lawn.Painted, Lawn.Surface }, c.Options.Select(o => o.Value).ToArray());
        }

        [Fact]
        public void ManicuredParkPresetsKeepTheSurface()
        {
            var park = GeneratorRegistry.Get("park");
            foreach (var p in park.Presets)
            {
                string want = p.Id == "formal" || p.Id == "victorian" || p.Id == "modern" ? Lawn.Surface : Lawn.Painted;   // (parks makeover) Garden walk is painted now
                Assert.Equal(want, p.Values.S(DressingControls.LawnId, null));
            }
            // A type without a lawn has no choice.
            Assert.DoesNotContain(GeneratorRegistry.Get("plaza").Controls, x => x.Id == DressingControls.LawnId);
        }

        [Fact]
        public void PaintedLawnLeavesBareTerrainWithLawnPaintAndMulch()
        {
            var painted = Compose("park", 7);
            var surface = Compose("park", 7, r => r.Root.Settings.Values[DressingControls.LawnId] = Lawn.Surface);
            double sp = LawnSurfaceShare(painted), ss = LawnSurfaceShare(surface);
            _out.WriteLine($"grass surface share: painted {sp:0.00}, surface {ss:0.00}");
            Assert.True(ss > 0.6, "the surface lawn should cover the park");
            Assert.True(sp < 0.2, "a painted lawn must not lay the whole grass surface");
            var paints = painted.Layers.SelectMany(l => l.Paint).ToList();
            Assert.Contains(paints, p => p.Shape != null && p.Layer == TerrainMaterials.Lush);   // lawn tint and/or mulch
            Assert.Contains(paints, p => p.Shape != null && p.Layer == TerrainMaterials.Dry);
            // Nothing is painted under the paths or the plaza / play surfaces that remain.
            var stamps = PaintRaster.Build(paints, new PaintRasterOptions { Cell = 1f, Align = 1f, TileCells = 1024 });
            var covers = painted.Layers.SelectMany(l => l.Surfaces).Where(s => s.Ring.Length >= 3).Select(s => Poly.To2D(s.Ring)).ToList();
            int under = 0, tested = 0;
            foreach (var c in covers)
            {
                var reg = LayerRegion.From(c);
                if (reg == null || reg.Area < 30f) continue;
                var p = reg.DeepestPoint(out float ins, null, 8);
                if (ins < 2.5f) continue;
                tested++;
                for (int layer = 1; layer <= 4; layer++)
                    if (PaintRaster.Sample(stamps, layer, p) > 0.05f) under++;
            }
            _out.WriteLine($"surfaces tested {tested}, painted under {under}");
            Assert.Equal(0, under);
        }

        [Fact]
        public void MulchFollowsTheTreesAndItsToggle()
        {
            var on = Compose("park", 9, r => r.Root.Settings.Values["treeDensity"] = 60.0);
            var off = Compose("park", 9, r => { r.Root.Settings.Values["treeDensity"] = 60.0; r.Root.Settings.Values[DressingControls.MulchId] = false; });
            var trees = on.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Tree).ToList();
            Assert.NotEmpty(trees);
            var stamps = PaintRaster.Build(on.Layers.SelectMany(l => l.Paint).ToList(), new PaintRasterOptions { Cell = 1f, Align = 1f, TileCells = 1024 });
            var offStamps = PaintRaster.Build(off.Layers.SelectMany(l => l.Paint).ToList(), new PaintRasterOptions { Cell = 1f, Align = 1f, TileCells = 1024 });
            double onSum = 0, offSum = 0;
            foreach (var t in trees) { onSum += PaintRaster.Sample(stamps, TerrainMaterials.Lush, t.Position.xz + 1f); offSum += PaintRaster.Sample(offStamps, TerrainMaterials.Lush, t.Position.xz + 1f); }
            _out.WriteLine($"mean forest-floor weight under trees: on {onSum / trees.Count:0.00}, off {offSum / trees.Count:0.00}");
            Assert.True(onSum / trees.Count > offSum / trees.Count + 0.15, "no mulch under the trees");
        }

        [Fact]
        public void GroundPaintIsDeterministic()
        {
            var a = Compose("picnicGrounds", 3);
            var b = Compose("picnicGrounds", 3);
            var pa = a.Layers.SelectMany(l => l.Paint).ToList(); var pb = b.Layers.SelectMany(l => l.Paint).ToList();
            Assert.Equal(pa.Count, pb.Count);
            var o = new PaintRasterOptions { Cell = 2f, Align = 2f, TileCells = 512 };
            var sa = PaintRaster.Build(pa, o); var sb = PaintRaster.Build(pb, o);
            Assert.Equal(sa.Count, sb.Count);
            for (int i = 0; i < sa.Count; i++) { Assert.Equal(sa[i].Layer, sb[i].Layer); Assert.Equal(sa[i].Mask, sb[i].Mask); }
        }

        [Fact]
        public void GroundPaintAloneNeedsNoAcknowledgement()
        {
            var plan = new ComposedPlan();
            var layer = new AreaPlan();
            layer.Paint.Add(new PlannedPaint { Ring = Poly.To3D(Square(0, 0, 20, 20), _ => 0f), Layer = TerrainMaterials.Dirt, Strength = 0.5f });
            plan.Layers.Add(layer);
            var effects = UiModels.Permanent(plan);
            Assert.Single(effects);
            Assert.False(UiModels.NeedsAck(effects));
            Assert.Equal("", UiModels.PermanentFingerprint(effects));
            var j = JsonValue.Parse(UiModels.Summary(new SummaryInput { Plan = plan }).ToString());
            Assert.False(j["permanentNeedsAck"].AsBool());
            Assert.True(j["permanentAcknowledged"].AsBool());
            // With a terraform the tick is back, and repainting more does not reset it.
            layer.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(Square(0, 0, 10, 10), _ => 0f), Permanent = true });
            var withLake = UiModels.Permanent(plan);
            Assert.True(UiModels.NeedsAck(withLake));
            string fp = UiModels.PermanentFingerprint(withLake);
            layer.Paint.Add(new PlannedPaint { Ring = Poly.To3D(Square(30, 0, 60, 20), _ => 0f), Layer = TerrainMaterials.Dry, Strength = 0.5f });
            Assert.Equal(fp, UiModels.PermanentFingerprint(UiModels.Permanent(plan)));
            // Shape paints count their probed area, not their support box.
            var kit = new PaintKit(layer.Paint, null, 1u);
            var r = kit.Radial(new float2(200, 200), TerrainMaterials.Lush, 1f, 0f, 5f, Falloff.Linear);
            Assert.InRange(r.AreaEstimate, 40f, 110f);
        }

        [Fact]
        public void GroundPaintComposeStaysFast()

        {
            Compose("park", 1);   // warm up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var plan = Compose("park", 2, r => r.Root.Settings.Values["treeDensity"] = 80.0);
            long compose = sw.ElapsedMilliseconds;
            sw.Restart();
            var stamps = PaintRaster.Build(plan.Layers.SelectMany(l => l.Paint).ToList(), new PaintRasterOptions());
            _out.WriteLine($"compose {compose} ms, raster {sw.ElapsedMilliseconds} ms, {stamps.Count} stamps");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(1500), "raster " + sw.ElapsedMilliseconds + " ms");
        }
    }
}
