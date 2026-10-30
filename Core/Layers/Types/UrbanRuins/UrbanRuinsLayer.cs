// Urban ruins: the skate park with a broken-down highway. Composition:
//   * the HIGHWAY: a deck line along the long axis (centre or one side), cut into spans by pillar
//     pairs. Spans collapse by chance ("Collapsed share"): the fallen deck lies on the ground as
//     broken asphalt with faded lane markings, tilted slabs and a rubble heap; the pillars beside it
//     are stumps. Intact spans are the roof of the DIY spot. Optional on-ramp embankment (terraform,
//     rising towards the break) ends abruptly at a collapsed span;
//   * the DIY SPOT under the intact spans: concrete pads with skate lines (ramps at the ends facing
//     in, rails/boxes between), concrete-block ledges, an optional dug bowl, graffiti on pillars;
//   * the YARD beside it, in priority by subtype: BMX dirt-jump lines (terraform), a parkour field of
//     blocks on gravel, a zig-zag row of legal graffiti walls;
//   * edges: spectator benches facing the pads, work lamps, a broken fence, overgrowth reclaiming the
//     outline and the fallen deck.
// Subtypes: DIY skate spot, Broken highway ruins, Graffiti park, Parkour yard, BMX dirt jumps.

using Placemaker.Core.Layers.Types.Scrapyard;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.Ruins;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.UrbanRuins
{
    [AreaGenerator(311, Group = "sports")]
    public sealed class UrbanRuinsLayer : LeisureLayer
    {
        public static readonly string[] Subtypes = { "diySpot", "highwayRuins", "graffitiPark", "parkour", "dirtJumps", "buildingRuins" };

        protected override string Area => "Ruins";
        public override string Id => "urbanRuins";
        public override string Icon => "Media/Game/Icons/ExtradosedBridge01.svg";
        public override float MinArea => 800f;
        protected override int WantedEntrances(LayerRegion region) => math.min(2, Entrances.WantedByArea(region.Area));
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag, DirtJumps.Place };

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            DirtJumps.InPlace(Feature(DirtJumpsAddOn.LayerId, ("lines", 2.0))),
            Feature(DebrisAddOn.LayerId, ("mix", "mixed"), ("where", "edges")),
            Feature("restrooms"),
            Feature("shops", ("count", 1.0), ("where", "entrances")),
            Feature("decals", ("theme", "graffiti"), ("ground", "paved"), ("density", 1.0)),
            DecayKit.Relabel(Feature("bigtrees", ("density", 1.0)), "Placemaker.Ruins.Feature.bigtrees"),
            Feature("terrain", ("shape", "mounds"), ("height", 1.0)),
            StreetShopsFeature(Area),
        };

        protected override void DefineControls(ControlSet c)
        {
            // (rundown, 2026-10-30) the standard style is the ruined building: the skate spots are hidden on the Rundown shelf
            c.Choice("subtype", "buildingRuins", ControlTier.Essential, "spot", Subtypes).Tip();
            c.Slider("skateItems", 0, 0, 30, 1, "count", ControlTier.Essential, "spot").Tip();
            c.Slider("graffiti", 60, 0, 100, 5, "%", ControlTier.Essential, "spot").Tip();
            c.Slider("decay", 75, 0, 100, 5, "%", ControlTier.Essential, "spot").Tip();
            c.Slider("overgrowth", 50, 0, 100, 5, "%", ControlTier.Essential, "spot").Tip();

            c.Toggle("deck", false, ControlTier.More, "deck").Tip();
            c.Choice("deckSide", "side", ControlTier.More, "deck", "centre", "side").When("deck=true").Tip();
            c.Slider("deckWidth", 14, 8, 24, 1, "m", ControlTier.More, "deck").When("deck=true");
            c.Slider("pillarSpacing", 16, 10, 30, 1, "m", ControlTier.More, "deck").When("deck=true").Tip();
            c.Slider("collapse", 30, 0, 100, 5, "%", ControlTier.More, "deck").When("deck=true").Tip();
            c.Slider("embankment", 0, 0, 4, 0.5, "m", ControlTier.More, "deck").When("deck=true").Tip();
            c.Toggle("markings", true, ControlTier.More, "deck").When("deck=true").Tip();

            c.Slider("padShare", 0, 0, 80, 5, "%", ControlTier.More, "skate").Tip();
            c.Slider("ledges", 0, 0, 20, 1, "count", ControlTier.More, "skate").Tip();
            c.Slider("bowl", 0, 0, 3, 0.25, "m", ControlTier.More, "skate").Tip();
            c.AssetMulti("rampAssets", AssetCategories.SportsSkate, ControlTier.More, "skate");
            c.Asset("padSurface", AssetCategories.SurfacePaving, ControlTier.More, "skate");

            c.Slider("jumpLines", 0, 0, 4, 1, "count", ControlTier.More, "yard").Tip();
            c.Slider("jumpHeight", 1.5, 0.5, 3, 0.25, "m", ControlTier.More, "yard");
            c.Slider("jumpSpacing", 9, 6, 14, 0.5, "m", ControlTier.More, "yard").Tip();
            c.Slider("blocks", 0, 0, 40, 1, "count", ControlTier.More, "yard").Tip();
            c.Slider("blockSpacing", 4.5, 3, 8, 0.5, "m", ControlTier.More, "yard");
            c.Slider("walls", 0, 0, 12, 1, "count", ControlTier.More, "yard").Tip();

            c.Slider("benches", 0, 0, 16, 1, "count", ControlTier.More, "amenities");
            c.Toggle("lamps", false, ControlTier.More, "amenities");
            c.Toggle("fence", true, ControlTier.More, "amenities");
            c.Slider("fenceGaps", 50, 0, 100, 5, "%", ControlTier.More, "amenities").When("fence=true").Tip();

            c.AssetMulti("slabAssets", AssetCategories.RuinsSlab, ControlTier.More, "assets");
            c.AssetMulti("pillarAssets", AssetCategories.RuinsPillar, ControlTier.More, "assets");
            c.AssetMulti("rubbleAssets", AssetCategories.RuinsRubble, ControlTier.More, "assets");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            c.AssetMulti("weedAssets", AssetCategories.RuinsWeeds, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.RandomRange("skateItems", 4, 16);
            c.RandomRange("embankment", 0, 3);
            c.RandomRange("jumpLines", 0, 2);
            c.RandomRange("blocks", 0, 20);
            c.RandomRange("bowl", 0, 1.5);
        }

        protected override void DefinePresets()
        {
            Preset("diySpot", ("subtype", "diySpot"), ("deck", true), ("deckSide", "side"), ("collapse", 20.0), ("padShare", 55.0), ("skateItems", 12.0),
                ("ledges", 6.0), ("bowl", 1.5), ("walls", 2.0), ("jumpLines", 0.0), ("blocks", 0.0), ("graffiti", 75.0), ("decay", 45.0), ("overgrowth", 30.0));
            Preset("highwayRuins", ("subtype", "highwayRuins"), ("deck", true), ("deckSide", "centre"), ("deckWidth", 18.0), ("collapse", 65.0), ("padShare", 0.0),
                ("skateItems", 0.0), ("ledges", 0.0), ("bowl", 0.0), ("embankment", 3.0), ("walls", 0.0), ("graffiti", 40.0), ("decay", 90.0), ("overgrowth", 70.0), ("benches", 0.0), ("lamps", false));
            Preset("graffitiPark", ("subtype", "graffitiPark"), ("deck", true), ("deckSide", "side"), ("collapse", 0.0), ("padShare", 60.0), ("skateItems", 6.0),
                ("ledges", 4.0), ("walls", 10.0), ("graffiti", 100.0), ("decay", 25.0), ("overgrowth", 20.0), ("benches", 10.0));
            Preset("parkour", ("subtype", "parkour"), ("deck", false), ("padShare", 20.0), ("skateItems", 2.0), ("ledges", 0.0), ("blocks", 30.0),
                ("blockSpacing", 4.5), ("walls", 6.0), ("graffiti", 45.0), ("decay", 40.0), ("overgrowth", 25.0));
            Preset("dirtJumps", ("subtype", "dirtJumps"), ("deck", true), ("deckSide", "side"), ("deckWidth", 12.0), ("collapse", 80.0), ("padShare", 0.0),
                ("skateItems", 0.0), ("ledges", 0.0), ("jumpLines", 3.0), ("jumpHeight", 1.75), ("walls", 0.0), ("graffiti", 30.0), ("decay", 60.0), ("overgrowth", 55.0));
            // (rundown, 2026-10-29) the ruins of a building: floor slab, half walls, column stumps, a basement pit with old rainwater
            Preset("buildingRuins", ("subtype", "buildingRuins"), ("deck", false), ("padShare", 0.0), ("skateItems", 0.0), ("ledges", 0.0), ("bowl", 0.0),
                ("jumpLines", 0.0), ("blocks", 0.0), ("walls", 0.0), ("graffiti", 20.0), ("decay", 75.0), ("overgrowth", 50.0), ("benches", 0.0), ("lamps", false),
                ("fence", true), ("fenceGaps", 55.0), ("embankment", 0.0));
        }

        private struct Span { public OrientedRect R; public bool Collapsed, Ramp; }

        /// <summary>Type-specific curated defaults (over GritAssets' shared sets); null = shared only.</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtObjects = null;

        /// <summary>
        /// (rf-sports) The ruins' own ground: weathered (dark) asphalt pads and a cracked, oil-stained fallen carriageway. The
        /// shared gritty default for paving is Concrete Surface 02, which in the game's own catalog is a bright white slab:
        /// the DIY spot read as two white squares on the lawn.
        /// </summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtSurfaces = new Dictionary<string, GritAssets.Set>(System.StringComparer.Ordinal)
        {
            [AssetCategories.SurfacePaving] = new GritAssets.Set(true, "EAI - Surface Pack Concrete Weathered Asphalt Surface|Pavement Surface 01|Concrete Surface 01"),
            [AssetCategories.SurfaceAsphalt] = new GritAssets.Set(true, "EAI - Surface Pack Concrete Cracked Asphalt Surface|Pavement Surface 01|Concrete Surface 01"),
        };

        /// <summary>(rf-sports) Road arrows and a speed number: the fallen carriageway reads as a highway (the marking pool's best were no-parking stripes).</summary>
        private static readonly string[] Arrows = { "EU RoadArrow Forward", "NA RoadArrow Forward" };
        private static readonly string[] SpeedSigns = { "EU LaneMarkings Speed100", "EU LaneMarkings Speed120", "NA LaneMarkings Speed65" };

        // Art pass: whatever the player left automatic gets the curated gritty defaults (GritAssets).
        protected override void PlanCore(LayerContext ctx) { PlanArt(ctx); GritAssets.Apply(ctx.Plan, ctx.Seed, ArtObjects, ArtSurfaces); GritAssets.CapDecals(ctx.Plan, ctx.LayerRegion.Area, 130f); }

        private void PlanArt(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var guard = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var all = new Zones();      // everything laid out (module search avoids it)
            var keep = new Zones();     // pads, modules, ramp: scatters keep out
            var gates = Gates(ctx);
            string sub = ctx.S("subtype");
            // Deck and pads follow the plot edges (a square plot used to get a diagonal highway).
            YardKit.GridFrame(region, out var centre, out var axis, out var side, out float major, out float minor);
            float decay = ctx.F("decay") / 100f, graffiti = ctx.F("graffiti") / 100f, over = ctx.F("overgrowth") / 100f;
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            bool hasGraffiti = graffiti > 0f && AssetAvailability.Has(AssetCategories.DecalGraffiti);

            // ── 1. The highway deck and its spans.
            var spans = new List<Span>();
            OrientedRect deck = default;
            bool hasDeck = false;
            if (ctx.B("deck"))
            {
                float dw = ctx.F("deckWidth");
                var through = centre + side * (ctx.S("deckSide") == "side" ? -minor * 0.22f : 0f);
                if (!region.Contains(through)) through = centre;
                if (LeisureKit.Chord(region, through, axis, 4f, out var d0, out var d1))
                    hasDeck = DecayKit.FitRect(region, occ, math.lerp(d0, d1, 0.5f), math.normalizesafe(d1 - d0), math.distance(d0, d1) * 0.5f, dw * 0.5f, 2f, out deck, 8f, 3f);
                if (!hasDeck) ctx.Issue(IssueSeverity.Info, "Placemaker.Ruins.Issue.NoDeck");
            }
            int rampSpan = -1, breakSpan = -1;
            if (hasDeck)
            {
                float spacing = ctx.F("pillarSpacing");
                int n = math.max(1, (int)math.round(deck.HalfLength * 2f / spacing));
                float len = deck.HalfLength * 2f / n;
                float collapse = ctx.F("collapse") / 100f;
                if (ctx.F("embankment") > 0f && n >= 3)
                {
                    // The on-ramp rises at the end farther from the main entrance and breaks off into the next span.
                    bool atStart = gates.Count == 0 || math.distance(deck.Local(-deck.HalfLength, 0f), gates[0].Position) > math.distance(deck.Local(deck.HalfLength, 0f), gates[0].Position);
                    rampSpan = atStart ? 0 : n - 1;
                    breakSpan = atStart ? 1 : n - 2;
                }
                for (int i = 0; i < n; i++)
                {
                    float u = -deck.HalfLength + (i + 0.5f) * len;
                    uint v = Hashing.Mix(ctx.Seed, 0x5BA4u + (uint)i);
                    var r = new OrientedRect { Center = deck.Local(u, 0f), Axis = deck.Axis, HalfLength = len * 0.5f, HalfWidth = deck.HalfWidth };
                    bool collapsed = i == breakSpan || (i != rampSpan && Hashing.Unit(v) < collapse);
                    spans.Add(new Span { R = r, Collapsed = collapsed, Ramp = i == rampSpan });
                }
            }
            ctx.Check();

            // ── 2. Pads: the DIY spot under runs of intact spans (or one central pad without a deck).
            var pads = new List<OrientedRect>();
            float padShare = ctx.F("padShare") / 100f;
            var padAsset = AssetMix.One(ctx.S("padSurface"));
            if (padShare > 0f)
            {
                if (hasDeck)
                {
                    float extra = minor * padShare * 0.3f;
                    for (int i = 0; i < spans.Count;)
                    {
                        if (spans[i].Collapsed || spans[i].Ramp) { i++; continue; }
                        int j = i;
                        while (j + 1 < spans.Count && !spans[j + 1].Collapsed && !spans[j + 1].Ramp) j++;
                        var c = math.lerp(spans[i].R.Center, spans[j].R.Center, 0.5f);
                        float hl = (j - i + 1) * spans[i].R.HalfLength;
                        if (DecayKit.FitRect(region, occ, c, deck.Axis, hl, deck.HalfWidth + extra, 1.5f, out var pad, 4f, math.max(3f, deck.HalfWidth * 0.6f)))
                            pads.Add(pad);
                        i = j + 1;
                    }
                }
                else
                {
                    float a = region.Area * padShare * 0.5f;
                    float hl = math.sqrt(a * 1.6f) * 0.5f, hw = hl / 1.6f;
                    var deep = region.DeepestPoint(out _, null, 14);
                    if (DecayKit.FitRect(region, occ, deep, axis, hl, hw, 2f, out var pad, 5f, 4f)) pads.Add(pad);
                }
                foreach (var pad in pads)
                {
                    ctx.Surface(pad.Ring, SurfaceRole.Paving, padAsset, AssetCategories.SurfacePaving);
                    all.Add(pad.Ring); keep.Add(pad.Ring);
                }
            }

            // ── 2b. (rundown) The ruins of a building (Building ruins).
            var building = sub == "buildingRuins" ? BuildingRuins.Plan(ctx, region, occ, all, keep, centre, axis, major, minor, decay) : null;
            if (building != null) { var fr = building.Footprint.Ring; all.Add(fr); keep.Add(fr); }

            // ── 3. Spans on the ground: fallen deck pieces (broken asphalt), the ramp.
            var fallen = new List<DecayKit.Slab>();
            var fallenR = new List<(OrientedRect r, DecayKit.Slab s)>();
            string slabCat = null;
            int markings = 0, fallenSlabs = 0;
            bool wantMarkings = ctx.B("markings") && AssetAvailability.Has(AssetCategories.DecalMarkings);
            float embank = ctx.F("embankment");
            for (int i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (!s.Collapsed && !s.Ramp) continue;
                uint v = Hashing.Mix(ctx.Seed, 0xFA11u + (uint)i);
                var shrunk = new OrientedRect { Center = s.R.Center, Axis = s.R.Axis, HalfLength = s.R.HalfLength - 0.6f, HalfWidth = s.R.HalfWidth };
                var slab = DecayKit.BrokenSlab(ctx, shrunk, s.Ramp ? 0.3f : 0.55f + 0.45f * decay, SurfaceRole.Asphalt, AssetMix.One(""), AssetCategories.SurfaceAsphalt, v);
                all.Add(s.R.Ring);
                if (s.Ramp)
                {
                    keep.Add(s.R.Ring);
                    Embankment(ctx, s.R, embank, i < breakSpan);
                    continue;
                }
                fallen.Add(slab);
                fallenR.Add((shrunk, slab));
                // Faded lane markings on the fallen carriageway (dashes on the centre and lane lines).
                if (wantMarkings)
                {
                    // (rf-sports) Faded white lane dashes only from white line pieces (never the general marking pool: its
                    // best-ranked items are yellow no-parking stripes), then one arrow per lane (two lanes each way) and a
                    // speed number now and then, all from the game's own road markings.
                    if (AssetAvailability.Has(AssetCategories.DecalFieldLine))
                        foreach (var w in new[] { 0f, -s.R.HalfWidth * 0.5f, s.R.HalfWidth * 0.5f })
                            for (float u = -s.R.HalfLength + 2f; u < s.R.HalfLength - 1f; u += 6f)
                            {
                                uint mv = Hashing.Mix(v, (uint)(u * 10f + w * 100f + 1000f));
                                if (Hashing.Unit(mv) > 0.85f - 0.5f * decay) continue;
                                if (DecayKit.Decal(ctx, guard, s.R.Local(u, w), s.R.Yaw, AssetCategories.DecalFieldLine, mv)) markings++;
                            }
                    for (int lane = 0; lane < 4; lane++)
                    {
                        float w = (lane - 1.5f) * s.R.HalfWidth * 0.5f;
                        uint mv = Hashing.Mix(v, 0xA770u + (uint)lane);
                        if (Hashing.Unit(mv) > 0.9f - 0.45f * decay) continue;
                        float u = (Hashing.Unit(Hashing.Mix(mv, 1u)) - 0.5f) * s.R.HalfLength * 0.8f;
                        var p = s.R.Local(u, w);
                        if (!guard.Free(p, 0.6f, 0.4f, 0f)) continue;
                        bool speed = Hashing.Unit(Hashing.Mix(mv, 2u)) < 0.25f;
                        ctx.Object(p, s.R.Yaw + (w < 0f ? math.PI : 0f), ObjectRole.Decal, AssetMix.One("", speed ? SpeedSigns : Arrows), mv, -1, AssetCategories.DecalMarkings);
                        markings++;
                    }
                }
            }
            ctx.Check();

            // ── 4. Pillars at the span joints (stumps next to collapsed spans), graffiti on their sides.
            int pillars = 0, graffitiCount = 0;
            if (hasDeck)
            {
                string pillarCat = DecayKit.Cat(ctx, ctx.L("pillarAssets"), AssetCategories.RuinsPillar, AssetCategories.RuinsSlab);
                var vs = deck.HalfWidth >= 6f ? new[] { -deck.HalfWidth + 1.6f, deck.HalfWidth - 1.6f } : new[] { 0f };
                for (int k = 0; k <= spans.Count && pillarCat != null; k++)
                {
                    bool nextToRamp = (k == rampSpan || k == rampSpan + 1) && rampSpan >= 0;
                    if (nextToRamp) continue;
                    bool leftDown = k > 0 && spans[k - 1].Collapsed, rightDown = k < spans.Count && spans[k].Collapsed;
                    float u = -deck.HalfLength + k * deck.HalfLength * 2f / spans.Count;
                    u = math.clamp(u, -deck.HalfLength + 1.6f, deck.HalfLength - 1.6f);
                    foreach (var w in vs)
                    {
                        var p = deck.Local(u, w);
                        uint v = Hashing.Mix(ctx.Seed, 0x9111u + (uint)(k * 7) + (uint)(w > 0 ? 1 : 0));
                        if (!occ.Free(p, 1.3f, 1f, 0.2f)) continue;
                        var o = ctx.Object(p, deck.Yaw, ObjectRole.Furniture, AssetMix.Pick(ctx.L("pillarAssets"), v), v, -1, pillarCat);
                        // Pillars beside a fallen span are broken stumps.
                        o.Scale = leftDown && rightDown ? 0.45f + 0.2f * Hashing.Unit(v) : leftDown || rightDown ? 0.65f + 0.25f * Hashing.Unit(v) : 1f;
                        occ.Take(p, 1.4f, 2);
                        pillars++;
                        if (hasGraffiti)
                            foreach (var dir in new[] { deck.Axis, -deck.Axis, deck.Side, -deck.Side })
                            {
                                uint gv = Hashing.Mix(v, (uint)(dir.x * 10f + 20f) * 3u + (uint)(dir.y * 10f + 20f));
                                if (Hashing.Unit(gv) < graffiti * 0.6f && DecayKit.Decal(ctx, guard, p + dir * 1.9f, math.atan2(dir.x, dir.y), AssetCategories.DecalGraffiti, gv)) graffitiCount++;
                            }
                    }
                }
            }
            ctx.Check();

            // ── 5. Paths from the entrances to the nearest pad (or the deck / the middle).
            foreach (var g in gates)
            {
                float2 target = region.DeepestPoint(out _, null, 12);
                float best = float.MaxValue;
                foreach (var pad in pads)
                {
                    var pr = LayerRegion.From(pad.Ring);
                    if (pr == null) continue;
                    var q = pr.ClosestOnBoundary(g.Position, out _, out _);
                    float d = math.distance(q, g.Position);
                    if (d < best) { best = d; target = q - math.normalizesafe(q - g.Position) * 0.1f; }
                }
                if (pads.Count == 0 && hasDeck)
                {
                    // Beside the deck on the gate's side, level with the gate.
                    float along = math.clamp(math.dot(g.Position - deck.Center, deck.Axis), -deck.HalfLength + 2f, deck.HalfLength - 2f);
                    float sideSign = math.dot(g.Position - deck.Center, deck.Side) >= 0f ? 1f : -1f;
                    target = deck.Local(along, sideSign * (deck.HalfWidth + 2f));
                }
                if (!region.Contains(target)) continue;
                // s9: straight in from the edge, never a leg sideways along it.
                var route = GateApproach.ToPoint(region, g.Position, target, LeisureLayer.ApproachLength(ctx));
                if (route == null) continue;
                for (int i = 1; i < route.Count; i++) LeisureKit.Path(ctx, occ, route[i - 1], route[i], NetRole.Pathway, pathAsset);
            }
            if (ctx.B("fence"))
                DecayKit.BrokenFence(ctx, occ, region, gates, ctx.F("fenceGaps") / 100f, AssetMix.One(ctx.S("fenceAsset"), AbandonedLotLayer.SiteFences), 0xFE7Du);
            ctx.Check();

            // Room for the dirt-jumps feature, on the yard side.
            var yardSide = centre + side * (hasDeck && ctx.S("deckSide") == "side" ? minor * 0.25f : deck.HalfWidth + minor * 0.2f);
            DirtJumps.CarvePlace(ctx, region, occ, all, region.Contains(yardSide) ? yardSide : centre, axis, 2);

            // ── 6. The DIY spot: a dug bowl, skate lines and concrete ledges on the pads.
            float bowlDepth = ctx.F("bowl");
            bool bowl = false;
            if (bowlDepth > 0f)
                foreach (var pad in pads)
                {
                    // Mid-span spots first (clear of the pillar pairs at the joints), then the pad centre.
                    var us = new List<float> { 0f };
                    foreach (var s in spans) if (!s.Collapsed && !s.Ramp) us.Insert(0, math.dot(s.R.Center - pad.Center, pad.Axis));
                    foreach (float R0 in new[] { math.min(pad.HalfWidth - 1.2f, 7f), 5f, 4f })
                    {
                        float R = math.min(R0, pad.HalfWidth - 1.2f);
                        if (R < 4f || pad.HalfLength < R + 1f) continue;
                        foreach (var u in us)
                        {
                            var c = pad.Local(u, 0f);
                            if (math.abs(u) > pad.HalfLength - R - 0.5f || !occ.Free(c, R, 1f, 0.3f)) continue;
                            DirtJumps.Dish(ctx, c, R, bowlDepth);
                            occ.Take(c, R, 2);
                            bowl = true;
                            break;
                        }
                        if (bowl) break;
                    }
                    if (bowl) break;
                }
            int skate = 0, ledges = 0;
            string rampCat = ctx.I("skateItems") > 0 && pads.Count > 0 ? DecayKit.Cat(ctx, ctx.L("rampAssets"), AssetCategories.SportsSkate) : null;
            if (rampCat != null || (ctx.I("ledges") > 0 && pads.Count > 0))
            {
                float padArea = 0f;
                foreach (var pad in pads) padArea += pad.HalfLength * pad.HalfWidth;
                for (int pi = 0; pi < pads.Count; pi++)
                {
                    var pad = pads[pi];
                    float share = pad.HalfLength * pad.HalfWidth / math.max(1f, padArea);
                    // (rf-sports) A pad is filled to its size (about one obstacle per 100 m2 of pad): a 120 m pad with twelve
                    // obstacles read as an empty concrete slab.
                    int items = rampCat == null ? 0 : math.max((int)math.ceil(ctx.I("skateItems") * share), ctx.I("skateItems") > 0 && sub != "parkour" ? (int)(pad.HalfLength * pad.HalfWidth * 4f / 100f) : 0);
                    int ledgeWanted = (int)math.ceil(ctx.I("ledges") * share);
                    skate += SkateLines(ctx, occ, pad, items, ctx.L("rampAssets"), rampCat, Hashing.Mix(ctx.Seed, 0x5CA0u + (uint)pi));
                    if (ledgeWanted > 0)
                    {
                        slabCat = slabCat ?? DecayKit.Cat(ctx, ctx.L("slabAssets"), AssetCategories.RuinsSlab, AssetCategories.RuinsWall);
                        if (slabCat != null) ledges += Ledges(ctx, occ, pad, ledgeWanted, ctx.L("slabAssets"), slabCat, Hashing.Mix(ctx.Seed, 0x1ED0u + (uint)pi));
                    }
                }
            }
            ctx.Check();

            // ── 7. The yard: modules in subtype priority, in free space on the far side of the deck.
            var far = centre + side * (hasDeck && ctx.S("deckSide") == "side" ? minor * 0.25f : 0f);
            if (hasDeck && ctx.S("deckSide") != "side") far = centre + side * (deck.HalfWidth + minor * 0.2f);
            if (!region.Contains(far)) far = centre;
            int jumps = 0, blocks = 0, walls = 0;
            var order = sub == "parkour" ? new[] { "blocks", "walls", "jumps" } : sub == "graffitiPark" ? new[] { "walls", "blocks", "jumps" } : new[] { "jumps", "blocks", "walls" };
            foreach (var m in order)
            {
                if (m == "jumps" && ctx.I("jumpLines") > 0)
                {
                    float spacing = ctx.F("jumpSpacing");
                    int maxJumps = math.clamp((int)((major * 0.55f - 17f) / spacing), 2, 6);
                    jumps = DirtJumps.PlaceLines(ctx, region, occ, all, far, axis, ctx.I("jumpLines"), maxJumps, ctx.F("jumpHeight"), spacing, true, true, 3, Hashing.Mix(ctx.Seed, 0xB3Au));   // (rf-sports) 3 = dirt (1 was dry grass: the track was invisible)
                    if (jumps == 0) ctx.Issue(IssueSeverity.Info, "Placemaker.Ruins.Issue.Fewer", 0, ctx.I("jumpLines"));
                }
                else if (m == "blocks" && ctx.I("blocks") > 0)
                    blocks = ParkourField(ctx, region, occ, all, keep, guard, far, axis, ctx.I("blocks"), ctx.F("blockSpacing"), graffiti);
                else if (m == "walls" && ctx.I("walls") > 0)
                    walls = WallRow(ctx, region, occ, all, keep, guard, far, axis, ctx.I("walls"), math.max(graffiti, sub == "graffitiPark" ? 0.6f : 0f), ref graffitiCount);
            }
            ctx.Check();

            // ── 8. Collapse debris: tilted slabs and rubble on the fallen spans.
            string rubbleCat = decay > 0f && spans.Exists(s => s.Collapsed) ? DecayKit.Cat(ctx, ctx.L("rubbleAssets"), AssetCategories.RuinsRubble, AssetCategories.Rock) : null;
            int rubbleItems = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (!s.Collapsed) continue;
                uint v = Hashing.Mix(ctx.Seed, 0xC011u + (uint)i);
                slabCat = slabCat ?? DecayKit.Cat(ctx, ctx.L("slabAssets"), AssetCategories.RuinsSlab, AssetCategories.RuinsWall);
                if (slabCat != null)
                    for (int k = 0; k < 2 + (int)(decay * 2f); k++)
                    {
                        uint kv = Hashing.Mix(v, (uint)k);
                        var p = s.R.Local((Hashing.Unit(kv) - 0.5f) * s.R.HalfLength * 1.4f, (Hashing.Unit(Hashing.Mix(kv)) - 0.5f) * s.R.HalfWidth * 1.2f);
                        if (!occ.Free(p, 1.8f, 1f, 0.2f)) continue;
                        var o = ctx.Object(p, s.R.Yaw + (Hashing.Unit(Hashing.Mix(kv, 2u)) - 0.5f) * 0.9f, ObjectRole.Furniture, AssetMix.Pick(ctx.L("slabAssets"), kv), kv, -1, slabCat);
                        o.Scale = 1.2f + 0.7f * Hashing.Unit(Hashing.Mix(kv, 3u));
                        occ.Take(p, 1.8f, 2);
                        fallenSlabs++;
                    }
                // (rf-sports) The carriageway's own furniture, so the fallen deck reads as a highway: broken runs of jersey
                // barriers along both edges. (No abandoned vehicles: the game has no vehicle props to stand in.)
                for (int e = -1; e <= 1; e += 2)
                    for (float u = -s.R.HalfLength + 1.5f; u < s.R.HalfLength - 1f; u += 3.2f)
                    {
                        uint bv = Hashing.Mix(v, 0xBA00u + (uint)((u + 100f) * 10f) + (uint)(e + 1) * 7919u);
                        if (Hashing.Unit(bv) < 0.25f + 0.45f * decay) continue;
                        var p = s.R.Local(u, e * (s.R.HalfWidth - 0.9f));
                        if (!region.ContainsDisc(p, 1f) || !occ.Free(p, 1f, 0.6f, 0.1f)) continue;
                        float tilt = (Hashing.Unit(Hashing.Mix(bv, 1u)) - 0.5f) * 0.5f * decay;
                        ctx.Object(p, s.R.Yaw + math.PI * 0.5f + tilt, ObjectRole.Furniture, AssetMix.One("", "ConcreteBarrier01", "ConcreteBarrier02"), bv, -1, AssetCategories.ScrapBarrier);
                        occ.Take(p, 1f, 2);
                    }
                if (rubbleCat != null)
                {
                    var res = DecayKit.RubbleField(ctx, occ, guard, region, s.R.Center, math.max(s.R.HalfLength, s.R.HalfWidth) * 1.3f, math.min(1f, decay * 1.1f),
                        ctx.L("rubbleAssets"), rubbleCat, 1f, 0x2B2u + (uint)i * 41u, p => keep.Inside(p, 0.8f), 160);
                    rubbleItems += res.Piles + res.Chunks;
                }
            }
            ctx.Check();

            // ── 9. Spectators and work lamps along the pads.
            int benches = 0;
            if (ctx.I("benches") > 0 && pads.Count > 0)
            {
                int perPad = (int)math.ceil(ctx.I("benches") / (float)pads.Count);
                for (int pi = 0; pi < pads.Count && benches < ctx.I("benches"); pi++)
                {
                    var pad = pads[pi];
                    int placed = 0;
                    for (int k = 0; k < perPad * 3 && placed < perPad && benches < ctx.I("benches"); k++)
                    {
                        float sgn = (k & 1) == 0 ? 1f : -1f;
                        float u = (((k / 2) + 0.5f) / math.max(1, (perPad + 1) / 2) - 0.5f) * pad.HalfLength * 1.6f;
                        var p = pad.Local(u, sgn * (pad.HalfWidth + 1.9f));
                        if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, pad.Local(u, 0f)), ObjectRole.Bench, AssetMix.One(""), null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE70u + (uint)k + (uint)pi * 50u)))
                        { placed++; benches++; }
                    }
                }
            }
            if (ctx.B("lamps"))
                foreach (var pad in pads)
                    for (float u = -pad.HalfLength + 3f; u <= pad.HalfLength - 3f; u += 14f)
                        foreach (var sgn in new[] { -1f, 1f })
                        {
                            var p = pad.Local(u, sgn * (pad.HalfWidth + 0.9f));
                            LeisureKit.Put(ctx, occ, p, 0f, ObjectRole.Lamp, AssetMix.One(""), null, 0.4f, Hashing.Mix(ctx.Seed, (int)(p.x * 3f), (int)(p.y * 3f)), 0.3f, 0.1f);
                        }
            ctx.Check();

            // ── 10. Overgrowth reclaiming the edges and the fallen deck; weeds in its cracks.
            string weedCat = over > 0f ? DecayKit.Cat(ctx, ctx.L("weedAssets"), AssetCategories.RuinsWeeds, AssetCategories.Bush) : null;
            int crackWeeds = 0;
            foreach (var slab in fallen) crackWeeds += DecayKit.CrackWeeds(ctx, occ, slab.Cracks, over, ctx.L("weedAssets"), weedCat, 0xC3Bu + (uint)crackWeeds);
            if (building != null)
            {
                // weeds up through the floor and a few young trees inside the walls (birch and poplar seed into ruins first)
                crackWeeds += DecayKit.CrackWeeds(ctx, occ, building.Floor.Cracks, math.saturate(over * 1.2f), ctx.L("weedAssets"), weedCat, 0xC3Cu);
                int saplings = 1 + (int)(over * 4f);
                for (int k = 0, tries = 0; k < saplings && tries < 40; tries++)
                {
                    uint v = Hashing.Mix(ctx.Seed, 0x5A91u + (uint)tries);
                    var fp = building.Footprint;
                    var p = fp.Local((Hashing.Unit(v) - 0.5f) * fp.HalfLength * 1.7f, (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * fp.HalfWidth * 1.7f);
                    if (building.Pit != null && Geometry.Poly.Contains(building.Pit, p)) continue;
                    if (!occ.Free(p, 2f, 0.3f, 0.1f)) continue;
                    var t = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Tree, (ctx.L("treeSpecies") != null && ctx.L("treeSpecies").Length > 0 ? AssetMix.Pick(ctx.L("treeSpecies"), v) : AssetMix.Pick(new[] { "BirchTree01", "EU_PoplarTree01", "EU_AlderTree01" }, v)), v, 0, AssetCategories.Tree);
                    occ.Take(p, 2f, 2);
                    k++;
                }
            }
            var (weeds, trees) = DecayKit.Overgrowth(ctx, occ, region, over, 4f + 18f * over, ctx.L("weedAssets"), weedCat, ctx.L("treeSpecies"), 0x0F7u,
                p => keep.Inside(p, 1.2f), 16f);
            ctx.Check();

            // ── 11. Wear on the pads: cracks and graffiti along their edges.
            foreach (var pad in pads)
            {
                if (hasGraffiti)
                    foreach (var sgn in new[] { -1f, 1f })
                        graffitiCount += DecayKit.GraffitiAlong(ctx, guard, pad.Local(-pad.HalfLength + 1f, sgn * (pad.HalfWidth - 0.6f)), pad.Local(pad.HalfLength - 1f, sgn * (pad.HalfWidth - 0.6f)),
                            -sgn * pad.Side, graffiti * 0.35f, 0x6B0u + (uint)(sgn + 2f) + (uint)(pad.Center.x * 7f), 4f);
                if (AssetAvailability.Has(AssetCategories.DecalCracks) && decay > 0f)
                {
                    int cracks = (int)(pad.HalfLength * pad.HalfWidth * 4f / 60f * decay);
                    for (int k = 0; k < cracks && k < 80; k++)
                    {
                        uint v = Hashing.Mix(ctx.Seed, 0xC4A0u + (uint)k + (uint)(pad.Center.x * 13f));
                        var p = pad.Local((Hashing.Unit(v) - 0.5f) * pad.HalfLength * 1.8f, (Hashing.Unit(Hashing.Mix(v)) - 0.5f) * pad.HalfWidth * 1.8f);
                        DecayKit.Decal(ctx, guard, p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, AssetCategories.DecalCracks, v);
                    }
                }
            }

            // ── 12. (rundown) The designed ground (library decals): old concrete, weedy cracks, lane lines, rubble, stagnant water.
            UrbanRuinsGround.Dress(ctx, sub, decay, over, pads, fallenR, building);

            EmitGreenRegions(ctx, occ, 60f);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.SkateFeatures", skate + ledges + blocks, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Pillars", pillars, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Jumps", jumps, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.RubblePiles", rubbleItems + fallenSlabs, Keys.UnitCount);
            ctx.Plan.Facts["spans"] = spans.Count;
            ctx.Plan.Facts["collapsedSpans"] = spans.FindAll(s => s.Collapsed).Count;
            ctx.Plan.Facts["pads"] = pads.Count;
            ctx.Plan.Facts["pillars"] = pillars;
            ctx.Plan.Facts["skate"] = skate;
            ctx.Plan.Facts["ledges"] = ledges;
            ctx.Plan.Facts["bowl"] = bowl ? 1 : 0;
            ctx.Plan.Facts["jumps"] = jumps;
            ctx.Plan.Facts["blocks"] = blocks;
            ctx.Plan.Facts["walls"] = walls;
            ctx.Plan.Facts["graffiti"] = graffitiCount;
            ctx.Plan.Facts["markings"] = markings;
            ctx.Plan.Facts["fallenSlabs"] = fallenSlabs;
            ctx.Plan.Facts["weeds"] = weeds + crackWeeds;
            ctx.Plan.Facts["youngTrees"] = trees;
            ctx.Plan.Facts["benches"] = benches;
        }

        /// <summary>Earth on-ramp rising towards the break: nested raise rings, the top ring at full height at the broken end.</summary>
        private static void Embankment(LayerContext ctx, OrientedRect span, float height, bool breakAtHighU)
        {
            const int steps = 4;
            float dir = breakAtHighU ? 1f : -1f;
            float end = dir * (span.HalfLength - 0.5f);
            float start = -dir * (span.HalfLength - 3f);
            for (int k = 1; k <= steps; k++)
            {
                float s = math.lerp(start, end, (k - 1) / (float)steps * 0.85f);
                float a = math.min(s, end), b = math.max(s, end);
                var r = new OrientedRect { Center = span.Local((a + b) * 0.5f, 0f), Axis = span.Axis, HalfLength = (b - a) * 0.5f, HalfWidth = span.HalfWidth - 0.5f - (k - 1) * 0.6f };
                if (r.HalfLength < 1f || r.HalfWidth < 1f) break;
                float baseH = ctx.H(span.Local(end, 0f));
                ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = Geometry.Poly.To3D(r.Ring, ctx.H), Kind = TerraformKind.Raise, TargetHeight = baseH + height * k / steps, Permanent = true,
                });
            }
            DirtJumps.WarnPermanent(ctx);
        }

        /// <summary>Skate lines along the pad: ramps at both ends facing in, rails/boxes between. Returns the count.</summary>
        private static readonly string[] SkateEnds = { "SkateParkRamp01", "SkateParkRamp02", "SkateParkRamp03", "SkateParkRamp04" };
        private static readonly string[] SkateMiddles = { "SkateParkBox01", "SkateParkRail01", "SkateParkBox02", "SkateParkRail02" };

        private static int SkateLines(LayerContext ctx, Occupancy occ, OrientedRect pad, int items, string[] ids, string cat, uint seed)
        {
            if (cat == null || items <= 0) return 0;
            // (rf-sports) One skate line per ~11 m of pad width (up to four), evenly across it.
            int nl = pad.HalfWidth >= 5f ? math.clamp((int)math.round(pad.HalfWidth * 2f / 11f), 2, 4) : 1;
            var lines = new float[nl];
            for (int i = 0; i < nl; i++) lines[i] = nl == 1 ? 0f : -pad.HalfWidth * 0.8f + pad.HalfWidth * 1.6f * i / (nl - 1);
            bool auto = ids == null || ids.Length == 0 || System.Array.TrueForAll(ids, string.IsNullOrEmpty);
            int n = 0;
            // (rf-sports) A big pad gets the game's half-pipe (14 x 5 m) as its centrepiece; the lines run round it.
            if (auto && pad.HalfLength >= 12f && pad.HalfWidth >= 7f
                && LeisureKit.Put(ctx, occ, pad.Center, pad.Yaw + math.PI * 0.5f, ObjectRole.Furniture, AssetMix.One("", "SkateParkHalfPipe01", "SkateParkHalfPipe02"), cat, 7.2f,
                    Hashing.Mix(seed, 0x4A1Fu), 0.4f, 0.4f, 2)) n++;
            int perLine = (int)math.ceil(items / (float)lines.Length);
            for (int li = 0; li < lines.Length; li++)
            {
                float w = lines[li];
                int slots = math.max(1, perLine);
                float step = (pad.HalfLength * 2f - 4f) / math.max(1, slots);
                for (int k = 0; k < slots && n < items; k++)
                {
                    float u = -pad.HalfLength + 2f + step * (k + 0.5f);
                    bool end = k == 0 || k == slots - 1;
                    float yaw = pad.Yaw + (k < slots / 2 ? 0f : math.PI);
                    if (!end) yaw = pad.Yaw + ((k + li) % 2 == 0 ? 0f : math.PI * 0.5f) * 0.25f;
                    uint v = Hashing.Mix(seed, (uint)(li * 64 + k));
                    // (rf-sports) Automatic skate props are one uniform prop per build (every obstacle the same box): a line
                    // gets ramps or a half-pipe at its ends and boxes and rails between, from the game's own skate set.
                    var asset = auto ? AssetMix.One("", end ? SkateEnds[(int)(v % (uint)SkateEnds.Length)] : SkateMiddles[(int)(v % (uint)SkateMiddles.Length)])
                        : AssetMix.Pick(ids, v);
                    foreach (var du in new[] { 0f, 1.5f, -1.5f })
                        if (LeisureKit.Put(ctx, occ, pad.Local(u + du, w), yaw, ObjectRole.Furniture, asset, cat, end && auto ? 2.9f : 1.8f, v, 0.4f, 0.4f, 2))
                        { n++; break; }
                }
            }
            return n;
        }

        /// <summary>Concrete-block ledges along the pad's middle line, parallel to the axis.</summary>
        private static int Ledges(LayerContext ctx, Occupancy occ, OrientedRect pad, int count, string[] ids, string cat, uint seed)
        {
            int n = 0;
            float step = (pad.HalfLength * 2f - 3f) / math.max(1, count);
            for (int k = 0; k < count; k++)
            {
                float u = -pad.HalfLength + 1.5f + step * (k + 0.5f);
                uint v = Hashing.Mix(seed, (uint)k);
                float w = pad.HalfWidth >= 5f ? 0f : pad.HalfWidth * 0.5f;
                foreach (var dw in new[] { 0f, 1.2f, -1.2f })
                    if (LeisureKit.Put(ctx, occ, pad.Local(u, w + dw), pad.Yaw + math.PI * 0.5f, ObjectRole.Furniture, AssetMix.Pick(ids, v), cat, 1.1f, v, 0.4f, 0.3f, 2))
                    { n++; break; }
            }
            return n;
        }

        /// <summary>Parkour field: a lattice of concrete blocks of varied size on gravel, a few gaps for runs. Returns blocks placed.</summary>
        private static int ParkourField(LayerContext ctx, LayerRegion region, Occupancy occ, Zones all, Zones keep, Occupancy guard, float2 near, float2 axis,
            int blocks, float spacing, float graffiti)
        {
            string cat = DecayKit.Cat(ctx, ctx.L("slabAssets"), AssetCategories.RuinsSlab, AssetCategories.RuinsWall, AssetCategories.Rock);
            if (cat == null) return 0;
            for (int attempt = 0; attempt < 4 && blocks >= 3; attempt++, blocks = (int)(blocks * 0.65f))
            {
                int cols = (int)math.ceil(math.sqrt(blocks * 1.5f)), rows = (int)math.ceil(blocks / (float)cols);
                float hl = cols * spacing * 0.5f + 1.5f, hw = rows * spacing * 0.5f + 1.5f;
                if (!DecayKit.FindRect(region, occ, all, near, axis, hl, hw, 2f, Hashing.Mix(ctx.Seed, 0x9A4Cu + (uint)attempt), out var r, 120)) continue;
                ctx.Surface(r.Ring, SurfaceRole.Ground, AssetMix.One(""), AssetCategories.SurfaceNatural);
                all.Add(r.Ring); keep.Add(r.Ring);
                int n = 0;
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < cols && n < blocks; i++)
                    {
                        uint v = Hashing.Mix(ctx.Seed, 0x9A50 + i, j);
                        if (Hashing.Unit(v) < 0.12f) continue;
                        var jit = (new float2(Hashing.Unit(Hashing.Mix(v, 1u)), Hashing.Unit(Hashing.Mix(v, 2u))) - 0.5f) * spacing * 0.2f;
                        var p = r.Local(-hl + 1.5f + spacing * (i + 0.5f) + jit.x, -hw + 1.5f + spacing * (j + 0.5f) + jit.y);
                        float yaw = r.Yaw + (((i + j) & 1) == 0 ? 0f : math.PI * 0.5f) + (Hashing.Unit(Hashing.Mix(v, 3u)) - 0.5f) * 0.3f;
                        if (!occ.Free(p, 1f, 0.5f, 0.2f)) continue;
                        var o = ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(ctx.L("slabAssets"), v), v, -1, cat);
                        o.Scale = 0.75f + 0.6f * Hashing.Unit(Hashing.Mix(v, 4u));
                        occ.Take(p, 1f, 2);
                        n++;
                        if (Hashing.Unit(Hashing.Mix(v, 5u)) < graffiti * 0.3f && AssetAvailability.Has(AssetCategories.DecalGraffiti))
                            DecayKit.Decal(ctx, guard, p + r.Side * 1.4f, yaw, AssetCategories.DecalGraffiti, Hashing.Mix(v, 6u));
                    }
                return n;
            }
            ctx.Issue(IssueSeverity.Info, "Placemaker.Ruins.Issue.Fewer", 0, blocks);
            return 0;
        }

        /// <summary>Legal graffiti walls in a zig-zag row on old concrete, painted on both sides. Returns walls placed.</summary>
        private static int WallRow(LayerContext ctx, LayerRegion region, Occupancy occ, Zones all, Zones keep, Occupancy guard, float2 near, float2 axis,
            int walls, float graffiti, ref int graffitiCount)
        {
            string cat = DecayKit.Cat(ctx, null, AssetCategories.RuinsWall, AssetCategories.RuinsSlab);
            if (cat == null) return 0;
            for (int attempt = 0; attempt < 4 && walls >= 1; attempt++, walls = math.max(1, (int)(walls * 0.6f)))
            {
                float hl = walls * 2.6f + 1.5f, hw = 3.5f;
                if (!DecayKit.FindRect(region, occ, all, near, axis, hl, hw, 1.5f, Hashing.Mix(ctx.Seed, 0x3A1Bu + (uint)attempt), out var r, 120))
                {
                    if (walls == 1) break;
                    continue;
                }
                ctx.Surface(r.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                all.Add(r.Ring); keep.Add(r.Ring);
                int n = 0;
                for (int k = 0; k < walls; k++)
                {
                    var p = r.Local(-hl + 1.5f + 2.6f + k * 5.2f, 0f);
                    float tilt = (k & 1) == 0 ? 0.45f : -0.45f;
                    float yaw = r.Yaw + math.PI * 0.5f + tilt;
                    uint v = Hashing.Mix(ctx.Seed, 0x3A20u + (uint)k);
                    if (!occ.Free(p, 1.2f, 0.5f, 0.2f)) continue;
                    ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.One(""), v, -1, cat);
                    occ.Take(p, 1.4f, 2);
                    n++;
                    var face = new float2(math.sin(yaw + math.PI * 0.5f), math.cos(yaw + math.PI * 0.5f));
                    foreach (var sgn in new[] { -1f, 1f })
                        if (Hashing.Unit(Hashing.Mix(v, (uint)(sgn + 3f))) < graffiti && AssetAvailability.Has(AssetCategories.DecalGraffiti)
                            && DecayKit.Decal(ctx, guard, p + face * sgn * 1.3f, yaw, AssetCategories.DecalGraffiti, Hashing.Mix(v, (uint)(sgn + 7f)))) graffitiCount++;
                }
                return n;
            }
            ctx.Issue(IssueSeverity.Info, "Placemaker.Ruins.Issue.Fewer", 0, walls);
            return 0;
        }
    }
}
