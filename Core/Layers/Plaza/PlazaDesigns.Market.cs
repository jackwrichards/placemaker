// The market square (the plazas makeover, 2026-10-29, docs/runbooks/plazas.md). The user graded it D: a scatter of stall
// huts on a bare slab round one big tree. Rebuilt on the designed squares' kit as an old-town market square:
//
//   ground    setts in a dark flag frame, two market streets across on the square's own axes in pale stone
//   the cross a fountain (a drum of wall fountains in the clean game) where the streets meet, flowerbeds, benches
//   stalls    four quarters of stalls in rows back to back, every row facing an aisle: food stalls, produce stands (a table
//             under a big parasol with crates of produce beside it), the odd cart and gazebo; how many follows the
//             player's "stalls" (and "fill" packs the quarters)
//   edges     a ring of trees in pits with benches and lamps ("tree ring"), cafe tables under parasols at one end ("cafe"),
//             bins along the streets
// Market squares have no lanes: like every square their shops (when street shops are on) face the city streets.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        private static readonly string[] Produce = { "Crate01", "Crate02", "Crate03", "CratePile03" };
        private static readonly string[] Carts = { "FoodCart01", "FoodCart02" };

        private static void Market(Kit k)
        {
            float fw = k.Fw;
            k.Leftover(fw + 3f, "bosque");
            k.WallId = "EU_Wall03";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            bool ring = k.Ctx.Settings == null || k.Ctx.B("treeRing");
            bool fill = k.Ctx.B("fill");
            float edge = fw + (ring ? 5.5f : 1.5f);
            int before = k.TreeCount;
            if (ring) k.RingTrees(fw + 2f, 8.5f, true, 1, true);
            k.Ctx.Plan.Facts["ringTrees"] = k.TreeCount - before;
            float ia = k.HA - edge, ib = k.HB - edge;
            if (ia < 10f || ib < 10f) { Piazza(k); return; }
            // the market streets on the square's axes, pale stone
            float half = math.max(2.4f, (float)k.Ctx.F("aisle") * 0.6f + 0.8f);
            k.Lay(PWalk, k.Box(-ia - 1f, ia + 1f, -half, half).Ring, k.P.Path);
            k.Lay(PWalk, k.Box(-half, half, -ib - 1f, ib + 1f).Ring, k.P.Path);
            // the market cross
            float room = math.clamp(math.min(ia, ib) * 0.22f, 5f, 8f);
            k.Lay(PFeature + 3, PavingShapes.Polygon(k.C, room + 1.5f, 32, k.Phase), k.P.Focal);
            k.Lay(PKerb, PavingShapes.Annulus(k.C, room + 1.5f, 0.8f, 32), k.P.Band);
            float keep = k.Centre(k.C, room, "fountain");
            k.SeatRing(k.C, keep + 1.2f, 8, false);
            k.Core = keep;
            // cafes at the -A end
            int cafe = k.Ctx.Settings == null ? 2 : k.Ctx.I("cafe");
            float cafeDepth = cafe > 0 && !fill ? math.clamp(ia * 0.22f, 7f, 14f) : 0f;
            if (cafeDepth > 0f)
                foreach (float sv in new[] { -1f, 1f })
                    k.CafeGrid(-ia, -ia + cafeDepth, sv > 0 ? half + 1.5f : -ib, sv > 0 ? ib : -half - 1.5f, 5.2f, true);

            // the stalls: rows along A in each quarter, back to back, each row facing its aisle
            int asked = k.Ctx.Settings == null ? 16 : k.Ctx.I("stalls");
            float density = fill ? 1f : math.clamp(asked / 30f, 0.35f, 1f);
            float aisle = math.max(3.5f, (float)k.Ctx.F("aisle"));
            const float depth = 3.8f, pitch = 4.8f;
            int made = 0, produce = 0;
            foreach (float sv in new[] { -1f, 1f })
            {
                // row pairs: aisle | row facing it | row back to back | aisle ...
                float v = half + aisle;
                int pair = 0;
                while (v + depth * 2f <= ib + 0.5f)
                {
                    foreach (int side in new[] { 0, 1 })
                    {
                        float rv = v + depth * (side + 0.5f);
                        var facing = -k.S * sv * (side == 0 ? 1f : -1f);
                        foreach (float su in new[] { -1f, 1f })
                        {
                            float u0 = su < 0 ? -ia + cafeDepth + 1f : half + 3f, u1 = su < 0 ? -half - 3f : ia - 1f;
                            if (math.abs(rv) < keep + 3f) continue;
                            int i = 0;
                            for (float u = u0 + pitch * 0.5f; u <= u1 - pitch * 0.5f + 0.01f; u += pitch, i++)
                            {
                                // a gap every so often (a cut through to the next aisle) and thinned to the asked number
                                if (i % 6 == 5) continue;
                                var p = k.W(u, sv * rv);
                                if (!k.Region.ContainsDisc(p, 2.4f) || math.distance(p, k.C) < keep + 4f) continue;
                                float roll = Hashing.Unit(k.V(0x3A7Du));
                                if (roll < 0.48f) { if (k.Stall(p, facing)) made++; }
                                else if (roll < 0.92f) { if (ProduceStand(k, p, facing)) { made++; produce++; } }
                                else if (MarketProp(k, p, facing, AssetMix.Pick(Carts, k.V(0x3A7Eu)).Id, AssetCategories.MarketStall, 2.3f)) made++;
                            }
                        }
                    }
                    v += depth * 2f + aisle;
                    pair++;
                    if (!fill && pair >= math.max(1, (int)math.round(density * 3f))) break;
                }
                // bins along the cross street
                foreach (var p in LeisureKit.Along(k.W(-ia + 2f, sv * (half + 0.9f)), k.W(ia - 2f, sv * (half + 0.9f)), 24f))
                    if (math.distance(p, k.C) > keep + 3f) LeisureKit.Put(k.Ctx, k.Occ, p, 0f, ObjectRole.Bin, new AssetRef("Trashbin01", "Trashbin02"), AssetCategories.Bin, 0.5f, k.V(0xB1E0u), 0.2f, 0.1f);
            }
            // flags at the ends of the long street
            foreach (float su in new[] { -1f, 1f }) foreach (float sv in new[] { -1f, 1f }) k.BigFlag(k.W(su * (ia - 0.5f), sv * (half + 0.8f)));
            k.Ctx.Plan.Facts["marketStalls"] = made;
            k.Ctx.Plan.Facts["marketProduce"] = produce;
        }

        /// <summary>A produce stand: a table under a big parasol, crates of produce stacked beside it.</summary>
        private static bool ProduceStand(Kit k, float2 p, float2 facing)
        {
            if (!k.Occ.Free(p, 2.1f, 0.1f, 0.05f)) return false;
            float yaw = math.atan2(facing.x, facing.y);
            uint v = k.V(0x9C0Du);
            k.Ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(SmallTables, v), v, -1, AssetCategories.MarketStall);
            k.Ctx.Object(p - facing * 1.1f, yaw, ObjectRole.Furniture, AssetMix.Pick(BigParasols, Hashing.Mix(v, 3u)), Hashing.Mix(v, 5u), -1, AssetCategories.MarketStall);
            var side = new float2(-facing.y, facing.x);
            int n = 1 + (int)(Hashing.Unit(Hashing.Mix(v, 11u)) * 3f);
            for (int i = 0; i < n; i++)
                k.Ctx.Object(p - facing * 0.6f + side * ((i - (n - 1) * 0.5f) * 1.05f + 1.4f * (i % 2 == 0 ? 1f : -1f)), yaw + (i % 2) * 1.57f, ObjectRole.Furniture,
                    AssetMix.Pick(Produce, Hashing.Mix(v, 13u + (uint)i)), Hashing.Mix(v, 17u + (uint)i), -1, AssetCategories.RuinsJunk);
            k.Occ.Take(p, 2.1f, 1);
            return true;
        }

        private static bool MarketProp(Kit k, float2 p, float2 facing, string id, string cat, float r) =>
            LeisureKit.Put(k.Ctx, k.Occ, p, math.atan2(facing.x, facing.y), ObjectRole.Furniture, new AssetRef(id), cat, r, k.V(0x9C0Eu), 0.1f, 0.05f, 1);
    }
}
