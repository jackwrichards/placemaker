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
            // (round 3, the lead: "darker, patched, mismatched surfaces") decades of repairs: rectangles of other concrete,
            // asphalt and broken tiles laid over the slabs, more toward the edges and the busy side
            {
                var patchIds = new[] { new AssetRef(GroundSurfaces.VAsphalt), new AssetRef(GroundSurfaces.VAsphalt), new AssetRef(GroundSurfaces.VAsphalt), new AssetRef(GroundSurfaces.VConcrete) };   // dark repairs: pale ones read as white boxes from 45 degrees
                int patches = math.clamp((int)(ia * ib / 160f), 4, 46);
                for (int i = 0; i < patches; i++)
                {
                    float pw2 = math.lerp(3f, 9f, Hashing.Unit(k.V(0x9A01u))), ph = math.lerp(2.5f, 7f, Hashing.Unit(k.V(0x9A02u)));
                    float cu = math.lerp(-ia + pw2, ia - pw2, Hashing.Unit(k.V(0x9A03u))), cv = math.lerp(-ib + ph, ib - ph, Hashing.Unit(k.V(0x9A04u)));
                    // snapped to the slab grid (repairs follow the joints)
                    cu = math.round(cu / 4f) * 4f; cv = math.round(cv / 4f) * 4f;
                    k.Lay(PPattern - 2, k.Box(cu - pw2, cu + pw2, cv - ph, cv + ph).Ring, patchIds[(int)(Hashing.Unit(k.V(0x9A05u)) * 4f) % 4]);
                }
            }

            // The pit: 0.9 m down, its banks paved (skaters ride them), a ledge on the long rims, a stair at the -A end.
            float pu = math.clamp(ia * 0.36f, 9f, 30f), pv = math.clamp(ib * 0.34f, 6.5f, 21f);
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
            if (fu > 14f)
            {
                // a big floor: a half pipe at the far end, rails and ledges in rows, a second ramp
                Prop(k, k.W(-fu + 4f, 0f), k.Phase + math.PI * 0.5f, "SkateParkHalfPipe02", AssetCategories.SportsSkate, 6f);
                for (int i = 0; i < 4; i++)
                {
                    float uu = math.lerp(-fu * 0.55f, fu * 0.7f, i / 3f);
                    Prop(k, k.W(uu, (i & 1) == 0 ? fv * 0.7f : -fv * 0.7f), k.Phase + 0.3f * (i - 1.5f), i % 2 == 0 ? AssetMix.Pick(SkateRails, k.V(0x5CA3u)).Id : AssetMix.Pick(SkateLedges, k.V(0x5CA4u)).Id, AssetCategories.SportsSkate, 1.6f);
                }
                if (fv > 9f) Prop(k, k.W(fu * 0.1f, -fv * 0.2f), k.Phase, "SkateParkRamp03", AssetCategories.SportsSkate, 3.5f);
            }
            LeisureKit.TakeRect(k.Occ, k.Box(-pu, pu, -pv, pv), 0);
            k.Core = math.min(pu, pv);
            if (k.FountainChild) k.CentreClear(k.C, math.min(pu, pv));

            // (round 3, the lead: "an even grid of about 200 identical planters ... compose big sites") the pit is the one main
            // space, framed by a ring of big planter boxes with gaps on the axes; each quarter beyond it is a room of its own
            // (the kiosk court at the busiest gate, a fenced-off broken corner, a poster-wall court, an unkept garden); the
            // rest is open ground.
            int pl = 0;
            float ru = pu + 8f, rv = pv + 7.5f;
            for (int side = 0; side < 4; side++)
            {
                bool alongU = side < 2; float sg = (side & 1) == 0 ? -1f : 1f;
                float len = alongU ? ru : rv;
                for (float t = -len + 3f; t <= len - 3f + 0.01f; t += 7.5f)
                {
                    if (math.abs(t) < 5f) continue;   // the axes stay open
                    var c = alongU ? k.W(t, sg * rv) : k.W(sg * ru, t);
                    if (GrungyPlanter(k, c, 2.8f, pl, true)) pl++;
                }
            }
            var gateP = k.Gates.Count > 0 ? k.Local(k.Gates[0].Position) : new float2(-ia, ib);
            var kinds = new List<string> { "garden", "fenced", "posters" };
            int rot = (int)(Hashing.Unit(k.V(0x9A10u)) * 3f) % 3;
            int rooms = 0;
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                {
                    // the quarter's room: beyond the planter ring, toward the corner
                    float u0 = su * 3f, u1 = su * (ia - 2f), v0 = sv * (rv + 5f), v1 = sv * (ib - 2f);
                    if (math.abs(v1 - v0) < 12f) { u0 = su * (ru + 5f); v0 = sv * 3f; }
                    if (math.abs(u1 - u0) < 12f || math.abs(v1 - v0) < 10f) continue;
                    float lu0 = math.min(u0, u1), lu1 = math.max(u0, u1), lv0 = math.min(v0, v1), lv1 = math.max(v0, v1);
                    // a room no larger than ~34 x 26 m, set toward the corner: the rest of the quarter stays open
                    float wu = math.min(lu1 - lu0, 34f), wv = math.min(lv1 - lv0, 26f);
                    if (su > 0) lu0 = lu1 - wu; else lu1 = lu0 + wu;
                    if (sv > 0) lv0 = lv1 - wv; else lv1 = lv0 + wv;
                    bool busy = math.sign(gateP.x) == su && math.sign(gateP.y) == sv;
                    string kind = busy ? "kiosks" : kinds[(rot + rooms) % 3];
                    GrungyRoom(k, kind, lu0, lu1, lv0, lv1, su, sv);
                    if (!busy) rooms++;
                }
            k.Ctx.Plan.Facts["grungyPlanters"] = pl;
            k.Ctx.Plan.Facts["grungyRooms"] = rooms;
            // the dry fountain: a basin of walls on the cross axis, no water for years, weeds and litter in it
            if (ia > pu + 18f)
            {
                var fc = k.W(-(pu + 8f + ia) * 0.5f - 1f, 0f);
                float ap = 3.2f;
                if (k.Occ.Free(fc, ap + 1f, 0.2f, 0.1f))
                {
                    float fr = ap / math.cos(math.PI / 8f);
                    k.Lay(PFeature - 6, PavingShapes.Polygon(fc, ap - 0.25f, 8, k.Phase + math.PI / 8f), k.P.Inlay);
                    for (int i = 0; i < 8; i++)
                    {
                        float a0 = k.Phase + math.PI / 8f + i * math.PI / 4f, a1 = a0 + math.PI / 4f;
                        k.Wall(fc + new float2(math.cos(a0), math.sin(a0)) * fr, fc + new float2(math.cos(a1), math.sin(a1)) * fr, 1, null, 0.35f, false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        float a = Hashing.Unit(k.V(0x6A13u)) * 6.28f;
                        k.Ctx.Object(fc + new float2(math.cos(a), math.sin(a)) * 1.4f, a, ObjectRole.Bush, AssetMix.Pick(i == 0 ? DeadBushes : WildBushes, k.V(0x6A14u)), k.V(0x6A15u), -1, AssetCategories.RuinsWeeds);
                    }
                    k.Occ.Take(fc, fr + 1.2f, 0);
                    k.Ctx.Plan.Facts["dryFountain"] = 1;
                }
            }
            k.Ctx.Plan.Facts["grungyPlanters"] = pl;

            // bins with bags heaped round them along the walk round the pit
            BinWithBags(k, k.W(ru - 2f, rv + 3f));
            BinWithBags(k, k.W(-ru + 2f, -rv - 3f));
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

        private static readonly string[] Rubble = { "RubblePileConcrete01", "RubblePileMixed02", "RubblePileConcrete03", "RubblePileMixed01" };
        private static readonly string[] Cones = { "SafetyCone01", "SafetyCone02", "SafetyCone03" };

        /// <summary>One room of the grungy square in a box (local u0..u1, v0..v1); su, sv point to the square's corner.</summary>
        private static void GrungyRoom(Kit k, string kind, float u0, float u1, float v0, float v1, float su, float sv)
        {
            var box = k.Box(u0, u1, v0, v1);
            float cu = (u0 + u1) * 0.5f, cv = (v0 + v1) * 0.5f;
            var c = k.W(cu, cv);
            var inward = math.normalizesafe(k.C - c, k.A);
            var side = new float2(-inward.y, inward.x);
            float yIn = math.atan2(inward.x, inward.y);
            switch (kind)
            {
                case "kiosks":
                {
                    // where people walk in: two kiosks back to back, a food cart, newspaper boxes, tables, gum, bins
                    k.Lay(PFeature + 4, box.Ring, new AssetRef(GroundSurfaces.VConcretePale, GroundSurfaces.VConcrete));
                    k.Stall(c - side * 3f, inward, true);
                    k.Stall(c + side * 3.4f, inward, true);
                    Prop(k, c + inward * 6f + side * 5f, yIn, AssetMix.Pick(Carts, k.V(0x9B01u)).Id, AssetCategories.Kiosk, 2.3f);
                    for (int i = 0; i < 3; i++) Prop(k, c + inward * 3.2f - side * (6.5f + i * 0.8f), yIn, AssetMix.Pick(Newspapers, k.V(0x9B02u)).Id, AssetCategories.Kiosk, 0.45f);
                    Prop(k, c + inward * 4.2f + side * 0.5f, yIn, "AStand02 - ShortMarket", AssetCategories.Kiosk, 0.5f);
                    for (int i = 0; i < 4; i++) k.CafeSet(c + inward * (7.5f + (i / 2) * 3.4f) + side * ((i & 1) == 0 ? -3f : 1.5f), yIn + i, false, true);
                    Prop(k, c - inward * 3.5f + side * 1f, yIn, AssetMix.Pick(Boxes, k.V(0x9B03u)).Id, AssetCategories.RuinsJunk, 0.9f);
                    Prop(k, c - inward * 3.5f - side * 4f, yIn + 1.57f, "TrashContainerEmpty02", "industrial.skip", 1.6f);
                    BinWithBags(k, c + inward * 2f - side * 9f);
                    k.Light(c + inward * 5f - side * 1.5f);
                    break;
                }
                case "fenced":
                {
                    // closed for works years ago: broken asphalt and dirt behind safety fencing, rubble, cones, weeds
                    var inner = k.Box(u0 + 1f, u1 - 1f, v0 + 1f, v1 - 1f);
                    k.Lay(PFeature + 4, inner.Ring, new AssetRef(GroundSurfaces.VAsphalt), SurfaceRole.Asphalt, AssetCategories.SurfaceAsphalt);
                    var r = inner.Ring;
                    for (int e = 0; e < 4; e++)
                    {
                        var a0 = r[e]; var a1 = r[(e + 1) % 4];
                        float len = math.distance(a0, a1); var d = (a1 - a0) / len;
                        for (float t = 1.1f; t < len - 1f; t += 2.2f)
                        {
                            if (e == 0 && math.abs(t - len * 0.5f) < 2.4f) continue;   // the gate left open
                            if (Hashing.Unit(k.V(0x9B10u)) < 0.08f) continue;           // a fence panel gone
                            k.Ctx.Object(a0 + d * t, math.atan2(d.x, d.y) + 1.5708f, ObjectRole.Furniture, new AssetRef("SafetyBarrier0" + (1 + (int)(Hashing.Unit(k.V(0x9B11u)) * 3f) % 3), "SafetyBarrier01"), k.V(0x9B12u), -1, AssetCategories.ConstructionBarrier);
                        }
                    }
                    int piles = math.clamp((int)((u1 - u0) * (v1 - v0) / 90f), 2, 7);
                    for (int i = 0; i < piles; i++)
                    {
                        var q = k.W(math.lerp(u0 + 3f, u1 - 3f, Hashing.Unit(k.V(0x9B13u))), math.lerp(v0 + 3f, v1 - 3f, Hashing.Unit(k.V(0x9B14u))));
                        Prop(k, q, Hashing.Unit(k.V(0x9B15u)) * 6.28f, AssetMix.Pick(Rubble, k.V(0x9B16u)).Id, "ruins.rubble", 2.2f);
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        var q = k.W(math.lerp(u0 + 2f, u1 - 2f, Hashing.Unit(k.V(0x9B17u))), math.lerp(v0 + 2f, v1 - 2f, Hashing.Unit(k.V(0x9B18u))));
                        if (i < 3) Prop(k, q, 0f, AssetMix.Pick(Cones, k.V(0x9B19u)).Id, AssetCategories.ConstructionCone, 0.4f);
                        else Prop(k, q, Hashing.Unit(k.V(0x9B1Au)) * 6.28f, AssetMix.Pick(WildBushes, k.V(0x9B1Bu)).Id, AssetCategories.RuinsWeeds, 0.9f);
                    }
                    Prop(k, k.W(u0 + 3f, v0 + 3f), 0.4f, "ConcreteBarrier01", AssetCategories.ConstructionBarrier, 0.7f);
                    break;
                }
                case "posters":
                {
                    // a court of free-standing walls plastered with posters, a dumped sofa, a burning barrel, bins with bags
                    int walls = (v1 - v0) > 18f ? 3 : 2;
                    for (int w = 0; w < walls; w++)
                    {
                        float vv = math.lerp(v0 + 3f, v1 - 3f, walls == 1 ? 0.5f : w / (float)(walls - 1));
                        float wl = math.min(u1 - u0 - 6f, 14f) * (w == 1 ? 0.7f : 1f);
                        float s0 = cu - wl * 0.5f + (w == 1 ? su * 3f : 0f);
                        var wa = k.W(s0, vv); var wb = k.W(s0 + wl, vv);
                        k.Wall(wa, wb, 2);
                        var d = math.normalizesafe(wb - wa); var n = new float2(-d.y, d.x);
                        for (float t = 1.2f; t < wl - 1f; t += 2.4f)
                            foreach (float sd in new[] { -1f, 1f })
                            {
                                if (Hashing.Unit(k.V(0x9051u)) < 0.15f) continue;
                                var o = k.Ctx.Object(wa + d * t + n * (sd * 0.12f), math.atan2(n.x * sd, n.y * sd), ObjectRole.Furniture,
                                    new AssetRef(AssetMix.Pick(Posters, k.V(0x9050u)).Id, "PosterSmall01 - ShortMarket"), k.V(0x9052u), -1, AssetCategories.Kiosk);
                                o.Elevation = math.lerp(0.4f, 1.1f, Hashing.Unit(k.V(0x9053u)));
                            }
                    }
                    Prop(k, k.W(cu, v0 + 1.6f), yIn, "OutdoorSofa02", AssetCategories.RuinsJunk, 1.2f);
                    Prop(k, k.W(cu + 3f, v0 + 1.8f), 0f, "BarrelFire01", AssetCategories.RuinsJunk, 0.5f);
                    Prop(k, k.W(cu - 3.5f, v0 + 1.5f), 0.7f, "Pallet03", AssetCategories.RuinsJunk, 0.7f);
                    BinWithBags(k, k.W(u0 + 2f, cv));
                    BinWithBags(k, k.W(u1 - 2f, cv));
                    k.Light(k.W(cu, v1 - 1.5f));
                    break;
                }
                default:
                {
                    // the garden nobody keeps: rough grass in a broken low wall, self-sown trees, wild and dead bushes, a bench
                    var inner = k.Box(u0 + 1f, u1 - 1f, v0 + 1f, v1 - 1f);
                    k.Lay(PGreen, inner.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                    var r = inner.Ring;
                    for (int e = 0; e < 4; e++)
                    {
                        var a0 = r[e]; var a1 = r[(e + 1) % 4];
                        float len = math.distance(a0, a1); var d = (a1 - a0) / len;
                        for (float t = 0f; t < len - 1f; t += 5f)
                            if (Hashing.Unit(k.V(0x9B20u)) > 0.3f) k.Wall(a0 + d * t, a0 + d * math.min(len, t + 5f), 1, null, 0.5f, false);
                    }
                    int n = math.clamp((int)((u1 - u0) * (v1 - v0) / 60f), 4, 14);
                    for (int i = 0; i < n; i++)
                    {
                        var q = k.W(math.lerp(u0 + 3f, u1 - 3f, Hashing.Unit(k.V(0x9B21u))), math.lerp(v0 + 3f, v1 - 3f, Hashing.Unit(k.V(0x9B22u))));
                        if (i % 3 == 0) { uint tv = k.V(0x9B23u); if (k.Occ.Free(q, 2.5f, 0.2f, 0.1f)) { k.Ctx.Object(q, Hashing.Unit(tv) * 6.28f, ObjectRole.Tree, AssetMix.Pick(PlazaArt.PlaneTrees, tv), tv, i % 2 == 0 ? 1 : 4, null); k.Occ.Take(q, 2.5f, 3); k.TreeCount++; } }
                        else Prop(k, q, Hashing.Unit(k.V(0x9B24u)) * 6.28f, AssetMix.Pick(i % 4 == 1 ? DeadBushes : WildBushes, k.V(0x9B25u)).Id, AssetCategories.RuinsWeeds, 1f);
                    }
                    k.Seat(k.W(cu, v0 - 1.2f * sv), -k.S * sv, PlazaArt.ModernBenches);
                    break;
                }
            }
            k.Ctx.Plan.Facts["grungyRoom_" + kind] = 1;
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
        private static bool GrungyPlanter(Kit k, float2 c, float h, int index, bool ring = false)
        {
            var box = new OrientedRect { Center = c, Axis = k.A, HalfLength = h, HalfWidth = h };
            if (!k.Fits(box, 1.2f) || !k.Occ.Free(c, ring ? h : h * 1.2f, ring ? 0.1f : 0.3f, ring ? 0.05f : 0.2f) || k.NearGate(c, ring ? h + 1f : h + 4f)) return false;
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
