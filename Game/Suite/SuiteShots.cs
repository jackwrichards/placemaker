// Game runner: the camera and settled screenshots.
// Camera: the game camera is aimed at a target, waits until it holds still, then the angle and distance actually
// rendered are measured (the game lifts the eye over terrain and clamps tilt) and corrected (up to 3 passes); what
// was rendered is recorded next to what was asked. A target that does not exist is a step error and no picture.
// Shots go through the settle gate:
//   1. a wall-clock floor since the last event (camera move, ghost, build, delete, restore, grow) — blends in CS2 are
//      timed in seconds, not frames;
//   2. the engine quiet for >= 10 frames and >= 150 ms: camera still, no mesh loading, full level of detail, virtual
//      texturing idle (no busy requests, no tile counter moving, init queues empty), no fading batches, temporaries
//      stable and no ghost pending;
//   3. pixel proof: three captures >= 250 ms and >= 15 frames apart; both pairs differ by less than the noise floor
//      calibrated at the start of the run (changed pixels and the worst 32x32 tile, full resolution).
// A shot that does not settle within the timeout is saved anyway and its caption says so.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Rendering;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private float3 FocusOf(SuiteBlock b) => new float3(b.Center.x, _fixture.H(b.Center.x, b.Center.y), b.Center.y);

        // ───────────── camera ─────────────

        /// <summary>
        /// Frames the camera and waits until it arrived (pivot held, rendered camera still for 5 frames), then measures the
        /// rendered angle and distance and corrects the request until they match (pitch within 1°, distance within 3%).
        /// fov: a vertical field of view for this framing (null = the game's own).
        /// </summary>
        private IEnumerator CameraTo(float3 pivot, float yaw, float pitch, float distance, JObject rec, float? fov)
        {
            float t0 = Time.realtimeSinceStartup;
            SuiteCapture.Fov(fov);
            float askPitch = pitch, askDist = distance;
            bool arrived = false;
            int passes = 0;
            (float Pitch, float Yaw, float Distance, float Fov, float Aspect) eff = default;
            for (; passes < 4; passes++)
            {
                SuiteCapture.Aim(pivot, yaw, askPitch, askDist);
                Event("camera");
                arrived = false;
                Vector3 lastP = Vector3.positiveInfinity;
                Quaternion lastR = Quaternion.identity;
                int still = 0, frames = 0;
                float until = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    frames++;
                    var cam = Camera.main;
                    if (cam == null) continue;
                    var tr = cam.transform;
                    bool moved = (tr.position - lastP).sqrMagnitude > 0.0004f || Quaternion.Angle(tr.rotation, lastR) > 0.02f;
                    lastP = tr.position;
                    lastR = tr.rotation;
                    still = moved ? 0 : still + 1;
                    if (frames >= 3 && still >= 5 && SuiteCapture.PivotNear(pivot, 1f)) { arrived = true; break; }
                }
                eff = SuiteCapture.Effective(pivot);
                if (!arrived || float.IsNaN(eff.Pitch)) break;
                float dp = pitch - eff.Pitch, dd = distance / math.max(1f, eff.Distance);
                if (math.abs(dp) <= 1f && math.abs(1f - dd) <= 0.03f) break;
                askPitch = math.clamp(askPitch + dp, 1f, 89.9f);
                askDist = math.max(5f, askDist * dd);
            }
            Waited(rec, "camera", Time.realtimeSinceStartup - t0, arrived ? 0f : 20f);
            var c = rec["camera"] as JObject ?? (JObject)(rec["camera"] = new JObject());
            c["pivot"] = new JArray(Math.Round(pivot.x, 1), Math.Round(pivot.y, 1), Math.Round(pivot.z, 1));
            c["asked"] = new JObject { ["yaw"] = yaw, ["pitch"] = pitch, ["distance"] = Math.Round(distance, 1), ["fov"] = fov.HasValue ? (JToken)fov.Value : "game" };
            c["effective"] = new JObject
            {
                ["yaw"] = Math.Round(eff.Yaw, 1), ["pitch"] = Math.Round(eff.Pitch, 1), ["distance"] = Math.Round(eff.Distance, 1),
                ["fov"] = Math.Round(eff.Fov, 1), ["aspect"] = Math.Round(eff.Aspect, 3), ["passes"] = passes + 1,
            };
            if (!float.IsNaN(eff.Pitch) && (math.abs(eff.Pitch - pitch) > 1.5f || math.abs(1f - distance / math.max(1f, eff.Distance)) > 0.05f)) c["clamped"] = true;
            if (!float.IsNaN(eff.Distance) && eff.Fov > 0f)
                c["metresPerPixel"] = Math.Round(2f * eff.Distance * math.tan(math.radians(eff.Fov) * 0.5f) / math.max(1, Screen.height), 3);
        }

        /// <summary>Resolves a camera request: target and view. Returns false (rec error, no move) when the target does not exist.</summary>
        private IEnumerator CameraStep(ScenarioState st, JObject c, JObject rec, Action<bool> ok, int index = -1)
        {
            var b = st.S.Block;
            string target = ((string)c["target"] ?? "block").ToLowerInvariant();
            float3 pivot = FocusOf(b);
            float radius = 85f;
            string err = null;
            int i = index >= 0 ? index : (c["index"] != null && c["index"].Type == JTokenType.Integer ? (int)c["index"] : 0);
            switch (target)
            {
                case "block":
                    if (b.Kind == SuiteBlockKind.Plot) radius = math.max(radius, 0.5f * math.max(b.X1 - b.X0, b.Z1 - b.Z0));
                    break;
                case "area":
                case "build":
                    if (st.Ring != null && st.Ring.Length > 0)
                    {
                        var (lo, hi) = Bounds(st.Ring, 0f);
                        pivot = P3((lo + hi) * 0.5f);
                        radius = math.max(25f, math.length(hi - lo) * 0.5f);
                    }
                    else err = "no outline to frame";
                    break;
                case "entrance":
                {
                    var markers = st.Markers.Count > 0 ? st.Markers.Select(x => x.At).ToList() : _tool.SuiteMarkers().Select(x => x.At).ToList();
                    if (i < 0 || i >= markers.Count) { err = $"no entrance {i} (have {markers.Count})"; break; }
                    pivot = markers[i];
                    radius = 15f;
                    break;
                }
                case "issue":
                {
                    var at = (_tool.Plan?.Issues.Concat(_tool.Plan.Layers.SelectMany(l => l.Issues)) ?? Enumerable.Empty<PlanIssue>()).Where(x => x.At.HasValue).Select(x => x.At.Value).ToList();
                    if (i < 0 || i >= at.Count) { err = $"no issue marker {i} (have {at.Count})"; break; }
                    pivot = at[i];
                    radius = 15f;
                    break;
                }
                case "point":
                    pivot = P3(Pt(c["at"], b, (bool?)c["abs"] ?? false));
                    radius = 20f;
                    break;
                case "joint":
                {
                    // (p2-beaches) where a deck (pier, jetty, boardwalk, footbridge) meets a ground path: a close-up of each
                    var joints = DeckJoints();
                    if (i < 0 || i >= joints.Count) { err = $"no deck joint {i} (have {joints.Count})"; break; }
                    pivot = joints[i];
                    radius = 7f;
                    break;
                }
                case "overview":
                    pivot = new float3((_fixture.Layout.LineX(0) + _fixture.Layout.LineX(_fixture.Layout.NX)) * 0.5f, _fixture.Layout.Base, (_fixture.Layout.LineZ(0) + _fixture.Layout.LineZ(_fixture.Layout.NZ)) * 0.5f);
                    radius = 600f;
                    break;
                default:
                    err = $"unknown camera target '{target}' (block, area, build, entrance, issue, point, joint, overview)";
                    break;
            }
            string view = ((string)c["view"] ?? (target == "entrance" || target == "issue" || target == "point" ? "closeup" : "oblique")).ToLowerInvariant();
            float yaw = 25f, pitch = 60f, dist = radius * 3.4f;
            float? fov = null;
            switch (view)
            {
                case "oblique": break;
                case "top": yaw = 0f; pitch = 89.5f; fov = 18f; dist = 1.15f * radius / math.tan(math.radians(9f)); break;
                case "closeup": yaw = 30f; pitch = 38f; dist = math.clamp(radius * 3f, 45f, 60f); break;
                case "low": yaw = 25f; pitch = 20f; dist = math.max(30f, radius * 2.2f); break;
                default: err = err ?? $"unknown view '{view}' (oblique, top, closeup, low)"; break;
            }
            if (c["yaw"] != null) yaw = (float)c["yaw"];
            if (c["pitch"] != null) pitch = (float)c["pitch"];
            if (c["fov"] != null) fov = (float)c["fov"];
            // the build fills the picture: first guess from the geometry, then framed on what the camera really shows
            bool fit = (target == "build" || target == "area") && (view == "oblique" || view == "low" || view == "top") && st.Ring != null && st.Ring.Length > 0 && c["distance"] == null;
            if (fit) dist = FitDistance(st.Ring, pivot, yaw, pitch, fov);
            if (c["distance"] != null) dist = (float)c["distance"];
            // (nature makeover) an eye at a ground point and height looking at the target: "eye": [x, z] (block-relative, or
            // absolute with "abs"), "height" metres above the ground there (1.7). Yaw, pitch and distance follow from the two.
            if (c["eye"] is JArray)
            {
                var e2 = Pt(c["eye"], b, (bool?)c["abs"] ?? false);
                float eh = _fixture.H(e2.x, e2.y) + ((float?)c["height"] ?? 1.7f);
                var d = pivot - new float3(e2.x, eh, e2.y);
                float horiz = math.max(0.1f, math.length(d.xz));
                yaw = math.degrees(math.atan2(d.x, d.z));
                pitch = math.clamp(math.degrees(math.atan2(-d.y, horiz)), 1f, 89.9f);
                dist = math.length(d);
            }
            // (nature makeover) "clearView": true turns the camera round its target to the direction whose line of sight passes
            // the fewest trees and bushes of the plan (a close or low look into a wood otherwise sees only the nearest crowns);
            // the requested yaw breaks ties. "height" (with "distance") sets a low eye: the pitch follows.
            if ((bool?)c["clearView"] == true)
            {
                if (c["height"] != null && c["eye"] == null)
                    pitch = math.clamp(math.degrees(math.atan2((float)c["height"], math.max(1f, dist))), 1f, 89.9f);
                var obs = new List<float2>(st.Sightblock);
                var plan = obs.Count > 0 ? null : _tool.Plan;
                if (plan?.Layers != null)
                    foreach (var l in plan.Layers) foreach (var o in l.Objects)
                        if (o != null && (o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush)) obs.Add(o.Position.xz);
                float bestScore = float.MaxValue, bestYaw = yaw;
                for (int k = 0; k < 24; k++)
                {
                    float y = yaw + k * 15f;
                    float yr = math.radians(y), pr = math.radians(pitch);
                    var f = new float3(math.sin(yr) * math.cos(pr), -math.sin(pr), math.cos(yr) * math.cos(pr));
                    var eye = pivot - f * dist;
                    float score = 0f;
                    var a = eye.xz; var ab = pivot.xz - a; float L2 = math.max(1e-3f, math.lengthsq(ab));
                    foreach (var q in obs)
                    {
                        float t = math.saturate(math.dot(q - a, ab) / L2);
                        if (t > 0.97f) continue;   // things at the target itself are what we look at
                        float dd = math.distance(q, a + ab * t);
                        // the line is high above the ground near the eye on a steep look: crowns only block when the line is low
                        float hLine = math.lerp(eye.y, pivot.y, t) - _fixture.H(q.x, q.y);
                        if (dd < 3.5f && hLine < 18f) score += 1f - dd / 3.5f;
                    }
                    score += math.abs(((k * 15f + 180f) % 360f) - 180f) / 720f;
                    if (score < bestScore) { bestScore = score; bestYaw = y; }
                }
                yaw = bestYaw;
                (rec["camera"] as JObject ?? (JObject)(rec["camera"] = new JObject()))["clearView"] = Math.Round(bestScore, 2);
            }
            var cr = rec["camera"] as JObject ?? (JObject)(rec["camera"] = new JObject());
            cr["target"] = target;
            cr["index"] = i;
            cr["view"] = view;
            if (err != null) { cr["error"] = err; rec["error"] = "camera: " + err; ok(false); yield break; }
            yield return CameraTo(pivot, yaw, pitch, dist, rec, fov);
            if (fit)
            {
                // where the outline (and 12 m above it) lands on the screen: re-centre it and come closer until it fills
                // the picture (the game aims a little above its pivot, and the near side looks bigger than the far side)
                int passes = 0;
                for (; passes < 4; passes++)
                {
                    var box = ScreenBox(st.Ring);
                    if (box == null) break;
                    var (x0, y0, x1, y1) = box.Value;
                    float cx = (x0 + x1) * 0.5f - 0.5f, cy = (y0 + y1) * 0.5f - 0.5f, now = math.max(x1 - x0, y1 - y0);
                    cr["filled"] = Math.Round(now, 2);
                    if (math.abs(cx) < 0.02f && math.abs(cy) < 0.02f && math.abs(now - FrameFill) < 0.03f) break;
                    var eff = SuiteCapture.Effective(pivot);
                    float d = float.IsNaN(eff.Distance) ? dist : eff.Distance;
                    float ty = math.tan(math.radians(float.IsNaN(eff.Fov) ? 32f : eff.Fov) * 0.5f), tx = ty * (_cfg.shotWidth / (float)math.max(1, _cfg.shotHeight));
                    float yr = math.radians(yaw), pr = math.radians(float.IsNaN(eff.Pitch) ? pitch : eff.Pitch);
                    var ahead = new float3(math.sin(yr), 0f, math.cos(yr));
                    var right = new float3(math.cos(yr), 0f, -math.sin(yr));
                    pivot += ahead * (cy * 2f * d * ty / math.max(0.2f, math.sin(pr))) + right * (cx * 2f * d * tx);
                    pivot.y = _fixture.H(pivot.x, pivot.z);
                    dist = math.max(20f, dist * now / FrameFill);
                    yield return CameraTo(pivot, yaw, pitch, dist, rec, fov);
                }
                cr["fitPasses"] = passes;
            }
            ok(true);
        }

        /// <summary>How much of the picture the build fills (the larger of width and height, 1 = edge to edge).</summary>
        private const float FrameFill = 0.9f;

        /// <summary>The outline's corners, on the ground and 12 m above it, in the rendered camera's viewport (0..1); null when behind it.</summary>
        private (float X0, float Y0, float X1, float Y1)? ScreenBox(float2[] ring)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in ring)
                foreach (var lift in new[] { 0f, 12f })
                {
                    var v = cam.WorldToViewportPoint(new Vector3(p.x, _fixture.H(p.x, p.y) + lift, p.y));
                    if (v.z <= 0f) return null;
                    x0 = math.min(x0, v.x); x1 = math.max(x1, v.x);
                    y0 = math.min(y0, v.y); y1 = math.max(y1, v.y);
                }
            return (x0, y0, x1, y1);
        }

        /// <summary>
        /// The camera distance at which every corner of the outline's bounding box, on the ground and 12 m above it, is
        /// inside the picture with a small margin, for a camera at this yaw and pitch looking at the pivot (the camera sits
        /// south of the pivot: yaw 0 looks north).
        /// </summary>
        private float FitDistance(float2[] ring, float3 pivot, float yaw, float pitch, float? fov)
        {
            var (lo, hi) = Bounds(ring, 0f);
            float vfov = fov ?? (Camera.main != null ? Camera.main.fieldOfView : 35f);
            float aspect = _cfg.shotWidth / (float)math.max(1, _cfg.shotHeight);
            float ty = math.tan(math.radians(vfov) * 0.5f), tx = ty * aspect;
            const float fill = 0.92f;
            float y = math.radians(yaw), p = math.radians(pitch);
            var f = new float3(math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p));
            var r = math.normalize(new float3(f.z, 0f, -f.x));
            var u = math.cross(f, r);
            float dist = 30f;
            foreach (var x in new[] { lo.x, hi.x })
                foreach (var z in new[] { lo.y, hi.y })
                    foreach (var lift in new[] { 0f, 12f })
                    {
                        var c = new float3(x, _fixture.H(x, z) + lift, z) - pivot;
                        float ahead = math.dot(c, f);
                        dist = math.max(dist, math.abs(math.dot(c, r)) / (tx * fill) - ahead);
                        dist = math.max(dist, math.abs(math.dot(c, u)) / (ty * fill) - ahead);
                    }
            return dist;
        }

        private IEnumerator StepCameraAndShot(ScenarioState st, JObject step, JObject rec)
        {
            string shot = (string)step["shot"];
            var c = step["camera"] as JObject ?? step;
            if (c["index"] is JValue v && v.Type == JTokenType.String && (string)v == "all")
            {
                // every entrance / issue: one picture each, named <shot>-<n>
                int count = CountTargets(st, (string)c["target"]);
                if (count == 0) { rec["error"] = $"camera: no {(string)c["target"]} to frame"; yield break; }
                var shots = new JArray();
                for (int k = 0; k < count; k++)
                {
                    var r = new JObject();
                    bool okk = false;
                    yield return CameraStep(st, c, r, x => okk = x, k);
                    if (okk && !string.IsNullOrEmpty(shot)) yield return StepShot(st, ShotOptions(step), r, shot + "-" + k);
                    shots.Add(r);
                }
                rec["shots"] = shots;
                yield break;
            }
            bool ok = false;
            yield return CameraStep(st, c, rec, x => ok = x);
            if (ok && !string.IsNullOrEmpty(shot)) yield return StepShot(st, ShotOptions(step), rec, shot);
        }

        /// <summary>The picture options of a step (mode, street names, zone grid), without its camera.</summary>
        private static JObject ShotOptions(JObject step) => new JObject { ["shotMode"] = step["shotMode"], ["labels"] = step["labels"], ["zones"] = step["zones"] };

        private int CountTargets(ScenarioState st, string target)
        {
            switch ((target ?? "").ToLowerInvariant())
            {
                case "entrance": return st.Markers.Count > 0 ? st.Markers.Count : _tool.SuiteMarkers().Count;
                case "issue": return (_tool.Plan?.Issues.Concat(_tool.Plan.Layers.SelectMany(l => l.Issues)) ?? Enumerable.Empty<PlanIssue>()).Count(x => x.At.HasValue);
                case "joint": return DeckJoints().Count;
            }
            return 0;
        }

        /// <summary>
        /// (p2-beaches) The ends of the last plan's decks (nets that do not follow the terrain: piers, jetties, boardwalks, footbridges)
        /// that meet a ground-following path end within 1 m: the joints Phase 1 fixed in BuildService (one node, no overhang), each
        /// framed for its own close-up. Deck-to-deck joints over water are left out.
        /// </summary>
        private List<PlannedNet> _builtNets;

        private List<float3> DeckJoints()
        {
            var nets = _tool.Plan?.Layers.SelectMany(l => l.Nets).ToList() ?? _builtNets ?? new List<PlannedNet>();
            var ground = new List<float3>();
            foreach (var n in nets) if (n.FollowTerrain) { ground.Add(n.A); ground.Add(n.D); }
            var joints = new List<float3>();
            foreach (var n in nets)
            {
                if (n.FollowTerrain) continue;
                foreach (var e in new[] { n.A, n.D })
                {
                    if (!ground.Any(gp => math.distance(gp.xz, e.xz) < 1f)) continue;
                    if (joints.Any(j => math.distance(j.xz, e.xz) < 2f)) continue;
                    joints.Add(new float3(e.x, _fixture.H(e.x, e.z), e.z));
                }
            }
            return joints;
        }

        // ───────────── settle gate ─────────────

        private int _noiseChanged = -1;
        private float _noiseTile = -1f;
        private int ChangedLimit => Math.Max(400, _noiseChanged < 0 ? 400 : 3 * _noiseChanged);
        private float TileLimit => Math.Max(6f, _noiseTile < 0f ? 6f : 2f * _noiseTile);

        private static float Floor(string ev)
        {
            switch (ev)
            {
                case "camera": return 0.35f;
                case "ghost": return 0.35f;
                case "build": return 0.6f;
                case "delete": return 0.6f;
                case "restore": return 0.6f;
                case "grow": return 1.0f;
                case "load": return 3.0f;
            }
            return 0.35f;
        }

        private struct Signals
        {
            public Vector3 CamPos;
            public Quaternion CamRot;
            public int Loading, Fade, Temps;
            public float FrameLod, Lod;
            public long[] Vt;
            public bool Ghost;
        }

        private Signals ReadSignals()
        {
            var s = new Signals();
            var cam = Camera.main;
            if (cam != null) { s.CamPos = cam.transform.position; s.CamRot = cam.transform.rotation; }
            try { s.Loading = _world.GetExistingSystemManaged<BatchMeshSystem>()?.loadingRemaining ?? 0; } catch (Exception) { s.Loading = -1; }
            var rs = _world.GetExistingSystemManaged<RenderingSystem>();
            s.FrameLod = rs?.frameLod ?? 1f;
            s.Lod = rs?.levelOfDetail ?? 1f;
            s.Vt = SuiteCapture.Streaming(_world);
            try
            {
                var pcs = _world.GetExistingSystemManaged<PreCullingSystem>();
                var fc = pcs != null ? pcs.GetFadeContainer() : Entity.Null;
                s.Fade = fc != Entity.Null && _world.EntityManager.HasBuffer<FadeBatch>(fc) ? _world.EntityManager.GetBuffer<FadeBatch>(fc, true).Length : 0;
            }
            catch (Exception) { s.Fade = -1; }
            s.Temps = TempCount();
            s.Ghost = _tool != null && _tool.GhostPending;
            return s;
        }

        private static bool Quiet(Signals a, Signals b, out string why)
        {
            why = null;
            if ((a.CamPos - b.CamPos).sqrMagnitude > 0.0001f || Quaternion.Angle(a.CamRot, b.CamRot) > 0.01f) why = "camera moving";
            else if (b.Loading > 0) why = "meshes loading";
            else if (b.FrameLod < b.Lod - 1e-4f) why = "level of detail ramping";
            else if (b.Vt != null && (b.Vt[0] > 0 || b.Vt[1] > 0 || b.Vt[2] > 0)) why = "textures streaming";
            else if (b.Vt != null && a.Vt != null && (a.Vt[3] != b.Vt[3] || a.Vt[4] != b.Vt[4] || a.Vt[5] != b.Vt[5] || a.Vt[6] != b.Vt[6])) why = "texture tiles loading";
            else if (b.Fade > 0) why = "objects fading";
            else if (b.Temps != a.Temps || b.Ghost) why = "ghost changing";
            return why == null;
        }

        private JObject SignalsJson(Signals s)
        {
            var o = new JObject { ["meshesLoading"] = s.Loading, ["frameLod"] = Math.Round(s.FrameLod, 3), ["levelOfDetail"] = Math.Round(s.Lod, 3), ["fadingBatches"] = s.Fade, ["temps"] = s.Temps, ["ghostPending"] = s.Ghost };
            if (s.Vt != null) { var vt = new JObject(); for (int i = 0; i < s.Vt.Length; i++) vt[SuiteCapture.StreamingNames[i]] = s.Vt[i]; o["vt"] = vt; }
            return o;
        }

        private Texture2D _capture;
        private string _captureError;

        private IEnumerator CaptureOnce(string mode)
        {
            _capture = null;
            yield return new WaitForEndOfFrame();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (mode == "ui") _capture = SuiteCapture.GrabTexture2D(out _captureError);
            else _capture = SuiteCapture.RenderTexture2D(_cfg.shotWidth, _cfg.shotHeight, mode == "overlay", out _captureError);
            _suiteMsThisFrame += (float)sw.Elapsed.TotalMilliseconds;
        }

        private string ShotFile(ScenarioState st, string name)
        {
            string baseName = $"{st.FileIndex + 1:D2}-{Safe(st.S.Id)}.{st.StepIndex:D2}-{Safe(name)}";
            string file = baseName + ".png";
            for (int k = 2; File.Exists(Path.Combine(_shots, file)); k++) file = baseName + "-" + k + ".png";
            return file;
        }

        /// <summary>A settled screenshot (see the file header). The record gets the file, mode, settle phases, pixel pairs and signals.</summary>
        private IEnumerator StepShot(ScenarioState st, JObject step, JObject rec, string name)
        {
            if (step["camera"] is JObject cam)
            {
                bool ok = false;
                yield return CameraStep(st, cam, rec, x => ok = x);
                if (!ok) yield break;   // a missing target is an error, and there is no picture of something else
            }
            string mode = ((string)step["shotMode"] ?? (string)step["mode"] ?? "").ToLowerInvariant();
            if (mode == "grab") mode = "overlay";
            if (mode != "render" && mode != "overlay" && mode != "ui") mode = _tool.IsActive && _tool.Stage != ToolStage.Manage ? "overlay" : "render";
            // the person moved the mouse off the map to look — except after a hover step, whose picture is of what the cursor shows
            if (mode != "ui" && !((bool?)step["keepCursor"] ?? false)) PlacemakerToolSystem.SuiteCursorAway();
            bool labels = (bool?)step["labels"] ?? false;
            if (labels) SuiteCapture.Labels(true);
            // zones: the game's zone grid is drawn only while the active tool asks for it; the runner's own tool does, for this picture
            bool zones = (bool?)step["zones"] ?? false;
            global::Game.Tools.ToolBaseSystem toolBefore = null;
            if (zones)
            {
                if (mode == "render") mode = "overlay";   // the clean capture hides overlays, and the zone grid is one (no outline: our tool draws none)
                var ft = FixtureTool;
                toolBefore = ft.Tools.activeTool;
                ft.ShowZones = true;
                int u0 = ft.Updates;
                ft.Tools.activeTool = ft;
                yield return Until(() => ft.Tools.activeTool == ft && ft.Updates > u0, 5f, "zone grid");
                yield return Frames(20);   // culling and batches pick the zone blocks up
                Event("camera");
            }
            if (!_look.WindowOk) rec["windowMismatch"] = $"{Screen.width}x{Screen.height} (shots are {_cfg.shotWidth}x{_cfg.shotHeight})";
            string file = ShotFile(st, name);
            string path = Path.Combine(_shots, file);
            float t0 = Time.realtimeSinceStartup;
            float timeout = _cfg.settleTimeout;
            var settle = new JObject { ["event"] = _event, ["msSinceEvent"] = Math.Round((t0 - _eventAt) * 1000f) };

            // 1. wall-clock floor since the last event
            float floor = Floor(_event);
            while (Time.realtimeSinceStartup - _eventAt < floor && Time.realtimeSinceStartup - t0 < timeout) yield return null;
            settle["floorMs"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);

            // 1b. the build's code-made grime is generated on worker threads after the build: wait for it (its own, longer limit)
            var grime = global::Placemaker.Game.Decals.DynamicDecalSystem.Instance;
            if (grime != null && grime.Pending > 0)
            {
                float tg = Time.realtimeSinceStartup;
                while (grime.Pending > 0 && Time.realtimeSinceStartup - tg < 90f) yield return null;
                settle["grimeMs"] = Math.Round((Time.realtimeSinceStartup - tg) * 1000f);
                if (grime.Pending > 0) settle["grimeNotDone"] = grime.Pending;
            }

            // 2. engine signals quiet for >= 10 frames and >= 150 ms
            float t1 = Time.realtimeSinceStartup;
            var prev = ReadSignals();
            int quietFrames = 0;
            float quietSince = Time.realtimeSinceStartup;
            string lastWhy = null;
            bool signalsOk = false;
            while (Time.realtimeSinceStartup - t0 < timeout)
            {
                yield return null;
                var now = ReadSignals();
                if (Quiet(prev, now, out var why) && _lastFrameMs < 100f) { if (quietFrames == 0) quietSince = Time.realtimeSinceStartup; quietFrames++; }
                else { quietFrames = 0; lastWhy = why ?? "slow frame"; }
                prev = now;
                if (quietFrames >= 10 && Time.realtimeSinceStartup - quietSince >= 0.15f) { signalsOk = true; break; }
            }
            settle["signalsMs"] = Math.Round((Time.realtimeSinceStartup - t1) * 1000f);
            settle["signals"] = SignalsJson(prev);
            if (!signalsOk) settle["signalsNotQuiet"] = lastWhy ?? "timeout";

            // 3. pixel proof: two consecutive pairs below the noise limits
            float t2 = Time.realtimeSinceStartup;
            var pairs = new JArray();
            // a view that is not straight down shows the far distance in its top part: far lots, horizon, distant shadow
            // cascades. The proof is about the subject: the top 15 % of such a picture is left out (recorded).
            float topExclude = Camera.main != null && Camera.main.transform.forward.y > -0.95f ? 0.15f : 0f;
            settle["topExcluded"] = topExclude;
            Texture2D a = null, b = null;
            string err = null;
            int good = 0;
            // the proof is about the scene: always on clean render captures (the overlay and the panel may animate: a
            // pulsing marker, a spinner); the picture in the asked mode is taken once the scene is proven still
            string proof = "render";
            settle["proofMode"] = proof;
            yield return CaptureOnce(proof);
            a = _capture;
            err = _captureError;
            SuiteCapture.Diff lastDiff = default;
            while (a != null && Time.realtimeSinceStartup - t0 < timeout + 2f)
            {
                float ta = Time.realtimeSinceStartup;
                yield return Frames(15);
                while (Time.realtimeSinceStartup - ta < 0.25f) yield return null;
                yield return CaptureOnce(proof);
                b = _capture;
                if (b == null) { err = _captureError; break; }
                var d = SuiteCapture.Compare(a, b, topExclude);
                lastDiff = d;
                bool pass = d.Comparable && SuiteStats.PairSettled(d.Changed16, d.TileMax, ChangedLimit, TileLimit);
                pairs.Add(new JArray(d.Changed16, Math.Round(d.TileMax, 1), pass ? 1 : 0));
                if (pass) good++; else good = 0;
                if (good >= 2) { UnityEngine.Object.Destroy(a); a = b; b = null; break; }
                if (Time.realtimeSinceStartup - t0 >= timeout)
                {
                    break;
                }
                UnityEngine.Object.Destroy(a);
                a = b;
                b = null;
            }
            bool settled = good >= 2 && signalsOk;
            settle["pixelsMs"] = Math.Round((Time.realtimeSinceStartup - t2) * 1000f);
            settle["pairs"] = pairs;
            settle["limits"] = new JArray(ChangedLimit, Math.Round(TileLimit, 1));
            settle["totalMs"] = Math.Round((Time.realtimeSinceStartup - t0) * 1000f);
            var keep = b ?? a;
            Texture2D final = null;
            if (keep != null && mode != proof)
            {
                yield return CaptureOnce(mode);
                final = _capture;
                if (final == null) err = _captureError;
            }
            if (keep != null)
            {
                try { File.WriteAllBytes(path, (final ?? keep).EncodeToPNG()); }
                catch (Exception e) { err = e.Message; }
                settle["luma"] = Math.Round(SuiteCapture.MeanLuma(keep), 1);
            }
            if (a != null) UnityEngine.Object.Destroy(a);
            if (b != null) UnityEngine.Object.Destroy(b);
            if (final != null) UnityEngine.Object.Destroy(final);
            if (labels) SuiteCapture.Labels(false);
            if (zones)
            {
                var ft = FixtureTool;
                ft.ShowZones = false;
                bool wasOurs = toolBefore == _tool;
                ft.Tools.activeTool = wasOurs || toolBefore == null ? ft.DefaultTool : toolBefore;
                yield return Frames(5);
                if (wasOurs) yield return OpenTool();
            }
            string rel = err == null ? "shots/" + file : "ERROR " + err;
            rec["shot"] = rel;
            rec["shotName"] = name;
            rec["shotMode"] = mode;
            rec["shotSettled"] = settled;
            rec["settle"] = settle;
            Waited(rec, "shotSettle", Time.realtimeSinceStartup - t0, 0f);
            if (!settled && err == null)
            {
                var t = rec["timeouts"] as JArray ?? (JArray)(rec["timeouts"] = new JArray());
                t.Add($"shot {name} unsettled after {timeout:F0} s ({(signalsOk ? "pixels still changing: " + lastDiff.Changed16 + " px, tile " + lastDiff.TileMax.ToString("F1") : "engine not quiet: " + (lastWhy ?? "?"))})");
            }
            st.Shots[name] = rel;
            if (err != null) Say($"shot {name} failed: {err}");
            _contact.Add(new ShotRow
            {
                Scenario = st.FileIndex, Id = st.S.Id, Step = st.StepIndex, Do = (string)st.Result["steps"]?[st.StepIndex]?["do"] ?? "", Name = name, File = err == null ? "shots/" + file : null,
                Mode = mode, Settled = settled, SettleMs = (int)(double)settle["totalMs"], Camera = (rec["camera"] as JObject)?.DeepClone() as JObject, Error = err,
            });
        }

        /// <summary>
        /// The noise floor of the capture path on a still view (for the settle gate's limits): several render captures
        /// 300 ms apart, the largest changed-pixel count and worst tile between consecutive ones.
        /// </summary>
        private IEnumerator CalibrateNoise(float3 pivot)
        {
            var rec = new JObject();
            yield return CameraTo(pivot, 25f, 60f, 300f, rec, null);
            yield return Seconds(1f);
            Texture2D prev = null;
            int worstC = 0;
            float worstT = 0f;
            var pairs = new JArray();
            for (int i = 0; i < 8; i++)
            {
                yield return CaptureOnce("render");
                var t = _capture;
                if (t == null) break;
                if (prev != null)
                {
                    var d = SuiteCapture.Compare(prev, t);
                    worstC = Math.Max(worstC, d.Changed16);
                    worstT = Math.Max(worstT, d.TileMax);
                    pairs.Add(new JArray(d.Changed16, Math.Round(d.TileMax, 2), d.TileX, d.TileY));
                    UnityEngine.Object.Destroy(prev);
                }
                prev = t;
                yield return Seconds(0.3f);
            }
            if (prev != null) UnityEngine.Object.Destroy(prev);
            // the median pair is the floor (one pair can catch a transient); the worst is recorded too
            var cs = pairs.Select(x => (int)x[0]).OrderBy(x => x).ToList();
            var ts = pairs.Select(x => (float)x[1]).OrderBy(x => x).ToList();
            _noiseChanged = cs.Count > 0 ? cs[cs.Count / 2] : 400;
            _noiseTile = ts.Count > 0 ? ts[ts.Count / 2] : 3f;
            _summary["noiseFloor"] = new JObject { ["changed16"] = _noiseChanged, ["tile32"] = Math.Round(_noiseTile, 2), ["worstChanged16"] = worstC, ["worstTile32"] = Math.Round(worstT, 2), ["pairs"] = pairs, ["limits"] = new JArray(ChangedLimit, Math.Round(TileLimit, 1)) };
            Say($"noise floor (median pair): changed16 {_noiseChanged}, tile {_noiseTile:F2} (worst {worstC} / {worstT:F2}) -> limits {ChangedLimit} px / tile {TileLimit:F1}");
        }

        private static string Safe(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return id.Replace('+', 'x').Replace(' ', '_');
        }
    }
}
