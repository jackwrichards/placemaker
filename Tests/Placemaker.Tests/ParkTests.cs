using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class ParkTests
    {
        private readonly ITestOutputHelper _out;
        public ParkTests(ITestOutputHelper output) { _out = output; }

        private static readonly float3[] Hectare5 = TestKit.Rect(250, 200);

        [Fact]
        public void DefaultParkHasPathsPlantingFurnitureAndRegions()
        {
            var plan = TestKit.Plan("park", TestKit.Entered("park", TestKit.Input(Hectare5, site: TestKit.RoadAlongBottom(250))));
            Assert.False(plan.HasErrors);
            Assert.NotEmpty(plan.Nets);
            Assert.All(plan.Nets, n => Assert.True(n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide));
            Assert.True(TestKit.Count(plan, ObjectRole.Tree) > 60, "trees: " + TestKit.Count(plan, ObjectRole.Tree));
            Assert.True(TestKit.Count(plan, ObjectRole.Bush) > 60);
            Assert.True(TestKit.Count(plan, ObjectRole.Bench) > 3);
            Assert.True(TestKit.Count(plan, ObjectRole.Lamp) > 3);
            Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Core) && r.Tags.Contains(RegionTags.Green));
            Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Edge));
            // s33: the default lawn is painted terrain (no grass surface); "Grass surface" lays exactly one.
            Assert.Empty(plan.Surfaces.Where(x => x.Role != SurfaceRole.Water));   // (Biome mix) the marsh pools are water
            var surf = TestKit.Plan("park", TestKit.Entered("park", TestKit.Input(Hectare5, new SettingsBag { Values = { [Placemaker.Core.Decals.DressingControls.LawnId] = "surface" } }, site: TestKit.RoadAlongBottom(250))));
            Assert.Single(surf.Surfaces.Where(x => x.Role != SurfaceRole.Water));

            Assert.Equal(50000, TestKit.Stat(plan, Keys.StatArea), 0);
            Assert.True(TestKit.Stat(plan, Keys.StatPathLength) > 100);
            // The road runs along the bottom edge: at least one path end connects to it.
            Assert.Contains(plan.Nets, n => n.StartAttach == 77 || n.EndAttach == 77);
        }

        [Theory]
        [InlineData(1u)]
        [InlineData(42u)]
        [InlineData(987654321u)]
        public void SameSeedSamePlan(uint seed)
        {
            var a = TestKit.Plan("park", TestKit.Input(TestKit.LShape(180), seed: seed));
            var b = TestKit.Plan("park", TestKit.Input(TestKit.LShape(180), seed: seed));
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
        }

        [Fact]
        public void DifferentSeedsDiffer()
        {
            var a = TestKit.Plan("park", TestKit.Input(Hectare5, seed: 1));
            var b = TestKit.Plan("park", TestKit.Input(Hectare5, seed: 2));
            Assert.NotEqual(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
        }

        public static IEnumerable<object[]> Shapes()
        {
            yield return new object[] { "rect", TestKit.Rect(250, 200) };
            yield return new object[] { "L", TestKit.LShape(200) };
            yield return new object[] { "blob", TestKit.Blob(110, 90, 7) };
            yield return new object[] { "small", TestKit.Rect(30, 25) };
            yield return new object[] { "sliver", TestKit.Rect(200, 14) };
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void InvariantsHoldOnAllShapes(string name, float3[] ring)
        {
            foreach (var style in new[] { "natural", "formal", "woodland", "meadow", "pocket" })
            {
                var s = new SettingsBag();
                s.Values["style"] = style;
                s.Values["fence"] = true;
                var plan = TestKit.Plan("park", TestKit.Input(ring, s, seed: 3));
                Assert.False(plan.HasErrors, name + "/" + style);
                TestKit.AssertObjectsInside(plan, ring);
                TestKit.AssertObjectsClearOfNets(plan);
                TestKit.AssertSurfacesAccepted(plan);
                foreach (var r in plan.Regions)
                    foreach (var p in r.Ring)
                        Assert.True(Poly.Contains(Poly.To2D(ring), p.xz) || Poly.DistanceToBoundary(Poly.To2D(ring), p.xz) < 0.5f,
                            $"{name}/{style}: region {r.Key} leaves the park");
            }
        }

        [Fact]
        public void RespectsReservedSpace()
        {
            var input = TestKit.Input(Hectare5);
            input.Reserved.AddDisc(new float2(125, 100), 20f, "Centerpiece");
            input.Reserved.AddCorridor(new float2(0, 150), new float2(250, 150), 4f, "Street");
            var plan = TestKit.Plan("park", input);
            TestKit.AssertClearOfReserved(plan, input.Reserved);
        }

        [Fact]
        public void FiveHectaresPlansWithinBudget()
        {
            var input = TestKit.Input(TestKit.Blob(130, 120, 3), site: TestKit.RoadAlongBottom(250));
            TestKit.Plan("park", input);   // JIT warm-up
            var sw = Stopwatch.StartNew();
            const int runs = 5;
            for (int i = 0; i < runs; i++)
            {
                input.Seed = (uint)(i + 11);
                TestKit.Plan("park", input);
            }
            double ms = sw.Elapsed.TotalMilliseconds / runs;
            _out.WriteLine($"park 5 ha: {ms:0.0} ms");
            Assert.True(ms < TimingBudget.Ms(250), $"park planning took {ms:0} ms");
        }

        [Fact]
        public void CancellationIsHonoured()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<System.OperationCanceledException>(() =>
                TestKit.Gen("park").Plan(TestKit.Input(Hectare5), cts.Token));
        }

        [Fact]
        public void SuggestedEntrancesFaceTheRoad()
        {
            var gen = TestKit.Gen("park");
            var markers = gen.SuggestMarkers(TestKit.Input(Hectare5, site: TestKit.RoadAlongBottom(250)));
            Assert.NotEmpty(markers);
            Assert.All(markers, m => Assert.Contains(m.Kind, EntranceKinds.Park));
            Assert.All(markers, m => Assert.True(m.Position.z < 1f, "entrance not on the road side: " + m.Position));
            // Playtest 4b: suggestions are hints; Build needs a placed entrance.
            Assert.False(gen.CheckReady(TestKit.Input(Hectare5)).Ready);
        }

        [Fact]
        public void PlayerEntrancesWin()
        {
            var markers = new List<EdgeMarker> { new EdgeMarker { Kind = "entrance", Position = new float3(250, 0, 100) } };
            var plan = TestKit.Plan("park", TestKit.Input(Hectare5, markers: markers));
            // The walk starts at the entrance (pulled in by its half width when no road takes it).
            Assert.Contains(plan.Nets, n => math.distance(n.A.xz, new float2(250, 100)) < 2f || math.distance(n.D.xz, new float2(250, 100)) < 2f);
            Assert.Equal(1, plan.Facts["entrances"]);
        }

        [Fact]
        public void LakeIsOptInPermanentAndWarned()
        {
            var off = new SettingsBag(); off.Values["moods"] = false; BiomeMixTests.LawnOnly(off);   // (parks makeover) the places shape the ground by default
            var plain = TestKit.Plan("park", TestKit.Input(Hectare5, off));
            Assert.Empty(plain.Terraform);
            var s = new SettingsBag(); s.Values["lake"] = true; s.Values["moods"] = false; BiomeMixTests.LawnOnly(s);
            var plan = TestKit.Plan("park", TestKit.Input(Hectare5, s));
            Assert.Single(plan.Terraform);
            Assert.True(plan.Terraform[0].Permanent);
            Assert.False(float.IsNaN(plan.Terraform[0].WaterLevel));
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Park.Issue.LakePermanent" && i.Severity == IssueSeverity.Warning);
            var lake = Poly.To2D(plan.Terraform[0].Ring);
            Assert.DoesNotContain(plan.Objects, o => Poly.Contains(lake, o.Position.xz));
        }

        [Fact]
        public void LakeIsNeverRandomized()
        {
            var gen = TestKit.Gen("park");
            for (uint seed = 1; seed < 60; seed++)
                Assert.False(gen.Randomize(gen.Defaults(), seed, new HashSet<string>()).B("lake", false));
        }

        [Fact]
        public void DensityControlsScaleCounts()
        {
            var lo = new SettingsBag(); lo.Values["treeDensity"] = 10.0; lo.Values["moods"] = false; BiomeMixTests.LawnOnly(lo);   // the park's own planting
            var hi = new SettingsBag(); hi.Values["treeDensity"] = 60.0; hi.Values["moods"] = false; BiomeMixTests.LawnOnly(hi);
            int a = TestKit.Count(TestKit.Plan("park", TestKit.Input(Hectare5, lo)), ObjectRole.Tree);
            int b = TestKit.Count(TestKit.Plan("park", TestKit.Input(Hectare5, hi)), ObjectRole.Tree);
            Assert.True(b > a * 3, $"{a} → {b}");
            var none = new SettingsBag(); none.Values["furniture"] = 0.0;
            Assert.Equal(0, TestKit.Count(TestKit.Plan("park", TestKit.Input(Hectare5, none)), ObjectRole.Bench));
        }

        [Fact]
        public void NoPathsModeStillPlants()
        {
            var s = new SettingsBag(); s.Values["paths"] = "none";
            var plan = TestKit.Plan("park", TestKit.Input(Hectare5, s));
            Assert.Empty(plan.Nets);
            Assert.True(TestKit.Count(plan, ObjectRole.Tree) > 20);
            Assert.Contains(plan.Regions, r => r.Key == "green:0");
        }

        [Fact]
        public void ChosenSpeciesAreUsed()
        {
            // (P2-8) the park's species are its lawn's: each natural area plants its own, so the check is on a lawn-only park
            var s = BiomeMixTests.LawnOnly(new SettingsBag()); s.Values["treeSpecies"] = new[] { "OakTree01", "BirchTree01" };
            var plan = TestKit.Plan("park", TestKit.Input(Hectare5, s));
            Assert.All(plan.Objects.Where(o => o.Role == ObjectRole.Tree), o => Assert.Contains(o.Asset.Id, new[] { "OakTree01", "BirchTree01" }));
            Assert.Contains(plan.Objects, o => o.Asset.Id == "OakTree01");
            Assert.Contains(plan.Objects, o => o.Asset.Id == "BirchTree01");
        }

        [Fact]
        public void MissingCategoryDegradesGracefully()
        {
            using (AssetAvailability.Scope(new Dictionary<string, int> { [AssetCategories.Tree] = 0 }))
            {
                var plan = TestKit.Plan("park", TestKit.Input(Hectare5));
                Assert.Equal(0, TestKit.Count(plan, ObjectRole.Tree));
                Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                Assert.False(plan.HasErrors);
            }
        }

        [Fact]
        public void InvalidGeometryReportsInsteadOfThrowing()
        {
            var bowtie = new[] { new float3(0, 0, 0), new float3(10, 0, 10), new float3(10, 0, 0), new float3(0, 0, 10) };
            Assert.True(TestKit.Plan("park", TestKit.Input(bowtie)).HasErrors);
            Assert.True(TestKit.Plan("park", TestKit.Input(new[] { new float3(0, 0, 0), new float3(1, 0, 0) })).HasErrors);
            Assert.True(TestKit.Plan("park", TestKit.Input(TestKit.Rect(5, 5))).HasErrors);   // below MinArea
        }
    }
}
