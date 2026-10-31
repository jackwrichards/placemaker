// Game runner — hand edits (P2-1, Core/Touches.cs) made the way a player makes them: the panel's chip for the kind, then a
// left-click on the map on the stall or outline side the plan offers. GPL-3.0.
//   {"do":"touch","kind":"stall.accessible","pick":"far"}      a plain stall far from the first entrance
//   {"do":"touch","kind":"stall.island","pick":"middle","index":2}   the third plain stall nearest the middle of the lot
//   {"do":"touch","kind":"grid.edge","side":"west"}             the outline side furthest west
//   {"do":"touch","kind":"stall.accessible","pick":"touched"}   the stall that edit was made on (clicking it again takes it off)
// Records what was clicked, how many hand edits the step has before and after, and the state of the clicked spot once re-planned.
//   {"do":"sketch","kind":"line.aisle","points":[[0.2,0.1],[0.8,0.9]]}   a sketch line (Core/Tool/LineSketch): a click per
//       point (fractions of the outline's box, west→east and south→north; snapping applies like a player's), then a right-click
//       to finish. "shift":true clicks with Shift (no snapping). Records the points clicked, where the line's points landed
//       and the hand edits before and after.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Game.Common;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private IEnumerator StepSketch(ScenarioState st, JObject step, JObject rec)
        {
            if (_tool.Shape == null) { rec["error"] = "no outline"; yield break; }
            yield return EnsureDesign(st, rec, "Hand edits");
            if (rec["error"] != null) yield break;
            yield return WaitPlan(st, rec);
            string kind = (string)step["kind"] ?? "line.aisle";
            var plan = _tool.Plan;
            var layer = plan?.Layers.FirstOrDefault(l => l != null && ((string)step["path"] == null || Touches.StepOf(l.StepPath) == (string)step["path"]));
            if (layer == null) { rec["error"] = "no plan layer"; yield break; }
            string path = Touches.StepOf(layer.StepPath);
            rec["path"] = path;
            yield return UiAct(st, rec, "hand edit " + kind, "touch-" + kind, "setTouchKind", new object[] { path, kind }, () => _tool.TouchKind == kind, () => _tool.SetTouchKind(path, kind));
            if (_tool.TouchKind != kind) { rec["error"] = "the hand-edit kind did not take"; yield break; }
            var ring = _tool.Shape.Ring;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            foreach (var p in ring) { mn = math.min(mn, p.xz); mx = math.max(mx, p.xz); }
            var stepObj = RecipePaths.Resolve(_tool.CurrentRecipe, path);
            int before = stepObj?.Touches.Count ?? 0;
            rec["touchesBefore"] = before;
            bool shift = (bool?)step["shift"] ?? false;
            var clicked = new JArray();
            foreach (var pt in (step["points"] as JArray) ?? new JArray())
            {
                if (!(pt is JArray xy) || xy.Count < 2) continue;
                var at = math.lerp(mn, mx, new float2((float)xy[0], (float)xy[1]));
                clicked.Add(new JArray(Math.Round(at.x, 1), Math.Round(at.y, 1)));
                yield return Press(st, rec, new float3(at.x, _fixture.H(at.x, at.y), at.y), false, _ => { }, shift: shift);
            }
            rec["clicked"] = clicked;
            rec["drawing"] = _tool.SketchPoints.Count;
            var last = clicked.Count > 0 ? (JArray)clicked[clicked.Count - 1] : null;
            if (last != null) yield return Press(st, rec, new float3((float)last[0], _fixture.H((float)last[0], (float)last[1]), (float)last[1]), true, _ => { });   // finish
            yield return KeyPress("esc");   // done
            stepObj = RecipePaths.Resolve(_tool.CurrentRecipe, path);
            int after = stepObj?.Touches.Count ?? 0;
            rec["touchesAfter"] = after;
            string landed = "";
            if (stepObj != null && after > before)
            {
                var t = stepObj.Touches[stepObj.Touches.Count - 1];
                rec["arg"] = t.Arg;
                landed = t.Arg;
            }
            yield return WaitPlan(st, rec);
            Mod.Log.Info($"Placemaker suite sketch: {kind}, {clicked.Count} click(s); hand edits {before} -> {after}; points {landed}");
        }

        private IEnumerator StepTouch(ScenarioState st, JObject step, JObject rec)
        {
            if (_tool.Shape == null) { rec["error"] = "no outline"; yield break; }
            yield return EnsureDesign(st, rec, "Hand edits");
            if (rec["error"] != null) yield break;
            yield return WaitPlan(st, rec);
            string kind = (string)step["kind"] ?? "stall.accessible";
            string group = Touches.GroupOf(kind);
            var plan = _tool.Plan;
            var layer = plan?.Layers.FirstOrDefault(l => l != null && ((string)step["path"] == null || Touches.StepOf(l.StepPath) == (string)step["path"]) && l.TouchTargets.Any(t => t.Group == group));
            if (layer == null) { rec["error"] = "the plan offers nothing to touch for " + kind; yield break; }
            string path = Touches.StepOf(layer.StepPath);
            rec["path"] = path;
            yield return UiAct(st, rec, "hand edit " + kind, "touch-" + kind, "setTouchKind", new object[] { path, kind }, () => _tool.TouchKind == kind, () => _tool.SetTouchKind(path, kind));
            if (_tool.TouchKind != kind) { rec["error"] = "the hand-edit kind did not take"; yield break; }

            var targets = layer.TouchTargets.Where(t => t.Group == group).ToList();
            TouchTarget pick = null;
            if (group == "grid")
            {
                string side = (string)step["side"] ?? "west";
                float2 d = side == "east" ? new float2(1, 0) : side == "north" ? new float2(0, 1) : side == "south" ? new float2(0, -1) : new float2(-1, 0);
                pick = targets.OrderByDescending(t => math.dot(t.Center.xz, d)).FirstOrDefault();
            }
            else
            {
                string how = (string)step["pick"] ?? "far";
                int index = (int?)step["index"] ?? 0;
                string state = (string)step["state"] ?? (how == "touched" ? null : "normal");
                var pool = targets.Where(t => state == null || t.State == state).ToList();
                if (how == "touched") pool = targets.Where(t => !string.IsNullOrEmpty(t.Touched)).ToList();
                float2 entrance = st.Markers.Count > 0 ? st.Markers[0].At.xz : _tool.Shape.Ring[0].xz;
                float2 mid = float2.zero;
                foreach (var p in _tool.Shape.Ring) mid += p.xz;
                mid /= math.max(1, _tool.Shape.Ring.Length);
                IEnumerable<TouchTarget> ordered = how == "near" ? pool.OrderBy(t => math.distance(t.Center.xz, entrance))
                    : how == "middle" ? pool.OrderBy(t => math.distance(t.Center.xz, mid))
                    : how == "touched" ? pool.OrderBy(t => math.distance(t.Center.xz, entrance))
                    : pool.OrderByDescending(t => math.distance(t.Center.xz, entrance));
                // far: skip the row ends (a group needs stalls on both sides)
                pick = ordered.Skip(how == "far" ? 3 + index : index).FirstOrDefault();
            }
            if (pick == null) { yield return KeyPress("esc"); rec["error"] = "no " + group + " to click"; yield break; }
            rec["target"] = new JArray(Math.Round(pick.Center.x, 1), Math.Round(pick.Center.z, 1));
            rec["stateBefore"] = pick.State;
            var stepObj = RecipePaths.Resolve(_tool.CurrentRecipe, path);
            int before = stepObj?.Touches.Count ?? 0;
            rec["touchesBefore"] = before;
            RaycastHit? hit = null;
            yield return Press(st, rec, new float3(pick.Center.x, _fixture.H(pick.Center.x, pick.Center.z), pick.Center.z), false, h => hit = h);
            yield return KeyPress("esc");   // done
            stepObj = RecipePaths.Resolve(_tool.CurrentRecipe, path);
            rec["touchesAfter"] = stepObj?.Touches.Count ?? 0;
            yield return WaitPlan(st, rec);
            var now = _tool.Plan?.Layers.FirstOrDefault(l => l != null && Touches.StepOf(l.StepPath) == path)?.TouchTargets
                .Where(t => t.Group == group).OrderBy(t => math.distance(t.Center.xz, pick.Center.xz)).FirstOrDefault();
            if (now != null) { rec["stateAfter"] = now.State; rec["touched"] = now.Touched; }
            if (_tool.Plan != null)
                foreach (var l in _tool.Plan.Layers)
                    if (l != null && Touches.StepOf(l.StepPath) == path && l.Facts.TryGetValue("touches.dropped", out var dr)) rec["dropped"] = dr;
            Mod.Log.Info($"Placemaker suite touch: {kind} at ({pick.Center.x:F1}, {pick.Center.z:F1}) {pick.State} -> {now?.State ?? "?"}; hand edits {before} -> {stepObj?.Touches.Count ?? 0}");
        }
    }
}
