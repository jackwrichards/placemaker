// Design edits triggered by the panel (contract.ts Triggers). Every committed edit is one undo
// step; slider drags preview live (previewValue) and become ONE step on endPreview.

using System;
using Placemaker.Core;
using Placemaker.Core.Tool;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private Recipe _previewBase;          // committed recipe while a slider drag previews
        private ValuePreviewKind _previewKind;   // drag (slider) or hover (asset picker) while _previewBase is set
        private int _designVersion;
        private int _modifiedVersion = -1;
        private bool _modified;

        // the last design of each type this session (style, zoning, every choice): picking the type again brings it back.
        // No type stays chosen after a build, so without this every build started from the type's defaults.
        private readonly System.Collections.Generic.Dictionary<string, (Recipe Recipe, string PresetLayer, string PresetId)> _lastDesigns =
            new System.Collections.Generic.Dictionary<string, (Recipe, string, string)>(StringComparer.Ordinal);

        /// <summary>The design on the panel, as its type's last (not an edit of a built group: that one is the receipt's).</summary>
        private void RememberDesign()
        {
            if (_recipe == null || string.IsNullOrEmpty(_recipeId) || _editingGroup != 0) return;
            _lastDesigns[_recipeId] = (_recipe.Clone(), _presetLayer ?? "", _presetId ?? "");
        }

        public int DesignVersion => _designVersion;
        public bool ValuePreviewActive => _previewBase != null;
        public ValuePreviewKind ValuePreviewKind => _previewBase != null ? _previewKind : ValuePreviewKind.None;

        private void DesignChanged(bool preview = false)
        {
            _designVersion++;
            _inputRevision++;
            _lastInputChange = UnityEngine.Time.realtimeSinceStartup;
            ClearBuildFeedback(false);
            Touch();
        }

        /// <summary>Applies one edit to the live recipe and commits it as an undo step when it changed something.</summary>
        private bool Edit(Func<Recipe, bool> change, string label, string coalesce = null)
        {
            EndValuePreview(true);
            if (_recipe == null) return false;
            var working = _recipe.Clone();
            bool changed;
            try { changed = change(working); }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: design edit failed: " + e.Message);
                return false;
            }
            if (!changed) return false;
            _recipe = working;
            DesignChanged();
            Commit(label, coalesce);
            return true;
        }

        public bool Modified
        {
            get
            {
                if (_modifiedVersion == _designVersion) return _modified;
                _modifiedVersion = _designVersion;
                _modified = false;
                var baseEntry = _recipes.Find(_recipeId);
                if (_recipe != null && baseEntry != null)
                    _modified = Fingerprint(_recipe) != Fingerprint(baseEntry.Recipe);
                return _modified;
            }
        }

        // Seeds and markers are per-site; they do not make a design "modified".
        private static string Fingerprint(Recipe r)
        {
            var c = r.Root.Clone();
            foreach (var kv in RecipePaths.Walk(new Recipe { Root = c })) { kv.Value.Markers.Clear(); kv.Value.Touches.Clear(); kv.Value.Seed = 0; }
            return RecipeJson.WriteStep(c).ToString();
        }

        // ───────────── triggers ─────────────

        public void SelectRecipe(string id)
        {
            EndValuePreview(true);
            // the painter is picked like a type (it is not one: no recipe, nothing planned)
            if (id == PainterId) { SetPainter(true); return; }
            bool leftPainter = SetPainter(false);
            if (string.IsNullOrEmpty(id) || id == _recipeId && _recipe != null) { if (leftPainter) DesignChanged(); return; }
            if (!SwitchRecipe(id)) return;
            DesignChanged();
            Commit("Placemaker.Undo.SelectRecipe");
        }

        /// <summary>
        /// Replaces the design with recipe <paramref name="id"/> (area kept): the player's last design of that type this session
        /// (new seeds, no entrances, its style), else a fresh copy on the type's Standard style. No undo step.
        /// </summary>
        private bool SwitchRecipe(string id)
        {
            Recipe r;
            string presetLayer = "", presetId = "";
            if (_lastDesigns.TryGetValue(id, out var last))
            {
                r = last.Recipe.Clone();
                foreach (var kv in RecipePaths.Walk(r)) { kv.Value.Markers.Clear(); kv.Value.Touches.Clear(); kv.Value.Seed = NewSeed(); }
                presetLayer = last.PresetLayer;
                presetId = last.PresetId;
            }
            else
            {
                r = _recipes.Instantiate(id, NewSeed());
                if (r == null) { Flash(new LocText("Placemaker.Problem.UnknownRecipe")); return false; }
                _editor.AddDefaultFeatures(r, NewSeed);
                // A type starts on its Standard style (its own design). A Standard the player saved over, or one the type ships
                // that differs from its recipe, is applied, so the panel shows it picked and unchanged.
                presetLayer = r.Root.Layer;
                presetId = _presets.Exists(presetLayer, PresetStore.StandardId) ? PresetStore.StandardId : "";
                if (presetId.Length > 0 && RecipeFor(presetLayer) == id && _presets.IsModified(r.Root, presetId))
                    _presets.Apply(r.Root, presetId, NewSeed);
            }
            RememberDesign();   // the design being left
            // Markers the player placed for the same root layer stay (switching park ↔ park+playground keeps entrances).
            bool sameRoot = _recipe != null && _recipe.Root.Layer == r.Root.Layer;
            if (sameRoot && _recipe.Root.Markers.Count > 0)
                r.Root.Markers.AddRange(_recipe.Root.Markers);
            bool rootEdited = sameRoot && _markersEdited.Contains(RecipePaths.Root);
            _markersEdited.Clear();
            if (rootEdited) _markersEdited.Add(RecipePaths.Root);
            _recipe = r;
            _recipeId = id;
            _presetLayer = presetLayer;
            _presetId = presetId;
            _focus = RecipePaths.Root;
            ExitMarkerMode();
            if (_settings != null) { _settings.LastRecipe = id; _settings.ApplyAndSave(); }
            return true;
        }

        public void FocusStep(string path)
        {
            if (RecipePaths.Resolve(_recipe, path) == null || _focus == path) return;
            _focus = path;
            Touch();   // focus is view state: not an undo step on its own
        }

        public void AddLayer(string parentPath, string layerId, string target)
        {
            string added = null;
            if (Edit(r => (added = _editor.AddLayer(r, parentPath, layerId, target, NewSeed())) != null, "Placemaker.Undo.AddLayer"))
                _focus = added;
        }

        public void RemoveStep(string path)
        {
            if (Edit(r => _editor.RemoveStep(r, path), "Placemaker.Undo.RemoveLayer"))
            {
                if (RecipePaths.Resolve(_recipe, _focus) == null) _focus = RecipePaths.ParentOf(path) ?? RecipePaths.Root;
                if (_markerPath != null && RecipePaths.Resolve(_recipe, _markerPath) == null) ExitMarkerMode();
            }
        }

        public void MoveStep(string path, int delta)
        {
            string moved = null;
            if (Edit(r => (moved = _editor.MoveStep(r, path, delta)) != null, "Placemaker.Undo.MoveLayer") && _focus == path)
                _focus = moved;
        }

        public void SetStepEnabled(string path, bool enabled) =>
            Edit(r => _editor.SetEnabled(r, path, enabled), enabled ? "Placemaker.Undo.EnableLayer" : "Placemaker.Undo.DisableLayer");

        /// <summary>UI: turn a built-in feature (playground, zoning…) of the fill step at path on or off — adds/removes its child step.</summary>
        public void SetFeature(string path, string featureLayer, bool enabled)
        {
            if (!Edit(r => _editor.SetFeature(r, path, featureLayer, enabled, NewSeed()),
                    enabled ? "Placemaker.Undo.EnableFeature" : "Placemaker.Undo.DisableFeature") || enabled) return;
            if (RecipePaths.Resolve(_recipe, _focus) == null) _focus = path;
            if (_markerPath != null && RecipePaths.Resolve(_recipe, _markerPath) == null) ExitMarkerMode();
        }

        public void SetStepTarget(string path, string target) =>
            Edit(r => _editor.SetTarget(r, path, target), "Placemaker.Undo.SetTarget");

        public void OverrideRegion(string parentPath, string regionKey, string layerId) =>
            Edit(r => _editor.OverrideRegion(r, parentPath, regionKey, layerId, NewSeed()),
                string.IsNullOrEmpty(layerId) ? "Placemaker.Undo.RemoveOverride" : "Placemaker.Undo.OverrideRegion");

        public void SetValue(string path, string controlId, string valueJson)
        {
            var v = ParseValue(valueJson);
            if (v == null) return;
            if (_previewBase != null)
            {
                // A drag that ends with setValue instead of endPreview: apply onto the committed base so the step is one undo entry.
                _recipe = _previewBase;
                _previewBase = null;
                _previewKind = ValuePreviewKind.None;
            }
            Edit(r => _editor.SetValue(r, path, controlId, v, true), "Placemaker.Undo.ChangeValue", path + "|" + controlId);
        }

        /// <summary>
        /// Live value, no undo step yet: a slider drag (re-plans debounced, always shown as sketch; endPreview
        /// commits ONE step) or a hovered asset in a picker (ghost/sketch follow it; endPreview reverts,
        /// setValue commits).
        /// </summary>
        public void PreviewValue(string path, string controlId, string valueJson)
        {
            var v = ParseValue(valueJson);
            if (v == null || _recipe == null) return;
            var step = RecipePaths.Resolve(_recipe, path);
            _previewKind = ValuePreviews.KindFor(step != null ? DesignEditor.FindControl(GeneratorRegistry.Get(step.Layer), controlId) : null);
            if (_previewBase == null) _previewBase = _recipe.Clone();
            var working = _recipe.Clone();
            if (!_editor.SetValue(working, path, controlId, v, true)) return;
            _recipe = working;
            DesignChanged(preview: true);
        }

        public void EndPreview() => EndValuePreview(true);

        private void EndValuePreview(bool commit)
        {
            if (_previewBase == null) return;
            var baseRecipe = _previewBase;
            _previewBase = null;
            // A hovered asset is only a look: leaving the picker (or any other edit) reverts it.
            if (_previewKind == ValuePreviewKind.Hover) commit = false;
            _previewKind = ValuePreviewKind.None;
            if (!commit)
            {
                _recipe = baseRecipe;
                DesignChanged();
                return;
            }
            DesignChanged();
            Commit("Placemaker.Undo.ChangeValue");
        }

        public void ResetStep(string path) => Edit(r => _editor.ResetStep(r, path), "Placemaker.Undo.ResetLayer");


        public void Randomize(string path) => Edit(r => _editor.Randomize(r, path ?? "", NewSeed()), "Placemaker.Undo.Randomize");

        public void UnlockValue(string path, string controlId) => Edit(r => _editor.UnlockValue(r, path, controlId), "Placemaker.Undo.UnlockValue");

        public void SaveRecipeAs(string name)
        {
            EndValuePreview(true);
            var id = _recipes.SaveAs(_recipe, name, NewSeed());
            if (id == null) { Flash(new LocText("Placemaker.Problem.SaveFailed")); return; }
            _recipeId = id;
            _modifiedVersion = -1;
            if (_settings != null) { _settings.LastRecipe = id; _settings.ApplyAndSave(); }
            Flash(new LocText("Placemaker.Hint.RecipeSaved"));
            Touch();
        }

        public void DeleteRecipe(string id)
        {
            if (!_recipes.Delete(id)) { Flash(new LocText("Placemaker.Problem.CannotDeleteRecipe")); return; }
            // The design itself stays (and is undoable); it is just no longer linked to a saved recipe.
            if (_recipeId == id) { _recipeId = ""; _modifiedVersion = -1; }
            Touch();
        }

        public void SaveAsDefault(string path)
        {
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null || string.IsNullOrEmpty(step.Layer)) return;
            _defaults.Set(step.Layer, step.Settings);
            _recipes.Reload();          // one-layer recipes pick up the new defaults
            _modifiedVersion = -1;
            Flash(new LocText("Placemaker.Hint.DefaultsSaved"));
            Touch();
        }

        public void ResetAllDefaults()
        {
            _defaults.Clear();
            _recipes.Reload();
            _modifiedVersion = -1;
            Touch();
        }

        private static object ParseValue(string json)
        {
            if (json == null) return null;
            if (!JsonValue.TryParse(json, out var v, out _))
                return json;   // tolerate a bare string from the UI
            return DesignEditor.FromJson(v);
        }
    }
}
