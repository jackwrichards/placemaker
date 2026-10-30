// Amphitheatre / open-air venue: a stage (or screen, bandstand, podium) at a focal point and rows
// of seating stepped in an arc facing it — or straight fan rows for an open-air cinema, or a full
// ring round a central bandstand. The bowl is carved into the terrain (dig: the stage sits in a pit
// and the rows climb back to the ground) or built up (build: raked rows rising to a crest with the
// promenade on top), always blended back to the original ground and clamped at the outline.
// Radial aisles climb the rows; a promenade runs along the top; side paths lead down to the
// orchestra. Lamps, kiosks and a tree ring line the rim; a backdrop of trees screens the stage.
// The bowl is handed to the composer as a "place" so features keep out of the seating.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Plaza;
using Placemaker.Core.Layers.Types.Events;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Amphitheatre
{
    [AreaGenerator(81, Group = "events")]
    public sealed class AmphitheatreLayer : LeisureLayer, ILinkedControls
    {
        /// <summary>(s18) Ground palette: picking one clears the per-role surface picks; a role pick reads Custom.</summary>
        public string[] OnValueSet(SettingsBag settings, string controlId) => GroundPalettes.OnValueSet(settings, controlId, "seatSurface");
        public void Derive(SettingsBag settings) => GroundPalettes.Derive(settings, "seatSurface");

        public const string BowlPlace = Composer.PlacePrefix + "bowl";
        private const string K = "Placemaker.Amphitheatre.";
        /// <summary>Steepest terrain change allowed towards the outline (rise/run), below the 30 % the tests and the build accept.</summary>
        private const float EdgeSlope = 0.28f;

        protected override string Area => "Amphitheatre";
        public override string Id => "amphitheatre";
        public override string Icon => "Media/Game/Icons/TouristAttractions.svg";
        public override float MinArea => 800f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(LightTowersAddOn.LayerId, ("where", "edges")), Feature("restrooms", ("count", 1.0)), Feature("shops", ("count", 2.0), ("where", "paths")),
            Feature("picnic", ("groups", 2.0)), FairgroundLayer.BigTrees(), Flowerbeds(), Feature("rocks"),
            StreetShopsFeature(Area), Feature("decals", ("theme", "leaves"), ("ground", "auto"), ("density", 1.0)),
        };

        private static FeatureInfo Flowerbeds()
        {
            var f = Feature("flowerbeds");
            f.LabelKey = "Placemaker.Park.Feature.flowerbeds";
            return f;
        }

        protected override int WantedEntrances(LayerRegion region) => math.clamp(Entrances.WantedByArea(region.Area), 1, 3);

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "theatre", ControlTier.Essential, "amphiLayout", "classic", "cinema", "bandstand", "meadow", "speakers", "theatre").Tip();
            c.Slider("rows", 8, 1, 24, 1, "count", ControlTier.Essential, "amphiLayout").Tip();
            c.Slider("span", 160, 60, 360, 10, "deg", ControlTier.Essential, "amphiLayout").Tip();
            c.Choice("terrain", "dig", ControlTier.Essential, "amphiLayout", "dig", "build", "slope", "none").Tip();

            c.Slider("rowDepth", 2.2, 1.2, 6, 0.2, "m", ControlTier.More, "amphiSeating").Tip();
            c.Slider("rise", 0.45, 0.1, 1.2, 0.05, "m", ControlTier.More, "amphiSeating").Tip();
            c.Choice("seats", "benches", ControlTier.More, "amphiSeating", "benches", "deckchairs", "picnic", "lawn").Tip();
            c.Slider("seatSpacing", 3, 1.5, 8, 0.5, "m", ControlTier.More, "amphiSeating");
            c.Slider("aisles", 3, 0, 8, 1, "count", ControlTier.More, "amphiSeating").Tip();
            c.Choice("rowShape", "arc", ControlTier.More, "amphiSeating", "arc", "straight").Tip();
            c.Choice("seatGround", "paved", ControlTier.More, "amphiSeating", "grass", "paved", "gravel");

            c.Choice("stageKind", "stage", ControlTier.More, "amphiStage", "stage", "screen", "bandstand", "podium");
            c.Slider("stageSize", 7, 2, 20, 1, "m", ControlTier.More, "amphiStage").Tip();
            c.Choice("stageSide", "auto", ControlTier.More, "amphiStage", "auto", "road", "far", "low", "centre").Tip();
            c.Toggle("orchestra", true, ControlTier.More, "amphiStage").Tip();
            c.Toggle("backdrop", true, ControlTier.More, "amphiStage").Tip();
            c.Asset("stageAsset", AssetCategories.Stage, ControlTier.More, "amphiStage");

            c.Toggle("promenade", true, ControlTier.More, "amphiRim").Tip();
            c.Slider("lampSpacing", 16, 0, 60, 2, "m", ControlTier.More, "amphiRim").Layout(4).Tip();
            c.Toggle("rimTrees", true, ControlTier.More, "amphiRim");
            c.Slider("kiosks", 1, 0, 8, 1, "count", ControlTier.More, "amphiRim").Tip();
            c.Toggle("lightTowers", false, ControlTier.More, "amphiRim").Tip();
            c.Slider("treeDensity", 15, 0, 80, 5, "perHa", ControlTier.More, "amphiRim");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "amphiRim");

            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            GroundPalettes.DefineControls(c, "ground", "seatSurface");
            c.Asset("seatSurface", AssetCategories.SurfacePaving, ControlTier.More, "ground");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.RandomRange("rows", 4, 12);
            c.RandomRange("span", 120, 200);
            c.Fixed("style");
            c.Fixed("terrain");
            c.Fixed("stageSide");
        }

        protected override void DefinePresets()
        {
            // (the plazas makeover) the stone theatre
            Preset("theatre", ("style", "theatre"), ("palette", GroundPalettes.Classic));
            Preset("classic", ("style", "classic"), ("rows", 10.0), ("span", 160.0), ("terrain", "dig"), ("rowDepth", 1.8), ("rise", 0.45),
                ("seats", "benches"), ("seatSpacing", 2.5), ("aisles", 3.0), ("rowShape", "arc"), ("seatGround", "paved"), ("stageKind", "stage"), ("stageSize", 7.0),
                ("orchestra", true), ("backdrop", true), ("kiosks", 1.0));
            Preset("cinema", ("style", "cinema"), ("rows", 10.0), ("span", 70.0), ("terrain", "build"), ("rowDepth", 3.2), ("rise", 0.3),
                ("seats", "deckchairs"), ("seatSpacing", 2.0), ("aisles", 2.0), ("rowShape", "straight"), ("seatGround", "grass"), ("stageKind", "screen"),
                ("stageSize", 9.0), ("orchestra", false), ("backdrop", false), ("kiosks", 2.0), ("lampSpacing", 24.0), ("rimTrees", false));
            Preset("bandstand", ("style", "bandstand"), ("rows", 4.0), ("span", 360.0), ("terrain", "dig"), ("rowDepth", 4.0), ("rise", 0.25),
                ("seats", "deckchairs"), ("seatSpacing", 3.5), ("aisles", 4.0), ("rowShape", "arc"), ("seatGround", "grass"), ("stageKind", "bandstand"),
                ("stageSide", "centre"), ("stageSize", 5.0), ("orchestra", true), ("backdrop", false), ("kiosks", 1.0), ("lampSpacing", 12.0));
            Preset("meadow", ("style", "meadow"), ("rows", 8.0), ("span", 120.0), ("terrain", "build"), ("rowDepth", 6.0), ("rise", 0.2),
                ("seats", "lawn"), ("aisles", 0.0), ("rowShape", "arc"), ("seatGround", "grass"), ("stageKind", "stage"), ("stageSize", 12.0),
                ("orchestra", false), ("backdrop", true), ("kiosks", 4.0), ("lightTowers", true), ("lampSpacing", 30.0), ("treeDensity", 10.0));
            Preset("speakers", ("style", "speakers"), ("rows", 3.0), ("span", 180.0), ("terrain", "dig"), ("rowDepth", 2.4), ("rise", 0.3),
                ("seats", "benches"), ("seatSpacing", 3.0), ("aisles", 1.0), ("rowShape", "arc"), ("seatGround", "paved"), ("stageKind", "podium"),
                ("stageSize", 3.0), ("orchestra", true), ("backdrop", true), ("kiosks", 0.0), ("lampSpacing", 12.0), ("treeDensity", 30.0));
        }

        // ───────────────────────────── bowl geometry ─────────────────────────────

        private enum Mode { Flat = 0, Dig = 1, Build = 2 }

        private sealed class Bowl
        {
            public float2 F, D;
            public float Base;           // angle of D
            public float Half;           // half span (radians); π when Full
            public bool Full, Straight;
            public float R0, RowDepth, Rise, StageSize;
            public int Rows;
            public bool Promenade;
            public Mode Mode;
            public float Ref;

            public float2 Perp => new float2(-D.y, D.x);
            public float ROut => R0 + Rows * RowDepth;
            public float RP => ROut + 2f;                       // promenade centre line
            public float Rim => Promenade ? RP + 1.5f : ROut + 0.5f;
            public float Height => Rows * Rise;
            public float Ext => Full ? 0f : 4f / math.max(4f, RP);
            public float Tan => math.tan(math.min(Half, math.radians(70f)));

            /// <summary>
            /// Seat coordinates of p: true distance from the focal point, row coordinate (radius, or
            /// distance along D for straight rows) and how far p lies outside the seating span (m, ≤ 0 inside).
            /// </summary>
            public void Coord(float2 p, out float dist, out float r, out float excess)
            {
                var d = p - F;
                dist = math.length(d);
                if (Straight)
                {
                    float u = math.dot(d, D), lat = math.abs(math.dot(d, Perp));
                    r = u;
                    excess = u <= 0f ? lat + (-u) : lat - u * Tan;
                    return;
                }
                r = dist;
                if (Full) { excess = -1f; return; }
                float a = math.abs(EventKit.Wrap(math.atan2(d.y, d.x) - Base));
                excess = (a - Half) * math.max(1f, dist);
            }

            /// <summary>Target height relative to Ref, and whether p is in the bowl's core (absolute levels).</summary>
            public float Profile(float2 p, out bool core, out float outside)
            {
                Coord(p, out float dist, out float r, out float excess);
                float h = Height;
                core = false; outside = 0f;
                if (Mode == Mode.Dig)
                {
                    if (dist <= R0) { core = true; return -h; }
                    float rows = r <= R0 ? -h : r >= ROut ? 0f : -h + (math.floor((r - R0) / RowDepth) + 1f) * Rise;
                    float side = -h * (1f - math.saturate((dist - R0) / math.max(1f, h / 0.3f)));
                    float taper = math.max(4f, RowDepth * 2f);
                    float w = math.saturate(1f - math.max(0f, excess) / taper);
                    core = excess <= 0f && r <= ROut;
                    outside = core ? 0f : math.max(math.max(0f, excess), math.max(0f, r - ROut));
                    return math.lerp(side, rows, w);
                }
                if (Mode == Mode.Build)
                {
                    if (dist <= R0) { core = true; return 0f; }
                    float crest = Rim;
                    float rows = r <= R0 ? 0f : r < ROut ? (math.floor((r - R0) / RowDepth) + 1f) * Rise : r <= crest ? h : math.max(0f, h - (r - crest) * 0.25f);
                    float taper = math.max(4f, h / 0.3f);
                    float w = math.saturate(1f - math.max(0f, excess) / taper);
                    core = excess <= 0f && r <= crest;
                    outside = core ? 0f : math.max(math.max(0f, excess), math.max(0f, r - crest));
                    return rows * w;
                }
                return 0f;
            }

            /// <summary>Reach of the terrain change from the focal point.</summary>
            public float Reach => Mode == Mode.Build ? Rim + Height / 0.25f + 8f : math.max(ROut, R0 + Height / 0.3f) + 8f;

            public bool InBowl(float2 p, float margin)
            {
                Coord(p, out float dist, out float r, out float excess);
                if (dist <= R0 + margin) return true;
                return r <= Rim + margin && excess <= margin;
            }
        }

        private static float BlendLength(float cell) => math.max(8f, cell * 3f);

        /// <summary>Planned ground height at p (original ground outside the bowl's influence; clamped towards the outline).</summary>
        private static float HeightAfter(LayerContext ctx, LayerRegion region, Bowl b, float2 p, float cell)
        {
            float h0 = ctx.H(p);
            if (b.Mode == Mode.Flat) return h0;
            float prof = b.Profile(p, out bool core, out float outside);
            float w = core ? 1f : math.saturate(1f - outside / BlendLength(cell));
            float off = (b.Ref + prof - h0) * w;
            float d = math.max(0f, region.BoundaryDistance(p, math.abs(off) / EdgeSlope + cell + 1f) - cell * 0.71f);
            return h0 + math.clamp(off, -EdgeSlope * d, EdgeSlope * d);
        }

        // ───────────────────────────── planning ─────────────────────────────

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            string style = ctx.S("style");
            if (style == "theatre")
            {
                // (the plazas makeover, 2026-10-29: the user graded the amphitheatre F, "it just looks horrible") a stone theatre on
                // the designed squares' kit (PlazaDesigns.Theatre): a smooth bowl of seating steps, an orchestra, a stage.
                ctx.NavigationArea(region.Ring, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
                PlazaLayer.PlanDesignedSquare(ctx, occ, region, gates, "theatre", GroundPalettes.Resolve(ctx.Settings, GroundPalettes.Classic, "seatSurface"));
                CommonStats(ctx);
                ctx.Plan.Facts["rows"] = ctx.Plan.Facts.TryGetValue("theatreRows", out var tr) ? tr : 0;
                return;
            }
            Lawn.Cover(ctx, region.Ring, AssetMix.One(""));

            var b = Fit(ctx, region, occ, gates);
            ctx.Check();
            if (b == null)
            {
                ctx.Issue(IssueSeverity.Info, K + "Issue.NoRoom");
                Fallback(ctx, region, occ, gates, pathAsset);
                return;
            }
            if (b.Rows < ctx.I("rows")) ctx.Issue(IssueSeverity.Info, K + "Issue.FewerRows", b.Rows, ctx.I("rows"));

            // Reference level: mean ground over the seating.
            double sum = 0; int n = 0;
            for (float a = -b.Half; a <= b.Half + 1e-3f; a += math.max(0.1f, b.Half / 6f))
                for (float r = b.R0; r <= b.ROut + 0.01f; r += math.max(2f, (b.ROut - b.R0) / 3f))
                {
                    var p = b.F + EventKit.Dir(b.Base + a) * r;
                    if (region.Contains(p)) { sum += ctx.H(p); n++; }
                }
            b.Ref = n > 0 ? (float)(sum / n) : ctx.H(b.F);

            // Terrain: one level cell per grid square, rows read as steps.
            float cell = math.clamp(b.RowDepth, 2f, 6f);
            cell = math.max(cell, b.Reach * 2f / 50f);
            if (b.Mode != Mode.Flat) Terraform(ctx, region, b, cell);
            ctx.Check();

            // Paths: aisles, promenade, side paths down to the orchestra, gate connections.
            var spine = new List<(float2 a, float2 b)>();
            Paths(ctx, region, occ, b, pathAsset, spine);
            occ.Take(b.F, b.R0 - 0.5f, 0);   // orchestra kept clear (stage placed into it below)
            EventKit.ConnectGates(ctx, occ, region, gates, spine, pathAsset);
            ctx.Check();

            // Stage, orchestra, backdrop and the style's extras (before the seats so the seats give way).
            Stage(ctx, region, occ, b, style);
            Extras(ctx, region, occ, b, style, cell);
            ctx.Check();

            // Seating surface and seats.
            SeatSurface(ctx, region, b);
            int seats = Seats(ctx, region, occ, b, cell, out float capacity);
            ctx.Check();

            // Rim: lamps, kiosks, tree ring; trees elsewhere.
            Rim(ctx, region, occ, b, style);
            int trees = (int)(region.Area / 10000f * ctx.F("treeDensity"));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, ctx.L("treeSpecies"), Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, Species(ctx, RimTrees), 2.5f, 18f, 0.5f, 0x7B00u, p => b.InBowl(p, 4f));
            PathBenches(ctx, occ, 50f, PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0u), 0xBE90u);
            ctx.Check();

            // Regions: the bowl is a place (features keep out), the orchestra is the core, the rest green.
            foreach (var ring in BowlRings(b, math.max(0.5f, b.R0 - 0.5f), b.Rim))
            {
                var clipped = EventKit.Clip(region, ring);
                if (clipped != null) ctx.AddRegion(clipped, "bowl", BowlPlace);
            }
            var orch = EventKit.Clip(region, LayerRegion.Circle(b.F, math.max(1.5f, b.R0 - 0.5f), 20));
            if (orch != null) ctx.AddRegion(orch, "core", RegionTags.Core, ctx.B("orchestra") ? RegionTags.Paved : RegionTags.Green);
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f) || b.InBowl(p, 1f), 2.5f, 150f, ctx.Cancel);
            for (int i = 0; i < pieces.Count && i < 24; i++) ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);

            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Seats", seats, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Rows", b.Rows, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Capacity", Math.Round(capacity), Keys.UnitCount);
            ctx.Plan.Facts["rows"] = b.Rows;
            ctx.Plan.Facts["seats"] = seats;
            ctx.Plan.Facts["capacity"] = Math.Round(capacity);
            ctx.Plan.Facts["bowlHeight"] = b.Mode == Mode.Flat ? 0 : b.Height;
        }

        // ───────────── fitting ─────────────

        private static Mode ModeOf(string s) => s == "dig" ? Mode.Dig : s == "build" ? Mode.Build : Mode.Flat;

        /// <summary>Focal point, facing and the most rows (≤ wanted) that fit inside the outline, clear of reserved polygons.</summary>
        /// <summary>Site the "rows" setting is for (m²); a bigger site gets more rows, up to twice as many.</summary>
        public const float RowsReferenceArea = 6000f;
        /// <summary>Deepest dig / highest bank the scaled bowl may reach (m): more rows get shallower steps.</summary>
        public const float MaxBowlHeight = 5f;

        /// <summary>
        /// (rf-squares) Rows for the "rows" setting on a site of <paramref name="area"/> m²: the setting is the bowl of a
        /// 0.6 ha site; a city block gets up to twice as many rows, so the venue fills its site instead of a small bowl in
        /// a big empty lawn.
        /// </summary>
        public static int ScaledRows(int rows, float area) => math.clamp((int)math.round(rows * math.clamp(area / RowsReferenceArea, 1f, 2f)), 1, 30);

        private Bowl Fit(LayerContext ctx, LayerRegion region, Occupancy occ, List<Gate> gates)
        {
            int wanted = ScaledRows(ctx.I("rows"), region.Area);
            float span = math.radians(ctx.F("span"));
            bool straight = ctx.S("rowShape") == "straight";
            string sideMode = ctx.S("stageSide");
            bool full = !straight && (span >= math.radians(350f) || sideMode == "centre");
            var proto = new Bowl
            {
                Full = full, Straight = straight, Half = full ? math.PI : straight ? math.min(span * 0.5f, math.radians(70f)) : span * 0.5f,
                RowDepth = ctx.F("rowDepth"), Rise = wanted > ctx.I("rows") ? math.min(ctx.F("rise"), math.max(MaxBowlHeight / wanted, ctx.F("rise") * ctx.I("rows") / wanted)) : ctx.F("rise"), StageSize = ctx.F("stageSize"), Promenade = ctx.B("promenade"),
                Mode = ModeOf(ctx.S("terrain")),
            };
            proto.R0 = proto.StageSize + 2f;
            LeisureKit.Frame(region, out var centre, out var axis, out _, out _, out _);

            if (full)
            {
                var c = region.DeepestPoint(out _, (p, cap) => occ.HitsPolygon(p, 1f) ? 0f : cap, 24);
                for (int rows = wanted; rows >= 1; rows--)
                {
                    var bw = Clone(proto, c, axis, rows);
                    if (Fits(region, occ, bw)) return bw;
                }
                return null;
            }

            // Anchor on the outline and facing (into the area).
            float2 anchor, dir;
            Anchor(ctx, region, gates, centre, sideMode, out anchor, out dir);
            Bowl best = null;
            float reach = math.length(region.Max - region.Min);
            int budget = 0;
            foreach (float lateral in new[] { 0f, -12f, 12f, -24f, 24f })
            {
                var start = anchor + new float2(-dir.y, dir.x) * lateral;
                for (float t = 2f; t < reach && budget < 900; t += 2f)
                {
                    var f = start + dir * t;
                    if (!region.Contains(f)) { if (t > proto.R0 * 2f + 20f) break; continue; }
                    int lo = best?.Rows ?? 0;
                    for (int rows = wanted; rows > lo; rows--)
                    {
                        budget++;
                        var bw = Clone(proto, f, dir, rows);
                        if (!Fits(region, occ, bw)) continue;
                        best = bw;
                        break;
                    }
                    if (best != null && best.Rows == wanted) return best;
                }
                if (best != null) return best;
            }
            return best;
        }

        private static Bowl Clone(Bowl p, float2 f, float2 d, int rows) => new Bowl
        {
            F = f, D = d, Base = EventKit.Angle(d), Half = p.Half, Full = p.Full, Straight = p.Straight, R0 = p.R0, RowDepth = p.RowDepth,
            Rise = p.Rise, StageSize = p.StageSize, Rows = rows, Promenade = p.Promenade, Mode = p.Mode,
        };

        private static void Anchor(LayerContext ctx, LayerRegion region, List<Gate> gates, float2 centre, string mode, out float2 anchor, out float2 dir)
        {
            var gate = gates.Count > 0 ? gates[0].Position : region.Ring[0];
            if (mode == "auto")
            {
                // Sloped ground: the stage at the low side, the seats climbing the slope. Else away from the road.
                float lo = float.MaxValue, hi = float.MinValue;
                for (float s = 0f; s < region.Perimeter; s += math.max(4f, region.Perimeter / 64f))
                {
                    float h = ctx.H(region.PointAt(s, out _, out _, out _));
                    lo = math.min(lo, h); hi = math.max(hi, h);
                }
                mode = hi - lo > 2.5f ? "low" : ctx.Site.NearbyNets.Count > 0 || ctx.Input.Shape.Attachments.Count > 0 ? "far" : "edge";
            }
            switch (mode)
            {
                case "road":
                {
                    anchor = region.ClosestOnBoundary(gate, out int seg, out _);
                    dir = region.InwardNormal(seg);
                    return;
                }
                case "far":
                {
                    var away = math.normalizesafe(centre - gate, new float2(0, 1));
                    if (LeisureKit.Chord(region, centre, away, 0f, out var a, out var b2))
                    {
                        anchor = math.dot(b2 - a, away) >= 0f ? b2 : a;
                        dir = -away;
                        return;
                    }
                    break;
                }
                case "low":
                {
                    float best = float.MaxValue; anchor = region.Ring[0];
                    for (float s = 0f; s < region.Perimeter; s += math.max(3f, region.Perimeter / 96f))
                    {
                        var p = region.PointAt(s, out _, out _, out _);
                        float h = ctx.H(p);
                        if (h < best) { best = h; anchor = p; }
                    }
                    dir = math.normalizesafe(centre - anchor, new float2(0, 1));
                    return;
                }
            }
            // Longest edge, facing inwards.
            int bestSeg = 0; float len = 0f;
            for (int i = 0; i < region.Ring.Length; i++)
            {
                float l = math.distance(region.Ring[i], region.Ring[(i + 1) % region.Ring.Length]);
                if (l > len) { len = l; bestSeg = i; }
            }
            anchor = math.lerp(region.Ring[bestSeg], region.Ring[(bestSeg + 1) % region.Ring.Length], 0.5f);
            dir = region.InwardNormal(bestSeg);
        }

        /// <summary>Seating, promenade and the stage's back (pit ramp when dug) all inside, clear of reserved polygons/discs.</summary>
        private static bool Fits(LayerRegion region, Occupancy occ, Bowl b)
        {
            bool Ok(float2 p, float margin) => region.ContainsDisc(p, margin) && !occ.HitsPolygon(p, 0.5f) && !occ.Discs.Overlaps(p, 0.5f) && !occ.Corridors.Overlaps(p, 0.5f);
            float rim = b.Rim + 0.5f;
            // Behind/around the stage: the orchestra disc (and the pit ramp when dug) must be inside.
            float back = b.Mode == Mode.Dig ? b.R0 + b.Height / EdgeSlope * 0.9f : b.R0 + 1f;
            back = math.min(back, rim);
            int nb = 12;
            for (int i = 0; i < nb; i++)
                if (!Ok(b.F + EventKit.Dir(i * 2f * math.PI / nb) * back, 0.5f)) return false;
            if (b.Straight)
            {
                float w = rim * b.Tan + 2f;
                for (int i = 0; i <= 8; i++)
                {
                    float f = -1f + i / 4f;
                    if (!Ok(b.F + b.D * rim + b.Perp * (w * f), 0.3f)) return false;
                    if (!Ok(b.F + b.D * (rim * 0.5f) + b.Perp * (w * 0.5f * f), 0.3f)) return false;
                }
                return true;
            }
            float half = b.Half + b.Ext;
            int n = math.max(6, (int)math.ceil(half * 2f * rim / 6f));
            for (int i = 0; i <= n; i++)
            {
                float a = b.Base - half + 2f * half * i / n;
                if (!Ok(b.F + EventKit.Dir(a) * rim, 0.3f)) return false;
                if ((i & 1) == 0 && !Ok(b.F + EventKit.Dir(a) * ((rim + b.R0) * 0.5f), 0.3f)) return false;
            }
            return true;
        }

        // ───────────── terrain ─────────────

        private static void Terraform(LayerContext ctx, LayerRegion region, Bowl b, float cell)
        {
            float reach = b.Reach;
            var min = math.max(region.Min, b.F - reach);
            var max = math.min(region.Max, b.F + reach);
            int nx = math.max(1, (int)math.ceil((max.x - min.x) / cell)), ny = math.max(1, (int)math.ceil((max.y - min.y) / cell));
            int cells = 0; float raise = 0f, lower = 0f;
            for (int iy = 0; iy < ny; iy++)
            {
                if ((iy & 7) == 0) ctx.Check();
                for (int ix = 0; ix < nx; ix++)
                {
                    var c = min + (new float2(ix, iy) + 0.5f) * cell;
                    if (!region.Contains(c)) continue;
                    float h0 = ctx.H(c);
                    float target = HeightAfter(ctx, region, b, c, cell);
                    float off = target - h0;
                    if (math.abs(off) < 0.15f) continue;
                    var square = LayerRegion.Rect(c, new float2(1, 0), cell * 0.5f, cell * 0.5f);
                    float2[] ring = square;
                    bool whole = true;
                    foreach (var q in square) if (!region.Contains(q)) { whole = false; break; }
                    if (!whole)
                    {
                        var pieces = Poly.Intersection(square, region.Ring);
                        if (pieces.Count == 0 || pieces[0].Outer == null || pieces[0].Outer.Length < 3) continue;
                        ring = pieces[0].Outer;
                    }
                    ctx.Plan.Terraform.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(ring, ctx.H), Kind = off > 0f ? TerraformKind.Raise : TerraformKind.Lower, TargetHeight = target, Permanent = true,
                    });
                    cells++;
                    raise = math.max(raise, off); lower = math.max(lower, -off);
                }
            }
            if (cells == 0) return;
            ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
            ctx.Plan.Facts["terrainCells"] = cells;
            ctx.Plan.Facts["terrainMaxRaise"] = raise;
            ctx.Plan.Facts["terrainMaxLower"] = lower;
        }

        // ───────────── paths ─────────────

        private static float2 RowPoint(Bowl b, float angle, float r) =>
            b.Straight ? b.F + b.D * r + b.Perp * (r * math.tan(angle)) : b.F + EventKit.Dir(b.Base + angle) * r;

        private static List<float> AisleAngles(LayerContext ctx, Bowl b)
        {
            int n = ctx.I("aisles");
            var r = new List<float>();
            if (n <= 0) return r;
            if (b.Full) { for (int i = 0; i < n; i++) r.Add((i + 0.5f) * 2f * math.PI / n); return r; }
            if (b.Straight)
            {
                for (int i = 0; i < n; i++) r.Add(math.atan((-1f + 2f * (i + 1) / (n + 1)) * b.Tan));
                return r;
            }
            for (int i = 0; i < n; i++) r.Add(-b.Half + (i + 1) * 2f * b.Half / (n + 1));
            return r;
        }

        private static void Paths(LayerContext ctx, LayerRegion region, Occupancy occ, Bowl b, AssetRef asset, List<(float2 a, float2 b)> spine)
        {
            float top = b.Promenade ? b.RP : b.ROut + 0.5f;
            foreach (float a in AisleAngles(ctx, b))
            {
                float k = b.Straight ? 1f / math.cos(a) : 1f;
                var p0 = b.Straight ? b.F + (b.D + b.Perp * math.tan(a)) * b.R0 : RowPoint(b, a, b.R0);
                var p1 = b.Straight ? b.F + (b.D + b.Perp * math.tan(a)) * top : RowPoint(b, a, top);
                LeisureKit.Path(ctx, occ, p0, p1, NetRole.Pathway, asset);
                spine.Add((p0, p1));
            }
            if (!b.Promenade) return;
            if (b.Straight)
            {
                float w = top * b.Tan + 3f;
                var a0 = b.F + b.D * top - b.Perp * w; var a1 = b.F + b.D * top + b.Perp * w;
                LeisureKit.Path(ctx, occ, a0, a1, NetRole.PathwayWide, asset);
                spine.Add((a0, a1));
                // Side paths down to the front corners.
                foreach (int s in new[] { -1, 1 })
                {
                    var e = s < 0 ? a0 : a1;
                    var front = b.F + b.D * b.R0 + b.Perp * (s * (b.R0 * b.Tan + 2.5f));
                    if (region.ContainsDisc(front, 1f) && !region.CrossesBoundary(e, front, false)) { LeisureKit.Path(ctx, occ, e, front, NetRole.Pathway, asset); spine.Add((e, front)); }
                }
                return;
            }
            float half = b.Full ? math.PI : b.Half + b.Ext;
            spine.AddRange(EventKit.Arc(ctx, occ, b.F, top, b.Base - half, b.Base + half, NetRole.Pathway, asset));
            if (b.Full) return;
            foreach (int s in new[] { -1, 1 })
            {
                var e = b.F + EventKit.Dir(b.Base + s * half) * top;
                var f = b.F + EventKit.Dir(b.Base + s * half) * (b.R0 - 0.3f);
                if (region.ContainsDisc(f, 0.5f) && !region.CrossesBoundary(e, f, false)) { LeisureKit.Path(ctx, occ, e, f, NetRole.Pathway, asset); spine.Add((e, f)); }
            }
        }

        // ───────────── stage, extras ─────────────

        // Art pass a2: designed defaults. The automatic pools would put billboards on the stage
        // (stage / kiosk categories), clock hands beside it (centerpiece.statue), a 17 m branded
        // marquee in the crowd (events.tent) and random conifers behind it.
        private static readonly string[] RimTrees = { "OakTree01", "NA_LindenTree01", "EU_ChestnutTree01" };
        private static readonly string[] Poplars = { "EU_PoplarTree01", "BirchTree01" };
        private static readonly string[] Deckchairs = { "DeckChair01", "DeckChair02", "DeckChair03" };
        private static readonly string[] Blankets = { "PicnicBlanket01", "PicnicBlanket02", "PicnicBlanket04", "PicnicBlanket05" };
        private static readonly AssetRef Screen = new AssetRef("BillboardLarge01 - Placesstages", "BillboardHuge02 - Placesstages", "StageSmall01");
        private static readonly AssetRef Bandstand = new AssetRef("Gazebo03", "Gazebo02", "Gazebo01");
        private static readonly AssetRef Tent = new AssetRef("CampgroundShelter02", "Pergola02", "Pergola01");

        private static AssetRef StageAsset(LayerContext ctx, string kind) =>
            PlazaArt.Or(ctx, "stageAsset", kind == "screen" ? Screen : kind == "bandstand" ? Bandstand : PlazaArt.Stage);

        private static string[] Species(LayerContext ctx, string[] designed)
        {
            var l = ctx.L("treeSpecies");
            return l != null && l.Length > 0 ? l : designed;
        }

        private static string StageCategory(LayerContext ctx, string kind)
        {
            string chosen = ctx.S("stageAsset");
            switch (kind)
            {
                case "screen": return LeisureKit.Category(ctx, chosen, AssetCategories.EventScreen, AssetCategories.Stage, AssetCategories.Centerpiece);
                case "bandstand": return LeisureKit.Category(ctx, chosen, AssetCategories.Stage, AssetCategories.Shelter, AssetCategories.Centerpiece);
                case "podium": return LeisureKit.Category(ctx, chosen, AssetCategories.Stage, AssetCategories.CenterpieceStatue, AssetCategories.Centerpiece);
                default: return LeisureKit.Category(ctx, chosen, AssetCategories.Stage, AssetCategories.Shelter, AssetCategories.Centerpiece);
            }
        }

        private static void Stage(LayerContext ctx, LayerRegion region, Occupancy occ, Bowl b, string style)
        {
            var orch = EventKit.Clip(region, LayerRegion.Circle(b.F, math.max(1.5f, b.R0 - 0.5f), 24));
            if (ctx.B("orchestra") && orch != null) ctx.Surface(orch, SurfaceRole.Paving, Palette(ctx).Focal, AssetCategories.SurfacePaving);
            string kind = ctx.S("stageKind");
            string cat = StageCategory(ctx, kind);
            var stagePos = b.Full ? b.F : b.F - b.D * (b.R0 * (kind == "screen" ? 0.5f : 0.35f));
            float yaw = b.Full ? b.Base : math.atan2(b.D.x, b.D.y);
            if (cat != null)
            {
                var o = EventKit.Place(ctx, stagePos, yaw, cat, StageAsset(ctx, kind), Hashing.Mix(ctx.Seed, 0x57A6u));
                o.Position.y = HeightAfter(ctx, region, b, stagePos, 4f);
            }
            if (style == "classic" && !b.Full)
            {
                // Flags either side of the stage (the statue category holds clock hands).
                string statue = EventKit.Optional("", AssetCategories.MemorialFlag, AssetCategories.CenterpieceStatue);
                if (statue != null)
                    foreach (int s in new[] { -1, 1 })
                    {
                        var p = stagePos + b.Perp * (s * (b.StageSize * 0.8f + 1f));
                        if (math.distance(p, b.F) < b.R0 - 1f && region.ContainsDisc(p, 1f))
                            EventKit.Place(ctx, p, yaw, statue, PlazaArt.FlagPole, Hashing.Mix(ctx.Seed, 0x57A7u + (uint)(s + 1)));
                    }
            }
            // Backdrop: an arc of trees behind the stage.
            if (ctx.B("backdrop") && !b.Full)
            {
                float r = b.StageSize + 4f;
                int n = 0;
                for (float a = -1.1f; a <= 1.1f; a += 7f / r)
                {
                    var p = b.F + EventKit.Dir(b.Base + math.PI + a) * r;
                    uint v = Hashing.Mix(ctx.Seed, 0xBD00u + (uint)n++);
                    // Columnar poplars: a green wall behind the stage (acoustic screen of the classic open-air theatre).
                    if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(Species(ctx, Poplars), v), null, 2.5f, v, 1f, 0.3f, 3, 3))
                        ctx.Plan.Objects[ctx.Plan.Objects.Count - 1].Position.y = HeightAfter(ctx, region, b, p, 4f);
                }
            }
        }

        private static void Extras(LayerContext ctx, LayerRegion region, Occupancy occ, Bowl b, string style, float cell)
        {
            switch (style)
            {
                case "meadow":
                {
                    // Crowd barrier along the front of the lawn, a mixing tent in the crowd, toilets off the promenade.
                    if (!b.Full)
                    {
                        var fence = AssetMix.One("");
                        float r = b.R0 + 0.6f;
                        int pieces = math.max(2, (int)math.ceil(2f * b.Half * r / 12f));
                        for (int i = 0; i < pieces; i++)
                            ctx.StraightNet(RowPoint(b, -b.Half + 2f * b.Half * i / pieces, r), RowPoint(b, -b.Half + 2f * b.Half * (i + 1) / pieces, r), NetRole.Fence, fence);
                    }
                    string tent = EventKit.Optional("", AssetCategories.EventTent, AssetCategories.Shelter);
                    if (tent != null)
                    {
                        var p = b.F + b.D * (b.R0 + (b.ROut - b.R0) * 0.55f);
                        if (EventKit.Put(ctx, occ, p, math.atan2(-b.D.x, -b.D.y), tent, Tent, 3f, Hashing.Mix(ctx.Seed, 0xF0Fu), 0.5f, 0.3f, 0))
                            ctx.Plan.Objects[ctx.Plan.Objects.Count - 1].Position.y = HeightAfter(ctx, region, b, p, cell);
                    }
                    string toilet = LeisureKit.Category(ctx, "", AssetCategories.EventToilet, AssetCategories.Restroom);
                    EventKit.CabinRow(ctx, occ, toilet, AssetMix.One(""), toilet == AssetCategories.EventToilet ? 8 : 1, toilet == AssetCategories.EventToilet ? 1.6f : 6f,
                        Hashing.Mix(ctx.Seed, 0x7012u), new List<float2> { b.F }, b.Rim + 3f);
                    break;
                }
                case "cinema":
                {
                    // Projection booth on the axis behind the rows.
                    string booth = EventKit.Optional("", AssetCategories.Kiosk, AssetCategories.EventTent, AssetCategories.Shelter);
                    if (booth != null)
                    {
                        var p = b.F + b.D * (b.Rim + 3.5f);
                        EventKit.Put(ctx, occ, p, math.atan2(-b.D.x, -b.D.y), booth, AssetMix.Pick(PlazaArt.Kiosks, 0u), 2.4f, Hashing.Mix(ctx.Seed, 0xB007u), 0.5f, 0.2f, 0);
                    }
                    break;
                }
                case "bandstand":
                {
                    string lights = EventKit.Optional("", AssetCategories.EventFestoon);
                    if (lights != null)
                    {
                        float r = b.Rim + 2f;
                        int n = 0;
                        for (float a = 0f; a < 2f * math.PI; a += 10f / r)
                        {
                            var p = b.F + EventKit.Dir(b.Base + a) * r;
                            if (EventKit.Put(ctx, occ, p, 0f, lights, AssetMix.One(""), 0.4f, Hashing.Mix(ctx.Seed, 0x1F10u + (uint)n), 0.5f, 0.1f)) n++;
                        }
                    }
                    break;
                }
            }
            if (ctx.B("lightTowers"))
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.EventLightTower, AssetCategories.FloodLight, AssetCategories.Lamp);
                if (cat != null)
                {
                    var spots = new List<float2>();
                    if (b.Full) for (int i = 0; i < 4; i++) spots.Add(b.F + EventKit.Dir(b.Base + (i + 0.5f) * math.PI * 0.5f) * (b.Rim + 3f));
                    else
                    {
                        float h = b.Straight ? math.atan(b.Tan) : b.Half;
                        foreach (int s in new[] { -1, 1 })
                        {
                            spots.Add(b.Straight ? b.F + b.D * (b.R0 + 1f) + b.Perp * (s * ((b.R0 + 1f) * b.Tan + 3f)) : b.F + EventKit.Dir(b.Base + s * (h + 3.5f / (b.R0 + 1f))) * (b.R0 + 1f));
                            spots.Add(b.F + EventKit.Dir(b.Base + s * h * 0.6f) * (b.Rim + 3f));
                        }
                    }
                    int n = 0;
                    foreach (var p in spots)
                        if (EventKit.Put(ctx, occ, p, LeisureKit.Yaw(p, b.F), cat, AssetMix.One(""), 0.8f, Hashing.Mix(ctx.Seed, 0x1750u + (uint)n), 0.5f, 0.1f))
                        {
                            ctx.Plan.Objects[ctx.Plan.Objects.Count - 1].Position.y = HeightAfter(ctx, region, b, p, cell);
                            n++;
                        }
                }
            }
        }

        // ───────────── seating ─────────────

        private static List<float2[]> BowlRings(Bowl b, float r0, float r1)
        {
            var rings = new List<float2[]>();
            if (b.Straight)
            {
                float w0 = r0 * b.Tan, w1 = r1 * b.Tan + (b.Promenade && r1 > b.ROut ? 3f : 0f);
                rings.Add(Poly.EnsureCcw(new[] { b.F + b.D * r0 - b.Perp * w0, b.F + b.D * r0 + b.Perp * w0, b.F + b.D * r1 + b.Perp * w1, b.F + b.D * r1 - b.Perp * w1 }));
                return rings;
            }
            if (b.Full)
            {
                rings.Add(EventKit.Sector(b.F, r0, r1, b.Base, b.Base + math.PI));
                rings.Add(EventKit.Sector(b.F, r0, r1, b.Base + math.PI, b.Base + 2f * math.PI));
                return rings;
            }
            float half = r1 > b.ROut ? b.Half + b.Ext : b.Half;
            rings.Add(EventKit.Sector(b.F, r0, r1, b.Base - half, b.Base + half));
            return rings;
        }

        private static PavingPalette Palette(LayerContext ctx) =>
            GroundPalettes.Resolve(ctx.Settings, ctx.S("style") == "cinema" ? GroundPalettes.Modern : GroundPalettes.Sandstone, "seatSurface");

        private static void SeatSurface(LayerContext ctx, LayerRegion region, Bowl b)
        {
            string ground = ctx.S("seatGround");
            if (ground == "grass") return;
            // s18: rows in the palette's field round an orchestra in its focal stone (Automatic: warm sandstone).
            var asset = ground == "paved" ? Palette(ctx).Field : AssetMix.One("");
            foreach (var ring in BowlRings(b, b.R0, b.ROut))
            {
                var clipped = EventKit.Clip(region, ring);
                if (clipped == null) continue;
                if (ground == "paved") ctx.Surface(clipped, SurfaceRole.Paving, asset, AssetCategories.SurfacePaving);
                else ctx.Surface(clipped, SurfaceRole.Ground, asset, AssetCategories.SurfaceNatural);
            }
        }

        private static int Seats(LayerContext ctx, LayerRegion region, Occupancy occ, Bowl b, float cell, out float capacity)
        {
            capacity = 0f;
            string kind = ctx.S("seats");
            float area = 0f;
            foreach (var ring in BowlRings(b, b.R0, b.ROut)) area += Poly.Area(ring);
            if (kind == "lawn") { capacity = area / 1.2f; return 0; }
            string cat; float radius, perSeat;
            AssetRef asset;
            switch (kind)
            {
                case "deckchairs":
                    cat = LeisureKit.Category(ctx, "", AssetCategories.EventDeckchair, AssetCategories.Bench); radius = 0.7f; perSeat = 1f; asset = AssetMix.Pick(Deckchairs, 0u); break;
                case "picnic":
                    cat = LeisureKit.Category(ctx, "", AssetCategories.Picnic, AssetCategories.Bench); radius = 1.4f; perSeat = 6f; asset = AssetMix.Pick(Blankets, 0u); break;
                default:
                    cat = AssetAvailability.Require(ctx.Plan, AssetCategories.Bench, new[] { ctx.S("benchAsset") }, Keys.Category(AssetCategories.Bench)) ? AssetCategories.Bench : null;
                    radius = 0.9f; perSeat = 4f; asset = PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0u); break;
            }
            if (cat == null) { capacity = area / 1.2f; return 0; }
            if (cat == AssetCategories.Bench) { radius = 0.9f; perSeat = 4f; asset = PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0u); }
            radius = math.max(0.4f, math.min(radius, b.RowDepth * 0.5f - 0.1f));   // neighbouring rows must not block each other
            float spacing = math.max(ctx.F("seatSpacing"), radius * 2f + 0.2f);
            int seats = 0;
            var aisles = AisleAngles(ctx, b);
            float clear = Composer.DefaultHalfWidth(NetRole.Pathway) + radius + 0.2f;
            for (int k = 0; k < b.Rows; k++)
            {
                float r = b.R0 + (k + 0.5f) * b.RowDepth;
                // Row as a line coordinate s (arc length, or lateral offset for straight rows), split into
                // sections between the aisles; seats spread evenly inside each section.
                float s0, s1;
                if (b.Straight) { s1 = r * b.Tan; s0 = -s1; }
                else if (b.Full) { s0 = 0f; s1 = 2f * math.PI * r; }
                else { s0 = -b.Half * r; s1 = b.Half * r; }
                var cuts = new List<float>();
                foreach (float a in aisles) cuts.Add(b.Straight ? r * math.tan(a) : a * r);
                cuts.Sort();
                var sections = new List<float2>();
                if (b.Full && cuts.Count > 0)
                    for (int i = 0; i < cuts.Count; i++)
                    {
                        float a0 = cuts[i] + clear, a1 = (i + 1 < cuts.Count ? cuts[i + 1] : cuts[0] + s1) - clear;
                        if (a1 > a0) sections.Add(new float2(a0, a1));
                    }
                else
                {
                    float from = s0 + radius * 0.5f;
                    foreach (float c in cuts) { if (c - clear > from) sections.Add(new float2(from, c - clear)); from = c + clear; }
                    if (s1 - radius * 0.5f > from) sections.Add(new float2(from, s1 - radius * 0.5f));
                }
                var pts = new List<float2>();
                foreach (var sec in sections)
                {
                    float len = sec.y - sec.x;
                    if (len < radius * 2f) continue;
                    int n = math.max(1, (int)math.floor(len / spacing));
                    for (int i = 0; i < n; i++)
                    {
                        float s = sec.x + (i + 0.5f) * len / n;
                        pts.Add(b.Straight ? b.F + b.D * r + b.Perp * s : b.F + EventKit.Dir(b.Base + s / r) * r);
                    }
                }
                foreach (var p in pts)
                {
                    float yaw = b.Straight ? math.atan2(-b.D.x, -b.D.y) : LeisureKit.Yaw(p, b.F);
                    if (!EventKit.Put(ctx, occ, p, yaw, cat, asset, radius, Hashing.Mix(ctx.Seed, 0x5EA0u + (uint)seats), 0.5f, 0.1f)) continue;
                    ctx.Plan.Objects[ctx.Plan.Objects.Count - 1].Position.y = HeightAfter(ctx, region, b, p, cell);
                    seats++;
                }
            }
            capacity = seats * perSeat;
            return seats;
        }

        // ───────────── rim ─────────────

        private static void Rim(LayerContext ctx, LayerRegion region, Occupancy occ, Bowl b, string style)
        {
            float outer = b.Rim + 0.9f;
            float half = b.Full ? math.PI : b.Half + b.Ext;
            Func<float, float2> at = b.Straight
                ? (Func<float, float2>)(f => b.F + b.D * outer + b.Perp * (f * (outer * b.Tan + 3f)))
                : f => b.F + EventKit.Dir(b.Base + f * half) * outer;
            float length = b.Straight ? 2f * (outer * b.Tan + 3f) : 2f * half * outer;
            float lamps = ctx.F("lampSpacing");
            if (lamps > 0f && b.Promenade)
            {
                var lamp = PlazaArt.Or(ctx, "lampAsset", PlazaArt.ParkLamp);
                int n = math.max(1, (int)(length / lamps));
                for (int i = 0; i <= n; i++)
                {
                    if (b.Full && i == n) break;
                    var p = at(-1f + 2f * i / n);
                    LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A70u + (uint)i), 0.4f, 0.1f);
                }
            }
            int kiosks = ctx.I("kiosks");
            if (kiosks > 0)
            {
                string cat = LeisureKit.Category(ctx, "", style == "meadow" ? AssetCategories.EventFoodTruck : AssetCategories.Kiosk, AssetCategories.Kiosk);
                if (cat != null)
                {
                    int placed = 0;
                    // Just outside the rim; further out when the (real-width) promenade takes that ring.
                    for (int i = 0; i < kiosks * 9 && placed < kiosks; i++)
                    {
                        float r = outer + 2.8f + (i / (kiosks * 3)) * 2.5f;
                        float f = kiosks == 1 && i == 0 ? 0.7f : -0.85f + 1.7f * ((i * 0.618034f) % 1f);
                        var p = b.Straight ? b.F + b.D * r + b.Perp * (f * (r * b.Tan + 3f)) : b.F + EventKit.Dir(b.Base + f * half) * r;
                        if (EventKit.Put(ctx, occ, p, LeisureKit.Yaw(p, b.F), cat, AssetMix.Pick(style == "meadow" ? PlazaArt.Carts : PlazaArt.Kiosks, (uint)i), 2.4f, Hashing.Mix(ctx.Seed, 0x6170u + (uint)i), 0.8f, 0.3f)) placed++;
                    }
                }
            }
            if (ctx.B("rimTrees") && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, ctx.L("treeSpecies"), Keys.Category(AssetCategories.Tree)))
            {
                float r = outer + 6f;
                float h2 = b.Full ? math.PI : half + 6f / math.max(6f, r);
                int n = 0;
                if (b.Straight)
                {
                    float w = r * b.Tan + 6f;
                    foreach (var p in LeisureKit.Along(b.F + b.D * r - b.Perp * w, b.F + b.D * r + b.Perp * w, 9f))
                    {
                        uint v = Hashing.Mix(ctx.Seed, 0x7C00u + (uint)n++);
                        LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(Species(ctx, RimTrees), v), null, 2.5f, v, 1f, 0.4f, 3, 3);
                    }
                }
                else
                    for (float a = -h2; a <= h2 + 1e-3f; a += 9f / r)
                    {
                        if (b.Full && a > h2 - 9f / r) break;
                        var p = b.F + EventKit.Dir(b.Base + a) * r;
                        uint v = Hashing.Mix(ctx.Seed, 0x7C00u + (uint)n++);
                        LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(Species(ctx, RimTrees), v), null, 2.5f, v, 1f, 0.4f, 3, 3);
                    }
            }
        }

        // ───────────── fallback ─────────────

        /// <summary>No room for even one row: a stage on a lawn with a few benches facing it.</summary>
        private void Fallback(LayerContext ctx, LayerRegion region, Occupancy occ, List<Gate> gates, AssetRef pathAsset)
        {
            var c = region.DeepestPoint(out float clear, null, 16);
            string cat = StageCategory(ctx, ctx.S("stageKind"));
            if (cat != null && clear > 3f) { EventKit.Place(ctx, c, 0f, cat, StageAsset(ctx, ctx.S("stageKind")), Hashing.Mix(ctx.Seed, 0x57A6u)); occ.Take(c, 3f, 0); }
            var bench = PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0u);
            for (int i = 0; i < 8; i++)
            {
                var p = c + EventKit.Dir(i * math.PI / 4f) * math.min(8f, clear - 1f);
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, c), ObjectRole.Bench, bench, null, 0.9f, Hashing.Mix(ctx.Seed, 0xFB00u + (uint)i));
            }
            ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core);
            CommonStats(ctx);
        }
    }
}
