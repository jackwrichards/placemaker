// Placemaker roads layer — grid and loop/superblock patterns, shared clipping and node merging.
// Adapted from Grid Road Generator (Ti4goc), Core/GridGenerator.cs; part of Placemaker (GPL-3.0).
// Changes for Placemaker: no static caches or global lock (thread-safe, reentrant), cooperative
// cancellation through GridParameters.Cancel, per-call diagnostics objects instead of statics.
// The "merge with the nearest accepted node" rule follows the "super node" idea of CS2-NetworkTools
// (Luca Rager, MIT).
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Generators
{
    /// <summary>
    /// A road segment to create: a start point and an end point. Coordinates are in world space (X, Z;
    /// Y = terrain height, handled elsewhere).
    /// </summary>
    public struct RoadSegmentDef
    {
        public float3 Start;
        public float3 End;
        public bool IsHorizontal; // "horizontal" = parallel to the grid's main axis
        /// <summary>
        /// True if End is the free end of a cul-de-sac (CulDeSacMode): the game layer places a
        /// turning-circle object there once the segment is created.
        /// </summary>
        public bool IsCulDeSacEnd;
        /// <summary>
        /// True for a segment of the column/row chosen as the avenue (GridParameters.AvenueColumnIndex/
        /// AvenueRowIndex): runs through like a normal collector (never a cul-de-sac on an avenue), but
        /// uses a dedicated third prefab.
        /// </summary>
        public bool IsAvenue;
        /// <summary>
        /// True for a segment of a superblock's pedestrian interior (GridParameters.SuperblockMode, see
        /// GenerateLoopGrid/GenerateSuperblockInterior): reuses the third prefab slot ("secondary",
        /// otherwise unused in Loop mode since the Arterial level that held it was removed) for a
        /// dedicated pedestrian network instead of the usual drivable loop/cul-de-sac. Never true
        /// together with IsAvenue.
        /// </summary>
        public bool IsPedestrian;
        /// <summary>
        /// True for a CURVED segment (Loop mode, see GridGenerator.GenerateLoopGrid): a real curve
        /// (fitted by the game from StartTangent/EndTangent) rather than a straight line. Never true
        /// for a collector segment or a cul-de-sac spur (always straight). StartTangent/EndTangent are
        /// only meaningful when IsArc is true.
        /// </summary>
        public bool IsArc;
        /// <summary>
        /// True for a segment of a cul-de-sac street that is not its last one (Organic pattern): same
        /// cul-de-sac network as the end (IsCulDeSacEnd) but without a turning circle, so the whole
        /// cul-de-sac stays on a single network.
        /// </summary>
        public bool IsLocal;
        /// <summary>
        /// Cul-de-sac end (IsCulDeSacEnd, turning circle) that nevertheless keeps its street's network
        /// instead of switching to the cul-de-sac network (Contour pattern: a level street that ends in
        /// a cul-de-sac is still a level street).
        /// </summary>
        public bool KeepNetwork;
        /// <summary>
        /// Segment of the Radial pattern's central roundabout ring: its own network, chosen in the
        /// Roundabout tab. Always emitted counter-clockwise (right-hand traffic); the game layer
        /// reverses it for left-hand traffic.
        /// </summary>
        public bool IsRoundabout;
        public float3 StartTangent;
        public float3 EndTangent;

        public RoadSegmentDef(float3 start, float3 end, bool isHorizontal, bool isCulDeSacEnd = false, bool isAvenue = false)
        {
            Start = start;
            End = end;
            IsHorizontal = isHorizontal;
            IsCulDeSacEnd = isCulDeSacEnd;
            IsAvenue = isAvenue;
            IsPedestrian = false;
            IsArc = false;
            IsLocal = false;
            KeepNetwork = false;
            IsRoundabout = false;
            StartTangent = default;
            EndTangent = default;
        }

        /// <summary>Curved piece (see IsArc): unit tangents, both pointing in the direction of travel.</summary>
        public static RoadSegmentDef Arc(float3 start, float3 end, float3 startTangent, float3 endTangent, bool isCulDeSacEnd = false)
        {
            var def = new RoadSegmentDef(start, end, isHorizontal: false, isCulDeSacEnd: isCulDeSacEnd);
            def.IsArc = true;
            def.StartTangent = startTangent;
            def.EndTangent = endTangent;
            return def;
        }
    }

    /// <summary>
    /// Roundabout at the crossing of two avenues (column AND row enabled, see ComputeAvenueRoundabout):
    /// Center/Radius are only used by the game layer to pick and place the complete decorative
    /// roundabout asset. No road segment is generated for the ring itself: the avenue arms cross
    /// straight through (+ intersection), the same way a cul-de-sac keeps its plain end under the
    /// decorative turning circle.
    /// </summary>
    public struct RoundaboutInfo
    {
        public bool HasRoundabout;
        public float3 Center;
        public float Radius;
    }

    public enum SpacingMode
    {
        /// <summary>Spreads rows/columns evenly to fill the whole area.</summary>
        FitToArea,
        /// <summary>Fixed spacing (meters); the number of roads is computed automatically.</summary>
        FixedSpacing
    }

    /// <summary>Which grid axis or axes can become cul-de-sacs in CulDeSacMode.</summary>
    public enum CulDeSacAxis
    {
        /// <summary>Columns only (axisIsU), perpendicular to the main axis: the original behavior.</summary>
        Columns = 0,
        /// <summary>Rows only (axisIsV), parallel to the main axis.</summary>
        Rows = 1,
        /// <summary>Both axes, each with its own independent pattern.</summary>
        Both = 2
    }

    public struct GridParameters
    {
        public SpacingMode Mode;
        public int Rows;
        public int Columns;
        public float SpacingMeters;
        /// <summary>Extra rotation (degrees, -90..+90) of the grid relative to the polygon's longest edge.</summary>
        public float AngleOffsetDegrees;
        /// <summary>
        /// Grid aligned to the terrain (Grid and Loop): one axis follows the area's dominant contour
        /// lines (requires HeightAt); AngleOffsetDegrees is added on top.
        /// </summary>
        public bool AlignToTerrain;
        /// <summary>Pedestrian links from cul-de-sac ends to the neighboring street (see AddPedestrianLinks).</summary>
        public bool PedestrianLinks;
        /// <summary>"Mixed" pattern: Radial in a central circle, Organic around it (see GenerateMixed).</summary>
        public bool MixedMode;
        /// <summary>Radius (m) of the Mixed pattern's central circle.</summary>
        public float MixedCoreRadius;

        /// <summary>
        /// Suburban mode: some lines become cul-de-sacs instead of through collectors, according to
        /// CulDeSacAxis.
        /// </summary>
        public bool CulDeSacMode;
        /// <summary>Which axis or axes can become cul-de-sacs (see CulDeSacMode).</summary>
        public CulDeSacAxis CulDeSacAxis;
        /// <summary>Cul-de-sac depth (0.5–0.9), as a fraction of the actual (clipped) length of the segment between two collectors.</summary>
        public float CulDeSacDepth;
        /// <summary>Alternates the cul-de-sac origin between the upper and lower collector from one block to the next.</summary>
        public bool Staggered;
        /// <summary>Cul-de-sac frequency (0–100 %): deterministic "one in N" pattern.</summary>
        public float CulDeSacRatio;

        /// <summary>
        /// Avenue (classic grid only, see GenerateGrid/EmitLine): the column at AvenueColumnIndex
        /// (among the u lines actually generated, 0-based) uses a dedicated third prefab instead of the
        /// main network and runs across the whole perimeter like a normal collector (never a cul-de-sac
        /// on an avenue). An out-of-range index silently has no effect (no line matches), never an
        /// error.
        /// </summary>
        public bool AvenueColumnEnabled;
        public int AvenueColumnIndex;
        /// <summary>Same as AvenueColumnEnabled/AvenueColumnIndex, for a row (v line).</summary>
        public bool AvenueRowEnabled;
        public int AvenueRowIndex;

        /// <summary>
        /// "Loop" mode (see GenerateLoopGrid): spacing (m) of the SPARSE collectors that bound the
        /// large blocks, independent of SpacingMeters/Rows/Columns (which belong to GenerateGrid).
        /// Usually much larger than a classic grid spacing: each block then gets an inner loop (see
        /// LoopCulDeSacRatio).
        /// </summary>
        public float CollectorSpacingMeters;
        /// <summary>Frequency (0–100 %) at which a loop gets a cul-de-sac spur toward its center (see GenerateLoopGrid).</summary>
        public float LoopCulDeSacRatio;
        /// <summary>
        /// "Superblock" mode (Loop mode only, inspired by Barcelona's superilles): the selection is cut
        /// into zones of about SuperblockZoneMeters, separated by PEDESTRIAN streets
        /// (RoadSegmentDef.IsPedestrian); one line in SuperblockSubdivisions is a collector, bounding
        /// superblocks of 3×3 zones. Never any cul-de-sac. See GenerateLoopGrid.
        /// </summary>
        public bool SuperblockMode;
        /// <summary>
        /// Target size (m) of a buildable zone in superblock mode, adjusted so the zones fill the
        /// selection exactly (user feedback: "change the scale of the squares, smaller, with a minimum
        /// of about 100 m").
        /// </summary>
        public float SuperblockZoneMeters;
        /// <summary>
        /// "Concentric" pattern (Loop mode only, exclusive with SuperblockMode): rings that follow the
        /// perimeter's shape, see ConcentricGenerator.
        /// </summary>
        public bool ConcentricMode;
        /// <summary>Requested number of inner rings (ConcentricMode); fewer if the shape is too narrow.</summary>
        public int ConcentricLayers;
        /// <summary>Number of spokes started on each innermost ring (ConcentricMode).</summary>
        public int ConcentricConnections;
        /// <summary>
        /// "Radial" pattern (with ConcentricMode): a roundabout in the center and RadialAvenues
        /// straight avenues out to the perimeter, without the concentric rings (see
        /// ConcentricGenerator.GenerateRadial).
        /// </summary>
        public bool RadialMode;
        /// <summary>Number of straight avenues in the Radial pattern.</summary>
        public int RadialAvenues;
        /// <summary>Requested radius (m) of the Radial pattern's central roundabout (adjusted to the shape).</summary>
        public float RadialRoundaboutRadius;
        /// <summary>Number of circular rings around the Radial pattern's roundabout (0 = none).</summary>
        public int RadialLayers;
        /// <summary>
        /// "Cul-de-sac tree" pattern (Grid family, LoopMode false): a collector along the long axis,
        /// perpendicular branches, cul-de-sacs in pairs; see GridGenerator.GenerateTree.
        /// </summary>
        public bool TreeMode;
        /// <summary>Distance (m) between two branches on the same side of the collector.</summary>
        public float TreeBranchSpacing;
        /// <summary>Distance (m) between two pairs of cul-de-sacs along a branch.</summary>
        public float TreeCulDeSacSpacing;
        /// <summary>Target cul-de-sac length (m), reduced to fit between two branches and away from the perimeter.</summary>
        public float TreeCulDeSacLength;
        /// <summary>
        /// "Organic" pattern (Grid family, LoopMode false): a subdivision with winding streets and
        /// cul-de-sacs; see GridGenerator.GenerateOrganic.
        /// </summary>
        public bool OrganicMode;
        /// <summary>Target distance (m) between two neighboring streets (lots on both sides).</summary>
        public float OrganicStreetSpacing;
        /// <summary>Street curviness (0–100 %).</summary>
        public float OrganicCurviness;
        /// <summary>Share (0–100 %) of branches that, on reaching another street, join it as a loop instead of ending in a cul-de-sac.</summary>
        public float OrganicLoopShare;
        /// <summary>Random variant (the same value gives the same result).</summary>
        public int OrganicSeed;
        /// <summary>
        /// "Contour" pattern (Grid family, LoopMode false): streets along the terrain's contour lines,
        /// linked by slope-limited climbs; see GridGenerator.GenerateContour.
        /// </summary>
        public bool ContourMode;
        /// <summary>Target plan distance (m) between two neighboring level streets.</summary>
        public float ContourSpacing;
        /// <summary>Target distance (m) between two climbs along a level street.</summary>
        public float ContourConnectorSpacing;
        /// <summary>
        /// Terrain height at world point (x, z). Supplied by the caller (the game layer: real terrain;
        /// tests: synthetic relief) and never saved in the settings. Without it, the Contour pattern
        /// generates nothing.
        /// </summary>
        public Func<float2, float> HeightAt;
        /// <summary>Cancellation of a running generation (never saved). Checked between phases and in long loops.</summary>
        public CancellationToken Cancel;
        /// <summary>
        /// (s34) "Follow contours": winding patterns (organic, tree) steer their streets along the contour lines and keep
        /// climbs under <see cref="MaxGrade"/>; straight patterns are aligned by the caller (AlignToTerrain) and the planner
        /// bends them. False = the original GRG behaviour (golden replay).
        /// </summary>
        public bool FollowContours;
        /// <summary>(s34) Steepest grade (rise/run) the terrain-following patterns aim for; 0 = 10 %.</summary>
        public float MaxGrade;
        /// <summary>(s34) Organic: smallest distance (m) between side junctions on the main street; 0 = the original 45 m.</summary>
        public float MainJunctionSpacing;
        /// <summary>
        /// (Smarter roads) Grid frame chosen by the planner (RoadLayoutSearch): origin and u axis in world XZ. When set it
        /// replaces the longest-edge / terrain frame and AngleOffsetDegrees (the search folds the player's angle in).
        /// </summary>
        public bool UseFrame;
        public float2 FrameOrigin;
        public float2 FrameU;
        /// <summary>(Smarter roads) Fixed spacing: a column (u line) passes through local u = AnchorU (an existing street it continues).</summary>
        public bool AnchoredU;
        public float AnchorU;
        /// <summary>(Smarter roads) Fixed spacing: a row (v line) passes through local v = AnchorV (a spine, an existing street).</summary>
        public bool AnchoredV;
        public float AnchorV;
        /// <summary>(Smarter roads) Spacing of the columns / rows (m), 0 = SpacingMeters (the search may stretch it a little).</summary>
        public float SpacingU, SpacingV;
        /// <summary>
        /// (Smarter roads) No border road: the outermost columns / rows run this far (m) inside the outline (one lot deep), the
        /// rest spread evenly between them, so nothing runs out to the edge looking for a border. 0 = the usual spread.
        /// </summary>
        public float EdgeInset;
        public static GridParameters Default => new GridParameters
        {
            Mode = SpacingMode.FitToArea,
            Rows = 3,
            Columns = 3,
            SpacingMeters = 100f,
            AngleOffsetDegrees = 0f,
            CulDeSacMode = false,
            CulDeSacAxis = CulDeSacAxis.Columns,
            CulDeSacDepth = 0.6f,
            Staggered = true,
            CulDeSacRatio = 100f,
            AvenueColumnEnabled = false,
            AvenueColumnIndex = 0,
            AvenueRowEnabled = false,
            AvenueRowIndex = 0,
            CollectorSpacingMeters = 150f,
            LoopCulDeSacRatio = 50f,
        };
    }

    /// <summary>
    /// Generates a road grid inside an ARBITRARY perimeter (convex, concave, rotated or irregular
    /// polygon) defined by the selected nodes in click order.
    ///
    /// Algorithm:
    ///  1. The polygon is closed from the points in selection order.
    ///  2. A local frame (u, v) is built, aligned with the polygon's longest edge,
    ///     so the grid follows the perimeter's "main street" rather than the world
    ///     axes. AngleOffsetDegrees adds an extra rotation (u and v turn together).
    ///  3. Grid lines u = const and v = const are generated within the local
    ///     bounding box, according to the spacing mode.
    ///  4. Each line is clipped to the polygon with the even-odd rule: intersections
    ///     with the edges are sorted along the line and paired up. A concave polygon
    ///     therefore naturally yields several sub-segments per line.
    ///  5. Interior crossings between u and v lines are precomputed, and each line
    ///     is emitted as sub-segments between consecutive crossings. A crossing's
    ///     world point is computed ONCE and shared (same floats) by the sub-segment
    ///     ends of both lines, so the game merges those ends into one intersection node.
    ///  6. Segments too short to be a road are discarded.
    ///
    /// Special case: with exactly 2 nodes, they are treated as opposite corners of an axis-aligned
    /// rectangle (simple, predictable behavior).
    /// </summary>
    public static partial class GridGenerator
    {
        /// <summary>Minimum length of a generated segment, in meters (below this it is not a viable road).</summary>
        public const float MinSegmentLength = 8f;

        /// <summary>
        /// Minimum distance (m) between two distinct generated nodes (crossings). Below it, the later
        /// crossing (scan order: columns, then rows) is MERGED into the nearest already-accepted
        /// crossing (like NetworkTools' "super node", see BuildSubSegments): the two lines crossing
        /// there join that existing node instead of creating a separate, nearly coincident
        /// intersection. No node is lost; grid lines are just reconnected to a neighboring node that is
        /// already there. Cul-de-sacs too close to their target collector are still simply dropped
        /// (EmitLine, a different case: there is no existing node to merge into at that spot).
        /// </summary>
        public const float MinNodeDistance = 8f;

        private const float Epsilon = 1e-4f;

        /// <summary>
        /// Tolerance (m) for snapping a crossing to a clipped interval: a crossing within this distance
        /// of an interval end is merged with it (T-junction at the polygon edge) instead of creating a
        /// micro-segment.
        /// </summary>
        private const float JoinTolerance = 0.05f;

        /// <summary>
        /// Spatial bucket-grid index (cell size = MinNodeDistance) for finding the nearest
        /// already-accepted point WITHOUT scanning the whole list. User feedback (mod forum): with
        /// small spacing over a large area the number of crossings explodes, and BuildSubSegments used
        /// to scan ALL accepted points for EVERY new crossing (inside a double loop that is already O(u
        /// lines × v lines)): a true O(n²) inside an O(n²) function, so O(n⁴) in practice, re-run on
        /// EVERY preview frame in the original mod. Result: the game froze with small spacing on a
        /// large perimeter. Only the neighboring 3×3 buckets are checked: with bucket size =
        /// MinNodeDistance, no point closer than MinNodeDistance can lie beyond a direct neighbor
        /// bucket.
        /// </summary>
        private sealed class SpatialPointIndex
        {
            private readonly float _cellSize;
            private readonly Dictionary<(int, int), List<float3>> _buckets = new Dictionary<(int, int), List<float3>>();

            public SpatialPointIndex(float cellSize)
            {
                _cellSize = math.max(cellSize, 0.01f);
            }

            private (int, int) CellOf(float2 xz) => ((int)math.floor(xz.x / _cellSize), (int)math.floor(xz.y / _cellSize));

            public void Add(float3 point)
            {
                var cell = CellOf(point.xz);
                if (!_buckets.TryGetValue(cell, out List<float3> list))
                {
                    list = new List<float3>();
                    _buckets[cell] = list;
                }
                list.Add(point);
            }

            /// <summary>Nearest accepted point within maxDistance, if there is one.</summary>
            public bool TryFindNearest(float3 point, float maxDistance, out float3 nearest)
            {
                (int cx, int cy) = CellOf(point.xz);
                float bestDist = float.MaxValue;
                nearest = default;
                bool found = false;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!_buckets.TryGetValue((cx + dx, cy + dy), out List<float3> list))
                            continue;
                        foreach (float3 candidate in list)
                        {
                            float d = math.distance(candidate.xz, point.xz);
                            if (d < maxDistance && d < bestDist)
                            {
                                bestDist = d;
                                nearest = candidate;
                                found = true;
                            }
                        }
                    }
                }
                return found;
            }
        }

        /// <summary>Clipped grid line: its position on the transverse axis and its interior intervals.</summary>
        private struct GridLine
        {
            public float Position;
            /// <summary>Sorted [x, y] intervals along the line that lie inside the polygon.</summary>
            public List<float2> Intervals;
        }

        /// <summary>Convenience overload when neither the omission diagnostic nor the roundabout is needed (e.g. existing tests).</summary>
        public static List<RoadSegmentDef> GenerateGrid(IReadOnlyList<float3> selectedNodePositions, GridParameters parameters)
            => GenerateGrid(selectedNodePositions, parameters, out _, out _);

        /// <summary>Convenience overload when only the omission diagnostic is needed (e.g. existing tests).</summary>
        public static List<RoadSegmentDef> GenerateGrid(IReadOnlyList<float3> selectedNodePositions,
            GridParameters parameters, out int omittedNodeCount)
            => GenerateGrid(selectedNodePositions, parameters, out omittedNodeCount, out _);

        /// <summary>
        /// Generates the grid. omittedNodeCount counts, combined: crossings MERGED into an
        /// already-accepted generated node that was too close (MinNodeDistance, see BuildSubSegments;
        /// the node itself is kept, only merged) and cul-de-sac stubs simply dropped because there was
        /// no existing node to merge into (EmitLine); 0 if neither. roundabout: see
        /// RoundaboutInfo/ComputeAvenueRoundabout (HasRoundabout=false unless both avenue axes are
        /// active at once, or if their crossing falls outside the polygon).
        /// </summary>
        public static List<RoadSegmentDef> GenerateGrid(IReadOnlyList<float3> selectedNodePositions,
            GridParameters parameters, out int omittedNodeCount, out RoundaboutInfo roundabout)
            => GenerateGridCore(selectedNodePositions, parameters, out omittedNodeCount, out roundabout);

        private static List<RoadSegmentDef> GenerateGridCore(IReadOnlyList<float3> selectedNodePositions,
            GridParameters parameters, out int omittedNodeCount, out RoundaboutInfo roundabout)
        {
            roundabout = default;
            omittedNodeCount = 0;
            if (selectedNodePositions == null || selectedNodePositions.Count < 2)
                throw new ArgumentException("At least 2 selected nodes are required.");

            // Average height; reprojected onto the terrain at placement.
            float y = 0f;
            foreach (var p in selectedNodePositions) y += p.y;
            y /= selectedNodePositions.Count;

            // 2D projection (X, Z)
            var pts = new List<float2>(selectedNodePositions.Count);
            foreach (var p in selectedNodePositions) pts.Add(new float2(p.x, p.z));

            // 2 nodes: axis-aligned rectangle, opposite corners.
            if (pts.Count == 2)
            {
                float2 mn = math.min(pts[0], pts[1]);
                float2 mx = math.max(pts[0], pts[1]);
                pts = new List<float2> { mn, new float2(mx.x, mn.y), mx, new float2(mn.x, mx.y) };
            }

            var polygon = RemoveConsecutiveDuplicates(pts);
            if (polygon.Count < 3 || math.abs(SignedArea(polygon)) < 1f)
                return new List<RoadSegmentDef>(); // degenerate polygon (collinear/coincident points)

            if (parameters.MixedMode)
            {
                return WithPedestrianLinks(GenerateMixed(polygon, y, parameters), polygon, y, parameters);
            }
            if (parameters.TreeMode)
            {
                return WithPedestrianLinks(GenerateTree(polygon, y, parameters), polygon, y, parameters);
            }
            if (parameters.OrganicMode)
            {
                return WithPedestrianLinks(GenerateOrganic(polygon, y, parameters), polygon, y, parameters);
            }
            if (parameters.ContourMode)
            {
                return GenerateContour(polygon, y, parameters);
            }

            LinePositions(polygon, parameters, out float2 origin, out float2 uDir, out float2 vDir, out var local, out var uPositions, out var vPositions);

            // Clip each line to the polygon (interior intervals).
            var uLines = new List<GridLine>(uPositions.Count);
            foreach (var u in uPositions)
                uLines.Add(new GridLine { Position = u, Intervals = ClipLineToPolygon(local, axisIsU: true, position: u) });
            var vLines = new List<GridLine>(vPositions.Count);
            foreach (var v in vPositions)
                vLines.Add(new GridLine { Position = v, Intervals = ClipLineToPolygon(local, axisIsU: false, position: v) });

            List<RoadSegmentDef> grid = BuildSubSegments(uLines, vLines, origin, uDir, vDir, y, parameters, out omittedNodeCount, out roundabout);
            // Streets that meet the perimeter nearly parallel to it (acute angle), or very short ends.
            CleanPerimeterEnds(grid, selectedNodePositions, s => !s.IsCulDeSacEnd);
            return WithPedestrianLinks(grid, polygon, y, parameters);
        }

        // ------------------------------------------------------------------
        // Pre-splitting at interior crossings
        // ------------------------------------------------------------------

        /// <summary>
        /// Computes the crossings between u and v lines (in local coordinates, line u = x crosses line
        /// v = z at (x, z), kept only if it lies within the clipped intervals of BOTH lines), then
        /// emits each line as sub-segments between consecutive crossings. Each crossing's world float3
        /// is computed once and reused as is by both lines: the meeting ends are bit-identical, which
        /// is required for them to merge into a single node.
        ///
        /// A new crossing within MinNodeDistance of an ALREADY accepted crossing (necessarily on a
        /// different line pair: two crossings on the same pair would be the same point) is MERGED into
        /// that nearest existing point (like NetworkTools' "super node") rather than simply dropped.
        /// Both its lines (u and v) join the already-accepted node, which is never moved toward a
        /// barycenter (its position stays fixed, so splits already emitted for it remain valid as they
        /// are). The scan goes columns then rows, so it is deterministic: the later crossing always
        /// yields to the first accepted one.
        /// </summary>
        private static List<RoadSegmentDef> BuildSubSegments(List<GridLine> uLines, List<GridLine> vLines,
            float2 origin, float2 uDir, float2 vDir, float y, GridParameters parameters, out int omittedNodeCount,
            out RoundaboutInfo roundabout)
        {
            roundabout = default;
            var uSplits = new List<(float t, float3 world)>[uLines.Count];
            var vSplits = new List<(float t, float3 world)>[vLines.Count];
            for (int i = 0; i < uLines.Count; i++) uSplits[i] = new List<(float, float3)>();
            for (int i = 0; i < vLines.Count; i++) vSplits[i] = new List<(float, float3)>();

            var acceptedWorldPoints = new SpatialPointIndex(MinNodeDistance);
            // Counts grid crossings MERGED into an already-accepted node (below) AND real cul-de-sacs
            // dropped later (EmitLine, a different case: a stub ending almost on its collector is
            // simply removed, not merged, since there is nothing existing to merge into at that exact
            // spot). A single combined counter for both, as before merging was introduced; only the
            // first category is now a real merge (node kept, segments reconnected) rather than a loss.
            omittedNodeCount = 0;

            for (int iu = 0; iu < uLines.Count; iu++)
            {
                for (int iv = 0; iv < vLines.Count; iv++)
                {
                    float u = uLines[iu].Position;
                    float v = vLines[iv].Position;
                    if (!ContainsPosition(uLines[iu].Intervals, v) || !ContainsPosition(vLines[iv].Intervals, u))
                        continue;

                    float3 world = ToWorld(origin, uDir, vDir, u, v, y);

                    // Merge into the nearest ALREADY accepted point (like NetworkTools' "super node")
                    // instead of dropping this crossing: both lines (u and v) of THIS crossing then
                    // join that existing point instead of creating a separate, nearly coincident node.
                    // The accepted point keeps a FIXED position (never moved to a barycenter): split
                    // entries already emitted for earlier crossings reference it as is, and recomputing
                    // them retroactively would add complexity for no visible benefit (the offset is by
                    // definition < MinNodeDistance).
                    bool merged = acceptedWorldPoints.TryFindNearest(world, MinNodeDistance, out float3 mergeTarget);

                    if (merged)
                    {
                        omittedNodeCount++;
                        uSplits[iu].Add((v, mergeTarget));
                        vSplits[iv].Add((u, mergeTarget));
                        continue; // does not itself become a new distinct accepted point
                    }

                    acceptedWorldPoints.Add(world);
                    uSplits[iu].Add((v, world));
                    vSplits[iv].Add((u, world));
                }
            }

            var segments = new List<RoadSegmentDef>();
            // Cul-de-sacs: EmitLine decides line by line whether the mode applies via
            // AppliesToAxis(parameters.CulDeSacAxis, axisIsU): columns, rows, or both. isAvenueLine:
            // whether line i is the chosen avenue on this axis; see EmitLine (never a cul-de-sac on an
            // avenue, dedicated prefab in the game layer).
            for (int i = 0; i < uLines.Count; i++)
            {
                bool isAvenueLine = parameters.AvenueColumnEnabled && i == parameters.AvenueColumnIndex;
                EmitLine(segments, uLines[i], uSplits[i], axisIsU: true, origin, uDir, vDir, y, parameters, ref omittedNodeCount, isAvenueLine);
            }
            for (int i = 0; i < vLines.Count; i++)
            {
                bool isAvenueLine = parameters.AvenueRowEnabled && i == parameters.AvenueRowIndex;
                EmitLine(segments, vLines[i], vSplits[i], axisIsU: false, origin, uDir, vDir, y, parameters, ref omittedNodeCount, isAvenueLine);
            }

            // Roundabout at the crossing of two avenues (column AND row enabled), see
            // ComputeAvenueRoundabout: the avenue arms cross straight through (+ intersection) and the
            // game layer places a complete decorative asset on top. No effect if only one avenue is
            // enabled (a single avenue just runs across the whole perimeter like a normal collector).
            if (parameters.AvenueColumnEnabled && parameters.AvenueRowEnabled)
            {
                roundabout = ComputeAvenueRoundabout(uLines, vLines, origin, uDir, vDir, y, parameters);
            }

            return segments;
        }

        /// <summary>
        /// The grid's frame and line positions for a polygon (world XZ, consecutive duplicates removed): the planner's frame
        /// (UseFrame) or the longest edge / terrain one with the angle offset; columns and rows evenly spread, or (fixed
        /// spacing with an anchor) through the anchor at the column / row spacing. Shared with RoadLayoutSearch.
        /// </summary>
        internal static void LinePositions(List<float2> polygon, GridParameters parameters, out float2 origin, out float2 uDir, out float2 vDir,
            out List<float2> local, out List<float> uPositions, out List<float> vPositions)
        {
            if (parameters.UseFrame && math.lengthsq(parameters.FrameU) > 1e-6f)
            {
                origin = parameters.FrameOrigin;
                uDir = math.normalize(parameters.FrameU);
                vDir = new float2(-uDir.y, uDir.x);
            }
            else
            {
                // Local frame aligned with the longest edge, plus the adjustable angle.
                (origin, uDir, vDir) = BuildLocalFrame(polygon, parameters.AngleOffsetDegrees, parameters.AlignToTerrain ? parameters.HeightAt : null);
            }
            // Polygon in local coordinates + local bounding box.
            local = new List<float2>(polygon.Count);
            float2 lmin = new float2(float.MaxValue), lmax = new float2(float.MinValue);
            foreach (var p in polygon)
            {
                var lp = new float2(math.dot(p - origin, uDir), math.dot(p - origin, vDir));
                local.Add(lp);
                lmin = math.min(lmin, lp);
                lmax = math.max(lmax, lp);
            }
            // Grid line positions in the local frame.
            if (parameters.Mode == SpacingMode.FitToArea)
            {
                uPositions = DistributeEvenly(lmin.x, lmax.x, parameters.Columns);
                vPositions = DistributeEvenly(lmin.y, lmax.y, parameters.Rows);
            }
            else
            {
                float su = parameters.SpacingU > 0.5f ? parameters.SpacingU : parameters.SpacingMeters;
                float sv = parameters.SpacingV > 0.5f ? parameters.SpacingV : parameters.SpacingMeters;
                uPositions = parameters.AnchoredU ? DistributeAnchored(lmin.x, lmax.x, su, parameters.AnchorU, parameters.EdgeInset)
                    : parameters.EdgeInset > 0.5f ? DistributeInset(lmin.x, lmax.x, su, parameters.EdgeInset) : DistributeFixed(lmin.x, lmax.x, su);
                vPositions = parameters.AnchoredV ? DistributeAnchored(lmin.y, lmax.y, sv, parameters.AnchorV, parameters.EdgeInset)
                    : parameters.EdgeInset > 0.5f ? DistributeInset(lmin.y, lmax.y, sv, parameters.EdgeInset) : DistributeFixed(lmin.y, lmax.y, sv);
            }
        }

        /// <summary>The default frame (longest edge or terrain, with the angle offset) of a world ring: origin and u axis.</summary>
        internal static (float2 origin, float2 u) DefaultFrame(IReadOnlyList<float3> ring, GridParameters parameters)
        {
            var pts = new List<float2>(ring.Count);
            foreach (var p in ring) pts.Add(p.xz);
            var polygon = RemoveConsecutiveDuplicates(pts);
            if (polygon.Count < 3) return (float2.zero, new float2(1f, 0f));
            var f = BuildLocalFrame(polygon, parameters.AngleOffsetDegrees, parameters.AlignToTerrain ? parameters.HeightAt : null);
            return (f.origin, f.uDir);
        }

        /// <summary>
        /// (Smarter roads) The first and last line <paramref name="inset"/> m inside [min, max], the others evenly between them about
        /// <paramref name="spacing"/> apart; a span too short for two lines gets one in the middle.
        /// </summary>
        internal static List<float> DistributeInset(float min, float max, float spacing, float inset)
        {
            var result = new List<float>();
            float span = max - min - 2f * inset;
            if (spacing <= 0.5f || max - min < 2f * MinSegmentLength) return result;
            if (span < 0.45f * spacing)
            {
                if (max - min >= inset) result.Add(0.5f * (min + max));
                return result;
            }
            int n = math.max(1, (int)math.round(span / spacing));
            for (int k = 0; k <= n; k++) result.Add(min + inset + span * k / n);
            return result;
        }

        /// <summary>
        /// (Smarter roads) Lines through <paramref name="anchor"/> every <paramref name="spacing"/> m, those at least 0.4 spacing
        /// inside [min, max] (a thinner edge block would be a sliver).
        /// </summary>
        internal static List<float> DistributeAnchored(float min, float max, float spacing, float anchor, float inset = 0f)
        {
            var result = new List<float>();
            if (spacing <= 0.5f || !math.isfinite(anchor)) return result;
            float lo = min + 0.4f * spacing, hi = max - 0.4f * spacing;
            float k0 = math.ceil((lo - anchor) / spacing);
            for (float k = k0; anchor + k * spacing <= hi && result.Count < 4096; k++) result.Add(anchor + k * spacing);
            // No border: an edge block much deeper than a lot gets one more line a lot deep inside the edge.
            if (inset > 0.5f && result.Count > 0)
            {
                if (result[0] - (min + inset) > 0.6f * spacing) result.Insert(0, min + inset);
                if ((max - inset) - result[result.Count - 1] > 0.6f * spacing) result.Add(max - inset);
            }
            return result;
        }

        /// <summary>True if t lies in one of the intervals (JoinTolerance at the ends).</summary>
        private static bool ContainsPosition(List<float2> intervals, float t)
        {
            foreach (var interval in intervals)
            {
                if (t >= interval.x - JoinTolerance && t <= interval.y + JoinTolerance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Emits a line's sub-segments: for each clipped interval, the chain [end, sorted interior
        /// crossings, end] is walked in consecutive pairs. A crossing within JoinTolerance of an
        /// interval end replaces that end (T-junction: the crossing's shared world point is reused).
        /// Pieces &lt; MinSegmentLength are discarded.
        ///
        /// Cul-de-sac mode (parameters.CulDeSacMode, on the axis or axes selected by CulDeSacAxis):
        /// each piece between two consecutive chain points ("block") is, according to CulDeSacRatio
        /// (deterministic "one in N" pattern), either left as a through street or replaced by a
        /// cul-de-sac starting from one end of the block only (alternating top/bottom if Staggered) and
        /// stopping at CulDeSacDepth of its length. A block whose chosen start end is NOT a real
        /// collector (just a clipping point on the polygon edge, never an entry in `splits`) is dropped
        /// rather than creating a cul-de-sac hanging in the void.
        /// </summary>
        private static void EmitLine(List<RoadSegmentDef> segments, GridLine line,
            List<(float t, float3 world)> splits, bool axisIsU, float2 origin, float2 uDir, float2 vDir, float y,
            GridParameters parameters, ref int omittedNodeCount, bool isAvenueLine = false)
        {
            splits.Sort((a, b) => a.t.CompareTo(b.t));
            // An avenue always runs through, never a cul-de-sac (dedicated prefab in the game layer).
            bool culDeSac = parameters.CulDeSacMode && AppliesToAxis(parameters.CulDeSacAxis, axisIsU) && !isAvenueLine;
            int blockIndex = 0;

            foreach (var interval in line.Intervals)
            {
                float tA = interval.x;
                float tB = interval.y;
                float3 worldA = InterpolateAlongLine(line, axisIsU, tA, origin, uDir, vDir, y);
                float3 worldB = InterpolateAlongLine(line, axisIsU, tB, origin, uDir, vDir, y);

                // isCollector: true only if this chain point comes from a real crossing (splits), false
                // for a bare clipping end on the polygon edge (no existing road there).
                var chain = new List<(float t, float3 world, bool isCollector)> { (tA, worldA, false) };
                bool lastIsCollector = false;
                foreach (var split in splits)
                {
                    if (split.t < tA - JoinTolerance || split.t > tB + JoinTolerance)
                        continue; // crossing on another interval of the same line
                    if (split.t <= tA + JoinTolerance)
                        chain[0] = (tA, split.world, true);
                    else if (split.t >= tB - JoinTolerance)
                    {
                        worldB = split.world;
                        lastIsCollector = true;
                    }
                    else
                        chain.Add((split.t, split.world, true));
                }
                chain.Add((tB, worldB, lastIsCollector));

                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    // ACTUAL distance between the world points, not the t gap: a merged point (see
                    // BuildSubSegments) keeps its original t but its world position may be that of a
                    // neighboring accepted node, so the two are no longer necessarily proportional as
                    // they were before merging (world always came straight from t).
                    if (math.distance(chain[i].world.xz, chain[i + 1].world.xz) < MinSegmentLength)
                        continue; // block too short

                    if (!culDeSac)
                    {
                        segments.Add(new RoadSegmentDef(chain[i].world, chain[i + 1].world, isHorizontal: !axisIsU, isAvenue: isAvenueLine));
                        continue;
                    }

                    EmitCulDeSacBlock(segments, line, chain[i], chain[i + 1], blockIndex, axisIsU, parameters,
                        origin, uDir, vDir, y, ref omittedNodeCount);
                    blockIndex++;
                }
            }
        }

        /// <summary>
        /// If the avenue column AND row are both active, reports a roundabout at their crossing; same
        /// idea as a cul-de-sac's turning circle: the avenue arms cross straight through (+
        /// intersection, no trimming) and the game layer places a complete decorative asset on top
        /// ("&lt;Size&gt;Roundabout01", already a full roundabout with paved ring and markings, not
        /// just a center island). No arc road segments are generated here any more: only center/radius
        /// are returned (to pick the asset size), via RoundaboutInfo. No effect if the crossing falls
        /// outside the polygon at that point (ContainsPosition) or if an avenue index is out of range.
        /// </summary>
        private static RoundaboutInfo ComputeAvenueRoundabout(List<GridLine> uLines,
            List<GridLine> vLines, float2 origin, float2 uDir, float2 vDir, float y, GridParameters parameters)
        {
            // Out-of-range index (e.g. Columns/Rows reduced after picking a larger avenue index):
            // silently no effect, like AvenueColumnIndex/AvenueRowIndex elsewhere.
            if (parameters.AvenueColumnIndex < 0 || parameters.AvenueColumnIndex >= uLines.Count) return default;
            if (parameters.AvenueRowIndex < 0 || parameters.AvenueRowIndex >= vLines.Count) return default;

            GridLine uLine = uLines[parameters.AvenueColumnIndex];
            GridLine vLine = vLines[parameters.AvenueRowIndex];
            float u = uLine.Position;
            float v = vLine.Position;
            if (!ContainsPosition(uLine.Intervals, v) || !ContainsPosition(vLine.Intervals, u))
                return default;

            float3 center = ToWorld(origin, uDir, vDir, u, v, y);
            float radius = math.min(parameters.SpacingMeters * 0.25f, 25f);
            return new RoundaboutInfo { HasRoundabout = true, Center = center, Radius = radius };
        }

        /// <summary>
        /// A single block in cul-de-sac mode: decides (deterministic CulDeSacRatio pattern) whether it
        /// stays a through street or becomes a cul-de-sac, picks the start end (Staggered alternation),
        /// and emits the cul-de-sac if its start is a real collector; otherwise the block is just
        /// dropped. A cul-de-sac whose free end falls within MinNodeDistance of the target collector
        /// (depth close to 1 on a short block) is dropped too: it would be a node nearly coincident
        /// with an existing intersection.
        /// </summary>
        private static void EmitCulDeSacBlock(List<RoadSegmentDef> segments, GridLine line,
            (float t, float3 world, bool isCollector) a, (float t, float3 world, bool isCollector) b,
            int blockIndex, bool axisIsU, GridParameters parameters, float2 origin, float2 uDir, float2 vDir, float y,
            ref int omittedNodeCount)
        {
            if (!IsCulDeSacBlock(blockIndex, parameters.CulDeSacRatio))
            {
                // Outside the pattern: normal through block, as without the mode.
                segments.Add(new RoadSegmentDef(a.world, b.world, isHorizontal: !axisIsU));
                return;
            }

            // Top/bottom alternation: even blocks start from a, odd ones from b.
            bool startFromA = !parameters.Staggered || blockIndex % 2 == 0;
            var start = startFromA ? a : b;
            var end = startFromA ? b : a;

            if (!start.isCollector)
            {
                return; // start is not a real collector (outside the polygon): cul-de-sac dropped
            }

            float depth = math.clamp(parameters.CulDeSacDepth, 0.5f, 0.8f);
            float stubT = start.t + (end.t - start.t) * depth;
            float3 stubWorld = InterpolateAlongLine(line, axisIsU, stubT, origin, uDir, vDir, y);

            // ACTUAL distance to start.world, not the t gap: if start is a merged point (see
            // BuildSubSegments), start.world may be offset from its original start.t, while stubWorld
            // comes straight from stubT (never affected by merging), so the two are no longer
            // necessarily proportional.
            if (math.distance(stubWorld.xz, start.world.xz) < MinSegmentLength)
            {
                return; // cul-de-sac too short to be a viable road
            }

            if (math.distance(stubWorld.xz, end.world.xz) < MinNodeDistance)
            {
                omittedNodeCount++;
                return; // the cul-de-sac end would nearly coincide with the target collector
            }

            segments.Add(new RoadSegmentDef(start.world, stubWorld, isHorizontal: !axisIsU, isCulDeSacEnd: true));
        }

        /// <summary>True if CulDeSacAxis allows the mode on a line of this axisIsU.</summary>
        private static bool AppliesToAxis(CulDeSacAxis axis, bool axisIsU) => axis switch
        {
            CulDeSacAxis.Columns => axisIsU,
            CulDeSacAxis.Rows => !axisIsU,
            CulDeSacAxis.Both => true,
            _ => axisIsU
        };

        /// <summary>Deterministic "one in N" pattern: N = round(100/ratio), never random.</summary>
        private static bool IsCulDeSacBlock(int blockIndex, float ratioPercent)
        {
            if (ratioPercent <= 0f) return false;
            if (ratioPercent >= 100f) return true;
            int n = math.max(1, (int)math.round(100f / ratioPercent));
            return blockIndex % n == 0;
        }

        /// <summary>World point at coordinate t along a grid line (u=const if axisIsU, else v=const).</summary>
        private static float3 InterpolateAlongLine(GridLine line, bool axisIsU, float t,
            float2 origin, float2 uDir, float2 vDir, float y)
        {
            return axisIsU
                ? ToWorld(origin, uDir, vDir, line.Position, t, y)
                : ToWorld(origin, uDir, vDir, t, line.Position, y);
        }

        /// <summary>Converts a local point (u, v) to a world point (X, y, Z).</summary>
        private static float3 ToWorld(float2 origin, float2 uDir, float2 vDir, float u, float v, float y)
        {
            float2 world = origin + u * uDir + v * vDir;
            return new float3(world.x, y, world.y);
        }

        // ------------------------------------------------------------------
        // Local frame
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds pedestrian links (AddPedestrianLinks) when GridParameters.PedestrianLinks is on and
        /// there are segments to link.
        /// </summary>
        private static List<RoadSegmentDef> WithPedestrianLinks(List<RoadSegmentDef> segments, List<float2> polygon, float y, GridParameters parameters)
        {
            if (parameters.PedestrianLinks && segments.Count > 0)
            {
                AddPedestrianLinks(segments, polygon, y);
            }
            return segments;
        }

        /// <summary>
        /// Dominant contour direction in the polygon: principal axis of the slope tensor (sum of g·gᵀ,
        /// sign-insensitive, so opposite slopes on a hill do not cancel out), rotated by 90°. False if
        /// the area is nearly flat (mean slope &lt; 1 %).
        /// </summary>
        internal static bool TryContourDirection(List<float2> polygon, Func<float2, float> heightAt, out float2 direction) =>
            TryContourDirection(polygon, heightAt, out direction, out _);

        /// <summary>
        /// (s34) Same, with how directional the slope is: (λ1 − λ2) / (λ1 + λ2) of the slope tensor, 0 on a round hill
        /// (every direction climbs), 1 on an even hillside.
        /// </summary>
        internal static bool TryContourDirection(List<float2> polygon, Func<float2, float> heightAt, out float2 direction, out float anisotropy)
            => TryContourDirection(polygon, heightAt, out direction, out anisotropy, out _);

        /// <summary>(s61) Same, with the mean slope over the area (rise/run): a warp onto contours only makes sense on a real slope.</summary>
        internal static bool TryContourDirection(List<float2> polygon, Func<float2, float> heightAt, out float2 direction, out float anisotropy, out float meanSlope)
        {
            anisotropy = 0f;
            meanSlope = 0f;
            direction = new float2(1f, 0f);
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            foreach (float2 p in polygon)
            {
                min = math.min(min, p);
                max = math.max(max, p);
            }
            const int samples = 16;
            const float h = 8f;
            float sxx = 0f, syy = 0f, sxy = 0f, slope = 0f;
            int count = 0;
            for (int i = 0; i < samples; i++)
            {
                for (int j = 0; j < samples; j++)
                {
                    float2 p = math.lerp(min, max, (new float2(i, j) + 0.5f) / samples);
                    if (!PointInPolygon(p, polygon)) continue;
                    float2 g = new float2(heightAt(p + new float2(h, 0f)) - heightAt(p - new float2(h, 0f)),
                        heightAt(p + new float2(0f, h)) - heightAt(p - new float2(0f, h))) / (2f * h);
                    sxx += g.x * g.x;
                    syy += g.y * g.y;
                    sxy += g.x * g.y;
                    slope += math.length(g);
                    count++;
                }
            }
            meanSlope = count > 0 ? slope / count : 0f;
            if (count == 0 || slope / count < 0.01f)
            {
                return false;
            }
            float gradientAngle = 0.5f * math.atan2(2f * sxy, sxx - syy);
            float trace = sxx + syy;
            anisotropy = trace > 1e-9f ? math.sqrt((sxx - syy) * (sxx - syy) + 4f * sxy * sxy) / trace : 0f;
            direction = new float2(-math.sin(gradientAngle), math.cos(gradientAngle)); // perpendicular to the slope
            return true;
        }

        /// <summary>
        /// Builds the frame (origin, u axis, v axis): u follows the polygon's longest edge (or, with
        /// heightAt, the dominant contour direction, see TryContourDirection), then gets an extra
        /// counter-clockwise rotation of angleOffsetDegrees; v stays perpendicular to it.
        /// </summary>
        private static (float2 origin, float2 uDir, float2 vDir) BuildLocalFrame(List<float2> polygon, float angleOffsetDegrees,
            Func<float2, float> heightAt = null)
        {
            int bestIndex = 0;
            float bestLengthSq = -1f;

            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[(i + 1) % polygon.Count];
                float lenSq = math.lengthsq(b - a);
                if (lenSq > bestLengthSq)
                {
                    bestLengthSq = lenSq;
                    bestIndex = i;
                }
            }

            float2 origin2 = polygon[bestIndex];
            float2 uDir = math.normalize(polygon[(bestIndex + 1) % polygon.Count] - origin2);
            // (s61) Only on a real slope: a 1-3 % tilt used to turn a strict grid away from the outline.
            if (heightAt != null && TryContourDirection(polygon, heightAt, out float2 contour, out _, out float tilt) && tilt >= RoadContourWarp.MinSlope)
            {
                uDir = contour;
            }

            if (angleOffsetDegrees != 0f)
            {
                float radians = math.radians(angleOffsetDegrees);
                float cosA = math.cos(radians);
                float sinA = math.sin(radians);
                uDir = new float2(uDir.x * cosA - uDir.y * sinA, uDir.x * sinA + uDir.y * cosA);
            }

            float2 vDir = new float2(-uDir.y, uDir.x); // perpendicular (rotated +90°)
            return (origin2, uDir, vDir);
        }

        // ------------------------------------------------------------------
        // Even-odd clipping of a grid line to the polygon
        // ------------------------------------------------------------------

        /// <summary>
        /// Clips the line (u = position) or (v = position) at the polygon boundaries (in local
        /// coordinates). Returns the intervals INSIDE the polygon: each float2 is [start, end] along
        /// the line (v coordinate if axisIsU, otherwise u), sorted ascending.
        /// </summary>
        private static List<float2> ClipLineToPolygon(List<float2> local, bool axisIsU, float position)
        {
            // Intersections of the line with each edge, expressed as the coordinate along the line (t =
            // v if axisIsU, otherwise t = u).
            var crossings = new List<float>();

            for (int i = 0; i < local.Count; i++)
            {
                float2 p1 = local[i];
                float2 p2 = local[(i + 1) % local.Count];

                // "Transverse" coordinate (the one that must cross 'position')
                float c1 = axisIsU ? p1.x : p1.y;
                float c2 = axisIsU ? p2.x : p2.y;
                // Coordinate "along the line"
                float t1 = axisIsU ? p1.y : p1.x;
                float t2 = axisIsU ? p2.y : p2.x;

                // Half-open rule [c1, c2): each vertex is counted only once, which guarantees an even
                // number of intersections (robust ray casting when the line passes exactly through a
                // vertex).
                bool crosses = (c1 <= position && position < c2) || (c2 <= position && position < c1);
                if (!crosses) continue;

                float denom = c2 - c1;
                if (math.abs(denom) < Epsilon) continue; // edge parallel to the line

                float f = (position - c1) / denom;
                crossings.Add(t1 + f * (t2 - t1));
            }

            crossings.Sort();

            var result = new List<float2>();
            // Even-odd pairing: [enter, exit], [enter, exit], ...
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                float tA = crossings[i];
                float tB = crossings[i + 1];
                if (tB - tA < MinSegmentLength) continue; // piece too short

                result.Add(new float2(tA, tB));
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Utilities
        // ------------------------------------------------------------------

        private static List<float2> RemoveConsecutiveDuplicates(List<float2> pts)
        {
            var result = new List<float2>();
            foreach (var p in pts)
            {
                if (result.Count == 0 || math.distance(result[result.Count - 1], p) > Epsilon)
                    result.Add(p);
            }
            if (result.Count > 1 && math.distance(result[0], result[result.Count - 1]) < Epsilon)
                result.RemoveAt(result.Count - 1);
            return result;
        }

        /// <summary>Signed area (shoelace). Used to detect degenerate polygons.</summary>
        private static float SignedArea(List<float2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static List<float> DistributeEvenly(float min, float max, int count)
        {
            var result = new List<float>();
            if (count <= 0) return result;
            float step = (max - min) / (count + 1);
            for (int i = 1; i <= count; i++)
                result.Add(min + step * i);
            return result;
        }

        /// <summary>
        /// Interior positions spaced ABOUT `spacing` apart, distributed EVENLY over all of [min, max]
        /// (delegates to DistributeEvenly with the cell count closest to `spacing`), NOT a fixed step
        /// from `min` with the whole remainder dumped on the last cell (old behavior). In-game user
        /// feedback: the last cell on an axis (the span % spacing remainder) could end up noticeably
        /// WIDER (or narrower) than all the others ("the top one is wider than the bottom one"). This
        /// stayed hidden while a uniform shrink of cells at the perimeter edge masked the difference,
        /// but was real as soon as a cell at the region edge (not necessarily cut by the perimeter)
        /// simply got the whole remainder. Spreading the same small slack over ALL cells at once (via
        /// DistributeEvenly) gives nearly identical cell widths everywhere, at the cost of an actual
        /// spacing slightly different from `spacing` (never more than spacing/2 off).
        /// </summary>
        private static List<float> DistributeFixed(float min, float max, float spacing)
        {
            if (spacing <= 0.5f) return new List<float>();
            float span = max - min;
            int cellCount = (int)math.round(span / spacing);
            if (cellCount <= 1) return new List<float>();
            return DistributeEvenly(min, max, cellCount - 1);
        }

        // ------------------------------------------------------------------
        // Curve sampling (curved perimeter, e.g. roundabout)
        // ------------------------------------------------------------------

        /// <summary>Target distance (m) between two sampled points along a curved edge.</summary>
        public const float CurveSampleSpacing = 5f;

        /// <summary>Safety cap: max number of points inserted for a single edge (very long curve).</summary>
        public const int MaxCurveSamplesPerEdge = 24;

        /// <summary>Turn (degrees) beyond which a perimeter vertex is a spurious back-and-forth, if it touches a short edge.</summary>
        public const float BacktrackTurnDegrees = 150f;
        /// <summary>Edge length (m) below which a turn sharper than BacktrackTurnDegrees is treated as a back-and-forth.</summary>
        public const float BacktrackMaxLength = 20f;

        /// <summary>
        /// Removes back-and-forths from a closed outline: vertices where the path nearly doubles back
        /// (turn > BacktrackTurnDegrees) on a short edge. Seen on a real perimeter: where two roads
        /// meet, the last samples of a curve overshot the next node by ~3 m, then the outline came back
        /// (179° turn). A real road corner is never that sharp over such a short length.
        /// </summary>
        public static void RemoveBacktracks(List<float2> loop)
        {
            bool removed = true;
            while (removed && loop.Count > 3)
            {
                removed = false;
                for (int i = 0; i < loop.Count && loop.Count > 3; i++)
                {
                    int count = loop.Count;
                    float2 prev = loop[(i - 1 + count) % count];
                    float2 curr = loop[i];
                    float2 next = loop[(i + 1) % count];
                    float2 dIn = math.normalizesafe(curr - prev);
                    float2 dOut = math.normalizesafe(next - curr);
                    bool shortEdge = math.distance(prev, curr) < BacktrackMaxLength || math.distance(curr, next) < BacktrackMaxLength;
                    if (shortEdge && math.dot(dIn, dOut) < math.cos(math.radians(BacktrackTurnDegrees)))
                    {
                        loop.RemoveAt(i);
                        removed = true;
                        i--;
                    }
                }
            }
        }

        /// <summary>Same as RemoveBacktracks(List&lt;float2&gt;), on 3D positions (xz plane).</summary>
        public static void RemoveBacktracks(List<float3> loop)
        {
            var flat = new List<float2>(loop.Count);
            foreach (float3 p in loop)
            {
                flat.Add(p.xz);
            }
            RemoveBacktracks(flat);
            if (flat.Count == loop.Count)
            {
                return;
            }
            // Keep the remaining vertices in order (with their original height).
            var kept = new List<float3>(flat.Count);
            int j = 0;
            foreach (float3 p in loop)
            {
                if (j < flat.Count && math.all(p.xz == flat[j]))
                {
                    kept.Add(p);
                    j++;
                }
            }
            loop.Clear();
            loop.AddRange(kept);
        }

        /// <summary>
        /// Samples STRICTLY INTERIOR points (t in ]0, 1[) along a cubic Bézier (a = start, b/c =
        /// control points, d = end) to approximate an EXISTING curved edge (roundabout, bend...) in the
        /// perimeter polygon. A polygon built only from the nodes cuts straight across the curve
        /// (chord), which can make the generated grid spill onto the curved road itself. The ends (a
        /// and d) are NOT included: they are already the selected node positions on the caller's side.
        /// No dependency on Colossal.Mathematics here (the caller splits Bezier4x3 into 4 float3s):
        /// Core stays testable without the game SDK.
        /// </summary>
        public static List<float3> SampleCurve(float3 a, float3 b, float3 c, float3 d)
        {
            var result = new List<float3>();
            float length = CubicBezierLength(a, b, c, d);
            int samples = math.clamp((int)math.ceil(length / CurveSampleSpacing), 0, MaxCurveSamplesPerEdge);
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / (samples + 1);
                result.Add(CubicBezierPosition(a, b, c, d, t));
            }
            return result;
        }

        private static float3 CubicBezierPosition(float3 a, float3 b, float3 c, float3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        /// <summary>Approximate length as a 16-segment polyline; enough to choose a sample count.</summary>
        private static float CubicBezierLength(float3 a, float3 b, float3 c, float3 d)
        {
            const int segments = 16;
            float length = 0f;
            float3 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                float3 point = CubicBezierPosition(a, b, c, d, (float)i / segments);
                length += math.distance(prev, point);
                prev = point;
            }
            return length;
        }

        // ------------------------------------------------------------------
        // Loop mode ("superblocks"): a SINGLE collector grid (no separate Arterial level any more, see
        // GenerateLoopGrid's doc) bounds regular blocks, each filled with a UNIFORM loop with fixed
        // corners (EmitSimpleLoopBlock).
        // ------------------------------------------------------------------

        /// <summary>Corner rounding radius of the loop (see EmitSimpleLoopBlock), before reduction for a small block.</summary>
        private const float LoopCornerRadiusMax = 20f;

        /// <summary>
        /// Minimum loop width/depth below which the cul-de-sac is NOT mandatory, even if the chosen
        /// frequency would normally place one here. In-game user feedback: "cul-de-sacs don't have to
        /// exist in narrower spaces". In a cell that is too narrow, the spur squeezed against the
        /// rounded corners produced a degenerate/invalid shape rather than a plain loop without a
        /// branch. 3× LoopCornerRadiusMax: enough room for the corners AND the spur to stay visually
        /// distinct.
        /// </summary>
        private const float MinCulDeSacCellWidth = LoopCornerRadiusMax * 3f;

        /// <summary>
        /// Fraction of the block's smallest dimension used as margin between the loop and the collector
        /// (see EmitSimpleLoopBlock/LoopMarginMin/LoopMarginMax). A FIXED margin (first attempt, 10 m)
        /// left the loop almost touching the collectors once CollectorSpacingMeters exceeded about a
        /// hundred meters, visually indistinguishable from a denser classic grid (bug reported in game:
        /// "everything stuck together"). A fraction guarantees a visible yard around the loop,
        /// proportional to the block's actual size.
        /// </summary>
        private const float LoopMarginFraction = 0.25f;

        /// <summary>Minimum margin (m): avoids a loop that nearly touches the collector on a small block.</summary>
        private const float LoopMarginMin = 15f;

        /// <summary>Maximum margin (m): avoids a ridiculously small, centered loop on a very large block.</summary>
        private const float LoopMarginMax = 45f;

        /// <summary>
        /// Scales tried, in order, to fit the loop rectangle into an edge block cut by the perimeter
        /// (see EmitSimpleLoopBlock): 100% first (normal size), then symmetric shrinking toward the
        /// block center in 15-point steps down to 40% (below that the loop would be a silly dot rather
        /// than a real street). The first one that fits entirely inside the polygon is used, or no loop
        /// at all if even 40% still spills over.
        /// </summary>
        private static readonly float[] LoopFitScales = { 1f, 0.85f, 0.7f, 0.55f, 0.4f };

        /// <summary>
        /// Generates "Loop" mode: a SINGLE collector grid (same primitives as GenerateGrid:
        /// BuildLocalFrame/DistributeFixed/ClipLineToPolygon/BuildSubSegments, at
        /// CollectorSpacingMeters) cuts the perimeter into regular blocks; each block ENTIRELY inside
        /// the perimeter (see PointInPolygon, tested at the 4 corners of its loop) gets a UNIFORM loop
        /// with fixed corners (EmitSimpleLoopBlock), linked to the collector by a single branch.
        ///
        /// Replaces an older 2-level version (sparse Arterial &gt; Collector &gt; loop) whose loop
        /// followed the perimeter's actual outline through adaptive sampling (FindMaxLegDepth). That
        /// was a repeated cause of degenerate shapes in game (successive user reports: slanted back
        /// side, disproportionate corners, multiple kinks) AND the dominant performance cost (sampling
        /// + two calls to SplitSegmentsAtMidSpanAttachPoints per arterial block; the in-game
        /// performance log showed up to 350 ms per regeneration). The Arterial level also had a
        /// separate structural bug: an arterial block that was too narrow kept no inner collector
        /// (clearance filter against the arterial), so NO loop at all ("the lower I set [Arterial
        /// Spacing], the more the loops disappear"). One level with fixed geometry (rounded rectangle,
        /// never adaptive): simpler, more robust, and much cheaper to regenerate on every preview
        /// frame.
        ///
        /// Known limitation: an edge block cut by the actual outline (non-rectangular perimeter) tries
        /// several rectangle sizes shrunk toward its center before giving up (see
        /// EmitSimpleLoopBlock/LoopFitScales), so it stays empty only if even the smallest tested size
        /// (40% of normal) still spills over, or if the block's center itself falls outside the
        /// polygon.
        /// </summary>
        public static List<RoadSegmentDef> GenerateLoopGrid(IReadOnlyList<float3> selectedNodePositions, GridParameters parameters)
            => GenerateLoopGridCore(selectedNodePositions, parameters);

        private static List<RoadSegmentDef> GenerateLoopGridCore(IReadOnlyList<float3> selectedNodePositions, GridParameters parameters)
        {
            if (parameters.ConcentricMode)
            {
                return parameters.RadialMode
                    ? ConcentricGenerator.GenerateRadial(selectedNodePositions, parameters.RadialAvenues, parameters.RadialRoundaboutRadius, parameters.RadialLayers,
                        cancel: parameters.Cancel)
                    : ConcentricGenerator.Generate(selectedNodePositions, parameters.ConcentricLayers, parameters.ConcentricConnections,
                        cancel: parameters.Cancel);
            }

            if (selectedNodePositions == null || selectedNodePositions.Count < 2 || parameters.CollectorSpacingMeters <= 0.5f)
                return new List<RoadSegmentDef>();

            // Settings specific to the CLASSIC grid (avenue on column/row N, per-line cul-de-sacs):
            // BuildSubSegments/EmitLine, shared with GenerateGrid, apply them to ANY grid they build,
            // including the collectors and the fine pedestrian grid here. Left active from Grid mode,
            // they turned an inner line into a collector and cut/removed others (user feedback, in-game
            // log: "only 6 building areas instead of 9", "everything mixed up"). Loop mode has its own
            // settings (LoopCulDeSacRatio, CulDeSacDepth), so these are neutralized here for the rest
            // of the generation.
            parameters.AvenueColumnEnabled = false;
            parameters.AvenueRowEnabled = false;
            parameters.CulDeSacMode = false;

            float y = 0f;
            foreach (var p in selectedNodePositions) y += p.y;
            y /= selectedNodePositions.Count;

            var pts = new List<float2>(selectedNodePositions.Count);
            foreach (var p in selectedNodePositions) pts.Add(new float2(p.x, p.z));

            if (pts.Count == 2)
            {
                float2 mn = math.min(pts[0], pts[1]);
                float2 mx = math.max(pts[0], pts[1]);
                pts = new List<float2> { mn, new float2(mx.x, mn.y), mx, new float2(mn.x, mx.y) };
            }

            var polygon = RemoveConsecutiveDuplicates(pts);
            if (polygon.Count < 3 || math.abs(SignedArea(polygon)) < 1f)
                return new List<RoadSegmentDef>();

            (float2 origin, float2 uDir, float2 vDir) = BuildLocalFrame(polygon, parameters.AngleOffsetDegrees,
                parameters.AlignToTerrain ? parameters.HeightAt : null);

            var local = new List<float2>(polygon.Count);
            float2 lmin = new float2(float.MaxValue), lmax = new float2(float.MinValue);
            foreach (var p in polygon)
            {
                var lp = new float2(math.dot(p - origin, uDir), math.dot(p - origin, vDir));
                local.Add(lp);
                lmin = math.min(lmin, lp);
                lmax = math.max(lmax, lp);
            }

            // Superblock mode: the selection is cut into zones of about SuperblockZoneMeters,
            // distributed EVENLY to fill it exactly ("the cell size adapts to the perimeter and the
            // size of the area"). One line in SuperblockSubdivisions is a collector ("collector >
            // pedestrian > pedestrian > collector"), the others are pedestrian (see
            // GenerateSuperblockInterior); the selected perimeter (an existing road) closes it all. A
            // small selection (≤ 3 zones per axis) therefore has no inner collector: a single
            // superblock.
            List<float> uPositions;
            List<float> vPositions;
            List<float> uPedestrian = null;
            List<float> vPedestrian = null;
            if (parameters.SuperblockMode)
            {
                SplitSuperblockLines(lmin.x, lmax.x, parameters.SuperblockZoneMeters, out uPositions, out uPedestrian);
                SplitSuperblockLines(lmin.y, lmax.y, parameters.SuperblockZoneMeters, out vPositions, out vPedestrian);
            }
            else
            {
                uPositions = DistributeFixed(lmin.x, lmax.x, parameters.CollectorSpacingMeters);
                vPositions = DistributeFixed(lmin.y, lmax.y, parameters.CollectorSpacingMeters);
            }

            var uLines = new List<GridLine>(uPositions.Count);
            foreach (var u in uPositions)
                uLines.Add(new GridLine { Position = u, Intervals = ClipLineToPolygon(local, axisIsU: true, position: u) });
            var vLines = new List<GridLine>(vPositions.Count);
            foreach (var v in vPositions)
                vLines.Add(new GridLine { Position = v, Intervals = ClipLineToPolygon(local, axisIsU: false, position: v) });

            var segments = BuildSubSegments(uLines, vLines, origin, uDir, vDir, y, parameters, out _, out _);

            // Collectors = "avenue" network (2nd prefab, see RoadSegmentDef.IsAvenue). The loop
            // (emitted below, never IsAvenue) stays on the MAIN network; the cul-de-sac spur
            // (IsCulDeSacEnd) shares that same network (resolved in the game layer).
            for (int i = 0; i < segments.Count; i++)
            {
                RoadSegmentDef collector = segments[i];
                collector.IsAvenue = true;
                segments[i] = collector;
            }

            // uBounds/vBounds add the PERIMETER extents to the inner collector positions so that every
            // strip, edges included, is treated as a full block (with no inner collector at all this
            // yields a single block = the whole polygon, which is still handled correctly since the
            // loop's 4-corner test already filters out cases where it would be too large or outside the
            // polygon).
            var uBounds = new List<float> { lmin.x };
            uBounds.AddRange(uPositions);
            uBounds.Add(lmax.x);
            var vBounds = new List<float> { lmin.y };
            vBounds.AddRange(vPositions);
            vBounds.Add(lmax.y);

            int blockIndex = 0;
            for (int iu = 0; iu + 1 < uBounds.Count; iu++)
            {
                for (int iv = 0; iv + 1 < vBounds.Count; iv++)
                {
                    float uMinCell = uBounds[iu];
                    float uMaxCell = uBounds[iu + 1];
                    float vMinCell = vBounds[iv];
                    float vMaxCell = vBounds[iv + 1];

                    // Superblock (in-game user feedback, inspired by Barcelona's superilles, with a
                    // screenshot of the real model): NOT a single loop but a REAL fine grid inside EACH
                    // block bounded by the collectors; see GenerateSuperblockInterior. When
                    // uMinCell/uMaxCell/vMinCell/vMaxCell touch the edge of the selected POLYGON (not
                    // an inner collector), the fine grid still attaches to it: that edge is almost
                    // always a REAL existing road in game (the player clicks along it), and the game
                    // layer (Placemaker's RoadsPlanner/RoadConnections) connects to it automatically,
                    // exactly as it already does for the collectors (user feedback: "the pedestrian
                    // roads also have to connect to the base road, not just the collectors"; leaving an
                    // empty ring between the real perimeter and the 1st collector wastes all that
                    // land). Ends that touch NO real road (synthetic case with no road at the edge) are
                    // filtered afterwards by PruneDeadEndPedestrianSegments.
                    if (parameters.SuperblockMode)
                    {
                        GenerateSuperblockInterior(segments, local, new float2(uMinCell, vMinCell), new float2(uMaxCell, vMaxCell),
                            WithinRange(uPedestrian, uMinCell, uMaxCell), WithinRange(vPedestrian, vMinCell, vMaxCell),
                            origin, uDir, vDir, y, parameters);
                        continue;
                    }

                    // No inner collector at all on the V axis (spacing larger than the whole
                    // perimeter): neither vMin nor vMax of this single strip is a REAL collector
                    // (vBounds then only holds [lmin.y, lmax.y], the polygon bounds, not roads). Better
                    // no loop than one whose branch hangs in the void.
                    if (vPositions.Count == 0)
                    {
                        continue;
                    }

                    // Access side (toward vMin or vMax): the first edge strip (iv==0, before the 1st
                    // inner collector) MUST attach to vMax (the 1st real collector), since vMin here is
                    // only the polygon edge, not a road. Last edge strip (last collector -> edge): MUST
                    // attach to vMin, same reason. Fully interior strips alternate per actual row;
                    // otherwise ALL the city's loops would attach to the same absolute side of each
                    // collector (in-game feedback: "the loops should be on both sides of the main
                    // road").
                    bool isFirstStrip = iv == 0;
                    bool isLastStrip = iv == vBounds.Count - 2;
                    bool connectToMin = isFirstStrip ? false : isLastStrip ? true : (iv & 1) == 0;

                    bool withCulDeSac = IsCulDeSacBlock(blockIndex, parameters.LoopCulDeSacRatio);
                    bool emitted = EmitSimpleLoopBlock(segments, uMinCell, uMaxCell, vMinCell, vMaxCell, local,
                        origin, uDir, vDir, y, withCulDeSac, parameters.CulDeSacDepth, connectToMin);
                    if (emitted)
                    {
                        blockIndex++;
                    }
                }
            }

            // The loop's branch (EmitSimpleLoopBlock) meets the collector SET BACK from the block's
            // edge (never at an end of the collector itself): BuildSubSegments, called BEFORE this
            // loop, knows nothing about that attach point (computed much later, per block). Collector
            // and loop touch visually but share no real vertex until this post-pass runs (in-game
            // feedback: "the main road (loop) doesn't merge into the collector").
            segments = SplitSegmentsAtMidSpanAttachPoints(segments, isTarget: s => s.IsAvenue, isAttachSource: s => !s.IsAvenue);

            // Collectors that reach the perimeter nearly parallel to it, or as a very short end near a
            // junction: acute angles and junctions crammed against the perimeter (collisions in game).
            CleanPerimeterEnds(segments, selectedNodePositions, s => s.IsAvenue);

            // Final safety net (repeated in-game user feedback: "There are still cul-de-sacs... remove
            // them completely"): beyond clipping inner lines to each superblock's bounds in
            // GenerateSuperblockInterior (enough for a rectangular perimeter), an IRREGULAR perimeter
            // can still make ClipLineToPolygon (the real outline, not just the lmin/lmax bounding box)
            // cut an inner line mid-way at a spot that matches NO other segment. ITERATIVELY removes
            // any pedestrian segment with an end shared by NO other segment (pedestrian or collector),
            // until a fixed point (one removal can expose another further along the same chain).
            // Guarantees that NO cul-de-sac can survive in superblock mode, whatever the exact cause.
            if (parameters.SuperblockMode)
            {
                segments = PruneDeadEndPedestrianSegments(segments, polygon);
            }

            return segments;
        }

        /// <summary>
        /// Iteratively removes every pedestrian segment (RoadSegmentDef.IsPedestrian) with AT LEAST one
        /// end that is NEITHER shared by another segment NOR on the real perimeter selected by the
        /// player (see the call in GenerateLoopGrid for why). A "shared" end means ANOTHER segment has
        /// a Start or End at EXACTLY (bit-identical) the same position, consistent with the rest of the
        /// generator (see SplitSegmentsAtMidSpanAttachPoints). An end on the REAL perimeter is NOT a
        /// dead end: in game that perimeter is almost always an existing road, which the game layer
        /// (RoadsPlanner/RoadConnections) connects automatically. Treating it as a dead end wasted all
        /// the land between the perimeter and the 1st collector (user feedback: "the pedestrian roads
        /// also have to connect to the base road"). Iterative because removing a segment can orphan the
        /// end of ANOTHER segment further along the same chain; runs until no more removals are needed.
        /// </summary>
        private static List<RoadSegmentDef> PruneDeadEndPedestrianSegments(List<RoadSegmentDef> segments, List<float2> polygon)
        {
            var result = new List<RoadSegmentDef>(segments);
            const float boundaryTolerance = 0.05f;
            bool removedAny;
            do
            {
                removedAny = false;
                var pointCounts = new Dictionary<(float, float, float), int>();
                void CountPoint(float3 p)
                {
                    var key = (p.x, p.y, p.z);
                    pointCounts[key] = pointCounts.TryGetValue(key, out int c) ? c + 1 : 1;
                }
                foreach (RoadSegmentDef s in result)
                {
                    CountPoint(s.Start);
                    CountPoint(s.End);
                }
                bool IsDangling(float3 p)
                {
                    if (pointCounts[(p.x, p.y, p.z)] > 1)
                    {
                        return false; // shared by another segment: never a dead end
                    }
                    // Compare in WORLD space (segment.Start/End are already world-space, as is
                    // `polygon` here, see GenerateLoopGrid): NO conversion to local u/v space, which
                    // would compare different frames and fail the test even for a point really on the
                    // perimeter (fixed bug: it wasted the same ring of land as the original defect).
                    return !IsPointOnPolygonEdge(p.xz, polygon, boundaryTolerance);
                }
                for (int i = result.Count - 1; i >= 0; i--)
                {
                    RoadSegmentDef s = result[i];
                    if (!s.IsPedestrian)
                    {
                        continue; // never prune a collector: always a through road by nature
                    }
                    if (IsDangling(s.Start) || IsDangling(s.End))
                    {
                        result.RemoveAt(i);
                        removedAny = true;
                    }
                }
            } while (removedAny);
            return result;
        }

        /// <summary>
        /// True if `point` (same coordinate space as `polygon`) lies on one of the polygon's edges,
        /// within tolerance. Used by PruneDeadEndPedestrianSegments to tell an end on the real
        /// perimeter (never a dead end: the game layer connects it) from one genuinely orphaned in the
        /// middle of the land.
        /// </summary>
        private static bool IsPointOnPolygonEdge(float2 point, List<float2> polygon, float tolerance)
        {
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[(i + 1) % n];
                float2 ab = b - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-6f)
                {
                    continue;
                }
                float t = math.clamp(math.dot(point - a, ab) / lenSq, 0f, 1f);
                float2 projected = a + t * ab;
                if (math.distance(projected, point) < tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// One-way "swirl" (superblock, GridParameters.SuperblockMode): each cell (iu,iv) of the
        /// [uBounds,vBounds] grid circulates around its 4 edges, clockwise if (iu+iv) is even,
        /// counter-clockwise otherwise. The alternation guarantees that a segment, ALWAYS shared by
        /// exactly 2 neighboring cells (or just 1 at the zone edge), gets a single consistent direction
        /// from both sides (two neighboring cells never demand opposite directions on their shared
        /// edge: a checkerboard-parity property that holds for any horizontally OR vertically adjacent
        /// pair). Reorients each segment selected by isTarget (swapping Start/End if needed) WITHOUT
        /// changing its geometry: traffic direction on a one-way road is given by the segment's
        /// Start→End order, not by a separate flag (the engine has no "one-way" flag, see
        /// CompositionFlags; only the chosen PREFAB can be inherently one-way, e.g. an alley). Used for
        /// the fine inner grid of a superblock (GenerateSuperblockInterior, isTarget=IsPedestrian,
        /// uBounds/vBounds LOCAL to THAT superblock: each superblock restarts its own parity). Each
        /// segment ALWAYS spans exactly one interval between 2 consecutive uBounds/vBounds positions
        /// (BuildSubSegments splits at every crossing), so it maps to exactly ONE cell on each side: no
        /// segment ever has to satisfy several cells at once.
        /// </summary>
        private static void ApplyOneWaySwirl(List<RoadSegmentDef> segments, List<float> uBounds, List<float> vBounds,
            float2 origin, float2 uDir, float2 vDir, Func<RoadSegmentDef, bool> isTarget)
        {
            float2 ToLocal(float3 world) => new float2(math.dot(world.xz - origin, uDir), math.dot(world.xz - origin, vDir));

            for (int i = 0; i < segments.Count; i++)
            {
                RoadSegmentDef seg = segments[i];
                if (!isTarget(seg) || seg.IsArc)
                {
                    continue;
                }

                float2 a = ToLocal(seg.Start);
                float2 b = ToLocal(seg.End);
                bool desiredForward;
                if (seg.IsHorizontal)
                {
                    // Line at constant V (the interval varies in U): lower boundary of the block
                    // (iu,iv) BELOW it, i.e. that block's "top" edge; clockwise = +u (see the method
                    // doc for the 4-edge convention).
                    int iv = FindExactBoundIndex(vBounds, (a.y + b.y) * 0.5f);
                    int iu = FindIntervalIndex(uBounds, (a.x + b.x) * 0.5f);
                    if (iv <= 0 || iv >= vBounds.Count - 1 || iu < 0 || iu >= uBounds.Count - 1)
                    {
                        continue; // not a real inner line (polygon edge): never reoriented
                    }
                    bool clockwise = ((iu + iv) & 1) == 0;
                    bool wantsIncreasingU = clockwise;
                    desiredForward = wantsIncreasingU == (b.x >= a.x);
                }
                else
                {
                    // Line at constant U: "left" edge of the block (iu,iv) to its RIGHT; clockwise = -v
                    // (left edge, see the method doc).
                    int iu = FindExactBoundIndex(uBounds, (a.x + b.x) * 0.5f);
                    int iv = FindIntervalIndex(vBounds, (a.y + b.y) * 0.5f);
                    if (iu <= 0 || iu >= uBounds.Count - 1 || iv < 0 || iv >= vBounds.Count - 1)
                    {
                        continue;
                    }
                    bool clockwise = ((iu + iv) & 1) == 0;
                    bool wantsDecreasingV = clockwise;
                    desiredForward = wantsDecreasingV == (b.y <= a.y);
                }

                if (!desiredForward)
                {
                    (seg.Start, seg.End) = (seg.End, seg.Start);
                    segments[i] = seg;
                }
            }
        }

        /// <summary>EXACT index (within 0.5 m) of value in bounds (sorted ascending), or -1 if none. Used by ApplyOneWaySwirl to find the boundary line the segment lies on.</summary>
        private static int FindExactBoundIndex(List<float> bounds, float value)
        {
            for (int i = 0; i < bounds.Count; i++)
            {
                if (math.abs(bounds[i] - value) < 0.5f)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Index i such that bounds[i] &lt;= value &lt;= bounds[i+1] (with a 0.5 m margin), or -1. Used by ApplyOneWaySwirl to find the interval (block) the segment covers.</summary>
        private static int FindIntervalIndex(List<float> bounds, float value)
        {
            for (int i = 0; i + 1 < bounds.Count; i++)
            {
                if (value >= bounds[i] - 0.5f && value <= bounds[i + 1] + 0.5f)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Number of subdivisions per axis inside a superblock (see GenerateSuperblockInterior): 3×3 =
        /// 9 buildable "zones", exactly the real model of Barcelona's superilles (screenshot provided
        /// by the user: four collectors bound the superblock, and a fine grid of pedestrian streets
        /// subdivides it into 9 zones to build on; never a single loop).
        /// </summary>
        private const int SuperblockSubdivisions = 3;

        /// <summary>Floor (m) for GridParameters.SuperblockZoneMeters; below it the zones become too small to build on.</summary>
        public const float SuperblockZoneMetersMin = 100f;

        /// <summary>
        /// Cuts [min,max] into evenly distributed zones of about zoneMeters, then splits the inner
        /// lines into collectors (one in SuperblockSubdivisions, see GridParameters.SuperblockMode) and
        /// pedestrian streets.
        /// </summary>
        private static void SplitSuperblockLines(float min, float max, float zoneMeters, out List<float> collectors, out List<float> pedestrian)
        {
            collectors = new List<float>();
            pedestrian = new List<float>();
            float zone = math.max(zoneMeters, SuperblockZoneMetersMin);
            int zoneCount = math.max(1, (int)math.round((max - min) / zone));
            List<float> lines = DistributeEvenly(min, max, zoneCount - 1);
            for (int i = 0; i < lines.Count; i++)
            {
                // 1-based line index: 3, 6, 9... = collectors.
                if ((i + 1) % SuperblockSubdivisions == 0)
                {
                    collectors.Add(lines[i]);
                }
                else
                {
                    pedestrian.Add(lines[i]);
                }
            }
        }

        /// <summary>Positions in `positions` strictly within ]min, max[.</summary>
        private static List<float> WithinRange(List<float> positions, float min, float max)
        {
            var result = new List<float>();
            foreach (float p in positions)
            {
                if (p > min + JoinTolerance && p < max - JoinTolerance)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>
        /// Fills the interior of ONE superblock [regionMin,regionMax] (bounded by the collectors, see
        /// GenerateLoopGrid/GridParameters.SuperblockMode) with a REAL fine grid
        /// (SuperblockSubdivisions-1 inner lines per axis, same primitives as the collector grid
        /// itself: DistributeFixed-like/ ClipLineToPolygon/BuildSubSegments) instead of classic Loop
        /// mode's single loop. Every emitted segment is marked IsPedestrian (see RoadSegmentDef); never
        /// a cul-de-sac (user feedback: "No cul-de-sacs"; the fine grid is fully connected by nature).
        /// The one-way swirl is applied locally to THIS grid (see ApplyOneWaySwirl); each superblock
        /// restarts its own parity, independently of its neighbors.
        ///
        /// Inner lines are clipped against the superblock's REAL [regionMin,regionMax], whether a given
        /// side is an inner collector or the edge of the polygon selected by the player: that edge is
        /// almost always a REAL existing road in game, which the game layer
        /// (RoadsPlanner/RoadConnections) connects automatically. Holding the lines back from that edge
        /// (previous behavior) wasted all the land between the real perimeter and the 1st collector
        /// (user feedback: "the pedestrian roads also have to connect to the base road"). Ends that
        /// touch NO real road (synthetic case with no road at the edge, e.g. tests) are filtered
        /// afterwards by PruneDeadEndPedestrianSegments (which specifically exempts points on the real
        /// perimeter).
        /// </summary>
        private static void GenerateSuperblockInterior(List<RoadSegmentDef> segments, List<float2> polygon, float2 regionMin, float2 regionMax,
            List<float> uPositions, List<float> vPositions,
            float2 origin, float2 uDir, float2 vDir, float y, GridParameters parameters)
        {
            if (uPositions.Count == 0 && vPositions.Count == 0)
            {
                return; // single-zone superblock: nothing to subdivide
            }
            float2 center = (regionMin + regionMax) * 0.5f;
            if (!PointInPolygon(center, polygon))
            {
                return; // superblock entirely outside the developable area
            }

            // Inner lines are clipped against the block's REAL border (regionMin/regionMax), not a
            // shrunk one. When that border coincides with the real perimeter selected by the player
            // (the normal case in game: the player clicks along an existing road), the game layer
            // connects it to that real road automatically, exactly as it already does for the collector
            // network. Ends that touch no real road are filtered later by
            // PruneDeadEndPedestrianSegments.
            var uLines = new List<GridLine>(uPositions.Count);
            foreach (float u in uPositions)
            {
                uLines.Add(new GridLine { Position = u, Intervals = IntersectIntervalsWithRange(ClipLineToPolygon(polygon, axisIsU: true, position: u), regionMin.y, regionMax.y) });
            }
            var vLines = new List<GridLine>(vPositions.Count);
            foreach (float v in vPositions)
            {
                vLines.Add(new GridLine { Position = v, Intervals = IntersectIntervalsWithRange(ClipLineToPolygon(polygon, axisIsU: false, position: v), regionMin.x, regionMax.x) });
            }

            var interior = BuildSubSegments(uLines, vLines, origin, uDir, vDir, y, parameters, out _, out _);
            for (int i = 0; i < interior.Count; i++)
            {
                RoadSegmentDef seg = interior[i];
                seg.IsPedestrian = true;
                interior[i] = seg;
            }

            var uBoundsLocal = new List<float> { regionMin.x };
            uBoundsLocal.AddRange(uPositions);
            uBoundsLocal.Add(regionMax.x);
            var vBoundsLocal = new List<float> { regionMin.y };
            vBoundsLocal.AddRange(vPositions);
            vBoundsLocal.Add(regionMax.y);
            ApplyOneWaySwirl(interior, uBoundsLocal, vBoundsLocal, origin, uDir, vDir, isTarget: s => s.IsPedestrian);

            segments.AddRange(interior);
        }

        /// <summary>
        /// Intersects a list of intervals (already clipped against the whole polygon, see
        /// ClipLineToPolygon) with [rangeMin, rangeMax]. Used by GenerateSuperblockInterior to confine
        /// an inner line to ITS superblock in addition to the whole polygon (the stricter of the two
        /// wins).
        /// </summary>
        private static List<float2> IntersectIntervalsWithRange(List<float2> intervals, float rangeMin, float rangeMax)
        {
            var result = new List<float2>();
            foreach (float2 interval in intervals)
            {
                float a = math.max(interval.x, rangeMin);
                float b = math.min(interval.y, rangeMax);
                if (b - a > MinSegmentLength)
                {
                    result.Add(new float2(a, b));
                }
            }
            return result;
        }

        /// <summary>
        /// Bucket size (m) for the spatial index of attach points below. In-game user feedback
        /// (performance log): Loop mode took up to 350 ms per regeneration, almost all of it in this
        /// function, which was then called SEVERAL times (once per block + once globally) and compared
        /// ALL target segments with ALL attach points: O(targets × points), the same structural flaw as
        /// BuildSubSegments (see SpatialPointIndex). A generous size (typical collector/arterial
        /// spacings are tens to hundreds of meters) keeps few buckets to visit per target segment
        /// without losing real candidates.
        /// </summary>
        private const float AttachPointBucketSize = 50f;

        /// <summary>
        /// Splits each "target" segment (isTarget, always straight) where an "attach source" segment
        /// (isAttachSource) meets it STRICTLY IN THE MIDDLE of its span, never at its own ends (those
        /// are already real crossings, handled by BuildSubSegments). Used by GenerateLoopGrid to join
        /// collector and loop (target=IsAvenue, source=everything else): without this post-pass, the
        /// collector and the loop's branch touch visually but share no real vertex ("X doesn't merge
        /// into Y"). An attach point is found by projecting each Start/End of a source segment onto
        /// EACH target segment's line (parameter t strictly in ]0, 1[, and distance to the line nearly
        /// zero: these points are computed to land EXACTLY on it, not just "nearby", so a loose
        /// tolerance would signal a real geometric coincidence, not a false positive). Several sources
        /// attached at the same spot produce nearly identical points, deduplicated via
        /// MinSegmentLength.
        /// </summary>
        private static List<RoadSegmentDef> SplitSegmentsAtMidSpanAttachPoints(List<RoadSegmentDef> segments,
            Func<RoadSegmentDef, bool> isTarget, Func<RoadSegmentDef, bool> isAttachSource)
        {
            var attachPoints = new List<float3>();
            foreach (RoadSegmentDef s in segments)
            {
                if (isAttachSource(s))
                {
                    attachPoints.Add(s.Start);
                    attachPoints.Add(s.End);
                }
            }
            if (attachPoints.Count == 0)
            {
                return segments;
            }

            // Spatial bucket index: instead of testing ALL attach points against EACH target segment,
            // only visit the buckets covering the segment's bounding box (margin = projection tolerance
            // below).
            var buckets = new Dictionary<(int, int), List<float3>>();
            foreach (float3 p in attachPoints)
            {
                var cell = ((int)math.floor(p.x / AttachPointBucketSize), (int)math.floor(p.z / AttachPointBucketSize));
                if (!buckets.TryGetValue(cell, out List<float3> bucket))
                {
                    bucket = new List<float3>();
                    buckets[cell] = bucket;
                }
                bucket.Add(p);
            }

            var result = new List<RoadSegmentDef>(segments.Count);
            foreach (RoadSegmentDef target in segments)
            {
                if (!isTarget(target) || target.IsArc)
                {
                    result.Add(target);
                    continue;
                }

                float2 a = target.Start.xz;
                float2 ab = target.End.xz - a;
                float lenSq = math.lengthsq(ab);
                if (lenSq < 1e-4f)
                {
                    result.Add(target);
                    continue;
                }

                float2 minXz = math.min(target.Start.xz, target.End.xz) - 0.5f;
                float2 maxXz = math.max(target.Start.xz, target.End.xz) + 0.5f;
                int cx0 = (int)math.floor(minXz.x / AttachPointBucketSize);
                int cx1 = (int)math.floor(maxXz.x / AttachPointBucketSize);
                int cz0 = (int)math.floor(minXz.y / AttachPointBucketSize);
                int cz1 = (int)math.floor(maxXz.y / AttachPointBucketSize);

                var hits = new List<(float t, float3 point)>();
                for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    if (!buckets.TryGetValue((cx, cz), out List<float3> candidates))
                    {
                        continue;
                    }
                    foreach (float3 candidate in candidates)
                    {
                        float2 p = candidate.xz;
                        float t = math.dot(p - a, ab) / lenSq;
                        if (t <= 0f || t >= 1f)
                        {
                            continue; // at or beyond an end: never a real mid-span join
                        }
                        float2 projected = a + t * ab;
                        if (math.distance(projected, p) > 0.5f)
                        {
                            continue; // not on this target segment
                        }
                        // Fixed bug: recomputing the point via math.lerp(target.Start, target.End, t)
                        // builds a coordinate INDEPENDENT of the source's (up to 0.5 m off, the
                        // tolerance above; never guaranteed bit-identical even at zero tolerance,
                        // because of float rounding along two different computation paths). Downstream,
                        // the game layer only knows real points (existing node/edge) or brand-new free
                        // points: two FRESHLY generated segments that should share THE SAME new node
                        // have no way to recognize each other other than EXACT position equality. The
                        // game (GenerateNodesSystem) then turns two nearly identical but not bit-exact
                        // points into TWO nodes close together instead of one: exactly the "Overlapping
                        // objects" error with no junction, but between two segments we generated
                        // ourselves, never against a real road (so invisible to all earlier
                        // diagnostics, which only tested joins to existing roads). Fix: take the source
                        // point (candidate) AS IS, never recomputed, which guarantees a bit-identical
                        // coordinate for the two segments that must meet.
                        float3 point = candidate;
                        if (math.distance(point, target.Start) < MinSegmentLength
                            || math.distance(point, target.End) < MinSegmentLength)
                        {
                            continue; // too close to a real end: degenerate piece
                        }
                        hits.Add((t, point));
                    }
                }

                if (hits.Count == 0)
                {
                    result.Add(target);
                    continue;
                }

                hits.Sort((x, y) => x.t.CompareTo(y.t));

                float3 pieceStart = target.Start;
                float3 lastAccepted = target.Start;
                foreach ((float _, float3 point) in hits)
                {
                    if (math.distance(point, lastAccepted) < MinSegmentLength)
                    {
                        continue; // near-duplicate (2 sources attached at the same spot)
                    }
                    result.Add(new RoadSegmentDef(pieceStart, point, target.IsHorizontal, isAvenue: target.IsAvenue));
                    pieceStart = point;
                    lastAccepted = point;
                }
                result.Add(new RoadSegmentDef(pieceStart, target.End, target.IsHorizontal, isAvenue: target.IsAvenue));
            }
            return result;
        }

        /// <summary>Minimum side (m) of a loop shrunk to fit the shape: smaller loops are not placed.</summary>
        private const float MinLoopSide = 70f;
        /// <summary>Minimum angle (degrees) between a street and the perimeter where it joins it.</summary>
        private const float MinPerimeterJoinAngle = 35f;
        /// <summary>Street end shorter (m) than this between a junction and the perimeter: removed.</summary>
        private const float MinPerimeterStub = 20f;

        /// <summary>
        /// Cleans up street ends (filtered by `eligible`) that join the perimeter: removes those that
        /// meet it at less than MinPerimeterJoinAngle or are shorter than MinPerimeterStub from a
        /// junction, then the streets left dangling (free end off the perimeter), until a fixed point.
        /// A segment is only removed if its other end stays connected (junction).
        /// </summary>
        internal static void CleanPerimeterEnds(List<RoadSegmentDef> segments, IReadOnlyList<float3> perimeter, Func<RoadSegmentDef, bool> eligible)
        {
            int n = perimeter.Count;
            if (n < 3 || segments.Count == 0)
            {
                return;
            }
            // Distance to the perimeter, cached per end point (a large grid shares its nodes).
            var info = new Dictionary<float3, (bool on, float2 tangent)>();
            (bool on, float2 tangent) Info(float3 p)
            {
                if (info.TryGetValue(p, out var cached)) return cached;
                float best = float.MaxValue;
                float2 tangent = new float2(1f, 0f);
                for (int i = 0; i < n; i++)
                {
                    float2 a = perimeter[i].xz, ab = perimeter[(i + 1) % n].xz - a;
                    float lengthSq = math.lengthsq(ab);
                    if (lengthSq < 1e-6f) continue;
                    float t = math.saturate(math.dot(p.xz - a, ab) / lengthSq);
                    float d = math.distance(p.xz, a + t * ab);
                    if (d < best)
                    {
                        best = d;
                        tangent = ab / math.sqrt(lengthSq);
                    }
                }
                return info[p] = (best < 0.5f, tangent);
            }

            var incident = new Dictionary<float3, List<int>>();
            var degree = new Dictionary<float3, int>();
            void Link(float3 p, int k)
            {
                if (!incident.TryGetValue(p, out var list)) incident[p] = list = new List<int>();
                list.Add(k);
                degree[p] = degree.TryGetValue(p, out int d) ? d + 1 : 1;
            }
            for (int k = 0; k < segments.Count; k++)
            {
                Link(segments[k].Start, k);
                Link(segments[k].End, k);
            }

            var alive = new bool[segments.Count];
            var queue = new List<int>();
            for (int k = 0; k < segments.Count; k++)
            {
                alive[k] = true;
                if (!eligible(segments[k])) continue;
                // Only segments on the perimeter or dangling count; the rest is never removed up front.
                if (Info(segments[k].Start).on || Info(segments[k].End).on || degree[segments[k].Start] == 1 || degree[segments[k].End] == 1)
                {
                    queue.Add(k);
                }
            }
            while (queue.Count > 0)
            {
                int k = queue[queue.Count - 1];
                queue.RemoveAt(queue.Count - 1);
                if (!alive[k]) continue;
                RoadSegmentDef s = segments[k];
                (bool startOn, float2 startTangent) = Info(s.Start);
                (bool endOn, float2 endTangent) = Info(s.End);
                bool remove = false;
                if (startOn != endOn)
                {
                    float3 on = startOn ? s.Start : s.End, off = startOn ? s.End : s.Start;
                    float2 tangent = startOn ? startTangent : endTangent;
                    // Street direction at the arrival point (the arc tangent if there is one).
                    float2 direction = math.normalizesafe(off.xz - on.xz);
                    if (s.IsArc)
                    {
                        float3 t = startOn ? s.StartTangent : -s.EndTangent;
                        direction = math.normalizesafe(t.xz, direction);
                    }
                    float angle = math.degrees(math.asin(math.saturate(math.abs(tangent.x * direction.y - tangent.y * direction.x))));
                    float length = math.distance(s.Start.xz, s.End.xz);
                    remove = degree[off] >= 3 && (angle < MinPerimeterJoinAngle || length < MinPerimeterStub);
                }
                else if (!startOn && !endOn && !s.IsCulDeSacEnd && (degree[s.Start] == 1) != (degree[s.End] == 1))
                {
                    // Street left dangling after a removal (free end off the perimeter, other end
                    // connected).
                    remove = true;
                }
                if (!remove) continue;
                alive[k] = false;
                foreach (float3 p in new[] { s.Start, s.End })
                {
                    degree[p]--;
                    foreach (int other in incident[p])
                    {
                        if (alive[other] && eligible(segments[other])) queue.Add(other);
                    }
                }
            }
            int write = 0;
            for (int k = 0; k < segments.Count; k++)
            {
                if (alive[k]) segments[write++] = segments[k];
            }
            segments.RemoveRange(write, segments.Count - write);
        }

        /// <summary>
        /// Emits a UNIFORM loop with fixed rounded corners for ONE block [uMin,uMax]x[vMin,vMax];
        /// replaces the old EmitLoopBlock (adaptive depth by sampling the real outline, see
        /// GenerateLoopGrid's doc for why it was replaced). The rectangle is inset symmetrically on all
        /// 4 sides by a margin (LoopMarginFraction, clamped to LoopMarginMin/LoopMarginMax), with
        /// corners rounded at a fixed radius (LoopCornerRadiusMax, reduced if needed). NO sampling and
        /// NO dependence on the polygon's actual shape beyond a simple "are the 4 rectangle corners
        /// inside?" test: a block cut by the perimeter edge gets a smaller rectangle (LoopFitScales)
        /// rather than a loop deformed to hug the edge, so the geometry is always valid, never
        /// degenerate.
        ///
        /// A single branch connects the loop to the collector, on the side given by connectToMin (vMin
        /// or vMax); the cul-de-sac, if enabled, starts from the OPPOSITE side (the loop's "back",
        /// farthest from the collector) and runs toward the center.
        ///
        /// Classic Loop mode only (never called when GridParameters.SuperblockMode is true; see
        /// GenerateLoopGrid/GenerateSuperblockInterior, where a fine grid replaces this single loop).
        /// </summary>
        private static bool EmitSimpleLoopBlock(List<RoadSegmentDef> segments, float uMin, float uMax, float vMin, float vMax,
            List<float2> polygon, float2 origin, float2 uDir, float2 vDir, float y, bool withCulDeSac, float culDeSacDepth, bool connectToMin)
        {
            float width = uMax - uMin;
            float height = vMax - vMin;
            float uMargin = math.clamp(width * LoopMarginFraction, LoopMarginMin, LoopMarginMax);
            float vMargin = math.clamp(height * LoopMarginFraction, LoopMarginMin, LoopMarginMax);
            if (width < 2f * uMargin + MinSegmentLength || height < 2f * vMargin + MinSegmentLength)
            {
                return false; // block too small for a readable loop
            }

            // The rectangle's 4 corners must be inside the REAL polygon (not just the block center).
            // But instead of giving up as soon as the FULL-SIZE rectangle spills over (in-game user
            // feedback: "what do we do with these empty spaces?", edge blocks left completely empty
            // with no loop), several DECREASING sizes are tried, ALWAYS centered on the same point
            // (LoopFitScales, 100% -> 40%): the rectangle shrinks symmetrically toward the block center
            // until its 4 corners fit, or down to the floor (40%; below that the loop would be a silly
            // dot, better to leave it empty). NO outline sampling here (unlike the old EmitLoopBlock):
            // always a plain rectangle, never a shape hugging the edge; only its SIZE adapts, in a few
            // cheap tries (4 PointInPolygon tests per scale).
            float centerU = (uMin + uMax) * 0.5f;
            float centerV = (vMin + vMax) * 0.5f;
            if (!PointInPolygon(new float2(centerU, centerV), polygon))
            {
                return false; // the center itself is not in the developable area
            }

            float fullHalfWidth = width * 0.5f - uMargin;
            float fullHalfHeight = height * 0.5f - vMargin;

            float leftU = 0f, rightU = 0f, nearV = 0f, farV = 0f;
            bool fits = false;
            bool shrunk = false;
            foreach (float scale in LoopFitScales)
            {
                float halfW = fullHalfWidth * scale;
                float halfH = fullHalfHeight * scale;
                float l = centerU - halfW, rr = centerU + halfW, n = centerV - halfH, f = centerV + halfH;
                if (PointInPolygon(new float2(l, n), polygon) && PointInPolygon(new float2(rr, n), polygon)
                    && PointInPolygon(new float2(rr, f), polygon) && PointInPolygon(new float2(l, f), polygon))
                {
                    leftU = l; rightU = rr; nearV = n; farV = f;
                    fits = true;
                    shrunk = scale < 1f;
                    break;
                }
            }
            if (!fits)
            {
                return false;
            }
            // Loop shrunk to fit an irregular shape: below MinLoopSide it is just a small ring of 10 m
            // segments (reproduced from an empty area in game). A full-size loop in a small regular
            // block is still allowed.
            if (shrunk && (rightU - leftU < MinLoopSide || farV - nearV < MinLoopSide))
            {
                return false;
            }

            withCulDeSac = withCulDeSac && (rightU - leftU) >= MinCulDeSacCellWidth && (farV - nearV) >= MinCulDeSacCellWidth;

            float r = math.min(LoopCornerRadiusMax, math.min(rightU - leftU, farV - nearV) * 0.5f);
            if (r < 1f)
            {
                return false; // block too narrow to visibly round the corners
            }

            // 4 vertices of the inset rectangle (before corner rounding), clockwise from the top-left
            // corner in local U/V.
            float2 pTL = new float2(leftU, nearV), pTR = new float2(rightU, nearV);
            float2 pBR = new float2(rightU, farV), pBL = new float2(leftU, farV);

            // Straight/arc transition points, one pair per corner (entry and exit of each arc).
            float2 topEnd = new float2(pTR.x - r, pTR.y), rightStart = new float2(pTR.x, pTR.y + r);
            float2 rightEnd = new float2(pBR.x, pBR.y - r), bottomStart = new float2(pBR.x - r, pBR.y);
            float2 bottomEnd = new float2(pBL.x + r, pBL.y), leftStart = new float2(pBL.x, pBL.y - r);
            float2 leftEnd = new float2(pTL.x, pTL.y + r), topStart = new float2(pTL.x + r, pTL.y);

            float3 wU = new float3(uDir.x, 0f, uDir.y);
            float3 wV = new float3(vDir.x, 0f, vDir.y);
            float3 ToW(float2 p) => ToWorld(origin, uDir, vDir, p.x, p.y, y);

            void AddStraight(float2 a, float2 b)
            {
                float3 wa = ToW(a), wb = ToW(b);
                if (math.distance(wa, wb) >= MinSegmentLength)
                {
                    segments.Add(new RoadSegmentDef(wa, wb, isHorizontal: false));
                }
            }
            void AddArc(float2 a, float2 b, float3 tangentIn, float3 tangentOut)
            {
                float3 wa = ToW(a), wb = ToW(b);
                if (math.distance(wa, wb) >= MinSegmentLength)
                {
                    segments.Add(RoadSegmentDef.Arc(wa, wb, tangentIn, tangentOut));
                }
            }

            // The branch (always present) touches the connectToMin side (nearV=top) or the opposite one
            // (farV=bottom) AT ITS MIDDLE (uMid); the cul-de-sac (if enabled) ALWAYS starts from the
            // side opposite the branch (the loop's "back"), also at its middle. So uMid ALWAYS splits
            // the branch side in two, and ALSO the back side if a cul-de-sac is emitted there. Without
            // that, these branches touch the middle of a loop side without ever sharing a real vertex
            // with it (in-game user feedback: "collisions between the road that accesses the loop and
            // the loop": a missing junction, exactly the same structural bug as the one already fixed
            // between collector and loop, see SplitSegmentsAtMidSpanAttachPoints). The split is
            // UNCONDITIONAL (instead of first checking that both halves exceed MinSegmentLength):
            // AddStraight already silently drops a half that is too short while keeping the other, so
            // the junction vertex (uMid, that side) is a REAL vertex of the surviving half in every
            // case; an "approximate junction" fallback is never needed.
            float uMid = (leftU + rightU) * 0.5f;
            bool connectorOnTopSide = connectToMin;
            bool spurOnTopSide = !connectToMin;
            bool splitTop = connectorOnTopSide || (withCulDeSac && spurOnTopSide);
            bool splitBottom = !connectorOnTopSide || (withCulDeSac && !spurOnTopSide);
            bool emitSpur = withCulDeSac;

            // The loop's full outline, clockwise: 4 straight sides (top/bottom split in 2 when needed,
            // see above) + 4 rounded corners.
            if (splitTop)
            {
                AddStraight(topStart, new float2(uMid, topStart.y));
                AddStraight(new float2(uMid, topStart.y), topEnd);
            }
            else
            {
                AddStraight(topStart, topEnd);
            }
            AddArc(topEnd, rightStart, wU, wV);
            AddStraight(rightStart, rightEnd);
            AddArc(rightEnd, bottomStart, wV, -wU);
            if (splitBottom)
            {
                AddStraight(bottomStart, new float2(uMid, bottomStart.y));
                AddStraight(new float2(uMid, bottomStart.y), bottomEnd);
            }
            else
            {
                AddStraight(bottomStart, bottomEnd);
            }
            AddArc(bottomEnd, leftStart, -wU, -wV);
            AddStraight(leftStart, leftEnd);
            AddArc(leftEnd, topStart, -wV, wU);

            // Single branch to the collector, on the connectToMin side (vMin) or the opposite one
            // (vMax): from the middle of the chosen side (uMid, now a REAL shared vertex, see above) to
            // the actual collector line (the block edge BEFORE the margin; see
            // SplitSegmentsAtMidSpanAttachPoints for the join on the collector side).
            float connectorNearV = connectToMin ? nearV : farV;
            float connectorFarV = connectToMin ? vMin : vMax;
            AddStraight(new float2(uMid, connectorFarV), new float2(uMid, connectorNearV));

            // Cul-de-sac: starts at the exact vertex where the back side was split (uMid, that side)
            // and runs toward the center. spurLength = culDeSacDepth fraction of the loop's total
            // depth, same convention as the classic grid's cul-de-sacs (EmitCulDeSacBlock, clamped to
            // 0.5-0.8).
            if (emitSpur)
            {
                float farSideV = spurOnTopSide ? nearV : farV;
                float depth = math.abs(farV - nearV);
                float spurLength = depth * math.clamp(culDeSacDepth, 0.5f, 0.8f);
                float spurSign = spurOnTopSide ? 1f : -1f;
                float2 spurStart = new float2(uMid, farSideV);
                float2 spurEnd = new float2(uMid, farSideV + spurSign * spurLength);
                float3 wStart = ToW(spurStart), wEnd = ToW(spurEnd);
                if (math.distance(wStart, wEnd) >= MinSegmentLength)
                {
                    segments.Add(new RoadSegmentDef(wStart, wEnd, isHorizontal: false, isCulDeSacEnd: true));
                }
            }

            return true;
        }

        /// <summary>True if point is inside polygon (same coordinate space; even-odd rule, horizontal ray casting). Used e.g. to check that a Loop block lies entirely inside the perimeter.</summary>
        private static bool PointInPolygon(float2 point, List<float2> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (crosses)
                {
                    float t = (point.y - a.y) / (b.y - a.y);
                    float xCross = a.x + t * (b.x - a.x);
                    if (point.x < xCross)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

    }
}
