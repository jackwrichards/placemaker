// Animal park: a visitor attraction built around fenced enclosures — a petting farm of small pens,
// grazing paddocks along a central walk, a horse paddock with a riding arena, a wooded deer park
// with a pond, or an aviary garden. Composition:
//   a visitor hub by the main entrance (barn, kiosk, picnic tables; its open middle is the "core"
//   for the playground / picnic / farm stall features);
//   circulation first — a loop walk inset from the edge, a central spine, or a meandering trail;
//   enclosures from the space the walks leave (the inside of the loop, the two sides of the spine,
//   the pieces between trails), cut into pens across their long axis with fenced alleys between,
//   kept clear of the duck pond and the riding arena;
//   each pen: fence (walk-in gate for petting pens), shelter at the back (far from the walk),
//   trough by the gate, hay by the shelter, worn ground, shade trees or woodland, animal spawners;
//   viewpoints on the walk facing the pens, aviaries beside the trails, trees outside the pens.
// Pens are "place:enclosure" regions so features never land among the animals.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Types.UrbanFarm;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.AnimalPark
{
    [AreaGenerator(71, Group = "farms")]
    public sealed class AnimalParkLayer : LeisureLayer
    {
        public const string LayerId = "animalPark";
        public const string EnclosureTag = "place:enclosure";

        protected override string Area => "AnimalPark";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Animal.svg";
        public override float MinArea => 1500f;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag, EnclosureTag };
        protected override int WantedEntrances(LayerRegion region) => region.Area > 30000f ? 2 : 1;

        private const int MaxPens = 12;
        /// <summary>Depth of a petting pen from its walk (m); the rest of the enclosure space is meadow.</summary>
        private const float PettingPenDepth = 20f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            CoreFeature("playground"),
            CoreFeature("picnic"),
            CoreFeature(Farmstead.FarmStandAddOn.LayerId),
            Feature("restrooms"),
            Feature("shops", ("count", 1.0), ("where", "entrances")),
            StreetShopsFeature(Area),
            Feature(Farmstead.ApiaryAddOn.LayerId, ("hives", 4.0)),
            Feature("decals", ("theme", "dirt"), ("density", 0.6)),   // art pass: 2.0 scattered ~170 dirt decals per 1.2 ha
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("kind", "petting", ControlTier.Essential, "enclosures", "petting", "paddocks", "horses", "deer", "aviary").Tip();
            c.Slider("pens", 5, 1, 10, 1, "count", ControlTier.Essential, "enclosures").Tip();
            c.Toggle("shelters", true, ControlTier.Essential, "enclosures").Tip();
            c.Toggle("walkIn", true, ControlTier.More, "enclosures").Tip();
            c.Toggle("troughs", true, ControlTier.More, "enclosures");
            c.Toggle("hay", true, ControlTier.More, "enclosures");
            c.Toggle("arena", false, ControlTier.More, "enclosures").Tip();
            c.Slider("jumps", 6, 0, 10, 1, "count", ControlTier.More, "enclosures").When("arena=true");
            c.Slider("aviaries", 0, 0, 12, 1, "count", ControlTier.More, "enclosures").Tip();   // vanilla aviaries are gazebos: none unless asked (rf-farms)
            c.Toggle("animals", true, ControlTier.Essential, "animals").Tip();
            c.Slider("animalsPerPen", 2, 1, 6, 1, "count", ControlTier.More, "animals").When("animals=true");
            c.Toggle("pond", true, ControlTier.More, "animals").Tip();
            c.Slider("pondSize", 16, 10, 40, 2, "m", ControlTier.More, "animals").Layout(8).When("pond=true");
            c.Choice("layout", "loop", ControlTier.Essential, "visitors", "loop", "spine", "trail").Tip();
            c.Toggle("hub", true, ControlTier.Essential, "visitors").Tip();
            c.Slider("viewpoints", 3, 0, 8, 1, "count", ControlTier.More, "visitors").Tip();
            c.Slider("benchSpacing", 35, 0, 80, 5, "m", ControlTier.More, "visitors").Layout(5).Tip();
            c.Toggle("lamps", false, ControlTier.More, "visitors");
            c.Slider("shadeTrees", 20, 0, 60, 5, "perHa", ControlTier.More, "planting").Tip();
            c.Slider("woodland", 0, 0, 60, 5, "%", ControlTier.More, "planting").Tip();
            c.Toggle("hedgerow", true, ControlTier.More, "planting").Tip();
            c.Choice("boundary", "fence", ControlTier.More, "grounds", "none", "fence");
            c.Toggle("wornGround", true, ControlTier.More, "grounds").Tip();
            c.Slider("groundLayer", 1, 1, 4, 1, "count", ControlTier.More, "grounds").Tip().When("wornGround=true");
            c.Choice("terrain", "none", ControlTier.More, "grounds", "none", "mounds", "flatten").Tip();
            c.Asset("penFenceAsset", AssetCategories.Fence, ControlTier.More, "assets");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "assets");
            c.Asset("shelterAsset", AssetCategories.AnimalParkShelter, ControlTier.More, "assets");
            c.Asset("troughAsset", AssetCategories.AnimalParkTrough, ControlTier.More, "assets");
            c.Asset("hayAsset", AssetCategories.AnimalParkHay, ControlTier.More, "assets");
            c.Asset("aviaryAsset", AssetCategories.AnimalParkAviary, ControlTier.More, "assets");
            c.Asset("spawnerAsset", BuildCategories.AnimalSpawner, ControlTier.More, "assets");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            c.RandomRange("pens", 2, 7);
            c.RandomRange("aviaries", 0, 1);
            c.RandomRange("woodland", 0, 30);
            c.Fixed("terrain");
            c.Fixed("wornGround");
            c.Fixed("groundLayer");
            c.Chance("arena", 0.25f);
            c.Chance("lamps", 0.25f);
        }

        protected override void DefinePresets()
        {
            Preset("pettingFarm", ("kind", "petting"), ("pens", 6.0), ("layout", "loop"), ("walkIn", true), ("aviaries", 0.0), ("animalsPerPen", 2.0),
                ("pond", true), ("pondSize", 20.0), ("viewpoints", 2.0), ("benchSpacing", 30.0), ("shadeTrees", 20.0), ("hedgerow", true));
            Preset("paddocks", ("kind", "paddocks"), ("pens", 4.0), ("layout", "spine"), ("walkIn", false), ("aviaries", 0.0), ("animalsPerPen", 3.0),
                ("pond", false), ("viewpoints", 4.0), ("benchSpacing", 50.0), ("shadeTrees", 15.0), ("hedgerow", true));
            Preset("horsePaddock", ("kind", "horses"), ("pens", 3.0), ("layout", "loop"), ("walkIn", false), ("arena", true), ("jumps", 6.0),
                ("aviaries", 0.0), ("animalsPerPen", 2.0), ("pond", false), ("viewpoints", 2.0), ("shadeTrees", 10.0), ("hedgerow", false));
            Preset("deerPark", ("kind", "deer"), ("pens", 1.0), ("layout", "trail"), ("walkIn", false), ("troughs", false), ("aviaries", 0.0),
                ("animalsPerPen", 5.0), ("pond", true), ("pondSize", 24.0), ("viewpoints", 3.0), ("woodland", 40.0), ("shadeTrees", 0.0),
                ("hedgerow", false), ("terrain", "mounds"));
            Preset("aviaryGarden", ("kind", "aviary"), ("pens", 2.0), ("layout", "trail"), ("walkIn", false), ("aviaries", 10.0), ("animalsPerPen", 1.0),
                ("pond", true), ("pondSize", 18.0), ("viewpoints", 2.0), ("benchSpacing", 25.0), ("lamps", true), ("shadeTrees", 30.0), ("hedgerow", true));
        }

        private sealed class Pen
        {
            public float2[] Ring;
            public LayerRegion Region;
            public bool HasPond;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            string kind = ctx.S("kind"), layout = ctx.S("layout");
            float hw = Composer.DefaultHalfWidth(NetRole.Pathway);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out float major, out float minor);
            // Squarish sites: the principal axis is an arbitrary diagonal — run the spine straight in from the entrance.
            if (gates.Count > 0 && major < minor * 1.25f)
            {
                axis = region.InwardNormal(gates[0].Segment);
                side = new float2(-axis.y, axis.x);
            }
            Lawn.Cover(ctx, region.Ring, AssetMix.One("Grass Surface 01"));

            // 1. Boundary: a natural hedge (hedgerow on) or a tall wooden fence — never the automatic brick wall.
            bool hedgerow = ctx.B("hedgerow");
            if (ctx.S("boundary") == "fence" || hedgerow) Fence(ctx, gates, AnimalParkArt.Boundary(ctx.S("fenceAsset"), hedgerow), 2.5f);

            // 2. Visitor hub by the main entrance (its open middle is the features' core).
            bool hubOn = ctx.B("hub") || ctx.Input.HasChildTarget(RegionTags.Core);
            float hubR = math.clamp(math.sqrt(region.Area) * 0.09f, 10f, 18f);
            float2 hubC = default; float2[] hubRing = null;
            if (hubOn && gates.Count > 0)
            {
                var inward = region.InwardNormal(gates[0].Segment);
                hubC = gates[0].Position + inward * (hubR + 2f);
                if (!region.ContainsDisc(hubC, hubR * 0.6f)) hubC = region.DeepestPoint(out _, null, 12);
                hubRing = FarmKit.Largest(FarmKit.Clip(LayerRegion.Circle(hubC, hubR, 20), region.Ring, 30f));
                if (hubRing == null) hubOn = false;
            }
            else hubOn = false;
            ctx.Check();

            // 3. Visitor circulation and the space it leaves for enclosures.
            var walks = new List<(float2 a, float2 b)>();
            var interior = new List<float2[]>();
            float edgeKeep = ctx.B("hedgerow") ? 4f : 2.5f;
            if (layout == "trail")
            {
                NaturalPaths(ctx, occ, gates, region.Area > 25000f ? 2 : 1, 5f, 30f, "narrow", pathAsset, region.Area > 20000f);
                foreach (var s in occ.Paths.Segments) if (s.Kind != Occupancy.KindReserved) walks.Add((s.A, s.B));
            }
            else
            {
                if (layout == "loop")
                {
                    float loopInset = math.clamp(minor * 0.12f, 9f, 18f);
                    float2[] loop = null;
                    try { loop = FarmKit.Largest(Poly.Offset(region.Ring, loopInset)); } catch (Exception) { }
                    if (loop != null && Poly.Area(loop) > 600f)
                    {
                        loop = Poly.Simplify(loop, 1.5f);
                        for (int i = 0; i < loop.Length; i++)
                        {
                            var a = loop[i]; var b = loop[(i + 1) % loop.Length];
                            if (math.distance(a, b) < 1f) continue;
                            LeisureKit.Path(ctx, occ, a, b, NetRole.Pathway, pathAsset);
                            walks.Add((a, b));
                        }
                        try { interior.AddRange(Poly.Offset(region.Ring, loopInset + hw + 2.5f)); } catch (Exception) { }
                    }
                }
                if (walks.Count == 0)
                {
                    // Spine (also the fallback when the area is too narrow for a loop).
                    foreach (var (a, b) in FarmKit.Chords(region, centre, axis, 2f))
                    {
                        LeisureKit.Path(ctx, occ, a, b, NetRole.Pathway, pathAsset);
                        walks.Add((a, b));
                    }
                    float2[] inner = null;
                    try { inner = FarmKit.Largest(Poly.Offset(region.Ring, edgeKeep)); } catch (Exception) { }
                    if (inner != null)
                        foreach (var half in FarmKit.SplitByLines(inner, new List<(float2, float2)> { (centre, axis) }, 8, ctx.Cancel))
                            try { interior.AddRange(Poly.Offset(half, hw + 2.5f)); } catch (Exception) { }
                }
                ConnectGates(ctx, occ, gates, walks, NetRole.Pathway, pathAsset);
                // Entrance paths and the hub cut through the enclosure space.
                for (int i = 0; i < occ.Paths.Count; i++)
                {
                    var s = occ.Paths.Segments[i];
                    if (s.Kind == Occupancy.KindReserved) continue;
                    interior = FarmKit.Carve(interior, FarmKit.SegmentRect(s.A, s.B, s.HalfWidth + 2.5f), 150f);
                }
                if (hubOn) interior = FarmKit.Carve(interior, LayerRegion.Circle(hubC, hubR + 2.5f, 20), 150f);
            }
            ctx.Check();

            // 3b. Hedgerow inside the outline, then aviaries beside the walks (both claim space before the pens).
            if (hedgerow)
            {
                // The hedge itself is the boundary net; loose wild shrubs in front of it give it depth
                // (was ~190 clipped hedge pieces in a row).
                FarmKit.Edge(ctx, occ, region, 2.2f, 6.5f, gates, 4.5f, AssetCategories.Bush, AnimalParkArt.HedgerowShrubs, 1.0f, 0xAE06u);
            }
            var hubAt = hubC; float hubKeep = hubOn ? hubR + 2.5f : 0f;
            bool InHub(float2 p, float r) => hubKeep > 0f && math.distancesq(p, hubAt) < (hubKeep + r) * (hubKeep + r);
            var aviarySpots = Aviaries(ctx, occ, InHub, kind);
            if (layout == "trail")
                interior = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 2.5f) || occ.Discs.Overlaps(p, 1f, 1.5f) || occ.HitsPolygon(p, 1f) ||
                    region.BoundaryDistance(p, edgeKeep + 1f) < edgeKeep || InHub(p, 0f), 3f, 250f, ctx.Cancel);
            else
                foreach (var a in aviarySpots) interior = FarmKit.Carve(interior, LayerRegion.Circle(a, AviaryRadius + 1.5f, 12), 150f);
            interior.Sort((a, b) => Poly.Area(b).CompareTo(Poly.Area(a)));
            ctx.Check();

            // 4. Pond (inside the biggest enclosure space) and riding arena, before the pens are cut.
            float2 pondC = default; float pondR = 0f;
            if (ctx.B("pond") && interior.Count > 0)
            {
                pondR = ctx.F("pondSize") * 0.5f;
                var host = LayerRegion.From(interior[0]);
                if (host != null)
                {
                    var c = host.DeepestPoint(out float clear, (q, cap) => occ.Clearance(q, cap), 14);
                    if (clear < pondR + 4f) pondR = math.max(0f, clear - 4f);
                    if (pondR >= 4f && region.BoundaryDistance(c, pondR + 9f) >= pondR + 8f) pondC = c;
                    else pondR = 0f;
                }
            }
            OrientedRect arena = default; bool hasArena = false;
            if (ctx.B("arena") && interior.Count > 0)
            {
                var host = LayerRegion.From(interior[0]);
                LeisureKit.Frame(host ?? region, out _, out var hax, out _, out _, out _);
                foreach (var size in new[] { new float2(40f, 20f), new float2(30f, 16f), new float2(22f, 12f) })
                {
                    var found = FarmKit.PackRows(host, occ, hax, size.x, size.y, 0f, 0f, 1, 3f, false);
                    if (found.Count == 0) continue;
                    arena = found[0]; hasArena = true; break;
                }
                if (hasArena)
                {
                    var grown = new OrientedRect { Center = arena.Center, Axis = arena.Axis, HalfLength = arena.HalfLength + 3f, HalfWidth = arena.HalfWidth + 3f };
                    interior = FarmKit.Carve(interior, grown.Ring, 150f);
                    interior.Sort((a, b) => Poly.Area(b).CompareTo(Poly.Area(a)));
                }
            }

            // 5. Pens: share the wanted count out by area, cut each space across its long axis.
            var pens = CutPens(ctx, interior, math.clamp(ctx.I("pens"), 1, MaxPens), pondR > 0f ? pondC : (float2?)null, pondR, kind);
            if (pens.Count == 0) ctx.Issue(IssueSeverity.Warning, "Placemaker.AnimalPark.Issue.NoPens");
            var penSet = new FarmKit.RingSet();
            for (int i = 0; i < pens.Count; i++) { penSet.Add(pens[i].Ring); ctx.AddRegion(pens[i].Ring, "pen:" + i, EnclosureTag); }
            ctx.Check();

            // 6. Terrain (pond basin kept out of the shaping) and the pond itself.
            string terrain = ctx.S("terrain");
            if (terrain != "none")
            {
                var pc = pondC; float keep = pondR + 8f;
                TerrainShaper.Shape(ctx, region, new TerrainSpec
                {
                    Mode = terrain == "flatten" ? TerrainMode.Flatten : TerrainMode.Mounds, Height = 2.5f, Scale = 30f, MaxSlopePct = 20f, Falloff = 16f,
                    Seed = Hashing.Mix(ctx.Seed, 0x40ADu), Skip = keep > 8f ? (Func<float2, bool>)(q => math.distancesq(q, pc) < keep * keep) : null,
                });
            }
            if (pondR > 0f)
            {
                var lake = LeisureKit.Blob(pondC, pondR, Hashing.Mix(ctx.Seed, 0x9071u), 16, 0.18f);
                ParkLayer.EmitLake(ctx, lake, 1.5f);
                occ.Take(pondC, pondR + 1f, 6);
                ctx.Plan.Facts["pondArea"] = Poly.Area(lake);
            }

            // 7. Hub, arena and the pens' contents.
            if (hubOn) Hub(ctx, occ, hubC, hubR, gates[0].Position);
            if (hasArena) Arena(ctx, occ, arena);
            int spawners = Furnish(ctx, occ, pens, kind);
            ctx.Check();

            // 8. Along the walks: viewpoints facing the pens, aviaries, benches, lamps.
            Viewpoints(ctx, occ, pens, kind);
            int aviaries = aviarySpots.Count;
            if (ctx.F("benchSpacing") > 0f) FarmKit.BesidePaths(ctx, occ, ctx.F("benchSpacing"), ObjectRole.Bench, Bench(ctx, kind), 1.3f, 0xBE4Cu);
            if (ctx.B("lamps")) FarmKit.BesidePaths(ctx, occ, 26f, ObjectRole.Lamp, AssetMix.One(""), 0.5f, 0x1A4Bu);
            if (hubOn) occ.Take(hubC, hubR * 0.55f, 9);   // keep the hub's middle for the features
            ctx.Check();

            // 9. Shade trees outside the pens.
            var trees = ctx.L("treeSpecies");
            if (trees.Length == 0) trees = AnimalParkArt.Trees(kind);
            float outside = math.max(0f, region.Area - SumArea(pens));
            int wanted = (int)(outside / 10000f * ctx.F("shadeTrees"));
            if (wanted > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, trees, Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, occ, region, wanted, ObjectRole.Tree, trees, 2.4f, 18f, 0.4f, 0x7EE5u, p => penSet.Contains(p, 2.5f), 1, 3);

            // 10. Regions for the features.
            EmitRegions(ctx, occ, penSet, hubOn ? hubRing : null);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Enclosures", pens.Count, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Aviaries", aviaries, Keys.UnitCount);
            ctx.Plan.Facts["arena"] = hasArena ? 1 : 0;
        }

        /// <summary>Rustic campground benches on the farm walks, garden benches in the aviary garden.</summary>
        private static AssetRef Bench(LayerContext ctx, string kind)
        {
            string chosen = ctx.S("benchAsset");
            if (!string.IsNullOrEmpty(chosen)) return AssetMix.One(chosen);
            return kind == "aviary" ? AssetMix.One("GardenBench02", "GardenBench01") : AssetMix.One("CampgroundBench01", "GardenBench01");
        }

        private static float SumArea(List<Pen> pens)
        {
            float a = 0f;
            foreach (var p in pens) a += p.Region.Area;
            return a;
        }

        // ───────────── pens ─────────────

        private static List<Pen> CutPens(LayerContext ctx, List<float2[]> spaces, int wanted, float2? pond, float pondR, string kind)
        {
            var pens = new List<Pen>();
            float minPen = kind == "petting" || kind == "aviary" ? 80f : 200f;
            var usable = new List<float2[]>();
            float total = 0f;
            foreach (var s in spaces) if (Poly.Area(s) >= minPen) { usable.Add(s); total += Poly.Area(s); }
            if (usable.Count == 0) return pens;
            if (usable.Count > wanted) usable.RemoveRange(wanted, usable.Count - wanted);
            total = 0f; foreach (var s in usable) total += Poly.Area(s);
            int left = wanted;
            for (int si = 0; si < usable.Count && pens.Count < MaxPens; si++)
            {
                var space = usable[si];
                int k = si == usable.Count - 1 ? left : math.clamp((int)math.round(wanted * Poly.Area(space) / total), 1, math.max(1, left - (usable.Count - 1 - si)));
                k = math.max(1, math.min(k, (int)(Poly.Area(space) / minPen)));
                left -= k;
                var reg = LayerRegion.From(space);
                if (reg == null) continue;
                reg.PrincipalAxes(out var o, out var ax, out float spanMajor, out float spanMinor);
                // Squarish spaces have an arbitrary (often diagonal) principal axis: cut square to the longest edge
                // instead, so pens line up with the walks and the outline.
                if (spanMajor < spanMinor * 1.25f)
                {
                    float longest = 0f;
                    for (int e = 0; e < space.Length; e++)
                    {
                        var d = space[(e + 1) % space.Length] - space[e];
                        if (math.length(d) > longest) { longest = math.length(d); ax = math.normalizesafe(d, ax); }
                    }
                }
                var sd = FarmKit.Perp(ax);
                var span = FarmKit.Span(space, o, ax);
                var lines = new List<(float2, float2)>();
                // A squarish space cut into 4+ pens becomes two rows of pens either side of a fenced
                // alley, not a stack of thin strips (art pass: 60 x 10 m "pens").
                var cross = FarmKit.Span(space, o, sd);
                bool twoRows = k >= 4 && spanMajor < spanMinor * 1.25f && cross.y - cross.x > 36f;
                int strips = twoRows ? (k + 1) / 2 : k;
                if (twoRows)
                {
                    float m = (cross.x + cross.y) * 0.5f;
                    if (pond.HasValue && Poly.Contains(space, pond.Value))
                    {
                        float pm = math.dot(pond.Value - o, sd);
                        if (math.abs(m - pm) < pondR + 4f) m = pm + math.sign(m - pm + 1e-3f) * (pondR + 4f);
                    }
                    lines.Add((o + sd * m, ax));
                }
                for (int j = 1; j < strips; j++)
                {
                    float t = math.lerp(span.x, span.y, j / (float)strips);
                    // Never cut through the pond: shift the line clear of it.
                    if (pond.HasValue && Poly.Contains(space, pond.Value))
                    {
                        float pt = math.dot(pond.Value - o, ax);
                        if (math.abs(t - pt) < pondR + 4f) t = pt + math.sign(t - pt + 1e-3f) * (pondR + 4f);
                    }
                    lines.Add((o + ax * t, sd));
                }
                // Petting pens line the walk, a pen deep; the middle of the space stays a farm meadow (pond, trees,
                // the features) instead of six 40 x 60 m bare pens filling the whole site (rf-farms).
                float2[] meadow = null;
                if (kind == "petting")
                {
                    try { meadow = FarmKit.Largest(Poly.Offset(space, PettingPenDepth)); } catch (Exception) { }
                    if (meadow != null && Poly.Area(meadow) < 400f) meadow = null;
                }
                foreach (var whole in FarmKit.SplitByLines(space, lines, 32, ctx.Cancel))
                {
                    var cell = whole;
                    if (meadow != null)
                    {
                        float2[] best = null;
                        try
                        {
                            foreach (var d in Poly.Difference(cell, meadow))
                                foreach (var r in Poly.CutHoles(d))
                                    if (best == null || Poly.Area(r) > Poly.Area(best)) best = r;
                        }
                        catch (Exception) { best = null; }
                        if (best == null) continue;
                        cell = best;
                    }
                    List<float2[]> inset;
                    try { inset = Poly.Offset(cell, 1.3f); } catch (Exception) { continue; }
                    var ring = FarmKit.Largest(inset);
                    if (ring == null || Poly.Area(ring) < minPen * 0.6f) continue;
                    ring = Poly.Clean(ring, 0.3f);
                    var pr = ring.Length >= 3 ? LayerRegion.From(ring) : null;
                    if (pr == null) continue;
                    pens.Add(new Pen { Ring = ring, Region = pr, HasPond = pond.HasValue && pr.Contains(pond.Value) });
                    if (pens.Count >= MaxPens) break;
                }
            }
            return pens;
        }

        private static string[] SpawnerFallbacks(string kind)
        {
            switch (kind)
            {
                case "horses": return new[] { "Horse Spawner", "Farm Animal Spawner" };
                case "deer": return new[] { "Forest Animal Spawner", "Deer Spawner" };
                case "aviary": return new[] { "Bird Spawner", "Duck Spawner", "Farm Animal Spawner" };
                case "paddocks": return new[] { "Cow Spawner", "Sheep Spawner", "Farm Animal Spawner" };
                default: return new[] { "Farm Animal Spawner", "Goat Spawner", "Sheep Spawner", "Pig Spawner", "Chicken Spawner" };
            }
        }

        private static int Furnish(LayerContext ctx, Occupancy occ, List<Pen> pens, string kind)
        {
            if (pens.Count == 0) return 0;
            var penFence = AnimalParkArt.PenFence(ctx.S("penFenceAsset"), kind);
            bool walkIn = ctx.B("walkIn");
            // Installed animal-park props first, else sized vanilla stand-ins (no 19 m barns in a pen, no crates as hay).
            var (shelterAsset, shelterCat) = AnimalParkArt.Prop(ctx.S("shelterAsset"), AssetCategories.AnimalParkShelter, AnimalParkArt.ShelterStandIn(kind), AssetCategories.Shelter);
            if (!ctx.B("shelters")) shelterCat = null;
            // Troughs have no vanilla stand-in: placed only when installed or picked, without a "missing" note.
            string troughCat = ctx.B("troughs") && (!string.IsNullOrEmpty(ctx.S("troughAsset")) || AssetAvailability.Has(AssetCategories.AnimalParkTrough)) ? AssetCategories.AnimalParkTrough : null;
            var (hayAsset, hayCat) = AnimalParkArt.Prop(ctx.S("hayAsset"), AssetCategories.AnimalParkHay, AnimalParkArt.Hay, AssetCategories.FarmHay);
            if (!ctx.B("hay")) hayCat = null;
            bool worn = ctx.B("wornGround");
            int layer = math.clamp(ctx.I("groundLayer"), 1, 4);
            var spawnerAsset = new AssetRef(ctx.S("spawnerAsset"), SpawnerFallbacks(kind));
            int perPen = ctx.B("animals") ? math.clamp(ctx.I("animalsPerPen"), 1, 6) : 0;
            float woodland = ctx.F("woodland") / 100f, shade = ctx.F("shadeTrees");
            var trees = ctx.L("treeSpecies");
            if (trees.Length == 0) trees = AnimalParkArt.Trees(kind);
            bool treesOk = woodland > 0f || shade > 0f;
            int spawners = 0, patches = 0;
            float shelterR = kind == "petting" || kind == "aviary" ? 2.2f : 3.2f;
            for (int i = 0; i < pens.Count; i++)
            {
                ctx.Check();
                var pen = pens[i]; var pr = pen.Region;
                uint v = Hashing.Mix(ctx.Seed, 0x9E40u + (uint)i);
                var centroid = pr.Centroid;

                // Gate: the pen edge closest to a walk.
                float bestD = float.MaxValue; float2 gate = pr.Ring[0]; int gateSeg = 0;
                for (int e = 0; e < pr.Ring.Length; e++)
                {
                    var a = pr.Ring[e]; var b = pr.Ring[(e + 1) % pr.Ring.Length];
                    float len = math.distance(a, b);
                    int steps = math.max(1, (int)(len / 3f));
                    for (int s = 0; s <= steps; s++)
                    {
                        var q = math.lerp(a, b, s / (float)steps);
                        float d = occ.Paths.Count > 0 ? occ.Paths.Clearance(q, 80f) : 0f;
                        if (d < bestD - 0.01f && len > 4f && s > 0 && s < steps) { bestD = d; gate = q; gateSeg = e; }
                    }
                }
                var penGates = new List<Gate> { new Gate { Position = gate, Segment = gateSeg } };
                bool openGate = walkIn && (kind == "petting" || kind == "aviary");
                foreach (var run in Fences.Runs(pr, openGate ? penGates : null, 1.4f))
                {
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, penFence);
                    occ.TakeCorridor(run.c0, run.c1, 0.45f, 5);
                }
                var inward = math.normalizesafe(centroid - gate, pr.InwardNormal(gateSeg));

                // Ground: worked earth in small pens, worn patches by the gate in paddocks.
                // Straw-bedded petting pens (mud, then sand without the farming pack).
                // Without a straw / mud surface pack the vanilla fallback was grey "Sand Surface 02" slabs (rf-farms): then
                // the pen is grass worn to earth by terrain paint instead.
                if (kind == "petting")
                {
                    if (AssetAvailability.Has(AssetCategories.SurfaceDirt))
                        ctx.Surface(pen.Ring, SurfaceRole.Decorative, AssetMix.One(AnimalParkArt.Straw, "EAI - Surface Pack Ground Mud Surface"), AssetCategories.SurfaceDirt);
                    else if (worn && patches < 40) { TerrainPaint.Patch(ctx, pen.Ring, layer, 0.55f, 1.5f); patches++; }
                }
                if (worn && patches < 40)
                {
                    var patch = LeisureKit.Blob(gate + inward * 4f, math.min(5f, math.sqrt(pr.Area) * 0.25f), Hashing.Mix(v, 5u), 12, 0.25f);
                    var clipped = FarmKit.Largest(FarmKit.Clip(patch, pen.Ring, 6f));
                    if (clipped != null) { TerrainPaint.Patch(ctx, clipped, layer, 0.8f, 1.2f); patches++; }
                }

                // Shelter at the back (the pen point farthest from any walk), facing the pen.
                float2 back = centroid;
                if (shelterCat != null)
                {
                    float2[] ins = null;
                    try { ins = FarmKit.Largest(Poly.Offset(pen.Ring, shelterR + 1.2f)); } catch (Exception) { }
                    if (ins != null)
                    {
                        float far = -1f;
                        foreach (var q in ins)
                        {
                            float d = occ.Paths.Count > 0 ? occ.Paths.Clearance(q, 200f) : math.distance(q, gate);
                            if (d > far && occ.Free(q, shelterR, 0.3f, 0.2f)) { far = d; back = q; }
                        }
                        if (far >= 0f)
                            LeisureKit.Put(ctx, occ, back, LeisureKit.Yaw(back, centroid), ObjectRole.Furniture, shelterAsset, shelterCat,
                                shelterR, Hashing.Mix(v, 1u), 0.3f, 0.2f);
                    }
                }
                if (troughCat != null)
                {
                    var along = FarmKit.Perp(inward);
                    foreach (var p in new[] { gate + inward * 2.6f + along * 2.2f, gate + inward * 3.5f - along * 2.2f, centroid })
                        if (pr.ContainsDisc(p, 1f) && LeisureKit.Put(ctx, occ, p, FarmKit.Yaw(along), ObjectRole.Furniture, AssetMix.One(ctx.S("troughAsset")), troughCat,
                            0.8f, Hashing.Mix(v, 2u), 0.3f, 0.2f)) break;
                }
                if (hayCat != null)
                    for (int k = 0; k < 3; k++)
                    {
                        var p = back + math.normalizesafe(centroid - back) * (shelterR + 1.6f) + FarmKit.Perp(math.normalizesafe(centroid - back)) * ((k - 1) * 1.8f);
                        if (pr.ContainsDisc(p, 0.8f)) LeisureKit.Put(ctx, occ, p, Hashing.Unit(Hashing.Mix(v, 9u + (uint)k)) * 6.283f, ObjectRole.Furniture, hayAsset, hayCat, 0.9f, Hashing.Mix(v, 3u + (uint)k), 0.3f, 0.1f);
                    }

                // Trees: woodland pasture (deer) or a few shade trees.
                if (treesOk)
                {
                    int count = (int)(pr.Area / 10000f * (shade + woodland * 140f));
                    var penRegion = pr;
                    if (count > 0)
                        LeisureKit.Plant(ctx, occ, pr, count, ObjectRole.Tree, trees, 2.4f, woodland > 0.2f ? 22f : 12f, woodland > 0.2f ? 0.25f : 0.5f,
                            0x7AE0u + (uint)i * 7u, p => !penRegion.ContainsDisc(p, 2.6f), 1, 3);
                }

                // Animals: spawners spread over the open middle (they need no footprint).
                if (perPen > 0)
                {
                    var c = pr.DeepestPoint(out float clear, (q, cap) => occ.Clearance(q, cap), 10);
                    for (int k = 0; k < perPen; k++)
                    {
                        float a = k * 2.39996f + Hashing.Unit(v) * 6.283f;
                        var p = k == 0 ? c : c + new float2(math.cos(a), math.sin(a)) * math.min(clear * 0.7f, 3f + k * 2f);
                        if (!pr.ContainsDisc(p, 1.5f)) p = c;
                        ctx.Object(p, a, ObjectRole.AnimalSpawner, spawnerAsset, Hashing.Mix(v, 40u + (uint)k));
                        spawners++;
                    }
                }
            }
            return spawners;
        }

        // ───────────── hub, arena, viewpoints, aviaries ─────────────

        private static void Hub(LayerContext ctx, Occupancy occ, float2 c, float r, float2 gate)
        {
            var region = ctx.LayerRegion;
            var yard = FarmKit.Largest(FarmKit.Clip(LeisureKit.Blob(c, r * 0.8f, Hashing.Mix(ctx.Seed, 0x4B0u), 16, 0.12f), region.Ring, 20f));
            if (yard != null)
                ctx.Surface(yard, SurfaceRole.Ground, AssetMix.One(AnimalParkArt.Yard, "Pavement Surface 02"), Placemaker.Core.Build.SurfaceCategories.Gravel);
            var toGate = math.normalizesafe(gate - c, new float2(0, -1));
            var side = FarmKit.Perp(toGate);
            uint v = Hashing.Mix(ctx.Seed, 0x4B1u);
            // Barn at the back of the hub, kiosk and picnic tables on the sides, bins by the way in.
            // Barn at the back of the hub when a real one fits (vanilla barns are 15-20 m), else a farm shed.
            var back = c - toGate * (r * 0.72f);
            bool barn = r >= 16f && LeisureKit.Put(ctx, occ, c - toGate * (r - 9.5f), LeisureKit.Yaw(c - toGate, c), ObjectRole.Furniture,
                AssetMix.One("Barn03", "Barn01"), AssetCategories.CityFarmBarn, 9.5f, v, 0.5f, 0.2f);
            if (!barn)
                LeisureKit.Put(ctx, occ, back, LeisureKit.Yaw(c - toGate, c), ObjectRole.Furniture, AssetMix.One("ToolShed03", "FirewoodShelter02"), AssetCategories.GardenShed,
                    2.6f, v, 0.5f, 0.2f);
            LeisureKit.Put(ctx, occ, c + side * (r * 0.72f), LeisureKit.Yaw(c + side, c), ObjectRole.Furniture, AssetMix.One("Kiosk01", "Kiosk02"), AssetCategories.Kiosk, 2.4f, Hashing.Mix(v, 1u), 0.5f, 0.2f);
            for (int k = 0; k < 3; k++)
            {
                var p = c - side * (r * 0.7f) + toGate * ((k - 1) * 3.4f);
                LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, c), ObjectRole.Furniture, AnimalParkArt.PicnicTable, AssetCategories.CafeSeating, 1.4f, Hashing.Mix(v, 2u + (uint)k), 0.5f, 0.2f);
            }
            for (int k = -1; k <= 1; k += 2)
                LeisureKit.Put(ctx, occ, c + toGate * (r * 0.75f) + side * (k * 3.2f), 0f, ObjectRole.Bin, AssetMix.One(""), null, 0.5f, Hashing.Mix(v, 9u + (uint)(k + 1)), 0.5f, 0.2f);
        }

        private static void Arena(LayerContext ctx, Occupancy occ, OrientedRect a)
        {
            ctx.Surface(a.Ring, SurfaceRole.Ground, AssetMix.One(AnimalParkArt.ArenaSand, "Sand Surface 01"), AssetCategories.SurfaceSand);
            // Rail fence round the arena; the gate on the short end nearest a walk.
            var fenceRegion = LayerRegion.From(new OrientedRect { Center = a.Center, Axis = a.Axis, HalfLength = a.HalfLength + 0.4f, HalfWidth = a.HalfWidth + 0.4f }.Ring);
            if (fenceRegion != null)
            {
                var e0 = a.Local(-a.HalfLength - 0.4f, 0f); var e1 = a.Local(a.HalfLength + 0.4f, 0f);
                float d0 = occ.Paths.Count > 0 ? occ.Paths.Clearance(e0, 200f) : 0f, d1 = occ.Paths.Count > 0 ? occ.Paths.Clearance(e1, 200f) : 1f;
                var gateAt = d0 <= d1 ? e0 : e1;
                fenceRegion.ClosestOnBoundary(gateAt, out int seg, out float t);
                var gates = new List<Gate> { new Gate { Position = gateAt, Segment = seg, T = t } };
                foreach (var run in Fences.Runs(fenceRegion, gates, 2f))
                {
                    ctx.StraightNet(run.c0, run.c1, NetRole.Fence, AnimalParkArt.PenFence(ctx.S("penFenceAsset"), "horses"));
                    occ.TakeCorridor(run.c0, run.c1, 0.45f, 5);
                }
            }
            int jumps = ctx.I("jumps");
            // Show jumps: installed ones, else the dog-agility jump fence (a 1.6 m rail jump).
            var (jumpAsset, jumpCat) = AnimalParkArt.Prop("", AssetCategories.AnimalParkJump, AnimalParkArt.Jump, AssetCategories.DogAgility);
            if (jumps > 0)
                for (int k = 0; k < jumps; k++)
                {
                    // Two lines of jumps down the long sides, a figure-of-eight course.
                    int perSide = (jumps + 1) / 2;
                    float u = math.lerp(-a.HalfLength + 5f, a.HalfLength - 5f, perSide == 1 ? 0.5f : (k / 2) / (float)(perSide - 1));
                    float w = (k & 1) == 0 ? a.HalfWidth * 0.45f : -a.HalfWidth * 0.45f;
                    LeisureKit.Put(ctx, occ, a.Local(u, w), a.Yaw + math.PI * 0.5f, ObjectRole.Furniture, jumpAsset, jumpCat, 1.6f,
                        Hashing.Mix(ctx.Seed, 0x7A3Cu + (uint)k), 0.3f, 0.3f);
                }
            ctx.AddRegion(a.Ring, "arena", EnclosureTag);
        }

        private static void Viewpoints(LayerContext ctx, Occupancy occ, List<Pen> pens, string kind)
        {
            int wanted = ctx.I("viewpoints");
            if (wanted <= 0 || occ.Paths.Count == 0 || pens.Count == 0) return;
            var order = new List<Pen>(pens);
            order.Sort((a, b) => b.Region.Area.CompareTo(a.Region.Area));
            var bench = Bench(ctx, kind);
            // The deer park hide is a small open shelter: the catalog's "lookouts" are 30-55 m towers.
            int placed = 0; bool lookout = kind == "deer";
            for (int i = 0; placed < wanted && i < order.Count * 3; i++)
            {
                var pen = order[i % order.Count];
                // The walk point nearest the pen (rotating round the pen on repeat visits).
                var probe = i < order.Count ? pen.Region.Centroid : pen.Ring[(i * 7) % pen.Ring.Length];
                if (occ.Paths.Nearest(probe, 200f, out var onPath) < 0) continue;
                var toPen = math.normalizesafe(probe - onPath);
                if (lookout)
                {
                    var lp = onPath + toPen * -1f * (Composer.DefaultHalfWidth(NetRole.Pathway) + 3.4f);
                    if (LeisureKit.Put(ctx, occ, lp, LeisureKit.Yaw(lp, probe), ObjectRole.Furniture, AnimalParkArt.Hide, AssetCategories.Shelter, 2.6f,
                        Hashing.Mix(ctx.Seed, 0x100Cu + (uint)i), 0.5f, 0.3f)) { placed++; lookout = false; continue; }
                }
                var side = FarmKit.Perp(toPen);
                foreach (float s in new[] { -1f, 1f })
                {
                    // Benches on the far side of the walk, facing over it into the pen.
                    var p = onPath - toPen * (Composer.DefaultHalfWidth(NetRole.Pathway) + 1.6f) + side * (s * 1.6f);
                    if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, probe), ObjectRole.Bench, bench, null, 1.3f, Hashing.Mix(ctx.Seed, 0x1E0u + (uint)(i * 2) + (s > 0 ? 1u : 0u)), 0.4f, 0.2f))
                        if (s > 0) placed++;
                }
            }
        }

        public const float AviaryRadius = 3.2f;

        private static List<float2> Aviaries(LayerContext ctx, Occupancy occ, Func<float2, float, bool> avoid, string kind)
        {
            int wanted = ctx.I("aviaries");
            var spots = new List<float2>();
            if (wanted <= 0) return spots;
            // Installed aviaries, else an open garden pavilion (bird spawners live in the aviary pens).
            var (asset, aviaryCat) = AnimalParkArt.Prop(ctx.S("aviaryAsset"), AssetCategories.AnimalParkAviary, AnimalParkArt.Aviary, AssetCategories.Shelter);
            float r = AviaryRadius;
            bool flowers = kind == "aviary" && AssetAvailability.Has(AssetCategories.Flowerbed);
            int placed = 0, k = 0;
            float spacing = 18f;
            for (int pass = 0; pass < 2 && placed < wanted; pass++, spacing *= 0.6f)
                foreach (var w in FarmKit.Walk(occ, spacing, 0.5f + pass * 0.25f))
                {
                    if (placed >= wanted) break;
                    k++;
                    var onPath = w.onPath; var t = w.tangent;
                    var n = FarmKit.Perp(t);
                    {
                        float sg = (k & 1) == 0 ? 1f : -1f;
                        var p = onPath + n * sg * (w.halfWidth + r + 0.6f);
                        if (avoid(p, r) || !occ.Free(p, r, 0.8f, 0.3f)) p = onPath - n * sg * (w.halfWidth + r + 0.6f);
                        if (avoid(p, r) || !occ.Free(p, r, 0.8f, 0.3f)) continue;
                        uint v = Hashing.Mix(ctx.Seed, 0xA71Au + (uint)k);
                        ctx.Object(p, LeisureKit.Yaw(p, onPath), ObjectRole.Furniture, asset, v, -1, aviaryCat);
                        occ.Take(p, r, 1);
                        placed++; spots.Add(p);
                        if (!flowers) continue;
                        foreach (float fs in new[] { -1f, 1f })
                        {
                            var q = p + t * fs * (r + 1.2f);
                            if (avoid(q, 1f) || !occ.Free(q, 0.7f, 0.5f, 0.1f)) continue;
                            ctx.Object(q, 0f, ObjectRole.Planter, AssetMix.One(""), Hashing.Mix(v, fs > 0 ? 1u : 2u), -1, AssetCategories.Flowerbed);
                            occ.Take(q, 0.7f, 1);
                        }
                    }
                }
            return spots;
        }

        // ───────────── regions ─────────────

        private static void EmitRegions(LayerContext ctx, Occupancy occ, FarmKit.RingSet pens, float2[] hub)
        {
            var region = ctx.LayerRegion;
            if (hub != null) ctx.AddRegion(hub, "core:0", RegionTags.Core, RegionTags.Green);
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f) || pens.Contains(p, 0.5f), 2.5f, 150f, ctx.Cancel);
            for (int i = 0; i < pieces.Count && i < 24; i++)
                if (hub == null && i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);
            if (hub == null && pieces.Count == 0) ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core);
        }
    }
}
