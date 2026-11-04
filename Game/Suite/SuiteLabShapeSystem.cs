// Game runner lab (exploration, not a Placemaker feature): the "custom assets on the fly" spike. Turns a mesh generated
// in code into a real game object the game's own renderer draws (lit, shadowed, culled like any prop), the way
// ExtraAssetsImporter builds its decal boxes, but as a solid 3D shape with a vanilla material:
//   mesh (packed like the game's importer packs it: octahedral normals/tangents when the material asks for them)
//   â†’ GeometryAsset saved as a file in our own registered asset database (the renderer streams geometry from files and
//     unloads it again; an in-memory geometry would stall or vanish)
//   â†’ RenderPrefab (that geometry + a vanilla building SurfaceAsset, shared, so virtual texturing still works)
//   â†’ StaticObjectPrefab, added in PrefabUpdate before PrefabInitializeSystem, used a few frames later.
// Registered only while the game runner is active (SuiteGate). The database folder is wiped at every start.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Colossal.AssetPipeline.Native;
using Colossal.IO.AssetDatabase;
using Colossal.Mathematics;
using Colossal.PSI.Common;
using Colossal.PSI.Environment;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace Placemaker.Game.Suite
{
    /// <summary>The lab's own file-backed asset database (ModsData/Placemaker/suite/lab-shapes).</summary>
    public readonly struct LabShapeDatabase : IAssetDatabaseDescriptor<LabShapeDatabase>
    {
        public static string RootPath => Path.Combine(EnvPath.kUserDataPath, "ModsData", "Placemaker", "suite", "lab-shapes");
        public string name => "PlacemakerLab";
        public bool canWriteSettings => false;
        public DlcId dlcId => DlcId.Virtual;
        public IAssetFactory assetFactory => DefaultAssetFactory.instance;
        public IDataSourceProvider dataSourceProvider => new FileSystemDataSource(name, RootPath, assetFactory, 0L);
        public bool Equals(LabShapeDatabase other) => true;
        public override bool Equals(object obj) => obj is LabShapeDatabase;
        public override int GetHashCode() => typeof(LabShapeDatabase).GetHashCode();
    }

    public sealed partial class SuiteLabShapeSystem : GameSystemBase
    {
        internal sealed class Request
        {
            public string Name;
            public float2[] Ring;
            public float Height;
            public string Material;   // substring of "<render prefab>#<k> <template>", or null: the first suitable building surface
            public string CopyOf;     // instead of a shape: a renamed copy of this static object prefab (same meshes), named Name
            // results
            public bool Done;
            public string Error;
            public Entity Prefab = Entity.Null;
            public int ReadyFrame;
            public readonly Dictionary<string, object> Info = new Dictionary<string, object>();
        }

        internal sealed class SurfaceCandidate
        {
            public string Label, Template;
            public SurfaceAsset Surface;
            public bool Octo, Vt;
            public string[] Keywords;
        }

        private PrefabSystem _prefabs;
        private Task _dbTask;
        private string _dbError;
        private readonly Queue<Request> _queue = new Queue<Request>();
        private List<SurfaceCandidate> _candidates;
        private readonly List<Mesh> _keep = new List<Mesh>();

        public bool DatabaseReady => _dbTask != null && _dbTask.IsCompleted && !_dbTask.IsFaulted;
        public string DatabaseState => _dbError ?? (_dbTask == null ? "not started" : _dbTask.IsFaulted ? "failed: " + _dbTask.Exception?.GetBaseException().Message : _dbTask.IsCompleted ? "ready" : "registering");

        internal void Enqueue(Request r) => _queue.Enqueue(r);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            try
            {
                if (Directory.Exists(LabShapeDatabase.RootPath)) Directory.Delete(LabShapeDatabase.RootPath, true);
                Directory.CreateDirectory(LabShapeDatabase.RootPath);
                var db = AssetDatabase<LabShapeDatabase>.instance;
                _dbTask = AssetDatabase.global.RegisterDatabase(db).ContinueWith(t =>
                {
                    if (t.IsFaulted) throw t.Exception;
                    db.PopulateFromDataSource(false, CancellationToken.None, new Colossal.TaskProgress(_ => { })).Wait();
                });
                Mod.Log.Info("Placemaker lab: shape database registering at " + LabShapeDatabase.RootPath);
            }
            catch (Exception e)
            {
                _dbError = "failed: " + e.Message;
                Mod.Log.Warn("Placemaker lab: shape database not registered: " + e);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_queue.Count == 0 || !DatabaseReady) return;
            var r = _queue.Dequeue();
            try { Make(r); }
            catch (Exception e)
            {
                r.Error = e.GetType().Name + ": " + e.Message;
                Mod.Log.Warn("Placemaker lab: shape " + r.Name + " failed: " + e);
            }
            r.Done = true;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ materials â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static readonly string[] Unsuitable = { "EMISSIVE", "INTERIOR", "WIND", "WINDOW", "DECAL", "TRANSPAR", "ALPHA", "GLASS", "VERTEXCOLOR", "VEGETATION", "COLORMASK_FROM_VERTEX" };

        /// <summary>Surfaces of vanilla buildings, described (template, keywords, VT); suitable ones first.</summary>
        internal List<SurfaceCandidate> Candidates()
        {
            if (_candidates != null) return _candidates;
            var list = new List<SurfaceCandidate>();
            var seen = new HashSet<SurfaceAsset>();
            var lib = AssetDatabase.global.resources.materialLibrary;
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BuildingData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    if (list.Count >= 400) break;
                    if (!_prefabs.TryGetPrefab<BuildingPrefab>(e, out var b) || b == null || b.m_Meshes == null) continue;
                    foreach (var mi in b.m_Meshes)
                    {
                        if (!(mi?.m_Mesh is RenderPrefab rp)) continue;
                        for (int k = 0; k < rp.materialCount; k++)
                        {
                            SurfaceAsset s;
                            try { s = rp.GetSurfaceAsset(k); } catch (Exception) { continue; }
                            if (s == null || !seen.Add(s)) continue;
                            try { s.LoadProperties(useVT: true); } catch (Exception) { continue; }   // our reference keeps it loaded
                            string template = "";
                            try { template = lib.GetMaterialDescription(s.materialTemplateHash)?.m_Material?.name ?? ""; } catch (Exception) { }
                            var kw = s.keywords?.ToArray() ?? Array.Empty<string>();
                            bool vt = false;
                            try { vt = s.isVTMaterial; } catch (Exception) { }
                            list.Add(new SurfaceCandidate
                            {
                                Label = rp.name + "#" + k, Template = template, Surface = s, Keywords = kw, Vt = vt,
                                Octo = kw.Contains("_TANGENTSPACE_OCTO"),
                            });
                        }
                    }
                }
            }
            _candidates = list.OrderBy(c => Suitable(c) ? 0 : 1).ToList();
            return _candidates;
        }

        internal static bool Suitable(SurfaceCandidate c) =>
            !c.Keywords.Any(k => Unsuitable.Any(u => k.ToUpperInvariant().Contains(u))) && !Unsuitable.Any(u => c.Template.ToUpperInvariant().Contains(u));

        private SurfaceCandidate Pick(string want)
        {
            var c = Candidates();
            if (!string.IsNullOrEmpty(want))
            {
                var hit = c.FirstOrDefault(x => (x.Label + " " + x.Template).IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) return hit;
            }
            return c.FirstOrDefault(Suitable) ?? c.FirstOrDefault();
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ one shape â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void Make(Request r)
        {
            if (r.CopyOf != null) { MakeCopy(r); return; }
            var surf = Pick(r.Material);
            if (surf == null) { r.Error = "no building surface found to borrow"; return; }
            r.Info["surface"] = surf.Label;
            r.Info["template"] = surf.Template;
            r.Info["keywords"] = string.Join(" ", surf.Keywords);
            r.Info["vt"] = surf.Vt;
            r.Info["octahedral"] = surf.Octo;

            var raw = SuiteLabMesh.Extrude(r.Ring, r.Height, 0f, r.Name);
            if (raw == null) { r.Error = "the outline could not be triangulated"; return; }
            var mesh = Pack(raw, surf.Octo, r.Name);
            UnityEngine.Object.Destroy(raw);
            _keep.Add(mesh);
            r.Info["vertices"] = mesh.vertexCount;
            r.Info["triangles"] = mesh.triangles.Length / 3;

            var db = AssetDatabase<LabShapeDatabase>.instance;
            var guid = Colossal.Hash128.CreateGuid(r.Name);
            var geo = db.AddAsset<GeometryAsset>(AssetDataPath.Create("Shapes", r.Name, EscapeStrategy.None), guid);
            geo.SetData(new[] { mesh });
            geo.Save();
            r.Info["geometryFile"] = geo.ToString();
            geo.Unload();

            var b = mesh.bounds;
            var render = ScriptableObject.CreateInstance<RenderPrefab>();
            render.name = r.Name + " mesh";
            render.geometryAsset = geo;
            render.surfaceAssets = new[] { surf.Surface };
            render.bounds = new Bounds3(b.min, b.max);
            render.meshCount = 1;
            render.vertexCount = mesh.vertexCount;
            render.indexCount = (int)mesh.GetIndexCount(0);
            render.surfaceArea = 0f;
            render.isImpostor = false;
            render.manualVTRequired = false;
            if (!Placemaker.Game.Assets.AssetCatalogSystem.AddOwnPrefab(_prefabs, render)) { r.Error = "AddPrefab refused the render prefab"; return; }

            var obj = ScriptableObject.CreateInstance<StaticObjectPrefab>();
            obj.name = r.Name;
            obj.m_Meshes = new[] { new ObjectMeshInfo { m_Mesh = render, m_Position = float3.zero, m_Rotation = quaternion.identity } };
            if (!Placemaker.Game.Assets.AssetCatalogSystem.AddOwnPrefab(_prefabs, obj)) { r.Error = "AddPrefab refused the object prefab"; return; }
            r.Prefab = _prefabs.GetEntity(obj);
            r.ReadyFrame = UnityEngine.Time.frameCount + 3;
            Mod.Log.Info($"Placemaker lab: made shape '{r.Name}': {mesh.vertexCount} vertices, bounds {b.size}, surface {surf.Label} ({surf.Template}, VT {surf.Vt}, octahedral {surf.Octo}), keywords [{string.Join(" ", surf.Keywords)}]");
        }

        /// <summary>
        /// A static object prefab under another name, sharing the original's meshes (CreateInstance + AddComponentFrom, as
        /// DecalOpacitySystem made its copies). Used to put an object that only this game knows into a save (issue #2).
        /// </summary>
        private void MakeCopy(Request r)
        {
            StaticObjectPrefab original = null;
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ObjectGeometryData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                    if (_prefabs.TryGetPrefab<StaticObjectPrefab>(e, out var p) && p != null && p.name == r.CopyOf) { original = p; break; }
            if (original == null || original.m_Meshes == null || original.m_Meshes.Length == 0) { r.Error = "no static object prefab '" + r.CopyOf + "' with meshes"; return; }
            var obj = (StaticObjectPrefab)ScriptableObject.CreateInstance(original.GetType());
            obj.name = r.Name;
            obj.m_Meshes = (ObjectMeshInfo[])original.m_Meshes.Clone();
            obj.m_Circular = original.m_Circular;
            foreach (var component in original.components)
                if (component != null && !(component is UIObject) && !(component is ObsoleteIdentifiers)) obj.AddComponentFrom(component);
            if (!Placemaker.Game.Assets.AssetCatalogSystem.AddOwnPrefab(_prefabs, obj)) { r.Error = "AddPrefab refused the copy"; return; }
            r.Prefab = _prefabs.GetEntity(obj);
            r.ReadyFrame = UnityEngine.Time.frameCount + 3;
            r.Info["copyOf"] = original.name;
            Mod.Log.Info($"Placemaker lab: made '{r.Name}', a copy of {original.GetType().Name} '{original.name}'");
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ vertex packing (as Colossal.AssetPipeline's AttributePackingPostProcessor) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [StructLayout(LayoutKind.Sequential)]
        private struct OctoVertex { public short nx, ny; public float t; public float u, v; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PlainVertex { public float3 n; public float4 t; public float u, v; }

        private static Mesh Pack(Mesh src, bool octo, string name)
        {
            var pos = src.vertices;
            var nrm = src.normals;
            var tan = src.tangents;
            var uv = src.uv;
            int n = pos.Length;
            var mesh = new Mesh { name = name };
            var attrs = new List<VertexAttributeDescriptor> { new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0) };
            if (octo)
            {
                attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm16, 2, 1));
                attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 1, 1));
            }
            else
            {
                attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1));
                attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1));
            }
            attrs.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1));
            mesh.SetVertexBufferParams(n, attrs.ToArray());
            var p3 = new float3[n];
            for (int i = 0; i < n; i++) p3[i] = pos[i];
            mesh.SetVertexBufferData(p3, 0, 0, n, 0);
            if (octo)
            {
                var n3 = new float3[n];
                var t4 = new float4[n];
                for (int i = 0; i < n; i++) { n3[i] = nrm[i]; t4[i] = tan[i]; }
                var packedN = new short[n * 2];
                var packedT = new float[n];
                var h1 = GCHandle.Alloc(n3, GCHandleType.Pinned);
                var h2 = GCHandle.Alloc(packedN, GCHandleType.Pinned);
                var h3 = GCHandle.Alloc(t4, GCHandleType.Pinned);
                var h4 = GCHandle.Alloc(packedT, GCHandleType.Pinned);
                try
                {
                    NativeMath.ArrayNormalsToOctahedral(h1.AddrOfPinnedObject(), n, h2.AddrOfPinnedObject());
                    NativeMath.ArrayTangentsToOctahedral(h3.AddrOfPinnedObject(), n, h4.AddrOfPinnedObject());
                }
                finally { h1.Free(); h2.Free(); h3.Free(); h4.Free(); }
                var s1 = new OctoVertex[n];
                for (int i = 0; i < n; i++) s1[i] = new OctoVertex { nx = packedN[2 * i], ny = packedN[2 * i + 1], t = packedT[i], u = uv[i].x, v = uv[i].y };
                mesh.SetVertexBufferData(s1, 0, 0, n, 1);
            }
            else
            {
                var s1 = new PlainVertex[n];
                for (int i = 0; i < n; i++) s1[i] = new PlainVertex { n = nrm[i], t = tan[i], u = uv[i].x, v = uv[i].y };
                mesh.SetVertexBufferData(s1, 0, 0, n, 1);
            }
            var idx = src.triangles;
            var i16 = new ushort[idx.Length];
            for (int i = 0; i < idx.Length; i++) i16[i] = (ushort)idx[i];
            mesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(i16, 0, 0, idx.Length);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length, MeshTopology.Triangles));
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
