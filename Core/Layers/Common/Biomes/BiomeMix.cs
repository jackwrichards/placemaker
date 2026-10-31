// Placemaker — Biome mix: the controls (docs/runbooks/biomes.md).
//
// A type offers some biomes (BiomeOffer). Each offered biome is one slider of the "biomes" group (0-100 %: its share of the
// site's natural ground), plus "Patch size" (biomeScale: many small patches .. a few big areas). The panel draws the group as
// one mix control (UI/src/controls/BiomeMix.tsx: a bar of the shares with draggable dividers, a row per biome that balances
// the others); without it the sliders still work: the shares are weights, normalised here. Styles preset the mix
// (BiomeMix.Preset gives every offered biome a value, so a style never inherits the type's default shares by accident).

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Layers.Common.Biomes
{
    /// <summary>What a type offers: its biomes (in the panel's order) and their default shares.</summary>
    public sealed class BiomeOffer
    {
        public readonly List<(string id, double share)> Biomes = new List<(string, double)>();
        /// <summary>Default patch size (0 many small patches .. 100 a few big areas).</summary>
        public double Scale = 50;

        public BiomeOffer(params (string id, double share)[] biomes)
        {
            foreach (var b in biomes) if (BiomeLibrary.Get(b.id) != null) Biomes.Add(b);
        }

        public bool Offers(string id) { foreach (var b in Biomes) if (b.id == id) return true; return false; }

        /// <summary>
        /// (P2-8) The type's own planting controls that only act on its lawn (the host biome): the panel shows them in the
        /// lawn's box of the Landscape card instead of Parameters (group "biome:&lt;host id&gt;").
        /// </summary>
        public string[] LawnControls = Array.Empty<string>();
    }

    public static class BiomeMix
    {
        /// <summary>The panel's group (one mix control).</summary>
        public const string Group = "biomes";
        public const string ScaleId = "biomeScale";
        public const string ScaleLabel = "Placemaker.Biome.scale";
        /// <summary>Plan facts: "biome.&lt;id&gt;" = the share of the natural ground it got (%).</summary>
        public const string FactPrefix = "biome.";

        public static string ControlIdOf(string biomeId) =>
            string.IsNullOrEmpty(biomeId) ? "biome" : "biome" + char.ToUpperInvariant(biomeId[0]) + biomeId.Substring(1);

        /// <summary>The biome a control id stands for (null: not a biome control).</summary>
        public static BiomeDef OfControl(string controlId)
        {
            if (controlId == null || !controlId.StartsWith("biome", StringComparison.Ordinal) || controlId == ScaleId) return null;
            foreach (var b in BiomeLibrary.All) if (b.ControlId == controlId) return b;
            return null;
        }

        /// <summary>Adds the mix's sliders (one per offered biome) and the patch size to a type's controls.</summary>
        public static void Define(ControlSet c, BiomeOffer offer)
        {
            if (offer == null || offer.Biomes.Count == 0) return;
            foreach (var (id, share) in offer.Biomes)
            {
                var b = BiomeLibrary.Get(id);
                var s = c.Slider(b.ControlId, share, 0, 100, 1, "%", ControlTier.Essential, Group);
                s.LabelKey = b.LabelKey; s.TooltipKey = b.TooltipKey;
                c.Fixed(b.ControlId);
            }
            var sc = c.Slider(ScaleId, offer.Scale, 0, 100, 1, "%", ControlTier.Essential, Group);
            sc.LabelKey = ScaleLabel; sc.TooltipKey = ScaleLabel + ".Tooltip";
            c.Fixed(ScaleId);
            // (P2-8) each biome's own few settings (shown in its box when it is clicked on the bar)
            BiomeKnobs.Define(c, offer);
            string host = null;
            foreach (var (id, _) in offer.Biomes) { var hb = BiomeLibrary.Get(id); if (hb != null && hb.Host) { host = hb.Id; break; } }
            if (host != null)
                foreach (var id in offer.LawnControls)
                    foreach (var ctl in c.List) if (ctl.Id == id) ctl.Group = BiomeKnobs.GroupPrefix + host;
        }

        /// <summary>A style's mix: every offered biome gets a value (the ones not named: 0).</summary>
        public static (string key, object value)[] Preset(BiomeOffer offer, double scale, params (string id, double share)[] shares)
        {
            var list = new List<(string, object)>();
            foreach (var (id, _) in offer.Biomes)
            {
                double v = 0;
                foreach (var s in shares) if (s.id == id) v = s.share;
                list.Add((ControlIdOf(id), v));
            }
            list.Add((ScaleId, scale));
            return list.ToArray();
        }

        /// <summary>Joins a style's other values with its mix.</summary>
        public static (string key, object value)[] With((string key, object value)[] values, (string key, object value)[] mix)
        {
            var list = new List<(string, object)>(values);
            list.AddRange(mix);
            return list.ToArray();
        }

        /// <summary>The shares the plan uses (normalised to 1; biomes at 0 left out). Nothing set: the first offered biome.</summary>
        public static List<(BiomeDef biome, float share)> Shares(LayerContext ctx, BiomeOffer offer)
        {
            var list = new List<(BiomeDef, float)>();
            if (offer == null) return list;
            double sum = 0;
            foreach (var (id, _) in offer.Biomes)
            {
                var b = BiomeLibrary.Get(id);
                double v = Math.Max(0, ctx.Settings.Num(b.ControlId, 0));
                if (v <= 0) continue;
                list.Add((BiomeKnobs.Tuned(ctx.Settings, b), (float)v));   // (P2-8) as the player tuned it
                sum += v;
            }
            if (sum <= 0)
            {
                list.Clear();
                if (offer.Biomes.Count > 0) list.Add((BiomeLibrary.Get(offer.Biomes[0].id), 1f));
                return list;
            }
            for (int i = 0; i < list.Count; i++) list[i] = (list[i].Item1, (float)(list[i].Item2 / sum));
            return list;
        }

        /// <summary>Patch size 0..1 (0 many small patches, 1 a few big areas).</summary>
        public static float Scale(LayerContext ctx, BiomeOffer offer) =>
            (float)Math.Max(0, Math.Min(1, ctx.Settings.Num(ScaleId, offer?.Scale ?? 50) / 100.0));

        /// <summary>The mix has anything but the host's own ground (a plain 100 % lawn plans exactly as the type always did).</summary>
        public static bool AnyNatural(List<(BiomeDef biome, float share)> shares)
        {
            foreach (var (b, s) in shares) if (!b.Host && s > 0f) return true;
            return false;
        }
    }
}
