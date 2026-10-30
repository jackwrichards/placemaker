// Optional round-4 types: skate park, cemetery grounds, greenway (linear park).

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    /// <summary>
    /// Skate park: a paved bowl area with a cluster of ramps/rails in the middle (sports.skate),
    /// spectator benches round it, graffiti decals at the edges, lamps and a few trees outside.
    /// </summary>
    // Retired from the Make list (playtest: broken, and the game already ships a skate park). Kept loadable for old builds.
    [AreaGenerator(58, Group = "sports", Hidden = true)]
    public sealed class SkateParkLayer : LeisureLayer
    {
        protected override string Area => "Skate";
        public override string Id => "skatePark";
        public override string Icon => "Media/Game/Icons/Attractions.svg";
        public override float MinArea => 300f;
        protected override int WantedEntrances(LayerRegion region) => 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("shops", ("count", 1.0), ("where", "edges")), StreetShopsFeature(Area),
            Feature("decals", ("theme", "graffiti"), ("ground", "paved"), ("density", 1.0)), Feature("terrain", ("shape", "bowl"), ("height", 1.5)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("ramps", 8, 2, 24, 1, "count", ControlTier.Essential, "skate");
            c.Slider("paved", 70, 30, 100, 5, "%", ControlTier.Essential, "skate").Tip();
            c.Toggle("graffiti", true, ControlTier.Essential, "skate");
            c.AssetMulti("rampAssets", AssetCategories.SportsSkate, ControlTier.More, "skate");
            c.Asset("surface", AssetCategories.SurfacePaving, ControlTier.More, "skate");
            c.Slider("benches", 6, 0, 16, 1, "count", ControlTier.More, "skate");
            c.Toggle("lamps", true, ControlTier.More, "skate");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
        }

        protected override void DefinePresets()
        {
            Preset("street", ("ramps", 10.0), ("paved", 90.0), ("graffiti", true));
            Preset("bowl", ("ramps", 5.0), ("paved", 60.0), ("graffiti", false));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            Lawn.Cover(ctx, region.Ring, AssetMix.One(""));
            var c = region.DeepestPoint(out float ins, null, 14);
            float r = math.max(6f, ins * math.sqrt(ctx.F("paved") / 100f));
            var pad = region.InsetVertices(math.max(1f, ins - r));
            if (!Geometry.Poly.IsSimple(pad) || Geometry.Poly.SignedArea(pad) < 50f) pad = LayerRegion.Circle(c, math.min(r, ins - 1f), 24);
            string padCat = AssetCategories.SurfacePaving;
            ctx.Surface(pad, SurfaceRole.Paving, AssetMix.One(ctx.S("surface")), padCat);
            var padRegion = LayerRegion.From(pad) ?? region;
            foreach (var g in gates) LeisureKit.Path(ctx, occ, g.Position, padRegion.ClosestOnBoundary(g.Position, out _, out _), NetRole.Pathway, AssetMix.One(""));

            var ids = ctx.L("rampAssets");
            int ramps = 0;
            if (AssetAvailability.Require(ctx.Plan, AssetCategories.SportsSkate, ids, Keys.Category(AssetCategories.SportsSkate)))
            {
                int wanted = ctx.I("ramps");
                const float golden = 2.39996323f;
                for (int i = 0; i < wanted * 3 && ramps < wanted; i++)
                {
                    float rr = 5f * math.sqrt(i + 0.5f);
                    var p = c + new float2(math.cos(i * golden), math.sin(i * golden)) * rr;
                    if (!padRegion.ContainsDisc(p, 2.5f)) continue;
                    uint v = Hashing.Mix(ctx.Seed, 0x5CA7u + (uint)i);
                    if (LeisureKit.Put(ctx, occ, p, (v % 8u) * 0.785f, ObjectRole.Furniture, AssetMix.Pick(ids, v), AssetCategories.SportsSkate, 2.2f, v, 0.5f, 1.2f)) ramps++;
                }
            }
            var bench = AssetMix.One("");
            int b = 0;
            for (float s = 0; s < padRegion.Perimeter && b < ctx.I("benches"); s += padRegion.Perimeter / math.max(1, ctx.I("benches")))
            {
                var e = padRegion.PointAt(s, out _, out var inward, out _);
                var p = e - inward * 2f;
                if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, c), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE5u + (uint)b))) b++;
            }
            if (ctx.B("lamps"))
                for (int i = 0; i < 6; i++)
                {
                    var e = padRegion.PointAt(padRegion.Perimeter * (i + 0.25f) / 6f, out _, out var inward, out _);
                    LeisureKit.Put(ctx, occ, e - inward * 0.8f, 0f, ObjectRole.Lamp, AssetMix.One(""), null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A5u + (uint)i), 0.3f, 0.1f);
                }
            if (ctx.B("graffiti") && AssetAvailability.Has(AssetCategories.DecalGraffiti))
                for (float s = 1f; s < padRegion.Perimeter; s += 7f)
                {
                    var e = padRegion.PointAt(s, out var t, out var inward, out _);
                    ctx.Object(e + inward * 1.2f, math.atan2(t.x, t.y), ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(ctx.Seed, (uint)s), -1, AssetCategories.DecalGraffiti);
                }
            LeisureKit.Plant(ctx, occ, region, (int)(region.Area / 10000f * 30f), ObjectRole.Tree, ctx.L("x"), 2.4f, 12f, 0.5f, 0x7E5u, p => padRegion.Contains(p));
            EmitGreenRegions(ctx, occ, 60f);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Equipment", ramps, Keys.UnitCount);
        }
    }

    /// <summary>
    /// Cemetery grounds: straight solemn paths on a grid, sections of graves in rows (all facing the
    /// same way), hedges round each section, a chapel / memorial at the crossing, cypress-like tree rows.
    /// </summary>
    [AreaGenerator(59, Group = "parks")]
    public sealed class CemeteryGroundsLayer : LeisureLayer
    {
        protected override string Area => "Cemetery";
        public override string Id => "cemeteryGrounds";
        public override string Icon => "Media/Game/Icons/Deathcare.svg";
        public override float MinArea => 1000f;
        protected override int WantedEntrances(LayerRegion region) => 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("restrooms"), StreetShopsFeature(Area), Feature("decals", ("theme", "leaves"), ("density", 0.3)), Feature("terrain", ("shape", "flatten")),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("sectionSize", 24, 12, 40, 2, "m", ControlTier.Essential, "sections").Layout(12).Tip();
            c.Slider("graveSpacing", 2.5, 1.8, 4, 0.1, "m", ControlTier.Essential, "sections");
            c.Toggle("hedges", true, ControlTier.Essential, "sections");
            c.Toggle("chapel", true, ControlTier.Essential, "sections").Tip();
            c.AssetMulti("graveAssets", AssetCategories.Grave, ControlTier.More, "sections");
            c.Asset("chapelAsset", AssetCategories.CenterpieceStatue, ControlTier.More, "sections").When("chapel=true");
            c.Asset("treeAsset", AssetCategories.Tree, ControlTier.More, "sections");
            c.Toggle("fence", true, ControlTier.More, "boundary");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
        }

        protected override void DefinePresets()
        {
            // Art pass (a1): an old churchyard has weathered headstones; a lawn cemetery modern ones in open rows.
            Preset("churchyard", ("sectionSize", 16.0), ("graveSpacing", 2.2), ("hedges", true), ("graveAssets", (string[])CemeteryArt.OldStones.Clone()));
            Preset("lawn", ("sectionSize", 32.0), ("graveSpacing", 3.0), ("hedges", false), ("graveAssets", (string[])CemeteryArt.LawnStones.Clone()));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            Lawn.Cover(ctx, region.Ring, AssetMix.One("", ParkArt.Grass));
            var graph = ParkPathStyles.Formal(region, LeisureKit.Points(gates), p => region.ContainsDisc(p, 2f));
            var curves = ParkPaths.Build(graph, region, false, "narrow");
            ParkPaths.Index(curves, occ.Corridors, 100);
            ParkPaths.Index(curves, occ.Paths, 100);
            ParkPaths.Emit(ctx, curves, gates, AssetMix.One(""));
            LeisureKit.FrameToward(region, LeisureKit.Points(gates), out var centre, out var axis, out var side, out _, out _);

            if (ctx.B("chapel"))
            {
                string cat = LeisureKit.Category(ctx, ctx.S("chapelAsset"), AssetCategories.CenterpieceStatue, AssetCategories.Shelter, AssetCategories.Centerpiece);
                if (cat != null) LeisureKit.Put(ctx, occ, centre + axis * 8f + side * 8f, math.atan2(axis.x, axis.y), cat == AssetCategories.Shelter ? ObjectRole.Furniture : ObjectRole.Centerpiece,
                    AssetMix.One(ctx.S("chapelAsset"), CemeteryArt.Chapel), cat, 4f, Hashing.Mix(ctx.Seed, 0xC4Au), 1f, 0.5f, 0);
            }
            float size = ctx.F("sectionSize");
            var sections = LeisureKit.Pack(region, occ, centre, axis, size, size * 0.7f, 3.5f, 3.5f, 400, 2f);
            var ids = ctx.L("graveAssets");
            bool graves = AssetAvailability.Require(ctx.Plan, AssetCategories.Grave, ids, Keys.Category(AssetCategories.Grave));
            // The automatic grave pool mixes 6.5 m mausoleums into the 2.5 m rows; headstones only.
            if (ids.Length == 0) ids = CemeteryArt.Stones;
            string hedgeCat = ctx.B("hedges") ? LeisureKit.Category(ctx, "", AssetCategories.Hedge, AssetCategories.Bush) : null;
            float sp = ctx.F("graveSpacing");
            int count = 0;
            for (int i = 0; i < sections.Count && count < 6000; i++)
            {
                var r = sections[i];
                uint v = Hashing.Mix(ctx.Seed, 0x6A7Eu + (uint)i);
                if (graves)
                    for (float u = -r.HalfLength + 1.2f; u <= r.HalfLength - 1.2f; u += sp)
                        for (float w = -r.HalfWidth + 1.5f; w <= r.HalfWidth - 1.5f; w += sp * 1.4f)
                        {
                            uint gv = Hashing.Mix(v, (uint)(u * 10f) * 977u + (uint)(w * 10f));
                            ctx.Object(r.Local(u, w), r.Yaw, ObjectRole.Furniture, AssetMix.Pick(ids, gv), gv, -1, AssetCategories.Grave);
                            count++;
                        }
                if (hedgeCat != null)
                {
                    // Low hedge pieces (~4.1 m) along each side at their own pitch, turned along the side; an opening
                    // on the side facing the nearest path (the old rows were 1.8 m apart and all faced north).
                    var ring = r.Ring;
                    bool isHedge = hedgeCat == AssetCategories.Hedge;
                    int door = -1; float doorD = float.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        var mid = (ring[k] + ring[(k + 1) % 4]) * 0.5f;
                        if (occ.Paths.Nearest(mid, 200f, out var foot) >= 0 && math.distance(mid, foot) < doorD) { doorD = math.distance(mid, foot); door = k; }
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        var a = ring[k]; var b = ring[(k + 1) % 4];
                        float yaw = LeisureKit.Yaw(a, b);
                        var mid = (a + b) * 0.5f;
                        foreach (var p in LeisureKit.Along(a, b, isHedge ? 4.1f : 1.8f))
                        {
                            if (k == door && math.distance(p, mid) < 2.2f) continue;
                            ctx.Object(p, yaw, ObjectRole.Bush, isHedge ? AssetMix.One("", ParkArt.LowHedge) : AssetMix.Pick(ParkArt.GreenShrubs, v), v, -1, hedgeCat);
                        }
                    }
                }
            }
            PathLamps(ctx, occ, 30f, AssetMix.One("", ParkArt.ParkLamps), 0x1A6u);
            PathBenches(ctx, occ, 60f, AssetMix.One("", ParkArt.Benches), 0xBE6u);
            LeisureKit.Plant(ctx, occ, region, (int)(region.Area / 10000f * 25f), ObjectRole.Tree, ctx.S("treeAsset").Length > 0 ? new[] { ctx.S("treeAsset") } : CemeteryArt.Trees,
                2.2f, 10f, 0.5f, 0x7C6u, p => region.BoundaryDistance(p, 10f) > 6f, 3, 3);
            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(""), 3f);
            EmitGreenRegions(ctx, occ, 80f);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Graves", count, Keys.UnitCount);
        }
    }

    /// <summary>
    /// Greenway / linear park: one main walk (wide, meandering gently) along the long axis of a strip,
    /// a narrow parallel path where the strip is wide enough, planting along both sides, and rest stops
    /// (bench pair, bin, shade tree) every N metres.
    /// </summary>
    [AreaGenerator(60, Group = "nature")]
    public sealed class GreenwayParkLayer : LeisureLayer
    {
        protected override string Area => "Greenway";
        public override string Id => "greenwayPark";
        public override string Icon => "Media/Game/Icons/PedestrianPathWithBikeLanes.svg";
        public override float MinArea => 600f;
        protected override int WantedEntrances(LayerRegion region) => 2;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), CoreFeature("playground", ("size", "small")), StreetShopsFeature(Area),
            Feature("decals", ("theme", "leaves")), Feature("terrain", ("shape", "berms"), ("height", 1.5)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("restStops", 60, 25, 200, 5, "m", ControlTier.Essential, "layout").Layout(20).Tip();
            c.Slider("treeDensity", 60, 0, 200, 5, "perHa", ControlTier.Essential, "planting");
            c.Toggle("secondPath", true, ControlTier.Essential, "layout").Tip();
            c.Slider("bushDensity", 80, 0, 250, 5, "perHa", ControlTier.More, "planting");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "layout");
            c.Toggle("lamps", true, ControlTier.More, "layout");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
        }

        protected override void DefinePresets()
        {
            Preset("leafy", ("treeDensity", 110.0), ("bushDensity", 120.0));
            Preset("open", ("treeDensity", 30.0), ("bushDensity", 40.0), ("secondPath", false));
        }

        // Art pass a3: a greenway is a shared walk-and-cycle spine (vanilla path with bike lanes) under an
        // avenue of one species, a quieter footpath beside it joined at both ends, copses and meadow around.
        private static readonly string[] GreenwayAvenue = { "NA_LindenTree01", "NA_LondonPlaneTree01" };
        private static readonly string[] GreenwayTrees = { "OakTree01", "EU_ChestnutTree01", "NA_LindenTree01", "BirchTree01" };
        private static readonly string[] GreenwayBushes = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWildElderly01", "FlowerBushWild02" };
        private static readonly string[] GreenwayBenches = { "Pack10-Bench01", "Pack10-Bench03" };
        private static readonly string[] GreenwayLamps = { "LightpolePark01", "EU_LightpolePark01" };
        private static readonly string[] GreenwayBins = { "Trashbin01", "Trashbin02" };

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            Lawn.Cover(ctx, region.Ring, new AssetRef("", "Grass Surface 01"));
            string picked = ctx.S("pathAsset");
            var spineAsset = string.IsNullOrEmpty(picked) ? new AssetRef("", "PedestrianPathWithBikeLanes") : AssetMix.One(picked);
            var spine = NaturalPaths(ctx, occ, gates, 0, 4f, 40f, string.IsNullOrEmpty(picked) ? "wide" : "narrow", spineAsset);
            var footAsset = string.IsNullOrEmpty(picked) ? new AssetRef("", "Pavement Path") : AssetMix.One(picked);
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out float minor);
            if (ctx.B("secondPath")) Footpath(ctx, occ, region, spine, footAsset);   // fits itself (needs ~40 m of run)
            // Avenue: one species both sides of the spine at a steady rhythm.
            if (ctx.F("treeDensity") > 0f && AssetAvailability.Has(AssetCategories.Tree))
            {
                float spacing = ctx.M(16f), hw = Composer.DefaultHalfWidth(string.IsNullOrEmpty(picked) ? NetRole.PathwayWide : NetRole.Pathway);
                var avenue = AssetMix.Pick(GreenwayAvenue, 0u);
                int n = 0;
                foreach (var c in spine)
                {
                    float acc = spacing * 0.5f;
                    for (int i = 1; i < c.Samples.Length; i++)
                    {
                        var p0 = c.Samples[i - 1]; var p1 = c.Samples[i];
                        float len = math.distance(p0, p1);
                        if (len < 1e-3f) continue;
                        var dir = (p1 - p0) / len; var nrm = new float2(-dir.y, dir.x);
                        for (; acc <= len; acc += spacing)
                        {
                            var p = p0 + dir * acc;
                            bool nearGate = false;
                            foreach (var g in gates) if (math.distance(g.Position, p) < 10f) { nearGate = true; break; }
                            if (nearGate) continue;
                            foreach (float s in new[] { 1f, -1f })
                                if (LeisureKit.Put(ctx, occ, p + nrm * s * (hw + 3.2f), 0f, ObjectRole.Tree, avenue, null, 2.4f, Hashing.Mix(ctx.Seed, 0xA7E0u + (uint)n++), 1f, 0.3f, 3, 3)) { }
                        }
                        acc -= len;
                    }
                }
            }
            // Rest stops.
            var bench = AssetMix.Pick(GreenwayBenches, 0u); int stops = 0;
            foreach (var an in PathAnchors.Pick(PathAnchors.Beside(occ, 3f, 8f, 0.6f), 200, ctx.F("restStops"), false))
            {
                var t = an.Tangent;
                var away = math.normalizesafe(an.Position - an.Toward);
                var seat = an.Position - away * 1.7f;   // seats at the path edge (the anchor disc reaches 3 m out)
                bool ok = LeisureKit.Put(ctx, occ, seat + t * 1.4f, LeisureKit.Yaw(an.Position, an.Toward), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0x5700u + (uint)stops));
                LeisureKit.Put(ctx, occ, seat - t * 1.4f, LeisureKit.Yaw(an.Position, an.Toward), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0x5780u + (uint)stops));
                LeisureKit.Put(ctx, occ, seat - t * 3.2f, 0f, ObjectRole.Bin, AssetMix.Pick(GreenwayBins, 0u), null, 0.5f, Hashing.Mix(ctx.Seed, 0x5800u + (uint)stops));
                LeisureKit.Put(ctx, occ, an.Position + away * 4f, 0f, ObjectRole.Tree, AssetMix.Pick(GreenwayTrees, (uint)stops), null, 2.4f, Hashing.Mix(ctx.Seed, 0x5880u + (uint)stops), 1f, 0.3f, 3, 3);
                if (ok) stops++;
            }
            if (ctx.B("lamps")) PathLamps(ctx, occ, 25f, AssetMix.Pick(GreenwayLamps, 0u), 0x1A7u);
            float ha = region.Area / 10000f;
            // Copses in the meadow: strong clumping (low floor) so trees gather instead of dotting the lawn evenly.
            NatureKit.Plant(ctx, occ, region, (int)(ha * ctx.F("treeDensity")), ObjectRole.Tree, GreenwayTrees, 2.3f, 28f, 0x7E7u, null, 0.05f);   // (nature makeover) groves, no bands
            NatureKit.Plant(ctx, occ, region, (int)(ha * ctx.F("bushDensity")), ObjectRole.Bush, GreenwayBushes, 1f, 14f, 0xB07u, null, 0.08f);
            EmitGreenRegions(ctx, occ, 80f);
            CommonStats(ctx);
            ctx.Plan.Facts["restStops"] = stops;
        }

        /// <summary>
        /// The quiet footpath: the spine offset sideways (towards the side with more room), kept inside, emitted as
        /// short straight pieces and joined to the spine at both ends (the old straight chord along the principal
        /// axis ran diagonally across square sites and never met the spine, art pass a3).
        /// </summary>
        private static void Footpath(LayerContext ctx, Occupancy occ, LayerRegion region, List<PathCurve> spine, AssetRef asset)
        {
            // The spine comes as several curves (split at junctions): order all their samples along the walk's
            // overall direction (first to last sample of the network) into one polyline.
            var all = new List<float2>();
            foreach (var c in spine) all.AddRange(c.Samples);
            if (all.Count < 2) return;
            float2 from = all[0], to = all[0]; float far = -1f;
            foreach (var p in all) { float d = math.distancesq(p, all[0]); if (d > far) { far = d; to = p; } }
            far = -1f;
            foreach (var p in all) { float d = math.distancesq(p, to); if (d > far) { far = d; from = p; } }
            var dirAll = math.normalizesafe(to - from);
            all.Sort((x, y) => math.dot(x - from, dirAll).CompareTo(math.dot(y - from, dirAll)));
            var mainLine = Geometry.Poly.ResamplePolyline(all.ToArray(), 4f);
            float off = ctx.M(32f, 12f), keep = math.max(ctx.EdgeMargin + 3f, 4f);
            List<float2> Side(float sgn)
            {
                var pts = new List<float2>();
                var s = mainLine;
                var cur = new List<float2>();
                for (int i = 0; i < s.Length; i++)
                {
                    var d = math.normalizesafe(s[math.min(s.Length - 1, i + 1)] - s[math.max(0, i - 1)]);
                    var p = s[i] + new float2(-d.y, d.x) * (sgn * off);
                    if (region.ContainsDisc(p, keep) && occ.Paths.Clearance(p, off) > off * 0.6f) cur.Add(p);
                    else { if (cur.Count > pts.Count) pts = cur; cur = new List<float2>(); }   // longest continuous run
                }
                if (cur.Count > pts.Count) pts = cur;
                return pts;
            }
            var l = Side(1f); var r = Side(-1f);
            var run = l.Count >= r.Count ? l : r;
            if (run.Count < 3) return;
            var line = Geometry.Poly.ResamplePolyline(run.ToArray(), ctx.M(16f, 6f));
            if (line.Length < 2 || PolylineLength(line) < ctx.M(40f, 20f)) return;
            var ends = new[] { line[0], line[line.Length - 1] };
            var feet = new float2[2];
            for (int e = 0; e < 2; e++) feet[e] = occ.Paths.Nearest(ends[e], 200f, out var f) >= 0 ? f : ends[e];
            for (int i = 1; i < line.Length; i++) LeisureKit.Path(ctx, occ, line[i - 1], line[i], NetRole.Pathway, asset);
            for (int e = 0; e < 2; e++)
                if (math.distance(feet[e], ends[e]) > 2f && !region.CrossesBoundary(ends[e], feet[e], true)) LeisureKit.Path(ctx, occ, feet[e], ends[e], NetRole.Pathway, asset);
        }

        private static float PolylineLength(float2[] line)
        {
            float l = 0f;
            for (int i = 1; i < line.Length; i++) l += math.distance(line[i - 1], line[i]);
            return l;
        }
    }

    /// <summary>Art pass (a1): vanilla cemetery set (headstones, the old-cemetery mausoleum as chapel, columnar poplars).</summary>
    public static class CemeteryArt
    {
        public static readonly string[] OldStones =
            { "OldCemeteryTombstone01", "OldCemeteryTombstone02", "OldCemeteryTombstone03", "OldCemeteryTombstone04", "OldCemeteryTombstone05", "OldCemeteryTombstone06", "OldCemeteryTombstone07", "OldCemeteryTombstone08" };
        public static readonly string[] LawnStones =
            { "Tombstone01", "Tombstone02", "Tombstone03", "Tombstone04", "Tombstone05", "Tombstone06", "Tombstone07", "Tombstone08", "Tombstone09", "TombstoneDouble01", "TombstoneDouble02" };
        public static readonly string[] Stones =
            { "Tombstone01", "OldCemeteryTombstone02", "Tombstone03", "OldCemeteryTombstone05", "Tombstone05", "OldCemeteryTombstone07", "Tombstone07", "TombstoneDouble01" };
        public static readonly string[] Chapel = { "OldCemeteryMausoleum01" };
        /// <summary>Lombardy poplars stand in for cypresses; limes and birches as in old European cemeteries.</summary>
        public static readonly string[] Trees = { "EU_PoplarTree01", "NA_LindenTree01", "BirchTree01" };
    }
}
