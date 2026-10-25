// Game runner: the panel, as a person uses it. Each action clicks the panel's real element (data-testid) when
// it is on the screen, else fires the panel's own trigger through the UI view (engine.trigger: the binding path with
// its argument conversions, in the UI update), and only as a last resort calls the tool directly — the path taken is
// recorded per action ("ui": click / trigger / direct). Before an action the panel's state is read the way a person
// sees it (the button's disabled state and reason, the tile in the Make row, the control's availability); after it the
// effect is checked (the step fails with the reason when nothing changed).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        // ───────────── the UI bridge ─────────────

        private IEnumerator UiRun(string body, Action<JObject> done, int frames = 40)
        {
            var ui = SuiteUiSystem.Instance;
            if (ui == null) { done(null); yield break; }
            int id = ui.Run(body);
            if (id < 0) { done(null); yield break; }
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                var r = ui.Take(id);
                if (r != null) { done(r); yield break; }
            }
            done(null);
        }

        private bool _bridgeOk = true;

        /// <summary>
        /// One panel action. testId: the element a person clicks (null = none on screen for this); trigger/args: the panel's
        /// trigger; effect: what must change; direct: the tool call when the UI view is unavailable.
        /// Records {label, path, element state} in rec["ui"] and sets rec error when the element is disabled or nothing happened.
        /// </summary>
        private IEnumerator UiAct(ScenarioState st, JObject rec, string label, string testId, string trigger, object[] args, Func<bool> effect, Action direct, bool mustChange = true, int effectFrames = 20)
        {
            var log = rec["ui"] as JArray ?? (JArray)(rec["ui"] = new JArray());
            var entry = new JObject { ["action"] = label };
            log.Add(entry);
            PlacemakerToolSystem.SuiteCursorAway();   // the mouse is on the panel
            bool done = effect != null && effect();
            if (done && mustChange) { entry["path"] = "none (already so)"; yield break; }
            if (SuiteUiSystem.Available && _bridgeOk)
            {
                if (testId != null)
                {
                    JObject reply = null;
                    yield return UiRun(SuiteUiSystem.ElementJs(testId, click: false), r => reply = r);
                    if (reply == null) { _bridgeOk = SuiteUiSystem.Instance.Reports > 0; entry["bridge"] = "no reply from the UI view"; }
                    var v = reply?["value"] as JObject;
                    if (v != null && ((bool?)v["found"] ?? false))
                    {
                        entry["element"] = testId;
                        entry["shown"] = (string)v["text"];
                        // a person can press a button the panel shows greyed (aria-disabled): its onClick decides what happens
                        if ((bool?)v["disabled"] ?? false) entry["shownDisabled"] = true;
                        JObject clicked = null;
                        yield return UiRun(SuiteUiSystem.ElementJs(testId, click: true), r => clicked = r);
                        entry["click"] = (string)clicked?["value"]?["clicked"];
                        for (int i = 0; i < effectFrames && effect != null && !effect(); i++) yield return null;
                        if (effect == null || effect()) { entry["path"] = "click"; yield break; }
                        entry["clickNoEffect"] = true;
                        if ((bool?)entry["shownDisabled"] == true)
                        {
                            // what a person sees is a greyed button that does nothing: no trigger behind its back
                            entry["path"] = "click (greyed, no effect)";
                            if (mustChange) rec["error"] = $"'{label}' is greyed out in the panel and pressing it does nothing: {(string)v["text"]}";
                            yield break;
                        }
                    }
                    else if (v != null) entry["element"] = testId + " (not on screen)";
                }
                if (trigger != null && SuiteUiSystem.Available)
                {
                    JObject reply = null;
                    yield return UiRun(SuiteUiSystem.TriggerJs(trigger, args ?? Array.Empty<object>()), r => reply = r);
                    if (reply != null && ((bool?)reply["ok"] ?? false))
                    {
                        for (int i = 0; i < effectFrames && effect != null && !effect(); i++) yield return null;
                        if (effect == null || effect()) { entry["path"] = "trigger"; yield break; }
                        entry["triggerNoEffect"] = true;
                    }
                    else entry["trigger"] = reply == null ? "no reply" : (string)reply["error"];
                }
            }
            if (direct != null)
            {
                direct();
                for (int i = 0; i < effectFrames && effect != null && !effect(); i++) yield return null;
                entry["path"] = SuiteUiSystem.Available && _bridgeOk ? "direct (the panel path had no effect)" : "direct (UI view unavailable)";
            }
            if (effect != null && !effect() && mustChange) rec["error"] = $"'{label}' changed nothing" + ((bool?)entry["shownDisabled"] == true ? $" (the panel shows it disabled: {(string)entry["shown"]})" : "") + (_tool.FlashText != null ? ": " + _tool.FlashText.Key : "");
        }

        /// <summary>The main button: what the panel shows (enabled, reason) is checked, then it is clicked.</summary>
        private IEnumerator UiPrimary(ScenarioState st, JObject rec, string label)
        {
            int v0 = _tool.ViewVersion;
            var stage0 = _tool.Stage;
            bool building0 = _tool.Building;
            string marker0 = _tool.MarkerKind;
            int built0 = _tool.SuiteBuildsDone;
            // a build done at once that keeps the selection (a Zoning look) counts by the builds done
            yield return UiAct(st, rec, label, "primary", "primary", null,
                () => _tool.Stage != stage0 || _tool.Building != building0 || _tool.MarkerKind != marker0 || _tool.SuiteBuildsDone != built0,
                () => _tool.Primary());
        }

        private IEnumerator UiGoToStage(JObject rec, ToolStage target)
        {
            string id = StageMachine.Id(target);
            yield return UiAct(null, rec, "stage " + id, "stagebar-" + id, "goToStage", new object[] { id }, () => _tool.Stage == target, () => _tool.GoToStage(target));
        }

        private IEnumerator UiSetAreaMode(JObject rec, SelectionMode mode)
        {
            string id = SelectionModes.Id(mode);
            yield return UiAct(null, rec, "area mode " + id, null, "setAreaMode", new object[] { id }, () => _tool.Mode == mode, () => _tool.SetAreaMode(mode));
            yield return Frames(2);
        }

        /// <summary>What the panel is bound to, parsed (records an error when a binding does not parse).</summary>
        private JToken Bound(JObject rec, string binding)
        {
            var ui = SuiteUiSystem.Instance;
            if (ui == null) return null;
            var t = ui.Bound(binding, out var err);
            if (err != null && rec != null)
            {
                var bad = rec["bindingErrors"] as JArray ?? (JArray)(rec["bindingErrors"] = new JArray());
                bad.Add(binding + ": " + err);
            }
            return t;
        }

        // ───────────── type, preset, value ─────────────

        private string ActivePresetId() => _tool.ActivePreset?.Id ?? "";
        private string RootLayer() => _tool.CurrentRecipe?.Root?.Layer ?? "";

        private IEnumerator StepType(ScenarioState st, JObject step, JObject rec)
        {
            string layer = LayerAliasesCanonical((string)step["layer"] ?? st.S.Layer);
            string preset = (string)step["preset"] ?? st.S.Preset ?? "";
            bool fresh = (bool?)step["fresh"] ?? true;
            if (string.IsNullOrEmpty(layer)) { rec["error"] = "type: no layer given"; st.Result["status"] = "type-not-selected"; yield break; }
            yield return EnsureDesign(st, rec, "The type browser");
            string recipeId = _tool.SuiteRecipeFor(layer);
            if (recipeId == null) { rec["error"] = $"type '{layer}' has no recipe (valid types: {string.Join(", ", CatalogLayers())})"; st.Result["status"] = "type-not-selected"; yield break; }
            // the tile must be in the Make row the panel is bound to
            var catalog = Bound(rec, "catalog");
            bool tile = catalog?["recipes"] is JArray rs && rs.Any(r => (string)r["id"] == recipeId);
            rec["makeRowTile"] = tile;
            // (core) A type the central shelf hides has no tile on screen: it is picked by id (the panel's trigger), and recorded so.
            if (catalog?["recipes"] is JArray rs2 && rs2.Any(r => (string)r["id"] == recipeId && (bool?)r["shelved"] == true)) rec["shelved"] = "hidden from the picker by the shelf (Recipes/shelf); picked by id";
            if (catalog != null && !tile) { rec["error"] = $"type '{layer}' (recipe {recipeId}) is not offered in the panel's Make row"; st.Result["status"] = "type-not-selected"; yield break; }
            if (fresh && RootLayer() == layer)
            {
                // a fresh design, as after opening the game: another type first, then the seed is reset before the pick
                var other = _tool.Recipes.Entries.FirstOrDefault(e => !e.Custom && e.Recipe?.Root != null && e.Recipe.Root.Layer != layer);
                if (other != null) yield return UiAct(st, rec, "another type (fresh design)", null, "selectRecipe", new object[] { other.Recipe.Id }, () => RootLayer() != layer, () => _tool.SelectRecipe(other.Recipe.Id));
                rec["fresh"] = true;
            }
            PlacemakerToolSystem.SuiteResetSeed(st.S.Seed);
            if (_tool.RecipeId != recipeId || RootLayer() != layer)
                yield return UiAct(st, rec, "type " + layer, "recipe-" + recipeId, "selectRecipe", new object[] { recipeId }, () => RootLayer() == layer, () => _tool.SelectRecipe(recipeId));
            if (RootLayer() != layer)
            {
                rec["error"] = "type not selected" + (_tool.FlashText != null ? ": " + _tool.FlashText.Key : "");
                st.Result["status"] = "type-not-selected";
                yield break;
            }
            if (!string.IsNullOrEmpty(preset))
            {
                yield return PickPreset(st, rec, layer, preset);
                if (rec["error"] != null) { st.Result["status"] = "type-not-selected"; yield break; }
            }
            if (string.IsNullOrEmpty(st.S.Layer)) st.S.Layer = layer;
            rec["recipeRoot"] = RootLayer();
            rec["activePreset"] = ActivePresetId();
            rec["seed"] = st.S.Seed;
            yield return WaitPlan(st, rec);
            if (_tool.Plan == null && _tool.Shape != null && st.Pace != "user") { rec["error"] = "no plan: " + _tool.SuiteUnreadyText(); st.Result["status"] = "no-plan"; }
        }

        private IEnumerable<string> CatalogLayers()
        {
            foreach (var g in GeneratorRegistry.All)
                if (!GeneratorRegistry.IsHidden(g) && !string.IsNullOrEmpty(GeneratorGroups.Of(g))) yield return g.Id;
        }

        private IEnumerator PickPreset(ScenarioState st, JObject rec, string layer, string preset)
        {
            var g = GeneratorRegistry.Get(layer);
            var ids = g != null ? PresetIds(g) : new List<string>();
            if (!_tool.Presets.Exists(layer, preset)) { rec["error"] = $"unknown preset '{preset}' for {layer} (valid: {string.Join(", ", ids)})"; yield break; }
            yield return UiAct(st, rec, "preset " + preset, "preset-" + preset, "selectPreset", new object[] { layer, preset },
                () => RootLayer() == layer && ActivePresetId() == preset, () => _tool.SelectPreset(layer, preset));
            if (ActivePresetId() != preset && rec["error"] == null) rec["error"] = $"preset '{preset}' did not become active (active: '{ActivePresetId()}')";
            rec["activePreset"] = ActivePresetId();
        }

        private IEnumerator StepPreset(ScenarioState st, JObject step, JObject rec)
        {
            string layer = LayerAliasesCanonical((string)step["layer"] ?? (RootLayer().Length > 0 ? RootLayer() : st.S.Layer));
            string preset = (string)step["preset"] ?? "";
            yield return EnsureDesign(st, rec, "The style picker");
            if (rec["error"] != null) yield break;
            int rev0 = _tool.InputRevision;
            yield return PickPreset(st, rec, layer, preset);
            if (rec["error"] != null) yield break;
            yield return WaitPlan(st, rec);
            if (_tool.InputRevision == rev0 && _tool.PlanRevision == rec["planRevision"]?.Value<int>()) rec["note"] = "the preset left the plan input unchanged";
        }

        private IEnumerator StepValue(ScenarioState st, JObject step, JObject rec)
        {
            string path = (string)step["path"] ?? "0", id = (string)step["id"];
            yield return EnsureDesign(st, rec, "Options");
            if (rec["error"] != null) yield break;
            var ctrl = _tool.SuiteControl(path, id);
            if (ctrl == null)
            {
                var stepObj = RecipePaths.Resolve(_tool.CurrentRecipe, path);
                var g = stepObj != null ? GeneratorRegistry.Get(stepObj.Layer) : null;
                rec["error"] = $"unknown option '{id}' at step {path}" + (g != null ? $" (valid: {string.Join(", ", g.Controls.Select(c => c.Id))})" : "");
                yield break;
            }
            // the panel greys out a control that does not apply here, and refuses its value
            var unavailable = _tool.Unavailability();
            if (unavailable != null && unavailable.TryGetValue(path, out var byId) && byId != null && byId.TryGetValue(id, out var reason))
            {
                rec["error"] = $"option '{id}' is greyed out in the panel: {reason?.Key}";
                yield break;
            }
            string json = step["json"] != null ? (string)step["json"] : step["value"]?.ToString(Formatting.None);
            if (json == null) { rec["error"] = "value: give value or json"; yield break; }
            JToken want;
            try { want = JToken.Parse(json); } catch (Exception) { want = new JValue(json); }
            rec["kind"] = ctrl.Kind.ToString();
            if (ctrl.Kind == ControlKind.Slider && want.Type is JTokenType.Integer or JTokenType.Float)
            {
                double v = (double)want, clamped = math.clamp(v, ctrl.Min, ctrl.Max);
                if (ctrl.Step > 0) clamped = ctrl.Min + Math.Round((clamped - ctrl.Min) / ctrl.Step) * ctrl.Step;
                clamped = Math.Round(clamped, 6);
                if (Math.Abs(clamped - v) > 1e-6) rec["clampedTo"] = clamped;
                want = new JValue(clamped);
                // the slider: previewValue every frame while dragging (sketch), then setValue and endPreview on release
                double from = Convert.ToDouble(_tool.SuiteValue(path, id) ?? ctrl.Default ?? ctrl.Min);
                int frames = st.Pace == "human" ? 10 : 3;
                for (int k = 1; k <= frames; k++)
                {
                    double x = from + (clamped - from) * k / frames;
                    yield return UiAct(st, rec, "drag " + id, null, "previewValue", new object[] { path, id, x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) }, null, () => _tool.PreviewValue(path, id, x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)), mustChange: false, effectFrames: 1);
                }
                string final = clamped.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                yield return UiAct(st, rec, "release " + id, null, "setValue", new object[] { path, id, final }, null, () => _tool.SetValue(path, id, final), mustChange: false, effectFrames: 1);
                yield return UiAct(st, rec, "end drag", null, "endPreview", null, null, () => _tool.EndPreview(), mustChange: false, effectFrames: 1);
            }
            else if (ctrl.Kind == ControlKind.Asset || ctrl.Kind == ControlKind.AssetMulti)
            {
                // a picker: hover the asset (the preview follows), then click it
                string j = want.ToString(Formatting.None);
                yield return UiAct(st, rec, "hover " + id, null, "previewValue", new object[] { path, id, j }, null, () => _tool.PreviewValue(path, id, j), mustChange: false, effectFrames: 3);
                yield return UiAct(st, rec, "pick " + id, null, "setValue", new object[] { path, id, j }, null, () => _tool.SetValue(path, id, j), mustChange: false, effectFrames: 2);
            }
            else
            {
                string j = want.ToString(Formatting.None);
                yield return UiAct(st, rec, "set " + id, null, "setValue", new object[] { path, id, j }, null, () => _tool.SetValue(path, id, j), mustChange: false, effectFrames: 2);
            }
            yield return Frames(2);
            // read back: the design holds the value
            var got = _tool.SuiteValue(path, id);
            string gotJson = got == null ? "null" : JToken.FromObject(got).ToString(Formatting.None);
            rec["effective"] = gotJson;
            if (!JToken.DeepEquals(Norm(JToken.Parse(gotJson)), Norm(want))) rec["error"] = $"option '{id}' reads {gotJson} after setting {want.ToString(Formatting.None)}";
            rec["undoLabel"] = _tool.UndoLabel?.Key;
            yield return WaitPlan(st, rec);
        }

        private static JToken Norm(JToken t)
        {
            if (t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)) return new JValue(Math.Round(Convert.ToDouble(v.Value), 4));
            return t;
        }

        // ───────────── review ─────────────

        /// <summary>What the Review panel lists: the summary binding's issues (plan, layers, build notes, clip, ghost-dropped, failure).</summary>
        private JArray Issues(JObject rec = null)
        {
            var summary = Bound(rec, "summary");
            if (summary?["issues"] is JArray shown) return new JArray(shown.Take(60));
            var a = new JArray();
            var plan = _tool.Plan;
            if (plan == null) return a;
            foreach (var i in plan.Issues.Concat(plan.Layers.SelectMany(l => l.Issues)).Take(60))
            {
                var o = new JObject { ["severity"] = i.Severity.ToString(), ["key"] = i.Key };
                if (i.Args != null && i.Args.Length > 0) o["args"] = new JArray(i.Args.Select(x => x?.ToString() ?? ""));
                if (i.At.HasValue) o["at"] = new JArray(Math.Round(i.At.Value.x, 1), Math.Round(i.At.Value.z, 1));
                if (!string.IsNullOrEmpty(i.Cause)) o["cause"] = i.Cause + (string.IsNullOrEmpty(i.CauseValue) ? "" : " = " + i.CauseValue);
                if (i.Dropped) o["dropped"] = true;
                a.Add(o);
            }
            if (_tool.GhostDroppedIssue != null) a.Add(new JObject { ["severity"] = "Warning", ["key"] = _tool.GhostDroppedIssue.Key, ["args"] = new JArray(_tool.GhostDropped) });
            return a;
        }

        private IEnumerator StepPreview(ScenarioState st, JObject step, JObject rec)
        {
            yield return WaitPlan(st, rec);
            var primary = _tool.SuitePrimaryInfo();
            if (_tool.Stage != ToolStage.Review)
            {
                if (primary.Kind == "toReview" && primary.Enabled) yield return UiPrimary(st, rec, "Next");
                if (_tool.Stage != ToolStage.Review)
                {
                    rec["error"] = "cannot preview: " + primary.Kind + (primary.Enabled ? "" : " disabled") + " " + _tool.SuiteUnreadyText() + (rec["error"] != null ? " / " + rec["error"] : "");
                    st.Result["status"] = "blocked";
                    st.Result["blockedAt"] = "design";
                    st.Result["unready"] = _tool.SuiteUnreadyText();
                    yield break;
                }
            }
            yield return WaitFor(rec, "review", () => _tool.Stage == ToolStage.Review, 15f);
            yield return WaitPlan(st, rec, always: true);
            if (PlanBlocked(out string blocked)) { rec["ghostSkipped"] = "the plan cannot be built (" + blocked + "): no ghost to wait for"; rec["previewMode"] = "sketch"; }
            else if (_tool.PreviewModeSelected == PreviewMode.Ghost) yield return WaitGhost(rec);
            else rec["previewMode"] = "sketch";
            rec["issues"] = Issues(rec);
            rec["planCost"] = Math.Round(_tool.PlanCost);
            // (rf-shops3) The plan's facts per layer, for reading a run without the pictures (gates landed, rows, lots, fails).
            if (_tool.Plan != null)
                foreach (var l in _tool.Plan.Layers)
                    if (l.Facts != null && l.Facts.Count > 0)
                        Mod.Log.Info($"Placemaker plan facts ({st.S.Id}): " + string.Join(" ", l.Facts.Select(kv => kv.Key + "=" + Math.Round(kv.Value, 1).ToString(System.Globalization.CultureInfo.InvariantCulture))));
            st.Result["planIssues"] = rec["issues"];
            st.Result["planCost"] = rec["planCost"];
            // what the panel shows: the bound state and summary parse, and the main button as the person sees it
            var state = Bound(rec, "state");
            if (state != null) rec["panelPrimary"] = state["primary"]?.DeepClone();
        }

        private static (float2 Min, float2 Max) Bounds(float2[] ring, float margin)
        {
            float2 lo = ring[0], hi = ring[0];
            foreach (var p in ring) { lo = math.min(lo, p); hi = math.max(hi, p); }
            return (lo - margin, hi + margin);
        }

        private IEnumerator StepPreviewMode(ScenarioState st, JObject step, JObject rec)
        {
            string mode = ((string)step["mode"] ?? "ghost").ToLowerInvariant();
            if (!PreviewModes.TryParse(mode, out var want)) { rec["error"] = "previewMode: sketch or ghost"; yield break; }
            yield return UiAct(st, rec, "preview " + mode, "preview-mode-" + mode, "setPreviewMode", new object[] { mode }, () => _tool.PreviewModeSelected == want, () => _tool.SetPreviewMode(mode));
            rec["previewMode"] = PreviewModes.Id(_tool.PreviewModeSelected);
            if (want == PreviewMode.Ghost && _tool.Stage == ToolStage.Review) yield return WaitGhost(rec);
            else yield return Frames(4);
        }

        private IEnumerator StepEdit(ScenarioState st, JObject step, JObject rec)
        {
            if ((bool?)step["build"] ?? false)
            {
                if (st.Group == 0 || !st.Built) { rec["error"] = "no build to edit"; yield break; }
                // a person clicks the build (Manage), then Edit in its panel
                yield return SelectBuild(st, rec);
                if (rec["error"] != null) yield break;
                int g = st.Group;
                yield return UiAct(st, rec, "Edit", "manage-edit", "editGroup", new object[] { (double)g }, () => _tool.EditingGroup == g && _tool.Stage == ToolStage.Design, () => _tool.EditGroup(g));
                rec["group"] = g;
                if (_tool.EditingGroup != g || _tool.Stage != ToolStage.Design) { rec["error"] = $"edit did not load the build (editing {_tool.EditingGroup}, stage {_tool.Stage}" + (_tool.FlashText != null ? ", " + _tool.FlashText.Key : "") + ")"; yield break; }
                st.Ring = _tool.Shape?.Ring2D() ?? st.Ring;
            }
            else
            {
                yield return UiGoToStage(rec, ToolStage.Design);
                if (_tool.Stage != ToolStage.Design) { rec["error"] = "cannot go back to Design from " + _tool.Stage; yield break; }
            }
            rec["stage"] = _tool.Stage.ToString();
            yield return WaitPlan(st, rec);
        }

        private IEnumerator StepClear(ScenarioState st, JObject step, JObject rec)
        {
            if (!_tool.SuiteHasSelection) { rec["error"] = "nothing to clear"; yield break; }
            if (_tool.Stage != ToolStage.Area)
            {
                var implicitSteps = rec["implicit"] as JArray ?? (JArray)(rec["implicit"] = new JArray());
                implicitSteps.Add("goToStage area (Clear is shown in Area)");
                yield return UiGoToStage(rec, ToolStage.Area);
            }
            yield return UiAct(st, rec, "Clear", "clear-area", "clearArea", null, () => !_tool.SuiteHasSelection, () => _tool.ClearArea());
            yield return Frames(3);
            if (_tool.SuiteHasSelection) rec["error"] = "the selection is still there after Clear";
        }

        private IEnumerator StepUndoRedo(ScenarioState st, JObject step, JObject rec, bool undo)
        {
            string via = ((string)step["via"] ?? "key").ToLowerInvariant();
            bool can = undo ? _tool.CanUndo : _tool.CanRedo;
            rec["label"] = (undo ? _tool.UndoLabel : _tool.RedoLabel)?.Key;
            if (!can) { rec["error"] = undo ? "nothing to undo" : "nothing to redo"; yield break; }
            bool other0 = undo ? _tool.CanRedo : _tool.CanUndo;
            int v0 = _tool.ViewVersion;
            if (via == "panel") yield return UiAct(st, rec, undo ? "Undo" : "Redo", undo ? "undo" : "redo", undo ? "undo" : "redo", null, () => _tool.ViewVersion != v0, () => { if (undo) _tool.Undo(); else _tool.Redo(); });
            else yield return KeyPress(undo ? "undo" : "redo");
            rec["via"] = via == "panel" ? "panel" : "key (Ctrl+" + (undo ? "Z" : "Y") + ")";
            if (undo ? !_tool.CanRedo : !_tool.CanUndo) rec["error"] = (undo ? "undo" : "redo") + " did not move the history";
            yield return WaitPlan(st, rec);
        }

        private IEnumerator StepAnarchy(ScenarioState st, JObject step, JObject rec)
        {
            if (!_tool.AnarchyAvailable) { rec["error"] = "Anarchy mod not loaded: the panel has no anarchy switch (for 'ignore collisions in my builds' use {\"do\":\"setting\",\"name\":\"IgnoreOwnCollisions\",\"value\":true})"; yield break; }
            bool on = (bool?)step["on"] ?? true;
            yield return UiAct(st, rec, "anarchy", "anarchy", "setAnarchy", new object[] { on }, () => _tool.AnarchyEnabled == on, () => _tool.SetAnarchy(on));
            yield return WaitPlan(st, rec);
        }

        /// <summary>Review's "One group" switch beside Build (on: one group, the default; off: built as separate pieces).</summary>
        private IEnumerator StepGroupSwitch(ScenarioState st, JObject step, JObject rec)
        {
            bool on = (bool?)step["on"] ?? true;
            yield return UiAct(st, rec, "one group", "build-group", "setBuildUngrouped", new object[] { !on }, () => _tool.BuildUngrouped == !on, () => _tool.SetBuildUngrouped(!on));
            rec["separate"] = _tool.BuildUngrouped;
        }

        /// <summary>A Placemaker option (the Options page), set through the settings object and read back.</summary>
        /// <summary>
        /// The game's Options screen on Placemaker's page (what a person opens from the pause menu), scrolled so a label
        /// (scrollTo: its exact text, e.g. a group's name) sits near the top; close: back to the game. Shots with shotMode
        /// "ui" show it.
        /// </summary>
        private IEnumerator StepOptions(ScenarioState st, JObject step, JObject rec)
        {
            if ((bool?)step["close"] ?? false)
            {
                var screens = _world.GetExistingSystemManaged<global::Game.UI.InGame.GameScreenUISystem>();
                if (screens != null) screens.activeScreen = global::Game.UI.InGame.GameScreenUISystem.GameScreen.Main;
                yield return Seconds(0.5f);
                yield break;
            }
            var options = _world.GetExistingSystemManaged<global::Game.UI.Menu.OptionsUISystem>();
            var s = ToolRegistration.Settings;
            if (options == null || s == null) { rec["error"] = "options: no options screen"; yield break; }
            string err = null;
            try { options.OpenPage(s.id, global::Placemaker.Game.Settings.PlacemakerSettings.SectionMain, false); }
            catch (Exception e) { err = e.Message; }
            if (err != null) { rec["error"] = "options: " + err; yield break; }
            yield return Seconds(1.5f);
            string to = (string)step["scrollTo"];
            if (to != null)
            {
                int offset = (int?)step["offset"] ?? 8;
                string js = "var want=" + SuiteUiSystem.Q(to) + ";var all=document.querySelectorAll('*');var el=null;"
                    + "for(var i=0;i<all.length;i++){var n=all[i];if(String(n.textContent||'').trim()===want){el=n;}}"
                    + "if(!el)return {found:false};"
                    + "var p=el.parentElement;while(p&&!(p.scrollHeight>p.clientHeight+4))p=p.parentElement;"
                    + "if(!p){try{el.scrollIntoView();}catch(e){}return {found:true,scroller:false};}"
                    + "var er=el.getBoundingClientRect(),pr=p.getBoundingClientRect();"
                    + "p.scrollTop=p.scrollTop+(er.top-pr.top)-" + offset + ";"
                    + "return {found:true,scroller:true,top:p.scrollTop,height:p.scrollHeight,view:p.clientHeight};";
                JObject reply = null;
                yield return UiRun(js, r => reply = r);
                rec["scroll"] = reply?["value"] ?? (JToken)(reply?["error"] ?? "no reply");
                if (reply == null || !((bool?)reply["value"]?["found"] ?? false)) rec["error"] = "options: no label '" + to + "' on the page";
                yield return Seconds(0.6f);
            }
            rec["polished"] = global::Placemaker.Game.Neighbours.NeighbourPolishSystem.StatusPolished + " of " + global::Placemaker.Game.Neighbours.NeighbourPolishSystem.StatusTotal;
        }

        private IEnumerator StepSetting(ScenarioState st, JObject step, JObject rec)
        {
            string name = (string)step["name"];
            if (name == "PolishNewHouses" || name == "NeighbourhoodPolish")
            {
                // Outgrow's switches are the city's own now (NeighbourPolishSystem.Switches), no longer settings
                bool on = (bool?)step["value"] ?? true;
                if (name == "PolishNewHouses") global::Placemaker.Game.Neighbours.NeighbourPolishSystem.SetNewBuildings(on);
                else global::Placemaker.Game.Neighbours.NeighbourPolishSystem.SetRunning(on);
                rec["effective"] = on;
                rec["city"] = true;
                yield return Frames(2);
                yield break;
            }
            var s = ToolRegistration.Settings;
            var prop = s?.GetType().GetProperty(name ?? "");
            if (s == null || prop == null || !prop.CanWrite) { rec["error"] = $"unknown setting '{name}'"; yield break; }
            if (!prop.CanRead)
            {
                // a button (write-only): pressed, as after its confirmation
                try { prop.SetValue(s, true); rec["pressed"] = name; Mod.Log.Info("Placemaker suite: setting button " + name + " pressed"); }
                catch (Exception e) { rec["error"] = $"setting {name}: {e.Message}"; }
                yield return Frames(4);
                yield break;
            }
            try
            {
                var v = step["value"].ToObject(prop.PropertyType);
                prop.SetValue(s, v);
                s.ApplyAndSave();
                var got = prop.GetValue(s);
                rec["effective"] = JToken.FromObject(got);
                if (!Equals(got, v)) rec["error"] = $"setting {name} reads {got} after setting {v}";
            }
            catch (Exception e) { rec["error"] = $"setting {name}: {e.Message}"; }
            yield return Frames(2);
            yield return WaitPlan(st, rec);
        }

        /// <summary>The game's language before the first locale step: a locale step without one goes back to it.</summary>
        private static string s_LocaleBefore;

        /// <summary>
        /// The game's language for the rest of the run (`locale`: "de-DE"), the way the Options menu switches it but not saved
        /// to the player's settings; without `locale`, back to the language the game had before. The panel re-reads its texts.
        /// </summary>
        private IEnumerator StepLocale(JObject step, JObject rec)
        {
            var lm = global::Game.SceneFlow.GameManager.instance?.localizationManager;
            if (lm == null) { rec["error"] = "no localization manager"; yield break; }
            string want = (string)step["locale"];
            if (string.IsNullOrEmpty(want))
            {
                want = s_LocaleBefore;
                if (want == null) { rec["note"] = "no locale step before: nothing to undo"; yield break; }
                s_LocaleBefore = null;
            }
            else
            {
                if (!lm.GetSupportedLocales().Contains(want)) { rec["error"] = $"the game has no locale '{want}' ({string.Join(", ", lm.GetSupportedLocales())})"; yield break; }
                if (s_LocaleBefore == null) s_LocaleBefore = lm.activeLocaleId;
            }
            lm.SetActiveLocale(want);
            rec["locale"] = lm.activeLocaleId;
            Mod.Log.Info("Placemaker suite: locale " + lm.activeLocaleId);
            yield return Frames(10);
        }
    }
}
