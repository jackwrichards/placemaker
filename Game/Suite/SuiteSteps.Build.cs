// Game runner: build, select, delete (panel, bulldozer, info panel), grow. Each step does what a person does and waits
// until the game is done with it; what the build contains is in Placemaker's log and its build audit.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Groups;
using Placemaker.Game.Tool;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private IEnumerator StepBuild(ScenarioState st, JObject step, JObject rec)
        {
            var res = st.Result;
            string when = ((string)step["when"] ?? (st.Pace == "user" ? "planReady" : "settled")).ToLowerInvariant();
            rec["when"] = when;
            if (_tool.Stage != ToolStage.Review)
            {
                var r = new JObject();
                var sub = rec["sub"] as JArray ?? (JArray)(rec["sub"] = new JArray());
                sub.Add(r);
                if (when == "settled") yield return StepPreview(st, step, r);
                else
                {
                    // a person pressing Next then Build at once (Enter, Enter): no wait for the ghost
                    yield return WaitPlan(st, r, always: true);
                    var p = _tool.SuitePrimaryInfo();
                    if (p.Kind == "toReview" && p.Enabled) yield return UiPrimary(st, r, "Next");
                }
                if (r["error"] != null) { rec["error"] = r["error"]; yield break; }
            }
            if (when == "settled")
            {
                yield return WaitPlan(st, rec, always: true);
                // a plan that cannot be built has no ghost worth waiting for: the greyed-out button is reported below at once
                if (!PlanBlocked(out _) && _tool.PreviewModeSelected == PreviewMode.Ghost && (_tool.GhostPending || _tool.PreviewModeEffective != PreviewMode.Ghost)) yield return WaitGhost(rec);
            }
            else if (when == "planready") yield return WaitPlan(st, rec, always: true);
            else if (when == "ghostpending")
            {
                yield return Frames(1);
                rec["ghostPendingAtPress"] = _tool.GhostPending;
            }
            // (nature makeover) what grows in the plan, for camera steps with "clearView" after the build
            st.Sightblock.Clear();
            if (_tool.Plan?.Layers != null)
                foreach (var l in _tool.Plan.Layers) foreach (var o in l.Objects)
                    if (o != null && (o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush)) st.Sightblock.Add(o.Position.xz);
            var primary = _tool.SuitePrimaryInfo();
            if (!primary.Enabled && primary.Reason == "Placemaker.Reason.AcknowledgePermanent")
            {
                yield return UiAct(st, rec, "OK, permanent", "ack-permanent", "acknowledgePermanent", new object[] { true }, () => _tool.PermanentAcknowledged, () => _tool.AcknowledgePermanent(true));
                primary = _tool.SuitePrimaryInfo();
                rec["acknowledgedPermanent"] = true;
            }
            if ((primary.Kind != "build" && primary.Kind != "finishEdit") || !primary.Enabled)
            {
                rec["error"] = "build button: " + primary.Kind + (primary.Enabled ? "" : " disabled") + " " + _tool.SuiteUnreadyText();
                res["status"] = "blocked";
                res["blockedAt"] = "review";
                res["unready"] = _tool.SuiteUnreadyText();
                yield break;
            }
            int doneBefore = _tool.SuiteBuildsDone;
            // (p2-beaches) the plan is gone after the build: keep its nets for the joint close-ups
            _builtNets = _tool.Plan?.Layers.SelectMany(l => l.Nets).ToList();
            float t0 = Time.realtimeSinceStartup;
            // what the build leaves: "fresh" (default, the area cleared as every scenario expects), "keep" (the area kept,
            // back to Design: the player's Build & keep area) or "close" (Build & close)
            string after = ((string)step["after"] ?? "fresh").ToLowerInvariant();
            _tool.SuiteNextAfterBuild = after == "keep" ? PlacemakerToolSystem.AfterBuild.KeepArea
                : after == "close" ? PlacemakerToolSystem.AfterBuild.Close : PlacemakerToolSystem.AfterBuild.Fresh;
            string via = ((string)step["via"] ?? "panel").ToLowerInvariant();
            if (via == "enter") { yield return KeyPress("enter"); rec["ui"] = new JArray(new JObject { ["action"] = "Build", ["path"] = "key Enter" }); }
            else yield return UiPrimary(st, rec, "Build");
            if (rec["error"] != null) { res["status"] = "blocked"; res["blockedAt"] = "review"; yield break; }
            yield return WaitFor(rec, "buildStart", () => _tool.Building || _tool.SuiteBuildsDone != doneBefore, 10f);
            yield return WaitFor(rec, "build", () => !_tool.Building, 300f);
            bool buildTimedOut = _timedOut;
            res["buildMs"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
            rec["buildPath"] = _tool.SuiteLastBuildPath;
            if (_tool.SuiteBuildsDone == doneBefore)
            {
                var f = _tool.BuildFailure;
                res["status"] = "build-failed";
                res["failure"] = f == null ? (buildTimedOut ? "timeout" : "unknown") : f.Key + (f.Args != null && f.Args.Length > 0 ? " (" + string.Join(", ", f.Args) + ")" : "");
                rec["error"] = "build failed: " + res["failure"];
                if (buildTimedOut) _abortBatch = "a build did not finish in 300 s: the rest of the batch would run into it";
                yield break;
            }
            Event("build");
            st.Built = true;
            st.Deleted = false;
            st.BuildCount++;
            st.Group = _tool.SuiteLastBuiltGroup;
            rec["group"] = st.Group;
            res["status"] = "built";
            res["buildPath"] = _tool.SuiteLastBuildPath;
            res["notes"] = new JArray(_tool.BuildNotes.Select(n => n.Key + (n.Args != null && n.Args.Length > 0 ? " (" + string.Join(", ", n.Args) + ")" : "")));
            // the game is done with the build when no temporaries are left; the next picture waits for the scene to be still
            yield return WaitFor(rec, "settle", () => TempCount() == 0, 30f);
        }

        // ───────────── select (the Manage stage: a click on the build) ─────────────

        /// <summary>A person clicks the build: Manage stage, the game's raycast (lots, nets, objects) at its centre on the screen.</summary>
        private IEnumerator SelectBuild(ScenarioState st, JObject rec)
        {
            if (st.Group == 0 || Services.Groups?.Get(st.Group) == null) { rec["error"] = "no build to select"; yield break; }
            if (_tool.Stage != ToolStage.Manage)
            {
                yield return UiGoToStage(rec, ToolStage.Manage);
                if (_tool.Stage != ToolStage.Manage) { yield return KeyPress("esc"); }
            }
            if (_tool.Stage != ToolStage.Manage) { rec["error"] = "cannot reach the Manage stage from " + _tool.Stage; yield break; }
            yield return Frames(3);   // the Manage raycast masks are in place
            // a person looks at the build to click it: when its centre is not comfortably on the screen, frame it first
            if (st.Ring != null)
            {
                var c3 = P3(st.Ring.Aggregate(float2.zero, (a2, p2) => a2 + p2) / st.Ring.Length);
                yield return ReadPanelRect();
                if (Clickable(c3, out var sc3) != null || sc3.y < Screen.height * 0.2f || sc3.y > Screen.height * 0.8f)
                {
                    var crec = new JObject();
                    bool okc = false;
                    yield return CameraStep(st, new JObject { ["target"] = "build", ["view"] = "oblique" }, crec, x => okc = x);
                    var implicitSteps = rec["implicit"] as JArray ?? (JArray)(rec["implicit"] = new JArray());
                    implicitSteps.Add("camera build oblique (to see the build before clicking it)");
                }
            }
            // aim at a visible member: the owner lot's centre, else the outline's centre
            float3 target = st.Ring != null ? P3(st.Ring.Aggregate(float2.zero, (a, p) => a + p) / st.Ring.Length) : st.Focus;
            int g = st.Group;
            int tries = 0;
            foreach (var cand in SelectTargets(st, target))
            {
                tries++;
                RaycastHit? hit = null;
                yield return Press(st, rec, cand, false, h => hit = h);
                if (rec["error"] != null && tries < 4) { rec.Remove("error"); continue; }
                yield return Frames(3);
                if (_tool.ManagedGroup == g) { rec["selected"] = g; rec["selectClicks"] = tries; yield break; }
                rec["selectMiss" + tries] = $"clicked ({cand.x:F0},{cand.z:F0}): managed {_tool.ManagedGroup}, hover {_tool.HoverGroup}";
                if (tries >= 4) break;
            }
            rec["error"] = $"one click on the build did not select it (group {g}; {tries} tries)";
        }

        private IEnumerable<float3> SelectTargets(ScenarioState st, float3 centre)
        {
            yield return centre;
            if (st.Ring == null) yield break;
            var (min, max) = Bounds(st.Ring, -8f);
            yield return P3(new float2(math.lerp(min.x, max.x, 0.3f), math.lerp(min.y, max.y, 0.3f)));
            yield return P3(new float2(math.lerp(min.x, max.x, 0.7f), math.lerp(min.y, max.y, 0.6f)));
            yield return P3(new float2(math.lerp(min.x, max.x, 0.5f), math.lerp(min.y, max.y, 0.8f)));
        }

        private IEnumerator StepSelect(ScenarioState st, JObject step, JObject rec)
        {
            yield return SelectBuild(st, rec);
        }

        // ───────────── delete ─────────────

        private IEnumerator StepDelete(ScenarioState st, JObject step, JObject rec)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built to delete (loose builds have no group)"; yield break; }
            int g = st.Group;
            // a person clicks the build, then Remove in its panel, then OK
            yield return SelectBuild(st, rec);
            if (rec["error"] != null) yield break;
            yield return UiAct(st, rec, "Remove", "manage-remove", null, null, null, null, mustChange: false, effectFrames: 4);
            yield return UiAct(st, rec, "OK (remove)", "confirm-ok", "removeGroup", new object[] { (double)g }, () => Services.Groups?.Get(g) == null, () => _tool.RemoveGroup(g), mustChange: false, effectFrames: 10);
            yield return AfterDelete(st, rec, g);
        }

        /// <summary>Waits until the group is gone and the game has no temporaries left.</summary>
        private IEnumerator AfterDelete(ScenarioState st, JObject rec, int g)
        {
            yield return WaitFor(rec, "delete", () => Services.Groups == null || Services.Groups.Get(g) == null, 60f);
            bool gone = !_timedOut;
            yield return WaitFor(rec, "settle", () => TempCount() == 0, 20f);
            Event("delete");
            st.Deleted = gone;
            st.Built = !gone;
            st.Result["status"] = gone ? "deleted" : "delete-failed";
            if (!gone) rec["error"] = $"the build (group {g}) is still there 60 s after the delete";
            if (_tool.Stage == ToolStage.Manage) { yield return KeyPress("esc"); }
        }

        /// <summary>The bulldozer's way: a delete definition for the build's owner lot (what BulldozeToolSystem applies), then the group's cascade.</summary>
        private IEnumerator StepBulldoze(ScenarioState st, JObject step, JObject rec)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built to bulldoze"; yield break; }
            int g = st.Group;
            var em = _world.EntityManager;
            var members = _lookup.MemberSet(g);
            string target = ((string)step["target"] ?? "owner").ToLowerInvariant();
            Entity victim = Entity.Null;
            foreach (var m in members)
            {
                if (!em.Exists(m) || em.HasComponent<Deleted>(m) || !em.HasComponent<PlacemakerMember>(m)) continue;
                var role = em.GetComponentData<PlacemakerMember>(m).Role;
                if (target == "owner" && role == MemberRole.OwnerArea) { victim = m; break; }
                if (target == "part" && role == MemberRole.Generic && em.HasComponent<global::Game.Objects.Object>(m)) { victim = m; break; }
            }
            if (victim == Entity.Null) { rec["error"] = $"no {target} member to bulldoze"; yield break; }
            rec["bulldozed"] = _lookup.KindOf(victim) + " " + _lookup.PrefabName(victim);
            yield return CloseTool();
            yield return ApplyWithFixtureTool(t =>
            {
                var def = t.NewDefinition();
                em.AddComponentData(def, new CreationDefinition { m_Original = victim, m_Flags = CreationFlags.Delete });
                em.AddComponent<Updated>(def);
                if (em.HasBuffer<global::Game.Areas.Node>(victim))
                {
                    var src = em.GetBuffer<global::Game.Areas.Node>(victim, true).ToNativeArray(Allocator.Temp);
                    em.AddBuffer<global::Game.Areas.Node>(def).CopyFrom(src);
                    src.Dispose();
                }
                else if (em.HasComponent<global::Game.Objects.Transform>(victim))
                {
                    var tr = em.GetComponentData<global::Game.Objects.Transform>(victim);
                    em.AddComponentData(def, new ObjectDefinition { m_Position = tr.m_Position, m_Rotation = tr.m_Rotation, m_LocalPosition = tr.m_Position, m_LocalRotation = tr.m_Rotation, m_ParentMesh = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_PrefabSubIndex = -1 });
                }
                return 1;
            }, "bulldoze " + rec["bulldozed"]);
            rec["via"] = "the bulldozer's delete definition (applied through the runner's fixture tool, not the bulldozer UI)";
            yield return AfterDelete(st, rec, g);
            yield return OpenTool();
        }

        /// <summary>The vanilla selected-info panel's Edit / Remove, with the Placemaker tool closed (how a person finds a build later).</summary>
        private IEnumerator StepInfo(ScenarioState st, JObject step, JObject rec, bool edit)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built"; yield break; }
            int g = st.Group;
            yield return CloseTool();
            var em = _world.EntityManager;
            var members = _lookup.MemberSet(g);
            Entity owner = members.FirstOrDefault(m => em.Exists(m) && em.HasComponent<PlacemakerMember>(m) && em.GetComponentData<PlacemakerMember>(m).Role == MemberRole.OwnerArea);
            if (owner == Entity.Null) owner = members.FirstOrDefault(m => em.Exists(m) && !em.HasComponent<Deleted>(m));
            var sel = _world.GetExistingSystemManaged<global::Game.UI.InGame.SelectedInfoUISystem>();
            try { sel?.SetSelection(owner); } catch (Exception e) { rec["selectError"] = e.Message; }
            yield return Frames(20);
            rec["selected"] = _lookup.KindOf(owner) + " " + _lookup.PrefabName(owner);
            if (edit)
            {
                yield return UiAct(st, rec, "info Edit", "info-edit", "editGroup", new object[] { (double)g }, () => _tool.EditingGroup == g, () => _tool.EditGroup(g), mustChange: false, effectFrames: 20);
                yield return Frames(10);
                var state = Bound(rec, "state");
                bool open = (bool?)state?["open"] ?? false;
                rec["toolActive"] = _tool.IsActive;
                rec["panelOpen"] = open;
                rec["stage"] = _tool.Stage.ToString();
                rec["editing"] = _tool.EditingGroup;
                if (_tool.EditingGroup != g) rec["error"] = "Edit in the info panel did not load the build";
                else if (!_tool.IsActive || !open) rec["error"] = $"Edit in the info panel loaded the build but the tool is {(_tool.IsActive ? "active" : "closed")} and the panel {(open ? "open" : "not shown")}";
                if (!_tool.IsActive) yield return OpenTool();
                if (_tool.EditingGroup == g) st.Ring = _tool.Shape?.Ring2D() ?? st.Ring;
            }
            else
            {
                yield return UiAct(st, rec, "info Remove", "info-remove", null, null, null, null, mustChange: false, effectFrames: 4);
                yield return UiAct(st, rec, "OK (remove)", "info-confirm-ok", "removeGroup", new object[] { (double)g }, () => Services.Groups?.Get(g) == null, () => _tool.RemoveGroup(g), mustChange: false, effectFrames: 10);
                yield return AfterDelete(st, rec, g);
                yield return OpenTool();
            }
            try { sel?.SetSelection(Entity.Null); } catch (Exception) { }
        }

        // ───────────── grow ─────────────

        private IEnumerator StepGrow(ScenarioState st, JObject step, JObject rec)
        {
            bool fast = (bool?)step["fast"] ?? true;
            yield return Grow((float?)step["seconds"] ?? 30f, "grow-" + st.S.Id + "-" + st.StepIndex, fast);
            rec["grow"] = _summary["grow-" + st.S.Id + "-" + st.StepIndex];
            if (st.Built && !st.Deleted && st.Ring != null && st.Ring.Length >= 3)
            {
                try { rec["census"] = GrowCensus(st); } catch (Exception e) { rec["censusError"] = e.Message; }
            }
            Event("grow");
        }

        /// <summary>
        /// (shops) Which road every grown building inside the build's outline faces (its Building.m_RoadEdge): one of the
        /// build's own lanes, or a city street. Logged to Placemaker.log as "grow census" so a run says, without pictures,
        /// whether the shops face the site's lanes.
        /// </summary>
        private JObject GrowCensus(ScenarioState st)
        {
            var em = _world.EntityManager;
            var ring = st.Ring;
            bool Inside(float2 p)
            {
                bool c = false;
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                    if ((ring[i].y > p.y) != (ring[j].y > p.y) && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x) c = !c;
                return c;
            }
            int ours = 0, city = 0, none = 0;
            var cityRoads = new Dictionary<string, int>(StringComparer.Ordinal);
            var ourRoads = new Dictionary<string, int>(StringComparer.Ordinal);
            var cityAt = new List<string>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var ents = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var b in ents)
                {
                    var p = em.GetComponentData<global::Game.Objects.Transform>(b).m_Position;
                    if (!Inside(p.xz)) continue;
                    var edge = em.GetComponentData<global::Game.Buildings.Building>(b).m_RoadEdge;
                    if (edge == Entity.Null || !em.Exists(edge)) { none++; continue; }
                    string name = _lookup.PrefabName(edge);
                    if (_lookup.GroupOf(edge) == st.Group) { ours++; ourRoads[name] = ourRoads.TryGetValue(name, out var n) ? n + 1 : 1; }
                    else { city++; cityRoads[name] = cityRoads.TryGetValue(name, out var n) ? n + 1 : 1; if (cityAt.Count < 12) cityAt.Add($"({p.x:F0}, {p.z:F0})"); }
                }
            }
            string Fmt(Dictionary<string, int> d) => string.Join(", ", d.Select(kv => $"{kv.Value} {kv.Key}"));
            Mod.Log.Info($"Placemaker grow census ({st.S.Id}, group {st.Group}): {ours + city + none} buildings in the outline; {ours} face the build's own lanes ({Fmt(ourRoads)}), {city} face city streets ({Fmt(cityRoads)}){(cityAt.Count > 0 ? " at " + string.Join(" ", cityAt) : "")}{(none > 0 ? $", {none} with no road" : "")}.");
            // (rf-shops3) Trees of the build after the growth: the shops types plant trees on their own plots; a building that grows
            // over them should hide (Overridden) or remove what is in its way. Measurement only.
            int trees = 0, hidden = 0, underBuildings = 0;
            var lots = new List<float2>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var b in ents)
                {
                    var p = em.GetComponentData<global::Game.Objects.Transform>(b).m_Position.xz;
                    if (Inside(p)) lots.Add(p);
                }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<global::Game.Objects.Tree>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var t in ents)
                {
                    if (em.GetComponentData<PlacemakerMember>(t).GroupId != st.Group) continue;
                    trees++;
                    bool hid = em.HasComponent<global::Game.Common.Overridden>(t);
                    if (hid) hidden++;
                    var p = em.GetComponentData<global::Game.Objects.Transform>(t).m_Position.xz;
                    if (!hid) foreach (var c in lots) if (math.distance(c, p) < 7f) { underBuildings++; break; }
                }
            Mod.Log.Info($"Placemaker grow census trees ({st.S.Id}, group {st.Group}): {trees} trees of the build after the growth, {hidden} hidden by the game (Overridden: something over them), {underBuildings} still shown within 7 m of a grown building's centre.");
            return new JObject { ["buildings"] = ours + city + none, ["ourLanes"] = ours, ["cityStreets"] = city, ["noRoad"] = none, ["cityRoads"] = Fmt(cityRoads), ["ourRoads"] = Fmt(ourRoads), ["trees"] = trees, ["treesHidden"] = hidden, ["treesUnderBuildings"] = underBuildings };
        }
    }
}
