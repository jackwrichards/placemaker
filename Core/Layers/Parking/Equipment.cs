// P2-5 Parking looks: what stands in a car park besides cars and trees (docs/runbooks/parking-looks.md, "Equipment").
//
// From the game's own assets (asset research 2026-10-31, lab send-lab-parking-looks-1031-123914):
//   pay station    ParkingReaderStand01 (a ticket reader on a post); pay booth ParkingLotBooth01/02
//   barrier        ParkingLotGate01/02 (boom gate)
//   signs          ParkInfosign01-03 (an info board at the entrance) and the painted P (ParkingIconDecal01)
//   canopies       Carport01-04 (5.9 x 5.9 m roofs) in rows over the stalls: the game has no solar panel prop
//   bikes, bins    BikeRack01-03 / CommercialBikeRack01-03, Trashbin01-04
//   dumped cars    Placemaker's own vehicle props (the game's vehicle props are not placeable)
//   camp           MakeshiftTent01-04, BarrelFire01/02, Trashpile01-08, OutdoorSofa01, CratePile01-03, Trashbag01-04
//   construction   ConcreteBarrier01-03, SafetyBarrier01-03, SafetyCone01-04, PortableToilet01, ShippingContainer01,
//                  PileSandSmall01 / PileStoneSmall01, Pallet01-04
// Nothing here is a cart corral: the corral is built from two short railings in a stall given up for it.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    /// <summary>A corner of the lot given over to something else (the camp, the works): its centre and radius, frame-local.</summary>
    internal sealed class LotCorner
    {
        public double2 Center;
        public double Radius;
        /// <summary>The works: the band of the lot still being built (frame x from Min to Max; NaN = none).</summary>
        public double MinX = double.NaN, MaxX = double.NaN;
    }

    internal static class Equipment
    {
        private static uint H(uint seed, int i, uint salt) => Hashing.Mix(seed, i, unchecked((int)salt));
        private static float U(uint seed, int i, uint salt) => Hashing.Unit(Hashing.Mix(seed, i, unchecked((int)salt)));

        /// <summary>
        /// Before the objects are made: the camp's corner and the works' band take their stalls (removed, so no car parks
        /// there and no marking is painted). Returns the corners for <see cref="Place"/>.
        /// </summary>
        public static (LotCorner camp, LotCorner works) Reserve(Layout layout, ParkingLook look, uint seed)
        {
            LotCorner camp = null, works = null;
            if (layout.Lot.Count < 3) return (null, null);
            PGeo.Bounds(layout.Lot, out var lo, out var hi);
            var anchors = new List<double2>();
            foreach (var e in layout.Entrances) if (e.Connected) anchors.Add(e.Point);
            if (anchors.Count == 0) anchors.Add(PGeo.Centroid(layout.Lot));
            double Far(double2 p) { double m = double.MaxValue; foreach (var a in anchors) m = Math.Min(m, PGeo.Len(a - p)); return m; }
            if (look.Construction > 0.01f)
            {
                // the works: the end of the lot farthest from the entrances, a share of its length
                double w = (hi.x - lo.x) * look.Construction;
                bool atMax = Far(new double2(hi.x, (lo.y + hi.y) * 0.5)) >= Far(new double2(lo.x, (lo.y + hi.y) * 0.5));
                works = atMax ? new LotCorner { MinX = hi.x - w, MaxX = hi.x + 1 } : new LotCorner { MinX = lo.x - 1, MaxX = lo.x + w };
                works.Center = new double2((Math.Max(lo.x, works.MinX) + Math.Min(hi.x, works.MaxX)) * 0.5, (lo.y + hi.y) * 0.5);
                foreach (var b in layout.Bays)
                    if (!b.Removed && b.Center.x >= works.MinX && b.Center.x <= works.MaxX) { b.Removed = true; }
            }
            if (look.Camp)
            {
                // the camp: the lot corner farthest from the entrances (and from the works), 9-14 m across
                double best = -1; double2 at = PGeo.Centroid(layout.Lot);
                foreach (var c in layout.Lot)
                {
                    var p = c + PGeo.Norm(PGeo.Centroid(layout.Lot) - c) * 9;
                    if (!PGeo.Contains(layout.Lot, p)) continue;
                    if (works != null && p.x >= works.MinX - 8 && p.x <= works.MaxX + 8) continue;
                    double f = Far(p);
                    if (f > best) { best = f; at = p; }
                }
                camp = new LotCorner { Center = at, Radius = 7 + 3 * U(seed, 1, 0xCA3Bu) };
                foreach (var b in layout.Bays)
                    if (!b.Removed && PGeo.Len(b.Center - at) < camp.Radius + 2) b.Removed = true;
            }
            layout.Camp = camp; layout.Works = works;
            return (camp, works);
        }

        private static PlannedObject Obj(AreaPlan plan, double2 w, double2 face, Func<double2, float> Hgt, AssetRef asset, string cat, uint v, ObjectRole role = ObjectRole.Furniture)
        {
            var o = new PlannedObject
            {
                Position = new float3((float)w.x, Hgt(w), (float)w.y), Yaw = (float)Math.Atan2(face.x, face.y), Role = role, Asset = asset,
                Category = cat, Variation = v,
            };
            plan.Objects.Add(o);
            return o;
        }

        /// <summary>Places every piece of equipment the look asks for. <paramref name="isGreen"/>: frame-local.</summary>
        public static void Place(AreaPlan plan, Layout layout, ParkingSettings s, Func<double2, float> Hgt, Func<double2, bool> isGreen, uint seed,
            LotCorner camp, LotCorner works)
        {
            var k = s.Look;
            var f = layout.Frame;
            int n = 0;
            bool FreeLocal(double2 p, double r)
            {
                if (!PGeo.Contains(layout.Lot, p)) return false;
                foreach (var l in layout.LiveLines) if (PGeo.DistPointSeg(p, l.A, l.B) < l.Width * 0.5 + r) return false;
                foreach (var b in layout.LiveBays) if (b.Quad != null && PGeo.Len(b.Center - p) < 4.5 && PGeo.Contains(b.Quad, p)) return false;
                return true;
            }
            // ── at the vehicle entrances: sign, pay station, gate, booth ──
            foreach (var e in layout.Entrances)
            {
                if (!e.Connected || !EntranceKinds.ForCars(e.Kind)) continue;
                var side = PGeo.Perp(e.Inward);
                // stand beside the lane, 1.6 m in from the edge, on the side that is free
                double2 Beside(double inward, double off)
                {
                    foreach (double sgn in new[] { 1.0, -1.0 })
                    {
                        var p = e.Point + e.Inward * inward + side * (sgn * (e.Width * 0.5 + off));
                        if (FreeLocal(p, 0.3)) return p;
                    }
                    return e.Point + e.Inward * inward + side * (e.Width * 0.5 + off);
                }
                if (k.Signs)
                {
                    var p = Beside(1.2, 1.4);
                    Obj(plan, f.ToWorld(p), f.DirToWorld(-e.Inward), Hgt, new AssetRef("ParkInfosign01", "ParkInfosign02", "ParkSignpost01"), AssetCategories.TrailSign, (uint)n++);
                    // the painted P in the lane just inside
                    if (k.Paved) Obj(plan, f.ToWorld(e.Point + e.Inward * 7.5), f.DirToWorld(-e.Inward), Hgt, new AssetRef("ParkingIconDecal01", "ParkingTextDecal01"), AssetCategories.DecalMarkings, (uint)n++, ObjectRole.Decal);
                }
                bool inbound = EntranceKinds.In(e.Kind);
                if ((k.Payment == Payment.Entrance || k.Barrier) && inbound)
                {
                    var p = Beside(5.5, 0.6);
                    Obj(plan, f.ToWorld(p), f.DirToWorld(PGeo.Norm(e.Point + e.Inward * 5.5 - p)), Hgt, new AssetRef("ParkingReaderStand01"), AssetCategories.TrailSign, (uint)n++);
                }
                if (k.Barrier)
                {
                    var p = Beside(6.5, 0.5);
                    Obj(plan, f.ToWorld(p), f.DirToWorld(PGeo.Norm(e.Point + e.Inward * 6.5 - p)), Hgt, new AssetRef("ParkingLotGate01", "ParkingLotGate02"), AssetCategories.TrailSign, (uint)n++);
                }
                if (k.Kiosk && inbound)
                {
                    var p = Beside(8.5, 1.8);
                    Obj(plan, f.ToWorld(p), f.DirToWorld(PGeo.Norm(e.Point + e.Inward * 8.5 - p)), Hgt, new AssetRef("ParkingLotBooth01", "ParkingLotBooth02", "Kiosk01"), AssetCategories.Kiosk, (uint)n++);
                }
            }
            // ── along the rows: pay stations, bins, bike racks, at the planted row ends ──
            var ends = RowEnds(layout, isGreen);
            for (int i = 0; i < ends.Count; i++)
            {
                var (p, face) = ends[i];
                if (works != null && p.x >= works.MinX && p.x <= works.MaxX) continue;
                if (camp != null && PGeo.Len(p - camp.Center) < camp.Radius + 3) continue;
                if ((k.Payment == Payment.Rows && i % 3 == 0) || (k.Payment == Payment.Coin && i == 0))
                    Obj(plan, f.ToWorld(p), f.DirToWorld(face), Hgt, new AssetRef("ParkingReaderStand01"), AssetCategories.TrailSign, (uint)n++);
                else if (k.Bins && i % 3 == 1)
                    Obj(plan, f.ToWorld(p), f.DirToWorld(face), Hgt, new AssetRef("Trashbin01", "Trashbin02"), AssetCategories.Bin, (uint)n++, ObjectRole.Bin);
                else if (k.Bikes && i % 4 == 2)
                    Obj(plan, f.ToWorld(p), f.DirToWorld(PGeo.Perp(face)), Hgt, new AssetRef("CommercialBikeRack01", "BikeRack01"), AssetCategories.TrailSign, (uint)n++);
            }
            // ── canopies over the stall rows (carport roofs end to end), chargers under them stay ──
            if (k.Solar)
            {
                int cn = 0;
                foreach (var b in layout.LiveBays)
                {
                    if (b.Quad == null || b.Role == BayRole.Blank) continue;
                    // one roof per two stalls: the even stalls of a row (by Order) carry it, centred between the pair
                    if ((b.Order & 1) != 0) continue;
                    var c = b.Center + b.RowDir * (b.Width * 0.5);
                    Obj(plan, f.ToWorld(c), f.DirToWorld(-b.Axis), Hgt, new AssetRef("Carport02", "Carport03", "Carport01"), AssetCategories.Shelter, (uint)(n + cn++));
                }
                plan.Facts["canopies"] = cn;
                n += cn;
            }
            // ── cart corrals: every 24th stall of a long row given up to two railings ──
            if (k.Carts)
            {
                int cc = 0;
                foreach (var b in layout.LiveBays)
                {
                    if (b.Quad == null || b.Role != BayRole.Normal || b.Order % 24 != 12) continue;
                    b.Role = BayRole.Blank;
                    foreach (double sgn in new[] { -0.6, 0.6 })
                    {
                        var p = b.Center + b.RowDir * sgn;
                        Obj(plan, f.ToWorld(p), f.DirToWorld(b.Axis), Hgt, new AssetRef("EU_Railing02", "EU_Railing01"), AssetCategories.Fence, (uint)(n + cc++), ObjectRole.FencePiece);
                    }
                }
                plan.Facts["cartCorrals"] = cc / 2;
                n += cc;
            }
            // ── dumped cars in the odd spots beside the real stalls (past the row ends) ──
            if (k.RundownCars > 0.01f)
            {
                int cars = 0, i = 0;
                foreach (var spot in OddSpots(layout, isGreen, FreeLocal))
                {
                    i++;
                    if (U(seed, i, 0xCA25u) > k.RundownCars) continue;
                    double turn = (U(seed, i, 0x7A2Bu) - 0.5) * 0.9;
                    var face = PGeo.Rotate(spot.axis, turn);
                    var o = Obj(plan, f.ToWorld(spot.p), f.DirToWorld(face), Hgt, new AssetRef(Types.Yards.VehicleProps.Car(1 + (int)(U(seed, i, 0xC0Au) * 9.99f)), "Placemaker Vehicle Car 1"), AssetCategories.RuinsJunk, (uint)(n + cars));
                    o.ColorSeed = (int)(H(seed, i, 0xC01u) & 0xFFFF);
                    cars++;
                }
                plan.Facts["rundownCars"] = cars;
                n += cars;
            }
            // ── the camp ──
            if (camp != null)
            {
                string[] tents = { "MakeshiftTent01", "MakeshiftTent02", "MakeshiftTent03", "MakeshiftTent04" };
                int tn = 3 + (int)(U(seed, 2, 0xCA3Bu) * 3);
                for (int i = 0; i < tn; i++)
                {
                    double a = i * 2 * Math.PI / tn + U(seed, i, 0x7E1u) * 0.6;
                    var p = camp.Center + new double2(Math.Cos(a), Math.Sin(a)) * camp.Radius * (0.45 + 0.35 * U(seed, i, 0x7E2u));
                    Obj(plan, f.ToWorld(p), f.DirToWorld(PGeo.Norm(camp.Center - p)), Hgt, new AssetRef(tents[i % 4], tents[0]), AssetCategories.Camping, (uint)(n++));
                }
                Obj(plan, f.ToWorld(camp.Center), new double2(0, 1), Hgt, new AssetRef("BarrelFire01", "BarrelFire02"), AssetCategories.CampingFire, (uint)n++);
                string[] junk = { "OutdoorSofa01", "Trashpile03", "CratePile01", "Trashbag01", "Trashpile05", "Trashbag02", "Pallet02" };
                for (int i = 0; i < junk.Length; i++)
                {
                    double a = U(seed, i, 0x7E3u) * 2 * Math.PI;
                    var p = camp.Center + new double2(Math.Cos(a), Math.Sin(a)) * camp.Radius * (0.2 + 0.7 * U(seed, i, 0x7E4u));
                    Obj(plan, f.ToWorld(p), new double2(Math.Cos(a * 3), Math.Sin(a * 3)), Hgt, new AssetRef(junk[i], "Trashpile01"), AssetCategories.RuinsJunk, (uint)n++);
                }
                plan.Facts["camp"] = tn;
            }
            // ── the works: a barrier line across the lot, the site behind it ──
            if (works != null)
            {
                PGeo.Bounds(layout.Lot, out var lo, out var hi);
                bool atMax = works.MaxX > hi.x;
                double xLine = atMax ? works.MinX : works.MaxX;
                int wn = 0;
                for (double y = lo.y + 1; y < hi.y; y += 2.2)
                {
                    var p = new double2(xLine, y);
                    if (!PGeo.Contains(layout.Lot, p)) continue;
                    Obj(plan, f.ToWorld(p), f.DirToWorld(new double2(0, 1)), Hgt, new AssetRef(wn % 5 == 2 ? "SafetyBarrier01" : "ConcreteBarrier01", "ConcreteBarrier01"), AssetCategories.ConstructionBarrier, (uint)(n + wn++));
                }
                double inner = atMax ? 1 : -1;
                var c = works.Center;
                double depth = Math.Abs(Math.Min(hi.x, works.MaxX) - Math.Max(lo.x, works.MinX));
                string[] site = { "ShippingContainer01", "PortableToilet01", "PileSandMedium01", "PileStoneMedium01", "PileSandSmall01", "Pallet01", "Pallet03", "SafetyCone01", "SafetyCone02", "SafetyCone01",
                    Types.Yards.VehicleProps.Loader(1), Types.Yards.VehicleProps.Truck(1), "TrashContainerEmpty01", "CableReel01" };
                for (int i = 0; i < site.Length; i++)
                {
                    var p = new double2(c.x + (U(seed, i, 0x5A1u) - 0.5) * depth * 0.6, lo.y + (hi.y - lo.y) * (0.15 + 0.7 * U(seed, i, 0x5A2u)));
                    if (!PGeo.Contains(layout.Lot, p)) continue;
                    Obj(plan, f.ToWorld(p), f.DirToWorld(new double2(inner, 0)), Hgt, new AssetRef(site[i], "SafetyCone01"), AssetCategories.ConstructionMaterial, (uint)(n + wn++));
                }
                plan.Facts["worksPieces"] = wn;
            }
        }

        /// <summary>Spots on the planting at the ends of stall rows (frame-local) and the direction they face (the aisle).</summary>
        private static List<(double2 p, double2 face)> RowEnds(Layout layout, Func<double2, bool> isGreen)
        {
            var r = new List<(double2, double2)>();
            foreach (var b in layout.LiveBays)
            {
                if (b.Quad == null) continue;
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var p = b.Center + b.RowDir * (sgn * (b.Width * 0.5 + 1.0)) - b.Axis * (b.Length * 0.25);
                    if (!isGreen(p) || !isGreen(p + b.RowDir * sgn * 0.6)) continue;
                    bool dup = false;
                    foreach (var q in r) if (PGeo.Len(q.Item1 - p) < 8) { dup = true; break; }
                    if (!dup) r.Add((p, -b.Axis));
                }
            }
            return r;
        }

        /// <summary>Paved spots just past the row ends, no stall and no lane: where a dumped car would be left.</summary>
        private static IEnumerable<(double2 p, double2 axis)> OddSpots(Layout layout, Func<double2, bool> isGreen, Func<double2, double, bool> free)
        {
            var taken = new List<double2>();
            foreach (var b in layout.LiveBays)
            {
                if (b.Quad == null) continue;
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var p = b.Center + b.RowDir * (sgn * (b.Width * 1.15));
                    // on the paving or on the verge (a car dumped on the grass at a row end reads as abandoned at once)
                    if (!free(p, 0.9)) continue;
                    bool dup = false;
                    foreach (var q in taken) if (PGeo.Len(q - p) < 6) { dup = true; break; }
                    if (dup) continue;
                    taken.Add(p);
                    yield return (p, b.Axis);
                }
            }
        }
    }
}
