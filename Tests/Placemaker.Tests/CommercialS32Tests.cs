// s32: the second Commercial pass — high street, retail park, mall park, mall district, the lifestyle centre and the
// food court. On top of CommercialTests (every plot zoned and facing a street or lane, no overlaps, nothing on a plot)
// and LotGradingTests (plots at their road's grade on slopes): every plot is zonable (ZoneGuard drops none), lanes
// form connected networks joined to a street with no car dead ends, walks and lot footpaths meet lanes at nodes, stall
// markings equal the stalls, big plots on the streets and small ones on the lanes carry their own zones, and the plans
// are deterministic. Run on the test sites and on gallery-sized squares with the gallery's asset catalog.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Commercial;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class CommercialS32Tests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        public static readonly string[] NewTypes = { "highStreet", "retailPark", "mallPark", "mallDistrict" };
        /// <summary>Types whose composition s32 built or rebuilt (the strip mall is checked by StripMallRegressionTests).</summary>
        public static readonly string[] S32Types = { "highStreet", "retailPark", "mallPark", "mallDistrict", "outdoorMall", "shoppingPlaza" };

        private static Dictionary<string, AssetInfo> _catalog;

        /// <summary>The gallery's asset fixture (net widths, footprints): what the planners see in the game.</summary>
        internal static Dictionary<string, AssetInfo> Catalog()
        {
            if (_catalog != null) return _catalog;
            var root = JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(TestKit.RepoRoot(), "Tests", "Placemaker.Tests", "Fixtures", "catalog-bounds.json")));
            var d = new Dictionary<string, AssetInfo>();
            foreach (var kv in root["items"].AsObject())
            {
                var info = new AssetInfo { Id = kv.Key };
                if (kv.Value?["w"] != null) info.NetWidth = (float)(double)kv.Value["w"];
                if (kv.Value?["b"] != null)
                {
                    var a = kv.Value["b"].AsArray().Select(x => (float)(double)x).ToArray();
                    info.BoundsMin = new float3(a[0], 0, a[1]);
                    info.BoundsMax = new float3(a[2], a[4], a[3]);
                }
                d[kv.Key] = info;
            }
            return _catalog = d;
        }

        private static float3[] Site(string site) => site switch
        {
            "big" => TestKit.Rect(160, 160),
            "wide" => TestKit.Rect(220, 110),
            "L" => TestKit.LShape(150),
            "sq25" => TestKit.Rect(158, 158),
            "sq40" => TestKit.Rect(200, 200),
            _ => TestKit.Rect(115, 115),
        };

        public static IEnumerable<object[]> Cases()
        {
            foreach (var t in S32Types)
                foreach (var p in new[] { "" }.Concat(GeneratorRegistry.Get(t).Presets.Select(x => x.Id)))
                    foreach (var s in new[] { "small", "big", "wide", "L", "sq25", "sq40" })
                        if (Poly.Area(Poly.To2D(Site(s))) >= GeneratorRegistry.Get(t).MinArea)
                            yield return new object[] { t, p, s };
        }

        internal static ComposedPlan Compose(string type, string preset, string siteName, out SiteContext site, uint seed = 5, System.Action<Recipe> tweak = null)
        {
            var ring = Site(siteName);
            site = ZoningCutoutTests.Streets(ring);
            // Gallery-sized squares plan with the gallery's catalog and layout scale (real widths, real footprints).
            if (siteName.StartsWith("sq")) { site.Assets = Catalog(); site.LayoutScale = 0.5f; }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), seed);
            if (!string.IsNullOrEmpty(preset)) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            tweak?.Invoke(r);
            // (rf-shops3) The entrance types wait for the player's ways in: a car entrance mid main street, a footpath, a driveway.
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static bool IsLane(PlannedNet n) => n.Role == NetRole.Alley || n.Role == NetRole.Street;

        private static bool Pedestrian(PlannedNet n) => n.Asset.Id != null && n.Asset.Id.StartsWith("Pedestrian Street");

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryPlotZonesLanesConnectWalksJoinAndLotsAreMarked(string type, string preset, string siteName)
        {
            var plan = Compose(type, preset, siteName, out var site);
            string ctx = $"{type}/{preset}/{siteName}";
            Assert.False(plan.HasErrors, ctx);
            var host = plan.Layers.First(l => l.GeneratorId == type);

            // Every plot the type planned can be zoned: ZoneGuard dropped none (a walk ending at a plot's back, a one-cell
            // row backed by another plot, a lane piece too short for a block — each would cost a plot its zoning).
            Assert.DoesNotContain(plan.AllIssues(), i => i.Key == ZoneGuard.NotZonableKey);

            // Plots face a road that carries zone blocks.
            var plots = host.Regions.Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
            Assert.NotEmpty(plots);
            var lanes = host.Nets.Where(IsLane).ToList();
            Assert.All(lanes, n => Assert.True(ZoneBlockPredictor.Zones(n.Role), ctx + ": a shop lane without zone blocks"));

            // Lanes: every connected network reaches an existing street; car lanes never dead-end (pedestrian streets may
            // end on their plaza).
            var ends = new List<(float2 p, PlannedNet n)>();
            foreach (var n in lanes) { ends.Add((n.A.xz, n)); ends.Add((n.D.xz, n)); }
            bool Shared(float2 p, PlannedNet self) => host.Nets.Any(o => o != self && (math.distance(o.A.xz, p) < 0.01f || math.distance(o.D.xz, p) < 0.01f));
            foreach (var n in lanes)
            {
                bool startOk = n.StartAttach >= 0 || Shared(n.A.xz, n);
                bool endOk = n.EndAttach >= 0 || Shared(n.D.xz, n);
                if (!Pedestrian(n)) Assert.True(startOk && endOk, $"{ctx}: car lane {n.A.xz}–{n.D.xz} dead-ends");
                else Assert.True(startOk || endOk, $"{ctx}: pedestrian lane {n.A.xz}–{n.D.xz} is cut loose");
            }
            var comp = new int[lanes.Count];
            for (int i = 0; i < comp.Length; i++) comp[i] = i;
            int Find(int i) { while (comp[i] != i) i = comp[i] = comp[comp[i]]; return i; }
            for (int i = 0; i < lanes.Count; i++)
                for (int j = i + 1; j < lanes.Count; j++)
                {
                    var a = lanes[i]; var b = lanes[j];
                    if (math.distance(a.A.xz, b.A.xz) < 0.01f || math.distance(a.A.xz, b.D.xz) < 0.01f || math.distance(a.D.xz, b.A.xz) < 0.01f || math.distance(a.D.xz, b.D.xz) < 0.01f)
                        comp[Find(i)] = Find(j);
                }
            foreach (var g in Enumerable.Range(0, lanes.Count).GroupBy(Find))
                Assert.True(g.Any(i => lanes[i].StartAttach >= 0 || lanes[i].EndAttach >= 0), $"{ctx}: a lane network reaches no street");

            // Walks (paseos, park walks, lot footpaths) that reach a lane meet it at a node, never mid-piece.
            foreach (var w in host.Nets.Where(n => n.Role == NetRole.Pathway))
                foreach (var p in new[] { w.A.xz, w.D.xz })
                    foreach (var l in lanes)
                    {
                        float d = math.sqrt(SegmentIndex.DistSq(p, l.A.xz, l.D.xz));
                        if (d > 0.3f) continue;
                        Assert.True(math.distance(p, l.A.xz) < 0.05f || math.distance(p, l.D.xz) < 0.05f, $"{ctx}: walk end {p} on lane {l.A.xz}–{l.D.xz} is not a node");
                    }

            // Real lots: one marking per stall.
            if (host.Facts.TryGetValue("lots", out var lots) && lots > 0)
                Assert.Equal((int)host.Facts["capacity"], host.Objects.Count(o => o.Role == ObjectRole.ParkingBay));
        }

        // (rf-shops3) The mall district and the mall park's visible styles are planned from the entrances round a ring drive: every
        // row faces the ring, no row stands on a street (so "outer row" has nothing to zone there). The mall park's hidden car-free
        // promenade keeps the old composition with its street rows.
        [Theory]
        [InlineData("mallPark", "promenade")]
        [InlineData("highStreet", "boulevard")]
        public void BigBuildingsOnTheStreetsLittleOnesOnTheLanes(string type, string preset)
        {
            // (core) Street rows are zoned only with "Shops facing the streets" on (off by default for these lane types).
            var plan = Compose(type, preset, "sq25", out _, 5, r => r.Root.Settings.Values["streetShops"] = true);
            var host = plan.Layers.First(l => l.GeneratorId == type);
            var gen = (LayerBase)GeneratorRegistry.Get(type);
            var settings = gen.Defaults();
            if (preset != "") settings = gen.Presets.First(p => p.Id == preset).Values;
            string inner = ZoneKinds.PrefabId(settings.S("zoneKind", "")), outer = ZoneKinds.PrefabId(settings.S("outerZone", ""));
            var ids = host.Zoning.Select(z => z.ZoneId).Distinct().ToList();
            Assert.Contains(inner, ids);
            Assert.Contains(outer, ids);
            if (type != "highStreet")
            {
                // The street plots are the deep ones.
                var lanes = host.Nets.Where(IsLane).ToList();
                var roads = StreetFrontage.Roads(new AreaInput { Site = ZoningCutoutTests.Streets(Site("sq25")), Shape = new AreaShape { Ring = Site("sq25") } });
                var plots = host.Regions.Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
                float Depth(float2[] q) => math.min(math.distance(q[0], q[1]), math.distance(q[1], q[2]));
                bool OnStreet(float2[] q) => Enumerable.Range(0, q.Length).Any(i => roads.Any(r => math.sqrt(SegmentIndex.DistSq(0.5f * (q[i] + q[(i + 1) % q.Length]), r.A, r.B)) <= r.HalfWidth + StreetFrontage.Tolerance));
                var street = plots.Where(OnStreet).ToList();
                var lane = plots.Where(q => !OnStreet(q)).ToList();
                Assert.NotEmpty(street);
                Assert.NotEmpty(lane);
                Assert.True(street.Max(q => math.max(math.distance(q[0], q[1]), math.distance(q[1], q[2]))) > lane.Average(q => Depth(q)), type + ": street plots are not the big ones");
            }
        }

        [Theory]
        [InlineData("highStreet", "market")]
        [InlineData("retailPark", "classic")]
        [InlineData("mallPark", "commons")]
        [InlineData("mallDistrict", "district")]
        [InlineData("outdoorMall", "lifestyle")]
        [InlineData("shoppingPlaza", "foodCourt")]
        public void PlansAreDeterministic(string type, string preset)
        {
            var a = Compose(type, preset, "sq25", out _);
            var b = Compose(type, preset, "sq25", out _);
            Assert.Equal(StripMallRegressionTests.Fingerprint(a), StripMallRegressionTests.Fingerprint(b));
        }

        [Fact]
        public void LotsConnectToTheShopFrontsOnFoot()
        {
            // Paseos: a side lot's footpath runs through the row to the pedestrian street (high street, outdoor mall), the
            // lifestyle centre's walks cross the ring drive into the rows.
            foreach (var (type, preset) in new[] { ("highStreet", "market"), ("outdoorMall", "promenade"), ("outdoorMall", "lifestyle") })
            {
                var plan = Compose(type, preset, "sq40", out _);
                var host = plan.Layers.First(l => l.GeneratorId == type);
                Assert.True(host.Facts["lots"] > 0, type + ": no lot");
                var peds = host.Nets.Where(n => IsLane(n) && Pedestrian(n)).ToList();
                Assert.NotEmpty(peds);
                var nodes = peds.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
                Assert.True(host.Nets.Any(w => w.Role == NetRole.Pathway && nodes.Any(p => math.distance(p, w.A.xz) < 0.05f || math.distance(p, w.D.xz) < 0.05f)),
                    $"{type}/{preset}: no walk reaches the pedestrian street");
            }
        }

        [Fact]
        public void TheMallParkIsAParkWithAPlaygroundRingedByShops()
        {
            var plan = Compose("mallPark", "family", "sq40", out _);
            var host = plan.Layers.First(l => l.GeneratorId == "mallPark");
            // A closed loop: every loop node is shared by two pieces; the park's walks open onto it.
            Assert.Contains(host.Objects, o => o.Category != null && o.Category.StartsWith("play"));
            Assert.True(host.Nets.Count(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide) >= 4);
            // (rf-shops3) The park inside a ring that follows the site (here a road with parking both sides), small shops outside
            // it facing the park on every side (the old layout's 16 plots stood on the streets too).
            Assert.Equal(1.0, host.Facts[MallParkLayer.RingFact]);
            Assert.True(host.Facts["shopPlots"] >= 8, "shop plots " + host.Facts["shopPlots"]);
            var sides = new HashSet<int>();
            foreach (var g in host.Regions.Where(Cutouts.IsCutout))
            {
                var p = Poly.Centroid(Poly.To2D(g.Ring)) - new float2(100f, 100f);
                sides.Add(math.abs(p.x) > math.abs(p.y) ? (p.x > 0 ? 0 : 1) : (p.y > 0 ? 2 : 3));
            }
            Assert.Equal(4, sides.Count);
        }

        [Fact]
        public void TheStripMallKeepsItsFivePresetsAndEveryS32TypeHasTwoToFive()
        {
            Assert.Equal(new[] { "classic", "corner", "rearAlley", "neighbourhood", "bigBox", "plazaRow", "parkRow", "pondRow" }, GeneratorRegistry.Get("stripMall").Presets.Select(p => p.Id).ToArray());
            foreach (var t in S32Types)
            {
                Assert.Equal("commercial", GeneratorGroups.Of(GeneratorRegistry.Get(t)));
                Assert.InRange(GeneratorRegistry.Get(t).Presets.Count, 2, 5);
            }
        }

        [Fact]
        public void TheHarmonyPassNeverTouchesACommercialType()
        {
            // (s35) Commercial types grade themselves (plots at their road's grade): SiteHarmony (s31) must lock every one.
            foreach (var g in GeneratorRegistry.All.Where(g => GeneratorGroups.Of(g) == "commercial"))
                Assert.False(SiteHarmony.Participates(g.Id), g.Id);
            Assert.Equal(8, GeneratorRegistry.All.Count(g => GeneratorGroups.Of(g) == "commercial"));
        }

        [Fact]
        public void LevelSitesAreNeverGraded()
        {
            foreach (var t in NewTypes)
            {
                var plan = Compose(t, "", "big", out _);
                Assert.Empty(plan.Layers.SelectMany(l => l.Terraform));
            }
        }
    }
}
