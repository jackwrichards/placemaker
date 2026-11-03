// Placemaker roads layer — "Terraced blocks": the ground for every road pattern (grid, organic, superblocks, loops…), so
// a road network laid over a hill never leaves its zoning on a slope. The same engine as the hillside terraces
// (TerraceField: one continuous height field on a 2 m node grid, emitted once as sloped quads, the same bank limits,
// clash resolution, earthwork cap and dressing), but its sources are every zoning street of the final network:
//
//   streets    every zoning street is a band source: within its band (road + zone cells + shoulder on each side) the ground
//              is the street's own height at the nearest point (its profile: the smoothed hillside along it), tilted by the
//              cross-fall away from the uphill side; level ACROSS the street, so a block's frontage never leans. Where
//              another street lies across the band (the street behind, on the other side of the block), the band stops
//              short of it by the width the height step needs at the bank slope: the two levels meet in a bank (or a
//              steep "wall" face) at the back of the lots, not in a ramp through them.
//   walls      "Retaining walls": where the back of the lots is cut deep enough into the hill (MinWall), the band runs on
//              past its zone cells to a wall line and the ground above stays (the upper level or the hillside): a jump the
//              field keeps (no bank), under a wide quay (8 m) laid along the line at the upper level. The game raises a
//              quay's side wherever the ground beside its deck lies 2 m or more below it (RESEARCH-NOTES-ADDENDUM 78) and
//              draws the concrete quay wall there; its deck fills the cut's top. Where a wall cannot stand (a cross
//              street, the outline, a step too low after the earthwork cap) the levels meet in a bank as before.
//   corridors  walkways and roads that zone nothing lie on their own profile, blended in no steeper than the banks.
//   flat       ground the survey finds flatter than the threshold is never touched (the change fades out between 60 % and
//              100 % of the threshold).
//   limits     TerraceField.Resolve (the lesser protection gives way where two levels clash), the change dies out towards
//              the outline, buildings and existing roads, and the cut / fill is capped at the earthwork limit.
//
// Pure and deterministic; the result's HeightAfter is the surface the build rasters (Sculpt.HeightAfter).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Terraces
{
    /// <summary>A retaining wall: a wide quay's centre line along the top of a cut, its deck heights and uphill directions.</summary>
    internal sealed class TerraceWall
    {
        public float2[] Line;
        public float[] Deck;
        public float2[] Up;
        /// <summary>Height of the wall face at each point (m).</summary>
        public float[] Rise;

        public float Length
        {
            get
            {
                float l = 0f;
                for (int i = 1; i < Line.Length; i++) l += math.distance(Line[i - 1], Line[i]);
                return l;
            }
        }
    }

    internal static class BlockField
    {
        private const float Reach = 50f;   // beyond a band, its pull fades to nothing over this (m)
        private const float Eps = 0.3f;
        /// <summary>Half width of the wall's quay (the wide pedestrian quay, 8 m).</summary>
        public const float QuayHalf = 4f;
        /// <summary>
        /// A zoning street climbing steeper than this (over 30 m) holds no terrace of its own: it runs through the terraces of the
        /// streets along the hill as a ramp (a corridor on its own profile, banks to the terraces), its frontage zoned only where
        /// it lies level (on a hill, a grid's terraces follow its contour streets across whole blocks).
        /// </summary>
        public const float ClimbGrade = 0.06f;
        /// <summary>Lowest wall (m): a pathway's side is Raised from 2 m (its elevation limit), with a margin for the heightmap.</summary>
        public const float MinWall = 2.6f;
        /// <summary>Lowest wall face the build keeps after the limits (m).</summary>
        public const float KeepWall = 2.2f;
        /// <summary>Shortest wall (m).</summary>
        public const float MinWallLength = 16f;
        /// <summary>The wall line lies this far past the zone cells: the quay's low edge just clears them (m).</summary>
        private const float WallReach = QuayHalf + 0.5f;

        private struct Src
        {
            public float2 A, B;
            public float YA, YB;
            public int Source;
            public float Up, Dn;      // band half width on the uphill / downhill side
            public float2 UpDir;
            public float Half;
            public int Kind;          // corridors: 1 walk, 2 other road
            public bool WallUp;       // a retaining wall may stand at the uphill band edge
            public float Grade;       // the street's own grade here (rise/run)
        }

        /// <summary>
        /// Where bands overlap (a block's corners, back to back lots), the nearer street owns the ground, a gentle street
        /// further than a steep one (lots face the contour streets on a hill): the plateau weight of a band at distance d.
        /// </summary>
        private static float OwnWeight(float d, float grade)
        {
            float e = d * (1f + 12f * grade) + 1f;
            return 1f / (e * e);
        }

        /// <summary>The survey of a site: the smoothed ground (street profiles) and its slope (the flat gate).</summary>
        internal sealed class Survey
        {
            public ScalarGrid Ground, Slope;
            public bool Sloped;
            public float SlopedShare, Relief;
            public float SlopeAt(float2 p) => Slope.Sample(p);
            public float GroundAt(float2 p) => Ground.Sample(p);
        }

        /// <summary>The smoothed ground and its slope over the area (as the terrace layout surveys it).</summary>
        public static Survey Measure(float2[] boundary, Func<float2, float> heightAt, float flatSlope)
        {
            Poly.Bounds(boundary, out float2 bmin, out float2 bmax);
            float ext = math.cmax(bmax - bmin);
            float cell = math.clamp(ext / 160f, 3f, 12f);
            var g = ScalarGrid.Over(bmin - 30f, bmax + 30f, cell);
            for (int j = 0; j < g.Ny; j++)
                for (int i = 0; i < g.Nx; i++)
                {
                    float h = heightAt(g.Pos(i, j));
                    g.V[j * g.Nx + i] = math.isfinite(h) ? h : 0f;
                }
            g.Blur(math.max(12f, 2f * cell));
            var slope = g.CloneEmpty();
            for (int j = 0; j < g.Ny; j++)
                for (int i = 0; i < g.Nx; i++)
                    slope.V[j * g.Nx + i] = math.length(g.Gradient(g.Pos(i, j)));
            var s = new Survey { Ground = g, Slope = slope };
            int inside = 0, sloped = 0;
            var heights = new List<float>();
            for (int j = 0; j < g.Ny; j++)
                for (int i = 0; i < g.Nx; i++)
                {
                    if (!Poly.Contains(boundary, g.Pos(i, j))) continue;
                    inside++;
                    heights.Add(g.V[j * g.Nx + i]);
                    if (slope.V[j * g.Nx + i] >= flatSlope) sloped++;
                }
            if (inside < 4) return s;
            heights.Sort();
            s.Relief = heights[(int)(0.97f * (heights.Count - 1))] - heights[(int)(0.03f * (heights.Count - 1))];
            s.SlopedShare = sloped / (float)inside;
            s.Sloped = s.SlopedShare >= 0.15f && s.Relief >= 2.5f;
            return s;
        }

        /// <summary>
        /// Footpaths that climb the hill steeper than a walk may (vanilla has no stairs) cannot cross the terraces: each would
        /// cut every terrace it crosses and break its walls. They are dropped (marked removed); returns how many.
        /// </summary>
        public static int DropClimbingWalks(RoadNetwork net, Survey survey)
        {
            int dropped = 0;
            var pts = new List<float3>();
            foreach (var e in net.Edges)
            {
                if (!e.Alive || !e.Walk) continue;
                pts.Clear();
                e.Curve.Flatten(pts, null, 6f, 4f);
                float steepest = 0f;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i].xz; var b = pts[i + 1].xz;
                    var d = math.normalizesafe(b - a, new float2(1, 0));
                    var c = (a + b) * 0.5f;
                    steepest = math.max(steepest, math.abs(survey.GroundAt(c + d * 10f) - survey.GroundAt(c - d * 10f)) / 20f);
                }
                if (steepest <= TerraceOptions.WalkGrade + 0.01f) continue;
                e.Removed = true;
                dropped++;
            }
            return dropped;
        }

        public static TerraceGround Build(LayerContext ctx, float2[] boundary, Survey survey, RoadNetwork net, TerraceOptions o, Func<NetRole, float> hw,
            RoadObstacles obstacles, List<ExistingRoad> existing, CancellationToken cancel)
        {
            var heightAt = (Func<float2, float>)ctx.H;
            var res = new TerraceGround { HeightAfter = heightAt };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (survey == null || !survey.Sloped || boundary == null || boundary.Length < 3) return res;

            // ── sources: every zoning street a band, everything else a corridor; heights the smoothed hillside ──
            var baseSegs = new List<Src>();
            var overSegs = new List<Src>();
            var pts = new List<float3>();
            float a = o.BankSlope;
            float cf = o.CrossFall;
            bool walls = o.Retaining;
            float full = 0f;
            for (int ei = 0; ei < net.Edges.Count; ei++)
            {
                var e = net.Edges[ei];
                if (!e.Alive) continue;
                pts.Clear();
                e.Curve.Flatten(pts, null, 6f, 4f);
                if (pts.Count < 2) continue;
                bool band = !e.Walk && ZoneBlockPredictor.Zones(e.Role);
                res.Roads.Add((e.Curve, e.Walk ? 2 : band ? 0 : 3));
                float roadHalf = e.Walk ? 1.5f : hw(e.Role);
                float bandHalf = roadHalf + o.ZoneDepth + TerraceOptions.Shoulder;
                full = math.max(full, bandHalf + (walls ? WallReach : 0f));
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    var A = pts[k].xz; var B = pts[k + 1].xz;
                    var s = new Src { A = A, B = B, YA = survey.GroundAt(A), YB = survey.GroundAt(B), Source = ei, Half = roadHalf + (e.Walk ? 1f : 1.5f), Kind = e.Walk ? 1 : 2 };
                    // its grade over 30 m (a street that climbs straight up the hill is a ramp through the terraces, not one)
                    var dirAB = math.normalizesafe(B - A, new float2(1, 0));
                    var cm = (A + B) * 0.5f;
                    s.Grade = math.abs(survey.GroundAt(cm + dirAB * 15f) - survey.GroundAt(cm - dirAB * 15f)) / 30f;
                    if (band && s.Grade <= ClimbGrade)
                    {
                        var d = math.normalizesafe(B - A, new float2(1, 0));
                        var n = new float2(-d.y, d.x);
                        var grad = survey.Ground.Gradient((A + B) * 0.5f);
                        s.UpDir = math.dot(grad, n) >= 0f ? n : -n;
                        s.Up = s.Dn = bandHalf;
                        baseSegs.Add(s);
                    }
                    else overSegs.Add(s);
                }
                cancel.ThrowIfCancellationRequested();
            }
            if (baseSegs.Count == 0) return res;
            var baseIdx = new SegGrid(16f);
            for (int i = 0; i < baseSegs.Count; i++) baseIdx.Add(baseSegs[i].A, baseSegs[i].B, baseSegs[i].Source, i);
            var overIdx = new SegGrid(16f);
            for (int i = 0; i < overSegs.Count; i++) overIdx.Add(overSegs[i].A, overSegs[i].B, overSegs[i].Source, i);

            // ── bands stop short of the street behind: the two levels meet in a bank (or a wall) at the back of the lots ──
            var hits = new List<int>(256);
            float bankFactor = o.Wall ? 1.0f : 1.15f;
            for (int i = 0; i < baseSegs.Count; i++)
            {
                var s = baseSegs[i];
                var mid = (s.A + s.B) * 0.5f;
                float yMid = 0.5f * (s.YA + s.YB);
                float roadHalf = s.Half - 1.5f;
                float bandHalf0 = s.Up;
                // The wall line: past the zone cells by the quay's half width (its low edge clears the last cell).
                float wallLine = bandHalf0 - TerraceOptions.Shoulder + WallReach;
                float maxDist = 2f * full + 60f;
                for (int side = 0; side < 2; side++)
                {
                    var dir = side == 0 ? s.UpDir : -s.UpDir;
                    var far = mid + dir * maxDist;
                    baseIdx.Query(math.min(mid, far) - 1f, math.max(mid, far) + 1f, hits);
                    float best = float.MaxValue, yHit = yMid;
                    foreach (int si in hits)
                    {
                        var t = baseSegs[baseIdx.Index[si]];
                        if (t.Source == s.Source) continue;
                        if (!RayHit(mid, dir, t.A, t.B, out float u, out float f) || u < 1f || u >= best) continue;
                        best = u;
                        yHit = math.lerp(t.YA, t.YB, f);
                    }
                    if (best == float.MaxValue)
                    {
                        // Open hillside behind the lots: a wall where the cut at the wall line is deep enough.
                        if (walls && side == 0)
                        {
                            float edgeY = yMid + cf * math.max(0f, wallLine - roadHalf);
                            float behind = survey.GroundAt(mid + dir * (wallLine + 3f));
                            if (behind - edgeY >= MinWall) { s.WallUp = true; s.Up = wallLine; }
                        }
                        continue;
                    }
                    // The step between the two levels at the back of the lots (the cross-falls take a little of it).
                    float step = math.abs(yHit - yMid) - cf * math.max(0f, best - 2f * roadHalf);
                    bool walled = walls && step >= MinWall && (side == 0 ? yHit > yMid : yHit < yMid);
                    if (walled)
                    {
                        // A wall takes no room: the lower band runs to the wall line (or halfway to the street behind), the
                        // upper band down to meet it.
                        float half = best * 0.5f;
                        if (side == 0) { s.WallUp = true; s.Up = math.max(roadHalf + 1.5f, math.min(half, wallLine)); }
                        else s.Dn = half <= wallLine ? math.max(roadHalf + 1.5f, half) : bandHalf0;
                        continue;
                    }
                    float bankW = math.abs(yHit - yMid) / a * bankFactor;
                    float bandHalf = side == 0 ? s.Up : s.Dn;
                    float b = math.clamp((best - bankW) * 0.5f, roadHalf + 1.5f, bandHalf);
                    if (side == 0) s.Up = b; else s.Dn = b;
                }
                baseSegs[i] = s;
            }

            res.Ms[0] = clock.Elapsed.TotalMilliseconds;
            // ── grid: the area, minus buildings and existing roads (their ground stays) ──
            var ring = boundary;
            Poly.Bounds(ring, out float2 mn, out float2 mx);
            var exist = existing ?? new List<ExistingRoad>();
            bool Sculptable(float2 p)
            {
                if (!Poly.Contains(ring, p)) return false;
                if (obstacles != null && obstacles.BlockedAt(p, 3f)) return false;
                foreach (var r in exist)
                {
                    float2 lo = math.min(math.min(r.Curve.A.xz, r.Curve.B.xz), math.min(r.Curve.C.xz, r.Curve.D.xz)) - r.HalfWidth - 4f;
                    float2 hi = math.max(math.max(r.Curve.A.xz, r.Curve.B.xz), math.max(r.Curve.C.xz, r.Curve.D.xz)) + r.HalfWidth + 4f;
                    if (p.x < lo.x || p.y < lo.y || p.x > hi.x || p.y > hi.y) continue;
                    if (r.Curve.DistanceXZ(p, out _) <= r.HalfWidth + 3f) return false;
                }
                return true;
            }
            var sc = new Sculpt(heightAt, mn, mx, 2f, Sculptable);
            res.Grid = sc;
            int count = sc.Count;
            var terraceNode = new bool[count];
            var corridorNode = new bool[count];
            var protect = new byte[count];
            float pw = o.Wall ? 1f : 2f;
            float rInv = math.pow(Reach, -pw);
            float fringe = o.Wall ? 1.1f : 1.5f;
            float thr = o.FlatSlope;

            int nSrc = net.Edges.Count;
            var bestD = new float[nSrc]; var bestY = new float[nSrc]; var bestO = new float[nSrc]; var bestBand = new float[nSrc]; var bestRoad = new float[nSrc];
            var bestWall = new bool[nSrc];
            var bestGrade = new float[nSrc];
            var stamp = new int[nSrc]; int tick = 0;
            var touched = new List<int>(64);
            float baseReach = full + Reach;
            float overReach = 40f;

            for (int k = 0; k < count; k++)
            {
                if ((k & 1023) == 0) cancel.ThrowIfCancellationRequested();
                if (!sc.Free[k]) continue;
                float2 p = sc.Pos(k);
                float gNat = survey.SlopeAt(p);
                float gate = Sculpt.Smooth01((gNat - 0.6f * thr) / math.max(1e-3f, 0.4f * thr));
                float ground = sc.Base[k];

                tick++;
                touched.Clear();
                if (gate > 0f) baseIdx.Query(p - baseReach, p + baseReach, hits);
                else hits.Clear();
                foreach (int si in hits)
                {
                    var s = baseSegs[baseIdx.Index[si]];
                    float d = math.sqrt(Seg.DistSq(p, s.A, s.B, out float t));
                    if (d > baseReach) continue;
                    int src = s.Source;
                    if (stamp[src] != tick) { stamp[src] = tick; bestD[src] = float.MaxValue; touched.Add(src); }
                    if (d >= bestD[src]) continue;
                    float2 c = math.lerp(s.A, s.B, t);
                    float side = math.dot(p - c, s.UpDir);
                    bestD[src] = d; bestY[src] = math.lerp(s.YA, s.YB, t); bestO[src] = side; bestBand[src] = side >= 0f ? s.Up : s.Dn; bestRoad[src] = s.Half - 1.5f;
                    bestWall[src] = side >= 0f && s.WallUp;
                    bestGrade[src] = s.Grade;
                }
                float sw = 0f, sy = 0f, pwSum = 0f, pySum = 0f;
                bool streetHit = false, plateau = false;
                foreach (int src in touched)
                {
                    float d = bestD[src], band = bestBand[src], roadHalf = bestRoad[src];
                    float x = d - band;
                    if (x >= Reach) continue;
                    // Past a wall line the band pulls nothing: the ground above the wall stays (the upper level, the hillside).
                    if (x > 0f && bestWall[src]) continue;
                    float sgn = math.clamp(bestO[src] / 4f, -1f, 1f);
                    float target;
                    if (x <= 0f) target = bestY[src] + cf * sgn * math.max(0f, d - roadHalf);
                    else
                    {
                        float edge = bestY[src] + cf * sgn * math.max(0f, band - roadHalf);
                        float width = math.max(4f, fringe * math.abs(edge - ground) / a);
                        target = math.lerp(edge, ground, Sculpt.Smooth01(x / width));
                    }
                    float xe = math.max(x, Eps);
                    float w = (pw > 1.5f ? 1f / (xe * xe) : 1f / xe) - rInv;
                    if (w <= 0f) continue;
                    sw += w; sy += w * target;
                    if (x <= 0f)
                    {
                        // inside a band only the bands count, nearest street first (a corner lot belongs to the street it faces)
                        float wb = OwnWeight(d, bestGrade[src]);
                        pwSum += wb; pySum += wb * target;
                        if (d <= roadHalf + 1.5f) streetHit = true;
                        plateau = true;
                    }
                }
                float ft = ground + gate * ((pwSum > 0f ? pySum / pwSum : sw > 0f ? sy / sw : ground) - ground);

                float bSum = 0f, bY = 0f, bMax = 0f, cSum = 0f, cY = 0f;
                bool corridor = false;
                int corridorLevel = 0;
                if (overSegs.Count > 0)
                {
                    tick++;
                    touched.Clear();
                    overIdx.Query(p - overReach, p + overReach, hits);
                    foreach (int si in hits)
                    {
                        var s = overSegs[overIdx.Index[si]];
                        float d = math.sqrt(Seg.DistSq(p, s.A, s.B, out float t));
                        int src = s.Source;
                        if (stamp[src] != tick) { stamp[src] = tick; bestD[src] = float.MaxValue; touched.Add(src); }
                        if (d >= bestD[src]) continue;
                        bestD[src] = d; bestY[src] = math.lerp(s.YA, s.YB, t); bestBand[src] = s.Half; bestO[src] = s.Kind;
                    }
                    foreach (int src in touched)
                    {
                        float d = bestD[src], half = bestBand[src], y = bestY[src];
                        float b;
                        if (d <= half) { b = 1f; corridor = true; corridorLevel = math.max(corridorLevel, bestO[src] == 1f ? 2 : 3); }
                        else
                        {
                            float width = math.max(4f, (o.Wall ? 1.2f : 1.6f) * math.abs(y - ft) / a);
                            if (d - half >= width) continue;
                            b = 1f - Sculpt.Smooth01((d - half) / width);
                        }
                        bSum += b; bY += b * y; bMax = math.max(bMax, b);
                        if (d <= half) { float wc = 1f / (d + 0.5f); cSum += wc; cY += wc * y; }
                    }
                }
                bool streetRoad = streetHit && gate > 0.999f;
                float result = cSum > 0f ? (streetRoad ? 0.5f * (ft + cY / cSum) : cY / cSum)
                    : streetRoad ? ft : bSum > 0f ? math.lerp(ft, bY / bSum, bMax) : ft;
                if (streetRoad) { corridor = true; corridorLevel = 3; }
                sc.Target[k] = result;
                bool onTerrace = plateau && bMax <= 0f;
                terraceNode[k] = onTerrace && gate > 0.999f;
                corridorNode[k] = corridor;
                protect[k] = corridorNode[k] ? (byte)corridorLevel : terraceNode[k] ? (byte)1 : (byte)0;
            }
            cancel.ThrowIfCancellationRequested();

            res.Ms[1] = clock.Elapsed.TotalMilliseconds;
            // ── retaining walls: the stretches of the wall lines where the jump stands, flagged so the limits keep it ──
            sbyte[] wallSide = null;
            List<TerraceWall> runs = null;
            if (walls)
            {
                runs = WallRuns(sc, ring, baseSegs, baseIdx, overSegs, overIdx, o, hits);
                if (runs.Count > 0)
                {
                    wallSide = new sbyte[count];
                    Flag(sc, runs, wallSide);
                    // the foot and the top hold their levels (a wall that cannot stand is released below and laid as a bank)
                    for (int k = 0; k < count; k++) if (wallSide[k] != 0 && protect[k] == 0) protect[k] = 1;
                }
            }

            // ── limits ──
            res.Initial = (byte[])protect.Clone();
            var planned = (float[])sc.Target.Clone();
            void Limits(bool log)
            {
                TerraceField.Resolve(sc, protect, a, cancel, float.PositiveInfinity, log ? res.Clashes : null, wallSide);
                sc.CapToBorder(a * 0.9f);
                for (int k = 0; k < count; k++)
                {
                    if (!sc.Free[k]) continue;
                    float off = sc.Target[k] - sc.Base[k], lim = math.clamp(off, -o.Earthwork, o.Earthwork);
                    if (math.abs(off - lim) > 0.05f) res.Limited = true;
                    sc.Target[k] = sc.Base[k] + lim;
                }
                TerraceField.Resolve(sc, protect, a, cancel, o.Earthwork, null, wallSide);
            }
            Limits(true);
            if (wallSide != null)
            {
                // A wall stands only where the limits left its face high enough: the rest is laid again as a bank.
                // (the flags always match the walls kept: a jump no wall covers is smoothed by the next limits pass)
                for (int pass = 0; pass < 4; pass++)
                {
                    runs = Verify(sc, runs, out bool changed);
                    if (!changed) break;
                    Array.Clear(wallSide, 0, count);
                    if (runs.Count > 0) Flag(sc, runs, wallSide);
                    Limits(false);
                }
                foreach (var w in runs) Decks(sc, w);
                res.Walls.AddRange(runs);
            }
            res.Earth = sc.Measure();
            res.Protect = protect;
            int terraced = 0;
            for (int k = 0; k < count; k++)
            {
                bool kept = math.abs(sc.Target[k] - planned[k]) < 0.05f;
                if (terraceNode[k] && !(protect[k] == 1 && kept)) { terraceNode[k] = false; res.LostTerrace++; }
                if (corridorNode[k] && !(protect[k] >= 2 && kept)) res.LostCorridor++;
                if (terraceNode[k]) terraced++;
            }
            // Under a wall's quay: no bank paint, no trees.
            if (wallSide != null && res.Walls.Count > 0)
            {
                Array.Clear(wallSide, 0, count);
                Flag(sc, res.Walls, wallSide, QuayHalf + 1.5f);
                for (int k = 0; k < count; k++) if (wallSide[k] != 0) corridorNode[k] = true;
            }
            res.Terrace = terraceNode; res.Corridor = corridorNode;
            res.TerracedArea = terraced * sc.Cell * sc.Cell;

            res.Ms[2] = clock.Elapsed.TotalMilliseconds;
            // ── emit ──
            var tr = sc.Emit(ctx, new[] { ring }, Sculpt.MinChange, 2);
            res.Cells = tr.Cells;
            if (tr.Cells > 0) res.HeightAfter = sc.HeightAfter;
            else res.Walls.Clear();
            res.Ms[3] = clock.Elapsed.TotalMilliseconds;
            TerraceField.Inspect?.Invoke(res);
            return res;
        }

        // ───────────── retaining walls ─────────────

        /// <summary>
        /// The wall lines of every walled band side, sampled every ~2 m, kept where a wall can stand: inside the outline and
        /// clear of it, the foot on this street's level, no road or walk across the quay, and the planned face at least
        /// <see cref="MinWall"/> high. Runs shorter than <see cref="MinWallLength"/> are dropped.
        /// </summary>
        private static List<TerraceWall> WallRuns(Sculpt sc, float2[] ring, List<Src> baseSegs, SegGrid baseIdx, List<Src> overSegs, SegGrid overIdx,
            TerraceOptions o, List<int> hits)
        {
            var runs = new List<TerraceWall>();
            var locked = sc.LockedDistance();
            var line = new List<float2>();
            var ups = new List<float2>();
            var rises = new List<float>();
            void Flush()
            {
                if (line.Count >= 2) runs.Add(new TerraceWall { Line = line.ToArray(), Up = ups.ToArray(), Rise = rises.ToArray() });
                line.Clear(); ups.Clear(); rises.Clear();
            }
            bool Clear(float2 q, int src)
            {
                // Another street or a corridor across (or right beside) the quay: no wall there.
                float r = QuayHalf + 2f;
                baseIdx.Query(q - (r + 20f), q + (r + 20f), hits);
                foreach (int si in hits)
                {
                    var t = baseSegs[baseIdx.Index[si]];
                    if (t.Source == src) continue;
                    if (math.sqrt(Seg.DistSq(q, t.A, t.B, out _)) <= t.Half + r) return false;
                }
                overIdx.Query(q - (r + 20f), q + (r + 20f), hits);
                foreach (int si in hits)
                {
                    var t = overSegs[overIdx.Index[si]];
                    if (math.sqrt(Seg.DistSq(q, t.A, t.B, out _)) <= t.Half + r) return false;
                }
                return true;
            }
            int Owner(float2 p)
            {
                // the street whose plateau weighs most here (as in the field)
                baseIdx.Query(p - 80f, p + 80f, hits);
                int bestSrc = -1; float best = 0f;
                foreach (int si in hits)
                {
                    var t = baseSegs[baseIdx.Index[si]];
                    float w = OwnWeight(math.sqrt(Seg.DistSq(p, t.A, t.B, out _)), t.Grade);
                    if (w > best) { best = w; bestSrc = t.Source; }
                }
                return bestSrc;
            }
            int last = -1;
            for (int i = 0; i < baseSegs.Count; i++)
            {
                var s = baseSegs[i];
                if (s.Source != last) { Flush(); last = s.Source; }
                if (!s.WallUp) { Flush(); continue; }
                float len = math.distance(s.A, s.B);
                int n = math.max(1, (int)math.ceil(len / 2f));
                for (int q = 0; q <= n; q++)
                {
                    if (q == 0 && line.Count > 0) continue;   // the joint with the segment before
                    var c = math.lerp(s.A, s.B, q / (float)n);
                    var p = c + s.UpDir * s.Up;
                    if (line.Count > 0 && math.distance(line[line.Count - 1], p) > 4f) Flush();
                    bool ok = Poly.Contains(ring, p) && sc.Sample(locked, p) >= QuayHalf + 6f && Owner(p - s.UpDir * 2.5f) == s.Source && Clear(p, s.Source);
                    float rise = 0f;
                    if (ok)
                    {
                        var fp = p - s.UpDir * 2.5f; var tp = p + s.UpDir * 3f;
                        float foot = sc.Sample(sc.Target, fp), top = sc.Sample(sc.Target, tp);
                        rise = top - foot;
                        float change = math.max(math.abs(sc.ChangeAt(fp)), math.abs(sc.ChangeAt(tp)));
                        ok = rise >= MinWall && change <= o.Earthwork - 0.3f && sc.Sample(locked, p) >= change / (0.9f * o.BankSlope) + 2f * sc.Cell + QuayHalf;
                    }
                    if (!ok) { Flush(); continue; }
                    line.Add(p); ups.Add(s.UpDir); rises.Add(rise);
                }
            }
            Flush();
            // a street is several edges (split at its nodes): its pieces of wall are one wall
            Merge(runs);
            runs.RemoveAll(w => w.Length < MinWallLength);
            return runs;
        }

        /// <summary>Joins wall pieces whose ends meet (within 4.5 m) running the same way, until none do.</summary>
        internal static void Merge(List<TerraceWall> runs)
        {
            bool joined = true;
            while (joined)
            {
                joined = false;
                for (int i = 0; i < runs.Count && !joined; i++)
                    for (int j = 0; j < runs.Count && !joined; j++)
                    {
                        if (i == j) continue;
                        var a = runs[i]; var b = runs[j];
                        // a's end to b's start, b turned around when it runs the other way
                        for (int flip = 0; flip < 2 && !joined; flip++)
                        {
                            var bl = flip == 0 ? b : Reverse(b);
                            float2 ae = a.Line[a.Line.Length - 1], bs = bl.Line[0];
                            if (math.distance(ae, bs) > 4.5f) continue;
                            var ta = math.normalizesafe(ae - a.Line[a.Line.Length - 2]);
                            var tb = math.normalizesafe(bl.Line[1] - bs);
                            if (math.dot(ta, tb) < 0.85f || math.dot(a.Up[a.Up.Length - 1], bl.Up[0]) < 0.85f) continue;
                            int n = a.Line.Length + bl.Line.Length;
                            var w = new TerraceWall { Line = new float2[n], Up = new float2[n], Rise = new float[n] };
                            Array.Copy(a.Line, w.Line, a.Line.Length); Array.Copy(bl.Line, 0, w.Line, a.Line.Length, bl.Line.Length);
                            Array.Copy(a.Up, w.Up, a.Up.Length); Array.Copy(bl.Up, 0, w.Up, a.Up.Length, bl.Up.Length);
                            Array.Copy(a.Rise, w.Rise, a.Rise.Length); Array.Copy(bl.Rise, 0, w.Rise, a.Rise.Length, bl.Rise.Length);
                            runs[i] = w;
                            runs.RemoveAt(j);
                            joined = true;
                        }
                    }
            }
        }

        private static TerraceWall Reverse(TerraceWall w)
        {
            var r = new TerraceWall { Line = (float2[])w.Line.Clone(), Up = (float2[])w.Up.Clone(), Rise = (float[])w.Rise.Clone() };
            Array.Reverse(r.Line); Array.Reverse(r.Up); Array.Reverse(r.Rise);
            return r;
        }

        /// <summary>Marks the nodes on either side of each wall line (−1 the foot, +1 the top): the limits never smooth across it.</summary>
        private static void Flag(Sculpt sc, List<TerraceWall> walls, sbyte[] side, float reach = QuayHalf + 0.5f)
        {
            foreach (var w in walls)
            {
                int last = w.Line.Length - 2;
                for (int i = 0; i <= last; i++)
                {
                    float2 q0 = w.Line[i], q1 = w.Line[i + 1];
                    var d = q1 - q0;
                    float len2 = math.lengthsq(d);
                    if (len2 < 1e-4f) continue;
                    float2 lo = math.min(q0, q1) - (reach + sc.Cell), hi = math.max(q0, q1) + (reach + sc.Cell);
                    int i0 = math.max(0, (int)math.floor((lo.x - sc.Origin.x) / sc.Cell)), i1 = math.min(sc.Nx - 1, (int)math.ceil((hi.x - sc.Origin.x) / sc.Cell));
                    int j0 = math.max(0, (int)math.floor((lo.y - sc.Origin.y) / sc.Cell)), j1 = math.min(sc.Ny - 1, (int)math.ceil((hi.y - sc.Origin.y) / sc.Cell));
                    float tLo = i == 0 ? 0f : -0.25f, tHi = i == last ? 1f : 1.25f;
                    for (int j = j0; j <= j1; j++)
                        for (int ii = i0; ii <= i1; ii++)
                        {
                            int k = j * sc.Nx + ii;
                            if (side[k] != 0 || !sc.Free[k]) continue;
                            var p = sc.Pos(k);
                            float t = math.dot(p - q0, d) / len2;
                            if (t < tLo || t > tHi) continue;
                            var up = math.normalizesafe(math.lerp(w.Up[i], w.Up[i + 1], math.saturate(t)), w.Up[i]);
                            float v = math.dot(p - math.lerp(q0, q1, t), up);
                            if (math.abs(v) <= reach) side[k] = v < 0f ? (sbyte)-1 : (sbyte)1;
                        }
                }
            }
        }

        /// <summary>
        /// The walls the built ground still carries: each line resampled to ~8 m chords (quays are built straight), a chord
        /// kept where the face at both its ends is at least <see cref="KeepWall"/> high, contiguous chords of at least
        /// <see cref="MinWallLength"/> kept as one wall.
        /// </summary>
        internal static List<TerraceWall> Verify(Sculpt sc, List<TerraceWall> runs, out bool changed)
        {
            changed = false;
            var kept = new List<TerraceWall>();
            foreach (var w in runs)
            {
                Chords(w, 8f, out var pts, out var ups);
                var ok = new bool[pts.Length];
                var rise = new float[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    float foot = sc.Sample(sc.Target, pts[i] - ups[i] * 2.5f), top = sc.Sample(sc.Target, pts[i] + ups[i] * 3f);
                    rise[i] = top - foot;
                }
                // a wall stands at its full height: where its face drops well under the run's typical height (its ends,
                // where a climbing street's ramp comes in) a bank takes over, not a wall tapering to nothing
                var sorted = (float[])rise.Clone();
                Array.Sort(sorted);
                float typical = sorted[sorted.Length / 2];
                float need = math.max(KeepWall, math.min(typical * 0.75f, typical - 1.2f));
                for (int i = 0; i < pts.Length; i++) ok[i] = rise[i] >= need;
                foreach (bool b in ok) changed |= !b;
                int start = -1;
                for (int i = 0; i <= pts.Length; i++)
                {
                    bool good = i < pts.Length && ok[i];
                    if (good && start < 0) start = i;
                    if (good) continue;
                    if (start >= 0 && i - start >= 2)
                    {
                        int n = i - start;
                        var piece = new TerraceWall { Line = new float2[n], Up = new float2[n], Rise = new float[n] };
                        Array.Copy(pts, start, piece.Line, 0, n);
                        Array.Copy(ups, start, piece.Up, 0, n);
                        Array.Copy(rise, start, piece.Rise, 0, n);
                        if (piece.Length >= MinWallLength) kept.Add(piece);
                        else changed = true;
                    }
                    start = -1;
                }
            }
            return kept;
        }

        /// <summary>The deck: the built ground at the quay's upper edge (its uphill side then sits on the ground, its low side is the wall).</summary>
        internal static void Decks(Sculpt sc, TerraceWall w)
        {
            w.Deck = new float[w.Line.Length];
            for (int i = 0; i < w.Line.Length; i++)
            {
                float d = sc.Sample(sc.Target, w.Line[i] + w.Up[i] * QuayHalf);
                if (!math.isfinite(d)) d = sc.Sample(sc.Target, w.Line[i] + w.Up[i] * 2f);
                w.Deck[i] = d;
            }
            // even along the wall (the grid's own steps would show in the deck): a running mean over five points
            if (w.Deck.Length < 3) return;
            var raw = (float[])w.Deck.Clone();
            for (int i = 0; i < raw.Length; i++)
            {
                float sum = 0f; int n = 0;
                for (int k = math.max(0, i - 2); k <= math.min(raw.Length - 1, i + 2); k++) { sum += raw[k]; n++; }
                w.Deck[i] = sum / n;
            }
        }

        /// <summary>A wall line resampled to equal chords of about <paramref name="chord"/> m (end points kept).</summary>
        private static void Chords(TerraceWall w, float chord, out float2[] pts, out float2[] ups)
        {
            var s = new float[w.Line.Length];
            for (int i = 1; i < s.Length; i++) s[i] = s[i - 1] + math.distance(w.Line[i - 1], w.Line[i]);
            float len = s[s.Length - 1];
            int n = math.max(1, (int)math.round(len / chord));
            pts = new float2[n + 1];
            ups = new float2[n + 1];
            int seg = 0;
            for (int i = 0; i <= n; i++)
            {
                float at = len * i / n;
                while (seg < s.Length - 2 && s[seg + 1] < at) seg++;
                float span = math.max(1e-4f, s[seg + 1] - s[seg]);
                float t = math.saturate((at - s[seg]) / span);
                pts[i] = math.lerp(w.Line[seg], w.Line[seg + 1], t);
                ups[i] = math.normalizesafe(math.lerp(w.Up[seg], w.Up[seg + 1], t), w.Up[seg]);
            }
        }

        /// <summary>
        /// The walls' quays as nets: straight pieces between the wall's points at its deck heights, explicit heights; the game
        /// raises a quay's low side into its concrete wall (RESEARCH-NOTES-ADDENDUM 78).
        /// </summary>
        internal static int AddQuays(AreaPlan plan, List<TerraceWall> walls)
        {
            int n = 0;
            if (walls == null) return 0;
            var quay = new AssetRef("", Types.Waterfront.WaterfrontArt.QuayWide);
            foreach (var w in walls)
                for (int i = 1; i < w.Line.Length; i++)
                {
                    var p0 = new float3(w.Line[i - 1].x, w.Deck[i - 1], w.Line[i - 1].y);
                    var p1 = new float3(w.Line[i].x, w.Deck[i], w.Line[i].y);
                    plan.Nets.Add(new PlannedNet
                    {
                        A = p0, B = math.lerp(p0, p1, 1f / 3f), C = math.lerp(p0, p1, 2f / 3f), D = p1,
                        Role = NetRole.PathwayWide, Asset = quay, FollowTerrain = false, Category = AssetCategories.NetQuay,
                    });
                    n++;
                }
            return n;
        }

        /// <summary>Where the ray p + u·dir (u ≥ 0) crosses segment a–b: its distance u and the fraction f along a–b.</summary>
        private static bool RayHit(float2 p, float2 dir, float2 a, float2 b, out float u, out float f)
        {
            u = f = 0f;
            var e = b - a;
            float den = dir.x * e.y - dir.y * e.x;
            if (math.abs(den) < 1e-5f) return false;
            var w = a - p;
            u = (w.x * e.y - w.y * e.x) / den;
            f = (w.x * dir.y - w.y * dir.x) / den;
            return u >= 0f && f >= 0f && f <= 1f;
        }
    }
}
