// Placemaker — every design edit the panel can trigger, as pure operations on a Recipe.
//
// The tool clones the committed recipe, applies one operation, and commits the result to History
// when the operation reports a change. Nothing here throws for bad input from the UI (unknown
// path, wrong value type): it returns false / null and the edit is ignored.

using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public sealed class DesignEditor
    {
        private readonly Func<string, IAreaGenerator> _resolve;
        private readonly Func<string, SettingsBag> _savedDefaults;

        /// <param name="resolve">Layer id → generator (GeneratorRegistry.Get in the game).</param>
        /// <param name="savedDefaults">Player-saved defaults for a layer (overlaid on the generator defaults), or null.</param>
        public DesignEditor(Func<string, IAreaGenerator> resolve, Func<string, SettingsBag> savedDefaults = null)
        {
            _resolve = resolve ?? (_ => null);
            _savedDefaults = savedDefaults ?? (_ => null);
        }

        public IAreaGenerator Generator(string layerId) => string.IsNullOrEmpty(layerId) ? null : _resolve(layerId);

        /// <summary>Generator defaults, then the player's saved defaults for that layer on top.</summary>
        public SettingsBag DefaultsFor(string layerId)
        {
            var gen = Generator(layerId);
            SettingsBag bag;
            try { bag = gen?.Defaults()?.Clone() ?? new SettingsBag(); }
            catch (Exception) { bag = new SettingsBag(); }
            var saved = _savedDefaults(layerId);
            if (saved != null)
                foreach (var kv in saved.Values)
                {
                    var schema = FindControl(gen, kv.Key);
                    if (gen != null && schema == null) continue;           // stale id from an older version
                    var coerced = schema != null ? Coerce(schema, kv.Value) : kv.Value;
                    if (coerced != null) bag.Values[kv.Key] = coerced is string[] a ? (string[])a.Clone() : coerced;
                }
            return bag;
        }

        /// <summary>
        /// Recipe files store only overrides (docs/proposals/recipe-json.md): every step's settings
        /// become generator defaults (+ saved defaults) with the file values on top. Unknown ids are
        /// kept for forward compatibility; known ids are coerced to their control type.
        /// </summary>
        public void ApplyDefaults(Recipe recipe)
        {
            if (recipe?.Root == null) return;
            foreach (var kv in RecipePaths.Walk(recipe))
            {
                var step = kv.Value;
                var gen = Generator(step.Layer);
                var merged = DefaultsFor(step.Layer);
                foreach (var v in step.Settings.Values)
                {
                    var schema = FindControl(gen, v.Key);
                    var coerced = schema != null ? Coerce(schema, Migrated(gen, v.Key, v.Value)) : v.Value;
                    if (coerced != null) merged.Values[v.Key] = coerced;
                }
                step.Settings = merged;
            }
        }

        public RecipeStep NewStep(string layerId, string target, uint seed) => new RecipeStep
        {
            Layer = layerId ?? "",
            Target = target ?? "",
            Settings = DefaultsFor(layerId),
            Seed = seed,
        };

        /// <summary>Recipe for a layer that has no shipped recipe file: that layer alone with its defaults.</summary>
        public Recipe SingleLayerRecipe(IAreaGenerator gen, uint seed)
        {
            var r = Recipe.Single(gen, seed);
            r.Root.Settings = DefaultsFor(gen.Id);
            return r;
        }

        // ───────────── structure ─────────────

        /// <summary>Adds a child layer. Returns the new step's path or null.</summary>
        public string AddLayer(Recipe recipe, string parentPath, string layerId, string target, uint seed)
        {
            var parent = RecipePaths.Resolve(recipe, parentPath);
            if (parent == null || string.IsNullOrEmpty(layerId)) return null;
            if (Depth(parentPath) >= Composer.MaxDepth) return null;
            parent.Children.Add(NewStep(layerId, target, seed));
            return RecipePaths.Child(parentPath, parent.Children.Count - 1);
        }

        /// <summary>Removes a step (or a region override). The root cannot be removed.</summary>
        public bool RemoveStep(Recipe recipe, string path)
        {
            var loc = RecipePaths.Locate(recipe, path);
            if (loc?.Parent == null) return false;
            if (loc.OverrideKey != null) return loc.Parent.RegionOverrides.Remove(loc.OverrideKey);
            loc.Parent.Children.RemoveAt(loc.Index);
            return true;
        }

        /// <summary>Moves a step among its siblings. Returns its new path, or null when nothing moved.</summary>
        public string MoveStep(Recipe recipe, string path, int delta)
        {
            var loc = RecipePaths.Locate(recipe, path);
            if (loc?.Parent == null || loc.OverrideKey != null || delta == 0) return null;
            var list = loc.Parent.Children;
            int to = math.clamp(loc.Index + delta, 0, list.Count - 1);
            if (to == loc.Index) return null;
            var s = list[loc.Index];
            list.RemoveAt(loc.Index);
            list.Insert(to, s);
            return RecipePaths.Child(RecipePaths.ParentOf(path), to);
        }

        // ───────────── features ─────────────

        /// <summary>
        /// Turns a built-in feature of the fill step at <paramref name="path"/> on (adds the child step:
        /// feature layer defaults + saved defaults, FeatureInfo.Preset on top, Target from the
        /// FeatureInfo) or off (removes that child). A disabled feature child is re-enabled instead
        /// of duplicated. Returns false when nothing changed or the feature is unknown.
        /// </summary>
        public bool SetFeature(Recipe recipe, string path, string featureLayer, bool enabled, uint seed)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null) return false;
            var f = Features.Find(Generator(step.Layer), featureLayer);
            if (f == null) return false;
            int idx = Features.ChildIndex(step, f);
            if (!enabled)
            {
                if (idx < 0) return false;
                step.Children.RemoveAt(idx);
                return true;
            }
            if (idx >= 0)
            {
                if (step.Children[idx].Enabled) return false;
                step.Children[idx].Enabled = true;
                return true;
            }
            if (Depth(path) >= Composer.MaxDepth) return false;
            step.Children.Add(FeatureStep(f, seed));
            return true;
        }

        public RecipeStep FeatureStep(FeatureInfo f, uint seed, SettingsBag presetValues = null)
        {
            var s = NewStep(f.Layer, f.Target ?? "", seed);
            var gen = Generator(f.Layer);
            foreach (var bag in new[] { f.Preset, presetValues })
                if (bag != null)
                    foreach (var kv in bag.Values)
                    {
                        var schema = FindControl(gen, kv.Key);
                        if (gen != null && schema == null) continue;
                        var v = schema != null ? Coerce(schema, kv.Value) : kv.Value;
                        if (v != null) s.Settings.Values[kv.Key] = v is string[] a ? (string[])a.Clone() : v;
                    }
            return s;
        }

        /// <summary>The feature values preset <paramref name="p"/> gives feature layer <paramref name="layer"/> (null when none).</summary>
        public static SettingsBag PresetFeatureValues(PresetInfo p, string layer) =>
            p?.FeatureValues != null && layer != null && p.FeatureValues.TryGetValue(layer, out var bag) ? bag : null;

        /// <summary>
        /// (rf-parks) A built-in preset's feature values (PresetInfo.FeatureValues) on the feature children that are on: every key any
        /// preset of the type sets for that feature gets this preset's value, or the feature's own default when this preset sets none,
        /// so switching from the picnic park to another style puts the picnic groups back.
        /// </summary>
        private bool ApplyFeatureValues(RecipeStep step, IAreaGenerator gen, PresetInfo preset)
        {
            bool changed = false;
            IReadOnlyList<PresetInfo> all;
            try { all = gen.Presets; } catch (Exception) { return false; }
            foreach (var f in Features.Of(gen))
            {
                if (f == null) continue;
                int idx = Features.ChildIndex(step, f);
                if (idx < 0 || !step.Children[idx].Enabled) continue;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in all) { var b = PresetFeatureValues(p, f.Layer); if (b != null) foreach (var k in b.Values.Keys) keys.Add(k); }
                if (keys.Count == 0) continue;
                var expected = FeatureStep(f, 0u, PresetFeatureValues(preset, f.Layer));
                var child = step.Children[idx];
                foreach (var k in keys)
                {
                    if (!expected.Settings.Values.TryGetValue(k, out var v)) continue;
                    if (child.Settings.Values.TryGetValue(k, out var old) && ValueEquals(old, v)) continue;
                    child.Settings.Values[k] = v is string[] a ? (string[])a.Clone() : v;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Adds every DefaultOn feature (of every fill step) that has no child yet — for a recipe just
        /// picked from the Make row. Returns true when anything was added.
        /// </summary>
        public bool AddDefaultFeatures(Recipe recipe, Func<uint> seed)
        {
            if (recipe?.Root == null) return false;
            bool changed = false;
            foreach (var kv in new List<KeyValuePair<string, RecipeStep>>(RecipePaths.Walk(recipe)))
            {
                var step = kv.Value;
                foreach (var f in Features.Of(Generator(step.Layer)))
                {
                    if (f == null || !f.DefaultOn || Features.ChildIndex(step, f) >= 0) continue;
                    if (Depth(kv.Key) >= Composer.MaxDepth) continue;
                    step.Children.Add(FeatureStep(f, seed != null ? seed() : 0u));
                    changed = true;
                }
            }
            return changed;
        }

        public bool SetEnabled(Recipe recipe, string path, bool enabled)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null || step.Enabled == enabled) return false;
            if (step == recipe.Root && !enabled) return false;   // a recipe with a disabled root builds nothing
            step.Enabled = enabled;
            return true;
        }

        public bool SetTarget(Recipe recipe, string path, string target)
        {
            var loc = RecipePaths.Locate(recipe, path);
            if (loc?.Parent == null || loc.OverrideKey != null) return false;
            target = target ?? "";
            if (loc.Step.Target == target) return false;
            loc.Step.Target = target;
            return true;
        }

        /// <summary>Sets ("" removes) the override for one region of the step at parentPath.</summary>
        public bool OverrideRegion(Recipe recipe, string parentPath, string regionKey, string layerId, uint seed)
        {
            var parent = RecipePaths.Resolve(recipe, parentPath);
            if (parent == null || string.IsNullOrEmpty(regionKey)) return false;
            if (string.IsNullOrEmpty(layerId)) return parent.RegionOverrides.Remove(regionKey);
            if (parent.RegionOverrides.TryGetValue(regionKey, out var existing) && existing != null && existing.Layer == layerId) return false;
            parent.RegionOverrides[regionKey] = NewStep(layerId, "", seed);
            return true;
        }

        // ───────────── values ─────────────

        /// <summary>
        /// Sets one control value. Values are coerced to the control's type and clamped to its range;
        /// an invalid value (unknown option, wrong type) is rejected. When <paramref name="lockIt"/> the
        /// id is added to Locked so Randomize keeps the player's explicit choice.
        /// </summary>
        public bool SetValue(Recipe recipe, string path, string controlId, object value, bool lockIt)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null || string.IsNullOrEmpty(controlId)) return false;
            var gen = Generator(step.Layer);
            var schema = FindControl(gen, controlId);
            if (gen != null && schema == null) return false;
            object v = schema != null ? Coerce(schema, value) : value;
            if (v == null) return false;
            bool changed = !step.Settings.Values.TryGetValue(controlId, out var old) || !ValueEquals(old, v);
            if (changed) step.Settings.Values[controlId] = v;
            bool lockChanged = lockIt && step.Locked.Add(controlId);
            // (s16) Linked controls: a road mix writes the road-type picks; derived values (the mix) follow the picks.
            if (gen is ILinkedControls linked)
            {
                try
                {
                    var before = step.Settings.Clone();
                    var driven = linked.OnValueSet(step.Settings, controlId) ?? Array.Empty<string>();
                    if (lockIt) foreach (var id in driven) lockChanged |= step.Locked.Add(id);
                    linked.Derive(step.Settings);
                    changed |= !before.ContentEquals(step.Settings);
                }
                catch (Exception) { }
            }
            return changed || lockChanged;
        }

        public bool UnlockValue(Recipe recipe, string path, string controlId)
        {
            var step = RecipePaths.Resolve(recipe, path);
            return step != null && controlId != null && step.Locked.Remove(controlId);
        }

        /// <summary>Back to defaults (generator + saved). Markers and children stay.</summary>
        public bool ResetStep(Recipe recipe, string path)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null) return false;
            var defaults = DefaultsFor(step.Layer);
            bool changed = !defaults.ContentEquals(step.Settings) || step.Locked.Count > 0;
            step.Settings = defaults;
            step.Locked.Clear();
            return changed;
        }

        /// <summary>Applies a preset's values over the current ones and locks them (a preset is an explicit choice).</summary>
        public bool ApplyPreset(Recipe recipe, string path, string presetId)
        {
            var step = RecipePaths.Resolve(recipe, path);
            var gen = Generator(step?.Layer);
            if (step == null || gen == null) return false;
            PresetInfo preset = null;
            try
            {
                foreach (var p in gen.Presets) if (p != null && p.Id == presetId) { preset = p; break; }
            }
            catch (Exception) { return false; }
            if (preset == null) return false;
            bool changed = false;
            foreach (var kv in preset.Values.Values)
            {
                var schema = FindControl(gen, kv.Key);
                var v = schema != null ? Coerce(schema, kv.Value) : kv.Value;
                if (v == null) continue;
                if (!step.Settings.Values.TryGetValue(kv.Key, out var old) || !ValueEquals(old, v))
                {
                    step.Settings.Values[kv.Key] = v is string[] a ? (string[])a.Clone() : v;
                    changed = true;
                }
                changed |= step.Locked.Add(kv.Key);
            }
            if (gen is ILinkedControls linked)
            {
                try { linked.Derive(step.Settings); } catch (Exception) { }
            }
            // The preset's own features (PresetInfo.Features): "x" on, "-x" off; other features stay as they are.
            uint fs = recipe.Root?.Seed ?? 0u;
            foreach (var f in preset.Features ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(f)) continue;
                bool on = f[0] != '-';
                changed |= SetFeature(recipe, path, on ? f : f.Substring(1), on, Placemaker.Core.Layers.Common.Hashing.Mix(fs, Seeds.Hash(f)));
            }
            changed |= ApplyFeatureValues(step, gen, preset);
            return changed;
        }

        /// <summary>
        /// Rolls new values and a new layout seed for the step at <paramref name="path"/> and its
        /// subtree ("" = the whole recipe). Locked values are kept exactly, even if a generator's
        /// Randomize ignores the locked list.
        /// </summary>
        public bool Randomize(Recipe recipe, string path, uint seed)
        {
            string rootPath = string.IsNullOrEmpty(path) ? RecipePaths.Root : path;
            var start = RecipePaths.Resolve(recipe, rootPath);
            if (start == null) return false;
            bool changed = false;
            int n = 0;
            foreach (var kv in WalkFrom(start))
            {
                var step = kv;
                uint stepSeed = Seeds.Mix(seed, (uint)(++n));
                if (step.Seed != stepSeed) { step.Seed = stepSeed; changed = true; }
                var gen = Generator(step.Layer);
                if (gen == null) continue;
                SettingsBag rolled;
                try { rolled = gen.Randomize(step.Settings.Clone(), stepSeed, new HashSet<string>(step.Locked, StringComparer.Ordinal)); }
                catch (Exception) { continue; }
                if (rolled == null) continue;
                foreach (var id in step.Locked)
                    if (step.Settings.Values.TryGetValue(id, out var keep)) rolled.Values[id] = keep;
                    else rolled.Values.Remove(id);
                if (!rolled.ContentEquals(step.Settings)) { step.Settings = rolled; changed = true; }
            }
            return changed;
        }

        // ───────────── markers ─────────────

        public bool AddMarker(Recipe recipe, string path, EdgeMarker marker)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null || string.IsNullOrEmpty(marker.Kind)) return false;
            step.Markers.Add(marker);
            return true;
        }

        /// <summary>Removes the marker nearest to <paramref name="at"/> within <paramref name="radius"/> (any kind when kind is null).</summary>
        public bool RemoveMarkerNear(Recipe recipe, string path, float2 at, float radius, string kind = null)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null) return false;
            int best = -1;
            float bestD = radius;
            for (int i = 0; i < step.Markers.Count; i++)
            {
                if (kind != null && step.Markers[i].Kind != kind) continue;
                float d = math.distance(step.Markers[i].Position.xz, at);
                if (d <= bestD) { bestD = d; best = i; }
            }
            if (best < 0) return false;
            step.Markers.RemoveAt(best);
            return true;
        }

        /// <summary>Removes the marker placed last (markers are kept in the order they were placed).</summary>
        public bool RemoveLastMarker(Recipe recipe, string path)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null || step.Markers.Count == 0) return false;
            step.Markers.RemoveAt(step.Markers.Count - 1);
            return true;
        }

        public bool ClearMarkers(Recipe recipe, string path, string kind)
        {
            var step = RecipePaths.Resolve(recipe, path);
            if (step == null) return false;
            int removed = step.Markers.RemoveAll(m => string.IsNullOrEmpty(kind) || m.Kind == kind);
            return removed > 0;
        }

        // ───────────── readiness ─────────────

        public sealed class ReadinessResult
        {
            public string StepPath;
            public string NextStepKey = "";
            public string ReasonKey = "";
            public object[] ReasonArgs = Array.Empty<object>();
            public string MarkerKind;
        }

        /// <summary>
        /// First enabled step whose generator is not ready for this area, or null. Child steps are
        /// checked against the whole area (their real regions only exist after planning) — a cheap
        /// approximation that matches what markers need: they are placed on the outer outline.
        /// </summary>
        public ReadinessResult FirstUnready(Recipe recipe, AreaShape area, SiteContext site)
        {
            var all = AllUnready(recipe, area, site);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>Every enabled step that is not ready, in walk order (first = what the main button asks for).</summary>
        public List<ReadinessResult> AllUnready(Recipe recipe, AreaShape area, SiteContext site)
        {
            var list = new List<ReadinessResult>();
            if (recipe?.Root == null || area == null) return list;
            foreach (var kv in RecipePaths.Walk(recipe))
            {
                var step = kv.Value;
                if (!step.Enabled || !AncestorsEnabled(recipe, kv.Key)) continue;
                var gen = Generator(step.Layer);
                if (gen == null) continue;
                Readiness r;
                try
                {
                    r = gen.CheckReady(new AreaInput
                    {
                        Shape = area,
                        Site = site ?? new SiteContext(),
                        Settings = step.Settings,
                        Markers = Composer.MarkersOn(step.Markers, area.Ring),
                        Seed = step.Seed,
                        RegionTags = new[] { RegionTags.Root },
                    });
                }
                catch (Exception) { continue; }
                if (r == null || r.Ready) continue;
                list.Add(new ReadinessResult
                {
                    StepPath = kv.Key,
                    NextStepKey = r.NextStepKey ?? "",
                    ReasonKey = r.ReasonKey ?? "",
                    ReasonArgs = r.ReasonArgs ?? Array.Empty<object>(),
                    MarkerKind = FirstMarkerKind(gen),
                });
            }
            return list;
        }

        private static bool AncestorsEnabled(Recipe recipe, string path)
        {
            for (var p = RecipePaths.ParentOf(path); p != null; p = RecipePaths.ParentOf(p))
            {
                var s = RecipePaths.Resolve(recipe, p);
                if (s != null && !s.Enabled) return false;
            }
            return true;
        }

        public static string FirstMarkerKind(IAreaGenerator gen)
        {
            if (gen == null) return null;
            try
            {
                foreach (var c in gen.Controls)
                    if (c != null && c.Kind == ControlKind.Markers && c.MarkerKinds != null && c.MarkerKinds.Length > 0)
                        return c.MarkerKinds[0];
            }
            catch (Exception) { }
            return null;
        }

        // ───────────── helpers ─────────────

        public static ControlSchema FindControl(IAreaGenerator gen, string id)
        {
            if (gen == null || id == null) return null;
            try
            {
                foreach (var c in gen.Controls) if (c != null && c.Id == id) return c;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>s14: a saved value of a retired option, mapped to its successor by the layer (LayerBase.Migrate).</summary>
        private static object Migrated(IAreaGenerator gen, string id, object value) =>
            gen is Placemaker.Core.Layers.Common.LayerBase lb ? lb.Migrate(id, value) : value;

        /// <summary>Converts a raw value to the control's storage type; null when it does not fit.</summary>
        public static object Coerce(ControlSchema c, object value)
        {
            switch (c.Kind)
            {
                case ControlKind.Slider:
                {
                    if (!TryNumber(value, out double d) || double.IsNaN(d) || double.IsInfinity(d)) return null;
                    if (c.Max > c.Min) d = Math.Max(c.Min, Math.Min(c.Max, d));
                    return d;
                }
                case ControlKind.Toggle:
                    if (value is bool b) return b;
                    if (TryNumber(value, out double nb)) return nb != 0;
                    return null;
                case ControlKind.Choice:
                {
                    string s = value as string ?? (value is IConvertible cv && !(value is bool) ? Convert.ToString(cv, CultureInfo.InvariantCulture) : null);
                    if (s == null) return null;
                    if (c.Options != null && c.Options.Count > 0)
                    {
                        foreach (var o in c.Options) if (o != null && o.Value == s) return s;
                        return null;
                    }
                    return s;
                }
                case ControlKind.Asset:
                    return value as string ?? (value == null ? null : (value is string[] one && one.Length > 0 ? one[0] : null));
                case ControlKind.AssetMulti:
                case ControlKind.Arrangement:
                    if (value is string[] arr) return (string[])arr.Clone();
                    if (value is string single) return single.Length == 0 ? Array.Empty<string>() : new[] { single };
                    if (value is IEnumerable<string> seq) return new List<string>(seq).ToArray();
                    return null;
                case ControlKind.Markers:
                default:
                    return value;
            }
        }

        public static bool TryNumber(object value, out double d)
        {
            switch (value)
            {
                case double x: d = x; return true;
                case float x: d = x; return true;
                case int x: d = x; return true;
                case long x: d = x; return true;
                case uint x: d = x; return true;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                default: d = 0; return false;
            }
        }

        public static bool ValueEquals(object a, object b)
        {
            if (a is string[] x && b is string[] y)
            {
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                return true;
            }
            if (TryNumber(a, out double da) && TryNumber(b, out double db) && !(a is string) && !(b is string)) return da == db;
            return Equals(a, b);
        }

        /// <summary>JSON value from the UI → settings value (double, bool, string, string[]).</summary>
        public static object FromJson(JsonValue v)
        {
            if (v == null) return null;
            switch (v.Kind)
            {
                case JsonKind.Bool: return v.AsBool();
                case JsonKind.Number: return v.AsNumber();
                case JsonKind.String: return v.AsString();
                case JsonKind.Array:
                {
                    var list = new List<string>(v.Count);
                    foreach (var item in v.Items)
                    {
                        if (item.Kind == JsonKind.String) list.Add(item.AsString());
                        else if (item.Kind == JsonKind.Number) list.Add(item.AsNumber().ToString("R", CultureInfo.InvariantCulture));
                    }
                    return list.ToArray();
                }
                default: return null;
            }
        }

        public static JsonValue ToJson(object value)
        {
            switch (value)
            {
                case null: return JsonValue.Null;
                case bool b: return JsonValue.Bool(b);
                case string s: return JsonValue.Str(s);
                case string[] arr:
                {
                    var a = JsonValue.Arr();
                    foreach (var s in arr) a.Add(s ?? "");
                    return a;
                }
                default:
                    return TryNumber(value, out double d) ? JsonValue.Num(d) : JsonValue.Str(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static int Depth(string path)
        {
            int d = 0;
            foreach (char c in path) if (c == '/') d++;
            return d;
        }

        private static IEnumerable<RecipeStep> WalkFrom(RecipeStep start)
        {
            var stack = new WorkStack<RecipeStep>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var s = stack.Pop();
                yield return s;
                var keys = new List<string>(s.RegionOverrides.Keys);
                keys.Sort(StringComparer.Ordinal);
                for (int k = keys.Count - 1; k >= 0; k--) if (s.RegionOverrides[keys[k]] != null) stack.Push(s.RegionOverrides[keys[k]]);
                for (int i = s.Children.Count - 1; i >= 0; i--) if (s.Children[i] != null) stack.Push(s.Children[i]);
            }
        }
    }
}
