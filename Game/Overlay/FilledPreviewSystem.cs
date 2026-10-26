// Placemaker — filled polygons for the preview (selected area, planned surfaces/areas, hovered block).
//
// CS2's overlay buffer can only draw curves and circles, and AreaBatchSystem skips Temp areas
// (RESEARCH-NOTES rule 9), so filled previews are our own meshes drawn with the game's overlay
// material. Ported from ParkingLotTool's ParkingLotFlaechennetz.cs (KruemelGames, GPL-3.0):
// triangulate each ring (Core EarClip), subdivide triangles to ≤ 8 m edges so they follow the
// terrain, and draw one instanced indirect call per layer with the material's custom-mesh buffer
// (colours in linear space). Meshes are rebuilt only when a layer's signature changes.

using System;
using System.Collections.Generic;
using Game;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Placemaker.Core.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Overlay
{
    public sealed partial class FilledPreviewSystem : GameSystemBase
    {
        private const float Hover = 0.15f;
        private const float MaxEdge = 8f;
        private const int MaxTriangles = 400000;

        private sealed class Layer
        {
            public Mesh Mesh;
            public Bounds Bounds;
            public Material Material;
            public ComputeBuffer Data;
            public ComputeBuffer Args;
            public readonly uint[] ArgValues = new uint[5];
            public long Signature = long.MinValue;
            public Color Color;
            public float Priority = float.NaN;
            public bool Used;
        }

        private readonly Dictionary<string, Layer> _layers = new Dictionary<string, Layer>(StringComparer.Ordinal);
        private TerrainSystem _terrain;
        private Material _template;
        private int _templateAttempts;
        private bool _reported;
        private readonly List<float2> _stack = new List<float2>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        private bool EnsureTemplate()
        {
            if (_template != null) return true;
            if (_templateAttempts > 30) return false;
            _templateAttempts++;
            try
            {
                var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
                var query = GetEntityQuery(ComponentType.ReadOnly<OverlayConfigurationData>());
                if (query.IsEmptyIgnoreFilter) return false;
                var config = prefabs.GetSingletonPrefab<OverlayConfigurationPrefab>(query);
                if (config == null || config.m_SolidObjectMaterial == null) return false;
                _template = config.m_SolidObjectMaterial;
                return true;
            }
            catch (Exception e)
            {
                if (!_reported) { _reported = true; Mod.Log.Warn("Placemaker: overlay material unavailable, filled previews off (" + e.Message + ")"); }
                _templateAttempts = 999;
                return false;
            }
        }

        /// <summary>Marks the start of a frame's layer updates; layers not set again are hidden by EndFrame.</summary>
        public void BeginFrame()
        {
            foreach (var l in _layers.Values) l.Used = false;
        }

        /// <summary>
        /// Shows <paramref name="rings"/> (CCW or CW, simple) filled with <paramref name="color"/> under
        /// <paramref name="key"/>. <paramref name="signature"/> must change whenever the rings change.
        /// <paramref name="convex"/>: every ring is small and convex (discs, bars, stalls): fanned with shared corners, no ear
        /// clipping (the site plan's 15,000 crowns and stall lines took 290 ms the slow way).
        /// </summary>
        public void SetLayer(string key, IReadOnlyList<float3[]> rings, Color color, float priority, long signature, bool convex = false)
        {
            if (!EnsureTemplate()) return;
            if (!_layers.TryGetValue(key, out var layer)) _layers[key] = layer = new Layer();
            layer.Used = true;
            if (layer.Mesh == null || layer.Signature != signature)
            {
                layer.Signature = signature;
                BuildMesh(layer, rings, convex);
            }
            if (layer.Mesh == null) return;
            ApplyStyle(layer, color, priority);
        }

        public void EndFrame()
        {
            foreach (var l in _layers.Values)
                if (!l.Used && l.Mesh != null && l.Signature != long.MinValue)
                {
                    // Forget the signature too: the same rings shown again later must rebuild the (now empty) mesh.
                    l.Mesh.Clear();
                    l.Signature = long.MinValue;
                }
        }

        public void ClearAll()
        {
            foreach (var l in _layers.Values)
            {
                l.Used = false;
                if (l.Mesh != null) l.Mesh.Clear();
                l.Signature = long.MinValue;
            }
        }

        public static long Signature(IReadOnlyList<float3[]> rings)
        {
            unchecked
            {
                long h = rings?.Count ?? 0;
                if (rings == null) return h;
                foreach (var r in rings)
                {
                    if (r == null) { h = h * 31 + 7; continue; }
                    h = h * 31 + r.Length;
                    foreach (var p in r)
                    {
                        h = h * 31 + (long)math.round(p.x * 100f);
                        h = h * 31 + (long)math.round(p.z * 100f);
                    }
                }
                return h;
            }
        }

        private void ApplyStyle(Layer layer, Color color, float priority)
        {
            layer.Material ??= new Material(_template) { hideFlags = HideFlags.HideAndDontSave, name = "Placemaker fill" };
            if (layer.Data != null && layer.Color.Equals(color) && math.abs(layer.Priority - priority) < 0.001f) return;
            layer.Color = color;
            layer.Priority = priority;
            var data = new OverlayRenderSystem.CustomMeshdData
            {
                m_Matrix = Matrix4x4.identity,
                m_InverseMatrix = Matrix4x4.identity,
                m_FillColor = color.linear,
                m_Size = new float2(1f, 1f),
                m_CustomMeshType = (int)OverlayRenderSystem.CustomMeshType.Plane,
            };
            layer.Data ??= new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf(typeof(OverlayRenderSystem.CustomMeshdData)));
            layer.Data.SetData(new[] { data });
            layer.Material.SetBuffer(Shader.PropertyToID("colossal_OverlayCustomMeshBuffer"), layer.Data);
            // Vanilla surfaces draw at −100…−95; above them so the preview is visible over existing areas.
            layer.Material.SetFloat("_TransparentSortPriority", priority);
        }

        private void BuildMesh(Layer layer, IReadOnlyList<float3[]> rings, bool convex = false)
        {
            var heights = _terrain.GetHeightData(false);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
            if (rings != null)
                foreach (var ring in rings)
                {
                    if (ring == null || ring.Length < 3 || tris.Count / 3 >= MaxTriangles) continue;
                    if (convex && Small(ring))
                    {
                        // a fan over the ring's own corners at the ground height of its middle (one sample: a stall line or a
                        // crown is too small for the slope to show)
                        int b = verts.Count;
                        float2 mid = float2.zero;
                        foreach (var p in ring) mid += p.xz;
                        var g = OnTerrain(mid / ring.Length, ref heights, ref min, ref max);
                        foreach (var p in ring)
                        {
                            var v = new Vector3(p.x, g.y, p.z);
                            min = math.min(min, (float3)v);
                            max = math.max(max, (float3)v);
                            verts.Add(v);
                        }
                        for (int i = 1; i + 1 < ring.Length; i++) { tris.Add(b); tris.Add(b + i); tris.Add(b + i + 1); }
                        continue;
                    }
                    var flat = Poly.EnsureCcw(Poly.To2D(ring));
                    if (!EarClip.Triangulate(flat, out var triangles))
                    {
                        // Ear clipping failed (self-touching ring): a fan still shows roughly where it is.
                        triangles = new List<int3>();
                        for (int i = 1; i + 1 < flat.Length; i++) triangles.Add(new int3(0, i, i + 1));
                    }
                    foreach (var t in triangles)
                    {
                        if (tris.Count / 3 >= MaxTriangles) break;
                        Subdivide(flat[t.x], flat[t.y], flat[t.z], ref heights, verts, tris, ref min, ref max);
                    }
                }
            if (verts.Count == 0)
            {
                if (layer.Mesh != null) layer.Mesh.Clear();
                return;
            }
            layer.Mesh ??= new Mesh { hideFlags = HideFlags.HideAndDontSave };
            layer.Mesh.Clear();
            layer.Mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            layer.Mesh.SetVertices(verts);
            layer.Mesh.SetTriangles(tris, 0);
            layer.Mesh.RecalculateBounds();
            var size = math.max(max - min, new float3(1f)) + 8f;
            layer.Bounds = new Bounds((min + max) * 0.5f, size);
        }

        private void Subdivide(float2 a, float2 b, float2 c, ref TerrainHeightData heights, List<Vector3> verts, List<int> tris, ref float3 min, ref float3 max)
        {
            _stack.Clear();
            _stack.Add(a); _stack.Add(b); _stack.Add(c);
            while (_stack.Count >= 3)
            {
                int n = _stack.Count;
                float2 p = _stack[n - 3], q = _stack[n - 2], r = _stack[n - 1];
                _stack.RemoveRange(n - 3, 3);
                if (tris.Count / 3 >= MaxTriangles) return;
                float pq = math.distance(p, q), qr = math.distance(q, r), rp = math.distance(r, p);
                if (math.max(pq, math.max(qr, rp)) > MaxEdge)
                {
                    if (pq >= qr && pq >= rp) { var m = (p + q) * 0.5f; Push(p, m, r); Push(m, q, r); }
                    else if (qr >= rp) { var m = (q + r) * 0.5f; Push(p, q, m); Push(p, m, r); }
                    else { var m = (r + p) * 0.5f; Push(p, q, m); Push(m, q, r); }
                    continue;
                }
                int i = verts.Count;
                verts.Add(OnTerrain(p, ref heights, ref min, ref max));
                verts.Add(OnTerrain(q, ref heights, ref min, ref max));
                verts.Add(OnTerrain(r, ref heights, ref min, ref max));
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
        }

        /// <summary>Spans less than two subdivision edges: flat enough to follow the ground with its corners only.</summary>
        private static bool Small(float3[] ring)
        {
            float2 lo = ring[0].xz, hi = lo;
            foreach (var p in ring) { lo = math.min(lo, p.xz); hi = math.max(hi, p.xz); }
            return math.cmax(hi - lo) <= MaxEdge * 2f;
        }

        private void Push(float2 a, float2 b, float2 c)
        {
            _stack.Add(a); _stack.Add(b); _stack.Add(c);
        }

        private static Vector3 OnTerrain(float2 p, ref TerrainHeightData heights, ref float3 min, ref float3 max)
        {
            var w = new float3(p.x, 0f, p.y);
            w.y = TerrainUtils.SampleHeight(ref heights, w) + Hover;
            min = math.min(min, w);
            max = math.max(max, w);
            return new Vector3(w.x, w.y, w.z);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            foreach (var l in _layers.Values)
            {
                if (!l.Used || l.Mesh == null || l.Material == null || l.Mesh.vertexCount == 0) continue;
                try { Draw(l); }
                catch (Exception e)
                {
                    if (!_reported) { _reported = true; Mod.Log.Warn("Placemaker: filled preview draw failed: " + e.Message); }
                }
            }
        }

        private static void Draw(Layer l)
        {
            bool fresh = l.Args == null;
            l.Args ??= new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            uint index = l.Mesh.GetIndexCount(0), start = l.Mesh.GetIndexStart(0), baseVertex = l.Mesh.GetBaseVertex(0);
            if (fresh || l.ArgValues[0] != index || l.ArgValues[2] != start || l.ArgValues[3] != baseVertex)
            {
                l.ArgValues[0] = index; l.ArgValues[1] = 1; l.ArgValues[2] = start; l.ArgValues[3] = baseVertex; l.ArgValues[4] = 0;
                l.Args.SetData(l.ArgValues);
            }
            Graphics.DrawMeshInstancedIndirect(l.Mesh, 0, l.Material, l.Bounds, l.Args, 0, null,
                UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, null);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            foreach (var l in _layers.Values)
            {
                l.Data?.Release();
                l.Args?.Release();
                if (l.Mesh != null) UnityEngine.Object.Destroy(l.Mesh);
                if (l.Material != null) UnityEngine.Object.Destroy(l.Material);
            }
            _layers.Clear();
            base.OnDestroy();
        }

        [Preserve]
        public FilledPreviewSystem() { }
    }
}
