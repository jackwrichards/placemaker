// P2-5 Parking looks: the look settings of a car park (docs/runbooks/parking-looks.md).
//
// Type -> Style -> Settings -> Add-ons (docs/ARCHITECTURE.md, "Controls model"): every style of the parking type is a
// preset of these settings, and every one of them changes the plan (ControlEffectTests). The ids are save format: never
// rename them. Read only what acts (SettingsBag.Reads greys the rest in the panel).

using System;

namespace Placemaker.Core.Layers.Parking
{
    internal static class LookIds
    {
        // ── look: paving and wear ──
        public const string Paving = "paving";              // "asphalt" | "darkAsphalt" | "concrete" | "pavers" | "gravel" | "dirt"
        public const string StallPaving = "stallPaving";    // "same" | "concrete" | "pavers" | "grassPavers"
        public const string PavingAge = "pavingAge";        // 0 (just sealed, black) .. 100 (sun-bleached)
        public const string Cleanliness = "cleanliness";    // 0 (filthy) .. 100 (spotless)
        public const string Wear = "wear";                  // 0 .. 200 % of the style's marks of use
        // ── lines ──
        public const string MarkingStyle = "markingStyle";  // "classic" | "double" | "tee" | "boxes" | "japan" | "yellow"
        public const string PaintAge = "paintAge";          // 0 (fresh) .. 100 (nearly gone)
        public const string Numbers = "stallNumbers";       // numbered stalls
        public const string Crossings = "crossings";        // "none" | "dashed" | "zebra"
        public const string AisleMarks = "aisleMarks";      // arrows, stop bars, hatched no-parking corners
        // ── edge ──
        public const string Kerbs = "kerbs";                // "concrete" | "granite" | "redWhite" | "yellow" | "flush" | "none"
        public const string WheelStops = "wheelStops";
        // ── planting ──
        public const string IslandStyle = "islandStyle";    // "grass" | "shrubs" | "hedge" | "bioswale" | "gravel"
        // ── facilities ──
        public const string Signs = "signs";
        public const string Payment = "payment";            // "none" | "entrance" | "rows" | "coin"
        public const string Barrier = "barrier";
        public const string SolarCanopies = "solarCanopies";
        public const string CartCorrals = "cartCorrals";
        public const string BikeStands = "bikeStands";
        public const string Bins = "bins";
        // ── extras (real facilities and characters of a place) ──
        public const string RundownCars = "rundownCars";    // 0 .. 100 % of the odd corners holding a dumped car
        public const string Camp = "camp";                  // a homeless camp in a corner
        public const string Construction = "construction";  // 0 .. 60 % of the lot still being built
        public const string Kiosk = "kiosk";                // a pay booth at the entrance
        public const string Drainage = "drainage";          // "none" | "grates" | "channel" | "barranca"
        // ── layout ──
        public const string Shape = "lotShape";             // "rows" | "round" | "terraced"
    }

    internal enum Paving { Asphalt = 0, DarkAsphalt = 1, Concrete = 2, Pavers = 3, Gravel = 4, Dirt = 5 }
    internal enum StallPaving { Same = 0, Concrete = 1, Pavers = 2, GrassPavers = 3 }
    internal enum MarkingStyle { Classic = 0, Double = 1, Tee = 2, Boxes = 3, Japan = 4, Yellow = 5 }
    internal enum Crossings { None = 0, Dashed = 1, Zebra = 2 }
    internal enum KerbKind { Concrete = 0, Granite = 1, RedWhite = 2, Yellow = 3, Flush = 4, None = 5 }
    internal enum IslandStyle { Grass = 0, Shrubs = 1, Hedge = 2, Bioswale = 3, Gravel = 4 }
    internal enum Payment { None = 0, Entrance = 1, Rows = 2, Coin = 3 }
    internal enum Drainage { None = 0, Grates = 1, Channel = 2, Barranca = 3 }
    internal enum LotShape { Rows = 0, Round = 1, Terraced = 2 }

    /// <summary>The resolved look of one plan (immutable). Defaults are the Classic style (today's lot, kerbed and decaled).</summary>
    internal sealed class ParkingLook
    {
        public readonly Paving Paving;
        public readonly StallPaving StallPaving;
        public readonly float PavingAge, Cleanliness, Wear;
        public readonly MarkingStyle Markings;
        public readonly float PaintAge;
        public readonly bool Numbers, AisleMarks;
        public readonly Crossings Crossings;
        public readonly KerbKind Kerbs;
        public readonly bool WheelStops;
        public readonly IslandStyle Islands;
        public readonly bool Signs, Barrier, Solar, Carts, Bikes, Bins, Camp, Kiosk;
        public readonly Payment Payment;
        public readonly float RundownCars, Construction;
        public readonly Drainage Drainage;
        public readonly LotShape Shape;

        public static readonly string[] PavingValues = { "asphalt", "darkAsphalt", "concrete", "pavers", "gravel", "dirt" };
        public static readonly string[] StallPavingValues = { "same", "concrete", "pavers", "grassPavers" };
        public static readonly string[] MarkingValues = { "classic", "double", "tee", "boxes", "japan", "yellow" };
        public static readonly string[] CrossingValues = { "none", "dashed", "zebra" };
        public static readonly string[] KerbValues = { "concrete", "granite", "redWhite", "yellow", "flush", "none" };
        public static readonly string[] IslandValues = { "grass", "shrubs", "hedge", "bioswale", "gravel" };
        public static readonly string[] PaymentValues = { "none", "entrance", "rows", "coin" };
        public static readonly string[] DrainageValues = { "none", "grates", "channel", "barranca" };
        public static readonly string[] ShapeValues = { "rows", "round", "terraced" };

        /// <summary>The look of a plan; <paramref name="markings"/> = the space markings switch, <paramref name="green"/> = there is planting.</summary>
        public ParkingLook(SettingsBag bag, bool markings, bool green)
        {
            bag = bag ?? new SettingsBag();
            Paving = (Paving)Pick(bag, LookIds.Paving, PavingValues, 0);
            bool paved = Paving != Paving.Gravel && Paving != Paving.Dirt;
            StallPaving = paved ? (StallPaving)Pick(bag, LookIds.StallPaving, StallPavingValues, 0) : StallPaving.Same;
            PavingAge = Paving == Paving.Asphalt || Paving == Paving.DarkAsphalt ? Pct(bag, LookIds.PavingAge, 45) : 0.5f;
            Cleanliness = Pct(bag, LookIds.Cleanliness, 55);
            Wear = Pct(bag, LookIds.Wear, 100);
            bool lines = markings && paved;
            Markings = lines ? (MarkingStyle)Pick(bag, LookIds.MarkingStyle, MarkingValues, 0) : MarkingStyle.Classic;
            PaintAge = lines ? Pct(bag, LookIds.PaintAge, 35) : 0f;
            Numbers = lines && bag.B(LookIds.Numbers, false);
            Crossings = paved ? (Crossings)Pick(bag, LookIds.Crossings, CrossingValues, 1) : Crossings.None;
            AisleMarks = paved && bag.B(LookIds.AisleMarks, true);
            Kerbs = (KerbKind)Pick(bag, LookIds.Kerbs, KerbValues, 0);
            WheelStops = markings && bag.B(LookIds.WheelStops, false);
            Islands = green ? (IslandStyle)Pick(bag, LookIds.IslandStyle, IslandValues, 0) : IslandStyle.Grass;
            Signs = bag.B(LookIds.Signs, true);
            Payment = (Payment)Pick(bag, LookIds.Payment, PaymentValues, 0);
            Barrier = bag.B(LookIds.Barrier, false);
            Solar = bag.B(LookIds.SolarCanopies, false);
            Carts = bag.B(LookIds.CartCorrals, false);
            Bikes = bag.B(LookIds.BikeStands, false);
            Bins = bag.B(LookIds.Bins, true);
            RundownCars = Pct(bag, LookIds.RundownCars, 0);
            Camp = bag.B(LookIds.Camp, false);
            Construction = Math.Min(0.6f, Pct(bag, LookIds.Construction, 0));
            Kiosk = bag.B(LookIds.Kiosk, false);
            Drainage = (Drainage)Pick(bag, LookIds.Drainage, DrainageValues, 1);
            Shape = (LotShape)Pick(bag, LookIds.Shape, ShapeValues, 0);
        }

        /// <summary>How worn the ground looks overall, 0..~2.4: the wear amount, more when dirty.</summary>
        public float Grime => Wear * (1.35f - 0.7f * Cleanliness);

        public bool Paved => Paving != Paving.Gravel && Paving != Paving.Dirt;

        private static int Pick(SettingsBag bag, string id, string[] values, int fallback)
        {
            int i = Array.IndexOf(values, bag.S(id, values[fallback]));
            return i < 0 ? fallback : i;
        }

        private static float Pct(SettingsBag bag, string id, double def)
        {
            double v = bag.Num(id, def);
            if (double.IsNaN(v) || double.IsInfinity(v)) v = def;
            return (float)Math.Max(0, Math.Min(200, v)) / 100f;
        }
    }
}
