// The fairground (the plazas makeover, 2026-10-29, docs/runbooks/plazas.md): back as the events side of Plazas. It was
// hidden: stall rows on a lawn with an empty half. Rebuilt on the designed squares' kit as a summer fair on a green:
//
//   midway    a paved avenue across the green, lined both sides with food stalls, carts and kiosks facing it, festival
//             flags every few stalls, bins
//   stage     at one end: the game's stage, deckchairs in rows and picnic blankets on the lawn in front, the backstage
//             behind it (a generator, cable reels, a trailer)
//   kids      at the other end: a play corner (climbing frames, swings, slides, a playset) on sand
//   food court benches and picnic tables under shade trees off the avenue, grills, a row of portable toilets
//   loop      a paved promenade round the green inside the outline, trees along it
// "christmas" is the same fair in winter: setts instead of the lawn, spruces instead of the play corner, a big tree.

using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        private static readonly string[] PlayKit = { "ClimbingFrame04", "PlaygroundPlayset02", "SwingSet03", "ClimbingFrame01", "PlaygroundPlayset01", "Slide02", "SwingSet01", "ClimbingFrame03" };
        private static readonly string[] Deck = { "DeckChair01", "DeckChair02", "DeckChair03" };
        private static readonly string[] Blanket = { "PicnicBlanket01", "PicnicBlanket02", "PicnicBlanket04", "PicnicBlanket05" };
        private static readonly string[] PicnicTables = { "CampgroundTableset01", "CampgroundTableset02" };
        private static readonly string[] Spruces = { "SpruceTree01", "EuropeanSpruce01", "PineTree01" };

        private static void Fair(Kit k, bool winter)
        {
            float fw = k.Fw;
            k.Leftover(fw + 3f, "bosque");
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + 3.5f), k.P.Path);
            float ea = k.EA, eb = k.EB;
            if (ea < 14f || eb < 12f) { Piazza(k); return; }
            if (eb > ea) { k.TurnQuarter(); ea = k.EA; eb = k.EB; }
            // the green (summer) or setts (winter) inside the loop
            if (!winter) k.Lay(PGreen + 4, k.Box(-ea, ea, -eb, eb).Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
            // the midway across the middle
            const float half = 3.2f;
            k.Lay(PWalk, k.Box(-ea - 1f, ea + 1f, -half, half).Ring, k.P.Path);
            float stageEnd = ea - math.clamp(ea * 0.3f, 12f, 26f), kidsEnd = -ea + math.clamp(ea * 0.25f, 10f, 20f);
            int stalls = 0;
            foreach (float sv in new[] { -1f, 1f })
            {
                int i = 0;
                for (float u = kidsEnd + 3f; u <= stageEnd - 3f; u += 5.2f, i++)
                {
                    var p = k.W(u, sv * (half + 2.4f));
                    var facing = -k.S * sv;
                    if (i % 7 == 3) { k.BigFlag(k.W(u, sv * (half + 0.6f))); continue; }
                    float roll = Hashing.Unit(k.V(0xFA1Au));
                    bool ok = roll < 0.6f ? k.Stall(p, facing) : roll < 0.85f ? MarketProp(k, p, facing, AssetMix.Pick(Carts, k.V(0xFA1Bu)).Id, AssetCategories.MarketStall, 2.3f) : k.Stall(p, facing, true);
                    if (ok) stalls++;
                }
                foreach (var p in LeisureKit.Along(k.W(kidsEnd + 5f, sv * (half + 0.7f)), k.W(stageEnd - 5f, sv * (half + 0.7f)), 18f))
                    LeisureKit.Put(k.Ctx, k.Occ, p, 0f, ObjectRole.Bin, new AssetRef("Trashbin02", "Trashbin01"), AssetCategories.Bin, 0.5f, k.V(0xFA1Cu), 0.1f, 0.05f);
            }
            // the food courts behind the stalls: picnic tables and benches under shade trees, grills
            foreach (float sv in new[] { -1f, 1f })
            {
                float v0 = half + 6.5f, v1 = eb - 3f;
                if (v1 - v0 < 6f) continue;
                int t = 0;
                for (float v = v0 + 3f; v <= v1; v += 8f)
                    for (float u = kidsEnd + 5f; u <= stageEnd - 5f; u += 9f, t++)
                    {
                        var p = k.W(u + ((t & 1) * 4f), sv * v);
                        if ((t % 4) == 0) { if (winter) { if ((t % 8) == 0) k.Tree(p, false, Spruces); } else k.Tree(p, false, PlazaArt.GardenTrees); }
                        else if ((t % 4) == 1 || (t % 4) == 3) MarketProp(k, p, k.A, AssetMix.Pick(PicnicTables, k.V(0xFA1Du)).Id, AssetCategories.Picnic, 1.3f);
                        else if (!winter && Hashing.Unit(k.V(0xFA1Eu)) < 0.4f) MarketProp(k, p, k.A, "Grill01", AssetCategories.Picnic, 0.8f);
                        else k.CafeSet(p, k.Phase + 0.4f, !winter, true);
                    }
            }
            // the stage end: the stage facing the midway, deckchairs and blankets in front, the backstage behind it
            {
                var sc = k.W(ea - 4f, 0f);
                k.Lay(PFeature, k.Box(ea - 8f, ea - 0.5f, -6f, 6f).Ring, k.P.Focal);
                k.Ctx.Object(sc, math.atan2(-k.A.x, -k.A.y), ObjectRole.Furniture, new AssetRef("StageSmall01"), k.V(0xFA20u), -1, AssetCategories.Stage);
                k.Occ.Take(sc, 4.5f, 0);
                int rowN = 0;
                for (float u = ea - 11f; u >= stageEnd + 1f; u -= 2.6f, rowN++)
                    for (float v = -eb + 4f; v <= eb - 4f; v += winter ? 3.2f : 1.6f)
                    {
                        if (math.abs(v) < half + 0.5f) continue;
                        var p = k.W(u, v);
                        if (winter) { if (Hashing.Unit(k.V(0xFA21u)) < 0.5f) k.Seat(p, k.A); continue; }
                        if (Hashing.Unit(k.V(0xFA22u)) < (rowN < 3 ? 0.85f : 0.4f))
                            MarketProp(k, p, k.A, AssetMix.Pick(rowN < 3 ? Deck : Blanket, k.V(0xFA23u)).Id, rowN < 3 ? AssetCategories.EventDeckchair : AssetCategories.Picnic, 0.5f);
                    }
                foreach (var p in LeisureKit.Along(k.W(ea - 2f, -eb + 2.5f), k.W(ea - 2f, -eb + 9f), 1.6f)) MarketProp(k, p, -k.A, "PortableToilet01", AssetCategories.EventToilet, 0.7f);
                MarketProp(k, k.W(ea - 2f, eb - 5f), -k.S, "BackupGenerator01", AssetCategories.EventBackstage, 2.5f);
                MarketProp(k, k.W(ea - 5.5f, eb - 3f), 0f * k.A + k.A, "CableReel01", AssetCategories.EventBackstage, 0.7f);
                foreach (float s in new[] { -1f, 1f }) k.BigFlag(k.W(ea - 7.5f, s * 7f), true);
            }
            // the other end: the play corner (summer) on sand, or a grove of spruces round a big tree (winter)
            {
                float u0 = -ea + 1f, u1 = kidsEnd - 1f;
                var box = k.Box(u0, u1, -eb + 2f, eb - 2f);
                if (!winter)
                {
                    foreach (float sv in new[] { -1f, 1f })
                    {
                        var sand = k.Box(u0 + 1f, u1 - 1f, sv > 0 ? half + 2f : -eb + 3f, sv > 0 ? eb - 3f : -half - 2f);
                        k.Lay(PFeature + 1, sand.Ring, new AssetRef("", "Sand Surface 01", "Sand Surface"), SurfaceRole.Ground, AssetCategories.SurfaceSand);
                        int j = 0;
                        for (float u = u0 + 4f; u <= u1 - 4f; u += 8f)
                            for (float v = sv > 0 ? half + 6f : -eb + 7f; v <= (sv > 0 ? eb - 7f : -half - 6f); v += 8f, j++)
                                MarketProp(k, k.W(u, v), sv > 0 ? -k.S : k.S, PlayKit[(j + (sv > 0 ? 0 : 3)) % PlayKit.Length], AssetCategories.Playground, 3.2f);
                        foreach (var p in LeisureKit.Along(k.W(u0 + 3f, sv * (half + 1.2f)), k.W(u1 - 3f, sv * (half + 1.2f)), 4f)) k.Seat(p, k.S * sv);
                    }
                }
                else
                {
                    k.Tree(k.W((u0 + u1) * 0.5f, 0f), false, Spruces, 4f);
                    foreach (float sv in new[] { -1f, 1f })
                        MarketProp(k, k.W((u0 + u1) * 0.5f + 3f, sv * (half + 2f)), k.A, "Snowman0" + (1 + (int)(Hashing.Unit(k.V(0xFA30u)) * 3f) % 3), AssetCategories.EventChristmas, 0.9f);
                    // stalls round the big tree instead of a forest: a little Christmas village at the far end
                    foreach (float sv in new[] { -1f, 1f })
                        for (float u = u0 + 3f; u <= u1 - 3f; u += 5.2f)
                            k.Stall(k.W(u, sv * (half + 6f)), -k.S * sv);
                    foreach (float sv in new[] { -1f, 1f }) k.Tree(k.W(u0 + 2.5f, sv * (eb - 4f)), false, Spruces, 1.8f);
                }
            }
            // trees along the loop
            if (!winter) k.RingTrees(fw + 1.7f, 13f, false, 0, true);
            else foreach (var p in LeisureKit.Along(k.W(-ea, -eb - 1.5f), k.W(ea, -eb - 1.5f), 14f)) k.Light(p);
            k.Core = half;
            k.Ctx.Plan.Facts["fairStalls"] = stalls;
            k.Ctx.Plan.Facts["stalls"] = stalls;
        }
    }
}
