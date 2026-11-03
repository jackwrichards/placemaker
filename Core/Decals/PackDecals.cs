// Placemaker — no decals from decal packs (GitHub issue #2; the user, 2026-10-19). GPL-3.0.
//
// The base game ships only markings, court lines and Chirper logos as decals: every ground decal (leaves, dirt, sand,
// moss, puddles, cracks, graffiti, paving patterns, tyre ruts) comes from a decal pack. Builds placed the decorative ones
// as see-through copies made at runtime, and a save names them: when a copy was not ready at load (the packs arrive
// 30–75 s after startup) the game drew white boxes, and none can outlive the mod. The user: "Let's just drop decal
// support." So a plan keeps only the base game's kinds (markings, parking stalls, court and field lines); whatever else
// a layer or the dressing pass would place is dropped in one place (Dressing.Apply, after every layer has planned), and
// the controls that only placed pack decals are hidden from the panel. The layers' decal code stays (AGENTS.md: cut,
// never delete); Game/Build/OldDecalCleanupSystem clears the see-through copies older builds left in saves.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.AddOns;

namespace Placemaker.Core.Decals
{
    public static class PackDecals
    {
        /// <summary>A decal of this category stays in plans: the base game has it (markings, stalls, court and field lines).</summary>
        public static bool Kept(string category) =>
            DecalCatalog.IsFunctionalCategory(category) && category != AssetCategories.DecalPaving && category != AssetCategories.DecalFarm;

        /// <summary>Removes every decal of a layer the base game has no kind of; the number removed.</summary>
        public static int Drop(AreaPlan layer) =>
            layer?.Objects == null ? 0 : layer.Objects.RemoveAll(o => o != null && o.Role == ObjectRole.Decal && !Kept(o.Category));

        /// <summary>The Decals feature (a layer of pack decals) is offered by no type any more.</summary>
        public static bool IsHiddenFeature(string featureLayer) => featureLayer == DecalsAddOn.LayerId;

        // Type controls that only placed pack decals (each type's own toggles and sliders; the Decals section's are no
        // longer made, DressingControls.For).
        private static readonly Dictionary<string, string[]> HiddenControls = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ground"] = new[] { "decals" },
            ["debris"] = new[] { "stains" },
            ["abandonedLot"] = new[] { "graffiti", "cracking", "puddles" },
            ["constructionSite"] = new[] { "mudDecals", "puddles" },
            ["quarry"] = new[] { "mudDecals" },
            ["scrapyard"] = new[] { "grime" },
            ["landfill"] = new[] { "grime" },
            ["junkPiles"] = new[] { "grime" },
            ["logisticsYard"] = new[] { "stains" },
            ["urbanRuins"] = new[] { "graffiti" },
            ["skatePark"] = new[] { "graffiti" },
        };

        /// <summary>Every (type, control) pair hidden by <see cref="IsHiddenControl"/> (tests check each still exists).</summary>
        public static IEnumerable<(string Layer, string Control)> HiddenControlIds
        {
            get { foreach (var kv in HiddenControls) foreach (var c in kv.Value) yield return (kv.Key, c); }
        }

        /// <summary>Hidden from the panel: it only placed decals from decal packs (they are dropped whatever it says).</summary>
        public static bool IsHiddenControl(string layerId, string controlId) =>
            layerId != null && controlId != null && HiddenControls.TryGetValue(layerId, out var ids) && Array.IndexOf(ids, controlId) >= 0;
    }
}
