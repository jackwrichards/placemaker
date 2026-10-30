// Art pass (a1, 2026-10-13): explicit, verified default assets for the park cluster (park, campground,
// picnic grounds, dog park, adventure playground, memorial park, orchard park, cemetery grounds).
//
// Why explicit ids instead of the automatic category pools: the player's catalog dump showed the shared
// pools carry things that do not belong in a park — "bench" holds bus stops, cafe chairs and school
// extension wings, "lamp" holds wall and roof lights, "play.nature" starts with a chainsaw-wielding logger,
// "water.jets" is an agricultural sprinkler, "centerpiece.statue" is a pair of clock hands. Every id below
// is vanilla and was checked against catalog-dump.json; AssetRef fallbacks keep the list order, and an id
// a playset lacks falls through to the next one and finally to the automatic pool (Build/AutoAssets.Set).
// A player's own asset choice always wins: these are only used when the control is left on automatic.

namespace Placemaker.Core.Layers.Park
{
    public static class ParkArt
    {
        // ───── furniture ─────
        public static readonly string[] Benches = { "GardenBench01", "GardenBench02", "GardenBench03", "GardenBench04" };
        public static readonly string[] RusticBenches = { "CampgroundBench01", "CampgroundBench02", "GardenBench04" };
        public static readonly string[] ParkLamps = { "LightpolePark01", "LightpolePark02", "LightpolePark04", "EU_LightpolePark01" };
        public static readonly string[] LowLights = { "GardenLight01", "GardenLight03", "GardenLight02" };
        public static readonly string[] Bins = { "Trashbin02", "Trashbin01", "Trashbin03" };
        public static readonly string[] Gazebos = { "Gazebo01", "Gazebo02", "Gazebo03" };
        public static readonly string[] Pergolas = { "Pergola01", "Pergola03", "Pergola05" };
        public static readonly string[] Signposts = { "ParkSignpost01", "ParkSignpost02", "ParkInfosign01" };
        public static readonly string[] Toilets = { "Outhouse01", "Outhouse02" };

        // ───── planting ─────
        /// <summary>Broadleaf parkland trees (no palms, no conifers, no fruit trees).</summary>
        public static readonly string[] ParkTrees = { "OakTree01", "NA_LindenTree01", "EU_ChestnutTree01", "NA_LondonPlaneTree01", "NA_HickoryTree01", "BirchTree01" };
        /// <summary>Avenue trees: one even, upright species reads as an allée.</summary>
        public static readonly string[] AvenueTrees = { "NA_LindenTree01", "NA_LondonPlaneTree01" };
        public static readonly string[] WoodlandTrees = { "OakTree01", "BirchTree01", "EU_AlderTree01", "NA_HickoryTree01", "SpruceTree01", "PineTree01" };
        public static readonly string[] Conifers = { "PineTree01", "SpruceTree01", "EuropeanSpruce01" };
        public static readonly string[] WaterTrees = { "EU_AlderTree01", "EU_PoplarTree01", "BirchTree01" };
        public static readonly string[] FlowerShrubs = { "FlowerBushWild02", "GreenBushWildAdult01", "FlowerBushWildChild02", "FlowerBushWild02", "GreenBushWildElderly01", "GreenBushWildTeen01", "FlowerBushWild02" };   // (makeover) lilac massed with greens of three sizes: one species in one size read as polka dots   // (makeover) never FlowerBushWild01 or FlowerBushWildElderly01: their pale teal clashes with every lawn (bush test run 20261030-003549-bushes) (run show-24-builds-1030-001417-7e24)
        public static readonly string[] GreenShrubs = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWildTeen01", "GreenBushWild02" };
        public static readonly string[] MixedShrubs = { "GreenBushWild01", "FlowerBushWild02", "GreenBushWildAdult01", "GreenBushWildElderly01", "FlowerBushWildChild02", "GreenBushWildTeen01", "GreenBushWild02", "GreenBushWildChild01" };
        public static readonly string[] LowHedge = { "HedgePieceLow01", "HedgePieceLow02" };
        public static readonly string[] HighHedge = { "HedgePieceHigh01", "HedgePieceHigh02" };
        public static readonly string[] Flowerbeds = { "Flowerbed01", "Flowerbed02" };

        // ───── ground ─────
        public const string Grass = "Grass Surface 01";
        public const string Meadow = "Grass Surface 02";
        public const string Forest = "Forestry Surface 01";
        public const string Sand = "Sand Surface 01";
        public const string Pavement = "Pavement Surface 01";
        public const string Tiles = "Tiles Surface 01";
        /// <summary>Bark mulch (modded) for play and tent pitches, falling back to the vanilla forest floor.</summary>
        public static readonly string[] Mulch = { "EAI - Surface Pack Ground Mulch Surface", Forest };
    }
}

namespace Placemaker.Core.Layers.Park
{
    /// <summary>Campground defaults (vanilla CampfireSite palette: makeshift tents, campground table sets and shelters, info signs).</summary>
    public static class CampArt
    {
        public static readonly string[] Tents = { "MakeshiftTent01", "MakeshiftTent02", "MakeshiftTent03", "MakeshiftTent04" };
        /// <summary>No vanilla campfire prop exists; a small charcoal grill stands in for the pitch fire.</summary>
        public static readonly string[] Fire = { "Grill02", "Grill01" };
        public static readonly string[] Tables = { "CampgroundTableset01", "CampgroundTableset02" };
        public static readonly string[] Pad = { ParkArt.Forest };
        /// <summary>Caravan hardstanding: fine gravel (Terrain Surfaces pack), the forest floor without it.</summary>
        public const string Hardstanding = "Terrain Surfaces Pack Rock gaga TTP - Small Pebbles grey M Surface";
        public static readonly string[] Sign = { "ParkInfosign03", "ParkInfosign01" };
        public static readonly string[] Shelter = { "CampgroundShelter02", "CampgroundShelter01" };
        public static readonly string[] Trees = { "OakTree01", "BirchTree01", "EU_AlderTree01", "NA_HickoryTree01", "PineTree01", "SpruceTree01" };
        public static readonly string[] ForestTrees = { "PineTree01", "SpruceTree01", "BirchTree01", "EuropeanSpruce01" };
    }
}
