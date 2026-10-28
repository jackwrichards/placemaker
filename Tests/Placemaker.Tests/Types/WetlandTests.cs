// Wetland (type wave 6a): presets on 1 ha and a 6 ha L-shape, determinism, reserved space, speed,
// features, keys, and the wetland's own promises — permanent shallow water with a warning, dry
// causeways under the boardwalks, islands, reed beds at the shore, hides, distinct subtypes.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Wetland;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using static Placemaker.Tests.Types.TypeWaveKit06;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class WetlandTests
    {
        private const string Id = "wetland";
        private readonly ITestOutputHelper _out;
        public WetlandTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> Presets() => WetlandLayer.Subtypes.Select(s => new object[] { s });

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetPlansOneHectareCleanly(string preset)
        {
            var plan = AssertPresetClean(Id, preset, OneHa, TestKit.RoadAlongBottom(100));
            Assert.True(plan.Facts["ponds"] >= 1, preset + ": no pond on 1 ha");
        }

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetPlansSixHectareLShapeCleanly(string preset)
        {
            var plan = AssertPresetClean(Id, preset, SixHaL, TestKit.RoadAlongBottom(293));
            Assert.True(plan.Facts["ponds"] >= 1);
            _out.WriteLine($"{preset}: water {plan.Facts["waterArea"]:0} m², ponds {plan.Facts["ponds"]}, islands {plan.Facts["islands"]}, " +
                           $"boardwalk {plan.Facts["boardwalkLength"]:0} m, hides {plan.Facts["hides"]}, objects {plan.Objects.Count}");
        }

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetIsDeterministic(string preset) => AssertDeterministic(Id, preset, SixHaL, TestKit.RoadAlongBottom(293));

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetKeepsOutOfReservedSpace(string preset) => AssertRespectsReserved(Id, preset);

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetIsFastOnFiveHectares(string preset)
        {
            long ms = TimeFiveHa(Id, preset);
            _out.WriteLine($"{preset}: {ms} ms");
            Assert.True(ms < TimingBudget.Ms(250), $"{preset} took {ms} ms for 5 ha");
        }

        [Fact]
        public void FeaturesCompose() => AssertFeaturesCompose(Id);

        [Fact]
        public void EveryKeyHasEnglishText() => AssertKeys(Id, "Wetland", "wetland.json",
            AssetCategories.WetlandReed, AssetCategories.WetlandTree, AssetCategories.WetlandHide, AssetCategories.WetlandNestBox);

        [Fact]
        public void SchemaIsDeepButTidy() => AssertSchemaShape(Id, "subtype", "nature");

        [Theory]
        [InlineData("wetland")]
        [InlineData("zenGarden")]
        public void PondsOnHillsStayOnLevelGround(string id)
        {
            // hills 6 m high every 80 m: a pond across a slope took the level of its lowest bank and dug its uphill side into
            // a pit with a wall; its islands and crossings kept the hill (cones, walled ledges; game runner 27 Oct)
            var site = TestKit.RoadAlongBottom(250);
            site.HeightAt = p => 500f + 3f * math.sin(p.x / 80f * 6.283f) + 3f * math.cos(p.y / 70f * 6.283f);
            string preset = id == "wetland" ? "birdReserve" : "stroll";
            var plan = PlanPreset(id, preset, FiveHa, site);
            float worst = 0f;
            foreach (var t in plan.Terraform.Where(t => !float.IsNaN(t.WaterLevel)))
                foreach (var q in Poly.Resample(Poly.To2D(t.Ring), 2f))
                    worst = math.max(worst, site.HeightAt(q) - (t.WaterLevel + 0.3f));
            _out.WriteLine($"{id}: ponds {(plan.Facts.TryGetValue("ponds", out var n) ? n : 0)}, the bank rises up to {worst:F1} m above the lowest");
            Assert.True(worst <= 4f * WaterBodies.MaxRise + 0.4f, $"{id}: a pond's bank rises {worst:F1} m above its lowest point");
        }

        [Fact]
        public void WaterIsShallowPermanentAndWarned()
        {
            var plan = PlanPreset(Id, "wetlandPark", FiveHa, TestKit.RoadAlongBottom(250));
            var basins = plan.Terraform.Where(t => !float.IsNaN(t.WaterLevel)).ToList();
            Assert.NotEmpty(basins);
            Assert.All(plan.Terraform, t => Assert.True(t.Permanent));
            Assert.Contains(plan.Issues, i => i.Key == WetlandLayer.PermanentKey && i.Severity == IssueSeverity.Warning);
            // Water stays below the banks and the basins are shallow.
            foreach (var b in basins)
            {
                Assert.True(b.WaterLevel < 0f);
                Assert.True(b.WaterLevel - b.TargetHeight <= 3.01f);
                Assert.True(Poly.Contains(Poly.To2D(b.Ring), b.WaterAnchor.xz) || Poly.DistanceToBoundary(Poly.To2D(b.Ring), b.WaterAnchor.xz) < 0.5f);
            }
            // Water is handed to the composer as a claimed place.
            Assert.Contains(plan.Regions, r => r.Tags.Contains(WetlandLayer.WaterPlace));
            Assert.InRange(plan.Facts["waterArea"], 50000 * 0.12, 50000 * 0.5);
        }

        [Fact]
        public void BoardwalksBridgeTheWaterAndJoinTheTrails()
        {
            // Art pass a3: boardwalks are decks over open water (FollowTerrain=false, clear of the water level),
            // not causeways cutting the pond, and every bank end meets another path at the same height.
            var plan = PlanPreset(Id, "reedMarsh", SixHaL, TestKit.RoadAlongBottom(293));
            Assert.True(plan.Facts["boardwalkLength"] > 20, "no boardwalk");
            var basins = plan.Terraform.Where(t => !float.IsNaN(t.WaterLevel)).ToList();
            var ponds = plan.Regions.Where(r => r.Tags.Contains(WetlandLayer.WaterPlace)).Select(r => Poly.To2D(r.Ring)).ToList();
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway).ToList();
            int overWater = 0;
            foreach (var n in paths)
            {
                var mid = Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).xz;
                if (!ponds.Any(p => Poly.Contains(p, mid))) continue;
                overWater++;
                Assert.False(n.FollowTerrain, "a boardwalk over water must not follow the pond bed");
                float level = basins.Where(b => Poly.Contains(Poly.To2D(b.Ring), mid)).Select(b => b.WaterLevel).DefaultIfEmpty(float.NaN).Max();
                if (!float.IsNaN(level)) Assert.True(Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).y >= level + 0.3f, $"deck {n.A.y:0.00} too close to the water {level:0.00}");
            }
            Assert.True(overWater > 0, "no path segment lies over the water");
            // Elevated pieces are connected: each end is shared with another path at the same height (spur tips excepted).
            int loose = 0, ends = 0;
            foreach (var n in paths.Where(n => !n.FollowTerrain))
                foreach (var e in new[] { n.A, n.D })
                {
                    ends++;
                    if (!paths.Any(o => o != n && (math.distance(o.A, e) < 0.05f || math.distance(o.D, e) < 0.05f))) loose++;
                }
            Assert.True(loose * 4 <= ends, $"{loose} of {ends} boardwalk ends are not joined");
        }

        [Fact]
        public void BirdReserveHasIslandsHidesAndScreen()
        {
            var plan = PlanPreset(Id, "birdReserve", SixHaL, TestKit.RoadAlongBottom(293));
            Assert.True(plan.Facts["islands"] >= 2, "islands " + plan.Facts["islands"]);
            Assert.True(plan.Facts["hides"] >= 2, "hides " + plan.Facts["hides"]);
            // No vanilla lookout tower: the automatic lookout is an open gazebo by the water.
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Shelter && (o.Asset.Id ?? "").StartsWith("Gazebo"));
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Fence);
            // Islands stay wild: no path ends on them, and they are not dug out.
            var park = PlanPreset(Id, "wetlandPark", SixHaL, TestKit.RoadAlongBottom(293));
            Assert.True(plan.Objects.Count(o => o.Role == ObjectRole.Tree) > park.Objects.Count(o => o.Role == ObjectRole.Tree));
        }

        [Fact]
        public void ReedsGatherAtTheShore()
        {
            var plan = PlanPreset(Id, "reedMarsh", FiveHa, TestKit.RoadAlongBottom(250));
            var reeds = plan.Objects.Where(o => o.Category == AssetCategories.WetlandReed).ToList();
            Assert.True(reeds.Count > 100, "reeds " + reeds.Count);
            var ponds = plan.Regions.Where(r => r.Tags.Contains(WetlandLayer.WaterPlace)).Select(r => Poly.To2D(r.Ring)).ToList();
            float Shore(float2 p) => ponds.Min(r => Poly.DistanceToBoundary(r, p));
            Assert.True(reeds.Count(o => Shore(o.Position.xz) < 8f) > reeds.Count * 0.9);
            // Some stand in the shallow water (the reed shelf).
            Assert.Contains(reeds, o => ponds.Any(r => Poly.Contains(r, o.Position.xz)));
        }

        [Fact]
        public void SubtypesAreVisiblyDifferent()
        {
            var plans = WetlandLayer.Subtypes.ToDictionary(s => s, s => PlanPreset(Id, s, SixHaL, TestKit.RoadAlongBottom(293)));
            Assert.Equal(plans.Count, plans.Values.Select(TestKit.Fingerprint).Distinct().Count());
            Assert.True(plans["reedMarsh"].Facts["waterArea"] > plans["rainGarden"].Facts["waterArea"] * 1.5);
            Assert.True(plans["waterGarden"].Facts.ContainsKey("terrace"));
            Assert.Contains(plans["waterGarden"].Objects, o => o.Role == ObjectRole.Lamp);
            Assert.DoesNotContain(plans["birdReserve"].Objects, o => o.Role == ObjectRole.Lamp);
            // Rain-garden swales are long and thin: their pond outlines are far more elongated than a lake's.
            float Elongation(AreaPlan p) => p.Regions.Where(r => r.Tags.Contains(WetlandLayer.WaterPlace))
                .Select(r => { var x = Poly.To2D(r.Ring); return Poly.Perimeter(x) * Poly.Perimeter(x) / Poly.Area(x); }).Max();
            Assert.True(Elongation(plans["rainGarden"]) > Elongation(plans["birdReserve"]));
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.WetlandReed] = 0, [AssetCategories.Bush] = 0, [AssetCategories.WetlandHide] = 0,
                [AssetCategories.Lookout] = 0, [AssetCategories.Shelter] = 0, [AssetCategories.WetlandTree] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                var plan = PlanPreset(Id, "birdReserve", FiveHa, TestKit.RoadAlongBottom(250));
                Assert.False(plan.HasErrors, Errors(plan.Issues));
                Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                Assert.DoesNotContain(plan.Objects, o => none.ContainsKey(o.Category ?? "") || o.Role == ObjectRole.Bush);
                Assert.NotEmpty(plan.Terraform);
            }
        }

        [Fact]
        public void SlopedGroundKeepsWaterBelowEveryBank()
        {
            var site = TestKit.RoadAlongBottom(250);
            site.HeightAt = p => 0.03f * p.x + 0.01f * p.y;
            var plan = PlanPreset(Id, "wetlandPark", FiveHa, site);
            Assert.False(plan.HasErrors);
            foreach (var r in plan.Regions.Where(r => r.Tags.Contains(WetlandLayer.WaterPlace)))
            {
                var ring = Poly.To2D(r.Ring);
                float lowest = Poly.Resample(ring, 2f).Min(p => site.HeightAt(p));
                foreach (var b in plan.Terraform.Where(t => !float.IsNaN(t.WaterLevel) && Poly.Contains(ring, t.WaterAnchor.xz)))
                    Assert.True(b.WaterLevel < lowest, $"water {b.WaterLevel:0.00} above the lowest bank {lowest:0.00}");
            }
        }

        [Theory]
        [InlineData("Reeds01", AssetCategories.WetlandReed)]
        [InlineData("CattailCluster02", AssetCategories.WetlandReed)]
        [InlineData("BirdHide01", AssetCategories.WetlandHide)]
        [InlineData("HuntingStand01", AssetCategories.WetlandHide)]
        [InlineData("BirdHouse01", AssetCategories.WetlandNestBox)]
        [InlineData("InsectHotel01", AssetCategories.WetlandNestBox)]
        public void Classification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(category, AssetHeuristics.Classify(name, t));
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name + " should be automatic for " + category);
        }

        [Fact]
        public void WetTreesAndReedPlantsKeepTheirGeneralCategory()
        {
            var tree = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.Plant | AssetTraits.Tree;
            var willow = AssetHeuristics.Classify("WillowTree01", tree);
            Assert.Contains(AssetCategories.WetlandTree, willow);
            Assert.Contains(AssetCategories.Tree, willow);
            var plant = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.Plant;
            var reed = AssetHeuristics.Classify("ReedBush01", plant);
            Assert.Contains(AssetCategories.WetlandReed, reed);
            Assert.Contains(AssetCategories.Bush, reed);
            Assert.DoesNotContain(AssetCategories.WetlandReed, AssetHeuristics.Classify("BreedingFarm01", plant));
        }
    }
}
