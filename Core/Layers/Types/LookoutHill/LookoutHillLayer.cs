// Lookout hill: a big landform raised out of the area and blended back to the original ground at the
// outline (HillField), a path that climbs it at a walkable grade (HillPaths: spiral or switchbacks),
// and a summit viewpoint — paved plaza, benches looking out, telescopes, a tower, pavilion or cairn.
// Trees cover the lower slopes up to a tree line so the top stays open for the view.
// Subtypes: hilltop lookout, sledding hill (a long open run on the far side, straw bales at the
// run-out), viewpoint mound, observation terraces (stepped levels with benches on every terrace edge)
// and kite hill (an open dome with kites and a windsock, picnics on the flanks).
// Every object, path and surface sits on the planned (raised) ground height.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.LookoutHill
{
    [AreaGenerator(208, Group = "nature")]
    public sealed class LookoutHillLayer : LeisureLayer
    {
        public const string SummitTag = "summit";

        protected override string Area => "LookoutHill";
        public override string Id => "lookoutHill";
        public override string Icon => "Media/Game/Icons/TerrainHeight.svg";
        public override float MinArea => 1500f;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag, SummitTag };

        public static readonly string[] Subtypes = { "hilltop", "sledding", "viewpointMound", "terraces", "kiteHill" };

        protected override int WantedEntrances(LayerRegion region) => math.min(2, Entrances.WantedByArea(region.Area));

        /// <summary>A feature labelled with its layer name (no Placemaker.Feature.* key of its own).</summary>
        private static FeatureInfo Plain(string layer, params (string key, object value)[] preset)
        {
            var f = Feature(layer, preset);
            f.LabelKey = "";
            return f;
        }

        protected override List<FeatureInfo> BuildFeatures()
        {
            var view = Feature(ViewpointAddOn.LayerId);
            view.Target = SummitTag;
            view.LabelKey = "Placemaker.LookoutHill.Feature.viewpoint";
            view.TooltipKey = "Placemaker.LookoutHill.Feature.viewpoint.Tooltip";
            return new List<FeatureInfo>
            {
                view, Feature("picnic", ("groups", 2.0)), Feature("restrooms"), Feature("shops", ("count", 1.0), ("where", "entrances")),
                StreetShopsFeature(Area), Feature("rocks", ("clusters", 4.0), ("size", "large")), Plain("bigtrees", ("density", 1.5)),
                Feature("decals", ("theme", "leaves"), ("density", 1.5)),
            };
        }

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("subtype", "hilltop", ControlTier.Essential, "hill", Subtypes).Tip();
            c.Slider("height", 16, 3, 40, 1, "m", ControlTier.Essential, "hill").Tip();
            c.Choice("shape", "mound", ControlTier.Essential, "hill", "mound", "cone", "dome", "mesa", "terraced").Tip();
            c.Choice("summitAt", "centre", ControlTier.More, "hill", "centre", "far", "highest").Tip();
            c.Slider("plateau", 8, 3, 30, 1, "m", ControlTier.More, "hill").Layout(3).Tip();
            c.Slider("maxSlope", 30, 10, 45, 5, "%", ControlTier.More, "hill").Tip();
            c.Slider("roughness", 30, 0, 100, 10, "%", ControlTier.More, "hill").Tip();
            c.Slider("levels", 3, 2, 5, 1, "count", ControlTier.More, "hill").When("shape=terraced");

            c.Choice("pathStyle", "spiral", ControlTier.Essential, "path", "spiral", "switchback", "direct", "none").Tip();
            c.Slider("grade", 9, 4, 20, 1, "%", ControlTier.More, "path").Tip();
            c.Choice("pathWidth", "narrow", ControlTier.More, "path", "narrow", "wide");
            c.Slider("restSpots", 4, 0, 12, 1, "count", ControlTier.More, "path").Tip();
            c.Toggle("lamps", false, ControlTier.More, "path");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "path");

            c.Choice("viewpoint", "tower", ControlTier.Essential, "summit", "plaza", "tower", "pavilion", "cairn", "none").Tip();
            c.Slider("summitBenches", 6, 0, 16, 1, "count", ControlTier.More, "summit");
            c.Slider("telescopes", 0, 0, 6, 1, "count", ControlTier.More, "summit").Tip();   // 0: no vanilla telescope exists (art pass a3)
            c.Toggle("summitPaint", true, ControlTier.More, "summit").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "summit").When("summitPaint=true");

            c.Slider("treeDensity", 90, 0, 400, 10, "perHa", ControlTier.Essential, "planting").Tip();
            c.Slider("treeLine", 65, 10, 100, 5, "%", ControlTier.More, "planting").Tip();
            c.Slider("bushDensity", 40, 0, 300, 10, "perHa", ControlTier.More, "planting");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Slider("runWidth", 26, 10, 60, 2, "m", ControlTier.Essential, "sledding").When("subtype=sledding").Tip();
            c.Toggle("runOut", true, ControlTier.More, "sledding").When("subtype=sledding").Tip();
            c.Slider("kites", 5, 0, 12, 1, "count", ControlTier.Essential, "kites").When("subtype=kiteHill").Tip();
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.Fixed("subtype");
            c.RandomRange("height", 6, 24);
            c.RandomRange("treeDensity", 30, 180);
            c.RandomRange("grade", 6, 12);
            c.Chance("lamps", 0.3f);
        }

        protected override void DefinePresets()
        {
            Preset("hilltop", ("subtype", "hilltop"), ("height", 18.0), ("shape", "mound"), ("summitAt", "centre"), ("plateau", 9.0),
                ("pathStyle", "spiral"), ("viewpoint", "tower"), ("treeDensity", 120.0), ("treeLine", 65.0));
            Preset("sledding", ("subtype", "sledding"), ("height", 14.0), ("shape", "dome"), ("summitAt", "far"), ("plateau", 8.0), ("maxSlope", 30.0),
                ("pathStyle", "switchback"), ("viewpoint", "pavilion"), ("treeDensity", 70.0), ("treeLine", 100.0), ("summitBenches", 4.0), ("telescopes", 0.0));
            Preset("viewpointMound", ("subtype", "viewpointMound"), ("height", 8.0), ("shape", "cone"), ("plateau", 5.0), ("pathStyle", "spiral"),
                ("viewpoint", "plaza"), ("telescopes", 0.0), ("summitBenches", 8.0), ("treeDensity", 40.0), ("treeLine", 30.0), ("roughness", 10.0));
            Preset("terraces", ("subtype", "terraces"), ("height", 16.0), ("shape", "terraced"), ("levels", 4.0), ("plateau", 12.0),
                ("pathStyle", "switchback"), ("viewpoint", "plaza"), ("summitBenches", 10.0), ("treeDensity", 50.0), ("lamps", true));
            Preset("kiteHill", ("subtype", "kiteHill"), ("height", 12.0), ("shape", "dome"), ("plateau", 14.0), ("pathStyle", "direct"),
                ("viewpoint", "cairn"), ("treeDensity", 30.0), ("treeLine", 25.0), ("kites", 6.0), ("summitBenches", 4.0), ("roughness", 0.0));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            string sub = ctx.S("subtype");
            region.DeepestPoint(out float inscribed);
            inscribed = math.max(3f, inscribed);

            // ── Summit & landform ──
            var summit = Summit(ctx, region, gates, inscribed);
            // (rf-nature) The summit plaza holds its centrepiece with room to walk round it and benches on its rim:
            // the 9 m plateau gave the hilltop gazebo (7.6 x 6.5 m) a plaza barely wider than itself, with the benches
            // jammed against it. The plateau grows to fit, as far as the area allows.
            float centreFit = CentreRadius(ctx.S("viewpoint"));
            float plateau = math.min(math.max(ctx.F("plateau"), centreFit > 0f ? (centreFit + 4.5f) / 0.85f : 0f), inscribed * 0.45f);
            var spec = new HillSpec
            {
                Height = ctx.F("height"), Shape = HillSpec.Parse(ctx.S("shape")), Summit = summit, Plateau = plateau,
                MaxSlope = ctx.F("maxSlope") / 100f, Roughness = ctx.F("roughness") / 100f, Levels = ctx.I("levels"),
                Seed = Hashing.Mix(ctx.Seed, 0x4111u),
            };
            // Obstacles inside the outline keep their ground: the hill flattens out around them.
            Func<float2, float> skip = null;
            bool anyReserved = ctx.Input.Reserved.Discs.Count + ctx.Input.Reserved.Corridors.Count + ctx.Input.Reserved.Polygons.Count > 0
                || (ctx.Site?.Obstacles != null && ctx.Site.Obstacles.Count > 0);
            if (anyReserved) skip = p => region.BoundaryDistance(p, 8f) >= 6f && occ.Clearance(p, 8f) < 6f ? 0f : float.MaxValue;
            var field = HillField.Build(ctx, region, spec, skip);
            field.Emit(ctx, region);
            if (field.Peak < spec.Height * 0.8f) ctx.Issue(IssueSeverity.Info, "Placemaker.LookoutHill.Issue.Lower", Math.Round(field.Peak, 1), Math.Round(spec.Height));
            ctx.Check();

            float summitR = math.max(4f, plateau * 0.85f);
            bool plaza = ctx.S("viewpoint") != "none";
            occ.Take(summit, summitR + 0.5f, 90);

            // ── Sledding run: claimed before the path so the path keeps off it ──
            float2 runA = default, runB = default; bool run = false;
            if (sub == "sledding") run = SledRun(ctx, occ, region, summit, summitR, gates, out runA, out runB);
            ctx.Check();

            // ── The way up ──
            float pathLen = ClimbPaths(ctx, occ, region, field, gates, summit, summitR);
            ctx.Check();

            // ── Summit ──
            if (plaza)
            {
                ctx.Surface(LayerRegion.Circle(summit, summitR, 24), SurfaceRole.Paving, AssetMix.Pick(sub == "terraces" ? TerracePaving : SummitPaving, 0u), AssetCategories.SurfacePaving);
                SummitDressing(ctx, occ, new Occupancy(region, ctx.Input.Reserved, ctx.Site), summit, summitR);
            }
            if (ctx.B("summitPaint"))
                TerrainPaint.Patch(ctx, LeisureKit.Blob(summit, summitR + 7f, Hashing.Mix(ctx.Seed, 0x5A1u), 16, 0.2f), ctx.I("paintLayer"), 0.7f, 3f);
            ctx.Check();

            // ── Subtype extras ──
            if (run) RunDressing(ctx, occ, region, runA, runB);
            if (spec.Shape == HillShape.Terraced) TerraceBenches(ctx, occ, field, spec);
            if (sub == "kiteHill") Kites(ctx, occ, field, summit);
            if (ctx.I("restSpots") > 0 && pathLen > 0f) PathBenches(ctx, occ, pathLen / ctx.I("restSpots") / ctx.Scale, BenchFor(ctx), 0xBE60u);
            if (ctx.B("lamps")) PathLamps(ctx, occ, 22f, AssetMix.Pick(Lamps, 0u), 0x1A60u);
            ctx.Check();

            // ── Planting below the tree line ──
            float treeTop = field.Peak * ctx.F("treeLine") / 100f + 0.01f;
            Func<float2, bool> reject = p => field.OffsetAt(p) > treeTop || math.distance(p, summit) < summitR + 4f;
            float ha = region.Area / 10000f;
            var treeIds = ctx.L("treeSpecies");
            if (treeIds.Length == 0 && AssetAvailability.Has(AssetCategories.Tree)) treeIds = TreesFor(sub);
            int trees = math.min(10000, (int)(ha * ctx.F("treeDensity")));
            // (nature makeover) groves with glades between, not an even scatter (the hilltop's wood stood on a lattice)
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                NatureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, treeIds, 2.3f, 30f, 0x7F00u, reject, 0.08f);
            int bushes = math.min(10000, (int)(ha * ctx.F("bushDensity")));
            if (bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, null, Keys.Category(AssetCategories.Bush)))
                NatureKit.Plant(ctx, occ, region, bushes, ObjectRole.Bush, Bushes, 1.1f, 16f, 0xB600u, reject, 0.1f);
            // rock coming through on the steep shoulders and round the crest; stones spill downhill
            Func<float2, float> built = p => ctx.H(p) + field.OffsetAt(p);
            float Steep(float2 p) { var gr = field.Gradient(p); return math.length(gr); }
            int rockGroups = sub == "kiteHill" ? (int)math.round(ha * 0.8f) : (int)math.round(ha * 2.2f) + 1;
            NatureKit.Rocks(ctx, occ, region, math.clamp(rockGroups, 1, 40), 0x40C0u, p =>
            {
                if (math.distance(p, summit) < summitR + 5f) return 0f;
                float crest = math.saturate(1f - math.abs(math.distance(p, summit) - summitR - 14f) / 18f);
                return math.saturate(Steep(p) * 2.2f + crest * 0.6f) * (run && math.distance(p, (runA + runB) * 0.5f) < math.distance(runA, runB) * 0.6f ? 0.1f : 1f);
            }, built);
            ctx.Check();

            // ── Regions, heights, stats ──
            ctx.AddRegion(LayerRegion.Circle(summit, summitR, 20), SummitTag, SummitTag, RegionTags.Paved);
            EmitGreenRegions(ctx, occ);
            Drape(ctx.Plan, field);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.HillHeight", Math.Round(field.Peak, 1), Keys.UnitM);
            ctx.Plan.Facts["peak"] = field.Peak;
            ctx.Plan.Facts["hillCells"] = field.Cells;
            ctx.Plan.Facts["summitX"] = summit.x;
            ctx.Plan.Facts["summitZ"] = summit.y;
        }

        private static float2 Summit(LayerContext ctx, LayerRegion region, List<Gate> gates, float inscribed)
        {
            var occ0 = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var deepest = region.DeepestPoint(out _, (p, c) => occ0.Clearance(p, c));
            string mode = ctx.S("summitAt");
            if (mode == "centre") return deepest;
            var g0 = gates.Count > 0 ? gates[0].Position : region.Centroid;
            float2 best = deepest; float bestScore = float.MinValue;
            const int grid = 24;
            var step = (region.Max - region.Min) / grid;
            for (int y = 0; y <= grid; y++)
                for (int x = 0; x <= grid; x++)
                {
                    var p = region.Min + step * new float2(x, y);
                    if (!region.Contains(p)) continue;
                    float d = occ0.Clearance(p, inscribed + 1f);
                    if (d < inscribed * 0.6f) continue;
                    float score = mode == "far" ? math.distance(p, g0) + d * 0.5f : ctx.H(p) * 10f + d * 0.05f;
                    if (score > bestScore) { bestScore = score; best = p; }
                }
            return best;
        }

        private float ClimbPaths(LayerContext ctx, Occupancy occ, LayerRegion region, HillField field, List<Gate> gates, float2 summit, float summitR)
        {
            string style = ctx.S("pathStyle");
            if (style == "none" || gates.Count == 0) return 0f;
            var role = ctx.S("pathWidth") == "wide" ? NetRole.PathwayWide : NetRole.Pathway;
            float hw = Composer.DefaultHalfWidth(role);
            var asset = AssetMix.One(ctx.S("pathAsset"));
            float total = 0f;
            var main = new List<float2>();
            for (int gi = 0; gi < gates.Count; gi++)
            {
                var g = gates[gi];
                var inward = region.InwardNormal(g.Segment);
                // A second entrance joins the first path when it can; otherwise it climbs on its own.
                if (gi > 0 && main.Count > 1)
                {
                    // (nature makeover) the spur joins the climb where it is a walk: the nearest point of the main path it can
                    // reach at a walking grade (the climb starts low, so there is one near the foot of the hill), not the
                    // nearest point, which on a big site was a straight line cut up the hillside
                    float H(float2 p) => field.OffsetAt(p) + ctx.H(p);
                    float maxGrade = math.max(0.08f, ctx.F("grade") / 100f * 1.4f);
                    int best = -1, near = -1; float bd = float.MaxValue, nd = float.MaxValue;
                    for (int i = 0; i < main.Count; i++)
                    {
                        float d = math.distance(main[i], g.Position);
                        if (d >= bd && d >= nd) continue;
                        if (!Clear(region, occ, g.Position, main[i], hw)) continue;
                        if (d < nd) { nd = d; near = i; }
                        if (d < bd && math.abs(H(main[i]) - H(g.Position)) <= math.max(1.5f, d * maxGrade)) { bd = d; best = i; }
                    }
                    if (best < 0 || bd > 160f) { best = near; bd = nd; }
                    if (best >= 0 && bd < 160f)
                    {
                        if (bd >= 1f)
                        {
                            var a = g.Position; var e = main[best];
                            var cb = a + inward * bd * 0.35f; var cc = e + (a - e) * 0.3f;
                            var spur = bd > 12f && CurveInside(region, a, cb, cc, e, hw) ? ctx.CurvedNet(a, cb, cc, e, role, asset) : ctx.StraightNet(a, e, role, asset);
                            AttachStart(spur, g);
                            var prev = a;
                            for (int k = 1; k <= 16; k++)
                            {
                                var q = Composer.Bezier(spur.A, spur.B, spur.C, spur.D, k / 16f).xz;
                                occ.TakeCorridor(prev, q, hw, 100);
                                occ.Paths.Add(prev, q, hw, 100);
                                prev = q;
                            }
                        }
                        total += bd;
                        continue;
                    }
                }
                var spec = new ClimbSpec
                {
                    Grade = ctx.F("grade") / 100f, Style = style, HalfWidth = hw, Spacing = hw * 2f + 3f,
                    LegLength = math.clamp(math.sqrt(region.Area) * 0.3f, 24f, 70f),
                    Summit = summit, SummitRadius = summitR, Seed = Hashing.Mix(ctx.Seed, (uint)gi + 0xC11u),
                };
                var pts = HillPaths.Climb(field, region, occ, g.Position, g.Position + inward * (hw + 2.5f), spec);
                if (pts.Count < 3) continue;
                if (!HillPaths.Reached(pts, spec)) ctx.Issue(IssueSeverity.Info, "Placemaker.LookoutHill.Issue.PathShort");
                Relax(pts, region, hw);
                var simple = Poly.SimplifyPolyline(pts, 0.6f);
                int n = simple.Length;
                for (int i = 0; i + 1 < n; i++)
                {
                    // (rf-nature) Smooth pieces (Catmull-Rom tangents through the walker's corners) instead of straight
                    // chords, so the climb reads as one flowing path, not a chain of kinks; straight where a curve
                    // would leave the outline. The first piece starts on the street (attached), like every park path.
                    var a = simple[i]; var b = simple[i + 1];
                    var p0 = i > 0 ? simple[i - 1] : a; var p3 = i + 2 < n ? simple[i + 2] : b;
                    var cb = a + (b - p0) / 6f; var cc = b - (p3 - a) / 6f;
                    var net = i > 0 && i + 2 < n && CurveInside(region, a, cb, cc, b, hw)
                        ? ctx.CurvedNet(a, cb, cc, b, role, asset) : ctx.StraightNet(a, b, role, asset);
                    if (i == 0) AttachStart(net, g);
                    // Pieces are added to the occupancy one by one; the last piece onto the plaza is fine.
                    var prev = a;
                    for (int k = 1; k <= 6; k++)
                    {
                        var q = Composer.Bezier(net.A, net.B, net.C, net.D, k / 6f).xz;
                        occ.TakeCorridor(prev, q, hw, 100);
                        occ.Paths.Add(prev, q, hw, 100);
                        prev = q;
                    }
                    total += math.distance(a, b);
                }
                if (main.Count == 0) main.AddRange(simple);
            }
            return total;
        }

        /// <summary>
        /// (rf-nature) Rounds the walker's corners (it steps 4 m at a time and turns where the hill's contours turn, so a
        /// hill on a square block gave a square spiral with sharp elbows): a few passes of neighbour averaging over the
        /// inner points; the street end, its entry point and the last steps onto the plaza stay put, and no point leaves the outline.
        /// </summary>
        private static void Relax(List<float2> pts, LayerRegion region, float hw)
        {
            if (pts.Count < 5) return;
            var tmp = new float2[pts.Count];
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < pts.Count; i++) tmp[i] = pts[i];
                for (int i = 2; i < pts.Count - 3; i++)
                {
                    var q = tmp[i] * 0.5f + (tmp[i - 1] + tmp[i + 1]) * 0.25f;
                    if (region.ContainsDisc(q, hw + 1f)) pts[i] = q;
                }
            }
        }

        private static void AttachStart(PlannedNet net, Gate g)
        {
            if (g.AttachId < 0 || math.distancesq(net.A.xz, g.Position) > 1e-4f) return;
            net.StartAttach = g.AttachId;
            net.StartAttachT = g.AttachT;
        }

        private static bool CurveInside(LayerRegion region, float2 a, float2 b, float2 c, float2 d, float hw)
        {
            for (int i = 1; i < 8; i++)
            {
                float u = i / 8f, v = 1f - u;
                var p = v * v * v * a + 3f * v * v * u * b + 3f * v * u * u * c + u * u * u * d;
                if (!region.ContainsDisc(p, hw + 0.5f)) return false;
            }
            return true;
        }

        private static bool Clear(LayerRegion region, Occupancy occ, float2 a, float2 b, float hw)
        {
            if (region.CrossesBoundary(a, b, true)) return false;
            int n = math.max(1, (int)(math.distance(a, b) / 2f));
            for (int i = 1; i < n; i++)
            {
                var p = math.lerp(a, b, i / (float)n);
                if (!region.Contains(p) || occ.Discs.Overlaps(p, hw, 0.2f) || occ.HitsPolygon(p, hw)) return false;
                if (i < n - 1 && occ.Corridors.Overlaps(p, hw, 0.2f)) return false;
            }
            return true;
        }

        private void SummitDressing(LayerContext ctx, Occupancy occ, Occupancy res, float2 summit, float r)
        {
            uint v = Hashing.Mix(ctx.Seed, 0x5077u);
            // Centrepiece: tower, pavilion or cairn.
            // Art pass a3: the lookout pool is a 56 m fire-station upgrade and a 32 m water-tower deck, so the
            // "tower" is the tall open gazebo, the pavilion a timber shelter, and the cairn is built from stones.
            string[] chain; AssetRef centre;
            switch (ctx.S("viewpoint"))
            {
                case "tower": chain = new[] { AssetCategories.Shelter }; centre = new AssetRef("", "Gazebo03", "Gazebo02"); break;
                case "pavilion": chain = new[] { AssetCategories.Shelter }; centre = new AssetRef("", "CampgroundShelter02", "Gazebo01"); break;
                case "cairn": chain = new[] { AssetCategories.Rock }; centre = new AssetRef("", "Boulder03", "Boulder07"); break;
                default: chain = null; centre = default; break;
            }
            float centreR = 0f;
            if (chain != null)
            {
                string cat = LeisureKit.Category(ctx, "", chain);
                float fit = math.min(CentreRadius(ctx.S("viewpoint")), math.max(2f, r - 2.5f));
                if (cat != null && res.Free(summit, fit, 0.5f, 0.2f) && !occ.Paths.Overlaps(summit, fit, 0.2f))
                {
                    centreR = fit;
                    var o = ctx.Object(summit, Hashing.Unit(v) * 6.283f, ObjectRole.Furniture, centre, v, -1, cat);
                    if (ctx.S("viewpoint") == "cairn")
                    {
                        // A cairn: one weathered boulder, small stones heaped round its foot.
                        o.Scale = 0.55f;
                        for (int k = 0; k < 9; k++)
                        {
                            float a = k * 0.698f + Hashing.Unit(Hashing.Mix(v, (uint)k + 70u)) * 0.4f;
                            var s = ctx.Object(summit + new float2(math.cos(a), math.sin(a)) * (1.5f + 0.4f * Hashing.Unit(Hashing.Mix(v, (uint)k + 90u))), a,
                                ObjectRole.Furniture, new AssetRef("", "Rock08", "Rock01"), Hashing.Mix(v, (uint)k + 80u), -1, AssetCategories.Rock);
                            s.Scale = 0.8f + 0.5f * Hashing.Unit(Hashing.Mix(v, (uint)k + 95u));
                        }
                    }
                }
            }
            // Benches on the rim looking out, telescopes between them.
            int benches = ctx.I("summitBenches"), scopes = ctx.I("telescopes");
            float rim = math.max(centreR + 1.8f, r - 1.3f);
            float phase = Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f;
            var placedAt = new List<float2>();
            for (int i = 0; i < benches; i++)
            {
                float a = phase + i * 2f * math.PI / benches;
                var p = summit + new float2(math.cos(a), math.sin(a)) * rim;
                if (Spot(ctx, occ, res, p, LeisureKit.Yaw(summit, p), ObjectRole.Bench, null, 1.0f, Hashing.Mix(v, 10u + (uint)i), BenchFor(ctx))) placedAt.Add(p);
            }
            string scope = scopes > 0 ? LeisureKit.Category(ctx, "", AssetCategories.HillTelescope) : null;
            if (scope != null)
                for (int i = 0; i < scopes; i++)
                {
                    float a = phase + (i + 0.5f) * 2f * math.PI / math.max(1, scopes);
                    var p = summit + new float2(math.cos(a), math.sin(a)) * math.max(centreR + 1.2f, r - 0.8f);
                    Spot(ctx, occ, res, p, LeisureKit.Yaw(summit, p), ObjectRole.Furniture, scope, 0.5f, Hashing.Mix(v, 40u + (uint)i), AssetMix.One(""));
                }
        }

        /// <summary>Half the footprint of the summit centrepiece (its bounding circle), 0 for none.</summary>
        private static float CentreRadius(string viewpoint)
        {
            switch (viewpoint)
            {
                case "tower": return 5f;      // Gazebo03, 7.6 x 6.5 m
                case "pavilion": return 4.5f; // CampgroundShelter02 / Gazebo01
                case "cairn": return 2.5f;    // a boulder with stones heaped round its foot
                default: return 0f;
            }
        }

        /// <summary>Puts an object on the summit plaza (inside the reserved plaza disc, clear of what is already there).</summary>
        private static bool Spot(LayerContext ctx, Occupancy occ, Occupancy res, float2 p, float yaw, ObjectRole role, string cat, float radius, uint v, AssetRef asset)
        {
            foreach (var o in ctx.Plan.Objects)
                if (math.distancesq(o.Position.xz, p) < (radius + 1.1f) * (radius + 1.1f)) return false;
            if (occ.Paths.Overlaps(p, radius, 0.2f) || !res.Free(p, radius, radius, 0.2f)) return false;
            ctx.Object(p, yaw, role, asset, v, -1, cat);
            return true;
        }

        // ───────────── art direction (art pass a3) ─────────────
        private static readonly string[] SummitPaving = { "Pavement Surface 01", "Pavement Surface 02" };
        private static readonly string[] TerracePaving = { "Tiles Surface 01", "Pavement Surface 02" };
        private static readonly string[] Lamps = { "LightpolePark03", "EU_LightpolePark03" };
        private static readonly string[] Bushes = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWildElderly01", "GreenBushWild02" };   // (nature makeover) no blue-flowered garden bush on a wild hill
        private static readonly string[] Blankets = { "PicnicBlanket01", "PicnicBlanket02", "PicnicBlanket03", "PicnicBlanket04", "PicnicBlanket05" };

        private static AssetRef BenchFor(LayerContext ctx) => ctx.S("subtype") == "terraces"
            ? AssetMix.Pick(new[] { "GardenBench01", "GardenBench02" }, 0u)
            : AssetMix.Pick(new[] { "Pack10-Bench01", "Pack10-Bench03" }, 0u);

        private static string[] TreesFor(string sub)
        {
            switch (sub)
            {
                case "sledding": return new[] { "SpruceTree01", "PineTree01", "BirchTree01", "EuropeanSpruce01" };
                case "terraces": return new[] { "NA_LindenTree01", "NA_LondonPlaneTree01" };
                case "kiteHill": return new[] { "OakTree01", "EU_ChestnutTree01" };
                case "viewpointMound": return new[] { "NA_LindenTree01", "OakTree01", "BirchTree01" };
                default: return new[] { "OakTree01", "PineTree01", "EU_ChestnutTree01", "BirchTree01" };
            }
        }

        private bool SledRun(LayerContext ctx, Occupancy occ, LayerRegion region, float2 summit, float summitR, List<Gate> gates, out float2 a, out float2 b)
        {
            a = b = summit;
            float hw = ctx.F("runWidth") * 0.5f;
            var g0 = gates.Count > 0 ? gates[0].Position : summit;
            var toGate = math.normalizesafe(g0 - summit);
            float best = 0f; float2 bestDir = default;
            for (int k = 0; k < 24; k++)
            {
                float ang = k * math.PI / 12f;
                var d = new float2(math.cos(ang), math.sin(ang));
                float len = 0f;
                for (float s = summitR + 2f; s < 2000f; s += 3f)
                {
                    if (!region.ContainsDisc(summit + d * s, hw * 0.6f + 6f)) break;
                    len = s;
                }
                // Long runs win; never straight at the entrance the path starts from.
                float score = len * (math.dot(d, toGate) > 0.6f ? 0.3f : 1f);
                if (score > best) { best = score; bestDir = d; }
            }
            if (best < summitR + 25f) { ctx.Issue(IssueSeverity.Info, "Placemaker.LookoutHill.Issue.NoRun"); return false; }
            a = summit + bestDir * (summitR + 1.5f);
            float runLen = 0f;
            for (float s = summitR + 2f; s < 2000f; s += 3f) { if (!region.ContainsDisc(summit + bestDir * s, hw * 0.6f + 6f)) break; runLen = s; }
            b = summit + bestDir * runLen;
            occ.TakeCorridor(a, b, hw, 95);
            var rect = new OrientedRect { Center = math.lerp(a, b, 0.5f), Axis = bestDir, HalfLength = math.distance(a, b) * 0.5f, HalfWidth = hw };
            var clipped = LeisureKit.Intersect(region, rect.Ring);
            if (clipped != null) ctx.Surface(clipped.Ring, SurfaceRole.Grass, AssetMix.One(""), AssetCategories.SurfaceGrass);
            ctx.Plan.Facts["runLength"] = math.distance(a, b);
            return true;
        }

        private void RunDressing(LayerContext ctx, Occupancy occ, LayerRegion region, float2 a, float2 b)
        {
            var d = math.normalizesafe(b - a); var side = new float2(-d.y, d.x);
            float hw = ctx.F("runWidth") * 0.5f;
            if (ctx.B("runOut"))
            {
                string bale = AssetAvailability.Has(AssetCategories.HillBale) ? AssetCategories.HillBale : null;
                int n = math.max(3, (int)(hw * 2f / 2.6f));
                int placed = 0;
                if (bale != null)
                    for (int i = 0; i < n; i++)
                    {
                        float u = (i / (float)(n - 1) - 0.5f) * 2f * hw;
                        var p = b + d * (2.5f + 0.02f * u * u) + side * u;
                        if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, a), ObjectRole.Furniture, AssetMix.One(""), bale, 0.9f,
                            Hashing.Mix(ctx.Seed, 0xBA1u + (uint)i), 0.5f, 0.1f)) placed++;
                    }
                if (placed == 0)
                {
                    // No bales installed: a run-out fence across the bottom instead.
                    var f0 = b + d * 3f - side * hw; var f1 = b + d * 3f + side * hw;
                    if (region.Contains(f0) && region.Contains(f1) && !region.CrossesBoundary(f0, f1, false)) ctx.StraightNet(f0, f1, NetRole.Fence, AssetMix.One(""));
                }
            }
            // Parents' benches beside the run-out.
            for (int e = -1; e <= 1; e += 2)
            {
                var p = b - d * 4f + side * (e * (hw + 3f));
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, b - d * 4f), ObjectRole.Bench, BenchFor(ctx), null, 1.3f, Hashing.Mix(ctx.Seed, 0xBE8u + (uint)(e + 1)), 0.5f, 0.3f);
            }
        }

        private void TerraceBenches(LayerContext ctx, Occupancy occ, HillField field, HillSpec spec)
        {
            int levels = math.clamp(spec.Levels, 2, 6);
            int placed = 0;
            for (int k = 1; k < levels; k++)
            {
                float level = field.Peak * k / levels;
                var pts = field.Contour(level, math.max(0.4f, field.Cell * 0.12f));
                var chosen = new List<float2>();
                for (int i = 0; i < pts.Count && chosen.Count < 6; i++)
                {
                    int idx = (int)(Hashing.Mix(ctx.Seed, (uint)(k * 1000 + i)) % (uint)pts.Count);
                    var p = pts[idx];
                    bool far = true;
                    foreach (var q in chosen) if (math.distance(q, p) < 16f) { far = false; break; }
                    if (!far) continue;
                    var down = -math.normalizesafe(field.OffsetGradient(p));
                    if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, p + down), ObjectRole.Bench, BenchFor(ctx), null, 1.3f,
                        Hashing.Mix(ctx.Seed, 0x7E2u + (uint)placed), 1f, 0.3f)) { chosen.Add(p); placed++; }
                }
            }
            ctx.Plan.Facts["terraceBenches"] = placed;
        }

        private void Kites(LayerContext ctx, Occupancy occ, HillField field, float2 summit)
        {
            int n = ctx.I("kites");
            if (n <= 0) return;
            var pts = new List<float2>();
            for (float lvl = 0.75f; lvl >= 0.35f && pts.Count < 200; lvl -= 0.1f) pts.AddRange(field.Contour(field.Peak * lvl, field.Peak * 0.05f));
            var chosen = new List<float2>();
            for (int i = 0; i < pts.Count && chosen.Count < n; i++)
            {
                var p = pts[(int)(Hashing.Mix(ctx.Seed, (uint)i + 0x417Eu) % (uint)pts.Count)];
                bool far = true;
                foreach (var q in chosen) if (math.distance(q, p) < 12f) { far = false; break; }
                if (far && LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(summit, p), ObjectRole.Furniture, AssetMix.Pick(Blankets, (uint)chosen.Count), AssetCategories.Picnic, 1.3f,
                    Hashing.Mix(ctx.Seed, 0x41E0u + (uint)chosen.Count), 1f, 0.5f)) chosen.Add(p);
            }
            // Kite flyers' camp on the flanks: picnic blankets facing the summit. No vanilla kite or windsock exists
            // (and the game skips unresolved props), so the flanks get blankets only (art pass a3).

        }

        /// <summary>Puts everything the layer planned onto the raised ground.</summary>
        private static void Drape(AreaPlan plan, HillField field)
        {
            foreach (var o in plan.Objects) o.Position.y = field.HeightAt(o.Position.xz);
            foreach (var n in plan.Nets)
            {
                n.A.y = field.HeightAt(n.A.xz); n.B.y = field.HeightAt(n.B.xz);
                n.C.y = field.HeightAt(n.C.xz); n.D.y = field.HeightAt(n.D.xz);
            }
            foreach (var s in plan.Surfaces)
                for (int i = 0; i < s.Ring.Length; i++) s.Ring[i].y = field.HeightAt(s.Ring[i].xz);
            foreach (var r in plan.Regions)
                for (int i = 0; i < r.Ring.Length; i++) r.Ring[i].y = field.HeightAt(r.Ring[i].xz);
        }
    }
}
