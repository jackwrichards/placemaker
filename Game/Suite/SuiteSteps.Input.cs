// Game runner: world input like a person. A press at a world point is first made clickable (on the screen, not
// under the panel: the camera pans there like a person would, recorded), then the GAME's raycast at that screen point
// (with the active tool's own masks) decides what is hit, the cursor glides to it over a few frames, and clicks.
// Area tools (bucket, points, paint, road nodes) and entrance placement all use it.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Tools;
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
        private float2 Pt(JToken t, SuiteBlock b, bool abs) =>
            t is JArray a && a.Count >= 2 ? (abs ? new float2((float)a[0], (float)a[1]) : b.Center + new float2((float)a[0], (float)a[1])) : b.Center;

        private float3 P3(float2 p) => new float3(p.x, _fixture.H(p.x, p.y), p.y);

        private IEnumerator Feed(JObject rec, float timeout = 20f)
        {
            yield return WaitFor(rec, "input", () => PlacemakerToolSystem.SuiteFramesPending == 0, timeout);
            if (_timedOut)
            {
                int n = PlacemakerToolSystem.SuiteResetInput();
                rec["error"] = $"scripted input not consumed in {timeout:F0} s ({n} frames dropped): the tool was not running";
            }
            yield return Frames(3);
        }

        // ───────────── the panel's rectangle (a person cannot click the world under it) ─────────────

        private Rect? _panelRect;
        private int _panelRectFrame = -100;

        private IEnumerator ReadPanelRect()
        {
            if (Time.frameCount - _panelRectFrame < 30) yield break;
            _panelRectFrame = Time.frameCount;
            _panelRect = null;
            if (!SuiteUiSystem.Available) yield break;
            JObject reply = null;
            yield return UiRun(SuiteUiSystem.PanelRectJs, r => reply = r);
            var v = reply?["value"] as JObject;
            if (v == null || !((bool?)v["open"] ?? false)) yield break;
            var a = v["rect"] as JArray;
            var view = v["view"] as JArray;
            if (a == null || a.Count < 4) yield break;
            float sx = view != null && (float)view[0] > 0 ? Screen.width / (float)view[0] : 1f;
            float sy = view != null && (float)view[1] > 0 ? Screen.height / (float)view[1] : 1f;
            // CSS: top-left origin; Unity screen: bottom-left
            float x = (float)a[0] * sx, y = (float)a[1] * sy, w = (float)a[2] * sx, h = (float)a[3] * sy;
            _panelRect = new Rect(x, Screen.height - y - h, w, h);
        }

        private const float ScreenMargin = 24f;

        private string Clickable(float3 world, out Vector3 screen)
        {
            screen = SuiteCapture.ToScreen(world);
            if (screen.z <= 0f) return "behind the camera";
            if (screen.x < ScreenMargin || screen.y < ScreenMargin || screen.x > Screen.width - ScreenMargin || screen.y > Screen.height - ScreenMargin) return "off-screen";
            if (_panelRect.HasValue && _panelRect.Value.Contains(new Vector2(screen.x, screen.y))) return "under the panel";
            return null;
        }

        private sealed class RaycastContext { }
        private readonly RaycastContext _rayCtx = new RaycastContext();

        /// <summary>The game's raycast at a screen point with the active tool's masks; null when nothing is hit.</summary>
        private IEnumerator Raycast(Vector3 screen, Action<RaycastResult?> done, TypeMask? typeOverride = null)
        {
            var rs = _world.GetExistingSystemManaged<RaycastSystem>();
            var trs = _world.GetExistingSystemManaged<ToolRaycastSystem>();
            var cam = Camera.main;
            if (rs == null || trs == null || cam == null) { done(null); yield break; }
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var ray = cam.ScreenPointToRay(screen);
                float3 dir = ray.direction;
                var line = new Colossal.Mathematics.Line3.Segment(ray.origin, (float3)ray.origin + dir * (cam.farClipPlane / math.clamp(math.dot(dir, (float3)cam.transform.forward), 0.25f, 1f)));
                var input = new RaycastInput
                {
                    m_Line = line, m_Offset = trs.rayOffset, m_Owner = trs.owner,
                    m_TypeMask = typeOverride ?? trs.typeMask,
                    m_Flags = trs.raycastFlags & ~(RaycastFlags.UIDisable | RaycastFlags.ToolDisable | RaycastFlags.FreeCameraDisable | RaycastFlags.DebugDisable),
                    m_CollisionMask = trs.collisionMask, m_NetLayerMask = trs.netLayerMask, m_AreaTypeMask = trs.areaTypeMask,
                    m_RouteType = trs.routeType, m_TransportType = trs.transportType, m_IconLayerMask = trs.iconLayerMask, m_UtilityTypeMask = trs.utilityTypeMask,
                };
                rs.AddInput(_rayCtx, input);
                yield return null;
                var r = rs.GetResult(_rayCtx);
                if (r.IsCreated && r.Length > 0)
                {
                    var hit = r[0];
                    done(hit.m_Owner != Entity.Null ? hit : (RaycastResult?)null);
                    yield break;
                }
            }
            done(null);
        }

        /// <summary>
        /// Makes a world point clickable (pans the camera to it when it is off-screen or under the panel, like a person),
        /// raycasts at its screen point, glides the cursor there and clicks. rec gets the press (screen point, hit,
        /// how far the hit is from the scripted point, pans). Returns the hit through done (null = refused, with rec error).
        /// </summary>
        private IEnumerator Press(ScenarioState st, JObject rec, float3 target, bool right, Action<RaycastHit?> done, bool releaseOnly = false, TypeMask? mask = null, bool shift = false)
        {
            var presses = rec["presses"] as JArray ?? (JArray)(rec["presses"] = new JArray());
            var pr = new JObject { ["target"] = new JArray(Math.Round(target.x, 1), Math.Round(target.z, 1)) };
            presses.Add(pr);
            yield return ReadPanelRect();
            string why = Clickable(target, out var screen);
            if (why != null)
            {
                // a person scrolls the view until the spot is in front of them, away from the panel
                var c = _world.GetExistingSystemManaged<global::Game.Rendering.CameraUpdateSystem>()?.gamePlayController;
                if (c != null)
                {
                    pr["pan"] = why;
                    float3 pivot = target;
                    // keep the spot left of the panel: aim a little to its right
                    if (_panelRect.HasValue) pivot += new float3(math.cos(math.radians(c.angle.x)), 0f, -math.sin(math.radians(c.angle.x))) * (c.zoom * 0.15f);
                    var crec = new JObject();
                    yield return CameraTo(pivot, c.angle.x, c.angle.y, c.zoom, crec, null);
                    _panelRectFrame = -100;
                    yield return ReadPanelRect();
                    why = Clickable(target, out screen);
                    pr["afterPan"] = new JObject { ["screen"] = new JArray(Math.Round(screen.x), Math.Round(screen.y), Math.Round(screen.z, 1)), ["camera"] = SuiteCapture.CameraState(), ["arrived"] = crec["timeouts"] == null };
                }
                if (why != null) { pr["refused"] = why; rec["error"] = $"not clickable: the point ({target.x:F0},{target.z:F0}) is {why}"; done(null); yield break; }
            }
            pr["screen"] = new JArray(Math.Round(screen.x), Math.Round(screen.y));
            var trsNow = _world.GetExistingSystemManaged<ToolRaycastSystem>();
            pr["mask"] = (mask ?? trsNow?.typeMask)?.ToString() + " / " + _tool.Stage;
            RaycastResult? hit = null;
            yield return Raycast(screen, r => hit = r, mask);
            if (!hit.HasValue)
            {
                pr["hit"] = "nothing";
                rec["error"] = $"nothing under the cursor at screen ({screen.x:F0},{screen.y:F0}) for the tool's raycast";
                done(null);
                yield break;
            }
            var h = hit.Value.m_Hit;
            var owner = hit.Value.m_Owner;
            pr["hit"] = new JArray(Math.Round(h.m_HitPosition.x, 1), Math.Round(h.m_HitPosition.z, 1));
            pr["hitEntity"] = owner == Entity.Null ? "" : _lookup.KindOf(owner) + " " + _lookup.PrefabName(owner);
            pr["hitOffM"] = Math.Round(math.distance(h.m_HitPosition.xz, target.xz), 2);
            // the cursor arrives: from where it rests, or from the panel side when it is over the panel
            var from = PlacemakerToolSystem.SuiteCursor ?? (h.m_HitPosition + new float3(40f, 0f, 0f));
            int glide = st.Pace == "human" ? 8 : st.Pace == "user" ? 3 : 4;
            PlacemakerToolSystem.SuiteHover(from, h.m_HitPosition, glide);
            PlacemakerToolSystem.SuiteClickHit(owner, h, right, shift);
            yield return Feed(rec);
            float pause = StepPause(st);
            if (pause > 0f) yield return Seconds(pause);
            done(h);
        }

        // ───────────── area tools ─────────────

        /// <summary>
        /// (Area overhaul) The cursor rests on a spot in the Area stage with a tool picked, so the shot shows what it shows
        /// there: the fill under the cursor with its closed gaps, reach and stops (bucket), or the brush and the fill a
        /// stroke would paint in (paint). Nothing is clicked.
        /// </summary>
        private IEnumerator StepHover(ScenarioState st, JObject step, JObject rec)
        {
            var b = st.S.Block;
            bool abs = (bool?)step["abs"] ?? false;
            string tool = ((string)step["tool"] ?? "bucket").ToLowerInvariant();
            if (_tool.Stage != ToolStage.Area) yield return UiGoToStage(rec, ToolStage.Area);
            var mode = tool == "paint" ? SelectionMode.BlockPaint : tool == "points" || tool == "draw" ? SelectionMode.Draw : SelectionMode.Block;
            yield return UiSetAreaMode(rec, mode);
            var at = P3(Pt(step["at"], b, abs));
            var from = PlacemakerToolSystem.SuiteCursor ?? (at + new float3(30f, 0f, 0f));
            PlacemakerToolSystem.SuiteHover(from, at, 8);
            // After the glide the cursor rests there (the last scripted position repeats until new input).
            float wait = (float?)step["seconds"] ?? 2f;
            yield return Feed(rec);
            yield return Seconds(wait);
            var hover = _tool.HoverFace;
            rec["at"] = new JArray(Math.Round(at.x, 1), Math.Round(at.z, 1));
            if (hover?.Fill != null)
            {
                rec["fill"] = hover.Fill.ToString();
                rec["fillDoors"] = hover.Doors.Count;
                rec["fillStops"] = hover.Stops.Count;
            }
            else rec["fill"] = _tool.HoverFaceProblem?.Key ?? "none";
        }

        private IEnumerator StepArea(ScenarioState st, JObject step, JObject rec)
        {
            // a lab step (a street, a bulldozer) may have left the runner's own tool active: Placemaker's tool picks the area
            if (!_tool.IsActive) yield return OpenTool();
            var b = st.S.Block;
            bool abs = (bool?)step["abs"] ?? false;
            string tool = ((string)step["tool"] ?? "bucket").ToLowerInvariant();
            // a person looks at what they select (and the tool's grab radius follows the camera distance)
            var crec = new JObject();
            float lookFrom = b.Kind == SuiteBlockKind.Plot ? math.max(320f, 1.3f * math.max(b.X1 - b.X0, b.Z1 - b.Z0)) : 320f;
            yield return CameraTo(FocusOf(b), 25f, 55f, (float?)step["cameraDistance"] ?? lookFrom, crec, null);
            rec["areaCamera"] = crec["camera"];
            if (_tool.Stage == ToolStage.Manage) yield return UiGoToStage(rec, ToolStage.Area);
            if (_tool.SuiteHasSelection)
            {
                yield return UiAct(st, rec, "clear area", "clear-area", "clearArea", null, () => !_tool.SuiteHasSelection, () => _tool.ClearArea());
                yield return Frames(2);
            }
            switch (tool)
            {
                case "bucket":
                case "block":
                {
                    yield return UiSetAreaMode(rec, SelectionMode.Block);
                    RaycastHit? hit = null;
                    yield return Press(st, rec, P3(Pt(step["at"], b, abs)), false, h => hit = h);
                    if (!hit.HasValue) { st.Result["status"] = "no-outline"; yield break; }
                    // (area overhaul) Shift+clicks add more fills: "shift": [[dx, dz], ...]
                    if (step["shift"] is JArray more)
                        foreach (var p in more)
                        {
                            RaycastHit? h2 = null;
                            yield return Press(st, rec, P3(Pt(p, b, abs)), false, h => h2 = h, shift: true);
                            if (!h2.HasValue) { st.Result["status"] = "no-outline"; yield break; }
                        }
                    break;
                }
                case "points":
                case "draw":
                {
                    yield return UiSetAreaMode(rec, SelectionMode.Draw);
                    var pts = new List<float2>();
                    if (step["points"] is JArray list) foreach (var p in list) pts.Add(Pt(p, b, abs));
                    else
                    {
                        var wh = step["rect"] as JArray;
                        float w = wh != null ? (float)wh[0] : 60f, h = wh != null ? (float)wh[1] : 40f;
                        var c = Pt(step["at"], b, abs);
                        pts.Add(c + new float2(-w / 2, -h / 2)); pts.Add(c + new float2(w / 2, -h / 2)); pts.Add(c + new float2(w / 2, h / 2)); pts.Add(c + new float2(-w / 2, h / 2));
                    }
                    // the first corner again closes the outline ("open": true leaves it open, the corners still being drawn)
                    bool open = (bool?)step["open"] ?? false;
                    var snaps = new JArray();
                    foreach (var p in open ? pts : pts.Concat(new[] { pts[0] }))
                    {
                        RaycastHit? hit = null;
                        yield return Press(st, rec, P3(p), false, h => hit = h);
                        if (!hit.HasValue) { st.Result["status"] = "no-outline"; yield break; }
                        // what the click snapped to (the tool's last snap) and where the corner went
                        var ls = _tool.LastSnap;
                        snaps.Add($"{ls.Kind} ({ls.Position.x:F1},{ls.Position.z:F1}) clicked ({p.x:F1},{p.y:F1})");
                    }
                    rec["snaps"] = snaps;
                    Mod.Log.Info("Placemaker suite: points snapped: " + string.Join(" | ", snaps));
                    rec["clicks"] = pts.Count + (open ? 0 : 1);
                    if (open) { rec["corners"] = _tool.Draw.Count; yield break; }
                    break;
                }
                case "land":
                {
                    // the scenario's own land: its shape (rectangle, circle, pinch, L, points) clicked with the Points tool
                    var plot = st.S.Plot;
                    if (plot == null) { rec["error"] = "area tool 'land' needs a scenario with \"land\""; st.Result["status"] = "no-outline"; yield break; }
                    yield return UiSetAreaMode(rec, SelectionMode.Draw);
                    var pts = plot.Outline();
                    foreach (var p in pts.Concat(new[] { pts[0] }))   // the first corner again closes the outline
                    {
                        RaycastHit? hit = null;
                        yield return Press(st, rec, P3(p), false, h => hit = h);
                        if (!hit.HasValue) { st.Result["status"] = "no-outline"; yield break; }
                    }
                    rec["clicks"] = pts.Length + 1;
                    rec["shape"] = $"{plot.Shape} {plot.Size.x:F0} x {plot.Size.y:F0} m on plot {plot.Name}";
                    break;
                }
                case "paint":
                case "paintanything":
                case "outgrowbrush":
                {
                    // outgrowBrush: Global outgrow's brush (on already, from its window); the panel's area mode is left alone
                    bool outgrow = tool == "outgrowbrush";
                    if (!outgrow) yield return UiSetAreaMode(rec, tool == "paint" ? SelectionMode.BlockPaint : SelectionMode.Paint);
                    float size = (float?)step["size"] ?? math.min(b.X1 - b.X0, b.Z1 - b.Z0) * 0.45f;
                    if (outgrow) _tool.SetOutgrowBrushWidth(size);
                    else _tool.SetBrush((bool?)step["square"] ?? false, size, (float?)step["angle"] ?? 0f);
                    yield return Frames(2);
                    var path = new List<float3>();
                    if (step["stroke"] is JArray stroke) foreach (var p in stroke) path.Add(P3(Pt(p, b, abs)));
                    else
                    {
                        float hw = (b.X1 - b.X0) * 0.25f, hh = (b.Z1 - b.Z0) * 0.2f;
                        foreach (var d in new[] { new float2(-hw, -hh), new float2(hw, -hh), new float2(hw, hh), new float2(-hw, hh) }) path.Add(P3(b.Center + d));
                    }
                    // every point of the stroke must be on the screen, away from the panel
                    yield return ReadPanelRect();
                    foreach (var p in path)
                    {
                        var why = Clickable(p, out _);
                        if (why != null) { rec["error"] = $"stroke point ({p.x:F0},{p.z:F0}) is {why}"; st.Result["status"] = "no-outline"; yield break; }
                    }
                    var from = PlacemakerToolSystem.SuiteCursor ?? (path[0] + new float3(40f, 0f, 0f));
                    PlacemakerToolSystem.SuiteHover(from, path[0], 6);
                    if (st.Pace == "human")
                    {
                        // a person drags about 30 m/s at 60 Hz: a point every 0.5 m, held for the frames a 60 Hz frame lasts here
                        int hold = math.clamp((int)math.round((1f / 60f) / math.max(1e-3f, _lastFrameMs / 1000f)), 1, 6);
                        var pts = new List<float3>();
                        for (int i = 1; i < path.Count; i++)
                        {
                            int n = math.max(1, (int)math.ceil(math.distance(path[i - 1], path[i]) / 0.5f));
                            for (int k = 1; k <= n; k++) for (int r = 0; r < hold; r++) pts.Add(math.lerp(path[i - 1], path[i], k / (float)n));
                        }
                        PlacemakerToolSystem.SuiteQueue(new PlacemakerToolSystem.SuiteFrame { At = path[0], LeftDown = true, LeftHeld = true });
                        foreach (var p in pts) PlacemakerToolSystem.SuiteQueue(new PlacemakerToolSystem.SuiteFrame { At = p, LeftHeld = true });
                        PlacemakerToolSystem.SuiteQueue(new PlacemakerToolSystem.SuiteFrame { At = path[path.Count - 1] });
                        rec["strokeFrames"] = pts.Count + 2;
                    }
                    else
                    {
                        int per = (int)math.ceil(math.max(1f, path.Count > 1 ? math.distance(path[0], path[1]) / math.max(2f, size * 0.25f) : 1f));
                        PlacemakerToolSystem.SuiteDrag(path.ToArray(), per);
                    }
                    rec["brush"] = size;
                    yield return Feed(rec, 60f);
                    // Outgrow's brush picks no area (the buildings it touched are outgrown): nothing more to wait for
                    if (outgrow) { rec["brushed"] = _tool.LastStamp.Lots; yield break; }
                    break;
                }
                case "nodes":
                case "perimeter":
                {
                    // Area overhaul: Road nodes is part of Points — the same clicks on the junctions, the corners snap to the nodes.
                    yield return UiSetAreaMode(rec, SelectionMode.Draw);
                    var targets = new List<float2>();
                    if (step["nodes"] is JArray list) foreach (var p in list) targets.Add(Pt(p, b, abs));
                    else { targets.Add(new float2(b.X0, b.Z0)); targets.Add(new float2(b.X1, b.Z0)); targets.Add(new float2(b.X1, b.Z1)); targets.Add(new float2(b.X0, b.Z1)); }
                    var nodes = new JArray();
                    float3 firstAt = default;
                    bool first = true;
                    foreach (var p in targets)
                    {
                        var n = NearestRoadNode(p, out var at);
                        if (n == Entity.Null) { rec["error"] = $"no road node near ({p.x:F0},{p.y:F0})"; st.Result["status"] = "no-outline"; yield break; }
                        if (first) { firstAt = at; first = false; }
                        RaycastHit? hit = null;
                        yield return Press(st, rec, at, false, h => hit = h);
                        if (!hit.HasValue) { st.Result["status"] = "no-outline"; yield break; }
                        nodes.Add(new JArray(Math.Round(at.x), Math.Round(at.z)));
                    }
                    RaycastHit? last = null;
                    yield return Press(st, rec, firstAt, false, h => last = h);   // back to the first node: closes the loop
                    rec["nodes"] = nodes;
                    break;
                }
                default:
                    rec["error"] = "unknown area tool '" + tool + "' (bucket, points, land, paint, paintAnything, nodes)";
                    st.Result["status"] = "no-outline";
                    yield break;
            }
            // Paint area ends in the Area stage: a person presses Next
            yield return WaitFor(rec, "outline", () => _tool.Shape != null && !_tool.Painting, 20f);
            if (_tool.Shape != null && _tool.Stage == ToolStage.Area && _tool.SuitePrimaryInfo().Kind == "toDesign")
                yield return UiPrimary(st, rec, "Next");
            if (_tool.Shape == null)
            {
                rec["error"] = "no outline" + (_tool.FlashText != null ? ": " + _tool.FlashText.Key : "");
                st.Result["status"] = "no-outline";
                yield break;
            }
            st.Ring = _tool.Shape.Ring2D();
            rec["mode"] = _tool.Mode.ToString();
            float area = math.abs(Placemaker.Core.Geometry.Poly.SignedArea(st.Ring));
            rec["areaM2"] = Math.Round(area);
            rec["corners"] = st.Ring.Length;
            st.Result["areaM2"] = rec["areaM2"];
            yield return WaitPlan(st, rec);
        }

        /// <summary>The road junction nearest a point (Road layer nodes only — what the Road-nodes raycast can hit).</summary>
        private Entity NearestRoadNode(float2 p, out float3 at)
        {
            at = default;
            var em = _world.EntityManager;
            var best = Entity.Null;
            float bestD = 20f;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Net.Node>(), ComponentType.ReadOnly<global::Game.Net.ConnectedEdge>(),
                ComponentType.Exclude<global::Game.Common.Deleted>(), ComponentType.Exclude<global::Game.Tools.Temp>(), ComponentType.Exclude<global::Game.Common.Owner>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (!_lookup.CityNet(e)) continue;
                    var pos = em.GetComponentData<global::Game.Net.Node>(e).m_Position;
                    float d = math.distance(pos.xz, p);
                    if (d < bestD) { bestD = d; best = e; at = pos; }
                }
            return best;
        }

        // ───────────── entrances ─────────────

        /// <summary>Where an entrance goes: a side of the block (t along it, west→east / south→north), a point, or the south street.</summary>
        private float2 EntranceTarget(JObject step, SuiteBlock b)
        {
            bool abs = (bool?)step["abs"] ?? false;
            if (step["at"] is JArray) return Pt(step["at"], b, abs);
            float t = (float?)step["t"] ?? 0.5f;
            switch (((string)step["side"] ?? "south").ToLowerInvariant())
            {
                case "north": return new float2(math.lerp(b.X0, b.X1, t), b.Z1);
                case "east": return new float2(b.X1, math.lerp(b.Z0, b.Z1, t));
                case "west": return new float2(b.X0, math.lerp(b.Z0, b.Z1, t));
                default: return new float2(math.lerp(b.X0, b.X1, t), b.Z0);
            }
        }

        private float3 OnOutline(float2 p)
        {
            var ring = _tool.Shape.Ring;
            if (!Composer.ClosestSegment(ring, p, out int seg, out float t, out _)) return P3(p);
            var at = math.lerp(ring[seg], ring[(seg + 1) % ring.Length], t);
            return new float3(at.x, _fixture.H(at.x, at.z), at.z);
        }

        /// <summary>Entrances live in Design: a person in Review goes back to Design first (recorded).</summary>
        private IEnumerator EnsureDesign(ScenarioState st, JObject rec, string control)
        {
            if (_tool.Stage == ToolStage.Design) yield break;
            if (_tool.Stage == ToolStage.Review || _tool.Stage == ToolStage.Area)
            {
                var implicitSteps = rec["implicit"] as JArray ?? (JArray)(rec["implicit"] = new JArray());
                implicitSteps.Add($"goToStage design ({control} is shown in Design, the tool was in {_tool.Stage})");
                yield return UiGoToStage(rec, ToolStage.Design);
                yield return WaitPlan(st, rec);
            }
            if (_tool.Stage != ToolStage.Design) rec["error"] = $"{control} is not shown in {_tool.Stage} and Design is not reachable";
        }

        /// <summary>Starts marker placement the way the panel does: an explicit step/kind, else the main button's request.</summary>
        private IEnumerator StartPlacement(ScenarioState st, JObject step, JObject rec)
        {
            string path = (string)step["path"], kind = (string)step["kind"];
            if (path != null)
            {
                yield return UiAct(st, rec, "marker kind", null, "setMarkerKind", new object[] { path, kind ?? "entrance" }, () => _tool.MarkerKind != null, () => _tool.SetMarkerKind(path, kind ?? "entrance"));
                rec["via"] = "panel";
                yield break;
            }
            var req = _tool.SuiteMarkerRequest();
            if (req.Path != null)
            {
                // "Add at least one entrance": pressing the main button starts placement
                yield return UiPrimary(st, rec, "Add an entrance");
                rec["via"] = "button";
                yield break;
            }
            if (kind != null || _tool.SuiteMarkers().Count > 0)
            {
                var last = _tool.SuiteMarkers().LastOrDefault();
                string p = last.Path ?? "0", k = kind ?? MarkerVariants.BaseKind(last.Kind) ?? "entrance";
                yield return UiAct(st, rec, "marker kind", "marker-kind-" + k, "setMarkerKind", new object[] { p, k }, () => _tool.MarkerKind != null, () => _tool.SetMarkerKind(p, k));
                rec["via"] = "panel";
            }
        }

        private IEnumerator StepEntrance(ScenarioState st, JObject step, JObject rec)
        {
            if (_tool.Shape == null) { rec["error"] = "no outline"; yield break; }
            yield return EnsureDesign(st, rec, "Entrances");
            if (rec["error"] != null) yield break;
            yield return StartPlacement(st, step, rec);
            if (_tool.MarkerKind == null) { rec["error"] = "entrance placement did not start (no step asks for entrances; give path/kind)"; yield break; }
            yield return Frames(2);
            rec["path"] = _tool.MarkerPath;
            rec["kind"] = _tool.MarkerKind;
            // "crosswalk": true/false presses the panel's Crosswalk switch first when it is the other way (absent: as it is)
            if (step["crosswalk"] != null && (bool)step["crosswalk"] != _tool.MarkerCrosswalk)
            {
                bool on = (bool)step["crosswalk"];
                string mp = _tool.MarkerPath;
                yield return UiAct(st, rec, "crosswalk", "marker-crosswalk", "setMarkerCrosswalk", new object[] { mp, -1.0, on }, () => _tool.MarkerCrosswalk == on, () => _tool.SetMarkerCrosswalk(mp, -1, on));
            }
            rec["crosswalk"] = _tool.MarkerCrosswalk;
            int before = _tool.SuiteMarkers().Count;
            var target = OnOutline(EntranceTarget(step, st.S.Block));
            RaycastHit? hit = null;
            yield return Press(st, rec, target, false, h => hit = h);
            if (!hit.HasValue) { yield return KeyPress("esc"); yield break; }
            yield return KeyPress("esc");   // Esc: done placing
            var markers = _tool.SuiteMarkers();
            rec["at"] = new JArray(Math.Round(target.x, 1), Math.Round(target.z, 1));
            if (markers.Count <= before) { rec["error"] = "the click placed no entrance" + (_tool.FlashText != null ? ": " + _tool.FlashText.Key : ""); yield break; }
            // the tool's own marker (snapped to the outline), not the scripted point
            var placed = markers.OrderBy(m => math.distance(m.At.xz, target.xz)).First();
            rec["placed"] = new JArray(Math.Round(placed.At.x, 1), Math.Round(placed.At.z, 1));
            rec["placedKind"] = placed.Kind;
            st.Markers.Add((placed.At, placed.Kind, placed.Path));
            yield return WaitPlan(st, rec);
        }

        /// <summary>Default scripts: while the main button asks for entrances, add one on the south street side (then east, north, west).</summary>
        private IEnumerator StepEntrancesAuto(ScenarioState st, JObject step, JObject rec)
        {
            var placed = new JArray();
            string[] sides = { "south", "east", "north", "west" };
            // (rf-beach) only sides with a street when the block has one: land by a lake has no street on its water side
            var b = st.S.Block;
            if (b != null)
            {
                var streets = sides.Where(x => !string.IsNullOrEmpty(x == "south" ? b.South : x == "east" ? b.East : x == "north" ? b.North : b.West)).ToArray();
                if (streets.Length > 0) sides = streets;
            }
            for (int k = 0; k < sides.Length && _tool.SuiteMarkerRequest().Path != null; k++)
            {
                var sub = new JObject { ["side"] = sides[k] };
                var r = new JObject();
                yield return StepEntrance(st, sub, r);
                placed.Add(r);
                if (r["error"] != null) { rec["error"] = r["error"]; break; }
            }
            rec["placed"] = placed;
            rec["asked"] = placed.Count;
        }

        /// <summary>
        /// Move = the UI's way: the placed entrance's remove button in the panel, then in placement mode a left-click on the new
        /// spot (a right-click only takes back the last one placed). Markers are matched by position.
        /// </summary>
        private IEnumerator StepMoveEntrance(ScenarioState st, JObject step, JObject rec, bool removeOnly)
        {
            yield return EnsureDesign(st, rec, "Entrances");
            if (rec["error"] != null) yield break;
            var markers = _tool.SuiteMarkers();
            int index = (int?)step["index"] ?? 0;
            (float3 At, string Kind, string Path) m;
            if (index >= 0 && index < st.Markers.Count)
            {
                // the script's entrance n: the design's marker nearest where it was placed
                var want = st.Markers[index].At;
                if (markers.Count == 0) { rec["error"] = "the design has no entrances"; yield break; }
                m = markers.OrderBy(x => math.distance(x.At.xz, want.xz)).First();
                float d = math.distance(m.At.xz, want.xz);
                rec["matchedM"] = Math.Round(d, 2);
                if (d > 2f) { rec["error"] = $"entrance {index} of the script is not in the design any more (nearest marker {d:F1} m away)"; yield break; }
            }
            else if (index >= 0 && index < markers.Count) m = markers[index];
            else { rec["error"] = $"no entrance {index} (script placed {st.Markers.Count}, design has {markers.Count})"; yield break; }
            int count0 = markers.Count;
            string mk = MarkerVariants.BaseKind(m.Kind);
            // its index in its step's list (StepView.markerList: the panel's rows and their remove buttons)
            int li = markers.IndexOf(m), idx = markers.Take(li).Count(x => x.Path == m.Path);
            yield return UiAct(st, rec, "remove entrance", "marker-remove-" + idx, "removeMarker", new object[] { m.Path, (double)idx }, () => _tool.SuiteMarkers().Count < count0, () => _tool.RemoveMarker(m.Path, idx));
            if (rec["error"] != null) yield break;
            float3 to = default;
            if (!removeOnly)
            {
                yield return UiAct(st, rec, "marker kind", "marker-kind-" + mk, "setMarkerKind", new object[] { m.Path, mk }, () => _tool.MarkerKind != null, () => _tool.SetMarkerKind(m.Path, mk));
                yield return Frames(2);
                to = OnOutline(EntranceTarget(step, st.S.Block));
                RaycastHit? hit2 = null;
                yield return Press(st, rec, to, false, h => hit2 = h);
                if (!hit2.HasValue) { yield return KeyPress("esc"); yield break; }
                yield return KeyPress("esc");
            }
            var after = _tool.SuiteMarkers();
            rec["from"] = new JArray(Math.Round(m.At.x, 1), Math.Round(m.At.z, 1));
            if (!removeOnly) rec["to"] = new JArray(Math.Round(to.x, 1), Math.Round(to.z, 1));
            rec["markers"] = after.Count;
            // the effect: nothing left at the old spot; the count unchanged (move) or one lower (remove)
            bool oldGone = !after.Any(x => math.distance(x.At.xz, m.At.xz) < 1.5f) || (!removeOnly && math.distance(to.xz, m.At.xz) < 1.5f);
            int expect = removeOnly ? count0 - 1 : count0;
            if (!oldGone) rec["error"] = "the entrance is still at its old spot";
            else if (after.Count != expect) rec["error"] = $"expected {expect} entrances after the {(removeOnly ? "removal" : "move")}, the design has {after.Count}";
            int si = st.Markers.FindIndex(x => math.distance(x.At.xz, m.At.xz) < 2f);
            if (si >= 0)
            {
                if (removeOnly) st.Markers.RemoveAt(si);
                else
                {
                    var moved = after.OrderBy(x => math.distance(x.At.xz, to.xz)).FirstOrDefault();
                    st.Markers[si] = (moved.Path != null ? moved.At : to, m.Kind, m.Path);
                }
            }
            yield return WaitPlan(st, rec);
        }

        // ───────────── keys ─────────────

        /// <summary>A key through the tool's own key handling (Enter = the main button, Esc = the Esc chain, undo/redo).</summary>
        private IEnumerator KeyPress(string key)
        {
            PlacemakerToolSystem.SuiteKey(key);
            float until = Time.realtimeSinceStartup + 5f;
            while (PlacemakerToolSystem.SuiteKeyPending && Time.realtimeSinceStartup < until) yield return null;
            yield return Frames(2);
        }

        /// <summary>What a right-click can take back, for the record: stage, an area or not, placing or not, corners, fills, road nodes, entrances.</summary>
        private JObject RightClickState() => new JObject
        {
            ["stage"] = _tool.Stage.ToString(),
            ["area"] = _tool.SuiteHasSelection,
            ["placing"] = _tool.MarkerKind ?? "",
            ["corners"] = _tool.Draw.Count,
            ["closed"] = _tool.Draw.Closed,
            ["fills"] = _tool.Blocks.Rings.Count,
            ["roadNodes"] = _tool.Perimeter.Nodes.Count,
            ["entrances"] = _tool.SuiteMarkers().Count,
        };

        /// <summary>
        /// A right-click on the world in whatever the tool is doing (at: a point, default the block's centre; placing: an
        /// entrance type is pressed first, as for "entrance"). A scripted right-click also presses the game's Cancel, as the
        /// real mouse does. rec: the state before and after.
        /// </summary>
        private IEnumerator StepRightClick(ScenarioState st, JObject step, JObject rec)
        {
            var b = st.S.Block;
            bool abs = (bool?)step["abs"] ?? false;
            if ((bool?)step["placing"] ?? false)
            {
                yield return StartPlacement(st, step, rec);
                if (_tool.MarkerKind == null) { rec["error"] = "entrance placement did not start"; yield break; }
                yield return Frames(2);
            }
            rec["before"] = RightClickState();
            RaycastHit? hit = null;
            yield return Press(st, rec, P3(Pt(step["at"], b, abs)), true, h => hit = h);
            yield return Frames(3);
            rec["after"] = RightClickState();
        }

        private IEnumerator StepKey(ScenarioState st, JObject step, JObject rec)
        {
            string key = ((string)step["key"] ?? "").ToLowerInvariant();
            if (key != "enter" && key != "esc" && key != "undo" && key != "redo") { rec["error"] = "key must be enter, esc, undo or redo"; yield break; }
            var before = (_tool.Stage, _tool.ViewVersion);
            yield return KeyPress(key);
            rec["stage"] = _tool.Stage.ToString();
            if (PlacemakerToolSystem.SuiteKeyPending) { rec["error"] = "the key was never handled (the tool was not running)"; yield break; }
            if (_tool.ViewVersion == before.ViewVersion && _tool.Stage == before.Stage) rec["note"] = "the key changed nothing";
            yield return WaitPlan(st, rec);
        }
    }
}
