// Town / market square: a paved, walkable square (the plaza's ground) with a ring of trees just
// inside the outline and benches between them, a civic centrepiece (statue, fountain or clock) in
// the middle, and a grid of market stalls — back-to-back pairs facing the aisles — filling the
// space between. Lamps along the aisles, cafe tables under the trees, bollards on the road edges.
//
// rf-shops3: entrances as in the parking tool (MarketEntrances): the player places them, nothing is suggested, Build waits for
// the first. Gates for people, car entrances (a small car park at that edge) and driveways (a service lane for the stall vans).
// Big market ("fill") is the market quarter (MarketQuarter): market streets planned from the gates, food courts, planting
// islands and café groves, stalls to the site's real rim.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Commercial;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Plaza;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(57, Group = "squares")]
    public sealed class MarketSquareLayer : LeisureLayer, ILinkedControls
    {
        /// <summary>(s18) Ground palette: picking one clears the per-role surface picks; a role pick reads Custom.</summary>
        public string[] OnValueSet(SettingsBag settings, string controlId) => GroundPalettes.OnValueSet(settings, controlId, "surface");
        public void Derive(SettingsBag settings) => GroundPalettes.Derive(settings, "surface");

        /// <summary>A square with its own entrance kinds (MarketEntrances), not the park's typed entrances.</summary>
        protected override bool TypedEntrances => false;

        /// <summary>(rf-shops3) Build waits for the first entrance (any kind: one gate for people makes a market).</summary>
        public override Readiness CheckReady(AreaInput input) => MarketEntrances.Ready(input);

        public const string AccessKind = PlazaLayer.AccessKind;

        protected override string Area => "Market";
        public override string Id => "marketSquare";
        public override string Icon => "Media/Game/Icons/PropsCommercial.svg";
        public override string[] Emits => new[] { RegionTags.Paved, RegionTags.Core, StreetFrontage.ShopsTag };
        public override float MinArea => 400f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("shops", ("count", 3.0), ("where", "edges")), StreetShopsFeature(Area), Feature("restrooms", ("count", 1.0)),
            Feature("decals", ("theme", "paving"), ("ground", "paved"), ("density", 0.6)), Feature("terrain", ("shape", "flatten")),
        };

        /// <summary>(rf-shops3) No automatic entrances: the player places every one ("it wouldn't do anything unless I did").</summary>
        public override List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("stalls", 16, 0, 60, 1, "count", ControlTier.Essential, "market").Tip();
            c.Slider("aisle", 4.5, 3, 8, 0.5, "m", ControlTier.Essential, "market").Tip();
            c.Toggle("treeRing", true, ControlTier.Essential, "market").Tip();
            // (shops2) The user, Big market: "shouldn't it fill the entire area and have more intermingled other things?"
            c.Toggle("fill", false, ControlTier.Essential, "market").Tip();
            c.Choice("centerKind", "statue", ControlTier.Essential, "market", "none", "any", "fountain", "statue");
            c.Asset("stallAsset", AssetCategories.MarketStall, ControlTier.More, "market");
            c.Slider("treeSpacing", 8, 5, 14, 1, "m", ControlTier.More, "market").Layout(7).When("treeRing=true");
            c.Slider("cafe", 2, 0, 8, 1, "count", ControlTier.More, "market");
            c.Asset("cafeAsset", AssetCategories.CafeSeating, ControlTier.More, "market");
            c.Toggle("bollards", true, ControlTier.More, "market").Tip();
            c.Asset("bollardAsset", AssetCategories.Bollard, ControlTier.More, "market").When("bollards=true");
            c.Choice("pattern", "none", ControlTier.More, "market", "none", "grid", "border", "scatter");
            GroundPalettes.DefineControls(c, "ground", "surface");
            c.Asset("surface", AssetCategories.SurfacePaving, ControlTier.More, "ground");
            c.Asset("centerpiece", AssetCategories.Centerpiece, ControlTier.More, "assets");
            c.Asset("treeAsset", AssetCategories.Tree, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            // rf-shops3: the parking tool's way of placing entrances (gates, car entrances, driveways).
            MarketEntrances.Control(c, "access");
            c.Toggle("groundPaint", false, ControlTier.More, "ground").Tip();
            DefineStreetShopControls(c);
            c.RandomRange("stalls", 8, 24);
            c.Fixed("centerKind");
        }

        protected override void DefinePresets()
        {
            Preset("weekly", ("stalls", 16.0), ("aisle", 4.5), ("treeRing", true), ("centerKind", "statue"));
            // rf-shops3: the market quarter, a fountain (a planted centre without one) at the market cross, soil in the tree pits.
            Preset("bigMarket", ("stalls", 44.0), ("aisle", 4.0), ("treeRing", false), ("centerKind", "fountain"), ("cafe", 0.0), ("fill", true), ("groundPaint", true));
            Preset("farmers", ("stalls", 10.0), ("aisle", 5.5), ("treeRing", true), ("cafe", 3.0), ("centerKind", "fountain"), ("pattern", "border"));
        }

        /// <summary>(the plazas makeover) Every market square is the designed one; the old composition stays for reference.</summary>
        public static bool DesignedMarket = true;

        /// <summary>Square the "stalls" count is for (m²): a bigger square gets proportionally more, up to 4x.</summary>
        public const float StallsReferenceArea = 4000f;

        /// <summary>
        /// (rf-squares) The stalls a square of <paramref name="area"/> m² holds for the "stalls" setting: the setting is the
        /// market of a 0.4 ha square; a city block (1.5 ha) gets about four times as many, so the market fills the
        /// square instead of a small cluster in a big empty paved field.
        /// </summary>
        public static int ScaledStalls(int stalls, float area) =>
            stalls <= 0 ? 0 : math.min(240, (int)math.round(stalls * math.clamp(area / StallsReferenceArea, 1f, 4f)));

        /// <summary>(shops2) Spacing of the market streets (m) when the market fills the square.</summary>
        public const float StreetStep = 56f;

        /// <summary>
        /// (shops2, superseded by MarketQuarter in rf-shops3; kept) The filled market's streets: aisles 6 m wide across the square on its axes every <see cref="StreetStep"/> m
        /// (the middle ones are the main aisles), lined with trees; at every other crossing a food court (cafe seating round
        /// trees and benches), so the stall blocks are broken up by shade and places to sit, not one sea of stalls.
        /// </summary>
        internal static void MarketStreets(LayerContext ctx, Occupancy occ, LayerRegion region, float2 c, float2 axis, List<Gate> gates, List<float2> pits)
        {
            PlazaDressing.Extents(region, c, axis, out float minA, out float maxA, out float minB, out float maxB);
            var side = new float2(-axis.y, axis.x);
            var us = new List<float>(); var vs = new List<float>();
            for (float u = 0f; u <= maxA - 20f; u += StreetStep) { us.Add(u); if (u > 0f && -u >= minA + 20f) us.Add(-u); }
            for (float v = 0f; v <= maxB - 20f; v += StreetStep) { vs.Add(v); if (v > 0f && -v >= minB + 20f) vs.Add(-v); }
            const float half = 3.2f;
            foreach (var u in us) occ.TakeCorridor(c + axis * u + side * minB, c + axis * u + side * maxB, half, 6);
            foreach (var v in vs) occ.TakeCorridor(c + side * v + axis * minA, c + side * v + axis * maxA, half, 6);
            bool trees = AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, new[] { ctx.S("treeAsset") }, Keys.Category(AssetCategories.Tree));
            var ids = string.IsNullOrEmpty(ctx.S("treeAsset")) ? PlazaArt.ChestnutTrees : new[] { ctx.S("treeAsset") };
            int t = 0;
            // Trees along the streets that run along the square's axis (one side each), every 14 m, clear of the crossings.
            if (trees)
                foreach (var v in vs)
                    for (float u = minA + 8f; u <= maxA - 8f; u += 14f)
                    {
                        bool nearCross = false; foreach (var uu in us) if (math.abs(u - uu) < 8f) nearCross = true;
                        if (nearCross) continue;
                        var p = c + axis * u + side * (v + half + 2.1f);
                        if (!region.Contains(p) || PlazaDressing.NearGate(p, gates, 6f)) continue;
                        if (LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Tree, AssetMix.Pick(ids, (uint)t), null, 1.6f, Hashing.Mix(ctx.Seed, 0x7300u + (uint)t), 0.6f, 0.1f, 3, 3)) { pits.Add(p); t++; }
                    }
            // Food courts at every other crossing (not the centre, where the main aisles meet).
            bool seats = PlazaArt.Available(ctx, "cafeAsset", AssetCategories.CafeSeating, AssetCategories.Picnic);
            var bench = PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0);
            int courts = 0;
            for (int i = 0; i < us.Count; i++)
                for (int j = 0; j < vs.Count; j++)
                {
                    float u = us[i], v = vs[j];
                    if (u == 0f && v == 0f) continue;
                    int iu = (int)math.round(u / StreetStep), iv = (int)math.round(v / StreetStep);
                    if (((iu + iv) & 1) != 0) continue;
                    var q = c + axis * u + side * v;
                    const float R = 11f;
                    if (!region.Contains(q) || Placemaker.Core.Geometry.Poly.DistanceToBoundary(region.Ring, q) < R + 4f) continue;
                    // The court: a tree in each corner, cafe seating between them, benches facing in.
                    for (int k = 0; k < 4; k++)
                    {
                        float a = math.atan2(axis.y, axis.x) + math.PI * 0.25f + k * math.PI * 0.5f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var tp = q + dir * (R - 2f);
                        if (trees && LeisureKit.Put(ctx, occ, tp, 0f, ObjectRole.Tree, AssetMix.Pick(ids, (uint)t), null, 1.6f, Hashing.Mix(ctx.Seed, 0x7400u + (uint)t), 0.6f, 0.1f, 3, 3)) { pits.Add(tp); t++; }
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        float a = math.atan2(axis.y, axis.x) + k * math.PI * 0.5f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var sp = q + dir * (R - 4.5f) + new float2(-dir.y, dir.x) * 4.5f;
                        uint vr = Hashing.Mix(ctx.Seed, 0xC0C0u + (uint)(courts * 8 + k));
                        if (seats) LeisureKit.Put(ctx, occ, sp, math.atan2(dir.x, dir.y), ObjectRole.Furniture, PlazaArt.Or(ctx, "cafeAsset", PlazaArt.Patios, vr), AssetCategories.CafeSeating, 2.4f, vr, 0.5f, 0.2f, 0);
                        var bp = q + dir * (R - 1.5f) - new float2(-dir.y, dir.x) * 4f;
                        LeisureKit.Put(ctx, occ, bp, math.atan2(-dir.x, -dir.y), ObjectRole.Bench, bench, null, 1.1f, Hashing.Mix(vr, 7u), 0.5f, 0.1f);
                    }
                    occ.Take(q, R, 0);
                    courts++;
                }
            ctx.Plan.Facts["marketStreets"] = us.Count + vs.Count;
            ctx.Plan.Facts["foodCourts"] = courts;
            ctx.Plan.Facts["streetTrees"] = t;
        }

        /// <summary>The selected area itself (not a piece another type plans with this one, e.g. a shopping plaza's market court).</summary>
        private static bool IsRoot(AreaInput input)
        {
            var tags = input?.RegionTags;
            return tags == null || tags.Length == 0 || Array.IndexOf(tags, RegionTags.Root) >= 0;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var site = ctx.LayerRegion;
            // rf-shops3: the player's entrances only. A market planted inside another type (a shopping plaza's court) has none of its
            // own and keeps the suggested access points it always had.
            var placed = MarketEntrances.Resolve(ctx.Input, site);
            var lots = new List<MarketEntrances.CarPark>();
            var lanes = new List<MarketEntrances.ServiceLane>();
            var avoid = new List<float2[]>();
            if (placed.Count > 0)
            {
                lots = MarketEntrances.CarParks(ctx, new Mall(ctx), site, placed);
                foreach (var lot in lots) avoid.Add(lot.Ring);
                lanes = MarketEntrances.ServiceLanes(ctx, site, placed, avoid);
                foreach (var l in lanes) avoid.Add(l.Ring);
                ctx.Plan.Facts["carParks"] = lots.Count;
                ctx.Plan.Facts["serviceLanes"] = lanes.Count;
            }
            var region = MarketEntrances.Rest(site, avoid);
            ctx.LayerRegion = region;
            // The ways in for people, on what is left: the gates, and each car park's door into the market.
            var doors = new List<MarketQuarter.Door>();
            foreach (var g in placed)
            {
                if (!g.People) continue;
                var p = region.ClosestOnBoundary(g.At, out int seg, out _);
                doors.Add(new MarketQuarter.Door { At = p, In = region.InwardNormal(seg), Segment = seg, Street = g.HasStreet });
                g.Landed = "gate";
            }
            foreach (var lot in lots)
            {
                var p = region.ClosestOnBoundary(lot.Door, out int seg, out _);
                if (math.distance(p, lot.Door) > 6f) continue;
                doors.Add(new MarketQuarter.Door { At = p, In = region.InwardNormal(seg), Segment = seg, Lot = true });
            }
            var bays = new List<float2>();
            foreach (var l in lanes) bays.Add(region.ClosestOnBoundary(l.End, out _, out _));
            var gates = new List<Gate>();
            foreach (var d in doors) gates.Add(new Gate { Position = d.At, Segment = d.Segment, AttachId = -1, Kind = AccessKind });
            if (placed.Count == 0 && !IsRoot(ctx.Input))
                gates = Entrances.Resolve(ctx.Input, region, AccessKind, math.clamp((int)(region.Perimeter / 80f), 2, 8));
            ctx.Plan.Facts["gates"] = doors.Count;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            ctx.NavigationArea(region.Ring, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
            // (the plazas makeover, 2026-10-29: the user graded the market square D) an old-town market square on the designed
            // squares' kit (PlazaDesigns.Market): stalls in rows facing aisles round a market cross, a tree ring, cafes.
            if (DesignedMarket)
            {
                foreach (var b in bays) occ.Take(b, 5f, 6);
                PlazaLayer.PlanDesignedSquare(ctx, occ, region, gates, "market", GroundPalettes.Resolve(ctx.Settings, GroundPalettes.Cobble, "surface"));
                ctx.Stat("Placemaker.Stat.Stalls", ctx.Plan.Facts.TryGetValue("marketStalls", out var ms) ? ms : 0, Keys.UnitCount);
                return;
            }
            if (ctx.B("fill"))
            {
                PlanQuarter(ctx, region, occ, doors, bays, avoid);
                return;
            }
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
                occ.TakeCorridor(g.Position, g.Position + inward * 6f, 1.5f, 6);   // keep the way in clear
            }
            foreach (var b in bays) occ.Take(b, 5f, 6);
            LeisureKit.Frame(region, out var centre, out _, out _, out _, out _);
            var axis = PlazaDressing.SquareAxis(region);
            // rf-squares: two main aisles across the square on its own axes, meeting at the centre: a big market reads as
            // streets of stalls in four quarters. (Aisles to every access point cut the stall rows at odd angles.)
            {
                PlazaDressing.Extents(region, centre, axis, out float minA, out float maxA, out float minB, out float maxB);
                var sideAxis = new float2(-axis.y, axis.x);
                occ.TakeCorridor(centre + axis * minA, centre + axis * maxA, 2.4f, 6);
                occ.TakeCorridor(centre + sideAxis * minB, centre + sideAxis * maxB, 2.4f, 6);
            }
            region.DeepestPoint(out float ins, null, 12);
            // Art pass a2: an old-town square in setts (a fountain square in warm sandstone), framed in dark
            // flags, the market floor in a lighter stone, trees in pits — one paving tiling (PavingKit).
            string kind = ctx.S("centerKind");
            // s18: the ground palette (Automatic: old-town cobbles, sandstone round a fountain — never the ochre look).
            var palette = GroundPalettes.Resolve(ctx.Settings, kind == "fountain" ? GroundPalettes.Sandstone : GroundPalettes.Cobble, "surface");
            var pave = new PavingPlan { Axis = axis };
            var pits = new List<float2>();

            // Centrepiece on its own rondel: the market cross the stalls gather round.
            float coreR = 0f, rondel = 0f;
            if (kind != "none" && ins > 10f && PlazaDressing.CentreAssetsMissing(ctx))
            {
                // rf-squares: no fountain or statue installed (the clean game): a planted centre instead of an empty disc.
                coreR = math.clamp(ins * 0.16f, 5f, 9f);
                rondel = PlazaDressing.SpecimenCentre(ctx, occ, centre, coreR, pave, 0x5BE1u);
                occ.Take(centre, coreR + 1.5f, 0);
            }
            else if (kind != "none" && ins > 10f)
            {
                string sub = PlazaDressing.CenterCategory(kind);
                string cat = LeisureKit.Category(ctx, ctx.S("centerpiece"), sub ?? AssetCategories.Centerpiece, AssetCategories.Centerpiece);
                if (cat != null)
                {
                    var asset = PlazaDressing.CenterAsset(ctx, kind == "any" ? "fountain" : kind);
                    coreR = math.clamp(ins * 0.16f, 5f, 9f);
                    rondel = coreR;
                    ctx.Object(centre, math.atan2(axis.x, axis.y), ObjectRole.Centerpiece, asset, Hashing.Mix(ctx.Seed, 0xCE0u), -1, cat);
                    occ.Take(centre, coreR + 1.5f, 0);
                    // Benches on the rondel's diagonals, looking at it.
                    for (int k = 0; k < 4; k++)
                    {
                        float a = math.atan2(axis.y, axis.x) + math.PI * 0.25f + k * math.PI * 0.5f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var p = centre + dir * (coreR - 1.2f);
                        ctx.Object(p, math.atan2(-dir.x, -dir.y), ObjectRole.Bench, PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0), Hashing.Mix(ctx.Seed, 0xCE10u + (uint)k));
                    }
                }
            }

            // Tree ring just inside the frame, a bench between every other pair looking into the square.
            float fw = PlazaDressing.FrameWidth(region);
            if (ctx.B("treeRing") && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, new[] { ctx.S("treeAsset") }, Keys.Category(AssetCategories.Tree)))
            {
                var bench = PlazaArt.Or(ctx, "benchAsset", PlazaArt.Benches, 0);
                var ids = string.IsNullOrEmpty(ctx.S("treeAsset")) ? PlazaArt.ChestnutTrees : new[] { ctx.S("treeAsset") };
                float spacing = math.max(7f, ctx.Raw("treeSpacing"));
                // rf-squares: a true offset (the per-vertex inset folded over on a block's chamfered corners: no ring).
                var inner = region.InsetRing(fw + 2.6f);
                var ringRegion = inner != null ? LayerRegion.From(inner) : null;
                int t = 0;
                if (ringRegion != null)
                {
                    int count = math.max(1, (int)math.round(ringRegion.Perimeter / spacing));
                    float step = ringRegion.Perimeter / count;
                    float2 prev = default; bool havePrev = false;
                    for (int i = 0; i < count; i++)
                    {
                        var p = ringRegion.PointAt(step * (i + 0.5f), out _, out var inward, out _);
                        bool ok = !PlazaDressing.NearGate(p, gates, 6f) &&
                            LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Tree, AssetMix.Pick(ids, 0), null, 2.2f, Hashing.Mix(ctx.Seed, 0x7100u + (uint)t), 1f, 0.2f, 3, 3);
                        if (ok)
                        {
                            pits.Add(p);
                            if (havePrev && (t & 1) == 1 && math.distance(prev, p) < spacing * 1.4f)
                            {
                                var mid = (prev + p) * 0.5f;
                                LeisureKit.Put(ctx, occ, mid + inward * 0.6f, math.atan2(inward.x, inward.y), ObjectRole.Bench, bench, null, 1.1f, Hashing.Mix(ctx.Seed, 0x7200u + (uint)t), 0.5f, 0.1f);
                            }
                            t++;
                        }
                        prev = p; havePrev = ok;
                    }
                }
                ctx.Plan.Facts["ringTrees"] = t;
            }
            ctx.Check();

            // (shops2) A market filling the whole square: tree-lined market streets every ~55 m, food courts at some of their
            // crossings, and stalls in every block between them.
            bool fill = ctx.B("fill");
            if (fill) MarketStreets(ctx, occ, region, centre, axis, gates, pits);
            ctx.Check();

            // The market block: stalls back to back round the centre, facing their aisles, on a lighter floor.
            int stalls = 0;
            int asked = ctx.I("stalls");
            int wanted = fill ? (int)math.min(2400f, region.Area / 18f) : ScaledStalls(asked, region.Area);
            if (wanted > 0 && PlazaArt.Available(ctx, "stallAsset", AssetCategories.MarketStall, AssetCategories.Kiosk))
            {
                stalls = PlazaDressing.StallBlock(ctx, occ, region, centre, axis, wanted, ctx.F("aisle"), "stallAsset", 0x5700u, out var block);
                if (stalls > 0)
                    pave.Add(new OrientedRect { Center = block.Center, Axis = block.Axis, HalfLength = block.HalfLength + 2.5f, HalfWidth = block.HalfWidth + 1.5f }.Ring,
                        SurfaceRole.Plaza, palette.Accent, AssetCategories.SurfacePaving);
                if (stalls < asked && !fill) ctx.Issue(IssueSeverity.Info, "Placemaker.Market.Issue.Fewer", stalls, asked);
            }
            ctx.Check();

            // Shared plaza dressing (same controls: cafe/cafeAsset, bollards/bollardAsset, pattern).
            PlazaDressing.Cafe(ctx, occ);
            PlazaDressing.Bollards(ctx, occ, region, gates);
            PlazaDressing.Lighting(ctx, occ, region, gates, centre, 0f, PlazaArt.ParkLamp);
            PlazaDressing.Ground(ctx, pave, region, gates, centre, rondel, palette, pits, axis, false);
            pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);

            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            if (coreR > 0f) ctx.AddRegion(LayerRegion.Circle(centre, coreR, 20), "core", RegionTags.Core, RegionTags.Paved);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Stalls", stalls, Keys.UnitCount);
        }

        /// <summary>(rf-shops3) Big market: the market quarter (MarketQuarter) on what the car parks and service lanes leave.</summary>
        private void PlanQuarter(LayerContext ctx, LayerRegion region, Occupancy occ, List<MarketQuarter.Door> doors, List<float2> bays, List<float2[]> avoid)
        {
            var palette = GroundPalettes.Resolve(ctx.Settings, GroundPalettes.Cobble, "surface");
            // The market floor on Automatic: granite setts, or (a game without the paving packs) the warm vanilla setts, so the floor
            // is not one white field beside the pale streets, the light courts and the dark frame.
            string chosen = ctx.S(GroundPalettes.Control);
            if ((string.IsNullOrEmpty(chosen) || chosen == GroundPalettes.Automatic) && string.IsNullOrEmpty(ctx.S("surface")))
                palette.Field = new AssetRef(GroundSurfaces.Setts, GroundSurfaces.WarmCobble, GroundSurfaces.VTilesWarm);
            var pave = new PavingPlan { Axis = PlazaDressing.SquareAxis(region) };
            int stalls = MarketQuarter.Plan(ctx, region, occ, pave, palette, doors, bays, avoid, out _, out _);
            ctx.Check();
            // The player's own café terraces (the "cafe" slider; the quarter has its courts and groves).
            PlazaDressing.Cafe(ctx, occ);
            pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);
            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Stalls", stalls, Keys.UnitCount);
            if (stalls == 0) ctx.Issue(IssueSeverity.Info, "Placemaker.Market.Issue.Fewer", 0, ctx.I("stalls"));
        }
    }
}
