using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    /// <summary>
    /// s34 roads overhaul: connectivity guarantee, access management on main roads, walkways, back lanes, gravel tips,
    /// pre-zoning, terrain fit (follow contours / ignore), grade issues, junction geometry, presets — on road-scale sites
    /// (two existing streets, a farm in the middle, flat / hill / valley ground), like the gallery's --roads renders.
    /// </summary>
    public class RoadsDesignTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsDesignTests(ITestOutputHelper o) { _out = o; }

        // ───────────────────────── sites ─────────────────────────

        private static readonly float2[] BaseRing =
        {
            new float2(0, 0), new float2(140, -6), new float2(152, 58), new float2(118, 102), new float2(46, 96), new float2(6, 70),
        };

        public static float3[] Ring(float area)
        {
            double a = 0;
            for (int i = 0, j = BaseRing.Length - 1; i < BaseRing.Length; j = i++) a += BaseRing[j].x * BaseRing[i].y - BaseRing[i].x * BaseRing[j].y;
            float s = (float)math.sqrt(area / math.abs(a * 0.5));
            return BaseRing.Select(p => new float3(p.x * s, 0, p.y * s)).ToArray();
        }

        public static Func<float2, float> Ground(float3[] ring, string kind)
        {
            float xMin = ring.Min(p => p.x), zMin = ring.Min(p => p.z), xMax = ring.Max(p => p.x), zMax = ring.Max(p => p.z);
            float ext = math.max(xMax - xMin, zMax - zMin);
            switch (kind)
            {
                case "hill":
                {
                    var top = new float2(xMin + (xMax - xMin) * 0.58f, zMin + (zMax - zMin) * 0.52f);
                    float sig = ext * 0.27f;
                    return p => 0.015f * (p.x - xMin) + 34f * math.exp(-math.lengthsq(p - top) / (2f * sig * sig));
                }
                case "valley":
                {
                    var a = new float2(xMin, zMin + (zMax - zMin) * 0.25f);
                    var d = math.normalize(new float2(xMax - xMin, (zMax - zMin) * 0.6f));
                    float sig = ext * 0.17f;
                    return p =>
                    {
                        float2 v = p - a;
                        float along = math.dot(v, d), across = v.x * d.y - v.y * d.x;
                        return 20f * (1f - math.exp(-across * across / (2f * sig * sig))) - 0.012f * along;
                    };
                }
                case "steep": return p => 0.13f * (p.y - zMin) + 0.02f * (p.x - xMin);
                // the game runner's "hills" (egg crate, 4 m every 60 m: no main slope) and "mountains" (30 m over 320 m)
                case "eggcrate": return p => 4f * math.sin(p.x * 2f * math.PI / 60f) * math.cos(p.y * 2f * math.PI / 60f);
                case "mountains":
                {
                    float k = 2f * math.PI / 320f;
                    return p => 30f * (0.55f * math.sin((p.x * 0.8f + p.y * 0.6f) * k) + 0.3f * math.sin((-p.x * 0.5f + p.y * 0.9f) * k * 2f + 1.3f)
                        + 0.15f * math.sin((p.x * 0.3f - p.y) * k * 4f + 0.7f));
                }
                default: return _ => 0f;
            }
        }

        private static NearbyNet Street(long id, float3 a, float3 d) => new NearbyNet
        {
            ExternalId = id, A = a, B = math.lerp(a, d, 1f / 3f), C = math.lerp(a, d, 2f / 3f), D = d, Width = 12f, Category = NetCategory.Road, HasSidewalk = true,
        };

        /// <summary>The gallery's road site: streets just outside the south and west edges, a farm a little west of the centre.</summary>
        public static SiteContext Site(float3[] ring, string kind)
        {
            var h = Ground(ring, kind);
            var site = new SiteContext { HeightAt = h };
            float3 Out(float3 p0, float3 p1, bool start)
            {
                float3 d = math.normalize(p1 - p0);
                float3 n = new float3(d.z, 0, -d.x) * 8f;
                var p = start ? p0 - d * 20f + n : p1 + d * 20f + n;
                p.y = h(p.xz);
                return p;
            }
            int last = ring.Length - 1;
            site.NearbyNets.Add(Street(9000, Out(ring[0], ring[1], true), Out(ring[0], ring[1], false)));
            site.NearbyNets.Add(Street(9001, Out(ring[last], ring[0], true), Out(ring[last], ring[0], false)));
            float xMin = ring.Min(p => p.x), zMin = ring.Min(p => p.z), xMax = ring.Max(p => p.x), zMax = ring.Max(p => p.z);
            var c = new float2(xMin + (xMax - xMin) * 0.36f, zMin + (zMax - zMin) * 0.55f);
            Obstacle Box(float2 centre, float w, float dd, float yaw)
            {
                float2 u = new float2(math.cos(yaw), math.sin(yaw)) * (w * 0.5f), v = new float2(-math.sin(yaw), math.cos(yaw)) * (dd * 0.5f);
                return new Obstacle { A = centre - u - v, B = centre + u - v, C = centre + u + v, D = centre - u + v, Kind = ObstacleKind.Building };
            }
            site.Obstacles.Add(Box(c, 18f, 12f, 0.3f));
            site.Obstacles.Add(Box(c + new float2(34f, 10f), 36f, 20f, 0.3f));
            site.Obstacles.Add(Box(c + new float2(8f, 38f), 12f, 10f, 0.3f));
            return site;
        }

        public static AreaInput SiteInput(SettingsBag settings, string kind, out SiteContext site, float area = 200000f)
        {
            var ring = Ring(area);
            site = Site(ring, kind);
            var h = site.HeightAt;
            return new AreaInput
            {
                Shape = new AreaShape { Ring = ring.Select(p => new float3(p.x, h(p.xz), p.z)).ToArray(), Source = AreaSource.Drawn },
                Site = site, Settings = settings, Seed = 1,
            };
        }

        private static SettingsBag Preset(string id)
        {
            var layer = new RoadsLayer();
            return layer.Presets.Single(p => p.Id == id).Values.Clone();
        }

        private static SettingsBag Pattern(string pattern)
        {
            var s = new RoadsLayer().Defaults();
            s.Values[RoadsIds.Pattern] = pattern;
            return s;
        }

        private static AreaPlan Run(AreaInput input) => new RoadsLayer().Plan(input, CancellationToken.None);

        private static IEnumerable<(string name, SettingsBag s)> Designs()
        {
            foreach (var p in RoadsSchema.PatternValues) yield return ("pattern " + p, Pattern(p));
            foreach (var p in new RoadsLayer().Presets) yield return ("preset " + p.Id, p.Values.Clone());
        }

        private static readonly string[] Kinds = { "flat", "hill", "valley", "steep" };

        /// <summary>(p1-ground) Every preset builds roads on rough land with no main slope (egg-crate hills) and on mountains: the
        /// game runner's hillside preset on its hills built nothing ("NothingToBuild").</summary>
        [Fact]
        public void EveryPresetBuildsOnHillsAndMountains()
        {
            var failures = new List<string>();
            foreach (var p in new RoadsLayer().Presets)
                foreach (var kind in new[] { "eggcrate", "mountains" })
                {
                    var input = SiteInput(p.Values.Clone(), kind, out _);
                    var plan = Run(input);
                    int roads = plan.Nets.Count(n => n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide);
                    _out.WriteLine($"{p.Id}/{kind}: {plan.Nets.Count} nets, {roads} roads");
                    if (roads == 0) failures.Add($"{p.Id}/{kind}: no roads");
                }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // ───────────────────────── connectivity ─────────────────────────

        [Fact]
        public void EveryPatternAndPresetIsOneNetworkJoinedToTheCity()
        {
            var failures = new List<string>();
            foreach (var (name, s) in Designs())
                foreach (var kind in Kinds)
                {
                    var input = SiteInput(s.Clone(), kind, out var site);
                    var plan = Run(input);
                    if (plan.Nets.Count == 0)
                    {
                        // Contour needs a slope; everything else must place roads.
                        if (!(name.Contains("contour") || name.Contains("hillside")) || kind != "flat") failures.Add($"{name}/{kind}: no roads");
                        continue;
                    }
                    var audit = RoadsAudit.Measure(plan, site.HeightAt, 0.1f, true, input.Shape.Ring.Select(p => p.xz).ToArray());
                    _out.WriteLine($"{name}/{kind}: {audit}");
                    if (audit.Components != 1) failures.Add($"{name}/{kind}: {audit.Components} separate pieces");
                    if (audit.Unanchored > 0) failures.Add($"{name}/{kind}: {audit.Unanchored} pieces not joined to the existing streets");
                    if (audit.LooseWalkEnds > 0) failures.Add($"{name}/{kind}: {audit.LooseWalkEnds} walkway ends on nothing");
                    bool caps = s.S(RoadsIds.CapStyle, "asphalt") != "none";
                    if (caps && audit.BareDeadEnds > 0) failures.Add($"{name}/{kind}: {audit.BareDeadEnds} dead ends without a turning circle");
                    if (audit.ShortPieces > 0)
                        failures.Add($"{name}/{kind}: {audit.ShortPieces} road pieces under 6 m " + string.Join(" ", plan.Nets
                            .Where(n => math.distance(n.A.xz, n.D.xz) < 6f).Select(n => $"{n.Role}({n.A.x:0},{n.A.z:0})")));
                    if (plan.HasErrors) failures.Add($"{name}/{kind}: errors " + string.Join(",", plan.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
                }
            foreach (var f in failures) _out.WriteLine("FAIL " + f);
            Assert.Empty(failures);
        }

        [Fact]
        public void IslandsCutOffByObstaclesAreLinkedNotLeftFloating()
        {
            // The playtest: streets around a ruin were removed and left the rest floating. A big building across the
            // middle of a grid cuts it in two; the pieces must come back as one network.
            var ring = Ring(200000f);
            var site = new SiteContext();
            float xMin = ring.Min(p => p.x), xMax = ring.Max(p => p.x), zMin = ring.Min(p => p.z), zMax = ring.Max(p => p.z);
            float zm = (zMin + zMax) * 0.5f;
            site.Obstacles.Add(new Obstacle
            {
                A = new float2(xMin - 10, zm - 14), B = new float2(xMax * 0.7f, zm - 14), C = new float2(xMax * 0.7f, zm + 14), D = new float2(xMin - 10, zm + 14),
                Kind = ObstacleKind.Building,
            });
            var s = Pattern("grid");
            s.Values[RoadsIds.PerimeterRoad] = "off";
            var input = new AreaInput { Shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn }, Site = site, Settings = s, Seed = 1 };
            var plan = Run(input);
            var audit = RoadsAudit.Measure(plan, null, 0.1f, false);
            _out.WriteLine(audit.ToString() + " links " + plan.Facts[RoadsPlanner.FactLinks]);
            Assert.Equal(1, audit.Components);
            Assert.DoesNotContain(plan.Issues, i => i.Key == RoadsPlanner.IssueIslands);
        }

        [Fact]
        public void ConnectivityLinksIslandsOrDropsThemWithAnIssue()
        {
            var net = new RoadNetwork();
            void Road(float x0, float z0, float x1, float z1) => net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(x0, 0, z0), new float3(x1, 0, z1)), NetRole.Street));
            // Main network: an L.
            Road(0, 0, 200, 0); Road(200, 0, 200, 200);
            // Island 1: 60 m off the main line (linkable).
            Road(0, 60, 0, 200);
            // Island 2: far away beyond any link.
            Road(2000, 2000, 2100, 2000);
            net.Weld();
            net.Nodes[RoadGraph.NodeAt(net, new float2(0, 0), 0.1f)].Anchored = true;
            var res = RoadConnectivity.Ensure(net, new RoadConnectivity.Options { MaxLink = 220f });
            RoadGraph.Components(net, RoadGraph.Car, out int count);
            Assert.Equal(1, count);
            Assert.Equal(1, res.Links);
            Assert.True(res.Dropped >= 1);
            Assert.Contains(net.Edges, e => e.Connector && e.Alive);
        }

        [Fact]
        public void DeadEndsBecomeCulDeSacsOrGo()
        {
            var net = new RoadNetwork();
            void Road(float x0, float z0, float x1, float z1) => net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(x0, 0, z0), new float3(x1, 0, z1)), NetRole.Street));
            Road(0, 0, 100, 0); Road(100, 0, 200, 0); Road(200, 0, 300, 0);
            Road(100, 0, 100, 20);    // 20 m stub: goes
            Road(200, 0, 200, 120);   // 120 m dead end: gets a turning circle
            net.Weld();
            net.Nodes[RoadGraph.NodeAt(net, new float2(0, 0), 0.1f)].Anchored = true;
            net.Nodes[RoadGraph.NodeAt(net, new float2(300, 0), 0.1f)].Anchored = true;
            var o = new RoadConnectivity.Options();
            var res = new RoadConnectivity.Result();
            RoadConnectivity.CleanDeadEnds(net, o, res);
            Assert.Equal(1, res.Pruned);
            Assert.Equal(1, res.Capped);
            var tip = net.Edges.Single(e => e.Alive && math.distance(e.Curve.D.xz, new float2(200, 120)) < 0.1f || e.Alive && math.distance(e.Curve.A.xz, new float2(200, 120)) < 0.1f);
            Assert.True(tip.CapAtEnd);
            Assert.True(math.distance(tip.Curve.D.xz, new float2(200, 120)) < 0.1f, "the tip is the curve's end");
        }

        // ───────────────────────── access management ─────────────────────────

        [Fact]
        public void SideStreetsJoinAMainRoadNoCloserThanTheJunctionSpacing()
        {
            // A ladder: an avenue with a side street every 50 m, all side streets joined by a back street, so every
            // junction may go. With 150 m spacing the kept ones are ≥ 150 m apart; the cut ones end in cul-de-sacs.
            var net = new RoadNetwork();
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(-50, 0, 0), new float3(0, 0, 0)), NetRole.Avenue));
            for (int x = 0; x < 600; x += 50)
            {
                net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(x, 0, 0), new float3(x + 50, 0, 0)), NetRole.Avenue));
                net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(x, 0, 0), new float3(x, 0, 160)), NetRole.Street));
                net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(x, 0, 160), new float3(x + 50, 0, 160)), NetRole.Street));
            }
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(600, 0, 0), new float3(600, 0, 160)), NetRole.Street));
            net.Weld();
            net.Nodes[RoadGraph.NodeAt(net, new float2(-50, 0), 0.1f)].Anchored = true;
            var cuts = RoadAccess.Apply(net, 150f, 35f, 40f);
            Assert.NotEmpty(cuts);
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var junctions = new List<float>();
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                bool main = inc[n].Any(i => RoadAccess.IsMain(net.Edges[i])), side = inc[n].Any(i => !RoadAccess.IsMain(net.Edges[i]));
                if (main && side) junctions.Add(net.Nodes[n].Position.x);
            }
            junctions.Sort();
            _out.WriteLine("kept junctions at " + string.Join(", ", junctions));
            for (int i = 1; i < junctions.Count; i++) Assert.True(junctions[i] - junctions[i - 1] >= 150f * 0.92f, $"junctions {junctions[i - 1]} and {junctions[i]}");
            RoadGraph.Components(net, RoadGraph.Car, out int count);
            Assert.Equal(1, count);
            Assert.Contains(net.Edges, e => e.Alive && e.CapAtEnd && e.Role == NetRole.Street);
        }

        [Fact]
        public void JunctionSpacingIsRespectedInPlannedLayouts()
        {
            foreach (var kind in new[] { "flat", "valley" })
            {
                var s = Pattern("grid");
                s.Values[RoadsIds.GridSpacing] = 80.0;
                s.Values[RoadsIds.AvenueRow] = true;
                s.Values[RoadsIds.AvenueRowIndex] = 2.0;
                s.Values[RoadsIds.JunctionSpacing] = 180.0;
                var input = SiteInput(s, kind, out var site);
                var plan = Run(input);
                var audit = RoadsAudit.Measure(plan, null, 0.1f, true);
                _out.WriteLine($"{kind}: {audit}");
                Assert.True(plan.Facts[RoadsPlanner.FactAccessCuts] > 0, "side streets were cut back");
                Assert.True(audit.MinMainJunctionGap >= 180f * 0.9f, $"{kind}: main road junctions {audit.MinMainJunctionGap:0} m apart");
                Assert.Equal(1, audit.Components);
                // 0 = every side street joins (the old behaviour).
                s.Values[RoadsIds.JunctionSpacing] = 0.0;
                var all = Run(SiteInput(s, kind, out _));
                Assert.Equal(0.0, all.Facts[RoadsPlanner.FactAccessCuts]);
            }
        }

        // ───────────────────────── walkways, lanes, gravel ─────────────────────────

        [Fact]
        public void CulDeSacsGetFootpathsToTheNextStreet()
        {
            // Two parallel cul-de-sacs off a street, their bulbs 60 m from the next street ahead: a walk links each.
            var net = new RoadNetwork();
            void Road(float x0, float z0, float x1, float z1, bool cap = false) => net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(x0, 0, z0), new float3(x1, 0, z1)), NetRole.Street, cap));
            Road(0, 0, 100, 0); Road(100, 0, 300, 0); Road(300, 0, 400, 0); Road(400, 0, 400, 300); Road(400, 300, 0, 300); Road(0, 300, 0, 0);
            Road(100, 0, 100, 220, true);
            Road(300, 0, 300, 220, true);
            net.Weld();
            var res = RoadWalkways.Build(net, new RoadWalkways.Options { Density = 1f });
            _out.WriteLine($"links {res.CulDeSacLinks}, cut-throughs {res.CutThroughs}");
            Assert.True(res.CulDeSacLinks >= 2);
            var walks = net.Edges.Where(e => e.Alive && e.Walk).ToList();
            var inc = RoadGraph.Incidence(net, RoadGraph.Any);
            foreach (var w in walks)
            {
                // Both ends are real nodes shared with a road (never free ends, never the bulb tip).
                Assert.True(inc[w.N0].Count >= 2 && inc[w.N1].Count >= 2);
                foreach (var tip in new[] { new float2(100, 220), new float2(300, 220) })
                {
                    Assert.True(math.distance(w.Curve.A.xz, tip) > 10f && math.distance(w.Curve.D.xz, tip) > 10f, "walk leaves at the neck, not the turning circle");
                }
            }
            // The cul-de-sacs keep their turning circles (their tips are still dead ends).
            var deg = RoadGraph.Degrees(net, RoadGraph.Any);
            Assert.Equal(1, deg[RoadGraph.NodeAt(net, new float2(100, 220), 0.1f)]);
            Assert.Equal(1, deg[RoadGraph.NodeAt(net, new float2(300, 220), 0.1f)]);
        }

        [Fact]
        public void WalkwaysNeverCrossARoadAndStayWalkable()
        {
            foreach (var kind in new[] { "flat", "hill" })
            {
                var input = SiteInput(Preset("suburban-loops"), kind, out var site);
                var plan = Run(input);
                var walks = plan.Nets.Where(n => n.Role == NetRole.Pathway).ToList();
                var roads = plan.Nets.Where(n => n.Role != NetRole.Pathway).ToList();
                Assert.NotEmpty(walks);
                foreach (var w in walks)
                {
                    var wc = new RoadCurve(w.A, w.B, w.C, w.D);
                    if (kind != "flat") Assert.True(RoadGraph.MaxGrade(wc, site.HeightAt, 8f, 2.5f) <= RoadWalkways.MaxWalkGrade + 0.005f, "walk too steep");
                    var wp = RoadGraph.Sample(wc, 2f);
                    foreach (var r in roads)
                    {
                        var rp = RoadGraph.Sample(new RoadCurve(r.A, r.B, r.C, r.D), 2f);
                        for (int i = 1; i + 2 < wp.Count; i++)
                            for (int k = 0; k + 1 < rp.Count; k++)
                                Assert.False(Placemaker.Core.Geometry.Poly.SegmentsIntersect(wp[i], wp[i + 1], rp[k], rp[k + 1]) && math.distance(wp[i], w.A.xz) > 2f && math.distance(wp[i + 1], w.D.xz) > 2f,
                                    $"{kind}: walk at {wp[i]} crosses a road");
                    }
                }
            }
        }

        [Fact]
        public void BackLanesRunThroughGridBlocksAndNeverMeetAvenues()
        {
            var s = Preset("downtown");
            var input = SiteInput(s, "flat", out _);
            var plan = Run(input);
            var lanes = plan.Nets.Where(n => n.Role == NetRole.Alley).ToList();
            Assert.NotEmpty(lanes);
            Assert.Equal(RoadsIds.LaneAsset, lanes[0].Control);
            Assert.Equal(RoadRoles.Alley, lanes[0].Asset.Id.Length > 0 ? lanes[0].Asset.Id : lanes[0].Asset.Fallbacks[0]);
            var avenueEnds = plan.Nets.Where(n => n.Role == NetRole.Avenue).SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            foreach (var l in lanes)
                foreach (var p in new[] { l.A.xz, l.D.xz })
                    Assert.DoesNotContain(avenueEnds, q => math.distance(q, p) < 0.1f);
            // Gravel lanes.
            s.Values[RoadsIds.LaneStyle] = "gravel";
            var gravel = Run(SiteInput(s, "flat", out _));
            Assert.Contains(gravel.Nets, n => n.Role == NetRole.Alley && n.Asset.Id == RoadRoles.Gravel);
        }

        [Fact]
        public void GravelTipsAndTrailsInTheRuralVillage()
        {
            var plan = Run(SiteInput(Preset("rural-village"), "flat", out _));
            Assert.Contains(plan.Nets, n => n.Asset.Id == RoadRoles.Gravel);
            // Gravel cul-de-sacs get small turning circles.
            var caps = plan.Objects.Where(o => o.Role == ObjectRole.CulDeSacCap).ToList();
            Assert.NotEmpty(caps);
            var trails = plan.Nets.Where(n => n.Role == NetRole.Pathway && n.Category == AssetCategories.TrailPath).ToList();
            if (trails.Count > 0) Assert.NotEmpty(plan.Paint);   // dirt worn along the trails
            // dirt along the trails, desire paths and worn corners; dry grass on the verges (smarter roads: ground wear)
            Assert.All(plan.Paint, p => Assert.Contains(p.Layer, new[] { TerrainMaterials.Dirt, TerrainMaterials.Dry }));
        }

        // ───────────────────────── zoning ─────────────────────────

        [Fact]
        public void RoadsNeverZoneEvenWithAPreZoneValue()
        {
            // The user (26 Oct): no zoning in Roads, the Zoning category zones. A saved pre-zone value changes nothing.
            var s = Pattern("grid");
            s.Values[RoadsIds.PreZone] = ZoneKinds.ResidentialRow;
            var plan = Run(SiteInput(s, "flat", out _));
            Assert.False(RoadPreZone.Offered);
            Assert.Empty(plan.Zoning);
            Assert.False(plan.Facts.ContainsKey(RoadPreZone.Fact));
            // Default: no zoning at all.
            var none = Run(SiteInput(Pattern("grid"), "flat", out _));
            Assert.Empty(none.Zoning);
            Assert.False(none.Facts.ContainsKey(RoadPreZone.Fact));
        }

        [Fact]
        public void PreZoneAfterTheBuildZonesTheLiveBlocksOfTheNewRoadsOnly()
        {
            var road = new PlannedNet { A = new float3(0, 0, 0), B = new float3(40, 0, 0), C = new float3(80, 0, 0), D = new float3(120, 0, 0), Role = NetRole.Street };
            var mine = ZoneBlockPredictor.Blocks(road.A, road.B, road.C, road.D, 16f);
            var live = new List<RoadPreZone.LiveBlock>();
            // The game split the road at x = 60: two owner edges on the planned curve.
            foreach (var g in mine) live.Add(new RoadPreZone.LiveBlock { Grid = g, OwnerA = road.A, OwnerB = new float3(20, 0, 0), OwnerC = new float3(40, 0, 0), OwnerD = new float3(60, 0, 0) });
            // A city street nearby.
            var city = ZoneBlockPredictor.Blocks(new float3(0, 0, 100), new float3(40, 0, 100), new float3(80, 0, 100), new float3(120, 0, 100), 16f);
            foreach (var g in city) live.Add(new RoadPreZone.LiveBlock { Grid = g, OwnerA = new float3(0, 0, 100), OwnerB = new float3(40, 0, 100), OwnerC = new float3(80, 0, 100), OwnerD = new float3(120, 0, 100) });
            var quads = RoadPreZone.FromLive(new[] { road }, live, "EU Commercial Low");
            Assert.Equal(mine.Count, quads.Count);
            // Each quad covers every cell centre of its block (the Marquee tool takes cells by centre).
            for (int i = 0; i < mine.Count; i++)
                for (int x = 0; x < mine[i].Size.x; x++)
                    for (int y = 0; y < mine[i].Size.y; y++)
                        Assert.True(Placemaker.Core.Geometry.Poly.Contains(quads[i].Corners, ZoneGuard.CellCentre(mine[i], x, y)));
        }

        [Fact]
        public void OldRoadsRecipesWithTheZoningFeatureLoadAsPreZone()
        {
            var roads = new RecipeStep { Layer = "roads", Settings = new RoadsLayer().Defaults() };
            var zoning = new RecipeStep { Layer = EdgeZoningAddOn.LayerId, Target = RegionTags.Edge, Settings = new EdgeZoningAddOn().Defaults() };
            zoning.Settings.Values[EdgeZoningAddOn.KindControl] = ZoneKinds.ResidentialHigh;
            roads.Children.Add(zoning);
            LayerAliases.Normalize(roads);
            Assert.Empty(roads.Children);
            Assert.Equal(ZoneKinds.ResidentialHigh, roads.Settings.S(RoadsIds.PreZone, ""));
            // A disabled feature child goes without zoning anything.
            var off = new RecipeStep { Layer = "roads", Settings = new RoadsLayer().Defaults() };
            off.Children.Add(new RecipeStep { Layer = EdgeZoningAddOn.LayerId, Target = RegionTags.Edge, Enabled = false, Settings = new EdgeZoningAddOn().Defaults() });
            LayerAliases.Normalize(off);
            Assert.Empty(off.Children);
            Assert.Equal(RoadPreZone.None, off.Settings.S(RoadsIds.PreZone, RoadPreZone.None));
            // Idempotent.
            Assert.Equal(0, RoadsLayer.Migrate(roads));
        }

        // ───────────────────────── terrain ─────────────────────────

        [Fact]
        public void SteepRoadsAreReportedWhereTheyAre()
        {
            var plan = Run(SiteInput(Preset("strict-grid"), "steep", out _));
            var steep = plan.Issues.Where(i => i.Key == RoadsPlanner.IssueSteep).ToList();
            Assert.NotEmpty(steep);
            Assert.All(steep, i => Assert.True(i.At.HasValue));
            Assert.All(steep, i => Assert.True(Convert.ToDouble(i.Args[0]) > 10.0));
            Assert.Contains(plan.Stats, st => st.Key == RoadsPlanner.StatSteepest);
        }

        [Fact]
        public void FollowContoursMakesFewerSteepRoadsThanIgnoringTheTerrain()
        {
            foreach (var kind in new[] { "hill", "valley", "steep" })
            {
                foreach (var pattern in new[] { "grid", "organic" })
                {
                    var follow = Pattern(pattern);
                    follow.Values[RoadsIds.TerrainFit] = "follow";
                    var ignore = Pattern(pattern);
                    ignore.Values[RoadsIds.TerrainFit] = "ignore";
                    var a = RoadsAudit.Measure(Run(SiteInput(follow, kind, out var site)), site.HeightAt, 0.1f, true);
                    var b = RoadsAudit.Measure(Run(SiteInput(ignore, kind, out _)), site.HeightAt, 0.1f, true);
                    _out.WriteLine($"{pattern}/{kind}: follow {a.SteepRoads} steep of {a.CarNets} ({a.SteepestGrade:P0}), ignore {b.SteepRoads} of {b.CarNets} ({b.SteepestGrade:P0})");
                    float fa = a.CarNets > 0 ? (float)a.SteepRoads / a.CarNets : 0f, fb = b.CarNets > 0 ? (float)b.SteepRoads / b.CarNets : 0f;
                    Assert.True(fa <= fb + 0.02f, $"{pattern}/{kind}: following the contours made more steep roads");
                    Assert.Equal(1, a.Components);
                }
            }
        }

        [Fact]
        public void ContourGridStreetsLieAlongTheContours()
        {
            var s = Pattern("grid");
            s.Values[RoadsIds.TerrainFit] = "follow";
            var input = SiteInput(s, "valley", out var site);
            var follow = Run(input);
            var ignore = Pattern("grid");
            ignore.Values[RoadsIds.TerrainFit] = "ignore";
            var strict = Run(SiteInput(ignore, "valley", out _));
            // Height change along the streets per metre: lower when the grid follows the contours.
            float Climb(AreaPlan p)
            {
                double rise = 0, run = 0;
                foreach (var n in p.Nets)
                {
                    if (n.Role == NetRole.Pathway) continue;
                    var pts = RoadGraph.Sample(new RoadCurve(n.A, n.B, n.C, n.D), 4f);
                    for (int i = 1; i < pts.Count; i++) { rise += math.abs(site.HeightAt(pts[i]) - site.HeightAt(pts[i - 1])); run += math.distance(pts[i], pts[i - 1]); }
                }
                return (float)(rise / math.max(1.0, run));
            }
            float cf = Climb(follow), cs = Climb(strict);
            _out.WriteLine($"mean climb: follow {cf:P1}, strict {cs:P1}");
            Assert.True(cf < cs * 0.9f, "the contour grid climbs less");
        }

        // ───────────────────────── junction geometry ─────────────────────────

        [Fact]
        public void BendsAtPlainNodesAreRounded()
        {
            var net = new RoadNetwork();
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(0, 0, 0), new float3(100, 0, 0)), NetRole.Street));
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(100, 0, 0), new float3(100, 0, 100)), NetRole.Street));
            net.Weld();
            Assert.Equal(1, RoadTopology.FilletBends(net, r => 8f));
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            for (int n = 0; n < net.Nodes.Count; n++)
            {
                if (inc[n].Count != 2) continue;
                float turn = 180f - RoadGraph.Angle(RoadGraph.Leaving(net.Edges[inc[n][0]], n), RoadGraph.Leaving(net.Edges[inc[n][1]], n));
                Assert.True(turn < 3f, $"a {turn:0}° corner is left at {net.Nodes[n].Position.xz}");
            }
            // The corner node itself is gone: the curve passes inside it at about the bend radius.
            Assert.Equal(-1, RoadGraph.NodeAt(net, new float2(100, 0), 1f));
        }

        [Fact]
        public void SideStreetsMeetOtherRoadsSquarely()
        {
            var net = new RoadNetwork();
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(-100, 0, 0), new float3(0, 0, 0)), NetRole.Street));
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(0, 0, 0), new float3(100, 0, 0)), NetRole.Street));
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(0, 0, 0), new float3(80, 0, -60)), NetRole.CulDeSac));
            net.Weld();
            Assert.Equal(1, RoadTopology.SquareJunctions(net));
            float2 leave = RoadGraph.Leaving(net.Edges[2], RoadGraph.NodeAt(net, float2.zero, 0.1f));
            Assert.True(math.abs(leave.x) < 0.2f, "leaves at ~90° to the through street");
        }

        [Fact]
        public void JunctionsCrowdingEachOtherMerge()
        {
            var net = new RoadNetwork();
            void Road(float x0, float z0, float x1, float z1) => net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(x0, 0, z0), new float3(x1, 0, z1)), NetRole.Street));
            Road(0, 0, 100, 0); Road(100, 0, 110, 0); Road(110, 0, 300, 0);
            Road(100, 0, 100, 100); Road(110, 0, 110, -100);
            net.Weld();
            Assert.Equal(1, RoadTopology.MergeCloseJunctions(net, 18f));
            var deg = RoadGraph.Degrees(net, RoadGraph.Car);
            Assert.Contains(deg, d => d == 4);
        }

        // ───────────────────────── presets ─────────────────────────

        [Fact]
        public void PresetsAreWholeDesigns()
        {
            var layer = new RoadsLayer();
            var ids = layer.Presets.Select(p => p.Id).ToList();
            foreach (var id in new[] { "downtown", "suburb", "village", "hillside", "garden-city", "walkable-grid", "terraced-grid", "suburban-loops",
                "rural-village", "superblocks", "old-town", "strict-grid" })
                Assert.Contains(id, ids);
            var lang = TestKit.Lang();
            foreach (var p in layer.Presets)
            {
                Assert.True(lang.ContainsKey(p.LabelKey), p.LabelKey);
                // Every preset value is a real control with a valid value.
                foreach (var kv in p.Values.Values)
                {
                    var c = layer.Controls.SingleOrDefault(x => x.Id == kv.Key);
                    Assert.NotNull(c);
                    if (c.Kind == ControlKind.Choice) Assert.Contains(c.Options, o => o.Value == kv.Value as string);
                    if (c.Kind == ControlKind.Slider) Assert.InRange(Convert.ToDouble(kv.Value), c.Min, c.Max);
                }
            }
            Assert.Equal("follow", Preset("terraced-grid").S(RoadsIds.TerrainFit, ""));
            Assert.Equal("ignore", Preset("strict-grid").S(RoadsIds.TerrainFit, ""));
            Assert.Equal(RoadMixes.Find("country").Street, Preset("rural-village").S(RoadsIds.StreetAsset, ""));
        }
    }
}
