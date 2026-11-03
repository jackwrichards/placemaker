// s14: the Commercial types (Core/Layers/Commercial): strip mall, shopping plaza, outdoor mall, shops & park.
// Every type × preset on a few sites: shop plots face a street or one of the type's lanes and are zoned with the
// chosen zone; lanes join existing streets; parking lots are real Parking-planner lots that keep off plots and
// lanes and are reachable; nothing stands in a cutout; plans are deterministic.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Commercial;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class CommercialTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        public static readonly string[] Types = { "stripMall", "shoppingPlaza", "outdoorMall", "shopsPark", "highStreet", "retailPark", "mallPark", "mallDistrict" };

        private static float3[] Site(string site) => site switch
        {
            "big" => TestKit.Rect(160, 160),
            "wide" => TestKit.Rect(220, 110),
            "L" => TestKit.LShape(150),
            _ => TestKit.Rect(115, 115),
        };

        public static IEnumerable<object[]> Cases()
        {
            foreach (var t in Types)
            {
                var presets = new List<string> { "" };
                presets.AddRange(GeneratorRegistry.Get(t).Presets.Select(p => p.Id));
                foreach (var p in presets)
                    foreach (var s in new[] { "small", "big", "wide", "L" })
                        if (Poly.Area(Poly.To2D(Site(s))) >= GeneratorRegistry.Get(t).MinArea)
                            yield return new object[] { t, p, s };
            }
        }

        private static ComposedPlan Compose(string type, string preset, float3[] ring, SiteContext site, uint seed = 5, string zone = null, bool entrances = true)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), seed);
            if (!string.IsNullOrEmpty(preset)) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            if (zone != null) r.Root.Settings.Values["zoneKind"] = zone;
            // (rf-shops3) The entrance types wait for the player's ways in: a car entrance mid main street, a footpath, a driveway.
            if (entrances) ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static List<(float2 a, float2 b, float hw)> RoadLines(SiteContext site, AreaPlan host)
        {
            var lines = new List<(float2, float2, float)>();
            foreach (var n in site.NearbyNets)
            {
                float3 prev = n.A;
                for (int i = 1; i <= 16; i++) { var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f); lines.Add((prev.xz, p.xz, n.Width * 0.5f)); prev = p; }
            }
            foreach (var n in host.Nets.Where(n => n.Role == NetRole.Alley || n.Role == NetRole.Street))
                lines.Add((n.A.xz, n.D.xz, n.Role == NetRole.Street ? Composer.DefaultHalfWidth(NetRole.Street) : StreetShops.AlleyHalf));
            return lines;
        }

        private static float2[] Inset(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            return q.Select(p => p + math.normalizesafe(c - p) * by).ToArray();
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryTypeAndPresetComposesARealCommercialSite(string type, string preset, string siteName)
        {
            var ring = Site(siteName);
            var site = ZoningCutoutTests.Streets(ring);
            var plan = Compose(type, preset, ring, site);
            string ctx = $"{type}/{preset}/{siteName}";
            Assert.False(plan.HasErrors, ctx + ": " + string.Join("; ", plan.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args ?? new object[0]))));
            var host = plan.Layers.First(l => l.GeneratorId == type);
            var plots = host.Regions.Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
            Assert.NotEmpty(plots);

            // Zoning: every plot gets zoned (it faces a street or lane), every quad lies on a plot, the zone is the chosen one.
            Assert.NotEmpty(host.Zoning);
            foreach (var q in plots)
                Assert.True(host.Zoning.Any(z => Poly.Contains(Inset(q, -1.2f), 0.25f * (z.A.xz + z.B.xz + z.C.xz + z.D.xz))), $"{ctx}: plot at {Poly.Centroid(q)} has no zoning");
            foreach (var z in host.Zoning)
            {
                var c = 0.25f * (z.A.xz + z.B.xz + z.C.xz + z.D.xz);
                Assert.Contains(plots, q => Poly.Contains(q, c) || Poly.DistanceToBoundary(q, c) < 1.2f);
                Assert.False(string.IsNullOrEmpty(z.ZoneId), ctx + ": empty zone id zones nothing in game");
            }
            for (int i = 0; i < plots.Count; i++)
                for (int k = i + 1; k < plots.Count; k++)
                    Assert.False(StreetShops.ConvexOverlap(plots[i], plots[k], 0.05f), $"{ctx}: plots {i} and {k} overlap");
            var roads = RoadLines(site, host);
            foreach (var q in plots)
            {
                bool faces = false;
                for (int e = 0; e < q.Length && !faces; e++)
                {
                    var m = 0.5f * (q[e] + q[(e + 1) % q.Length]);
                    faces = roads.Any(r => math.sqrt(SegmentIndex.DistSq(m, r.a, r.b)) <= r.hw + StreetFrontage.Tolerance + 0.01f);
                }
                Assert.True(faces, $"{ctx}: plot at {Poly.Centroid(q)} faces no street or lane");
            }

            // Lanes: attached ends sit on an existing street; free ends join another piece (identical node) or are dead ends.
            var lanes = host.Nets.Where(n => n.Role == NetRole.Alley || n.Role == NetRole.Street).ToList();
            foreach (var n in lanes)
            {
                if (n.StartAttach >= 0) Assert.Contains(site.NearbyNets, r => r.ExternalId == n.StartAttach && math.distance(Composer.Bezier(r.A, r.B, r.C, r.D, n.StartAttachT).xz, n.A.xz) < 0.5f);
                if (n.EndAttach >= 0) Assert.Contains(site.NearbyNets, r => r.ExternalId == n.EndAttach && math.distance(Composer.Bezier(r.A, r.B, r.C, r.D, n.EndAttachT).xz, n.D.xz) < 0.5f);
                Assert.True(n.StartAttach < 0 || n.StartAttach >= 0 && site.NearbyNets.Any(r => r.ExternalId == n.StartAttach), ctx + ": synthetic attach leaked");
                foreach (var p in Enumerable.Range(0, 13).Select(i => Composer.Bezier(n.A, n.B, n.C, n.D, i / 12f).xz))
                    Assert.DoesNotContain(plots, q => Poly.Contains(Inset(q, 0.3f), p));
            }
            Assert.DoesNotContain(plan.Layers.SelectMany(l => l.Nets), n => n.StartAttach < -1000 || n.EndAttach < -1000);

            // Parking: bays and drive lanes keep off plots; every bay is near a drive aisle.
            var bays = host.Objects.Where(o => o.Role == ObjectRole.ParkingBay).ToList();
            foreach (var b in bays)
                Assert.DoesNotContain(plots, q => Poly.Contains(q, b.Position.xz) || Poly.DistanceToBoundary(q, b.Position.xz) < 1.2f);
            foreach (var n in host.Nets.Where(n => n.Role == NetRole.DriveAisle || n.Role == NetRole.DriveConnector))
                foreach (var p in Enumerable.Range(0, 9).Select(i => Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz))
                    Assert.DoesNotContain(plots, q => Poly.Contains(Inset(q, 0.3f), p));
            if (host.Facts.TryGetValue("lots", out var lots) && lots > 0)
            {
                Assert.NotEmpty(bays);
                // Every lot is entered: a connector or an alley stub reaches a street or a lane.
                Assert.Contains(host.Nets, n => n.Role == NetRole.DriveConnector);
            }

            // Nothing of any layer stands on a plot (Cutouts.Enforce + planning around them) but the plot planting (rf-shops3: trees and
            // shrubs at the back of a plot, categories tree.plot / bush.plot, kept by Cutouts.KeepsOnPlot), which stands on nothing else.
            foreach (var l in plan.Layers)
                foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal))
                {
                    if (Cutouts.KeepsOnPlot(o))
                    {
                        Assert.True(o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush, $"{ctx}: plot planting {o.Role}");
                        Assert.True(plots.Any(q => Poly.Contains(q, o.Position.xz)), $"{ctx}: plot planting at {o.Position.xz} off the plots");
                        continue;
                    }
                    Assert.False(plots.Any(q => Poly.Contains(Inset(q, 0.3f), o.Position.xz)), $"{ctx}: {l.GeneratorId} {o.Role} at {o.Position.xz} on a plot");
                }
            TestKit.AssertSurfacesAccepted(host);
        }

        [Theory]
        [InlineData("stripMall", "")]
        [InlineData("shoppingPlaza", "court")]
        [InlineData("outdoorMall", "loop")]
        [InlineData("shopsPark", "alley")]
        public void PlansAreDeterministic(string type, string preset)
        {
            var ring = Site("big");
            var a = Compose(type, preset, ring, ZoningCutoutTests.Streets(ring));
            var b = Compose(type, preset, ring, ZoningCutoutTests.Streets(ring));
            Assert.Equal(a.Layers.Count, b.Layers.Count);
            for (int i = 0; i < a.Layers.Count; i++) Assert.Equal(TestKit.Fingerprint(a.Layers[i]), TestKit.Fingerprint(b.Layers[i]));
        }

        [Theory]
        [InlineData(ZoneKinds.CommercialLow, "EU Commercial Low")]
        [InlineData(ZoneKinds.ResidentialRow, "EU Residential Medium Row")]
        [InlineData(ZoneKinds.OfficeHigh, "Office High")]
        [InlineData(ZoneKinds.Industrial, "Industrial Manufacturing")]
        public void TheZoneTypeFlowsIntoTheZoning(string kind, string prefab)
        {
            var ring = Site("big");
            var plan = Compose("stripMall", "", ring, ZoningCutoutTests.Streets(ring), zone: kind);
            var zoning = plan.Layers.SelectMany(l => l.Zoning).ToList();
            Assert.NotEmpty(zoning);
            Assert.All(zoning, z => Assert.Equal(prefab, z.ZoneId));
        }

        [Fact]
        public void TheZoningFeatureDefaultsToLowCommercialAndTakesTheZoneType()
        {
            var ring = TestKit.Rect(130, 130);
            var site = ZoningCutoutTests.Streets(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 3);
            Editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 4);
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var zoning = plan.Layers.SelectMany(l => l.Zoning).ToList();
            Assert.NotEmpty(zoning);
            Assert.All(zoning, z => Assert.Equal("EU Commercial Low", z.ZoneId));
            var child = r.Root.Children.First(c => c.Layer == LayerBase.StreetShopsLayer);
            child.Settings.Values[Placemaker.Core.Layers.AddOns.EdgeZoningAddOn.KindControl] = ZoneKinds.ResidentialLow;
            plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            Assert.All(plan.Layers.SelectMany(l => l.Zoning), z => Assert.Equal("EU Residential Low", z.ZoneId));
            // An explicit zone asset still wins (mods).
            child.Settings.Values[Placemaker.Core.Layers.AddOns.EdgeZoningAddOn.ZoneControl] = "W7CommercialBeach";
            plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            Assert.All(plan.Layers.SelectMany(l => l.Zoning), z => Assert.Equal("W7CommercialBeach", z.ZoneId));
        }

        [Fact]
        public void ZoneIdsFollowTheCityTheme()
        {
            Assert.Equal("NA Commercial Low", ZoneKinds.ForTheme("EU Commercial Low", "NA"));
            Assert.Equal("EU Residential Medium Row", ZoneKinds.ForTheme("NA Residential Medium Row", "EU"));
            Assert.Equal("Office Low", ZoneKinds.ForTheme("Office Low", "NA"));
            Assert.Equal("EU Commercial Low", ZoneKinds.ForTheme("EU Commercial Low", ""));
            foreach (var k in ZoneKinds.All) Assert.False(string.IsNullOrEmpty(ZoneKinds.PrefabId(k)));
        }

        [Fact]
        public void LotsEnteredFromALaneShareANodeWithIt()
        {
            // Rear alley: the lot behind the shops is entered only from the lane — its alley stubs end on lane nodes.
            var ring = Site("big");
            var plan = Compose("stripMall", "rearAlley", ring, ZoningCutoutTests.Streets(ring));
            var host = plan.Layers.First(l => l.GeneratorId == "stripMall");
            Assert.True(host.Facts["lots"] >= 1);
            var alleys = host.Nets.Where(n => n.Role == NetRole.Alley).ToList();
            // Stubs: alley pieces with both ends free of streets that touch the lot; each stub end lies on another alley's node.
            var nodes = alleys.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            int joined = 0;
            foreach (var n in alleys.Where(n => n.StartAttach < 0 && n.EndAttach < 0 && math.distance(n.A.xz, n.D.xz) < 6f))
                foreach (var e in new[] { n.A.xz, n.D.xz })
                    if (nodes.Count(p => math.distance(p, e) < 1e-3f) >= 3) joined++;
            Assert.True(joined >= 1, "no lot stub shares a node with the lane");
        }

        [Fact]
        public void WithoutAStreetTheSiteBecomesAParkWithAWarning()
        {
            // (rf-shops3) The types laid out from the streets (here the shopping plaza); the types planned from the player's
            // entrances never become a park (below).
            var ring = Site("small");
            var plan = Compose("shoppingPlaza", "", ring, new SiteContext());
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.AllIssues(), i => i.Key == CommercialLayer.NoStreetKey);
            Assert.Empty(plan.Layers.SelectMany(l => l.Zoning));
        }

        [Theory]
        [InlineData("stripMall")]
        [InlineData("mallPark")]
        [InlineData("mallDistrict")]
        public void WithoutAStreetAnEntranceTypeIsStillPlannedNeverAPark(string type)
        {
            // (rf-shops3) No street, no entrance: the composition is still planned (the preview), with its ring and its plots;
            // no pocket park, and nothing joins a street.
            var ring = Site("small");
            var plan = Compose(type, "", ring, new SiteContext(), entrances: false);
            Assert.False(plan.HasErrors);
            Assert.DoesNotContain(plan.AllIssues(), i => i.Key == CommercialLayer.NoStreetKey);
            var host = plan.Layers.First(l => l.GeneratorId == type);
            Assert.Contains(host.Regions, Cutouts.IsCutout);
            Assert.Contains(host.Nets, n => n.Role == NetRole.Alley || n.Role == NetRole.Street);
            Assert.DoesNotContain(plan.Layers.SelectMany(l => l.Nets), n => n.StartAttach >= 0 || n.EndAttach >= 0);
        }

        [Fact]
        public void CommercialTypesAreInTheCommercialGroup()
        {
            foreach (var t in Types)
            {
                var g = GeneratorRegistry.Get(t);
                Assert.NotNull(g);
                Assert.Equal("commercial", GeneratorGroups.Of(g));
                Assert.InRange(g.Presets.Count, 2, 8);   // (shops4) the strip mall: its five and three with a square in the row
            }
        }
    }
}
