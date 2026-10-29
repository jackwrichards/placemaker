// Strip mall: street → parking lot → a lot lane → a row of shops facing the lot across the lane → a green
// buffer behind. Styles (presets):
//   classic    the lot fills the frontage; a U-shaped lane (both legs on the street) runs behind it and the
//              shops line the lane's far side, facing the lot
//   corner     the lot holds a street corner; an L-shaped lane wraps it from one street to the other and the
//              shops wrap the lane in an L (corner retail)
//   rearAlley  shops on the street; a service lane loops behind them and a lot behind the lane is entered
//              from it — the street keeps its shop fronts, the cars go round the back
// The lot has street entrances (connector to the kerb, as the Parking layer builds them) and entrances on the
// lane (short alley stubs sharing a node with the lane).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    [AreaGenerator(15, Group = Group)]
    public sealed partial class StripMallLayer : CommercialLayer
    {
        protected override string Area => "StripMall";
        public override string Id => "stripMall";
        public override string Icon => "Media/Game/Icons/ZoneCommercial.svg";

        /// <summary>(shops) A second strip on the back street when the first leaves half the block (a lot, a lane and two cells of shops: ~45 m).</summary>
        protected override float ComposeMoreBelow => 0.6f;
        /// <summary>(shops4) A lot one aisle deep, the lane and two-cell plots (about 48 m) by three plots: smaller says "too small"; an area
        /// big enough but too narrow behind every entrance says so itself (LotCentre, NoRoom).</summary>
        public override float MinArea => 1600f;
        protected override float MoreReach => 44f;
        /// <summary>(shops2) Plan fact set when the whole site was laid out as rows of stores round lots (<see cref="Rows"/>): the number of store rows.</summary>
        public const string RowsFact = "stripRows";

        public static readonly string[] Styles = { "classic", "corner", "rearAlley" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "classic", ControlTier.Essential, "layout", Styles).Tip();
            c.Slider("frontage", 240, 60, 240, 10, "m", ControlTier.More, "layout").Tip().When("style!=corner");
            // (shops2) The default is the classic strip mall: a long row of single-storey shops (low-density commercial, 32 m deep) round a big lot.
            CommonControls(c, ZoneKinds.CommercialLow, 32, "large", maxDepth: 48);
            // (rf-shops3) The architect's extras: walks, plaza, bikes, food trucks, pads, designed planting, building sites, plot trees, worn ground.
            // (shops4) Off by default: the strip mall is the lot and the shops (the user: "what are those green rectangles, why is there a
            // path going nowhere"); the extras stay for the player to add.
            // (shops4) A square in the middle of the row of shops: a plaza, a small park or a garden (off by default).
            Shared(c.Choice("square", "none", ControlTier.More, "extras", "none", "plaza", "park", "garden", "pond")).Tip();
            ExtrasControls(c, walkways: false, plaza: false, bikes: "none", trucks: 0, pads: false, designed: false, hedges: true, sites: true, worn: false, water: "none", plotTrees: false);
            c.Fixed("style");
        }

        protected override void DefinePresets()
        {
            // (shops2) The layout of the retail park's Megastores (the user: A-, "should just be what the regular strip mall is"), grown as
            // low-rise shops (a real strip mall is one storey, not office blocks): a big lot; on a big site several rows round a grid of lots.
            Preset("classic", ("style", "classic"), ("lotSize", "large"), ("plotDepth", 32.0), ("zoneKind", ZoneKinds.CommercialLow), ("evShare", 10.0), ("frontage", 240.0));
            Preset("corner", ("style", "corner"), ("lotSize", "medium"), ("plotDepth", 24.0), ("zoneKind", ZoneKinds.CommercialLow));
            Preset("rearAlley", ("style", "rearAlley"), ("lotSize", "medium"), ("plotDepth", 16.0), ("zoneKind", ZoneKinds.CommercialLow));
            Preset("neighbourhood", ("style", "classic"), ("lotSize", "small"), ("frontage", 90.0), ("plotDepth", 16.0), ("lotGreen", "medians"), ("zoneKind", ZoneKinds.CommercialLow));
            // (shops2) Big box: a few very wide, deep low-rise stores (one-storey boxes) on the biggest lots.
            Preset("bigBox", ("style", "classic"), ("lotSize", "large"), ("frontage", 240.0), ("plotDepth", 48.0), ("zoneKind", ZoneKinds.CommercialLow), ("evShare", 10.0), ("lotMedian", 3.5));
            // (shops4) Variations of the strip mall with a square in the middle of the row: a café plaza, a small park.
            Preset("plazaRow", ("style", "classic"), ("lotSize", "large"), ("plotDepth", 32.0), ("zoneKind", ZoneKinds.CommercialLow), ("evShare", 10.0), ("square", "plaza"));
            Preset("parkRow", ("style", "classic"), ("lotSize", "large"), ("plotDepth", 32.0), ("zoneKind", ZoneKinds.CommercialLow), ("evShare", 10.0), ("square", "park"));
            Preset("pondRow", ("style", "classic"), ("lotSize", "large"), ("plotDepth", 32.0), ("zoneKind", ZoneKinds.CommercialLow), ("evShare", 10.0), ("square", "pond"));
        }

        /// <summary>(rf-shops3) Planned from the player's entrances (the centre: StripMallCentre.cs); the hidden corner and rear-alley styles too.</summary>
        protected override bool UsesEntrances => true;

        protected override bool ComposeFromEntrances(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            string style = ctx.S("style");
            if (style != "classic" && style != "corner") return false;
            // (shops4) The lot first, the shops behind it (LotCentre.cs); the ring centre (StripMallCentre.cs) is no longer used.
            // (corner, 2026-10-28) The corner strip mall is the same lot with the shops in an L: along the back and down one side, the
            // lot open to the other street (the user's photo of a corner strip mall; the old L lane round a corner lot built a park).
            bool big = ctx.Raw("plotDepth") >= 44f;
            string sq = ctx.S("square");
            // the land behind and beside the rows is the shops' back side (MallFront.cs), not a park
            mall.BackOfHouse = true;
            // a planted edge round the lot for the street front's hedge and sign (MallFront.StreetFront)
            mall.LotSetback = ctx.B("hedges") ? 2.5f : 0f;
            return LotCentre(ctx, mall, runs, new LotLayout
            {
                MaxPiece = big ? 6 : 3, AnchorWeight = big ? 1.25f : 1.8f, Square = sq == "none" || string.IsNullOrEmpty(sq) ? null : sq,
                Shape = style == "corner" ? "L" : null,
            });
        }

        /// <summary>(2026-10-28) The street front of the user's photo: a low hedge along the sidewalk, open at the drives; a pylon sign by the entrance.</summary>
        protected override void FinishFront(LayerContext ctx, Mall mall) => mall.StreetFront(ctx.B("hedges"), true, 0xF00u);

        protected override void Compose(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            string style = ctx.S("style");
            if (style == "corner" && Corner(ctx, mall, runs)) return;
            if (style == "rearAlley" && RearAlley(ctx, mall, runs[0])) return;
            if (style != "classic") ctx.Issue(IssueSeverity.Info, FallbackKey);
            // (shops2) The whole site as rows of stores facing a grid of lots (a real strip mall / retail centre); another frontage
            // adds nothing to it (ComposeMore).
            if (style == "classic")
            {
                if (mall.Extra) { if (ctx.Plan.Facts.ContainsKey(RowsFact)) return; }
                else if (Rows(ctx, mall, runs[0])) return;
            }
            if (!Classic(ctx, mall, runs[0], runs))
            {
                if (mall.Extra) return;
                // No room for a lot and a lane: plots straight on the street.
                var r = runs[0];
                mall.ShopRow(r, r.From + 2f, r.To - 2f, 0f, Cells(ctx));
                ctx.Issue(IssueSeverity.Info, NoLotKey);
            }
        }

        // ───────────── (shops2) rows of stores round a grid of lots ─────────────

        /// <summary>
        /// (shops2) The strip mall fronts the busiest street: of the runs nearly as long as the longest, the one along the widest
        /// street. The lot faces the main road (as real strip malls do) and the last row of stores backs onto the quieter street
        /// (in the game a row backing onto a four-lane road lost its cells to that road's deep zone block).
        /// </summary>
        private static StreetShops.Run MainStreet(LayerContext ctx, List<StreetShops.Run> runs)
        {
            var roads = StreetFrontage.Roads(ctx.Input);
            float Width(StreetShops.Run r)
            {
                var p = r.At(0.5f * (r.From + r.To), 0f);
                float best = float.MaxValue, w = 0f;
                foreach (var rd in roads) { float d = math.sqrt(SegmentIndex.DistSq(p, rd.A, rd.B)); if (d < best) { best = d; w = rd.HalfWidth; } }
                return w;
            }
            var main = runs[0];
            float mw = Width(main);
            foreach (var r in runs)
                if (r.Length >= 0.7f * runs[0].Length && Width(r) > mw + 0.5f) { main = r; mw = Width(r); }
            return main;
        }

        /// <summary>(shops2) A lot band at least this deep (two double-loaded aisles) between rows when there are several rows of stores.</summary>
        private static float MinLotBetween(LayerContext ctx) => Mall.LotDepth(math.max(2, Mall.Aisles(ctx.S("lotSize"))));

        /// <summary>
        /// (shops2) The user: "if I zone a big area and say strip mall, I should get a giant strip mall with multiple rows of buildings
        /// and a labyrinth of parking". The site, seen from its main street (frame: s along it, t inward), becomes:
        ///   • rails: lanes from the main street straight to the back street (or a U closed by the last rung when there is none),
        ///     the outer two a store depth in from the side streets, more between them every ~180 m;
        ///   • depth bands, front to back: a lot on the main street, then pairs of store rows back to back between two rungs
        ///     (each row faces its rung and the lot beyond it), lots between, and a last row backing onto the back of the site
        ///     when the depth fits it; the lots stay at least two aisles deep (big lots: three) between rows;
        ///   • rows of big stores along the outer rails' outer sides (the horseshoe of the retail park's Megastores);
        ///   • everything else becomes lots (LotFirst/FillRest), entered from the streets, the rails and the rungs.
        /// False when the site cannot take it (the old composition is used then).
        /// </summary>
        private static bool Rows(LayerContext ctx, Mall mall, StreetShops.Run r)
        {
            // Why the rows did not fit (plan fact stripRowsFail: the step that gave up), for the gallery and the logs.
            var planMark = PlanMark.Of(ctx.Plan); var mallMark = mall.Mark();
            bool Fail(int step) { planMark.Restore(ctx.Plan); mall.Rewind(mallMark); ctx.Plan.Facts["stripRowsFail"] = step; return false; }
            var ring = mall.Region.Ring;
            float sMin = float.MaxValue, sMax = float.MinValue, tMax = float.MinValue;
            foreach (var p in ring)
            {
                float s = math.dot(p - r.A, r.Dir), t = math.dot(p - r.A, r.In);
                sMin = math.min(sMin, s); sMax = math.max(sMax, s); tMax = math.max(tMax, t);
            }
            var (asset, h) = Mall.ShopLane(ctx);
            int cells = Cells(ctx);
            // Shopfronts: three cells wide for the strip mall's small shops, six for the big boxes.
            int maxPiece = ctx.Raw("plotDepth") >= 44f ? 6 : 3;
            if (tMax < 60f || sMax - sMin < 90f) return Fail(1);

            // Where a rail's line is inside the site (its front and back ends; the first stretch from the front).
            (float a, float b) Span(float x)
            {
                float a = float.NaN, b = float.NaN;
                for (float t = -2f; t <= tMax + 2f; t += 1f)
                    if (mall.Region.Contains(r.At(x, t))) { if (float.IsNaN(a)) a = t; b = t; }
                    else if (!float.IsNaN(a)) break;
                return (a, b);
            }
            // The outer rails: the pair of lines that hold the largest rectangle between them (the whole site for a rectangle, the
            // inscribed square of a round block, the long arm of an L).
            var cand = new List<(float x, float a, float b)>();
            for (float x = sMin + 2f; x <= sMax - 2f; x += 4f) { var sp = Span(x); if (!float.IsNaN(sp.a) && sp.b - sp.a >= 40f) cand.Add((x, sp.a, sp.b)); }
            float bestArea = 0f, xL = 0f, xR = 0f;
            for (int i = 0; i < cand.Count; i++)
            {
                float lo = cand[i].a, hi = cand[i].b;
                for (int j = i + 1; j < cand.Count; j++)
                {
                    lo = math.max(lo, cand[j].a); hi = math.min(hi, cand[j].b);
                    if (hi - lo < 40f) break;
                    float w = cand[j].x - cand[i].x;
                    if (w < 90f) continue;
                    if (w * (hi - lo) > bestArea) { bestArea = w * (hi - lo); xL = cand[i].x; xR = cand[j].x; }
                }
            }
            if (bestArea <= 0f) return Fail(2);
            // Rows of stores outside the outer rails where the land beyond them is a store deep (moving the rail in for it).
            float dd0 = cells * Mall.Cell;
            float Outside(float x, float dir, float t)
            {
                float w = 0f;
                for (float s = 1f; s < 200f; s += 1f) { if (!mall.Region.Contains(r.At(x + dir * s, t))) break; w = s; }
                return w;
            }
            bool sideRowsL, sideRowsR;
            {
                var sl = Span(xL); var sr = Span(xR);
                float mid = 0.5f * (math.max(sl.a, sr.a) + math.min(sl.b, sr.b));
                float wl = Outside(xL, -1f, mid), wr = Outside(xR, 1f, mid);
                float needL = dd0 + h + mall.KerbMargin(r.At(xL - wl, mid), r.In) + 1f;
                float needR = dd0 + h + mall.KerbMargin(r.At(xR + wr, mid), r.In) + 1f;
                sideRowsL = false; sideRowsR = false;
                if (wl >= needL) sideRowsL = true;
                else if (xR - (xL + needL - wl) >= 140f) { xL += needL - wl; sideRowsL = true; }
                if (wr >= needR) sideRowsR = true;
                else if ((xR - (needR - wr)) - xL >= 140f) { xR -= needR - wr; sideRowsR = true; }
                if (!sideRowsL) xL = math.max(xL, sMin + h + 3f);
                if (!sideRowsR) xR = math.min(xR, sMax - h - 3f);
            }
            int intervals = math.max(1, (int)math.round((xR - xL) / 180f));
            var xs = new List<float>();
            for (int k = 0; k <= intervals; k++) xs.Add(math.lerp(xL, xR, (float)k / intervals));
            var spans = new (float a, float b)[xs.Count];
            bool through = true;
            float t0 = float.MinValue, t1 = float.MaxValue;
            for (int k = 0; k < xs.Count; k++)
            {
                spans[k] = Span(xs[k]);
                if (float.IsNaN(spans[k].a)) return Fail(3);
                if (!StreetShops.StreetEnd(ctx.Input, r.At(xs[k], spans[k].b), r.In, out _, out _, out _)) through = false;
                if (!StreetShops.StreetEnd(ctx.Input, r.At(xs[k], spans[k].a), -r.In, out _, out _, out _)) return Fail(4);
                if (k == 0 || k == xs.Count - 1) { t0 = math.max(t0, spans[k].a); t1 = math.min(t1, spans[k].b); }
            }
            // The common stretch holds between the outer rails too (a concave back street bows in between them).
            for (float x = xs[0] + 4f; x < xs[xs.Count - 1] - 4f; x += 4f) { var sp = Span(x); if (!float.IsNaN(sp.a)) { t0 = math.max(t0, sp.a); t1 = math.min(t1, sp.b); } }
            float D = t1 - t0;
            if (D < 60f) return Fail(5);

            // Depth pattern over the rails' common stretch: lot, then n pairs (rung, row, row, rung, lot), then optionally a last
            // rung and row (needed when there is no street behind: it is the U's back run).
            float lotMin1 = Mall.LotDepth(1) + 4f, lotBetween = MinLotBetween(ctx);
            // A last row backing onto a street stops short of its first zone row (else the row is cut shallower and a lawn is left).
            float kbBack = through ? mall.KerbMargin(r.At(0.5f * (xs[0] + xs[xs.Count - 1]), t1 + 1f), r.Dir) : 0f;
            int bestRows = 0, bestN = 0, bestCells = cells; bool bestEnd = false; float bestLot = 0f;
            for (int c = cells; c >= 2 && bestRows == 0; c--)
            {
                float d = c * Mall.Cell;
                for (int n = 0; n <= 12; n++)
                    foreach (bool end in new[] { true, false })
                    {
                        if (!through && !end) continue;
                        int nRows = 2 * n + (end ? 1 : 0);
                        if (nRows == 0) continue;
                        float fixedT = n * (4f * h + 2f * d) + (end ? 2f * h + d + kbBack : 0f);
                        float lot = (D - fixedT) / (n + 1);
                        if (lot < (nRows >= 2 ? lotBetween : lotMin1)) continue;
                        // more rows first; then a last row backing onto the back (stores' backs to the neighbours, not a lot)
                        if (nRows > bestRows || (nRows == bestRows && end && !bestEnd))
                        { bestRows = nRows; bestN = n; bestEnd = end; bestLot = lot; bestCells = c; }
                    }
            }
            if (bestRows == 0) return Fail(6);
            cells = bestCells;
            float dd = cells * Mall.Cell;
            var levels = new List<(float y, int side)>();   // rung centre lines and the side their row stands on (+1 deeper, -1 nearer)
            {
                float t = t0 + bestLot;
                for (int k = 0; k < bestN; k++)
                {
                    levels.Add((t + h, +1));
                    levels.Add((t + 3f * h + 2f * dd, -1));
                    t += 4f * h + 2f * dd + bestLot;
                }
                if (bestEnd) levels.Add((t + h, +1));
            }

            var rails = new Mall.Lane[xs.Count];
            Mall.Lane u = null;
            if (through)
            {
                for (int k = 0; k < xs.Count; k++)
                    rails[k] = mall.Connect(Mall.End.Street(r.At(xs[k], spans[k].a), -r.In), null, Mall.End.Street(r.At(xs[k], spans[k].b), r.In), NetRole.Alley, asset, h);
            }
            else
            {
                // No street behind: the last rung closes the outer rails into a U; the stores of the last row back onto the site's edge.
                if (!bestEnd) return Fail(7);
                float yU = levels[levels.Count - 1].y;
                u = mall.Connect(Mall.End.Street(r.At(xs[0], spans[0].a), -r.In), new[] { r.At(xs[0], yU), r.At(xs[xs.Count - 1], yU) },
                    Mall.End.Street(r.At(xs[xs.Count - 1], spans[xs.Count - 1].a), -r.In), NetRole.Alley, asset, h);
                if (u == null) return Fail(8);
                rails[0] = u; rails[xs.Count - 1] = u;
                for (int k = 1; k + 1 < xs.Count; k++)
                    rails[k] = mall.Connect(Mall.End.Street(r.At(xs[k], spans[k].a), -r.In), null, Mall.End.OnLane(u, r.At(xs[k], yU)), NetRole.Alley, asset, h);
            }
            int railCount = 0; foreach (var rl in rails) if (rl != null) railCount++;
            if (railCount < 2)
            {
                foreach (var rl in rails) mall.Remove(rl);
                return Fail(9);
            }

            // Rungs between neighbouring rails at every level both rails reach.
            var rungs = new List<(Mall.Lane lane, int side)>();
            for (int li = 0; li < levels.Count; li++)
            {
                var (y, side) = levels[li];
                if (u != null && li == levels.Count - 1) continue;   // the U's back run is this rung
                for (int k = 0; k + 1 < xs.Count; k++)
                {
                    if (rails[k] == null || rails[k + 1] == null) continue;
                    bool In(int i) => y >= spans[i].a + h + 6f && y <= spans[i].b - h - 6f;
                    if (!In(k) || !In(k + 1)) continue;
                    // (a rail ending on a street at an angle runs a little askew: the rung meets it where it really is)
                    var pa = Mall.Project(rails[k], r.At(xs[k], y), out _); var pb = Mall.Project(rails[k + 1], r.At(xs[k + 1], y), out _);
                    var g = mall.Connect(Mall.End.OnLane(rails[k], pa), null, Mall.End.OnLane(rails[k + 1], pb), NetRole.Alley, asset, h);
                    if (g != null) rungs.Add((g, side));
                }
            }

            // The lots go in before the rows: their stubs join the rungs and rails where the parking planner puts them, and the
            // rows across those lanes are then split at the real joins (in the game a row planned first lost its cells where a
            // later stub split the lane under it: "10 -> 0" plots). The rows' land is held meanwhile.
            var holds = new List<float2[]>();
            void HoldRow(Mall.Lane lane, int side, int seg0, int seg1)
            {
                if (lane == null) return;
                for (int i = seg0; i <= seg1 && i + 1 < lane.Points.Count; i++)
                {
                    var f = Mall.Frame(lane.Points[i], lane.Points[i + 1]);
                    float a = side > 0 ? h : -(Mall.ZoneOffset(h) + dd + 1f), b = side > 0 ? Mall.ZoneOffset(h) + dd + 1f : -h;
                    // Clear of the crossing lanes at its ends (further in where a rail runs askew).
                    for (float trim = h + 0.5f; trim <= h + 16f; trim += 2f)
                    {
                        if (f.To - 2f * trim < 8f) break;
                        var rect = Mall.Rect(f, trim, f.To - trim, a, b);
                        if (mall.Free(rect)) { holds.Add(mall.Hold(rect)); break; }
                    }
                }
            }
            foreach (var (lane, side) in rungs) HoldRow(lane, side, 0, 0);
            if (u != null) HoldRow(u, +1, 0, u.Points.Count - 2);
            else
            {
                if (sideRowsL) HoldRow(rails[0], +1, 0, rails[0].Points.Count - 2);
                var lastRail = rails[xs.Count - 1];
                if (sideRowsR && lastRail != null) HoldRow(lastRail, -1, 0, lastRail.Points.Count - 2);
            }
            mall.MainDir = r.Dir;
            mall.LotBudget = float.MaxValue;
            RestLots(ctx, mall, 0xA80u, 400f, false, true);
            foreach (var hd in holds) mall.Release(hd);

            // Store rows: along the rungs (facing the lot across them), then the U's back run, then outside the outer rails.
            int rows = 0;
            foreach (var (lane, side) in rungs) rows += mall.LaneRows(lane, cells, null, side, 0f, 0f, maxPiece, 2) > 0 ? 1 : 0;
            if (u != null)
                mall.LaneRows(u, cells, null, +1, mall.GateTrim(u, true, Mall.Cell), mall.GateTrim(u, false, Mall.Cell), maxPiece, 2);
            {
                if (u == null)
                {
                    if (rails[0] != null && sideRowsL) mall.LaneRows(rails[0], cells, null, +1, mall.GateTrim(rails[0], true, Mall.Cell), mall.GateTrim(rails[0], false, Mall.Cell), maxPiece, 2);
                    var last = rails[xs.Count - 1];
                    if (last != null && sideRowsR) mall.LaneRows(last, cells, null, -1, mall.GateTrim(last, true, Mall.Cell), mall.GateTrim(last, false, Mall.Cell), maxPiece, 2);
                }
            }
            if (mall.Shops.Count == 0)
            {
                foreach (var g in rungs) mall.Remove(g.lane);
                foreach (var rl in rails) mall.Remove(rl);
                return Fail(10);
            }
            ctx.Plan.Facts[RowsFact] = bestRows;
            ctx.Plan.Facts["stripRails"] = railCount;
            ctx.Plan.Facts["stripRungs"] = rungs.Count;
            return true;
        }

        /// <summary>(shops2) After the rows of stores, the rest of the site is lots (no access alleys or street rows across the front lot).</summary>
        protected override void LotFirst(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            // (shops4) The lot-first layout planned its lots itself; opening a shut-in leftover by giving back a shop plot (OpenAccess)
            // took every shop of an odd site.
            if (mall.LotFirstMode) return;
            if (!ctx.Plan.Facts.ContainsKey(RowsFact) && !ctx.Plan.Facts.ContainsKey(CentreFact)) { base.LotFirst(ctx, mall, runs); return; }
            // Every bay between the rails and rungs is a whole lot: no budget shared out (a share cut a bay's lot short by the
            // crescents beside a round block that never become lots, and planted the rest).
            mall.LotBudget = float.MaxValue;
            RestLots(ctx, mall, 0xB00u, 400f, false, true);
        }

        /// <summary>The frontage span used: corners kept clear for lane junctions, capped and centred.</summary>
        private static void Span(LayerContext ctx, StreetShops.Run r, out float s0, out float s1)
        {
            s0 = r.From + (r.StartCorner ? Mall.CornerClear : 2f);
            s1 = r.To - (r.EndCorner ? Mall.CornerClear : 2f);
            float max = math.max(60f, ctx.Raw("frontage"));
            if (s1 - s0 > max) { float c = 0.5f * (s0 + s1); s0 = c - max * 0.5f; s1 = c + max * 0.5f; }
        }

        private static bool Classic(LayerContext ctx, Mall mall, StreetShops.Run r, List<StreetShops.Run> runs = null)
        {
            Span(ctx, r, out float s0, out float s1);
            if (mall.Extra)
            {
                // (shops) Another frontage: the stretch with free land behind it for a lot, the lane and two cells of shops.
                var b = mall.BestSpan(r, 50f, Mall.LotDepth(1) + 2f * Mall.AlleyHalf + 2f * Mall.Cell, s0, s1, true);
                if (b == null) return false;
                s0 = b.Value.s0; s1 = b.Value.s1;
            }
            if (s1 - s0 < 50f) return false;
            int cells = Cells(ctx), aisles = Mall.Aisles(ctx.S("lotSize"));
            // (shops) Free land only: a second strip on another street (ComposeMore) stops at the first one's shops.
            float reach = math.min(mall.Reach(r, s0, s1), mall.FreeReach(r, s0, s1));
            // (s26) The shops front this lane (across it from the lot): it must carry zone blocks.
            var (laneAsset, h) = Mall.ShopLane(ctx);
            // (shops) A block deep enough for two strips, with a street behind: this one takes the front half, so the back
            // street gets its own strip (ComposeMore) back to back with this one instead of half a block of lot or lawn.
            float need = Mall.LotDepth(1) + 2f * h + 2f * Mall.Cell + 1f;
            if (!mall.Extra && runs != null && reach >= 2f * need + 4f)
                foreach (var o in runs)
                    if (math.dot(o.In, r.In) < -0.7f && o.Length >= 60f) { reach = math.max(need, reach * 0.5f - 2f); break; }
            // (shops) The shops keep their depth before the lot does: fewer aisles first, then shallower shops.
            while (aisles > 0 && Mall.LotDepth(aisles) + 2 * h + cells * Mall.Cell > reach - 1f)
                if (aisles > 1) aisles--; else if (cells > 2) cells--; else aisles--;
            if (aisles == 0) return false;
            float lt = Mall.LotDepth(aisles);
            var lane = mall.AddLane(r.At(s0 + h, 0f), -r.In, new[] { r.At(s0 + h, lt + h), r.At(s1 - h, lt + h) }, r.At(s1 - h, 0f), -r.In, NetRole.Alley, laneAsset, h);
            if (lane == null) return false;
            var entrances = new List<(float2, string)>();
            LotEntrances(entrances, r, s0 + 2 * h, s1 - 2 * h, 0f, "entrance");
            // Lane entrances between and beside the street ones, so cars can circulate lot ↔ shops.
            entrances.Add((r.At(math.lerp(s0, s1, 0.5f), lt), "alley"));
            if (s1 - s0 > 120f) { entrances.Add((r.At(math.lerp(s0, s1, 0.12f) + 4f, lt), "alley")); entrances.Add((r.At(math.lerp(s0, s1, 0.88f) - 4f, lt), "alley")); }
            // (s25) Walkways from the lot to the shop fronts across the lane (a footpath meeting the lane at a node).
            Walkways(entrances, r, s0, s1, lt);
            mall.Lot(Mall.Rect(r, s0 + 2 * h, s1 - 2 * h, 0f, lt), entrances, Mall.Parking(ctx), 0xA1u);
            // The shop row faces the lot across the lane's back run (and wraps the legs' ends).
            mall.ShopRow(r, s0, s1, lt + 2 * h, cells);
            return true;
        }

        private static bool RearAlley(LayerContext ctx, Mall mall, StreetShops.Run r)
        {
            Span(ctx, r, out float s0, out float s1);
            if (s1 - s0 < 50f) return false;
            int cells = Cells(ctx), aisles = Mall.Aisles(ctx.S("lotSize"));
            float h = Mall.AlleyHalf;
            float reach = mall.Reach(r, s0, s1);
            while (aisles > 0 && cells * Mall.Cell + 2 * h + Mall.LotDepth(aisles) > reach - 1f)
                if (cells > 1) cells--; else aisles--;
            float back = cells * Mall.Cell;
            var lane = mall.AddLane(r.At(s0 + h, 0f), -r.In, new[] { r.At(s0 + h, back + h), r.At(s1 - h, back + h) }, r.At(s1 - h, 0f), -r.In, NetRole.Alley, Mall.AlleyAsset, h);
            if (lane == null) return false;
            // Shop fronts on the street, backs (and service) on the lane.
            mall.ShopRow(r, s0 + 2 * h, s1 - 2 * h, 0f, cells);
            if (aisles > 0)
            {
                float t0 = back + 2 * h, lt = Mall.LotDepth(aisles);
                var entrances = new List<(float2, string)>();
                LotEntrances(entrances, r, s0, s1, t0, "alley");
                Walkways(entrances, r, s0, s1, t0);
                if (!mall.Lot(Mall.Rect(r, s0, s1, t0, t0 + lt), entrances, Mall.Parking(ctx), 0xA2u)) ctx.Issue(IssueSeverity.Info, NoLotKey);
            }
            else ctx.Issue(IssueSeverity.Info, NoLotKey);
            return true;
        }

        /// <summary>Corner retail: lot on a street corner, an L lane round it, shops round the lane.</summary>
        private static bool Corner(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            // The corner whose two streets are longest (the shorter of the two counts).
            int best = -1, next = -1; float bestLen = 0f;
            for (int i = 0; i < runs.Count; i++)
            {
                if (!runs[i].EndCorner) continue;
                for (int j = 0; j < runs.Count; j++)
                {
                    if (i == j || !runs[j].StartCorner || math.distance(runs[i].At(runs[i].To, 0f), runs[j].At(runs[j].From, 0f)) > 1f) continue;
                    float len = math.min(runs[i].Length, runs[j].Length);
                    if (len > bestLen) { bestLen = len; best = i; next = j; }
                }
            }
            if (best < 0 || bestLen < 70f) return false;
            var a = runs[best];
            float h = Mall.AlleyHalf;
            int cells = Cells(ctx), aisles = Mall.Aisles(ctx.S("lotSize"));
            float lot = Mall.LotDepth(aisles);
            float la = math.min(lot + 8f, a.Length * 0.55f), lb = math.min(lot, runs[next].Length * 0.55f);
            if (la < 24f || lb < 20f) return false;
            // A frame: u back along street A from the corner, t inward (= along street B).
            float2 P(float u, float t) => a.At(a.To - u, t);
            float legU = la + h, backT = lb + h;
            var lane = mall.AddLane(P(legU, 0f), -a.In, new[] { P(legU, backT) }, P(0f, backT), a.Dir, NetRole.Alley, Mall.AlleyAsset, h);
            if (lane == null) return false;
            var entrances = new List<(float2, string)>
            {
                (P(la * 0.5f, 0f), "entrance"),
                (P(0f, lb * 0.5f), "entrance"),
                (P(la, lb * 0.5f), "alley"),
                (P(la * 0.5f, lb), "alley"),
            };
            var ring = Poly4(P(la, 0f), P(0f, 0f), P(0f, lb), P(la, lb));
            if (!mall.Lot(ring, entrances, Mall.Parking(ctx), 0xA3u)) ctx.Issue(IssueSeverity.Info, NoLotKey);
            // Shops: along the leg (facing it, backs to the rest of the block) and along the back run, meeting in an L.
            float d = cells * Mall.Cell;
            int pieces = math.max(1, (int)math.ceil((backT + h) / (3 * Mall.Cell)));
            for (int k = 0; k < pieces; k++)
            {
                float t0 = (backT + h) * k / pieces, t1 = (backT + h) * (k + 1) / pieces;
                for (int dd = cells; dd >= 1; dd--)
                    if (mall.Shop(Poly4(P(legU + h + dd * Mall.Cell, t0), P(legU + h, t0), P(legU + h, t1), P(legU + h + dd * Mall.Cell, t1)))) break;
            }
            // The back row ends where the lane does: a plot beyond it would face no road (no zone block reaches it).
            float uEnd = legU + h;
            pieces = math.max(1, (int)math.ceil(uEnd / (3 * Mall.Cell)));
            for (int k = 0; k < pieces; k++)
            {
                float u0 = uEnd * k / pieces, u1 = uEnd * (k + 1) / pieces;
                for (int dd = cells; dd >= 1; dd--)
                    if (mall.Shop(Poly4(P(u1, backT + h), P(u0, backT + h), P(u0, backT + h + dd * Mall.Cell), P(u1, backT + h + dd * Mall.Cell)))) break;
            }
            // Shops continue along both streets beyond the L where the frontage allows (a row on the street).
            float rest = a.To - (legU + h + d + 4f);
            if (rest - a.From > 2 * Mall.Cell) mall.ShopRow(a, a.From + (a.StartCorner ? 2f : 0f), rest, 0f, cells);
            var b = runs[next];
            float bFrom = b.From + backT + h + d + 4f;
            if (b.To - bFrom > 2 * Mall.Cell) mall.ShopRow(b, bFrom, b.To - (b.EndCorner ? 2f : 0f), 0f, cells);
            return true;
        }

        /// <summary>Two pedestrian entrances (PLT Fussweg) on the lot edge at t, between the car entrances.</summary>
        private static void Walkways(List<(float2, string)> list, StreetShops.Run r, float s0, float s1, float t)
        {
            if (s1 - s0 < 60f) return;
            list.Add((r.At(math.lerp(s0, s1, 0.32f), t), "footpath"));
            list.Add((r.At(math.lerp(s0, s1, 0.68f), t), "footpath"));
        }


        private static float2[] Poly4(float2 a, float2 b, float2 c, float2 d) => Geometry.Poly.EnsureCcw(new[] { a, b, c, d });
    }
}
