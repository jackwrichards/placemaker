// s21 decal overhaul: decals are a complement of every feature, take no space, and are dressed by the
// composer (Core/Decals). docs/DECALS.md has the census and the rules.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Decals;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class DecalsTests
    {
        private readonly ITestOutputHelper _out;
        public DecalsTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static Recipe Recipe(string layer, uint seed = 7, Action<Recipe> edit = null)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(layer), seed);
            Editor.ApplyDefaults(r);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            edit?.Invoke(r);
            uint k = 0;
            foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
            return r;
        }

        private static ComposedPlan Compose(Recipe r, float3[] ring, SiteContext site = null)
        {
            site = site ?? new SiteContext();
            TestKit.Entered(r, ring, site);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static List<PlannedObject> Decals(ComposedPlan p, bool functional = false) =>
            p.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Decal && DecalCatalog.IsFunctionalCategory(o.Category) == functional).ToList();

        private static float3[] Site => TestKit.Rect(120, 100);

        // ───────────────────────── census / classifier ─────────────────────────

        [Theory]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 1 Decal", DecalFamily.Oil)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 17 Band A Wet Sand Decal", DecalFamily.WetSand)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 11 Band A Decal", DecalFamily.Puddle)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 20 Wet Track Long Decal", DecalFamily.Tyre)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 42 Track Short Decal", DecalFamily.Tyre)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 81 A M Decal", DecalFamily.Wet)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Debris 15 Glass Spreaded Decal", DecalFamily.Glass)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Debris 01 Decal", DecalFamily.Rubble)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Seaweed 01 Brown Decal", DecalFamily.BeachWrack)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Litter 05 Papers 01 Large Decal", DecalFamily.Litter)]
        [InlineData("G87 Trash Decals Dirty Pack Leaf G87 Leaf 01 Decal", DecalFamily.LeafLitter)]
        [InlineData("Fallen leaves decals Leaf Dead Leaves1 Yellow lined Decal", DecalFamily.LeafEdge)]
        [InlineData("Fallen leaves decals Leaf Dead Leaves7 Brown scattered Decal", DecalFamily.LeafLitter)]
        [InlineData("Ground Texture Decal Pack Ground LargeMulch01 Decal", DecalFamily.Mulch)]
        [InlineData("ground texture decal pack ground largemulch01 decal", DecalFamily.Mulch)]       // classification plug-ins see lower case
        [InlineData("Ground Texture Decal Pack Ground ScatteredRocks01 Decal", DecalFamily.Rocks)]
        [InlineData("Ground Texture Decal Pack Ground Moss01 Decal", DecalFamily.Moss)]
        [InlineData("Ground Texture Decal Pack Ground Dirt02 Decal", DecalFamily.Dirt)]
        [InlineData("Sand Surfaces and Beach Decals Beach Palm-leaf-01 Decal", DecalFamily.PalmLitter)]
        [InlineData("Sand Surfaces and Beach Decals Beach Tire-Track-01 Decal", DecalFamily.Tyre)]
        [InlineData("Sand Surfaces and Beach Decals Beach Footprint-01-Left Decal", DecalFamily.Footprint)]
        [InlineData("Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 3 Decal", DecalFamily.Patch)]
        [InlineData("Cracks and Damage Decal Pack RoadAssets Crack - Small 1 Decal", DecalFamily.Crack)]
        [InlineData("G87 Dirty Roads Stains G87 Dirt Road Transition 01 Decal", DecalFamily.Mud)]
        [InlineData("RealVision Decals Ground Texture125 - Oil Decal", DecalFamily.Oil)]
        [InlineData("RealVision Decals Ground Texture154 - Castle Wall Decal", DecalFamily.BuildingOnly)]
        [InlineData("RealVision Decals Ground Graff - 03 Decal", DecalFamily.Graffiti)]
        [InlineData("Manicured Grass Texture Pack Ground Manicured Grass - Stripes Large 12m Decal", DecalFamily.LawnPattern)]
        [InlineData("ParkingLotDecal01", DecalFamily.ParkingStall)]
        [InlineData("EU RoadArrow Forward", DecalFamily.Marking)]
        [InlineData("AlphabetDecalA01", DecalFamily.Marking)]
        [InlineData("TennisCourtDecal01", DecalFamily.SportsMarking)]
        [InlineData("PlaygroundDecal02", DecalFamily.SportsMarking)]
        [InlineData("ChirperDecalSmall01", DecalFamily.Logo)]
        public void CensusFamiliesFromItemWords(string name, DecalFamily family) =>
            Assert.Equal(family, DecalCatalog.Describe(name).Family);

        [Fact]
        public void ShapesComeFromRealFootprints()
        {
            Assert.Equal(DecalShape.Strip, DecalCatalog.Describe("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 11 Band A Decal", 2, 6).Shape);
            Assert.Equal(DecalShape.Blob, DecalCatalog.Describe("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 1 Decal", 3, 3).Shape);
            Assert.Equal(DecalShape.Directional, DecalCatalog.Describe("Sand Surfaces and Beach Decals Beach Tire-Track-01 Decal", 2, 2).Shape);
            Assert.Equal(DecalShape.Sheet, DecalCatalog.Describe("RealVision Decals Ground Texture20 - Dirt Decal", 40, 40).Shape);
            Assert.Equal(DecalShape.Tiny, DecalCatalog.Describe("G87 Trash Decals Dirty Pack Trash G87 Debris 12 Glass Bottle Green Decal", 0.7f, 0.7f).Shape);
            Assert.Equal(DecalOrientation.AlongHost, DecalCatalog.Describe("G87 Trash Decals Dirty Pack Trash G87 Seaweed 01 Brown Decal", 8, 4).Orientation);
            Assert.True(DecalCatalog.Describe("ParkingLotDecal01").Functional);
            Assert.False(DecalCatalog.Describe("Fallen leaves decals Leaf Dead Leaves2 Yellow light Decal").Functional);
        }

        [Fact]
        public void DressingPoolsClassifyAndAreEligible()
        {
            const AssetTraits decal = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.DecalMesh;
            var oil = AssetHeuristics.Classify("G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal", decal);
            Assert.Contains(DecalPools.Oil, oil);
            Assert.True(AssetHeuristics.AutoEligible(DecalPools.Oil, "G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal", decal));
            var lined = AssetHeuristics.Classify("Fallen leaves decals Leaf Dead Leaves3 Red lined Decal", decal);
            Assert.Contains(DecalPools.LeavesEdge, lined);
            // Building-only and functional decals never join a dressing pool.
            Assert.DoesNotContain(AssetHeuristics.Classify("RealVision Decals Ground Texture161 - Castle Window Decal", decal), c => DecalPools.IsDressingPool(c));
            Assert.DoesNotContain(AssetHeuristics.Classify("ParkingIconDecal01", decal | AssetTraits.Vanilla), c => DecalPools.IsDressingPool(c));
            // A 0.3 m tyre-curve bit is no tyre mark; 30 m stains are no stains.
            Assert.False(AssetCuration.FitsSize(DecalPools.Tyre, 0.3f));
            Assert.True(AssetCuration.FitsSize(DecalPools.Tyre, 6f));
            Assert.False(AssetCuration.FitsSize(DecalPools.Stain, 20f));
            // Variety: decals of a pool mix per object.
            Assert.False(AutoAssets.IsUniform(DecalPools.Oil));
        }

        // ───────────────────────── decals take no space ─────────────────────────

        [Fact]
        public void DeclutterNeverDropsOrMovesDecals()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan();
            plan.Layers.Add(l);
            l.Objects.Add(new PlannedObject { Position = new float3(10, 0, 10), Role = ObjectRole.Bench, Asset = new AssetRef("Bench") });
            for (int i = 0; i < 20; i++)
                l.Objects.Add(new PlannedObject { Position = new float3(10 + i * 0.1f, 0, 10), Role = ObjectRole.Decal, Asset = new AssetRef("Leaves"), Category = AssetCategories.DecalLeaves });
            l.Nets.Add(new PlannedNet { A = new float3(0, 0, 10), B = new float3(7, 0, 10), C = new float3(14, 0, 10), D = new float3(20, 0, 10), Role = NetRole.Pathway, Asset = new AssetRef("Path") });
            var assets = new Dictionary<string, AssetInfo>
            {
                ["Bench"] = new AssetInfo { Id = "Bench", BoundsMin = new float3(-1, 0, -0.4f), BoundsMax = new float3(1, 1, 0.4f) },
                ["Leaves"] = new AssetInfo { Id = "Leaves", BoundsMin = new float3(-3, -0.5f, -3), BoundsMax = new float3(3, 0.5f, 3) },
                ["Path"] = new AssetInfo { Id = "Path", NetWidth = 3f },
            };
            var before = l.Objects.Where(o => o.Role == ObjectRole.Decal).Select(o => o.Position).ToList();
            var report = Declutter.Run(plan, assets);
            var after = l.Objects.Where(o => o.Role == ObjectRole.Decal).Select(o => o.Position).ToList();
            Assert.Equal(before, after);
            Assert.False(report.Dropped.ContainsKey(ObjectRole.Decal));
            Assert.DoesNotContain(plan.Issues, i => i.Key == Declutter.IssueKey(ObjectRole.Decal));
            Assert.False(Declutter.Participates(ObjectRole.Decal));
        }

        [Fact]
        public void ObjectBudgetNeitherCountsNorThinsDecals()
        {
            var plan = new AreaPlan();
            for (int i = 0; i < 5000; i++)
                plan.Objects.Add(new PlannedObject { Position = new float3(i % 100, 0, i / 100), Role = ObjectRole.Decal, Category = AssetCategories.DecalLeaves });
            for (int i = 0; i < 100; i++)
                plan.Objects.Add(new PlannedObject { Position = new float3(i, 0, 0), Role = ObjectRole.Tree });
            int removed = ObjectBudget.Enforce(plan, 10000f, ObjectBudget.DefaultPerHectare);
            Assert.Equal(0, removed);
            Assert.Equal(5000, plan.Objects.Count(o => o.Role == ObjectRole.Decal));
        }

        // ───────────────────────── no decals from decal packs (issue #2) ─────────────────────────

        [Theory]
        [InlineData("park")]
        [InlineData("plaza")]
        [InlineData("parking")]
        [InlineData("beach")]
        [InlineData("sportsPark")]
        [InlineData("campground")]
        [InlineData("abandonedLot")]
        [InlineData("constructionSite")]
        [InlineData("quarry")]
        [InlineData("scrapyard")]
        [InlineData("landfill")]
        [InlineData("logisticsYard")]
        [InlineData("urbanRuins")]
        [InlineData("mallDistrict")]
        public void PlansHoldNoDecalsFromDecalPacks(string type)
        {
            // Gritty asked for the most wear; the beach on its water (the waterline was dressed with wet sand and wrack).
            var site = type == "beach" ? Types.WaterTypesKit.WaterInside(18f) : null;
            var p = Compose(Recipe(type, 7, r => r.Root.Settings.Values[DressingControls.StyleId] = "gritty"), Site, site);
            var all = p.Layers.SelectMany(l => l.Objects).ToList();
            Assert.NotEmpty(all);
            Assert.DoesNotContain(all, o => o.Role == ObjectRole.Decal && !PackDecals.Kept(o.Category));
        }

        [Fact]
        public void OnlyTheBaseGameKindsOfDecalsAreKept()
        {
            var l = new AreaPlan();
            string[] kept = { AssetCategories.DecalMarkings, AssetCategories.ParkingDecal, "decal.courtbasketball", "decal.fieldline" };
            string[] dropped = { AssetCategories.DecalLeaves, AssetCategories.DecalPaving, AssetCategories.DecalFarm, DecalPools.Oil, AssetCategories.DecalGraffiti, "" };
            foreach (var c in kept.Concat(dropped)) l.Objects.Add(new PlannedObject { Role = ObjectRole.Decal, Category = c });
            l.Objects.Add(new PlannedObject { Role = ObjectRole.ParkingBay, Category = AssetCategories.ParkingDecal });
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Category = AssetCategories.Tree });
            Assert.Equal(dropped.Length, PackDecals.Drop(l));
            Assert.Equal(kept, l.Objects.Where(o => o.Role == ObjectRole.Decal).Select(o => o.Category).ToArray());
            Assert.Equal(2, l.Objects.Count(o => o.Role != ObjectRole.Decal));
        }

        [Fact]
        public void NoTypeOffersPackDecalsInThePanel()
        {
            var familyIds = Enum.GetValues(typeof(DressFamily)).Cast<DressFamily>().Select(DressingControls.FamilyId).ToList();
            foreach (var g in GeneratorRegistry.All)
            {
                Assert.DoesNotContain(Features.Of(g), f => f.Layer == Placemaker.Core.Layers.AddOns.DecalsAddOn.LayerId);
                Assert.DoesNotContain(g.Controls, c => c.Id == DressingControls.AmountId || familyIds.Contains(c.Id));
            }
            // every hidden type control still exists (a renamed one would show again)
            foreach (var (layer, control) in PackDecals.HiddenControlIds)
                Assert.Contains(GeneratorRegistry.Get(layer).Controls, c => c.Id == control);
        }

        [Fact]
        public void TheWearStyleSitsWithTheGroundPaint()
        {
            foreach (var id in DressingProfiles.LayerIds)
            {
                var g = GeneratorRegistry.Get(id);
                if (g == null) continue;
                var style = g.Controls.FirstOrDefault(c => c.Id == DressingControls.StyleId);
                Assert.NotNull(style);
                Assert.Equal(DressingControls.PaintGroup, style.Group);
                foreach (var p in g.Presets)
                    Assert.Equal(DressingControls.Styles[(int)DressingProfiles.StyleFor(id, p.Id)], p.Values.S(DressingControls.StyleId, null));
            }
            Assert.Equal("gritty", GeneratorRegistry.Get("abandonedLot").Defaults().S(DressingControls.StyleId, null));
            Assert.Equal("clean", GeneratorRegistry.Get("formalGarden").Defaults().S(DressingControls.StyleId, null));
            Assert.Null(GeneratorRegistry.Get("roads").Controls.FirstOrDefault(c => c.Id == DressingControls.StyleId));
        }

        // ───────────────────────── the dressing pass ─────────────────────────

        [Fact]
        public void DecalFitTurnsLongXPrefabsAlongThePlannedAxis()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan();
            plan.Layers.Add(l);
            float yaw = 0.7f;
            var band = new PlannedObject { Role = ObjectRole.Decal, Category = DecalPools.Tyre, Asset = new AssetRef("LongZ"), Yaw = yaw };
            var tar = new PlannedObject { Role = ObjectRole.Decal, Category = DecalPools.Tyre, Asset = new AssetRef("LongX"), Yaw = yaw };
            var mark = new PlannedObject { Role = ObjectRole.Decal, Category = AssetCategories.DecalMarkings, Asset = new AssetRef("LongX"), Yaw = yaw };
            l.Objects.AddRange(new[] { band, tar, mark });
            var assets = new Dictionary<string, AssetInfo>
            {
                ["LongZ"] = new AssetInfo { BoundsMin = new float3(-1, -0.5f, -3), BoundsMax = new float3(1, 0.5f, 3) },
                ["LongX"] = new AssetInfo { BoundsMin = new float3(-5, -0.5f, -2.5f), BoundsMax = new float3(5, 0.5f, 2.5f) },
            };
            Assert.Equal(1, DecalFit.Orient(plan, assets));
            var want = new float2(math.sin(yaw), math.cos(yaw));
            foreach (var o in new[] { band, tar })
            {
                var info = assets[o.Asset.Id];
                var axis = DecalFit.LongAxis(o.Yaw, info.BoundsMin, info.BoundsMax);
                Assert.True(math.abs(math.dot(axis, want)) > 0.999f, o.Asset.Id + " long axis " + axis + " vs " + want);
            }
            Assert.Equal(yaw, mark.Yaw);   // functional markings are placed exactly by their layer
        }

        // ───────────────────────── controls ─────────────────────────

        [Fact]
        public void OldSavesWithoutDecalSettingsGetTheDefaults()
        {
            var s = DressingControls.Read(GeneratorRegistry.Get("park"), new SettingsBag());
            Assert.NotNull(s);
            Assert.Equal(1f, s.Master);
            Assert.Equal(DressStyle.LivedIn, s.Style);
            Assert.Equal(1f, s.Of(DressFamily.Trees));
            Assert.Equal(0f, s.Of(DressFamily.Works));
            var parking = DressingControls.Read(GeneratorRegistry.Get("parking"), new SettingsBag { Values = { ["dressAmount"] = 0.0 } });
            Assert.Equal(0f, parking.Master);
        }

        // ───────────────────────── helpers ─────────────────────────

        private static float AngleBetweenAxes(float yawA, float yawB)
        {
            var a = new float2(math.sin(yawA), math.cos(yawA)); var b = new float2(math.sin(yawB), math.cos(yawB));
            return math.acos(math.saturate(math.abs(math.dot(a, b))));
        }

        private static (float dist, float2 tangent) NearestPath(ComposedPlan p, float2 q)
        {
            float best = float.MaxValue; var tan = new float2(0, 1);
            foreach (var l in p.Layers)
                foreach (var n in l.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide))
                {
                    var prev = n.A.xz;
                    for (int i = 1; i <= 24; i++)
                    {
                        var c = Composer.Bezier(n.A, n.B, n.C, n.D, i / 24f).xz;
                        var ab = c - prev; float t = math.saturate(math.dot(q - prev, ab) / math.max(1e-6f, math.lengthsq(ab)));
                        float d = math.distance(q, prev + ab * t);
                        if (d < best) { best = d; tan = math.normalizesafe(ab); }
                        prev = c;
                    }
                }
            return (best, tan);
        }
    }
}
