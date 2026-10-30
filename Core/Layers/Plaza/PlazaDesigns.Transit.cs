// The transit plaza's forecourt (the plazas makeover, 2026-10-29, docs/runbooks/plazas.md). The transit plaza was hidden:
// a bus lay-by along the street and then a big bare white slab with a ring of benches. The lay-by, the bus bays, the taxi
// rank and the park & ride stay (TransitPlazaLayer); behind them the forecourt is now laid on the designed squares' kit:
//
//   platform  a long canopy of pergola bays two deep along the kerb, benches under it facing the buses, a dark platform
//             edge, a kiosk and newspaper boxes at its ends
//   grove     trees in pits on a loose grid with benches between them, an open walk on the axis from the platform to
//             the focal point (the game's statue or a tall flag in a round of dark stone)
//   bikes     rows of bike racks under the trees at one end (many more at a bike hub)
// The frontage side comes from the layer (the plan's facts "frontX", "frontY": the inward normal of the kerb).

using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        private static readonly string[] BikeRacks = { "BikeRack01", "BikeRack02", "BikeRack03" };
        private static readonly string[] BikeShelterIds = { "BicycleShelter01", "BicycleShelter02", "BicycleShelter03" };

        private static void Transit(Kit k)
        {
            float fw = k.Fw;
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            // turn the frame so -S faces the street (the kerb lane)
            var n = new float2(k.Ctx.Plan.Facts.TryGetValue("frontX", out var fx) ? (float)fx : 0f, k.Ctx.Plan.Facts.TryGetValue("frontY", out var fy) ? (float)fy : 1f);
            if (math.abs(math.dot(n, k.A)) > math.abs(math.dot(n, k.S))) k.TurnQuarter();
            if (math.dot(n, k.S) < 0f) k.Flip();
            // (round 3) a big site: the forecourt keeps a real square's size along its street, a park behind it
            if (k.HA > CapA + 6f || k.HB > CapB + 6f)
            {
                PlazaDressing.Extents(k.Region, k.C, k.A, out _, out _, out float minB, out _);
                float hb0 = k.HB;
                k.HA = math.min(k.HA, CapA); k.HB = math.min(k.HB, CapB);
                k.C += k.S * (minB + k.HB + 0f) * (hb0 > k.HB ? 1f : 0f);
                k.Capped = true;
                k.Ctx.Plan.Facts["designCapped"] = 1;
            }
            float ea = k.EA, eb = k.EB;
            if (ea < 10f || eb < 9f) { Piazza(k); return; }
            bool bikes = k.Ctx.Plan.Facts.TryGetValue("transitBikes", out var bf) && bf > 0;
            bool focalPiece = k.Ctx.Plan.Facts.TryGetValue("transitFocal", out var ff) && ff > 0;
            int trees = k.Ctx.Plan.Facts.TryGetValue("transitTrees", out var tf) ? (int)tf : 1;
            // the platform canopy along the kerb
            float vp = -eb + 0.5f;
            k.Lay(PFeature - 2, k.Box(-ea, ea, vp - 0.5f, vp + 0.3f).Ring, k.P.Inlay);
            float cl = math.min(ea - 6f, 48f);
            var strip = k.Canopy(k.W(-cl, vp + 1.2f), k.W(cl, vp + 1.2f), 2, "PergolaModular04", true);
            k.Lay(PFeature + 2, strip.Ring, k.P.Accent);
            foreach (var p in LeisureKit.Along(k.W(-cl + 1.5f, vp + 2.4f), k.W(cl - 1.5f, vp + 2.4f), 3.4f)) k.Seat(p, -k.S, PlazaArt.ModernBenches);
            foreach (var p in LeisureKit.Along(k.W(-cl + 4f, vp + 5f), k.W(cl - 4f, vp + 5f), 12f))
                LeisureKit.Put(k.Ctx, k.Occ, p, 0f, ObjectRole.Bin, new AssetRef("Trashbin03", "Trashbin01"), AssetCategories.Bin, 0.5f, k.V(0xB1E1u), 0.1f, 0.05f);
            LeisureKit.TakeRect(k.Occ, strip);
            foreach (float su in new[] { -1f, 1f })
            {
                var kp = k.W(su * (cl + 3.2f), vp + 3.4f);
                if (k.Region.ContainsDisc(kp, 3f)) k.Stall(kp, k.S, true);
                Prop(k, k.W(su * (cl + 1.2f), vp + 0.8f), k.Phase, AssetMix.Pick(Newspapers, k.V(0xBE11u)).Id, AssetCategories.Kiosk, 0.5f);
            }
            // the focal point on the axis, an open walk to it from the platform
            float vf = math.min(eb * 0.35f, vp + 6f + (eb - vp) * 0.45f);
            var focal = k.W(0f, vf);
            k.Lay(PWalk, k.Box(-3f, 3f, vp + 6f, vf).Ring, k.P.Path);
            k.Lay(PFeature + 3, PavingShapes.Polygon(focal, 6f, 32, k.Phase), k.P.Focal);
            k.Lay(PKerb, PavingShapes.Annulus(focal, 6f, 0.8f, 32), k.P.Band);
            if (focalPiece && k.CentreAvailable) k.CentrePiece(focal, 4f, "statue");
            else { k.BigFlag(focal, true); k.SeatRing(focal, 4.6f, 6, false); }
            k.Occ.Take(focal, 6.5f, 0);
            k.Occ.TakeCorridor(k.W(0f, vp + 6f), focal, 3f, 2);
            k.Core = 6f;
            // the grove: trees in pits on a loose grid, benches between, bike racks at one end
            float bikeU = bikes ? 0f : ea * 0.45f;
            int t = 0;
            float pitch = trees == 2 ? 15f : 13f;
            // (the plazas makeover, game runner on 220 x 200) a big forecourt was one endless grove: it is broken into rooms by
            // two cross walks, and every few cells a lawn island with trees and benches round it takes the place of the grid
            bool big = ea * eb > 2600f;
            var walks = new List<float>();
            if (big)
                foreach (float su in new[] { -1f, 1f })
                {
                    float uw = su * ea * 0.5f;
                    walks.Add(uw);
                    k.Lay(PWalk, k.Box(uw - 2f, uw + 2f, vp + 6f, eb).Ring, k.P.Path);
                    k.Light(k.W(uw + 2.6f, (vp + eb) * 0.5f));
                }
            int iv = 0;
            for (float v = vp + 10f; v <= eb - 2.5f; v += 11f, iv++)
            {
                int iu = 0;
                for (float u = -ea + 3f; u <= ea - 3f + 0.01f; u += pitch, t++, iu++)
                {
                    var p = k.W(u + (trees == 1 ? (iv & 1) * 6.5f : 0f), v);
                    bool onWalk = false;
                    foreach (float uw in walks) if (math.abs(math.dot(p - k.C, k.A) - uw) < 4.5f) onWalk = true;
                    if (onWalk) continue;
                    if (big)
                    {
                        // (round 3, the lead: "the transit grove is still open") rooms, not one grid: a third of the forecourt is open
                        // paving with a cafe corner and a kiosk, the rest groves, by zones across and bands of ~22 m
                        int zi = (int)((u + ea) / (2f * ea / 3f)), bi = (int)((v - vp - 10f) / 22f);
                        if (((zi + bi * 2) % 3) == 1)
                        {
                            if (((iu + iv) % 4) == 0) k.CafeSet(p, k.Phase + 0.3f * iu, true, true);
                            else if (((iu + iv) % 7) == 3) k.Stall(p, -k.S, true);
                            continue;
                        }
                    }
                    if (big && trees > 0 && (iu % 2) == 0 && ((iu / 2 + iv) % 3) == 1 && v + 5f < eb && math.distance(p, focal) > 14f)
                    {
                        // a lawn island: grass in a dark kerb, two trees, benches facing out on its long sides
                        var c = p + k.A * (pitch * 0.5f);
                        var isl = new OrientedRect { Center = c, Axis = k.A, HalfLength = pitch * 0.5f + 1.5f, HalfWidth = 3.2f };
                        if (k.Fits(isl, 1f) && k.Occ.Free(c, 4f, 0.2f, 0.1f))
                        {
                            k.Lay(PFeature, isl.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                            k.Lay(PKerb, PavingShapes.Frame(new OrientedRect { Center = c, Axis = k.A, HalfLength = isl.HalfLength + 0.5f, HalfWidth = isl.HalfWidth + 0.5f }.Ring, 0.5f), k.P.Band);
                            k.Tree(c - k.A * (pitch * 0.3f), false, PlazaArt.GardenTrees);
                            k.Tree(c + k.A * (pitch * 0.3f), false, PlazaArt.GardenTrees);
                            foreach (float sv in new[] { -1f, 1f })
                                foreach (var q in LeisureKit.Along(c - k.A * (pitch * 0.4f) + k.S * (sv * 4.6f), c + k.A * (pitch * 0.4f) + k.S * (sv * 4.6f), 3.6f)) k.Seat(q, k.S * sv);
                            LeisureKit.TakeRect(k.Occ, isl);
                            iu++; u += pitch; t++;
                            continue;
                        }
                    }
                    if (trees == 3 && (t % 4) >= 2) { if ((t % 4) == 2) k.CafeSet(p, k.Phase + 0.3f, true, true); continue; }
                    if (math.distance(p, focal) < 9f || math.abs(u) < 4.5f) continue;
                    bool inBikes = bikes ? math.abs(u) > 6f && (t % 3) != 0 : u > bikeU;
                    if (inBikes)
                    {
                        if (trees > 0) k.Tree(p, true, PlazaArt.LimeTrees);
                        else if (bikes && (t % 2) == 0) LeisureKit.Put(k.Ctx, k.Occ, p, k.Phase, ObjectRole.Furniture, new AssetRef(AssetMix.Pick(BikeShelterIds, k.V(0xB1C2u)).Id, "BicycleShelter01"), AssetCategories.Shelter, 2.2f, k.V(0xB1C3u), 0.1f, 0.05f);
                        foreach (float dv in new[] { -2.4f, 2.4f })
                            foreach (var q in LeisureKit.Along(p + k.S * dv - k.A * 3f, p + k.S * dv + k.A * 3f, 1.2f))
                                LeisureKit.Put(k.Ctx, k.Occ, q, k.Phase + math.PI * 0.5f, ObjectRole.Furniture, new AssetRef(AssetMix.Pick(BikeRacks, k.V(0xB1C0u)).Id, "BikeRack01"), "transport.bikeRack", 0.45f, k.V(0xB1C1u), 0.05f, 0.02f);
                    }
                    else if ((t & 3) != 3) { if (trees > 0) k.Tree(p, true, PlazaArt.PlaneTrees); k.Seat(p - k.A * 2.6f, -k.A); }
                    else k.CafeSet(p, k.Phase + 0.3f, true, true);
                }
            }
            foreach (float su in new[] { -1f, 1f }) k.Light(k.W(su * 3.6f, (vp + vf) * 0.5f));
            if (bikes)
                foreach (float su in new[] { -1f, 1f })
                    foreach (var p in LeisureKit.Along(k.W(su * 6f, vp + 9f - 3.5f), k.W(su * (ea - 4f), vp + 9f - 3.5f), 5f))
                        LeisureKit.Put(k.Ctx, k.Occ, p, k.Phase, ObjectRole.Furniture, new AssetRef(AssetMix.Pick(BikeShelterIds, k.V(0xB1C4u)).Id, "BicycleShelter01"), AssetCategories.Shelter, 2.2f, k.V(0xB1C5u), 0.1f, 0.05f);
        }
    }
}
