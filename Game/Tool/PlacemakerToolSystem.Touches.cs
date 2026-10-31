// (P2-1) Hand edits on the plan (Core/Touches.cs): the panel picks a kind ("Accessible", "Island", "Rows line up here"…),
// then a click on the world applies it to the thing under the cursor that the layer offered (AreaPlan.TouchTargets: the
// stalls, the outline's edges); clicking it again with the same kind takes the edit off; right-click takes back the last
// one; Esc or the panel's button when done. The edits live in the recipe step like markers: undoable, saved with the
// build, there again on Edit.

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
            _touchPath = path;
            _touchKind = kind;
            _focus = path;
            if (_stage != ToolStage.Design && _stage != ToolStage.Review) SetStage(ToolStage.Design);
            Touch();
        }

        public void ExitTouchMode()
        {
            if (_touchKind == null) return;
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

        private void UpdateTouches()
        {
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
