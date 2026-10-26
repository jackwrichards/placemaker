// Placemaker neighbourhood polish — the ground paint YardStitch plans (GPL-3.0).
//
// Same engine path as a build's terrain paint (Game/Build/BuildService.Paint.cs, docs/TERRAIN-PAINT.md): Core PaintRaster
// turns the planned paints into square strength masks on a worker thread, and each mask is one
// TerrainMaterialSystem.ApplyBrush call with the brush prefab's texture swapped for the mask (one stamp per frame).
// Painting keeps the max per splatmap texel, so painting the same blend again changes nothing.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPaintSystem : GameSystemBase
    {
        public static readonly List<PlannedPaint> Queue = new List<PlannedPaint>();
        public static int TotalStamps;

        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private readonly Dictionary<int, Entity> _tools = new Dictionary<int, Entity>();
        private Entity _brush = Entity.Null;
        private Task<List<PaintStamp>> _job;
        private List<PaintStamp> _stamps;
        private int _cursor;
        private bool _unavailable;
        private readonly List<UnityEngine.Texture2D> _textures = new List<UnityEngine.Texture2D>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // (its main thread time, for the game runner's outgrowFrames)
            long timed = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateTimed(); }
            finally { NeighbourPolishSystem.NoteSystem(2, timed); }
        }

        private void UpdateTimed()
        {
            try
            {
                if (_stamps != null)
                {
                    if (_cursor < _stamps.Count) { Apply(_stamps[_cursor++]); TotalStamps++; return; }
                    foreach (var t in _textures) UnityEngine.Object.Destroy(t);
                    _textures.Clear();
                    _stamps = null;
                    return;
                }
                if (_job != null)
                {
                    if (!_job.IsCompleted) return;
                    _stamps = _job.IsFaulted || _job.IsCanceled ? null : _job.Result;
                    if (_job.IsFaulted) Mod.Log.Warn("Placemaker neighbourhood paint raster failed: " + _job.Exception?.GetBaseException().Message);
                    _job = null;
                    _cursor = 0;
                    return;
                }
                if (Queue.Count == 0 || _unavailable) { Queue.Clear(); return; }
                if (!Resolve()) { _unavailable = true; Mod.Log.Warn("Placemaker neighbourhood paint: no terrain material brush in this game."); return; }
                var paints = new List<PlannedPaint>(Queue);
                Queue.Clear();
                float texel = 3.5f;
                try { var area = _terrain.playableArea; if (area.x > 1f) texel = area.x / 4096f; } catch (Exception) { }
                var opts = new PaintRasterOptions { Cell = texel * 0.5f, Align = texel, TileCells = 512, MaxStamps = 48 };
                _job = Task.Run(() => PaintRaster.Build(paints, opts));
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker neighbourhood paint failed: " + e.Message);
                _stamps = null;
                _job = null;
            }
        }

        private void Apply(PaintStamp stamp)
        {
            if (!_tools.TryGetValue(math.clamp(stamp.Layer, 0, 4), out var tool)) return;
            if (!_prefabs.TryGetPrefab<BrushPrefab>(_brush, out var brushPrefab) || brushPrefab == null) return;
            var materials = World.GetOrCreateSystemManaged<global::Game.Rendering.TerrainMaterialSystem>();
            int n = stamp.N;
            var tex = new UnityEngine.Texture2D(n, n, UnityEngine.TextureFormat.RGBA32, false, true) { wrapMode = UnityEngine.TextureWrapMode.Clamp, filterMode = UnityEngine.FilterMode.Bilinear, name = "PlacemakerPolishMask" };
            var bytes = new byte[n * n * 4];
            for (int i = 0; i < n * n; i++)
            {
                byte v = (byte)math.clamp((int)math.round(stamp.Mask[i] * 255f), 0, 255);
                bytes[i * 4] = bytes[i * 4 + 1] = bytes[i * 4 + 2] = bytes[i * 4 + 3] = v;
            }
            tex.LoadRawTextureData(bytes);
            tex.Apply(false, false);
            _textures.Add(tex);
            var heights = _terrain.GetHeightData(true);
            var c = stamp.Center;
            float h = TerrainUtils.SampleHeight(ref heights, new float3(c.x, 0f, c.y));
            var brush = new Brush
            {
                m_Tool = tool, m_Position = new float3(c.x, h, c.y), m_Target = new float3(c.x, h, c.y), m_Start = new float3(c.x, h, c.y),
                m_Angle = 0f, m_Size = stamp.Size, m_Strength = stamp.Erase && stamp.Layer > 0 ? 0f : 1f, m_Opacity = 1f,
            };
            var original = brushPrefab.m_Texture;
            try { brushPrefab.m_Texture = tex; materials.ApplyBrush(brush, _brush); }
            finally { brushPrefab.m_Texture = original; }
        }

        /// <summary>The map's terrain material tools (Extra1..4, Clear) and a soft brush (as BuildService resolves them).</summary>
        private bool Resolve()
        {
            if (_tools.Count > 0 && _brush != Entity.Null) return true;
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                string brushName = "";
                foreach (var e in all)
                {
                    if (!_prefabs.TryGetPrefab<PrefabBase>(e, out var prefab) || prefab == null || !prefab.isBuiltin) continue;   // vanilla tools only
                    if (prefab is TerraformingPrefab tf && tf.m_Target == TerraformingTarget.Material && tf.TryGet<TerrainMaterialProperties>(out var mat) && mat != null)
                    {
                        int layer = mat.m_Type == TerrainMaterialType.None ? 0 : mat.m_Type == TerrainMaterialType.Extra1 ? 1 : mat.m_Type == TerrainMaterialType.Extra2 ? 2
                            : mat.m_Type == TerrainMaterialType.Extra3 ? 3 : mat.m_Type == TerrainMaterialType.Extra4 ? 4 : -1;
                        if (layer >= 0 && !_tools.ContainsKey(layer)) _tools[layer] = e;
                    }
                    else if (prefab is BrushPrefab && (_brush == Entity.Null
                        || (prefab.name.IndexOf("soft", StringComparison.OrdinalIgnoreCase) >= 0 && brushName.IndexOf("soft", StringComparison.OrdinalIgnoreCase) < 0)))
                    {
                        _brush = e;
                        brushName = prefab.name;
                    }
                }
            }
            return _tools.Count > 0 && _brush != Entity.Null;
        }
    }
}
