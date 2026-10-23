// Draw mode: PLT-quality polygon editor (KruemelGames' ParkingLotTool, GPL-3.0, behaviour ported;
// geometry rules in Core/Tool/PolygonEditor).
//
// Area overhaul: corners snap to road nodes (junctions, dead ends) and two in a row follow the road between them
// (PlacemakerToolSystem.Points); the outline is cut by the shared stops.
//
// Mouse map (shown in the panel hints and cursor tooltips):
//   while drawing:  click = add corner · click first corner / double-click = close
//                   right-click = remove last corner (or the corner under the cursor)
//   closed outline: drag corner = move · drag edge = insert a corner and move it
//                   Ctrl+click edge = insert corner · Shift+drag edge = move the whole edge
//                   right-click corner = remove it · Alt = no snapping

using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private const float DoubleClickSeconds = 0.35f;
        private const float DragThreshold = 0.6f;

        private int _hoverPoint = -1, _hoverEdge = -1;
        private bool _hoverClose;
        private float3 _cursor;
        private bool _cursorValid;
        private int _dragIndex = -1;
        private float3 _dragStartWorld;
        private float3 _pressWorld;
        private bool _dragMoved;
        private int _pendingEdge = -1;
        private float _lastAddTime = -10f;
        private float3 _lastAddPos;
        private float2 _edgeStartCursor;
        private float3 _edgeGuidePoint;

        public int HoverPoint => _hoverPoint;
        public int HoverEdge => _hoverEdge;
        public bool HoverClose => _hoverClose;
        public float3 Cursor => _cursor;
        /// <summary>The ground height at p (the overlay draws what is painted on the ground, not at the cursor's height).</summary>
        public float GroundAt(float2 p) => _site != null ? _site.SampleHeight(p) : _cursor.y;
        public bool CursorValid => _cursorValid;
        public PolygonEditor Draw => _draw;
        public bool EdgeDragging => _gesture == Gesture.DragEdge;
        public float3 EdgeGuidePoint => _edgeGuidePoint;

        private void UpdateDraw()
        {
            _cursorValid = _in.HasHit;
            if (!_in.HasHit && _gesture == Gesture.None) { _hoverPoint = _hoverEdge = -1; _hoverClose = false; return; }

            switch (_gesture)
            {
                case Gesture.DragPoint:
                case Gesture.InsertDrag:
                    ContinuePointDrag();
                    return;
                case Gesture.DragEdge:
                    ContinueEdgeDrag();
                    return;
            }

            float radius = HandleRadius(_in.Hit);
            _hoverPoint = _draw.HitPoint(_in.Hit.xz, radius);
            _hoverEdge = _hoverPoint < 0 ? _draw.HitEdge(_in.Hit.xz, radius * 0.8f, out _, out _) : -1;
            _hoverClose = _draw.ClosesAt(_in.Hit.xz, _in.Hit.xz, radius * 1.4f);

            if (!_draw.Closed) UpdateDrawOpen(radius);
            else UpdateDrawClosed(radius);
        }

        private void UpdateDrawOpen(float radius)
        {
            // Near the first corner the click closes; snapping there would only pull the cursor away.
            _cursor = _hoverClose ? _draw.Points[0] : Snap(_in.Hit, -1);
            // a click the snap puts on the first corner closes too (it could only add a corner on top of it)
            if (!_hoverClose && _draw.ClosesAt(_in.Hit.xz, _cursor.xz, radius * 1.4f)) { _hoverClose = true; _cursor = _draw.Points[0]; }

            if (_in.LeftPressed)
            {
                if (_hoverClose) { CloseOutline(); return; }
                if (_hoverPoint >= 0 && _hoverPoint != _draw.Count - 1)
                {
                    BeginPointDrag(_hoverPoint, false);
                    return;
                }
                bool doubleClick = _in.Time - _lastAddTime <= DoubleClickSeconds && math.distance(_lastAddPos.xz, _in.Hit.xz) <= radius;
                if (doubleClick && _draw.Count >= PolygonEditor.MinPoints) { CloseOutline(); return; }
                if (_hoverPoint == _draw.Count - 1 && _draw.Count > 0) { BeginPointDrag(_hoverPoint, false); return; }
                var problem = _draw.CheckAdd(_cursor.xz);
                if (problem != null) { Flash(new LocText(problem)); return; }
                _draw.AddPoint(_cursor, _lastSnap.Kind != SnapKind.None ? _lastSnap.Direction : float2.zero, _lastSnap.Kind == SnapKind.RoadNode ? _lastSnap.Node : 0L);
                _draw.SetFirstClick(_in.Hit.xz);   // (only the first corner's counts)
                _lastAddTime = _in.Time;
                _lastAddPos = _cursor;
                ShapeChanged();
                Commit("Placemaker.Undo.AddPoint");
                return;
            }
            RightClickStartsOver();
        }

        private void CloseOutline()
        {
            var problem = _draw.CheckClose();
            if (problem != null) { Flash(new LocText(problem)); return; }
            _draw.Close();
            ShapeChanged();
            Commit("Placemaker.Undo.CloseArea");
            RebuildShapeIfDirty(true);
            if (_stage == ToolStage.Area && _shape != null) SetStage(ToolStage.Design);
        }

        private void UpdateDrawClosed(float radius)
        {
            _cursor = _in.Hit;
            if (_in.LeftPressed)
            {
                if (_hoverPoint >= 0) { BeginPointDrag(_hoverPoint, false); return; }
                if (_hoverEdge >= 0)
                {
                    _draw.HitEdge(_in.Hit.xz, radius, out _, out float2 foot);
                    var at = WithHeight(new float3(foot.x, 0, foot.y));
                    if (_in.Ctrl)
                    {
                        int idx = _draw.InsertPoint(_hoverEdge, at);
                        if (idx >= 0) { ShapeChanged(); Commit("Placemaker.Undo.InsertPoint"); }
                        else Flash(new LocText("Placemaker.Problem.TooClose"));
                        return;
                    }
                    if (_in.Shift)
                    {
                        if (_draw.BeginEdgeDrag(_hoverEdge))
                        {
                            _gesture = Gesture.DragEdge;
                            _edgeStartCursor = _in.Hit.xz;
                            _edgeGuidePoint = at;
                            _dragMoved = false;
                        }
                        return;
                    }
                    // Plain drag on an edge: like the vanilla area tool, a corner is inserted once the mouse moves.
                    _gesture = Gesture.InsertDrag;
                    _pendingEdge = _hoverEdge;
                    _pressWorld = at;
                    _dragIndex = -1;
                    _dragMoved = false;
                }
                return;
            }
            // Choosing the area: right-click starts over. Designing: a right-click on a corner removes that corner (as in the
            // game's own area tool), anywhere else nothing.
            if (_stage == ToolStage.Area && RightClickStartsOver()) return;
            if (_in.RightPressed && _hoverPoint >= 0)
            {
                if (_draw.RemovePoint(_hoverPoint)) { ShapeChanged(); Commit("Placemaker.Undo.RemovePoint"); }
                else Flash(new LocText(_draw.Count <= PolygonEditor.MinPoints ? "Placemaker.Problem.NeedThreePoints" : "Placemaker.Problem.Crossing"));
            }
        }

        private void BeginPointDrag(int index, bool inserted)
        {
            _gesture = inserted ? Gesture.InsertDrag : Gesture.DragPoint;
            _dragIndex = index;
            _dragStartWorld = _draw.Points[index];
            _pressWorld = _in.Hit;
            _dragMoved = false;
        }

        private void ContinuePointDrag()
        {
            if (_in.LeftHeld)
            {
                if (!_in.HasHit) return;
                if (!_dragMoved && math.distance(_in.Hit.xz, _pressWorld.xz) < DragThreshold) return;
                if (_dragIndex < 0)
                {
                    // Pending insert on an edge: create the corner now that the mouse moved.
                    int idx = _draw.InsertPoint(_pendingEdge, _pressWorld);
                    if (idx < 0) { _gesture = Gesture.None; return; }
                    _dragIndex = idx;
                    _dragStartWorld = _pressWorld;
                }
                _dragMoved = true;
                var target = Snap(_in.Hit, _dragIndex);
                _cursor = _draw.MovePoint(_dragIndex, target, _draw.Points[_dragIndex]);
                _draw.SetAxis(_dragIndex, _lastSnap.Kind != SnapKind.None ? _lastSnap.Direction : float2.zero);
                _draw.SetNode(_dragIndex, _lastSnap.Kind == SnapKind.RoadNode ? _lastSnap.Node : 0L);
                _hoverPoint = _dragIndex;
                ShapeChanged(throttled: true);
                return;
            }
            // Released.
            var g = _gesture;
            _gesture = Gesture.None;
            if (_dragMoved)
            {
                ShapeChanged();
                Commit(g == Gesture.InsertDrag ? "Placemaker.Undo.InsertPoint" : "Placemaker.Undo.MovePoint");
            }
            _dragIndex = -1;
            _pendingEdge = -1;
        }

        private void ContinueEdgeDrag()
        {
            if (_in.LeftHeld)
            {
                if (!_in.HasHit) return;
                float raw = math.dot(_in.Hit.xz - _edgeStartCursor, _draw.EdgeNormal);
                if (!_dragMoved && math.abs(raw) < DragThreshold) return;
                _dragMoved = true;
                float offset = raw;
                // Snap the edge to the world (probe at its middle) or to line up with another corner.
                var pts = _draw.Points;
                int e = _draw.DraggedEdge;
                float cornerSnap = _draw.SnapEdgeOffset(raw, 1.5f, out bool cornerHit);
                float worldSnap = raw;
                bool worldHit = false;
                if (!_in.Alt)
                {
                    var probe = _edgeGuidePoint;
                    probe.xz += _draw.EdgeNormal * raw;
                    var snapped = SnapWorldOnly(probe);
                    if (_lastSnap.Kind != SnapKind.None)
                    {
                        worldSnap = raw + math.dot(snapped.xz - probe.xz, _draw.EdgeNormal);
                        worldHit = true;
                    }
                }
                if (worldHit && (!cornerHit || math.abs(worldSnap - raw) <= math.abs(cornerSnap - raw))) offset = worldSnap;
                else if (cornerHit) offset = cornerSnap;
                _draw.DragEdge(offset, true);
                _cursor = _in.Hit;
                ShapeChanged(throttled: true);
                return;
            }
            _gesture = Gesture.None;
            if (_dragMoved)
            {
                _draw.EndEdgeDrag();
                ShapeChanged();
                Commit("Placemaker.Undo.MoveEdge");
            }
            else _draw.CancelEdgeDrag();
        }

        private float3 SnapWorldOnly(float3 raw)
        {
            _lastSnap = new SnapResult { Position = raw };
            var mask = GetActualSnap() & (global::Game.Tools.Snap.NetSide | global::Game.Tools.Snap.ObjectSide
                                          | global::Game.Tools.Snap.ExistingGeometry | global::Game.Tools.Snap.ZoneGrid);
            if (mask == global::Game.Tools.Snap.None) return raw;
            _resolver.SnapDistance = math.clamp(HandleRadius(raw), 3f, 16f);
            _resolver.Begin(raw);
            _snapCollector.Collect(_resolver, mask);
            _lastSnap = _resolver.Result();
            return _lastSnap.Position;
        }

        /// <summary>Esc / tool deactivation mid-drag: put everything back as it was before the gesture.</summary>
        private void CancelGesture()
        {
            // a finished Paint area stroke waiting for its outline is kept (an undo step), not cancelled
            CommitBlockPaintStroke(true);
            switch (_gesture)
            {
                case Gesture.DragPoint:
                case Gesture.InsertDrag:
                case Gesture.DragEdge:
                case Gesture.Paint:
                case Gesture.Erase:
                    _draw.CancelEdgeDrag();
                    // The committed snapshot is the state before the gesture.
                    var s = _history.Current;
                    _gesture = Gesture.None;
                    if (s != null) RestoreSelectionOnly(s);
                    break;
            }
            // a click waiting for its fill (bucket) or a Paint area stroke waiting to start
            _bucketClick = null;
            if (_bpStart != null)
            {
                _bpStart = null;
                if (_history.Current != null) RestoreSelectionOnly(_history.Current);
            }
            _gesture = Gesture.None;
            _dragIndex = -1;
            _pendingEdge = -1;
            EndValuePreview(false);
        }

        private void RestoreSelectionOnly(ToolSnapshot s)
        {
            _draw.Restore(s.Draw);
            if (s.Paint != null) _paint.Restore(s.Paint); else _paint.Clear();
            _paintSnap = s.Paint;
            _paintSnapVersion = _paint.Version;
            RestoreBlockPaint(s.BlockPaint);
            _shape = s.Shape;
            _shapeDirty = false;
            ShapeChanged(alreadyBuilt: true);
        }
    }
}
