// Placemaker — terrain texture painting (PlannedPaint) (GPL-3.0).
//
// CS2 (decompiled 1.6.2f1): the vanilla "Paint texture" tool is TerrainToolSystem with a TerraformingPrefab whose
// m_Target = TerraformingTarget.Material and whose TerrainMaterialProperties.m_Type is Extra1..Extra4 (None =
// "Texture Eraser"); prefabs "Terrain Material Extra 1..4" / "Terrain Material Clear", on vanilla maps Dry grass,
// Lush grass, Dirt, Sand. On Apply, ApplyBrushesSystem calls the public TerrainMaterialSystem.ApplyBrush(Brush,
// brushPrefab), which dispatches the PaintSplatmap compute shader ("PaintDither"/"EraseDither") on the user
// splatmaps (4096² R8G8 over the playable area, 1024² over the world; both saved with the city) with
// _BrushTexture = BrushPrefab.m_Texture mapped on the square brush.m_Position ± m_Size/2 and _BrushOpacity =
// brush.m_Strength. So a per-texel strength field is ONE call per square stamp with a generated mask texture.
//
// What the build does (s13): Core PaintRaster turns every PlannedPaint (ring + feather + noise + gradient) into
// world-aligned square stamps (one grid per material layer, merged by max) on a worker thread; the build then
// applies at most one stamp per frame (each ≤ 512² texels, a 1 MB RGBA32 upload) with the resolved brush prefab's
// m_Texture swapped for the stamp mask during the call. Exactly once per build: the Paint step runs only from the
// terraform step, never on retries (ResetForRetry → EmitNets), and _paintStarted guards re-entry. Removing the
// group wipes its paint over the outline (BuildService.Unpaint, p1-bulldoze). If the raster fails, the old per-ring brush dabs run.
//
// s33 (PaintSplatmap DXBC disassembled, docs/TERRAIN-PAINT.md): the user splatmap keeps one 4-bit weight per material
// (R8 = Extra1 | Extra2 << 4, G8 = Extra3 | Extra4 << 4); PaintDither = max(old, mask.a × strength) on the tool's
// channel, quantised with a 2×2 checkerboard (0.25 / 0.75), EraseDither = min(old, 1 − mask.a) on the tool's channels
// (Clear: all). Repainting the same value is a no-op. The brush maps u = +x, v = +z at angle 0 (no calibration needed).
// A stamp marked Erase with a material tool is applied at strength 0, which selects EraseDither for that channel only.


using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private const float PaintBrushRadius = 6f;
        private readonly Dictionary<int, Entity> _paintTools = new Dictionary<int, Entity>();

        private bool HasPaint()
        {
            var plan = _request?.Plan;
            if (plan == null) return false;
            foreach (var l in plan.Layers) if (l?.Paint != null && l.Paint.Count > 0) return true;
            return false;
        }

        private Step AfterTerraform() => HasPaint() ? Step.Paint : Step.EmitNets;

        private struct PaintDab { public Entity Tool; public float3 At; public float Strength; }
        private readonly List<PaintDab> _paintDabs = new List<PaintDab>();
        private int _paintCursor;
        // Frame budget for the fallback dabs (a big area can need thousands).
        private const int MaxPaintDabsPerFrame = 96;

        // ── direct stamps ──
        private const int PaintStampTexels = 512;            // stamp side cap: ≤ 1 MB upload per frame
        private const int PaintMaxStamps = 48;
        private const double PaintRasterTimeoutSeconds = 15.0;
        private const int PaintSettleFrames = 2;              // GPU work done before the textures go
        private Task<List<PaintStamp>> _paintJob;
        private CancellationTokenSource _paintCancel;
        private DateTime _paintJobStart;
        private List<PaintStamp> _paintStamps;
        private int _paintStampCursor, _paintLastApplyFrame;
        private bool _paintStarted, _paintDabsMode;
        private readonly List<UnityEngine.Texture2D> _paintTextures = new List<UnityEngine.Texture2D>();

        /// <summary>Called from ResetTerraform (every transaction reset / cancel).</summary>
        private void ResetPaint()
        {
            try { _paintCancel?.Cancel(); } catch (Exception) { }
            _paintCancel?.Dispose();
            _paintCancel = null;
            _paintJob = null;
            _paintStamps = null;
            _paintStampCursor = 0;
            _paintStarted = false;
            _paintDabsMode = false;
            DestroyPaintTextures();
        }

        private void DestroyPaintTextures()
        {
            foreach (var t in _paintTextures) if (t != null) UnityEngine.Object.Destroy(t);
            _paintTextures.Clear();
        }

        private List<PlannedPaint> AllPaints()
        {
            var list = new List<PlannedPaint>();
            foreach (var l in _request.Plan.Layers)
                if (l?.Paint != null)
                    foreach (var p in l.Paint) if (p?.Ring != null && p.Ring.Length >= 3) list.Add(p);
            return list;
        }

        private void DoPaint(int frame)
        {
            if (frame == _stepFrame)
            {
                // Exactly once per build: a second entry (should never happen) must not paint (dither) again.
                if (_paintStarted) { Go(Step.EmitNets); return; }
                _paintStarted = true;
                DiscardTerraformBrushes();
                _paintDabs.Clear();
                _paintCursor = 0;
                if (!ResolveTerraformTools() || !ResolvePaintTools())
                {
                    _warnings.Add("Placemaker.Build.PaintUnavailable");
                    Mod.Log.Warn("Placemaker: no terrain material brush in this game; texture painting skipped.");
                    Go(Step.EmitNets);
                    return;
                }
                var paints = AllPaints();
                if (paints.Count == 0) { Go(Step.EmitNets); return; }
                // One splatmap texel (14336 m / 4096 = 3.5 m on vanilla maps): grid aligned to it, cells half a texel.
                float texel = 3.5f;
                try { var area = _terrain.playableArea; if (area.x > 1f) texel = area.x / 4096f; } catch (Exception) { }
                var opts = new PaintRasterOptions { Cell = texel * 0.5f, Align = texel, TileCells = PaintStampTexels, MaxStamps = PaintMaxStamps };
                _paintCancel = new CancellationTokenSource();
                opts.Cancel = _paintCancel.Token;
                _paintJobStart = DateTime.UtcNow;
                _paintJob = Task.Run(() => PaintRaster.Build(paints, opts));
                return;
            }

            if (_paintDabsMode) { PaintDabsFrame(frame); return; }

            if (_paintStamps == null)
            {
                if (_paintJob == null) { Go(Step.EmitNets); return; }
                if (!_paintJob.IsCompleted)
                {
                    SetProgress(0.18f);
                    if ((DateTime.UtcNow - _paintJobStart).TotalSeconds > PaintRasterTimeoutSeconds)
                    {
                        _paintCancel?.Cancel();
                        Mod.Log.Warn("Placemaker terrain paint raster timed out; texture painting skipped.");
                        _warnings.Add("Placemaker.Build.PaintUnavailable");
                        Go(Step.EmitNets);
                    }
                    return;
                }
                if (_paintJob.IsFaulted || _paintJob.IsCanceled)
                {
                    Mod.Log.Warn("Placemaker terrain paint raster failed, using brush dabs: " + _paintJob.Exception?.GetBaseException().Message);
                    BeginPaintDabs();
                    return;
                }
                _paintStamps = _paintJob.Result ?? new List<PaintStamp>();
                _paintStampCursor = 0;
                _paintLastApplyFrame = frame;
                int erases = 0; foreach (var s in _paintStamps) if (s.Erase) erases++;
                Mod.Log.Info($"Placemaker paints the terrain: {_paintStamps.Count} stamps ({erases} erase), {PaintRaster.PaintedArea(_paintStamps):0} m², cell {(_paintStamps.Count > 0 ? _paintStamps[0].Cell : 0f):0.##} m.");

            }

            if (_paintStampCursor < _paintStamps.Count)
            {
                // One stamp per frame: a stamp is ≤ 512² texels, so the upload + dispatch stays in the low milliseconds.
                var stamp = _paintStamps[_paintStampCursor++];   // advanced first: a stamp that throws is never retried (no double dither)
                try { ApplyStamp(stamp); }
                catch (Exception e)
                {
                    Mod.Log.Warn("Placemaker direct terrain painting failed: " + e.Message);
                    if (_paintStampCursor == 1) { BeginPaintDabs(); return; }   // nothing painted yet: the dabs can still do it
                    _warnings.Add("Placemaker.Build.PaintUnavailable");
                    _paintStampCursor = _paintStamps.Count;
                }
                _paintLastApplyFrame = frame;
                SetProgress(0.18f + 0.02f * (_paintStampCursor / (float)math.max(1, _paintStamps.Count)));
                return;
            }
            if (frame - _paintLastApplyFrame < PaintSettleFrames) return;
            DestroyPaintTextures();
            Go(Step.EmitNets);
        }

        /// <summary>
        /// Blits one stamp: the brush prefab's texture is swapped for the stamp mask during the call (ApplyBrush reads
        /// BrushPrefab.m_Texture and executes its command buffer inside the call). Strength lives in the mask.
        /// </summary>
        private void ApplyStamp(PaintStamp stamp)
        {
            if (!_paintTools.TryGetValue(math.clamp(stamp.Layer, 0, 4), out var tool)) return;
            if (!_prefabs.TryGetPrefab<BrushPrefab>(_brushPrefab, out var brushPrefab) || brushPrefab == null)
                throw new InvalidOperationException("brush prefab missing");
            var materials = World.GetOrCreateSystemManaged<global::Game.Rendering.TerrainMaterialSystem>();
            if (materials == null) throw new InvalidOperationException("TerrainMaterialSystem missing");
            // s33: the PaintSplatmap kernel (DXBC disassembled) maps the brush texture with u = +x, v = +z at m_Angle 0
            // (uv = rotate(texel centre − brush centre) / half size), and row 0 of a Texture2D is v = 0: identity, whatever
            // the heightmap brush calibration found for the terraform shader.
            var tex = StampTexture(stamp, false, false);
            var c = stamp.Center;
            float h = Height(c);
            var brush = new Brush
            {
                m_Tool = tool,
                m_Position = new float3(c.x, h, c.y),
                m_Target = new float3(c.x, h, c.y),
                m_Start = new float3(c.x, h, c.y),
                m_Angle = 0f,
                m_Size = stamp.Size,
                // s33: strength 0 with a material tool runs EraseDither on that material's channel only
                // (TerrainMaterialSystem.ApplyToSplatmap: kernel = strength > 0 ? Paint : Erase); the mask is the strength.
                m_Strength = stamp.Erase && stamp.Layer > 0 ? 0f : 1f,
                m_Opacity = 1f,
            };
            var original = brushPrefab.m_Texture;
            try
            {
                brushPrefab.m_Texture = tex;
                materials.ApplyBrush(brush, _brushPrefab);
            }
            finally
            {
                brushPrefab.m_Texture = original;
            }
        }

        /// <summary>RGBA32 mask (all channels = strength), row 0 = min z; flips per the calibrated brush orientation.</summary>
        private UnityEngine.Texture2D StampTexture(PaintStamp s, bool flipX, bool flipY)
        {
            int n = s.N;
            var tex = new UnityEngine.Texture2D(n, n, UnityEngine.TextureFormat.RGBA32, false, true)
            {
                wrapMode = UnityEngine.TextureWrapMode.Clamp,
                filterMode = UnityEngine.FilterMode.Bilinear,
                name = "PlacemakerPaintMask",
            };
            var bytes = new byte[n * n * 4];
            for (int y = 0; y < n; y++)
            {
                int ty = flipY ? n - 1 - y : y;
                for (int x = 0; x < n; x++)
                {
                    byte v = (byte)math.clamp((int)math.round(s.Mask[y * n + x] * 255f), 0, 255);
                    if (v == 0) continue;
                    int o = (ty * n + (flipX ? n - 1 - x : x)) * 4;
                    bytes[o] = bytes[o + 1] = bytes[o + 2] = bytes[o + 3] = v;
                }
            }
            tex.LoadRawTextureData(bytes);
            tex.Apply(false, false);
            _paintTextures.Add(tex);
            return tex;
        }

        // ───────────────────────── fallback: brush dabs ─────────────────────────

        private void BeginPaintDabs()
        {
            _paintDabsMode = true;
            _heightsValid = false;
            _paintDabs.Clear();
            _paintCursor = 0;
            int skipped = 0;
            foreach (var paint in AllPaints())
            {
                // s33: shape fields and per-material erases have no dab equivalent (a dab paints its whole ring).
                if (paint.Shape != null || (paint.Erase && paint.Layer > 0)) { skipped++; continue; }
                if (!_paintTools.TryGetValue(math.clamp(paint.Layer, 0, 4), out var tool)) continue;
                var ring = new float2[paint.Ring.Length];
                for (int i = 0; i < ring.Length; i++) ring[i] = paint.Ring[i].xz;
                foreach (var q in TerraformDabs.Dabs(ring, PaintBrushRadius))
                    _paintDabs.Add(new PaintDab { Tool = tool, At = new float3(q.x, Height(q), q.y), Strength = math.saturate(paint.Strength) });
            }
            Mod.Log.Info($"Placemaker paints the terrain with {_paintDabs.Count} brush dabs (fallback; {skipped} field paints skipped).");
        }

        private void PaintDabsFrame(int frame)
        {
            // One wave per two frames: emit + apply, then discard on the next frame.
            if (((frame - _stepFrame) & 1) == 1) { DiscardTerraformBrushes(); return; }
            if (_paintCursor >= _paintDabs.Count)
            {
                DiscardTerraformBrushes();
                _paintDabs.Clear();
                Go(Step.EmitNets);
                return;
            }
            int end = Math.Min(_paintDabs.Count, _paintCursor + MaxPaintDabsPerFrame);
            for (int i = _paintCursor; i < end; i++) EmitPaintBrush(_paintDabs[i].Tool, _paintDabs[i].At, _paintDabs[i].Strength);
            _paintCursor = end;
            _tool.Requested = ApplyMode.Apply;
            SetProgress(0.18f + 0.02f * (_paintCursor / (float)_paintDabs.Count));
        }

        private void EmitPaintBrush(Entity tool, float3 position, float strength)
        {
            var def = EntityManager.CreateEntity();
            EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = _brushPrefab });
            EntityManager.AddComponentData(def, new BrushDefinition
            {
                m_Tool = tool,
                m_Line = new Line3.Segment(position, position),
                m_Size = PaintBrushRadius * 2f,
                m_Angle = 0f,
                m_Strength = strength,
                m_Time = BrushTime,
                m_Target = position,
                m_Start = position,
            });
            EntityManager.AddComponent<Updated>(def);
            _brushDefinitions.Add(def);
        }

        /// <summary>Material brushes by layer: 0 = erase (TerrainMaterialType.None), 1..4 = Extra1..Extra4.</summary>
        private bool ResolvePaintTools()
        {
            if (_paintTools.Count > 0)
            {
                bool valid = true;
                foreach (var e in _paintTools.Values) if (!EntityManager.Exists(e)) valid = false;
                if (valid) return true;
                _paintTools.Clear();
            }
            if (_allPrefabs == default) _allPrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            using var prefabs = _allPrefabs.ToEntityArray(Allocator.Temp);
            var names = new List<string>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (!_prefabs.TryGetPrefab<PrefabBase>(prefabs[i], out var prefab) || !(prefab is TerraformingPrefab tf) || !prefab.isBuiltin) continue;   // vanilla paint tools only
                if (tf.m_Target != TerraformingTarget.Material || !tf.TryGet<TerrainMaterialProperties>(out var mat) || mat == null) continue;
                int layer;
                switch (mat.m_Type)
                {
                    case TerrainMaterialType.None: layer = 0; break;
                    case TerrainMaterialType.Extra1: layer = 1; break;
                    case TerrainMaterialType.Extra2: layer = 2; break;
                    case TerrainMaterialType.Extra3: layer = 3; break;
                    case TerrainMaterialType.Extra4: layer = 4; break;
                    default: continue;
                }
                if (!_paintTools.ContainsKey(layer)) { _paintTools[layer] = prefabs[i]; names.Add(layer + "=" + tf.name); }
            }
            if (_paintTools.Count > 0)
                Mod.Log.Info($"Placemaker terrain paint layers available: {string.Join(", ", names)}.");
            return _paintTools.Count > 0;
        }
    }
}
