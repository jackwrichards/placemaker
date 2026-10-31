// Paint mode (Grid Road Generator's brush): drag to paint, right-drag to erase, Shift + move the
// mouse to rotate a square brush. Size comes from the panel (1–1000 m). The largest painted
// island becomes the outline (holes filled — CS2 areas cannot have holes).
//
// s15: (1) "Paint: keep off buildings" (Options, default on) cuts building lots out of the outline
// (Core/Geometry/ObstacleCut; enclosed lots are opened with a 1 m corridor like Block paint). Off = the
// legacy outline; the build never touches buildings either way (Core/Build/SiteSafety). Buildings
// near the brush are shaded red while painting. (2) Performance: the outline (marching squares +
// smoothing + keep-off-roads booleans + attachment search) is rebuilt once on mouse-up instead of
// ~7×/s while dragging; a stamp only counts as a change when it changed cells.

using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private float2 _lastStroke;

        public BrushMask Paint => _paint;
        public bool Painting => _gesture == Gesture.Paint || _gesture == Gesture.Erase;

        /// <summary>
        /// (s15) The brush takes input and its cursor / Block paint shading are drawn: Area stage, brush mode, tool
        /// active, not placing markers, mouse over the world — or a stroke still running (Core/Tool/BrushCursor).
        /// </summary>
        public bool BrushLive => BrushCursor.Live(IsActive, _stage, _mode, _markerKind != null || _touchKind != null, _in.OverWorld, Painting);

        /// <summary>
        /// (s27) Plain Paint honours the shared brush rules: it cuts what the polygon stops name (buildings, other
        /// Placemaker builds, lots, zoning) out of its outline and drops cells under water; roads are its keep-off-roads
        /// clip. "Paint over anything" = the old Paint. (s15's "Paint: keep off buildings" option is superseded.)
        /// </summary>
        public bool PaintAvoidsBuildings
        {
            get
            {
                var r = BrushRules;
                return r.WaterOn || r.Stops(BrushObstacleKind.Building) || r.Stops(BrushObstacleKind.PlacemakerBuild) || r.Stops(BrushObstacleKind.Lot)
                    || r.Stops(BrushObstacleKind.ZonedCell) || r.Stops(BrushObstacleKind.UnbuiltZonedCell) || r.Stops(BrushObstacleKind.EmptyZoneCell);
            }
        }

        private void UpdatePaint()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.Hit;
            float radius = _brushSize * 0.5f;

            if (_gesture == Gesture.Paint || _gesture == Gesture.Erase)
            {
                if (_in.LeftHeld)
                {
                    if (_in.HasHit)
                    {
                        int v = _paint.Version;
                        if (_paint.Stroke(_lastStroke, _in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle))
                            _lastStroke = _in.Hit.xz;
                        // Only a stroke that changed cells is an input change (holding still costs nothing).
                        if (_paint.Version != v) ShapeChanged(throttled: true);
                        if (PaintAvoidsBuildings) EnsurePaintBuildings(_in.Hit.xz, radius);
                    }
                    return;
                }
                var g = _gesture;
                _gesture = Gesture.None;
                ShapeChanged();
                Commit(g == Gesture.Erase ? "Placemaker.Undo.Erase" : "Placemaker.Undo.Paint");
                if (!_paint.IsEmpty && _shape == null)
                    Flash(new LocText(_paintOnlyBuildings ? "Placemaker.Problem.OnlyBuildings" : "Placemaker.Problem.PaintTooSmall"));
                return;
            }

            // Shift + mouse movement turns a square brush (the cursor stays where it is visually).
            if (_in.Shift && _brushSquare && !_in.LeftHeld && !_in.RightHeld)
            {
                if (math.abs(_in.MouseDelta.x) > 0.01f)
                {
                    _brushAngle = ((_brushAngle + _in.MouseDelta.x * 0.5f) % 360f + 360f) % 360f;
                    Touch();
                }
                return;
            }

            // Right-click starts over (the area is cleared); Shift+drag erases.
            if (RightClickStartsOver()) return;
            if (!_in.HasHit) return;
            if (_in.LeftPressed)
            {
                _gesture = _in.Shift ? Gesture.Erase : Gesture.Paint;
                _lastStroke = _in.Hit.xz;
                if (PaintAvoidsBuildings) EnsurePaintBuildings(_in.Hit.xz, radius, strokeStart: true);
                _paint.Stamp(_in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle);
                ShapeChanged(throttled: true);
            }
        }

        // ───────────── buildings for plain Paint (s15) ─────────────

        /// <summary>Building lots/footprints around the paint (gathered per stroke, not per frame).</summary>
        private List<float2[]> _paintBuildings = new List<float2[]>();
        private System.Func<float2, bool> _paintWater;
        private BrushRules _paintRules;
        private float2 _paintBuildingsMin, _paintBuildingsMax;
        private float _paintBuildingsAt = -100f;
        private int _paintBuildingsVersion;
        private bool _paintOnlyBuildings;

        /// <summary>Buildings gathered around the current paint (overlay: shaded while painting).</summary>
        public IReadOnlyList<float2[]> PaintBuildings => _paintBuildings;
        public int PaintBuildingsVersion => _paintBuildingsVersion;

        /// <summary>Re-reads buildings when the brush leaves the gathered box (or at a stroke start after 5 s).</summary>
        private void EnsurePaintBuildings(float2 at, float radius, bool strokeStart = false)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            bool inside = math.all(at - radius >= _paintBuildingsMin) && math.all(at + radius <= _paintBuildingsMax);
            if (inside && !(strokeStart && now - _paintBuildingsAt > 5f)) return;
            float pad = math.max(150f, radius * 2f);
            float2 min = at - radius - pad, max = at + radius + pad;
            var o = _shape?.Ring;
            if (o != null && o.Length > 0)
            {
                // Keep covering what is already painted (the outline is cut against all of it).
                foreach (var p in o) { min = math.min(min, p.xz - 10f); max = math.max(max, p.xz + 10f); }
            }
            ReadPaintBuildings(min, max, strokeStart);
        }

        private void ReadPaintBuildings(float2 min, float2 max, bool fresh = false)
        {
            _bpObstacles ??= new BlockObstacles(EntityManager, World);
            var rules = BrushRules;
            _paintBuildings = rules.Active(_bpObstacles.Gather(min, max, _editingGroup, fresh), k => k != BrushObstacleKind.Road);
            _paintWater = rules.WaterOn ? _bpObstacles.Water(min, max, _paint.CellSize, fresh) : null;
            _paintRules = rules;
            _paintBuildingsMin = min;
            _paintBuildingsMax = max;
            _paintBuildingsAt = UnityEngine.Time.realtimeSinceStartup;
            _paintBuildingsVersion++;
        }

        private AreaShape BuildPaintShape()
        {
            _paintOnlyBuildings = false;
            if (_paint.IsEmpty) return null;
            bool stops = PaintAvoidsBuildings;
            // Obstacles gathered under other rules (changed in the panel since): read them again.
            if (stops && (_paintRules == null || !_paintRules.Equals(BrushRules))) _paintBuildingsMin = new float2(float.MaxValue);
            var outline = _paint.Outline();
            if (outline.Count < 3) return null;
            var ring2 = new float2[outline.Count];
            for (int i = 0; i < ring2.Length; i++) ring2[i] = outline[i].xz;
            if (stops)
            {
                // The gathered box must cover the whole outline (undo/redo restores paint without a stroke).
                Poly.Bounds(ring2, out var mn, out var mx);
                if (math.any(mn < _paintBuildingsMin) || math.any(mx > _paintBuildingsMax)) ReadPaintBuildings(mn - 10f, mx + 10f);
                if (_paintWater != null)
                {
                    // Cells under water are left out before the outline is traced (the same cells Block paint never paints).
                    var dry = BrushMask.FromSnapshot(_paint.Save());
                    dry.EraseWhere(_paintWater);
                    var o2 = dry.IsEmpty ? new List<float3>() : dry.Outline();
                    if (o2.Count < 3) { _paintOnlyBuildings = true; return null; }
                    ring2 = new float2[o2.Count];
                    for (int i = 0; i < ring2.Length; i++) ring2[i] = o2[i].xz;
                }
                var cut = ObstacleCut.Subtract(ring2, ObstacleCut.Grow(_paintBuildings, Placemaker.Core.Build.SiteSafety.Margin), out int touched, 100f);
                if (cut == null) { _paintOnlyBuildings = touched > 0; return null; }
                ring2 = cut;
            }
            // y = terrain height. Largest island, holes filled (CS2 areas cannot have holes).
            var ring = Poly.To3D(ring2, p => _site.SampleHeight(p));
            var shape = new AreaShape { Ring = ring, Source = AreaSource.Painted };
            WithFoundAttachments(shape);
            return shape;
        }
    }
}
