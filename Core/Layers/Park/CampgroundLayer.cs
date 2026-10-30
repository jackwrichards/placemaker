// Campground layer (Fill): a loop of narrow paths with pitches along it — each pitch a gravel
// pad with a tent/caravan, campfire and optional table — restrooms near the paths, and woodland
// between the pitches. Reuses the park path planner (ported from ParkManager, tomkpunkt,
// GPL-3.0) and the shared scatter sampler (from ParkingLotTool, KruemelGames, GPL-3.0).
//
// Regions: green:N (open ground between pitches, largest also {core}), like the park.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    [AreaGenerator(40, Group = "nature")]
    public sealed class CampgroundLayer : LayerBase, IHasFeatures
    {
        public const string LayerId = "campground";

        protected override string Area => "Campground";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Forest.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag };
        protected override bool HostsStreetShops => true;
        public override float MinArea => 600f;

        private static readonly FeatureInfo[] FeatureList =
        {
            new FeatureInfo { Layer = "restrooms", LabelKey = "Placemaker.Campground.Feature.restrooms", TooltipKey = "Placemaker.Campground.Feature.restrooms.Tooltip", DefaultOn = true },
            new FeatureInfo { Layer = "playground", Target = RegionTags.Core, LabelKey = "Placemaker.Campground.Feature.playground", Preset = ParkLayer.Bag(("size", "small")) },
            // Not on by default: pitches already have tables, and the core clearing goes to the playground.
            new FeatureInfo { Layer = "picnic", Target = RegionTags.Core, LabelKey = "Placemaker.Campground.Feature.picnic", Preset = ParkLayer.Bag(("groups", 2.0), ("shelter", true)) },
            new FeatureInfo { Layer = "shops", LabelKey = "Placemaker.Feature.shops", TooltipKey = "Placemaker.Feature.shops.Tooltip", Preset = ParkLayer.Bag(("count", 1.0), ("where", "entrances")) },
            StreetShopsFeature("Campground", "campground"),
            new FeatureInfo { Layer = "rocks", LabelKey = "Placemaker.Feature.rocks" },
            new FeatureInfo { Layer = "decals", LabelKey = "Placemaker.Feature.decals", Preset = ParkLayer.Bag(("theme", "leaves"), ("density", 0.4)), DefaultOn = true },
            new FeatureInfo { Layer = "terrain", LabelKey = "Placemaker.Feature.terrain", TooltipKey = "Placemaker.Feature.terrain.Tooltip", Preset = ParkLayer.Bag(("shape", "mounds"), ("height", 1.5)) },
        };

        public IReadOnlyList<FeatureInfo> Features => FeatureList;

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("pitchSize", 10, 7, 16, 0.5, "m", ControlTier.Essential, "pitches").Tip();
            c.AssetMulti("pitchAssets", AssetCategories.Camping, ControlTier.Essential, "pitches");
            c.Slider("treeDensity", 60, 0, 200, 5, "perHa", ControlTier.Essential, "planting");
            c.Toggle("facilities", true, ControlTier.Essential, "facilities").Tip();
            c.Toggle("campfires", true, ControlTier.More, "pitches");
            c.Asset("campfireAsset", AssetCategories.Camping, ControlTier.More, "pitches").When("campfires=true");
            c.Toggle("tables", true, ControlTier.More, "pitches");
            c.Asset("tableAsset", AssetCategories.Picnic, ControlTier.More, "pitches").When("tables=true");
            c.Toggle("pads", true, ControlTier.More, "pitches");
            c.Asset("padSurface", AssetCategories.SurfaceNatural, ControlTier.More, "pitches").When("pads=true");
            c.Choice("pathWidth", "narrow", ControlTier.More, "layout", "narrow", "wide");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "layout");
            EntranceKinds.WithOptions(c.Markers("entrances", ControlTier.More, EntranceKinds.Leisure), EntranceKinds.Leisure);
            c.Asset("restroomAsset", AssetCategories.Restroom, ControlTier.More, "facilities").When("facilities=true");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");
            c.Toggle("groundSurface", false, ControlTier.More, "ground");
            c.Asset("surface", AssetCategories.SurfaceNatural, ControlTier.More, "ground").When("groundSurface=true");
            c.Toggle("fence", false, ControlTier.More, "boundary");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.RandomRange("pitchSize", 8, 13);
            c.RandomRange("treeDensity", 30, 120);
            c.Fixed("facilities");
            DefineStreetShopControls(c);
        }

        protected override void DefinePresets()
        {
            Preset("forest", ("treeDensity", 110.0), ("pitchSize", 9.0), ("pads", true));
            // Art pass (a1): a meadow camp is open grass — tents straight on the lawn, no pads.
            Preset("meadow", ("treeDensity", 20.0), ("pitchSize", 12.0), ("tables", true), ("pads", false));
            // Caravans (travel trailers when the player has them; tents otherwise) on gravel hardstandings.
            Preset("caravan", ("treeDensity", 35.0), ("pitchSize", 14.0), ("pathWidth", "wide"), ("campfires", false),
                ("pitchAssets", new[] { "Prop_CamperTrailer01" }), ("padSurface", CampArt.Hardstanding));
            PresetFeatures("forest", "-playground");
        }

        public override List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            var region = LayerRegion.From(input?.Shape?.Ring);
            return region == null ? new List<EdgeMarker>() : Entrances.SuggestTyped(input, region, 1, EntranceKinds.Leisure);
        }

        /// <summary>Playtest 4b: the player places the entrance (suggestions are hints only).</summary>
        public override Readiness CheckReady(AreaInput input) => Entrances.RequireTyped(input, EntranceKinds.Leisure);

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            // One entrance is typical for a campground (a reception point); more only if the player places them.
            var gates = Entrances.ResolveTyped(ctx.Input, region, 1, EntranceKinds.Leisure);
            var gatePoints = new List<float2>();
            foreach (var g in gates) gatePoints.Add(g.Position);
            var graph = ParkPathPlanner.PlanBest(region, gatePoints,
                new ParkPathPlanner.Options { Loops = math.max(1, ParkPathPlanner.LoopsByArea(region.Area)), StretchLoop = region.Area > 20000f },
                Hashing.Mix(ctx.Seed, 0xCA4Fu), ctx.Cancel);
            var curves = ParkPaths.Build(graph, region, true, ctx.S("pathWidth") == "wide" ? "wide" : "narrow");
            foreach (var cv in curves)   // a main entrance gets a wide drive-in walk
                if (cv.GateA >= 0 && gates[cv.GateA].Kind == EntranceKinds.Main || cv.GateD >= 0 && gates[cv.GateD].Kind == EntranceKinds.Main)
                { cv.Role = NetRole.PathwayWide; cv.HalfWidth = Composer.DefaultHalfWidth(NetRole.PathwayWide); }
            ParkPaths.Index(curves, occ.Corridors, 100);
            ctx.Check();

            // Pitches along the paths.
            var pitchIds = ctx.L("pitchAssets");
            bool canPitch = AssetAvailability.Require(ctx.Plan, AssetCategories.Camping, pitchIds, Keys.Category(AssetCategories.Camping));
            if (pitchIds.Length == 0) pitchIds = CampArt.Tents;
            float size = ctx.F("pitchSize");
            var pitches = new List<(float2 c, float2 t, float side)>();
            if (canPitch)
                foreach (var cv in curves)
                {
                    // Art pass (a1): breathing room between pitches (trees grow in the gaps) and the two sides
                    // staggered by half a step, so the loop reads as a campsite rather than a row of plots.
                    // (nature makeover) no even necklace: the gap to the next pitch, the set-back from the path and the odd
                    // missing pitch vary, the way a campsite fills a wood
                    float step = size + math.max(2.5f, size * 0.45f);
                    int pi = 0;
                    for (float along = 4f + size * 0.5f; along < cv.Length - 4f - size * 0.5f; along += step * (0.8f + 0.9f * Hashing.Unit(Hashing.Mix(ctx.Seed, 0x9170 + pi, (int)cv.Length))))
                        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                        {
                            uint pv = Hashing.Mix(ctx.Seed, 0x9171 + pi++, sideIndex);
                            if (Hashing.Unit(pv) < 0.18f) continue;
                            float at = along + (sideIndex == 1 ? step * (0.3f + 0.4f * Hashing.Unit(Hashing.Mix(pv, 1u))) : 0f);
                            if (at > cv.Length - 4f - size * 0.5f) continue;
                            var p = cv.At(at, out var t);
                            var n = new float2(-t.y, t.x);
                            float side = sideIndex == 0 ? 1f : -1f;
                            var c = p + n * side * (cv.HalfWidth + 0.8f + size * 0.5f + 3.5f * Hashing.Unit(Hashing.Mix(pv, 2u)));
                            if (!PitchFits(region, occ, c, t, size, gatePoints)) continue;
                            pitches.Add((c, t, side));
                            occ.Take(c, size * 0.5f, 1);
                        }
                }
            ctx.Check();

            // Facilities: one restroom per 25 pitches near the paths, plus one near the entrance.
            int restrooms = 0;
            // The restrooms feature replaces the built-in restroom block.
            if (ctx.B("facilities") && !ctx.Input.HasChild("restrooms") && pitches.Count > 0 &&
                AssetAvailability.Require(ctx.Plan, AssetCategories.Restroom, new[] { ctx.S("restroomAsset") }, Keys.Category(AssetCategories.Restroom)))
            {
                var asset = AssetMix.One(ctx.S("restroomAsset"), ParkArt.Toilets);
                float r = AssetMix.FootprintRadius(ctx.Site, asset, 4f);
                int wanted = math.max(1, (pitches.Count + 24) / 25);
                for (int i = 0; i < wanted; i++)
                {
                    if (!Spots.Find(occ, r, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, (uint)i + 50u), out var spot, null, 0f, 1f)) break;
                    var toward = occ.Corridors.Nearest(spot, 300f, out var q) >= 0 ? q : region.Centroid;
                    ctx.Object(spot, math.atan2(toward.x - spot.x, toward.y - spot.y), ObjectRole.Furniture, asset, Hashing.Mix(ctx.Seed, (uint)i), -1, AssetCategories.Restroom);
                    occ.Take(spot, r, 2);
                    restrooms++;
                }
            }

            // Pitch contents.
            // (nature makeover) A pitch is a worn clearing, not a square plot: an organic pad of forest floor, the tent turned
            // the way its owner liked, a fire ring of stones with ash in it (no vanilla campfire prop: the stones and the ash
            // decal are the fire; a picked campfire asset stands in the ring), two short logs to sit on, the table nearby.
            string pickedFire = ctx.S("campfireAsset");
            var table = AssetMix.One(ctx.S("tableAsset"), CampArt.Tables);
            bool fires = ctx.B("campfires");
            bool rockOk = AssetAvailability.Has(AssetCategories.Rock);
            bool tables = ctx.B("tables") && (ctx.S("tableAsset") != "" || AssetAvailability.Has(AssetCategories.Picnic));
            for (int i = 0; i < pitches.Count; i++)
            {
                var (c, t, side) = pitches[i];
                var toPath = new float2(t.y, -t.x) * side;   // unit vector from the pitch towards its path
                uint v = Hashing.Mix(ctx.Seed, (uint)i + 7u);
                if (ctx.B("pads")) ctx.Surface(LeisureKit.Blob(c, size * 0.46f, Hashing.Mix(v, 9u), 12, 0.2f), SurfaceRole.Ground, AssetMix.One(ctx.S("padSurface"), CampArt.Pad), AssetCategories.SurfaceNatural);
                float jitter = (Hashing.Unit(Hashing.Mix(v, 4u)) - 0.5f) * 0.9f;
                var tent = c - toPath * size * 0.17f + t * size * 0.08f * (Hashing.Unit(Hashing.Mix(v, 5u)) - 0.5f);
                ctx.Object(tent, math.atan2(toPath.x, toPath.y) + jitter, ObjectRole.Furniture, AssetMix.Pick(pitchIds, v), v, -1, AssetCategories.CampingPitch);
                // a second small tent on the bigger pitches now and then (a family)
                if (size >= 9f && Hashing.Unit(Hashing.Mix(v, 6u)) < 0.3f)
                    ctx.Object(tent - t * size * 0.3f * (Hashing.Unit(Hashing.Mix(v, 7u)) < 0.5f ? 1f : -1f), math.atan2(toPath.x, toPath.y) + jitter + 0.5f, ObjectRole.Furniture, AssetMix.Pick(pitchIds, Hashing.Mix(v, 8u)), Hashing.Mix(v, 8u), -1, AssetCategories.CampingPitch);
                var fireAt = c + toPath * size * 0.26f + t * size * 0.24f;
                if (fires) FireRing(ctx, fireAt, 0.85f, 7, pickedFire, rockOk, Hashing.Mix(v, 1u), 2, math.atan2(t.x, t.y));
                if (tables) ctx.Object(c + toPath * size * 0.25f - t * size * 0.25f, math.atan2(t.x, t.y) + jitter * 0.5f, ObjectRole.Furniture, table, Hashing.Mix(v, 2u), -1, AssetCategories.Picnic);
            }
            ctx.Check();

            // Art pass (a1): a reception point — an info sign at the entrance and a camp shelter just inside.
            if (gates.Count > 0)
            {
                var g = gates[0].Position;
                region.ClosestOnBoundary(g, out int gs, out float gt);
                region.PointAt(region.AlongOf(gs, gt), out var tan, out var inward, out _);
                float hwGate = Composer.DefaultHalfWidth(ctx.S("pathWidth") == "wide" ? NetRole.PathwayWide : NetRole.Pathway);
                var sign = g + inward * 2.5f + tan * (hwGate + 1.4f);
                if (region.ContainsDisc(sign, 0.8f) && occ.Free(sign, 0.8f, 0.5f, 0.2f))
                {
                    ctx.Object(sign, math.atan2(-inward.x, -inward.y), ObjectRole.Furniture, AssetMix.One("", CampArt.Sign), Hashing.Mix(ctx.Seed, 0x5160u), -1, AssetCategories.TrailSign);
                    occ.Take(sign, 0.8f, 2);
                }
                var hut = g + inward * 9f - tan * (hwGate + 4.5f);
                if (region.ContainsDisc(hut, 3.5f) && occ.Free(hut, 3.2f, 1f, 0.3f))
                {
                    ctx.Object(hut, math.atan2(tan.x, tan.y), ObjectRole.Furniture, AssetMix.One("", CampArt.Shelter), Hashing.Mix(ctx.Seed, 0x5161u), -1, AssetCategories.Shelter);
                    occ.Take(hut, 3.2f, 2);
                    // (nature makeover) the woodstore beside it, where campers fetch their firewood
                    var wood = hut + tan * 5.5f + inward * 1.5f;
                    if (region.ContainsDisc(wood, 2.2f) && occ.Free(wood, 2f, 0.5f, 0.3f))
                    {
                        ctx.Object(wood, math.atan2(-inward.x, -inward.y), ObjectRole.Furniture, AssetMix.One("", CampArt.Woodstore), Hashing.Mix(ctx.Seed, 0x5162u), -1, AssetCategories.Shelter);
                        occ.Take(wood, 2f, 2);
                    }
                }
            }

            // Open ground between pitches (regions for children) and a clearing on the largest piece that
            // the woodland leaves free, so a picnic area or playground add-on has room in a dense forest camp.
            var lawns = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || NearPitch(pitches, p, size), 2f, 150f, ctx.Cancel);
            float2 clearing = float2.zero; float clearingRadius = 0f;
            var largest = lawns.Count > 0 ? LayerRegion.From(lawns[0]) : null;
            if (largest != null)
            {
                clearing = largest.DeepestPoint(out float inscribed, (q, cap) => occ.Clearance(q, cap), 16);
                clearingRadius = math.min(inscribed - 1f, 16f);
            }

            // (nature makeover) The camp fire circle: the heart of the camp in its clearing, a big ring of stones with logs round
            // it to sit on (when no playground has the clearing).
            if (fires && clearingRadius >= 7f && !ctx.Input.HasChild("playground"))
            {
                FireRing(ctx, clearing, 1.5f, 11, pickedFire, rockOk, Hashing.Mix(ctx.Seed, 0xF1EEu), 0, 0f);
                for (int k = 0; k < 5; k++)
                {
                    float a = k * 1.2566f + 0.3f;
                    var dir = new float2(math.cos(a), math.sin(a));
                    var lp = clearing + dir * 4.2f;
                    ctx.Object(lp, math.atan2(-dir.x, -dir.y), ObjectRole.Bench, AssetMix.Pick(CampArt.FireSeats, (uint)k), Hashing.Mix(ctx.Seed, 0xF1F0u + (uint)k), -1, AssetCategories.Bench);   // facing the fire
                }
                occ.Take(clearing, 5.5f, 2);
            }

            // Woodland between the pitches: groves with sunlit gaps, young trees at the edges, a few boulders.
            var treeIds = ctx.L("treeSpecies");
            var plantIds = treeIds.Length > 0 ? treeIds : ctx.F("treeDensity") >= 80f ? CampArt.ForestTrees : CampArt.Trees;
            int trees = (int)math.round(region.Area / 10000f * ctx.F("treeDensity"));
            int placedTrees = 0;
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                placedTrees = NatureKit.Plant(ctx, occ, region, trees, ObjectRole.Tree, plantIds, 2.4f, 26f, 0x7EEu,
                    p => clearingRadius >= 6f && math.distance(p, clearing) < clearingRadius + 2.5f, 0.25f, 0.015f);
            NatureKit.Rocks(ctx, occ, region, math.clamp((int)math.round(region.Area / 10000f * 0.8f), 1, 12), 0x40C3u, p => occ.Corridors.Overlaps(p, 4f) ? 0.05f : 0.5f, null, 0.8f);

            if (ctx.B("groundSurface")) ctx.Surface(region.Ring, SurfaceRole.Ground, AssetMix.One(ctx.S("surface")));
            float pathLength = ParkPaths.Emit(ctx, curves, gates, AssetMix.One(ctx.S("pathAsset")));
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(region, gates, 3.75f)) ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }

            for (int i = 0; i < lawns.Count; i++)
                if (i == 0) ctx.AddRegion(lawns[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(lawns[i], "green:" + i, RegionTags.Green);

            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat(Keys.StatPitches, pitches.Count, Keys.UnitCount);
            ctx.Stat(Keys.StatPathLength, Math.Round(pathLength), Keys.UnitM);
            ctx.Stat(Keys.StatTrees, placedTrees, Keys.UnitCount);
            ctx.Stat(Keys.StatRestrooms, restrooms, Keys.UnitCount);
            if (pitches.Count == 0 && canPitch) ctx.Issue(IssueSeverity.Warning, "Placemaker.Campground.Issue.NoPitches");
        }

        /// <summary>The tag of a fire ring's little region: the designed ground lays its ash and the trodden ground round it.</summary>
        public const string FireTag = "place:fire";

        /// <summary>
        /// (nature makeover) A fire ring: small stones (Rock01-30) set in a circle, half in the ground; a picked campfire asset
        /// in the middle; <paramref name="logs"/> short logs to sit on facing it; its spot as a "place:fire" region for the ash.
        /// </summary>
        private static void FireRing(LayerContext ctx, float2 c, float r, int stones, string pickedFire, bool rockOk, uint v, int logs, float yaw)
        {
            if (!string.IsNullOrEmpty(pickedFire)) ctx.Object(c, 0f, ObjectRole.Furniture, AssetMix.One(pickedFire), v, -1, AssetCategories.CampingFire);
            if (rockOk)
                for (int k = 0; k < stones; k++)
                {
                    float a = k * 6.2832f / stones + Hashing.Unit(Hashing.Mix(v, (uint)k)) * 0.3f;
                    var p = c + new float2(math.cos(a), math.sin(a)) * r;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, (uint)k + 50u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(NatureKit.Stones, Hashing.Mix(v, (uint)k + 90u)), Hashing.Mix(v, (uint)k + 90u), -1, AssetCategories.Rock);
                    o.Scale = r < 1f ? 0.42f : 0.55f;
                    o.Elevation = -0.06f;
                }
            for (int k = 0; k < logs; k++)
            {
                float a = yaw + (k == 0 ? 0.6f : 2.4f) + Hashing.Unit(Hashing.Mix(v, (uint)k + 70u)) * 0.5f;
                var dir = new float2(math.cos(a), math.sin(a));
                var lp = c + dir * (r + 1.5f);
                // (the game draws its 10 m logs at full length whatever the scale: a camp bench is the seat)
                ctx.Object(lp, math.atan2(-dir.x, -dir.y), ObjectRole.Bench, AssetMix.Pick(CampArt.FireSeats, Hashing.Mix(v, (uint)k + 80u)), Hashing.Mix(v, (uint)k + 80u), -1, AssetCategories.Bench);
            }
            ctx.AddRegion(LayerRegion.Circle(c, r + 0.4f, 10), "fire:" + (v & 0xFFFF), FireTag);
        }

        private static bool NearPitch(List<(float2 c, float2 t, float side)> pitches, float2 p, float size)
        {
            float r = size * 0.75f;
            foreach (var pi in pitches) if (math.distancesq(pi.c, p) < r * r) return true;
            return false;
        }

        private static bool PitchFits(LayerRegion region, Occupancy occ, float2 c, float2 t, float size, List<float2> gates)
        {
            foreach (var corner in LayerRegion.Rect(c, t, size * 0.5f, size * 0.5f))
                if (!region.ContainsDisc(corner, 1f) || occ.Corridors.Overlaps(corner, 0f, 0.3f)) return false;
            foreach (var g in gates) if (math.distancesq(g, c) < (size + 8f) * (size + 8f)) return false;
            // The pad (half-diagonal ≈ 0.71·size) must not touch earlier pitches or reserved items.
            if (occ.Discs.Overlaps(c, size * 0.5f, 1f)) return false;
            return occ.Free(c, size * 0.45f, 1f, 0.2f);
        }
    }
}
