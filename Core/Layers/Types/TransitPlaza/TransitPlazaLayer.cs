// Transit plaza: the paved place where people change between bus, taxi, bike, car and foot.
// Composition, from the street inwards:
//   1. the kerb lane along the road frontage: one asphalt lay-by strip with bus bays, a taxi rank and
//      kiss-and-ride spaces marked by marking decals (bays are paved areas + markings, not roads),
//   2. the platform behind it: waiting shelters at each bus bay, timetable/taxi signs at the bay
//      heads, bollards between the lay-by and the plaza,
//   3. optionally a park & ride car park at one end of the frontage (the Parking layer's planner),
//   4. the forecourt: paving + pedestrian area, bike racks in rows (under shelters at a bike hub),
//      a focal centrepiece, trees in a grid / rows / islands, benches, map boards, kiosks, lamps.
// Subtypes are presets: park & ride, bus interchange, bike hub, taxi rank, station forecourt.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Plaza;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Transport
{
    [AreaGenerator(91, Group = "transport")]
    public sealed class TransitPlazaLayer : LeisureLayer, ILinkedControls
    {
        /// <summary>(s18) Ground palette: picking one clears the per-role surface picks; a role pick reads Custom.</summary>
        public string[] OnValueSet(SettingsBag settings, string controlId) => GroundPalettes.OnValueSet(settings, controlId, "pavingAsset");
        public void Derive(SettingsBag settings) => GroundPalettes.Derive(settings, "pavingAsset");

        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        protected override string Area => "TransitPlaza";
        public override string Id => "transitPlaza";
        public override string Icon => "Media/Game/Icons/BusShelter.svg";
        public override float MinArea => 600f;
        public override string[] Emits => new[] { RegionTags.Paved, RegionTags.Core };
        protected override bool HostsStreetShops => false;

        /// <summary>(the plazas makeover) The forecourt is the designed one; the old composition stays for reference.</summary>
        public static bool DesignedForecourt = true;

        public static readonly string[] Styles = { "parkRide", "busPlaza", "bikeHub", "taxiRank", "forecourt" };
        public const string StatBusBays = "Placemaker.TransitPlaza.Stat.BusBays";
        public const string StatTaxiSpaces = "Placemaker.TransitPlaza.Stat.TaxiSpaces";
        public const string StatDropOff = "Placemaker.TransitPlaza.Stat.DropOff";
        public const string StatBikeRacks = "Placemaker.TransitPlaza.Stat.BikeRacks";

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            new FeatureInfo { Layer = "plazatrees", Target = RegionTags.Paved, LabelKey = "Placemaker.Plaza.Feature.trees" },
            new FeatureInfo { Layer = "kiosk", Target = RegionTags.Paved, LabelKey = "Placemaker.Plaza.Feature.kiosk" },
            CoreFeature("fountain", ("size", "small")),
            Feature("flowerbeds", ("density", 20.0), ("nearPaths", false)).Relabel("Placemaker.Plaza.Feature.flowerbeds"),
            Feature("shops", ("count", 2.0), ("where", "edges")),
            Feature("decals", ("theme", "cracks"), ("ground", "paved"), ("density", 0.6)),
            Feature("terrain", ("shape", "flatten")),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "busPlaza", ControlTier.Essential, "layout", Styles).Tip();
            c.Slider("lotShare", 0, 0, 75, 5, "%", ControlTier.Essential, "layout").Tip();
            c.Slider("busBays", 3, 0, 8, 1, "count", ControlTier.Essential, "bays");
            c.Slider("taxis", 2, 0, 12, 1, "count", ControlTier.Essential, "bays").Tip();
            c.Slider("dropOff", 2, 0, 12, 1, "count", ControlTier.Essential, "bays").Tip();
            c.Slider("bayLength", 15, 12, 20, 1, "m", ControlTier.More, "bays").Tip();
            c.Toggle("bayShelters", true, ControlTier.More, "bays");
            c.Toggle("bollards", true, ControlTier.More, "bays").Tip();

            c.Choice("stallAngle", "90", ControlTier.More, "spaces", "90", "60", "45");
            c.Slider("evShare", 4, 0, 30, 1, "%", ControlTier.More, "spaces");

            c.Slider("bikeRacks", 6, 0, 60, 1, "count", ControlTier.More, "bikes").Tip();
            c.Toggle("bikeShelters", false, ControlTier.More, "bikes").Tip();
            c.Toggle("bikeService", false, ControlTier.More, "bikes").Tip();

            c.Slider("benches", 10, 0, 40, 1, "count", ControlTier.More, "furniture");
            c.Slider("signs", 2, 0, 8, 1, "count", ControlTier.More, "furniture").Tip();
            c.Slider("kiosks", 1, 0, 4, 1, "count", ControlTier.More, "furniture");
            c.Toggle("centrepiece", false, ControlTier.More, "furniture").Tip();

            c.Choice("treeLayout", "grid", ControlTier.More, "planting", "grid", "rows", "islands", "none").Tip();
            c.Slider("treeSpacing", 10, 6, 20, 1, "m", ControlTier.More, "planting").Layout(6).When("treeLayout=grid");
            c.Toggle("planters", false, ControlTier.More, "planting").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Toggle("lamps", true, ControlTier.More, "lighting");
            c.Slider("lampSpacing", 16, 10, 30, 1, "m", ControlTier.More, "lighting").Layout(8).When("lamps=true");

            GroundPalettes.DefineControls(c, "ground", "pavingAsset");
            c.Asset("pavingAsset", AssetCategories.SurfacePaving, ControlTier.More, "ground");
            c.Asset("shelterAsset", AssetCategories.TransportShelter, ControlTier.More, "assets");
            c.Asset("bikeRackAsset", AssetCategories.BikeRack, ControlTier.More, "assets");
            c.Asset("signAsset", AssetCategories.Wayfinding, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            c.Asset("bollardAsset", AssetCategories.Bollard, ControlTier.More, "assets");
            EntranceMarkers(c);

            c.RandomRange("lotShare", 0, 40);
            c.RandomRange("bikeRacks", 0, 20);
            c.Fixed("style");
        }

        protected override void DefinePresets()
        {
            Preset("parkRide", ("style", "parkRide"), ("lotShare", 55.0), ("busBays", 2.0), ("taxis", 0.0), ("dropOff", 4.0), ("bikeRacks", 10.0),
                ("treeLayout", "rows"), ("benches", 6.0));
            Preset("busPlaza", ("style", "busPlaza"), ("busBays", 6.0), ("taxis", 2.0), ("dropOff", 0.0), ("benches", 14.0), ("signs", 4.0),
                ("kiosks", 2.0), ("treeLayout", "islands"));
            Preset("bikeHub", ("style", "bikeHub"), ("busBays", 1.0), ("taxis", 0.0), ("dropOff", 2.0), ("bikeRacks", 40.0), ("bikeShelters", true),
                ("bikeService", true), ("treeLayout", "rows"), ("benches", 6.0));
            Preset("taxiRank", ("style", "taxiRank"), ("busBays", 0.0), ("taxis", 10.0), ("dropOff", 6.0), ("signs", 3.0), ("benches", 8.0),
                ("treeLayout", "grid"), ("treeSpacing", 12.0));
            Preset("forecourt", ("style", "forecourt"), ("busBays", 1.0), ("taxis", 4.0), ("dropOff", 4.0), ("centrepiece", true),
                ("treeLayout", "rows"), ("benches", 16.0), ("signs", 3.0), ("bikeRacks", 8.0), ("kiosks", 2.0));
        }

        private enum Bay { Bus = 0, Taxi = 1, DropOff = 2 }

        // Art pass a2: designed defaults. The automatic pools are unusable here: transport.bikeRack holds
        // 40 m bicycle parking halls, transport.shelter is empty (→ pergolas), transport.sign holds a 7 m
        // tram shelter. Racks and bike shelters are the vanilla parking props; shelters the stop shelters.
        private static readonly AssetRef BusShelter = new AssetRef("EU_BusStop02", "NA_BusStop02", "TramStation Shelter");
        private static readonly AssetRef TaxiShelter = new AssetRef("EU_TaxiStop02", "NA_TaxiStop02", "EU_BusStop02");
        private static readonly string[] Racks = { "BikeRack01", "BikeRack02", "BikeRack03" };
        private static readonly string[] BikeShelters = { "BicycleShelter01", "BicycleShelter02", "BicycleShelter03" };

        protected override void PlanCore(LayerContext ctx)
        {
            var full = ctx.LayerRegion;
            string style = ctx.S("style");
            var gates = Gates(ctx);

            // Frontage: the longest road-facing outline piece, else the first entrance's edge, else the longest edge.
            Frontage(ctx, full, gates, out var fa, out var fb);
            TransportKit.EdgeFrame(full, fa, fb, out var t, out var n, out float uMin, out float uMax, out float vMax);
            float fLen = math.distance(fa, fb);

            // ── Park & ride car park at one end of the frontage ──
            SubLot lot = null;
            float share = ctx.F("lotShare") / 100f;
            float ru0 = uMin, ru1 = uMax;
            if (share > 0f)
            {
                float gateU = gates.Count > 0 ? math.dot(gates[0].Position - fa, t) : uMin;
                bool atEnd = gateU < (uMin + uMax) * 0.5f;
                float lotLen = math.max(30f, (uMax - uMin) * share);
                float u0 = atEnd ? uMax - lotLen : uMin - 3f, u1 = atEnd ? uMax + 3f : uMin + lotLen;
                var cut = TransportKit.FrameRect(fa, t, n, u0, u1, -3f, vMax + 3f);
                float e0 = math.max(u0, 0f), e1 = math.min(u1, fLen);
                float uE = e1 - e0 > 10f ? (e0 + e1) * 0.5f : (math.max(u0, uMin) + math.min(u1, uMax)) * 0.5f;
                var entrance = full.ClosestOnBoundary(fa + t * uE - n, out _, out _);
                lot = TransportKit.PlanLot(ctx, cut, entrance, TransportKit.Bag(("stallAngle", ctx.S("stallAngle")), ("evShare", (double)ctx.F("evShare")),
                    ("greenery", "edges"), ("lanterns", ctx.B("lamps"))), 0xB1u);
                if (lot != null)
                {
                    var rest = TransportKit.Subtract(full.Ring, cut, MinArea * 0.5f);
                    var restRegion = rest == null ? null : LayerRegion.From(rest);
                    if (restRegion != null)
                    {
                        ctx.LayerRegion = restRegion;
                        if (atEnd) ru1 = u0; else ru0 = u1;
                    }
                }
            }
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            if (lot != null) TransportKit.Occupy(occ, ctx.Plan);
            ctx.Check();

            // ── Kerb lane with bays ──
            int bus = ctx.I("busBays"), taxis = ctx.I("taxis"), drop = ctx.I("dropOff");
            float kerbDepth = bus > 0 ? 3.6f : 2.8f;
            float k0 = math.max(0f, ru0) + 1.5f, k1 = math.min(fLen, ru1) - 1.5f;
            float2[] plazaRing = region.Ring;
            int placedBus = 0, placedTaxi = 0, placedDrop = 0;
            if (bus + taxis + drop > 0 && k1 - k0 > 8f)
            {
                var band = TransportKit.FrameRect(fa, t, n, k0, k1, -3f, kerbDepth);
                var kerb = TransportKit.Intersect(region.Ring, band, 20f);
                var plaza = TransportKit.Subtract(region.Ring, band, MinArea * 0.3f);
                if (kerb != null && plaza != null && ctx.Surface(kerb, SurfaceRole.Asphalt, AssetMix.One(""), AssetCategories.SurfaceAsphalt))
                {
                    plazaRing = plaza;
                    Bays(ctx, occ, fa, t, n, k0, k1, kerbDepth, style, ref placedBus, ref placedTaxi, ref placedDrop);
                    occ.TakeCorridor(fa + t * k0 + n * (kerbDepth * 0.5f - 0.5f), fa + t * k1 + n * (kerbDepth * 0.5f - 0.5f), kerbDepth * 0.5f + 0.5f, 2);
                }
            }
            ctx.Check();

            // (the plazas makeover, 2026-10-29: the transit plaza comes back) the forecourt on the designed squares' kit
            // (PlazaDesigns.Transit): a canopy along the platform, a grove with benches and bike racks, a focal point.
            if (DesignedForecourt)
            {
                var pal = GroundPalettes.Resolve(ctx.Settings, style == "forecourt" ? GroundPalettes.Classic : GroundPalettes.Modern, "pavingAsset");
                ctx.NavigationArea(plazaRing, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
                var pr = LayerRegion.From(plazaRing) ?? region;
                var inner = new List<Gate>();
                foreach (var g in gates)
                {
                    var p = pr.ClosestOnBoundary(g.Position, out int seg, out _);
                    if (math.distance(p, g.Position) < 12f) inner.Add(new Gate { Position = p, Segment = seg, AttachId = -1, Kind = g.Kind });
                }
                ctx.Plan.Facts["frontX"] = n.x;
                ctx.Plan.Facts["frontY"] = n.y;
                ctx.Plan.Facts["transitBikes"] = style == "bikeHub" ? 1 : 0;
                ctx.Plan.Facts["transitFocal"] = style == "forecourt" || style == "bikeHub" || ctx.B("centrepiece") ? 1 : 0;
                string tl = ctx.S("treeLayout");
                ctx.Plan.Facts["transitTrees"] = tl == "none" ? 0 : tl == "rows" ? 2 : tl == "islands" ? 3 : 1;
                int before = ctx.Plan.Objects.Count;
                PlazaLayer.PlanDesignedSquare(ctx, occ, pr, inner, "transit", pal);
                // "no trees" holds for the designed square and its surround too (the lead's merge, 2026-10-30)
                if (tl == "none")
                    for (int i = ctx.Plan.Objects.Count - 1; i >= before; i--)
                        if (ctx.Plan.Objects[i].Role == ObjectRole.Tree) ctx.Plan.Objects.RemoveAt(i);
                ctx.Stat(StatBusBays, placedBus, Keys.UnitCount);
                ctx.Stat(StatTaxiSpaces, placedTaxi, Keys.UnitCount);
                ctx.Stat(StatDropOff, placedDrop, Keys.UnitCount);
                int racksD = 0; foreach (var o in ctx.Plan.Objects) if (o.Category == "transport.bikeRack") racksD++;
                ctx.Stat(StatBikeRacks, racksD, Keys.UnitCount);
                ctx.Plan.Facts["bikeRacks"] = racksD;
                ctx.Plan.Facts["busBays"] = placedBus;
                ctx.Plan.Facts["taxiSpaces"] = placedTaxi;
                ctx.Plan.Facts["dropOff"] = placedDrop;
                ctx.Plan.Facts["lot"] = lot != null ? 1 : 0;
                return;
            }

            // ── Forecourt paving ──
            // Art pass a2: the forecourt is laid in concrete blocks, framed in dark slabs, with a light
            // platform strip along the kerb, a rondel round the focal point, tree pits and a bike floor
            // (PavingKit tiling, emitted at the end once everything that cuts into it is known).
            // s18: ground palette, Automatic = modern concrete (a forecourt: classic stone).
            var palette = GroundPalettes.Resolve(ctx.Settings, ctx.S("style") == "forecourt" ? GroundPalettes.Classic : GroundPalettes.Modern, "pavingAsset");
            var pave = new PavingPlan { Axis = t };
            ctx.NavigationArea(plazaRing, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
            var plazaRegion = LayerRegion.From(plazaRing) ?? region;
            if (placedBus + placedTaxi + placedDrop > 0)
                pave.Add(TransportKit.FrameRect(fa, t, n, k0, k1, kerbDepth, kerbDepth + 3f), SurfaceRole.Plaza, palette.Accent, AssetCategories.SurfacePaving);

            // Focal point on the axis from the middle of the kerb inwards.
            float uc = (k0 + k1) * 0.5f;
            var focal = fa + t * uc + n * (kerbDepth + math.max(8f, (vMax - kerbDepth) * 0.45f));
            if (!plazaRegion.ContainsDisc(focal, 6f) || occ.Clearance(focal, 6f) < 5f) focal = plazaRegion.DeepestPoint(out _, (q, cap) => occ.Clearance(q, cap), 14);
            bool centre = (ctx.B("centrepiece") || style == "forecourt" || style == "bikeHub") && PlazaArt.Available(ctx, "", AssetCategories.Centerpiece, AssetCategories.CenterpieceStatue);
            if (centre)
            {
                // The bicycle sculpture: a station forecourt's landmark and a bike hub's mascot.
                var focalAsset = style == "forecourt" ? new AssetRef("BicycleStatue01", "LargeFountain02") : PlazaArt.Sculpture;
                if (LeisureKit.Put(ctx, occ, focal, LeisureKit.Yaw(focal, focal - n), ObjectRole.Centerpiece, focalAsset, AssetCategories.Centerpiece, 3f, Hashing.Mix(ctx.Seed, 0xCE1u), 1f, 0.3f, 0))
                    ctx.Plan.Facts["focal"] = 1;
            }
            occ.Take(focal, 5f, 2);   // the focal space stays open (the fountain feature lands here)
            var phase = math.atan2(t.y, t.x);
            pave.Add(PavingShapes.Polygon(focal, 6.5f, 40, phase), SurfaceRole.Plaza, palette.Accent, AssetCategories.SurfacePaving);
            pave.Add(PavingShapes.Annulus(focal, 6.5f, 1f), SurfaceRole.Plaza, palette.Band, AssetCategories.SurfacePaving);
            ctx.Check();

            // ── Bikes ──
            int racks = BikeRows(ctx, occ, region, fa, t, n, uc, kerbDepth, pave, palette);
            ctx.Check();

            // ── Trees ──
            var trees = new List<float2>();
            Trees(ctx, occ, plazaRegion, region, fa, t, n, trees, pave);
            ctx.Check();

            // ── Benches, signs, kiosks, lamps, bins ──
            int benches = Benches(ctx, occ, fa, t, n, k0, k1, kerbDepth, trees, focal);
            Signs(ctx, occ, gates, focal, t);
            Kiosks(ctx, occ);
            if (ctx.B("lamps")) Lamps(ctx, occ, region, fa, t, n);

            // Ground last: pits under the paved trees, the frame, the field.
            foreach (var tr in trees) pave.Add(LayerRegion.Rect(tr, t, 1.1f, 1.1f), SurfaceRole.Plaza, palette.Inlay, AssetCategories.SurfacePaving);
            pave.Add(PavingShapes.Frame(plazaRing, PlazaDressing.FrameWidth(plazaRegion)), SurfaceRole.Plaza, palette.Band, AssetCategories.SurfacePaving);
            pave.Emit(ctx, plazaRing, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);

            ctx.AddRegion(plazaRing, "paved", RegionTags.Paved);
            ctx.AddRegion(LayerRegion.Circle(focal, 5f, 20), "core", RegionTags.Core, RegionTags.Paved);
            ctx.Stat(Keys.StatArea, System.Math.Round(full.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatTrees, Count(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
            ctx.Stat(StatBusBays, placedBus, Keys.UnitCount);
            ctx.Stat(StatTaxiSpaces, placedTaxi, Keys.UnitCount);
            ctx.Stat(StatDropOff, placedDrop, Keys.UnitCount);
            ctx.Stat(StatBikeRacks, racks, Keys.UnitCount);
            ctx.Plan.Facts["busBays"] = placedBus;
            ctx.Plan.Facts["taxiSpaces"] = placedTaxi;
            ctx.Plan.Facts["dropOff"] = placedDrop;
            ctx.Plan.Facts["bikeRacks"] = racks;
            ctx.Plan.Facts["lot"] = lot != null ? 1 : 0;
        }

        private static void Frontage(LayerContext ctx, LayerRegion full, List<Gate> gates, out float2 a, out float2 b)
        {
            var roads = StreetFrontage.Roads(ctx.Input);
            var edges = StreetFrontage.Edges(full.Ring, roads);
            float best = -1f; a = b = full.Ring[0];
            foreach (var (p, q) in edges)
            {
                float len = math.distance(p, q);
                if (len > best) { best = len; a = p; b = q; }
            }
            if (best >= 12f) return;
            if (gates.Count > 0)
            {
                int s = gates[0].Segment;
                a = full.Ring[s]; b = full.Ring[(s + 1) % full.Ring.Length];
                if (math.distance(a, b) >= 12f) return;
            }
            best = -1f;
            for (int i = 0; i < full.Ring.Length; i++)
            {
                var p = full.Ring[i]; var q = full.Ring[(i + 1) % full.Ring.Length];
                float len = math.distance(p, q);
                if (len > best) { best = len; a = p; b = q; }
            }
        }

        // ───────────── kerb lane ─────────────

        private static void Bays(LayerContext ctx, Occupancy occ, float2 fa, float2 t, float2 n, float k0, float k1, float depth, string style,
            ref int placedBus, ref int placedTaxi, ref int placedDrop)
        {
            var region = ctx.LayerRegion;
            var seq = new List<(Bay kind, float len)>();
            float busLen = ctx.F("bayLength");
            void AddRun(Bay kind, int count, float len) { for (int i = 0; i < count; i++) seq.Add((kind, len)); }
            if (style == "taxiRank") { AddRun(Bay.Taxi, ctx.I("taxis"), 6f); AddRun(Bay.DropOff, ctx.I("dropOff"), 6.5f); AddRun(Bay.Bus, ctx.I("busBays"), busLen); }
            else { AddRun(Bay.Bus, ctx.I("busBays"), busLen); AddRun(Bay.Taxi, ctx.I("taxis"), 6f); AddRun(Bay.DropOff, ctx.I("dropOff"), 6.5f); }
            float total = 0f;
            foreach (var s in seq) total += s.len + (s.kind == Bay.Bus ? 3f : 0.5f);
            float u = k0 + math.max(0f, (k1 - k0 - total) * 0.5f);
            var yaw = LeisureKit.Yaw(float2.zero, t);
            var facing = LeisureKit.Yaw(float2.zero, -n);   // platform furniture faces the kerb
            string marks = AssetAvailability.Has(AssetCategories.DecalMarkings) ? AssetCategories.DecalMarkings : null;
            if (marks == null) AssetAvailability.Require(ctx.Plan, AssetCategories.DecalMarkings, null, Keys.Category(AssetCategories.DecalMarkings));
            // Designed shelters/signs are explicit: categories only label them (never an empty pool's fallback).
            string shelter = ctx.B("bayShelters") && PlazaArt.Available(ctx, "shelterAsset", AssetCategories.TransportShelter, AssetCategories.Shelter) ? AssetCategories.TransportShelter : null;
            string sign = PlazaArt.Available(ctx, "signAsset", AssetCategories.Wayfinding) ? AssetCategories.Wayfinding : null;
            var bench = PlazaArt.Or(ctx, "benchAsset", PlazaArt.ModernBenches, 0);
            uint seed = Hashing.Mix(ctx.Seed, 0xBA70u);
            var prev = (Bay)(-1);
            int index = 0;
            foreach (var (kind, len) in seq)
            {
                float gap = kind == Bay.Bus ? 3f : 0.5f;
                if (kind != prev && prev != (Bay)(-1)) u += 2f;
                if (u + len > k1) break;
                float mid = u + len * 0.5f;
                var c = fa + t * mid + n * (depth * 0.5f);
                var e0 = fa + t * u + n * (depth * 0.5f); var e1 = fa + t * (u + len) + n * (depth * 0.5f);
                if (!region.Contains(e0) || !region.Contains(e1)) { u += len + gap; continue; }
                uint v = Hashing.Mix(seed, (uint)index++);
                if (marks != null)
                {
                    ctx.Object(c, yaw, ObjectRole.Decal, AssetMix.One(""), v, -1, marks);
                    if (kind == Bay.Bus)
                    {
                        ctx.Object(e0 + t * 1f, yaw, ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(v, 1u), -1, marks);
                        ctx.Object(e1 - t * 1f, yaw, ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(v, 2u), -1, marks);
                    }
                }
                var platform = fa + t * mid + n * (depth + 2.2f);
                switch (kind)
                {
                    case Bay.Bus:
                        placedBus++;
                        if (shelter != null)
                            LeisureKit.Put(ctx, occ, platform, facing, ObjectRole.Furniture, PlazaArt.Or(ctx, "shelterAsset", BusShelter), shelter, 2.0f, Hashing.Mix(v, 3u), 0.3f, 0.2f, 0);
                        else LeisureKit.Put(ctx, occ, platform, facing, ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(v, 3u));
                        if (sign != null) LeisureKit.Put(ctx, occ, fa + t * (u + len - 1.2f) + n * (depth + 0.9f), facing, ObjectRole.Furniture, PlazaArt.Or(ctx, "signAsset", PlazaArt.Signpost), sign, 0.4f, Hashing.Mix(v, 4u), 0.2f, 0.2f);
                        break;
                    case Bay.Taxi:
                        placedTaxi++;
                        if (prev != Bay.Taxi)
                        {
                            if (sign != null) LeisureKit.Put(ctx, occ, fa + t * (u + 0.8f) + n * (depth + 0.9f), facing, ObjectRole.Furniture, PlazaArt.Or(ctx, "signAsset", PlazaArt.Signpost), sign, 0.4f, Hashing.Mix(v, 5u), 0.2f, 0.2f);
                            if (shelter != null && ctx.I("taxis") >= 4)
                                LeisureKit.Put(ctx, occ, fa + t * (u + 3f) + n * (depth + 2.2f), facing, ObjectRole.Furniture, PlazaArt.Or(ctx, "shelterAsset", TaxiShelter), shelter, 2.0f, Hashing.Mix(v, 6u), 0.3f, 0.2f, 0);
                        }
                        break;
                    default:
                        placedDrop++;
                        if (prev != Bay.DropOff) LeisureKit.Put(ctx, occ, platform, facing, ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(v, 7u));
                        break;
                }
                prev = kind;
                u += len + gap;
            }

            // Bollards along the platform edge keep cars on the lay-by.
            if (ctx.B("bollards") && PlazaArt.Available(ctx, "bollardAsset", AssetCategories.Bollard))
            {
                int i = 0;
                for (float b = k0 + 0.5f; b <= k1 - 0.5f; b += 2.5f)
                    LeisureKit.Put(ctx, occ, fa + t * b + n * (depth + 0.45f), 0f, ObjectRole.Furniture, PlazaArt.Or(ctx, "bollardAsset", PlazaArt.Bollard), AssetCategories.Bollard, 0.25f, Hashing.Mix(seed, 0x800u + (uint)i++), 0.1f, 0.15f);
            }
        }

        // ───────────── bikes ─────────────

        private static int BikeRows(LayerContext ctx, Occupancy occ, LayerRegion region, float2 fa, float2 t, float2 n, float uc, float kerbDepth,
            PavingPlan pave, PavingPalette palette)
        {
            int want = ctx.I("bikeRacks");
            if (want <= 0 || !PlazaArt.Available(ctx, "bikeRackAsset", AssetCategories.BikeRack)) return 0;
            bool sheltered = ctx.B("bikeShelters") && AssetAvailability.Has(AssetCategories.Shelter);
            bool chosen = !string.IsNullOrEmpty(ctx.S("bikeRackAsset"));
            // A row: five racks 1.6 m apart, or two bike shelters end to end (each holds its own racks).
            const int perRow = 5;
            int rows = (want + perRow - 1) / perRow;
            var centre = fa + t * uc + n * (kerbDepth + 8f);
            var packed = LeisureKit.Pack(region, occ, centre, t, 8.4f, 2.8f, 2.4f, 3f, rows, 1.2f, false, true);
            int placed = 0, row = 0;
            foreach (var r in packed)
            {
                uint v = Hashing.Mix(ctx.Seed, 0xB1C0u + (uint)row++);
                pave.Add(new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength + 0.8f, HalfWidth = r.HalfWidth + 0.8f }.Ring,
                    SurfaceRole.Plaza, palette.Inlay, AssetCategories.SurfacePaving);
                if (sheltered && !chosen)
                {
                    foreach (float side in new[] { -1f, 1f })
                    {
                        if (placed >= want) break;
                        ctx.Object(r.Local(side * 2.1f, 0f), r.Yaw, ObjectRole.Furniture, AssetMix.Pick(BikeShelters, v), Hashing.Mix(v, (uint)(side + 2)), -1, AssetCategories.Shelter);
                        placed += perRow / 2 + 1;
                    }
                    continue;
                }
                for (int k = 0; k < perRow && placed < want; k++)
                {
                    var asset = PlazaArt.Or(ctx, "bikeRackAsset", Racks, v);
                    ctx.Object(r.Local(-3.2f + k * 1.6f, 0f), r.Yaw + 1.5708f, ObjectRole.Furniture, asset, Hashing.Mix(v, (uint)k), -1, AssetCategories.BikeRack);
                    placed++;
                }
            }
            placed = System.Math.Min(placed, want);
            if (ctx.B("bikeService") && packed.Count > 0 && PlazaArt.Available(ctx, "", AssetCategories.Kiosk))
            {
                var r0 = packed[0];
                foreach (float side in new[] { 1f, -1f })
                    if (LeisureKit.Put(ctx, occ, r0.Local(side * (r0.HalfLength + 2.8f), 0f), r0.Yaw, ObjectRole.Furniture, PlazaArt.Or(ctx, "", PlazaArt.Kiosks, 1u), AssetCategories.Kiosk, 2.2f,
                        Hashing.Mix(ctx.Seed, 0xB5EFu), 0.3f, 0.3f, 0)) break;
            }
            return placed;
        }

        // ───────────── trees ─────────────

        private static void Trees(LayerContext ctx, Occupancy occ, LayerRegion plaza, LayerRegion region, float2 fa, float2 t, float2 n, List<float2> placed, PavingPlan pave)
        {
            string layout = ctx.S("treeLayout");
            if (layout == "none") return;
            bool planters = ctx.B("planters");
            var ids = ctx.L("treeSpecies");
            if (ids == null || ids.Length == 0) ids = PlazaArt.PlaneTrees;
            var role = planters ? ObjectRole.Planter : ObjectRole.Tree;
            float r = planters ? 1.2f : 2.4f;
            float edge = PlazaDressing.FrameWidth(plaza) + 1.2f;
            bool Try(float2 p, uint salt, bool pit)
            {
                if (!plaza.ContainsDisc(p, edge)) return false;
                uint v = Hashing.Mix(ctx.Seed, salt);
                if (!LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, role, planters ? PlazaArt.Or(ctx, "", PlazaArt.Pots, v) : AssetMix.Pick(ids, v), planters ? AssetCategories.Planter : null, r, v, 1.2f, 0.4f, 3, planters ? -1 : 2))
                    return false;
                if (pit && !planters) placed.Add(p);
                return true;
            }
            if (layout == "islands")
            {
                // Planted islands: a lawn disc with three trees and a bench either side looking out.
                int count = math.clamp((int)(plaza.Area / 900f), 1, 12);
                var avoid = new List<float2>();
                for (int i = 0; i < count; i++)
                {
                    if (!Spots.Find(occ, 6.5f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x15A0u + (uint)i), out var c, avoid, 18f, edge, budget: 900)) break;
                    avoid.Add(c);
                    pave.Add(PavingShapes.Polygon(c, 4.6f, 28), SurfaceRole.Grass, new AssetRef("", PavingPalettes.VGrass), AssetCategories.SurfaceGrass);
                    float ph = Hashing.Unit(Hashing.Mix(ctx.Seed, 0x15B0u + (uint)i)) * 6.283f;
                    Try(c, 0x15C0u + (uint)i, false);
                    for (int k = 0; k < 5; k++)
                    {
                        float a = ph + k * 1.2566f;
                        uint bv = Hashing.Mix(ctx.Seed, 0x15C8u + (uint)(i * 8 + k));
                        ctx.Object(c + new float2(math.cos(a), math.sin(a)) * 3.2f, a, ObjectRole.Bush, AssetMix.Pick(PlazaArt.CityBushes, bv), bv, -1, AssetCategories.Bush);
                    }
                    occ.Take(c, 4.8f, 3);
                    for (int k = 0; k < 2; k++)
                    {
                        float a = ph + 1.05f + k * 3.14f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        LeisureKit.Put(ctx, occ, c + dir * 6.2f, LeisureKit.Yaw(c, c + dir), ObjectRole.Bench, PlazaArt.Or(ctx, "benchAsset", PlazaArt.ModernBenches, 0), null, 1.0f, Hashing.Mix(ctx.Seed, 0x15D0u + (uint)(i * 2 + k)));
                    }
                }
                return;
            }
            // Trees are real size: their spacing is never shrunk by the layout scale (a 6 m grid was a forest).
            if (layout == "rows")
            {
                // Allées: two rows down each side of the forecourt, away from the street, trees 9 m apart.
                float ru0 = float.MaxValue, ru1 = float.MinValue, rv = 0f;
                foreach (var p in plaza.Ring)
                {
                    float u = math.dot(p - fa, t), v = math.dot(p - fa, n);
                    ru0 = math.min(ru0, u); ru1 = math.max(ru1, u); rv = math.max(rv, v);
                }
                int k = 0;
                foreach (float off in new[] { edge + 3f, edge + 10f })
                    foreach (float u in new[] { ru0 + off, ru1 - off })
                        for (float v = 9f; v < rv - edge - 2f; v += 9f)
                            Try(fa + t * u + n * v, 0x7600u + (uint)k++, true);
                return;
            }
            float along = math.max(10f, ctx.Raw("treeSpacing"));
            float across = layout == "rows" ? 18f : math.max(10f, ctx.Raw("treeSpacing"));
            // Lattice in the frontage frame; rows (allées) run away from the street.
            float uMin = float.MaxValue, uMax = float.MinValue, vMax = 0f;
            foreach (var p in plaza.Ring)
            {
                float u = math.dot(p - fa, t), v = math.dot(p - fa, n);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMax = math.max(vMax, v);
            }
            float uc = (uMin + uMax) * 0.5f;
            float concourse = math.max(10f, (uMax - uMin) * 0.2f);
            float belt = layout == "rows" ? 14f : 26f;
            int i0 = (int)math.floor((uMin - uc) / across) - 1, i1 = (int)math.ceil((uMax - uc) / across) + 1;
            int count2 = 0;
            for (int iu = i0; iu <= i1; iu++)
                for (float v = along * 0.5f + 8f; v < vMax; v += along)
                {
                    if (count2++ > 4000) return;
                    float u = uc + (iu + 0.5f) * across;
                    // The concourse from the kerb to the focal point stays open; the trees frame it as
                    // bosques on both sides and a belt along the back, never an orchard over the square.
                    if (math.abs(u - uc) < concourse && v < vMax - 16f) continue;
                    var p = fa + t * u + n * v;
                    if (plaza.BoundaryDistance(p, belt + 1f) > belt) continue;
                    Try(p, 0x7700u + (uint)count2, true);
                }
        }

        // ───────────── furniture ─────────────

        private static int Benches(LayerContext ctx, Occupancy occ, float2 fa, float2 t, float2 n, float k0, float k1, float depth, List<float2> trees, float2 focal)
        {
            int want = ctx.I("benches");
            var bench = PlazaArt.Or(ctx, "benchAsset", PlazaArt.ModernBenches, 0);
            int n0 = Count(ctx.Plan, ObjectRole.Bench), placed = 0;
            uint seed = Hashing.Mix(ctx.Seed, 0xBE70u);
            var bin = PlazaArt.Bin;
            // Facing the focal point, round it.
            for (int i = 0; i < 6 && placed < want; i++)
            {
                float a = i * 1.047f;
                var p = focal + new float2(math.cos(a), math.sin(a)) * 7f;
                if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, focal), ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(seed, (uint)i))) placed++;
            }
            // Beside the trees, back to the trunk.
            foreach (var tr in trees)
            {
                if (placed >= want) break;
                var p = tr + t * 2.9f;
                if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(tr, p), ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(seed, 100u + (uint)placed)))
                {
                    placed++;
                    if ((placed & 3) == 0) LeisureKit.Put(ctx, occ, tr - t * 2.6f, 0f, ObjectRole.Bin, bin, null, 0.4f, Hashing.Mix(seed, 200u + (uint)placed), 0.2f, 0.1f);
                }
            }
            // Waiting benches along the platform.
            for (float u = k0 + 2f; u < k1 - 2f && placed < want; u += 5f)
                if (LeisureKit.Put(ctx, occ, fa + t * u + n * (depth + 5.2f), LeisureKit.Yaw(float2.zero, -n), ObjectRole.Bench, bench, null, 1.0f, Hashing.Mix(seed, 300u + (uint)placed))) placed++;
            return n0 + placed;
        }

        private static void Signs(LayerContext ctx, Occupancy occ, List<Gate> gates, float2 focal, float2 t)
        {
            int want = ctx.I("signs");
            if (want <= 0) return;
            if (!PlazaArt.Available(ctx, "signAsset", AssetCategories.Wayfinding)) return;
            string cat = AssetCategories.Wayfinding;
            var asset = PlazaArt.Or(ctx, "signAsset", PlazaArt.Signpost);
            int placed = 0;
            var spots = new List<float2> { focal + t * 8f, focal - t * 8f };
            foreach (var g in gates)
            {
                var inward = math.normalizesafe(ctx.LayerRegion.Centroid - g.Position);
                spots.Insert(0, g.Position + inward * 3f);
            }
            for (int i = 0; i < spots.Count && placed < want; i++)
                if (LeisureKit.Put(ctx, occ, spots[i], LeisureKit.Yaw(spots[i], focal), ObjectRole.Furniture, asset, cat, 0.6f, Hashing.Mix(ctx.Seed, 0x5160u + (uint)i), 0.5f, 0.3f))
                    placed++;
        }

        private static void Kiosks(LayerContext ctx, Occupancy occ)
        {
            int want = ctx.I("kiosks");
            if (want <= 0) return;
            if (!PlazaArt.Available(ctx, "", AssetCategories.Kiosk)) return;
            string cat = AssetCategories.Kiosk;
            var avoid = new List<float2>();
            for (int i = 0; i < want; i++)
            {
                if (!Spots.Find(occ, 2.4f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x4100u + (uint)i), out var p, avoid, 20f, 1f, budget: 900)) break;
                avoid.Add(p);
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, ctx.LayerRegion.Centroid), ObjectRole.Furniture, AssetMix.Pick(PlazaArt.Kiosks, (uint)i), cat, 2.6f, Hashing.Mix(ctx.Seed, 0x4200u + (uint)i), 0.5f, 0.3f, 0);
            }
        }

        private static void Lamps(LayerContext ctx, Occupancy occ, LayerRegion region, float2 fa, float2 t, float2 n)
        {
            float s = math.max(12f, ctx.Raw("lampSpacing"));
            var lamp = PlazaArt.Or(ctx, "lampAsset", PlazaArt.StreetLamp);
            float uMin = float.MaxValue, uMax = float.MinValue, vMax = 0f;
            foreach (var p in region.Ring)
            {
                float u = math.dot(p - fa, t), v = math.dot(p - fa, n);
                uMin = math.min(uMin, u); uMax = math.max(uMax, u); vMax = math.max(vMax, v);
            }
            int i = 0;
            for (float v = 2f; v < vMax; v += s)
                for (float u = uMin + s * 0.5f; u < uMax; u += s)
                {
                    if (i++ > 3000) return;
                    var p = fa + t * u + n * v;
                    // Nudge off taken spots: try the point and two neighbours.
                    foreach (var q in new[] { p, p + t * 1.5f, p - t * 1.5f })
                        if (LeisureKit.Put(ctx, occ, q, 0f, ObjectRole.Lamp, lamp, null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A70u + (uint)i), 0.5f, 0.3f)) break;
                }
        }
    }
}
