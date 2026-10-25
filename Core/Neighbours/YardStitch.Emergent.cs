// Placemaker neighbourhood polish — the emergent split of the land between gardens (pure geometry, engine-free).
//
// Two ideas instead of a rule per situation (the user, 24 Oct: rows meeting at the back at an angle "take jagged edges and steal
// land from each other"; "the minimum amount of turns to make the full space covered, while also letting every zone have roughly
// an equal piece of the pie ... think of how a real architect would do it"):
//   1. Fair: every bit of open land within a garden's reach goes to the lot nearest to it (distance to the lot, not to the house:
//      two rows' back edges meet on the line half way between them, however the rows are angled).
//   2. Straight: each edge two gardens share is drawn with as few straight pieces as stay within a tolerance of that line. Both
//      gardens draw it from the same points, so they meet exactly: no gaps, no overlaps, no notches.
// The land is looked at on a grid aligned to the world (the same cells in every pass), only around the gardens the plan edits.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static partial class YardStitch
    {
        /// <summary>
        /// Each edited garden's plot: the cells nearest its lot (within <paramref name="reach"/>, its convex reach), outlined and
        /// straightened (knobs emergentCell, the grid's cell in metres, and emergentTol, how far a straightened edge may stray).
        /// </summary>
        private static void Emergent(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, List<float2>[] reach,
            List<float2>[] wide, List<(int I, int J)> alleys, StitchSettings s, StitchPlan plan)
        {
            int n = houses.Count;
            var clock = AllocatedBytes != null ? System.Diagnostics.Stopwatch.StartNew() : null;
            var times = clock != null ? new List<string>() : null;
            void Lap(string what) { if (clock == null) return; times.Add($"{what} {clock.Elapsed.TotalMilliseconds:F2}"); clock.Restart(); }
            float cell = math.clamp(s.Knob("emergentCell", 1f), 0.25f, 4f);
            float tol = math.max(0f, s.Knob("emergentTol", 1.5f));
            // near to what: the lot's street front (1: the land between two rows backing onto each other is split half way between
            // their streets, one line however deep each lot is, as plots are laid out) or the lot itself (0)
            float frontShare = math.saturate(s.Knob("emergentFront", 1f));
            // a lot comes with a strip of its own all round (m): the gaps between two lots' corners on a curve are theirs, not a
            // tooth of the garden across the way
            float apron = math.max(0f, s.Knob("emergentApron", 3f));
            float apronSq = apron * apron;
            // the edited gardens (a fixed one keeps the garden it has; FixedGardens)
            var edit = new List<int>();
            for (int i = 0; i < n; i++) if (touched[i] && houses[i].Edit && houses[i].Polished && reach[i] != null && reach[i].Count >= 3) edit.Add(i);
            if (edit.Count == 0) return;
            var lo = new float2(float.MaxValue);
            var hi = new float2(float.MinValue);
            foreach (int i in edit) { lo = math.min(lo, MinOf(wide[i])); hi = math.max(hi, MaxOf(wide[i])); }
            // (on the world's grid: a pass next door sees the very same cells)
            lo = math.floor(lo / cell) * cell - cell;
            hi = math.ceil(hi / cell) * cell + cell;
            int w = (int)math.round((hi.x - lo.x) / cell), h = (int)math.round((hi.y - lo.y) / cell);
            if (w <= 0 || h <= 0 || (long)w * h > 4_000_000) { plan.Notes.Add($"emergent: {w} x {h} cells, too many: the old split"); return; }

            // the houses that compete for this land: polished ones by their reach (their lot only when they have none), an
            // unpolished lot is nobody's land (the polished garden beside it reaches its edge)
            // (each by two reaches: its own, where it grows on its own, and a wider one, where it grows only into land another
            // garden reaches too: the land between houses is shared, the open land past a row's last house is nobody's)
            var seeds = new List<int>();
            var seedPoly = new List<float2[]>();
            var seedWide = new List<float2[]>();
            // (a garden another pass planned is final: its house claims only the land in it, and the gardens planned now reach
            // its edge exactly, FixedGardens cutting it out; before, both sides drew the line between them by nearness, a
            // little differently in each pass: slivers and crossings where they met)
            var seedGarden = new List<float2[]>();
            var seedGardenLo = new List<float2>();
            var seedGardenHi = new List<float2>();
            for (int j = 0; j < n; j++)
            {
                if (!houses[j].Polished) continue;
                bool grows = touched[j] && reach[j] != null && reach[j].Count >= 3;
                var r = Ccw(grows ? reach[j].ToArray() : Lot(houses[j]));
                var wr = grows && wide[j] != null && wide[j].Count >= 3 ? Ccw(wide[j].ToArray()) : r;
                var rlo = MinOf(wr);
                var rhi = MaxOf(wr);
                if (rhi.x < lo.x || rlo.x > hi.x || rhi.y < lo.y || rlo.y > hi.y) continue;
                seeds.Add(j);
                seedPoly.Add(r);
                seedWide.Add(wr);
                var fixedGarden = IsFixed(houses[j]) && s.Knob("emergentFixed", 0f) > 0.5f ? Ccw(houses[j].Garden) : null;
                seedGarden.Add(fixedGarden);
                seedGardenLo.Add(fixedGarden != null ? MinOf(fixedGarden) : default);
                seedGardenHi.Add(fixedGarden != null ? MaxOf(fixedGarden) : default);
            }
            // filed by 16 m buckets
            const float bucket = 16f;
            int bw = (int)math.ceil(w * cell / bucket), bh = (int)math.ceil(h * cell / bucket);
            var buckets = new List<int>[bw * bh];
            for (int k = 0; k < seeds.Count; k++)
            {
                var r = seedWide[k];
                var rlo = math.max(MinOf(r), lo);
                var rhi = math.min(MaxOf(r), hi);
                if (rhi.x < rlo.x || rhi.y < rlo.y) continue;
                int x0 = (int)((rlo.x - lo.x) / bucket), x1 = math.min(bw - 1, (int)((rhi.x - lo.x) / bucket));
                int y0 = (int)((rlo.y - lo.y) / bucket), y1 = math.min(bh - 1, (int)((rhi.y - lo.y) / bucket));
                for (int by = y0; by <= y1; by++)
                    for (int bx = x0; bx <= x1; bx++)
                        (buckets[by * bw + bx] ??= new List<int>()).Add(k);
            }
            // land nobody gets: roads, paths, tracks, buildings, water, other lots (the barriers), walkways, unpolished lots
            var blocked = new bool[w * h];
            foreach (var b in s_barriers)
                if (!(b.Max.x < lo.x || b.Min.x > hi.x || b.Max.y < lo.y || b.Min.y > hi.y)) Fill(blocked, w, h, lo, cell, b.Ring);
            foreach (var (ai, bi) in alleys)
            {
                var a = houses[ai];
                var b = houses[bi];
                float hz = math.min(a.HalfLot.y, b.HalfLot.y);
                Fill(blocked, w, h, lo, cell, new[] { a.FrontRight, b.FrontLeft, BackAt(b, -1, hz), BackAt(a, +1, hz) });
            }
            for (int j = 0; j < n; j++) if (!houses[j].Polished) Fill(blocked, w, h, lo, cell, Lot(houses[j]));

            Lap("setup");
            // only the land an edited garden may reach is worked out, and a cell round it (its outline's neighbours)
            var need = new bool[w * h];
            foreach (int i in edit) Fill(need, w, h, lo, cell, wide[i]);
            var near = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (need[y * w + x])
                        for (int yy = math.max(0, y - 1); yy <= math.min(h - 1, y + 1); yy++)
                            for (int xx = math.max(0, x - 1); xx <= math.min(w - 1, x + 1); xx++) near[yy * w + xx] = true;

            Lap("need");
            // ── 1. fair: each cell to the nearest lot that reaches it ──
            var label = new int[w * h];
            // the cells in a lot or its strip all round (never shaved off a garden, never moved to the other row)
            var own = new bool[w * h];
            // (the way to each reaching lot from the cell: land between houses has houses on opposite sides of it)
            var ways = new float2[64];
            // each owner's cells' box (its outline is looked for there only)
            var box = new int4[n];
            for (int k = 0; k < n; k++) box[k] = new int4(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
            for (int y = 0; y < h; y++)
            {
                int by = math.min(bh - 1, (int)((y + 0.5f) * cell / bucket));
                for (int x = 0; x < w; x++)
                {
                    if (!near[y * w + x]) { label[y * w + x] = -3; continue; }
                    var p = lo + new float2(x + 0.5f, y + 0.5f) * cell;
                    var list = buckets[by * bw + math.min(bw - 1, (int)((x + 0.5f) * cell / bucket))];
                    int best = -1, bestK = -1, reachers = 0;
                    float2 bestWay = default;
                    bool inPlay = false;
                    float bestD = float.MaxValue;
                    if (list != null)
                        foreach (int k in list)
                        {
                            var hk = houses[seeds[k]];
                            var l = hk.ToLocal(p);
                            float ex = math.max(0f, math.abs(l.x) - hk.HalfLot.x), ez = math.max(0f, math.abs(l.y) - hk.HalfLot.y);
                            // (towards the lot's nearest point, in its own frame: only how two of them turn from each other counts)
                            var way = hk.Right * (math.clamp(l.x, -hk.HalfLot.x, hk.HalfLot.x) - l.x) + hk.Forward * (math.clamp(l.y, -hk.HalfLot.y, hk.HalfLot.y) - l.y);
                            float d = ex * ex + ez * ez;
                            // (a lot always holds its own land; elsewhere a barrier holds it)
                            if (d > 0f && blocked[y * w + x]) continue;
                            if (d > 0f && seedGarden[k] != null)
                            {
                                var glo = seedGardenLo[k];
                                var ghi = seedGardenHi[k];
                                if (p.x < glo.x || p.y < glo.y || p.x > ghi.x || p.y > ghi.y || !Poly.Contains(seedGarden[k], p)) continue;
                                d = 0f;   // (its own: nobody else's land)
                            }
                            // (from its street front: depth from the street and how far past its side; or a share of each: the
                            // whole lot is fair to a deep lot, the street gives one line where lots differ in depth)
                            if (d > apronSq && frontShare > 0f)
                            {
                                float fd = math.sqrt(ex * ex + (l.y - hk.HalfLot.y) * (l.y - hk.HalfLot.y));
                                float mixed = math.lerp(math.sqrt(d), fd, frontShare);
                                d = apronSq + 1f + mixed * mixed;
                            }
                            if (d > 0f && !InConvex(seedWide[k], p)) continue;
                            if (reachers < ways.Length) ways[reachers] = math.normalizesafe(way);
                            if (!inPlay && (d == 0f || InConvex(seedPoly[k], p))) inPlay = true;
                            reachers++;
                            if (d >= bestD) continue;
                            bestD = d;
                            best = seeds[k];
                            bestK = k;
                            bestWay = math.normalizesafe(way);
                        }
                    // (land in play: a garden reaches it on its own, or it lies between two houses, a lot the other way more than
                    // 120° round from the way to the nearest; then the nearest takes it)
                    if (best >= 0 && bestD > 0f && !inPlay)
                    {
                        bool between = false;
                        for (int q = 0; q < math.min(reachers, ways.Length) && !between; q++) between = math.dot(ways[q], bestWay) < -0.5f;
                        if (!between) best = -1;
                    }
                    label[y * w + x] = best;
                    own[y * w + x] = best >= 0 && bestD <= apronSq;
                    if (best >= 0) box[best] = new int4(math.min(box[best].x, x), math.min(box[best].y, y), math.max(box[best].z, x), math.max(box[best].w, y));
                }
            }
            Lap("label");
            void Take(int x, int y, int to)
            {
                label[y * w + x] = to;
                if (to >= 0) box[to] = new int4(math.min(box[to].x, x), math.min(box[to].y, y), math.max(box[to].z, x), math.max(box[to].w, y));
            }
            // ── no yard narrower than a yard is any use: strips under emergentMinWidth (m) past a lot's own strip go to the
            // garden round them, else to nobody ──
            float minWidth = s.Knob("emergentMinWidth", 0f);
            if (minWidth > 0f)
            {
                var seedIndex = new Dictionary<int, int>();
                for (int k = 0; k < seeds.Count; k++) seedIndex[seeds[k]] = k;
                Narrows(label, own, w, h, (int)math.round(minWidth * 0.5f / cell),
                    (x, y, to) => seedIndex.TryGetValue(to, out int k) && InConvex(seedWide[k], lo + new float2(x + 0.5f, y + 0.5f) * cell), Take, plan);
            }
            Lap("shape");
            int L(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? -2 : label[y * w + x];
            // where three or more owners meet (nobody's land counts as one), or two touch corner to corner: the points every
            // garden's outline keeps, so the edges between them are drawn the same by both sides
            // the lots' corners: kept by every outline that passes them, at the exact corner (a straightened edge cut across a
            // lot's corner, and the lot added back after left a notch of tiny edges on both sides)
            var anchors = new Dictionary<int, float2>();
            foreach (int j in seeds)
                foreach (var c in Lot(houses[j]))
                {
                    var g = (int2)math.round((c - lo) / cell);
                    if (g.x < 0 || g.y < 0 || g.x > w || g.y > h) continue;
                    int key = g.y * (w + 1) + g.x;
                    if (!anchors.ContainsKey(key)) anchors[key] = c;
                }
            bool Node(int gx, int gy)
            {
                if (anchors.ContainsKey(gy * (w + 1) + gx)) return true;
                int a = L(gx - 1, gy - 1), b = L(gx, gy - 1), c = L(gx, gy), d = L(gx - 1, gy);
                int distinct = 1 + (b != a ? 1 : 0) + (c != a && c != b ? 1 : 0) + (d != a && d != b && d != c ? 1 : 0);
                return distinct >= 3 || distinct == 2 && a == c && b == d && a != b;
            }

            // ── 2. straight: each edited garden's outline, its shared edges straightened between the nodes ──
            int made = 0;
            foreach (int i in edit)
            {
                if (box[i].x > box[i].z) continue;
                var ring = Outline(label, w, h, i, houses[i], lo, cell, box[i]);
                if (ring == null) continue;
                Lap("outline");
                var pts = Straighten(ring, Node, tol / cell, math.max(0f, s.Knob("emergentMinEdge", 2f)) / cell);
                Lap("straighten");
                if (pts.Count < 3) continue;
                var world = pts.Select(g => anchors.TryGetValue(g.y * (w + 1) + g.x, out var exact) ? exact : lo + new float2(g.x, g.y) * cell).ToList();
                if (!Poly.IsCcw(world)) world.Reverse();
                // never past its reach (its street front, a ring's middle, a street it hugs: exact lines), and its lot whole
                foreach (var (p, d) in Edges(Ccw((wide[i] ?? reach[i]).ToArray()))) world = ClipHalfPlane(world, p, d, p + new float2(-d.y, d.x));
                if (world.Count < 3) continue;
                var withLot = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(world) }, new[] { (IReadOnlyList<float2>)Ccw(Lot(houses[i])) }, BoolOp.Union, FillRule.NonZero);
                var keep = withLot.OrderByDescending(r => Poly.Contains(r.Outer, houses[i].Position) ? 1 : 0).ThenByDescending(r => r.Area).FirstOrDefault();
                if (keep == null) continue;
                var outer = Poly.RemoveCollinear(keep.Outer, 1e-3f);
                if (outer.Length < 3 || Poly.Area(outer) < 1f) continue;
                plots[i] = outer;
                made++;
                Lap("finish");
            }
            if (times != null) plan.Notes.Add("emergent (ms): " + string.Join(", ", times.GroupBy(x => x.Split(' ')[0]).Select(g => $"{g.Key} {g.Sum(x => double.Parse(x.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)):F1}")));
            plan.Notes.Add($"emergent: {made} gardens shared by nearness on {w} x {h} cells of {cell:0.##} m, {seeds.Count} lots competing");
        }

        /// <summary>
        /// Strips of a garden narrower than 2·r cells past its lot's own strip: what stays when every cell nearer than r to another
        /// owner goes and the rest grows back r (an opening). A strip that goes is given to the garden round it that reaches it
        /// (the one it touches most), else to nobody.
        /// </summary>
        private static void Narrows(int[] label, bool[] own, int w, int h, int r, Func<int, int, int, bool> reaches, Action<int, int, int> take, StitchPlan plan)
        {
            if (r < 1) return;
            int count = w * h;
            const int Far = 1 << 20;
            // distance in cells (8 ways) to the nearest cell of another owner
            var d = new int[count];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, l = label[i];
                    bool edge = l < 0 || x == 0 || y == 0 || x == w - 1 || y == h - 1
                        || label[i - 1] != l || label[i + 1] != l || label[i - w] != l || label[i + w] != l;
                    d[i] = edge ? 0 : Far;
                }
            for (int y = 1; y < h; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    d[i] = math.min(d[i], math.min(math.min(d[i - 1], d[i - w]), math.min(d[i - w - 1], d[i - w + 1])) + 1);
                }
            for (int y = h - 2; y >= 0; y--)
                for (int x = w - 2; x >= 1; x--)
                {
                    int i = y * w + x;
                    d[i] = math.min(d[i], math.min(math.min(d[i + 1], d[i + w]), math.min(d[i + w + 1], d[i + w - 1])) + 1);
                }
            // grown back r cells from the core (r or more from another owner, or a lot's own strip), within the same owner
            var back = new int[count];
            var queue = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                back[i] = Far;
                if (label[i] >= 0 && (d[i] >= r || own[i])) { back[i] = 0; queue.Enqueue(i); }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (back[i] >= r) continue;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (label[j] != label[i] || back[j] <= back[i] + 1) continue;
                        back[j] = back[i] + 1;
                        queue.Enqueue(j);
                    }
            }
            var lost = new List<int>();
            for (int i = 0; i < count; i++) if (label[i] >= 0 && back[i] > r) lost.Add(i);
            if (lost.Count == 0) return;
            const int Open = -4;   // (nobody's while the gardens round it grow in)
            var was = new Dictionary<int, int>(lost.Count);
            foreach (int i in lost) { was[i] = label[i]; label[i] = Open; }
            int given = 0;
            for (int round = 0; round <= r; round++)
            {
                var gets = new List<(int I, int To)>();
                foreach (int i in lost)
                {
                    if (label[i] != Open) continue;
                    int x = i % w, y = i / w;
                    int a = x > 0 ? label[i - 1] : -1, b = x < w - 1 ? label[i + 1] : -1, c = y > 0 ? label[i - w] : -1, e = y < h - 1 ? label[i + w] : -1;
                    int best = -1, bestCount = 0;
                    foreach (int cand in new[] { a, b, c, e })
                    {
                        if (cand < 0 || cand == was[i]) continue;   // (another garden: its own is what it was shaved off)
                        int n = (a == cand ? 1 : 0) + (b == cand ? 1 : 0) + (c == cand ? 1 : 0) + (e == cand ? 1 : 0);
                        if (n > bestCount || n == bestCount && cand < best) { best = cand; bestCount = n; }
                    }
                    if (best >= 0 && reaches(x, y, best)) gets.Add((i, best));
                }
                if (gets.Count == 0) break;
                foreach (var (i, to) in gets) { take(i % w, i / w, to); given++; }
            }
            // (a strip no garden round it reaches stays its own garden's: shaved off, it was a gap between fences)
            int kept = 0;
            foreach (int i in lost) if (label[i] == Open) { label[i] = was[i]; kept++; }
            plan.Notes.Add($"emergent: {lost.Count} cells of strips under {2 * r} cells wide: {given} to the garden round them, {kept} kept");
        }

        /// <summary>
        /// The emergent split's fences: each edited house's own fence and hedge lines along its lot's sides and back (not its
        /// street front, not one already laid on a shared side) onto the edge of its garden they face: the nearest edge
        /// running the same way (within 20 degrees) on the line's outer side, along it for at least 2 m; laid along that whole
        /// edge. One line per edge (the nicest, the longest). A line no edge takes, inside the garden now, goes; the edges no
        /// line takes are closed with pieces (CloseGardens).
        /// </summary>
        private static void SnapLines(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan,
            Dictionary<long, (float2 A, float2 B)> moved, HashSet<long> placed, HashSet<long> stay, StitchSettings s)
        {
            int snapped = 0, gone = 0;
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!touched[i] || !h.Edit || !h.Polished || plots[i] == null || plots[i].Length < 3) continue;
                var ring = Ccw(plots[i]);
                int m = ring.Length;
                var taken = new bool[m];
                var lines = h.Lines
                    .Where(l => !plan.HideLines.Contains(l.Id) && !placed.Contains(l.Id) && !stay.Contains(l.Id))
                    .Where(l => OnLotEdge(h, l.A, s.EdgeTolerance) && OnLotEdge(h, l.B, s.EdgeTolerance))
                    .Where(l => h.ToLocal(l.A).y < h.HalfLot.y - 1.2f || h.ToLocal(l.B).y < h.HalfLot.y - 1.2f)
                    .OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).ToList();
                foreach (var l in lines)
                {
                    var (a, b) = moved.TryGetValue(l.Id, out var mv) ? mv : (l.A, l.B);
                    float len = math.distance(a, b);
                    if (len < 0.5f) continue;
                    var d = (b - a) / len;
                    var mid = (a + b) * 0.5f;
                    // (outward: away from the house)
                    var outN = new float2(-d.y, d.x);
                    if (math.dot(mid - h.Position, outN) < 0f) outN = -outN;
                    int best = -1;
                    float bestOff = float.MaxValue;
                    for (int e = 0; e < m; e++)
                    {
                        if (taken[e]) continue;
                        var ea = ring[e];
                        var eb = ring[(e + 1) % m];
                        float elen = math.distance(ea, eb);
                        if (elen < 2f) continue;
                        var ed = (eb - ea) / elen;
                        if (math.abs(math.dot(ed, d)) < 0.94f) continue;
                        var emid = (ea + eb) * 0.5f;
                        if (h.ToLocal(emid).y >= h.HalfLot.y - 1.2f) continue;   // the street front
                        float off = math.dot(emid - mid, outN);
                        if (off < -0.6f) continue;
                        float u0 = math.dot(ea - mid, d), u1 = math.dot(eb - mid, d);
                        float overlap = math.min(len * 0.5f, math.max(u0, u1)) - math.max(-len * 0.5f, math.min(u0, u1));
                        if (overlap < math.min(2f, 0.3f * len)) continue;
                        if (off < bestOff) { bestOff = off; best = e; }
                    }
                    if (best < 0)
                    {
                        // nowhere to go: inside the garden now (it reaches past it), it goes; on its edge, it stays
                        if (Poly.Contains(ring, mid) && DistanceToRing(ring, mid) > 1f) { plan.HideLines.Add(l.Id); gone++; }
                        continue;
                    }
                    taken[best] = true;
                    var p0 = ring[best];
                    var p1 = ring[(best + 1) % m];
                    // (its own direction kept: its end A at the edge's end nearer A)
                    moved[l.Id] = math.distance(l.A, p0) + math.distance(l.B, p1) <= math.distance(l.A, p1) + math.distance(l.B, p0) ? (p0, p1) : (p1, p0);
                    snapped++;
                }
            }
            if (snapped + gone > 0) plan.Notes.Add($"emergent: {snapped} lines laid on their garden's edges, {gone} inside a garden gone");
        }

        /// <summary>
        /// Loose fence ends joined: an end of this plan's fences (an edited house's line, a closing piece) that meets no other
        /// fence and no road, house or wall, with another fence within <c>joinReach</c> (2.5 m) ahead or beside it: a line is
        /// carried on to it, a piece run gets a piece across the gap. (The emergent split's gardens meet at junctions the old
        /// joining steps never saw: three gardens and a lot another pass planned, a side line short of the street.)
        /// </summary>
        private static void JoinLooseEnds(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            float reach = s.Knob("joinReach", 2.5f);
            if (reach <= 0f) return;
            var segs = new List<(long Line, int Piece, float2 A, float2 B, bool Mine)>();
            var segHouse = new List<long>();   // (whose it is)
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (h.Polished && !h.Edit && h.Standing != null) { foreach (var (a, b) in h.Standing) { segs.Add((-1, -1, a, b, false)); segHouse.Add(h.Id); } continue; }
                bool mine = touched[i] && h.Edit && h.Polished;
                foreach (var l in h.Lines)
                {
                    if (plan.HideLines.Contains(l.Id)) continue;
                    var (a, b) = moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
                    if (math.distance(a, b) < 0.3f) continue;
                    segs.Add((l.Id, -1, a, b, mine));
                    segHouse.Add(h.Id);
                }
            }
            int pieceCount = plan.FencePieces.Count;
            for (int k = 0; k < pieceCount; k++)
            {
                var (_, at, yaw, name) = plan.FencePieces[k];
                var d = new float2(math.sin(yaw), math.cos(yaw)) * (name != null && name.IndexOf("Short", StringComparison.Ordinal) >= 0 ? PieceShort * 0.5f : PieceLong * 0.5f);
                segs.Add((-1, k, at - d, at + d, true));
                segHouse.Add(plan.FencePieces[k].House);
            }
            var pieceOf = new Dictionary<long, string>();
            foreach (var hh in houses) if (hh.Edit && hh.Polished) pieceOf[hh.Id] = PieceFor(hh, (int)K(hh, s, "fenceKind", 0f));
            if (segs.Count == 0) return;
            // filed by 8 m cell
            const float Cell = 8f;
            var cells = new Dictionary<long, List<int>>();
            void File(int idx)
            {
                var (_, _, a, b, _) = segs[idx];
                var lo = math.min(a, b) - reach;
                var hi = math.max(a, b) + reach;
                for (int x = (int)math.floor(lo.x / Cell); x <= (int)math.floor(hi.x / Cell); x++)
                    for (int y = (int)math.floor(lo.y / Cell); y <= (int)math.floor(hi.y / Cell); y++)
                    {
                        long key = ((long)x << 32) ^ (uint)y;
                        if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>();
                        l.Add(idx);
                    }
            }
            for (int k = 0; k < segs.Count; k++) File(k);
            List<int> Near(float2 p) => cells.TryGetValue(((long)(int)math.floor(p.x / Cell) << 32) ^ (uint)(int)math.floor(p.y / Cell), out var l) ? l : null;
            var walls = Obstacles(houses);
            bool Against(float2 p)
            {
                if (OnBarrier(p) || BarrierNear(p, 1.5f, 0.6f)) return true;
                foreach (var w in walls) if (Poly.Contains(w, p) || DistanceToRing(w, p) < 0.6f) return true;
                return false;
            }
            // (a join runs along a garden's edge, never across a garden)
            var rings = new List<(float2[] Ring, float2 Lo, float2 Hi)>();
            for (int i = 0; i < houses.Count; i++)
                if (touched[i] && plots[i] != null && plots[i].Length >= 3) rings.Add((plots[i], MinOf(plots[i]) - 1.5f, MaxOf(plots[i]) + 1.5f));
            bool OnAnEdge(float2 p)
            {
                foreach (var (r, rlo, rhi) in rings)
                    if (p.x >= rlo.x && p.y >= rlo.y && p.x <= rhi.x && p.y <= rhi.y && DistanceToRing(r, p) <= 1f) return true;
                return false;
            }
            float SegDist(float2 a, float2 b, float2 p, out float2 q)
            {
                var ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
                q = a + ab * t;
                return math.distance(p, q);
            }
            int lines = 0, pieces = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                // (this plan's fences, and a fence another pass laid: its loose end is met by a piece of the garden planned now)
                bool standing = !segs[i].Mine && segs[i].Line < 0 && segs[i].Piece < 0;
                if (!segs[i].Mine && !standing) continue;
                for (int end = 0; end < 2; end++)
                {
                    var (line, piece, a, b, _) = segs[i];
                    var p = end == 0 ? a : b;
                    var outDir = math.normalizesafe(end == 0 ? a - b : b - a);
                    var cand = Near(p);
                    if (cand == null) continue;
                    bool touches = false;
                    int best = -1;
                    float bestD = reach;
                    float2 bestQ = default;
                    foreach (int j in cand)
                    {
                        if (j == i) continue;
                        float dj = SegDist(segs[j].A, segs[j].B, p, out var q);
                        if (dj <= 0.6f) { touches = true; break; }
                        // (ahead or beside, never back along itself)
                        if (dj < bestD && math.dot(math.normalizesafe(q - p), outDir) > -0.2f) { bestD = dj; best = j; bestQ = q; }
                    }
                    if (touches || best < 0 || Against(p)) continue;
                    // (across a house, a shed or a road: a way through, left open; across a garden: not a garden's border)
                    if (Against((p + bestQ) * 0.5f) || !OnAnEdge((p + bestQ) * 0.5f)) continue;
                    if (standing)
                    {
                        // (only to this plan's fence, by a piece of its garden)
                        if (!segs[best].Mine || !pieceOf.TryGetValue(segHouse[best], out var kind)) continue;
                        var gap = bestQ - p;
                        plan.FencePieces.Add((segHouse[best], (p + bestQ) * 0.5f, math.atan2(gap.x, gap.y), bestD <= PieceShort + 0.2f ? ShortPiece(kind) : kind));
                        segs.Add((-1, plan.FencePieces.Count - 1, p, bestQ, false));
                        segHouse.Add(segHouse[best]);
                        File(segs.Count - 1);
                        pieces++;
                        continue;
                    }
                    if (line >= 0)
                    {
                        var na = end == 0 ? bestQ : a;
                        var nb = end == 0 ? b : bestQ;
                        moved[line] = (na, nb);
                        segs[i] = (line, piece, na, nb, true);
                        lines++;
                    }
                    else if (piece >= 0)
                    {
                        var (house, _, _, name) = plan.FencePieces[piece];
                        var gap = bestQ - p;
                        string bridge = bestD <= PieceShort + 0.2f ? ShortPiece(name) : name;
                        var mid = (p + bestQ) * 0.5f;
                        plan.FencePieces.Add((house, mid, math.atan2(gap.x, gap.y), bridge));
                        segs.Add((-1, plan.FencePieces.Count - 1, p, bestQ, false));
                        segHouse.Add(house);
                        File(segs.Count - 1);
                        pieces++;
                    }
                }
            }
            if (lines + pieces > 0) plan.Notes.Add($"emergent: {lines} line ends and {pieces} piece runs carried on to the fence beside them");

            // one border drawn twice, crossing at a shallow angle (a garden's edge and a neighbour's fence another pass laid, a
            // little apart): this plan's piece there goes, its line ends where they cross (the longer part kept)
            var dropPieces = new HashSet<int>();
            int cut = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                if (!segs[i].Mine) continue;
                var (line, piece, a, b, _) = segs[i];
                var cand = Near((a + b) * 0.5f);
                if (cand == null) continue;
                var di = math.normalizesafe(b - a);
                foreach (int j in cand)
                {
                    if (j == i) continue;
                    var (oa, ob) = (segs[j].A, segs[j].B);
                    var dj = math.normalizesafe(ob - oa);
                    float along = math.abs(math.dot(di, dj));
                    if (along > 0.9995f) continue;
                    if (!Poly.SegmentIntersection(a, b, oa, ob, out var x, out float ta, out float tb)) continue;
                    if (ta < 0.05f || ta > 0.95f || tb < 0.02f || tb > 0.98f) continue;
                    // (its own piece run, end to end: no crossing)
                    if (piece >= 0 && segs[j].Piece >= 0 && plan.FencePieces[segs[j].Piece].House == plan.FencePieces[piece].House) continue;
                    float len = math.distance(a, b);
                    if (along < math.cos(math.radians(20f)))
                    {
                        // across it: a run\x27s last piece overshooting the fence it meets (a piece is 4 m whatever is left) slides
                        // back along itself to end on it; a line ends on it; crossing it in the middle is left alone
                        float over = math.min(ta, 1f - ta) * len;
                        if (over > 2.2f) continue;
                        // (only an end that runs on loose past it: in a run another fence crosses, the piece stays)
                        {
                            var past = ta >= 0.5f ? b : a;
                            bool continues = false;
                            var pc = Near(past);
                            if (pc != null) foreach (int q2 in pc) if (q2 != i && q2 != j && SegDist(segs[q2].A, segs[q2].B, past, out _) <= 0.6f) { continues = true; break; }
                            if (continues) continue;
                        }
                        var shift = di * (ta >= 0.5f ? -(1f - ta) * len : ta * len);
                        if (piece >= 0)
                        {
                            var (house, at, yaw, name) = plan.FencePieces[piece];
                            plan.FencePieces[piece] = (house, at + shift, yaw, name);
                            segs[i] = (line, piece, a + shift, b + shift, true);
                            (a, b) = (a + shift, b + shift);
                            cut++;
                        }
                        else if (line >= 0)
                        {
                            var (na, nb) = ta >= 0.5f ? (a, x) : (x, b);
                            moved[line] = (na, nb);
                            segs[i] = (line, piece, na, nb, true);
                            (a, b) = (na, nb);
                            cut++;
                        }
                        continue;
                    }
                    if (piece >= 0) { dropPieces.Add(piece); break; }
                    if (line >= 0)
                    {
                        var (na, nb) = ta >= 0.5f ? (a, x) : (x, b);
                        moved[line] = (na, nb);
                        segs[i] = (line, piece, na, nb, true);
                        (a, b) = (na, nb);
                        cut++;
                    }
                }
            }
            if (dropPieces.Count > 0)
            {
                var keep = new List<(long, float2, float, string)>(plan.FencePieces.Count);
                for (int k = 0; k < plan.FencePieces.Count; k++) if (!dropPieces.Contains(k)) keep.Add(plan.FencePieces[k]);
                plan.FencePieces.Clear();
                plan.FencePieces.AddRange(keep);
            }
            // a scrap of a house's own line (under 1.5 m) touching nothing at either end goes
            int scraps = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                var (line, _, a, b, mine) = segs[i];
                if (!mine || line < 0 || math.distance(a, b) >= 1.5f) continue;
                bool Meets(float2 p)
                {
                    var cand = Near(p);
                    if (cand != null) foreach (int j in cand) if (j != i && SegDist(segs[j].A, segs[j].B, p, out _) <= 0.6f) return true;
                    return Against(p);
                }
                if (Meets(a) || Meets(b)) continue;
                plan.HideLines.Add(line);
                scraps++;
            }
            if (dropPieces.Count + cut + scraps > 0) plan.Notes.Add($"emergent: {dropPieces.Count} pieces and {cut} line ends off a fence they crossed along, {scraps} scraps of lines gone");
        }

        /// <summary>The edges of a convex counter-clockwise ring as (start, direction).</summary>
        private static IEnumerable<(float2 P, float2 D)> Edges(float2[] ring)
        {
            for (int k = 0; k < ring.Length; k++)
            {
                var d = ring[(k + 1) % ring.Length] - ring[k];
                if (math.lengthsq(d) > 1e-8f) yield return (ring[k], d);
            }
        }

        /// <summary>Another house's lot lies behind this one's within <paramref name="gap"/> (m): not a side neighbour (a deeper one
        /// beside it reaches past its back), and well within its width.</summary>
        private static bool LotBehind(IReadOnlyList<StitchHouse> houses, int i, float gap, int left, int right)
        {
            var h = houses[i];
            float hx = math.max(0.5f, h.HalfLot.x - 2f), z0 = -h.HalfLot.y - 1f;
            var box = new[] { h.ToWorld(new float2(-hx, -h.HalfLot.y - gap)), h.ToWorld(new float2(hx, -h.HalfLot.y - gap)), h.ToWorld(new float2(hx, z0)), h.ToWorld(new float2(-hx, z0)) };
            float reach = math.length(h.HalfLot) + gap;
            for (int j = 0; j < houses.Count; j++)
            {
                if (j == i || j == left || j == right || math.distance(houses[j].Position, h.Position) > reach + math.length(houses[j].HalfLot)) continue;
                if (Overlaps(box, Lot(houses[j]))) return true;
            }
            return false;
        }

        /// <summary>Squared distance from a point to a house's lot (0 inside).</summary>
        private static float LotDistanceSq(StitchHouse h, float2 p)
        {
            var l = h.ToLocal(p);
            float dx = math.max(0f, math.abs(l.x) - h.HalfLot.x), dz = math.max(0f, math.abs(l.y) - h.HalfLot.y);
            return dx * dx + dz * dz;
        }

        /// <summary>Squared distance from a point to a house's street front (its lot's front edge).</summary>
        private static float FrontDistanceSq(StitchHouse h, float2 p)
        {
            var l = h.ToLocal(p);
            float dx = math.max(0f, math.abs(l.x) - h.HalfLot.x), dz = l.y - h.HalfLot.y;
            return dx * dx + dz * dz;
        }

        /// <summary>Inside a convex counter-clockwise ring (its edges included).</summary>
        private static bool InConvex(float2[] r, float2 p)
        {
            for (int k = 0; k < r.Length; k++)
            {
                var a = r[k];
                var b = r[(k + 1) % r.Length];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < -1e-3f) return false;
            }
            return true;
        }

        /// <summary>Marks the cells whose middle lies inside a ring (even-odd, by rows).</summary>
        private static void Fill(bool[] mask, int w, int h, float2 lo, float cell, IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count < 3) return;
            var rlo = MinOf(ring);
            var rhi = MaxOf(ring);
            int y0 = math.max(0, (int)math.floor((rlo.y - lo.y) / cell - 0.5f)), y1 = math.min(h - 1, (int)math.ceil((rhi.y - lo.y) / cell - 0.5f));
            var xs = new List<float>();
            for (int y = y0; y <= y1; y++)
            {
                float py = lo.y + (y + 0.5f) * cell;
                xs.Clear();
                for (int k = 0; k < ring.Count; k++)
                {
                    var a = ring[k];
                    var b = ring[(k + 1) % ring.Count];
                    if ((a.y > py) == (b.y > py)) continue;
                    xs.Add(a.x + (py - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = math.max(0, (int)math.ceil((xs[k] - lo.x) / cell - 0.5f)), x1 = math.min(w - 1, (int)math.floor((xs[k + 1] - lo.x) / cell - 0.5f));
                    for (int x = x0; x <= x1; x++) mask[y * w + x] = true;
                }
            }
        }

        /// <summary>
        /// The outline of one owner's cells (counter-clockwise, grid corners), the piece holding its house (else its biggest):
        /// cell edges with the owner on the left, followed round, turning left first where two pieces touch corner to corner.
        /// </summary>
        private static List<int2> Outline(int[] label, int w, int h, int owner, StitchHouse house, float2 lo, float cell, int4 box)
        {
            // out of each corner: the edges leaving it (at most two)
            var outs = new Dictionary<int, (int2 A, int2 B)>();
            var second = new Dictionary<int, int2>();
            int Key(int2 g) => g.y * (w + 1) + g.x;
            bool Mine(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && label[y * w + x] == owner;
            void Add(int2 a, int2 b)
            {
                int k = Key(a);
                if (outs.TryGetValue(k, out var was)) second[k] = b; else outs[k] = (b, default);
            }
            for (int y = box.y; y <= box.w; y++)
                for (int x = box.x; x <= box.z; x++)
                {
                    if (label[y * w + x] != owner) continue;
                    if (!Mine(x, y - 1)) Add(new int2(x, y), new int2(x + 1, y));
                    if (!Mine(x + 1, y)) Add(new int2(x + 1, y), new int2(x + 1, y + 1));
                    if (!Mine(x, y + 1)) Add(new int2(x + 1, y + 1), new int2(x, y + 1));
                    if (!Mine(x - 1, y)) Add(new int2(x, y + 1), new int2(x, y));
                }
            if (outs.Count == 0) return null;
            var used = new HashSet<(int, int)>();
            List<int2> best = null;
            double bestScore = double.MinValue;
            var hp = (house.Position - lo) / cell;
            foreach (var start in outs.Keys.ToList())
            {
                foreach (var first in second.TryGetValue(start, out var s2) ? new[] { outs[start].A, s2 } : new[] { outs[start].A })
                {
                    var a0 = new int2(start % (w + 1), start / (w + 1));
                    if (used.Contains((start, Key(first)))) continue;
                    var loop = new List<int2> { a0 };
                    var cur = a0;
                    var next = first;
                    int guard = 0;
                    while (guard++ < 4 * (w + 1) * (h + 1))
                    {
                        used.Add((Key(cur), Key(next)));
                        var dir = next - cur;
                        cur = next;
                        if (cur.Equals(a0)) break;
                        loop.Add(cur);
                        int k = Key(cur);
                        if (!outs.TryGetValue(k, out var o)) break;
                        if (second.TryGetValue(k, out var alt))
                        {
                            // two ways on: the left turn (dir rotated +90°) keeps to this piece
                            var left = new int2(-dir.y, dir.x);
                            next = (o.A - cur).Equals(left) ? o.A : (alt - cur).Equals(left) ? alt : o.A;
                            if (used.Contains((k, Key(next)))) next = next.Equals(o.A) ? alt : o.A;
                        }
                        else next = o.A;
                    }
                    if (loop.Count < 4) continue;
                    double area = 0;
                    for (int q = 0; q < loop.Count; q++) { var p = loop[q]; var r = loop[(q + 1) % loop.Count]; area += (double)p.x * r.y - (double)r.x * p.y; }
                    if (area <= 0) continue;   // a hole
                    bool holds = Poly.Contains(loop.Select(g => new float2(g.x, g.y)).ToArray(), hp);
                    double score = (holds ? 1e12 : 0) + area;
                    if (score > bestScore) { bestScore = score; best = loop; }
                }
            }
            return best;
        }

        /// <summary>
        /// A grid outline with each run between two nodes straightened (Douglas–Peucker, ties broken by position, so the
        /// garden on the other side straightens the same run to the same points); straight runs of cells are corners only.
        /// </summary>
        private static List<int2> Straighten(List<int2> ring, Func<int, int, bool> node, float tol, float minSeg = 0f)
        {
            int m = ring.Count;
            var keep = new bool[m];
            int nodes = 0;
            for (int k = 0; k < m; k++) if (node(ring[k].x, ring[k].y)) { keep[k] = true; nodes++; }
            if (nodes == 0)
            {
                // nobody shares any of it: from its lowest corner and the corner farthest from it
                int a = 0;
                for (int k = 1; k < m; k++) if (ring[k].x < ring[a].x || ring[k].x == ring[a].x && ring[k].y < ring[a].y) a = k;
                int b = a;
                long far = -1;
                for (int k = 0; k < m; k++) { var d = ring[k] - ring[a]; long dd = (long)d.x * d.x + (long)d.y * d.y; if (dd > far) { far = dd; b = k; } }
                keep[a] = keep[b] = true;
            }
            // each run from one kept corner to the next
            var starts = Enumerable.Range(0, m).Where(k => keep[k]).ToList();
            var outKeep = (bool[])keep.Clone();
            for (int q = 0; q < starts.Count; q++)
            {
                int a = starts[q], b = starts[(q + 1) % starts.Count];
                int len = (b - a + m) % m;
                if (len == 0) len = m;
                Dp(ring, a, len, tol, outKeep);
                if (minSeg > 0f) MinSegments(ring, a, len, minSeg, outKeep);
            }
            var result = new List<int2>();
            for (int k = 0; k < m; k++) if (outKeep[k]) result.Add(ring[k]);
            return result;
        }

        /// <summary>
        /// No piece of a run (between two fixed corners) shorter than <paramref name="minSeg"/> (a short fence piece: one laid
        /// there sticks out past the bend): the shortest such piece goes first, its free end let go (of two free ends, the one
        /// that bends the line least; ties by position, so the garden on the other side lets go the same corners).
        /// </summary>
        private static void MinSegments(List<int2> ring, int a, int len, float minSeg, bool[] keep)
        {
            int m = ring.Count;
            // the run's corners in order, which of them are fixed (its two ends)
            var idx = new List<int>();
            for (int t = 0; t <= len; t++) { int k = (a + t) % m; if (keep[k]) idx.Add(k); }
            if (idx.Count < 3) return;
            float2 P(int k) => new float2(ring[k].x, ring[k].y);
            bool Before(int2 p, int2 q) => p.x < q.x || p.x == q.x && p.y < q.y;
            while (idx.Count >= 3)
            {
                int best = -1;
                float bestLen = minSeg;
                for (int s = 0; s + 1 < idx.Count; s++)
                {
                    float l = math.distance(P(idx[s]), P(idx[s + 1]));
                    if (l >= bestLen + 1e-4f) continue;
                    // (a piece between the two fixed ends cannot go)
                    if (s == 0 && s + 1 == idx.Count - 1) continue;
                    if (best >= 0 && math.abs(l - bestLen) <= 1e-4f)
                    {
                        var lo1 = Before(ring[idx[s]], ring[idx[s + 1]]) ? ring[idx[s]] : ring[idx[s + 1]];
                        var lo0 = Before(ring[idx[best]], ring[idx[best + 1]]) ? ring[idx[best]] : ring[idx[best + 1]];
                        if (!Before(lo1, lo0)) continue;
                    }
                    best = s;
                    bestLen = l;
                }
                if (best < 0) return;
                // which end goes: a fixed end never; of two free ones, the one whose going moves the line least
                bool firstFree = best > 0, secondFree = best + 1 < idx.Count - 1;
                int drop;
                if (firstFree && secondFree)
                {
                    float Dev(int s) { var p = P(idx[s]); var u = P(idx[s - 1]); var v = P(idx[s + 1]); var d = v - u; float dl = math.length(d); return dl > 1e-6f ? math.abs(d.x * (p.y - u.y) - d.y * (p.x - u.x)) / dl : math.distance(p, u); }
                    float d0 = Dev(best), d1 = Dev(best + 1);
                    drop = math.abs(d0 - d1) > 1e-4f ? (d0 < d1 ? best : best + 1) : (Before(ring[idx[best]], ring[idx[best + 1]]) ? best : best + 1);
                }
                else drop = firstFree ? best : best + 1;
                keep[idx[drop]] = false;
                idx.RemoveAt(drop);
            }
        }

        private static void Dp(List<int2> ring, int a, int len, float tol, bool[] keep)
        {
            if (len < 2) return;
            int m = ring.Count;
            var pa = new float2(ring[a].x, ring[a].y);
            var pb = new float2(ring[(a + len) % m].x, ring[(a + len) % m].y);
            var d = pb - pa;
            float dl = math.length(d);
            int best = -1;
            float bestD = tol;
            int2 bestP = default;
            for (int t = 1; t < len; t++)
            {
                var g = ring[(a + t) % m];
                var p = new float2(g.x, g.y);
                float dist = dl > 1e-6f ? math.abs(d.x * (p.y - pa.y) - d.y * (p.x - pa.x)) / dl : math.distance(p, pa);
                // (ties by position: the same corner from either side)
                if (dist > bestD + 1e-4f || best >= 0 && math.abs(dist - bestD) <= 1e-4f && (g.x < bestP.x || g.x == bestP.x && g.y < bestP.y))
                {
                    best = t;
                    bestD = dist;
                    bestP = g;
                }
            }
            if (best < 0) return;
            keep[(a + best) % m] = true;
            Dp(ring, a, best, tol, keep);
            Dp(ring, (a + best) % m, len - best, tol, keep);
        }
    }
}
