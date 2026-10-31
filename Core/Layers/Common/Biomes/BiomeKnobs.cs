// Placemaker — Biome mix: each biome's own few settings (P2-8, docs/runbooks/biomes.md, "Per-biome settings").
//
// The user (2026-10-31): "maybe each biome can be customized itself. But we also have other parameters below. Is that where
// those are global and then the biomes are biome-specific? It needs to be really good UX." So a biome in the mix has 2-4
// settings of its own (the ones that matter for it: a meadow's trees and wildflowers, a forest's kind of trees and dead wood,
// a wetland's open water), shown when the player clicks it on the bar (UI/src/controls/BiomeMix.tsx). The whole place's
// settings (patch size, paths, water, facilities, the ground) stay where they are.
//
// A knob is one control per offered biome: id "biome<Biome><Knob>" (biomeMeadowTrees), group "biome:<id>" (the panel puts it
// in that biome's box; the Parameters list leaves it out), label "Placemaker.Biome.Knob.<knob>" (shared by every biome).
// It is read only while its biome is in the mix (a knob of a biome at 0 % is "not used": the panel greys it, the dead-control
// test skips it). What it does is a change to a copy of the biome's definition (BiomeKnobs.Apply), so the field, the builder
// and the decals all see one tuned biome; the plan keeps the values in its facts ("biomeKnob.<id>.<knob>") for the decals.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common.Biomes
{
    /// <summary>One setting of a biome: a slider (Min..Max, Default) or a choice of Options.</summary>
    public sealed class BiomeKnob
    {
        public string Key = "";
        /// <summary>The label's last part when it is not the key ("reeds" for a wetland's shrubs).</summary>
        public string Label;
        public double Default, Min, Max = 100, Step = 1;
        public string Unit = "%";
        /// <summary>A choice instead of a slider (its values; Default is the index of the default option).</summary>
        public string[] Options;

        public static BiomeKnob Slider(string key, double def, double max, string unit = "%", double step = 1) =>
            new BiomeKnob { Key = key, Default = def, Min = 0, Max = max, Unit = unit, Step = step };
        public static BiomeKnob Choice(string key, int def, params string[] options) =>
            new BiomeKnob { Key = key, Default = def, Options = options };
        public BiomeKnob Named(string label) { Label = label; return this; }
    }

    public static class BiomeKnobs
    {
        public const string LabelPrefix = "Placemaker.Biome.Knob.";
        public const string GroupPrefix = "biome:";
        public const string FactPrefix = "biomeKnob.";

        // ── the knob keys (labels "Placemaker.Biome.Knob.<key>") ──
        public const string Trees = "trees", Shrubs = "shrubs", Flowers = "flowers", Rocks = "rocks", Relief = "relief",
            Water = "water", DeadWood = "deadWood", Kind = "kind", Colours = "colours", Grass = "grass", Wear = "wear",
            Edges = "edges", Beds = "beds", Leaves = "leaves";

        public static string ControlId(BiomeDef b, string key) => b.ControlId + char.ToUpperInvariant(key[0]) + key.Substring(1);
        public static string Group(BiomeDef b) => GroupPrefix + b.Id;

        /// <summary>The knobs a biome shows (its own list, else made from what it has).</summary>
        public static BiomeKnob[] Of(BiomeDef b)
        {
            if (b == null || b.Host) return Array.Empty<BiomeKnob>();
            if (b.Knobs != null) return b.Knobs;
            var list = new List<BiomeKnob>();
            if (b.RowPitch <= 0f && b.TreesPerHa > 0f) list.Add(BiomeKnob.Slider(Trees, b.TreesPerHa, TreeMax(b.TreesPerHa), "perHa"));
            if (b.BushesPerHa > 0f || b.Plants.Length > 0) list.Add(BiomeKnob.Slider(Shrubs, 100, 200));
            if (b.CrownsPerHa + b.BouldersPerHa + b.StonesPerHa > 30f) list.Add(BiomeKnob.Slider(Rocks, 100, 200));
            if (b.Lift > 0.3f || b.Dip > 0.3f) list.Add(BiomeKnob.Slider(Relief, 100, 200));
            return list.ToArray();
        }

        /// <summary>A trees slider's top: about twice the design, in round steps.</summary>
        public static double TreeMax(double design) => design <= 0 ? 20 : design < 30 ? 60 : design < 80 ? 150 : 400;

        /// <summary>Adds every offered biome's knobs to a type's controls (BiomeMix.Define).</summary>
        public static void Define(ControlSet c, BiomeOffer offer)
        {
            foreach (var (id, _) in offer.Biomes)
            {
                var b = BiomeLibrary.Get(id);
                foreach (var k in Of(b))
                {
                    string cid = ControlId(b, k.Key);
                    ControlSchema s;
                    if (k.Options != null)
                    {
                        s = c.Choice(cid, k.Options[(int)k.Default], ControlTier.Essential, Group(b), k.Options);
                        for (int i = 0; i < s.Options.Count; i++) s.Options[i].LabelKey = LabelPrefix + (k.Label ?? k.Key) + "." + k.Options[i];
                    }
                    else s = c.Slider(cid, k.Default, k.Min, k.Max, k.Step, k.Unit, ControlTier.Essential, Group(b));
                    s.LabelKey = LabelPrefix + (k.Label ?? k.Key);
                    s.TooltipKey = "";
                    c.Fixed(cid);
                }
            }
        }

        /// <summary>The biome as the player tuned it (the library's own when nothing is set). Reads only this biome's knobs.</summary>
        public static BiomeDef Tuned(SettingsBag settings, BiomeDef b)
        {
            var knobs = Of(b);
            if (knobs.Length == 0 || settings == null) return b;
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            var choices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var k in knobs)
            {
                string cid = ControlId(b, k.Key);
                if (k.Options != null) choices[k.Key] = settings.S(cid, k.Options[(int)k.Default]);
                else values[k.Key] = settings.Num(cid, k.Default);
            }
            return Apply(b, knobs, values, choices);
        }

        /// <summary>The facts that let the decals rebuild the tuned biome (BiomeGround: FromFacts).</summary>
        public static void WriteFacts(Dictionary<string, double> facts, SettingsBag settings, BiomeDef b)
        {
            foreach (var k in Of(b))
            {
                string cid = ControlId(b, k.Key);
                double v;
                if (k.Options != null) { v = Array.IndexOf(k.Options, settings.S(cid, k.Options[(int)k.Default])); if (v < 0) v = k.Default; }
                else v = settings.Num(cid, k.Default);
                facts[FactPrefix + b.Id + "." + k.Key] = v;
            }
        }

        /// <summary>The tuned biome from a plan's facts (decals); the library's own when the plan holds none.</summary>
        public static BiomeDef FromFacts(IReadOnlyDictionary<string, double> facts, BiomeDef b)
        {
            var knobs = Of(b);
            if (knobs.Length == 0 || facts == null) return b;
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            var choices = new Dictionary<string, string>(StringComparer.Ordinal);
            bool any = false;
            foreach (var k in knobs)
            {
                if (!facts.TryGetValue(FactPrefix + b.Id + "." + k.Key, out var v)) continue;
                any = true;
                if (k.Options != null) choices[k.Key] = k.Options[math.clamp((int)math.round(v), 0, k.Options.Length - 1)];
                else values[k.Key] = v;
            }
            return any ? Apply(b, knobs, values, choices) : b;
        }

        private static float Factor(Dictionary<string, double> v, string key, double def = 100) =>
            v.TryGetValue(key, out var x) ? (float)math.max(0, x / def) : 1f;

        private static bool IsFlower(string kind) => kind.StartsWith("flowerDrift", StringComparison.Ordinal) || kind == "meadowFlowers" || kind == "wildflowerDrift" || kind == "daisyLawn" || kind == "cloverPatch";
        private static bool IsRock(string kind) => kind == "rockOutcrop" || kind == "stonesCarpet" || kind.StartsWith("scree", StringComparison.Ordinal) || kind == "pebbles" || kind == "gravelCarpet" || kind == "shinglePatch";
        private static bool IsWear(string kind) => kind == "animalTrail" || kind == "barePatch" || kind == "trampledGrass" || kind == "wornPath" || kind == "groundMottle" || kind == "goalmouthWear";
        /// <summary>A flowering layer: mostly flowering shrubs (a bed of green shrubs with a few in flower is shrubs).</summary>
        private static bool IsFlowerPlant(BiomePlants p) { int n = 0; foreach (var id in p.Ids) if (id.StartsWith("Flower", StringComparison.Ordinal)) n++; return n * 2 > p.Ids.Length; }

        /// <summary>The biome with the knobs' values applied (a copy; the library's stays as it is).</summary>
        public static BiomeDef Apply(BiomeDef b, BiomeKnob[] knobs, Dictionary<string, double> v, Dictionary<string, string> c)
        {
            var d = b.Copy();
            if (v.TryGetValue(Trees, out var trees)) d.TreesPerHa = (float)math.max(0, trees);
            float shrubs = Factor(v, Shrubs), flowers = Factor(v, Flowers), rocks = Factor(v, Rocks), relief = Factor(v, Relief),
                dead = Factor(v, DeadWood), wear = Factor(v, Wear), edges = Factor(v, Edges), leaves = Factor(v, Leaves);
            d.BushesPerHa *= shrubs;
            d.CrownsPerHa *= rocks; d.BouldersPerHa *= rocks; d.StonesPerHa *= rocks;
            d.Lift *= relief; d.Dip *= relief; d.Break *= math.sqrt(relief);
            d.Dead = math.min(0.6f, d.Dead * dead); d.LogsPerHa *= dead;
            if (v.ContainsKey(Wear)) d.CoreStrength = math.min(1f, d.CoreStrength * wear);   // the field's worn heart is paint
            if (v.TryGetValue(Water, out var water)) d.Pools = (float)math.clamp(water / 100.0, 0, 0.8);
            if (v.TryGetValue(Beds, out var beds)) d.Beds = (float)math.clamp(beds / 100.0, 0.05, 0.8);
            if (c.TryGetValue(Kind, out var kind)) d.Trees = TreesOfKind(kind, d.Trees);
            string colours = c.TryGetValue(Colours, out var col) ? col : null;
            string grass = c.TryGetValue(Grass, out var gr) ? gr : null;
            float grassF = grass == "short" ? 0.35f : grass == "tall" ? 1.6f : 1f;
            // plants: flowering drifts follow Flowers, the rest Shrubs (reeds, tussocks, heather)
            var plants = new List<BiomePlants>();
            foreach (var p in d.Plants)
            {
                var q = p.Copy();
                bool flower = IsFlowerPlant(p);
                q.PerHa *= flower ? flowers : shrubs;
                if (!flower && grass != null && p.Where != BiomeWhere.Water) q.PerHa *= grassF;
                if (flower && colours != null) q.Ids = FlowerPlants(colours, q.Ids);
                if (q.PerHa > 0.01f && q.Ids.Length > 0) plants.Add(q);
            }
            d.Plants = plants.ToArray();
            var decals = new List<BiomeDecal>();
            foreach (var x in d.Decals)
            {
                var y = x.Copy();
                if (IsFlower(x.Kind)) { y.Cover *= flowers; if (colours != null) y.Cover *= ColourWeight(colours, x.Kind); }
                else if (IsRock(x.Kind)) y.Cover *= math.sqrt(rocks);
                else if (IsWear(x.Kind)) y.Cover *= wear;
                // leaves lie where trees stand: a biome designed without trees gets its leaves only with the trees the player adds
                else if (x.Kind.StartsWith("leaf", StringComparison.Ordinal) && v.ContainsKey(Leaves)) y.Cover *= leaves;
                else if (x.Kind.StartsWith("leaf", StringComparison.Ordinal) && b.TreesPerHa <= 0f) y.Cover *= (float)math.saturate(d.TreesPerHa / 6.0);
                else if (x.Kind == "longGrass" || x.Kind == "meadowSward" || x.Kind == "dryGrass")
                {
                    if (x.Where == BiomeWhere.Rim && v.ContainsKey(Edges)) y.Cover *= edges;
                    else if (grass != null) y.Cover *= grassF;
                }
                if (y.Cover > 0.005f) decals.Add(y);
            }
            d.Decals = decals.ToArray();
            return d;
        }

        // ── kinds of trees (forest, glade) ──
        private static readonly string[] Broadleaf = { "OakTree01", "EU_ChestnutTree01", "NA_LindenTree01", "EU_AlderTree01", "NA_HickoryTree01", "OakTree01", "BirchTree01" };
        private static readonly string[] Conifer = { "PineTree01", "SpruceTree01", "EuropeanSpruce01", "PineTree01", "SpruceTree01" };
        private static readonly string[] Birch = { "BirchTree01", "BirchTree01", "BirchTree01", "PineTree01", "EU_AlderTree01" };

        private static string[] TreesOfKind(string kind, string[] own) => kind == "broadleaf" ? Broadleaf : kind == "conifer" ? Conifer : kind == "birch" ? Birch : own;

        // ── flower colours (flower field, meadow) ──
        private static float ColourWeight(string colours, string kind)
        {
            if (!kind.StartsWith("flowerDrift", StringComparison.Ordinal)) return 1f;
            string hue = kind.Substring("flowerDrift".Length).ToLowerInvariant();
            switch (colours)
            {
                case "warm": return hue == "red" || hue == "yellow" ? 1.5f : hue == "cream" ? 0.6f : 0.15f;
                case "cool": return hue == "blue" || hue == "purple" ? 1.5f : hue == "cream" ? 0.6f : 0.15f;
                case "purple": return hue == "purple" ? 2.2f : hue == "blue" ? 0.4f : 0.08f;
                case "yellow": return hue == "yellow" ? 2.2f : hue == "cream" ? 0.4f : 0.08f;
                case "white": return hue == "cream" ? 2.4f : 0.08f;
                default: return 1f;
            }
        }

        private static string[] FlowerPlants(string colours, string[] own)
        {
            switch (colours)
            {
                case "warm": case "yellow": return new[] { "FlowerBushCity02", "FlowerBushCity01", "GreenBushWildChild01" };
                case "cool": case "purple": return new[] { "FlowerBushWildChild02", "FlowerBushCity03", "GreenBushWildChild01" };
                case "white": return new[] { "GreenBushWildChild01", "FlowerBushCity01" };
                default: return own;
            }
        }
    }
}
