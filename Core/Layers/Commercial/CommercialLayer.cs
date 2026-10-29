// s14: the "Commercial" category (Make-row group "commercial"). Each type is a composition built with the
// Mall kit: lanes, zoned shop rows (cutouts), parking lots from the Parking planner and green / plaza pieces
// from the Park, Plaza and Market square planners. Shared controls read the same on every type
// (labels Placemaker.Commercial.<id>, zone type Placemaker.Zone.*).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public abstract partial class CommercialLayer : LeisureLayer
    {
        public const string Group = "commercial";
        protected const string K = "Placemaker.Commercial.";
        public const string NoStreetKey = K + "Issue.NoStreet";
        public const string NoLotKey = K + "Issue.NoLot";
        public const string FallbackKey = K + "Issue.Fallback";
        public const string PadKey = K + "Issue.Pad";
        public const string RestKey = K + "Issue.Rest";
        /// <summary>(rf-shops3) A shops type planned from entrances whose layout does not fit the area, with no street along it.</summary>
        public const string NoLayoutKey = K + "Issue.NoLayout";

        protected override bool TypedEntrances => false;
        protected override bool HostsStreetShops => false;
        public override float MinArea => 2500f;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Paved, StreetFrontage.ShopsTag };

        /// <summary>
        /// (rf-shops3) The type is planned from the player's entrances (ShopEntrances, as in ParkingLotTool): an "entrances" control,
        /// nothing suggested, Build waits for a way in for cars, and the layout joins the streets only where the player said.
        /// </summary>
        protected virtual bool UsesEntrances => false;

        public override Readiness CheckReady(AreaInput input) => UsesEntrances ? ShopEntrances.Ready(input) : base.CheckReady(input);

        public override List<EdgeMarker> SuggestMarkers(AreaInput input) => UsesEntrances ? new List<EdgeMarker>() : base.SuggestMarkers(input);

        /// <summary>
        /// (rf-shops3) The architect's extras (panel group "Extras", shared keys Placemaker.Commercial.*; Mall.Extras): each type passes
        /// its defaults, each style may override them in its preset.
        /// </summary>
        protected static void ExtrasControls(ControlSet c, bool walkways = true, bool plaza = true, string bikes = "racks", int trucks = 2, bool pads = false,
            bool designed = true, bool hedges = true, bool sites = true, bool worn = true, string water = "none", bool plotTrees = true)
        {
            Shared(c.Toggle("walkways", walkways, ControlTier.More, "extras")).Tip();
            Shared(c.Toggle("pocketPlaza", plaza, ControlTier.More, "extras")).Tip();
            Shared(c.Choice("bikes", bikes, ControlTier.More, "extras", "none", "racks", "paths")).Tip();
            Shared(c.Slider("foodTrucks", trucks, 0, 6, 1, "count", ControlTier.More, "extras")).Tip();
            Shared(c.Toggle("pads", pads, ControlTier.More, "extras")).Tip();
            Shared(c.Choice("landscape", designed ? "designed" : "plain", ControlTier.More, "extras", "plain", "designed")).Tip();
            Shared(c.Toggle("hedges", hedges, ControlTier.More, "extras")).Tip();
            Shared(c.Toggle("buildingSites", sites, ControlTier.More, "extras")).Tip();
            Shared(c.Toggle("plotTrees", plotTrees, ControlTier.More, "extras")).Tip();
            Shared(c.Toggle("wornGround", worn, ControlTier.More, "extras")).Tip();
            Shared(c.Choice("water", water, ControlTier.More, "extras", "none", "pond", "rainGardens", "both")).Tip();
            Shared(c.Slider("lotMedian", 2.5, 2.5, 6, 0.5, "m", ControlTier.More, "parking")).Tip();
        }

        /// <summary>
        /// Few, cheap features: the composition itself is the type. (s25) The "flatten terrain" feature is gone: one plane
        /// over lanes, lots and plots on a slope dug the plots in and banked the lanes; the type grades itself now
        /// (MallGrading, the lots' LotGrading). Decal sprinkles are off by default: the lots' own dressing (s21) is enough,
        /// the cracks theme put dark blobs all over the lot (playtest).
        /// </summary>
        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("decals", ("theme", "cracks"), ("ground", "paved"), ("density", 0.4)),
        };

        protected override string[] DefaultFeatures => Array.Empty<string>();

        /// <summary>Zone type, plot depth and the parking options shared by every commercial type.</summary>
        /// <param name="streetShops">(core) Default of "Shops facing the streets": off for the types built round their own lanes (their
        /// street rows are planted), on for the types whose shops stand on the streets by design (shops park, retail park).</param>
        protected void CommonControls(ControlSet c, string zone = ZoneKinds.CommercialLow, double depth = 24, string lotSize = "medium", bool lot = true, string lotWhen = "", double maxDepth = 40, bool green = true, bool streetShops = false)
        {
            if (UsesEntrances) ShopEntrances.Control(c);
            var z = EdgeZoningAddOn.ZoneKindControl("zoneKind", "shops", zone);
            c.List.Add(z);
            Shared(c.Slider("plotDepth", depth, 8, maxDepth, 8, "m", ControlTier.Essential, "shops")).Tip();
            // (core) Off: plots along the city streets are planted and every shop faces the site's own lanes and lot.
            Shared(c.Toggle("streetShops", streetShops, ControlTier.More, "shops")).Tip();
            if (lot)
            {
                if (lotSize != null) Shared(c.Choice("lotSize", lotSize, ControlTier.Essential, "parking", "small", "medium", "large")).Tip();
                Shared(c.Choice("stallAngle", "90", ControlTier.More, "parking", "90", "60", "45")).When(lotWhen);
                Shared(c.Choice("lotGreen", "medians", ControlTier.More, "parking", "none", "edges", "medians")).Tip().When(lotWhen);
                Shared(c.Slider("evShare", 4, 0, 30, 1, "%", ControlTier.More, "parking")).Tip().When(lotWhen);
                Shared(c.Toggle("lamps", true, ControlTier.More, "parking")).When(lotWhen);
            }
            // (s50) The park is not part of a shopping area: an optional extra, off unless the type has a park by design.
            if (green) Shared(c.Toggle("extraGreen", false, ControlTier.More, "layout")).Tip();
            c.Fixed("zoneKind");
            c.Fixed("plotDepth");
        }

        /// <summary>(s32) Shops on the streets round the site (a row facing each street) and their zone ("big buildings").</summary>
        protected static void OuterRowControls(ControlSet c, string zone, bool on = true)
        {
            Shared(c.Toggle("outerRow", on, ControlTier.Essential, "shops")).Tip();
            var z = EdgeZoningAddOn.ZoneKindControl("outerZone", "shops", zone);
            z.LabelKey = K + "outerZone";
            z.TooltipKey = K + "outerZone.Tooltip";
            z.VisibleWhen = "outerRow=true";
            c.List.Add(z);
            c.Fixed("outerZone");
        }

        /// <summary>Points a control at the shared Placemaker.Commercial.* keys (options too).</summary>
        protected static ControlSchema Shared(ControlSchema c)
        {
            c.LabelKey = K + c.Id;
            foreach (var o in c.Options) o.LabelKey = K + c.Id + "." + o.Value;
            return c;
        }

        protected static ControlSchema Tipped(ControlSchema c) { c.TooltipKey = c.LabelKey + ".Tooltip"; return c; }

        protected override void PlanCore(LayerContext ctx)
        {
            var mall = NewMall(ctx);
            var runs = mall.Runs();
            if (UsesEntrances)
            {
                // (rf-shops3) Planned from the player's entrances: the composition lays out its own network (whatever the site
                // touches: a circle inside a block too) and joins the streets only at the entrances.
                mall.Gates = ShopEntrances.Resolve(ctx);
                if (ComposeFromEntrances(ctx, mall, runs))
                {
                    ctx.Check();
                    LotFirst(ctx, mall, runs);
                    ctx.Check();
                    mall.PlantStreetPlots(ctx.B("streetShops"));
                    // (2026-10-28) Street plots that stay (a small strip mall's shops on the street) become their real cells, as in
                    // the street compositions below: unsnapped, the game zoned none of them ("cannot zone (land blocked)").
                    mall.SnapStreetPlotsToCells();
                    FillRest(ctx, mall);
                    FinishDesign(ctx, mall);
                    FinishFront(ctx, mall);
                    mall.Finish(ZoneKinds.PrefabId(ctx.S("zoneKind")));
                    CommonStats(ctx);
                    return;
                }
                // (shops4) The layout said why it does not fit (an error the panel shows; Build waits): nothing else is tried.
                if (ctx.Plan.Issues.Exists(i => i.Key == NoRoomKey || i.Key == TooSteepKey)) return;
                // The site could not hold it: the type's older layout (its lanes join the streets themselves) when a street runs
                // along it; a shops type never quietly becomes a park (the lead: a user drawing 8 m off a road got one).
                if (runs.Count == 0)
                {
                    ctx.Issue(IssueSeverity.Warning, NoLayoutKey);
                    return;
                }
                mall = NewMall(ctx);
                ctx.Issue(IssueSeverity.Info, FallbackKey);
            }
            if (runs.Count == 0)
            {
                // Nothing to zone from: a pocket park keeps the site useful until a street is built.
                ctx.Issue(IssueSeverity.Warning, NoStreetKey);
                mall.Fill(ctx.LayerRegion.Ring, "park", "pocket", null, 0x51u);
                return;
            }
            ctx.Check();
            // (shops) The composition is laid out from one main frontage. On a curved or cut-up block the longest straight run may
            // not hold it (the mall district, mall park and retail park fell back to a row of street plots and a sea of lots): the
            // next longest runs are tried as the main frontage, from a clean plan each time; when none holds it, the longest
            // run's fallback is kept as before.
            var mark = PlanMark.Of(ctx.Plan);
            var all = runs;
            int tries = math.min(all.Count, MainRunTries);
            bool held = false;
            for (int k = 0; k < tries && !held; k++)
            {
                if (k > 0)
                {
                    if (all[k].Length < MinMainRun) continue;
                    mark.Restore(ctx.Plan);
                    mall = NewMall(ctx);
                    runs = new List<StreetShops.Run> { all[k] };
                    for (int i = 0; i < all.Count; i++) if (i != k) runs.Add(all[i]);
                }
                Compose(ctx, mall, runs);
                if (runs[0].Length < CurvedFrontage || mall.TakenArea() < mall.Region.Area * ComposeMoreBelow) ComposeMore(ctx, mall, runs);
                ctx.Check();
                held = mall.Lanes.Count > 0 && !mark.IssuedSince(ctx.Plan, FallbackKey);
            }
            if (!held && !ReferenceEquals(runs, all))
            {
                // None held it: back to the longest run's composition (what the type always did).
                mark.Restore(ctx.Plan);
                mall = NewMall(ctx);
                runs = all;
                Compose(ctx, mall, runs);
                if (runs[0].Length < CurvedFrontage || mall.TakenArea() < mall.Region.Area * ComposeMoreBelow) ComposeMore(ctx, mall, runs);
                ctx.Check();
            }
            mall.MainDir = runs[0].Dir;
            // (s50) Lot first: the shop rows, access alleys and lots the composition did not reach.
            LotFirst(ctx, mall, runs);
            ctx.Check();
            // (core) Shops face the build's lanes: plots facing only a city street are planted unless the player asks for street shops.
            mall.PlantStreetPlots(ctx.B("streetShops"));
            // (s40) Zoning first: street plots become their real cells before the rest is planned around them.
            mall.SnapStreetPlotsToCells();
            FillRest(ctx, mall);
            FinishDesign(ctx, mall);
            mall.Finish(ZoneKinds.PrefabId(ctx.S("zoneKind")));
            CommonStats(ctx);
        }

        /// <summary>(strip malls, 2026-10-28) The type's own last touches on an entrance composition (the strip mall's street front).</summary>
        protected virtual void FinishFront(LayerContext ctx, Mall mall) { }

        /// <summary>(rf-shops3) The type's entrance composition (UsesEntrances): true when it laid out the site; false falls back to Compose.</summary>
        protected virtual bool ComposeFromEntrances(LayerContext ctx, Mall mall, List<StreetShops.Run> runs) => false;

        /// <summary>(rf-shops3) The plots' ground and planting, last (the plots are final): building sites, plot trees; the extras' counts.</summary>
        private static void FinishDesign(LayerContext ctx, Mall mall)
        {
            mall.DressGates();
            mall.ServiceYards();
            mall.PaintSites();
            mall.PlantPlots();
            if (mall.BikeRackCount > 0) ctx.Plan.Facts["bikeRacks"] = mall.BikeRackCount;
            if (mall.FoodCartCount > 0) ctx.Plan.Facts["foodCarts"] = mall.FoodCartCount;
        }

        /// <summary>
        /// (shops) Most of the site all lots together may take (the user: "the parking lots look weird"; lot first filled every
        /// leftover with asphalt). The composition's own lots always go in; leftover lots stop at this share and the rest is planted.
        /// </summary>
        protected virtual float LotShare => 0.4f;

        private Mall NewMall(LayerContext ctx)
        {
            var m = new Mall(ctx) { GradeTolerance = GradeTolerance };
            m.LotBudget = m.Region.Area * LotShare;
            return m;
        }

        /// <summary>(shops) How many frontage runs are tried as the main one, and the shortest run tried (m).</summary>
        private const int MainRunTries = 4;
        private const float MinMainRun = 60f;

        /// <summary>(shops) The lengths of a plan's lists (and a copy of its facts), so a composition that fell back can be taken out again.</summary>
        protected sealed class PlanMark
        {
            private int _regions, _surfaces, _nets, _objects, _areas, _terraform, _paint, _zoning, _stats, _issues;
            private Dictionary<string, double> _facts;
            public static PlanMark Of(AreaPlan p) => new PlanMark
            {
                _regions = p.Regions.Count, _surfaces = p.Surfaces.Count, _nets = p.Nets.Count, _objects = p.Objects.Count, _areas = p.Areas.Count,
                _terraform = p.Terraform.Count, _paint = p.Paint.Count, _zoning = p.Zoning.Count, _stats = p.Stats.Count, _issues = p.Issues.Count,
                _facts = new Dictionary<string, double>(p.Facts, StringComparer.Ordinal),
            };
            private static void Cut<T>(List<T> l, int n) { if (l.Count > n) l.RemoveRange(n, l.Count - n); }
            public void Restore(AreaPlan p)
            {
                Cut(p.Regions, _regions); Cut(p.Surfaces, _surfaces); Cut(p.Nets, _nets); Cut(p.Objects, _objects); Cut(p.Areas, _areas);
                Cut(p.Terraform, _terraform); Cut(p.Paint, _paint); Cut(p.Zoning, _zoning); Cut(p.Stats, _stats); Cut(p.Issues, _issues);
                p.Facts.Clear();
                foreach (var kv in _facts) p.Facts[kv.Key] = kv.Value;
            }
            public bool IssuedSince(AreaPlan p, string key)
            {
                for (int i = _issues; i < p.Issues.Count; i++) if (p.Issues[i].Key == key) return true;
                return false;
            }
        }

        /// <summary>(s32) Smallest height change the site grading emits (Mall.GradeTolerance); the s32 types grade finer.</summary>
        protected virtual float GradeTolerance => Sculpt.MinChange;

        protected abstract void Compose(LayerContext ctx, Mall mall, List<StreetShops.Run> runs);

        /// <summary>
        /// (s46) The composition only works one frontage (the longest). When most of the site is still free afterwards (a
        /// block with curved or angled streets, whose straight pieces are short), the same composition is tried on the
        /// other frontages, longest first — whatever fits (lot, lane and shops, or at least the shop row) — so the site is
        /// not left over. A piece that finds its land taken is skipped (every kit call checks the land it needs).
        /// </summary>
        private void ComposeMore(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            float total = mall.Region.Area;
            // (shops) The runs with the most free land straight behind them first (the back street of a strip mall before the
            // short side streets beside it); a run with less than MoreReach of free land behind it is not composed.
            var cand = new List<(int k, float free)>();
            for (int k = 1; k < runs.Count; k++)
            {
                var rk = runs[k];
                float mk = 0.5f * (rk.From + rk.To);
                cand.Add((k, mall.FreeReachAt(rk, mk)));
            }
            // The street opposite the main one first (a second strip back to back with the first), then the most free land.
            float Rank((int k, float free) c) => c.free + (math.dot(runs[c.k].In, runs[0].In) < -0.7f ? 1000f : 0f);
            cand.Sort((a, b) => Rank(b).CompareTo(Rank(a)));
            foreach (var (k, free) in cand)
            {
                ctx.Check();
                if (mall.TakenArea() > total * LeftoverMax) break;
                if (free < MoreReach) continue;
                var r = runs[k];
                if (r.Length < 3 * Mall.Cell + 2f) continue;
                // The frontage must still be free where the middle of the run starts.
                float m = 0.5f * (r.From + r.To);
                if (!mall.Fits(Mall.Rect(r, m - Mall.Cell, m + Mall.Cell, 0f, Mall.Cell))) continue;
                var order = new List<StreetShops.Run> { r };
                for (int i = 0; i < runs.Count; i++) if (i != k) order.Add(runs[i]);
                mall.Extra = true;
                try { Compose(ctx, mall, order); }
                finally { mall.Extra = false; }
            }
        }

        /// <summary>(shops) The other frontages are composed when less than this share of the site is taken after the main one.</summary>
        protected virtual float ComposeMoreBelow => 0.25f;
        /// <summary>(shops) Least free land (m) behind a run's middle for another composition there.</summary>
        protected virtual float MoreReach => 0f;

        /// <summary>Compose another frontage while less than this share of the site is taken.</summary>
        private const float LeftoverMax = 0.6f;
        /// <summary>A longest straight frontage shorter than this (m) is a curved or cut-up block: every frontage gets its composition.</summary>
        private const float CurvedFrontage = 110f;

        // ───────────── (s50) lot first ─────────────

        /// <summary>The planted strip between a street and the first plots of an access alley leaving it (m).</summary>
        public const float AlleyForecourt = 4f;
        /// <summary>Shortest free stretch of street frontage that gets plots in the lot-first pass (m).</summary>
        private const float StreetRowMin = 48f;

        /// <summary>Does this type put a park in what is left over (by design, or the "Green" extra)?</summary>
        protected virtual bool KeepsPark(LayerContext ctx) => ctx.B("extraGreen");

        /// <summary>Does the lot-first pass put plot rows on the street frontage the composition left free? (The retail park keeps its pads and boxes as designed.)</summary>
        protected virtual bool RowsOnFreeFrontage => true;

        /// <summary>
        /// (s50) The lot is the base of a commercial site: every street frontage the composition left free gets shop rows
        /// round an access alley (shops on both sides, running in from the street to the lot behind), the free frontage
        /// between the alleys gets plots, and the land behind is planned as a parking lot of its own shape, entered from the
        /// alley's end (or a street). What no lot can use stays for <see cref="FillRest"/>.
        /// </summary>
        protected virtual void LotFirst(LayerContext ctx, Mall mall, List<StreetShops.Run> runs)
        {
            // (rf-shops3) Planned from entrances: no access alley or street row joins a street by itself (FillRest plans the lots).
            if (mall.EntranceMode) return;
            int cells = Cells(ctx);
            var alleys = AccessAlleys(ctx, mall, runs, cells);
            ctx.Check();
            if (RowsOnFreeFrontage) mall.StreetRows(runs, cells, null, 3, 2, StreetRowMin);
            // A type with a park keeps its biggest leftover for it: lots come after the park is chosen (FillRest).
            if (KeepsPark(ctx)) { DropDeadAlleys(mall, alleys, runs, cells); return; }
            RestLots(ctx, mall, 0xB00u, 400f, false, true);
            if (DropDeadAlleys(mall, alleys, runs, cells)) RestLots(ctx, mall, 0xB40u, 400f, false, true);
        }

        /// <summary>An access alley and the plots along it (so it can be taken back).</summary>
        protected sealed class Alley
        {
            public Mall.Lane Lane;
            public List<float2[]> Rows = new List<float2[]>();
        }

        /// <summary>
        /// A car lane never dead-ends: an access alley whose end no lot joined (no lot behind it could be planned, or a stall was in
        /// the way of its stub) is taken back with its plots, and the frontage it held goes to street plots instead.
        /// </summary>
        private static bool DropDeadAlleys(Mall mall, List<Alley> alleys, List<StreetShops.Run> runs, int cells)
        {
            bool dropped = false;
            foreach (var a in alleys)
            {
                var end = a.Lane.Points[a.Lane.Points.Count - 1];
                bool joined = false;
                foreach (var n in mall.Ctx.Plan.Nets)
                    if (math.distance(n.A.xz, end) < 0.3f || math.distance(n.D.xz, end) < 0.3f) { joined = true; break; }
                if (joined) continue;
                mall.Remove(a.Lane);
                foreach (var r in a.Rows) { mall.Shops.Remove(r); mall.Taken.Remove(r); mall.ShopZone.Remove(r); }
                dropped = true;
            }
            if (dropped) mall.StreetRows(runs, cells, null, 3, 2, StreetRowMin);
            return dropped;
        }

        /// <summary>
        /// Access alleys: on every run, one every ~100 m, a short lane straight in from the street with a row of plots on each
        /// side, ending where the land behind is deep enough for a lot (the lot is planned later from the leftover piece).
        /// </summary>
        protected List<Alley> AccessAlleys(LayerContext ctx, Mall mall, List<StreetShops.Run> runs, int cells)
        {
            var list = new List<Alley>();
            var (asset, h) = Mall.ShopLane(ctx);
            float d = cells * Mall.Cell;
            foreach (var r in runs)
            {
                ctx.Check();
                float from = r.From + (r.StartCorner ? Mall.CornerClear : 2f), to = r.To - (r.EndCorner ? Mall.CornerClear : 2f);
                float span = to - from;
                float need = 2f * (h + d) + 2f * Mall.Cell;
                if (span < need + 8f) continue;
                int n = span > 280f ? 3 : span > 150f ? 2 : 1;
                for (int k = 0; k < n; k++)
                {
                    float m = from + span * (k + 0.5f) / n;
                    float reach = mall.ReachAt(r, m);
                    // The alley needs the rows' depth and a lot (one aisle at least) behind it.
                    float lane = math.min(math.floor((reach - Mall.LotDepth(1) - 2f) / Mall.Cell) * Mall.Cell, 6f * Mall.Cell);
                    if (lane < d + Mall.Cell) continue;
                    var foot = Mall.Rect(r, m - h - d - 1f, m + h + d + 1f, 0f, lane);
                    if (!mall.Fits(foot)) continue;
                    var al = mall.AddLane(r.At(m, 0f), -r.In, new[] { r.At(m, lane) }, null, default, NetRole.Alley, asset, h);
                    if (al == null) continue;
                    var entry = new Alley { Lane = al };
                    int before = mall.Shops.Count;
                    mall.LaneRows(al, cells, null, 0, mall.GateTrim(al, true, AlleyForecourt), 0f, 3, 2);
                    for (int i = before; i < mall.Shops.Count; i++) entry.Rows.Add(mall.Shops[i]);
                    list.Add(entry);
                }
            }
            return list;
        }

        /// <summary>
        /// Lots on what is left: every piece of at least <paramref name="minArea"/> that a street or lane can reach becomes a
        /// parking lot of its own shape (largest first; each lot changes what is left, so the pieces are found again).
        /// <paramref name="keepLargest"/>: the largest piece stays free (for a park).
        /// </summary>
        protected static void RestLots(LayerContext ctx, Mall mall, uint salt, float minArea = 500f, bool keepLargest = false, bool openAccess = false)
        {
            var failed = new HashSet<long>();
            for (int pass = 0; pass < 24; pass++)
            {
                ctx.Check();
                bool any = false;
                var pieces = mall.RestPieces(minArea);
                int skip = keepLargest ? 1 : 0;
                for (int i = skip; i < pieces.Count; i++)
                {
                    var piece = pieces[i];
                    var c = Poly.Centroid(piece);
                    long key = ((long)(Poly.Area(piece) * 10f) * 73856093L) ^ ((long)(c.x * 100f) * 19349663L) ^ ((long)(c.y * 100f) * 83492791L);
                    if (failed.Contains(key)) continue;
                    // (shops) The parking budget, shared evenly by the big leftovers: a piece bigger than its share keeps a lot of
                    // that size by its way in (the street or lane it is entered from) and the rest is planted, so two sides of a
                    // composition get the same lot instead of one side all asphalt and the other all lawn.
                    float left = mall.LotBudget - mall.LotArea();
                    if (left < 600f) { failed.Add(key); continue; }
                    int big = 0; foreach (var q in pieces) if (Poly.Area(q) >= 1200f) big++;
                    float share = left / math.max(1, big);
                    if (0.7f * Poly.Area(piece) > share)
                    {
                        var near = mall.NearEntrance(piece, share / 0.7f);
                        if (near == null) { failed.Add(key); continue; }
                        piece = near;
                    }
                    // Shut in by plots: give back the smallest plot that touches it and a street.
                    if (openAccess && Poly.Area(piece) >= 1200f) { var parts = Mall.Smooth(piece); var basis = parts.Count > 0 ? parts[0] : piece; if (Poly.Area(basis) >= 1200f && mall.Entries(basis).Count == 0 && mall.OpenAccess(basis)) { any = true; break; } }
                    if (mall.PolyLot(piece, Mall.Parking(ctx), salt + (uint)pass * 0x10u + (uint)i)) { any = true; break; }
                    // (rf-shops3) An L or a T of land (the inside of a ring round an L) holds no tidy lot: cut it at its inner corner, square
                    // to the main street, and try the parts (the rest is a new piece on the next pass).
                    if (Poly.Area(piece) >= 1500f)
                    {
                        bool split = false;
                        foreach (var part in mall.SplitAtReflex(piece))
                            if (mall.PolyLot(part, Mall.Parking(ctx), salt + 0x7u + (uint)pass * 0x10u + (uint)i)) { split = true; break; }
                        if (split) { any = true; break; }
                    }
                    failed.Add(key);
                    // (rf-shops3) What was left unparked, for the run logs (a corner that stayed lawn: why).
                    ctx.Plan.Facts["restLotFail"] = (ctx.Plan.Facts.TryGetValue("restLotFail", out var rf) ? rf : 0) + 1;
                    ctx.Plan.Facts["restLotFailM2"] = (ctx.Plan.Facts.TryGetValue("restLotFailM2", out var rm) ? rm : 0) + math.round(Poly.Area(piece));
                    if (mall.Entries(piece).Count == 0) ctx.Plan.Facts["restLotNoEntry"] = (ctx.Plan.Facts.TryGetValue("restLotNoEntry", out var rn) ? rn : 0) + 1;
                }
                if (!any) break;
            }
        }

        /// <summary>
        /// (s50) A park by design may take at most <paramref name="maxShare"/> of the site: when the largest leftover is bigger, its
        /// middle (the leftover shrunk until it fits the share) is held for the park and the band round it goes to lots and
        /// shops. Returns the held ring (give it to <see cref="Mall.Release"/> before planting the park), or null.
        /// </summary>
        protected static float2[] HoldParkCore(Mall mall, float maxShare)
        {
            var pieces = mall.RestPieces(500f);
            if (pieces.Count == 0) return null;
            var big = pieces[0];
            float cap = mall.Region.Area * maxShare;
            if (Poly.Area(big) <= cap) return null;
            for (float w = 8f; w <= 64f; w += 4f)
            {
                float2[] best = null; float bestA = 0f;
                try
                {
                    foreach (var c in Poly.Offset(big, w, OffsetJoin.Miter))
                        if (c?.Outer != null && c.Outer.Length >= 3 && Poly.Area(c.Outer) > bestA) { bestA = Poly.Area(c.Outer); best = c.Outer; }
                }
                catch (System.Exception) { }
                if (best == null) return null;
                if (bestA <= cap) return mall.Hold(best);
            }
            return null;
        }

        /// <summary>The lots' planted edges: what no lot could use is lawn and trees, never paths (a buffer, not a park).</summary>
        protected static void LotEdges(LayerContext ctx, Mall mall, uint salt = 0x980u)
        {
            // (rf-shops3) The kept land (frontage, spine, plaza, pond) is dressed first, then every leftover is designed planting.
            mall.DressKept();
            uint k = 0;
            foreach (var piece in mall.Rest(60f))
            {
                ctx.Check();
                mall.Landscape(piece, salt + k++);
                ctx.Plan.Facts["lotEdges"] = (ctx.Plan.Facts.TryGetValue("lotEdges", out var le) ? le : 0.0) + Poly.Area(piece);
            }
        }

        /// <summary>
        /// (s50) What is left after the lots: more lot, then planted edges. A park only where the type has one by design
        /// (they override this) or the "Green" extra asks for one (the largest wide leftover piece, like the s32 types').
        /// </summary>
        protected virtual void FillRest(LayerContext ctx, Mall mall)
        {
            bool park = KeepsPark(ctx);
            RestLots(ctx, mall, 0xB80u, 220f, park);
            if (park) GreenRest(ctx, mall, "pocket", false, 0x900u);
            LotEdges(ctx, mall);
        }


        /// <summary>
        /// (s32) What is left, for the s32 types: wide parts become parks whose walks open onto the lanes beside them (the
        /// first, largest one in <paramref name="mainStyle"/>, with a playground when asked), thin arms planted verges that
        /// keep clear of every net (Mall.Green) — gateway strips beside lane mouths, the gaps behind rows.
        /// </summary>
        protected static void GreenRest(LayerContext ctx, Mall mall, string mainStyle = "pocket", bool playground = false, uint salt = 0x900u)
        {
            uint k = 0;
            bool first = true;
            foreach (var (piece, strip) in mall.RestSplit(150f))
            {
                ctx.Check();
                bool wide = !strip && !mall.IsStrip(piece);
                if (wide && mall.FillOpen(piece, "park", first ? mainStyle : "pocket", mall.LaneDoors(piece), Doors(mall), salt + k))
                {
                    if (first && playground) mall.AddOn(piece, "playground", salt + 0x40u + k, b => b.Values["size"] = "small");
                    first = false;
                }
                else mall.Green(piece, salt + 0x80u + k);
                k++;
            }
        }

        /// <summary>Lane ends and lot corners a green piece should open towards.</summary>
        protected static List<float2> Doors(Mall mall)
        {
            var list = new List<float2>(mall.Doors);
            foreach (var l in mall.Lanes)
                if (l.EndAttach < 0 && l.Points.Count > 1) list.Add(l.Points[l.Points.Count - 1]);
            return list;
        }


        protected static int Cells(LayerContext ctx) => Mall.Cells(ctx.Raw("plotDepth"));

        protected static void LotEntrances(List<(float2, string)> list, StreetShops.Run r, float s0, float s1, float t, string kind)
        {
            float w = s1 - s0;
            if (w > 90f) { list.Add((r.At(s0 + w * 0.25f, t), kind)); list.Add((r.At(s0 + w * 0.75f, t), kind)); }
            else list.Add((r.At(0.5f * (s0 + s1), t), kind));
        }
    }
}
