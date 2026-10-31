// Placemaker — presets: Category ▸ Type ▸ Preset (contract.ts PresetView / ActivePreset).
//
// A type (layer) ships built-in presets (IAreaGenerator.Presets: a few setting values over the
// defaults). Every Fill type also has STANDARD (StandardId, first in its list): the type's own design, what
// picking the type gives (its recipe's values over the defaults, its DefaultOn features, nothing locked).
// It was the "Default" chip (no preset, user 2026-10-20: "there should be no default style, just lists of
// styles"); now it is a built-in like the others. A type that ships a preset with that id (parking) uses it.
// A design that names no style (older saves, a built group being edited, a deleted style) reads as Standard.
// The player can
//   * save over a built-in        → an OVERRIDE entry (same id; "Reset to default" removes it),
//   * "Save as…" a new preset     → a CUSTOM entry (★, own id, literal name, BasedOn for display),
//   * rename / delete customs.
// Overrides and customs store the full step: settings, Locked and the feature children (playground,
// zoning…) with their settings. Markers, seeds and free-form children never belong to a preset.
//
// Pure: the game persists Write()/Read() to ModsData/Placemaker/presets.json (one entry per preset,
// human-readable so the lead can bake player presets into the built-in defaults).

using System;
using System.Collections.Generic;
using System.Globalization;
using Placemaker.Core.Layers.Common;

namespace Placemaker.Core.Tool
{
    /// <summary>A preset the player saved: a custom one or an override of a built-in.</summary>
    public sealed class UserPreset
    {
        public string Id = "";
        /// <summary>Layer (type) id, canonical (LayerAliases).</summary>
        public string Layer = "";
        /// <summary>Literal name (customs). Overrides keep the built-in's id here for readability.</summary>
        public string Name = "";
        /// <summary>Built-in preset this was derived from ("" = none). Display only.</summary>
        public string BasedOn = "";
        /// <summary>True: replaces the built-in preset with the same Id.</summary>
        public bool IsOverride;
        /// <summary>Settings + Locked + feature children (with their settings). No markers, seeds 0.</summary>
        public RecipeStep Step = new RecipeStep();
        public DateTime Created, Modified;

        public UserPreset Clone()
        {
            var c = (UserPreset)MemberwiseClone();
            c.Step = Step?.Clone() ?? new RecipeStep();
            return c;
        }
    }

    /// <summary>One entry of a type's preset list (mirrors contract.ts PresetView).</summary>
    public sealed class PresetView
    {
        public string Id = "";
        /// <summary>Localisation key for built-ins; literal text when custom.</summary>
        public string Name = "";
        public string Icon = "";
        public bool Custom;
        public bool Overridden;
        public string BasedOn = "";
    }

    /// <summary>The preset applied to the root fill step (mirrors contract.ts ActivePreset).</summary>
    public sealed class ActivePresetInfo
    {
        public string Id = "";
        public string Layer = "";
        public bool Modified;
        public bool CanSave;
    }

    public sealed class PresetStore
    {
        public const int CurrentFormatVersion = 1;
        public const int MaxNameLength = 60;

        /// <summary>The style every Fill type has and starts on: its own design ("Standard").</summary>
        public const string StandardId = "standard";
        /// <summary>Name key of a Standard the type does not ship itself (Lang/en-US.ui.json).</summary>
        public const string StandardNameKey = "Placemaker.UI.Preset.Standard";

        private readonly DesignEditor _editor;
        private readonly Func<DateTime> _clock;
        private readonly Func<string, RecipeStep> _typeBase;
        private readonly List<UserPreset> _presets = new List<UserPreset>();

        /// <summary>Bumped on every change (catalog binding refresh).</summary>
        public int Version { get; private set; }

        /// <summary>formatVersion of the last Load (may be newer than CurrentFormatVersion).</summary>
        public int LoadedFormatVersion { get; private set; } = CurrentFormatVersion;

        /// <param name="typeBase">Layer → the root step of the type's own recipe (its values over the defaults, as picking the
        /// type gives them), or null: Standard is that step; without it Standard is the layer's defaults.</param>
        public PresetStore(DesignEditor editor, Func<DateTime> clock = null, Func<string, RecipeStep> typeBase = null)
        {
            _editor = editor ?? new DesignEditor(null);
            _clock = clock ?? (() => DateTime.UtcNow);
            _typeBase = typeBase;
        }

        public IReadOnlyList<UserPreset> All => _presets;

        // ───────────── lookup ─────────────

        public UserPreset Find(string layer, string id)
        {
            if (string.IsNullOrEmpty(layer) || string.IsNullOrEmpty(id)) return null;
            layer = LayerAliases.Canonical(layer);
            foreach (var p in _presets) if (p.Layer == layer && p.Id == id) return p;
            return null;
        }

        /// <summary>A built-in preset of the layer: one it ships, or its Standard (a type that ships none gets one, see <see cref="IsTypeStandard"/>).</summary>
        public PresetInfo BuiltIn(string layer, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try
            {
                var gen = _editor.Generator(LayerAliases.Canonical(layer));
                var shipped = Shipped(gen, id);
                if (shipped != null) return shipped;
                if (IsTypeStandard(gen, id)) return new PresetInfo { Id = StandardId, LabelKey = StandardNameKey };
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// True for the Standard of a Fill type that ships no preset with that id: the type's own design (what picking the
        /// type gives), resolved from the type's recipe instead of preset values.
        /// </summary>
        public bool IsTypeStandard(string layer, string id)
        {
            try { return IsTypeStandard(_editor.Generator(LayerAliases.Canonical(layer)), id); }
            catch (Exception) { return false; }
        }

        private static bool IsTypeStandard(IAreaGenerator gen, string id) =>
            id == StandardId && gen != null && gen.Kind == LayerKind.Fill && Shipped(gen, id) == null;

        private static PresetInfo Shipped(IAreaGenerator gen, string id)
        {
            if (gen == null || string.IsNullOrEmpty(id)) return null;
            IReadOnlyList<PresetInfo> presets;
            try { presets = gen.Presets; } catch (Exception) { return null; }
            if (presets != null)
                foreach (var p in presets) if (p != null && p.Id == id) return p;
            return null;
        }

        /// <summary>A preset with this id exists for the layer (built-in, override or custom).</summary>
        public bool Exists(string layer, string id) => Find(layer, id) != null || BuiltIn(layer, id) != null;

        public bool IsCustom(string layer, string id)
        {
            var u = Find(layer, id);
            return u != null && (!u.IsOverride || BuiltIn(layer, id) == null);
        }

        /// <summary>Standard first, then the other built-ins in their order (overrides flagged), then customs (★) by creation time.</summary>
        public List<PresetView> ViewsFor(IAreaGenerator gen)
        {
            var list = new List<PresetView>();
            if (gen == null) return list;
            var builtIns = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<PresetInfo> presets = null;
            try { presets = gen.Presets; } catch (Exception) { presets = null; }
            if (IsTypeStandard(gen, StandardId))
            {
                builtIns.Add(StandardId);
                var u = Find(gen.Id, StandardId);
                list.Add(new PresetView { Id = StandardId, Name = StandardNameKey, Overridden = u != null && u.IsOverride });
            }
            if (presets != null)
                foreach (var p in presets)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id) || !builtIns.Add(p.Id)) continue;
                    var u = Find(gen.Id, p.Id);
                    var view = new PresetView { Id = p.Id, Name = p.LabelKey ?? "", Icon = p.Icon ?? "", Overridden = u != null && u.IsOverride };
                    if (p.Id == StandardId) list.Insert(0, view);   // a Standard the type ships comes first too
                    else list.Add(view);
                }
            var customs = new List<UserPreset>();
            foreach (var u in _presets)
                if (u.Layer == gen.Id && !builtIns.Contains(u.Id)) customs.Add(u);
            customs.Sort((a, b) =>
            {
                int c = a.Created.CompareTo(b.Created);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            foreach (var u in customs)
            {
                var basedOn = BuiltIn(gen.Id, u.BasedOn);
                list.Add(new PresetView
                {
                    Id = u.Id,
                    Name = string.IsNullOrEmpty(u.Name) ? u.Id : u.Name,
                    Icon = basedOn?.Icon ?? "",
                    Custom = true,
                    BasedOn = u.BasedOn ?? "",
                });
            }
            return list;
        }

        // ───────────── apply / compare ─────────────

        /// <summary>
        /// The step a preset produces for a fresh step of <paramref name="layer"/>: built-in = defaults
        /// (+ saved defaults) + PresetInfo.Values (locked) + the type's DefaultOn features and the preset's own (PresetInfo.Features); override /
        /// custom = defaults + the stored settings, stored Locked and the stored feature children; a type's own Standard
        /// (<see cref="IsTypeStandard"/>) = the type's recipe step as picking the type gives it (its Locked, its DefaultOn features).
        /// Null when the preset does not exist.
        /// </summary>
        public RecipeStep Resolve(string layer, string presetId)
        {
            layer = LayerAliases.Canonical(layer);
            var gen = _editor.Generator(layer);
            var step = new RecipeStep { Layer = layer ?? "" };
            var user = Find(layer, presetId);
            if (user != null)
            {
                step.Settings = Overlay(_editor.DefaultsFor(layer), gen, user.Step.Settings, null);
                foreach (var id in user.Step.Locked) if (step.Settings.Values.ContainsKey(id)) step.Locked.Add(id);
                // (Phase 1) a style saved before the look settings: its look features' children become their values
                Layers.Common.LookSettings.MigrateFrom(gen, user.Step, step.Settings, false);
                foreach (var c in user.Step.Children)
                {
                    if (c == null || !c.Enabled) continue;
                    var f = FeatureFor(gen, c);
                    if (f == null || Features.ChildIndex(step, f) >= 0) continue;
                    var fs = new RecipeStep { Layer = f.Layer, Target = f.Target ?? "" };
                    var fg = _editor.Generator(f.Layer);
                    fs.Settings = Overlay(_editor.DefaultsFor(f.Layer), fg, c.Settings, null);
                    foreach (var id in c.Locked) if (fs.Settings.Values.ContainsKey(id)) fs.Locked.Add(id);
                    step.Children.Add(fs);
                }
                return step;
            }
            if (IsTypeStandard(gen, presetId)) return ResolveTypeStandard(layer, gen, step);
            var builtIn = Shipped(gen, presetId);
            if (builtIn == null) return null;
            var locked = new HashSet<string>(StringComparer.Ordinal);
            step.Settings = Overlay(_editor.DefaultsFor(layer), gen, builtIn.Values, locked);
            step.Locked = locked;
            foreach (var f in Features.Of(gen))
            {
                if (f == null || !builtIn.FeatureOn(f) || Features.ChildIndex(step, f) >= 0) continue;
                var fs = _editor.FeatureStep(f, 0, DesignEditor.PresetFeatureValues(builtIn, f.Layer));
                fs.Markers.Clear();
                step.Children.Add(fs);
            }
            return step;
        }

        /// <summary>
        /// A type's own Standard: what picking the type gives (RecipeStore.Instantiate + DesignEditor.AddDefaultFeatures): the
        /// type recipe's settings and Locked (the layer's defaults without one), its enabled feature children, then every
        /// DefaultOn feature it has no child for.
        /// </summary>
        private RecipeStep ResolveTypeStandard(string layer, IAreaGenerator gen, RecipeStep step)
        {
            RecipeStep b = null;
            try { b = _typeBase?.Invoke(layer); } catch (Exception) { b = null; }
            if (b != null && b.Layer != layer) b = null;
            step.Settings = b?.Settings != null ? b.Settings.Clone() : _editor.DefaultsFor(layer);
            if (b != null) foreach (var id in b.Locked) if (step.Settings.Values.ContainsKey(id)) step.Locked.Add(id);
            if (b != null) Layers.Common.LookSettings.MigrateFrom(gen, b, step.Settings, true);
            if (b != null)
                foreach (var c in b.Children)
                {
                    if (c == null || !c.Enabled || FeatureFor(gen, c) == null) continue;
                    step.Children.Add(new RecipeStep
                    {
                        Layer = c.Layer ?? "",
                        Target = c.Target ?? "",
                        Settings = c.Settings?.Clone() ?? new SettingsBag(),
                        Locked = new HashSet<string>(c.Locked, StringComparer.Ordinal),
                    });
                }
            foreach (var f in Features.Of(gen))
            {
                if (f == null || !f.DefaultOn || Features.ChildIndex(step, f) >= 0) continue;
                if (b != null && Features.ChildIndex(b, f) >= 0) continue;   // the recipe has it (off): picking the type does not add it
                var fs = _editor.FeatureStep(f, 0);
                fs.Markers.Clear();
                step.Children.Add(fs);
            }
            return step;
        }

        /// <summary>
        /// Applies a preset to <paramref name="step"/>: settings, Locked and feature children are replaced
        /// (a feature that stays on keeps its seed and markers); markers, seed, target, free-form children
        /// and region overrides stay. Returns true when the step changed (false also for an unknown preset).
        /// </summary>
        public bool Apply(RecipeStep step, string presetId, Func<uint> seed = null)
        {
            if (step == null) return false;
            var resolved = Resolve(step.Layer, presetId);
            if (resolved == null) return false;
            var gen = _editor.Generator(step.Layer);
            bool changed = !SameSettings(step.Settings, resolved.Settings) || !step.Locked.SetEquals(resolved.Locked);
            step.Settings = resolved.Settings;
            step.Locked = resolved.Locked;

            var old = new List<RecipeStep>();
            var kept = new List<RecipeStep>();
            foreach (var c in step.Children)
            {
                if (c != null && FeatureFor(gen, c) != null) old.Add(c);
                else kept.Add(c);
            }
            var features = new List<RecipeStep>();
            foreach (var r in resolved.Children)
            {
                RecipeStep prev = null;
                foreach (var o in old) if (o.Layer == r.Layer && (o.Target ?? "") == (r.Target ?? "")) { prev = o; break; }
                if (prev != null)
                {
                    old.Remove(prev);
                    if (!prev.Enabled || !SameSettings(prev.Settings, r.Settings)) changed = true;
                    r.Seed = prev.Seed;
                    r.Markers = new List<EdgeMarker>(prev.Markers);
                    r.Touches = new List<HandTouch>(prev.Touches);
                    r.Children = prev.Children;
                    r.RegionOverrides = prev.RegionOverrides;
                }
                else
                {
                    r.Seed = seed != null ? seed() : 0u;
                    changed = true;
                }
                features.Add(r);
            }
            if (old.Count > 0) changed = true;   // features the preset has off
            // Features keep the type's order; free-form children stay after them, as before.
            features.Sort((a, b) => FeatureOrder(gen, a).CompareTo(FeatureOrder(gen, b)));
            step.Children = new List<RecipeStep>(features.Count + kept.Count);
            step.Children.AddRange(features);
            step.Children.AddRange(kept);
            return changed;
        }

        /// <summary>
        /// True when the step's settings, enabled features or feature settings differ from what the
        /// preset would produce. Locked, seeds, markers and free-form children are ignored. An unknown
        /// preset counts as modified.
        /// </summary>
        public bool IsModified(RecipeStep step, string presetId)
        {
            if (step == null) return false;
            var expected = Resolve(step.Layer, presetId);
            if (expected == null) return true;
            return !SameDesign(step, expected, _editor.Generator(step.Layer));
        }

        /// <summary>
        /// The style shown as picked for <paramref name="root"/>. A design that names no style of its own type (an older save,
        /// a built group being edited, a deleted or unknown style) is on the type's Standard (modified when it differs from it).
        /// Id "" only for a root without styles (not a Fill type).
        /// </summary>
        public ActivePresetInfo Active(RecipeStep root, string layer, string presetId)
        {
            var a = new ActivePresetInfo { Layer = root?.Layer ?? "" };
            if (root == null) return a;
            if (string.IsNullOrEmpty(presetId) || LayerAliases.Canonical(layer) != root.Layer || !Exists(root.Layer, presetId)) presetId = StandardId;
            if (!Exists(root.Layer, presetId)) return a;
            a.Id = presetId;
            a.Modified = IsModified(root, presetId);
            a.CanSave = true;
            return a;
        }

        // ───────────── save / rename / delete ─────────────

        /// <summary>
        /// Save over <paramref name="presetId"/>: a custom preset is overwritten; a built-in gets (or
        /// updates) an override. Returns the stored entry, or null when the id is unknown for the layer.
        /// </summary>
        public UserPreset Save(RecipeStep step, string presetId)
        {
            if (step == null || string.IsNullOrEmpty(step.Layer) || string.IsNullOrEmpty(presetId)) return null;
            var now = _clock();
            var existing = Find(step.Layer, presetId);
            var builtIn = BuiltIn(step.Layer, presetId);
            if (existing == null && builtIn == null) return null;
            if (existing == null)
            {
                existing = new UserPreset { Id = presetId, Layer = step.Layer, Name = presetId, BasedOn = presetId, IsOverride = true, Created = now };
                _presets.Add(existing);
            }
            existing.IsOverride = builtIn != null;
            existing.Step = Capture(step);
            existing.Modified = now;
            Version++;
            return existing;
        }

        /// <summary>New custom preset from the step. <paramref name="basedOn"/> = the active preset (a custom's own BasedOn is followed).</summary>
        public UserPreset SaveAs(RecipeStep step, string name, string basedOn = null)
        {
            if (step == null || string.IsNullOrEmpty(step.Layer)) return null;
            var now = _clock();
            name = CleanName(name);
            string origin = "";
            if (!string.IsNullOrEmpty(basedOn))
            {
                if (BuiltIn(step.Layer, basedOn) != null) origin = basedOn;
                else origin = Find(step.Layer, basedOn)?.BasedOn ?? "";
            }
            var p = new UserPreset
            {
                Id = NewId(step.Layer, name),
                Layer = step.Layer,
                Name = name,
                BasedOn = origin,
                Step = Capture(step),
                Created = now,
                Modified = now,
            };
            _presets.Add(p);
            Version++;
            return p;
        }

        /// <summary>Renames a custom preset (built-ins and overrides keep their localised name).</summary>
        public bool Rename(string layer, string id, string name)
        {
            if (!IsCustom(layer, id)) return false;
            var p = Find(layer, id);
            name = CleanName(name);
            if (p.Name == name) return false;
            p.Name = name;
            p.Modified = _clock();
            Version++;
            return true;
        }

        /// <summary>Custom: deleted. Overridden built-in: reset to default. Plain built-in: false.</summary>
        public bool Delete(string layer, string id)
        {
            var p = Find(layer, id);
            if (p == null) return false;
            _presets.Remove(p);
            Version++;
            return true;
        }

        /// <summary>Removes the override of a built-in preset ("Reset to default").</summary>
        public bool Reset(string layer, string id)
        {
            var p = Find(layer, id);
            if (p == null || !p.IsOverride || BuiltIn(layer, id) == null) return false;
            _presets.Remove(p);
            Version++;
            return true;
        }

        public void Clear()
        {
            if (_presets.Count == 0) return;
            _presets.Clear();
            Version++;
        }

        // ───────────── JSON ─────────────

        /// <summary>presets.json: { formatVersion, presets: [ { layer, id, name, basedOn, override, created, modified, step } ] }.</summary>
        public JsonValue Write()
        {
            var arr = JsonValue.Arr();
            var sorted = new List<UserPreset>(_presets);
            sorted.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Layer, b.Layer);
                if (c != 0) return c;
                if (a.IsOverride != b.IsOverride) return a.IsOverride ? -1 : 1;
                c = a.Created.CompareTo(b.Created);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            foreach (var p in sorted)
            {
                var o = JsonValue.Obj()
                    .Set("layer", p.Layer)
                    .Set("id", p.Id)
                    .Set("name", p.Name ?? "");
                if (!string.IsNullOrEmpty(p.BasedOn)) o.Set("basedOn", p.BasedOn);
                o.Set("override", p.IsOverride);
                o.Set("created", Date(p.Created));
                o.Set("modified", Date(p.Modified));
                o.Set("step", RecipeJson.WriteStep(p.Step ?? new RecipeStep { Layer = p.Layer }));
                arr.Add(o);
            }
            return JsonValue.Obj()
                .Set("formatVersion", CurrentFormatVersion)
                .Set("about", "Placemaker player presets. One entry per preset: layer = type id; override = replaces the built-in preset with this id; step = the full saved settings and features.")
                .Set("presets", arr);
        }

        /// <summary>Parses presets.json. Throws FormatException for a document that is not a preset file.</summary>
        public static List<UserPreset> Read(JsonValue doc, out int formatVersion)
        {
            if (doc == null || !doc.IsObject) throw new FormatException("presets.json must be a JSON object");
            formatVersion = (int)doc["formatVersion"].AsNumber(1);
            var list = new List<UserPreset>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entries = doc["presets"];
            if (!entries.IsArray && !entries.IsNull) throw new FormatException("presets.json: 'presets' must be an array");
            foreach (var e in entries.Items)
            {
                if (e == null || !e.IsObject) continue;
                var stepJson = e["step"];
                string layer = e["layer"].AsString("");
                if (layer.Length == 0 && stepJson.IsObject) layer = stepJson["layer"].AsString("");
                string id = e["id"].AsString("");
                if (layer.Length == 0 || id.Length == 0) continue;
                var step = stepJson.IsObject ? RecipeJson.ReadStep(stepJson, 0) : new RecipeStep();
                step.Layer = layer;
                LayerAliases.Normalize(step);   // retired layer / control ids → canonical
                layer = step.Layer;
                Strip(step, 0);
                if (!seen.Add(layer + "\n" + id)) continue;   // first entry wins
                list.Add(new UserPreset
                {
                    Id = id,
                    Layer = layer,
                    Name = e["name"].AsString(id),
                    BasedOn = e["basedOn"].AsString(""),
                    IsOverride = e["override"].AsBool(false),
                    Step = step,
                    Created = ParseDate(e["created"].AsString("")),
                    Modified = ParseDate(e["modified"].AsString("")),
                });
            }
            return list;
        }

        /// <summary>Replaces the store's contents with a parsed file (see Read).</summary>
        public void Load(JsonValue doc)
        {
            var list = Read(doc, out int v);
            LoadedFormatVersion = v;
            _presets.Clear();
            _presets.AddRange(list);
            Version++;
        }

        public void Load(string text) => Load(JsonValue.Parse(text));

        // ───────────── helpers ─────────────

        /// <summary>What a preset stores: settings, Locked and enabled feature children (no markers, seeds, free-form children).</summary>
        public RecipeStep Capture(RecipeStep step)
        {
            var gen = _editor.Generator(step.Layer);
            var c = new RecipeStep
            {
                Layer = step.Layer ?? "",
                Settings = step.Settings?.Clone() ?? new SettingsBag(),
                Locked = new HashSet<string>(step.Locked, StringComparer.Ordinal),
            };
            foreach (var ch in step.Children)
            {
                if (ch == null || !ch.Enabled) continue;
                if (gen != null ? FeatureFor(gen, ch) == null : false) continue;
                c.Children.Add(new RecipeStep
                {
                    Layer = ch.Layer ?? "",
                    Target = ch.Target ?? "",
                    Settings = ch.Settings?.Clone() ?? new SettingsBag(),
                    Locked = new HashSet<string>(ch.Locked, StringComparer.Ordinal),
                });
            }
            return c;
        }

        private static void Strip(RecipeStep s, int depth)
        {
            s.Seed = 0;
            s.Markers.Clear();
            s.Touches.Clear();
            s.RegionOverrides.Clear();
            s.Enabled = true;
            if (depth > 0) { s.Children.Clear(); return; }
            s.Target = "";
            s.Children.RemoveAll(c => c == null || !c.Enabled);
            foreach (var c in s.Children) Strip(c, depth + 1);
        }

        private static FeatureInfo FeatureFor(IAreaGenerator gen, RecipeStep child)
        {
            if (child == null) return null;
            foreach (var f in Features.Of(gen))
                if (f != null && f.Layer == child.Layer && (f.Target ?? "") == (child.Target ?? "")) return f;
            return null;
        }

        private static int FeatureOrder(IAreaGenerator gen, RecipeStep child)
        {
            var list = Features.Of(gen);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Layer == child.Layer && (list[i].Target ?? "") == (child.Target ?? "")) return i;
            return list.Count;
        }

        /// <summary>Base + values coerced to the generator's controls (stale ids dropped when the generator is known).</summary>
        private static SettingsBag Overlay(SettingsBag bag, IAreaGenerator gen, SettingsBag values, HashSet<string> locked)
        {
            if (values == null) return bag;
            foreach (var kv in values.Values)
            {
                var schema = DesignEditor.FindControl(gen, kv.Key);
                if (gen != null && schema == null) continue;
                var v = schema != null ? DesignEditor.Coerce(schema, kv.Value) : kv.Value;
                if (v == null) continue;
                bag.Values[kv.Key] = v is string[] a ? (string[])a.Clone() : v;
                locked?.Add(kv.Key);
            }
            return bag;
        }

        public static bool SameSettings(SettingsBag a, SettingsBag b)
        {
            a = a ?? new SettingsBag();
            b = b ?? new SettingsBag();
            if (a.Values.Count != b.Values.Count) return false;
            foreach (var kv in a.Values)
            {
                if (!b.Values.TryGetValue(kv.Key, out var o)) return false;
                if (!DesignEditor.ValueEquals(kv.Value, o)) return false;
            }
            return true;
        }

        private static bool SameDesign(RecipeStep step, RecipeStep expected, IAreaGenerator gen)
        {
            if (!SameSettings(step.Settings, expected.Settings)) return false;
            var mine = EnabledFeatures(step, gen);
            var theirs = EnabledFeatures(expected, gen);
            if (mine.Count != theirs.Count) return false;
            foreach (var kv in mine)
            {
                if (!theirs.TryGetValue(kv.Key, out var t)) return false;
                if (!SameSettings(kv.Value.Settings, t.Settings)) return false;
            }
            return true;
        }

        private static Dictionary<string, RecipeStep> EnabledFeatures(RecipeStep step, IAreaGenerator gen)
        {
            var d = new Dictionary<string, RecipeStep>(StringComparer.Ordinal);
            foreach (var c in step.Children)
            {
                if (c == null || !c.Enabled || FeatureFor(gen, c) == null) continue;
                var key = c.Layer + "@" + (c.Target ?? "");
                if (!d.ContainsKey(key)) d[key] = c;
            }
            return d;
        }

        public static string CleanName(string name)
        {
            name = (name ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
            if (name.Length == 0) name = "My preset";
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength).TrimEnd();
            return name;
        }

        /// <summary>Readable, unique id: "custom-shady-grove", "custom-shady-grove-2"…</summary>
        private string NewId(string layer, string name)
        {
            var chars = new System.Text.StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') chars.Append(c);
                else if (chars.Length > 0 && chars[chars.Length - 1] != '-') chars.Append('-');
                if (chars.Length >= 32) break;
            }
            string slug = chars.ToString().Trim('-');
            if (slug.Length == 0) slug = "preset";
            string baseId = "custom-" + slug, id = baseId;
            for (int n = 2; Exists(layer, id); n++) id = baseId + "-" + n.ToString(CultureInfo.InvariantCulture);
            return id;
        }

        private static string Date(DateTime t) =>
            t == default ? "" : t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static DateTime ParseDate(string s) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : default;
    }
}
