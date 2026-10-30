// Logistics yard — "like a parking lot but not for cars". The yard is laid out on a parking-style
// lattice in the region's principal frame: storage blocks (the "stall rows") separated by drive
// aisles, with cross aisles every block length, and the lattice shifted so the main entrance always
// lands on an aisle crossing (trucks drive straight in). Each block is filled with slots of the
// chosen cargo (container rows, pallet stacks, crate piles, lumber piles, bulk heaps; "mixed"
// varies per block), on a concrete pad when the ground is hard.
//
// Around that: truck loading bays along one or both far edges (concrete bays + bay-line decals,
// parked trucks, warehouse sheds as props behind them), a gate lane with guard booth and boom
// barrier, a site office, floodlight masts on the aisle crossings, machines (forklifts, reach
// stackers) working the aisles, aisle markings and oil/dirt stains, a security fence and an
// optional landscaped buffer strip. The yard pad is levelled (blended to the original ground at
// the outline) so stacks never sit on a slope.
//
// Everything is a game asset by category with graceful degradation: a missing cargo kind falls
// back along a chain (pallets → crates → any cargo) and reports Info when nothing is installed.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Types.Scrapyard;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Logistics
{
    [AreaGenerator(260, Group = "industrial")]
    public sealed class LogisticsYardLayer : LeisureLayer
    {
        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        public const string LayerId = "logisticsYard";
        public static readonly string[] Styles = { "containers", "pallets", "crates", "lumber", "bulk", "mixed" };

        protected override string Area => "LogisticsYard";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/ZoneIndustrialWarehouses.svg";
        public override float MinArea => 800f;
        public override string[] Emits => new[] { RegionTags.Core, RegionTags.Paved };
        protected override bool HostsStreetShops => false;
        protected override int WantedEntrances(LayerRegion region) => region.Area > 30000f ? 2 : 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("decals", ("theme", "dirt"), ("density", 1.0), ("ground", "paved")),
            CoreFeature(YardBreakAddOn.LayerId),
            CoreFeature("restrooms", ("count", 1.0), ("byArea", false)),
            CoreFeature("shops", ("count", 1.0)),
        };

        protected override void DefineControls(ControlSet c)
        {
            // Essentials: what is stored and how the grid runs.
            c.Choice("style", "containers", ControlTier.Essential, "yardLayout", Styles).Tip();
            c.Slider("aisleWidth", 8, 4, 14, 0.5, "m", ControlTier.Essential, "yardLayout").Tip();
            c.Slider("blockDepth", 11, 4, 30, 1, "m", ControlTier.Essential, "yardLayout").Tip();
            c.Slider("blockLength", 48, 16, 120, 4, "m", ControlTier.Essential, "yardLayout").Tip();
            c.Slider("fill", 75, 20, 100, 5, "%", ControlTier.Essential, "yardLayout").Tip();
            c.Slider("angle", 0, -90, 90, 5, "deg", ControlTier.More, "yardLayout").Tip();

            // Loading bays.
            c.Choice("dockSide", "back", ControlTier.Essential, "yardLoading", "none", "back", "front", "both").Tip();
            c.Slider("bays", 8, 0, 30, 1, "count", ControlTier.More, "yardLoading");
            c.Slider("bayWidth", 4.5, 3.5, 6, 0.5, "m", ControlTier.More, "yardLoading");
            c.Slider("trucks", 40, 0, 100, 10, "%", ControlTier.More, "yardLoading").Tip();
            c.Toggle("sheds", true, ControlTier.More, "yardLoading").Tip();

            // Equipment and buildings-as-props.
            c.Slider("machines", 3, 0, 12, 1, "perHa", ControlTier.More, "yardEquipment").Tip();
            c.Toggle("floodlights", true, ControlTier.More, "yardEquipment");
            c.Slider("lightSpacing", 36, 16, 80, 4, "m", ControlTier.More, "yardEquipment").Layout(12).When("floodlights=true");
            c.Toggle("gatehouse", true, ControlTier.More, "yardEquipment").Tip();
            c.Toggle("office", true, ControlTier.More, "yardEquipment").Tip();

            // Ground.
            c.Choice("ground", "asphalt", ControlTier.More, "ground", "asphalt", "concrete", "gravel", "dirt");
            c.Toggle("markings", true, ControlTier.More, "ground").Tip();
            c.Slider("stains", 0.6, 0, 3, 0.2, "per100m2", ControlTier.More, "ground").Tip();
            c.Toggle("level", true, ControlTier.More, "ground").Tip();

            // Boundary.
            c.Toggle("fence", true, ControlTier.More, "boundary");
            c.Slider("buffer", 0, 0, 16, 1, "m", ControlTier.More, "boundary").Tip();
            c.Slider("bufferTrees", 60, 0, 200, 10, "perHa", ControlTier.More, "boundary");

            // Assets.
            c.AssetMulti("cargoAssets", AssetCategories.LogisticsCargo, ControlTier.More, "assets");
            c.AssetMulti("machineAssets", AssetCategories.LogisticsMachine, ControlTier.More, "assets");
            c.AssetMulti("truckAssets", AssetCategories.LogisticsTruck, ControlTier.More, "assets");
            c.AssetMulti("shedAssets", AssetCategories.LogisticsShed, ControlTier.More, "assets");
            c.Asset("cabinAsset", AssetCategories.LogisticsCabin, ControlTier.More, "assets");
            c.Asset("lampAsset", AssetCategories.FloodLight, ControlTier.More, "assets");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "assets");
            c.Asset("groundSurface", AssetCategories.SurfaceAny, ControlTier.More, "assets");
            EntranceMarkers(c);

            c.RandomRange("aisleWidth", 6, 10);
            c.RandomRange("fill", 50, 90);
            c.RandomRange("angle", 0, 0);
            c.Fixed("level");
            c.Fixed("buffer");
        }

        protected override void DefinePresets()
        {
            Preset("containerStacks", ("style", "containers"), ("aisleWidth", 10.0), ("blockDepth", 11.0), ("blockLength", 52.0), ("dockSide", "front"),
                ("bays", 6.0), ("trucks", 60.0), ("sheds", false), ("machines", 3.0), ("ground", "asphalt"));
            Preset("palletYard", ("style", "pallets"), ("aisleWidth", 5.0), ("blockDepth", 8.0), ("blockLength", 28.0), ("dockSide", "back"),
                ("bays", 10.0), ("trucks", 40.0), ("sheds", true), ("machines", 5.0), ("ground", "concrete"), ("fill", 70.0));
            Preset("lumberYard", ("style", "lumber"), ("aisleWidth", 7.0), ("blockDepth", 10.0), ("blockLength", 36.0), ("dockSide", "none"),
                ("sheds", true), ("machines", 2.0), ("ground", "gravel"), ("markings", false), ("fill", 85.0), ("buffer", 6.0));
            Preset("storageYard", ("style", "bulk"), ("aisleWidth", 8.0), ("blockDepth", 14.0), ("blockLength", 40.0), ("dockSide", "none"),
                ("machines", 2.0), ("ground", "dirt"), ("markings", false), ("stains", 1.6), ("fill", 65.0), ("floodlights", false));
            Preset("loadingYard", ("style", "crates"), ("aisleWidth", 9.0), ("blockDepth", 6.0), ("blockLength", 24.0), ("dockSide", "both"),
                ("bays", 24.0), ("trucks", 70.0), ("sheds", true), ("machines", 4.0), ("ground", "asphalt"), ("fill", 45.0));
            Preset("shipmentYard", ("style", "mixed"), ("aisleWidth", 6.0), ("blockDepth", 10.0), ("blockLength", 24.0), ("dockSide", "back"),
                ("bays", 6.0), ("sheds", true), ("machines", 4.0), ("ground", "concrete"), ("fill", 80.0));
        }

        // ───────────── planning ─────────────

        private sealed class Yard
        {
            public LayerContext Ctx;
            public LayerRegion Region, Inner;
            public Occupancy Occ;
            public float2 Centre, A, S;      // lattice origin (aligned to the main gate) and axes
            public float Aisle, Depth, Length;
            public float PitchS => Depth + Aisle;
            public float PitchA => Length + Aisle;
            public int Decals, Items, Bays, Machines, Lights;
            public const int MaxDecals = 900, MaxItems = 3200;
            /// <summary>Ground before props (GroundCover): decals and stacked goods are accents on paved pads, capped per hectare.</summary>
            public int DecalCap = MaxDecals, MarkCap = MaxDecals, ItemCap = MaxItems;
            public const float DecalsPerHa = 110f, ItemsPerHa = 260f;
        }

        // Art pass (a4): curated, correctly sized goods for everything the player left automatic. The
        // automatic pools drew 35 m log piles as "lumber", 190 m coal mountains and a 37 m pipe rack as
        // "bulk", ship-deck container stacks, play cabins as offices and fire barrels as stock.
        private static readonly string[] OwnTrucks = { Yards.VehicleProps.Truck(1), Yards.VehicleProps.Truck(2), Yards.VehicleProps.Truck(3), Yards.VehicleProps.Van(1), Yards.VehicleProps.Truck(2) };
        private static readonly string[] OwnMachines = { Yards.VehicleProps.Loader(1), Yards.VehicleProps.Tractor(2), Yards.VehicleProps.Van(2) };

        private static readonly Dictionary<string, GritAssets.Set> ArtObjects = new Dictionary<string, GritAssets.Set>
        {
            // Timber bundles (3.5 × 4.7 m log stacks) and split firewood stacks.
            [AssetCategories.LogisticsLumber] = new GritAssets.Set(false, "ForestForwarderTrailer01_LogPile01", "ForestForwarderTrailer01_LogPile02",
                "ForestForwarderTrailer01_LogPile03", "ForestForwarderTrailer01_LogPile01-1|ForestForwarderTrailer01_LogPile01",
                "ForestForwarderTrailer01_LogPile02-2|ForestForwarderTrailer01_LogPile02", "FirewoodPile03"),
            // Bulk bays: truck-tipped heaps that fit an 8 m bay, and pipe stacks.
            [AssetCategories.LogisticsBulk] = new GritAssets.Set(false, "OreMiningTruck01_OrePile02", "OreMiningTruck01_OrePile03",
                "OreMiningTruck01_OrePile01", "OilPipe01|OreMiningTruck01_OrePile02"),
            [AssetCategories.LogisticsContainer] = new GritAssets.Set(false, "ShippingContainer01", "ShippingContainer02", "ShippingContainer03"),
            [AssetCategories.LogisticsPallet] = new GritAssets.Set(false, "Pallet01", "Pallet02", "Pallet03", "Pallet04", "CratePile01", "CratePile02", "GasBottlePallet01"),
            [AssetCategories.LogisticsCrate] = new GritAssets.Set(false, "CratePile01", "CratePile02", "CratePile03", "Crate01", "Crate02", "CardboardBoxPile02"),
            [AssetCategories.LogisticsBarrel] = new GritAssets.Set(false, "BarrelPile01", "BarrelPile02", "BarrelPile03", "Barrel01", "Barrel03", "GasBottleRack01|BarrelPile01"),
            [AssetCategories.LogisticsTruck] = new GritAssets.Set(false, "Prop_TruckTrailer01|Prop_TruckTrailer03", "Prop_TruckTrailer03|Prop_TruckTrailer01",
                "Prop_TruckTrailer04|Prop_TruckTrailer01", "Prop_TruckTrailer02|Prop_TruckTrailer01"),
            [AssetCategories.LogisticsMachine] = new GritAssets.Set(false, "Prop_FrontendLoader01"),
            [AssetCategories.LogisticsCabin] = new GritAssets.Set(true, "ParkingLotBooth01|ShippingContainer01|ToolShed03"),
            [AssetCategories.LogisticsBarrier] = new GritAssets.Set(true, "ParkingLotGate01|ParkingLotGate02"),
            [AssetCategories.FloodLight] = new GritAssets.Set(true, "LightpoleCommercial01|LightpoleCommercial02|LightpoleIndustrial01"),
        };

        protected override void PlanCore(LayerContext ctx) { PlanArt(ctx); GritAssets.Apply(ctx.Plan, ctx.Seed, ArtObjects); GritAssets.CapDecals(ctx.Plan, ctx.LayerRegion.Area, 110f); }

        private void PlanArt(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);

            if (ctx.B("level"))
                TerrainShaper.Shape(ctx, region, new TerrainSpec { Mode = TerrainMode.Flatten, MaxSlopePct = 12f, Falloff = 16f, Seed = ctx.Seed });
            ctx.Check();

            // Landscaped buffer: the yard works on the inset outline.
            var inner = region;
            float buffer = ctx.F("buffer");
            if (buffer > 0.5f)
            {
                var inset = region.InsetVertices(buffer);
                var r = Poly.IsSimple(inset) && Poly.SignedArea(inset) > region.Area * 0.35f ? LayerRegion.From(inset) : null;
                if (r != null) inner = r;
                else ctx.Issue(IssueSeverity.Info, "Placemaker.LogisticsYard.Issue.NoBuffer");
            }

            var y = new Yard
            {
                Ctx = ctx, Region = region, Inner = inner, Occ = occ,
                Aisle = ctx.F("aisleWidth"), Depth = ctx.F("blockDepth"), Length = ctx.F("blockLength"),
            };

            y.DecalCap = GroundCover.AccentBudget(inner.Area, Yard.DecalsPerHa, 40, Yard.MaxDecals);
            y.MarkCap = y.DecalCap * 3 / 5;
            y.ItemCap = GroundCover.AccentBudget(inner.Area, Yard.ItemsPerHa, 60, Yard.MaxItems);
            Ground(y);
            Frame(y, gates);
            var entries = GateLanes(y, gates);
            ctx.Check();
            if (gates.Count > 0) Gatehouse(y, gates[0], entries[0]);
            // A staff court by the entrance, only when a feature will use it (the park's "place" pattern).
            float2[] staff = null;
            if (gates.Count > 0 && (ctx.Input.HasChild(YardBreakAddOn.LayerId) || ctx.Input.HasChild("restrooms") || ctx.Input.HasChild("shops")))
                staff = StaffCourt(y, gates[0], entries[0]);
            Docks(y, gates);
            ctx.Check();
            Floodlights(y);
            Storage(y);
            ctx.Check();
            Machines(y);
            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(ctx.S("fenceAsset")), math.max(4f, y.Aisle * 0.5f + 1f));
            Markings(y);
            Stains(y);
            BufferPlanting(y);
            ctx.Check();

            // Aisles become corridors so features (break area, restrooms, food truck) only take leftovers.
            AisleCorridors(y);
            // The marks of work (YardWear): tyre tracks and old lines along the aisles, oil under trucks and forklifts, rust off the containers.
            {
                var brief = Yards.YardWear.Of(ctx, Id, ctx.S("style"), Hard(ctx) ? "concrete" : ctx.S("ground"), gates, inner.Ring);
                brief.Markings = Hard(ctx);
                Yards.YardWear.Lay(ctx, brief);
            }
            EmitFreeRegions(ctx, occ, staff);

            ctx.Stat(Keys.StatArea, System.Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat("Placemaker.Stat.StorageSlots", y.Items, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.LoadingBays", y.Bays, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.YardMachines", y.Machines, Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, y.Lights, Keys.UnitCount);
            ctx.Plan.Facts["storageSlots"] = y.Items;
            ctx.Plan.Facts["loadingBays"] = y.Bays;
        }

        private static void Ground(Yard y)
        {
            var ctx = y.Ctx;
            // The automatic yard surface is the game's own worn ones (industrial makeover, 2026-10-30: the automatic asphalt and
            // paving read as one bright white sheet in a clean game): dark pavement, grey pavement, gravel, oily earth.
            var asset = AssetMix.One(ctx.S("groundSurface"));
            if (string.IsNullOrEmpty(asset.Id))
                switch (ctx.S("ground"))
                {
                    case "concrete": asset = new AssetRef("Pavement Surface 02", "Pavement Surface 01"); break;
                    case "gravel": asset = new AssetRef("Sand Surface 02", "Sand Surface 01"); break;
                    case "dirt": asset = new AssetRef("Oil Surface 01"); break;
                    default: asset = new AssetRef("Pavement Surface 01", "Pavement Surface 02"); break;
                }
            switch (ctx.S("ground"))
            {
                case "concrete": ctx.Surface(y.Inner.Ring, SurfaceRole.Paving, asset, AssetCategories.SurfacePaving); break;
                case "gravel": ctx.Surface(y.Inner.Ring, SurfaceRole.Ground, asset, AssetCategories.SurfaceNatural); break;
                case "dirt":
                {
                    string cat = LeisureKit.Category(ctx, ctx.S("groundSurface"), AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural) ?? AssetCategories.SurfaceNatural;
                    ctx.Surface(y.Inner.Ring, SurfaceRole.Ground, asset, cat);
                    break;
                }
                default: ctx.Surface(y.Inner.Ring, SurfaceRole.Asphalt, asset, AssetCategories.SurfaceAsphalt); break;
            }
        }

        private static bool Hard(LayerContext ctx) => ctx.S("ground") == "asphalt" || ctx.S("ground") == "concrete";

        /// <summary>Principal frame rotated by the angle control, origin shifted so an aisle crossing sits at the main gate.</summary>
        private static void Frame(Yard y, List<Gate> gates)
        {
            // Art pass: rows follow the plot's dominant edges (a square plot used to get diagonal blocks).
            YardKit.GridFrame(y.Inner, out var centre, out var axis, out _, out _, out _);
            float ang = math.radians(y.Ctx.F("angle"));
            float cs = math.cos(ang), sn = math.sin(ang);
            y.A = math.normalizesafe(new float2(axis.x * cs - axis.y * sn, axis.x * sn + axis.y * cs), new float2(1, 0));
            y.S = new float2(-y.A.y, y.A.x);
            y.Centre = centre;
            if (gates.Count > 0)
            {
                var g = gates[0];
                var entry = g.Position + y.Region.InwardNormal(g.Segment) * math.max(1f, y.Ctx.F("buffer"));
                y.Centre = entry;   // aisle centres at k·PitchS, cross aisles at j·PitchA from the entry point
            }
        }

        /// <summary>Kept-clear entry lanes from every gate into the lattice.</summary>
        private static List<float2> GateLanes(Yard y, List<Gate> gates)
        {
            var entries = new List<float2>();
            float hw = y.Aisle * 0.5f + 1f;
            foreach (var g in gates)
            {
                var n = y.Region.InwardNormal(g.Segment);
                float reach = math.clamp(math.sqrt(y.Inner.Area) * 0.3f, 14f, 40f) + y.Ctx.F("buffer");
                var end = g.Position + n * reach;
                y.Occ.TakeCorridor(g.Position - n * 2f, end, hw, 100);
                entries.Add(g.Position + n * math.max(1f, y.Ctx.F("buffer")));
            }
            if (entries.Count == 0) entries.Add(y.Inner.Centroid);
            return entries;
        }

        private static void Gatehouse(Yard y, Gate g, float2 entry)
        {
            var ctx = y.Ctx;
            var n = y.Region.InwardNormal(g.Segment);
            var t = new float2(n.y, -n.x);
            float hw = y.Aisle * 0.5f + 1f;
            if (ctx.B("gatehouse"))
            {
                string cab = LeisureKit.Category(ctx, ctx.S("cabinAsset"), AssetCategories.LogisticsCabin, AssetCategories.GardenShed);
                if (cab != null)
                {
                    var p = entry + n * 5f + t * (hw + 2.4f);
                    if (!LeisureKit.Put(ctx, y.Occ, p, LeisureKit.Yaw(p, p - t), ObjectRole.Furniture, AssetMix.One(ctx.S("cabinAsset")), cab, 2f, Hashing.Mix(ctx.Seed, 0x6A7Eu), 0.5f))
                    {
                        p = entry + n * 5f - t * (hw + 2.4f);
                        LeisureKit.Put(ctx, y.Occ, p, LeisureKit.Yaw(p, p + t), ObjectRole.Furniture, AssetMix.One(ctx.S("cabinAsset")), cab, 2f, Hashing.Mix(ctx.Seed, 0x6A7Fu), 0.5f);
                    }
                }
                // The boom barrier stands in the lane on purpose (it is the gate); only if one is installed.
                if (AssetAvailability.Has(AssetCategories.LogisticsBarrier))
                {
                    var b = entry + n * 3f + t * (hw - 1.2f);
                    if (y.Inner.ContainsDisc(b, 0.4f))
                    {
                        ctx.Object(b, math.atan2(-t.x, -t.y), ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0xBA5u), -1, AssetCategories.LogisticsBarrier);
                        y.Occ.Take(b, 0.4f, 6);
                    }
                }
            }
            if (ctx.B("office"))
            {
                string cab = LeisureKit.Category(ctx, ctx.S("cabinAsset"), AssetCategories.LogisticsCabin, AssetCategories.GardenShed, AssetCategories.Shelter);
                if (cab == null) return;
                var asset = AssetMix.One(ctx.S("cabinAsset"));
                bool placed = false;
                for (int i = 0; i < 6 && !placed; i++)
                {
                    float side = (i & 1) == 0 ? -1f : 1f;
                    var p = entry + n * (9f + 4f * (i / 2)) + t * side * (hw + 4.5f);
                    placed = LeisureKit.Put(ctx, y.Occ, p, LeisureKit.Yaw(p, p - t * side), ObjectRole.Furniture, asset, cab, 3.8f, Hashing.Mix(ctx.Seed, 0x0FF1u + (uint)i), 0.8f);
                }
                if (!placed && Spots.Find(y.Occ, 4f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0x0FF9u), out var s, null, 0f, 1f, 1500))
                    LeisureKit.Put(ctx, y.Occ, s, LeisureKit.Yaw(s, y.Inner.Centroid), ObjectRole.Furniture, asset, cab, 3.8f, Hashing.Mix(ctx.Seed, 0x0FFAu), 0.8f);
            }
        }

        // ───────────── loading bays ─────────────

        private static void Docks(Yard y, List<Gate> gates)
        {
            var ctx = y.Ctx;
            string side = ctx.S("dockSide");
            int wanted = ctx.I("bays");
            if (side == "none" || wanted <= 0) return;
            var gate = gates.Count > 0 ? gates[0].Position : y.Inner.Centroid;
            var ring = y.Inner.Ring;
            // Best edge by side: back = far from the gate, front = near it (but never the gate's own edge piece).
            var order = new List<(int e, float score)>();
            for (int e = 0; e < ring.Length; e++)
            {
                var a = ring[e]; var b = ring[(e + 1) % ring.Length];
                float len = math.distance(a, b);
                if (len < 20f) continue;
                var m = math.lerp(a, b, 0.5f);
                float d = math.distance(m, gate);
                float sc = len * 0.35f + (side == "front" ? -d : d);
                order.Add((e, sc));
            }
            order.Sort((p, q) => q.score.CompareTo(p.score));
            int edges = side == "both" ? 2 : 1;
            int perEdge = side == "both" ? (wanted + 1) / 2 : wanted;
            int used = 0;
            foreach (var (e, _) in order)
            {
                if (used >= edges) break;
                if (DockEdge(y, e, perEdge, gate, side == "front")) used++;
            }
        }

        private static bool DockEdge(Yard y, int e, int wanted, float2 gate, bool nearGate)
        {
            var ctx = y.Ctx;
            var ring = y.Inner.Ring;
            var a = ring[e]; var b = ring[(e + 1) % ring.Length];
            float len = math.distance(a, b);
            var t = math.normalizesafe(b - a);
            var n = y.Inner.InwardNormal(e);
            var m = math.lerp(a, b, 0.5f);
            bool sheds = ctx.B("sheds");
            float shedDepth = sheds ? 11f : 0f, bayLen = 17f, bw = ctx.F("bayWidth");
            int fit = (int)math.floor((len - 6f) / bw);
            int count = math.min(wanted, fit);
            if (count <= 0) return false;
            // Shift the bay row away from the gate along the edge when it is the front edge.
            float shift = 0f;
            if (nearGate)
            {
                float gu = math.dot(gate - m, t);
                float free = (len - 6f - count * bw) * 0.5f;
                shift = -math.sign(gu) * free;
            }
            var bays = new List<OrientedRect>();
            for (int i = 0; i < count; i++)
            {
                float u = shift + (i - (count - 1) * 0.5f) * bw;
                var r = new OrientedRect { Center = m + t * u + n * (shedDepth + 0.6f + bayLen * 0.5f), Axis = n, HalfLength = bayLen * 0.5f, HalfWidth = bw * 0.5f - 0.15f };
                if (LeisureKit.RectFits(y.Inner, y.Occ, r, 0.2f, 0.2f)) bays.Add(r);
            }
            if (bays.Count == 0) return false;

            bool hard = Hard(ctx);
            // The game's own lorries (Placemaker's vehicle props) when the player picked none: the pack trailers of the art set are
            // refused by the vanilla rule, so the bays stood empty in a clean game (industrial makeover, 2026-10-30).
            var truckIds = ctx.L("truckAssets");
            if (truckIds.Length == 0) truckIds = OwnTrucks;
            string truckCat = AssetCategories.LogisticsTruck;
            string decal = ctx.B("markings") ? MarkingCategory(ctx) : null;
            float truckChance = ctx.F("trucks") / 100f;
            foreach (var r in bays)
            {
                y.Bays++;
                ctx.Surface(r.Ring, SurfaceRole.Paving, new AssetRef("Concrete Surface 02", "Pavement Surface 02"), hard ? AssetCategories.SurfacePaving : AssetCategories.SurfaceAsphalt);
                if (decal != null)
                    for (int k = -1; k <= 1; k++)
                    {
                        // Bay lines on both long sides (shared lines are fine: decals never reserve space).
                        Decal(y, r.Local(k * r.HalfLength * 0.6f, -r.HalfWidth), r.Yaw, decal, 0xBA70u);
                        if (k == 0) Decal(y, r.Local(-r.HalfLength + 0.8f, 0f), r.Yaw + math.PI * 0.5f, decal, 0xBA71u);
                    }
                uint v = Hashing.Mix(ctx.Seed, (int)(r.Center.x * 17f), (int)(r.Center.y * 13f));
                if (truckCat != null && Hashing.Unit(v) < truckChance)
                {
                    // Backed in: cab faces the yard.
                    ctx.Object(r.Local(-r.HalfLength * 0.1f, 0f), r.Yaw, ObjectRole.Furniture, AssetMix.Pick(truckIds, v), v, -1, truckCat);
                }
            }
            // Claim the bay row (+ apron) in one corridor.
            float u0 = float.MaxValue, u1 = float.MinValue;
            foreach (var r in bays) { float u = math.dot(r.Center - m, t); u0 = math.min(u0, u - r.HalfWidth); u1 = math.max(u1, u + r.HalfWidth); }
            float depth = shedDepth + bayLen + 3f;
            y.Occ.TakeCorridor(m + t * u0 + n * (depth * 0.5f), m + t * u1 + n * (depth * 0.5f), depth * 0.5f, 3);

            if (sheds)
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.LogisticsShed, AssetCategories.GardenShed, AssetCategories.Shelter);
                var ids = ctx.L("shedAssets");
                if (ids.Length > 0) cat = AssetCategories.LogisticsShed;
                if (cat != null)
                {
                    int i = 0;
                    for (float u = u0 + 6f; u <= u1 - 5.5f; u += 13f, i++)
                    {
                        var p = m + t * u + n * 5.6f;
                        if (!y.Inner.ContainsDisc(p, 4.5f)) continue;
                        uint v = Hashing.Mix(ctx.Seed, 0x5EDu + (uint)i + (uint)e * 97u);
                        ctx.Object(p, math.atan2(n.x, n.y), ObjectRole.Furniture, AssetMix.Pick(ids, v), v, -1, cat);
                    }
                }
            }
            return true;
        }

        // ───────────── storage lattice ─────────────

        private const int MaxStackItems = 18;
        private struct Slot { public float Length, Width; public string Kind; }

        private static Slot SlotFor(string kind)
        {
            switch (kind)
            {
                case "containers": return new Slot { Length = 12.2f, Width = 2.45f, Kind = kind };
                // A timber bundle (3.5 × 4.7 m); the old 6 × 1.6 m slot drew 35 m log piles.
                case "lumber": return new Slot { Length = 5f, Width = 3.7f, Kind = kind };
                case "bulk": return new Slot { Length = 8f, Width = 8f, Kind = kind };
                case "barrels": return new Slot { Length = 1.6f, Width = 1.6f, Kind = kind };
                case "crates": return new Slot { Length = 2.2f, Width = 2.2f, Kind = kind };
                default: return new Slot { Length = 2.4f, Width = 2.4f, Kind = "pallets" };
            }
        }

        private static string[] ChainFor(string kind)
        {
            switch (kind)
            {
                case "containers": return new[] { AssetCategories.LogisticsContainer, AssetCategories.LogisticsCrate, AssetCategories.LogisticsCargo };
                case "lumber": return new[] { AssetCategories.LogisticsLumber, AssetCategories.LogisticsBulk, AssetCategories.LogisticsCargo };
                case "bulk": return new[] { AssetCategories.LogisticsBulk, AssetCategories.LogisticsCargo };
                case "barrels": return new[] { AssetCategories.LogisticsBarrel, AssetCategories.LogisticsCrate, AssetCategories.LogisticsCargo };
                case "crates": return new[] { AssetCategories.LogisticsCrate, AssetCategories.LogisticsPallet, AssetCategories.LogisticsCargo };
                default: return new[] { AssetCategories.LogisticsPallet, AssetCategories.LogisticsCrate, AssetCategories.LogisticsCargo };
            }
        }

        private static readonly string[] MixedKinds = { "containers", "pallets", "crates", "lumber", "barrels", "pallets" };

        private static void LatticeRange(Yard y, float2 axis, float pitch, float offset, out int k0, out int k1)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in y.Inner.Ring) { float d = math.dot(v - y.Centre, axis); lo = math.min(lo, d); hi = math.max(hi, d); }
            k0 = (int)math.floor((lo - offset) / pitch) - 1;
            k1 = (int)math.ceil((hi - offset) / pitch) + 1;
        }

        private static void Storage(Yard y)
        {
            var ctx = y.Ctx;
            string style = ctx.S("style");
            var chosen = ctx.L("cargoAssets");
            float fill = ctx.F("fill") / 100f;
            bool hard = Hard(ctx) || ctx.S("ground") == "gravel";
            bool marks = ctx.B("markings") && Hard(ctx);
            string decal = marks ? MarkingCategory(ctx) : null;
            var cache = new Dictionary<string, string>();
            string Cat(string kind)
            {
                if (chosen.Length > 0) return AssetCategories.LogisticsCargo;
                if (cache.TryGetValue(kind, out var c)) return c;
                c = LeisureKit.Category(ctx, "", ChainFor(kind));
                cache[kind] = c;
                return c;
            }

            // Blocks sit between aisle centres: across S at (k + ½)·PitchS, along A at (j + ½)·PitchA.
            LatticeRange(y, y.S, y.PitchS, y.PitchS * 0.5f, out int k0, out int k1);
            LatticeRange(y, y.A, y.PitchA, y.PitchA * 0.5f, out int j0, out int j1);
            int blocks = 0;
            for (int k = k0; k <= k1; k++)
            {
                for (int j = j0; j <= j1; j++)
                {
                    if (y.Items >= y.ItemCap) return;
                    var c = y.Centre + y.S * ((k + 0.5f) * y.PitchS) + y.A * ((j + 0.5f) * y.PitchA);
                    var block = new OrientedRect { Center = c, Axis = y.A, HalfLength = y.Length * 0.5f, HalfWidth = y.Depth * 0.5f };
                    // Cheap reject: block fully outside.
                    if (!AnyInside(y.Inner, block)) continue;
                    uint bh = Hashing.Mix(ctx.Seed, k * 7919 + 13, j * 104729 + 7);
                    string kind = style == "mixed" ? MixedKinds[(int)(bh % (uint)MixedKinds.Length)] : style;
                    // Pallet and crate yards keep a few barrel rows for variety.
                    if ((style == "pallets" || style == "crates") && Hashing.Unit(Hashing.Mix(bh, 3u)) < 0.15f) kind = "barrels";
                    string cat = Cat(kind);
                    if (cat == null) continue;
                    int placed = FillBlock(y, block, SlotFor(kind), cat, chosen, fill, bh);
                    if (placed == 0) continue;
                    blocks++;
                    if (hard && kind != "bulk") Pad(y, block);
                    if (decal != null) BlockLines(y, block, decal);
                }
            }
            ctx.Plan.Facts["storageBlocks"] = blocks;
        }

        private static bool AnyInside(LayerRegion r, OrientedRect b)
        {
            if (r.Contains(b.Center)) return true;
            foreach (var q in b.Ring) if (r.Contains(q)) return true;
            return false;
        }

        private static int FillBlock(Yard y, OrientedRect block, Slot slot, string cat, string[] chosen, float fill, uint bh)
        {
            var ctx = y.Ctx;
            int placed = 0;
            float gapL = slot.Kind == "bulk" ? 2f : 0.5f, gapW = slot.Kind == "bulk" ? 2f : 0.35f;
            int nu = math.max(1, (int)math.floor((block.HalfLength * 2f + gapL) / (slot.Length + gapL)));
            int nv = math.max(1, (int)math.floor((block.HalfWidth * 2f + gapW) / (slot.Width + gapW)));
            if (slot.Length > block.HalfLength * 2f + 0.01f || slot.Width > block.HalfWidth * 2f + 0.01f) return 0;
            float spanU = nu * slot.Length + (nu - 1) * gapL, spanV = nv * slot.Width + (nv - 1) * gapW;
            float r = slot.Width * 0.5f;
            // Ground before props (GroundCover): small goods (pallets, crates, barrels, lumber) are a compact
            // representative stack on the block's paved pad — about 40 % of the slots, at most 18 — never
            // the whole block tiled with props. Containers and bulk heaps keep filling their blocks.
            int startCol = 0, stackCols = nu;
            if (slot.Length < 7f && slot.Kind != "bulk")
            {
                int wantedItems = math.clamp((int)math.round(nu * nv * 0.4f), math.min(nu * nv, 4), MaxStackItems);
                stackCols = math.clamp((wantedItems + nv - 1) / nv, 1, nu);
                startCol = (int)(bh % (uint)math.max(1, nu - stackCols + 1));
            }
            int samples = math.max(1, (int)math.ceil(slot.Length / slot.Width));
            for (int iu = 0; iu < nu; iu++)
                for (int iv = 0; iv < nv; iv++)
                {
                    if (y.Items >= y.ItemCap) return placed;
                    if (iu < startCol || iu >= startCol + stackCols) continue;
                    float u = -spanU * 0.5f + slot.Length * 0.5f + iu * (slot.Length + gapL);
                    float v = -spanV * 0.5f + slot.Width * 0.5f + iv * (slot.Width + gapW);
                    uint h = Hashing.Mix(bh, iu * 31 + 1, iv * 17 + 5);
                    if (Hashing.Unit(h) > fill) continue;
                    var p = block.Local(u, v);
                    // Long items are tested as a row of discs along their length.
                    bool ok = true;
                    for (int s = 0; s < samples && ok; s++)
                    {
                        float f = samples == 1 ? 0f : (s / (float)(samples - 1) - 0.5f) * (slot.Length - slot.Width);
                        var q = p + block.Axis * f;
                        ok = y.Inner.ContainsDisc(q, r + 0.4f) && !y.Occ.Discs.Overlaps(q, r, 0.1f) && !y.Occ.Corridors.Overlaps(q, r, 0.2f) && !y.Occ.HitsPolygon(q, r);
                    }
                    if (!ok) continue;
                    float yaw = block.Yaw + ((h >> 8 & 1) == 0 ? 0f : math.PI);
                    var o = ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(chosen, h), h, -1, cat);
                    if (slot.Kind == "bulk") o.Scale = 0.8f + 0.4f * Hashing.Unit(Hashing.Mix(h, 9u));
                    for (int s = 0; s < samples; s++)
                    {
                        float f = samples == 1 ? 0f : (s / (float)(samples - 1) - 0.5f) * (slot.Length - slot.Width);
                        y.Occ.Take(p + block.Axis * f, r, 2);
                    }
                    placed++;
                    y.Items++;
                }
            return placed;
        }

        /// <summary>Concrete pad under a block, clipped to the yard.</summary>
        private static void Pad(Yard y, OrientedRect block)
        {
            var grown = new OrientedRect { Center = block.Center, Axis = block.Axis, HalfLength = block.HalfLength + 0.5f, HalfWidth = block.HalfWidth + 0.5f };
            var ring = grown.Ring;
            bool whole = true;
            foreach (var q in ring) if (!y.Inner.Contains(q)) { whole = false; break; }
            if (whole) { y.Ctx.Surface(ring, SurfaceRole.Paving, new AssetRef("Pavement Surface 02", "Concrete Surface 02"), AssetCategories.SurfacePaving); return; }
            try
            {
                var pieces = Poly.Intersection(ring, y.Inner.Ring);
                foreach (var piece in pieces)
                    if (piece.Outer != null && piece.Outer.Length >= 3 && Poly.Area(piece.Outer) > 20f)
                        y.Ctx.Surface(piece.Outer, SurfaceRole.Paving, new AssetRef("Pavement Surface 02", "Concrete Surface 02"), AssetCategories.SurfacePaving);
            }
            catch (System.Exception) { /* a pad is decoration; skip on degenerate clips */ }
        }

        private static string MarkingCategory(LayerContext ctx) =>
            AssetAvailability.Has(AssetCategories.DecalMarkings) ? AssetCategories.DecalMarkings
            : AssetAvailability.Has(AssetCategories.DecalPaving) ? AssetCategories.DecalPaving : null;

        private static void Decal(Yard y, float2 p, float yaw, string cat, uint salt)
        {
            if (y.Decals >= y.MarkCap || !y.Inner.ContainsDisc(p, 0.3f)) return;
            uint v = Hashing.Mix(y.Ctx.Seed, salt + (uint)y.Decals);
            y.Ctx.Object(p, yaw, ObjectRole.Decal, AssetMix.One(""), v, -1, cat);
            y.Decals++;
        }

        /// <summary>Painted block outline: dashes along the long sides, ticks at the corners.</summary>
        private static void BlockLines(Yard y, OrientedRect b, string cat)
        {
            float hl = b.HalfLength + 0.6f, hw = b.HalfWidth + 0.6f;
            for (float u = -hl; u <= hl + 0.01f; u += math.max(hl, 6f))
            {
                Decal(y, b.Local(u, -hw), b.Yaw, cat, 0xB10Cu);
                Decal(y, b.Local(u, hw), b.Yaw, cat, 0xB10Du);
            }
            Decal(y, b.Local(-hl, 0f), b.Yaw + math.PI * 0.5f, cat, 0xB10Eu);
            Decal(y, b.Local(hl, 0f), b.Yaw + math.PI * 0.5f, cat, 0xB10Fu);
        }

        // ───────────── equipment ─────────────

        private static void Floodlights(Yard y)
        {
            var ctx = y.Ctx;
            if (!ctx.B("floodlights")) return;
            string cat = LeisureKit.Category(ctx, ctx.S("lampAsset"), AssetCategories.FloodLight, AssetCategories.Lamp);
            if (cat == null) return;
            var asset = AssetMix.One(ctx.S("lampAsset"));
            float spacing = ctx.F("lightSpacing");
            int sk = math.max(1, (int)math.round(spacing / y.PitchS)), sj = math.max(1, (int)math.round(spacing / y.PitchA));
            LatticeRange(y, y.S, y.PitchS, 0f, out int k0, out int k1);
            LatticeRange(y, y.A, y.PitchA, 0f, out int j0, out int j1);
            float off = y.Aisle * 0.5f + 0.7f;
            for (int k = k0; k <= k1; k++)
            {
                if (((k % sk) + sk) % sk != 0) continue;
                for (int j = j0; j <= j1; j++)
                {
                    if (((j % sj) + sj) % sj != 0) continue;
                    // On the block corner next to the crossing, so the mast never stands in a lane.
                    var p = y.Centre + y.S * (k * y.PitchS + off) + y.A * (j * y.PitchA + off);
                    if (!y.Inner.ContainsDisc(p, 1.5f)) continue;
                    if (LeisureKit.Put(ctx, y.Occ, p, math.atan2(y.A.x, y.A.y), ObjectRole.Lamp, asset, cat == AssetCategories.Lamp ? null : cat, 0.6f,
                        Hashing.Mix(ctx.Seed, 0xF100u + (uint)y.Lights), 1f, 0.2f)) y.Lights++;
                }
            }
        }

        private static void Machines(Yard y)
        {
            var ctx = y.Ctx;
            int wanted = (int)math.round(y.Inner.Area / 10000f * ctx.F("machines"));
            if (wanted <= 0) return;
            var ids = ctx.L("machineAssets");
            if (ids.Length == 0) ids = OwnMachines;
            string cat = AssetCategories.LogisticsMachine;
            if (cat == null) return;
            LatticeRange(y, y.S, y.PitchS, 0f, out int k0, out int k1);
            LatticeRange(y, y.A, y.PitchA, 0f, out int j0, out int j1);
            float laneOff = math.max(0f, y.Aisle * 0.5f - 1.8f);
            for (int i = 0; i < wanted * 12 && y.Machines < wanted; i++)
            {
                uint h = Hashing.Mix(ctx.Seed, 0x3AC0u + (uint)i);
                int k = k0 + (int)(h % (uint)math.max(1, k1 - k0 + 1));
                float along = (j0 + Hashing.Unit(Hashing.Mix(h, 1u)) * (j1 - j0 + 1)) * y.PitchA;
                float side = (h >> 20 & 1) == 0 ? -1f : 1f;
                var p = y.Centre + y.S * (k * y.PitchS + side * laneOff) + y.A * along;
                // Facing the stacks it is working on, or along the aisle.
                float yaw = (h >> 21 & 1) == 0 ? math.atan2(y.S.x * side, y.S.y * side) : math.atan2(y.A.x, y.A.y);
                if (!y.Inner.ContainsDisc(p, 2.5f)) continue;
                if (LeisureKit.Put(ctx, y.Occ, p, yaw, ObjectRole.Furniture, AssetMix.Pick(ids, h), cat, 1.6f, h, 1f, 0.3f)) y.Machines++;
            }
        }

        private static void Markings(Yard y)
        {
            var ctx = y.Ctx;
            if (!ctx.B("markings") || !Hard(ctx)) return;
            string cat = MarkingCategory(ctx);
            if (cat == null) { AssetAvailability.Require(ctx.Plan, AssetCategories.DecalMarkings, null, Keys.Category(AssetCategories.DecalMarkings)); return; }
            // Dashed centre lines along the aisles (both directions).
            LatticeRange(y, y.S, y.PitchS, 0f, out int k0, out int k1);
            for (int k = k0; k <= k1; k++) AisleDashes(y, y.Centre + y.S * (k * y.PitchS), y.A, cat);
            LatticeRange(y, y.A, y.PitchA, 0f, out int j0, out int j1);
            for (int j = j0; j <= j1; j++) AisleDashes(y, y.Centre + y.A * (j * y.PitchA), y.S, cat);
        }

        private static void AisleDashes(Yard y, float2 through, float2 dir, string cat)
        {
            if (!LeisureKit.Chord(y.Inner, through, dir, 3f, out var a, out var b)) return;
            float yaw = math.atan2(dir.x, dir.y);
            foreach (var p in LeisureKit.Along(a, b, 14f))
                if (!y.Occ.Discs.Overlaps(p, 0.8f)) Decal(y, p, yaw, cat, 0xA15Eu);
        }

        private static void Stains(Yard y)
        {
            var ctx = y.Ctx;
            int wanted = math.min(600, (int)(y.Inner.Area / 100f * ctx.F("stains")));
            if (wanted <= 0) return;
            string cat = LeisureKit.Category(ctx, "", AssetCategories.DecalDirt, AssetCategories.DecalPuddles, AssetCategories.DecalCracks);
            if (cat == null) return;
            int n = 0;
            uint clump = Hashing.Mix(ctx.Seed, 0x57A1u);
            foreach (var p in Scatter.Candidates(y.Inner, 2.5f, Hashing.Mix(ctx.Seed, 0x57A2u), wanted * 8 + 60, ctx.Cancel))
            {
                if (n >= wanted || y.Decals + n >= y.DecalCap) break;
                if (Hashing.Unit(Hashing.Mix(clump, (int)(p.x * 3f), (int)(p.y * 3f))) > Scatter.Clustering(p, 14f, clump, 0.2f)) continue;
                if (y.Occ.Discs.Overlaps(p, 0.5f)) continue;
                uint v = Hashing.Mix(clump, (uint)n);
                ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Decal, AssetMix.One(""), v, -1, cat);
                n++;
            }
        }

        private static void BufferPlanting(Yard y)
        {
            var ctx = y.Ctx;
            if (y.Inner == y.Region) return;
            int trees = (int)((y.Region.Area - y.Inner.Area) / 10000f * ctx.F("bufferTrees"));
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, null, Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, y.Occ, y.Region, trees, ObjectRole.Tree, null, 2.4f, 18f, 0.6f, 0xB0FFu, p => y.Inner.Contains(p), 1, 3);
            int bushes = trees;
            if (bushes > 0 && AssetAvailability.Has(AssetCategories.Bush))
                LeisureKit.Plant(ctx, y.Occ, y.Region, bushes, ObjectRole.Bush, null, 1f, 8f, 0.4f, 0xB0F0u, p => y.Inner.Contains(p));
        }

        private static void AisleCorridors(Yard y)
        {
            LatticeRange(y, y.S, y.PitchS, 0f, out int k0, out int k1);
            for (int k = k0; k <= k1; k++)
                if (LeisureKit.Chord(y.Inner, y.Centre + y.S * (k * y.PitchS), y.A, 0f, out var a, out var b))
                    { y.Occ.TakeCorridor(a, b, y.Aisle * 0.5f, 101); Yards.YardWear.NoteLane(y.Ctx.Plan, new[] { a, b }, y.Aisle * 0.5f, Hard(y.Ctx)); }
            LatticeRange(y, y.A, y.PitchA, 0f, out int j0, out int j1);
            for (int j = j0; j <= j1; j++)
                if (LeisureKit.Chord(y.Inner, y.Centre + y.A * (j * y.PitchA), y.S, 0f, out var a, out var b))
                    { y.Occ.TakeCorridor(a, b, y.Aisle * 0.5f, 101); Yards.YardWear.NoteLane(y.Ctx.Plan, new[] { a, b }, y.Aisle * 0.5f, Hard(y.Ctx)); }
        }

        /// <summary>Leftover yard space (not storage, lanes or bays) for features; largest is "core".</summary>
        private static void EmitFreeRegions(LayerContext ctx, Occupancy occ, float2[] staff)
        {
            var region = ctx.LayerRegion;
            if (staff != null) ctx.AddRegion(staff, "yard:staff", RegionTags.Paved, RegionTags.Core);
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.Discs.Overlaps(p, 1.2f) || occ.HitsPolygon(p, 0.5f), 3f, 120f, ctx.Cancel);
            if (pieces.Count == 0 && staff == null) { ctx.AddRegion(region.Ring, "yard:0", RegionTags.Paved, RegionTags.Core); return; }
            for (int i = 0; i < pieces.Count && i < 16; i++)
                if (i == 0 && staff == null) ctx.AddRegion(pieces[i], "yard:0", RegionTags.Paved, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "yard:" + i, RegionTags.Paved);
        }

        /// <summary>
        /// Free court (≈ 24 × 20 m) beside the entrance lane, claimed before storage so the break area,
        /// restrooms and food truck have room. Null when nothing fits (features then use leftovers).
        /// </summary>
        private static float2[] StaffCourt(Yard y, Gate g, float2 entry)
        {
            var n = y.Region.InwardNormal(g.Segment);
            var t = new float2(n.y, -n.x);
            float hw = y.Aisle * 0.5f + 1f;
            foreach (var (w, d) in new[] { (24f, 20f), (18f, 16f), (14f, 12f) })
                for (int i = 0; i < 6; i++)
                {
                    float side = (i & 1) == 0 ? 1f : -1f;
                    float depthOff = 4f + (i / 2) * 8f;
                    var r = new OrientedRect { Center = entry + n * (depthOff + d * 0.5f) + t * side * (hw + 1f + w * 0.5f), Axis = t, HalfLength = w * 0.5f, HalfWidth = d * 0.5f };
                    if (!LeisureKit.RectFits(y.Inner, y.Occ, r, 0.5f)) continue;
                    y.Occ.TakeCorridor(r.Local(-r.HalfLength, 0f), r.Local(r.HalfLength, 0f), r.HalfWidth, 7);
                    return r.Ring;
                }
            return null;
        }
    }
}
