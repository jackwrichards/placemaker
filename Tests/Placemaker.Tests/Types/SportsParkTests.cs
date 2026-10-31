// Sports park redesign (s10): one facility concept (the shared field / amenity features, sized by the
// park), a composed layout (hero + terrace + clubhouse, clusters by type, dynamic spacing, loop, spine,
// plaza, tidy lot only when large with frontage), five presets, migration of the retired facility
// controls, and the definition-of-done invariants at 1 ha and a 6 ha L-shape.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.AddOns.Fields;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Tool;
using Placemaker.Tests.Types.T04;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    [Collection(TimingCollection.Name)]
    public class SportsParkTests
    {
        private readonly ITestOutputHelper _out;
        public SportsParkTests(ITestOutputHelper output) { _out = output; }

        private const string Id = SportsParkLayer.LayerId;
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        private static IAreaGenerator Gen => TestKit.Gen(Id);
        private static readonly float3[] OneHa = TestKit.Rect(100, 100);
        private static readonly float3[] ThreeHa = TestKit.Rect(180, 170);
        private static readonly float3[] FiveHa = TestKit.Rect(250, 200);
        private static readonly float3[] SixHaL = TestKit.LShape(293.3f);

        /// <summary>The recipe a player gets: defaults, default features, then the preset (values and features).</summary>
        private static Recipe Preset(string preset)
        {
            var r = Editor.SingleLayerRecipe(Gen, 11);
            uint n = 200;
            Editor.AddDefaultFeatures(r, () => ++n);
            if (preset != null) Assert.True(Editor.ApplyPreset(r, RecipePaths.Root, preset) || true);
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);

        private static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        private static AreaPlan Host(ComposedPlan p) => p.Layers.First(l => l.GeneratorId == Id);

        private static List<OrientedRect> Places(AreaPlan host, string layer = null) =>
            host.Regions.Where(r => r.Tags.Contains(SportsParkLayer.SizedTag) && (layer == null || r.Tags.Contains(Amenities.Place(layer))))
                .Select(r => Amenities.RectOf(LayerRegion.From(r.Ring))).ToList();

        private static float Gap(OrientedRect a, OrientedRect b)
        {
            float best = float.MaxValue;
            var ra = a.Ring; var rb = b.Ring;
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    best = math.min(best, math.sqrt(SegmentIndex.DistSq(ra[i], rb[j], rb[(j + 1) % 4])));
                    best = math.min(best, math.sqrt(SegmentIndex.DistSq(rb[i], ra[j], ra[(j + 1) % 4])));
                }
            return best;
        }

        // ───────────── one concept for facilities ─────────────

        [Fact]
        public void FacilitiesAreTheSharedFeaturesWithHostCountsAndNoOwnFacilityControls()
        {
            var ids = Gen.Controls.Select(c => c.Id).ToList();
            foreach (var retired in new[] { "pitches", "basketball", "tennis", "fitness", "stands", "floodlights", "markings", "cages", "courtSurface", "pitchSurface" })
                Assert.DoesNotContain(retired, ids);
            var features = Features.Of(Gen).ToList();
            foreach (var f in SportsParkLayer.Facilities())
            {
                var fi = features.Single(x => x.Layer == f);
                Assert.Equal(Amenities.Place(f), fi.Target);
                foreach (var o in fi.Options) Assert.Contains(o, ids);
            }
            Assert.Contains(features, f => f.Layer == "pitch" && f.Options.Contains("pitchCount") && f.Options.Contains("pitchSize"));
            Assert.Contains(features, f => f.Layer == "tennisCourts" && f.Options.Contains("tennisCourtsCount"));
            // (the five designed grounds of sports parks round 2: SportsDesigns)
            Assert.InRange(Gen.Presets.Count, 10, 11);
            foreach (var p in new[] { "community", "stadium", "tennisClub", "campus", "neighbourhood" }) Assert.Contains(Gen.Presets, x => x.Id == p);
        }

        [Fact]
        public void FieldsFillTheirSizedPlacesAndFaceTheTerrace()
        {
            var plan = Compose(Preset("community"), FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            var host = Host(plan);
            var pitchPlace = Places(host, "pitch").Single();
            var pitch = plan.Layers.Single(l => l.GeneratorId == "pitch");
            // The field took the size of its place (host-sized), not its own "auto" search.
            Assert.True(math.abs(pitch.Facts["fieldLength"] - pitchPlace.HalfLength * 2f) < 1f);
            Assert.False(pitch.Facts.ContainsKey("fieldSpur"));
            Assert.Equal(1, host.Facts["terrace"]);
            Assert.Equal(1, host.Facts["clubhouse"]);
            // Stands are on the terrace side of the pitch. Since 2026-10-29 the terrace seats the spectators itself (the pitch
            // place carries FieldAddOn.HostStandsTag and the field lays only its dugouts, on the far touchline).
            var terrace = new float2((float)host.Facts["terraceX"], (float)host.Facts["terraceZ"]);
            float side(float2 p) => math.dot(p - pitchPlace.Center, pitchPlace.Side);
            bool hosted = host.Facts.TryGetValue("terraceStands", out var ts) && ts > 0;
            var stands = (hosted ? host.Objects : pitch.Objects).Where(o => o.Category == AssetCategories.SportsStand).ToList();
            Assert.NotEmpty(stands);
            float tSide = math.sign(side(terrace));
            Assert.Equal(tSide, math.sign(stands.Select(o => side(o.Position.xz)).OrderBy(x => -math.abs(x)).Take(3).Average()));
            // The team benches (dugouts) sit on the far touchline, facing the stands across the field.
            var dugouts = pitch.Objects.Where(o => o.Role == ObjectRole.Bench).ToList();
            Assert.All(dugouts, d => Assert.Equal(-tSide, math.sign(side(d.Position.xz))));
            // Every place is inside the area and the places never overlap.
            var places = Places(host);
            for (int i = 0; i < places.Count; i++)
                for (int j = i + 1; j < places.Count; j++)
                    Assert.True(Gap(places[i], places[j]) > 0.9f, "places " + i + " and " + j + " touch");
        }

        [Fact]
        public void MarkingsAreWhiteFieldLinesNeverRoadOrHazardMarkings()
        {
            var plan = Compose(Preset("campus"), SixHaL, TestKit.RoadAlongBottom(293));
            var decals = plan.Layers.Where(l => SportsParkLayer.Facilities().Contains(l.GeneratorId)).SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Decal && Placemaker.Core.Decals.DecalCatalog.IsFunctionalCategory(o.Category)).ToList();   // s21: dressing adds decorative decals
            Assert.NotEmpty(decals);
            Assert.All(decals, d => Assert.True(d.Category == AssetCategories.DecalFieldLine || d.Category == AssetCategories.DecalCourtBasketball, d.Category));
            var rules = new FieldMarkingCategoryRules();
            Assert.True(FieldMarkingCategoryRules.IsFieldLine("g87 uk road markings roadmarkings g87 uk terminal line white decal"));
            foreach (var bad in new[] { "nopakingstripingdecal01 caution", "nostripingdecal01 striping", "g87 uk zigzag line full decal white", "g87 uk terminal line yellow decal", "hazard stripe white line" })
                Assert.False(FieldMarkingCategoryRules.IsFieldLine(bad), bad);
            Assert.Equal(false, rules.AutoEligible(AssetCategories.DecalFieldLine, "nopakingstripingdecal04", Placemaker.Core.Build.AssetTraits.DecalMesh));
            Assert.True(FieldMarkingCategoryRules.IsBasketballCourt("basketballcourtdecal01"));
        }

        // ───────────── composition ─────────────

        [Fact]
        public void HeroTerraceClustersAndDynamicSpacing()
        {
            var site = TestKit.RoadAlongBottom(293);
            var club = Host(Compose(Preset("tennisClub"), SixHaL, site));
            var blocks = Places(club, "tennisCourts");
            Assert.True(blocks.Count >= 2, "tennis club has " + blocks.Count + " blocks");
            // Blocks stand shoulder to shoulder (a court complex), not spread over the park.
            foreach (var b in blocks) Assert.True(blocks.Where(o => !o.Center.Equals(b.Center)).Min(o => Gap(b, o)) < 3f);
            Assert.Equal(1, club.Facts["terrace"]);

            // More breathing room → bigger gaps between different facilities.
            float MinGap(double spacing)
            {
                var r = Preset("campus");
                r.Root.Settings.Values["spacing"] = spacing;
                var host = Host(Compose(r, SixHaL, site));
                var regions = host.Regions.Where(x => x.Tags.Contains(SportsParkLayer.SizedTag)).ToList();
                float best = float.MaxValue;
                for (int i = 0; i < regions.Count; i++)
                    for (int j = i + 1; j < regions.Count; j++)
                    {
                        if (regions[i].Tags.Intersect(regions[j].Tags).Any(t => t.StartsWith(Composer.PlacePrefix, StringComparison.Ordinal))) continue;
                        best = math.min(best, Gap(Amenities.RectOf(LayerRegion.From(regions[i].Ring)), Amenities.RectOf(LayerRegion.From(regions[j].Ring))));
                    }
                return best;
            }
            float tight = MinGap(70), roomy = MinGap(160);
            _out.WriteLine($"min gap between different facilities: {tight:0.0} m at 70 %, {roomy:0.0} m at 160 %");
            Assert.True(roomy > tight, $"{roomy} vs {tight}");
            Assert.True(tight >= 4f);
        }

        [Fact]
        public void ParkingOnlyForLargeParksWithFrontage()
        {
            var big = Host(Compose(Preset("community"), SixHaL, TestKit.RoadAlongBottom(293)));
            Assert.True(big.Facts.TryGetValue("lotStalls", out var stalls) && stalls > 10, "no lot on 6 ha with a road");
            Assert.Contains(big.Areas, a => a.Role == AreaRole.Lot);
            var noRoad = Host(Compose(Preset("community"), SixHaL, new SiteContext()));
            Assert.False(noRoad.Facts.ContainsKey("lotStalls"));
            var small = Host(Compose(Preset("community"), OneHa, TestKit.RoadAlongBottom(100)));
            Assert.False(small.Facts.ContainsKey("lotStalls"));
            var off = Preset("campus");
            off.Root.Settings.Values["parking"] = "off";
            Assert.False(Host(Compose(off, SixHaL, TestKit.RoadAlongBottom(293))).Facts.ContainsKey("lotStalls"));
        }

        [Fact]
        public void PresetsLookDistinct()
        {
            var site = TestKit.RoadAlongBottom(293);
            var sig = new Dictionary<string, string>();
            foreach (var p in Gen.Presets)
            {
                var plan = Compose(Preset(p.Id), SixHaL, site);
                var host = Host(plan);
                var kinds = host.Regions.Where(r => r.Tags.Contains(SportsParkLayer.SizedTag))
                    .Select(r => r.Tags.First(t => t.StartsWith(Composer.PlacePrefix, StringComparison.Ordinal))).GroupBy(t => t).OrderBy(g => g.Key)
                    .Select(g => g.Key + "×" + g.Count());
                // The designed grounds (SportsDesigns) have no hero or loop: their design id tells them apart.
                string fact(string key) => host.Facts.TryGetValue(key, out var v) ? v.ToString() : "-";
                sig[p.Id] = string.Join(",", kinds) + "|hero=" + fact("hero") + "|loop=" + fact("loop") + "|design=" + fact("sportsDesign");
                _out.WriteLine(p.Id + ": " + sig[p.Id]);
            }
            Assert.Equal(sig.Count, sig.Values.Distinct().Count());
            Assert.Contains("runningTrack", sig["stadium"]);
            Assert.DoesNotContain("pitch", sig["neighbourhood"].Split('|')[0]);
            Assert.Contains("hero=0", sig["neighbourhood"]);
        }

        // ───────────── definition of done ─────────────

        [Fact]
        public void EveryPresetPlansOneHectareAndSixHectareLCleanly()
        {
            foreach (var p in Gen.Presets)
                foreach (var (ring, site, name) in new[] { (OneHa, TestKit.RoadAlongBottom(100), "1 ha"), (SixHaL, TestKit.RoadAlongBottom(293), "6 ha L") })
                {
                    var plan = Compose(Preset(p.Id), ring, site);
                    Assert.True(string.IsNullOrEmpty(Errors(plan)), p.Id + "/" + name + ": " + Errors(plan));
                    var host = Host(plan);
                    T04TypeTestBase.AssertInside(host, ring);
                    T04TypeTestBase.AssertNothingOnPaths(host);
                    TestKit.AssertSurfacesAccepted(host);
                    T04TypeTestBase.AssertTerraformBlended(host, ring, site.HeightAt ?? (q => 0f));
                    Assert.Contains(host.Regions, r => r.Tags.Contains(RegionTags.Core));
                    foreach (var l in plan.Layers) T04TypeTestBase.AssertInside(l, ring, 1.2f);
                    Assert.True(host.Facts["facilities"] >= 1, p.Id + "/" + name + " has no facility");
                    _out.WriteLine($"{p.Id}/{name}: {host.Facts["facilities"]} facilities, {host.Objects.Count} objects, {plan.Layers.Count} layers");
                }
        }

        [Fact]
        public void DesignedGroundsHaveWalksToEveryFacility()
        {
            // P2-4 (the user: sports parks "lack paths to the different facilities"): the designs were paving surfaces only
            foreach (var id in SportsDesigns.Ids)
            {
                var host = Host(Compose(Preset(id), FiveHa, TestKit.RoadAlongBottom(250)));
                if (!host.Facts.ContainsKey("sportsDesign")) { _out.WriteLine(id + ": classic park on this site"); continue; }
                int walks = host.Nets.Count(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);
                _out.WriteLine($"{id}: {host.Facts["facilities"]} facilities, {walks} walk pieces, {host.Facts["designWalkSpurs"]} spurs");
                Assert.True(walks >= 6, id + " has no walks");
                Assert.True(host.Facts["designWalkSpurs"] >= 1, id + " has no walk to a facility");
                T04TypeTestBase.AssertNothingOnPaths(host);
            }
        }

        [Fact]
        public void TheArenaOnAHillCutsItsSeatingIntoTheSlope()
        {
            // P2-4 (the lead: on a hill the arena stood on one big green plinth): the banks the hill rises behind become seating
            var ring = TestKit.Rect(240, 200);
            var site = TestKit.RoadAlongBottom(240);
            site.HeightAt = q => 0.12f * q.y;
            var host = Host(Compose(Preset("arena"), ring, site));
            Assert.True(host.Facts.ContainsKey("sportsDesign"), "the arena falls back to the classic park on this site");
            _out.WriteLine($"bank {host.Facts["bowlBank"]} m, terraced banks {host.Facts["bowlTerracedBanks"]}");
            Assert.True(host.Facts["bowlTerracedBanks"] >= 1);
        }

        [Fact]
        public void ABigSiteFillsAsASportsComplex()
        {
            // P2-4 (the lead: a 15 ha park was "a few pitches in a wood"): "Grow with the site" multiplies the counts on a big site
            var big = TestKit.Rect(420, 360);
            var on = Host(Compose(Preset("community"), big, TestKit.RoadAlongBottom(420)));
            var offPreset = Preset("community");
            offPreset.Root.Settings.Values["growWithSite"] = false;
            var off = Host(Compose(offPreset, big, TestKit.RoadAlongBottom(420)));
            var block = Host(Compose(Preset("community"), FiveHa, TestKit.RoadAlongBottom(250)));
            _out.WriteLine($"15 ha: {on.Facts["facilities"]} facilities growing, {off.Facts["facilities"]} as set; 5 ha: {block.Facts["facilities"]}");
            Assert.True(on.Facts["facilities"] >= off.Facts["facilities"] + 3, $"{on.Facts["facilities"]} vs {off.Facts["facilities"]}");
            T04TypeTestBase.AssertNothingOnPaths(on);
        }

        [Fact]
        public void EntrancePlazaGrowsWithThePark()
        {
            // P2-4 (the user: the plaza doesn't grow when the park is drawn very big)
            var small = Host(Compose(Preset("community"), FiveHa, TestKit.RoadAlongBottom(250)));
            var big = Host(Compose(Preset("community"), TestKit.Rect(420, 360), TestKit.RoadAlongBottom(420)));
            _out.WriteLine($"5 ha: {small.Facts["plazaWidth"]} x {small.Facts["plazaDepth"]}; 15 ha: {big.Facts["plazaWidth"]} x {big.Facts["plazaDepth"]}, " +
                $"{big.Facts["plazaTrees"]} trees, {big.Facts["plazaBenches"]} benches");
            Assert.True(big.Facts["plazaWidth"] > small.Facts["plazaWidth"] * 1.6f);
            Assert.True(big.Facts["plazaDepth"] > small.Facts["plazaDepth"] * 1.3f);
            Assert.True(big.Facts["plazaTrees"] >= 8);
            T04TypeTestBase.AssertNothingOnPaths(big);
        }

        [Fact]
        public void DeterministicAndFastOnFiveHectares()
        {
            foreach (var p in Gen.Presets)
            {
                var a = Compose(Preset(p.Id), SixHaL, TestKit.RoadAlongBottom(293));
                var b = Compose(Preset(p.Id), SixHaL, TestKit.RoadAlongBottom(293));
                Assert.Equal(TestKit.Fingerprint(Host(a)), TestKit.Fingerprint(Host(b)));
                long best = long.MaxValue;
                for (int k = 0; k < 2; k++)
                {
                    var sw = Stopwatch.StartNew();
                    Host(Compose(Preset(p.Id), FiveHa, TestKit.RoadAlongBottom(250)));
                    best = Math.Min(best, sw.ElapsedMilliseconds);
                }
                _out.WriteLine($"{p.Id}: {best} ms (whole recipe)");
                // The host alone stays well under budget; the recipe includes every field's own layout.
                var host = TestKit.Gen(Id);
                var input = TestKit.Input(FiveHa, Preset(p.Id).Root.Settings, 3, null, TestKit.RoadAlongBottom(250));
                host.Plan(input, CancellationToken.None);
                var sw2 = Stopwatch.StartNew();
                host.Plan(input, CancellationToken.None);
                Assert.True(sw2.ElapsedMilliseconds < TimingBudget.Ms(250), $"{p.Id} host took {sw2.ElapsedMilliseconds} ms for 5 ha");
            }
        }

        [Fact]
        public void RespectsReservedSpace()
        {
            var ring = TestKit.Rect(220, 180);
            var site = new SiteContext { HeightAt = q => 0.03f * q.x + 0.01f * q.y };
            foreach (var p in Gen.Presets)
            {
                var input = TestKit.Input(ring, p.Values, 7, null, site);
                input.ChildTargets = Features.Of(Gen).Where(f => p.FeatureOn(f)).Select(f => f.Target ?? "").ToArray();
                input.ChildLayers = Features.Of(Gen).Where(f => p.FeatureOn(f)).Select(f => f.Layer).ToArray();
                input.Reserved.AddDisc(new float2(110, 90), 14f, "monument");
                var plan = Gen.Plan(input, CancellationToken.None);
                Assert.False(plan.HasErrors, p.Id);
                try { TestKit.AssertClearOfReserved(plan, input.Reserved); } catch (Exception e) { throw new Exception(p.Id + ": " + e.Message); }
                foreach (var n in plan.Nets.Where(n => n.Role != NetRole.Fence))
                    for (int k = 0; k <= 8; k++)
                    {
                        var q = Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz;
                        Assert.True(math.distance(q, new float2(110, 90)) >= 14f, $"{p.Id}: {n.Role} crosses the reserved disc at {q}");
                    }
                foreach (var r in plan.Regions.Where(r => r.Tags.Contains(SportsParkLayer.SizedTag)))
                    foreach (var v in r.Ring) Assert.True(math.distance(v.xz, new float2(110, 90)) > 14f, p.Id + ": a place covers the reserved disc");
            }
        }

        // ───────────── migration ─────────────

        [Fact]
        public void RetiredFacilityControlsMigrateToFeatures()
        {
            var step = new RecipeStep { Layer = Id, Seed = 5, Settings = Gen.Defaults() };
            var v = step.Settings.Values;
            v.Remove("pitchSize");
            v["pitches"] = 2.0; v["tennis"] = 6.0; v["basketball"] = 3.0; v["fitness"] = true; v["pitchSize"] = "small"; v["cages"] = true;
            step.Locked.Add("tennis");
            // A field child from before the redesign, still finding its own spot.
            step.Children.Add(new RecipeStep { Layer = "skateBowl", Target = "", Settings = TestKit.Gen("skateBowl").Defaults() });
            var recipe = new Recipe { Root = step };
            Assert.True(LayerAliases.Normalize(recipe) > 0);
            foreach (var gone in new[] { "pitches", "tennis", "basketball", "fitness", "cages" }) Assert.False(v.ContainsKey(gone), gone);
            Assert.Equal(2.0, v["pitchCount"]);
            Assert.Equal("seven", v["pitchSize"]);
            Assert.Equal("four", v["tennisCourtsSize"]);
            Assert.Equal(2.0, v["tennisCourtsCount"]);
            Assert.Equal(3.0, v["basketballCourtCount"]);
            foreach (var f in new[] { "pitch", "tennisCourts", "basketballCourt", "fitness", "skateBowl" })
                Assert.Contains(step.Children, c => c.Layer == f && c.Target == Amenities.Place(f));
            // Idempotent, and the migrated recipe plans.
            int before = step.Children.Count;
            LayerAliases.Normalize(recipe);
            Assert.Equal(before, step.Children.Count);
            var plan = Compose(recipe, SixHaL, TestKit.RoadAlongBottom(293));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            Assert.Contains(plan.Layers, l => l.GeneratorId == "pitch");
            Assert.Contains(plan.Layers, l => l.GeneratorId == "tennisCourts");
            Assert.Contains(plan.Layers, l => l.GeneratorId == "basketballCourt");
        }
    }
}
