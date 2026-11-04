// Placemaker roads layer — connecting the planned network to existing roads.
//
// Adapted from Grid Road Generator (Ti4goc), Systems/GridRoadToolSystem.cs: MakeCoursePos,
// FindInteriorExistingEdges, SplitSegmentsCrossingInteriorEdges and RingCrossings. The ECS parts
// (entities, quadtree, CoursePos) stay in the game layer; here they are expressed through
// SiteContext.NearbyNets / AreaShape.Attachments ExternalIds and PlannedNet.StartAttach/EndAttach.
//
// That file also carries code adapted from CS2-NetworkTools, Copyright (c) Luca Rager,
// MIT License (https://github.com/lucarager/CS2-NetworkTools):
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software
//   and associated documentation files (the "Software"), to deal in the Software without
//   restriction, including without limitation the rights to use, copy, modify, merge, publish,
//   distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions: The above copyright
//   notice and this permission notice shall be included in all copies or substantial portions of
//   the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    /// <summary>An existing road edge the plan may connect to.</summary>
    internal struct ExistingRoad
    {
        public long Id;
        public RoadCurve Curve;
        public float HalfWidth;
        /// <summary>Its place in the hierarchy (highways and ramps: <see cref="RoadRank.Highway"/>).</summary>
        public RoadRank Rank;
    }

    /// <summary>"Connect to": which existing roads the plan may join (the others are barriers it never crosses).</summary>
    public static class ConnectTo
    {
        /// <summary>Every road but highways and their ramps (the default).</summary>
        public const string Streets = "streets";
        /// <summary>Small roads only: two-lane roads, lanes and gravel roads (the big roads keep their junctions few).</summary>
        public const string Small = "small";
        /// <summary>Any road, highways too (the player's explicit choice).</summary>
        public const string Any = "any";

        public static readonly string[] Values = { Streets, Small, Any };

        public static bool Allows(string rule, RoadRank rank)
        {
            if (rule == Any) return true;
            if (rank == RoadRank.Highway) return false;
            return rule != Small || rank <= RoadRank.Minor;
        }
    }

    internal static class RoadConnections
    {
        /// <summary>A generated end this close to a selected/attached existing node joins that node (GRG NodeSnapDistance).</summary>
        public const float NodeSnapDistance = GridGenerator.MinNodeDistance;
        /// <summary>Base distance under which an end joins an existing edge (GRG EdgeSnapDistance).</summary>
        public const float EdgeSnapDistance = GridGenerator.MinNodeDistance;
        /// <summary>
        /// Crossings closer than this to a true end are left to end snapping. Deliberately half the
        /// snap distance so there is no dead zone where a crossing is neither split nor snapped (GRG).
        /// </summary>
        public const float CrossingEndSkip = EdgeSnapDistance * 0.5f;
        /// <summary>Parameters this close to 0/1 mean "the edge's start/end node" rather than a split.</summary>
        public const float EndNodeT = 0.01f;

        public static List<ExistingRoad> Collect(SiteContext site)
        {
            var list = new List<ExistingRoad>();
            if (site?.NearbyNets == null) return list;
            foreach (var n in site.NearbyNets)
            {
                if (n.Category != NetCategory.Road) continue;
                if (!MathEx.Finite(n.A) || !MathEx.Finite(n.B) || !MathEx.Finite(n.C) || !MathEx.Finite(n.D)) continue;
                if (math.distance(n.A.xz, n.D.xz) < 0.1f && math.distance(n.B.xz, n.C.xz) < 0.1f) continue;
                list.Add(new ExistingRoad
                {
                    Id = n.ExternalId,
                    Curve = new RoadCurve(n.A, n.B, n.C, n.D),
                    HalfWidth = n.Width > 0f && math.isfinite(n.Width) ? n.Width * 0.5f : Composer.DefaultHalfWidth(NetRole.Street),
                    Rank = RoadClasses.Rank(n),
                });
            }
            return list;
        }

        /// <summary>
        /// Splits the existing roads into those the plan may join (<paramref name="connect"/> on and the rule allows it) and
        /// the barriers: roads it must neither join nor cross (highways and ramps by default; every road with connecting off).
        /// </summary>
        public static void Partition(List<ExistingRoad> all, bool connect, string rule, out List<ExistingRoad> joinable, out List<ExistingRoad> barriers)
        {
            joinable = new List<ExistingRoad>();
            barriers = new List<ExistingRoad>();
            foreach (var r in all)
                if (connect && ConnectTo.Allows(rule, r.Rank)) joinable.Add(r); else barriers.Add(r);
        }

        /// <summary>
        /// Cuts the planned roads where they would cross a barrier (a highway, a ramp, a road the player keeps free): the piece
        /// within the barrier's corridor (its half width + the road's + 4 m) goes, the two sides end short of it. The free ends
        /// are the later passes' business (linked to the city elsewhere, a turning circle, or gone). Returns the edges cut.
        /// </summary>
        public static int CutAtBarriers(RoadNetwork net, List<ExistingRoad> barriers, System.Func<NetRole, float> halfWidth, System.Func<RoadEdge, bool> which = null)
        {
            if (barriers == null || barriers.Count == 0) return 0;
            var index = BuildIndex(barriers, out _);
            var scratch = new List<int>();
            var seen = new HashSet<int>();
            var pts = new List<float3>();
            var ts = new List<float>();
            int cut = 0, count = net.Edges.Count;
            for (int ei = 0; ei < count; ei++)
            {
                var e = net.Edges[ei];
                if (!e.Alive || (which != null && !which(e))) continue;
                pts.Clear(); ts.Clear();
                e.Curve.Flatten(pts, ts, 2f, 4f);
                var ranges = new List<float2>();
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    float2 a = pts[i].xz, b = pts[i + 1].xz;
                    index.Query(math.min(a, b) - 0.5f, math.max(a, b) + 0.5f, scratch, seen);
                    foreach (int sid in scratch)
                    {
                        if (!Seg.Intersect(a, b, index.A[sid], index.B[sid], out float ta, out _)) continue;
                        var bar = barriers[index.Owner[sid]];
                        float gap = bar.HalfWidth + halfWidth(e.Role) + 4f;
                        float t = math.lerp(ts[i], ts[i + 1], math.saturate(ta));
                        const float step = 1f / 256f;
                        float lo = t, hi = t;
                        while (lo > 0f && bar.Curve.DistanceXZ(e.Curve.Position(lo).xz, out _) < gap) lo -= step;
                        while (hi < 1f && bar.Curve.DistanceXZ(e.Curve.Position(hi).xz, out _) < gap) hi += step;
                        ranges.Add(new float2(math.max(0f, lo), math.min(1f, hi)));
                    }
                }
                // an end lying on (or right beside) a barrier, touching it without crossing: that end goes too
                foreach (var bar in barriers)
                {
                    float gap = bar.HalfWidth + halfWidth(e.Role) + 4f;
                    const float step = 1f / 256f;
                    if (bar.Curve.DistanceXZ(e.Curve.A.xz, out _) < gap)
                    {
                        float t = 0f;
                        while (t < 1f && bar.Curve.DistanceXZ(e.Curve.Position(t).xz, out _) < gap) t += step;
                        ranges.Add(new float2(0f, math.min(1f, t)));
                    }
                    if (bar.Curve.DistanceXZ(e.Curve.D.xz, out _) < gap)
                    {
                        float t = 1f;
                        while (t > 0f && bar.Curve.DistanceXZ(e.Curve.Position(t).xz, out _) < gap) t -= step;
                        ranges.Add(new float2(math.max(0f, t), 1f));
                    }
                }
                if (ranges.Count == 0) continue;
                ranges.Sort((x, y) => x.x.CompareTo(y.x));
                var keep = new List<float2>();
                float from = 0f;
                foreach (var r in ranges)
                {
                    if (r.x > from) keep.Add(new float2(from, r.x));
                    from = math.max(from, r.y);
                }
                if (from < 1f) keep.Add(new float2(from, 1f));
                var original = e.Curve;
                bool cap = e.CapAtEnd;
                keep.RemoveAll(k => original.Sub(k.x, k.y).LengthXZ(8) < 2f);
                cut++;
                if (keep.Count == 0) { e.Removed = true; continue; }
                for (int k = 0; k < keep.Count; k++)
                {
                    var piece = original.Sub(keep[k].x, keep[k].y);
                    bool tip = cap && keep[k].y >= 1f;
                    if (k == 0) { e.Curve = piece; e.CapAtEnd = tip; }
                    else { var copy = e.Clone(piece); copy.CapAtEnd = tip; net.Edges.Add(copy); }
                }
            }
            return cut;
        }

        /// <summary>Removes every alive piece crossing a barrier (the safety net after the passes that add walks and lanes).</summary>
        public static int DropBarrierCrossings(RoadNetwork net, List<ExistingRoad> barriers, System.Func<RoadEdge, bool> which = null)
        {
            if (barriers == null || barriers.Count == 0) return 0;
            int removed = 0;
            foreach (var e in net.Edges)
            {
                if (!e.Alive || (which != null && !which(e))) continue;
                if (!CrossesAny(RoadGraph.Sample(e.Curve, 3f), barriers, 1f)) continue;
                e.Removed = true;
                removed++;
            }
            return removed;
        }

        /// <summary>True when the polyline crosses one of the roads anywhere but within <paramref name="endSkip"/> m of its ends.</summary>
        public static bool CrossesAny(IReadOnlyList<float2> line, List<ExistingRoad> roads, float endSkip = 0.5f)
        {
            if (roads == null || roads.Count == 0 || line.Count < 2) return false;
            var pts = new List<float3>();
            float2 first = line[0], last = line[line.Count - 1];
            foreach (var r in roads)
            {
                pts.Clear();
                r.Curve.Flatten(pts, null, 4f, 6f);
                for (int k = 0; k + 1 < pts.Count; k++)
                    for (int i = 0; i + 1 < line.Count; i++)
                        if (Seg.Intersect(line[i], line[i + 1], pts[k].xz, pts[k + 1].xz, out float ta, out _))
                        {
                            float2 hit = math.lerp(line[i], line[i + 1], ta);
                            if (math.distance(hit, first) > endSkip && math.distance(hit, last) > endSkip) return true;
                        }
            }
            return false;
        }

        /// <summary>
        /// (Smarter roads) Where a planned road runs ON an existing road for part of its length (the grid line laid along a
        /// spine, a street over an existing stub), that part goes and the rest ends on the existing road: on its node when one
        /// is within 12 m, else on the road itself. Whole pieces on top of a road are the overlap pass's. Returns the trims.
        /// </summary>
        public static int TrimOverlaps(RoadNetwork net, List<ExistingRoad> existing, System.Func<NetRole, float> halfWidth)
        {
            if (existing == null || existing.Count == 0) return 0;
            int trimmed = 0, count = net.Edges.Count;
            for (int ei = 0; ei < count; ei++)
            {
                var e = net.Edges[ei];
                if (!e.Alive || !e.IsCar || e.Role == NetRole.PerimeterRoad) continue;
                float len = e.Curve.LengthXZ(12);
                if (len < 12f) continue;
                int n = math.clamp((int)math.ceil(len / 2f), 6, 400);
                var on = new bool[n + 1];
                var road = new int[n + 1];
                float hw = halfWidth(e.Role);
                int onCount = 0;
                for (int i = 0; i <= n; i++)
                {
                    float t = (float)i / n;
                    float2 p = e.Curve.Position(t).xz;
                    float2 dir = e.Curve.TangentXZ(t);
                    road[i] = -1;
                    for (int r = 0; r < existing.Count; r++)
                    {
                        float d = existing[r].Curve.DistanceXZ(p, out float rt);
                        if (d > (hw + existing[r].HalfWidth) * 0.6f) continue;
                        if (math.abs(math.dot(dir, existing[r].Curve.TangentXZ(rt))) < 0.94f || !Beside(existing[r].Curve, p, rt)) continue;
                        on[i] = true; road[i] = r; onCount++;
                        break;
                    }
                }
                if (onCount == 0 || onCount > n * 0.8f) continue;   // nothing on a road, or a whole piece on one (RemoveOverlapping)
                // Runs of samples on a road at least 8 m long, cut out.
                var cuts = new List<float2>();
                int i0 = -1;
                for (int i = 0; i <= n + 1; i++)
                {
                    bool o = i <= n && on[i];
                    if (o && i0 < 0) i0 = i;
                    if (!o && i0 >= 0)
                    {
                        float a = (float)i0 / n, b = (float)(i - 1) / n;
                        if ((b - a) * len >= 8f) cuts.Add(new float2(a, b));
                        i0 = -1;
                    }
                }
                if (cuts.Count == 0) continue;
                var keep = new List<float2>();
                float from = 0f;
                foreach (var c in cuts)
                {
                    if (c.x > from + 1e-4f) keep.Add(new float2(from, c.x));
                    from = c.y;
                }
                if (from < 1f - 1e-4f) keep.Add(new float2(from, 1f));
                var original = e.Curve;
                bool cap = e.CapAtEnd;
                keep.RemoveAll(k => original.Sub(k.x, k.y).LengthXZ(8) < 6f);
                trimmed++;
                if (keep.Count == 0) { e.Removed = true; continue; }
                for (int k = 0; k < keep.Count; k++)
                {
                    var piece = original.Sub(keep[k].x, keep[k].y);
                    // the cut ends land on the existing road (its node when one is near)
                    if (keep[k].x > 0f) piece = piece.WithEnds(OntoRoad(existing, piece.A), piece.D);
                    if (keep[k].y < 1f) piece = piece.WithEnds(piece.A, OntoRoad(existing, piece.D));
                    bool tip = cap && keep[k].y >= 1f;
                    if (k == 0) { e.Curve = piece; e.CapAtEnd = tip; }
                    else { var copy = e.Clone(piece); copy.CapAtEnd = tip; net.Edges.Add(copy); }
                }
            }
            return trimmed;
        }

        /// <summary>The point of the nearest existing road to p: an end node of it when within 12 m, else the closest point.</summary>
        private static float3 OntoRoad(List<ExistingRoad> existing, float3 p)
        {
            float best = float.MaxValue;
            float3 q = p;
            foreach (var r in existing)
            {
                float d = r.Curve.DistanceXZ(p.xz, out float t);
                if (d >= best) continue;
                best = d;
                float len = r.Curve.LengthXZ(8);
                q = t * len < 12f ? r.Curve.A : (1f - t) * len < 12f ? r.Curve.D : r.Curve.Position(t);
            }
            return best < 30f ? q : p;
        }

        /// <summary>
        /// (Smarter roads) Interior streets running alongside an existing road closer than a building plot (centre lines within
        /// both half widths + 20 m for most of their length) would leave a sliver between the two: the existing road already
        /// fronts that land, the street goes. Perimeter pieces have their own rule (RemoveOverlapping). Returns the count.
        /// </summary>
        public static int RemoveAlongside(RoadNetwork net, List<ExistingRoad> existing, System.Func<NetRole, float> halfWidth)
        {
            if (existing == null || existing.Count == 0) return 0;
            int removed = 0;
            foreach (var e in net.Edges)
            {
                if (!e.Alive || !e.IsCar || e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Roundabout || e.Connector) continue;
                float len = e.Curve.LengthXZ(12);
                if (len < 20f) continue;
                float hw = halfWidth(e.Role);
                int close = 0;
                const int samples = 9;
                for (int i = 1; i <= samples; i++)
                {
                    float t = i / (samples + 1f);
                    float2 p = e.Curve.Position(t).xz;
                    float2 dir = e.Curve.TangentXZ(t);
                    foreach (var r in existing)
                    {
                        float d = r.Curve.DistanceXZ(p, out float rt);
                        if (d > hw + r.HalfWidth + 20f) continue;
                        if (math.abs(math.dot(dir, r.Curve.TangentXZ(rt))) < 0.9f || !Beside(r.Curve, p, rt)) continue;
                        close++;
                        break;
                    }
                }
                if (close >= 7) { e.Removed = true; removed++; }
            }
            return removed;
        }

        /// <summary>
        /// Is p beside the road (its nearest point at parameter <paramref name="t"/>), not out beyond one of its ends? A street
        /// continuing an existing road in line with it is not "alongside" it.
        /// </summary>
        public static bool Beside(RoadCurve road, float2 p, float t)
        {
            if (t > 0.002f && t < 0.998f) return true;
            bool end = t >= 0.5f;
            float2 e = end ? road.D.xz : road.A.xz;
            float2 outward = end ? road.TangentXZ(1f) : -road.TangentXZ(0f);
            return math.dot(p - e, outward) <= 1f;
        }

        /// <summary>Snap distance for an existing road: wide roads catch ends that stop at their curb.</summary>
        public static float SnapDistance(ExistingRoad r) => math.max(EdgeSnapDistance, r.HalfWidth + 2f);

        /// <summary>
        /// Splits generated edges where they cross an existing road away from their ends, so each piece
        /// ends exactly on the road and connects there (GRG SplitSegmentsCrossingInteriorEdges). Curves
        /// are split exactly (de Casteljau) — GRG had to leave arcs unsplit.
        /// </summary>
        public static void SplitAtExisting(RoadNetwork net, List<ExistingRoad> existing)
        {
            if (existing.Count == 0) return;
            var index = BuildIndex(existing, out var chordTs);
            var scratch = new List<int>();
            var seen = new HashSet<int>();
            var pts = new List<float3>();
            var ts = new List<float>();
            var hits = new List<float>();
            int count = net.Edges.Count;
            for (int ei = 0; ei < count; ei++)
            {
                var e = net.Edges[ei];
                if (!e.Alive) continue;
                pts.Clear(); ts.Clear(); hits.Clear();
                e.Curve.Flatten(pts, ts, 2f, 4f);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    float2 a = pts[i].xz, b = pts[i + 1].xz;
                    index.Query(math.min(a, b) - 0.5f, math.max(a, b) + 0.5f, scratch, seen);
                    foreach (int sid in scratch)
                    {
                        if (!Seg.Intersect(a, b, index.A[sid], index.B[sid], out float ta, out _)) continue;
                        float t = math.lerp(ts[i], ts[i + 1], math.saturate(ta));
                        hits.Add(t);
                    }
                }
                if (hits.Count == 0) continue;
                hits.Sort();
                float3 start = e.Curve.A, end = e.Curve.D;
                var accepted = new List<float>();
                float3 last = start;
                foreach (float t in hits)
                {
                    float3 p = e.Curve.Position(t);
                    if (math.distance(p.xz, start.xz) < CrossingEndSkip || math.distance(p.xz, end.xz) < CrossingEndSkip) continue;
                    if (math.distance(p.xz, last.xz) < GridGenerator.MinSegmentLength) continue;
                    accepted.Add(t);
                    last = p;
                }
                if (accepted.Count == 0) continue;
                float prev = 0f;
                var original = e.Curve;
                bool cap = e.CapAtEnd;
                for (int k = 0; k <= accepted.Count; k++)
                {
                    float t1 = k < accepted.Count ? accepted[k] : 1f;
                    var piece = original.Sub(prev, t1);
                    if (k == 0)
                    {
                        e.Curve = piece;
                        // Only the last piece keeps the cul-de-sac tip; a mid-street crossing is never one.
                        e.CapAtEnd = false;
                    }
                    else
                    {
                        var copy = e.Clone(piece);
                        copy.CapAtEnd = k == accepted.Count && cap;
                        net.Edges.Add(copy);
                    }
                    prev = t1;
                }
            }
        }

        /// <summary>
        /// Points where existing roads cross the outline: forced nodes of the perimeter road so it
        /// joins those roads instead of passing over them (GRG RingCrossings). Also takes the game's
        /// RoadCrossing attachments when it reports them directly.
        /// </summary>
        public static List<float3> RingCrossings(IReadOnlyList<float3> ring, List<ExistingRoad> existing, List<BoundaryAttachment> attachments)
        {
            var result = new List<float3>();
            int n = ring.Count;
            var pts = new List<float3>();
            foreach (var r in existing)
            {
                pts.Clear();
                r.Curve.Flatten(pts, null, 4f, 6f);
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    float2 c = pts[k].xz, d = pts[k + 1].xz;
                    for (int i = 0; i < n; i++)
                    {
                        float2 a = ring[i].xz, b = ring[(i + 1) % n].xz;
                        if (Seg.Intersect(a, b, c, d, out float t, out float u) && t > 0f && t < 1f && u >= 0f && u < 1f)
                        {
                            float2 hit = math.lerp(a, b, t);
                            AddUnique(result, new float3(hit.x, math.lerp(ring[i].y, ring[(i + 1) % n].y, t), hit.y), 2f);
                        }
                    }
                }
            }
            if (attachments != null)
                foreach (var a in attachments)
                    if (a.Kind == AttachmentKind.RoadCrossing && MathEx.Finite(a.Position))
                        AddUnique(result, a.Position, 2f);
            return result;
        }

        private static void AddUnique(List<float3> list, float3 p, float tolerance)
        {
            foreach (var q in list) if (math.distance(q.xz, p.xz) < tolerance) return;
            list.Add(p);
        }

        /// <summary>
        /// Marks generated edges that run along an existing road (most of their length inside the two
        /// road bodies, roughly parallel) as removed: the road is already there. Typical case: a drawn
        /// area whose outline follows a street — its perimeter road would be laid on top of it.
        /// </summary>
        public static int RemoveOverlapping(RoadNetwork net, List<ExistingRoad> existing, System.Func<NetRole, float> halfWidth)
        {
            if (existing.Count == 0) return 0;
            int removed = 0;
            foreach (var e in net.Edges)
            {
                if (!e.Alive) continue;
                float hw = halfWidth(e.Role);
                // (s34) A perimeter piece running alongside an existing road (closer than a frontage row) only doubles it:
                // the playtest's corner leftovers hugged the street and turned the grid's last street into a hairpin.
                float reach = e.Role == NetRole.PerimeterRoad ? 1f : 0.8f;
                float extra = e.Role == NetRole.PerimeterRoad ? 14f : 0f;
                int need = e.Role == NetRole.PerimeterRoad ? 4 : 5;
                int inside = 0;
                const int samples = 7;
                for (int i = 1; i <= samples; i++)
                {
                    float t = i / (samples + 1f);
                    float2 p = e.Curve.Position(t).xz;
                    float2 dir = e.Curve.TangentXZ(t);
                    foreach (var r in existing)
                    {
                        float d = r.Curve.DistanceXZ(p, out float rt);
                        if (d > (r.HalfWidth + hw) * reach + extra) continue;
                        if (math.abs(math.dot(dir, r.Curve.TangentXZ(rt))) < (extra > 0f ? 0.7f : 0.85f) || !Beside(r.Curve, p, rt)) continue;
                        inside++;
                        break;
                    }
                }
                if (inside >= need)
                {
                    e.Removed = true;
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// Decides for every node whether it joins an existing node or edge (GRG MakeCoursePos rules
        /// 1 and 2) and moves it exactly onto that point. Rule 3 (free end) leaves the node alone.
        /// </summary>
        public static int Attach(RoadNetwork net, List<ExistingRoad> existing, List<BoundaryAttachment> attachments)
        {
            int attached = 0;
            int[] degree = net.Degrees(true);
            for (int ni = 0; ni < net.Nodes.Count; ni++)
            {
                if (degree[ni] == 0) continue;
                var node = net.Nodes[ni];
                float2 p = node.Position.xz;
                bool done = false;

                // 1) An existing node the game reported on the outline (block/perimeter pick).
                if (attachments != null)
                {
                    float best = NodeSnapDistance;
                    foreach (var a in attachments)
                    {
                        if (a.Kind != AttachmentKind.RoadNode || !MathEx.Finite(a.Position)) continue;
                        float d = math.distance(a.Position.xz, p);
                        if (d < best)
                        {
                            best = d;
                            node.Attach = a.ExternalId;
                            node.AttachT = 0f;
                            node.Position = a.Position;
                            done = true;
                        }
                    }
                }

                // 2) An existing edge: split it there, or use its end node when the point is at an end.
                if (!done)
                {
                    float best = float.MaxValue;
                    foreach (var r in existing)
                    {
                        float d = r.Curve.DistanceXZ(p, out float t);
                        if (d >= SnapDistance(r) || d >= best) continue;
                        best = d;
                        if (t <= EndNodeT) t = 0f; else if (t >= 1f - EndNodeT) t = 1f;
                        node.Attach = r.Id;
                        node.AttachT = t;
                        node.Position = t == 0f ? r.Curve.A : t == 1f ? r.Curve.D : r.Curve.Position(t);
                        done = true;
                    }
                }
                if (done) attached++;
            }
            // Move curve ends onto the snapped nodes.
            foreach (var e in net.Edges)
            {
                if (e.Discarded) continue;
                e.Curve = e.Curve.WithEnds(net.Nodes[e.N0].Position, net.Nodes[e.N1].Position);
            }
            return attached;
        }

        private static SegmentIndex BuildIndex(List<ExistingRoad> existing, out List<float> chordTs)
        {
            chordTs = new List<float>();
            var index = new SegmentIndex(32f);
            var pts = new List<float3>();
            var ts = new List<float>();
            for (int r = 0; r < existing.Count; r++)
            {
                pts.Clear(); ts.Clear();
                existing[r].Curve.Flatten(pts, ts, 2f, 4f);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    index.Add(pts[i].xz, pts[i + 1].xz, r);
                    chordTs.Add(ts[i]);
                }
            }
            return index;
        }
    }
}
