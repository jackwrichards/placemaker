// rf-shops3 — Big market as the market quarter (Core/Layers/Leisure/MarketQuarter.cs) and the market's entrances as in the parking tool
// (MarketEntrances). The user: "I want to be able to set entrances and exits almost exactly like I can in the parking lot tool. And it
// wouldn't do anything unless I did." On Big market: "shouldn't it fill the entire area and have more intermingled other things?"

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class MarketQuarterTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static float3[] Circle(float r, int n = 40)
        {
            var ring = new float3[n];
            for (int i = 0; i < n; i++) { float a = i * 2f * math.PI / n; ring[i] = new float3(r + math.cos(a) * r, 0, r + math.sin(a) * r); }
            return ring;
        }

        /// <summary>The gallery's L (280 x 260, the notch to the north-east), CCW.</summary>
        private static float3[] L(float w, float d) => new[]
        {
            new float3(0, 0, 0), new float3(w, 0, 0), new float3(w, 0, d * 0.5f), new float3(w * 0.5f, 0, d * 0.5f), new float3(w * 0.5f, 0, d), new float3(0, 0, d),
        };

        /// <summary>A marker on the outline nearest the point (x, z).</summary>
        private static EdgeMarker At(float3[] ring, string kind, float x, float z)
        {
            var region = LayerRegion.From(ring);
            var p = region.ClosestOnBoundary(new float2(x, z), out int seg, out float t);
            return new EdgeMarker { Kind = kind, RingSegment = seg, RingT = t, Position = new float3(p.x, 0, p.y) };
        }

        private static AreaPlan Market(float3[] ring, string preset, params EdgeMarker[] markers) => Market(ring, preset, null, markers);

        private static AreaPlan Market(float3[] ring, string preset, System.Action<SettingsBag> edit, params EdgeMarker[] markers)
        {
            var site = ZoningCutoutTests.Streets(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("marketSquare"), 7);
            if (!string.IsNullOrEmpty(preset)) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            edit?.Invoke(r.Root.Settings);
            r.Root.Markers.AddRange(markers);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            Assert.False(plan.HasErrors, string.Join(", ", plan.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
            return plan.Layers.First(l => l.GeneratorId == "marketSquare");
        }

        private static List<float2> Stalls(AreaPlan p) => p.Objects.Where(o => o.Category == AssetCategories.MarketStall).Select(o => o.Position.xz).ToList();

        // ───────────── entrances ─────────────

        [Fact]
        public void NothingHappensUntilTheFirstEntranceIsPlaced()
        {
            var gen = TestKit.Gen("marketSquare");
            var ring = TestKit.Rect(134, 124);
            Assert.Empty(gen.SuggestMarkers(TestKit.Input(ring)));
            var none = gen.CheckReady(TestKit.Input(ring));
            Assert.False(none.Ready);
            Assert.Equal("Placemaker.Market.Step.PlaceEntrance", none.NextStepKey);
            Assert.Equal("Placemaker.Market.Reason.NeedsEntrance", none.ReasonKey);
            // Any one of the market's kinds is enough (one gate for people makes a market); another type's kind is not.
            foreach (var kind in MarketEntrances.Kinds)
                Assert.True(gen.CheckReady(TestKit.Input(ring, markers: new List<EdgeMarker> { At(ring, kind, 67, 0) })).Ready, kind);
            Assert.False(gen.CheckReady(TestKit.Input(ring, markers: new List<EdgeMarker> { At(ring, "pedestrian", 67, 0) })).Ready);
            Assert.Equal(1, MarkerKinds.Required(gen, gen.Defaults()));
        }

        [Fact]
        public void TheEntrancesAreTheParkingToolsKindsWithLabelsAndIcons()
        {
            var kinds = MarkerKinds.For(TestKit.Gen("marketSquare"));
            Assert.Equal(MarketEntrances.Kinds, kinds.Select(k => k.Kind).ToArray());
            Assert.Equal("access", kinds[0].Kind);   // "Add an entrance" places a gate for people
            Assert.Equal("Placemaker.Market.Marker.Access", kinds[0].LabelKey);
            foreach (var k in kinds.Skip(1)) Assert.StartsWith("Placemaker.Parking.Marker.", k.LabelKey);
            Assert.All(kinds, k => { Assert.EndsWith(".Tip", k.TooltipKey); Assert.StartsWith("Media/", k.Icon); });
            var lang = TestKit.Lang();
            Assert.All(kinds, k => { Assert.True(lang.ContainsKey(k.LabelKey), k.LabelKey); Assert.True(lang.ContainsKey(k.TooltipKey), k.TooltipKey); });
        }

        [Fact]
        public void ACarEntranceMakesASmallCarParkAtThatEdge()
        {
            var ring = TestKit.Rect(300, 280);
            var p = Market(ring, "bigMarket", At(ring, "access", 150, 0), At(ring, "entrance", 300, 140));
            Assert.Equal(1.0, p.Facts["carParks"]);
            Assert.True(p.Facts.TryGetValue("capacity", out var cap) && cap >= 20, "car park stalls " + cap);
            // The car park's aisles are at that edge, and no stall stands on its land.
            Assert.Contains(p.Nets, n => n.Role == NetRole.DriveAisle && n.A.x > 230f);
            Assert.DoesNotContain(Stalls(p), s => s.x > 280f && math.abs(s.y - 140f) < 20f);
            // People walk from it into the market: a door with bollards on its inner edge.
            Assert.True(p.Facts["gates"] >= 2.0, "gates " + p.Facts["gates"]);
        }

        [Fact]
        public void ADrivewayMakesAServiceLaneFromAJunctionOnTheStreet()
        {
            var ring = TestKit.Rect(300, 280);
            var p = Market(ring, "bigMarket", At(ring, "access", 150, 0), At(ring, "alley", 0, 160));
            Assert.Equal(1.0, p.Facts["serviceLanes"]);
            var lane = Assert.Single(p.Nets, n => n.Role == NetRole.Alley);
            Assert.Equal(503L, lane.StartAttach);   // the west street (ZoningCutoutTests.Streets: 500 + edge index)
            Assert.True(lane.D.x > 10f && lane.D.x < 30f, "lane end " + lane.D);
            Assert.DoesNotContain(Stalls(p), s => s.x < lane.D.x + 2f && math.abs(s.y - 160f) < 4f);
        }

        // ───────────── the market quarter ─────────────

        public static IEnumerable<object[]> Shapes() => new[]
        {
            new object[] { "block" }, new object[] { "big" }, new object[] { "long" }, new object[] { "L" }, new object[] { "circle" },
        };

        private static float3[] Shape(string id)
        {
            switch (id)
            {
                case "big": return TestKit.Rect(300, 280);
                case "long": return TestKit.Rect(380, 110);
                case "L": return L(280, 260);
                case "circle": return Circle(130);
                default: return TestKit.Rect(134, 124);
            }
        }

        [Theory(Skip = "(the plazas makeover, 2026-10-29) the market quarter is retired: every market square is the designed market (PlazaDesigns.Market, DesignedSquaresTests); MarketSquareLayer.DesignedMarket = false brings it back")]
        [MemberData(nameof(Shapes))]
        public void BigMarketFillsTheWholeSiteToItsRim(string shape)
        {
            var ring = Shape(shape);
            var region = LayerRegion.From(ring);
            var p = Market(ring, "bigMarket", At(ring, "access", region.Centroid.x, -100f));
            var stalls = Stalls(p);
            Assert.True(stalls.Count >= region.Area / 70f, $"{shape}: {stalls.Count} stalls on {region.Area:0} m²");
            // No empty field anywhere: every spot of the site is within 22 m of a stall (a circle and an L are filled, not their
            // inscribed rectangle).
            var spots = new List<float2>();
            for (float x = region.Min.x + 5f; x < region.Max.x; x += 10f)
                for (float y = region.Min.y + 5f; y < region.Max.y; y += 10f)
                {
                    var q = new float2(x, y);
                    if (!region.Contains(q) || region.BoundaryDistance(q, 20f) < 8f) continue;
                    float best = stalls.Min(s => math.distancesq(s, q));
                    if (best > 22f * 22f) spots.Add(q);
                }
            Assert.True(spots.Count == 0, $"{shape}: no stall near {string.Join(" ", spots.Take(8).Select(s => $"({s.x:0},{s.y:0})"))}");
            // Things mixed in: the market cross, planting islands or café groves, street trees, food carts.
            Assert.Equal(1.0, p.Facts["marketCross"]);
            Assert.True(p.Facts["plantingIslands"] + p.Facts["cafeGroves"] >= (region.Area > 20000f ? 4 : 2), $"{shape}: islands {p.Facts["plantingIslands"]} groves {p.Facts["cafeGroves"]}");
            Assert.True(p.Facts["streetTrees"] >= region.Area / 600f, $"{shape}: trees {p.Facts["streetTrees"]}");
            Assert.True(p.Facts["foodCarts"] >= 1, $"{shape}: food carts {p.Facts["foodCarts"]}");
        }

        [Fact(Skip = "(the plazas makeover) the market quarter is retired: every market square is the designed market (PlazaDesigns.Market, DesignedSquaresTests); MarketSquareLayer.DesignedMarket = false brings it back")]
        public void TheMarketStreetRunsBetweenTheTwoGates()
        {
            var ring = TestKit.Rect(300, 280);
            var p = Market(ring, "bigMarket", At(ring, "access", 150, 0), At(ring, "access", 150, 280));
            Assert.Equal(0.0, p.Facts["marketStreetCurved"]);
            // Its 10 m are clear of stalls from gate to gate; stalls front it on both sides.
            var stalls = Stalls(p);
            Assert.DoesNotContain(stalls, s => math.abs(s.x - 150f) < 4.5f);
            Assert.True(stalls.Count(s => math.abs(s.x - 150f) < 9f) >= 20, "stalls fronting the market street");
            // Gates offset across the site: the street bends through the market cross.
            var bent = Market(ring, "bigMarket", At(ring, "access", 80, 0), At(ring, "access", 230, 280));
            Assert.True(bent.Facts.ContainsKey("marketStreetCurved"));
        }

        [Fact(Skip = "(the plazas makeover) the market quarter is retired: every market square is the designed market (PlazaDesigns.Market, DesignedSquaresTests); MarketSquareLayer.DesignedMarket = false brings it back")]
        public void EveryGateGetsBikeRacksBollardsAndAFoodCart()
        {
            var ring = TestKit.Rect(300, 280);
            var p = Market(ring, "bigMarket", At(ring, "access", 150, 0));
            var gate = new float2(150, 0);
            Assert.True(p.Objects.Count(o => o.Category == AssetCategories.BikeRack && math.distance(o.Position.xz, gate) < 20f) >= 5, "bike racks");
            Assert.True(p.Objects.Count(o => o.Category == AssetCategories.Bollard && math.distance(o.Position.xz, gate) < 8f) >= 3, "bollards");
            Assert.Contains(p.Objects, o => o.Category == AssetCategories.EventFoodTruck && math.distance(o.Position.xz, gate) < 20f);
            Assert.Contains(p.Objects, o => o.Role == ObjectRole.AccessPoint && math.distance(o.Position.xz, gate) < 1f);
        }

        [Fact(Skip = "(the plazas makeover) the market quarter is retired: every market square is the designed market (PlazaDesigns.Market, DesignedSquaresTests); MarketSquareLayer.DesignedMarket = false brings it back")]
        public void GroundPaintLeavesSoilInThePitsAndLawnInTheIslands()
        {
            var ring = TestKit.Rect(300, 280);
            var painted = Market(ring, "bigMarket", At(ring, "access", 150, 0));
            Assert.Contains(painted.Paint, x => x.Layer == TerrainMaterials.Dirt);
            Assert.Contains(painted.Paint, x => x.Layer == TerrainMaterials.Lush);
            // Off: no paint at all; the islands are lawn surfaces and the pits dark paving instead.
            var plain = Market(ring, "bigMarket", s => s.Values["groundPaint"] = false, At(ring, "access", 150, 0));
            Assert.Empty(plain.Paint);
            Assert.Contains(plain.Surfaces, s => s.Role == SurfaceRole.Grass);
        }

        [Theory]
        [InlineData("small")]
        [InlineData("triangle")]
        [InlineData("sliver")]
        [InlineData("noEntrance")]
        public void OddSitesStillPlanAMarket(string id)
        {
            float3[] ring = id == "small" ? TestKit.Rect(32, 30)
                : id == "triangle" ? new[] { new float3(0, 0, 0), new float3(200, 0, 0), new float3(40, 0, 160) }
                : id == "sliver" ? TestKit.Rect(260, 26)
                : TestKit.Rect(150, 140);
            var p = id == "noEntrance" ? Market(ring, "bigMarket") : Market(ring, "bigMarket", At(ring, "access", 30, 0));
            Assert.True(Stalls(p).Count >= 4, id + ": " + Stalls(p).Count);
            TestKit.AssertObjectsInside(p, ring, 0.6f);
        }

        [Fact]
        public void TheHiddenStylesStillBuild()
        {
            var ring = TestKit.Rect(110, 110);
            foreach (var preset in new[] { "weekly", "farmers", "" })
            {
                var p = Market(ring, preset, At(ring, "access", 55, 0));
                Assert.True(Stalls(p).Count >= 8, preset + ": " + Stalls(p).Count);
            }
        }
    }
}
