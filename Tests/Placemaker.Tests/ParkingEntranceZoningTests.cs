// Parking round 2: PLT parity for entrance kinds (two-way, way in/out, driveway junctions, footpath),
// bus stops, frontage zoning (edge zoning + zoning patches), schema groups, readiness and suggestions.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ParkingEntranceZoningTests
    {
        private static readonly float3[] Rect = ParkingKit.Rect(160, 110);

        private static SettingsBag With(params (string Key, object Value)[] values)
        {
            var b = new ParkingLayer().Defaults();
            foreach (var v in values) b.Values[v.Key] = v.Value;
            return b;
        }

        private static NearbyNet Road(long id, double ax, double az, double bx, double bz, float width = 12, bool oneWay = false)
        {
            var a = new float3((float)ax, 0, (float)az); var b = new float3((float)bx, 0, (float)bz);
            return new NearbyNet { ExternalId = id, A = a, B = math.lerp(a, b, 1f / 3), C = math.lerp(a, b, 2f / 3), D = b, Width = width, Category = NetCategory.Road, IsOneWay = oneWay };
        }

        /// <summary>Street along the bottom edge (z = 0): centre 10 m out, 12 m wide → kerb 4 m from the outline.</summary>
        private static SiteContext Street(params NearbyNet[] more)
        {
            var site = new SiteContext();
            site.NearbyNets.Add(Road(42, -100, -10, 260, -10));
            site.NearbyNets.AddRange(more);
            return site;
        }

        private static List<EdgeMarker> M(params (int Edge, double Along, string Kind)[] ms) =>
            ms.Select(m => ParkingKit.Marker(Rect, m.Edge, m.Along, m.Kind)).ToList();

        private static void AssertClean(ParkingRun r)
        {
            var q = ParkingKit.Check(r);
            Assert.True(q.Problems.Count == 0, string.Join("; ", q.Problems.Take(5)) + " | " + q);
            Assert.False(r.Plan.HasErrors, string.Join(",", r.Plan.Issues.Select(i => i.Key)));
        }

        // ───────────── readiness ─────────────

        [Fact]
        public void ReadinessCountsDirectionNotBuildType()
        {
            var g = new ParkingLayer();
            Readiness R(params string[] kinds) => g.CheckReady(new AreaInput { Markers = kinds.Select(k => new EdgeMarker { Kind = k }).ToList() });
            Assert.False(R().Ready);
            Assert.Equal("Placemaker.Parking.Step.PlaceEntrance", R().NextStepKey);
            Assert.Equal(1, (int)R().ReasonArgs[0]);                          // minimum vehicle entrances
            Assert.Equal(0, (int)R().ReasonArgs[1]);
            Assert.False(R("footpath").Ready);                                 // people only
            Assert.False(R("busStop", "zoningPatch", "footpath").Ready);
            Assert.True(R("alley").Ready);
            Assert.True(R("alleyIn", "alleyOut").Ready);
            Assert.True(R("entry", "alleyOut").Ready);
            Assert.True(R("alleyIn", "exit").Ready);
            Assert.Equal("Placemaker.Parking.Reason.EntryWithoutExit", R("alleyIn").ReasonKey);
            Assert.Equal("Placemaker.Parking.Reason.ExitWithoutEntry", R("alleyOut", "footpath").ReasonKey);
            Assert.Equal(2, (int)R("entrance", "footpath", "exit").ReasonArgs[1]);
        }

        // ───────────── schema ─────────────

        [Fact]
        public void SchemaExposesMarkerKindsWithLabelsIconsAndOrderedGroups()
        {
            var g = new ParkingLayer();
            var markers = g.Controls.Single(c => c.Kind == ControlKind.Markers);
            Assert.Equal("entrances", markers.Group);
            Assert.Same(markers, g.Controls[0]);                                // entrances first
            Assert.Equal(markers.MarkerKinds, markers.Options.Select(o => o.Value).ToArray());
            foreach (var o in markers.Options)
            {
                Assert.Equal("Placemaker.Parking.Marker." + Placemaker.Core.Tool.MarkerKinds.Pascal(o.Value), o.LabelKey);
                Assert.Equal("Placemaker.Parking.Marker." + Placemaker.Core.Tool.MarkerKinds.Pascal(o.Value) + ".Tip", o.TooltipKey);
                Assert.False(string.IsNullOrEmpty(o.Icon), o.Value);
            }
            var order = new[] { "entrances", "layout", "spaces", "planting", "lighting", "surfaces", "zoning", "fee" };
            // s21: the shared decal dressing controls (own section / host sections) are not part of the lot's own order.
            var own = g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id)).ToList();
            var idx = own.Select(c => Array.IndexOf(order, c.Group)).ToList();
            Assert.DoesNotContain(-1, idx);
            for (int i = 1; i < idx.Count; i++) Assert.True(idx[i] >= idx[i - 1], "groups are contiguous and ordered at " + own[i].Id);
            Assert.Equal(order, idx.Distinct().Select(i => order[i]).ToArray());
            Assert.Equal(AssetCategories.Zone, g.Controls.Single(c => c.Id == "zoneType").AssetCategory);
            Assert.DoesNotContain(g.Controls, c => c.Id == "entranceStyle");    // replaced by marker kinds
        }

        // ───────────── suggestions ─────────────

        [Fact]
        public void SuggestionsAreOneTwoWayOrEntryExitOnALongFrontageAndNeverAlleys()
        {
            var g = new ParkingLayer();
            // small lot: one two-way entrance
            var small = ParkingKit.Rect(60, 40);
            var s1 = g.SuggestMarkers(new AreaInput { Shape = new AreaShape { Ring = small }, Site = Street(), Settings = g.Defaults() });
            Assert.Single(s1);
            Assert.Equal("entrance", s1[0].Kind);

            // big lot with a 300 m frontage on one street: a way in and a way out, far apart
            var big = ParkingKit.Rect(300, 120);
            var site = new SiteContext();
            site.NearbyNets.Add(Road(5, -100, -10, 450, -10));
            var input = new AreaInput { Shape = new AreaShape { Ring = big }, Site = site, Settings = g.Defaults() };
            var s2 = g.SuggestMarkers(input);
            Assert.Equal(new[] { "entry", "exit" }, s2.Select(m => m.Kind).ToArray());
            Assert.All(s2, m => Assert.Equal(0, m.RingSegment));
            Assert.True(math.distance(s2[0].Position, s2[1].Position) >= 40);
            input.Markers = s2;
            Assert.True(g.CheckReady(input).Ready);
            var plan = g.Plan(input, CancellationToken.None);
            Assert.Equal(2, plan.Nets.Count(n => n.Role == NetRole.DriveConnector && n.Asset.Id == "Placemaker Slow Invisible Road Path - 1xOneway"));

            foreach (var m in s1.Concat(s2)) Assert.DoesNotContain("alley", m.Kind);
        }

        // ───────────── each kind → nets ─────────────

        [Fact]
        public void TwoWayEntranceIsAPathToTheKerb()
        {
            var r = ParkingKit.Run(Rect, null, M((0, 60, "entrance")), Street());
            var con = r.Plan.Nets.Single(n => n.Role == NetRole.DriveConnector);
            Assert.Equal("Placemaker Slow Invisible Road Path - 2xTwoway", con.Asset.Id);
            Assert.InRange(con.A.z, -4.05f, -3.95f);
            Assert.DoesNotContain(r.Plan.Nets, n => n.Role == NetRole.Alley);
            Assert.DoesNotContain(r.Plan.Objects, o => o.Role == ObjectRole.Arrow);
            AssertClean(r);
        }

        [Fact]
        public void AlleyKindsBuildAJunctionWithTheStreet()
        {
            var two = ParkingKit.Run(Rect, null, M((0, 60, "alley")), Street());
            var a = two.Plan.Nets.Single(n => n.Role == NetRole.Alley);
            Assert.Equal("Alley", a.Asset.Id);
            Assert.Equal(42, a.StartAttach);
            Assert.InRange(a.StartAttachT, 0.4f, 0.5f);
            Assert.InRange(a.A.z, -10.01f, -9.99f);                               // on the street centreline
            Assert.True(math.distance(a.D, two.Plan.Nets.Single(n => n.Role == NetRole.DriveConnector).A) < 1e-3f);
            AssertClean(two);

            var io = ParkingKit.Run(Rect, null, M((0, 40, "alleyIn"), (0, 120, "alleyOut")), Street());
            var alleys = io.Plan.Nets.Where(n => n.Role == NetRole.Alley).ToList();
            Assert.Equal(2, alleys.Count);
            Assert.All(alleys, n => Assert.Equal("Alley Oneway", n.Asset.Id));
            var inn = alleys.Single(n => math.min(n.A.x, n.D.x) < 80);
            var outn = alleys.Single(n => math.min(n.A.x, n.D.x) > 80);
            Assert.Equal(42, inn.StartAttach);                                    // street → lot
            Assert.Equal(-1, inn.EndAttach);
            Assert.True(inn.A.z < inn.D.z);
            Assert.Equal(42, outn.EndAttach);                                     // lot → street
            Assert.True(outn.A.z > outn.D.z);
            Assert.Equal(2, io.Plan.Objects.Count(o => o.Role == ObjectRole.Arrow));
            Assert.Equal(2, io.Plan.Nets.Count(n => n.Role == NetRole.DriveConnector && n.Asset.Id == "Placemaker Slow Invisible Road Path - 1xOneway"));
            AssertClean(io);
        }

        [Fact]
        public void LegacyAlleyStyleStillBuildsJunctions()
        {
            var r = ParkingKit.Run(Rect, With(("entranceStyle", "alley")), M((0, 40, "entry"), (0, 120, "exit")), Street());
            Assert.Equal(2, r.Plan.Nets.Count(n => n.Role == NetRole.Alley));
        }

        [Fact]
        public void FootpathIsForPeopleOnlyAndJoinsTheStreet()
        {
            var r = ParkingKit.Run(Rect, null, M((0, 40, "entrance"), (0, 120, "footpath")), Street());
            Assert.Single(r.Plan.Nets, n => n.Role == NetRole.DriveConnector);   // only the car entrance
            var paths = r.Plan.Nets.Where(n => n.Role == NetRole.Pathway).ToList();
            var outer = paths.Single(n => n.EndAttach == 42);
            Assert.Equal("Invisible Pedestrian Path", outer.Asset.Id);
            Assert.InRange(outer.D.z, -10.01f, -9.99f);
            Assert.InRange(outer.A.z, -0.01f, 0.01f);                             // starts on the outline
            Assert.InRange(outer.A.x, 115f, 125f);
            Assert.Contains(paths, n => n != outer && (math.distance(n.A, outer.A) < 1e-3f || math.distance(n.D, outer.A) < 1e-3f));
            AssertClean(r);

            // a footpath alone is not an entrance for cars
            var only = ParkingKit.Run(Rect, null, M((0, 120, "footpath")), Street());
            Assert.Contains(only.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.NoEntrance");
            Assert.DoesNotContain(only.Plan.Nets, n => n.Role == NetRole.DriveConnector);
        }

        [Fact]
        public void BusStopStandsOnTheLotSideSidewalkFacingTraffic()
        {
            var r = ParkingKit.Run(Rect, null, M((0, 40, "entrance"), (0, 100, "busStop")), Street());
            var stop = r.Plan.Objects.Single(o => o.Asset.Id == "Placemaker:BusStop");
            Assert.Equal(ObjectRole.Marker, stop.Role);
            Assert.InRange(stop.Position.z, -5.6f, -5.4f);                       // kerb side of a 12 m street, 1.5 m in
            Assert.InRange(stop.Position.x, 99f, 101f);
            // right-hand traffic: the lane next to the lot (north side of an eastbound road line) runs west
            Assert.InRange(stop.Yaw, -(float)Math.PI / 2 - 0.01f, -(float)Math.PI / 2 + 0.01f);
            var lht = ParkingKit.Run(Rect, null, M((0, 40, "entrance"), (0, 100, "busStop")), WithLht(Street()));
            Assert.InRange(lht.Plan.Objects.Single(o => o.Asset.Id == "Placemaker:BusStop").Yaw, (float)Math.PI / 2 - 0.01f, (float)Math.PI / 2 + 0.01f);

            var none = ParkingKit.Run(Rect, null, M((0, 40, "entrance"), (2, 50, "busStop")), Street());
            Assert.Contains(none.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.BusStopNoRoad");
        }

        private static SiteContext WithLht(SiteContext s) { s.LeftHandTraffic = true; return s; }

        [Fact]
        public void AtMostFifteenEntrances()
        {
            var ms = Enumerable.Range(0, 18).Select(i => ParkingKit.Marker(Rect, i % 4, 10 + (i / 4) * 20, "entrance")).ToList();
            var r = ParkingKit.Run(Rect, null, ms, Street());
            Assert.Contains(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.TooManyEntrances" && (int)i.Args[1] == 3);
            Assert.True(r.Layout.Entrances.Count <= 15);
        }

        // ───────────── zoning ─────────────

        private static double OverlapArea(float2[] a, float2[] b) =>
            Poly.Intersection(a, b).Sum(reg => (double)reg.Area);

        /// <summary>No zoned quad shares area with a surface, stall, lane or path, and every quad stays inside the outline.</summary>
        private static void AssertZoningClear(ParkingRun r)
        {
            var outline = r.Input.Shape.Ring.Select(p => p.xz).ToArray();
            if (Poly.SignedArea(outline) < 0) Array.Reverse(outline);
            double outlineArea = Poly.Area(outline);
            var f = r.Layout.Frame;
            foreach (var z in r.Plan.Zoning)
            {
                var q = new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz };
                if (Poly.SignedArea(q) < 0) Array.Reverse(q);
                double qa = Poly.Area(q);
                Assert.True(OverlapArea(q, outline) > qa - 0.5, "zoning leaves the outline");
                foreach (var s in r.Plan.Surfaces)
                    Assert.True(OverlapArea(q, s.Ring.Select(p => p.xz).ToArray()) < 0.05, "zoning on a surface");
                foreach (var b in r.Layout.LiveBays)
                {
                    var bq = b.Quad.Select(p => { var w = f.ToWorld(p); return new double2(w.x, w.y); }).ToArray();
                    Assert.False(PGeo.ConvexOverlap(q.Select(p => new double2(p.x, p.y)).ToArray(), bq, 1e-3), "zoning on a stall");
                }
                foreach (var n in r.Plan.Nets)
                {
                    var cor = PGeo.Corridor(new double2(n.A.x, n.A.z), new double2(n.D.x, n.D.z), n.Role == NetRole.Pathway ? 0.9 : 1.9);
                    Assert.False(PGeo.ConvexOverlap(q.Select(p => new double2(p.x, p.y)).ToArray(), cor, 1e-3), "zoning on a " + n.Role);
                }
            }
        }

        [Fact]
        public void OneSideZoningLeavesABandOfBuildingPlotsAlongTheStreet()
        {
            var plain = ParkingKit.Run(Rect, null, M((0, 40, "entrance")), Street());
            var r = ParkingKit.Run(Rect, With(("edgeZoning", "oneSide"), ("zoningDepth", 3.0), ("zoneType", "EU Residential Low")), M((0, 40, "entrance")), Street());
            AssertClean(r);
            Assert.NotEmpty(r.Plan.Zoning);
            Assert.All(r.Plan.Zoning, z => Assert.Equal("EU Residential Low", z.ZoneId));
            // depth: 3 cells from the street edge, the kerb is 4 m out → 20 m inside the lot
            foreach (var z in r.Plan.Zoning)
            {
                Assert.InRange(math.min(z.A.z, z.D.z), -0.01f, 0.01f);
                Assert.InRange(math.max(z.A.z, z.D.z), 19.95f, 20.05f);
                Assert.True(math.distance(z.A.xz, z.B.xz) <= 48.01f);
            }
            // a gap around the entrance at x = 40 (7 m lane + 3 m each side)
            Assert.DoesNotContain(r.Plan.Zoning, z => math.min(z.A.x, z.B.x) < 40 && math.max(z.A.x, z.B.x) > 40);
            AssertZoningClear(r);
            Assert.True(r.Plan.Facts["zoning.cells"] > 0);
            Assert.True(r.Plan.Facts["capacity"] < plain.Plan.Facts["capacity"], "the band costs spaces");
            // the lot owner area no longer covers the band
            Assert.True(r.Plan.Areas.Single().Ring.Min(p => p.z) > 19.9f);
            // the entrance still reaches the kerb through the band
            var con = r.Plan.Nets.Single(n => n.Role == NetRole.DriveConnector);
            Assert.InRange(math.min(con.A.z, con.D.z), -4.05f, -3.95f);
        }

        [Fact]
        public void AllSidesZonesEveryStreetSideAndOnlyThose()
        {
            var site = Street(Road(9, 170, -50, 170, 160));                        // a second street on the right
            var one = ParkingKit.Run(Rect, With(("edgeZoning", "oneSide")), M((0, 40, "entrance")), site);
            var all = ParkingKit.Run(Rect, With(("edgeZoning", "all")), M((0, 40, "entrance")), site);
            AssertClean(all);
            AssertZoningClear(all);
            bool Right(PlannedZoning z) => math.max(z.A.x, z.B.x) > 159.9f && math.abs(z.A.x - z.B.x) < 0.01f;
            Assert.DoesNotContain(one.Plan.Zoning, Right);                          // longest frontage only (160 m bottom)
            Assert.Contains(all.Plan.Zoning, Right);
            Assert.DoesNotContain(all.Plan.Zoning, z => math.max(z.A.z, z.B.z) > 109.9f && math.abs(z.A.z - z.B.z) < 0.01f); // no street on top
        }

        [Fact]
        public void ZoningPatchIsAPlotOfTheChosenWidthAtTheMarker()
        {
            var r = ParkingKit.Run(Rect, With(("zoningPatchWidth", 4.0)), M((0, 30, "entrance"), (0, 110, "zoningPatch")), Street());
            AssertClean(r);
            AssertZoningClear(r);
            double width = r.Plan.Zoning.Sum(z => (double)math.distance(z.A.xz, z.B.xz));
            Assert.InRange(width, 31.9, 32.1);
            Assert.All(r.Plan.Zoning, z => Assert.InRange((math.min(z.A.x, z.B.x) + math.max(z.A.x, z.B.x)) / 2, 100f, 120f));

            var far = ParkingKit.Run(Rect, null, M((0, 30, "entrance"), (2, 60, "zoningPatch")), Street());
            Assert.Empty(far.Plan.Zoning);
            Assert.Contains(far.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.ZoningPatchNoStreet");
        }

        [Fact]
        public void NoStreetMeansNoZoningAndAnUnchangedLot()
        {
            var plain = ParkingKit.Run(Rect, null, M((0, 40, "entrance")));
            var r = ParkingKit.Run(Rect, With(("edgeZoning", "all")), M((0, 40, "entrance")));
            Assert.Empty(r.Plan.Zoning);
            Assert.Contains(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.ZoningNoStreet");
            Assert.Equal(plain.Plan.Facts["capacity"], r.Plan.Facts["capacity"]);
        }

        [Fact]
        public void ZoningThatWouldEatTheLotIsDropped()
        {
            var thin = ParkingKit.Rect(100, 30);
            var site = new SiteContext();
            site.NearbyNets.Add(Road(1, -50, -6, 150, -6, 12));
            site.NearbyNets.Add(Road(2, -50, 36, 150, 36, 12));
            var r = ParkingKit.Run(thin, With(("edgeZoning", "all"), ("zoningDepth", 6.0)), new List<EdgeMarker> { ParkingKit.Marker(thin, 0, 50) }, site);
            Assert.Empty(r.Plan.Zoning);
            Assert.Contains(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.ZoningLeavesNoRoom");
            Assert.False(r.Plan.HasErrors);
        }

        [Fact]
        public void ZoningNeverOverlapsTheLotOnRotatedConcaveShapes()
        {
            // the L/T/U sweep with a street along edge 0 and a zoning patch; every variant must stay clear
            var shapes = ParkingKit.LShapes().Where((s, i) => i % 23 == 0).ToList();
            int zoned = 0;
            foreach (var s in shapes)
            {
                var a = s.Ring[0]; var b = s.Ring[1];
                var ring2 = s.Ring.Select(p => p.xz).ToArray();
                var tan = math.normalize(b.xz - a.xz);
                var outward = new float2(tan.y, -tan.x);
                if (!Poly.IsCcw(ring2)) outward = -outward;
                var ra = a.xz - tan * 60 + outward * 9; var rb = b.xz + tan * 60 + outward * 9;
                var site = new SiteContext();
                site.NearbyNets.Add(Road(77, ra.x, ra.y, rb.x, rb.y, 10));
                float len = math.distance(a.xz, b.xz);
                var markers = new List<EdgeMarker> { ParkingKit.Marker(s.Ring, 0, len * 0.3), ParkingKit.Marker(s.Ring, 0, len * 0.75, "zoningPatch") };
                var r = ParkingKit.Run(s.Ring, With(("edgeZoning", "all"), ("zoningDepth", 4.0)), markers, site);
                Assert.False(r.Plan.HasErrors, s.Name + ": " + string.Join(",", r.Plan.Issues.Select(i => i.Key)));
                AssertZoningClear(r);
                if (r.Plan.Zoning.Count > 0) zoned++;
            }
            Assert.True(zoned > shapes.Count / 2, $"only {zoned}/{shapes.Count} shapes got zoning");
        }

        // ───────────── determinism & performance ─────────────

        [Fact]
        public void EntrancesAndZoningAreDeterministic()
        {
            var ms = M((0, 30, "alleyIn"), (0, 130, "alleyOut"), (1, 50, "footpath"), (0, 80, "busStop"), (0, 100, "zoningPatch"));
            var site = Street(Road(9, 170, -50, 170, 160));
            var bag = With(("edgeZoning", "all"));
            var a = ParkingKit.Run(Rect, bag, ms, site).Plan;
            var b = ParkingKit.Run(Rect, bag.Clone(), ms.ToList(), site).Plan;
            Assert.Equal(a.Nets.Count, b.Nets.Count);
            for (int i = 0; i < a.Nets.Count; i++)
            {
                Assert.Equal(a.Nets[i].A, b.Nets[i].A); Assert.Equal(a.Nets[i].D, b.Nets[i].D);
                Assert.Equal(a.Nets[i].Role, b.Nets[i].Role); Assert.Equal(a.Nets[i].StartAttach, b.Nets[i].StartAttach); Assert.Equal(a.Nets[i].EndAttach, b.Nets[i].EndAttach);
            }
            Assert.Equal(a.Zoning.Count, b.Zoning.Count);
            for (int i = 0; i < a.Zoning.Count; i++) { Assert.Equal(a.Zoning[i].A, b.Zoning[i].A); Assert.Equal(a.Zoning[i].C, b.Zoning[i].C); }
            Assert.Equal(a.Objects.Count, b.Objects.Count);
        }

        [Fact]
        public void ZoningStaysWithinTheTimeBudget()
        {
            var big = ParkingKit.Rect(250, 200);                                   // 5 ha
            var site = new SiteContext();
            site.NearbyNets.Add(Road(1, -100, -10, 350, -10));
            site.NearbyNets.Add(Road(2, 260, -50, 260, 250));
            site.NearbyNets.Add(Road(3, -100, 210, 350, 210));
            var ms = new List<EdgeMarker> { ParkingKit.Marker(big, 0, 60), ParkingKit.Marker(big, 0, 190, "footpath"), ParkingKit.Marker(big, 2, 100, "alley") };
            var bag = With(("edgeZoning", "all"));
            ParkingKit.Run(big, bag, ms, site); // JIT
            var sw = Stopwatch.StartNew();
            var r = ParkingKit.Run(big, bag, ms, site);
            sw.Stop();
            Assert.NotEmpty(r.Plan.Zoning);
            AssertZoningClear(r);
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(400), sw.ElapsedMilliseconds + " ms");
        }
    }
}
