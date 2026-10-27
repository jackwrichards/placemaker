// Placemaker — terraform brushes, lake water and zoning (GPL-3.0).
// Terraform: the desired ground (Core TerraformRaster) is applied directly with
// TerrainSystem.ApplyBrush (Shift kernel, one generated mask texture per tile) and verified against
// the CPU height readback. Fallback and water adapted from ParkManager's ParkLakeBuild (tomkpunkt,
// GPL-3.0): the game's level brush re-emitted for spots off target, then a constant-level water
// source fills the basin. Zoning follows GridRoadGenerator's Zoning
// definitions (Ti4goc, GPL-3.0): ZoningFlags.Zone|Marquee without Overwrite never repaints cells
// that are already zoned.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        // ───────────────────────── direct terraform (playtest 4b) ─────────────────────────
        // One generated mask texture per tile, applied with TerrainSystem.ApplyBrush (Shift kernel):
        // no brush entities, no previews, a handful of applications in total. Every pass is measured
        // against the CPU height readback; the first pass also calibrates the brush texture
        // orientation (remembered for the session). Old per-frame level-brush dabs remain only as a
        // fallback when the direct path is unavailable or does not converge.

        private const float TerraformCell = 1.75f;        // half a heightmap pixel (14336 m / 4096)
        private const float TerraformBank = 4f;           // blended edge outside the rings (m)
        private const float TerraformTolerance = 0.3f;
        private const float TerraformOffFraction = 0.02f;
        private const int TerraformMaxPasses = 4;
        private const int TerraformWaitFrames = 6;        // apply → cascade re-render → CPU readback
        private const int TerraformMaxStaleWaits = 3;
        private const int TerraformTileTexels = 1024;
        private const int TerraformMaxRasterCells = 1 << 20;
        private const double DirectTerraformSeconds = 6.0;
        private const float ShiftToolStrength = 2000f;    // TerrainSystem.GetTerrainAdjustmentSpeed(Shift)

        private enum TfMode { Direct, Dabs, Preparing }

        private struct TfApplied { public UnityEngine.Texture2D Texture; public Brush Brush; public Bounds2 Area; }

        /// <summary>Brush texture orientation found by calibration (0..3 = flipX | flipY·2); -1 unknown.</summary>
        private static int s_shiftOrientation = -1;

        private TerraformRaster _tfRaster;
        private TfMode _tfMode;
        private bool _tfAwaiting;        // a pass was applied and is waiting to be measured
        private bool _tfReverting;       // the last applications are being undone (wrong orientation)
        private bool _tfVerified;        // the ground reached the raster: nets/objects may use it
        private int _tfPass, _tfOrientation, _tfOrientationTries, _tfStaleWaits, _tfWaitUntil;
        private float _tfStartRms, _tfLastRms;
        private TerraformRaster.Residual _tfFinal;
        private readonly List<TfApplied> _tfApplied = new List<TfApplied>();
        private readonly List<UnityEngine.Texture2D> _tfTextures = new List<UnityEngine.Texture2D>();

        private void ResetTerraform()
        {
            // Cancel/cleanup must also delete any brush definitions still alive (else they keep shaping).
            DiscardTerraformBrushes();
            DestroyTerraformTextures();
            _dabs.Clear();
            _paintDabs.Clear();
            _paintCursor = 0;
            ResetPaint();
            _dabCursor = 0;
            _tfRaster = null;
            _tfVerified = false;
            _tfAwaiting = _tfReverting = false;
            _tfWaitUntil = 0;
            _tfJob = null;   // a running one disposes its own height copy
        }

        private void DestroyTerraformTextures()
        {
            foreach (var t in _tfTextures) if (t != null) UnityEngine.Object.Destroy(t);
            _tfTextures.Clear();
            _tfApplied.Clear();
        }

        /// <summary>Monotonic progress: retries and phase changes never move the bar backwards.</summary>
        private void SetProgress(float value)
        {
            if (value > _status.Progress) _status.Progress = math.min(1f, value);
        }

        private List<(float2[] ring, float target, int layer)> TerraformItems() => TerraformItems(out _);

        /// <summary>Terraform items of the plan; <paramref name="vertexHeights"/> parallel: ring heights of sloped (s20) items, else null.</summary>
        private List<(float2[] ring, float target, int layer)> TerraformItems(out List<float[]> vertexHeights)
        {
            var items = new List<(float2[] ring, float target, int layer)>();
            vertexHeights = new List<float[]>();
            foreach (var w in _work.Terraform)
            {
                var t = _request.Plan.Layers[w.Layer].Terraform[w.Index];
                if (!math.isfinite(t.TargetHeight) || t.Ring == null || t.Ring.Length < 3) continue;
                var ring = new float2[t.Ring.Length];
                for (int i = 0; i < ring.Length; i++) ring[i] = t.Ring[i].xz;
                items.Add((ring, t.TargetHeight, w.Layer));
                vertexHeights.Add(TerraformSurface.VertexHeights(t));
            }
            return items;
        }

        private void BeginTerraform()
        {
            _dabs.Clear();
            _dabCursor = 0;
            _terraformStart = DateTime.UtcNow;
            _tfPass = _tfOrientationTries = _tfStaleWaits = 0;
            _tfAwaiting = _tfReverting = _tfVerified = false;
            _tfWaitUntil = 0;
            _tfOrientation = s_shiftOrientation >= 0 ? s_shiftOrientation : 0;
            var items = TerraformItems(out var vertexHeights);
            if (items.Count == 0) { Go(AfterTerraform()); return; }
            if (BeginTerraformJob(items, vertexHeights)) return;
            try
            {
                // The ground before this build (read now, before any pass) carries the banks around sloped patches (s20).
                _heightsValid = false;
                _tfRaster = TerraformRaster.Build(items, TerraformCell, TerraformBank, TerraformMaxRasterCells, vertexHeights, Height);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker terraform raster failed: " + e.Message);
                _tfRaster = null;
            }
            if (_tfRaster == null) { BeginTerraformDabs(items); return; }
            GuardPits();
            _tfMode = TfMode.Direct;
            _tfStartRms = _tfLastRms = -1f;
            Mod.Log.Info($"Placemaker terraform: {items.Count} items, raster {_tfRaster.Width}x{_tfRaster.Height} @ {_tfRaster.Cell:F2} m (direct brush).");
        }

        // ───── (perf) the target raster and the pit guard on a worker ─────
        // Both read the ground before the build at every texel (the pit guard 49 samples each, up to 4 passes): 0.3 s for a
        // 1.5 ha lot, 2.7 s for 16 ha, in one frame of the game's thread (teardown 14). Now from a copy of the game's height
        // map, through the game's own sampler (the same heights to the bit), while the game runs on.

        private System.Threading.Tasks.Task<(TerraformRaster raster, string pitNote, double ms)> _tfJob;
        private List<(float2[] ring, float target, int layer)> _tfJobItems;

        /// <summary>A copy of the game's height map (a few ms) that a worker samples with TerrainUtils.SampleHeight; the worker disposes it.</summary>
        private bool TryCopyHeights(out TerrainHeightData copy)
        {
            copy = default;
            try
            {
                var live = _terrain.GetHeightData(true);
                if (!live.isCreated) return false;
                var heights = new NativeArray<ushort>(live.heights, Allocator.Persistent);
                var down = live.downscaledHeights.IsCreated && live.downscaledHeights.Length > 0
                    ? new NativeArray<ushort>(live.downscaledHeights, Allocator.Persistent) : new NativeArray<ushort>(0, Allocator.Persistent);
                copy = new TerrainHeightData(heights, down, live.resolution, live.scale, live.offset, live.hasBackdrop);
                return true;
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: no copy of the height map (" + e.Message + "); worked out in this frame."); return false; }
        }

        private static void DisposeHeights(TerrainHeightData d)
        {
            if (d.heights.IsCreated) d.heights.Dispose();
            if (d.downscaledHeights.IsCreated) d.downscaledHeights.Dispose();
        }

        private bool BeginTerraformJob(List<(float2[] ring, float target, int layer)> items, List<float[]> vertexHeights)
        {
            if (!TryCopyHeights(out var copy)) return false;
            bool guard = _request?.Plan != null && !TerraformPits.Intentional(_request.Plan);
            _tfJobItems = items;
            _tfMode = TfMode.Preparing;
            _tfJob = System.Threading.Tasks.Task.Run(() =>
            {
                var d = copy;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    Func<float2, float> height = p => { float h = TerrainUtils.SampleHeight(ref d, new float3(p.x, 0f, p.y)); return math.isfinite(h) ? h : 0f; };
                    var raster = TerraformRaster.Build(items, TerraformCell, TerraformBank, TerraformMaxRasterCells, vertexHeights, height);
                    string note = null;
                    if (raster != null && !guard) note = "Placemaker pit guard: skipped (this build digs on purpose).";
                    else if (raster != null)
                    {
                        int n = TerraformPits.Fill(raster, height, PitDepth, out float deepest, out var at);
                        if (n > 0) note = $"Placemaker pit guard: raised {n} texel(s) of the target, deepest bowl {deepest:F2} m at ({at.x:F0}, {at.y:F0}) (a lot has no hollow).";
                    }
                    return (raster, note, sw.Elapsed.TotalMilliseconds);
                }
                finally { DisposeHeights(d); }
            });
            return true;
        }

        /// <summary>DoTerraform while the target is made: waits for it, then starts the brush passes (or the dabs without one).</summary>
        private void DoTerraformPreparing()
        {
            if (_tfJob == null) { Go(AfterTerraform()); return; }
            if (!_tfJob.IsCompleted) return;
            var job = _tfJob;
            _tfJob = null;
            TerraformRaster raster = null;
            if (job.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
            {
                raster = job.Result.raster;
                if (job.Result.pitNote != null) Mod.Log.Info(job.Result.pitNote);
                Mod.Log.Info($"Placemaker terraform target made in the background in {job.Result.ms:0} ms.");
            }
            else Mod.Log.Warn("Placemaker terraform raster failed: " + job.Exception?.GetBaseException().Message);
            _terraformStart = DateTime.UtcNow;   // the time limit counts the brush passes, not the wait
            _tfRaster = raster;
            if (_tfRaster == null) { BeginTerraformDabs(_tfJobItems); return; }
            _tfMode = TfMode.Direct;
            _tfStartRms = _tfLastRms = -1f;
            Mod.Log.Info($"Placemaker terraform: {_tfJobItems.Count} items, raster {_tfRaster.Width}x{_tfRaster.Height} @ {_tfRaster.Cell:F2} m (direct brush).");
        }

        /// <summary>(s55) A lot is never meant to have a bowl in it (playtest 91): raise any pit of the target to its rim and log it.</summary>
        private void GuardPits()
        {
            try
            {
                if (_tfRaster == null || _work == null || _request?.Plan == null) return;
                if (TerraformPits.Intentional(_request.Plan)) { Mod.Log.Info("Placemaker pit guard: skipped (this build digs on purpose)."); return; }
                int n = TerraformPits.Fill(_tfRaster, Height, PitDepth, out float deepest, out var at);
                if (n > 0) Mod.Log.Warn($"Placemaker pit guard: raised {n} texel(s) of the target, deepest bowl {deepest:F2} m at ({at.x:F0}, {at.y:F0}) (a lot has no hollow).");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker pit guard failed: " + e.Message); }
        }

        /// <summary>A pit is a bowl lower than the ring round it by more than this (m).</summary>
        private const float PitDepth = 0.35f;

        /// <summary>(s55) After the build: does the live terrain hold a bowl although the target has none? One log line per bowl (max 3).</summary>
        private void VerifyLiveHollows()
        {
            if (!DebugBuildChecks) return;
            try
            {
                if (_tfRaster == null || !_tfVerified) return;
                _heightsValid = false;   // read the terrain as it is now
                var pits = TerraformPits.Worst(_tfRaster, Height, 0.5f);
                if (pits.Count == 0) { Mod.Log.Info("Placemaker terrain check: no bowl deeper than 0.5 m in the built ground."); return; }
                foreach (var p in pits)
                {
                    float planned = _tfRaster.TrySample(p.At, _ => p.Height, out float t) ? t : float.NaN;
                    Mod.Log.Warn($"Placemaker terrain check: bowl {p.Depth:F2} m deep at ({p.At.x:F0}, {p.At.y:F0}); live ground {p.Height:F2} m, target there {planned:F2} m"
                        + (math.abs(planned - p.Height) > 0.6f ? " (the game's ground differs from the target)" : " (the target itself has it)") + ".");
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker terrain check failed: " + e.Message); }
        }

        private void DoTerraform(int frame)
        {
            if (_tfMode == TfMode.Dabs) { DoTerraformDabs(frame); return; }
            if (_tfMode == TfMode.Preparing) { DoTerraformPreparing(); return; }
            if (frame < _tfWaitUntil) return;
            if ((DateTime.UtcNow - _terraformStart).TotalSeconds > DirectTerraformSeconds)
            {
                FinishTerraformDirect(false, "time limit");
                return;
            }
            _heightsValid = false;
            var deltas = _tfRaster.Deltas(Height, TerraformTolerance, out var stats);
            if (_tfStartRms < 0) _tfStartRms = math.max(1e-3f, stats.Rms);
            SetProgress(0.1f + 0.08f * math.saturate(1f - stats.Rms / _tfStartRms));

            if (_tfReverting)
            {
                // The wrong orientation was undone: try the next one from the restored ground.
                _tfReverting = false;
                DestroyTerraformTextures();
                _tfOrientation = (_tfOrientation + 1) & 3;
                _tfLastRms = stats.Rms;
            }
            else if (_tfAwaiting)
            {
                _tfAwaiting = false;
                float before = _tfLastRms;
                if (math.abs(stats.Rms - before) < 0.01f * math.max(0.05f, before) && _tfStaleWaits < TerraformMaxStaleWaits)
                {
                    // Nothing changed yet: the CPU height copy lags the GPU. Measure again shortly.
                    _tfStaleWaits++;
                    _tfAwaiting = true;
                    _tfWaitUntil = frame + TerraformWaitFrames;
                    return;
                }
                _tfStaleWaits = 0;
                bool improved = stats.Rms < before * 0.8f;
                if (!improved && s_shiftOrientation < 0)
                {
                    if (++_tfOrientationTries >= 4)
                    {
                        Mod.Log.Warn("Placemaker direct terraform: no brush orientation converged; using level brushes.");
                        RevertLastApplied();
                        BeginTerraformDabs(TerraformItems());
                        _tfWaitUntil = frame + TerraformWaitFrames;
                        return;
                    }
                    Mod.Log.Info($"Placemaker direct terraform: orientation {_tfOrientation} did not converge (rms {before:F2} -> {stats.Rms:F2}); reverting.");
                    RevertLastApplied();
                    _tfReverting = true;
                    _tfWaitUntil = frame + TerraformWaitFrames;
                    return;
                }
                if (improved && s_shiftOrientation < 0)
                {
                    s_shiftOrientation = _tfOrientation;
                    Mod.Log.Info($"Placemaker direct terraform: brush orientation {_tfOrientation} confirmed.");
                }
                DestroyTerraformTextures();
            }

            _tfFinal = stats;
            if (stats.Done(TerraformTolerance, TerraformOffFraction))
            {
                FinishTerraformDirect(true, null);
                return;
            }
            if (_tfPass >= TerraformMaxPasses)
            {
                FinishTerraformDirect(false, $"{stats.InsideOff}/{stats.Inside} cells still off after {_tfPass} passes");
                return;
            }
            int applied = ApplyShiftPass(deltas);
            _tfPass++;
            _tfLastRms = stats.Rms;
            _tfAwaiting = applied > 0;
            _tfWaitUntil = frame + TerraformWaitFrames;
            if (applied == 0) FinishTerraformDirect(true, null);
        }

        private void FinishTerraformDirect(bool done, string why)
        {
            DestroyTerraformTextures();
            _tfVerified = done;
            if (!done)
            {
                _warnings.Add("Placemaker.Build.TerraformIncomplete");
                Mod.Log.Warn($"Placemaker terraform incomplete ({why}): worst {_tfFinal.InsideMaxAbs:F2} m at ({_tfFinal.WorstAt.x:F0}, {_tfFinal.WorstAt.y:F0}), live {_tfFinal.WorstLive:F2} m, wanted {_tfFinal.WorstWanted:F2} m, rms {_tfFinal.Rms:F2} m.");
            }
            else Mod.Log.Info($"Placemaker terraform reached its target in {_tfPass} pass(es), {(DateTime.UtcNow - _terraformStart).TotalSeconds:F1} s"
                + $" (measured on the live terrain: {_tfFinal.InsideOff}/{_tfFinal.Inside} texels off, worst {_tfFinal.InsideMaxAbs:F2} m, rms {_tfFinal.Rms:F2} m).");
            SetProgress(0.18f);
            Go(AfterTerraform());
        }

        /// <summary>Raise and lower brushes per tile. Returns the number of brush applications.</summary>
        private int ApplyShiftPass(float[] deltas)
        {
            bool flipX = (_tfOrientation & 1) != 0, flipY = (_tfOrientation & 2) != 0;
            int applied = 0;
            float dt = UnityEngine.Time.unscaledDeltaTime;
            _tfAppliedDelta = math.clamp(dt, 0.05f, 1f);
            foreach (var tile in _tfRaster.Tiles(TerraformTileTexels))
            {
                float metres = TerraformRaster.Masks(deltas, _tfRaster.Width, _tfRaster.Height, tile, flipX, flipY, TerraformTolerance * 0.25f,
                    out var raise, out var lower, out bool anyRaise, out bool anyLower);
                if (metres <= 0f) continue;
                float strength = TerraformRaster.ShiftStrength(metres, dt, ShiftToolStrength, out float maskScale);
                _tfRaster.TileWorld(tile, out var center, out float size);
                if (anyRaise) { ApplyShift(raise, tile.Size, maskScale, center, size, strength); applied++; }
                if (anyLower) { ApplyShift(lower, tile.Size, maskScale, center, size, -strength); applied++; }
            }
            return applied;
        }

        private void ApplyShift(float[] mask, int n, float scale, float2 center, float size, float strength)
        {
            var tex = MaskTexture(mask, n, scale);
            float y = Height(center);
            var brush = new Brush
            {
                m_Tool = Entity.Null,   // no TerraformingBrushData → the Shift speed (2000) applies
                m_Position = new float3(center.x, y, center.y),
                m_Target = new float3(center.x, y, center.y),
                m_Start = new float3(center.x, y, center.y),
                m_Angle = 0f,
                m_Size = size,
                m_Strength = strength,
                m_Opacity = 1f,
            };
            var area = new Bounds2(center - size * 0.5f, center + size * 0.5f);
            _terrain.ApplyBrush(TerraformingType.Shift, area, brush, tex);
            _tfApplied.Add(new TfApplied { Texture = tex, Brush = brush, Area = area });
        }

        /// <summary>Undoes the last pass exactly (Shift is additive): same textures, opposite strength.</summary>
        private void RevertLastApplied()
        {
            var list = new List<TfApplied>(_tfApplied);
            _tfApplied.Clear();
            float dt = UnityEngine.Time.unscaledDeltaTime;
            foreach (var a in list)
            {
                var b = a.Brush;
                // The game scales by this frame's clamped delta time: rescale so the change matches.
                b.m_Strength = -b.m_Strength * _tfAppliedDelta / math.clamp(dt, 0.05f, 1f);
                _terrain.ApplyBrush(TerraformingType.Shift, a.Area, b, a.Texture);
            }
        }

        /// <summary>Clamped delta time of the frame the last pass was applied in.</summary>
        private float _tfAppliedDelta = 0.05f;

        /// <summary>RGBA half texture (every channel = mask): the kernels' sampled channel is not documented.</summary>
        private UnityEngine.Texture2D MaskTexture(float[] mask, int n, float scale)
        {
            var tex = new UnityEngine.Texture2D(n, n, UnityEngine.TextureFormat.RGBAHalf, false, true)
            {
                wrapMode = UnityEngine.TextureWrapMode.Clamp,
                filterMode = UnityEngine.FilterMode.Bilinear,
                name = "PlacemakerMask",
            };
            var pixels = new UnityEngine.Color[n * n];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = mask[i] * scale;
                pixels[i] = new UnityEngine.Color(v, v, v, v);
            }
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            _tfTextures.Add(tex);
            return tex;
        }

        /// <summary>
        /// Ground for nets/objects after terraform: the raster's desired height where terraform
        /// touched the ground and reached it (verified), else the live terrain. Keeps paths and
        /// objects consistent with the terraform even while the CPU height copy still lags.
        /// </summary>
        private float GroundHeight(float2 p)
        {
            if (_tfVerified && _tfRaster != null && _tfRaster.TrySample(p, Height, out float h)) return h;
            return Height(p);
        }

        // ───────────────────────── fallback: level brush dabs ─────────────────────────

        private const float BrushRadius = 12f;
        private const float LevelTolerance = 0.3f;
        private const float BrushTime = 0.3f;
        // Frame budget: at most this many level brushes per frame (round-robin over the spots), so a big
        // area never stalls the game (playtest: ~960 brushes/frame dropped it to 2 FPS).
        private const int MaxBrushesPerFrame = 48;
        // Real-time limit, independent of frame rate.
        private const double TerraformTimeoutSeconds = 75.0;
        private const int TerraformSettleFrames = 10;

        private struct Dab { public float2 P; public float Target; }

        private readonly List<Dab> _dabs = new List<Dab>();
        private readonly List<Entity> _brushDefinitions = new List<Entity>();
        private int _dabCursor;
        private DateTime _terraformStart;
        private Entity _levelTool, _brushPrefab;
        private EntityQuery _allPrefabs;

        private void BeginTerraformDabs(List<(float2[] ring, float target, int layer)> items)
        {
            _tfMode = TfMode.Dabs;
            _dabs.Clear();
            _dabCursor = 0;
            _terraformStart = DateTime.UtcNow;
            _stepFrame = UnityEngine.Time.frameCount;
            if (!ResolveTerraformTools())
            {
                _warnings.Add("Placemaker.Build.TerraformUnavailable");
                Go(AfterTerraform());
                return;
            }
            // A feature's own levelling inside a terraformed parent wins over the parent's cells there
            // (otherwise both targets are dabbed at once and never settle).
            foreach (var d in TerraformDabs.Merge(items, BrushRadius)) _dabs.Add(new Dab { P = d.P, Target = d.Target });
            Mod.Log.Info($"Placemaker terraform fallback: {_dabs.Count} level-brush spots.");
            if (_dabs.Count == 0) Go(AfterTerraform());
        }

        private void DoTerraformDabs(int frame)
        {
            if (frame < _tfWaitUntil) return;
            DiscardTerraformBrushes();
            _heightsValid = false;
            int elapsed = frame - _stepFrame;
            int pending = 0, emitted = 0, n = _dabs.Count;
            for (int k = 0; k < n; k++)
            {
                var d = _dabs[(_dabCursor + k) % n];
                float h = Height(d.P);
                if (math.abs(h - d.Target) <= LevelTolerance) continue;
                pending++;
                if (emitted < MaxBrushesPerFrame) { EmitBrush(new float3(d.P.x, h, d.P.y), d.Target); emitted++; }
            }
            if (n > 0) _dabCursor = (_dabCursor + MaxBrushesPerFrame) % n;
            SetProgress(0.1f + 0.08f * (_dabs.Count == 0 ? 1f : 1f - pending / (float)_dabs.Count));
            bool done = pending == 0 && elapsed > TerraformSettleFrames;
            bool timedOut = elapsed > TerraformTimeoutFrames * math.max(1, (n + MaxBrushesPerFrame - 1) / MaxBrushesPerFrame) / 4
                || (DateTime.UtcNow - _terraformStart).TotalSeconds > TerraformTimeoutSeconds;
            if (done || timedOut)
            {
                DiscardTerraformBrushes();
                if (!done)
                {
                    _warnings.Add("Placemaker.Build.TerraformIncomplete");
                    Mod.Log.Warn($"Placemaker terraform timed out with {pending}/{_dabs.Count} spots off target.");
                }
                Go(AfterTerraform());
                return;
            }
            if (pending > 0) _tool.Requested = ApplyMode.Apply;
        }

        private void EmitBrush(float3 position, float target)
        {
            var def = EntityManager.CreateEntity();
            EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = _brushPrefab });
            EntityManager.AddComponentData(def, new BrushDefinition
            {
                m_Tool = _levelTool,
                m_Line = new Line3.Segment(position, position),
                m_Size = BrushRadius * 2f,
                m_Angle = 0f,
                m_Strength = 1f,
                m_Time = BrushTime,
                m_Target = new float3(position.x, target, position.z),
                m_Start = position,
            });
            EntityManager.AddComponent<Updated>(def);
            _brushDefinitions.Add(def);
        }

        private void DiscardTerraformBrushes()
        {
            foreach (var d in _brushDefinitions)
                if (EntityManager.Exists(d) && !EntityManager.HasComponent<Deleted>(d)) EntityManager.AddComponent<Deleted>(d);
            _brushDefinitions.Clear();
        }

        /// <summary>Vanilla height-level terraform tool and a (preferably soft) brush.</summary>
        private bool ResolveTerraformTools()
        {
            if (_levelTool != Entity.Null && EntityManager.Exists(_levelTool) && _brushPrefab != Entity.Null && EntityManager.Exists(_brushPrefab)) return true;
            _levelTool = _brushPrefab = Entity.Null;
            if (_allPrefabs == default) _allPrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            string brushName = "";
            using var prefabs = _allPrefabs.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < prefabs.Length; i++)
            {
                // vanilla tools and brushes only (a mod brush shape would shape the ground its own way)
                if (!_prefabs.TryGetPrefab<PrefabBase>(prefabs[i], out var prefab) || prefab == null || !prefab.isBuiltin) continue;
                if (prefab is TerraformingPrefab tf && tf.m_Type == TerraformingType.Level && tf.m_Target == TerraformingTarget.Height)
                {
                    if (_levelTool == Entity.Null) _levelTool = prefabs[i];
                }
                else if (prefab is BrushPrefab && (_brushPrefab == Entity.Null
                    || (prefab.name.IndexOf("soft", StringComparison.OrdinalIgnoreCase) >= 0 && brushName.IndexOf("soft", StringComparison.OrdinalIgnoreCase) < 0)))
                {
                    _brushPrefab = prefabs[i];
                    brushName = prefab.name;
                }
            }
            return _levelTool != Entity.Null && _brushPrefab != Entity.Null;
        }

        // ───────────────────────── after apply: water ─────────────────────────

        private void AfterApply()
        {
            if (_work == null) return;
            foreach (var w in _work.Terraform)
            {
                var t = _request.Plan.Layers[w.Layer].Terraform[w.Index];
                if (!math.isfinite(t.WaterLevel)) continue;
                try { CreateWaterSource(t); }
                catch (Exception e) { Mod.Log.Warn("Placemaker could not add lake water: " + e.Message); _warnings.Add("Placemaker.Build.NoWater"); }
            }
        }

        private void CreateWaterSource(PlannedTerraform t)
        {
            var ring = new float2[t.Ring.Length];
            for (int i = 0; i < ring.Length; i++) ring[i] = t.Ring[i].xz;
            var anchor = TerraformDabs.Anchor(ring, out float innerRadius);
            if (math.all(math.isfinite(t.WaterAnchor.xz)) && math.lengthsq(t.WaterAnchor.xz) > 0 && ReservedSpace.PointInPolygon(t.WaterAnchor.xz, ring))
                anchor = t.WaterAnchor.xz;
            _heightsValid = false;
            float bottom = Height(anchor);
            if (t.WaterLevel - bottom < 0.5f)
            {
                _warnings.Add("Placemaker.Build.NoWater");
                Mod.Log.Warn($"Placemaker lake basin not deep enough (ground {bottom:F2}, water {t.WaterLevel:F2}); no water added.");
                return;
            }
            bool legacy = _water.UseLegacyWaterSources;
            var position = new float3(anchor.x, bottom, anchor.y);
            // The legacy water system reads the level as an absolute elevation, the current one as
            // a height above the source position (PM measured both).
            var source = new global::Game.Simulation.WaterSourceData
            {
                m_ConstantDepth = legacy ? 1 : 0,
                m_Height = legacy ? t.WaterLevel : t.WaterLevel - bottom,
                m_Radius = math.max(5f, innerRadius * 0.9f),
                m_Polluted = 0f,
                m_Id = _water.GetNextSourceId(),
                m_Modifier = 1f,
            };
            // A multiplier of exactly 1 marks a radius too small for the simulation.
            source.m_Multiplier = WaterSystem.CalculateSourceMultiplier(source, position);
            for (int attempt = 0; attempt < 200 && source.m_Multiplier == 1f; attempt++)
            {
                source.m_Radius += 1f;
                source.m_Multiplier = WaterSystem.CalculateSourceMultiplier(source, position);
            }
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, source);
            EntityManager.AddComponentData(e, new global::Game.Objects.Transform { m_Position = position, m_Rotation = quaternion.identity });
            EntityManager.AddComponent<Created>(e);
            EntityManager.AddComponent<Updated>(e);
            EntityManager.AddComponentData(e, new PlacemakerMember { GroupId = _groupId, Kind = MemberKind.Auxiliary, Role = MemberRole.WaterSource });
        }

        // ───────────────────────── zoning ─────────────────────────

        private int _zoningEmitted;

        private void DoZoningEmit()
        {
            int emitted = 0, missing = 0;
            var missingIds = new HashSet<string>(StringComparer.Ordinal);
            if (_refreshedZoning != null)
            {
                // (s26) Quads re-snapped to the live blocks: the zone prefab per zone id comes from the planned work items.
                var prefabs = new Dictionary<string, Entity>(StringComparer.Ordinal);
                foreach (var w in _work.Zoning)
                {
                    var id = _request.Plan.Layers[w.Layer].Zoning[w.Index].ZoneId ?? "";
                    if (prefabs.ContainsKey(id)) continue;
                    var pf = PrefabOf(w);
                    if (pf != Entity.Null && EntityManager.HasComponent<ZoneData>(pf)) prefabs[id] = pf;
                }
                foreach (var q in _refreshedZoning)
                {
                    if (!prefabs.TryGetValue(q.ZoneId ?? "", out var prefab)) { missing++; missingIds.Add(q.ZoneId ?? ""); continue; }
                    var c = q.Corners;
                    float3 P(float2 v) => new float3(v.x, Height(v), v.y);
                    var def = NewDefinition();
                    EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = prefab });
                    EntityManager.AddComponentData(def, new Zoning { m_Position = new Quad3(P(c[0]), P(c[1]), P(c[2]), P(c[3])), m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee });
                    EntityManager.AddComponent<Updated>(def);
                    NoteUsed(prefab);
                    emitted++;
                }
            }
            else foreach (var w in _work.Zoning)
            {
                var z = _request.Plan.Layers[w.Layer].Zoning[w.Index];
                var prefab = PrefabOf(w);
                if (prefab == Entity.Null || !EntityManager.HasComponent<ZoneData>(prefab))
                {
                    // (s22) Was silent: a zone the playset does not have left the cutouts unzoned without a word.
                    missing++;
                    missingIds.Add(z.ZoneId ?? "");
                    continue;
                }
                var def = NewDefinition();
                EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = prefab });
                EntityManager.AddComponentData(def, new Zoning
                {
                    m_Position = new Quad3(z.A, z.B, z.C, z.D),
                    // No ZoningFlags.Overwrite: cells the player already zoned are kept.
                    m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee,
                });
                EntityManager.AddComponent<Updated>(def);
                NoteUsed(prefab);
                emitted++;
            }
            if (missing > 0)
                Mod.Log.Warn($"Placemaker zoning: {missing} cutout quad(s) skipped, zone prefab not found: {string.Join(", ", missingIds)}.");
            Mod.Log.Info($"Placemaker zoning: {emitted} zoning quad(s) emitted for group {_groupId} (Marquee, keeps existing zoning).");
            _zoningEmitted = emitted;
            Go(emitted > 0 ? Step.ZoningApply : Step.Finish);
        }

        /// <summary>Frames after the zoning Apply before the cells are checked (zoning and cell jobs run in between).</summary>
        private const int ZoningVerifyFrames = 40;

        private void DoZoningApply(int frame)
        {
            int elapsed = frame - _stepFrame;
            if (elapsed == 2) _tool.Requested = ApplyMode.Apply;
            else if (elapsed == 4) DestroyOwnDefinitions();
            else if (elapsed >= ZoningVerifyFrames)
            {
                try { VerifyZoning(); }
                catch (Exception e) { Mod.Log.Warn("Placemaker zoning check failed: " + e.Message); }
                Go(Step.Finish);
            }
        }

        private struct ZoneBlockIterator : global::Colossal.Collections.INativeQuadTreeIterator<Entity, Bounds2>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(Bounds2 bounds) => MathUtils.Intersect(bounds, Bounds);
            public void Iterate(Bounds2 bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds, Bounds)) Results.Add(e);
            }
        }

        /// <summary>
        /// (s22) What the game made of the cutouts: for every cell ZoneGuard planned as zoned, the live cell at that
        /// spot — zoned, empty, blocked or hidden. Playtest: after a build no cutout cell existed at all and the log
        /// said nothing; this line says which step lost them.
        /// </summary>
        private void VerifyZoning()
        {
            if (!DebugBuildChecks) return;
            var plan = _request?.Plan;
            if (plan?.ZoneCells == null) return;
            var planned = new List<float2>();
            foreach (var c in plan.ZoneCells)
                if (c.State == ZoneCellState.Zoned) planned.Add(0.25f * (c.A + c.B + c.C + c.D));
            if (planned.Count == 0) return;
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            foreach (var p in planned) { min = math.min(min, p); max = math.max(max, p); }
            var search = World.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
            var tree = search.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new NativeList<Entity>(64, Allocator.Temp);
            int zoned = 0, empty = 0, blocked = 0, hidden = 0, found = 0;
            var causes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var examples = new List<string>();
            try
            {
                var it = new ZoneBlockIterator { Bounds = new Bounds2(min - 60f, max + 60f), Results = results };
                tree.Iterate(ref it);
                var state = new int[planned.Count];   // 0 none, 1 hidden, 2 blocked, 3 empty, 4 zoned (best wins)
                var where = new (Entity block, int2 cell, float off)[planned.Count];
                var seen = new HashSet<Entity>();
                for (int i = 0; i < results.Length; i++)
                {
                    var e = results[i];
                    if (!seen.Add(e) || !EntityManager.HasComponent<global::Game.Zones.Block>(e) || !EntityManager.HasBuffer<global::Game.Zones.Cell>(e)) continue;
                    if (EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                    var block = EntityManager.GetComponentData<global::Game.Zones.Block>(e);
                    var cells = EntityManager.GetBuffer<global::Game.Zones.Cell>(e, true);
                    if (cells.Length < block.m_Size.x * block.m_Size.y) continue;
                    for (int y = 0; y < block.m_Size.y; y++)
                        for (int x = 0; x < block.m_Size.x; x++)
                        {
                            var at = global::Game.Zones.ZoneUtils.GetCellPosition(block, new int2(x, y)).xz;
                            for (int k = 0; k < planned.Count; k++)
                            {
                                float d2 = math.distancesq(at, planned[k]);
                                if (d2 > 4f) continue;
                                var cell = cells[x + y * block.m_Size.x];
                                int st = (cell.m_State & global::Game.Zones.CellFlags.Blocked) != 0 ? 2
                                    : (cell.m_State & global::Game.Zones.CellFlags.Visible) == 0 ? 1
                                    : cell.m_Zone.Equals(global::Game.Zones.ZoneType.None) ? 3 : 4;
                                if (st > state[k]) { state[k] = st; where[k] = (e, new int2(x, y), math.sqrt(d2)); }
                            }
                        }
                }
                try { AuditZonePlots(plan, planned, state); }
                catch (Exception ze) { Mod.Log.Warn("Placemaker zoning plot audit failed: " + ze.Message); }
                for (int k = 0; k < planned.Count; k++)
                {
                    int st = state[k];
                    if (st > 0) found++;
                    if (st == 4) zoned++; else if (st == 3) empty++; else if (st == 1) hidden++;
                    else if (st == 2)
                    {
                        blocked++;
                        string why = BlockedCause(where[k].block, where[k].cell);
                        causes.TryGetValue(why, out int n);
                        causes[why] = n + 1;
                        if (examples.Count < 6) examples.Add($"{why} at ({planned[k].x:F0}, {planned[k].y:F0}) row {where[k].cell.y}, {where[k].off:F1} m off plan");
                    }
                }
            }
            finally { results.Dispose(); }
            string msg = $"Placemaker zoning: group {_groupId}: zoned {zoned} of {planned.Count} cutout cell(s) ({_zoningEmitted} quad(s)); "
                + $"empty {empty}, blocked {blocked}, hidden {hidden}, not found {planned.Count - found}.";
            if (zoned < planned.Count) Mod.Log.Warn(msg); else Mod.Log.Info(msg);
            if (blocked > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in causes) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(" x").Append(kv.Value);
                Mod.Log.Warn($"Placemaker zoning: blocked cutout cells by cause: {sb}. e.g. {string.Join("; ", examples)}.");
            }
        }

        /// <summary>
        /// (s26, s36) What blocks a live cell. The direct sources of Game.Zones.CellBlockJobs (one of our zone block lots,
        /// one of the build's nets, another physical lot) are looked for in this cell and every cell in front of it
        /// (CleanBlockedCells blocks every cell behind a blocked one, and row 0 behind a blocked row 1); when none is
        /// found the column was cut by the game's depth rule (CellOverlapJobs: a column deeper than its neighbours is
        /// cut to theirs, a lone 1-column cutout entirely) — reported with the neighbours' depths.
        /// </summary>
        private string BlockedCause(Entity blockEntity, int2 xy)
        {
            if (blockEntity == Entity.Null || !EntityManager.Exists(blockEntity)) return "unknown";
            var block = EntityManager.GetComponentData<global::Game.Zones.Block>(blockEntity);
            var cells = EntityManager.GetBuffer<global::Game.Zones.Cell>(blockEntity, true);
            int rows = math.min(block.m_Size.y - 1, math.max(xy.y, 1));
            for (int y = 0; y <= rows; y++)
            {
                if ((cells[xy.x + y * block.m_Size.x].m_State & global::Game.Zones.CellFlags.Blocked) == 0) continue;
                string direct = DirectCause(block, new int2(xy.x, y));
                if (direct == null) continue;
                return y == xy.y ? direct : $"{direct} at row {y} (CleanBlockedCells: the column behind it is blocked)";
            }
            int Depth(int x)
            {
                if (x < 0 || x >= block.m_Size.x) return -1;
                int y = 0;
                while (y < block.m_Size.y && (cells[x + y * block.m_Size.x].m_State & global::Game.Zones.CellFlags.Blocked) == 0) y++;
                return y;
            }
            return $"the game's depth rule (CellOverlapJobs ReduceDepth: a column deeper than its neighbours, depths left {Depth(xy.x - 1)} / right {Depth(xy.x + 1)}; a lone column is cut to 0) or a city net";
        }

        /// <summary>A direct blocking source of the cell (CellBlockJobs), or null.</summary>
        private string DirectCause(global::Game.Zones.Block block, int2 xy)
        {
            var centre = global::Game.Zones.ZoneUtils.GetCellPosition(block, xy).xz;
            float2 d = block.m_Direction, r = new float2(d.y, -d.x);
            var sq = new[] { centre + (d + r) * 3.98f, centre + (d - r) * 3.98f, centre - (d + r) * 3.98f, centre - (d - r) * 3.98f };
            foreach (var ring in _blockerRings)
                if (ObstacleCut.Overlaps(sq, ring)) return "our zone block lot";
            var plan = _request.Plan;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                {
                    if (n == null || n.Role == NetRole.Fence) continue;
                    float hw = Composer.DefaultHalfWidth(n.Role);
                    for (int i = 0; i <= 16; i++)
                    {
                        var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz;
                        if (Poly.Contains(sq, p) || Poly.DistanceToBoundary(sq, p) < hw)
                            return "our " + n.Role + " net" + (string.IsNullOrEmpty(n.Asset.Id) ? "" : " " + n.Asset.Id);
                    }
                }
            }
            var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Areas.Area>(), ComponentType.ReadOnly<global::Game.Areas.Node>(),
                ComponentType.ReadOnly<global::Game.Prefabs.PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using (var areas = query.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < areas.Length; i++)
                {
                    var a = areas[i];
                    var prefab = EntityManager.GetComponentData<global::Game.Prefabs.PrefabRef>(a).m_Prefab;
                    if (!EntityManager.HasComponent<global::Game.Prefabs.AreaGeometryData>(prefab)) continue;
                    var gd = EntityManager.GetComponentData<global::Game.Prefabs.AreaGeometryData>(prefab);
                    if ((gd.m_Flags & global::Game.Areas.GeometryFlags.PhysicalGeometry) == 0) continue;
                    var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(a, true);
                    if (nodes.Length < 3) continue;
                    var ring = new float2[nodes.Length];
                    for (int j = 0; j < ring.Length; j++) ring[j] = nodes[j].m_Position.xz;
                    if (!ObstacleCut.Overlaps(sq, ring)) continue;
                    bool ours = EntityManager.HasComponent<PlacemakerMember>(a);
                    return (ours ? "our lot " : "lot ") + _prefabs.GetPrefabName(prefab);
                }
            return null;
        }
    }
}
