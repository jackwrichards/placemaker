// Block paint overlay: the block the brush is fenced to (faint dashed curb line), what will not be
// painted (zoned cells, lots, areas in a faint red tint), the block a new stroke would start in
// (dashed white), the painted cells / smoothed outline (as Paint) and the brush (red while erasing).

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Rendering;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Overlay
{
    public sealed partial class PlacemakerOverlaySystem
    {
        private static readonly Color Blocked = new Color(1f, 0.3f, 0.25f, 0.18f);
        private static readonly Color HoverBlock = new Color(1f, 1f, 1f, 0.45f);

        private int _bpVersion = -1;
        private float _bpBoundaryAt;
        /// <summary>Time (ms) of the last painted-cells outline (the tool's slow-frame log reads it).</summary>
        public static float BoundaryMs;
        private List<(float2 a, float2 b)> _bpBoundary = new List<(float2, float2)>();
        private PlacemakerToolSystem.BlockPaintContext _bpSigFor;
        private long _bpSig;
        private List<float3[]> _bpShown;
        private List<float3[]> _bpNear = new List<float3[]>();
        private PlacemakerToolSystem.BlockPaintContext _bpNearFor;
        private float2 _bpNearAt = new float2(float.MaxValue);

        /// <summary>
        /// Obstacles within reach of the brush (open-land fallback: the work box holds hundreds of road
        /// pieces and zone cells). Re-filtered only after the cursor moved 16 m, so the fill mesh is not
        /// rebuilt every frame.
        /// </summary>
        private List<float3[]> NearBrush(PlacemakerToolSystem.BlockPaintContext cur)
        {
            float2 at = _tool.CursorValid ? _tool.Cursor.xz : cur.Seed;
            if (ReferenceEquals(_bpNearFor, cur) && math.distance(at, _bpNearAt) < 16f) return _bpNear;
            _bpNearFor = cur;
            _bpNearAt = at;
            float reach = _tool.BrushSize * 0.5f + 60f;
            var list = new List<float3[]>();
            foreach (var o in cur.Obstacles)
            {
                float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                foreach (var p in o) { mn = math.min(mn, p.xz); mx = math.max(mx, p.xz); }
                float2 d = math.max(math.max(mn - at, at - mx), 0f);
                if (math.length(d) <= reach) list.Add(o);
            }
            _bpNear = list;
            return list;
        }

        // The ground under the painted cells (their corners lie on the 2 m brush grid): sampled once per corner, not per frame.
        private readonly Dictionary<long, float> _groundAt = new Dictionary<long, float>();
        private float _groundSince;

        /// <summary>A painted point on the ground (drawn where it is painted; was at the cursor's height and moved with it).</summary>
        private float3 OnGround(float2 p)
        {
            // the ground changes with every build: a cache older than a few seconds outside a stroke is read again
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!_tool.Painting && now - _groundSince > 3f) { _groundAt.Clear(); _groundSince = now; }
            long k = ((long)(int)math.round(p.x * 4f) << 32) ^ (uint)(int)math.round(p.y * 4f);
            if (!_groundAt.TryGetValue(k, out float y))
            {
                if (_groundAt.Count > 200000) _groundAt.Clear();
                y = _tool.GroundAt(p);
                _groundAt[k] = y;
            }
            return new float3(p.x, y, p.y);
        }

        private void DrawBlockPaintMode(OverlayRenderSystem.Buffer buffer, float w, AreaShape shape)
        {
            var cur = _tool.BlockPaintCurrent;
            // s15: shading and hover outline only while the brush is live (Area stage, over the world, no markers).
            bool selecting = _tool.BrushLive;
            if (cur != null)
            {
                // Open box: no fence line; the shading shows the stops near the brush.
                if (cur.Block != null && _tool.LayerVisible(AreaLegend.Fence)) Ring(buffer, cur.Block, OutlineDim, w * 0.8f, dashed: true);
                if (selecting && _tool.LayerVisible(AreaLegend.Doors))
                    foreach (var d in cur.Doors)
                        for (int i = 0; i + 1 < d.Length; i++) buffer.DrawDashedLine(DoorColor, new Line3.Segment(d[i], d[i + 1]), w * 1.4f, w * 3f, w * 2f);
                if (selecting && cur.Obstacles.Count > 0 && _tool.LayerVisible(AreaLegend.Stops))
                {
                    var shown = cur.Open ? NearBrush(cur) : cur.Obstacles;
                    if (!ReferenceEquals(_bpSigFor, cur) || !ReferenceEquals(_bpShown, shown))
                    {
                        _bpSigFor = cur;
                        _bpShown = shown;
                        _bpSig = FilledPreviewSystem.Signature(shown);
                    }
                    if (shown.Count > 0) _fill.SetLayer("blockPaintBlocked", shown, Blocked, -93.8f, _bpSig);
                }
            }

            var hover = _tool.HoverFace;
            // (a fill that only stopped at the bucket's reach is open land: Paint area has no disc there, so none is shown)
            if (hover?.Area != null && !(hover.Fill?.HitReach ?? false) && !_tool.Painting && _tool.BrushLive && _tool.LayerVisible(AreaLegend.Fence))
            {
                Ring(buffer, hover.Area.Ring, HoverBlock, w * 0.7f, dashed: true);
                DrawFillExtras(buffer, w, hover);
            }

            var mask = _tool.BlockPaintMask;
            // The cells' edges are found again over the whole painted area: during a stroke at most 8 times a second (lag
            // spikes painting fast, 2026-10-21: every frame the brush moved, more the bigger the area).
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (mask.Version != _bpVersion && (!_tool.Painting || now - _bpBoundaryAt > 0.125f))
            {
                _bpVersion = mask.Version;
                _bpBoundaryAt = now;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _bpBoundary = mask.RawBoundary();
                BoundaryMs = (float)sw.Elapsed.TotalMilliseconds;
            }
            // While painting: the outline the stroke would give (worked out in the background) over the painted cells, dim;
            // the cells alone until the first outline is in. Once the shape exists: its outline.
            if (_tool.Painting || shape == null)
            {
                var preview = _tool.BlockPaintPreview;
                int max = math.min(_bpBoundary.Count, 20000);
                var cellColor = preview != null ? OutlineDim : Outline;
                for (int i = 0; i < max; i++)
                {
                    var (a, b) = _bpBoundary[i];
                    buffer.DrawLine(cellColor, new Line3.Segment(OnGround(a), OnGround(b)), w * (preview != null ? 0.45f : 0.7f));
                }
                if (preview != null && _tool.LayerVisible(AreaLegend.Painted))
                    for (int i = 0; i < preview.Length; i++)
                    {
                        float2 a = preview[i], b = preview[(i + 1) % preview.Length];
                        buffer.DrawLine(Outline, new Line3.Segment(OnGround(a), OnGround(b)), w);
                    }
            }
            else if (_tool.LayerVisible(AreaLegend.Painted)) Ring(buffer, shape.Ring, Outline, w);

            var c = _tool.Erasing ? Erase : Brush;
            if (_tool.BrushLive) DrawBrushCursor(buffer, w, _tool.Painting ? c : new Color(c.r, c.g, c.b, 0.2f));
        }
    }
}
