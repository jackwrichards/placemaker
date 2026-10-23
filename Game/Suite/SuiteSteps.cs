// Game runner: scenarios as step scripts (suite/README.md has the vocabulary with an example per step).
// A scenario is a list of steps that drive the real tool the way a person does:
//   * world clicks: the point is projected to the screen (panned into view like a person would, refused under the
//     panel), the GAME's raycast at that screen point decides what is hit, the cursor glides there, then clicks;
//   * panel actions: the panel's real elements are clicked (data-testid) or its triggers fired through the UI view,
//     in the stage whose panel shows that control (the person's way there is taken and recorded);
//   * keys go through the tool's own key handling (Enter, Esc, Ctrl+Z/Y).
// Every action checks that it happened (a click that changed nothing is logged with the reason and the scenario's
// remaining steps may stop). Waits are on readiness and logged; shots go through the settle gate (SuiteShots). The
// runner measures and judges nothing: what Placemaker did is in its own log. This file: the scenario loop, step
// dispatch, waits.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private sealed class ScenarioState
        {
            public Scenario S;
            public JObject Result = new JObject();
            public int Group;
            public bool Built, Deleted;
            public float2[] Ring;
            public float3 Focus;
            public float Radius = 80f;
            public int FileIndex;
            /// <summary>Entrances as the script placed them (position, kind, step path), in script order.</summary>
            public readonly List<(float3 At, string Kind, string Path)> Markers = new List<(float3, string, string)>();
            public JObject Shots => Result["shots"] as JObject ?? (JObject)(Result["shots"] = new JObject());
            /// <summary>Hard failure (no outline, type not selected, build failed): the remaining steps are skipped.</summary>
            public bool Stop;
            public string Pace = "human";
            public int BuildCount;
            public int StepIndex;
        }

        private readonly Dictionary<string, ScenarioState> _byId = new Dictionary<string, ScenarioState>(StringComparer.Ordinal);
        private EntityQuery _tempQuery;

        // what happened last (for the settle gate's wall-clock floor)
        private float _eventAt;
        private string _event = "load";

        private void Event(string what)
        {
            _event = what;
            _eventAt = Time.realtimeSinceStartup;
        }

        // ───────────── defaults: one-line scenarios become step lists ─────────────

        private static List<JObject> DefaultSteps(Scenario s)
        {
            var steps = new List<JObject>();
            if (s.Draw != null && s.Draw.Length >= 2)
                steps.Add(new JObject { ["do"] = "area", ["tool"] = "points", ["rect"] = new JArray(s.Draw[0], s.Draw[1]) });
            else steps.Add(new JObject { ["do"] = "area", ["tool"] = s.Plot != null ? "land" : "bucket" });
            steps.Add(new JObject { ["do"] = "type", ["layer"] = s.Layer, ["preset"] = s.Preset ?? "" });
            foreach (var v in s.Values) steps.Add(new JObject { ["do"] = "value", ["path"] = v.path ?? "0", ["id"] = v.id, ["json"] = v.json });
            steps.Add(new JObject { ["do"] = "entrances" });
            string when = (s.BuildWhen ?? "settled").ToLowerInvariant();
            if (when == "settled")
            {
                steps.Add(new JObject { ["do"] = "preview", ["shot"] = "preview", ["camera"] = new JObject { ["target"] = "build", ["view"] = "oblique" }, ["shotMode"] = "overlay" });
                steps.Add(new JObject { ["do"] = "shot", ["name"] = "ghost", ["camera"] = new JObject { ["target"] = "build", ["view"] = "oblique" }, ["shotMode"] = "render" });
            }
            // planReady / ghostPending: the person presses Next and Build at once (the build step does both), no preview look
            if (!string.IsNullOrEmpty(s.PreviewMode)) steps.Add(new JObject { ["do"] = "previewMode", ["mode"] = s.PreviewMode });
            steps.Add(new JObject { ["do"] = "build", ["when"] = s.BuildWhen ?? "settled" });
            steps.Add(new JObject { ["do"] = "shot", ["name"] = "built", ["camera"] = new JObject { ["target"] = "build", ["view"] = "oblique" }, ["shotMode"] = "render" });
            steps.Add(new JObject { ["do"] = "shot", ["name"] = "top", ["camera"] = new JObject { ["target"] = "build", ["view"] = "top" }, ["shotMode"] = "render" });
            return steps;
        }

        // ───────────── one scenario ─────────────

        private IEnumerator RunScenario(ScenarioState st)
        {
            var s = st.S;
            var res = st.Result;
            var errMark = ErrorMark();
            res["id"] = s.Id;
            if (!string.IsNullOrEmpty(s.Layer)) res["layer"] = s.Layer;
            if (!string.IsNullOrEmpty(s.Preset)) res["preset"] = s.Preset;
            res["note"] = s.Note;
            res["git"] = _cfg.git;
            res["assembly"] = AssemblyVersion();
            st.Pace = (s.Pace ?? _cfg.pace ?? "human").ToLowerInvariant();
            res["pace"] = st.Pace;
            res["seed"] = s.Seed;
            if (s.Block == null) { res["status"] = "no-block"; res["siteNote"] = s.SiteNote; yield break; }
            var b = s.Block;
            res["block"] = new JObject
            {
                ["id"] = b.Id, ["kind"] = b.Kind.ToString(), ["requested"] = s.Site, ["west"] = b.West, ["east"] = b.East, ["south"] = b.South, ["north"] = b.North,
                ["center"] = new JArray(b.Center.x, b.Center.y), ["reused"] = _fixture.TimesUsed(b) > 1, ["note"] = s.SiteNote,
                ["size"] = $"{b.X1 - b.X0:F0} x {b.Z1 - b.Z0:F0} m between road centre lines",
            };
            if (st.Focus.Equals(float3.zero)) st.Focus = FocusOf(b);
            Say($"scenario {s.Id} on {b.Id} ({b.Kind}){(s.On != null ? " on scenario " + s.On : "")}, pace {st.Pace}");
            _byId[s.Id] = st;
            _look?.Reassert();

            yield return OpenTool();
            // a clean slate: no leftover clicks, no history of the previous scenario, nothing still planning or building
            int leftover = PlacemakerToolSystem.SuiteResetInput();
            if (leftover > 0) res["leftoverInputFrames"] = leftover;
            yield return Idle(res);
            if ((string)res["status"] == "tool-not-idle") yield break;
            if (s.On == null)
            {
                if (_tool.Stage == ToolStage.Manage) _tool.GoToStage(ToolStage.Area);
                if (_tool.SuiteHasSelection) _tool.ClearArea();
                _tool.SuiteLeavePainter();   // a scenario starts on a type, never on the polish painter
                _tool.SuiteResetHistory();
                yield return Frames(2);
            }
            else
            {
                // an 'on' scenario starts from the host's build: its receipt is loaded the way a person edits it, or
                // only camera/shot/issue/select/delete steps run (checked in RunOne)
                _tool.SuiteResetHistory();
            }
            PlacemakerToolSystem.SuiteResetSeed(s.Seed);

            var steps = s.Steps ?? DefaultSteps(s);
            var outSteps = new JArray();
            res["steps"] = outSteps;
            res["status"] = "running";
            for (int i = 0; i < steps.Count && !st.Stop; i++)
            {
                var step = steps[i];
                string what = ((string)step["do"] ?? "").Trim();
                var rec = new JObject { ["i"] = i, ["do"] = what };
                foreach (var p in step.Properties()) if (p.Name != "do" && p.Name != "shot" && p.Name != "camera" && !p.Name.StartsWith("_")) rec[p.Name] = p.Value.DeepClone();
                outSteps.Add(rec);
                st.StepIndex = i;
                _scenario = s.Id + "#" + i + ":" + what;
                var bad = Schema.CheckStep(step);
                if (bad != null) { rec["error"] = bad; if (IsHard(what)) st.Stop = true; continue; }
                int pending = PlacemakerToolSystem.SuiteResetInput();
                if (pending > 0) rec["staleInputFramesDropped"] = pending;
                float t0 = Time.realtimeSinceStartup;
                OpenWindow(i, what);
                yield return Guard(Step(st, step, rec), e => { rec["error"] = "suite error: " + e.GetType().Name + ": " + e.Message; rec["suiteError"] = true; Say($"step {i} {what} threw: {e}"); });
                if (PlacemakerToolSystem.SuiteFramesPending > 0)
                {
                    rec["error"] = (rec["error"] != null ? rec["error"] + "; " : "") + $"{PlacemakerToolSystem.SuiteFramesPending} scripted input frames never ran (the tool did not take them)";
                    PlacemakerToolSystem.SuiteResetInput();
                }
                rec["ms"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
                string shot = (string)step["shot"];
                if (!string.IsNullOrEmpty(shot) && what != "shot" && what != "camera")
                {
                    // the step's own shot: frames what the step asks for, else what the step is about (never an automatic close-up)
                    var cam = step["camera"] as JObject ?? DefaultShotCamera(what);
                    var sstep = ShotOptions(step);
                    sstep["camera"] = cam;
                    if (what == "hover") sstep["keepCursor"] = true;
                    var srec = new JObject();
                    yield return Guard(StepShot(st, sstep, srec, shot), e => srec["error"] = "suite error: " + e.Message);
                    rec["shotRecord"] = srec;
                    if (srec["error"] != null && rec["error"] == null) rec["shotError"] = srec["error"];
                }
                rec["frames"] = CloseWindow();
                Say($"step {i} {what}: {rec["ms"]} ms, worst frame {rec["frames"]?["maxMs"]} ms, over 50 ms: {rec["frames"]?["over50"]}, over 100: {rec["frames"]?["over100"]} of {rec["frames"]?["frames"]}" + (rec["planComputeMs"] != null ? $", plan {rec["planComputeMs"]} ms" : "")
                    + (rec["shot"] != null ? $", shot {rec["shot"]}" : rec["shotRecord"]?["shot"] != null ? $", shot {rec["shotRecord"]["shot"]}" : "") + (rec["error"] != null ? " — " + rec["error"] : ""));
                if (rec["error"] != null && IsHard(what) && !((bool?)rec["suiteError"] ?? false)) st.Stop = true;
            }
            _scenario = s.Id;
            yield return Frames(30);   // errors of the last step land in this scenario's window
            res["errors"] = ErrorsSince(errMark);
            if ((string)res["status"] == "running") res["status"] = st.Deleted ? "deleted" : st.Built ? "built" : "designed";
            Totals(res);
        }

        /// <summary>The shot of a step without its own camera: the build for build/preview/delete steps, else the current view.</summary>
        private static JObject DefaultShotCamera(string what)
        {
            switch (what)
            {
                case "build": case "preview": case "delete": case "edit": case "bulldoze": case "area":
                    return new JObject { ["target"] = "build", ["view"] = "oblique" };
            }
            return null;
        }

        private static bool IsHard(string what) => what == "area" || what == "type" || what == "build" || what == "preview";

        /// <summary>
        /// The tool takes no new scene until nothing is building, planning or emitting a ghost. Stuck for 20 s (a preview
        /// that never finishes) → closed and reopened once, as a person would; still stuck → the batch stops at once with
        /// the reason, instead of every later scenario waiting on the same stuck tool (a 35-build batch once spent 34 min so).
        /// </summary>
        private IEnumerator Idle(JObject res)
        {
            float t0 = Time.realtimeSinceStartup;
            bool IsIdle() => !_tool.Building && !_tool.Planning && !_tool.GhostPending;
            yield return Until(IsIdle, _tool.Building ? 60f : 20f, "tool idle");
            if (_timedOut && !_tool.Building)
            {
                string stuck = ToolState();
                res["toolReset"] = stuck;
                Say($"tool not idle after 20 s ({stuck}): closing and reopening it");
                yield return CloseTool();
                yield return Frames(30);
                yield return OpenTool();
                yield return Until(IsIdle, 45f, "tool idle after reopening it");
                if (!_timedOut) Say("tool idle again after reopening it");
            }
            res["idleWaitMs"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
            if (_timedOut)
            {
                res["status"] = "tool-not-idle";
                res["refused"] = $"tool not idle ({ToolState()})";
                _abortBatch = _tool.Building
                    ? "a build was still running when the next scenario started: every later result would be contaminated"
                    : $"the Placemaker tool stayed busy even after closing and reopening it ({ToolState()}): every later scenario would wait on it";
            }
        }

        private string ToolState() =>
            $"building {_tool.Building}, planning {_tool.Planning}, ghost pending {_tool.GhostPending}, stage {_tool.Stage}, active {_tool.IsActive}"
            + (Placemaker.Game.Preview.GhostPreview.Service is Placemaker.Game.Build.BuildService b ? ", " + b.GhostDebug : "");

        /// <summary>The build button is greyed out for a reason no waiting can change (plan errors, money, no area or type).</summary>
        private bool PlanBlocked(out string why)
        {
            var p = _tool.SuitePrimaryInfo();
            why = null;
            if (p.Enabled) return false;
            switch (p.Reason)
            {
                case "Placemaker.Reason.PlanErrors": case "Placemaker.Reason.NoMoney": case "Placemaker.Reason.BuildUnavailable":
                case "Placemaker.Reason.NoArea": case "Placemaker.Reason.NoRecipe": case "Placemaker.Reason.CloseArea": case "Placemaker.Reason.GroupsUnavailable":
                    why = p.Reason + " " + _tool.SuiteUnreadyText();
                    return true;
            }
            return false;
        }

        private string _abortBatch;

        /// <summary>Sum of each wait kind over the steps (nested records included) and every timeout, for the summary.</summary>
        private static void Totals(JObject res)
        {
            var t = new JObject();
            var timeouts = new JArray();
            void Walk(JObject s)
            {
                if (s["waits"] is JObject w) foreach (var p in w.Properties()) t[p.Name] = Math.Round(((double?)t[p.Name] ?? 0) + (double)p.Value);
                if (s["timeouts"] is JArray to) foreach (var x in to) timeouts.Add(x);
                if (s["planComputeMs"] != null) t["planComputeMs"] = Math.Max((double?)t["planComputeMs"] ?? 0, (double)s["planComputeMs"]);
                foreach (var key in new[] { "shotRecord" }) if (s[key] is JObject n) Walk(n);
                foreach (var key in new[] { "shots", "placed", "sub" }) if (s[key] is JArray a) foreach (var n in a.OfType<JObject>()) Walk(n);
            }
            if (res["steps"] is JArray steps) foreach (var s in steps.OfType<JObject>()) Walk(s);
            res["waitsMs"] = t;
            res["timeouts"] = timeouts;
        }

        private IEnumerator Step(ScenarioState st, JObject step, JObject rec)
        {
            string what = (string)step["do"] ?? "";
            switch (what)
            {
                case "area": yield return StepArea(st, step, rec); break;
                case "clear": yield return StepClear(st, step, rec); break;
                case "hover": yield return StepHover(st, step, rec); break;
                case "type": yield return StepType(st, step, rec); break;
                case "value": yield return StepValue(st, step, rec); break;
                case "entrance": yield return StepEntrance(st, step, rec); break;
                case "entrances": yield return StepEntrancesAuto(st, step, rec); break;
                case "moveEntrance": yield return StepMoveEntrance(st, step, rec, false); break;
                case "removeEntrance": yield return StepMoveEntrance(st, step, rec, true); break;
                case "preview": yield return StepPreview(st, step, rec); break;
                case "previewMode": yield return StepPreviewMode(st, step, rec); break;
                case "edit": yield return StepEdit(st, step, rec); break;
                case "build": yield return StepBuild(st, step, rec); break;
                case "select": yield return StepSelect(st, step, rec); break;
                case "delete": yield return StepDelete(st, step, rec); break;
                case "bulldoze": yield return StepBulldoze(st, step, rec); break;
                case "infoEdit": yield return StepInfo(st, step, rec, edit: true); break;
                case "infoRemove": yield return StepInfo(st, step, rec, edit: false); break;
                case "infoUngroup": yield return StepInfoUngroup(st, step, rec); break;
                case "camera": yield return StepCameraAndShot(st, step, rec); break;
                case "terrain": yield return StepTerrainMap(st, step, rec); break;
                case "shot": yield return StepShot(st, step, rec, (string)step["name"] ?? (string)step["shot"] ?? "shot" + rec["i"]); break;
                case "wait":
                    if (step["frames"] != null) yield return Frames((int)step["frames"]);
                    else yield return Seconds((float?)step["seconds"] ?? 1f);
                    break;
                case "grow": yield return StepGrow(st, step, rec); break;
                case "undo": yield return StepUndoRedo(st, step, rec, true); break;
                case "redo": yield return StepUndoRedo(st, step, rec, false); break;
                case "key": yield return StepKey(st, step, rec); break;
                case "rightClick": yield return StepRightClick(st, step, rec); break;
                case "anarchy": yield return StepAnarchy(st, step, rec); break;
                case "group": yield return StepGroupSwitch(st, step, rec); break;
                case "setting": yield return StepSetting(st, step, rec); break;
                case "options": yield return StepOptions(st, step, rec); break;
                case "preset": yield return StepPreset(st, step, rec); break;
                case "lab": yield return StepLab(st, step, rec); break;
                case "polish": yield return StepPolish(st, step, rec); break;
                default: rec["error"] = "unknown step '" + what + "' (suite/schema.json lists the steps)"; break;
            }
        }

        // ───────────── readiness waits (recorded) ─────────────

        private IEnumerator WaitFor(JObject rec, string name, Func<bool> ready, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return Until(ready, timeout, name);
            Waited(rec, name, Time.realtimeSinceStartup - t0, _timedOut ? timeout : 0f);
        }

        private void Waited(JObject rec, string name, float seconds, float timedOutAfter)
        {
            var w = rec["waits"] as JObject ?? (JObject)(rec["waits"] = new JObject());
            w[name] = Math.Round(((double?)w[name] ?? 0) + seconds * 1000.0);
            if (timedOutAfter > 0f)
            {
                var t = rec["timeouts"] as JArray ?? (JArray)(rec["timeouts"] = new JArray());
                t.Add($"timed out waiting for {name} after {timedOutAfter:F0} s");
            }
        }

        // No type chosen (d4467e3: none until the player picks one; an area drawn first or after a build) plans nothing:
        // nothing to wait for (it waited 240 s).
        private bool PlanReady() => _tool.Shape == null || string.IsNullOrEmpty(_tool.RecipeId) || _tool.CurrentRecipe == null
            || (!_tool.Planning && _tool.Plan != null && _tool.PlanRevision == _tool.InputRevision);

        /// <summary>The plan for the current input revision is finished. At "user" pace a person does not wait: a few frames only.</summary>
        private IEnumerator WaitPlan(ScenarioState st, JObject rec, float timeout = 240f, bool always = false)
        {
            yield return Frames(2);   // the change reaches the planner's input revision on the next tool update
            if (st != null && st.Pace == "user" && !always) { yield return Frames(4); rec["planWait"] = "skipped (user pace)"; yield break; }
            yield return WaitFor(rec, "plan", PlanReady, timeout);
            if (_tool.Plan != null) rec["planComputeMs"] = Math.Round(_tool.PlanMilliseconds);
            rec["planRevision"] = _tool.PlanRevision;
        }

        private int TempCount()
        {
            if (_tempQuery == default) _tempQuery = _world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Tools.Temp>());
            return _tempQuery.CalculateEntityCount();
        }

        /// <summary>The ghost is emitted (Ghost mode effective, nothing pending) and its temporaries stopped changing.</summary>
        private IEnumerator WaitGhost(JObject rec, float timeout = 45f)
        {
            float t0 = Time.realtimeSinceStartup;
            // (shops4) a refused plan (an error: Build waits) shows no ghost: nothing to wait for
            yield return Until(() => (_tool.PreviewModeEffective == PreviewMode.Ghost && !_tool.GhostPending) || (_tool.PlanRefused && !_tool.Planning), timeout, "ghost");
            if (_tool.PlanRefused) { rec["ghost"] = "none: the plan is refused"; yield break; }
            bool timedOut = _timedOut;
            int last = -1, still = 0;
            float until = Time.realtimeSinceStartup + 30f;
            while (!timedOut && still < 10 && Time.realtimeSinceStartup < until)
            {
                yield return null;
                int n = TempCount();
                if (n == last && !_tool.GhostPending) still++; else still = 0;
                last = n;
            }
            if (still < 10 && !timedOut) timedOut = true;
            Waited(rec, "ghost", Time.realtimeSinceStartup - t0, timedOut ? timeout : 0f);
            rec["ghostTemps"] = last;
            rec["ghostDropped"] = _tool.GhostDropped;
            Event("ghost");
        }

        private static float StepPause(ScenarioState st) => st == null ? 0f : st.Pace == "human" ? 0.4f : st.Pace == "user" ? 0.05f : 0f;
    }
}
