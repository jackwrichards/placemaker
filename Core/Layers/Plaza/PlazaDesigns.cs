// Designed plazas (2026-10-28, the user: "very intricate designed plazas ... nice styling of the surfaces ... sleek,
// modern looking, but some of them might be more vintage looking, like terraced gardens or fenced gardens with
// concrete in between or fenced gardens with designs in between. Think of Union Square in New York").
//
// The classic plaza (PlazaLayer.PlanCore) composes from generic parts round the entrances: a rondel, walks from every
// access point, a ring of trees. On a city block the tool suggests 6 to 8 access points anywhere on the outline and
// the walks cut the square at odd angles. A designed plaza instead lays a real square's plan on the square's own axes
// (PlazaDressing.SquareAxis) in a box fitted round its focal point, and connects every entrance through a paved
// perimeter walk that runs just inside the outline, so the composition never depends on where the gates are:
//
//   grand     a tree-lined promenade round the edge (two rows of trees), lawns railed with low fences between a
//             spine, a cross walk and diagonals meeting at a round centre (statue or flag), a paved forecourt with
//             stone steps and flags at one end. (Union Square, New York.)
//   rooms     garden rooms: a grid of hedged rooms (lawn quarters round a planted centre, or a flower court round a
//             tree), walks between them laid with a stone kerb round every room and a medallion where walks cross.
//   railed    railed gardens: large lawns with cut corners behind low railings, trees round their insides, broad pale
//             walks between them crossed by dark stone bands that run across the whole square, a circus at the centre.
//   terraces  a terraced garden: three levels rising to a belvedere in the middle, each step a planted bank with a
//             hedge on its top edge and stairs on the axes; vintage warm stone.
//   modern    pale large slabs with dark bands across the whole square, long linear planters with benches along
//             them, a grid bosque of trees in tree pits either side, a centre pad with jets (a dark stone mirror
//             without fountains installed).
//   piazza    an old-town piazza: setts laid round a central fountain in rings and rays (a compass of dark stone),
//             benches round the fountain, chestnuts in the corners, cafe terraces on the edges.
//
// Paving: every element is added to the square's PavingPlan with a priority (Lay); the plan is a tiling, first claims
// first (PavingKit header: overlapping paved surfaces z-fight), so the order here is pits, features, lawns, frame,
// walks, promenade, bands, field. Without fountains or statues installed (the clean game) a centre is planted
// (PlazaDressing.SpecimenCentre) or carries a flag pole.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        public const string Control = "design";
        public const string Classic = "classic";
        public static readonly string[] Ids = { Classic, "grand", "rooms", "railed", "terraces", "modern", "piazza",
            "sunkenPlaza", "waterSteps", "marketHall", "dining", "grandSteps", "grungy", "theatre", "market", "transit", "fair", "winterFair" };

        public static bool Handles(string id) => !string.IsNullOrEmpty(id) && id != Classic && Array.IndexOf(Ids, id) > 0;

        public struct Result
        {
            public float2 Centre;
            public float CoreRadius;
        }

        // Paving priorities (lower claims first).
        internal const int PPit = 0, PFeature = 10, PKerb = 12, PGreen = 20, PFrame = 25, PWalk = 30, PPromenade = 60, PPattern = 70, PField = 90;

        public static Result Plan(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, PavingPalette palette, PavingPlan pave,
            float2 axis, bool fountainChild, string design)
        {
            var k = new Kit(ctx, occ, region, gates, palette, pave, axis, fountainChild);
            ctx.Plan.Facts["plazaDesign"] = Array.IndexOf(Ids, design);
            // (round 3) a real square's size in the middle of a big site, a surround in the design's manner round it (Surround.cs);
            // the transit forecourt keeps its whole site (its platform runs along the street)
            // the theatre grows with its site instead (a real Greek theatre is 60-80 m across) and lays out its own grounds
            if (design != "theatre") k.CapBox(design == "transit");
            switch (design)
            {
                case "grand": Grand(k); break;
                case "rooms": Rooms(k); break;
                case "railed": Railed(k); break;
                case "terraces": Terraces(k); break;
                case "modern": Modern(k); break;
                case "sunkenPlaza": SunkenPlaza(k); break;
                case "waterSteps": WaterSteps(k); break;
                case "marketHall": MarketHall(k); break;
                case "dining": Dining(k); break;
                case "grandSteps": GrandSteps(k); break;
                case "grungy": Grungy(k); break;
                case "theatre": Theatre(k); break;
                case "market": Market(k); break;
                case "transit": Transit(k); break;
                case "fair": Fair(k, false); break;
                case "winterFair": Fair(k, true); break;
                default: Piazza(k); break;
            }
            ctx.Check();
            if (k.Capped) k.Surround(SurroundManner(design)); else k.FillLeftover();
            ctx.Check();
            // The square's edge lamps in one rhythm (designs light their own walks first; these fill the outline).
            PlazaDressing.Lighting(ctx, occ, region, gates, k.C, 0f, k.Lamp);
            PlazaDressing.Bollards(ctx, occ, region, gates);
            k.Flush();
            // (the plazas makeover) the designs name vanilla props directly: in an install without a category (or a test
            // scope without it) those props are left out and the plan says so, like every type's "no assets" info
            var missing = new HashSet<string>(StringComparer.Ordinal);
            ctx.Plan.Objects.RemoveAll(o =>
            {
                if (o == null || string.IsNullOrEmpty(o.Category) || AssetAvailability.Has(o.Category)) return false;
                missing.Add(o.Category);
                return true;
            });
            foreach (var cat in missing) AssetAvailability.Require(ctx.Plan, cat, null, Keys.Category(cat));
            ctx.Plan.Facts["designTrees"] = k.TreeCount;
            ctx.Plan.Facts["designBenches"] = k.BenchCount;
            ctx.Plan.Facts["designHedges"] = k.HedgeCount;
            ctx.Plan.Facts["designFences"] = k.FenceCount;
            ctx.Plan.Facts["designWalls"] = k.WallCount;
            ctx.Plan.Facts["designFlights"] = k.FlightCount;
            ctx.Plan.Facts["designCafes"] = k.CafeCount;
            ctx.Plan.Facts["designCanopies"] = k.CanopyCount;
            ctx.Plan.Facts["designWallFountains"] = k.FountainCount;
            return new Result { Centre = k.C, CoreRadius = k.Core };
        }

        // ───────────── grand square (Union Square) ─────────────

        private static void Grand(Kit k)
        {
            float m = math.min(k.HA, k.HB);
            float fw = k.Fw;
            float prom = math.clamp(m * 0.12f, 6f, 11f);
            k.Leftover(fw + prom + 1f, "fence");
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            // Two rows of trees along the promenade: benches under the inner row looking out over it, lamps in the outer.
            k.RingTrees(fw + 1.7f, 8.5f, true, 0, true);
            k.RingTrees(fw + prom - 1.7f, 8.5f, true, -1, false);

            float ia = k.HA - fw - prom - 1.5f, ib = k.HB - fw - prom - 1.5f;
            if (ia < 14f || ib < 12f) { Piazza(k); return; }
            // The forecourt at the -A end: paved in the focal stone, a dark border, stone steps up to the lawns, flags.
            float fc = math.clamp(ia * 2f * 0.17f, 9f, 20f);
            float f0 = -ia, f1 = -ia + fc;
            var court = k.Box(f0, f1, -ib, ib);
            k.Lay(PKerb, PavingShapes.Frame(court.Ring, 1f), k.P.Band);
            for (int s = 0; s < 3; s++)
            {
                float u = f1 - 1.4f - s * 1.5f;
                k.Lay(PFeature, PavingShapes.Band(k.W(u, -ib + 1.2f), k.W(u, ib - 1.2f), 0.6f), k.P.Band);
            }
            k.Lay(PFeature + 1, court.Ring, k.P.Focal);
            float fu = (f0 + f1) * 0.5f - 1f;
            if (k.CentreAvailable)
                k.CentrePiece(k.W(fu, 0f), 3f, "statue");
            else
                for (int i = -2; i <= 2; i++)
                    k.Flag(k.W(fu, i * math.min(5f, ib * 0.18f)));
            // 2026-10-29: the greenmarket (Union Square's farmers' market): stalls along both long sides facing in,
            // cafe tables under parasols between them and the statue or flags.
            foreach (float sv in new[] { -1f, 1f })
            {
                int st = 0;
                for (float u = f0 + 3f; u <= f1 - 5f; u += 6.2f, st++)
                    if (!k.Stall(k.W(u, sv * (ib - 3.2f)), -k.S * sv, (st % 4) == 3)) k.Pot(k.W(u, sv * (ib - 2f)));
                if (f1 - f0 > 12f) k.CafeGrid(f0 + 3f, f1 - 4f, sv > 0 ? ib * 0.3f : -ib + 7.5f, sv > 0 ? ib - 7.5f : -ib * 0.3f, 5.4f, true);
            }
            k.Seat(k.W(f1 - 6.2f, -ib * 0.45f), -k.A);
            k.Seat(k.W(f1 - 6.2f, ib * 0.45f), -k.A);

            // The lawns between a spine, a cross walk and four diagonals meeting at a round centre.
            float u0 = f1 + 1.5f, u1 = ia;
            float cu = (u0 + u1) * 0.5f;
            var cL = k.W(cu, 0f);
            float w = math.clamp(m * 0.06f, 3.5f, 5f);
            float rc = math.clamp(math.min((u1 - u0) * 0.5f, ib) * 0.22f, 6f, 12f);
            var walks = new List<(float2 a, float2 b)>
            {
                (k.W(u0 - 1f, 0f), k.W(u1 + 1f, 0f)),
                (k.W(cu, -ib - 1f), k.W(cu, ib + 1f)),
                (k.W(u0, -ib), cL), (k.W(u0, ib), cL), (k.W(u1, -ib), cL), (k.W(u1, ib), cL),
            };
            foreach (var (a, b) in walks) k.Lay(PWalk, PavingShapes.Band(a, b, w), k.P.Path);
            k.Lay(PFeature, PavingShapes.Polygon(cL, rc, 32, k.Phase), k.P.Focal);
            k.Lay(PKerb, PavingShapes.Annulus(cL, rc, 1.2f), k.P.Band);
            float sidewalk = 2.6f;
            var clips = new List<float2[]>();
            foreach (var (a, b) in walks) clips.Add(PavingShapes.Band(a + math.normalizesafe(a - b) * 2f, b + math.normalizesafe(b - a) * 2f, w + sidewalk * 2f));
            clips.Add(PavingShapes.Polygon(cL, rc + 1.2f + sidewalk, 16, k.Phase));
            var lawns = k.Pieces(k.Box(u0, u1, -ib, ib).Ring, clips, 40f);
            foreach (var lawn in lawns)
            {
                k.Lay(PGreen, lawn, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                k.FenceRing(lawn, 0.35f);
                k.ShrubBorder(lawn, 1.1f, 3.2f);
                k.TreesIn(lawn, 11f, 3f, false);
            }
            // Benches either side of every walk (backs to the railings), lamps on the walk edges.
            foreach (var (a, b) in walks) k.WalkSeats(a, b, w * 0.5f + 1.1f, 7.5f, cL, rc + 4f, 14f);
            // The centre: a statue (or a flag in a ring of flowerbeds), benches round it looking in.
            k.Core = rc + 1.2f;
            if (k.FountainChild) k.CentreClear(cL, rc);
            else if (k.CentreAvailable) k.CentrePiece(cL, rc - 2f, "statue");
            else k.FountainDrum(cL, math.clamp(rc * 0.42f, 2.6f, 4.2f), false);
            k.SeatRing(cL, rc - 1.2f, 8, true);
            k.LampRing(cL, rc + 0.6f, 4);
        }

        // ───────────── garden rooms (parterre) ─────────────

        private static void Rooms(Kit k)
        {
            float fw = k.Fw;
            float prom = 5f;
            k.Leftover(fw + prom + 1f, "hedge");
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            k.RingTrees(fw + 1.5f, 9f, true, 0, true);

            float ia = k.HA - fw - prom - 1f, ib = k.HB - fw - prom - 1f;
            const float w = 4.6f, target = 20f;
            int nu = Count(ia * 2f, w, target), nv = Count(ib * 2f, w, target);
            float su = (ia * 2f - (nu - 1) * w) / nu, sv = (ib * 2f - (nv - 1) * w) / nv;
            if (su < 9f || sv < 9f) { Piazza(k); return; }
            k.Ctx.Plan.Facts["gardenRooms"] = nu * nv;
            int ci = nu / 2, cj = nv / 2;
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    float uc = -ia + su * 0.5f + i * (su + w), vc = -ib + sv * 0.5f + j * (sv + w);
                    var room = new OrientedRect { Center = k.W(uc, vc), Axis = k.A, HalfLength = su * 0.5f, HalfWidth = sv * 0.5f };
                    if (!k.Fits(room, 0.5f)) continue;
                    bool centre = (nu % 2 == 1 && nv % 2 == 1) ? i == ci && j == cj : false;
                    k.Lay(PKerb, PavingShapes.Frame(room.Ring, 0.6f), k.P.Band);
                    // Hedges just inside the kerb, open in the middle of every side.
                    k.HedgeBox(room, 1.1f, 3.4f, centre);
                    var inner = new OrientedRect { Center = room.Center, Axis = room.Axis, HalfLength = room.HalfLength - 1.8f, HalfWidth = room.HalfWidth - 1.8f };
                    if (centre) FocalRoom(k, inner);
                    else if (((i + j) & 1) == 0) LawnRoom(k, inner);
                    else FlowerRoom(k, inner);
                    // Lamps at the room's corners, on the walk.
                    foreach (var (cu2, cv2) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                        k.Light(room.Local(cu2 * (room.HalfLength + w * 0.5f), cv2 * (room.HalfWidth + w * 0.5f)));
                }
            // A medallion where four walks cross: a dark diamond with a pale heart.
            for (int i = 0; i < nu - 1; i++)
                for (int j = 0; j < nv - 1; j++)
                {
                    var c = k.W(-ia + (i + 1) * (su + w) - w * 0.5f, -ib + (j + 1) * (sv + w) - w * 0.5f);
                    k.Lay(PFeature, Diamond(c, k.A, w * 0.9f), k.P.Inlay);
                    k.Lay(PFeature - 1, Diamond(c, k.A, w * 0.45f), k.P.Focal);
                }
            // Thin dark runners down the middle of the walks.
            for (int i = 0; i < nu - 1; i++)
            {
                float u = -ia + (i + 1) * (su + w) - w * 0.5f;
                k.Lay(PPattern, PavingShapes.Band(k.W(u, -ib), k.W(u, ib), 0.5f), k.P.Inlay);
            }
            for (int j = 0; j < nv - 1; j++)
            {
                float v = -ib + (j + 1) * (sv + w) - w * 0.5f;
                k.Lay(PPattern, PavingShapes.Band(k.W(-ia, v), k.W(ia, v), 0.5f), k.P.Inlay);
            }
            // 2026-10-29: arbors (modular pergola bays) over the two walks either side of the middle row, open where walks cross.
            if (nv >= 3)
            {
                int mid = nv / 2;
                var crossU = new List<float>();
                for (int i = 0; i < nu - 1; i++) crossU.Add(-ia + (i + 1) * (su + w) - w * 0.5f);
                foreach (int j in new[] { mid - 1, mid })
                {
                    float v = -ib + (j + 1) * (sv + w) - w * 0.5f;
                    float s0 = -ia + 0.5f;
                    foreach (float cu in crossU)
                    {
                        if (cu - w * 0.5f - 0.6f - s0 > 3f) k.Canopy(k.W(s0, v - 1.5f), k.W(cu - w * 0.5f - 0.6f, v - 1.5f), 1, "PergolaModular01");
                        s0 = cu + w * 0.5f + 0.6f;
                    }
                    if (ia - 0.5f - s0 > 3f) k.Canopy(k.W(s0, v - 1.5f), k.W(ia - 0.5f, v - 1.5f), 1, "PergolaModular01");
                }
            }
            if (nu % 2 == 0 || nv % 2 == 0) k.Core = 0f;
        }

        private static int Count(float span, float w, float target)
        {
            // An odd count (a middle room for the focal piece), rooms no smaller than the target.
            int n = math.max(1, (int)math.floor((span + w) / (target + w)));
            if ((n & 1) == 0) n = math.max(1, n - 1);
            return n;
        }

        /// <summary>Four lawn quarters round a planted centre, gravel cross paths from the hedge openings, topiary on the corners.</summary>
        private static void LawnRoom(Kit k, OrientedRect r)
        {
            float pw = 1.6f;
            k.Lay(PFeature, PavingShapes.Band(r.Local(-r.HalfLength, 0f), r.Local(r.HalfLength, 0f), pw), k.P.Path);
            k.Lay(PFeature, PavingShapes.Band(r.Local(0f, -r.HalfWidth), r.Local(0f, r.HalfWidth), pw), k.P.Path);
            float rr = math.min(3.2f, math.min(r.HalfLength, r.HalfWidth) * 0.4f);
            k.Lay(PFeature - 2, PavingShapes.Polygon(r.Center, rr - 1.1f, 20), k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
            k.Lay(PFeature - 1, PavingShapes.Polygon(r.Center, rr, 20), k.P.Focal);
            k.Lay(PGreen, r.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
            k.Topiary(r.Center);
            for (int q = 0; q < 4; q++)
            {
                float a = q * math.PI * 0.5f + math.PI * 0.25f;
                k.Flowerbed(r.Center + new float2(math.cos(a), math.sin(a)) * (rr + 1.6f), a);
            }
            foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                k.Topiary(r.Local(su * (r.HalfLength - 1f), sv * (r.HalfWidth - 1f)));
                k.Topiary(r.Local(su * (pw * 0.5f + 1.2f), sv * (r.HalfWidth - 1f)));
                k.Topiary(r.Local(su * (r.HalfLength - 1f), sv * (pw * 0.5f + 1.2f)));
            }
            // A bench each side of the centre on the long path, facing it.
            foreach (float s in new[] { -1f, 1f })
                k.Seat(r.Local(s * (rr + 3.2f), pw * 0.5f + 0.9f), -r.Side);
        }

        /// <summary>A paved flower court: two long beds of flowers either side of a tree, benches looking at it.</summary>
        private static void FlowerRoom(Kit k, OrientedRect r)
        {
            k.Lay(PGreen + 1, r.Ring, k.P.Accent);
            bool along = r.HalfLength >= r.HalfWidth;
            float half = along ? r.HalfLength : r.HalfWidth, across = along ? r.HalfWidth : r.HalfLength;
            var dir = along ? r.Axis : r.Side;
            var nrm = new float2(-dir.y, dir.x);
            float bw = math.clamp(across * 0.28f, 1.8f, 3.2f);
            foreach (float s in new[] { -1f, 1f })
            {
                var c = r.Center + nrm * (s * (across - bw * 0.5f - 1f));
                var a = c - dir * (half - 2f); var b = c + dir * (half - 2f);
                var bed = PavingShapes.Band(a, b, bw);
                if (bed == null) continue;
                k.Lay(PFeature, bed, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                foreach (var p in LeisureKit.Along(a, b, 2.8f)) k.Flowerbed(p, math.atan2(dir.x, dir.y));
                k.Occ.TakeCorridor(a, b, bw * 0.5f, 3);
            }
            k.Lay(PPit, LayerRegion.Rect(r.Center, k.A, 1.4f, 1.4f), k.P.Inlay);
            k.Tree(r.Center, false, PlazaArt.GardenTrees);
            foreach (float s in new[] { -1f, 1f })
                k.Seat(r.Center + dir * (s * 4f), -dir * s);
        }

        /// <summary>The middle room: the square's focal piece on a paved floor, benches round it, beds in the corners.</summary>
        private static void FocalRoom(Kit k, OrientedRect r)
        {
            k.Lay(PGreen + 1, r.Ring, k.P.Focal);
            float room = math.min(r.HalfLength, r.HalfWidth) - 2.5f;
            float rad = math.max(3.5f, room * 0.55f);
            k.Lay(PKerb, PavingShapes.Annulus(r.Center, rad, 0.9f), k.P.Band);
            k.Core = rad + 0.9f;
            if (k.FountainChild) k.CentreClear(r.Center, rad);
            else if (k.CentreAvailable) k.CentrePiece(r.Center, rad - 1f, "fountain");
            else k.FountainDrum(r.Center, math.clamp(rad * 0.45f, 2.6f, 4f), false);
            foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                k.Flowerbed(r.Local(su * (r.HalfLength - 1.6f), sv * (r.HalfWidth - 1.6f)), 0f);
        }

        private static float2[] Diamond(float2 c, float2 axis, float r)
        {
            var side = new float2(-axis.y, axis.x);
            return new[] { c + axis * r, c + side * r, c - axis * r, c - side * r };
        }

        // ───────────── railed gardens ─────────────

        private static void Railed(Kit k)
        {
            float fw = k.Fw;
            float m = math.min(k.HA, k.HB);
            k.Leftover(fw + 5f, "fence");
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            // Broad pale walks everywhere the lawns leave (the field), dark bands laid across them below.
            k.Lay(PField, PavingShapes.Inset(k.Region.Ring, fw), k.P.Path);
            k.RingTrees(fw + 2.2f, 9f, true, 1, true);

            float ia = k.HA - fw - 6.5f, ib = k.HB - fw - 6.5f;
            float w = math.clamp(m * 0.13f, 7f, 12f);
            // Lawns of about 34 m: 3 x 3 round a paved middle garden on a big square, 2 x 2 round a circus on a smaller one.
            int nu = math.clamp((int)math.floor((ia * 2f + w) / (30f + w)), 1, 3), nv = math.clamp((int)math.floor((ib * 2f + w) / (30f + w)), 1, 3);
            if (nu == 3 && nv < 3) nu = 2;
            if (nv == 3 && nu < 3) nv = 2;
            if (nu * nv == 1 && ia > 24f && ib > 24f) nu = nv = 2;
            float su = (ia * 2f - (nu - 1) * w) / nu, sv = (ib * 2f - (nv - 1) * w) / nv;
            if (su < 10f || sv < 10f) { Piazza(k); return; }
            bool middle = nu == 3 && nv == 3;
            float rc = middle ? math.min(su, sv) * 0.5f - 1.5f : math.clamp(w * 1.25f, 8f, 14f);
            var lawnRects = new List<OrientedRect>();
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    if (middle && i == 1 && j == 1) continue;
                    float uc = -ia + su * 0.5f + i * (su + w), vc = -ib + sv * 0.5f + j * (sv + w);
                    lawnRects.Add(new OrientedRect { Center = k.W(uc, vc), Axis = k.A, HalfLength = su * 0.5f, HalfWidth = sv * 0.5f });
                }
            k.Ctx.Plan.Facts["railedLawns"] = lawnRects.Count;
            var circus = middle ? PavingShapes.Polygon(k.C, 0.1f, 4) : PavingShapes.Polygon(k.C, rc + 3f, 24, k.Phase);
            foreach (var r in lawnRects)
            {
                float cut = math.min(r.HalfLength, r.HalfWidth) * 0.36f;
                var ring = Chamfer(r, cut);
                // The circus cuts the inner corner of every lawn when they meet in the middle.
                var pieces = nu * nv > 1 && !middle ? k.Pieces(ring, new List<float2[]> { circus }, 60f) : new List<float2[]> { ring };
                foreach (var lawn in pieces)
                {
                    if (!k.Fits(lawn, 0.5f)) continue;
                    k.Lay(PGreen, lawn, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                    k.FenceRing(lawn, 0.4f);
                    var lr = LayerRegion.From(lawn);
                    if (lr == null) continue;
                    // Trees round the inside of the railings, a ring of flowerbeds in the middle of the lawn.
                    var inner = lr.InsetRing(3.4f);
                    var ir = inner != null ? LayerRegion.From(inner) : null;
                    if (ir != null)
                    {
                        int n = math.max(3, (int)math.round(ir.Perimeter / 10f));
                        for (int t = 0; t < n; t++) k.Tree(ir.PointAt(ir.Perimeter * (t + 0.5f) / n, out _, out _, out _), false, null);
                    }
                    var deep = lr.DeepestPoint(out float ins, null, 16);
                    if (ins >= 7f)
                    {
                        float br = math.min(4.2f, ins * 0.35f);
                        for (int f = 0; f < 6; f++)
                        {
                            float a = f * math.PI / 3f;
                            k.Flowerbed(deep + new float2(math.cos(a), math.sin(a)) * br, a);
                        }
                        k.Topiary(deep);
                    }
                    // Benches outside the railings on the long sides, looking at the walk.
                    for (int e = 0; e < lawn.Length; e++)
                    {
                        var a = lawn[e]; var b = lawn[(e + 1) % lawn.Length];
                        float len = math.distance(a, b);
                        if (len < 9f) continue;
                        var d = (b - a) / len;
                        var outward = new float2(d.y, -d.x);   // CCW ring: outside is to the right
                        foreach (var p in LeisureKit.Along(a + d * 2.5f, b - d * 2.5f, 7f)) k.Seat(p + outward * 1.5f, outward);
                        k.Light(a + outward * 1.6f + d * 1.2f);
                    }
                }
            }
            // Dark bands across the whole square on the lawns' edge lines (they show only in the walks).
            var lines = new List<float2[]>();
            float reach = math.length(k.Region.Max - k.Region.Min);
            for (int i = 1; i < nu; i++) lines.Add(LayerRegion.Rect(k.W(-ia + i * (su + w) - w * 0.5f, 0f), k.S, reach, 0.35f));
            for (int j = 1; j < nv; j++) lines.Add(LayerRegion.Rect(k.W(0f, -ib + j * (sv + w) - w * 0.5f), k.A, reach, 0.35f));
            foreach (float s in new[] { -1f, 1f })
            {
                lines.Add(LayerRegion.Rect(k.W(s * (ia + 1.8f), 0f), k.S, reach, 0.35f));
                lines.Add(LayerRegion.Rect(k.W(0f, s * (ib + 1.8f)), k.A, reach, 0.35f));
            }
            k.Lay(PPattern, lines, k.P.Inlay);
            // The circus where the walks meet.
            if (middle)
            {
                // The middle garden: a paved court in the focal stone round the centre, four flower beds on its diagonals.
                var court = k.Box(-su * 0.5f, su * 0.5f, -sv * 0.5f, sv * 0.5f);
                k.Lay(PKerb, PavingShapes.Frame(court.Ring, 1f), k.P.Band);
                k.Lay(PFeature + 1, court.Ring, k.P.Focal);
                k.Lay(PKerb, PavingShapes.Annulus(k.C, rc * 0.55f, 1f), k.P.Band);
                k.Core = rc * 0.55f + 1f;
                if (k.FountainChild) k.CentreClear(k.C, rc * 0.55f);
                else if (k.CentreAvailable) k.CentrePiece(k.C, rc * 0.4f, "fountain");
                else k.FountainDrum(k.C, math.clamp(rc * 0.55f * 0.45f, 2.6f, 4f), false);
                foreach (var (cu, cv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var bc = court.Local(cu * (court.HalfLength - 4.2f), cv * (court.HalfWidth - 4.2f));
                    k.Lay(PFeature - 2, PavingShapes.Polygon(bc, 2.6f, 20), k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                    k.Lay(PFeature - 1, PavingShapes.Annulus(bc, 2.6f, 0.9f, 20), k.P.Band);
                    k.Tree(bc, false, PlazaArt.GardenTrees);
                    k.Seat(bc + math.normalizesafe(k.C - bc) * 4.4f, math.normalizesafe(k.C - bc));
                }
                k.SeatRing(k.C, rc * 0.55f + 2.6f, 8, true);
                k.LampRing(k.C, rc * 0.55f + 4.4f, 4);
            }
            else if (nu * nv > 1)
            {
                k.Lay(PFeature, PavingShapes.Polygon(k.C, rc, 32, k.Phase), k.P.Focal);
                k.Lay(PKerb, PavingShapes.Annulus(k.C, rc, 1.2f), k.P.Band);
                k.Core = rc + 1.2f;
                if (k.FountainChild) k.CentreClear(k.C, rc);
                else if (k.CentreAvailable) k.CentrePiece(k.C, rc - 2f, "fountain");
                else k.FountainDrum(k.C, math.clamp(rc * 0.4f, 2.6f, 4.2f), false);
                k.SeatRing(k.C, math.max(rc - 1.6f, 7f), 8, true);
                k.LampRing(k.C, rc + 0.6f, 8);
                // Kiosks on the circus's diagonals, between the walks.
                for (int q = 0; q < 4; q++)
                {
                    float an = k.Phase + math.PI * 0.25f + q * math.PI * 0.5f;
                    var kp = k.C + new float2(math.cos(an), math.sin(an)) * (rc + 4.5f);
                    k.Stall(kp, math.normalizesafe(k.C - kp), true);
                }
            }
        }

        private static float2[] Chamfer(OrientedRect r, float c)
        {
            float a = r.HalfLength, b = r.HalfWidth;
            return new[]
            {
                r.Local(-a + c, -b), r.Local(a - c, -b), r.Local(a, -b + c), r.Local(a, b - c),
                r.Local(a - c, b), r.Local(-a + c, b), r.Local(-a, b - c), r.Local(-a, -b + c),
            };
        }

        // ───────────── terraced garden ─────────────

        private static void Terraces(Kit k)
        {
            float fw = k.Fw;
            float prom = 5f;
            k.Leftover(fw + prom + 1f, "hedge");
            k.WallId = "EU_Wall03";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            k.RingTrees(fw + 1.5f, 9f, true, 0, true);
            float ia = k.HA - fw - prom - 0.5f, ib = k.HB - fw - prom - 0.5f;
            // 2026-10-29: crisp steps (the user: "the levels read as gentle slopes"): 1.8 m a step on a 45 % bank, a
            // stone wall along every foot, a hedge along every top, stone stairs with cheek walls on the axes.
            float h = math.clamp(k.Ctx.F("levelHeight"), 0.9f, 2.4f);
            float bank = BankFor(h);
            int levels = math.clamp(k.Ctx.I("levelCount"), 2, 3);
            float m = math.min(ia, ib);
            float floor = (m - 12f - (levels - 1) * bank) / (levels - 1);
            while (levels > 2 && floor < 7f) { levels--; floor = (m - 12f - (levels - 1) * bank) / (levels - 1); }
            if (floor < 5f) { k.Ctx.Issue(IssueSeverity.Info, "Placemaker.Plaza.Issue.TooSmallForLevels"); Rooms(k); return; }
            floor = math.min(floor, 14f);
            k.Ctx.Plan.Facts["terraceLevels"] = levels;
            var foot = new float[levels]; var head = new float[levels];
            float d = 0f;
            for (int l = 1; l < levels; l++) { d += floor; foot[l] = d; d += bank; head[l] = d; }
            OrientedRect At(float inset) => new OrientedRect { Center = k.C, Axis = k.A, HalfLength = ia - inset, HalfWidth = ib - inset };
            var lv = new Levels(k, ia, ib);
            for (int l = 1; l < levels; l++) lv.Add(-ia + foot[l], ia - foot[l], -ib + foot[l], ib - foot[l], h, bank);
            lv.Emit();

            // Banks: planted, a wall at the foot, a hedge on top, stairs in the middle of every side.
            const float stair = 4.6f;
            for (int l = 1; l < levels; l++)
            {
                float u0 = -ia + foot[l], u1 = ia - foot[l], v0 = -ib + foot[l], v1 = ib - foot[l];
                var gaps = new List<Gap> { new Gap(0, 0f, stair * 0.5f), new Gap(1, 0f, stair * 0.5f), new Gap(2, 0f, stair * 0.5f), new Gap(3, 0f, stair * 0.5f) };
                Edges(k, u0, u1, v0, v1, B4(bank), false, Head.Hedge, gaps);
                k.Flight(k.W(u0 - 0.3f, 0f), k.W(u0 + bank + 0.3f, 0f), stair);
                k.Flight(k.W(u1 + 0.3f, 0f), k.W(u1 - bank - 0.3f, 0f), stair);
                k.Flight(k.W(0f, v0 - 0.3f), k.W(0f, v0 + bank + 0.3f), stair);
                k.Flight(k.W(0f, v1 + 0.3f), k.W(0f, v1 - bank - 0.3f), stair);
                // Flowers and clipped shrubs on the slope either side of the stairs.
                var oring = At(foot[l]).Ring; var iring = At(head[l]).Ring;
                for (int e = 0; e < 4; e++)
                {
                    int f = (e + 1) % 4;
                    var slopeMid = math.lerp(oring[e], iring[e], 0.55f);
                    var slopeEnd = math.lerp(oring[f], iring[f], 0.55f);
                    float sl = math.distance(slopeMid, slopeEnd);
                    var sd = (slopeEnd - slopeMid) / math.max(1e-3f, sl);
                    int fi = 0;
                    for (float s = 3f; s < sl - 3f; s += 3.2f, fi++)
                        if (math.abs(s - sl * 0.5f) > stair * 0.5f + 1.6f)
                        {
                            if ((fi % 3) == 2) k.Topiary(slopeMid + sd * s);
                            else k.Flowerbed(slopeMid + sd * s, math.atan2(sd.x, sd.y));
                        }
                }
            }
            // Floors: benches on each terrace looking down over the one below, lime trees along its inner edge.
            for (int l = 1; l < levels; l++)
            {
                var i = At(head[l]);
                float to = l + 1 < levels ? foot[l + 1] : math.min(i.HalfLength, i.HalfWidth);
                float fl = to - head[l];
                if (fl < 4f) continue;
                var ring = i.Ring;
                for (int e = 0; e < 4; e++)
                {
                    int f = (e + 1) % 4;
                    var a = ring[e]; var b = ring[f];
                    float len = math.distance(a, b);
                    var dd = (b - a) / len;
                    var inward = new float2(-dd.y, dd.x);
                    foreach (var p in LeisureKit.Along(a + dd * 4f, b - dd * 4f, 6.5f))
                    {
                        if (math.abs(math.dot(p - (a + b) * 0.5f, dd)) < stair * 0.5f + 2f) continue;
                        k.Seat(p + inward * 2.2f, -inward);
                    }
                    if (l + 1 < levels && fl >= 9f)
                        foreach (var p in LeisureKit.Along(a + dd * 3f, b - dd * 3f, 9f))
                        {
                            if (math.abs(math.dot(p - (a + b) * 0.5f, dd)) < stair * 0.5f + 3f) continue;
                            k.Tree(p + inward * (fl - 2.6f), true, PlazaArt.LimeTrees);
                        }
                }
            }
            // At the foot of the first bank: a flower border in front of the wall, open at the stairs, benches looking up at it.
            {
                var ring = At(foot[1]).Ring;
                for (int e = 0; e < 4; e++)
                {
                    var a = ring[e]; var b = ring[(e + 1) % 4];
                    float len = math.distance(a, b);
                    var dd = (b - a) / len;
                    var outward = new float2(dd.y, -dd.x);
                    float mid = len * 0.5f;
                    foreach (var (s0, s1) in new[] { (2f, mid - stair * 0.5f - 0.8f), (mid + stair * 0.5f + 0.8f, len - 2f) })
                    {
                        if (s1 - s0 < 4f) continue;
                        var p0 = a + dd * s0 + outward * 1.7f; var p1 = a + dd * s1 + outward * 1.7f;
                        k.Lay(PFeature, PavingShapes.Band(p0, p1, 2.4f), k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                        foreach (var p in LeisureKit.Along(p0, p1, 2.8f)) k.Flowerbed(p, math.atan2(dd.x, dd.y));
                        k.Occ.TakeCorridor(p0, p1, 1.3f, 3);
                        foreach (var p in LeisureKit.Along(p0, p1, 9f)) k.Seat(p + outward * 3.2f, -outward);
                    }
                }
            }
            // The belvedere on top: a paved rondel round a pavilion (or the focal piece), lawns between the stair axes.
            var top = At(head[levels - 1]);
            float rad = math.min(math.min(top.HalfLength, top.HalfWidth) - 2f, 10f);
            k.Lay(PFeature, PavingShapes.Polygon(k.C, rad, 32, k.Phase), k.P.Focal);
            k.Lay(PKerb, PavingShapes.Annulus(k.C, rad, 1f), k.P.Band);
            k.Core = rad + 1f;
            if (top.HalfLength - 2f > stair * 0.5f + 6f && top.HalfWidth - 2f > stair * 0.5f + 6f)
                foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    float u0 = stair * 0.5f + 1.2f, u1 = top.HalfLength - 2.2f, v0 = stair * 0.5f + 1.2f, v1 = top.HalfWidth - 2.2f;
                    var lawn = k.Box(su * u0, su * u1, sv * v0, sv * v1);
                    var pieces = k.Pieces(lawn.Ring, new List<float2[]> { PavingShapes.Polygon(k.C, rad + 2.2f, 32, k.Phase) }, 20f);
                    foreach (var pc in pieces) k.Lay(PGreen, pc, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                    k.Tree(k.W(su * (u1 - 2.4f), sv * (v1 - 2.4f)), false, PlazaArt.GardenTrees);
                    k.Flowerbed(k.W(su * (u1 - 2.4f), sv * (v0 + 2f)), 0f);
                    k.Flowerbed(k.W(su * (u0 + 2f), sv * (v1 - 2.4f)), 0f);
                }
            if (k.FountainChild) k.CentreClear(k.C, rad);
            else if (k.CentreAvailable) k.CentrePiece(k.C, rad * 0.5f, "fountain");
            else if (!k.Gazebo(k.C, k.Phase)) k.FountainDrum(k.C, math.min(rad * 0.4f, 4f), false);
            if (PlazaArt.Available(k.Ctx, "shelterAsset", AssetCategories.Shelter))
                foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var p = top.Local(su * (top.HalfLength - 3.2f), sv * (top.HalfWidth - 3.2f));
                    if (k.Region.ContainsDisc(p, 3f) && math.distance(p, k.C) > rad + 3f)
                        LeisureKit.Put(k.Ctx, k.Occ, p, LeisureKit.Yaw(p, k.C), ObjectRole.Furniture, PlazaArt.Or(k.Ctx, "shelterAsset", PlazaArt.Pergolas, k.V(0x5A00u)), AssetCategories.Shelter, 2.4f, k.V(0x5A10u), 0.5f, 0.2f, 0);
                }
            k.SeatRing(k.C, math.max(rad - 1.4f, 5.5f), 6, false);
        }

        private static float3[] Lift(float2[] ring, float h)
        {
            var r = new float3[ring.Length];
            for (int i = 0; i < ring.Length; i++) r[i] = new float3(ring[i].x, h, ring[i].y);
            return r;
        }

        // ───────────── modern ─────────────

        private static void Modern(Kit k)
        {
            float fw = k.Fw;
            k.Leftover(fw + 3f, "bosque");
            k.WallId = "EU_Wall05";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            float ia = k.HA - fw - 3f, ib = k.HB - fw - 3f;
            if (ia < 16f || ib < 14f) { Piazza(k); return; }
            // Dark stone bands right across the square every 7.5 m (they show between everything else).
            var bands = new List<float2[]>();
            float reach = math.length(k.Region.Max - k.Region.Min);
            for (float u = 3.75f; u < reach; u += 7.5f)
                foreach (float s in new[] { -1f, 1f })
                    bands.Add(LayerRegion.Rect(k.W(s * u, 0f), k.S, reach, 0.3f));
            k.Lay(PPattern, bands, k.P.Inlay);

            // 2026-10-29 (the user: "its tree grids are too dense", "a plain grey pad"): linear planters either side of
            // an open promenade, one row of trees in pits beyond them with cafe tables between, a long pergola canopy
            // on one side, a sunken water court with a fountain wall in the middle, tall flags at the promenade's ends.
            float vp = math.clamp(ib * 0.3f, 8f, 15f);
            const float pw = 2.6f, gap = 10f;
            float end = ia * 0.9f;
            foreach (float s in new[] { -1f, 1f })
            {
                Planter(k, -end, -gap * 0.5f, s * vp, pw);
                Planter(k, gap * 0.5f, end, s * vp, pw);
            }
            float vr = vp + pw * 0.5f + 6f;
            int strata = 1;
            if (vr <= ib - 3f)
            {
                strata++;
                foreach (float s in new[] { -1f, 1f })
                {
                    int t = 0;
                    for (float u = -end + 2f; u <= end - 2f + 0.01f; u += 9f, t++)
                    {
                        var p = k.W(u, s * vr);
                        if (!k.Region.ContainsDisc(p, fw + 2.5f) || math.abs(u) < gap * 0.5f + 2f) continue;
                        k.Tree(p, true, PlazaArt.PlaneTrees);
                        if ((t & 1) == 0) k.CafeSet(p + k.A * 4.5f, k.Phase + 0.4f, false, true);
                        else k.Seat(p - k.S * (s * 2.4f), -k.S * s, PlazaArt.ModernBenches);
                    }
                }
            }
            // The canopy: two bays deep along the +S side beyond the trees, benches under it looking at the square.
            float vc = vr + 4.5f;
            if (vc + 6.5f <= ib)
            {
                strata++;
                float cl = math.min(end * 0.62f, 36f);
                var a = k.W(cl, vc + 6f); var b = k.W(-cl, vc + 6f);
                foreach (var p in LeisureKit.Along(k.W(-cl + 1.5f, vc + 1.6f), k.W(cl - 1.5f, vc + 1.6f), 3f)) k.Seat(p, -k.S, PlazaArt.ModernBenches);
                foreach (var p in LeisureKit.Along(k.W(-cl + 3f, vc + 4.4f), k.W(cl - 3f, vc + 4.4f), 6f)) k.CafeSet(p, k.Phase, false, true);
                var strip = k.Canopy(a, b, 2, "PergolaModular04", true);
                LeisureKit.TakeRect(k.Occ, strip);
                k.Lay(PFeature + 2, strip.Ring, k.P.Focal);
                // The -S side: the same canopy, cafe tables under it and in front of it, planters along the edge.
                foreach (var p in LeisureKit.Along(k.W(-cl + 1.5f, -(vc + 1.6f)), k.W(cl - 1.5f, -(vc + 1.6f)), 3f)) k.Seat(p, k.S, PlazaArt.ModernBenches);
                foreach (var p in LeisureKit.Along(k.W(-cl + 3f, -(vc + 4.4f)), k.W(cl - 3f, -(vc + 4.4f)), 6f)) k.CafeSet(p, k.Phase, false, true);
                var s2 = k.Canopy(k.W(-cl, -(vc + 6f)), k.W(cl, -(vc + 6f)), 2, "PergolaModular04", true);
                LeisureKit.TakeRect(k.Occ, s2);
                k.Lay(PFeature + 2, s2.Ring, k.P.Focal);
                foreach (float sd in new[] { -1f, 1f })
                    foreach (var p in LeisureKit.Along(k.W(-cl - 8f, sd * (vc + 3f)), k.W(-end, sd * (vc + 3f)), 4.5f)) k.BoxPlanterRow(p, p + k.A * 0.1f, 1f);
            }
            // Beyond the canopies to the edge: a loose grove (trees in pits on a 10 m grid), raised beds with benches between.
            for (float v = vc + 12f; v <= ib - 4f; v += 10f)
            {
                strata++;
                foreach (float s in new[] { -1f, 1f })
                {
                    int t = 0;
                    for (float u = -end + 3f; u <= end - 3f + 0.01f; u += 10f, t++)
                    {
                        var p = k.W(u + (((int)(v / 10f)) & 1) * 5f, s * v);
                        if (!k.Region.ContainsDisc(p, fw + 3f)) continue;
                        if ((t & 1) == 0) k.Tree(p, true, PlazaArt.PlaneTrees);
                        else
                        {
                            k.RaisedBeds(p - k.A * 3f, p + k.A * 3f);
                            k.Seat(p - k.S * (s * 2f), -k.S * s, PlazaArt.ModernBenches);
                        }
                    }
                }
            }
            k.Ctx.Plan.Facts["modernStrata"] = strata;
            ModernCentre(k, ia, vp, pw, gap, end);
        }

        /// <summary>A linear planter on u0..u1 at v: lawn strip in a dark kerb, trees every 12 m, low hedges between, benches along both sides.</summary>
        private static void Planter(Kit k, float a0, float a1, float v, float pw)
        {
            var a = k.W(a0, v); var b = k.W(a1, v);
            var strip = new OrientedRect { Center = (a + b) * 0.5f, Axis = k.A, HalfLength = (a1 - a0) * 0.5f, HalfWidth = pw * 0.5f };
            if (!k.Fits(strip, 1f)) return;
            k.Lay(PFeature, strip.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
            k.Lay(PKerb, PavingShapes.Frame(new OrientedRect { Center = strip.Center, Axis = k.A, HalfLength = strip.HalfLength + 0.6f, HalfWidth = strip.HalfWidth + 0.6f }.Ring, 0.6f), k.P.Band);
            var trees = LeisureKit.Along(a, b, 12f);
            for (int t = 0; t < trees.Count; t++)
            {
                k.Tree(trees[t], false, PlazaArt.LimeTrees);
                var next = t + 1 < trees.Count ? trees[t + 1] : b - k.A * 0.5f;
                var from = trees[t] + k.A * 1.4f; var to = next - k.A * 1.4f;
                if (math.dot(to - from, k.A) > 2f) k.Hedge(from, to);
                if (t == 0 && math.dot(trees[0] - a, k.A) > 3f) k.Hedge(a + k.A * 0.6f, trees[0] - k.A * 1.4f);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                var off = k.S * (side * (pw * 0.5f + 1.6f));
                foreach (var p in LeisureKit.Along(a, b, 4.5f))
                {
                    bool atTree = false;
                    foreach (var t in trees) if (math.distancesq(t, p) < 4f) { atTree = true; break; }
                    if (!atTree) k.Seat(p + off, k.S * side, PlazaArt.ModernBenches);
                }
            }
            k.Light(a - k.A * 1.4f);
            k.Light(b + k.A * 1.4f);
        }

        private static void ModernCentre(Kit k, float ia, float vp, float pw, float gap, float end)
        {
            // A sunken water court 0.9 m deep: a stone bank and a wall round a dark mirror, a fountain wall down its
            // middle (jets when the player has them), benches on the rim looking in.
            float pu = math.min(gap * 0.5f + 7f, ia * 0.35f), pv = math.max(4.5f, vp - pw * 0.5f - 3.5f);
            float b = BankFor(WallH);
            var lv = new Levels(k, k.EA - 1f, k.EB - 1f);
            lv.Add(-pu, pu, -pv, pv, -WallH, b);
            lv.Emit();
            Edges(k, -pu, pu, -pv, pv, B4(b), true, Head.None, null, k.P.Band);
            var mirror = k.Box(-pu + b, pu - b, -pv + b, pv - b);
            k.Lay(PFeature, mirror.Ring, k.P.Inlay);
            k.Lay(PKerb, PavingShapes.Frame(k.Box(-pu - 0.8f, pu + 0.8f, -pv - 0.8f, pv + 0.8f).Ring, 0.8f), k.P.Band);
            k.Core = math.min(pu, pv);
            if (k.FountainChild) k.CentreClear(k.C, math.min(pu, pv));
            else if (k.JetsAvailable)
            {
                for (float u = -pu + 4f; u <= pu - 4f + 0.01f; u += 4f)
                    foreach (float v in new[] { -pv * 0.4f, pv * 0.4f })
                        k.Ctx.Object(k.W(u, v), 0f, ObjectRole.Centerpiece, PlazaArt.SmallFountain, k.V(0x5E10u), -1, AssetCategories.CenterpieceFountain);
            }
            else if (pu - b > 4f) k.FountainWall(k.W(-pu + b + 2f, 0f), k.W(pu - b - 2f, 0f));
            LeisureKit.TakeRect(k.Occ, k.Box(-pu, pu, -pv, pv), 0);
            foreach (float s in new[] { -1f, 1f })
            {
                foreach (var p in LeisureKit.Along(k.W(-pu + 1.5f, s * (pv + 2.2f)), k.W(pu - 1.5f, s * (pv + 2.2f)), 3.6f)) k.Seat(p, -k.S * s, PlazaArt.ModernBenches);
                foreach (float t in new[] { -1f, 1f }) k.Light(k.W(s * (pu + 1.6f), t * (pv + 1.6f)));
                // Tall flags in pairs at both ends of the promenade.
                foreach (float t in new[] { -1f, 1f }) k.BigFlag(k.W(s * (end - 1f), t * 3f), true);
            }
            if (k.CentreAvailable)
                foreach (float s in new[] { -1f, 1f }) k.CentrePiece(k.W(s * (end - 6f), 0f), 3f, "statue");
        }

        // ───────────── piazza ─────────────

        private static void Piazza(Kit k)
        {
            float fw = k.Fw;
            float m = math.min(k.HA, k.HB);
            k.Leftover(fw + 3f, "bosque");
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            float r0 = math.clamp(m * 0.15f, 5f, 11f);
            k.Lay(PFeature, PavingShapes.Polygon(k.C, r0, 40, k.Phase), k.P.Focal);
            k.Lay(PKerb, PavingShapes.Annulus(k.C, r0, 1.2f), k.P.Band);
            // Rings and rays of dark stone round the fountain: the compass of an old-town square.
            float reach = math.length(k.Region.Max - k.Region.Min);
            // A rosette round the fountain's kerb: sixteen sectors in alternating stone out to the first ring.
            float rose = r0 + 1.2f + math.clamp(m * 0.09f, 4f, 7f);
            var sectors = new List<float2[]>();
            for (int i = 0; i < 16; i += 2)
            {
                float a0 = k.Phase + i * math.PI / 8f, a1 = a0 + math.PI / 8f;
                var pts = new List<float2>();
                for (int s = 0; s <= 4; s++) { float a = math.lerp(a0, a1, s / 4f); pts.Add(k.C + new float2(math.cos(a), math.sin(a)) * rose); }
                for (int s = 4; s >= 0; s--) { float a = math.lerp(a0, a1, s / 4f); pts.Add(k.C + new float2(math.cos(a), math.sin(a)) * (r0 + 1.2f)); }
                sectors.Add(pts.ToArray());
            }
            k.Lay(PFeature + 2, sectors, k.P.Path);
            int ring = 0;
            for (float r = rose; r < reach * 0.6f; r += ring < 3 ? 4.5f : 7f, ring++)
                k.Lay(PPattern, PavingShapes.Annulus(k.C, r, (ring & 1) == 0 ? 1.0f : 0.7f, 64), (ring & 1) == 0 ? k.P.Band : k.P.Inlay);
            var rays = new List<float2[]>();
            for (int i = 0; i < 16; i++)
            {
                float a = k.Phase + i * math.PI / 8f;
                var d = new float2(math.cos(a), math.sin(a));
                // Main rays (on the axes and diagonals) run to the edge; the others stop at the rosette's third ring.
                float to = (i & 1) == 0 ? reach : rose + 13.5f;
                rays.Add(PavingShapes.Band(k.C + d * (r0 + 1.2f), k.C + d * to, (i & 1) == 0 ? 0.9f : 0.6f));
            }
            k.Lay(PPattern + 1, rays, k.P.Inlay);
            k.Core = r0 + 1.2f;
            // 2026-10-29 (the user: "piazza: sparse"): a fountain in the middle (a drum of wall fountains when none is
            // installed), cafe terraces under parasols in front of a pergola loggia along both long sides, a raised
            // church forecourt (sagrato) at one end with a wide flight of steps, kiosks in the far corners.
            float ea = k.EA - 1f, eb = k.EB - 1f;
            bool alongA = k.HA >= k.HB;
            float sag0 = float.MaxValue;
            if (alongA && ea - rose > 30f && eb > 16f)
            {
                float sd = math.min(15f, ea * 0.24f), b = BankFor(WallH);
                float u1 = ea, u0 = ea - sd, v1 = eb - 2f;
                var lv = new Levels(k, ea, eb);
                lv.Add(u0, u1, -v1, v1, WallH, B4(b));
                lv.Emit();
                float w = v1 * 0.55f;
                k.WallId = "EU_Wall03";
                Edges(k, u0, u1, -v1, v1, B4(b), false, Head.Rail, new List<Gap> { new Gap(0, 0f, w) }, k.P.Band);
                k.Flight(k.W(u0 - 0.3f, 0f), k.W(u0 + b + 0.3f, 0f), w * 2f - 0.6f, true, true, k.P.Accent);
                k.Lay(PFeature + 3, k.Box(u0 + b, u1 - b, -v1 + b, v1 - b).Ring, k.P.Focal);
                foreach (var p in LeisureKit.Along(k.W(u1 - b - 3.8f, -v1 + b + 4f), k.W(u1 - b - 3.8f, v1 - b - 4f), 8f)) k.Tree(p, true, PlazaArt.ChestnutTrees);
                k.CafeGrid(u0 + b + 1.6f, u1 - b - 6.4f, -v1 + b + 1.6f, v1 - b - 1.6f, 5.2f, true);
                foreach (float s in new[] { -1f, 1f }) k.BigFlag(k.W(u1 - b - 1f, s * (v1 - b - 1f)), true);
                sag0 = u0 - 1f;
                k.Ctx.Plan.Facts["sagrato"] = 1;
            }
            if (k.FountainChild && k.CentreAvailable) k.CentreClear(k.C, r0);
            else if (k.CentreAvailable) k.Centre(k.C, r0 - 0.5f, "fountain");
            else k.FountainDrum(k.C, math.clamp(r0 * 0.48f, 2.6f, 5.2f), false);
            k.SeatRing(k.C, r0 + 2.4f, 8, true);
            k.LampRing(k.C, r0 + 4.6f, 8);
            // Flower pots on the rosette's rim, between the main rays.
            for (int i = 0; i < 8; i++)
            {
                float a = k.Phase + i * math.PI / 4f + math.PI / 8f;
                k.Pot(k.C + new float2(math.cos(a), math.sin(a)) * (rose - 1f));
            }
            // Chestnuts in the corners: three in pits round a bench.
            foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                // (2026-10-29: from the box's real edge, EA/EB; HA/HB include the frame margin.)
                float cu = math.max(k.EA - 11f, k.EA * 0.6f), cv = math.max(k.EB - 11f, k.EB * 0.6f);
                var c = k.W(su * cu, sv * cv);
                if (!k.Region.ContainsDisc(c, fw + 5f) || math.distance(c, k.C) < r0 + 9f) continue;
                if (alongA && su > 0f && cu > sag0 - 5f) continue;
                for (int t = 0; t < 3; t++)
                {
                    float a = t * 2.094f + math.atan2(sv, su);
                    k.Tree(c + new float2(math.cos(a), math.sin(a)) * 3.6f, true, PlazaArt.ChestnutTrees);
                }
                k.Seat(c, math.normalizesafe(k.C - c));
            }
            CafeRows(k, ea, eb, alongA ? math.min(ea, sag0) : ea);
            foreach (float s in new[] { -1f, 1f })
            {
                var p = alongA ? k.W(-ea + 4f, s * (eb - 5f)) : k.W(s * (ea - 5f), -eb + 4f);
                k.Stall(p, math.normalizesafe(k.C - p), true);
            }
            PlazaDressing.EdgePlanters(k.Ctx, k.Occ, k.Region, k.Gates);
        }

        /// <summary>
        /// Cafe terraces along the square's two long sides: a pergola loggia one bay deep against the edge, two rows of
        /// tables under big parasols in front of it, pots at the row ends. <paramref name="stop"/> ends the rows (a forecourt).
        /// </summary>
        private static void CafeRows(Kit k, float ea, float eb, float stop)
        {
            if (k.Ctx.I("cafe") <= 0) return;
            bool alongA = k.HA >= k.HB;
            var dir = alongA ? k.A : k.S;
            var nrm = alongA ? k.S : k.A;
            float half = alongA ? ea : eb, across = alongA ? eb : ea;
            float t0 = -half * 0.72f, t1 = math.min(half * 0.72f, stop - 3f);
            if (t1 - t0 < 12f) return;
            int placed = 0;
            foreach (float s in new[] { -1f, 1f })
            {
                // The loggia: its line just inside the edge, the bays reaching in towards the square.
                var la = k.C + dir * t0 + nrm * (s * (across - 0.4f));
                var lb = k.C + dir * t1 + nrm * (s * (across - 0.4f));
                // The bays reach to the left of la to lb: turn the line so that is towards the square.
                var dl = lb - la;
                if (math.dot(new float2(-dl.y, dl.x), k.C - la) < 0f) { var tmp = la; la = lb; lb = tmp; }
                foreach (var p in LeisureKit.Along(la - nrm * (s * 1.5f), lb - nrm * (s * 1.5f), 4f)) k.CafeSet(p, math.atan2(dir.x, dir.y), false, true);
                var strip = k.Canopy(la, lb, 1, "PergolaModular05", true);
                LeisureKit.TakeRect(k.Occ, strip);
                foreach (float row in new[] { 5.6f, 11f })
                {
                    float off = across - 0.4f - 3f - row + 2.4f;
                    var a = k.C + dir * (t0 + 2f) + nrm * (s * off); var b = k.C + dir * (t1 - 2f) + nrm * (s * off);
                    foreach (var p in LeisureKit.Along(a, b, 5.2f)) if (k.CafeSet(p, math.atan2(dir.x, dir.y) + row * 0.1f)) placed++;
                }
                foreach (float e in new[] { -1f, 1f }) k.Pot(k.C + dir * (e > 0 ? t1 - 0.6f : t0 + 0.6f) + nrm * (s * (across - 6f)));
            }
            k.Ctx.Plan.Facts["cafeRows"] = placed;
        }
        // ───────────── kit ─────────────

        internal sealed partial class Kit
        {
            public readonly LayerContext Ctx;
            public readonly Occupancy Occ;
            public readonly LayerRegion Region;
            public readonly List<Gate> Gates;
            public readonly PavingPalette P;
            public readonly PavingPlan Pave;
            public float2 A, S;
            public float2 C;   // (round 3) moved by the transit forecourt toward its street on a big site
            /// <summary>Half sizes of the box (on A and S) round C the composition is laid in.</summary>
            public float HA, HB;
            /// <summary>Half sizes of the box to its real edge on the outline (HA/HB reach the frame margin past it).</summary>
            public float EA => HA - Fw - 4f;
            public float EB => HB - Fw - 4f;
            public readonly float Fw;
            public float Phase;
            public readonly bool FountainChild, CentreAvailable, JetsAvailable;
            public readonly AssetRef Bench, Lamp, Grass, HedgeAsset, FenceAsset;
            public readonly string[] Trees;
            public float Core;
            public int TreeCount, BenchCount, HedgeCount, FenceCount;
            private int _n;
            private readonly List<(int prio, int order, List<float2[]> rings, SurfaceRole role, AssetRef asset, string cat)> _lay = new List<(int, int, List<float2[]>, SurfaceRole, AssetRef, string)>();
            private readonly List<float2> _pits = new List<float2>();

            public Kit(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, PavingPalette p, PavingPlan pave, float2 axis, bool fountainChild)
            {
                Ctx = ctx; Occ = occ; Region = region; Gates = gates; P = p; Pave = pave;
                A = math.normalizesafe(axis, new float2(1f, 0f)); S = new float2(-A.y, A.x);
                Phase = math.atan2(A.y, A.x);
                Fw = PlazaDressing.FrameWidth(region);
                FountainChild = fountainChild;
                // The box: the largest rectangle on the square's axes whose inner part (past the frame) lies inside the
                // outline — the whole block on a rectangle, one arm of an L, the fat part of a triangle. What it leaves
                // is planted by FillLeftover.
                BestBox(region, A, S, Fw + 4f, out C, out HA, out HB);
                ctx.Plan.Facts["designBox"] = (float)Math.Round(HA * 2f) * 1000f + (float)Math.Round(HB * 2f);
                bool modern = p.Id == GroundPalettes.Modern;
                Bench = PlazaArt.Or(ctx, "benchAsset", modern ? PlazaArt.ModernBenches : PlazaArt.Benches, 0);
                Lamp = PlazaArt.Or(ctx, "lampAsset", modern ? PlazaArt.ModernLamp : PlazaArt.ParkLamp);
                Grass = new AssetRef("", PavingPalettes.VGrass);
                HedgeAsset = PlazaArt.Or(ctx, "planterAsset", PlazaArt.LowHedge);
                FenceAsset = AssetMix.One(ctx.S("fenceAsset"), "EU_FenceResidentialLow05", "EU_FenceResidentialLow01", "FenceResidentialLow02");
                Trees = string.IsNullOrEmpty(ctx.S("treeAsset")) ? PlazaLayer.TreeSpecies(p) : new[] { ctx.S("treeAsset") };
                CentreAvailable = !string.IsNullOrEmpty(ctx.S("centerpiece")) || AssetAvailability.Has(AssetCategories.Centerpiece);
                JetsAvailable = !string.IsNullOrEmpty(ctx.S("jetAsset")) || AssetAvailability.Has(AssetCategories.CenterpieceFountain);
            }

            /// <summary>
            /// The largest axis-aligned (on A/S) rectangle whose part inside <paramref name="margin"/> lies in the region:
            /// grown from the frame's middle, the focal point and a grid of candidates; the middle wins ties (a
            /// rectangle keeps its symmetric box). Returns its centre and half sizes (margin included).
            /// </summary>
            internal static void BestBox(LayerRegion region, float2 a, float2 s, float margin, out float2 centre, out float ha, out float hb)
            {
                var focal = LeisureKit.FocalPoint(region, out _);
                PlazaDressing.Extents(region, focal, a, out float minA, out float maxA, out float minB, out float maxB);
                var cands = new List<float2> { focal + a * ((minA + maxA) * 0.5f) + s * ((minB + maxB) * 0.5f), focal };
                const int g = 7;
                for (int i = 0; i < g; i++)
                    for (int j = 0; j < g; j++)
                        cands.Add(focal + a * math.lerp(minA, maxA, (i + 0.5f) / g) + s * math.lerp(minB, maxB, (j + 0.5f) / g));
                bool Fits(float2 c, float u, float v)
                {
                    var ring = new OrientedRect { Center = c, Axis = a, HalfLength = u, HalfWidth = v }.Ring;
                    foreach (var q in ring) if (!region.Contains(q)) return false;
                    for (int i = 0; i < 4; i++) if (region.CrossesBoundary(ring[i], ring[(i + 1) % 4], false)) return false;
                    return true;
                }
                centre = focal; ha = hb = margin + 2f;
                float best = -1f;
                foreach (var c in cands)
                {
                    if (!region.Contains(c) || !Fits(c, 1f, 1f)) continue;
                    float u = 1f, v = 1f;
                    foreach (float step in new[] { 4f, 1f, 0.25f })
                        for (int it = 0; it < 400; it++)
                        {
                            bool gu = Fits(c, u + step, v), gv = Fits(c, u, v + step);
                            if (gu) u += step;
                            if (gv) v += step;
                            if (!gu && !gv) break;
                        }
                    float area = u * v;
                    if (area > best * 1.03f) { best = area; centre = c; ha = u + margin; hb = v + margin; }
                }
            }

            /// <summary>
            /// The ground the box leaves (one arm of an L, a triangle's corners): each piece deep enough becomes a planted
            /// garden in the design's manner — a lawn behind railings or hedges with trees on a loose lattice and benches
            /// looking out, or (modern, piazza) a bosque of trees in pits.
            /// </summary>
            public void FillLeftover(float edgeInset, string manner)
            {
                var inner = Region.InsetRing(edgeInset);
                if (inner == null) return;
                var box = new OrientedRect { Center = C, Axis = A, HalfLength = HA - edgeInset + 3f, HalfWidth = HB - edgeInset + 3f };
                var pieces = Pieces(inner, new List<float2[]> { box.Ring }, 150f);
                int made = 0;
                foreach (var piece in pieces)
                {
                    var lr = LayerRegion.From(piece);
                    if (lr == null) continue;
                    var deep = lr.DeepestPoint(out float ins, null, 16);
                    if (ins < 5f) continue;
                    if (manner == "bosque")
                    {
                        // 2026-10-29 (the user: "the leftover arm is a plain lawn with a grid of trees"): a cafe grove,
                        // trees on a looser grid with small tables between them, a kiosk where it is deepest.
                        // A paved court round a fountain where the piece is deepest, cafe tables under parasols round it.
                        float keepR = 0f;
                        if (ins >= 12f)
                        {
                            Lay(PFeature + 3, PavingShapes.Polygon(deep, 9f, 32, Phase), P.Focal);
                            Lay(PKerb, PavingShapes.Annulus(deep, 9f, 0.8f, 32), P.Band);
                            keepR = Centre(deep, 6f, "fountain");
                            SeatRing(deep, keepR + 1f, 6, true);
                            for (int q = 0; q < 6; q++)
                            {
                                float an = Phase + q * math.PI / 3f + 0.5f;
                                CafeSet(deep + new float2(math.cos(an), math.sin(an)) * (keepR + 5.5f), an, true);
                            }
                            keepR += 9f;
                        }
                        else if (ins >= 7f) Stall(deep, math.normalizesafe(C - deep), true);
                        var lrMin = lr.Min; var lrMax = lr.Max;
                        int gi = 0;
                        for (float x = lrMin.x + 6f; x < lrMax.x; x += 12f)
                            for (float y = lrMin.y + 6f; y < lrMax.y; y += 12f, gi++)
                            {
                                var p = new float2(x, y);
                                if (!lr.ContainsDisc(p, 3f) || math.distance(p, deep) < keepR) continue;
                                Tree(p, true, PlazaArt.PlaneTrees);
                                var q2 = p + new float2(6f, 6f);
                                if ((gi & 1) == 0 && lr.ContainsDisc(q2, 3f) && math.distance(q2, deep) >= keepR) CafeSet(q2, Phase + 0.4f, false, true);
                            }
                        made++;
                        continue;
                    }
                    if (manner == "wild")
                    {
                        // (the grungy square) the corner nobody keeps: rough grass, wild and dead bushes, a self-sown tree or two
                        foreach (var rough in Poly.Offset(piece, 1.5f))
                        {
                            if (rough == null || rough.Length < 3 || Poly.Area(rough) < 60f) continue;
                            Lay(PGreen, rough, Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                            var rr = LayerRegion.From(rough);
                            if (rr == null) continue;
                            int wi = 0;
                            for (float x = rr.Min.x + 2.5f; x < rr.Max.x; x += 4.5f)
                                for (float y = rr.Min.y + 2.5f; y < rr.Max.y; y += 4.5f, wi++)
                                {
                                    var p = new float2(x, y) + (new float2(Hashing.Unit(V(0x3101u)), Hashing.Unit(V(0x3102u))) - 0.5f) * 3f;
                                    if (!rr.ContainsDisc(p, 1.2f) || Hashing.Unit(V(0x3103u)) < 0.35f) continue;
                                    if (wi % 7 == 3 && rr.ContainsDisc(p, 3f)) { Tree(p, false, PlazaArt.PlaneTrees); continue; }
                                    bool dead = Hashing.Unit(V(0x3104u)) < 0.3f;
                                    LeisureKit.Put(Ctx, Occ, p, Hashing.Unit(V(0x3105u)) * 6.28f, ObjectRole.Bush, AssetMix.Pick(dead ? DeadBushes : WildBushes, V(0x3106u)), AssetCategories.RuinsWeeds, 0.9f, V(0x3107u), 0.1f, 0.05f, 0);
                                }
                        }
                        made++;
                        continue;
                    }
                    var lawns = Poly.Offset(piece, 1.5f);
                    foreach (var lawn in lawns)
                    {
                        if (lawn == null || lawn.Length < 3 || Poly.Area(lawn) < 100f) continue;
                        Lay(PGreen, lawn, Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                        if (manner == "hedge")
                        {
                            for (int e = 0; e < lawn.Length; e++)
                            {
                                var p0 = lawn[e]; var p1 = lawn[(e + 1) % lawn.Length];
                                var d = math.normalizesafe(p1 - p0);
                                var n = new float2(-d.y, d.x);
                                Hedge(p0 + d * 0.6f + n * 0.8f, p1 - d * 0.6f + n * 0.8f);
                            }
                        }
                        else FenceRing(lawn, 0.4f);
                        ShrubBorder(lawn, 1.3f, 4f);
                        // A pavilion in the garden's deepest point on a paved round, a walk from it towards the square,
                        // flowerbeds round it; trees round the edge and in the rest.
                        var llr = LayerRegion.From(lawn);
                        float lins = 0f;
                        var ld = llr != null ? llr.DeepestPoint(out lins, null, 16) : deep;
                        if (lins >= 8f)
                        {
                            Lay(PFeature + 3, PavingShapes.Polygon(ld, 5.6f, 24, Phase), P.Focal);
                            Lay(PKerb, PavingShapes.Annulus(ld, 5.6f, 0.7f, 24), P.Band);
                            var toC = math.normalizesafe(C - ld);
                            Lay(PWalk - 1, PavingShapes.Band(ld + toC * 5f, ld + toC * (lins + 4f), 3f), P.Path);
                            Occ.TakeCorridor(ld + toC * 5f, ld + toC * (lins + 4f), 2f, 2);
                            Gazebo(ld, math.atan2(toC.x, toC.y), 0, true);
                            for (int f = 0; f < 6; f++)
                            {
                                float an = Phase + f * math.PI / 3f + 0.5f;
                                Flowerbed(ld + new float2(math.cos(an), math.sin(an)) * 7.6f, an);
                            }
                            SeatRing(ld, 6.6f, 4, true);
                        }
                        // Cross walks through the pavilion, trees round the garden's edge (not a lattice), beds along the walks.
                        if (lins >= 8f)
                            foreach (var ax in new[] { A, S })
                            {
                                var band = PavingShapes.Band(ld - ax * 200f, ld + ax * 200f, 2.6f);
                                foreach (var part in Poly.Intersection(band, lawn))
                                    if (part?.Outer != null && part.Area > 6f) Lay(PGreen - 2, Poly.EnsureCcw(part.Outer), P.Path);
                                for (float t = 9f; t < 60f; t += 3.2f)
                                    foreach (float sg in new[] { -1f, 1f })
                                    {
                                        var fp = ld + ax * (sg * t) + new float2(-ax.y, ax.x) * 2.4f;
                                        if (llr != null && llr.ContainsDisc(fp, 2f)) Flowerbed(fp, Phase);
                                    }
                                Occ.TakeCorridor(ld - ax * (lins + 6f), ld + ax * (lins + 6f), 1.6f, 2);
                            }
                        var edge = llr != null ? llr.InsetRing(4f) : null;
                        var er = edge != null ? LayerRegion.From(edge) : null;
                        if (er != null)
                        {
                            int nt = math.max(3, (int)math.round(er.Perimeter / 10f));
                            for (int t = 0; t < nt; t++) Tree(er.PointAt(er.Perimeter * (t + 0.5f) / nt, out _, out _, out _), false, PlazaArt.GardenTrees);
                        }
                        if (lins < 8f) TreesIn(lawn, 12f, 3f, false);
                        // Benches outside on the long edges.
                        for (int e = 0; e < lawn.Length; e++)
                        {
                            var p0 = lawn[e]; var p1 = lawn[(e + 1) % lawn.Length];
                            float len = math.distance(p0, p1);
                            if (len < 10f) continue;
                            var d = (p1 - p0) / len;
                            var outward = new float2(d.y, -d.x);
                            foreach (var p in LeisureKit.Along(p0 + d * 3f, p1 - d * 3f, 9f)) Seat(p + outward * 1.4f, outward);
                        }
                        made++;
                    }
                }
                Ctx.Plan.Facts["leftoverGardens"] = made;
            }

            private float _leftEdge = -1f;
            private string _leftManner;
            /// <summary>Asks for the ground outside the box to be planted after the design (see <see cref="FillLeftover"/>).</summary>
            public void Leftover(float edgeInset, string manner) { _leftEdge = edgeInset; _leftManner = manner; }
            public void FillLeftover() { if (_leftEdge > 0f) FillLeftover(_leftEdge, _leftManner); }

            public float2 W(float u, float v) => C + A * u + S * v;
            public uint V(uint salt) => Hashing.Mix(Ctx.Seed, salt + (uint)(_n++ * 7919));

            public OrientedRect Box(float u0, float u1, float v0, float v1) =>
                new OrientedRect { Center = W((u0 + u1) * 0.5f, (v0 + v1) * 0.5f), Axis = A, HalfLength = math.abs(u1 - u0) * 0.5f, HalfWidth = math.abs(v1 - v0) * 0.5f };

            public bool Fits(OrientedRect r, float margin) => Fits(r.Ring, margin);

            /// <summary>The ring (grown by <paramref name="margin"/>, approximately) lies inside the square and off what is taken.</summary>
            public bool Fits(float2[] ring, float margin)
            {
                for (int i = 0; i < ring.Length; i++)
                {
                    if (!Region.Contains(ring[i]) || Region.BoundaryDistance(ring[i], margin + 1f) < margin) return false;
                    if (Region.CrossesBoundary(ring[i], ring[(i + 1) % ring.Length], false)) return false;
                }
                foreach (var poly in Occ.Polygons)
                {
                    var hit = Poly.Boolean(new[] { (IReadOnlyList<float2>)ring }, new[] { (IReadOnlyList<float2>)poly }, BoolOp.Intersection);
                    if (hit.Count > 0 && hit[0].Area > 1f) return false;
                }
                return true;
            }

            public void Lay(int prio, float2[] ring, AssetRef asset, SurfaceRole role = SurfaceRole.Plaza, string cat = AssetCategories.SurfacePaving)
            {
                if (ring == null || ring.Length < 3) return;
                Lay(prio, new List<float2[]> { ring }, asset, role, cat);
            }

            public void Lay(int prio, List<float2[]> rings, AssetRef asset, SurfaceRole role = SurfaceRole.Plaza, string cat = AssetCategories.SurfacePaving)
            {
                if (rings == null || rings.Count == 0) return;
                var list = new List<float2[]>();
                foreach (var r in rings) if (r != null && r.Length >= 3) list.Add(r);
                if (list.Count > 0) _lay.Add((prio, _lay.Count, list, role, asset, cat));
            }

            /// <summary>Hands the paving to the square's plan in priority order (tree pits first).</summary>
            public void Flush()
            {
                // Tree pits only in paving: a pit cut into a lawn's edge leaves a notch with corners the game refuses.
                foreach (var t in _pits)
                {
                    var pit = LayerRegion.Rect(t, A, 1.1f, 1.1f);
                    // ... nor across the frame band (a slanted outline cuts the square's corner into a spike).
                    bool onGreen = false;
                    foreach (var q in pit) if (!Region.Contains(q) || Region.BoundaryDistance(q, Fw + 1f) < Fw + 0.3f) onGreen = true;
                    foreach (var e in _lay)
                    {
                        if (e.role != SurfaceRole.Grass || onGreen) continue;
                        foreach (var r in e.rings)
                        {
                            Poly.Bounds(r, out var mn, out var mx);
                            if (t.x < mn.x - 2f || t.y < mn.y - 2f || t.x > mx.x + 2f || t.y > mx.y + 2f) continue;
                            var hit = Poly.Intersection(pit, r);
                            float a = 0f;
                            foreach (var h in hit) a += h.Area;
                            if (a > 0.05f) { onGreen = true; break; }
                        }
                    }
                    if (!onGreen) Lay(PPit, pit, P.Inlay);
                }
                _lay.Sort((x, y) => x.prio != y.prio ? x.prio.CompareTo(y.prio) : x.order.CompareTo(y.order));
                foreach (var e in _lay) Pave.Add(e.rings, e.role, e.asset, e.cat);
            }

            /// <summary>The subject ring minus the clip rings, as hole-free pieces of at least <paramref name="minArea"/> m².</summary>
            public List<float2[]> Pieces(float2[] subject, List<float2[]> clips, float minArea)
            {
                var res = new List<float2[]>();
                List<Region> parts;
                try
                {
                    var cl = new List<IReadOnlyList<float2>>();
                    foreach (var c in clips) if (c != null && c.Length >= 3) cl.Add(c);
                    parts = Poly.Boolean(new[] { (IReadOnlyList<float2>)subject }, cl, BoolOp.Difference);
                }
                catch (Exception) { return res; }
                foreach (var part in parts)
                {
                    if (part?.Outer == null || part.Area < minArea) continue;
                    if (part.HasHoles) { foreach (var r in Poly.CutHoles(part)) if (Poly.Area(r) >= minArea) res.Add(Poly.EnsureCcw(r)); }
                    else res.Add(Poly.EnsureCcw(part.Outer));
                }
                return res;
            }

            public bool NearGate(float2 p, float d) => PlazaDressing.NearGate(p, Gates, d);

            public bool Tree(float2 p, bool pit, string[] ids, float r = 2.2f)
            {
                if (NearGate(p, 6f)) return false;
                uint v = V(0x7E00u);
                if (!LeisureKit.Put(Ctx, Occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Tree, AssetMix.Pick(ids ?? Trees, v), null, r, v, 1f, 0.3f, 3, 3)) return false;
                if (pit) _pits.Add(p);
                TreeCount++;
                return true;
            }

            /// <summary>A bench facing <paramref name="facing"/>.</summary>
            public bool Seat(float2 p, float2 facing, string[] ids = null)
            {
                var f = math.normalizesafe(facing, A);
                var asset = ids != null && string.IsNullOrEmpty(Ctx.S("benchAsset")) ? AssetMix.Pick(ids, 0) : Bench;
                if (!LeisureKit.Put(Ctx, Occ, p, math.atan2(f.x, f.y), ObjectRole.Bench, asset, null, 1.1f, V(0xBE40u), 0.4f, 0.1f)) return false;
                BenchCount++;
                return true;
            }

            public bool Light(float2 p) => LeisureKit.Put(Ctx, Occ, p, 0f, ObjectRole.Lamp, Lamp, null, 0.4f, V(0x1170u), 0.4f, 0.1f);

            public void Flowerbed(float2 p, float yaw)
            {
                uint v = V(0xF100u);
                LeisureKit.Put(Ctx, Occ, p, yaw, ObjectRole.Planter, PlazaArt.Or(Ctx, "flowerbedAsset", PlazaArt.Flowerbeds, v), AssetCategories.Flowerbed, 1.2f, v, 0.3f, 0.05f, 3);
            }

            public void Topiary(float2 p)
            {
                uint v = V(0x70B0u);
                LeisureKit.Put(Ctx, Occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Bush, PlazaArt.Or(Ctx, "bushAsset", PlazaArt.GreenBushes, v), AssetCategories.Bush, 0.55f, v, 0.3f, 0.05f, 3);
            }

            public void Pot(float2 p)
            {
                uint v = V(0xED00u);
                LeisureKit.Put(Ctx, Occ, p, 0f, ObjectRole.Planter, PlazaArt.Or(Ctx, "planterAsset", PlazaArt.Pots, v), AssetCategories.Planter, 0.5f, v, 0.3f, 0.1f);
            }

            public void Flag(float2 p) =>
                LeisureKit.Put(Ctx, Occ, p, 0f, ObjectRole.Furniture, PlazaArt.FlagPole, AssetCategories.MemorialFlag, 0.4f, V(0xF1A0u), 0.3f, 0.1f);

            public void Hedge(float2 a, float2 b)
            {
                if (math.distance(a, b) < 2.5f) return;
                HedgeCount += GroundCover.HedgeRow(Ctx, a, b, HedgeAsset, AssetCategories.Hedge, ObjectRole.Bush, V(0x4ED0u), 4f, Occ);
                Occ.TakeCorridor(a, b, 0.7f, 2);
            }

            public void FenceLine(float2 a, float2 b)
            {
                if (math.distance(a, b) < 1.5f) return;
                Ctx.StraightNet(a, b, NetRole.Fence, FenceAsset);
                Occ.TakeCorridor(a, b, 0.5f, 2);
                FenceCount++;
            }

            /// <summary>Low railings just inside a lawn's edge (one straight piece per edge).</summary>
            public void FenceRing(float2[] ring, float inset)
            {
                var ins = Poly.Offset(ring, inset);
                if (ins.Count == 0) return;
                var r = Poly.EnsureCcw(ins[0]);
                // Merge nearly straight runs (arcs of a round centre) into longer pieces.
                var pts = new List<float2>();
                for (int i = 0; i < r.Length; i++)
                {
                    var prev = r[(i + r.Length - 1) % r.Length]; var cur = r[i]; var next = r[(i + 1) % r.Length];
                    var d0 = math.normalizesafe(cur - prev); var d1 = math.normalizesafe(next - cur);
                    if (math.dot(d0, d1) < 0.985f || math.distance(prev, cur) > 6f) pts.Add(cur);
                }
                if (pts.Count < 3) pts = new List<float2>(r);
                for (int i = 0; i < pts.Count; i++) FenceLine(pts[i], pts[(i + 1) % pts.Count]);
            }

            /// <summary>Flowering shrubs just inside a lawn's railings, every <paramref name="pitch"/> metres.</summary>
            public void ShrubBorder(float2[] ring, float inset, float pitch)
            {
                var ins = Poly.Offset(ring, inset);
                if (ins.Count == 0) return;
                var lr = LayerRegion.From(ins[0]);
                if (lr == null) return;
                int n = (int)(lr.Perimeter / pitch);
                for (int i = 0; i < n; i++)
                {
                    var p = lr.PointAt(lr.Perimeter * (i + 0.5f) / n, out _, out _, out _);
                    uint v = V(0x5B00u);
                    LeisureKit.Put(Ctx, Occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Bush, PlazaArt.Or(Ctx, "bushAsset", PlazaArt.CityBushes, v), AssetCategories.Bush, 0.5f, v, 0.2f, 0.05f, 3);
                }
            }

            /// <summary>Trees on a loose lattice inside a lawn (kept <paramref name="edge"/> metres off its edge).</summary>
            public void TreesIn(float2[] ring, float pitch, float edge, bool pit)
            {
                var lr = LayerRegion.From(ring);
                if (lr == null) return;
                var min = lr.Min; var max = lr.Max;
                float reach = math.length(max - min);
                var c0 = W(0f, 0f);
                int n = (int)math.ceil(reach / pitch) + 2;
                for (int i = -n * 2; i <= n * 2; i++)
                    for (int j = -n * 2; j <= n * 2; j++)
                    {
                        var p = c0 + A * (i * pitch + ((j & 1) != 0 ? pitch * 0.5f : 0f)) + S * (j * pitch * 0.87f);
                        if (p.x < min.x || p.y < min.y || p.x > max.x || p.y > max.y) continue;
                        if (!lr.ContainsDisc(p, edge)) continue;
                        Tree(p, pit, PlazaArt.PlaneTrees);
                    }
            }

            /// <summary>Hedges just inside a room's edge, open in the middle of every side.</summary>
            public void HedgeBox(OrientedRect room, float inset, float opening, bool high = false)
            {
                var r = new OrientedRect { Center = room.Center, Axis = room.Axis, HalfLength = room.HalfLength - inset, HalfWidth = room.HalfWidth - inset }.Ring;
                for (int e = 0; e < 4; e++)
                {
                    var a = r[e]; var b = r[(e + 1) % 4];
                    float len = math.distance(a, b);
                    var d = (b - a) / len;
                    float h = len * 0.5f - opening * 0.5f;
                    if (high) { HighHedge(a + d * 0.3f, a + d * h); HighHedge(b - d * h, b - d * 0.3f); }
                    else { Hedge(a + d * 0.3f, a + d * h); Hedge(b - d * h, b - d * 0.3f); }
                }
            }

            /// <summary>
            /// Trees along the outline at <paramref name="inset"/>: in pits; benches between every other pair (facing the
            /// middle when <paramref name="benches"/> &gt; 0, the outline when &lt; 0); lamps between the others.
            /// </summary>
            public void RingTrees(float inset, float pitch, bool pit, int benches, bool lamps)
            {
                var inner = Region.InsetRing(inset);
                var r = inner != null ? LayerRegion.From(inner) : null;
                if (r == null) return;
                int count = math.max(1, (int)math.round(r.Perimeter / pitch));
                float step = r.Perimeter / count;
                float2 prev = default; bool havePrev = false;
                for (int i = 0; i < count; i++)
                {
                    var p = r.PointAt(step * (i + 0.5f), out var t, out var inward, out _);
                    bool ok = Tree(p, pit, null);
                    if (ok && havePrev && math.distance(prev, p) < pitch * 1.4f)
                    {
                        var mid = (prev + p) * 0.5f;
                        if ((i & 1) == 0 && benches != 0) Seat(mid, benches > 0 ? inward : -inward);
                        else if (lamps) Light(mid);
                    }
                    prev = p; havePrev = ok;
                }
            }

            /// <summary>Benches along both sides of a walk facing it, lamps on its edges; nothing near the centre or the ends.</summary>
            public void WalkSeats(float2 a, float2 b, float off, float pitch, float2 centre, float keep, float lampPitch)
            {
                float len = math.distance(a, b);
                if (len < 8f) return;
                var d = (b - a) / len;
                var n = new float2(-d.y, d.x);
                int i = 0;
                foreach (var p in LeisureKit.Along(a + d * 4f, b - d * 4f, pitch))
                {
                    if (math.distance(p, centre) < keep) continue;
                    Seat(p + n * off, -n);
                    Seat(p - n * off, n);
                    i++;
                }
                int k = 0;
                foreach (var p in LeisureKit.Along(a + d * 2f, b - d * 2f, lampPitch))
                {
                    if (math.distance(p, centre) < keep) continue;
                    Light(p + n * ((k++ & 1) == 0 ? off - 0.4f : -(off - 0.4f)) + d * (pitch * 0.5f));
                }
            }

            public void SeatRing(float2 c, float r, int n, bool facingIn)
            {
                for (int i = 0; i < n; i++)
                {
                    float a = Phase + (i + 0.5f) * 2f * math.PI / n;
                    var d = new float2(math.cos(a), math.sin(a));
                    Seat(c + d * r, facingIn ? -d : d);
                }
            }

            public void LampRing(float2 c, float r, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    float a = Phase + i * 2f * math.PI / n;
                    Light(c + new float2(math.cos(a), math.sin(a)) * r);
                }
            }

            /// <summary>An installed centrepiece of the kind (the player's pick first).</summary>
            public void CentrePiece(float2 c, float clear, string kind)
            {
                var asset = PlazaDressing.CenterAsset(Ctx, kind);
                string cat = PlazaDressing.CenterCategory(kind) ?? AssetCategories.Centerpiece;
                if (!Occ.Free(c, 1f, 0.5f, 0f)) return;
                Ctx.Object(c, Phase + math.PI * 0.5f, ObjectRole.Centerpiece, asset, V(0xCE47u), -1, cat);
                Occ.Take(c, math.max(1.5f, clear), 0);
            }

            /// <summary>The fountain feature builds the centre: keep it clear for it.</summary>
            public void CentreClear(float2 c, float r)
            {
                // Nothing to build without installed fountains: plant it (as the classic square does).
                if (!CentreAvailable) PlazaDressing.SpecimenCentre(Ctx, Occ, c, math.max(3.5f, r - 0.6f), Pave, 0x5BE0u);
                else Occ.Take(c, math.max(3f, r - 0.5f), 0);
            }

            /// <summary>A sloped terraform quad (corner heights in y).</summary>
            public void Quad(float3[] ring, bool raise)
            {
                Ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = ring, Kind = raise ? TerraformKind.Raise : TerraformKind.Lower,
                    TargetHeight = TerraformSurface.At(ring, Poly.Centroid(Poly.To2D(ring))), Permanent = true, Sloped = true,
                });
            }
        }
    }
}
