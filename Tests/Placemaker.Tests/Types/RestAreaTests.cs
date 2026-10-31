// Type wave: rest area (group "transport"). Shared checks for the transport types live in
// TransportTypeChecks below (TransitPlazaTests uses them too).

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
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests.Types
{
    public static class TransportTypeChecks
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>1 ha square and a 6 ha L-shape, both with a road along the bottom edge.</summary>
        public static readonly float3[] OneHa = TestKit.Rect(100, 100);
        public static readonly float3[] SixHaL = TestKit.LShape(293.3f);
        public static readonly float3[] FiveHa = TestKit.Rect(250, 200);

        public static Recipe With(string layer, SettingsBag values = null, params string[] features)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), 11);
            if (values != null) foreach (var kv in values.Values) r.Root.Settings.Values[kv.Key] = kv.Value;
            uint seed = 100;
            foreach (var f in features) Assert.True(Editor.SetFeature(r, "0", f, true, seed++), "feature " + f + " of " + layer);
            return r;
        }

        public static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null) =>
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site ?? new SiteContext(), CancellationToken.None);

        public static SiteContext Road(float3[] ring)
        {
            float w = ring.Max(p => p.x);
            return TestKit.RoadAlongBottom(w);
        }

        public static string Errors(ComposedPlan p) =>
            string.Join("; ", p.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key + " " + string.Join(",", i.Args)));

        public static void AssertInside(AreaPlan plan, float3[] ring)
        {
            var r = Poly.To2D(ring);
            foreach (var o in plan.Objects)
                Assert.True(Poly.Contains(r, o.Position.xz) || Poly.DistanceToBoundary(r, o.Position.xz) < 0.05f,
                    $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} lies outside the area");
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {n.Role} end {p.xz} outside");
            foreach (var s in plan.Surfaces)
                foreach (var p in s.Ring)
                    Assert.True(Poly.Contains(r, p.xz) || Poly.DistanceToBoundary(r, p.xz) < 0.5f, $"{plan.GeneratorId}: {s.Role} surface point {p.xz} outside");
        }

        /// <summary>No placed object (decals and markers excepted) sits on a footpath of the same plan.</summary>
        public static void AssertNothingOnPaths(AreaPlan plan)
        {
            foreach (var o in plan.Objects)
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint || o.Role == ObjectRole.AnimalSpawner || o.Role == ObjectRole.ParkingBay
                    || o.Role == ObjectRole.Arrow) continue;
                foreach (var n in plan.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + 0.3f,
                        $"{plan.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        public static void EveryPresetPlansOneAndSixHectares(string id)
        {
            var gen = TestKit.Gen(id);
            Assert.InRange(gen.Presets.Count, 4, 6);
            foreach (var ring in new[] { OneHa, SixHaL })
                foreach (var p in gen.Presets)
                {
                    var plan = Compose(With(id, p.Values), ring, Road(ring));
                    Assert.True(string.IsNullOrEmpty(Errors(plan)), id + "/" + p.Id + ": " + Errors(plan));
                    var layer = plan.Layers.Single(l => l.GeneratorId == id);
                    Assert.True(layer.Objects.Count > 15, $"{id}/{p.Id} placed only {layer.Objects.Count} objects");
                    AssertInside(layer, ring);
                    AssertNothingOnPaths(layer);
                    TestKit.AssertSurfacesAccepted(layer);
                    Assert.Contains(layer.Regions, r => r.Tags.Contains(RegionTags.Core));
                }
        }

        public static void Deterministic(string id)
        {
            foreach (var p in TestKit.Gen(id).Presets)
            {
                var a = Compose(With(id, p.Values), SixHaL, Road(SixHaL));
                var b = Compose(With(id, p.Values), SixHaL, Road(SixHaL));
                Assert.Equal(TestKit.Fingerprint(a.Layers[0]), TestKit.Fingerprint(b.Layers[0]));
            }
        }

        public static void RespectsReserved(string id)
        {
            var gen = TestKit.Gen(id);
            var reserved = new ReservedSpace();
            reserved.AddDisc(new float2(50, 60), 6f, "test");
            reserved.AddCorridor(new float2(20, 30), new float2(90, 85), 2f, "test");
            foreach (var p in gen.Presets)
            {
                var input = TestKit.Input(OneHa, p.Values, 3, null, Road(OneHa));
                input.Reserved = reserved;
                var plan = gen.Plan(input, CancellationToken.None);
                Assert.False(plan.HasErrors, id + "/" + p.Id);
                foreach (var o in plan.Objects)
                {
                    if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.ParkingBay || o.Role == ObjectRole.Arrow) continue;
                    float r = 0.3f;
                    Assert.True(math.distance(o.Position.xz, new float2(50, 60)) >= 6f + r, $"{id}/{p.Id}: {o.Role}/{o.Category} at {o.Position.xz} in reserved disc");
                    Assert.True(math.sqrt(SegmentIndex.DistSq(o.Position.xz, new float2(20, 30), new float2(90, 85))) >= 2f + r,
                        $"{id}/{p.Id}: {o.Role}/{o.Category} at {o.Position.xz} on reserved corridor");
                }
            }
        }

        public static void Fast(string id, ITestOutputHelper output)
        {
            foreach (var p in TestKit.Gen(id).Presets)
            {
                Compose(With(id, p.Values), FiveHa, Road(FiveHa));   // JIT
                var sw = Stopwatch.StartNew();
                Compose(With(id, p.Values), FiveHa, Road(FiveHa));
                sw.Stop();
                output.WriteLine($"{id}/{p.Id}: {sw.ElapsedMilliseconds} ms");
                Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(250), $"{id}/{p.Id} took {sw.ElapsedMilliseconds} ms for 5 ha");
            }
        }

        public static void FeaturesCompose(string id)
        {
            var features = Features.Of(GeneratorRegistry.Get(id)).Select(f => f.Layer).ToArray();
            Assert.NotEmpty(features);
            var plan = Compose(With(id, null, features), FiveHa, Road(FiveHa));
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            foreach (var f in features) Assert.Contains(plan.Layers, l => l.GeneratorId == f);
        }

        public static void LabelsExist(string id, string sourceFolder, string recipeFile)
        {
            var lang = TestKit.Lang();
            var g = TestKit.Gen(id);
            var missing = new List<string>();
            void Need(string key) { if (!string.IsNullOrEmpty(key) && !lang.ContainsKey(key)) missing.Add(key); }
            Need(g.NameKey);
            foreach (var c in g.Controls)
            {
                Need(c.LabelKey); Need(c.TooltipKey);
                if (!string.IsNullOrEmpty(c.Group) && c.Group != "Basics") Need("Placemaker.UI.Group." + c.Group);
                foreach (var o in c.Options) { Need(o.LabelKey); Need(o.TooltipKey); }
                Assert.True(!lang.TryGetValue(c.LabelKey, out var label) || label.Split(' ').Length <= 3, c.LabelKey + " label too long");
            }
            foreach (var p in g.Presets) Need(p.LabelKey);
            foreach (var f in Features.Of(g)) { Need(f.LabelKey); Need(f.TooltipKey); }
            var root = TestKit.RepoRoot();
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Core", "Layers", "Types", sourceFolder), "*.cs"))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(Placemaker\\.[A-Za-z0-9_.:]+[A-Za-z0-9])\""))
                    Need(m.Groups[1].Value);
            foreach (var f in typeof(AssetCategories).GetFields())
            {
                var cat = (string)f.GetValue(null);
                if (cat.StartsWith("transport.", StringComparison.Ordinal)) Need(Keys.Category(cat));
            }
            var recipe = TestKit.LoadRecipe(recipeFile);
            Assert.Equal(id, recipe.Root.Layer);
            Need(recipe.NameKey);
            Need("Placemaker.UI.MakeGroup.Transport");
            Assert.Equal("transport", GeneratorGroups.Of(g));
            Assert.True(missing.Count == 0, "Missing en-US keys: " + string.Join(", ", missing.Distinct()));
            Assert.True(File.Exists(Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II\Cities2_Data\Content\Game\UI", recipe.Icon.Replace('/', '\\')))
                || !Directory.Exists(@"C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II"), "icon " + recipe.Icon);
        }

        public static void ControlCountAndGroups(string id)
        {
            var g = TestKit.Gen(id);
            Assert.InRange(g.Controls.Count(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")), 15, 40);   // (Phase 1) look settings not counted
            var groups = g.Controls.Where(c => !Placemaker.Core.Decals.DressingControls.IsDressingId(c.Id) && !c.Id.StartsWith("look")).Select(c => c.Group).Distinct().ToList();   // (Phase 1) look settings not counted
            Assert.InRange(groups.Count(x => x != "entrances" && x != "assets"), 4, 8);
            Assert.Contains(g.Controls, c => c.Id == "style" && c.Kind == ControlKind.Choice && c.Options.Count == g.Presets.Count);
        }

        public static void MissingCategoriesDegradeToInfo(string id, Dictionary<string, int> none)
        {
            using (AssetAvailability.Scope(none))
                foreach (var p in TestKit.Gen(id).Presets)
                {
                    var plan = Compose(With(id, p.Values), FiveHa, Road(FiveHa));
                    Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
                    Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Layers[0].Objects, o => none.ContainsKey(o.Category ?? "") && o.Role != ObjectRole.Decal);
                }
        }

        public static AreaPlan Preset(string id, string preset, float3[] ring = null, SiteContext site = null)
        {
            ring = ring ?? FiveHa;
            var p = TestKit.Gen(id).Presets.Single(x => x.Id == preset);
            return Compose(With(id, p.Values), ring, site ?? Road(ring)).Layers[0];
        }
    }

    [Collection(Placemaker.Tests.TimingCollection.Name)]
    public class RestAreaTests
    {
        private const string Id = "restArea";
        private readonly ITestOutputHelper _out;
        public RestAreaTests(ITestOutputHelper output) { _out = output; }

        [Fact] public void EveryPresetPlansOneAndSixHectares() => TransportTypeChecks.EveryPresetPlansOneAndSixHectares(Id);
        [Fact] public void IsDeterministic() => TransportTypeChecks.Deterministic(Id);
        [Fact] public void RespectsReserved() => TransportTypeChecks.RespectsReserved(Id);
        [Fact] public void IsFastOnFiveHectares() => TransportTypeChecks.Fast(Id, _out);
        [Fact] public void EveryFeatureComposes() => TransportTypeChecks.FeaturesCompose(Id);
        [Fact] public void EveryLabelHasEnglishText() => TransportTypeChecks.LabelsExist(Id, "RestArea", "restArea.json");
        [Fact] public void ControlsAreDeepButTidy() => TransportTypeChecks.ControlCountAndGroups(Id);

        [Fact]
        public void HasACarParkAmenitiesAndAPath()
        {
            var plan = TransportTypeChecks.Preset(Id, "highway");
            Assert.True(plan.Facts["capacity"] > 20, "stalls " + plan.Facts["capacity"]);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.DriveAisle);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Restroom);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Vending);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Picnic);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Wayfinding);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Fence);   // dog walk
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Paving);
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Asphalt);
        }

        [Fact]
        public void PresetsLookDifferent()
        {
            var highway = TransportTypeChecks.Preset(Id, "highway");
            var picnic = TransportTypeChecks.Preset(Id, "picnic");
            var truck = TransportTypeChecks.Preset(Id, "truck");
            var overlook = TransportTypeChecks.Preset(Id, "overlook", null, Sloped());
            var ev = TransportTypeChecks.Preset(Id, "ev");
            int Cat(AreaPlan p, string c) => p.Objects.Count(o => o.Category == c);
            Assert.True(Cat(picnic, AssetCategories.Picnic) > 2 * Cat(truck, AssetCategories.Picnic), "picnic stop has more tables");
            Assert.True(Cat(truck, AssetCategories.FuelPump) >= 2, "truck stop fuel island");
            Assert.Equal(0, Cat(highway, AssetCategories.FuelPump));
            Assert.True(truck.Facts["capacity"] < highway.Facts["capacity"] * 3 && truck.Facts["capacity"] > 0);
            Assert.Contains(overlook.Objects, o => o.Category == AssetCategories.Lookout);
            Assert.NotEmpty(overlook.Terraform);
            Assert.Empty(highway.Terraform);
            Assert.True(ev.Facts["evCapacity"] > highway.Facts["evCapacity"] + 3, "EV stop has chargers");
            Assert.Contains(ev.Objects, o => o.Category == AssetCategories.CafeSeating);
            // The truck stop's stalls are long truck bays.
            Assert.True(truck.Facts["capacity"] / truck.Stats.Where(s => s.Key == "Placemaker.Parking.Stat.Area").Sum(s => s.Value)
                < highway.Facts["capacity"] / highway.Stats.Where(s => s.Key == "Placemaker.Parking.Stat.Area").Sum(s => s.Value));
        }

        private static SiteContext Sloped()
        {
            var site = TransportTypeChecks.Road(TransportTypeChecks.FiveHa);
            site.HeightAt = p => 0.02f * p.x + 0.01f * p.y;
            return site;
        }

        [Fact]
        public void ViewpointMoundIsBlendedAndObjectsStandOnIt()
        {
            var site = Sloped();
            var plan = TransportTypeChecks.Preset(Id, "overlook", null, site);
            Assert.NotEmpty(plan.Terraform);
            Assert.Contains(plan.Issues, i => i.Key == TerrainShaper.PermanentKey);
            var ring = Poly.To2D(TransportTypeChecks.FiveHa);
            foreach (var t in plan.Terraform)
            {
                Assert.True(t.Permanent);
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                if (plan.Areas.Any(a => a.Role == AreaRole.Lot && Poly.Contains(Poly.To2D(a.Ring), c))) continue;   // s58: the car park grades itself (level tiers), its terraform is merged now
                float edge = Poly.DistanceToBoundary(ring, c);
                float change = math.abs(t.TargetHeight - site.HeightAt(c));
                Assert.True(change < edge * 0.3f + 0.3f, $"{change:0.00} m change {edge:0.0} m from the edge");
                Assert.True(change <= 8.01f);
            }
            // The lookout stands on the raised ground, not on the original terrain.
            var look = plan.Objects.First(o => o.Category == AssetCategories.Lookout);
            Assert.True(look.Position.y > site.HeightAt(look.Position.xz) + 1f, "lookout on the mound");
        }

        [Fact]
        public void WithoutCarParkTheStopStillPlans()
        {
            var values = TestKit.Gen(Id).Defaults();
            values.Values["lotShare"] = 0.0;
            var plan = TransportTypeChecks.Compose(TransportTypeChecks.With(Id, values), TransportTypeChecks.OneHa, TransportTypeChecks.Road(TransportTypeChecks.OneHa));
            Assert.True(string.IsNullOrEmpty(TransportTypeChecks.Errors(plan)));
            Assert.DoesNotContain(plan.Layers[0].Nets, n => n.Role == NetRole.DriveAisle);
            Assert.Contains(plan.Layers[0].Objects, o => o.Category == AssetCategories.Restroom);
        }

        [Fact]
        public void TinyAreaDegradesWithoutErrors()
        {
            var ring = TestKit.Rect(45, 40);
            var plan = TransportTypeChecks.Compose(TransportTypeChecks.With(Id), ring, TransportTypeChecks.Road(ring));
            Assert.True(string.IsNullOrEmpty(TransportTypeChecks.Errors(plan)), TransportTypeChecks.Errors(plan));
            TransportTypeChecks.AssertInside(plan.Layers[0], ring);
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            // Art pass: tables, shelters, kiosks, vending, map boards and the view pavilion are explicit vanilla
            // ids, so empty categories never leave an automatic (droppable) pick behind; fuel pumps (no vanilla
            // stand-in) still degrade to the Info note.
            var none = new Dictionary<string, int>
            {
                [AssetCategories.Vending] = 0, [AssetCategories.FuelPump] = 0, [AssetCategories.Wayfinding] = 0, [AssetCategories.Lookout] = 0,
                [AssetCategories.Shelter] = 0, [AssetCategories.DogAmenity] = 0, [AssetCategories.Bin] = 0, [AssetCategories.CafeSeating] = 0,
                [AssetCategories.Picnic] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var p in TestKit.Gen(Id).Presets)
                {
                    var plan = TransportTypeChecks.Compose(TransportTypeChecks.With(Id, p.Values), TransportTypeChecks.FiveHa, TransportTypeChecks.Road(TransportTypeChecks.FiveHa));
                    Assert.True(string.IsNullOrEmpty(TransportTypeChecks.Errors(plan)), TransportTypeChecks.Errors(plan));
                    Assert.DoesNotContain(plan.Layers[0].Objects, o => none.ContainsKey(o.Category ?? "") && o.Role != ObjectRole.Decal && string.IsNullOrEmpty(o.Asset.Id));
                    if (p.Id == "truck") Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                    Assert.DoesNotContain(plan.Layers[0].Objects, o => o.Asset.Id == "MakeshiftTent01" || o.Asset.Id == "WaterTower01 Viewing Deck");
                }
        }

        [Fact]
        public void TransportAssetsAreClassified()
        {
            const AssetTraits obj = AssetTraits.StaticObject | AssetTraits.HasMesh;
            List<string> C(string name) => AssetHeuristics.Classify(name, obj);
            Assert.Contains(AssetCategories.TransportShelter, C("BusShelter01"));
            Assert.Contains(AssetCategories.Shelter, C("BusShelter01"));
            Assert.Contains(AssetCategories.BikeRack, C("BicycleRack02"));
            Assert.Contains(AssetCategories.Vending, C("VendingMachine01"));
            Assert.Contains(AssetCategories.FuelPump, C("GasPump01"));
            Assert.Contains(AssetCategories.Wayfinding, C("CityMapBoard01"));
            Assert.DoesNotContain(AssetCategories.BikeRack, C("ParkBench01"));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.BikeRack, "BicycleRack02", obj));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Shelter, "BusShelter01", obj));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Wayfinding, "CityMapBoard01", obj | AssetTraits.NetAttached));
        }
    }
}
