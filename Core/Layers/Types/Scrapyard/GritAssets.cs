// Art pass (a4): explicit, verified default assets for the gritty cluster (scrapyard, landfill, quarry,
// construction site, abandoned lot, urban ruins).
//
// Why explicit: the shared automatic pools are built from name rules and pick nonsense for these
// categories in a real playset — ruins.pillar/slab pools are 90 m suspension-bridge pylons,
// industrial.garbage is railings, construction.container includes 70 m container stacks,
// construction.material a 37 m desalination pipe rack, industrial.skip a 0.6 m wheelie bin,
// industrial.cabin a children's play cabin, lamp.flood a 3 m park lamp, and industrial.scrap /
// industrial.tire / industrial.wreck have no members at all (nothing would be built). Every set below
// was checked against the player's catalog dump (ids, footprints); vanilla ids come first or as the
// fallback, modded packs only where they are clearly better (decals: there are no vanilla decals).
//
// How it applies: each set is a list of options "id|fallback|fallback". An object/surface/net whose
// asset is still automatic (the player chose nothing) gets one option by its Variation (mixed sets)
// or by the plan salt (uniform sets: one fence, one lamp model, one barrier model per plan). The
// AssetRef keeps the option's own fallbacks plus the other options' primaries, so a missing pack
// degrades to a sibling and finally to the category pool (AutoAssets: explicit id → first existing
// fallback → category). Player choices (non-empty ids) are never touched.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;

namespace Placemaker.Core.Layers.Types.Scrapyard
{
    public static class GritAssets
    {
        /// <summary>A curated set: options of "primary|fallback|…"; uniform sets pick once per plan.</summary>
        public sealed class Set
        {
            public readonly string[][] Options;
            public readonly bool Uniform;
            public Set(bool uniform, params string[] options)
            {
                Uniform = uniform;
                Options = new string[options.Length][];
                for (int i = 0; i < options.Length; i++) Options[i] = options[i].Split('|');
            }
        }

        private static Set Mixed(params string[] o) => new Set(false, o);
        private static Set Same(params string[] o) => new Set(true, o);

        // ───────────── objects (by category) ─────────────

        public static readonly Dictionary<string, Set> Objects = new Dictionary<string, Set>(StringComparer.Ordinal)
        {
            // Scrap: rusty ore-coloured loads and dark metal heaps, drums, cable drums, crates — reads as
            // sorted metal from above. Weighted towards the heaps.
            [AssetCategories.ScrapHeap] = Mixed("CoalTruck01_OrePile01", "CoalTruck01_OrePile01", "CoalTruck01_OrePile01",
                "CoalTruck01_CoalPile01", "BarrelPile02", "BarrelPile03", "CableReel02", "CratePile03"),
            // No vanilla car wrecks or tyres exist: vehicle props (utility vans, trailers, caravans, truck
            // cabs) stand in for wrecks, low dark coal loads for tyre heaps.
            [AssetCategories.ScrapWreck] = Mixed("Placemaker Vehicle Car 2|Placemaker Vehicle Car 3", "Placemaker Vehicle Car 4|Placemaker Vehicle Car 5", "Placemaker Vehicle Car 6|Placemaker Vehicle Car 2",
                "Placemaker Vehicle Car 7|Placemaker Vehicle Car 3", "Placemaker Vehicle Van 1|Placemaker Vehicle Car 4", "Placemaker Vehicle Camper 1|Placemaker Vehicle Car 5"),
            [AssetCategories.ScrapTire] = Mixed("CoalTruck01_CoalPile01"),
            [AssetCategories.ScrapContainer] = Mixed("ShippingContainer01", "ShippingContainer02", "ShippingContainer03", "ShippingContainer01", "ShippingContainer03"),
            [AssetCategories.ScrapPallet] = Mixed("CratePile01", "CratePile02", "CratePile03", "Pallet02", "Pallet01", "Crate02", "Pallet04"),
            [AssetCategories.ScrapBarrel] = Mixed("BarrelPile01", "Barrel01", "Barrel03", "Barrel02", "BarrelPile03", "GasBottleRack01|GasBottlePallet01", "Barrel01"),
            [AssetCategories.ScrapMachine] = Mixed("Placemaker Vehicle Loader 1|BackupGenerator01", "Placemaker Vehicle Loader 1|BackupGenerator01", "Placemaker Vehicle Truck 1|BackupGenerator01"),
            [AssetCategories.ScrapCabin] = Same("ParkingLotBooth01|ToolShed03|ShippingContainer01"),
            [AssetCategories.ScrapBarrier] = Mixed("ConcreteBarrier01", "ConcreteBarrier02", "ConcreteBarrier03"),

            // Landfill.
            [AssetCategories.LandfillGarbage] = Mixed("Trashpile01", "Trashpile02", "Trashpile03", "Trashpile06", "Trashpile04", "Trashpile08",
                "CoalTruck01_GarbagePile01", "Trashbag01", "Trashbag03"),
            [AssetCategories.LandfillSkip] = Mixed("LargeOpenContainer01|TrashContainerEmpty04", "TrashContainerEmpty04|LargeOpenContainer01", "TrashContainerEmpty03|TrashContainerEmpty04"),
            [AssetCategories.LandfillPile] = Mixed("CoalTruck01_StonePile01", "CoalTruck01_OrePile01", "CoalTruck01_StonePile01", "CoalTruck01_CoalPile01"),
            [AssetCategories.LandfillBale] = Mixed("CardboardBoxPile01", "CardboardBoxPile02", "CardboardBoxPile03", "CratePile02"),

            // Construction.
            [AssetCategories.ConstructionCabin] = Mixed("ShippingContainer01", "ShippingContainer02", "ToolShed03|ShippingContainer03"),
            [AssetCategories.ConstructionToilet] = Same("PortableToilet01"),
            [AssetCategories.ConstructionContainer] = Mixed("ShippingContainer01", "ShippingContainer02", "ShippingContainer03"),
            // (rundown, 2026-10-30) the old Prop_ vehicle names never placed (lab-rundown-machines): Placemaker's own vehicle props (VehicleProps,
            // made from the game's vehicles; AutoAssets.IsOwn lets them through): the wheel loader, the dump truck, the mining tractor
            [AssetCategories.ConstructionMachine] = Mixed("Placemaker Vehicle Loader 1|BackupGenerator01", "Placemaker Vehicle Truck 1|BackupGenerator01", "Placemaker Vehicle Loader 1|BackupGenerator01", "Placemaker Vehicle Tractor 1|Placemaker Vehicle Truck 1|BackupGenerator01"),
            [AssetCategories.ConstructionCrane] = Same("ConstructionCrane01"),
            [AssetCategories.ConstructionBarrier] = Same("SafetyBarrier01|SafetyBarrier03|ConcreteBarrier01", "SafetyBarrier03|SafetyBarrier01|ConcreteBarrier01", "ConcreteBarrier01|ConcreteBarrier02"),
            [AssetCategories.ConstructionCone] = Same("SafetyCone01|SafetyCone02", "SafetyCone03|SafetyCone01"),
            [AssetCategories.ConstructionMaterial] = Mixed("Pallet01", "Pallet02", "Pallet03", "CableReel01", "CableReel02", "GasBottlePallet01", "CratePile01", "Pallet04", "OilPipe01|Pallet02"),
            [AssetCategories.ConstructionPile] = Mixed("CoalTruck01_StonePile01", "CoalTruck01_StonePile01", "CoalTruck01_OrePile01"),
            [AssetCategories.ConstructionRubble] = Mixed("RubblePileConcrete01", "RubblePileConcrete02", "RubblePileConcrete03", "RubblePileConcrete04", "RubblePileMixed01", "RubblePileMixed02", "RubblePileMixed03", "RubblePileWood01"),
            [AssetCategories.ConstructionSkip] = Mixed("LargeOpenContainer01|TrashContainerEmpty04", "TrashContainerEmpty04|LargeOpenContainer01"),
            // No scaffold props exist: tall chain-link panels read as a site hoarding round the work.
            [AssetCategories.ConstructionScaffold] = Same("FenceIndustrialPieceHighShort01|FenceIndustrialPieceHighShort02"),
            [AssetCategories.ConstructionSign] = Same("SafetyBarrier02|SafetyBarrier01"),
            [AssetCategories.ConstructionGenerator] = Same("BackupGenerator01"),
            [AssetCategories.QuarryPlant] = Same("OreConveyorBelt01"),

            // Ruins.
            [AssetCategories.RuinsRubble] = Mixed("RubblePileConcrete01", "RubblePileConcrete02", "RubblePileConcrete03", "RubblePileConcrete04",
                "RubblePileMixed01", "RubblePileMixed02", "RubblePileMixed03", "RubblePileMixed04", "RubblePileWood02", "RubblePileWood04"),
            [AssetCategories.RuinsJunk] = Mixed("Barrel01", "Barrel03", "BarrelPile01", "Pallet01", "Pallet03", "CardboardBoxPile01", "CardboardBoxPile03",
                "Trashpile05", "Trashpile07", "Trashbag02", "Crate01", "OutdoorSofa02|OutdoorSofa01"),
            // Broken concrete columns with rebar: highway pillars and stumps (never bridge pylons).
            [AssetCategories.RuinsPillar] = Mixed("RubblePylonLarge01", "RubblePylonLarge02", "RubblePylonLarge03", "RubblePylonLarge04"),
            // Concrete blocks for ledges, parkour and fallen deck pieces.
            [AssetCategories.RuinsSlab] = Mixed("ConcreteBarrier01", "ConcreteBarrier02", "ConcreteBarrier03"),
            // Free-standing walls: cinder-block and brick panels tall enough to paint on.
            [AssetCategories.RuinsWall] = Mixed("dJP-ResProps-Wall-CBlock|Brickworks-Fence-Tall-2m|EU_Wall01Long", "Brickworks-Fence-Tall-4m|dJP-ResProps-Wall-CBlock|EU_Wall02Long",
                "dJP-ResProps-Wall-CBlock-Breeze|dJP-ResProps-Wall-CBlock|EU_Wall01Long", "EU_Wall03Long|EU_Wall01Long"),
            [AssetCategories.RuinsShelter] = Mixed("MakeshiftTent01", "MakeshiftTent02", "MakeshiftTent03", "MakeshiftTent04"),

            [AssetCategories.FloodLight] = Same("LightpoleIndustrial01|LightpoleIndustrial02|LightpoleCommercial01", "LightpoleIndustrial02|LightpoleIndustrial01|LightpoleCommercial01"),

            // Decals (all packs are mods; nothing vanilla exists, the category pool stays the last resort).
            [AssetCategories.DecalDirt] = Mixed(
                "G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 1 Decal", "G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal",
                "G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 3 Decal", "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 01 A S Decal",
                "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 04 B S Decal", "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 07 C S Decal",
                "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 15 F S Decal", "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 42 Track Short Decal",
                "G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 43 Track Long Decal", "Ground Texture Decal Pack Ground Dirt01 Decal",
                "Ground Texture Decal Pack Ground Dirt03 Decal", "G87 Trash Decals Dirty Pack Trash G87 Dust 02 Brown Decal"),
            [AssetCategories.DecalLitter] = Mixed(
                "G87 Trash Decals Dirty Pack Trash G87 Debris 01 Decal", "G87 Trash Decals Dirty Pack Trash G87 Debris 02 Decal",
                "G87 Trash Decals Dirty Pack Trash G87 Debris 03 Decal", "G87 Trash Decals Dirty Pack Trash G87 Debris 04 Decal",
                "G87 Trash Decals Dirty Pack Trash G87 Litter 01 Decal", "G87 Trash Decals Dirty Pack Trash G87 Litter 02 Decal",
                "G87 Trash Decals Dirty Pack Trash G87 Litter 03 Decal", "G87 Trash Decals Dirty Pack Trash G87 Trash Misc 01 Decal",
                "G87 Trash Decals Dirty Pack Trash G87 Trash Misc 02 Decal", "G87 Trash Decals Dirty Pack Trash G87 Trash Misc 03 Decal",
                "G87 Trash Decals Dirty Pack Trash G87 Trash Cardboard 01 Decal", "G87 Trash Decals Dirty Pack Trash G87 Debris 15 Glass Spreaded Decal"),
            [AssetCategories.DecalPuddles] = Mixed(
                "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 11 Band A Decal", "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 12 Band A Wet Decal",
                "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 13 Band B Decal", "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 19 Wet Track Short Decal",
                "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 21 Trio Wet Decal", "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 22 Lone Medium Wet Decal",
                "G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 23 Wide Wet Decal", "G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 84 B M Decal"),
            [AssetCategories.DecalCracks] = Mixed(
                "Cracks and Damage Decal Pack RoadAssets Crack - Small 1 Decal", "Cracks and Damage Decal Pack RoadAssets Crack - Small 2 Decal",
                "Cracks and Damage Decal Pack RoadAssets Crack - Small 3 Decal", "Cracks and Damage Decal Pack RoadAssets Crack - Small 5 Decal",
                "Cracks and Damage Decal Pack RoadAssets Crack - Medium 1 Decal", "Cracks and Damage Decal Pack RoadAssets Crack - Medium 2 Decal",
                "Cracks and Damage Decal Pack RoadAssets Crack - Medium 3 Decal", "Cracks and Damage Decal Pack RoadAssets Damage - Large 2 Decal",
                "Cracks and Damage Decal Pack RoadAssets Damage - Large 3 Decal", "G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 46 Medium Decal",
                "G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 41 Long Medium Decal", "Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 2 Decal"),
        };

        // ───────────── surfaces (by category) and fences ─────────────

        /// <summary>Default ground per surface category for these types (one per plan).</summary>
        public static readonly Dictionary<string, Set> Surfaces = new Dictionary<string, Set>(StringComparer.Ordinal)
        {
            [AssetCategories.SurfaceDirt] = Same("EAI - Surface Pack Ground Mud Surface|Terrain Surfaces Pack Ground gaga TTP - Soil M Surface|Oil Surface 01"),
            [AssetCategories.SurfaceNatural] = Same("Terrain Surfaces Pack Rock gaga TTP - Smaller Pebbles grey M Surface|Ore Surface 01|Sand Surface 01"),
            [AssetCategories.SurfaceAsphalt] = Same("EAI - Surface Pack Concrete Weathered Asphalt Surface|EAI - Surface Pack Concrete Cracked Asphalt Surface|Concrete Surface 02"),
            [AssetCategories.SurfacePaving] = Same("Concrete Surface 02|Concrete Surface 01|Pavement Surface 02"),
        };

        /// <summary>Chain-link industrial fence (net), residential fences only as the last resort.</summary>
        public static readonly Set IndustrialFence = Same("FenceIndustrialLow02|FenceIndustrialLow01|NA_FenceResidentialHigh01|EU_FenceResidentialHigh01");

        // ───────────── API ─────────────

        /// <summary>One option of a set as an AssetRef (option fallbacks, then the other options' primaries).</summary>
        public static AssetRef Ref(Set set, uint pick)
        {
            if (set == null || set.Options.Length == 0) return new AssetRef("");
            int n = set.Options.Length;
            int k = (int)(Hashing.Mix(pick, 0x6A17u) % (uint)n);
            var opt = set.Options[k];
            var fb = new List<string>(opt.Length - 1 + n - 1);
            for (int i = 1; i < opt.Length; i++) fb.Add(opt[i]);
            for (int j = 1; j < n; j++)
            {
                var other = set.Options[(k + j) % n][0];
                if (other != opt[0] && !fb.Contains(other)) fb.Add(other);
            }
            return new AssetRef(opt[0], fb.ToArray());
        }

        /// <summary>Curated default for a category (mixed by variation, uniform by plan salt); automatic when unknown.</summary>
        public static AssetRef For(string category, uint variation, uint planSalt, IReadOnlyDictionary<string, Set> overrides = null)
        {
            Set set = null;
            if (category != null && (overrides == null || !overrides.TryGetValue(category, out set))) Objects.TryGetValue(category, out set);
            if (set == null) return new AssetRef("");
            return Ref(set, set.Uniform ? planSalt : variation);
        }

        /// <summary>The player's list when non-empty (AssetMix), else the curated default.</summary>
        public static AssetRef Pick(string[] playerIds, string category, uint variation, uint planSalt)
        {
            if (playerIds != null) foreach (var id in playerIds) if (!string.IsNullOrEmpty(id)) return AssetMix.Pick(playerIds, variation);
            return For(category, variation, planSalt);
        }

        /// <summary>
        /// Fills every still-automatic object, surface and fence of the plan with the curated defaults.
        /// <paramref name="objects"/>/<paramref name="surfaces"/> override per category for one type.
        /// </summary>
        public static void Apply(AreaPlan plan, uint salt, IReadOnlyDictionary<string, Set> objects = null, IReadOnlyDictionary<string, Set> surfaces = null, Set fence = null)
        {
            if (plan == null) return;
            foreach (var o in plan.Objects)
            {
                if (o == null || !string.IsNullOrEmpty(o.Asset.Id) || string.IsNullOrEmpty(o.Category)) continue;
                var r = For(o.Category, o.Variation, Hashing.Mix(salt, (uint)o.Category.Length * 131u + (uint)o.Category[0]), objects);
                if (!string.IsNullOrEmpty(r.Id)) o.Asset = Merge(r, o.Asset);
            }
            foreach (var s in plan.Surfaces)
            {
                if (s == null || !string.IsNullOrEmpty(s.Asset.Id) || string.IsNullOrEmpty(s.Category)) continue;
                Set set = null;
                if (surfaces == null || !surfaces.TryGetValue(s.Category, out set)) Surfaces.TryGetValue(s.Category, out set);
                if (set != null) s.Asset = Merge(Ref(set, Hashing.Mix(salt, 0x5Fu)), s.Asset);
            }
            var f = fence ?? IndustrialFence;
            foreach (var n in plan.Nets)
                if (n != null && n.Role == NetRole.Fence && string.IsNullOrEmpty(n.Asset.Id) && string.IsNullOrEmpty(n.Category))
                    n.Asset = Merge(Ref(f, Hashing.Mix(salt, 0xFE7Cu)), n.Asset);
        }

        /// <summary>
        /// Thins decals to at most <paramref name="perHa"/> per hectare of <paramref name="areaM2"/>, evenly
        /// over plan order (every k-th kept), so wear stays wear and never turns into a carpet of stickers.
        /// </summary>
        public static int CapDecals(AreaPlan plan, float areaM2, float perHa)
        {
            if (plan == null || areaM2 <= 0f) return 0;
            int decals = 0;
            foreach (var o in plan.Objects) if (o != null && o.Role == ObjectRole.Decal) decals++;
            int max = (int)(areaM2 / 10000f * perHa);
            if (decals <= max) return 0;
            float keep = max / (float)decals, acc = 0f;
            int removed = plan.Objects.RemoveAll(o =>
            {
                if (o == null || o.Role != ObjectRole.Decal) return false;
                acc += keep;
                if (acc >= 1f) { acc -= 1f; return false; }
                return true;
            });
            return removed;
        }

        /// <summary>Keeps any built-in fallbacks the layer already gave (after the curated ones).</summary>
        private static AssetRef Merge(AssetRef curated, AssetRef existing)
        {
            if (existing.Fallbacks == null || existing.Fallbacks.Length == 0) return curated;
            var fb = new List<string>(curated.Fallbacks ?? Array.Empty<string>());
            foreach (var x in existing.Fallbacks) if (!string.IsNullOrEmpty(x) && !fb.Contains(x)) fb.Add(x);
            return new AssetRef(curated.Id, fb.ToArray());
        }
    }
}
