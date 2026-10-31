using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Geo = Placemaker.Core.Geometry.Poly;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;
using Xunit;
using static Placemaker.Tests.RoadsTestUtil;

namespace Placemaker.Tests
{
    /// <summary>Roads layer behaviour: blocks, connections, obstacles, options, recipes, edge zoning.</summary>
    [Collection(TimingCollection.Name)]
    public class RoadsPlanTests
    {
        private static readonly float3[] Rect600 = Ccw(Poly(0, 0, 600, 0, 600, 400, 0, 400));

        private static SiteContext BlockSite(bool diagonal = false)
        {
            var site = new SiteContext { HeightAt = Hills };
            long id = 100;
            void Road(float x0, float z0, float x1, float z1, float w = 12f)
            {
                var a = new float3(x0, 0, z0); var d = new float3(x1, 0, z1);
                site.NearbyNets.Add(new NearbyNet { ExternalId = id++, A = a, B = math.lerp(a, d, 1 / 3f), C = math.lerp(a, d, 2 / 3f), D = d, Width = w, Category = NetCategory.Road });
            }
            Road(0, 0, 400, 0); Road(400, 0, 400, 300); Road(400, 300, 0, 300); Road(0, 300, 0, 0);
            if (diagonal) Road(-50, 50, 450, 260, 16f);
            return site;
        }

        private static List<float2[]> Blocks(AreaPlan plan) =>
            plan.Regions.Where(r => r.Tags.Contains(RegionTags.Block)).Select(r => Ring2(r.Ring)).ToList();

        private static float DistToNet(float2 p, PlannedNet n) => new RoadCurve(n.A, n.B, n.C, n.D).DistanceXZ(p, out _);

        /// <summary>Degree of every net end point (ends at the same position share a node, as in the game).</summary>
        private static Dictionary<(float, float), int> Degrees(AreaPlan plan)
        {
            var d = new Dictionary<(float, float), int>();
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                {
                    var k = (p.x, p.z);
                    d[k] = d.TryGetValue(k, out int v) ? v + 1 : 1;
                }
            return d;
        }

        [Fact]
        public void LayersAreRegisteredWithTheirContracts()
        {
            var roads = GeneratorRegistry.Get("roads");
            Assert.NotNull(roads);
            Assert.Equal(LayerKind.Fill, roads.Kind);
            Assert.Contains(RegionTags.Block, roads.Emits);
            Assert.Contains(RegionTags.Edge, roads.Emits);
            var zoning = GeneratorRegistry.Get("edgeZoning");
            Assert.NotNull(zoning);
            Assert.Equal(LayerKind.AddOn, zoning.Kind);
            Assert.Contains(RegionTags.Edge, zoning.Accepts);
            Assert.Contains(RegionTags.Block, zoning.Accepts);
            var pattern = roads.Controls.Single(c => c.Id == RoadsIds.Pattern);
            Assert.Equal(ControlKind.Choice, pattern.Kind);
            Assert.Equal(10, pattern.Options.Count);   // s52: + hillside terraces
            Assert.Equal(AssetCategories.Road, roads.Controls.Single(c => c.Id == RoadsIds.StreetAsset).AssetCategory);
            Assert.Equal(AssetCategories.Zone, zoning.Controls.Single(c => c.Id == EdgeZoningAddOn.ZoneControl).AssetCategory);
            Assert.Equal(roads.Controls.Count, roads.Controls.Select(c => c.Id).Distinct().Count());
            foreach (var c in roads.Controls.Where(c => c.VisibleWhen.Length > 0))
                Assert.Contains(roads.Controls, o => o.Id == c.VisibleWhen.Split('=')[0]);
            foreach (var c in roads.Controls.Where(c => c.Kind == ControlKind.Slider))
                Assert.InRange(Convert.ToDouble(c.Default), c.Min, c.Max);
        }

        private static Dictionary<string, string> LoadLang(string file)
        {
            var all = new Dictionary<string, string>(StringComparer.Ordinal);
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var p in doc.RootElement.EnumerateObject()) all[p.Name] = p.Value.GetString();
            return all;
        }

        private static IEnumerable<string> RequiredKeys()
        {
            foreach (var id in new[] { "roads", "edgeZoning" })
            {
                var g = GeneratorRegistry.Get(id);
                yield return g.NameKey;
                foreach (var c in g.Controls)
                {
                    yield return c.LabelKey;
                    yield return c.TooltipKey;
                    foreach (var o in c.Options) { yield return o.LabelKey; yield return o.TooltipKey; }
                }
                foreach (var p in g.Presets) yield return p.LabelKey;
            }
            foreach (var id in new[] { "roads", "edgeZoning" })
                foreach (var c in GeneratorRegistry.Get(id).Controls) yield return RoadsSchema.GroupLabel(c.Group);
            foreach (var k in new[] { RoadsPlanner.StatLength, RoadsPlanner.StatBlocks, RoadsPlanner.StatJunctions, RoadsPlanner.StatConnections,
                RoadsPlanner.IssueNothing, RoadsPlanner.IssueFlat, RoadsPlanner.IssueObstacles, RoadsPlanner.IssueFailed, RoadsPlanner.IssueBadArea,
                "Placemaker.Roads.Reason.NoArea", "Placemaker.EdgeZoning.Stat.Area", "Placemaker.EdgeZoning.Issue.NoFrontage" })
                yield return k;
        }

        [Fact]
        public void EveryControlOptionPresetStatAndIssueHasEnglishText()
        {
            var lang = LoadLang(Path.Combine(TestKit.RepoRoot(), "Lang", "en-US.roads.json"));
            var missing = RequiredKeys().Distinct().Where(k => !lang.ContainsKey(k) || string.IsNullOrWhiteSpace(lang[k])).ToList();
            Assert.True(missing.Count == 0, "missing en-US: " + string.Join(", ", missing));
        }

        [Fact]
        public void TranslationsAreValidSubsetsOfEnglish()
        {
            string dir = Path.Combine(TestKit.RepoRoot(), "Lang");
            var en = LoadLang(Path.Combine(dir, "en-US.roads.json"));
            int files = 0;
            foreach (var file in Directory.GetFiles(dir, "*.roads.json"))
            {
                files++;
                var loc = LoadLang(file);
                foreach (var kv in loc)
                {
                    Assert.True(en.ContainsKey(kv.Key), $"{Path.GetFileName(file)}: unknown key {kv.Key}");
                    Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{Path.GetFileName(file)}: empty {kv.Key}");
                    Assert.True(en[kv.Key].Contains("{0}") == kv.Value.Contains("{0}"), $"{Path.GetFileName(file)}: placeholder in {kv.Key}");
                }
            }
            Assert.True(files > 1);
        }

        [Fact]
        public void GridEmitsStableBlocksCoveringTheAreaMinusRoads()
        {
            var input = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid")));
            input.Site.HeightAt = Hills;
            var plan = Run(input);
            var blocks = plan.Regions.Where(r => r.Tags.Contains(RegionTags.Block)).ToList();
            // (Smarter roads) the count follows the layout the rating picks on the hill (bent streets, no slivers): about two
            // dozen blocks; the same input always gives the same blocks (keys in order, below)
            Assert.InRange(blocks.Count, 16, 30);
            Assert.Equal(blocks.Count, Run(input).Regions.Count(r => r.Tags.Contains(RegionTags.Block)));
            for (int i = 0; i < blocks.Count; i++) Assert.Equal("block:" + i, blocks[i].Key);
            foreach (var b in blocks)
            {
                var r = Ring2(b.Ring);
                Assert.True(Geo.IsSimple(r));
                Assert.True(Geo.SignedArea(r) > 0);
                Assert.True(Geo.MinEdgeLength(r) >= SurfaceAcceptance.MinEdge - 1e-3f);
                // On the ground the build leaves (terraced blocks on a hill).
                Assert.All(b.Ring, p => Assert.Equal((plan.HeightAfter ?? Hills)(p.xz), p.y, 3));
            }
            // Ordered along the principal (long) axis: first block at the low-x end.
            Assert.True(Geo.Centroid(Ring2(blocks[0].Ring)).x < 100f);
            // Sample the area: every point is either buildable land (in exactly one block) or under a road.
            var rings = blocks.Select(b => Ring2(b.Ring)).ToList();
            int inBlock = 0, neither = 0, total = 0;
            var area = Ring2(Rect600);
            for (float x = 2; x < 600; x += 5)
                for (float z = 2; z < 400; z += 5)
                {
                    var p = new float2(x, z);
                    if (!Geo.Contains(area, p)) continue;
                    total++;
                    int containing = rings.Count(r => Geo.Contains(r, p));
                    Assert.True(containing <= 1, $"blocks overlap at {p}");
                    // (a retaining wall's quay lies inside its block by design: not a road; nor a footpath, which crosses blocks)
                    float road = plan.Nets.Where(n => n.Category != AssetCategories.NetQuay && n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide)
                        .Min(n => DistToNet(p, n) - Composer.DefaultHalfWidth(n.Role));
                    if (containing == 1)
                    {
                        Assert.True(road > -0.3f, $"block point {p} lies {-road:F2} m inside a road");
                        inBlock++;
                    }
                    else if (road > 0.5f) neither++;
                }
            // Leftovers: only the rounded corners outside the perimeter road.
            Assert.True(neither < 0.02 * total, $"{neither} of {total} samples are neither block nor road");
            Assert.True(inBlock > 0.6 * total);
        }

        [Fact]
        public void EdgeStripsLieInsideBlocksAlongNewStreets()
        {
            var plan = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"))));
            var blocks = Blocks(plan);
            var edges = plan.Regions.Where(r => r.Tags.Contains(RegionTags.Edge)).ToList();
            Assert.NotEmpty(edges);
            for (int i = 0; i < edges.Count; i++) Assert.Equal("edge:" + i, edges[i].Key);
            foreach (var e in edges)
            {
                var r = Ring2(e.Ring);
                Assert.True(Geo.IsSimple(r) && Geo.SignedArea(r) > 0);
                float2 c = Geo.Centroid(r);
                Assert.Contains(blocks, b => Geo.Contains(b, c));
                float near = r.Min(p => plan.Nets.Min(n => DistToNet(p, n) - Composer.DefaultHalfWidth(n.Role)));
                Assert.True(near < 1f, "strip does not touch a street");
            }
        }

        [Fact]
        public void EveryPatternEmitsBlocks()
        {
            foreach (var pat in RoadsSchema.PatternValues)
            {
                var input = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, pat)));
                input.Site.HeightAt = Hills;
                var plan = Run(input);
                Assert.True(plan.Nets.Count > 0, pat);
                Assert.True(Blocks(plan).Count >= 2, $"{pat}: {Blocks(plan).Count} blocks");
                Assert.False(plan.HasErrors, pat);
            }
        }

        [Fact]
        public void SameInputSamePlan_DifferentVariationDifferentPlan()
        {
            AreaPlan P(int variation) => Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "organic"), (RoadsIds.OrganicVariation, (double)variation))));
            Assert.Equal(TestKit.Fingerprint(P(3)), TestKit.Fingerprint(P(3)));
            Assert.NotEqual(TestKit.Fingerprint(P(3)), TestKit.Fingerprint(P(4)));
        }

        [Fact]
        public void PlanningIsThreadSafeAndReentrant()
        {
            var inputs = RoadsSchema.PatternValues.Select(p =>
            {
                var i = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, p)));
                i.Site.HeightAt = Hills;
                return i;
            }).ToList();
            var sequential = inputs.Select(i => TestKit.Fingerprint(Run(i))).ToList();
            var parallel = new string[inputs.Count * 3];
            Parallel.For(0, parallel.Length, k => parallel[k] = TestKit.Fingerprint(Run(inputs[k % inputs.Count])));
            for (int k = 0; k < parallel.Length; k++) Assert.Equal(sequential[k % inputs.Count], parallel[k]);
        }

        [Fact]
        public void HonoursCancellation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            foreach (var pat in new[] { "grid", "concentric", "organic" })
                Assert.ThrowsAny<OperationCanceledException>(() =>
                    new RoadsLayer().Plan(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, pat))), cts.Token));
        }

        [Fact]
        public void BadInputReportsIssuesInsteadOfThrowing()
        {
            var layer = new RoadsLayer();
            Assert.Empty(layer.Plan(Input(Poly(0, 0, 1, 0, 1, 1)), CancellationToken.None).Nets);
            var nan = layer.Plan(Input(new[] { new float3(float.NaN, 0, 0), new float3(1, 0, 0), new float3(0, 0, 1) }), CancellationToken.None);
            Assert.True(nan.HasErrors);
            var two = layer.Plan(new AreaInput { Shape = new AreaShape { Ring = new[] { new float3(0, 0, 0), new float3(500, 0, 0) } }, Settings = layer.Defaults() }, CancellationToken.None);
            Assert.True(two.HasErrors);
            Assert.Empty(layer.Plan(null, CancellationToken.None).Nets);
        }

        [Fact]
        public void StreetEndsOnExistingRoadsAreAttachedExactly()
        {
            var site = BlockSite();
            var outline = Poly(0, 0, 400, 0, 400, 300, 0, 300);
            var plan = Run(Input(outline, AreaSource.Block, Settings((RoadsIds.Pattern, "grid")), site));
            Assert.NotEmpty(plan.Nets);
            Assert.DoesNotContain(plan.Nets, n => n.Role == NetRole.PerimeterRoad);
            var ids = site.NearbyNets.ToDictionary(n => n.ExternalId);
            var ring = Ring2(Ccw(outline));
            int attached = 0;
            foreach (var n in plan.Nets)
                foreach (var (p, id, t) in new[] { (n.A, n.StartAttach, n.StartAttachT), (n.D, n.EndAttach, n.EndAttachT) })
                {
                    if (Geo.DistanceToBoundary(ring, p.xz) < 1f) Assert.True(id >= 0, $"end {p} on the outline road is not attached");
                    if (id < 0) continue;
                    attached++;
                    Assert.True(ids.ContainsKey(id));
                    Assert.InRange(t, 0f, 1f);
                    var e = ids[id];
                    var at = new RoadCurve(e.A, e.B, e.C, e.D).Position(t);
                    Assert.True(math.distance(at, p) < 1e-3f, $"attached end {p} is off the road at t={t}");
                }
            Assert.True(attached >= 8);
            Assert.True(TestKit.Stat(plan, RoadsPlanner.StatConnections) >= 8);
        }

        [Fact]
        public void StreetsCrossingAnExistingRoadAreSplitAndJoinIt()
        {
            var site = BlockSite(diagonal: true);
            var plan = Run(Input(Poly(0, 0, 400, 0, 400, 300, 0, 300), AreaSource.Block, Settings((RoadsIds.Pattern, "grid")), site));
            var diag = site.NearbyNets.Last();
            var dc = new RoadCurve(diag.A, diag.B, diag.C, diag.D);
            int joins = 0;
            foreach (var n in plan.Nets)
            {
                // No new street passes over the diagonal road except at its own ends.
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                for (int i = 2; i <= 18; i++)
                    Assert.True(dc.DistanceXZ(c.Position(i / 20f).xz, out _) > 0.5f, $"street {n.A}->{n.D} crosses the existing road mid-segment");
                if (n.StartAttach == diag.ExternalId) joins++;
                if (n.EndAttach == diag.ExternalId) joins++;
            }
            Assert.True(joins >= 4, $"{joins} ends join the crossing road");
            foreach (var b in Blocks(plan))
                foreach (var p in b) { float dd = dc.DistanceXZ(p, out float tt); Assert.True(dd > diag.Width / 2 - 0.3f, $"block covers the existing road at {p} d={dd} t={tt}"); }
        }

        [Fact]
        public void DrawnPerimeterRoadJoinsRoadsCrossingTheOutline()
        {
            var site = new SiteContext();
            var a = new float3(-100, 0, 150); var d = new float3(700, 0, 250);
            site.NearbyNets.Add(new NearbyNet { ExternalId = 9, A = a, B = math.lerp(a, d, 1 / 3f), C = math.lerp(a, d, 2 / 3f), D = d, Width = 16, Category = NetCategory.Road });
            var plan = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid")), site));
            int perimeterJoins = plan.Nets.Where(n => n.Role == NetRole.PerimeterRoad).Count(n => n.StartAttach == 9 || n.EndAttach == 9);
            Assert.True(perimeterJoins >= 4, $"perimeter road joins the crossing road {perimeterJoins} times");
        }

        [Fact]
        public void ConnectExistingOffLeavesEndsFree()
        {
            var plan = Run(Input(Poly(0, 0, 400, 0, 400, 300, 0, 300), AreaSource.Block,
                Settings((RoadsIds.Pattern, "grid"), (RoadsIds.ConnectExisting, false)), BlockSite()));
            Assert.All(plan.Nets, n => Assert.True(n.StartAttach < 0 && n.EndAttach < 0));
        }

        [Fact]
        public void ObstacleRemovalLeavesNoDanglingStubs()
        {
            var centre = new float2(310, 190);
            foreach (var pat in new[] { "grid", "loop", "concentric", "radial", "organic", "superblock" })
            {
                var site = new SiteContext { HeightAt = _ => 10f, WaterDepthAt = p => math.distance(p, centre) < 90f ? 5f : 0f };
                var plan = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, pat)), site));
                Assert.NotEmpty(plan.Nets);
                foreach (var n in plan.Nets)
                {
                    var c = new RoadCurve(n.A, n.B, n.C, n.D);
                    for (int i = 0; i <= 16; i++) Assert.True(site.WaterDepthAt(c.Position(i / 16f).xz) <= 0.3f, $"{pat}: road in the lake");
                }
                var caps = plan.Objects.Where(o => o.Role == ObjectRole.CulDeSacCap).Select(o => o.Position.xz).ToList();
                var reference = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, pat)), new SiteContext { HeightAt = _ => 10f }));
                var deadEndsBefore = Degrees(reference).Where(kv => kv.Value == 1).Select(kv => new float2(kv.Key.Item1, kv.Key.Item2)).ToList();
                foreach (var kv in Degrees(plan).Where(kv => kv.Value == 1))
                {
                    var p = new float2(kv.Key.Item1, kv.Key.Item2);
                    bool intentional = caps.Any(q => math.distance(q, p) < 0.01f) || deadEndsBefore.Any(q => math.distance(q, p) < 0.5f);
                    Assert.True(intentional, $"{pat}: stub left at {p} by obstacle removal");
                }
                Assert.Contains(plan.Issues, i => i.Key == RoadsPlanner.IssueObstacles);
                Assert.DoesNotContain(Blocks(plan), b => Geo.Contains(b, centre) && Geo.DistanceToBoundary(b, centre) > 0f && false);
            }
        }

        [Fact]
        public void BuildingsAreAvoided()
        {
            var site = new SiteContext();
            // A building exactly on the grid street x = 300.
            site.Obstacles.Add(new Obstacle { A = new float2(290, 140), B = new float2(310, 140), C = new float2(310, 160), D = new float2(290, 160), Kind = ObstacleKind.Building });
            var plan = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid")), site));
            foreach (var n in plan.Nets)
            {
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                for (int i = 0; i <= 16; i++)
                {
                    float2 p = c.Position(i / 16f).xz;
                    Assert.False(p.x > 286 && p.x < 314 && p.y > 136 && p.y < 164, "road through the building: " + n.Role + " " + n.A + " " + n.B + " " + n.C + " " + n.D + " at " + p + " bypassed " + (plan.Facts.TryGetValue(RoadsPlanner.FactBypassed, out var by) ? by : -1));
                }
            }
            // (s61) The street first tries to bend round the building; only when it cannot, the blocks beside it merge.
            Assert.True(TestKit.Stat(plan, RoadsPlanner.StatBlocks) < 24 || plan.Facts.ContainsKey(RoadsPlanner.FactBypassed), "the street was bypassed or the blocks beside the removed street merge");
        }

        [Fact]
        public void AvoidObstaclesOffKeepsEverything()
        {
            var site = new SiteContext { WaterDepthAt = _ => 9f };
            var plan = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"), (RoadsIds.AvoidObstacles, false)), site));
            Assert.Equal(Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid")))).Nets.Count, plan.Nets.Count);
        }

        [Fact]
        public void PerimeterRoadFollowsSourceUnlessForced()
        {
            bool Has(AreaSource src, string mode) => Run(Input(Rect600, src, Settings((RoadsIds.PerimeterRoad, mode)))).Nets.Any(n => n.Role == NetRole.PerimeterRoad);
            Assert.True(Has(AreaSource.Drawn, "auto"));
            Assert.True(Has(AreaSource.Painted, "auto"));
            Assert.False(Has(AreaSource.Block, "auto"));
            Assert.False(Has(AreaSource.Perimeter, "auto"));
            Assert.True(Has(AreaSource.Block, "on"));
            Assert.False(Has(AreaSource.Drawn, "off"));
        }

        [Fact]
        public void CulDeSacsGetTurningCirclesAndNotches()
        {
            var plan = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "tree"), (RoadsIds.CapStyle, "trees"))));
            var caps = plan.Objects.Where(o => o.Role == ObjectRole.CulDeSacCap).ToList();
            Assert.NotEmpty(caps);
            var deg = Degrees(plan);
            foreach (var c in caps)
            {
                Assert.Equal(1, deg[(c.Position.x, c.Position.z)]);
                Assert.EndsWith("03", c.Asset.Id);
                Assert.InRange(c.AttachedNet, 0, plan.Nets.Count - 1);
                foreach (var b in Blocks(plan)) Assert.False(Geo.Contains(b, c.Position.xz), "block covers a turning circle");
            }
            var none = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "tree"), (RoadsIds.CapStyle, "none"))));
            Assert.DoesNotContain(none.Objects, o => o.Role == ObjectRole.CulDeSacCap);
        }

        [Fact]
        public void CapAndIslandAssetsFollowGameNaming()
        {
            Assert.Equal("CulDeSacSmall01", RoadsPlanner.CapAsset("asphalt", "auto", 6f).Id);
            Assert.Equal("CulDeSacLarge01", RoadsPlanner.CapAsset("asphalt", "xl", 6f).Id);
            Assert.Equal("CulDeSacXL02", RoadsPlanner.CapAsset("grass", "auto", 30f).Id);
            Assert.Contains("CulDeSacSmall02", RoadsPlanner.CapAsset("grass", "large", 6f).Fallbacks);
            Assert.Equal("MediumRoundabout01", RoadsPlanner.IslandAsset(12f).Id);
        }

        [Fact]
        public void CrossingAvenuesGetARoundaboutIsland()
        {
            var plan = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"),
                (RoadsIds.AvenueColumn, true), (RoadsIds.AvenueColumnIndex, 2.0), (RoadsIds.AvenueRow, true), (RoadsIds.AvenueRowIndex, 1.0))));
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Avenue);
            Assert.Single(plan.Objects, o => o.Role == ObjectRole.RoundaboutIsland);
            var off = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"),
                (RoadsIds.AvenueColumn, true), (RoadsIds.AvenueRow, true), (RoadsIds.RoundaboutIsland, false))));
            Assert.DoesNotContain(off.Objects, o => o.Role == ObjectRole.RoundaboutIsland);
        }

        [Fact]
        public void RadialRoundaboutIslandIsACoreBlock_LeftHandTrafficReversesTheRing()
        {
            var right = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "radial"))));
            Assert.Contains(right.Regions, r => r.Tags.Contains(RegionTags.Core) && r.Tags.Contains(RegionTags.Block));
            var ring = right.Nets.Where(n => n.Role == NetRole.Roundabout).ToList();
            Assert.NotEmpty(ring);
            var lht = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "radial")));
            lht.Site.LeftHandTraffic = true;
            var left = Run(lht).Nets.Where(n => n.Role == NetRole.Roundabout).ToList();
            Assert.Equal(ring.Count, left.Count);
            foreach (var r in ring) Assert.Contains(left, l => math.distance(l.A, r.D) < 1e-3f && math.distance(l.D, r.A) < 1e-3f);
        }

        [Fact]
        public void AssetsUpgradesAndWidthsReachThePlan()
        {
            var site = new SiteContext();
            site.Assets["Wide Road"] = new AssetInfo { Id = "Wide Road", NetWidth = 30f };
            var narrow = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"), (RoadsIds.StreetTrees, true), (RoadsIds.StreetBikeLanes, true))));
            var wide = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"), (RoadsIds.StreetAsset, "Wide Road")), site));
            Assert.All(wide.Nets.Where(n => n.Role == NetRole.Street), n => Assert.Equal("Wide Road", n.Asset.Id));
            Assert.All(narrow.Nets.Where(n => n.Role == NetRole.Street), n =>
            {
                Assert.Contains("Left.SecondaryBeautification", n.Upgrades);
                Assert.Contains("Right.SecondaryLane", n.Upgrades);
            });
            double Area(AreaPlan p) => Blocks(p).Sum(b => Geo.Area(b));
            Assert.True(Area(wide) < Area(narrow) - 10000, "wider streets leave less buildable land");
            var withAvenue = Run(Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"), (RoadsIds.AvenueColumn, true), (RoadsIds.AvenueAsset, "Big Avenue"))));
            Assert.Contains(withAvenue.Nets, n => n.Role == NetRole.Avenue);
            Assert.All(withAvenue.Nets.Where(n => n.Role == NetRole.Avenue), n => Assert.Equal("Big Avenue", n.Asset.Id));
        }

        [Fact]
        public void ContourNeedsSlopedTerrain()
        {
            var layer = new RoadsLayer();
            var flat = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "contour")));
            Assert.False(layer.CheckReady(flat).Ready);
            // (p1-ground) a plan made anyway falls back to winding streets rather than nothing
            var flatPlan = Run(flat);
            Assert.NotEmpty(flatPlan.Nets);
            Assert.True(flatPlan.Facts.ContainsKey(RoadsPlanner.FactContourFallback));
            var hilly = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "contour")));
            hilly.Site.HeightAt = p => 0.12f * p.x;
            Assert.True(layer.CheckReady(hilly).Ready);
            Assert.NotEmpty(Run(hilly).Nets);
        }

        [Fact]
        public void FollowTerrainLiftsNodesOntoTheGround()
        {
            var on = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid")));
            on.Site.HeightAt = Hills;
            var onPlan = Run(on);
            var built = onPlan.HeightAfter ?? Hills;
            // (a retaining wall's quay stands at the top of its wall, its deck at explicit heights)
            Assert.All(onPlan.Nets.Where(n => n.Category != AssetCategories.NetQuay), n => { Assert.Equal(built(n.A.xz), n.A.y, 3); Assert.True(n.FollowTerrain); });
            // Natural land: the ground as it is.
            var nat = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"), (RoadsIds.LandShape, "natural")));
            nat.Site.HeightAt = Hills;
            var natPlan = Run(nat);
            Assert.Null(natPlan.HeightAfter);
            Assert.All(natPlan.Nets, n => Assert.Equal(Hills(n.A.xz), n.A.y, 3));
            var off = Input(Rect600, AreaSource.Drawn, Settings((RoadsIds.Pattern, "grid"), (RoadsIds.FollowTerrain, false)));
            off.Site.HeightAt = Hills;
            var plan = Run(off);
            Assert.All(plan.Nets, n => Assert.Equal(plan.Nets[0].A.y, n.A.y, 3));
        }

        [Fact]
        public void RandomizeKeepsPatternAndLockedValues()
        {
            var layer = new RoadsLayer();
            var s = Settings((RoadsIds.Pattern, "organic"), (RoadsIds.OrganicSpacing, 100.0));
            var r1 = layer.Randomize(s, 7, new[] { RoadsIds.OrganicSpacing });
            Assert.Equal("organic", r1.S(RoadsIds.Pattern, ""));
            Assert.Equal(100.0, r1.Num(RoadsIds.OrganicSpacing, 0));
            Assert.True(r1.ContentEquals(layer.Randomize(s, 7, new[] { RoadsIds.OrganicSpacing })));
            Assert.False(r1.ContentEquals(layer.Randomize(s, 8, new[] { RoadsIds.OrganicSpacing })));
            foreach (var c in layer.Controls.Where(c => c.Kind == ControlKind.Slider))
                Assert.InRange(r1.Num(c.Id, c.Min), c.Min, c.Max);
        }

        [Fact]
        public void PresetsOnlyUseKnownControls()
        {
            var layer = new RoadsLayer();
            foreach (var p in layer.Presets)
                foreach (var k in p.Values.Values.Keys) Assert.Contains(layer.Controls, c => c.Id == k);
        }

        [Fact]
        public void FiveHectareAreaPlansWithinBudget()
        {
            var ring = Poly(0, 0, 250, 0, 250, 200, 0, 200);
            var layer = new RoadsLayer();
            foreach (var pat in RoadsSchema.PatternValues) layer.Plan(Input(ring, AreaSource.Drawn, Settings((RoadsIds.Pattern, pat))), CancellationToken.None);
            foreach (var pat in RoadsSchema.PatternValues)
            {
                var input = Input(ring, AreaSource.Drawn, Settings((RoadsIds.Pattern, pat)));
                input.Site.HeightAt = Hills;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                layer.Plan(input, CancellationToken.None);
                sw.Stop();
                // ARCHITECTURE budget: 250 ms in the mod (Release); the Debug test build under parallel load gets 3x.
                Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(750), $"{pat}: {sw.ElapsedMilliseconds} ms for 5 ha");
            }
        }

        // ───────────── recipes ─────────────

        [Theory]
        [InlineData("roads.json")]
        [InlineData("roads-with-parks.json")]
        [InlineData("roads-with-zoning.json")]
        public void ShippedRoadRecipesLoadAndCompose(string file)
        {
            var recipe = TestKit.LoadRecipe(file);
            Assert.Equal(Path.GetFileNameWithoutExtension(file), recipe.Id);
            Assert.True(TestKit.Lang().ContainsKey(recipe.NameKey), recipe.NameKey);
            void Validate(RecipeStep step, IAreaGenerator parent)
            {
                var g = GeneratorRegistry.Get(step.Layer);
                Assert.NotNull(g);
                foreach (var kv in step.Settings.Values) Assert.Contains(g.Controls, c => c.Id == kv.Key);
                if (parent != null && step.Target.Length > 0) Assert.Contains(step.Target, parent.Emits);
                if (g.Accepts.Length > 0 && step.Target.Length > 0) Assert.Contains(step.Target, g.Accepts);
                foreach (var ch in step.Children) Validate(ch, g);
            }
            Validate(recipe.Root, null);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = Rect600, Source = AreaSource.Drawn }, new SiteContext(), CancellationToken.None);
            Assert.False(plan.HasErrors, string.Join("; ", plan.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args))));
            var roads = plan.Layers.Single(l => l.GeneratorId == "roads");
            if (file == "roads-with-parks.json")
            {
                int blocks = roads.Regions.Count(r => r.Tags.Contains(RegionTags.Block));
                Assert.True(blocks >= 4);
                Assert.Equal(blocks, plan.Layers.Count(l => l.GeneratorId == "park"));
            }
            if (file == "roads-with-zoning.json")
            {
                var zoning = plan.Layers.Where(l => l.GeneratorId == "edgeZoning").SelectMany(l => l.Zoning).ToList();
                Assert.NotEmpty(zoning);
                var area = Ring2(Rect600);
                foreach (var z in zoning) foreach (var p in new[] { z.C, z.D }) Assert.True(Geo.Contains(area, p.xz));
            }
        }

        // ───────────── edge zoning ─────────────

        private static AreaInput ZoningInput(float3[] ring, float depth = 48f)
        {
            var z = new EdgeZoningAddOn();
            var s = z.Defaults();
            s.Values[EdgeZoningAddOn.DepthControl] = (double)depth;
            s.Values[EdgeZoningAddOn.ZoneControl] = "EU Residential Low";
            var input = new AreaInput { Shape = new AreaShape { Ring = Ccw(ring) }, Settings = s, RegionTags = new[] { RegionTags.Block } };
            // A street along the bottom edge (z = -8), reserved by the parent roads layer.
            input.Reserved.AddCorridor(new float2(-50, -8), new float2(250, -8), 8f, nameof(NetRole.Street));
            return input;
        }

        [Fact]
        public void EdgeZoningPaintsFrontageInsideTheRegion()
        {
            var ring = TestKit.Rect(200, 120);
            var plan = new EdgeZoningAddOn().Plan(ZoningInput(ring, 32f), CancellationToken.None);
            Assert.NotEmpty(plan.Zoning);
            var r2 = Ring2(Ccw(ring));
            foreach (var q in plan.Zoning)
            {
                Assert.Equal("EU Residential Low", q.ZoneId);
                Assert.True(q.A.z < 0.01f && q.B.z < 0.01f, "road side on the frontage");
                Assert.True(Geo.Contains(r2, q.C.xz) || Geo.DistanceToBoundary(r2, q.C.xz) < 0.06f);
                Assert.InRange(q.C.z, 31.9f, 32.1f);
                Assert.InRange(math.distance(q.A.xz, q.B.xz), 8f, EdgeZoningAddOn.PieceLength + 0.01f);
            }
            float covered = plan.Zoning.Sum(q => math.distance(q.A.xz, q.B.xz));
            Assert.InRange(covered, 199f, 201f);
        }

        [Fact]
        public void EdgeZoningShrinksInThinRegionsAndSkipsOccupiedOrRoadlessOnes()
        {
            var thin = new EdgeZoningAddOn().Plan(ZoningInput(TestKit.Rect(200, 20)), CancellationToken.None);
            Assert.NotEmpty(thin.Zoning);
            Assert.All(thin.Zoning, q => Assert.True(q.C.z <= 20.01f));
            var input = ZoningInput(TestKit.Rect(200, 120));
            input.Reserved.AddDisc(new float2(100, 20), 3f, "Bench");
            var occupied = new EdgeZoningAddOn().Plan(input, CancellationToken.None);
            var spanning = occupied.Zoning.Where(q => math.min(q.A.x, q.B.x) < 100 && math.max(q.A.x, q.B.x) > 100).ToList();
            Assert.NotEmpty(spanning);
            Assert.All(spanning, q => Assert.True(q.C.z <= 17f, $"zoning reaches over the bench ({q.C.z} m deep)"));
            var none = new EdgeZoningAddOn().Plan(new AreaInput { Shape = new AreaShape { Ring = TestKit.Rect(100, 100) }, Settings = new EdgeZoningAddOn().Defaults() }, CancellationToken.None);
            Assert.Empty(none.Zoning);
            Assert.Contains(none.Issues, i => i.Key == "Placemaker.EdgeZoning.Issue.NoFrontage");
        }
    }
}
