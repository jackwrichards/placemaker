// Art direction for the wetland (art pass a3): explicit vanilla assets used whenever the matching asset
// control is on automatic. There are no vanilla reeds, bird hides or lookout towers: reeds are the small
// grassy wild bushes, hides are the timber campground shelters, and the "lookout" pool (a fire-station
// upgrade and a water-tower deck, 32-56 m buildings) is never used unless the player picks an asset.

using Placemaker.Core.Layers.Common;

namespace Placemaker.Core.Layers.Types.Wetland
{
    public static class WetlandArt
    {
        public static readonly string[] Reeds = { "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWild01", "GreenBushWildChild01" };
        public static readonly string[] Trees = { "EU_AlderTree01", "BirchTree01", "EU_PoplarTree01" };
        /// <summary>(nature makeover) Lake country: northern shores of pine, spruce and birch, an alder at the water.</summary>
        public static readonly string[] LakeTrees = { "PineTree01", "SpruceTree01", "BirchTree01", "EuropeanSpruce01", "PineTree01", "BirchTree01", "EU_AlderTree01" };
        public static readonly string[] Bushes = { "GreenBushWild02", "GreenBushWildAdult01", "GreenBushWildElderly01", "FlowerBushWild01" };
        public static readonly string[] GardenShrubs = { "FlowerBushWild02", "FlowerBushWild01", "GreenBushWild01" };
        public static readonly string[] Flowers = { "Flowerbed02", "Flowerbed01" };
        public static readonly string[] Hides = { "CampgroundShelter02", "CampgroundShelter01" };
        public static readonly string[] Lookouts = { "Gazebo03", "Gazebo02", "Gazebo01" };
        public static readonly string[] Benches = { "CampgroundBench01", "GardenBench03" };
        public static readonly string[] GardenBenches = { "GardenBench01", "GardenBench02" };
        public static readonly string[] Lamps = { "EU_LightpolePark03", "LightpolePark03" };

        public static string[] Or(string[] picked, string[] curated) => picked != null && picked.Length > 0 ? picked : curated;

        /// <summary>The player's list, else the curated one while the category has installed items (else automatic/none).</summary>
        public static string[] Or(string[] picked, string[] curated, string category) =>
            picked != null && picked.Length > 0 ? picked : AssetAvailability.Has(category) ? curated : System.Array.Empty<string>();
        public static string[] Or(string picked, string[] curated) => string.IsNullOrEmpty(picked) ? curated : new[] { picked };
    }
}
