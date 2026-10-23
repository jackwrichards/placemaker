// Parking layer behaviour: contract, schema, readiness, markers, entrances, nets, objects,
// surfaces, determinism, reentrancy, cancellation and bad input.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class ParkingTests
    {
        private static readonly float3[] Rect = ParkingKit.Rect(160, 110);
        private static List<EdgeMarker> OneEntrance(float3[] ring, int edge = 0, double along = 60, string kind = "entrance") =>
            new List<EdgeMarker> { ParkingKit.Marker(ring, edge, along, kind) };

        private static SettingsBag With(params (string Key, object Value)[] values)
        {
            var b = new ParkingLayer().Defaults();
            foreach (var v in values) b.Values[v.Key] = v.Value;
            return b;
        }

        private static void AssertClean(ParkingRun r, string what = "")
        {
            var q = ParkingKit.Check(r);
            Assert.True(q.Problems.Count == 0, what + ": " + string.Join("; ", q.Problems.Take(5)) + " | " + q);
            Assert.False(r.Plan.HasErrors, what + ": " + string.Join(",", r.Plan.Issues.Select(i => i.Key)));
        }

        // ───────────── contract ─────────────

        [Fact]
        public void RegisteredAsFillLayer()
        {
            var g = GeneratorRegistry.Get("parking");
            Assert.NotNull(g);
            Assert.Equal(LayerKind.Fill, g.Kind);
            Assert.Contains(RegionTags.Lot, g.Emits);
            Assert.Contains(RegionTags.Green, g.Emits);
        }

        [Fact]
        public void ControlIdsAreUniqueAndDefaultsCoverThem()
        {
            var g = new ParkingLayer();
            var ids = g.Controls.Select(c => c.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            var d = g.Defaults();
            foreach (var c in g.Controls.Where(c => c.Kind != ControlKind.Markers)) Assert.True(d.Values.ContainsKey(c.Id), c.Id);
            foreach (var c in g.Controls.Where(c => c.Kind == ControlKind.Slider))
            {
                double v = d.Num(c.Id, double.NaN);
                Assert.InRange(v, c.Min, c.Max);
            }
            Assert.Contains(g.Controls, c => c.Kind == ControlKind.Markers && c.MarkerKinds.SequenceEqual(new[] { "entrance", "entry", "exit", "alley", "alleyIn", "alleyOut", "footpath" }));
            Assert.Equal(4, g.Controls.Count(c => c.Tier == ControlTier.Essential));
            Assert.Equal("rowAngleMode=fixed", g.Controls.Single(c => c.Id == "rowAngle").VisibleWhen);
        }

        [Fact]
        public void EveryKeyHasEnglishText()
        {
            var root = Path.GetFullPath(Path.Combine(ParkingKit.FixtureDir(), "..", "..", "..", ".."));
            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "Lang", "en-US.parking.json")));
            // s21: the shared decal dressing controls live in en-US.dress.json.
            foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "Lang", "en-US.dress.json")))) en[kv.Key] = kv.Value;
            var g = new ParkingLayer();
            var keys = new List<string> { g.NameKey };
            foreach (var c in g.Controls)
            {
                keys.Add(c.LabelKey); keys.Add(c.TooltipKey);
                foreach (var o in c.Options) { keys.Add(o.LabelKey); keys.Add(o.TooltipKey); }
            }
            keys.AddRange(g.Presets.Select(p => p.LabelKey));
            // every literal key in the layer's sources
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Core", "Layers", "Parking"), "*.cs"))
            {
                var src = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(src, "\"(Placemaker\\.Parking\\.[A-Za-z.]+)\"")) keys.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(src, "K \\+ \"([A-Za-z.]+)\"")) keys.Add("Placemaker.Parking." + m.Groups[1].Value);
            }
            var missing = keys.Distinct().Where(k => !k.EndsWith(".") && !en.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, "missing: " + string.Join(", ", missing));
            // other locales only translate keys that exist in English
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Lang"), "*.parking.json"))
            {
                var loc = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                Assert.All(loc.Keys, k => Assert.True(en.ContainsKey(k), Path.GetFileName(file) + ": " + k));
            }
        }

        [Fact]
        public void ShippedRecipesParseAndCompose()
        {
            var root = Path.GetFullPath(Path.Combine(ParkingKit.FixtureDir(), "..", "..", "..", ".."));
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Recipes"), "parking*.json"))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                var step = r.GetProperty("root");
                Assert.Equal("parking", step.GetProperty("layer").GetString());
                var recipe = new Recipe { Id = r.GetProperty("id").GetString(), NameKey = r.GetProperty("nameKey").GetString(), Root = new RecipeStep { Layer = "parking", Seed = 3 } };
                foreach (var p in step.GetProperty("settings").EnumerateObject())
                    recipe.Root.Settings.Values[p.Name] = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDouble()
                        : p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : (object)p.Value.GetBoolean();
                recipe.Root.Markers.AddRange(OneEntrance(Rect));
                var composed = Composer.Compose(recipe, new AreaShape { Ring = Rect }, new SiteContext(), CancellationToken.None);
                Assert.False(composed.HasErrors, Path.GetFileName(file) + ": " + string.Join(",", composed.AllIssues().Select(i => i.Key)));
                Assert.True(composed.Layers[0].Facts["capacity"] > 100, Path.GetFileName(file));
            }
        }

        // ───────────── readiness & markers ─────────────

        [Fact]
        public void ReadinessNeedsTwoWayOrEntryAndExit()
        {
            var g = new ParkingLayer();
            AreaInput In(params string[] kinds) => new AreaInput { Markers = kinds.Select(k => new EdgeMarker { Kind = k }).ToList() };
            var none = g.CheckReady(In());
            Assert.False(none.Ready);
            Assert.Equal("Placemaker.Parking.Step.PlaceEntrance", none.NextStepKey);
            Assert.Equal("Placemaker.Parking.Reason.NeedsEntrance", none.ReasonKey);
            Assert.Equal("Placemaker.Parking.Reason.EntryWithoutExit", g.CheckReady(In("entry")).ReasonKey);
            Assert.Equal("Placemaker.Parking.Reason.ExitWithoutEntry", g.CheckReady(In("exit")).ReasonKey);
            Assert.True(g.CheckReady(In("entry", "exit")).Ready);
            Assert.True(g.CheckReady(In("entrance")).Ready);
            Assert.False(g.CheckReady(In("bogus")).Ready);
        }

        private static NearbyNet Road(long id, double ax, double az, double bx, double bz, float width = 12, bool oneWay = false)
        {
            var a = new float3((float)ax, 0, (float)az); var b = new float3((float)bx, 0, (float)bz);
            return new NearbyNet { ExternalId = id, A = a, B = math.lerp(a, b, 1f / 3), C = math.lerp(a, b, 2f / 3), D = b, Width = width, Category = NetCategory.Road, IsOneWay = oneWay };
        }

        [Fact]
        public void SuggestedEntrancesSitOnTheAdjacentRoadAndMakeItReady()
        {
            var g = new ParkingLayer();
            var site = new SiteContext();
            site.NearbyNets.Add(Road(42, -100, -10, 260, -10));            // street along the bottom edge (z = 0)
            site.NearbyNets.Add(Road(7, 175, -50, 175, 160, 30));          // wide arterial along the right edge
            var input = new AreaInput { Shape = new AreaShape { Ring = Rect }, Site = site, Settings = g.Defaults() };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var m = g.SuggestMarkers(input);
            sw.Stop();
            Assert.NotEmpty(m);
            Assert.All(m, x => Assert.Equal("entrance", x.Kind));
            Assert.Equal(0, m[0].RingSegment);                             // the street, not the arterial
            Assert.InRange(m[0].Position.z, -0.01f, 0.01f);
            Assert.True(m.Count == 2, "a 1.8 ha lot gets a second entrance");
            Assert.True(math.distance(m[0].Position, m[1].Position) >= 40);
            input.Markers = m;
            Assert.True(g.CheckReady(input).Ready);
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(50), sw.ElapsedMilliseconds + " ms");

            // the suggestions build without warnings about roads, connected to the street
            var plan = g.Plan(input, CancellationToken.None);
            Assert.DoesNotContain(plan.Issues, i => i.Key.EndsWith("EntranceNoRoad"));
            Assert.Equal(2, plan.Nets.Count(n => n.Role == NetRole.DriveConnector));
        }

        [Fact]
        public void NoRoadsMeansNoSuggestionsButPlanStillPreviews()
        {
            var g = new ParkingLayer();
            var input = new AreaInput { Shape = new AreaShape { Ring = Rect }, Settings = g.Defaults() };
            Assert.Empty(g.SuggestMarkers(input));
            var plan = g.Plan(input, CancellationToken.None);
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Parking.Issue.NoEntrance");
            Assert.True(plan.Facts["capacity"] > 100);
        }

        [Fact]
        public void PlanNeverInventsEntrances()
        {
            // Suggestions are applied by the tool as real markers; the plan only builds what is placed.
            var site = new SiteContext();
            site.NearbyNets.Add(Road(42, -100, -10, 260, -10));
            var r = ParkingKit.Run(Rect, null, null, site);
            Assert.Contains(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.NoEntrance");
            Assert.DoesNotContain(r.Plan.Nets, n => n.Role == NetRole.DriveConnector);
            Assert.True(r.Plan.Facts["capacity"] > 100, "the preview still shows the lot");
            AssertClean(r);
        }

        // ───────────── entrances & nets ─────────────

        [Fact]
        public void ConnectEntranceRunsToTheKerbForLocalConnect()
        {
            var site = new SiteContext();
            site.NearbyNets.Add(Road(42, -100, -10, 260, -10, 12));      // centre 10 m out, kerb at 4 m
            var r = ParkingKit.Run(Rect, null, OneEntrance(Rect), site);
            var con = r.Plan.Nets.Single(n => n.Role == NetRole.DriveConnector);
            Assert.InRange(con.A.z, -4.05f, -3.95f);                      // starts at the kerb
            Assert.Equal(-1, con.StartAttach);
            Assert.DoesNotContain(r.Plan.Nets, n => n.Role == NetRole.Alley);
        }

        [Fact]
        public void AlleyEntranceAttachesToTheRoad()
        {
            var site = new SiteContext();
            site.NearbyNets.Add(Road(42, -100, -10, 260, -10));
            var r = ParkingKit.Run(Rect, With(("entranceStyle", "alley")), OneEntrance(Rect, 0, 60), site);
            var alley = r.Plan.Nets.Single(n => n.Role == NetRole.Alley);
            Assert.Equal(42, alley.StartAttach);
            Assert.InRange(alley.StartAttachT, 0.4f, 0.5f);              // x = 60 on a road from -100 to 260
            Assert.InRange(alley.A.z, -10.01f, -9.99f);
            Assert.InRange(alley.D.z, -0.01f, 0.01f);
            var con = r.Plan.Nets.Single(n => n.Role == NetRole.DriveConnector);
            Assert.True(math.distance(con.A, alley.D) < 1e-3f, "the alley ends where the connector starts");
            AssertClean(r);
        }

        [Fact]
        public void OneWayEntrancesPointTheRightWayAndGetArrowsAndFootpaths()
        {
            var markers = new List<EdgeMarker> { ParkingKit.Marker(Rect, 0, 40, "entry"), ParkingKit.Marker(Rect, 0, 120, "exit") };
            var r = ParkingKit.Run(Rect, null, markers);
            AssertClean(r);
            var cons = r.Plan.Nets.Where(n => n.Role == NetRole.DriveConnector).ToList();
            Assert.Equal(2, cons.Count);
            var entry = cons.Single(n => math.min(n.A.x, n.D.x) < 80);
            var exit = cons.Single(n => math.min(n.A.x, n.D.x) > 80);
            Assert.True(entry.A.z < entry.D.z, "entry drives into the lot");
            Assert.True(exit.A.z > exit.D.z, "exit drives out of the lot");
            Assert.Equal("Invisible Road Path - 1xOneway", entry.Asset.Id);
            Assert.Equal(2, r.Plan.Objects.Count(o => o.Role == ObjectRole.Arrow));
            Assert.Equal(2, r.Plan.Nets.Count(n => n.Role == NetRole.Pathway));
        }

        [Fact]
        public void NetsShareEndpointsAtEveryJunction()
        {
            var r = ParkingKit.Run(Rect, null, OneEntrance(Rect));
            var nets = r.Plan.Nets.Where(n => n.Role == NetRole.DriveAisle || n.Role == NetRole.DriveConnector).ToList();
            Assert.True(nets.Count > 10);
            var ends = nets.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            foreach (var n in nets)
            {
                Assert.True(math.distance(n.A.xz, n.D.xz) >= 0.5f, "no sub-metre course");
                foreach (var p in ends)
                {
                    // an endpoint lying on another course must be one of its ends (T-junctions are split)
                    var d = PGeo.DistPointSeg(new double2(p.x, p.y), new double2(n.A.x, n.A.z), new double2(n.D.x, n.D.z));
                    if (d < 1e-3)
                        Assert.True(math.distance(p, n.A.xz) < 1e-3f || math.distance(p, n.D.xz) < 1e-3f, $"junction at {p} not split");
                }
            }
            Assert.Contains(nets, n => n.Asset.Id == "Invisible Road Path - 2xTwoway");
            Assert.Contains(nets, n => n.Asset.Id == "Invisible Car Path - 1xTwoway");
        }

        // ───────────── objects, surfaces, regions, facts ─────────────

        [Fact]
        public void PlanCarriesBaysSurfacesLotRegionsStatsAndFacts()
        {
            var r = ParkingKit.Run(Rect, null, OneEntrance(Rect));
            AssertClean(r, "rect");
            var p = r.Plan;
            int bays = p.Objects.Count(o => o.Role == ObjectRole.ParkingBay);
            Assert.Equal(bays, (int)p.Facts["capacity"]);
            Assert.Equal(p.Objects.Count(o => o.Role == ObjectRole.ParkingBay && o.Asset.Id == "ParkingLotElectricDecal01"), (int)p.Facts["evCapacity"]);
            Assert.Equal(p.Objects.Count(o => o.Role == ObjectRole.ParkingBay && o.Asset.Id == "ParkingLotDisabledDecal01"), (int)p.Facts["disabledCapacity"]);
            Assert.True(p.Facts["evCapacity"] >= 2 && p.Facts["disabledCapacity"] >= 3);
            Assert.Equal(p.Facts["evCapacity"] / 2, p.Objects.Count(o => o.Role == ObjectRole.Charger));
            Assert.Equal(10, p.Facts["parking.fee"]);
            Assert.Single(p.Areas);
            Assert.Equal(AreaRole.Lot, p.Areas[0].Role);
            // Art pass: modded asphalt first, PLT's vanilla pavement as the fallback.
            Assert.Contains(p.Surfaces, s => s.Role == SurfaceRole.Asphalt && s.Asset.Id == "G87 Vanilla Asphalt Pavement G87 VA Surface GND Surface"
                && s.Asset.Fallbacks.Contains("Pavement Surface 01"));
            Assert.Contains(p.Surfaces, s => s.Role == SurfaceRole.Grass && s.Asset.Id == "Grass Surface 01");
            Assert.All(p.Surfaces, s => Assert.True(SurfaceAcceptance.IsAccepted(s.Ring.Select(q => q.xz).ToArray())));
            Assert.Contains(p.Regions, g => g.Tags.Contains(RegionTags.Lot) && g.Key.StartsWith("lot:"));
            Assert.Contains(p.Regions, g => g.Tags.Contains(RegionTags.Green) && g.Key.StartsWith("green:"));
            Assert.True(p.Objects.Count(o => o.Role == ObjectRole.Lamp) > 10);
            Assert.True(p.Objects.Count(o => o.Role == ObjectRole.Tree) > 5);
            Assert.Contains(p.Objects, o => o.Role == ObjectRole.Bush);
            var stats = p.Stats.ToDictionary(s => s.Key, s => s.Value);
            Assert.Equal(bays, stats["Placemaker.Parking.Stat.Stalls"]);
            Assert.Equal(160 * 110, stats["Placemaker.Parking.Stat.Area"], 0);
            Assert.True(stats["Placemaker.Parking.Stat.AisleLength"] > 500);
            Assert.Equal(p.Objects.Count(o => o.Role == ObjectRole.Tree), stats["Placemaker.Parking.Stat.Trees"]);
            // 1.76 ha with perimeter road, medians, cross lanes: PLT's comparable rectangle carried ~2 stalls per 100 m²
            Assert.InRange(bays, 330, 420);
        }

        [Fact]
        public void BayYawPointsFromTheAisleIntoTheStall()
        {
            var r = ParkingKit.Run(Rect, null, OneEntrance(Rect));
            var f = r.Layout.Frame;
            foreach (var b in r.Layout.LiveBays.Take(50))
            {
                var o = r.Plan.Objects.First(x => x.Role == ObjectRole.ParkingBay && math.distance(x.Position.xz, ToF(f.ToWorld(b.Center))) < 1e-3f);
                var fwd = new float2(math.sin(o.Yaw), math.cos(o.Yaw));
                var axis = f.DirToWorld(b.Axis);
                Assert.True(math.dot(fwd, new float2((float)axis.x, (float)axis.y)) > 0.999f);
                // the front edge (aisle side) is half a stall length behind the centre, along -forward
                var front = f.ToWorld((b.Quad[0] + b.Quad[1]) * 0.5);
                Assert.True(math.distance(ToF(front), o.Position.xz - fwd * (float)b.Length / 2) < 1e-3f);
            }
        }

        private static float2 ToF(double2 d) => new float2((float)d.x, (float)d.y);

        [Fact]
        public void GreeneryNoneIsPavedEdgeToEdge()
        {
            var r = ParkingKit.Run(Rect, With(("greenery", "none")), OneEntrance(Rect));
            AssertClean(r);
            Assert.All(r.Plan.Surfaces, s => Assert.Equal(SurfaceRole.Asphalt, s.Role));
            Assert.DoesNotContain(r.Plan.Objects, o => o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush);
            Assert.True(r.Layout.UncoveredArea < 1);
        }

        [Fact]
        public void EdgesGreeneryFitsMoreThanMedians()
        {
            var med = ParkingKit.Run(Rect, null, OneEntrance(Rect));
            var edges = ParkingKit.Run(Rect, With(("greenery", "edges")), OneEntrance(Rect));
            AssertClean(edges);
            Assert.True(edges.Stalls >= med.Stalls, $"{edges.Stalls} < {med.Stalls}");
        }

        [Theory]
        [InlineData("60")]
        [InlineData("45")]
        public void AngledStallsAreValidAndFewer(string angle)
        {
            var straight = ParkingKit.Run(Rect, null, OneEntrance(Rect));
            var r = ParkingKit.Run(Rect, With(("stallAngle", angle)), OneEntrance(Rect));
            AssertClean(r, angle);
            Assert.True(r.Stalls < straight.Stalls && r.Stalls > straight.Stalls / 2);
            Assert.Contains(r.Layout.LiveBays, b => b.Kind == BayKind.Inner && math.abs(PGeo.Dot(b.Axis, b.RowDir)) > 0.4);
        }

        [Fact]
        public void LeftHandTrafficMirrorsAngledStalls()
        {
            var right = ParkingKit.Run(Rect, With(("stallAngle", "60")), OneEntrance(Rect));
            var left = ParkingKit.Run(Rect, With(("stallAngle", "60")), OneEntrance(Rect), new SiteContext { LeftHandTraffic = true });
            AssertClean(left);
            var a = right.Layout.LiveBays.First(b => b.Kind == BayKind.Inner);
            var b2 = left.Layout.LiveBays.First(b => b.Kind == BayKind.Inner && math.abs(b.Axis.y - a.Axis.y) < 1e-9);
            Assert.Equal(-a.Axis.x, b2.Axis.x, 6);
        }

        [Fact]
        public void WithoutPerimeterRoadEveryStallIsStillReachable()
        {
            var r = ParkingKit.Run(Rect, With(("perimeterRoad", false), ("crossEvery", 0.0)), OneEntrance(Rect));
            AssertClean(r);
            Assert.DoesNotContain(r.Layout.LiveBays, b => b.Kind != BayKind.Inner);
            Assert.True(r.Stalls > 300);
        }

        [Fact]
        public void FixedRowAngleRotatesTheRows()
        {
            var r = ParkingKit.Run(Rect, With(("rowAngleMode", "fixed"), ("rowAngle", 30.0)), OneEntrance(Rect));
            AssertClean(r);
            Assert.Equal(30, r.Layout.RowAngleDeg, 3);
            var across = ParkingKit.Run(Rect, With(("rowAngleMode", "across")), OneEntrance(Rect));
            Assert.Equal(90, across.Layout.RowAngleDeg, 3);
        }

        [Fact]
        public void ReservedSpaceIsAvoided()
        {
            var reserved = new ReservedSpace();
            reserved.AddDisc(new float2(80, 55), 12, "fountain");
            var r = ParkingKit.Run(Rect, null, OneEntrance(Rect), reserved: reserved);
            Assert.Contains(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.ReservedSpace");
            foreach (var o in r.Plan.Objects.Where(o => o.Role == ObjectRole.ParkingBay))
                Assert.True(math.distance(o.Position.xz, new float2(80, 55)) > 12);
        }

        // ───────────── robustness ─────────────

        [Fact]
        public void BadInputBecomesIssuesNotExceptions()
        {
            var g = new ParkingLayer();
            AreaPlan P(float3[] ring) => g.Plan(new AreaInput { Shape = new AreaShape { Ring = ring }, Settings = g.Defaults() }, CancellationToken.None);
            Assert.Contains(P(ParkingKit.Ring(0, 0, 100, 100, 100, 0, 0, 100)).Issues, i => i.Key == "Placemaker.Parking.Issue.BadShape"); // bow tie
            Assert.Contains(P(ParkingKit.Rect(10, 10)).Issues, i => i.Key == "Placemaker.Parking.Issue.TooSmall");
            Assert.Contains(P(ParkingKit.Rect(1000, 1000)).Issues, i => i.Key == "Placemaker.Parking.Issue.TooLarge");
            Assert.Contains(P(Array.Empty<float3>()).Issues, i => i.Key == "Placemaker.Parking.Issue.BadShape");
            Assert.Contains(P(ParkingKit.Ring(0, 0, 100, 0, 200, 0)).Issues, i => i.Severity == IssueSeverity.Error); // degenerate
            var nan = ParkingKit.Rect(100, 80); nan[2].x = float.NaN;
            Assert.True(P(nan).HasErrors);
            // spikes, duplicates and collinear points are cleaned
            var messy = ParkingKit.Ring(0, 0, 50, 0, 50, 0, 100, 0, 100, 80, 100, 80.0001, 0, 80);
            var ok = P(messy);
            Assert.False(ok.HasErrors);
            Assert.True(ok.Facts["capacity"] > 50);
            // a narrow strip: no stalls, a warning, no throw
            var strip = P(ParkingKit.Rect(400, 4));
            Assert.Contains(strip.Issues, i => i.Severity >= IssueSeverity.Warning);
            // garbage settings are clamped
            var bag = g.Defaults();
            bag.Values["aisleWidth"] = double.NaN; bag.Values["stallWidth"] = -5.0; bag.Values["stallAngle"] = "17"; bag.Values["evShare"] = 1e9;
            var weird = g.Plan(new AreaInput { Shape = new AreaShape { Ring = Rect }, Settings = bag }, CancellationToken.None);
            Assert.False(weird.HasErrors);
        }

        [Fact]
        public void CancellationIsHonoured()
        {
            var g = new ParkingLayer();
            var input = new AreaInput { Shape = new AreaShape { Ring = Rect }, Settings = g.Defaults(), Markers = OneEntrance(Rect) };
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => g.Plan(input, cts.Token));
            // cancelled mid-run on a big lot: stops promptly
            var big = ParkingKit.Rect(450, 400);
            var input2 = new AreaInput { Shape = new AreaShape { Ring = big }, Settings = g.Defaults(), Markers = OneEntrance(big) };
            var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.ThrowsAny<OperationCanceledException>(() => g.Plan(input2, cts2.Token));
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(1500), sw.ElapsedMilliseconds + " ms");
        }

        private static string Fingerprint(AreaPlan p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in p.Objects) sb.Append($"{o.Role}{o.Asset.Id}{o.Position.x:R}{o.Position.z:R}{o.Yaw:R}{o.Variation};");
            foreach (var n in p.Nets) sb.Append($"{n.Role}{n.A.x:R}{n.A.z:R}{n.D.x:R}{n.D.z:R};");
            foreach (var s in p.Surfaces) { sb.Append(s.Role); foreach (var q in s.Ring) sb.Append($"{q.x:R},{q.z:R}"); sb.Append(';'); }
            foreach (var i in p.Issues) sb.Append(i.Key);
            return sb.ToString();
        }

        [Fact]
        public void DeterministicAndReentrant()
        {
            var shape = ParkingKit.LShapes()[123].Ring;
            var markers = OneEntrance(shape, 0, 20);
            var first = Fingerprint(ParkingKit.Run(shape, null, markers).Plan);
            Assert.Equal(first, Fingerprint(ParkingKit.Run(shape, null, markers).Plan));
            var results = new string[6];
            Parallel.For(0, results.Length, i => results[i] = Fingerprint(ParkingKit.Run(shape, null, markers).Plan));
            Assert.All(results, r => Assert.Equal(first, r));
            // another seed changes planting only
            var other = ParkingKit.Run(shape, null, markers, seed: 99).Plan;
            var basePlan = ParkingKit.Run(shape, null, markers).Plan;
            Assert.Equal(basePlan.Objects.Count(o => o.Role == ObjectRole.ParkingBay), other.Objects.Count(o => o.Role == ObjectRole.ParkingBay));
        }

        [Fact]
        public void RandomizeKeepsLockedChoicesAndIsDeterministic()
        {
            var g = new ParkingLayer();
            var cur = g.Defaults();
            cur.Values["stallAngle"] = "45";
            var locked = new HashSet<string> { "stallAngle" };
            var a = g.Randomize(cur, 5, locked);
            var b = g.Randomize(cur, 5, locked);
            Assert.True(a.ContentEquals(b));
            Assert.Equal("45", a.S("stallAngle", ""));
            bool anyDiff = Enumerable.Range(1, 10).Any(s => !g.Randomize(cur, (uint)s, locked).ContentEquals(cur));
            Assert.True(anyDiff);
        }

        [Fact]
        public void PresetsOnlyUseKnownControls()
        {
            var g = new ParkingLayer();
            var ids = new HashSet<string>(g.Controls.Select(c => c.Id));
            foreach (var p in g.Presets)
                foreach (var k in p.Values.Values.Keys) Assert.Contains(k, ids);
        }
    }
}
