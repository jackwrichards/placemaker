// Placemaker — Biome mix for any type in two lines (docs/runbooks/biomes.md, "Offering biomes on your type").
//
//   public static readonly BiomeOffer Biomes = new BiomeOffer(("lawn", 50), ("forest", 30), ("rocky", 20));   // the type's offer
//   DefineControls:  BiomeMix.Define(c, Biomes);                                                              // the mix's sliders
//   PlanCore, once the paths are laid and the facilities have their ground (taken in occ), before the planting:
//       var biomes = BiomeHost.Plan(ctx, occ, Biomes, gates, blocked);
//
// Everything the occupancy already holds (discs: facilities, clearings; polygons: plots, buildings; reserved space) stays the
// type's own ground, so do it after the type has taken what it must have; the corridors are the paths the areas border and
// keep a verge along. The areas' outlines go into occ, so the type's own planting afterwards stays on its lawn.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common.Biomes
{
    public static class BiomeHost
    {
        /// <summary>
        /// Plans the type's mix over its region (null: nothing natural in the mix, the type plans as it always did).
        /// <paramref name="gates"/> keep 12 m of open ground; <paramref name="blocked"/> more ground that stays the type's.
        /// </summary>
        public static BiomeResult Plan(LayerContext ctx, Occupancy occ, BiomeOffer offer, IReadOnlyList<float2> gates = null, Func<float2, bool> blocked = null,
            bool shape = true, string treeDensityId = "treeDensity", float treeDesign = 0f, float2 rowAxis = default)
        {
            var mix = BiomeMix.Shares(ctx, offer);
            if (!BiomeMix.AnyNatural(mix)) return null;
            var reserved = ctx.Input?.Reserved;
            float ta = 1f;
            if (treeDesign > 0f && ctx.ControlOf?.Invoke(treeDensityId) != null)
            {
                float td = ctx.F(treeDensityId);
                ta = td <= 0f ? 0f : math.clamp(td / treeDesign, 0.25f, 2f);
            }
            var site = new BiomeSite
            {
                Ctx = ctx, Region = ctx.LayerRegion, Paths = occ.Corridors, Occ = occ,
                Verge = ctx.M(3f, 2.5f),
                Blocked = p =>
                {
                    if (blocked != null && blocked(p)) return true;
                    if (gates != null) foreach (var g in gates) if (math.distancesq(g, p) < 144f) return true;
                    return occ.Discs.Overlaps(p, 2.5f) || occ.HitsPolygon(p, 1.5f) || reserved != null && reserved.Blocks(p, 1.5f);
                },
                Trees = ctx.L("treeSpecies") ?? Array.Empty<string>(), Bushes = ctx.L("bushSpecies") ?? Array.Empty<string>(),
                TreeAmount = ta,
                Shape = shape && !ctx.Input.HasChild("terrain"),
                RowAxis = rowAxis,
            };
            return BiomeBuilder.Plan(site, mix, BiomeMix.Scale(ctx, offer), Hashing.Mix(ctx.Seed, 0xB10Eu));
        }
    }
}
