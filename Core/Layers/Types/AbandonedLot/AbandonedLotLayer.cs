// Abandoned lot: a plot the city forgot. Composition (not soup):
//   * a collapse/attention point ("Rubble focus": centre, street side or far corner) — rubble piles
//     near it thinning to chunks and dust, optional rubble mounds (terraform, blended to the edge);
//   * old foundations: concrete slabs broken into shards along slanted cracks, weeds coming up in the
//     cracks, wall remnants standing in runs along the slab edges, graffiti on them;
//   * overgrowth reclaiming from the outline inwards (weeds, young self-seeded trees in the corners);
//   * a fence rotting in panels, desire paths worn across from gate to gate;
//   * junk dumped at the street side; a squatter camp in the most sheltered corner.
// Subtypes are real compositions: Overgrown (green, slab mostly reclaimed), Rubble field (mounds,
// piles everywhere, dirt ground), Demolished block (clean foundations in a row along the street,
// sorted piles, intact site fence), Squatter camp (shacks round a fire), Fenced wasteland (graded
// gravel behind an intact chain-link fence, waiting for a developer).

using Placemaker.Core.Layers.Types.Scrapyard;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.UrbanRuins;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Ruins
{
    [AreaGenerator(310, Group = "industrial")]
    public sealed class AbandonedLotLayer : LeisureLayer
    {
        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        public static readonly string[] Subtypes = { "overgrown", "rubbleField", "demolished", "squatterCamp", "fencedWasteland", "brokenCarPark", "burntOut", "emptyLot" };

        /// <summary>Vanilla industrial fences (prefab names = their UI icon names); automatic otherwise.</summary>
        public static readonly string[] SiteFences = { "TallIndustrialChainLinkFence", "IndustrialMetalFence", "LowIndustrialWoodFence" };

        protected override string Area => "Abandoned";
        public override string Id => "abandonedLot";
        public override string Icon => "Media/Game/Icons/Bulldozer.svg";
        public override float MinArea => 300f;
        protected override int WantedEntrances(LayerRegion region) => math.min(2, Entrances.WantedByArea(region.Area));
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag, DirtJumps.Place };

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(DebrisAddOn.LayerId, ("mix", "junk"), ("where", "edges")),
            DirtJumps.InPlace(Feature(DirtJumpsAddOn.LayerId, ("lines", 1.0), ("height", 1.25))),
            DecayKit.Relabel(Feature("bigtrees", ("density", 0.3)), "Placemaker.Abandoned.Feature.bigtrees"),
            Feature("rocks", ("size", "small"), ("clusters", 3.0)),
            Feature("decals", ("theme", "dirt"), ("ground", "lawn"), ("density", 1.5)),
            Feature("terrain", ("shape", "mounds"), ("height", 1.0)),
            StreetShopsFeature(Area),
        };

        protected override void DefineControls(ControlSet c)
        {
            // (rundown, 2026-10-30) the standard lot is the rubble field: overgrown and vacant lot are hidden (they read as a park and as a lawn)
            c.Choice("subtype", "rubbleField", ControlTier.Essential, "lot", Subtypes).Tip();
            c.Slider("overgrowth", 60, 0, 100, 5, "%", ControlTier.Essential, "lot").Tip();
            c.Slider("rubble", 30, 0, 100, 5, "%", ControlTier.Essential, "lot").Tip();
            c.Slider("junk", 30, 0, 100, 5, "%", ControlTier.Essential, "lot").Tip();
            c.Slider("graffiti", 30, 0, 100, 5, "%", ControlTier.Essential, "lot").Tip();

            c.Slider("foundations", 1, 0, 4, 1, "count", ControlTier.More, "ground").Tip();
            c.Slider("cracking", 60, 0, 100, 5, "%", ControlTier.More, "ground").Tip();
            c.Toggle("groundPaint", true, ControlTier.More, "ground").Tip();
            c.Slider("paintLayer", 3, 1, 4, 1, "count", ControlTier.More, "ground").When("groundPaint=true").Tip();
            c.Toggle("desirePaths", true, ControlTier.More, "ground").Tip();
            c.Toggle("puddles", true, ControlTier.More, "ground");
            c.Asset("slabSurface", AssetCategories.SurfacePaving, ControlTier.More, "ground");

            c.Choice("focus", "centre", ControlTier.More, "rubble", "centre", "street", "corner").Tip();
            c.Choice("pileSize", "medium", ControlTier.More, "rubble", "small", "medium", "large");
            c.Slider("mounds", 0, 0, 4, 0.5, "m", ControlTier.More, "rubble").Tip();
            c.Toggle("walls", true, ControlTier.More, "rubble").Tip();
            c.AssetMulti("rubbleAssets", AssetCategories.RuinsRubble, ControlTier.More, "rubble");

            c.Slider("youngTrees", 20, 0, 80, 5, "perHa", ControlTier.More, "planting").Tip();
            c.Toggle("crackWeeds", true, ControlTier.More, "planting").Tip();
            c.AssetMulti("weedAssets", AssetCategories.RuinsWeeds, ControlTier.More, "planting");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "planting");

            c.Toggle("fence", true, ControlTier.More, "boundary");
            c.Slider("fenceGaps", 40, 0, 100, 5, "%", ControlTier.More, "boundary").When("fence=true").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");

            c.Slider("shelters", 0, 0, 16, 1, "count", ControlTier.More, "camp").Tip();
            c.Toggle("campfire", true, ControlTier.More, "camp");
            c.AssetMulti("shelterAssets", AssetCategories.RuinsShelter, ControlTier.More, "camp");

            c.AssetMulti("junkAssets", AssetCategories.RuinsJunk, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.RandomRange("foundations", 0, 2);
            c.RandomRange("mounds", 0, 2);
            c.RandomRange("shelters", 0, 6);
            c.Fixed("paintLayer");
        }

        protected override void DefinePresets()
        {
            Preset("overgrown", ("subtype", "overgrown"), ("overgrowth", 90.0), ("rubble", 15.0), ("junk", 20.0), ("graffiti", 20.0),
                ("foundations", 1.0), ("cracking", 85.0), ("youngTrees", 25.0), ("fenceGaps", 70.0), ("focus", "centre"), ("mounds", 0.0));
            Preset("rubbleField", ("subtype", "rubbleField"), ("overgrowth", 40.0), ("rubble", 90.0), ("junk", 25.0), ("graffiti", 10.0),
                ("foundations", 0.0), ("mounds", 2.5), ("pileSize", "large"), ("youngTrees", 0.0), ("fenceGaps", 55.0), ("walls", false));
            Preset("demolished", ("subtype", "demolished"), ("overgrowth", 10.0), ("rubble", 45.0), ("junk", 10.0), ("graffiti", 15.0),
                ("foundations", 3.0), ("cracking", 25.0), ("focus", "street"), ("youngTrees", 0.0), ("fenceGaps", 0.0), ("desirePaths", false));
            Preset("squatterCamp", ("subtype", "squatterCamp"), ("overgrowth", 50.0), ("rubble", 20.0), ("junk", 70.0), ("graffiti", 55.0),
                ("foundations", 1.0), ("cracking", 60.0), ("shelters", 7.0), ("campfire", true), ("fenceGaps", 60.0), ("focus", "corner"), ("youngTrees", 6.0));
            Preset("fencedWasteland", ("subtype", "fencedWasteland"), ("overgrowth", 35.0), ("rubble", 10.0), ("junk", 15.0), ("graffiti", 25.0),
                ("foundations", 0.0), ("fenceGaps", 0.0), ("youngTrees", 5.0), ("desirePaths", false), ("walls", false));
            // (rundown, 2026-10-29) three more kinds of forgotten lot, each its own story and ground (AbandonedLotGround)
            Preset("brokenCarPark", ("subtype", "brokenCarPark"), ("overgrowth", 55.0), ("rubble", 10.0), ("junk", 25.0), ("graffiti", 20.0),
                ("foundations", 0.0), ("cracking", 80.0), ("youngTrees", 12.0), ("fenceGaps", 50.0), ("walls", false), ("mounds", 0.0), ("shelters", 0.0), ("desirePaths", false));
            Preset("burntOut", ("subtype", "burntOut"), ("overgrowth", 25.0), ("rubble", 35.0), ("junk", 20.0), ("graffiti", 30.0),
                ("foundations", 1.0), ("cracking", 70.0), ("youngTrees", 0.0), ("fenceGaps", 45.0), ("focus", "centre"), ("mounds", 0.0), ("shelters", 0.0), ("pileSize", "small"));
            Preset("emptyLot", ("subtype", "emptyLot"), ("overgrowth", 40.0), ("rubble", 0.0), ("junk", 12.0), ("graffiti", 0.0),
                ("foundations", 0.0), ("youngTrees", 6.0), ("fence", false), ("desirePaths", true), ("walls", false), ("mounds", 0.0), ("shelters", 0.0));
        }

        /// <summary>Free-standing advertising hoardings of the game's own brands (the vacant lot's billboard).</summary>
        private static readonly string[] Hoardings = { "BillboardLarge01 - CatInACabin", "BillboardLarge02 - Pilestack", "BillboardLarge01 - Pilestack", "BillboardLarge02 - CatInACabin" };

        /// <summary>The wild plants of each kind of lot (vanilla wild bushes; the dead one where it burnt or dried out).</summary>
        private static string[] WeedsFor(string sub)
        {
            switch (sub)
            {
                // (rundown) never the teal FlowerBushWild ones (RundownPlants)
                case "burntOut": return Rundown.RundownPlants.Burnt;
                case "emptyLot": case "fencedWasteland": case "demolished": case "rubbleField": case "brokenCarPark": return Rundown.RundownPlants.Sparse;
                default: return Rundown.RundownPlants.Scrub;
            }
        }

        /// <summary>The burnt-out lot's heaps: mixed brick, concrete and timber (char decals under them).</summary>
        private static readonly string[] BurntRubble = { "RubblePileMixed01", "RubblePileMixed02", "RubblePileMixed03", "RubblePileMixed04", "RubblePileConcrete03" };   // (the wood heaps are fresh pale planks in the game, not charred)

        /// <summary>Type-specific curated defaults (over GritAssets' shared sets); null = shared only.</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtObjects = new Dictionary<string, GritAssets.Set>
        {
            // Squatters keep warm round a burn barrel (the pool's "campfire" was a tent).
            [AssetCategories.CampingFire] = new GritAssets.Set(true, "BarrelFire01|BarrelFire02", "BarrelFire02|BarrelFire01"),
            // (rundown) Fly-tipping as people really dump it: rubbish heaps and bags, an old sofa, boxes and pallets, a few drums.
            [AssetCategories.RuinsJunk] = new GritAssets.Set(false, "Trashpile01", "Trashpile02", "Trashpile03", "Trashpile04", "Trashpile06", "Trashpile07",
                "Trashbag01", "Trashbag03", "OutdoorSofa01", "OutdoorSofa03", "CardboardBoxPile02", "Pallet02", "Pallet04", "Barrel03", "BarrelPile01"),
        };

        /// <summary>(rundown) Old slabs read as weathered grey, never the bright white Concrete Surface 02 of the shared gritty set.</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtSurfaces = new Dictionary<string, GritAssets.Set>(System.StringComparer.Ordinal)
        {
            [AssetCategories.SurfacePaving] = new GritAssets.Set(true, "Pavement Surface 01|Concrete Surface 01"),
            [AssetCategories.SurfaceAsphalt] = new GritAssets.Set(true, "Pavement Surface 01|Concrete Surface 01"),
        };

        // Art pass: whatever the player left automatic gets the curated gritty defaults (GritAssets).
        protected override void PlanCore(LayerContext ctx) { PlanArt(ctx); GritAssets.Apply(ctx.Plan, ctx.Seed, ArtObjects, ArtSurfaces); GritAssets.CapDecals(ctx.Plan, ctx.LayerRegion.Area, 110f); }

        private void PlanArt(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var guard = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var zones = new Zones();
            var gates = Gates(ctx);
            string sub = ctx.S("subtype");
            // Foundations follow the plot edges, like the building that stood here.
            YardKit.GridFrame(region, out var centre, out var axis, out var side, out float major, out float minor);
            float ha = region.Area / 10000f;
            float over = ctx.F("overgrowth") / 100f, rubble = ctx.F("rubble") / 100f, junk = ctx.F("junk") / 100f, graffiti = ctx.F("graffiti") / 100f;
            int paintLayer = ctx.I("paintLayer");
            bool paint = ctx.B("groundPaint");
            var focus = DecayKit.Focus(region, gates, ctx.S("focus"));
            float pileScale = ctx.S("pileSize") == "small" ? 0.7f : ctx.S("pileSize") == "large" ? 1.5f : 1f;
            string weedCat = DecayKit.Cat(ctx, ctx.L("weedAssets"), AssetCategories.RuinsWeeds, AssetCategories.Bush);
            // (rundown) each lot its own plants: the burnt lot is dead scrub, the empty lot young scrub, the rest the wild mix
            string[] weedIds = ctx.L("weedAssets") != null && ctx.L("weedAssets").Length > 0 ? ctx.L("weedAssets") : WeedsFor(sub);
            string rubbleCat = rubble > 0f ? DecayKit.Cat(ctx, ctx.L("rubbleAssets"), AssetCategories.RuinsRubble, AssetCategories.Rock) : null;
            // the burnt-out lot's heaps are charred timber (unless the player picked their own)
            string[] rubbleIds = sub == "burntOut" && (ctx.L("rubbleAssets") == null || ctx.L("rubbleAssets").Length == 0) ? BurntRubble : ctx.L("rubbleAssets");
            string junkCat = junk > 0f ? DecayKit.Cat(ctx, ctx.L("junkAssets"), AssetCategories.RuinsJunk) : null;
            ctx.Check();
            var st = new AbandonedLotGround.Story { Sub = sub, Over = over, Rubble = rubble, Junk = junk, Focus = focus };

            // ── Boundary first: the fence and desire paths shape where everything else may go.
            if (ctx.B("fence"))
            {
                float gaps = ctx.F("fenceGaps") / 100f;
                DecayKit.BrokenFence(ctx, occ, region, gates, gaps, AssetMix.One(ctx.S("fenceAsset"), SiteFences), 0xFE7Cu, sub == "fencedWasteland" ? 2.5f : 3.5f);
            }

            // ── Ground.
            float2[] whole = DecayKit.Inset(region, 1.5f);
            // (rundown) the lot's paint is laid with its ground design (AbandonedLotGround.Paint): dry grass over all, dirt in blobs
            if (sub == "fencedWasteland")
            {
                // Graded gravel pad behind the fence, waiting for a developer.
                var pad = DecayKit.Inset(region, math.clamp(math.sqrt(region.Area) * 0.08f, 4f, 12f));
                if (pad != null && Geometry.Poly.SignedArea(pad) > 200f) { ctx.Surface(pad, SurfaceRole.Ground, AssetMix.One(""), AssetCategories.SurfaceNatural); }
            }

            // ── Foundations: broken slabs (in a row along the street for a demolished block).
            int wantedSlabs = ctx.I("foundations");
            // Footprint of a former building: full length 9–32 m, about 0.62 as deep.
            float slabL = math.clamp(math.sqrt(region.Area) * 0.13f, 9f, 32f), slabW = slabL * 0.62f;
            var slabs = new List<(OrientedRect r, DecayKit.Slab s)>();
            var slabAsset = AssetMix.One(ctx.S("slabSurface"));
            if (sub == "brokenCarPark") BrokenCarPark(ctx, region, occ, zones, gates, centre, axis, major, minor, slabs, st);
            for (int i = 0; i < wantedSlabs; i++)
            {
                float2 near = sub == "demolished" ? focus + axis * ((i - (wantedSlabs - 1) * 0.5f) * (slabL + 5f)) : focus + side * (i * slabW * 1.2f) * ((i & 1) == 0 ? 1f : -1f);
                if (!region.Contains(near)) near = focus;
                uint s = Hashing.Mix(ctx.Seed, 0x51ABu + (uint)i);
                float scale = 0.8f + 0.4f * Hashing.Unit(s);
                if (!DecayKit.FindRect(region, occ, zones, near, axis, slabL * 0.5f * scale, slabW * 0.5f * scale, 2f, s, out var r, 90)) continue;
                zones.Add(Grow(r, 0.6f).Ring);
                var slab = DecayKit.BrokenSlab(ctx, r, ctx.F("cracking") / 100f, SurfaceRole.Paving, slabAsset, AssetCategories.SurfacePaving, s);
                slabs.Add((r, slab));
                st.Slabs.Add((r, slab, false));
            }
            ctx.Check();

            // ── Room for the dirt-jumps feature (kids dig their jumps into the open ground).
            DirtJumps.CarvePlace(ctx, region, occ, zones, region.DeepestPoint(out _, null, 12), axis, 1);

            // ── Desire paths: worn across the lot between the gates (and to the camp later).
            if (ctx.B("desirePaths") && gates.Count >= 2)
                DecayKit.DesirePath(ctx, occ, region, gates[0].Position, gates[1].Position, paintLayer, 0xD351u, st.Shortcuts);
            else if (ctx.B("desirePaths") && gates.Count == 1 && (sub == "emptyLot" || sub == "brokenCarPark"))
            {
                // one gate: the shortcut runs from it to the far side, where people climb through to the next street
                var far = region.ClosestOnBoundary(region.Centroid + (region.Centroid - gates[0].Position), out _, out _);
                DecayKit.DesirePath(ctx, occ, region, gates[0].Position, far, paintLayer, 0xD353u, st.Shortcuts);
            }
            // ── Tyre ruts: cars that park on the empty lot, the demolition machines, the lorries that graded the pad.
            if (gates.Count > 0 && (sub == "emptyLot" || sub == "demolished" || sub == "fencedWasteland"))
            {
                var g0 = gates[0].Position; var inw = region.InwardNormal(gates[0].Segment);
                float reach = math.min(minor * (sub == "emptyLot" ? 0.4f : 0.65f), 60f);
                var bendDir = new float2(-inw.y, inw.x) * ((Hashing.Unit(Hashing.Mix(ctx.Seed, 0x7A7u)) - 0.5f) * reach * 0.6f);
                var line = new[] { g0 + inw * 1f, g0 + inw * reach * 0.5f + bendDir * 0.5f, g0 + inw * reach + bendDir };
                if (region.Contains(line[2])) { st.Ruts.Add(line); occ.TakeCorridor(line[0], line[1], 1.6f, 40); occ.TakeCorridor(line[1], line[2], 1.6f, 40); }
            }

            // ── Wall remnants on the slab edges (runs survive, gaps where they fell), graffiti on them.
            int walls = 0, graffitiCount = 0;
            string wallCat = ctx.B("walls") && slabs.Count > 0 ? DecayKit.Cat(ctx, null, AssetCategories.RuinsWall, AssetCategories.RuinsSlab) : null;
            foreach (var (r, slab) in slabs)
            {
                var ring = r.Ring;
                for (int e = 0; e < 4; e++)
                {
                    var a = ring[e]; var b = ring[(e + 1) % 4];
                    var t = math.normalizesafe(b - a);
                    var inward = new float2(-t.y, t.x);
                    float yaw = math.atan2(t.x, t.y) + math.PI * 0.5f;
                    float survive = sub == "demolished" ? 0.25f : 0.45f;
                    foreach (var p0 in LeisureKit.Along(a, b, 2.6f))
                    {
                        var p = p0 + inward * 0.6f;
                        uint v = Hashing.Mix(ctx.Seed, (int)(p.x * 3f) + 0x3A11, (int)(p.y * 3f));
                        bool stands = wallCat != null && ValueNoise.Clumps(p, 9f, Hashing.Mix(ctx.Seed, 0xA11u)) > 1f - survive;
                        if (stands && occ.Free(p, 0.7f, 0.6f, 0.05f))
                        {
                            // (rundown) dark masonry only (the automatic wall pool gave white balustrades), low stubs and the odd taller piece
                            // the garden walls run along their local Z (lab: 0.2 x 0.9 x 3.0 m): the old +90 degrees stood every piece across the slab edge like a comb
                            var wo = ctx.Object(p, math.atan2(t.x, t.y), ObjectRole.Furniture, AssetMix.Pick(Hashing.Unit(Hashing.Mix(v, 5u)) < 0.3f ? UrbanRuins.BuildingRuins.Tall : UrbanRuins.BuildingRuins.Low, v), v, -1, wallCat);
                            if (Hashing.Unit(Hashing.Mix(v, 5u)) < 0.12f) wo.Scale = 1.3f + 0.5f * Hashing.Unit(Hashing.Mix(v, 6u));
                            occ.Take(p, 0.9f, 2);
                            walls++;
                            if (Hashing.Unit(Hashing.Mix(v, 3u)) < graffiti && AssetAvailability.Has(AssetCategories.DecalGraffiti)
                                && DecayKit.Decal(ctx, guard, p - inward * 1.3f, yaw, AssetCategories.DecalGraffiti, Hashing.Mix(v, 4u))) graffitiCount++;
                        }
                    }
                }
                // Cracks and puddles on the slab.
                if (AssetAvailability.Has(AssetCategories.DecalCracks))
                    foreach (var sh in slab.Shards)
                    {
                        var c = Geometry.Poly.Centroid(sh);
                        uint v = Hashing.Mix(ctx.Seed, (int)(c.x * 5f), (int)(c.y * 5f));
                        if (Hashing.Unit(v) < 0.4f + ctx.F("cracking") / 160f) DecayKit.Decal(ctx, guard, c, Hashing.Unit(v) * 6.283f, AssetCategories.DecalCracks, v);
                    }
                if (ctx.B("puddles") && AssetAvailability.Has(AssetCategories.DecalPuddles))
                    for (int k = 0; k < 2; k++)
                    {
                        uint v = Hashing.Mix(ctx.Seed, (uint)k + 0x9D1u + (uint)(r.Center.x * 7f));
                        var p = r.Local((Hashing.Unit(v) - 0.5f) * r.HalfLength * 1.4f, (Hashing.Unit(Hashing.Mix(v)) - 0.5f) * r.HalfWidth * 1.4f);
                        DecayKit.Decal(ctx, guard, p, Hashing.Unit(v) * 6.283f, AssetCategories.DecalPuddles, v);
                    }
            }
            ctx.Check();

            // ── Squatter camp in the most sheltered spot (far from the gates).
            int shelters = ctx.I("shelters");
            if (sub == "squatterCamp") shelters = math.max(shelters, 4);
            int sheltersPlaced = 0;
            float2 camp = focus;
            bool hasCamp = false;
            if (shelters > 0)
            {
                float campR = math.clamp(3.5f + shelters * 0.75f, 5f, 15f);
                var avoid = LeisureKit.Points(gates);
                if (Spots.Find(occ, campR + 2.5f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0xCA4Bu), out camp, avoid, math.min(30f, minor * 0.4f), 2f)
                    || Spots.Find(occ, campR * 0.7f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0xCA4Cu), out camp))
                {
                    hasCamp = true; st.HasCamp = true; st.Camp = camp; st.CampR = campR;
                    if (paint) TerrainPaint.Patch(ctx, LeisureKit.Blob(camp, campR + 2f, Hashing.Mix(ctx.Seed, 0xCA4Du), 14, 0.25f), paintLayer, 0.8f, 2f);
                    if (ctx.B("campfire"))
                    {
                        string fire = DecayKit.Cat(ctx, null, AssetCategories.CampingFire, AssetCategories.Camping);
                        if (fire != null) LeisureKit.Put(ctx, occ, camp, 0f, ObjectRole.Furniture, AssetMix.One(""), fire, 1.2f, Hashing.Mix(ctx.Seed, 0xF14Eu), 1f, 0.3f);
                        else occ.Take(camp, 1.2f, 2);
                        // Crates and drums as seats round the fire.
                        if (junkCat != null)
                            for (int k = 0; k < 4; k++)
                            {
                                float a = k * 1.5708f + 0.4f;
                                var p = camp + new float2(math.cos(a), math.sin(a)) * 2.6f;
                                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, camp), ObjectRole.Furniture, AssetMix.Pick(ctx.L("junkAssets"), (uint)k), junkCat, 0.6f, Hashing.Mix(ctx.Seed, 0x5EA7u + (uint)k), 0.5f, 0.1f);
                            }
                    }
                    else occ.Take(camp, 1.5f, 2);
                    string shelterCat = DecayKit.Cat(ctx, ctx.L("shelterAssets"), AssetCategories.RuinsShelter, AssetCategories.CampingPitch, AssetCategories.Camping, AssetCategories.GardenShed);
                    if (shelterCat != null)
                    {
                        float phase = Hashing.Unit(Hashing.Mix(ctx.Seed, 0x5E1u)) * 6.283f;
                        for (int k = 0; k < shelters * 3 && sheltersPlaced < shelters; k++)
                        {
                            uint v = Hashing.Mix(ctx.Seed, 0x5E10u + (uint)k);
                            float rr = campR * (0.75f + 0.35f * Hashing.Unit(v)) + (k / shelters) * 3f;
                            float a = phase + (k + 0.3f * Hashing.Unit(Hashing.Mix(v))) * 6.283f / shelters;
                            var p = camp + new float2(math.cos(a), math.sin(a)) * rr;
                            if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, camp), ObjectRole.Furniture, AssetMix.Pick(ctx.L("shelterAssets"), v), shelterCat, 2f, v, 0.8f, 0.4f)) sheltersPlaced++;
                        }
                    }
                    if (junkCat != null)
                        DecayKit.JunkCluster(ctx, occ, camp + new float2(campR + 3f, 0f), 4 + (int)(junk * 6f), ctx.L("junkAssets"), junkCat, 0xCA5Eu);
                    if (ctx.B("desirePaths") && gates.Count > 0)
                        DecayKit.DesirePath(ctx, occ, region, gates[0].Position, camp, paintLayer, 0xD352u, st.Shortcuts);
                }
                else ctx.Issue(IssueSeverity.Info, "Placemaker.Abandoned.Issue.NoCampSpot");
            }
            ctx.Check();

            // ── Rubble mounds (terraform) around the collapse point, blended to the ground at their rim.
            float rubbleR = math.clamp(math.sqrt(region.Area) * (0.22f + 0.35f * rubble), 10f, 110f) * (sub == "rubbleField" ? 1.4f : 1f);
            int moundCells = 0;
            if (ctx.F("mounds") > 0f && sub != "rubbleField")
            {
                var sub0 = LeisureKit.Intersect(region, LayerRegion.Circle(focus, rubbleR, 20));
                if (sub0 != null && sub0.Area > 200f)
                {
                    var res = TerrainShaper.Shape(ctx, sub0, new TerrainSpec
                    {
                        Mode = TerrainMode.Mounds, Height = ctx.F("mounds"), Scale = 16f, MaxSlopePct = 35f, Falloff = math.max(8f, rubbleR * 0.35f),
                        Seed = Hashing.Mix(ctx.Seed, 0x40DDu), Skip = p => zones.Inside(p, 2f) || (hasCamp && math.distance(p, camp) < 14f),
                    });
                    moundCells = res.Cells;
                }
            }

            // ── Rubble: piles near the collapse point thinning outwards (a second and third focus on a rubble field).
            var rubbleTotal = new DecayKit.RubbleResult();
            if (rubbleCat != null && sub == "rubbleField")
            {
                // (rundown, the coordinator's round 7: "heaps in an even cluster at the centre") a fly-tipped field: big mounds where the
                // lorries backed in from the street, a trail of spills, broken slabs, all half grown over
                rubbleTotal.Piles += TippedRubble(ctx, region, occ, zones, gates, focus, axis, rubbleIds, rubbleCat, st);
                // a raised mound of tipped earth under each (permanent terraform, blended to the ground)
                for (int mi = 0; mi < st.Mounds.Count && ctx.F("mounds") > 0f; mi++)
                {
                    var (mc, mr) = st.Mounds[mi];
                    var mreg = LeisureKit.Intersect(region, LayerRegion.Circle(mc, mr + 4f, 16));
                    if (mreg == null || mreg.Area < 60f) continue;
                    moundCells += TerrainShaper.Shape(ctx, mreg, new TerrainSpec { Mode = TerrainMode.Mounds, Height = ctx.F("mounds") * 0.6f, Scale = 10f, MaxSlopePct = 35f, Falloff = 4f, Seed = Hashing.Mix(ctx.Seed, 0x40DEu + (uint)mi), Skip = p => zones.Inside(p, 2f) }).Cells;
                }
            }
            else if (rubbleCat != null)
            {
                var foci = new List<float2> { focus };
                if (sub == "rubbleField" && Spots.Find(occ, 6f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0xF0C2u), out var f2, foci, rubbleR * 0.8f, 2f, 1500))
                    foci.Add(f2);
                bool onSlabs = sub == "rubbleField";
                for (int i = 0; i < foci.Count; i++)
                {
                    var res = DecayKit.RubbleField(ctx, occ, guard, region, foci[i], rubbleR * (i == 0 ? 1f : 0.7f), rubble, rubbleIds, rubbleCat, pileScale,
                        0x2B1u + (uint)i * 77u, p => !onSlabs && zones.Inside(p, 0.5f), onSlabs ? 900 : 450);
                    rubbleTotal.Piles += res.Piles; rubbleTotal.Chunks += res.Chunks; rubbleTotal.Decals += res.Decals;
                }
                if (sub == "demolished")
                    rubbleTotal.Piles += SortedPiles(ctx, occ, zones, region, focus, axis, side, rubbleCat, pileScale);
            }
            ctx.Check();

            // ── (rundown) The vacant lot's tell: an advertising hoarding on the street side, the lot let to whoever pays for the view.
            if (sub == "emptyLot" && gates.Count > 0)
            {
                int seg = gates[0].Segment;
                var a = region.Ring[seg]; var b = region.Ring[(seg + 1) % region.Ring.Length];
                var inw = region.InwardNormal(seg);
                float t = Hashing.Unit(Hashing.Mix(ctx.Seed, 0xB111u)) < 0.5f ? 0.22f : 0.78f;
                var p = math.lerp(a, b, t) + inw * 4f;
                if (region.ContainsDisc(p, 3f) && occ.Free(p, 3f, 0.3f, 0f))
                {
                    ctx.Object(p, math.atan2(-inw.x, -inw.y), ObjectRole.Furniture, AssetMix.Pick(Hoardings, Hashing.Mix(ctx.Seed, 0xB112u)), Hashing.Mix(ctx.Seed, 0xB112u), -1, AssetCategories.RuinsJunk);
                    occ.Take(p, 3.5f, 2);
                }
            }

            // ── Junk dumped at the street side / along the fence.
            int junkItems = 0;
            if (junkCat != null)
            {
                int heaps = (int)math.round(junk * (1.5f + ha * 3f));
                float2 street = gates.Count > 0 ? gates[0].Position : region.Ring[0];
                var spots = new List<float2>();
                for (int i = 0; i < heaps; i++)
                {
                    if (!Spots.Find(occ, 2.5f, i % 3 == 2 ? Spots.Preference.Open : Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x7C0u + (uint)i), out var s, spots, 10f, 1.5f, 1200)) break;
                    spots.Add(s);
                    junkItems += DecayKit.JunkCluster(ctx, occ, s, 3 + (int)(junk * 6f), ctx.L("junkAssets"), junkCat, 0x7C10u + (uint)i * 19u);
                }
            }

            // ── Overgrowth reclaiming from the edges; weeds in the cracks first.
            int crackWeeds = 0;
            if (ctx.B("crackWeeds"))
                foreach (var (r, slab) in slabs)
                    crackWeeds += DecayKit.CrackWeeds(ctx, occ, slab.Cracks, math.saturate(over * 0.9f + 0.05f) * (sub == "brokenCarPark" ? 0.3f : 1f), weedIds, weedCat, 0xC3Au + (uint)(r.Center.x * 3f));
            float edgeReach = sub == "overgrown" ? 22f : 14f;
            var (weeds, trees) = DecayKit.Overgrowth(ctx, occ, region, over, ctx.F("youngTrees"), weedIds, weedCat, Rundown.RundownPlants.Or(ctx.L("treeSpecies"), Rundown.RundownPlants.Trees), 0x0F6u,
                p => zones.Inside(p, 0.8f), edgeReach, sub == "overgrown" ? 1f : sub == "squatterCamp" || sub == "brokenCarPark" ? 0.5f : 0.3f);
            ctx.Check();

            // ── Ground decals: dirt and puddles across the open ground, graffiti on the fence line.
            if (AssetAvailability.Has(AssetCategories.DecalDirt))
            {
                int dirt = math.min(400, (int)(region.Area / 250f * (0.3f + rubble)));
                int n = 0;
                foreach (var p in Scatter.Candidates(region, 4f, Hashing.Mix(ctx.Seed, 0xD1Bu), dirt * 3 + 20, ctx.Cancel))
                {
                    if (n >= dirt) break;
                    uint v = Hashing.Mix(ctx.Seed, (int)(p.x * 9f), (int)(p.y * 9f));
                    if (Scatter.Clustering(p, 12f, Hashing.Mix(ctx.Seed, 0xD1Cu), 0.2f) < Hashing.Unit(v)) continue;
                    if (DecayKit.Decal(ctx, guard, p, Hashing.Unit(v) * 6.283f, AssetCategories.DecalDirt, v)) n++;
                }
            }
            if (ctx.B("puddles") && AssetAvailability.Has(AssetCategories.DecalPuddles))
            {
                int puddles = math.clamp((int)(ha * 12f), 2, 60), n = 0;
                foreach (var p in Scatter.Candidates(region, 8f, Hashing.Mix(ctx.Seed, 0x9D2u), puddles * 4, ctx.Cancel))
                {
                    if (n >= puddles) break;
                    if (zones.Inside(p)) continue;
                    if (DecayKit.Decal(ctx, guard, p, Hashing.Unit(Hashing.Mix(ctx.Seed, (int)p.x, (int)p.y)) * 6.283f, AssetCategories.DecalPuddles, Hashing.Mix(ctx.Seed, (int)p.y, (int)p.x))) n++;
                }
            }
            if (ctx.B("fence") && graffiti > 0f)
            {
                var ring = region.Ring;
                for (int e = 0; e < ring.Length; e++)
                {
                    var a = ring[e]; var b = ring[(e + 1) % ring.Length];
                    graffitiCount += DecayKit.GraffitiAlong(ctx, guard, a, b, region.InwardNormal(e), graffiti * 0.35f, 0x6AFu + (uint)e * 97u, 6f);
                }
            }

            st.RubbleR = rubbleR; st.BurnSeat = slabs.Count > 0 ? slabs[0].r.Center : focus; st.BurnR = math.max(14f, rubbleR * 0.9f);
            AbandonedLotGround.Dress(ctx, st);

            EmitGreenRegions(ctx, occ, 80f);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.RubblePiles", rubbleTotal.Piles + rubbleTotal.Chunks, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.JunkItems", junkItems, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Foundations", slabs.Count, Keys.UnitCount);
            ctx.Stat(Keys.StatBushes, weeds + crackWeeds, Keys.UnitCount);
            if (hasCamp) ctx.Stat("Placemaker.Stat.Shelters", sheltersPlaced, Keys.UnitCount);
            ctx.Plan.Facts["foundations"] = slabs.Count;
            ctx.Plan.Facts["walls"] = walls;
            ctx.Plan.Facts["rubblePiles"] = rubbleTotal.Piles;
            ctx.Plan.Facts["rubbleChunks"] = rubbleTotal.Chunks;
            ctx.Plan.Facts["junk"] = junkItems;
            ctx.Plan.Facts["weeds"] = weeds;
            ctx.Plan.Facts["crackWeeds"] = crackWeeds;
            ctx.Plan.Facts["youngTrees"] = trees;
            ctx.Plan.Facts["graffiti"] = graffitiCount;
            ctx.Plan.Facts["shelters"] = sheltersPlaced;
            ctx.Plan.Facts["moundCells"] = moundCells;
            if (wantedSlabs > slabs.Count) ctx.Issue(IssueSeverity.Info, "Placemaker.Abandoned.Issue.FewerFoundations", slabs.Count, wantedSlabs);
        }

        /// <summary>
        /// The old car park: one big asphalt slab broken along its cracks, faded bays in rows either side of an aisle, a lone
        /// lamp still standing, concrete blocks dragged across the way in, rubbish dumped in a corner bay.
        /// </summary>
        private static void BrokenCarPark(LayerContext ctx, LayerRegion region, Occupancy occ, Zones zones, List<Gate> gates, float2 centre, float2 axis,
            float major, float minor, List<(OrientedRect r, DecayKit.Slab s)> slabs, AbandonedLotGround.Story st)
        {
            float inset = math.clamp(math.sqrt(region.Area) * 0.06f, 3f, 10f);
            float hl = math.min(major * 0.5f - inset, 70f), hw = math.min(minor * 0.5f - inset, 45f);
            if (hl < 8f || hw < 6f) return;
            if (!DecayKit.FitRect(region, occ, centre, axis, hl, hw, 1.5f, out var r, 8f, 6f)) return;
            var slab = DecayKit.BrokenSlab(ctx, r, 0f, SurfaceRole.Asphalt, AssetMix.One(""), AssetCategories.SurfaceAsphalt, Hashing.Mix(ctx.Seed, 0xCA9Bu));
            zones.Add(r.Ring);
            // one slab broken by wandering cracks (a grid of shards read as paving slabs, not an old car park): random walks in
            // from the edges, where frost and roots got in; weeds come up along them
            slab.Cracks.Clear();
            slab.Cracks.AddRange(DecayKit.WanderingCracks(r, 4 + (int)(r.HalfLength * r.HalfWidth / 220f * (0.4f + ctx.F("cracking") / 100f)), Hashing.Mix(ctx.Seed, 0xC4ACu)));
            slabs.Add((r, slab));
            st.Slabs.Add((r, slab, true));
            st.Bays = r; st.HasBays = true;
            // a lone lamp on the aisle (one of two still standing), its twin a stump of rubble
            var lampAt = r.Local((Hashing.Unit(Hashing.Mix(ctx.Seed, 0x1A3Du)) - 0.5f) * r.HalfLength * 0.8f, 0f);
            if (occ.Free(lampAt, 0.6f, 0.4f, 0f))
            {
                ctx.Object(lampAt, r.Yaw, ObjectRole.Lamp, AssetMix.Pick(new[] { "LightpoleIndustrial01", "LightpoleIndustrial02" }, Hashing.Mix(ctx.Seed, 0x1A3Eu)), Hashing.Mix(ctx.Seed, 0x1A3Eu), -1, AssetCategories.Lamp);
                occ.Take(lampAt, 0.8f, 2);
            }
            // concrete blocks across each way in (nobody drives in any more)
            foreach (var g in gates)
            {
                var inw = region.InwardNormal(g.Segment);
                var t = new float2(-inw.y, inw.x);
                for (int k = -1; k <= 1; k++)
                {
                    var p = g.Position + inw * 2.2f + t * (k * 2.4f);
                    uint v = Hashing.Mix(ctx.Seed, 0xB10Cu + (uint)k * 7u + (uint)g.Segment);
                    if (!region.Contains(p) || !occ.Free(p, 1.1f, 0.5f, 0f)) continue;
                    ctx.Object(p, math.atan2(t.x, t.y) + (Hashing.Unit(v) - 0.5f) * 0.5f, ObjectRole.Furniture, AssetMix.Pick(new[] { "ConcreteBarrier01", "ConcreteBarrier02", "ConcreteBarrier03" }, v), v, -1, AssetCategories.RuinsSlab);
                    occ.Take(p, 1.2f, 2);
                }
            }
            // (no abandoned car: the game's vehicle props are not placeable objects; a dumped heap of rubbish in a corner bay instead)
            var heap = r.Local(r.HalfLength * 0.8f * (Hashing.Unit(Hashing.Mix(ctx.Seed, 0x7A1Eu)) < 0.5f ? -1f : 1f), r.HalfWidth * 0.66f);
            if (occ.Free(heap, 1.4f, 0.3f, 0f))
            {
                ctx.Object(heap, Hashing.Unit(Hashing.Mix(ctx.Seed, 0x7A20u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(new[] { "Trashpile01", "Trashpile02", "Trashpile06" }, Hashing.Mix(ctx.Seed, 0x7A1Fu)), Hashing.Mix(ctx.Seed, 0x7A1Fu), -1, AssetCategories.RuinsJunk);
                occ.Take(heap, 1.6f, 2);
            }
        }

        private static readonly string[] TipHeaps = { "RubblePileConcrete01", "RubblePileConcrete02", "RubblePileMixed01", "RubblePileMixed03", "RubblePileConcrete04", "RubblePileMixed04" };
        private static readonly string[] SpillHeaps = { "RubblePileConcrete03", "RubblePileMixed02", "CoalTruck01_StonePile01", "RubblePileWood02" };

        /// <summary>
        /// A fly-tipped rubble field: 3-6 big tipped mounds (each 2-4 heaps run together, a raised mound under it) in a fan where
        /// lorries backed in from the street, a trail of smaller spills from the way in, a few broken slabs lying about, scrub
        /// growing into the older mounds. Returns heaps placed; the mounds and spills go to the ground story.
        /// </summary>
        private static int TippedRubble(LayerContext ctx, LayerRegion region, Occupancy occ, Zones zones, List<Gate> gates, float2 focus, float2 axis,
            string[] chosen, string cat, AbandonedLotGround.Story st)
        {
            var ids = chosen != null && chosen.Length > 0 ? chosen : TipHeaps;
            var spills = chosen != null && chosen.Length > 0 ? chosen : SpillHeaps;
            float2 g0 = gates.Count > 0 ? gates[0].Position : region.ClosestOnBoundary(focus, out _, out _);
            float2 inw = gates.Count > 0 ? region.InwardNormal(gates[0].Segment) : math.normalizesafe(focus - g0);
            var side = new float2(-inw.y, inw.x);
            // the tipping spread over years across most of the lot (round 9: one corner full, the rest empty)
            float reach = math.clamp(math.sqrt(region.Area) * 0.8f, 25f, 160f);
            int mounds = math.clamp(4 + (int)(region.Area / 4000f), 4, 12);
            int placed = 0;
            for (int m = 0, made = 0; m < mounds * 3 && made < mounds; m++)
            {
                uint h = Hashing.Mix(ctx.Seed, 0x7199u + (uint)m * 17u);
                // a fan from the way in: each lorry tipped a little further in or to the side of the last
                float d = math.lerp(12f, reach, Hashing.Unit(h));
                float s = (Hashing.Unit(Hashing.Mix(h, 1u)) - 0.5f) * reach * 0.9f;
                var c = g0 + inw * d + side * s;
                if (!region.ContainsDisc(c, 7f)) continue;
                int heaps = 2 + (int)(Hashing.Unit(Hashing.Mix(h, 2u)) * 3f);
                float big = 1.25f + 0.55f * Hashing.Unit(Hashing.Mix(h, 3u));
                var dir = math.normalizesafe(inw + side * (Hashing.Unit(Hashing.Mix(h, 4u)) - 0.5f));
                int here = 0;
                for (int k = 0; k < heaps; k++)
                {
                    var p = c + dir * ((k - heaps * 0.5f) * 3.6f * big) + new float2(-dir.y, dir.x) * ((Hashing.Unit(Hashing.Mix(h, 10u + (uint)k)) - 0.5f) * 3f);
                    if (!region.ContainsDisc(p, 4f) || zones.Inside(p, 1f) || !occ.Free(p, 3.5f * big, 0.3f, 0f)) continue;
                    uint v = Hashing.Mix(h, 20u + (uint)k);
                    var o = ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(ids, v), v, -1, cat);
                    o.Scale = big * (0.85f + 0.3f * Hashing.Unit(Hashing.Mix(v, 1u)));
                    occ.Take(p, 4.5f * big, 2);
                    here++; placed++;
                }
                if (here == 0) continue;
                made++;
                st.Mounds.Add((c, 5f * big + heaps * 1.5f));
                // the oldest mounds are grown over: scrub on their flanks
                if (m % 2 == 1)
                    for (int k = 0; k < 4 + heaps; k++)
                    {
                        uint v = Hashing.Mix(h, 40u + (uint)k);
                        float a = Hashing.Unit(v) * 6.283f, r = 4f * big + 2f * Hashing.Unit(Hashing.Mix(v, 1u)) + heaps;
                        var p = c + new float2(math.cos(a), math.sin(a)) * r;
                        LeisureKit.Put(ctx, occ, p, a, ObjectRole.Bush, AssetMix.Pick(Rundown.RundownPlants.Scrub, v), null, 0.9f, v, 0.2f, 0.1f);
                    }
            }
            // the trail of spills: what fell off the backs of the lorries on the way in, thinning out
            var trailEnd = g0 + inw * reach * 0.9f;
            for (int k = 0; k < 10; k++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x7300u + (uint)k);
                var p = math.lerp(g0 + inw * 5f, trailEnd, Hashing.Unit(v)) + side * ((Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 9f);
                if (!region.ContainsDisc(p, 2f) || zones.Inside(p, 1f)) continue;
                if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(spills, v), cat, 1.8f, v, 0.2f, 0.1f))
                {
                    st.Spills.Add(p);
                    placed++;
                }
            }
            st.TipLine = new[] { g0, g0 + inw * reach * 0.5f, trailEnd };
            // broken slabs: concrete blocks and kerb pieces dumped with the rest, lying at angles
            for (int k = 0; k < 8; k++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x7400u + (uint)k);
                var p = g0 + inw * math.lerp(8f, reach, Hashing.Unit(v)) + side * ((Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * reach);
                if (!region.ContainsDisc(p, 2f) || !occ.Free(p, 1.2f, 0.2f, 0f)) continue;
                var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(new[] { "ConcreteBarrier01", "ConcreteBarrier02", "ConcreteBarrier03" }, v), v, -1, AssetCategories.RuinsSlab);
                o.Scale = 0.9f + 0.4f * Hashing.Unit(Hashing.Mix(v, 3u));
                occ.Take(p, 1.3f, 2);
            }
            return placed;
        }

        private static OrientedRect Grow(OrientedRect r, float d) =>
            new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength + d, HalfWidth = r.HalfWidth + d };

        /// <summary>Demolished block: the cleared rubble sorted into a neat row of big piles along the street side.</summary>
        private static int SortedPiles(LayerContext ctx, Occupancy occ, Zones zones, LayerRegion region, float2 focus, float2 axis, float2 side, string cat, float scale)
        {
            int n = 0;
            float2 dir = math.dot(side, region.DeepestPoint(out _, null, 12) - focus) > 0f ? side : -side;
            var row = focus + dir * 6f;
            for (int i = -4; i <= 4; i++)
            {
                var p = row + axis * (i * 7f * scale);
                uint v = Hashing.Mix(ctx.Seed, 0x5097u + (uint)(i + 10));
                if (zones.Inside(p, 2f)) continue;
                if (LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), cat, 2.4f * scale, v, 1f, 0.6f)) n++;
            }
            return n;
        }
    }
}
