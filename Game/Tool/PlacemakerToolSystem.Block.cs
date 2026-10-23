// Block mode ("Paint bucket"), area overhaul (2026-10-17): click takes the fill under the cursor;
// Shift+click adds another fill next to it (or removes one); right-click removes the one under the cursor.
//
// The fill is Core/Geometry/AreaFill on the shared walls (Game/Selection/AreaWalls, the "Stops at" rules): road and path
// surfaces with their junctions and turning circles, buildings, lots, my builds, zoning, water. Openings narrower than
// the Gap setting count as closed and narrower places are left out; the Reach setting bounds it in open land. It runs
// in the background (AreaFillRunner), only after a click (2026-10-21; it used to fill on hover, a lag spike each time the
// cursor left the outline), and is taken when it is back. The user's grades
// that started it: "Paint bucket gets like a D minus ... it needs more settings ... a threshold ... doesn't always avoid
// zones ... in corners it doesn't get to the full corner ... goes inside tiny little cracks ... goes onto roads even
// though I have avoid roads on ... things cut into it that don't make any sense".
// Before the overhaul it traced faces of a road graph (FillGraph) and cut area walls afterwards; that code stays for
// Road nodes' old snapshots, nothing here uses it.

using System;
using System.Collections.Generic;
using Game.Net;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Placemaker.Game.Selection;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>A fill under the cursor (hover) or clicked: the result and its outline as an area.</summary>
        public sealed class BlockPick
        {
            public FillResult Fill;
            public AreaShape Area;
            public float2 Seed;
            /// <summary>Overlay: the stops along its edge (red), its closed gaps, the reach circle when it met it.</summary>
            public List<float3[]> Stops = new List<float3[]>();
            public List<float3[]> Doors = new List<float3[]>();
            public float3[] Reach;
        }

        private BlockPick _hoverFace;
        private LocText _hoverFaceProblem;
        private AreaWalls _areaWalls;
        private AreaFillRunner _fills;

        public BlockPick HoverFace => _hoverFace;
        public LocText HoverFaceProblem => _hoverFaceProblem;
        public BlockSelectionState Blocks => _blocks;
        /// <summary>The fill being hovered is still being worked out (the overlay can say so).</summary>
        public bool FillBusy => _fills != null && _fills.Busy;

        private AreaWalls Walls => _areaWalls ??= new AreaWalls(EntityManager, _roads, _bpObstacles ??= new BlockObstacles(EntityManager, World));
        private AreaFillRunner Fills => _fills ??= new AreaFillRunner(Walls);

        /// <summary>The city changed (a build, an undo of one): forget walls, grids and fills.</summary>
        private void InvalidateFills()
        {
            _areaWalls?.Invalidate();
            _fills?.Invalidate();
            _hoverFace = null;
            _bucketClick = null;
        }

        private int _blockRulesKey;
        /// <summary>A bucket click whose fill is still being worked out (and whether Shift was held).</summary>
        private float2? _bucketClick;
        private bool _bucketShift;
        /// <summary>A click is waiting for its fill (the overlay can say so).</summary>
        public bool FillPending => _bucketClick.HasValue || _bpStart != null;

        /// <summary>
        /// The reach as a circle at the cursor (the user, 2026-10-22: "the reach should be shown in like a dotted outline as I
        /// move my mouse around ... just the full circle"): Paint bucket, choosing the area, over the world, no markers.
        /// </summary>
        public bool BucketReachLive => IsActive && _mode == SelectionMode.Block && _stage == ToolStage.Area && _markerKind == null
            && _brush == 0 && !_outgrowMode && _in.OverWorld && _in.HasHit;
        /// <summary>The radius the fill stops at in open land (clamped as AreaFill clamps it).</summary>
        public float BucketReach => math.clamp(BrushRules.Fill().Reach, FillOptions.MinReach, FillOptions.MaxReach);

        private int FillKey(BrushRules rules)
        {
            var r = rules.ForOutline();
            return r.GetHashCode() * 31 + _editingGroup;
        }

        private void UpdateBlock()
        {
            _cursorValid = _in.HasHit;
            Fills.Pump();
            int key = FillKey(BrushRules);
            if (key != _blockRulesKey)
            {
                bool first = _blockRulesKey == 0;
                _blockRulesKey = key;
                if (!first && _blocks.Seeds.Count > 0 && _gesture == Gesture.None) RepickBlocks();
            }
            // No fill on hover (2026-10-21, the user: "when I mouse over things with my paint bucket tool, not even
            // clicking, I get a lag spike. Shouldn't we not calculate the fill until you click?"): a click asks for the fill
            // in the background and takes it a few frames later, when it is back.
            if (_in.HasHit) _cursor = _in.Hit;
            // the clicked fill keeps its stops, closed gaps and reach on show while the cursor stays in it
            if (_hoverFace?.Fill?.Ring != null && !(_in.HasHit && Poly.Contains(_hoverFace.Fill.Ring, _in.Hit.xz))) _hoverFace = null;

            // Right-click starts over while choosing the area (a fill still on its way is dropped too).
            if (RightClickStartsOver()) { if (_stage == ToolStage.Area) _bucketClick = null; return; }
            if (_in.LeftPressed && _in.HasHit)
            {
                _bucketClick = _in.Hit.xz;
                _bucketShift = _in.Shift;
                Fills.Request(_bucketClick.Value, BrushRules, _editingGroup);
                Touch();
            }
            if (_bucketClick.HasValue)
            {
                var r = Fills.Ready(_bucketClick.Value, BrushRules, _editingGroup);
                if (r == null) { if (!Fills.Busy) Fills.Request(_bucketClick.Value, BrushRules, _editingGroup); return; }
                _bucketClick = null;
                Touch();
                LogFill("click", r);
                var pick = r.Status == FillStatus.Ok ? ToPick(r) : null;
                if (pick == null) { Flash(Problem(r) ?? new LocText("Placemaker.Problem.NoBlockHere")); return; }
                _hoverFace = pick;
                if (!_bucketShift)
                {
                    _blocks = new BlockSelectionState();
                    _blocks.Seeds.Add(pick.Seed);
                    _blocks.Rings.Add(pick.Fill.Ring);
                    ShapeChanged();
                    Commit("Placemaker.Undo.SelectBlock");
                    if (_stage == ToolStage.Area && _shape != null) SetStage(ToolStage.Design);
                    return;
                }
                ToggleBlock(pick);
                return;
            }
        }

        /// <summary>The fill at a click: the hover's when the click is inside it, else filled now.</summary>
        private BlockPick PickAt(float2 p, out LocText problem)
        {
            problem = null;
            if (_hoverFace?.Fill?.Ring != null && Poly.Contains(_hoverFace.Fill.Ring, p)) return _hoverFace;
            FillResult r;
            try { r = Fills.Now(p, BrushRules, _editingGroup); }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker: fill failed: " + e.Message);
                problem = new LocText("Placemaker.Problem.NoBlockHere");
                return null;
            }
            LogFill("click", r);
            problem = Problem(r);
            return r.Status == FillStatus.Ok ? ToPick(r) : null;
        }

        private static LocText Problem(FillResult r)
        {
            if (r == null) return null;
            switch (r.Status)
            {
                case FillStatus.Ok: return null;
                case FillStatus.OnWall: return new LocText("Placemaker.Problem.FillOnWall");
                case FillStatus.TooSmall: return new LocText("Placemaker.Problem.FillTooSmall");
                default: return new LocText("Placemaker.Problem.NoBlockHere");
            }
        }

        private void LogFill(string what, FillResult r)
        {
            Mod.Log.Info($"Placemaker: fill ({what}) at ({r.Seed.x:0.0}, {r.Seed.y:0.0}): {r}" + (r.Error != null ? " — " + r.Error : "") + "; walls " + (_areaWalls?.LastStats ?? ""));
        }

        private BlockPick ToPick(FillResult r)
        {
            var pick = new BlockPick { Fill = r, Seed = r.Seed, Area = FillShape(r.Ring, r) };
            Func<float2, float> y = p => _site.SampleHeight(p);
            foreach (var d in r.Doors) if (d != null && d.Length >= 2) pick.Doors.Add(Poly.To3D(d, y));
            if (r.HitReach) pick.Reach = Poly.To3D(AreaFill.Circle(r.Seed, r.Reach, 96), y);
            var walls = r.Window_?.Walls;
            if (walls != null && r.Ring != null)
            {
                Poly.Bounds(r.Ring, out var mn, out var mx);
                foreach (var w in walls)
                {
                    if (pick.Stops.Count >= 400) break;
                    if (w.Ring == null || IsNet(w.Kind)) continue;
                    Poly.Bounds(w.Ring, out var a, out var b);
                    if (b.x < mn.x - 1f || b.y < mn.y - 1f || a.x > mx.x + 1f || a.y > mx.y + 1f) continue;
                    if (ShowsInBlock(w.Ring, r.Ring)) pick.Stops.Add(Poly.To3D(w.Ring, y));
                }
            }
            return pick;
        }

        private static bool IsNet(BrushObstacleKind k) => k == BrushObstacleKind.Road || k == BrushObstacleKind.Path || k == BrushObstacleKind.Bike || k == BrushObstacleKind.Track;

        /// <summary>An outline from the fill as an area: heights from the ground, attachments to the roads along it.</summary>
        private AreaShape FillShape(float2[] ring, FillResult r)
        {
            if (ring == null || ring.Length < 3) return null;
            return new AreaShape { Ring = Poly.To3D(ring, p => _site.SampleHeight(p)), Source = RoadBound(ring) ? AreaSource.Block : AreaSource.Painted };
        }

        /// <summary>
        /// The outline runs along road curbs for most of its length (a city block): the roads planner then adds no
        /// perimeter road and anchors to the junctions on it, like the old block pick.
        /// </summary>
        private bool RoadBound(float2[] ring)
        {
            var roads = new List<float2[]>();
            Poly.Bounds(ring, out var mn, out var mx);
            foreach (var w in Walls.All(mn - 2f, mx + 2f, _editingGroup))
            {
                if (w.Kind != BrushObstacleKind.Road || w.Ring == null) continue;
                Poly.Bounds(w.Ring, out var a, out var b);
                if (b.x < mn.x - 1f || b.y < mn.y - 1f || a.x > mx.x + 1f || a.y > mx.y + 1f) continue;
                roads.Add(w.Ring);
            }
            if (roads.Count == 0) return false;
            float on = 0f, all = 0f;
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % n];
                float len = math.distance(a, b);
                all += len;
                var mid = (a + b) * 0.5f;
                foreach (var w in roads)
                    if (Poly.DistanceToBoundary(w, mid) < 0.6f) { on += len; break; }
            }
            return all > 0f && on >= 0.6f * all;
        }

        private int IndexOfFill(float2 p)
        {
            for (int i = 0; i < _blocks.Rings.Count; i++)
                if (_blocks.Rings[i] != null && Poly.Contains(_blocks.Rings[i], p)) return i;
            return -1;
        }

        private void ToggleBlock(BlockPick pick)
        {
            int idx = IndexOfFill(pick.Seed);
            if (idx >= 0) { RemoveBlock(idx); return; }
            var next = _blocks.Clone();
            next.Seeds.Add(pick.Seed);
            next.Rings.Add(pick.Fill.Ring);
            if (next.Rings.Count > 1 && UnionOfFills(next.Rings) == null) { Flash(new LocText("Placemaker.Problem.NotAdjacent")); return; }
            _blocks = next;
            ShapeChanged();
            Commit("Placemaker.Undo.AddBlock");
            if (_stage == ToolStage.Area && _shape != null) SetStage(ToolStage.Design);
        }

        private void RemoveBlock(int idx)
        {
            var next = _blocks.Clone();
            next.Seeds.RemoveAt(idx);
            next.Rings.RemoveAt(idx);
            if (next.Rings.Count > 1 && UnionOfFills(next.Rings) == null) { Flash(new LocText("Placemaker.Problem.WouldSplit")); return; }
            _blocks = next;
            ShapeChanged();
            Commit("Placemaker.Undo.RemoveBlock");
        }

        /// <summary>Gap (m) between two fills that Shift+click still joins (a road or a path between two blocks).</summary>
        private const float JoinAcross = 40f;

        /// <summary>
        /// Several fills as one outline: their union, and where two of them face each other across less than JoinAcross
        /// (a street between two blocks) the strip between them. Null when they stay apart.
        /// </summary>
        private static float2[] UnionOfFills(List<float2[]> rings)
        {
            var list = new List<IReadOnlyList<float2>>();
            foreach (var r in rings) if (r != null && r.Length >= 3) list.Add(Poly.EnsureCcw((float2[])r.Clone()));
            if (list.Count == 0) return null;
            if (list.Count == 1) return (float2[])list[0];
            List<Region> union;
            try { union = Poly.Boolean(list, null, BoolOp.Union, FillRule.NonZero); }
            catch (Exception) { return null; }
            if (union.Count > 1)
            {
                // The strips between them: a closing of the union, kept only where it touches two different fills.
                float d = JoinAcross * 0.5f;
                List<Region> closed;
                try { closed = Poly.Offset(Poly.Offset(union, -d, OffsetJoin.Round, 4f, 0.3f), d, OffsetJoin.Round, 4f, 0.3f); }
                catch (Exception) { return null; }
                List<Region> extra;
                try { extra = Poly.Boolean(closed, union, BoolOp.Difference); }
                catch (Exception) { return null; }
                var keep = new List<IReadOnlyList<float2>>(list);
                foreach (var e in extra)
                {
                    var grown = Poly.Offset(e.Outer, -0.3f);
                    var probe = grown.Count > 0 ? grown[0] : e.Outer;
                    int touches = 0;
                    foreach (var r in list) if (ObstacleCut.Overlaps(probe, r)) touches++;
                    if (touches >= 2) keep.Add(e.Outer);
                }
                try { union = Poly.Boolean(keep, null, BoolOp.Union, FillRule.NonZero); }
                catch (Exception) { return null; }
            }
            if (union.Count != 1) return null;
            var u = union[0];
            if (!u.HasHoles) return u.Outer;
            var cut = Poly.CutHoles(u, ObstacleCut.Corridor);
            float2[] best = null;
            float area = 0f;
            foreach (var c in cut) { float a = Poly.Area(c); if (a > area) { area = a; best = c; } }
            return best;
        }

        /// <summary>(Rules changed) Fill the picked spots again with the new stops, gap and reach.</summary>
        private void RepickBlocks()
        {
            _hoverFace = null;
            if (_blocks.Seeds.Count == 0) return;
            var next = new BlockSelectionState();
            foreach (var seed in _blocks.Seeds)
            {
                FillResult r;
                try { r = Fills.Now(seed, BrushRules, _editingGroup); }
                catch (Exception) { continue; }
                if (r.Status != FillStatus.Ok) continue;
                if (next.Rings.Exists(x => x != null && Poly.Contains(x, seed))) continue;
                next.Seeds.Add(seed);
                next.Rings.Add(r.Ring);
            }
            if (next.Rings.Count > 1 && UnionOfFills(next.Rings) == null)
            {
                int last = next.Rings.Count - 1;
                next = new BlockSelectionState { Seeds = { next.Seeds[last] }, Rings = { next.Rings[last] } };
            }
            _blocks = next;
            ShapeChanged();
        }

        /// <summary>Shape for the picked fills (their union).</summary>
        private AreaShape BuildBlockShape()
        {
            if (_blocks.Seeds.Count == 0) return null;
            // Snapshots from before the overhaul hold road loops, not fills: fill again from where they were clicked.
            while (_blocks.Rings.Count < _blocks.Seeds.Count)
            {
                var seed = _blocks.Seeds[_blocks.Rings.Count];
                var r = Fills.Now(seed, BrushRules, _editingGroup);
                _blocks.Rings.Add(r.Status == FillStatus.Ok ? r.Ring : null);
            }
            var rings = _blocks.Rings.FindAll(x => x != null && x.Length >= 3);
            if (rings.Count == 0) return null;
            var ring = rings.Count == 1 ? rings[0] : UnionOfFills(rings);
            if (ring == null) return null;
            var shape = FillShape(ring, null);
            if (shape == null) return null;
            WithFoundAttachments(shape);
            AddNodeAttachments(shape);
            return shape;
        }

        /// <summary>
        /// Road junctions and dead ends on the outline (within their own half-width of it) as RoadNode attachments, as the
        /// old block pick gave for every corner of its road loop (entrances and the roads planner anchor to them).
        /// </summary>
        private void AddNodeAttachments(AreaShape shape)
        {
            try
            {
                var ring2 = shape.Ring2D();
                Poly.Bounds(ring2, out var mn, out var mx);
                var found = new List<Entity>();
                _roads.Query(mn - 20f, mx + 20f, found);
                var seen = new HashSet<Entity>();
                foreach (var e in found)
                {
                    if (!EntityManager.HasComponent<Node>(e) || !seen.Add(e) || !EntityManager.HasBuffer<ConnectedEdge>(e)) continue;
                    float half = 0f;
                    var edges = EntityManager.GetBuffer<ConnectedEdge>(e, true);
                    for (int i = 0; i < edges.Length; i++)
                        if (_roads.IsBoundaryEdge(edges[i].m_Edge)) half = math.max(half, _roads.RoadWidth(edges[i].m_Edge) * 0.5f);
                    if (half <= 0f) continue;
                    var p = EntityManager.GetComponentData<Node>(e).m_Position;
                    if (Poly.DistanceToBoundary(ring2, p.xz) > half + 3f) continue;
                    Composer.ClosestSegment(shape.Ring, p.xz, out int seg, out float st, out _);
                    shape.Attachments.Add(new BoundaryAttachment { Kind = AttachmentKind.RoadNode, ExternalId = EntityIds.Pack(e), Position = p, RingSegment = seg, RingT = st });
                }
            }
            catch (Exception ex) { Mod.Log.Warn("Placemaker: node attachments failed: " + ex.Message); }
        }
    }
}
