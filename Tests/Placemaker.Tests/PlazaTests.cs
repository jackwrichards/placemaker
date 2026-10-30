using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class PlazaTests
    {
        /// <summary>(the plazas makeover) These tests are about the classic composition round the entrances; the type's default is now the grand square.</summary>
        private static AreaPlan Classic(AreaInput input) { input.Settings.Values["design"] = "classic"; return TestKit.Plan("plaza", input); }

        private static readonly float3[] Square = TestKit.Rect(80, 60);

        [Fact]
        public void DefaultPlazaIsWalkablePavedAndFurnished()
        {
            var plan = Classic(TestKit.Input(Square, site: TestKit.RoadAlongBottom(80)));
            Assert.False(plan.HasErrors);
            Assert.Single(plan.Areas);
            Assert.Equal(AreaRole.PedestrianArea, plan.Areas[0].Role);
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Plaza);
            Assert.Equal(1, TestKit.Count(plan, ObjectRole.Centerpiece));
            Assert.True(TestKit.Count(plan, ObjectRole.Bench) >= 4);
            Assert.True(TestKit.Count(plan, ObjectRole.AccessPoint) >= 1);
            Assert.Contains(plan.Regions, r => r.Key == "paved");
            Assert.Contains(plan.Regions, r => r.Key == "core" && r.Tags.Contains(RegionTags.Core));
            Assert.Contains(plan.Regions, r => r.Tags.Contains(RegionTags.Edge));
            TestKit.AssertSurfacesAccepted(plan);
            TestKit.AssertObjectsInside(plan, Square);
        }

        [Theory]
        [InlineData("single", 1)]
        [InlineData("mirrored", 2)]
        [InlineData("axis", 3)]
        [InlineData("none", 0)]
        public void CenterModes(string mode, int expected)
        {
            var s = new SettingsBag(); s.Values["centerMode"] = mode; s.Values["centerSize"] = 3.0; s.Values["centerSpacing"] = 15.0;
            var plan = Classic(TestKit.Input(TestKit.Rect(100, 50), s));
            Assert.Equal(expected, TestKit.Count(plan, ObjectRole.Centerpiece));
        }

        [Fact]
        public void OversizedCenterpieceIsDroppedWithInfo()
        {
            var s = new SettingsBag(); s.Values["centerSize"] = 20.0;
            var plan = Classic(TestKit.Input(TestKit.Rect(30, 25), s));
            Assert.Equal(0, TestKit.Count(plan, ObjectRole.Centerpiece));
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Plaza.Issue.CenterpieceDoesNotFit");
            Assert.False(plan.HasErrors);
        }

        [Theory]
        [InlineData("center")]
        [InlineData("edges")]
        public void ArrangementIsPlacedInOrderAndClear(string mode)
        {
            var s = new SettingsBag();
            s.Values["arrangement"] = new[] { "bush", "bench", "lamp:MyLamp", "bench", "bush" };
            s.Values["arrangementMode"] = mode;
            s.Values["furniture"] = 8.0;
            s.Values["treeLayout"] = "none"; s.Values["beds"] = 0.0;   // the arrangement alone (no allée benches, no bed bushes)
            var input = TestKit.Input(Square, s);
            input.Reserved.AddDisc(new float2(10, 10), 4f, "Tree");
            var plan = Classic(input);
            Assert.True(TestKit.Count(plan, ObjectRole.Bench) >= 4, "benches " + TestKit.Count(plan, ObjectRole.Bench));
            Assert.Equal(TestKit.Count(plan, ObjectRole.Bench), TestKit.Count(plan, ObjectRole.Bush));
            Assert.All(plan.Objects.Where(o => o.Role == ObjectRole.Lamp), o => Assert.Equal("MyLamp", o.Asset.Id));
            TestKit.AssertClearOfReserved(plan, input.Reserved);
            TestKit.AssertObjectsInside(plan, Square);
            // Furniture never sits on another piece of furniture.
            var items = plan.Objects.Where(o => o.Role != ObjectRole.AccessPoint && o.Role != ObjectRole.Centerpiece).ToList();
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                    Assert.True(math.distance(items[i].Position.xz, items[j].Position.xz) > 0.9f);
        }

        [Fact]
        public void SameSeedSamePlan()
        {
            var a = Classic(TestKit.Input(TestKit.LShape(90), seed: 5));
            var b = Classic(TestKit.Input(TestKit.LShape(90), seed: 5));
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
        }

        [Fact]
        public void AccessMarkersFromPlayerAreUsed()
        {
            var markers = new List<EdgeMarker>
            {
                new EdgeMarker { Kind = "access", Position = new float3(0, 0, 30) },
                new EdgeMarker { Kind = "access", Position = new float3(80, 0, 30) },
            };
            var plan = Classic(TestKit.Input(Square, markers: markers));
            var access = plan.Objects.Where(o => o.Role == ObjectRole.AccessPoint).ToList();
            Assert.Equal(2, access.Count);
            Assert.Contains(access, o => math.distance(o.Position.xz, new float2(0, 30)) < 0.1f);
        }

        [Fact]
        public void RandomizeNeverOverridesLockedChoices()
        {
            // ParkManager's "Variant" overwrote explicit choices; here locked ids survive every roll.
            var g = TestKit.Gen("plaza");
            var current = g.Defaults();
            current.Values["centerMode"] = "mirrored";
            current.Values["arrangement"] = new[] { "tree", "bench", "tree" };
            current.Values["centerpiece"] = "MyFountain";
            var locked = new HashSet<string> { "centerMode", "arrangement" };
            bool anyChange = false;
            for (uint seed = 1; seed < 40; seed++)
            {
                var r = g.Randomize(current, seed, locked);
                Assert.Equal("mirrored", r.S("centerMode", ""));
                Assert.Equal(new[] { "tree", "bench", "tree" }, r.L("arrangement"));
                Assert.Equal("MyFountain", r.S("centerpiece", ""));   // assets are never rolled
                anyChange |= !r.ContentEquals(current);
                var arrangement = g.Randomize(current, seed, new HashSet<string>()).L("arrangement");
                Assert.InRange(arrangement.Length, 1, 5);
                Assert.Equal(arrangement, arrangement.Reverse().ToArray());   // mirror-symmetric (ParkManager roller)
            }
            Assert.True(anyChange);
        }

        [Fact]
        public void MissingCentrepieceCategoryDegrades()
        {
            using (AssetAvailability.Scope(new Dictionary<string, int> { [AssetCategories.Centerpiece] = 0 }))
            {
                var plan = Classic(TestKit.Input(Square));
                Assert.Equal(0, TestKit.Count(plan, ObjectRole.Centerpiece));
                Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue);
                Assert.True(TestKit.Count(plan, ObjectRole.Bench) > 0);
            }
        }

        [Fact]
        public void CancellationIsHonoured()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<System.OperationCanceledException>(() => TestKit.Gen("plaza").Plan(TestKit.Input(Square), cts.Token));
        }
    }
}
