// Placemaker — add-ons that are really the look of a type, shown as settings of the type (Phase 1, 2026-10-30).
//
// The user: "Why is big trees an add-on? Shouldn't that just be part of the artistic vision of the actual parameters?"
// Big trees, rocky areas, water, shore, flowerbeds, plaza trees, ground shape and ground patches, the kickabout lawn, the
// terrace garden, light towers and the neglect clutter of the rundown and industrial types are no longer add-ons the player
// switches on: each is ONE setting of the type (a choice of levels or a switch) that its styles preset. Real facilities
// (courts, playgrounds, restrooms, kiosks…) stay add-ons.
//
// How it works (docs/ARCHITECTURE.md, "Controls model"):
// - The feature layer still does the work. A look setting names the feature it drives and, per level, what the feature
//   child gets (a preset of the feature layer, values, or "*key" = the host's value times a factor).
// - LayerBase adds the setting to every type that offers the feature (its group: the first of Groups the type has), turns
//   the styles' feature lists into the setting's value, and moves the feature's own options (FeatureInfo.Options) under it.
// - Features.Of no longer lists these features, so the panel shows no add-on for them and the design keeps no child: the
//   planner makes the child at compose time from the setting (Children).
// - Saved designs from before keep their feature child. A step without the setting's value reads it from that child (on =
//   the legacy level, the child kept exactly as built); once the player sets a value, the value decides.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Layers.Common
{
    /// <summary>One level of a look setting: its saved value and what the feature child gets at it.</summary>
    public sealed class LookLevel
    {
        public string Value = "";
        public string LabelKey = "";
        /// <summary>Preset of the feature layer applied at this level (null = none).</summary>
        public string ChildPreset;
        /// <summary>Child values on top ("*key" = the value the child had times this factor).</summary>
        public (string id, object value)[] Child = Array.Empty<(string, object)>();
    }

    /// <summary>A feature shown as one setting of the types that offer it.</summary>
    public sealed class LookSetting
    {
        /// <summary>The feature layer it drives.</summary>
        public string Feature = "";
        /// <summary>The setting's control id on the type.</summary>
        public string ControlId = "";
        public string LabelKey = "";
        public string TooltipKey = "";
        /// <summary>Sections it joins: the first the type has, else the first of the list.</summary>
        public string[] Groups = Array.Empty<string>();
        /// <summary>Section when the type has none of Groups (null = the first of Groups).</summary>
        public string Fallback;
        /// <summary>A switch (off / on) instead of a choice of levels.</summary>
        public bool Toggle;
        /// <summary>Levels; [0] is off ("none"). A switch has exactly two.</summary>
        public LookLevel[] Levels = Array.Empty<LookLevel>();
        /// <summary>The level "on" means for a host (its default when on, and what a saved design's child stands for); null = Levels[1].</summary>
        public Func<FeatureInfo, string> OnValue;

        public string On(FeatureInfo f)
        {
            string v = null;
            try { v = OnValue?.Invoke(f); } catch (Exception) { v = null; }
            return Level(v) != null && v != LookSettings.Off ? v : Levels.Length > 1 ? Levels[1].Value : LookSettings.Off;
        }

        public LookLevel Level(string value)
        {
            if (value == null) return null;
            foreach (var l in Levels) if (l.Value == value) return l;
            return null;
        }
    }

    public static class LookSettings
    {
        public const string Off = "none";
        /// <summary>Section of the ground's shape (rocks, mounds, patches): the "Shape" part of the Ground section.</summary>
        public const string ShapeGroup = "terrain";

        private const string K = "Placemaker.Look.";

        private static LookLevel L(string value, string labelKey, string preset = null, params (string, object)[] child) =>
            new LookLevel { Value = value, LabelKey = labelKey, ChildPreset = preset, Child = child ?? Array.Empty<(string, object)>() };

        private static LookLevel None() => L(Off, K + "Level.none");

        /// <summary>None / few / some / many: the feature's amount times 0.5 / 1 / 2.5.</summary>
        private static LookLevel[] Amounts(params string[] keys)
        {
            (string, object)[] Times(double k) { var a = new (string, object)[keys.Length]; for (int i = 0; i < keys.Length; i++) a[i] = ("*" + keys[i], k); return a; }
            return new[] { None(), L("few", K + "Level.few", null, Times(0.5)), L("some", K + "Level.some", null, Times(1.0)), L("many", K + "Level.many", null, Times(2.5)) };
        }

        private static LookSetting Switch(string feature, string id, params string[] groups) => new LookSetting
        {
            Feature = feature, ControlId = id, LabelKey = K + id, TooltipKey = K + id + ".Tooltip", Groups = groups, Toggle = true,
            Fallback = groups.Length > 0 ? groups[groups.Length - 1] : "",
            Levels = new[] { None(), L("on", "Placemaker.UI.On") },
        };

        private static readonly string[] Planting = { "planting", "trees", "greens", "green" };
        private static readonly string[] Clutter = { "site", "yard", "yardLayout", "operations", "heaps", "lot", "rubble", "debris", "junk", "layout" };

        private static readonly string[] TerrainShapes = { "mounds", "hills", "terraces", "terracedDown", "sunken", "raised", "bowl", "berms", "flatten" };
        private static readonly string[] ShoreStyles = { "auto", "promenade", "boardwalk", "beach", "reeds", "pier" };   // never quays (no wharf)
        private static readonly string[] PatchPresets = { "mixed", "meadowMown", "rockGarden", "sandGravel", "wornLawn" };

        private static LookLevel[] With(string[] values, Func<string, LookLevel> make)
        {
            var list = new List<LookLevel> { None() };
            foreach (var v in values) list.Add(make(v));
            return list.ToArray();
        }

        private static string HostString(FeatureInfo f, string key) => f?.Preset != null ? f.Preset.S(key, null) : null;

        /// <summary>Every look setting (feature layer → setting).</summary>
        public static readonly LookSetting[] All =
        {
            new LookSetting
            {
                Feature = "bigtrees", ControlId = "lookBigTrees", LabelKey = K + "lookBigTrees", TooltipKey = K + "lookBigTrees.Tooltip",
                Groups = Planting, Levels = Amounts("density"),
                OnValue = f => { double d = f?.Preset?.Num("density", 2) ?? 2; return d <= 1.0 ? "few" : d <= 2.5 ? "some" : "many"; },
            },
            new LookSetting
            {
                Feature = "flowerbeds", ControlId = "lookFlowerbeds", LabelKey = K + "lookFlowerbeds", TooltipKey = K + "lookFlowerbeds.Tooltip",
                Groups = Planting, Levels = Amounts("density"), OnValue = f => "some",
            },
            new LookSetting
            {
                Feature = "plazatrees", ControlId = "lookPlazaTrees", LabelKey = K + "lookPlazaTrees", TooltipKey = K + "lookPlazaTrees.Tooltip",
                Groups = Planting,
                Levels = With(new[] { "grid", "islands" }, v => L(v, "Placemaker.AddOn.PlazaTrees.layout." + v, null, ("layout", v))),
                OnValue = f => HostString(f, "layout") ?? "grid",
            },
            new LookSetting
            {
                // "a whole rocky area, not some rocks placed in places": rock zones (clusters of big rocks on a painted patch)
                Feature = "rocks", ControlId = "lookRockyAreas", LabelKey = K + "lookRockyAreas", TooltipKey = K + "lookRockyAreas.Tooltip",
                Groups = new[] { ShapeGroup },
                Levels = new[]
                {
                    None(),
                    L("few", K + "Level.few", null, ("*clusters", 0.5), ("rocks", 8.0), ("patch", true)),
                    L("some", K + "Level.some", null, ("*clusters", 1.0), ("rocks", 10.0), ("patch", true)),
                    L("many", K + "Level.many", null, ("*clusters", 2.5), ("rocks", 12.0), ("patch", true)),
                },
                OnValue = f => "some",
            },
            new LookSetting
            {
                Feature = "terrain", ControlId = "lookTerrainShape", LabelKey = K + "lookTerrainShape", TooltipKey = K + "lookTerrainShape.Tooltip",
                Groups = new[] { ShapeGroup },
                Levels = With(TerrainShapes, v => L(v, "Placemaker.AddOn.Terrain.shape." + v, null, ("shape", v))),
                OnValue = f => HostString(f, "shape") ?? "mounds",
            },
            new LookSetting
            {
                Feature = "ground", ControlId = "lookGroundPatches", LabelKey = K + "lookGroundPatches", TooltipKey = K + "lookGroundPatches.Tooltip",
                Groups = new[] { ShapeGroup },
                Levels = With(PatchPresets, v => L(v, "Placemaker.AddOn.Ground.Preset." + v, v)),
                OnValue = f => "mixed",
            },
            new LookSetting
            {
                // the pond grows into water: none / pond / lake (the park sizes a pond smaller than a lake)
                Feature = "pond", ControlId = "lookWater", LabelKey = K + "lookWater", TooltipKey = K + "lookWater.Tooltip",
                Groups = new[] { "water" },
                Levels = new[] { None(), L("pond", K + "lookWater.pond", "plain"), L("lake", K + "lookWater.lake", "islandLake") },
                OnValue = f => "pond",
            },
            new LookSetting
            {
                Feature = "shore", ControlId = "lookShore", LabelKey = K + "lookShore", TooltipKey = K + "lookShore.Tooltip",
                Groups = new[] { "water" },
                Levels = With(ShoreStyles, v => v == "auto"
                    ? L(v, "Placemaker.AddOn.Shore.style.auto")
                    : L(v, "Placemaker.AddOn.Shore.style." + v, null, ("style", v))),
                OnValue = f => "auto",
            },
            Switch("kickabout", "lookKickabout", "playZones", "play", "layout"),
            Switch("terraceGarden", "lookTerraceGarden", "layout"),
            Switch("lightTowers", "lookLightTowers", "lighting", "furniture", "layout"),
            // Neglect clutter: part of how worn or busy the place is; its styles (Neglected among them) decide. Never a slider.
            Switch("debris", "lookDebris", Clutter),
            Switch("junkPiles", "lookJunkPiles", Clutter),
            Switch("skipRow", "lookSkips", Clutter),
            Switch("spoilHeaps", "lookSpoilHeaps", Clutter),
            Switch("workLights", "lookWorkLights", Clutter),
            Switch("yardBreak", "lookBreakArea", Clutter),
        };

        /// <summary>Features no type offers any more, as an add-on or a setting (the user: "completely remove animal spawns").</summary>
        private static readonly HashSet<string> Retired = new HashSet<string>(StringComparer.Ordinal) { "wildlifeHabitat" };

        private static readonly Dictionary<string, LookSetting> ByFeature = Index();

        private static Dictionary<string, LookSetting> Index()
        {
            var d = new Dictionary<string, LookSetting>(StringComparer.Ordinal);
            foreach (var l in All) d[l.Feature] = l;
            return d;
        }

        public static LookSetting ForFeature(string featureLayer) =>
            featureLayer != null && ByFeature.TryGetValue(featureLayer, out var l) ? l : null;

        /// <summary>The feature is shown as a setting of its host (never as an add-on).</summary>
        public static bool IsLook(string featureLayer) => ForFeature(featureLayer) != null;

        /// <summary>The feature is gone from every type (never offered).</summary>
        public static bool IsRetired(string featureLayer) => featureLayer != null && Retired.Contains(featureLayer);

        /// <summary>
        /// The look settings a type has, with the feature each drives (its raw IHasFeatures list): only those whose control the
        /// type has (LayerBase adds them; <paramref name="building"/>: while it does, before its controls exist).
        /// </summary>
        public static List<(LookSetting look, FeatureInfo feature)> Of(IAreaGenerator gen, bool building = false)
        {
            var list = new List<(LookSetting, FeatureInfo)>();
            if (gen == null || gen.Kind != LayerKind.Fill || !(gen is IHasFeatures hf)) return list;
            IReadOnlyList<FeatureInfo> all;
            try { all = hf.Features; } catch (Exception) { return list; }
            if (all == null) return list;
            foreach (var f in all)
            {
                var l = f == null ? null : ForFeature(f.Layer);
                if (l != null && !list.Exists(x => x.Item1 == l) && (building || HasControl(gen, l.ControlId))) list.Add((l, f));
            }
            return list;
        }

        private static bool HasControl(IAreaGenerator gen, string id)
        {
            try { if (gen.Controls != null) foreach (var c in gen.Controls) if (c != null && c.Id == id) return true; }
            catch (Exception) { }
            return false;
        }

        /// <summary>The type shows this feature as its setting (so not as an add-on).</summary>
        public static bool IsLookOf(IAreaGenerator gen, string featureLayer)
        {
            var l = ForFeature(featureLayer);
            return l != null && HasControl(gen, l.ControlId);
        }

        public static (LookSetting look, FeatureInfo feature) Find(IAreaGenerator gen, string controlId)
        {
            foreach (var x in Of(gen)) if (x.look.ControlId == controlId) return x;
            return (null, null);
        }

        // ───────────── the control ─────────────

        /// <summary>The setting as a control of its host (group: the first of Groups the host has).</summary>
        public static ControlSchema Control(LookSetting l, FeatureInfo f, Func<string, bool> hasGroup)
        {
            string group = l.Fallback ?? (l.Groups.Length > 0 ? l.Groups[0] : "");
            foreach (var g in l.Groups) if (hasGroup != null && hasGroup(g)) { group = g; break; }
            var c = new ControlSchema
            {
                Id = l.ControlId, Tier = ControlTier.Essential, Group = group, LabelKey = l.LabelKey, TooltipKey = l.TooltipKey,
                Default = ToControl(l, f != null && f.DefaultOn ? l.On(f) : Off),
            };
            if (l.Toggle) c.Kind = ControlKind.Toggle;
            else
            {
                c.Kind = ControlKind.Choice;
                foreach (var lv in l.Levels) c.Options.Add(new ControlOption { Value = lv.Value, LabelKey = lv.LabelKey });
            }
            return c;
        }

        /// <summary>The condition "the setting is on" (visibleWhen syntax).</summary>
        public static string OnCondition(LookSetting l) => l.Toggle ? l.ControlId + "=true" : l.ControlId + "!=" + Off;

        /// <summary>A level as the control's value (a switch: bool).</summary>
        public static object ToControl(LookSetting l, string level) => l.Toggle ? (object)(level != null && level != Off) : (level ?? Off);

        /// <summary>The level a stored control value stands for (unknown values: off).</summary>
        public static string FromControl(LookSetting l, FeatureInfo f, object value)
        {
            if (value is bool b) return b ? l.On(f) : Off;
            if (value is string s)
            {
                if (l.Toggle) return s == "true" || s == "on" ? l.On(f) : Off;
                return l.Level(s) != null ? s : Off;
            }
            if (value is double d) return d != 0 ? l.On(f) : Off;
            return Off;
        }

        // ───────────── the level of a step ─────────────

        /// <summary>
        /// The level of <paramref name="l"/> on <paramref name="step"/>: its value, or (a design saved before the setting) the
        /// feature child it still has (on = the legacy level; disabled or missing = off).
        /// </summary>
        public static string LevelOf(LookSetting l, FeatureInfo f, RecipeStep step)
        {
            if (step?.Settings != null && step.Settings.Values.TryGetValue(l.ControlId, out var v)) return FromControl(l, f, v);
            int idx = Tool.Features.ChildIndex(step, f);
            return idx >= 0 && step.Children[idx].Enabled ? l.On(f) : Off;
        }

        /// <summary>The step's setting value as the panel shows it (also for designs saved before the setting).</summary>
        public static object ShownValue(LookSetting l, FeatureInfo f, RecipeStep step) => ToControl(l, LevelOf(l, f, step));

        /// <summary>A child step of a look feature (shown as the setting, never as a step of its own).</summary>
        public static bool IsLookChild(IAreaGenerator parent, RecipeStep child)
        {
            if (child == null) return false;
            foreach (var (l, f) in Of(parent))
                if (f.Layer == child.Layer && (f.Target ?? "") == (child.Target ?? "")) return true;
            return false;
        }

        /// <summary>
        /// Writes the settings a step does not have yet. <paramref name="fresh"/>: a type just picked (its recipe's feature
        /// child, else the setting's default); otherwise a saved design (its feature child, else off: the player turned it off).
        /// Returns true when anything was written.
        /// </summary>
        public static bool Migrate(IAreaGenerator gen, RecipeStep step, bool fresh) => MigrateFrom(gen, step, step?.Settings, fresh);

        /// <summary>
        /// <see cref="Migrate"/> reading <paramref name="source"/> (its settings and feature children) and writing into
        /// <paramref name="into"/> (a style resolved over the type's defaults: the source decides, not those defaults).
        /// </summary>
        public static bool MigrateFrom(IAreaGenerator gen, RecipeStep source, SettingsBag into, bool fresh)
        {
            if (source == null || into == null) return false;
            bool changed = false;
            foreach (var (l, f) in Of(gen))
            {
                if (source.Settings != null && source.Settings.Values.ContainsKey(l.ControlId))
                {
                    if (!ReferenceEquals(into, source.Settings)) into.Values[l.ControlId] = source.Settings.Values[l.ControlId];
                    continue;
                }
                int idx = Tool.Features.ChildIndex(source, f);
                string level = idx >= 0 ? (source.Children[idx].Enabled ? l.On(f) : Off) : fresh && f.DefaultOn ? l.On(f) : Off;
                into.Values[l.ControlId] = ToControl(l, level);
                changed = true;
            }
            return changed;
        }

        // ───────────── compose ─────────────

        /// <summary>
        /// The children the planner runs for <paramref name="step"/>: its own, with each look feature's child made from the
        /// setting (added, tuned to the level, or left out when off). The step itself is never changed. A saved design's
        /// child with no setting value runs exactly as it was built.
        /// </summary>
        public static IReadOnlyList<RecipeStep> Children(IAreaGenerator gen, RecipeStep step)
        {
            if (step == null) return Array.Empty<RecipeStep>();
            var looks = Of(gen);
            if (looks.Count == 0) return step.Children;
            List<RecipeStep> list = null;
            foreach (var (l, f) in looks)
            {
                int idx = Tool.Features.ChildIndex(step, f);
                var existing = idx >= 0 ? step.Children[idx] : null;
                string level = LevelOf(l, f, step);
                bool keyed = step.Settings != null && step.Settings.Values.ContainsKey(l.ControlId);
                if (level == Off)
                {
                    if (existing != null) { list ??= new List<RecipeStep>(step.Children); list.Remove(existing); }
                    continue;
                }
                if (existing != null && existing.Enabled && !keyed) continue;   // a saved design: its child as built
                var lv = l.Level(level);
                RecipeStep child;
                if (existing != null && existing.Enabled)
                {
                    child = existing.Clone();
                    Apply(child, lv, null);
                }
                else
                {
                    child = new RecipeStep { Layer = f.Layer, Target = f.Target ?? "", Seed = Seeds.Mix(step.Seed, Seeds.Hash("look:" + f.Layer)) };
                    if (f.Preset != null) foreach (var kv in f.Preset.Values) child.Settings.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
                    Apply(child, lv, GeneratorRegistry.Get(f.Layer));
                }
                list ??= new List<RecipeStep>(step.Children);
                if (existing != null) list[list.IndexOf(existing)] = child;
                else list.Add(child);
            }
            return list ?? step.Children;
        }

        /// <summary>A level's values on a feature child (its preset first, then the values; "*key" multiplies what the child has).</summary>
        private static void Apply(RecipeStep child, LookLevel lv, IAreaGenerator featureGen)
        {
            if (lv == null) return;
            featureGen ??= GeneratorRegistry.Get(child.Layer);
            if (!string.IsNullOrEmpty(lv.ChildPreset) && featureGen?.Presets != null)
                foreach (var p in featureGen.Presets)
                    if (p != null && p.Id == lv.ChildPreset)
                    {
                        foreach (var kv in p.Values.Values) child.Settings.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
                        break;
                    }
            foreach (var (id, value) in lv.Child)
            {
                if (id.StartsWith("*", StringComparison.Ordinal))
                {
                    string key = id.Substring(1);
                    double k = value is double d ? d : 1.0;
                    double baseValue = child.Settings.Num(key, double.NaN);
                    var schema = Tool.DesignEditor.FindControl(featureGen, key);
                    if (double.IsNaN(baseValue)) baseValue = schema?.Default is double dd ? dd : 1.0;
                    double v = baseValue * k;
                    if (schema != null && schema.Max > schema.Min) v = Math.Max(schema.Min, Math.Min(schema.Max, v));
                    if (schema != null && schema.Step >= 1) v = Math.Max(schema.Min, Math.Round(v));
                    child.Settings.Values[key] = v;
                }
                else child.Settings.Values[id] = value;
            }
        }
    }
}
