// Farmstead art defaults (rf-farms, 2026-10-16), checked in the game runner's clean profile. Vanilla has
// farm props only as parts of the agriculture buildings; the automatic chains reached the wrong things: raised-bed
// corner pieces as "crops", a 16 m irrigation rig parked in the yard as "machinery", gazebos as field shelters and
// no silo at all. Each default is an explicit vanilla id; installed farm assets (or the player's pick) still win.

using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Yards;

namespace Placemaker.Core.Layers.Types.Farmstead
{
    internal static class FarmArt
    {
        /// <summary>Tractor trailers with their loads, parked nose-in along the yard front.</summary>
        public static readonly string[] Trailers =
        {
            "TractorTrailer01_CropPile02", "TractorTrailer03_CropPile02", "TractorTrailer01_CropPile03", "TractorTrailer03_CropPile01",
        };

        /// <summary>Feed bins: the tall vanilla water tanks, in a row at the yard end.</summary>
        public static readonly AssetRef SiloStandIn = AssetMix.One("WaterTank03", "WaterTank02");

        /// <summary>The irrigation rig that stands in the big arable fields (arable farms).</summary>
        public const string Irrigation = "AgricultureSprinklerSystem";

        /// <summary>The farm tractor: Placemaker's prop of the game's own farm tractor (VehicleProps), else a loaded trailer.</summary>
        public static readonly AssetRef Tractor = new AssetRef(VehicleProps.FarmTractor(1), VehicleProps.FarmTractor(2), "TractorTrailer01_CropPile02");

        /// <summary>The farm pickup / runabout: the game's taller cars (SUVs, pickups) as props; vanilla has no quad bike.</summary>
        public static readonly AssetRef Pickup = new AssetRef(VehicleProps.Car(3), VehicleProps.Car(9), VehicleProps.Car(8), VehicleProps.Car(1));

        /// <summary>The farm's water tanks (vanilla WaterTank01-03).</summary>
        public static AssetRef Tank(int k) => k % 3 == 0 ? AssetMix.One("WaterTank02", "WaterTank03", "WaterTank01") : k % 3 == 1 ? AssetMix.One("WaterTank03", "WaterTank02") : AssetMix.One("WaterTank01", "WaterTank02");

        /// <summary>Firewood: the open log shelter, else a pile.</summary>
        public static readonly AssetRef Firewood = new AssetRef("FirewoodShelter01", "FirewoodShelter02", "FirewoodPile01", "FirewoodPile02");
    }
}
