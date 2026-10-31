// Beach: a shore laid out in bands measured from the REAL waterline (s20 rewrite of the s13 art pass).
//
//   survey     ShoreGround first: the real waterline (terrain below the water level next to water, not the coarse
//              snapshot or the drawn outline), the bank kind along the shore (low / bank / cliff) and the slope
//              classes. Cliff stretches stay natural and get a cliff-top walk with lookouts instead of sand; a shore
//              that is mostly cliff says so (Design issue) and builds only that.
//   ground     one continuous sculpted surface (sloped patches, no terraces): the beach face at "Beach slope" from
//              ~20 m under the water up to the back of the sand, a graded bank (≤ ~25 %) up to the original ground
//              behind, low smooth dune ridges parallel to the shore (no noise lumps); cut/fill limited
//   sand       terrain paint at full strength from under the water to the back of the sand, fading inland
//   promenade  ONE walk parallel to the water on a level bench (profile smoothed and grade-limited along it); short
//              perpendicular accesses graded down to level landings on the sand; entrances join it on constant-grade ramps
//   sand use   umbrella sets in loose rows, lifeguard posts, courts, boats, rocks / tide pools on a rocky shore
//   behind     huts and kiosks on level pads (or skipped), benches facing the sea, lamps in rhythm, dunes with marram
//              grass, a lawn or woods
// Everything stands on the final ground (Resnap after emission); furniture never on slopes steeper than 12 %.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    [AreaGenerator(70, Group = "water")]
    public sealed partial class BeachLayer : LeisureLayer
    {
        public static readonly string[] Styles = { "sandy", "rocky", "river", "cove", "volleyball", "shingle", "mulch", "granite", "ravine", "marsh", "harbour", "wild" };
        private static readonly List<Gate> NoGates = new List<Gate>();
        /// <summary>How far past the outline the sand may reach to find the water (m).</summary>
        public const float ForeshoreReach = Shoreline.MaxGap;
        /// <summary>Default reach of the sand and the graded slope under the water (m).</summary>
        public const float IntoWater = 20f;
        private const float LandingDepth = 4f, LandingHalfWidth = 2.75f;

        protected override string Area => "Beach";
        public override string Id => "beach";
        public override string Icon => "Media/Game/Icons/CityBeach.svg";
        public override float MinArea => 1500f;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            // Area-wide features run on the dry land only ("core" = the largest free piece of shore.Land):
            // on the whole outline they planted picnic shelters and trees in the water (art pass a3).
            Feature(BeachBarAddOn.LayerId), Feature("restrooms"), Feature("shops", ("count", 2.0), ("where", "paths")), StreetShopsFeature(Area),
            CoreFeature("playground", ("size", "small")), CoreFeature("picnic"), CoreFeature("rocks", ("size", "medium")),
            CoreFeature("bigtrees"), CoreFeature("decals", ("theme", "dirt")),
        };

        /// <summary>Restrooms only: picnic shelters and dirt decals made the sand read as a lawn (art pass a3).</summary>
        protected override string[] DefaultFeatures => new[] { "restrooms" };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "sandy", ControlTier.Essential, "beach", Styles).Tip();
            c.Choice("waterSide", "auto", ControlTier.Essential, "beach", Shoreline.Sides).Tip();
            c.Slider("beachDepth", 60, 12, 80, 2, "m", ControlTier.Essential, "beach").Layout(8).Tip();
            c.Slider("sandBlend", 14, 2, 40, 1, "m", ControlTier.Essential, "beach").Tip();
            c.Slider("umbrellas", 50, 0, 160, 5, "perHa", ControlTier.Essential, "beach").Tip();
            c.Choice("layout", "scattered", ControlTier.Essential, "beach", "scattered", "rows").Tip();

            c.Toggle("carve", true, ControlTier.More, "shore").Tip();
            c.Slider("slope", 5, 2, 15, 1, "%", ControlTier.More, "shore").When("carve=true").Tip();
            c.Slider("wetBand", 6, 2, 15, 1, "m", ControlTier.More, "shore").Tip();
            c.Toggle("sandReach", true, ControlTier.More, "shore").Tip();
            c.Toggle("sandSurface", false, ControlTier.More, "shore").Tip();
            c.Asset("sandAsset", AssetCategories.SurfaceSand, ControlTier.More, "shore").When("sandSurface=true");
            c.Toggle("paint", true, ControlTier.More, "shore").Tip();
            c.Slider("paintLayer", TerrainMaterials.Sand, 1, 4, 1, "count", ControlTier.More, "shore").When("paint=true").Tip();

            c.Choice("backdrop", "dunes", ControlTier.More, "backdrop", "dunes", "lawn", "woods", "none").Tip();
            c.Slider("duneHeight", 2, 0.5, 10, 0.5, "m", ControlTier.More, "backdrop").When("backdrop=dunes");
            c.Slider("duneDepth", 20, 8, 70, 2, "m", ControlTier.More, "backdrop").Layout(6).Tip();
            c.Toggle("duneFence", false, ControlTier.More, "backdrop").When("backdrop=dunes").Tip();
            c.Slider("grassDensity", 60, 0, 100, 5, "%", ControlTier.More, "backdrop").Tip();
            c.Slider("treeDensity", 15, 0, 100, 5, "perHa", ControlTier.More, "backdrop");

            c.Toggle("backPath", true, ControlTier.More, "backPath").Tip();
            c.Choice("pathWidth", "narrow", ControlTier.More, "backPath", "narrow", "wide");
            c.Slider("accessSpacing", 60, 20, 200, 10, "m", ControlTier.More, "backPath").Layout(10).Tip();
            c.Toggle("landings", true, ControlTier.More, "backPath").Tip();
            c.Slider("benchSpacing", 25, 0, 80, 5, "m", ControlTier.More, "backPath").Layout(5).Tip();
            c.Toggle("lamps", true, ControlTier.More, "backPath");

            c.Toggle("showers", true, ControlTier.More, "amenities").Tip();
            c.Choice("huts", "few", ControlTier.More, "amenities", "none", "few", "row").Tip();
            c.Slider("lifeguards", 1, 0, 8, 1, "count", ControlTier.More, "amenities").Tip();
            c.Slider("kiosks", 1, 0, 6, 1, "count", ControlTier.More, "amenities");
            c.Slider("courts", 0, 0, 8, 1, "count", ControlTier.More, "amenities").Tip();
            c.Toggle("boats", false, ControlTier.More, "amenities").Tip();
            c.Toggle("fires", false, ControlTier.More, "amenities").Tip();

            c.Slider("rockClusters", 0, 0, 20, 1, "count", ControlTier.More, "rocks").Tip();
            c.Slider("tidePools", 0, 0, 12, 1, "count", ControlTier.More, "rocks").Tip();

            c.Asset("umbrellaAsset", AssetCategories.BeachUmbrella, ControlTier.More, "assets");
            c.Asset("loungerAsset", AssetCategories.BeachLounger, ControlTier.More, "assets");
            c.Asset("lifeguardAsset", AssetCategories.BeachLifeguard, ControlTier.More, "assets");
            c.Asset("hutAsset", AssetCategories.BeachHut, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("landingAsset", AssetCategories.SurfacePaving, ControlTier.More, "assets").When("landings=true");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            c.AssetMulti("grassSpecies", AssetCategories.Bush, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);
            // s20 (appended): how far the sand and the graded slope continue under the water.
            c.Slider("underwater", IntoWater, 0, 40, 2, "m", ControlTier.More, "shore").When("sandReach=true").Tip();
            // rf-beach (appended): timber jetties from the sand out over the water, a T-head to swim from.
            c.Slider("jetties", 0, 0, 3, 1, "count", ControlTier.More, "amenities").Tip();
            c.Slider("jettyLength", 26, 8, 80, 2, "m", ControlTier.More, "amenities").Tip();
            c.Toggle("jettyHead", true, ControlTier.More, "amenities").Tip();
            // (appended) a bay carved into the land below the water level, so the lake flows in and the shore curves (ShoreCarve).
            c.Slider("bay", 0, 0, 40, 2, "m", ControlTier.Essential, "shore").Tip();
            DefineFormControls(c);

            c.Fixed("waterSide");
            c.Fixed("carve");
            c.Fixed("paintLayer");
            c.Fixed("sandReach");
            c.Fixed("underwater");
            c.RandomRange("beachDepth", 36, 70);
            c.RandomRange("sandBlend", 8, 24);
            c.RandomRange("umbrellas", 10, 60);
            c.RandomRange("courts", 0, 2);
            c.RandomRange("lifeguards", 0, 2);
            c.RandomRange("rockClusters", 0, 4);
            c.RandomRange("tidePools", 0, 2);
            c.Chance("boats", 0.3f);
            c.Chance("fires", 0.3f);
        }

        protected override void DefinePresets()
        {
            // Preset ids are save format; only labels and values changed.
            // Family beach: deep sand, loose umbrella groups, lifeguard posts, low dunes with marram grass and pines behind.
            // rf-beach: busier (umbrellas 70 -> 110 /ha), a swimming jetty with a T-head, boats pulled up, dunes a little higher.
            Preset("sandy", ("style", "sandy"), ("beachDepth", 56.0), ("sandBlend", 16.0), ("umbrellas", 110.0), ("layout", "scattered"), ("backdrop", "dunes"),
                ("huts", "none"), ("lifeguards", 2.0), ("duneDepth", 30.0), ("duneHeight", 3.0), ("grassDensity", 70.0), ("treeDensity", 40.0), ("kiosks", 1.0),
                ("jetties", 1.0), ("jettyLength", 26.0), ("jettyHead", true), ("boats", true), ("bay", 24.0), ("berm", 0.7), ("hummocks", 0.4),
                ("biomeLawn", 0.0), ("biomeDunes", 30.0), ("biomeForest", 30.0), ("biomeHeath", 25.0), ("biomeMeadow", 15.0));   // p2-beaches: grey dunes into pines, not a park
            // Promenade beach: tidy rows, one wide promenade on a level bench with benches and kiosks, palms on a lawn.
            // rf-beach: denser rows (110 -> 150 /ha), a palm avenue along the promenade (Backdrop), a long pier with a T-head.
            Preset("resort", ("style", "sandy"), ("beachDepth", 52.0), ("sandBlend", 6.0), ("umbrellas", 150.0), ("layout", "rows"), ("backdrop", "lawn"), ("huts", "none"),
                ("pathWidth", "wide"), ("benchSpacing", 15.0), ("kiosks", 2.0), ("lifeguards", 2.0), ("slope", 4.0), ("treeDensity", 30.0), ("accessSpacing", 45.0),
                ("jetties", 1.0), ("jettyLength", 44.0), ("jettyHead", true), ("bay", 16.0), ("berm", 0.5),
                ("biomeLawn", 0.0), ("biomeMeadow", 40.0), ("biomeForest", 35.0), ("biomeHeath", 25.0));   // the palm avenue along the promenade, then no park (a lawn share planted palms in grid rows)
            // Rocky cove: dirt texture with sand pockets, rocks along the waterline, tide pools, a wide rough blend.
            Preset("rocky", ("style", "rocky"), ("umbrellas", 5.0), ("sandSurface", false), ("paintLayer", (double)TerrainMaterials.Dirt), ("backdrop", "none"),
                ("huts", "none"), ("lifeguards", 0.0), ("kiosks", 0.0), ("rockClusters", 8.0), ("tidePools", 5.0), ("beachDepth", 36.0), ("sandBlend", 20.0),
                ("slope", 8.0), ("treeDensity", 30.0), ("benchSpacing", 30.0));
            Preset("river", ("style", "river"), ("beachDepth", 30.0), ("sandBlend", 8.0), ("wetBand", 3.0), ("slope", 3.0), ("umbrellas", 10.0), ("backdrop", "lawn"),
                ("treeDensity", 35.0), ("boats", true), ("lifeguards", 0.0), ("huts", "none"), ("accessSpacing", 40.0), ("underwater", 12.0));
            // Wild dunes: no amenities, sand drifting up low, smooth dune ridges parallel to the shore, headlands, fire rings, pines.
            // rf-beach: real dunes (2 -> 3.5 m, deeper belt, thick marram grass; no sand fences: vanilla has no timber one), pines only far behind them.
            Preset("cove", ("style", "cove"), ("beachDepth", 44.0), ("sandBlend", 22.0), ("umbrellas", 6.0), ("backdrop", "dunes"), ("duneDepth", 60.0),
                ("duneHeight", 8.0), ("treeDensity", 25.0), ("grassDensity", 90.0), ("fires", true), ("lifeguards", 0.0), ("huts", "none"), ("kiosks", 0.0),
                ("lamps", false), ("rockClusters", 3.0), ("boats", true), ("benchSpacing", 0.0), ("accessSpacing", 90.0), ("showers", false), ("bay", 32.0),
                ("berm", 0.9), ("blowouts", 3.0), ("hummocks", 1.0), ("boardwalk", true),
                ("biomeLawn", 0.0), ("biomeDunes", 50.0), ("biomeHeath", 30.0), ("biomeForest", 20.0), ("treeSpecies", new[] { "PineTree01", "PineTree01", "EuropeanSpruce01", "PineTree01", "SpruceTree01" }));
            Preset("volleyball", ("style", "volleyball"), ("beachDepth", 66.0), ("sandBlend", 10.0), ("courts", 4.0), ("umbrellas", 25.0), ("accessSpacing", 120.0),
                ("lifeguards", 1.0), ("kiosks", 2.0), ("huts", "none"), ("pathWidth", "wide"), ("backdrop", "lawn"), ("treeDensity", 30.0), ("slope", 3.0));
            DefineFormPresets();
        }

        protected override int WantedEntrances(LayerRegion region) => math.min(4, Entrances.WantedByArea(region.Area));

        // ───────────── planning ─────────────

        private sealed class Work
        {
            public LayerContext Ctx;
            public Shoreline Shore;
            public ShoreGround G;
            public Occupancy Occ;
            public string Style;
            /// <summary>Distances from the real waterline: wet band, back of the sand, the walk's centre.</summary>
            public float Wet, D, PathDw, Hw, Blend, Under;
            public bool HasPath;
            public float2[] WaterEdge;
            public List<float2> BeachRuns = new List<float2>(), CliffRuns = new List<float2>();
            public List<float2[]> Lines = new List<float2[]>();
            public List<System.Func<float, float>> Profiles = new List<System.Func<float, float>>();
            /// <summary>Access paths: top (on the promenade) and bottom (the path end, at the landing).</summary>
            public List<(float2 top, float2 bottom)> Access = new List<(float2, float2)>();
            public List<float2> Landings = new List<float2>();
            public AssetRef Umbrella, Lounger;
            public int Umbrellas, Loungers, Showers, Huts, Guards, Courts, Rocks, Pools, Boats, Fires, Kiosks, Lookouts, Jetties;
            /// <summary>(p2-beaches) the landforms, and what the layout put where (the ground design reads them).</summary>
            public BeachForms Forms;
            /// <summary>(p2-beaches) The pocket beach cut into a cliff shore, or null.</summary>
            public ShoreCarve.Pocket Pocket;
            public readonly List<float2> UmbrellaSpots = new List<float2>();
            public readonly List<(float2 root, float2 end)> Decks = new List<(float2, float2)>();
            public readonly List<(float2 p, float r)> RockSpots = new List<(float2, float)>();
            public readonly List<float2> FirePlaces = new List<float2>();

            public float FromWater(float2 p) => Shore.FromWater(p);
        }


        protected override void PlanCore(LayerContext ctx)
        {
            var region0 = ctx.LayerRegion;
            var shore = Shoreline.Analyze(ctx, region0, ctx.S("waterSide"));
            if (shore == null) { ctx.Issue(IssueSeverity.Warning, Keys.NoSpace, NameKey); return; }
            // a bay first: the lake flows into it, and everything below is laid out along the curved waterline it makes
            bool carvedBay = false;
            if (ctx.F("bay") >= 3f && shore.CliffShare < ShoreGround.CliffShoreShare && ShoreCarve.Bay(ctx, shore, ctx.F("bay"), 18f, Hashing.Mix(ctx.Seed, 0xBA7u), out _))
            {
                carvedBay = true;
                var carved = Shoreline.Analyze(ctx, region0, ctx.S("waterSide"));
                if (carved != null) shore = carved;
            }
            // p2-beaches: a shore that is all cliff gets a pocket beach cut into it (a cove down to the water, a gully path up)
            var pocket = carvedBay ? null : ShoreCarve.CutPocket(ctx, shore, shore.MaxDepth + shore.GapAt(shore.Length * 0.5f));
            if (pocket != null)
            {
                var cut = Shoreline.Analyze(ctx, region0, ctx.S("waterSide"));
                if (cut != null) shore = cut;
            }
            ctx.LayerRegion = shore.Land;
            var b = new Work { Ctx = ctx, Shore = shore, Style = ctx.S("style"), Pocket = pocket };
            b.Under = ctx.B("sandReach") ? ctx.F("underwater") : 0f;
            // p2-beaches: the sand covers the whole carved bay floor (it stopped 20 m out from the new waterline and left the old
            // bank as a band of grass under the shallow water: the game runner's family beach, SandIsPaintedToTheWater)
            if (carvedBay && b.Under > 0f) b.Under = math.max(b.Under, ctx.F("bay") + 12f);
            float landDepth = shore.MaxDepth + shore.GapAt(shore.Length * 0.5f);
            b.Wet = math.min(ctx.F("wetBand"), math.max(2f, landDepth * 0.2f));
            b.D = math.min(ctx.F("beachDepth"), math.max(b.Wet + 6f, landDepth - 6f));
            if (pocket != null) { b.D = pocket.Sand - 1f; b.Wet = math.min(b.Wet, 4f); }
            // p2-beaches: the landforms first (headlands and knolls reach out into the water: the sculpt must reach that far)
            b.Forms = BeachForms.Plan(ctx, shore, b.Wet, b.D, Hashing.Mix(ctx.Seed, 0xF0A3u));
            ShoreAdvice(ctx, shore, landDepth);
            // s30: a 2 m node grid (a 4 m grid smeared the promenade bench and the access ramps into cross slopes).
            b.G = ShoreGround.Create(ctx, shore, math.max(b.Under, b.Forms.Reach), 2f);
            b.Occ = new Occupancy(shore.Land, ctx.Input.Reserved, ctx.Site);
            var g = b.G;
            // only the cove's sand is shaped: the walls, the gully and the cliff top beside the cove (the beach profile, measured from
            // the cove's waterline, dug a trench along the rim) stay as cut
            if (pocket != null) g.Lock(pocket.OnWall, p => !pocket.OnSand(p, 1.5f) && (pocket.Outside(p, out _, out _) < 0f || shore.FromWater(p) < pocket.Sand + 24f));
            b.Occ.GroundOk = (p, r) => g.CanStand(p, r);
            b.Blend = math.max(1f, ctx.F("sandBlend"));
            var pathRole = ctx.S("pathWidth") == "wide" ? NetRole.PathwayWide : NetRole.Pathway;
            b.Hw = Composer.DefaultHalfWidth(pathRole);
            b.PathDw = b.D + b.Hw + 0.5f;
            b.HasPath = ctx.B("backPath") && landDepth > b.PathDw + b.Hw + 2f && pocket == null;   // a pocket beach: the gully path
            b.WaterEdge = shore.WaterEdge();
            b.BeachRuns = g.Runs(k => k != ShoreKind.Cliff, 12f);
            b.CliffRuns = g.Runs(k => k == ShoreKind.Cliff, 16f);
            ctx.Plan.Facts["beachDepth"] = b.D;
            ctx.Plan.Facts["sandBlend"] = b.Blend;
            ctx.Plan.Facts["foreshore"] = g.Foreshore.Count;
            PickSetAssets(b);

            bool allCliff = pocket == null && shore.CliffShare >= ShoreGround.CliffShoreShare || b.BeachRuns.Count == 0;
            ctx.Plan.Facts["pocketBeach"] = pocket != null && !allCliff ? 1 : 0;
            float runLen = 0f; foreach (var r in b.BeachRuns) runLen += r.y - r.x;
            ctx.Plan.Facts["beachRunLength"] = runLen;
            if (shore.CliffShare > 0.05f)
                ctx.Issue(allCliff ? IssueSeverity.Warning : IssueSeverity.Info, ShoreGround.CliffKey, (int)math.round(shore.CliffShare * 100f));

            if (!allCliff)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Terrain(b);
                ctx.Plan.Facts["msTerrain"] = sw.ElapsedMilliseconds;
                ctx.Check();
                Ground(b);
                StyleGround(b);
            }
            Paths(b, pathRole, allCliff);
            ctx.Check();
            if (!allCliff)
            {
                Boardwalk(b);   // p2-beaches: decks first, everything keeps off them
                IslandBridges(b);
                Jetties(b);   // rf-beach: before anything else takes the sand at the water
                Courts(b);
                FormRocks(b);
                Rocks(b);
                TidePools(b);
                MarshPlanting(b);
                SandPockets(b);
                Lifeguards(b);
                Showers(b);
                Boats(b);
                ctx.Check();
                UmbrellaSets(b);
                FirePits(b);
            }
            BackPathFurniture(b);
            HutsAndKiosks(b);
            ctx.Check();
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            if (!allCliff) Hinterland(b);
            ctx.Plan.Facts["msHinterland"] = sw2.ElapsedMilliseconds;
            if (!allCliff) BlendGrass(b);
            if (!allCliff) DuneSand(b);
            Backdrop(b);

            sw2.Restart();
            if (!allCliff) LayDecals(b);
            ctx.Plan.Facts["msDecals"] = sw2.ElapsedMilliseconds;
            sw2.Restart();
            // s30: banks ≤ 33 % (45 % read as a near-vertical sheet over a 3.5 m heightmap texel once the walk bench was re-laid).
            g.Finish(b.Forms.MaxCut, b.Forms.MaxFill, b.Forms.MaxGrade, b.Forms.BorderGrade);
            ctx.Plan.Facts["msFinish"] = sw2.ElapsedMilliseconds;
            EmitGreenRegions(ctx, b.Occ, g);
            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Shoreline", System.Math.Round(shore.Length), Keys.UnitM);
            ctx.Stat("Placemaker.Stat.Umbrellas", b.Umbrellas, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Loungers", b.Loungers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Showers", b.Showers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Huts", b.Huts, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Lifeguards", b.Guards, Keys.UnitCount);
            if (b.Courts > 0) ctx.Stat("Placemaker.Stat.Courts", b.Courts, Keys.UnitCount);
            if (b.Rocks > 0) ctx.Stat("Placemaker.Stat.Rocks", b.Rocks, Keys.UnitCount);
            ctx.Plan.Facts["tidePools"] = b.Pools;
            ctx.Plan.Facts["boats"] = b.Boats;
            ctx.Plan.Facts["firePits"] = b.Fires;
            ctx.Plan.Facts["kiosks"] = b.Kiosks;
            ctx.Plan.Facts["accessPaths"] = b.Access.Count;
            ctx.Plan.Facts["landings"] = b.Landings.Count;
            ctx.Plan.Facts["lifeguards"] = b.Guards;
            ctx.Plan.Facts["lookouts"] = b.Lookouts;
            ctx.Plan.Facts["jetties"] = b.Jetties;
            if (b.Jetties > 0) ctx.Stat("Placemaker.Stat.Piers", b.Jetties, Keys.UnitCount);
            // s30: how smooth the promenade came out (tests, gallery, debug dump).
            float radius = 9999f, grade = 0f, wlen = 0f; int wig = 0;
            for (int li = 0; li < b.Lines.Count; li++)
            {
                var l = b.Lines[li];
                radius = math.min(radius, ShoreWalk.MinRadius(l)); wig += ShoreWalk.Wiggles(l); wlen += Shoreline.LengthOf(l);
                var s = ShoreWalk.Arc(l); var h = new float[l.Length];
                for (int i = 0; i < l.Length; i++) h[i] = b.Profiles[li](s[i]);
                grade = math.max(grade, ShoreWalk.MaxGrade(h, s));
            }
            ctx.Plan.Facts["walks"] = b.Lines.Count;
            ctx.Plan.Facts["walkLength"] = wlen;
            ctx.Plan.Facts["walkMinRadius"] = math.min(radius, 9999f);
            ctx.Plan.Facts["walkWiggles"] = wig;
            ctx.Plan.Facts["walkMaxGrade"] = grade;
        }

        /// <summary>Green regions for the features: free land that is not cliff and not steep.</summary>
        internal static void EmitGreenRegions(LayerContext ctx, Occupancy occ, ShoreGround g)
        {
            var region = ctx.LayerRegion;
            // (and never ground the sculpt took under the water)
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f) || g.OnCliff(p) || g.Sculpt.GradeAt(p, 2f) > 0.2f
                || g.HasLevel && ctx.G(p) < g.Level + 0.4f,
                2.5f, 150f, ctx.Cancel);
            if (pieces.Count == 0) { ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core); return; }
            for (int i = 0; i < pieces.Count && i < 24; i++)
                if (i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);
        }

        /// <summary>One umbrella model and one lounger model per build (a beach rents out one kind), unless the player chose.</summary>
        private static void PickSetAssets(Work b)
        {
            var ctx = b.Ctx;
            uint v = Hashing.Mix(ctx.Seed, 0x5E75u);
            b.Umbrella = AssetMix.Pick(BeachArt.Or(ctx.S("umbrellaAsset"), BeachArt.Umbrellas), v);
            b.Lounger = AssetMix.Pick(BeachArt.Or(ctx.S("loungerAsset"), BeachArt.Loungers), Hashing.Mix(v, 1u));
        }

        private static bool InBeachRun(Work b, float along)
        {
            foreach (var r in b.BeachRuns) if (along >= r.x - 1f && along <= r.y + 1f) return true;
            return false;
        }

        // ───────────── terrain, ground ─────────────

        /// <summary>
        /// The ground profile from the water: under the water a shelf at 1.4 × the beach slope (never deeper than 1.8 m,
        /// filled at most 1 m), the beach face at the beach slope up to the back of the sand, then a graded bank blending
        /// into the original ground (its width set so the bank stays under ~25 %), low dune ridges on top behind the walk.
        /// Smoothed; cut/fill limited at Finish. Only lowers on a flat shore (fills just hollows).
        /// </summary>
        private static void Terrain(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G;
            bool carve = ctx.B("carve");
            bool dunes = ctx.S("backdrop") == "dunes" && ctx.F("duneHeight") > 0f;
            if (!carve && !dunes && !b.Forms.Any) return;
            float slope = ctx.F("slope") / 100f;
            float D = b.D;
            // Typical rise from the back of the sand to the ground behind it → bank width.
            var rises = new List<float>();
            foreach (var run in b.BeachRuns)
                for (float s = run.x; s <= run.y; s += 8f)
                {
                    var w = shore.WaterAt(s, out var inw);
                    float r0 = g.Ref(s);
                    rises.Add(ctx.H(w + inw * (D + 30f)) - (r0 + slope * D));
                }
            rises.Sort();
            float rise = rises.Count > 0 ? math.max(0f, rises[rises.Count / 2]) : 0f;
            float bankW = math.clamp(rise / 0.2f, 10f, 50f);
            ctx.Plan.Facts["bankRise"] = rise;
            float duneStart = b.PathDw + b.Hw + 2.5f, duneEnd = duneStart + ctx.F("duneDepth");
            float bankStart = b.HasPath ? math.max(D, b.PathDw + b.Hw + 6.5f) : D;
            float duneH = ctx.F("duneHeight");
            uint seed = Hashing.Mix(ctx.Seed, 0xD00Eu);
            float phase1 = Hashing.Unit(seed) * 6.283f, phase2 = Hashing.Unit(Hashing.Mix(seed, 1u)) * 6.283f;
            float under = math.max(slope * 1.4f, 0.05f);
            var forms = b.Forms;
            if (!carve) forms.Berm = 0f;   // the berm is part of the carved beach face ("Carve terrain" off leaves the beach as it is)
            forms.Dunes(ctx, shore, duneStart, duneEnd, dunes ? duneH : 0f);
            g.Profile((dw, along, h0, p) =>
            {
                float r0 = g.Ref(along);
                float t = h0;
                if (carve)
                {
                    float plane = dw >= 0f ? r0 + 0.05f + slope * dw : math.max(r0 - 1.8f, r0 + 0.05f + under * dw);
                    if (dw <= D)
                        t = plane <= h0 ? plane : math.min(plane, h0 + (dw < 0f ? 1f : 0.6f));
                    else
                    {
                        float top = r0 + 0.05f + slope * D + slope * 0.5f * (dw - D);
                        // s30: the bank starts behind the promenade bench (it used to start at the back of the sand, under the bench,
                        // and the bench squeezed it into a near-vertical sheet).
                        float u = Sculpt.Smooth01((dw - bankStart) / bankW);
                        t = math.lerp(math.min(top, h0 + 0.6f), h0, u);
                    }
                    // s30: never deeper than the earthwork limit: a carve the limit clamped afterwards left a shelf and a near-vertical
                    // bank behind it on a high shore; held here, the beach rides up parallel to the bank instead.
                    t = math.max(t, h0 - 4.5f);
                }
                if (dunes && dw > duneStart && dw < duneEnd)
                {
                    // p2-beaches: two ridges instead of one smooth hump (the game runner's 5 m dunes read flat from 45°): a foredune
                    // with a steep sea face and a long lee, a lower back dune whose crest wanders, a slack between them, peaks and
                    // saddles along the shore, and the crests ending at the belt's ends.
                    float belt = duneEnd - duneStart;
                    float u = (dw - duneStart) / belt;
                    float x1 = (u - 0.3f) * belt / 7f;
                    float fore = x1 < 0f ? math.exp(-x1 * x1 * 2.2f) : math.exp(-x1 * x1 * 0.7f);
                    float c2 = 0.72f + 0.08f * math.sin(along / 23f + phase2);
                    float x2 = (u - c2) * belt / 6f;
                    float back = 0.7f * (x2 < 0f ? math.exp(-x2 * x2 * 1.4f) : math.exp(-x2 * x2 * 0.9f));
                    float ends = math.saturate(u * belt / 3f) * math.saturate((1f - u) * belt / 6f);
                    float peaks = 0.5f + 0.5f * ValueNoise.Sample(new float2(along / 32f, 0.5f), Hashing.Mix(seed, 3u))
                        + 0.12f * math.sin(along / 90f * 6.283f + phase1);
                    float lumps = 0.22f * (ValueNoise.Sample(p / 7f, Hashing.Mix(seed, 4u)) - 0.5f);
                    // p2-beaches: the dunes stand on the original ground, not on the bank the beach carve lowered (they came out
                    // 1.5 m high in the game runner); the foredune's sea face takes up the rise from the beach.
                    float baseH = math.lerp(t, math.max(t, h0), math.saturate(u * belt / 5f));
                    t = baseH + duneH * math.max(0f, (math.max(fore, back) + lumps) * peaks) * ends;
                }
                return forms.Apply(t, dw, along, h0, p, g.Ref(along));
            });
            // p2-beaches: the earthwork limits follow the forms (the old 1.5 m fill cap flattened 3.5 m dunes to 1.5 m)
            g.Settle(forms.MaxCut, forms.MaxFill, forms.MaxGrade, 10f);
            ctx.Plan.Facts["formsFill"] = forms.MaxFill;
        }

        /// <summary>
        /// The sand: one texture field from under the water (full strength there too, so the beach visibly continues into
        /// it) to the back of the beach, fading over "Sand blend" m with a noisy edge. Dunes get a lighter sand thinning up
        /// the slope. Cliff stretches are never painted. The old hard-edged sand surface is opt-in only.
        /// </summary>
        private static void Ground(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G;
            float top = b.HasPath ? b.D : b.D + 2f;
            int layer = math.clamp(ctx.I("paintLayer"), 1, 4);
            bool rocky = b.Style == "rocky";
            if (ctx.B("paint"))
            {
                var clip = Poly.UnionAll(g.Clips);
                float full = rocky ? math.min(top, b.Wet + 6f) : top;
                float fade = rocky ? math.max(b.Blend, top - full + b.Blend * 0.5f) : b.Blend;
                foreach (var run in b.BeachRuns)
                    foreach (var strip in shore.StripFromWater(-math.max(2f, b.Under), top + b.Blend + 3f, run.x, run.y))
                        foreach (var piece in Clip(strip, clip))
                            TerrainPaint.Field(ctx, piece, layer, 1f, 3f, math.clamp(b.Blend * 0.12f, 0.8f, 3f), b.WaterEdge, full, fade, 0f, 0x5A4Du);
            }
            if (ctx.B("sandSurface") && AssetAvailability.Require(ctx.Plan, AssetCategories.SurfaceSand, new[] { ctx.S("sandAsset") }, Keys.Category(AssetCategories.SurfaceSand)))
                foreach (var ring in shore.BandFromWater(0f, top))
                    ctx.Surface(ring, SurfaceRole.Ground, AssetMix.Pick(BeachArt.Or(ctx.S("sandAsset"), BeachArt.Sand), 0u), AssetCategories.SurfaceSand);
            if (ctx.S("backdrop") == "lawn")
            {
                // The lawn starts where the sand has mostly faded (not at the path), so the blend stays visible.
                float from = b.PathDw + b.Hw + 0.8f + (ctx.B("paint") ? b.Blend * 0.6f : 0f);
                foreach (var ring in shore.BandFromWater(from, shore.MaxDepth + Shoreline.MaxGap + 20f))
                    Waterfront.WaterfrontLayer.Lawn(ctx, ring);   // (rf-beach) painted, not the hard-edged grass surface
            }
        }

        /// <summary>
        /// (p2-beaches) Sand over the whole dune belt, laid after the land behind (its biome patches spilled over the belt and
        /// painted it green: the game runner's dunes read as scrub; at 0.6 thinning to the crest they did too).
        /// </summary>
        private static void DuneSand(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!ctx.B("paint") || ctx.S("backdrop") != "dunes" || b.Style == "rocky") return;
            var clip = Poly.UnionAll(b.G.Clips);
            int layer = math.clamp(ctx.I("paintLayer"), 1, 4);
            float d0 = b.PathDw + b.Hw + 0.5f, depth = ctx.F("duneDepth");
            foreach (var run in b.BeachRuns)
                foreach (var strip in shore.StripFromWater(d0, d0 + depth + 2f, run.x, run.y))
                    foreach (var piece in Clip(strip, clip))
                        // no gradient: measured from the old waterline it stopped 20-32 m short behind a carved bay
                        TerrainPaint.Field(ctx, piece, layer, 1f, 6f, 2.5f, null, 0f, 0f, 0f, 0xD0E5u);
        }

        private static List<float2[]> Clip(float2[] ring, List<Region> clip)
        {
            var result = new List<float2[]>();
            try
            {
                var subject = new List<IReadOnlyList<float2>> { ring };
                foreach (var r in Poly.Boolean(subject, ToRings(clip), BoolOp.Intersection, FillRule.NonZero))
                    foreach (var piece in Poly.CutHoles(r, 0.5f))
                    {
                        var c = Poly.Clean(piece, 0.3f);
                        if (c.Length >= 3 && Poly.Area(c) > 8f) result.Add(c);
                    }
            }
            catch (System.Exception) { }
            return result;
        }

        private static List<IReadOnlyList<float2>> ToRings(List<Region> regions)
        {
            var list = new List<IReadOnlyList<float2>>();
            foreach (var r in regions)
            {
                if (r.Outer != null) list.Add(r.Outer);
                if (r.Holes != null) foreach (var h in r.Holes) list.Add(h);
            }
            return list;
        }

        /// <summary>Rocky style: pockets of sand between the rocks near the water (sand texture over the dirt).</summary>
        private static void SandPockets(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (b.Style != "rocky" || !ctx.B("paint")) return;
            int n = math.clamp((int)(shore.Length / 35f), 2, 10);
            var stations = shore.Stations(n, 8f, 0.35f);
            for (int i = 0; i < stations.Count; i++)
            {
                if (!InBeachRun(b, stations[i])) continue;
                uint v = Hashing.Mix(ctx.Seed, 0x5A0Cu + (uint)i);
                var p = shore.WaterAt(stations[i], out var inw);
                var tan = new float2(inw.y, -inw.x);
                float r = 4f + Hashing.Unit(v) * 4f;
                var c = p + inw * (b.Wet * 0.5f + Hashing.Unit(Hashing.Mix(v, 1u)) * 4f) + tan * (Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * 10f;
                TerrainPaint.Field(ctx, LeisureKit.Blob(c, r, v, 14, 0.3f), TerrainMaterials.Sand, 0.95f, r * 0.6f, 1f, null, 0f, 0f, 0f, v);
            }
        }

        // ───────────── circulation ─────────────

        /// <summary>(s30) The promenade's smooth curve(s) over one beach run (on land, off cliffs).</summary>
        private static List<float2[]> BeachWalkLines(Work b, float2 run)
        {
            var shore = b.Shore; var g = b.G;
            // p2-beaches: the walk stops at a ravine's rim (a footbridge spans it, RavineBridges)
            var f = b.Forms;
            return ShoreWalk.SmoothFromWater(shore, b.PathDw, run, 24f, null, p => shore.Land.ContainsDisc(p, b.Hw + 1f) && !g.OnCliff(p)
                && (f.Ravines.Count == 0 || !RavineAt(b, p)), 24f);
        }

        private void Paths(Work b, NetRole role, bool allCliff)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ; var g = b.G;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            var spine = new List<(float2, float2)>();
            var walkEnds = new List<(float2 a, float ha, float2 e, float he)>();
            if (b.HasPath && !allCliff)
                // s30: ONE smooth curve near the water (ShoreWalk: the waterline offset smoothed over tens of metres, radius
                // ≥ 40 m) per beach run, on ONE level bench smoothed over ±40 m and ≤ 2 % along it; it ends where its level would
                // have to climb to ground it may not change (the side borders, a cliff).
                foreach (var run in b.BeachRuns)
                foreach (var line in BeachWalkLines(b, run))
                {
                    foreach (var (piece, profile) in g.Walk(line, b.Hw + 1.5f, 5f, 40f, 0.02f, 0.8f, 24f))
                    {
                        var curves = Shoreline.Curves(shore.Land, piece, role);
                        if (curves.Count == 0) continue;
                        ParkPaths.Index(curves, occ.Corridors, 100);
                        ParkPaths.Index(curves, occ.Paths, 100);
                        ParkPaths.Emit(ctx, curves, NoGates, asset);
                        var c0 = curves[0].Samples; var c1 = curves[curves.Count - 1].Samples;
                        walkEnds.Add((c0[0], profile(0f), c1[c1.Length - 1], profile(Shoreline.LengthOf(piece))));
                        foreach (var c in curves)
                            for (int i = 1; i < c.Samples.Length; i++) spine.Add((c.Samples[i - 1], c.Samples[i]));
                        b.Lines.Add(piece);
                        b.Profiles.Add(profile);
                    }
                }
            b.HasPath = b.Lines.Count > 0;
            RavineBridges(b, walkEnds);

            var cliffLines = new List<float2[]>();
            // Cliff stretches: a walk on the cliff top with lookouts (the cliff face stays natural).
            foreach (var run in b.CliffRuns)
            {
                var res = CliffTop.Plan(ctx, g, occ, run.x, run.y, NetRole.Pathway, asset, AssetMix.Pick(BeachArt.Benches, 0u),
                    AssetMix.Pick(BeachArt.Railings, 0u), AssetMix.Pick(BeachArt.Lamps, 0u), AssetMix.Pick(BeachArt.Lookout, 0u), 70f);
                b.Lookouts += res.Lookouts;
                cliffLines.AddRange(res.Lines);
                foreach (var line in res.Lines)
                    for (int i = 1; i < line.Length; i++) spine.Add((line[i - 1], line[i]));
            }

            // Entrances from the land side (never across the sand), each one constant-grade ramp.
            var all = Gates(ctx);
            var gates = new List<Gate>();
            foreach (var gt in all) if (shore.FromWater(gt.Position) > b.D * 0.6f) gates.Add(gt);
            // An entrance placed on the wet sand or in the water is reached over the sand: no path is drawn to it.
            if (gates.Count == 0) foreach (var gt in all) if (shore.FromWater(gt.Position) > b.Wet + 4f) gates.Add(gt);
            // p2-beaches: the pocket beach's path, down the gully floor from the cliff top to the back of the sand
            var gully = new List<float2[]>();
            if (b.Pocket != null && !allCliff)
            {
                var pk = b.Pocket;
                LeisureKit.Path(ctx, occ, pk.Top, pk.Bottom, NetRole.Pathway, asset);
                gully.Add(new[] { pk.Top, pk.Bottom });
                spine.Add((pk.Top, pk.Bottom));
                b.Access.Add((pk.Top, pk.Bottom));
            }
            if (spine.Count > 0)
            {
                var walks = new List<float2[]>(b.Lines);
                walks.AddRange(gully);
                walks.AddRange(cliffLines);
                g.ConnectAll(occ, gates.ConvertAll(gt => gt.Position), walks, NetRole.Pathway, asset, b.HasPath ? b.Hw + 1.5f : 0f);
            }
            if (allCliff) return;

            // Short accesses off the promenade, each ending on a level landing on the sand.
            float spacing = ctx.F("accessSpacing");
            if (b.HasPath)
                for (int li = 0; li < b.Lines.Count; li++)
                {
                    var line = b.Lines[li];
                    float len = Shoreline.LengthOf(line);
                    int n = math.max(1, (int)math.floor(len / spacing));
                    for (int k = 0; k < n; k++)
                    {
                        float s = (k + 0.5f) * len / n;
                        TryAccess(b, Shoreline.PointOn(line, s, out _), b.Profiles[li](s), asset);
                    }
                }
            else
                foreach (var gt in gates)
                {
                    var from = gt.Position;
                    if (shore.FromWater(from) < b.Wet + 8f) continue;
                    TryAccess(b, from, g.H(from), asset);
                }
        }

        /// <summary>
        /// One access: a short straight path from the promenade towards the water, graded at a constant slope down to a
        /// small level landing (deck or paving) on the sand — people walk on from there. An aisle to the water stays free.
        /// </summary>
        private static void TryAccess(Work b, float2 top, float topH, AssetRef asset)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ; var g = b.G;
            shore.Nearest(top, out var inw, out float along);
            if (!InBeachRun(b, along)) return;
            var tan = new float2(inw.y, -inw.x);
            float sand = shore.FromWater(top) - b.Wet;
            if (sand < 14f) return;   // a narrow beach is stepped onto straight from the promenade
            float run = math.clamp(sand * 0.28f, 4f, 12f);
            var bottom = top - inw * run;
            var landing = bottom - inw * (LandingDepth * 0.5f);
            if (!shore.Land.ContainsDisc(landing, LandingHalfWidth + 0.5f) || shore.Land.CrossesBoundary(top, bottom, true)) return;
            float len = math.distance(top, bottom);
            for (float s = b.Hw + 2f; s < len; s += 1.5f)
            {
                var q = math.lerp(top, bottom, s / len);
                if (occ.Corridors.Overlaps(q, 1.2f, 0.3f) || occ.Discs.Overlaps(q, 1.2f, 0.2f) || occ.HitsPolygon(q, 1.2f)) return;
            }
            bool landings = ctx.B("landings");
            if (landings && !occ.Free(landing, LandingHalfWidth, 0.3f, 0.2f)) return;
            foreach (var (t0, _) in b.Access) if (math.distance(t0, top) < 25f) return;
            // Grade: the ramp may fall at most 10 %; a landing too far below is raised a little instead.
            float lowH = g.H(landing);
            float botH = math.max(lowH, topH - 0.10f * len);
            if (g.HasLevel) botH = math.max(botH, g.Level + 0.35f);
            // s30: level across well past the path (a 3 m flat strip on the 2 m grid), banks widening with the cut: no lip along it.
            // The ramp starts at the bench's sea edge: from the walk centre its first metres tilted the bench beside the junction.
            var rampTop = top - inw * math.min(b.Hw + 1.5f, len * 0.4f);
            g.Ramp(rampTop, topH, bottom, botH, 3f, math.max(3f, math.abs(topH - g.H(math.lerp(rampTop, bottom, 0.5f))) / 0.25f));
            LeisureKit.Path(ctx, occ, top, bottom, NetRole.Pathway, asset);
            b.Access.Add((top, bottom));
            if (landings)
            {
                var rect = new OrientedRect { Center = landing, Axis = tan, HalfLength = LandingHalfWidth, HalfWidth = LandingDepth * 0.5f };
                g.Sculpt.Pad(rect.Ring, botH, 3f);
                if (ctx.Surface(rect.Ring, SurfaceRole.Paving, AssetMix.Pick(BeachArt.Or(ctx.S("landingAsset"), BeachArt.Landing), 0u), AssetCategories.SurfacePaving))
                {
                    // Taken as a corridor across the landing (capsule ends seaward/landward), so its sides stay exact for the showers.
                    occ.TakeCorridor(landing + inw * (LandingDepth * 0.5f), landing - inw * (LandingDepth * 0.5f), LandingHalfWidth, 2);
                    b.Landings.Add(landing);
                }
            }
            // Aisle to the water: no umbrella sets in front of the access.
            var waterP = shore.WaterAt(along, out _);
            occ.TakeCorridor(landing - inw * (LandingDepth * 0.5f), waterP + inw * 1f, 2.2f, 5);
        }

        // ───────────── on the sand ─────────────

        private static float Yaw(float2 dir) => math.atan2(dir.x, dir.y);

        private static void Courts(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("courts");
            if (wanted == 0 && b.Style == "volleyball") wanted = math.clamp((int)(shore.Length / 40f), 2, 6);
            if (wanted <= 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachVolleyball, AssetCategories.Sports);
            if (cat == null) return;
            float dc = math.clamp((b.Wet + b.D) * 0.5f, b.Wet + 6f, b.D - 6f);
            if (dc < b.Wet + 5f) { ctx.Issue(IssueSeverity.Info, "Placemaker.Beach.Issue.NoCourtSpace"); return; }
            // Grouped from one end of the beach (a volleyball area), not sprinkled along it.
            int tries = wanted * 8;
            var stations = shore.Stations(tries, 12f);
            for (int i = 0; i < stations.Count && b.Courts < wanted; i++)
            {
                if (!InBeachRun(b, stations[i])) continue;
                var c0 = shore.WaterAt(stations[i], out var inw);
                var tan = new float2(inw.y, -inw.x);
                var rect = new OrientedRect { Center = c0 + inw * dc, Axis = tan, HalfLength = 9f, HalfWidth = 5f };
                if (!LeisureKit.RectFits(shore.Land, b.Occ, rect, 0.5f)) continue;
                if (!b.G.MakeLevel(rect.Center, 5f, 1.2f, out _)) continue;
                LeisureKit.TakeRect(b.Occ, rect);
                uint v = Hashing.Mix(ctx.Seed, 0xC0u + (uint)i);
                // No vanilla volleyball net: the tennis net at 0.7 scale (9.2 m) spans the 10 m court, facing
                // along it (yaw = court axis puts the net's long local x across the court). Art pass a3.
                var net = ctx.Object(rect.Center, rect.Yaw, ObjectRole.Furniture, new AssetRef("", "TennisNet01"), v, -1, cat);
                net.Scale = 0.7f;
                b.Courts++;
            }
            if (b.Courts < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Beach.Issue.FewerCourts", b.Courts, wanted);
        }

        private static void Rocks(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("rockClusters");
            if (wanted == 0 && b.Style == "rocky") wanted = math.clamp((int)(shore.Length / 30f), 4, 12);
            bool headlands = b.Style == "cove" && !shore.Closed;
            if (wanted <= 0 && !headlands) return;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, null, Keys.Category(AssetCategories.Rock))) return;
            var spots = new List<(float2 c, float r, int n)>();
            if (headlands)
                foreach (float s in new[] { math.min(8f, shore.Length * 0.1f), shore.Length - math.min(8f, shore.Length * 0.1f) })
                {
                    var p = shore.WaterAt(s, out var inw);
                    spots.Add((p + inw * math.min(6f, b.D * 0.3f), 7.5f, 16));
                }
            var stations = shore.Stations(wanted, 6f, 0.3f);
            for (int i = 0; i < stations.Count; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x40C0u + (uint)i);
                var p = shore.WaterAt(stations[i], out var inw);
                var tan = new float2(inw.y, -inw.x);
                bool rocky = b.Style == "rocky";
                // Rocky shores: clusters straddle the waterline; elsewhere a few on the upper sand.
                float d = rocky ? 1.5f + Hashing.Unit(v) * (b.Wet + 3f) : b.Wet + Hashing.Unit(v) * math.max(2f, b.D - b.Wet - 4f);
                float r = rocky ? 2.5f + Hashing.Unit(Hashing.Mix(v, 1u)) * 3f : 2f + Hashing.Unit(Hashing.Mix(v, 1u)) * 2f;
                spots.Add((p + inw * d + tan * (Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * 8f, r, rocky ? 9 : 5));
            }
            const float golden = 2.39996323f;
            for (int k = 0; k < spots.Count; k++)
            {
                var (c, R, n) = spots[k];
                uint cs = Hashing.Mix(ctx.Seed, 0x40D0u + (uint)k * 31u);
                int placed = 0;
                for (int i = 0; i < n * 2 && placed < n; i++)
                {
                    float rr = R * math.sqrt((i + 0.5f) / (n * 1.4f));
                    if (rr > R) break;
                    var p = c + new float2(math.cos(i * golden + cs % 7), math.sin(i * golden + cs % 7)) * rr;
                    uint v = Hashing.Mix(cs, (uint)i);
                    float scale = 0.5f + 1.1f * Hashing.Unit(v) * (1f - rr / (R + 0.01f) * 0.6f);
                    float fr = 1.1f * scale;
                    // Rocks may sit on a slope, but never out in the water or on a cliff face.
                    if (b.G.OnCliff(p) || b.Shore.FromWater(p) < 0.5f || !FreeIgnoringGround(b.Occ, p, fr)) continue;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 5u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    b.Occ.Take(p, fr, 2);
                    placed++;
                }
                b.Rocks += placed;
            }
        }

        /// <summary>Occupancy check without the ground test (natural things — rocks, trees — may stand on slopes).</summary>
        private static bool FreeIgnoringGround(Occupancy occ, float2 p, float r, float margin = 0.5f, float gap = 0.1f)
        {
            var keep = occ.GroundOk;
            occ.GroundOk = null;
            try { return occ.Free(p, r, margin, gap); }
            finally { occ.GroundOk = keep; }
        }

        private static void TidePools(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G;
            int wanted = ctx.I("tidePools");
            if (wanted == 0 && b.Style == "rocky") wanted = math.clamp((int)(shore.Length / 40f), 2, 6);
            if (wanted <= 0) return;
            bool rocks = AssetAvailability.Has(AssetCategories.Rock);
            var stations = shore.Stations(wanted * 2, 10f, 0.7f);
            for (int i = 0; i < stations.Count && b.Pools < wanted; i++)
            {
                if (!InBeachRun(b, stations[i])) continue;
                uint v = Hashing.Mix(ctx.Seed, 0x7D00u + (uint)i);
                var p = shore.WaterAt(stations[i], out var inw);
                float r = 2.2f + Hashing.Unit(v) * 2f;
                var c = p + inw * (b.Wet * 0.5f + r + 1f);
                if (shore.EdgeDistance(c) < r + 8f || !b.Occ.Free(c, r + 0.6f, 1f, 0.2f)) continue;
                // A shallow bowl in the sculpted ground (smooth rim), not a flat-bottomed cut.
                float h = g.H(c);
                g.Sculpt.PadDisc(c, r * 0.45f, h - 0.7f, r * 0.8f);
                b.Occ.Take(c, r, 2);
                b.Pools++;
                if (!rocks) continue;
                int n = 3 + (int)(Hashing.Unit(Hashing.Mix(v, 3u)) * 3f);
                for (int k = 0; k < n; k++)
                {
                    float a = Hashing.Unit(Hashing.Mix(v, (uint)k + 20u)) * 6.283f;
                    var q = c + new float2(math.cos(a), math.sin(a)) * (r + 1.1f);
                    if (!FreeIgnoringGround(b.Occ, q, 0.7f, 0.5f, 0.05f)) continue;
                    var o = ctx.Object(q, a, ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(v, (uint)k + 40u), -1, AssetCategories.Rock);
                    o.Scale = 0.5f + 0.5f * Hashing.Unit(Hashing.Mix(v, (uint)k + 60u));
                    b.Occ.Take(q, 0.7f, 2);
                    b.Rocks++;
                }
            }
            if (b.Pools > 0) ShoreTerrain.Warn(ctx);
        }

        /// <summary>
        /// Lifeguard posts behind the wet sand, evenly spaced and facing the water. Vanilla CS2 has no lifeguard
        /// tower, so unless the player picks one, a post is a tall umpire chair (reads as a lifeguard chair) with a
        /// lifebuoy station and a flag pole beside it.
        /// </summary>
        private static void Lifeguards(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            int wanted = ctx.I("lifeguards");
            if (wanted <= 0) return;
            string chosen = ctx.S("lifeguardAsset");
            string cat;
            AssetRef chair;
            if (!string.IsNullOrEmpty(chosen)) { cat = AssetCategories.BeachLifeguard; chair = AssetMix.One(chosen); }
            else
            {
                cat = LeisureKit.Category(ctx, "", AssetCategories.SportsTennis, AssetCategories.BeachLifeguard);
                if (cat == null) return;
                chair = cat == AssetCategories.SportsTennis ? new AssetRef(BeachArt.LifeguardChair) : AssetMix.One("");
            }
            bool nautical = AssetAvailability.Has(AssetCategories.WaterfrontNautical);
            foreach (float s in shore.Stations(wanted, 15f))
            {
                for (int k = 0; k < 7; k++)
                {
                    float off = (k == 0 ? 0 : (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * 4f);
                    if (!InBeachRun(b, s + off)) continue;
                    var p = shore.WaterAt(s + off, out var inw);
                    var tan = new float2(inw.y, -inw.x);
                    var q = p + inw * (b.Wet + 3.5f);
                    uint v = Hashing.Mix(ctx.Seed, 0x6A00u + (uint)b.Guards);
                    if (!LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, chair, cat, 1.2f, v, 0.8f, 0.4f)) continue;
                    b.Guards++;
                    if (nautical) LeisureKit.Put(ctx, b.Occ, q + tan * 2.1f + inw * 0.6f, Yaw(-inw), ObjectRole.Furniture, new AssetRef(BeachArt.Lifebuoy), AssetCategories.WaterfrontNautical, 0.5f, Hashing.Mix(v, 1u), 0.4f, 0f);
                    LeisureKit.Put(ctx, b.Occ, q - tan * 2.1f + inw * 0.8f, 0f, ObjectRole.Furniture, new AssetRef(BeachArt.Flag), AssetCategories.Decoration, 0.3f, Hashing.Mix(v, 2u), 0.4f, 0f);
                    // Keep a little apron around the post (no umbrellas crowding the chair).
                    b.Occ.Take(q, 3f, 6);
                    break;
                }
            }
        }

        /// <summary>Showers beside the landings (the end of each access), where people come off the sand.</summary>
        private static void Showers(Work b)
        {
            var ctx = b.Ctx;
            if (!ctx.B("showers") || b.Access.Count == 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachShower);
            if (cat == null) return;
            foreach (var (top, bottom) in b.Access)
            {
                var dir = math.normalizesafe(bottom - top);
                var side = new float2(-dir.y, dir.x);
                var at = bottom + dir * (LandingDepth * 0.5f);
                foreach (float sgn in new[] { 1f, -1f })
                {
                    var p = at + side * sgn * (LandingHalfWidth + 1.1f);
                    if (LeisureKit.Put(ctx, b.Occ, p, Yaw(-side * sgn), ObjectRole.Furniture, AssetMix.One(""), cat, 0.6f, Hashing.Mix(ctx.Seed, 0x5400u + (uint)b.Showers), 0.5f, 0.2f))
                    { b.Showers++; break; }
                }
            }
        }

        /// <summary>
        /// (rf-beach) Timber jetties: straight piers from the sand out over the water, in line with an access path where one
        /// is near, rooted where the beach face reaches the deck height (so the deck lands on the sand), with a T-head at the
        /// end to swim from. Like the waterfront's piers they may reach past the outline over the real water.
        /// </summary>
        private static void Jetties(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G;
            int wanted = ctx.I("jetties");
            if (wanted <= 0 || shore.Mode == ShoreMode.Assumed || !shore.HasWaterline) return;
            float maxLen = ctx.F("jettyLength");
            bool head = ctx.B("jettyHead");
            var asset = new AssetRef("", Waterfront.WaterfrontArt.Pier);
            var stations = shore.Stations(wanted, 18f, 0.5f);
            foreach (float s0 in stations)
            {
                float s = s0;
                // in line with the nearest access landing when one is close
                float best = 16f;
                float2 from = default; bool hasFrom = false;
                foreach (var (_, bottom) in b.Access)
                {
                    shore.Nearest(bottom, out _, out float a);
                    if (math.abs(a - s0) < best) { best = math.abs(a - s0); s = a; from = bottom; hasFrom = true; }
                }
                if (!InBeachRun(b, s)) continue;
                var water = shore.WaterAt(s, out var inw);
                // the deck 0.8 m over the water (over the reference ground at the waterline when the level is not trusted)
                float deck = (g.HasLevel ? g.Level : g.Ref(s)) + 0.8f;
                // the root: walking up the beach face from the water to where the ground meets the deck
                float2 root = water; bool found = false;
                for (float d = 0f; d < b.D; d += 0.5f)
                {
                    var q = water + inw * d;
                    if (g.H(q) >= deck - 0.5f) { root = q; found = true; break; }
                }
                if (!found) continue;
                var dir = -inw;
                float len = 0f, wet = 0f;
                for (float l = 2f; l <= maxLen; l += 2f)
                {
                    var q = root + dir * l;
                    bool isWet = shore.IsWet(q);
                    if (!isWet && wet > 0f) break;   // water ends (an island, the far shore)
                    if (shore.Mode == ShoreMode.Inside && !shore.Outline.ContainsDisc(q, 2f) && !isWet) break;
                    if (isWet) wet += 2f;
                    len = l;
                }
                if (wet < 8f) continue;
                var end = root + dir * len;
                // p2-beaches: the jetty starts on a path end (the access in line with it, or a spur off the promenade), so the
                // deck and the path share one node: no overhang, no step (docs/GROUND-RUNDOWN.md, bridges)
                if (!hasFrom)
                {
                    float nd = 40f;
                    foreach (var line in b.Lines)
                        foreach (var q in line) { float dd = math.distance(q, root); if (dd < nd) { nd = dd; from = q; hasFrom = true; } }
                }
                if (hasFrom && math.distance(from, root) > 1.5f && !shore.Land.CrossesBoundary(from, root, true))
                    LeisureKit.Path(ctx, b.Occ, from, root, NetRole.Pathway, AssetMix.One(ctx.S("pathAsset")));
                b.Decks.Add((root, end));
                ctx.Plan.Nets.Add(new PlannedNet
                {
                    A = new float3(root.x, deck, root.y), B = new float3(math.lerp(root, end, 1f / 3f).x, deck, math.lerp(root, end, 1f / 3f).y),
                    C = new float3(math.lerp(root, end, 2f / 3f).x, deck, math.lerp(root, end, 2f / 3f).y), D = new float3(end.x, deck, end.y),
                    Role = NetRole.Pathway, Asset = asset, FollowTerrain = false, Category = AssetCategories.NetPier,
                });
                if (head)
                {
                    var side = new float2(-dir.y, dir.x);
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        var h = end + side * sgn * 7f;
                        if (!shore.IsWet(h)) continue;
                        ctx.Plan.Nets.Add(new PlannedNet
                        {
                            A = new float3(end.x, deck, end.y), B = new float3(math.lerp(end, h, 1f / 3f).x, deck, math.lerp(end, h, 1f / 3f).y),
                            C = new float3(math.lerp(end, h, 2f / 3f).x, deck, math.lerp(end, h, 2f / 3f).y), D = new float3(h.x, deck, h.y),
                            Role = NetRole.Pathway, Asset = asset, FollowTerrain = false, Category = AssetCategories.NetPier,
                        });
                    }
                }
                // umbrellas, boats and posts keep off the jetty and a lane beside it
                b.Occ.TakeCorridor(root + inw * 2f, end, 3f, 100);
                b.Jetties++;
            }
        }

        private static void Boats(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!ctx.B("boats")) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.BeachBoat);
            if (cat == null) return;
            int groups = math.clamp((int)(shore.Length / 50f), 1, 6);
            foreach (float s in shore.Stations(groups, 8f, 0.25f))
            {
                uint v = Hashing.Mix(ctx.Seed, 0xB0A7u + (uint)b.Boats);
                int n = 2 + (int)(Hashing.Unit(v) * 3f);
                for (int k = 0; k < n; k++)
                {
                    float sk = s + (k - (n - 1) * 0.5f) * 2.6f;
                    if (!InBeachRun(b, sk)) continue;
                    var p = shore.WaterAt(sk, out var inw);
                    var q = p + inw * math.clamp(b.Wet * 0.6f, 2.5f, b.Wet + 1f);
                    float yaw = Yaw(-inw) + (Hashing.Unit(Hashing.Mix(v, (uint)k)) - 0.5f) * 0.4f;
                    if (LeisureKit.Put(ctx, b.Occ, q, yaw, ObjectRole.Furniture, AssetMix.One(""), cat, 1.2f, Hashing.Mix(v, (uint)k + 7u), 0.5f, 0.1f)) b.Boats++;
                }
            }
        }

        /// <summary>
        /// Umbrella sets in rows parallel to the water: tidy (resort) or loose (public beach: jittered, with gaps where
        /// the crowd thins out). A set is one umbrella with two loungers beside its canopy, all facing the water; one
        /// umbrella and one lounger model per build.
        /// </summary>
        private static void UmbrellaSets(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            float density = ctx.F("umbrellas");
            if (density <= 0f) return;
            string umbrella = LeisureKit.Category(ctx, ctx.S("umbrellaAsset"), AssetCategories.BeachUmbrella, AssetCategories.CafeSeating);
            if (umbrella == null) return;
            string lounger = LeisureKit.Category(ctx, ctx.S("loungerAsset"), AssetCategories.BeachLounger, AssetCategories.Bench);
            float d0 = b.Wet + 3f, d1 = b.D - (b.HasPath ? b.Hw + 3f : 1.5f);
            if (d1 <= d0) return;
            float beachLen = 0f;
            foreach (var r in b.BeachRuns) beachLen += r.y - r.x;
            int wanted = (int)math.round(density * beachLen * (d1 - d0) / 10000f);
            if (wanted <= 0) return;
            bool tidy = ctx.S("layout") == "rows";
            uint clump = Hashing.Mix(ctx.Seed, 0x0C10u);

            bool Set(float2 p, uint v)
            {
                shore.Nearest(p, out var inw, out float pAlong);
                float dp = shore.FromWater(p);
                if (dp < d0 || dp > d1 || !InBeachRun(b, pAlong)) return false;   // never on a cliff top beside the beach
                if (b.G.H(p) > b.G.Ref(pAlong) + 1.4f + ctx.F("slope") / 100f * dp) return false;   // on the sand, not on a bank above it
                var toWater = -inw;
                var tan = new float2(inw.y, -inw.x);
                float jitter = tidy ? 0f : (Hashing.Unit(Hashing.Mix(v, 3u)) - 0.5f) * 0.35f;
                if (!occ.Free(p, 1.6f, 0.6f, 0.3f)) return false;
                // Loungers beside the canopy (inside it the game hides one of the two overlapping props).
                var spots = new List<float2>();
                int count = tidy ? 2 : (Hashing.Unit(Hashing.Mix(v, 4u)) < 0.2f ? 1 : 2);
                for (int k = 0; k < count; k++)
                {
                    float side = count == 1 ? (Hashing.Unit(v) < 0.5f ? 1f : -1f) : (k == 0 ? -1f : 1f);
                    var q = p + tan * side * 2.2f + inw * 0.3f;
                    if (lounger != null && occ.Free(q, 0.7f, 0.4f, 0.1f)) spots.Add(q);
                }
                ctx.Object(p, Yaw(toWater) + jitter, ObjectRole.Furniture, b.Umbrella, v, -1, umbrella);
                b.UmbrellaSpots.Add(p);
                occ.Take(p, 1.6f, 1);
                b.Umbrellas++;
                foreach (var q in spots)
                {
                    var role = lounger == AssetCategories.Bench ? ObjectRole.Bench : ObjectRole.Furniture;
                    ctx.Object(q, Yaw(toWater) + jitter, role, b.Lounger, Hashing.Mix(v, (uint)b.Loungers + 11u), -1, role == ObjectRole.Bench ? null : lounger);
                    occ.Take(q, 0.7f, 1);
                    b.Loungers++;
                }
                return true;
            }

            float rowGap = tidy ? 6.5f : 7.5f;
            int rows = math.max(1, (int)math.floor((d1 - d0) / rowGap) + 1);
            // Loose rows skip ~35 % of the slots and some slots collide with aisles/posts: a denser grid for the same count.
            float spacing = math.max(tidy ? 6f : 6.5f, rows * beachLen / math.max(1, wanted) * (tidy ? 0.9f : 0.65f));
            for (int r = 0; r < rows && b.Umbrellas < wanted; r++)
            {
                float d = d0 + 0.5f + r * rowGap;
                if (d > d1) break;
                foreach (var line in shore.OffsetFromWater(d, 1.5f, 6f, 2f, p => b.G.OnCliff(p)))
                {
                    float len = Shoreline.LengthOf(line);
                    for (float s = (r & 1) == 0 ? spacing * 0.5f : spacing; s < len && b.Umbrellas < wanted; s += spacing)
                    {
                        uint v = Hashing.Mix(ctx.Seed, 0x0B00u + (uint)(r * 997) + (uint)(s * 10f));
                        var p = Shoreline.PointOn(line, s, out var t);
                        if (!tidy)
                        {
                            // Loose: a gap where the crowd thins (value-noise clumps), the rest nudged off the grid.
                            float chance = 0.45f + 0.55f * Scatter.Clustering(p, 26f, clump, 0.2f);
                            if (Hashing.Unit(Hashing.Mix(clump, (int)(p.x * 13f), (int)(p.y * 7f))) > chance) continue;
                            var n = new float2(-t.y, t.x);
                            p += t * (Hashing.Unit(Hashing.Mix(v, 7u)) - 0.5f) * spacing * 0.35f + n * (Hashing.Unit(Hashing.Mix(v, 8u)) - 0.5f) * 2.4f;
                        }
                        // A slot blocked by an aisle or a post slides along the row a little.
                        if (!Set(p, v) && !Set(p + t * 2.6f, v)) Set(p - t * 2.6f, v);
                    }
                }
            }
        }

        private static void FirePits(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!ctx.B("fires")) return;
            // No vanilla fire pit, and the camping fallback is makeshift tents: build the ring from small rocks instead.
            if (!AssetAvailability.Has(AssetCategories.Rock)) return;
            int wanted = math.clamp((int)(shore.Length / 60f), 1, 4);
            float d = math.clamp((b.Wet + b.D) * 0.55f, b.Wet + 4f, b.D - 4f);
            foreach (float s in shore.Stations(wanted, 10f, 0.75f))
            {
                if (!InBeachRun(b, s)) continue;
                var p = shore.WaterAt(s, out var inw);
                var c = p + inw * d;
                if (!b.Occ.Free(c, 3.6f, 0.8f, 0.3f)) continue;
                // A ring of small beach stones, rough campground benches around it.
                uint v = Hashing.Mix(ctx.Seed, 0xF1E0u + (uint)b.Fires);
                for (int k = 0; k < 7; k++)
                {
                    float a = k * 0.8976f + Hashing.Unit(Hashing.Mix(v, (uint)k + 50u)) * 0.3f;
                    var o = ctx.Object(c + new float2(math.cos(a), math.sin(a)) * 0.85f, a, ObjectRole.Furniture,
                        new AssetRef("", "Rock05", "Rock12", "Rock21"), Hashing.Mix(v, (uint)k + 60u), -1, AssetCategories.Rock);
                    o.Scale = 0.55f;
                }
                b.Occ.Take(c, 1.3f, 1);
                b.FirePlaces.Add(c);
                float phase = Hashing.Unit(v) * 6.283f;
                for (int k = 0; k < 3; k++)
                {
                    float a = phase + k * 2.094f;
                    var q = c + new float2(math.cos(a), math.sin(a)) * 2.7f;
                    LeisureKit.Put(ctx, b.Occ, q, LeisureKit.Yaw(q, c), ObjectRole.Bench, new AssetRef("", "CampgroundBench01", "CampgroundBench02"), null, 1.1f, Hashing.Mix(v, (uint)k), 0.5f, 0.1f);
                }
                b.Fires++;
            }
        }

        // ───────────── back path & behind ─────────────

        /// <summary>Benches on the sea side of the promenade facing the water, bins beside every other one, lamps in rhythm on the land side.</summary>
        private static void BackPathFurniture(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (!b.HasPath) return;
            float benchSpacing = ctx.F("benchSpacing");
            bool lamps = ctx.B("lamps");
            int benches = 0, lampsN = 0;
            for (int li = 0; li < b.Lines.Count; li++)
            {
                var line = b.Lines[li];
                float len = Shoreline.LengthOf(line);
                if (benchSpacing > 0f)
                    for (float s = benchSpacing * 0.5f; s < len; s += benchSpacing)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out _);
                        var q = p - inw * (b.Hw + 0.9f);
                        uint v = Hashing.Mix(ctx.Seed, 0xBE00u + (uint)benches + (uint)li * 1000u);
                        if (!LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Bench, AssetMix.Pick(BeachArt.Benches, 0u), null, 1.1f, v, 0.5f, 0.2f)) continue;
                        b.G.Sculpt.PadDisc(q, 1.3f, b.Profiles[li](s), 1.5f);
                        benches++;
                        if ((benches & 1) == 0)
                        {
                            var tan = new float2(inw.y, -inw.x);
                            LeisureKit.Put(ctx, b.Occ, q + tan * 2.3f, 0f, ObjectRole.Bin, AssetMix.Pick(BeachArt.Bins, 0u), null, 0.5f, Hashing.Mix(v, 1u), 0.4f, 0.2f);
                        }
                    }
                if (lamps)
                    for (float s = 12f; s < len; s += 28f)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        shore.Nearest(p, out var inw, out _);
                        if (LeisureKit.Put(ctx, b.Occ, p + inw * (b.Hw + 0.8f), 0f, ObjectRole.Lamp, AssetMix.Pick(BeachArt.Lamps, 0u), null, 0.4f, Hashing.Mix(ctx.Seed, 0x1A00u + (uint)lampsN), 0.4f, 0.2f))
                            lampsN++;
                    }
            }
        }

        private static void HutsAndKiosks(Work b)
        {
            if (!b.HasPath) return;
            Kiosks(b);   // first: a long hut row would otherwise take every spot behind the path
            Huts(b);
        }

        /// <summary>Kiosks beside the access junctions, behind the walk on a level pad (or on the sand side when the bank is steep).</summary>
        private static void Kiosks(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G;
            int kiosks = ctx.I("kiosks");
            if (kiosks <= 0) return;
            string kcat = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
            if (kcat == null) return;
            var tops = new List<float2>();
            foreach (var (top, _) in b.Access) tops.Add(top);
            if (tops.Count == 0) foreach (var line in b.Lines) tops.Add(Shoreline.PointOn(line, Shoreline.LengthOf(line) * 0.5f, out _));
            for (int i = 0; i < kiosks * 2 && b.Kiosks < kiosks; i++)
            {
                // Spread over the access junctions, then try the remaining ones.
                int idx = i < kiosks ? (int)((i + 0.5f) * tops.Count / kiosks) % tops.Count : (i * 3 + 1) % tops.Count;
                var p = tops[idx];
                shore.Nearest(p, out var inw, out _);
                var tan = new float2(inw.y, -inw.x);
                foreach (float side in new[] { 1f, -1f })
                    foreach (float along in new[] { 6f, -6f, 12f, -12f })
                    {
                        var q = p + inw * side * (b.Hw + 4f) + tan * along;
                        if (!FreeIgnoringGround(b.Occ, q, 2.5f, 0.8f, 0.3f) || !g.MakeLevel(q, 2.5f, 1.4f, out _)) continue;
                        if (!LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, AssetMix.Pick(BeachArt.Kiosks, (uint)b.Kiosks), kcat, 2.5f, Hashing.Mix(ctx.Seed, 0x6105u + (uint)b.Kiosks), 0.8f, 0.3f)) continue;
                        b.Kiosks++;
                        goto next;
                    }
                next:;
            }
        }

        private static void Huts(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G;
            string mode = ctx.S("huts");
            string hut = mode == "none" ? null : LeisureKit.Category(ctx, ctx.S("hutAsset"), AssetCategories.BeachHut, AssetCategories.GardenShed);
            if (hut == null) return;
            var hutAsset = AssetMix.One(ctx.S("hutAsset"));
            float hd = b.Hw + 2.8f;
            foreach (var line in b.Lines)
            {
                float len = Shoreline.LengthOf(line);
                var places = new List<float>();
                if (mode == "row")
                    // p2-beaches: terraces of four to seven with a gap between (one unbroken 3.6 m row read as a fence of boxes)
                    for (float s = 4f; s < len - 4f; )
                    {
                        int run = 4 + (int)(Hashing.Unit(Hashing.Mix(ctx.Seed, 0x4071u + (uint)places.Count)) * 4f);
                        for (int k = 0; k < run && s < len - 4f; k++, s += 3.6f) places.Add(s);
                        s += 9f;
                    }
                else
                {
                    int groups = math.max(1, (int)(len / 70f));
                    for (int gi = 0; gi < groups; gi++)
                    {
                        float c = (gi + 0.5f) * len / groups + 9f;
                        for (int k = -1; k <= 1; k++) places.Add(c + k * 3.6f);
                    }
                }
                foreach (float s in places)
                {
                    if (s < 0f || s > len) continue;
                    var p = Shoreline.PointOn(line, s, out _);
                    shore.Nearest(p, out var inw, out _);
                    var q = p + inw * hd;
                    // Huts stand level: on a pad cut into a gentle bank, never tilted on a steep one.
                    if (!FreeIgnoringGround(b.Occ, q, 1.5f, 0.6f, 0.15f) || !g.MakeLevel(q, 1.5f, 1.2f, out _)) continue;
                    if (LeisureKit.Put(ctx, b.Occ, q, Yaw(-inw), ObjectRole.Furniture, hutAsset, hut, 1.5f, Hashing.Mix(ctx.Seed, 0x4070u + (uint)b.Huts), 0.6f, 0.15f))
                        b.Huts++;
                }
            }
        }

        /// <summary>
        /// Beach grass through the sand blend behind the promenade: sparse where the sand is still strong, thicker as
        /// it fades into the ground (dune backdrops plant their own belt).
        /// </summary>
        private static void BlendGrass(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore;
            if (ctx.S("backdrop") == "dunes" || b.Blend < 3f) return;
            float pct = ctx.F("grassDensity") / 100f;
            if (pct <= 0f) return;
            float from = b.D + (b.HasPath ? b.Hw * 2f + 1.5f : 1f), to = b.D + b.Blend + 4f;
            var grass = BeachArt.Or(ctx.L("grassSpecies"), BeachArt.DuneGrass);
            uint salt = Hashing.Mix(ctx.Seed, 0xB1E9u);
            foreach (var ring in shore.BandFromWater(from, to))
            {
                var band = LayerRegion.From(ring);
                if (band == null) continue;
                int wanted = (int)(band.Area / 10000f * 380f * pct);
                LeisureKit.Plant(ctx, b.Occ, band, wanted, ObjectRole.Bush, grass, 0.9f, 7f, 0.35f, 0xB1E5u, p =>
                {
                    float t = math.saturate((shore.FromWater(p) - b.D) / b.Blend);
                    return b.G.OnCliff(p) || Hashing.Unit(Hashing.Mix(salt, (int)(p.x * 5f), (int)(p.y * 5f))) > 0.25f + 0.75f * t;
                });
            }
        }

        private static void Backdrop(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var occ = b.Occ;
            string backdrop = ctx.S("backdrop");
            float behind = b.PathDw + (b.HasPath ? b.Hw + 2.2f : 1.5f);
            float duneEnd = behind + ctx.F("duneDepth");
            var trees = BeachArt.Or(ctx.L("treeSpecies"), BeachArt.Trees(b.Style, backdrop));
            var grass = BeachArt.Or(ctx.L("grassSpecies"), backdrop == "dunes" ? BeachArt.DuneGrass : BeachArt.Shrubs);
            bool canTrees = trees.Length > 0 || AssetAvailability.Has(AssetCategories.Tree);
            float treeDensity = ctx.F("treeDensity");
            float far = shore.MaxDepth + Shoreline.MaxGap + 20f;
            // Plants grow on slopes (not on cliff faces).
            var keep = occ.GroundOk;
            occ.GroundOk = (p, r) => !b.G.OnCliff(p);
            try
            {
                if (backdrop == "dunes")
                {
                    // Marram grass in clumps over the dunes: thin on the sandy foot, densest on the crests and behind.
                    float pct = ctx.F("grassDensity") / 100f;
                    uint salt = Hashing.Mix(ctx.Seed, 0xD6A6u);
                    foreach (var ring in shore.BandFromWater(behind, duneEnd))
                    {
                        var band = LayerRegion.From(ring);
                        if (band == null) continue;
                        // p2-beaches: tufts, not a carpet: the sea face of the foredune bare, a few on its crest, more in the slack
                        // and on the back dune (450 /ha covered the sand and the relief with round bushes)
                        // marram where the sand stands high (crests and upper slopes), none in the slacks: the grass draws the ridges
                        // (the game's light barely shades bare sand: 5 m dunes read flat from 45°)
                        float duneH = math.max(1f, ctx.F("duneHeight"));
                        int wanted = (int)(band.Area / 10000f * 320f * pct);
                        LeisureKit.Plant(ctx, occ, band, wanted, ObjectRole.Bush, grass, 0.9f, 9f, 0.2f, 0xD6A5u, p =>
                        {
                            float u = (shore.FromWater(p) - behind) / math.max(1f, duneEnd - behind);
                            if (u < 0.2f) return true;   // the sea face stays bare
                            float high = math.saturate((b.G.H(p) - ctx.H(p)) / (0.55f * duneH));
                            return Hashing.Unit(Hashing.Mix(salt, (int)(p.x * 5f), (int)(p.y * 5f))) > 0.9f * high * high;
                        });
                    }
                    occ.GroundOk = keep;
                    if (ctx.B("duneFence")) SandFences(b, behind - 0.6f);
                    occ.GroundOk = (p, r) => !b.G.OnCliff(p);
                    if (canTrees && treeDensity > 0f)
                        foreach (var ring in shore.BandFromWater(duneEnd, far))
                        {
                            var band = LayerRegion.From(ring);
                            if (band == null) continue;
                            LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * treeDensity * 1.5f), ObjectRole.Tree, trees, 2.5f, 20f, 0.3f, 0x7E55u, null, 1, 3);
                        }
                    return;
                }
                if (backdrop == "none" && treeDensity <= 0f) return;
                // (rf-beach) A lawn backdrop gets an avenue: one tree every ~12 m along the promenade's land side (palms on a
                // resort beach), instead of only trees dotted over the lawn.
                if (backdrop == "lawn" && b.HasPath && canTrees)
                    for (int li = 0; li < b.Lines.Count; li++)
                    {
                        var line = b.Lines[li];
                        float len = Shoreline.LengthOf(line);
                        int k = 0;
                        for (float s = 6f; s < len - 3f; s += 12f, k++)
                        {
                            var p = Shoreline.PointOn(line, s, out _);
                            shore.Nearest(p, out var inw, out _);
                            uint v = Hashing.Mix(ctx.Seed, 0xA7E0u + (uint)k + (uint)li * 500u);
                            LeisureKit.Put(ctx, occ, p + inw * (b.Hw + 3.2f), Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(trees, (uint)k), null, 2.5f, v, 0.4f, 0.2f);
                        }
                    }
                float factor = backdrop == "woods" ? 1.8f : 1f;
                // Trees stand back from the sand blend (they grow where the ground is ground again).
                float from = (backdrop == "woods" ? behind : behind + 3f) + b.Blend * 0.5f;
                if (canTrees && treeDensity > 0f)
                    foreach (var ring in shore.BandFromWater(from, far))
                    {
                        var band = LayerRegion.From(ring);
                        if (band == null) continue;
                        LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * treeDensity * factor), ObjectRole.Tree, trees, 2.5f,
                            backdrop == "lawn" ? 26f : 16f, backdrop == "lawn" ? 0.15f : 0.35f, 0x7E56u,
                            b.Forms.Ravines.Count > 0 ? (System.Func<float2, bool>)(p => RavineAt(b, p)) : null, 1, 3);   // trees hid the ravines
                    }
                if (backdrop == "woods")
                {
                    float pct = ctx.F("grassDensity") / 100f;
                    foreach (var ring in shore.BandFromWater(from, from + 6f))
                    {
                        var band = LayerRegion.From(ring);
                        if (band == null) continue;
                        LeisureKit.Plant(ctx, occ, band, (int)(band.Area / 10000f * 120f * pct), ObjectRole.Bush, grass, 1f, 10f, 0.2f, 0xB0B5u, null);
                    }
                }
            }
            finally { occ.GroundOk = keep; }
        }

        /// <summary>Sand fences along the dune foot: short runs with gaps where paths cross, only where the ground is even.</summary>
        private static void SandFences(Work b, float d)
        {
            var ctx = b.Ctx; var occ = b.Occ;
            foreach (var line in b.Shore.OffsetFromWater(d, 1f, 10f, 3f, p => b.G.OnCliff(p)))
            {
                float len = Shoreline.LengthOf(line);
                const float run = 16f, gap = 5f;
                for (float s = 2f; s + 4f < len; s += run + gap)
                {
                    var a = Shoreline.PointOn(line, s, out _);
                    var e = Shoreline.PointOn(line, math.min(len, s + run), out _);
                    if (math.distance(a, e) < 4f) continue;
                    bool blocked = false;
                    for (float t = 0f; t <= 1.001f; t += 0.125f)
                    {
                        var q = math.lerp(a, e, t);
                        if (occ.Paths.Overlaps(q, 0.6f, 0.5f) || occ.Discs.Overlaps(q, 0.3f, 0.1f) || b.G.Sculpt.GradeAt(q) > 0.15f) { blocked = true; break; }
                    }
                    if (blocked || b.Shore.Land.CrossesBoundary(a, e, false)) continue;
                    ctx.StraightNet(a, e, NetRole.Fence, AssetMix.Pick(BeachArt.SandFence, 0u));
                    occ.TakeCorridor(a, e, 0.3f, 5);
                }
            }
        }
    }
}
