// Plaza layer: walkable paved square with centerpiece(s), furniture arrangement, access points.
// Planner ported from ParkManager (tomkpunkt, GPL-3.0), Geometry/PlazaPlanner.cs and
// Geometry/PlazaVariantRoller.cs (randomisation, now respecting locked controls).
//
// The whole polygon is walkable: a pedestrian navigation area (vanilla "Pedestrian Area") plus
// "Pedestrian Access Location" markers where people enter; no hidden path ring (ParkManager).
//
// Regions: paved (whole plaza), core (disc around the centre), edge:N (4 m frontage strips),
// place:<amenity> (round 5) the rectangle reserved for an amenity feature at its chosen position.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Shore;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Placemaker.Core.Layers.Plaza
{
    [AreaGenerator(20, Group = "squares")]
    public sealed class PlazaLayer : LayerBase, IHasFeatures, ILinkedControls
    {
        public const string LayerId = "plaza";
        public const string AccessKind = "access";
        public const int MaxArrangementItems = 5;

        /// <summary>Arrangement tokens the editor offers, in display order.</summary>
        public static readonly string[] ItemKinds = { "bench", "lamp", "bin", "tree", "bush", "planter", "flowerbed", "kiosk" };

        protected override string Area => "Plaza";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Pathways.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Emits
        {
            get
            {
                var tags = new List<string> { RegionTags.Paved, RegionTags.Core, RegionTags.Edge, StreetFrontage.ShopsTag, ShoreKit.Place };
                tags.AddRange(Amenities.PlaceTags(Hosted));
                return tags.ToArray();
            }
        }

        /// <summary>Amenities a plaza offers (round 5): the hard-surface ones â€” no dog run on a square.</summary>
        public static readonly string[] Hosted = { "basketball", "tableTennis", "chess", "fitness", "splashPad", "bandstand", "noticeboard", "volleyball",
            "bigStatue", "obelisk", "triumphalArch", "sculptureWalk", "terraceGarden", "folly" };
        protected override bool HostsStreetShops => true;

        /// <summary>(s18) Picking a palette clears the per-role surface picks (like a road mix writes its roads).</summary>
        public string[] OnValueSet(SettingsBag settings, string controlId) => GroundPalettes.OnValueSet(settings, controlId, "surface");
        /// <summary>(s18) The palette reads Custom while a role is overridden.</summary>
        public void Derive(SettingsBag settings) => GroundPalettes.Derive(settings, "surface");

        /// <summary>(s18) A micro or small square stays a square: street shops would carve most of it away.</summary>
        protected override bool StreetShopsFit(LayerContext ctx)
        {
            LeisureKit.FocalPoint(ctx.LayerRegion, out float inscribed);
            if (!PlazaTiers.IsCompact(PlazaTiers.Of(ctx.LayerRegion.Area, inscribed))) return true;
            ctx.Issue(IssueSeverity.Info, "Placemaker.Feature.streetShops.Issue.TooSmall");
            return false;
        }
        public override float MinArea => 60f;

        private static readonly FeatureInfo[] FeatureList = WithAmenities(new[]
        {
            new FeatureInfo { Layer = "playground", LabelKey = "Placemaker.Plaza.Feature.playground", Preset = Park.ParkLayer.Bag(("size", "small"), ("fence", true)) },
            // Fountain before the trees: children plan in list order and the fountain wants the middle.
            new FeatureInfo
            {
                Layer = "fountain", LabelKey = "Placemaker.Plaza.Feature.fountain", TooltipKey = "Placemaker.Plaza.Feature.fountain.Tooltip",
                // The plaza lays the rondel itself (paving tiling): no apron surface on top of it.
                // s18: automatic asset (the large kit starts with LargeFountain02) so a small square gets a fountain that
                // fits, centred on its focal point; the square lights itself (no second lamp ring).
                Preset = Park.ParkLayer.Bag(("size", PlazaTiers.FountainSize), ("apron", false), ("benches", 6.0), ("lamps", false), ("planters", false)), DefaultOn = true,
            },
            // Art pass a2: the square plants its own trees (treeLayout: allÃ©e / bosque / groves) â€” the scattered
            // feature trees are opt-in.
            new FeatureInfo { Layer = "plazatrees", LabelKey = "Placemaker.Plaza.Feature.trees", TooltipKey = "Placemaker.Plaza.Feature.trees.Tooltip",
                Preset = Park.ParkLayer.Bag(("layout", "islands"), ("spacing", 16.0)) },
            new FeatureInfo { Layer = "kiosk", LabelKey = "Placemaker.Plaza.Feature.kiosk" },
            new FeatureInfo { Layer = "flowerbeds", LabelKey = "Placemaker.Plaza.Feature.flowerbeds", Preset = Park.ParkLayer.Bag(("density", 30.0), ("nearPaths", false)) },
            new FeatureInfo { Layer = "shops", LabelKey = "Placemaker.Feature.shops", TooltipKey = "Placemaker.Feature.shops.Tooltip", Preset = Park.ParkLayer.Bag(("count", 3.0), ("where", "edges")) },
            StreetShopsFeature("Plaza", "plaza"),
            new FeatureInfo { Layer = "decals", LabelKey = "Placemaker.Feature.decals", Preset = Park.ParkLayer.Bag(("theme", "cracks"), ("ground", "paved"), ("density", 0.8)) },
            new FeatureInfo { Layer = "terrain", LabelKey = "Placemaker.Feature.flatten", TooltipKey = "Placemaker.Feature.terrain.Tooltip", Preset = Park.ParkLayer.Bag(("shape", "flatten")) },
            // g2: on by default, idle unless the square is partly in (or along) water: a harbour quay.
            ShoreKit.Feature(("style", "wharf"), ("wharfShare", 100.0), ("bandWidth", 8.0), ("railings", false)),
        });

        private static FeatureInfo[] WithAmenities(FeatureInfo[] own)
        {
            var list = new List<FeatureInfo>(own);
            foreach (var id in Hosted) list.Add(Amenities.Feature("Plaza", id, false));
            return list.ToArray();
        }

        public IReadOnlyList<FeatureInfo> Features => FeatureList;

        /// <summary>
        /// Reserves the rectangle of each enabled amenity at its chosen position (before the big dressing,
        /// which then works round it) and hands it to the amenity as its place. Returns what was placed.
        /// </summary>
        private static List<(string layer, OrientedRect rect)> PlaceAmenities(LayerContext ctx, Occupancy occ, LayerRegion region, List<float2> gates)
        {
            var result = new List<(string, OrientedRect)>();
            foreach (var id in Hosted)
            {
                if (!ctx.Input.HasChildTarget(Amenities.Place(id))) continue;
                var spec = Amenities.Spec(id);
                if (!Amenities.TrySolve(region, occ, spec.Length, spec.Width, ctx.S(Amenities.AtControl(id)), gates, Hashing.Mix(ctx.Seed, Seeds.Hash(id)), null, out var rect, out bool moved, 0.5f))
                {
                    ctx.Issue(IssueSeverity.Warning, "Placemaker.Plaza.Issue.NoRoomForAmenity", "Placemaker.Feature." + id);
                    continue;
                }
                if (moved) ctx.Issue(IssueSeverity.Info, "Placemaker.Plaza.Issue.AmenityMoved", "Placemaker.Feature." + id);
                Amenities.Take(occ, rect, 0.5f, 6);
                ctx.AddRegion(rect.Ring, Amenities.Place(id), Amenities.Place(id), RegionTags.Paved);
                result.Add((id, rect));
            }
            if (result.Count > 0) ctx.Plan.Facts["amenities"] = result.Count;
            return result;
        }

        /// <summary>Clear radius the plaza keeps at its centre for a fountain feature (large fountain + seating ring).</summary>
        public const float FountainClearRadius = 12.5f;

        protected override void DefineControls(ControlSet c)
        {
            c.Asset("centerpiece", AssetCategories.Centerpiece, ControlTier.Essential, "centre");
            c.Choice("centerMode", "single", ControlTier.Essential, "centre", "single", "mirrored", "axis", "none");
            // s18: the palette chooser heads the ground section; "surface" (the field) and the role controls override it.
            GroundPalettes.DefineControls(c, "ground", "surface");
            c.Asset("surface", AssetCategories.SurfacePaving, ControlTier.Essential, "ground");
            // Pre-s18 paving style: kept (save format) but hidden; an old save's style still plans while the palette is Automatic.
            c.Choice("paveStyle", "auto", ControlTier.Essential, "ground", "auto", "civic", "warm", "garden", "modern", "cobble").Tip().When("palette=legacy");
            c.Slider("furniture", 0, 0, 24, 1, "count", ControlTier.Essential, "furniture").Tip();
            c.RandomRange("furniture", 2, 16);

            c.Slider("centerSize", 5, 2, 20, 0.5, "m", ControlTier.More, "centre").Tip();
            c.Slider("centerSpacing", 20, 5, 60, 1, "m", ControlTier.More, "centre").Layout(5);
            c.Arrangement("arrangement", "", new[] { "bench", "lamp", "bench" }, ControlTier.More, "furniture").Tip();
            c.Choice("arrangementMode", "center", ControlTier.More, "furniture", "center", "edges");
            c.Slider("arrangementGap", 4, 0, 20, 0.5, "m", ControlTier.More, "furniture").Layout(0);
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "assets");
            c.Asset("binAsset", AssetCategories.Bin, ControlTier.More, "assets");
            c.Asset("treeAsset", AssetCategories.Tree, ControlTier.More, "assets");
            c.Asset("bushAsset", AssetCategories.Bush, ControlTier.More, "assets");
            c.Asset("planterAsset", AssetCategories.Planter, ControlTier.More, "assets");
            c.Asset("flowerbedAsset", AssetCategories.Flowerbed, ControlTier.More, "assets");
            c.Asset("kioskAsset", AssetCategories.Kiosk, ControlTier.More, "assets");
            c.Toggle("walkable", true, ControlTier.More, "ground").Tip();
            c.Toggle("paving", true, ControlTier.More, "ground");
            c.Markers("access", ControlTier.More, AccessKind).Tip();
            c.Toggle("fence", false, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Fixed("centerSize");
            c.Fixed("walkable");
            c.Fixed("paving");
            c.Chance("fence", 0.35f);   // ParkManager PlazaVariantRoller.FenceChance

            // Depth (round 4): levels, amphitheatre, green, water, shade, activity, identity. See PlazaDressing.
            // 2026-10-28: the designed squares (PlazaDesigns); "classic" = the composition round the entrances below.
            c.Choice(PlazaDesigns.Control, PlazaDesigns.Classic, ControlTier.More, "identity", PlazaDesigns.Ids).Tip();
            c.Fixed(PlazaDesigns.Control);
            c.Choice("centerKind", "any", ControlTier.More, "identity", "any", "fountain", "statue").Tip();
            c.Choice("pattern", "border", ControlTier.More, "identity", "none", "grid", "border", "scatter").Tip();
            c.Choice("lighting", "standard", ControlTier.More, "identity", "standard", "feature").Tip();
            c.Toggle("bollards", false, ControlTier.More, "identity").Tip();
            c.Asset("bollardAsset", AssetCategories.Bollard, ControlTier.More, "identity").When("bollards=true");
            c.Choice("levels", "flat", ControlTier.More, "levels", "flat", "terracedUp", "terracedDown", "sunken", "raised", "split", "cascade", "podium", "sunkenCourt").Tip();
            c.Slider("levelCount", 3, 2, 4, 1, "count", ControlTier.More, "levels");
            c.Slider("levelHeight", 1.5, 0.5, 4, 0.5, "m", ControlTier.More, "levels").Tip();
            c.Toggle("levelPlanters", true, ControlTier.More, "levels");
            c.Toggle("amphitheatre", false, ControlTier.More, "amphitheatre").Tip();
            c.Slider("rows", 4, 2, 8, 1, "count", ControlTier.More, "amphitheatre").When("amphitheatre=true");
            c.Toggle("amphiRaise", true, ControlTier.More, "amphitheatre").When("amphitheatre=true").Tip();
            c.Asset("stageAsset", AssetCategories.Stage, ControlTier.More, "amphitheatre");
            c.Slider("lawns", 0, 0, 40, 5, "%", ControlTier.More, "green").Tip();
            c.Slider("beds", 4, 0, 12, 1, "count", ControlTier.More, "green").Tip();
            c.Slider("planters", 0, 0, 30, 1, "count", ControlTier.More, "green");
            c.Choice("treeLayout", "edge", ControlTier.More, "green", "none", "edge", "grid", "groves").Tip();
            c.Choice("border", "none", ControlTier.More, "green", "none", "grass", "gravel").Tip();
            c.Toggle("pool", false, ControlTier.More, "water").Tip();
            c.Toggle("realWater", false, ControlTier.More, "water").When("pool=true").Tip();
            c.Asset("poolSurface", AssetCategories.SurfaceWater, ControlTier.More, "water").When("pool=true");
            c.Toggle("jets", false, ControlTier.More, "water").Tip();
            c.Asset("jetAsset", AssetCategories.WaterJets, ControlTier.More, "water").When("jets=true");
            c.Slider("shade", 0, 0, 6, 1, "count", ControlTier.More, "shade").Tip();
            c.Asset("shelterAsset", AssetCategories.Shelter, ControlTier.More, "shade");
            c.Slider("cafe", 0, 0, 8, 1, "count", ControlTier.More, "activity").Tip();
            c.Asset("cafeAsset", AssetCategories.CafeSeating, ControlTier.More, "activity");
            c.Slider("stalls", 0, 0, 24, 1, "count", ControlTier.More, "activity").Tip();
            c.Asset("stallAsset", AssetCategories.MarketStall, ControlTier.More, "activity");
            c.Toggle("games", false, ControlTier.More, "activity").Tip();
            c.Toggle("performance", false, ControlTier.More, "activity").Tip();
            DefineStreetShopControls(c);
            Amenities.DefineHostControls(c, false, Hosted);
            foreach (var id in new[] { "paveStyle", "levels", "levelCount", "levelHeight", "amphitheatre", "rows", "pool", "realWater", "jets", "stalls", "performance", "games", "bollards", "pattern", "lighting", "centerKind", "border", "treeLayout" })
                c.Fixed(id);
            c.RandomRange("lawns", 0, 20);
            c.RandomRange("beds", 0, 4);
            c.RandomRange("planters", 0, 12);
            c.RandomRange("shade", 0, 2);
            c.RandomRange("cafe", 0, 2);
        }

        protected override void DefinePresets()
        {
            // Art pass a2: each preset is a designed square â€” its paving (s18: its ground palette + pattern), its
            // focal point, its planting and seating that faces that focal point (see PlazaDressing).
            Preset("civic", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Classic), ("centerMode", "single"), ("centerSize", 4.0), ("centerKind", "statue"), ("furniture", 8.0),
                ("arrangement", new[] { "bench" }), ("arrangementMode", "center"), ("arrangementGap", 5.0), ("treeLayout", "grid"), ("pattern", "border"),
                ("lighting", "feature"), ("bollards", true), ("planters", 0.0), ("beds", 0.0));
            Preset("fountain", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Sandstone), ("centerMode", "single"), ("centerSize", 9.0), ("centerKind", "fountain"), ("furniture", 0.0),
                ("pattern", "scatter"), ("beds", 4.0), ("treeLayout", "edge"), ("lighting", "feature"), ("planters", 8.0));
            Preset("market", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Cobble), ("centerMode", "none"), ("furniture", 0.0), ("stalls", 18.0), ("cafe", 2.0), ("treeLayout", "edge"),
                ("pattern", "none"), ("beds", 0.0), ("shade", 0.0));
            Preset("garden", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Garden), ("centerMode", "single"), ("centerSize", 4.0), ("centerKind", "any"), ("furniture", 0.0),
                ("lawns", 30.0), ("beds", 2.0), ("treeLayout", "edge"), ("border", "grass"), ("pattern", "none"));
            Preset("sunken", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Garden), ("centerMode", "single"), ("centerSize", 4.0), ("furniture", 0.0), ("levels", "sunken"), ("levelHeight", 2.0),
                ("lawns", 25.0), ("beds", 0.0), ("planters", 0.0), ("treeLayout", "edge"), ("centerKind", "fountain"), ("pattern", "none"));
            Preset("amphitheatre", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Classic), ("centerMode", "none"), ("furniture", 0.0), ("amphitheatre", true), ("rows", 6.0), ("shade", 0.0),
                ("treeLayout", "edge"), ("lighting", "feature"), ("pattern", "none"), ("beds", 2.0));
            Preset("terraced", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Modern), ("centerMode", "single"), ("centerSize", 4.0), ("furniture", 0.0), ("levels", "terracedUp"), ("levelCount", 3.0),
                ("levelHeight", 2.5), ("beds", 4.0), ("treeLayout", "edge"), ("centerKind", "statue"), ("pattern", "none"));
            Preset("cafe", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.BrickStone), ("centerMode", "single"), ("centerSize", 3.0), ("centerKind", "fountain"), ("furniture", 0.0), ("cafe", 6.0),
                ("shade", 2.0), ("planters", 0.0), ("pattern", "grid"), ("games", true), ("jets", false), ("treeLayout", "edge"), ("beds", 0.0));
            // Round 5: architectural level variants (PlazaLevels) and two classic squares.
            Preset("splitLevel", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Modern), ("centerMode", "single"), ("centerSize", 3.0), ("furniture", 0.0), ("levels", "split"), ("levelHeight", 1.5),
                ("levelPlanters", true), ("treeLayout", "edge"), ("pattern", "border"), ("beds", 2.0));
            Preset("cascade", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Modern), ("centerMode", "axis"), ("centerSize", 3.0), ("centerSpacing", 18.0), ("centerKind", "fountain"),
                ("furniture", 6.0), ("arrangement", new[] { "bench" }), ("arrangementMode", "center"), ("arrangementGap", 4.0), ("levels", "cascade"),
                ("levelCount", 4.0), ("levelHeight", 1.0), ("levelPlanters", true), ("beds", 0.0), ("treeLayout", "edge"), ("pattern", "none"));
            Preset("podium", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Modern), ("centerMode", "single"), ("centerSize", 4.0), ("centerKind", "statue"), ("furniture", 8.0),
                ("arrangement", new[] { "bench" }), ("arrangementMode", "center"), ("arrangementGap", 6.0), ("levels", "podium"),
                ("levelHeight", 1.5), ("levelPlanters", true), ("lighting", "feature"), ("bollards", true), ("pattern", "grid"), ("beds", 0.0), ("treeLayout", "edge"));
            Preset("sunkenCourt", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Modern), ("centerMode", "single"), ("centerSize", 3.0), ("centerKind", "fountain"), ("furniture", 0.0), ("levels", "sunkenCourt"),
                ("levelHeight", 2.0), ("levelPlanters", true), ("cafe", 3.0), ("shade", 0.0), ("treeLayout", "grid"), ("pattern", "none"), ("beds", 0.0));
            // 2026-10-28: the old piazza (gridded sandstone, kiosks) is now the designed old-town piazza (PlazaDesigns).
            Preset("piazza", ("palette", GroundPalettes.Cobble), (PlazaDesigns.Control, "piazza"), ("centerKind", "fountain"), ("furniture", 0.0),
                ("cafe", 4.0), ("bollards", true), ("lighting", "feature"), ("treeLayout", "none"), ("planters", 10.0), ("beds", 0.0));
            // 2026-10-28: designed squares (PlazaDesigns): each lays a real square's plan on the square's own axes.
            Preset("grand", ("palette", GroundPalettes.Classic), (PlazaDesigns.Control, "grand"), ("centerKind", "statue"), ("furniture", 0.0), ("lighting", "feature"));
            Preset("rooms", ("palette", GroundPalettes.Cobble), (PlazaDesigns.Control, "rooms"), ("centerKind", "fountain"), ("furniture", 0.0));
            Preset("railed", ("palette", GroundPalettes.Classic), (PlazaDesigns.Control, "railed"), ("centerKind", "fountain"), ("furniture", 0.0), ("lighting", "feature"));
            // 2026-10-29: grey stone (the sandstone palette is beige sand in the vanilla surfaces), steps of 1.8 m.
            Preset("terraces", ("palette", GroundPalettes.Classic), (PlazaDesigns.Control, "terraces"), ("centerKind", "fountain"), ("furniture", 0.0),
                ("levelCount", 3.0), ("levelHeight", 1.8));
            // 2026-10-29: designed squares on their own ground (PlazaDesigns.Squares).
            Preset("sunkenPlaza", ("palette", GroundPalettes.Classic), (PlazaDesigns.Control, "sunkenPlaza"), ("centerKind", "fountain"), ("furniture", 0.0), ("lighting", "feature"));
            Preset("waterSteps", ("palette", GroundPalettes.Modern), (PlazaDesigns.Control, "waterSteps"), ("centerKind", "fountain"), ("furniture", 0.0));
            Preset("marketHall", ("palette", GroundPalettes.Cobble), (PlazaDesigns.Control, "marketHall"), ("centerKind", "fountain"), ("furniture", 0.0), ("lighting", "feature"));
            Preset("dining", ("palette", GroundPalettes.Modern), (PlazaDesigns.Control, "dining"), ("centerKind", "fountain"), ("furniture", 0.0));
            Preset("grandSteps", ("palette", GroundPalettes.Classic), (PlazaDesigns.Control, "grandSteps"), ("centerKind", "fountain"), ("furniture", 0.0), ("lighting", "feature"));
            // 2026-10-29 (the plazas makeover): the grungy square, its own design and contents (PlazaDesigns.Grungy), worn by default.
            Preset("grungy", (PlazaDesigns.Control, "grungy"), ("centerKind", "statue"), ("furniture", 0.0), ("lighting", "standard"));   // worn by default: DressingProfiles (plaza, grungy) is Gritty
            Preset("modern", ("palette", GroundPalettes.Modern), (PlazaDesigns.Control, "modern"), ("centerKind", "statue"), ("furniture", 0.0), ("lighting", "standard"));
            Preset("green", (PlazaDesigns.Control, PlazaDesigns.Classic), ("palette", GroundPalettes.Garden), ("centerMode", "single"), ("centerSize", 4.0), ("centerKind", "statue"), ("furniture", 0.0), ("lawns", 40.0),
                ("beds", 0.0), ("treeLayout", "edge"), ("border", "grass"), ("pattern", "none"));

            // Playtest 4b: presets look complete. On top of the DefaultOn fountain each square turns on what
            // belongs to it; statue, garden and market squares keep their own centre (no fountain feature).
            PresetFeatures("civic", "-fountain");
            PresetFeatures("podium", "-fountain");
            PresetFeatures("terraced", "-fountain");
            PresetFeatures("green", "-fountain");
            PresetFeatures("garden", "-fountain");
            PresetFeatures("cascade", "-fountain");
            PresetFeatures("amphitheatre", "-fountain");
            PresetFeatures("market", "kiosk", "-fountain");
            PresetFeatures("cafe", "kiosk");
            PresetFeatures("piazza", "kiosk", "-fountain");
            foreach (var id in new[] { "grand", "rooms", "railed", "terraces", "modern", "sunkenPlaza", "waterSteps", "marketHall", "dining", "grandSteps", "grungy" }) PresetFeatures(id, "-fountain");
        }

        public override List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            var region = LayerRegion.From(input?.Shape?.Ring);
            if (region == null) return new List<EdgeMarker>();
            return Entrances.Suggest(input, region, AccessKind, WantedAccess(region));
        }

        public override Readiness CheckReady(AreaInput input) => new Readiness { Ready = true };

        private static int WantedAccess(LayerRegion r) => math.clamp((int)(r.Perimeter / 80f), 2, 8);

        /// <summary>
        /// ParkManager's variant roller, per control: centre mode (15 % none), spacing, arrangement
        /// mode/gap, density and a mirror-symmetric arrangement â€” but never a locked control.
        /// </summary>
        protected override bool RandomizeControl(ControlSchema control, ref Random rng, SettingsBag into)
        {
            switch (control.Id)
            {
                case "centerMode":
                    into.Values["centerMode"] = rng.NextFloat() < 0.15f ? "none" : new[] { "single", "mirrored", "axis" }[rng.NextInt(0, 3)];
                    return true;
                case "arrangement":
                {
                    int length = rng.NextInt(1, MaxArrangementItems + 1);
                    int half = (length + 1) / 2;
                    // Benches, lamps, bins, trees and bushes always exist in vanilla; the rest only when chosen.
                    var pool = new[] { "bench", "lamp", "bin", "tree", "bush" };
                    var kinds = new string[half];
                    for (int i = 0; i < half; i++) kinds[i] = pool[rng.NextInt(0, pool.Length)];
                    var result = new string[length];
                    for (int i = 0; i < length; i++) result[i] = kinds[math.min(i, length - 1 - i)];
                    into.Values["arrangement"] = result;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The issue a plaza raises on uneven ground (the user, 2026-10-30: plazas look broken on a hill; level the area first).</summary>
        public const string NotLevelKey = "Placemaker.Plaza.Issue.NotLevel";

        /// <summary>
        /// Warns once when the ground under a square rises more than 1.5 m from its lowest to its highest point: squares are laid
        /// flat and look broken on a slope, so the player is told to level the area first (the game's Level tool).
        /// </summary>
        public static void WarnIfNotLevel(LayerContext ctx, LayerRegion region)
        {
            if (region?.Ring == null || ctx.Plan.Issues.Exists(i => i.Key == NotLevelKey)) return;
            Placemaker.Core.Geometry.Poly.Bounds(region.Ring, out var lo, out var hi);
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y <= 8; y++)
                for (int x = 0; x <= 8; x++)
                {
                    var q = math.lerp(lo, hi, new float2(x / 8f, y / 8f));
                    if (!Placemaker.Core.Geometry.Poly.Contains(region.Ring, q)) continue;
                    float h = ctx.H(q); min = math.min(min, h); max = math.max(max, h);
                }
            foreach (var q in region.Ring) { float h = ctx.H(q); min = math.min(min, h); max = math.max(max, h); }
            if (max - min > 1.5f) ctx.Issue(IssueSeverity.Warning, NotLevelKey, Math.Round(max - min, 1));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            ShoreKit.CarveShore(ctx, 8f);   // g2: the wet part + a quay band go to the "Shore" feature
            var region = ctx.LayerRegion;
            WarnIfNotLevel(ctx, region);
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            // Inside a place (a park's plaza) people arrive along the parent's paths: access where they end.
            var gates = InPlace(ctx.Input) ? PathEnds(ctx.Input, region) : new List<Gate>();
            if (gates.Count == 0) gates = Entrances.Resolve(ctx.Input, region, AccessKind, WantedAccess(region));
            var gatePoints = new List<float2>();
            foreach (var g in gates) gatePoints.Add(g.Position);

            // Ground: walkable navigation area; the paving is laid at the end as a tiling (PavingKit) once
            // every element that cuts into it (lawns, beds, pools, tree pits) is known.
            if (ctx.B("walkable")) ctx.NavigationArea(region.Ring, AreaRole.PedestrianArea, new AssetRef("", "Pedestrian Area", "Walking Area"));
            var palette = PlazaDressing.Palette(ctx);
            var pave = new PavingPlan();
            var axis = PlazaDressing.SquareAxis(region);
            pave.Axis = axis;
            ctx.Check();
            bool fountainChild = ctx.Input.HasChild("fountain");
            // s18: size tier from the real area and the largest inscribed circle; the focal point (incentre of a
            // triangle) is where the fountain add-on lands and where the square centres its composition.
            var focal = LeisureKit.FocalPoint(region, out float inscribed);
            var tier = PlazaTiers.Of(region.Area, inscribed);
            if (PlazaTiers.IsCompact(tier))
            {
                PlanCompact(ctx, occ, region, gates, gatePoints, palette, pave, axis, tier, fountainChild);
                return;
            }

            if (PlazaDesigns.Handles(ctx.S(PlazaDesigns.Control)))
            {
                PlanDesign(ctx, occ, region, gates, gatePoints, palette, pave, axis, fountainChild);
                return;
            }

            // Big elements first (they claim space the furniture arrangement then works around).
            PlazaDressing.Border(ctx, region, pave);
            var amenities = PlaceAmenities(ctx, occ, region, gatePoints);
            var amenityRings = amenities.ConvertAll(a => a.rect.Ring);
            if (amenityRings.Count > 0) focal = LeisureKit.FocalPoint(region, AsReserved(amenityRings), out _);   // the ground the fountain child sees
            if (PlazaLevels.Handles(ctx.S("levels"))) PlazaLevels.Shape(ctx, occ, region, ctx.S("levels"), amenityRings);
            else PlazaDressing.Levels(ctx, occ, region, gates);
            PlazaDressing.Amphitheatre(ctx, occ, region, fountainChild || ctx.S("centerMode") != "none", math.max(FountainClearRadius, ctx.F("centerSize")) + 3f);
            PlazaDressing.Pool(ctx, occ, region, focal, fountainChild || ctx.S("centerMode") != "none" ? math.max(FountainClearRadius, ctx.F("centerSize")) : 0f, pave, palette);
            PlazaDressing.Stalls(ctx, occ, region, pave, palette);
            ctx.Check();

            // The entrance axes stay open (they are the processional ways to the centre).
            bool axes = ctx.S("centerMode") != "none" || fountainChild;
            if (axes && region.Contains(focal))
                foreach (var g in gates)
                {
                    var dir = math.normalizesafe(focal - g.Position);
                    float len = math.distance(focal, g.Position) - math.max(FountainClearRadius, ctx.F("centerSize")) - 5f
                        - (ctx.S("centerMode") == "axis" || ctx.S("centerMode") == "mirrored" ? ctx.F("centerSpacing") : 0f);
                    if (len > 2f) occ.TakeCorridor(g.Position, g.Position + dir * len, 1.8f, 2);
                }

            // Centre + arrangement.
            var mode = ParseCenterMode(ctx.S("centerMode"));
            string centerKind = ctx.S("centerKind");
            var centerAsset = PlazaDressing.CenterAsset(ctx, centerKind);
            bool centerAvailable = mode == CenterMode.None || AssetAvailability.Require(ctx.Plan, AssetCategories.Centerpiece,
                new[] { ctx.S("centerpiece") }, Keys.Category(AssetCategories.Centerpiece));
            // rf-squares: no centrepiece installed (the clean game): keep the centre and plant it (PlazaDressing.SpecimenCentre).
            bool specimen = PlazaDressing.CentreAssetsMissing(ctx);
            if (!centerAvailable && !specimen) mode = CenterMode.None;
            float centerRadius = math.max(ctx.F("centerSize"), AssetMix.FootprintRadius(ctx.Site, centerAsset, 0f));
            // A fountain feature takes the centre: keep it clear (the add-on places the fountain itself).
            bool fountainFeature = ctx.Input.HasChild("fountain");
            // s18: the rondel is laid to the fountain the add-on will really build on the focal point (its size
            // shrinks to fit), so pad and fountain agree instead of a fountain straddling the pad's edge.
            float fountainApron = 0f, fountainPad = 0f;
            if (fountainFeature)
            {
                mode = CenterMode.Single;
                fountainApron = FountainAddOn.PredictApron(ctx.Site, region.BoundaryDistance(focal), PlazaTiers.FountainSize, true, out float basin, out bool alone);
                fountainPad = fountainApron <= 0f ? 0f : alone ? basin + 1.2f : fountainApron - 0.4f;
                centerRadius = fountainApron > 0f ? fountainApron - 0.3f : math.min(FountainClearRadius, inscribed * 0.6f);
            }
            var items = ParseArrangement(ctx);
            var layout = PlazaPlanner.Generate(region, occ, gatePoints, new PlazaPlanner.Options
            {
                CenterMode = mode, CenterRadius = centerRadius, CenterSpacing = ctx.F("centerSpacing"),
                ArrangementMode = ctx.S("arrangementMode") == "edges" ? ArrangementMode.AlongEdges : ArrangementMode.AroundCenter,
                ArrangementGap = ctx.F("arrangementGap"), Groups = ctx.I("furniture"), Arrangement = items,
                PreferredCenter = focal, KeepClear = fountainApron > 0f ? fountainApron + 1.0f : 0f,
            });
            if (layout.CenterpieceDropped && !fountainFeature) ctx.Issue(IssueSeverity.Info, "Placemaker.Plaza.Issue.CenterpieceDoesNotFit");
            ctx.Check();

            // Emit.
            uint variant = Hashing.Mix(ctx.Seed, 0xCE47u);
            string centerCat = PlazaDressing.CenterCategory(centerKind) ?? AssetCategories.Centerpiece;
            float rondel = 0f;
            if (!fountainFeature)
                foreach (var c in layout.Centerpieces)
                {
                    if (specimen) { rondel = math.max(rondel, PlazaDressing.SpecimenCentre(ctx, occ, c.Position, c.Radius + 2.5f, pave, 0x5BE0u + (uint)(rondel * 10f))); continue; }
                    ctx.Object(c.Position, math.atan2(axis.x, axis.y), ObjectRole.Centerpiece, centerAsset, variant, -1, centerCat);   // one design for all: a mirrored pair matches
                    occ.Take(c.Position, c.Radius, 0);
                    rondel = math.max(rondel, c.Radius + 2.5f);
                }
            else
            {
                // The fountain's seating ring and the reserved discs of everything placed here stay clear of each other.
                occ.Take(layout.Center, fountainApron > 0f ? fountainApron + 0.9f : centerRadius, 0);
                rondel = fountainApron > 0f ? fountainPad : centerRadius - 1f;
                // rf-squares: the fountain add-on has nothing to build without installed fountains: plant the rondel.
                if (specimen && rondel > 2f) PlazaDressing.SpecimenCentre(ctx, occ, layout.Center, rondel, pave, 0x5BE0u);
            }
            // A civic centre is framed by flags on the rondel's diagonals.
            if (centerKind == "statue" && layout.Centerpieces.Count == 1 && !fountainFeature && !specimen)
                for (int k = 0; k < 4; k++)
                {
                    float a = math.atan2(axis.y, axis.x) + math.PI * 0.25f + k * math.PI * 0.5f;
                    var p = layout.Center + new float2(math.cos(a), math.sin(a)) * (rondel - 0.8f);
                    LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Furniture, PlazaArt.FlagPole, AssetCategories.MemorialFlag, 0.4f, Hashing.Mix(variant, 0xF1A0u + (uint)k), 0.3f, 0.1f);
                }
            foreach (var f in layout.Furniture)
            {
                uint v = Hashing.Mix(variant, (uint)f.ArrangementId * 31u + (uint)Array.IndexOf(ItemKinds, f.Item.Kind));
                ctx.Object(f.Position, f.Yaw, f.Item.Role, f.Item.Asset, v, f.Item.Role == ObjectRole.Tree ? 3 : -1, f.Item.Category);
                occ.Take(f.Position, f.Item.FootprintRadius, 1);
            }
            ctx.Check();

            // Planting and smaller dressing into what is left.
            var pits = new List<float2>();
            PlazaDressing.Trees(ctx, occ, region, gates, layout.Center, rondel > 0f ? rondel : layout.CoreRadius, axis, pits, pave, TreeSpecies(palette));
            PlazaDressing.Lawns(ctx, occ, region, layout.Center, axis, rondel, pave);
            PlazaDressing.Beds(ctx, occ, region, layout.Center, axis, rondel, pave);
            PlazaDressing.EdgePlanters(ctx, occ, region, gates);
            PlazaDressing.Shade(ctx, occ);
            PlazaDressing.Cafe(ctx, occ);
            PlazaDressing.Games(ctx, occ);
            PlazaDressing.PerformanceSpot(ctx, occ, region);
            PlazaDressing.Jets(ctx, occ);
            // One family of lamps: an arrangement lamp the player named lights the whole square.
            var lampDesigned = palette.Id == GroundPalettes.Modern ? PlazaArt.ModernLamp : PlazaArt.ParkLamp;
            foreach (var it in items) if (it.Kind == "lamp" && !string.IsNullOrEmpty(it.Asset.Id)) { lampDesigned = it.Asset; break; }
            PlazaDressing.Lighting(ctx, occ, region, gates, layout.Center, rondel, lampDesigned);
            PlazaDressing.Bollards(ctx, occ, region, gates);
            ctx.Check();

            // The ground: rondel, axes, frame and pattern laid round everything placed above.
            if (ctx.B("paving"))
            {
                PlazaDressing.Ground(ctx, pave, region, gates, layout.Center, rondel, palette, pits, axis, axes);
                pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);
            }
            else if (pave.Count > 0) pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving, false);
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
            }
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(region, gates, 3f))
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }

            // Regions.
            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            float coreRadius = layout.CoreRadius;
            if (coreRadius >= 4f) ctx.AddRegion(LayerRegion.Circle(layout.Center, coreRadius, 24), "core", RegionTags.Core, RegionTags.Paved);
            var strips = region.BoundaryStrips(4f, math.clamp((int)math.round(region.Perimeter / 200f), 2, 6));
            for (int i = 0; i < strips.Count; i++) ctx.AddRegion(strips[i], "edge:" + i, RegionTags.Edge, RegionTags.Paved);

            // Summary.
            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatFountains, fountainFeature ? 0 : layout.Centerpieces.Count, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, CountRole(ctx.Plan, ObjectRole.Bench), Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, CountRole(ctx.Plan, ObjectRole.Lamp), Keys.UnitCount);
            ctx.Stat(Keys.StatTrees, CountRole(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Plan.Facts["accessPoints"] = gates.Count;
        }

        /// <summary>
        /// (s18) Micro and small squares: one clear idea (PlazaCompact) â€” field, one border band, a focal piece that
        /// fits with its pad and seating, lamps on the edge rhythm. Big-square elements (lawns, beds, rings, axes,
        /// grids, amphitheatre, pool) are left out; activities the player asked for (stalls, cafe, shade) and
        /// amenities still come, each only where it fits.
        /// </summary>
        private void PlanCompact(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, List<float2> gatePoints, PavingPalette palette,
            PavingPlan pave, float2 axis, PlazaTier tier, bool fountainChild)
        {
            var amenityRings = PlaceAmenities(ctx, occ, region, gatePoints).ConvertAll(a => a.rect.Ring);
            // The focal composition first: levels, bowl and stalls work round it (the fountain must find its pad free).
            var r = PlazaCompact.Plan(ctx, occ, region, gates, pave, palette, tier, fountainChild, axis, AsReserved(amenityRings));
            if (PlazaLevels.Handles(ctx.S("levels"))) PlazaLevels.Shape(ctx, occ, region, ctx.S("levels"), amenityRings, NetRole.Pathway);
            else PlazaDressing.Levels(ctx, occ, region, gates, NetRole.Pathway);
            if (tier == PlazaTier.Small)
                PlazaDressing.Amphitheatre(ctx, occ, region, false, 0f);   // the amphitheatre square keeps its bowl where one fits
            PlazaDressing.Stalls(ctx, occ, region, pave, palette);
            ctx.Check();
            PlazaDressing.Shade(ctx, occ);
            PlazaDressing.Cafe(ctx, occ);
            ctx.Check();

            if (ctx.B("paving")) pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);
            else if (pave.Count > 0) pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving, false);
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
            }
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(region, gates, 3f))
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }

            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            if (r.CoreRadius >= 4f) ctx.AddRegion(LayerRegion.Circle(r.Focal, r.CoreRadius, 24), "core", RegionTags.Core, RegionTags.Paved);
            var strips = region.BoundaryStrips(math.min(4f, PlazaTiers.BandWidth(tier, 99f) + 2f), math.clamp((int)math.round(region.Perimeter / 200f), 2, 6));
            for (int i = 0; i < strips.Count; i++) ctx.AddRegion(strips[i], "edge:" + i, RegionTags.Edge, RegionTags.Paved);

            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatFountains, fountainChild ? 0 : CountRole(ctx.Plan, ObjectRole.Centerpiece), Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, CountRole(ctx.Plan, ObjectRole.Bench), Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, CountRole(ctx.Plan, ObjectRole.Lamp), Keys.UnitCount);
            ctx.Stat(Keys.StatTrees, CountRole(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Plan.Facts["accessPoints"] = gates.Count;
        }

        /// <summary>
        /// (2026-10-28) A designed square (PlazaDesigns): amenities first, then the design lays its own paving, planting,
        /// seating and lighting; cafe terraces and pergolas the player asked for go where they fit.
        /// </summary>
        private void PlanDesign(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, List<float2> gatePoints, PavingPalette palette,
            PavingPlan pave, float2 axis, bool fountainChild)
        {
            PlaceAmenities(ctx, occ, region, gatePoints);
            ctx.Check();
            // (the plazas makeover) the grungy square on Automatic: weathered dark concrete, not the pale modern slabs
            if (ctx.S(PlazaDesigns.Control) == "grungy" && (string.IsNullOrEmpty(ctx.S(GroundPalettes.Control)) || ctx.S(GroundPalettes.Control) == GroundPalettes.Automatic)) palette = PlazaDesigns.GrungyPalette;
            var r = PlazaDesigns.Plan(ctx, occ, region, gates, palette, pave, axis, fountainChild, ctx.S(PlazaDesigns.Control));
            PlazaDressing.Shade(ctx, occ);
            if (ctx.S(PlazaDesigns.Control) != "piazza") PlazaDressing.Cafe(ctx, occ);
            ctx.Check();

            if (ctx.B("paving")) pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);
            else if (pave.Count > 0) pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving, false);
            ctx.Plan.Facts["pavingRepairLoss"] = (float)Math.Round(pave.RepairLoss, 1);
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
            }
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(region, gates, 3f))
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }

            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            if (r.CoreRadius >= 4f) ctx.AddRegion(LayerRegion.Circle(r.Centre, r.CoreRadius, 24), "core", RegionTags.Core, RegionTags.Paved);
            var strips = region.BoundaryStrips(4f, math.clamp((int)math.round(region.Perimeter / 200f), 2, 6));
            for (int i = 0; i < strips.Count; i++) ctx.AddRegion(strips[i], "edge:" + i, RegionTags.Edge, RegionTags.Paved);

            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatFountains, CountRole(ctx.Plan, ObjectRole.Centerpiece), Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, CountRole(ctx.Plan, ObjectRole.Bench), Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, CountRole(ctx.Plan, ObjectRole.Lamp), Keys.UnitCount);
            ctx.Stat(Keys.StatTrees, CountRole(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Plan.Facts["accessPoints"] = gates.Count;
        }

        /// <summary>
        /// (the plazas makeover, 2026-10-29) A designed square for another type of the Plazas category (the market square's
        /// "market", the amphitheatre's "theatre"): the design on the square's own axes, its paving tiling, access points,
        /// regions and stats, exactly as the plaza lays its own designs. The caller has made the navigation area.
        /// </summary>
        internal static void PlanDesignedSquare(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, string design, PavingPalette palette)
        {
            if (design != "theatre") WarnIfNotLevel(ctx, region);   // a theatre shapes its own bowl
            var axis = PlazaDressing.SquareAxis(region);
            var pave = new PavingPlan { Axis = axis };
            var r = PlazaDesigns.Plan(ctx, occ, region, gates, palette, pave, axis, false, design);
            ctx.Check();
            pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving);
            ctx.Plan.Facts["pavingRepairLoss"] = (float)Math.Round(pave.RepairLoss, 1);
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
            }
            ctx.AddRegion(region.Ring, "paved", RegionTags.Paved);
            if (r.CoreRadius >= 4f) ctx.AddRegion(LayerRegion.Circle(r.Centre, r.CoreRadius, 24), "core", RegionTags.Core, RegionTags.Paved);
            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatBenches, CountRole(ctx.Plan, ObjectRole.Bench), Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, CountRole(ctx.Plan, ObjectRole.Lamp), Keys.UnitCount);
            ctx.Stat(Keys.StatTrees, CountRole(ctx.Plan, ObjectRole.Tree), Keys.UnitCount);
            ctx.Plan.Facts["accessPoints"] = gates.Count;
        }

        /// <summary>Tree species per paving character: planes on civic squares, chestnuts on warm ones, mixed in gardens.</summary>
        private static ReservedSpace AsReserved(List<float2[]> rings)
        {
            var r = new ReservedSpace();
            r.Polygons.AddRange(rings);
            return r;
        }

        internal static string[] TreeSpecies(PavingPalette p) =>
            p.Id == GroundPalettes.Garden ? PlazaArt.GardenTrees
            : p.Id == GroundPalettes.Sandstone || p.Id == GroundPalettes.Terracotta || p.Id == GroundPalettes.Cobble || p.Id == GroundPalettes.BrickStone ? PlazaArt.ChestnutTrees
            : p.Id == GroundPalettes.Modern ? PlazaArt.LimeTrees : PlazaArt.PlaneTrees;

        private static bool InPlace(AreaInput input)
        {
            foreach (var m in input.Markers) if (m.Kind == AccessKind) return false;   // the player placed access points
            if (input.RegionTags == null) return false;
            foreach (var t in input.RegionTags) if (t != null && t.StartsWith(Composer.PlacePrefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Access points where reserved paths of the parent end on this region's outline.</summary>
        public static List<Gate> PathEnds(AreaInput input, LayerRegion region)
        {
            var gates = new List<Gate>();
            if (input.Reserved == null) return gates;
            foreach (var c in input.Reserved.Corridors)
            {
                if (c.Tag != nameof(NetRole.Pathway) && c.Tag != nameof(NetRole.PathwayWide)) continue;
                foreach (var end in new[] { c.A, c.B })
                {
                    if (region.BoundaryDistance(end, 1.6f) > 1.5f) continue;
                    var p = region.ClosestOnBoundary(end, out int seg, out float t);
                    bool near = false;
                    foreach (var g in gates) if (math.distancesq(g.Position, p) < 16f) { near = true; break; }
                    if (!near) gates.Add(new Gate { Position = p, Segment = seg, T = t, AttachId = -1 });
                }
            }
            gates.Sort((a, b) => a.Segment != b.Segment ? a.Segment.CompareTo(b.Segment) : a.T.CompareTo(b.T));
            return gates;
        }

        private static int CountRole(AreaPlan plan, ObjectRole role)
        {
            int n = 0;
            foreach (var o in plan.Objects) if (o.Role == role) n++;
            return n;
        }

        public static CenterMode ParseCenterMode(string s)
        {
            switch (s)
            {
                case "mirrored": return CenterMode.Mirrored;
                case "axis": return CenterMode.Axis;
                case "none": return CenterMode.None;
                default: return CenterMode.Single;
            }
        }

        /// <summary>
        /// Arrangement tokens: "kind" or "kind:assetId" (kinds in <see cref="ItemKinds"/>), max 5, unknown
        /// tokens skipped. An item without asset uses the matching "&lt;kind&gt;Asset" control (automatic when empty).
        /// </summary>
        public static List<ArrangementItem> ParseArrangement(LayerContext ctx)
        {
            var result = new List<ArrangementItem>();
            foreach (var raw in ctx.L("arrangement"))
            {
                if (result.Count >= MaxArrangementItems) break;
                if (string.IsNullOrEmpty(raw)) continue;
                int colon = raw.IndexOf(':');
                string kind = colon < 0 ? raw : raw.Substring(0, colon);
                string asset = colon < 0 ? "" : raw.Substring(colon + 1);
                if (!Describe(kind, out var role, out var category, out float footprint)) continue;
                if (string.IsNullOrEmpty(asset)) asset = ctx.S(kind + "Asset");
                if (!AssetAvailability.Require(ctx.Plan, category, new[] { asset }, Keys.Category(category))) continue;
                var assetRef = AssetMix.One(asset);
                result.Add(new ArrangementItem
                {
                    Kind = kind, Role = role, Category = category, Asset = assetRef,
                    FootprintRadius = AssetMix.FootprintRadius(ctx.Site, assetRef, footprint),
                });
            }
            return result;
        }

        public static bool Describe(string kind, out ObjectRole role, out string category, out float footprint)
        {
            switch (kind)
            {
                case "bench": role = ObjectRole.Bench; category = AssetCategories.Bench; footprint = 1.35f; return true;
                case "lamp": role = ObjectRole.Lamp; category = AssetCategories.Lamp; footprint = 0.55f; return true;
                case "bin": role = ObjectRole.Bin; category = AssetCategories.Bin; footprint = 0.5f; return true;
                case "tree": role = ObjectRole.Tree; category = AssetCategories.Tree; footprint = 1.5f; return true;
                case "bush": role = ObjectRole.Bush; category = AssetCategories.Bush; footprint = 0.9f; return true;
                case "planter": role = ObjectRole.Planter; category = AssetCategories.Planter; footprint = 1.0f; return true;
                case "flowerbed": role = ObjectRole.Planter; category = AssetCategories.Flowerbed; footprint = 1.2f; return true;
                case "kiosk": role = ObjectRole.Furniture; category = AssetCategories.Kiosk; footprint = 2.5f; return true;
                default: role = ObjectRole.Furniture; category = ""; footprint = 1f; return false;
            }
        }
    }
}
