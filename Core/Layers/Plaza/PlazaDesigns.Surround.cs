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
                float ha = EA + 1f, hb = EB + 1f;   // the square's real edge (no pale margin band between it and the surround)
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
                if (manner != "promenade") Lay(PWalk + 1, PavingShapes.Frame(edge.Ring, 3.6f), manner == "wild" ? P.Band : P.Path);
                foreach (var g in Gates)
                {
                    var l = Local(g.Position);
                    var to = W(math.clamp(l.x, -ha - 1.5f, ha + 1.5f), math.clamp(l.y, -hb - 1.5f, hb + 1.5f));
                    if (math.distance(g.Position, to) > 4f) Lay(PWalk + 1, PavingShapes.Band(g.Position, to, 3.6f), manner == "wild" ? P.Band : P.Path);
                }
                foreach (var e in edge.Ring) Light(e);
                for (int s = 0; s < 4; s++)
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
                    ParkPiece(piece, manner, ref trees);
                }
                Ctx.Plan.Facts["surroundTrees"] = trees;
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
