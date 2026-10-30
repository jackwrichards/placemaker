// Nature reserve / forest: dense natural planting (trees in clumps of mixed ages, an understorey of
// bushes), narrow meandering trails only, a few clearings with a bench each, a lookout beside the
// trail at the highest point, wildlife spawners and painted forest floor. No lamps, few benches.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(54, Group = "nature")]
    public sealed class NatureReserveLayer : LeisureLayer
    {
        protected override string Area => "Reserve";
        public override string Id => "natureReserve";
        public override string Icon => "Media/Game/Icons/Trees.svg";
        public override float MinArea => 2000f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("rocks", ("clusters", 4.0), ("size", "large")), Feature("picnic", ("groups", 2.0)), Feature("restrooms", ("byArea", true), ("haEach", 8.0)),
            Feature("decals", ("theme", "leaves"), ("density", 1.0)), Feature("terrain", ("shape", "hills"), ("height", 3.0)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("treeDensity", 220, 20, 450, 10, "perHa", ControlTier.Essential, "planting").Tip();
            c.Slider("bushDensity", 140, 0, 400, 10, "perHa", ControlTier.Essential, "planting");
            c.Slider("clearings", 2, 0, 8, 1, "count", ControlTier.Essential, "trails").Tip();
            c.Toggle("lookout", true, ControlTier.Essential, "trails").Tip();
            c.Asset("lookoutAsset", AssetCategories.Lookout, ControlTier.More, "trails").When("lookout=true");
            c.Asset("trailAsset", AssetCategories.Pathway, ControlTier.More, "trails");
            c.Slider("benches", 1, 0, 5, 0.5, "per100m", ControlTier.More, "trails");
            c.Toggle("animals", true, ControlTier.More, "planting").Tip();
            c.Toggle("forestFloor", true, ControlTier.More, "planting").Tip();
            c.Slider("floorLayer", 1, 1, 4, 1, "count", ControlTier.More, "planting").When("forestFloor=true");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "planting");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            c.RandomRange("treeDensity", 120, 300);
            c.Fixed("forestFloor");
        }

        protected override void DefinePresets()
        {
            Preset("forest", ("treeDensity", 260.0), ("bushDensity", 120.0), ("clearings", 2.0));
            Preset("heath", ("treeDensity", 20.0), ("bushDensity", 230.0), ("clearings", 3.0), ("lookout", true));
            Preset("wildwood", ("treeDensity", 380.0), ("bushDensity", 200.0), ("clearings", 1.0), ("benches", 0.5), ("lookout", false));
        }

        // ───────────── art direction (art pass a3) ─────────────
        // Explicit vanilla species per character, used while the species controls are on automatic. Presets only
        // set densities, so the character follows the density: open heath (≤ 80 trees/ha) is birch and pine over
        // heather-like flowering scrub; dense wildwood (≥ 340) is old oak/spruce/alder; in between a mixed forest.

        private static string Character(LayerContext ctx)
        {
            float t = ctx.F("treeDensity");
            return t <= 80f ? "heath" : t >= 340f ? "wildwood" : "forest";
        }

        private static string[] Trees(string character)
        {
            switch (character)
            {
                case "heath": return new[] { "BirchTree01", "PineTree01", "BirchTree01" };
                case "wildwood": return new[] { "OakTree01", "EuropeanSpruce01", "EU_AlderTree01", "NA_HickoryTree01", "OakTree01" };
                default: return new[] { "OakTree01", "EU_ChestnutTree01", "NA_LindenTree01", "SpruceTree01", "BirchTree01" };
            }
        }

        private static string[] Bushes(string character) => character == "heath"
            ? new[] { "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildElderly01", "FlowerBushWildDead02", "GreenBushWildChild01", "GreenBushWild01" }   // (nature makeover) moor scrub: low, mostly green, a little dead; the heather is the ground decal
            : new[] { "GreenBushWild01", "GreenBushWild02", "GreenBushWildAdult01", "GreenBushWildElderly01" };

        /// <summary>Open timber shelter as the automatic lookout (the lookout pool is 32-56 m buildings).</summary>
        private static readonly string[] Lookouts = { "CampgroundShelter02", "CampgroundShelter01" };
        private static readonly string[] Benches = { "CampgroundBench01", "CampgroundBench02" };
        /// <summary>Fallen trunks (vanilla 10 m logs) lying in the wood.</summary>
        private static readonly string[] Logs = { "Log01", "Log02", "Log04", "Log05", "Log07", "Log08" };

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            // No lawn surface: a reserve stands on natural ground (a mown grass texture under a forest read as a park).
            int loops = math.max(1, ParkPathPlanner.LoopsByArea(region.Area));
            NaturalPaths(ctx, occ, gates, loops, 5f, 34f, "narrow", AssetMix.One(ctx.S("trailAsset")), region.Area > 20000f);
            ctx.Check();

            // Lookout beside the trail at the highest point.
            if (ctx.B("lookout"))
            {
                bool picked = !string.IsNullOrEmpty(ctx.S("lookoutAsset"));
                string cat = picked ? AssetCategories.Lookout : LeisureKit.Category(ctx, "", AssetCategories.Shelter);
                if (cat != null)
                {
                    var asset = picked ? AssetMix.One(ctx.S("lookoutAsset")) : AssetMix.Pick(Lookouts, 0u);
                    float r = AssetMix.FootprintRadius(ctx.Site, asset, 4f);
                    var anchors = PathAnchors.Beside(occ, r, 8f, 1.2f);
                    ctx.Plan.Facts["lookoutAnchors"] = anchors.Count;
                    // Highest spot first; the next one when the best is taken (a richer trail network may cross it).
                    var order = new List<int>();
                    for (int i = 0; i < anchors.Count; i++) order.Add(i);
                    float Height(int i) => ctx.H(anchors[i].Position) + region.BoundaryDistance(anchors[i].Position, 30f) * 0.01f;
                    order.Sort((x, y) => Height(y).CompareTo(Height(x)) != 0 ? Height(y).CompareTo(Height(x)) : x.CompareTo(y));
                    foreach (int best in order)
                        if (LeisureKit.Put(ctx, occ, anchors[best].Position, LeisureKit.Yaw(anchors[best].Position, anchors[best].Toward), ObjectRole.Furniture, asset, cat, r,
                            Hashing.Mix(ctx.Seed, 0x100Cu), 0.5f, 0.3f, 0)) break;
                }
            }

            // Clearings: open meadows with a bench, kept free of trees.
            var clearings = new List<(float2 c, float r)>();
            var bench = AssetMix.Pick(Benches, 0u);
            for (int i = 0; i < ctx.I("clearings"); i++)
            {
                float r = 9f + 6f * Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i + 0xC1Eu));
                var avoid = new List<float2>();
                foreach (var cl in clearings) avoid.Add(cl.c);
                if (!Spots.Find(occ, r, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0xC1E0u + (uint)i), out var c, avoid, r * 3f, 2f, budget: 1500)) break;
                clearings.Add((c, r));
                var toPath = occ.Paths.Nearest(c, 300f, out var q) >= 0 ? q : region.Centroid;
                var b = c + math.normalizesafe(toPath - c) * (r * 0.5f);
                ctx.Object(b, LeisureKit.Yaw(b, c), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, 0xBE0u + (uint)i));
                occ.Take(c, r, 200);
            }
            PathBenches(ctx, occ, 100f / math.max(0.25f, ctx.F("benches")), bench, 0xBE20u);
            ctx.Check();

            // Dense planting: trees in clumps of mixed age, an understorey of bushes.
            string character = Character(ctx);
            var treeIds = ctx.L("treeSpecies"); var bushIds = ctx.L("bushSpecies");
            if (treeIds.Length == 0 && AssetAvailability.Has(AssetCategories.Tree)) treeIds = Trees(character);
            if (bushIds.Length == 0 && AssetAvailability.Has(AssetCategories.Bush)) bushIds = Bushes(character);
            float ha = region.Area / 10000f;
            int trees = math.min(12000, (int)(ha * ctx.F("treeDensity")));
            int bushes = math.min(12000, (int)(ha * ctx.F("bushDensity")));
            // (nature makeover) groves and glades, saplings at the edges, standing deadwood in the old forest; rock outcrops
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                NatureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, treeIds, 2.1f, character == "heath" ? 40f : 34f, 0xF0Eu, null,
                    character == "heath" ? 0.03f : 0.18f, character == "heath" ? 0f : character == "wildwood" ? 0.07f : 0.035f);
            ctx.Check();
            if (bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, bushIds, Keys.Category(AssetCategories.Bush)))
                NatureKit.Plant(ctx, occ, region, bushes, ObjectRole.Bush, bushIds, 0.9f, character == "heath" ? 18f : 12f, 0xB05u, null, character == "heath" ? 0.12f : 0.1f);
            ctx.Check();
            NatureKit.Rocks(ctx, occ, region, math.clamp((int)math.round(ha * (character == "heath" ? 1.6f : 1f)), 1, 30), 0x40C1u, p =>
            {
                var e = new float2(3f, 0f); var f = new float2(0f, 3f);
                float slope = math.length(new float2(ctx.H(p + e) - ctx.H(p - e), ctx.H(p + f) - ctx.H(p - f))) / 6f;
                return 0.25f + math.saturate(slope * 3f);
            });

            if (ctx.B("animals"))
            {
                int n = math.clamp((int)(ha * 1.5f), 1, 12);
                foreach (var s in Spread(occ, 3f, n, 0x5A3u, 30f))
                    ctx.Object(s, 0f, ObjectRole.AnimalSpawner, new AssetRef("", "Forest Animal Spawner"), Hashing.Mix(ctx.Seed, (uint)(s.x * 3f)));
            }
            // Deadwood: fallen trunks in the wood (not in clearings or on trails), more in the wildwood.
            int logs = character == "wildwood" ? (int)(ha * 6f) : character == "forest" ? (int)(ha * 4f) : (int)(ha * 0.5f);
            NatureKit.Fallen(ctx, occ, region, logs, 0x1065u);
            if (ctx.B("forestFloor"))
            {
                // Woodland floor painted in soft organic patches through the wood (one rectangle inset from the outline
                // read as a hard-edged slab, art pass a3); clearings stay green.
                int patches = math.clamp((int)(ha * 5f), 3, 40);
                for (int i = 0; i < patches; i++)
                {
                    uint v = Hashing.Mix(ctx.Seed, 0xF100u + (uint)i);
                    float r = 8f + 7f * Hashing.Unit(v);
                    var c = region.Min + new float2(Hashing.Unit(Hashing.Mix(v, 1u)), Hashing.Unit(Hashing.Mix(v, 2u))) * (region.Max - region.Min);
                    if (!region.ContainsDisc(c, r + 2f)) continue;
                    bool inClearing = false;
                    foreach (var cl in clearings) if (math.distance(cl.c, c) < cl.r + r * 0.5f) { inClearing = true; break; }
                    if (inClearing) continue;
                    TerrainPaint.Patch(ctx, LeisureKit.Blob(c, r, Hashing.Mix(v, 3u), 14, 0.3f), ctx.I("floorLayer"), character == "heath" ? 0.45f : 0.7f, 6f);
                }
            }

            // Clearings are the regions features use (core = the largest).
            if (clearings.Count > 0)
            {
                clearings.Sort((a, b) => b.r.CompareTo(a.r));
                for (int i = 0; i < clearings.Count; i++)
                    if (i == 0) ctx.AddRegion(LayerRegion.Circle(clearings[i].c, clearings[i].r, 20), "green:0", RegionTags.Green, RegionTags.Core);
                    else ctx.AddRegion(LayerRegion.Circle(clearings[i].c, clearings[i].r, 20), "green:" + i, RegionTags.Green);
            }
            else EmitGreenRegions(ctx, occ);
            CommonStats(ctx);
            ctx.Stat(Keys.StatBushes, Count(ctx.Plan, ObjectRole.Bush), Keys.UnitCount);
            ctx.Plan.Facts["clearings"] = clearings.Count;
        }

        private static List<float2> Spread(Occupancy occ, float radius, int count, uint seed, float minDistance)
        {
            var r = new List<float2>();
            for (int i = 0; i < count; i++)
            {
                if (!Spots.Find(occ, radius, Spots.Preference.Open, Hashing.Mix(seed, (uint)i), out var s, r, minDistance, budget: 1500)) break;
                r.Add(s);
            }
            return r;
        }
    }
}
