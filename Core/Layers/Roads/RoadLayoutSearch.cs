// Placemaker roads layer (smarter roads) — where the grid goes: its frame and its phase, chosen to fit the network that
// is already there and the land.
//
//   spine     a straight existing road through the area (an avenue first, a big road, a straight small road last) turns
//             the grid to it: one row lies on the road itself (that row is the existing road, the planner drops it) and
//             the streets meet it square.
//   stubs     an existing street ending at the area, or a T whose free side faces it, wants a grid street to continue it:
//             the columns / rows are shifted (and their spacing stretched up to 12 %) so as many as possible line up.
//   choice    every candidate (the outline frame, the spine frame; even or anchored lines) is generated (cheap) and
//             costed: steep pieces, awkward crossings with existing roads, streets hugging one, streets meeting the
//             outline at a knife edge, stubs left unaligned. The cheapest wins; ties keep the outline frame.
//   avenue    a generated avenue never lies on a smaller existing road (that road would become part of an avenue); with a
//             spine on the rows, the avenue row is the spine itself.
//
// The player's own choices stay: an angle, "align to terrain" or Follow contours lock the frame (only the phase moves);
// "count" mode keeps its even spread.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadLayoutSearch
    {
        public sealed class Result
        {
            public GridParameters Params;
            /// <summary>"outline" | "spine" | "terrain".</summary>
            public string Frame = "outline";
            public bool Spine;
            public float2 SpinePoint, SpineDir;
            public RoadRank SpineRank;
            /// <summary>1: a row lies on the spine, 2: a column.</summary>
            public int SpineAxis;
            public int StubsAligned, Stubs, Candidates;
            public float Cost;
        }

        private struct Anchor
        {
            public float2 P;      // a point of the existing street the grid line should continue
            public float2 Dir;    // its direction (either sign)
            public float W;
        }

        private sealed class XNode
        {
            public float2 P;
            public readonly List<(float2 dir, int road)> Out = new List<(float2, int)>();
        }

        private const float StubReach = 100f;
        /// <summary>Test hook: one line per candidate (the road lab's --trace).</summary>
        [ThreadStatic] internal static Action<string> Trace;
        private const float AlignTol = 4f;

        /// <summary>The cheapest candidate over every frame (one plan: the planner's own pick when it does not compare in full).</summary>
        public static Result ForGrid(IReadOnlyList<float3> ring, GridParameters gp, List<ExistingRoad> joinable, List<ExistingRoad> all,
            Func<float2, float> height, float maxGrade, bool frameFree, Func<NetRole, float> hw, CancellationToken cancel)
        {
            var frames = Frames(ring, gp, joinable, all, height, maxGrade, frameFree, hw, cancel);
            Result best = null;
            foreach (var r in frames) if (best == null || r.Cost < best.Cost - 0.5f) best = r;
            return best ?? new Result { Params = gp };
        }

        /// <summary>
        /// The best candidate of each frame (the outline's, the spine's when an existing straight road crosses the area): the
        /// planner plans each in full and the rating picks (RoadsPlanner.Plan). The outline frame comes first.
        /// </summary>
        /// <summary>Existing junctions (three roads or more) of the network the plan joins.</summary>
        private static List<float2> Junctions(List<XNode> nodes)
        {
            var list = new List<float2>();
            foreach (var n in nodes) if (n.Out.Count >= 3) list.Add(n.P);
            return list;
        }

        public static List<Result> Frames(IReadOnlyList<float3> ring, GridParameters gp, List<ExistingRoad> joinable, List<ExistingRoad> all,
            Func<float2, float> height, float maxGrade, bool frameFree, Func<NetRole, float> hw, CancellationToken cancel)
        {
            var results = new List<Result>();
            var polygon = new List<float2>(ring.Count);
            foreach (var p in ring) if (polygon.Count == 0 || math.distance(polygon[polygon.Count - 1], p.xz) > 1e-3f) polygon.Add(p.xz);
            if (polygon.Count >= 2 && math.distance(polygon[0], polygon[polygon.Count - 1]) <= 1e-3f) polygon.RemoveAt(polygon.Count - 1);
            if (polygon.Count < 3) { results.Add(new Result { Params = gp }); return results; }
            var poly = polygon.ToArray();
            bool spacing = gp.Mode == SpacingMode.FixedSpacing && gp.SpacingMeters > 0.5f;

            var nodes = Nodes(joinable);
            var anchors = Stubs(nodes, joinable, poly);
            var spine = FindSpine(joinable, nodes, poly, out float2 spinePoint, out float2 spineDir, out RoadRank spineRank);
            // Branch off the spine's existing junctions: a cross street of the grid passes through each (its side roads
            // continue as grid streets), so no grid junction lands a few metres beside one.
            if (spine)
            {
                float2 nrm = new float2(-spineDir.y, spineDir.x);
                foreach (var n in nodes)
                {
                    if (n.Out.Count < 3) continue;
                    if (math.abs(math.dot(n.P - spinePoint, nrm)) > 6f) continue;   // not on the spine's line
                    if (!Poly.Contains(poly, n.P) && Poly.DistanceToBoundary(poly, n.P) > 60f) continue;
                    foreach (var (dir, _) in n.Out)
                        if (math.abs(math.dot(dir, spineDir)) < 0.42f) { anchors.Add(new Anchor { P = n.P, Dir = nrm, W = 1.5f }); break; }
                }
            }

            // frames: (name, origin, u axis, which lines must lie on the spine: 0 none, 1 a row, 2 a column)
            var frames = new List<(string name, float2 origin, float2 u, int must)>();
            var def = GridGenerator.DefaultFrame(ring, gp);
            float along = spine ? math.abs(math.dot(spineDir, def.u)) : 0f;
            bool sameAxes = spine && (along >= 0.9986f || along <= 0.0523f);   // within 3 degrees of the frame's axes
            if (sameAxes)
                // the outline frame already runs along the spine: only its phase moves (a row, or a column, on the spine)
                frames.Add(("spine", spinePoint, def.u, along >= 0.9986f ? 1 : 2));
            else
            {
                frames.Add((gp.AlignToTerrain ? "terrain" : "outline", def.origin, def.u, 0));
                if (frameFree && spine) frames.Add(("spine", spinePoint, spineDir, 1));
            }

            foreach (var (name, origin, u, must) in frames)
            {
                cancel.ThrowIfCancellationRequested();
                var res = new Result { Params = gp, Frame = name, Stubs = anchors.Count, Cost = float.MaxValue };
                float2 v = new float2(-u.y, u.x);
                var cands = new List<(GridParameters p, int aligned)>();
                var p0 = gp;
                p0.UseFrame = true; p0.FrameOrigin = origin; p0.FrameU = u;
                p0.AnchoredU = p0.AnchoredV = false; p0.SpacingU = p0.SpacingV = 0f;
                int alignedHere = 0;
                bool mustRow = must == 1, mustCol = must == 2;
                if (spacing)
                {
                    Bounds(poly, origin, u, v, out float2 lmin, out float2 lmax);
                    var colAnchors = new List<(float a, float w)>();
                    var rowAnchors = new List<(float a, float w)>();
                    foreach (var an in anchors)
                    {
                        if (math.abs(math.dot(an.Dir, v)) >= 0.978f) colAnchors.Add((math.dot(an.P - origin, u), an.W));
                        else if (math.abs(math.dot(an.Dir, u)) >= 0.978f) rowAnchors.Add((math.dot(an.P - origin, v), an.W));
                    }
                    if (mustRow) rowAnchors.Add((0f, 6f));   // the spine passes through the origin along u
                    if (mustCol) colAnchors.Add((0f, 6f));   // ... or along v
                    var pa = p0;
                    float su = Phase(lmin.x, lmax.x, gp.SpacingMeters, colAnchors, mustCol, out float au, out int alU);
                    float sv = Phase(lmin.y, lmax.y, gp.SpacingMeters, rowAnchors, mustRow, out float av, out int alV);
                    if (alU > 0) { pa.AnchoredU = true; pa.AnchorU = au; pa.SpacingU = su; }
                    if (alV > 0) { pa.AnchoredV = true; pa.AnchorV = av; pa.SpacingV = sv; }
                    alignedHere = alU + alV - (mustRow && alV > 0 ? 1 : 0) - (mustCol && alU > 0 ? 1 : 0);
                    if (pa.AnchoredU || pa.AnchoredV) cands.Add((pa, alignedHere));
                    if (must == 0 || cands.Count == 0) cands.Add((p0, 0));
                    // An existing road through the area: the grid's phase decides where its junctions fall on that road (never
                    // a grid crossing a few metres off it). Quarter-spacing shifts of the free axes are tried too.
                    if (CrossesInterior(all, poly))
                    {
                        var basis = cands[0].p;
                        int baseAligned = cands[0].aligned;
                        GridGenerator.LinePositions(polygon, basis, out _, out _, out _, out _, out var up, out var vp);
                        float s0u = basis.SpacingU > 0.5f ? basis.SpacingU : gp.SpacingMeters, s0v = basis.SpacingV > 0.5f ? basis.SpacingV : gp.SpacingMeters;
                        foreach (float f in new[] { 0.25f, 0.5f, 0.75f })
                        {
                            if (!basis.AnchoredU && up.Count > 0)
                            {
                                var q = basis; q.AnchoredU = true; q.AnchorU = up[0] + f * s0u; q.SpacingU = s0u;
                                cands.Add((q, baseAligned));
                            }
                            if (!basis.AnchoredV && vp.Count > 0)
                            {
                                var q = basis; q.AnchoredV = true; q.AnchorV = vp[0] + f * s0v; q.SpacingV = s0v;
                                cands.Add((q, baseAligned));
                            }
                        }
                    }
                }
                else cands.Add((p0, 0));
                foreach (var (c, aligned) in cands)
                {
                    float cost = Cost(ring, poly, c, joinable, all, height, maxGrade, hw, Junctions(nodes));
                    cost -= 6f * aligned;
                    Trace?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture, "layout {0} anchoredU={1} anchoredV={2} aligned={3} cost={4:0.0} spine={5}",
                        name, c.AnchoredU, c.AnchoredV, aligned, cost, spine));
                    if (cost < res.Cost - 0.5f)
                    {
                        res.Params = c;
                        res.StubsAligned = aligned;
                        res.Cost = cost;
                    }
                }
                res.Spine = name == "spine";
                res.SpineAxis = must;
                res.SpinePoint = spinePoint; res.SpineDir = spineDir; res.SpineRank = spineRank;
                FixAvenues(ref res.Params, polygon, joinable, res);
                results.Add(res);
            }
            return results;
        }

        // ───────────────────────────── existing network ─────────────────────────────

        private static List<XNode> Nodes(List<ExistingRoad> roads)
        {
            var nodes = new List<XNode>();
            int At(float2 p)
            {
                for (int i = 0; i < nodes.Count; i++) if (math.distance(nodes[i].P, p) <= 1f) return i;
                nodes.Add(new XNode { P = p });
                return nodes.Count - 1;
            }
            for (int r = 0; r < roads.Count; r++)
            {
                var c = roads[r].Curve;
                nodes[At(c.A.xz)].Out.Add((c.TangentXZ(0f), r));
                nodes[At(c.D.xz)].Out.Add((-c.TangentXZ(1f), r));
            }
            return nodes;
        }

        /// <summary>
        /// Existing streets that end at the area or meet a road beside it with their far side free: a grid street through the
        /// node along that street continues it. Dead ends weigh most; a T's stem as much; a crossing's arms a little.
        /// </summary>
        private static List<Anchor> Stubs(List<XNode> nodes, List<ExistingRoad> roads, float2[] poly)
        {
            var list = new List<Anchor>();
            foreach (var n in nodes)
            {
                bool inside = Poly.Contains(poly, n.P);
                if (!inside && Poly.DistanceToBoundary(poly, n.P) > StubReach) continue;
                foreach (var (dir, r) in n.Out)
                {
                    if (roads[r].Rank == RoadRank.Highway) continue;
                    // the far side (−dir) must be free at this node
                    bool occupied = false;
                    foreach (var (d2, r2) in n.Out) if (r2 != r && math.dot(d2, -dir) > 0.94f) occupied = true;
                    if (occupied) continue;
                    // continuing the street (−dir) must lead into the area
                    if (!inside && !RayHits(poly, n.P, -dir, StubReach)) continue;
                    float w = n.Out.Count == 1 ? 1.2f : n.Out.Count == 3 ? 1f : 0.6f;
                    list.Add(new Anchor { P = n.P, Dir = dir, W = w });
                }
            }
            return list;
        }

        private static bool RayHits(float2[] poly, float2 p, float2 dir, float reach)
        {
            float2 q = p + dir * reach;
            for (int i = 0; i < poly.Length; i++)
                if (Seg.Intersect(p, q, poly[i], poly[(i + 1) % poly.Length], out _, out _)) return true;
            return false;
        }

        /// <summary>
        /// The straightest, biggest existing road through the area: runs of roads continuing straight at their nodes, their
        /// part inside the outline at least 100 m and a third of the area's extent that way, within 5 m (1.2 %) of a line.
        /// </summary>
        private static bool FindSpine(List<ExistingRoad> roads, List<XNode> nodes, float2[] poly, out float2 point, out float2 dir, out RoadRank rank)
        {
            point = default; dir = new float2(1f, 0f); rank = RoadRank.Lane;
            if (roads.Count == 0) return false;
            // runs: union of roads continuing straight through a node
            var parent = new int[roads.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var n in nodes)
                for (int a = 0; a < n.Out.Count; a++)
                    for (int b = a + 1; b < n.Out.Count; b++)
                        if (math.dot(n.Out[a].dir, n.Out[b].dir) < -0.978f && roads[n.Out[a].road].Rank == roads[n.Out[b].road].Rank)
                        {
                            int ra = Find(n.Out[a].road), rb = Find(n.Out[b].road);
                            if (ra != rb) parent[math.max(ra, rb)] = math.min(ra, rb);
                        }
            var runs = new Dictionary<int, List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                int r = Find(i);
                if (!runs.TryGetValue(r, out var l)) runs[r] = l = new List<int>();
                l.Add(i);
            }
            float bestScore = 0f;
            bool found = false;
            foreach (var run in runs.Values)
            {
                var pts = new List<float2>();
                float inside = 0f;
                RoadRank rr = RoadRank.Lane;
                foreach (int ri in run)
                {
                    if (roads[ri].Rank == RoadRank.Highway) { inside = 0f; break; }
                    if (roads[ri].Rank > rr) rr = roads[ri].Rank;
                    var line = new List<float2>();
                    var f = new List<float3>();
                    roads[ri].Curve.Flatten(f, null, 4f, 4f);
                    foreach (var p in f) line.Add(p.xz);
                    foreach (var piece in Poly.ClipPolylineToPolygon(poly, line))
                        for (int k = 0; k < piece.Length; k++)
                        {
                            pts.Add(piece[k]);
                            if (k > 0) inside += math.distance(piece[k - 1], piece[k]);
                        }
                }
                if (inside < 100f || pts.Count < 2) continue;
                // principal axis of the inside points
                float2 mean = float2.zero;
                foreach (var p in pts) mean += p;
                mean /= pts.Count;
                float sxx = 0f, syy = 0f, sxy = 0f;
                foreach (var p in pts) { float2 d = p - mean; sxx += d.x * d.x; syy += d.y * d.y; sxy += d.x * d.y; }
                float ang = 0.5f * math.atan2(2f * sxy, sxx - syy);
                float2 axis = new float2(math.cos(ang), math.sin(ang));
                float2 nrm = new float2(-axis.y, axis.x);
                float dev = 0f;
                foreach (var p in pts) dev = math.max(dev, math.abs(math.dot(p - mean, nrm)));
                if (dev > math.max(5f, 0.012f * inside)) continue;   // straight: a winding road is no spine
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var p in poly) { float s = math.dot(p - mean, axis); lo = math.min(lo, s); hi = math.max(hi, s); }
                if (inside < 0.33f * (hi - lo)) continue;
                float weight = rr >= RoadRank.Major ? 3f : rr == RoadRank.Medium ? 2f : rr == RoadRank.Minor ? 1f : 0.5f;
                float score = weight * inside;
                if (score <= bestScore) continue;
                bestScore = score;
                found = true;
                rank = rr;
                // the line's point nearest the area's centre, so the frame origin lies on the road
                float2 c = Poly.Centroid(poly);
                point = mean + axis * math.dot(c - mean, axis);
                dir = axis;
            }
            return found;
        }

        /// <summary>An existing road runs through the area (some of it more than 20 m inside the outline).</summary>
        private static bool CrossesInterior(List<ExistingRoad> roads, float2[] poly)
        {
            var pts = new List<float3>();
            foreach (var r in roads)
            {
                pts.Clear();
                r.Curve.Flatten(pts, null, 8f, 8f);
                foreach (var p in pts)
                    if (Poly.Contains(poly, p.xz) && Poly.DistanceToBoundary(poly, p.xz) > 20f) return true;
            }
            return false;
        }

        // ───────────────────────────── phase ─────────────────────────────

        /// <summary>
        /// Spacing and anchor of one axis's lines so the most anchors lie on a line (within 4 m); the spacing may stretch
        /// ±12 %. <paramref name="must"/>: the last anchor (the spine) must be on a line. Returns the spacing; aligned = 0 when
        /// nothing lines up (even spread).
        /// </summary>
        private static float Phase(float min, float max, float target, List<(float a, float w)> anchors, bool must, out float anchor, out int aligned)
        {
            anchor = 0f; aligned = 0;
            if (anchors.Count == 0) return target;
            float bestScore = 0f, bestS = target;
            var bases = new List<float>();
            if (must) bases.Add(anchors[anchors.Count - 1].a);
            else foreach (var (a, _) in anchors) bases.Add(a);
            foreach (float f in new[] { 1f, 0.94f, 1.06f, 0.88f, 1.12f })
            {
                float s = target * f;
                foreach (float b in bases)
                {
                    var pos = GridGenerator.DistributeAnchored(min, max, s, b);
                    if (pos.Count == 0) continue;
                    float score = 0f; int count = 0;
                    foreach (var (a, w) in anchors)
                    {
                        float k = math.round((a - b) / s);
                        float near = b + k * s;
                        float err = math.abs(a - near);
                        if (err <= AlignTol && near >= pos[0] - 0.5f && near <= pos[pos.Count - 1] + 0.5f) { score += w * (1f - 0.5f * err / AlignTol); count++; }   // exact beats a near miss
                    }
                    // stretched spacing costs a little, so does a far edge block much wider than the rest
                    score -= math.abs(f - 1f) * 3f;
                    float edge = max - pos[pos.Count - 1], edge0 = pos[0] - min;
                    if (math.max(edge, edge0) > 1.3f * s) score -= 0.4f;
                    if (score > bestScore + 1e-3f) { bestScore = score; bestS = s; anchor = b; aligned = count; }
                }
            }
            return bestS;
        }

        private static void Bounds(float2[] poly, float2 origin, float2 u, float2 v, out float2 lmin, out float2 lmax)
        {
            lmin = new float2(float.MaxValue); lmax = new float2(float.MinValue);
            foreach (var p in poly)
            {
                var l = new float2(math.dot(p - origin, u), math.dot(p - origin, v));
                lmin = math.min(lmin, l); lmax = math.max(lmax, l);
            }
        }

        // ───────────────────────────── cost of a candidate ─────────────────────────────

        private static float Cost(IReadOnlyList<float3> ring, float2[] poly, GridParameters p, List<ExistingRoad> joinable, List<ExistingRoad> all,
            Func<float2, float> height, float maxGrade, Func<NetRole, float> hw, List<float2> junctions)
        {
            List<RoadSegmentDef> segs;
            try { segs = GridGenerator.GenerateGrid(ring, p); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return float.MaxValue; }
            if (segs.Count == 0) return float.MaxValue;
            float cost = 0f, total = 0f, steep = 0f;
            var lines = new List<List<float2>>();
            foreach (var r in all)
            {
                var f = new List<float3>();
                r.Curve.Flatten(f, null, 4f, 5f);
                var l = new List<float2>(f.Count);
                foreach (var q in f) l.Add(q.xz);
                lines.Add(l);
            }
            foreach (var s in segs)
            {
                var c = RoadCurve.Straight(s.Start, s.End);
                float len = c.LengthXZ(2);
                total += len;
                if (len < 20f) cost += 1f;
                if (height != null && RoadGraph.MaxGrade(c, height, 16f, 4f) > maxGrade + 0.005f) steep += len;
                float2 a = s.Start.xz, b = s.End.xz, dir = math.normalizesafe(b - a);
                // the outline at the segment's ends: a knife-edge meeting
                foreach (var e in new[] { a, b })
                {
                    float2 q = Poly.ClosestPointOnBoundary(poly, e, out int seg, out _);
                    if (math.distance(q, e) > 1f) continue;
                    float2 edge = math.normalizesafe(poly[(seg + 1) % poly.Length] - poly[seg]);
                    if (math.abs(math.dot(edge, dir)) > 0.57f) cost += 3f;   // under 55°
                }
                // crossings with existing roads at awkward angles; streets hugging an existing road
                for (int r = 0; r < all.Count; r++)
                {
                    var l = lines[r];
                    bool joinableRoad = all[r].Rank != RoadRank.Highway;
                    for (int k = 0; k + 1 < l.Count; k++)
                    {
                        if (!Seg.Intersect(a, b, l[k], l[k + 1], out float ta, out _)) continue;
                        if (ta * len < 2f || (1f - ta) * len < 2f) continue;
                        float cos = math.abs(math.dot(dir, math.normalizesafe(l[k + 1] - l[k])));
                        // a new crossing a few metres from an existing junction crowds it
                        float2 hitAt = math.lerp(a, b, ta);
                        foreach (var j in junctions)
                        {
                            float dj = math.distance(j, hitAt);
                            if (dj > 3f && dj < 45f) { cost += 6f; break; }
                        }
                        if (cos > 0.5f) cost += joinableRoad ? 6f : 2f;    // under 60°
                        if (cos > 0.77f) cost += 6f;                        // under 40°
                        break;
                    }
                    int close = 0;
                    for (int i = 1; i <= 5; i++)
                    {
                        float2 m = math.lerp(a, b, i / 6f);
                        float d = all[r].Curve.DistanceXZ(m, out float rt);
                        if (d > hw(NetRole.Street) + all[r].HalfWidth + 20f || d < (hw(NetRole.Street) + all[r].HalfWidth) * 0.6f) continue;
                        if (math.abs(math.dot(dir, all[r].Curve.TangentXZ(rt))) > 0.9f && RoadConnections.Beside(all[r].Curve, m, rt)) close++;
                    }
                    if (close >= 4) cost += 4f;
                }
            }
            if (total > 1f) cost += 300f * steep / total;
            // grid junctions a few metres off an existing road: two junctions crowding each other once the road is crossed
            var ends = new Dictionary<long, (float2 p, int n)>();
            foreach (var s in segs)
                foreach (var e in new[] { s.Start.xz, s.End.xz })
                {
                    long k = ((long)math.round(e.x * 10f) << 32) ^ (uint)(int)math.round(e.y * 10f);
                    ends[k] = ends.TryGetValue(k, out var v) ? (v.p, v.n + 1) : (e, 1);
                }
            foreach (var kv in ends)
            {
                if (kv.Value.n < 3) continue;
                foreach (var r in all)
                {
                    float d = r.Curve.DistanceXZ(kv.Value.p, out _);
                    if (d > 3f && d < 25f + r.HalfWidth) { cost += 5f; break; }
                }
            }
            Trace?.Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  cost parts: segs={0} roads={1} steep={2:0}/{3:0} total={4:0.0}", segs.Count, all.Count, steep, total, cost));
            return cost;
        }

        // ───────────────────────────── avenues ─────────────────────────────

        /// <summary>
        /// A generated avenue line that would lie on a smaller existing road moves to the nearest line that does not (the small
        /// road stays small); with a spine on the rows the avenue row is the spine.
        /// </summary>
        private static void FixAvenues(ref GridParameters p, List<float2> polygon, List<ExistingRoad> roads, Result res)
        {
            if (!p.AvenueColumnEnabled && !p.AvenueRowEnabled) return;
            GridGenerator.LinePositions(polygon, p, out float2 origin, out float2 u, out float2 v, out _, out var uPos, out var vPos);
            if (p.AvenueRowEnabled && res.Spine && res.SpineAxis == 1)
            {
                // the row closest to the spine (the spine passes through the frame origin: v = 0)
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < vPos.Count; i++) if (math.abs(vPos[i]) < bd) { bd = math.abs(vPos[i]); best = i; }
                if (best >= 0 && bd < 6f) p.AvenueRowIndex = best;
            }
            bool OnSmaller(bool column, float pos)
            {
                float2 a = column ? origin + u * pos : origin + v * pos;
                float2 d = column ? v : u;
                int on = 0, n = 0;
                for (float t = -2000f; t <= 2000f; t += 10f)
                {
                    float2 q = a + d * t;
                    if (!Poly.Contains(polygon, q)) continue;
                    n++;
                    foreach (var r in roads)
                    {
                        if (r.Rank >= RoadRank.Medium) continue;
                        float dist = r.Curve.DistanceXZ(q, out float rt);
                        if (dist < 12f && math.abs(math.dot(d, r.Curve.TangentXZ(rt))) > 0.9f && RoadConnections.Beside(r.Curve, q, rt)) { on++; break; }
                    }
                }
                return n > 0 && on > n * 0.2f;
            }
            int Move(bool column, int index, List<float> pos)
            {
                if (index < 0 || index >= pos.Count || !OnSmaller(column, pos[index])) return index;
                for (int step = 1; step < pos.Count; step++)
                    foreach (int k in new[] { index + step, index - step })
                        if (k >= 0 && k < pos.Count && !OnSmaller(column, pos[k])) return k;
                return index;
            }
            if (p.AvenueColumnEnabled) p.AvenueColumnIndex = Move(true, p.AvenueColumnIndex, uPos);
            if (p.AvenueRowEnabled && !(res.Spine && res.SpineAxis == 1)) p.AvenueRowIndex = Move(false, p.AvenueRowIndex, vPos);
        }
    }
}
