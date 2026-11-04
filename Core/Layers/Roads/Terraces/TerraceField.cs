// Placemaker roads layer (s52) — "Hillside terraces": the ground. ONE continuous height field on a 2 m node grid
// (Common/Sculpt), emitted once as sloped quads (no flat cells, so no curtains — s20/s30), built from the FINAL road
// network (after the connectivity pass, obstacles, city links):
//
//   terraces   every terrace street is one source: within its band (road + zone cells + shoulder on each side, narrowed
//              where the next street is close — TerraceStreet.BandUp/Down) the ground is the street's level, tilted by
//              the cross-fall away from the bank (the road strip itself level across). Past the band the edge eases back
//              into the hillside over the width its height difference needs at the bank slope; sources are blended with
//              weights 1/x^p (x = distance past the band; p = 2 rounded banks, p = 1 straight "wall" faces): between two
//              terraces a smooth bank, beyond the last one an embankment or cutting, far from every street the hillside.
//              Terrace edges follow the road curves exactly — nothing is blocky.
//   corridors  every other road (links, city links, lanes, bank walks) lies on its own designed profile, level across its
//              width plus a shoulder, blended into the terraces over a width that keeps the blend no steeper than the
//              banks; a terrace street's own road strip is a road too.
//   flat       ground the survey finds flatter than the threshold is never terraced (the terraces fade out between 60 %
//              and 100 % of the threshold; designed roads keep their profile).
//   limits     every bank node is held to the bank slope; where protected ground clashes the lesser gives way (Resolve);
//              the change dies out towards the outline, buildings and existing roads (locked nodes), and the cut/fill is
//              capped at the earthwork limit (an Info issue says so).
//
// Pure and deterministic; the result's HeightAfter is the same surface the build rasters (Sculpt.HeightAfter).

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Terraces
{
    internal sealed class TerraceGround
    {
        public Func<float2, float> HeightAfter;
        public Sculpt Grid;
        /// <summary>Per grid node: on a level terrace (inside a band, not blended away by a corridor).</summary>
        public bool[] Terrace;
        /// <summary>Per grid node: on a road corridor.</summary>
        public bool[] Corridor;
        public Earthwork Earth;
        public bool Limited;
        public int Cells;
        public float TerracedArea;
        /// <summary>Steepest bank the build made (rise/run over one node step, natural slopes excluded).</summary>
        public float SteepestBank;
        /// <summary>Largest gap between a road's designed height and the ground built under its centre line (terrace streets / other roads) and where.</summary>
        public float StreetGap, RoadGap;
        public float2 StreetGapAt, RoadGapAt;
        public int RoadGapKind;
        /// <summary>Corridor / terrace nodes that had to give way to a clash (diagnostics).</summary>
        public int LostCorridor, LostTerrace;
        /// <summary>Where protected ground clashed in the first round (x, z, levels k·10 + m, height step) — diagnostics.</summary>
        public readonly List<float4> Clashes = new List<float4>();
        /// <summary>The layout the field was built for (diagnostics: its rejects, streets, routes).</summary>
        public TerraceLayout Layout;
        /// <summary>Protection per grid node as planned and as built (0 bank, 1 terrace, 2 walk, 3 road) — diagnostics.</summary>
        public byte[] Initial, Protect;
        /// <summary>Milliseconds at the end of sources, field, limits, emit (diagnostics).</summary>
        public readonly double[] Ms = new double[4];
        /// <summary>The roads the field was built for: 0 terrace street, 1 link, 2 bank walk, 3 other (city links, lanes) — diagnostics.</summary>
        public readonly List<(RoadCurve curve, int kind)> Roads = new List<(RoadCurve, int)>();
        /// <summary>Retaining walls (terraced blocks): the quays along the tops of the cuts.</summary>
        public readonly List<TerraceWall> Walls = new List<TerraceWall>();
    }

    internal static class TerraceField
    {
        private const float Reach = 50f;        // beyond a band, a terrace's pull fades to nothing over this (m): wider than any fringe (≤ 1.5 × 8 m / 0.25)
        /// <summary>Diagnostics (gallery, tests): called with every built ground on this thread. Never set in the mod.</summary>
#pragma warning disable CS0649   // set by the tests and the gallery only
        [ThreadStatic] internal static Action<TerraceGround> Inspect;
#pragma warning restore CS0649
        private const float Eps = 0.3f;

        private struct Src
        {
            public float2 A, B;
            public float YA, YB;
            public int Source;
            // Terrace streets: band half widths at both ends (uphill / downhill), the uphill direction.
            public float UpA, UpB, DnA, DnB;
            public float2 UpDir;
            public float Half;
            public int Kind;   // overlays: 0 terrace link, 1 walk, 2 other (city link, lane)
        }

        public static TerraceGround Build(LayerContext ctx, TerraceLayout lay, RoadNetwork net, TerraceOptions o, Func<NetRole, float> hw,
            RoadObstacles obstacles, List<ExistingRoad> existing, CancellationToken cancel)
        {
            var heightAt = (Func<float2, float>)ctx.H;
            var res = new TerraceGround { HeightAfter = heightAt, Layout = lay };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (!lay.Sloped || lay.Boundary == null || lay.Boundary.Length < 3) return res;

            // ── sources ──
            var baseSegs = new List<Src>();
            var overSegs = new List<Src>();
            var pts = new List<float3>();
            float maxBand = 0f;
            for (int ei = 0; ei < net.Edges.Count; ei++)
            {
                var e = net.Edges[ei];
                if (!e.Alive) continue;
                res.Roads.Add((e.Curve, e.Walk ? 2 : e.TerraceStreet >= 0 ? 0 : e.TerraceLink ? 1 : 3));
                pts.Clear();
                e.Curve.Flatten(pts, null, 6f, 4f);
                if (pts.Count < 2) continue;
                bool terrace = e.TerraceStreet >= 0 && e.TerraceStreet < lay.Streets.Count && !e.Walk;
                TerraceStreet st = terrace ? lay.Streets[e.TerraceStreet] : null;
                float half = e.Walk ? 1.5f + 1f : hw(e.Role) + 1.5f;
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    // A terrace street is ONE source whatever its pieces (its pull must not grow with the number of edges).
                    var s = new Src { A = pts[k].xz, B = pts[k + 1].xz, YA = pts[k].y, YB = pts[k + 1].y, Source = terrace ? e.TerraceStreet : ei, Half = half, Kind = e.Walk ? 1 : e.TerraceLink ? 0 : 2 };
                    if (terrace)
                    {
                        float sa = st.StationOf(s.A, out _), sb = st.StationOf(s.B, out _);
                        s.UpA = st.BandAt(sa, true); s.UpB = st.BandAt(sb, true);
                        s.DnA = st.BandAt(sa, false); s.DnB = st.BandAt(sb, false);
                        s.UpDir = st.UpAt(sa);
                        // The street's own level (the curve may have been nudged by junction geometry: the plan is the street).
                        maxBand = math.max(maxBand, math.max(math.max(s.UpA, s.UpB), math.max(s.DnA, s.DnB)));
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

            res.Ms[0] = clock.Elapsed.TotalMilliseconds;
            // ── grid: the area, minus buildings and existing roads (their ground stays) ──
            var ring = lay.Boundary;
            Poly.Bounds(ring, out float2 mn, out float2 mx);
            var exist = existing ?? new List<ExistingRoad>();
            bool Sculptable(float2 p)
            {
                if (!Poly.Contains(ring, p)) return false;
                if (obstacles != null && obstacles.BlockedAt(p, 3f)) return false;
                foreach (var r in exist)
                {
                    float2 a = math.min(math.min(r.Curve.A.xz, r.Curve.B.xz), math.min(r.Curve.C.xz, r.Curve.D.xz)) - r.HalfWidth - 4f;
                    float2 b = math.max(math.max(r.Curve.A.xz, r.Curve.B.xz), math.max(r.Curve.C.xz, r.Curve.D.xz)) + r.HalfWidth + 4f;
                    if (p.x < a.x || p.y < a.y || p.x > b.x || p.y > b.y) continue;
                    if (r.Curve.DistanceXZ(p, out _) <= r.HalfWidth + 3f) return false;
                }
                return true;
            }
            var sc = new Sculpt(heightAt, mn, mx, 2f, Sculptable);
            res.Grid = sc;
            int count = sc.Count;
            var terraceNode = new bool[count];
            var corridorNode = new bool[count];
            // Protection: 0 none (a bank, free to give), 1 a level terrace, 2 a walk, 3 a road corridor (locked ground counts as 4).
            var protect = new byte[count];
            float a = o.BankSlope;
            float pw = o.Wall ? 1f : 2f;
            float rInv = math.pow(Reach, -pw);
            // A smoothstep's steepest point is 1.5× its mean: a rounded fringe is 1.5× as wide as its rise needs, a wall 1.1×.
            float fringe = o.Wall ? 1.1f : 1.5f;
            float cf = o.CrossFall;
            float roadHalf = o.StreetHalf;
            float thr = o.FlatSlope;

            // Per-node scratch: nearest distance per source (stamped).
            int nSrc = math.max(net.Edges.Count, lay.Streets.Count);
            var bestD = new float[nSrc]; var bestY = new float[nSrc]; var bestO = new float[nSrc]; var bestBand = new float[nSrc];
            var stamp = new int[nSrc]; int tick = 0;
            var touched = new List<int>(64);
            var hits = new List<int>(256);
            float baseReach = maxBand + Reach;
            float overReach = 40f;

            for (int k = 0; k < count; k++)
            {
                if ((k & 1023) == 0) cancel.ThrowIfCancellationRequested();
                if (!sc.Free[k]) continue;
                float2 p = sc.Pos(k);
                float gNat = lay.SlopeAt(p);
                float gate = Sculpt.Smooth01((gNat - 0.6f * thr) / math.max(1e-3f, 0.4f * thr));
                // Flat ground is never terraced (the gate fades the terraces out there); designed roads keep their profile anyway.
                float ground = sc.Base[k];

                // Terraces (Shepard blend of band edges and the natural ground).
                tick++;
                touched.Clear();
                if (gate > 0f)
                    baseIdx.Query(p - baseReach, p + baseReach, hits);
                else hits.Clear();
                foreach (int si in hits)
                {
                    var s = baseSegs[baseIdx.Index[si]];
                    float d2 = Seg.DistSq(p, s.A, s.B, out float t);
                    float d = math.sqrt(d2);
                    if (d > baseReach) continue;
                    int src = s.Source;
                    if (stamp[src] != tick) { stamp[src] = tick; bestD[src] = float.MaxValue; touched.Add(src); }
                    if (d >= bestD[src]) continue;
                    float2 c = math.lerp(s.A, s.B, t);
                    float side = math.dot(p - c, s.UpDir);
                    float band = side >= 0f ? math.lerp(s.UpA, s.UpB, t) : math.lerp(s.DnA, s.DnB, t);
                    bestD[src] = d; bestY[src] = math.lerp(s.YA, s.YB, t); bestO[src] = side; bestBand[src] = band;
                }
                float sw = 0f, sy = 0f, pwSum = 0f, pySum = 0f;
                bool streetHit = false;
                bool plateau = false;
                foreach (int src in touched)
                {
                    float d = bestD[src], band = bestBand[src];
                    float x = d - band;
                    if (x >= Reach) continue;
                    // Continuous side factor (no step past a street's end): uphill +1, downhill −1.
                    float sgn = math.clamp(bestO[src] / 4f, -1f, 1f);
                    float target;
                    if (x <= 0f) target = bestY[src] + cf * sgn * math.max(0f, d - roadHalf);
                    else
                    {
                        // Past the band: the terrace edge eases back into the hillside over the width its height
                        // difference needs at the bank slope (an embankment / cutting, never a long fill apron).
                        float edge = bestY[src] + cf * sgn * math.max(0f, band - roadHalf);
                        float width = math.max(4f, fringe * math.abs(edge - ground) / a);
                        target = math.lerp(edge, ground, Sculpt.Smooth01(x / width));
                    }
                    float xe = math.max(x, Eps);
                    float w = (pw > 1.5f ? 1f / (xe * xe) : 1f / xe) - rInv;
                    if (w <= 0f) continue;
                    sw += w; sy += w * target;
                    // On a terrace only the terraces count (exactly level, whatever bank is near).
                    if (x <= 0f) { pwSum += w; pySum += w * target; if (d <= roadHalf + 1.5f) streetHit = true; }
                    if (x <= 0f) plateau = true;
                }
                float ft = ground + gate * ((pwSum > 0f ? pySum / pwSum : sw > 0f ? sy / sw : ground) - ground);

                // Corridors of the other roads, blended no steeper than the banks.
                float bSum = 0f, bY = 0f, bMax = 0f, cSum = 0f, cY = 0f;
                bool corridor = false;
                int corridorLevel = 0;   // 2 a walk, 3 a road (a road keeps its profile before a walk does)
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
                        // On a road itself only the roads there count (nearest first), never the blend of one passing nearby.
                        if (d <= half) { float wc = 1f / (d + 0.5f); cSum += wc; cY += wc * y; }
                    }
                }
                // A terrace street's own road strip is a road too: it keeps its level against a link passing near (where two
                // roads overlap, at a junction, they meet halfway — their designed heights agree there anyway).
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
            // ── limits ──
            res.Initial = (byte[])protect.Clone();
            var planned = (float[])sc.Target.Clone();
            Resolve(sc, protect, a, cancel, float.PositiveInfinity, res.Clashes);
            // The change dies out towards the outline, buildings and existing roads; nothing is cut or filled past the
            // earthwork limit (a hard box the bank pass keeps too).
            sc.CapToBorder(a * 0.9f);
            for (int k = 0; k < count; k++)
            {
                if (!sc.Free[k]) continue;
                float off = sc.Target[k] - sc.Base[k], lim = math.clamp(off, -o.Earthwork, o.Earthwork);
                if (math.abs(off - lim) > 0.05f) res.Limited = true;
                sc.Target[k] = sc.Base[k] + lim;
            }
            Resolve(sc, protect, a, cancel, o.Earthwork);
            res.Earth = sc.Measure();
            res.Protect = protect;
            // What is still a level terrace / a road on its profile after the limits.
            int terraced = 0;
            for (int k = 0; k < count; k++)
            {
                bool kept = math.abs(sc.Target[k] - planned[k]) < 0.05f;
                if (terraceNode[k] && !(protect[k] == 1 && kept)) { terraceNode[k] = false; res.LostTerrace++; }
                if (corridorNode[k] && !(protect[k] >= 2 && kept)) res.LostCorridor++;
                if (terraceNode[k]) terraced++;
            }
            res.Terrace = terraceNode; res.Corridor = corridorNode;
            res.TerracedArea = terraced * sc.Cell * sc.Cell;
            res.SteepestBank = SteepestChange(sc);

            res.Ms[2] = clock.Elapsed.TotalMilliseconds;
            // ── emit ──
            var tr = sc.Emit(ctx, new[] { ring }, Sculpt.MinChange, 2);
            res.Cells = tr.Cells;
            if (tr.Cells > 0) res.HeightAfter = sc.HeightAfter;
            // How well the built ground keeps the designed road heights (where the ground is not left flat on purpose).
            foreach (var group in new[] { baseSegs, overSegs })
                foreach (var s in group)
                {
                    float2 mid = (s.A + s.B) * 0.5f;
                    if (Sculpt.Smooth01((lay.SlopeAt(mid) - 0.6f * thr) / math.max(1e-3f, 0.4f * thr)) < 1f) continue;
                    if (!Poly.Contains(ring, mid) || Poly.DistanceToBoundary(ring, mid) < 20f) continue;
                    float gap = math.abs(res.HeightAfter(mid) - (s.YA + s.YB) * 0.5f);
                    if (group == baseSegs) { if (gap > res.StreetGap) { res.StreetGap = gap; res.StreetGapAt = mid; } }
                    else if (gap > res.RoadGap) { res.RoadGap = gap; res.RoadGapAt = mid; res.RoadGapKind = s.Kind; }
                }
            res.Ms[3] = clock.Elapsed.TotalMilliseconds;
            Inspect?.Invoke(res);
            return res;
        }

        /// <summary>
        /// The bank limit, and where two protected things meet at different heights (two terraces closer than their banks
        /// need, a road through a terrace, a terrace or road against ground that may not change) the lesser one gives way —
        /// a terrace before a walk, a walk before a road, any of them before locked ground — over the distance the height
        /// step needs at the bank slope, so the bank fits in one go: a terrace that cannot be full width narrows next to the
        /// road instead of ending in a cliff, and the road keeps its profile.
        /// </summary>
        public static void Resolve(Sculpt sc, byte[] protect, float slope, CancellationToken cancel, float earth = float.PositiveInfinity, List<float4> log = null, sbyte[] wall = null)
        {
            int nx = sc.Nx, ny = sc.Ny, count = nx * ny;
            var T = sc.Target; var B = sc.Base;
            // A clash is any step past what the bank pass holds (a hair of tolerance): a bank squeezed past the slope is one too.
            float step = slope / 1.08f * sc.Cell;
            float run = slope / 1.08f;   // what LimitBanks holds
            // Per protection level (1 terrace, 2 walk, 3 road): remaining release radius (nodes) spreading from the clashes.
            var rem = new float[3][];
            for (int l = 0; l < 3; l++) rem[l] = new float[count];
            byte Level(int k) => sc.Free[k] ? protect[k] : (byte)4;
            for (int round = 0; round < 8; round++)
            {
                LimitBanks(sc, protect, slope, cancel, earth, wall);
                int found = 0;
                for (int l = 0; l < 3; l++) Array.Clear(rem[l], 0, count);
                void Seed(int k, int level, float radius)
                {
                    if (level < 1 || level > 3) return;
                    rem[level - 1][k] = math.max(rem[level - 1][k], radius + 1f);
                }
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int k = j * nx + i;
                        for (int n = 0; n < 2; n++)
                        {
                            int i2 = i + (n == 0 ? 1 : 0), j2 = j + (n == 1 ? 1 : 0);
                            if (i2 >= nx || j2 >= ny) continue;
                            int m = j2 * nx + i2;
                            if (!sc.Free[k] && !sc.Free[m]) continue;
                            // A retaining wall's nodes hold (BlockField): its jump is the wall, the land around its ends gives way
                            // through the bank pass (no release spreading from a wall end).
                            if (wall != null && (wall[k] != 0 || wall[m] != 0)) continue;
                            float dh = math.abs(T[k] - T[m]);
                            float allowed = math.max(step, math.abs(B[k] - B[m])) + 0.03f;
                            if (dh <= allowed) continue;
                            found++;
                            byte lk = Level(k), lm = Level(m);
                            if (log != null && round == 0 && log.Count < 24)
                            {
                                var at = sc.Pos(k); bool far = true;
                                foreach (var q in log) far &= math.distancesq(q.xy, at) > 625f;
                                if (far) log.Add(new float4(at, lk * 10 + lm, dh));
                            }
                            // How far the lesser protection has to give for a bank of this step (nodes).
                            // The step between two neighbours understates what lies behind them: each round reaches further.
                            float radius = math.min((dh / run / sc.Cell + 1f) * (1f + 0.5f * round), 60f / sc.Cell);
                            if (lk == 0 || lm == 0)
                            {
                                // A bank node squeezed between protected ground it cannot reconcile: the terraces around it give
                                // first, after five rounds the walks, after seven the roads.
                                int esc = round < 5 ? 1 : round < 7 ? 2 : 3;
                                Seed(lk == 0 ? k : m, esc, radius);
                                continue;
                            }
                            int lower = Math.Min((int)lk, (int)lm);
                            if (lower >= 4) continue;
                            if (lk == lower) Seed(k, lower, radius);
                            if (lm == lower) Seed(m, lower, radius);
                        }
                    }
                if (found == 0) return;
                // Spread each level's radius (two chamfer passes, 8-neighbour), release what lies inside.
                for (int l = 0; l < 3; l++)
                {
                    var r = rem[l];
                    bool any = false;
                    for (int k = 0; k < count && !any; k++) any = r[k] > 0f;
                    if (!any) continue;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool fwd = pass == 0;
                        for (int jj = 0; jj < ny; jj++)
                        {
                            int j = fwd ? jj : ny - 1 - jj;
                            for (int ii = 0; ii < nx; ii++)
                            {
                                int i = fwd ? ii : nx - 1 - ii;
                                int k = j * nx + i;
                                float best = r[k];
                                for (int n = 0; n < 8; n++)
                                {
                                    int i2 = i + Di[n], j2 = j + Dj[n];
                                    if (i2 < 0 || j2 < 0 || i2 >= nx || j2 >= ny) continue;
                                    float v = r[j2 * nx + i2] - (n < 4 ? 1f : 1.41421356f);
                                    if (v > best) best = v;
                                }
                                r[k] = best;
                            }
                        }
                    }
                    byte lv = (byte)(l + 1);
                    for (int k = 0; k < count; k++)
                        if (r[k] > 0f && protect[k] != 0 && protect[k] <= lv && (wall == null || wall[k] == 0)) protect[k] = 0;   // a wall holds: the land around it gives
                }
                cancel.ThrowIfCancellationRequested();
            }
            LimitBanks(sc, protect, slope, cancel, earth, wall);
        }

        /// <summary>
        /// Holds every free, unprotected node within the bank slope of its neighbours (or the natural slope there, if steeper):
        /// terraces and road corridors are fixed, the banks between them give. Alternating sweeps until nothing moves.
        /// </summary>
        public static void LimitBanks(Sculpt sc, byte[] protect, float slope, CancellationToken cancel, float earth = float.PositiveInfinity, sbyte[] wall = null)
        {
            // Eight directions: the steepest gradient between them is at most 1/cos 22.5° = 1.08× the limit, so hold 1/1.08 of it.
            float step = slope / 1.08f * sc.Cell, diag = step * 1.41421356f;
            int nx = sc.Nx, ny = sc.Ny;
            var T = sc.Target; var B = sc.Base;
            // Only nodes whose neighbours moved since they were last held are looked at again (the same result, a fraction of
            // the work: after the first sweeps only the fronts of the banks still move).
            var active = new bool[nx * ny];
            for (int k = 0; k < active.Length; k++) active[k] = true;
            for (int sweep = 0; sweep < 100; sweep++)
            {
                float moved = 0f;
                bool forward = (sweep & 1) == 0;
                for (int jj = 0; jj < ny; jj++)
                {
                    int j = forward ? jj : ny - 1 - jj;
                    for (int ii = 0; ii < nx; ii++)
                    {
                        int i = forward ? ii : nx - 1 - ii;
                        int k = j * nx + i;
                        if (!active[k]) continue;
                        active[k] = false;
                        if (!sc.Free[k] || protect[k] != 0) continue;
                        // The earthwork limit is a hard box around the original ground.
                        float lo = B[k] - earth, hi = B[k] + earth;
                        for (int n = 0; n < 8; n++)
                        {
                            int i2 = i + Di[n], j2 = j + Dj[n];
                            if (i2 < 0 || j2 < 0 || i2 >= nx || j2 >= ny) continue;
                            int m = j2 * nx + i2;
                            if (wall != null && wall[k] * wall[m] < 0) continue;
                            float allowed = math.max(n < 4 ? step : diag, math.abs(B[k] - B[m]));
                            lo = math.max(lo, T[m] - allowed);
                            hi = math.min(hi, T[m] + allowed);
                        }
                        float v = T[k];
                        float nv = lo > hi ? 0.5f * (lo + hi) : math.clamp(v, lo, hi);
                        if (nv != v)
                        {
                            T[k] = nv;
                            moved = math.max(moved, math.abs(nv - v));
                            for (int n = 0; n < 8; n++)
                            {
                                int i2 = i + Di[n], j2 = j + Dj[n];
                                if (i2 >= 0 && j2 >= 0 && i2 < nx && j2 < ny) active[j2 * nx + i2] = true;
                            }
                        }
                    }
                }
                if (moved < 0.005f) break;
                if ((sweep & 7) == 7) cancel.ThrowIfCancellationRequested();
            }
        }

        private static readonly int[] Di = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dj = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Steepest step between neighbouring changed nodes beyond the natural slope there (rise/run).</summary>
        private static float SteepestChange(Sculpt sc)
        {
            float worst = 0f;
            int nx = sc.Nx, ny = sc.Ny;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    for (int n = 0; n < 2; n++)
                    {
                        int i2 = i + (n == 0 ? 1 : 0), j2 = j + (n == 1 ? 1 : 0);
                        if (i2 >= nx || j2 >= ny) continue;
                        int m = j2 * nx + i2;
                        if (math.abs(sc.Offset(k)) < Sculpt.MinChange && math.abs(sc.Offset(m)) < Sculpt.MinChange) continue;
                        float g = math.abs(sc.Target[k] - sc.Target[m]) / sc.Cell;
                        float nat = math.abs(sc.Base[k] - sc.Base[m]) / sc.Cell;
                        if (g > nat + 0.001f) worst = math.max(worst, g);
                    }
                }
            return worst;
        }
    }
}
