// Memorial park: a solemn, axial composition.
//
//   * the axis — the area's long chord; the procession starts at the end nearest the main entrance
//     and leads to the focal monument (at the far end, or in the centre with the axis continuing);
//   * the focal monument — on a paved forecourt (round, or square for war memorials and monument
//     squares), optionally raised on a terraformed mound blended back to the ground; flags behind it,
//     plaques / wreaths in an arc before it, uplights round it, a flower ring (poppies) outside;
//   * the reflecting pool — on the axis between procession and monument; the walk splits into two
//     paths either side of the pool and rejoins at cross paths (shared nodes, so CS2 joins them);
//   * allées — tree rows either side of the procession, placed in symmetric pairs only;
//   * hedged rooms — quiet garden rooms in rows flanking the allées, each opening towards the axis
//     with a short spur path: benches, flowers, a sculpture or a single specimen tree;
//   * solemn lighting — low bollard lights, lantern pairs, or uplights only;
//   * sculpture park variant — no axis: a meandering trail with sculptures on paved pads beside it,
//     each set against a hedge backdrop, and a large sculpture as the focal piece.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns.Monuments;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Types.AdventurePlayground;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.MemorialPark
{
    [AreaGenerator(218, Group = "parks")]
    public sealed class MemorialParkLayer : LeisureLayer
    {
        public const string K = "Placemaker.Memorial.";
        public static readonly string[] Styles = { "garden", "square", "war", "reflection", "sculpture" };

        protected override string Area => "Memorial";
        public override string Id => "memorialPark";
        public override string Icon => "Media/Game/Icons/Unique.svg";
        public override float MinArea => 1200f;
        protected override int WantedEntrances(LayerRegion region) => region.Area < 8000f ? 1 : 2;

        protected override List<FeatureInfo> BuildFeatures()
        {
            var flags = Feature(FlagCircleAddOn.LayerId);
            flags.LabelKey = K + "Feature.flagCircle";
            flags.TooltipKey = K + "Feature.flagCircle.Tooltip";
            var trees = Feature("bigtrees", ("density", 1.5));
            trees.LabelKey = K + "Feature.bigtrees";
            var beds = Feature("flowerbeds", ("density", 6.0), ("assets", (string[])ParkArt.Flowerbeds.Clone()));
            beds.LabelKey = K + "Feature.flowerbeds";
            return MonumentFeatures.With(new List<FeatureInfo>
            {
                Feature("restrooms"), flags, trees, beds, StreetShopsFeature(Area),
                Feature("decals", ("theme", "leaves"), ("density", 0.3)), Feature("terrain", ("shape", "flatten")),
            });
        }

        protected override void DefineControls(ControlSet c)
        {
            // Composition.
            c.Choice("style", "garden", ControlTier.Essential, "composition", Styles).Tip();
            c.Choice("focal", "end", ControlTier.Essential, "composition", "end", "centre").Tip();
            c.Slider("processionWidth", 6, 3, 12, 1, "m", ControlTier.Essential, "composition").Tip();
            c.Toggle("crossAxis", false, ControlTier.More, "composition").Tip();
            // FEEDBACK-4: walks are path nets; a paved strip under the procession (and pads under sculptures) only on request.
            c.Toggle("paving", false, ControlTier.More, "composition").Tip();
            // Monument.
            c.Slider("forecourt", 10, 5, 24, 1, "m", ControlTier.Essential, "monument").Layout(5).Tip();
            c.Toggle("raised", false, ControlTier.Essential, "monument").Tip();
            c.Slider("raiseHeight", 1.5, 0.5, 3, 0.5, "m", ControlTier.More, "monument").When("raised=true");
            c.Slider("plaques", 4, 0, 16, 1, "count", ControlTier.More, "monument").Tip();
            c.Slider("flags", 0, 0, 12, 1, "count", ControlTier.More, "monument");
            c.Slider("sculptures", 0, 0, 30, 1, "count", ControlTier.More, "monument").Tip();
            c.Asset("monumentAsset", AssetCategories.MemorialMonument, ControlTier.More, "monument");
            c.Slider("monumentScale", 220, 100, 400, 10, "%", ControlTier.More, "monument").Tip();
            c.AssetMulti("sculptureAssets", AssetCategories.MemorialSculpture, ControlTier.More, "monument");
            // Water.
            c.Toggle("pool", true, ControlTier.Essential, "water").Tip();
            c.Slider("poolLength", 50, 20, 80, 5, "%", ControlTier.More, "water").When("pool=true").Tip();
            c.Slider("poolWidth", 5, 2, 12, 0.5, "m", ControlTier.More, "water").When("pool=true");
            c.Toggle("realWater", false, ControlTier.More, "water").When("pool=true").Tip();
            c.Asset("poolAsset", AssetCategories.SurfaceWater, ControlTier.More, "water").When("pool=true");
            // Hedged rooms.
            c.Toggle("rooms", true, ControlTier.Essential, "rooms").Tip();
            c.Slider("roomSize", 14, 8, 24, 1, "m", ControlTier.More, "rooms").Layout(9).When("rooms=true");
            c.Choice("roomContent", "mixed", ControlTier.More, "rooms", "mixed", "benches", "flowers", "sculpture", "tree").When("rooms=true").Tip();
            c.Asset("hedgeAsset", AssetCategories.Hedge, ControlTier.More, "rooms");
            // Planting.
            c.Toggle("allees", true, ControlTier.Essential, "planting").Tip();
            c.Slider("treeSpacing", 9, 6, 16, 1, "m", ControlTier.More, "planting").Layout(6).When("allees=true");
            c.Slider("lawnTrees", 20, 0, 80, 5, "perHa", ControlTier.More, "planting").Tip();
            c.Toggle("flowerRing", true, ControlTier.More, "planting").Tip();
            c.Toggle("borders", false, ControlTier.More, "planting").Tip();
            c.Toggle("bosque", false, ControlTier.More, "planting").Tip();
            // makeover (2026-10-29): the side lawns get places of their own before any grove (ParkMoods): a wildflower meadow of
            // remembrance, a ring of standing stones, a quiet copse, a knoll of boulders. Off the axis, so the axis stays solemn.
            c.Toggle("moods", true, ControlTier.Essential, "planting").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");
            // Lighting.
            c.Choice("lighting", "bollards", ControlTier.Essential, "lighting", "bollards", "lanterns", "uplights", "none").Tip();
            c.Slider("lampSpacing", 14, 6, 30, 1, "m", ControlTier.More, "lighting").Layout(5);
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "lighting");
            // Boundary.
            c.Choice("enclosure", "hedge", ControlTier.More, "boundary", "hedge", "fence", "none").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("enclosure=fence");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "boundary");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            c.RandomRange("forecourt", 7, 14);
            c.RandomRange("lawnTrees", 5, 40);
            c.Fixed("realWater");
            c.Fixed("monumentScale");
        }

        protected override void DefinePresets()
        {
            // rf-parks: every axial style keeps its lawn open (few lawn trees, so the axis, the rooms and the monument read) and has a
            // scaled focal monument; each gets its own signature: flower borders, poppies and flags, a fountain rill, a sculpture trail.
            Preset("garden", ("style", "garden"), ("focal", "end"), ("pool", false), ("rooms", true), ("roomContent", "flowers"), ("lighting", "bollards"),
                ("flowerRing", true), ("borders", true), ("plaques", 4.0), ("lawnTrees", 0.0), ("monumentScale", 220.0), ("bosque", true));
            Preset("square", ("style", "square"), ("focal", "centre"), ("crossAxis", true), ("paving", true), ("forecourt", 16.0), ("pool", false), ("rooms", false),
                ("lighting", "lanterns"), ("enclosure", "none"), ("processionWidth", 10.0), ("lawnTrees", 0.0), ("flowerRing", false));
            Preset("war", ("style", "war"), ("focal", "end"), ("raised", true), ("raiseHeight", 2.0), ("plaques", 12.0), ("flags", 10.0), ("pool", true),
                ("poolLength", 45.0), ("rooms", true), ("roomContent", "benches"), ("lighting", "uplights"), ("flowerRing", true), ("borders", true), ("enclosure", "hedge"),
                ("lawnTrees", 0.0), ("monumentScale", 350.0), ("bosque", true));
            Preset("reflection", ("style", "reflection"), ("focal", "end"), ("pool", true), ("poolLength", 70.0), ("poolWidth", 7.0), ("rooms", true),
                ("roomContent", "tree"), ("lighting", "bollards"), ("plaques", 0.0), ("allees", true), ("treeSpacing", 8.0), ("lawnTrees", 0.0), ("monumentScale", 260.0), ("bosque", true));
            Preset("sculpture", ("style", "sculpture"), ("sculptures", 14.0), ("pool", true), ("poolLength", 30.0), ("rooms", true), ("roomContent", "sculpture"),
                ("allees", false), ("lighting", "uplights"), ("lawnTrees", 25.0), ("plaques", 0.0), ("enclosure", "fence"), ("monumentScale", 220.0));
        }

        // ───────────── planning ─────────────

        /// <summary>A straight path course as explicit nodes; spurs insert junction nodes so CS2 joins them.</summary>
        private sealed class Line
        {
            public readonly List<float2> Nodes = new List<float2>();
            public NetRole Role;
            public bool IsSpur;

            public float Distance(float2 p, out int seg, out float2 q)
            {
                float best = float.MaxValue; seg = 0; q = Nodes[0];
                for (int i = 0; i + 1 < Nodes.Count; i++)
                {
                    var c = SegmentIndex.ClosestPoint(p, Nodes[i], Nodes[i + 1]);
                    float d = math.distance(c, p);
                    if (d < best) { best = d; seg = i; q = c; }
                }
                return best;
            }

            /// <summary>Junction at the closest point (an existing node when one is within 1.5 m).</summary>
            public float2 Junction(float2 p)
            {
                Distance(p, out int seg, out var q);
                foreach (var n in Nodes) if (math.distance(n, q) < 1.5f) return n;
                Nodes.Insert(seg + 1, q);
                return q;
            }
        }

        private sealed class State
        {
            public LayerContext Ctx;
            public LayerRegion Region;
            public Occupancy Occ;
            public CategoryChains Chains;
            public readonly List<Line> Lines = new List<Line>();
            public AssetRef PathAsset;
            public float2 Monument;
            public float Forecourt;
            public int Rooms, Sculptures, Plaques, Flags;
            public List<Gate> Gates;
            public ParkSite MoodSite;
            public List<MoodPlace> Moods;
            public float2 VistaA, VistaB;
            public float Vista;
        }

        /// <summary>(makeover) Places with a mood in the ground the composition left, never on the vista along the axis.</summary>
        private static void PlanMoods(State st)
        {
            var ctx = st.Ctx;
            if (st.Moods != null || !ctx.B("moods")) return;
            st.MoodSite = new ParkSite { LayerRegion = st.Region, Occ = st.Occ, Seed = ctx.Seed, Cancel = ctx.Cancel, Gates = st.Gates };
            if (st.Vista > 0f) st.Occ.TakeCorridor(st.VistaA, st.VistaB, st.Vista + 2f, 7);   // the vista stays open
            var open = FreeSpace.Pieces(st.Region, p => st.Occ.Corridors.Overlaps(p, 0.75f) || st.Occ.Discs.Overlaps(p, 2f) || st.Occ.HitsPolygon(p, 1f), 2f, 80f, ctx.Cancel);   // the ground the groves, rooms and monument left
            st.Moods = ParkMoods.Plan(ctx, st.MoodSite, open, "memorial", true, 0.8f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var st = new State
            {
                Ctx = ctx, Region = region, Occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site), Chains = new CategoryChains(ctx),
                PathAsset = AssetMix.One(ctx.S("pathAsset")),
            };
            var gates = Gates(ctx);
            st.Gates = gates;
            Lawn.Cover(ctx, region.Ring, AssetMix.One("", ParkArt.Grass));
            bool trail = ctx.S("style") == "sculpture";
            float vista = 0f;
            float2 vistaA = default, vistaB = default;
            if (!trail && !Axial(st, gates, out vistaA, out vistaB, out vista))
            {
                ctx.Issue(IssueSeverity.Info, K + "Issue.NoAxis");
                trail = true;
            }
            if (trail) Trail(st, gates);
            PlanMoods(st);   // the axial styles planned theirs before the groves (Axial); the trail here
            ctx.Check();

            // Enclosure.
            string enclosure = ctx.S("enclosure");
            if (enclosure == "fence") Fence(ctx, gates, AssetMix.One(ctx.S("fenceAsset")));
            else if (enclosure == "hedge") HedgeBoundary(st, gates);

            // Lighting along every path.
            string lighting = ctx.S("lighting");
            float spacing = ctx.F("lampSpacing");
            if (lighting == "lanterns") PathLights(st, spacing, chainLow: false, pairs: true);
            else if (lighting == "bollards") PathLights(st, math.max(5f, spacing * 0.5f), chainLow: true, pairs: false);
            // FEEDBACK-4: seats and bins stand beside the path nets (never out on the lawn).
            if (!trail) ctx.Plan.Facts["pathBenches"] = PathBenches(ctx, st.Occ, 40f, AssetMix.One("", ParkArt.Benches), 0xBE7Au);
            ctx.Plan.Facts["pathBins"] = PathBins(ctx, st.Occ, 80f, 0xB1A0u);
            ctx.Check();

            // Lawn trees keep the vista along the axis open (areal count: 1/scale, see docs/proposals/size.md).
            var treeIds = Or(ctx.L("treeSpecies"), MemorialAssets.LawnTrees);
            float ha = region.Area / 10000f;
            int trees = math.min(3000, (int)(ha * ctx.F("lawnTrees") / ctx.Scale));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, st.Occ, region, trees, ObjectRole.Tree, treeIds, 2.4f, 22f, 0.45f, 0x7E30u,
                    p => vista > 0f && SegmentIndex.DistSq(p, vistaA, vistaB) < vista * vista, 2, 3);
            ctx.Check();

            foreach (var l in st.Lines)
                for (int i = 0; i + 1 < l.Nodes.Count; i++)
                    if (math.distance(l.Nodes[i], l.Nodes[i + 1]) > 0.5f) ctx.StraightNet(l.Nodes[i], l.Nodes[i + 1], l.Role, st.PathAsset);

            ParkMoods.Emit(ctx, st.MoodSite, st.Moods, false);
            EmitGreenRegions(ctx, st.Occ, 100f);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Rooms", st.Rooms, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Sculptures", st.Sculptures, Keys.UnitCount);
            ctx.Plan.Facts["rooms"] = st.Rooms;
            ctx.Plan.Facts["sculptures"] = st.Sculptures;
            ctx.Plan.Facts["plaques"] = st.Plaques;
            ctx.Plan.Facts["flags"] = st.Flags;
        }

        /// <summary>The player's choice, or the art-pass default when the control is on automatic.</summary>
        private static string[] Or(string[] chosen, string[] defaults) => chosen != null && chosen.Length > 0 ? chosen : defaults;

        /// <summary>
        /// (rf-parks) The category for a piece with curated vanilla defaults: the first category with items, else the chain's head.
        /// The curated pieces are named explicitly and the game resolves them by name whatever the category holds, so an empty
        /// category (a clean game has no memorial monument, sculpture or fountain items) no longer drops the focal piece.
        /// </summary>
        private static string CatOr(State st, bool chosen, params string[] chain) => chosen ? chain[0] : CategoryChains.Quiet(chain) ?? chain[0];

        private static float2 LongestEdge(LayerRegion region)
        {
            float best = 0f; var axis = new float2(1, 0);
            for (int i = 0; i < region.Ring.Length; i++)
            {
                var e = region.Ring[(i + 1) % region.Ring.Length] - region.Ring[i];
                float l = math.length(e);
                if (l > best) { best = l; axis = e / l; }
            }
            return axis;
        }

        private static bool LineOk(LayerRegion region, float2 a, float2 b, bool aOnEdge) =>
            math.distance(a, b) > 1f && (aOnEdge ? region.BoundaryDistance(a, 2f) < 1.5f || region.Contains(a) : region.ContainsDisc(a, 0.3f))
            && region.ContainsDisc(b, 0.3f) && !region.CrossesBoundary(a, b, aOnEdge);

        private static Line AddLine(State st, NetRole role, params float2[] nodes) => AddLine(st, role, false, nodes);

        private static Line AddLine(State st, NetRole role, bool firstOnEdge, params float2[] nodes)
        {
            for (int i = 0; i + 1 < nodes.Length; i++) if (!LineOk(st.Region, nodes[i], nodes[i + 1], firstOnEdge && i == 0)) return null;
            var l = new Line { Role = role };
            l.Nodes.AddRange(nodes);
            float hw = Composer.DefaultHalfWidth(role);
            for (int i = 0; i + 1 < nodes.Length; i++)
            {
                st.Occ.TakeCorridor(nodes[i], nodes[i + 1], hw, 100);
                st.Occ.Paths.Add(nodes[i], nodes[i + 1], hw, 100);
            }
            st.Lines.Add(l);
            return l;
        }

        /// <summary>Spur from p to the nearest line (junction node inserted); false when it would leave the area.</summary>
        private static bool Spur(State st, float2 p, NetRole role, float maxLength = 80f, bool onEdge = false, float2 outward = default)
        {
            Line best = null; float bestD = float.MaxValue;
            bool directed = math.lengthsq(outward) > 0.5f;
            foreach (var l in st.Lines)
            {
                if (l.IsSpur) continue;   // spurs join trunk paths only (never cut across a room)
                float d = l.Distance(p, out _, out var q);
                if (directed && d > 0.5f && math.dot(math.normalizesafe(q - p), outward) < 0.6f) continue;
                if (d < bestD && d <= maxLength && !st.Region.CrossesBoundary(p, q, true)) { bestD = d; best = l; }
            }
            if (best == null || bestD < 1f) return best != null;
            var j = best.Junction(p);
            var spur = AddLine(st, role, onEdge, p, j);
            if (spur != null) spur.IsSpur = true;
            return spur != null;
        }

        /// <summary>
        /// Entrance joined to the trunk paths straight in from the edge (GateApproach, s9): never a leg along
        /// the outline. Falls back to the old nearest-point spur only when no inward route exists.
        /// </summary>
        private static void GateSpur(State st, float2 gate)
        {
            var spine = new List<(float2 a, float2 b)>();
            var owners = new List<Line>();
            foreach (var l in st.Lines)
            {
                if (l.IsSpur) continue;
                for (int i = 0; i + 1 < l.Nodes.Count; i++) { spine.Add((l.Nodes[i], l.Nodes[i + 1])); owners.Add(l); }
            }
            var route = GateApproach.Route(st.Region, gate, spine, LeisureLayer.ApproachLength(st.Ctx));
            if (route == null) { Spur(st, gate, NetRole.Pathway, 400f, true, GateApproach.Inward(st.Region, gate)); return; }
            if (route.Count < 2) return;
            var last = route[route.Count - 1];
            Line host = null; float best = float.MaxValue;
            foreach (var l in owners) { float d = l.Distance(last, out _, out _); if (d < best) { best = d; host = l; } }
            if (host == null) return;
            route[route.Count - 1] = host.Junction(last);
            var spur = AddLine(st, NetRole.Pathway, true, route.ToArray());
            if (spur != null) spur.IsSpur = true;
        }

        // ───────────── axial composition ─────────────

        private bool Axial(State st, List<Gate> gates, out float2 vistaA, out float2 vistaB, out float vista)
        {
            var ctx = st.Ctx; var region = st.Region; var occ = st.Occ;
            vistaA = vistaB = default; vista = 0f;
            // s9: the procession aims at the first entrance (axis along its edge normal, shifted through it).
            LeisureKit.FrameToward(region, LeisureKit.Points(gates), out var centre, out var axis, out var side, out _, out _);
            if (!LeisureKit.Chord(region, centre, axis, 4f, out var a0, out var a1)) return false;
            // Art pass (a1): the procession comes straight in from the main entrance when that axis is nearly as
            // long — on a square site the principal axis is a diagonal, which put the monument in a corner.
            if (gates.Count > 0)
            {
                region.ClosestOnBoundary(gates[0].Position, out int gs, out float gt);
                region.PointAt(region.AlongOf(gs, gt), out _, out var inward, out _);
                if (LeisureKit.Chord(region, centre, inward, 4f, out var g0, out var g1) && math.distance(g0, g1) >= 0.6f * math.distance(a0, a1))
                { a0 = g0; a1 = g1; }
            }
            var gate0 = gates.Count > 0 ? gates[0].Position : a0;
            var start = math.distance(a0, gate0) <= math.distance(a1, gate0) ? a0 : a1;
            var far = math.all(start == a0) ? a1 : a0;
            var dir = math.normalizesafe(far - start);
            var nrm = new float2(-dir.y, dir.x);
            float chord = math.distance(start, far);
            string style = ctx.S("style");
            bool centreFocus = ctx.S("focal") == "centre" || style == "square";
            // A second entrance at the far end of the axis would be blocked by an end monument: it moves to the centre
            // and the axis runs on through to that entrance.
            for (int gi = 1; gi < gates.Count && !centreFocus; gi++)
                if (math.distance(gates[gi].Position, far) < ctx.F("forecourt") * 1.5f + 10f) centreFocus = true;
            bool squareCourt = style == "war" || style == "square";
            float fr = ctx.F("forecourt") * (style == "square" ? 1.4f : 1f);
            fr = math.min(fr, chord * (centreFocus ? 0.3f : 0.22f));
            if (fr < ctx.M(4f, 3f)) return false;
            var mon = centreFocus ? math.lerp(start, far, 0.5f) : far - dir * (fr + 2f);
            // Pull the monument inwards until the forecourt fits the outline and the free space.
            float courtReach = squareCourt ? fr * 1.42f : fr;
            for (int i = 0; i < 20 && !occ.Free(mon, courtReach, courtReach + 1.5f, 0.2f); i++)
            {
                if (i == 10) { fr *= 0.75f; courtReach = squareCourt ? fr * 1.42f : fr; }
                mon -= dir * 2f;
            }
            if (!occ.Free(mon, courtReach, courtReach + 1f, 0.2f) || math.dot(mon - start, dir) < fr + ctx.M(8f, 4f)) return false;
            st.Monument = mon; st.Forecourt = fr;

            if (ctx.B("raised"))
            {
                float h = Mounds.Raise(ctx, region, mon, courtReach + 4f + ctx.F("raiseHeight") * 3f, courtReach + 0.5f, ctx.F("raiseHeight"), 0.3f);
                if (h > 0f) ctx.Plan.Facts["raised"] = h;
            }
            var court = squareCourt ? LayerRegion.Rect(mon, dir, fr, fr) : LayerRegion.Circle(mon, fr, 28);
            ctx.Surface(court, SurfaceRole.Plaza, AssetMix.One("", ParkArt.Pavement), AssetCategories.SurfacePaving);
            occ.Take(mon, courtReach, 0);

            // Procession: start → forecourt rim, splitting round the pool.
            // Thin by default (FEEDBACK-4): a wide path only for a broad processional way (8 m+, e.g. the monument square).
            bool broad = ctx.F("processionWidth") >= 8f, paving = ctx.B("paving");
            float hwMain = broad ? Composer.DefaultHalfWidth(NetRole.PathwayWide) : Composer.DefaultHalfWidth(NetRole.Pathway);
            var mainRole = broad ? NetRole.PathwayWide : NetRole.Pathway;
            var rimIn = mon - dir * fr;
            float axisLen = math.distance(start, rimIn);
            float bandHalf = math.max(hwMain, (paving ? 1f : ctx.Scale) * ctx.F("processionWidth") * 0.5f);
            bool pool = false;
            OrientedRect poolRect = default;
            float walkOff = 0f;
            if (ctx.B("pool") && axisLen > ctx.M(26f, 16f))
            {
                float w = ctx.F("poolWidth");
                float len = (axisLen - 12f) * ctx.F("poolLength") / 100f;
                var mid = start + dir * (axisLen * 0.5f);
                for (int i = 0; i < 4 && !pool; i++, len *= 0.8f)
                {
                    if (len < ctx.M(8f, 6f)) break;
                    poolRect = new OrientedRect { Center = mid, Axis = dir, HalfLength = len * 0.5f + 1.2f, HalfWidth = w * 0.5f + 1.2f };
                    pool = LeisureKit.RectFits(region, occ, poolRect, ctx.M(4.5f, 2.5f));
                }
                if (!pool) ctx.Issue(IssueSeverity.Info, K + "Issue.NoPool");
            }
            if (pool)
            {
                walkOff = poolRect.HalfWidth + 0.6f + Composer.DefaultHalfWidth(NetRole.Pathway);
                float uA = axisLen * 0.5f - poolRect.HalfLength - 2.5f, uB = axisLen * 0.5f + poolRect.HalfLength + 2.5f;
                var A0 = start + dir * uA; var B0 = start + dir * uB;
                var Am = A0 - nrm * walkOff; var Ap = A0 + nrm * walkOff; var Bm = B0 - nrm * walkOff; var Bp = B0 + nrm * walkOff;
                bool ok = AddLine(st, mainRole, start, A0) != null;
                ok &= AddLine(st, NetRole.Pathway, Am, A0, Ap) != null;
                ok &= AddLine(st, NetRole.Pathway, Ap, Bp) != null;
                ok &= AddLine(st, NetRole.Pathway, Am, Bm) != null;
                ok &= AddLine(st, NetRole.Pathway, Bm, B0, Bp) != null;
                ok &= AddLine(st, mainRole, B0, rimIn) != null;
                if (!ok) ctx.Issue(IssueSeverity.Info, K + "Issue.AxisBroken");
                Pool(st, poolRect);
            }
            else
            {
                if (AddLine(st, mainRole, start, rimIn) == null) return false;
                if (ctx.F("processionWidth") >= 4f)
                {
                    // The walk is the path net; the paved strip under it is optional (FEEDBACK-4: no painted walks).
                    var band = LayerRegion.Rect(start + dir * (axisLen * 0.5f), dir, axisLen * 0.5f, bandHalf);
                    var clipped = LeisureKit.Intersect(region, band);
                    if (paving && clipped != null) ctx.Surface(clipped.Ring, SurfaceRole.Plaza, AssetMix.One("", ParkArt.Pavement), AssetCategories.SurfacePaving);
                    occ.TakeCorridor(start, rimIn, bandHalf, 100);
                }
            }
            // The axis continues beyond a central monument; a cross axis through it on request.
            var spans = new List<(float2 a, float2 b)> { (start, rimIn) };
            if (centreFocus)
            {
                var rimOut = mon + dir * fr;
                if (AddLine(st, mainRole, rimOut, far) != null) spans.Add((rimOut, far));
            }
            if (ctx.B("crossAxis") || style == "square")
                if (LeisureKit.Chord(region, mon, nrm, 4f, out var c0, out var c1))
                {
                    if (math.dot(c0 - mon, nrm) > 0f) { var t = c0; c0 = c1; c1 = t; }
                    var r0 = mon - nrm * fr; var r1 = mon + nrm * fr;
                    if (math.dot(r0 - c0, nrm) > 2f && AddLine(st, mainRole, c0, r0) != null) spans.Add((c0, r0));
                    if (math.dot(c1 - r1, nrm) > 2f && AddLine(st, mainRole, r1, c1) != null) spans.Add((r1, c1));
                }
            foreach (var g in gates) if (math.distance(g.Position, start) > 0.5f) GateSpur(st, g.Position);
            ctx.Check();

            Monument(st, dir, squareCourt);
            ctx.Check();

            // Rooms flank the allées; the allée sits between the procession (or the pool walks) and the rooms.
            float inner = pool ? walkOff + Composer.DefaultHalfWidth(NetRole.Pathway) : bandHalf;
            float alleeOff = inner + 3.2f;
            float roomsFrom = (ctx.B("allees") ? alleeOff + 3.6f : inner + 3f);
            if (ctx.B("rooms")) RoomRows(st, spans, roomsFrom);
            if (ctx.B("allees")) Allees(st, spans, alleeOff);
            if (ctx.B("borders")) Borders(st, spans, inner + 1.5f);
            st.VistaA = start; st.VistaB = centreFocus ? far : mon; st.Vista = alleeOff + 2f;
            if (ctx.B("bosque"))
            {
                float roomsOut = ctx.B("rooms") ? roomsFrom + ctx.F("roomSize") * 0.84f + 4f : (ctx.B("allees") ? alleeOff + 5f : inner + 6f);
                Bosques(st, start, dir, math.distance(start, centreFocus ? far : mon), roomsOut);
            }
            // the groves first (they give the memorial its structure: without them its side lawns read empty, run
            // show-23-builds-1030-012225-3eac); the places take the ground the groves leave
            PlanMoods(st);
            vistaA = start; vistaB = centreFocus ? far : mon; vista = alleeOff + 2f;
            ctx.Plan.Facts["axisLength"] = math.distance(start, mon);
            return true;
        }

        private static void Pool(State st, OrientedRect coping)
        {
            var ctx = st.Ctx;
            ctx.Surface(coping.Ring, SurfaceRole.Plaza, AssetMix.One("", ParkArt.Pavement), AssetCategories.SurfacePaving);
            var water = new OrientedRect { Center = coping.Center, Axis = coping.Axis, HalfLength = coping.HalfLength - 1.2f, HalfWidth = coping.HalfWidth - 1.2f };
            LeisureKit.TakeRect(st.Occ, coping);
            bool real = ctx.B("realWater");
            if (real)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var q in water.Ring) { float h = ctx.H(q); lo = math.min(lo, h); hi = math.max(hi, h); }
                if (hi - lo > 0.5f || ctx.B("raised")) { real = false; ctx.Issue(IssueSeverity.Info, K + "Issue.PoolNotFlat"); }
                else
                {
                    float level = lo - 0.2f;
                    ctx.Plan.Terraform.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(water.Ring, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - 0.6f, Permanent = true,
                        WaterLevel = level, WaterAnchor = new float3(water.Center.x, level, water.Center.y),
                    });
                    ctx.Issue(IssueSeverity.Warning, K + "Issue.PoolPermanent");
                }
            }
            if (!real)
            {
                string cat = LeisureKit.Category(ctx, ctx.S("poolAsset"), AssetCategories.SurfaceWater, AssetCategories.SurfacePaving);
                if (cat != null) ctx.Surface(water.Ring, SurfaceRole.Water, AssetMix.One(ctx.S("poolAsset")), cat);
            }
            ctx.Plan.Facts["poolArea"] = water.HalfLength * water.HalfWidth * 4f;
            // rf-parks: a rill of small fountains down the pool's middle. A clean game has no water-look surface, so the pool alone
            // was a grey slab; with the fountains it reads as water in any game.
            if (water.HalfWidth >= 1.2f)
            {
                string fc = CatOr(st, false, AssetCategories.CenterpieceFountain, AssetCategories.Centerpiece);
                float step = math.max(6f, water.HalfWidth * 2f + 3f);
                int nf = 0;
                foreach (var p in LeisureKit.Along(water.Local(-water.HalfLength + step * 0.5f, 0f), water.Local(water.HalfLength - step * 0.5f, 0f), step))
                {
                    var fo = ctx.Object(p, math.atan2(water.Axis.x, water.Axis.y), ObjectRole.Furniture, AssetMix.Pick(MemorialAssets.PoolFountains, (uint)nf), Hashing.Mix(ctx.Seed, 0xF0F0u + (uint)nf), -1, fc);
                    fo.Scale = math.clamp(water.HalfWidth * 2f / 4f, 0.6f, 1.2f);
                    nf++;
                }
                ctx.Plan.Facts["poolFountains"] = nf;
            }
            // Low lights on the coping, both long sides.
            if (ctx.S("lighting") != "none")
            {
                string cat = st.Chains.Get(false, AssetCategories.LampLow, AssetCategories.Lamp);
                if (cat != null)
                {
                    float step = math.max(4f, ctx.F("lampSpacing") * 0.6f);
                    int i = 0;
                    foreach (int s in new[] { -1, 1 })
                        foreach (var p in LeisureKit.Along(coping.Local(-water.HalfLength, s * (coping.HalfWidth - 0.6f)), coping.Local(water.HalfLength, s * (coping.HalfWidth - 0.6f)), step))
                            ctx.Object(p, 0f, ObjectRole.Lamp, AssetMix.One(ctx.S("lampAsset"), ParkArt.LowLights), Hashing.Mix(ctx.Seed, 0x9A00u + (uint)i++), -1, cat);
                }
            }
        }

        /// <summary>The focal monument with its flags, plaques, benches, uplights and flower ring.</summary>
        private static void Monument(State st, float2 dir, bool squareCourt)
        {
            var ctx = st.Ctx; var occ = st.Occ;
            var mon = st.Monument; float fr = st.Forecourt;
            string style = ctx.S("style");
            float faceStart = LeisureKit.Yaw(mon, mon - dir);
            // rf-parks: the focal piece is always there (curated vanilla pieces resolve by name when the category is empty, as in a
            // clean game: the memorial had no monument at all) and it is scaled up to read as a monument, not a garden ornament.
            string cat = style == "sculpture"
                ? CatOr(st, ctx.L("sculptureAssets").Length > 0, AssetCategories.MemorialSculpture, AssetCategories.CenterpieceStatue, AssetCategories.Centerpiece)
                : CatOr(st, ctx.S("monumentAsset").Length > 0, AssetCategories.MemorialMonument, AssetCategories.CenterpieceStatue, AssetCategories.Centerpiece);
            {
                var asset = style == "sculpture" ? AssetMix.Pick(Or(ctx.L("sculptureAssets"), MemorialAssets.Sculptures), ctx.Seed) : AssetMix.One(ctx.S("monumentAsset"), MemorialAssets.Monuments);
                var ob = ctx.Object(mon, faceStart, ObjectRole.Centerpiece, asset, Hashing.Mix(ctx.Seed, 0x303u), -1, cat);
                ob.Scale = math.clamp(ctx.F("monumentScale") / 100f, 0.5f, 5f);
                ctx.Plan.Facts["monumentScale"] = ob.Scale;
            }
            float baseA = math.atan2(dir.y, dir.x);   // pointing away from the procession (behind the monument)
            float inner = fr - 1.2f;
            // Flags in an arc behind the monument.
            int flags = ctx.I("flags");
            if (flags > 0)
            {
                string fc = st.Chains.Get(false, AssetCategories.MemorialFlag);
                if (fc != null)
                    for (int i = 0; i < flags; i++)
                    {
                        float a = baseA + (flags == 1 ? 0f : (i / (flags - 1f) - 0.5f) * 2.4f);
                        var p = mon + new float2(math.cos(a), math.sin(a)) * inner;
                        if (occ.Paths.Overlaps(p, 0.5f, 0.4f)) continue;
                        ctx.Object(p, faceStart, ObjectRole.Furniture, AssetMix.One("", MemorialAssets.Flags), Hashing.Mix(ctx.Seed, 0xF1A0u + (uint)i), -1, fc);
                        st.Flags++;
                    }
            }
            // Plaques / wreaths in an arc in front, leaving the approach open.
            int plaques = ctx.I("plaques");
            if (plaques > 0)
            {
                string pc = st.Chains.Get(false, AssetCategories.MemorialPlaque, AssetCategories.Grave);
                if (pc != null)
                {
                    float r = math.max(2.5f, fr * 0.55f);
                    for (int i = 0; i < plaques; i++)
                    {
                        int half = (plaques + 1) / 2;
                        int k = i % half; float s = i < half ? 1f : -1f;
                        float a = baseA + math.PI + s * (0.55f + k * (1.3f / math.max(1, half)));
                        var p = mon + new float2(math.cos(a), math.sin(a)) * r;
                        if (occ.Paths.Overlaps(p, 0.6f, 0.4f)) continue;
                        ctx.Object(p, LeisureKit.Yaw(p, mon) + math.PI, ObjectRole.Furniture, AssetMix.One("", MemorialAssets.Plaques), Hashing.Mix(ctx.Seed, 0x91A0u + (uint)i), -1, pc);
                        st.Plaques++;
                    }
                }
            }
            // Benches on the forecourt's sides, looking at the monument.
            var bench = AssetMix.One("", ParkArt.Benches);
            int nb = style == "square" ? 8 : 2;
            for (int i = 0; i < nb; i++)
            {
                float a = nb == 2 ? baseA + (i == 0 ? 1f : -1f) * math.PI * 0.5f : baseA + math.PI / 8f + i * math.PI * 2f / nb;
                var p = mon + new float2(math.cos(a), math.sin(a)) * (inner - (squareCourt ? 0f : 0.3f));
                if (occ.Paths.Overlaps(p, 1f, 0.4f)) continue;
                if (math.abs(math.atan2(math.sin(a - baseA - math.PI), math.cos(a - baseA - math.PI))) < 0.4f) continue;
                ctx.Object(p, LeisureKit.Yaw(p, mon), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, 0xBE30u + (uint)i));
            }
            // Uplights round the monument (always, unless no lighting).
            if (ctx.S("lighting") != "none")
            {
                string lc = st.Chains.Get(false, AssetCategories.LampLow, AssetCategories.Lamp);
                if (lc != null)
                    for (int i = 0; i < 4; i++)
                    {
                        float a = baseA + math.PI * 0.25f + i * math.PI * 0.5f;
                        var p = mon + new float2(math.cos(a), math.sin(a)) * math.min(3f, fr * 0.4f);
                        if (occ.Paths.Overlaps(p, 0.4f, 0.4f)) continue;
                        ctx.Object(p, 0f, ObjectRole.Lamp, AssetMix.One(ctx.S("lampAsset"), ParkArt.LowLights),
                            Hashing.Mix(ctx.Seed, 0x0A00u + (uint)i), -1, lc);
                    }
                if (ctx.S("lighting") == "lanterns")
                    for (int i = 0; i < 4; i++)
                    {
                        float a = baseA + math.PI * 0.25f + i * math.PI * 0.5f;
                        var p = mon + new float2(math.cos(a), math.sin(a)) * (squareCourt ? fr * 1.3f : fr - 0.6f);
                        if (!occ.Paths.Overlaps(p, 0.4f, 0.4f))
                            ctx.Object(p, 0f, ObjectRole.Lamp, AssetMix.One(ctx.S("lampAsset"), ParkArt.ParkLamps), Hashing.Mix(ctx.Seed, 0x0A10u + (uint)i));
                    }
            }
            // Flower ring (poppies for a war memorial) just outside the forecourt.
            if (ctx.B("flowerRing"))
            {
                // Art pass (a1): a wreath of flowering shrubs (vanilla FlowerBushWild, poppies when the player has them),
                // spaced by their real ~1.6 m footprint; 2.5 m flowerbed tiles overlapped each other on the curve.
                string fc = st.Chains.Get(false, AssetCategories.Bush, AssetCategories.Flowerbed);
                if (fc != null)
                {
                    var wreath = style == "war" ? MemorialAssets.Poppies : MemorialAssets.WreathShrubs;
                    float r = (squareCourt ? fr * 1.42f : fr) + 1.4f;
                    int n = math.clamp((int)(2f * math.PI * r / 1.9f), 8, 120);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * 2f * math.PI / n;
                        var p = mon + new float2(math.cos(a), math.sin(a)) * r;
                        uint v = Hashing.Mix(ctx.Seed, 0xF100u + (uint)i);
                        LeisureKit.Put(ctx, occ, p, a, fc == AssetCategories.Bush ? ObjectRole.Bush : ObjectRole.Planter, fc == AssetCategories.Bush ? AssetMix.Pick(wreath, v) : AssetMix.One("", ParkArt.Flowerbeds), fc, 0.8f,
                            v, 0.5f, 0.1f);
                    }
                }
            }
            // A monument square: a formal ring of trees round the paved court.
            if (style == "square")
            {
                var treeIds = Or(ctx.L("treeSpecies"), MemorialAssets.Avenue);
                float r = fr * 1.42f + 5f;
                int n = math.clamp((int)(2f * math.PI * r / math.max(6f, ctx.F("treeSpacing"))), 6, 40);
                for (int i = 0; i < n; i++)
                {
                    float a = i * 2f * math.PI / n + 0.2f;
                    uint v = Hashing.Mix(ctx.Seed, 0x5D00u + (uint)i);
                    LeisureKit.Put(ctx, occ, mon + new float2(math.cos(a), math.sin(a)) * r, 0f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), null, 2.2f, v, 1f, 0.3f, 3, 3);
                }
            }
        }

        /// <summary>Tree rows either side of each axis span, in symmetric pairs only.</summary>
        private static void Allees(State st, List<(float2 a, float2 b)> spans, float offset)
        {
            var ctx = st.Ctx;
            var treeIds = Or(ctx.L("treeSpecies"), MemorialAssets.Avenue);
            float step = ctx.F("treeSpacing");
            int i = 0;
            foreach (var (a, b) in spans)
            {
                var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x);
                float len = math.distance(a, b);
                for (float u = 4f; u < len - 2f; u += step)
                {
                    var p = a + d * u;
                    var l = p + n * offset; var r = p - n * offset;
                    if (!st.Occ.Free(l, 2.2f, 1f, 0.3f) || !st.Occ.Free(r, 2.2f, 1f, 0.3f)) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0xA11Eu + (uint)(i / 2));
                    foreach (var q in new[] { l, r })
                    {
                        ctx.Object(q, 0f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), v, 3);
                        st.Occ.Take(q, 2.2f, 3);
                        i++;
                    }
                }
            }
            ctx.Plan.Facts["alleeTrees"] = i;
        }

        /// <summary>
        /// (rf-parks) Flower borders either side of the procession, between the walk and the allée: a ribbon of flowerbeds (a
        /// memorial garden) or of poppies (a war memorial), in symmetric pairs, so the axis reads from far away.
        /// </summary>
        private static void Borders(State st, List<(float2 a, float2 b)> spans, float offset)
        {
            var ctx = st.Ctx;
            bool war = ctx.S("style") == "war";
            string cat = war ? CatOr(st, false, AssetCategories.Bush, AssetCategories.Flowerbed) : CatOr(st, false, AssetCategories.Flowerbed, AssetCategories.Bush);
            // Checked at a shrub's full footprint (1.1 m) against paths and reserved space; poppies then hold only their core,
            // so the next one 1.7 m along still fits.
            float step = war ? 1.7f : 2.4f, rad = 1.1f, hold = war ? 0.5f : 1.1f;
            int n = 0;
            foreach (var (a, b) in spans)
            {
                var d = math.normalizesafe(b - a); var nrm = new float2(-d.y, d.x);
                float len = math.distance(a, b);
                for (float u = 3f; u < len - 2f; u += step)
                {
                    var p = a + d * u;
                    var l = p + nrm * offset; var r = p - nrm * offset;
                    if (!st.Occ.Free(l, rad, 0.2f, 0.05f) || !st.Occ.Free(r, rad, 0.2f, 0.05f)) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0xB0D0u + (uint)n);
                    var asset = war ? AssetMix.Pick(MemorialAssets.Poppies, v) : AssetMix.Pick(ParkArt.Flowerbeds, v);
                    foreach (var q in new[] { l, r })
                    {
                        ctx.Object(q, math.atan2(d.x, d.y), war ? ObjectRole.Bush : ObjectRole.Planter, asset, v, -1, cat);
                        st.Occ.Take(q, hold, 4);
                        n++;
                    }
                }
            }
            ctx.Plan.Facts["borderPlants"] = n;
        }

        /// <summary>
        /// (rf-parks) Bosques: the side lawns beyond the rooms planted as a regular grid of trees, mirrored across the axis (trees are
        /// placed in symmetric pairs only, like the allées). A memorial grove frames the axis and fills the big empty lawns that loose
        /// lawn trees used to clump into.
        /// </summary>
        private static void Bosques(State st, float2 start, float2 dir, float length, float from)
        {
            var ctx = st.Ctx; var region = st.Region;
            var nrm = new float2(-dir.y, dir.x);
            // Per style (first game run: one dense grid read as a forest block, and the same on every style): the war memorial a
            // solemn double row, the reflection garden a grove of remembrance in quincunx, the memorial garden sparse specimen trees.
            string style = ctx.S("style");
            float k = style == "garden" ? 2.1f : style == "war" ? 1.3f : 1.6f;
            int maxRows = style == "war" ? 2 : 99;
            bool quincunx = style != "war";
            float sp = math.max(8f, ctx.F("treeSpacing") * k);
            var treeIds = Or(ctx.L("treeSpecies"), MemorialAssets.Avenue);
            float edge = ctx.M(5f, 3f);
            int n = 0, rows = 0, step = 0;
            for (float v = from + sp * 0.5f; v < 400f && rows < maxRows; v += sp, step++)
            {
                bool any = false, inside = false;
                float off = quincunx && (step & 1) == 1 ? sp * 0.5f : 0f;
                for (float u = sp * 0.75f + off; u < length - sp * 0.25f; u += sp)
                {
                    var p = start + dir * u;
                    var l = p + nrm * v; var r = p - nrm * v;
                    if (region.Contains(l) || region.Contains(r)) inside = true;
                    if (region.BoundaryDistance(l, edge + 3f) < edge || region.BoundaryDistance(r, edge + 3f) < edge) continue;
                    if (!region.Contains(l) || !region.Contains(r)) continue;
                    if (!st.Occ.Free(l, 2.4f, 1f, 0.3f) || !st.Occ.Free(r, 2.4f, 1f, 0.3f)) continue;
                    uint vv = Hashing.Mix(ctx.Seed, 0xB05Cu + (uint)rows);
                    foreach (var q in new[] { l, r })
                    {
                        ctx.Object(q, 0f, ObjectRole.Tree, AssetMix.Pick(treeIds, vv), Hashing.Mix(vv, (uint)n), 3);
                        st.Occ.Take(q, 2.4f, 3);
                        n++;
                    }
                    any = true;
                }
                if (any) rows++;
                if (!inside) break;
            }
            ctx.Plan.Facts["bosqueTrees"] = n;
        }

        /// <summary>Hedged rooms in a row on both sides of each axis span, opening towards the axis.</summary>
        private static void RoomRows(State st, List<(float2 a, float2 b)> spans, float from)
        {
            var ctx = st.Ctx; var region = st.Region;
            float s = ctx.F("roomSize");
            float hl = s * 0.5f, hw = s * 0.42f;
            int index = 0;
            foreach (var (a, b) in spans)
            {
                var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x);
                float len = math.distance(a, b);
                for (float u = hl + 2f; u <= len - hl - 1f; u += s + ctx.M(3f, 2f))
                {
                    var p = a + d * u;
                    var left = new OrientedRect { Center = p + n * (from + hw), Axis = d, HalfLength = hl, HalfWidth = hw };
                    var right = new OrientedRect { Center = p - n * (from + hw), Axis = d, HalfLength = hl, HalfWidth = hw };
                    bool lf = LeisureKit.RectFits(region, st.Occ, left, 0.6f), rf = LeisureKit.RectFits(region, st.Occ, right, 0.6f);
                    // Symmetric pairs; a single room only where the outline leaves one side (irregular areas).
                    if (lf && rf) { Room(st, left, -1f, index++); Room(st, right, 1f, index++); }
                    else if (lf && !region.Contains(right.Center)) Room(st, left, -1f, index++);
                    else if (rf && !region.Contains(left.Center)) Room(st, right, 1f, index++);
                }
            }
        }

        /// <param name="towardAxis">Local v sign of the room side facing the axis.</param>
        private static void Room(State st, OrientedRect r, float towardAxis, int index)
        {
            var ctx = st.Ctx;
            var opening = r.Local(0f, towardAxis * r.HalfWidth);
            LeisureKit.TakeRect(st.Occ, r);
            // Spur from the opening out to the nearest path (junction node on that path). FEEDBACK-4: the walk then
            // continues into the room as a path net, and the room's benches stand beside it (never out on the lawn).
            var door = opening + r.Side * towardAxis * 0.2f;
            bool joined = Spur(st, door, NetRole.Pathway, 30f, false, r.Side * towardAxis);
            uint v = Hashing.Mix(ctx.Seed, 0x500Au + (uint)index);
            string hedgeCat = st.Chains.Get(ctx.S("hedgeAsset").Length > 0, AssetCategories.Hedge, AssetCategories.Bush);
            if (hedgeCat != null)
            {
                var ring = r.Ring;
                var hedge = AssetMix.One(ctx.S("hedgeAsset"), ParkArt.HighHedge);
                for (int k = 0; k < 4; k++)
                {
                    var e0 = ring[k]; var e1 = ring[(k + 1) % 4];
                    float yaw = LeisureKit.Yaw(e0, e1);
                    foreach (var p in LeisureKit.Along(e0, e1, hedgeCat == AssetCategories.Hedge ? 4.1f : 1.6f))   // hedge pieces are ~4.1 m long
                    {
                        if (math.distance(p, opening) < 2.0f) continue;
                        ctx.Object(p, yaw, ObjectRole.Bush, hedge, v, -1, hedgeCat);
                    }
                }
            }
            string content = ctx.S("roomContent");
            if (content == "mixed")
            {
                string[] cycle;
                switch (ctx.S("style"))
                {
                    case "war": cycle = new[] { "benches", "flowers" }; break;
                    case "reflection": cycle = new[] { "tree", "benches" }; break;
                    case "sculpture": cycle = new[] { "sculpture", "tree" }; break;
                    default: cycle = new[] { "flowers", "benches", "tree", "sculpture" }; break;
                }
                content = cycle[(index / 2) % cycle.Length];
            }
            var bench = AssetMix.One("", ParkArt.Benches);
            float hw = r.HalfWidth;
            float pathHw = Composer.DefaultHalfWidth(NetRole.Pathway);
            const float benchSide = 2.9f;   // walk half width + bench footprint, from the walk's centre line
            var back = r.Local(0f, -towardAxis * (hw - 1.0f));
            var centre = r.Center;
            // Where the inner walk ends (local v, positive towards the axis): clear of what stands at its end.
            float walkEndV;
            switch (content)
            {
                case "benches": walkEndV = -(hw - 3.1f); break;          // plaque at the back
                case "flowers": walkEndV = -(hw - 4.2f); break;          // bench at the back
                case "sculpture": walkEndV = 2.6f; break;                // sculpture in the centre
                default: walkEndV = 4.0f; break;                         // specimen tree in the centre
            }
            float doorV = hw + 0.2f;
            bool walk = false;
            if (joined && doorV - walkEndV >= 1.5f)
            {
                var inner = AddLine(st, NetRole.Pathway, door, r.Local(0f, towardAxis * walkEndV));
                if (inner != null) { inner.IsSpur = true; walk = true; }
            }
            // Benches either side of the walk (facing it), or either side of the door when the room has no walk.
            float seatV = walk ? math.max(walkEndV + 0.6f, math.min(doorV - 1.6f, 0.5f * (walkEndV + doorV))) : hw - 1.6f;
            switch (content)
            {
                case "benches":
                {
                    foreach (int sgn in new[] { -1, 1 })
                    {
                        if (r.HalfLength - benchSide < 1.2f) break;
                        var p = r.Local(sgn * benchSide, towardAxis * seatV);
                        ctx.Object(p, LeisureKit.Yaw(p, r.Local(0f, towardAxis * seatV)), ObjectRole.Bench, bench, Hashing.Mix(v, (uint)(sgn + 3)));
                    }
                    string pc = CategoryChains.Quiet(AssetCategories.MemorialPlaque);
                    if (pc != null) { ctx.Object(back, LeisureKit.Yaw(back, opening), ObjectRole.Furniture, AssetMix.One("", MemorialAssets.Plaques), Hashing.Mix(v, 9u), -1, pc); st.Plaques++; }
                    break;
                }
                case "flowers":
                {
                    string fc = st.Chains.Get(false, AssetCategories.Flowerbed, AssetCategories.Bush);
                    if (fc != null)
                        for (float fu = -r.HalfLength + 2.2f; fu <= r.HalfLength - 2.1f; fu += 2.9f)
                            for (float fv = -r.HalfWidth + 2.2f; fv <= r.HalfWidth - 2.1f; fv += 2.9f)
                            {
                                if (math.abs(fu) < pathHw + 0.7f) continue;   // the walk through the middle
                                ctx.Object(r.Local(fu, fv), r.Yaw, fc == AssetCategories.Bush ? ObjectRole.Bush : ObjectRole.Planter, AssetMix.One("", ParkArt.Flowerbeds), Hashing.Mix(v, (uint)(fu * 10f + fv * 100f + 1000f)), -1, fc);
                            }
                    // The bench closes the walk at the back, looking down it to the opening.
                    var seat = r.Local(0f, -towardAxis * (hw - 1.3f));
                    ctx.Object(seat, LeisureKit.Yaw(seat, opening), ObjectRole.Bench, bench, Hashing.Mix(v, 5u));
                    break;
                }
                case "sculpture":
                {
                    string sc = CatOr(st, ctx.L("sculptureAssets").Length > 0, AssetCategories.MemorialSculpture, AssetCategories.CenterpieceStatue, AssetCategories.Centerpiece);
                    if (ctx.B("paving")) ctx.Surface(LayerRegion.Circle(centre, math.min(2.4f, r.HalfWidth - 1.5f), 16), SurfaceRole.Plaza, AssetMix.One("", ParkArt.Pavement), AssetCategories.SurfacePaving);
                    if (sc != null)
                    {
                        ctx.Object(centre, LeisureKit.Yaw(centre, opening), ObjectRole.Centerpiece, AssetMix.Pick(Or(ctx.L("sculptureAssets"), MemorialAssets.Sculptures), v), v, -1, sc);
                        st.Sculptures++;
                    }
                    var p = r.Local(benchSide, towardAxis * seatV);
                    if (r.HalfLength - benchSide >= 1.2f) ctx.Object(p, LeisureKit.Yaw(p, centre), ObjectRole.Bench, bench, Hashing.Mix(v, 7u));
                    break;
                }
                default:   // a single specimen tree, two benches beside the walk looking at it
                {
                    var treeIds = Or(ctx.L("treeSpecies"), MemorialAssets.SpecimenTrees);
                    ctx.Object(centre, 0f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), v, 3);
                    foreach (int sgn in new[] { -1, 1 })
                    {
                        var p = r.Local(sgn * benchSide, towardAxis * seatV);
                        if (r.HalfLength - benchSide < 1.2f || math.distance(p, centre) < 3.6f) continue;
                        ctx.Object(p, LeisureKit.Yaw(p, centre), ObjectRole.Bench, bench, Hashing.Mix(v, (uint)(sgn + 11)));
                    }
                    break;
                }
            }
            if (ctx.S("lighting") == "uplights")
            {
                string lc = st.Chains.Get(false, AssetCategories.LampLow, AssetCategories.Lamp);
                if (lc != null)
                    foreach (int sgn in new[] { -1, 1 })
                        ctx.Object(opening + r.Axis * (sgn * 2.1f) - r.Side * towardAxis * 0.8f, 0f, ObjectRole.Lamp, AssetMix.One(ctx.S("lampAsset"), ParkArt.LowLights), Hashing.Mix(v, (uint)(sgn + 21)), -1, lc);
            }
            st.Rooms++;
        }

        // ───────────── sculpture park ─────────────

        private void Trail(State st, List<Gate> gates)
        {
            var ctx = st.Ctx; var region = st.Region; var occ = st.Occ;
            int loops = math.max(1, ParkPathPlanner.LoopsByArea(region.Area));
            NaturalPaths(ctx, occ, gates, loops, 5f, 20f, "narrow", st.PathAsset, region.Area > 20000f);
            ctx.Check();
            // Focal sculpture on a round forecourt in the most open spot, joined to the trail.
            float fr = math.clamp(ctx.F("forecourt"), 5f, 14f);
            if (Spots.Find(occ, fr + 1f, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0xF0Cu), out var mon, null, 0f, 2f, 2500))
            {
                st.Monument = mon; st.Forecourt = fr;
                ctx.Surface(LayerRegion.Circle(mon, fr, 24), SurfaceRole.Plaza, AssetMix.One("", ParkArt.Pavement), AssetCategories.SurfacePaving);
                occ.Take(mon, fr, 0);
                var dir = new float2(0, 1);
                if (occ.Paths.Nearest(mon, 500f, out var foot) >= 0)
                {
                    dir = math.normalizesafe(mon - foot, dir);
                    var rim = mon - dir * fr;
                    if (math.distance(rim, foot) > 1f && !region.CrossesBoundary(rim, foot, false))
                        LeisureKit.Path(ctx, occ, rim, foot, NetRole.Pathway, st.PathAsset);
                }
                if (ctx.B("raised")) Mounds.Raise(ctx, region, mon, fr + 5f, fr + 0.5f, ctx.F("raiseHeight"), 0.3f);
                Monument(st, dir, false);
            }
            ctx.Check();
            // Pool in an open lawn.
            if (ctx.B("pool"))
            {
                float w = ctx.F("poolWidth"), len = math.clamp(math.sqrt(region.Area) * ctx.F("poolLength") / 100f, 8f, 60f);
                LeisureKit.Frame(region, out _, out var axis, out _, out float major, out float minor);
                if (minor > major * 0.85f) axis = LongestEdge(region);   // square to the street, not diagonal
                bool placed = false;
                for (int i = 0; i < 4 && !placed; i++, len *= 0.8f)
                {
                    float reach = math.sqrt(len * len + w * w) * 0.5f + 1.5f;
                    if (!Spots.Find(occ, reach, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0x9001u + (uint)i), out var pc, null, 0f, 2f, 2000)) continue;
                    var coping = new OrientedRect { Center = pc, Axis = axis, HalfLength = len * 0.5f + 1.2f, HalfWidth = w * 0.5f + 1.2f };
                    if (!LeisureKit.RectFits(region, occ, coping, 1f)) continue;
                    Pool(st, coping);
                    placed = true;
                }
                if (!placed) ctx.Issue(IssueSeverity.Info, K + "Issue.NoPool");
            }
            // Sculptures beside the trail on paved pads with a hedge backdrop.
            int want = ctx.I("sculptures");
            if (want > 0)
            {
                var ids = ctx.L("sculptureAssets");
                string sc = CatOr(st, ids.Length > 0, AssetCategories.MemorialSculpture, AssetCategories.CenterpieceStatue, AssetCategories.Centerpiece);
                string hedgeCat = ctx.B("rooms") ? st.Chains.Get(ctx.S("hedgeAsset").Length > 0, AssetCategories.Hedge, AssetCategories.Bush) : null;
                if (sc != null)
                {
                    var anchors = PathAnchors.Pick(PathAnchors.Beside(occ, 3.6f, 5f, 0.8f), want, ctx.M(11f, 7.5f), false);
                    int i = 0;
                    foreach (var an in anchors)
                    {
                        if (!occ.Free(an.Position, 3.6f, 4.2f, 0.3f)) continue;
                        uint v = Hashing.Mix(ctx.Seed, 0x5C00u + (uint)i++);
                        if (ctx.B("paving")) ctx.Surface(LayerRegion.Circle(an.Position, 2.2f, 14), SurfaceRole.Plaza, AssetMix.One("", ParkArt.Pavement), AssetCategories.SurfacePaving);
                        var sob = ctx.Object(an.Position, LeisureKit.Yaw(an.Position, an.Toward), ObjectRole.Centerpiece, AssetMix.Pick(Or(ids, MemorialAssets.Sculptures), v), v, -1, sc);
                        sob.Scale = math.clamp(ctx.F("monumentScale") / 100f * 0.6f, 0.8f, 2.5f);
                        st.Sculptures++;
                        if (hedgeCat != null)
                        {
                            var away = math.normalizesafe(an.Position - an.Toward);
                            float a0 = math.atan2(away.y, away.x);
                            // Art pass (a1): three ~4 m hedge pieces laid along the arc (tangent), not seven fanned radially
                            // through each other; plain shrubs keep the old tight arc.
                            bool pieces = hedgeCat == AssetCategories.Hedge;
                            float rad = pieces ? 3.6f : 3.2f, stepA = pieces ? 4.1f / 3.6f : 0.32f;
                            int half = pieces ? 1 : 3;
                            for (int k = -half; k <= half; k++)
                            {
                                float a = a0 + k * stepA;
                                var p = an.Position + new float2(math.cos(a), math.sin(a)) * rad;
                                var tangent = new float2(-math.sin(a), math.cos(a));
                                ctx.Object(p, pieces ? math.atan2(tangent.x, tangent.y) : a, ObjectRole.Bush,
                                    pieces ? AssetMix.One(ctx.S("hedgeAsset"), ParkArt.HighHedge) : AssetMix.Pick(ParkArt.GreenShrubs, v), v, -1, hedgeCat);
                            }
                        }
                        occ.Take(an.Position, 3.6f, 2);
                        if (ctx.S("lighting") == "uplights")
                        {
                            string lc = st.Chains.Get(false, AssetCategories.LampLow, AssetCategories.Lamp);
                            if (lc != null) ctx.Object(math.lerp(an.Position, an.Toward, 0.45f), 0f, ObjectRole.Lamp, AssetMix.One(ctx.S("lampAsset"), ParkArt.LowLights), Hashing.Mix(v, 3u), -1, lc);
                        }
                    }
                    if (st.Sculptures < want) ctx.Issue(IssueSeverity.Info, K + "Issue.FewerSculptures", st.Sculptures, want);
                }
            }
            // Benches along the trail between the sculptures.
            PathBenches(ctx, occ, 45f, AssetMix.One("", ParkArt.Benches), 0xBE77u);
        }

        // ───────────── edges & lights ─────────────

        private static void HedgeBoundary(State st, List<Gate> gates)
        {
            var ctx = st.Ctx; var region = st.Region;
            string cat = st.Chains.Get(ctx.S("hedgeAsset").Length > 0, AssetCategories.Hedge, AssetCategories.Bush);
            if (cat == null) return;
            var hedge = AssetMix.One(ctx.S("hedgeAsset"), ParkArt.HighHedge);
            int n = 0;
            for (float s = 0.8f; s < region.Perimeter && n < 4000; s += cat == AssetCategories.Hedge ? 4.1f : 1.7f)
            {
                var e = region.PointAt(s, out var t, out var inward, out _);
                var p = e + inward * 1.1f;
                bool atGate = false;
                foreach (var g in gates) if (math.distance(g.Position, e) < 3.5f) { atGate = true; break; }
                if (atGate) continue;
                if (LeisureKit.Put(ctx, st.Occ, p, LeisureKit.Yaw(e, e + t), ObjectRole.Bush, hedge, cat, 0.7f, Hashing.Mix(ctx.Seed, 0x4ED6u + (uint)n), 0.4f, 0.05f)) n++;
            }
            ctx.Plan.Facts["hedgePieces"] = n;
        }

        /// <summary>Lights beside every path: lantern pairs (lamp) or single low bollard lights (lamp.low).</summary>
        private static void PathLights(State st, float interval, bool chainLow, bool pairs)
        {
            if (interval <= 0f) return;
            var ctx = st.Ctx;
            string cat = chainLow ? st.Chains.Get(false, AssetCategories.LampLow, AssetCategories.Lamp) : null;
            if (chainLow && cat == null) return;
            var lamp = AssetMix.One(ctx.S("lampAsset"), chainLow ? ParkArt.LowLights : ParkArt.ParkLamps);
            int n = 0;
            int k = 0;
            foreach (var (p, nrm, hw) in PathWalk.Every(st.Occ, interval))
            {
                foreach (int sgn in pairs ? new[] { -1, 1 } : new[] { (k & 1) == 0 ? 1 : -1 })
                {
                    var q = p + nrm * sgn * (hw + 0.7f);
                    if (LeisureKit.Put(ctx, st.Occ, q, 0f, ObjectRole.Lamp, lamp, cat, 0.35f, Hashing.Mix(ctx.Seed, 0x1A30u + (uint)n), 0.3f, 0.15f)) n++;
                }
                k++;
            }
            ctx.Plan.Facts["pathLights"] = n;
        }
    }
}
