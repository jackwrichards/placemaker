// The grungy square (the plazas makeover, 2026-10-29, docs/runbooks/plazas.md). The user: "a grungy plaza or a nice
// plaza ... some of them might be more than just what settings can do"; "neglect is more than stripping away features or
// adding features. It changes the entire vibe of everything." So this is not a tidy square with dirt on it: it is a
// late-sixties civic plaza nobody has looked after since, with its own contents.
//
//   ground     concrete slabs on a grid of dark joints (every joint is a seam the ground design fills with grime and
//              weeds), a dark band round the edge
//   the pit    a sunken court 0.9 m deep in the middle with paved banks: the skaters' place (ledge boxes, rails, a small
//              ramp, a board left lying), a stair down at one end, a concrete ledge along its rim
//   planters   square concrete planter boxes round the pit, a tree in each (one in three dead or still a sapling), wild
//              bushes and dead shrubs in them, tired benches against some of their sides
//   the kiosk  a kiosk by the busiest entrance with newspaper boxes, an A-board, cardboard piled at its back, a skip,
//              bin bags heaped round the bins
//   the wall   a free-standing wall plastered with posters on both faces, an old sofa dumped against it
//   the rest   jersey barriers shutting an old service entrance, wild growth taking the corners
//
// The plan's fact "grungy" tells the ground design (Core/Decals/Dynamic/PlazaGround.cs) to lay its neglect: skate scuffs,
// weeds, concrete stains, gum, litter in the corners. Every prop id is vanilla (ModsData catalog-dump, 2026-10-29).

using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        private static readonly string[] SkateLedges = { "SkateParkBox02", "SkateParkBox01" };
        private static readonly string[] SkateRails = { "SkateParkRail01", "SkateParkRail02" };
        private static readonly string[] WildBushes = { "GreenBushWildTeen01", "GreenBushWildAdult01", "GreenBushWildElderly01", "GreenBushWildChild01" };   // (2026-10-30) no FlowerBushWild01 / Elderly01 / Child02: they draw pale teal-blue in the game
        private static readonly string[] DeadBushes = { "FlowerBushWildDead02" };
        private static readonly string[] Posters = { "PosterSmall01 - ShortMarket", "PosterSmall02 - ShortMarket", "BillboardWallSmall03 - ShortMarket", "BillboardWallSmall01 - ShortMarket", "PosterMedium02 - ShortMarket" };
        private static readonly string[] BinBags = { "Trashbag01", "Trashbag02", "Trashbag03", "Trashbag04" };
        private static readonly string[] Boxes = { "CardboardBoxPile01", "CardboardBoxPile03", "CardboardBox02", "CardboardBoxPile02" };
        private static readonly string[] Newspapers = { "NewspaperVendingMachine01", "NewspaperVendingMachine02", "NewspaperVendingMachine03" };

        /// <summary>The grungy square's ground on Automatic: weathered dark concrete, dark tile bands (vanilla fallbacks first in the clean game).</summary>
        internal static readonly PavingPalette GrungyPalette = new PavingPalette
        {
            Id = "grungy",
            Field = new AssetRef(GroundSurfaces.VTilesDark, GroundSurfaces.VConcrete),
            Band = new AssetRef(GroundSurfaces.VAsphalt, GroundSurfaces.VTilesDark),
            Accent = new AssetRef(GroundSurfaces.VConcrete, GroundSurfaces.VConcretePale),
            Inlay = new AssetRef(GroundSurfaces.VAsphalt, GroundSurfaces.VTilesDark),
            Path = new AssetRef(GroundSurfaces.VConcrete, GroundSurfaces.VConcretePale),
        };

        private static void Grungy(Kit k)
        {
            float fw = k.Fw;
            k.Leftover(fw + 3f, "wild");
            k.WallId = "EU_Wall05";
            k.Ctx.Plan.Facts["grungy"] = 1;
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            float ia = k.HA - fw - 3f, ib = k.HB - fw - 3f;
            if (ia < 14f || ib < 12f) { GrungySmall(k, ia, ib); return; }

            // The slab grid: dark joints every 6 m both ways over the whole square (pieces the ground design reads as seams).
            var joints = new List<float2[]>();
            float reach = math.length(k.Region.Max - k.Region.Min);
            for (float t = 4f; t < reach; t += 8f)
                foreach (float s in new[] { -1f, 1f })
                {
                    joints.Add(LayerRegion.Rect(k.W(s * t, 0f), k.S, reach, 0.25f));
                    joints.Add(LayerRegion.Rect(k.W(0f, s * t), k.A, reach, 0.25f));
                }
            k.Lay(PPattern, joints, k.P.Inlay);

            // The pit: 0.9 m down, its banks paved (skaters ride them), a ledge on the long rims, a stair at the -A end.
            float pu = math.clamp(ia * 0.34f, 9f, 18f), pv = math.clamp(ib * 0.32f, 6.5f, 11f);
            float b = BankFor(WallH);
            var lv = new Levels(k, k.EA - 1f, k.EB - 1f);
            lv.Add(-pu, pu, -pv, pv, -WallH, b);
            lv.Emit();
            var gaps = new List<Gap> { new Gap(0, 0f, 2.4f) };
            Edges(k, -pu, pu, -pv, pv, B4(b), true, Head.None, gaps, k.P.Field);
            var floor = k.Box(-pu + b, pu - b, -pv + b, pv - b);
            k.Lay(PFeature, floor.Ring, k.P.Path);
            k.Flight(k.W(-pu + b + 0.2f, 0f), k.W(-pu - 0.4f, 0f), 4.4f, true, false);
            // the ledge: a low concrete wall along both long rims, open in the middle
            foreach (float sv in new[] { -1f, 1f })
            {
                k.Wall(k.W(-pu + 1f, sv * (pv + 0.6f)), k.W(-2.5f, sv * (pv + 0.6f)), 1, null, 0.45f);
                k.Wall(k.W(2.5f, sv * (pv + 0.6f)), k.W(pu - 1f, sv * (pv + 0.6f)), 1, null, 0.45f);
            }
            // skate pieces on the floor
            float fu = pu - b - 2f, fv = pv - b - 1.5f;
            Prop(k, k.W(-fu * 0.35f, 0f), k.Phase + math.PI * 0.5f, AssetMix.Pick(SkateLedges, k.V(0x5CA1u)).Id, AssetCategories.SportsSkate, 1.8f);
            Prop(k, k.W(fu * 0.3f, fv * 0.45f), k.Phase + math.PI * 0.5f, AssetMix.Pick(SkateRails, k.V(0x5CA2u)).Id, AssetCategories.SportsSkate, 1.4f);
            Prop(k, k.W(fu * 0.3f, -fv * 0.45f), k.Phase + math.PI * 0.5f, "SkateParkBox01", AssetCategories.SportsSkate, 1.2f);
            if (fu > 8f) Prop(k, k.W(fu - 1.5f, 0f), k.Phase - math.PI * 0.5f, "SkateParkRamp01", AssetCategories.SportsSkate, 2.4f);
            Prop(k, k.W(-fu * 0.1f, fv * 0.6f), k.Phase + 0.4f, "Skateboard01", AssetCategories.SportsSkate, 0.4f);
            LeisureKit.TakeRect(k.Occ, k.Box(-pu, pu, -pv, pv), 0);
            k.Core = math.min(pu, pv);
            if (k.FountainChild) k.CentreClear(k.C, math.min(pu, pv));

            // Planters round the pit: 5 m concrete boxes, a tree each, wild and dead bushes, tired benches on some sides.
            int pl = 0;
            for (float v = -ib + 5f; v <= ib - 5f + 0.01f; v += 12f)
                for (float u = -ia + 6f; u <= ia - 6f + 0.01f; u += 12f)
                {
                    // the walk round the pit and the cross walks on the axes stay open
                    if (math.abs(u) < pu + 6f && math.abs(v) < pv + 5f) continue;
                    if (math.abs(v) < 3.5f || math.abs(u) < 3.5f) continue;
                    if (Hashing.Unit(k.V(0x6A10u)) < 0.18f) continue;   // a box taken out long ago: a darker square left in the slabs
                    float jitter = (Hashing.Unit(k.V(0x6A11u)) - 0.5f) * 2f;
                    if (GrungyPlanter(k, k.W(u + jitter, v), Hashing.Unit(k.V(0x6A12u)) < 0.3f ? 3.4f : 2.5f, pl)) pl++;
                }
            // the dry fountain: a basin of walls on the cross axis, no water for years, weeds and litter in it
            if (ia > pu + 18f)
            {
                var fc = k.W(-(pu + ia) * 0.5f - 2f, 0f);
                float ap = 3.2f;
                if (k.Occ.Free(fc, ap + 1f, 0.2f, 0.1f))
                {
                    float rv = ap / math.cos(math.PI / 8f);
                    k.Lay(PFeature - 6, PavingShapes.Polygon(fc, ap - 0.25f, 8, k.Phase + math.PI / 8f), k.P.Inlay);
                    for (int i = 0; i < 8; i++)
                    {
                        float a0 = k.Phase + math.PI / 8f + i * math.PI / 4f, a1 = a0 + math.PI / 4f;
                        k.Wall(fc + new float2(math.cos(a0), math.sin(a0)) * rv, fc + new float2(math.cos(a1), math.sin(a1)) * rv, 1, null, 0.35f, false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        float a = Hashing.Unit(k.V(0x6A13u)) * 6.28f;
                        k.Ctx.Object(fc + new float2(math.cos(a), math.sin(a)) * 1.4f, a, ObjectRole.Bush, AssetMix.Pick(i == 0 ? DeadBushes : WildBushes, k.V(0x6A14u)), k.V(0x6A15u), -1, AssetCategories.RuinsWeeds);
                    }
                    k.Occ.Take(fc, rv + 1.2f, 0);
                    k.Ctx.Plan.Facts["dryFountain"] = 1;
                }
            }
            k.Ctx.Plan.Facts["grungyPlanters"] = pl;

            // The kiosk at the busiest entrance (the first gate), facing into the square, with its clutter.
            var gate = k.Gates.Count > 0 ? k.Gates[0].Position : k.W(-ia, ib * 0.5f);
            var inward = math.normalizesafe(k.C - gate, k.A);
            var kp = gate + inward * 9f + new float2(-inward.y, inward.x) * 5f;
            if (k.Region.ContainsDisc(kp, 4f) && k.Stall(kp, inward, true))
            {
                var side = new float2(-inward.y, inward.x);
                Prop(k, kp + side * 3f + inward * 1f, math.atan2(inward.x, inward.y), AssetMix.Pick(Newspapers, k.V(0xBE01u)).Id, AssetCategories.Kiosk, 0.5f);
                Prop(k, kp + side * 3.8f + inward * 1f, math.atan2(inward.x, inward.y), AssetMix.Pick(Newspapers, k.V(0xBE02u)).Id, AssetCategories.Kiosk, 0.5f);
                Prop(k, kp - side * 3f + inward * 1.6f, math.atan2(inward.x, inward.y), "AStand01 - ShortMarket", AssetCategories.Kiosk, 0.5f);
                Prop(k, kp - inward * 2.4f + side * 1f, math.atan2(side.x, side.y), AssetMix.Pick(Boxes, k.V(0xBE03u)).Id, AssetCategories.RuinsJunk, 0.9f);
                Prop(k, kp - inward * 2.4f - side * 1.2f, math.atan2(side.x, side.y), AssetMix.Pick(Boxes, k.V(0xBE04u)).Id, AssetCategories.RuinsJunk, 0.9f);
                Prop(k, kp - inward * 3f - side * 3.8f, math.atan2(side.x, side.y), "TrashContainerEmpty02", "industrial.skip", 1.6f);
                BinWithBags(k, kp + side * 4.6f - inward * 0.4f);
            }
            // bins with bags heaped round them on the far side too
            BinWithBags(k, k.W(ia - 2f, ib - 2f));
            BinWithBags(k, k.W(-ia * 0.3f, -(pv + 3f)));

            // The poster wall: two courses, posters on both faces, a dumped sofa at its foot.
            float wv = -(ib - 1.5f);
            if (math.abs(wv) > pv + 13f || ib > pv + 12f)
            {
                var wa = k.W(ia * 0.15f, wv); var wb = k.W(ia * 0.15f + math.min(12f, ia * 0.6f), wv);
                k.Wall(wa, wb, 2);
                float len = math.distance(wa, wb);
                var d = (wb - wa) / len; var n = new float2(-d.y, d.x);
                int i = 0;
                for (float t = 1.4f; t < len - 1f; t += 2.6f, i++)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        if (k.V(0x9051u) % 5 == 0) continue;
                        var o = k.Ctx.Object(wa + d * t + n * (s * 0.12f), math.atan2(n.x * s, n.y * s), ObjectRole.Furniture,
                            new AssetRef(AssetMix.Pick(Posters, k.V(0x9050u)).Id, "PosterSmall01 - ShortMarket"), k.V(0x9052u), -1, AssetCategories.Kiosk);
                        o.Elevation = 1.05f;
                    }
                Prop(k, (wa + wb) * 0.5f + n * 1.4f, math.atan2(-n.x, -n.y), "OutdoorSofa02", AssetCategories.RuinsJunk, 1.2f);
                Prop(k, wb + n * 1.2f + d * 0.6f, 0.7f, "Pallet03", AssetCategories.RuinsJunk, 0.7f);
            }

            // Jersey barriers across an old service entrance (the last gate), leaving a walking gap.
            if (k.Gates.Count > 1)
            {
                var g = k.Gates[k.Gates.Count - 1].Position;
                var gin = math.normalizesafe(k.C - g, k.A); var gs = new float2(-gin.y, gin.x);
                foreach (float t in new[] { -3.2f, -1.9f, 1.9f, 3.2f })
                    Prop(k, g + gin * (fw + 1.6f) + gs * t, math.atan2(gs.x, gs.y), "ConcreteBarrier0" + (1 + (int)(k.V(0xBA77u) % 3)), AssetCategories.ConstructionBarrier, 0.6f);
            }
            // Lamps along the pit's rims.
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f }) k.Light(k.W(su * (pu + 2f), sv * (pv + 2f)));
        }

        /// <summary>A small grungy square: the planter boxes and the kiosk only.</summary>
        private static void GrungySmall(Kit k, float ia, float ib)
        {
            int pl = 0;
            foreach (float su in new[] { -1f, 1f })
                if (GrungyPlanter(k, k.W(su * math.max(3f, ia * 0.5f), 0f), math.clamp(ib * 0.35f, 1.6f, 2.5f), pl)) pl++;
            BinWithBags(k, k.W(0f, math.max(1f, ib - 1.5f)));
            k.Ctx.Plan.Facts["grungyPlanters"] = pl;
        }

        /// <summary>A square concrete planter box (half size <paramref name="h"/>): a tree, wild growth, a bench or two.</summary>
        private static bool GrungyPlanter(Kit k, float2 c, float h, int index)
        {
            var box = new OrientedRect { Center = c, Axis = k.A, HalfLength = h, HalfWidth = h };
            if (!k.Fits(box, 1.2f) || !k.Occ.Free(c, h * 1.2f, 0.3f, 0.2f) || k.NearGate(c, h + 4f)) return false;
            var r = box.Ring;
            for (int i = 0; i < 4; i++) k.Wall(r[i], r[(i + 1) % 4], 1, null, 0.4f, false);
            k.Lay(PFeature, new OrientedRect { Center = c, Axis = k.A, HalfLength = h - 0.2f, HalfWidth = h - 0.2f }.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
            uint v = k.V(0x6A01u);
            int age = (index % 3) == 1 ? 4 : (index % 3) == 2 ? 0 : 2;   // one in three dead, one a sapling
            k.Ctx.Object(c, Hashing.Unit(v) * 6.28f, ObjectRole.Tree, AssetMix.Pick(PlazaArt.PlaneTrees, v), v, age, null);
            k.TreeCount++;
            for (int j = 0; j < 3; j++)
            {
                float a = Hashing.Unit(k.V(0x6A02u)) * 6.28f;
                var p = c + new float2(math.cos(a), math.sin(a)) * (h * 0.62f);
                bool dead = Hashing.Unit(k.V(0x6A03u)) < 0.35f;
                k.Ctx.Object(p, a, ObjectRole.Bush, AssetMix.Pick(dead ? DeadBushes : WildBushes, k.V(0x6A04u)), k.V(0x6A05u), -1, AssetCategories.RuinsWeeds);
            }
            k.Occ.Take(c, h + 0.4f, 0);
            // tired benches against one or two sides, looking out
            for (int s = 0; s < 4; s++)
            {
                if (Hashing.Unit(k.V(0x6A06u)) > 0.4f) continue;
                var m = (r[s] + r[(s + 1) % 4]) * 0.5f;
                var outN = math.normalizesafe(m - c);
                k.Seat(m + outN * 1.1f, outN, PlazaArt.ModernBenches);
            }
            return true;
        }

        private static void BinWithBags(Kit k, float2 p)
        {
            if (!k.Region.ContainsDisc(p, 1.5f) || !k.Occ.Free(p, 1.2f, 0.2f, 0.1f)) return;
            k.Ctx.Object(p, 0f, ObjectRole.Bin, new AssetRef("Trashbin02", "Trashbin01"), k.V(0xB1B0u), -1, AssetCategories.Bin);
            for (int i = 0; i < 3; i++)
            {
                float a = Hashing.Unit(k.V(0xB1B1u)) * 6.28f;
                k.Ctx.Object(p + new float2(math.cos(a), math.sin(a)) * 0.85f, a, ObjectRole.Furniture, AssetMix.Pick(BinBags, k.V(0xB1B2u)), k.V(0xB1B3u), -1, AssetCategories.RuinsJunk);
            }
            k.Occ.Take(p, 1.3f, 0);
        }

        private static bool Prop(Kit k, float2 p, float yaw, string id, string category, float r) =>
            k.Region.ContainsDisc(p, r) && LeisureKit.Put(k.Ctx, k.Occ, p, yaw, ObjectRole.Furniture, new AssetRef(id), category, r, k.V(0x9909u), 0.2f, 0.1f, 1);
    }
}
