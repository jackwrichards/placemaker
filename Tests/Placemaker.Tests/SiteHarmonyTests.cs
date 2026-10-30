// s31: everything Placemaker builds on rough / bumpy ground. Playtest: a park on a bumpy hill with the Terrain feature
// on — paths warped and folded, grass tore up through the paving, white curtains at the path edges, a bike path curled
// over a bump, picnic tables and play equipment tilted. Checked on the ground the build leaves (the build's raster over
// the plan): path cross-slope and along-grade, no curtains, flat pads, objects on the final ground, furniture never on
// a slope, mounds never inside path corridors.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class SiteHarmonyTests
    {
        private readonly ITestOutputHelper _out;
        public SiteHarmonyTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>
        /// Rough test grounds: "rough" = rolling noise of a few metres (±2 m swells at ~25 m plus ±0.6 m bumps at ~9 m);
        /// "hill" = the same noise (a bit lower) on a 5 m hill with a 2 % tilt (large-scale grade ≤ ~8 %, the walk limit).
        /// </summary>
        public static Func<float2, float> Ground(string kind) => kind switch
        {
            "rough" => p => 4f * (ValueNoise.Clumps(p, 24f, 31u) - 0.5f) + 1.2f * (ValueNoise.Clumps(p, 9f, 37u) - 0.5f),
            "hill" => p => 0.02f * p.x + 5f * math.exp(-math.lengthsq(p - new float2(80f, 60f)) / (2f * 50f * 50f))
                           + 3f * (ValueNoise.Clumps(p, 22f, 41u) - 0.5f) + 1f * (ValueNoise.Clumps(p, 8f, 43u) - 0.5f),
            // A smooth hill (no bumps): the Terrain feature applies in full here (no rough-ground scale-back).
            "smoothhill" => p => 0.02f * p.x + 5f * math.exp(-math.lengthsq(p - new float2(80f, 60f)) / (2f * 50f * 50f)),
            _ => _ => 0f,
        };

        public static readonly float3[] Site = TestKit.Rect(150, 110);

        public static SiteContext SiteOn(string ground, float3[] ring = null)
        {
            ring = ring ?? Site;
            var site = ZoningCutoutTests.Streets(ring);
            var g = Ground(ground);
            site.HeightAt = g;
            for (int k = 0; k < site.NearbyNets.Count; k++)
            {
                var n = site.NearbyNets[k];
                n.A.y = g(n.A.xz); n.B.y = g(n.B.xz); n.C.y = g(n.C.xz); n.D.y = g(n.D.xz);
                site.NearbyNets[k] = n;
            }
            return site;
        }

        /// <summary>A type with its default features, optionally with the Terrain feature (rolling hills) on.</summary>
        public static ComposedPlan Compose(string type, string ground, bool terrain, bool harmony = true, string terrainPreset = "rolling", bool moods = true)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 7);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            if (!moods) r.Root.Settings.Values["moods"] = false;   // (parks makeover) the places' own ground, off to test the Terrain feature alone
            if (terrain)
            {
                if (!Editor.SetFeature(r, RecipePaths.Root, "terrain", true, 900)) r.Root.Children.Add(new RecipeStep { Layer = "terrain", Seed = 900, Settings = GeneratorRegistry.Get("terrain").Defaults() });
                var step = r.Root.Children.First(c => c.Layer == "terrain");
                foreach (var kv in Preset(terrainPreset)) step.Settings.Values[kv.Key] = kv.Value;
            }
            else
                foreach (var c in r.Root.Children) if (c.Layer == "terrain") c.Enabled = false;
            var site = SiteOn(ground);
            bool was = SiteHarmony.Disabled;
            SiteHarmony.Disabled = !harmony;
            try { return Composer.Compose(r, new AreaShape { Ring = Site, Source = AreaSource.Drawn }, site, CancellationToken.None); }
            finally { SiteHarmony.Disabled = was; }
        }

        private static Dictionary<string, object> Preset(string id) => id switch
        {
            "rolling" => new Dictionary<string, object> { ["shape"] = "hills", ["height"] = 3.0, ["scale"] = 45.0 },
            "gentle" => new Dictionary<string, object> { ["shape"] = "mounds", ["height"] = 1.5, ["scale"] = 25.0 },
            _ => new Dictionary<string, object> { ["shape"] = "mounds", ["height"] = 2.0 },
        };

        /// <summary>Every visible fill type whose land the harmony pass grades (parks and leisure types).</summary>
        public static IEnumerable<string> HarmonyTypes() =>
            GeneratorRegistry.All.Where(g => g.Kind == LayerKind.Fill && !GeneratorRegistry.IsHidden(g) && !GeneratorRegistry.IsLoose(g.Id) && SiteHarmony.Participates(g.Id))
                .Select(g => g.Id).OrderBy(x => x, StringComparer.Ordinal);

        public static IEnumerable<object[]> HarmonyTypeIds() => HarmonyTypes().Select(t => new object[] { t });

        /// <summary>The playtest's kinds of place, also on the bumpiest ground.</summary>
        public static IEnumerable<object[]> CoreTypeIds() =>
            new[] { "park", "picnicGrounds", "adventurePlayground", "sportsPark", "campground", "botanicalGarden", "plaza" }.Select(t => new object[] { t });

        private static void AssertHarmony(string type, string ground, ComposedPlan plan)
        {
            var m = SiteHarmony.Measure(plan, SiteOn(ground));
            string at = $"{type} on {ground}: {m}";
            Assert.True(m.MaxJump <= 0.8f, "curtain (terraform step) " + at);
            Assert.True(m.MaxPathCross <= 0.021f, "path cross-slope > 2 % " + at);
            bool steepIssue = plan.AllIssues().Any(i => i.Key == SiteHarmony.IssueSteepPath);
            Assert.True(m.MaxPathGradeRatio <= 1.1f || steepIssue, "path along-grade over its limit " + at);
            Assert.True(m.MaxPadDeviation <= 0.15f, "pad not flat " + at);
            Assert.True(m.MaxPadTilt <= 0.035f, "pad tilted " + at);
            Assert.True(m.MaxObjectOff <= 0.3f, "object off the final ground " + at);
            Assert.True(m.FurnitureSteep <= math.max(1, m.Furniture / 20), "furniture on slopes " + at);
            foreach (var l in plan.Layers)
                if (l.Facts.TryGetValue("harmonyDecorInCorridor", out var d)) Assert.True(d <= 1e-3, "decoration inside a path corridor " + at);
        }

        [Theory]
        [MemberData(nameof(HarmonyTypeIds))]
        public void AllTypes_OnBumpyHill_WithTerrainFeature_PlayNice(string type)
        {
            var plan = Compose(type, "hill", true);
            Assert.DoesNotContain(plan.AllIssues(), i => i.Severity == IssueSeverity.Error);
            AssertHarmony(type, "hill", plan);
        }

        [Theory]
        [MemberData(nameof(CoreTypeIds))]
        public void CoreTypes_OnRoughGround_PlayNice(string type)
        {
            foreach (bool terrain in new[] { false, true })
                AssertHarmony(type + (terrain ? "+terrain" : ""), "rough", Compose(type, "rough", terrain));
        }

        [Fact]
        public void PathsBesideWaterStayAboveIt()
        {
            // a pond in a hollow of the park (water surface at -0.5 m): paths graded onto its rim lay at the water and flooded
            var site = SiteOn("flat");
            float2 c = new float2(75f, 55f);
            Func<float2, float> g = p => -2f * math.exp(-math.lengthsq(p - c) / (2f * 22f * 22f));
            site.HeightAt = g;
            site.WaterDepthAt = p => math.max(0f, -0.5f - g(p));
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 7);
            var plan = Composer.Compose(r, new AreaShape { Ring = Site, Source = AreaSource.Drawn }, site, CancellationToken.None);
            float worst = float.MaxValue; int near = 0;
            foreach (var l in plan.Layers)
                foreach (var n in l.Nets)
                {
                    if (!SiteHarmony.IsPathNet(n) || (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide)) continue;
                    for (int i = 0; i <= 16; i++)
                    {
                        var q = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f);
                        float w = SiteHarmony.WaterNear(q.xz, 1f, null, site.WaterDepthAt, g);
                        if (!math.isfinite(w)) continue;
                        near++;
                        worst = math.min(worst, q.y - w);
                    }
                }
            _out.WriteLine($"{near} path points beside the water, lowest {worst:F2} m above it");
            Assert.True(near > 0, "no path near the pond: the test proves nothing");
            Assert.True(worst >= SiteHarmony.WaterClearance - 0.1f, $"a path lies {worst:F2} m above the water");
        }

        /// <summary>The playtest, reproduced: without the pass the same park on rough ground has warped paths and tilted tables.</summary>
        [Fact]
        public void WithoutHarmony_TheOldArtefactsAreBack()
        {
            var m = SiteHarmony.Measure(Compose("park", "rough", true, harmony: false), SiteOn("rough"));
            Assert.True(m.MaxPathCross > 0.1f, "cross-slope " + m);
            Assert.True(m.MaxPathGradeRatio > 1.3f, "along-grade " + m);
            Assert.True(m.FurnitureSteep > 3, "furniture " + m);
        }

        /// <summary>Mounds never reach a path: the ground under every path is the same with the Terrain feature on or off.</summary>
        [Theory]
        [InlineData("park")]
        [InlineData("picnicGrounds")]
        [InlineData("botanicalGarden")]
        public void Mounds_NeverInsidePathCorridors(string type)
        {
            var site = SiteOn("smoothhill");
            var off = Compose(type, "smoothhill", false, moods: false);
            var on = Compose(type, "smoothhill", true, moods: false);
            var fOff = SiteHarmony.FinalGround(off, site.HeightAt);
            var fOn = SiteHarmony.FinalGround(on, site.HeightAt);
            float changed = 0f, inCorridor = 0f;
            var nets = on.Layers.Where(l => SiteHarmony.Participates(l.GeneratorId)).SelectMany(l => l.Nets).Where(SiteHarmony.IsPathNet).ToList();
            for (float y = 4f; y < 106f; y += 2f)
                for (float x = 4f; x < 146f; x += 2f)
                {
                    var p = new float2(x, y);
                    float d = math.abs(fOn(p) - fOff(p));
                    bool corridor = nets.Any(n => TestKit.DistanceToCurve(p, n) <= SiteHarmony.HalfWidth(n, site) + SiteHarmony.Shoulder);
                    if (corridor) inCorridor = math.max(inCorridor, d); else changed = math.max(changed, d);
                }
            Assert.True(changed > 0.5f, $"{type}: the Terrain feature should shape the lawns (max change {changed:0.00} m)");
            Assert.True(inCorridor <= 0.1f, $"{type}: the ground under a path moved {inCorridor:0.00} m with the Terrain feature on");
        }

        /// <summary>One field: the participating layers' terraform is a single set of sloped quads (no level patches stacked at other heights).</summary>
        [Fact]
        public void Terraform_IsOneSmoothField()
        {
            var plan = Compose("park", "hill", true);
            var holders = plan.Layers.Where(l => SiteHarmony.Participates(l.GeneratorId) && l.Terraform.Any(t => !math.isfinite(t.WaterLevel))).ToList();
            Assert.Single(holders);
            Assert.Equal("park", holders[0].GeneratorId);
            Assert.All(holders[0].Terraform.Where(t => !math.isfinite(t.WaterLevel)), t => Assert.True(t.Sloped));
            Assert.True(holders[0].Facts["harmonyCells"] > 0);
        }

        /// <summary>Mixed recipes: a road grid's blocks as parks, a split park | plaza. Roads keep their ground; the parks are graded.</summary>
        [Theory]
        [InlineData("{\"id\":\"x\",\"root\":{\"layer\":\"roads\",\"children\":[{\"layer\":\"park\",\"target\":\"block\"}]}}")]
        [InlineData("{\"id\":\"x\",\"root\":{\"layer\":\"split\",\"children\":[{\"layer\":\"park\",\"target\":\"part:0\",\"children\":[{\"layer\":\"terrain\",\"settings\":{\"shape\":\"hills\",\"height\":3}}]},{\"layer\":\"plaza\",\"target\":\"part:1\"}]}}")]
        public void MixedRecipes_GradeOnlyTheirParks(string json)
        {
            var r = RecipeJson.Parse(json).Recipe;
            var ring = TestKit.Rect(240, 180);
            var site = SiteOn("hill", ring);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            Assert.DoesNotContain(plan.AllIssues(), i => i.Severity == IssueSeverity.Error);
            var m = SiteHarmony.Measure(plan, site);
            _out.WriteLine(m.ToString());
            Assert.True(m.MaxPathCross <= 0.021f, m.ToString());
            Assert.True(m.MaxObjectOff <= 0.3f, m.ToString());
            Assert.True(m.MaxJump <= 0.8f, m.ToString());
            // Road nets are never harmony's: their layer keeps no harmony field.
            Assert.DoesNotContain(plan.Layers.Where(l => !SiteHarmony.Participates(l.GeneratorId)), l => l.Facts.ContainsKey("harmonyCells"));
        }

        [Fact]
        public void FlatGround_WithoutTerrainFeature_NoEarthworks()
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 7);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            r.Root.Settings.Values["moods"] = false;   // (parks makeover) the places shape the ground unless switched off
            var plan = Composer.Compose(r, new AreaShape { Ring = Site, Source = AreaSource.Drawn }, ZoningCutoutTests.Streets(Site), CancellationToken.None);
            Assert.Equal(0, plan.Layers.Sum(l => l.Terraform.Count(t => !math.isfinite(t.WaterLevel))));
            Assert.DoesNotContain(plan.AllIssues(), i => i.Key == TerrainShaper.PermanentKey);
        }

        [Fact]
        public void TerrainFeature_ScalesBackOnRoughGround()
        {
            var rough = Compose("park", "rough", true);
            var smooth = Compose("park", "smoothhill", true);
            var tr = rough.Layers.First(l => l.GeneratorId == "terrain");
            var ts = smooth.Layers.First(l => l.GeneratorId == "terrain");
            Assert.True(tr.Facts["terrainRoughness"] > ts.Facts["terrainRoughness"]);
            Assert.Contains(tr.Issues, i => i.Key == Placemaker.Core.Layers.AddOns.TerrainAddOn.IssueRough || i.Key == Placemaker.Core.Layers.AddOns.TerrainAddOn.IssueScaled);
            Assert.DoesNotContain(ts.Issues, i => i.Key == Placemaker.Core.Layers.AddOns.TerrainAddOn.IssueRough);
        }

        /// <summary>The Terrain feature on its own (no harmony) is already a smooth landform and keeps off the paths.</summary>
        [Fact]
        public void TerrainFeature_IsSmooth_AndAvoidsPaths()
        {
            var plan = Compose("park", "smoothhill", true, harmony: false);
            var t = plan.Layers.First(l => l.GeneratorId == "terrain");
            Assert.NotEmpty(t.Terraform);
            Assert.All(t.Terraform, x => Assert.True(x.Sloped));
            var site = SiteOn("smoothhill");
            var F = SiteHarmony.FinalGround(plan, site.HeightAt);
            foreach (var n in plan.Layers.First(l => l.GeneratorId == "park").Nets.Where(SiteHarmony.IsPathNet))
                for (int i = 0; i <= 8; i++)
                {
                    var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                    Assert.True(math.abs(F(p) - site.HeightAt(p)) <= 0.15f, $"mound under a path at {p}: {F(p) - site.HeightAt(p):0.00} m");
                }
        }

        /// <summary>Debug aid: HARMONY_PROFILE=type prints the pass's phase times on 5 ha (flat, road along the bottom).</summary>
        [Fact]
        public void Profile_FiveHectares()
        {
            var t = Environment.GetEnvironmentVariable("HARMONY_PROFILE");
            if (string.IsNullOrEmpty(t)) return;
            var ring = TestKit.Rect(250, 200);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(t), 7);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            foreach (var ground in new[] { "flat", "hill" })
            {
                var site = SiteOn(ground, ring);
                for (int rep = 0; rep < 2; rep++)
                {
                    SiteHarmony.DebugLog = new List<string>();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
                    _out.WriteLine($"{t} {ground}: compose {sw.ElapsedMilliseconds} ms; " + string.Join("; ", SiteHarmony.DebugLog));
                    SiteHarmony.DebugLog = null;
                }
            }
        }

        [Fact]
        public void BigPark_OnHill_WithinBudget()
        {
            var ring = TestKit.Rect(250, 200);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 7);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            Editor.SetFeature(r, RecipePaths.Root, "terrain", true, 900);
            var site = SiteOn("hill", ring);
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            long ms = sw.ElapsedMilliseconds;
            _out.WriteLine($"5 ha park on a hill: {ms} ms");
            Assert.True(ms < 4000, $"5 ha park took {ms} ms");
        }

        [Fact]
        public void Sweep_AllTypes_Print()
        {
            foreach (var t in HarmonyTypes())
                foreach (var ground in new[] { "hill" })
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    ComposedPlan plan;
                    try { plan = Compose(t, ground, true); }
                    catch (Exception e) { _out.WriteLine($"{t}: FAILED {e.GetType().Name} {e.Message}"); continue; }
                    long ms = sw.ElapsedMilliseconds;
                    var m = SiteHarmony.Measure(plan, SiteOn(ground));
                    var facts = plan.Layers.SelectMany(l => l.Facts).Where(kv => kv.Key.StartsWith("harmony")).Select(kv => kv.Key.Substring(7) + "=" + kv.Value.ToString("0.##"));
                    _out.WriteLine($"{t} {ground} {ms} ms: {m} | {string.Join(" ", facts)}");
                }
        }

        /// <summary>Debug aid: HARMONY_TYPES=a,b dotnet test --filter ...Details_ForTypes prints what is off, item by item.</summary>
        [Fact]
        public void Details_ForTypes()
        {
            var env = Environment.GetEnvironmentVariable("HARMONY_TYPES");
            if (string.IsNullOrEmpty(env)) return;
            foreach (var t in env.Split(','))
            {
                var atEnv = Environment.GetEnvironmentVariable("HARMONY_AT");
                if (!string.IsNullOrEmpty(atEnv)) { var q = atEnv.Split(':').Select(float.Parse).ToArray(); SiteHarmony.DebugAt = p => math.distance(p, new float2(q[0], q[1])) < 3f; SiteHarmony.DebugLog = new List<string>(); }
                var plan = Compose(t, "hill", true);
                if (SiteHarmony.DebugLog != null) foreach (var line in SiteHarmony.DebugLog) _out.WriteLine("      dbg " + line);
                SiteHarmony.DebugAt = null; SiteHarmony.DebugLog = null;
                _out.WriteLine($"== {t}: {SiteHarmony.Measure(plan, SiteOn("hill"))}");
                Details(plan, SiteOn("hill"));
                var at = Environment.GetEnvironmentVariable("HARMONY_AT");
                if (!string.IsNullOrEmpty(at))
                {
                    var xy = at.Split(':').Select(float.Parse).ToArray();
                    var F = SiteHarmony.FinalGround(plan, SiteOn("hill").HeightAt);
                    var o = new float2(xy[0], xy[1]);
                    for (int dy = 3; dy >= -3; dy--)
                        _out.WriteLine("      " + string.Join(" ", Enumerable.Range(-3, 7).Select(dx => F(o + new float2(dx, dy)).ToString("0.00").PadLeft(6))));
                    foreach (var l in plan.Layers) foreach (var tf in l.Terraform) { var r2 = Poly.To2D(tf.Ring); if (Poly.Contains(r2, o) || Poly.DistanceToBoundary(r2, o) < 4f) _out.WriteLine($"      tf {l.GeneratorId} sloped {tf.Sloped} target {tf.TargetHeight:0.00} n {tf.Ring.Length} c {Poly.Centroid(r2)} water {tf.WaterLevel}"); }
                    foreach (var l in plan.Layers) foreach (var n in l.Nets) if (TestKit.DistanceToCurve(o, n) < 6f) _out.WriteLine($"      net {l.GeneratorId} {n.Role}/{n.Category} follow {n.FollowTerrain} d {TestKit.DistanceToCurve(o, n):0.0}");
                    foreach (var ob in plan.Layers.SelectMany(l => l.Objects).Where(q => math.distance(q.Position.xz, o) < 4f && q.Role != ObjectRole.Decal))
                        _out.WriteLine($"      near {ob.Role}/{ob.Category} at {ob.Position}");
                }
            }
        }

        [Fact]
        public void Diagnose_ParkOnBumpyHill_BeforeAndAfter()
        {
            foreach (var ground in new[] { "rough", "hill" })
                foreach (bool terrain in new[] { false, true })
                    foreach (bool harmony in new[] { false, true })
                    {
                        var plan = Compose("park", ground, terrain, harmony);
                        var m = SiteHarmony.Measure(plan, SiteOn(ground));
                        _out.WriteLine($"park {ground} terrain={terrain} harmony={harmony}: {m}");
                        if (harmony) Details(plan, SiteOn(ground));
                    }
        }

        private void Details(ComposedPlan plan, SiteContext site)
        {
            var F = SiteHarmony.FinalGround(plan, site.HeightAt);
            var nets = plan.Layers.Where(l => SiteHarmony.Participates(l.GeneratorId)).SelectMany(l => l.Nets.Select(n => (l, n))).Where(x => SiteHarmony.IsPathNet(x.n)).ToList();
            foreach (var (l, n) in nets)
            {
                var cu = new Cubic(n.A, n.B, n.C, n.D);
                float hw = SiteHarmony.HalfWidth(n, site);
                float lim = SiteHarmony.GradeLimit(n);
                float len = cu.LengthXZ(32);
                for (float s = 0; s + 4f <= len; s += 2f)
                {
                    var p0 = cu.Position(s / len).xz; var p1 = cu.Position((s + 4f) / len).xz;
                    float gr = math.abs(F(p1) - F(p0)) / math.max(0.5f, math.distance(p0, p1));
                    if (gr > lim * 1.05f)
                        _out.WriteLine($"   grade {gr:0.000} (lim {lim}) {l.GeneratorId} {n.Role}/{n.Category} at {p0} s {s:0} of {len:0} dA {math.distance(p0, n.A.xz):0.0} dD {math.distance(p0, n.D.xz):0.0} orig {site.HeightAt(p0):0.00}->{site.HeightAt(p1):0.00} F {F(p0):0.00}->{F(p1):0.00}");
                }
                for (int i = 1; i < 20; i++)
                {
                    float t = i / 20f;
                    var p = cu.Position(t).xz; var tan = math.normalize(cu.Tangent(t).xz); var nrm = new float2(-tan.y, tan.x);
                    float cs = math.abs(F(p + nrm * hw) - F(p - nrm * hw)) / (2 * hw);
                    if (cs <= 0.02f) continue;
                    var near = nets.Where(x => x.n != n && TestKit.DistanceToCurve(p, x.n) < 8f).Select(x => $"{x.n.Role}/{x.n.Category}@{TestKit.DistanceToCurve(p, x.n):0.0}");
                    _out.WriteLine($"   cross {cs:0.000} {l.GeneratorId} {n.Role}/{n.Category} hw {hw} at {p} t {t:0.00} near [{string.Join(", ", near)}] dA {math.distance(p, n.A.xz):0.0} dD {math.distance(p, n.D.xz):0.0}");
                }
            }
            foreach (var l in plan.Layers)
            {
                if (!SiteHarmony.Participates(l.GeneratorId)) continue;
                foreach (var s in l.Surfaces)
                {
                    if (!SiteHarmony.IsPadSurface(s)) continue;
                    var ring = Poly.To2D(s.Ring);
                    float tilt = 0f;
                    var c = Poly.Centroid(ring);
                    tilt = TerrainSurvey.Grade(F, c, 2f);
                    var pts = new List<float3>(); int inBand = 0;
                    Poly.Bounds(ring, out var a, out var b);
                    for (float y = a.y + 0.5f; y < b.y; y += 1f)
                        for (float x = a.x + 0.5f; x < b.x; x += 1f)
                        {
                            var p = new float2(x, y);
                            if (!Poly.Contains(ring, p)) continue;
                            if (nets.Any(q => TestKit.DistanceToCurve(p, q.n) < SiteHarmony.HalfWidth(q.n, site) + 2.5f)) { inBand++; continue; }
                            pts.Add(new float3(p.x, F(p), p.y));
                        }
                    float dev = -1, tl = -1;
                    if (pts.Count >= 4 && SiteHarmony.FitPlane(pts, c, 1f, out float a0, out float2 gg))
                    {
                        tl = math.length(gg); dev = pts.Max(q => math.abs(a0 + math.dot(gg, q.xz - c) - q.y));
                        if (dev > 0.1f)
                            foreach (var q in pts.OrderByDescending(q => math.abs(a0 + math.dot(gg, q.xz - c) - q.y)).Take(4))
                            {
                                float dp = nets.Count == 0 ? -1f : nets.Min(x => TestKit.DistanceToCurve(q.xz, x.n));
                                var fo = plan.Layers.SelectMany(x => x.Objects).Where(o => SiteHarmony.IsFurniture(o)).OrderBy(o => math.distance(o.Position.xz, q.xz)).FirstOrDefault() ?? new PlannedObject();
                                _out.WriteLine($"      dev {a0 + math.dot(gg, q.xz - c) - q.y:0.00} at {q.xz} r {math.distance(q.xz, c):0.0} path {dp:0.0} furn {fo.Role}/{fo.Category}@{math.distance(fo.Position.xz, q.xz):0.0} orig {site.HeightAt(q.xz):0.00} F {q.y:0.00}");
                            }
                    }
                    _out.WriteLine($"   surface {l.GeneratorId} {s.Role} {s.Category} area {Poly.Area(ring):0} at {c} grade@c {tilt:0.000} fit tilt {tl:0.000} dev {dev:0.00} inBand {inBand}/{inBand + pts.Count}");
                }
                foreach (var o in l.Objects)
                {
                    if (o.Role != ObjectRole.Decal && math.abs(o.Position.y - F(o.Position.xz)) > 0.3f && math.abs(o.Position.y - F(o.Position.xz)) < 2.5f) _out.WriteLine($"   OFF {l.GeneratorId} {o.Role}/{o.Category} at {o.Position} F {F(o.Position.xz):0.00} orig {site.HeightAt(o.Position.xz):0.00}");
                    if (!SiteHarmony.IsFurniture(o)) continue;
                    float r = math.max(0.75f, math.min(2f, SiteHarmony.FootprintRadius(o, site)));
                    float g = TerrainSurvey.Grade(F, o.Position.xz, r);
                    float off = math.abs(o.Position.y - F(o.Position.xz));
                    if (g > 0.12f || off > 0.3f)
                    {
                        var inSurf = plan.Layers.SelectMany(x => x.Surfaces).Where(s => SiteHarmony.IsPadSurface(s) && Poly.Contains(Poly.To2D(s.Ring), o.Position.xz)).Select(s => $"{s.Role}/{Poly.Area(Poly.To2D(s.Ring)):0}");
                        float dp = nets.Count == 0 ? -1 : nets.Min(x => TestKit.DistanceToCurve(o.Position.xz, x.n));
                        _out.WriteLine($"   obj {l.GeneratorId} {o.Role} {o.Category} at {o.Position.xz} r {r:0.0} grade {g:0.000} off {off:0.00} path {dp:0.0} surf [{string.Join(",", inSurf)}] orig {site.HeightAt(o.Position.xz):0.00} relief0 {TerrainSurvey.Relief(site.HeightAt, o.Position.xz, r):0.00}");
                    }
                }
            }
        }
    }
}
