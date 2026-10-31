// Placemaker — "too big" (Phase 1, 2026-10-30): a gentle warning beside the hard "too small".
//
// The user: "we should have a warning for too big. It'll work. Just be careful. It's going to be laggy to bulldoze. A lot of
// these aren't specifically catered to be this large." Not a block: Build stays possible. Each Make-row group has the size its
// types are made for; a type can name its own (LayerBase.ComfortMaxArea). Zoning types have none.

using System;

namespace Placemaker.Core.Tool
{
    public static class SizeGuide
    {
        public const string TooBigKey = "Placemaker.Problem.TooBigFor";

        /// <summary>The area (m²) the type is made for at most; 0 = no limit.</summary>
        public static double ComfortMax(IAreaGenerator gen)
        {
            if (gen == null || gen.Kind != LayerKind.Fill) return 0;
            if (gen is Layers.Common.LayerBase lb && lb.ComfortMaxArea > 0) return lb.ComfortMaxArea;
            switch (GeneratorGroups.Of(gen))
            {
                case "parking": return 30_000;
                case "squares": return 25_000;
                case "commercial": return 60_000;
                case "transport": return 40_000;
                case "gardens": return 60_000;
                case "parks": return 150_000;
                case "sports": return 120_000;
                case "events": return 150_000;
                case "water": return 150_000;
                case "industrial": return 200_000;
                case "farms": return 400_000;
                case "nature": return 500_000;
                case "roads": return 2_000_000;
                case "zoning": return 0;
                default: return 150_000;
            }
        }

        /// <summary>The warning for an area of <paramref name="areaM2"/>, or null (made for it, or past the hard maximum: that blocks).</summary>
        public static LocText Warning(IAreaGenerator gen, double areaM2)
        {
            double max = ComfortMax(gen);
            if (max <= 0 || areaM2 <= max) return null;
            try { if (gen.MaxArea > 0 && areaM2 > gen.MaxArea) return null; } catch (Exception) { }
            return new LocText(TooBigKey, Math.Round(max / 10_000.0, 1));
        }
    }
}
