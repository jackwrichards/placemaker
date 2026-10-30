// The designed squares of the plazas makeover (2026-10-29, docs/runbooks/plazas.md): the stone theatre, the market square,
// the transit forecourt, the summer fair, the Christmas market and the grungy square plan on a hectare, a six-hectare L
// and a slope without errors, inside their outline, with surfaces the game keeps, and their own signature.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests.Types
{
    public class DesignedSquaresTests
    {
        public static IEnumerable<object[]> Cases() => new[]
        {
            new object[] { "amphitheatre", "theatre", "theatreRows" },
            new object[] { "marketSquare", "weekly", "marketStalls" },
            new object[] { "marketSquare", "bigMarket", "marketStalls" },
            new object[] { "fairground", "fairground", "fairStalls" },
            new object[] { "fairground", "christmas", "fairStalls" },
            new object[] { "transitPlaza", "busPlaza", "busBays" },
            new object[] { "plaza", "grungy", "grungyPlanters" },
        };

        private static SettingsBag Preset(string type, string id)
        {
            var gen = GeneratorRegistry.Get(type);
            var p = gen.Presets.First(x => x.Id == id);
            var bag = gen.Defaults();
            foreach (var kv in p.Values.Values) bag.Values[kv.Key] = kv.Value;
            return bag;
        }

        private static SiteContext Sloped() { var s = TestKit.RoadAlongBottom(300); s.HeightAt = p => 0.03f * p.x + 0.01f * p.y; return s; }

        [Theory]
        [MemberData(nameof(Cases))]
        public void PlansCleanlyOnAnySite(string type, string preset, string signature)
        {
            foreach (var ring in new[] { EventsKit.OneHa, EventsKit.SixHaL })
                foreach (var site in new[] { TestKit.RoadAlongBottom(300), Sloped() })
                {
                    var plan = EventsKit.Plan(type, ring, Preset(type, preset), site);
                    Assert.False(plan.HasErrors, type + "/" + preset + ": " + string.Join(",", plan.Issues.Select(i => i.Key)));
                    Assert.True(plan.Objects.Count > 20, type + "/" + preset + " placed only " + plan.Objects.Count);
                    EventsKit.AssertInside(plan, ring, 1.5f);
                    TestKit.AssertSurfacesAccepted(plan);
                    Assert.True(plan.Facts.TryGetValue(signature, out var n) && n > 0, type + "/" + preset + ": " + signature + " " + n);
                }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void IsDeterministic(string type, string preset, string signature)
        {
            var a = EventsKit.Plan(type, EventsKit.SixHaL, Preset(type, preset));
            var b = EventsKit.Plan(type, EventsKit.SixHaL, Preset(type, preset));
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            Assert.True(a.Facts.ContainsKey(signature) || signature == "busBays");
        }

        [Fact]
        public void TheTheatreTierLipsFollowTheTiers()
        {
            var plan = EventsKit.Plan("amphitheatre", EventsKit.OneHa, Preset("amphitheatre", "theatre"), TestKit.RoadAlongBottom(300));
            var orch = plan.Objects.Where(o => o.Category == AssetCategories.Stage).Select(o => o.Position.xz).First();
            int bad = 0, n = 0;
            var cen = new float2((float)plan.Facts["theatreFx"], (float)plan.Facts["theatreFz"]);
            foreach (var o in plan.Objects.Where(o => (o.Asset.Id ?? "").StartsWith("EU_Wall") && o.Elevation < -0.2f && o.Elevation > -0.6f))
            {
                var fwd = new float2(math.sin(o.Yaw), math.cos(o.Yaw));
                var rad = math.normalizesafe(o.Position.xz - cen);
                n++;
                if (math.abs(math.dot(fwd, rad)) > 0.3f) bad++;
            }
            Assert.True(n > 50, "lips " + n);
            Assert.True(bad == 0, bad + " of " + n + " tier lips run across their tier");
        }

        [Fact]
        public void TheTheatreIsASmoothBowlOfTiers()
        {
            var plan = EventsKit.Plan("amphitheatre", EventsKit.OneHa, Preset("amphitheatre", "theatre"), TestKit.RoadAlongBottom(300));
            Assert.True(plan.Facts["theatreRows"] >= 10, "rows " + plan.Facts["theatreRows"]);
            Assert.InRange(plan.Facts["theatreRise"], 2.0, 6.01);
            Assert.All(plan.Terraform, t => Assert.True(t.Sloped));
            Assert.True(plan.Facts["ownGround"] > 0);
        }
    }
}
