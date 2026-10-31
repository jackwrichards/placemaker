// (P2-1) Hand edits on the plan (Core/Touches.cs): the panel picks a kind ("Accessible", "Island", "Rows line up here"…),
// then a click on the world applies it to the thing under the cursor that the layer offered (AreaPlan.TouchTargets: the
// stalls, the outline's edges); clicking it again with the same kind takes the edit off; right-click takes back the last
// one; Esc or the panel's button when done. The edits live in the recipe step like markers: undoable, saved with the
// build, there again on Edit.
// Line kinds ("line.aisle": Core/Tool/LineSketch, docs/AREA-TOOLS.md "Sketch lines") are drawn instead: each click adds a
// snapped point (Shift: as clicked), right-click or a click on the last point finishes, Esc drops the line being drawn;
// a click on a finished line of the same kind takes it off.

using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private string _touchPath, _touchKind;
        private TouchTarget _touchHover;
        private readonly LineSketch _sketch = new LineSketch();
        private float3 _sketchHover;
        private SketchSnap _sketchHow;
        private bool _sketchHoverValid;

        /// <summary>The line being drawn (its points), for the overlay.</summary>
        public IReadOnlyList<float3> SketchPoints => _sketch.Points;
        /// <summary>Where the next point of a sketch line goes (valid while drawing a line kind).</summary>
        public bool SketchHover(out float3 at, out SketchSnap how) { at = _sketchHover; how = _sketchHow; return _sketchHoverValid; }
        public static bool IsLineKind(string kind) => Touches.GroupOf(kind) == "line";

        /// <summary>The hand edits of a step (for the overlay); empty when there is none.</summary>
        public IReadOnlyList<HandTouch> TouchesOf(string path)
        {
            var step = RecipePaths.Resolve(_recipe, path);
            return step != null ? (IReadOnlyList<HandTouch>)step.Touches : System.Array.Empty<HandTouch>();
        }

        public string TouchPath => _touchPath;
        public string TouchKind => _touchKind;
        /// <summary>The target under the cursor while a hand-edit kind is picked (null = none).</summary>
        public TouchTarget TouchHover => _touchHover;

        /// <summary>UI: pick the hand edit the next click makes for the step at <paramref name="path"/>; "" = done.</summary>
        public void SetTouchKind(string path, string kind)
        {
            if (string.IsNullOrEmpty(kind) || _shape == null || RecipePaths.Resolve(_recipe, path) == null) { ExitTouchMode(); return; }
            CancelGesture();
            ExitMarkerMode();
            FinishSketchLine();
            _touchPath = path;
            _touchKind = kind;
            _focus = path;
            if (_stage != ToolStage.Design && _stage != ToolStage.Review) SetStage(ToolStage.Design);
            Touch();
        }

        public void ExitTouchMode()
        {
            if (_touchKind == null) return;
            FinishSketchLine();   // leaving with a line half drawn keeps it when it has two points
            _sketchHoverValid = false;
            _touchKind = null;
            _touchPath = null;
            _touchHover = null;
            Touch();
        }

        /// <summary>UI: take off every hand edit of the step (kind "" = all kinds).</summary>
        public void ClearTouches(string path, string kind)
        {
            EndValuePreview(true);
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null) return;
            int n = step.Touches.RemoveAll(t => string.IsNullOrEmpty(kind) || t.Kind == kind);
            if (n == 0) return;
            DesignChanged();
            Commit("Placemaker.Undo.ClearTouches");
        }

        /// <summary>The targets the latest plan of the step offers for <paramref name="kind"/>'s group.</summary>
        private IEnumerable<TouchTarget> TouchTargetsFor(string path, string kind)
        {
            var plan = _plan;
            if (plan == null) yield break;
            string group = Touches.GroupOf(kind);
            foreach (var layer in plan.Layers)
            {
                if (layer == null || Touches.StepOf(layer.StepPath) != path || layer.TouchTargets == null) continue;
                foreach (var t in layer.TouchTargets) if (t != null && t.Group == group) yield return t;
            }
        }

        private static bool Inside(float2[] poly, float2 p)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Esc while drawing: the line being drawn goes, the mode stays. False when nothing was being drawn.</summary>
        private bool CancelSketchLine()
        {
            if (!_sketch.Drawing) return false;
            _sketch.Clear();
            Touch();
            return true;
        }

        private void FinishSketchLine()
        {
            if (_touchKind == null || !IsLineKind(_touchKind)) { _sketch.Clear(); return; }
            var t = _sketch.Finish(_touchKind);
            _sketch.Clear();
            if (!t.HasValue) return;
            var step = RecipePaths.Resolve(_recipe, _touchPath);
            if (step == null) return;
            step.Touches.Add(t.Value);
            DesignChanged();
            Commit("Placemaker.Undo.HandEdit");
        }

        /// <summary>What a sketch point snaps to: the outline, the step's planned paths and roads, its finished lines.</summary>
        private SketchSnaps SketchSnapsFor(float reach)
        {
            var snaps = new SketchSnaps { Reach = reach };
            snaps.AddRing(_shape?.Ring2D());
            var plan = _plan;
            if (plan != null)
                foreach (var layer in plan.Layers)
                {
                    if (layer == null || Touches.StepOf(layer.StepPath) != _touchPath) continue;
                    foreach (var n in layer.Nets) snaps.AddSegment(n.A.xz, n.D.xz);
                }
            var step = RecipePaths.Resolve(_recipe, _touchPath);
            if (step != null)
                foreach (var t in step.Touches)
                {
                    if (!IsLineKind(t.Kind)) continue;
                    var pts = LineSketch.Decode(t.Arg);
                    for (int i = 0; i + 1 < pts.Length; i++) snaps.AddSegment(pts[i], pts[i + 1]);
                }
            return snaps;
        }

        private void UpdateSketch()
        {
            _sketchHoverValid = false;
            _cursorValid = _in.HasHit;
            if (!_in.HasHit || _shape == null) return;
            _cursor = _in.Hit;
            float reach = math.clamp(HandleRadius(_in.Hit) * 1.5f, 1.5f, 12f);
            var p = _sketch.Snap(_in.Hit.xz, SketchSnapsFor(reach), _in.Shift, out _sketchHow);
            _sketchHover = new float3(p.x, _in.Hit.y, p.y);
            _sketchHoverValid = true;
            var step = RecipePaths.Resolve(_recipe, _touchPath);
            if (step == null) return;
            if (_in.LeftPressed)
            {
                if (!_sketch.Drawing)
                {
                    // a click on a finished line of this kind takes it off (as clicking a touched stall again)
                    int hit = LineSketch.Hit(step.Touches, _in.Hit.xz, math.max(1f, reach * 0.6f), k => k == _touchKind);
                    if (hit >= 0)
                    {
                        step.Touches.RemoveAt(hit);
                        DesignChanged();
                        Commit("Placemaker.Undo.HandEdit");
                        return;
                    }
                }
                if (!_sketch.Add(_sketchHover) && _sketch.CanFinish) FinishSketchLine();   // the last point again: done
                Touch();
                return;
            }
            if (_in.RightPressed)
            {
                if (_sketch.Drawing)
                {
                    if (_sketch.CanFinish) FinishSketchLine(); else _sketch.Clear();
                    Touch();
                    return;
                }
                if (step.Touches.Count == 0) return;
                step.Touches.RemoveAt(step.Touches.Count - 1);
                DesignChanged();
                Commit("Placemaker.Undo.HandEdit");
            }
        }

        private void UpdateTouches()
        {
            if (IsLineKind(_touchKind)) { UpdateSketch(); return; }
            _touchHover = null;
            _cursorValid = _in.HasHit;
            if (!_in.HasHit || _shape == null) return;
            _cursor = _in.Hit;
            var p = _in.Hit.xz;
            // the target under the cursor; else the nearest centre within reach of the cursor's handle size
            float best = math.max(2f, HandleRadius(_in.Hit));
            foreach (var t in TouchTargetsFor(_touchPath, _touchKind))
            {
                if (Inside(t.Outline, p)) { _touchHover = t; break; }
                float d = math.distance(t.Center.xz, p);
                if (d < best) { best = d; _touchHover = t; }
            }
            if (_in.LeftPressed && _touchHover != null)
            {
                var step = RecipePaths.Resolve(_recipe, _touchPath);
                if (step == null) return;
                bool single = Touches.GroupOf(_touchKind) == "grid";   // one picked edge per design
                if (Touches.Toggle(step.Touches, new HandTouch(_touchKind, _touchHover.Center), _touchHover, single))
                {
                    DesignChanged();
                    Commit("Placemaker.Undo.HandEdit");
                }
                return;
            }
            if (_in.RightPressed)
            {
                var step = RecipePaths.Resolve(_recipe, _touchPath);
                if (step == null || step.Touches.Count == 0) return;
                step.Touches.RemoveAt(step.Touches.Count - 1);
                DesignChanged();
                Commit("Placemaker.Undo.HandEdit");
            }
        }
    }
}
