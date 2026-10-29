// Game runner lab (research, never on for players): dynamic decals that are never game entities (docs/research/dynamic-decals.md).
// Textures generated in code (SuiteLabDecalArt), drawn with the game's own "DefaultDecal" material template (the material the
// game's decals and ExtraAssetsImporter's pack decals are made from) on a plain Unity MeshRenderer, the way the game's own
// RenderPrefabRenderer draws a decal prefab outside the batch system. Nothing is an entity, a prefab or an asset: a save never
// names them, and without the code that makes them they simply are not there.
//   {"do":"lab","op":"dynDecalInfo"}                    HDRP decal settings, the camera's frame settings, the template material
//   {"do":"lab","op":"dynDecal","kind":"dirt","at":[dx,dz],"anchor":"south","size":[8,8],"res":512,"yaw":0,
//        "layers":"terrain","height":3,"seed":1,"route":"mesh|projector","priority":0,"compress":true}
//   {"do":"lab","op":"dynPath","points":[[dx,dz],[dx,dz],[dx,dz],[dx,dz]],"width":2.6,"segment":8,"res":[256,96]}
//   {"do":"lab","op":"dynFrames","frames":240}          frame times (median, 95 %, worst)
//   {"do":"lab","op":"dynDecalClear"}                    every lab decal destroyed (the mod switched off / removed)
//   {"do":"lab","op":"dynDecalCount"}                    lab decals alive + entities or prefabs that carry our name (after a load)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        internal const string DynDecalPrefix = "PlacemakerDynDecal";

        private sealed class DynDecal
        {
            public GameObject Go;
            public Material Mat;
            public Mesh Mesh;
            public Texture2D[] Tex;
            public long Bytes;
        }

        private static readonly List<DynDecal> _dynDecals = new List<DynDecal>();
        private static Material _dynTemplate;
        private static readonly List<float2> _dynRing = new List<float2>();

        private void LabDynFrameAll(ScenarioState st, JObject rec)
        {
            if (_dynRing.Count == 0) { rec["error"] = "lab: no lab decals to frame"; return; }
            float2 lo = _dynRing.Aggregate(math.min), hi = _dynRing.Aggregate(math.max);
            st.Ring = new[] { lo, new float2(hi.x, lo.y), hi, new float2(lo.x, hi.y) };
            Event("build");
        }

        private static Material DynTemplate(JObject rec)
        {
            if (_dynTemplate != null) return _dynTemplate;
            var lib = AssetDatabase.global?.resources?.materialLibrary;
            _dynTemplate = lib?.GetMaterial("DefaultDecal");
            if (_dynTemplate == null && rec != null) rec["error"] = "lab: no 'DefaultDecal' material in the game's material library";
            return _dynTemplate;
        }

        private static int Layers(string s)
        {
            if (string.IsNullOrEmpty(s)) return 1;
            int m = 0;
            foreach (var p in s.Split(',', '|', ' '))
                switch (p.Trim().ToLowerInvariant())
                {
                    case "terrain": m |= 1; break;
                    case "roads": m |= 2; break;
                    case "buildings": m |= 4; break;
                    case "vehicles": m |= 8; break;
                    case "creatures": m |= 16; break;
                    case "other": m |= 32; break;
                    case "all": m |= 63; break;
                }
            return m == 0 ? 1 : m;
        }

        private IEnumerator LabDynDecalInfo(JObject rec)
        {
            yield return null;
            var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (asset != null)
            {
                var s = asset.currentPlatformRenderPipelineSettings;
                rec["hdrp"] = new JObject
                {
                    ["supportDecals"] = s.supportDecals, ["supportDecalLayers"] = s.supportDecalLayers, ["supportSurfaceGradient"] = s.supportSurfaceGradient,
                    ["decalNormalBufferHP"] = s.decalNormalBufferHP, ["drawDistance"] = s.decalSettings.drawDistance, ["atlas"] = $"{s.decalSettings.atlasWidth}x{s.decalSettings.atlasHeight}",
                    ["perChannelMask"] = s.decalSettings.perChannelMask, ["maxDecalsOnScreen"] = s.lightLoopSettings.maxDecalsOnScreen,
                    ["litShaderMode"] = s.supportedLitShaderMode.ToString(),
                };
            }
            else rec["hdrp"] = "no HDRenderPipelineAsset: " + GraphicsSettings.currentRenderPipeline?.GetType().Name;
            var cam = Camera.main;
            if (cam != null)
            {
                try
                {
                    var hd = HDCamera.GetOrCreate(cam);
                    var fs = hd.frameSettings;
                    rec["camera"] = new JObject
                    {
                        ["decals"] = fs.IsEnabled(FrameSettingsField.Decals), ["decalLayers"] = fs.IsEnabled(FrameSettingsField.DecalLayers),
                        ["litShaderMode"] = fs.litShaderMode.ToString(), ["ssr"] = fs.IsEnabled(FrameSettingsField.SSR),
                        ["depthPrepassDeferred"] = fs.IsEnabled(FrameSettingsField.DepthPrepassWithDeferredRendering),
                    };
                }
                catch (Exception e) { rec["camera"] = "frame settings: " + e.Message; }
            }
            var lib = AssetDatabase.global?.resources?.materialLibrary;
            if (lib?.m_Materials != null)
                rec["decalTemplates"] = new JArray(lib.m_Materials.Where(d => d?.m_Material != null && d.m_Material.name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(d => $"{d.m_Material.name} (shader {d.m_Material.shader?.name}, VT {d.m_SupportsVT}, queue {d.m_Material.renderQueue})"));
            var t = DynTemplate(rec);
            if (t != null)
            {
                var passes = new JArray();
                for (int i = 0; i < t.passCount; i++) passes.Add(t.GetPassName(i));
                var props = new JObject();
                foreach (MaterialPropertyType kind in new[] { MaterialPropertyType.Float, MaterialPropertyType.Int, MaterialPropertyType.Vector, MaterialPropertyType.Texture })
                    foreach (var n in t.GetPropertyNames(kind))
                        props[n] = kind == MaterialPropertyType.Float ? (JToken)Math.Round(t.GetFloat(n), 3)
                            : kind == MaterialPropertyType.Int ? t.GetInt(n)
                            : kind == MaterialPropertyType.Vector ? t.GetVector(n).ToString()
                            : (t.GetTexture(n)?.name ?? "none");
                rec["template"] = new JObject
                {
                    ["name"] = t.name, ["shader"] = t.shader.name, ["queue"] = t.renderQueue, ["passes"] = passes,
                    ["keywords"] = new JArray(t.shaderKeywords), ["properties"] = props,
                };
            }
            Mod.Log.Info("Placemaker lab: dyn decal info " + rec.ToString(Newtonsoft.Json.Formatting.None));
        }

        private float2 DynAt(ScenarioState st, JObject step)
        {
            var b = st.S.Block;
            var at = step["at"] is JArray a && a.Count >= 2 ? new float2((float)a[0], (float)a[1]) : float2.zero;
            if ((bool?)step["abs"] == true) return at;
            switch (((string)step["anchor"] ?? "").ToLowerInvariant())
            {
                case "south": return new float2(b.Center.x + at.x, b.Z0 + at.y);
                case "north": return new float2(b.Center.x + at.x, b.Z1 + at.y);
                case "west": return new float2(b.X0 + at.x, b.Center.y + at.y);
                case "east": return new float2(b.X1 + at.x, b.Center.y + at.y);
                default: return b.Center + at;
            }
        }

        private IEnumerator LabDynDecal(ScenarioState st, JObject step, JObject rec)
        {
            var template = DynTemplate(rec);
            if (template == null) yield break;
            string kind = ((string)step["kind"] ?? "dirt").ToLowerInvariant();
            var size = step["size"] is JArray sz && sz.Count >= 2 ? new float2((float)sz[0], (float)sz[1]) : new float2(6f, 6f);
            int resX, resY;
            if (step["res"] is JArray ra && ra.Count >= 2) { resX = (int)ra[0]; resY = (int)ra[1]; }
            else { int r = (int?)step["res"] ?? 512; float asp = size.y / size.x; resX = r; resY = math.max(16, (int)math.round(r * asp)); }
            float yawDeg = (float?)step["yaw"] ?? 0f, height = (float?)step["height"] ?? 3f;
            int seed = (int?)step["seed"] ?? 1;
            var at = DynAt(st, step);
            var task = Task.Run(() => LabDecalArt.Make(kind, resX, resY, size, seed, at, math.radians(yawDeg),
                flipU: (bool?)step["flipU"] == true, flipV: (bool?)step["flipV"] == true));
            yield return Until(() => task.IsCompleted, 60f, "decal textures");
            if (!task.IsCompleted || task.IsFaulted) { rec["error"] = "lab: texture generation failed: " + task.Exception?.GetBaseException().Message; yield break; }
            var d = Spawn(st, step, rec, task.Result, at, size, yawDeg, height, kind, template);
            if (d == null) yield break;
            Mod.Log.Info($"Placemaker lab: dyn decal {kind} at ({at.x:F1}, {at.y:F1}) {size.x}x{size.y} m, {resX}x{resY}, generated in {task.Result.Ms:F0} ms on a worker, " +
                $"uploaded in {rec["uploadMs"]} ms, {d.Bytes / 1024} KiB on the GPU, route {rec["route"]}, layers {rec["layers"]}, queue {rec["queue"]}");
        }

        private IEnumerator LabDynPath(ScenarioState st, JObject step, JObject rec)
        {
            var template = DynTemplate(rec);
            if (template == null) yield break;
            var b = st.S.Block;
            var pts = (step["points"] as JArray)?.Select(p => b.Center + new float2((float)p[0], (float)p[1])).ToArray();
            if (pts == null || pts.Length != 4) { rec["error"] = "lab: dynPath needs four Bézier points"; yield break; }
            var cv = LabCurve.Bezier(pts[0], pts[1], pts[2], pts[3]);
            float width = (float?)step["width"] ?? 2.6f, seg = (float?)step["segment"] ?? 8f;
            int resX = 256, resY = 96;
            if (step["res"] is JArray ra && ra.Count >= 2) { resX = (int)ra[0]; resY = (int)ra[1]; }
            int seed = (int?)step["seed"] ?? 3;
            int n = math.max(1, (int)math.ceil(cv.Length / seg));
            float margin = 1.5f, boxW = width * 1.4f + 2.5f;
            var jobs = new List<(float2 c, float yaw, float2 size, Task<LabDecalMaps> t)>();
            for (int i = 0; i < n; i++)
            {
                float s0 = cv.Length * i / n, s1 = i == n - 1 ? cv.Length + 1f : cv.Length * (i + 1) / n;
                var a = cv.At(s0); var e = cv.At(math.min(s1, cv.Length));
                var mid = cv.At((s0 + math.min(s1, cv.Length)) * 0.5f);
                var dir = math.normalizesafe(e - a);
                var c = (a + e) * 0.5f;
                float sag = math.distance(mid, c);
                c = math.lerp(c, mid, 0.5f);   // centre the box on the bulge
                float yaw = math.atan2(-dir.y, dir.x);   // local +x along the chord (RotateY: x -> (cos, -sin))
                var size = new float2(math.distance(a, e) + 2f * margin, boxW + 2f * sag);
                float ls0 = s0, ls1 = s1;
                var task = Task.Run(() => LabDecalArt.Make("path", resX, resY, size, seed, c, yaw, cv, ls0, ls1, width,
                    (bool?)step["flipU"] == true, (bool?)step["flipV"] == true));
                jobs.Add((c, yaw, size, task));
            }
            yield return Until(() => jobs.All(j => j.t.IsCompleted), 120f, "path textures");
            if (jobs.Any(j => !j.t.IsCompleted || j.t.IsFaulted)) { rec["error"] = "lab: path textures failed: " + jobs.FirstOrDefault(j => j.t.IsFaulted).t?.Exception?.GetBaseException().Message; yield break; }
            double genMs = 0; long bytes = 0;
            var ring = new List<float2>();
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                var sub = new JObject();
                var d = Spawn(st, step, sub, j.t.Result, j.c, j.size, math.degrees(j.yaw), (float?)step["height"] ?? 3f, "path" + i, template, frameIt: false);
                if (d == null) { rec["error"] = sub["error"]; yield break; }
                genMs += j.t.Result.Ms; bytes += d.Bytes;
                rec["route"] = sub["route"]; rec["queue"] = sub["queue"];
            }
            for (int k = 0; k <= 20; k++) ring.Add(cv.At(cv.Length * k / 20f));
            float2 lo = ring.Aggregate(math.min) - 4f, hi = ring.Aggregate(math.max) + 4f;
            var box = new[] { lo, new float2(hi.x, lo.y), hi, new float2(lo.x, hi.y) };
            st.Ring = box;
            _dynRing.AddRange(box);
            Event("build");
            rec["segments"] = jobs.Count; rec["length"] = Math.Round(cv.Length, 1); rec["generateMsTotal"] = Math.Round(genMs); rec["gpuKiB"] = bytes / 1024;
            Mod.Log.Info($"Placemaker lab: dyn path {cv.Length:F0} m in {jobs.Count} boxes ({resX}x{resY} each), generated in {genMs:F0} ms of worker time, {bytes / 1024} KiB on the GPU");
        }

        private DynDecal Spawn(ScenarioState st, JObject step, JObject rec, LabDecalMaps maps, float2 at, float2 size, float yawDeg, float height, string kind, Material template, bool frameIt = true)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool compress = (bool?)step["compress"] ?? true;
            var d = new DynDecal();
            try
            {
                var alb = Tex(maps.Albedo, maps.W, maps.H, false, kind + " albedo", compress);
                var nrm = Tex(maps.Normal, maps.W, maps.H, true, kind + " normal", compress);
                var msk = Tex(maps.Mask, maps.W, maps.H, true, kind + " mask", compress);
                d.Tex = new[] { alb, nrm, msk };
                d.Bytes = d.Tex.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
                int layers = Layers((string)step["layers"]);
                var m = new Material(template) { name = DynDecalPrefix + " " + kind, hideFlags = HideFlags.HideAndDontSave };
                Set(m, "_BaseColorMap", alb); Set(m, "_NormalMap", nrm); Set(m, "_MaskMap", msk);
                if (m.HasProperty("_BaseColor")) m.SetVector("_BaseColor", new Vector4(1f, 1f, 1f, (float?)step["opacity"] ?? 1f));
                foreach (var f in new[] { "_AffectAlbedo", "_AffectNormal", "_AffectSmoothness", "_AffectAO", "_NormalOpacity", "_MetallicOpacity" })
                    if (m.HasProperty(f)) m.SetFloat(f, 1f);
                if (m.HasProperty("_AffectMetal")) m.SetFloat("_AffectMetal", 0f);
                m.SetVector("colossal_TextureArea", new Vector4(0f, 0f, 1f, 1f));
                m.SetVector("colossal_MeshSize", new Vector4(size.x, height, size.y, 0f));
                m.SetFloat("colossal_DecalLayerMask", math.asfloat(layers));
                if ((bool?)step["noVT"] != false) m.DisableKeyword("ENABLE_VT");
                try { HDMaterial.ValidateMaterial(m); } catch (Exception e) { rec["validate"] = e.Message; }
                m.renderQueue = template.renderQueue + ((int?)step["priority"] ?? 0);
                d.Mat = m;

                var go = new GameObject(DynDecalPrefix + " " + kind) { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                float y = _fixture.H(at.x, at.y);
                go.transform.SetPositionAndRotation(new Vector3(at.x, y, at.y), Quaternion.Euler(0f, yawDeg, 0f));
                string route = ((string)step["route"] ?? "mesh").ToLowerInvariant();
                if (route == "projector")
                {
                    // HDRP's own decal projector: projects along its +z, so it looks down
                    go.transform.rotation = Quaternion.Euler(90f, yawDeg, 0f);
                    var pr = go.AddComponent<DecalProjector>();
                    pr.material = m;
                    pr.size = new Vector3(size.x, size.y, height);
                    pr.pivot = Vector3.zero;
                    pr.drawDistance = 2000f;
                    rec["projectorPass"] = m.FindPass("DBufferProjector");
                }
                else
                {
                    d.Mesh = Box(size.x, height, size.y);
                    go.AddComponent<MeshFilter>().sharedMesh = d.Mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = m;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    rec["meshPass"] = m.FindPass("DBufferMesh");
                }
                d.Go = go;
                _dynDecals.Add(d);
                rec["route"] = route; rec["layers"] = layers; rec["queue"] = m.renderQueue;
                rec["generateMs"] = Math.Round(maps.Ms); rec["uploadMs"] = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
                rec["gpuKiB"] = d.Bytes / 1024; rec["covered"] = Math.Round(100.0 * maps.Covered / (maps.W * maps.H), 1);
                rec["atAbs"] = new JArray(Math.Round(at.x, 1), Math.Round(at.y, 1));
                rec["keywords"] = new JArray(m.shaderKeywords);
            }
            catch (Exception e)
            {
                rec["error"] = "lab: decal not made: " + e.Message;
                Mod.Log.Warn("Placemaker lab: dyn decal failed: " + e);
                return null;
            }
            if (frameIt)
            {
                float r = math.max(size.x, size.y) * 0.5f + 2f;
                var ring = new[] { at + new float2(-r, -r), at + new float2(r, -r), at + new float2(r, r), at + new float2(-r, r) };
                st.Ring = ring;   // this decal alone (a closeup frames it); dynFrameAll frames them all
                _dynRing.AddRange(ring);
                Event("build");
            }
            return d;
        }

        private static void Set(Material m, string name, Texture t) { if (m.HasProperty(name)) m.SetTexture(name, t); }

        private static Texture2D Tex(byte[] data, int w, int h, bool linear, string name, bool compress)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            {
                name = DynDecalPrefix + " " + name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear, anisoLevel = 8,
            };
            t.SetPixelData(data, 0);
            t.Apply(true, false);
            if (compress) t.Compress(false);
            t.Apply(false, true);
            return t;
        }

        /// <summary>The decal box as ExtraAssetsImporter builds it (centred, x length, y height, z width; 24 vertices).</summary>
        private static Mesh Box(float lx, float hy, float wz)
        {
            var c = new[]
            {
                new Vector3(-lx, -hy, wz) * 0.5f, new Vector3(lx, -hy, wz) * 0.5f, new Vector3(lx, -hy, -wz) * 0.5f, new Vector3(-lx, -hy, -wz) * 0.5f,
                new Vector3(-lx, hy, wz) * 0.5f, new Vector3(lx, hy, wz) * 0.5f, new Vector3(lx, hy, -wz) * 0.5f, new Vector3(-lx, hy, -wz) * 0.5f,
            };
            var v = new[] { c[0], c[1], c[2], c[3], c[7], c[4], c[0], c[3], c[4], c[5], c[1], c[0], c[6], c[7], c[3], c[2], c[5], c[6], c[2], c[1], c[7], c[6], c[5], c[4] };
            var n = new[] { Vector3.down, Vector3.left, Vector3.forward, Vector3.back, Vector3.right, Vector3.up }.SelectMany(x => Enumerable.Repeat(x, 4)).ToArray();
            Vector2 a = new Vector2(0, 0), b = new Vector2(1, 0), cc = new Vector2(0, 1), dd = new Vector2(1, 1);
            var uv = Enumerable.Range(0, 6).SelectMany(_ => new[] { dd, cc, a, b }).ToArray();
            var tri = new[] { 3, 1, 0, 3, 2, 1, 7, 5, 4, 7, 6, 5, 11, 9, 8, 11, 10, 9, 15, 13, 12, 15, 14, 13, 19, 17, 16, 19, 18, 17, 23, 21, 20, 23, 22, 21 };
            var m = new Mesh { name = DynDecalPrefix + " box", hideFlags = HideFlags.HideAndDontSave, vertices = v, normals = n, uv = uv, triangles = tri };
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        private IEnumerator LabDynFrames(JObject step, JObject rec)
        {
            int n = (int?)step["frames"] ?? 240;
            var ms = new List<float>(n);
            yield return Frames(5);
            for (int i = 0; i < n; i++) { yield return null; ms.Add(Time.unscaledDeltaTime * 1000f); }
            ms.Sort();
            rec["frameMs"] = new JObject { ["median"] = Math.Round(ms[ms.Count / 2], 2), ["p95"] = Math.Round(ms[(int)(ms.Count * 0.95f)], 2), ["worst"] = Math.Round(ms[ms.Count - 1], 2), ["mean"] = Math.Round(ms.Average(), 2) };
            rec["decals"] = _dynDecals.Count;
            Mod.Log.Info($"Placemaker lab: dyn frames ({_dynDecals.Count} lab decals, label {(string)step["label"]}) " + rec["frameMs"].ToString(Newtonsoft.Json.Formatting.None));
        }

        private static void LabDynDecalClear(JObject rec)
        {
            int n = _dynDecals.Count;
            foreach (var d in _dynDecals)
            {
                if (d.Go != null) UnityEngine.Object.Destroy(d.Go);
                if (d.Mat != null) UnityEngine.Object.Destroy(d.Mat);
                if (d.Mesh != null) UnityEngine.Object.Destroy(d.Mesh);
                if (d.Tex != null) foreach (var t in d.Tex) if (t != null) UnityEngine.Object.Destroy(t);
            }
            _dynDecals.Clear();
            _dynRing.Clear();
            rec["destroyed"] = n;
            Mod.Log.Info($"Placemaker lab: dyn decals cleared ({n} destroyed with their materials, meshes and textures)");
        }

        private void LabDynDecalCount(JObject rec)
        {
            var em = _world.EntityManager;
            var prefabs = _world.GetExistingSystemManaged<PrefabSystem>();
            int prefabsNamed = 0, missing = 0;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var pd = em.GetComponentData<PrefabData>(e);
                    string name = null;
                    try { name = pd.m_Index >= 0 ? prefabs.GetPrefabName(e) : prefabs.GetObsoleteID(pd).GetName(); } catch (Exception) { }
                    if (pd.m_Index < 0) missing++;
                    if (name != null && name.IndexOf(DynDecalPrefix, StringComparison.Ordinal) >= 0) prefabsNamed++;
                }
            var (onMissing, _) = ObjectsOnMissingPrefabs();
            int objs = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name.StartsWith(DynDecalPrefix, StringComparison.Ordinal));
            rec["labDecalsAlive"] = _dynDecals.Count;
            rec["gameObjectsNamed"] = objs;
            rec["prefabsNamedLikeOurs"] = prefabsNamed;
            rec["obsoletePrefabs"] = missing;
            rec["objectsOnMissingPrefabs"] = onMissing;
            Mod.Log.Info($"Placemaker lab: dyn decal count: {_dynDecals.Count} alive, {objs} game objects named {DynDecalPrefix}, {prefabsNamed} prefabs with our name, {missing} obsolete (missing) prefabs, {onMissing} objects on a missing prefab");
        }
    }
}
