// Default vanilla assets the parking layer asks for. Ported from ParkingLotTool (KruemelGames,
// GPL-3.0): Tools/ParkingLotFahrregeln.cs (drive path catalogue), Tools/ParkingLotBayObjects.cs
// (decals, charger), Tools/ParkingLotEntranceArrows.cs, Geometry/ParkingLanterns.cs,
// Tools/ParkingLotVegetationUI.cs. Every one is only a default inside an AssetRef — the game
// catalog falls back to the role default when an id is missing (ARCHITECTURE rule 5).

using System;

namespace Placemaker.Core.Layers.Parking
{
    internal static class ParkingAssets
    {
        // Drive paths by drivable core width (PLT Fahrregeln.cs). PLT built runtime clones
        // ("PLT " + name, 25 km/h, cheaper CarPathfind) — that is the game layer's business.
        private static readonly (string Name, double Core)[] TwoWayPaths =
        {
            ("Invisible Car Path - 1xTwoway", 3.0),
            ("Invisible Road Path - 1xTwoway", 4.0),
            ("Invisible Car Path - 2xTwoway", 6.0),
            ("Invisible Road Path - 2xTwoway", 7.0),
        };
        public const string OneWayPath = "Invisible Road Path - 1xOneway";
        public const string Alley = "Alley";
        // PLT GasseEin/GasseAus were built from the vanilla one-way alley (2026-09-18).
        public const string AlleyOneWay = "Alley Oneway";
        /// <summary>
        /// Sentinel id: the game layer resolves the vanilla bus stop like PLT ParkingLotBusStops.ResolveBusStopPrefab
        /// (built-in, bus TransportStopData with passengers, RouteConnection Road, not "Integrated", prefer a "Sign").
        /// Unresolved it falls back to an invisible marker object (ObjectRole.Marker).
        /// </summary>
        public const string BusStop = "Placemaker:BusStop";
        public const string PedestrianPath = "Invisible Pedestrian Path";

        public const string Decal = "ParkingLotDecal01";
        public const string DisabledDecal = "ParkingLotDisabledDecal01";
        public const string ElectricDecal = "ParkingLotElectricDecal01";
        public const string Charger = "ParkingLotCharger01";
        /// <summary>(P2-1) A copy of the stall decal without its mesh (Game/Assets/InvisibleBayPrefabSystem): no paint, the parking lane stays. Save format.</summary>
        public const string InvisibleBay = "Placemaker Invisible Parking Bay";
        // Art pass (2026-10-13): a real asphalt texture first (EAI surface pack, already cloned as an
        // overlay in the player's setup), the vanilla pavement PLT used as the fallback.
        // s25: the lot read as pale concrete in game (EAI "Concrete Asphalt" is a light grey slab under CS2's sun). The
        // G87 vanilla-asphalt surface is the road asphalt as a surface (dark, fine grain); EAI and vanilla pavement stay
        // as fallbacks for playsets without it.
        public const string Asphalt = "G87 Vanilla Asphalt Pavement G87 VA Surface GND Surface";
        public const string AsphaltEai = "EAI - Surface Pack Concrete Asphalt 001 Surface";
        public const string AsphaltVanilla = "Pavement Surface 01";
        public const string Grass = "Grass Surface 01";
        public const string LampSingle = "StreetlightSingle02";
        public const string LampDouble = "StreetlightDouble02";
        public static readonly string[] Arrows = { "RoadArrow Forward Placeholder", "NA RoadArrow Forward", "EU RoadArrow Forward" };
        public static readonly string[] Trees = { "EU_AlderTree01", "BirchTree01", "NA_LondonPlaneTree01", "NA_LindenTree01", "OakTree01" };
        public static readonly string[] Bushes = { "GreenBushWild02", "FlowerBushWild02", "GreenBushWild01" };
        /// <summary>
        /// Default planting when the player picked none (art pass): one shade tree species in formal rows
        /// (London plane, the classic car park tree) and a green + a flowering shrub mixed in the medians.
        /// </summary>
        public const string DefaultTree = "NA_LondonPlaneTree01";
        public static readonly string[] DefaultBushes = { "GreenBushWild02", "FlowerBushWild02" };

        /// <summary>
        /// The widest two-way path whose core fits the requested width; 3 m when nothing fits
        /// (PLT NetBuilder: "wide" for aisles/ring, "narrow" for cross lanes; the 7 m
        /// "2xPerpendicular" variant is deliberately excluded — its parking lanes would double the stalls).
        /// </summary>
        public static string DrivePath(double width)
        {
            string best = TwoWayPaths[0].Name;
            foreach (var p in TwoWayPaths) if (p.Core <= width + 1e-6) best = p.Name;
            return best;
        }

        /// <summary>
        /// The slow copy of a drive path (Game/Assets/SlowPathPrefabSystem: 25 km/h, dearer to drive through, so through
        /// traffic keeps to the streets; PLT parity). Save format: never rename.
        /// </summary>
        public static string SlowName(string path) => "Placemaker Slow " + path;

        /// <summary>A two-way drive path for <paramref name="width"/>: the slow copy first, the vanilla path when it is missing.</summary>
        public static AssetRef Drive(double width)
        {
            var path = DrivePath(width);
            return new AssetRef(SlowName(path), path);
        }

        /// <summary>The one-way entrance path: slow first, then vanilla, then the two-way path for <paramref name="width"/>.</summary>
        public static AssetRef OneWay(double width)
        {
            var path = DrivePath(width);
            return new AssetRef(SlowName(OneWayPath), OneWayPath, SlowName(path), path);
        }

        public static AssetRef Pick(string chosen, string fallback, params string[] more)
        {
            var fb = new string[more.Length + 1];
            fb[0] = fallback;
            Array.Copy(more, 0, fb, 1, more.Length);
            return new AssetRef(string.IsNullOrEmpty(chosen) ? fallback : chosen, fb);
        }
    }
}
