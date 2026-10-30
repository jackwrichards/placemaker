// s28 phase 2: ground wear painted into the terrain texture (Core/Decals/GroundWear.cs).

using System;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Decals;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class GroundWearTests
    {
        private readonly ITestOutputHelper _out;
        public GroundWearTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static Recipe Recipe(string layer, uint seed, Action<Recipe> edit = null)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), seed);
            Editor.ApplyDefaults(r);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            edit?.Invoke(r);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            return r;
        }

        private static ComposedPlan Compose(Recipe r)
        {
            var ring = TestKit.Rect(120, 100);
            var site = new SiteContext();
            TestKit.Entered(r, ring, site);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static int Paints(ComposedPlan p) => p.Layers.Sum(l => l.Paint.Count);

        private static Action<Recipe> Set(params (string id, object v)[] values) => r => { foreach (var (id, v) in values) r.Root.Settings.Values[id] = v; };

        [Fact]
        public void BareLawnGetsFaintWearAndZeroPaintsNothing()
        {
            GroundDesignTests.Lib();   // the shared decal library, for the weathered ground below
            var bare = Compose(Recipe("park", 7, Set(("groundSurface", false))));
            var none = Compose(Recipe("park", 7, Set(("groundSurface", false), (DressingControls.PaintWearId, 0.0))));
            int added = Paints(bare) - Paints(none);
            _out.WriteLine($"ground wear paints: {added}");
            // (parks makeover, 2026-10-29) a park is weathered by the shared decal library (ParkGround): the paint keeps only its soft
            // tone (lawn tint, the lived-in mulch under the crowns); the wear that paint used to fake is laid as decals.
            Assert.True(added >= 1, $"only {added} ground-wear paints on a bare-lawn park");
            Assert.True(bare.Weathering != null && bare.Weathering.Instances > 0, "no decal ground");
            Assert.True(added <= GroundWear.MaxPaints);
            // Wear is dirt / dry grass (stronger since the 2026-10-14 tuning: 100 % = 3x the old base).
            foreach (var p in bare.Layers.SelectMany(l => l.Paint).Skip(0))
                if (p.Layer == TerrainMaterials.Dirt || p.Layer == TerrainMaterials.Dry) Assert.True(p.Strength <= 1f);
            var wear = bare.Layers.SelectMany(l => l.Paint).Where(p => (p.Layer == TerrainMaterials.Dirt || p.Layer == TerrainMaterials.Dry)).ToList();
            Assert.NotEmpty(wear);
            Assert.Contains(bare.AllIssues(), i => i.Key == TerrainPaint.PermanentKey);
        }

        [Fact]
        public void NoWearUnderAGrassSurface()
        {
            // s33: the park's default lawn is painted terrain; "Grass surface" brings the surface back.
            var covered = Compose(Recipe("park", 7, Set((DressingControls.LawnId, Lawn.Surface))));
            var none = Compose(Recipe("park", 7, Set((DressingControls.LawnId, Lawn.Surface), (DressingControls.PaintWearId, 0.0))));
            GroundDesignTests.Lib();   // the shared decal library, for the weathered ground below
            var bare = Compose(Recipe("park", 7, Set(("groundSurface", false))));
            var bareNone = Compose(Recipe("park", 7, Set(("groundSurface", false), (DressingControls.PaintWearId, 0.0))));
            int onSurface = Paints(covered) - Paints(none), onBare = Paints(bare) - Paints(bareNone);
            _out.WriteLine($"grass surface: {onSurface}, bare terrain: {onBare}");
            Assert.True(onSurface < onBare / 2 + 1, $"{onSurface} paints under a grass surface vs {onBare} on bare terrain");
            // Every wear paint centre is off the surfaces.
            var surfaces = covered.Layers.SelectMany(l => l.Surfaces).Where(s => s.Ring.Length >= 3).Select(s => Poly.To2D(s.Ring)).ToList();
            // s33: wear is a masked field: sample the real raster at points deep inside every surface.
            var stamps = Placemaker.Core.Build.PaintRaster.Build(covered.Layers.SelectMany(l => l.Paint).Skip(Paints(none)).ToList(),
                new Placemaker.Core.Build.PaintRasterOptions { Cell = 1f, Align = 1f, TileCells = 1024 });
            foreach (var s in surfaces)
            {
                var reg = LayerRegion.From(s);
                if (reg == null || reg.Area < 30f) continue;
                var c = reg.DeepestPoint(out float ins, null, 8);
                if (ins < 2.5f) continue;
                for (int layer = 1; layer <= 4; layer++) Assert.True(Placemaker.Core.Build.PaintRaster.Sample(stamps, layer, c) < 0.05f, $"paint {layer} under a surface at {c}");
            }
        }

        [Fact]
        public void DesirePathsToggleAndDeterminism()
        {
            var on = Compose(Recipe("park", 11, Set(("groundSurface", false))));
            var off = Compose(Recipe("park", 11, Set(("groundSurface", false), (DressingControls.DesirePathsId, false))));
            var again = Compose(Recipe("park", 11, Set(("groundSurface", false))));
            Assert.True(Paints(on) >= Paints(off));
            Assert.Equal(Paints(on), Paints(again));
            var a = on.Layers.SelectMany(l => l.Paint).ToList(); var b = again.Layers.SelectMany(l => l.Paint).ToList();
            for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Layer, b[i].Layer); Assert.Equal(a[i].Strength, b[i].Strength); Assert.Equal(a[i].Ring[0], b[i].Ring[0]); }
        }

        [Fact]
        public void ControlsAreInTheDecalsSection()
        {
            var gen = GeneratorRegistry.Get("park");
            var wear = gen.Controls.First(c => c.Id == DressingControls.PaintWearId);
            var desire = gen.Controls.First(c => c.Id == DressingControls.DesirePathsId);
            // s33: they moved to the "Ground paint" section with the lawn choice, mulch, lawn variation and clear.
            Assert.Equal(DressingControls.PaintGroup, wear.Group);
            Assert.Equal(DressingControls.PaintGroup, desire.Group);
            foreach (var id in new[] { DressingControls.LawnId, DressingControls.MulchId, DressingControls.LawnTintId, DressingControls.ClearPaintId })
                Assert.Equal(DressingControls.PaintGroup, gen.Controls.First(c => c.Id == id).Group);
            Assert.Equal(false, gen.Controls.First(c => c.Id == DressingControls.ClearPaintId).Default);

            Assert.Equal(ControlKind.Toggle, desire.Kind);
            // A clean formal garden does not trample desire lines by default.
            Assert.Equal(false, GeneratorRegistry.Get("formalGarden").Controls.First(c => c.Id == DressingControls.DesirePathsId).Default);
        }
    }
}
