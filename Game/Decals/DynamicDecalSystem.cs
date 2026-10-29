// Placemaker — dynamic decals in the game: code-made grime drawn with the game's own decal material, never as entities
// (docs/DYNAMIC-DECALS.md, docs/research/dynamic-decals.md). GPL-3.0.
//
// Every finished group record that carries strokes (PlacemakerDecalBlob) gets its decals: the strokes are cut into tiles,
// each tile generated on a background thread (Core/Decals/Dynamic: deterministic from the seed, mip chain and BC3 made
// there too), then the main thread only copies the bytes into three textures and puts a box renderer with a copy of the
// game's "DefaultDecal" material in the scene, a few tiles a frame within a time budget. Groups that go (undo, bulldoze,
// ungroup, another city loaded) take their renderers, materials and textures with them. Renderers are culled by distance
// and hidden while an infoview is open. Everything is HideAndDontSave: the save never sees any of it; the strokes on the
// record are the only trace, and without Placemaker the game skips that component type.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core.Build;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Game.Groups;
using Placemaker.Game.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Scripting;

namespace Placemaker.Game.Decals
{
    public sealed partial class DynamicDecalSystem : GameSystemBase
    {
        public const string Prefix = "PlacemakerGrime";
        /// <summary>Main-thread time a frame may spend putting finished tiles on the GPU.</summary>
        public const double UploadBudgetMs = 2.0;

        internal static DynamicDecalSystem Instance { get; private set; }
        /// <summary>Lab switch (the game runner): false hides and frees every dynamic decal.</summary>
        internal static bool Enabled = true;

        private sealed class Tile
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public Material Mat;
            public Mesh Mesh;
            public Texture2D[] Tex;
            public float3 Centre;
            public float Cull;
            public float Fade = -1f;
            public long Bytes;
        }

        private sealed class Group
        {
            public int Id;
            public Entity Record;
            public int Hash;
            public int Token;
            public int Strokes, TilesPlanned, TilesEmpty;
            public int Pending;
            public readonly List<Tile> Tiles = new List<Tile>();
            public double WorkerMs;
            public System.Diagnostics.Stopwatch Since;
            public double UploadMs, WorstUploadMs;
            public bool Logged;
            public List<DecalStroke> List;
        }

        private sealed class Job
        {
            public Group Group;
            public int Token;
            public StrokeFrame Frame;
            public DecalTile Tile;
            public byte Layers;
            public int Order;
            public float Lod;
            // results
            public byte[] Albedo, Normal, Mask;
            public int W, H, Covered;
            public double Ms;
            public string Error;
        }

        private readonly Dictionary<int, Group> _groups = new Dictionary<int, Group>();
        private readonly ConcurrentQueue<Job> _todo = new ConcurrentQueue<Job>();
        private readonly ConcurrentQueue<Job> _done = new ConcurrentQueue<Job>();
        private int _workers, _queued;
        private int _maxWorkers;
        private EntityQuery _records;
        private ToolSystem _tools;
        private TerrainSystem _terrain;
        private Material _template;
        private int _lastVersion = -1, _lastScan = -1000;
        private bool _hidden;
        private readonly HashSet<string> _warnedKinds = new HashSet<string>();

        /// <summary>Tiles waiting to be generated or put on the GPU (the game runner waits for 0 before a picture).</summary>
        public int Pending => Volatile.Read(ref _queued) + _done.Count;
        public int GroupCount => _groups.Count;
        public int TileCount { get { int n = 0; foreach (var g in _groups.Values) n += g.Tiles.Count; return n; } }
        public long GpuBytes { get { long n = 0; foreach (var g in _groups.Values) foreach (var t in g.Tiles) n += t.Bytes; return n; } }

        /// <summary>The n-th stroke of a kind in the newest group (the game runner frames close-ups on it).</summary>
        internal bool TryFind(string kind, int index, out float2 lo, out float2 hi)
        {
            lo = hi = default;
            Group best = null;
            foreach (var g in _groups.Values) if (best == null || g.Id > best.Id) best = g;
            if (best?.List == null) return false;
            int n = 0;
            foreach (var s in best.List)
                if (s.Kind == kind && n++ == index) { s.Bounds(out lo, out hi); return true; }
            return false;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            _records = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerGroup>(), ComponentType.ReadOnly<PlacemakerDecalBlob>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _tools = World.GetOrCreateSystemManaged<ToolSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _maxWorkers = math.clamp(Environment.ProcessorCount / 4, 1, 3);
            // the mod folder is known only once the mod has loaded: the library is read on first use (Start)
        }

        internal static void LoadLibrary()
        {
            string path = ModPaths.ModFolder != null ? Path.Combine(ModPaths.ModFolder, "Recipes", DecalLibrary.FolderName) : null;
            try
            {
                if (path == null || !File.Exists(Path.Combine(path, DecalLibrary.FileName))) { Mod.Log.Warn("Placemaker dynamic decals: no decal library at " + path + "; grime is not drawn."); return; }
                // the stroke kinds (kinds.json) and the shared library's families (library/*.json)
                var lib = DecalLibrary.LoadFolder(path);
                DecalLibrary.Current = lib;
                Mod.Log.Info($"Placemaker dynamic decals: {lib.Count} kinds in the library ({path}).");
                foreach (var w in lib.Warnings) Mod.Log.Warn("Placemaker dynamic decals: " + w);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker dynamic decals: cannot read the decal library: " + e.Message); }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            ClearAll("system destroyed");
            SharedDecals.ClearAll();
            if (ReferenceEquals(Instance, this)) Instance = null;
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            ClearAll("another city loads");
            SharedDecals.ClearAll();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                if (!Enabled) { if (_groups.Count > 0) ClearAll("switched off"); return; }
                int frame = UnityEngine.Time.frameCount;
                int version = GroupRegistrySystem.Instance?.Version ?? 0;
                if (version != _lastVersion || frame - _lastScan > 60)
                {
                    _lastVersion = version; _lastScan = frame;
                    Scan();
                }
                Upload();
                Cull();
                SharedDecals.Tick(_terrain, _tools?.activeInfoview != null);
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker dynamic decals: update failed");
            }
        }

        // ───────────────────────── groups ─────────────────────────

        private void Scan()
        {
            var seen = new HashSet<int>();
            using (var es = _records.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    var g = EntityManager.GetComponentData<PlacemakerGroup>(e);
                    if (g.GroupId <= 0) continue;
                    // still being built, being removed, or no longer one build: no grime (yet)
                    if (EntityManager.HasComponent<PlacemakerUnderConstruction>(e) || EntityManager.HasComponent<PlacemakerRemoval>(e)) continue;
                    if ((g.Flags & (int)(GroupFlags.Dissolved | GroupFlags.Ungrouped)) != 0) continue;
                    var buf = EntityManager.GetBuffer<PlacemakerDecalBlob>(e, true);
                    if (buf.Length == 0) continue;
                    int hash = buf.Length;
                    unchecked { for (int i = 0; i < buf.Length; i += 7) hash = hash * 31 + buf[i].Value; }
                    seen.Add(g.GroupId);
                    if (_groups.TryGetValue(g.GroupId, out var have) && have.Hash == hash && have.Record == e) continue;
                    if (have != null) Drop(have, "its strokes changed");
                    var bytes = new byte[buf.Length];
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = buf[i].Value;
                    Start(g.GroupId, e, hash, bytes);
                }
            var gone = new List<Group>();
            foreach (var g in _groups.Values) if (!seen.Contains(g.Id)) gone.Add(g);
            foreach (var g in gone) Drop(g, "the group went");
        }

        private void Start(int id, Entity record, int hash, byte[] bytes)
        {
            var set = DecalCodec.Decode(bytes);
            if (set == null) { Mod.Log.Warn($"Placemaker dynamic decals: group {id}: the strokes on its record are unreadable ({bytes.Length} bytes); no grime drawn."); return; }
            var group = new Group { Id = id, Record = record, Hash = hash, Token = Environment.TickCount ^ (id * 7919), Strokes = set.Strokes.Count, Since = System.Diagnostics.Stopwatch.StartNew(), List = set.Strokes };
            _groups[id] = group;
            // the group's own kinds first, then the library
            var own = new Dictionary<string, DecalKind>(StringComparer.Ordinal);
            foreach (var json in set.Kinds)
            {
                var warnings = new List<string>();
                var k = DecalLibrary.ParseKind(json, warnings);
                if (k != null) own[k.Id] = k;
                foreach (var w in warnings) Mod.Log.Warn($"Placemaker dynamic decals: group {id}: {w}");
            }
            if (DecalLibrary.Current.Count == 0) LoadLibrary();
            var lib = DecalLibrary.Current;
            var jobs = new List<Job>();
            for (int i = 0; i < set.Strokes.Count; i++)
            {
                var s = set.Strokes[i];
                var kind = own.TryGetValue(s.Kind, out var ok) ? ok : lib.Get(s.Kind);
                if (kind == null)
                {
                    if (_warnedKinds.Add(s.Kind)) Mod.Log.Warn($"Placemaker dynamic decals: no kind '{s.Kind}' in the library; its strokes are not drawn.");
                    continue;
                }
                var frame = new StrokeFrame(s, kind);
                foreach (var t in DecalTiler.Tiles(frame, i))
                    jobs.Add(new Job { Group = group, Token = group.Token, Frame = frame, Tile = t, Layers = s.Layers != 0 ? s.Layers : kind.Layers, Order = kind.Order + s.Order, Lod = kind.Lod });
            }
            group.TilesPlanned = jobs.Count;
            group.Pending = jobs.Count;
            foreach (var j in jobs) { _todo.Enqueue(j); Interlocked.Increment(ref _queued); }
            Wake();
        }

        private void Drop(Group g, string why)
        {
            g.Token = -1;   // results still in flight are thrown away
            long bytes = 0;
            foreach (var t in g.Tiles) { bytes += t.Bytes; Free(t); }
            int n = g.Tiles.Count;
            g.Tiles.Clear();
            _groups.Remove(g.Id);
            Mod.Log.Info($"Placemaker dynamic decals: group {g.Id}: {n} tiles removed ({why}), {bytes / 1024} KiB freed.");
        }

        private void ClearAll(string why)
        {
            if (_groups.Count == 0) return;
            foreach (var g in new List<Group>(_groups.Values)) Drop(g, why);
            while (_todo.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
            while (_done.TryDequeue(out _)) { }
        }

        private static void Free(Tile t)
        {
            if (t.Go != null) UnityEngine.Object.Destroy(t.Go);
            if (t.Mat != null) UnityEngine.Object.Destroy(t.Mat);
            if (t.Mesh != null) UnityEngine.Object.Destroy(t.Mesh);
            if (t.Tex != null) foreach (var x in t.Tex) if (x != null) UnityEngine.Object.Destroy(x);
        }

        // ───────────────────────── workers (background threads) ─────────────────────────

        private void Wake()
        {
            while (true)
            {
                int w = Volatile.Read(ref _workers);
                if (w >= _maxWorkers || _todo.IsEmpty) return;
                if (Interlocked.CompareExchange(ref _workers, w + 1, w) != w) continue;
                var th = new Thread(Work) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = Prefix + " worker" };
                th.Start();
            }
        }

        private void Work()
        {
            try
            {
                while (_todo.TryDequeue(out var j))
                {
                    try
                    {
                        if (j.Group.Token == j.Token)
                        {
                            var img = DecalGenerator.Generate(j.Frame, j.Tile);
                            j.W = img.W; j.H = img.H; j.Covered = img.Covered; j.Ms = img.Ms;
                            if (img.Covered > 0)
                            {
                                j.Albedo = DecalTexture.Bc3Chain(img.Albedo, img.W, img.H, alphaWeighted: true, normal: false);
                                j.Normal = DecalTexture.Bc3Chain(img.Normal, img.W, img.H, alphaWeighted: false, normal: true);
                                j.Mask = DecalTexture.Bc3Chain(img.Mask, img.W, img.H, alphaWeighted: false, normal: false);
                            }
                        }
                    }
                    catch (Exception e) { j.Error = e.GetType().Name + ": " + e.Message; }
                    _done.Enqueue(j);
                    Interlocked.Decrement(ref _queued);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _workers);
                if (!_todo.IsEmpty) Wake();
            }
        }

        // ───────────────────────── main thread: upload, cull ─────────────────────────

        private Material Template()
        {
            if (_template != null) return _template;
            _template = AssetDatabase.global?.resources?.materialLibrary?.GetMaterial("DefaultDecal");
            return _template;
        }

        private void Upload()
        {
            if (_done.IsEmpty) return;
            var template = Template();
            if (template == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            TerrainHeightData heights = default;
            bool haveHeights = false;
            while (clock.Elapsed.TotalMilliseconds < UploadBudgetMs && _done.TryDequeue(out var j))
            {
                var g = j.Group;
                if (g.Token != j.Token || !_groups.ContainsKey(g.Id)) continue;
                g.Pending--;
                g.WorkerMs += j.Ms;
                if (j.Error != null) Mod.Log.Warn($"Placemaker dynamic decals: group {g.Id}: a tile of '{j.Frame.K.Id}' failed: {j.Error}");
                if (j.Albedo == null) { g.TilesEmpty++; Finish(g); continue; }
                if (!haveHeights) { heights = _terrain.GetHeightData(true); haveHeights = true; }
                double t0 = clock.Elapsed.TotalMilliseconds;
                var tile = Make(j, template, ref heights);
                double dt = clock.Elapsed.TotalMilliseconds - t0;
                g.UploadMs += dt; g.WorstUploadMs = math.max(g.WorstUploadMs, dt);
                if (tile != null) { g.Tiles.Add(tile); if (_hidden) tile.Renderer.enabled = false; }
                Finish(g);
            }
        }

        private void Finish(Group g)
        {
            if (g.Pending > 0 || g.Logged) return;
            g.Logged = true;
            long bytes = 0; foreach (var t in g.Tiles) bytes += t.Bytes;
            Mod.Log.Info($"Placemaker dynamic decals: group {g.Id}: {g.Strokes} strokes drawn as {g.Tiles.Count} tiles ({g.TilesEmpty} empty of {g.TilesPlanned}) in {g.Since.Elapsed.TotalSeconds:F1} s " +
                $"({g.WorkerMs / 1000.0:F1} s of worker time on up to {_maxWorkers} threads); upload {g.UploadMs:F0} ms on the main thread, worst tile {g.WorstUploadMs:F1} ms; {bytes / (1024 * 1024.0):F1} MiB on the GPU.");
        }

        private static float Height(ref TerrainHeightData h, float2 p)
        {
            float y = TerrainUtils.SampleHeight(ref h, new float3(p.x, 0f, p.y));
            return math.isfinite(y) ? y : 0f;
        }

        private Tile Make(Job j, Material template, ref TerrainHeightData heights)
        {
            var t = j.Tile;
            var tile = new Tile();
            try
            {
                // the box spans the ground under it (sampled at its corners, edge middles and centre) plus a margin
                float lo = float.MaxValue, hi = float.MinValue;
                var corners = t.Corners();
                for (int k = 0; k < 4; k++)
                {
                    float a = Height(ref heights, corners[k]), b = Height(ref heights, math.lerp(corners[k], corners[(k + 1) % 4], 0.5f));
                    lo = math.min(lo, math.min(a, b)); hi = math.max(hi, math.max(a, b));
                }
                float c0 = Height(ref heights, t.Center);
                lo = math.min(lo, c0); hi = math.max(hi, c0);
                float boxH = hi - lo + 6f, y = (hi + lo) * 0.5f;

                var alb = Tex(j.Albedo, j.W, j.H, false, "albedo");
                var nrm = Tex(j.Normal, j.W, j.H, true, "normal");
                var msk = Tex(j.Mask, j.W, j.H, true, "mask");
                tile.Tex = new[] { alb, nrm, msk };
                if (alb == null || nrm == null || msk == null) { Free(tile); return null; }
                tile.Bytes = j.Albedo.Length + j.Normal.Length + j.Mask.Length;

                var m = new Material(template) { name = Prefix + " " + j.Frame.K.Id, hideFlags = HideFlags.HideAndDontSave };
                Set(m, "_BaseColorMap", alb); Set(m, "_NormalMap", nrm); Set(m, "_MaskMap", msk);
                if (m.HasProperty("_BaseColor")) m.SetVector("_BaseColor", new Vector4(1f, 1f, 1f, 1f));
                foreach (var f in new[] { "_AffectAlbedo", "_AffectNormal", "_AffectSmoothness", "_AffectAO", "_NormalOpacity", "_MetallicOpacity" })
                    if (m.HasProperty(f)) m.SetFloat(f, 1f);
                if (m.HasProperty("_AffectMetal")) m.SetFloat("_AffectMetal", 0f);
                m.SetVector("colossal_TextureArea", new Vector4(0f, 0f, 1f, 1f));
                m.SetVector("colossal_MeshSize", new Vector4(t.Size.x, boxH, t.Size.y, 0f));
                m.SetFloat("colossal_DecalLayerMask", math.asfloat((int)j.Layers));
                m.DisableKeyword("ENABLE_VT");
                try { HDMaterial.ValidateMaterial(m); } catch (Exception) { }
                m.renderQueue = template.renderQueue + math.clamp(j.Order, -50, 50);
                tile.Mat = m;

                var go = new GameObject(Prefix + " " + j.Frame.K.Id) { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.transform.SetPositionAndRotation(new Vector3(t.Center.x, y, t.Center.y), Quaternion.Euler(0f, math.degrees(t.Yaw), 0f));
                tile.Mesh = Box(t.Size.x, boxH, t.Size.y);
                go.AddComponent<MeshFilter>().sharedMesh = tile.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                tile.Go = go; tile.Renderer = mr;
                tile.Centre = new float3(t.Center.x, y, t.Center.y);
                float size = math.max(t.Size.x, t.Size.y);
                tile.Cull = math.clamp(j.Lod * size, 250f, 1400f);
                return tile;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker dynamic decals: a tile was not made: " + e.Message);
                Free(tile);
                return null;
            }
        }

        private static void Set(Material m, string name, Texture t) { if (m.HasProperty(name)) m.SetTexture(name, t); }

        /// <summary>A BC3 texture from bytes made on a worker: one copy into the texture's own memory, then the upload.</summary>
        private static Texture2D Tex(byte[] bc3, int w, int h, bool linear, string what)
        {
            var t = new Texture2D(w, h, TextureFormat.DXT5, true, linear)
            {
                name = Prefix + " " + what, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            var raw = t.GetRawTextureData<byte>();
            if (raw.Length != bc3.Length)
            {
                Mod.Log.Warn($"Placemaker dynamic decals: texture {w}x{h} expects {raw.Length} bytes, the worker made {bc3.Length}; tile skipped.");
                UnityEngine.Object.Destroy(t);
                return null;
            }
            raw.CopyFrom(bc3);
            t.Apply(false, true);
            return t;
        }

        /// <summary>The decal box (centred; x length, y height, z width; 24 vertices), as the game's pack decals have it.</summary>
        private static Mesh Box(float lx, float hy, float wz)
        {
            var c = new[]
            {
                new Vector3(-lx, -hy, wz) * 0.5f, new Vector3(lx, -hy, wz) * 0.5f, new Vector3(lx, -hy, -wz) * 0.5f, new Vector3(-lx, -hy, -wz) * 0.5f,
                new Vector3(-lx, hy, wz) * 0.5f, new Vector3(lx, hy, wz) * 0.5f, new Vector3(lx, hy, -wz) * 0.5f, new Vector3(-lx, hy, -wz) * 0.5f,
            };
            var v = new[] { c[0], c[1], c[2], c[3], c[7], c[4], c[0], c[3], c[4], c[5], c[1], c[0], c[6], c[7], c[3], c[2], c[5], c[6], c[2], c[1], c[7], c[6], c[5], c[4] };
            var n = new Vector3[24];
            var dirs = new[] { Vector3.down, Vector3.left, Vector3.forward, Vector3.back, Vector3.right, Vector3.up };
            for (int i = 0; i < 24; i++) n[i] = dirs[i / 4];
            Vector2 a = new Vector2(0, 0), b = new Vector2(1, 0), cc = new Vector2(0, 1), dd = new Vector2(1, 1);
            var uv = new Vector2[24];
            for (int f = 0; f < 6; f++) { uv[f * 4] = dd; uv[f * 4 + 1] = cc; uv[f * 4 + 2] = a; uv[f * 4 + 3] = b; }
            var tri = new[] { 3, 1, 0, 3, 2, 1, 7, 5, 4, 7, 6, 5, 11, 9, 8, 11, 10, 9, 15, 13, 12, 15, 14, 13, 19, 17, 16, 19, 18, 17, 23, 21, 20, 23, 22, 21 };
            var m = new Mesh { name = Prefix + " box", hideFlags = HideFlags.HideAndDontSave, vertices = v, normals = n, uv = uv, triangles = tri };
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        private void Cull()
        {
            bool hide = _tools?.activeInfoview != null;
            var cam = Camera.main;
            if (hide != _hidden)
            {
                _hidden = hide;
                foreach (var g in _groups.Values) foreach (var t in g.Tiles) if (t.Renderer != null) t.Renderer.enabled = !hide;
            }
            if (hide || cam == null) return;
            // a slice of the tiles each frame: distance culling with a fade over the last quarter
            float3 p = cam.transform.position;
            foreach (var g in _groups.Values)
                foreach (var t in g.Tiles)
                {
                    if (t.Renderer == null) continue;
                    float d = math.distance(p, t.Centre);
                    float fade = math.saturate((t.Cull - d) / (t.Cull * 0.25f));
                    bool on = fade > 0.01f;
                    if (t.Renderer.enabled != on) t.Renderer.enabled = on;
                    if (on && math.abs(fade - t.Fade) > 0.03f)
                    {
                        t.Fade = fade;
                        t.Mat.SetVector("_BaseColor", new Vector4(1f, 1f, 1f, fade));
                    }
                }
        }
    }
}
