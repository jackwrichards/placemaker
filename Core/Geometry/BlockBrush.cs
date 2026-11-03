// Placemaker — "Block paint": a brush fenced to one city block (fill-aware brush).
//
// The player asked for a paint bucket's boundary with a brush's freedom: paint only inside the block
// under the cursor, never across a road, and keep clear of what is already there (zoned cells,
// building lots, other areas, water). The game layer hands in the block's curb outline and the
// obstacle polygons; this class
//   * rasterises the paintable cells once (Allowed: centre inside the block and outside every
//     obstacle) and fences the paint mask with it (BrushMask.Limit), so a stamp can never leave the
//     block even when the cursor drifts over the road;
//   * turns the painted cells into ONE simple outline: the lightly smoothed brush outline (BrushMask),
//     with every stretch that runs within SnapDistance of the curb or an obstacle pulled exactly onto
//     it (frontage / entrance logic must see the curb itself, not a ragged line 3 m inside it) by a
//     morphological closing of (paint ∪ forbidden), clipped exactly to (block − obstacles), largest
//     piece kept; an obstacle enclosed by paint is opened with a narrow corridor (CS2 areas cannot
//     have holes), plain unpainted gaps are filled like Paint.
// Pure geometry (Poly booleans/offsets); no engine types.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public sealed class BlockBrushOutline
    {
        /// <summary>Simple CCW ring (XZ), ≥ 3 points.</summary>
        public float2[] Ring;
        /// <summary>Pieces smaller than the kept one that an obstacle cut off (dropped).</summary>
        public int DroppedPieces;
        /// <summary>An obstacle was enclosed by paint and the outline was opened around it.</summary>
        public bool OpenedAroundObstacle;
        /// <summary>Part of the outline lies exactly on the block's curb.</summary>
        public bool TouchesCurb;
    }

    public sealed class BlockBrush
    {
        /// <summary>Corridor (m) cut from an enclosed obstacle to the outside so the outline needs no hole.</summary>
        public const float HoleCorridor = 1f;
        /// <summary>Points closer than this (m) to the curb count as "on the curb".</summary>
        public const float OnCurbTolerance = 0.05f;
        /// <summary>Moving-average passes for the painted outline (BrushMask.Outlines).</summary>
        public const int SmoothPasses = 1;

        private readonly List<float2[]> _obstacles = new List<float2[]>();
        private List<Region> _allowedRegions;

        /// <param name="block">Curb outline of the block (any orientation; cleaned to a simple CCW ring).</param>
        /// <param name="obstacles">Polygons nothing may be painted on (lots, zoned cells, areas). Only those touching the block are kept.</param>
        /// <param name="cellSize">Brush cell size; must match the paint mask.</param>
        /// <param name="blockedAt">Optional per-point veto evaluated at cell centres (e.g. water deeper than wading depth).</param>
        public BlockBrush(IReadOnlyList<float2> block, IEnumerable<IReadOnlyList<float2>> obstacles = null,
            float cellSize = BrushMask.DefaultCellSize, Func<float2, bool> blockedAt = null)
        {
            CellSize = cellSize > 0.01f && !float.IsNaN(cellSize) ? cellSize : BrushMask.DefaultCellSize;
            Block = block != null && block.Count >= 3 ? Poly.Clean(block, 0.05f) : Array.Empty<float2>();
            Allowed = new BrushMask(CellSize);
            if (Block.Length < 3) return;
            Poly.Bounds(Block, out var bmin, out var bmax);
            if (obstacles != null)
            {
                foreach (var o in obstacles)
                {
                    if (o == null || o.Count < 3) continue;
                    bool finite = true;
                    foreach (var p in o) if (!Poly.IsFinite(p)) { finite = false; break; }
                    if (!finite) continue;
                    Poly.Bounds(o, out var omin, out var omax);
                    if (omin.x > bmax.x || omin.y > bmax.y || omax.x < bmin.x || omax.y < bmin.y) continue;
                    var ring = new float2[o.Count];
                    for (int i = 0; i < ring.Length; i++) ring[i] = o[i];
                    if (Poly.Area(ring) < 0.01f) continue;
                    _obstacles.Add(Poly.EnsureCcw(ring));
                }
            }
            Allowed.PaintPolygon(Block);
            foreach (var o in _obstacles) Allowed.PaintPolygon(o, erase: true);
            if (blockedAt != null) Allowed.EraseWhere(blockedAt);
        }

        public float CellSize { get; }
        /// <summary>The block's curb outline (simple, CCW). Empty when the input was unusable.</summary>
        public float2[] Block { get; }
        /// <summary>Obstacles that touch the block (CCW).</summary>
        public IReadOnlyList<float2[]> Obstacles => _obstacles;
        /// <summary>Cells that may be painted (centre inside the block and outside every obstacle).</summary>
        public BrushMask Allowed { get; private set; }
        /// <summary>Open-land fallback (no closed block): Block is a square work box, roads are obstacles.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>Nothing in this block can be painted (all zoned / built / under water, or no block).</summary>
        public bool Full => !Unconstrained && Allowed.IsEmpty;
        /// <summary>Gap (m) between paint and the curb/an obstacle that is closed by pulling the edge onto it.</summary>
        private float _snap = -1f;
        /// <remarks>Default 1.5 cells (6 m); 0 = no snapping (paint edges are only clipped). Set before the first Outline.</remarks>
        public float SnapDistance
        {
            get => _snap >= 0f ? _snap : 1.5f * CellSize;
            set { _snap = float.IsNaN(value) ? -1f : math.clamp(value, 0f, 30f); _boxForbidden = null; _boxAllowed = null; }
        }
        /// <summary>Moving-average passes for the painted outline (0–6; Paint uses 6).</summary>
        public int Smoothing { get; set; } = SmoothPasses;
        /// <summary>Nothing to stop at (all brush rules off): behaves exactly like Paint — no fence, Paint's outline.</summary>
        public bool Unconstrained { get; private set; }

        public bool Contains(float2 p) => Block.Length >= 3 && Poly.Contains(Block, p);

        /// <summary>Half side (m) of the open-land work box around the first stroke.</summary>
        public const float OpenHalfSize = 300f;

        /// <summary>No rule left to stop at: an unfenced brush whose outline is exactly Paint's.</summary>
        public static BlockBrush Free(float2 start, float cellSize = BrushMask.DefaultCellSize) =>
            new BlockBrush(null, null, cellSize) { IsOpen = true, Unconstrained = true };

        /// <summary>
        /// Fallback where the cursor is not inside a closed block (open land, dead-end roads, unfinished
        /// grids): a square work box around <paramref name="centre"/> with the road footprints passed in as
        /// obstacles (with the lots, zoning, areas), restricted to the side of the roads the stroke starts
        /// on — the allowed cells 4-connected to <paramref name="centre"/>, so a stroke dragged across a
        /// road never paints the far side. <see cref="Full"/> when the centre itself cannot be painted.
        /// </summary>
        public static BlockBrush Open(float2 centre, IEnumerable<IReadOnlyList<float2>> obstacles,
            float cellSize = BrushMask.DefaultCellSize, Func<float2, bool> blockedAt = null, float halfSize = OpenHalfSize, bool keepSide = true)
        {
            halfSize = math.max(10f, halfSize);
            var box = new[] { centre + new float2(-halfSize, -halfSize), centre + new float2(halfSize, -halfSize),
                              centre + new float2(halfSize, halfSize), centre + new float2(-halfSize, halfSize) };
            var b = new BlockBrush(box, obstacles, cellSize, blockedAt) { IsOpen = true };
            if (keepSide) b.Allowed = b.Allowed.ConnectedComponent(centre);
            return b;
        }

        /// <summary>True if the cell under p may be painted.</summary>
        public bool CanPaintAt(float2 p) => Unconstrained || Allowed.IsPaintedAt(p);

        /// <summary>Fences a paint mask to this block (painting outside the allowed cells does nothing).</summary>
        public void Attach(BrushMask paint)
        {
            if (paint != null) paint.Limit = Unconstrained ? null : Allowed;
        }

        /// <summary>Removes painted cells outside the allowed cells (e.g. after the obstacles changed).</summary>
        public int Trim(BrushMask paint)
        {
            if (paint == null) return 0;
            return paint.EraseWhere(p => !Allowed.IsPaintedAt(p));
        }

        /// <summary>Block minus obstacles as exact polygons (lazy; the clip region of every outline).</summary>
        public IReadOnlyList<Region> AllowedRegions
        {
            get
            {
                if (_allowedRegions != null) return _allowedRegions;
                if (Block.Length < 3) return _allowedRegions = new List<Region>();
                try
                {
                    _allowedRegions = _obstacles.Count == 0
                        ? new List<Region> { new Region(Block) }
                        : Poly.Boolean(new IReadOnlyList<float2>[] { Block }, _obstacles, BoolOp.Difference);
                }
                catch (Exception) { _allowedRegions = new List<Region> { new Region(Block) }; }
                return _allowedRegions;
            }
        }

        /// <summary>
        /// (Area overhaul) The allowed regions inside a box only: the block clipped to the box minus the obstacles that touch
        /// it. The open-land brush holds hundreds of walls (every road piece in 600 m); an outline only needs those near the
        /// paint. Cached for the last box.
        /// </summary>
        public IReadOnlyList<Region> AllowedIn(float2 min, float2 max)
        {
            if (_allowedRegions != null) return _allowedRegions;
            if (_boxAllowed != null && math.all(min >= _boxMin) && math.all(max <= _boxMax)) return _boxAllowed;
            if (Block.Length < 3) return new List<Region>();
            var box = new[] { min, new float2(max.x, min.y), max, new float2(min.x, max.y) };
            var near = new List<IReadOnlyList<float2>>();
            foreach (var o in _obstacles)
            {
                Poly.Bounds(o, out var a, out var b);
                if (b.x < min.x || b.y < min.y || a.x > max.x || a.y > max.y) continue;
                near.Add(o);
            }
            List<Region> r;
            try
            {
                var clipped = Poly.Boolean(new IReadOnlyList<float2>[] { Block }, new IReadOnlyList<float2>[] { box }, BoolOp.Intersection);
                r = near.Count == 0 ? clipped : Poly.Boolean(clipped, near.ConvertAll(x => new Region(x as float2[] ?? new List<float2>(x).ToArray())), BoolOp.Difference);
            }
            catch (Exception) { r = AllowedRegions as List<Region> ?? new List<Region>(AllowedRegions); }
            _boxAllowed = r;
            _boxMin = min;
            _boxMax = max;
            _boxForbidden = null;
            return r;
        }

        private List<Region> _boxAllowed;
        private float2 _boxMin, _boxMax;
        private List<IReadOnlyList<float2>> _boxForbidden;

        /// <summary>
        /// Everything inside the box that may not be painted, as rings: the box minus the allowed regions in it (= outside
        /// the curb plus the obstacles inside the block).
        /// </summary>
        private List<IReadOnlyList<float2>> Forbidden(float2 mn, float2 mx, IReadOnlyList<Region> allowed)
        {
            if (_boxForbidden != null && ReferenceEquals(allowed, _boxAllowed)) return _boxForbidden;
            var list = new List<IReadOnlyList<float2>>();
            if (Block.Length < 3) return list;
            var box = new[] { mn, new float2(mx.x, mn.y), mx, new float2(mn.x, mx.y) };
            try
            {
                foreach (var r in Poly.Boolean(new[] { new Region(box) }, allowed, BoolOp.Difference))
                    foreach (var ring in r.Normalized().Rings()) list.Add(ring);
            }
            catch (Exception) { list.Clear(); }
            if (ReferenceEquals(allowed, _boxAllowed)) _boxForbidden = list;
            return list;
        }

        /// <summary>
        /// The painted area as one simple outline, or null when less than minArea m² is painted (or
        /// nothing usable remains inside the block).
        /// </summary>
        public BlockBrushOutline Outline(BrushMask paint, float minArea = 100f)
        {
            if (paint == null || paint.IsEmpty) return null;
            if (Unconstrained)
            {
                // Exactly Paint's outline (largest island, holes filled), with this brush's smoothing.
                var free = paint.Outlines(keepIslands: false, keepHoles: false, minIslandArea: minArea, smoothPasses: Smoothing);
                return free.Count == 0 ? null : new BlockBrushOutline { Ring = free[0].Outer };
            }
            if (Block.Length < 3) return null;
            // Light smoothing: Paint's 6 passes shrink a 30 m stroke by a third; the curb and obstacle
            // sides are made exact below anyway, so only the free edges need de-stairing.
            var smooth = paint.Outlines(keepIslands: false, keepHoles: false, minIslandArea: minArea, smoothPasses: Smoothing);
            if (smooth.Count == 0) return null;
            var painted = smooth[0].Outer;
            // Only the walls near the paint matter (snap reach around it); a box rounded out to 16 m so small strokes reuse it.
            Poly.Bounds(painted, out var pmin, out var pmax);
            float reach = 2f * SnapDistance + 2f * CellSize + 4f;
            float2 bmin = math.floor((pmin - reach) / 16f) * 16f, bmax = math.ceil((pmax + reach) / 16f) * 16f;
            var allowed = AllowedIn(bmin, bmax);
            if (allowed.Count == 0) return null;

            List<Region> pieces;
            try
            {
                // Pull edges that run close to the curb / an obstacle onto it: a morphological closing of
                // (paint ∪ forbidden) fills every gap narrower than SnapDistance between them without
                // growing the paint anywhere else (no lips along the curb, only small fillets in the
                // corners). Limited to the paint's neighbourhood so narrow unpainted strips elsewhere
                // (between two zoned rows, say) are not swept in. Then clip exactly to block − obstacles.
                float r = 0.5f * SnapDistance;
                if (r <= 0.01f) pieces = Poly.Boolean(new[] { new Region(painted) }, allowed, BoolOp.Intersection);   // snapping off
                else
                {
                    var rings = new List<IReadOnlyList<float2>> { painted };
                    rings.AddRange(Forbidden(bmin, bmax, allowed));
                    var joined = Poly.Boolean(rings, null, BoolOp.Union, FillRule.Positive);
                    var closed = Poly.Offset(Poly.Offset(joined, -r, OffsetJoin.Round, 4f, 0.2f), r, OffsetJoin.Round, 4f, 0.2f);
                    var near = Poly.Offset(new Region(painted), -(SnapDistance + CellSize), OffsetJoin.Round, 4f, 0.5f);
                    var inside = Poly.Boolean(closed, allowed, BoolOp.Intersection);
                    pieces = near.Count > 0 ? Poly.Boolean(inside, near, BoolOp.Intersection) : inside;
                }
            }
            catch (Exception)
            {
                return null;
            }
            pieces.RemoveAll(p => p == null || p.Outer == null || p.Outer.Length < 3 || p.Area < 1f);
            if (pieces.Count == 0) return null;
            pieces.Sort((a, b) => b.Area.CompareTo(a.Area));
            var kept = pieces[0];
            if (kept.Area < minArea) return null;

            var result = new BlockBrushOutline();
            for (int i = 1; i < pieces.Count; i++) if (pieces[i].Area >= 0.5f * CellSize * CellSize) result.DroppedPieces++;

            float2[] outer = kept.Outer;
            if (kept.HasHoles)
            {
                // Only obstacles leave holes here (painted gaps were filled above): open them with a corridor.
                result.OpenedAroundObstacle = true;
                List<float2[]> cut;
                try { cut = Poly.CutHoles(kept, HoleCorridor); }
                catch (Exception) { cut = new List<float2[]>(); }
                float best = -1f;
                outer = null;
                foreach (var c in cut) { float a = Poly.Area(c); if (a > best) { best = a; outer = c; } }
                if (outer == null) return null;
            }

            // Round joins leave vertex pairs a few dm apart: thin them (Douglas–Peucker keeps original
            // vertices, so points on the curb stay exactly on it) and drop edges CS2 would refuse.
            var thin = Poly.Simplify(outer, 0.08f);
            if (thin.Length >= 3 && Poly.IsSimple(thin)) outer = thin;
            outer = Poly.Clean(outer, SurfaceAcceptance.MinEdge + 0.05f);
            if (outer.Length < 3) return null;
            outer = Poly.EnsureCcw(Poly.RemoveCollinear(outer, 1e-4f));
            if (outer.Length < 3 || !Poly.IsSimple(outer) || Poly.Area(outer) < minArea) return null;

            foreach (var p in outer)
                if (Poly.DistanceToBoundary(Block, p) <= OnCurbTolerance) { result.TouchesCurb = true; break; }
            result.Ring = outer;
            return result;
        }
    }
}
