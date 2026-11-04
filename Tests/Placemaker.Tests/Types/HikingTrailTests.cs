// Type wave, builder 12: hiking trail (contour-following routes, switchbacks, trailheads, stops, footbridges).

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class HikingTrailTests
    {
        private const string Id = "hikingTrail";
        private readonly ITestOutputHelper _out;
        public HikingTrailTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> PresetIds() => GeneratorRegistry.Get(Id).Presets.Select(p => new object[] { p.Id });

        private static SettingsBag PresetValues(string preset) => GeneratorRegistry.Get(Id).Presets.Single(p => p.Id == preset).Values;

        private static SettingsBag With(SettingsBag b, params (string k, object v)[] values)
        {
            var c = b.Clone();
            foreach (var (k, v) in values) c.Values[k] = v;
            return c;
        }

        private static IEnumerable<PlannedNet> Trail(AreaPlan p) => p.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);

        private static float Length(AreaPlan p) => Trail(p).Sum(n => math.distance(n.A.xz, n.D.xz));

        [Fact]
        public void IsANatureTypeWithEnoughPresetsAndControls()
        {
            var g = GeneratorRegistry.Get(Id);
            Assert.NotNull(g);
            Assert.Equal("nature", GeneratorGroups.Of(g));
            Assert.InRange(g.Presets.Count, 4, 6);
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id)), 15, 40);
            Assert.InRange(g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id)).Select(c => c.Group).Distinct().Count(), 4, 8);
            Assert.Contains(g.Controls, c => c.Id == "landscape" && c.Kind == ControlKind.Choice);
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetPlansOneHectareAndSixHectareL(string preset)
        {
            foreach (var (ring, site) in new[] { (B12Kit.OneHa, B12Kit.Hilly(100)), (B12Kit.SixHaL, B12Kit.Hilly(293)), (B12Kit.SixHaL, B12Kit.Road(293)) })
            {
                var plan = B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), ring, site);
                Assert.True(string.IsNullOrEmpty(B12Kit.Errors(plan)), preset + ": " + B12Kit.Errors(plan));
                var layer = plan.Layers.Single(l => l.GeneratorId == Id);
                Assert.True(Trail(layer).Count() >= 3, preset + " has no real trail");
                Assert.True(layer.Objects.Count > 10, preset + " placed only " + layer.Objects.Count + " objects");
                B12Kit.AssertInside(layer, ring);
                B12Kit.AssertNothingOnPaths(layer);
                TestKit.AssertSurfacesAccepted(layer);
                Assert.Contains(layer.Regions, r => r.Tags.Contains(RegionTags.Core));
                B12Kit.AssertTerraformBlended(layer, ring, site);
                // The trail is one connected network: every net end is shared with another net or is the gate.
                var ends = Trail(layer).SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
                int loose = ends.Count(e => ends.Count(o => math.distancesq(o, e) < 1e-4f) < 2);
                Assert.True(loose <= 3, $"{preset}: {loose} loose trail ends");
            }
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetIsDeterministic(string preset)
        {
            var a = B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), B12Kit.SixHaL, B12Kit.Hilly(293));
            var b = B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), B12Kit.SixHaL, B12Kit.Hilly(293));
            Assert.Equal(TestKit.Fingerprint(a.Layers[0]), TestKit.Fingerprint(b.Layers[0]));
        }

        [Theory]
        [MemberData(nameof(PresetIds))]
        public void PresetRespectsReservedSpace(string preset)
        {
            var reserved = B12Kit.SomeReserved(new float2(125, 100));
            var input = TestKit.Input(B12Kit.FiveHa, PresetValues(preset), 7, null, B12Kit.Hilly(250));
            input.Reserved = reserved;
            var plan = GeneratorRegistry.Get(Id).Plan(input, CancellationToken.None);
            Assert.False(plan.HasErrors);
            B12Kit.AssertClearOfReserved(plan, reserved);
            foreach (var n in Trail(plan))
                foreach (var c in reserved.Corridors)
                    for (int i = 0; i <= 8; i++)
                    {
                        var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                        Assert.True(math.sqrt(SegmentIndex.DistSq(p, c.A, c.B)) > c.HalfWidth, "trail runs on a reserved corridor");
                    }
        }

        [Fact]
        public void IsFastOnFiveHectares()
        {
            foreach (var p in GeneratorRegistry.Get(Id).Presets)
            {
                B12Kit.Compose(B12Kit.With(Id, p.Values), B12Kit.FiveHa, B12Kit.Hilly(250));   // JIT
                var sw = Stopwatch.StartNew();
                B12Kit.Compose(B12Kit.With(Id, p.Values), B12Kit.FiveHa, B12Kit.Hilly(250));
                sw.Stop();
                _out.WriteLine($"{p.Id}: {sw.ElapsedMilliseconds} ms");
                Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{p.Id} took {sw.ElapsedMilliseconds} ms for 5 ha");
            }
        }

        [Fact]
        public void SteepSlopeGetsSwitchbacksWithinTheGrade()
        {
            // 25 % slope rising away from the road; the summit route must climb it.
            var site = TestKit.RoadAlongBottom(200);
            site.HeightAt = p => 20f + 0.25f * p.y;
            var ring = TestKit.Rect(200, 200);
            var settings = With(PresetValues("summitClimb"), ("maxGrade", 8.0), ("shapeTerrain", "none"));
            var plan = B12Kit.Compose(B12Kit.With(Id, settings), ring, site).Layers[0];
            var nets = Trail(plan).ToList();
            float total = 0f, steep = 0f;
            foreach (var n in nets)
            {
                float len = math.distance(n.A.xz, n.D.xz);
                float grade = math.abs(site.HeightAt(n.D.xz) - site.HeightAt(n.A.xz)) / math.max(0.1f, len);
                total += len;
                if (grade > 0.08f * 1.6f) steep += len;
            }
            _out.WriteLine($"trail {total:0} m, steep {steep:0} m, nets {nets.Count}");
            Assert.True(steep / total < 0.2f, $"{steep:0} of {total:0} m are steeper than the max grade");
            // Gaining ~40 m at ≤ 8 % needs ≥ 500 m of trail on a 200 m slope: it must zig-zag.
            Assert.True(total > 2.5f * 200f, "no switchbacks: trail only " + total + " m");
            // Heading changes: many sharp turns (switchbacks).
            int turns = 0;
            for (int i = 1; i < nets.Count; i++)
            {
                var d0 = math.normalizesafe(nets[i - 1].D.xz - nets[i - 1].A.xz);
                var d1 = math.normalizesafe(nets[i].D.xz - nets[i].A.xz);
                if (math.dot(d0, d1) < 0f) turns++;
            }
            Assert.True(turns >= 2, "expected switchback turns, got " + turns);
        }

        [Fact]
        public void RidgeTrailKeepsToTheCrestAndCanyonToTheFloor()
        {
            var site = B12Kit.Hilly(250, 0f, 25f);   // ridge along z = 120
            float MeanRank(string preset)
            {
                var plan = B12Kit.Compose(B12Kit.With(Id, With(PresetValues(preset), ("shapeTerrain", "none"))), B12Kit.FiveHa, site).Layers[0];
                var pts = Trail(plan).Select(n => Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).xz).ToList();
                return pts.Average(p => (site.HeightAt(p) - 50f) / 25f);
            }
            float ridge = MeanRank("ridgeHike"), canyon = MeanRank("canyonTrail");
            _out.WriteLine($"ridge {ridge:0.00} canyon {canyon:0.00}");
            Assert.True(ridge > canyon + 0.25f, $"ridge trail ({ridge:0.00}) should run higher than the canyon trail ({canyon:0.00})");
        }

        [Fact]
        public void CrossesWaterOnFootbridges()
        {
            var site = TestKit.RoadAlongBottom(250);
            site.WaterDepthAt = p => p.x > 60f && p.x < 72f ? 1.5f : 0f;
            var plan = B12Kit.Compose(B12Kit.With(Id, PresetValues("forestTrail")), B12Kit.FiveHa, site).Layers[0];
            var bridges = Trail(plan).Where(n => !n.FollowTerrain).ToList();
            Assert.NotEmpty(bridges);
            Assert.True(TestKit.Stat(plan, "Placemaker.Stat.Footbridges") >= 1);
            foreach (var n in Trail(plan).Where(n => n.FollowTerrain))
                for (int i = 1; i < 8; i++)
                    Assert.True(site.WaterDepthAt(Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz) <= 0.2f, "a trail section (not a bridge) runs through water");
            // Nothing is planted in the water.
            Assert.DoesNotContain(plan.Objects, o => o.Role == ObjectRole.Tree && site.WaterDepthAt(o.Position.xz) > 0f);

            // Without bridges the trail stays dry.
            var dry = B12Kit.Compose(B12Kit.With(Id, With(PresetValues("forestTrail"), ("bridges", false))), B12Kit.FiveHa, site).Layers[0];
            Assert.DoesNotContain(Trail(dry), n => !n.FollowTerrain);
        }

        [Fact]
        public void TrailheadHasParkingMapAndBench()
        {
            var road = B12Kit.Road(250);
            // (rf-nature) The car park is off by default (no driveway reaches it); asked for, it is still laid out.
            var plain = B12Kit.Compose(TestKit.Entered(B12Kit.With(Id, PresetValues("forestTrail")), B12Kit.FiveHa, road), B12Kit.FiveHa, road).Layers[0];
            Assert.False(plain.Facts.ContainsKey("parkingStalls"), "a car park by default");
            var plan = B12Kit.Compose(TestKit.Entered(B12Kit.With(Id, With(PresetValues("forestTrail"), ("parking", 8.0))), B12Kit.FiveHa, road), B12Kit.FiveHa, road).Layers[0];
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.TrailSign);
            Assert.True(plan.Facts.TryGetValue("parkingStalls", out var stalls) && stalls >= 8, "no trailhead car park");
            Assert.Contains(plan.Surfaces, s => s.Category == AssetCategories.SurfaceNatural);
            // A gravel trailhead has no painted bays (road markings never go into leisure places; art pass a3).
            Assert.DoesNotContain(plan.Objects, o => o.Category == AssetCategories.DecalMarkings);
            Assert.True(TestKit.Stat(plan, "Placemaker.Stat.RestSpots") >= 1);
            Assert.True(TestKit.Stat(plan, "Placemaker.Stat.Viewpoints") >= 1);
            // The path starts at the entrance and meets the road (stops at its kerb: the game joins it, StreetJoins).
            Assert.Contains(Trail(plan), n => TestKit.MeetsStreet(n, road));
        }

        [Fact]
        public void LandscapesLookDifferent()
        {
            int Trees(string preset) => B12Kit.Compose(B12Kit.With(Id, PresetValues(preset)), B12Kit.FiveHa, B12Kit.Hilly(250)).Layers[0].Objects.Count(o => o.Role == ObjectRole.Tree);
            Assert.True(Trees("forestTrail") > 5 * Trees("meadowWalk"));
            var canyon = B12Kit.Compose(B12Kit.With(Id, PresetValues("canyonTrail")), B12Kit.FiveHa, B12Kit.Hilly(250)).Layers[0];
            Assert.Contains(canyon.Terraform, t => t.Kind == TerraformKind.Lower);
            Assert.Contains(canyon.Objects, o => o.Category == AssetCategories.Rock);
            var ridge = B12Kit.Compose(B12Kit.With(Id, PresetValues("ridgeHike")), B12Kit.FiveHa, B12Kit.Hilly(250)).Layers[0];
            Assert.Contains(ridge.Terraform, t => t.Kind == TerraformKind.Raise);
            var summit = B12Kit.Compose(B12Kit.With(Id, PresetValues("summitClimb")), B12Kit.FiveHa, B12Kit.Hilly(250)).Layers[0];
            // No vanilla lookout tower: the summit gets an open timber shelter on its viewpoint.
            Assert.Contains(summit.Objects, o => o.Category == AssetCategories.Shelter && (o.Asset.Id ?? "").StartsWith("CampgroundShelter"));
            Assert.DoesNotContain(summit.Objects, o => o.Category == AssetCategories.Lookout);
            var meadow = B12Kit.Compose(B12Kit.With(Id, PresetValues("meadowWalk")), B12Kit.FiveHa, B12Kit.Hilly(250)).Layers[0];
            Assert.NotEmpty(meadow.Paint);
            Assert.DoesNotContain(meadow.Nets, n => n.Role == NetRole.PathwayWide);   // trails are narrow (art pass a3)
        }

        [Theory]
        [InlineData("ridgeHike")]
        [InlineData("canyonTrail")]
        [InlineData("summitClimb")]
        public void ShapedGroundIsSmoothOnHillsAndTheTrailStandsOnIt(string preset)
        {
            // (rf-nature) The game runner's hills (4 m egg-crate bumps, 30 m apart): the old ridge and gorge were flat 12 m
            // squares each levelled to its own centre (a staircase, with pits up to 80 m deep left beside the trail).
            var site = TestKit.RoadAlongBottom(250);
            site.HeightAt = p => 50f + 4f * math.sin(p.x * 0.21f) * math.sin(p.y * 0.21f);
            var plan = B12Kit.Compose(TestKit.Entered(B12Kit.With(Id, PresetValues(preset)), B12Kit.FiveHa, site), B12Kit.FiveHa, site).Layers[0];
            Assert.NotEmpty(plan.Terraform);
            Assert.All(plan.Terraform, t => Assert.True(t.Sloped, "a levelled terraform square"));
            Assert.True(plan.Terraform.Count <= 2200, $"{plan.Terraform.Count} terraform patches (the build rasters each; too many run out its time)");
            // The trail (terrain-following pieces) lies on the shaped ground: every end within 1 m of the planned surface.
            float after(float2 q) { foreach (var t in plan.Terraform) if (Placemaker.Core.Geometry.Poly.Contains(Placemaker.Core.Geometry.Poly.To2D(t.Ring), q)) return TerraformSurface.TargetAt(t, q); return site.HeightAt(q); }
            foreach (var n in Trail(plan).Where(n => n.FollowTerrain))
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(math.abs(p.y - after(p.xz)) < 1f, $"{preset}: trail end {p.y:0.0} m, ground {after(p.xz):0.0} m");
        }

        [Fact]
        public void RouteShapesDiffer()
        {
            var site = B12Kit.Hilly(250);
            var loop = B12Kit.Compose(B12Kit.With(Id, With(PresetValues("forestTrail"), ("route", "loop"))), B12Kit.FiveHa, site).Layers[0];
            var nets = Trail(loop).ToList();
            // A loop returns to where it started.
            Assert.Contains(nets.Skip(1), n => math.distance(n.D.xz, nets[0].A.xz) < 0.01f);
            var through = B12Kit.Compose(B12Kit.With(Id, With(PresetValues("forestTrail"), ("route", "through"))), B12Kit.FiveHa, site).Layers[0];
            var tn = Trail(through).ToList();
            Assert.True(math.distance(tn.First().A.xz, tn.Last().D.xz) > 50f, "through trail should end far from its start");
        }

        [Fact]
        public void EveryFeatureComposes()
        {
            var features = Features.Of(GeneratorRegistry.Get(Id)).Select(f => f.Layer).ToArray();
            var plan = B12Kit.Compose(B12Kit.With(Id, null, features), B12Kit.FiveHa, B12Kit.Hilly(250));
            Assert.True(string.IsNullOrEmpty(B12Kit.Errors(plan)), B12Kit.Errors(plan));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
            foreach (var l in plan.Layers) B12Kit.AssertInside(l, B12Kit.FiveHa, 0.5f);
            var camp = plan.Layers.Single(l => l.GeneratorId == "trailCamp");
            Assert.Contains(camp.Objects, o => o.Category == AssetCategories.CampingPitch || o.Category == AssetCategories.Camping);
            foreach (var l in plan.Layers) B12Kit.AssertNothingOnPaths(new AreaPlan { GeneratorId = l.GeneratorId, Objects = l.Objects, Nets = plan.Layers[0].Nets });
        }

        [Fact]
        public void MissingSignsAndRocksDegradeToInfo()
        {
            var none = new Dictionary<string, int> { [AssetCategories.TrailSign] = 0, [AssetCategories.Rock] = 0, [AssetCategories.Lookout] = 0, [AssetCategories.Shelter] = 0 };
            using (AssetAvailability.Scope(none))
            {
                var plan = B12Kit.Compose(B12Kit.With(Id, PresetValues("summitClimb")), B12Kit.FiveHa, B12Kit.Hilly(250));
                Assert.True(string.IsNullOrEmpty(B12Kit.Errors(plan)), B12Kit.Errors(plan));
                Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                Assert.DoesNotContain(plan.Layers[0].Objects, o => none.ContainsKey(o.Category ?? ""));
                Assert.NotEmpty(Trail(plan.Layers[0]));
            }
        }

        [Fact]
        public void CategoryRulesClassifyTrailSigns()
        {
            const AssetTraits prop = AssetTraits.StaticObject | AssetTraits.HasMesh;
            Assert.Contains(AssetCategories.TrailSign, AssetHeuristics.Classify("WoodenSignPost01", prop));
            Assert.Contains(AssetCategories.TrailSign, AssetHeuristics.Classify("ParkMapBoard", prop));
            Assert.DoesNotContain(AssetCategories.TrailSign, AssetHeuristics.Classify("RoadSignSpeed50", prop));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.TrailSign, "WoodenSignPost01", prop));
        }

        [Fact]
        public void EveryKeyHasEnglishTextAndTheRecipeComposes()
        {
            B12Kit.AssertLang(GeneratorRegistry.Get(Id), "HikingTrail");
            B12Kit.AssertRecipe(Id);
        }
    }
}
