// Placemaker — the draw-mode outline editor, engine-free.
//
// Behaviour ported from ParkingLotTool's polygon editor (KruemelGames, GPL-3.0:
// Tools/ParkingLotToolSystem.cs, ParkingLotEdgeDrag.cs): every edit that would make the outline
// self-intersect is refused or shortened instead of producing a broken area — a moved corner or
// edge stops at the last valid position found by bisection, exactly like PLT's SafeEdgeOffset.
// That keeps the outline usable at all times, which matters because CS2 silently drops areas
// it cannot triangulate (RESEARCH-NOTES rule 6).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public sealed class PolygonEditor
    {
        public const int MinPoints = 3;
        /// <summary>Points closer than this are treated as the same point (CS2 drops edges &lt; 0.3 m).</summary>
        public const float MinEdge = 0.5f;
        public const int MaxPoints = 256;
        private const int BisectionSteps = 14;

        public sealed class Snapshot
        {
            public float3[] Points = Array.Empty<float3>();
            public float2[] Axes = Array.Empty<float2>();
            /// <summary>(Area overhaul) The road node each corner sits on (packed entity; 0 = none).</summary>
            public long[] Nodes = Array.Empty<long>();
            public bool Closed;
        }

        private readonly List<float3> _points = new List<float3>();
        /// <summary>Snap axis each point was placed with (for guide lines through earlier corners).</summary>
        private readonly List<float2> _axes = new List<float2>();
        /// <summary>(Area overhaul) The road node each corner was snapped to (0 = none): two in a row follow the road between.</summary>
        private readonly List<long> _nodes = new List<long>();

        public IReadOnlyList<float3> Points => _points;
        public IReadOnlyList<float2> Axes => _axes;
        public IReadOnlyList<long> Nodes => _nodes;
        public bool Closed { get; private set; }
        public int Count => _points.Count;
        public bool IsEmpty => _points.Count == 0;

        /// <summary>Bumped on every change, so the tool can detect "shape changed" cheaply.</summary>
        public int Version { get; private set; }

        // ───────────── snapshot ─────────────

        public Snapshot Save() => new Snapshot { Points = _points.ToArray(), Axes = _axes.ToArray(), Nodes = _nodes.ToArray(), Closed = Closed };

        public void Restore(Snapshot s)
        {
            _points.Clear();
            _axes.Clear();
            _nodes.Clear();
            if (s != null)
            {
                _points.AddRange(s.Points ?? Array.Empty<float3>());
                for (int i = 0; i < _points.Count; i++) _axes.Add(s.Axes != null && i < s.Axes.Length ? s.Axes[i] : float2.zero);
                for (int i = 0; i < _points.Count; i++) _nodes.Add(s.Nodes != null && i < s.Nodes.Length ? s.Nodes[i] : 0L);
                Closed = s.Closed && _points.Count >= MinPoints;
            }
            else Closed = false;
            Version++;
        }

        public void Clear()
        {
            if (_points.Count == 0 && !Closed) return;
            _points.Clear();
            _axes.Clear();
            _nodes.Clear();
            Closed = false;
            Version++;
        }

        /// <summary>Loads a finished ring (e.g. a block outline converted to editable corners).</summary>
        public void SetRing(IReadOnlyList<float3> ring)
        {
            _points.Clear();
            _axes.Clear();
            _nodes.Clear();
            if (ring != null)
                foreach (var p in ring) { _points.Add(p); _axes.Add(float2.zero); _nodes.Add(0L); }
            Closed = _points.Count >= MinPoints;
            Version++;
        }

        // ───────────── queries ─────────────

        public float2[] Ring2D()
        {
            var r = new float2[_points.Count];
            for (int i = 0; i < r.Length; i++) r[i] = _points[i].xz;
            return r;
        }

        public float Area() => Closed && _points.Count >= MinPoints ? Poly.Area(Ring2D()) : 0f;

        public float Perimeter()
        {
            float p = 0;
            for (int i = 0; i + 1 < _points.Count; i++) p += math.distance(_points[i].xz, _points[i + 1].xz);
            if (Closed && _points.Count >= 2) p += math.distance(_points[_points.Count - 1].xz, _points[0].xz);
            return p;
        }

        /// <summary>The closed outline as a CCW ring (AreaShape convention), or empty when not closed/simple.</summary>
        public float3[] RingCcw()
        {
            if (!Closed || _points.Count < MinPoints) return Array.Empty<float3>();
            var ring = _points.ToArray();
            if (!Poly.IsSimple(Ring2D())) return Array.Empty<float3>();
            return Poly.EnsureCcw(ring);
        }

        public bool IsValid => Closed && _points.Count >= MinPoints && Poly.IsSimple(Ring2D()) && Area() > 1f;

        /// <summary>Nearest corner within <paramref name="radius"/>, or -1.</summary>
        public int HitPoint(float2 p, float radius)
        {
            int best = -1;
            float bestD = radius;
            for (int i = 0; i < _points.Count; i++)
            {
                float d = math.distance(_points[i].xz, p);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Nearest edge within <paramref name="radius"/> (closed outline: includes the closing edge), or -1.</summary>
        public int HitEdge(float2 p, float radius, out float t, out float2 foot)
        {
            t = 0; foot = p;
            int best = -1;
            float bestD = radius;
            int n = _points.Count;
            int edges = Closed ? n : n - 1;
            for (int i = 0; i < edges; i++)
            {
                float2 a = _points[i].xz, b = _points[(i + 1) % n].xz;
                float tt = Project(a, b, p);
                float2 f = math.lerp(a, b, tt);
                float d = math.distance(f, p);
                if (d <= bestD) { bestD = d; best = i; t = tt; foot = f; }
            }
            return best;
        }

        // ───────────── open polygon: adding corners ─────────────

        /// <summary>Why a new corner at p would be refused (null = fine).</summary>
        public string CheckAdd(float2 p)
        {
            if (Closed) return "Placemaker.Problem.AlreadyClosed";
            if (_points.Count >= MaxPoints) return "Placemaker.Problem.TooManyPoints";
            if (_points.Count == 0) return null;
            float2 last = _points[_points.Count - 1].xz;
            if (math.distance(last, p) < MinEdge) return "Placemaker.Problem.TooClose";
            for (int i = 0; i < _points.Count - 1; i++)
                if (math.distance(_points[i].xz, p) < MinEdge) return "Placemaker.Problem.TooClose";
            // The new segment may touch only its own predecessor segment.
            for (int i = 0; i + 1 < _points.Count - 1; i++)
                if (Poly.SegmentsIntersect(last, p, _points[i].xz, _points[i + 1].xz)) return "Placemaker.Problem.Crossing";
            return null;
        }

        public bool AddPoint(float3 p, float2 axis = default, long node = 0L)
        {
            if (CheckAdd(p.xz) != null) return false;
            _points.Add(p);
            _axes.Add(axis);
            _nodes.Add(node);
            Version++;
            return true;
        }

        /// <summary>Why the outline cannot be closed now (null = it can).</summary>
        public string CheckClose()
        {
            if (Closed) return "Placemaker.Problem.AlreadyClosed";
            if (_points.Count < MinPoints) return "Placemaker.Problem.NeedThreePoints";
            if (!Poly.IsSimple(Ring2D())) return "Placemaker.Problem.Crossing";
            if (Poly.Area(Ring2D()) <= 1f) return "Placemaker.Problem.TooSmall";
            return null;
        }

        public bool Close()
        {
            if (CheckClose() != null) return false;
            Closed = true;
            Version++;
            return true;
        }

        /// <summary>Removes the last corner of an open outline (right-click while drawing).</summary>
        public bool RemoveLast()
        {
            if (Closed || _points.Count == 0) return false;
            _points.RemoveAt(_points.Count - 1);
            _axes.RemoveAt(_axes.Count - 1);
            _nodes.RemoveAt(_nodes.Count - 1);
            Version++;
            return true;
        }

        // ───────────── editing corners ─────────────

        /// <summary>
        /// Removes a corner. Refused when a closed outline would drop below three corners or become
        /// self-intersecting (removing a corner can make the two neighbours' new edge cross others).
        /// </summary>
        public bool RemovePoint(int index)
        {
            if (index < 0 || index >= _points.Count) return false;
            if (Closed)
            {
                if (_points.Count <= MinPoints) return false;
                var probe = new List<float2>(Ring2D());
                probe.RemoveAt(index);
                if (!Poly.IsSimple(probe) || Poly.Area(probe) <= 1f) return false;
            }
            _points.RemoveAt(index);
            _axes.RemoveAt(index);
            _nodes.RemoveAt(index);
            Version++;
            return true;
        }

        /// <summary>Inserts a corner on edge <paramref name="edge"/> (between edge and edge+1). Returns the new index or -1.</summary>
        public int InsertPoint(int edge, float3 p)
        {
            int n = _points.Count;
            int edges = Closed ? n : n - 1;
            if (edge < 0 || edge >= edges || n >= MaxPoints) return -1;
            float2 a = _points[edge].xz, b = _points[(edge + 1) % n].xz;
            if (math.distance(a, p.xz) < MinEdge || math.distance(b, p.xz) < MinEdge) return -1;
            var probe = new List<float2>(Ring2D());
            probe.Insert(edge + 1, p.xz);
            if (Closed ? !Poly.IsSimple(probe) : !OpenChainSimple(probe)) return -1;
            _points.Insert(edge + 1, p);
            _axes.Insert(edge + 1, float2.zero);
            _nodes.Insert(edge + 1, 0L);
            Version++;
            return edge + 1;
        }

        /// <summary>
        /// Moves a corner towards <paramref name="target"/>. If the target would make the outline
        /// self-intersect (or collapse an edge), the corner stops at the furthest valid point on the
        /// way (bisection). Returns the position actually applied.
        /// </summary>
        public float3 MovePoint(int index, float3 target, float3 from)
        {
            if (index < 0 || index >= _points.Count) return target;
            if (MoveValid(index, target.xz)) { Set(index, target); return target; }
            if (!MoveValid(index, from.xz)) { Set(index, target); return target; }   // already invalid: do not trap the player
            float lo = 0f, hi = 1f;
            for (int s = 0; s < BisectionSteps; s++)
            {
                float mid = (lo + hi) * 0.5f;
                if (MoveValid(index, math.lerp(from.xz, target.xz, mid))) lo = mid; else hi = mid;
            }
            var p = math.lerp(from, target, lo);
            Set(index, p);
            return p;
        }

        private void Set(int index, float3 p)
        {
            if (_points[index].Equals(p)) return;
            _points[index] = p;
            Version++;
        }

        public void SetAxis(int index, float2 axis)
        {
            if (index >= 0 && index < _axes.Count) _axes[index] = axis;
        }

        /// <summary>(Area overhaul) The road node corner <paramref name="index"/> sits on (0 = none).</summary>
        public void SetNode(int index, long node)
        {
            if (index < 0 || index >= _nodes.Count || _nodes[index] == node) return;
            _nodes[index] = node;
            Version++;
        }

        private bool MoveValid(int index, float2 p)
        {
            var probe = Ring2D();
            probe[index] = p;
            int n = probe.Length;
            if (n >= 2)
            {
                if (index > 0 || Closed)
                    if (math.distance(probe[(index - 1 + n) % n], p) < MinEdge) return false;
                if (index < n - 1 || Closed)
                    if (math.distance(probe[(index + 1) % n], p) < MinEdge) return false;
            }
            return Closed ? ClosedValid(probe, math.sign(Poly.SignedArea(Ring2D()))) : OpenChainSimple(probe);
        }

        /// <summary>
        /// Simple, non-degenerate and still wound the same way: a drag must never flip the outline
        /// inside-out (a mirrored polygon is "simple" too, but it is not the shape the player had).
        /// </summary>
        private static bool ClosedValid(IReadOnlyList<float2> probe, float referenceSign)
        {
            float signed = Poly.SignedArea(probe);
            return math.abs(signed) > 1f && math.sign(signed) == referenceSign && Poly.IsSimple(probe);
        }

        private static bool OpenChainSimple(IReadOnlyList<float2> pts)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
                for (int j = i + 2; j + 1 < pts.Count; j++)
                    if (Poly.SegmentsIntersect(pts[i], pts[i + 1], pts[j], pts[j + 1])) return false;
            return true;
        }

        // ───────────── edge drag (Shift+drag: move a whole edge) ─────────────

        private float2[] _edgeStart;
        private float3[] _edgeStartWorld;
        private int _edge = -1;
        private float2 _edgeNormal;

        public bool EdgeDragActive => _edge >= 0;
        public int DraggedEdge => _edge;
        public float2 EdgeNormal => _edgeNormal;

        public bool BeginEdgeDrag(int edge)
        {
            int n = _points.Count;
            if (!Closed || edge < 0 || edge >= n) return false;
            _edge = edge;
            _edgeStart = Ring2D();
            _edgeStartWorld = _points.ToArray();
            float2 a = _edgeStart[edge], b = _edgeStart[(edge + 1) % n];
            float2 tangent = b - a;
            float len = math.length(tangent);
            _edgeNormal = len < 1e-6f ? new float2(0, 1) : new float2(-tangent.y, tangent.x) / len;
            return true;
        }

        /// <summary>
        /// Moves the dragged edge by <paramref name="offset"/> metres along its normal. With
        /// <paramref name="slideNeighbours"/> the two corners slide along their other edges so the
        /// neighbouring edges keep their direction (PLT's Shift behaviour); otherwise they move
        /// straight along the normal. The offset is shortened to the last valid one.
        /// Returns the offset applied.
        /// </summary>
        public float DragEdge(float offset, bool slideNeighbours)
        {
            if (_edge < 0) return 0;
            float applied = EdgeOffsetValid(offset, slideNeighbours) ? offset : SafeOffset(offset, slideNeighbours);
            ApplyEdge(applied, slideNeighbours);
            return applied;
        }

        public void EndEdgeDrag()
        {
            // Corners moved off their road nodes no longer follow the road.
            if (_edge >= 0 && _edgeStartWorld != null)
                for (int k = 0; k < 2; k++)
                {
                    int i = (_edge + k) % _points.Count;
                    if (i < _nodes.Count && i < _edgeStartWorld.Length && !_points[i].Equals(_edgeStartWorld[i])) _nodes[i] = 0L;
                }
            _edge = -1;
            _edgeStart = null;
            _edgeStartWorld = null;
        }

        public void CancelEdgeDrag()
        {
            if (_edge < 0) return;
            for (int i = 0; i < _points.Count && i < _edgeStartWorld.Length; i++) _points[i] = _edgeStartWorld[i];
            Version++;
            EndEdgeDrag();
        }

        /// <summary>Signed distance of other corners along the edge normal — snap targets so an edge lines up with a corner (PLT SnapEdgeOffset).</summary>
        public float SnapEdgeOffset(float wanted, float snapDistance, out bool snapped)
        {
            snapped = false;
            if (_edge < 0) return wanted;
            int n = _edgeStart.Length;
            int a = _edge, b = (_edge + 1) % n;
            float best = wanted, bestD = snapDistance;
            for (int i = 0; i < n; i++)
            {
                if (i == a || i == b) continue;
                float cand = math.dot(_edgeStart[i] - _edgeStart[a], _edgeNormal);
                float d = math.abs(cand - wanted);
                if (d > bestD) continue;
                bestD = d; best = cand; snapped = true;
            }
            return best;
        }

        private float SafeOffset(float wanted, bool slide)
        {
            float lo = 0f, hi = wanted;
            for (int s = 0; s < BisectionSteps; s++)
            {
                float mid = (lo + hi) * 0.5f;
                if (EdgeOffsetValid(mid, slide)) lo = mid; else hi = mid;
            }
            return lo;
        }

        private bool EdgeOffsetValid(float offset, bool slide)
        {
            var probe = (float2[])_edgeStart.Clone();
            int n = probe.Length;
            int a = _edge, b = (_edge + 1) % n;
            probe[a] = MovedCorner(a, a - 1, offset, slide);
            probe[b] = MovedCorner(b, b + 1, offset, slide);
            for (int i = 0; i < n; i++)
                if (math.distance(probe[i], probe[(i + 1) % n]) < MinEdge) return false;
            return ClosedValid(probe, math.sign(Poly.SignedArea(_edgeStart)));
        }

        private void ApplyEdge(float offset, bool slide)
        {
            int n = _edgeStart.Length;
            int a = _edge, b = (_edge + 1) % n;
            var pa = MovedCorner(a, a - 1, offset, slide);
            var pb = MovedCorner(b, b + 1, offset, slide);
            _points[a] = new float3(pa.x, _edgeStartWorld[a].y, pa.y);
            _points[b] = new float3(pb.x, _edgeStartWorld[b].y, pb.y);
            Version++;
        }

        private float2 MovedCorner(int corner, int neighbour, float offset, bool slide)
        {
            var start = _edgeStart[corner];
            var straight = start + _edgeNormal * offset;
            int n = _edgeStart.Length;
            if (!slide || n < 3) return straight;
            var other = _edgeStart[((neighbour % n) + n) % n];
            var dir = start - other;
            float len = math.length(dir);
            if (len < 1e-6f) return straight;
            dir /= len;
            float along = math.dot(dir, _edgeNormal);
            // Nearly parallel neighbour edge: sliding would shoot the corner far away.
            if (math.abs(along) < 0.25f) return straight;
            return start + dir * (offset / along);
        }

        // ───────────── shape problems ─────────────

        /// <summary>Localisation key describing why the closed outline is unusable, or null.</summary>
        public string Problem()
        {
            if (!Closed) return null;
            var r = Ring2D();
            if (r.Length < MinPoints) return "Placemaker.Problem.NeedThreePoints";
            if (!Poly.IsSimple(r)) return "Placemaker.Problem.Crossing";
            if (Poly.Area(r) <= 1f) return "Placemaker.Problem.TooSmall";
            return null;
        }

        public static float Project(float2 a, float2 b, float2 p)
        {
            var ab = b - a;
            float len = math.lengthsq(ab);
            return len < 1e-9f ? 0f : math.saturate(math.dot(p - a, ab) / len);
        }
    }
}
