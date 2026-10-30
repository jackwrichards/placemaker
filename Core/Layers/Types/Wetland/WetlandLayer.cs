// Wetland (type wave 6a, group "nature"): a network of shallow organic ponds grown between natural
// trails, crossed by zig-zag boardwalks (causeways — see WaterBodies), with islands left wild for
// birds, reed beds massed along the shores (and on the shallow shelf), wet woodland, bird hides on
// the banks or at the end of boardwalk spurs, painted marsh ground and gentle hummocks.
//
// Subtypes (each a preset; the "subtype" control picks the composition, the sliders tune it):
//   wetlandPark  balanced ponds, trail loops, two boardwalks, a hide, benches
//   waterGarden  smaller rounder pools, flower & iris margins, lamps, a viewing terrace
//   reedMarsh    one big ragged marsh, reeds everywhere, a single trail with long boardwalks
//   rainGarden   linear swales along the area (rain-water cells), planted margins, stepping crossings
//   birdReserve  open lake with many islands, hides and a lookout, screen planting, fenced
//
// Composition order: trails → ponds in the space left → crossings → islands (clear of crossings)
// → basins (with causeways) → hides/lookout/terrace → reed belt → trees/bushes → paint/terrain.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Wetland
{
    [AreaGenerator(611, Group = "nature")]
    public sealed class WetlandLayer : LeisureLayer
    {
        public const string LayerId = "wetland";
        public const string WaterPlace = "place:water";
        public const string PermanentKey = "Placemaker.Wetland.Issue.WaterPermanent";
        public static readonly string[] Subtypes = { "wetlandPark", "waterGarden", "reedMarsh", "rainGarden", "birdReserve", "lakeland" };

        protected override string Area => "Wetland";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Water.svg";
        public override float MinArea => 2000f;
        protected override bool HostsStreetShops => false;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, WaterPlace };

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            // Area-wide features on the dry ground ("core"), never in the ponds or a lake the area was drawn over.
            Feature(WildlifeHabitatAddOn.LayerId), CoreFeature("picnic", ("groups", 2.0), ("tables", 2.0)), Feature("restrooms", ("byArea", true), ("haEach", 6.0)),
            KioskFeature(), CoreFeature("rocks", ("clusters", 3.0), ("size", "small")), CoreFeature("decals", ("theme", "puddles"), ("density", 1.0)),
        };

        internal static FeatureInfo KioskFeature()
        {
            var f = Feature("kiosk", ("count", 1.0));
            f.LabelKey = "Placemaker.AddOn.Kiosk.Name";
            return f;
        }

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("subtype", "wetlandPark", ControlTier.Essential, "layout", Subtypes).Tip();
            c.Slider("waterCover", 35, 10, 70, 5, "%", ControlTier.Essential, "layout").Tip();
            c.Slider("ponds", 3, 1, 10, 1, "count", ControlTier.Essential, "layout").Tip();

            c.Slider("depth", 1.2, 0.4, 3, 0.1, "m", ControlTier.Essential, "water").Tip();
            c.Slider("islands", 2, 0, 8, 1, "count", ControlTier.Essential, "water").Tip();
            c.Toggle("shelf", true, ControlTier.More, "water").Tip();

            c.Slider("boardwalks", 2, 0, 6, 1, "count", ControlTier.Essential, "trails").Tip();
            c.Toggle("zigzag", true, ControlTier.More, "trails").Tip();
            c.Slider("benches", 1.5, 0, 5, 0.5, "per100m", ControlTier.More, "trails");
            c.Toggle("terrace", false, ControlTier.More, "trails").Tip();
            c.Asset("trailAsset", AssetCategories.Pathway, ControlTier.More, "trails");
            c.Asset("boardwalkAsset", AssetCategories.Pathway, ControlTier.More, "trails").Tip();

            c.Slider("reeds", 70, 0, 100, 5, "%", ControlTier.Essential, "planting").Tip();
            c.Slider("reedBelt", 4, 1, 10, 0.5, "m", ControlTier.More, "planting").Layout(1).Tip();
            c.Slider("treeDensity", 60, 0, 250, 10, "perHa", ControlTier.More, "planting");
            c.Slider("bushDensity", 80, 0, 300, 10, "perHa", ControlTier.More, "planting");
            c.Toggle("screen", false, ControlTier.More, "planting").Tip();
            c.AssetMulti("reedSpecies", AssetCategories.WetlandReed, ControlTier.More, "planting");
            c.AssetMulti("treeSpecies", AssetCategories.WetlandTree, ControlTier.More, "planting");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "planting");

            c.Slider("hides", 1, 0, 6, 1, "count", ControlTier.Essential, "wildlife").Tip();
            c.Asset("hideAsset", AssetCategories.WetlandHide, ControlTier.More, "wildlife");
            c.Toggle("lookout", false, ControlTier.More, "wildlife").Tip();
            c.Asset("lookoutAsset", AssetCategories.Lookout, ControlTier.More, "wildlife").When("lookout=true");
            c.Toggle("animals", true, ControlTier.More, "wildlife").Tip();

            c.Toggle("marshPaint", true, ControlTier.More, "ground").Tip();
            c.Slider("marshLayer", 1, 1, 4, 1, "count", ControlTier.More, "ground").When("marshPaint=true").Tip();
            c.Slider("hummocks", 1, 0, 3, 0.5, "m", ControlTier.More, "ground").Tip();

            c.Toggle("fence", false, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            EntranceMarkers(c);

            c.RandomRange("waterCover", 20, 50);
            c.RandomRange("depth", 0.6, 2);
            c.Fixed("marshLayer");
            c.Chance("fence", 0.2f);
            c.Chance("lookout", 0.3f);
        }

        protected override void DefinePresets()
        {
            Preset("wetlandPark", ("subtype", "wetlandPark"));
            Preset("waterGarden", ("subtype", "waterGarden"), ("waterCover", 25.0), ("ponds", 5.0), ("depth", 0.8), ("islands", 1.0), ("boardwalks", 3.0),
                ("benches", 3.0), ("terrace", true), ("reeds", 40.0), ("reedBelt", 2.5), ("treeDensity", 40.0), ("bushDensity", 120.0), ("hides", 0.0),
                ("hummocks", 0.5), ("animals", false));
            Preset("reedMarsh", ("subtype", "reedMarsh"), ("waterCover", 55.0), ("ponds", 2.0), ("depth", 0.8), ("islands", 3.0), ("boardwalks", 3.0),
                ("benches", 0.5), ("reeds", 100.0), ("reedBelt", 7.0), ("treeDensity", 20.0), ("bushDensity", 40.0), ("hides", 1.0), ("hummocks", 0.0));
            Preset("rainGarden", ("subtype", "rainGarden"), ("waterCover", 20.0), ("ponds", 3.0), ("depth", 0.5), ("islands", 0.0), ("shelf", false),
                ("boardwalks", 4.0), ("zigzag", false), ("benches", 2.5), ("terrace", true), ("reeds", 50.0), ("reedBelt", 3.0), ("treeDensity", 50.0),
                ("bushDensity", 160.0), ("hides", 0.0), ("hummocks", 0.0), ("animals", false));
            // (nature makeover) Lake country: big clear lakes with rocky, wooded shores (pine, spruce, birch), boulders at the
            // waterline, few reeds, a trail along the banks and one boardwalk across a narrows.
            Preset("lakeland", ("subtype", "lakeland"), ("waterCover", 45.0), ("ponds", 3.0), ("depth", 2.2), ("islands", 2.0), ("boardwalks", 1.0),
                ("benches", 1.0), ("reeds", 22.0), ("reedBelt", 2.0), ("treeDensity", 90.0), ("bushDensity", 50.0), ("hides", 0.0), ("hummocks", 1.5),
                ("animals", true));
            Preset("birdReserve", ("subtype", "birdReserve"), ("waterCover", 45.0), ("ponds", 1.0), ("depth", 1.8), ("islands", 6.0), ("boardwalks", 1.0),
                ("benches", 0.5), ("reeds", 80.0), ("reedBelt", 5.0), ("treeDensity", 90.0), ("screen", true), ("hides", 4.0), ("lookout", true),
                ("fence", true), ("hummocks", 1.5));
        }

        /// <summary>What each subtype does beyond the sliders.</summary>
        private struct Style
        {
            public float Wander, Jitter, BlobScale, PathAmp;
            public bool Linear, Lamps, Flowers, GardenMargin, Lakeland;
            /// <summary>(nature makeover) Depth (m) of the shallow marsh hollows dug in the wet ground round the ponds (0 = none).</summary>
            public float Hollows;
            public int ExtraLoops;
            public float TrailWidth;

            public static Style For(string subtype)
            {
                switch (subtype)
                {
                    case "waterGarden": return new Style { Wander = 0.35f, Jitter = 0.12f, BlobScale = 0.55f, PathAmp = 4f, Lamps = true, Flowers = true, ExtraLoops = 1 };
                    case "reedMarsh": return new Style { Wander = 0.9f, Jitter = 0.32f, BlobScale = 0.8f, PathAmp = 8f, ExtraLoops = -1, Hollows = 0.5f };
                    case "lakeland": return new Style { Wander = 0.55f, Jitter = 0.2f, BlobScale = 1.15f, PathAmp = 6f, Lakeland = true };
                    case "rainGarden": return new Style { Wander = 0.15f, Jitter = 0.15f, BlobScale = 0.3f, PathAmp = 3f, Linear = true, GardenMargin = true, Flowers = true };
                    case "birdReserve": return new Style { Wander = 0.55f, Jitter = 0.26f, BlobScale = 1f, PathAmp = 7f, ExtraLoops = 0, Hollows = 0.35f };
                    default: return new Style { Wander = 0.6f, Jitter = 0.24f, BlobScale = 0.7f, PathAmp = 6f, Hollows = 0.4f };
                }
            }
        }

        protected override void PlanCore(LayerContext ctx)
        {
            // Drawn across an existing lake: build on the dry part only (art pass a3).
            ctx.LayerRegion = Beach.Shoreline.DryLand(ctx, ctx.LayerRegion);
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            string subtype = ctx.S("subtype");
            var style = Style.For(subtype);
            float ha = region.Area / 10000f;

            // 1. Trails first: the water grows in the space they leave.
            int loops = math.max(0, ParkPathPlanner.LoopsByArea(region.Area) + style.ExtraLoops);
            NaturalPaths(ctx, occ, gates, loops, style.PathAmp, 16f, "narrow", AssetMix.One(ctx.S("trailAsset")), region.Area > 25000f);
            ctx.Check();

            // 2. Ponds.
            var water = new WaterBodies();
            float depth = math.max(0.3f, ctx.F("depth"));
            int ponds = math.max(1, ctx.I("ponds"));
            float target = region.Area * ctx.F("waterCover") / 100f;
            float blob = math.clamp(math.sqrt(target / ponds / math.PI) * style.BlobScale, 4f, 28f);
            region.PrincipalAxes(out _, out var axis, out _, out _);
            var spec = new GrowSpec
            {
                Ponds = ponds, TargetArea = target, BlobRadius = blob, MinRadius = style.Linear ? 2.5f : 3.5f,
                EdgeMargin = math.max(6f, 2.5f * (depth + 0.5f)), PathMargin = 3f, PondGap = 5f,
                Wander = style.Wander, Jitter = style.Jitter, Linear = style.Linear, Axis = axis,
            };
            water.Grow(ctx, occ, spec, Hashing.Mix(ctx.Seed, 0x9013u));
            if (water.Ponds.Count == 0)
            {
                // Tight site: accept closer paths and smaller pools before giving up.
                spec.PathMargin = 2f; spec.MinRadius = 2.5f; spec.BlobRadius = math.max(3f, blob * 0.6f); spec.EdgeMargin = math.max(5f, spec.EdgeMargin * 0.8f);
                water.Grow(ctx, occ, spec, Hashing.Mix(ctx.Seed, 0x9014u));
            }
            water.Freeze(region);
            if (water.Ponds.Count == 0) ctx.Issue(IssueSeverity.Info, "Placemaker.Wetland.Issue.NoRoom");
            else if (water.Ponds.Count < ponds) ctx.Issue(IssueSeverity.Info, "Placemaker.Wetland.Issue.FewerPonds", water.Ponds.Count, ponds);
            ctx.Check();

            // 3. Boardwalk crossings (largest ponds first), spurs end on a platform for a hide.
            var boardwalk = AssetMix.One(ctx.S("boardwalkAsset"));
            var crossings = new SegmentIndex(10f);
            var platforms = new List<float2>();
            float boardLength = 0f;
            int wantedCrossings = ctx.I("boardwalks");
            var order = new List<Pond>(water.Ponds);
            order.Sort((a, b) => b.Area.CompareTo(a.Area));
            var crossed = new List<float2>();
            for (int i = 0, k = 0; order.Count > 0 && k < wantedCrossings && i < order.Count + wantedCrossings; i++)
            {
                var pond = order[i % order.Count];
                if (pond.Area < 120f) continue;
                var line = water.Crossing(pond, occ, 30f, ctx.B("zigzag"), Hashing.Mix(ctx.Seed, (uint)(i * 41 + 5)), out bool spur, crossed);
                if (line == null) continue;
                bool clash = false;
                for (int s = 2; s + 1 < line.Count && !clash; s++)
                    if (crossings.Count > 0 && crossings.Overlaps((line[s - 1] + line[s]) * 0.5f, 3f, 1f)) clash = true;
                if (clash) continue;
                // Art pass a3: a real boardwalk over open water instead of a causeway that cut the pond in
                // pieces. Segments over water are FollowTerrain=false at a deck 0.6 m above the pond level
                // (never below the lower bank end); land legs follow the ground, so both meet at the bank
                // vertex at the same height and the walk stays one connected network.
                float deck = pond.Level + 0.6f, bankLow = float.MaxValue;
                foreach (var v in line) if (!water.InWater(v, 0.3f)) bankLow = math.min(bankLow, ctx.H(v));
                if (bankLow < float.MaxValue) deck = math.max(deck, math.min(bankLow, pond.Level + 1.5f));
                float Y(float2 v) => water.InWater(v, 0.3f) ? deck : ctx.H(v);
                for (int s = 1; s < line.Count; s++)
                {
                    var a = line[s - 1]; var bb = line[s];
                    bool overWater = water.InWater(a, 0.3f) || water.InWater(bb, 0.3f) || water.InWater((a + bb) * 0.5f, 0.3f);
                    if (overWater) Boardwalk(ctx, occ, a, Y(a), bb, Y(bb), boardwalk);
                    else LeisureKit.Path(ctx, occ, a, bb, NetRole.Pathway, boardwalk);
                    crossings.Add(a, bb, 1.5f);
                    boardLength += math.distance(a, bb);
                }
                if (spur)
                {
                    // The platform lies just beyond the end of the walk, so what stands on it is off the path.
                    var end = line[line.Count - 1];
                    var fwd = math.normalizesafe(end - line[line.Count - 2], new float2(0, 1));
                    var platform = end + fwd * 4.2f;
                    water.KeepDryDisc(platform, 3.6f);
                    platforms.Add(platform);
                }
                crossed.Add(line[line.Count / 2]);
                k++;
            }
            ctx.Check();

            // 4. Islands, clear of the crossings; then the basins.
            int islands = water.AddIslands(region, ctx.I("islands"), subtype == "birdReserve" ? 5f : 4f, subtype == "birdReserve" ? 9f : 6f, Hashing.Mix(ctx.Seed, 0x15A7u), crossings);
            bool shelf = ctx.B("shelf");
            int basins = water.EmitBasins(ctx, depth, math.min(0.35f, depth * 0.5f), shelf ? math.clamp(ctx.F("reedBelt") * 0.6f, 1.5f, 4f) : 0f);
            if (basins > 0) ctx.Issue(IssueSeverity.Warning, PermanentKey);
            water.EmitPlaces(ctx, WaterPlace);
            ctx.Check();

            // 5. Hides (platforms first, then banks facing islands / open water), lookout, terrace.
            int hides = PlaceHides(ctx, occ, water, platforms, ctx.I("hides"));
            for (int i = 0; i < platforms.Count; i++)
            {
                // Platforms without a hide get a bench looking out over the water.
                var p = platforms[i];
                var from = occ.Paths.Nearest(p, 50f, out var q) >= 0 ? q : p;
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(from, p), ObjectRole.Bench, AssetMix.Pick(WetlandArt.Benches, 0u), null, 1.3f, Hashing.Mix(ctx.Seed, 0xB1A7u + (uint)i), 0f, 0.3f);
            }
            if (ctx.B("lookout")) PlaceLookout(ctx, occ, water);
            if (ctx.B("terrace")) PlaceTerrace(ctx, occ, water);
            BenchesBesidePaths(ctx, occ, water, ctx.F("benches"));
            if (style.Lamps) LampsBesidePaths(ctx, occ, water, 22f);
            ctx.Check();

            // 6. Shore planting: reeds (and flowers / garden margins), then wet woodland.
            int reeds = ShorePlanting(ctx, occ, water, style, shelf);
            ctx.Check();
            if (style.Lakeland) RockyShores(ctx, occ, water);
            PlantLand(ctx, occ, water, style, ha);
            ctx.Check();

            // 7. Ground: marsh paint around the water, hummocks on dry land.
            if (ctx.B("marshPaint")) MarshPaint(ctx, water, ctx.F("reedBelt") + 3f, ctx.I("marshLayer"));
            var hollow = style.Hollows > 0.05f && water.Ponds.Count > 0 ? MarshHollows(ctx, occ, water, style.Hollows) : null;
            float hum = ctx.F("hummocks");
            if (hum > 0.05f)
                TerrainShaper.Shape(ctx, region, new TerrainSpec
                {
                    Mode = TerrainMode.Mounds, Height = hum, Scale = 18f, MaxSlopePct = 20f, Falloff = 14f, Seed = Hashing.Mix(ctx.Seed, 0x4077u),
                    Skip = p => water.InWater(p, 8f) || occ.Corridors.Overlaps(p, 3f) || NearHollow(hollow, p),
                });
            if (ctx.B("animals"))
            {
                int n = math.clamp((int)(ha * 1.5f), 1, 10), placed = 0;
                foreach (var p in Scatter.Candidates(region, 6f, Hashing.Mix(ctx.Seed, 0xA417u), 400, ctx.Cancel))
                {
                    if (placed >= n) break;
                    if (water.InWater(p, 3f) || !occ.Free(p, 2f, 2f, 0.5f)) continue;
                    ctx.Object(p, 0f, ObjectRole.AnimalSpawner, new AssetRef("", "Forest Animal Spawner"), Hashing.Mix(ctx.Seed, (uint)placed + 0xA5u));
                    occ.Take(p, 2f, 5);
                    placed++;
                }
            }
            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(ctx.S("fenceAsset")));

            // 8. Regions for features: dry ground only.
            EmitDryRegions(ctx, occ, water);
            CommonStats(ctx);
            ctx.Stat(Keys.StatLakeArea, System.Math.Round(water.TotalArea), Keys.UnitM2);
            ctx.Stat("Placemaker.Stat.Ponds", water.Ponds.Count, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Islands", islands, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Boardwalk", System.Math.Round(boardLength), Keys.UnitM);
            ctx.Stat("Placemaker.Stat.Hides", hides, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Reeds", reeds, Keys.UnitCount);
            ctx.Plan.Facts["waterArea"] = water.TotalArea;
            ctx.Plan.Facts["ponds"] = water.Ponds.Count;
            ctx.Plan.Facts["islands"] = islands;
            ctx.Plan.Facts["boardwalkLength"] = boardLength;
            ctx.Plan.Facts["hides"] = hides;
            ctx.Plan.Facts["basins"] = basins;
        }

        // ───────────────────────────── structures ─────────────────────────────

        /// <summary>One boardwalk course over water: straight, level or ramping, not following the pond bed.</summary>
        private static void Boardwalk(LayerContext ctx, Occupancy occ, float2 a, float ya, float2 b, float yb, AssetRef asset)
        {
            if (math.distance(a, b) < 1f) return;
            float3 A = new float3(a.x, ya, a.y), D = new float3(b.x, yb, b.y);
            ctx.Plan.Nets.Add(new PlannedNet
            {
                A = A, B = math.lerp(A, D, 1f / 3f), C = math.lerp(A, D, 2f / 3f), D = D,
                Role = NetRole.Pathway, Asset = asset, FollowTerrain = false,
            });
            float hw = Composer.DefaultHalfWidth(NetRole.Pathway);
            occ.TakeCorridor(a, b, hw, 100);
            occ.Paths.Add(a, b, hw, 100);
        }

        private static int PlaceHides(LayerContext ctx, Occupancy occ, WaterBodies water, List<float2> platforms, int wanted)
        {
            if (wanted <= 0) return 0;
            // Never the lookout pool (32-56 m buildings): a timber shelter is the hide when none is installed.
            string cat = LeisureKit.Category(ctx, ctx.S("hideAsset"), AssetCategories.WetlandHide, AssetCategories.Shelter);
            if (cat == null) return 0;
            var asset = AssetMix.Pick(WetlandArt.Or(ctx.S("hideAsset"), WetlandArt.Hides), 0u);
            float r = math.min(3f, AssetMix.FootprintRadius(ctx.Site, asset, 2.2f));
            int placed = 0;
            var done = new List<float2>();
            // On the platform at the end of a boardwalk spur, facing the open water away from the walk.
            foreach (var p in platforms)
            {
                if (placed >= wanted) break;
                var from = occ.Paths.Nearest(p, 50f, out var q) >= 0 ? q : p;
                if (occ.Discs.Overlaps(p, r, 0.2f) || occ.Corridors.Overlaps(p, r, 0.3f)) continue;
                ctx.Object(p, LeisureKit.Yaw(from, p), ObjectRole.Furniture, asset, Hashing.Mix(ctx.Seed, 0x41DEu + (uint)placed), -1, cat);
                occ.Take(p, r, 2);
                done.Add(p); placed++;
            }
            // On the bank: shore points, the hide set back on dry land facing an island or the widest water.
            var shore = water.Shore(4f);
            while (placed < wanted)
            {
                float best = float.MaxValue; float2 at = default, look = default;
                for (int i = 0; i < shore.Count; i++)
                {
                    var (s, outward, island) = shore[i];
                    if (island) continue;
                    var h = s + outward * (r + 1.2f);
                    if (water.InWater(h, r + 0.5f) || !occ.Free(h, r, 1f, 0.5f)) continue;
                    if (!ctx.LayerRegion.ContainsDisc(h, r + 5f)) continue;   // s9: no hide (and spur) squeezed against the outline
                    float spread = float.MaxValue;
                    foreach (var d in done) spread = math.min(spread, math.distance(d, h));
                    if (spread < 35f) continue;
                    float toPath = occ.Paths.Count > 0 ? occ.Paths.Clearance(h, 60f) : 0f;
                    float islandDist = 60f; float2 islandAt = s - outward * 10f;
                    foreach (var pond in water.Ponds)
                        foreach (var isl in pond.Islands)
                        {
                            var ic = Poly.Centroid(isl);
                            float d = math.distance(ic, h);
                            if (d < islandDist) { islandDist = d; islandAt = ic; }
                        }
                    float score = math.abs(toPath - 6f) + islandDist * 0.6f + Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i)) * 2f;
                    if (score < best) { best = score; at = h; look = islandDist < 60f ? islandAt : s - outward * 10f; }
                }
                if (best == float.MaxValue) break;
                ctx.Object(at, LeisureKit.Yaw(at, look), ObjectRole.Furniture, asset, Hashing.Mix(ctx.Seed, 0x41E0u + (uint)placed), -1, cat);
                occ.Take(at, r, 2);
                done.Add(at); placed++;
                // A short spur from the trail to the hide's back.
                if (occ.Paths.Nearest(at, 30f, out var foot) >= 0)
                {
                    var door = at + math.normalizesafe(foot - at) * (r + 0.6f);
                    if (math.distance(door, foot) > 2.5f && !ctx.LayerRegion.CrossesBoundary(door, foot, false) && !CrossesWater(water, door, foot))
                        LeisureKit.Path(ctx, occ, foot, door, NetRole.Pathway, AssetMix.One(ctx.S("trailAsset")));
                }
            }
            if (placed < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Wetland.Issue.FewerHides", placed, wanted);
            return placed;
        }

        private static bool CrossesWater(WaterBodies water, float2 a, float2 b)
        {
            float len = math.distance(a, b);
            for (float t = 0f; t <= len; t += 1f)
                if (water.InWater(math.lerp(a, b, len < 0.01f ? 0f : t / len), 0.5f)) return true;
            return false;
        }

        private static void PlaceLookout(LayerContext ctx, Occupancy occ, WaterBodies water)
        {
            // A picked lookout asset is honoured; automatic is an open gazebo by the water, never the lookout pool.
            bool picked = !string.IsNullOrEmpty(ctx.S("lookoutAsset"));
            string cat = picked ? AssetCategories.Lookout : LeisureKit.Category(ctx, "", AssetCategories.Shelter);
            if (cat == null) return;
            var asset = AssetMix.Pick(WetlandArt.Or(ctx.S("lookoutAsset"), WetlandArt.Lookouts), 0u);
            float r = AssetMix.FootprintRadius(ctx.Site, asset, 4f);
            var anchors = PathAnchors.Beside(occ, r, 8f, 1.2f);
            int best = -1; float score = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                var p = anchors[i].Position;
                if (water.InWater(p, r + 1f)) continue;
                float s = water.Distance(p, 80f) - ctx.H(p) * 0.5f;
                if (s < score) { score = s; best = i; }
            }
            if (best >= 0)
                LeisureKit.Put(ctx, occ, anchors[best].Position, LeisureKit.Yaw(anchors[best].Position, anchors[best].Toward), ObjectRole.Furniture, asset, cat, r,
                    Hashing.Mix(ctx.Seed, 0x100Cu), 0.5f, 0.3f, 0);
        }

        /// <summary>A small paved viewing terrace on the bank with benches facing the water.</summary>
        private static void PlaceTerrace(LayerContext ctx, Occupancy occ, WaterBodies water)
        {
            const float R = 5.5f;
            var anchors = PathAnchors.Beside(occ, R, 10f, 0.4f);
            int best = -1; float score = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                var p = anchors[i].Position;
                if (water.InWater(p, R + 0.5f)) continue;
                float d = water.Distance(p, 60f);
                float s = d + anchors[i].GateDistance * 0.05f;
                if (d < 40f && s < score) { score = s; best = i; }
            }
            if (best < 0) return;
            var c = anchors[best].Position;
            if (!ctx.Surface(LayerRegion.Circle(c, R, 20), SurfaceRole.Plaza, AssetMix.One(""), AssetCategories.SurfacePaving)) return;
            occ.Take(c, R, 6);
            var look = NearestShore(water, c);
            float baseYaw = math.atan2(look.y - c.y, look.x - c.x);
            var bench = AssetMix.One("");
            for (int i = -1; i <= 1; i++)
            {
                float a = baseYaw + i * 0.6f;
                var p = c + new float2(math.cos(a), math.sin(a)) * (R - 1.2f);
                ctx.Object(p, LeisureKit.Yaw(p, look), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, 0x7E2u + (uint)(i + 2)));
            }
            ctx.Plan.Facts["terrace"] = 1;
        }

        private static float2 NearestShore(WaterBodies water, float2 p)
        {
            float best = float.MaxValue; float2 at = p + new float2(0, 10);
            foreach (var pond in water.Ponds)
            {
                var q = Poly.ClosestPointOnBoundary(pond.Ring, p, out _, out _);
                float d = math.distancesq(q, p);
                if (d < best) { best = d; at = q; }
            }
            return at;
        }

        private static void BenchesBesidePaths(LayerContext ctx, Occupancy occ, WaterBodies water, float per100)
        {
            if (per100 <= 0f || occ.Paths.Count == 0) return;
            float interval = 100f / per100, total = 0f;
            foreach (var s in occ.Paths.Segments) total += math.distance(s.A, s.B);
            var anchors = PathAnchors.Beside(occ, 1.3f, 6f, 0.4f);
            anchors.RemoveAll(a => water.InWater(a.Position, 1.5f));
            // Prefer seats with a view over the water.
            anchors.Sort((a, b) => water.Distance(a.Position, 40f).CompareTo(water.Distance(b.Position, 40f)));
            int wanted = math.clamp((int)(total / interval), 0, 150), n = 0;
            var bench = AssetMix.Pick(ctx.S("subtype") == "waterGarden" || ctx.S("subtype") == "rainGarden" ? WetlandArt.GardenBenches : WetlandArt.Benches, 0u);
            foreach (var a in PathAnchors.Pick(anchors, wanted, interval * 0.6f, false))
                if (LeisureKit.Put(ctx, occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Bench, bench, null, 1.3f, Hashing.Mix(ctx.Seed, 0xBE20u + (uint)n))) n++;
        }

        private static void LampsBesidePaths(LayerContext ctx, Occupancy occ, WaterBodies water, float interval)
        {
            int n = 0;
            var lamp = AssetMix.Pick(WetlandArt.Lamps, 0u);
            // Paths are indexed as short pieces (~2 m): spacing is measured across pieces and against the lamps
            // already placed, otherwise every piece got its own lamp (a lamp every 2 m, art pass a3).
            var placed = new List<float2>();
            float carry = interval * 0.5f;
            foreach (var s in new List<SegmentIndex.Seg>(occ.Paths.Segments))
            {
                var dir = math.normalizesafe(s.B - s.A);
                var nrm = new float2(-dir.y, dir.x);
                float len = math.distance(s.A, s.B);
                for (float t = interval - carry; t <= len; t += interval)
                {
                    var p = s.A + dir * t;
                    var q = p + nrm * ((n & 1) == 0 ? 1f : -1f) * (s.HalfWidth + 0.8f);
                    bool near = false;
                    foreach (var o in placed) if (math.distancesq(o, q) < interval * interval * 0.5f) { near = true; break; }
                    if (near || water.InWater(q, 0.8f)) continue;
                    if (LeisureKit.Put(ctx, occ, q, 0f, ObjectRole.Lamp, lamp, null, 0.5f, Hashing.Mix(ctx.Seed, 0x1A00u + (uint)n), 0.4f, 0.2f)) { n++; placed.Add(q); }
                }
                carry = (carry + len) % interval;
            }
        }

        // ───────────────────────────── planting ─────────────────────────────

        /// <summary>
        /// Reed beds massed in clumps along every shore — from the shallow shelf (in the water) up the bank
        /// — and fringes around the islands. Water / rain gardens mix in flowers and garden shrubs.
        /// </summary>
        private static int ShorePlanting(LayerContext ctx, Occupancy occ, WaterBodies water, Style style, bool shelf)
        {
            float density = ctx.F("reeds") / 100f;
            if (density <= 0f || water.Ponds.Count == 0) return 0;
            var reedIds = WetlandArt.Or(ctx.L("reedSpecies"), WetlandArt.Reeds, AssetCategories.Bush);
            var bushIds = WetlandArt.Or(ctx.L("bushSpecies"), WetlandArt.GardenShrubs, AssetCategories.Bush);
            string reedCat = LeisureKit.Category(ctx, reedIds.Length > 0 ? "x" : "", AssetCategories.WetlandReed, AssetCategories.Bush);
            string flowerCat = style.Flowers && AssetAvailability.Has(AssetCategories.Flowerbed) ? AssetCategories.Flowerbed : null;
            if (reedCat == null && flowerCat == null) return 0;
            var region = ctx.LayerRegion;
            float belt = ctx.F("reedBelt");
            float inWater = shelf ? math.min(3f, belt * 0.5f) : 0.8f;
            uint clump = Hashing.Mix(ctx.Seed, 0x4EEDu);
            int placed = 0;
            const float pitch = 1.5f;
            foreach (var (s, outward, island) in water.Shore(pitch))
            {
                if (placed >= 6000) break;
                float lo = island ? -1.2f : -inWater, hi = island ? 0.8f : belt;
                for (float o = lo; o <= hi; o += pitch)
                {
                    uint h = Hashing.Mix(clump, (int)(s.x * 7f + o * 13f), (int)(s.y * 7f));
                    var p = s + outward * o + new float2(Hashing.Unit(h) - 0.5f, Hashing.Unit(Hashing.Mix(h)) - 0.5f) * 0.6f;
                    // Thicker at the waterline, thinning up the bank.
                    float edge = 1f - math.saturate(math.max(0f, o) / math.max(1f, hi + 0.5f)) * 0.6f;
                    float chance = density * edge * Scatter.Clustering(p, 9f, clump, 0.25f);
                    if (Hashing.Unit(Hashing.Mix(h, 3u)) > chance) continue;
                    if (!region.ContainsDisc(p, 1f) || !occ.Free(p, 0.8f, 1f, 0.3f)) continue;
                    if (!island && o > 0.5f && water.InWater(p, 0f) && !water.OnIsland(p)) continue;
                    uint v = Hashing.Mix(ctx.Seed, (uint)placed * 2654435761u + 0x4E1u);
                    bool flower = flowerCat != null && Hashing.Unit(Hashing.Mix(v, 9u)) < (style.GardenMargin ? 0.45f : 0.3f) && !water.InWater(p);
                    bool shrub = style.GardenMargin && (bushIds.Length > 0 || AssetAvailability.Has(AssetCategories.Bush)) && !flower && o > 1f && Hashing.Unit(Hashing.Mix(v, 11u)) < 0.4f;
                    if (flower) ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Planter, AssetMix.Pick(WetlandArt.Flowers, v), v, -1, flowerCat);
                    else if (shrub) ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Bush, AssetMix.Pick(bushIds, v), v);
                    else if (reedCat != null) ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Bush, AssetMix.Pick(reedIds, v), v, -1, reedCat);
                    else continue;
                    occ.Take(p, 0.8f, 4);
                    placed++;
                }
            }
            return placed;
        }

        /// <summary>Wet woodland (denser near the water), understorey, a screen belt on the outline, island trees.</summary>
        private static void PlantLand(LayerContext ctx, Occupancy occ, WaterBodies water, Style style, float ha)
        {
            var region = ctx.LayerRegion;
            var treeIds = WetlandArt.Or(ctx.L("treeSpecies"), style.Lakeland ? WetlandArt.LakeTrees : WetlandArt.Trees, AssetCategories.WetlandTree);
            var bushIds = WetlandArt.Or(ctx.L("bushSpecies"), WetlandArt.Bushes, AssetCategories.Bush);
            string treeCat = LeisureKit.Category(ctx, treeIds.Length > 0 ? "x" : "", AssetCategories.WetlandTree, AssetCategories.Tree);
            uint salt = Hashing.Mix(ctx.Seed, 0x7EE5u);
            bool Reject(float2 p)
            {
                if (water.InWater(p, 2f)) return true;
                // Carr woodland gathers at the margins: half the trees far from water are dropped.
                return water.Distance(p, 40f) > 25f && Hashing.Unit(Hashing.Mix(salt, (int)p.x, (int)p.y)) < 0.5f;
            }
            if (treeCat != null)
            {
                int trees = math.min(8000, (int)(ha * ctx.F("treeDensity")));
                if (style.Lakeland)   // (nature makeover) lake country: groves down to the rocky shore, glades between
                    NatureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, treeIds, 2.2f, 30f, 0x7E1u, p => water.InWater(p, 1.5f), 0.12f, 0.02f, treeCat == AssetCategories.Tree ? null : treeCat);
                else LeisureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, treeIds, 2.2f, 22f, 0.3f, 0x7E1u, Reject, 1, 3,
                    treeCat == AssetCategories.Tree ? null : treeCat);
                if (ctx.B("screen"))
                {
                    int screen = math.min(4000, (int)(region.Perimeter * 10f / 30f));
                    LeisureKit.Plant(ctx, occ, region, screen, ObjectRole.Tree, treeIds, 2.2f, 30f, 0.7f, 0x5C2u,
                        p => region.BoundaryDistance(p, 14f) > 12f || water.InWater(p, 2f), 2, 3, treeCat == AssetCategories.Tree ? null : treeCat);
                }
                // One tree on each island big enough, bushes round it.
                int k = 0;
                foreach (var pond in water.Ponds)
                    foreach (var isl in pond.Islands)
                    {
                        var lr = LayerRegion.From(isl);
                        if (lr == null) continue;
                        var c = lr.DeepestPoint(out float ir, null, 10);
                        uint v = Hashing.Mix(ctx.Seed, 0x1514u + (uint)k++);
                        if (ir >= 3f && occ.Free(c, 1.5f, 0f, 0.2f))
                        {
                            ctx.Object(c, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), v, 2, treeCat == AssetCategories.Tree ? null : treeCat);
                            occ.Take(c, 1.5f, 3);
                        }
                        for (int b = 0; b < 4 && (bushIds.Length > 0 || AssetAvailability.Has(AssetCategories.Bush)); b++)
                        {
                            float a = b * 1.57f + Hashing.Unit(v) * 3f;
                            var p = c + new float2(math.cos(a), math.sin(a)) * math.max(1.2f, ir * 0.55f);
                            if (!Poly.Contains(isl, p) || !occ.Free(p, 0.8f, 0f, 0.1f)) continue;
                            ctx.Object(p, a, ObjectRole.Bush, AssetMix.Pick(bushIds, Hashing.Mix(v, (uint)b)), Hashing.Mix(v, (uint)b));
                            occ.Take(p, 0.8f, 4);
                        }
                    }
            }
            int bushes = math.min(8000, (int)(ha * ctx.F("bushDensity")));
            if (bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, bushIds, Keys.Category(AssetCategories.Bush)))
            {
                if (style.Lakeland) NatureKit.Plant(ctx, occ, region, bushes, ObjectRole.Bush, bushIds, 0.9f, 14f, 0xB05u, p => water.InWater(p, 1f), 0.08f);
                else LeisureKit.Plant(ctx, occ, region, bushes, ObjectRole.Bush, bushIds, 0.9f, 10f, 0.3f, 0xB05u, p => water.InWater(p, 1f));
            }
        }

        /// <summary>Within a hummock cell (12 m) of a marsh hollow: the two terraforms never overlap.</summary>
        private static bool NearHollow(System.Func<float2, float> hollow, float2 p)
        {
            if (hollow == null) return false;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (hollow(p + new float2(dx, dy) * 8f) > 0.005f) return true;
            return false;
        }

        /// <summary>
        /// (nature makeover) Shallow marsh hollows in the wet ground round the ponds: organic dips 25-60 cm deep, kept off the
        /// trails, boardwalks and the ponds' own banks, so the designed ground (Decals/Dynamic/NatureGround) finds real hollows
        /// to fill with marsh mud and sedge even on flat land. Sloped terraform (Common/Sculpt); what stands in them is put back
        /// onto the new ground. Returns the depth field (null when nothing was dug).
        /// </summary>
        private static System.Func<float2, float> MarshHollows(LayerContext ctx, Occupancy occ, WaterBodies water, float depth)
        {
            var region = ctx.LayerRegion;
            float belt = ctx.F("reedBelt");
            var centres = new List<(float2 c, float r, float d)>();
            int want = math.clamp((int)(region.Area / 1600f), 2, 40);
            foreach (var p in Scatter.Candidates(region, 5f, Hashing.Mix(ctx.Seed, 0x4011u), 6000, ctx.Cancel, true))
            {
                if (centres.Count >= want) break;
                float dw = water.Distance(p, 40f);
                if (dw < 5f || dw > belt + 18f || water.InWater(p, 5f)) continue;
                uint v = Hashing.Mix(ctx.Seed, 0x4012u + (uint)centres.Count * 977u + (uint)(p.x * 3f));
                float r = 5f + 6f * Hashing.Unit(v);
                if (occ.Corridors.Overlaps(p, r + 2.5f, 0f) || !region.ContainsDisc(p, r + 4f)) continue;
                bool far = true;
                foreach (var o in centres) if (math.distance(o.c, p) < (o.r + r) * 0.9f) { far = false; break; }
                if (!far) continue;
                centres.Add((p, r, depth * (0.6f + 0.5f * Hashing.Unit(Hashing.Mix(v, 1u)))));
            }
            if (centres.Count == 0) return null;
            uint ns = Hashing.Mix(ctx.Seed, 0x4013u);
            System.Func<float2, float> field = p =>
            {
                float best = 0f;
                foreach (var (c, r, d) in centres)
                {
                    float rr = r * (0.75f + 0.5f * ValueNoise.Clumps(p, 6f, ns));
                    float t = math.distance(p, c) / rr;
                    if (t < 1f) best = math.max(best, d * (1f - t * t) * (1f - t * t));
                }
                return best;
            };
            var sc = Sculpt.Over(ctx, region, null, 3f, p => water.InWater(p, 4f) || occ.Corridors.Overlaps(p, 1.5f, 0f));
            sc.Shape((p, h0, cur) => { float d = field(p); return d > 0.01f ? h0 - d : float.NaN; });
            sc.Smooth(1);
            sc.LimitGrade(0.2f);
            var res = sc.Emit(ctx, new[] { region.Ring }, 0.05f);
            if (res == null || res.Cells == 0) return null;
            // what already stands in a hollow sits on the new ground
            foreach (var o in ctx.Plan.Objects) if (field(o.Position.xz) > 0.01f) o.Position = ctx.P3(o.Position.xz);
            ctx.Plan.Facts["marshHollows"] = centres.Count;
            return field;
        }

        /// <summary>
        /// (nature makeover, lake country) Rocky shores: boulders set at the waterline here and there along the banks, half in
        /// the water, small stones spilled round them.
        /// </summary>
        private static void RockyShores(LayerContext ctx, Occupancy occ, WaterBodies water)
        {
            if (!AssetAvailability.Has(AssetCategories.Rock)) return;
            var region = ctx.LayerRegion;
            int k = 0;
            foreach (var (s, outward, island) in water.Shore(3f))
            {
                uint v = Hashing.Mix(ctx.Seed, 0x5C0Bu + (uint)k++);
                if (Hashing.Unit(v) > (island ? 0.3f : 0.16f)) continue;
                var p = s + outward * (0.4f + Hashing.Unit(Hashing.Mix(v, 1u)) * 1.2f);
                float sc = 0.45f + 0.7f * Hashing.Unit(Hashing.Mix(v, 2u));
                if (!region.ContainsDisc(p, 3f) || !occ.Free(p, 2.2f * sc, 0.5f, 0.3f)) continue;
                var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(NatureKit.Boulders, v), v, -1, AssetCategories.Rock);
                o.Scale = sc;
                o.Elevation = -0.4f * sc;
                occ.Take(p, 2.2f * sc, 5);
                for (int i = 0; i < 4; i++)
                {
                    uint w = Hashing.Mix(v, (uint)(20 + i));
                    var q = p + outward * (Hashing.Unit(w) * 3f - 1f) + new float2(-outward.y, outward.x) * (Hashing.Unit(Hashing.Mix(w, 1u)) - 0.5f) * 5f;
                    if (!region.ContainsDisc(q, 1f) || !occ.Free(q, 0.5f, 0.3f, 0.2f)) continue;
                    var st = ctx.Object(q, Hashing.Unit(Hashing.Mix(w, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(NatureKit.Stones, w), w, -1, AssetCategories.Rock);
                    st.Scale = 0.6f + 0.8f * Hashing.Unit(Hashing.Mix(w, 3u));
                    occ.Take(q, 0.5f, 5);
                }
            }
        }

        private static void MarshPaint(LayerContext ctx, WaterBodies water, float reach, int layer)
        {
            var region = ctx.LayerRegion;
            foreach (var pond in water.Ponds)
            {
                List<float2[]> grown;
                try { grown = Poly.Offset(pond.Ring, -reach); } catch (System.Exception) { continue; }
                if (grown.Count == 0) continue;
                var clipped = LeisureKit.Intersect(region, grown[0]);
                if (clipped != null) TerrainPaint.Patch(ctx, clipped.Ring, layer, 0.8f, 3f);
            }
        }

        private static void EmitDryRegions(LayerContext ctx, Occupancy occ, WaterBodies water)
        {
            var region = ctx.LayerRegion;
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f) || water.InWater(p, 1.5f), 2.5f, 150f, ctx.Cancel);
            if (pieces.Count == 0) { ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core); return; }
            for (int i = 0; i < pieces.Count && i < 24; i++)
                if (i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);
        }
    }
}
