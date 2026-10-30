// Art pass (a1): explicit memorial defaults. The player's catalog has no asset the memorial rules classify
// as a monument or sculpture, so the focal piece used to fall back to a fountain. Vanilla does ship
// standing stones and stone heads (in "decoration"): a standing stone reads as a memorial stone / cenotaph,
// the stone heads and the bicycle statue as sculpture. Plaques fall back to low old headstones.

using Placemaker.Core.Layers.Park;

namespace Placemaker.Core.Layers.Types.MemorialPark
{
    public static class MemorialAssets
    {
        public static readonly string[] Monuments = { "StandingStone01", "StandingStone03", "StandingStone05", "StandingStone07" };
        public static readonly string[] Sculptures = { "StoneHead01", "StoneHead02", "StoneHead03", "BicycleStatue01", "StandingStone02", "StandingStone06" };
        public static readonly string[] Plaques = { "OldCemeteryTombstone04", "OldCemeteryTombstone01", "OldCemeteryTombstone03" };
        public static readonly string[] Flags = { "FlagPole01", "FlagPole02" };
        public static readonly string[] WreathShrubs = { "FlowerBushWild02", "FlowerBushWild02" };
        /// <summary>Poppies when the player has the VFP pack, otherwise red/pink flowering shrubs.</summary>
        public static readonly string[] Poppies = { "VFP PoppyBushWild", "FlowerBushWild02", "FlowerBushWild02" };
        public static readonly string[] Avenue = ParkArt.AvenueTrees;
        public static readonly string[] LawnTrees = { "NA_LindenTree01", "OakTree01", "EU_ChestnutTree01" };
        /// <summary>(rf-parks) The rill of fountains down a reflecting pool (vanilla, ~3.5 m across).</summary>
        public static readonly string[] PoolFountains = { "SmallFountain03", "SmallFountain02", "SmallFountain01" };
        public static readonly string[] SpecimenTrees = { "OakTree01", "EU_ChestnutTree01", "NA_LondonPlaneTree01" };
    }
}
