// Placemaker — the site plan's objects as rough models (sketch study; the user, 25 Oct: "every furniture should be modeled.
// Cars, junk, whatever. Even if it's crude looking, it should be kind of how it's going to look in the end").
//
// Every placed object but trees, bushes and decals is drawn at its real footprint (its asset's bounds × scale, turned by its
// yaw), in a colour for what it is: a car is a red box the size of a car, a container a long box in a shipping colour, a pallet
// a small wood square, a tyre a dark disc. Tall things cast a short shadow to the north (the sun is south), so the picture reads
// as objects standing on the ground. The kinds are grouped into a few legend rows (Furniture, Places, Buildings, Vehicles,
// Storage, Junk, Barriers, Crops, Stone), so the legend stays short whatever the type.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public sealed partial class SitePlan
    {
        // ── Prop rows (legend ids; Furniture and Places keep their PreviewLegend ids) ──
        public const string Buildings = "obj:buildings", Vehicles = "obj:vehicles", Storage = "obj:storage", Junk = "obj:junk",
            Barriers = "obj:barriers", Crops = "obj:crops", Stone = "obj:stone";

        /// <summary>One object as a rough model.</summary>
        public struct Prop
        {
            /// <summary>The footprint (corners in order); a disc when <see cref="Round"/> (then <see cref="At"/> and <see cref="R"/>).</summary>
            public float2[] Poly;
            public bool Round;
            public float2 At;
            public float R;
            public string Color;
            public string Row;
            /// <summary>Metres: a shadow is cast from 1.5 m up.</summary>
            public float Height;
        }

        public readonly List<Prop> Props = new List<Prop>();

        /// <summary>The legend rows of the props, in display order, with their swatch and label.</summary>
        public static readonly (string row, string sym, string key, string label)[] PropRows =
        {
            (Places, "place", "Placemaker.SitePlan.Places", "Places"),
            (Buildings, "building", "Placemaker.SitePlan.Buildings", "Buildings"),
            (Vehicles, "vehicle", "Placemaker.SitePlan.Vehicles", "Vehicles"),
            (Storage, "storage", "Placemaker.SitePlan.Storage", "Storage"),
            (Junk, "junk", "Placemaker.SitePlan.Junk", "Junk"),
            (Crops, "crop", "Placemaker.SitePlan.Crops", "Crops"),
            (Stone, "stone", "Placemaker.SitePlan.Stone", "Stones"),
            (Barriers, "barrier", "Placemaker.SitePlan.Barriers", "Barriers"),
            (Furniture, "furniture", "Placemaker.SitePlan.Furniture", "Furniture"),
        };

        private static readonly string[] CarColors = { "#d8dadc", "#2c2f33", "#3f78c0", "#c9473b", "#e8c440", "#5fb8c8", "#8a8f96", "#e8e6e0" };
        private static readonly string[] ContainerColors = { "#b8453a", "#3d6ea8", "#4f8a4a", "#d98a32", "#7a7f86", "#a8b0b8" };

        private static bool Has(string cat, params string[] words)
        {
            foreach (var w in words) if (cat.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>What an object is drawn as: its colour, its legend row, round or not, and a height when the asset has none.</summary>
        public static (string color, string row, bool round, float height) Kind(ObjectRole role, string cat, uint variation)
        {
            cat = (cat ?? "").ToLowerInvariant();
            string pick(string[] set) => set[(int)(variation % (uint)set.Length)];
            switch (role)
            {
                case ObjectRole.Lamp: return ("#fff3c4", Furniture, true, 6f);
                case ObjectRole.Bin: return ("#2a2b2e", Furniture, true, 1f);
                case ObjectRole.Bench: return ("#8a6440", Furniture, false, 0.8f);
                case ObjectRole.Centerpiece:
                    return Has(cat, "fountain") ? ("#7fb6e6", Places, true, 1.5f) : ("#b9b4aa", Stone, false, 3f);
            }
            // vehicles and machines
            // cars in car colours (the built yard is a sea of colourful wrecks)
            if (Has(cat, "wreck")) return (pick(CarColors), Vehicles, false, 1.5f);
            if (Has(cat, "machine", "machinery", "golf.cart", "plant")) return ("#e0b030", Vehicles, false, 3f);
            if (Has(cat, "truck", "foodtruck", "boat", "car")) return (pick(CarColors), Vehicles, false, 2f);
            // storage
            if (Has(cat, "container", "skip")) return (pick(ContainerColors), Storage, false, 2.6f);
            if (Has(cat, "pallet", "crate", "lumber", "material", "bulk")) return ("#b08a5a", Storage, false, 1f);
            if (Has(cat, "barrel")) return (pick(new[] { "#3f6fa8", "#a84a32", "#5a6a3a" }), Storage, true, 1f);
            if (Has(cat, "silo")) return ("#c9ccd0", Buildings, true, 12f);
            // junk
            if (Has(cat, "tire")) return ("#2b2b2d", Junk, true, 0.6f);
            if (Has(cat, "bale", "hay")) return ("#d8c070", Crops, false, 1.2f);
            if (Has(cat, "scrap", "garbage", "junk", "rubble", "pile", "heap")) return (pick(new[] { "#7b6e62", "#8f8a80", "#6e6a5a" }), Junk, false, 1.2f);
            // buildings and stalls
            if (Has(cat, "glasshouse")) return ("#cfe8ea", Buildings, false, 3f);
            if (Has(cat, "tent", "festoon", "awning")) return ("#f0efe8", Buildings, false, 3f);
            if (Has(cat, "cabin", "shed", "kiosk", "stall", "restroom", "toilet", "shelter", "stage", "aviary", "backstage", "screen", "lighttower", "teahouse", "showers", "shower"))
                return (pick(new[] { "#d9d4c8", "#c8b89a", "#b9aa94" }), Buildings, false, 3f);
            // barriers and signs
            if (Has(cat, "cone")) return ("#f07a20", Barriers, true, 0.7f);
            if (Has(cat, "barrier", "bollard", "fence")) return ("#e8e4dc", Barriers, false, 1f);
            if (Has(cat, "sign", "flag", "plaque", "speaker", "bikerack")) return ("#2f6db5", Barriers, false, 2f);
            // crops
            if (Has(cat, "lavender")) return ("#9b7fd0", Crops, false, 0.6f);
            if (Has(cat, "crop", "vine", "trellis", "prop", "beehive")) return ("#b8c86a", Crops, false, 0.8f);
            // stone
            if (Has(cat, "rock", "stone")) return ("#8f8f8a", Stone, true, 1f);
            if (Has(cat, "grave", "memorial", "sculpture", "statue", "monument", "pillar", "slab", "wall", "ruins")) return ("#b9b4aa", Stone, false, 1.5f);
            // places: play, sports, picnic, the park features
            if (Has(cat, "play", "sports", "skate", "fitness", "dog.", "goal", "golf", "ride", "trough", "jump", "fishing", "nautical"))
                return ("#f0a868", Places, false, 1.5f);
            if (Has(cat, "picnic", "seating", "deckchair", "lounger", "umbrella", "camping", "fire", "pitch"))
                return (Has(cat, "umbrella") ? "#e05050" : "#8a6440", Furniture, Has(cat, "umbrella", "fire"), 1f);
            if (Has(cat, "lamp", "lantern")) return ("#fff3c4", Furniture, true, 5f);
            return ("#c8b89a", Furniture, false, 1f);
        }

        /// <summary>A placed object as a rough model (null for what the site plan draws otherwise: trees, bushes, decals, markers).</summary>
        private static Prop? ToProp(PlannedObject o, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            string cat = RoleCategories.ForObject(o) ?? "";
            var (color, row, round, h) = Kind(o.Role, cat, o.Variation);
            var poly = Footprint(o, assets);
            if (TryInfo(o, assets, out var info)) h = (info.BoundsMax.y - info.BoundsMin.y) * (o.Scale > 0.05f ? o.Scale : 1f);
            // never smaller than 0.6 m across: a bin or a cone stays visible
            float2 mid = (poly[0] + poly[2]) * 0.5f;
            float a = math.distance(poly[0], poly[1]), b = math.distance(poly[1], poly[2]);
            if (round)
            {
                float r = math.max(0.3f, math.max(a, b) * 0.5f);
                return new Prop { Round = true, At = mid, R = r, Color = color, Row = row, Height = h };
            }
            if (a < 0.6f || b < 0.6f)
            {
                float2 u = math.normalizesafe(poly[1] - poly[0], new float2(1, 0)), v = math.normalizesafe(poly[3] - poly[0], new float2(0, 1));
                float ha = math.max(0.3f, a * 0.5f), hb = math.max(0.3f, b * 0.5f);
                poly = new[] { mid - u * ha - v * hb, mid + u * ha - v * hb, mid + u * ha + v * hb, mid - u * ha + v * hb };
            }
            return new Prop { Poly = poly, Color = color, Row = row, Height = h };
        }

        /// <summary>The offset of a prop's shadow (north, a little east; longer for taller things, at most 6 m).</summary>
        public static float2 ShadowOffset(float height) => new float2(0.35f, 1f) * math.min(6f, height * 0.35f);
    }
}
