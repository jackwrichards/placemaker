// Placemaker — the shared decal library in the game (docs/DYNAMIC-DECALS.md, "The shared library"). GPL-3.0.
//
// Every variant ("leafOak#3") is made once for the whole city and shared by every instance that uses it: one set of three
// BC3 textures and one copy of the game's DefaultDecal material per variant; an instance is only a box renderer with a
// property block (its box size and opacity). Variants are made on background threads, and kept on disk as a pure cache
// (ModsData/Placemaker/decal-cache/<kind>-<index>-<recipe hash>.bc3; deleting it only costs the time to remake them).
// Unused variants are freed when the library is over its GPU budget. Nothing is an entity or a prefab: the save never
// sees any of it, and removing the mod leaves nothing behind (the disk cache is in ModsData, like the build audits).
// Driven by DynamicDecalSystem's update (Tick): uploads within a time budget, distance culling, hidden in infoviews.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Environment;
using Game.Simulation;
using Placemaker.Core.Decals.Dynamic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Placemaker.Game.Decals
{
    internal static class SharedDecals
    {
        public const string Prefix = "PlacemakerDecal";
        /// <summary>GPU bytes the library may hold; unused variants beyond it are freed (oldest use first).</summary>
        public const long BudgetBytes = 200L * 1024 * 1024;
        private const double UploadBudgetMs = 1.5;
        private const float BoxHeight = 10f;
        private const uint CacheMagic = 0x31434450;   // "PDC1"

        internal sealed class Variant
        {
            public string Id, Kind;
            public int Index;
            public DecalKind K;
            public int State;            // 0 none, 1 queued, 2 ready, 3 failed
            public Texture2D[] Tex;
            public Material Mat;
            public float2 Span;
            public long Bytes;
            public int Refs;
            public int LastUse;
            public bool FromDisk;
            public double Ms;
            // worker results
            public byte[] A, N, M;
            public int W, H;
            public string Error;
        }

        public sealed class Instance
        {
            internal Variant V;
            public string Owner;
            public float2 Centre;
            public float Yaw, Opacity = 1f;
            public float2 Scale = new float2(1f, 1f);
            public float Y = float.NaN;
            internal GameObject Go;
            internal MeshRenderer Renderer;
            internal MaterialPropertyBlock Block;
            internal float Cull, Fade = -1f;
        }

        private static readonly Dictionary<string, Variant> _variants = new Dictionary<string, Variant>(StringComparer.Ordinal);
        private static readonly List<Instance> _instances = new List<Instance>();
        private static readonly List<Instance> _waiting = new List<Instance>();
        private static readonly ConcurrentQueue<Variant> _todo = new ConcurrentQueue<Variant>();
        private static readonly ConcurrentQueue<Variant> _done = new ConcurrentQueue<Variant>();
        private static readonly Dictionary<long, Mesh> _meshes = new Dictionary<long, Mesh>();
        private static int _workers, _queued;
        private static Material _template;
        private static bool _hidden;
        private static int _cullFrom;

        public static int Pending => Volatile.Read(ref _queued) + _done.Count + _waiting.Count;
        public static int InstanceCount => _instances.Count;
        public static int VariantCount { get { int n = 0; foreach (var v in _variants.Values) if (v.State == 2) n++; return n; } }
        public static long GpuBytes { get { long n = 0; foreach (var v in _variants.Values) n += v.Bytes; return n; } }
        public static int FromDisk { get { int n = 0; foreach (var v in _variants.Values) if (v.FromDisk && v.State == 2) n++; return n; } }
        public static double MakeMs { get { double n = 0; foreach (var v in _variants.Values) n += v.Ms; return n; } }

        private static string CacheDir => Path.Combine(EnvPath.kUserDataPath, "ModsData", "Placemaker", "decal-cache");

        /// <summary>Places one instance of a library variant ("kind#index", or a kind: its variant from the seed).</summary>
        public static Instance Place(string variantId, string owner, float2 centre, float yaw, float scale = 1f, float opacity = 1f, uint seed = 0) =>
            Place(variantId, owner, centre, yaw, new float2(scale, scale), opacity, seed);

        /// <summary>Places one instance stretched by <paramref name="scale"/> along its own x and z (strips fit their run).</summary>
        public static Instance Place(string variantId, string owner, float2 centre, float yaw, float2 scale, float opacity = 1f, uint seed = 0)
        {
            var lib = DecalLibrary.Current;
            if (lib.Count == 0) DynamicDecalSystem.LoadLibrary();
            lib = DecalLibrary.Current;
            if (!DecalVariants.TryParseId(variantId, out var kind, out int index)) return null;
            var k = lib.Get(kind);
            if (k == null) return null;
            if (variantId.IndexOf('#') < 0) index = (int)(seed % (uint)math.max(1, k.Variants.Count));
            index = DecalVariants.Normalize(k, index);   // plain variants, and the end caps of strips and chains
            string id = DecalVariants.Id(kind, index);
            if (!_variants.TryGetValue(id, out var v) || v.K != k)
            {
                v = new Variant { Id = id, Kind = kind, Index = index, K = k };
                _variants[id] = v;
            }
            v.Refs++;
            v.LastUse = Time.frameCount;
            var inst = new Instance { V = v, Owner = owner, Centre = centre, Yaw = yaw, Scale = math.max(new float2(0.05f), scale), Opacity = math.saturate(opacity) };
            if (v.State == 0) { v.State = 1; Interlocked.Increment(ref _queued); _todo.Enqueue(v); Wake(); }
            _waiting.Add(inst);
            return inst;
        }

        /// <summary>Removes every instance of an owner (a build, a lab run); null removes all.</summary>
        public static int Clear(string owner)
        {
            int n = 0;
            for (int i = _instances.Count - 1; i >= 0; i--)
                if (owner == null || _instances[i].Owner == owner) { Free(_instances[i]); _instances.RemoveAt(i); n++; }
            for (int i = _waiting.Count - 1; i >= 0; i--)
                if (owner == null || _waiting[i].Owner == owner) { _waiting[i].V.Refs--; _waiting.RemoveAt(i); n++; }
            return n;
        }

        /// <summary>Frees everything (another city loads, the system goes).</summary>
        public static void ClearAll()
        {
            Clear(null);
            while (_todo.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
            while (_done.TryDequeue(out _)) { }
            foreach (var v in _variants.Values) FreeVariant(v);
            _variants.Clear();
            foreach (var m in _meshes.Values) if (m != null) UnityEngine.Object.Destroy(m);
            _meshes.Clear();
        }

        private static void Free(Instance i)
        {
            if (i.Go != null) UnityEngine.Object.Destroy(i.Go);
            i.Go = null; i.Renderer = null;
            if (i.V != null) i.V.Refs--;
        }

        private static void FreeVariant(Variant v)
        {
            if (v.Mat != null) UnityEngine.Object.Destroy(v.Mat);
            if (v.Tex != null) foreach (var t in v.Tex) if (t != null) UnityEngine.Object.Destroy(t);
            v.Mat = null; v.Tex = null; v.Bytes = 0; v.State = 0;
        }

        // ───────────── workers ─────────────

        private static void Wake()
        {
            int max = math.clamp(Environment.ProcessorCount / 4, 1, 3);
            while (true)
            {
                int w = Volatile.Read(ref _workers);
                if (w >= max || _todo.IsEmpty) return;
                if (Interlocked.CompareExchange(ref _workers, w + 1, w) != w) continue;
                new Thread(Work) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = Prefix + " worker" }.Start();
            }
        }

        private static void Work()
        {
            try
            {
                while (_todo.TryDequeue(out var v))
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        string file = Path.Combine(CacheDir, DecalVariants.CacheKey(v.K, v.Index) + ".bc3");
                        if (!ReadCache(file, v))
                        {
                            var b = DecalVariants.Bake(v.K, v.Index);
                            v.W = b.Img.W; v.H = b.Img.H; v.Span = b.Span;
                            v.A = DecalTexture.Bc3Chain(b.Img.Albedo, v.W, v.H, alphaWeighted: true, normal: false);
                            v.N = DecalTexture.Bc3Chain(b.Img.Normal, v.W, v.H, alphaWeighted: false, normal: true);
                            v.M = DecalTexture.Bc3Chain(b.Img.Mask, v.W, v.H, alphaWeighted: false, normal: false);
                            WriteCache(file, v);
                        }
                    }
                    catch (Exception e) { v.Error = e.GetType().Name + ": " + e.Message; }
                    v.Ms = clock.Elapsed.TotalMilliseconds;
                    _done.Enqueue(v);
                    Interlocked.Decrement(ref _queued);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _workers);
                if (!_todo.IsEmpty) Wake();
            }
        }

        private static bool ReadCache(string file, Variant v)
        {
            try
            {
                if (!File.Exists(file)) return false;
                using var r = new BinaryReader(File.OpenRead(file));
                if (r.ReadUInt32() != CacheMagic || r.ReadInt32() != DecalVariants.GeneratorVersion) return false;
                v.W = r.ReadInt32(); v.H = r.ReadInt32(); v.Span = new float2(r.ReadSingle(), r.ReadSingle());
                int size = DecalTexture.Bc3Size(v.W, v.H);
                if (v.W < 4 || v.H < 4 || v.W > 2048 || v.H > 2048) return false;
                v.A = r.ReadBytes(size); v.N = r.ReadBytes(size); v.M = r.ReadBytes(size);
                if (v.M.Length != size) return false;
                v.FromDisk = true;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static void WriteCache(string file, Variant v)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                string tmp = file + ".tmp";
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    w.Write(CacheMagic); w.Write(DecalVariants.GeneratorVersion);
                    w.Write(v.W); w.Write(v.H); w.Write(v.Span.x); w.Write(v.Span.y);
                    w.Write(v.A); w.Write(v.N); w.Write(v.M);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch (Exception) { }   // a pure cache: a failed write only costs a remake next time
        }

        // ───────────── main thread ─────────────

        private static Material Template()
        {
            if (_template == null) _template = AssetDatabase.global?.resources?.materialLibrary?.GetMaterial("DefaultDecal");
            return _template;
        }

        /// <summary>Called every frame by DynamicDecalSystem.</summary>
        public static void Tick(TerrainSystem terrain, bool hide)
        {
            if (_variants.Count == 0 && _instances.Count == 0) return;
            var template = Template();
            if (template == null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed.TotalMilliseconds < UploadBudgetMs && _done.TryDequeue(out var v))
            {
                if (v.Error != null) { v.State = 3; Mod.Log.Warn($"Placemaker decal library: {v.Id} was not made: {v.Error}"); continue; }
                if (!Upload(v, template)) v.State = 3;
            }
            if (_waiting.Count > 0)
            {
                var heights = terrain.GetHeightData(true);
                for (int i = _waiting.Count - 1; i >= 0 && clock.Elapsed.TotalMilliseconds < UploadBudgetMs * 2; i--)
                {
                    var inst = _waiting[i];
                    if (inst.V.State == 1) continue;
                    _waiting.RemoveAt(i);
                    if (inst.V.State != 2) { inst.V.Refs--; continue; }
                    if (float.IsNaN(inst.Y))
                    {
                        float y = TerrainUtils.SampleHeight(ref heights, new float3(inst.Centre.x, 0f, inst.Centre.y));
                        inst.Y = math.isfinite(y) ? y : 0f;
                    }
                    Make(inst);
                    _instances.Add(inst);
                    if (hide || _hidden) inst.Renderer.enabled = false;
                }
            }
            Cull(hide);
            if (Time.frameCount % 300 == 0) Evict();
        }

        private static bool Upload(Variant v, Material template)
        {
            try
            {
                var a = Tex(v.A, v.W, v.H, false); var n = Tex(v.N, v.W, v.H, true); var m = Tex(v.M, v.W, v.H, true);
                v.A = v.N = v.M = null;
                if (a == null || n == null || m == null) { if (a) UnityEngine.Object.Destroy(a); if (n) UnityEngine.Object.Destroy(n); if (m) UnityEngine.Object.Destroy(m); return false; }
                v.Tex = new[] { a, n, m };
                v.Bytes = DecalVariants.GpuBytes(v.W, v.H);
                var mat = new Material(template) { name = Prefix + " " + v.Id, hideFlags = HideFlags.HideAndDontSave };
                Set(mat, "_BaseColorMap", a); Set(mat, "_NormalMap", n); Set(mat, "_MaskMap", m);
                if (mat.HasProperty("_BaseColor")) mat.SetVector("_BaseColor", new Vector4(1f, 1f, 1f, 1f));
                foreach (var f in new[] { "_AffectAlbedo", "_AffectNormal", "_AffectSmoothness", "_AffectAO", "_NormalOpacity", "_MetallicOpacity" })
                    if (mat.HasProperty(f)) mat.SetFloat(f, 1f);
                if (mat.HasProperty("_AffectMetal")) mat.SetFloat("_AffectMetal", 0f);
                mat.SetVector("colossal_TextureArea", new Vector4(0f, 0f, 1f, 1f));
                mat.SetVector("colossal_MeshSize", new Vector4(v.Span.x, BoxHeight, v.Span.y, 0f));
                mat.SetFloat("colossal_DecalLayerMask", math.asfloat((int)v.K.Layers));
                mat.DisableKeyword("ENABLE_VT");
                try { HDMaterial.ValidateMaterial(mat); } catch (Exception) { }
                mat.renderQueue = template.renderQueue + math.clamp(v.K.Order, -50, 50);
                v.Mat = mat;
                v.State = 2;
                return true;
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker decal library: upload of " + v.Id + " failed: " + e.Message); return false; }
        }

        private static void Set(Material m, string name, Texture t) { if (m.HasProperty(name)) m.SetTexture(name, t); }

        private static Texture2D Tex(byte[] bc3, int w, int h, bool linear)
        {
            if (bc3 == null) return null;
            var t = new Texture2D(w, h, TextureFormat.DXT5, true, linear) { name = Prefix, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var raw = t.GetRawTextureData<byte>();
            if (raw.Length != bc3.Length) { UnityEngine.Object.Destroy(t); return null; }
            raw.CopyFrom(bc3);
            t.Apply(false, true);
            return t;
        }

        private static void Make(Instance inst)
        {
            var v = inst.V;
            var size = v.Span * inst.Scale;
            var go = new GameObject(Prefix + " " + v.Id) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.transform.SetPositionAndRotation(new Vector3(inst.Centre.x, inst.Y, inst.Centre.y), Quaternion.Euler(0f, math.degrees(inst.Yaw), 0f));
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = v.Mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            // the instance's own box size and opacity; the variant's textures and material stay shared
            inst.Block = new MaterialPropertyBlock();
            inst.Block.SetVector("colossal_MeshSize", new Vector4(size.x, BoxHeight, size.y, 0f));
            inst.Block.SetVector("_BaseColor", new Vector4(1f, 1f, 1f, inst.Opacity));
            mr.SetPropertyBlock(inst.Block);
            inst.Go = go; inst.Renderer = mr;
            inst.Cull = math.clamp(v.K.Lod * math.max(size.x, size.y), 600f, 2000f);   // parks are looked at from far away
        }

        private static Mesh BoxMesh(float2 size)
        {
            long key = ((long)math.round(size.x * 20f) << 32) | (uint)math.round(size.y * 20f);
            if (_meshes.TryGetValue(key, out var m) && m != null) return m;
            float lx = size.x, wz = size.y, hy = BoxHeight;
            var c = new[]
            {
                new Vector3(-lx, -hy, wz) * 0.5f, new Vector3(lx, -hy, wz) * 0.5f, new Vector3(lx, -hy, -wz) * 0.5f, new Vector3(-lx, -hy, -wz) * 0.5f,
                new Vector3(-lx, hy, wz) * 0.5f, new Vector3(lx, hy, wz) * 0.5f, new Vector3(lx, hy, -wz) * 0.5f, new Vector3(-lx, hy, -wz) * 0.5f,
            };
            var vtx = new[] { c[0], c[1], c[2], c[3], c[7], c[4], c[0], c[3], c[4], c[5], c[1], c[0], c[6], c[7], c[3], c[2], c[5], c[6], c[2], c[1], c[7], c[6], c[5], c[4] };
            var nrm = new Vector3[24];
            var dirs = new[] { Vector3.down, Vector3.left, Vector3.forward, Vector3.back, Vector3.right, Vector3.up };
            for (int i = 0; i < 24; i++) nrm[i] = dirs[i / 4];
            Vector2 a = new Vector2(0, 0), b = new Vector2(1, 0), cc = new Vector2(0, 1), dd = new Vector2(1, 1);
            var uv = new Vector2[24];
            for (int f = 0; f < 6; f++) { uv[f * 4] = dd; uv[f * 4 + 1] = cc; uv[f * 4 + 2] = a; uv[f * 4 + 3] = b; }
            var tri = new[] { 3, 1, 0, 3, 2, 1, 7, 5, 4, 7, 6, 5, 11, 9, 8, 11, 10, 9, 15, 13, 12, 15, 14, 13, 19, 17, 16, 19, 18, 17, 23, 21, 20, 23, 22, 21 };
            m = new Mesh { name = Prefix + " box", hideFlags = HideFlags.HideAndDontSave, vertices = vtx, normals = nrm, uv = uv, triangles = tri };
            m.RecalculateTangents(); m.RecalculateBounds();
            _meshes[key] = m;
            return m;
        }

        private static void Cull(bool hide)
        {
            if (hide != _hidden)
            {
                _hidden = hide;
                foreach (var i in _instances) if (i.Renderer != null) i.Renderer.enabled = !hide;
            }
            var cam = Camera.main;
            if (hide || cam == null || _instances.Count == 0) return;
            float3 p = cam.transform.position;
            // a slice a frame: at most 2000 instances are looked at per frame
            int n = math.min(_instances.Count, 2000);
            for (int k = 0; k < n; k++)
            {
                _cullFrom = (_cullFrom + 1) % _instances.Count;
                var i = _instances[_cullFrom];
                if (i.Renderer == null) continue;
                float d = math.distance(p, new float3(i.Centre.x, i.Y, i.Centre.y));
                float fade = math.saturate((i.Cull - d) / (i.Cull * 0.25f));
                bool on = fade > 0.01f;
                if (i.Renderer.enabled != on) i.Renderer.enabled = on;
                if (on && math.abs(fade - i.Fade) > 0.03f)
                {
                    i.Fade = fade;
                    i.Block.SetVector("_BaseColor", new Vector4(1f, 1f, 1f, i.Opacity * fade));
                    i.Renderer.SetPropertyBlock(i.Block);
                }
            }
        }

        /// <summary>Frees variants nobody uses while the library is over budget, the longest unused first.</summary>
        private static void Evict()
        {
            long total = GpuBytes;
            if (total <= BudgetBytes) return;
            var idle = new List<Variant>();
            foreach (var v in _variants.Values) if (v.Refs <= 0 && v.State == 2) idle.Add(v);
            idle.Sort((x, y) => x.LastUse.CompareTo(y.LastUse));
            int freed = 0;
            foreach (var v in idle)
            {
                if (total <= BudgetBytes * 0.85) break;
                total -= v.Bytes;
                FreeVariant(v);
                freed++;
            }
            if (freed > 0) Mod.Log.Info($"Placemaker decal library: freed {freed} unused variants, {total / 1048576.0:F0} MiB left on the GPU");
        }
    }
}
