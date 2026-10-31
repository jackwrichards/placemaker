// Placemaker — the "Make" row: shipped recipes (Recipes/*.json in the mod folder), player recipes
// (ModsData/Placemaker/recipes/*.json) and a synthesized one-layer recipe for every registered
// Fill layer that has no shipped file, so a new layer shows up without anyone writing JSON.

using System;
using System.Collections.Generic;
using System.IO;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Settings;

namespace Placemaker.Game.Recipes
{
    public sealed class RecipeStore
    {
        public sealed class Entry
        {
            public Recipe Recipe;
            public bool Custom;
            public string DescriptionKey = "";
            public int Order;
            public string FilePath;   // player recipes only
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Func<DesignEditor> _editor;

        /// <summary>Bumped on every reload/save/delete so the catalog binding knows to refresh.</summary>
        public int Version { get; private set; }

        public RecipeStore(Func<DesignEditor> editor) { _editor = editor; }

        public IReadOnlyList<Entry> Entries => _entries;

        public Entry Find(string id)
        {
            foreach (var e in _entries) if (e.Recipe.Id == id) return e;
            return null;
        }

        /// <summary>A fresh, editable copy of a recipe (with saved defaults for one-layer recipes).</summary>
        public Recipe Instantiate(string id, uint seed)
        {
            var e = Find(id);
            if (e == null) return null;
            var r = e.Recipe.Clone();
            if (r.Root.Seed == 0) r.Root.Seed = seed;
            return r;
        }

        private static bool s_shelfLoaded;

        /// <summary>
        /// (core) Reads the central shelf (Recipes/shelf/*.json in the mod folder: one file per Make-row category) into
        /// <see cref="Shelf.Current"/>: which categories exist and in which order, which types and styles are hidden.
        /// </summary>
        public static Shelf LoadShelf()
        {
            var files = new List<KeyValuePair<string, string>>();
            string dir = ModPaths.ModFolder != null ? Path.Combine(ModPaths.ModFolder, "Recipes", Shelf.FolderName) : null;
            try
            {
                if (dir != null && Directory.Exists(dir))
                {
                    var paths = Directory.GetFiles(dir, "*.json");
                    Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
                    foreach (var p in paths) files.Add(new KeyValuePair<string, string>(Path.GetFileName(p), File.ReadAllText(p)));
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: cannot read the shelf in " + dir + ": " + e.Message); }
            var shelf = Shelf.Parse(files);
            Shelf.Current = shelf;
            s_shelfLoaded = true;
            int types = 0, styles = 0;
            var shown = new List<string>();
            foreach (var c in shelf.Categories)
            {
                types += c.HiddenTypes.Count;
                foreach (var kv in c.HiddenStyles) styles += kv.Value.Count;
                shown.Add(c.Hidden ? c.Id + " (hidden)" : c.Id);
            }
            Mod.Log.Info($"Placemaker shelf: {shelf.Categories.Count} categories from {files.Count} file(s) [{string.Join(", ", shown)}], {types} hidden type(s), {styles} hidden style(s).");
            foreach (var w in shelf.Warnings) Mod.Log.Warn("Placemaker shelf: " + w);
            return shelf;
        }

        /// <summary>(core) A shipped type the shelf hides (its category is hidden or it is in hiddenTypes). Player recipes never are.</summary>
        public bool IsShelved(string id)
        {
            var e = Find(id);
            if (e == null || e.Custom) return false;
            var g = GeneratorRegistry.Get(e.Recipe?.Root?.Layer ?? id) ?? GeneratorRegistry.Get(id);
            return g != null && Shelf.Current.IsTypeShelved(g.Id, GeneratorGroups.Of(g));
        }

        /// <summary>(core) The first type the Make row shows: the first category in shelf order, then the recipe order.</summary>
        public string FirstShown()
        {
            string best = null;
            int bestOrder = int.MaxValue;
            foreach (var e in _entries)
            {
                if (e.Custom || e.Recipe == null) continue;
                var g = GeneratorRegistry.Get(e.Recipe.Id);
                if (g == null || g.Kind != LayerKind.Fill || GeneratorRegistry.IsHidden(g)) continue;
                var grp = GeneratorGroups.Of(g);
                if (string.IsNullOrEmpty(grp) || Shelf.Current.IsTypeShelved(g.Id, grp)) continue;
                var cat = Shelf.Current.Category(Shelf.Current.CategoryOf(g.Id, grp));
                int order = cat != null ? cat.Order : int.MaxValue - 1;
                if (order < bestOrder) { bestOrder = order; best = e.Recipe.Id; }
            }
            return best;
        }

        /// <summary>The shelf, read once (the game runner may need it before the tool opens).</summary>
        public static Shelf EnsureShelf() => s_shelfLoaded ? Shelf.Current : LoadShelf();

        public void Reload()
        {
            LoadShelf();
            _entries.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (ModPaths.ModFolder != null)
                foreach (var e in LoadFolder(Path.Combine(ModPaths.ModFolder, "Recipes"), false))
                    if (seen.Add(e.Recipe.Id)) _entries.Add(e);

            // Every Fill layer is makeable even without a shipped file.
            var editor = _editor();
            foreach (var g in GeneratorRegistry.All)
            {
                if (g == null || g.Kind != LayerKind.Fill || GeneratorRegistry.IsHidden(g) || seen.Contains(g.Id)) continue;
                try
                {
                    var r = editor.SingleLayerRecipe(g, 0);
                    seen.Add(r.Id);
                    _entries.Add(new Entry { Recipe = r, Order = LayerOrder(g.Id) });
                }
                catch (Exception ex) { Mod.Log.Warn("Placemaker: layer " + g.Id + " has no usable defaults: " + ex.Message); }
            }

            foreach (var e in LoadFolder(ModPaths.PlayerRecipeFolder, true))
                if (seen.Add(e.Recipe.Id)) _entries.Add(e);

            _entries.Sort((a, b) =>
            {
                if (a.Custom != b.Custom) return a.Custom ? 1 : -1;
                int c = a.Order.CompareTo(b.Order);
                return c != 0 ? c : string.CompareOrdinal(a.Recipe.Id, b.Recipe.Id);
            });
            Version++;
        }

        private IEnumerable<Entry> LoadFolder(string folder, bool custom)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;
            string[] files;
            try { files = Directory.GetFiles(folder, "*.json"); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: cannot list " + folder + ": " + e.Message); return list; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var editor = _editor();
            foreach (var path in files)
            {
                try
                {
                    var file = RecipeJson.Parse(File.ReadAllText(path));
                    var r = file.Recipe;
                    if (file.FormatVersion > RecipeJson.CurrentFormatVersion)
                    {
                        // A newer format may mean something different: skip rather than build the wrong thing.
                        Mod.Log.Warn("Placemaker: skipping " + Path.GetFileName(path) + " — format " + file.FormatVersion + " is newer than this version understands");
                        continue;
                    }
                    if (string.IsNullOrEmpty(r.Id)) r.Id = Path.GetFileNameWithoutExtension(path);
                    if (GeneratorRegistry.Get(r.Root.Layer) == null)
                        Mod.Log.Info("Placemaker: recipe " + r.Id + " uses unknown layer '" + r.Root.Layer + "' (mod missing?) — listed, will report an issue");
                    if (custom && string.IsNullOrEmpty(r.Name) && string.IsNullOrEmpty(r.NameKey)) r.Name = r.Id;
                    // Files hold overrides only (player recipes hold full values; overlaying is harmless).
                    editor.ApplyDefaults(r);
                    foreach (var kv in RecipePaths.Walk(r)) { kv.Value.Markers.Clear(); kv.Value.Touches.Clear(); }
                    int order = file.Order != 1000 ? file.Order : LayerOrder(r.Root.Layer);
                    list.Add(new Entry { Recipe = r, Custom = custom, DescriptionKey = file.DescriptionKey, Order = order, FilePath = custom ? path : null });
                }
                catch (Exception e)
                {
                    Mod.Log.Warn("Placemaker: skipping recipe " + Path.GetFileName(path) + ": " + e.Message);
                }
            }
            return list;
        }

        /// <summary>Make-row order: the root layer's [AreaGenerator(order)] position (spec: then file name).</summary>
        private static int LayerOrder(string layer)
        {
            var all = GeneratorRegistry.All;
            for (int i = 0; i < all.Count; i++) if (all[i].Id == layer) return i * 10;
            return 10000;
        }

        /// <summary>Saves the design as a player recipe. Returns its id, or null on failure.</summary>
        public string SaveAs(Recipe design, string name, uint salt)
        {
            if (design?.Root == null) return null;
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "My recipe";
            if (name.Length > 60) name = name.Substring(0, 60);
            var r = design.Clone();
            r.Id = RecipeJson.NewPlayerRecipeId(name, salt);
            r.Name = name;
            r.NameKey = "";
            foreach (var kv in RecipePaths.Walk(r)) { kv.Value.Markers.Clear(); kv.Value.Touches.Clear(); }   // markers belong to one site, not to a recipe
            var path = Path.Combine(ModPaths.PlayerRecipeFolder, r.Id + ".json");
            try
            {
                ModPaths.WriteAllTextAtomic(path, RecipeJson.Write(r).Write(true));
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: could not save recipe: " + e.Message);
                return null;
            }
            Reload();
            return r.Id;
        }

        /// <summary>Deletes a player recipe file (shipped/synthesized recipes cannot be deleted).</summary>
        public bool Delete(string id)
        {
            var e = Find(id);
            if (e == null || !e.Custom || string.IsNullOrEmpty(e.FilePath)) return false;
            try
            {
                // Keep a backup next to it instead of destroying the player's work outright.
                var bak = e.FilePath + ".deleted";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(e.FilePath, bak);
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Placemaker: could not delete recipe " + id + ": " + ex.Message);
                return false;
            }
            Reload();
            return true;
        }
    }
}
