// Beach (p2-beaches): the ground paint of the landforms and the styles (earth on rock, forest floor under pines and on the
// marsh), laid after the sand (BeachLayer.Ground) so the sand gives way to it.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Biomes;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    public sealed partial class BeachLayer
    {
        /// <summary>
        /// The land behind the beach (behind the dunes, the promenade's lawn edge or the bluff's scarp) as the style's Biome mix:
        /// dunes grading into heath and pine wood, shingle flats, rock, marsh. The beach, its walks and its ravines stay its own.
        /// </summary>
        private void Hinterland(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var f = b.Forms;
            float start = ctx.S("backdrop") == "dunes" ? b.PathDw + b.Hw + 2.5f + ctx.F("duneDepth") + 4f : b.PathDw + b.Hw + 6f + b.Blend * 0.5f;
            if (f.Bluff > 0f) start = math.max(start, f.BluffAt + f.BluffW + 2f);
            var gates = Gates(ctx).ConvertAll(gt => gt.Position);
            var res = BiomeHost.Plan(ctx, b.Occ, Biomes, gates, p =>
            {
                float dw = shore.FromWater(p);
                if (dw < start || b.G.OnCliff(p)) return true;
                shore.Nearest(p, out _, out float s);
                return f.InRavine(s, dw, 4f);
            }, shape: false);   // the beach shapes its own land (two sculpts over one bank left trees 0.3-0.5 m off the ground)
            ctx.Plan.Facts["hinterlandBiomes"] = res != null ? 1 : 0;
        }

        /// <summary>
        /// (p2-beaches) The pocket beach's ground: rock on its walls and lip, sand over the cove floor, gravel up the gully (the
        /// game runner: the cut read as a green dip with grass to the water).
        /// </summary>
        private static void PocketGround(Work b, List<Geometry.Region> clip)
        {
            var ctx = b.Ctx; var pk = b.Pocket;
            float lip = (pk.TopH - pk.Level) / ShoreCarve.Pocket.Wall + 2.5f;
            void Fill(List<Geometry.Region> regions, int layer, float strength, float feather, float noise, uint salt)
            {
                foreach (var r in regions)
                    foreach (var piece in Geometry.Poly.CutHoles(r, 0.5f))
                        Paint(ctx, clip, piece, layer, strength, feather, noise, null, 0f, 0f, 0f, salt);
            }
            var outer = Geometry.Poly.Boolean(pk.Rings(lip), new List<IReadOnlyList<float2>>(), Geometry.BoolOp.Union, Geometry.FillRule.NonZero);
            var walls = Geometry.Poly.Boolean(pk.Rings(lip), pk.Rings(-1.5f), Geometry.BoolOp.Difference, Geometry.FillRule.NonZero);
            Fill(walls, TerrainMaterials.Dirt, 1f, 1.5f, 1.5f, 0x90C1u);
            // the cove floor: sand from under the water to the gully mouth
            var sand = Geometry.Poly.Boolean(new[] { pk.Rings(-1.5f)[0] }, new List<IReadOnlyList<float2>>(), Geometry.BoolOp.Union, Geometry.FillRule.NonZero);
            Fill(sand, TerrainMaterials.Sand, 1f, 2f, 1.5f, 0x90C2u);
            var g = pk.Gully - 1.5f;
            var gully = new[] { pk.At(-g, pk.Sand + 2f), pk.At(g, pk.Sand + 2f), pk.At(g, pk.End), pk.At(-g, pk.End) };
            if (Geometry.Poly.SignedArea(gully) < 0f) System.Array.Reverse(gully);
            Paint(ctx, clip, gully, TerrainMaterials.Dirt, 0.55f, 3f, 2f, null, 0f, 0f, 0f, 0x90C3u);
            ctx.Plan.Facts["pocketPaint"] = outer.Count;
        }

        private static bool RavineAt(Work b, float2 p)
        {
            b.Shore.Nearest(p, out _, out float s);
            return b.Forms.InRavine(s, b.Shore.FromWater(p), b.Hw + 2.5f);
        }

        /// <summary>A ring round a stretch of shore from dw0 to dw1 whose centre line wanders (s(dw)) and whose half-width varies.</summary>
        private static float2[] Ribbon(Shoreline shore, float dw0, float dw1, System.Func<float, float> centre, System.Func<float, float> half)
        {
            var left = new List<float2>(); var right = new List<float2>();
            for (float dw = dw0; dw <= dw1 + 0.01f; dw += 2f)
            {
                float c = centre(dw), h = half(dw);
                left.Add(At(shore, c - h, dw));
                right.Add(At(shore, c + h, dw));
            }
            right.Reverse();
            left.AddRange(right);
            return left.ToArray();
        }

        private static void Paint(LayerContext ctx, List<Geometry.Region> clip, IReadOnlyList<float2> ring, int layer, float strength, float feather, float noise,
            float2[] gradientFrom, float full, float fade, float min, uint salt)
        {
            foreach (var piece in Clip(ring is float2[] a ? a : new List<float2>(ring).ToArray(), clip))
                TerrainPaint.Field(ctx, piece, layer, strength, feather, noise, gradientFrom, full, fade, min, salt);
        }

        private static void StyleGround(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var f = b.Forms;
            if (!ctx.B("paint")) return;
            // every field clipped to the land and the foreshore (the bluff's strip reached past the outline, over the road)
            var clip = Geometry.Poly.UnionAll(b.G.Clips);
            float far = shore.MaxDepth + Shoreline.MaxGap + 20f;
            // rock: headlands, platforms, knolls, ravines are earth and stone, never sand
            foreach (var (hs, hlen, hh, hw) in f.Headlands)
            {
                var ring = Ribbon(shore, -hlen - 2f, 16f, dw => hs + 4f * math.sin(dw / 9f + hs) * (1f - math.saturate((dw + hlen) / (hlen + 18f))),
                    dw => hw * (0.55f + 0.45f * math.saturate((dw + hlen) / (hlen + 18f))) * 1.1f);
                Paint(ctx, clip, ring, TerrainMaterials.Dirt, 0.95f, 2.5f, 2f, null, 0f, 0f, 0f, 0x4EA0u);
            }
            foreach (var pl in f.Platforms)
                foreach (var strip in shore.StripFromWater(f.PlatformFrom, f.PlatformTo + 3f, pl.x, pl.y))
                    Paint(ctx, clip, strip, TerrainMaterials.Dirt, 0.9f, 2.5f, 2f, null, 0f, 0f, 0f, 0x9A70u);
            foreach (var (ic, ir, ih, _) in f.Islands)
                TerrainPaint.Field(ctx, LeisureKit.Blob(ic, ir * 1.2f, Hashing.Mix(f.Seed, (uint)(ic.x * 5f)), 14, 0.25f), TerrainMaterials.Dirt, 0.85f, 2.5f, 1.5f, null, 0f, 0f, 0f, 0x15A1u);
            foreach (var (kc, kr, kh) in f.Knolls)
                Paint(ctx, clip, LeisureKit.Blob(kc, kr * 1.1f, Hashing.Mix(f.Seed, (uint)(kc.x * 7f)), 14, 0.25f), TerrainMaterials.Dirt, 0.85f, 2.5f, 1.5f, null, 0f, 0f, 0f, 0x6A00u);
            foreach (var (rs, rw, rlen) in f.Ravines)
            {
                var ring = Ribbon(shore, -2f, math.min(rlen, far), dw => rs + 3f * math.sin(dw / 13f + rs), dw => rw * (0.75f + 0.35f * math.saturate(dw / 40f)) + 3f);
                Paint(ctx, clip, ring, TerrainMaterials.Dirt, 1f, 2.5f, 2.5f, null, 0f, 0f, 0f, 0x2A70u);
            }
            if (f.Bluff > 0f)
                foreach (var strip in shore.StripFromWater(f.BluffAt - 2f, far))
                {
                    Paint(ctx, clip, strip, TerrainMaterials.Dirt, 0.55f, 5f, 4f, null, 0f, 0f, 0f, 0xB1F0u);
                    Paint(ctx, clip, strip, TerrainMaterials.Lush, 0.35f, 6f, 6f, null, 0f, 0f, 0f, 0xB1F1u);
                }
            if (b.Pocket != null) PocketGround(b, clip);
            switch (b.Style)
            {
                case "mulch":
                    // the pines' floor reaches down onto the sand: duff from a few metres behind the wet band, full under the trees
                    foreach (var strip in shore.StripFromWater(b.Wet + 3f, far))
                        Paint(ctx, clip, strip, TerrainMaterials.Lush, 0.95f, 6f, 5f, null, 0f, 0f, 0f, 0x3C10u);
                    break;
                case "marsh":
                    foreach (var strip in shore.StripFromWater(b.Wet * 0.5f, f.MarshTo + 8f))
                        Paint(ctx, clip, strip, TerrainMaterials.Lush, 0.9f, 4f, 3f, null, 0f, 0f, 0f, 0x3A55u);
                    foreach (var (pc, pr) in f.Pans)
                        Paint(ctx, clip, LeisureKit.Blob(pc, pr, Hashing.Mix(f.Seed, (uint)(pc.y * 3f)), 12, 0.3f), TerrainMaterials.Dirt, 0.75f, 1.5f, 1f, null, 0f, 0f, 0f, 0x3A56u);
                    break;
                case "shingle":
                    // banded: fine sand in the troughs between the storm ridges, the crests left coarse grey shingle
                    for (int i = 0; i < f.Ridges.Count; i++)
                    {
                        float t0 = i == 0 ? b.Wet : f.Ridges[i - 1] + 2.5f, t1 = f.Ridges[i] - 2f;
                        if (t1 - t0 < 1.5f) continue;
                        foreach (var strip in shore.StripFromWater(t0, t1))
                            Paint(ctx, clip, strip, TerrainMaterials.Sand, 0.55f, 1.5f, 2f, null, 0f, 0f, 0f, 0x5B1Eu + (uint)i);
                    }
                    break;
                case "granite":
                    // coarse grey-tan sand: earth through the sand away from the water
                    foreach (var strip in shore.StripFromWater(b.Wet + 2f, b.D + 6f))
                        Paint(ctx, clip, strip, TerrainMaterials.Dirt, 0.4f, 5f, 5f, null, 0f, 0f, 0f, 0x6A10u);
                    break;
            }
        }
    }
}
