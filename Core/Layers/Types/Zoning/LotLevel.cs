// Placemaker "Zoning" — Level the lots: every lot the Zoning tool zones on sloped land becomes a level pad at the height it
// meets its road (level across the lot, following the road along it), with banks blending into the land around (no steeper
// than the bank setting, past the area where needed, never over buildings, roads or water). A lot that would need more
// earth than the limit is made shallower (down to its zone's smallest depth) or left empty. Lots already level are left
// alone. One continuous height field on a 2 m grid (Common/Sculpt), the bank limits and clash resolution of the road tool's
// terraces (TerraceField.Resolve). Retaining walls (levelWalls): where a lot's back meets ground 2.6 m or more above its
// pad (the lot behind it, back to back, or the hillside), a wall stands just behind it: a wide quay at the upper level
// whose low side the game raises into its concrete wall (as the road tool's terraced blocks); the lot behind gives up the
// row under the quay. Elsewhere two levels meet in a bank, which has to take ground: the field is laid, each lot keeps
// the rows from its road that came out level (a lot under its zone's smallest depth is left empty), and the field is laid
// again for the shortened lots (the bank now behind them), up to three times; then emitted once, the banks painted like
// the terraces'. Pure.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Layers.Roads.Terraces;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Zoning
{
    internal static class LotLevel
    {
        /// <summary>A lot already this level (most ground off its pad, m) is left alone.</summary>
        public const float LevelEnough = 0.2f;

        internal sealed class Lot
        {
            public ZoneGrid G;
            public int X0, W, D;
            /// <summary>Its zone's smallest depth: shorter, the lot stays empty.</summary>
            public int MinD = 1;
            public string ZoneId, Stat;
            /// <summary>The cells it claimed (row from the road, square): zoned once the ground is laid, rows under D only.</summary>
            public readonly List<(int X, int Y, float2[] Sq)> Cells = new List<(int, int, float2[])>();
            /// <summary>Its zone's narrowest lot (columns): narrower, the lot stays empty.</summary>
            public int MinW = 1;
            /// <summary>Pad height per column (x0 … x0 + w − 1).</summary>
            public float[] Front;
            /// <summary>The lot needs earthwork (not level as it is).</summary>
            public bool Level;
            /// <summary>Its depth as laid out (rows), before a wall or a bank took any.</summary>
            public int D0;
            /// <summary>Not a lot: the gap between two levelled lots of one street, levelled with them (never zoned).</summary>
            public bool Filler;
            /// <summary>A filler's ground (front, front, back, back) and height; its back edge and the direction to its road.</summary>
            public float2[] Quad;
            public Func<float2, float> Height;
            public float2 BackA, BackB, Dir;
        }

        /// <summary>A wall at the back of a lot: the quay's line and the lot below it.</summary>
        internal sealed class LotWall
        {
            public TerraceWall Wall;
            public Lot Below;
        }

        internal sealed class Options
        {
            public float MaxEarth = 4f;
            public float BankSlope = 0.4f;
            public bool Paint = true;
            public uint Seed = 1;
            /// <summary>Retaining walls at the backs of lower lots (else banks).</summary>
            public bool Walls = true;
        }

        /// <summary>The ground the lot meets its road at, per column (1 m in from the front edge, averaged with its neighbours).</summary>
        public static float[] Fronts(ZoneGrid g, Func<float2, float> ground)
        {
            int sx = g.Size.x;
            var raw = new float[sx];
            for (int x = 0; x < sx; x++)
            {
                var c = ZoneGuard.CellCentre(g, x, 0);
                var d = math.normalizesafe(g.Direction);
                raw[x] = ground(c + d * (ZoneGuard.Cell * 0.5f - 1f));
            }
            var f = new float[sx];
            for (int x = 0; x < sx; x++)
            {
                float s = raw[x] * 2f, w = 2f;
                if (x > 0) { s += raw[x - 1]; w += 1f; }
                if (x + 1 < sx) { s += raw[x + 1]; w += 1f; }
                f[x] = s / w;
            }
            return f;
        }

        /// <summary>
        /// How deep (≤ <paramref name="d"/>, ≥ <paramref name="minD"/>) the lot of columns x0 … x0 + w − 1 can be levelled at its
        /// front heights within <paramref name="maxEarth"/> of cut or fill; 0 when not even its smallest depth can. Also
        /// whether it needs levelling at all.
        /// </summary>
        public static int Depth(ZoneGrid g, float[] fronts, int x0, int w, int d, int minD, Func<float2, float> ground, float maxEarth, out bool needs)
        {
            needs = false;
            var dir = math.normalizesafe(g.Direction);
            var right = new float2(dir.y, -dir.x);
            int ok = 0;
            float most = 0f;
            for (int y = 0; y < d; y++)
            {
                float rowMost = 0f;
                for (int x = x0; x < x0 + w; x++)
                {
                    var c = ZoneGuard.CellCentre(g, x, y);
                    float f = fronts[x];
                    for (int k = 0; k < 5; k++)
                    {
                        var p = k == 0 ? c : c + (k == 1 ? dir : k == 2 ? -dir : k == 3 ? right : -right) * 3.5f;
                        rowMost = math.max(rowMost, math.abs(ground(p) - f));
                    }
                }
                if (rowMost > maxEarth) break;
                most = math.max(most, rowMost);
                ok = y + 1;
            }
            if (ok < minD) return 0;
            needs = most > LevelEnough;
            return ok;
        }

        /// <summary>
        /// The pads and their banks as terraform (sloped quads), the bank paint; facts and stats. Returns the ground after (null:
        /// nothing changed).
        /// </summary>
        public static Func<float2, float> Build(LayerContext ctx, List<Lot> lots, IReadOnlyList<(float3[] Line, float Width)> roads, Options o, CancellationToken cancel,
            List<KeyValuePair<string, double>> timings = null)
        {
            var clock = new PlanClock(timings);
            Sculpt sc = null;
            bool[] pad = null;
            List<Lot> level = null;
            foreach (var l in lots) l.D0 = l.D;
            var site = ctx.Input.Site;
            // the lots and the gaps between them (a row levelled as one: no ridge or trench of natural ground between two lots)
            var all = new List<Lot>(lots);
            all.AddRange(Fillers(lots, site.HeightAt));
            clock.Lap("fillers");
            var walls = PlanWalls(all, site, roads, o);
            clock.Lap("walls");
            var ws0 = WallStats;
            ctx.Plan.Facts["polishWallPoints"] = ws0.Ok;
            ctx.Plan.Facts["polishWallPointsLow"] = ws0.Low;
            ctx.Plan.Facts["polishWallPointsBlocked"] = ws0.Blocked;
            ctx.Plan.Facts["polishWallPointsRoad"] = ws0.Road;
            ctx.Plan.Facts["polishWallPointsHigh"] = ws0.High;
            ctx.Plan.Facts["polishWallsPlanned"] = walls.Count;
            for (int pass = 0; pass < 4; pass++)
            {
                level = all.FindAll(l => l.Level && l.D > 0);
                if (level.Count == 0 && walls.Count == 0) { sc = null; break; }
                var fieldTimes = timings != null ? new List<KeyValuePair<string, double>>() : null;
                sc = Field(ctx, level, all, walls, roads, o, cancel, out pad, fieldTimes);
                clock.Lap("field " + (pass + 1) + " (" + level.Count + " lots, " + sc.Count + " nodes)", fieldTimes);
                // the walls the limits left standing (a wall that cannot is laid again as a bank)
                if (walls.Count > 0)
                {
                    var kept = BlockField.Verify(sc, walls.ConvertAll(w => w.Wall), out bool dropped);
                    if (dropped)
                    {
                        var keep = new List<LotWall>();
                        foreach (var k in kept)
                        {
                            LotWall best = null; float bd = float.MaxValue;
                            foreach (var w in walls)
                            {
                                float d = math.distance(w.Wall.Line[0], k.Line[0]);
                                foreach (var q in w.Wall.Line) d = math.min(d, math.distance(q, k.Line[0]));
                                if (d < bd) { bd = d; best = w; }
                            }
                            if (best != null) keep.Add(new LotWall { Wall = k, Below = best.Below });
                        }
                        walls = keep;
                        if (pass < 3) continue;
                    }
                }
                // each lot keeps the rows from its road that lie level on this ground
                bool changed = false;
                foreach (var l in all)
                {
                    if (l.D == 0 || l.Filler) continue;
                    if (!Fit(sc, l)) continue;
                    changed = true;
                }
                clock.Lap("fit " + (pass + 1));
                if (!changed) break;
            }
            int lost = 0, shorter = 0; foreach (var l in lots) { if (l.D == 0) lost++; else if (l.D < l.D0) shorter++; }
            ctx.Plan.Facts["polishLevelLotsLost"] = lost; ctx.Plan.Facts["polishLevelLotsShorter"] = shorter; ctx.Plan.Facts["polishLevelLotsAll"] = lots.Count;
            Inspect?.Invoke(sc, lots);
            if (sc == null) return null;
            var tr = sc.Emit(ctx, null, Sculpt.MinChange, 2);
            clock.Lap("emit");
            if (tr.Cells == 0) return null;
            if (walls.Count > 0)
            {
                var ws = walls.ConvertAll(w => w.Wall);
                foreach (var w in ws) BlockField.Decks(sc, w);
                BlockField.AddQuays(ctx.Plan, ws);
                float len = 0f; foreach (var w in ws) len += w.Length;
                ctx.Plan.Facts["polishLevelWalls"] = ws.Count;
                ctx.Plan.Facts["polishLevelWallLength"] = len;
            }
            var earth = sc.Measure();
            ctx.Plan.Facts["polishLevelLots"] = level.Count(l => !l.Filler);
            ctx.Plan.Facts["polishLevelCut"] = earth.CutVolume;
            ctx.Plan.Facts["polishLevelFill"] = earth.FillVolume;
            ctx.Plan.Facts["polishLevelMaxCut"] = earth.MaxCut;
            ctx.Plan.Facts["polishLevelMaxFill"] = earth.MaxFill;
            ctx.Plan.Stats.Add(new PlanStat(StatLevelled, level.Count(l => !l.Filler), "count"));
            ctx.Plan.Stats.Add(new PlanStat(RoadsPlanner.StatCut, Math.Round(earth.CutVolume / 10.0) * 10.0, "m³"));
            ctx.Plan.Stats.Add(new PlanStat(RoadsPlanner.StatFill, Math.Round(earth.FillVolume / 10.0) * 10.0, "m³"));
            if (o.Paint)
            {
                // (no bank paint under a wall's quay or along its top)
                var under = new bool[sc.Count];
                foreach (var lw in walls)
                {
                    var line = lw.Wall.Line;
                    for (int k = 0; k < sc.Count; k++)
                    {
                        if (under[k]) continue;
                        var p = sc.Pos(k);
                        for (int i = 0; i + 1 < line.Length; i++)
                            if (Seg.DistSq(p, line[i], line[i + 1], out _) < 6f * 6f) { under[k] = true; break; }
                    }
                }
                var g = new TerraceGround { Grid = sc, Terrace = pad, Corridor = under, HeightAfter = sc.HeightAfter, Cells = tr.Cells };
                ctx.Plan.Facts["polishLevelPaints"] = TerraceDressing.Paint(ctx.Plan, g, null, new TerraceOptions { Seed = o.Seed });
            }
            clock.Lap("measure, paint");
            return sc.HeightAfter;
        }

        /// <summary>
        /// The empty columns (one or two) between two levelled lots of one row: levelled with them, as deep as the shallower,
        /// at the height the row meets its road there; never zoned.
        /// </summary>
        private static List<Lot> Fillers(List<Lot> lots, Func<float2, float> ground)
        {
            var r = new List<Lot>();
            var live = lots.FindAll(l => l.D > 0 && !l.Filler);
            // each lot's front corners and back line in its street's frame: (u from, u to) along the street, its front line
            (float lo, float hi, float hLo, float hHi) Span(Lot l, float2 origin, float2 right)
            {
                var g = l.G;
                float h = ZoneGuard.Cell * 0.5f;
                var d = math.normalizesafe(g.Direction);
                var rr = ZoneGuard.Right(d);
                float uHi = (g.Size.x - 2 * l.X0 - 1) * h + h, uLo = (g.Size.x - 2 * (l.X0 + l.W - 1) - 1) * h - h;
                float vF = (g.Size.y - 1) * h;
                float a = math.dot(g.Position + rr * uHi + d * vF - origin, right), b = math.dot(g.Position + rr * uLo + d * vF - origin, right);
                // the front heights at its two ends (its first column is the high-u end)
                float fa = l.Front[0], fb = l.Front[l.W - 1];
                return a <= b ? (a, b, fa, fb) : (b, a, fb, fa);
            }
            var done = new HashSet<(Lot, Lot)>();
            foreach (var A in live)
            {
                var dir = math.normalizesafe(A.G.Direction);
                var right = ZoneGuard.Right(dir);
                float h = ZoneGuard.Cell * 0.5f;
                var origin = A.G.Position + dir * ((A.G.Size.y - 1) * h + h);   // on A's front line
                var sa = Span(A, origin, right);
                // its nearest neighbour on the same street (parallel, the same front line) on its high side
                Lot best = null; float gap = float.MaxValue; (float lo, float hi, float hLo, float hHi) sb = default;
                foreach (var B in live)
                {
                    if (B == A || math.dot(math.normalizesafe(B.G.Direction), dir) < 0.995f) continue;
                    var bo = B.G.Position + math.normalizesafe(B.G.Direction) * ((B.G.Size.y - 1) * h + h);
                    if (math.abs(math.dot(bo - origin, dir)) > 1.5f) continue;
                    var span = Span(B, origin, right);
                    float gp = span.lo - sa.hi;
                    if (gp < 0.2f || gp > 20f || gp >= gap) continue;
                    best = B; gap = gp; sb = span;
                }
                if (best == null || !done.Add((A, best))) continue;
                float depth = ZoneGuard.Cell * math.min(A.D, best.D);
                float uA = sa.hi, uB = sb.lo, hA = sa.hHi, hB = sb.hLo;
                var front = origin;
                var quad = new[]
                {
                    front + right * (uA - 0.5f), front + right * (uB + 0.5f),
                    front + right * (uB + 0.5f) - dir * (depth + 0.5f), front + right * (uA - 0.5f) - dir * (depth + 0.5f),
                };
                var o = origin; var rgt = right;
                r.Add(new Lot
                {
                    G = A.G, X0 = 0, W = 1, D = math.min(A.D, best.D), D0 = math.min(A.D, best.D), MinD = 1, MinW = 1, Front = new[] { hA },
                    Level = true, Filler = true, Quad = Poly.EnsureCcw(quad), Dir = dir,
                    BackA = front + right * uA - dir * depth, BackB = front + right * uB - dir * depth,
                    Height = p => math.lerp(hA, hB, math.saturate((math.dot(p - o, rgt) - uA) / math.max(0.1f, uB - uA))),
                });
            }
            return r;
        }

        /// <summary>How many rows of the lot, from its road, lie level across (and no steeper along than its road) on the field.</summary>
        [ThreadStatic] internal static Action<Sculpt, List<Lot>> Inspect;

        /// <summary>
        /// The largest level part of the lot on the field: a run of its columns (at least its zone's narrowest) and its rows from
        /// the road while every cell of them lies level (at least its zone's shallowest). A neighbour at another height at one
        /// side tilts that side's column, not the whole lot. Returns whether the lot changed (narrower, shallower or empty).
        /// </summary>
        internal static bool Fit(Sculpt sc, Lot l)
        {
            int W = l.W, D = l.D;
            var ok = new bool[W * D];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < D; y++) ok[y * W + x] = CellLevel(sc, l, l.X0 + x, y);
            int bestA = 0, bestW = 0, bestD = 0, best = 0;
            for (int a = 0; a < W; a++)
                for (int b = a + math.max(1, l.MinW) - 1; b < W; b++)
                {
                    int d = 0;
                    while (d < D)
                    {
                        bool all = true;
                        for (int x = a; x <= b && all; x++) all = ok[d * W + x];
                        if (!all) break;
                        d++;
                    }
                    if (d < l.MinD) continue;
                    int area = (b - a + 1) * d;
                    // the most land; at equal land the lot nearer its first place
                    if (area > best || area == best && a == 0) { best = area; bestA = a; bestW = b - a + 1; bestD = d; }
                }
            if (best == 0)
            {
                bool was = l.D > 0;
                l.D = 0;
                return was;
            }
            if (bestA == 0 && bestW == W && bestD == D) return false;
            var front = new float[bestW];
            Array.Copy(l.Front, bestA, front, 0, bestW);
            l.Front = front;
            l.X0 += bestA;
            l.W = bestW;
            l.D = bestD;
            return true;
        }

        /// <summary>A cell lies level across (and no steeper along than its road) on the field.</summary>
        private static bool CellLevel(Sculpt sc, Lot l, int x, int y)
        {
            var g = l.G;
            var dir = math.normalizesafe(g.Direction);
            var right = new float2(dir.y, -dir.x);
            const float R = 2.5f;   // 1.5 m inside the cell edge: the 2 m grid's first bank node beyond the pad (grown 0.5 m) never reaches it
            var c = ZoneGuard.CellCentre(g, x, y);
            float h = sc.HeightAt(c);
            float lim = 0.03f * R + 0.03f;
            if (math.abs(sc.HeightAt(c + dir * R) - h) > lim || math.abs(sc.HeightAt(c - dir * R) - h) > lim) return false;
            int i = x - l.X0;
            int i0 = math.max(i - 1, 0), i1 = math.min(i + 1, l.W - 1);
            float grade = i1 > i0 ? math.abs(l.Front[i1] - l.Front[i0]) / (ZoneGuard.Cell * (i1 - i0)) : 0f;
            float limA = (grade + 0.04f) * R + 0.06f;
            return math.abs(sc.HeightAt(c + right * R) - h) <= limA && math.abs(sc.HeightAt(c - right * R) - h) <= limA;
        }

        internal static int LevelRows(Sculpt sc, Lot l)
        {
            var g = l.G;
            var dir = math.normalizesafe(g.Direction);
            var right = new float2(dir.y, -dir.x);
            const float R = 2.5f;   // 1.5 m inside the cell edge: the 2 m grid's first bank node beyond the pad (grown 0.5 m) never reaches it
            for (int y = 0; y < l.D; y++)
                for (int x = l.X0; x < l.X0 + l.W; x++)
                {
                    var c = ZoneGuard.CellCentre(g, x, y);
                    float h = sc.HeightAt(c);
                    // across: level (a little give for the build's raster); along: the road's own grade here and a little more
                    float lim = 0.03f * R + 0.03f;
                    if (math.abs(sc.HeightAt(c + dir * R) - h) > lim || math.abs(sc.HeightAt(c - dir * R) - h) > lim) return y;
                    int i = x - l.X0;
                    float grade = l.W > 1 ? math.abs(l.Front[math.min(i + 1, l.W - 1)] - l.Front[math.max(i - 1, 0)]) / (ZoneGuard.Cell * math.max(1, math.min(i + 1, l.W - 1) - math.max(i - 1, 0))) : 0f;
                    float limA = (grade + 0.04f) * R + 0.06f;
                    if (math.abs(sc.HeightAt(c + right * R) - h) > limA || math.abs(sc.HeightAt(c - right * R) - h) > limA) return y;
                }
            return l.D;
        }

        /// <summary>The pads of the levelled lots and the banks around them (not emitted).</summary>
        private static Sculpt Field(LayerContext ctx, List<Lot> level, List<Lot> all, List<LotWall> walls, IReadOnlyList<(float3[] Line, float Width)> roads, Options o,
            CancellationToken cancel, out bool[] pad, List<KeyValuePair<string, double>> timings = null)
        {
            var clock = new PlanClock(timings);
            var site = ctx.Input.Site;
            float a = math.clamp(o.BankSlope, 0.1f, 0.46f);
            float margin = o.MaxEarth / a * 1.3f + 6f;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            var rects = new List<(float2[] r, Lot lot)>();
            foreach (var l in level)
            {
                var r = RectOf(l, l.D, 0.5f);   // (D: its rows as they stand now)
                rects.Add((r, l));
                foreach (var q in r) { mn = math.min(mn, q); mx = math.max(mx, q); }
            }
            foreach (var w in walls) foreach (var q in w.Wall.Line) { mn = math.min(mn, q); mx = math.max(mx, q); }
            mn -= margin; mx += margin;
            var obstacles = new RoadObstacles(site, mn, mx);
            float2 inMin = mn + 4f, inMax = mx - 4f;
            bool Sculptable(float2 p)
            {
                if (p.x < inMin.x || p.y < inMin.y || p.x > inMax.x || p.y > inMax.y) return false;
                return !obstacles.BlockedAt(p);
            }
            var sc = new Sculpt(ctx.H, mn, mx, 2f, Sculptable);
            // (zoning speed) The roads' ground stays: each road segment visits only the nodes around it (every road for every node
            // was 2.6 s a pass on 4 x 4 blocks; the same nodes come out).
            foreach (var (line, width) in roads)
            {
                cancel.ThrowIfCancellationRequested();
                float r = width * 0.5f + 0.5f;
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    float2 s0 = line[i].xz, e = line[i + 1].xz - s0;
                    float ee = math.max(1e-6f, math.dot(e, e));
                    Nodes(sc, math.min(s0, s0 + e) - r, math.max(s0, s0 + e) + r, out int i0, out int i1, out int j0, out int j1);
                    for (int jj = j0; jj <= j1; jj++)
                        for (int ii = i0; ii <= i1; ii++)
                        {
                            int k = jj * sc.Nx + ii;
                            if (!sc.Free[k]) continue;
                            var p = sc.Pos(k);
                            float t = math.saturate(math.dot(p - s0, e) / ee);
                            if (math.distancesq(p, s0 + e * t) <= r * r) sc.Free[k] = false;
                        }
                }
            }
            clock.Lap("grid");
            int count = sc.Count;
            var protect = new byte[count];
            pad = new bool[count];
            // each node the pad of the first lot it lies in: each lot visits only the nodes under it
            foreach (var (r, l) in rects)
            {
                cancel.ThrowIfCancellationRequested();
                Poly.Bounds(r, out var rmn, out var rmx);
                Nodes(sc, rmn, rmx, out int i0, out int i1, out int j0, out int j1);
                for (int jj = j0; jj <= j1; jj++)
                    for (int ii = i0; ii <= i1; ii++)
                    {
                        int k = jj * sc.Nx + ii;
                        if (!sc.Free[k] || pad[k]) continue;
                        var p = sc.Pos(k);
                        if (!Poly.Contains(r, p)) continue;
                        sc.Target[k] = PadAt(l, p);
                        protect[k] = 1;
                        pad[k] = true;
                    }
            }
            // walls: the lot below runs on to the quay's middle, the ground above the quay is the lot above it (its pad, though
            // it gave up that row) or the hillside; the jump between them is the wall, held through the limits
            sbyte[] side = null;
            if (walls.Count > 0)
            {
                side = new sbyte[count];
                const float reach = BlockField.QuayHalf + 0.5f;
                var laidOut = new LotRects(all, l => l.D0 > 0 ? RectOf(l, l.D0, 0.5f) : null);   // the lots as laid out
                foreach (var lw in walls)
                {
                    var w = lw.Wall;
                    for (int i = 0; i + 1 < w.Line.Length; i++)
                    {
                        float2 q0 = w.Line[i], q1 = w.Line[i + 1];
                        var d = q1 - q0;
                        float len2 = math.lengthsq(d);
                        if (len2 < 1e-4f) continue;
                        float2 lo = math.min(q0, q1) - (reach + sc.Cell), hi = math.max(q0, q1) + (reach + sc.Cell);
                        int i0 = math.max(0, (int)math.floor((lo.x - sc.Origin.x) / sc.Cell)), i1 = math.min(sc.Nx - 1, (int)math.ceil((hi.x - sc.Origin.x) / sc.Cell));
                        int j0 = math.max(0, (int)math.floor((lo.y - sc.Origin.y) / sc.Cell)), j1 = math.min(sc.Ny - 1, (int)math.ceil((hi.y - sc.Origin.y) / sc.Cell));
                        float tLo = i == 0 ? 0f : -0.25f, tHi = i + 2 == w.Line.Length ? 1f : 1.25f;
                        for (int jj = j0; jj <= j1; jj++)
                            for (int ii = i0; ii <= i1; ii++)
                            {
                                int k = jj * sc.Nx + ii;
                                if (side[k] != 0 || !sc.Free[k]) continue;
                                var p = sc.Pos(k);
                                float t = math.dot(p - q0, d) / len2;
                                if (t < tLo || t > tHi) continue;
                                var up = w.Up[i];
                                float v = math.dot(p - math.lerp(q0, q1, t), up);
                                if (math.abs(v) > reach) continue;
                                if (v < 0f) sc.Target[k] = PadAt(lw.Below, p);
                                else
                                {
                                    var above = laidOut.Find(p, lw.Below);
                                    sc.Target[k] = above != null ? PadAt(above, p) : sc.Base[k];
                                }
                                side[k] = v < 0f ? (sbyte)-1 : (sbyte)1;
                                protect[k] = 1;
                                pad[k] = v < 0f;
                            }
                    }
                }
            }
            clock.Lap("pads");
            Settle(sc, protect, side, a, o.MaxEarth, cancel);
            clock.Lap("banks");
            for (int k = 0; k < count; k++)
                if (sc.Free[k]) sc.Target[k] = sc.Base[k] + math.clamp(sc.Target[k] - sc.Base[k], -o.MaxEarth, o.MaxEarth);
            Settle(sc, protect, side, a, o.MaxEarth, cancel);
            clock.Lap("banks again");
            return sc;
        }

        /// <summary>The grid's nodes over the box [mn, mx] (a node more each way, clamped to the grid; none when it lies off it).</summary>
        private static void Nodes(Sculpt sc, float2 mn, float2 mx, out int i0, out int i1, out int j0, out int j1)
        {
            float2 lo = math.floor((mn - sc.Origin) / sc.Cell) - 1f, hi = math.ceil((mx - sc.Origin) / sc.Cell) + 1f;
            i0 = (int)math.max(lo.x, 0f); j0 = (int)math.max(lo.y, 0f);
            i1 = (int)math.min(hi.x, sc.Nx - 1); j1 = (int)math.min(hi.y, sc.Ny - 1);
        }

        /// <summary>
        /// (zoning speed) The lots' rectangles and their bounds, in the lots' order: the first lot a point lies in. (A lot's
        /// rectangle was made again, and every lot tried, for every point asked about.)
        /// </summary>
        private sealed class LotRects
        {
            private readonly List<Lot> _lots = new List<Lot>();
            private readonly List<float2[]> _rects = new List<float2[]>();
            private readonly List<float4> _box = new List<float4>();

            /// <param name="rect">A lot's rectangle (null: not a lot to find).</param>
            public LotRects(List<Lot> lots, Func<Lot, float2[]> rect)
            {
                foreach (var l in lots)
                {
                    var r = rect(l);
                    if (r == null) continue;
                    Poly.Bounds(r, out var mn, out var mx);
                    _lots.Add(l); _rects.Add(r); _box.Add(new float4(mn, mx));
                }
            }

            public Lot Find(float2 p, Lot except)
            {
                for (int i = 0; i < _lots.Count; i++)
                {
                    var b = _box[i];
                    if (p.x < b.x || p.y < b.y || p.x > b.z || p.y > b.w || _lots[i] == except) continue;
                    if (Poly.Contains(_rects[i], p)) return _lots[i];
                }
                return null;
            }
        }

        private static readonly int[] Ni = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Nj = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// The banks between the pads: the bank pass holds every free node within the bank slope of its neighbours; where two
        /// levels still meet steeper (pads at different heights side by side, a pad against a road or a building), only the
        /// pad nodes right at that step give way, one ring at a time, until the bank fits (not the wide release of the road
        /// tool's terraces: a lot keeps every row its bank does not take). Wall nodes always hold.
        /// </summary>
        private static void Settle(Sculpt sc, byte[] protect, sbyte[] side, float slope, float earth, CancellationToken cancel)
        {
            int nx = sc.Nx, ny = sc.Ny;
            var T = sc.Target; var B = sc.Base;
            float step = slope / 1.08f * sc.Cell, diag = step * 1.41421356f;
            var release = new List<int>();
            for (int round = 0; round < 40; round++)
            {
                TerraceField.LimitBanks(sc, protect, slope, cancel, earth, side);
                release.Clear();
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int k = j * nx + i;
                        if (!sc.Free[k]) continue;
                        for (int n = 0; n < 8; n++)
                        {
                            int i2 = i + Ni[n], j2 = j + Nj[n];
                            if (i2 < 0 || j2 < 0 || i2 >= nx || j2 >= ny) continue;
                            int m = j2 * nx + i2;
                            if (side != null && side[k] * side[m] < 0) continue;
                            float allowed = math.max(n < 4 ? step : diag, math.abs(B[k] - B[m])) + 0.03f;
                            if (math.abs(T[k] - T[m]) <= allowed) continue;
                            // the protected one of the pair gives way (a wall node never)
                            if (protect[k] != 0 && (side == null || side[k] == 0)) release.Add(k);
                            if (sc.Free[m] && protect[m] != 0 && (side == null || side[m] == 0)) release.Add(m);
                        }
                    }
                if (release.Count == 0) return;
                foreach (int k in release) protect[k] = 0;
                if ((round & 3) == 3) cancel.ThrowIfCancellationRequested();
            }
            TerraceField.LimitBanks(sc, protect, slope, cancel, earth, side);
        }

        public const string StatLevelled = "Placemaker.Polish.Stat.Levelled";

        /// <summary>A lot's ground over its first rows (a filler: its quad), grown by <paramref name="grow"/>.</summary>
        private static float2[] RectOf(Lot l, int rows, float grow)
        {
            if (l.Quad != null) return l.Quad;
            return ZoneGuard.CellRect(l.G, l.X0, l.X0 + l.W - 1, 0, math.max(1, rows) - 1, grow);
        }

        /// <summary>
        /// The walls: along each lot's back edge, where the ground just behind it (the lot behind, back to back, or the
        /// hillside) lies at least BlockField.MinWall above its pad, a quay's line 4 m behind the edge (its low edge on the
        /// lot's back line), away from roads and buildings; pieces along one street joined, at least BlockField.MinWallLength long. The
        /// lots behind give up the rows the quay covers.
        /// </summary>
        /// <summary>Where along the lots' backs a wall could stand and why not (the plan's facts).</summary>
        private struct WallCount { public int Ok, Low, Off, High, Blocked, Road; }
        [ThreadStatic] private static WallCount WallStats;

        private static List<LotWall> PlanWalls(List<Lot> lots, SiteContext site, IReadOnlyList<(float3[] Line, float Width)> roads, Options o)
        {
            WallStats = default;
            var result = new List<LotWall>();
            var ground = site.HeightAt;
            if (ground == null) return result;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            foreach (var l in lots)
                if (l.D > 0) foreach (var q in RectOf(l, l.D, 0f)) { mn = math.min(mn, q); mx = math.max(mx, q); }
            if (mn.x > mx.x) return result;
            var obstacles = new RoadObstacles(site, mn - 40f, mx + 40f);
            // the quay just behind the lot: its low edge on the lot's back line (the lot keeps its cells, the lot behind gives one row)
            const float off = BlockField.QuayHalf;
            var behind = new LotRects(lots, l => l.D > 0 ? RectOf(l, l.D, 0.5f) : null);
            foreach (var A in lots)
            {
                if (A.D <= 0) continue;
                var g = A.G;
                var dir = A.Filler ? A.Dir : math.normalizesafe(g.Direction);
                var right = new float2(dir.y, -dir.x);
                float h = ZoneGuard.Cell * 0.5f;
                float u0, u1, vBack;
                var origin = g.Position;
                if (A.Filler)
                {
                    origin = A.BackA;
                    u0 = math.min(0f, math.dot(A.BackB - A.BackA, right));
                    u1 = math.max(0f, math.dot(A.BackB - A.BackA, right));
                    vBack = 0f;
                }
                else
                {
                    u0 = (g.Size.x - 2 * (A.X0 + A.W - 1) - 1) * h - h;
                    u1 = (g.Size.x - 2 * A.X0 - 1) * h + h;
                    vBack = (g.Size.y - 2 * (A.D - 1) - 1) * h - h;
                }
                var pieces = new List<TerraceWall>();
                var line = new List<float2>(); var rises = new List<float>();
                void Flush()
                {
                    if (line.Count >= 2)
                    {
                        var up = new float2[line.Count];
                        for (int i = 0; i < up.Length; i++) up[i] = -dir;
                        pieces.Add(new TerraceWall { Line = line.ToArray(), Up = up, Rise = rises.ToArray() });
                    }
                    line.Clear(); rises.Clear();
                }
                for (float u = u0 + 1f; u <= u1 - 1f + 0.01f; u += 2f)
                {
                    var pb = origin + right * u + dir * vBack;
                    float foot = PadAt(A, pb + dir * 1f);
                    var q = pb - dir * 2f;
                    var B = behind.Find(q, A);
                    float top = B != null ? PadAt(B, q) : ground(pb - dir * 6f);
                    float rise = top - foot;
                    var c = pb - dir * off;
                    bool ok = o.Walls && rise >= BlockField.MinWall && rise <= 2f * o.MaxEarth + 1f && !obstacles.BlockedAt(c, BlockField.QuayHalf + 2f);
                    if (ok) foreach (var (rl, width) in roads) if (Near(rl, c, width * 0.5f + BlockField.QuayHalf + 2f)) { ok = false; break; }
                    if (rise < BlockField.MinWall) WallStats.Low++;
                    else if (!o.Walls) WallStats.Off++;
                    else if (rise > 2f * o.MaxEarth + 1f) WallStats.High++;
                    else if (obstacles.BlockedAt(c, BlockField.QuayHalf + 2f)) WallStats.Blocked++;
                    else if (!ok) WallStats.Road++;
                    else WallStats.Ok++;
                    if (!ok) { Flush(); continue; }
                    line.Add(c); rises.Add(rise);
                }
                Flush();
                foreach (var p in pieces) result.Add(new LotWall { Wall = p, Below = A });
            }
            // a street's lots side by side: their pieces are one wall (each piece keeps its own lot below)
            var all = result.ConvertAll(w => w.Wall);
            var below = new Dictionary<TerraceWall, Lot>();
            foreach (var w in result) below[w.Wall] = w.Below;
            BlockField.Merge(all);
            result.Clear();
            foreach (var w in all)
            {
                if (w.Length < BlockField.MinWallLength) continue;
                // the lot below: the one whose back the wall's middle lies behind
                var mid = w.Line[w.Line.Length / 2];
                Lot best = null; float bd = float.MaxValue;
                foreach (var l in lots)
                {
                    if (l.D <= 0) continue;
                    float d = math.distance(Poly.Centroid(RectOf(l, l.D, 0f)), mid);
                    if (d < bd && math.dot(mid - l.G.Position, math.normalizesafe(l.G.Direction)) < 0f) { bd = d; best = l; }
                }
                foreach (var kv in below) if (Array.IndexOf(w.Line, kv.Key.Line[0]) >= 0) { best = kv.Value; break; }
                result.Add(new LotWall { Wall = w, Below = best });
            }
            // the lots behind give up the rows under the quays
            foreach (var l in lots)
            {
                if (l.D <= 0 || l.Filler) continue;
                while (l.D > 0 && Covered(l, l.D - 1, result)) l.D--;
                if (l.D < l.MinD) l.D = 0;
            }
            Interfaces(lots, result, o);
            return result;
        }

        /// <summary>
        /// Where a lot's back or side meets a neighbouring lot higher up (another block's lot, a corner, a lot along a climbing
        /// street), the bank between their levels needs ground: the lower lot gives the rows (its back) or the columns (its
        /// side) the bank takes at the bank slope, while it stays at least its zone's smallest; a wall's stretch needs none.
        /// </summary>
        private static void Interfaces(List<Lot> lots, List<LotWall> walls, Options o)
        {
            float run = math.clamp(o.BankSlope, 0.1f, 0.46f) / 1.08f;
            var rects = new LotRects(lots, l => l.D > 0 ? RectOf(l, l.D, 0.5f) : null);
            var cuts = new Dictionary<Lot, int3>();   // x: back rows, y: columns at the first column's side, z: at the last's
            bool UnderWall(float2 p)
            {
                foreach (var lw in walls)
                {
                    var w = lw.Wall.Line;
                    for (int i = 0; i + 1 < w.Length; i++)
                        if (Seg.DistSq(p, w[i], w[i + 1], out _) < 49f) return true;
                }
                return false;
            }
            foreach (var A in lots)
            {
                if (A.D <= 0 || A.Filler) continue;
                var g = A.G;
                var dir = math.normalizesafe(g.Direction);
                var right = ZoneGuard.Right(dir);
                float h = ZoneGuard.Cell * 0.5f;
                float uHi = (g.Size.x - 2 * A.X0 - 1) * h + h, uLo = (g.Size.x - 2 * (A.X0 + A.W - 1) - 1) * h - h;
                float vFront = (g.Size.y - 1) * h + h, vBack = (g.Size.y - 2 * (A.D - 1) - 1) * h - h;
                // the step up to the neighbour across one edge: its highest point (m), over the edge from a to b, outward n
                float Step(float2 a, float2 b, float2 n)
                {
                    float most = 0f;
                    float len = math.distance(a, b);
                    for (float t = 1f; t < len; t += 2f)
                    {
                        var p = math.lerp(a, b, t / len);
                        var q = p + n * 1.5f;
                        if (UnderWall(p)) continue;
                        var B = rects.Find(q, A);
                        if (B != null) most = math.max(most, PadAt(B, q) - PadAt(A, p - n * 1f));
                    }
                    return most;
                }
                float2 P(float u, float v) => g.Position + right * u + dir * v;
                int Need(float step) => step > 0.6f ? (int)math.ceil((step / run + 0.5f) / ZoneGuard.Cell) : 0;
                int back = Need(Step(P(uHi, vBack), P(uLo, vBack), -dir));
                int first = Need(Step(P(uHi, vFront), P(uHi, vBack), right));
                int last = Need(Step(P(uLo, vFront), P(uLo, vBack), -right));
                if (back + first + last > 0) cuts[A] = new int3(back, first, last);
            }
            foreach (var kv in cuts)
            {
                var l = kv.Key; var c = kv.Value;
                if (c.x > 0 && l.D - c.x >= l.MinD) l.D -= c.x;
                int cols = c.y + c.z;
                if (cols > 0 && l.W - cols >= math.max(1, l.MinW))
                {
                    var front = new float[l.W - cols];
                    Array.Copy(l.Front, c.y, front, 0, front.Length);
                    l.Front = front;
                    l.X0 += c.y;
                    l.W -= cols;
                }
                else if (cols > 0)
                {
                    // too narrow to give both: the side with the bigger step, if it can
                    int one = math.max(c.y, c.z);
                    if (l.W - one >= math.max(1, l.MinW))
                    {
                        int skip = c.y >= c.z ? c.y : 0;
                        var front = new float[l.W - one];
                        Array.Copy(l.Front, skip, front, 0, front.Length);
                        l.Front = front;
                        l.X0 += skip;
                        l.W -= one;
                    }
                }
            }
        }

        /// <summary>Whether any cell of the lot's row lies within a quay's width.</summary>
        private static bool Covered(Lot l, int y, List<LotWall> walls)
        {
            float reach = BlockField.QuayHalf + ZoneGuard.Cell * 0.5f - 0.1f;
            for (int x = l.X0; x < l.X0 + l.W; x++)
            {
                var c = ZoneGuard.CellCentre(l.G, x, y);
                foreach (var lw in walls)
                {
                    if (lw.Below == l) continue;
                    var w = lw.Wall.Line;
                    for (int i = 0; i + 1 < w.Length; i++)
                        if (Seg.DistSq(c, w[i], w[i + 1], out _) < reach * reach) return true;
                }
            }
            return false;
        }

        /// <summary>The pad height at p inside a lot: its front height, interpolated between the columns (level across the lot).</summary>
        private static float PadAt(Lot l, float2 p)
        {
            if (l.Height != null) return l.Height(p);
            var g = l.G;
            var d = math.normalizesafe(g.Direction);
            var r = new float2(d.y, -d.x);
            float u = math.dot(p - g.Position, r);
            float fx = (g.Size.x - 1 - u / (ZoneGuard.Cell * 0.5f)) * 0.5f;
            fx = math.clamp(fx, l.X0, l.X0 + l.W - 1);
            int x = (int)math.floor(fx);
            int x1 = math.min(x + 1, l.X0 + l.W - 1);
            return math.lerp(l.Front[x - l.X0], l.Front[x1 - l.X0], fx - x);
        }

        private static bool Near(float3[] line, float2 p, float r)
        {
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i].xz; var e = line[i + 1].xz - a;
                float t = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                if (math.distancesq(p, a + e * t) <= r * r) return true;
            }
            return false;
        }
    }
}
