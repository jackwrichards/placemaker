// Placemaker — built-in features of a Fill layer (playground in a park, zoning along new streets…).
//
// A feature is not a separate thing in the recipe: it IS a direct child step of the fill step whose
// Layer and Target match the FeatureInfo. "Enabled" in the UI = such a child exists (and is
// enabled). Toggling a feature adds or removes that child (an undoable design edit, see
// DesignEditor.SetFeature). Any other child is a free-form mix, which the Make row hides for now
// (FEEDBACK-2: shelve mixing in the UI; the engine keeps supporting it).

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Tool
{
    public static class Features
    {
        /// <summary>Features a layer offers (only Fill layers implementing IHasFeatures; never null).</summary>
        public static IReadOnlyList<FeatureInfo> Of(IAreaGenerator gen)
        {
            if (gen == null || gen.Kind != LayerKind.Fill || !(gen is IHasFeatures hf)) return Array.Empty<FeatureInfo>();
            // (issue #2) the Decals feature placed only decals from decal packs, which plans no longer keep (PackDecals)
            try { return hf.Features == null ? (IReadOnlyList<FeatureInfo>)Array.Empty<FeatureInfo>() : Visible(gen, hf.Features); }
            catch (Exception) { return Array.Empty<FeatureInfo>(); }
        }

        // (Phase 1) Look features (big trees, rocky areas, water…) are settings of the type, not add-ons (Layers/Common/LookSettings),
        // and retired ones (wildlife) are offered by no type.
        private static bool Hidden(IAreaGenerator gen, FeatureInfo f) =>
            f != null && (Decals.PackDecals.IsHiddenFeature(f.Layer) || Layers.Common.LookSettings.IsLookOf(gen, f.Layer) || Layers.Common.LookSettings.IsRetired(f.Layer));

        private static IReadOnlyList<FeatureInfo> Visible(IAreaGenerator gen, IReadOnlyList<FeatureInfo> all)
        {
            foreach (var f in all)
                if (Hidden(gen, f))
                {
                    var list = new List<FeatureInfo>(all.Count);
                    foreach (var g in all) if (g == null || !Hidden(gen, g)) list.Add(g);
                    return list;
                }
            return all;
        }

        public static FeatureInfo Find(IAreaGenerator gen, string featureLayer)
        {
            if (string.IsNullOrEmpty(featureLayer)) return null;
            foreach (var f in Of(gen)) if (f != null && f.Layer == featureLayer) return f;
            return null;
        }

        /// <summary>Index of the child step implementing <paramref name="f"/> under <paramref name="parent"/>, or -1.</summary>
        public static int ChildIndex(RecipeStep parent, FeatureInfo f)
        {
            if (parent == null || f == null) return -1;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                var c = parent.Children[i];
                if (c != null && c.Layer == f.Layer && (c.Target ?? "") == (f.Target ?? "")) return i;
            }
            return -1;
        }

        public static bool IsFeatureChild(IAreaGenerator parentGen, RecipeStep child)
        {
            if (child == null) return false;
            foreach (var f in Of(parentGen))
                if (f != null && f.Layer == child.Layer && (f.Target ?? "") == (child.Target ?? "")) return true;
            return false;
        }

        /// <summary>
        /// Make-row rule (2026-10-13, single types only): a shipped recipe is listed when its root is a
        /// Fill layer with no children and no region overrides — i.e. exactly one type. Shipped mixes
        /// (roads+parks, split park/plaza) and "type + feature" variants (park with playground, roads
        /// with zoning) are hidden: the features are toggles on the plain type now. Player recipes are
        /// always listed (the player made them on purpose). Hidden recipes stay loadable by id.
        /// </summary>
        public static bool ShowInMakeRow(Recipe recipe, bool custom, Func<string, IAreaGenerator> resolve)
        {
            if (recipe?.Root == null) return false;
            if (custom) return true;
            if (recipe.Root.Children.Count > 0 || recipe.Root.RegionOverrides.Count > 0) return false;
            IAreaGenerator gen = null;
            try { gen = resolve?.Invoke(recipe.Root.Layer); } catch (Exception) { gen = null; }
            return gen == null || gen.Kind == LayerKind.Fill;
        }
    }
}
