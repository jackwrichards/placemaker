// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/ParkingGeometry.Types.cs
// (BayKind, BayRole, Zufahrtsart, ParkingLayout), reduced to what the parking layer needs and
// translated. All coordinates are in the local row frame (x along the rows) unless noted.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal enum BayKind { Perimeter = 0, Inner = 1, InnerPerimeter = 2 }

    internal enum BayRole { Normal = 0, Disabled = 1, Electric = 2 }

    /// <summary>
    /// Entrance kinds = PLT's seven Zufahrtsarten (Geometry/ParkingGeometry.Types.cs): Zufahrt,
    /// Einfahrt, Ausfahrt, Fussweg, Gasse, GasseEin, GasseAus. Marker kinds: see ParkingIds.Kind*.
    /// </summary>
    internal enum EntranceKind { TwoWay = 0, Entry = 1, Exit = 2, Footpath = 3, Alley = 4, AlleyIn = 5, AlleyOut = 6 }

    /// <summary>The questions about an entrance kind, each answered in one place (PLT Zufahrtsarten).</summary>
    internal static class EntranceKinds
    {
        public static bool IsAlley(EntranceKind k) => k == EntranceKind.Alley || k == EntranceKind.AlleyIn || k == EntranceKind.AlleyOut;
        public static bool OneWay(EntranceKind k) => k == EntranceKind.Entry || k == EntranceKind.Exit || k == EntranceKind.AlleyIn || k == EntranceKind.AlleyOut;
        /// <summary>Drives from the lot to the street (A→B order of the course is the driving direction).</summary>
        public static bool Outbound(EntranceKind k) => k == EntranceKind.Exit || k == EntranceKind.AlleyOut;
        public static bool ForCars(EntranceKind k) => k != EntranceKind.Footpath;
        /// <summary>Cars can get in here (two-way or inbound).</summary>
        public static bool In(EntranceKind k) => ForCars(k) && !Outbound(k);
        /// <summary>Cars can get out here (two-way or outbound).</summary>
        public static bool Out(EntranceKind k) => ForCars(k) && (!OneWay(k) || Outbound(k));

        public static bool TryParse(string marker, out EntranceKind kind)
        {
            switch (marker)
            {
                case ParkingIds.KindEntrance: kind = EntranceKind.TwoWay; return true;
                case ParkingIds.KindEntry: kind = EntranceKind.Entry; return true;
                case ParkingIds.KindExit: kind = EntranceKind.Exit; return true;
                case ParkingIds.KindFootpath: kind = EntranceKind.Footpath; return true;
                case ParkingIds.KindAlley: kind = EntranceKind.Alley; return true;
                case ParkingIds.KindAlleyIn: kind = EntranceKind.AlleyIn; return true;
                case ParkingIds.KindAlleyOut: kind = EntranceKind.AlleyOut; return true;
                default: kind = EntranceKind.TwoWay; return false;
            }
        }

        /// <summary>
        /// Legacy setting entranceStyle = "alley" (round 1): every car entrance was built as a junction.
        /// Kept so saved designs rebuild the same way; the control is no longer shown.
        /// </summary>
        public static EntranceKind WithLegacyStyle(EntranceKind k, bool alleys)
        {
            if (!alleys) return k;
            switch (k)
            {
                case EntranceKind.TwoWay: return EntranceKind.Alley;
                case EntranceKind.Entry: return EntranceKind.AlleyIn;
                case EntranceKind.Exit: return EntranceKind.AlleyOut;
                default: return k;
            }
        }
    }

    internal enum LineKind { Ring = 0, Aisle = 1, Cross = 2, Connector = 3, Footpath = 4 }

    internal sealed class Bay
    {
        /// <summary>[0],[1] front edge (aisle side), [2],[3] back edge — PLT's quad convention.</summary>
        public double2[] Quad;
        public double2 Center;
        /// <summary>Unit vector from the aisle toward the back of the stall (driving-in direction).</summary>
        public double2 Axis;
        /// <summary>Unit vector along the row (direction of increasing Order).</summary>
        public double2 RowDir;
        public double Width, Length;
        public BayKind Kind;
        public BayRole Role;
        public int Row;
        public int Order;
        /// <summary>Drive line serving this stall.</summary>
        public int Line;
        public bool Removed;
    }

    internal sealed class DriveLine
    {
        public int Id;
        public double2 A, B;
        public LineKind Kind;
        public double Width;
        /// <summary>One-way in direction A→B.</summary>
        public bool OneWay;
        /// <summary>Index into Layout.Entrances for connectors/footpaths, else -1.</summary>
        public int Entrance = -1;
        public bool Removed;
        public double Length => PGeo.Len(B - A);
    }

    internal sealed class EntrancePlan
    {
        public EntranceKind Kind;
        /// <summary>Point on the lot outline.</summary>
        public double2 Point;
        /// <summary>Unit inward normal of the outline edge at Point.</summary>
        public double2 Inward;
        public int Edge;
        public double Along;
        /// <summary>Where the connector meets the lot's drive network.</summary>
        public double2 Junction;
        public int ConnectorLine = -1;
        /// <summary>Footpath line of a pedestrian entrance (Kind = Footpath), else -1.</summary>
        public int FootLine = -1;
        public double Width;
        public bool Connected;
    }

    internal sealed class Charger
    {
        public double2 Position;
        /// <summary>Facing (unit), same as the paired stalls' axis reversed (toward the aisle).</summary>
        public double2 Facing;
        public int BayA, BayB;
    }

    internal sealed class Lantern
    {
        public double2 Position;
        public double2 Facing;
        public bool Double;
    }

    internal sealed class Plant
    {
        public double2 Position;
        public bool Tree;
        public int Species;
        public uint Seed;
        /// <summary>
        /// Planted on the centre line of the green strip behind a stall row (Vegetation.PlanRows): evenly spaced,
        /// one growth stage, never thinned at random (density sets the spacing instead).
        /// </summary>
        public bool Row;
    }

    internal sealed class SurfacePoly
    {
        public List<double2> Ring;
        public bool Asphalt;
        /// <summary>The convex cells merged into Ring — the fallback when CS2 would reject Ring.</summary>
        public List<List<double2>> Cells;
    }

    internal struct LayoutIssue
    {
        public IssueSeverity Severity;
        public string Key;
        public object[] Args;
        public double2? At;
        public LayoutIssue(IssueSeverity s, string key, double2? at = null, params object[] args)
        { Severity = s; Key = key; At = at; Args = args ?? System.Array.Empty<object>(); }
    }

    /// <summary>Everything the layout engine produced, in the local frame.</summary>
    internal sealed class Layout
    {
        public Frame Frame;
        public List<double2> Lot = new List<double2>();
        public List<Bay> Bays = new List<Bay>();
        public List<DriveLine> Lines = new List<DriveLine>();
        public List<EntrancePlan> Entrances = new List<EntrancePlan>();
        public List<Charger> Chargers = new List<Charger>();
        /// <summary>Bay index pairs (into Bays) sharing one charger.</summary>
        public List<int2> ElectricPairs = new List<int2>();
        public List<Lantern> Lanterns = new List<Lantern>();
        public List<Plant> Plants = new List<Plant>();
        public List<SurfacePoly> Surfaces = new List<SurfacePoly>();
        /// <summary>
        /// (perf) The nested zone outlines corner by corner: the lot, then each inset as Inset.Of returned it (same corner
        /// count, edge i parallel to the lot's edge i), so the bands between them tile into one quad per edge
        /// (SurfaceCells.Build). ZoneChainZones: the zone of each band, then the zone inside the last ring.
        /// </summary>
        public List<double2[]> ZoneChain;
        public int[] ZoneChainZones;
        public List<LayoutIssue> Issues = new List<LayoutIssue>();
        public bool PerimeterRoad;
        public double RowAngleDeg;
        public double LotArea;
        public double AisleLength;
        /// <summary>Lot area not covered by an accepted surface (m²), set when the plan is emitted.</summary>
        public double UncoveredArea;

        public IEnumerable<Bay> LiveBays { get { foreach (var b in Bays) if (!b.Removed) yield return b; } }
        public IEnumerable<DriveLine> LiveLines { get { foreach (var l in Lines) if (!l.Removed) yield return l; } }

        public int StallCount
        {
            get { int n = 0; foreach (var b in Bays) if (!b.Removed) n++; return n; }
        }

        public int CountRole(BayRole r)
        {
            int n = 0; foreach (var b in Bays) if (!b.Removed && b.Role == r) n++; return n;
        }
    }

    /// <summary>Row frame: local x runs along the parking rows.</summary>
    internal struct Frame
    {
        public double2 Origin, X, Y;
        public Frame(double2 origin, double2 xAxis) { Origin = origin; X = PGeo.Norm(xAxis); Y = PGeo.Perp(X); }
        public double2 ToLocal(double2 w) { var d = w - Origin; return new double2(PGeo.Dot(d, X), PGeo.Dot(d, Y)); }
        public double2 ToWorld(double2 l) => Origin + X * l.x + Y * l.y;
        public double2 DirToWorld(double2 d) => X * d.x + Y * d.y;
        public double2 DirToLocal(double2 d) => new double2(PGeo.Dot(d, X), PGeo.Dot(d, Y));
    }
}
