// Big sites (the plazas makeover, round 3, 2026-10-30; the lead: "big sites must be composed, not a small module tiled
// across. Use hierarchy: one main space, a few distinct rooms, edges with their own life, and leave real open ground").
// A designed square keeps the size of a real square (at most about 140 x 124 m with its margins) in the middle of a big
// site; the ground round it gets a surround of its own in the design's manner:
//
//   park       lawn with trees in loose clusters, a walk round the square with lamps, benches looking in, a path in from
//              every entrance (the stone theatre, the fair, the classic squares)
//   promenade  paving with rows of trees in pits parallel to the square, benches between, cafe corners (the market)
//   wild       rough grass with wild and dead bushes, self-sown trees, a dumped skip, jersey barriers (the grungy square)
//
// The surround is laid after the design at the field's priority, so every frame, promenade and ring of trees the design
// lays along the site's own outline stays where it is.

using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        /// <summary>Largest half sizes of a design's box (margins included).</summary>
        internal const float CapA = 70f, CapB = 62f;

        private static string SurroundManner(string design) =>
            design == "grungy" ? "wild" : design == "market" || design == "winterFair" || design == "marketHall" || design == "dining" ? "promenade" : "park";

        internal sealed partial class Kit
        {
            /// <summary>The box before the cap (the design's box on a site of normal size).</summary>
            public float FullHA, FullHB;
            public bool Capped;

            public void CapBox(bool later = false)
            {
                FullHA = HA; FullHB = HB;
                if (later) return;   // the transit forecourt caps itself once it knows where its street is
                if (HA <= CapA + 6f && HB <= CapB + 6f) return;
                // keep the box's proportions near the site's, inside the cap
                HA = math.min(HA, CapA); HB = math.min(HB, CapB);
                Capped = true;
                Ctx.Plan.Facts["designCapped"] = 1;
            }

            /// <summary>Fills the ground between the capped box and the site's outline (see the file header).</summary>
            public void Surround(string manner)
            {
                PlazaDressing.Extents(Region, C, A, out float minA, out float maxA, out float minB, out float maxB);
                float ha = EA - 1f, hb = EB - 1f;   // (round 4) right at the square's frame: no pale margin band
                var strips = new List<float2[]>
                {
                    Box(minA - 2f, maxA + 2f, hb, maxB + 2f).Ring, Box(minA - 2f, maxA + 2f, minB - 2f, -hb).Ring,
                    Box(ha, maxA + 2f, -hb, hb).Ring, Box(minA - 2f, -ha, -hb, hb).Ring,
                };
                var inner = Region.InsetRing(Fw + 1f);
                if (inner == null) return;
                var pieces = new List<float2[]>();
                foreach (var st in strips)
                    foreach (var part in Poly.Intersection(st, inner))
                        if (part?.Outer != null && part.Area > 120f) pieces.Add(Poly.EnsureCcw(part.Outer));
                // the walk round the square, and a path in from every entrance
                var edge = Box(-ha - 3f, ha + 3f, -hb - 3f, hb + 3f);
                if (manner == "park")
                {
                    // the square's deliberate edge: a dark kerb on its frame and the walk round it
                    Lay(PKerb, PavingShapes.Frame(Box(-ha, ha, -hb, hb).Ring, 0.8f), P.Band);
                    Lay(PWalk + 1, PavingShapes.Frame(edge.Ring, 3.6f), P.Path);
                }
                else if (manner == "promenade") Lay(PKerb, PavingShapes.Frame(Box(-ha, ha, -hb, hb).Ring, 0.8f), P.Band);
                foreach (var g in manner == "wild" ? new List<Gate>() : Gates)
                {
                    var l = Local(g.Position);
                    var to = W(math.clamp(l.x, -ha - 1.5f, ha + 1.5f), math.clamp(l.y, -hb - 1.5f, hb + 1.5f));
                    if (math.distance(g.Position, to) > 4f) Lay(PWalk + 1, PavingShapes.Band(g.Position, to, 3.6f), manner == "wild" ? P.Band : P.Path);
                }
                if (manner != "wild") foreach (var e in edge.Ring) Light(e);
                for (int s = 0; s < 4 && manner != "wild"; s++)
                {
                    var a = edge.Ring[s]; var b = edge.Ring[(s + 1) % 4];
                    foreach (var q in LeisureKit.Along(a, b, 20f)) Light(q + math.normalizesafe(q - C) * 2.4f);
                }
                int trees = 0;
                foreach (var piece in pieces)
                {
                    var lr = LayerRegion.From(piece);
                    if (lr == null) continue;
                    if (manner == "promenade")
                    {
                        // paving (the field), rows of trees in pits parallel to the square, benches between them
                        for (float d = 6f; d <= 16f; d += 10f)
                        {
                            var ring = Box(-ha - d, ha + d, -hb - d, hb + d).Ring;
                            bool any = false;
                            for (int s = 0; s < 4; s++)
                                foreach (var q in LeisureKit.Along(ring[s], ring[(s + 1) % 4], 8f))
                                {
                                    if (!lr.ContainsDisc(q, 2.5f)) continue;
                                    any = true;
                                    if (Tree(q, true, PlazaArt.PlaneTrees)) trees++;
                                    var dir = math.normalizesafe(C - q);
                                    if (Hashing.Unit(V(0x5A01u)) < 0.4f) Seat(q + new float2(-dir.y, dir.x) * 4f, dir);
                                    else if (Hashing.Unit(V(0x5A02u)) < 0.15f) CafeSet(q + new float2(-dir.y, dir.x) * 4f, Phase, true, true);
                                }
                        }
                        // beyond the two rows of trees: the park's lawn and clusters (a sea of trees in pits read as a forest)
                        var outer = Box(-ha - 22f, ha + 22f, -hb - 22f, hb + 22f).Ring;
                        var rest = Pieces(piece, new List<float2[]> { outer }, 200f);
                        foreach (var rp in rest) ParkPiece(rp, "park", ref trees);
                        continue;
                    }
                    if (manner == "wild") WildPiece(piece, ref trees); else ParkPiece(piece, manner, ref trees);
                }
                if (manner == "wild") DesirePaths(ha, hb);
                Ctx.Plan.Facts["surroundTrees"] = trees;
            }

            private Placemaker.Core.Paint.PaintKit _paint;

            /// <summary>
            /// (round 4, the lead: "the surround for grungy must be the city's rough edge") cracked asphalt lots and gravel,
            /// a fenced compound of containers, dirt desire paths through rank dry grass, scrub on rubble, dead trees, a poster wall.
            /// No mown lawn: bare terrain painted dry and dirty.
            /// </summary>
            private void WildPiece(float2[] piece, ref int trees)
            {
                var lr = LayerRegion.From(piece);
                if (lr == null) return;
                if (_paint == null)
                {
                    _paint = new Placemaker.Core.Paint.PaintKit(Ctx.Plan.Paint, Ctx.H, Hashing.Mix(Ctx.Seed, 0x51D0u)) { MaxPaints = 200 };
                    Ctx.Issue(IssueSeverity.Warning, TerrainPaint.PermanentKey);
                }
                // rank dry grass over everything, dirt worn through in patches
                _paint.Fill(piece, TerrainMaterials.Dry, 0.85f, 2f, 0.8f);
                _paint.Speckle(piece, TerrainMaterials.Dirt, 0.75f, 9f, 0.35f);
                var mn = lr.Min; var mx = lr.Max;
                float w = mx.x - mn.x, h = mx.y - mn.y;
                // the long axis of the piece, in local frame terms
                var dirLong = w >= h ? new float2(1f, 0f) : new float2(0f, 1f);
                float len = math.max(w, h), dep = math.min(w, h);
                var c = lr.DeepestPoint(out float ins, null, 16);
                if (ins < 6f) return;
                var side = new float2(-dirLong.y, dirLong.x);
                // 1) an informal asphalt lot, cracked (the ground design lays its cracks and stains)
                float lotL = math.min(len * 0.4f, 34f), lotW = math.min(dep - 6f, 18f);
                var lotC = c - dirLong * (len * 0.22f);
                if (lotW > 8f)
                {
                    var lot = LayerRegion.Rect(lotC, dirLong, lotL * 0.5f, lotW * 0.5f);
                    if (Fits(lot, 1f))
                    {
                        Lay(PFeature + 9, lot, new AssetRef(GroundSurfaces.VAsphalt), SurfaceRole.Asphalt, AssetCategories.SurfaceAsphalt);
                        // a skip and pallets at one end, cones from the last market day
                        Prop(this, lotC + dirLong * (lotL * 0.5f - 3f), math.atan2(side.x, side.y), "TrashContainerEmpty03", "industrial.skip", 2f);
                        Prop(this, lotC - dirLong * (lotL * 0.5f - 2f) + side * 3f, 0.5f, "Pallet01", AssetCategories.RuinsJunk, 0.7f);
                        for (int i = 0; i < 3; i++) Prop(this, lotC + side * (i - 1) * 3f, 0f, "SafetyCone0" + (1 + i), AssetCategories.ConstructionCone, 0.4f);
                    }
                }
                // 2) a fenced compound of containers at the other end
                var cc = c + dirLong * (len * 0.25f);
                float cl = math.min(len * 0.3f, 22f), cw = math.min(dep - 6f, 14f);
                if (cw > 8f)
                {
                    var comp = LayerRegion.Rect(cc, dirLong, cl * 0.5f, cw * 0.5f);
                    if (Fits(comp, 1f))
                    {
                        _paint.Fill(comp, TerrainMaterials.Dirt, 1f, 1f, 0.5f);
                        for (int e = 0; e < 4; e++)
                        {
                            var a0 = comp[e]; var a1 = comp[(e + 1) % 4];
                            float el = math.distance(a0, a1); var d = (a1 - a0) / el;
                            for (float t = 1.1f; t < el - 1f; t += 2.2f)
                                if (Hashing.Unit(V(0x51D1u)) > 0.07f)
                                    Ctx.Object(a0 + d * t, math.atan2(d.x, d.y) + 1.5708f, ObjectRole.Furniture, new AssetRef("SafetyBarrier0" + (1 + (int)(Hashing.Unit(V(0x51D2u)) * 3f) % 3), "SafetyBarrier01"), V(0x51D3u), -1, AssetCategories.ConstructionBarrier);
                        }
                        float yawC = math.atan2(dirLong.x, dirLong.y) + 1.5708f;
                        for (int i = 0; i < 3; i++)
                            Prop(this, cc + dirLong * ((i - 1) * 3.2f), yawC, "ShippingContainer0" + (1 + i), "construction.container", 2.8f);
                        Prop(this, cc + side * (cw * 0.3f), 0.3f, AssetMix.Pick(Rubble, V(0x51D4u)).Id, "ruins.rubble", 2.2f);
                    }
                }
                // 3) scrub on rubble and dead or self-sown trees, in loose clumps over the rest
                for (float x = mn.x + 8f; x < mx.x; x += 18f)
                    for (float y = mn.y + 8f; y < mx.y; y += 18f)
                    {
                        var q0 = new float2(x, y) + (new float2(Hashing.Unit(V(0x51D5u)), Hashing.Unit(V(0x51D6u))) - 0.5f) * 8f;
                        if (!lr.ContainsDisc(q0, 4f) || Hashing.Unit(V(0x51D7u)) < 0.35f) continue;
                        if (Hashing.Unit(V(0x51D8u)) < 0.4f) Prop(this, q0, Hashing.Unit(V(0x51D9u)) * 6.28f, AssetMix.Pick(Rubble, V(0x51DAu)).Id, "ruins.rubble", 2f);
                        for (int i = 0; i < 4; i++)
                        {
                            float an = Hashing.Unit(V(0x51DBu)) * 6.28f, rr = 1.5f + Hashing.Unit(V(0x51DCu)) * 4f;
                            var q = q0 + new float2(math.cos(an), math.sin(an)) * rr;
                            if (!lr.ContainsDisc(q, 1.5f)) continue;
                            if (i == 0 && Hashing.Unit(V(0x51DDu)) < 0.5f)
                            {
                                uint tv = V(0x51DEu);
                                if (Occ.Free(q, 2.5f, 0.2f, 0.1f)) { Ctx.Object(q, Hashing.Unit(tv) * 6.28f, ObjectRole.Tree, AssetMix.Pick(PlazaArt.PlaneTrees, tv), tv, Hashing.Unit(tv) < 0.4f ? 4 : 1, null); Occ.Take(q, 2.5f, 3); trees++; TreeCount++; }
                                continue;
                            }
                            LeisureKit.Put(Ctx, Occ, q, an, ObjectRole.Bush, AssetMix.Pick(i == 3 ? DeadBushes : WildBushes, V(0x51DFu)), AssetCategories.RuinsWeeds, 0.9f, V(0x51E0u), 0.1f, 0.05f, 0);
                        }
                    }
                // 4) a poster wall along the piece where it is long enough
                if (len > 30f)
                {
                    var wa = c + side * (dep * 0.3f) - dirLong * 7f; var wb = wa + dirLong * 14f;
                    if (lr.ContainsDisc(wa, 1.5f) && lr.ContainsDisc(wb, 1.5f))
                    {
                        Wall(wa, wb, 2);
                        var n = new float2(-dirLong.y, dirLong.x);
                        for (float t = 1.2f; t < 13f; t += 2.4f)
                            foreach (float sd in new[] { -1f, 1f })
                            {
                                var o = Ctx.Object(wa + dirLong * t + n * (sd * 0.12f), math.atan2(n.x * sd, n.y * sd), ObjectRole.Furniture,
                                    new AssetRef(AssetMix.Pick(Posters, V(0x51E1u)).Id, "PosterSmall01 - ShortMarket"), V(0x51E2u), -1, AssetCategories.Kiosk);
                                o.Elevation = math.lerp(0.4f, 1.1f, Hashing.Unit(V(0x51E3u)));
                            }
                    }
                }
            }

            /// <summary>Dirt desire paths from every entrance straight to the square, cut through the rough edge.</summary>
            public void DesirePaths(float ha, float hb)
            {
                if (_paint == null) return;
                foreach (var g in Gates)
                {
                    var l = Local(g.Position);
                    var to = W(math.clamp(l.x * 0.3f, -ha, ha), math.clamp(l.y * 0.3f, -hb, hb));
                    _paint.Stroke(new[] { g.Position, (g.Position + to) * 0.5f + new float2(Hashing.Unit(V(0x51E4u)) - 0.5f, Hashing.Unit(V(0x51E5u)) - 0.5f) * 6f, to }, TerrainMaterials.Dirt, 1f, 2.4f, 1f, 1f, 0.8f, 0.1f, 1.2f, 8f);
                }
            }

            private void ParkPiece(float2[] piece, string manner, ref int trees)
            {
                var lr = LayerRegion.From(piece);
                if (lr == null) return;
                Lay(PField - 1, piece, Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                // trees in loose clusters on a jittered 24 m lattice, a share of the lattice left open (real open ground)
                var mn = lr.Min; var mx = lr.Max;
                for (float x = mn.x + 10f; x < mx.x; x += 24f)
                    for (float y = mn.y + 10f; y < mx.y; y += 24f)
                    {
                        var cc = new float2(x, y) + (new float2(Hashing.Unit(V(0x5A10u)), Hashing.Unit(V(0x5A11u))) - 0.5f) * 10f;
                        if (!lr.ContainsDisc(cc, 5f) || Hashing.Unit(V(0x5A12u)) < 0.4f) continue;
                        int n = 2 + (int)(Hashing.Unit(V(0x5A13u)) * 4f);
                        for (int i = 0; i < n; i++)
                        {
                            float an = Hashing.Unit(V(0x5A14u)) * 6.28f, rr = 2f + Hashing.Unit(V(0x5A15u)) * 6f;
                            var q = cc + new float2(math.cos(an), math.sin(an)) * rr;
                            if (!lr.ContainsDisc(q, 2f)) continue;
                            if (manner == "wild")
                            {
                                if (i == 0 && Tree(q, false, PlazaArt.PlaneTrees)) { trees++; continue; }
                                LeisureKit.Put(Ctx, Occ, q, an, ObjectRole.Bush, AssetMix.Pick(i % 3 == 2 ? DeadBushes : WildBushes, V(0x5A16u)), AssetCategories.RuinsWeeds, 0.9f, V(0x5A17u), 0.1f, 0.05f, 0);
                            }
                            else if (Tree(q, false, PlazaArt.GardenTrees)) trees++;
                        }
                        if (manner != "wild" && Hashing.Unit(V(0x5A18u)) < 0.5f)
                        {
                            var dir = math.normalizesafe(C - cc);
                            Seat(cc + dir * 8f, dir);
                        }
                    }
                if (manner == "wild")
                {
                    // the edge nobody tends: a skip, a run of jersey barriers, bin bags
                    var c0 = lr.DeepestPoint(out _, null, 12);
                    Prop(this, c0, Phase, "TrashContainerEmpty03", "industrial.skip", 2f);
                    var d0 = math.normalizesafe(C - c0); var sd = new float2(-d0.y, d0.x);
                    for (int i = -2; i <= 2; i++) Prop(this, c0 + d0 * 4f + sd * (i * 1.3f), math.atan2(sd.x, sd.y), "ConcreteBarrier0" + (1 + (i + 2) % 3), AssetCategories.ConstructionBarrier, 0.6f);
                }
            }
        }
    }
}
