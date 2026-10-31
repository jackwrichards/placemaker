// Grand park wave, builder g4: monuments & follies (big statue, folly, obelisk, triumphal gateway,
// sculpture walk, terrace garden) — as park / plaza features with a place and "Path to it", standing
// alone, on memorial parks and formal gardens; scale honoured; terrain blended; graceful degradation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.AddOns.Monuments;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Plaza;
using Placemaker.Core.Tool;
using Placemaker.Tests.Types;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class MonumentsTests
    {
        private readonly ITestOutputHelper _out;
        public MonumentsTests(ITestOutputHelper output) { _out = output; }

        public static readonly string[] Ids = MonumentFeatures.All;
        public static IEnumerable<object[]> IdData() => Ids.Select(i => new object[] { i });
        public static IEnumerable<object[]> PresetData()
        {
            foreach (var id in Ids)
                foreach (var p in GeneratorRegistry.Get(id).Presets)
                    yield return new object[] { id, p.Id };
        }

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

        private static string Errors(IEnumerable<PlanIssue> issues) =>
            string.Join("; ", issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        private static float2[] Ring2(float3[] r) => r.Select(p => p.xz).ToArray();

        private static AreaPlan Alone(string id, SettingsBag settings = null, float w = 100, float h = 80, uint seed = 8) =>
            TestKit.Plan(id, TestKit.Input(TestKit.Rect(w, h), settings, seed));

        private static SettingsBag Bag(params (string key, object value)[] kv)
        {
            var b = new SettingsBag();
            foreach (var (k, v) in kv) b.Values[k] = v;
            return b;
        }

        /// <summary>Objects that are meant to stand astride a walk (arches) are exempt from the path clearance.</summary>
        private static bool Astride(PlannedObject o) => o.Category == AssetCategories.MonumentArch;

        private static void AssertClearOfOwnPaths(AreaPlan plan)
        {
            foreach (var o in plan.Objects.Where(o => o.Role != ObjectRole.Decal && !Astride(o)))
                foreach (var n in plan.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide))
                    Assert.True(TestKit.DistanceToCurve(o.Position.xz, n) >= Composer.DefaultHalfWidth(n.Role) + 0.2f,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} stands on its own path");
        }

        // ───────────── as park features (place + path to it) ─────────────

        [Theory]
        [MemberData(nameof(IdData))]
        public void ComposesInAParkWithAPathToIt(string id)
        {
            var ring = TestKit.Rect(300, 220);
            var recipe = With("park", id);
            // A wide walk (the pre-4b default) keeps the entrance path off the place, so the "path to it" spur exists.
            recipe.Root.Settings.Values["pathWidth"] = "mixed";
            var plan = Compose(recipe, ring, TestKit.RoadAlongBottom(300));
            Assert.True(string.IsNullOrEmpty(Errors(plan.AllIssues())), Errors(plan.AllIssues()));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var place = Ring2(park.Regions.Single(r => r.Key == Amenities.Place(id)).Ring);
            var spec = Amenities.Spec(id);
            Assert.Equal(spec.Length * spec.Width, Poly.Area(place), 1f);
            var m = plan.Layers.Single(l => l.GeneratorId == id);
            Assert.EndsWith("@" + Amenities.Place(id), m.StepPath);
            Assert.True(m.Objects.Count >= 5, id + " placed only " + m.Objects.Count + " objects");
            TestKit.AssertSurfacesAccepted(m);
            foreach (var o in m.Objects.Where(o => o.Role != ObjectRole.Decal))
                Assert.True(Poly.Contains(place, o.Position.xz) || Poly.DistanceToBoundary(place, o.Position.xz) < 0.05f, $"{id} {o.Role} outside its place");
            foreach (var t in m.Terraform)
                foreach (var p in t.Ring) Assert.True(Poly.Contains(place, p.xz) || Poly.DistanceToBoundary(place, p.xz) < 0.1f, $"{id} terraform leaves its place");
            TypeWaveKit06.AssertTerraformBlended(m, ring, _ => 0f);
            // The park keeps its planting out; the monument keeps its objects off the park's paths and its own.
            foreach (var o in park.Objects.Where(o => o.Role != ObjectRole.Decal)) Assert.False(Poly.Contains(place, o.Position.xz), $"park {o.Role} inside the {id}");
            foreach (var o in m.Objects.Where(o => o.Role != ObjectRole.Decal && !Astride(o)))
                foreach (var n in park.Nets.Where(n => n.Role != NetRole.Fence))
                    Assert.True(TestKit.DistanceToCurve(o.Position.xz, n) >= Composer.DefaultHalfWidth(n.Role) + 0.2f, $"{id} {o.Role} on a park path");
            AssertClearOfOwnPaths(m);
            // "Path to it": the park's spur ends at the place, and the monument's own walk starts right there (shared node).
            var ends = park.Nets.Where(n => n.Role != NetRole.Fence).SelectMany(n => new[] { n.A.xz, n.D.xz })
                .Where(p => Poly.DistanceToBoundary(place, p) < 1.2f).ToList();
            Assert.NotEmpty(ends);
            Assert.Contains(m.Nets, n => ends.Any(e => math.distance(e, n.A.xz) < 0.05f || math.distance(e, n.D.xz) < 0.05f));
            Assert.Equal(TestKit.Fingerprint(m), TestKit.Fingerprint(Compose(recipe, ring, TestKit.RoadAlongBottom(300)).Layers.Single(l => l.GeneratorId == id)));
        }

        [Fact]
        public void AllMonumentsFitAGrandParkTogetherWithinBudget()
        {
            var ring = TestKit.Rect(420, 300);
            var recipe = With("park", Ids);
            Compose(recipe, ring, TestKit.RoadAlongBottom(420));   // JIT
            var sw = Stopwatch.StartNew();
            var plan = Compose(recipe, ring, TestKit.RoadAlongBottom(420));
            sw.Stop();
            _out.WriteLine($"park 12.6 ha + 6 monuments: {sw.Elapsed.TotalMilliseconds:0} ms");
            Assert.True(string.IsNullOrEmpty(Errors(plan.AllIssues())), Errors(plan.AllIssues()));
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var places = Ids.Select(id => Ring2(park.Regions.Single(x => x.Key == Amenities.Place(id)).Ring)).ToList();
            for (int i = 0; i < places.Count; i++)
                for (int j = i + 1; j < places.Count; j++)
                    Assert.True(Poly.Intersection(places[i], places[j]).Sum(r => Poly.Area(r.Outer)) < 1f, Ids[i] + " overlaps " + Ids[j]);
            Assert.True(sw.Elapsed.TotalMilliseconds < TimingBudget.Ms(1500), "took " + sw.Elapsed.TotalMilliseconds);
        }

        // ───────────── standing alone (own spot), every preset ─────────────

        [Theory]
        [MemberData(nameof(PresetData))]
        public void EveryPresetPlansAloneAndIsDeterministic(string id, string preset)
        {
            var values = GeneratorRegistry.Get(id).Presets.Single(p => p.Id == preset).Values.Clone();
            foreach (var (w, h) in new[] { (100f, 80f), (160f, 120f) })
            {
                var ring = TestKit.Rect(w, h);
                var plan = TestKit.Plan(id, TestKit.Input(ring, values.Clone(), 8));
                Assert.False(plan.HasErrors, id + "/" + preset + ": " + Errors(plan.Issues));
                Assert.True(plan.Objects.Count >= 5, $"{id}/{preset} placed only {plan.Objects.Count} objects");
                TestKit.AssertObjectsInside(plan, ring);
                TestKit.AssertSurfacesAccepted(plan);
                TypeWaveKit06.AssertTerraformBlended(plan, ring, _ => 0f);
                AssertClearOfOwnPaths(plan);
                Assert.Equal(TestKit.Fingerprint(plan), TestKit.Fingerprint(TestKit.Plan(id, TestKit.Input(ring, values.Clone(), 8))));
            }
            // An L-shaped 6 ha outline and a too-small one: issues, never exceptions or errors.
            var l = TestKit.Plan(id, TestKit.Input(TypeWaveKit06.SixHaL, values.Clone(), 3));
            Assert.False(l.HasErrors);
            TestKit.AssertObjectsInside(l, TypeWaveKit06.SixHaL);
            var tiny = TestKit.Plan(id, TestKit.Input(TestKit.Rect(12, 9), values.Clone(), 3));
            Assert.False(tiny.HasErrors);
        }

        [Fact]
        public void TooTightSpaceShrinksTheLayoutWithANote()
        {
            var plan = Alone(FollyAddOn.LayerId, null, 66, 50);
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.Issues, i => i.Key == MonumentAddOn.K + "Issue.Smaller");
            Assert.NotEmpty(plan.Objects);
        }

        // ───────────── what each one is ─────────────

        [Fact]
        public void BigStatueIsScaledUpOnAPlinthAtTheHeadOfAnAxis()
        {
            var hero = Alone(BigStatueAddOn.LayerId);
            var statue = hero.Objects.First(o => o.Role == ObjectRole.Centerpiece);
            Assert.Equal(2.5f, statue.Scale, 2);
            Assert.True(hero.Facts["plinthHeight"] >= 1f);
            Assert.True(hero.Terraform.Count >= 9, "stepped plinth = annulus pieces + top");
            Assert.All(hero.Terraform, t => Assert.Equal(TerraformKind.Raise, t.Kind));
            Assert.Contains(hero.Nets, n => n.Role == NetRole.PathwayWide);
            // The figure faces down the axis, towards the approach.
            var walk = hero.Nets.First(n => n.Role == NetRole.PathwayWide);
            var far = math.distance(walk.A.xz, statue.Position.xz) > math.distance(walk.D.xz, statue.Position.xz) ? walk.A.xz : walk.D.xz;
            var look = new float2(math.sin(statue.Yaw), math.cos(statue.Yaw));
            Assert.True(math.dot(look, math.normalize(far - statue.Position.xz)) > 0.7f, "statue does not face its approach");
            // Avenue items come in mirrored pairs.
            Assert.True(hero.Facts["avenue"] >= 2 && hero.Facts["avenue"] % 2 == 0);
            var colossus = TestKit.Plan(BigStatueAddOn.LayerId, TestKit.Input(TestKit.Rect(100, 80), GeneratorRegistry.Get(BigStatueAddOn.LayerId).Presets.Single(p => p.Id == "colossus").Values.Clone(), 8));
            Assert.True(colossus.Facts["statueScale"] > hero.Facts["statueScale"] * 1.5);
            var group = Alone(BigStatueAddOn.LayerId, Bag(("kind", "group")));
            Assert.Equal(3.0, group.Facts["figures"]);
            // Small spaces scale the figure down instead of overflowing.
            var tight = TestKit.Plan(BigStatueAddOn.LayerId, TestKit.Input(TestKit.Rect(40, 28), Bag(("size", 500.0)), 8));
            Assert.Contains(tight.Issues, i => i.Key == MonumentAddOn.K + "Issue.ScaledDown");
            Assert.True(tight.Facts["statueScale"] < 5.0);
        }

        [Fact]
        public void FollyHasARotundaALagoonAndAColonnadeYouWalkThrough()
        {
            var plan = Alone(FollyAddOn.LayerId, null, 110, 80);
            Assert.True(plan.Facts["rotundaColumns"] >= 8, "rotunda ring");
            Assert.True(plan.Facts["colonnadeColumns"] >= 12, "colonnade arms");
            Assert.True(plan.Facts["lagoonArea"] > 200);
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Water);
            // The walk is a closed loop: every path end is shared with another path (or is the entry spur's outer end).
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway).ToList();
            Assert.True(paths.Count >= 5);
            int loose = paths.SelectMany(n => new[] { n.A.xz, n.D.xz }).Count(p => paths.Count(o => math.distance(o.A.xz, p) < 0.05f || math.distance(o.D.xz, p) < 0.05f) < 2);
            Assert.True(loose <= 1, loose + " loose path ends");
            // Colonnade columns flank the walk: each lies just beside a path (both sides used).
            var cols = plan.Objects.Where(o => o.Category == AssetCategories.MonumentColumn).ToList();
            var rotundaCentre = plan.Objects.First(o => o.Role == ObjectRole.Centerpiece).Position.xz;
            var arm = cols.Where(c => math.distance(c.Position.xz, rotundaCentre) > 20f).ToList();
            Assert.NotEmpty(arm);
            Assert.All(arm, c => Assert.InRange(paths.Min(n => TestKit.DistanceToCurve(c.Position.xz, n)), 2f, 4.5f));
            // Columns are scaled (monumental), and the walk passes inside the rotunda ring.
            Assert.All(cols, c => Assert.Equal(1.5f, c.Scale, 2));
            Assert.Contains(paths, n => Enumerable.Range(0, 9).Any(k => math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz, rotundaCentre) < 8f));
            // Without a lagoon and arms: a belvedere / temple of columns only.
            var bare = Alone(FollyAddOn.LayerId, Bag(("lagoon", false), ("arms", false)), 110, 80);
            Assert.DoesNotContain(bare.Surfaces, s => s.Role == SurfaceRole.Water);
            Assert.Equal(0.0, bare.Facts["colonnadeColumns"]);
        }

        [Fact]
        public void ObeliskStandsOnAStepedBaseInAPavedCourt()
        {
            var plan = Alone(ObeliskAddOn.LayerId);
            var ob = plan.Objects.Single(o => o.Role == ObjectRole.Centerpiece && o.Category == AssetCategories.MonumentObelisk);
            Assert.Equal(2.5f, ob.Scale, 2);
            Assert.True(plan.Facts["plinthHeight"] > 0.5);
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Paving);
            Assert.True(plan.Facts["hedges"] >= 8);
            foreach (var court in new[] { "diamond", "round" })
                Assert.False(Alone(ObeliskAddOn.LayerId, Bag(("court", court))).HasErrors);
        }

        [Fact]
        public void GatewayStandsAstrideAWalkThatRunsThroughIt()
        {
            var plan = Alone(TriumphalArchAddOn.LayerId);
            var arch = plan.Objects.Single(o => o.Category == AssetCategories.MonumentArch);
            Assert.Equal(2f, arch.Scale, 2);
            var walk = plan.Nets.Where(n => n.Role == NetRole.PathwayWide).ToList();
            Assert.Contains(walk, n => TestKit.DistanceToCurve(arch.Position.xz, n) < 0.5f);
            var pylons = Alone(TriumphalArchAddOn.LayerId, Bag(("kind", "pylons")));
            Assert.Equal(2.0, pylons.Facts["gatePieces"]);
            var propylaea = Alone(TriumphalArchAddOn.LayerId, Bag(("kind", "propylaea")));
            Assert.True(propylaea.Facts["gatePieces"] >= 4);   // the walk through it is the real 8 m wide path (FEEDBACK-4)
        }

        [Fact]
        public void SculptureWalkFramesEachPieceWithABenchFacingIt()
        {
            var plan = Alone(SculptureWalkAddOn.LayerId, null, 120, 60);
            var pieces = plan.Objects.Where(o => o.Category == AssetCategories.MonumentSculpture).ToList();
            Assert.Equal(6, pieces.Count);
            Assert.True(pieces.Select(p => p.Variation).Distinct().Count() == pieces.Count, "sculptures should vary");
            Assert.True(pieces.Select(p => p.Scale).Distinct().Count() > 1, "scale varies");
            var benches = plan.Objects.Where(o => o.Role == ObjectRole.Bench).ToList();
            Assert.True(benches.Count >= 5);
            foreach (var b in benches)
            {
                var nearest = pieces.OrderBy(p => math.distance(p.Position.xz, b.Position.xz)).First();
                var look = new float2(math.sin(b.Yaw), math.cos(b.Yaw));
                Assert.True(math.dot(look, math.normalize(nearest.Position.xz - b.Position.xz)) > 0.9f, "bench does not face its sculpture");
            }
            // Pieces alternate sides of the walk.
            Assert.Equal(6, Alone(SculptureWalkAddOn.LayerId, Bag(("style", "zigzag")), 120, 60).Objects.Count(o => o.Category == AssetCategories.MonumentSculpture));
        }

        [Fact]
        public void TerraceGardenStepsDownToTheCentreWithSpokesRingsAndAnAmphitheatre()
        {
            var plan = Alone(TerraceGardenAddOn.LayerId, null, 100, 80);
            Assert.Equal(3.0, plan.Facts["terraceLevels"]);
            Assert.True(plan.Terraform.Count > 50);
            // Sunken: the centre is the lowest; the change grows towards the middle.
            var centre = plan.Objects.First(o => o.Role == ObjectRole.Centerpiece).Position.xz;
            var near = plan.Terraform.OrderBy(t => math.distance(Poly.Centroid(Ring2(t.Ring)), centre)).First();
            Assert.True(near.TargetHeight < -1f, "centre not sunken: " + near.TargetHeight);
            Assert.All(plan.Terraform, t => Assert.True(t.TargetHeight >= near.TargetHeight - 0.01f));
            // Spokes and ring arcs share nodes: every spoke end touches a ring arc end (or is the entry).
            var paths = plan.Nets.Where(n => n.Role == NetRole.Pathway).ToList();
            Assert.True(paths.Count >= 6 + 2 * 6 - 3);
            var arcs = paths.Where(n => math.distance(n.B.xz, math.lerp(n.A.xz, n.D.xz, 1f / 3f)) > 0.2f).ToList();
            Assert.NotEmpty(arcs);
            int joined = paths.Except(arcs).Count(s => arcs.Any(a => new[] { a.A.xz, a.D.xz }.Any(e => math.distance(e, s.A.xz) < 0.05f || math.distance(e, s.D.xz) < 0.05f)));
            Assert.True(joined >= 6, "spokes not joined to the rings: " + joined);
            // The amphitheatre: seats on rows, facing the stage at the foot.
            Assert.True(plan.Facts["seats"] >= 6, "seats " + plan.Facts["seats"]);
            Assert.Equal(1.0, plan.Facts["stage"]);
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Decorative);
            // A garden mount rises instead (and says why it has no amphitheatre).
            var mount = Alone(TerraceGardenAddOn.LayerId, Bag(("direction", "raised"), ("shape", "circle")), 100, 80);
            Assert.True(mount.Terraform.Max(t => t.TargetHeight) > 1f);
            Assert.Contains(mount.Issues, i => i.Key == MonumentAddOn.K + "Issue.AmphiNeedsSunken");
            // Flat parterre: no terrain at all.
            Assert.Empty(Alone(TerraceGardenAddOn.LayerId, Bag(("direction", "flat"))).Terraform);
        }

        // ───────────── graceful degradation ─────────────

        [Fact]
        public void MonumentsDegradeWhenColumnsArchesAndObelisksAreMissing()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.MonumentColumn] = 0, [AssetCategories.MonumentArch] = 0, [AssetCategories.MonumentObelisk] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                var folly = Alone(FollyAddOn.LayerId, null, 110, 80);
                Assert.False(folly.HasErrors);
                Assert.Contains(folly.Issues, i => i.Key == MonumentAddOn.K + "Issue.NoColumns");
                Assert.True(folly.Objects.Count(o => o.Role == ObjectRole.Tree) >= 8, "living colonnade");
                Assert.Equal(0.0, folly.Facts["arches"]);
                var gate = Alone(TriumphalArchAddOn.LayerId);
                Assert.Contains(gate.Issues, i => i.Key == MonumentAddOn.K + "Issue.NoArch");
                Assert.DoesNotContain(gate.Objects, o => o.Category == AssetCategories.MonumentArch);
                Assert.Equal(2.0, gate.Facts["gatePieces"]);
                var ob = Alone(ObeliskAddOn.LayerId);
                Assert.Contains(ob.Issues, i => i.Key == MonumentAddOn.K + "Issue.NoObelisk");
                Assert.Contains(ob.Objects, o => o.Role == ObjectRole.Centerpiece && o.Category == AssetCategories.MemorialMonument);
            }
            var noStatues = new Dictionary<string, int>
            {
                [AssetCategories.CenterpieceStatue] = 0, [AssetCategories.MemorialMonument] = 0, [AssetCategories.MemorialSculpture] = 0,
                [AssetCategories.Centerpiece] = 0, [AssetCategories.MonumentSculpture] = 0, [AssetCategories.CenterpieceFountain] = 0,
            };
            using (AssetAvailability.Scope(noStatues))
                foreach (var id in new[] { BigStatueAddOn.LayerId, SculptureWalkAddOn.LayerId })
                {
                    var plan = Alone(id);
                    Assert.False(plan.HasErrors);
                    Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue);
                    Assert.DoesNotContain(plan.Objects, o => o.Role == ObjectRole.Centerpiece);
                }
        }

        [Theory]
        [InlineData("Column01", AssetCategories.MonumentColumn)]
        [InlineData("MarblePillar", AssetCategories.MonumentColumn)]
        [InlineData("TriumphalArch01", AssetCategories.MonumentArch)]
        [InlineData("ParkArch_Stone", AssetCategories.MonumentArch)]
        [InlineData("Obelisk01", AssetCategories.MonumentObelisk)]
        [InlineData("AbstractSculpture02", AssetCategories.MonumentSculpture)]
        public void CategoryRulesRecogniseMonumentParts(string name, string category)
        {
            var cats = AssetHeuristics.Classify(name, AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon);
            Assert.Contains(category, cats);
        }

        [Theory]
        [InlineData("ArchiveCabinet")]
        [InlineData("MarchingBand")]
        [InlineData("ColumnLamp01")]
        public void CategoryRulesIgnoreLookAlikes(string name)
        {
            var cats = AssetHeuristics.Classify(name, AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon);
            Assert.DoesNotContain(AssetCategories.MonumentArch, cats);
            Assert.DoesNotContain(AssetCategories.MonumentColumn, cats);
        }

        // ───────────── hosts ─────────────

        [Fact]
        public void MonumentsAreFeaturesOfParkPlazaMemorialParkAndFormalGarden()
        {
            foreach (var host in new[] { "park", "plaza", "memorialPark", "formalGarden" })
            {
                var features = Features.Of(GeneratorRegistry.Get(host)).Select(f => f.Layer).ToList();
                // (Phase 1) the terrace garden is a setting of its hosts, the other monuments add-ons
                foreach (var id in Ids)
                    if (Placemaker.Core.Layers.Common.LookSettings.IsLook(id)) Assert.Contains(GeneratorRegistry.Get(host).Controls, c => c.Id == Placemaker.Core.Layers.Common.LookSettings.ForFeature(id).ControlId);
                    else Assert.Contains(id, features);
            }
            foreach (var id in Ids) Assert.Contains(id, PlazaLayer.Hosted);
        }

        [Theory]
        [InlineData("memorialPark")]
        [InlineData("formalGarden")]
        public void MonumentsFindTheirOwnSpotInHostsWithoutPlaces(string host)
        {
            var ring = TestKit.Rect(320, 240);
            var plan = Compose(With(host, BigStatueAddOn.LayerId, ObeliskAddOn.LayerId, TerraceGardenAddOn.LayerId), ring, TestKit.RoadAlongBottom(320));
            Assert.True(string.IsNullOrEmpty(Errors(plan.AllIssues())), Errors(plan.AllIssues()));
            int built = 0;
            foreach (var id in new[] { BigStatueAddOn.LayerId, ObeliskAddOn.LayerId, TerraceGardenAddOn.LayerId })
            {
                var m = plan.Layers.Single(l => l.GeneratorId == id);
                TestKit.AssertObjectsInside(m, ring);
                if (m.Objects.Count > 0) built++;
            }
            Assert.True(built >= 2, host + ": only " + built + " monuments found room");
        }

        // ───────────── keys ─────────────

        [Fact]
        public void EveryKeyHasEnglishText()
        {
            var lang = TestKit.Lang();
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            foreach (var id in Ids)
            {
                var g = GeneratorRegistry.Get(id);
                Need(g.NameKey);
                foreach (var c in g.Controls)
                {
                    Need(c.LabelKey); Need(c.TooltipKey); Need("Placemaker.UI.Group." + c.Group);
                    foreach (var o in c.Options) Need(o.LabelKey);
                    Assert.True(lang.TryGetValue(c.LabelKey, out var label) && label.Split(' ').Length <= 3, c.LabelKey);
                }
                foreach (var p in g.Presets) Need(p.LabelKey);
                Need("Placemaker.Feature." + id); Need("Placemaker.Feature." + id + ".Tooltip");
                Need(Keys.Category(g.Controls.First(c => c.Kind == ControlKind.Asset || c.Kind == ControlKind.AssetMulti).AssetCategory));
            }
            foreach (var host in new[] { "park", "plaza" })
                foreach (var f in Features.Of(GeneratorRegistry.Get(host)).Where(f => Ids.Contains(f.Layer)))
                    foreach (var o in f.Options)
                    {
                        var c = GeneratorRegistry.Get(host).Controls.Single(x => x.Id == o);
                        Need(c.LabelKey); Need(c.TooltipKey); Need("Placemaker.UI.Group." + c.Group);
                        foreach (var opt in c.Options) Need(opt.LabelKey);
                    }
            foreach (var file in Directory.GetFiles(Path.Combine(TestKit.RepoRoot(), "Core", "Layers", "AddOns", "Monuments"), "*.cs"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                    Need(m.Groups[1].Value);
            foreach (var issue in new[] { "Smaller", "ScaledDown", "NoColumns", "NoArch", "NoObelisk", "WaterNotFlat", "FewerSculptures", "NoRoomForCentre", "AmphiNeedsSunken" })
                Need(MonumentAddOn.K + "Issue." + issue);
            foreach (var c in new[] { AssetCategories.MonumentColumn, AssetCategories.MonumentArch, AssetCategories.MonumentObelisk, AssetCategories.MonumentSculpture })
                Need(Keys.Category(c));
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
        }
    }
}
