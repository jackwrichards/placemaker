// Placemaker — the area tools' fill: what the Paint bucket takes from one click, and the block Paint area paints in.
//
// The user (area overhaul, 2026-10-17): "it needs a threshold. If an opening is 10 meters across, it should still paint
// bucket and consider that closed", "sometimes it goes inside tiny little cracks too", "sometimes in corners it doesn't
// get to the full corner", "sometimes it goes onto roads even though I have avoid roads on", "maybe paint bucket just
// has a border radius too ... if I paint bucket in an empty flat area it would do that second radius".
//
// Method, on the WallField around the click (walls drawn into cells, distance to the nearest wall per cell):
//   1. The core: cells farther than half the gap from every wall. Openings narrower than the gap have no core cells,
//      so the click's core stops at every such opening (the gap is closed) and never enters a crack narrower than it.
//   2. The body: everything within half the gap of the click's core (the union of the round brushes of that size that
//      fit), which reaches every wall except into sharp corners and narrow places.
//   3. Corners: what is left between the body and the walls is kept where it is a corner (it ends within half a gap of
//      the body and is closer to this core than to any other); a crack or a passage to another room is longer than
//      that and stays out. A short passage to another room (a gap in a thin wall) is split at its middle.
//   4. Reach: cells farther than Reach from the click are never taken, so open land fills a disc.
//   5. The outline: the region's cells plus the wall cells along it, traced as rings, minus the wall polygons exactly
//      (curbs, turning circles and zone edges are exact; only closed gaps and the reach follow the grid), the piece
//      under the click, holes opened with a corridor (CS2 areas have none).
// The window around the click starts small and fine and grows (coarser) only when the region runs into its border.
// Pure geometry: the game layer hands in the wall polygons (WallQuery) and a water test.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public enum FillStatus { Ok = 0, OnWall = 1, Nothing = 2, TooSmall = 3, Failed = 4 }

    public sealed class FillOptions
    {
        public const float DefaultGap = 10f, MaxGap = 40f;
        public const float DefaultReach = 500f, MinReach = 20f, MaxReach = 1500f;

        /// <summary>Openings narrower than this (m) count as closed; places narrower than it are left out. 0 = off.</summary>
        public float Gap = DefaultGap;
        /// <summary>How far (m) the fill may spread from the click.</summary>
        public float Reach = DefaultReach;
        /// <summary>The finest cell (m), used for small windows.</summary>
        public float FinestCell = 0.5f;
        /// <summary>Cells per window at most (the cell grows with the window).</summary>
        public int MaxCells = 280_000;
        /// <summary>Smaller results are refused (m²).</summary>
        public float MinArea = 40f;
        /// <summary>First window half-size (m); grown while the region runs into the window's border.</summary>
        public float FirstWindow = 110f;
        /// <summary>Skip the exact cut (the grid outline, simplified).</summary>
        public bool Rough;

        public FillOptions Clone() => (FillOptions)MemberwiseClone();
    }

    /// <summary>The walls touching a box, appended to <paramref name="into"/> (the game layer caches them).</summary>
    public delegate void WallQuery(float2 min, float2 max, List<BrushObstacle> into);

    /// <summary>
    /// Thrown by a WallQuery that cannot answer for this box off the main thread (the game reads the walls there): the fill
    /// stops and the caller reads the walls of [Min, Max] and runs it again.
    /// </summary>
    public sealed class FillNeedsWalls : Exception
    {
        public readonly float2 Min, Max;
        public FillNeedsWalls(float2 min, float2 max) : base("walls needed") { Min = min; Max = max; }
    }

    public sealed class FillResult
    {
        public FillStatus Status = FillStatus.Failed;
        /// <summary>The filled area: one simple CCW ring (XZ), exact along walls.</summary>
        public float2[] Ring;
        /// <summary>Stretches of the outline that close a gap or cut off a crack (not on a wall, not on the reach).</summary>
        public List<float2[]> Doors = new List<float2[]>();
        /// <summary>The fill reached its reach somewhere (the outline follows the reach circle there).</summary>
        public bool HitReach;
        public float2 Seed;
        public float Reach;
        /// <summary>Cell size (m) and window half-size (m) of the grid that decided the region; attempts made.</summary>
        public float Cell, Window;
        public int Attempts;
        /// <summary>The gap actually used (smaller than asked inside places narrower than it).</summary>
        public float GapUsed;
        public double Ms, MsGrid, MsRegion, MsCut;
        public int WallsCut;
        /// <summary>The region before the exact cut (rings, for a quick preview).</summary>
        public List<float2[]> Rough = new List<float2[]>();
        /// <summary>The window's grid and walls (Paint area fences its brush with them).</summary>
        public FillWindow Window_;
        /// <summary>The region's cells on that grid.</summary>
        public bool[] Cells;
        /// <summary>What went wrong (Failed).</summary>
        public string Error;

        public override string ToString() =>
            $"{Status} area {(Ring != null ? Poly.Area(Ring) : 0f):F0} m2, cell {Cell:F2} m, window {Window:F0} m x{Attempts}, gap {GapUsed:F1} m, "
            + $"doors {Doors.Count}, reach {(HitReach ? "hit" : "no")}, walls cut {WallsCut}, {Ms:F1} ms (grid {MsGrid:F1}, region {MsRegion:F1}, cut {MsCut:F1})";
    }

    /// <summary>The walls of one window drawn into a grid, kept for further clicks in it (made larger than asked).</summary>
    public sealed class FillWindow
    {
        public const float Grow = 1.35f;
        public WallField Field;
        public List<BrushObstacle> Walls;
        public float2 Center;
        public float Half;

        /// <summary>The window holds the square of half-size <paramref name="half"/> around <paramref name="p"/> at this cell size.</summary>
        public bool Covers(float2 p, float half, float cell) =>
            Field != null && math.abs(Field.Cell - cell) < 1e-4f && math.all(math.abs(p - Center) <= Half - half + 1e-3f);

        /// <summary>Cell size for a window asked with half-size <paramref name="half"/> (the grid is made Grow times larger).</summary>
        public static float CellFor(float half, FillOptions o) => math.max(o.FinestCell, 2f * half * Grow / math.sqrt(math.max(1000, o.MaxCells)));
    }

    public static class AreaFill
    {
        /// <summary>
        /// The region around <paramref name="seed"/>. <paramref name="walls"/> are asked for the walls of each window;
        /// <paramref name="underWater"/> (optional) marks water cells. <paramref name="cache"/> (optional, any length) keeps
        /// the last windows' grids for further clicks near them.
        /// </summary>
        public static FillResult Fill(float2 seed, WallQuery walls, Func<float2, bool> underWater, FillOptions o, FillWindow[] cache = null)
        {
            var sw = Stopwatch.StartNew();
            o = o ?? new FillOptions();
            var r = new FillResult { Seed = seed, Reach = math.clamp(o.Reach, FillOptions.MinReach, FillOptions.MaxReach) };
            float gap = math.clamp(o.Gap, 0f, FillOptions.MaxGap);
            float half = math.min(o.FirstWindow, r.Reach + 2f);
            try
            {
                for (int attempt = 1; attempt <= 4; attempt++)
                {
                    r.Attempts = attempt;
                    float cell = FillWindow.CellFor(half, o);
                    var t0 = sw.Elapsed.TotalMilliseconds;
                    FillWindow win = null;
                    if (cache != null) foreach (var c in cache) if (c != null && c.Covers(seed, half, cell)) { win = c; break; }
                    if (win == null)
                    {
                        win = Prepare(seed, half * FillWindow.Grow, cell, walls, underWater);
                        if (cache != null && cache.Length > 0)
                        {
                            for (int k = cache.Length - 1; k > 0; k--) cache[k] = cache[k - 1];
                            cache[0] = win;
                        }
                    }
                    r.MsGrid += sw.Elapsed.TotalMilliseconds - t0;
                    var t1 = sw.Elapsed.TotalMilliseconds;
                    var region = Region(win.Field, seed, gap, r.Reach, out var status, out bool touchesBorder, out float gapUsed);
                    r.MsRegion += sw.Elapsed.TotalMilliseconds - t1;
                    r.Cell = cell;
                    r.Window = half;
                    r.GapUsed = gapUsed;
                    if (region == null) { r.Status = status; return r; }
                    if (touchesBorder && half < r.Reach + 1f)
                    {
                        half = math.min(r.Reach + 2f, half * 2.6f);
                        continue;
                    }
                    var t2 = sw.Elapsed.TotalMilliseconds;
                    r.Window_ = win;
                    r.Cells = region;
                    Outline(win, region, seed, r.Reach, o, r);
                    r.MsCut = sw.Elapsed.TotalMilliseconds - t2;
                    return r;
                }
                r.Status = FillStatus.Failed;
                return r;
            }
            catch (FillNeedsWalls) { throw; }
            catch (Exception e)
            {
                r.Status = FillStatus.Failed;
                r.Error = e.GetType().Name + ": " + e.Message;
                return r;
            }
            finally { r.Ms = sw.Elapsed.TotalMilliseconds; }
        }

        /// <summary>The grid of one window: walls drawn in, water marked, distances measured.</summary>
        public static FillWindow Prepare(float2 center, float half, float cell, WallQuery walls, Func<float2, bool> underWater)
        {
            float2 min = center - half - 2f * cell, max = center + half + 2f * cell;
            var list = new List<BrushObstacle>();
            walls?.Invoke(min, max, list);
            var f = new WallField(min, max, cell);
            foreach (var w in list) f.AddWall(w.Ring, (byte)((int)w.Kind + 1));
            if (underWater != null) f.AddWhere(underWater, (byte)((int)BrushObstacleKind.Water + 1));
            f.ComputeDistance();
            return new FillWindow { Field = f, Walls = list, Center = center, Half = half };
        }

        // ───────────── the region on the grid ─────────────

        /// <summary>
        /// The cells of the click's region (true = in), or null with a status. <paramref name="touchesBorder"/>: the region
        /// runs into the window's border (a bigger window would see more).
        /// </summary>
        public static bool[] Region(WallField f, float2 seed, float gap, float reach, out FillStatus status, out bool touchesBorder, out float gapUsed)
        {
            status = FillStatus.Ok;
            touchesBorder = false;
            gapUsed = gap;
            int w = f.W, h = f.H, n = w * h;
            var d = f.Dist ?? f.ComputeDistance();
            // Cells the fill may take: free land within reach of the click.
            var ok = new bool[n];
            float r2 = reach * reach;
            // Row by row: the span of the reach disc on the row, its free cells.
            for (int j = 0; j < h; j++)
            {
                float dy = f.Origin.y + (j + 0.5f) * f.Cell - seed.y;
                float rest = r2 - dy * dy;
                if (rest < 0f) continue;
                float half = math.sqrt(rest);
                int a = math.max(0, (int)math.ceil((seed.x - half - f.Origin.x) / f.Cell - 0.5f));
                int b = math.min(w - 1, (int)math.floor((seed.x + half - f.Origin.x) / f.Cell - 0.5f));
                int row = j * w;
                for (int i = a; i <= b; i++) ok[row + i] = f.State[row + i] == WallField.Free;
            }
            int s = SeedCell(f, seed, ok);
            if (s < 0) { status = FillStatus.OnWall; return null; }

            // Walls are drawn conservatively (a cell a wall touches is a wall), so free land looks up to a cell narrower than it
            // is: an opening exactly as wide as the gap must still count as open.
            float t = 0.5f * gap - 0.75f * f.Cell;
            int core = -1;
            if (t >= f.Cell)
            {
                core = NearestCore(f, ok, s, t, out float widest);
                if (core < 0)
                {
                    // The click is in a place narrower than the gap all round: use that place's own width.
                    t = 0.6f * widest;
                    gapUsed = 2f * t;
                    core = t >= f.Cell ? NearestCore(f, ok, s, t, out _) : -1;
                }
            }
            var region = new bool[n];
            if (core < 0)
            {
                // No gap logic: every free cell connected to the click.
                gapUsed = 0f;
                var q = new Queue<int>();
                region[s] = true;
                q.Enqueue(s);
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    int ci = c % w, cj = c / w;
                    if (ci > 0 && !region[c - 1] && ok[c - 1]) { region[c - 1] = true; q.Enqueue(c - 1); }
                    if (ci < w - 1 && !region[c + 1] && ok[c + 1]) { region[c + 1] = true; q.Enqueue(c + 1); }
                    if (cj > 0 && !region[c - w] && ok[c - w]) { region[c - w] = true; q.Enqueue(c - w); }
                    if (cj < h - 1 && !region[c + w] && ok[c + w]) { region[c + w] = true; q.Enqueue(c + w); }
                }
                touchesBorder = TouchesBorder(region, w, h);
                return region;
            }

            // 1. The click's core.
            float tc = t / f.Cell;
            var inC = new bool[n];
            {
                var q = new Queue<int>();
                inC[core] = true;
                q.Enqueue(core);
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    int ci = c % w, cj = c / w;
                    if (ci > 0) Take(c - 1);
                    if (ci < w - 1) Take(c + 1);
                    if (cj > 0) Take(c - w);
                    if (cj < h - 1) Take(c + w);
                    void Take(int nb) { if (!inC[nb] && ok[nb] && d[nb] > t) { inC[nb] = true; q.Enqueue(nb); } }
                }
            }
            // Bounds of the core, grown by how far the body and corners can reach.
            int ci0 = w, cj0 = h, ci1 = -1, cj1 = -1;
            for (int k = 0; k < n; k++)
                if (inC[k]) { int i = k % w, j = k / w; if (i < ci0) ci0 = i; if (i > ci1) ci1 = i; if (j < cj0) cj0 = j; if (j > cj1) cj1 = j; }
            // Corners reach 2t from the core at 60 degrees (t / sin 30); a little more for the grid.
            float deep = 2.2f * tc + 3f, deep2 = deep * deep;
            int grow = (int)math.ceil(deep) + 2;
            int i0 = math.max(0, ci0 - grow), j0 = math.max(0, cj0 - grow), i1 = math.min(w, ci1 + grow + 1), j1 = math.min(h, cj1 + grow + 1);
            // 2. Distances to this core, and to every other core (other rooms) in that box.
            var dc = WallField.SquaredDistance(w, h, i0, j0, i1, j1, k => inC[k]);
            var dox = WallField.SquaredDistance(w, h, math.max(0, i0 - grow), math.max(0, j0 - grow), math.min(w, i1 + grow), math.min(h, j1 + grow),
                k => !inC[k] && ok[k] && d[k] > t);
            float tc2 = (tc + 0.5f) * (tc + 0.5f);
            // The body: within half a gap of the core (the round brushes that fit), always connected to it.
            for (int j = j0; j < j1; j++)
                for (int i = i0; i < i1; i++)
                {
                    int k = j * w + i;
                    if (ok[k] && (inC[k] || dc[k] <= tc2)) region[k] = true;
                }
            // 3. Corners: free cells beyond the body, nearer this core than any other, in pieces that end within half a gap.
            var cand = new bool[n];
            for (int j = j0; j < j1; j++)
                for (int i = i0; i < i1; i++)
                {
                    int k = j * w + i;
                    if (ok[k] && !region[k] && dc[k] <= deep2 && dc[k] <= dox[k]) cand[k] = true;
                }
            var seen = new bool[n];
            var piece = new List<int>();
            var pq = new Queue<int>();
            for (int j = j0; j < j1; j++)
                for (int i = i0; i < i1; i++)
                {
                    int k0 = j * w + i;
                    if (!cand[k0] || seen[k0]) continue;
                    piece.Clear();
                    bool touchesBody = false, tooDeep = false;
                    seen[k0] = true;
                    pq.Enqueue(k0);
                    while (pq.Count > 0)
                    {
                        int c = pq.Dequeue();
                        piece.Add(c);
                        int ci = c % w, cj = c / w;
                        if (ci > 0) Look(c - 1);
                        if (ci < w - 1) Look(c + 1);
                        if (cj > 0) Look(c - w);
                        if (cj < h - 1) Look(c + w);
                    }
                    if (!touchesBody || tooDeep) continue;
                    foreach (var c in piece) region[c] = true;

                    void Look(int nb)
                    {
                        if (region[nb]) { touchesBody = true; return; }
                        if (cand[nb]) { if (!seen[nb]) { seen[nb] = true; pq.Enqueue(nb); } return; }
                        // A free neighbour that is neither body nor candidate is farther than a corner reaches (a crack),
                        // unless another core is nearer to it (the middle of a short passage).
                        if (ok[nb] && dc[nb] < dox[nb]) tooDeep = true;
                    }
                }
            touchesBorder = TouchesBorder(region, w, h);
            return region;
        }

        private static bool TouchesBorder(bool[] region, int w, int h)
        {
            for (int i = 0; i < w; i++) if (region[w + i] || region[(h - 2) * w + i]) return true;
            for (int j = 0; j < h; j++) if (region[j * w + 1] || region[j * w + w - 2]) return true;
            return false;
        }

        /// <summary>The free cell under the click, or the nearest one within 1.5 m (a click on the curb's edge).</summary>
        private static int SeedCell(WallField f, float2 seed, bool[] ok)
        {
            var c = f.CellOf(seed);
            if (f.InRange(c.x, c.y) && ok[f.Index(c.x, c.y)]) return f.Index(c.x, c.y);
            int rad = math.max(1, (int)math.ceil(1.5f / f.Cell));
            int best = -1;
            float bd = float.MaxValue;
            for (int dj = -rad; dj <= rad; dj++)
                for (int di = -rad; di <= rad; di++)
                {
                    int i = c.x + di, j = c.y + dj;
                    if (!f.InRange(i, j) || !ok[f.Index(i, j)]) continue;
                    float dd = math.distancesq(f.Center(i, j), seed);
                    if (dd < bd && dd <= 1.5f * 1.5f + f.Cell * f.Cell) { bd = dd; best = f.Index(i, j); }
                }
            return best;
        }

        /// <summary>
        /// The core cell (farther than <paramref name="t"/> from every wall) nearest to the click through free land, within
        /// a few gaps of it; -1 when none. <paramref name="widest"/>: the largest wall distance seen on the way.
        /// </summary>
        private static int NearestCore(WallField f, bool[] ok, int s, float t, out float widest)
        {
            int w = f.W, h = f.H;
            var d = f.Dist;
            widest = d[s];
            if (d[s] > t) return s;
            float limit = 3f * t + 12f;
            int maxSteps = (int)math.ceil(limit / f.Cell);
            var dist = new Dictionary<int, int> { [s] = 0 };
            var q = new Queue<int>();
            q.Enqueue(s);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int steps = dist[c];
                if (steps >= maxSteps) continue;
                int ci = c % w, cj = c / w;
                for (int m = 0; m < 4; m++)
                {
                    int nb = m == 0 ? (ci > 0 ? c - 1 : -1) : m == 1 ? (ci < w - 1 ? c + 1 : -1) : m == 2 ? (cj > 0 ? c - w : -1) : (cj < h - 1 ? c + w : -1);
                    if (nb < 0 || !ok[nb] || dist.ContainsKey(nb)) continue;
                    dist[nb] = steps + 1;
                    if (d[nb] > widest) widest = d[nb];
                    if (d[nb] > t) return nb;
                    q.Enqueue(nb);
                }
            }
            return -1;
        }

        // ───────────── the outline ─────────────

        private static void Outline(FillWindow win, bool[] region, float2 seed, float reach, FillOptions o, FillResult r)
        {
            var f = win.Field;
            int w = f.W, h = f.H, n = w * h;
            float r2 = reach * reach;
            // Doors: free cells outside the region next to it (closed gaps, cut-off cracks); beyond the reach: the reach.
            var door = new bool[n];
            bool hitReach = false;
            for (int k = 0; k < n; k++)
            {
                if (!region[k]) continue;
                int i = k % w, j = k / w;
                for (int m = 0; m < 4; m++)
                {
                    int nb = m == 0 ? (i > 0 ? k - 1 : -1) : m == 1 ? (i < w - 1 ? k + 1 : -1) : m == 2 ? (j > 0 ? k - w : -1) : (j < h - 1 ? k + w : -1);
                    if (nb < 0 || region[nb] || f.State[nb] != WallField.Free) continue;
                    if (math.distancesq(f.Center(nb), seed) > r2) hitReach = true;
                    else door[nb] = true;
                }
            }
            r.HitReach = hitReach;
            // The band: the region plus the wall cells within two cells of it (the exact cut trims them back to the wall's
            // edge; two so the simplified band never dips inside a slanted wall), except wall cells at a door's jambs (a door
            // ends at the wall, it does not wrap round its end).
            var band = new bool[n];
            var jamb = new bool[n];
            for (int k = 0; k < n; k++)
            {
                if (region[k]) band[k] = true;
                bool edge = region[k] && Edge(k), isDoor = door[k];
                if (!edge && !isDoor) continue;
                int i = k % w, j = k / w;
                for (int dj = -2; dj <= 2; dj++)
                    for (int di = -2; di <= 2; di++)
                    {
                        int x = i + di, y = j + dj;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        int nb = y * w + x;
                        if (isDoor) jamb[nb] = true;
                        else if (f.State[nb] == WallField.Wall) band[nb] = true;
                    }
            }
            for (int k = 0; k < n; k++) if (jamb[k] && !region[k]) band[k] = false;

            bool Edge(int k)
            {
                int i = k % w, j = k / w;
                return i == 0 || j == 0 || i == w - 1 || j == h - 1 || !region[k - 1] || !region[k + 1] || !region[k - w] || !region[k + w];
            }
            var raw = CellTrace.Rings(band, w, h, f.Origin, f.Cell);
            var rings = new List<float2[]>(raw.Count);
            float tol = 0.45f * f.Cell;
            foreach (var ring in raw)
            {
                var simple = Poly.Simplify(ring, tol);
                bool keep = simple.Length >= 3 && Poly.IsSimple(simple) && math.sign(Poly.SignedArea(simple)) == math.sign(Poly.SignedArea(ring));
                rings.Add(keep ? simple : ring);
            }
            r.Rough = rings;
            if (rings.Count == 0) { r.Status = FillStatus.Nothing; return; }

            // Exact: minus the walls along the band (and within the reach circle where the reach was met).
            float2 bmin = new float2(float.MaxValue), bmax = new float2(float.MinValue);
            foreach (var ring in rings) foreach (var p in ring) { bmin = math.min(bmin, p); bmax = math.max(bmax, p); }
            var clip = new List<IReadOnlyList<float2>>();
            foreach (var wall in win.Walls)
            {
                if (wall.Ring == null || wall.Ring.Length < 3) continue;
                Poly.Bounds(wall.Ring, out var wmin, out var wmax);
                if (wmax.x < bmin.x || wmax.y < bmin.y || wmin.x > bmax.x || wmin.y > bmax.y) continue;
                if (!TouchesBand(f, band, wmin, wmax)) continue;
                clip.Add(Poly.IsCcw(wall.Ring) ? wall.Ring : Poly.Reversed(wall.Ring));
            }
            r.WallsCut = clip.Count;
            List<Region> pieces = o.Rough || clip.Count == 0
                ? Poly.Boolean(rings, null, BoolOp.Union, FillRule.NonZero)
                : Poly.Boolean(rings, clip, BoolOp.Difference, FillRule.NonZero);
            if (hitReach)
            {
                var circle = Circle(seed, reach, math.clamp((int)(reach / 4f), 48, 400));
                pieces = Poly.Boolean(pieces, new[] { new Region(circle) }, BoolOp.Intersection);
            }
            var outer = FillWalls.PickPiece(pieces, seed, out _);
            if (outer == null) { r.Status = FillStatus.Nothing; return; }
            r.Ring = outer;
            if (Poly.Area(outer) < o.MinArea) { r.Status = FillStatus.TooSmall; return; }
            r.Status = FillStatus.Ok;
            r.Doors = DoorStretches(f, outer, hitReach, seed, reach);
        }

        private static bool TouchesBand(WallField f, bool[] band, float2 mn, float2 mx)
        {
            var a = f.CellOf(mn);
            var b = f.CellOf(mx);
            int i0 = math.max(0, a.x - 1), j0 = math.max(0, a.y - 1), i1 = math.min(f.W - 1, b.x + 1), j1 = math.min(f.H - 1, b.y + 1);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (band[j * f.W + i]) return true;
            return false;
        }

        public static float2[] Circle(float2 c, float radius, int sides)
        {
            var d = new float2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * 2f * math.PI / sides;
                d[i] = c + new float2(math.cos(a), math.sin(a)) * radius;
            }
            return d;
        }

        /// <summary>Stretches of the outline that lie on no wall and not on the reach circle: the closed gaps.</summary>
        private static List<float2[]> DoorStretches(WallField f, float2[] ring, bool hitReach, float2 seed, float reach)
        {
            var doors = new List<float2[]>();
            int n = ring.Length;
            var isDoor = new bool[n];
            for (int k = 0; k < n; k++)
            {
                float2 a = ring[k], b = ring[(k + 1) % n];
                float len = math.distance(a, b);
                int samples = math.max(1, (int)(len / math.max(1f, f.Cell)));
                int open = 0, counted = 0;
                for (int s = 0; s < samples; s++)
                {
                    float2 p = math.lerp(a, b, (s + 0.5f) / samples);
                    if (hitReach && math.abs(math.distance(p, seed) - reach) < 2f * f.Cell) continue;
                    var c = f.CellOf(p);
                    if (!f.InRange(c.x, c.y)) continue;
                    counted++;
                    // On a wall's edge the nearest wall is within a cell or so; a door has free land on both sides.
                    int idx = f.Index(c.x, c.y);
                    if (f.State[idx] == WallField.Free && f.Dist[idx] > 0.6f * f.Cell) open++;
                }
                isDoor[k] = counted > 0 && open * 3 > counted;
            }
            int startAt = -1;
            for (int k = 0; k < n; k++) if (!isDoor[k]) { startAt = k; break; }
            if (startAt < 0) return doors;
            List<float2> cur = null;
            for (int m = 1; m <= n; m++)
            {
                int k = (startAt + m) % n;
                if (isDoor[k])
                {
                    cur ??= new List<float2> { ring[k] };
                    cur.Add(ring[(k + 1) % n]);
                }
                else if (cur != null) { doors.Add(cur.ToArray()); cur = null; }
            }
            if (cur != null) doors.Add(cur.ToArray());
            return doors;
        }
    }
}
