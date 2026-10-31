// Parking layer settings: control ids (save format — never rename) and an immutable, validated
// snapshot read from a SettingsBag.
//
// Replaces ParkingLotTool's hand-written LayoutSettings.Clone (KruemelGames, GPL-3.0), which once
// silently dropped a field (PLT 2026-09-02: a zoning field was set, passed on and evaluated, but
// Clone did not copy it — "348 stalls with and without the area, identical in all six cases").
// Here there is nothing to clone: every run builds a fresh readonly snapshot from the bag, and
// SettingsBag.Clone copies all values generically.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Layers.Parking
{
    internal static class ParkingIds
    {
        // Essential
        public const string StallAngle = "stallAngle";          // "90" | "60" | "45"
        public const string Entrances = "entrances";            // markers
        public const string AsphaltSurface = "asphaltSurface";  // asset, surface.asphalt
        public const string Greenery = "greenery";              // "none" | "edges" | "medians"
        // More
        public const string AisleWidth = "aisleWidth";
        public const string StallWidth = "stallWidth";
        public const string StallLength = "stallLength";
        public const string Setback = "setback";
        public const string RowAngleMode = "rowAngleMode";      // "edge" | "street" | "zones" | "across" | "fixed"
        public const string RowAngle = "rowAngle";
        /// <summary>(P2-1) Turn of the grid against what it lines up with: "0" | "30" | "45" | "60" | "90" | "free".</summary>
        public const string GridTurn = "gridTurn";
        /// <summary>(P2-1) The free turn in degrees (gridTurn = "free").</summary>
        public const string GridTurnFree = "gridTurnFree";
        /// <summary>(P2-1) Hand-edit tool (ControlKind.Touches): the touch kinds below.</summary>
        public const string HandEdits = "handEdits";

        // Touch kinds (save format, Core/Touches.cs).
        public const string TouchAccessible = "stall.accessible";
        public const string TouchCharger = "stall.ev";
        public const string TouchPlain = "stall.plain";     // a plain stall: no automatic accessible or charger stall here
        public const string TouchBlank = "stall.blank";     // no stall: empty paved space
        public const string TouchIsland = "stall.island";   // no stall: a planted island
        public const string TouchEdge = "grid.edge";        // the rows line up with (and start at) this edge
        public const string LineAisle = "line.aisle";       // a sketch line (Core/Tool/LineSketch): a drive aisle through the rows
        public const string LineWalk = "line.walk";         // a sketch line: a walkway through the rows
        public static readonly string[] TouchKinds = { TouchAccessible, TouchCharger, TouchPlain, TouchBlank, TouchIsland, TouchEdge, LineAisle, LineWalk };
        public const string PerimeterRoad = "perimeterRoad";
        public const string CrossEvery = "crossEvery";          // stalls between cross lanes, 0 = none
        public const string RowEndPlanting = "rowEndPlanting";
        public const string MedianDepth = "medianDepth";
        public const string EntranceStyle = "entranceStyle";    // "connect" | "alley"
        public const string Lanterns = "lanterns";
        /// <summary>(P2-1) Painted space markings (off: invisible bays, cars still park; no chargers; PLT "Bay markings").</summary>
        public const string Markings = "markings";
        public const string LanternAsset = "lanternAsset";
        public const string LanternSpacing = "lanternSpacing";
        public const string EvShare = "evShare";                // percent of stalls with a charger
        public const string ChargerAsset = "chargerAsset";
        public const string AccessibleShare = "accessibleShare"; // percent of stalls
        public const string GreenSurface = "greenSurface";
        public const string Trees = "trees";
        public const string Bushes = "bushes";
        public const string PlantingDensity = "plantingDensity";
        public const string Fee = "fee";
        /// <summary>(s25) Grade the lot on sloped ground: level tiers, ramps ≤ 8 % between them.</summary>
        public const string Grading = "grading";

        // Zoning (PLT Randzoning / Zoningflaeche, redesigned as frontage zoning — docs/proposals/parking.md)
        public const string EdgeZoning = "edgeZoning";             // "none" | "oneSide" | "all"
        public const string ZoneType = "zoneType";                 // asset, category "zone" ("" = automatic)
        public const string ZoningDepth = "zoningDepth";           // zone cells (8 m) from the street, 1..6
        public const string ZoningPatchWidth = "zoningPatchWidth"; // cells along the edge per zoning patch marker

        // Marker kinds (save format). PLT's seven entrance kinds + bus stop + zoning patch.
        public const string KindEntrance = "entrance";   // PLT Zufahrt (two-way)
        public const string KindEntry = "entry";         // PLT Einfahrt
        public const string KindExit = "exit";           // PLT Ausfahrt
        public const string KindAlley = "alley";         // PLT Gasse (two-way junction)
        public const string KindAlleyIn = "alleyIn";     // PLT GasseEin
        public const string KindAlleyOut = "alleyOut";   // PLT GasseAus
        public const string KindFootpath = "footpath";   // PLT Fussweg (pedestrians only)
        public const string KindBusStop = "busStop";
        public const string KindZoningPatch = "zoningPatch";

        /// <summary>
        /// The marker kinds the panel offers, in display order. Bus stop and building plot are not offered (2026-10-22, the
        /// user: "doing a bus stop or a building plot for a parking lot... just don't make sense"); designs that have them
        /// still build them (PlanBuilder).
        /// </summary>
        public static readonly string[] MarkerKinds =
        {
            KindEntrance, KindEntry, KindExit, KindAlley, KindAlleyIn, KindAlleyOut, KindFootpath,
        };

        /// <summary>PLT ParkingLotToolSystem.MaxEntranceCount: one quota shared by all entrance kinds.</summary>
        public const int MaxEntrances = 15;
        /// <summary>Vehicle entrances needed before Build (two-way/alley, or one way in plus one way out).</summary>
        public const int MinVehicleEntrances = 1;
    }

    internal enum Greenery { None = 0, Edges = 1, Medians = 2 }

    internal enum RowAngleMode { Edge = 0, Across = 1, Fixed = 2, Street = 3, ZoneGrid = 4 }

    internal enum EdgeZoningMode { None = 0, OneSide = 1, All = 2 }

    /// <summary>Validated, clamped settings. Defaults follow PLT's measured CS2 preset (LayoutSettings.Cs2).</summary>
    internal sealed class ParkingSettings
    {
        // PLT LayoutSettings.Cs2: decal ParkingLotDecal01 spawns "Invisible Parking Lane -
        // Perpendicular 2.9x5.9"; the bay is 3.0 x 5.9 m with SlotInterval 3.0 (no gap).
        public const double DefaultStallWidth = 3.0;
        public const double DefaultStallLength = 5.9;
        // "Invisible Road Path - 2xTwoway" has a 7.00 m drivable core.
        public const double DefaultAisle = 7.0;
        // "Invisible Car Path - 1xTwoway", 3 m core, is what PLT used for cross lanes (Cw = 3).
        public const double CrossWidth = 3.0;
        // md = 2.5: md = 0 deleted 487 m² of green on PLT's rectangle for 26 extra stalls.
        public const double DefaultMedian = 2.5;
        public const double DefaultSetback = 1.0;
        // PLT Cr = 34 m with caps ⇒ 9 stalls between cross lanes on a 3 m stall.
        public const int DefaultCrossEvery = 9;
        // Measured one-way core of "Invisible Road Path - 1xOneway" (PLT Entrance.EinspurigeBreite).
        public const double OneWayWidth = 4.0;
        // Measured cross-section of the vanilla "Invisible Pedestrian Path" (PLT Entrance.Fusswegbreite).
        public const double FootpathWidth = 2.0;
        // CS2 drops surface pieces thinner than ~0.375 m after its 0.1 m inset (PLT Cs2Mindestkante).
        public const double Cs2MinEdge = 0.375;

        public readonly int StallAngleDeg;
        public readonly double AngleRad;
        public readonly double StallWidth, StallLength, Aisle, Setback, Median, Cross;
        public readonly Greenery Greenery;
        public readonly RowAngleMode AngleMode;
        public readonly double RowAngleDeg;
        /// <summary>(P2-1) Turn of the grid against its reference (edge, street, picked edge), degrees.</summary>
        public readonly double GridTurnDeg;
        public readonly bool PerimeterRoad, RowEndPlanting, Alleys, Lanterns, Grading, Markings;
        public readonly int CrossEvery;
        public readonly double LanternSpacing, EvShare, AccessibleShare, Fee, PlantingDensity;
        public readonly string AsphaltAsset, GreenAsset, LanternAsset, ChargerAsset;
        public readonly string[] Trees, Bushes;
        public readonly EdgeZoningMode EdgeZoning;
        public readonly string ZoneType;
        public readonly int ZoningDepthCells, ZoningPatchCells;

        /// <summary>CS2 ZoneUtils.CELL_SIZE.</summary>
        public const double ZoneCell = 8.0;
        /// <summary>CS2 BlockSystem hard-codes block.m_Size.y = 6: zone depth from the street (PLT Randzoning.cs).</summary>
        public const int MaxZoneDepthCells = 6;

        public ParkingSettings(SettingsBag bag)
        {
            bag = bag ?? new SettingsBag();
            var a = bag.S(ParkingIds.StallAngle, "90");
            StallAngleDeg = a == "45" ? 45 : a == "60" ? 60 : 90;
            AngleRad = StallAngleDeg * Math.PI / 180.0;
            StallWidth = Clamp(bag.Num(ParkingIds.StallWidth, DefaultStallWidth), 2.2, 5);
            StallLength = Clamp(bag.Num(ParkingIds.StallLength, DefaultStallLength), 4, 9);
            Aisle = Clamp(bag.Num(ParkingIds.AisleWidth, DefaultAisle), 3, 14);
            Setback = Clamp(bag.Num(ParkingIds.Setback, DefaultSetback), 0, 20);
            var g = bag.S(ParkingIds.Greenery, "medians");
            Greenery = g == "none" ? Greenery.None : g == "edges" ? Greenery.Edges : Greenery.Medians;
            Median = Greenery == Greenery.Medians ? Clamp(bag.Num(ParkingIds.MedianDepth, DefaultMedian), 0, 12) : 0;
            Cross = CrossWidth;
            var m = bag.S(ParkingIds.RowAngleMode, "edge");
            AngleMode = m == "across" ? RowAngleMode.Across : m == "fixed" ? RowAngleMode.Fixed : m == "street" ? RowAngleMode.Street : m == "zones" ? RowAngleMode.ZoneGrid : RowAngleMode.Edge;
            // read only where they act (the panel greys unread controls: SettingsBag.Reads)
            RowAngleDeg = AngleMode == RowAngleMode.Fixed ? bag.Num(ParkingIds.RowAngle, 0) : 0;
            if (double.IsNaN(RowAngleDeg) || double.IsInfinity(RowAngleDeg)) RowAngleDeg = 0;
            GridTurnDeg = 0;
            if (AngleMode != RowAngleMode.Fixed)
            {
                var turn = bag.S(ParkingIds.GridTurn, "0");
                if (turn == "free") GridTurnDeg = Clamp(bag.Num(ParkingIds.GridTurnFree, 0), 0, 179);
                else if (double.TryParse(turn, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)) GridTurnDeg = Clamp(t, 0, 179);
            }
            PerimeterRoad = bag.B(ParkingIds.PerimeterRoad, true);
            Grading = bag.B(ParkingIds.Grading, true);
            CrossEvery = (int)Math.Round(Clamp(bag.Num(ParkingIds.CrossEvery, DefaultCrossEvery), 0, 60));
            RowEndPlanting = bag.B(ParkingIds.RowEndPlanting, true) && Greenery != Greenery.None;
            Alleys = bag.S(ParkingIds.EntranceStyle, "connect") == "alley";
            Lanterns = bag.B(ParkingIds.Lanterns, true);
            Markings = bag.B(ParkingIds.Markings, true);
            LanternSpacing = Clamp(bag.Num(ParkingIds.LanternSpacing, 30), 20, 40);
            EvShare = Clamp(bag.Num(ParkingIds.EvShare, 4), 0, 50);
            AccessibleShare = Clamp(bag.Num(ParkingIds.AccessibleShare, 2), 0, 20);
            Fee = Clamp(bag.Num(ParkingIds.Fee, 10), 0, 50);
            PlantingDensity = Clamp(bag.Num(ParkingIds.PlantingDensity, 50), 0, 100);
            AsphaltAsset = bag.S(ParkingIds.AsphaltSurface, "");
            GreenAsset = bag.S(ParkingIds.GreenSurface, "");
            LanternAsset = bag.S(ParkingIds.LanternAsset, "");
            ChargerAsset = bag.S(ParkingIds.ChargerAsset, "");
            Trees = bag.L(ParkingIds.Trees);
            Bushes = bag.L(ParkingIds.Bushes);
            var ez = bag.S(ParkingIds.EdgeZoning, "none");
            EdgeZoning = ez == "all" ? EdgeZoningMode.All : ez == "oneSide" ? EdgeZoningMode.OneSide : EdgeZoningMode.None;
            ZoneType = bag.S(ParkingIds.ZoneType, "");
            ZoningDepthCells = (int)Math.Round(Clamp(bag.Num(ParkingIds.ZoningDepth, 3), 1, MaxZoneDepthCells));
            ZoningPatchCells = (int)Math.Round(Clamp(bag.Num(ParkingIds.ZoningPatchWidth, 4), 1, 20));
        }

        /// <summary>Depth of a stall row perpendicular to its aisle (rotated rectangle extent).</summary>
        public double RowDepth => StallAngleDeg == 90 ? StallLength
            : StallLength * Math.Sin(AngleRad) + StallWidth * Math.Cos(AngleRad);

        /// <summary>Distance between neighbouring stalls along the row.</summary>
        public double Pitch => StallAngleDeg == 90 ? StallWidth : StallWidth / Math.Sin(AngleRad);

        /// <summary>Extent along the row of one (rotated) stall.</summary>
        public double StallSpan => StallAngleDeg == 90 ? StallWidth
            : StallLength * Math.Cos(AngleRad) + StallWidth * Math.Sin(AngleRad);

        private static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return lo;
            return Math.Max(lo, Math.Min(hi, v));
        }
    }
}
