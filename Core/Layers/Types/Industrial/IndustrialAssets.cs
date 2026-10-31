// Industrial category (2026-10-19): the props the handcrafted yards are built from, each with its real footprint so rows,
// stacks and bays can be laid out to the centimetre instead of scattered.
//
// Every item names a vanilla prefab (the clean game the runner uses has nothing else) and, where something better exists,
// prefers it: Placemaker's own vehicle props (Game/Assets/VehiclePropSystem: the game's cars, vans, trucks, tractors and
// campers made into static props, VehicleProps below), then a vanilla stand-in. Never an add-on pack's asset (the user,
// 2026-10-19: "we're not going to use any add-on packs"): the user's game builds what the clean game runner showed. The
// build takes the first id that exists (AssetCatalogSystem.Resolve: id, fallbacks, category default).
//
// Sizes are the catalog's bounds (catalog-dump.json, x = local width, z = local depth along the prop's facing). Local +Z is
// where the prop faces: yaw = atan2(dir.x, dir.y) turns it to world direction dir (LeisureKit.Yaw).

using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    /// <summary>One prop kind: the asset (with fallbacks), its footprint and its category (the last-resort pool).</summary>
    public readonly struct YardItem
    {
        public readonly AssetRef[] Options;
        /// <summary>Footprint: X across the prop, Z along its facing (metres).</summary>
        public readonly float X, Z;
        public readonly string Category;
        /// <summary>Where the footprint's centre lies from the prop's pivot along its local Z (most props: 0).</summary>
        public readonly float CZ;

        public YardItem(string category, float x, float z, params string[] options) : this(category, x, z, 0f, options) { }

        public YardItem(string category, float x, float z, float cz, params string[] options)
        {
            Category = category; X = x; Z = z; CZ = cz;
            Options = new AssetRef[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                var parts = options[i].Split('|');
                var fb = new string[parts.Length - 1];
                System.Array.Copy(parts, 1, fb, 0, fb.Length);
                Options[i] = new AssetRef(parts[0], fb);
            }
        }

        /// <summary>One option by a stable pick (rows use one pick per row so a row is one kind).</summary>
        public AssetRef Pick(uint v) => Options.Length == 0 ? new AssetRef("") : Options[(int)(v % (uint)Options.Length)];
        public float Radius => 0.5f * math.sqrt(X * X + Z * Z);
    }

    /// <summary>
    /// Placemaker's vehicle props (made by the game layer from the game's own vehicles, VehiclePropSystem). Core names the
    /// slots; the game fills each slot with a vehicle of that kind found in the playset (sorted by name, so a slot keeps its
    /// vehicle from one load to the next). A slot the game could not fill simply falls back to the next id.
    /// </summary>
    public static class VehicleProps
    {
        public const string Prefix = "Placemaker Vehicle ";
        public static string Car(int i) => Prefix + "Car " + i;
        public static string Van(int i) => Prefix + "Van " + i;
        public static string Truck(int i) => Prefix + "Truck " + i;
        public static string Garbage(int i) => Prefix + "Garbage " + i;
        public static string Tractor(int i) => Prefix + "Tractor " + i;
        /// <summary>A farm tractor (the game's field tractors, never the mining tractor).</summary>
        public static string FarmTractor(int i) => Prefix + "FarmTractor " + i;
        public static string Camper(int i) => Prefix + "Camper " + i;
        public static string Loader(int i) => Prefix + "Loader " + i;

        /// <summary>Slot kinds and how many of each the game makes (the game layer reads this list).</summary>
        public static readonly (string kind, int count)[] Slots =
        {
            ("Car", 10), ("Van", 3), ("Truck", 3), ("Garbage", 2), ("Tractor", 2), ("FarmTractor", 2), ("Camper", 2), ("Loader", 2),
        };
    }

    /// <summary>
    /// (P2-3) Houses as static props: the meshes of the game's own low-density houses (Game/Assets/VehiclePropSystem makes
    /// them with the vehicle props). Vanilla has no house prop at all; a farm without its farmhouse read as a barn yard.
    /// Each slot takes the first of its sources the game has (EU first: the rural look), so a slot keeps its house.
    /// </summary>
    public static class HouseProps
    {
        public const string Prefix = "Placemaker House ";
        public static string House(int i) => Prefix + i;

        /// <summary>Slot → the vanilla buildings it may copy, in order (lot cells in the name: 3x3 = 24 × 24 m).</summary>
        public static readonly string[][] Sources =
        {
            // level 3-5 houses on 3 × 3 lots: the level-1 ones are cabins (a green tiny home with a porch read as a mobile home,
            // run 20261031-101259-farm-close)
            new[] { "EU_ResidentialLow03_L3_3x3", "NA_ResidentialLow01_L2_3x3", "EU_ResidentialLow03_L1_3x3" },
            new[] { "EU_ResidentialLow03_L5_3x3", "EU_ResidentialLow03_L3_3x3", "EU_ResidentialLow01_L1_3x3" },
            new[] { "NA_ResidentialLow01_L2_3x3", "EU_ResidentialLow03_L3_3x3", "NA_ResidentialLow01_L1_3x3" },
            new[] { "EU_ResidentialLow01_L3_3x2", "EU_ResidentialLow03_L3_3x2", "EU_ResidentialLow03_L3_3x3" },
        };
    }

    public static class IndustrialAssets
    {
        // ───────────── containers ─────────────

        /// <summary>A 20 ft box (6.06 × 2.44 m), long side along local Z.</summary>
        public static readonly YardItem Box20 = new YardItem(AssetCategories.ScrapContainer, 2.45f, 6.06f,
            "ShippingContainer01", "ShippingContainer02", "ShippingContainer03");
        /// <summary>A 40 ft box (12.2 × 2.44 m): the train-car container, else the trailer box. NOT USED: both render empty
        /// without their vehicle (seen in the game 2026-10-19).</summary>
        public static readonly YardItem Box40 = new YardItem(AssetCategories.ScrapContainer, 2.6f, 12.6f,
            "TrainCargoCar01_Container01|TruckTrailer02_Container01|ShippingContainer01");
        /// <summary>Open-top roll-off container (skip): long side along local X.</summary>
        public static readonly YardItem OpenSkip = new YardItem(AssetCategories.LandfillSkip, 6.3f, 2.3f,
            "LargeOpenContainer01|TrashContainerEmpty04");
        public static readonly YardItem BigSkip = new YardItem(AssetCategories.LandfillSkip, 5.7f, 2.4f,
            "TrashContainerEmpty04|LargeOpenContainer01");
        public static readonly YardItem Dumpster = new YardItem(AssetCategories.LandfillSkip, 2.8f, 2.1f,
            "TrashContainerEmpty03|TrashContainerEmpty02");

        /// <summary>Stacked container blocks (vanilla harbour stacks): rows of 40 ft boxes side by side along local X.</summary>
        public static class Stacks
        {
            public const float SmallX = 13.9f, MediumX = 34.9f, LargeX = 70f;
            public const float LowZ = 13.2f, TallZ = 15.9f;
            /// <summary>Stack id by length class (0 small, 1 medium, 2 large) and tier (0 two high, 1 three/four high, 2 six high).</summary>
            public static string Id(int size, int tier)
            {
                string s = size == 2 ? "Large" : size == 1 ? "Medium" : "Small";
                string h = tier >= 2 ? "High" : tier == 1 ? "Medium" : "Low";
                return "ShippingContainerPile" + s + h + "01";
            }
            public static float X(int size) => size == 2 ? LargeX : size == 1 ? MediumX : SmallX;
            /// <summary>Depth of a stack: the two-high small and medium stacks are shallower.</summary>
            public static float Z(int size, int tier) => tier == 0 && size < 2 ? LowZ : TallZ;
            /// <summary>
            /// The two-high small and medium stacks are not centred on their pivot (catalog bounds: z −5.29…7.93 and −7.93…5.29),
            /// the footprint centre lies this far along local Z.
            /// </summary>
            public static float PivotZ(int size, int tier) => tier == 0 && size == 0 ? 1.32f : tier == 0 && size == 1 ? -1.32f : 0f;
            public static AssetRef Ref(int size, int tier)
            {
                // A missing tier falls back to the other heights of the same length (never to a longer stack).
                var fb = new System.Collections.Generic.List<string>();
                for (int t = 0; t < 3; t++) if (t != tier) fb.Add(Id(size, t));
                return new AssetRef(Id(size, tier), fb.ToArray());
            }
        }

        /// <summary>Painted container slots (the harbour's grid decals): 15.9 × 13.9 and 15.9 × 7 m.</summary>
        public static readonly YardItem SlotGrid = new YardItem(AssetCategories.DecalMarkings, 15.9f, 13.9f, "ContainerGridDecal02|ContainerGridDecal01");

        // ───────────── vehicles (wrecks, staff cars, machines) ─────────────

        /// <summary>Cars (wrecks in the yard, staff cars at the office): Placemaker's car props, then pack props, then a trailer.</summary>
        // Footprint of the largest car the game has (Car10 2.2 × 5.2 m, VehiclePropSystem log): rows never let two touch.
        public static readonly YardItem Car = new YardItem(AssetCategories.ScrapWreck, 2.25f, 5.2f,
            VehicleProps.Car(1) + "|CarTrailer01_Props02",
            VehicleProps.Car(2) + "|CarTrailer01_Props01",
            VehicleProps.Car(3) + "|CarTrailer01_Props03",
            VehicleProps.Car(4) + "|CarTrailer01_Props02",
            VehicleProps.Car(5) + "|CarTrailer01_Props01",
            VehicleProps.Car(6) + "|CarTrailer01_Props03",
            VehicleProps.Car(7) + "|CarTrailer01_Props02",
            VehicleProps.Car(8) + "|CarTrailer01_Props01",
            VehicleProps.Car(9) + "|CarTrailer01_Props03",
            VehicleProps.Car(10) + "|CarTrailer01_Props02");
        public static readonly YardItem Van = new YardItem(AssetCategories.ScrapWreck, 2.1f, 5.4f,
            VehicleProps.Van(1) + "|" + VehicleProps.Car(1) + "|CarTrailer01_Props02",
            VehicleProps.Van(2) + "|" + VehicleProps.Car(2) + "|CarTrailer01_Props01",
            VehicleProps.Van(3) + "|" + VehicleProps.Car(3) + "|CarTrailer01_Props03");
        public static readonly YardItem Truck = new YardItem(AssetCategories.LogisticsTruck, 2.8f, 8.4f,
            VehicleProps.Truck(1) + "|CoalTruck01_OrePile01",
            VehicleProps.Truck(2) + "|CoalTruck01_CoalPile01",
            VehicleProps.Truck(3) + "|CoalTruck01_StonePile01");
        public static readonly YardItem GarbageTruck = new YardItem(AssetCategories.LandfillSkip, 2.9f, 8.6f,
            VehicleProps.Garbage(1) + "|TrashContainerEmpty03",
            VehicleProps.Garbage(2) + "|TrashContainerEmpty03");
        public static readonly YardItem Tractor = new YardItem(AssetCategories.ScrapMachine, 3.2f, 4.8f,
            VehicleProps.Tractor(1) + "|CableReel02",
            VehicleProps.Tractor(2) + "|CableReel02");
        /// <summary>Wheel loader / material handler working a heap.</summary>
        public static readonly YardItem Loader = new YardItem(AssetCategories.ScrapMachine, 5.0f, 9.1f,
            VehicleProps.Loader(1) + "|BackupGenerator01",
            VehicleProps.Loader(2) + "|BackupGenerator01");
        /// <summary>Mobile home / caravan (an office or a night watchman's home).</summary>
        public static readonly YardItem Camper = new YardItem(AssetCategories.ScrapCabin, 2.6f, 7.4f,
            VehicleProps.Camper(1) + "|ShippingContainer01",
            VehicleProps.Camper(2) + "|ShippingContainer02");

        // ───────────── offices, gate, lights ─────────────

        /// <summary>A container converted into a site office (20 ft box).</summary>
        public static readonly YardItem ContainerOffice = new YardItem(AssetCategories.ScrapCabin, 2.45f, 6.06f, "ShippingContainer01");
        /// <summary>Portacabin-sized hut (the gate booth): long side along local X.</summary>
        public static readonly YardItem Booth = new YardItem(AssetCategories.ScrapCabin, 5.7f, 2.7f, "ParkingLotBooth01|ToolShed03|Kiosk01");
        public static readonly YardItem BigBooth = new YardItem(AssetCategories.ScrapCabin, 6.9f, 6.5f, "ParkingLotBooth02|ParkingLotBooth01|ToolShed03");
        public static readonly YardItem Shed = new YardItem(AssetCategories.GardenShed, 4.4f, 2.6f, "ToolShed03|ToolShed02");
        public static readonly YardItem Toilet = new YardItem(AssetCategories.ConstructionToilet, 1.3f, 1.3f, "PortableToilet01");
        public static readonly YardItem Generator = new YardItem(AssetCategories.ConstructionGenerator, 4.5f, 2.5f, "BackupGenerator01");
        public static readonly YardItem Boom = new YardItem(AssetCategories.ScrapBarrier, 0.7f, 0.6f, "ParkingLotGate01|ParkingLotGate02");
        public static readonly YardItem Flood = new YardItem(AssetCategories.FloodLight, 2.7f, 0.6f, "LightpoleIndustrial01|LightpoleIndustrial02|LightpoleCommercial01");
        public static readonly YardItem Flag = new YardItem(AssetCategories.MemorialFlag, 0.4f, 0.4f, "FlagPole01|FlagPole02");
        public static readonly YardItem Bin = new YardItem(AssetCategories.Bin, 0.7f, 0.7f, "Trashbin01|Trashbin02");
        public static readonly YardItem Stall = new YardItem(AssetCategories.ParkingDecal, 3.2f, 6.2f, "ParkingLotDecal01");
        public static readonly YardItem Block = new YardItem(AssetCategories.ScrapBarrier, 1.2f, 0.6f, "ConcreteBarrier01", "ConcreteBarrier02", "ConcreteBarrier03");
        public static readonly YardItem Carport = new YardItem(AssetCategories.LogisticsShed, 5.9f, 5.9f, "Carport02|Carport03|Carport01");

        // ───────────── heaps and goods ─────────────

        /// <summary>Rusty metal: ore-coloured truck loads (the scrap look from above).</summary>
        public static readonly YardItem MetalHeap = new YardItem(AssetCategories.ScrapHeap, 7.6f, 9.0f,
            "OreMiningTruck01_OrePile03|OreMiningTruck01_OrePile02", "OreMiningTruck01_OrePile02|OreMiningTruck01_OrePile03");
        /// <summary>
        /// The big tipped heaps (8.9 × 14.7 m): the vanilla Pile*Small01 props are EMPTY on their own (their mesh needs the
        /// building's resource state; seen in the game 2026-10-19), so only the asset packs' filled copies are named.
        /// </summary>
        public static readonly YardItem MetalMountain = new YardItem(AssetCategories.ScrapHeap, 8.9f, 14.7f, "OreMiningTruck01_OrePile03");
        public static readonly YardItem DarkHeap = new YardItem(AssetCategories.ScrapHeap, 8.9f, 14.7f, "OreMiningTruck01_OrePile01");
        public static readonly YardItem StoneHeap = new YardItem(AssetCategories.ConstructionPile, 8.9f, 14.7f, "RubblePileConcrete02");
        public static readonly YardItem SandHeap = new YardItem(AssetCategories.ConstructionPile, 8.9f, 14.7f, "RubblePileConcrete03");
        public static readonly YardItem GarbageHeap = new YardItem(AssetCategories.LandfillGarbage, 8.9f, 14.7f, "Trashpile01");
        public static readonly YardItem Rubble = new YardItem(AssetCategories.ConstructionRubble, 9.5f, 8.6f,
            "RubblePileConcrete01", "RubblePileConcrete02", "RubblePileConcrete03", "RubblePileConcrete04");
        public static readonly YardItem MixedRubble = new YardItem(AssetCategories.ConstructionRubble, 7.6f, 6.0f,
            "RubblePileMixed01", "RubblePileMixed02", "RubblePileMixed03", "RubblePileMixed04");
        public static readonly YardItem WoodRubble = new YardItem(AssetCategories.ConstructionRubble, 8.3f, 7.8f,
            "RubblePileWood01", "RubblePileWood02", "RubblePileWood03", "RubblePileWood04");
        public static readonly YardItem TruckLoad = new YardItem(AssetCategories.ScrapHeap, 2.5f, 5.8f,
            "CoalTruck01_OrePile01", "CoalTruck01_CoalPile01", "CoalTruck01_StonePile01");
        /// <summary>Tyres: no vanilla tyre prop exists; dark rubber-black truck loads stand in, laid in rows.</summary>
        public static readonly YardItem Tyres = new YardItem(AssetCategories.ScrapTire, 2.5f, 5.8f,
            "CoalTruck01_CoalPile01", "CoalTruck01_CoalPile01");
        public static readonly YardItem Trash =new YardItem(AssetCategories.LandfillGarbage, 2.0f, 2.0f,
            "Trashpile01", "Trashpile02", "Trashpile03", "Trashpile06", "Trashpile04", "Trashpile08");
        public static readonly YardItem Pallets = new YardItem(AssetCategories.ScrapPallet, 1.25f, 1.3f, "Pallet02", "Pallet01", "Pallet03", "Pallet04");
        public static readonly YardItem Crates = new YardItem(AssetCategories.ScrapPallet, 2.4f, 1.3f, "CratePile02", "CratePile01", "CratePile03");
        public static readonly YardItem Barrels = new YardItem(AssetCategories.ScrapBarrel, 3.4f, 2.0f, "BarrelPile03", "BarrelPile01", "BarrelPile02");
        public static readonly YardItem Drums = new YardItem(AssetCategories.ScrapBarrel, 0.7f, 0.7f, "Barrel01", "Barrel03", "Barrel02");
        public static readonly YardItem Gas = new YardItem(AssetCategories.ScrapBarrel, 1.0f, 1.0f, "GasBottlePallet01|GasBottleRack01");
        public static readonly YardItem Reels = new YardItem(AssetCategories.ConstructionMaterial, 2.5f, 2.5f, "CableReel02|CableReel01");
        public static readonly YardItem Pipes = new YardItem(AssetCategories.LogisticsBulk, 3.0f, 8.0f, "OilPipe01|FirewoodPile03");
        public static readonly YardItem Timber = new YardItem(AssetCategories.LogisticsLumber, 3.5f, 4.7f,
            "ForestForwarderTrailer01_LogPile01", "ForestForwarderTrailer01_LogPile02", "ForestForwarderTrailer01_LogPile03");
        public static readonly YardItem Firewood = new YardItem(AssetCategories.LogisticsLumber, 3.1f, 1.3f, "FirewoodPile03", "FirewoodPile01", "FirewoodPile02");
        public static readonly YardItem Boxes = new YardItem(AssetCategories.ScrapPallet, 2.1f, 1.2f, "CardboardBoxPile03", "CardboardBoxPile01", "CardboardBoxPile02");
        public static readonly YardItem Bales = new YardItem(AssetCategories.FarmHay, 1.6f, 1.6f, "HeyBale02", "HeyBale03");

        // ───────────── plant ─────────────

        public static readonly YardItem TankSmall = new YardItem(AssetCategories.LogisticsBarrel, 10f, 11f, "OilTankSmall01", "OilTankSmall02", "OilTankSmall03");
        public static readonly YardItem TankMedium = new YardItem(AssetCategories.LogisticsBarrel, 14.2f, 15f, "OilTankMedium01", "OilTankMedium02", "OilTankMedium03");
        public static readonly YardItem StackSmall = new YardItem(AssetCategories.FarmMachinery, 3.7f, 3.7f, "IndustrialChimneySmall01 Agriculture");
        public static readonly YardItem StackMedium = new YardItem(AssetCategories.FarmMachinery, 3.6f, 3.6f, "IndustrialChimneyMedium01 Agriculture|IndustrialChimneySmall01 Agriculture");
        public static readonly YardItem StackLarge = new YardItem(AssetCategories.FarmMachinery, 3.0f, 3.0f, "IndustrialChimneyLarge01 Agriculture|IndustrialChimneyMedium01 Agriculture");
        /// <summary>Processing plant blocks (the industrial buildings' machinery), long side along local Z.</summary>
        public static readonly YardItem PlantLong = new YardItem(AssetCategories.FarmMachinery, 8.6f, 22.4f,
            "IndustrialManufacturingDecoration04_1x3 Ore|IndustrialManufacturingDecoration03_1x3 Oil");
        public static readonly YardItem PlantSquare = new YardItem(AssetCategories.FarmMachinery, 14.8f, 16f,
            "IndustrialManufacturingDecoration04_2x2 Ore|IndustrialManufacturingDecoration02_2x2 Forestry");
        public static readonly YardItem PlantSmall = new YardItem(AssetCategories.FarmMachinery, 6.8f, 6.0f,
            "IndustrialManufacturingDecoration03_1x1 Oil|IndustrialManufacturingDecoration04_1x1 Ore");
        public static readonly YardItem Conveyor = new YardItem(AssetCategories.QuarryPlant, 2.5f, 8.0f, "OreConveyorBelt01");
    }
}
