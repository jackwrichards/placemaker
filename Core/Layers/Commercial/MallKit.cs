// s32 — more of the Commercial kit: lane networks (T and X junctions between the type's own lanes, closed loops),
// rows of plots along any lane segment with their own zone (big high-density plots on the streets, small low-density
// ones on the lanes), walks (footpaths joined to lanes at a node), paseos through a row, add-on features (playground,
// fountain, kiosks…) run as sub-plans on a green or paved piece.
//
// Zoning rules the compositions follow (s26, decompiled 1.6.2f1 BlockSystem / ZoneGuard):
//   * a plot is zoned by the road whose block reaches it at the shallowest row; ties go to the build's own lanes;
//   * so a plot meant to face an existing street keeps one whole cell (8 m) away from every lane of the build — the
//     lane's row 0 then lies in the gap (blocked, planted) and the street's row 0 wins (Gap);
//   * a plot touching two of the build's lanes may face either (both carry the same zone);
//   * a lane only carries zone blocks if its prefab does: Alley / Pedestrian Street are checked against the catalog
//     (AssetInfo.ZoneBlocks) and replaced by a road that does when the game says they carry none.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class Mall
    {
        /// <summary>A whole zone cell: what keeps a street-facing plot from being zoned by a lane beside it.</summary>
        public const float Gap = 8f;
        /// <summary>(s39) Shortest free stretch of frontage that gets plots: two cells and a metre (two whole columns whatever the grid).</summary>
        public const float MinStretch = 2 * Cell + 1f;
        /// <summary>(rf-shops3) Rows of the designed compositions: 0 = the old even split; above 0 pieces are rounded to the
        /// row's piece size and the end pieces of a row of three or more are this many times wider (anchors).</summary>
        public float AnchorWeight;
        /// <summary>Width of a paseo (a walk through a row of plots, m).</summary>
        /// <remarks>(s39) Wide enough that no zone column whose centre lies on a plot touches the walk (a walk closes the column it
        /// crosses; a 3 m walk ± 4 m of cell): the plots either side keep whole columns (game depth rule, s36).</remarks>
        public const float PaseoWidth = 11f;

        /// <summary>(s32) Zone of a plot when it differs from the type's zone (reference keys: the ring arrays in Shops).</summary>
        public readonly Dictionary<float2[], string> ShopZone = new Dictionary<float2[], string>();

        /// <summary>Walks (footpaths) the composition laid itself: from → to, for tests and the walk counter.</summary>
        public readonly List<(float2 a, float2 b)> Walks = new List<(float2, float2)>();

        /// <summary>
        /// (s32) A pedestrian lane that shops can face: "Pedestrian Street Small" (8 m) or "Pedestrian Street" (16 m) when
        /// the catalog does not say it carries no zone blocks, else the shop lane (alley / road).
        /// </summary>
        public static (AssetRef asset, float half, bool pedestrian) PedLane(LayerContext ctx, bool wide)
        {
            var assets = ctx?.Site?.Assets;
            string id = wide ? "Pedestrian Street" : "Pedestrian Street Small";
            if (assets != null && assets.TryGetValue(id, out var info) && info.ZoneBlocks == 2)
            {
                var (a, h) = ShopLane(ctx);
                return (a, h, false);
            }
            float half = wide ? 8f : AlleyHalf;
            if (assets != null && assets.TryGetValue(id, out var w) && w.NetWidth > 0.5f) half = w.NetWidth * 0.5f;
            return (wide ? PedestrianStreet : PedestrianStreetSmall, half, true);
        }

        /// <summary>(s32) A road for big-box traffic: the "Small Road" when it exists (12 m), else the shop lane.</summary>
        public static (AssetRef asset, float half) ServiceRoad(LayerContext ctx)
        {
            var assets = ctx?.Site?.Assets;
            AssetInfo road = default;
            bool known = assets != null && assets.TryGetValue(StreetShops.AlleyFallback, out road);
            if (known && road.ZoneBlocks == 2) return ShopLane(ctx);
            // The vanilla "Small Road" is the 16 m Two-Lane Road (RoadRoles).
            return (new AssetRef(StreetShops.AlleyFallback, StreetShops.AlleyAsset), known && road.NetWidth > 0.5f ? road.NetWidth * 0.5f : 8f);
        }

        // ───────────── lane networks ─────────────

        public enum EndKind { Street, OnLane, Free }

        /// <summary>Where a lane starts or ends: on an existing street (outline point + outward), on one of the mall's lanes, or free.</summary>
        public struct End
        {
            public EndKind Kind;
            public float2 At, Outward;
            public Lane On;
            public static End Street(float2 outlinePoint, float2 outward) => new End { Kind = EndKind.Street, At = outlinePoint, Outward = outward };
            public static End OnLane(Lane lane, float2 p) => new End { Kind = EndKind.OnLane, At = p, On = lane };
            public static End Free(float2 p) => new End { Kind = EndKind.Free, At = p };
        }

        /// <summary>
        /// A lane from <paramref name="a"/> through <paramref name="inner"/> to <paramref name="b"/>. Ends on another lane
        /// become a junction (the host is split there at Finish: identical node); null when a street end finds no street,
        /// a point leaves the site or the corridor is not free.
        /// </summary>
        public Lane Connect(End a, IList<float2> inner, End b, NetRole role, AssetRef asset, float half)
        {
            var lane = new Lane { Role = role, Asset = asset, Half = half, Shoulder = 2f * MallGrading.Cell, MaxGrade = 0.06f };
            if (!Resolve(a, out var pa, out lane.StartAttach, out lane.StartT)) return null;
            if (!Resolve(b, out var pb, out lane.EndAttach, out lane.EndT)) return null;
            lane.StartOn = a.Kind == EndKind.OnLane ? a.On : null;
            lane.EndOn = b.Kind == EndKind.OnLane ? b.On : null;
            lane.Points.Add(pa);
            if (inner != null) lane.Points.AddRange(inner);
            lane.Points.Add(pb);
            for (int i = 1; i + 1 < lane.Points.Count; i++) if (!Region.Contains(lane.Points[i])) return null;
            if (a.Kind != EndKind.Street && !Region.Contains(pa)) return null;
            if (b.Kind != EndKind.Street && !Region.Contains(pb)) return null;
            for (int i = 0; i + 1 < lane.Points.Count; i++) if (math.distance(lane.Points[i], lane.Points[i + 1]) < 1f) return null;
            var corridors = Corridors(lane);
            foreach (var c in corridors) if (!Free(c)) return null;
            lane.Index = Lanes.Count;
            lane.Land = corridors;
            Lanes.Add(lane);
            Taken.AddRange(corridors);
            if (lane.StartOn != null) lane.StartOn.Joins.Add(pa);
            if (lane.EndOn != null) lane.EndOn.Joins.Add(pb);
            return lane;
        }

        /// <summary>
        /// (s32) A lot's stub or footpath that found one of the lanes ends exactly on that lane's segment: the lane is split
        /// at the projection (Finish), so the net end must be that point (a hit computed on a segment's end beyond a bend
        /// would otherwise stop a few centimetres short of the node). Ends already on the line are left bit-for-bit alone.
        /// </summary>
        private void SnapOnto(Lane lane, int seg, ref float3 end, ref float3 control)
        {
            if (seg < 0 || seg + 1 >= lane.Points.Count) return;
            var a = lane.Points[seg]; var b = lane.Points[seg + 1];
            var ab = b - a; float l2 = math.lengthsq(ab);
            if (l2 < 1e-6f) return;
            var q = a + ab * math.saturate(math.dot(end.xz - a, ab) / l2);
            if (math.distance(q, end.xz) <= 0.01f) return;
            var d = new float3(q.x - end.x, 0f, q.y - end.z);
            end += d; control += d;
        }

        /// <summary>
        /// (rf-shops3) A lot's stub ending within 1.5 m of a join already on its lane (another net's end) ends on that join: a stub 0.3 m
        /// from a driveway's junction made Finish move both split points onto one, and the driveway's lane ended beside the node (a
        /// dead end). (Near a corner of the lane Finish moves the corner onto the stub's end instead.)
        /// </summary>
        private static void OntoNode(Lane lane, ref float3 end, ref float3 control)
        {
            float best = 1.5f; var q = end.xz;
            foreach (var j in lane.Joins) { float dj = math.distance(j, end.xz); if (dj < best) { best = dj; q = j; } }
            if (math.distance(q, end.xz) <= 0.01f) return;
            var d = new float3(q.x - end.x, 0f, q.y - end.z);
            end += d; control += d;
        }

        /// <summary>A join on <paramref name="lane"/>, once (a repeated join would split the lane twice at one point).</summary>
        private static void AddJoin(Lane lane, float2 p)
        {
            foreach (var j in lane.Joins) if (math.distance(j, p) <= 0.01f) return;
            lane.Joins.Add(p);
        }

        /// <summary>Takes back a lane made by <see cref="Connect"/> (before anything was planned against it).</summary>
        public void Remove(Lane lane)
        {
            if (lane == null || !Lanes.Remove(lane)) return;
            if (lane.Land != null) foreach (var c in lane.Land) Taken.Remove(c);
            if (lane.StartOn != null) lane.StartOn.Joins.Remove(lane.Points[0]);
            if (lane.EndOn != null) lane.EndOn.Joins.Remove(lane.Points[lane.Points.Count - 1]);
            for (int i = 0; i < Lanes.Count; i++) Lanes[i].Index = i;
        }

        /// <summary>A closed loop through <paramref name="ring"/> (corners), starting and ending at <paramref name="start"/> on its first side.</summary>
        public Lane Loop(float2 start, IList<float2> corners, NetRole role, AssetRef asset, float half)
        {
            var pts = new List<float2>(corners);
            var lane = Connect(End.Free(start), pts, End.Free(start + new float2(0.0001f, 0f)), role, asset, half);
            if (lane == null) return null;
            lane.Points[lane.Points.Count - 1] = start;
            lane.Closed = true;
            return lane;
        }

        private bool Resolve(End e, out float2 p, out long attach, out float t)
        {
            attach = -1; t = 0f; p = e.At;
            switch (e.Kind)
            {
                case EndKind.Street:
                    return StreetShops.StreetEnd(Ctx.Input, e.At, e.Outward, out p, out attach, out t);
                case EndKind.OnLane:
                    if (e.On == null) return false;
                    p = Project(e.On, e.At, out float d);
                    return d < 0.5f;
                default:
                    return true;
            }
        }

        /// <summary>
        /// (s39) A point on a lane within 1.5 m of one of its nodes or joins becomes that node: Finish splits a lane only
        /// more than a metre from its nodes, so a join closer than that would end short of the node.
        /// </summary>
        public static float2 OnNode(Lane lane, float2 p)
        {
            float best = 1.5f; var q = p;
            foreach (var v in lane.Points) { float d = math.distance(v, p); if (d < best) { best = d; q = v; } }
            foreach (var v in lane.Joins) { float d = math.distance(v, p); if (d < best) { best = d; q = v; } }
            return q;
        }

        /// <summary>The closest point of a lane's centre line.</summary>
        public static float2 Project(Lane lane, float2 p, out float dist)
        {
            float best = float.MaxValue; float2 q = p;
            for (int i = 0; i + 1 < lane.Points.Count; i++)
            {
                var a = lane.Points[i]; var b = lane.Points[i + 1];
                var ab = b - a; float l2 = math.lengthsq(ab);
                float t = l2 > 1e-6f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                var c = a + ab * t;
                float d = math.distance(p, c);
                if (d < best) { best = d; q = c; }
            }
            dist = best;
            return q;
        }

        // ───────────── frames and rows ─────────────

        /// <summary>Where a lane's zone cells begin (ZoneUtils.GetCellWidth · 4 from its centre line; = the edge of 8/16 m lanes).</summary>
        public static float ZoneOffset(Lane lane) => ZoneOffset(lane.Half);

        /// <summary>Where a lane of half width <paramref name="half"/> zones from (its zone blocks start on the cell grid of its width).</summary>
        public static float ZoneOffset(float half) => math.max(half, Build.ZoneBlockPredictor.CellWidth(2f * half) * 4f);

        /// <summary>
        /// (rf-shops3) How far in from the outline (at <paramref name="p"/>, outward normal <paramref name="nOut"/>) a ring drive of half
        /// width <paramref name="half"/> runs so a row <paramref name="depth"/> m deep fits between it and the street: the row starts
        /// at the drive's zone offset (a 12 m drive zones from 8 m, not 6), and its back stays clear of the street's first zone row
        /// (<see cref="KerbMargin"/>, a metre to spare: <see cref="BacksOntoStreet"/> tests the plot's corners).
        /// </summary>
        public float RowInset(float2 p, float2 nOut, float half, float depth) =>
            ZoneOffset(half) + depth + math.max(1.5f, KerbMargin(p, new float2(-nOut.y, nOut.x)) + 0.5f);

        /// <summary>A frame along a segment: s along a → b, t to its left (CCW inward for an outline edge).</summary>
        public static StreetShops.Run Frame(float2 a, float2 b)
        {
            float len = math.distance(a, b);
            var dir = len > 1e-4f ? (b - a) / len : new float2(1f, 0f);
            return new StreetShops.Run { A = a, Dir = dir, In = new float2(-dir.y, dir.x), From = 0f, To = len };
        }

        /// <summary>
        /// A row of plots along the line t = <paramref name="t0"/> of a frame, s0 … s1, reaching <paramref name="cells"/> whole
        /// cells away (sign <paramref name="dir"/>). Pieces of up to <paramref name="maxPiece"/> cells, each as deep as fits
        /// (≥ <paramref name="minCells"/>); a piece that fits at no depth is retried cell by cell, so a crossing lane or a
        /// plaza costs one cell of frontage, not a whole piece.
        /// </summary>
        public int Row(StreetShops.Run r, float s0, float s1, float t0, int cells, float dir, string zone, int maxPiece = 3, int minCells = 2)
        {
            if (s1 - s0 < Cell - 0.01f || cells < 1) return 0;
            // Two cells at least (unless the plots are asked one cell deep): CS2 blocks row 0 when row 1 is blocked, so a
            // one-cell plot is zoned two rows deep — into the plot behind it, which then loses its zoning (ZoneGuard).
            minCells = math.min(minCells, cells);
            // The free stretches of the frontage first (1 m probes one cell deep), then pieces spread evenly over each:
            // a crossing lane, a paseo or a neighbour's plot ends a stretch instead of costing a whole piece.
            const float step = 1f;
            float depth = Cell * math.max(1, minCells) - 0.4f;
            bool Open(float s)
            {
                float e = math.min(s1, s + step);
                return Region.Contains(r.At(0.5f * (s + e), t0 + dir * depth * 0.5f)) && FreeFast(Rect(r, s, e, t0 + dir * 0.2f, t0 + dir * depth));
            }
            int made = 0;
            float a = s0;
            while (a < s1 - 0.5f)
            {
                while (a < s1 - 0.5f && !Open(a)) a += step;
                float b = a;
                while (b < s1 - 0.01f && Open(b)) b += step;
                b = math.min(b, s1);
                // (s39) A stretch holds at least two whole columns of any cell grid: the game blocks a lone zoned column
                // between unzoned ones (CellOverlapJobs depth rule, s36).
                if (b - a >= MinStretch) made += Stretch(r, a, b, t0, cells, dir, zone, maxPiece, minCells);
                a = b + step;
            }
            return made;
        }

        private int Stretch(StreetShops.Run r, float s0, float s1, float t0, int cells, float dir, string zone, int maxPiece, int minCells)
        {
            int made = 0;
            float len = s1 - s0;
            // Pieces at least maxPiece cells long: every 8 m of frontage holds one cell centre of the road's block whatever its
            // alignment, so a piece shorter than a cell could hold none (ZoneGuard would drop it).
            int n = math.max(1, (int)math.floor(len / (maxPiece * Cell) + 0.01f));
            // (rf-shops3) A designed row: pieces about maxPiece cells (rounded, not floored: a 45 m stretch is two shops, not one
            // 45 m box), never under two cells, and an anchor at each end of a longer row (AnchorWeight wider than the rest).
            var cut = new float[n + 1];
            if (AnchorWeight > 0f)
            {
                n = math.max(1, math.min((int)math.floor(len / (2f * Cell) + 0.01f), (int)math.round(len / (maxPiece * Cell))));
                float total = 0f;
                // The inline pieces between the anchors keep two cells too (a 68 m row of three gave the middle shop 14.7 m: one
                // zone column, which the game blocks): fewer pieces until they do.
                for (; ; n--)
                {
                    total = 0f;
                    for (int k = 0; k < n; k++) total += n >= 3 && (k == 0 || k == n - 1) ? AnchorWeight : 1f;
                    if (n <= 1 || len / total >= 2f * Cell - 0.01f) break;
                }
                cut = new float[n + 1];
                float acc = 0f;
                for (int k = 0; k <= n; k++)
                {
                    cut[k] = s0 + len * acc / total;
                    if (k < n) acc += n >= 3 && (k == 0 || k == n - 1) ? AnchorWeight : 1f;
                }
            }
            else for (int k = 0; k <= n; k++) cut[k] = math.lerp(s0, s1, (float)k / n);
            for (int k = 0; k < n; k++)
            {
                float a = cut[k], b = cut[k + 1];
                if (Piece(r, a, b, t0, cells, dir, zone, minCells)) { made++; continue; }
                int m = (int)math.floor((b - a) / Cell + 0.01f);
                if (m >= 2)
                {
                    for (int j = 0; j < m; j++)
                        if (Piece(r, math.lerp(a, b, (float)j / m), math.lerp(a, b, (float)(j + 1) / m), t0, cells, dir, zone, minCells)) made++;
                }
                else if (b - a >= Cell + 1f)
                {
                    // A piece of one cell and a bit: one cell at either end may still fit beside what is in the way.
                    if (Piece(r, a, a + Cell, t0, cells, dir, zone, minCells)) made++;
                    else if (Piece(r, b - Cell, b, t0, cells, dir, zone, minCells)) made++;
                }
            }
            return made;
        }

        private bool Piece(StreetShops.Run r, float a, float b, float t0, int cells, float dir, string zone, int minCells)
        {
            for (int d = cells; d >= math.max(1, minCells); d--)
            {
                // (shops) A lane plot whose back reaches a city street's first zone row is killed by the kerb strip there (the
                // retail park's back terrace: 54 cells planned, 14 zoned): it stops a cell short of the street.
                if (t0 != 0f && BacksOntoStreet(r, a, b, t0 + dir * d * Cell)) continue;
                var rect = Rect(r, a, b, t0, t0 + dir * d * Cell);
                if (!Shop(rect)) continue;
                if (!string.IsNullOrEmpty(zone)) ShopZone[rect] = zone;
                return true;
            }
            return false;
        }

        /// <summary>The band (m) beyond a city street's edge that its first zone row (and a kerb strip in it) takes.</summary>
        public const float KerbBand = Cell + 1f;

        /// <summary>
        /// (shops) Does the back edge (t = <paramref name="tBack"/>, s0 … s1) of a lane plot lie within <see cref="KerbBand"/> of a
        /// city street running along it? ZoneGuard.KerbStrips blocks the street's first row along the build, and a plot reaching
        /// into it loses its cells (CleanBlockedCells blocks a column in front of a blocked cell).
        /// </summary>
        public bool BacksOntoStreet(StreetShops.Run r, float s0, float s1, float tBack)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            for (int k = 0; k <= 4; k++)
            {
                var p = r.At(math.lerp(s0, s1, k / 4f), tBack);
                foreach (var road in roads)
                {
                    var dir = math.normalizesafe(road.B - road.A);
                    if (math.abs(math.dot(dir, r.Dir)) < 0.8f) continue;   // only a street along the plot's back
                    if (math.sqrt(SegmentIndex.DistSq(p, road.A, road.B)) < road.HalfWidth + KerbBand) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// (shops) How far in from outline point <paramref name="p"/> a lane plot's back must stay so it is clear of the first zone row
        /// of a city street running along <paramref name="along"/> beside it (see <see cref="BacksOntoStreet"/>); 0 with no such street.
        /// Wide streets (a 4-lane Medium Road) need more than narrow ones.
        /// </summary>
        public float KerbMargin(float2 p, float2 along)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            float m = 0f;
            foreach (var road in roads)
            {
                var dir = math.normalizesafe(road.B - road.A);
                if (math.abs(math.dot(dir, math.normalizesafe(along))) < 0.8f) continue;
                float d = math.sqrt(SegmentIndex.DistSq(p, road.A, road.B));
                if (d > 40f) continue;
                m = math.max(m, road.HalfWidth + KerbBand + 0.5f - d);
            }
            return m;
        }

        /// <summary>
        /// Rows on both sides (or one: side = ±1, 0 = both) of every segment of a lane, from its edge, <paramref name="trimStart"/>
        /// / <paramref name="trimEnd"/> metres kept clear at the lane's ends (a street mouth's gateway, a plaza).
        /// </summary>
        public int LaneRows(Lane lane, int cells, string zone, int side = 0, float trimStart = 0f, float trimEnd = 0f, int maxPiece = 3, int minCells = 2, Func<int, bool> segment = null)
        {
            int made = 0;
            int last = lane.Points.Count - 2;
            for (int i = 0; i <= last; i++)
            {
                // (rf-shops3) Only the segments asked for (a ring drive carries rows on its back and sides, not on its front).
                if (segment != null && !segment(i)) continue;
                var a = lane.Points[i]; var b = lane.Points[i + 1];
                var f = Frame(a, b);
                float s0 = 0f, s1 = f.To;
                // At a bend the next segment's corridor takes the corner square; rows of both segments stop at its edge.
                if (i > 0) s0 += lane.Half;
                if (i < last) s1 -= lane.Half;
                if (i == 0) s0 += trimStart;
                if (i == last) s1 -= trimEnd;
                // At a T or X junction the rows keep clear of the crossing lane by more than the facing tolerance: a corner
                // plot touching both lanes would be zoned by two crossing grids (ZoneGuard keeps the first, the rest of
                // the plot is lost). The corner stays a small planted square.
                if (i == 0 && lane.StartOn != null) s0 += lane.StartOn.Half + JunctionClear;
                if (i == last && lane.EndOn != null) s1 -= lane.EndOn.Half + JunctionClear;
                var cuts = new List<(float a, float b)>();
                foreach (var other in Lanes)
                {
                    if (other == lane) continue;
                    if (other.StartOn == lane) Cut(other.Points[0], other.Half);
                    if (other.EndOn == lane) Cut(other.Points[other.Points.Count - 1], other.Half);
                }
                // (s39) A join (lot stub, footpath) splits the lane into pieces, each with its own cell grid: a plot never
                // straddles one (on a 16 m road the two grids meet at the join with no overlap; a plot across it could keep a
                // lone column of each, which the game blocks).
                foreach (var j in lane.Joins)
                {
                    float u = math.dot(j - a, f.Dir);
                    if (math.abs(math.dot(j - a, f.In)) <= 0.5f && u > 0.5f && u < f.To - 0.5f) cuts.Add((u - 0.25f, u + 0.25f));
                }
                void Cut(float2 p, float half)
                {
                    float u = math.dot(p - a, f.Dir);
                    if (math.abs(math.dot(p - a, f.In)) > 0.5f || u < -half - JunctionClear || u > f.To + half + JunctionClear) return;
                    cuts.Add((u - half - JunctionClear, u + half + JunctionClear));
                }
                cuts.Sort((x, y) => x.a.CompareTo(y.a));
                float from = s0;
                foreach (var (ca, cb) in cuts)
                {
                    made += Both(f, from, math.min(ca, s1));
                    from = math.max(from, cb);
                }
                made += Both(f, from, s1);
            }
            return made;

            int Both(StreetShops.Run f, float u0, float u1)
            {
                if (u1 - u0 < Cell - 0.01f) return 0;
                int m = 0;
                if (side >= 0) m += Row(f, u0, u1, ZoneOffset(lane), cells, 1f, zone, maxPiece, minCells);
                if (side <= 0) m += Row(f, u0, u1, -ZoneOffset(lane), cells, -1f, zone, maxPiece, minCells);
                return m;
            }
        }

        /// <summary>Extra clearance (m) of lane rows from a crossing lane's edge: more than the 4 m facing tolerance.</summary>
        public const float JunctionClear = StreetFrontage.Tolerance + 0.5f;

        /// <summary>
        /// Rows of plots facing existing streets (the runs given), kept a whole cell (<see cref="Gap"/>) away from every lane
        /// of the mall so the street — not the lane — zones them (s26 ranking: the shallowest row wins, ties go to our lanes).
        /// Call after the lane rows: they take the land beside the lanes first.
        /// </summary>
        public int StreetRows(IEnumerable<StreetShops.Run> runs, int cells, string zone, int maxPiece = 3, int minCells = 2, float minLength = 2 * Cell)
        {
            var holds = new List<float2[]>();
            foreach (var lane in Lanes)
                foreach (var c in Corridors(lane)) holds.Add(Hold(GrowConvex(c, Gap - 0.25f)));
            // On a slope a street's plots and the lane plots behind them sit at different grades (each at its own road's):
            // a planted bank between them takes the step (back to back, the step would fall inside a plot — a pit).
            float bank = SlopeGap();
            if (bank > 0f)
                foreach (var s in Shops.ToArray()) holds.Add(Hold(GrowConvex(s, bank - 0.25f)));
            int made = 0;
            foreach (var r in runs)
            {
                // Free stretches only: a row is never cut into one-cell slivers between a lot and a corner.
                float s = r.From;
                while (s < r.To - Cell)
                {
                    float a = s;
                    while (a < r.To && !Free(Rect(r, a, math.min(r.To, a + 2f), 0.2f, Cell))) a += 2f;
                    float b = a;
                    while (b < r.To && Free(Rect(r, b, math.min(r.To, b + 2f), 0.2f, Cell))) b += 2f;
                    b = math.min(b, r.To);
                    if (b - a >= minLength) made += Row(r, a, b, 0f, cells, 1f, zone, maxPiece, minCells);
                    s = b + 2f;
                }
            }
            foreach (var h in holds) Release(h);
            return made;
        }

        private float _slopeGap = -1f;

        /// <summary>
        /// (s32) The bank kept between rows of plots facing different roads: none on level ground, one cell (8 m) where the
        /// site falls more than 2 % (the plane fitted through 25 samples of the original ground). Two rows back to back on
        /// a 6 % slope sit ~2.5 m apart in height; a 1 : 3 bank takes that step outside both plots.
        /// </summary>
        public float SlopeGap()
        {
            if (_slopeGap >= 0f) return _slopeGap;
            var pts = new List<float3>();
            for (int j = 0; j <= 4; j++)
                for (int i = 0; i <= 4; i++)
                {
                    var p = math.lerp(Region.Min, Region.Max, new float2(i / 4f, j / 4f));
                    if (Region.Contains(p)) pts.Add(new float3(p.x, Ctx.H(p), p.y));
                }
            _slopeGap = 0f;
            if (pts.Count < 4) return _slopeGap;
            // Least squares h = a·x + b·z + c.
            double sx = 0, sz = 0, sh = 0, sxx = 0, szz = 0, sxz = 0, sxh = 0, szh = 0; int n = pts.Count;
            foreach (var p in pts) { sx += p.x; sz += p.z; sh += p.y; sxx += p.x * p.x; szz += p.z * p.z; sxz += p.x * p.z; sxh += p.x * p.y; szh += p.z * p.y; }
            double mx = sx / n, mz = sz / n, mh = sh / n;
            double cxx = sxx / n - mx * mx, czz = szz / n - mz * mz, cxz = sxz / n - mx * mz, cxh = sxh / n - mx * mh, czh = szh / n - mz * mh;
            double det = cxx * czz - cxz * cxz;
            if (Math.Abs(det) < 1e-9) return _slopeGap;
            double a = (cxh * czz - czh * cxz) / det, b = (czh * cxx - cxh * cxz) / det;
            if (Math.Sqrt(a * a + b * b) > 0.02) _slopeGap = Gap;
            return _slopeGap;
        }

        /// <summary>A convex ring offset outward by <paramref name="by"/> (mitred corners).</summary>
        public static float2[] GrowConvex(float2[] q, float by)
        {
            var ring = Poly.EnsureCcw(q);
            int n = ring.Length;
            var r = new float2[n];
            for (int i = 0; i < n; i++)
            {
                var p0 = ring[(i + n - 1) % n]; var p1 = ring[i]; var p2 = ring[(i + 1) % n];
                var d1 = math.normalizesafe(p1 - p0); var d2 = math.normalizesafe(p2 - p1);
                var n1 = new float2(d1.y, -d1.x); var n2 = new float2(d2.y, -d2.x);
                float k = 1f + math.dot(n1, n2);
                r[i] = p1 + (k > 0.2f ? (n1 + n2) / k : n1 + n2) * by;
            }
            return r;
        }

        /// <summary>
        /// How far from a street-attached end the lane's rows start: the stretch outside the site plus <paramref name="gate"/>
        /// (the depth of the street's own plots, which keep a gateway strip beside the lane mouth).
        /// </summary>
        public float GateTrim(Lane lane, bool start, float gate)
        {
            if (lane.Points.Count < 2 || (start ? lane.StartAttach : lane.EndAttach) < 0) return 0f;
            var a = start ? lane.Points[0] : lane.Points[lane.Points.Count - 1];
            var b = start ? lane.Points[1] : lane.Points[lane.Points.Count - 2];
            float len = math.distance(a, b);
            var d = (b - a) / math.max(len, 1e-4f);
            float s = 0f;
            while (s < len && !Region.Contains(a + d * s)) s += 0.5f;
            // (shops) Never inside the street's first zone row (the outline sits ~2 m off the kerb): a kerb strip there kills the plot.
            return s + math.max(gate, KerbBand - 2f);
        }

        private readonly Dictionary<float2[], float4> _bounds = new Dictionary<float2[], float4>();

        /// <summary>
        /// <see cref="Free"/> with a bounding-box test first (the row probes call it thousands of times). Taken pieces far
        /// from the probe are skipped; near ones get the same separating-axis test as Free.
        /// </summary>
        public bool FreeFast(float2[] poly)
        {
            float2 mn = poly[0], mx = poly[0];
            foreach (var v in poly) { mn = math.min(mn, v); mx = math.max(mx, v); }
            foreach (var t in Taken)
            {
                if (!_bounds.TryGetValue(t, out var b))
                {
                    float2 tn = t[0], tx = t[0];
                    foreach (var v in t) { tn = math.min(tn, v); tx = math.max(tx, v); }
                    b = new float4(tn, tx);
                    _bounds[t] = b;
                }
                if (b.z < mn.x - 0.1f || b.x > mx.x + 0.1f || b.w < mn.y - 0.1f || b.y > mx.y + 0.1f) continue;
                if (StreetShops.ConvexOverlap(t, poly, 0.05f)) return false;
            }
            return true;
        }

        // ───────────── reservations ─────────────

        /// <summary>Land kept free while rows are planned (gateway gaps at lane mouths); handed back with <see cref="Release"/>.</summary>
        public float2[] Hold(float2[] poly)
        {
            var p = Poly.EnsureCcw(poly);
            Taken.Add(p);
            return p;
        }

        public void Release(float2[] held)
        {
            if (held != null) Taken.Remove(held);
        }

        /// <summary>
        /// A walk (pedestrian path) from <paramref name="a"/> to <paramref name="b"/>; an end on a lane joins it at a node.
        /// Its strip is taken so no plot or tree lands on it. The net is planned now and re-snapped by the grading.
        /// </summary>
        public bool Walk(float2 a, Lane aOn, float2 b, Lane bOn, float half = 1.5f)
        {
            if (aOn != null) a = OnNode(aOn, Project(aOn, a, out _));
            if (bOn != null) b = OnNode(bOn, Project(bOn, b, out _));
            float len = math.distance(a, b);
            if (len < 2f) return false;
            var d = (b - a) / len; var n = new float2(-d.y, d.x);
            // The corridor stops at the lanes' edges (their land is taken already).
            float ea = aOn != null ? aOn.Half + 0.1f : 0f, eb = bOn != null ? bOn.Half + 0.1f : 0f;
            if (len - ea - eb < 1f) return false;
            var strip = Poly.EnsureCcw(new[] { a + d * ea - n * half, b - d * eb - n * half, b - d * eb + n * half, a + d * ea + n * half });
            if (!Free(strip)) return false;
            Taken.Add(strip);
            if (aOn != null) aOn.Joins.Add(a);
            if (bOn != null) bOn.Joins.Add(b);
            Ctx.StraightNet(a, b, NetRole.Pathway, new AssetRef(""));
            Walks.Add((a, b));
            return true;
        }

        /// <summary>
        /// A point where people arrive at a piece: beside an existing street or one of the mall's lanes or walks, and not on
        /// a plot's edge (a walk ending at a plot would end at a shop's back wall — and close the zone cells it touches).
        /// </summary>
        public bool Arrival(float2 p)
        {
            foreach (var s in Shops) if (Poly.DistanceToBoundary(s, p) < 2.5f || Poly.Contains(s, p)) return false;
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            foreach (var r in roads)
                if (math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) <= r.HalfWidth + 6f) return true;
            foreach (var l in Lanes)
                for (int i = 0; i + 1 < l.Points.Count; i++)
                    if (math.sqrt(SegmentIndex.DistSq(p, l.Points[i], l.Points[i + 1])) <= l.Half + 3f) return true;
            foreach (var (a, b) in Walks)
                if (math.sqrt(SegmentIndex.DistSq(p, a, b)) <= 4f) return true;
            return false;
        }

        /// <summary>An existing street runs along a → b (the plot facing test: three samples within its half width + 4 m).</summary>
        public bool StreetAlong(float2 a, float2 b)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            bool Near(float2 p)
            {
                foreach (var r in roads)
                    if (math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) <= r.HalfWidth + StreetFrontage.Tolerance) return true;
                return false;
            }
            return Near(math.lerp(a, b, 0.2f)) && Near(math.lerp(a, b, 0.5f)) && Near(math.lerp(a, b, 0.8f));
        }

        // ───────────── green and paved pieces ─────────────

        /// <summary>
        /// Plans a piece with another type's planner (Fill) whose walks open onto the mall's lanes: each lane door is the
        /// closest point of that lane's centre line; a walk ending at the piece's edge in front of it is stretched onto
        /// it and the lane is split there (identical node), so people walk straight from the lane into the piece.
        /// </summary>
        public bool FillOpen(float2[] ring, string layerId, string preset, IList<(Lane lane, float2 at)> laneDoors, IList<float2> doors, uint salt, Action<SettingsBag> edit = null, bool suggested = true,
            string[] childLayers = null, string[] childTargets = null, List<PlannedRegion> places = null)
        {
            var pts = new List<float2>();
            var map = new List<(Lane lane, float2 p)>();
            float snap = 2.5f;
            if (laneDoors != null)
                foreach (var (lane, at) in laneDoors)
                {
                    if (lane == null) continue;
                    var p = OnNode(lane, Project(lane, at, out _));
                    pts.Add(p);
                    map.Add((lane, p));
                    snap = math.max(snap, lane.Half + 2f);
                }
            if (doors != null) pts.AddRange(doors);
            int before = Ctx.Plan.Nets.Count;
            if (!Fill(ring, layerId, preset, pts, salt, suggested, edit, snap, Arrival, childLayers, childTargets, places)) return false;
            for (int k = before; k < Ctx.Plan.Nets.Count; k++)
            {
                var n = Ctx.Plan.Nets[k];
                if (!PathJoins.IsPath(n)) continue;
                foreach (var (lane, p) in map)
                    if (math.distancesq(n.A.xz, p) < 1e-4f || math.distancesq(n.D.xz, p) < 1e-4f)
                        if (!lane.Joins.Contains(p)) lane.Joins.Add(p);
            }
            KeepWalksOffPlots(before);
            return true;
        }

        /// <summary>
        /// A planner's own walk that dead-ends at a plot's back (it looked for a way out there) is pulled back 3.5 m: a net
        /// touching a zone cell closes it and every cell behind (ZoneGuard / the game), which would cost the plot its zoning.
        /// </summary>
        private void KeepWalksOffPlots(int from)
        {
            var nets = Ctx.Plan.Nets;
            int Degree(float2 p)
            {
                int c = 0;
                foreach (var m in nets) { if (math.distancesq(m.A.xz, p) < 1e-4f) c++; if (math.distancesq(m.D.xz, p) < 1e-4f) c++; }
                return c;
            }
            float Near(float2 p)
            {
                float best = float.MaxValue;
                foreach (var s in Shops) best = math.min(best, Poly.Contains(s, p) ? 0f : Poly.DistanceToBoundary(s, p));
                return best;
            }
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                for (int k = from; k < nets.Count; k++)
                {
                    var n = nets[k];
                    if (!PathJoins.IsPath(n)) continue;
                    float len = math.distance(n.A.xz, n.D.xz);
                    bool endBad = n.EndAttach < 0 && Near(n.D.xz) < 1.5f && Degree(n.D.xz) == 1;
                    bool startBad = !endBad && n.StartAttach < 0 && Near(n.A.xz) < 1.5f && Degree(n.A.xz) == 1;
                    if (!endBad && !startBad) continue;
                    if (len < 5f)
                    {
                        // Too short to pull back: the piece goes (the one before it becomes the dead end, next pass).
                        RemoveNet(k--);
                        changed = true;
                        continue;
                    }
                    // (s48) Pulled back just far enough that the walk's edge clears the cells (the game needs ~1.5 m centre to cell), not a fixed 3.5 m.
                    float f = 3.5f / len;
                    {
                        var tip = endBad ? n.D.xz : n.A.xz; var tail = endBad ? n.A.xz : n.D.xz;
                        float pull = 0f;
                        while (pull < 3.5f && Near(math.lerp(tip, tail, pull / len)) < 1.5f) pull += 0.25f;
                        f = math.min(f, math.max(pull, 0.25f) / len);
                    }
                    if (endBad) { n.D = math.lerp(n.D, n.A, f); n.C = math.lerp(n.C, n.A, f * 0.5f); }
                    else { n.A = math.lerp(n.A, n.D, f); n.B = math.lerp(n.B, n.D, f * 0.5f); }
                    changed = true;
                }
                if (!changed) break;
            }
        }

        /// <summary>Removes a planned net; objects attached to later nets keep their net (indices shift).</summary>
        private void RemoveNet(int idx)
        {
            Ctx.Plan.Nets.RemoveAt(idx);
            foreach (var o in Ctx.Plan.Objects)
            {
                if (o.AttachedNet == idx) o.AttachedNet = -1;
                else if (o.AttachedNet > idx) o.AttachedNet--;
            }
        }

        /// <summary>
        /// (s32) A planted piece: lawn and trees that keep clear of every net and object planned so far — a line of trees down
        /// the middle of a strip (a verge, a gateway beside a lane mouth), a loose grove on a wider piece. Never paths.
        /// </summary>
        public void Green(float2[] ring, uint salt, float spacing = 9f)
        {
            var region = LayerRegion.From(ring);
            if (region == null) return;
            Lawn(ring);
            var reserved = new ReservedSpace();
            Composer.Reserve(Ctx.Plan, reserved);
            var rng = Hashing.Rng(Hashing.Mix(Ctx.Seed, salt), "green");
            region.DeepestPoint(out float clearance, null, 16);
            var placed = new List<float2>();
            bool Try(float2 p, float minEdge)
            {
                if (!region.Contains(p) || region.BoundaryDistance(p, 4f) < minEdge || reserved.Blocks(p, 2.2f)) return false;
                foreach (var q in placed) if (math.distancesq(q, p) < spacing * spacing * 0.8f) return false;
                placed.Add(p);
                Ctx.Object(p, rng.NextFloat(0f, 6.283f), ObjectRole.Tree, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + (uint)placed.Count), 1 + (int)(rng.NextUInt() % 2));
                return true;
            }
            if (clearance < 7f)
            {
                // A strip: one even line along its spine (the long axis of its bounds), as street trees.
                if (clearance < 2.6f) { Take(ring); return; }
                var size = region.Max - region.Min;
                bool alongX = size.x >= size.y;
                float len = alongX ? size.x : size.y;
                int n = math.max(1, (int)math.floor(len / spacing));
                float step = len / n;
                for (int i = 0; i < n; i++)
                {
                    float u = (alongX ? region.Min.x : region.Min.y) + step * (i + 0.5f);
                    // the spine at this station: the deepest point across the strip
                    float2 best = default; float bestD = -1f;
                    float lo = alongX ? region.Min.y : region.Min.x, hi = alongX ? region.Max.y : region.Max.x;
                    for (float v = lo; v <= hi; v += 0.5f)
                    {
                        var p = alongX ? new float2(u, v) : new float2(v, u);
                        if (!region.Contains(p)) continue;
                        float d = region.BoundaryDistance(p, 8f);
                        if (d > bestD) { bestD = d; best = p; }
                    }
                    if (bestD > 0f) Try(best, 2.4f);
                }
            }
            else
            {
                for (float y = region.Min.y + spacing * 0.5f; y < region.Max.y; y += spacing * 0.8f)
                    for (float x = region.Min.x + spacing * 0.5f; x < region.Max.x; x += spacing * 0.8f)
                        Try(new float2(x, y) + new float2(rng.NextFloat(-2f, 2f), rng.NextFloat(-2f, 2f)), 3.2f);
            }
            Take(ring);
        }

        /// <summary>
        /// Where a piece runs along one of the mall's lanes for at least <paramref name="minRun"/> metres: one door per contact,
        /// at its middle (on the lane), so a park or square on the piece opens onto the lane.
        /// </summary>
        public List<(Lane lane, float2 at)> LaneDoors(float2[] piece, float minRun = 12f)
        {
            var list = new List<(Lane, float2)>();
            var region = LayerRegion.From(piece);
            if (region == null) return list;
            foreach (var lane in Lanes)
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                {
                    var a = lane.Points[i]; var b = lane.Points[i + 1];
                    float len = math.distance(a, b);
                    if (len < minRun) continue;
                    var d = (b - a) / len; var n = new float2(-d.y, d.x);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float runStart = -1f;
                        for (float s = 0f; s <= len + 0.01f; s += 2f)
                        {
                            bool on = s < len - 0.01f && region.Contains(a + d * s + n * side * (lane.Half + 1.2f));
                            if (on && runStart < 0f) runStart = s;
                            if (!on && runStart >= 0f)
                            {
                                if (s - runStart >= minRun) list.Add((lane, a + d * (0.5f * (runStart + s))));
                                runStart = -1f;
                            }
                        }
                    }
                }
            return list;
        }

        /// <summary>A paseo (a walk through a row of plots): paved, a lamp beside the walk, planters at both ends.</summary>
        public void DressPaseo(float2[] rect, float2 a, float2 b, uint salt)
        {
            Ctx.Surface(rect, SurfaceRole.Paving, new AssetRef(""), AssetCategories.SurfacePaving);
            float len = math.distance(a, b);
            if (len < 6f) return;
            var d = (b - a) / len; var n = new float2(-d.y, d.x);
            float off = PaseoWidth * 0.5f - 0.7f;
            Ctx.Object(a + d * (len * 0.5f) + n * off, math.atan2(-n.x, -n.y), ObjectRole.Lamp, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt));
            Ctx.Object(a + d * 2.5f - n * off, 0f, ObjectRole.Planter, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + 1));
            Ctx.Object(b - d * 2.5f - n * off, 0f, ObjectRole.Planter, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + 2));
        }

        // ───────────── features on a piece ─────────────

        /// <summary>
        /// Runs an add-on (playground, fountain, kiosk, shops…) on <paramref name="ring"/> as a sub-plan: it sees everything
        /// planned so far as taken (and the plots), places into what is free and is merged. Its warnings stay its own
        /// (an optional feature that finds no room is simply left out).
        /// </summary>
        public bool AddOn(float2[] ring, string layerId, uint salt, Action<SettingsBag> edit = null)
        {
            var gen = GeneratorRegistry.Get(layerId) as LayerBase;
            var region = ring == null ? null : LayerRegion.From(ring);
            if (gen == null || region == null || region.Area < gen.MinArea) return false;
            var settings = gen.Defaults();
            edit?.Invoke(settings);
            var reserved = Ctx.Input.Reserved != null ? Ctx.Input.Reserved.Clone() : new ReservedSpace();
            Composer.Reserve(Ctx.Plan, reserved);
            foreach (var s in Shops) reserved.Polygons.Add(s);
            var input = new AreaInput
            {
                Shape = new AreaShape { Ring = Poly.To3D(ring, Ctx.G), Source = AreaSource.Drawn },
                Site = Ctx.Site, Settings = settings, Seed = Hashing.Mix(Ctx.Seed, salt), RegionTags = new[] { RegionTags.Green },
                Reserved = reserved, Markers = new List<EdgeMarker>(),
            };
            AreaPlan sub;
            try { sub = gen.Plan(input, Ctx.Cancel); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { sub = null; }
            if (sub == null || sub.HasErrors || sub.Objects.Count == 0) return false;
            sub.Regions.Clear();
            sub.Issues.RemoveAll(i => i.Severity != IssueSeverity.Info);
            Merge(sub);
            return true;
        }
    }
}
