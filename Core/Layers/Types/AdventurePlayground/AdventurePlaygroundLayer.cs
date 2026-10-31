// Adventure playground: a whole park built for play. Composition, not soup:
//
//   * circulation — a meandering path network from the entrances (loop on bigger sites);
//   * the parents' hub — a paved square near the middle with shade (shelter and/or trees), benches on
//     its rim looking out at the play zones, bins and picnic tables;
//   * play zones by age around the hub — toddlers closest (low fence, sand), juniors (swings at the
//     rim, slides, climbing), teens farthest (climbing, fitness), or one mixed zone; each zone is a
//     cluster of equipment on a soft surface with a safety clearance between the items, worn ground
//     painted round it and a parent bench on the hub side;
//   * signature zones — a splash pad with jets and a chain of stepping pools (water play), a nature
//     play area on a mound with a log balance trail, boulders and a willow tunnel, or a central
//     jungle-gym tower ringed by climbing frames;
//   * sightlines — the corridor from the hub to every zone is kept free of trees and bushes, so
//     parents see every zone from the benches; shade trees stand on the far side of each zone.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Biomes;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.AdventurePlayground
{
    [AreaGenerator(217, Group = "parks")]
    public sealed class AdventurePlaygroundLayer : LeisureLayer
    {
        public const string K = "Placemaker.AdvPlay.";
        public static readonly string[] Styles = { "adventure", "toddler", "water", "nature", "jungle" };

        protected override string Area => "AdvPlay";
        public override string Id => "adventurePlayground";
        public override string Icon => "Media/Game/Icons/Play.svg";
        public override float MinArea => 600f;
        protected override int WantedEntrances(LayerRegion region) => math.min(2, Entrances.WantedByArea(region.Area));

        protected override List<FeatureInfo> BuildFeatures()
        {
            var kick = Feature(KickaboutAddOn.LayerId);
            kick.LabelKey = K + "Feature.kickabout";
            kick.TooltipKey = K + "Feature.kickabout.Tooltip";
            return new List<FeatureInfo>
            {
                Feature("restrooms"), Feature("shops", ("count", 1.0), ("where", "entrances")), kick, Feature("picnic", ("groups", 2.0)),
                StreetShopsFeature(Area), Feature("rocks", ("clusters", 2.0), ("size", "small")),
                Feature("decals", ("theme", "dirt"), ("density", 0.5)), Feature("terrain", ("shape", "mounds"), ("height", 1.5)),
            };
        }

        protected override void DefineControls(ControlSet c)
        {
            // Play zones (essentials first).
            c.Choice("style", "adventure", ControlTier.Essential, "playZones", Styles).Tip();
            c.Choice("ageZones", "byAge", ControlTier.Essential, "playZones", "byAge", "mixed", "toddlers").Tip();
            c.Slider("equipment", 16, 4, 48, 1, "count", ControlTier.Essential, "playZones").Tip();
            c.Slider("clearance", 2.5, 1.5, 4, 0.5, "m", ControlTier.Essential, "playZones").Tip();
            c.Choice("ground", "sand", ControlTier.Essential, "playZones", "sand", "rubber", "bark", "grass").Tip();
            // Water play.
            c.Toggle("waterPlay", true, ControlTier.Essential, "water").Tip();
            c.Slider("jets", 8, 2, 24, 1, "count", ControlTier.More, "water").When("waterPlay=true");
            c.Slider("pools", 3, 0, 6, 1, "count", ControlTier.More, "water").When("waterPlay=true").Tip();
            // Nature play.
            c.Toggle("naturePlay", true, ControlTier.Essential, "nature").Tip();
            // makeover (2026-10-29): wild places between the play zones (ParkMoods): a knoll of boulders to scramble, a ravine,
            // a copse with logs to balance on.
            c.Toggle("moods", true, ControlTier.Essential, "nature").Tip();
            c.Toggle("moodGround", true, ControlTier.More, "nature").When("moods=true").Tip();
            BiomeMix.Define(c, Biomes);
            c.Slider("logs", 8, 0, 24, 1, "count", ControlTier.More, "nature").When("naturePlay=true");
            c.Slider("boulders", 6, 0, 20, 1, "count", ControlTier.More, "nature").When("naturePlay=true");
            c.Toggle("mound", true, ControlTier.More, "nature").When("naturePlay=true").Tip();
            c.Slider("moundHeight", 1.5, 0.5, 3, 0.5, "m", ControlTier.More, "nature").When("mound=true");
            c.Toggle("tunnel", true, ControlTier.More, "nature").When("naturePlay=true").Tip();
            // Parents.
            c.Slider("benches", 10, 2, 30, 1, "count", ControlTier.Essential, "parents").Tip();
            c.Choice("shade", "both", ControlTier.Essential, "parents", "both", "trees", "shelters", "none").Tip();
            c.Toggle("sightlines", true, ControlTier.More, "parents").Tip();
            c.Toggle("hubTables", true, ControlTier.More, "parents").Tip();
            c.Slider("treeDensity", 40, 0, 150, 5, "perHa", ControlTier.More, "parents");
            // Ground.
            c.Toggle("wornGround", true, ControlTier.More, "ground").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "ground").When("wornGround=true").Tip();
            c.Asset("surfaceAsset", AssetCategories.SurfaceNatural, ControlTier.More, "ground");
            // Boundary.
            c.Toggle("fence", true, ControlTier.More, "boundary").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Toggle("lamps", true, ControlTier.More, "boundary");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "boundary");
            // Assets.
            c.AssetMulti("equipmentAssets", AssetCategories.Playground, ControlTier.More, "assets");
            c.AssetMulti("climbAssets", AssetCategories.PlayClimb, ControlTier.More, "assets");
            c.AssetMulti("toddlerAssets", AssetCategories.PlayToddler, ControlTier.More, "assets");
            c.Asset("shelterAsset", AssetCategories.Shelter, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            c.RandomRange("equipment", 8, 28);
            c.RandomRange("benches", 4, 16);
            c.Fixed("wornGround");
            c.Fixed("paintLayer");
        }

        /// <summary>(Biome mix) The wild ground between the play zones: a glade, a wood to build dens in, rocks to scramble, long grass, bark.</summary>
        public static readonly BiomeOffer Biomes = new BiomeOffer(("lawn", 40), ("meadow", 10), ("mulch", 5), ("glade", 15), ("forest", 15), ("rocky", 15), ("wetland", 0)) { Scale = 45 };

        protected override void DefinePresets()
        {
            Preset("adventure", ("style", "adventure"), ("ageZones", "byAge"), ("equipment", 24.0), ("ground", "sand"), ("waterPlay", true), ("naturePlay", true),
                ("biomeLawn", 40.0), ("biomeMeadow", 10.0), ("biomeMulch", 5.0), ("biomeGlade", 15.0), ("biomeForest", 15.0), ("biomeRocky", 15.0));
            Preset("toddler", ("style", "toddler"), ("ageZones", "toddlers"), ("equipment", 14.0), ("clearance", 1.5), ("ground", "rubber"),
                ("waterPlay", true), ("jets", 4.0), ("pools", 0.0), ("naturePlay", false), ("benches", 14.0), ("shade", "shelters"), ("fence", true),
                ("biomeLawn", 72.0), ("biomeMeadow", 10.0), ("biomeMulch", 10.0), ("biomeGlade", 8.0), ("biomeForest", 0.0), ("biomeRocky", 0.0));
            Preset("water", ("style", "water"), ("ageZones", "mixed"), ("equipment", 8.0), ("ground", "sand"), ("waterPlay", true), ("jets", 18.0),
                ("pools", 5.0), ("naturePlay", false), ("shade", "both"),
                ("biomeLawn", 60.0), ("biomeMeadow", 15.0), ("biomeMulch", 0.0), ("biomeGlade", 15.0), ("biomeForest", 0.0), ("biomeRocky", 0.0), ("biomeWetland", 10.0));
            Preset("nature", ("style", "nature"), ("ageZones", "mixed"), ("equipment", 6.0), ("ground", "bark"), ("waterPlay", false), ("naturePlay", true),
                ("logs", 18.0), ("boulders", 14.0), ("mound", true), ("moundHeight", 2.5), ("tunnel", true), ("treeDensity", 75.0), ("lamps", false),
                ("biomeLawn", 15.0), ("biomeMeadow", 15.0), ("biomeMulch", 0.0), ("biomeGlade", 25.0), ("biomeForest", 25.0), ("biomeRocky", 20.0));
            Preset("jungle", ("style", "jungle"), ("ageZones", "byAge"), ("equipment", 30.0), ("clearance", 3.0), ("ground", "sand"), ("waterPlay", false),
                ("naturePlay", false), ("shade", "trees"),
                ("biomeLawn", 20.0), ("biomeMeadow", 0.0), ("biomeMulch", 0.0), ("biomeGlade", 20.0), ("biomeForest", 45.0), ("biomeRocky", 15.0));
        }

        // ───────────── planning ─────────────

        private enum ZoneKind { Toddler, Junior, Teen, Mixed, Jungle, Water, Nature }

        private sealed class Zone
        {
            public ZoneKind Kind;
            public int Items;
            public float ItemRadius, Spacing, Radius;
            public float2 Centre;
            public bool Placed, Fenced;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            var chains = new CategoryChains(ctx);
            Lawn.Cover(ctx, region.Ring, AssetMix.One("", ParkArt.Grass));

            // Circulation: meandering paths between the entrances, a loop on bigger sites.
            int loops = region.Area > 9000f ? 1 : 0;
            NaturalPaths(ctx, occ, gates, loops, 3.5f, 24f, "narrow", AssetMix.One(ctx.S("pathAsset")), region.Area > 25000f);
            ctx.Check();

            // Parents' hub near the middle, joined to the nearest path.
            region.DeepestPoint(out float ins, null, 14);
            float hubR = math.clamp(ins * 0.16f, 4.5f, 10f);
            bool hub = Spots.Find(occ, hubR + 1f, Spots.Preference.Centre, Hashing.Mix(ctx.Seed, 0x40Bu), out var hubC, null, 0f, 2f, 2500);
            if (!hub)
            {
                hubR = 3.5f;
                hub = Spots.Find(occ, hubR + 0.5f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x40Cu), out hubC, null, 0f, 1f, 2500);
            }
            if (!hub) hubC = region.DeepestPoint(out _, null, 12);
            var spurs = new List<(float2 a, float2 b)>();
            if (hub)
            {
                ctx.Surface(LayerRegion.Circle(hubC, hubR, 20), SurfaceRole.Plaza, AssetMix.One(""), AssetCategories.SurfacePaving);
                occ.Take(hubC, hubR, 0);
                if (occ.Paths.Count > 0 && occ.Paths.Nearest(hubC, 1000f, out var foot) >= 0)
                {
                    var rim = hubC + math.normalizesafe(foot - hubC) * hubR;
                    if (math.distance(rim, foot) > 1f && !region.CrossesBoundary(rim, foot, false))
                    {
                        LeisureKit.Path(ctx, occ, rim, foot, NetRole.Pathway, AssetMix.One(ctx.S("pathAsset")));
                        spurs.Add((rim, foot));
                    }
                }
            }
            ctx.Check();

            // Zones, placed around the hub: nearest first for the youngest.
            var zones = PlanZones(ctx, region, occ);
            PlaceZones(ctx, region, occ, zones, hubC, hub ? hubR : 0f);
            ctx.Check();

            int items = 0, benches = 0;
            var sight = new List<(float2 a, float2 b)>();
            foreach (var z in zones)
            {
                if (!z.Placed) continue;
                var toHub = math.normalizesafe(hubC - z.Centre, new float2(0, 1));
                switch (z.Kind)
                {
                    case ZoneKind.Water: items += BuildWater(ctx, occ, z, toHub, chains); break;
                    case ZoneKind.Nature: items += BuildNature(ctx, region, occ, z, toHub, chains); break;
                    default: items += BuildEquipment(ctx, z, toHub, chains); break;
                }
                occ.Take(z.Centre, z.Radius, 2);
                if (z.Kind == ZoneKind.Toddler && (ctx.B("fence") || ctx.S("style") == "toddler"))
                {
                    z.Fenced = true;
                    foreach (var run in Fences.Ring(z.Centre, z.Radius + 0.4f, 12, toHub, 3f))
                    {
                        ctx.StraightNet(run.c0, run.c1, NetRole.Fence, AssetMix.One(ctx.S("fenceAsset"), PlayArt.Fence));
                        occ.TakeCorridor(run.c0, run.c1, 0.3f, 5);
                    }
                }
                if (ctx.B("wornGround") && z.Kind != ZoneKind.Water)
                    TerrainPaint.Patch(ctx, LeisureKit.Blob(z.Centre, (z.Radius + 1.8f) / 1.15f, Hashing.Mix(ctx.Seed, 0xD1Au + (uint)z.Kind), 16, 0.12f),
                        ctx.I("paintLayer"), 0.6f, 2f);
                sight.Add((hubC, z.Centre));
            }
            ctx.Check();

            // Wild places in the ground the zones left (makeover): they take it before the benches, hub dressing and shade trees.
            var moodSite = new ParkSite { LayerRegion = region, Occ = occ, Seed = ctx.Seed, Cancel = ctx.Cancel, Gates = gates };
            List<MoodPlace> moods = null;
            // (Biome mix) the wild ground's natural areas replace the places with moods
            var biomes = BiomeHost.Plan(ctx, occ, Biomes, gates.ConvertAll(g => g.Position));
            if (biomes == null && ctx.B("moods"))
            {
                var open = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f), 2f, 80f, ctx.Cancel);
                moods = ParkMoods.Plan(ctx, moodSite, open, "playground", ctx.B("moodGround"), ctx.S("style") == "toddler" ? 0.5f : 1f);
            }

            // Parent benches on every zone rim, on the side facing the hub (sightline back to the parents).
            var bench = AssetMix.One(ctx.S("benchAsset"), ParkArt.Benches);
            int wantedBenches = ctx.I("benches");
            int placedZones = 0;
            foreach (var z in zones) if (z.Placed) placedZones++;
            int perZone = placedZones == 0 ? 0 : math.clamp(wantedBenches / (placedZones + (hub ? 1 : 0)), 1, 3);
            foreach (var z in zones)
            {
                if (!z.Placed) continue;
                var toHub = math.normalizesafe(hubC - z.Centre, new float2(0, 1));
                float baseA = math.atan2(toHub.y, toHub.x);
                int done = 0;
                for (int k = 0; k < 7 && done < perZone && benches < wantedBenches; k++)
                {
                    float a = baseA + (k == 0 ? 0f : ((k + 1) / 2) * 0.42f * ((k & 1) == 0 ? 1f : -1f));
                    var p = z.Centre + new float2(math.cos(a), math.sin(a)) * (z.Radius + (z.Fenced ? 2.4f : 1.6f));
                    if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, z.Centre), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE40u + (uint)benches))) { done++; benches++; }
                }
            }

            // Hub dressing: shelter, benches on the rim looking out at the zones, bins, picnic tables.
            if (hub) benches += DressHub(ctx, occ, hubC, hubR, zones, spurs, wantedBenches - benches, chains);
            ctx.Check();

            // Shade trees behind each zone (far side from the hub keeps the sightline open).
            string shade = ctx.S("shade");
            var treeIds = ctx.L("treeSpecies");
            if (treeIds.Length == 0) treeIds = ctx.S("style") == "nature" ? ParkArt.WoodlandTrees : PlayArt.ShadeTrees;
            if (shade == "trees" || shade == "both")
                foreach (var z in zones)
                {
                    if (!z.Placed) continue;
                    var away = math.normalizesafe(z.Centre - hubC, new float2(0, 1));
                    float baseA = math.atan2(away.y, away.x);
                    int n = math.clamp((int)(z.Radius / 3f), 3, 7);
                    for (int i = 0; i < n; i++)
                    {
                        float a = baseA + (i - (n - 1) * 0.5f) * (2.2f / n);
                        var p = z.Centre + new float2(math.cos(a), math.sin(a)) * (z.Radius + 3.2f);
                        uint v = Hashing.Mix(ctx.Seed, 0x5AD0u + (uint)(z.Kind) * 31u + (uint)i);
                        LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), null, 2.4f, v, 0.8f, 0.3f, 3, 3);
                    }
                }

            if (ctx.B("lamps"))
            {
                int k = 0;
                foreach (var (p, nrm, hw) in PathWalk.Every(occ, 24f))
                {
                    var q = p + nrm * ((k & 1) == 0 ? 1f : -1f) * (hw + 0.8f);
                    LeisureKit.Put(ctx, occ, q, 0f, ObjectRole.Lamp, AssetMix.One("", ParkArt.ParkLamps), null, 0.4f, Hashing.Mix(ctx.Seed, 0x1AD0u + (uint)k), 0.3f, 0.2f);
                    k++;
                }
            }
            ctx.Check();

            // Planting fills the rest, never inside a sightline corridor.
            bool keepSight = ctx.B("sightlines");
            System.Func<float2, bool> blocksView = p =>
            {
                if (!keepSight) return false;
                foreach (var (a, b) in sight)
                    if (SegmentIndex.DistSq(p, a, b) < 3.5f * 3.5f) return true;
                return false;
            };
            float ha = region.Area / 10000f;
            int trees = math.min(4000, (int)(ha * ctx.F("treeDensity")));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, treeIds, 2.4f, 18f, 0.4f, 0x7AD0u, blocksView, 1, 3);
            LeisureKit.Plant(ctx, occ, region, math.min(4000, (int)(ha * ctx.F("treeDensity") * 0.6f)), ObjectRole.Bush, ParkArt.MixedShrubs, 1f, 8f, 0.3f, 0xB0D0u,
                p => blocksView(p) || region.BoundaryDistance(p, 9f) > 8f);
            ctx.Check();

            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(ctx.S("fenceAsset"), PlayArt.Fence));
            ParkMoods.Emit(ctx, moodSite, moods, false);

            // Regions: lawn left over for features; the hub is the core when there is one.
            EmitGreenRegions(ctx, occ, 100f);
            CommonStats(ctx);
            ctx.Stat(Keys.StatPlayItems, items, Keys.UnitCount);
            ctx.Plan.Facts["playZones"] = placedZones;
            ctx.Plan.Facts["hub"] = hub ? 1 : 0;
            ctx.Plan.Facts["sightlines"] = keepSight ? sight.Count : 0;
            if (placedZones == 0) ctx.Issue(IssueSeverity.Info, K + "Issue.NoZones");
        }

        // ───────────── zones ─────────────

        private static List<Zone> PlanZones(LayerContext ctx, LayerRegion region, Occupancy occ)
        {
            string style = ctx.S("style"), ages = ctx.S("ageZones");
            if (style == "toddler") ages = "toddlers";
            var kinds = new List<ZoneKind>();
            if (style == "water") kinds.Add(ZoneKind.Water);
            if (style == "nature") kinds.Add(ZoneKind.Nature);
            if (style == "jungle") kinds.Add(ZoneKind.Jungle);
            switch (ages)
            {
                case "toddlers": kinds.Insert(0, ZoneKind.Toddler); kinds.Add(ZoneKind.Toddler); break;
                case "mixed": kinds.Add(ZoneKind.Mixed); break;
                default:
                    kinds.Insert(0, ZoneKind.Toddler); kinds.Add(ZoneKind.Junior);
                    if (style != "jungle") kinds.Add(ZoneKind.Teen);
                    break;
            }
            if (ctx.B("waterPlay") && !kinds.Contains(ZoneKind.Water)) kinds.Add(ZoneKind.Water);
            if (ctx.B("naturePlay") && !kinds.Contains(ZoneKind.Nature)) kinds.Add(ZoneKind.Nature);

            // Equipment shared between the equipment zones by weight.
            float weights = 0f;
            foreach (var k in kinds) weights += Weight(k);
            int total = ctx.I("equipment");
            float clear = ctx.F("clearance");
            var zones = new List<Zone>();
            foreach (var k in kinds)
            {
                var z = new Zone { Kind = k };
                switch (k)
                {
                    case ZoneKind.Water:
                    {
                        int jets = ctx.I("jets");
                        z.Items = jets;
                        z.Radius = math.clamp(1.25f * math.sqrt(jets * 4f) + 3.5f, 6f, 16f);
                        break;
                    }
                    case ZoneKind.Nature:
                    {
                        float need = ctx.I("logs") * 10f + ctx.I("boulders") * 6f + (ctx.B("tunnel") ? 60f : 0f) + 80f;
                        z.Radius = math.clamp(math.sqrt(need / math.PI) + 3f, 8f, 24f);
                        break;
                    }
                    default:
                    {
                        z.Items = math.max(2, (int)math.round(total * Weight(k) / math.max(0.1f, weights)));
                        // rf-parks: tighter play zones (the vanilla pieces are 2-4 m across; at 2.6 m item radii the sand discs read
                        // as mostly empty sand).
                        z.ItemRadius = k == ZoneKind.Toddler ? 1.4f : k == ZoneKind.Jungle ? 2.8f : 2.1f;
                        z.Spacing = z.ItemRadius * 2f + clear;
                        z.Radius = z.Spacing * 0.62f * math.sqrt(z.Items) + z.ItemRadius + clear * 0.5f + (k == ZoneKind.Jungle ? 2f : 0f);
                        break;
                    }
                }
                zones.Add(z);
            }
            // Never more than ~55 % of the area in zones: shrink everything evenly.
            float sum = 0f;
            foreach (var z in zones) sum += math.PI * (z.Radius + 2f) * (z.Radius + 2f);
            float scale = math.min(1f, math.sqrt(region.Area * 0.55f / math.max(1f, sum)));
            if (scale < 1f)
                foreach (var z in zones)
                {
                    z.Radius = math.max(4f, z.Radius * scale);
                    if (z.Spacing > 0f) z.Items = math.max(1, (int)(z.Items * scale * scale));
                }
            return zones;
        }

        private static float Weight(ZoneKind k)
        {
            switch (k)
            {
                case ZoneKind.Toddler: return 0.7f;
                case ZoneKind.Mixed: return 1.3f;
                case ZoneKind.Jungle: return 1.8f;
                case ZoneKind.Water: case ZoneKind.Nature: return 0f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Rings outward from the hub: the first distance where a zone fits wins, and among the fitting
        /// directions the one farthest (in angle) from the zones already placed, so zones spread round the hub.
        /// </summary>
        private static void PlaceZones(LayerContext ctx, LayerRegion region, Occupancy occ, List<Zone> zones, float2 hub, float hubR)
        {
            var dirs = new List<float>();
            float reach = math.length(region.Max - region.Min);
            var taken = new List<(float2 c, float r)>();
            for (int zi = 0; zi < zones.Count; zi++)
            {
                var z = zones[zi];
                for (int attempt = 0; attempt < 3 && !z.Placed; attempt++)
                {
                    float r = z.Radius;
                    float startD = hubR + r + 3f;
                    if (z.Kind == ZoneKind.Teen) startD += 6f;   // the big kids a little farther out
                    for (float d = startD; d < reach && !z.Placed; d += 3f)
                    {
                        int steps = math.clamp((int)(2f * math.PI * d / 4f), 12, 48);
                        float best = -1f; float2 bestP = default; float bestA = 0f;
                        float phase = Hashing.Unit(Hashing.Mix(ctx.Seed, 0x2A0u + (uint)zi)) * 6.283f;
                        for (int s = 0; s < steps; s++)
                        {
                            float a = phase + s * 2f * math.PI / steps;
                            var p = hub + new float2(math.cos(a), math.sin(a)) * d;
                            if (!occ.Free(p, r + 1f, r + 2.5f, 0.8f)) continue;
                            bool clash = false;
                            foreach (var t in taken) if (math.distance(t.c, p) < t.r + r + 4f) { clash = true; break; }
                            if (clash) continue;
                            float sep = 10f;
                            foreach (var da in dirs) sep = math.min(sep, math.abs(math.atan2(math.sin(a - da), math.cos(a - da))));
                            if (sep > best) { best = sep; bestP = p; bestA = a; }
                        }
                        if (best >= 0f)
                        {
                            z.Centre = bestP; z.Placed = true; dirs.Add(bestA);
                        }
                    }
                    if (!z.Placed)
                    {
                        z.Radius *= 0.75f;
                        if (z.Spacing > 0f) z.Items = math.max(1, (int)(z.Items * 0.6f));
                    }
                }
                if (z.Placed) taken.Add((z.Centre, z.Radius));
                else ctx.Issue(IssueSeverity.Info, K + "Issue.ZoneSkipped", K + "Zone." + z.Kind);
            }
        }

        private (string cat, string[] ids) EquipmentFor(LayerContext ctx, CategoryChains chains, ZoneKind kind, int index, bool outer)
        {
            var any = ctx.L("equipmentAssets");
            var climb = ctx.L("climbAssets");
            var tod = ctx.L("toddlerAssets");
            switch (kind)
            {
                case ZoneKind.Toddler:
                    return (chains.Get(tod.Length > 0, AssetCategories.PlayToddler, AssetCategories.Playground), tod.Length > 0 ? tod : null);
                case ZoneKind.Junior:
                    // Swings on the outer ring (their swing arc needs the free rim), slides and frames inside.
                    if (outer && (index & 1) == 0) return (chains.Get(any.Length > 0, AssetCategories.PlaySwing, AssetCategories.Playground), any);
                    if ((index & 1) == 0) return (chains.Get(any.Length > 0, AssetCategories.PlaySlide, AssetCategories.Playground), any);
                    return (chains.Get(climb.Length > 0, AssetCategories.PlayClimb, AssetCategories.Playground), climb.Length > 0 ? climb : any);
                case ZoneKind.Teen:
                    if (index % 3 == 2) return (chains.Get(false, AssetCategories.SportsFitness, AssetCategories.PlayClimb, AssetCategories.Playground), null);
                    return (chains.Get(climb.Length > 0, AssetCategories.PlayClimb, AssetCategories.Playground), climb.Length > 0 ? climb : any);
                case ZoneKind.Jungle:
                    if (index > 0 && index % 3 == 0) return (chains.Get(any.Length > 0, AssetCategories.PlaySlide, AssetCategories.Playground), any);
                    return (chains.Get(climb.Length > 0, AssetCategories.PlayClimb, AssetCategories.Playground), climb.Length > 0 ? climb : any);
                default:
                {
                    string[] mix = { AssetCategories.PlaySwing, AssetCategories.PlaySlide, AssetCategories.PlayClimb, AssetCategories.PlayToddler };
                    return (chains.Get(any.Length > 0, mix[index % mix.Length], AssetCategories.Playground), any);
                }
            }
        }

        private string[] GroundChain(LayerContext ctx, ZoneKind kind)
        {
            string g = ctx.S("ground");
            if (kind == ZoneKind.Toddler && g == "grass") g = "rubber";
            switch (g)
            {
                case "sand": return new[] { AssetCategories.SurfaceSand, AssetCategories.SurfaceNatural };
                case "rubber": return new[] { AssetCategories.SurfacePaving };
                case "bark": return new[] { AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural };
                default: return null;
            }
        }

        /// <summary>Equipment cluster (phyllotaxis) on a soft surface, items a safety clearance apart, facing outward.</summary>
        private int BuildEquipment(LayerContext ctx, Zone z, float2 toHub, CategoryChains chains)
        {
            var groundChain = GroundChain(ctx, z.Kind);
            string chosenSurface = ctx.S("surfaceAsset");
            if (groundChain != null)
            {
                string cat = chains.Get(chosenSurface.Length > 0, groundChain);
                if (cat != null)
                    ctx.Surface(LeisureKit.Blob(z.Centre, z.Radius / 1.12f, Hashing.Mix(ctx.Seed, 0x5F0u + (uint)z.Kind), 16, 0.1f), SurfaceRole.Decorative,
                        AssetMix.One(chosenSurface, PlayArt.Ground(ctx.S("ground"), z.Kind == ZoneKind.Toddler)), cat);
            }
            var placed = new List<(float2 p, float r)>();
            int n = 0;
            const float golden = 2.39996323f;
            float phase = Hashing.Unit(Hashing.Mix(ctx.Seed, 0x9A1u + (uint)z.Kind)) * 6.283f;
            // Jungle gym: one big tower in the middle, frames and slides around it.
            int start = 0;
            if (z.Kind == ZoneKind.Jungle)
            {
                var (cat, ids) = EquipmentFor(ctx, chains, z.Kind, 0, false);
                if (cat != null)
                {
                    uint v = Hashing.Mix(ctx.Seed, 0x7A3u);
                    ctx.Object(z.Centre, LeisureKit.Yaw(z.Centre, z.Centre + toHub), ObjectRole.Furniture, ids != null && ids.Length > 0 ? AssetMix.Pick(ids, v) : AssetMix.One("", PlayArt.Tower), v, -1, cat);
                    placed.Add((z.Centre, z.ItemRadius + 1.5f));
                    n++;
                }
                start = 3;   // leave the tower's own clearance free
            }
            for (int i = start; i < z.Items * 4 + start && n < z.Items; i++)
            {
                float rr = z.Spacing * 0.62f * math.sqrt(i + 0.5f);
                if (rr + z.ItemRadius > z.Radius - 0.3f) break;
                float a = phase + i * golden;
                var p = z.Centre + new float2(math.cos(a), math.sin(a)) * rr;
                bool clash = false;
                foreach (var q in placed) if (math.distance(q.p, p) < q.r + z.ItemRadius + ctx.F("clearance")) { clash = true; break; }
                if (clash) continue;
                var (cat, ids) = EquipmentFor(ctx, chains, z.Kind, n, rr > (z.Radius - z.ItemRadius) * 0.55f);
                if (cat == null) break;
                uint v = Hashing.Mix(ctx.Seed, 0xE90u + (uint)z.Kind * 1009u + (uint)n);
                ctx.Object(p, LeisureKit.Yaw(z.Centre, p), ObjectRole.Furniture, AssetMix.Pick(ids, v), v, -1, cat);
                placed.Add((p, z.ItemRadius));
                n++;
            }
            return n;
        }

        /// <summary>Splash pad: paved pad, water-look centre, jets on a spiral; stepping pools trail away from the hub.</summary>
        private static int BuildWater(LayerContext ctx, Occupancy occ, Zone z, float2 toHub, CategoryChains chains)
        {
            float padR = z.Radius;
            ctx.Surface(LayerRegion.Circle(z.Centre, padR, 24), SurfaceRole.Plaza, AssetMix.One("", ParkArt.Tiles), AssetCategories.SurfacePaving);
            string water = CategoryChains.Quiet(AssetCategories.SurfaceWater);
            if (water != null) ctx.Surface(LeisureKit.Blob(z.Centre, padR * 0.45f, Hashing.Mix(ctx.Seed, 0x3A7u), 14, 0.12f), SurfaceRole.Water, AssetMix.One(""), water);
            int n = 0;
            // Art pass (a1): never "water.jets" here: in the vanilla catalog that is an agricultural sprinkler boom.
            // Small fountains (about 3.5 m across) stand in for the jets, spaced by their real footprint.
            // rf-parks: the curated fountains are named explicitly and resolve by name, so an empty fountain category (a clean game)
            // no longer leaves the splash pad an empty disc.
            string jets = CategoryChains.Quiet(AssetCategories.CenterpieceFountain, AssetCategories.Centerpiece) ?? AssetCategories.CenterpieceFountain;
            if (jets != null)
            {
                const float golden = 2.39996323f;
                var jetSpots = new List<float2>();
                for (int i = 0; i < z.Items * 3 && n < math.max(1, (z.Items + 2) / 3); i++)
                {
                    float rr = 2.6f * math.sqrt(i + 0.5f);
                    if (rr > padR - 2f) break;
                    var p = z.Centre + new float2(math.cos(i * golden), math.sin(i * golden)) * rr;
                    bool clash = false;
                    foreach (var q in jetSpots) if (math.distance(q, p) < 5.4f) { clash = true; break; }
                    if (clash) continue;
                    jetSpots.Add(p);
                    ctx.Object(p, 0f, ObjectRole.Furniture, AssetMix.Pick(PlayArt.Jets, (uint)i), Hashing.Mix(ctx.Seed, 0x7E7u + (uint)i), -1, jets);
                    n++;
                }
            }
            // Stepping pools: a gentle arc of small water-look pools with boulders between them.
            int pools = ctx.I("pools");
            if (pools > 0 && water != null)
            {
                occ.Take(z.Centre, padR, 2);
                var away = -toHub;
                var side = new float2(-away.y, away.x);
                float bend = (Hashing.Unit(Hashing.Mix(ctx.Seed, 0xBE7u)) - 0.5f) * 0.6f;
                var prev = z.Centre + away * padR;
                string rock = CategoryChains.Quiet(AssetCategories.Rock);
                for (int i = 0; i < pools; i++)
                {
                    float r = 1.6f + 0.5f * Hashing.Unit(Hashing.Mix(ctx.Seed, 0x9007u + (uint)i));
                    var dir = math.normalizesafe(away + side * bend * (i + 1));
                    var c = prev + dir * (r + 1.6f);
                    if (!occ.Free(c, r + 0.3f, r + 1.3f, 0.3f)) break;
                    ctx.Surface(LeisureKit.Blob(c, r, Hashing.Mix(ctx.Seed, 0x9100u + (uint)i), 12, 0.12f), SurfaceRole.Water, AssetMix.One(""), water);
                    occ.Take(c, r + 0.3f, 2);
                    if (rock != null)
                    {
                        var s = math.lerp(prev, c, 0.5f) + side * ((i & 1) == 0 ? 1.6f : -1.6f);
                        LeisureKit.Put(ctx, occ, s, Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i)) * 6.28f, ObjectRole.Furniture, AssetMix.Pick(PlayArt.Boulders, (uint)i), rock, 0.7f,
                            Hashing.Mix(ctx.Seed, 0x9200u + (uint)i), 0.5f, 0.1f);
                    }
                    prev = c + dir * r;
                }
            }
            ctx.Plan.Facts["splashJets"] = n;
            return n;
        }

        /// <summary>Nature play: a mound, a log balance trail round it, boulder clusters and a willow tunnel.</summary>
        private static int BuildNature(LayerContext ctx, LayerRegion region, Occupancy occ, Zone z, float2 toHub, CategoryChains chains)
        {
            int n = 0;
            var side = new float2(-toHub.y, toHub.x);
            if (ctx.B("mound"))
            {
                float h = Mounds.Raise(ctx, region, z.Centre, z.Radius * 0.62f, 0f, ctx.F("moundHeight"), 0.3f);
                if (h > 0f) ctx.Plan.Facts["moundHeight"] = h;
            }
            var local = new List<(float2 p, float r)>();
            bool Fits(float2 p, float r)
            {
                if (math.distance(p, z.Centre) + r > z.Radius - 0.3f) return false;
                foreach (var q in local) if (math.distance(q.p, p) < q.r + r + 0.4f) return false;
                return true;
            }
            // Log balance trail round the mound.
            string logCat = ctx.I("logs") > 0 ? chains.Get(false, AssetCategories.PlayNature) : null;
            if (logCat != null)
            {
                // Art pass (a1): the vanilla logs are ~10 m trunks (and the automatic "play.nature" pool also holds a
                // chainsaw-wielding logger): a few whole trunks laid end to end round the mound as a balance trail.
                float ring = z.Radius * 0.72f;
                int logs = math.min(ctx.I("logs"), math.max(1, (int)(2f * math.PI * ring / 11.5f)));
                float step = 11.5f / math.max(6f, ring);
                float a0 = math.atan2(toHub.y, toHub.x) + 0.6f;
                for (int i = 0, placedLogs = 0; i < logs * 2 && placedLogs < logs; i++)
                {
                    float a = a0 + i * step;
                    var p = z.Centre + new float2(math.cos(a), math.sin(a)) * ring;
                    var t = new float2(-math.sin(a), math.cos(a));
                    bool fits = true;
                    for (int k = -2; k <= 2 && fits; k++) fits = Fits(p + t * (k * 2.4f), 0.6f);
                    if (!fits) continue;
                    ctx.Object(p, math.atan2(t.x, t.y), ObjectRole.Furniture, AssetMix.Pick(PlayArt.Logs, (uint)i), Hashing.Mix(ctx.Seed, 0x1060u + (uint)i), -1, logCat);
                    for (int k = -2; k <= 2; k++) local.Add((p + t * (k * 2.4f), 0.6f));
                    n++; placedLogs++;
                }
            }
            // Willow tunnel: two bush rows, an arched walk-through across the zone on the hub side.
            if (ctx.B("tunnel"))
            {
                float len = math.min(10f, z.Radius * 0.9f);
                var mid = z.Centre + toHub * (z.Radius * 0.35f);
                for (float u = -len * 0.5f; u <= len * 0.5f; u += 1.4f)
                    foreach (float s in new[] { -1.2f, 1.2f })
                    {
                        var p = mid + side * u + toHub * s;
                        if (!Fits(p, 0.6f)) continue;
                        ctx.Object(p, 0f, ObjectRole.Bush, AssetMix.Pick(PlayArt.TunnelShrubs, (uint)(u * 10f + 100f)), Hashing.Mix(ctx.Seed, 0x7E1u + (uint)(u * 10f + 100f)));
                        local.Add((p, 0.6f));
                    }
            }
            // Boulders in two or three clusters (scramble rocks).
            int boulders = ctx.I("boulders");
            string rock = boulders > 0 ? chains.Get(false, AssetCategories.Rock) : null;
            if (rock != null)
            {
                int clusters = math.clamp(boulders / 4, 1, 3), placed = 0;
                for (int k = 0; k < clusters; k++)
                {
                    float a = math.atan2(-toHub.y, -toHub.x) + (k - (clusters - 1) * 0.5f) * 1.3f;
                    var cc = z.Centre + new float2(math.cos(a), math.sin(a)) * (z.Radius * 0.5f);
                    int want = boulders / clusters + (k < boulders % clusters ? 1 : 0);
                    for (int i = 0; i < want * 4 && want > 0; i++)
                    {
                        float rr = 1.3f * math.sqrt(i + 0.5f), aa = i * 2.39996323f;
                        var p = cc + new float2(math.cos(aa), math.sin(aa)) * rr;
                        float br = 0.7f + 0.5f * Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)(k * 97 + i)));
                        if (!Fits(p, br)) continue;
                        ctx.Object(p, Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)(k * 31 + i))) * 6.28f, ObjectRole.Furniture, AssetMix.Pick(PlayArt.Boulders, (uint)(k * 31 + i)),
                            Hashing.Mix(ctx.Seed, 0xB01Du + (uint)(k * 64 + i)), -1, rock);
                        local.Add((p, br));
                        placed++; n++;
                        if (--want <= 0) break;
                    }
                }
                ctx.Plan.Facts["boulders"] = placed;
            }
            // One climbing tree or play hut in the middle when installed (optional, no issue).
            string climb = CategoryChains.Quiet(AssetCategories.PlayClimb);
            if (climb != null && !ctx.B("mound") && Fits(z.Centre, 2.2f))
            {
                ctx.Object(z.Centre, 0f, ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0xC11Bu), -1, climb);
                n++;
            }
            return n;
        }

        private int DressHub(LayerContext ctx, Occupancy occ, float2 hubC, float hubR, List<Zone> zones, List<(float2 a, float2 b)> spurs, int benchBudget,
            CategoryChains chains)
        {
            string shade = ctx.S("shade");
            var bench = AssetMix.One(ctx.S("benchAsset"), ParkArt.Benches);
            if ((shade == "shelters" || shade == "both") && hubR >= 6f)
            {
                string cat = chains.Get(ctx.S("shelterAsset").Length > 0, AssetCategories.Shelter, AssetCategories.CafeSeating);
                if (cat != null) ctx.Object(hubC, 0f, ObjectRole.Furniture, AssetMix.One(ctx.S("shelterAsset"), ParkArt.Pergolas), Hashing.Mix(ctx.Seed, 0x5E1u), -1, cat);
            }
            bool OnSpur(float2 p)
            {
                foreach (var (a, b) in spurs) if (SegmentIndex.DistSq(p, a, b) < 2.4f * 2.4f) return true;
                return false;
            }
            // Benches round the rim, each looking out at the zone nearest its direction (outward-facing).
            int placed = 0;
            int ring = math.clamp(benchBudget, 0, math.max(2, (int)(2f * math.PI * hubR / 3.2f)));
            for (int i = 0; i < ring; i++)
            {
                float a = i * 2f * math.PI / math.max(1, ring) + 0.3f;
                var p = hubC + new float2(math.cos(a), math.sin(a)) * (hubR - 1.2f);
                if (OnSpur(p) || occ.Paths.Overlaps(p, 1f, 0.4f)) continue;
                float2 look = p + (p - hubC);
                float bestD = float.MaxValue;
                foreach (var z in zones)
                {
                    if (!z.Placed) continue;
                    float d = math.distance(z.Centre, p);
                    if (math.dot(math.normalizesafe(z.Centre - hubC), math.normalizesafe(p - hubC)) > 0.3f && d < bestD) { bestD = d; look = z.Centre; }
                }
                ctx.Object(p, LeisureKit.Yaw(p, look), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, 0xB0B0u + (uint)i));
                placed++;
            }
            // Bins at two rim points away from the benches.
            for (int i = 0; i < 2; i++)
            {
                float a = 0.3f + (i + 0.5f) * math.PI;
                var p = hubC + new float2(math.cos(a), math.sin(a)) * (hubR + 1.2f);
                LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Bin, AssetMix.One("", ParkArt.Bins), null, 0.5f, Hashing.Mix(ctx.Seed, 0xB1Bu + (uint)i), 0.3f, 0.2f);
            }
            // Picnic tables just outside the hub, between the zones.
            if (ctx.B("hubTables"))
            {
                string cat = chains.Get(false, AssetCategories.Picnic);
                if (cat != null)
                {
                    int t = 0;
                    for (int i = 0; i < 12 && t < 3; i++)
                    {
                        float a = i * 2f * math.PI / 12f + 0.15f;
                        var p = hubC + new float2(math.cos(a), math.sin(a)) * (hubR + 3.5f);
                        if (LeisureKit.Put(ctx, occ, p, a, ObjectRole.Furniture, AssetMix.One("", CampArt.Tables), cat, 1.6f, Hashing.Mix(ctx.Seed, 0x91Cu + (uint)i), 0.8f, 0.6f)) t++;
                    }
                }
            }
            return placed;
        }
    }
}
