// Waterfront (s30 rewrite of the s20 design): a smooth promenade near the water, and let the ground work itself out.
//
// Playtests 54–57: the s20 waterfront fitted itself to every wiggle of the real shore — a paving edge that zig-zagged with
// the waterline, a promenade level re-fitted every few metres (dipping up and down), quay / railing / boardwalk pieces cut
// apart by every link, pier and cliff probe (short walls at different heights, gaps, overlapping ends, S-curved timber
// decks), ramps, pads and step levels cut into the bank (vertical terrain "curtains" where they met the slope). Now:
//
//   walk        ONE smooth curve (ShoreWalk: the waterline offset, Gaussian-smoothed over tens of metres, pushed inland
//               where the bank would get too steep, minimum radius checked), at ONE smooth height (±40 m smoothing, ≤ 2 %
//               along it), set back from the water so it sits on land
//   edge        what meets the water is ONE continuous run along its own smooth curve at ONE deck height: a wide quay
//               (harbour), a walkway quay (pier park, river steps), a timber boardwalk (promenade), or nothing (natural bank)
//   ground      one continuous field from the curves: flat across the walk and its shoulders, one smooth graded bank down
//               to the edge / the water, one smooth bank (or, on a steep hillside, a deliberate retaining wall — the
//               "Hillside" control) up to the ground behind. Nothing else carves the ground (no ramps, pads or step levels)
//   links       straight paths down the smooth bank (only where it is gentle enough), piers as T junctions on the edge
//   railing     ONE continuous fence along the promenade's water edge, only where the ground there drops > 1 m
//   planting    rows and beds parallel to the walk (not to the waterline), a park behind
// Everything stands on the final ground (Resnap); furniture never on slopes steeper than 12 %.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Types.Beach;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Waterfront
{
    [AreaGenerator(72, Group = "water")]
    public sealed class WaterfrontLayer : LeisureLayer
    {
        public static readonly string[] Styles = { "promenade", "pier", "harbor", "riverSteps", "fishing" };
        /// <summary>(s20) What meets the water: automatic (by style), wide quay, walkway quay, boardwalk, natural bank.</summary>
        public static readonly string[] Edges = { "auto", "quay", "walkway", "boardwalk", "bank" };
        /// <summary>(s30) Where the promenade is cut into rising ground: automatic by height, a retaining wall, or a graded bank.</summary>
        public static readonly string[] Hillsides = { "auto", "wall", "bank" };
        private static readonly List<Gate> NoGates = new List<Gate>();
        /// <summary>An edge element may sit this far past the drawn outline to reach the real water (m).</summary>
        public const float EdgeReach = 8f;
        /// <summary>Flat shoulder on either side of the paved band (m): the field is level there before any bank starts.</summary>
        public const float Shoulder = 1.5f;
        /// <summary>Smallest radius a walk or an edge curve may have (m).</summary>
        public const float MinRadius = 40f;
        /// <summary>Steepest average grade of the bank a straight link path may run down (m/m).</summary>
        public const float LinkGrade = 0.08f;
        /// <summary>Steepest average grade of the bank from a soft-edged promenade down to the water (m/m).</summary>
        public const float SoftGrade = 0.10f;

        protected override string Area => "Waterfront";
        public override string Id => "waterfront";
        public override string Icon => "Media/Game/Icons/LeisurePier01Medium.svg";
        public override float MinArea => 800f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            new FeatureInfo
            {
                Layer = BeachBarAddOn.LayerId, LabelKey = "Placemaker.Feature.cafeTerrace",
                Preset = ParkLayer.Bag(("tables", 8.0), ("umbrellas", false)),
            },
            // Area-wide features run on the dry land only ("core"): on the whole outline they planted picnic
            // shelters and trees in the water and puddle decals on it (art pass a3).
            Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), StreetShopsFeature(Area),
            CoreFeature("playground", ("size", "small")), CoreFeature("picnic"), CoreFeature("fountain"), CoreFeature("flowerbeds"),
            CoreFeature("bigtrees"), CoreFeature("decals", ("theme", "puddles")),
        };

        /// <summary>
        /// None: the layer plants its own beds and trees. The flower-bed feature scattered planter rows across the lawn
        /// (playtest 29) and big trees broke the avenue; both stay available as opt-in features.
        /// </summary>
        protected override string[] DefaultFeatures => new string[0];

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "promenade", ControlTier.Essential, "waterfront", Styles).Tip();
            c.Choice("waterSide", "auto", ControlTier.Essential, "waterfront", Shoreline.Sides).Tip();
            c.Slider("promWidth", 12, 6, 30, 1, "m", ControlTier.Essential, "waterfront").Tip();
            c.Toggle("railings", false, ControlTier.Essential, "waterfront").Tip();
            c.Choice("edge", "auto", ControlTier.Essential, "waterfront", Edges).Tip();   // s20

            c.Choice("surface", "paved", ControlTier.More, "promenade", "paved", "gravel", "grass", "none").Tip();
            c.Asset("surfaceAsset", AssetCategories.SurfacePaving, ControlTier.More, "promenade").When("surface=paved");
            c.Choice("pathWidth", "wide", ControlTier.More, "promenade", "wide", "narrow");
            c.Slider("lampSpacing", 20, 0, 60, 5, "m", ControlTier.More, "promenade").Layout(5).Tip();
            c.Slider("benchSpacing", 16, 0, 60, 2, "m", ControlTier.More, "promenade").Layout(5).Tip();
            c.Slider("kiosks", 1, 0, 6, 1, "count", ControlTier.More, "promenade");

            c.Asset("railingAsset", AssetCategories.Fence, ControlTier.More, "edge").When("railings=true");
            c.Toggle("moorings", false, ControlTier.More, "edge").Tip();
            c.Slider("mooringSpacing", 8, 4, 30, 1, "m", ControlTier.More, "edge").Layout(3).When("moorings=true");
            c.Toggle("riprap", false, ControlTier.More, "edge").Tip();
            c.Choice("shoreTexture", "auto", ControlTier.More, "edge", "auto", "sand", "dirt", "none").Tip();

            c.Choice("steps", "none", ControlTier.More, "steps", "none", "spots", "continuous").Tip();
            c.Slider("stepCount", 2, 1, 8, 1, "count", ControlTier.More, "steps").When("steps=spots");
            c.Slider("stepWidth", 10, 4, 40, 1, "m", ControlTier.More, "steps").When("steps=spots");
            c.Slider("stepLevels", 3, 2, 6, 1, "count", ControlTier.More, "steps").Tip();

            c.Slider("piers", 1, 0, 6, 1, "count", ControlTier.More, "piers").Tip();
            c.Slider("pierLength", 30, 8, 120, 2, "m", ControlTier.More, "piers");
            c.Toggle("pierHeads", false, ControlTier.More, "piers").Tip();

            c.Toggle("fishing", false, ControlTier.More, "harbor").Tip();
            c.Slider("fishingSpacing", 20, 8, 60, 2, "m", ControlTier.More, "harbor").Layout(5).When("fishing=true");
            c.Toggle("nautical", false, ControlTier.More, "harbor").Tip();

            c.Choice("trees", "row", ControlTier.More, "planting", "none", "row", "double").Tip();
            c.Slider("treeSpacing", 12, 6, 30, 1, "m", ControlTier.More, "planting").Layout(6);
            c.Choice("backdrop", "park", ControlTier.More, "planting", "lawn", "park", "none").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            // s20 (appended): the edge element, its height and assets, planted beds.
            c.Slider("deckHeight", 1.5, 0.6, 3, 0.1, "m", ControlTier.More, "edge").Tip();
            c.Asset("quayAsset", AssetCategories.NetQuay, ControlTier.More, "edge");
            c.Asset("boardwalkAsset", AssetCategories.NetPier, ControlTier.More, "edge");
            c.Toggle("beds", true, ControlTier.More, "planting").Tip();
            // s30 (appended): the land side of a promenade cut into a slope.
            c.Choice("hillside", "auto", ControlTier.More, "promenade", Hillsides).Tip();
            c.Slider("maxWall", 3, 1, 6, 0.5, "m", ControlTier.More, "promenade").Tip();
            // (appended) a bay carved into the land below the water level: the promenade follows the curved shore (ShoreCarve).
            c.Slider("bay", 0, 0, 40, 2, "m", ControlTier.Essential, "promenade").Tip();

            c.Fixed("waterSide");
            c.Fixed("edge");
            c.RandomRange("promWidth", 10, 18);
            c.RandomRange("piers", 0, 3);
            c.Chance("moorings", 0.3f);
            c.Chance("riprap", 0.2f);
            c.Chance("fishing", 0.4f);
            c.Chance("nautical", 0.4f);
        }

        protected override void DefinePresets()
        {
            // Preset ids are save format; labels and values changed in s20 (see Lang).
            // Promenade boardwalk: a timber boardwalk just off the bank, a lawn embankment up to a paved promenade
            // with benches facing the water, lamps between them, planted beds and a tree row, a park behind.
            Preset("promenade", ("style", "promenade"), ("edge", "boardwalk"), ("promWidth", 14.0), ("surface", "paved"), ("steps", "none"), ("trees", "row"),
                ("piers", 0.0), ("backdrop", "park"), ("benchSpacing", 14.0), ("lampSpacing", 21.0), ("beds", true), ("railings", true), ("deckHeight", 1.0), ("bay", 18.0));
            // Pier park: a walkway quay along the water, a promenade above a lawn bank, piers with T-heads starting
            // from the quay.
            Preset("pier", ("style", "pier"), ("edge", "walkway"), ("promWidth", 12.0), ("pathWidth", "wide"), ("surface", "paved"), ("steps", "none"), ("piers", 2.0),
                ("pierLength", 50.0), ("pierHeads", true), ("nautical", true), ("trees", "row"), ("benchSpacing", 12.0), ("backdrop", "park"), ("railings", true));
            // Harbour quay: a wide quay at the water, a paved apron at deck level with kiosks, lamps and benches, an avenue behind.
            // rf-beach: two T-headed piers off the quay (the quay used to block its own pier), bollards along it, a park behind instead of a bare lawn.
            Preset("harbor", ("style", "harbor"), ("edge", "quay"), ("promWidth", 22.0), ("railings", false), ("moorings", true), ("nautical", true), ("kiosks", 3.0),
                ("lampSpacing", 16.0), ("trees", "row"), ("treeSpacing", 14.0), ("piers", 2.0), ("pierLength", 36.0), ("pierHeads", true), ("steps", "none"), ("backdrop", "park"), ("deckHeight", 1.8), ("bay", 20.0));
            // River steps: a low walkway quay along the river, paved flights on the smooth bank down to it from a tree-lined promenade.
            Preset("riverSteps", ("style", "riverSteps"), ("edge", "walkway"), ("promWidth", 16.0), ("railings", true), ("steps", "spots"), ("stepCount", 3.0),
                ("stepWidth", 10.0), ("stepLevels", 4.0), ("trees", "double"), ("backdrop", "park"), ("lampSpacing", 22.0), ("piers", 0.0), ("deckHeight", 0.9));
            // Natural bank: no hard edge, a soft bank graded into the water with riprap, fishing spots at the water,
            // a gravel walk set back.
            Preset("fishing", ("style", "fishing"), ("edge", "bank"), ("promWidth", 10.0), ("surface", "gravel"), ("pathWidth", "narrow"), ("railings", false),
                ("riprap", true), ("fishing", true), ("fishingSpacing", 16.0), ("lampSpacing", 0.0), ("benchSpacing", 0.0), ("trees", "none"), ("backdrop", "park"),
                ("kiosks", 0.0), ("steps", "none"), ("piers", 1.0), ("pierLength", 16.0), ("beds", false));
        }

        protected override int WantedEntrances(LayerRegion region) => math.min(4, Entrances.WantedByArea(region.Area));

        // ───────────── planning ─────────────

        /// <summary>One promenade with its edge: the walk curve, the edge curve (null: natural bank) and the bank widths.</summary>
        private sealed class Front
        {
            public ShoreWalk Walk;
            /// <summary>Quay / walkway / boardwalk centre line at the deck height (null for a natural bank).</summary>
            public ShoreWalk Edge;
            /// <summary>Per walk sample: width of the bank seaward of the flat shoulder (natural bank / boardwalk) and behind it.</summary>
            public float[] SeaW, BackW;
            /// <summary>Hillside wall (land side kept at the original ground behind the walk's land edge).</summary>
            public bool Wall;
            /// <summary>Walk pieces actually laid as nets (trimmed where the field is not level across them).</summary>
            public readonly List<float2[]> Laid = new List<float2[]>();
        }

        private sealed class Work
        {
            public LayerContext Ctx;
            public Shoreline Shore;
            public ShoreGround G;
            public Occupancy Occ;
            public string Style, Edge;
            /// <summary>Walk net half width, promenade width, paved band extents seaward / landward of the walk centre, edge half width.</summary>
            public float Hw, P, Ps, Pl, EdgeHw;
            /// <summary>Signed offset of the walk net from the band centre curve (hillside wall: at the land edge).</summary>
            public float NetOff;
            public float Deck, Level;
            public bool Water, Wall;
            public float WallMax, WallLength;
            public readonly List<Front> Fronts = new List<Front>();
            public List<float2> Runs = new List<float2>(), CliffRuns = new List<float2>();
            public readonly List<float2[]> Lines = new List<float2[]>();
            public readonly List<Func<float, float>> Profiles = new List<Func<float, float>>();
            public readonly List<float2[]> EdgeLines = new List<float2[]>();
            public readonly List<(float2 a, float2 b, float2 c, float2 d)> EdgePieces = new List<(float2, float2, float2, float2)>();
            public readonly List<float2[]> Pads = new List<float2[]>();
            public int Steps, Piers, Benches, Lamps, Moorings, Fishing, Kiosks, Rocks, Railings, RailRuns, Beds, Lookouts, EdgeNets, Links;
            public float PierLength, EdgeLength;
            /// <summary>Walk arc-length windows (per front) kept free of furniture: link and pier roots, step flights.</summary>
            public readonly List<(Front f, float2 win)> Gaps = new List<(Front, float2)>();

            public bool InGap(Front f, float s, float pad = 0f)
            {
                foreach (var g in Gaps) if (g.f == f && s >= g.win.x - pad && s <= g.win.y + pad) return true;
                return false;
            }

            public bool InRun(float s)
            {
                foreach (var r in Runs) if (s >= r.x - 1f && s <= r.y + 1f) return true;
                return false;
            }
        }

        private static string EdgeOf(LayerContext ctx)
        {
            string e = ctx.S("edge");
            if (!string.IsNullOrEmpty(e) && e != "auto") return e;
            switch (ctx.S("style"))
            {
                case "harbor": return "quay";
                case "promenade": return "boardwalk";
                case "pier": case "riverSteps": return "walkway";
                default: return "bank";
            }
        }

        private static bool Hard(Work w) => w.Edge == "quay" || w.Edge == "walkway";

        protected override void PlanCore(LayerContext ctx)
        {
            var region0 = ctx.LayerRegion;
            var shore = Shoreline.Analyze(ctx, region0, ctx.S("waterSide"));
            if (shore == null) { ctx.Issue(IssueSeverity.Warning, Keys.NoSpace, NameKey); return; }
            // a bay first: the promenade and its edge are laid out along the curved waterline it makes
            if (ctx.F("bay") >= 3f && ShoreCarve.Bay(ctx, shore, ctx.F("bay"), 18f, Hashing.Mix(ctx.Seed, 0xBA7u), out _))
            {
                var carved = Shoreline.Analyze(ctx, region0, ctx.S("waterSide"));
                if (carved != null) shore = carved;
            }
            ctx.LayerRegion = shore.Land;
            var w = new Work { Ctx = ctx, Shore = shore, Style = ctx.S("style"), Edge = EdgeOf(ctx) };
            w.Water = shore.Mode != ShoreMode.Assumed && shore.HasWaterline;
            if (!w.Water && w.Edge != "bank") w.Edge = "bank";   // no water found: nothing for a quay or boardwalk to face
            // A 2 m node grid: the flat band under a 3–5 m walk and its shoulders is resolved (a 4 m grid smeared it into a cross slope).
            var g = w.G = ShoreGround.Create(ctx, shore, w.Edge == "bank" ? 10f : w.Edge == "boardwalk" ? 6f : EdgeReach, 2f);
            w.Occ = new Occupancy(shore.Land, ctx.Input.Reserved, ctx.Site);
            w.Occ.GroundOk = (p, r) => g.CanStand(p, r);
            var role = ctx.S("pathWidth") == "narrow" ? NetRole.Pathway : NetRole.PathwayWide;
            w.Hw = Composer.DefaultHalfWidth(role);
            w.Runs = g.Runs(k => k != ShoreKind.Cliff, 12f);
            w.CliffRuns = g.Runs(k => k == ShoreKind.Cliff, 16f);
            w.Level = g.HasLevel ? g.Level : g.Ref(shore.Length * 0.5f);
            switch (w.Edge)
            {
                case "quay": w.EdgeHw = 4f; break;
                case "walkway": w.EdgeHw = 1.5f; break;
                case "boardwalk": w.EdgeHw = 1.5f; break;
                default: w.EdgeHw = 0f; break;
            }
            w.Deck = w.Level + (w.Edge == "bank" ? 0.6f : ctx.F("deckHeight"));
            if (w.Edge == "walkway" || w.Edge == "boardwalk")
            {
                // A walkway or boardwalk sits at the bank's own level (≥ 0.8 m above the water, ≤ Deck height): on a low shore it
                // is not a dike the promenade has to climb over.
                var gs = new List<float>();
                for (float s = 0f; s <= shore.Length; s += 8f) { var wp = shore.WaterAt(s, out var inw); gs.Add(ctx.H(wp + inw * 3f)); }
                gs.Sort();
                if (gs.Count > 0) w.Deck = math.clamp(gs[gs.Count / 2] + 0.15f, w.Level + 0.8f, w.Deck);
            }
            w.P = math.max(ctx.F("promWidth"), w.Hw * 2f + 3f);
            w.Ps = w.Pl = w.P * 0.5f;
            ctx.Plan.Facts["promenadeWidth"] = w.P;
            ctx.Plan.Facts["edgeKind"] = Array.IndexOf(Edges, w.Edge);

            bool allCliff = shore.CliffShare >= ShoreGround.CliffShoreShare || w.Runs.Count == 0;
            if (shore.CliffShare > 0.05f)
                ctx.Issue(allCliff ? IssueSeverity.Warning : IssueSeverity.Info, ShoreGround.CliffKey, (int)math.round(shore.CliffShare * 100f));

            if (!allCliff)
            {
                Curves(w);
                ctx.Check();
                Terrain(w);
                ctx.Check();
                EdgeElement(w);
                Promenade(w, role);
                ctx.Check();
                Piers(w);
                Steps(w);
                Links(w);
                FishingSpots(w);
                ctx.Check();
            }
            CliffWalks(w);
            ConnectEntrances(w);
            if (!allCliff)
            {
                Ground(w);
                EdgeDressing(w);
                PromenadeFurniture(w);
                ctx.Check();
                LandSide(w);
            }
            Backdrop(w);

            g.FinishField(0.25f);
            BeachLayer.EmitGreenRegions(ctx, w.Occ, g);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Shoreline", System.Math.Round(shore.Length), Keys.UnitM);
            ctx.Stat(Keys.StatLamps, w.Lamps, Keys.UnitCount);
            if (w.Piers > 0) ctx.Stat("Placemaker.Stat.Piers", w.Piers, Keys.UnitCount);
            if (w.Steps > 0) ctx.Stat("Placemaker.Stat.Steps", w.Steps, Keys.UnitCount);
            if (w.Fishing > 0) ctx.Stat("Placemaker.Stat.FishingSpots", w.Fishing, Keys.UnitCount);
            var f = ctx.Plan.Facts;
            f["piers"] = w.Piers;
            f["pierLength"] = w.PierLength;
            f["steps"] = w.Steps;
            f["moorings"] = w.Moorings;
            f["railings"] = w.Railings;
            f["railingRuns"] = w.RailRuns;
            f["kiosks"] = w.Kiosks;
            f["lookouts"] = w.Lookouts;
            f["edgeNets"] = w.EdgeNets;
            f["edgeLength"] = w.EdgeLength;
            f["edgeRuns"] = w.EdgeLines.Count;
            f["edgeLinks"] = w.Links;
            f["beds"] = w.Beds;
            f["deck"] = w.Deck;
            f["hillsideWall"] = w.Wall ? 1 : 0;
            f["wallHeightMax"] = w.WallMax;
            f["wallLength"] = w.WallLength;
            Metrics(w);
        }

        private static float Yaw(float2 dir) => math.atan2(dir.x, dir.y);

        /// <summary>Facts for tests, the gallery and the debug dump: how smooth the walk and the edge came out.</summary>
        private static void Metrics(Work w)
        {
            float radius = float.PositiveInfinity, edgeRadius = float.PositiveInfinity, grade = 0f, walkLen = 0f;
            int wiggles = 0, edgeWiggles = 0;
            foreach (var fr in w.Fronts)
            {
                foreach (var l in fr.Laid) { radius = math.min(radius, ShoreWalk.MinRadius(l)); wiggles += ShoreWalk.Wiggles(l); walkLen += Shoreline.LengthOf(l); }
                grade = math.max(grade, ShoreWalk.MaxGrade(fr.Walk.H, fr.Walk.S));
                if (fr.Edge != null) { edgeRadius = math.min(edgeRadius, ShoreWalk.MinRadius(fr.Edge.Line)); edgeWiggles += ShoreWalk.Wiggles(fr.Edge.Line); }
            }
            var f = w.Ctx.Plan.Facts;
            f["walks"] = w.Fronts.Count;
            f["walkLength"] = walkLen;
            f["walkMinRadius"] = math.isfinite(radius) ? radius : 9999f;
            f["walkWiggles"] = wiggles;
            f["walkMaxGrade"] = grade;
            f["edgeMinRadius"] = math.isfinite(edgeRadius) ? edgeRadius : 9999f;
            f["edgeWiggles"] = edgeWiggles;
        }

        // ───────────── curves ─────────────

        /// <summary>
        /// The walk curve(s) and the edge curve(s) per stretch of shore that is not cliff. A hard edge (quay, walkway) is
        /// laid first along the water (its sea side on the waterline) and the walk set back behind it far enough for a
        /// ≤ 8 % bank; a soft edge (bank, boardwalk) lays the walk first, set back so its bank to the water stays ≤ ~30 %.
        /// </summary>
        private static void Curves(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G;
            float sh = Shoulder;
            Func<float2, bool> onLand = p => shore.Land.ContainsDisc(p, w.Hw + 1f) && !g.OnCliff(p);
            foreach (var run in w.Runs)
            {
                if (Hard(w))
                {
                    // The quay: its centre half a deck inland of the waterline, smoothed, at most EdgeReach past the outline.
                    Func<float2, bool> edgeOk = p => !g.OnCliff(p) && (shore.Outline.Contains(p) || shore.Depth(p) <= EdgeReach);
                    foreach (var line in Longest(SmoothFromWater(shore, w.EdgeHw - 0.4f, run, 9f, null, edgeOk, 30f)))
                    {
                        var edge = ShoreWalk.Of(line);
                        if (edge == null) continue;
                        for (int i = 0; i < edge.H.Length; i++) edge.H[i] = w.Deck;
                        // The walk behind it: rise from the deck to the ground there, kept to a band; the bank between ≤ 8 %.
                        float minRise = w.Style == "riverSteps" ? 1.5f : w.Edge == "quay" ? 0f : 0.3f;
                        float maxRise = w.Edge == "quay" ? 0f : 2.5f;
                        float probe = w.EdgeHw + 1f + 8f + sh + w.Ps;
                        var rises = new List<float>();
                        foreach (var p in edge.Offset(probe, 6f)) rises.Add(ctx.H(p) - w.Deck);
                        rises.Sort();
                        float rise = rises.Count > 0 ? math.clamp(rises[rises.Count / 2], minRise, maxRise) : minRise;
                        float bankW = rise <= 0.05f ? 0f : math.max(6f, rise / LinkGrade);
                        float d = w.EdgeHw + 1f + bankW + sh + w.Ps;
                        foreach (var piece in Longest(Cut(ShoreWalk.TrimHooks(SmoothOffset(edge, d), ShoreWalk.MinCurveRadius), onLand, 24f)))
                        {
                            var walk = ShoreWalk.Of(piece);
                            if (walk == null) continue;
                            // Per sample: the room for the bank → highest walk level that keeps it ≤ 8 %.
                            var room = new float[walk.Line.Length];
                            for (int i = 0; i < room.Length; i++)
                            {
                                float u = edge.Signed(walk.Line[i], out _, out _);
                                room[i] = math.max(0f, u - (w.EdgeHw + 1f) - sh - w.Ps);
                            }
                            walk.Profile(ctx.H, w.Hw + 1f, 40f, 0.02f,
                                s => w.Deck + math.min(minRise, Room(room, walk, s) * LinkGrade),
                                s => w.Deck + math.min(maxRise, Room(room, walk, s) * LinkGrade));
                            w.Fronts.Add(new Front { Walk = walk, Edge = edge });
                        }
                    }
                }
                else
                {
                    // Natural bank / boardwalk: the walk a smooth line near the water, low: its level is the ground there, but never
                    // more than a ≤ 10 % bank above the water in front of it (so a straight link can run down it); where the ground
                    // behind rises higher, the hillside is cut (a graded bank or a retaining wall), the promenade does not climb.
                    float lo = w.Water ? w.Level + 0.8f : float.MinValue;
                    Func<float2, float> minDw = p => w.Ps + sh + 5f;
                    foreach (var line in Longest(SmoothFromWater(shore, w.Ps + sh + 8f, run, 24f, w.Water ? minDw : null, onLand, 24f)))
                    {
                        var walk = ShoreWalk.Of(line);
                        if (walk == null) continue;
                        var room = new float[walk.Line.Length];
                        for (int i = 0; i < room.Length; i++) room[i] = w.Water ? math.max(0f, shore.FromWater(walk.Line[i]) - w.Ps - sh) : 1e4f;
                        room = ShoreWalk.SmoothProfile(room, walk.S, 20f);
                        walk.Profile(ctx.H, w.Hw + 1f, 40f, 0.02f, _ => lo, s => w.Water ? w.Level + 0.4f + SoftGrade * Room(room, walk, s) : float.MaxValue);
                        var fr = new Front { Walk = walk };
                        if (w.Edge == "boardwalk") fr.Edge = BoardwalkCurve(w, walk);
                        w.Fronts.Add(fr);
                    }
                }
            }
            // Banks: seaward (natural bank / boardwalk) to the waterline, behind to the original ground; the hillside decision.
            float wallMax = 0f, wallLen = 0f, maxWall = ctx.F("maxWall");
            foreach (var fr in w.Fronts)
            {
                var walk = fr.Walk; int n = walk.Line.Length;
                fr.SeaW = new float[n]; fr.BackW = new float[n];
                for (int i = 0; i < n; i++)
                {
                    var p = walk.At(walk.S[i], out _, out var nrm);
                    fr.SeaW[i] = math.max(5f, (w.Water ? shore.FromWater(p) : 30f) - w.Ps - sh + 2f);
                    // Cut banks up to ~22 %, fill banks (a promenade raised over low ground) gentler, ~15 %: no plinth.
                    float rise = ctx.H(p + nrm * (w.Pl + sh + 18f)) - walk.H[i];
                    fr.BackW[i] = math.clamp(math.abs(rise) / (rise >= 0f ? 0.22f : 0.15f), 8f, 45f);
                    float wallH = ctx.H(p + nrm * (w.Pl + 1.5f)) - walk.H[i];
                    if (wallH > 0.5f) wallLen += i > 0 ? walk.S[i] - walk.S[i - 1] : 0f;
                    wallMax = math.max(wallMax, wallH);
                }
                fr.SeaW = ShoreWalk.SmoothProfile(fr.SeaW, walk.S, 16f);
                fr.BackW = ShoreWalk.SmoothProfile(fr.BackW, walk.S, 20f);
            }
            string hill = ctx.S("hillside");
            bool wall = hill == "wall" ? wallMax <= maxWall : hill == "auto" && wallMax >= 1.5f && wallMax <= maxWall;
            if (hill == "wall" && wallMax > maxWall) ctx.Issue(IssueSeverity.Info, "Placemaker.Waterfront.Issue.WallTooHigh", (int)math.round(wallMax), (int)math.round(maxWall));
            w.Wall = wall && w.Fronts.Count > 0;
            w.WallMax = w.Wall ? wallMax : 0f;
            w.WallLength = w.Wall ? wallLen : 0f;
            if (w.Wall)
            {
                // The wall stands at the paved band's land edge, and the walk net runs along it (its own cut makes the wall); lamps go to the sea side.
                w.NetOff = w.Pl - w.Hw;
                foreach (var fr in w.Fronts) fr.Wall = true;
            }
            ctx.Plan.Facts["hillsideRise"] = wallMax;
        }

        /// <summary>ONE promenade (one edge) per stretch of shore: the longest piece; stubs cut off by the outline's corners are dropped.</summary>
        private static List<float2[]> Longest(List<float2[]> pieces)
        {
            float2[] best = null;
            foreach (var p in pieces) if (best == null || Shoreline.LengthOf(p) > Shoreline.LengthOf(best)) best = p;
            return best == null ? new List<float2[]>() : new List<float2[]> { best };
        }

        private static float Room(float[] room, ShoreWalk walk, float s)
        {
            int i = (int)math.clamp(math.round(s / ShoreWalk.Spacing), 0, room.Length - 1);
            return room[i];
        }

        private static List<float2[]> SmoothFromWater(Shoreline shore, float dw, float2 run, float sigma, Func<float2, float> minDw, Func<float2, bool> keep, float minLength) =>
            ShoreWalk.SmoothFromWater(shore, dw, run, sigma, minDw, keep, minLength);

        /// <summary>A curve offset from another, then smoothed so the inside of a bend does not tighten below MinRadius.</summary>
        private static float2[] SmoothOffset(ShoreWalk c, float d)
        {
            var line = c.Offset(d);
            float sigma = 6f;
            for (int k = 0; k < 4 && ShoreWalk.MinRadius(line) < MinRadius; k++) { line = ShoreWalk.SmoothLine(line, sigma, 2); sigma *= 1.6f; }
            return line;
        }

        private static List<float2[]> Cut(float2[] line, Func<float2, bool> keep, float minLength)
        {
            var result = new List<float2[]>();
            var run = new List<float2>();
            void Flush()
            {
                if (run.Count >= 2 && Shoreline.LengthOf(run.ToArray()) >= minLength) result.Add(run.ToArray());
                run.Clear();
            }
            foreach (var p in line) { if (keep(p)) run.Add(p); else Flush(); }
            Flush();
            return result;
        }

        /// <summary>
        /// The boardwalk's line: parallel to the walk at the (smoothed) distance of the water plus 2.6 m, i.e. just off the
        /// bank over the water, then smoothed to ≥ MinRadius. One line; the pieces that end up over dry ground are dropped.
        /// </summary>
        private static ShoreWalk BoardwalkCurve(Work w, ShoreWalk walk)
        {
            var shore = w.Shore;
            int n = walk.Line.Length;
            var dist = new float[n];
            for (int i = 0; i < n; i++) dist[i] = shore.FromWater(walk.Line[i]) + 2.6f;
            dist = ShoreWalk.SmoothProfile(dist, walk.S, 40f);
            var pts = new List<float2>(n);
            for (int i = 0; i < n; i++) { var p = walk.At(walk.S[i], out _, out var nrm); pts.Add(p - nrm * dist[i]); }
            var line = ShoreWalk.SmoothLine(Shoreline.RemoveLoops(pts), 16f, 3);
            for (int k = 0; k < 3 && ShoreWalk.MinRadius(line) < MinRadius; k++) line = ShoreWalk.SmoothLine(line, 24f, 2);
            // Keep the longest stretch over the water (a deck over dry ground would sit on it).
            float2[] best = null;
            foreach (var piece in Cut(line, p => shore.IsWet(p) || w.Ctx.H(p) < w.Deck - 0.6f, 20f))
                if (best == null || Shoreline.LengthOf(piece) > Shoreline.LengthOf(best)) best = piece;
            var edge = ShoreWalk.Of(best);
            if (edge != null) for (int i = 0; i < edge.H.Length; i++) edge.H[i] = w.Deck;
            return edge;
        }

        // ───────────── terrain ─────────────

        /// <summary>
        /// The ground as ONE field over the sculpt grid: per node the signed distance from the walk (and from the edge),
        /// the walk height at the closest point and the original ground → target. Flat across the paved band and its
        /// shoulders; a smoothstep bank to the edge level (hard edges: deck level under and just behind the quay; the bed in
        /// front untouched) or to the original ground at the water (soft edges); behind, a smoothstep bank back to the
        /// original ground or — hillside wall — the original ground kept from the walk's land edge on (the net cut makes the
        /// wall). Beyond the walk's ends the change fades out; at the outline it is capped to ≤ 25 % (FinishField).
        /// </summary>
        private static void Terrain(Work w)
        {
            var g = w.G; var sc = g.Sculpt;
            if (w.Fronts.Count == 0) return;
            float sh = Shoulder;
            for (int k = 0; k < sc.Count; k++)
            {
                if (!sc.Free[k]) continue;
                var p = sc.Pos(k);
                float h0 = sc.Base[k];
                // The nearest front decides (normally there is one).
                Front fr = null; float u = 0f, s = 0f, beyond = 0f, best = float.MaxValue;
                foreach (var f in w.Fronts)
                {
                    float uu = f.Walk.Signed(p, out float ss, out float bb);
                    float d = math.abs(uu) + bb * 2f;
                    if (d < best) { best = d; fr = f; u = uu; s = ss; beyond = bb; }
                }
                float t = Target(w, fr, p, u, s, h0, sh);
                if (!math.isfinite(t)) t = h0;   // the ground stays (also on a re-run of the field)
                // Past the walk's ends the change dies out over ~20 m, and over the last ~12 m of arc length before a cliff stretch (the
                // cliff keeps its own ground and its own cliff-top walk); the outline cap does the rest. (Not by distance outside the
                // walk's own stretch: the shore's arc length jumps at the outline's corners, and the fade stepped there.)
                float along = g.AlongOf(k), toCliff = 0f;
                foreach (var r in w.CliffRuns) toCliff = math.max(toCliff, math.saturate((math.min(along - r.x, r.y - along) + 12f) / 12f));
                float fade = math.max(beyond / 20f, toCliff);
                if (fade > 0f) t = math.lerp(t, h0, Sculpt.Smooth01(fade));
                sc.Target[k] = t;
            }
            // No smoothing pass: the smoothstep banks already meet the flat band with zero slope, and a pass would tilt the band.
            sc.CapToBorder(0.25f);
        }

        private static float Target(Work w, Front fr, float2 p, float u, float s, float h0, float sh)
        {
            var walk = fr.Walk;
            float H = walk.HeightAt(s);
            int i = (int)math.clamp(math.round(s / ShoreWalk.Spacing), 0, walk.Line.Length - 1);
            // Hillside wall: the original ground is kept from the walk net's centre line on, so the step lies under the net (its own cut
            // renders the retaining wall at its land edge, as with a road built into a slope) — never on open ground.
            float flatS = w.Ps + sh, flatL = fr.Wall ? w.NetOff + w.Hw * 0.5f : w.Pl + sh;
            if (u >= -flatS && u <= flatL) return H;
            if (u > flatL)
            {
                if (fr.Wall && h0 >= H) return float.NaN;   // the hillside stays: the walk's own cut makes the wall
                return math.lerp(H, h0, Sculpt.Smooth01((u - flatL) / fr.BackW[i]));
            }
            float a = -u - flatS;   // metres seaward of the sea shoulder
            float sea = fr.SeaW[i];
            float soft = a >= sea ? h0 : math.lerp(H, h0, Sculpt.Smooth01(a / sea));
            if (Hard(w) && fr.Edge != null)
            {
                float ue = fr.Edge.Signed(p, out _, out float eb);
                float landEdge = w.EdgeHw + 1f, hard;
                // Under the deck and its land side: deck level; in front of the wall: the bed stays.
                // (the fill eases down to the bed under the deck's outer half, ending at the wall line: on a 2 m node grid a sharp
                // deck-to-bed edge stair-stepped out past the wall)
                if (ue <= landEdge) hard = math.lerp(w.Deck, math.min(h0, w.Deck), Sculpt.Smooth01((-ue + (w.EdgeHw - 2.5f)) / 2.5f));
                // Between the quay's land edge and the walk's sea shoulder: one smooth bank, measured between the two curves.
                else hard = math.lerp(w.Deck, H, Sculpt.Smooth01((ue - landEdge) / math.max(0.5f, (ue - landEdge) + a)));
                // Past the quay's ends the soft bank takes over within 20 m (no deck fill without its quay wall).
                return eb > 0f ? math.lerp(hard, soft, Sculpt.Smooth01(eb / 20f)) : hard;
            }
            return a >= sea ? float.NaN : soft;
        }

        // ───────────── edge element ─────────────

        private static void Deck(LayerContext ctx, (float2 a, float2 b, float2 c, float2 d) bz, float y, NetRole role, AssetRef asset, string category)
        {
            ctx.Plan.Nets.Add(new PlannedNet
            {
                A = new float3(bz.a.x, y, bz.a.y), B = new float3(bz.b.x, y, bz.b.y), C = new float3(bz.c.x, y, bz.c.y), D = new float3(bz.d.x, y, bz.d.y),
                Role = role, Asset = asset, FollowTerrain = false, Category = category,
            });
        }

        private static (float2, float2, float2, float2) Straight(float2 a, float2 b) => (a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b);

        /// <summary>
        /// The quay / walkway quay / boardwalk as ONE continuous run along its smooth curve at one deck height: short straight
        /// pieces (~8 m) sharing their nodes (quay and pier prefabs are built straight; a smooth curve of short chords turns
        /// a few degrees per node), never interrupted by links or piers — they join it at nodes.
        /// </summary>
        private static void EdgeElement(Work w)
        {
            var ctx = w.Ctx; var occ = w.Occ;
            if (w.Edge == "bank") return;
            bool board = w.Edge == "boardwalk";
            var role = w.Edge == "quay" ? NetRole.PathwayWide : NetRole.Pathway;
            string cat = board ? AssetCategories.NetPier : AssetCategories.NetQuay;
            var ids = board ? WaterfrontArt.Or(ctx.S("boardwalkAsset"), WaterfrontArt.Boardwalk)
                : WaterfrontArt.Or(ctx.S("quayAsset"), w.Edge == "quay" ? WaterfrontArt.QuayWide : WaterfrontArt.QuayNarrow);
            var asset = new AssetRef("", ids);
            if (!board)
            {
                // A quay ends where the ground behind it is no longer at its deck (near the outline the field has to fade out):
                // never a free-standing quay end with its wall exposed on the land side. The longest such stretch is kept and the
                // field is laid again around it (past its ends the soft bank takes over).
                bool trimmed = false;
                var map = new Dictionary<ShoreWalk, ShoreWalk>();
                foreach (var fr in w.Fronts)
                {
                    if (fr.Edge == null) continue;
                    if (!map.TryGetValue(fr.Edge, out var te))
                    {
                        var e = fr.Edge;
                        float2[] best = null;
                        foreach (var piece in Cut(e.Line, p => { e.Signed(p, out float es, out _); e.At(es, out _, out var en); return math.abs(w.G.H(p + en * (w.EdgeHw + 0.6f)) - w.Deck) < 0.3f; }, 16f))
                            if (best == null || Shoreline.LengthOf(piece) > Shoreline.LengthOf(best)) best = piece;
                        te = best == null ? null : best.Length >= e.Line.Length - 1 ? e : ShoreWalk.Of(best);
                        if (te != null && te != e) for (int i = 0; i < te.H.Length; i++) te.H[i] = w.Deck;
                        trimmed |= te != e;
                        map[e] = te;
                    }
                    fr.Edge = te;
                }
                if (trimmed) Terrain(w);
            }
            var done = new HashSet<ShoreWalk>();
            foreach (var fr in w.Fronts)
            {
                if (fr.Edge == null || !done.Add(fr.Edge)) continue;
                var src = fr.Edge.Line;
                var line = Chords(src, 8f);
                for (int i = 1; i < line.Length; i++)
                {
                    var bz = Straight(line[i - 1], line[i]);
                    Deck(ctx, bz, w.Deck, role, asset, cat);
                    w.EdgePieces.Add(bz);
                    w.EdgeNets++;
                    occ.TakeCorridor(line[i - 1], line[i], w.EdgeHw + 0.3f, 100);
                    if (!board) occ.Paths.Add(line[i - 1], line[i], w.EdgeHw, 100);
                }
                w.EdgeLength += Shoreline.LengthOf(line);
                w.EdgeLines.Add(line);
            }
        }

        /// <summary>A polyline resampled to equal chords of about <paramref name="chord"/> m (end points kept).</summary>
        private static float2[] Chords(float2[] line, float chord)
        {
            float len = Shoreline.LengthOf(line);
            int n = math.max(1, (int)math.round(len / chord));
            var pts = new float2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = Shoreline.PointOn(line, len * i / n, out _);
            return pts;
        }

        // ───────────── promenade ─────────────

        /// <summary>
        /// The walk as smooth Bézier courses along its curve, only where the planned ground is level across it (the ends,
        /// where the field fades to the border, are left off rather than built on a slope).
        /// </summary>
        private void Promenade(Work w, NetRole role)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            foreach (var fr in w.Fronts)
            {
                var walk = fr.Walk;
                var run = new List<float2>();
                void Flush()
                {
                    if (run.Count >= 2 && Shoreline.LengthOf(run.ToArray()) >= 16f)
                    {
                        var line = run.ToArray();
                        var curves = Shoreline.Curves(shore.Land, line, role, 30f);
                        if (curves.Count > 0)
                        {
                            ParkPaths.Index(curves, occ.Corridors, 100);
                            ParkPaths.Index(curves, occ.Paths, 100);
                            int first = ctx.Plan.Nets.Count;
                            ParkPaths.Emit(ctx, curves, NoGates, asset);
                            if (fr.Wall)
                                // Hillside wall: the walk is built at its own level like a road planted on a slope; where the hillside is
                                // higher, the game cuts it and draws the retaining wall along the walk's land edge.
                                for (int ni = first; ni < ctx.Plan.Nets.Count; ni++)
                                {
                                    var net = ctx.Plan.Nets[ni];
                                    net.FollowTerrain = false;
                                    net.A.y = WalkY(walk, net.A.xz); net.B.y = WalkY(walk, net.B.xz); net.C.y = WalkY(walk, net.C.xz); net.D.y = WalkY(walk, net.D.xz);
                                }
                            fr.Laid.Add(line);
                            w.Lines.Add(line);
                            w.Profiles.Add(walk.HeightAt);
                        }
                    }
                    run.Clear();
                }
                for (int i = 0; i < walk.Line.Length; i++)
                {
                    var p = walk.At(walk.S[i], out _, out var nrm) + nrm * w.NetOff;
                    float h = walk.H[i];
                    bool level = (fr.Wall || math.abs(g.H(p) - h) < 0.12f && math.abs(g.H(p + nrm * w.Hw) - h) < 0.15f) && math.abs(g.H(p - nrm * w.Hw) - h) < 0.15f;
                    if (level && shore.Land.ContainsDisc(p, w.Hw + 0.5f)) run.Add(p); else Flush();
                }
                Flush();
            }
        }

        private static float WalkY(ShoreWalk walk, float2 p) { walk.Signed(p, out float s, out _); return walk.HeightAt(s); }

        /// <summary>Cliff stretches: a walk on the cliff top with lookouts (the cliff face stays natural).</summary>
        private static void CliffWalks(Work w)
        {
            var ctx = w.Ctx;
            var bench = AssetMix.Pick(WaterfrontArt.Or(ctx.S("benchAsset"), WaterfrontArt.Benches), 0u);
            var lamp = AssetMix.Pick(WaterfrontArt.Or(ctx.S("lampAsset"), WaterfrontArt.Lamps), 0u);
            var rail = AssetMix.Pick(WaterfrontArt.Or(ctx.S("railingAsset"), WaterfrontArt.Railings), 0u);
            foreach (var run in w.CliffRuns)
            {
                var res = CliffTop.Plan(ctx, w.G, w.Occ, run.x, run.y, NetRole.Pathway, AssetMix.One(ctx.S("pathAsset")), bench, rail, lamp,
                    AssetMix.Pick(WaterfrontArt.Landing, 0u), 60f);
                w.Lookouts += res.Lookouts;
                w.Railings += res.Railings;
                w.Lines.AddRange(res.Lines);
                foreach (var _ in res.Lines) w.Profiles.Add(null);
            }
        }

        /// <summary>Entrances from the land side join the nearest walk on one constant-grade ramp each.</summary>
        private void ConnectEntrances(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            var walks = new List<float2[]>(w.Lines);
            if (walks.Count == 0 && w.Edge != "boardwalk") walks.AddRange(w.EdgeLines);
            if (walks.Count == 0) return;
            var gates = new List<float2>();
            foreach (var gt in Gates(ctx))
                if (shore.FromWater(gt.Position) > w.P * 0.5f) gates.Add(gt.Position);
            w.G.ConnectAll(w.Occ, gates, walks, NetRole.Pathway, AssetMix.One(ctx.S("pathAsset")), w.Fronts.Count > 0 ? w.Pl - w.NetOff + Shoulder : 0f);
        }

        /// <summary>Closest point on a set of Bézier pieces (16 samples each).</summary>
        private static float2 ClosestOn(List<(float2 a, float2 b, float2 c, float2 d)> pieces, float2 p, out float dist)
        {
            float best = float.MaxValue; float2 bp = p;
            foreach (var z in pieces)
            {
                float2 prev = z.a;
                for (int i = 1; i <= 16; i++)
                {
                    float t = i / 16f, u = 1f - t;
                    var q = u * u * u * z.a + 3f * u * u * t * z.b + 3f * u * t * t * z.c + t * t * t * z.d;
                    var c = SegmentIndex.ClosestPoint(p, prev, q);
                    float d = math.distance(c, p);
                    if (d < best) { best = d; bp = c; }
                    prev = q;
                }
            }
            dist = best;
            return bp;
        }

        /// <summary>Closest point on the laid walk pieces of a front (exact on the Bézier courses' polyline).</summary>
        private static bool OnLaid(Front fr, float2 p, out float2 q)
        {
            float best = float.MaxValue; q = p;
            foreach (var l in fr.Laid)
                for (int i = 1; i < l.Length; i++)
                {
                    var c = SegmentIndex.ClosestPoint(p, l[i - 1], l[i]);
                    float d = math.distance(c, p);
                    if (d < best) { best = d; q = c; }
                }
            return best < 1.5f;
        }

        /// <summary>
        /// Straight link paths from the walk down the smooth bank to the edge (quay / walkway: onto its deck; boardwalk: to
        /// the bank foot at deck level and a short level timber stub), every ~50 m, only where the planned bank along the
        /// link is no steeper than ~14 % anywhere — no ramp is cut for it.
        /// </summary>
        private static void Links(Work w)
        {
            var ctx = w.Ctx; var g = w.G; var occ = w.Occ;
            if (w.Edge == "bank" || w.EdgePieces.Count == 0) return;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            bool board = w.Edge == "boardwalk";
            var pierAsset = new AssetRef("", WaterfrontArt.Or(ctx.S("boardwalkAsset"), WaterfrontArt.Boardwalk));
            float spacing = w.Edge == "quay" ? 60f : 48f;
            foreach (var fr in w.Fronts)
            {
                if (fr.Edge == null) continue;
                var walk = fr.Walk;
                int n = math.max(1, (int)math.floor(walk.Length / spacing));
                for (int k = 0; k < n; k++)
                {
                    float s = (k + 0.5f) * walk.Length / n;
                    // Slide off a pier root or a step flight rather than skip the link.
                    foreach (float off in new[] { 0f, 14f, -14f, 22f, -22f })
                        if (!w.InGap(fr, s + off, 8f) && s + off > 8f && s + off < walk.Length - 8f) { s += off; break; }
                    if (w.InGap(fr, s, 8f)) continue;
                    var c = walk.At(s, out _, out var nrm);
                    if (!OnLaid(fr, c + nrm * w.NetOff, out var top)) continue;
                    float hTop = g.H(top);
                    float2 foot, deckP = default;
                    if (board)
                    {
                        // From the boardwalk straight inland to where the bank reaches the deck level: the link runs down the bank
                        // to there and a short level timber stub crosses to the boardwalk (a T junction on it).
                        deckP = ClosestOn(w.EdgePieces, ProjectToEdge(fr, top), out float dd);
                        if (dd > 3f) continue;
                        var inl = math.normalizesafe(top - deckP);
                        foot = deckP; bool found = false;
                        for (float d = 1.5f; d < 10f; d += 0.25f)
                        {
                            var q = deckP + inl * d;
                            if (g.H(q) >= w.Deck - 0.05f) { foot = q; found = true; break; }
                        }
                        if (!found || math.distance(foot, top) < w.Hw + 2f) continue;
                    }
                    else
                    {
                        foot = ClosestOn(w.EdgePieces, ProjectToEdge(fr, top), out float dFoot);
                        if (dFoot > 3f) continue;
                    }
                    if (!Gentle(g, top - nrm * w.Hw, foot, 0.17f)) continue;
                    // Clear of piers, steps, kiosks and each other.
                    bool blocked = false;
                    float len = math.distance(top, foot);
                    for (float t = w.Hw + 1f; t < len - 2f; t += 1.5f)
                    {
                        var q = math.lerp(top, foot, t / len);
                        if (occ.Discs.Overlaps(q, 1.2f, 0.1f) || occ.HitsPolygon(q, 1.2f)) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    ctx.Plan.Nets.Add(new PlannedNet
                    {
                        A = new float3(top.x, hTop, top.y), B = new float3(math.lerp(top, foot, 1f / 3f).x, 0f, math.lerp(top, foot, 1f / 3f).y),
                        C = new float3(math.lerp(top, foot, 2f / 3f).x, 0f, math.lerp(top, foot, 2f / 3f).y), D = new float3(foot.x, w.Deck, foot.y),
                        Role = NetRole.Pathway, Asset = asset, FollowTerrain = true,
                    });
                    occ.TakeCorridor(top, foot, 1.6f, 100);
                    occ.Paths.Add(top, foot, 1.5f, 100);
                    if (board) Deck(ctx, Straight(foot, deckP), w.Deck, NetRole.Pathway, pierAsset, AssetCategories.NetPier);
                    w.Gaps.Add((fr, new float2(s - 3f, s + 3f)));
                    w.Links++;
                }
            }
        }

        /// <summary>The point on the front's edge curve straight seaward of a walk point.</summary>
        private static float2 ProjectToEdge(Front fr, float2 top)
        {
            fr.Edge.Signed(top, out float se, out _);
            return fr.Edge.At(se, out _, out _);
        }

        /// <summary>The planned ground along a straight line never climbs steeper than <paramref name="max"/>.</summary>
        private static bool Gentle(ShoreGround g, float2 a, float2 b, float max)
        {
            float len = math.distance(a, b);
            if (len < 1f) return true;
            int n = math.max(2, (int)math.ceil(len / 1.5f));
            float prev = g.H(a);
            for (int i = 1; i <= n; i++)
            {
                float h = g.H(math.lerp(a, b, i / (float)n));
                if (math.abs(h - prev) / (len / n) > max) return false;
                prev = h;
            }
            return true;
        }

        // ───────────── piers, steps, fishing ─────────────

        /// <summary>
        /// Piers as T junctions: on a quay, walkway or boardwalk they start ON the edge (a node the composer splits, so its
        /// railing opens there) and run straight out at the deck level; on a natural bank a straight path runs down the
        /// smooth bank to where the ground reaches the deck level and the pier starts there. Inside the outline, over water.
        /// </summary>
        private static void Piers(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G; var occ = w.Occ;
            int wanted = ctx.I("piers");
            if (wanted <= 0) return;
            // (rf-beach) A shore drawn along the water (Edge) gets its piers too: they reach out past the outline over the real
            // water (the site's water snapshot reaches 60 m beyond it). In the game runner a waterfront drawn to the waterline
            // got no piers at all, so "Pier park" built none.
            bool beyond = shore.Mode == ShoreMode.Edge && w.Water;
            if (shore.Mode != ShoreMode.Inside && !beyond)
            {
                ctx.Issue(IssueSeverity.Info, "Placemaker.Waterfront.Issue.PiersNeedWater");
                return;
            }
            var pierAsset = new AssetRef("", WaterfrontArt.Or(ctx.S("boardwalkAsset"), WaterfrontArt.Pier));
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            float maxLen = ctx.F("pierLength");
            bool heads = ctx.B("pierHeads");
            float total = 0f; foreach (var fr in w.Fronts) total += fr.Walk.Length;
            if (total <= 0f) return;
            int placed = 0;
            for (int k = 0; k < wanted; k++)
            {
                // Spread over all fronts by arc length.
                float at = total * (k + 0.5f) / wanted, acc = 0f;
                Front fr = null; float s = 0f;
                foreach (var f in w.Fronts) { if (at <= acc + f.Walk.Length) { fr = f; s = at - acc; break; } acc += f.Walk.Length; }
                if (fr == null || fr.Walk.Length < 30f) continue;
                s = math.clamp(s, 14f, fr.Walk.Length - 14f);
                var c = fr.Walk.At(s, out var tan, out var nrm);
                float2 root, dir; float deck;
                if (fr.Edge != null)
                {
                    fr.Edge.Signed(c, out float se, out float eb);
                    if (eb > 0f) continue;
                    root = fr.Edge.At(se, out var et, out var en);
                    dir = -en; deck = w.Deck;
                }
                else
                {
                    if (!OnLaid(fr, c + nrm * w.NetOff, out var top)) continue;
                    // Down the smooth bank to where the planned ground is a little above the water.
                    deck = w.Level + 1.0f;
                    float2 foot = top; bool found = false;
                    for (float d = w.Ps + Shoulder; d < 60f; d += 0.5f)
                    {
                        var q = top - nrm * d;
                        if (g.H(q) <= deck + 0.05f) { foot = q; found = true; break; }
                    }
                    if (!found || !Gentle(g, top - nrm * w.Hw, foot, 0.17f)) continue;
                    LeisureKit.Path(ctx, occ, top, foot, NetRole.Pathway, pathAsset);
                    root = foot; dir = -nrm;
                }
                float len = 0f;
                for (float l = 2f; l <= maxLen; l += 2f)
                {
                    var q = root + dir * l;
                    if (!(beyond || shore.Outline.ContainsDisc(q, 3f)) || !shore.IsWet(q))
                    {
                        // (rf-beach) the first metres may still be under the edge deck itself (a wide quay's centre line is
                        // on land): look past its width before giving up. The harbour's pier was never built for this.
                        if (l <= w.EdgeHw + 2f && len == 0f) continue;
                        break;
                    }
                    len = l;
                }
                if (len < 8f) continue;
                var end = root + dir * len;
                Deck(ctx, Straight(root, end), deck, NetRole.Pathway, pierAsset, AssetCategories.NetPier);
                float length = len;
                if (heads)
                {
                    var side = new float2(-dir.y, dir.x);
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        var h = end + side * sgn * 8f;
                        if ((beyond || shore.Outline.ContainsDisc(h, 1.5f)) && shore.IsWet(h))
                        {
                            Deck(ctx, Straight(end, h), deck, NetRole.Pathway, pierAsset, AssetCategories.NetPier);
                            length += 8f;
                        }
                    }
                }
                occ.TakeCorridor(root, end, 2.2f, 100);
                w.Gaps.Add((fr, new float2(s - 5f, s + 5f)));
                w.Piers++;
                w.PierLength += length;
                placed++;
            }
        }

        /// <summary>
        /// River steps: paved flights ON the smooth bank between the promenade and the walkway quay (paving draped on the
        /// graded slope, a straight path down the middle) — no level patches cut into the bank.
        /// </summary>
        private static void Steps(Work w)
        {
            var ctx = w.Ctx; var g = w.G;
            string mode = ctx.S("steps");
            if (mode == "none" || !Hard(w)) return;
            bool paved = AssetAvailability.Has(AssetCategories.SurfacePaving);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            foreach (var fr in w.Fronts)
            {
                if (fr.Edge == null) continue;
                var walk = fr.Walk;
                var windows = new List<float2>();
                float end = math.min(12f, walk.Length * 0.1f);
                if (mode == "continuous")
                {
                    float usable = walk.Length - 2f * end;
                    int n = math.max(1, (int)math.round(usable / 26f));
                    for (int i = 0; i < n; i++) windows.Add(new float2(end + usable * (i + 0.25f) / n, end + usable * (i + 0.75f) / n));
                }
                else
                {
                    float half = ctx.F("stepWidth") * 0.5f;
                    int count = ctx.I("stepCount");
                    for (int i = 0; i < count; i++)
                    {
                        float s = end + half + (walk.Length - 2f * (end + half)) * (i + 0.5f) / count;
                        windows.Add(new float2(s - half, s + half));
                    }
                }
                foreach (var win in windows)
                {
                    float mid = (win.x + win.y) * 0.5f;
                    if (w.InGap(fr, mid, 6f)) continue;
                    var c = walk.At(mid, out _, out var nrm);
                    float hTop = walk.HeightAt(mid);
                    if (hTop - w.Deck < 0.8f) continue;   // nothing to step down: the bank is a gentle lawn there
                    fr.Edge.Signed(c, out float se, out _);
                    var foot = fr.Edge.At(se, out _, out _);
                    float depth = math.distance(c, foot) - w.Ps - w.EdgeHw;
                    if (depth < 3f) continue;
                    if (paved)
                    {
                        var ring = walk.Strip(-(w.Ps + depth), -(w.Ps - 0.2f), win.x, win.y);
                        foreach (var piece in ClipLand(w.Shore, ring))
                            if (ctx.Surface(piece, SurfaceRole.Paving, AssetMix.Pick(WaterfrontArt.Paving(w.Style), 0u), AssetCategories.SurfacePaving)) w.Pads.Add(piece);
                    }
                    for (float s = win.x; s < win.y; s += 3f)
                    {
                        var a = walk.At(s, out _, out var na); var b = walk.At(math.min(win.y, s + 3f), out _, out var nb);
                        w.Occ.TakeCorridor(a - na * (w.Ps + depth * 0.5f), b - nb * (w.Ps + depth * 0.5f), depth * 0.5f, 6);
                    }
                    if (OnLaid(fr, c + nrm * w.NetOff, out var top) && Gentle(g, top - nrm * w.Hw, foot, 0.16f))
                    {
                        ctx.Plan.Nets.Add(new PlannedNet
                        {
                            A = new float3(top.x, 0f, top.y), B = new float3(math.lerp(top, foot, 1f / 3f).x, 0f, math.lerp(top, foot, 1f / 3f).y),
                            C = new float3(math.lerp(top, foot, 2f / 3f).x, 0f, math.lerp(top, foot, 2f / 3f).y), D = new float3(foot.x, w.Deck, foot.y),
                            Role = NetRole.Pathway, Asset = pathAsset, FollowTerrain = true,
                        });
                        w.Occ.Paths.Add(top, foot, 1.5f, 100);
                    }
                    w.Gaps.Add((fr, win));
                    w.Steps++;
                }
            }
        }

        /// <summary>Natural bank: fishing spots at the water's edge, each on a small level landing with a seat and the rod.</summary>
        private static void FishingSpots(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            if (!ctx.B("fishing")) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontFishing);
            float spacing = ctx.F("fishingSpacing");
            float d = math.max(1.6f, w.Edge == "bank" ? 1.4f : w.EdgeHw * 2f + 1.4f);
            for (float s = spacing * 0.75f; s < shore.Length - 2f; s += spacing)
            {
                if (!w.InRun(s)) continue;
                var c = shore.WaterAt(s, out var inw);
                var tan = new float2(inw.y, -inw.x);
                var spot = c + inw * (d + 0.4f);
                if (!g.MakeLevel(spot, 2.2f, 0.8f, out _)) continue;
                uint v = Hashing.Mix(ctx.Seed, 0xF150u + (uint)w.Fishing);
                bool any = false;
                // The rod at the edge, a seat beside it facing the water, a bin every other spot.
                if (cat != null) any |= LeisureKit.Put(ctx, occ, c + inw * d + tan * 0.9f, Yaw(-inw), ObjectRole.Furniture, AssetMix.One(""), cat, 0.6f, v, 0.3f, 0.1f);
                any |= LeisureKit.Put(ctx, occ, c + inw * (d + 0.2f) - tan * 1.1f, Yaw(-inw), ObjectRole.Bench, AssetMix.Pick(WaterfrontArt.Or(ctx.S("benchAsset"), WaterfrontArt.AnglerBenches), 0u), null, 1.1f, Hashing.Mix(v, 1u), 0.3f, 0.1f);
                if ((w.Fishing & 1) == 0) LeisureKit.Put(ctx, occ, c + inw * (d + 0.1f) + tan * 2.9f, 0f, ObjectRole.Bin, AssetMix.One(""), null, 0.5f, Hashing.Mix(v, 2u), 0.3f, 0.1f);
                if (!any) continue;
                w.Fishing++;
                occ.Take(c + inw * d, 2.5f, 6);   // open edge: no riprap in front of the angler
            }
        }

        // ───────────── ground ─────────────

        private static List<float2[]> ClipLand(Shoreline shore, float2[] ring)
        {
            var result = new List<float2[]>();
            try
            {
                foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.Clean(ring, 0.2f) }, new[] { (IReadOnlyList<float2>)shore.Land.Ring }, BoolOp.Intersection, FillRule.NonZero))
                    foreach (var piece in Poly.CutHoles(r, 0.5f))
                    {
                        var c = Poly.Clean(piece, 0.3f);
                        if (c.Length >= 3 && Poly.Area(c) > 8f) result.Add(c);
                    }
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>
        /// The land behind a walk from signed offset <paramref name="d"/> inland: the offset line, extended along the end
        /// tangents and closed far inland, intersected with the land. Its sea-side edge is the smooth curve (not the
        /// waterline), so the lawn meets the paving in one clean line.
        /// </summary>
        private static List<float2[]> Behind(Shoreline shore, ShoreWalk walk, float d)
        {
            var line = walk.Offset(d, 3f);
            if (line.Length < 2) return new List<float2[]>();
            walk.At(0f, out var t0, out var n0); walk.At(walk.Length, out var t1, out var n1);
            const float Far = 600f;
            var ring = new List<float2>();
            ring.Add(line[0] - t0 * Far + n0 * Far);
            ring.Add(line[0] - t0 * Far);
            ring.AddRange(line);
            ring.Add(line[line.Length - 1] + t1 * Far);
            ring.Add(line[line.Length - 1] + t1 * Far + n1 * Far);
            return ClipLand(shore, ring.ToArray());
        }

        /// <summary>
        /// Surfaces and paint: the paved band along the walk curve (harbour: the whole apron from the quay), lawn behind it
        /// from the band's smooth land edge, ONE ground texture on the bank (lush grass, a clean sand/dirt band only at the
        /// waterline of a soft edge) — no patchwork of lawn surface, sand and dirt on the slope.
        /// </summary>
        private static void Ground(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            var pave = AssetMix.Pick(WaterfrontArt.Or(ctx.S("surfaceAsset"), WaterfrontArt.Paving(w.Style)), 0u);
            string surface = ctx.S("surface");
            bool pavedOk = surface != "paved" || AssetAvailability.Require(ctx.Plan, AssetCategories.SurfacePaving, new[] { ctx.S("surfaceAsset") }, Keys.Category(AssetCategories.SurfacePaving));
            bool gravelOk = surface != "gravel" || AssetAvailability.Require(ctx.Plan, AssetCategories.SurfaceNatural, null, Keys.Category(AssetCategories.SurfaceNatural));
            foreach (var fr in w.Fronts)
            {
                var walk = fr.Walk;
                float d0 = -w.Ps, d1 = w.Pl;
                if (w.Edge == "quay" && fr.Edge != null)
                {
                    // The apron reaches the quay deck: from the walk to the quay's land side.
                    float far = 0f;
                    for (int i = 0; i < walk.Line.Length; i += 4) { float ue = fr.Edge.Signed(walk.Line[i], out _, out float eb); if (eb <= 0f) far = math.max(far, ue); }
                    d0 = -math.max(w.Ps, far - w.EdgeHw + 0.3f);
                }
                switch (surface)
                {
                    case "paved":
                        if (pavedOk) foreach (var ring in ClipLand(shore, walk.Strip(d0, d1))) ctx.Surface(ring, SurfaceRole.Paving, pave, AssetCategories.SurfacePaving);
                        break;
                    case "gravel":
                        if (gravelOk) foreach (var ring in ClipLand(shore, walk.Strip(-(w.Hw + 0.8f), w.Hw + 0.8f))) ctx.Surface(ring, SurfaceRole.Ground, new AssetRef("", "Forestry Surface 01"), AssetCategories.SurfaceNatural);
                        break;
                    case "grass":
                        foreach (var ring in ClipLand(shore, walk.Strip(d0, d1))) ctx.Surface(ring, SurfaceRole.Grass, AssetMix.One(""));
                        break;
                }
                if (ctx.S("backdrop") != "none")
                    foreach (var ring in Behind(shore, walk, d1 + 0.3f)) Lawn(ctx, ring);
                // The bank in one texture: lush grass from the paving's sea edge down to the shore band.
                if (w.Water && w.Edge != "quay")
                {
                    float bankTo = w.Ps + Shoulder + 40f;
                    foreach (var ring in ClipLand(shore, walk.Strip(-bankTo, d0 - 0.2f)))
                        TerrainPaint.Field(ctx, ring, TerrainMaterials.Lush, 0.55f, 2.5f, 0f, null, 0f, 0f, 0f, 0xBA4Bu);
                }
            }
            if (w.Fronts.Count == 0 && ctx.S("backdrop") != "none")
                foreach (var ring in shore.BandFromWater(w.P + 2.5f, shore.MaxDepth + Shoreline.MaxGap + 20f)) Lawn(ctx, ring);
            ShoreTexture(w);
        }

        /// <summary>
        /// (rf-beach) The lawn behind the promenade: lush grass painted into the terrain with a soft, wandering edge (the grass
        /// surface read as a flat neon-green rectangle with hard edges in the game runner's pictures).
        /// </summary>
        internal static void Lawn(LayerContext ctx, IReadOnlyList<float2> ring) => TerrainPaint.Field(ctx, ring, TerrainMaterials.Lush, 0.6f, 5f, 3f, null, 0f, 0f, 0f, 0x1A3Bu);

        /// <summary>
        /// A clean shore band in the terrain texture at a soft edge: sand (promenade, pier) or dirt (fishing bank) from a few
        /// metres under the water to ~2.5 m up the bank, one even strength with a soft, unbroken edge. Quays are stone to the water.
        /// </summary>
        private static void ShoreTexture(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore;
            string mode = ctx.S("shoreTexture");
            if (mode == "auto")
                switch (w.Edge)
                {
                    case "bank": mode = w.Style == "fishing" ? "dirt" : "sand"; break;
                    case "boardwalk": mode = "sand"; break;
                    default: mode = "none"; break;
                }
            if (mode != "sand" && mode != "dirt" || !w.Water) return;
            int layer = mode == "sand" ? TerrainMaterials.Sand : TerrainMaterials.Dirt;
            var clip = Poly.UnionAll(w.G.Clips);
            var rings = new List<IReadOnlyList<float2>>();
            foreach (var r in clip) if (r.Outer != null) rings.Add(r.Outer);
            foreach (var run in w.Runs)
                foreach (var strip in shore.StripFromWater(-6f, 2.5f, run.x, run.y, 4f))
                    try
                    {
                        foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)strip }, rings, BoolOp.Intersection, FillRule.NonZero))
                            foreach (var piece in Poly.CutHoles(r, 0.5f))
                                TerrainPaint.Field(ctx, piece, layer, mode == "sand" ? 0.85f : 0.75f, 2f, 0f, null, 0f, 0f, 0f, 0x5E0Au);
                    }
                    catch (Exception) { }
        }

        // ───────────── dressing ─────────────

        /// <summary>Riprap along a natural bank, mooring bollards and life rings along the quay's land side (parallel to its curve).</summary>
        private static void EdgeDressing(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var occ = w.Occ; var g = w.G;
            if (ctx.B("moorings") && Hard(w))
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontMooring, AssetCategories.Bollard);
                if (cat != null)
                    foreach (var line in w.EdgeLines)
                    {
                        float len = Shoreline.LengthOf(line);
                        for (float s = 3f; s < len - 3f; s += ctx.F("mooringSpacing"))
                        {
                            var p = Shoreline.PointOn(line, s, out var t);
                            var inl = new float2(-t.y, t.x);
                            if (LeisureKit.Put(ctx, occ, p + inl * (w.EdgeHw + 0.8f), Yaw(-inl), ObjectRole.Furniture, AssetMix.Pick(WaterfrontArt.Moorings, 0u), cat, 0.4f, Hashing.Mix(ctx.Seed, 0x3009u + (uint)w.Moorings), 0.4f, 0.1f))
                                w.Moorings++;
                        }
                    }
            }
            if (ctx.B("riprap") && w.Edge == "bank" && AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, null, Keys.Category(AssetCategories.Rock)))
            {
                var keep = occ.GroundOk;
                occ.GroundOk = (p, r) => !g.OnCliff(p);
                for (float s = 1.5f; s < shore.Length - 1.5f; s += 2.4f)
                {
                    if (!w.InRun(s)) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0x21F0u + (uint)(s * 10f));
                    var c = shore.WaterAt(s + (Hashing.Unit(v) - 0.5f) * 1.2f, out var inw);
                    float scale = 0.6f + 0.7f * Hashing.Unit(Hashing.Mix(v, 1u));
                    var q = c + inw * (0.3f + Hashing.Unit(Hashing.Mix(v, 2u)) * 1.4f);
                    if (!shore.Land.Contains(q) || !occ.Free(q, 0.7f * scale, 0.3f, 0.05f)) continue;
                    var o = ctx.Object(q, Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    occ.Take(q, 0.7f * scale, 2);
                    w.Rocks++;
                }
                occ.GroundOk = keep;
            }
            if (ctx.B("nautical") && Hard(w))
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.WaterfrontNautical, AssetCategories.Decoration);
                if (cat != null)
                    foreach (var line in w.EdgeLines)
                    {
                        float len = Shoreline.LengthOf(line);
                        for (float s = 18f; s < len - 6f; s += 45f)
                            foreach (var (d, ds) in new[] { (0.9f, 0f), (0.9f, 3f), (0.9f, -3f), (2f, 0f), (2f, 4f) })
                            {
                                var p = Shoreline.PointOn(line, s + ds, out var t);
                                var inl = new float2(-t.y, t.x);
                                if (LeisureKit.Put(ctx, occ, p + inl * (w.EdgeHw + d), Yaw(-inl), ObjectRole.Furniture, AssetMix.Pick(WaterfrontArt.Nautical, 0u), cat, 0.5f, Hashing.Mix(ctx.Seed, 0xA9C0u + (uint)(s * 3f)), 0.3f, 0.1f)) break;
                            }
                    }
            }
        }

        /// <summary>
        /// The promenade's rhythm along the walk curve: benches on its water side facing the water every benchSpacing, a bin
        /// with every third, lamps on the land side (hillside wall: on the sea edge) half a bay off the benches; ONE
        /// continuous railing along the water edge where the ground drops.
        /// </summary>
        private static void PromenadeFurniture(Work w)
        {
            var ctx = w.Ctx; var occ = w.Occ;
            var bench = AssetMix.Pick(WaterfrontArt.Or(ctx.S("benchAsset"), WaterfrontArt.Benches), 0u);
            var lamp = AssetMix.Pick(WaterfrontArt.Or(ctx.S("lampAsset"), WaterfrontArt.Lamps), 0u);
            var rail = AssetMix.Pick(WaterfrontArt.Or(ctx.S("railingAsset"), WaterfrontArt.Railings), 0u);
            float benchSpacing = ctx.F("benchSpacing"), lampSpacing = ctx.F("lampSpacing");
            foreach (var fr in w.Fronts)
            {
                var walk = fr.Walk;
                foreach (var line in fr.Laid)
                {
                    float len = Shoreline.LengthOf(line);
                    if (benchSpacing > 0f)
                        for (float s = benchSpacing * 0.5f; s < len; s += benchSpacing)
                        {
                            var p = Shoreline.PointOn(line, s, out var t);
                            var inl = new float2(-t.y, t.x);
                            walk.Signed(p, out float ws, out _);
                            if (w.InGap(fr, ws, 4f)) continue;
                            var q = p - inl * (w.Hw + 1.6f);
                            uint v = Hashing.Mix(ctx.Seed, 0xBE70u + (uint)w.Benches);
                            if (!LeisureKit.Put(ctx, occ, q, Yaw(-inl), ObjectRole.Bench, bench, null, 0.9f, v, 0.3f, 0.1f)) continue;
                            w.Benches++;
                            if (w.Benches % 3 == 0)
                                LeisureKit.Put(ctx, occ, q + t * 1.9f, 0f, ObjectRole.Bin, AssetMix.Pick(WaterfrontArt.Bins, 0u), null, 0.4f, Hashing.Mix(v, 1u), 0.3f, 0.1f);
                        }
                    if (lampSpacing > 0f)
                    {
                        float phase = benchSpacing > 0f ? (benchSpacing * 0.5f + lampSpacing * 0.5f) % lampSpacing : lampSpacing * 0.5f;
                        float off = fr.Wall ? -(w.P - w.Hw - 0.8f) : w.Hw + 0.8f;
                        for (float s = phase; s < len; s += lampSpacing)
                        {
                            var p = Shoreline.PointOn(line, s, out var t);
                            var inl = new float2(-t.y, t.x);
                            if (LeisureKit.Put(ctx, occ, p + inl * off, Yaw(-inl), ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A70u + (uint)w.Lamps), 0.2f, 0.1f))
                                w.Lamps++;
                        }
                    }
                }
                if (ctx.B("railings")) Railing(w, fr, rail);
            }
            // Harbour apron: lamps and benches facing the water along the quay's land side, in the same rhythm.
            if (w.Edge == "quay")
                foreach (var line in w.EdgeLines)
                {
                    float len = Shoreline.LengthOf(line);
                    if (lampSpacing > 0f)
                        for (float s = lampSpacing * 0.5f; s < len; s += lampSpacing)
                        {
                            var p = Shoreline.PointOn(line, s, out var t);
                            var inl = new float2(-t.y, t.x);
                            if (LeisureKit.Put(ctx, occ, p + inl * (w.EdgeHw + 1f), Yaw(-inl), ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A90u + (uint)w.Lamps), 0.2f, 0.1f))
                                w.Lamps++;
                        }
                    if (benchSpacing > 0f)
                        for (float s = benchSpacing * 0.5f + (lampSpacing > 0f ? lampSpacing * 0.5f : 0f); s < len; s += benchSpacing)
                        {
                            var p = Shoreline.PointOn(line, s, out var t);
                            var inl = new float2(-t.y, t.x);
                            if (LeisureKit.Put(ctx, occ, p + inl * (w.EdgeHw + 1.6f), Yaw(-inl), ObjectRole.Bench, bench, null, 0.9f, Hashing.Mix(ctx.Seed, 0xBE90u + (uint)w.Benches), 0.3f, 0.1f))
                                w.Benches++;
                        }
                }
        }

        /// <summary>
        /// ONE railing along the paved band's water edge, parallel to the walk, where the planned ground there drops more
        /// than 1 m within 3.5 m. Short dips are bridged (≤ 16 m), short stretches dropped (&lt; 12 m): a few long runs of
        /// equal ~6 m pieces sharing their nodes, never a scatter of short segments. Openings only at links and piers.
        /// </summary>
        private static void Railing(Work w, Front fr, AssetRef rail)
        {
            var ctx = w.Ctx; var g = w.G; var occ = w.Occ;
            var walk = fr.Walk;
            float d = -(w.Ps + 0.6f);
            int n = walk.Line.Length;
            var drop = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var p = walk.At(walk.S[i], out _, out var nrm);
                var q = p + nrm * d;
                float h = g.H(q);
                drop[i] = h - math.min(g.H(q - nrm * 2f), g.H(q - nrm * 3.5f)) >= CliffTop.DropForRailing && w.Shore.Land.ContainsDisc(q, 0.3f)
                    && math.abs(h - walk.H[i]) < 0.2f;
            }
            // Bridge short gaps, then drop short runs.
            Fill(drop, walk.S, 16f, true);
            Fill(drop, walk.S, 12f, false);
            int start = -1;
            for (int i = 0; i <= n; i++)
            {
                bool on = i < n && drop[i] && !w.InGap(fr, walk.S[i], 1.5f);
                if (on && start < 0) start = i;
                if (!on && start >= 0)
                {
                    float s0 = walk.S[start], s1 = walk.S[i - 1];
                    int pieces = math.max(1, (int)math.round((s1 - s0) / 6f));
                    float2 prev = default;
                    for (int k = 0; k <= pieces; k++)
                    {
                        var p = walk.At(math.lerp(s0, s1, k / (float)pieces), out _, out var nrm) + nrm * d;
                        if (k > 0)
                        {
                            ctx.StraightNet(prev, p, NetRole.Fence, rail);
                            occ.TakeCorridor(prev, p, 0.3f, 5);
                            w.Railings++;
                        }
                        prev = p;
                    }
                    if (s1 - s0 > 1f) w.RailRuns++;
                    start = -1;
                }
            }
        }

        /// <summary>Flips runs of !value shorter than <paramref name="max"/> m that lie between value runs (bridge = true) or anywhere (drop short runs: value = false).</summary>
        private static void Fill(bool[] flags, float[] s, float max, bool value)
        {
            int n = flags.Length;
            int i = 0;
            while (i < n)
            {
                if (flags[i] == value) { i++; continue; }
                int j = i;
                while (j < n && flags[j] != value) j++;
                bool inner = i > 0 && j < n;
                float len = s[math.min(j, n - 1)] - s[i];
                if (len <= max && (inner || !value)) for (int k = i; k < j; k++) flags[k] = value;
                i = j;
            }
        }

        // ───────────── land side ─────────────

        private static void LandSide(Work w)
        {
            var ctx = w.Ctx; var occ = w.Occ; var g = w.G;
            int kiosks = ctx.I("kiosks");
            if (kiosks > 0)
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
                string seats = AssetAvailability.Has(AssetCategories.CafeSeating) ? AssetCategories.CafeSeating : null;
                float total = 0f; foreach (var fr in w.Fronts) total += fr.Walk.Length;
                if (cat != null && total > 0f)
                    for (int k = 0; k < kiosks; k++)
                    {
                        float at = total * (k + 0.5f) / kiosks, acc = 0f;
                        Front fr = null; float s0 = 0f;
                        foreach (var f in w.Fronts) { if (at <= acc + f.Walk.Length) { fr = f; s0 = at - acc; break; } acc += f.Walk.Length; }
                        if (fr == null) continue;
                        foreach (float off in new[] { 0f, 8f, -8f, 16f, -16f })
                        {
                            float s = s0 + off;
                            if (s < 6f || s > fr.Walk.Length - 6f || w.InGap(fr, s, 4f)) continue;
                            var c = fr.Walk.At(s, out _, out var nrm);
                            // Harbour: on the apron facing the quay; else on the level shoulder just behind the paving (no pad).
                            float u = w.Edge == "quay" || fr.Wall ? -(w.Ps - 3.4f) : w.Pl + 2.8f;
                            var kp = c + nrm * u;
                            if (TerrainSurvey.Relief(g.H, kp, 3f) > 0.35f) continue;
                            uint v = Hashing.Mix(ctx.Seed, 0x6170u + (uint)w.Kiosks);
                            if (!LeisureKit.Put(ctx, occ, kp, Yaw(-nrm), ObjectRole.Furniture, AssetMix.Pick(WaterfrontArt.Kiosks, (uint)w.Kiosks), cat, 2.5f, v, 0.8f, 0.3f)) continue;
                            w.Kiosks++;
                            if (seats != null)
                                for (int t = 0; t < 4; t++)
                                {
                                    var tan = new float2(nrm.y, -nrm.x);
                                    var q = kp - nrm * 3.6f + tan * ((t - 1.5f) * 2.3f);
                                    LeisureKit.Put(ctx, occ, q, Yaw(-nrm), ObjectRole.Furniture, AssetMix.One(""), seats, 0.9f, Hashing.Mix(v, (uint)t + 1u), 0.4f, 0.15f);
                                }
                            break;
                        }
                    }
            }
            if (ctx.B("beds")) Beds(w, ctx.F("treeSpacing"));
            string trees = ctx.S("trees");
            // One species per row: an avenue reads as planted, not as a sample of the tree pool.
            bool picked = ctx.L("treeSpecies").Length > 0;
            var ids = WaterfrontArt.Or(ctx.L("treeSpecies"), WaterfrontArt.Trees(w.Style));
            float spacing = ctx.F("treeSpacing");
            if (trees != "none" && (ids.Length > 0 || AssetAvailability.Has(AssetCategories.Tree)))
            {
                int rows = trees == "double" ? 2 : 1;
                int n = 0;
                var keep = occ.GroundOk;
                occ.GroundOk = (p, r) => !g.OnCliff(p) && g.Sculpt.GradeAt(p, 1.5f) < 0.3f;
                foreach (var fr in w.Fronts)
                    for (int r = 0; r < rows; r++)
                    {
                        float d = w.Pl + (ctx.B("beds") ? 4.6f : 2.2f) + r * 6f;
                        var line = fr.Walk.Offset(d, 2f);
                        float len = Shoreline.LengthOf(line);
                        for (float s = (r == 0 ? 0.5f : 1f) * spacing; s < len; s += spacing)
                        {
                            var p = Shoreline.PointOn(line, s, out _);
                            uint v = Hashing.Mix(ctx.Seed, 0x7EE0u + (uint)n);
                            if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(ids, picked ? v : (uint)r), null, 2.2f, v, 1f, 0.3f, 3, 2 + (int)(v % 2u)))
                                n++;
                        }
                    }
                occ.GroundOk = keep;
            }
        }

        /// <summary>
        /// Planted beds behind the promenade, parallel to the walk: low flowering shrubs in long soft beds (one kind per bed,
        /// alternating) in the bays between the lamps, with lush-grass paint under them.
        /// </summary>
        private static void Beds(Work w, float treeSpacing)
        {
            var ctx = w.Ctx; var occ = w.Occ; var g = w.G;
            float bay = math.max(10f, ctx.F("lampSpacing") > 0f ? ctx.F("lampSpacing") : treeSpacing * 1.5f);
            var keep = occ.GroundOk;
            occ.GroundOk = (p, r) => !g.OnCliff(p) && g.Sculpt.GradeAt(p, 1.2f) < 0.25f;
            int bed = 0;
            foreach (var fr in w.Fronts)
            {
                var line = fr.Walk.Offset(w.Pl + 2.4f, 1.5f);
                float len = Shoreline.LengthOf(line);
                for (float s0 = bay * 0.5f; s0 + bay * 0.6f < len; s0 += bay * 2f)
                {
                    var ids = new[] { WaterfrontArt.BedShrubs[bed % WaterfrontArt.BedShrubs.Length] };
                    int placed = 0;
                    var pts = new List<float2>();
                    for (float s = s0; s < s0 + bay * 0.6f; s += 2.1f)
                    {
                        var p = Shoreline.PointOn(line, s, out var t);
                        var nn0 = new float2(-t.y, t.x);
                        uint v = Hashing.Mix(ctx.Seed, 0xBED0u + (uint)(bed * 64 + placed));
                        var q = p + nn0 * (Hashing.Unit(v) - 0.5f) * 0.8f;
                        if (!occ.Free(q, 0.9f, 0.3f, 0.05f)) continue;
                        ctx.Object(q, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Bush, AssetMix.Pick(ids, 0u), v);
                        occ.Take(q, 0.9f, 4);
                        pts.Add(q);
                        placed++;
                    }
                    if (placed >= 3)
                    {
                        w.Beds++;
                        bed++;
                        var a = pts[0]; var b = pts[pts.Count - 1];
                        var dir = math.normalizesafe(b - a); var nn = new float2(-dir.y, dir.x);
                        var ring = new[] { a - dir * 1.2f - nn * 1.2f, b + dir * 1.2f - nn * 1.2f, b + dir * 1.2f + nn * 1.2f, a - dir * 1.2f + nn * 1.2f };
                        TerrainPaint.Field(ctx, ring, TerrainMaterials.Dirt, 0.45f, 0.9f, 0.3f, null, 0f, 0f, 0f, 0xBEDu + (uint)bed);
                    }
                }
            }
            occ.GroundOk = keep;
        }

        private static void Backdrop(Work w)
        {
            var ctx = w.Ctx; var shore = w.Shore; var g = w.G;
            if (ctx.S("backdrop") != "park") return;
            var ids = WaterfrontArt.Or(ctx.L("treeSpecies"), WaterfrontArt.Trees(w.Style));
            if (ids.Length == 0 && !AssetAvailability.Has(AssetCategories.Tree)) return;
            var keep = w.Occ.GroundOk;
            w.Occ.GroundOk = (p, r) => !g.OnCliff(p);
            var bands = new List<float2[]>();
            foreach (var fr in w.Fronts) bands.AddRange(Behind(shore, fr.Walk, w.Pl + 16f));
            if (w.Fronts.Count == 0) bands.AddRange(shore.BandFromWater(w.P + 16f, shore.MaxDepth + Shoreline.MaxGap + 20f));
            foreach (var ring in bands)
            {
                var band = LayerRegion.From(ring);
                if (band == null) continue;
                LeisureKit.Plant(ctx, w.Occ, band, (int)(band.Area / 10000f * 28f), ObjectRole.Tree, ids, 2.5f, 26f, 0.15f, 0x7EF0u, null, 1, 3);
                LeisureKit.Plant(ctx, w.Occ, band, (int)(band.Area / 10000f * 30f), ObjectRole.Bush, WaterfrontArt.Shrubs, 1f, 18f, 0.12f, 0xB0F0u, null);
            }
            w.Occ.GroundOk = keep;
        }
    }
}
