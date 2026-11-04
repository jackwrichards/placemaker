// Placemaker roads layer (smarter roads) — a grid with some life in it, but only where something gives it a reason
// ("Grid character"). Real grids are not perfect: an old road or a desire line cuts across them (Broadway), hills bend and
// break them (RoadTerrainFit). Random imperfection looks bad (tried: a diagonal for its own sake sliced every block).
//   strict   the plain grid: every street straight through, whatever the ground and the city around;
//   auto     (the default) the grid gives way where the site asks: on a hill it bends, its too-steep streets become steps
//            and its too-steep blocks hill parks (RoadTerrainFit); a desire line — two city streets on opposite sides that
//            the grid would make people zig-zag between — gets a diagonal street when the rating says it is worth it;
//   lively   more of it: a gentler desire line is enough for the diagonal, and the blocks are laid like bricks (inside the
//            outer ring every other cross street stops at the next street: staggered blocks, T junctions); the block in
//            the middle is the district's square ("green" + "core").
// The diagonal runs between the two city points, shifted (≤ 15 m) so it crosses every grid street a full junction gap (45 m) from a
// junction (never a six-way crossing); a street it would cross within 45 m of the outline makes it stop at the next one
// instead (no junction crowding the border). The small triangles it leaves are "green" (pocket parks for a recipe).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadCharacter
    {
        public const string Strict = "strict", Auto = "auto", Lively = "lively";
        public static readonly string[] Values = { Strict, Auto, Lively };

        /// <summary>Smallest grid that gets the brick pattern or a diagonal (blocks each way).</summary>
        public const int MinCells = 3;
        /// <summary>Detour the grid imposes between the two city points before a diagonal pays (auto / lively).</summary>
        public const float AutoGain = 1.25f, LivelyGain = 1.12f;

        // ───────────────────────────── desire-line diagonal ─────────────────────────────

        public sealed class Diagonal
        {
            public float2 A, B;
            /// <summary>Grid distance / straight distance between the two city points it serves.</summary>
            public float Detour;
            public int Crossings;
        }

        /// <summary>
        /// The diagonal for a grid, or null when nothing asks for one: two existing city points (street ends and junctions up
        /// to 150 m outside the outline) on opposite sides, at least 250 m apart, whose straight line crosses the area and is
        /// <paramref name="minGain"/>× shorter than the grid way, not steeper than the design grade.
        /// </summary>
        public static Diagonal DesireLine(IReadOnlyList<float3> ring, GridParameters gp, List<ExistingRoad> joinable, Func<float2, float> height,
            float maxGrade, float minGain)
        {
            var polygon = Polygon(ring);
            if (polygon.Count < 3 || joinable == null || joinable.Count == 0) return null;
            GridGenerator.LinePositions(polygon, gp, out float2 origin, out float2 u, out float2 v, out _, out var uPos, out var vPos);
            if (uPos.Count < MinCells - 1 || vPos.Count < MinCells - 1) return null;
            var poly = polygon.ToArray();
            // city points around the area
            var anchors = new List<float2>();
            foreach (var r in joinable)
            {
                if (r.Rank == RoadRank.Highway) continue;
                foreach (var p in new[] { r.Curve.A.xz, r.Curve.D.xz })
                {
                    if (Poly.Contains(poly, p) || Poly.DistanceToBoundary(poly, p) > 150f) continue;
                    bool dup = false;
                    foreach (var q in anchors) if (math.distance(p, q) < 5f) { dup = true; break; }
                    if (!dup) anchors.Add(p);
                }
            }
            if (anchors.Count < 2 || anchors.Count > 200) return null;
            Poly.Bounds(poly, out float2 mn, out float2 mx);
            float minLen = math.max(250f, 0.9f * math.cmin(mx - mn));
            Diagonal best = null;
            float bestScore = 0f;
            for (int i = 0; i < anchors.Count; i++)
                for (int j = i + 1; j < anchors.Count; j++)
                {
                    float2 a = anchors[i], b = anchors[j];
                    float d = math.distance(a, b);
                    if (d < minLen) continue;
                    float2 l = b - a;
                    float detour = (math.abs(math.dot(l, u)) + math.abs(math.dot(l, v))) / d;
                    if (detour < minGain) continue;
                    var inside = Clip(poly, a, b);
                    if (inside == null) continue;
                    float inLen = math.distance(inside.Value.Item1, inside.Value.Item2);
                    if (inLen < 0.55f * d) continue;
                    float score = inLen * (detour - 1f);
                    if (score <= bestScore) continue;
                    if (Served(inside.Value.Item1, inside.Value.Item2, joinable)) continue;
                    // A desire line is not exact: turn it (≤ 8° about its middle) toward 45° to the grid, where it meets the
                    // streets least sharply, then shift it (≤ 15 m) so every crossing is clear of the grid's junctions.
                    float2 dir = l / d;
                    float ang = math.degrees(math.atan2(math.dot(dir, v), math.dot(dir, u)));
                    float off45 = ((ang % 90f) + 90f) % 90f - 45f;          // -45..45: how far from the cells' diagonal
                    float turn = -math.clamp(off45, -8f, 8f);
                    float2 mid = (a + b) * 0.5f;
                    float cr = math.cos(math.radians(turn)), sr = math.sin(math.radians(turn));
                    float2 Rot(float2 p) { var q = p - mid; return mid + new float2(q.x * cr - q.y * sr, q.x * sr + q.y * cr); }
                    float2 ra = Rot(a), rb = Rot(b);
                    dir = math.normalizesafe(rb - ra);
                    float2 nrm = new float2(-dir.y, dir.x);
                    float bestClear = -1f; float2 sa = ra, sb = rb;
                    for (float s = -15f; s <= 15f; s += 1.5f)
                    {
                        float clear = Clearance(ra + nrm * s, rb + nrm * s, origin, u, v, uPos, vPos, poly);
                        if (clear > bestClear) { bestClear = clear; sa = ra + nrm * s; sb = rb + nrm * s; }
                    }
                    if (bestClear < RoadTopology.MinJunctionGap) continue;   // every crossing a full junction gap from the grid's junctions
                    if (math.abs(off45 - math.clamp(off45, -8f, 8f)) > 12f) continue;   // still under 33°: knife-edge crossings
                    var cut = Clip(poly, sa, sb);
                    if (cut == null) continue;
                    var (ca, cb) = cut.Value;
                    ca = TrimEnd(ca, cb, origin, u, v, uPos, vPos, poly);
                    cb = TrimEnd(cb, ca, origin, u, v, uPos, vPos, poly);
                    // an end on the outline a few metres from where a grid street ends there: give that end up (stop at a crossing)
                    if (EndCrowded(ca, origin, u, v, uPos, vPos, poly)) ca = Inward(ca, cb, origin, u, v, uPos, vPos);
                    if (EndCrowded(cb, origin, u, v, uPos, vPos, poly)) cb = Inward(cb, ca, origin, u, v, uPos, vPos);
                    if (math.distance(ca, cb) < 2f * math.min(Spacing(uPos), Spacing(vPos))) continue;
                    if (height != null)
                    {
                        var c = RoadCurve.Straight(new float3(ca.x, 0f, ca.y), new float3(cb.x, 0f, cb.y));
                        if (RoadGraph.MaxGrade(c, height, 16f, 4f) > maxGrade + 0.005f) continue;
                    }
                    bestScore = score;
                    best = new Diagonal { A = ca, B = cb, Detour = detour };
                }
            return best;
        }

        /// <summary>
        /// When the grid as set leaves no room for the diagonal (its blocks too small for every crossing to keep a full junction
        /// gap from the corners), a grid fitted to it: blocks at least two junction gaps each way, in the proportion of the
        /// diagonal's slope (≤ 1.7× the set spacing), lined up so the diagonal crosses every street at its middle. Returns
        /// false when no desire line asks for a diagonal or no such grid fits.
        /// </summary>
        public static bool FitGrid(IReadOnlyList<float3> ring, GridParameters gp, List<ExistingRoad> joinable, Func<float2, float> height,
            float maxGrade, float minGain, out GridParameters fitted)
        {
            fitted = gp;
            if (gp.AnchoredU && gp.AnchoredV) return false;   // a spine both ways: the grid stays on it
            var polygon = Polygon(ring);
            if (polygon.Count < 3 || joinable == null || joinable.Count == 0) return false;
            GridGenerator.LinePositions(polygon, gp, out float2 origin, out float2 u, out float2 v, out _, out var uPos, out var vPos);
            var poly = polygon.ToArray();
            var anchors = new List<float2>();
            foreach (var r in joinable)
            {
                if (r.Rank == RoadRank.Highway) continue;
                foreach (var p in new[] { r.Curve.A.xz, r.Curve.D.xz })
                {
                    if (Poly.Contains(poly, p) || Poly.DistanceToBoundary(poly, p) > 150f) continue;
                    bool dup = false;
                    foreach (var q in anchors) if (math.distance(p, q) < 5f) { dup = true; break; }
                    if (!dup) anchors.Add(p);
                }
            }
            if (anchors.Count < 2 || anchors.Count > 200) return false;
            Poly.Bounds(poly, out float2 mn, out float2 mx);
            float minLen = math.max(250f, 0.9f * math.cmin(mx - mn));
            float su0 = gp.SpacingU > 0.5f ? gp.SpacingU : gp.SpacingMeters, sv0 = gp.SpacingV > 0.5f ? gp.SpacingV : gp.SpacingMeters;
            if (!(su0 > 1f) || !(sv0 > 1f)) return false;
            float gap = 2f * RoadTopology.MinJunctionGap + 2f;
            float bestScore = 0f;
            bool found = false;
            for (int i = 0; i < anchors.Count; i++)
                for (int j = i + 1; j < anchors.Count; j++)
                {
                    float2 a = anchors[i], b = anchors[j];
                    float d = math.distance(a, b);
                    if (d < minLen) continue;
                    float2 l = b - a;
                    float detour = (math.abs(math.dot(l, u)) + math.abs(math.dot(l, v))) / d;
                    if (detour < minGain) continue;
                    var inside = Clip(poly, a, b);
                    if (inside == null) continue;
                    float inLen = math.distance(inside.Value.Item1, inside.Value.Item2);
                    if (inLen < 0.55f * d) continue;
                    float score = inLen * (detour - 1f);
                    if (score <= bestScore) continue;
                    if (Served(inside.Value.Item1, inside.Value.Item2, joinable)) continue;
                    // the same turn toward 45° as the desire line itself
                    float2 dir = l / d;
                    float ang = math.degrees(math.atan2(math.dot(dir, v), math.dot(dir, u)));
                    float off45 = ((ang % 90f) + 90f) % 90f - 45f;
                    if (math.abs(off45 - math.clamp(off45, -8f, 8f)) > 12f) continue;
                    float turn = -math.clamp(off45, -8f, 8f);
                    float2 mid = (a + b) * 0.5f;
                    float cr = math.cos(math.radians(turn)), sr = math.sin(math.radians(turn));
                    float2 rdir = new float2(dir.x * cr - dir.y * sr, dir.x * sr + dir.y * cr);
                    float dx = math.dot(rdir, u), dy = math.dot(rdir, v);
                    if (math.abs(dx) < 0.2f || math.abs(dy) < 0.2f) continue;
                    float slope = math.abs(dy / dx);
                    // blocks in the diagonal's proportion, at least two junction gaps each way
                    float su = math.max(su0, gap), sv = su * slope;
                    if (sv < gap) { sv = gap; su = sv / slope; }
                    if (su > 1.7f * su0 || sv > 1.7f * sv0) continue;
                    // line the grid up: the diagonal crosses every street halfway between two junctions
                    float2 p0 = new float2(math.dot(mid - origin, u), math.dot(mid - origin, v));
                    var g = gp;
                    g.SpacingU = su; g.SpacingV = sv;
                    if (gp.AnchoredV)
                    {
                        float x = p0.x + (gp.AnchorV - p0.y) * (dx / dy);
                        g.AnchoredU = true; g.AnchorU = x - su * 0.5f;
                    }
                    else
                    {
                        float ax = gp.AnchoredU ? gp.AnchorU : (uPos.Count > 0 ? uPos[0] : 0f);
                        float y = p0.y + (ax - p0.x) * (dy / dx);
                        g.AnchoredU = true; g.AnchorU = ax;
                        g.AnchoredV = true; g.AnchorV = y - sv * 0.5f;
                    }
                    if (DesireLine(ring, g, joinable, height, maxGrade, minGain) == null) continue;
                    bestScore = score;
                    fitted = g;
                    found = true;
                }
            return found;
        }

        /// <summary>
        /// A city road already runs along the desire line (within 90 m, under 25° to it, over half its length): the city
        /// already has that way through, a diagonal beside it would only double it.
        /// </summary>
        private static bool Served(float2 a, float2 b, List<ExistingRoad> roads)
        {
            float2 dir = math.normalizesafe(b - a);
            int near = 0;
            for (int i = 0; i <= 8; i++)
            {
                float2 p = math.lerp(a, b, i / 8f);
                foreach (var r in roads)
                {
                    if (r.Rank == RoadRank.Highway) continue;
                    if (r.Curve.DistanceXZ(p, out float t) > 90f) continue;
                    if (math.abs(math.dot(r.Curve.TangentXZ(t), dir)) < 0.9f) continue;
                    near++;
                    break;
                }
            }
            return near >= 5;
        }

        /// <summary>An end on the outline closer than a junction gap to where a grid street meets the outline.</summary>
        private static bool EndCrowded(float2 end, float2 origin, float2 u, float2 v, List<float> uPos, List<float> vPos, float2[] poly)
        {
            if (Poly.DistanceToBoundary(poly, end) > 1f) return false;   // already stops inside, on a crossing
            float2 l = new float2(math.dot(end - origin, u), math.dot(end - origin, v));
            foreach (var x in uPos) if (math.abs(l.x - x) < RoadTopology.MinJunctionGap) return true;
            foreach (var y in vPos) if (math.abs(l.y - y) < RoadTopology.MinJunctionGap) return true;
            return false;
        }

        /// <summary>The first crossing at least a junction gap in from the end (else the end as it is).</summary>
        private static float2 Inward(float2 end, float2 other, float2 origin, float2 u, float2 v, List<float> uPos, List<float> vPos)
        {
            float2 le = new float2(math.dot(end - origin, u), math.dot(end - origin, v));
            float2 d = new float2(math.dot(other - origin, u), math.dot(other - origin, v)) - le;
            float len = math.length(d);
            if (len < 1f) return end;
            float best = float.MaxValue;
            void Try(float pos, float from, float delta)
            {
                if (math.abs(delta) < 1e-4f) return;
                float s = (pos - from) / delta * len;
                if (s >= RoadTopology.MinJunctionGap && s < best) best = s;
            }
            foreach (var x in uPos) Try(x, le.x, d.x);
            foreach (var y in vPos) Try(y, le.y, d.y);
            return best < len - RoadTopology.MinJunctionGap ? end + (other - end) * (best / len) : end;
        }

        /// <summary>Smallest distance from a crossing of the line with a grid street (inside the area) to a grid junction.</summary>
        private static float Clearance(float2 a, float2 b, float2 origin, float2 u, float2 v, List<float> uPos, List<float> vPos, float2[] poly)
        {
            float2 la = new float2(math.dot(a - origin, u), math.dot(a - origin, v)), lb = new float2(math.dot(b - origin, u), math.dot(b - origin, v));
            float2 d = lb - la;
            float clear = float.MaxValue;
            void Check(float t, bool onColumn)
            {
                if (t <= 0f || t >= 1f) return;
                float2 lp = la + d * t;
                float2 w = origin + lp.x * u + lp.y * v;
                if (!Poly.Contains(poly, w)) return;
                // distance along the crossed street to its nearest junction (the other axis' lines)
                var other = onColumn ? vPos : uPos;
                float along = onColumn ? lp.y : lp.x;
                foreach (var o in other) clear = math.min(clear, math.abs(along - o));
            }
            if (math.abs(d.x) > 1e-4f) foreach (var x in uPos) Check((x - la.x) / d.x, true);
            if (math.abs(d.y) > 1e-4f) foreach (var y in vPos) Check((y - la.y) / d.y, false);
            return clear;
        }

        /// <summary>
        /// The end moved back to the second grid street crossed when the first is under 45 m from it (no junction by the border),
        /// and to the first street inside when it lands within 40 m of a corner of the outline (the corner stays clean).
        /// </summary>
        private static float2 TrimEnd(float2 end, float2 other, float2 origin, float2 u, float2 v, List<float> uPos, List<float> vPos, float2[] poly)
        {
            bool nearCorner = false;
            int pn = poly.Length;
            for (int i = 0; i < pn; i++)
            {
                float2 d0 = math.normalizesafe(poly[i] - poly[(i + pn - 1) % pn]), d1 = math.normalizesafe(poly[(i + 1) % pn] - poly[i]);
                if (RoadGraph.Angle(d0, d1) >= 35f && math.distance(poly[i], end) < 40f) { nearCorner = true; break; }
            }
            float2 le = new float2(math.dot(end - origin, u), math.dot(end - origin, v));
            float2 lo = new float2(math.dot(other - origin, u), math.dot(other - origin, v));
            float2 d = lo - le;
            float len = math.length(d);
            if (len < 1f) return end;
            var at = new List<float>();
            void Try(float pos, float from, float delta)
            {
                if (math.abs(delta) < 1e-4f) return;
                float t = (pos - from) / delta;
                if (t > 1e-3f && t < 1f) at.Add(t * len);
            }
            foreach (var x in uPos) Try(x, le.x, d.x);
            foreach (var y in vPos) Try(y, le.y, d.y);
            at.Sort();
            if (nearCorner && at.Count >= 2)
            {
                int k = at[0] >= RoadTopology.MinJunctionGap ? 0 : 1;
                if (at[k] < len - RoadTopology.MinJunctionGap) return end + (other - end) * (at[k] / len);
            }
            if (at.Count == 0 || at[0] >= RoadTopology.MinJunctionGap) return end;
            if (at.Count < 2 || at[1] > len - RoadTopology.MinJunctionGap) return end;
            return end + (other - end) * (at[1] / len);
        }

        /// <summary>
        /// Lays the diagonal into the network: every road it crosses is split there and so is the diagonal (shared nodes).
        /// Returns the number of crossings.
        /// </summary>
        public static int Insert(RoadNetwork net, Diagonal d, float y)
        {
            float3 a = new float3(d.A.x, y, d.A.y), b = new float3(d.B.x, y, d.B.y);
            var hits = new List<(float td, int edge, float te)>();
            var pts = new List<float3>();
            var ts = new List<float>();
            for (int ei = 0; ei < net.Edges.Count; ei++)
            {
                var e = net.Edges[ei];
                if (!e.Alive || !e.IsCar) continue;
                pts.Clear(); ts.Clear();
                e.Curve.Flatten(pts, ts, 3f, 4f);
                for (int i = 0; i + 1 < pts.Count; i++)
                    if (Seg.Intersect(d.A, d.B, pts[i].xz, pts[i + 1].xz, out float td, out float tb))
                    {
                        hits.Add((td, ei, math.lerp(ts[i], ts[i + 1], math.saturate(tb))));
                        break;
                    }
            }
            hits.Sort((x, z) => x.td.CompareTo(z.td));
            var points = new List<float3> { a };
            foreach (var (_, ei, te) in hits)
            {
                var e = net.Edges[ei];
                float3 p;
                float len = e.Curve.LengthXZ(8);
                if (te * len < 2f) p = e.Curve.A;
                else if ((1f - te) * len < 2f) p = e.Curve.D;
                else
                {
                    e.Curve.Split(te, out var left, out var right);
                    p = left.D;
                    bool cap = e.CapAtEnd;
                    e.Curve = left;
                    e.CapAtEnd = false;
                    var copy = e.Clone(right);
                    copy.CapAtEnd = cap;
                    net.Edges.Add(copy);
                }
                if (math.distance(p.xz, points[points.Count - 1].xz) > 1f) points.Add(p);
            }
            if (math.distance(b.xz, points[points.Count - 1].xz) > 1f) points.Add(b);
            for (int i = 0; i + 1 < points.Count; i++)
                net.Edges.Add(new RoadEdge(RoadCurve.Straight(points[i], points[i + 1]), NetRole.Street));
            net.Weld();
            d.Crossings = hits.Count;
            return hits.Count;
        }

        /// <summary>Small triangles a diagonal leaves (a corner under 55°, smaller than a cell): pocket greens.</summary>
        public static bool IsTriangle(float2[] ring, float cellArea)
        {
            if (ring == null || ring.Length < 3 || Poly.Area(ring) > cellArea * 0.75f) return false;
            int n = ring.Length;
            for (int i = 0; i < n; i++)
            {
                float2 d0 = math.normalizesafe(ring[(i + n - 1) % n] - ring[i]), d1 = math.normalizesafe(ring[(i + 1) % n] - ring[i]);
                if (math.lengthsq(d0) < 0.5f || math.lengthsq(d1) < 0.5f) continue;
                if (math.degrees(math.acos(math.clamp(math.dot(d0, d1), -1f, 1f))) < 55f) return true;
            }
            return false;
        }

        // ───────────────────────────── brick pattern ─────────────────────────────

        /// <summary>
        /// Brick pattern on the generator's segments: the cross streets (the axis with more lines) lose every other stretch
        /// between two main streets, alternating from one band to the next. The outer bands keep every stretch (streets that
        /// meet the outline or continue a city street run on), and so do avenues. Returns the stretches removed.
        /// </summary>
        public static int Brick(List<RoadSegmentDef> segments, IReadOnlyList<float3> ring, GridParameters gp)
        {
            var polygon = Polygon(ring);
            if (polygon.Count < 3 || segments.Count == 0) return 0;
            GridGenerator.LinePositions(polygon, gp, out float2 origin, out float2 u, out float2 v, out _, out var uPos, out var vPos);
            if (uPos.Count < MinCells - 1 || vPos.Count < MinCells - 1) return 0;
            bool breakColumns = uPos.Count >= vPos.Count;
            var cross = breakColumns ? uPos : vPos;
            var main = breakColumns ? vPos : uPos;
            if (main.Count < 2) return 0;
            int removed = 0;
            for (int k = segments.Count - 1; k >= 0; k--)
            {
                var s = segments[k];
                if (s.IsAvenue || s.IsCulDeSacEnd || s.IsArc) continue;
                float2 a = s.Start.xz, b = s.End.xz;
                float2 dir = math.normalizesafe(b - a);
                bool alongV = math.abs(math.dot(dir, v)) > 0.98f;
                if (alongV != breakColumns) continue;
                float2 m = (a + b) * 0.5f;
                float lc = breakColumns ? math.dot(m - origin, u) : math.dot(m - origin, v);
                float lm = breakColumns ? math.dot(m - origin, v) : math.dot(m - origin, u);
                int i = Nearest(cross, lc);
                if (math.abs(cross[i] - lc) > 2f) continue;
                int band = -1;
                for (int j = 0; j + 1 < main.Count; j++) if (lm > main[j] && lm < main[j + 1]) { band = j; break; }
                if (band < 0) continue;
                float la = breakColumns ? math.dot(a - origin, v) : math.dot(a - origin, u);
                float lb = breakColumns ? math.dot(b - origin, v) : math.dot(b - origin, u);
                if (math.abs(math.min(la, lb) - main[band]) > 2f || math.abs(math.max(la, lb) - main[band + 1]) > 2f) continue;
                if (((i + band) & 1) == 0) continue;
                segments.RemoveAt(k);
                removed++;
            }
            return removed;
        }

        /// <summary>The middle of the district: the block containing this point becomes its square.</summary>
        public static float2 SquareAt(IReadOnlyList<float2> boundary) => Poly.PoleOfInaccessibility(boundary, out _);

        // ───────────────────────────── helpers ─────────────────────────────

        private static List<float2> Polygon(IReadOnlyList<float3> ring)
        {
            var polygon = new List<float2>(ring.Count);
            foreach (var p in ring) if (polygon.Count == 0 || math.distance(polygon[polygon.Count - 1], p.xz) > 1e-3f) polygon.Add(p.xz);
            if (polygon.Count >= 2 && math.distance(polygon[0], polygon[polygon.Count - 1]) <= 1e-3f) polygon.RemoveAt(polygon.Count - 1);
            return polygon;
        }

        private static float Spacing(List<float> pos) => pos.Count < 2 ? 0f : (pos[pos.Count - 1] - pos[0]) / (pos.Count - 1);

        private static int Nearest(List<float> pos, float x)
        {
            int best = 0;
            for (int i = 1; i < pos.Count; i++) if (math.abs(pos[i] - x) < math.abs(pos[best] - x)) best = i;
            return best;
        }

        /// <summary>The longest piece of segment a→b inside the polygon.</summary>
        private static (float2, float2)? Clip(float2[] poly, float2 a, float2 b)
        {
            (float2, float2)? best = null;
            float bl = 0f;
            foreach (var (p, q) in Poly.ClipSegmentToPolygon(poly, a, b))
            {
                float l = math.distance(p, q);
                if (l > bl) { bl = l; best = (p, q); }
            }
            return best;
        }
    }
}
