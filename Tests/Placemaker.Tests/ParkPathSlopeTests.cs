using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    /// <summary>(p1-ground) Walks on hills run across the slope and zig-zag where they must climb (the user: paths on a big hill).</summary>
    public class ParkPathSlopeTests
    {
        private readonly ITestOutputHelper _out;
        public ParkPathSlopeTests(ITestOutputHelper o) { _out = o; }

        // the game runner's "mountains" (25 m, 320 m wavelength) around the site's middle
        private static float Mountains(float2 p)
        {
            float k = 2f * math.PI / 320f; float u = p.x - 130f, v = p.y - 110f;
            return 25f * (0.55f * math.sin((u * 0.8f + v * 0.6f) * k) + 0.3f * math.sin((-u * 0.5f + v * 0.9f) * k * 2f + 1.3f)
                + 0.15f * math.sin((u * 0.3f - v) * k * 4f + 0.7f));
        }

        [System.ThreadStatic] private static ITestOutputHelper Out;
        private static (double steep, double cut) Run(string id, string preset, bool off)
        {
            var gen = GeneratorRegistry.Get(id);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var site = TestKit.RoadAlongBottom(260);
            site.HeightAt = Mountains;
            var ring = TestKit.Rect(260, 220).Select(p => new float3(p.x, Mountains(p.xz), p.z)).ToArray();
            var recipe = editor.SingleLayerRecipe(gen, 3);
            if (preset != null) editor.ApplyPreset(recipe, "0", preset);
            TestKit.Entered(recipe, ring, site);
            ParkPathPlanner.SlopeCostOff = off;
            try
            {
                var plan = Composer.Compose(recipe, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
                if (!off)
                {
                    var by = new System.Collections.Generic.Dictionary<string, double>();
                    foreach (var l in plan.Layers)
                        foreach (var n in l.Nets)
                        {
                            if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                            for (int k = 0; k < 16; k++)
                            {
                                var a = Composer.Bezier(n.A, n.B, n.C, n.D, k / 16f).xz; var b = Composer.Bezier(n.A, n.B, n.C, n.D, (k + 1) / 16f).xz;
                                float d = math.distance(a, b); if (d < 0.5f) continue;
                                if (math.abs(Mountains(b) - Mountains(a)) / d > 0.12f) { string key = l.GeneratorId + ":" + (n.Control ?? "") + ":" + (n.Category ?? ""); by[key] = (by.TryGetValue(key, out var v) ? v : 0) + d; }
                            }
                        }
                    foreach (var kv in by.OrderByDescending(x => x.Value)) Out?.WriteLine($"  steep {kv.Key}: {kv.Value:F0} m");
                }
                var f = plan.Layers[0].Facts;
                return (f.TryGetValue("harmonySteepEdges", out var s) ? s : -1, f.TryGetValue("harmonyMaxCut", out var c) ? c : -1);
            }
            finally { ParkPathPlanner.SlopeCostOff = false; }
        }

        [Theory]
        [InlineData("park", "natural")]
        [InlineData("park", "garden")]
        [InlineData("park", "wild")]
        public void WalksOnMountainsAreFarLessSteep(string id, string preset)
        {
            Out = _out;
            var before = Run(id, preset, true);
            var after = Run(id, preset, false);
            _out.WriteLine($"{id}/{preset}: steep path pieces {before.steep} -> {after.steep}, deepest cut {before.cut:F1} -> {after.cut:F1} m");
            Assert.True(after.steep < before.steep * 0.75, $"steep pieces {before.steep} -> {after.steep}");
        }
    }
}
