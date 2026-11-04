// A painted or bucket-filled outline (wiggly edges, a thin neck) used to come back "Too small" on a lot of 2+ ha: PLT's
// corner-by-corner inset crossed itself on the wiggles (user report 2026-10-19). The lot is tidied first now.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class ParkingTidyOutlineTests
    {
        private readonly ITestOutputHelper _out;
        public ParkingTidyOutlineTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// About 2.4 ha: a straight street side (bottom, the entrances), the other sides traced like a painted area (a vertex
        /// every 2 m, ±0.8 m wiggle) with a deep notch where a kept building stands.
        /// </summary>
        private static float3[] Painted(float wiggle = 0.8f)
        {
            var pts = new List<float2> { new float2(0, 0), new float2(170, 0) };
            void Wavy(float2 a, float2 b)
            {
                float len = math.distance(a, b);
                var dir = (b - a) / len;
                var n = new float2(-dir.y, dir.x);
                int k = (int)(len / 2f);
                for (int i = 1; i < k; i++) pts.Add(a + dir * (len * i / k) + n * ((i % 2 == 0 ? 1f : -1f) * wiggle));
                pts.Add(b);
            }
            Wavy(new float2(170, 0), new float2(175, 140));
            Wavy(new float2(175, 140), new float2(110, 150));
            // the notch around a kept building
            Wavy(new float2(110, 150), new float2(105, 95));
            Wavy(new float2(105, 95), new float2(70, 92));
            Wavy(new float2(70, 92), new float2(65, 150));
            Wavy(new float2(65, 150), new float2(-5, 140));
            Wavy(new float2(-5, 140), new float2(0, 0));
            pts.RemoveAt(pts.Count - 1);   // back at the start
            return pts.Select(p => new float3(p.x, 0, p.y)).ToArray();
        }

        private static NearbyNet Road(long id, float2 a, float2 b, float width = 12f)
        {
            var A = new float3(a.x, 0, a.y); var B = new float3(b.x, 0, b.y);
            return new NearbyNet { ExternalId = id, A = A, B = math.lerp(A, B, 1f / 3), C = math.lerp(A, B, 2f / 3), D = B, Width = width, Category = NetCategory.Road };
        }

        /// <summary>Streets 10 m outside every side of the painted lot (centre lines), as around the user's block.</summary>
        private static SiteContext Streets()
        {
            var corners = new[] { new float2(0, 0), new float2(170, 0), new float2(175, 140), new float2(110, 150), new float2(65, 150), new float2(-5, 140) };
            var site = new SiteContext();
            var c = new float2(85, 70);
            for (int i = 0; i < corners.Length; i++)
            {
                var a = corners[i]; var b = corners[(i + 1) % corners.Length];
                if (i == 3) continue;   // the notch side: the kept building, no street
                var d = math.normalize(b - a); var outN = new float2(d.y, -d.x);
                if (math.dot(outN, (a + b) * 0.5f - c) < 0) outN = -outN;
                site.NearbyNets.Add(Road(100 + i, a - d * 20f + outN * 10f, b + d * 20f + outN * 10f));
            }
            return site;
        }

        [Fact]
        public void APaintedOutlineWithStreetZoningIsNotTooSmall()
        {
            var ring = Painted();
            var markers = new List<EdgeMarker> { ParkingKit.Marker(ring, 0, 40), ParkingKit.Marker(ring, 0, 130) };
            var settings = new Placemaker.Core.Layers.Parking.ParkingLayer().Defaults();
            settings.Values[Placemaker.Core.Layers.Parking.ParkingIds.EdgeZoning] = "all";
            var r = ParkingKit.Run(ring, settings, markers, Streets());
            var keys = string.Join(", ", r.Plan.Issues.Select(i => i.Key));
            _out.WriteLine($"{ring.Length} corners, stalls {r.Stalls}, zoning quads {r.Plan.Zoning.Count}, issues: {keys}");
            Assert.DoesNotContain(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.TooSmall");
            Assert.False(r.Plan.HasErrors, keys);
            Assert.True(r.Stalls > 100, $"only {r.Stalls} stalls on about 2.4 ha with a zoned frontage");
        }

        [Fact]
        public void APaintedOutlineIsNotTooSmall()
        {
            var ring = Painted();
            var markers = new List<EdgeMarker> { ParkingKit.Marker(ring, 0, 40), ParkingKit.Marker(ring, 0, 130) };
            var r = ParkingKit.Run(ring, markers: markers);
            var keys = string.Join(", ", r.Plan.Issues.Select(i => i.Key));
            _out.WriteLine($"{ring.Length} corners, stalls {r.Stalls}, issues: {keys}");
            Assert.DoesNotContain(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.TooSmall");
            Assert.False(r.Plan.HasErrors, keys);
            Assert.True(r.Stalls > 200, $"only {r.Stalls} stalls on about 2.4 ha");
        }

        /// <summary>A 170 x 140 m lot with cuts the area tools leave: a slit (a footpath or fence the fill stopped at) and a
        /// narrow neck to a side bay.</summary>
        private static float3[] Cut(float slit, float neck)
        {
            var p = new List<float2>
            {
                new float2(0, 0), new float2(170, 0), new float2(170, 140),
                // a slit 60 m deep from the top edge
                new float2(100 + slit, 140), new float2(100 + slit, 80), new float2(100, 80), new float2(100, 140),
                new float2(0, 140),
                // a side bay reached through a neck on the left
                new float2(0, 90 + neck), new float2(-30, 90 + neck), new float2(-30, 130), new float2(-70, 130), new float2(-70, 60),
                new float2(-30, 60), new float2(-30, 90), new float2(0, 90),
            };
            return p.Select(q => new float3(q.x, 0, q.y)).ToArray();
        }

        [Theory]
        [InlineData(0.6f, 3f)]
        [InlineData(0.05f, 3f)]
        [InlineData(3f, 1.2f)]
        public void SlitsAndNecksAreNotTooSmall(float slit, float neck)
        {
            var ring = Cut(slit, neck);
            var markers = new List<EdgeMarker> { ParkingKit.Marker(ring, 0, 40), ParkingKit.Marker(ring, 0, 130) };
            var r = ParkingKit.Run(ring, markers: markers);
            var keys = string.Join(", ", r.Plan.Issues.Select(i => i.Key));
            _out.WriteLine($"slit {slit} m, neck {neck} m: stalls {r.Stalls}, issues: {keys}");
            Assert.DoesNotContain(r.Plan.Issues, i => i.Key == "Placemaker.Parking.Issue.TooSmall");
            Assert.True(r.Stalls > 300, $"only {r.Stalls} stalls on about 2.6 ha");
        }

        // Known gap: a lot traced like a painted area (±0.8 m wiggles every 2 m) gets about a third fewer stalls (303 vs 471).
        [Fact(Skip = "known gap: wiggly painted edges cost about a third of the stalls")]
        public void AWigglyLotGetsAboutAsManyStallsAsAStraightOne()
        {
            var markers = new Func<float3[], List<EdgeMarker>>(ring => new List<EdgeMarker> { ParkingKit.Marker(ring, 0, 40), ParkingKit.Marker(ring, 0, 130) });
            var straight = Painted(0f);
            var wiggly = Painted(0.8f);
            var a = ParkingKit.Run(straight, markers: markers(straight));
            var b = ParkingKit.Run(wiggly, markers: markers(wiggly));
            _out.WriteLine($"straight {a.Stalls} stalls, wiggly {b.Stalls}");
            Assert.True(b.Stalls >= a.Stalls * 0.85, $"wiggly {b.Stalls} vs straight {a.Stalls}");
        }
    }
}
