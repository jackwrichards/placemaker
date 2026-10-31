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
        /// <summary>(P2-1) Index of the step's marker being dragged along the outline, -1 = none.</summary>
        private int _markerDrag = -1;
        /// <summary>(P2-1) The hover point snapped to one of the plan's drive aisles (drawn as a guide line to it).</summary>
        private bool _markerSnapped;
        private float3 _markerSnapTo;

        public bool MarkerSnapped => _markerSnapped;
        public float3 MarkerSnapTo => _markerSnapTo;
        /// <summary>(P2-1) A placed marker under the cursor (it can be dragged, right-click removes it), -1 = none.</summary>
        public int MarkerHot { get; private set; } = -1;

        public bool MarkerHoverValid => _markerHoverValid;
        public float3 MarkerHover => _markerHover;

        /// <summary>UI or primary button: start placing markers of <paramref name="kind"/> for the step at <paramref name="path"/>.</summary>
        public void StartMarkers(string path, string kind)
        {
            if (_shape == null || string.IsNullOrEmpty(kind) || RecipePaths.Resolve(_recipe, path) == null) return;
            CancelGesture();
            ExitTouchMode();
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
            _markerDrag = -1;
            MarkerHot = -1;
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

        /// <summary>PLT EntranceRoadSnapDistance: an entrance snaps to a drive line meeting the edge within this many metres along it.</summary>
        private const float MarkerSnapAlong = 5f;

        /// <summary>
        /// (P2-1, PLT ParkingLotEntrances.TryBuildEntranceCandidate) Moves the hover point along its outline edge onto the place where
        /// one of the step's planned drive aisles meets the edge, when that is within 5 m along it and the aisle meets the edge at 30°
        /// or more, so the entrance's connector runs straight down the aisle. The nearest aisle in space wins. Shift = freehand.
        /// </summary>
        private void SnapMarkerToAisles()
        {
            _markerSnapped = false;
            if (_in.Shift || _plan == null || !_markerHoverValid) return;
            var ring = _shape.Ring;
            float2 a = ring[_markerHoverSegment].xz, b = ring[(_markerHoverSegment + 1) % ring.Length].xz;
            float len = math.distance(a, b);
            if (len < 1f) return;
            float2 dir = (b - a) / len;
            float2 hover = _markerHover.xz;
            bool foot = _markerKind == "footpath";
            float bestD = float.MaxValue; float bestT = 0; float3 bestTo = default;
            foreach (var layer in _plan.Layers)
            {
                if (layer == null || layer.StepPath != _markerPath) continue;
                foreach (var n in layer.Nets)
                {
                    if (foot ? n.Role != NetRole.Pathway : n.Role != NetRole.DriveAisle) continue;
                    float2 p = n.A.xz, q = n.D.xz;
                    float2 nd = q - p;
                    float nl = math.length(nd);
                    if (nl < 0.5f) continue;
                    nd /= nl;
                    // meets the edge at 30 degrees or more (PLT EntranceRoadMinProjection 0.5)
                    float cross = dir.x * nd.y - dir.y * nd.x;
                    if (math.abs(cross) < 0.5f) continue;
                    // where the line of the aisle crosses the line of the edge
                    float s = ((p.x - a.x) * nd.y - (p.y - a.y) * nd.x) / cross;
                    if (s < 0.5f || s > len - 0.5f) continue;
                    float2 x = a + dir * s;
                    if (math.abs(s - math.dot(hover - a, dir)) > MarkerSnapAlong) continue;
                    // the aisle must end near the edge (not a line across the whole lot): its nearer end within 25 m
                    float endD = math.min(math.distance(p, x), math.distance(q, x));
                    if (endD > 25f) continue;
                    float d = DistToSeg(hover, p, q);
                    if (d < bestD) { bestD = d; bestT = s / len; bestTo = math.distance(p, x) < math.distance(q, x) ? n.A : n.D; }
                }
            }
            if (bestD == float.MaxValue) return;
            _markerSnapped = true;
            _markerSnapTo = bestTo;
            _markerHoverT = bestT;
            _markerHover = math.lerp(ring[_markerHoverSegment], ring[(_markerHoverSegment + 1) % ring.Length], bestT);
        }

        private static float DistToSeg(float2 p, float2 a, float2 b)
        {
            var ab = b - a; float l = math.lengthsq(ab);
            float t = l > 1e-9f ? math.saturate(math.dot(p - a, ab) / l) : 0f;
            return math.distance(p, a + ab * t);
        }

        private void UpdateMarkers()
        {
            _markerHoverValid = false;
            _markerSnapped = false;
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
                SnapMarkerToAisles();
            }
            var step = RecipePaths.Resolve(_recipe, _markerPath);
            if (step == null) return;
            // (P2-1) a placed marker of this step under the cursor: drag it along the outline, right-click removes it
            if (_markerDrag < 0)
            {
                MarkerHot = -1;
                float hot = HandleRadius(_in.Hit) * 1.2f;
                for (int i = 0; i < step.Markers.Count; i++)
                {
                    float dm = math.distance(step.Markers[i].Position.xz, _in.Hit.xz);
                    if (dm < hot) { hot = dm; MarkerHot = i; }
                }
            }
            if (_markerDrag >= 0)
            {
                if (_markerDrag >= step.Markers.Count) { _markerDrag = -1; return; }
                if (_in.LeftHeld && _markerHoverValid)
                {
                    var m = step.Markers[_markerDrag];
                    if (m.RingSegment != _markerHoverSegment || math.abs(m.RingT - _markerHoverT) > 1e-4f)
                    {
                        m.RingSegment = _markerHoverSegment; m.RingT = _markerHoverT; m.Position = _markerHover;
                        step.Markers[_markerDrag] = m;
                        DesignChanged();
                    }
                }
                if (!_in.LeftHeld || _in.LeftReleased)
                {
                    _markerDrag = -1;
                    MarkMarkersEdited(_markerPath);
                    Commit("Placemaker.Undo.MoveMarker");
                }
                return;
            }
            if (_in.LeftPressed && MarkerHot >= 0)
            {
                _markerDrag = MarkerHot;
                return;
            }
            if (_in.RightPressed && MarkerHot >= 0)
            {
                step.Markers.RemoveAt(MarkerHot);
                MarkerHot = -1;
                MarkMarkersEdited(_markerPath);
                DesignChanged();
                Commit("Placemaker.Undo.RemoveMarker");
                return;
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
