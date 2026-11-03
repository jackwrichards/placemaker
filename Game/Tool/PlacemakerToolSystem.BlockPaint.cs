// Block paint mode ("Paint area" with Smart edges): pressing the mouse picks where the stroke may paint, the stroke
// then paints with the normal brush fenced to it.
//
// Area overhaul (2026-10-17): with "One block" on, the fence is the Paint bucket's fill at the stroke's start (Core
// AreaFill: the shared stops, closed gaps, no cracks) — the same region the bucket would take, so the two tools agree;
// with it off, a work box around the start where every stop (road and path surfaces with their junctions and turning
// circles, buildings, lots, zoning, my builds) is fenced out, kept on the start's side of the roads. The walls are the
// exact road surfaces (Game/Selection/AreaWalls): the old fence joined curbs straight across junctions and left
// dead ends' turning circles paintable ("cul-de-sacs, it doesn't work"). Cells are 2 m (were 4: "it's kind of blocky").
//
// Gestures (same as Paint): drag = paint, right-drag = erase, plus Shift+drag = erase; Shift + move turns a square brush.
// A paint stroke that starts outside the current fence starts a new selection there (the old one is one undo step
// away). Erase strokes never switch.

using System;
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
        /// <summary>Brush cell (m) of Paint area.</summary>
        public const float BlockPaintCell = 2f;

        /// <summary>The region being painted, its brush fence and the stops shown as "won't paint here".</summary>
        public sealed class BlockPaintContext
        {
            /// <summary>One block: the fill the stroke started in; null in the open work box.</summary>
            public float2[] Ring;
            public bool Open;
            public float2 Seed;
            /// <summary>The fence's outline (the fill, or the work box) for the overlay; null when unconstrained.</summary>
            public float3[] Block;
            public BlockBrush Brush;
            public List<float3[]> Obstacles = new List<float3[]>();
            /// <summary>Closed gaps of the fill (overlay).</summary>
            public List<float3[]> Doors = new List<float3[]>();
            public BrushRules Rules;
            public int IgnoreGroup;
            public float BuiltAt;
        }

        /// <summary>Obstacles are re-read at most this often (s) between strokes: the city keeps growing.</summary>
        private const float BlockPaintRefresh = 5f;

        private readonly BrushMask _bpMask = new BrushMask(BlockPaintCell);
        private float2[] _bpRing;
        private bool _bpOpen;
        private float2 _bpSeed;
        private BlockPaintContext _bpCtx;
        private BlockObstacles _bpObstacles;
        private bool _bpEraseWithLeft;

        public BrushMask BlockPaintMask => _bpMask;
        private bool BlockPaintHasTarget => _bpOpen || _bpRing != null;
        public BlockPaintContext BlockPaintCurrent => _bpCtx != null && IsCurrentTarget(_bpCtx) ? _bpCtx : null;

        private bool IsCurrentTarget(BlockPaintContext c) =>
            _bpOpen ? c.Open && math.all(c.Seed == _bpSeed) : !c.Open && ReferenceEquals(c.Ring, _bpRing);

        /// <summary>The player's brush rules (Options / the panel's Stops at); plain Paint never reads them.</summary>
        public BrushRules BrushRules => _settings != null ? _settings.GetBrushRules() : BrushRules.Defaults();

        public void SetBrushRules(BrushRules rules)
        {
            if (rules == null) return;
            rules = rules.Normalized();
            var before = BrushRules;
            if (rules.Equals(before)) return;
            bool fillChanged = !rules.ForOutline().SameWalls(before.ForOutline()) || rules.Gap != before.Gap || rules.Reach != before.Reach;
            if (_settings != null) { _settings.SetBrushRules(rules); _settings.ApplyAndSave(); }
            // The fence is rebuilt from the cached walls on the next frame (UpdateBlockPaint) and the painted outline at
            // the next stroke; the bucket fills its spots again; Points cut their outline again.
            if (fillChanged)
            {
                _fills?.Invalidate();
                _hoverFace = null;
                _hoverFaceAt = new float2(float.MaxValue);
                if (_mode == SelectionMode.Block) RepickBlocks();
                else if (_mode == SelectionMode.Draw && _draw.Closed) ShapeChanged();
            }
            Touch();
        }

        public void ResetBrushRules() => SetBrushRules(BrushRules.Defaults());

        /// <summary>A new stroke at p continues the current selection (inside the same fill / the same side of the roads).</summary>
        private static bool Continues(BlockPaintContext c, float2 p) =>
            c?.Brush != null && (c.Open ? c.Brush.CanPaintAt(p) : c.Ring != null && Poly.Contains(c.Ring, p));

        private void UpdateBlockPaint()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.Hit;
            Fills.Pump();
            float radius = _brushSize * 0.5f;

            // A rule / Smart edges change re-fences once from the cached walls (shading + fence); no outline rebuild.
            if (_gesture != Gesture.Paint && _gesture != Gesture.Erase && _bpCtx != null && BlockPaintHasTarget && IsCurrentTarget(_bpCtx) && !BrushRules.Equals(_bpCtx.Rules))
                EnsureBlockPaintContext(false);

            if (_gesture == Gesture.Paint || _gesture == Gesture.Erase)
            {
                bool held = _gesture == Gesture.Paint || _bpEraseWithLeft ? _in.LeftHeld : _in.RightHeld;
                if (held)
                {
                    _bpStrokeFrames++;
                    if (_in.HasHit)
                    {
                        int v = _bpMask.Version;
                        if (_bpMask.Stroke(_lastStroke, _in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle))
                            _lastStroke = _in.Hit.xz;
                        // Only a stroke that changed cells is an input change; the outline is rebuilt on mouse-up.
                        if (_bpMask.Version != v) ShapeChanged(throttled: true);
                    }
                    PumpPaintPreview(BlockPaintCurrent);
                    return;
                }
                var g = _gesture;
                _gesture = Gesture.None;
                _bpPreview = null;
                Mod.Log.Info($"Placemaker: Paint area stroke ended after {_bpStrokeFrames} frames, {_bpMask.PaintedCellCount} cells painted");
                ShapeChanged();
                Commit(g == Gesture.Erase ? "Placemaker.Undo.Erase" : "Placemaker.Undo.Paint");
                if (!_bpMask.IsEmpty && _shape == null) Flash(new LocText("Placemaker.Problem.PaintTooSmall"));
                return;
            }

            // Hover: the fill a new stroke would paint in (asked in the background when the cursor leaves it).
            if (_in.HasHit && BrushRules.LockOn)
            {
                if (Continues(BlockPaintCurrent, _in.Hit.xz)) _hoverFace = null;
                else HoverFill(_in.Hit.xz);
            }
            else _hoverFace = null;

            // Shift + mouse movement turns a square brush (as in Paint).
            if (_in.Shift && _brushSquare && !_in.LeftHeld && !_in.RightHeld)
            {
                if (math.abs(_in.MouseDelta.x) > 0.01f)
                {
                    _brushAngle = ((_brushAngle + _in.MouseDelta.x * 0.5f) % 360f + 360f) % 360f;
                    Touch();
                }
                return;
            }

            if (!_in.HasHit || !(_in.LeftPressed || _in.RightPressed)) return;
            bool erase = _in.RightPressed || _in.Shift;
            if (!erase && !StartBlockPaintStroke(_in.Hit.xz)) return;
            if (erase && _bpMask.IsEmpty) return;

            _gesture = erase ? Gesture.Erase : Gesture.Paint;
            _bpStrokeFrames = 0;
            _bpEraseWithLeft = erase && !_in.RightPressed;
            _lastStroke = _in.Hit.xz;
            _bpMask.Stamp(_in.Hit.xz, radius, erase, _brushSquare, _brushAngle);
            ShapeChanged(throttled: true);
        }

        /// <summary>
        /// Makes the fill (or, with One block off or no fill here, the road side) under p the painted one — a new
        /// selection if it is another one. False = nothing can be painted here.
        /// </summary>
        private bool StartBlockPaintStroke(float2 p)
        {
            var cur = EnsureBlockPaintContext(false);
            if (!Continues(cur, p))
            {
                FillResult fill = null;
                if (BrushRules.LockOn)
                {
                    var pick = PickAt(p, out _hoverFaceProblem);
                    fill = pick?.Fill;
                }
                _bpMask.Clear();
                if (fill != null && fill.Status == FillStatus.Ok)
                {
                    _bpRing = fill.Ring;
                    _bpOpen = false;
                    _bpDoors = fill.Doors;
                }
                else
                {
                    // One block off, or nothing to fill here (on a wall's edge): free painting fenced by the stops.
                    _bpRing = null;
                    _bpOpen = true;
                    _bpDoors = null;
                }
                _bpSeed = p;
                _hoverFace = null;
                cur = EnsureBlockPaintContext(true);
                LocText refused = cur == null ? new LocText("Placemaker.Problem.NoBlockHere")
                                : cur.Brush.Full || !cur.Brush.CanPaintAt(p) && cur.Open ? new LocText(cur.Open ? "Placemaker.Problem.CannotPaintHere" : "Placemaker.Problem.BlockFull") : null;
                if (refused != null)
                {
                    // Nothing was painted: go back to the committed selection.
                    if (_history.Current != null) RestoreSelectionOnly(_history.Current);
                    Flash(refused);
                    return false;
                }
            }
            else if (UnityEngine.Time.realtimeSinceStartup - cur.BuiltAt > BlockPaintRefresh) cur = EnsureBlockPaintContext(true) ?? cur;

            if (cur.Brush.Full) { Flash(new LocText(cur.Open ? "Placemaker.Problem.CannotPaintHere" : "Placemaker.Problem.BlockFull")); return false; }
            cur.Brush.Attach(_bpMask);
            return true;
        }

        private List<float2[]> _bpDoors;
        private int _bpStrokeFrames;

        // ───────────── live outline while painting ─────────────
        // The user: "when I'm drawing the blocks, they don't always fully represent the final paint". While a stroke runs
        // the outline it would give (smoothing, curb snap, exact cut) is worked out again in the background every few
        // frames from a copy of the painted cells, and the overlay draws it over the cells.

        private System.Threading.Tasks.Task<float2[]> _bpPreviewJob;
        private int _bpPreviewJobVersion = -1;
        private float _bpPreviewAt;
        private float2[] _bpPreview;

        /// <summary>The outline the current stroke would give (XZ), or null (none yet, or not painting).</summary>
        public float2[] BlockPaintPreview => Painting && _mode == SelectionMode.BlockPaint ? _bpPreview : null;

        private void PumpPaintPreview(BlockPaintContext ctx)
        {
            if (_bpPreviewJob != null)
            {
                if (!_bpPreviewJob.IsCompleted) return;
                if (_bpPreviewJob.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) _bpPreview = _bpPreviewJob.Result;
                _bpPreviewJob = null;
            }
            if (ctx?.Brush == null || _bpMask.IsEmpty || _bpMask.Version == _bpPreviewJobVersion) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _bpPreviewAt < 0.1f) return;
            _bpPreviewAt = now;
            _bpPreviewJobVersion = _bpMask.Version;
            var snap = _bpMask.Save();
            var brush = ctx.Brush;
            float cell = _bpMask.CellSize;
            _bpPreviewJob = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var m = new BrushMask(cell);
                    m.Restore(snap);
                    lock (brush) return brush.Outline(m)?.Ring;
                }
                catch (Exception) { return null; }
            });
        }

        /// <summary>
        /// Fence + stops for the current target; rebuilt when the target, the rules or the edited group changed (or when
        /// forced). Null when there is no target.
        /// </summary>
        private BlockPaintContext EnsureBlockPaintContext(bool refresh)
        {
            if (!BlockPaintHasTarget) return null;
            var rules = BrushRules;
            var c = _bpCtx;
            if (!refresh && c != null && IsCurrentTarget(c) && rules.Equals(c.Rules) && c.IgnoreGroup == _editingGroup)
            {
                if (_bpMask.Limit != c.Brush.Allowed) c.Brush.Attach(_bpMask);
                return c;
            }
            if (refresh) _areaWalls?.Invalidate();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { c = _bpOpen ? BuildOpenContext(rules) : BuildFillContext(rules); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: Paint area fence failed: " + e.Message); c = null; }
            if (c == null) return null;
            Mod.Log.Info("Placemaker: Paint area fence built in " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (" + (c.Open ? "open box" : "one block") + "), "
                + c.Obstacles.Count + " shaded, rules " + RulesLine(rules) + "; walls " + (_areaWalls?.LastStats ?? ""));
            c.Rules = rules;
            c.IgnoreGroup = _editingGroup;
            c.BuiltAt = UnityEngine.Time.realtimeSinceStartup;
            _bpCtx = c;
            c.Brush.Attach(_bpMask);
            return c;
        }

        /// <summary>One block: paint only inside the fill (it already keeps off every stop, closes gaps, skips cracks).</summary>
        private BlockPaintContext BuildFillContext(BrushRules rules)
        {
            var ring = _bpRing;
            if (ring == null || ring.Length < 3) return null;
            var brush = new BlockBrush(ring, null, _bpMask.CellSize) { SnapDistance = rules.Snap, Smoothing = rules.Smoothing };
            var c = new BlockPaintContext { Ring = ring, Block = Poly.To3D(ring, q => _site.SampleHeight(q)), Brush = brush, Seed = _bpSeed };
            if (_bpDoors != null) foreach (var d in _bpDoors) if (d != null && d.Length >= 2) c.Doors.Add(Poly.To3D(d, q => _site.SampleHeight(q)));
            // Shown as "won't paint here": the stops along the fill's edge.
            Poly.Bounds(ring, out var mn, out var mx);
            var walls = new List<BrushObstacle>();
            Walls.Counted(mn - 2f, mx + 2f, _editingGroup, rules, walls);
            foreach (var w in walls)
                if (w.Kind != BrushObstacleKind.Road && w.Kind != BrushObstacleKind.Path && w.Kind != BrushObstacleKind.Bike && w.Kind != BrushObstacleKind.Track && ShowsInBlock(w.Ring, ring))
                    c.Obstacles.Add(Poly.To3D(w.Ring, q => _site.SampleHeight(q)));
            return c;
        }

        /// <summary>Open land / One block off: a work box around the start with every stop fenced out.</summary>
        private BlockPaintContext BuildOpenContext(BrushRules rules)
        {
            float h = BlockBrush.OpenHalfSize;
            float2 min = _bpSeed - h, max = _bpSeed + h;
            var walls = new List<BrushObstacle>();
            if (!rules.Anything) Walls.Counted(min, max, _editingGroup, rules, walls);
            var water = Walls.Water(min, max, _bpMask.CellSize, rules.ForOutline());
            var brush = rules.Build(_bpSeed, null, walls, rules.WaterOn ? water : null, _bpMask.CellSize);
            var c = new BlockPaintContext { Ring = null, Open = true, Seed = _bpSeed, Block = null, Brush = brush };
            foreach (var w in walls)
                if (w.Kind != BrushObstacleKind.Road && w.Kind != BrushObstacleKind.Path && w.Kind != BrushObstacleKind.Bike && w.Kind != BrushObstacleKind.Track)
                    c.Obstacles.Add(Poly.To3D(w.Ring, q => _site.SampleHeight(q)));
            return c;
        }

        private static string RulesLine(BrushRules r) =>
            (r.Anything ? "anything " : "") + (r.Roads ? "roads " : "") + (r.Paths ? "paths " : "") + (r.Bikes ? "bikes " : "") + (r.Tracks ? "tracks " : "")
            + (r.Buildings ? "buildings " : "") + (r.Builds ? "builds " : "") + (r.Lots ? "lots " : "") + "zoning" + r.ZoningLevel + " "
            + (r.Water ? "water " : "") + (r.StayInBlock ? "oneBlock " : "") + "gap " + r.Gap.ToString("0") + " reach " + r.Reach.ToString("0");

        private static bool ShowsInBlock(float2[] obstacle, float2[] block)
        {
            if (obstacle == null || obstacle.Length < 3) return false;
            if (ObstacleCut.Overlaps(obstacle, block)) return true;
            foreach (var p in obstacle) if (Poly.DistanceToBoundary(block, p) < 1f) return true;
            return false;
        }

        /// <summary>The current stroke erases (overlay colours the brush accordingly).</summary>
        public bool Erasing => _gesture == Gesture.Erase;

        private BrushMask.Snapshot _bpSnap;
        private int _bpSnapVersion = -1;

        private BlockPaintState BlockPaintSnapshot()
        {
            BrushMask.Snapshot mask = null;
            if (!_bpMask.IsEmpty)
            {
                if (_bpSnap == null || _bpSnapVersion != _bpMask.Version)
                {
                    _bpSnap = _bpMask.Save();
                    _bpSnapVersion = _bpMask.Version;
                }
                mask = _bpSnap;
            }
            return new BlockPaintState { Ring = _bpRing, Doors = _bpDoors, Mask = mask, Open = _bpOpen, Seed = _bpSeed };
        }

        private void RestoreBlockPaint(BlockPaintState s)
        {
            if (s?.Mask != null) _bpMask.Restore(s.Mask); else _bpMask.Clear();
            _bpSnap = s?.Mask;
            _bpSnapVersion = _bpMask.Version;
            _bpRing = s?.Ring;
            _bpDoors = s?.Doors;
            _bpOpen = s != null && s.Open;
            _bpSeed = s?.Seed ?? default;
            // A snapshot from before the overhaul holds a road loop instead of a fill: paint on in the open box.
            if (s != null && !s.Open && s.Ring == null && s.Face != null && s.Face.Count > 0) _bpOpen = true;
        }

        private void ClearBlockPaint()
        {
            _bpMask.Clear();
            _bpRing = null;
            _bpDoors = null;
            _bpOpen = false;
        }

        private AreaShape BuildBlockPaintShape()
        {
            if (_bpMask.IsEmpty || !BlockPaintHasTarget) return null;
            var ctx = EnsureBlockPaintContext(false);
            if (ctx == null) return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BlockBrushOutline o;
            lock (ctx.Brush) o = ctx.Brush.Outline(_bpMask);
            if (sw.Elapsed.TotalMilliseconds > 30) Mod.Log.Info("Placemaker: Paint area outline in " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms");
            if (o?.Ring == null) return null;
            var shape = new AreaShape { Ring = Poly.To3D(o.Ring, p => _site.SampleHeight(p)), Source = AreaSource.Painted };
            WithFoundAttachments(shape);
            return shape;
        }
    }
}
