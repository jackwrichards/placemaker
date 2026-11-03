// Footpaths meeting a city street (the user, 22 Oct 2026): without a crosswalk the path stops at the kerb, unattached (the
// game joins it by LocalConnect, no node on the road, no crosswalk); an entrance placed "with crosswalk" keeps the attach
// (the game splits the road there and draws the crosswalk). Layers/Common/StreetJoins, MarkerVariants.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class StreetJoinsTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        [Fact]
        public void TheCrosswalkVariantLivesInTheKind()
        {
            Assert.Equal("pedestrian+crosswalk", MarkerVariants.WithCrosswalk("pedestrian", true));
            Assert.Equal("pedestrian", MarkerVariants.WithCrosswalk("pedestrian+crosswalk", false));
            Assert.Equal("pedestrian+crosswalk", MarkerVariants.WithCrosswalk("pedestrian+crosswalk", true));
            Assert.True(MarkerVariants.HasCrosswalk("footpath+crosswalk"));
            Assert.False(MarkerVariants.HasCrosswalk("footpath"));
            Assert.False(MarkerVariants.HasCrosswalk(null));
            var m = MarkerVariants.Normalize(new EdgeMarker { Kind = "main+crosswalk" });
            Assert.Equal("main", m.Kind);
            Assert.True(m.Crosswalk);
            // the composer hands layers the plain kind
            var ring = TestKit.Rect(100, 80);
            var on = Composer.MarkersOn(new List<EdgeMarker> { new EdgeMarker { Kind = "pedestrian+crosswalk", Position = new float3(50, 0, 0) } }, ring);
            Assert.Equal("pedestrian", on.Single().Kind);
            Assert.True(on.Single().Crosswalk);
        }

        [Fact]
        public void OnlyWalkingEntrancesOfferACrosswalk()
        {
            var park = MarkerKinds.For(GeneratorRegistry.Get("park"));
            Assert.True(park.Single(k => k.Kind == "pedestrian").Crosswalk);
            Assert.True(park.Single(k => k.Kind == "main").Crosswalk);
            Assert.False(park.Single(k => k.Kind == "service").Crosswalk);
            var parking = MarkerKinds.For(GeneratorRegistry.Get("parking"));
            Assert.True(parking.Single(k => k.Kind == "footpath").Crosswalk);
            Assert.False(parking.Single(k => k.Kind == "entrance").Crosswalk);   // the two-way car entrance
            Assert.False(parking.Single(k => k.Kind == "alley").Crosswalk);
        }

        private static (ComposedPlan plan, SiteContext site, List<EdgeMarker> markers) Park(params string[] kinds)
        {
            var ring = TestKit.Rect(140, 120);
            var site = ZoningCutoutTests.Streets(ring);   // streets 8 m off the outline, 12 m wide: the kerb 2 m off it
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 5);
            // south edge (0) and east edge (1), mid-way
            for (int i = 0; i < kinds.Length; i++) r.Root.Markers.Add(ParkingKit.Marker(ring, i, i == 0 ? 70 : 60, kinds[i]));
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            return (plan, site, r.Root.Markers);
        }

        private static IEnumerable<(PlannedNet net, bool start, float3 at)> PathEnds(ComposedPlan plan) =>
            plan.Layers.Where(l => l != null).SelectMany(l => l.Nets).Where(PathJoins.IsPath)
                .SelectMany(n => new[] { (n, true, n.A), (n, false, n.D) });

        [Fact]
        public void APedestrianEntranceStopsAtTheKerbUnattached()
        {
            var (plan, site, markers) = Park("pedestrian");
            var gate = markers[0].Position.xz;
            var road = site.NearbyNets[0];
            // no footpath is attached to a street
            Assert.DoesNotContain(PathEnds(plan), e => (e.start ? e.net.StartAttach : e.net.EndAttach) >= 0);
            // the entrance's path ends at the kerb: its round end (half its width) clears the street's edge by the gap
            var end = PathEnds(plan).OrderBy(e => math.distance(e.at.xz, gate)).First();
            Assert.True(math.distance(end.at.xz, gate) < 6f, $"no path end near the entrance (nearest {math.distance(end.at.xz, gate):F1} m)");
            float d = StreetJoins.RoadDistance(road, end.at.xz);
            float want = road.Width * 0.5f + Composer.DefaultHalfWidth(end.net.Role) + StreetJoins.KerbGap;
            Assert.InRange(d, want - 0.1f, want + 0.1f);
            // within the game's LocalConnect reach: 4 m past the road's edge plus the path's half width
            Assert.True(d - road.Width * 0.5f <= Composer.DefaultHalfWidth(end.net.Role) + 4f);
            Assert.True(plan.KerbJoins >= 1);
            Assert.Equal(0, plan.CrosswalkJoins);
        }

        [Fact]
        public void ACrosswalkEntranceKeepsItsStreetJoin()
        {
            var (plan, site, markers) = Park("pedestrian+crosswalk", "pedestrian");
            Assert.True(plan.CrosswalkJoins >= 1, $"crosswalk joins {plan.CrosswalkJoins}, kerb joins {plan.KerbJoins}");
            var south = markers[0].Position.xz;
            var east = markers[1].Position.xz;
            var attached = PathEnds(plan).Where(e => (e.start ? e.net.StartAttach : e.net.EndAttach) >= 0).ToList();
            Assert.NotEmpty(attached);
            Assert.All(attached, e => Assert.True(math.distance(e.at.xz, south) < StreetJoins.CrosswalkReach, $"an attached path end at {e.at.xz} away from the crosswalk entrance"));
            Assert.All(attached, e => Assert.Equal(site.NearbyNets[0].ExternalId, e.start ? e.net.StartAttach : e.net.EndAttach));
            // the other entrance stops at its kerb
            var eastEnd = PathEnds(plan).OrderBy(e => math.distance(e.at.xz, east)).First();
            Assert.Equal(-1, eastEnd.start ? eastEnd.net.StartAttach : eastEnd.net.EndAttach);
            float wantEast = 6f + Composer.DefaultHalfWidth(eastEnd.net.Role) + StreetJoins.KerbGap;
            Assert.InRange(StreetJoins.RoadDistance(site.NearbyNets[1], eastEnd.at.xz), wantEast - 0.1f, wantEast + 0.1f);
        }

        [Fact]
        public void AnEndOnTheRoadIsCutBackToTheKerb()
        {
            var road = new NearbyNet { ExternalId = 7, A = new float3(-50, 0, -8), B = new float3(-15, 0, -8), C = new float3(15, 0, -8), D = new float3(50, 0, -8), Width = 12f, Category = NetCategory.Road };
            // a footpath from inside the area down to the road's centre line (a parking footpath's outer piece)
            var n = new PlannedNet { A = new float3(0, 0, 10), B = new float3(0, 0, 4), C = new float3(0, 0, -2), D = new float3(0, 0, -8), Role = NetRole.Pathway, EndAttach = 7, EndAttachT = 0.5f };
            StreetJoins.ToKerb(n, false, road, null, 1.5f);
            Assert.Equal(-1, n.EndAttach);
            Assert.InRange(n.D.z, -0.3f, -0.2f);   // 6 m (the street's half width) + 1.5 m (the path's) + 0.25 m from the centre line at z = -8
            Assert.Equal(10f, n.A.z, 3);
        }

        [Fact]
        public void AnEndShortOfTheKerbIsCarriedOnToIt()
        {
            var road = new NearbyNet { ExternalId = 7, A = new float3(-50, 0, -8), B = new float3(-15, 0, -8), C = new float3(15, 0, -8), D = new float3(50, 0, -8), Width = 12f, Category = NetCategory.Road };
            // starts at the outline 8 m from the centre line (2 m short of the kerb), runs inward
            var n = new PlannedNet { A = new float3(3, 0, 0), B = new float3(3, 0, 5), C = new float3(3, 0, 10), D = new float3(3, 0, 15), Role = NetRole.Pathway, StartAttach = 7, StartAttachT = 0.5f };
            StreetJoins.ToKerb(n, true, road);
            Assert.Equal(-1, n.StartAttach);
            Assert.InRange(n.A.z, -1.8f, -1.7f);   // carried on to 6.25 m from the centre line (no path width given)
            Assert.Equal(3f, n.A.x, 3);
            Assert.True(n.B.z < 5f && n.B.z > n.A.z);   // still straight, in order
        }

        [Fact]
        public void TheBuildKeepsADeadEndOffTheStreetByTheRealPathWidth()
        {
            var road = new NearbyNet { ExternalId = 7, A = new float3(-50, 0, -8), B = new float3(-15, 0, -8), C = new float3(15, 0, -8), D = new float3(50, 0, -8), Width = 12f, Category = NetCategory.Road };
            // an 8 m walk planned 0.25 m off the kerb (as for a 3 m one): its round end would touch the street
            var n = new PlannedNet { A = new float3(0, 0, 20), B = new float3(0, 0, 12), C = new float3(0, 0, 4), D = new float3(0, 0, -1.75f), Role = NetRole.PathwayWide };
            Assert.Equal(1, StreetJoins.ClearOfRoads(n, new[] { road }, 4f));
            Assert.InRange(n.D.z, 2.2f, 2.3f);    // 6 + 4 + 0.25 from the centre line
            Assert.Equal(20f, n.A.z, 3);
            // clear already: nothing moves; an attached end (a crosswalk entrance) never moves
            Assert.Equal(0, StreetJoins.ClearOfRoads(n, new[] { road }, 4f));
            var x = new PlannedNet { A = new float3(0, 0, 20), B = new float3(0, 0, 12), C = new float3(0, 0, 4), D = new float3(0, 0, -8), Role = NetRole.Pathway, EndAttach = 7 };
            Assert.Equal(0, StreetJoins.ClearOfRoads(x, new[] { road }, 1.5f));
            Assert.Equal(-8f, x.D.z, 3);
        }

        [Fact]
        public void InvisibleFootpathsKeepTheirStreetJoin()
        {
            var ring = TestKit.Rect(150, 130);
            var site = ZoningCutoutTests.Streets(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("parking"), 5);
            r.Root.Markers.Add(ParkingKit.Marker(ring, 0, 40, "entrance"));
            r.Root.Markers.Add(ParkingKit.Marker(ring, 2, 60, "footpath"));
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            Assert.Contains(PathEnds(plan), e => StreetJoins.IsInvisible(e.net) && (e.start ? e.net.StartAttach : e.net.EndAttach) == site.NearbyNets[2].ExternalId);
            Assert.Equal(0, plan.KerbJoins);
        }

        public static IEnumerable<object[]> WalkTypes() => new[] { "park", "parking", "stripMall", "plaza", "hikingTrail", "zenGarden", "lookoutHill", "adventurePlayground" }
            .Where(id => GeneratorRegistry.Get(id) != null).Select(id => new object[] { id });

        [Theory]
        [MemberData(nameof(WalkTypes))]
        public void NoTypeAttachesAFootpathToAStreetUnlessAskedTo(string type)
        {
            var ring = TestKit.Rect(150, 130);
            var site = ZoningCutoutTests.Streets(ring);
            var gen = GeneratorRegistry.Get(type);
            var r = Editor.SingleLayerRecipe(gen, 5);
            // one of every kind the type offers, round the outline
            var kinds = MarkerKinds.For(gen).Select(k => k.Kind).ToList();
            for (int i = 0; i < kinds.Count; i++) r.Root.Markers.Add(ParkingKit.Marker(ring, i % 4, 30 + 25 * (i / 4), kinds[i]));
            uint q = 0;
            Editor.AddDefaultFeatures(r, () => ++q);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var roads = new HashSet<long>(site.NearbyNets.Select(n => n.ExternalId));
            // invisible paths (a car park's walk to the street) keep their join: no crosswalk, and the kerb end crashed the game
            var bad = PathEnds(plan).Where(e => !StreetJoins.IsInvisible(e.net) && roads.Contains(e.start ? e.net.StartAttach : e.net.EndAttach)).ToList();
            Assert.True(bad.Count == 0, $"{type}: {bad.Count} footpath end(s) still split a street, e.g. at {string.Join("; ", bad.Take(3).Select(b => b.at.xz.ToString()))}");
            Assert.Equal(0, plan.CrosswalkJoins);
        }
    }
}
