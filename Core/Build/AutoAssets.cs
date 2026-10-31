// Placemaker — turning automatic asset refs (empty Id) into concrete prefab names (GPL-3.0).
// US5 "honest price": preview, Pricing.Of and the build must use the SAME prefabs, so the choice is
// made once, deterministically, before any of them run. Pure; the game catalog supplies the pools.
//
// FEEDBACK-2 #1 (consistency): an automatic build uses ONE asset per (step, category) — one fence,
// one bench, one lamp, one bin, one surface, one road… Variety (by PlannedObject.Variation) only
// for vegetation and a few "set" categories (playground equipment, tents), and even then drawn
// from one coherent vanilla park palette per step when the palette has that category.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    /// <summary>What the catalog offers the resolver. Every list is already filtered to automatic-eligible items.</summary>
    public sealed class AutoAssetSource
    {
        /// <summary>Ordered automatic candidates of a category (first = category default; null/empty = none).</summary>
        public Func<string, IReadOnlyList<string>> Pool;
        /// <summary>True when a prefab of that name is loaded (null = everything exists).</summary>
        public Func<string, bool> Exists;
        /// <summary>Palette names usable for automatic dressing, in a stable order (null/empty = no palettes).</summary>
        public IReadOnlyList<string> Palettes;
        /// <summary>(palette, category) → ordered eligible items of that category seen in the palette.</summary>
        public Func<string, string, IReadOnlyList<string>> PaletteItems;
        /// <summary>True for a built-in (vanilla) prefab; null = everything counts as vanilla (pure tests). A non-vanilla prefab is
        /// used only when the player picked it (ComposedPlan.Picks): Placemaker's own choices are vanilla (the user, 26 Oct).</summary>
        public Func<string, bool> Vanilla;
    }

    public static class AutoAssets
    {
        /// <summary>Categories where automatic objects vary by PlannedObject.Variation (natural look).</summary>
        public static readonly HashSet<string> VarietyCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Flowerbed,
            // Sets, not furniture: five identical swings or tents read as a copy-paste error.
            AssetCategories.Playground, AssetCategories.CampingPitch,
            // Natural scatter: a rocky outcrop or a leaf/dirt decal scatter of one prefab looks stamped.
            AssetCategories.Rock, AssetCategories.Decal, AssetCategories.DecalLeaves, AssetCategories.DecalDirt,
            AssetCategories.DecalCracks, AssetCategories.DecalPuddles, AssetCategories.DecalPaving, AssetCategories.DecalGraffiti,
        };

        /// <summary>Categories drawn from the step's park palette when the palette has items of them.</summary>
        public static readonly HashSet<string> PaletteCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            AssetCategories.Tree, AssetCategories.Bush, AssetCategories.Flowerbed, AssetCategories.Planter,
            AssetCategories.Bench, AssetCategories.Lamp, AssetCategories.Bin, AssetCategories.Centerpiece,
            AssetCategories.Playground, AssetCategories.Picnic, AssetCategories.Shelter, AssetCategories.Kiosk,
            AssetCategories.Restroom, AssetCategories.Bollard, AssetCategories.Decoration,
        };

        /// <summary>Largest number of distinct automatic assets in a variety category per step.</summary>
        public const int MaxVariety = 6;

        /// <summary>Fewest palette items a variety category needs before the palette replaces the pool.</summary>
        public const int MinPaletteVariety = 3;

        /// <summary>True when every automatic object of this category in a step is the same prefab.</summary>
        public static bool IsUniform(string category) => category == null || (!VarietyCategories.Contains(category) && !VarietyRegistry.Contains(category));

        /// <summary>
        /// Random seed for game objects/edges of a uniform category: the same within a build, so colour
        /// and mesh variations of one prefab do not differ from piece to piece (fence pieces, bench rows).
        /// </summary>
        public static int UniformSeed(string category, uint salt) => (int)Seeds.Mix(salt, "uniform|" + (category ?? ""));

        /// <summary>The palette of a step: stable for a seed, independent of the plan's contents.</summary>
        public static string PickPalette(IReadOnlyList<string> palettes, uint seed)
        {
            if (palettes == null || palettes.Count == 0) return null;
            return palettes[(int)(Seeds.Mix(seed, "palette") % (uint)palettes.Count)];
        }

        /// <summary>Back-compatible overload: pools only, no palettes.</summary>
        public static int Resolve(ComposedPlan plan, Func<string, IReadOnlyList<string>> pool, Func<string, bool> exists = null) =>
            Resolve(plan, new AutoAssetSource { Pool = pool, Exists = exists });

        /// <summary>
        /// Resolves every automatic ref of the plan in place and returns how many refs were resolved.
        /// Rules (deterministic: same plan + same source → same ids):
        /// • Steps: a layer belongs to the step of its parent layer (add-ons share their park's step);
        ///   a top-level layer, or a child of a split, starts a step. Each step picks one palette
        ///   from its seed.
        /// • Uniform categories (fences, benches, lamps, bins, centerpieces, surfaces, nets, chargers,
        ///   signs…): one id per (step, category) — the palette's best item when it has the category,
        ///   else the pool default.
        /// • Variety categories: pick by hash(Variation) among the first <see cref="MaxVariety"/> palette
        ///   items (or pool items when the palette lacks the category).
        /// • An explicit id that is not loaded falls back to its first loaded Fallback, else as automatic.
        /// </summary>
        /// <summary>
        /// Placemaker's vehicle props ("Placemaker Vehicle Car 1" …; VehiclePropSystem makes them from built-in vehicles only) count as
        /// vanilla. Only that prefix: our other copies ("Placemaker Overlay (", decal copies) can come from asset packs:
        /// without this the car yards built their fallbacks (trailer loads of boxes, cable reels, generators) instead of cars
        /// (industrial makeover, 2026-10-30).
        /// </summary>
        public static bool IsOwn(string id) => id != null && (id.StartsWith(Layers.Types.Yards.VehicleProps.Prefix, StringComparison.Ordinal)
            || id.StartsWith(Layers.Types.Yards.HouseProps.Prefix, StringComparison.Ordinal));   // (P2-3) the farmhouses, from vanilla houses

        public static int Resolve(ComposedPlan plan, AutoAssetSource source)
        {
            if (plan?.Layers == null || source?.Pool == null) return 0;
            // usable = loaded, and vanilla unless the player picked it (hard-coded pack names, modded palette items: left out)
            Func<string, bool> exists = source.Vanilla == null ? source.Exists
                : id => (source.Exists == null || source.Exists(id)) && (source.Vanilla(id) || plan.Picks.Contains(id) || IsOwn(id));
            int resolved = 0;
            var poolCache = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var candCache = new Dictionary<(string, string), IReadOnlyList<string>>();
            var steps = StepKeys(plan);
            var stepPalette = new Dictionary<string, string>(StringComparer.Ordinal);

            IReadOnlyList<string> Pool(string category)
            {
                if (category == null) return null;
                if (!poolCache.TryGetValue(category, out var list)) poolCache[category] = list = source.Pool(category);
                return list;
            }
            IReadOnlyList<string> Candidates(string step, string category)
            {
                if (category == null) return null;
                if (candCache.TryGetValue((step, category), out var list)) return list;
                list = null;
                if (PaletteCategories.Contains(category) && source.PaletteItems != null
                    && stepPalette.TryGetValue(step, out var palette) && palette != null)
                {
                    var items = source.PaletteItems(palette, category);
                    if (items != null && items.Count > 0)
                    {
                        var ok = new List<string>(items.Count);
                        foreach (var id in items) if (!string.IsNullOrEmpty(id) && (exists == null || exists(id))) ok.Add(id);
                        // A variety category needs a real mix: a palette with one birch would plant a
                        // monoculture (playtest 4b), so thin palettes defer to the curated pool.
                        if (ok.Count >= (IsUniform(category) ? 1 : MinPaletteVariety)) list = ok;
                    }
                }
                list = list ?? Pool(category);
                candCache[(step, category)] = list;
                return list;
            }
            string Pick(string step, string category, uint variation)
            {
                var list = Candidates(step, category);
                // An empty custom category (no "rubber" surface installed) walks to its parent (g5).
                if (list == null || list.Count == 0)
                    foreach (var parent in CategoryFallbacks.Chain(category))
                    {
                        if (parent == category) continue;
                        list = Candidates(step, parent);
                        if (list != null && list.Count > 0) break;
                    }
                if (list == null || list.Count == 0) return null;
                if (IsUniform(category) || list.Count == 1) return list[0];
                int n = Math.Min(list.Count, MaxVariety);
                return list[(int)(Seeds.Mix(variation, category) % (uint)n)];
            }

            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l == null) continue;
                var step = steps[li];
                if (!stepPalette.ContainsKey(step)) stepPalette[step] = PickPalette(source.Palettes, l.Seed);
                foreach (var n in l.Nets)
                    if (n != null && Needs(n.Asset, exists))
                    {
                        string cat = RoleCategories.ForNet(n);
                        string id = n.Role == NetRole.Alley && exists != null && exists(AlleyPrefab) ? AlleyPrefab
                            : (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide) && cat == AssetCategories.Pathway
                                ? PickWalk(Candidates(step, cat), n.Role == NetRole.PathwayWide) ?? Pick(step, cat, 0)
                                : Pick(step, cat, 0);
                        resolved += Set(ref n.Asset, id, exists);
                    }
                foreach (var s in l.Surfaces)
                    if (s != null && Needs(s.Asset, exists)) resolved += Set(ref s.Asset, Pick(step, RoleCategories.ForSurface(s), 0), exists);
                // (core) A lot area is never automatic: every area.lot in the game is a vanilla industry lot ("Agriculture Lot",
                // "Extractor Lot", ...) with its own economy, a farm-field surface, a border fence and CanOverrideObjects (it hid every
                // tree, bush and lamp inside parking lots). An empty lot asset stays empty and is not built (PlanWork.Flatten).
                foreach (var a in l.Areas)
                    if (a != null && a.Role != AreaRole.Lot && Needs(a.Asset, exists)) resolved += Set(ref a.Asset, Pick(step, RoleCategories.ForArea(a.Role), 0), exists);
                foreach (var o in l.Objects)
                {
                    if (o == null || !Needs(o.Asset, exists)) continue;
                    resolved += Set(ref o.Asset, Pick(step, RoleCategories.ForObject(o), o.Variation), exists);
                }
            }
            return resolved;
        }

        /// <summary>
        /// Step key per layer (same order as plan.Layers). A layer joins the step of the layer at its
        /// parent recipe path unless that parent is a split (whose children are separate steps).
        /// </summary>
        public static string[] StepKeys(ComposedPlan plan)
        {
            var keys = new string[plan.Layers.Count];
            var byPath = new Dictionary<string, (string step, string gen)>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Layers.Count; i++)
            {
                var l = plan.Layers[i];
                string path = l?.StepPath ?? "";
                int at = path.IndexOf('@');
                if (at >= 0) path = path.Substring(0, at);
                string step = path.Length > 0 ? path : "#" + i;
                int slash = path.LastIndexOf('/');
                if (slash > 0 && byPath.TryGetValue(path.Substring(0, slash), out var parent) && parent.gen != SplitGeneratorId)
                    step = parent.step;
                keys[i] = step;
                if (path.Length > 0 && !byPath.ContainsKey(path)) byPath[path] = (step, l?.GeneratorId ?? "");
            }
            return keys;
        }

        private const string SplitGeneratorId = "split";

        /// <summary>
        /// (FEEDBACK-4) The automatic ground walk: never a bridge, pier or quay; a plain thin path (vanilla
        /// "Pavement Path", 3 m) for Pathway, a wide one only for PathwayWide. Null when the pool has no ground walk.
        /// </summary>
        public static string PickWalk(IReadOnlyList<string> pool, bool wide)
        {
            if (pool == null) return null;
            string best = null; int bestScore = int.MinValue;
            for (int i = 0; i < pool.Count; i++)
            {
                var id = pool[i];
                int s = WalkScore(id, wide);
                if (s == int.MinValue) continue;
                s = s * 64 - i;   // pool order breaks ties
                if (s > bestScore) { bestScore = s; best = id; }
            }
            return best;
        }

        /// <summary>Rank of a pool item as an automatic ground walk (int.MinValue = never).</summary>
        public static int WalkScore(string id, bool wide)
        {
            if (string.IsNullOrEmpty(id)) return int.MinValue;
            var lower = id.ToLowerInvariant();
            foreach (var bad in new[] { "bridge", "pier", "quay", "boardwalk", "tunnel", "elevated", "bike", "cycle" })
                if (lower.Contains(bad)) return int.MinValue;
            bool isWide = lower.Contains("wide");
            int s = 0;
            if (lower.Contains("pavement path")) s += 4;
            else if (lower.Contains("path")) s += 2;
            if (lower.Contains("gravel")) s -= 1;
            s += isWide == wide ? 6 : 0;
            return s;
        }

        /// <summary>Vanilla two-way alley (PLT: never "Alley Oneway", it has 2.5 m parking strips).</summary>
        public const string AlleyPrefab = "Alley";

        public static bool IsAuto(AssetRef a) => string.IsNullOrEmpty(a.Id);

        /// <summary>Automatic, or an explicit id this playset does not have (then: fallbacks → default).</summary>
        private static bool Needs(AssetRef a, Func<string, bool> exists) => IsAuto(a) || (exists != null && !exists(a.Id));

        private static int Set(ref AssetRef asset, string id, Func<string, bool> exists)
        {
            // An automatic ref with explicit fallbacks keeps the catalog order: first existing fallback wins.
            // (s16) Exists == null means "everything exists" (pure tests): the first fallback wins then too.
            if (asset.Fallbacks != null)
                foreach (var f in asset.Fallbacks)
                    if (!string.IsNullOrEmpty(f) && (exists == null || exists(f))) { id = f; break; }
            if (string.IsNullOrEmpty(id)) return 0;
            // Keep the original fallbacks: they still apply if the chosen asset disappears later.
            asset = new AssetRef(id, asset.Fallbacks ?? Array.Empty<string>());
            return 1;
        }
    }
}
