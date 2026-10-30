// Scrapyard: a fenced industrial yard, laid out the way real yards work —
//   * a gate compound (weighbridge across the haul road, site office, barrier, floodlight),
//   * a haul road from the gate onto a loop that runs round the yard, so trucks never reverse far,
//   * an outer storage band between the loop and the fence (bays, racks, container or wreck rows),
//   * the core inside the loop, filled by the subtype:
//       scrapyard   concrete-block material bays round the edge, a terraformed scrap mountain with a
//                   material handler working it, secondary heaps, a line of baled cars, skips;
//       salvage     pick-your-part rows of goods back to back with aisles (walkable when visitors may
//                   come in), racks of pallets/barrels along the fence, a workshop and forklifts;
//       containers  blocks of containers in tight rows with reach-stacker aisles, floodlight masts at
//                   the block ends, a row along the fence;
//       tires       tyre mountains on terraformed mounds, puddles, weeds pushing through;
//       cars        a car graveyard: rows of wrecks parked nose to aisle at messy angles, an
//                   overgrown corner where trees grow through them, a crusher by the gate,
//   * gritty details: oil and dirt decals round machines and heaps, puddles on the haul road,
//     cracks on concrete, stray junk by the fence, weeds along it, dirt paint under heaps, a
//     screening tree belt, a fence that may be broken.
// Terrain: the yard can be levelled to one pad, and heaps sit on real terraformed mounds — both
// blended back to the original ground at the outline.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Scrapyard
{
    [AreaGenerator(310, Group = "industrial")]
    public sealed class ScrapyardLayer : LeisureLayer
    {
        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        public const string LayerId = "scrapyard";
        public static readonly string[] Styles = { "scrapyard", "salvage", "containers", "tires", "cars" };
        public const string NoCoreKey = "Placemaker.Scrapyard.Issue.NoCore";

        protected override string Area => "Scrapyard";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/PropsIndustrial.svg";
        public override float MinArea => 600f;
        protected override int WantedEntrances(LayerRegion region) => region.Area > 25000f ? 2 : 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(JunkPilesAddOn.LayerId), Feature("skipRow"),
            Feature("decals", ("theme", "dirt"), ("density", 1.5), ("clustering", 70.0)),
            Feature("rocks", ("size", "small"), ("clusters", 3.0)), YardKit.Labelled(Feature("bigtrees", ("density", 1.0)), "Placemaker.Scrapyard.Feature.bigtrees"),
            StreetShopsFeature(Area),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "scrapyard", ControlTier.Essential, "yard", Styles).Tip();
            c.Slider("fill", 70, 20, 100, 5, "%", ControlTier.Essential, "yard").Tip();
            c.Slider("aisle", 8, 5, 16, 1, "m", ControlTier.Essential, "yard").Tip();
            c.Slider("heapHeight", 4, 0, 12, 0.5, "m", ControlTier.Essential, "yard").Tip();

            c.Slider("machines", 2, 0, 8, 1, "count", ControlTier.More, "extras").Tip();
            c.Slider("skips", 6, 0, 20, 1, "count", ControlTier.More, "extras");
            c.Slider("strays", 20, 0, 80, 5, "perHa", ControlTier.More, "extras").Tip();
            c.Toggle("visitorPaths", false, ControlTier.More, "extras").Tip();
            c.Slider("overgrowth", 0, 0, 100, 5, "%", ControlTier.More, "extras").Tip();

            c.Choice("fence", "fence", ControlTier.More, "edge", "none", "fence", "broken").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "edge").When("fence=fence|broken");
            c.Toggle("screenTrees", true, ControlTier.More, "edge").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "edge").When("screenTrees=true");
            c.Slider("weeds", 30, 0, 150, 5, "perHa", ControlTier.More, "edge").Tip();
            c.Toggle("office", true, ControlTier.More, "edge").Tip();
            c.Toggle("weighbridge", true, ControlTier.More, "edge").Tip();
            c.Toggle("floodlights", true, ControlTier.More, "edge");

            c.Choice("ground", "dirt", ControlTier.More, "ground", "dirt", "gravel", "concrete", "mixed");
            c.Toggle("paint", true, ControlTier.More, "ground").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "ground").When("paint=true").Tip();
            c.Toggle("level", true, ControlTier.More, "ground").Tip();
            c.Slider("grime", 1.5, 0, 6, 0.25, "per100m2", ControlTier.More, "ground").Tip();

            c.AssetMulti("scrapAssets", AssetCategories.ScrapHeap, ControlTier.More, "assets");
            c.AssetMulti("wreckAssets", AssetCategories.ScrapWreck, ControlTier.More, "assets");
            c.AssetMulti("containerAssets", AssetCategories.ScrapContainer, ControlTier.More, "assets");
            c.AssetMulti("tireAssets", AssetCategories.ScrapTire, ControlTier.More, "assets");
            c.AssetMulti("machineAssets", AssetCategories.ScrapMachine, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets").When("visitorPaths=true");
            c.Asset("groundSurface", AssetCategories.SurfaceNatural, ControlTier.More, "assets");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.RandomRange("fill", 50, 90);
            c.RandomRange("heapHeight", 0, 6);
            c.Fixed("paintLayer");
        }

        protected override void DefinePresets()
        {
            Preset("scrapyard", ("style", "scrapyard"), ("ground", "dirt"), ("heapHeight", 4.0), ("fill", 70.0), ("machines", 2.0), ("skips", 6.0));
            Preset("salvage", ("style", "salvage"), ("ground", "gravel"), ("heapHeight", 0.0), ("fill", 75.0), ("visitorPaths", true),
                ("aisle", 6.0), ("machines", 2.0), ("skips", 2.0));
            Preset("containers", ("style", "containers"), ("ground", "concrete"), ("heapHeight", 0.0), ("fill", 85.0), ("aisle", 12.0),
                ("machines", 3.0), ("screenTrees", false), ("weeds", 5.0), ("skips", 0.0), ("grime", 1.0));
            Preset("tires", ("style", "tires"), ("ground", "dirt"), ("heapHeight", 3.0), ("fill", 60.0), ("weeds", 70.0), ("fence", "broken"),
                ("machines", 1.0), ("skips", 0.0), ("grime", 2.5), ("strays", 40.0));
            Preset("cars", ("style", "cars"), ("ground", "mixed"), ("heapHeight", 0.0), ("fill", 70.0), ("overgrowth", 30.0),
                ("visitorPaths", true), ("aisle", 6.0), ("machines", 1.0), ("weeds", 60.0), ("skips", 2.0));
        }

        // ───────────── planning ─────────────

        private struct Anchor { public float2 P; public float R; public Anchor(float2 p, float r) { P = p; R = r; } }

        private sealed class Yard
        {
            public LayerContext Ctx;
            public LayerRegion Region;
            public Occupancy Occ;
            public Occupancy ReservedOnly;
            public List<Gate> Gates;
            public float2 Centre, Axis, Side;
            public float Major, Minor;
            public float Fill, Aisle, EdgeInset, BandDepth;
            public float2[] Loop;
            public LayerRegion Core;
            public float2 Start;
            public readonly List<Mound> Mounds = new List<Mound>();
            public readonly List<Anchor> Anchors = new List<Anchor>();
            public readonly List<float2> HaulPoints = new List<float2>();
            public string HaulCategory;
            public SurfaceRole HaulRole;
            public int Heaps, Wrecks, Containers, Machines, Bays, Junk;
            public uint Salt(uint k) => Hashing.Mix(Ctx.Seed, k);
        }

        /// <summary>Type-specific curated defaults (over GritAssets' shared sets); null = shared only.</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtObjects = null, ArtSurfaces = null;

        // Art pass: whatever the player left automatic gets the curated gritty defaults (GritAssets).
        protected override void PlanCore(LayerContext ctx) { PlanArt(ctx); GritAssets.Apply(ctx.Plan, ctx.Seed, ArtObjects, ArtSurfaces); }

        private void PlanArt(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var y = new Yard
            {
                Ctx = ctx, Region = region,
                Occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site),
                ReservedOnly = new Occupancy(region, ctx.Input.Reserved, ctx.Site),
                Gates = Gates(ctx),
                Fill = math.saturate(ctx.F("fill") / 100f), Aisle = math.max(4f, ctx.F("aisle")),
            };
            YardKit.GridFrame(region, out y.Centre, out y.Axis, out y.Side, out y.Major, out y.Minor);
            string style = ctx.S("style");

            Ground(y, ctx.S("ground"));
            YardKit.Fence(ctx, y.Gates, ctx.S("fence"), AssetMix.One(ctx.S("fenceAsset")), y.Salt(0xFE7Cu));
            y.EdgeInset = 1.5f;
            // Room for the screening belt is kept now; the trees go in after the yard so roads,
            // paths and bays never have to dodge them.
            bool screen = ctx.B("screenTrees") && region.Area > 1500f;
            if (screen) y.EdgeInset = 6.2f;
            ctx.Check();

            Circulation(y);
            int cabins = 0;
            if (y.Gates.Count > 0)
                YardKit.GateCompound(ctx, y.Occ, y.Gates[0], y.Aisle, ctx.B("office"), ctx.B("weighbridge"), ctx.B("floodlights"), null, y.Salt(0x6A7Eu), out cabins);
            ctx.Check();

            switch (style)
            {
                case "salvage": Salvage(y); break;
                case "containers": ContainerDepot(y); break;
                case "tires": TireDump(y); break;
                case "cars": CarGraveyard(y); break;
                default: ClassicYard(y); break;
            }
            ctx.Check();

            if (screen) YardKit.Screen(ctx, y.Occ, y.Gates, 2.6f, 7.5f, ctx.L("treeSpecies"), y.Salt(0x5C2Eu));
            Skips(y, ctx.I("skips"));
            Machines(y, ctx.I("machines"), style);
            Strays(y, ctx.F("strays"));
            if (style == "cars" && ctx.F("overgrowth") > 0f) Overgrowth(y, ctx.F("overgrowth") / 100f);
            YardKit.Weeds(ctx, y.Occ, ctx.F("weeds"), 5f + y.EdgeInset, y.Salt(0x3EEDu));
            ctx.Check();
            Grime(y, ctx.F("grime"), style);
            if (ctx.B("paint")) PaintGround(y);
            if (ctx.B("paint")) Yards.YardWear.Lay(ctx, Yards.YardWear.Of(ctx, Id, style, ctx.S("ground"), y.Gates));

            var terrain = Shape(y);
            EmitGreenRegions(ctx, y.Occ, 150f);
            YardKit.Settle(ctx, terrain);

            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Heaps", y.Heaps, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Wrecks", y.Wrecks, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Containers", y.Containers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Machines", y.Machines, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Bays", y.Bays, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.JunkItems", y.Junk, Keys.UnitCount);
            ctx.Plan.Facts["cabins"] = cabins;
            if (terrain.Cells > 0) ctx.Stat("Placemaker.Stat.TerrainChange", System.Math.Round(math.max(terrain.MaxOffset, -terrain.MinOffset), 1), Keys.UnitM);
        }

        // ───────────── ground & circulation ─────────────

        private static void Ground(Yard y, string mode)
        {
            var ctx = y.Ctx;
            var chosen = AssetMix.One(ctx.S("groundSurface"));
            switch (mode)
            {
                case "concrete":
                    ctx.Surface(y.Region.Ring, SurfaceRole.Paving, AssetMix.One(""), YardKit.CatQuiet(AssetCategories.SurfacePaving, AssetCategories.SurfaceAsphalt) ?? AssetCategories.SurfacePaving);
                    y.HaulRole = SurfaceRole.Asphalt; y.HaulCategory = AssetCategories.SurfaceAsphalt;
                    break;
                case "gravel":
                    ctx.Surface(y.Region.Ring, SurfaceRole.Ground, chosen, AssetCategories.SurfaceNatural);
                    y.HaulRole = SurfaceRole.Ground; y.HaulCategory = YardKit.CatQuiet(AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural) ?? AssetCategories.SurfaceNatural;
                    break;
                case "mixed":
                    // Natural ground (no surface): grass and weeds between wrecks; dirt only where wheels go.
                    y.HaulRole = SurfaceRole.Ground; y.HaulCategory = YardKit.CatQuiet(AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural) ?? AssetCategories.SurfaceNatural;
                    break;
                default:
                    ctx.Surface(y.Region.Ring, SurfaceRole.Ground, chosen, YardKit.CatQuiet(AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural) ?? AssetCategories.SurfaceNatural);
                    y.HaulRole = SurfaceRole.Ground; y.HaulCategory = AssetCategories.SurfaceNatural;
                    break;
            }
        }

        /// <summary>Haul loop round the core (when the shape allows) and the spine from the first gate onto it.</summary>
        private static void Circulation(Yard y)
        {
            var ctx = y.Ctx; var region = y.Region;
            y.BandDepth = math.clamp(y.Minor * 0.12f, 7f, 14f);
            float loopAt = y.EdgeInset + y.BandDepth + 1f + y.Aisle * 0.5f;
            var loop = region.Area > 4000f ? region.InsetVertices(loopAt, 2.5f) : null;
            bool ok = loop != null && Poly.SignedArea(loop) > 600f && Poly.IsSimple(loop);
            if (ok)
                foreach (var v in loop)
                    if (!region.Contains(v) || region.BoundaryDistance(v, loopAt + 1f) < loopAt * 0.8f) { ok = false; break; }
            var haul = AssetMix.One("");
            if (ok)
            {
                y.Loop = loop;
                var closed = new List<float2>(loop) { loop[0] };
                YardKit.Haul(ctx, y.Occ, closed, y.Aisle, y.HaulRole, haul, y.HaulCategory);
                foreach (var p in loop) y.HaulPoints.Add(p);
                var coreRing = LayerRegion.From(loop)?.InsetVertices(y.Aisle * 0.5f + 1f, 2.5f);
                if (coreRing != null && Poly.IsSimple(coreRing) && Poly.SignedArea(coreRing) > 200f) y.Core = LayerRegion.From(coreRing);
            }
            if (y.Core == null) y.Core = region;
            if (y.Gates.Count == 0) { y.Start = y.Core.Centroid; return; }
            bool startSet = false;
            foreach (var g in y.Gates)
            {
                var inward = region.InwardNormal(g.Segment);
                var a = g.Position + inward * 0.3f;
                float2 b;
                if (y.Loop != null)
                {
                    var lr = LayerRegion.From(y.Loop);
                    b = lr != null ? lr.ClosestOnBoundary(a, out _, out _) : a + inward * 10f;
                }
                else
                {
                    var deep = region.DeepestPoint(out float ins, null, 12);
                    b = YardKit.Inside(region, a + inward * math.min(math.distance(a, deep), math.max(10f, ins)), deep, 1f);
                }
                if (region.CrossesBoundary(a, b, true)) b = YardKit.Inside(region, a + inward * 8f, region.Centroid, 1f);
                YardKit.Haul(ctx, y.Occ, new[] { a, b }, y.Aisle, y.HaulRole, haul, y.HaulCategory);
                y.HaulPoints.Add(math.lerp(a, b, 0.5f));
                if (!startSet) { y.Start = b; startSet = true; }
            }
        }

        // ───────────── subtypes ─────────────

        /// <summary>Material bays round the edge, a scrap mountain worked by a handler, secondary heaps, baled cars.</summary>
        private static void ClassicYard(Yard y)
        {
            var ctx = y.Ctx;
            var scrap = ctx.L("scrapAssets");
            string heapCat = YardKit.Cat(ctx, scrap, AssetCategories.ScrapHeap, AssetCategories.ScrapWreck, AssetCategories.ScrapPallet);
            float heapH = ctx.F("heapHeight");

            // The mountain first: the deepest free point of the core.
            var c = y.Core.DeepestPoint(out float ins, (q, cap) => y.Occ.Clearance(q, cap), 14);
            float R = math.min(ins - 2f, (6f + math.sqrt(y.Core.Area) * 0.22f) * (0.6f + 0.4f * y.Fill));
            if (R >= 4f && heapCat != null)
            {
                // Art pass: the peak is a few big rubble/ore loads (one read from above), the flanks
                // fewer, bigger scrap props — ~45% of the old count, which stamped 130 small items.
                if (scrap == null || scrap.Length == 0) y.Junk += Peak(y, c, R);
                int n = (int)(R * R * 0.15f * y.Fill) + 5;
                y.Junk += YardKit.Heap(ctx, y.Occ, c, R, n, scrap, heapCat, 2.8f, y.Salt(0x3A11u), 0.8f, 1.4f);
                y.Mounds.Add(new Mound { Center = c, Radius = R * 1.15f, Height = heapH, Flat = 0.2f });
                y.Anchors.Add(new Anchor(c, R));
                y.Heaps++;
            }
            else if (heapCat != null) y.Ctx.Issue(IssueSeverity.Info, NoCoreKey);

            // Bays round the edge, materials cycling like a sorted yard.
            string[] cycle = { AssetCategories.ScrapHeap, AssetCategories.ScrapHeap, AssetCategories.ScrapTire, AssetCategories.ScrapBarrel, AssetCategories.ScrapPallet, AssetCategories.ScrapWreck };
            float bayDepth = math.max(6f, y.BandDepth - 1f);
            y.Bays += YardKit.Bays(ctx, y.Occ, y.EdgeInset, 10f, bayDepth, y.Gates, 16f, BackHalf(y), (i, bc, inward, hl, hw) =>
            {
                string cat = YardKit.CatQuiet(cycle[i % cycle.Length], AssetCategories.ScrapHeap, AssetCategories.ScrapPallet);
                if (cat == null) return;
                float r = math.min(hl, hw) * 0.8f;
                int n = (int)(r * r * 0.3f * (0.4f + 0.6f * y.Fill)) + 2;
                y.Junk += YardKit.Heap(ctx, y.Occ, bc - inward * hw * 0.15f, r, n, cat == AssetCategories.ScrapHeap ? scrap : null, cat, cat == AssetCategories.ScrapWreck ? 2.6f : 1.8f, y.Salt(0xBA0u + (uint)i));
                y.Anchors.Add(new Anchor(bc, r));
                y.Heaps++;
            }, y.Salt(0xBA75u));

            // Secondary heaps scattered in the rest of the core.
            SecondaryHeaps(y, scrap, heapCat, heapH * 0.5f);

            // Baled cars: a tight row of crushed wrecks beside the spine.
            string wreck = YardKit.CatQuiet(AssetCategories.ScrapWreck);
            if (wreck != null && y.Gates.Count > 0)
            {
                var g = y.Gates[0];
                var inward = y.Region.InwardNormal(g.Segment);
                var side = new float2(inward.y, -inward.x);
                var a = g.Position + inward * 20f - side * (y.Aisle * 0.5f + 3.5f);
                var b = a + inward * 16f;
                y.Wrecks += YardKit.Row(ctx, y.Occ, a, b, 2.4f, ctx.L("wreckAssets"), wreck, 1.1f, y.Salt(0xBA1Eu), 0.05f, math.PI * 0.5f, 0.05f);
            }
        }

        /// <summary>Material bays only where the outline is in the back ~half of the yard (front stays open round the gate).</summary>
        private static System.Func<float2, bool> BackHalf(Yard y)
        {
            if (y.Gates.Count == 0) return null;
            var g = y.Gates[0].Position;
            float far = 0f;
            foreach (var v in y.Region.Ring) far = math.max(far, math.distance(v, g));
            float min = far * 0.55f;
            return c => math.distance(c, g) > min;
        }

        /// <summary>Big rubble and ore loads at the top of the scrap mountain. Returns the count placed.</summary>
        private static int Peak(Yard y, float2 c, float R)
        {
            string[] big = { "OreMiningTruck01_OrePile03", "RubblePileMixed03", "OreMiningTruck01_OrePile02", "RubblePileMixed01" };
            int want = R > 9f ? 3 : R > 6f ? 2 : 1, n = 0;
            for (int i = 0; i < 6 && n < want; i++)
            {
                uint v = y.Salt(0x9EA0u + (uint)i);
                float a = Hashing.Unit(v) * 6.283f, d = i == 0 ? 0f : R * 0.35f;
                var p = c + new float2(math.cos(a), math.sin(a)) * d;
                if (!y.Occ.Free(p, 3.6f, 0.5f, -0.5f)) continue;
                var o = y.Ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Furniture,
                    new AssetRef(big[(int)(v % (uint)big.Length)], big), v, -1, AssetCategories.ScrapHeap);
                y.Occ.Take(p, 3.6f, YardKit.KindStuff);
                n++;
            }
            return n;
        }

        private static void SecondaryHeaps(Yard y, string[] ids, string cat, float height)
        {
            if (cat == null) return;
            var ctx = y.Ctx;
            int wanted = (int)(y.Core.Area / 900f * y.Fill);
            int placed = 0;
            foreach (var p in YardKit.Shuffled(Scatter.Candidates(y.Core, 6f, y.Salt(0x5EC0u), 400, ctx.Cancel), y.Salt(0x5EC2u)))
            {
                if (placed >= wanted) break;
                float clear = y.Occ.Clearance(p, 9f);
                if (clear < 4f) continue;
                float r = math.min(7f, clear - 1.5f);
                int n = (int)(r * r * 0.25f * y.Fill) + 3;
                int got = YardKit.Heap(ctx, y.Occ, p, r, n, ids, cat, 2.4f, y.Salt(0x5EC1u + (uint)placed));
                if (got == 0) continue;
                y.Junk += got; y.Heaps++; placed++;
                y.Anchors.Add(new Anchor(p, r));
                if (height >= 0.5f) y.Mounds.Add(new Mound { Center = p, Radius = r * 1.2f, Height = height, Flat = 0.2f });
            }
        }

        /// <summary>Pick-your-part salvage: back-to-back goods rows with aisles, racks along the fence, a workshop.</summary>
        private static void Salvage(Yard y)
        {
            var ctx = y.Ctx;
            bool walk = ctx.B("visitorPaths");
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            float rowDepth = 3f;
            float pitch = y.Aisle + rowDepth * 2f + 0.6f;
            var aisles = YardKit.Lines(y.Core, y.Centre, y.Axis, pitch, 0f, 2f);
            foreach (var (a, b, _) in aisles)
            {
                if (walk) LeisureKit.Path(ctx, y.Occ, a, b, NetRole.Pathway, pathAsset);
                else YardKit.Haul(ctx, y.Occ, new[] { a, b }, math.min(y.Aisle, 5f), y.HaulRole, AssetMix.One(""), y.HaulCategory);
            }
            if (walk) ConnectVisitors(y, aisles, pathAsset);
            // Goods rows on both sides of each aisle.
            string[] goods = { AssetCategories.ScrapWreck, AssetCategories.ScrapPallet, AssetCategories.ScrapBarrel, AssetCategories.ScrapHeap, AssetCategories.ScrapTire, AssetCategories.ScrapPallet };
            int rowIndex = 0;
            foreach (var off in new[] { pitch * 0.5f - rowDepth * 0.5f - 0.3f, pitch * 0.5f + rowDepth * 0.5f + 0.3f })
                foreach (var (a, b, k) in YardKit.Lines(y.Core, y.Centre, y.Axis, pitch, off, 1.5f))
                {
                    // Each row is cut in runs of one kind of goods (an aisle of doors, an aisle of engines…).
                    float len = math.distance(a, b);
                    int runs = math.max(1, (int)(len / 18f));
                    for (int r = 0; r < runs; r++)
                    {
                        uint v = y.Salt(0x5A1Au + (uint)rowIndex * 131u + (uint)r);
                        string cat = YardKit.CatQuiet(goods[(int)(v % (uint)goods.Length)], AssetCategories.ScrapPallet, AssetCategories.ScrapHeap);
                        if (cat == null) continue;
                        var ra = math.lerp(a, b, (float)r / runs); var rb = math.lerp(a, b, (float)(r + 1) / runs);
                        bool car = cat == AssetCategories.ScrapWreck;
                        float spacing = car ? 2.8f : cat == AssetCategories.ScrapBarrel ? 2.0f : 2.8f;
                        int placedRun = 0, i = 0;
                        foreach (var p in LeisureKit.Along(ra, rb, spacing))
                        {
                            uint pv = Hashing.Mix(v, (uint)i++);
                            if (Hashing.Unit(pv) > 0.35f + 0.65f * y.Fill) continue;
                            float yaw = LeisureKit.Yaw(ra, rb) + (car ? math.PI * 0.5f : 0f) + (Hashing.Unit(Hashing.Mix(pv, 1u)) - 0.5f) * 0.3f;
                            if (LeisureKit.Put(ctx, y.Occ, p, yaw, ObjectRole.Furniture, AssetMix.Pick(Ids(ctx, cat), pv), cat, car ? 1.2f : 0.8f, pv, 0.6f, 0.05f))
                            {
                                placedRun++;
                                if (car) y.Wrecks++; else y.Junk++;
                            }
                        }
                        if (placedRun > 0) y.Anchors.Add(new Anchor(math.lerp(ra, rb, 0.5f), 4f));
                    }
                    rowIndex++;
                }
            // Racks of pallets and drums along the fence.
            EdgeRow(y, new[] { AssetCategories.ScrapPallet, AssetCategories.ScrapBarrel, AssetCategories.ScrapHeap }, 2.8f, 1.0f, 0f);
            if (y.Fill > 0.8f) EdgeRow(y, new[] { AssetCategories.ScrapBarrel, AssetCategories.ScrapPallet }, 2.4f, 0.8f, 2.6f);
            // Workshop shed deep in the yard.
            string shed = YardKit.CatQuiet(AssetCategories.ScrapCabin, AssetCategories.GardenShed);
            if (shed != null)
            {
                var far = FarFromGate(y);
                if (Spots.Find(y.Occ, 4f, Spots.Preference.Open, y.Salt(0x5ED0u), out var spot, new List<float2> { far }, 0f, 2f, 600))
                    LeisureKit.Put(ctx, y.Occ, spot, LeisureKit.Yaw(spot, y.Start), ObjectRole.Furniture, AssetMix.One(""), shed, 3.5f, y.Salt(0x5ED1u), 1f, 0.3f);
            }
        }

        /// <summary>Visitors walk in down the middle of the haul spine (clear of the gate compound), then across to the nearest aisle.</summary>
        private static void ConnectVisitors(Yard y, List<(float2 a, float2 b, int k)> aisles, AssetRef pathAsset)
        {
            if (aisles.Count == 0 || y.Gates.Count == 0) return;
            var ctx = y.Ctx;
            var g = y.Gates[0].Position + y.Region.InwardNormal(y.Gates[0].Segment) * 0.3f;
            float best = float.MaxValue; float2 foot = y.Start;
            foreach (var (a, b, _) in aisles)
            {
                var q = SegmentIndex.ClosestPoint(y.Start, a, b);
                float d = math.distance(q, y.Start);
                if (d < best && !y.Region.CrossesBoundary(y.Start, q, true)) { best = d; foot = q; }
            }
            if (!y.Region.CrossesBoundary(g, y.Start, true)) LeisureKit.Path(ctx, y.Occ, g, y.Start, NetRole.Pathway, pathAsset);
            if (best < float.MaxValue) LeisureKit.Path(ctx, y.Occ, y.Start, foot, NetRole.Pathway, pathAsset);
        }

        /// <summary>Container blocks with reach-stacker aisles, floodlight masts at block ends, a fence row.</summary>
        private static void ContainerDepot(Yard y)
        {
            var ctx = y.Ctx;
            var ids = ctx.L("containerAssets");
            string cat = YardKit.Cat(ctx, ids, AssetCategories.ScrapContainer);
            if (cat == null) return;
            const float L = 12.2f, W = 2.5f;
            int perBlock = 2 + (int)math.round(y.Fill * 4f);
            float rowPitch = W + 0.35f;
            float blockPitch = perBlock * rowPitch + y.Aisle;
            var across = y.Side;
            float minB = float.MaxValue, maxB = float.MinValue, minA = float.MaxValue, maxA = float.MinValue;
            foreach (var v in y.Core.Ring)
            {
                minB = math.min(minB, math.dot(v - y.Centre, across)); maxB = math.max(maxB, math.dot(v - y.Centre, across));
                minA = math.min(minA, math.dot(v - y.Centre, y.Axis)); maxA = math.max(maxA, math.dot(v - y.Centre, y.Axis));
            }
            int blocks = (int)math.ceil((maxB - minB) / blockPitch) + 1;
            float colPitch = L + 0.6f;
            // Every fifth column is a cross aisle so the blocks are not a solid wall.
            int cols = (int)math.ceil((maxA - minA) / colPitch) + 1;
            var masts = new List<float2>();
            for (int bk = 0; bk < blocks; bk++)
            {
                float b0 = minB + y.Aisle * 0.5f + bk * blockPitch;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int r = 0; r < perBlock; r++)
                {
                    float vb = b0 + r * rowPitch + W * 0.5f;
                    for (int ci = 0; ci < cols; ci++)
                    {
                        if (ci % 5 == 4) continue;
                        float va = minA + ci * colPitch + L * 0.5f;
                        var c = y.Centre + y.Axis * va + across * vb;
                        uint v = y.Salt(0xC0A7u + (uint)(bk * 4096 + r * 256 + ci));
                        if (Hashing.Unit(v) > 0.7f + 0.3f * y.Fill) continue;   // a missing box here and there
                        var rect = new OrientedRect { Center = c, Axis = y.Axis, HalfLength = L * 0.5f, HalfWidth = W * 0.5f };
                        if (!LeisureKit.RectFits(y.Core, y.Occ, rect, 0.2f, 0.1f)) continue;
                        float jitter = (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 0.04f;
                        ctx.Object(c, rect.Yaw + jitter + ((v & 1u) == 0 ? 0f : math.PI), ObjectRole.Furniture, AssetMix.Pick(ids, v), v, -1, cat);
                        LeisureKit.TakeRect(y.Occ, rect, YardKit.KindStuff);
                        y.Containers++;
                        lo = math.min(lo, va); hi = math.max(hi, va);
                    }
                }
                if (hi > lo)
                {
                    float vm = b0 + perBlock * rowPitch * 0.5f;
                    masts.Add(y.Centre + y.Axis * (lo - L * 0.5f - 2.2f) + across * vm);
                    masts.Add(y.Centre + y.Axis * (hi + L * 0.5f + 2.2f) + across * vm);
                    y.Anchors.Add(new Anchor(y.Centre + y.Axis * (lo + hi) * 0.5f + across * vm, 6f));
                }
            }
            if (ctx.B("floodlights"))
            {
                string flood = YardKit.CatQuiet(AssetCategories.FloodLight, AssetCategories.Lamp);
                if (flood != null)
                    for (int i = 0; i < masts.Count; i++)
                        LeisureKit.Put(ctx, y.Occ, masts[i], 0f, flood == AssetCategories.Lamp ? ObjectRole.Lamp : ObjectRole.Furniture, AssetMix.One(""),
                            flood == AssetCategories.Lamp ? null : flood, 0.6f, y.Salt(0x3A57u + (uint)i), 0.5f, 0.2f);
            }
            // One row along the fence, long side to the fence.
            var region = y.Region;
            for (float s = 8f; s < region.Perimeter; s += L + 0.8f)
            {
                var e = region.PointAt(s, out var t, out var inward, out _);
                bool nearGate = false;
                foreach (var g in y.Gates) if (math.distance(g.Position, e) < 14f) { nearGate = true; break; }
                if (nearGate) continue;
                var c = e + inward * (y.EdgeInset + W * 0.5f + 0.6f);
                var rect = new OrientedRect { Center = c, Axis = t, HalfLength = L * 0.5f, HalfWidth = W * 0.5f };
                if (!LeisureKit.RectFits(region, y.Occ, rect, 0.2f, 0.1f)) continue;
                uint v = y.Salt(0xC0EDu + (uint)s);
                ctx.Object(c, rect.Yaw, ObjectRole.Furniture, AssetMix.Pick(ids, v), v, -1, cat);
                LeisureKit.TakeRect(y.Occ, rect, YardKit.KindStuff);
                y.Containers++;
            }
        }

        /// <summary>Tyre mountains on terraformed mounds, small tyre heaps along the fence.</summary>
        private static void TireDump(Yard y)
        {
            var ctx = y.Ctx;
            var ids = ctx.L("tireAssets");
            string cat = YardKit.Cat(ctx, ids, AssetCategories.ScrapTire, AssetCategories.ScrapHeap);
            if (cat == null) return;
            float heapH = ctx.F("heapHeight");
            int wanted = math.clamp(1 + (int)(y.Core.Area / 1300f * y.Fill), 2, 14);
            var centres = new List<float2>();
            int made = 0;
            foreach (var p in YardKit.Shuffled(Scatter.Candidates(y.Core, 7f, y.Salt(0x713Eu), 500, ctx.Cancel), y.Salt(0x713Fu)))
            {
                if (made >= wanted) break;
                float clear = y.Occ.Clearance(p, 16f);
                if (clear < 6f) continue;
                bool close = false;
                foreach (var q in centres) if (math.distance(p, q) < 15f) { close = true; break; }
                if (close) continue;
                float r = math.min(14f, clear - 1.5f);
                int n = (int)(r * r * 0.3f * y.Fill) + 6;
                if ((ids == null || ids.Length == 0) && r >= 6.5f && y.Occ.Free(p, 5f, 0.5f, -0.5f))
                {
                    // The mountain itself: one long dark heap, loads tipped round it.
                    ctx.Object(p, Hashing.Unit(y.Salt(0x7150u + (uint)made)) * 6.283f, ObjectRole.Furniture,
                        new AssetRef("PileCoalSmall01", "Prop_PileCoalSmall01_Full", "CoalTruck01_CoalPile01"), y.Salt(0x7151u + (uint)made), -1, cat);
                    y.Occ.Take(p, 5f, YardKit.KindStuff);
                }
                int got = YardKit.Heap(ctx, y.Occ, p, r, n, ids, cat, 2.2f, y.Salt(0x7140u + (uint)made), 0.8f, 1.3f);
                if (got == 0) continue;
                centres.Add(p); made++;
                y.Junk += got; y.Heaps++;
                y.Anchors.Add(new Anchor(p, r));
                y.Mounds.Add(new Mound { Center = p, Radius = r * 1.1f, Height = heapH * math.saturate(r / 12f), Flat = 0.15f });
            }
            // Small heaps along the fence where tyres were simply thrown over it.
            var region = y.Region;
            int edge = 0;
            for (float s = 6f; s < region.Perimeter; s += 16f)
            {
                var e = region.PointAt(s, out _, out var inward, out _);
                bool nearGate = false;
                foreach (var g in y.Gates) if (math.distance(g.Position, e) < 12f) { nearGate = true; break; }
                if (nearGate) continue;
                uint v = y.Salt(0x71EDu + (uint)edge++);
                if (Hashing.Unit(v) > y.Fill) continue;
                var c = e + inward * (y.EdgeInset + 4f);
                int got = YardKit.Heap(ctx, y.Occ, c, 4f, 6, ids, cat, 1.0f, v);
                if (got > 0) { y.Junk += got; y.Heaps++; y.Anchors.Add(new Anchor(c, 4f)); }
            }
        }

        /// <summary>Car graveyard: double rows of wrecks nose to aisle, messy angles, tyre stacks at row ends.</summary>
        private static void CarGraveyard(Yard y)
        {
            var ctx = y.Ctx;
            var ids = ctx.L("wreckAssets");
            string cat = YardKit.Cat(ctx, ids, AssetCategories.ScrapWreck, AssetCategories.ScrapHeap);
            if (cat == null) return;
            bool walk = ctx.B("visitorPaths");
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            const float carLen = 4.8f;
            float pitch = y.Aisle + carLen * 2f + 0.8f;
            var lanes = YardKit.Lines(y.Core, y.Centre, y.Side, pitch, 0f, 2f);
            foreach (var (a, b, _) in lanes)
            {
                if (walk) LeisureKit.Path(ctx, y.Occ, a, b, NetRole.Pathway, pathAsset);
                else YardKit.Haul(ctx, y.Occ, new[] { a, b }, math.min(y.Aisle, 5f), y.HaulRole, AssetMix.One(""), y.HaulCategory);
            }
            if (walk) ConnectVisitors(y, lanes, pathAsset);
            string tire = YardKit.CatQuiet(AssetCategories.ScrapTire);
            int rowIndex = 0;
            foreach (var off in new[] { pitch * 0.5f - carLen * 0.5f - 0.4f, pitch * 0.5f + carLen * 0.5f + 0.4f })
                foreach (var (a, b, _) in YardKit.Lines(y.Core, y.Centre, y.Side, pitch, off, 1.5f))
                {
                    var dir = math.normalizesafe(b - a);
                    // Cars face their aisle: rows on the low side of a lane look one way, the others the other.
                    float face = LeisureKit.Yaw(a, b) + (off < pitch * 0.5f ? -math.PI * 0.5f : math.PI * 0.5f);
                    int i = 0, placed = 0;
                    foreach (var p in LeisureKit.Along(a, b, 2.7f))
                    {
                        uint v = y.Salt(0xCA25u + (uint)rowIndex * 977u + (uint)i++);
                        if (Hashing.Unit(v) > 0.3f + 0.7f * y.Fill) continue;
                        float yaw = face + (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 0.5f + (Hashing.Unit(Hashing.Mix(v, 2u)) < 0.08f ? math.PI : 0f);
                        if (LeisureKit.Put(ctx, y.Occ, p, yaw, ObjectRole.Furniture, AssetMix.Pick(ids, v), cat, 1.25f, v, 0.6f, 0.05f)) { y.Wrecks++; placed++; }
                    }
                    if (placed > 0)
                    {
                        y.Anchors.Add(new Anchor(math.lerp(a, b, 0.5f), 4f));
                        if (tire != null)
                            foreach (var end in new[] { a - dir * 1.8f, b + dir * 1.8f })
                                if (LeisureKit.Put(ctx, y.Occ, end, 0f, ObjectRole.Furniture, AssetMix.One(""), tire, 0.8f, y.Salt(0x71E5u + (uint)rowIndex), 0.6f, 0.1f)) y.Junk++;
                    }
                    rowIndex++;
                }
            // A row of wrecks along the fence, nose in.
            var region = y.Region;
            int k = 0;
            for (float s = 4f; s < region.Perimeter; s += 2.9f)
            {
                var e = region.PointAt(s, out var t, out var inward, out _);
                bool nearGate = false;
                foreach (var g in y.Gates) if (math.distance(g.Position, e) < 12f) { nearGate = true; break; }
                if (nearGate) continue;
                uint v = y.Salt(0xCAEDu + (uint)k++);
                if (Hashing.Unit(v) > 0.2f + 0.7f * y.Fill) continue;
                var p = e + inward * (y.EdgeInset + carLen * 0.5f + 0.5f);
                float yaw = LeisureKit.Yaw(p, p + inward) + (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 0.4f;
                if (LeisureKit.Put(ctx, y.Occ, p, yaw, ObjectRole.Furniture, AssetMix.Pick(ids, v), cat, 1.25f, v, 0.6f, 0.05f)) y.Wrecks++;
            }
            // The crusher's output: a stack of flattened cars near the gate.
            if (y.Gates.Count > 0)
            {
                var g = y.Gates[0];
                var inward = region.InwardNormal(g.Segment);
                var side = new float2(inward.y, -inward.x);
                var c = YardKit.Inside(region, g.Position + inward * 18f + side * (y.Aisle * 0.5f + 5f), y.Start, 4f);
                int got = YardKit.Heap(ctx, y.Occ, c, 3.5f, 8, ids, cat, 1.4f, y.Salt(0xC2u), 0.5f, 0.7f);
                if (got > 0) { y.Wrecks += got; y.Heaps++; y.Anchors.Add(new Anchor(c, 3.5f)); }
            }
        }

        private static string[] Ids(LayerContext ctx, string cat)
        {
            switch (cat)
            {
                case AssetCategories.ScrapWreck: return ctx.L("wreckAssets");
                case AssetCategories.ScrapHeap: return ctx.L("scrapAssets");
                case AssetCategories.ScrapTire: return ctx.L("tireAssets");
                case AssetCategories.ScrapContainer: return ctx.L("containerAssets");
                case AssetCategories.ScrapMachine: return ctx.L("machineAssets");
                default: return null;
            }
        }

        /// <summary>A row just inside the fence, cycling categories in runs.</summary>
        private static void EdgeRow(Yard y, string[] cats, float spacing, float radius, float extraInset)
        {
            var region = y.Region;
            int i = 0;
            for (float s = 3f; s < region.Perimeter; s += spacing)
            {
                var e = region.PointAt(s, out var t, out var inward, out _);
                bool nearGate = false;
                foreach (var g in y.Gates) if (math.distance(g.Position, e) < 12f) { nearGate = true; break; }
                if (nearGate) continue;
                uint v = y.Salt(0xED6Eu + (uint)(extraInset * 10f) * 7919u + (uint)i++);
                if (Hashing.Unit(v) > 0.3f + 0.7f * y.Fill) continue;
                string cat = YardKit.CatQuiet(cats[(int)(s / 20f) % cats.Length], AssetCategories.ScrapPallet);
                if (cat == null) continue;
                var p = e + inward * (y.EdgeInset + radius + 0.4f + extraInset);
                if (LeisureKit.Put(y.Ctx, y.Occ, p, LeisureKit.Yaw(p, p + t) + (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 0.3f, ObjectRole.Furniture,
                    AssetMix.Pick(Ids(y.Ctx, cat), v), cat, radius, v, 0.5f, 0.05f)) y.Junk++;
            }
        }

        private static float2 FarFromGate(Yard y)
        {
            if (y.Gates.Count == 0) return y.Region.Centroid;
            float best = -1f; float2 far = y.Region.Centroid;
            foreach (var v in y.Region.Ring)
            {
                float d = math.distance(v, y.Gates[0].Position);
                if (d > best) { best = d; far = v; }
            }
            return far;
        }

        // ───────────── shared details ─────────────

        /// <summary>Skips lined up along the spine, open side to the road.</summary>
        private static void Skips(Yard y, int count)
        {
            if (count <= 0 || y.Gates.Count == 0) return;
            string cat = YardKit.CatQuiet(AssetCategories.LandfillSkip, AssetCategories.ScrapContainer);
            if (cat == null) { AssetAvailability.Require(y.Ctx.Plan, AssetCategories.LandfillSkip, null, Keys.Category(AssetCategories.LandfillSkip)); return; }
            var g = y.Gates[0];
            var inward = y.Region.InwardNormal(g.Segment);
            var side = new float2(inward.y, -inward.x);
            int placed = 0;
            for (int k = 0; k < count * 3 && placed < count; k++)
            {
                float along = 14f + (k / 2) * 4.2f;
                float s = (k & 1) == 0 ? -1f : 1f;
                var p = g.Position + inward * along + side * s * (y.Aisle * 0.5f + 2.2f);
                if (!y.Region.Contains(p)) continue;
                if (LeisureKit.Put(y.Ctx, y.Occ, p, LeisureKit.Yaw(p, p - side * s), ObjectRole.Furniture, AssetMix.One(""), cat, 1.8f, y.Salt(0x5C1Bu + (uint)k), 0.5f, 0.2f))
                    placed++;
            }
            y.Junk += placed;
        }

        /// <summary>Working machines beside heaps and rows (handlers, forklifts, loaders, reach stackers).</summary>
        private static void Machines(Yard y, int count, string style)
        {
            if (count <= 0) return;
            var ctx = y.Ctx;
            var ids = ctx.L("machineAssets");
            string cat = YardKit.Cat(ctx, ids, AssetCategories.ScrapMachine);
            if (cat == null) return;
            var anchors = new List<Anchor>(y.Anchors);
            if (anchors.Count == 0) anchors.Add(new Anchor(y.Start, 4f));
            // Biggest heaps get a machine first.
            anchors.Sort((a, b) => b.R.CompareTo(a.R));
            int placed = 0;
            for (int i = 0; i < anchors.Count * 4 && placed < count; i++)
            {
                var an = anchors[i % anchors.Count];
                uint v = y.Salt(0x3AC0u + (uint)i);
                float ang = Hashing.Unit(v) * 6.283f;
                var p = an.P + new float2(math.cos(ang), math.sin(ang)) * (an.R + 3.5f);
                if (LeisureKit.Put(ctx, y.Occ, p, LeisureKit.Yaw(p, an.P), ObjectRole.Furniture, AssetMix.Pick(ids, v), cat, 2.6f, v, 1f, 0.3f))
                {
                    placed++;
                    y.Anchors.Add(new Anchor(p, 2.6f));
                }
            }
            y.Machines += placed;
        }

        private static readonly GritAssets.Set StraySet = new GritAssets.Set(false, "Barrel01", "Barrel02", "Barrel03", "Pallet01", "Pallet04", "Crate01", "CardboardBox02", "GasBottleCart01|Barrel01");

        /// <summary>Stray junk by the fence: single tyres, drums and bits of scrap.</summary>
        private static void Strays(Yard y, float perHa)
        {
            int wanted = (int)(y.Region.Area / 10000f * perHa);
            if (wanted <= 0) return;
            string[] cats = { AssetCategories.ScrapTire, AssetCategories.ScrapBarrel, AssetCategories.ScrapHeap, AssetCategories.ScrapPallet };
            float band = y.EdgeInset + 7f;
            int placed = 0, i = 0;
            foreach (var p in Scatter.Candidates(y.Region, 2.5f, y.Salt(0x57A7u), wanted * 20 + 50, y.Ctx.Cancel))
            {
                if (placed >= wanted) break;
                if (y.Region.BoundaryDistance(p, band + 1f) > band) continue;
                uint v = y.Salt(0x57A8u + (uint)i++);
                string cat = YardKit.CatQuiet(cats[(int)(v % (uint)cats.Length)]);
                if (cat == null) continue;
                // Strays are single small things (the category's heaps are metres long and would poke through the fence).
                var ids = Ids(y.Ctx, cat);
                var asset = ids != null && ids.Length > 0 ? AssetMix.Pick(ids, v) : GritAssets.Ref(StraySet, v);
                if (LeisureKit.Put(y.Ctx, y.Occ, p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Furniture, asset, cat, 0.7f, v, 0.6f, 0.3f))
                    placed++;
            }
            y.Junk += placed;
        }

        /// <summary>Trees and bushes growing through the wrecks where the yard has been left alone.</summary>
        private static void Overgrowth(Yard y, float amount)
        {
            var ctx = y.Ctx;
            uint noise = y.Salt(0x0F6Eu);
            float threshold = 1f - amount;
            System.Func<float2, bool> wild = p => ValueNoise.Clumps(p, 30f, noise) < threshold;
            int bushes = (int)(y.Region.Area / 10000f * 160f * amount);
            int trees = (int)(y.Region.Area / 10000f * 40f * amount);
            if (AssetAvailability.Has(AssetCategories.Bush))
                LeisureKit.Plant(ctx, y.Occ, y.Region, bushes, ObjectRole.Bush, null, 1.0f, 8f, 0.4f, 0x0F6Fu, wild);
            if (AssetAvailability.Has(AssetCategories.Tree))
                LeisureKit.Plant(ctx, y.Occ, y.Region, trees, ObjectRole.Tree, ctx.L("treeSpecies"), 2.4f, 12f, 0.4f, 0x0F70u, wild, 0, 2);
        }

        /// <summary>Oil and dirt round heaps and machines, puddles on the haul road, cracks on concrete.</summary>
        private static void Grime(Yard y, float per100, string style)
        {
            if (per100 <= 0f) return;
            var ctx = y.Ctx;
            // Art pass: never more than ~110 grime decals per hectare — beyond that it reads as noise, not wear.
            int total = (int)math.min(y.Region.Area / 100f * per100, y.Region.Area / 10000f * 110f);
            string dirt = YardKit.CatQuiet(AssetCategories.DecalLitter, AssetCategories.DecalDirt, AssetCategories.Decal);
            string puddle = YardKit.CatQuiet(AssetCategories.DecalPuddles, AssetCategories.DecalDirt);
            string crack = YardKit.CatQuiet(AssetCategories.DecalCracks, AssetCategories.DecalDirt);
            string oil = YardKit.CatQuiet(AssetCategories.DecalDirt);
            if (dirt == null && puddle == null) { AssetAvailability.Require(ctx.Plan, AssetCategories.DecalDirt, null, Keys.Category(AssetCategories.DecalDirt)); return; }
            int nearStuff = (int)(total * 0.6f), onRoad = (int)(total * 0.25f), loose = total - nearStuff - onRoad;
            int n = 0;
            if (y.Anchors.Count > 0)
            {
                int each = math.max(1, nearStuff / y.Anchors.Count);
                for (int i = 0; i < y.Anchors.Count && i < 200; i++)
                    n += YardKit.Grime(ctx, y.ReservedOnly, y.Anchors[i].P, y.Anchors[i].R + 3f, each, i % 3 == 2 ? dirt : oil ?? dirt, y.Salt(0x9E1u + (uint)i));
            }
            if (y.HaulPoints.Count > 0)
            {
                int each = math.max(1, onRoad / y.HaulPoints.Count);
                for (int i = 0; i < y.HaulPoints.Count && i < 200; i++)
                    n += YardKit.Grime(ctx, y.ReservedOnly, y.HaulPoints[i], y.Aisle * 0.6f, each, style == "tires" || style == "cars" ? puddle : (i & 1) == 0 ? puddle : crack, y.Salt(0x9E2u + (uint)i));
            }
            n += YardKit.Grime(ctx, y.ReservedOnly, y.Region.Centroid, math.sqrt(y.Region.Area), loose, ctx.S("ground") == "concrete" ? crack : dirt, y.Salt(0x9E3u));
            ctx.Plan.Facts["decals"] = n;
        }

        private static void PaintGround(Yard y)
        {
            int layer = y.Ctx.I("paintLayer");
            int n = 0;
            foreach (var a in y.Anchors)
            {
                if (n++ >= 24) break;
                TerrainPaint.Patch(y.Ctx, LeisureKit.Blob(a.P, a.R + 2.5f, y.Salt(0xFA1u + (uint)n), 12, 0.25f), layer, 0.9f, 2f);
            }
        }

        private static TerrainResult Shape(Yard y)
        {
            var ctx = y.Ctx;
            bool level = ctx.B("level");
            bool mounds = y.Mounds.Count > 0;
            if (!level && !mounds) return new TerrainResult { HeightAfter = ctx.H };
            float mean = level ? TerrainShaper.MeanHeight(ctx, y.Region) : 0f;
            var list = y.Mounds;
            return YardKit.Sculpt(ctx, y.Region, (p, h0) =>
            {
                float o = level ? mean - h0 : 0f;
                float m = 0f;
                for (int i = 0; i < list.Count; i++) m = math.max(m, list[i].Offset(p));
                return o + m;
            }, 45f, 12f, 8f);
        }
    }
}
