// Round 4 (LAYERS): new leisure types, the new features (shops, street shops, decals, terrain, rocks),
// plaza depth, park path styles and feature clearings, and the leisure asset classification.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class LeisureTests
    {
        private readonly ITestOutputHelper _out;
        public LeisureTests(ITestOutputHelper output) { _out = output; }

        public static readonly string[] Types =
        {
            "botanicalGarden", "sportsPark", "dogPark", "picnicGrounds", "natureReserve", "formalGarden", "communityGarden", "marketSquare",
            "skatePark", "cemeteryGrounds", "greenwayPark",
        };
        public static IEnumerable<object[]> TypeIds() => Types.Select(t => new object[] { t });

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        private static Recipe WithSettings(string layer, params (string k, object v)[] settings)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            foreach (var (k, v) in settings) r.Root.Settings.Values[k] = v;
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        private static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        /// <summary>5 ha (250 × 200 m) with a road along the bottom.</summary>
        private static readonly float3[] FiveHa = TestKit.Rect(250, 200);

        private static void AssertInside(AreaPlan plan, float3[] ring, float tolerance = 0.05f)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < tolerance,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {n.Role} end {p.xz} outside");
        }

        /// <summary>No placed object (decals and markers excepted) sits on a path of the same plan.</summary>
        private static void AssertNothingOnPaths(AreaPlan plan)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.AnimalSpawner) continue;
                foreach (var n in plan.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        // ───────────── every new type ─────────────

        [Theory]
        [MemberData(nameof(TypeIds))]
        public void TypePlansSomethingInsideClearOfPathsAndAccepted(string id)
        {
            var plan = Compose(With(id), FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            var layer = plan.Layers.Single(l => l.GeneratorId == id);
            Assert.True(layer.Objects.Count > 20, id + " placed only " + layer.Objects.Count + " objects");
            AssertInside(layer, FiveHa);
            AssertNothingOnPaths(layer);
            TestKit.AssertSurfacesAccepted(layer);
            Assert.Contains(layer.Regions, r => r.Tags.Contains(RegionTags.Core));
        }

        [Theory]
        [MemberData(nameof(TypeIds))]
        public void TypeIsDeterministic(string id)
        {
            var a = Compose(With(id), FiveHa, TestKit.RoadAlongBottom(250));
            var b = Compose(With(id), FiveHa, TestKit.RoadAlongBottom(250));
            Assert.Equal(TestKit.Fingerprint(a.Layers[0]), TestKit.Fingerprint(b.Layers[0]));
        }

        [Theory]
        [MemberData(nameof(TypeIds))]
        public void TypeIsFastOnFiveHectares(string id)
        {
            Compose(With(id), FiveHa, TestKit.RoadAlongBottom(250));   // JIT
            var sw = Stopwatch.StartNew();
            Compose(With(id), FiveHa, TestKit.RoadAlongBottom(250));
            sw.Stop();
            _out.WriteLine($"{id}: {sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{id} took {sw.ElapsedMilliseconds} ms for 5 ha");
        }

        [Theory]
        [MemberData(nameof(TypeIds))]
        public void EveryFeatureComposes(string id)
        {
            var gen = GeneratorRegistry.Get(id);
            var features = Features.Of(gen).Select(f => f.Layer).ToArray();
            Assert.NotEmpty(features);
            var plan = Compose(With(id, features), FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var l in plan.Layers) AssertInside(l, FiveHa, 1.2f);   // zoning quads reach 1 m over the road edge; objects never
            // Features run (at least one plan per feature) — or, for a facility the host found no room for
            // (every field of the sports park at once on 5 ha), the host says so.
            foreach (var f in features)
                Assert.True(plan.Layers.Any(l => l.GeneratorId == f)
                    || plan.AllIssues().Any(i => i.Args != null && i.Args.Any(a => a as string == "Placemaker.Feature." + f)), f + " neither ran nor was reported");
        }

        [Theory]
        [MemberData(nameof(TypeIds))]
        public void PresetsPlanOnIrregularOutlines(string id)
        {
            var gen = GeneratorRegistry.Get(id);
            Assert.True(gen.Presets.Count >= 2, id + " needs presets");
            var ring = TestKit.Blob(110, 40, 3);
            foreach (var p in gen.Presets)
            {
                var plan = gen.Plan(TestKit.Input(ring, p.Values, 5, null, TestKit.RoadAlongBottom(200)), CancellationToken.None);
                Assert.False(plan.HasErrors, id + "/" + p.Id);
                AssertInside(plan, ring);
                TestKit.AssertSurfacesAccepted(plan);
            }
        }

        [Fact]
        public void TypesAreVisiblyDifferent()
        {
            // Each type places its signature items.
            var expect = new Dictionary<string, string[]>
            {
                ["botanicalGarden"] = new[] { AssetCategories.Flowerbed, AssetCategories.Glasshouse },
                ["sportsPark"] = new[] { AssetCategories.SportsGoal, AssetCategories.SportsBasketball, AssetCategories.SportsTennis },
                ["dogPark"] = new[] { AssetCategories.DogAgility },
                ["picnicGrounds"] = new[] { AssetCategories.Picnic, AssetCategories.Shelter },
                ["natureReserve"] = new[] { AssetCategories.Shelter },   // automatic lookout = timber shelter (no vanilla tower)
                ["formalGarden"] = new[] { AssetCategories.Hedge, AssetCategories.CenterpieceFountain },
                ["communityGarden"] = new[] { AssetCategories.GardenCrop, AssetCategories.GardenShed },
                ["marketSquare"] = new[] { AssetCategories.MarketStall },
                ["skatePark"] = new[] { AssetCategories.SportsSkate },
                ["cemeteryGrounds"] = new[] { AssetCategories.Grave },
            };
            foreach (var kv in expect)
            {
                // s10: the sports park's facilities are its field features (they plan in their own layers).
                var composed = kv.Key == "sportsPark"
                    ? Compose(With(kv.Key, "pitch", "tennisCourts", "basketballCourt"), FiveHa, TestKit.RoadAlongBottom(250))
                    : Compose(With(kv.Key), FiveHa, TestKit.RoadAlongBottom(250));
                var objects = kv.Key == "sportsPark" ? composed.Layers.SelectMany(l => l.Objects).ToList() : composed.Layers[0].Objects;
                foreach (var cat in kv.Value)
                    Assert.True(objects.Any(o => o.Category == cat), kv.Key + " has no " + cat);
            }
            // The reserve is a forest: far denser planting than the picnic grounds.
            int Trees(string id) => Compose(With(id), FiveHa).Layers[0].Objects.Count(o => o.Role == ObjectRole.Tree);
            Assert.True(Trees("natureReserve") > 3 * Trees("picnicGrounds"));
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.DogAgility] = 0, [AssetCategories.Glasshouse] = 0, [AssetCategories.Shelter] = 0,
                [AssetCategories.SportsGoal] = 0, [AssetCategories.Sports] = 0, [AssetCategories.MarketStall] = 0, [AssetCategories.Kiosk] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var id in new[] { "dogPark", "botanicalGarden", "sportsPark", "marketSquare" })
                {
                    var plan = Compose(With(id), FiveHa);
                    Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
                    Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Layers[0].Objects, o => none.ContainsKey(o.Category ?? ""));
                }
        }

        [Fact]
        public void FormalGardenIsSymmetric()
        {
            var plan = Compose(With("formalGarden"), TestKit.Rect(200, 140)).Layers[0];
            var hedges = plan.Objects.Where(o => o.Category == AssetCategories.Hedge).Select(o => o.Position.xz).ToList();
            Assert.NotEmpty(hedges);
            // Mirror across both centre lines of the rectangle: every hedge has a partner.
            var c = new float2(100, 70);
            foreach (var h in hedges.Take(200))
            {
                var m = 2f * c - h;
                Assert.Contains(hedges, q => math.distance(q, m) < 0.6f);
            }
        }

        [Fact]
        public void DogParkHasDoubleGates()
        {
            var plan = Compose(With("dogPark"), TestKit.Rect(120, 90), TestKit.RoadAlongBottom(120)).Layers[0];
            var fences = plan.Nets.Count(n => n.Role == NetRole.Fence);
            var noVestibule = Compose(WithSettings("dogPark", ("doubleGates", false)), TestKit.Rect(120, 90), TestKit.RoadAlongBottom(120)).Layers[0];
            Assert.True(fences >= noVestibule.Nets.Count(n => n.Role == NetRole.Fence) + 3);
        }

        // ───────────── features ─────────────

        [Fact]
        public void TerrainBlendsToOriginalGroundAtTheEdgeAndKeepsTheSlope()
        {
            foreach (var shape in new[] { "mounds", "hills", "terraces", "terracedDown", "sunken", "raised", "bowl", "flatten" })
            {
                var site = new SiteContext { HeightAt = p => 0.02f * p.x };   // gentle slope
                var input = TestKit.Input(TestKit.Rect(200, 160), TestKit.Gen("terrain").Defaults(), 3, null, site);
                input.Settings.Values["shape"] = shape;
                input.Settings.Values["height"] = 4.0;
                var plan = TestKit.Plan("terrain", input);
                Assert.False(plan.HasErrors);
                if (shape != "flatten") Assert.NotEmpty(plan.Terraform);
                Assert.All(plan.Terraform, t => Assert.True(t.Permanent));
                if (plan.Terraform.Count > 0) Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey && i.Severity == IssueSeverity.Warning);
                var ring = Poly.To2D(input.Shape.Ring);
                foreach (var t in plan.Terraform)
                {
                    var c = Poly.Centroid(Poly.To2D(t.Ring));
                    float edge = Poly.DistanceToBoundary(ring, c);
                    float change = math.abs(t.TargetHeight - site.HeightAt(c));
                    // Blended back to the original ground: never steeper than ~30 % measured from the outline.
                    Assert.True(change < edge * 0.3f + 0.3f, $"{shape}: {change:0.00} m change {edge:0.0} m from the edge (cliff)");
                    // Mounds/hills add to the ground; level-based shapes also flatten it to its mean (ground range 4 m here).
                    float bound = shape == "mounds" || shape == "hills" ? 4.01f : 8.01f;
                    Assert.True(change <= bound, shape + " exceeds height");
                }
            }
        }

        [Fact]
        public void ShopsStandNearEntrancesBesidePaths()
        {
            var site = TestKit.RoadAlongBottom(200);
            var plan = Compose(TestKit.Entered(With("park", "shops"), TestKit.Rect(200, 150), site), TestKit.Rect(200, 150), site);
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var shops = plan.Layers.Single(l => l.GeneratorId == "shops");
            var stands = shops.Objects.Where(o => o.Category == AssetCategories.Kiosk).ToList();
            Assert.NotEmpty(stands);
            var ends = park.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide)
                .SelectMany(n => new[] { n.A.xz, n.D.xz }).Where(p => Poly.DistanceToBoundary(Poly.To2D(TestKit.Rect(200, 150)), p) < 5f).ToList();   // entrances stop at the kerb (StreetJoins)
            Assert.NotEmpty(ends);
            Assert.True(ends.Min(e => math.distance(e, stands[0].Position.xz)) < 40f, "first stand is not near an entrance");
            AssertNothingOnPaths(new AreaPlan { GeneratorId = "shops", Objects = shops.Objects, Nets = park.Nets });
        }

        [Fact]
        public void StreetShopsZoneTheFrontageAndTheParkKeepsOut()
        {
            var ring = TestKit.Rect(200, 150);
            // s12: the park defaults to speckled shops; compare an explicit row with speckled at the same amount.
            var row = With("park", "edgeZoning");
            row.Root.Settings.Values["shopLayout"] = "band";
            row.Root.Settings.Values["shopAmount"] = "some";
            var plan = Compose(row, ring, TestKit.RoadAlongBottom(200));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var zoning = plan.Layers.Where(l => l.GeneratorId == "edgeZoning").SelectMany(l => l.Zoning).ToList();
            Assert.NotEmpty(zoning);
            // (s40) The zoning is the road's own cells: a 12 m road counts as two cells wide (ZoneUtils.GetCellWidth), so its
            // row 0 starts 2 m past the kerb (z = 2; the Marquee rect is shrunk 1 m).
            Assert.All(zoning, z => Assert.True(math.min(math.min(z.A.z, z.B.z), math.min(z.C.z, z.D.z)) < 4f, "zoning not on the road side"));
            foreach (var z in zoning)
            {
                var quad = new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz };
                foreach (var o in park.Objects)
                    Assert.False(Poly.Contains(quad, o.Position.xz), $"{o.Role} at {o.Position.xz} stands on zoned land");
                // Entrance paths cross the frontage through a gap.
                foreach (var n in park.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide))
                    Assert.False(Poly.Contains(quad, n.A.xz) && Poly.Contains(quad, n.D.xz));
            }
            var pads = WithSettings("park", ("shopLayout", "pads"), ("shopAmount", "some"));
            Assert.True(Editor.SetFeature(pads, "0", "edgeZoning", true, 3));
            var padPlan = Compose(pads, ring, TestKit.RoadAlongBottom(200));
            double area(IEnumerable<PlannedZoning> zs) => zs.Sum(z => Pricing.PolygonArea(new[] { z.A, z.B, z.C, z.D }));
            Assert.True(area(padPlan.Layers.SelectMany(l => l.Zoning)) < area(zoning));
        }

        [Fact]
        public void RestroomsByAreaNearAnEntrance()
        {
            var ring = TestKit.Rect(250, 200);
            var site = TestKit.RoadAlongBottom(250);
            var plan = Compose(TestKit.Entered(With("park", "restrooms"), ring, site), ring, site);
            var rest = plan.Layers.Single(l => l.GeneratorId == "restrooms").Objects.Where(o => o.Category == AssetCategories.Restroom).ToList();
            Assert.Equal(2, rest.Count);   // 5 ha, one per 3 ha (rounded)
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var ends = park.Nets.SelectMany(n => new[] { n.A.xz, n.D.xz }).Where(p => Poly.DistanceToBoundary(Poly.To2D(ring), p) < 5f).ToList();   // entrances stop at the kerb (StreetJoins)
            Assert.True(rest.Min(r => ends.Min(e => math.distance(e, r.Position.xz))) < 45f, "no restroom near an entrance");
            Assert.True(math.distance(rest[0].Position.xz, rest[1].Position.xz) > 30f);
        }

        [Fact]
        public void RocksAndNaturalGroundPaintTheTerrainInside()
        {
            var r = With("park", "rocks");
            r.Root.Settings.Values["naturalGround"] = true;
            var plan = Compose(r, TestKit.Rect(200, 150));
            var rocks = plan.Layers.Single(l => l.GeneratorId == "rocks");
            Assert.Contains(rocks.Objects, o => o.Category == AssetCategories.Rock);
            Assert.NotEmpty(rocks.Paint);
            Assert.NotEmpty(plan.Layers.Single(l => l.GeneratorId == "park").Paint);
            var ring = Poly.To2D(TestKit.Rect(200, 150));
            foreach (var p in plan.Layers.SelectMany(l => l.Paint))
                foreach (var v in p.Ring) Assert.True(Poly.Contains(ring, v.xz), $"paint outside the area: layer {p.Layer} shape {p.Shape?.GetType().Name} at {v} ring {p.Ring.Length}");
            Assert.Contains(plan.AllIssues(), i => i.Key == TerrainPaint.PermanentKey);
        }

        [Theory]
        [InlineData("gazebo", 0)]
        [InlineData("gazebo", 1)]
        [InlineData("bandstand", 2)]
        public void BandstandAndGazeboBenchesFaceTheFocusOnPaving(string kind, int rows)
        {
            // In-game finding: benches round a gazebo/bandstand were loose on the lawn. Now every bench sits on
            // the paving, faces the structure (front = +Z of RotateY(yaw)), and a gazebo ring is evenly spaced.
            var r = With("park", "bandstand");
            var step = FindStep(r.Root, "bandstand");
            step.Settings.Values["kind"] = kind;
            step.Settings.Values["rows"] = (double)rows;
            var plan = Compose(r, TestKit.Rect(220, 160));
            var layer = plan.Layers.Single(l => l.GeneratorId == "bandstand");
            var focus = layer.Objects.First(o => o.Role == ObjectRole.Furniture).Position.xz;
            var benches = layer.Objects.Where(o => o.Role == ObjectRole.Bench).ToList();
            Assert.True(benches.Count >= 3, kind + " has " + benches.Count + " benches");
            var paving = layer.Surfaces.Select(s => Poly.To2D(s.Ring)).ToList();
            foreach (var b in benches)
            {
                var fwd = math.mul(quaternion.RotateY(b.Yaw), new float3(0, 0, 1)).xz;
                Assert.True(math.dot(fwd, math.normalize(focus - b.Position.xz)) > 0.98f, "bench not facing the " + kind);
                Assert.True(paving.Any(p => Poly.Contains(p, b.Position.xz)), "bench off the paving");
            }
            if (kind == "gazebo" && rows == 0)
            {
                var d = benches.Select(b => math.distance(b.Position.xz, focus)).ToList();
                Assert.True(d.Max() - d.Min() < 0.05f, "ring benches at one radius");
            }
        }

        private static RecipeStep FindStep(RecipeStep s, string layer)
        {
            if (s.Layer == layer) return s;
            foreach (var c in s.Children) { var f = FindStep(c, layer); if (f != null) return f; }
            return null;
        }

        [Fact]
        public void RockyOutcropsAreMostlyPaintWithFewClusteredRocks()
        {
            // Playtest 4b #3: mostly terrain paint, a few rocks in odd huddles, never evenly spaced.
            Assert.Equal(new[] { 5 }, Placemaker.Core.Layers.AddOns.RocksAddOn.OddGroups(5));
            Assert.Equal(new[] { 5, 1 }, Placemaker.Core.Layers.AddOns.RocksAddOn.OddGroups(6));
            Assert.Equal(new[] { 7, 3 }, Placemaker.Core.Layers.AddOns.RocksAddOn.OddGroups(10));
            var r = With("park", "rocks");
            var plan = Compose(r, TestKit.Rect(200, 150));
            var rocks = plan.Layers.Single(l => l.GeneratorId == "rocks");
            var stones = rocks.Objects.Where(o => o.Category == AssetCategories.Rock).ToList();
            Assert.NotEmpty(stones);
            float painted = rocks.Paint.Where(p => p.Strength >= 0.3f).Sum(p => Math.Abs(Poly.SignedArea(Poly.To2D(p.Ring))));
            float stoneArea = stones.Sum(o => 0.5f * o.Scale * o.Scale);
            Assert.True(painted > 8f * stoneArea, $"paint {painted:0} m² vs stones {stoneArea:0} m²");
            Assert.True(rocks.Paint.Select(p => p.Strength).Distinct().Count() >= 3, "soft edges need graded strengths");
            // Clustered: most stones have a neighbour within 3 m, and spacing varies (no lattice/spiral).
            var nn = stones.Select(a => stones.Where(b => b != a).Select(b => math.distance(a.Position.xz, b.Position.xz)).DefaultIfEmpty(0f).Min()).ToList();
            Assert.True(nn.Count(d => d < 3f) >= nn.Count * 0.7f, "rocks should huddle");
            Assert.True(nn.Max() - nn.Min() > 0.3f, "spacing must not be uniform");
            Assert.True(stones.Select(o => Math.Round(o.Scale, 1)).Distinct().Count() >= 3, "varied sizes");
        }

        // ───────────── park: free space, playground, path styles ─────────────

        [Theory]
        [InlineData("natural")]
        [InlineData("woodland")]
        [InlineData("formal")]
        [InlineData("garden")]
        [InlineData("meadow")]
        public void BigParkFeaturesFindTheirSpace(string preset)
        {
            // Playtest 2: "No free space left" in an obviously open park; the playground "did nothing".
            var gen = GeneratorRegistry.Get("park");
            var r = With("park", "playground", "picnic", "fountain", "restrooms");
            foreach (var kv in gen.Presets.Single(p => p.Id == preset).Values.Values) r.Root.Settings.Values[kv.Key] = kv.Value;
            var plan = Compose(r, TestKit.Rect(300, 200), TestKit.RoadAlongBottom(300));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var f in new[] { "playground", "picnic", "fountain", "restrooms" })
            {
                var layers = plan.Layers.Where(l => l.GeneratorId == f).ToList();
                Assert.NotEmpty(layers);
                Assert.True(layers.Sum(l => l.Objects.Count) > 0, $"{preset}: {f} placed nothing");
                Assert.DoesNotContain(layers.SelectMany(l => l.Issues), i => i.Key == Keys.NoSpace);
            }
        }

        [Theory]
        [InlineData("auto")]
        [InlineData("garden")]
        [InlineData("curvy")]
        [InlineData("formal")]
        [InlineData("lakeside")]
        [InlineData("scenic")]
        public void PathStylesPlanDeterministically(string style)
        {
            var r = WithSettings("park", ("pathStyle", style));
            Assert.True(Editor.SetFeature(r, "0", "pond", true, 5));
            Assert.True(Editor.SetFeature(r, "0", "playground", true, 6));
            Assert.True(Editor.SetFeature(r, "0", "picnic", true, 7));
            var a = Compose(r, TestKit.Rect(260, 200), TestKit.RoadAlongBottom(260));
            var b = Compose(r, TestKit.Rect(260, 200), TestKit.RoadAlongBottom(260));
            Assert.True(string.IsNullOrEmpty(Errors(a)), Errors(a));
            var park = a.Layers.Single(l => l.GeneratorId == "park");
            Assert.Contains(park.Nets, n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);
            Assert.Equal(TestKit.Fingerprint(park), TestKit.Fingerprint(b.Layers.Single(l => l.GeneratorId == "park")));
            AssertInside(park, TestKit.Rect(260, 200));
            TestKit.AssertObjectsClearOfNets(park);
            if (style == "formal")
                foreach (var n in park.Nets.Where(n => n.Role == NetRole.PathwayWide || n.Role == NetRole.Pathway))
                {
                    // Straight courses only, except feature spurs (also straight).
                    var d = math.normalizesafe(n.D.xz - n.A.xz);
                    Assert.True(math.abs(Poly.Cross(d, n.B.xz - n.A.xz)) < 0.05f && math.abs(Poly.Cross(d, n.C.xz - n.A.xz)) < 0.05f);
                }
            if (style == "lakeside" || style == "scenic")
                Assert.DoesNotContain(a.AllIssues(), i => i.Key == "Placemaker.Park.Issue.PathStyleFallback");
        }

        [Fact]
        public void PathToItLinksTheFeatures()
        {
            var r = With("park", "playground", "picnic");
            var plan = Compose(r, TestKit.Rect(300, 200), TestKit.RoadAlongBottom(300));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            Assert.Equal(2, (int)park.Facts["featureClearings"]);
            var off = With("park", "playground", "picnic");
            off.Root.Settings.Values["playgroundPath"] = false;
            off.Root.Settings.Values["picnicPath"] = false;
            var parkOff = Compose(off, TestKit.Rect(300, 200), TestKit.RoadAlongBottom(300)).Layers.Single(l => l.GeneratorId == "park");
            Assert.False(parkOff.Facts.ContainsKey("featureSpurs"));
        }

        // ───────────── plaza depth ─────────────

        [Theory]
        [InlineData("civic")]
        [InlineData("fountain")]
        [InlineData("market")]
        [InlineData("garden")]
        [InlineData("sunken")]
        [InlineData("amphitheatre")]
        [InlineData("terraced")]
        [InlineData("cafe")]
        public void PlazaPresetsAreDeepAndClean(string preset)
        {
            var gen = GeneratorRegistry.Get("plaza");
            var ring = TestKit.Rect(110, 80);
            var plan = gen.Plan(TestKit.Input(ring, gen.Presets.Single(p => p.Id == preset).Values, 4, null, TestKit.RoadAlongBottom(110)), CancellationToken.None);
            Assert.False(plan.HasErrors);
            AssertInside(plan, ring);
            AssertNothingOnPaths(plan);
            TestKit.AssertSurfacesAccepted(plan);
            switch (preset)
            {
                case "amphitheatre":
                    Assert.True(plan.Objects.Count(o => o.Role == ObjectRole.Bench) >= 12);
                    Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Stage);
                    Assert.NotEmpty(plan.Terraform);
                    break;
                case "sunken": case "terraced":
                    Assert.NotEmpty(plan.Terraform);
                    Assert.Contains(plan.Nets, n => n.Role == NetRole.PathwayWide);   // ramps
                    break;
                case "market":
                    Assert.True(plan.Objects.Count(o => o.Category == AssetCategories.MarketStall) >= 8);
                    break;
                case "civic":
                    Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Bollard);
                    // Art pass a2: the pattern is laid in real paving (several stones), not decals.
                    Assert.True(plan.Surfaces.Select(x => x.Asset.Id).Distinct().Count() >= 3);
                    break;
                case "cafe":
                    Assert.Contains(plan.Objects, o => o.Category == AssetCategories.CafeSeating);
                    break;
            }
            // More than a slab with a thing in the middle.
            Assert.True(plan.Objects.Select(o => o.Category ?? o.Role.ToString()).Distinct().Count() >= 4, preset + " is monotone");
        }

        [Fact]
        public void PlazaDressingIsFast()
        {
            var gen = GeneratorRegistry.Get("plaza");
            var v = gen.Presets.Single(p => p.Id == "amphitheatre").Values.Clone();
            foreach (var kv in gen.Presets.Single(p => p.Id == "cafe").Values.Values) if (!v.Values.ContainsKey(kv.Key) || kv.Key == "cafe" || kv.Key == "shade") v.Values[kv.Key] = kv.Value;
            v.Values["stalls"] = 10.0; v.Values["pool"] = true; v.Values["lawns"] = 20.0; v.Values["beds"] = 4.0; v.Values["levels"] = "terracedUp";
            gen.Plan(TestKit.Input(TestKit.Rect(220, 220), v), CancellationToken.None);
            // Best of three: a single sample sits near the budget (~200 ms) and flaked under load (g5).
            long best = long.MaxValue;
            AreaPlan plan = null;
            for (int run = 0; run < 3; run++)
            {
                var sw = Stopwatch.StartNew();
                plan = gen.Plan(TestKit.Input(TestKit.Rect(220, 220), v), CancellationToken.None);
                sw.Stop();
                best = System.Math.Min(best, sw.ElapsedMilliseconds);
            }
            Assert.False(plan.HasErrors);
            Assert.True(best < TimingBudget.Ms(400), best + " ms");   // maxed-out dressing; planning runs off the main thread
        }

        // ───────────── classification ─────────────

        [Theory]
        [InlineData("BasketballHoop01", AssetCategories.SportsBasketball)]
        [InlineData("SoccerGoal01", AssetCategories.SportsGoal)]
        [InlineData("TennisNet01", AssetCategories.SportsTennis)]
        [InlineData("DogParkAgilityJump01", AssetCategories.DogAgility)]
        [InlineData("DogWasteBin01", AssetCategories.DogAmenity)]
        [InlineData("Greenhouse01", AssetCategories.Glasshouse)]
        [InlineData("GardenShed02", AssetCategories.GardenShed)]
        [InlineData("VegetablePatch01", AssetCategories.GardenCrop)]
        [InlineData("MarketStall01", AssetCategories.MarketStall)]
        [InlineData("CafeTableParasol01", AssetCategories.CafeSeating)]
        [InlineData("Bandstand01", AssetCategories.Stage)]
        [InlineData("LookoutTower01", AssetCategories.Lookout)]
        [InlineData("Gravestone03", AssetCategories.Grave)]
        [InlineData("SkateRamp01", AssetCategories.SportsSkate)]
        [InlineData("Bleachers01", AssetCategories.SportsStand)]
        [InlineData("SportsFloodlight01", AssetCategories.FloodLight)]
        [InlineData("HedgeStraight01", AssetCategories.Hedge)]
        public void LeisureClassification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(category, AssetHeuristics.Classify(name, t));
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name + " should be automatic for " + category);
        }

        [Fact]
        public void SubcategoriesAlsoCarryTheirParent()
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(AssetCategories.Sports, AssetHeuristics.Classify("BasketballHoop01", t));
            Assert.Contains(AssetCategories.Kiosk, AssetHeuristics.Classify("MarketStall01", t));
            Assert.Contains(AssetCategories.Centerpiece, AssetHeuristics.Classify("StatueOfTheMayor", t));
            Assert.Contains(AssetCategories.CenterpieceStatue, AssetHeuristics.Classify("StatueOfTheMayor", t));
            // Stands are never benches.
            Assert.DoesNotContain(AssetCategories.Bench, AssetHeuristics.Classify("Bleachers01", t));
        }
    }
}
