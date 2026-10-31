// Playtest 4b #6/#10: typed park entrances (pedestrian, bike, main, service) suggested from the adjacent
// net, planners routing by kind, readiness, and the richer defaults / preset features.

using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class EntranceKindTests
    {
        private static NearbyNet Net(long id, float3 a, float3 b, float width, NetCategory cat, bool bike = false, bool oneWay = false) => new NearbyNet
        {
            ExternalId = id, A = a, B = math.lerp(a, b, 1f / 3f), C = math.lerp(a, b, 2f / 3f), D = b,
            Width = width, Category = cat, HasSidewalk = cat == NetCategory.Road, HasBikeLane = bike, IsOneWay = oneWay,
        };

        private static SettingsBag Bag(params (string k, object v)[] kv)
        {
            var b = new SettingsBag();
            foreach (var (k, v) in kv) b.Values[k] = v;
            return b;
        }

        private static EdgeMarker M(string kind, float x, float z) => new EdgeMarker { Kind = kind, Position = new float3(x, 0, z) };

        // ───────────── suggestion by road type ─────────────

        [Fact]
        public void BigRoadGivesAMainEntranceBikeLanesGiveBikeEntrancesPathsGivePedestrian()
        {
            var site = new SiteContext();
            site.NearbyNets.Add(Net(1, new float3(-30, 0, -13), new float3(230, 0, -13), 24, NetCategory.Road));              // big road, bottom
            site.NearbyNets.Add(Net(2, new float3(207, 0, -30), new float3(207, 0, 180), 12, NetCategory.Road, bike: true));   // bike lanes, right
            site.NearbyNets.Add(Net(3, new float3(-30, 0, 153), new float3(230, 0, 153), 4, NetCategory.Pathway));            // footpath, top
            var input = TestKit.Input(TestKit.Rect(200, 150), Bag(("bikePath", true)), site: site);
            var m = TestKit.Gen("park").SuggestMarkers(input);
            Assert.Contains(m, x => x.Kind == EntranceKinds.Main && x.Position.z < 1f);
            Assert.Contains(m, x => x.Kind == EntranceKinds.Bike && x.Position.x > 199f);
            Assert.True(m.Count(x => x.Kind == EntranceKinds.Main) <= 1);

            // Bike route off: no bike entrances, even facing bike lanes.
            var off = TestKit.Gen("park").SuggestMarkers(TestKit.Input(TestKit.Rect(200, 150), Bag(("bikePath", false)), site: site));
            Assert.DoesNotContain(off, x => x.Kind == EntranceKinds.Bike);
        }

        [Fact]
        public void QuietStreetGivesAServiceEntranceAwayFromTheOthers()
        {
            var input = TestKit.Input(TestKit.Rect(200, 150), site: TestKit.RoadAlongBottom(200));   // 12 m two-way street
            var m = TestKit.Gen("park").SuggestMarkers(input);
            var service = m.Where(x => x.Kind == EntranceKinds.Service).ToList();
            Assert.Single(service);
            foreach (var o in m.Where(x => x.Kind != EntranceKinds.Service))
                Assert.True(math.distance(o.Position.xz, service[0].Position.xz) >= 30f);
            // A small park gets no service entrance.
            var small = TestKit.Gen("park").SuggestMarkers(TestKit.Input(TestKit.Rect(60, 40), site: TestKit.RoadAlongBottom(60)));
            Assert.DoesNotContain(small, x => x.Kind == EntranceKinds.Service);
        }

        [Fact]
        public void SuggestionsKeepAwayFromCornersAndIntersections()
        {
            // An L-shaped frontage round the bottom-left corner, with a road intersection there.
            var site = new SiteContext();
            site.NearbyNets.Add(Net(1, new float3(-7, 0, -7), new float3(160, 0, -7), 12, NetCategory.Road));
            site.NearbyNets.Add(Net(2, new float3(-7, 0, -7), new float3(-7, 0, 110), 12, NetCategory.Road));
            site.NearbyNets.Add(Net(3, new float3(-7, 0, -7), new float3(-60, 0, -7), 12, NetCategory.Road));
            var ring = TestKit.Rect(140, 100);
            var region = LayerRegion.From(ring.Select(p => p.xz).ToArray());
            float clear = Entrances.CornerClear(region);
            foreach (var id in new[] { "park", "campground", "picnicGrounds" })
            {
                var m = TestKit.Gen(id).SuggestMarkers(TestKit.Input(ring, site: site));
                Assert.NotEmpty(m);
                foreach (var x in m)
                    foreach (var c in ring)
                        Assert.True(math.distance(x.Position.xz, c.xz) >= clear - 0.5f, $"{id}: {x.Kind} entrance {x.Position.xz} in the corner {c.xz}");
            }
        }

        // ───────────── routing by kind ─────────────

        private static bool IsBike(PlannedNet n) => n.Asset.Fallbacks != null && n.Asset.Fallbacks.Contains("TwoWayBikePath");

        [Fact]
        public void BikeRouteConnectsTheBikeEntrancesServiceTrackAndWideMainWalk()
        {
            var markers = new List<EdgeMarker>
            {
                M(EntranceKinds.Bike, 0, 100), M(EntranceKinds.Bike, 300, 110), M(EntranceKinds.Pedestrian, 220, 0),
                M(EntranceKinds.Main, 80, 0), M(EntranceKinds.Service, 150, 200),
            };
            var plan = TestKit.Plan("park", TestKit.Input(TestKit.Rect(300, 200), Bag(("bikePath", true)), 5, markers));
            Assert.False(plan.HasErrors);
            var bike = plan.Nets.Where(IsBike).ToList();
            Assert.NotEmpty(bike);
            var ends = bike.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            var loose = ends.Where(e => ends.Count(o => math.distancesq(o, e) < 1e-3f) == 1).ToList();
            Assert.Equal(2, loose.Count);
            Assert.Contains(loose, e => math.distance(e, new float2(0, 100)) < 1f);
            Assert.Contains(loose, e => math.distance(e, new float2(300, 110)) < 1f);
            // Service track reaches the service entrance; the main entrance gets a wide walk and a gateway.
            Assert.Equal(1f, (float)plan.Facts["serviceTracks"]);
            Assert.Contains(plan.Nets, n => n.Category == AssetCategories.TrailPath && (math.distance(n.A.xz, new float2(150, 200)) < 2f || math.distance(n.D.xz, new float2(150, 200)) < 2f));
            Assert.Contains(plan.Nets, n => n.Role == NetRole.PathwayWide && !IsBike(n) && (math.distance(n.A.xz, new float2(80, 0)) < 2f || math.distance(n.D.xz, new float2(80, 0)) < 2f));
            Assert.True(plan.Facts.ContainsKey("gatewayProps"));
            // A pedestrian entrance gets a thin path.
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway && (math.distance(n.A.xz, new float2(220, 0)) < 2f || math.distance(n.D.xz, new float2(220, 0)) < 2f));
        }

        [Fact]
        public void NoBikeEntrancesMeansNoBikeRouteAndAnInfo()
        {
            var markers = new List<EdgeMarker> { M(EntranceKinds.Pedestrian, 0, 100), M(EntranceKinds.Pedestrian, 300, 100) };
            var plan = TestKit.Plan("park", TestKit.Input(TestKit.Rect(300, 200), Bag(("bikePath", true)), 5, markers));
            Assert.DoesNotContain(plan.Nets, IsBike);
            Assert.Contains(plan.Issues, i => i.Key == "Placemaker.Park.Issue.NoBikeEntrances" && i.Severity == IssueSeverity.Info);
        }

        [Fact]
        public void LegacyEntranceMarkersStillCountAsPedestrian()
        {
            var markers = new List<EdgeMarker> { M(EntranceKinds.Legacy, 150, 0) };
            var input = TestKit.Input(TestKit.Rect(300, 200), null, 5, markers);
            Assert.True(TestKit.Gen("park").CheckReady(input).Ready);
            var plan = TestKit.Plan("park", input);
            Assert.Equal(1f, (float)plan.Facts["pedestrianEntrances"]);
        }

        // ───────────── readiness, marker kinds, defaults ─────────────

        [Fact]
        public void ParkFamilyNeedsAnEntranceIndustrialDoesNot()
        {
            foreach (var id in new[] { "park", "campground", "sportsPark", "zenGarden" })
            {
                var r = TestKit.Gen(id).CheckReady(TestKit.Input(TestKit.Rect(100, 80)));
                Assert.False(r.Ready, id);
                Assert.Equal("Placemaker.Entrance.Step.Place", r.NextStepKey);
                Assert.True(TestKit.Gen(id).CheckReady(TestKit.Input(TestKit.Rect(100, 80), markers: new List<EdgeMarker> { M(EntranceKinds.Main, 50, 0) })).Ready, id);
            }
            foreach (var id in new[] { "logisticsYard", "plaza", "roads" })
                Assert.True(TestKit.Gen(id).CheckReady(TestKit.Input(TestKit.Rect(100, 80))).Ready, id);
            // rf-shops3: the market square waits for an entrance like the parking tool (MarketQuarterTests).
            Assert.False(TestKit.Gen("marketSquare").CheckReady(TestKit.Input(TestKit.Rect(100, 80))).Ready);
        }

        [Fact]
        public void MarkerKindsArePublishedWithLabelsTooltipsAndVanillaIcons()
        {
            var kinds = MarkerKinds.For(TestKit.Gen("park"));
            Assert.Equal(EntranceKinds.Park, kinds.Select(k => k.Kind).ToArray());
            Assert.All(kinds, k =>
            {
                Assert.StartsWith("Placemaker.Entrance.", k.LabelKey);
                Assert.EndsWith(".Tip", k.TooltipKey);
                Assert.StartsWith("Media/Game/Icons/", k.Icon);
            });
            Assert.Equal(EntranceKinds.Leisure, MarkerKinds.For(TestKit.Gen("campground")).Select(k => k.Kind).ToArray());
            Assert.Equal(new[] { "entrance" }, MarkerKinds.For(TestKit.Gen("logisticsYard")).Select(k => k.Kind).ToArray());
        }

        [Fact]
        public void FreshTypesStartWithFittingFeatures()
        {
            void On(string type, params string[] layers)
            {
                var f = Features.Of(TestKit.Gen(type));
                foreach (var l in layers) Assert.True(f.Any(x => x.Layer == l && x.DefaultOn), $"{type}: {l} not on by default");
            }
            On("park", "playground", "picnic", "restrooms");
            // (Phase 1) big trees and flowerbeds are the park's look: settings, on by default
            Assert.Equal("some", TestKit.Gen("park").Defaults().S("lookBigTrees", ""));
            Assert.Equal("some", TestKit.Gen("park").Defaults().S("lookFlowerbeds", ""));
            On("plaza", "fountain");   // art pass a2: the square plants its own trees and beds
            On("campground", "restrooms");
            On("sportsPark", "pitch", "tennisCourts", "basketballCourt", "playground");   // s10: the clubhouse has the toilets
            // s20: the waterfront plants its own beds and tree rows (the flower-bed feature scattered planter rows over its lawn).
            Assert.Equal("none", TestKit.Gen("waterfront").Defaults().S("lookFlowerbeds", ""));
            Assert.Equal("none", TestKit.Gen("waterfront").Defaults().S("lookBigTrees", ""));
            // Slow or situational looks stay off.
            foreach (var type in new[] { "park", "plaza", "campground", "botanicalGarden" })
            {
                Assert.Equal("none", TestKit.Gen(type).Defaults().S("lookTerrainShape", "none"));
                Assert.Equal("none", TestKit.Gen(type).Defaults().S("lookRockyAreas", "none"));
            }
        }

        [Fact]
        public void PresetsTurnOnTheirOwnFeatures()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 3);
            editor.AddDefaultFeatures(r, () => 1);
            Assert.Contains(r.Root.Children, c => c.Layer == "playground");
            editor.ApplyPreset(r, RecipePaths.Root, "formal");
            Assert.Contains(r.Root.Children, c => c.Layer == "plaza" && c.Enabled);
            editor.ApplyPreset(r, RecipePaths.Root, "woodland");
            Assert.DoesNotContain(r.Root.Children, c => c.Layer == "playground" && c.Enabled);
        }
    }
}
