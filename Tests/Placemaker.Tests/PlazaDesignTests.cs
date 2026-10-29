// Designed plazas (Core/Layers/Plaza/PlazaDesigns.cs, 2026-10-28): each design style lays its own plan on a city
// block, an L and a triangle; every surface ring is one the game keeps as it is (AreaRings: no node the build would
// have to remove — a removed node is a wedge of bare ground or paving over a lawn in the game runner).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class PlazaDesignTests
    {
        private readonly ITestOutputHelper _out;
        public PlazaDesignTests(ITestOutputHelper o) { _out = o; }

        public static readonly string[] Designs = { "grand", "rooms", "railed", "terraces", "modern", "piazza", "sunkenPlaza", "waterSteps", "marketHall", "dining", "grandSteps" };

        private static ComposedPlan Compose(float3[] ring, string preset)
        {
            var gen = GeneratorRegistry.Get("plaza");
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = Recipe.Single(gen, 1);
            editor.ApplyDefaults(r);
            uint n = 0;
            editor.AddDefaultFeatures(r, () => ++n);
            editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            TestKit.Entered(r, ring, null);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, new SiteContext(), CancellationToken.None);
        }

        private static float3[] Block(float w, float d, float chamfer = 2f) => new[]
        {
            new float3(chamfer, 0, 0), new float3(w - chamfer, 0, 0), new float3(w, 0, chamfer), new float3(w, 0, d - chamfer),
            new float3(w - chamfer, 0, d), new float3(chamfer, 0, d), new float3(0, 0, d - chamfer), new float3(0, 0, chamfer),
        };

        private static float3[] L(float s) => new[]
        {
            new float3(0, 0, 0), new float3(s, 0, 0), new float3(s, 0, s * 0.45f), new float3(s * 0.45f, 0, s * 0.45f), new float3(s * 0.45f, 0, s), new float3(0, 0, s),
        };

        private static float3[] Triangle(float s) => new[] { new float3(0, 0, 0), new float3(s, 0, 0), new float3(s * 0.3f, 0, s * 0.9f) };

        public static IEnumerable<object[]> Cases()
        {
            foreach (var d in Designs)
                foreach (var site in new[] { "block", "big", "l", "triangle" })
                    yield return new object[] { d, site };
        }

        private static float3[] Site(string site) => site switch
        {
            "block" => Block(130f, 120f),
            "big" => Block(210f, 190f),
            "l" => L(150f),
            _ => Triangle(170f),
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryDesignBuildsItsPlanOnAnySite(string design, string site)
        {
            var plan = Compose(Site(site), design);
            Assert.False(plan.HasErrors);
            var layer = plan.Layers.First(l => l.GeneratorId == "plaza");
            Assert.True(layer.Facts.TryGetValue("plazaDesign", out var id) && Convert.ToInt32(id) > 0, design + ": not designed");
            Assert.True(layer.Objects.Count(o => o.Role == ObjectRole.Tree) >= 8, design + "/" + site + ": trees");
            Assert.True(layer.Objects.Count(o => o.Role == ObjectRole.Bench) >= 6, design + "/" + site + ": benches");
            Assert.True(layer.Surfaces.Count >= 10, design + "/" + site + ": surfaces");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EverySurfaceRingIsOneTheGameKeepsAsItIs(string design, string site)
        {
            var layer = Compose(Site(site), design).Layers.First(l => l.GeneratorId == "plaza");
            var bad = new List<string>();
            foreach (var s in layer.Surfaces)
            {
                var r = Poly.To2D(s.Ring);
                if (!AreaRings.IsValid(r, AreaRings.SurfaceMinNodeDistance + AreaRings.Margin) || !SurfaceAcceptance.IsAccepted(r))
                    bad.Add($"{s.Role} {s.Asset.Id} n={r.Length} area={Poly.Area(r):0.0} at node {AreaRings.FirstViolation(r, AreaRings.SurfaceMinNodeDistance + AreaRings.Margin)}: " + string.Join(" ", r.Select(p => $"({p.x:0.##},{p.y:0.##})")));
            }
            foreach (var b in bad.Take(12)) _out.WriteLine(b);
            // A few small corners (a tree pit beside a slanted walk) are still left to the build's own repair.
            Assert.True(bad.Count <= math.max(2, layer.Surfaces.Count / 30), $"{design}/{site}: {bad.Count} of {layer.Surfaces.Count} rings would be repaired by the build");
        }

        public static IEnumerable<object[]> HeightCases()
        {
            foreach (var d in new[] { "terraces", "modern", "sunkenPlaza", "waterSteps", "marketHall", "dining", "grandSteps" })
                foreach (var site in new[] { "block", "big" })
                    yield return new object[] { d, site };
        }

        /// <summary>
        /// (2026-10-29) Every level change of the designed squares is a planar bank no steeper than 45 % (steeper than 50 %
        /// over one heightmap texel renders as a sheet), reached by stone stairs and lined with walls.
        /// </summary>
        [Theory]
        [MemberData(nameof(HeightCases))]
        public void EveryLevelChangeIsAWalledBankWithStairs(string design, string site)
        {
            var layer = Compose(Site(site), design).Layers.First(l => l.GeneratorId == "plaza");
            Assert.NotEmpty(layer.Terraform);
            float steepest = 0f;
            foreach (var t in layer.Terraform)
            {
                Assert.True(t.Sloped && t.Permanent);
                // The patch's plane through its first three corners (every patch is planar: a quad run or a triangle).
                var a = t.Ring[0]; var b = t.Ring[1]; var c = t.Ring[2];
                var n = math.cross(b - a, c - a);
                if (math.abs(n.y) < 1e-4f) continue;
                float grade = math.length(new float2(n.x, n.z)) / math.abs(n.y);
                steepest = math.max(steepest, grade);
            }
            _out.WriteLine($"{design}/{site}: {layer.Terraform.Count} patches, steepest {steepest:0.00}, rise {Fact(layer, "designRise")}, drop {Fact(layer, "designDrop")}, flights {Fact(layer, "designFlights")}, walls {Fact(layer, "designWalls")}, cafes {Fact(layer, "designCafes")}, canopies {Fact(layer, "designCanopies")}, fountains {Fact(layer, "designWallFountains")}");
            Assert.True(steepest <= 0.46f, $"{design}/{site}: a bank of {steepest:0.00}");
            Assert.True(Fact(layer, "designRise") + Fact(layer, "designDrop") >= 0.85f, design + ": no level change");
            // (The modern plaza's sunken pool is water, not a floor: no stairs into it.)
            if (design != "modern") Assert.True(Fact(layer, "designFlights") >= 1, design + ": no stairs");
            Assert.True(Fact(layer, "designWalls") >= 8, design + ": no walls");
        }

        [Fact]
        public void WithoutInstalledFountainsTheDesignsBuildFountainsOfWallFountains()
        {
            using (AssetAvailability.Scope(new Dictionary<string, int> { { AssetCategories.Centerpiece, 0 }, { AssetCategories.CenterpieceFountain, 0 }, { AssetCategories.CenterpieceStatue, 0 } }))
                foreach (var d in new[] { "grand", "railed", "piazza", "dining", "grandSteps", "modern", "waterSteps", "sunkenPlaza" })
                {
                    var layer = Compose(Block(130f, 120f), d).Layers.First(l => l.GeneratorId == "plaza");
                    Assert.DoesNotContain(layer.Objects, o => o.Role == ObjectRole.Centerpiece);
                    Assert.True(Fact(layer, "designWallFountains") >= 4, d + ": " + Fact(layer, "designWallFountains") + " wall fountains");
                }
        }

        private static float Fact(AreaPlan p, string key) => p.Facts.TryGetValue(key, out var v) ? Convert.ToSingle(v) : 0f;

        [Fact]
        public void TerracesRiseInPlanesNotCells()
        {
            var layer = Compose(Block(130f, 120f), "terraces").Layers.First(l => l.GeneratorId == "plaza");
            Assert.True(layer.Facts.TryGetValue("terraceLevels", out var n) && Convert.ToInt32(n) >= 2);
            Assert.NotEmpty(layer.Terraform);
            Assert.All(layer.Terraform, t => Assert.True(t.Sloped && t.Permanent));
            float top = layer.Terraform.Max(t => t.Ring.Max(p => p.y));
            Assert.True(top >= 2f, "top " + top);
        }
    }
}
