// Marker placement (entrances, access points, split lines…): click on the outline to place a
// marker of the requested kind for one recipe step, right-click takes back the last one placed, Esc or
// the main button when done. Markers live in the recipe step (so they are undoable with the
// design) and are re-projected onto each region's outline by the composer.

using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private bool _markerHoverValid;
        private float3 _markerHover;
        private int _markerHoverSegment;
        private float _markerHoverT;

        public bool MarkerHoverValid => _markerHoverValid;
        public float3 MarkerHover => _markerHover;

        /// <summary>UI or primary button: start placing markers of <paramref name="kind"/> for the step at <paramref name="path"/>.</summary>
        public void StartMarkers(string path, string kind)
        {
            if (_shape == null || string.IsNullOrEmpty(kind) || RecipePaths.Resolve(_recipe, path) == null) return;
            CancelGesture();
            _markerPath = path;
            _markerKind = kind;
            _focus = path;
            if (_stage != ToolStage.Design && _stage != ToolStage.Review) SetStage(ToolStage.Design);
            Touch();
        }

        /// <summary>UI: which marker type the next click places (entrance, entry, exit…). Enters placement for that step.</summary>
        public void SetMarkerKind(string path, string kind)
        {
            if (string.IsNullOrEmpty(kind)) { ExitMarkerMode(); return; }
            if (_markerKind != null && _markerPath == path) { _markerKind = kind; Touch(); return; }
            StartMarkers(path, kind);
        }

        /// <summary>The entrances placed next come with a crosswalk (walking kinds only; Core/Layers/Common/StreetJoins). Off by default.</summary>
        private bool _markerCrosswalk;
        public bool MarkerCrosswalk => _markerCrosswalk;

        /// <summary>
        /// UI: crosswalk or not. <paramref name="index"/> &lt; 0: for the entrances placed next; else for the placed marker with that
        /// index in StepView.markerList (kept in its kind as "+crosswalk", MarkerVariants).
        /// </summary>
        public void SetMarkerCrosswalk(string path, int index, bool on)
        {
            if (index < 0) { _markerCrosswalk = on; Touch(); return; }
            EndValuePreview(true);
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null || index >= step.Markers.Count) return;
            var m = step.Markers[index];
            if (!OffersCrosswalk(path, MarkerVariants.BaseKind(m.Kind)) || MarkerVariants.HasCrosswalk(m.Kind) == on) return;
            m.Kind = MarkerVariants.WithCrosswalk(m.Kind, on);
            step.Markers[index] = m;
            MarkMarkersEdited(path);
            DesignChanged();
            Commit("Placemaker.Undo.Crosswalk");
        }

        /// <summary>The step's generator lets people walk in through <paramref name="kind"/>: it can have a crosswalk.</summary>
        private bool OffersCrosswalk(string path, string kind)
        {
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null) return false;
            foreach (var k in MarkerKinds.For(GeneratorRegistry.Get(step.Layer)))
                if (k.Kind == kind) return k.Crosswalk;
            return false;
        }

        /// <summary>The kind a new marker gets: with "+crosswalk" while the crosswalk choice is on and people walk in there.</summary>
        private string PlacedKind(string path, string kind) =>
            _markerCrosswalk && OffersCrosswalk(path, kind) ? MarkerVariants.WithCrosswalk(kind, true) : kind;

        /// <summary>UI: remove one marker by its index in StepView.markerList.</summary>
        public void RemoveMarker(string path, int index)
        {
            EndValuePreview(true);
            var step = RecipePaths.Resolve(_recipe, path);
            if (step == null || index < 0 || index >= step.Markers.Count) return;
            step.Markers.RemoveAt(index);
            MarkMarkersEdited(path);
            DesignChanged();
            Commit("Placemaker.Undo.RemoveMarker");
        }

        public void ExitMarkerMode()
        {
            if (_markerKind == null) return;
            _markerKind = null;
            _markerPath = null;
            _markerHoverValid = false;
            Touch();
        }

        public void ClearMarkers(string path, string kind)
        {
            if (_recipe == null) return;
            if (_editor.ClearMarkers(_recipe, path, kind))
            {
                MarkMarkersEdited(path);
                DesignChanged();
                Commit("Placemaker.Undo.ClearMarkers");
            }
        }

        private void UpdateMarkers()
        {
            _markerHoverValid = false;
            _cursorValid = _in.HasHit;
            if (!_in.HasHit || _shape == null) return;
            _cursor = _in.Hit;
            float radius = HandleRadius(_in.Hit) * 2f;
            if (Composer.ClosestSegment(_shape.Ring, _in.Hit.xz, out int seg, out float t, out float d) && d <= radius)
            {
                _markerHoverValid = true;
                _markerHoverSegment = seg;
                _markerHoverT = t;
                _markerHover = math.lerp(_shape.Ring[seg], _shape.Ring[(seg + 1) % _shape.Ring.Length], t);
            }
            if (_in.LeftPressed && _markerHoverValid)
            {
                var m = new EdgeMarker { Kind = PlacedKind(_markerPath, _markerKind), RingSegment = _markerHoverSegment, RingT = _markerHoverT, Position = _markerHover };
                if (_editor.AddMarker(_recipe, _markerPath, m))
                {
                    MarkMarkersEdited(_markerPath);
                    DesignChanged();
                    Commit("Placemaker.Undo.AddMarker");
                }
                return;
            }
            if (_in.RightPressed)
            {
                // Right-click takes back the last marker of this step, whatever its kind and wherever the cursor is (one rule:
                // right-click takes back the last thing placed); the panel's list removes any other one.
                if (_editor.RemoveLastMarker(_recipe, _markerPath))
                {
                    MarkMarkersEdited(_markerPath);
                    DesignChanged();
                    Commit("Placemaker.Undo.RemoveMarker");
                }
            }
        }
    }
}
