// Park layer: paths, lawn, planting, furniture, optional fence / wildlife, and built-in features
// (central plaza, pond, playground, picnic, restrooms, fountain, big trees, flowerbeds).
// Planners ported from ParkManager (tomkpunkt, GPL-3.0); vegetation scattering merged from
// ParkingLotTool (KruemelGames, GPL-3.0). See the individual files for details.
//
// Regions for children:
//   green:N      lawns between paths (largest first), tags {green}
//   green:0      additionally tagged {core} — the largest lawn, which holds a protected clearing
//   edge:N       boundary belt strips, tags {edge}
//   place:plaza  the plaza place (only when a child targets it), tags {place:plaza, paved}
//   place:pond   the pond basin (only when a child targets it), tags {place:pond, water}
//   place:<amenity>  (round 5) the rectangle reserved for an amenity feature (volleyball, basketball,
//                tableTennis, chess, fitness, splashPad, bandstand, dogRun, noticeboard) at the position
//                the player chose ("<id>At"), joined to the paths when "<id>Path" is on. See AddOns/Amenities.cs.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Shore;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    [AreaGenerator(30, Group = "parks")]
    public sealed class ParkLayer : LayerBase, IHasFeatures
    {
        public const string LayerId = "park";
        public const string EntranceKind = "entrance";
        public const float BeltDepth = 8f;
        public const string PlazaPlace = "place:plaza";
        public const string PondPlace = "place:pond";
        /// <summary>Hard limits of the plaza place (characteristic size, metres).</summary>
        public const float PlazaMinM = 12f, PlazaMaxM = 90f;

        protected override string Area => "Park";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Emits
        {
            get
            {
                var tags = new List<string> { RegionTags.Green, RegionTags.Core, RegionTags.Edge, PlazaPlace, PondPlace, StreetFrontage.ShopsTag, ShoreKit.Place };
                tags.AddRange(Amenities.PlaceTags());
                return tags.ToArray();
            }
        }
        protected override bool HostsStreetShops => true;
        public override float MinArea => 150f;

        private static readonly FeatureInfo[] FeatureList = WithAmenities(new[]
        {
            new FeatureInfo
            {
                Layer = "plaza", Target = PlazaPlace, LabelKey = "Placemaker.Park.Feature.plaza",
                TooltipKey = "Placemaker.Park.Feature.plaza.Tooltip",
                Options = new[] { "plazaShape", "plazaAt", "plazaSize" },
                // A park's plaza is a small, dressed square: patterned border, planters, trees round the edge.
                Preset = Bag(("centerMode", "single"), ("arrangementMode", "center"), ("furniture", 6.0), ("fence", false),
                    ("pattern", "border"), ("planters", 6.0), ("treeLayout", "edge"), ("beds", 2.0), ("centerKind", "fountain")),
            },
            new FeatureInfo { Layer = "playground", Target = RegionTags.Core, LabelKey = "Placemaker.Park.Feature.playground", Options = new[] { "playgroundPath" }, DefaultOn = true },
            new FeatureInfo
            {
                Layer = "pond", Target = PondPlace, LabelKey = "Placemaker.Park.Feature.pond",
                TooltipKey = "Placemaker.Park.Feature.pond.Tooltip", Options = new[] { "lakeSize" },
            },
            new FeatureInfo { Layer = "picnic", LabelKey = "Placemaker.Park.Feature.picnic", Preset = Bag(("groups", 2.0)), Options = new[] { "picnicPath" }, DefaultOn = true },
            new FeatureInfo { Layer = "restrooms", LabelKey = "Placemaker.Park.Feature.restrooms", Options = new[] { "restroomsPath" }, DefaultOn = true },
            new FeatureInfo { Layer = "fountain", LabelKey = "Placemaker.Park.Feature.fountain", Preset = Bag(("size", "medium")), Options = new[] { "fountainPath" } },
            new FeatureInfo { Layer = "bigtrees", LabelKey = "Placemaker.Park.Feature.bigtrees", DefaultOn = true },
            // Art pass (a1): real flowerbeds — on automatic the add-on's planter role resolved to planter boxes and pots.
            new FeatureInfo { Layer = "flowerbeds", LabelKey = "Placemaker.Park.Feature.flowerbeds", DefaultOn = true, Preset = Bag(("assets", (string[])ParkArt.Flowerbeds.Clone())) },
            new FeatureInfo { Layer = "shops", LabelKey = "Placemaker.Feature.shops", TooltipKey = "Placemaker.Feature.shops.Tooltip", Preset = Bag(("count", 2.0), ("where", "entrances")) },
            StreetShopsFeature("Park", "park"),
            new FeatureInfo { Layer = "rocks", LabelKey = "Placemaker.Feature.rocks" },
            new FeatureInfo { Layer = "ground", LabelKey = "Placemaker.Feature.ground", TooltipKey = "Placemaker.Feature.ground.Tooltip" },
            new FeatureInfo { Layer = "decals", LabelKey = "Placemaker.Feature.decals", Preset = Bag(("theme", "leaves"), ("density", 0.4)), DefaultOn = true },
            new FeatureInfo { Layer = "terrain", LabelKey = "Placemaker.Feature.terrain", TooltipKey = "Placemaker.Feature.terrain.Tooltip", Preset = Bag(("shape", "mounds"), ("height", 2.0)) },
            // g2: on by default, idle unless the area is partly in (or along) water.
            ShoreKit.Feature(("style", "auto")),
        });

        /// <summary>The park's own features followed by every amenity (each with position + "Path to it").</summary>
        private static FeatureInfo[] WithAmenities(FeatureInfo[] own)
        {
            var list = new List<FeatureInfo>(own);
            foreach (var a in Amenities.All) list.Add(Amenities.Feature("Park", a.Layer, true));
            return list.ToArray();
        }

        /// <summary>Features that need open ground of this radius, kept clear of planting by the park ("core" = in the core lawn).</summary>
        private static readonly (string layer, float radius, bool core)[] Destinations =
            { ("playground", 20f, true), ("picnic", 13f, false), ("fountain", 11f, false), ("restrooms", 6f, false) };

        public IReadOnlyList<FeatureInfo> Features => FeatureList;

        internal static SettingsBag Bag(params (string key, object value)[] values)
        {
            var b = new SettingsBag();
            foreach (var (k, v) in values) b.Values[k] = v;
            return b;
        }

        protected override void DefineControls(ControlSet c)
        {
            // Essentials first (DESIGN.md §2: style, paths, planting, furniture).
            c.Choice("style", "natural", ControlTier.Essential, "layout", ParkStyle.Ids).Tip();
            // Playtest 4b: thin paths by default; a main entrance still gets a wide walk.
            c.Choice("pathWidth", "narrow", ControlTier.Essential, "layout", "narrow", "mixed", "wide").Tip();
            c.Choice("pathStyle", "auto", ControlTier.Essential, "layout", ParkPathStyles.Styles).Tip();
            c.Slider("treeDensity", 26, 0, 150, 1, "perHa", ControlTier.Essential, "planting").Tip();
            c.Slider("furniture", 4, 0, 10, 0.5, "per100m", ControlTier.Essential, "furniture").Tip();
            c.Asset("surface", AssetCategories.SurfaceGrass, ControlTier.Essential, "ground").When(Lawn.ControlId + "=" + Lawn.Surface);
            c.RandomRange("treeDensity", 10, 60);
            c.RandomRange("furniture", 1.5, 5);

            // More options.
            c.Choice("paths", "auto", ControlTier.More, "layout", "auto", "none", "minimal", "loop", "network").Tip();
            c.Toggle("curvedPaths", true, ControlTier.More, "layout");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "layout");
            // Path hierarchy (ParkPathHierarchy.cs): bike route, small paths, dirt trails, desire lines.
            c.Toggle("bikePath", false, ControlTier.More, "paths").Tip();
            c.Slider("bikeGap", 10, 4, 24, 1, "m", ControlTier.More, "paths").Layout(4).When("bikePath=true").Tip();
            c.Asset("bikeAsset", AssetCategories.BikePath, ControlTier.More, "paths").When("bikePath=true").Tip();
            c.Slider("smallPaths", 0, 0, 8, 1, "count", ControlTier.More, "paths").Tip();
            c.Asset("smallPathAsset", AssetCategories.Pathway, ControlTier.More, "paths");
            c.Slider("trails", 0, 0, 6, 1, "count", ControlTier.More, "paths").Tip();
            c.Asset("trailAsset", AssetCategories.TrailPath, ControlTier.More, "paths").Tip();
            // s33: a dirt trail can be painted into the ground instead of being a path net (walkable when the playset has
            // an invisible pedestrian path, else visual only).
            c.Choice("trailStyle", "net", ControlTier.More, "paths", "net", "painted").Tip();
            c.Choice("desireLines", "none", ControlTier.More, "paths", "none", "worn", "paved").Tip();
            c.Slider("desireCount", 3, 1, 8, 1, "count", ControlTier.More, "paths");
            c.Slider("desireLayer", 3, 1, 4, 1, "count", ControlTier.More, "paths").When("desireLines=worn").Tip();   // s33: 3 = dirt (1 was dry grass)
            EntranceKinds.WithOptions(c.Markers("entrances", ControlTier.More, EntranceKinds.Park), EntranceKinds.Park).Tip();
            c.Slider("bushDensity", 60, 0, 250, 1, "perHa", ControlTier.More, "planting");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "planting");
            c.Toggle("belt", true, ControlTier.More, "planting").Tip();
            c.Toggle("animals", false, ControlTier.More, "planting").Tip();
            c.Toggle("lamps", true, ControlTier.More, "furniture");
            c.Toggle("bins", true, ControlTier.More, "furniture");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "furniture");
            c.Asset("lampAsset", AssetCategories.Lamp, ControlTier.More, "furniture").When("lamps=true");
            c.Asset("binAsset", AssetCategories.Bin, ControlTier.More, "furniture").When("bins=true");
            // s33: the Lawn choice (Ground paint section) decides; this older toggle only shows (and still turns the surface off) with Lawn = Grass surface.
            var legacyLawn = c.Toggle("groundSurface", true, ControlTier.More, "ground").When(Lawn.ControlId + "=" + Lawn.Surface);
            legacyLawn.HideWhenOff = true;
            c.Toggle("naturalGround", false, ControlTier.More, "ground").Tip();
            c.Slider("naturalLayer", 1, 1, 4, 1, "count", ControlTier.More, "ground").When("naturalGround=true").Tip();
            // Makeover (2026-10-29): places with their own mood (a rocky gully, a knoll of boulders, a wet hollow, a meadow
            // corner, a copse with fallen logs, standing stones): ParkMoods.cs. "moodGround" lets them shape the ground.
            c.Toggle("moods", true, ControlTier.Essential, "layout").Tip();
            c.Toggle("moodGround", true, ControlTier.More, "layout").When("moods=true").Tip();
            c.Toggle("carPark", false, ControlTier.More, "boundary").Tip();
            c.Toggle("communityGarden", false, ControlTier.More, "layout").Tip();
            c.Toggle("fence", false, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");

            // Feature options (shown under their feature toggle: see Features[].Options).
            c.Choice("plazaShape", "circle", ControlTier.More, "plaza", "circle", "square", "rectangle");
            c.Choice("plazaAt", "centre", ControlTier.More, "plaza", "centre", "corner", "edge", "entrance").Tip();
            c.Slider("plazaSize", 9, 4, 40, 1, "%", ControlTier.More, "plaza").Tip();
            c.Slider("lakeSize", 70, 20, 100, 5, "%", ControlTier.More, "pond").Tip();
            c.Toggle("playgroundPath", true, ControlTier.More, "playground").Tip();
            c.Toggle("picnicPath", true, ControlTier.More, "picnic").Tip();
            c.Slider("picnicSpots", 0, 0, 12, 1, "count", ControlTier.More, "picnic").Tip();
            c.Toggle("restroomsPath", true, ControlTier.More, "restrooms").Tip();
            c.Toggle("fountainPath", true, ControlTier.More, "fountain").Tip();
            DefineStreetShopControls(c);
            Amenities.DefineHostControls(c, true);
            foreach (var id in new[] { "playgroundPath", "picnicPath", "restroomsPath", "fountainPath", "picnicSpots" }) c.Fixed(id);

            c.Chance("fence", 0.2f);
            c.Fixed("lakeSize");
            c.Fixed("plazaShape");
            c.Fixed("plazaAt");
            c.Fixed("plazaSize");
            c.Chance("animals", 0.3f);
            c.Chance("lamps", 0.85f);
            c.Chance("bins", 0.9f);
            c.Chance("belt", 0.8f);
            c.Chance("curvedPaths", 0.8f);
            c.Fixed("groundSurface");
            c.Fixed("carPark");
            c.Fixed("communityGarden");
            c.RandomRange("bushDensity", 15, 90);
            c.Chance("bikePath", 0.15f);
            c.RandomRange("smallPaths", 0, 2);
            c.RandomRange("trails", 0, 2);
            c.Fixed("bikeGap");
            c.Fixed("desireCount");
            c.Fixed("desireLayer");
            c.Fixed("trailStyle");
        }

        protected override void DefinePresets()
        {
            Preset("natural", ("style", "natural"), ("treeDensity", 26.0), ("bushDensity", 65.0), ("furniture", 4.0), ("pathStyle", "curvy"), ("desireLines", "worn"));
            Preset("formal", ("style", "formal"), ("pathWidth", "narrow"), ("curvedPaths", false), ("treeDensity", 30.0),
                ("bushDensity", 60.0), ("furniture", 4.5), ("paths", "network"), ("pathStyle", "formal"));
            Preset("garden", ("style", "garden"), ("pathWidth", "narrow"), ("treeDensity", 22.0), ("bushDensity", 95.0),
                ("furniture", 4.0), ("pathStyle", "garden"), ("naturalGround", true));
            Preset("woodland", ("style", "woodland"), ("pathWidth", "narrow"), ("treeDensity", 80.0), ("bushDensity", 90.0),
                ("furniture", 1.5), ("animals", true), ("lamps", false), ("pathStyle", "garden"), ("naturalGround", true), ("trails", 3.0));
            Preset("meadow", ("style", "meadow"), ("pathWidth", "narrow"), ("treeDensity", 8.0), ("bushDensity", 20.0),
                ("furniture", 3.0), ("paths", "minimal"), ("pathStyle", "scenic"));
            Preset("pocket", ("style", "pocket"), ("treeDensity", 40.0), ("bushDensity", 60.0), ("furniture", 6.0),
                ("paths", "minimal"));
            // Round 5: six park idioms, each a different path figure + planting rule (not density tweaks).
            Preset("victorian", ("style", "victorian"), ("pathStyle", "figureEight"), ("pathWidth", "narrow"), ("treeDensity", 30.0),
                ("bushDensity", 75.0), ("furniture", 5.0), ("fence", true), ("lamps", true), ("bins", true), ("curvedPaths", true));
            Preset("landscape", ("style", "landscape"), ("pathStyle", "circuit"), ("pathWidth", "narrow"), ("treeDensity", 34.0),
                ("bushDensity", 45.0), ("furniture", 3.0), ("belt", true), ("lamps", false), ("curvedPaths", true), ("trails", 2.0), ("smallPaths", 1.0),
                ("lakeSize", 100.0));
            Preset("modern", ("style", "modern"), ("pathStyle", "promenade"), ("pathWidth", "narrow"), ("curvedPaths", false),
                ("treeDensity", 40.0), ("bushDensity", 45.0), ("furniture", 5.0), ("belt", true), ("lamps", true));
            Preset("wildflower", ("style", "wildflower"), ("pathStyle", "woodlandTrail"), ("pathWidth", "narrow"), ("treeDensity", 6.0),
                ("bushDensity", 45.0), ("furniture", 2.5), ("lamps", false), ("bins", false), ("naturalGround", true), ("animals", true));
            Preset("urban", ("style", "urban"), ("pathStyle", "spokeRing"), ("pathWidth", "mixed"), ("treeDensity", 45.0),
                ("bushDensity", 50.0), ("furniture", 7.0), ("lamps", true), ("bins", true), ("belt", true));
            Preset("riverside", ("style", "riverside"), ("pathStyle", "riverside"), ("pathWidth", "narrow"), ("treeDensity", 30.0),
                ("bushDensity", 60.0), ("furniture", 4.0), ("naturalGround", true), ("animals", true), ("curvedPaths", true), ("bikePath", true));
            // Grand park wave (g3): the full hierarchy — bike through-route, main walk, small paths, trails, worn shortcuts.
            Preset("greenway", ("style", "natural"), ("pathStyle", "curvy"), ("pathWidth", "narrow"), ("treeDensity", 28.0), ("bushDensity", 60.0),
                ("furniture", 3.5), ("bikePath", true), ("smallPaths", 2.0), ("trails", 1.0), ("desireLines", "worn"), ("curvedPaths", true));

            // rf-parks: picnic grounds fold into the park as a style: a gentle circuit round a big open lawn, picnic groups with
            // shelters and grills spread over the grass, shade-tree clumps round the edge, a playground and restrooms.
            Preset("picnic", ("style", "picnic"), ("pathStyle", "circuit"), ("pathWidth", "narrow"), ("treeDensity", 22.0), ("bushDensity", 20.0),
                ("furniture", 3.0), ("lamps", true), ("bins", true), ("belt", true), ("curvedPaths", true), ("picnicSpots", 7.0));

            // Playtest 4b: presets look complete. On top of the DefaultOn features (playground, picnic,
            // restrooms, big trees, flowerbeds, decals, shore) each idiom turns on what belongs to it.
            PresetFeatures("formal", "plaza");   // the park plaza has a fountain centre

            PresetFeatures("woodland", "-flowerbeds", "-playground", "-bigtrees");
            PresetFeatures("meadow", "-flowerbeds");
            PresetFeatures("pocket", "fountain", "-picnic", "-restrooms");
            PresetFeatures("victorian", "plaza");
            PresetFeatures("landscape", "-flowerbeds", "pond");   // a landscape park is a lawn falling to a lake with a temple island
            PresetFeatures("modern", "plaza");
            PresetFeatures("wildflower", "-flowerbeds", "-playground");
            // rf-parks: the urban pocket is the city's park: plaza, kiosk and shops on its street edge; no picnic, playground, big trees
            // or loose flowerbeds (they found no room on a city block and said so on every build).
            PresetFeatures("urban", "plaza", "shops", StreetShopsLayer, "-picnic", "-flowerbeds", "-playground", "-bigtrees");
            PresetFeatures("picnic", "-flowerbeds");
            // Makeover (2026-10-29), the user: "maybe some parks are more genuine ... rural, urban; fenced and unfenced". The country
            // park: native groves and long grass, a fence of its own, dirt trails, rustic benches, no lamps or loose flowerbeds.
            Preset("wild", ("style", "wild"), ("pathStyle", "curvy"), ("pathWidth", "narrow"), ("treeDensity", 22.0), ("bushDensity", 70.0),
                ("furniture", 2.0), ("lamps", false), ("bins", false), ("belt", true), ("curvedPaths", true), ("naturalGround", true), ("naturalLayer", 1.0),
                ("animals", true), ("trails", 2.0), ("trailStyle", "painted"), ("desireLines", "worn"), ("fence", true), ("benchAsset", "CampgroundBench01"), ("carPark", true), ("communityGarden", true));
            PresetFeatures("wild", "-flowerbeds", "-playground", "-restrooms", "-bigtrees");
            PresetFeatureValues("wild", "picnic", ("groups", 2.0), ("tables", 1.0));
            // The neglected park is its own version (the user: neglect "changes the entire vibe of everything"), not a grime amount:
            // thickets and old trees close in, the lawns go to seed, the paths crack and gather moss, litter, a tired playground.
            Preset("neglected", ("style", "neglected"), ("pathStyle", "curvy"), ("pathWidth", "narrow"), ("treeDensity", 34.0), ("bushDensity", 150.0),
                ("furniture", 3.0), ("lamps", true), ("bins", true), ("belt", true), ("curvedPaths", true), ("naturalGround", true), ("naturalLayer", 3.0),
                ("desireLines", "worn"));
            PresetFeatures("neglected", "-flowerbeds", "-bigtrees");
            // The urban pocket's plaza stays an open paved square (the park's own trees already ring it; with the plaza's edge trees
            // the canopy closed over it on a round site).
            PresetFeatureValues("urban", "plaza", ("treeLayout", "none"));
            PresetFeatureValues("picnic", "picnic", ("groups", 8.0), ("tables", 3.0), ("shelter", true), ("shadeTrees", true));
        }

        public override List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            var region = LayerRegion.From(input?.Shape?.Ring);
            if (region == null) return new List<EdgeMarker>();
            return Entrances.SuggestTyped(input, region, Entrances.WantedByArea(region.Area), EntranceKinds.Park);
        }

        /// <summary>Playtest 4b: the player places the entrances (suggestions are only hints) — at least one.</summary>
        public override Readiness CheckReady(AreaInput input) => Entrances.RequireTyped(input, EntranceKinds.Park);

        /// <summary>
        /// Walk gates first (pedestrian, main and bike — a bike entrance is bike + pedestrian, so the walk
        /// reaches it too and the bike route starts at the same point), then service gates, which only the
        /// service track reaches. <paramref name="walkCount"/> = number of walk gates.
        /// </summary>
        public static List<Gate> OrderGates(List<Gate> all, out int walkCount)
        {
            var walk = new List<Gate>(); var service = new List<Gate>();
            foreach (var g in all) (g.Kind == EntranceKinds.Service ? service : walk).Add(g);
            walkCount = walk.Count;
            walk.AddRange(service);
            return walk;
        }

        /// <summary>Entrance kind drives the path: from a main entrance a wide walk; from the others a thin path ("narrow", the default), the style's own width otherwise.</summary>
        private static void GatePathWidths(ParkSite s, string widthMode, bool grandMain)
        {
            foreach (var c in s.Curves)
            {
                string ka = c.GateA >= 0 && c.GateA < s.Gates.Count ? s.Gates[c.GateA].Kind : null;
                string kd = c.GateD >= 0 && c.GateD < s.Gates.Count ? s.Gates[c.GateD].Kind : null;
                if (ka == null && kd == null) continue;
                bool main = grandMain && (ka == EntranceKinds.Main || kd == EntranceKinds.Main);
                if (!main && widthMode != "narrow") continue;
                var role = main ? NetRole.PathwayWide : NetRole.Pathway;
                c.Role = role; c.HalfWidth = Composer.DefaultHalfWidth(role);
                if (role == NetRole.PathwayWide) c.Kind = PathEdgeKind.Primary;
            }
        }

        /// <summary>Grand entrance: a pair of planters and (with lamps on) a pair of lamps flanking each main gate's path.</summary>
        private static void GatewayProps(LayerContext ctx, ParkSite s)
        {
            var region = s.LayerRegion;
            float hw = Composer.DefaultHalfWidth(NetRole.PathwayWide);
            var planter = AssetMix.One("");
            int made = 0;
            for (int gi = 0; gi < s.Gates.Count; gi++)
            {
                var g = s.Gates[gi];
                if (g.Kind != EntranceKinds.Main) continue;
                region.ClosestOnBoundary(g.Position, out int seg, out float t);
                region.PointAt(region.AlongOf(seg, t), out var tangent, out var inward, out _);
                foreach (float side in new[] { -1f, 1f })
                {
                    uint v = Hashing.Mix(ctx.Seed, 0x6A7Eu + (uint)gi * 2u + (side > 0 ? 1u : 0u));
                    var pp = g.Position + inward * 1.6f + tangent * side * (hw + 1.4f);
                    float yaw = math.atan2(inward.x, inward.y);
                    if (region.ContainsDisc(pp, 0.8f) && !s.Occ.Corridors.Overlaps(pp, 1.2f) && !s.Occ.Discs.Overlaps(pp, 1f))
                    {
                        ctx.Object(pp, yaw, ObjectRole.Planter, planter, v);
                        s.Occ.Take(pp, 1f, 2);
                        made++;
                    }
                    var lp = g.Position + inward * 4.5f + tangent * side * (hw + 1.6f);
                    if (ctx.B("lamps") && region.ContainsDisc(lp, 0.5f) && !s.Occ.Corridors.Overlaps(lp, 1.2f) && !s.Occ.Discs.Overlaps(lp, ParkPlanting.LampRadius))
                    {
                        s.Items.Add(new ParkItem { Kind = ParkItemKind.Lamp, Position = lp, Yaw = yaw, Radius = ParkPlanting.LampRadius, Variant = v, Age = -1 });
                        s.Occ.Take(lp, ParkPlanting.LampRadius, 2);
                        made++;
                    }
                }
            }
            if (made > 0) ctx.Plan.Facts["gatewayProps"] = made;
        }

        /// <summary>The plaza rule described by the park's plaza options.</summary>
        public static PlaceRule PlazaRule(SettingsBag s) => new PlaceRule
        {
            Shape = PlaceRule.ParseShape(s.S("plazaShape", "circle")),
            Anchor = PlaceRule.ParseAnchor(s.S("plazaAt", "centre")),
            // Layout size: the plaza's share of the area scales with the square of the layout scale (its side with the scale).
            SizePct = (float)s.Num("plazaSize", 15) * Layout.Scale * Layout.Scale, MinM = Layout.M(PlazaMinM, 8f), MaxM = Layout.M(PlazaMaxM, 20f), Aspect = 1.6f, Margin = Layout.M(3f, 1f),
        };

        protected override void PlanCore(LayerContext ctx)
        {
            var clock = new PlanClock(ctx.Plan.Timings);   // (perf) the park's own steps, shown in brackets after "park"
            ShoreKit.CarveShore(ctx);   // g2: the wet part + a shore band go to the "Shore" feature
            // makeover: the park's own car park on its street side (the rest of the park keeps the remaining ground)
            Gate lotGate = default;
            bool hasLot = ctx.B("carPark") && ParkCarPark.Plan(ctx, out lotGate);
            var region = ctx.LayerRegion;
            var style = ParkStyle.Get(ctx.S("style"));
            if (style.Id == "neglected") ctx.Plan.Facts[Placemaker.Core.Decals.Dynamic.ParkGround.NeglectedFact] = 1;
            string pathMode = ctx.S("paths");
            int wantedGates = Entrances.WantedByArea(region.Area);
            var tiers = PathHierarchyOptions.From(ctx.Settings);
            // Typed entrances (pedestrian, bike, main, service): walk gates first (the main walk's gate
            // indices), then service gates.
            var resolved = Entrances.ResolveTyped(ctx.Input, region, wantedGates, EntranceKinds.Park);
            if (hasLot) resolved.Add(lotGate);
            var gates = OrderGates(resolved, out int walkCount);

            // 0. Places carved out for features (only when a child will fill them).
            float2[] plaza = Array.Empty<float2>();
            if (ctx.Input.HasChildTarget(PlazaPlace))
            {
                var toward = new List<float2>();
                foreach (var g in gates) toward.Add(g.Position);
                var placed = PlaceSolver.Solve(region, ctx.Input.Reserved?.Polygons, PlazaRule(ctx.Settings), toward, Hashing.Mix(ctx.Seed, 0x91A2u));
                if (placed.Ok)
                {
                    plaza = placed.Ring;
                    if (placed.Shrunk) ctx.Issue(IssueSeverity.Info, "Placemaker.Park.Issue.PlazaSmaller");
                }
                else ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.NoRoomForPlaza");
            }
            var reserved = ctx.Input.Reserved?.Clone() ?? new ReservedSpace();
            if (plaza.Length >= 3) reserved.Polygons.Add(plaza);
            var s = new ParkSite
            {
                LayerRegion = region, Occ = new Occupancy(region, reserved, ctx.Site),
                Seed = ctx.Seed, Cancel = ctx.Cancel,
            };
            if (plaza.Length >= 3)
            {
                // The plaza (outline and interior) is an obstacle for the lake planner and the belt/edge logic too.
                for (int i = 0; i < plaza.Length; i++) s.Occ.Corridors.Add(plaza[i], plaza[(i + 1) % plaza.Length], 0.5f, 101);
                foreach (var run in Poly.FillRuns(plaza, 3f)) s.Occ.Corridors.Add(run.a, run.b, 2f, 101);
            }

            // 1. Entrances and paths (paths end at the plaza's edge instead of crossing it). Path styles: ParkPathStyles.
            string pathStyle = ctx.S("pathStyle");
            if (style.Formal && pathStyle == "auto") pathStyle = "formal";
            bool pondFeature = ctx.Input.HasChildTarget(PondPlace);
            bool lakeFirst = (pathStyle == "lakeside" || pathStyle == "boardwalk") && (pondFeature || ctx.B("lake"));
            string widthMode = ctx.S("pathWidth");
            if (pathStyle == "woodlandTrail" && widthMode == "mixed") widthMode = "narrow";
            float2 avenueA = float2.zero, avenueB = float2.zero;
            if (lakeFirst) s.Lake = PlanLake(ctx, s, plaza);
            var clearings = new List<(string layer, float2 c, float r)>();
            var plazaRing = plaza;
            float edgeKeep = math.max(ctx.M(3f), ctx.EdgeMargin + 1f), lakeKeep = ctx.M(4f, 2.5f), plazaKeep = ctx.M(3f, 1.5f);
            Func<float2, bool> okPoint = p => region.ContainsDisc(p, edgeKeep) && !s.InLake(p, lakeKeep)
                && !(plazaRing.Length >= 3 && (Poly.Contains(plazaRing, p) || ParkSite.MinDistToRing(plazaRing, p) < plazaKeep));
            if (pathMode != "none")
            {
                s.Gates = gates;
                var gatePoints = new List<float2>();
                for (int gi = 0; gi < walkCount; gi++) gatePoints.Add(s.Gates[gi].Position);
                uint pathSeed = Hashing.Mix(ctx.Seed, 0xA11u);
                // Formal parks are axial by definition; every other style follows the toggle.
                bool curved = ctx.B("curvedPaths") && !style.Formal;
                ParkPathGraph graph = null;
                switch (pathStyle)
                {
                    case "formal": graph = ParkPathStyles.Formal(region, gatePoints, okPoint); curved = false; break;
                    case "lakeside": graph = ParkPathStyles.Lakeside(region, s.Lake, gatePoints); break;
                    case "scenic":
                    {
                        clearings = FindClearings(ctx, s, new List<LayerRegion> { region }, gatePoints);
                        var stops = new List<(float2 c, float r)>();
                        foreach (var cl in clearings) stops.Add((cl.c, cl.r));
                        if (plaza.Length >= 3) stops.Add((Poly.Centroid(plaza), ParkSite.MinDistToRing(plaza, Poly.Centroid(plaza)) + 1f));
                        if (stops.Count == 0) { var deep = region.DeepestPoint(out float ins, null, 12); if (ins > 12f) stops.Add((deep, 4f)); }
                        graph = ParkPathStyles.Scenic(region, gatePoints, stops, pathSeed, okPoint);
                        curved = true;
                        break;
                    }
                    // Round 5 figures (ParkPathShapes.cs).
                    case "riverside": graph = ParkPathStyles.Riverside(region, gatePoints, pathSeed, okPoint); curved = true; break;
                    case "figureEight": graph = ParkPathStyles.FigureEight(region, gatePoints, okPoint); curved = true; break;
                    case "spokeRing": graph = ParkPathStyles.SpokeRing(region, gatePoints, plaza, okPoint); curved = true; break;
                    case "woodlandTrail": graph = ParkPathStyles.WoodlandTrail(region, gatePoints, pathSeed, okPoint); curved = true; break;
                    case "boardwalk": graph = ParkPathStyles.Boardwalk(region, gatePoints, s.Lake, pathSeed, okPoint); curved = false; break;
                    case "promenade": graph = ParkPathStyles.Promenade(region, gatePoints, okPoint, out avenueA, out avenueB); curved = false; break;
                    case "circuit": graph = ParkPathStyles.Circuit(region, gatePoints, okPoint); curved = true; break;
                }
                if (graph == null || graph.IsEmpty)
                {
                    var options = new ParkPathPlanner.Options { Loops = LoopsFor(pathMode, region.Area), StretchLoop = pathMode == "network" || pathMode == "auto" && Layout.DesignArea(region.Area) > 12000f };
                    graph = ParkPathPlanner.PlanBest(region, gatePoints, options, pathSeed, ctx.Cancel);
                    if (pathStyle != "auto" && pathStyle != "garden" && pathStyle != "curvy" && pathStyle != "formal")
                        ctx.Issue(IssueSeverity.Info, "Placemaker.Park.Issue.PathStyleFallback");
                    avenueA = avenueB = float2.zero;
                }
                if (style.Grid) curved = false;
                if (pathStyle == "garden") { graph = ParkPathStyles.Meander(graph, gatePoints, ctx.M(6.5f), ctx.M(18f), pathSeed, okPoint); curved = true; }
                else if (pathStyle == "curvy") { graph = ParkPathStyles.Meander(graph, gatePoints, ctx.M(5f), ctx.M(34f), pathSeed, okPoint); curved = true; }
                s.Graph = graph;
                ctx.Plan.Facts["graphLength"] = graph?.TotalLength ?? 0;
                s.Curves = ParkPaths.Build(s.Graph, region, curved, widthMode);
                if (math.any(avenueA != avenueB)) WidenAvenue(s.Curves, avenueA, avenueB, widthMode);
                GatePathWidths(s, widthMode, pathStyle != "woodlandTrail");   // a trail park has no grand walk
                if (plaza.Length >= 3)
                {
                    s.Curves = ParkPlaces.ClipAndConnect(s.Curves, plaza, region, out int joins);
                    ctx.Plan.Facts["plazaJoins"] = joins;
                }
                ParkPaths.Index(s.Curves, s.Occ.Corridors, 100);
                // Path hierarchy on top of the main walk (bike route, small paths, dirt trails).
                if (tiers.AnyNets) ParkPathHierarchy.AddTiers(ctx, s, tiers, okPoint);
                // Bike gates no route serves and service gates get a short link to the network.
                ParkPathHierarchy.ConnectLooseGates(ctx, s);
            }
            ctx.Check();
            clock.Lap("paths");

            // 2. Pond (permanent terraform): a "pond" feature fills the place; the old "lake" setting emits it here.
            if (pondFeature || ctx.B("lake"))
            {
                if (!lakeFirst) s.Lake = PlanLake(ctx, s, plaza);
                if (s.Lake.Length < 3) ctx.Issue(IssueSeverity.Info, "Placemaker.Park.Issue.NoRoomForLake");
                else if (pondFeature) ctx.Plan.Facts["pondArea"] = Poly.Area(s.Lake);
                else EmitLake(ctx, s.Lake);
            }
            ctx.Check();

            // 3. Lawns between paths (regions) and the protected clearing on the largest one.
            clock.Lap("pond");
            var lawns = Lawns(ctx, s, plaza);
            clock.Lap("lawns");
            // 3c. Desire lines (worn or paved shortcuts) once the lawns are known; amenities and clearings then avoid them.
            if (pathMode != "none") ParkPathHierarchy.DesireLines(ctx, s, tiers, okPoint);
            // 3b. Open ground for the features (playground, picnic, fountain, restrooms): found now and kept
            // clear of furniture and planting, so the feature layers always find their spot (playtest 2: "No
            // free space left" in an obviously open park, and "the playground does nothing").
            // 3a. Amenities (round 5) first: the player chose where they go, so they get first pick of the ground.
            var amenities = PlaceAmenities(ctx, s, plaza, gates);
            int coreIndex = 0;
            if (pathStyle != "scenic" || pathMode == "none")
            {
                var search = new List<LayerRegion>();
                foreach (var l in lawns) { var lr = LayerRegion.From(l); if (lr != null) search.Add(lr); if (search.Count >= 8) break; }
                if (search.Count == 0) search.Add(region);
                clearings = FindClearings(ctx, s, search, s.Gates.ConvertAll(g => g.Position));
            }
            // The core tag goes to the lawn that holds the playground clearing (a smaller lawn when the largest has no room).
            {
                foreach (var cl in clearings)
                    if (cl.layer == "playground")
                        for (int i = 0; i < lawns.Count; i++)
                            if (Poly.Contains(lawns[i], cl.c)) { coreIndex = i; break; }
            }
            if (s.Curves.Count > 0)
            {
                var destinations = new List<Destination>();
                foreach (var (layer, rect) in amenities) destinations.Add(new Destination { Layer = layer, Centre = rect.Center, Radius = math.min(rect.HalfLength, rect.HalfWidth), Ring = rect.Ring });
                foreach (var (layer, c, r) in clearings) destinations.Add(new Destination { Layer = layer, Centre = c, Radius = r });
                AddSpurs(ctx, s, destinations, plaza);
            }
            if (lawns.Count > 0 && style.ClearingFactor > 0f)
            {
                var largest = LayerRegion.From(lawns[0]);
                if (largest != null)
                {
                    var centre = largest.DeepestPoint(out float inscribed, null, 16);
                    float radius = math.min(math.min(inscribed - 2f, math.sqrt(largest.Area) * 0.3f), ctx.M(30f)) * style.ClearingFactor;
                    if (radius >= ctx.M(6f, 4f)) { s.ClearingCenter = centre; s.ClearingRadius = radius; }
                }
            }
            ctx.Check();
            clock.Lap("clearings");

            // 3d. Places with a mood of their own (ParkMoods): they take their ground before the furniture and planting.
            // the community garden folds in as a lawn room of allotments (ParkMoods.Allotments), with or without the other places
            bool allotments = ctx.B("communityGarden");
            var moods = ctx.B("moods") || allotments ? ParkMoods.Plan(ctx, s, lawns, ctx.B("moods") ? style.Id : "none", ctx.B("moodGround"), 1f, allotments) : new List<MoodPlace>();
            ctx.Check();
            clock.Lap("moods");

            // 4. Furniture first so planting keeps clear of it (ParkManager ordering).
            GatewayProps(ctx, s);
            // (makeover) the groves are known before the benches, so a bench can sit in their shade (they only read the outline)
            bool groves = ctx.F("treeDensity") > 0f || ctx.F("bushDensity") > 0f;
            if (groves) ParkPlanting.BuildGroves(s, style);
            float per100 = ctx.F("furniture");
            if (per100 > 0f && s.Curves.Count > 0)
            {
                // Layout size: furniture per 100 m of path at design size → closer along the (real-size) paths.
                float benchInterval = 100f / per100 * ctx.Scale;
                bool regular = style.Formal || style.Grid;   // a formal park keeps its rhythm; the others seat people where they would sit
                if (regular) ParkPlanting.PathFurniture(s, ParkItemKind.Bench, benchInterval, ParkPlanting.BenchRadius, 0x7f4a7c15u);
                else
                {
                    var views = new List<float2>();
                    foreach (var m in moods) views.Add(m.C);
                    if (s.Lake.Length >= 3) views.Add(Poly.Centroid(s.Lake));
                    foreach (var cl in clearings) views.Add(cl.c);
                    ParkPlanting.SeatBenches(s, benchInterval, views, ParkPlanting.BenchRadius);
                }
                // lamps light the main walks (the trails and side paths stay dark)
                if (ctx.B("lamps"))
                {
                    float widest = 0f;
                    foreach (var c in s.Curves) if (c.Tier != PathTier.Trail && c.Tier != PathTier.Desire) widest = math.max(widest, c.HalfWidth);
                    ParkPlanting.PathFurniture(s, ParkItemKind.Lamp, benchInterval * 0.68f, ParkPlanting.LampRadius, 0x94d049bbu,
                        regular ? (Func<PathCurve, bool>)null : c => c.Tier != PathTier.Trail && c.Tier != PathTier.Desire && c.HalfWidth >= widest * 0.8f);
                }
                if (ctx.B("bins")) ParkPlanting.Bins(s, per100 / 3f);   // Bins: one per scaled 90 m of path
            }
            ctx.Check();

            // 5. Vegetation. Layout size: areal counts scale by 1/scale (between keeping the density and keeping the
            // spacing, since the canopies are real size); tree-to-tree clearances stay footprint-based (PlantFits).
            float carved = (s.Lake.Length >= 3 ? Poly.Area(s.Lake) : 0f) + (plaza.Length >= 3 ? Poly.Area(plaza) : 0f) + ParkMoods.Area(moods);   // the places plant their own
            float plantedHa = math.max(0f, region.Area - carved) / 10000f;
            int trees = math.min(4000, (int)math.round(plantedHa * ctx.F("treeDensity") / ctx.Scale));
            int bushes = math.min(6000, (int)math.round(plantedHa * ctx.F("bushDensity") / ctx.Scale));
            bool belt = ctx.B("belt");
            var treeIds = ctx.L("treeSpecies"); var bushIds = ctx.L("bushSpecies");
            bool canTrees = trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree));
            bool canBushes = bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, bushIds, Keys.Category(AssetCategories.Bush));
            clock.Lap("furniture");
            if ((canTrees || canBushes) && !groves) ParkPlanting.BuildGroves(s, style);
            if (canTrees) ParkPlanting.Vegetation(s, style, trees, true, belt);
            ctx.Check();
            clock.Lap("trees");
            if (canBushes) ParkPlanting.Vegetation(s, style, bushes, false, belt);
            if (s.BushDrifts > 0) { ctx.Plan.Facts["bushDrifts"] = s.BushDrifts; ctx.Plan.Facts["bushesWanted"] = s.BushesWanted; ctx.Plan.Facts["bushesPlanted"] = s.BushesPlanted; }
            if (ctx.B("animals")) ParkPlanting.AnimalSpawners(s);
            ctx.Check();
            clock.Lap("bushes");

            // 6. Emit everything.
            if (ctx.B("groundSurface")) Lawn.Cover(ctx, region.Ring, AssetMix.One(ctx.S("surface")));
            if (ctx.B("naturalGround")) NaturalGround(ctx, s, lawns, style, amenities);
            float pathLength;
            // (makeover) a neglected park's side paths are half lost under the grass: worn earth, no paving (the main walk stays)
            Predicate<PathCurve> lost = style.Id == "neglected" ? c => c.Tier == PathTier.Trail || c.Tier == PathTier.Small : c => c.Tier == PathTier.Trail;
            if ((ctx.S("trailStyle") == "painted" || style.Id == "neglected") && s.Curves.Exists(lost))
            {
                pathLength = ParkPaths.Emit(ctx, s.Curves.FindAll(c => !lost(c)), s.Gates, TierAssets(ctx));
                PaintedTrails(ctx, s.Curves.FindAll(lost), s.Gates, style.Id == "neglected" ? 0.55f : 0.85f);
            }
            else pathLength = ParkPaths.Emit(ctx, s.Curves, s.Gates, TierAssets(ctx));
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(region, gates, 3.75f))
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }
            // Art pass (a1): on automatic, each idiom plants its own vanilla species (the shared tree pool mixes in
            // apple trees and conifers, which read as an orchard or a forest, not a park).
            if (treeIds.Length == 0) treeIds = StyleTrees(style.Id);
            if (bushIds.Length == 0) bushIds = StyleBushes(style.Id);
            EmitItems(ctx, s, treeIds, bushIds);
            ParkMoods.Emit(ctx, s, moods, style.Id == "neglected");
            if (style.Id == "neglected") { Dumped(ctx, s); Neglect(ctx, s, clearings, amenities); }
            foreach (var g in gates)
                if (g.Suggested) ctx.Plan.Facts["suggestedEntrances"] = (ctx.Plan.Facts.TryGetValue("suggestedEntrances", out var n) ? n : 0) + 1;
            clock.Lap("emit");

            // 7. Regions for children.
            if (plaza.Length >= 3) ctx.AddRegion(plaza, PlazaPlace, PlazaPlace, RegionTags.Paved);
            if (pondFeature && s.Lake.Length >= 3) ctx.AddRegion(s.Lake, PondPlace, PondPlace, "water");
            foreach (var (layer, rect) in amenities) ctx.AddRegion(rect.Ring, Amenities.Place(layer), Amenities.Place(layer));
            for (int i = 0; i < lawns.Count; i++)
            {
                // a lawn that became a place is the place's: the features (picnic, restrooms …) stay on the park's own lawns
                if (i != coreIndex && moods.Exists(m => m.Lawn != null && Poly.Contains(m.Lawn, Poly.Centroid(lawns[i])) && Poly.Area(m.Lawn) > 0.5f * Poly.Area(lawns[i]))) continue;
                if (i == coreIndex) ctx.AddRegion(lawns[i], "green:" + i, RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(lawns[i], "green:" + i, RegionTags.Green);
            }
            var strips = region.BoundaryStrips(ctx.M(BeltDepth, 4f), math.clamp((int)math.round(region.Perimeter / 250f), 2, 6));
            for (int i = 0; i < strips.Count; i++) ctx.AddRegion(strips[i], "edge:" + i, RegionTags.Edge);
            clock.Lap("regions");

            // 8. Summary.
            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatPathLength, Math.Round(pathLength), Keys.UnitM);
            ctx.Stat(Keys.StatTrees, s.Count(ParkItemKind.Tree), Keys.UnitCount);
            ctx.Stat(Keys.StatBushes, s.Count(ParkItemKind.Bush), Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, s.Count(ParkItemKind.Bench), Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, s.Count(ParkItemKind.Lamp), Keys.UnitCount);
            ctx.Stat(Keys.StatBins, s.Count(ParkItemKind.Bin), Keys.UnitCount);
            ctx.Plan.Facts["parkArea"] = region.Area;
            ctx.Plan.Facts["entrances"] = gates.Count;
            foreach (var k in EntranceKinds.Park)
            {
                int n = 0; foreach (var g in gates) if (g.Kind == k) n++;
                if (n > 0) ctx.Plan.Facts[k + "Entrances"] = n;
            }
            if (plaza.Length >= 3) ctx.Plan.Facts["plazaArea"] = Poly.Area(plaza);
        }

        private static float2[] PlanLake(LayerContext ctx, ParkSite s, float2[] plaza)
        {
            var lake = ParkLakePlanner.Generate(s.LayerRegion, s.Occ.Corridors, s.Gates, ctx.F("lakeSize") / 100f, Hashing.Mix(ctx.Seed, 0x1A4Eu));
            if (lake.Length >= 3 && plaza.Length >= 3 && (Poly.Contains(plaza, Poly.Centroid(lake)) || Poly.Contains(lake, Poly.Centroid(plaza))))
                lake = Array.Empty<float2>();
            return lake;
        }

        /// <summary>Open ground for each enabled destination feature, searched in the given regions (largest first); taken in the park's occupancy.</summary>
        private static List<(string layer, float2 c, float r)> FindClearings(LayerContext ctx, ParkSite s, List<LayerRegion> search, List<float2> gates)
        {
            var result = new List<(string layer, float2 c, float r)>();
            var taken = new List<float2>();
            foreach (var (layer, radius, core) in Destinations)
            {
                if (!ctx.Input.HasChild(layer)) continue;
                bool found = false;
                for (int attempt = 0; attempt < 2 && !found; attempt++)
                {
                    // Real equipment does not shrink with the layout size: a playground needs ~14 m even at small scale.
                    float r = ctx.M(attempt == 0 ? radius : radius * 0.7f, layer == "playground" ? 14f : math.min(5f, radius));
                    for (int ri = 0; ri < search.Count && !found; ri++)
                    {
                        if (core && ri > 0 && attempt == 0) break;   // the core lawn first; a smaller one beats "no space" (4b defaults)
                        var occ = new Occupancy(search[ri], ctx.Site, s.Occ.Discs, s.Occ.Corridors);   // the park's paths and discs, seen from this lawn
                        var avoid = new List<float2>(gates);
                        avoid.AddRange(taken);
                        var pref = s.Curves.Count > 0 ? Spots.Preference.NearPaths : Spots.Preference.Open;
                        if (!Spots.Find(occ, r, pref, Hashing.Mix(ctx.Seed, Seeds.Hash(layer)), out var c, avoid, r + 6f, 1f)) continue;
                        if (s.InLake(c, r + 2f)) continue;
                        s.Occ.Take(c, r, 200);
                        taken.Add(c);
                        result.Add((layer, c, r));
                        found = true;
                    }
                }
            }
            // rf-parks: a picnic lawn keeps more open ground for its picnic groups (planting stays off it; no path spur to each).
            int extra = ctx.Input.HasChild("picnic") ? ctx.I("picnicSpots") : 0;
            int spots = 0;
            for (int k = 0; k < extra * 3 && spots < extra; k++)
            {
                float r = ctx.M(13f, 8f);   // a picnic group with its shelter, grill and shade trees needs about 12 m
                bool placed = false;
                for (int ri = 0; ri < search.Count && !placed; ri++)
                {
                    var occ = new Occupancy(search[ri], ctx.Site, s.Occ.Discs, s.Occ.Corridors);
                    var avoid = new List<float2>(gates);
                    avoid.AddRange(taken);
                    if (!Spots.Find(occ, r, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x91C0u + (uint)k), out var c, avoid, r * 2f + 3f, 1f)) continue;
                    if (s.InLake(c, r + 2f)) continue;
                    s.Occ.Take(c, r, 200);
                    taken.Add(c);
                    placed = true; spots++;
                }
                if (!placed) break;
            }
            if (extra > 0) ctx.Plan.Facts["picnicSpots"] = spots;
            ctx.Plan.Facts["featureClearings"] = result.Count;
            return result;
        }

        /// <summary>"Path to it": a short path from the nearest path to each destination that is not already beside one.</summary>
        /// <summary>A spot a "Path to it" spur leads to: a clearing (disc) or an amenity (rectangle ring).</summary>
        private struct Destination
        {
            public string Layer;
            public float2 Centre;
            public float Radius;
            public float2[] Ring;

            public bool Contains(float2 p) => Ring != null ? Poly.Contains(Ring, p) : math.distance(p, Centre) < Radius;
            public float Distance(float2 p) => Ring != null ? (Poly.Contains(Ring, p) ? 0f : ParkSite.MinDistToRing(Ring, p)) : math.max(0f, math.distance(p, Centre) - Radius);

            /// <summary>Where a straight spur from <paramref name="from"/> towards the centre meets the edge, nudged outside.</summary>
            public float2 Rim(float2 from)
            {
                var dir = math.normalizesafe(from - Centre, new float2(1, 0));
                if (Ring == null) return Centre + dir * (Radius + 0.5f);
                for (int i = 0; i < Ring.Length; i++)
                    if (Poly.SegmentIntersection(from, Centre, Ring[i], Ring[(i + 1) % Ring.Length], out var x, out _, out _))
                        return x + dir * 0.3f;
                return Centre + dir * (Radius + 0.5f);
            }
        }

        /// <summary>
        /// Reserves each enabled amenity's rectangle at the position the player chose (Amenities.TrySolve),
        /// clear of paths, plaza, lake and earlier amenities; it is then kept free of planting and furniture.
        /// </summary>
        private static List<(string layer, OrientedRect rect)> PlaceAmenities(LayerContext ctx, ParkSite s, float2[] plaza, List<Gate> gates)
        {
            var result = new List<(string, OrientedRect)>();
            var gatePoints = new List<float2>();
            foreach (var g in gates) gatePoints.Add(g.Position);
            Func<float2, bool> blocked = p => s.InLake(p, 1.5f) || plaza.Length >= 3 && Poly.Contains(plaza, p);
            foreach (var spec in Amenities.All)
            {
                if (!ctx.Input.HasChildTarget(Amenities.Place(spec.Layer))) continue;
                ctx.Check();
                string at = ctx.S(Amenities.AtControl(spec.Layer));
                if (!Amenities.TrySolve(s.LayerRegion, s.Occ, spec.Length, spec.Width, at, gatePoints, Hashing.Mix(ctx.Seed, Seeds.Hash(spec.Layer)), blocked, out var rect, out bool moved, 2.5f))   // room for its "path to it" beside thin paths
                {
                    ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.NoRoomForAmenity", "Placemaker.Feature." + spec.Layer);
                    continue;
                }
                if (moved) ctx.Issue(IssueSeverity.Info, "Placemaker.Park.Issue.AmenityMoved", "Placemaker.Feature." + spec.Layer);
                Amenities.Take(s.Occ, rect, 1f, 102);
                result.Add((spec.Layer, rect));
            }
            if (result.Count > 0) ctx.Plan.Facts["amenities"] = result.Count;
            return result;
        }

        /// <summary>Promenade: the curves on the avenue line become wide; with "mixed" widths the side walks stay narrow.</summary>
        private static void WidenAvenue(List<PathCurve> curves, float2 a, float2 b, string widthMode)
        {
            foreach (var c in curves)
            {
                bool onAxis = SegmentIndex.DistSq(c.A, a, b) < 1f && SegmentIndex.DistSq(c.D, a, b) < 1f;
                var role = onAxis ? NetRole.PathwayWide : widthMode == "wide" ? NetRole.PathwayWide : NetRole.Pathway;
                if (onAxis || widthMode == "mixed") { c.Role = role; c.HalfWidth = Composer.DefaultHalfWidth(role); }
            }
        }

        private static void AddSpurs(LayerContext ctx, ParkSite s, List<Destination> clearings, float2[] plaza)
        {
            foreach (var dest in clearings)
            {
                var c = dest.Centre;
                if (!ctx.Settings.B(dest.Layer + "Path", true)) continue;
                int bestCurve = -1; float bestT = 0f, bestD = float.MaxValue; float2 q = c;
                for (int i = 0; i < s.Curves.Count; i++)
                    for (int k = 0; k <= 32; k++)
                    {
                        var p = s.Curves[i].Point(k / 32f);
                        float d = dest.Distance(p);
                        if (d < bestD) { bestD = d; bestCurve = i; bestT = k / 32f; q = p; }
                    }
                if (bestCurve < 0 || bestD <= 1f) continue;   // a short connector still counts: the feature's own walk starts at its end
                var end = dest.Rim(q);
                bool ok = true;
                for (int k = 1; k < 10 && ok; k++)
                {
                    var p = math.lerp(q, end, k / 10f);
                    if (!s.LayerRegion.ContainsDisc(p, 1.5f) || s.InLake(p, 2f) || plaza.Length >= 3 && Poly.Contains(plaza, p)) ok = false;
                    foreach (var other in clearings)
                        if (!other.Centre.Equals(c) && other.Contains(p)) ok = false;
                }
                if (!ok) continue;
                var host = s.Curves[bestCurve];
                float2 start;
                if (bestT > 0.05f && bestT < 0.95f)
                {
                    s.Curves[bestCurve] = ParkPlaces.Sub(host, 0f, bestT);
                    s.Curves.Add(ParkPlaces.Sub(host, bestT, 1f));
                    start = s.Curves[bestCurve].D;
                }
                else start = bestT <= 0.05f ? host.A : host.D;
                var spur = new PathCurve
                {
                    A = start, B = math.lerp(start, end, 1f / 3f), C = math.lerp(start, end, 2f / 3f), D = end,
                    Role = NetRole.Pathway, HalfWidth = Composer.DefaultHalfWidth(NetRole.Pathway), Kind = PathEdgeKind.Secondary,
                };
                ParkPaths.Sample(spur);
                s.Curves.Add(spur);
                for (int i = 1; i < spur.Samples.Length; i++) s.Occ.Corridors.Add(spur.Samples[i - 1], spur.Samples[i], spur.HalfWidth, 100);
                ctx.Plan.Facts["featureSpurs"] = (ctx.Plan.Facts.TryGetValue("featureSpurs", out var n) ? n : 0) + 1;
            }
        }

        /// <summary>"Natural ground": a few irregular patches of painted terrain (map texture layer) in the open lawns.</summary>
        private static void NaturalGround(LayerContext ctx, ParkSite s, List<float2[]> lawns, ParkStyle style, List<(string layer, OrientedRect rect)> amenities)
        {
            int wanted = math.clamp((int)(s.LayerRegion.Area / 4000f * style.GroundPatches), 1, (int)(8 * style.GroundPatches));
            int perLawn = math.max(1, (int)math.round(style.GroundPatches));
            int made = 0;
            var done = new List<(float2 c, float r)>();
            for (int li = 0; li < lawns.Count && made < wanted; li++)
            {
                var lr = LayerRegion.From(lawns[li]);
                if (lr == null || lr.Area < 300f) continue;
                var deepest = lr.DeepestPoint(out float ins, null, 12);
                // The first patch fills the lawn's heart; a meadow style adds more, scattered through it.
                for (int k = 0, tries = 0; k < perLawn && made < wanted && tries < 24; tries++)
                {
                    uint v = Hashing.Mix(Hashing.Mix(ctx.Seed, 0x6A7Du + (uint)li), (uint)tries);
                    float2 c; float r;
                    if (tries == 0) { c = deepest; r = math.min(ins - 2f, 4f + math.sqrt(lr.Area) * 0.12f); }
                    else
                    {
                        c = math.lerp(lr.Min, lr.Max, new float2(Hashing.Unit(v), Hashing.Unit(Hashing.Mix(v, 1u))));
                        if (!lr.Contains(c)) continue;
                        r = math.min(lr.BoundaryDistance(c, 40f) - 2f, 4f + math.sqrt(lr.Area) * 0.08f);
                    }
                    if (r < 3f) continue;
                    bool clash = false;
                    foreach (var d in done) if (math.distance(d.c, c) < d.r + r) { clash = true; break; }
                    // Never under an amenity (a sand court on painted meadow reads as a mistake).
                    foreach (var (_, rect) in amenities)
                        if (math.distance(rect.Center, c) < r + math.length(new float2(rect.HalfLength, rect.HalfWidth))) { clash = true; break; }
                    if (clash) continue;
                    // Terrain paint, interior only (the blob stays inside the lawn), blended at its edge.
                    TerrainPaint.Patch(ctx, LeisureKit.Blob(c, r * 0.85f, v, 16, 0.22f), ctx.I("naturalLayer"), 0.8f + 0.2f * Hashing.Unit(v), 3f);
                    done.Add((c, r));
                    made++; k++;
                }
            }
        }

        /// <summary>
        /// (s33) Trails painted into the ground (TrailPaint): a dirt stroke per trail, plus an invisible pedestrian path so
        /// cims can walk it when the playset has one (never the drivable invisible paths); otherwise visual only (Info).
        /// </summary>
        private static void PaintedTrails(LayerContext ctx, List<PathCurve> trails, List<Gate> gates, float strength = 0.85f)
        {
            var kit = new Placemaker.Core.Paint.PaintKit(ctx.Plan.Paint, ctx.H, Hashing.Mix(ctx.Seed, 0x7A11u)) { Outline = ctx.LayerRegion.Ring };
            string walk = TrailPaint.WalkableNet(ctx.Site);
            foreach (var c in trails)
            {
                if (c.Samples == null || c.Samples.Length < 2) continue;
                TrailPaint.Stroke(kit, c.Samples, math.max(1.6f, c.HalfWidth * 2f + 0.4f), strength);
                if (walk == null) continue;
                var net = ctx.CurvedNet(c.A, c.B, c.C, c.D, c.Role, new AssetRef(walk));
                net.Category = AssetCategories.InvisiblePath;
                if (c.GateA >= 0 && c.GateA < gates.Count && gates[c.GateA].AttachId >= 0) { net.StartAttach = gates[c.GateA].AttachId; net.StartAttachT = gates[c.GateA].AttachT; }
                if (c.GateD >= 0 && c.GateD < gates.Count && gates[c.GateD].AttachId >= 0) { net.EndAttach = gates[c.GateD].AttachId; net.EndAttachT = gates[c.GateD].AttachT; }
            }
            TerrainPaint.Warn(ctx);
            if (walk == null) ctx.Issue(IssueSeverity.Info, TrailPaint.VisualOnlyKey);
        }

        /// <summary>The net asset of each path tier: main walk = pathAsset, bike/trail from their categories with name fallbacks.</summary>

        private static Func<PathTier, AssetRef> TierAssets(LayerContext ctx)
        {
            var main = AssetMix.One(ctx.S("pathAsset"));
            string small = ctx.S("smallPathAsset");
            var smallRef = string.IsNullOrEmpty(small) ? main : AssetMix.One(small);
            var bike = ParkPathNets.Bike(ctx.S("bikeAsset"));
            var trail = ParkPathNets.Trail(ctx.S("trailAsset"));
            return tier =>
            {
                switch (tier)
                {
                    case PathTier.Bike: return bike;
                    case PathTier.Trail: case PathTier.Service: return trail;
                    case PathTier.Small: case PathTier.Desire: return smallRef;
                    default: return main;
                }
            };
        }

        private static int LoopsFor(string mode, float area)
        {
            switch (mode)
            {
                case "minimal": return 0;
                case "loop": return 1;
                case "network": return math.max(2, ParkPathPlanner.LoopsByArea(area));
                default: return ParkPathPlanner.LoopsByArea(area);
            }
        }

        private static List<float2[]> Lawns(LayerContext ctx, ParkSite s, float2[] plaza)
        {
            bool hasPlaza = plaza.Length >= 3;
            if (s.Curves.Count == 0 && s.Lake.Length < 3 && !hasPlaza)
                return new List<float2[]> { s.LayerRegion.Ring };
            var corridors = s.Occ.Corridors;
            var lake = s.Lake;
            // (perf) Lake and plaza tests only near their bounds (beyond them both are false anyway).
            Bounds(lake, 2.01f, out var lakeLo, out var lakeHi);
            Bounds(plaza, 1.01f, out var plazaLo, out var plazaHi);
            return FreeSpace.Pieces(s.LayerRegion, p =>
                    corridors.Overlaps(p, 0.75f)
                    || lake.Length >= 3 && math.all(p >= lakeLo & p <= lakeHi) && (Poly.Contains(lake, p) || ParkSite.MinDistToRing(lake, p) < 2f)
                    || hasPlaza && math.all(p >= plazaLo & p <= plazaHi) && (Poly.Contains(plaza, p) || ParkSite.MinDistToRing(plaza, p) < 1f),
                2f, 120f, ctx.Cancel);
        }

        private static void Bounds(float2[] ring, float margin, out float2 lo, out float2 hi)
        {
            lo = new float2(float.MaxValue); hi = new float2(float.MinValue);
            foreach (var p in ring) { lo = math.min(lo, p); hi = math.max(hi, p); }
            lo -= margin; hi += margin;
        }

        /// <summary>Lake terraform + water + permanent warning. Shared with the pond feature layer.</summary>
        public static void EmitLake(LayerContext ctx, float2[] lake, float depth = 3f)
        {
            // Water must sit below the lowest bank, otherwise it spills: level = lowest shore − 0.5 m.
            float shore = float.MaxValue;
            foreach (var p in lake) shore = math.min(shore, ctx.H(p));
            if (!math.isfinite(shore)) shore = 0f;
            float level = shore - 0.5f;
            var anchor = ParkLakePlanner.WaterAnchor(lake, out _);
            ctx.Plan.Terraform.Add(new PlannedTerraform
            {
                Ring = Poly.To3D(lake, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - depth,
                Permanent = true, WaterLevel = level, WaterAnchor = new float3(anchor.x, level, anchor.y),
            });
            BasinBank.Add(ctx, lake, level);   // on a slope: a bank, not a wall cut down to the water
            ctx.Surface(lake, SurfaceRole.Water, new AssetRef("", Array.Empty<string>()));
            ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.LakePermanent");
            ctx.Stat(Keys.StatLakeArea, Math.Round(Poly.Area(lake)), Keys.UnitM2);
        }

        /// <summary>
        /// (makeover) A neglected park: what people dumped at its edges and nobody took away: an old sofa, a pile of boxes, a
        /// barrel, a heap of rotten wood, tucked against the thickets of the boundary (vanilla ruins.junk / ruins.rubble).
        /// </summary>
        private static void Dumped(LayerContext ctx, ParkSite s)
        {
            string[][] kinds =
            {
                new[] { "OutdoorSofa01", "OutdoorSofa02", "OutdoorSofa03" }, new[] { "CardboardBoxPile01", "CardboardBoxPile02", "CardboardBoxPile03" },
                new[] { "Barrel01", "Barrel02", "Barrel03" }, new[] { "RubblePileWood01", "RubblePileWood02" }, new[] { "Pallet01", "Pallet02" },
            };
            int n = math.clamp((int)(s.LayerRegion.Area / 5000f), 2, 6), made = 0;
            var avoid = new List<float2>();
            foreach (var g in s.Gates) avoid.Add(g.Position);
            for (int k = 0; k < n * 3 && made < n; k++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0xD0Bu + (uint)k);
                if (!Spots.Find(s.Occ, 2.2f, Spots.Preference.NearBoundary, v, out var p, avoid, 18f, 2f, 1500)) break;
                var set = kinds[(made + (int)(v % 3u)) % kinds.Length];
                var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Furniture, new AssetRef(set[(int)(Hashing.Mix(v, 2u) % (uint)set.Length)], set), v, -1,
                    set[0].StartsWith("Rubble") ? "ruins.rubble" : "ruins.junk");
                if (set[0].StartsWith("Rubble")) o.Scale = 0.6f;
                s.Occ.Take(p, 2.2f, 206);
                avoid.Add(p);
                made++;
            }
            ctx.Plan.Facts["dumped"] = made;
        }

        /// <summary>
        /// (makeover) Neglect at the human level, readable from 45°: the playground fenced off behind a builders' fence with a
        /// barrier and cones where its path comes in, junk and litter by the toilet block.
        /// </summary>
        private static void Neglect(LayerContext ctx, ParkSite s, List<(string layer, float2 c, float r)> clearings, List<(string layer, OrientedRect rect)> amenities)
        {
            int fenced = 0, junk = 0;
            var fence = new AssetRef("FenceIndustrialLow01", "FenceIndustrialHigh01", "FenceResidentialLow01");
            var barrier = new AssetRef("ConstructionBarrier02", "ConstructionBarrier01");
            var cone = new AssetRef("TrafficCone01", "TrafficCone02");
            foreach (var (layer, c, r) in clearings)
            {
                if (layer != "playground") continue;
                float rr = r + 1.2f;
                int n = math.clamp((int)(rr * 6.283f / 4f), 10, 28);
                float a0 = Hashing.Unit(Hashing.Mix(ctx.Seed, 0xFE0u)) * 6.283f;
                for (int i = 0; i < n; i++)
                {
                    float a = a0 + i * 6.283f / n, b = a0 + (i + 1) * 6.283f / n;
                    var p0 = c + new float2(math.cos(a), math.sin(a)) * rr;
                    var p1 = c + new float2(math.cos(b), math.sin(b)) * rr;
                    var mid = (p0 + p1) * 0.5f;
                    if (!s.LayerRegion.ContainsDisc(mid, 0.5f)) continue;
                    // where a path comes in, the gap is closed by a barrier and cones (a fence never crosses a path net)
                    if (s.Occ.Corridors.Overlaps(p0, 0.6f, 0.2f) || s.Occ.Corridors.Overlaps(p1, 0.6f, 0.2f) || s.Occ.Corridors.Overlaps(mid, 0.6f, 0.2f))
                    {
                        var t = math.normalizesafe(p1 - p0);
                        var bo = ctx.Object(mid + (c - mid) * 0.08f, math.atan2(t.x, t.y) + 1.5708f, ObjectRole.Furniture, barrier, Hashing.Mix(ctx.Seed, 0xFE1u + (uint)i), -1, AssetCategories.ConstructionBarrier);
                        for (int k = 0; k < 2; k++)
                            ctx.Object(mid + t * (k == 0 ? -1.4f : 1.4f) + (mid - c) * 0.12f, k * 1.3f, ObjectRole.Furniture, cone, Hashing.Mix(ctx.Seed, 0xFE2u + (uint)(i * 2 + k)), -1, AssetCategories.ConstructionCone);
                        junk++;
                        continue;
                    }
                    ctx.StraightNet(p0, p1, NetRole.Fence, fence);
                    fenced++;
                }
            }
            // the toilet block: bin bags, boxes and a barrel round it
            string[][] litter = { new[] { "CardboardBoxPile01", "CardboardBoxPile02", "CardboardBoxPile03" }, new[] { "Barrel01", "Barrel02", "Barrel03" } };
            int li = 0;
            var toilets = new List<(float2 c, float r)>();
            foreach (var (layer, c, r) in clearings) if (layer == "restrooms") toilets.Add((c, r));
            foreach (var (layer, rect) in amenities)
                if (layer.IndexOf("restroom", StringComparison.OrdinalIgnoreCase) >= 0 || layer.IndexOf("toilet", StringComparison.OrdinalIgnoreCase) >= 0)
                    toilets.Add((rect.Center, math.max(rect.HalfLength, rect.HalfWidth)));
            foreach (var (tc, tr) in toilets)
            {
                for (int k = 0; k < 8 && li < 3; k++)
                {
                    uint v = Hashing.Mix(ctx.Seed, 0xFE3u + (uint)k);
                    float a = Hashing.Unit(v) * 6.283f;
                    var p = tc + new float2(math.cos(a), math.sin(a)) * (tr + 1.2f);
                    if (!s.LayerRegion.ContainsDisc(p, 1f) || s.Occ.Corridors.Overlaps(p, 1.2f, 0.2f)) continue;
                    var set = litter[li % litter.Length];
                    ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Furniture, new AssetRef(set[(int)(Hashing.Mix(v, 2u) % (uint)set.Length)], set), v, -1, "ruins.junk");
                    li++; junk++;
                }
            }
            ctx.Plan.Facts["neglectFence"] = fenced;
            ctx.Plan.Facts["neglectJunk"] = junk;
        }

        internal static string[] StyleTrees(string style)
        {
            switch (style)
            {
                case "woodland": return ParkArt.WoodlandTrees;
                case "formal": case "modern": case "urban": return ParkArt.AvenueTrees;
                case "victorian": return new[] { "EU_ChestnutTree01", "NA_LondonPlaneTree01", "OakTree01", "NA_LindenTree01" };
                case "landscape": return new[] { "OakTree01", "EU_ChestnutTree01", "NA_LindenTree01", "NA_HickoryTree01" };
                case "meadow": case "wildflower": return new[] { "OakTree01", "BirchTree01", "NA_HickoryTree01" };
                case "riverside": return new[] { "EU_AlderTree01", "EU_PoplarTree01", "BirchTree01", "OakTree01" };
                case "pocket": return new[] { "NA_LindenTree01", "NA_LondonPlaneTree01", "BirchTree01" };
                case "picnic": return new[] { "OakTree01", "EU_ChestnutTree01", "NA_LondonPlaneTree01", "NA_LindenTree01" };
                case "wild": return new[] { "OakTree01", "BirchTree01", "EU_AlderTree01", "NA_HickoryTree01", "OakTree01", "PineTree01", "BirchTree01" };
                case "neglected": return new[] { "BirchTree01", "OakTree01", "EU_PoplarTree01", "NA_HickoryTree01", "EU_AlderTree01", "NA_LindenTree01" };
                default: return ParkArt.ParkTrees;
            }
        }

        internal static string[] StyleBushes(string style)
        {
            switch (style)
            {
                case "formal": case "modern": case "urban": case "woodland": return ParkArt.GreenShrubs;
                case "meadow": case "wildflower": case "garden": return ParkArt.FlowerShrubs;
                case "wild": return new[] { "GreenBushWild01", "GreenBushWildAdult01", "FlowerBushWild02", "GreenBushWild02", "GreenBushWildElderly01" };
                case "neglected": return new[] { "GreenBushWildElderly01", "FlowerBushWildDead02", "GreenBushWildAdult01", "FlowerBushWild02", "GreenBushWild02", "GreenBushWildElderly01" };
                default: return ParkArt.MixedShrubs;
            }
        }

        private static void EmitItems(LayerContext ctx, ParkSite s, string[] treeIds, string[] bushIds)
        {
            var bench = AssetMix.One(ctx.S("benchAsset"), ParkArt.Benches);
            var lamp = AssetMix.One(ctx.S("lampAsset"), ParkArt.ParkLamps);
            var bin = AssetMix.One(ctx.S("binAsset"), ParkArt.Bins);
            bool neglected = ctx.S("style") == "neglected";
            foreach (var it in s.Items)
            {
                switch (it.Kind)
                {
                    case ParkItemKind.Tree:
                    {
                        // a neglected park: old trees, and the odd dead one nobody has taken down
                        int age = it.Age;
                        if (neglected) { float u = Hashing.Unit(Hashing.Mix(ctx.Seed, (int)math.floor(it.Position.x * 3f), (int)math.floor(it.Position.y * 3f))); age = u < 0.12f ? PlannedAge.Dead : u < 0.45f ? PlannedAge.Old : age; }
                        ctx.Object(it.Position, it.Yaw, ObjectRole.Tree, AssetMix.Pick(treeIds, it.Variant), it.Variant, age);
                        break;
                    }
                    case ParkItemKind.Bush: ctx.Object(it.Position, it.Yaw, ObjectRole.Bush, AssetMix.Pick(bushIds, it.Variant), it.Variant); break;
                    case ParkItemKind.Bench:
                    {
                        // a neglected park: a bench gone (only its scuff stays), another kicked over on its back or its side
                        float u = neglected ? Hashing.Unit(Hashing.Mix(it.Variant, 0xBE0u)) : 1f;
                        if (u < 0.15f) break;
                        var o = ctx.Object(it.Position, it.Yaw, ObjectRole.Bench, bench, it.Variant);
                        if (u < 0.42f) { o.Roll = (u < 0.3f ? 1f : -1f) * 1.5f; o.Elevation = 0.25f; }
                        else if (u < 0.55f) { o.Pitch = 0.22f; o.Roll = 0.12f; }
                        break;
                    }
                    case ParkItemKind.Lamp:
                    {
                        var o = ctx.Object(it.Position, it.Yaw, ObjectRole.Lamp, lamp, it.Variant);
                        // leaning lamp posts nobody straightened
                        if (neglected) { float u = Hashing.Unit(Hashing.Mix(it.Variant, 0x1A3u)); if (u < 0.35f) { o.Pitch = (u - 0.17f) * 0.9f; o.Roll = (Hashing.Unit(Hashing.Mix(it.Variant, 7u)) - 0.5f) * 0.25f; } }
                        break;
                    }
                    case ParkItemKind.Bin: ctx.Object(it.Position, it.Yaw, ObjectRole.Bin, bin, it.Variant); break;
                    case ParkItemKind.Spawner:
                        ctx.Object(it.Position, 0f, ObjectRole.AnimalSpawner, new AssetRef("", "Forest Animal Spawner"), it.Variant); break;
                }
            }
        }
    }
}
