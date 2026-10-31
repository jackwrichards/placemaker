// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/ParkingVegetation.cs.
// Translated. The static density factor became a parameter; the "line" mode was dropped (unused
// by the parking layer); placement is deterministic per seed and position.
//
// Plants fill the green rings on a jittered grid; trees keep away from the strip edge more than
// bushes, clumping follows a smooth value noise, and spacing is enforced through a 16 m bucket grid.
//
// rf-parking (2026-10-16): the green strips behind stall rows (medians, the edge strip behind a perimeter row) are
// planted first, in rows (PlanRows): trees evenly spaced on the strip's centre line at their crown size, shrubs
// between them. The jittered fill then only adds to what is left (row ends, corners, wide islands), and trees there
// need room for a trunk well inside the green. In the game the old fill put a 10 m crown every 4 m anywhere in a
// 2.5 m median (the catalog's plant bounds are the collision core, not the crown): clumps over the aisles.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal sealed class Species
    {
        public bool Tree;
        public double Spacing;
        public double Height;
    }

    /// <summary>One stall's back edge, seen from the stall: a back corner, the row direction and which way is back.</summary>
    internal struct RowEdge
    {
        public double2 Corner;     // a back corner of a stall (local frame)
        public double2 RowDir;     // along the row
        public double2 Back;       // unit, perpendicular to the row, toward the strip behind the stall
        /// <summary>A row-end cap (the strip beside the last stall of a row): shrubs only, no trees.</summary>
        public bool Cap;
    }

    internal static class Vegetation
    {
        /// <summary>Hard cap on candidate positions (PLT Kandidatengrenze).</summary>
        private const int CandidateLimit = 400000;

        /// <summary>Trees in the jittered fill keep their trunk this far inside the green (a 2.5 m median has no such spot).</summary>
        public const double TreeEdge = 1.4;
        /// <summary>A planting line needs a strip this wide for trees, and this wide for shrubs.</summary>
        public const double MinTreeStrip = 0.7, MinShrubStrip = 0.5;
        /// <summary>The jittered fill keeps this far from any plant of a planting line.</summary>
        public const double RowClear = 4.0;

        public static List<Plant> Plan(List<List<double2>> rings, IReadOnlyList<Species> species, int density, uint seed,
            IReadOnlyList<Lantern> lanterns, Func<double2, bool> blocked, List<Plant> seeded = null)
        {
            var plants = new List<Plant>();
            if (density <= 0 || species.Count == 0 || rings == null) return plants;
            var occupied = new Dictionary<long, List<int>>();
            if (seeded != null)
                foreach (var sp in seeded)
                {
                    long sk = Key((int)Math.Floor(sp.Position.x / 16), (int)Math.Floor(sp.Position.y / 16));
                    if (!occupied.TryGetValue(sk, out var sl)) occupied[sk] = sl = new List<int>();
                    sl.Add(plants.Count);
                    plants.Add(sp);
                }
            var anchor = new double2(double.MaxValue, double.MaxValue);
            foreach (var r in rings) foreach (var p in r) anchor = math.min(anchor, p);
            int candidates = 0;
            // (perf) lanterns by 8 m cell (every candidate asked every lantern)
            var lamps = lanterns != null && lanterns.Count > 0 ? new PointGrid(8) : null;
            if (lamps != null) foreach (var l in lanterns) lamps.Add(l.Position);

            foreach (var ring in rings)
            {
                if (ring == null || ring.Count < 3) continue;
                var origin = ring[0];
                var axis = new double2(1, 0); double longest = 0;
                for (int i = 0; i < ring.Count; i++)
                {
                    var d = ring[(i + 1) % ring.Count] - ring[i];
                    double l = PGeo.Dot(d, d);
                    if (l > longest) { longest = l; axis = PGeo.Norm(d); }
                }
                if (longest < 0.01) continue;
                var offset = origin - anchor;
                uint areaSeed = Hash(seed ^ Hash((uint)(int)Math.Round(offset.x * 16)) ^ Hash((uint)(int)Math.Round(offset.y * 16) + 7919u));
                var across = PGeo.Perp(axis);
                var local = new List<double2>(ring.Count);
                var lo = new double2(double.MaxValue, double.MaxValue); var hi = new double2(double.MinValue, double.MinValue);
                foreach (var p in ring)
                {
                    var d = p - origin;
                    var l = new double2(PGeo.Dot(d, axis), PGeo.Dot(d, across));
                    local.Add(l); lo = math.min(lo, l); hi = math.max(hi, l);
                }
                // (perf) the ring's edges by grid cell for the inside / distance-to-edge tests (same answers)
                var localGrid = local.Count > 16 ? new RingGrid(local) : null;
                for (int pass = 0; pass < 2; pass++)
                {
                    var pool = new List<int>();
                    for (int i = 0; i < species.Count; i++) if (species[i].Tree == (pass == 0)) pool.Add(i);
                    if (pool.Count == 0) continue;
                    double narrowest = double.MaxValue;
                    foreach (int i in pool) narrowest = Math.Min(narrowest, Spacing(species[i]));
                    double step = Math.Max(2 / Math.Sqrt(3), narrowest * 0.5);
                    int nx = Math.Max(1, (int)Math.Floor((hi.x - lo.x) / step));
                    int ny = Math.Max(1, (int)Math.Floor((hi.y - lo.y) / step));
                    var cells = GridCells(local, lo, hi, nx, ny);
                    long total = cells.Count * 2L;
                    if (total == 0) continue;
                    long stride = 65537;
                    while (Gcd(stride, total) != 1) stride += 2;
                    for (long k = 0; k < total; k++)
                    {
                        if (++candidates > CandidateLimit) return Thin(plants, density);
                        var cell = cells[(int)(k * stride % total / 2)];
                        int ix = cell.x, iy = cell.y;
                        uint s = Hash((uint)(ix + iy * 65537 + pass * 104729 + 1));
                        s = Hash(s ^ areaSeed ^ Hash((uint)(k * stride % total % 2) + 37u));
                        int which = pool[(int)(Hash(s) % (uint)pool.Count)];
                        var kind = species[which];
                        var p = new double2(lo.x + (hi.x - lo.x) * (ix + .5) / nx, lo.y + (hi.y - lo.y) * (iy + .5) / ny);
                        p += new double2(Unit(Hash(s + 7)) - .5, Unit(Hash(s + 11)) - .5) * (hi - lo) / new double2(nx, ny);
                        if (!(localGrid != null ? localGrid.Contains(p) : PGeo.Contains(local, p))) continue;
                        // only the first 4 m of the distance matter (the chance saturates there)
                        double edge = Math.Min(localGrid != null ? localGrid.Dist(p, 4.0) : PGeo.DistToBoundary(local, p), 4.0);
                        if (edge < (kind.Tree ? TreeEdge : .10)) continue;
                        double chance = kind.Tree ? Lerp(.3, 1, Sat(edge / 4)) : Lerp(1, .7, Sat(edge / 4));
                        var world = origin + axis * p.x + across * p.y;
                        double group = GroupDensity((world - anchor) / (kind.Tree ? 18.0 : 7.0), seed + (uint)pass * 313u);
                        chance *= Lerp(.35, 1, group);
                        if (Unit(Hash(s + 23)) > chance) continue;
                        // a tree taller than the mast must keep 3 m off the lantern (PLT LaternenKoerper)
                        if (lamps != null && lamps.AnyWithin(world, kind.Tree ? 3.0 : 0.5)) continue;
                        int gx = (int)Math.Floor(world.x / 16), gy = (int)Math.Floor(world.y / 16);
                        bool clear = true;
                        for (int dy = -1; dy <= 1 && clear; dy++)
                            for (int dx = -1; dx <= 1 && clear; dx++)
                                if (occupied.TryGetValue(Key(gx + dx, gy + dy), out var near))
                                    foreach (int n in near)
                                    {
                                        var other = plants[n];
                                        double dist = (Spacing(kind) + Spacing(species[other.Species])) * .5;
                                        if (other.Row) dist = Math.Max(dist, RowClear);   // the lines stay lines: no stray fill beside them
                                        if (math.distancesq(world, other.Position) < dist * dist - .001) { clear = false; break; }
                                    }
                        if (!clear) continue;
                        // (perf) the stall / charger / reserved test after the spacing test, which drops more (both only filter)
                        if (blocked != null && blocked(world)) continue;
                        long key = Key(gx, gy);
                        if (!occupied.TryGetValue(key, out var list)) occupied[key] = list = new List<int>();
                        list.Add(plants.Count);
                        plants.Add(new Plant { Position = world, Species = which, Seed = s, Tree = kind.Tree });
                    }
                }
            }
            return Thin(plants, density);
        }

        private static List<Plant> Thin(List<Plant> plants, int density)
        {
            double keep = Math.Max(0, Math.Min(100, density)) / 100.0;
            plants.RemoveAll(p => !p.Row && Unit(Hash(p.Seed + 97)) >= keep);
            return plants;
        }

        /// <summary>
        /// Spacing factor of the planting lines for the density setting (P2-5: denser, the user found the median trees sparse):
        /// 50 % (the default) lets the crowns just touch, 100 % closes the canopy, 20 % leaves wide gaps.
        /// Was: 50 % left a fifth of a crown
        /// between trees, 80 % lets the crowns just meet, 100 % closes the canopy, 20 % leaves wide gaps.
        /// </summary>
        public static double RowSpacingFactor(int density) => 0.5 + 25.0 / Math.Max(5, Math.Min(100, density));

        /// <summary>
        /// Trees and shrubs in rows on the centre line of the green strip behind every stall row. The strip is found by
        /// probing from each stall's back corners straight back (perpendicular to the row) while the ground is green;
        /// its middle is the planting point. Points on one line are grouped (same direction and offset), split where
        /// the line breaks (cross lanes, row ends), and planted symmetrically: a tree every k-th point (k from the
        /// crown size and the density), shrubs between the trees. One tree species per line.
        /// </summary>
        public static List<Plant> PlanRows(IReadOnlyList<RowEdge> edges, IReadOnlyList<Species> species, int density, uint seed,
            IReadOnlyList<Lantern> lanterns, Func<double2, bool> isGreen, Func<double2, bool> blocked)
        {
            var plants = new List<Plant>();
            if (density <= 0 || species.Count == 0 || edges == null || edges.Count == 0) return plants;
            var treeKinds = new List<int>(); var shrubKinds = new List<int>();
            for (int i = 0; i < species.Count; i++) (species[i].Tree ? treeKinds : shrubKinds).Add(i);
            double factor = RowSpacingFactor(density);
            var lamps = lanterns != null && lanterns.Count > 0 ? new PointGrid(8) : null;
            if (lamps != null) foreach (var l in lanterns) lamps.Add(l.Position);

            // 1. the middle of the strip behind each back corner
            var pts = new List<(double2 P, double2 Dir, double Width, bool Cap)>();
            var seen = new Dictionary<long, List<int>>();
            const double Step = 0.3, MaxProbe = 12;
            foreach (var e in edges)
            {
                var start = e.Corner + e.Back * 0.05;
                if (!isGreen(start + e.Back * 0.1)) continue;
                double d = 0.1;
                while (d < MaxProbe && isGreen(start + e.Back * (d + Step))) d += Step;
                // the far edge to a few centimetres: halve the last step
                for (double h = Step * 0.5; h > 0.02; h *= 0.5) if (d + h < MaxProbe && isGreen(start + e.Back * (d + h))) d += h;
                if (d < MinShrubStrip) continue;
                var c = start + e.Back * (d * 0.5);
                // the strip must run along the row here (not a row-end cap seen from its last stall)
                if (!isGreen(c + e.RowDir * 0.6) || !isGreen(c - e.RowDir * 0.6)) continue;
                int gx = (int)Math.Floor(c.x / 4), gy = (int)Math.Floor(c.y / 4);
                bool dup = false;
                for (int dy = -1; dy <= 1 && !dup; dy++)
                    for (int dx = -1; dx <= 1 && !dup; dx++)
                        if (seen.TryGetValue(Key(gx + dx, gy + dy), out var near))
                            foreach (int n in near) if (math.distancesq(pts[n].P, c) < 0.6 * 0.6) { dup = true; break; }
                if (dup) continue;
                if (!seen.TryGetValue(Key(gx, gy), out var cell)) seen[Key(gx, gy)] = cell = new List<int>();
                cell.Add(pts.Count);
                pts.Add((c, e.RowDir, d, e.Cap));
            }
            if (pts.Count == 0) return plants;

            // 2. lines: the same direction (either sign) and the same offset across it
            var lines = new List<List<(double2 P, double Width)>>();
            var lineKey = new List<(double2 Dir, double Off, bool Cap)>();
            foreach (var q in pts)
            {
                var dir = q.Dir.x < -1e-6 || (Math.Abs(q.Dir.x) <= 1e-6 && q.Dir.y < 0) ? -q.Dir : q.Dir;
                double off = PGeo.Dot(q.P, PGeo.Perp(dir));
                int found = -1;
                for (int i = 0; i < lines.Count; i++)
                    if (lineKey[i].Cap == q.Cap && PGeo.Dot(lineKey[i].Dir, dir) > 0.999 && Math.Abs(lineKey[i].Off - off) < 2.0) { found = i; break; }
                if (found < 0) { lines.Add(new List<(double2, double)>()); lineKey.Add((dir, off, q.Cap)); found = lines.Count - 1; }
                lines[found].Add((q.P, q.Width));
            }

            var trees = new PointGrid();
            var shrubs = new PointGrid();
            var runs = new List<(List<(double2 P, double Width)> Run, double2 Dir, double Pitch, int Index, bool Cap)>();
            for (int li = 0; li < lines.Count; li++)
            {
                var dir = lineKey[li].Dir;
                var across = PGeo.Perp(dir);
                var raw = lines[li];
                // one straight line: every point onto the line's middle offset (the two rows of a median can see its
                // middle a little apart where one row ends earlier), where that is still green; then no two points closer than 1 m
                var offs = new List<double>();
                foreach (var q in raw) offs.Add(PGeo.Dot(q.P, across));
                offs.Sort();
                double mid = offs[offs.Count / 2];
                var line = new List<(double2 P, double Width)>(raw.Count);
                foreach (var q in raw)
                {
                    var moved = q.P + across * (mid - PGeo.Dot(q.P, across));
                    line.Add((isGreen(moved) ? moved : q.P, q.Width));
                }
                line.Sort((a, b) => PGeo.Dot(a.P, dir).CompareTo(PGeo.Dot(b.P, dir)));
                for (int i = line.Count - 1; i > 0; i--)
                    if (PGeo.Dot(line[i].P - line[i - 1].P, dir) < 1.0) line.RemoveAt(i);
                // the usual gap between neighbours on this line (the stall pitch)
                var gaps = new List<double>();
                for (int i = 1; i < line.Count; i++) gaps.Add(PGeo.Dot(line[i].P - line[i - 1].P, dir));
                gaps.Sort();
                double pitch = gaps.Count > 0 ? Math.Max(1.0, gaps[gaps.Count / 2]) : 3.0;
                // 3. runs: the line breaks where a gap is much wider than the pitch or the strip is cut (a cross lane)
                int from = 0;
                for (int i = 1; i <= line.Count; i++)
                {
                    if (i < line.Count && PGeo.Dot(line[i].P - line[i - 1].P, dir) < pitch * 1.6 && Unbroken(line[i - 1].P, line[i].P)) continue;
                    runs.Add((line.GetRange(from, i - from), dir, pitch, runs.Count, lineKey[li].Cap));
                    from = i;
                }
            }
            // the long lines first, so a stray stall (a special bay across a row end) never takes a tree from a median
            runs.Sort((a, b) => a.Run.Count != b.Run.Count ? b.Run.Count.CompareTo(a.Run.Count) : a.Index.CompareTo(b.Index));
            foreach (var r in runs) PlantRun(r.Run, r.Dir, r.Pitch, r.Index, r.Cap);
            return plants;

            bool Unbroken(double2 a, double2 b)
            {
                int n = Math.Max(1, (int)Math.Ceiling(math.distance(a, b) / 0.4));
                for (int j = 1; j < n; j++) if (!isGreen(math.lerp(a, b, (double)j / n))) return false;
                return true;
            }

            void PlantRun(List<(double2 P, double Width)> run, double2 dir, double pitch, int index, bool cap)
            {
                uint rs = Hash(seed ^ Hash((uint)index * 2654435761u + 17u));
                double runLen = PGeo.Dot(run[run.Count - 1].P - run[0].P, dir);
                if (treeKinds.Count > 0 && run.Count >= 3 && !cap)
                {
                    int kindT = treeKinds[(int)(rs % (uint)treeKinds.Count)];
                    double spacing = Spacing(species[kindT]) * factor;
                    int k = Math.Max(1, (int)Math.Round(spacing / pitch));
                    int count = run.Count;
                    int first = ((count - 1) % k) / 2;
                    if (count <= k && runLen + pitch < spacing * 0.6) first = count;   // too short for a crown
                    for (int i = first; i < count; i += k)
                    {
                        var q = run[i];
                        if (q.Width < MinTreeStrip || Near(q.P, true)) continue;
                        if (trees.AnyWithin(q.P, spacing * 0.85)) continue;
                        trees.Add(q.P);
                        plants.Add(new Plant { Position = q.P, Tree = true, Species = kindT, Seed = Hash(rs + (uint)i * 31u), Row = true });
                    }
                }
                if (shrubKinds.Count == 0) return;
                // shrubs between the trees: on the points and halfway between them, off the trunks
                double shrubStep = 0;
                foreach (int b in shrubKinds) shrubStep = Math.Max(shrubStep, Spacing(species[b]));
                shrubStep *= factor;
                const double TreeClear = 1.8;
                var last = new double2(double.NaN, double.NaN);
                int n = 0;
                for (int i = 0; i < run.Count; i++)
                    for (int h = 0; h < 2; h++)
                    {
                        if (h == 1 && i == run.Count - 1) break;
                        var p = h == 0 ? run[i].P : (run[i].P + run[i + 1].P) * 0.5;
                        double w = h == 0 ? run[i].Width : Math.Min(run[i].Width, run[i + 1].Width);
                        if (w < MinShrubStrip) continue;
                        if (!double.IsNaN(last.x) && math.distance(last, p) < shrubStep - 1e-6) continue;
                        if (trees.AnyWithin(p, TreeClear) || Near(p, false)) continue;
                        // lines that meet (cap ends, the wedges of angled rows) do not double up
                        if (shrubs.AnyWithin(p, shrubStep * 0.7)) continue;
                        shrubs.Add(p);
                        last = p;
                        int kindS = shrubKinds[(int)((rs / 7 + (uint)n) % (uint)shrubKinds.Count)];
                        n++;
                        plants.Add(new Plant { Position = p, Tree = false, Species = kindS, Seed = Hash(rs + 977u + (uint)(i * 2 + h)), Row = true });
                    }
            }

            bool Near(double2 p, bool tree)
            {
                if (lamps != null && lamps.AnyWithin(p, tree ? 3.0 : 0.8)) return true;
                return blocked != null && blocked(p);
            }
        }

        /// <summary>Points by 16 m cell (or the given size), for "anything closer than r".</summary>
        private sealed class PointGrid
        {
            private readonly Dictionary<long, List<double2>> _cells = new Dictionary<long, List<double2>>();
            private readonly double _size;
            public PointGrid(double size = 16) { _size = size; }
            public void Add(double2 p)
            {
                long k = Key((int)Math.Floor(p.x / _size), (int)Math.Floor(p.y / _size));
                if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<double2>();
                l.Add(p);
            }
            public bool AnyWithin(double2 p, double r)
            {
                int gx = (int)Math.Floor(p.x / _size), gy = (int)Math.Floor(p.y / _size);
                double r2 = r * r;
                int reach = Math.Max(1, (int)Math.Ceiling(r / _size));
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                        if (_cells.TryGetValue(Key(gx + dx, gy + dy), out var l))
                            foreach (var q in l) if (math.distancesq(q, p) < r2) return true;
                return false;
            }
        }

        private static List<int2> GridCells(List<double2> ring, double2 lo, double2 hi, int nx, int ny)
        {
            var cells = new List<int2>();
            double width = hi.x - lo.x;
            if (width <= 0) return cells;
            for (int iy = 0; iy < ny; iy++)
            {
                double py = lo.y + (hi.y - lo.y) * (iy + .5) / ny;
                foreach (var iv in PGeo.IntervalsAt(ring, py))
                {
                    int from = Math.Max(0, (int)Math.Ceiling((iv.x - lo.x) * nx / width - .5));
                    int to = Math.Min(nx - 1, (int)Math.Floor((iv.y - lo.x) * nx / width - .5));
                    for (int ix = from; ix <= to; ix++) cells.Add(new int2(ix, iy));
                }
            }
            return cells;
        }

        public static double Spacing(Species s) => Math.Max(0.5, s.Spacing);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static double Sat(double v) => Math.Max(0, Math.Min(1, v));
        private static long Gcd(long a, long b) { while (b != 0) { long n = a % b; a = b; b = n; } return a; }
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
        internal static uint Hash(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; return x ^ (x >> 16); } }
        internal static double Unit(uint x) => (x & 0xffffff) / 16777216.0;

        private static double GroupDensity(double2 p, uint seed)
        {
            int x = (int)Math.Floor(p.x), y = (int)Math.Floor(p.y);
            var t = p - new double2(x, y);
            t = t * t * (3 - 2 * t);
            double V(int a, int b) => Unit(Hash(unchecked((uint)a * 73856093u ^ (uint)b * 19349663u ^ seed)));
            return Lerp(Lerp(V(x, y), V(x + 1, y), t.x), Lerp(V(x, y + 1), V(x + 1, y + 1), t.x), t.y);
        }
    }
}
