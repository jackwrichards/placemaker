// shops4 — the strip mall, started over: a PARKING LOT with a row of shops behind it. The user (2026-10-17): "a strip mall should
// be a parking lot with shop buildings in and around it"; of the older layout (a ring road round the site, a planted
// frontage, a walkway spine, a plaza, islands and pads, the lot planned last on what was left): "why is it so much of a loop,
// how do the cars even get in there, what are those green rectangles, why is there a path going nowhere". So:
//   • the lot is planned first and is one piece: from the street the player's entrances are on, straight back to the shops;
//     cars come in through the player's entrances only (the parking planner's own connectors, as the parking type);
//   • one shop lane runs along the back of the site (the side away from the entrances), the shops behind it facing the lot;
//     the lot's aisles join it at both ends and between (no dead end, no loop);
//   • shops and parking about even (the user, 2026-10-18: "90 % parking lot, 10 % buildings ... shouldn't it be more of a
//     split?"): the lane turns down the sides (the U) and double rows of shops stand in the lot where that evens them out;
//   • when it does not fit, nothing is built and the panel says why (an error: Build waits).
// The frame (which side is the front) is the side most car entrances are on; when that does not fit, each car entrance's own
// side is tried, and the layout with the most shops and stalls is kept.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public abstract partial class CommercialLayer
    {
        /// <summary>How a type lays out its lot and shops (<see cref="LotCentre"/>).</summary>
        protected sealed class LotLayout
        {
            /// <summary>Widest plot in cells (big boxes 6) and the anchors' extra width at the row ends.</summary>
            public int MaxPiece = 3;
            public float AnchorWeight = 1.8f;
            /// <summary>A square in the middle of the back row, shops either side of it facing the same lane: "plaza", "park", "garden", or null.</summary>
            public string Square;
            /// <summary>"L": only layouts with one side row (the corner strip mall: the shops along the back and down one side, the lot open
            /// to the other street); null: any.</summary>
            public string Shape;
        }

        public const string LotFact = "lotCentre";
        /// <summary>(shops4) No layout fits: the area is too shallow behind its entrances, or too narrow (an error: Build waits).</summary>
        public const string NoRoomKey = K + "Issue.NoRoom";
        /// <summary>(shops4) The site is too steep for the shops' lanes on the ground as it is (an error: Build waits).</summary>
        public const string TooSteepKey = K + "Issue.TooSteep";
        /// <summary>(shops4) A footpath entrance behind the row of shops: no walk reaches the parking from there.</summary>
        public const string FootpathBehindKey = K + "Issue.FootpathBehind";

        /// <summary>The layout, else an error saying what the site lacks. True when laid out.</summary>
        [ThreadStatic] private static Dictionary<string, List<(float score, float tl, float a, float b, float2 gate)>> _scans;
        protected static bool LotCentre(LayerContext ctx, Mall mall, List<StreetShops.Run> runs, LotLayout o)
        {
            _scans = new Dictionary<string, List<(float score, float tl, float a, float b, float2 gate)>>();
            return LotCentreIn(ctx, mall, runs, o);
        }

        private static bool LotCentreIn(LayerContext ctx, Mall mall, List<StreetShops.Run> runs, LotLayout o)
        {
            mall.LotFirstMode = true;
            if (CompactLot(ctx, mall, runs, o)) { ctx.Plan.Facts[LotFact] = 1; return true; }
            // What the best frame lacked: depth behind the entrances (m), the least needed.
            var (asset, h) = Mall.ShopLane(ctx);
            float need = CompactDepth(h, 2);
            float have = 0f;
            foreach (var r in Frames(ctx, mall, runs)) { Extent(mall, r, out _, out _, out float t0, out float t1); have = math.max(have, t1 - math.max(t0, -2f)); }
            // On a steep site the lanes found no line across the slope gentle enough (the ground is not reshaped): say that instead.
            mall.Region.PrincipalAxes(out _, out _, out _, out float minor);
            float relief = Relief(ctx, mall.Region.Ring);
            if (relief / math.max(20f, minor) > 0.08f) ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = TooSteepKey, Args = new object[] { (int)math.round(100f * relief / math.max(20f, minor)) } });
            else ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = NoRoomKey, Args = new object[] { (int)math.ceil(need), (int)math.floor(have) } });
            return false;
        }

        /// <summary>Depth (m) the compact layout needs from the entrance edge to the back: one aisle of lot, the lane, the plots.</summary>
        private static float CompactDepth(float h, int cells) => 1f + Mall.LotDepth(1) + 2f * h + Mall.ZoneOffset(h) - h + cells * Mall.Cell + 1.5f;

        private static void Extent(Mall mall, StreetShops.Run r, out float s0, out float s1, out float t0, out float t1)
        {
            s0 = float.MaxValue; s1 = float.MinValue; t0 = float.MaxValue; t1 = float.MinValue;
            foreach (var p in mall.Region.Ring)
            {
                float s = math.dot(p - r.A, r.Dir), t = math.dot(p - r.A, r.In);
                s0 = math.min(s0, s); s1 = math.max(s1, s); t0 = math.min(t0, t); t1 = math.max(t1, t);
            }
        }

        // ───────────── compact ─────────────

        /// <summary>The frames a compact layout is tried in: the main frame, then each car entrance's own (its edge as the front).</summary>
        private static List<StreetShops.Run> Frames(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            // Square to the outline, never to a street run's own slightly skewed line (a lot askew to its own site): each car entrance's
            // edge first, then the longest edges of the outline (the preview before any entrance, and the other ways round).
            var list = new List<StreetShops.Run>();
            void Add(float2 at, float2 inward)
            {
                var f = new StreetShops.Run { A = at, In = inward, Dir = new float2(inward.y, -inward.x), From = -50f, To = 50f };
                foreach (var e in list) if (math.dot(e.In, f.In) > 0.999f) return;
                list.Add(f);
            }
            if (mall.Gates != null) foreach (var g in mall.Gates) if (g.Cars) Add(g.At, g.In);
            var ring = mall.Region.Ring;
            var edges = new List<int>();
            for (int i = 0; i < ring.Length; i++) edges.Add(i);
            edges.Sort((x, y) => math.distance(ring[(y + 1) % ring.Length], ring[y]).CompareTo(math.distance(ring[(x + 1) % ring.Length], ring[x])));
            float longest = edges.Count > 0 ? math.distance(ring[edges[0]], ring[(edges[0] + 1) % ring.Length]) : 0f;
            for (int k = 0; k < edges.Count && list.Count < 6; k++)
            {
                int i = edges[k];
                var p = ring[i]; var q = ring[(i + 1) % ring.Length];
                if (math.distance(p, q) < 20f) break;
                // (2026-10-28) A strip mall fronts its long street: a long thin site's short end as the front (no car entrance on it) stood
                // the rows across the site and put the back row's end plot in a city street's zone block (600 x 90 m: "CutoutNotZonable")
                if (math.distance(p, q) < 0.5f * longest)
                {
                    var n = mall.Region.InwardNormal(i);
                    var f = new StreetShops.Run { A = math.lerp(p, q, 0.5f), In = n, Dir = new float2(n.y, -n.x), From = -50f, To = 50f };
                    Extent(mall, f, out float e0, out float e1, out float d0, out float d1);
                    if (d1 - d0 > 2f * (e1 - e0)) continue;
                }
                Add(math.lerp(p, q, 0.5f), mall.Region.InwardNormal(i));
            }
            return list;
        }

        private static bool CompactLot(LayerContext ctx, Mall mall, List<StreetShops.Run> runs, LotLayout o)
        {
            var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
            var gates = mall.Gates;
            float bestScore = 0f; int bestK = -1;
            var frames = Frames(ctx, mall, runs);
            var tries = new List<float>();
            // The frames ranked by a cheap estimate (no lot planned); the best two are planned and scored (shops and parking in
            // balance, every entrance connected), then the winner planned again for real. (Planning every frame fully ran past the
            // game's 12 s planning limit on a 300 x 280 m site.)
            var order = new List<(float est, int k)>();
            for (int k = 0; k < frames.Count; k++) { ctx.Check(); if (CompactIn(ctx, mall, frames[k], o, true, out float e)) order.Add((e, k)); }
            order.Sort((x, y) => y.est.CompareTo(x.est));
            var chosen = new HashSet<int>();
            for (int i = 0; i < order.Count && i < 2; i++) chosen.Add(order[i].k);
            for (int k = 0; k < frames.Count; k++)
            {
                ctx.Check();
                if (!chosen.Contains(k)) { tries.Add(-1f); continue; }
                float score = 0f; int lostMask = 0;
                if (CompactIn(ctx, mall, frames[k], o))
                {
                    // Every entrance the player placed must lead somewhere: a layout that leaves one unconnected loses to one that does not.
                    int lost = 0; foreach (var g in gates) if ((g.Cars || g.Kind == ShopEntrances.Footpath) && g.Landed == "none") { lost++; lostMask += 1 << g.Index; }
                    // Shops and parking in balance (the user: "maybe 50 % shops, 50 % parking lot"; a stall with its share of aisle is
                    // about 26 m²).
                    float shops = mall.ShopArea(), lot = 26f * mall.Stalls;
                    score = math.max(1f, Even(shops, lot) - 100000f * lost);
                    // The best-ranked frame that connects every entrance is kept as it is (no second planning).
                    if (lost == 0 && k == order[0].k)
                    {
                        ctx.Plan.Facts["compactFrames"] = frames.Count; ctx.Plan.Facts["compactFrame"] = k;
                        return true;
                    }
                }
                tries.Add(math.round(score) + lostMask * 0.01f);
                planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.ResetDesign(); mall.RowHolds.Clear(); mall.Gates = gates;
                foreach (var g in gates) g.Landed = "none";
                if (score > bestScore) { bestScore = score; bestK = k; }
            }
            if (bestK < 0) return false;
            bool ok = CompactIn(ctx, mall, frames[bestK], o);
            ctx.Plan.Facts["compactFrames"] = frames.Count; ctx.Plan.Facts["compactFrame"] = bestK;
            for (int k = 0; k < tries.Count; k++) ctx.Plan.Facts["compactTry" + k] = tries[k];
            return ok;
        }

        /// <summary>
        /// The compact layout in frame <paramref name="r"/> (s along the front, t inward): the deepest lane line whose plots behind it
        /// stay inside the site, the lot in front of it entered from the player's entrances on its edge, the rest routed to the lane.
        /// </summary>
        private static bool CompactIn(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o) => CompactIn(ctx, mall, r, o, false, out _);

        /// <summary><paramref name="estimate"/>: only the best line's score (cheap: no lot planned), to rank the frames.</summary>
        private static bool CompactIn(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, bool estimate, out float est)
        {
            est = 0f;
            var (asset, h) = Mall.ShopLane(ctx);
            mall.MainDir = r.Dir;
            mall.AnchorWeight = o.AnchorWeight;
            Extent(mall, r, out float sMin, out float sMax, out float tMin, out float tMax);
            float zo = Mall.ZoneOffset(h);
            for (int cells = Cells(ctx); cells >= 2; cells--)
            {
                float d = cells * Mall.Cell;
                float back = zo + d + 1f;     // from the lane's centre line to the back of the plots
                float front = Mall.LotDepth(1) + h + 1f;
                // Lines to try, deepest first; before them the lines of car entrances on a side (the lane runs straight into them).
                var lines = new List<(float tl, float2 gate)>();
                foreach (var g in mall.Gates)
                {
                    if (!g.Cars || math.abs(math.dot(g.In, r.Dir)) < 0.7f) continue;
                    float tg = math.dot(g.At - r.A, r.In);
                    if (tg <= tMax - back && tg >= tMin + front) lines.Add((tg, g.At));
                }
                for (float tl = tMax - back; tl >= tMin + front; tl -= 3f) lines.Add((tl, new float2(float.NaN)));
                // Every line that holds a row, scored: shop ground and lot in balance, less the land left over behind the row (a
                // round site's rim holds a short row; deeper in, its chord is long); the best few are composed.
                // (scanned once per frame and plot depth: the frames' ranking and the real pass share it)
                string key = r.A.x.ToString("R") + "," + r.A.y.ToString("R") + "," + r.In.x.ToString("R") + "," + r.In.y.ToString("R") + "," + cells;
                if (!_scans.TryGetValue(key, out var cand))
                {
                cand = new List<(float score, float tl, float a, float b, float2 gate)>();
                _scans[key] = cand;
                foreach (var (tl, gate) in lines)
                {
                    // The longest stretch along this line where the lane, its plots and a strip of lot in front are all inside.
                    float a = float.NaN, b = float.NaN, ca = float.NaN;
                    for (float s = sMin; s <= sMax + 2f; s += 2f)
                    {
                        bool ok = s <= sMax && mall.Region.Contains(r.At(s, tl - h - 6f)) && mall.Region.Contains(r.At(s, tl)) && mall.Region.Contains(r.At(s, tl + back))
                            // and clear of the first zone rows of a city street behind or beside the plots (the street's own zone block
                            // takes those cells: a plot reaching into them is zoned short)
                            && mall.KerbMargin(r.At(s, tl + back), r.Dir) <= 0f && mall.KerbMargin(r.At(s, tl + back), r.In) <= 0f && mall.KerbMargin(r.At(s, tl + h), r.In) <= 0f;
                        if (ok) { if (float.IsNaN(ca)) ca = s; }
                        else if (!float.IsNaN(ca))
                        {
                            if (float.IsNaN(a) || s - ca > b - a) { a = ca; b = s - 2f; }
                            ca = float.NaN;
                        }
                    }
                    if (float.IsNaN(a) || b - a < 3f * Mall.Cell + 2f * h + 2f) continue;
                    a += 1f; b -= 1f;
                    if (!float.IsNaN(gate.x))
                    {
                        // the lane stops short of the side entrance by a drive's length (the drive joins its end)
                        float sg = math.dot(gate - r.A, r.Dir);
                        if (sg > 0.5f * (a + b)) b = math.min(b, sg - h - 6f); else a = math.max(a, sg + h + 6f);
                        if (b - a < 3f * Mall.Cell + 2f * h + 2f) continue;
                    }
                    float shops = (b - a) * d;
                    float lot = SliceArea(mall, r, sMin, sMax, tMin - 5f, tl - h);
                    float waste = SliceArea(mall, r, sMin, sMax, tl + back + 2f, tMax + 5f);
                    cand.Add((shops + lot - 0.5f * math.abs(shops - lot) - waste, tl, a, b, gate));
                }
                }
                cand = new List<(float score, float tl, float a, float b, float2 gate)>(cand);
                if (cand.Count == 0) continue;
                // The lines composed: the deepest that holds a row (the U leaves no land behind it), the few just short of it (a lot
                // of whole aisle bays in front of the row, the rest of the depth behind it), and the best few by score.
                var deep = cand[0];
                foreach (var c in cand) if (c.tl > deep.tl && float.IsNaN(c.gate.x)) deep = c;
                cand.Sort((x, y) => y.score.CompareTo(x.score));
                var tried = new List<(float score, float tl, float a, float b, float2 gate)> { deep };
                foreach (var c in cand) if (float.IsNaN(c.gate.x) && c.tl < deep.tl - 0.5f && c.tl >= deep.tl - Mall.LotDepth(1)) tried.Add(c);
                for (int c = 0, top = 0; c < cand.Count && top < 3; c++)
                {
                    bool known = false;
                    foreach (var x in tried) if (math.abs(x.tl - cand[c].tl) < 0.5f) known = true;
                    if (!known) { tried.Add(cand[c]); top++; }
                }
                // Every layout behind those lines (double rows, the U, their depths) estimated from the ground it gives the shops and
                // the lot; the most even split first (the user: "90 % parking lot, 10 % buildings ... shouldn't it be more of a split?").
                var opts = new List<(float score, int line, Variant v)>();
                int plain = 0;
                foreach (var g in mall.Gates) if (g.Cars && !ShopEntrances.IsAlley(g.Kind)) plain++;
                for (int li = 0; li < tried.Count; li++)
                    foreach (var v in Variants(ctx, mall, r, o, tried[li].tl, tried[li].a, tried[li].b, cells, h, sMin, sMax, tMin))
                        opts.Add((Even(v.Shops, v.Lot) - v.Waste - BehindWeight * v.Behind - OffLot * (plain - v.Landed) + (v.L && OpenSideOnStreet(mall, r, v, tried[li].tl) ? 0.08f * Even(v.Shops, v.Lot) : 0f), li, v));
                if (opts.Count == 0) continue;
                // (the corner strip mall) the L where one fits
                if (o.Shape == "L" && opts.Exists(op => op.v.L)) opts.RemoveAll(op => !op.v.L);
                // A double row out in the lot loses a quarter to a half of its plots in the game (bare patches between the shops);
                // inside the U it zones every one: where a U fits, no double row outside one (game run send-split-1018-114959).
                if (opts.Exists(op => op.v.SideCells > 0)) opts.RemoveAll(op => op.v.SideCells == 0 && op.v.K > 0);
                opts.Sort((x, y) => y.score.CompareTo(x.score));
                if (estimate) { est = opts[0].score; return true; }
                // Planned in that order. One that comes out well short of its estimate (an entrance behind the row took its shops,
                // a lot held fewer stalls) is taken back while the next is tried; the best of those is kept.
                var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
                // (the best six, then the plain row behind the best lines: the layout every site that holds a row can take)
                var order = new List<int>();
                for (int i = 0; i < opts.Count && order.Count < 6; i++) order.Add(i);
                for (int i = 6, plainRows = 0; i < opts.Count && plainRows < 3; i++)
                    if (opts[i].v.K == 0 && opts[i].v.SideCells == 0) { order.Add(i); plainRows++; }
                float bestReal = 0f; int bestI = -1;
                foreach (int i in order)
                {
                    ctx.Check();
                    var l = tried[opts[i].line];
                    if (!ComposeRows(ctx, mall, r, o, l.tl, l.a, l.b, cells, h, asset, sMin, sMax, tMin, opts[i].v)) continue;
                    float real = Even(mall.ShopArea(), 26f * mall.Stalls);
                    if (real >= 0.8f * opts[i].score && real >= bestReal) { Estimated(ctx, opts[i].v, real); return true; }
                    if (real > bestReal) { bestReal = real; bestI = i; }
                    planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.RowHolds.Clear();
                    foreach (var g in mall.Gates) g.Landed = "none";
                }
                if (bestI >= 0)
                {
                    var l = tried[opts[bestI].line];
                    if (ComposeRows(ctx, mall, r, o, l.tl, l.a, l.b, cells, h, asset, sMin, sMax, tMin, opts[bestI].v)) { Estimated(ctx, opts[bestI].v, bestReal); return true; }
                }
            }
            return false;
        }

        /// <summary>
        /// What a car entrance that does not come in through the lot costs a layout (m² of its balance): its drive runs to a lane
        /// instead, through a row of shops when the lane is behind one, and with no ramp from a street above or below the ground
        /// (the ground is not reshaped).
        /// </summary>
        private const float OffLot = 4000f;

        /// <summary>
        /// What a square metre left behind the back row (or beside a leg) costs a layout: that ground is planted, never shops or parking.
        /// Without it the row stood in the middle of a city block with a band of trees behind it (game run show-8-builds-1028-205715).
        /// </summary>
        private const float BehindWeight = 0.6f;

        /// <summary>The L's open side (no leg) faces a city street: the lot opens to both streets of the corner, the shops are seen from both.</summary>
        private static bool OpenSideOnStreet(Mall mall, StreetShops.Run r, Variant v, float tl)
        {
            Extent(mall, r, out float s0, out float s1, out float t0, out float t1);
            float s = v.LegA ? s1 : s0, t = 0.5f * (t0 + tl);
            var p = r.At(s, t);
            foreach (var road in StreetFrontage.Roads(mall.Ctx.Input))
            {
                if (math.abs(math.dot(math.normalizesafe(road.B - road.A), r.In)) < 0.8f) continue;
                if (math.sqrt(SegmentIndex.DistSq(p, road.A, road.B)) < road.HalfWidth + 12f) return true;
            }
            return false;
        }

        /// <summary>Shops and parking in balance: the ground both take, the larger counted only as far as the smaller (m²).</summary>
        private static float Even(float shops, float lot) => shops + lot - math.abs(shops - lot);

        /// <summary>Plan facts: the layout's estimated shop and lot ground, and its balance as planned (m²).</summary>
        private static void Estimated(LayerContext ctx, Variant v, float real)
        {
            ctx.Plan.Facts["estShops"] = math.round(v.Shops);
            ctx.Plan.Facts["estLot"] = math.round(v.Lot);
            ctx.Plan.Facts["evenPlanned"] = math.round(real);
        }

        /// <summary>
        /// One layout behind a lane line: <see cref="K"/> double rows between the front lot and the back row, their shops
        /// <see cref="MidCells"/> deep; with <see cref="SideCells"/> &gt; 0 the U, the back lane turning down both sides to a band of
        /// lot across the front (legs at <see cref="A2"/>, <see cref="B2"/>, their ends at <see cref="Ty"/>) with rows outside them.
        /// </summary>
        private struct Variant
        {
            public int K, MidCells, SideCells;
            /// <summary>(with <see cref="SideCells"/> &gt; 0) which legs the lane turns down: both (the U) or one (the L, its other end
            /// stopping at the back row's end, the lot running out to the open side).</summary>
            public bool LegA, LegB;
            public float A2, B2, Ty;
            public float Shops, Lot, Waste;
            /// <summary>Ground left behind the back row and beside the legs (m²): planted leftovers, never shops or parking.</summary>
            public float Behind;
            public bool L => SideCells > 0 && LegA != LegB;
            public int Landed;
        }

        /// <summary>The share of a double row's planned cells the game zones when the row stands out in the lot (not inside the U).</summary>
        private const float DoubleRowZoned = 0.75f;

        /// <summary>Below this width (m) between the U's legs the court holds no proper lot.</summary>
        private const float CourtMin = 40f;

        /// <summary>The layouts that fit behind lane line <paramref name="tl"/> (stretch a..b), each with its estimated shop and lot ground.</summary>
        private static List<Variant> Variants(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, float h,
            float sMin, float sMax, float tMin)
        {
            var list = new List<Variant>();
            float zo = Mall.ZoneOffset(h), lotMin = Mall.LotDepth(1) + 4f;
            float front = (tl - h) - tMin;
            var sides = new List<int> { 0 };
            // (side rows three or four cells deep: two-cell side rows on a city block gave less of both shops and lot than the plain row)
            for (int sc = math.min(cells, 4); sc >= 3; sc--) sides.Add(sc);
            var mids = new List<int>();
            for (int mc = math.min(cells, 3); mc >= 2; mc--) mids.Add(mc);
            foreach (int sc in sides)
            {
              foreach (var (la, lb) in sc > 0 ? new[] { (true, true), (true, false), (false, true) } : new[] { (false, false) })
              {
                if (o.Shape == "L" && sc > 0 && la == lb) continue;
                var v0 = new Variant { SideCells = sc, LegA = la, LegB = lb };
                if (sc > 0)
                {
                    if (!ULegs(mall, r, tl, a, b, sc, h, tMin, la, lb, out v0.A2, out v0.B2, out v0.Ty)) continue;
                }
                foreach (int mc in mids)
                {
                    float island = 2f * (zo + mc * Mall.Cell) + 2f * h;
                    int kMax = math.clamp((int)math.floor((front - lotMin) / (lotMin + island)), 0, 8);
                    if (o.Shape == "L" && mall.Region.Area < 60000f) kMax = 0;   // (a clean L; double rows in its lot only on a big site: 500 x 400 m was 64 % asphalt)
                    for (int k = mc == mids[0] ? 0 : 1; k <= kMax; k++)
                    {
                        var v = v0; v.K = k; v.MidCells = mc;
                        if (Estimate(ctx, mall, r, o, tl, a, b, cells, h, sMin, sMax, tMin, ref v)) list.Add(v);
                    }
                }
              }
            }
            return list;
        }

        /// <summary>
        /// The U's legs for side rows <paramref name="sideCells"/> deep: each as far out as its row stays inside the site and clear of the
        /// side street's first zone rows. False when no court of lot fits between them, or an entrance beside a side row would be cut off.
        /// </summary>
        private static bool ULegs(Mall mall, StreetShops.Run r, float tl, float a, float b, int sideCells, float h, float tMin, bool legA, bool legB, out float a2, out float b2, out float ty)
        {
            float side = Mall.ZoneOffset(h) + sideCells * Mall.Cell + 1f;   // a side row's depth from its lane's centre line to its back
            float ty0 = tMin + Mall.LotDepth(1) + 4f + h;                       // the legs' front ends: one band of lot across the whole front
            ty = ty0; a2 = float.NaN; b2 = float.NaN;
            if (tl - ty0 < 3f * Mall.Cell + 2f * h + 4f) return false;
            // (the L) one leg only where it carries a real row: a stub of a leg by a long lot is one plot the front street's zone block
            // takes (360 x 110 m: "CutoutNotZonable" at the leg's front end)
            if (legA != legB && tl - ty0 < 5f * Mall.Cell + 2f * h) return false;
            bool SideFits(float sLane, float dir)
            {
                for (float tt = ty0; tt <= tl; tt += 2f)
                {
                    var p = r.At(sLane - dir * side, tt);
                    if (!mall.Region.Contains(p) || mall.KerbMargin(p, r.In) > 0f || mall.KerbMargin(p, r.Dir) > 0f) return false;
                }
                return true;
            }
            // (a side without a leg: the back row's lane just ends there, the lot runs on to that side of the site)
            if (!legA) a2 = a; else for (float shift = 0f; shift <= 30f; shift += 2f) if (SideFits(a + side + shift, +1f)) { a2 = a + side + shift; break; }
            if (!legB) b2 = b; else for (float shift = 0f; shift <= 30f; shift += 2f) if (SideFits(b - side - shift, -1f)) { b2 = b - side - shift; break; }
            if (float.IsNaN(a2) || float.IsNaN(b2) || b2 - a2 - 2f * h < CourtMin) return false;
            // (the L) the back row is the main row: a leg longer than it means the frame stands the wrong way round (on 360 x 110 m a
            // 285 m leg off a short back row, whose last plot by the main street never zoned)
            if (legA != legB && b2 - a2 < 0.8f * (tl - ty0)) return false;
            foreach (var g in mall.Gates)
            {
                float gs = math.dot(g.At - r.A, r.Dir), gt = math.dot(g.At - r.A, r.In);
                if (gt > ty0 - h - 2f && ((legA && gs < a2 - h) || (legB && gs > b2 + h))) return false;
            }
            return true;
        }

        /// <summary>The stretch (s) where a double row's ring at y1..y2 and the lots either side stay inside the site, less a planted end each side.</summary>
        private static bool LoopSpan(Mall mall, StreetShops.Run r, float y1, float y2, float h, float zo, float di, float sMin, float sMax, float step, out float ia, out float ib)
        {
            ia = float.NaN; ib = float.NaN;
            float ca = float.NaN;
            for (float s = sMin; s <= sMax + step; s += step)
            {
                bool ok = s <= sMax;
                if (ok) foreach (float t in new[] { y1 - h - 6f, y1, y1 + zo + di, y2, y2 + h + 6f }) if (!mall.Region.Contains(r.At(s, t)) || mall.KerbMargin(r.At(s, t), r.In) > 0f) { ok = false; break; }
                if (ok) { if (float.IsNaN(ca)) ca = s; }
                else if (!float.IsNaN(ca)) { if (float.IsNaN(ia) || s - ca > ib - ia) { ia = ca; ib = s - step; } ca = float.NaN; }
            }
            if (float.IsNaN(ia)) return false;
            // a planted end and room to walk round it, not a lane along the street
            ia += 8f; ib -= 8f;
            return true;
        }

        /// <summary>
        /// The ground (m²) a layout gives the shops and the lot, and the lot ground no aisle bay uses (a strip of grass between the
        /// lot and the shops), from its geometry alone (nothing planned). False when it does not fit.
        /// </summary>
        private static bool Estimate(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, float h, float sMin, float sMax, float tMin, ref Variant v)
        {
            float zo = Mall.ZoneOffset(h), d = cells * Mall.Cell, di = v.MidCells * Mall.Cell, ds = v.SideCells * Mall.Cell;
            float span = 2f * (zo + di), island = span + 2f * h;
            var bands = BandDepths((tl - h) - tMin, v.K, island);
            if (bands == null) return false;
            bool u = v.SideCells > 0;
            float s0 = u ? v.A2 : a, s1 = u ? v.B2 : b;
            // (the ground is not reshaped: a lane steeper than a road should be is not laid, ComposeRows)
            if (Steep(ctx, r.At(s0, tl), r.At(s1, tl))) return false;
            if (u && ((v.LegA && Steep(ctx, r.At(v.A2, v.Ty), r.At(v.A2, tl))) || (v.LegB && Steep(ctx, r.At(v.B2, v.Ty), r.At(v.B2, tl))))) return false;
            // the lot's reach between the legs: to a leg's lane, or on to the site's side where there is none
            float ca = u && v.LegA ? v.A2 + h : sMin - 5f, cb = u && v.LegB ? v.B2 - h : sMax + 5f;
            float shops = (s1 - s0 - 2f * h - 1f) * d;
            if (o.Square != null && s1 - s0 >= 6f * Mall.Cell + 40f) shops -= math.clamp(0.3f * (s1 - s0), 32f, 64f) * d;
            if (u) shops += ((v.LegA ? 1f : 0f) + (v.LegB ? 1f : 0f)) * (tl - v.Ty - h - 0.5f) * ds;
            // what is left behind the back row (and beside the legs) is neither shops nor parking
            float behind = Slice(mall, r, sMin - 5f, sMax + 5f, tl + zo + d + 2f, tl + 400f);
            if (u && v.LegA) behind += Slice(mall, r, sMin - 5f, v.A2 - zo - ds - 2f, v.Ty, tl + zo + d + 2f);
            if (u && v.LegB) behind += Slice(mall, r, v.B2 + zo + ds + 2f, sMax + 5f, v.Ty, tl + zo + d + 2f);
            v.Behind = behind;
            float lot = 0f, waste = 0f, t = tMin;
            void Piece(float sa, float sb, float t0, float t1)
            {
                // (the front band reaches past the outline: its ground is what lies inside, its depth from the outline in)
                float area = Slice(mall, r, sa, sb, t0 <= tMin ? tMin - 5f : t0, t1);
                lot += Bays(area, t1 - t0, out float w);
                waste += w;
            }
            for (int j = 0; j <= v.K; j++)
            {
                float hi = t + bands[j];
                if (!u) Piece(sMin - 5f, sMax + 5f, t, hi);
                else if (j > 0) Piece(ca, cb, t, hi);
                else
                {
                    // the first band of the U: one lot, its rows across the court (as deep as the whole band) and the wings beside it
                    Piece(ca, cb, t, hi);
                    if (v.LegA) Piece(sMin - 5f, ca, t, v.Ty - h);
                    if (v.LegB) Piece(cb, sMax + 5f, t, v.Ty - h);
                }
                if (j == v.K) break;
                float y1 = hi + h, y2 = y1 + span;
                if (!LoopSpan(mall, r, y1, y2, h, zo, di, sMin, sMax, 2f, out float ia, out float ib)) return false;
                if (u && v.LegA) ia = math.max(ia, v.A2 + 2f * h + 8f);
                if (u && v.LegB) ib = math.min(ib, v.B2 - 2f * h - 8f);
                if (ib - ia < 6f * Mall.Cell + 2f * h) return false;
                if (Steep(ctx, r.At(ia, y1), r.At(ib, y1)) || Steep(ctx, r.At(ia, y2), r.At(ib, y2)) || Steep(ctx, r.At(ia, y1), r.At(ia, y2))) return false;
                // (a double row out in the lot zones about three quarters of its cells in the game, one inside the U all of them: game
                // runs send-split-1018-103736 and -111039, 21-47 % lost on four sites without the U, none on two with it)
                shops += 2f * (ib - ia - 2f * h - 1f) * di * (u ? 1f : DoubleRowZoned);
                t = hi + island;
            }
            // The car entrances that come in through the front lot (the others drive to a lane: no ramp from a street above or below
            // the ground, the ground is not reshaped).
            float frontHi = tMin + bands[0] - 0.3f;
            float legHi = u ? math.min(frontHi, v.Ty - h - 0.3f) : frontHi;
            int landed = 0;
            foreach (var g in mall.Gates)
                if (g.Cars && !ShopEntrances.IsAlley(g.Kind) && math.dot(g.At - r.A, r.In) < (BesideLeg(v, r, g.At, h) ? legHi : frontHi) - 4f) landed++;
            v.Shops = shops; v.Lot = lot; v.Waste = waste; v.Landed = landed;
            return true;
        }

        /// <summary>
        /// Outline point <paramref name="p"/> lies beside a leg of the U or the L (outside it): the lot there ends at the leg's front end.
        /// On the L's open side the lot runs up to the back row, so an entrance on that side street drives straight into it.
        /// </summary>
        private static bool BesideLeg(Variant v, StreetShops.Run r, float2 p, float h)
        {
            if (v.SideCells <= 0) return false;
            float s = math.dot(p - r.A, r.Dir);
            return (v.LegA && s < v.A2 + h) || (v.LegB && s > v.B2 - h);
        }

        /// <summary>
        /// The depths (m) of the bands of lot between <paramref name="k"/> double rows, front first, in <paramref name="front"/> metres:
        /// the bands between rows whole aisle bays (no strip of grass left in front of the shops), the front band the rest (beside the
        /// street, where the player's entrances come in). Null when they do not fit one bay each.
        /// </summary>
        private static float[] BandDepths(float front, int k, float island)
        {
            float lotMin = Mall.LotDepth(1) + 4f, rest = front - k * island;
            if (rest < (k + 1) * lotMin) return null;
            var bands = new float[k + 1];
            if (k == 0) { bands[0] = rest; return bands; }
            for (int n = math.max(1, (int)math.round((rest / (k + 1) - 4f) / Mall.LotDepth(1))); n >= 1; n--)
            {
                float inner = Mall.LotDepth(n) + 4f, b0 = rest - k * inner;
                if (b0 < lotMin) continue;
                bands[0] = b0;
                for (int j = 1; j <= k; j++) bands[j] = inner;
                return bands;
            }
            return null;
        }

        /// <summary>
        /// The parking in a band of lot <paramref name="depth"/> deep with ground <paramref name="area"/> (m²): only its whole aisle bays
        /// (two rows of stalls and the aisle between, <see cref="Mall.LotDepth"/>), short of the turns at their ends. A band one bay deep
        /// keeps less than three quarters of its ground as parking. <paramref name="waste"/>: the ground beyond its last whole bay.
        /// </summary>
        private static float Bays(float area, float depth, out float waste)
        {
            waste = 0f;
            if (depth < 1f || area <= 0f) return 0f;
            int n = (int)math.floor((depth - 2f) / Mall.LotDepth(1));
            waste = area * math.max(0f, depth - 4f - Mall.LotDepth(n)) / depth;
            float width = area / depth;
            return area * (n * (Mall.LotDepth(1) - 2.5f) / depth) * math.max(0f, 1f - 12f / width);
        }

        /// <summary>Area (m²) of the site inside s0..s1, t0..t1 of frame <paramref name="r"/>.</summary>
        private static float Slice(Mall mall, StreetShops.Run r, float s0, float s1, float t0, float t1)
        {
            if (t1 <= t0 || s1 <= s0) return 0f;
            try { float a = 0f; foreach (var p in Poly.Intersection(mall.Region.Ring, Mall.Rect(r, s0, s1, t0, t1))) if (p?.Outer != null) a += Poly.Area(p.Outer); return a; }
            catch (Exception) { return 0f; }
        }

        /// <summary>
        /// Layout <paramref name="v"/> behind lane line <paramref name="tl"/>: the back row; <see cref="Variant.K"/> double rows (each a
        /// block of shops back to back, ringed by its own lane: the shops face the lots on both sides) set evenly between the front lot
        /// and the back row; with the U, the back lane turns down both sides to a band of lot across the front, smaller shops outside
        /// its legs facing the lot (a real strip mall's U). Every band of lot is one lot entered from the lanes either side (their ends
        /// and corners: no dead end); the front band also from the player's entrances.
        /// </summary>
        private static bool ComposeRows(LayerContext ctx, Mall mall, StreetShops.Run r, LotLayout o, float tl, float a, float b, int cells, float h, AssetRef asset,
            float sMin, float sMax, float tMin, Variant v)
        {
            var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
            bool Fail(int step) { planMark.Restore(ctx.Plan); mall.Rewind(mallMark); mall.RowHolds.Clear(); foreach (var g in mall.Gates) g.Landed = "none"; ctx.Plan.Facts["compactFail"] = step; return false; }
            int k = v.K;
            bool u = v.SideCells > 0;
            float d = cells * Mall.Cell, zo = Mall.ZoneOffset(h), ds = v.SideCells * Mall.Cell;
            int ic = v.MidCells; float di = ic * Mall.Cell;
            float span = 2f * (zo + di), island = span + 2f * h;
            // the back row's ends: the U's legs, else the line's own stretch
            float a2 = u ? v.A2 : a, b2 = u ? v.B2 : b, ty = v.Ty;
            // The ground is not reshaped, so the lanes lie on it as it is: none steeper than a road should be (across the slope instead).
            if (Steep(ctx, r.At(a2, tl), r.At(b2, tl))) return Fail(20);
            if (u && ((v.LegA && Steep(ctx, r.At(a2, ty), r.At(a2, tl))) || (v.LegB && Steep(ctx, r.At(b2, ty), r.At(b2, tl))))) return Fail(22);
            bool legA = u && v.LegA, legB = u && v.LegB;
            // the lot's reach between the legs: to a leg's lane, or on to the site's side where there is none (the L)
            float ca = legA ? a2 + h + 0.3f : sMin - 5f, cb = legB ? b2 - h - 0.3f : sMax + 5f;
            Mall.Lane lane;
            if (u)
            {
                var mids = new List<float2>();
                if (legA) mids.Add(r.At(a2, tl));
                if (legB) mids.Add(r.At(b2, tl));
                lane = mall.Connect(Mall.End.Free(legA ? r.At(a2, ty) : r.At(a2, tl)), mids.ToArray(), Mall.End.Free(legB ? r.At(b2, ty) : r.At(b2, tl)), NetRole.Alley, asset, h);
            }
            else lane = mall.Connect(Mall.End.Free(r.At(a, tl)), null, Mall.End.Free(r.At(b, tl)), NetRole.Alley, asset, h);
            if (lane == null) return Fail(1);
            bool Across(int i) => math.abs(math.dot(math.normalizesafe(lane.Points[i + 1] - lane.Points[i]), r.Dir)) > 0.7f;
            // The U's side rows, outside its legs.
            if (u)
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                {
                    if (Across(i)) continue;
                    var f = Mall.Frame(lane.Points[i], lane.Points[i + 1]);
                    bool held = false;
                    for (float trim = h + 0.5f; trim <= h + 16f && !held; trim += 2f)
                    {
                        if (f.To - 2f * trim < 8f) break;
                        held = mall.HoldRow(Mall.Rect(f, trim, f.To - trim, h, zo + ds + 1f)) != null;
                    }
                    if (!held) return Fail(15);
                }
            // The square (an extra) takes the middle of the back row: its land is kept before the row is held either side of it.
            float2[] square = null;
            float rowT1 = tl + zo + d + 1f;
            if (o.Square != null && b2 - a2 >= 6f * Mall.Cell + 40f)
            {
                float w = math.clamp(0.3f * (b2 - a2), 32f, 64f), mid = 0.5f * (a2 + b2);
                square = mall.Hold(Mall.Rect(r, mid - 0.5f * w, mid + 0.5f * w, tl + h + 0.3f, rowT1));
                if (mall.HoldRow(Mall.Rect(r, a2 + h + 0.5f, mid - 0.5f * w - 0.3f, tl + h, rowT1)) == null) return Fail(2);
                if (mall.HoldRow(Mall.Rect(r, mid + 0.5f * w + 0.3f, b2 - h - 0.5f, tl + h, rowT1)) == null) return Fail(2);
            }
            else if (mall.HoldRow(Mall.Rect(r, a2 + h + 0.5f, b2 - h - 0.5f, tl + h, rowT1)) == null) return Fail(2);

            // ── the double rows ──
            var bandDepths = BandDepths((tl - h) - tMin, k, island);
            if (bandDepths == null) return Fail(16);
            var loops = new List<(Mall.Lane loop, float y1, float y2, float ia, float ib)>();
            float bandTop = tMin;
            for (int j = 0; j < k; j++)
            {
                bandTop += bandDepths[j];
                float y1 = bandTop + h, y2 = y1 + span;
                bandTop += island;
                if (!LoopSpan(mall, r, y1, y2, h, zo, di, sMin, sMax, 1f, out float ia, out float ib)) return Fail(10);
                // inside the U: a planted strip between the ring's ends and the legs
                if (legA) ia = math.max(ia, a2 + 2f * h + 8f);
                if (legB) ib = math.min(ib, b2 - 2f * h - 8f);
                if (ib - ia < 6f * Mall.Cell + 2f * h) return Fail(11);
                if (Steep(ctx, r.At(ia, y1), r.At(ib, y1)) || Steep(ctx, r.At(ia, y2), r.At(ib, y2)) || Steep(ctx, r.At(ia, y1), r.At(ia, y2))) return Fail(21);
                var loop = mall.Loop(r.At(ia, y1), new[] { r.At(ib, y1), r.At(ib, y2), r.At(ia, y2) }, NetRole.Alley, asset, h);
                if (loop == null) return Fail(12);
                if (mall.HoldRow(Mall.Rect(r, ia + h + 0.5f, ib - h - 0.5f, y1 + h, y1 + zo + di)) == null) return Fail(13);
                if (mall.HoldRow(Mall.Rect(r, ia + h + 0.5f, ib - h - 0.5f, y2 - zo - di, y2 - h)) == null) return Fail(14);
                loops.Add((loop, y1, y2, ia, ib));
            }

            // ── the lots: one band in front of each row ──
            var bands = new List<(float2[] ring, List<(float2 at, string kind)> entries)>();
            void Stubs(List<(float2 at, string kind)> e, float s0, float s1, float t)
            {
                // the lane's ends (its end nodes or the ring's corners become corners of the lot's aisles), then one per 80 m on long rows
                e.Add((r.At(s0 + 0.5f, t), "alley"));
                e.Add((r.At(s1 - 0.5f, t), "alley"));
                float len = s1 - s0;
                int n = len > 160f ? (int)math.floor(len / 80f) : 1;
                for (int q = 1; q < n; q++) e.Add((r.At(s0 + len * q / n, t), "alley"));
            }
            for (int j = 0; j <= k; j++)
            {
                float lo = j == 0 ? tMin - 5f : loops[j - 1].y2 + h + 0.3f;
                float hi = j == k ? tl - h - 0.3f : loops[j].y1 - h - 0.3f;
                float2[] lot;
                try
                {
                    float2[] piece = null;
                    if (!u) piece = Mall.Rect(r, sMin - 5f, sMax + 5f, lo, hi);
                    else if (j > 0) piece = Mall.Rect(r, ca, cb, lo, hi);
                    else
                    {
                        // the U's first band: across the whole front, and the court between the legs up to the first row
                        var fr = Mall.Rect(r, sMin - 5f, sMax + 5f, lo, ty - h - 0.3f);
                        if (hi - (ty - h) < 3f) piece = fr;
                        else
                        {
                            var court = Mall.Rect(r, ca, cb, ty - h - 1f, hi);
                            var union = Poly.Boolean(new[] { (IReadOnlyList<float2>)fr }, new[] { (IReadOnlyList<float2>)court }, BoolOp.Union);
                            if (union.Count == 1 && union[0].Holes.Count == 0) piece = Poly.EnsureCcw(union[0].Outer);
                        }
                    }
                    lot = piece == null ? null : Mall.Largest(Poly.Intersection(mall.Region.Ring, piece), 300f);
                }
                catch (Exception) { lot = null; }
                if (lot == null) return Fail(3);
                var e = new List<(float2 at, string kind)>();
                if (j < k) Stubs(e, loops[j].ia, loops[j].ib, hi);
                else if (u) Stubs(e, legA ? a2 + h + 0.3f : a2, legB ? b2 - h - 0.3f : b2, hi);   // the court's back corners and along the back
                else Stubs(e, a, b, hi);
                if (j > 0) Stubs(e, loops[j - 1].ia, loops[j - 1].ib, lo);
                // the U's legs end in the front band (straight down from them into its aisles)
                if (legA && j == 0) e.Add((r.At(a2, ty - h - 0.3f), "alley"));
                if (legB && j == 0) e.Add((r.At(b2, ty - h - 0.3f), "alley"));
                bands.Add((lot, e));
            }

            // Car entrances and footpaths on the front lot's own edge come in through it (the parking planner's connectors, as the
            // parking type); the others are routed to the nearest lane.
            var rest = new List<ShopEntrances.Gate>();
            bool cars = false;
            float frontHi = k == 0 ? tl - h - 0.3f : loops[0].y1 - h - 0.3f;
            float legHi = u ? math.min(frontHi, ty - h - 0.3f) : frontHi;
            foreach (var g in mall.Gates)
            {
                if ((g.Cars || g.Kind == ShopEntrances.Footpath) && Poly.DistanceToBoundary(bands[0].ring, g.At) < 1.5f
                    && math.dot(g.At - r.A, r.In) < (BesideLeg(v, r, g.At, h) ? legHi : frontHi) - 4f)
                { bands[0].entries.Add((g.At, g.Kind)); g.Landed = "lot"; if (g.Cars) cars = true; }
                else rest.Add(g);
            }
            var all = mall.Gates;
            mall.Gates = rest;
            int takenBefore = mall.Taken.Count;
            try { mall.RouteGates(); }
            finally { mall.Gates = all; }

            // The ground is painted, never reshaped (the user: "try to only use painting"); a lot is levelled only on a real slope.
            mall.Grading = false;
            var lots = new List<Mall.LotTry>();
            for (int j = 0; j < bands.Count; j++)
            {
                // The drives and driveways routed above took their corridors: the lot keeps off them (only them: cut by the lanes'
                // own corridors too, a band came out with a slanted sliver the asphalt left out under its stalls, "grassy parking spots").
                var lot = Minus(bands[j].ring, mall.Taken.GetRange(takenBefore, mall.Taken.Count - takenBefore));
                var parking = Mall.Parking(ctx);
                if (Relief(ctx, lot) < FlatRelief && StreetsLevel(ctx, mall)) { parking.Values["grading"] = false; ctx.Plan.Facts["flatGround"] = 1; }
                var t = BestLot(mall, lot, bands[j].entries, parking, Hashing.Mix(ctx.Seed, 0xC0Cu + (uint)j));
                if (t == null) return Fail(4);
                mall.CommitLot(t, true);   // (non-convex lots are kept as convex parts: a T kept whole blocked the side rows)
                lots.Add(t);
            }
            // No car entrance yet (the preview before the player places one: Build waits for it) is fine; placed ones must reach.
            bool anyCars = false; foreach (var g in mall.Gates) if (g.Cars) anyCars = true;
            if (anyCars && !cars && !LaneReached(mall)) return Fail(5);
            WalksToTheLot(ctx, mall, lots[0].Ring);
            ctx.Plan.Facts["gates"] = mall.Gates.Count;

            // ── the shops ──
            mall.ReleaseRows();
            int rows = mall.LaneRows(lane, cells, null, +1, 0f, 0f, o.MaxPiece, 2, i => !u || Across(i)) > 0 ? 1 : 0;
            if (u) rows += mall.LaneRows(lane, v.SideCells, null, +1, 0f, 0f, o.MaxPiece, 2, i => !Across(i)) > 0 ? 2 : 0;
            if (square != null)
            {
                mall.Release(square);
                uint salt = Hashing.Mix(ctx.Seed, 0x5A0u);
                if (o.Square == "pond")
                {
                    // A pond in the row (the user: "a pond could be cool"): water in a planted margin (MallDesign.DressPond, when the
                    // leftovers are dressed). The one piece of dug ground of the strip mall, and only when asked for.
                    var c = Poly.Centroid(square);
                    float rad = 0.5f * math.min(math.distance(square[0], square[1]), math.distance(square[1], square[2])) - 6f;
                    var kept = rad >= 7f ? mall.Keep(square, Mall.KeepKind.Pond, salt) : null;
                    if (kept != null) { kept.Inner = LeisureKit.Blob(c, rad, Hashing.Mix(ctx.Seed, 0xB0Bu), 14, 0.22f); ctx.Plan.Facts["square"] = 1; }
                    else mall.Green(square, salt + 1u);
                }
                else
                {
                    string layer = o.Square == "plaza" ? "plaza" : "park", preset = o.Square == "plaza" ? "cafe" : o.Square == "garden" ? "garden" : "pocket";
                    // two ways in from the lane (a quarter in from each side): its walks run through it, never to a dead end
                    float mid2 = 0.5f * (a2 + b2), sw = 0f;
                    foreach (var p in square) sw = math.max(sw, math.abs(math.dot(p - r.A, r.Dir) - mid2));
                    var doors = new List<(Mall.Lane, float2)> { (lane, r.At(mid2 - 0.5f * sw, tl)), (lane, r.At(mid2 + 0.5f * sw, tl)) };
                    if (mall.FillOpen(square, layer, preset, doors, null, salt)) ctx.Plan.Facts["square"] = 1;
                    else mall.Green(square, salt + 1u);
                }
            }
            foreach (var (loop, _, _, _, _) in loops) rows += mall.LaneRows(loop, ic, null, +1, 0f, 0f, o.MaxPiece, 2, i => i % 2 == 0) > 0 ? 2 : 0;
            if (mall.Shops.Count == 0) return Fail(6);
            ctx.Plan.Facts[StripMallLayer.RowsFact] = rows;
            ctx.Plan.Facts["doubleRows"] = k;
            if (k > 0) ctx.Plan.Facts["midCells"] = ic;
            if (u) { ctx.Plan.Facts[v.L ? "lRow" : "uRow"] = 1; ctx.Plan.Facts["sideCells"] = v.SideCells; ctx.Plan.Facts["legRow"] = math.round(tl - ty); }
            ctx.Plan.Facts["compactDepth"] = math.round(tl - tMin);
            return true;
        }

        /// <summary>The largest simple part of <paramref name="ring"/> outside everything in <paramref name="taken"/> (the ring itself when nothing cuts it).</summary>
        private static float2[] Minus(float2[] ring, List<float2[]> taken)
        {
            if (taken.Count == 0) return ring;
            var clip = new List<IReadOnlyList<float2>>();
            foreach (var tk in taken) clip.Add(Poly.EnsureCcw(tk));
            try
            {
                float2[] cut = null; float ca = 0f;
                foreach (var p in Poly.Boolean(new[] { (IReadOnlyList<float2>)ring }, clip, BoolOp.Difference))
                    if (p?.Outer != null && p.Holes.Count == 0 && Poly.Area(p.Outer) > ca) { ca = Poly.Area(p.Outer); cut = Poly.EnsureCcw(Poly.Clean(p.Outer, 0.05f)); }
                return cut != null && cut.Length >= 3 ? cut : ring;
            }
            catch (Exception) { return ring; }
        }

        /// <summary>A lane on the ground as it is from a to b climbs more than 8 % over any 8 m.</summary>
        private static bool Steep(LayerContext ctx, float2 a, float2 b)
        {
            float len = math.distance(a, b);
            for (float u = 0f; u + 8f <= len; u += 4f)
            {
                var p = math.lerp(a, b, u / len); var q = math.lerp(a, b, (u + 8f) / len);
                if (math.abs(ctx.H(q) - ctx.H(p)) / 8f > 0.08f) return true;
            }
            return false;
        }

        /// <summary>Area (m²) of the site between t0 and t1 in frame <paramref name="r"/>.</summary>
        private static float SliceArea(Mall mall, StreetShops.Run r, float s0, float s1, float t0, float t1)
        {
            if (t1 <= t0) return 0f;
            try { float a = 0f; foreach (var p in Poly.Intersection(mall.Region.Ring, Mall.Rect(r, s0 - 5f, s1 + 5f, t0, t1))) if (p?.Outer != null) a += Poly.Area(p.Outer); return a; }
            catch (Exception) { return 0f; }
        }

        /// <summary>Below this rise and fall (m) under the lot the ground is not reshaped at all.</summary>
        private const float FlatRelief = 1.0f;

        /// <summary>Every street a car entrance joins lies at the ground's level there (a street on a bank or in a cut needs its ramp).</summary>
        private static bool StreetsLevel(LayerContext ctx, Mall mall)
        {
            foreach (var g in mall.Gates)
            {
                if (!g.Cars || !g.HasStreet) continue;
                float hs = MallGrading.StreetHeight(ctx, g.StreetId, g.StreetT, g.StreetPoint);
                if (math.abs(hs - ctx.H(g.At)) > 0.3f) return false;
            }
            return true;
        }

        /// <summary>Highest minus lowest ground (m) over a ring, sampled every 4 m.</summary>
        private static float Relief(LayerContext ctx, float2[] ring)
        {
            float2 mn = float.MaxValue, mx = float.MinValue;
            foreach (var v in ring) { mn = math.min(mn, v); mx = math.max(mx, v); }
            float lo = float.MaxValue, hi = float.MinValue;
            for (float x = mn.x; x <= mx.x; x += 4f)
                for (float y = mn.y; y <= mx.y; y += 4f)
                {
                    var p = new float2(x, y);
                    if (!Poly.Contains(ring, p)) continue;
                    float hgt = ctx.H(p);
                    lo = math.min(lo, hgt); hi = math.max(hi, hgt);
                }
            return hi > lo ? hi - lo : 0f;
        }

        /// <summary>
        /// A footpath entrance that found no way to the shop lane without cutting through the row of shops (beside or behind it)
        /// walks into the parking lot instead: a short path from the sidewalk to the lot's nearest edge, people cross the lot to the
        /// shops as they would in a real one. Its warning goes.
        /// </summary>
        private static void WalksToTheLot(LayerContext ctx, Mall mall, float2[] lot)
        {
            foreach (var g in mall.Gates)
            {
                if (g.Kind != ShopEntrances.Footpath || g.Landed != "none") continue;
                var q = Poly.ClosestPointOnBoundary(lot, g.At, out _, out _);
                float len = math.distance(q, g.At);
                if (len < 2f || len > 30f) continue;
                var dir = (q - g.At) / len; var n = new float2(-dir.y, dir.x);
                var strip = Poly.EnsureCcw(new[] { g.At - n * 1.5f, q - n * 1.5f, q + n * 1.5f, g.At + n * 1.5f });
                bool blocked = false;
                foreach (var shop in mall.Shops) if (StreetShops.ConvexOverlap(shop, strip, 0.05f)) { blocked = true; break; }
                // Clear of every other net (a walk ending on a lot's drive: the game clipped the ground under the two, a 4.6 m pit in the
                // game runner's circle): 4 m from any of them but where it leaves the sidewalk.
                foreach (var net in ctx.Plan.Nets)
                {
                    if (blocked) break;
                    for (float u = 0.25f; u <= 1.001f; u += 0.25f)
                        if (math.sqrt(SegmentIndex.DistSq(math.lerp(g.At, q, u), net.A.xz, net.D.xz)) < 4f) { blocked = true; break; }
                }
                if (blocked || mall.PathChain(new List<float2> { g.At, q }, -g.In, null) == null) continue;
                g.Landed = "walk";
                ctx.Plan.Issues.RemoveAll(i => i.Key == Mall.NoLaneKey && i.Args != null && i.Args.Length > 0 && i.Args[0] is int k && k == g.Index + 1);
            }
            // A footpath still without a way: behind the row of shops (no walk cuts through a shop). Say so plainly.
            foreach (var g in mall.Gates)
            {
                if (g.Kind != ShopEntrances.Footpath || g.Landed != "none") continue;
                ctx.Plan.Issues.RemoveAll(i => i.Key == Mall.NoLaneKey && i.Args != null && i.Args.Length > 0 && i.Args[0] is int k && k == g.Index + 1);
                ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = FootpathBehindKey, Args = new object[] { g.Index + 1 }, At = ctx.P3(g.At) });
            }
            int landed = 0; foreach (var g in mall.Gates) if (g.Landed != "none") landed++;
            ctx.Plan.Facts["gatesLanded"] = landed;
        }

        /// <summary>Did a gate routed to the lane land (cars reach the shops through it when no entrance opens onto the lot)?</summary>
        private static bool LaneReached(Mall mall)
        {
            foreach (var g in mall.Gates) if (g.Cars && (g.Landed == "lane" || g.Landed == "drive")) return true;
            return false;
        }

        /// <summary>The lot planned with its rows along and across the frame; the variant with the most stalls the game will accept.</summary>
        private static Mall.LotTry BestLot(Mall mall, float2[] lot, List<(float2 at, string kind)> entries, SettingsBag parking, uint salt)
        {
            Mall.LotTry best = null; float bestScore = float.MinValue;
            float ang = math.degrees(math.atan2(mall.MainDir.Value.y, mall.MainDir.Value.x));
            foreach (bool ring in new[] { true, false })
                foreach (float rot in new[] { 0f, 90f })
                {
                    if (best != null) break;   // the lot's own drive round it, rows along the frame first; the others only when that fails
                    var bag = new SettingsBag();
                    foreach (var kv in parking.Values) bag.Values[kv.Key] = kv.Value;
                    bag.Values["setback"] = mall.LotSetback > 0f ? (double)mall.LotSetback : 0.5;   // (strip malls) a planted edge for the street front
                    bag.Values["rowAngleMode"] = "fixed";
                    bag.Values["rowAngle"] = (double)(ang + rot);
                    bag.Values["perimeterRoad"] = ring;
                    var t = mall.PlanLot(lot, entries, bag, salt + (uint)rot + (ring ? 7u : 0u), true);
                    if (t == null || NetFit.Violations(t.Sub.Nets, t.Site).Count > 0 || Mall.Warns(t)) continue;
                    t.Sub.Issues.RemoveAll(i => i.Key != null && i.Key.EndsWith("PerimeterRoadDropped"));   // our choice to try, not the player's
                    t.Capacity -= DropBaysOffAsphalt(t.Sub);   // no stall marking on grass ("grassy parking spots")
                    // An aisle ending in a turning circle reads as a dead end (the user: "how do the cars even get in there"): each
                    // costs the stalls of a short row.
                    int dead = DeadEnds(t.Sub.Nets);
                    float score = t.Capacity - 12f * dead;
                    if (score > bestScore) { bestScore = score; best = t; }
                }
            return best;
        }

        /// <summary>Drops the stall markings that do not lie wholly on the lot's asphalt (it stopped short of them); returns how many.</summary>
        private static int DropBaysOffAsphalt(AreaPlan sub)
        {
            var asphalt = new List<float2[]>();
            foreach (var sf in sub.Surfaces) if (sf.Role == SurfaceRole.Asphalt) asphalt.Add(Poly.To2D(sf.Ring));
            int off = 0;
            for (int i = sub.Objects.Count - 1; i >= 0; i--)
            {
                var ob = sub.Objects[i];
                if (ob.Role != ObjectRole.ParkingBay) continue;
                // the whole stall, not its middle: at a slanted lot edge the asphalt stopped mid-stall (the game runner's big site)
                var c = ob.Position.xz;
                var ax = new float2(math.sin(ob.Yaw), math.cos(ob.Yaw)); var px = new float2(-ax.y, ax.x);
                foreach (var p in new[] { c, c + ax * 2.3f, c - ax * 2.3f, c + px * 1.1f, c - px * 1.1f })
                {
                    bool on = false;
                    foreach (var a in asphalt) if (Poly.Contains(a, p) || Poly.DistanceToBoundary(a, p) < 0.35f) { on = true; break; }
                    if (!on) { off++; sub.Objects.RemoveAt(i); break; }
                }
            }
            return off;
        }

        /// <summary>Net ends no other net shares and that attach to no street or lane: aisles that stop in a turning circle.</summary>
        private static int DeadEnds(List<PlannedNet> nets)
        {
            var ends = new List<(float2 p, bool attached)>();
            foreach (var n in nets)
            {
                if (n == null || n.Role != NetRole.DriveAisle) continue;
                ends.Add((n.A.xz, n.StartAttach != -1));
                ends.Add((n.D.xz, n.EndAttach != -1));
            }
            int dead = 0;
            for (int i = 0; i < ends.Count; i++)
            {
                if (ends[i].attached) continue;
                int same = 0;
                for (int j = 0; j < ends.Count; j++) if (j != i && math.distancesq(ends[i].p, ends[j].p) < 0.04f) same++;
                if (same == 0) dead++;
            }
            return dead;
        }
    }
}
