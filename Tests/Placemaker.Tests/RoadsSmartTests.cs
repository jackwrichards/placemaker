using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Geo = Placemaker.Core.Geometry.Poly;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;
using Xunit;
using static Placemaker.Tests.RoadsTestUtil;

namespace Placemaker.Tests
{
    /// <summary>
    /// Smarter roads: highways never joined, the perimeter hugging the selection, no terracing by default, the grid fitting the
    /// network that is there (spine, lined-up links, hierarchy), a clean no-border mode, the grid giving way to hills and
    /// desire lines, ground wear, and the rating that judges all of it (Tests/Placemaker.RoadLab draws the same cases).
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class RoadsSmartTests
    {
        // ───────────────────────────── sites ─────────────────────────────

        private static float3[] Rect(float w, float d) => Ccw(new[] { new float3(0, 0, 0), new float3(w, 0, 0), new float3(w, 0, d), new float3(0, 0, d) });

        private sealed class Builder
        {
            public readonly SiteContext Site;
            private long _id = 5000;
            public Builder(Func<float2, float> height = null) { Site = new SiteContext { HeightAt = height ?? (_ => 0f) }; }

            public Builder Road(float2 a, float2 b, float width = 16f, bool highway = false)
            {
                var h = Site.HeightAt;
                var A = new float3(a.x, h(a), a.y); var D = new float3(b.x, h(b), b.y);
                Site.NearbyNets.Add(new NearbyNet
                {
                    ExternalId = _id++, A = A, B = math.lerp(A, D, 1f / 3f), C = math.lerp(A, D, 2f / 3f), D = D, Width = width,
                    Category = NetCategory.Road, HasSidewalk = !highway, IsHighway = highway, SpeedLimit = highway ? 110f : 50f, IsOneWay = highway,
                });
                return this;
            }

            public Builder Chain(float width, params float2[] pts)
            {
                for (int i = 0; i + 1 < pts.Length; i++) Road(pts[i], pts[i + 1], width);
                return this;
            }
        }

        /// <summary>Streets on the south and west with side streets every 110 m, a street 55 m north.</summary>
        private static Builder City(float w, float d, Func<float2, float> height = null)
        {
            var b = new Builder(height);
            var xs = new List<float> { -200f };
            for (float x = -90f; x < w + 120f; x += 110f) xs.Add(x);
            b.Chain(16f, xs.Select(x => new float2(x, -40f)).ToArray());
            foreach (var x in xs.Skip(1)) b.Road(new float2(x, -40f), new float2(x, -180f));
            var zs = new List<float> { -40f };
            for (float z = 70f; z < d + 40f; z += 110f) zs.Add(z);
            zs.Add(d + 55f);
            b.Chain(16f, zs.Select(z => new float2(-40f, z)).ToArray());
            foreach (var z in zs.Skip(1).Take(zs.Count - 2)) b.Road(new float2(-40f, z), new float2(-180f, z));
            var ns = new List<float> { -40f };
            for (float x = 70f; x < w + 120f; x += 110f) ns.Add(x);
            b.Chain(16f, ns.Select(x => new float2(x, d + 55f)).ToArray());
            foreach (var x in ns.Skip(1)) b.Road(new float2(x, d + 55f), new float2(x, d + 190f));
            return b;
        }

        private static AreaPlan Plan(float3[] ring, SiteContext site, params (string id, object value)[] values) =>
            Run(Input(ring, AreaSource.Drawn, Settings(values), site));

        private static RoadsRating Rate(AreaPlan plan, float3[] ring, SiteContext site, params (string id, object value)[] values)
        {
            var input = Input(ring, AreaSource.Drawn, Settings(values), site);
            return RoadsPlanner.Rate(plan, input, input.Settings, new RoadsLayer().Controls);
        }

        private static List<float2> Line(NearbyNet n)
        {
            var c = new RoadCurve(n.A, n.B, n.C, n.D);
            var pts = new List<float3>();
            c.Flatten(pts, null, 4f, 4f);
            return pts.Select(p => p.xz).ToList();
        }

        private static bool Crosses(PlannedNet n, List<float2> road)
        {
            var c = new RoadCurve(n.A, n.B, n.C, n.D);
            var pts = new List<float3>();
            c.Flatten(pts, null, 3f, 4f);
            for (int i = 0; i + 1 < pts.Count; i++)
                for (int k = 0; k + 1 < road.Count; k++)
                    if (Geo.SegmentsIntersect(pts[i].xz, pts[i + 1].xz, road[k], road[k + 1])) return true;
            return false;
        }

        private static double Fact(AreaPlan p, string key) => p.Facts.TryGetValue(key, out var v) ? v : 0;

        // ───────────────────────────── connections ─────────────────────────────

        [Fact]
        public void NeverLinksToAHighwayOrRampByDefault()
        {
            var ring = Rect(500, 360);
            var b = new Builder();
            b.Road(new float2(-300, 430), new float2(800, 430), 15f, highway: true);                 // carriageway 70 m north
            b.Road(new float2(260, 430), new float2(560, 380), 8f, highway: true);                    // a ramp down
            b.Chain(16f, new float2(-200, -40), new float2(130, -40), new float2(350, -40), new float2(700, -40));
            var plan = Plan(ring, b.Site);
            var highways = new HashSet<long>(b.Site.NearbyNets.Where(n => n.IsHighway).Select(n => n.ExternalId));
            Assert.NotEmpty(plan.Nets);
            Assert.DoesNotContain(plan.Nets, n => highways.Contains(n.StartAttach) || highways.Contains(n.EndAttach));
            foreach (var hw in b.Site.NearbyNets.Where(n => n.IsHighway))
                Assert.DoesNotContain(plan.Nets, n => Crosses(n, Line(hw)));
            var r = Rate(plan, ring, b.Site);
            Assert.Equal(0, r.HighwayLinks);
            Assert.True(r.CityLinks >= 1, "still joins the street south");
        }

        [Fact]
        public void AHighwayThroughTheAreaIsABarrier()
        {
            var ring = Rect(500, 400);
            var b = new Builder();
            b.Road(new float2(-200, 200), new float2(700, 200), 20f, highway: true);
            b.Chain(16f, new float2(-200, -40), new float2(700, -40));
            var plan = Plan(ring, b.Site);
            var hwy = Line(b.Site.NearbyNets[0]);
            Assert.NotEmpty(plan.Nets);
            Assert.DoesNotContain(plan.Nets, n => Crosses(n, hwy));
            Assert.DoesNotContain(plan.Nets, n => n.StartAttach == b.Site.NearbyNets[0].ExternalId || n.EndAttach == b.Site.NearbyNets[0].ExternalId);
            // "any road" lets the player join it on purpose
            var any = Plan(ring, b.Site, (RoadsIds.ConnectTo, ConnectTo.Any));
            Assert.Contains(any.Nets, n => n.StartAttach == b.Site.NearbyNets[0].ExternalId || n.EndAttach == b.Site.NearbyNets[0].ExternalId);
        }

        [Fact]
        public void LinksContinueTheGridStreetsOntoTheCitysJunctions()
        {
            var ring = Rect(500, 360);
            var b = City(500, 360);
            var plan = Plan(ring, b.Site);
            var r = Rate(plan, ring, b.Site);
            Assert.True(r.CityLinks >= 4, "links " + r.CityLinks);
            Assert.Equal(r.CityLinks, r.LinksAligned);
            Assert.True(r.LinksOnJunction >= 3, "on junction " + r.LinksOnJunction);
            Assert.Equal(0, r.SkewLinks);
            Assert.True(Fact(plan, RoadsPlanner.FactStubsAligned) >= 3);
        }

        // ───────────────────────────── perimeter, terrain ─────────────────────────────

        [Fact]
        public void ThePerimeterHugsTheSelection()
        {
            var ring = Rect(500, 360);
            var site = new SiteContext();
            var plan = Plan(ring, site);
            var r = Rate(plan, ring, site);
            Assert.True(r.CornerMaxGap <= 7.5f, "corner gap " + r.CornerMaxGap);
            Assert.True(r.OutlineMeanGap < 1f, "mean gap " + r.OutlineMeanGap);
            Assert.Equal(RoadsSchema.TightCorner, Convert.ToSingle(new RoadsLayer().Defaults().Values[RoadsIds.PerimeterCornerRadius]));
            // the winding styles round more (and say so)
            var village = new RoadsLayer().Presets.Single(p => p.Id == "village").Values;
            Assert.Equal((double)RoadsSchema.SoftCorner, village.Num(RoadsIds.PerimeterCornerRadius, 0));
        }

        [Fact]
        public void NoTerracingOrFlatteningByDefault()
        {
            var layer = new RoadsLayer();
            Assert.Equal("natural", layer.Defaults().S(RoadsIds.LandShape, ""));
            Assert.True(layer.Defaults().B(RoadsIds.FollowTerrain, false), "roads follow the ground");
            foreach (var p in layer.Presets)
                Assert.Equal(p.Values.S(RoadsIds.Pattern, "") == "terraces" ? "terrace" : "natural", p.Values.S(RoadsIds.LandShape, ""));
            var ring = Rect(500, 400);
            var site = new SiteContext { HeightAt = q => 0.015f * q.x + 34f * math.exp(-math.lengthsq(q - new float2(290, 208)) / (2f * 135f * 135f)) };
            var plan = Plan(ring, site);
            Assert.NotEmpty(plan.Nets);
            Assert.Empty(plan.Terraform);
            Assert.Null(plan.HeightAfter);
        }

        // ───────────────────────────── existing roads inside ─────────────────────────────

        private static Builder AvenueThrough(out float2 dir)
        {
            var b = new Builder(q => 0.01f * q.x);
            float2 mid = new float2(280, 210);
            dir = new float2(math.cos(math.radians(20f)), math.sin(math.radians(20f)));
            float2 n = new float2(-dir.y, dir.x);
            var d = dir;
            b.Chain(24f, new[] { -560f, -400f, 70f, 400f, 560f }.Select(t => mid + d * t).ToArray());
            b.Road(mid + d * 70f, mid + d * 70f + n * 60f).Road(mid + d * 70f, mid + d * 70f - n * 45f);
            b.Chain(16f, new float2(-200, -40), new float2(60, -40), new float2(300, -40), new float2(700, -40));
            return b;
        }

        [Fact]
        public void TheGridTurnsToAStraightAvenueAndMeetsItSquare()
        {
            var ring = Rect(560, 420);
            var b = AvenueThrough(out float2 dir);
            var plan = Plan(ring, b.Site);
            Assert.Equal(2, Fact(plan, RoadsPlanner.FactLayoutFrame));   // on the spine
            var avenue = new HashSet<long>(b.Site.NearbyNets.Where(n => n.Width > 20f).Select(n => n.ExternalId));
            int met = 0;
            foreach (var n in plan.Nets)
            {
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                if (avenue.Contains(n.StartAttach)) { met++; Assert.True(math.abs(math.dot(c.TangentXZ(0f), dir)) < 0.35f, "meets the avenue square"); }
                if (avenue.Contains(n.EndAttach)) { met++; Assert.True(math.abs(math.dot(c.TangentXZ(1f), dir)) < 0.35f, "meets the avenue square"); }
            }
            Assert.True(met >= 4, "streets join the avenue: " + met);
            var r = Rate(plan, ring, b.Site);
            Assert.Equal(0, r.DoubledPieces);
            Assert.Equal(0, r.Crossings);
            Assert.Equal(0, r.SpineCrowding);
        }

        [Fact]
        public void ASmallRoadThroughTheAreaStaysSmall()
        {
            var ring = Rect(500, 360);
            var b = new Builder();
            b.Chain(16f, new float2(-200, 70), new float2(150, 120), new float2(350, 250), new float2(700, 300));
            b.Chain(16f, new float2(-200, -40), new float2(700, -40));
            foreach (var preset in new[] { "downtown", null })
            {
                var settings = preset == null ? new RoadsLayer().Defaults() : new RoadsLayer().Presets.Single(p => p.Id == preset).Values.Clone();
                var input = Input(ring, AreaSource.Drawn, settings, b.Site);
                var plan = Run(input);
                var r = RoadsPlanner.Rate(plan, input, settings, new RoadsLayer().Controls);
                Assert.Equal(0, r.HierarchyBreaks);
                Assert.Equal(0, r.Crossings);
                Assert.Equal(0, r.DoubledPieces);
                Assert.Equal(0, r.AcuteJunctions);
            }
        }

        // ───────────────────────────── no border, character, wear ─────────────────────────────

        [Fact]
        public void NoBorderLeavesNoStreetsRunningOutToTheEdge()
        {
            var ring = Rect(500, 360);
            var site = new SiteContext();
            var plan = Plan(ring, site, (RoadsIds.PerimeterRoad, "off"));
            Assert.NotEmpty(plan.Nets);
            Assert.DoesNotContain(plan.Nets, n => n.Role == NetRole.PerimeterRoad);
            var r = Rate(plan, ring, site, (RoadsIds.PerimeterRoad, "off"));
            Assert.Equal(0, r.BareDeadEnds);
            var outline = Ring2(ring);
            foreach (var n in plan.Nets.Where(n => n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide))
                foreach (var p in new[] { n.A.xz, n.D.xz })
                    Assert.True(Geo.DistanceToBoundary(outline, p) > 10f, "a street end on the edge at " + p);
        }

        [Fact]
        public void TheGridGivesWayToAHillUnlessStrict()
        {
            var ring = Rect(700, 560);
            Func<float2, float> hill = q => 0.015f * q.x + 45f * math.exp(-math.lengthsq(q - new float2(380, 300)) / (2f * 150f * 150f));
            var site = City(700, 560, hill).Site;
            var auto = Plan(ring, site);
            Assert.True(Fact(auto, RoadsPlanner.FactStairs) + Fact(auto, RoadsPlanner.FactHillParks) + Fact(auto, RoadsPlanner.FactBent) > 0, "the hill shapes the grid");
            var strict = Plan(ring, site, (RoadsIds.GridCharacter, "strict"));
            Assert.Equal(0, Fact(strict, RoadsPlanner.FactStairs));
            Assert.Equal(0, Fact(strict, RoadsPlanner.FactHillParks));
            Assert.Equal(0, Fact(strict, RoadsPlanner.FactBent));
            var ra = Rate(auto, ring, site);
            var rs = Rate(strict, ring, site, (RoadsIds.GridCharacter, "strict"));
            Assert.True(ra.SteepShare <= rs.SteepShare, $"fewer steep roads: {ra.SteepShare} vs {rs.SteepShare}");
        }

        [Fact]
        public void LivelyLaysBrickBlocksAndADiagonalOnlyWhereADesireLineAsks()
        {
            var ring = Rect(640, 480);
            var open = new SiteContext();
            var lively = Plan(ring, open, (RoadsIds.GridCharacter, "lively"));
            Assert.True(Fact(lively, RoadsPlanner.FactBrick) > 0);
            Assert.Equal(0, Fact(lively, RoadsPlanner.FactDiagonal));   // nothing outside asks for one
            Assert.Contains(lively.Regions, g => g.Tags.Contains(RegionTags.Core) && g.Tags.Contains(RegionTags.Green));   // the square
            var strict = Plan(ring, open, (RoadsIds.GridCharacter, "strict"));
            Assert.Equal(0, Fact(strict, RoadsPlanner.FactBrick));

            // town to station: two streets meet the area at opposite corners
            var b = new Builder();
            b.Chain(16f, new float2(-260, -170), new float2(-140, -95), new float2(-40, -30));
            b.Road(new float2(-140, -95), new float2(-140, -230));
            b.Chain(16f, new float2(900, 680), new float2(780, 600), new float2(680, 530));
            b.Road(new float2(780, 600), new float2(780, 760));
            var withLine = Plan(ring, b.Site, (RoadsIds.GridCharacter, "lively"));
            Assert.Equal(1, Fact(withLine, RoadsPlanner.FactDiagonal));
            var r = Rate(withLine, ring, b.Site, (RoadsIds.GridCharacter, "lively"));
            Assert.True(r.MinJunctionAngle >= 40f, "no knife-edge crossings: " + r.MinJunctionAngle);
            Assert.Equal(0, Fact(Plan(ring, b.Site, (RoadsIds.GridCharacter, "strict")), RoadsPlanner.FactDiagonal));
        }

        [Fact]
        public void GroundWearPaintsAndCanBeTurnedOff()
        {
            var ring = Rect(500, 360);
            var b = City(500, 360);
            var plan = Plan(ring, b.Site, (RoadsIds.Pattern, "tree"));
            Assert.NotEmpty(plan.Paint);
            Assert.Contains(plan.Issues, i => i.Key == TerrainPaint.PermanentKey);
            Assert.True(Fact(plan, RoadsPlanner.FactWornCorners) > 0);
            var off = Plan(ring, b.Site, (RoadsIds.Pattern, "tree"), (RoadsIds.GroundWear, "off"));
            Assert.Empty(off.Paint);
        }

        [Fact]
        public void PlansAreDeterministic()
        {
            var ring = Rect(560, 420);
            var b = AvenueThrough(out _);
            var p1 = Plan(ring, b.Site);
            var p2 = Plan(ring, b.Site);
            Assert.Equal(p1.Nets.Count, p2.Nets.Count);
            for (int i = 0; i < p1.Nets.Count; i++)
            {
                Assert.Equal(p1.Nets[i].A, p2.Nets[i].A);
                Assert.Equal(p1.Nets[i].D, p2.Nets[i].D);
            }
            Assert.Equal(p1.Paint.Count, p2.Paint.Count);
        }

        // ───────────────────────────── the hard rules, everywhere ─────────────────────────────

        /// <summary>The designs every road-lab site is planned with (as the lab draws them).</summary>
        private static readonly string[] Designs =
        {
            "default", "default+gridCharacter=strict", "default+gridCharacter=lively", "default+perimeterRoad=off",
            "downtown", "village", "suburb", "walkable-grid", "terraced-grid",
        };

        private static SettingsBag Design(string variant)
        {
            var layer = new RoadsLayer();
            var parts = variant.Split('+');
            var s = parts[0] == "default" ? layer.Defaults() : layer.Presets.Single(p => p.Id == parts[0]).Values.Clone();
            foreach (var kv in parts.Skip(1))
            {
                var bits = kv.Split('=');
                s.Values[bits[0]] = bits[1];
            }
            return s;
        }

        /// <summary>
        /// The user's hard rules on every road-lab site × design: no two junctions closer than 45 m, no block too small or
        /// thin to build on, no knife-edge junction, no kink (a sharp bend with no curve), no hook, no stub ending right
        /// beside a road it does not join, no link too short to hold a building, never a highway joined or crossed, never an
        /// existing road (or one of our own) crossed without a junction, never a dead end without its turning circle.
        /// </summary>
        [Fact]
        public void NoCrowdedJunctionsSliversOrKnifeEdgesAnywhere()
        {
            var failures = new List<string>();
            var layer = new RoadsLayer();
            foreach (var id in Placemaker.RoadLab.Cases.All)
                foreach (var design in Designs)
                {
                    var lc = Placemaker.RoadLab.Cases.Make(id);
                    var settings = Design(design);
                    var site = new SiteContext { HeightAt = lc.Height, NearbyNets = lc.Nets, Obstacles = lc.Obstacles };
                    var input = new AreaInput { Shape = new AreaShape { Ring = lc.Ring, Source = lc.Source }, Site = site, Settings = settings, Seed = 1 };
                    var plan = layer.Plan(input, System.Threading.CancellationToken.None);
                    if (plan.Nets.Count == 0) { failures.Add($"{id}~{design}: no roads"); continue; }
                    var r = RoadsPlanner.Rate(plan, input, settings, layer.Controls);
                    string at(string kind) => string.Join(" ", r.Marks.Where(m => m.Kind == kind).Select(m => $"({m.At.x:0},{m.At.y:0})"));
                    if (r.CrowdedJunctions > 0) failures.Add($"{id}~{design}: {r.CrowdedJunctions} junctions closer than 45 m {at(RoadsRating.MarkCrowded)}");
                    if (r.Kinks > 0) failures.Add($"{id}~{design}: {r.Kinks} kinks {at(RoadsRating.MarkKink)}");
                    if (r.Hooks > 0) failures.Add($"{id}~{design}: {r.Hooks} hooks {at(RoadsRating.MarkHook)}");
                    if (r.StrayStubs > 0) failures.Add($"{id}~{design}: {r.StrayStubs} stubs ending beside a road {at(RoadsRating.MarkStub)}");
                    if (r.ShortLinks > 0) failures.Add($"{id}~{design}: {r.ShortLinks} links too short {at(RoadsRating.MarkShort)}");
                    if (r.Slivers > 0) failures.Add($"{id}~{design}: {r.Slivers} sliver blocks {at(RoadsRating.MarkSliver)}");
                    if (r.AcuteJunctions > 0) failures.Add($"{id}~{design}: {r.AcuteJunctions} knife-edge junctions {at(RoadsRating.MarkAcute)}");
                    if (r.HighwayLinks + r.HighwayCrossings > 0) failures.Add($"{id}~{design}: highway joined or crossed");
                    if (r.Crossings > 0) failures.Add($"{id}~{design}: {r.Crossings} existing roads crossed without a junction");
                    if (r.SelfCrossings > 0) failures.Add($"{id}~{design}: {r.SelfCrossings} of our streets crossing without a junction {at(RoadsRating.MarkSelfCross)}");
                    if (r.BareDeadEnds > 0) failures.Add($"{id}~{design}: {r.BareDeadEnds} dead ends without a turning circle {at(RoadsRating.MarkDeadEnd)}");
                }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // ───────────────────────────── the rating ─────────────────────────────

        [Fact]
        public void TheRatingCatchesAHighwayLinkAndAKnifeEdgeJunction()
        {
            var site = new Builder().Road(new float2(-100, 0), new float2(300, 0), 15f, highway: true).Site;
            var hwId = site.NearbyNets[0].ExternalId;
            var plan = new AreaPlan();
            plan.Nets.Add(new PlannedNet { A = new float3(100, 0, 0), B = new float3(100, 0, 30), C = new float3(100, 0, 60), D = new float3(100, 0, 90), Role = NetRole.Street, StartAttach = hwId, StartAttachT = 0.5f });
            var r = RoadsRating.Measure(plan, new[] { new float2(0, 10), new float2(200, 10), new float2(200, 200), new float2(0, 200) }, site, new RoadsRating.Options { Perimeter = false });
            Assert.Equal(1, r.HighwayLinks);
            Assert.True(r.Total <= 50f);

            var sharp = new AreaPlan();
            float3 o = new float3(0, 0, 0);
            foreach (var d in new[] { new float3(100, 0, 0), new float3(-100, 0, 0), new float3(100, 0, 30) })
                sharp.Nets.Add(new PlannedNet { A = o, B = math.lerp(o, d, 1f / 3f), C = math.lerp(o, d, 2f / 3f), D = d, Role = NetRole.Street });
            var rs = RoadsRating.Measure(sharp, null, new SiteContext(), new RoadsRating.Options { Perimeter = false });
            Assert.Equal(1, rs.AcuteJunctions);
            Assert.True(rs.Junctions < 100f);
        }
    }
}
