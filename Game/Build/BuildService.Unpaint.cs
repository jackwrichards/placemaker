// Placemaker — the ground paint of a removed build wiped (GPL-3.0).
//
// p1-bulldoze (the user: "bulldozing a place sometimes leaves things behind"): a build's terrain texture paint (fields,
// worn paths, sand, dirt; BuildService.Paint) lives in the city's splatmap, not in an entity, so removing the group left
// the whole picture of the place painted on the ground. GroupRemovalSystem now asks for it to be wiped before the group's
// record goes: one clear-all paint (layer 0, EraseDither on every channel) over the build's outline, rasterised like the
// build's own paint (PaintRaster, on a worker thread) and applied one stamp per frame through the same ApplyStamp. The
// soft edge sits inside the outline, so a neighbouring build's paint is never touched. Terraform (heights) stays, like
// the game's own bulldozer leaves the ground as it is.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private sealed class Unpaint
        {
            public Task<List<PaintStamp>> Job;
            public List<PaintStamp> Stamps;
            public int Cursor, LastFrame, Frames;
            public readonly List<UnityEngine.Texture2D> Textures = new List<UnityEngine.Texture2D>();
        }

        private readonly Dictionary<int, Unpaint> _unpaints = new Dictionary<int, Unpaint>();
        private const int UnpaintMaxFrames = 1800;

        /// <summary>
        /// One step of wiping the paint of group <paramref name="groupId"/> over <paramref name="ring"/> (its outline). The first
        /// call starts it; true once it is done (or cannot be done: no brush, no outline, a failure, or 30 s). Main thread.
        /// </summary>
        internal bool StepUnpaint(int groupId, float3[] ring)
        {
            try
            {
                if (!_unpaints.TryGetValue(groupId, out var u))
                {
                    if (ring == null || ring.Length < 3 || !ResolveTerraformTools() || !ResolvePaintTools() || !_paintTools.ContainsKey(0)) return true;
                    float texel = 3.5f;
                    try { var area = _terrain.playableArea; if (area.x > 1f) texel = area.x / 4096f; } catch (Exception) { }
                    var opts = new PaintRasterOptions { Cell = texel * 0.5f, Align = texel, TileCells = PaintStampTexels, MaxStamps = PaintMaxStamps };
                    var paint = new PlannedPaint { Ring = (float3[])ring.Clone(), Layer = 0, Strength = 1f, Feather = 0.75f };
                    u = new Unpaint { Job = Task.Run(() => PaintRaster.Build(new List<PlannedPaint> { paint }, opts)) };
                    _unpaints[groupId] = u;
                    return false;
                }
                int frame = UnityEngine.Time.frameCount;
                if (++u.Frames > UnpaintMaxFrames) { Mod.Log.Warn($"Placemaker: wiping the ground paint of group {groupId} took too long; stopped."); return EndUnpaint(groupId, u); }
                if (u.Stamps == null)
                {
                    if (!u.Job.IsCompleted) return false;
                    if (u.Job.IsFaulted || u.Job.IsCanceled)
                    {
                        Mod.Log.Warn($"Placemaker: the ground paint of group {groupId} could not be wiped: " + u.Job.Exception?.GetBaseException().Message);
                        return EndUnpaint(groupId, u);
                    }
                    u.Stamps = u.Job.Result ?? new List<PaintStamp>();
                    u.LastFrame = frame;
                    Mod.Log.Info($"Placemaker wipes the ground paint of group {groupId}: {u.Stamps.Count} stamp(s), {PaintRaster.PaintedArea(u.Stamps):0} m².");
                }
                if (u.Cursor < u.Stamps.Count)
                {
                    var stamp = u.Stamps[u.Cursor++];
                    int before = _paintTextures.Count;
                    ApplyStamp(stamp);
                    // the stamp's mask texture is this wipe's to destroy, not the build's
                    for (int i = _paintTextures.Count - 1; i >= before; i--) { u.Textures.Add(_paintTextures[i]); _paintTextures.RemoveAt(i); }
                    u.LastFrame = frame;
                    return false;
                }
                if (frame - u.LastFrame < PaintSettleFrames) return false;   // GPU work done before the textures go
                return EndUnpaint(groupId, u);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Placemaker: wiping the ground paint of group {groupId} failed: " + e.Message);
                if (_unpaints.TryGetValue(groupId, out var u)) EndUnpaint(groupId, u);
                return true;
            }
        }

        private bool EndUnpaint(int groupId, Unpaint u)
        {
            foreach (var t in u.Textures) if (t != null) UnityEngine.Object.Destroy(t);
            _unpaints.Remove(groupId);
            return true;
        }
    }
}
