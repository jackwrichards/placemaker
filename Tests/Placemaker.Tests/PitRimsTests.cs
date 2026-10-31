using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    /// <summary>(p1-ground) Every type that digs a hole: no fence round it, no pile hanging over its edge (the user, 2026-10-30).</summary>
    public class PitRimsTests
    {
        private readonly ITestOutputHelper _out;
        public PitRimsTests(ITestOutputHelper o) { _out = o; }

        private static readonly (string id, string preset)[] Types =
        {
            ("quarry", "openPit"), ("quarry", "abandoned"), ("quarry", "gravelPit"), ("quarry", "sandPit"),
            ("constructionSite", "foundation"), ("constructionSite", "earthworks"), ("constructionSite", "demolition"),
            ("urbanRuins", "buildingRuins"), ("landfill", "active"),
        };

        [Fact]
        public void NoFenceRoundAHoleAndNoPileOverItsEdge()
        {
            var failures = new List<string>();
            var editor = new DesignEditor(GeneratorRegistry.Get);
            foreach (var ground in new[] { "flat", "slope" })
                foreach (var (id, preset) in Types)
                {
                    var gen = GeneratorRegistry.Get(id);
                    if (gen == null) { failures.Add("no generator " + id); continue; }
                    var ring = TestKit.Rect(220, 200);
                    var site = TestKit.RoadAlongBottom(220);
                    if (ground == "slope") site.HeightAt = p => 0.08f * p.y;
                    ring = ring.Select(p => new float3(p.x, site.HeightAt(p.xz), p.z)).ToArray();
                    var recipe = editor.SingleLayerRecipe(gen, 7);
                    if (gen.Presets.Any(p => p.Id == preset)) editor.ApplyPreset(recipe, "0", preset);
                    TestKit.Entered(recipe, ring, site);
                    var plan = Composer.Compose(recipe, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
                    var f = PitRims.Measure(plan, site.HeightAt);
                    int fences = 0, piles = 0;
                    if (f != null)
                        foreach (var l in plan.Layers)
                        {
                            foreach (var n in l.Nets)
                                if (n.Role == NetRole.Fence)
                                    for (int k = 1; k < 16; k++)
                                        if (f.InFenceZone(Composer.Bezier(n.A, n.B, n.C, n.D, k / 16f).xz)) { fences++; break; }
                            foreach (var o in l.Objects)
                            {
                                if (Placemaker.Core.Layers.Common.Cutouts.IsBorder(o) && f.InFenceZone(o.Position.xz)) fences++;
                                if (PitRims.IsPile(o) && PitRims.Straddles(f, o.Position.xz, SiteHarmony.FootprintRadius(o, site))) piles++;
                            }
                        }
                    double cut = plan.Layers.Count > 0 && plan.Layers[0].Facts.TryGetValue("pitFencesCut", out var c) ? c : 0;
                    double gone = plan.Layers.Count > 0 && plan.Layers[0].Facts.TryGetValue("pitPilesRemoved", out var g) ? g : 0;
                    _out.WriteLine($"{id}/{preset}/{ground}: dug cells {f?.Dug ?? 0}, fences cut {cut}, piles removed {gone}; left: {fences} fences, {piles} piles");
                    if (fences > 0 || piles > 0) failures.Add($"{id}/{preset}/{ground}: {fences} fences, {piles} piles at the hole");
                }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
    }
}
