// rf-shops3 — the rules of the shops types planned from the player's entrances (strip mall, mall park, mall district;
// docs/refactor/SHOPS-ARCHITECT.md, "The architect's concepts"): Build waits for a way in for cars, nothing is suggested,
// nothing joins a street but the placed entrances, a round site that touches no street still becomes a shopping place
// (never a pocket park), the ring follows a circle, and the plot planting survives the cutout rule.

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
    public class ShopEntranceRulesTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static IEnumerable<object[]> Types() => new[] { new object[] { "stripMall" }, new object[] { "mallPark" }, new object[] { "mallDistrict" } };

        /// <summary>The visible styles (Recipes/shelf/shops.json), the ones planned from the entrances.</summary>
        public static IEnumerable<object[]> VisibleStyles() => new[]
        {
            new object[] { "stripMall", "classic" }, new object[] { "stripMall", "bigBox" },
            new object[] { "mallPark", "commons" }, new object[] { "mallPark", "garden" }, new object[] { "mallPark", "family" },
            new object[] { "mallDistrict", "district" }, new object[] { "mallDistrict", "dense" },
        };

        private static Recipe Recipe(string type, string preset = "")
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);

        private static string CompositionFact(string type) =>
            type == "stripMall" ? CommercialLayer.LotFact : type == "mallPark" ? MallParkLayer.RingFact : MallDistrictLayer.RingFact;

        // ───────────── Build waits for a way in for cars; nothing is suggested ─────────────

        [Theory]
        [MemberData(nameof(Types))]
        public void BuildWaitsForAWayInForCars(string type)
        {
            var gen = GeneratorRegistry.Get(type);
            Assert.True(ShopTestEntrances.Uses(gen), type + ": no entrances control");
            Readiness R(params string[] kinds) => gen.CheckReady(new AreaInput { Markers = kinds.Select(k => new EdgeMarker { Kind = k }).ToList() });

            var none = R();
            Assert.False(none.Ready, type + ": ready without an entrance");
            Assert.Equal("Placemaker.Commercial.Reason.NeedsEntrance", none.ReasonKey);
            // People only: a footpath or a bus stop is no way in for cars.
            Assert.False(R(ShopEntrances.Footpath).Ready, type + ": a footpath alone");
            Assert.False(R(ShopEntrances.BusStop, ShopEntrances.Footpath).Ready, type + ": a bus stop and a footpath");
            // One-way kinds need one in and one out.
            Assert.False(R(ShopEntrances.Entry).Ready, type + ": a way in alone");
            Assert.False(R(ShopEntrances.Exit).Ready, type + ": a way out alone");
            Assert.False(R(ShopEntrances.AlleyIn).Ready, type + ": a driveway in alone");
            Assert.True(R(ShopEntrances.Entry, ShopEntrances.Exit).Ready, type + ": entry + exit");
            Assert.True(R(ShopEntrances.AlleyIn, ShopEntrances.Exit).Ready, type + ": driveway in + way out");
            // Two-way kinds are enough on their own.
            Assert.True(R(ShopEntrances.TwoWay).Ready, type + ": a two-way entrance");
            Assert.True(R(ShopEntrances.Alley).Ready, type + ": a two-way driveway");
            Assert.True(R(ShopEntrances.Footpath, ShopEntrances.TwoWay).Ready, type + ": footpath + entrance");
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void NothingIsSuggested(string type)
        {
            var ring = TestKit.Rect(160, 160);
            var site = ZoningCutoutTests.Streets(ring);
            var gen = GeneratorRegistry.Get(type);
            var input = new AreaInput { Shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn }, Site = site, Settings = gen.Defaults(), Markers = new List<EdgeMarker>() };
            Assert.Empty(gen.SuggestMarkers(input) ?? new List<EdgeMarker>());
            // The control offers the parking type's kinds.
            var c = gen.Controls.First(x => x.Id == ShopEntrances.ControlId);
            Assert.Equal(ControlKind.Markers, c.Kind);
            foreach (var k in ShopEntrances.Kinds) Assert.Contains(k, c.MarkerKinds);
            // A bus stop is not offered, but one already in a design is still a shops marker.
            Assert.DoesNotContain(ShopEntrances.BusStop, c.MarkerKinds);
            Assert.True(ShopEntrances.IsKind(ShopEntrances.BusStop));
        }

        // ───────────── nothing joins a street but the placed entrances ─────────────

        private static bool NearStreet(SiteContext site, float2 p, float extra)
        {
            foreach (var s in site.NearbyNets)
                for (int i = 0; i < 32; i++)
                {
                    var a = Composer.Bezier(s.A, s.B, s.C, s.D, i / 32f).xz; var b = Composer.Bezier(s.A, s.B, s.C, s.D, (i + 1) / 32f).xz;
                    if (math.sqrt(SegmentIndex.DistSq(p, a, b)) <= s.Width * 0.5f + extra) return true;
                }
            return false;
        }

        /// <summary>
        /// Net ends of the plan that join an existing street: attached ends, and ends at a kerb (a drive or a walk the game
        /// connects). A walk's end stops half its width off the kerb (StreetJoins: its round end must not touch the street).
        /// </summary>
        private static List<(PlannedNet net, float2 at)> StreetJoins(ComposedPlan plan, SiteContext site)
        {
            var list = new List<(PlannedNet, float2)>();
            foreach (var l in plan.Layers.Where(l => l != null))
                foreach (var n in l.Nets)
                {
                    if (n.Role == NetRole.Fence) continue;
                    if (n.StartAttach >= 0) list.Add((n, n.A.xz));
                    if (n.EndAttach >= 0) list.Add((n, n.D.xz));
                    if (n.Role == NetRole.DriveConnector || n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide)
                        foreach (var e in new[] { n.A.xz, n.D.xz })
                            if (NearStreet(site, e, 1.5f + (n.Role == NetRole.DriveConnector ? 0f : Composer.DefaultHalfWidth(n.Role)))) list.Add((n, e));
                }
            return list;
        }

        [Theory]
        [MemberData(nameof(VisibleStyles))]
        public void WithoutEntrancesThePreviewJoinsNoStreet(string type, string preset)
        {
            var ring = TestKit.Rect(160, 160);
            var site = ZoningCutoutTests.Streets(ring);
            var plan = Compose(Recipe(type, preset), ring, site);
            string ctx = $"{type}/{preset}";
            Assert.False(plan.HasErrors, ctx);
            var host = plan.Layers.First(l => l.GeneratorId == type);
            // The plan is still made (the preview): the composition, its lanes and its plots.
            Assert.Equal(1.0, host.Facts.GetValueOrDefault(CompositionFact(type)));
            Assert.Contains(host.Regions, Cutouts.IsCutout);
            Assert.Contains(host.Nets, n => n.Role == NetRole.Alley || n.Role == NetRole.Street);
            // … but nothing joins a street.
            var joins = StreetJoins(plan, site);
            Assert.True(joins.Count == 0, $"{ctx}: {joins.Count} street join(s), e.g. {string.Join("; ", joins.Take(3).Select(j => j.net.Role + " at " + j.at))}");
            Assert.Equal(0.0, host.Facts.GetValueOrDefault("gatesLanded"));
        }

        [Theory]
        [MemberData(nameof(VisibleStyles))]
        public void EveryPlacedEntranceJoinsTheStreetItsKindSays(string type, string preset)
        {
            var ring = TestKit.Rect(160, 160);
            var site = ZoningCutoutTests.Streets(ring);
            var r = Recipe(type, preset);
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            var plan = Compose(r, ring, site);
            string ctx = $"{type}/{preset}";
            var host = plan.Layers.First(l => l.GeneratorId == type);
            Assert.Equal(3.0, host.Facts.GetValueOrDefault("gates"));
            Assert.True(host.Facts.GetValueOrDefault("gatesLanded") == 3.0, $"{ctx}: {host.Facts.GetValueOrDefault("gatesLanded")} of 3 entrances landed; issues {string.Join(",", plan.AllIssues().Select(i => i.Key))}");
            var joins = StreetJoins(plan, site);
            var markers = r.Root.Markers;
            float2 At(string kind) => markers.First(m => m.Kind == kind).Position.xz;
            bool Near(float2 a, float2 b) => math.distance(a, b) < 20f;
            // The car entrance: an invisible drive from the kerb (the street is left as it is).
            Assert.Contains(joins, j => j.net.Role == NetRole.DriveConnector && j.net.StartAttach < 0 && j.net.EndAttach < 0 && Near(j.at, At(ShopEntrances.TwoWay)));
            // The driveway: a real lane junction with the street.
            Assert.Contains(joins, j => (j.net.Role == NetRole.Alley || j.net.Role == NetRole.Street) && (j.net.StartAttach >= 0 || j.net.EndAttach >= 0) && Near(j.at, At(ShopEntrances.Alley)));
            // The footpath: a walk from the sidewalk.
            Assert.Contains(joins, j => (j.net.Role == NetRole.Pathway || j.net.Role == NetRole.PathwayWide) && Near(j.at, At(ShopEntrances.Footpath)));
            // Nothing else joins a street.
            Assert.True(joins.All(j => markers.Any(m => Near(j.at, m.Position.xz))), $"{ctx}: a street join away from every entrance: {string.Join("; ", joins.Where(j => !markers.Any(m => Near(j.at, m.Position.xz))).Select(j => j.net.Role + " at " + j.at))}");
        }

        // ───────────── round sites, sites touching no street ─────────────

        /// <summary>A round site of radius 100 m, 40 corners (Show.ps1 -Shape circle -Size 200), centred at (100, 100).</summary>
        private static float3[] Circle() => G.Circle(100f, 40, new float2(100f, 100f)).Select(p => new float3(p.x, 0, p.y)).ToArray();

        [Theory]
        [MemberData(nameof(Types))]
        public void ACircleTouchingNoStreetStillBecomesAShoppingPlace(string type)
        {
            // The circle's outline lies 8 m off the kerb of the only street (Show.ps1's gap): it touches no street, so the old
            // layouts found no frontage and planted a pocket park. The player's entrance is on the outline nearest the street.
            var ring = Circle();
            var site = new SiteContext();
            site.NearbyNets.Add(new NearbyNet { ExternalId = 77, A = new float3(-60, 0, -14), B = new float3(40, 0, -14), C = new float3(160, 0, -14), D = new float3(260, 0, -14), Width = 12f, Category = NetCategory.Road, HasSidewalk = true });
            Assert.Empty(ShopTestEntrances.Frontage(ring, site));
            var r = Recipe(type);
            ShopTestEntrances.Place(r, ring, site);
            var plan = Compose(r, ring, site);
            Assert.False(plan.HasErrors, type);
            var host = plan.Layers.First(l => l.GeneratorId == type);
            // Never a pocket park, never nothing: the type's own composition, shops round its ring.
            Assert.DoesNotContain(plan.AllIssues(), i => i.Key == CommercialLayer.NoStreetKey || i.Key == CommercialLayer.NoLayoutKey || i.Key == CommercialLayer.FallbackKey);
            Assert.Equal(1.0, host.Facts.GetValueOrDefault(CompositionFact(type)));
            Assert.True(host.Facts.GetValueOrDefault("shopPlots") >= (type == "stripMall" ? 2 : 4), type + ": shop plots " + host.Facts.GetValueOrDefault("shopPlots"));
            Assert.Equal(host.Facts["shopPlots"], host.Regions.Count(Cutouts.IsCutout));
            Assert.NotEmpty(host.Zoning);
            // The strip mall is a lot with shops round it.
            if (type == "stripMall") Assert.True(host.Facts.GetValueOrDefault("lots") > 0 && host.Facts.GetValueOrDefault("capacity") > 0, "strip mall without a lot");
            // The entrance reached the street across the gap: a drive from the kerb.
            Assert.Equal(1.0, host.Facts.GetValueOrDefault("gatesLanded"));
            Assert.Contains(StreetJoins(plan, site), j => j.net.Role == NetRole.DriveConnector);
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void TheRingFollowsACircle(string type)
        {
            if (type == "stripMall") return;   // (shops4) the strip mall has no ring: one lane behind its lot (LotCentre)
            var ring = Circle();
            var site = ZoningCutoutTests.Streets(ring);
            var r = Recipe(type);
            ShopTestEntrances.Place(r, ring, site);
            var plan = Compose(r, ring, site);
            Assert.False(plan.HasErrors, type);
            var host = plan.Layers.First(l => l.GeneratorId == type);
            Assert.Equal(1.0, host.Facts.GetValueOrDefault(CompositionFact(type)));
            Assert.True(host.Facts.GetValueOrDefault("lanes") > 0, type + ": no lane");
            // A ring drive round the site: its lane pieces head every way (not one straight drive).
            var dirs = new HashSet<int>();
            foreach (var n in host.Nets.Where(n => n.Role == NetRole.Alley || n.Role == NetRole.Street))
            {
                var d = n.D.xz - n.A.xz;
                if (math.length(d) < 4f) continue;
                dirs.Add(((int)math.floor((math.atan2(d.y, d.x) + math.PI) / (math.PI / 4f)) + 8) % 8);
            }
            Assert.True(dirs.Count >= 6, $"{type}: lane pieces head {dirs.Count} of 8 ways");
            // Shops on most sides: plot centres in at least 5 of the 8 sectors round the centre.
            var sectors = new HashSet<int>();
            foreach (var g in host.Regions.Where(Cutouts.IsCutout))
            {
                var p = Poly.Centroid(Poly.To2D(g.Ring)) - new float2(100f, 100f);
                sectors.Add(((int)math.floor((math.atan2(p.y, p.x) + math.PI) / (math.PI / 4f)) + 8) % 8);
            }
            Assert.True(sectors.Count >= 5, $"{type}: shops in {sectors.Count} of 8 sectors");
        }

        // ───────────── plot planting ─────────────

        [Fact]
        public void PlotPlantingSurvivesTheCutoutRuleAnOrdinaryTreeDoesNot()
        {
            var plot = new[] { new float2(0, 0), new float2(24, 0), new float2(24, 32), new float2(0, 32) };
            var layer = new AreaPlan { GeneratorId = "stripMall" };
            layer.Regions.Add(new PlannedRegion { Ring = Poly.To3D(plot, _ => 0f), Tags = new[] { StreetFrontage.ShopsTag, LayerBase.ShopsPlace }, Key = "shops:0" });
            PlannedObject O(ObjectRole role, float x, float z, string cat) => new PlannedObject { Role = role, Position = new float3(x, 0, z), Asset = new AssetRef(""), Category = cat };
            var plotTree = O(ObjectRole.Tree, 12, 29, Mall.PlotTree);
            var plotBush = O(ObjectRole.Bush, 1.3f, 30.7f, Mall.PlotBush);
            var tree = O(ObjectRole.Tree, 12, 16, AssetCategories.Tree);
            var bench = O(ObjectRole.Bench, 6, 10, null);
            var outside = O(ObjectRole.Tree, 40, 16, AssetCategories.Tree);
            layer.Objects.AddRange(new[] { plotTree, plotBush, tree, bench, outside });
            var plan = new ComposedPlan();
            plan.Layers.Add(layer);
            Cutouts.Enforce(plan);
            Assert.Contains(plotTree, layer.Objects);
            Assert.Contains(plotBush, layer.Objects);
            Assert.Contains(outside, layer.Objects);
            Assert.DoesNotContain(tree, layer.Objects);
            Assert.DoesNotContain(bench, layer.Objects);
        }

        [Fact]
        public void AComposedStripMallKeepsItsPlotTreesOnThePlots()
        {
            var ring = TestKit.Rect(160, 160);
            var site = ZoningCutoutTests.Streets(ring);
            var r = Recipe("stripMall", "classic");
            r.Root.Settings.Values["plotTrees"] = true;   // (shops4) off by default now: the player's extra
            ShopTestEntrances.Place(r, ring, site, footpath: true);
            var plan = Compose(r, ring, site);
            var host = plan.Layers.First(l => l.GeneratorId == "stripMall");
            var plots = host.Regions.Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
            var planted = host.Objects.Where(Cutouts.KeepsOnPlot).ToList();
            Assert.True(host.Facts.GetValueOrDefault("plotTrees") > 0, "no plot trees");
            Assert.Contains(planted, o => o.Category == Mall.PlotTree);
            // After the whole recipe ran (Cutouts.Enforce, ZoneGuard): every plot tree and shrub is still there, on a plot.
            Assert.Equal((int)(host.Facts["plotTrees"] + host.Facts["plotBushes"]), planted.Count);
            Assert.All(planted, o => Assert.Contains(plots, q => Poly.Contains(q, o.Position.xz)));
        }
    }
}
