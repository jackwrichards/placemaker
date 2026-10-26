// Playtest 4b #2 ("wrong benches, wrong decals, wrong planter boxes"): replays a real player's
// catalog-dump.json through the REAL classifier (AssetHeuristics.Classify / AutoEligible) and pool
// ranking (AutoPoolRanking) and checks what a player would get automatically.
//
// The dump has no component data, so traits are rebuilt from the categories the game assigned
// (tree → Plant|Tree, bench without a bench name → BenchActivity, decal.* → DecalMesh…). Items the
// game made automatic for no category at all were excluded by component data (brand, wall, water
// placement) and stay excluded here.
//
// Dump location: env PM_CATALOG_DUMP, else %USERPROFILE%/AppData/LocalLow/Colossal Order/Cities
// Skylines II/ModsData/Placemaker/catalog-dump.json. Without a dump the replay tests do nothing.
// With PM_WRITE_CURATION=1 the replay also writes docs/ASSET-CURATION.md and refreshes the pools and
// palettes of Tests/Placemaker.Tests/Fixtures/catalog-bounds.json (used by Tests/Placemaker.Gallery).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Placemaker.Core;
using Placemaker.Core.Build;
using Xunit;

namespace Placemaker.Tests
{
    public class AssetCurationTests
    {
        // ───────────────────────── pure rules (no dump needed) ─────────────────────────

        private const AssetTraits Vanilla = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon | AssetTraits.Vanilla;
        private const AssetTraits Mod = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;

        [Theory]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Stains G87 Stain A S Decal", AssetCategories.DecalDirt)]
        [InlineData("G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 01 Curb Decal", AssetCategories.DecalPuddles)]
        [InlineData("RealVision Decals Ground Texture - Park 01 Decal", null)]                 // "Texture" is not "text"
        [InlineData("RealVision Decals Ground Texture - Grass Patch 02 Decal", AssetCategories.DecalDirt)]
        [InlineData("RealVision Decals Ground Graff - 01 Decal", AssetCategories.DecalGraffiti)]
        [InlineData("Fallen leaves decals Leaf Dead Leaves1 Yellow scattered Decal", AssetCategories.DecalLeaves)]
        [InlineData("G87 Trash Decals Dirty Pack Trash G87 Litter 01 Decal", AssetCategories.DecalLitter)]
        [InlineData("AlphabetDecalA01", AssetCategories.DecalMarkings)]
        [InlineData("EU RoadArrow Forward", AssetCategories.DecalMarkings)]
        [InlineData("Cracks and Damage Decal Pack RoadAssets Crack - Small 01 Decal", AssetCategories.DecalCracks)]
        public void DecalThemesUseTheLastThemeWord(string name, string theme) =>
            Assert.Equal(theme, AssetHeuristics.DecalTheme(name));

        [Theory]
        [InlineData(AssetCategories.DecalMarkings, "AlphabetDecalA01", false)]
        [InlineData(AssetCategories.DecalMarkings, "NumberDecal101", false)]
        [InlineData(AssetCategories.DecalMarkings, "TennisCourtDecal01", false)]
        [InlineData(AssetCategories.Decal, "ContainerGridDecal01", false)]
        [InlineData(AssetCategories.Decal, "PlaygroundDecal01", false)]
        [InlineData(AssetCategories.DecalDirt, "G87 Dirty Roads Stains G87 Dirt Road Transition 01 Decal", false)]
        [InlineData(AssetCategories.DecalDirt, "RealVision Decals Ground Texture - Dirt 01 Decal", false)]   // s23: RealVision textures are hard-edged tiles
        [InlineData(AssetCategories.DecalLeaves, "Sand Surfaces and Beach Decals Beach Palm-leaf-01 Decal", false)]
        [InlineData(AssetCategories.DecalLeaves, "Fallen leaves decals Leaf Dead Leaves8 Brown light Decal", true)]   // s28: brown/green light leaves only
        [InlineData(AssetCategories.DecalPuddles, "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 01 Curb Decal", false)]
        public void DecalPoolsAreNatural(string category, string name, bool eligible) =>
            Assert.Equal(eligible, AssetHeuristics.AutoEligible(category, name, Mod | AssetTraits.DecalMesh));

        [Fact]
        public void ShrubsAreBushesAndRaisedBedsArePlanters()
        {
            var tree = Vanilla | AssetTraits.Plant | AssetTraits.Tree;
            Assert.Contains(AssetCategories.Bush, AssetHeuristics.Classify("GreenBushWild01", tree));
            Assert.DoesNotContain(AssetCategories.Tree, AssetHeuristics.Classify("FlowerBushWild02", tree));
            var bed = AssetHeuristics.Classify("GardenBed01WideHigh", Vanilla | AssetTraits.Plant);
            Assert.DoesNotContain(AssetCategories.Bush, bed);
            Assert.Contains(AssetCategories.Planter, bed);
            Assert.DoesNotContain(AssetCategories.Planter, AssetHeuristics.Classify("ParkMaintenanceDepot01", Vanilla));
            Assert.Empty(AssetHeuristics.Classify("EU_DoNotEnter01", Vanilla).Where(c => c.StartsWith("camping")));
            Assert.Equal(1, AssetHeuristics.Classify("AppleTree01", tree).Count(c => c == AssetCategories.Tree));
        }

        [Theory]
        [InlineData(AssetCategories.Bench, "Brickworks-Planter-2x2m", false)]
        [InlineData(AssetCategories.Bench, "TramStation Long Bench", false)]
        [InlineData(AssetCategories.Bench, "Rustic Park Bench 01", true)]
        [InlineData(AssetCategories.Bush, "VFP EvergreenCityBush01", false)]
        [InlineData(AssetCategories.Bush, "VFP SagoPalmBush", true)]
        [InlineData(AssetCategories.Kiosk, "SupermarketLidl", false)]
        public void ModdedFallbacksDropMismatches(string category, string name, bool eligible)
        {
            var t = Mod | (category == AssetCategories.Bush ? AssetTraits.Plant : AssetTraits.None) | (name.Contains("Planter") ? AssetTraits.BenchActivity : AssetTraits.None);
            Assert.Equal(eligible, AssetHeuristics.AutoEligible(category, name, t));
        }

        [Theory]
        [InlineData("palette:CityPark04", true)]
        [InlineData("palette:Basic Park 2x2", true)]
        [InlineData("palette:AmericanFootballField01", false)]
        [InlineData("palette:CampfireSite01", false)]
        [InlineData("palette:BicyclePark01", false)]
        [InlineData("palette:NotreDame", false)]
        public void OnlyOrdinaryParksArePalettes(string palette, bool general) =>
            Assert.Equal(general, AssetHeuristics.IsGeneralParkPalette(palette.Substring("palette:".Length)));

        [Theory]
        [InlineData(AssetCategories.WaterJets, "AgricultureSprinklerSystem", false)]   // in-game: a centre-pivot rig as splash pad
        [InlineData(AssetCategories.WaterJets, "Prop_TractorSprayer01", false)]
        [InlineData(AssetCategories.Playground, "Prop_CombineHarvester01", false)]
        [InlineData(AssetCategories.Decoration, "SiloSculpture01", false)]
        [InlineData(AssetCategories.WaterJets, "SplashPadJet01", true)]
        public void LeisurePoolsNeverTakeMachinery(string category, string name, bool eligible)
        {
            var c = AssetHeuristics.Classify(name, Mod);
            Assert.Equal(eligible, AssetHeuristics.AutoEligible(category, name, Mod));
        }

        [Fact]
        public void GiantBoundsAreDroppedFromFurniturePools()
        {
            var pool = AutoPoolRanking.Build(AssetCategories.WaterJets, new[]
            {
                new PoolCandidate("BigRig", true, footprint: 16f), new PoolCandidate("Jet", true, footprint: 0.6f),
            });
            Assert.Equal(new[] { "Jet" }, pool);
            Assert.False(AssetCuration.FitsSize(AssetCategories.Bench, 143f));
            Assert.True(AssetCuration.FitsSize(AssetCategories.Fence, 500f));   // nets/fences: no cap
            // Every category has a sanity cap, even ones no rule lists (future types).
            Assert.False(AssetCuration.FitsSize("someFutureType.prop", 56f));
            Assert.False(AssetCuration.FitsSize("industrial.container", 70f));
            Assert.False(AssetCuration.FitsSize("cemetery.grave", 6.5f));
            Assert.True(AssetCuration.FitsSize(AssetCategories.SurfacePaving, 500f));
        }

        // Art-pass findings (proposals art-a2/a3/a6): the shared pools themselves stay clean for every type.
        [Theory]
        [InlineData(AssetCategories.Lookout, "RuralFireStation_WatchTowerUpgrade", AssetTraits.BuildingPart)]   // 56 m fire-station upgrade
        [InlineData(AssetCategories.Lookout, "RuralFireStation_WatchTowerUpgrade", AssetTraits.None)]           // name alone
        [InlineData(AssetCategories.BikeRack, "BicycleParkingHall01 Bicycle Maintenance", AssetTraits.BuildingPart)]
        [InlineData(AssetCategories.BikeRack, "BicycleParkingHallSign01", AssetTraits.None)]
        [InlineData(AssetCategories.CenterpieceStatue, "TrainStation01ClockPointers01", AssetTraits.None)]
        [InlineData(AssetCategories.CenterpieceStatue, "NotreDameClockPointers", AssetTraits.None)]
        [InlineData("monument.column", "SubwayStationElevated02Pillar", AssetTraits.None)]
        [InlineData("monument.column", "2LaneSuspensionBridgePillar", AssetTraits.None)]
        [InlineData(AssetCategories.WaterJets, "AgricultureSprinklerSystem", AssetTraits.None)]
        [InlineData("play.nature", "Citizen_Logger_Chainsaw01", AssetTraits.ActivityProp)]
        [InlineData("play.nature", "Citizen_Logger_Chainsaw01", AssetTraits.None)]
        [InlineData(AssetCategories.Flowerbed, "Flowerbed Planter Box 01", AssetTraits.Plant)]
        [InlineData("cemetery.grave", "OldCemeteryMausoleum01", AssetTraits.None)]
        [InlineData(AssetCategories.Kiosk, "Stand01 - ShortMarket", AssetTraits.None)]
        [InlineData(AssetCategories.Kiosk, "Billboard Supermarket Kiosk", AssetTraits.None)]
        [InlineData("events.tent", "IKEA Marquee Sign", AssetTraits.None)]
        [InlineData(AssetCategories.Stage, "Big Billboard Stage", AssetTraits.None)]
        [InlineData(AssetCategories.CafeSeating, "Mall Food Court Table", AssetTraits.None)]
        [InlineData(AssetCategories.Playground, "Prop_Tractor01", AssetTraits.Vehicle)]
        [InlineData(AssetCategories.Decoration, "Prop_ParkMaintenanceVehicle01", AssetTraits.None)]
        [InlineData("sports.basketball", "Basketball01", AssetTraits.ActivityProp)]
        [InlineData("memorial.flag", "FlagPole01_Clt", AssetTraits.None)]
        [InlineData(AssetCategories.ParkingDecal, "BikeRack01", AssetTraits.ParkingLaneSubLane)]
        public void SharedPoolsRejectArtPassPollution(string category, string name, AssetTraits extra) =>
            Assert.False(AssetHeuristics.AutoEligible(category, name, Mod | extra), category + " took " + name);

        [Theory]
        [InlineData("waterfront.fishing", "FishingRod01_withFish", AssetTraits.ActivityProp)]   // anglers' props stay
        [InlineData("farm.machinery", "Prop_Tractor01", AssetTraits.Vehicle)]                   // work sites keep vehicles
        [InlineData("transport.sign", "ParkSignpost01", AssetTraits.None)]                      // signage keeps signs
        [InlineData("sports.basketball", "BasketballHoopStand01", AssetTraits.None)]            // not an ad stand
        [InlineData("cemetery.grave", "OldCemeteryTombstone01", AssetTraits.None)]
        public void SharedPoolsKeepTheRealThing(string category, string name, AssetTraits extra) =>
            Assert.True(AssetHeuristics.AutoEligible(category, name, Mod | extra), category + " lost " + name);

        [Fact]
        public void PrefabKindsComeFromClassAndIconNames()
        {
            Assert.Equal(AssetTraits.BuildingPart, AssetHeuristics.TraitsFromIcon("thumbnail://ThumbnailCamera/BuildingExtensionPrefab/WaterTower01%20Viewing%20Deck?width=128"));
            Assert.Equal(AssetTraits.Vehicle, AssetHeuristics.TraitsFromIcon("coui://ail/CarTrailerPrefab.TractorTrailer01.png"));
            Assert.Equal(AssetTraits.ActivityProp, AssetHeuristics.TraitsFromIcon("coui://ail/ActivityPropPrefab.Citizen_Logger_Chainsaw01.png"));
            Assert.Equal(AssetTraits.None, AssetHeuristics.TraitsFromIcon("coui://ail/StaticObjectPrefab.Bench01.png"));
            Assert.True(AssetHeuristics.IsVehicleName("Prop_CombineHarvester01"));
            Assert.False(AssetHeuristics.IsVehicleName("Prop_Trashbin01_Empty"));
            Assert.False(AssetHeuristics.IsVehicleName("Prop_CoalTruck01_CoalPile01_Full"));
            // Bike racks with bike parking lanes are bike racks, not car stalls.
            Assert.Contains(AssetCategories.BikeRack, AssetHeuristics.Classify("BikeRack01", Vanilla | AssetTraits.HasSubLane | AssetTraits.ParkingLaneSubLane));
        }

        // ───────────────────────── replay of a real catalog ─────────────────────────

        /// <summary>Categories the layers use for everyday dressing (the audit list of docs/ASSET-CURATION.md).</summary>
        public static readonly string[] Audited =
        {
            AssetCategories.Bench, AssetCategories.Lamp, AssetCategories.Bin, AssetCategories.Tree, AssetCategories.Bush,
            AssetCategories.Flowerbed, AssetCategories.Planter, AssetCategories.Hedge, AssetCategories.Fence,
            AssetCategories.Centerpiece, AssetCategories.CenterpieceFountain, AssetCategories.CenterpieceStatue,
            AssetCategories.Playground, AssetCategories.Picnic, AssetCategories.Restroom, AssetCategories.Kiosk,
            AssetCategories.Shelter, AssetCategories.Bollard, AssetCategories.Rock, AssetCategories.Decoration,
            AssetCategories.Decal, AssetCategories.DecalLeaves, AssetCategories.DecalDirt, AssetCategories.DecalCracks,
            AssetCategories.DecalPuddles, AssetCategories.DecalPaving, AssetCategories.DecalGraffiti, AssetCategories.DecalLitter,
            AssetCategories.DecalMarkings, AssetCategories.CampingPitch, AssetCategories.CampingFire, AssetCategories.Sports,
        };

        public sealed class DumpItem
        {
            public string Id;
            public bool Vanilla;
            public HashSet<string> GameCategories = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Palettes = new HashSet<string>(StringComparer.Ordinal);
            public bool AnyAuto;
            public float NetWidth;
            public string Icon;
            public float[] Size;   // x, y, z
            public JsonNode Bounds;
        }

        public sealed class Replay
        {
            public Dictionary<string, DumpItem> Items = new Dictionary<string, DumpItem>(StringComparer.Ordinal);
            public Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public Dictionary<string, List<string>> OldPools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public Dictionary<string, Dictionary<string, List<string>>> Palettes = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
            public List<string> UsablePalettes = new List<string>();
            public string Source = "";
        }

        public static string DumpPath()
        {
            var env = Environment.GetEnvironmentVariable("PM_CATALOG_DUMP");
            if (!string.IsNullOrEmpty(env)) return env;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "AppData", "LocalLow", "Colossal Order", "Cities Skylines II", "ModsData", "Placemaker", "catalog-dump.json");
        }

        private static Replay _replay;
        private static readonly object Gate = new object();

        public static Replay Load()
        {
            lock (Gate)
            {
                if (_replay != null) return _replay;
                var path = DumpPath();
                if (!File.Exists(path)) return null;
                _replay = Run(JsonNode.Parse(File.ReadAllText(path)));
                return _replay;
            }
        }

        /// <summary>Component traits rebuilt from the game's own categories of the item.</summary>
        public static AssetTraits TraitsOf(DumpItem it)
        {
            var c = it.GameCategories;
            bool Has(string x) => c.Contains(x);
            bool Any(string prefix) => c.Any(x => x == prefix || x.StartsWith(prefix + ".", StringComparison.Ordinal));
            var t = AssetTraits.None;
            if (it.Vanilla) t |= AssetTraits.Vanilla;
            if (Any("surface")) return t | AssetTraits.Surface;
            if (Has(Placemaker.Core.Build.BuildCategories.AreaLot)) return t | AssetTraits.LotArea;
            if (Has(Placemaker.Core.Build.BuildCategories.AreaPedestrian) || Has(Placemaker.Core.Build.BuildCategories.AreaHangaround)) return t | AssetTraits.SpaceArea;
            if (Has(AssetCategories.Zone)) return t | AssetTraits.Zone;
            if (c.Any(x => x.StartsWith("net.", StringComparison.Ordinal)) || (Has(AssetCategories.Fence) && it.NetWidth > 0f))
            {
                t |= AssetTraits.Net;
                if (Has(AssetCategories.Fence)) t |= AssetTraits.NetFence;
                if (Has(AssetCategories.Pathway) || Has("net.bike") || Has("net.trail")) t |= AssetTraits.Pathway;
                if (Has(AssetCategories.Road) || Has(AssetCategories.InvisiblePath)) t |= AssetTraits.Road;
                return t;
            }
            t |= AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            t |= AssetHeuristics.TraitsFromIcon(it.Icon);   // BuildingExtensionPrefab / CarPrefab / ActivityPropPrefab
            if (Any(AssetCategories.Decal)) t |= AssetTraits.DecalMesh;
            if (Any(AssetCategories.Tree)) t |= AssetTraits.Plant | AssetTraits.Tree;
            if (Has(AssetCategories.Bush) || Has(AssetCategories.Planter) || Has(AssetCategories.Flowerbed)) t |= AssetTraits.Plant;
            if (Has(AssetCategories.Bench) && !AssetHeuristics.IsBenchName(it.Id.ToLowerInvariant())) t |= AssetTraits.BenchActivity;
            if (Has(AssetCategories.Lamp) && !AssetHeuristics.ContainsAny(it.Id.ToLowerInvariant(), "lamp", "lantern", "streetlight", "street light")) t |= AssetTraits.StreetLight;
            if (Has(AssetCategories.SportsFitness)) t |= AssetTraits.ExerciseActivity;
            if (Has(AssetCategories.ParkingDecal)) t |= AssetTraits.HasSubLane | AssetTraits.ParkingLaneSubLane;
            if (Has(AssetCategories.Charger)) t |= AssetTraits.Charger;
            // Automatic for nothing in the game: excluded by component data (brand, wall/hanging, water placement).
            // Decals are never brand objects; a decal the game made automatic for nothing only failed the curated themes.
            if (!it.AnyAuto && !Any(AssetCategories.Decal)) t |= AssetTraits.BrandObject;
            return t;
        }

        public static Replay Run(JsonNode dump)
        {
            var r = new Replay { Source = "catalog-dump.json v" + dump["version"] + " " + dump["written"] };
            foreach (var kv in dump["categories"].AsObject())
                foreach (var node in kv.Value.AsArray())
                {
                    var id = (string)node["id"];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!r.Items.TryGetValue(id, out var it)) r.Items[id] = it = new DumpItem { Id = id, Vanilla = (bool)node["vanilla"] };
                    foreach (var tagNode in node["tags"].AsArray())
                    {
                        var tag = (string)tagNode;
                        if (tag == "auto") it.AnyAuto = true;
                        else if (tag.StartsWith("palette:", StringComparison.Ordinal)) it.Palettes.Add(tag);
                        else if (!tag.StartsWith("source:", StringComparison.Ordinal) && !tag.StartsWith("auto:", StringComparison.Ordinal)) it.GameCategories.Add(tag);
                    }
                    it.GameCategories.Add(kv.Key);
                    if (node["netWidth"] != null) it.NetWidth = (float)(double)node["netWidth"];
                    if (it.Icon == null) it.Icon = (string)node["icon"];
                    var b = node["bounds"];
                    if (b != null && it.Bounds == null)
                    {
                        it.Bounds = b.DeepClone();
                        it.Size = b["size"].AsArray().Select(x => (float)(double)x).ToArray();
                    }
                }
            foreach (var kv in dump["automaticPools"].AsObject())
                r.OldPools[kv.Key] = kv.Value.AsArray().Select(x => (string)x).ToList();

            var eligible = new Dictionary<string, List<PoolCandidate>>(StringComparer.Ordinal);
            var paletteCands = new Dictionary<string, Dictionary<string, List<PoolCandidate>>>(StringComparer.Ordinal);
            foreach (var it in r.Items.Values)
            {
                var t = TraitsOf(it);
                foreach (var cat in AssetHeuristics.Classify(it.Id, t))
                {
                    if (!AssetHeuristics.AutoEligible(cat, it.Id, t)) continue;
                    var cand = new PoolCandidate(it.Id, it.Vanilla, (t & AssetTraits.NetFence) != 0, it.NetWidth,
                        it.Size == null ? 0f : Math.Max(it.Size[0], it.Size[2]));
                    if (!eligible.TryGetValue(cat, out var l)) eligible[cat] = l = new List<PoolCandidate>();
                    l.Add(cand);
                    if (!AutoAssets.PaletteCategories.Contains(cat)) continue;
                    foreach (var p in it.Palettes)
                    {
                        if (!paletteCands.TryGetValue(p, out var byCat)) paletteCands[p] = byCat = new Dictionary<string, List<PoolCandidate>>(StringComparer.Ordinal);
                        if (!byCat.TryGetValue(cat, out var pl)) byCat[cat] = pl = new List<PoolCandidate>();
                        pl.Add(cand);
                    }
                }
            }
            foreach (var kv in eligible) r.Pools[kv.Key] = AutoPoolRanking.Build(kv.Key, kv.Value);
            foreach (var kv in paletteCands)
            {
                var byCat = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var c in kv.Value) byCat[c.Key] = AutoPoolRanking.Build(c.Key, c.Value, int.MaxValue);
                r.Palettes[kv.Key] = byCat;
                int core = new[] { AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Bench, AssetCategories.Lamp, AssetCategories.Bin }.Count(byCat.ContainsKey);
                if (core >= 2 && AssetHeuristics.IsGeneralParkPalette(kv.Key.Substring("palette:".Length))) r.UsablePalettes.Add(kv.Key);
            }
            r.UsablePalettes.Sort(StringComparer.Ordinal);
            return r;
        }

        private static List<string> Pool(Replay r, string category) => r.Pools.TryGetValue(category, out var l) ? l : new List<string>();

        [Fact]
        public void RealCatalogPoolsMatchWhatAPlayerExpects()
        {
            var r = Load();
            if (r == null) return;   // no dump on this machine
            void FirstStartsWith(string category, params string[] prefixes)
            {
                var pool = Pool(r, category);
                if (pool.Count == 0) return;
                Assert.True(prefixes.Any(p => pool[0].StartsWith(p, StringComparison.Ordinal)), category + " default is " + pool[0]);
            }
            void NoneContains(string category, params string[] parts)
            {
                foreach (var id in Pool(r, category))
                    Assert.False(parts.Any(p => id.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0), category + " pool has " + id);
            }
            FirstStartsWith(AssetCategories.Bench, "GardenBench");
            FirstStartsWith(AssetCategories.Lamp, "LightpolePark");
            FirstStartsWith(AssetCategories.Bin, "Trashbin");
            FirstStartsWith(AssetCategories.Bush, "GreenBushWild", "FlowerBushWild");
            NoneContains(AssetCategories.Bench, "Planter", "Sofa", "Chair", "Sport", "Stadium", "Tram", "School", " ");
            NoneContains(AssetCategories.Bush, "GardenBed", "City", "Pot", "Dead");
            NoneContains(AssetCategories.Tree, "Bush", "Palm");
            NoneContains(AssetCategories.Planter, "Depot", "Garage", "Wild");
            NoneContains(AssetCategories.Centerpiece, "Wall", "Clock");
            NoneContains(AssetCategories.Kiosk, "Pier", "Ticket", "Supermarket", "Billboard");
            NoneContains(AssetCategories.Playground, "School");
            NoneContains(AssetCategories.CampingPitch, "DoNotEnter");
            NoneContains(AssetCategories.Bollard, "Mooring");
            NoneContains(AssetCategories.WaterJets, "Agricultur", "Tractor", "Sprayer");
            // Art-pass findings, fixed in the shared pools (docs/proposals/art-a2/a3/a6).
            NoneContains(AssetCategories.Lookout, "Upgrade", "Viewing Deck", "FireStation");
            NoneContains(AssetCategories.BikeRack, "Hall", "Sign");
            NoneContains(AssetCategories.CenterpieceStatue, "Clock", "Pointer");
            NoneContains("monument.column", "Bridge", "Subway", "Pier");
            NoneContains("ruins.pillar", "Bridge", "Subway");
            NoneContains("play.nature", "Citizen", "Chainsaw");
            NoneContains("cemetery.grave", "Mausoleum");
            NoneContains(AssetCategories.Flowerbed, "Planter", "Box");
            NoneContains("events.tent", "IKEA", "Sign");
            NoneContains(AssetCategories.ParkingDecal, "Bicycle", "Bike");
            foreach (var c in new[] { AssetCategories.Kiosk, "kiosk.stall", AssetCategories.Stage, AssetCategories.CafeSeating, AssetCategories.Bench })
                NoneContains(c, "Billboard", "Supermarket", "Poster", "Stand01", "Stand02", " - ");
            foreach (var kv in r.Pools)
                foreach (var id in kv.Value)
                {
                    var t = TraitsOf(r.Items[id]);
                    Assert.False((t & AssetTraits.BuildingPart) != 0, kv.Key + " pool has the building part " + id);
                    if (kv.Key != "waterfront.fishing") Assert.False((t & AssetTraits.ActivityProp) != 0, kv.Key + " pool has the hand-held prop " + id);
                    if (AssetCuration.IsLeisureCategory(kv.Key)) Assert.False((t & AssetTraits.Vehicle) != 0, kv.Key + " pool has the vehicle " + id);
                }
            // Every leisure pool: no machinery, nothing bigger than the category's size cap.
            foreach (var kv in r.Pools)
            {
                if (!AssetCuration.IsLeisureCategory(kv.Key)) continue;
                foreach (var id in kv.Value)
                {
                    Assert.False(AssetCuration.IsMachineryForLeisure(kv.Key, id.ToLowerInvariant()), kv.Key + " pool has " + id);
                    var s = r.Items[id].Size;
                    if (s != null) Assert.True(AssetCuration.FitsSize(kv.Key, Math.Max(s[0], s[2])), kv.Key + " pool has a giant " + id);
                }
            }
            foreach (var c in new[] { AssetCategories.Decal, AssetCategories.DecalLeaves, AssetCategories.DecalDirt, AssetCategories.DecalCracks,
                         AssetCategories.DecalPuddles, AssetCategories.DecalPaving, AssetCategories.DecalMarkings })
                NoneContains(c, "Alphabet", "Number", "Text ", "TextDecal", "RoadArrow", "Speed", "Roundabout", "Curb", "Grid", "PlaygroundDecal", "Chirper");
            NoneContains(AssetCategories.DecalLeaves, "Palm", "Beach");
            NoneContains(AssetCategories.DecalDirt, "Road", "Trash", "Seaweed");
            // Furniture pools hold furniture-sized props only (no building extensions).
            foreach (var c in new[] { AssetCategories.Bench, AssetCategories.Lamp, AssetCategories.Bin, AssetCategories.Bush, AssetCategories.Planter,
                         AssetCategories.Picnic, AssetCategories.Playground, AssetCategories.Kiosk })
                foreach (var id in Pool(r, c))
                    if (r.Items[id].Size != null)
                        Assert.True(math_max(r.Items[id].Size[0], r.Items[id].Size[2]) < 16f, c + " pool has a " + r.Items[id].Size[0] + " m wide " + id);
            foreach (var kv in r.Pools) Assert.Equal(kv.Value.Count, kv.Value.Distinct().Count());
            Assert.DoesNotContain("palette:AmericanFootballField01", r.UsablePalettes);
            foreach (var p in r.UsablePalettes)
                if (r.Palettes[p].TryGetValue(AssetCategories.Bush, out var bushes))
                    Assert.DoesNotContain(bushes, b => b.StartsWith("GardenBed", StringComparison.Ordinal));

            if (Environment.GetEnvironmentVariable("PM_WRITE_CURATION") == "1")
            {
                var repo = RepoRoot();
                File.WriteAllText(Path.Combine(repo, "docs", "ASSET-CURATION.md"), Markdown(r));
                RefreshFixture(r, Path.Combine(repo, "Tests", "Placemaker.Tests", "Fixtures", "catalog-bounds.json"));
            }
        }

        private static float math_max(float a, float b) => a > b ? a : b;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        }

        private static string Markdown(Replay r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Asset curation — automatic picks (playtest 4b #2)");
            sb.AppendLine();
            sb.AppendLine("Generated by `Tests/Placemaker.Tests/AssetCurationTests.cs` (run with `PM_WRITE_CURATION=1 dotnet test Tests/Placemaker.Tests --filter AssetCuration`).");
            sb.AppendLine("It replays the player's `" + r.Source + "` through the real classifier (`AssetHeuristics` + `AssetCuration`)");
            sb.AppendLine("and pool ranking (`AutoPoolRanking`, shared with the game). Traits are rebuilt from the game's categories (the dump has no");
            sb.AppendLine("component data); items the game made automatic for nothing (brand / wall / water placement) stay excluded.");
            sb.AppendLine();
            sb.AppendLine("Rules: `Core/Build/AssetCuration.cs` — vanilla allow-lists per core category (list order = preference), modded");
            sb.AppendLine("name fallbacks minus known mismatches, word-based decal themes (last theme word wins), park-only palettes;");
            sb.AppendLine("pools take vanilla items when the category has any, modded ones otherwise. Variety categories (trees, bushes,");
            sb.AppendLine("flowerbeds, rocks, decals, playground) draw from the first 6; the others always use the first item, unless the");
            sb.AppendLine("step's park palette has the category (variety categories need ≥ 3 palette items).");
            sb.AppendLine();
            sb.AppendLine("Visual check: object icons are not on disk (game `coui://ail/...` sprites) and the thumbs folder holds surface");
            sb.AppendLine("swatches only, so nothing here was confirmed by eye; picks were checked by name, tags and real bounds");
            sb.AppendLine("(footprint/height column) — e.g. raised beds are hard 2×2 m boxes, wild bushes 1.3–2 m organic plants.");
            sb.AppendLine("Layouts were checked in the gallery (`Tests/Placemaker.Gallery`, which now also draws terrain paint):");
            sb.AppendLine("park/features-all shows rocky outcrops as soft graded paint blobs with small odd rock huddles.");
            sb.AppendLine("Decal pools also rank by real footprint: sheets wider than 12 m go last (RealVision 30–60 m ground textures).");
            sb.AppendLine();
            sb.AppendLine("Shared pool hygiene (art-pass follow-up, `AssetCuration.NeverAutomatic` + `MaxFootprint`), for every category and future");
            sb.AppendLine("type: building extensions/upgrades (BuildingExtensionPrefab, \"…Upgrade\", wings, clock pointers), citizens' hand-held activity");
            sb.AppendLine("props (except `waterfront.fishing`), vehicle props in leisure pools, advertising/shop props (billboards, ad stands and their");
            sb.AppendLine("\"X - Brand\" variants, signs outside sign categories, supermarkets/malls), bridge/subway/pier pillars and cloth sub-meshes are");
            sb.AppendLine("never automatic; per-category words drop mausoleums from grave rows, planter boxes from flowerbeds, farm sprinklers from jets.");
            sb.AppendLine("Every object category has a footprint cap (its own, its parent's, else 24 m leisure / 40 m work). The table lists every pool.");
            sb.AppendLine();
            sb.AppendLine("| category | before (game log) | after: first 10 automatic picks (footprint m × height m) |");
            sb.AppendLine("|---|---|---|");
            foreach (var c in Audited.Concat(r.Pools.Keys.Where(k => !Audited.Contains(k)).OrderBy(k => k, StringComparer.Ordinal)))
            {
                var before = r.OldPools.TryGetValue(c, out var o) ? string.Join(", ", o.Take(5)) + (o.Count > 5 ? "…" : "") : "—";
                var after = Pool(r, c).Take(10).Select(id =>
                {
                    var s = r.Items[id].Size;
                    return s == null ? id : $"{id} ({s[0]:0.#}×{s[2]:0.#}×{s[1]:0.#})";
                });
                var a = string.Join(", ", after);
                sb.AppendLine($"| `{c}` | {Esc(before)} | {Esc(a.Length > 0 ? a : "— (none: explicit picks only; layers skip or fall back)")} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Park palettes usable for automatic dressing");
            sb.AppendLine();
            sb.AppendLine(r.UsablePalettes.Count + " of " + r.Palettes.Count + ": " + string.Join(", ", r.UsablePalettes.Select(p => p.Substring(8))) + ".");
            sb.AppendLine();
            sb.AppendLine("| palette | bench | lamp | bush | tree | planter |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var p in r.UsablePalettes)
            {
                string Cell(string cat) => r.Palettes[p].TryGetValue(cat, out var l) ? string.Join(", ", l.Take(4)) + (l.Count > 4 ? "…" : "") : "(pool)";
                sb.AppendLine($"| {p.Substring(8)} | {Cell(AssetCategories.Bench)} | {Cell(AssetCategories.Lamp)} | {Cell(AssetCategories.Bush)} | {Cell(AssetCategories.Tree)} | {Cell(AssetCategories.Planter)} |");
            }
            return sb.ToString();
        }

        private static string Esc(string s) => s.Replace("|", "\\|");

        /// <summary>Rewrites the gallery fixture's pools and palettes with the curated replay (keeps its footprints, adds missing ones).</summary>
        private static void RefreshFixture(Replay r, string path)
        {
            if (!File.Exists(path)) return;
            var root = JsonNode.Parse(File.ReadAllText(path)).AsObject();
            var pools = new JsonObject();
            foreach (var kv in r.Pools.OrderBy(k => k.Key, StringComparer.Ordinal)) pools[kv.Key] = new JsonArray(kv.Value.Select(x => (JsonNode)x).ToArray());
            var palettes = new JsonObject();
            foreach (var p in r.UsablePalettes)
            {
                var o = new JsonObject();
                foreach (var kv in r.Palettes[p]) o[kv.Key] = new JsonArray(kv.Value.Select(x => (JsonNode)x).ToArray());
                palettes[p] = o;
            }
            var items = root["items"].AsObject();
            foreach (var id in r.Pools.Values.SelectMany(l => l).Concat(r.Palettes.Values.SelectMany(m => m.Values.SelectMany(l => l))).Distinct())
            {
                if (items.ContainsKey(id) || !r.Items.TryGetValue(id, out var it) || it.Bounds == null) continue;
                var min = it.Bounds["min"].AsArray(); var max = it.Bounds["max"].AsArray();
                var e = new JsonObject { ["b"] = new JsonArray((double)min[0], (double)min[2], (double)max[0], (double)max[2], (double)max[1]) };
                if (it.NetWidth > 0f) e["w"] = Math.Round(it.NetWidth, 2);
                items[id] = e;
            }
            root["source"] = r.Source + " (pools/palettes replayed through the curated classifier)";
            root["pools"] = pools;
            root["palettes"] = palettes;
            File.WriteAllText(path, root.ToJsonString());
        }
    }
}
