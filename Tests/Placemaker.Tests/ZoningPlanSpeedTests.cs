// (zoning speed) The user: "it takes so long to plan a zone thing ... It should be near instant." Zoning → Industrial estate →
// Default over a road grid of about 4 x 4 city blocks they had just placed (Level the lots on, natural ground off by a metre or
// so) sat on "Planning…" for a long time. Here the same: 4 x 4 blocks of the game runner's grid (Core/Suite/SuiteLayout), their
// streets' zone blocks as the game makes them, gently uneven ground, every zoning type and style planned and timed.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class ZoningPlanSpeedTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        private readonly ITestOutputHelper _out;
        public ZoningPlanSpeedTests(ITestOutputHelper output) { _out = output; }

        /// <summary>Blocks i0..i0+n−1 × j0..j0+n−1 of the runner's grid, their streets and zone blocks, ground off by about a metre.</summary>
        internal static SiteContext Grid(int n, out float3[] ring, float bumps = 1.2f)
        {
            var layout = new SuiteLayout { NX = n + 2, NZ = n + 2 };
            float x0 = layout.LineX(1), x1 = layout.LineX(1 + n), z0 = layout.LineZ(1), z1 = layout.LineZ(1 + n);
            float H(float2 p) => bumps * (math.sin(p.x / 47f) * math.cos(p.y / 61f) + 0.4f * math.sin((p.x + p.y) / 23f));
            var site = new SiteContext { Assets = CommercialS32Tests.Catalog(), LayoutScale = 0.5f, HeightAt = H };
            long id = 900;
            foreach (var s in layout.Roads())
            {
                var m = 0.5f * (s.A + s.B);
                if (m.x < x0 - 200f || m.x > x1 + 200f || m.y < z0 - 200f || m.y > z1 + 200f) continue;
                var a = new float3(s.A.x, H(s.A), s.A.y); var d = new float3(s.B.x, H(s.B), s.B.y);
                site.NearbyNets.Add(new NearbyNet
                {
                    ExternalId = id++, A = a, B = math.lerp(a, d, 1f / 3f), C = math.lerp(a, d, 2f / 3f), D = d,
                    Width = RoadRoles.Find(s.Prefab).Width, Category = NetCategory.Road, HasSidewalk = true,
                });
            }
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var cg = g; cg.Predicted = false; cg.Existing = false; site.ZoneGrids.Add(cg); }
            // the outline along the outer roads' centre lines (as the user drew it: corner to corner on the perimeter road)
            ring = new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) }
                .Select(p => new float3(p.x, H(p), p.y)).ToArray();
            return site;
        }

        private ComposedPlan Timed(string layer, string preset, SiteContext site, float3[] ring, out double ms)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 1);
            if (!string.IsNullOrEmpty(preset)) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            var sw = Stopwatch.StartNew();
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            ms = sw.Elapsed.TotalMilliseconds;
            return plan;
        }

        [Fact]
        public void IndustrialEstateOnFourByFourBlocks()
        {
            var site = Grid(4, out var ring);
            Timed("zoningIndustrial", null, site, ring, out _);   // warm-up (JIT, statics)
            var plan = Timed("zoningIndustrial", null, site, ring, out double ms);
            int cells = plan.Layers.Sum(l => l.Zoning.Count);
            _out.WriteLine($"zoningIndustrial default: {ms:0} ms, {cells} cells, {site.ZoneGrids.Count} zone blocks, {site.NearbyNets.Count} roads");
            _out.WriteLine(PlanTimings.Describe(plan));
            foreach (var l in plan.Layers) _out.WriteLine(string.Join(" ", l.Facts.Select(kv => kv.Key + "=" + kv.Value.ToString("0.#"))));
            _out.WriteLine("fingerprint " + Fingerprint(plan, ring));
            Assert.True(cells > 1000, $"{cells} cells");
        }

        /// <summary>Every zoning type (the neighbourhoods and the looks) with each of its styles: near instant on 4 x 4 blocks.</summary>
        [Fact]
        public void EveryZoningTypeAndStylePlansFourByFourBlocksQuickly()
        {
            var site = Grid(4, out var ring);
            Timed("zoningIndustrial", null, site, ring, out _);   // warm-up
            var slow = new List<string>();
            double worst = 0;
            foreach (var g in GeneratorRegistry.All.Where(g => g is Core.Layers.Types.Zoning.PolishLookLayer))
                foreach (var preset in new string[] { null }.Concat(g.Presets.Select(p => p.Id)))
                {
                    var plan = Timed(g.Id, preset, site, ring, out double ms);
                    worst = System.Math.Max(worst, ms);
                    _out.WriteLine($"{g.Id} {preset ?? "(default)"}: {ms:0} ms, {plan.Layers.Sum(l => l.Zoning.Count)} cells");
                    if (ms > 2000) slow.Add($"{g.Id} {preset}: {ms:0} ms ({PlanTimings.Describe(plan)})");
                }
            _out.WriteLine($"worst {worst:0} ms");
            Assert.True(slow.Count == 0, string.Join("\n", slow));
        }

        [Fact]
        public void HillyWithWallsOnFourByFourBlocks()
        {
            var site = Grid(4, out var ring, bumps: 4f);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("zoningSuburb"), 1);
            Editor.SetValue(r, RecipePaths.Root, "levelWalls", true, false);
            var sw = Stopwatch.StartNew();
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            _out.WriteLine($"zoningSuburb, walls on, hills: {sw.Elapsed.TotalMilliseconds:0} ms, {plan.Layers.Sum(l => l.Zoning.Count)} cells");
            _out.WriteLine(PlanTimings.Describe(plan));
            foreach (var l in plan.Layers) _out.WriteLine(string.Join(" ", l.Facts.Select(kv => kv.Key + "=" + kv.Value.ToString("0.#"))));
            _out.WriteLine("fingerprint " + Fingerprint(plan, ring));
        }

        /// <summary>The zoned cells, the terraform and the ground after it, as one number (the same plan: the same number).</summary>
        internal static string Fingerprint(ComposedPlan plan, float3[] ring)
        {
            unchecked
            {
                long h = 17;
                void Add(float v) { h = h * 31 + (long)math.round(v * 1000f); }
                foreach (var l in plan.Layers)
                {
                    foreach (var z in l.Zoning) { Add(z.A.x); Add(z.A.y); Add(z.A.z); Add(z.C.x); Add(z.C.y); Add(z.C.z); h = h * 31 + Seeds.Hash(z.ZoneId ?? ""); }
                    foreach (var t in l.Terraform) foreach (var q in t.Ring) { Add(q.x); Add(q.y); Add(q.z); }
                    foreach (var p in l.Paint) h = h * 31 + p.Ring.Length;
                    if (l.HeightAfter != null)
                        for (float x = ring[0].x - 40f; x <= ring[2].x + 40f; x += 7.3f)
                            for (float z = ring[0].z - 40f; z <= ring[2].z + 40f; z += 7.3f) Add(l.HeightAfter(new float2(x, z)));
                }
                return h.ToString("X");
            }
        }
    }
}
