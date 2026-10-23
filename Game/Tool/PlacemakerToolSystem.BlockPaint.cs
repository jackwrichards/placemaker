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
// No radius (2026-10-19, "it seems to have a radius limit"): the fill is the fence only where it is a closed block; where
// it ran into the bucket's reach (open land) the stroke paints in the open work box instead, and that box grows (up to
// MaxOpenSize a side) whenever the brush comes near its edge.

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
            /// <summary>Open: the work box it was built over.</summary>
            public float2 BoxMin, BoxMax;
        }

        /// <summary>Largest side (m) the open work box grows to.</summary>
        public const float MaxOpenSize = 5000f;
        /// <summary>The box grows when the brush comes this close (m, beyond its radius) to its edge.</summary>
        private const float OpenGrowMargin = 150f;
        private float2 _bpBoxMin, _bpBoxMax;

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
        public BrushRules BrushRules => OwnBrushRules ? _painterRules : PlayerBrushRules;

        /// <summary>The rules saved in the settings (the painter keeps its own copy for the session: PlacemakerToolSystem.Painter).</summary>
        private BrushRules PlayerBrushRules => _settings != null ? _settings.GetBrushRules() : BrushRules.Defaults();

        public void SetBrushRules(BrushRules rules)
        {
            if (rules == null) return;
            rules = rules.Normalized();
            var before = BrushRules;
            if (rules.Equals(before)) return;
            bool fillChanged = !rules.ForOutline().SameWalls(before.ForOutline()) || rules.Gap != before.Gap || rules.Reach != before.Reach;
            // the painter and a Zoning type use their own copy (BrushRules): a change goes there, or it would never show
            if (OwnBrushRules) _painterRules = rules;
            else if (_settings != null) { _settings.SetBrushRules(rules); _settings.ApplyAndSave(); }
            // The fence is rebuilt from the cached walls on the next frame (UpdateBlockPaint) and the painted outline at
            // the next stroke; the bucket fills its spots again; Points cut their outline again.
            if (fillChanged)
            {
                _fills?.Invalidate();
                _hoverFace = null;
                if (_mode == SelectionMode.Block) RepickBlocks();
                else if (_mode == SelectionMode.Draw && _draw.Closed) ShapeChanged();
            }
            Touch();
        }

        public void ResetBrushRules() => SetBrushRules(OwnBrushRules ? PainterRulesFrom(BrushRules.Defaults()) : BrushRules.Defaults());

        /// <summary>The painter or a Zoning type is in use: the brush rules are its own copy, not the settings' ones.</summary>
        private bool OwnBrushRules => (_painter || _wasPolishType) && _painterRules != null;

        /// <summary>A new stroke at p continues the current selection (inside the same fill / the same side of the roads).</summary>
        private static bool Continues(BlockPaintContext c, float2 p) =>
            c?.Brush != null && (c.Open ? c.Brush.CanPaintAt(p) : c.Ring != null && Poly.Contains(c.Ring, p));

        private void UpdateBlockPaint()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.Hit;
            Fills.Pump();
            PumpFence();
            CommitBlockPaintStroke(false);
            float radius = _brushSize * 0.5f;
            if (_bpStart != null) { PumpStrokeStart(radius); return; }

            // A rule / Smart edges change re-fences once from the cached walls (shading + fence); no outline rebuild.
            if (_gesture != Gesture.Paint && _gesture != Gesture.Erase && _bpCtx != null && BlockPaintHasTarget && IsCurrentTarget(_bpCtx) && !BrushRules.Equals(_bpCtx.Rules))
                EnsureBlockPaintContext(false);

            if (_gesture == Gesture.Paint || _gesture == Gesture.Erase)
            {
                bool held = _gesture == Gesture.Paint || _bpEraseWithLeft ? _in.LeftHeld : _in.RightHeld;
                if (held)
                {
                    _bpStrokeFrames++;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    if (_in.HasHit && _gesture == Gesture.Paint && _bpOpen) GrowOpenBox(_in.Hit.xz, radius);
                    double grow = sw.Elapsed.TotalMilliseconds;
                    if (_in.HasHit)
                    {
                        int v = _bpMask.Version;
                        var from = _lastStroke;
                        if (_bpMask.Stroke(_lastStroke, _in.Hit.xz, radius, _gesture == Gesture.Erase, _brushSquare, _brushAngle))
                        {
                            _bpBrushed.Add((from, _in.Hit.xz, radius, _gesture == Gesture.Erase));
                            _lastStroke = _in.Hit.xz;
                        }
                        // Only a stroke that changed cells is an input change; the outline is rebuilt on mouse-up.
                        if (_bpMask.Version != v) ShapeChanged(throttled: true);
                    }
                    double stroke = sw.Elapsed.TotalMilliseconds - grow;
                    PumpPaintPreview(BlockPaintCurrent);
                    double preview = sw.Elapsed.TotalMilliseconds - grow - stroke;
                    // Lag spikes painting fast (2026-10-21): a slow frame during a stroke says what this tool did in it.
                    float dt = UnityEngine.Time.unscaledDeltaTime * 1000f;
                    if (dt > 50f && _bpStrokeFrames > 2)
                        Mod.Log.Info($"Placemaker: Paint area slow frame {dt:0} ms during a stroke (frame {_bpStrokeFrames}, {_bpMask.PaintedCellCount} cells): "
                            + $"this frame grow {grow:0.0} ms, brush {stroke:0.0} ms, preview {preview:0.0} ms; last cell edges {Placemaker.Game.Overlay.PlacemakerOverlaySystem.BoundaryMs:0.0} ms");
                    return;
                }
                var g = _gesture;
                _gesture = Gesture.None;
                _bpPreview = null;
                Mod.Log.Info($"Placemaker: Paint area stroke ended after {_bpStrokeFrames} frames, {_bpMask.PaintedCellCount} cells painted");
                ShapeChanged();
                // the undo step holds the outline: taken once it is back from the worker
                _bpCommit = g == Gesture.Erase ? "Placemaker.Undo.Erase" : "Placemaker.Undo.Paint";
                CommitBlockPaintStroke(false);
                return;
            }

            // No fill on hover (2026-10-21, lag spikes): the fill a stroke paints in is worked out when it starts.
            _hoverFace = null;

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

            // Right-click starts over (the area is cleared); Shift+drag erases.
            if (_in.RightPressed) { CommitBlockPaintStroke(true); RightClickStartsOver(); return; }
            if (!_in.HasHit || !_in.LeftPressed) return;
            // a new stroke right after the last one: the last one's undo step first
            CommitBlockPaintStroke(true);
            if (_in.Shift)
            {
                if (_bpMask.IsEmpty) return;
                BeginBlockPaintGesture(_in.Hit.xz, radius, true, true);
                return;
            }
            // A paint stroke waits (a few frames) for the fill and the fence it paints in, worked out in the background;
            // the mouse path meanwhile is painted when it starts.
            _bpStart = new StrokeStart { At = _in.Hit.xz };
            _bpStart.Path.Add(_in.Hit.xz);
            PumpStrokeStart(radius);
        }

        /// <summary>The finished stroke's undo step, waiting for its outline (null: none).</summary>
        private string _bpCommit;

        /// <summary>Records the finished stroke once its outline is back (or now, worked out here, when <paramref name="now"/>).</summary>
        private void CommitBlockPaintStroke(bool now)
        {
            if (_bpCommit == null || !now && !BlockPaintOutlineReady()) return;
            var label = _bpCommit;
            _bpCommit = null;
            Commit(label);
            if (!_bpMask.IsEmpty && _shape == null) Flash(new LocText("Placemaker.Problem.PaintTooSmall"));
        }

        private void BeginBlockPaintGesture(float2 p, float radius, bool erase, bool eraseWithLeft)
        {
            _gesture = erase ? Gesture.Erase : Gesture.Paint;
            _bpStrokeFrames = 0;
            _bpEraseWithLeft = eraseWithLeft;
            _lastStroke = p;
            _bpMask.Stamp(p, radius, erase, _brushSquare, _brushAngle);
            _bpBrushed.Add((p, p, radius, erase));
            ShapeChanged(throttled: true);
        }

        /// <summary>A paint stroke pressed, waiting for its fill and fence (lag spikes, 2026-10-21: both used to be worked out on the press, 0.1–0.3 s).</summary>
        private sealed class StrokeStart
        {
            public float2 At;
            /// <summary>Where the mouse went while it waited.</summary>
            public List<float2> Path = new List<float2>();
            /// <summary>The target is chosen (the current selection, or a new one at At).</summary>
            public bool Decided;
        }

        private StrokeStart _bpStart;

        private void PumpStrokeStart(float radius)
        {
            var s = _bpStart;
            if (_in.HasHit && _in.LeftHeld && math.distance(_in.Hit.xz, s.Path[s.Path.Count - 1]) > 0.5f) s.Path.Add(_in.Hit.xz);
            if (!s.Decided)
            {
                if (!DecideStrokeTarget(s.At)) return;
                s.Decided = true;
            }
            var cur = EnsureBlockPaintContext(false);
            if (cur == null && _bpFence != null) return;
            _bpStart = null;
            LocText refused = cur == null ? new LocText("Placemaker.Problem.NoBlockHere")
                            : cur.Brush.Full || !cur.Brush.CanPaintAt(s.At) && cur.Open ? new LocText(cur.Open ? "Placemaker.Problem.CannotPaintHere" : "Placemaker.Problem.BlockFull") : null;
            if (refused != null)
            {
                // Nothing was painted: go back to the committed selection.
                if (_history.Current != null) RestoreSelectionOnly(_history.Current);
                Flash(refused);
                return;
            }
            cur.Brush.Attach(_bpMask);
            BeginBlockPaintGesture(s.At, radius, false, false);
            for (int i = 1; i < s.Path.Count; i++)
                if (_bpMask.Stroke(_lastStroke, s.Path[i], radius, false, _brushSquare, _brushAngle))
                {
                    _bpBrushed.Add((_lastStroke, s.Path[i], radius, false));
                    _lastStroke = s.Path[i];
                }
            // released already: the next frame ends the stroke as usual
        }

        /// <summary>
        /// Makes the fill (or, with One block off or no fill here, the road side) under p the painted one — a new
        /// selection if it is another one. False = not yet (the fill or the current fence is still being worked out).
        /// </summary>
        private bool DecideStrokeTarget(float2 p)
        {
            var cur = EnsureBlockPaintContext(false);
            // the current selection's fence is being built (after an undo): wait for it to know if p is in it
            if (cur == null && BlockPaintHasTarget && _bpFence != null && IsCurrentTarget(_bpFence.Ctx)) return false;
            if (Continues(cur, p))
            {
                // the city keeps growing: the fence is read again in the background, the stroke goes on with this one
                // (a big open box is not: that is a full rebuild, seconds of a worker)
                if (UnityEngine.Time.realtimeSinceStartup - cur.BuiltAt > BlockPaintRefresh && (!cur.Open || math.cmax(cur.BoxMax - cur.BoxMin) <= 1200f))
                    EnsureBlockPaintContext(true);
                return true;
            }
            FillResult fill = null;
            if (BrushRules.LockOn)
            {
                fill = Fills.Ready(p, BrushRules, _editingGroup);
                if (fill == null)
                {
                    if (!Fills.Busy) Fills.Request(p, BrushRules, _editingGroup);
                    return false;
                }
                LogFill("stroke", fill);
                _hoverFaceProblem = Problem(fill);
            }
            _bpMask.Clear();
            _bpBrushed.Clear();
            // a fill that ran into the reach is open land, not a block: no disc fence (the open box, below)
            if (fill != null && fill.Status == FillStatus.Ok && !fill.HitReach)
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
                _bpBoxMin = p - BlockBrush.OpenHalfSize;
                _bpBoxMax = p + BlockBrush.OpenHalfSize;
            }
            _bpSeed = p;
            _hoverFace = null;
            // its fence, built in the background (PumpStrokeStart waits for it)
            EnsureBlockPaintContext(true);
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
            // at most 10 a second, and never more than about a third of a worker (a big area's outline takes 0.2–0.5 s)
            if (now - _bpPreviewAt < math.max(0.1f, 3f * _bpPreviewMs / 1000f)) return;
            _bpPreviewAt = now;
            _bpPreviewJobVersion = _bpMask.Version;
            var snap = _bpMask.Save();
            var brush = ctx.Brush;
            float cell = _bpMask.CellSize;
            _bpPreviewJob = System.Threading.Tasks.Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var m = new BrushMask(cell);
                    m.Restore(snap);
                    lock (brush) return brush.Outline(m)?.Ring;
                }
                catch (Exception) { return null; }
                finally { _bpPreviewMs = (float)sw.Elapsed.TotalMilliseconds; }
            });
        }

        private volatile float _bpPreviewMs;

        // The outline at mouse-up (lag spike on release, 2026-10-21: 0.2–0.5 s on the game's thread for a big area) is worked
        // out on a worker; the selection changes when it is back (RebuildShapeIfDirty waits for it).
        private System.Threading.Tasks.Task<float2[]> _bpOutlineJob;
        private int _bpOutlineJobVersion = -1, _bpOutlineVersion = -1;
        private BlockBrush _bpOutlineJobBrush, _bpOutlineBrush;
        private float2[] _bpOutlineRing;
        private System.Diagnostics.Stopwatch _bpOutlineClock;

        /// <summary>False while the painted outline is still being worked out in the background (started here when needed).</summary>
        private bool BlockPaintOutlineReady()
        {
            if (_bpMask.IsEmpty || !BlockPaintHasTarget) return true;
            var ctx = BlockPaintCurrent;
            if (ctx?.Brush == null) return true;   // no fence yet (an undo): BuildBlockPaintShape makes both now
            if (_bpOutlineVersion == _bpMask.Version && ReferenceEquals(_bpOutlineBrush, ctx.Brush)) return true;
            if (_bpOutlineJob != null)
            {
                if (!_bpOutlineJob.IsCompleted) return false;
                var job = _bpOutlineJob;
                _bpOutlineJob = null;
                if (_bpOutlineJobVersion == _bpMask.Version && ReferenceEquals(_bpOutlineJobBrush, ctx.Brush))
                {
                    _bpOutlineRing = job.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? job.Result : null;
                    _bpOutlineVersion = _bpOutlineJobVersion;
                    _bpOutlineBrush = _bpOutlineJobBrush;
                    Mod.Log.Info("Placemaker: Paint area outline in " + _bpOutlineClock.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (in the background)");
                    return true;
                }
            }
            var snap = _bpMask.Save();
            var brush = ctx.Brush;
            float cell = _bpMask.CellSize;
            _bpOutlineJobVersion = _bpMask.Version;
            _bpOutlineJobBrush = brush;
            _bpOutlineClock = System.Diagnostics.Stopwatch.StartNew();
            _bpOutlineJob = System.Threading.Tasks.Task.Run(() =>
            {
                var m = new BrushMask(cell);
                m.Restore(snap);
                lock (brush) return brush.Outline(m)?.Ring;
            });
            return false;
        }

        /// <summary>
        /// Fence + stops for the current target; rebuilt when the target, the rules or the edited group changed (or when
        /// forced). Null when there is no target.
        /// </summary>
        // Lag spikes (2026-10-21, the user: "a lot of lag spikes ... when I'm dragging the paint area tool"): a fence took
        // 0.1–0.3 s on the game's thread (the open box's grid of every stop, more as the box grows), at the press, every
        // time the box grew during the drag and every 5 s. The walls are still read here; the fence itself is made on a
        // worker, and the one in use stays until the new one is back.
        private sealed class FenceJob
        {
            public BlockPaintContext Ctx;
            public System.Threading.Tasks.Task<(BlockBrush Brush, List<float2[]> Shown)> Task;
            public System.Diagnostics.Stopwatch Clock;
            /// <summary>Time (ms) the walls and water took to read on the game's thread.</summary>
            public double ReadMs;
        }

        private FenceJob _bpFence;
        /// <summary>The last fence that could not be made (not asked again until the target or the rules change).</summary>
        private BlockPaintContext _bpFenceFailed;

        private bool UpToDate(BlockPaintContext c, BrushRules rules) =>
            rules.Equals(c.Rules) && c.IgnoreGroup == _editingGroup && (!c.Open || math.all(c.BoxMin == _bpBoxMin) && math.all(c.BoxMax == _bpBoxMax));

        /// <summary>
        /// Fence + stops for the current target, or null when there is none yet. When the target, the rules, the edited
        /// group or the open box changed (or <paramref name="refresh"/>) a new one is built in the background and the one in
        /// use (same target) is returned meanwhile; <paramref name="now"/> builds it here instead.
        /// </summary>
        private BlockPaintContext EnsureBlockPaintContext(bool refresh, bool now = false)
        {
            if (!BlockPaintHasTarget) return null;
            var rules = BrushRules;
            var c = _bpCtx;
            bool current = c != null && IsCurrentTarget(c);
            if (!refresh && current && UpToDate(c, rules))
            {
                if (_bpMask.Limit != c.Brush.Allowed) c.Brush.Attach(_bpMask);
                return c;
            }
            var job = _bpFence;
            bool asked = job != null && IsCurrentTarget(job.Ctx) && UpToDate(job.Ctx, rules);
            if (now)
            {
                if (asked) { try { job.Task.Wait(); } catch (Exception) { } PumpFence(); }
                else
                {
                    var built = StartFence(rules, refresh, false);
                    if (built != null) Land(built);
                }
                c = _bpCtx;
                return c != null && IsCurrentTarget(c) ? c : null;
            }
            bool failed = !refresh && _bpFenceFailed != null && IsCurrentTarget(_bpFenceFailed) && UpToDate(_bpFenceFailed, rules);
            if (!asked && !failed) _bpFence = StartFence(rules, refresh, true);
            return current ? c : null;
        }

        /// <summary>Reads the walls (here) and makes the fence (on a worker when <paramref name="background"/>, else now).</summary>
        private FenceJob StartFence(BrushRules rules, bool refresh, bool background)
        {
            if (refresh) _areaWalls?.Invalidate();
            var job = new FenceJob { Clock = System.Diagnostics.Stopwatch.StartNew() };
            Func<(BlockBrush, List<float2[]>)> make;
            try { make = _bpOpen ? OpenFence(rules, refresh, out job.Ctx) : FillFence(rules, out job.Ctx); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: Paint area fence failed: " + e.Message); make = null; }
            if (make == null) return null;
            job.ReadMs = job.Clock.Elapsed.TotalMilliseconds;
            job.Ctx.Rules = rules;
            job.Ctx.IgnoreGroup = _editingGroup;
            _bpFenceFailed = null;
            job.Task = System.Threading.Tasks.Task.Run(make);
            if (!background) { try { job.Task.Wait(); } catch (Exception) { } }
            return job;
        }

        /// <summary>Takes a finished fence when it is still for the current target.</summary>
        private void PumpFence()
        {
            var job = _bpFence;
            if (job == null || !job.Task.IsCompleted) return;
            _bpFence = null;
            if (!BlockPaintHasTarget || !IsCurrentTarget(job.Ctx)) return;
            Land(job);
        }

        private void Land(FenceJob job)
        {
            var c = job.Ctx;
            if (job.Task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || job.Task.Result.Brush == null)
            {
                Mod.Log.Warn("Placemaker: Paint area fence failed: " + job.Task.Exception?.GetBaseException().Message);
                _bpFenceFailed = c;
                return;
            }
            c.Brush = job.Task.Result.Brush;
            foreach (var r in job.Task.Result.Shown) c.Obstacles.Add(Poly.To3D(r, q => _site.SampleHeight(q)));
            Mod.Log.Info("Placemaker: Paint area fence built in " + job.Clock.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (" + (c.Open ? "open box" : "one block") + ", in the background; walls and water read in " + job.ReadMs.ToString("0.0") + " ms), "
                + c.Obstacles.Count + " shaded, rules " + RulesLine(c.Rules) + "; walls " + (_areaWalls?.LastStats ?? ""));
            c.BuiltAt = UnityEngine.Time.realtimeSinceStartup;
            bool had = _bpCtx != null && IsCurrentTarget(_bpCtx);
            _bpCtx = c;
            c.Brush.Attach(_bpMask);
            // a selection restored (undo) before its fence was back: its outline now
            if (!had && !_bpMask.IsEmpty && !Painting && _bpStart == null) ShapeChanged();
        }

        private static bool IsNetKind(BrushObstacleKind k) => k == BrushObstacleKind.Road || k == BrushObstacleKind.Path || k == BrushObstacleKind.Bike || k == BrushObstacleKind.Track;

        /// <summary>One block: paint only inside the fill (it already keeps off every stop, closes gaps, skips cracks).</summary>
        private Func<(BlockBrush, List<float2[]>)> FillFence(BrushRules rules, out BlockPaintContext c)
        {
            c = null;
            var ring = _bpRing;
            if (ring == null || ring.Length < 3) return null;
            c = new BlockPaintContext { Ring = ring, Block = Poly.To3D(ring, q => _site.SampleHeight(q)), Seed = _bpSeed };
            if (_bpDoors != null) foreach (var d in _bpDoors) if (d != null && d.Length >= 2) c.Doors.Add(Poly.To3D(d, q => _site.SampleHeight(q)));
            // Shown as "won't paint here": the stops along the fill's edge.
            Poly.Bounds(ring, out var mn, out var mx);
            var walls = new List<BrushObstacle>();
            Walls.Counted(mn - 2f, mx + 2f, _editingGroup, rules, walls);
            float cell = _bpMask.CellSize;
            return () =>
            {
                var brush = new BlockBrush(ring, null, cell) { SnapDistance = rules.Snap, Smoothing = rules.Smoothing };
                var shown = new List<float2[]>();
                foreach (var w in walls)
                    if (!IsNetKind(w.Kind) && ShowsInBlock(w.Ring, ring)) shown.Add(w.Ring);
                return (brush, shown);
            };
        }

        /// <summary>
        /// The open work box reaches the brush: grown by OpenHalfSize past the brush on the side it neared (up to
        /// MaxOpenSize a side), the fence rebuilt over it from the cached walls (in the background: the brush paints in
        /// the old box until it is back); the painted cells stay.
        /// </summary>
        private void GrowOpenBox(float2 at, float radius)
        {
            // one growth at a time: the next one grows from it (a new one now would rebuild the whole box)
            if (_bpFence != null) return;
            float m = radius + OpenGrowMargin;
            if (at.x - m > _bpBoxMin.x && at.y - m > _bpBoxMin.y && at.x + m < _bpBoxMax.x && at.y + m < _bpBoxMax.y) return;
            float2 min = math.min(_bpBoxMin, at - BlockBrush.OpenHalfSize), max = math.max(_bpBoxMax, at + BlockBrush.OpenHalfSize);
            if (math.cmax(max - min) > MaxOpenSize) return;
            _bpBoxMin = min;
            _bpBoxMax = max;
            EnsureBlockPaintContext(false);
            Mod.Log.Info($"Placemaker: Paint area box grown to {max.x - min.x:0} x {max.y - min.y:0} m");
        }

        /// <summary>Open land / One block off: a work box around the start with every stop fenced out.</summary>
        private Func<(BlockBrush, List<float2[]>)> OpenFence(BrushRules rules, bool refresh, out BlockPaintContext c)
        {
            // the box laid out at the stroke's start, as far as it has grown since (old selections: around the start)
            float2 min = _bpBoxMin, max = _bpBoxMax;
            if (!(max.x > min.x) || !(max.y > min.y) || math.any(_bpSeed < min) || math.any(_bpSeed > max))
            {
                min = _bpSeed - BlockBrush.OpenHalfSize;
                max = _bpSeed + BlockBrush.OpenHalfSize;
                _bpBoxMin = min;
                _bpBoxMax = max;
            }
            var walls = new List<BrushObstacle>();
            if (!rules.Anything) Walls.Counted(min, max, _editingGroup, rules, walls);
            var seed = _bpSeed;
            float cell = _bpMask.CellSize;
            c = new BlockPaintContext { Ring = null, Open = true, Seed = seed, Block = null, BoxMin = min, BoxMax = max };
            // The box grew (same start, rules and group): only its new strips are fenced, from the fence in use.
            var old = _bpCtx;
            if (!refresh && old?.Brush != null && old.Open && old.Brush.IsOpen && !old.Brush.Unconstrained && IsCurrentTarget(old) && rules.Equals(old.Rules) && old.IgnoreGroup == _editingGroup
                && math.all(min <= old.BoxMin) && math.all(max >= old.BoxMax))
            {
                var from = old.Brush;
                float2 omin = old.BoxMin, omax = old.BoxMax;
                // water read only in the new strips (4 m: a 5 km box stays within what the water reader samples)
                var wet = new List<Func<float2, bool>>();
                if (rules.WaterOn)
                    foreach (var (a, b) in new[] { (min, new float2(omin.x, max.y)), (new float2(omax.x, min.y), max), (new float2(omin.x, min.y), new float2(omax.x, omin.y)), (new float2(omin.x, omax.y), new float2(omax.x, max.y)) })
                        if (b.x > a.x && b.y > a.y) { var w = Walls.Water(a, b, math.max(cell, 4f), rules.ForOutline()); if (w != null) wet.Add(w); }
                Func<float2, bool> stripWater = wet.Count == 0 ? null : p => { foreach (var w in wet) if (w(p)) return true; return false; };
                return () =>
                {
                    var rings = new List<IReadOnlyList<float2>>();
                    var r = rules.Normalized();
                    foreach (var w in walls) if (w.Ring != null && r.Stops(w.Kind)) rings.Add(w.Ring);
                    var brush = from.Grow(min, max, rings, stripWater);
                    var shown = new List<float2[]>();
                    foreach (var w in walls) if (!IsNetKind(w.Kind)) shown.Add(w.Ring);
                    return (brush, shown);
                };
            }
            var water = Walls.Water(min, max, cell, rules.ForOutline());
            return () =>
            {
                var brush = rules.Build(seed, null, walls, rules.WaterOn ? water : null, cell, min, max);
                var shown = new List<float2[]>();
                foreach (var w in walls) if (!IsNetKind(w.Kind)) shown.Add(w.Ring);
                return (brush, shown);
            };
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

        /// <summary>
        /// Where the brush went in this painted area (segments with the brush radius; erasing ones too), whatever the stops kept
        /// out: the painter picks the houses painted over even where the stops (lots, buildings) left them out of the area.
        /// Gone with a new area or an undo.
        /// </summary>
        private readonly List<(float2 A, float2 B, float R, bool Erase)> _bpBrushed = new List<(float2, float2, float, bool)>();

        /// <summary>The brush passed over p (and no later erasing stroke took it back).</summary>
        public bool BrushedOver(float2 p)
        {
            bool on = false;
            foreach (var (a, b, r, erase) in _bpBrushed)
            {
                var e = b - a;
                float t = math.lengthsq(e) < 1e-6f ? 0f : math.saturate(math.dot(p - a, e) / math.dot(e, e));
                if (math.distance(p, a + e * t) <= r) on = !erase;
            }
            return on;
        }

        private void RestoreBlockPaint(BlockPaintState s)
        {
            _bpBrushed.Clear();
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
            // the fence in use (a newer one may still be on its way); none for this selection yet (an undo): made now
            var ctx = BlockPaintCurrent ?? EnsureBlockPaintContext(false, now: true);
            if (ctx == null) return null;
            float2[] ring;
            if (_bpOutlineVersion == _bpMask.Version && ReferenceEquals(_bpOutlineBrush, ctx.Brush)) ring = _bpOutlineRing;
            else
            {
                // not worked out in the background (an undo, a build asking for the outline now)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                lock (ctx.Brush) ring = ctx.Brush.Outline(_bpMask)?.Ring;
                if (sw.Elapsed.TotalMilliseconds > 30) Mod.Log.Info("Placemaker: Paint area outline in " + sw.Elapsed.TotalMilliseconds.ToString("0.0") + " ms");
            }
            if (ring == null) return null;
            var shape = new AreaShape { Ring = Poly.To3D(ring, p => _site.SampleHeight(p)), Source = AreaSource.Painted };
            WithFoundAttachments(shape);
            return shape;
        }
    }
}
