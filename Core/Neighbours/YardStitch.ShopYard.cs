// Placemaker neighbourhood polish — what a shop's or an office's outgrown land holds (GPL-3.0).
//
// Any building whose look makes its land a car park yard gets one (a shop or an office by default; a house or a tower when
// the player gives it such a look: its service corner is only bins). A shop's extra land used to be one concrete slab out to its fence ("it's just kind of an empty field, it's weird"). Now,
// as the land behind real shops looks:
//   * a grass strip inside the fence (not along the street) with a few slim trees and bushes between them;
//   * near the shop: paved, a service corner behind one of the building's back corners (dumpsters, a gas cage; pallets,
//     crates, cardboard, a skip on a bigger lot, a shed or a backup generator on a big one) and an overflow car park in
//     marked rows, most bays with a parked car or van (Placemaker's vehicle props; decoration: nobody drives in);
//   * further out (past shopFar of the lot): no concrete, the ground painted green with a few trees and bushes.
// Pieces are the house's own (StitchPlan.Paths and Bushes), made and kept like every garden piece.
//
// Three styles (the outgrow type's "parkStyle", whatever the building is; the built-in types: offices, apartments and towers
// clean, shops and mixed housing everyday, industry industrial):
//   * clean: a wider grass strip, trees in planters at the ends of the rows, fewer cars, a tidy service corner (bins; an
//     office's generator);
//   * everyday: as above;
//   * industrial: concrete, no grass strip or trees, more vans, a fuller service corner (pallets, crates, barrels, a skip, a
//     container, a shed), and waste ground further out (dirt, dry grass, rubble, barrels, weeds).
// The edge of the paving: worn (as the strip malls': the ground painted, dirt fading into dry grass out from the paving; a
// works always, any other yard by chance, knob "parkWorn") or a grass strip with a faint dry halo.
// The land further out (past "parkReach" of the lot) follows the outgrow type: its ground layers, patches, rocks and its
// planting style's things (natural: logs; tidy: benches and flower beds; garden: bushes; yard: stock and rubble).

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static partial class YardStitch
    {
        // BushKinds of the shop yard (appended after the garden kinds: never renumber)
        private const int Dumpster2 = 29, Dumpster3 = 30, Skip = 31, Pallet3 = 32, CratePile3 = 33, Cardboard1 = 34, Cardboard3 = 35,
            GasCage = 36, ShedBig = 37, Container = 38, Generator = 39, StallLine = 40, FirstCar = 41, CarCount = 10, FirstVan = 51, VanCount = 3,
            FirstStreetTree = 54, StreetTreeCount = 3;

        /// <summary>An object's half size (local x, local z) for the yard's fitting.</summary>
        private static float2 HalfSize(int kind)
        {
            switch (kind)
            {
                case Dumpster2: return new float2(1.36f, 0.63f);
                case Dumpster3: return new float2(1.38f, 1.06f);
                case Skip: return new float2(3.16f, 1.17f);
                case 17: case 18: case Pallet3: return new float2(0.52f, 0.62f);
                case 19: case 20: case CratePile3: return new float2(0.97f, 0.7f);
                case Cardboard1: return new float2(0.71f, 0.61f);
                case Cardboard3: return new float2(1.08f, 0.62f);
                case GasCage: return new float2(0.52f, 0.51f);
                case ShedBig: return new float2(2.19f, 1.3f);
                case Container: return new float2(1.22f, 3.02f);
                case Generator: return new float2(2.37f, 1.26f);
                case StallLine: return new float2(1.6f, 3.1f);
                default: return kind >= FirstCar && kind < FirstVan + VanCount ? new float2(1.12f, 2.7f) : new float2(0.6f, 0.6f);
            }
        }

        /// <summary>The corners of an object's footprint (half size h, grown by m) at c turned by yaw (quaternion.RotateY).</summary>
        private static float2[] Footprint(float2 c, float2 h, float yaw, float m)
        {
            var rx = new float2(math.cos(yaw), -math.sin(yaw)) * (h.x + m);
            var rz = new float2(math.sin(yaw), math.cos(yaw)) * (h.y + m);
            return Ccw(new[] { c - rx - rz, c + rx - rz, c + rx + rz, c - rx + rz });
        }

        /// <summary>The yaw that turns local +z (or local +x) along the world direction d.</summary>
        private static float YawZ(float2 d) => math.atan2(d.x, d.y);
        private static float YawX(float2 d) => math.atan2(-d.y, d.x);

        private static bool InLand(List<Region> land, float2 p)
        {
            foreach (var r in land)
                if (Poly.Contains(r.Outer, p) && !r.Holes.Any(hl => Poly.Contains(hl, p))) return true;
            return false;
        }

        /// <summary>A footprint lies in the land (corners and centre), clear of what is built and of what was placed.</summary>
        private static bool Fits(float2[] fp, List<Region> land, Obstacle built, List<float2[]> taken)
        {
            foreach (var p in fp) if (!InLand(land, p)) return false;
            if (!InLand(land, Poly.Centroid(fp))) return false;
            foreach (var t in taken) if (Overlaps(fp, t)) return false;
            foreach (var r in built.Near(fp)) if (Overlaps(fp, r.Outer)) return false;
            return true;
        }

        private static void ShopYard(IReadOnlyList<StitchHouse> houses, StitchHouse h, float2[] plot, List<Region> spare, StitchPlan plan, StitchSettings s)
        {
            uint seed = Hash((ulong)h.Id * 0x51F15EEDUL ^ 0x5409UL);
            int style = ParkStyleOf(h, s);
            bool clean = style == PolishLooks.ParkClean, works = style == PolishLooks.ParkIndustrial;
            // a worn edge (painted ground, no grass strip): a works always, any other yard by its chance
            bool worn = works || Hash(seed ^ 0x3A9Du) % 100 < (uint)math.clamp(K(h, s, "parkWorn", 50f), 0f, 100f);
            float greenW = K(h, s, "shopGreen", worn ? 0f : clean ? 4f : 3f), farD = K(h, s, "parkReach", 60f);
            string paving = works && string.IsNullOrEmpty(h.MainSurface) ? "Concrete Surface 01" : PavingOf(h);
            var built = new Obstacle(houses);
            var lot = Ccw(Lot(h));
            var plotR = new Region(Ccw(plot));

            // the grass strip: the plot's outer band inside its fence, not along the street
            var land = spare;
            var greenLand = new List<Region>();
            if (greenW > 0.2f)
            {
                var inner = Poly.Offset(Ccw(plot), greenW).Select(r => new Region(Ccw(r))).ToList();
                var band = inner.Count > 0 ? Poly.Boolean(new[] { plotR }, inner, BoolOp.Difference) : new List<Region> { plotR };
                var street = Ccw(new[] { h.ToWorld(new float2(-2000f, h.HalfLot.y - greenW - 0.5f)), h.ToWorld(new float2(2000f, h.HalfLot.y - greenW - 0.5f)),
                    h.ToWorld(new float2(2000f, h.HalfLot.y + 2000f)), h.ToWorld(new float2(-2000f, h.HalfLot.y + 2000f)) });
                band = Poly.Boolean(band, new[] { new Region(street) }, BoolOp.Difference);
                greenLand = Poly.Boolean(spare, band, BoolOp.Intersection).Where(r => r.Area >= 6f).ToList();
                if (greenLand.Count > 0) land = Poly.Boolean(spare, greenLand, BoolOp.Difference);
            }
            // near the shop: paved; further out: green ground (only when there is a good piece of it)
            var near = land;
            var far = new List<Region>();
            var reach = Poly.Offset(lot, -farD, OffsetJoin.Round, 4f, 0.5f);
            if (reach.Count > 0)
            {
                var outer = Poly.Boolean(land, reach, BoolOp.Difference);
                if (outer.Sum(r => r.Area) >= K(h, s, "shopFarMin", 150f))
                {
                    far = outer.Where(r => r.Area >= 12f).ToList();
                    near = Poly.Boolean(land, reach, BoolOp.Intersection);
                }
            }
            near = near.Where(r => r.Area >= 10f).ToList();

            int paved = 0, grass = 0;
            foreach (var r in near)
            {
                var ring = Prepared(r.HasHoles ? Poly.CutHoles(r, 0f).First() : r.Outer);
                if (ring != null) { plan.Paths.Add((h.Id, ring, paving)); paved++; }
            }
            foreach (var r in greenLand)
            {
                var ring = Prepared(r.HasHoles ? Poly.CutHoles(r, 0f).First() : r.Outer);
                if (ring != null) { plan.Paths.Add((h.Id, ring, "Grass Surface 01")); grass++; }
            }

            // what the lot's look leaves out stays out: planting (trees, bushes), features (the service corner, the car park), paint
            bool planting = K(h, s, "plant", 1f) > 0.5f, features = K(h, s, "features", 1f) > 0.5f, paint = K(h, s, "paint", 1f) > 0.5f;
            var taken = new List<float2[]>();
            int trees = planting && !works ? StreetTrees(h, plot, greenLand, built, taken, plan, seed) : 0;
            int service = features ? ServiceCorner(h, near, built, taken, plan, seed, style) : 0;
            int stalls = 0, cars = 0, planters = 0;
            if (features)
                foreach (var r in near.OrderByDescending(r => r.Area)) Parking(h, r, built, taken, plan, s, seed, style, planting, ref stalls, ref cars, ref planters);
            if (paint) EdgeWear(h, near, plan, s, seed, worn, works);
            int farTrees = FarLand(h, far, built, taken, plan, s, seed, planting && !works, paint, works, features, out int farThings);
            string[] styleName = { "auto", "clean", "everyday", "industrial" };
            plan.Notes.Add($"{h.Id}: shop yard ({styleName[style]}, {(worn ? "worn edge" : "grass edge")}): {paved} paved pieces ({near.Sum(r => r.Area):F0} m², {paving}), {grass} grass strips with {trees} trees, a service corner of {service}, {stalls} parking bays ({cars} cars, {planters} planters), {far.Sum(r => r.Area):F0} m² {(works ? "waste ground" : ThemeOf(h))} further out ({farTrees} trees, {farThings} things)");
        }

        /// <summary>Trees along the grass strip (one slim kind per shop, every 13.5 m: big crowns hid the parked cars in the game),
        /// bushes between them.</summary>
        private static int StreetTrees(StitchHouse h, float2[] plot, List<Region> greenLand, Obstacle built, List<float2[]> taken, StitchPlan plan, uint seed)
        {
            if (greenLand.Count == 0) return 0;
            int kind = FirstStreetTree + (int)(seed % StreetTreeCount), trees = 0;
            var ring = Ccw(plot);
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % ring.Length];
                float len = math.distance(a, b);
                if (len < 6f) continue;
                var d = (b - a) / len;
                var n = new float2(-d.y, d.x);   // CCW ring: the inside is on the left
                for (float t = 4.5f; t < len - 3f; t += 4.5f)
                {
                    var p = a + d * t + n * 1.5f;
                    if (!InLand(greenLand, p)) continue;
                    bool treeSpot = (int)math.round(t / 4.5f) % 3 == 1;
                    var fp = Footprint(p, new float2(0.6f, 0.6f), 0f, treeSpot ? 0.4f : 0.1f);
                    if (built.Near(fp).Any(r => Overlaps(fp, r.Outer)) || taken.Any(x => Overlaps(fp, x))) continue;
                    uint hk = Hash(seed + (uint)k * 7919u + (uint)(t * 10f));
                    if (treeSpot) { plan.Bushes.Add((h.Id, p, (hk & 1023) / 1023f * 2f * math.PI, kind)); trees++; }
                    else if (hk % 100 < 60) plan.Bushes.Add((h.Id, p, (hk & 1023) / 1023f * 2f * math.PI, (int)(hk >> 10) % 2));
                    taken.Add(fp);
                }
            }
            return trees;
        }

        /// <summary>
        /// The service corner: in the corner of the paved land behind one of the building's back corners, an L along the two
        /// edges meeting there: the bins (and a gas cage) along one, the stock (pallets, crates, cardboard; a skip, and a shed,
        /// a container or a generator on a bigger lot) along the other. Each leg is kept short (in the game, one row along the
        /// whole back of the lot read as a line of junk).
        /// </summary>
        private static int ServiceCorner(StitchHouse h, List<Region> near, Obstacle built, List<float2[]> taken, StitchPlan plan, uint seed, int style)
        {
            if (near.Count == 0) return 0;
            float area = near.Sum(r => r.Area);
            // which back corner: by the shop's hash, the other one when that has no room
            float first = (seed >> 3) % 2 == 0 ? 1f : -1f;
            foreach (float side in new[] { first, -first })
            {
                var target = h.ToWorld(new float2(side * (h.HalfLot.x - 1f), -h.HalfLot.y - 2f));
                // the spot: 2.5 m clear of the land's edge, nearest that corner
                float2 best = default;
                float bestD = float.MaxValue;
                Region home = null;
                foreach (var r in near)
                {
                    Poly.Bounds(r.Outer, out var lo, out var hi);
                    for (float x = lo.x + 0.5f; x < hi.x; x += 1f)
                        for (float y = lo.y + 0.5f; y < hi.y; y += 1f)
                        {
                            var p = new float2(x, y);
                            if (!Poly.Contains(r.Outer, p) || r.Holes.Any(hl => Poly.Contains(hl, p)) || DistanceToRing(r.Outer, p) < 2.5f) continue;
                            float dist = math.distance(p, target);
                            if (dist < bestD) { bestD = dist; best = p; home = r; }
                        }
                }
                if (home == null || bestD > 25f) continue;
                // the two nearest edges of that land that are not parallel: the corner's legs
                var edges = new List<(float D, float2 A, float2 B)>();
                var ring = home.Outer;
                for (int k = 0; k < ring.Length; k++)
                {
                    var a = ring[k];
                    var b = ring[(k + 1) % ring.Length];
                    if (math.distance(a, b) >= 3f) edges.Add((DistanceToSegment(best, a, b), a, b));
                }
                if (edges.Count == 0) continue;
                edges.Sort((x, y) => x.D.CompareTo(y.D));
                var e1 = edges[0];
                var d1 = math.normalize(e1.B - e1.A);
                int i2 = edges.FindIndex(1, e => e.D < 8f && math.abs(math.dot(math.normalize(e.B - e.A), d1)) < 0.7f);
                var legs = new List<(float2 Edge0, float2 Dir)> { (e1.A, d1) };
                // where the legs meet, near the spot (else one leg from the spot's foot on the nearest edge)
                float2 corner = e1.A + d1 * math.dot(best - e1.A, d1);
                if (i2 > 0)
                {
                    var d2 = math.normalize(edges[i2].B - edges[i2].A);
                    if (LineCross(e1.A, d1, edges[i2].A, d2, out var cross) && math.distance(cross, best) < 8f) { legs.Add((edges[i2].A, d2)); corner = cross; }
                }

                // by the type's car park style, whatever the building is: clean, its bins (a backup generator on a big lot);
                // everyday, bins and a gas cage, stock (pallets, crates, cardboard; a skip, a shed or a container on a bigger
                // lot); industrial, a fuller corner of stock (pallets, crates, barrels, a skip, a container, a shed)
                bool clean = style == PolishLooks.ParkClean, works = style == PolishLooks.ParkIndustrial;
                var bins = clean ? new List<int> { Dumpster3, Dumpster2 } : new List<int> { GasCage, Dumpster3, Dumpster2 };
                var stock = new List<int>();
                if (works)
                {
                    stock.AddRange(new[] { 17 + (int)(seed % 2), CratePile3, 21, Pallet3 });
                    if (area >= 250f) stock.Add(Skip);
                    if (area >= 400f) stock.Add(Container);
                    if (area >= 900f) stock.Add(ShedBig);
                }
                else if (clean) { if (area >= 700f) stock.Add(Generator); }
                else
                {
                    stock.AddRange(new[] { 17 + (int)(seed % 2), CratePile3, Cardboard3 });
                    if (area >= 400f) stock.Add(Skip);
                    if (area >= 700f) stock.Add((seed >> 8) % 2 == 0 ? ShedBig : Container);
                }
                var placed = new List<(float2 At, float Yaw, int Kind, float2[] Fp)>();
                var groups = legs.Count == 2 && stock.Count > 0 ? new[] { bins, stock } : new[] { bins.Concat(stock).ToList() };
                float big = works ? 6f : 0f;   // a works' corner runs further along its edges
                float[] caps = groups.Length == 2 ? new[] { 9f, (area >= 700f ? 16f : 11f) + big } : new[] { (area >= 700f ? 18f : 13f) + big };
                for (int g = 0; g < groups.Length; g++)
                {
                    var (edge0, dir) = legs[g];
                    // along the leg, away from the corner (towards the spot); its back a metre off the edge, facing the yard
                    if (math.dot(best - corner, dir) < 0f) dir = -dir;
                    var nrm = new float2(-dir.y, dir.x);
                    if (math.dot(best - corner, nrm) < 0f) nrm = -nrm;
                    // the second leg starts past the first one's depth
                    float reach = g == 0 ? 0.3f : 3.4f;
                    foreach (int kind in groups[g])
                    {
                        var half = HalfSize(kind);
                        bool alongZ = half.y > half.x;
                        float hl = alongZ ? half.y : half.x, hd = alongZ ? half.x : half.y;
                        float yaw = alongZ ? YawX(nrm) : YawZ(nrm);
                        for (int tries = 0; tries < 3; tries++)
                        {
                            float t = reach + tries * 0.5f;
                            if (t + 2f * hl > caps[g]) break;
                            var at = corner + dir * (t + hl) + nrm * (1f + hd);
                            var fp = Footprint(at, half, yaw, 0.15f);
                            if (!Fits(fp, near, built, taken) || placed.Any(x => Overlaps(fp, x.Fp))) continue;
                            placed.Add((at, yaw, kind, fp));
                            reach = t + 2f * hl + 0.3f;
                            break;
                        }
                    }
                }
                if (placed.Count < (clean ? 2 : 3)) continue;
                foreach (var (at, yaw, kind, _) in placed)
                {
                    plan.Bushes.Add((h.Id, at, yaw, kind));
                    // kept clear: a little room round the corner
                    taken.Add(Footprint(at, HalfSize(kind), yaw, 1.2f));
                }
                return placed.Count;
            }
            return 0;
        }

        /// <summary>Where two lines (point and direction) cross; false when (nearly) parallel.</summary>
        private static bool LineCross(float2 p, float2 d, float2 q, float2 e, out float2 at)
        {
            at = default;
            float den = d.x * e.y - d.y * e.x;
            if (math.abs(den) < 1e-3f) return false;
            var w = q - p;
            float t = (w.x * e.y - w.y * e.x) / den;
            at = p + d * t;
            return true;
        }

        /// <summary>An overflow car park in marked rows (3.3 m bays, 6 m deep, 6.5 m aisles) over one piece of paved land; most bays
        /// with a parked car or van (industrial: more vans, fewer cars; clean: fewer, and a tree in a planter at both ends of a
        /// row of six or more). At least four bays, else none.</summary>
        private static void Parking(StitchHouse h, Region land, Obstacle built, List<float2[]> taken, StitchPlan plan, StitchSettings s, uint seed, int style, bool planting, ref int stalls, ref int cars, ref int planters)
        {
            bool clean = style == PolishLooks.ParkClean, works = style == PolishLooks.ParkIndustrial;
            uint carShare = (uint)math.clamp(K(h, s, "parkCars", 60f), 0f, 100f), vanShare = works ? 45u : 15u;
            if (land.Area < 80f) return;
            const float W = 3.3f, D = 6f, Aisle = 6.5f;
            // rows along the land's longer side (in the house's frame)
            var local = land.Outer.Select(h.ToLocal).ToArray();
            float2 lo = MinOf(local), hi = MaxOf(local);
            bool rowsAlongX = hi.x - lo.x >= hi.y - lo.y;
            var rAxis = rowsAlongX ? h.Right : h.Forward;     // along a row
            var dAxis = rowsAlongX ? h.Forward : h.Right;     // across rows (a bay's depth)
            float r0 = rowsAlongX ? lo.x : lo.y, r1 = rowsAlongX ? hi.x : hi.y, d0 = rowsAlongX ? lo.y : lo.x, d1 = rowsAlongX ? hi.y : hi.x;
            var one = new List<Region> { land };
            var bays = new List<(float2 At, float Yaw, bool Car, uint Hash, int Row)>();
            for (int row = 0; ; row++)
            {
                // rows 0,1 face one aisle, 2,3 the next (1 and 2 back to back)
                float start = d0 + 0.5f + row * D + ((row + 1) / 2) * Aisle;
                if (start + D > d1) break;
                bool aisleAfter = row % 2 == 0;
                for (float r = r0 + 0.5f; r + W <= r1; r += W)
                {
                    var l = new float2(r + W * 0.5f, start + D * 0.5f);
                    var at = rowsAlongX ? h.ToWorld(l) : h.ToWorld(new float2(l.y, l.x));
                    float yaw = YawZ(dAxis);
                    var fp = Footprint(at, new float2(W * 0.5f, D * 0.5f), yaw, 0f);
                    if (!Fits(fp, one, built, taken)) continue;
                    uint hk = Hash(seed + (uint)row * 104729u + (uint)(int)math.round(r * 10f));
                    bays.Add((at, aisleAfter ? yaw : yaw + math.PI, hk % 100 < carShare, hk, row));
                }
            }
            if (bays.Count < 4) return;
            // clean: the first and last bay of a row of six or more is a planter (a tree with a bush at its foot)
            var ends = new HashSet<int>();
            if (clean && planting)
                foreach (var g in bays.Select((b, i) => (b, i)).GroupBy(x => x.b.Row))
                {
                    var row = g.ToList();
                    if (row.Count < 6) continue;
                    ends.Add(row[0].i);
                    ends.Add(row[row.Count - 1].i);
                }
            int treeKind = FirstStreetTree + (int)((seed >> 5) % StreetTreeCount);
            for (int i = 0; i < bays.Count; i++)
            {
                var (at, yaw, car, hk, _) = bays[i];
                taken.Add(Footprint(at, new float2(W * 0.5f, D * 0.5f), yaw, 0f));
                if (ends.Contains(i))
                {
                    plan.Bushes.Add((h.Id, at, (hk & 1023) / 1023f * 2f * math.PI, treeKind));
                    var along = new float2(math.cos(yaw), -math.sin(yaw));
                    plan.Bushes.Add((h.Id, at + along * 0.9f, yaw, (int)((hk >> 3) % 2)));
                    plan.Bushes.Add((h.Id, at - along * 0.9f, yaw + 1.3f, (int)((hk >> 5) % 2)));
                    planters++;
                    continue;
                }
                plan.Bushes.Add((h.Id, at, yaw, StallLine));
                stalls++;
                if (!car) continue;
                int kind = (hk >> 8) % 100 < vanShare ? FirstVan + (int)((hk >> 12) % VanCount) : FirstCar + (int)((hk >> 12) % CarCount);
                // nose to the aisle, a little off the bay's back
                var to = new float2(math.sin(yaw), math.cos(yaw));
                plan.Bushes.Add((h.Id, at + to * 0.2f, yaw, kind));
                cars++;
            }
        }

        /// <summary>
        /// The paving's edge, painted as the strip malls' is. Worn: dirt full at the paving, fading over a few metres, dry grass
        /// further (a works wider and stronger; sand when the type's ground is sand). Not worn: a faint dry halo only.
        /// </summary>
        private static void EdgeWear(StitchHouse h, List<Region> near, StitchPlan plan, StitchSettings s, uint seed, bool worn, bool works)
        {
            int gardenLayer = (int)K(h, s, "gardenLayer", 2f);
            int edge = works ? 3 : gardenLayer == 4 ? 4 : (int)K(h, s, "edgeLayer", 3f);
            if (edge <= 0) edge = 3;
            int k = 0;
            foreach (var r in near)
            {
                if (r.Area < 20f) continue;
                var ring = Poly.Simplify(r.Outer, 0.5f);
                uint sd = seed ^ (uint)(k++ * 7919);
                if (worn)
                {
                    float w = works ? 7f : 5f;
                    foreach (var o in Poly.Offset(ring, -w, OffsetJoin.Round, 4f, 0.5f))
                        Paint(plan, o.Outer, edge, works ? 0.85f : 0.7f, w * 0.85f, w * 0.4f, 4f, sd);
                    foreach (var o in Poly.Offset(ring, -(w + 6f), OffsetJoin.Round, 4f, 0.5f))
                        Paint(plan, o.Outer, 1, works ? 0.6f : 0.45f, 6f, 2.5f, 6f, sd ^ 0x51u);
                }
                else
                    foreach (var o in Poly.Offset(ring, -3f, OffsetJoin.Round, 4f, 0.5f))
                        Paint(plan, o.Outer, 1, 0.3f, 2.6f, 1.2f, 4f, sd);
            }
        }

        /// <summary>
        /// The land further out, in the outgrow type's own ground and things: its garden ground painted (a works: dirt, waste
        /// ground), patches of its meadow and patch grounds, rocks by its rock chance, and its planting style's things (natural:
        /// logs and stones; tidy: benches and flower beds; garden: bushes; yard or a works: rubble, barrels, pallets, weeds);
        /// a few trees with bushes round them (a works none).
        /// </summary>
        private static int FarLand(StitchHouse h, List<Region> far, Obstacle built, List<float2[]> taken, StitchPlan plan, StitchSettings s, uint seed, bool planting, bool paint, bool works, bool features, out int things)
        {
            things = 0;
            string theme = works ? "yard" : ThemeOf(h);
            int layer = works ? (int)K(h, s, "yardLayer", 3f) : (int)K(h, s, "gardenLayer", 2f);
            if (layer <= 0) layer = (int)s.Knob("lushLayer", 2f);
            float strength = works ? 0.75f : K(h, s, "gardenPaint", 0.85f);
            int meadow = (int)K(h, s, "meadowLayer", 1f), patchLayer = (int)K(h, s, "patchLayer", 3f), rockLayer = (int)K(h, s, "rockLayer", 3f);
            float meadowStrength = K(h, s, "meadowStrength", 0.6f), patchChance = works ? 90f : K(h, s, "patchChance", 25f), rockChance = works ? 20f : K(h, s, "rockChance", 12f);
            int trees = 0;
            foreach (var r in far)
            {
                if (paint)
                    foreach (var piece in r.HasHoles ? Poly.CutHoles(r, 0f) : new List<float2[]> { r.Outer })
                    {
                        if (Poly.Area(piece) < 12f) continue;
                        Paint(plan, Poly.Simplify(piece, 0.3f), layer, strength, 2f, 1.5f, 6f, seed ^ (uint)Poly.Area(piece));
                    }
                // a few trees at roomy spots, 9 m or more apart
                int want = planting ? (int)math.clamp(r.Area / 150f, 1f, 8f) : 0;
                Poly.Bounds(r.Outer, out var lo, out var hi);
                var spots = new List<float2>();
                float cell = math.max(1f, math.sqrt(r.Area) / 30f);
                for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                    for (float y = lo.y + cell * 0.5f; y < hi.y; y += cell)
                    {
                        var p = new float2(x, y);
                        if (!Poly.Contains(r.Outer, p) || r.Holes.Any(hl => Poly.Contains(hl, p)) || DistanceToRing(r.Outer, p) < 4f) continue;
                        spots.Add(p);
                    }
                int kind = FirstStreetTree + (int)((seed >> 4) % StreetTreeCount);
                var order = spots.OrderBy(q => Hash(seed ^ (uint)(int)math.round(q.x * 7f) ^ (uint)(int)math.round(q.y * 13f) * 31u)).ToList();
                // the type's ground: patches of its meadow and patch grounds (a works: dirt and dry grass all over)
                if (paint && r.Area > 120f && order.Count > 0)
                {
                    int blobs = (int)math.clamp(r.Area / 260f, 1f, 8f);
                    for (int b = 0; b < blobs && b < order.Count; b++)
                    {
                        uint hb = Hash(seed ^ 0xB10Bu ^ (uint)b * 2654435761u);
                        var c = order[(int)(hb % (uint)order.Count)];
                        bool patch = hb % 100 < patchChance;
                        int pl = patch ? patchLayer : meadow;
                        if (pl <= 0) continue;
                        Paint(plan, Blob(c, 3.5f + (hb >> 8) % 5, hb, 10), pl, patch ? 0.65f : meadowStrength, 2f, 1.4f, 3.5f, hb);
                    }
                }
                // rocks by the type's rock chance, painted round with its rock ground
                if (features && rockChance > 0f && order.Count > 0)
                {
                    int rocks = (int)math.round(r.Area / 350f * rockChance / 100f * 3f);
                    for (int q = 0; q < rocks && q < order.Count; q++)
                    {
                        uint hq = Hash(seed ^ 0x5070u ^ (uint)q * 40503u);
                        var p = order[order.Count - 1 - (int)(hq % (uint)order.Count)];
                        int rk = q % 3 == 0 ? FirstRock + (int)(hq % 3) : FirstStone + (int)(hq % 5);
                        var fp = Footprint(p, new float2(1.4f, 1.4f), 0f, 0.4f);
                        if (built.Near(fp).Any(o => Overlaps(fp, o.Outer)) || taken.Any(x => Overlaps(fp, x))) continue;
                        plan.Bushes.Add((h.Id, p, (hq & 1023) / 1023f * 2f * math.PI, rk));
                        if (paint && rockLayer > 0) Paint(plan, Blob(p, 2.6f, hq, 8), rockLayer, 0.55f, 1.4f, 0.8f, 2.5f, hq);
                        taken.Add(fp);
                        things++;
                    }
                }
                // the planting style's things
                if (features && order.Count > 0)
                {
                    var kinds = theme == "natural" ? new[] { FirstLog, FirstLog + 1, FirstLog + 2, FirstStone + 1 }
                        : theme == "tidy" ? new[] { FirstBench, FirstBench + 1, Flowerbed, Flowerbed }
                        : theme == "yard" ? new[] { FirstRubble, FirstRubble + 1, FirstRubble + 2, FirstRubble + 3, 21, 22, 17, 19, 0, 1 }
                        : new[] { 0, 1, 2, 3, 4 };
                    int count = (int)math.clamp(r.Area / (theme == "yard" ? 140f : 220f), 0f, 14f);
                    for (int q = 0; q < count && q < order.Count; q++)
                    {
                        uint hq = Hash(seed ^ 0x7417u ^ (uint)q * 69069u);
                        var p = order[(int)((hq >> 3) % (uint)order.Count)] + new float2(((hq >> 12) & 15) / 15f - 0.5f, ((hq >> 16) & 15) / 15f - 0.5f) * 2f;
                        int kd = kinds[(int)(hq % (uint)kinds.Length)];
                        var fp = Footprint(p, new float2(1f, 1f), 0f, 0.3f);
                        if (!InLand(new List<Region> { r }, p) || built.Near(fp).Any(o => Overlaps(fp, o.Outer)) || taken.Any(x => Overlaps(fp, x))) continue;
                        plan.Bushes.Add((h.Id, p, (hq & 1023) / 1023f * 2f * math.PI, kd));
                        taken.Add(fp);
                        things++;
                    }
                }
                foreach (var p in order)
                {
                    if (trees >= want) break;
                    if (plan.Bushes.Any(b => b.House == h.Id && b.Kind >= FirstStreetTree && math.distance(b.Position, p) < 9f)) continue;
                    var fp = Footprint(p, new float2(0.6f, 0.6f), 0f, 0.5f);
                    if (built.Near(fp).Any(o => Overlaps(fp, o.Outer)) || taken.Any(x => Overlaps(fp, x))) continue;
                    uint hk = Hash(seed + (uint)trees * 31u);
                    plan.Bushes.Add((h.Id, p, (hk & 1023) / 1023f * 2f * math.PI, kind));
                    for (int k = 0; k < 2; k++)
                    {
                        float ang = ((hk >> (8 + k * 4)) & 15) / 15f * 2f * math.PI;
                        plan.Bushes.Add((h.Id, p + new float2(math.cos(ang), math.sin(ang)) * 2.6f, ang, (int)((hk >> (16 + k)) % 5)));
                    }
                    taken.Add(fp);
                    trees++;
                }
                // a worn patch or two of dry ground
                if (paint && r.Area > 200f)
                {
                    var c = spots.Count > 0 ? spots[(int)(seed % (uint)spots.Count)] : Poly.Centroid(r.Outer);
                    Paint(plan, Blob(c, 3f + (seed >> 6) % 3, seed, 10), 1, 0.55f, 1.8f, 1.2f, 3f, seed ^ 0xD27u);
                }
            }
            return trees;
        }
    }
}
