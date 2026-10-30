using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    /// <summary>Built-in features compose end-to-end (park plaza place, pond, plaza trees…), and the place solver.</summary>
    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class FeatureTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static Recipe With(string layer, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        private static float2[] Ring2(float3[] r) => r.Select(p => p.xz).ToArray();

        private static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        // ───────────── schema ─────────────

        [Fact]
        public void FeaturesReferToRealLayersAndOptions()
        {
            var lang = TestKit.Lang();
            foreach (var g in GeneratorRegistry.All)
                foreach (var f in Features.Of(g))
                {
                    var fg = GeneratorRegistry.Get(f.Layer);
                    Assert.NotNull(fg);
                    if (!string.IsNullOrEmpty(f.LabelKey)) Assert.True(lang.ContainsKey(f.LabelKey), f.LabelKey);
                    if (!string.IsNullOrEmpty(f.TooltipKey)) Assert.True(lang.ContainsKey(f.TooltipKey), f.TooltipKey);
                    foreach (var o in f.Options) Assert.Contains(g.Controls, c => c.Id == o);
                    foreach (var kv in f.Preset.Values) Assert.Contains(fg.Controls, c => c.Id == kv.Key);
                    if (f.Target.Length > 0) Assert.Contains(f.Target, g.Emits);
                }
            Assert.Equal(new[] { "plaza", "playground", "pond", "picnic", "restrooms", "fountain", "bigtrees", "flowerbeds", "shops", "edgeZoning", "rocks", "ground", "terrain", "shore",
                    "runningTrack", "cricketGround", "pitch", "baseballDiamond", "tennisCourts", "basketballCourt", "skateBowl",
                    "volleyball", "basketball", "tableTennis", "chess", "fitness", "splashPad", "bandstand", "dogRun", "noticeboard",
                    "bigStatue", "folly", "obelisk", "triumphalArch", "sculptureWalk", "terraceGarden" },
                Features.Of(GeneratorRegistry.Get("park")).Select(f => f.Layer).ToArray());
            Assert.Contains(Features.Of(GeneratorRegistry.Get("plaza")), f => f.Layer == "plazatrees");
            Assert.Contains(Features.Of(GeneratorRegistry.Get("plaza")), f => f.Layer == "kiosk");
            // s34: the roads type pre-zones its roads itself (control "preZone"); no edge-zoning feature any more.
            Assert.Empty(Features.Of(GeneratorRegistry.Get("roads")));
            Assert.Equal(7, Features.Of(GeneratorRegistry.Get("campground")).Count);   // issue #2: the Decals feature is gone
        }

        [Fact]
        public void EveryControlHasAGroupShortLabelAndEssentialsComeFirst()
        {
            var lang = TestKit.Lang();
            foreach (var g in GeneratorRegistry.All)
            {
                var order = new List<string>();
                foreach (var c in g.Controls)
                {
                    Assert.False(string.IsNullOrEmpty(c.Group), g.Id + "." + c.Id + " has no group");
                    if (c.Group != "Basics") Assert.True(lang.ContainsKey("Placemaker.UI.Group." + c.Group), "group heading " + c.Group);
                    if (!order.Contains(c.Group)) order.Add(c.Group);
                    Assert.True(lang.TryGetValue(c.LabelKey, out var label), c.LabelKey);
                    Assert.True(label.Split(' ').Length <= 3, g.Id + "." + c.Id + " label too long: " + label);
                }
                // The first group holds essentials (UI: essentials first, collapsed "more" groups after).
                var first = order.FirstOrDefault();
                if (first != null && g.Controls.Any(c => c.Tier == ControlTier.Essential))
                    Assert.Contains(g.Controls, c => c.Group == first && c.Tier == ControlTier.Essential);
            }
        }

        [Fact]
        public void SplitAndPondAreHiddenFromTheMakeRow()
        {
            Assert.True(GeneratorRegistry.IsHidden(GeneratorRegistry.Get("split")));
            Assert.True(GeneratorRegistry.IsHidden(GeneratorRegistry.Get("pond")));
            Assert.False(GeneratorRegistry.IsHidden(GeneratorRegistry.Get("park")));
            foreach (var mix in new[] { "split-park-plaza", "roads-with-parks", "park-with-playground", "roads-with-zoning" })
                Assert.False(System.IO.File.Exists(System.IO.Path.Combine(TestKit.RepoRoot(), "Recipes", mix + ".json")), mix);
        }

        // ───────────── park + plaza ─────────────

        [Theory]
        [InlineData("centre", "circle")]
        [InlineData("corner", "square")]
        [InlineData("edge", "rectangle")]
        [InlineData("entrance", "circle")]
        public void ParkWithPlazaComposes(string at, string shape)
        {
            var r = With("park", "plaza");
            r.Root.Settings.Values["plazaAt"] = at;
            r.Root.Settings.Values["plazaShape"] = shape;
            var ring = TestKit.Rect(240, 180);
            var plan = Compose(r, ring, TestKit.RoadAlongBottom(240));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var place = park.Regions.Single(x => x.Key == ParkLayer.PlazaPlace);
            Assert.Contains(RegionTags.Paved, place.Tags);
            var plazaRing = Ring2(place.Ring);
            var area = Poly.Area(plazaRing);
            Assert.InRange(area / (240f * 180f), 0.08f, 0.2f);

            // Planting and furniture avoid the plaza.
            foreach (var o in park.Objects) Assert.False(Poly.Contains(plazaRing, o.Position.xz), o.Role + " inside the plaza at " + o.Position);
            // The plaza layer fills exactly that place.
            var plaza = plan.Layers.Single(l => l.GeneratorId == "plaza");
            Assert.EndsWith("@" + ParkLayer.PlazaPlace, plaza.StepPath);
            Assert.NotEmpty(plaza.Surfaces);
            foreach (var o in plaza.Objects) Assert.True(Poly.Contains(plazaRing, o.Position.xz) || ParkPlaces.OutlineDistance(plazaRing, o.Position.xz) < 0.6f);

            // Paths connect to the plaza edge and never run through it.
            var paths = park.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide).ToList();
            Assert.NotEmpty(paths);
            int joins = paths.Sum(n => (ParkPlaces.OutlineDistance(plazaRing, n.A.xz) < 0.75f ? 1 : 0) + (ParkPlaces.OutlineDistance(plazaRing, n.D.xz) < 0.75f ? 1 : 0));
            Assert.True(joins >= 1, "no path reaches the plaza");
            foreach (var n in paths)
                for (int i = 1; i < 8; i++)
                {
                    var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                    Assert.False(Poly.Contains(plazaRing, p) && ParkPlaces.OutlineDistance(plazaRing, p) > 0.5f, "path crosses the plaza at " + p);
                }
            // Plaza access points sit where the park paths end.
            Assert.Contains(plaza.Objects, o => o.Role == ObjectRole.AccessPoint);
        }

        [Fact]
        public void PlazaPositionFollowsTheRule()
        {
            var ring = TestKit.Rect(240, 180);
            float2 Centre(string at)
            {
                var r = With("park", "plaza");
                r.Root.Settings.Values["plazaAt"] = at;
                var park = Compose(r, ring).Layers.Single(l => l.GeneratorId == "park");
                return Poly.Centroid(Ring2(park.Regions.Single(x => x.Key == ParkLayer.PlazaPlace).Ring));
            }
            var c = Centre("centre");
            Assert.True(math.distance(c, new float2(120, 90)) < 6f, "centre " + c);
            var k = Centre("corner");
            float toCorner = new[] { new float2(0, 0), new float2(240, 0), new float2(0, 180), new float2(240, 180) }.Min(p => math.distance(p, k));
            Assert.True(toCorner < 80f && math.min(k.x, 240 - k.x) < 55f && math.min(k.y, 180 - k.y) < 55f, "corner " + k);
            var e = Centre("edge");
            Assert.True(math.min(math.min(e.x, 240 - e.x), math.min(e.y, 180 - e.y)) < 55f && math.abs(e.x - 120f) < 10f, "edge " + e);
        }

        [Fact]
        public void PlazaPlaceOnlyExistsWhenTheFeatureIsOn()
        {
            var plan = Compose(With("park"), TestKit.Rect(200, 150));
            Assert.DoesNotContain(plan.Layers.Single(l => l.GeneratorId == "park").Regions, x => x.Key == ParkLayer.PlazaPlace);
            var r = With("park", "plaza");
            r.Root.Children[0].Enabled = false;
            plan = Compose(r, TestKit.Rect(200, 150));
            Assert.DoesNotContain(plan.Layers.Single(l => l.GeneratorId == "park").Regions, x => x.Key == ParkLayer.PlazaPlace);
        }

        [Fact]
        public void SiblingFeaturesKeepOutOfThePlaza()
        {
            var r = With("park", "plaza", "playground", "picnic", "fountain", "bigtrees", "flowerbeds", "restrooms");
            var plan = Compose(r, TestKit.Rect(260, 200), TestKit.RoadAlongBottom(260));
            Assert.False(plan.HasErrors, Errors(plan));
            var plazaRing = Ring2(plan.Layers.Single(l => l.GeneratorId == "park").Regions.Single(x => x.Key == ParkLayer.PlazaPlace).Ring);
            foreach (var l in plan.Layers.Where(l => l.GeneratorId != "park" && l.GeneratorId != "plaza"))
                foreach (var o in l.Objects)
                    Assert.False(Poly.Contains(plazaRing, o.Position.xz), l.GeneratorId + " placed " + o.Role + " inside the plaza");
            // No object of one layer sits on an object of another (footprints approximate).
            var all = plan.Layers.SelectMany(l => l.Objects.Select(o => (l.GeneratorId, o))).Where(x => x.o.Role != ObjectRole.AccessPoint).ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    if (all[i].GeneratorId == all[j].GeneratorId) continue;
                    float d = math.distance(all[i].o.Position.xz, all[j].o.Position.xz);
                    Assert.True(d > 0.5f, all[i].GeneratorId + " " + all[i].o.Role + " overlaps " + all[j].GeneratorId + " " + all[j].o.Role);
                }
        }

        [Fact]
        public void ParkWithPlazaIsDeterministicAndFast()
        {
            var ring = TestKit.Rect(300, 220);
            var a = Compose(With("park", "plaza", "playground"), ring);
            var sw = Stopwatch.StartNew();
            var b = Compose(With("park", "plaza", "playground"), ring);
            sw.Stop();
            Assert.Equal(a.Layers.Count, b.Layers.Count);
            for (int i = 0; i < a.Layers.Count; i++) Assert.Equal(TestKit.Fingerprint(a.Layers[i]), TestKit.Fingerprint(b.Layers[i]));
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(4000), "park + plaza took " + sw.ElapsedMilliseconds + " ms");
        }

        // ───────────── park + pond ─────────────

        [Fact]
        public void ParkWithPondHandsTheBasinToThePondLayer()
        {
            var pr = With("park", "pond"); pr.Root.Settings.Values["moods"] = false;   // (parks makeover) the places shape the ground unless switched off
            var plan = Compose(pr, TestKit.Rect(260, 200));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            Assert.Empty(park.Terraform);
            var basin = Ring2(park.Regions.Single(x => x.Key == ParkLayer.PondPlace).Ring);
            var pond = plan.Layers.Single(l => l.GeneratorId == "pond");
            var t = pond.Terraform[0];   // the basin (islands, g2, are raised after it)
            Assert.Equal(TerraformKind.Lower, t.Kind);
            Assert.All(pond.Terraform, x => Assert.True(x.Permanent));
            Assert.True(t.Permanent);
            Assert.Contains(pond.Issues, i => i.Key == "Placemaker.Park.Issue.LakePermanent");
            foreach (var o in park.Objects) Assert.False(Poly.Contains(basin, o.Position.xz), o.Role + " in the pond");
        }

        [Fact]
        public void ParkWithPlazaAndPondDoNotOverlap()
        {
            var plan = Compose(With("park", "plaza", "pond"), TestKit.Rect(320, 240));
            Assert.False(plan.HasErrors, Errors(plan));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var plazaRing = Ring2(park.Regions.Single(x => x.Key == ParkLayer.PlazaPlace).Ring);
            var pond = park.Regions.SingleOrDefault(x => x.Key == ParkLayer.PondPlace);
            if (pond != null)
                foreach (var p in Ring2(pond.Ring)) Assert.False(Poly.Contains(plazaRing, p), "pond shore inside the plaza");
        }

        // ───────────── plaza features ─────────────

        [Theory]
        [InlineData("grid")]
        [InlineData("islands")]
        public void PlazaWithTrees(string layout)
        {
            var r = With("plaza", "plazatrees");
            r.Root.Settings.Values["treeLayout"] = "none";   // the square's own allée off: the feature's trees alone
            r.Root.Children[0].Settings.Values["layout"] = layout;
            var plan = Compose(r, TestKit.Rect(90, 70));
            Assert.False(plan.HasErrors, Errors(plan));
            var trees = plan.Layers.Single(l => l.GeneratorId == "plazatrees");
            Assert.True(trees.Objects.Count(o => o.Role == ObjectRole.Tree) >= 3);
            var plaza = plan.Layers.Single(l => l.GeneratorId == "plaza");
            var reserved = new ReservedSpace();
            Composer.Reserve(plaza, reserved);
            TestKit.AssertClearOfReserved(trees, reserved);
            TestKit.AssertObjectsInside(trees, TestKit.Rect(90, 70));
        }

        [Fact]
        public void PlazaFountainFeatureTakesTheCentre()
        {
            var plan = Compose(With("plaza", "fountain", "kiosk", "flowerbeds", "playground"), TestKit.Rect(110, 90));
            Assert.False(plan.HasErrors, Errors(plan));
            var plaza = plan.Layers.Single(l => l.GeneratorId == "plaza");
            Assert.DoesNotContain(plaza.Objects, o => o.Role == ObjectRole.Centerpiece);
            var fountain = plan.Layers.Single(l => l.GeneratorId == "fountain").Objects.Single(o => o.Role == ObjectRole.Centerpiece);
            Assert.True(math.distance(fountain.Position.xz, new float2(55, 45)) < 12f, "fountain at " + fountain.Position);
            Assert.Contains(plan.Layers, l => l.GeneratorId == "kiosk" && l.Objects.Count > 0);
        }

        [Fact]
        public void CampgroundRestroomsFeatureReplacesBuiltInFacilities()
        {
            var plan = Compose(With("campground", "restrooms", "playground"), TestKit.Rect(200, 150), TestKit.RoadAlongBottom(200));
            Assert.False(plan.HasErrors, Errors(plan));
            Assert.Equal(0, TestKit.Stat(plan.Layers.Single(l => l.GeneratorId == "campground"), Keys.StatRestrooms));
            Assert.Contains(plan.Layers, l => l.GeneratorId == "restrooms" && l.Objects.Count > 0);
        }

        [Fact]
        public void RoadsNeverZoneTheirOwnRoads()
        {
            // s34 "Pre-zone new roads" is retired (the user, 26 Oct: the Zoning category zones): a saved value zones nothing.
            var gen = GeneratorRegistry.Get("roads");
            var recipe = Recipe.Single(gen, 1);
            recipe.Root.Settings.Values[Placemaker.Core.Layers.Roads.RoadsIds.PreZone] = "residentialLow";
            var plan = Composer.Compose(recipe, new AreaShape { Ring = TestKit.Rect(600, 400), Source = AreaSource.Drawn }, new SiteContext(), System.Threading.CancellationToken.None);
            Assert.False(plan.HasErrors, Errors(plan));
            var roads = plan.Layers.Single(l => l.GeneratorId == "roads");
            Assert.Empty(roads.Zoning);
            Assert.DoesNotContain(plan.Layers, l => l.GeneratorId == "edgeZoning");
        }

        // ───────────── place solver ─────────────

        private static float2[] Rect2(float w, float h) => Ring2(TestKit.Rect(w, h));

        private static PlaceRule Rule(PlaceShape s, PlaceAnchor a, float pct = 15f) =>
            new PlaceRule { Shape = s, Anchor = a, SizePct = pct, MinM = 12f, MaxM = 90f, Aspect = 1.6f, Margin = 3f };

        private static void AssertInside(float2[] parent, PlaceResult r, float margin)
        {
            var region = LayerRegion.From(parent);
            foreach (var p in Poly.Resample(r.Ring, 1f)) Assert.True(region.ContainsDisc(p, margin - 0.05f), "place outside at " + p);
        }

        [Theory]
        [InlineData(PlaceShape.Circle, PlaceAnchor.Centre)]
        [InlineData(PlaceShape.Square, PlaceAnchor.Corner)]
        [InlineData(PlaceShape.Rectangle, PlaceAnchor.Edge)]
        [InlineData(PlaceShape.Circle, PlaceAnchor.Entrance)]
        public void SolverFitsAndRespectsTheMargin(PlaceShape shape, PlaceAnchor anchor)
        {
            var parent = Rect2(200, 140);
            var r = PlaceSolver.Solve(parent, null, Rule(shape, anchor), new[] { new float2(100, 0) });
            Assert.True(r.Ok, r.ReasonKey);
            AssertInside(parent, r, 3f);
            Assert.InRange(Poly.Area(r.Ring) / (200f * 140f), 0.1f, 0.17f);
            if (anchor == PlaceAnchor.Entrance) Assert.True(r.Center.y < r.Size * 0.5f + 6f, "not next to the entrance: " + r.Center);
        }

        [Fact]
        public void SolverFailsOnATinyParent()
        {
            var r = PlaceSolver.Solve(Rect2(14, 14), null, Rule(PlaceShape.Square, PlaceAnchor.Centre));
            Assert.False(r.Ok);
            Assert.Equal(PlaceFailure.NoRoom, r.Failure);
            Assert.Equal(PlaceSolver.ReasonNoRoom, r.ReasonKey);
            Assert.Equal(PlaceFailure.InvalidParent, PlaceSolver.Solve(new[] { float2.zero, new float2(1, 0) }, null, Rule(PlaceShape.Circle, PlaceAnchor.Centre)).Failure);
        }

        [Fact]
        public void SolverClampsToMinimumOnSmallParents()
        {
            // 5 % of 30×30 is far below 12 m across: the place grows to the minimum and still fits.
            var r = PlaceSolver.Solve(Rect2(30, 30), null, Rule(PlaceShape.Circle, PlaceAnchor.Centre, 5f));
            Assert.True(r.Ok);
            Assert.True(r.Size >= 12f - 1e-3f);
        }

        [Fact]
        public void SolverShrinksWhenTheWantedSizeDoesNotFit()
        {
            // 40 % of a long thin strip cannot be a circle; it shrinks to the strip's width.
            var parent = Rect2(300, 30);
            var r = PlaceSolver.Solve(parent, null, Rule(PlaceShape.Circle, PlaceAnchor.Centre, 40f));
            Assert.True(r.Ok);
            Assert.True(r.Shrunk);
            AssertInside(parent, r, 3f);
        }

        [Fact]
        public void SolverHandlesConcaveParents()
        {
            var parent = Ring2(TestKit.LShape(200));
            foreach (var anchor in new[] { PlaceAnchor.Centre, PlaceAnchor.Corner, PlaceAnchor.Edge })
            {
                var r = PlaceSolver.Solve(parent, null, Rule(PlaceShape.Square, anchor, 10f));
                Assert.True(r.Ok, anchor + " " + r.ReasonKey);
                AssertInside(parent, r, 3f);
                // The notch never pokes into the place.
                foreach (var v in parent) Assert.False(Poly.Contains(r.Ring, v));
            }
        }

        [Fact]
        public void SolverAvoidsReservedPolygons()
        {
            var parent = Rect2(200, 200);
            var block = Ring2(TestKit.Rect(80, 80, 60, 60));   // the middle is taken
            var r = PlaceSolver.Solve(parent, new[] { block }, Rule(PlaceShape.Circle, PlaceAnchor.Centre, 5f));
            Assert.True(r.Ok);
            foreach (var p in r.Ring) Assert.False(Poly.Contains(block, p));
            foreach (var p in block) Assert.False(Poly.Contains(r.Ring, p));
        }

        [Fact]
        public void SolverIsDeterministicAndFast()
        {
            var parent = Ring2(TestKit.Blob(220, 90, 7));
            var sw = Stopwatch.StartNew();
            PlaceResult first = null;
            foreach (PlaceAnchor a in new[] { PlaceAnchor.Centre, PlaceAnchor.Corner, PlaceAnchor.Edge, PlaceAnchor.Entrance })
                foreach (PlaceShape s in new[] { PlaceShape.Circle, PlaceShape.Square, PlaceShape.Rectangle })
                {
                    var r1 = PlaceSolver.Solve(parent, null, Rule(s, a), new[] { parent[0] }, 5);
                    var r2 = PlaceSolver.Solve(parent, null, Rule(s, a), new[] { parent[0] }, 5);
                    Assert.Equal(r1.Ok, r2.Ok);
                    Assert.Equal(r1.Ring, r2.Ring);
                    first = first ?? r1;
                }
            sw.Stop();
            Assert.True(first.Ok);
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(3000), "solver took " + sw.ElapsedMilliseconds + " ms for 24 solves");
        }

        // ───────────── path clipping ─────────────

        [Fact]
        public void ClipAddsALinkWhenNoPathReachesThePlace()
        {
            var region = LayerRegion.From(Rect2(200, 200));
            var c = new PathCurve { A = new float2(10, 10), B = new float2(40, 10), C = new float2(70, 10), D = new float2(100, 10), Role = NetRole.Pathway, HalfWidth = 1.5f };
            ParkPaths.Sample(c);
            var place = LayerRegion.Circle(new float2(150, 150), 20f, 32);
            var result = ParkPlaces.ClipAndConnect(new List<PathCurve> { c }, place, region, out int joins);
            Assert.Equal(1, joins);
            Assert.Equal(2, result.Count);
            Assert.Equal(1, ParkPlaces.CountJoins(result, place));
        }

        [Fact]
        public void ClipCutsAPathCrossingThePlace()
        {
            var region = LayerRegion.From(Rect2(200, 200));
            var c = new PathCurve { A = new float2(10, 100), B = new float2(70, 100), C = new float2(130, 100), D = new float2(190, 100), Role = NetRole.Pathway, HalfWidth = 1.5f, GateA = 0, GateD = 1 };
            ParkPaths.Sample(c);
            var place = LayerRegion.Circle(new float2(100, 100), 25f, 32);
            var result = ParkPlaces.ClipAndConnect(new List<PathCurve> { c }, place, region, out int joins);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, joins);
            Assert.Equal(2, ParkPlaces.CountJoins(result, place));
            Assert.Equal(0, result[0].GateA);
            Assert.Equal(-1, result[0].GateD);
            Assert.Equal(1, result[1].GateD);
        }
    }
}
