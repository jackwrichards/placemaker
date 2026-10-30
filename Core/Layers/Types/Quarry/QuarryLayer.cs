// Quarry (industrial): a terraced excavation. The pit follows the outline (or an oval / organic shape)
// inside a rim yard; stepped benches go down level by level, and a haul road spirals from the gate
// down the benches to the floor (the terraform follows the ramp). Around the pit: a plant (crusher,
// screens), stockpiles, an office, machinery on the benches and the road, scree at the foot of each
// wall, warning signs and a rim fence. Subtypes give each quarry its own character:
//   openPit    deep rock benches, drill rigs, a crusher
//   gravelPit  two wide shallow benches, many stockpiles
//   sandPit    soft, wide steps, sand piles
//   lakeDig    the "ugly big lake construction zone": a wet dig flooded half way, dredgers at the
//              muddy water edge, conveyors to the stockpiles, haul road running into the water
//   flooded    a flooded old quarry lake: rocky banks, overgrown benches, a lookout, warnings
//   abandoned  dry, overgrown benches, scree, a rusting machine, broken fence
// The basin and the lake are permanent (warned). Water sits below every containing bench, so it
// never spills; every change is blended back to the original ground at the outline.

using Placemaker.Core.Layers.Types.Scrapyard;
using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.ConstructionSite;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Quarry
{
    [AreaGenerator(73, Group = "industrial")]
    public sealed class QuarryLayer : LeisureLayer
    {
        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        public const string LayerId = "quarry";
        public const string PitPlace = "place:pit";
        public const string LakeIssue = "Placemaker.Quarry.Issue.LakePermanent";
        public static readonly string[] Subtypes = { "openPit", "gravelPit", "sandPit", "lakeDig", "flooded", "abandoned" };

        protected override string Area => "Quarry";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/MiningExcavator.svg";
        public override float MinArea => 2000f;
        protected override bool HostsStreetShops => false;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core };
        protected override int WantedEntrances(LayerRegion region) => region.Area > 60000f ? 2 : 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(SpoilHeapsAddOn.LayerId, ("material", "gravel")), Feature(WorkLightsAddOn.LayerId), Feature("rocks", ("size", "large")),
            ConstructionSiteLayer.Named(Feature("bigtrees"), "Placemaker.AddOn.BigTrees.Name"), Feature("decals", ("theme", "dirt"), ("density", 2.0)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("subtype", "openPit", ControlTier.Essential, "pit", Subtypes).Tip();
            c.Slider("pitSize", 75, 30, 95, 5, "%", ControlTier.Essential, "pit").Tip();
            c.Choice("pitShape", "organic", ControlTier.Essential, "pit", "follow", "oval", "organic").Tip();
            c.Slider("levels", 3, 1, 6, 1, "count", ControlTier.Essential, "pit").Tip();

            c.Slider("benchHeight", 5, 1, 10, 0.5, "m", ControlTier.More, "benches").Tip();
            c.Slider("benchWidth", 14, 6, 30, 1, "m", ControlTier.More, "benches").Tip();
            c.Slider("rimWidth", 14, 6, 40, 1, "m", ControlTier.More, "benches").Tip();
            c.Slider("maxSlope", 60, 20, 100, 5, "%", ControlTier.More, "benches").Tip();

            c.Toggle("water", false, ControlTier.More, "water").Tip();
            c.Slider("waterFill", 60, 10, 95, 5, "%", ControlTier.More, "water").When("water=true").Tip();
            c.Choice("banks", "rocky", ControlTier.More, "water", "rocky", "muddy", "reeds").When("water=true");
            c.Toggle("warnings", true, ControlTier.More, "water").Tip();

            c.Toggle("haulRoad", true, ControlTier.More, "haul").Tip();
            c.Slider("roadWidth", 8, 5, 12, 1, "m", ControlTier.More, "haul").When("haulRoad=true");
            c.Slider("grade", 10, 5, 18, 1, "%", ControlTier.More, "haul").When("haulRoad=true").Tip();

            c.Slider("machines", 5, 0, 16, 1, "count", ControlTier.More, "works");
            c.Toggle("plant", true, ControlTier.More, "works").Tip();
            c.Slider("stockpiles", 6, 0, 24, 1, "count", ControlTier.More, "works");
            c.Toggle("lookout", false, ControlTier.More, "works").Tip();
            c.Toggle("fence", true, ControlTier.More, "works");

            c.Toggle("pitPaint", true, ControlTier.More, "ground").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "ground").When("pitPaint=true").Tip();
            c.Slider("scree", 8, 0, 40, 1, "perHa", ControlTier.More, "ground").Tip();
            c.Slider("mudDecals", 2, 0, 10, 0.5, "per100m2", ControlTier.More, "ground");
            c.Slider("overgrowth", 0, 0, 100, 5, "%", ControlTier.More, "ground").Tip();

            c.AssetMulti("machineAssets", AssetCategories.ConstructionMachine, ControlTier.More, "assets");
            c.AssetMulti("plantAssets", AssetCategories.QuarryPlant, ControlTier.More, "assets");
            c.AssetMulti("pileAssets", AssetCategories.ConstructionPile, ControlTier.More, "assets");
            c.AssetMulti("rockAssets", AssetCategories.Rock, ControlTier.More, "assets");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "assets");
            c.Asset("roadSurface", AssetCategories.SurfaceNatural, ControlTier.More, "assets");
            EntranceMarkers(c);

            c.RandomRange("levels", 2, 5);
            c.RandomRange("benchHeight", 3, 7);
            c.RandomRange("benchWidth", 10, 20);
            c.RandomRange("overgrowth", 0, 50);
            c.Fixed("paintLayer");
            c.Fixed("maxSlope");
            c.Chance("water", 0.35f);
        }

        protected override void DefinePresets()
        {
            Preset("openPit", ("subtype", "openPit"), ("levels", 4.0), ("benchHeight", 6.0), ("benchWidth", 14.0), ("pitShape", "organic"),
                ("machines", 6.0), ("stockpiles", 6.0), ("scree", 10.0), ("paintLayer", 3.0));
            Preset("gravelPit", ("subtype", "gravelPit"), ("levels", 2.0), ("benchHeight", 3.0), ("benchWidth", 18.0), ("pitShape", "follow"),
                ("machines", 6.0), ("stockpiles", 14.0), ("scree", 3.0), ("paintLayer", 1.0));
            Preset("sandPit", ("subtype", "sandPit"), ("levels", 2.0), ("benchHeight", 2.5), ("benchWidth", 22.0), ("maxSlope", 40.0),
                ("pitShape", "organic"), ("machines", 4.0), ("stockpiles", 10.0), ("scree", 0.0), ("paintLayer", 2.0));
            Preset("lakeDig", ("subtype", "lakeDig"), ("water", true), ("waterFill", 55.0), ("banks", "muddy"), ("levels", 3.0),
                ("benchHeight", 4.0), ("benchWidth", 14.0), ("pitSize", 85.0), ("pitShape", "follow"), ("machines", 8.0),
                ("stockpiles", 10.0), ("mudDecals", 4.0), ("scree", 2.0));
            Preset("flooded", ("subtype", "flooded"), ("water", true), ("waterFill", 75.0), ("banks", "rocky"), ("levels", 4.0),
                ("benchHeight", 5.0), ("machines", 0.0), ("plant", false), ("stockpiles", 0.0), ("lookout", true), ("overgrowth", 35.0),
                ("scree", 12.0), ("mudDecals", 0.5), ("paintLayer", 4.0));
            Preset("abandoned", ("subtype", "abandoned"), ("levels", 3.0), ("benchHeight", 5.0), ("machines", 1.0), ("plant", false),
                ("stockpiles", 2.0), ("overgrowth", 60.0), ("scree", 16.0), ("fence", false), ("mudDecals", 0.5), ("warnings", true));
        }

        /// <summary>Planning state of one quarry.</summary>
        private sealed class Q
        {
            public LayerContext Ctx;
            public LayerRegion Region, Pit;
            public Occupancy Occ;
            public string Subtype;
            public bool Active;
            public float2 Gate, Inward, Entry;
            public float Ref, BenchH, BenchW;
            public int Levels;
            public float Water = float.NaN;
            public float2[] WaterRing;
            public readonly List<float2> RoadP = new List<float2>();
            public readonly List<float> RoadH = new List<float>();      // NaN = on the rim (no terraform)
            public float RoadHalf;
            public Func<float2, float> After;
            public float Cell = 8f;
            public int Machines, Heaps, Rocks;

            public float PitDepthAt(float2 p) => Pit == null || !Pit.Contains(p) ? 0f : BenchH * math.min(Levels, math.floor(Pit.BoundaryDistance(p, Levels * BenchW + BenchW) / BenchW) + 1f);
            public bool InPit(float2 p, float margin = 0f) => Pit != null && (Pit.Contains(p) || (margin > 0f && Pit.BoundaryDistance(p, margin + 1f) < margin));
            public bool Wet(float2 p) => !float.IsNaN(Water) && WaterRing != null && Poly.Contains(WaterRing, p);
        }

        /// <summary>Type-specific curated defaults (over GritAssets' shared sets); null = shared only.</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtSurfaces = null;

        // Mining plant, not site plant: haul trucks, wheel loaders and one big excavator.
        private static readonly GritAssets.Set MiningMachines = new GritAssets.Set(false,
            "Prop_FrontendLoader01|Prop_CoalTruck01", "Prop_OreMiningTruck01|Prop_CoalTruck01", "Prop_FrontendLoader01|Prop_CoalTruck01",
            "Prop_MiningExcavator01|Prop_FrontendLoader01", "Prop_OreMiningTruck01|Prop_CoalTruck01");

        /// <summary>Stockpiles by what the pit wins: the game's real bulk heaps, not truck loads.</summary>
        private static GritAssets.Set Piles(string subtype)
        {
            switch (subtype)
            {
                case "sandPit": return new GritAssets.Set(false, "PileSandSmall01|Prop_PileSandSmall01_Full", "Prop_PileSandSmall01_Partial2|PileSandSmall01");
                case "gravelPit":
                case "lakeDig": return new GritAssets.Set(false, "PileStoneSmall01|Prop_PileStoneSmall01_Full", "PileSandSmall01", "Prop_PileStoneSmall01_Partial2|PileStoneSmall01");
                case "abandoned":
                case "flooded": return new GritAssets.Set(false, "Prop_PileStoneSmall01_Partial1|PileStoneSmall01", "OreMiningTruck01_OrePile01|CoalTruck01_StonePile01");
                default: return new GritAssets.Set(false, "PileStoneSmall01|Prop_PileStoneSmall01_Full", "Prop_PileStoneSmall01_Partial2|PileStoneSmall01", "PileStoneSmall01");
            }
        }

        // Art pass: whatever the player left automatic gets the curated gritty defaults (GritAssets);
        // decals are thinned to a believable density.
        protected override void PlanCore(LayerContext ctx)
        {
            PlanArt(ctx);
            var objects = new Dictionary<string, GritAssets.Set>
            {
                [AssetCategories.ConstructionMachine] = MiningMachines,
                [AssetCategories.ConstructionPile] = Piles(ctx.S("subtype")),
            };
            GritAssets.Apply(ctx.Plan, ctx.Seed, objects, ArtSurfaces);
            GritAssets.CapDecals(ctx.Plan, ctx.LayerRegion.Area, 110f);
        }

        // The game's own pieces when the player picked none (industrial makeover, 2026-10-30): the pack names in the sets above
        // are refused by the vanilla rule, so a clean game's quarry had no machines, no plant and no heaps (empty grass pits).
        private static readonly string[] OwnMachines = { Yards.VehicleProps.Loader(1), Yards.VehicleProps.Truck(1), Yards.VehicleProps.Tractor(1), Yards.VehicleProps.Truck(3), Yards.VehicleProps.Loader(2) };
        private static readonly string[] OwnPlant = { "IndustrialManufacturingDecoration04_1x1 Ore", "OreConveyorBelt01", "IndustrialManufacturingDecoration04_1x3 Ore" };
        private static string[] OwnPiles(string subtype)
        {
            switch (subtype)
            {
                case "sandPit": return new[] { "RubblePileConcrete03", "OreMiningTruck01_OrePile03", "RubblePileConcrete03" };
                case "gravelPit": case "lakeDig": return new[] { "RubblePileConcrete02", "RubblePileConcrete04", "RubblePileConcrete03" };
                case "abandoned": case "flooded": return new[] { "RubblePileMixed02", "RubblePileConcrete01", "RubblePileMixed04" };
                default: return new[] { "RubblePileConcrete02", "OreMiningTruck01_OrePile01", "RubblePileConcrete01", "RubblePileConcrete04" };
            }
        }
        private static string[] Or(string[] chosen, string[] own) { if (chosen != null) foreach (var c in chosen) if (!string.IsNullOrEmpty(c)) return chosen; return own; }

        private void PlanArt(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var q = new Q
            {
                Ctx = ctx, Region = region, Occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site), Subtype = ctx.S("subtype"),
                BenchH = math.max(0.5f, ctx.F("benchHeight")), BenchW = math.max(4f, ctx.F("benchWidth")),
            };
            q.Active = q.Subtype != "flooded" && q.Subtype != "abandoned";
            var gates = Gates(ctx);
            if (gates.Count > 0) { q.Gate = gates[0].Position; q.Inward = region.InwardNormal(gates[0].Segment); }
            else q.Gate = region.PointAt(0f, out _, out q.Inward, out _);

            // 1. Pit outline and benches.
            BuildPit(q);
            ctx.Check();
            // 2. Haul road spiral (heights drive the ramp terraform).
            if (q.Pit != null && ctx.B("haulRoad")) PlanRoad(q);
            // 3. Terrain: benches + ramp, blended to the outline.
            if (q.Pit != null)
            {
                var res = SiteKit.Sculpt(ctx, region, (p, h0) => Target(q, p), ctx.F("maxSlope"));
                q.After = res.HeightAfter;
                q.Cell = res.CellSize;
            }
            else q.After = ctx.H;
            ctx.Check();
            // 4. Water.
            if (q.Pit != null && ctx.B("water")) Flood(q);
            ctx.Check();

            // 5. Surfaces & paint: pit texture, haul road (dry parts), rim fence.
            int layer = math.clamp(ctx.I("paintLayer"), 1, 4);
            if (q.Pit != null && ctx.B("pitPaint"))
            {
                // The pit is worked ground, never lawn: earth over the benches and floor, the pit's own material (sand, gravel)
                // over it in drifts (TerrainPaint.Field as the yards do; Patch with intent 1 or 2 painted grass).
                int baseLayer = q.Subtype == "sandPit" || q.Subtype == "gravelPit" ? 4 : 3;
                TerrainPaint.Field(ctx, q.Pit.Ring, 3, 1f, 3f, 2f, null, 0f, 0f, 0f, Hashing.Mix(ctx.Seed, 0x9A10u));
                if (baseLayer == 4) TerrainPaint.Field(ctx, q.Pit.Ring, 4, 0.9f, 5f, 3f, null, 0f, 0f, 0f, Hashing.Mix(ctx.Seed, 0x9A11u));
                if (q.Subtype == "abandoned" || q.Subtype == "flooded") TerrainPaint.Field(ctx, q.Pit.Ring, 1, 0.55f, 6f, 4f, null, 0f, 0f, 0f, Hashing.Mix(ctx.Seed, 0x9A12u));
            }
            var mudZones = new List<float2[]>();
            if (q.RoadP.Count > 1) mudZones.AddRange(EmitRoad(q, ctx.B("pitPaint") ? layer : 0));
            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                if (q.Subtype == "abandoned")
                {
                    int k = 0;   // broken: every third run missing
                    foreach (var run in Fences.Runs(region, gates, 4f))
                        if ((Hashing.Mix(ctx.Seed, (uint)k++) % 3u) != 0) ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
                }
                else Fence(ctx, gates, fence, 4f);
            }
            ctx.Check();

            // 6. Rim: lookout path first (objects keep off it), warnings, plant, office, stockpiles.
            if (q.Pit != null && ctx.B("lookout")) Lookout(q);
            if (q.Pit != null && ctx.B("warnings")) Warnings(q);
            if (q.Active && ctx.B("plant")) Plant(q, mudZones);
            Stockpiles(q, mudZones);
            ctx.Check();
            // 7. Pit dressing: banks, scree, machines, overgrowth.
            if (q.WaterRing != null) Banks(q, mudZones);
            if (q.Pit != null) Scree(q);
            Machines(q);
            Overgrowth(q);
            ctx.Check();
            // 8. Mud decals by the gate, the road, the plant and the water.
            var apron = LeisureKit.Intersect(region, LayerRegion.Rect(q.Gate + q.Inward * 7f, q.Inward, 7f, 6f));
            if (apron != null) mudZones.Add(apron.Ring);
            float mud = ctx.F("mudDecals");
            if (mud > 0f)
            {
                string dirt = SiteKit.Cat(ctx, null, AssetCategories.DecalDirt, AssetCategories.Decal);
                string puddles = SiteKit.Cat(ctx, null, AssetCategories.DecalPuddles, AssetCategories.DecalDirt);
                uint salt = 0x3D00u;
                foreach (var z in mudZones)
                {
                    SiteKit.Decals(ctx, q.Occ, z, mud, dirt, salt++, q.Wet, 120);
                    SiteKit.Decals(ctx, q.Occ, z, mud * 0.2f, puddles, salt++, q.Wet, 30);
                }
            }

            // 8b. The marks of work (YardWear): ruts on the haul road (its dry part), dust off the plant and the heaps, oil
            // under the machines, weeds along the fence.
            {
                var brief = Yards.YardWear.Of(ctx, Id, q.Subtype, "dirt", gates);
                var dry = new List<float2>();
                foreach (var p in q.RoadP)
                {
                    bool wet = q.WaterRing != null && Poly.Contains(q.WaterRing, p);
                    if (!wet) { dry.Add(p); continue; }
                    if (dry.Count > 1) brief.Lanes.Add((dry.ToArray(), q.RoadHalf, false));
                    dry.Clear();
                }
                if (dry.Count > 1) brief.Lanes.Add((dry.ToArray(), q.RoadHalf, false));
                brief.Grime = q.Active ? 1f : 0.55f;
                Yards.YardWear.Lay(ctx, brief);
            }

            // 9. Regions: the pit is a place (features keep out of it), the rim yard is free for features.
            if (q.Pit != null) ctx.AddRegion(q.Pit.Ring, PitPlace, PitPlace);
            ConstructionSiteLayer.EmitYard(ctx, q.Occ, p => q.InPit(p, 3f));

            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat("Placemaker.Stat.PitDepth", Math.Round(q.Pit == null ? 0 : q.Levels * q.BenchH, 1), Keys.UnitM);
            ctx.Stat("Placemaker.Stat.Benches", q.Pit == null ? 0 : q.Levels, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Machines", q.Machines, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Heaps", q.Heaps, Keys.UnitCount);
            ctx.Plan.Facts["subtype"] = Array.IndexOf(Subtypes, q.Subtype);
            if (q.Pit != null) { ctx.Plan.Facts["pitArea"] = q.Pit.Area; ctx.Plan.Facts["benches"] = q.Levels; ctx.Plan.Facts["pitRef"] = q.Ref; }
        }

        // ───────────────────────── pit ─────────────────────────

        private static void BuildPit(Q q)
        {
            var ctx = q.Ctx;
            var region = q.Region;
            float rim = ctx.F("rimWidth");
            var baseRing = SiteKit.Inset(region.Ring, rim) ?? SiteKit.Inset(region.Ring, rim * 0.5f);
            var baseRegion = baseRing == null ? null : LayerRegion.From(baseRing);
            if (baseRegion == null || baseRegion.Area < 300f) { ctx.Issue(IssueSeverity.Info, "Placemaker.Quarry.Issue.NoRoomForPit"); return; }
            float size = ctx.F("pitSize") / 100f;
            var deep = baseRegion.DeepestPoint(out float ins, null, 16);
            float2[] pit = null;
            switch (ctx.S("pitShape"))
            {
                case "oval":
                {
                    LeisureKit.Frame(baseRegion, out _, out var axis, out var side, out _, out _);
                    if (LeisureKit.Chord(baseRegion, deep, axis, 0f, out var a0, out var a1) && LeisureKit.Chord(baseRegion, deep, side, 0f, out var b0, out var b1))
                    {
                        float ra = math.distance(a0, a1) * 0.5f * size, rb = math.distance(b0, b1) * 0.5f * size;
                        var mid = (a0 + a1) * 0.5f;
                        var e = new float2[40];
                        for (int i = 0; i < e.Length; i++)
                        {
                            float t = i * 2f * math.PI / e.Length;
                            e[i] = mid + axis * math.cos(t) * ra + side * math.sin(t) * rb;
                        }
                        pit = LeisureKit.Intersect(baseRegion, e)?.Ring;
                    }
                    break;
                }
                case "follow":
                case "organic":
                {
                    float extra = (1f - size) * ins * 0.6f;
                    var f = extra > 0.5f ? SiteKit.Inset(baseRing, extra) : baseRing;
                    if (f == null) break;
                    if (ctx.S("pitShape") == "organic")
                    {
                        // Wobble the face: resample and push vertices in by smooth noise (up to half a bench).
                        var r = Poly.Resample(f, 9f);
                        uint seed = Hashing.Mix(ctx.Seed, 0x0A61u);
                        var fr = LayerRegion.From(f);
                        if (r.Length >= 8 && fr != null)
                        {
                            var w = new float2[r.Length];
                            float amp = math.min(q.BenchW * 0.6f, ins * 0.15f);
                            for (int i = 0; i < r.Length; i++)
                            {
                                float n = ValueNoise.Periodic(i * 6f / r.Length, 6, seed);
                                var a = r[(i - 1 + r.Length) % r.Length]; var b = r[(i + 1) % r.Length];
                                var inward = math.normalizesafe(new float2(-(b - a).y, (b - a).x));
                                w[i] = r[i] + inward * amp * n;
                            }
                            if (Poly.IsSimple(w) && Poly.SignedArea(w) > 0f) f = w;
                        }
                    }
                    pit = Poly.Chaikin(f, 2);
                    break;
                }
            }
            if (pit == null || pit.Length < 3) pit = baseRing;
            pit = Poly.Clean(pit, 0.3f);
            if (!Poly.IsSimple(pit)) pit = baseRing;
            q.Pit = LayerRegion.From(pit);
            if (q.Pit == null || q.Pit.Area < 300f) { q.Pit = null; ctx.Issue(IssueSeverity.Info, "Placemaker.Quarry.Issue.NoRoomForPit"); return; }

            q.Pit.DeepestPoint(out float pitIns, null, 16);
            int wanted = math.max(1, ctx.I("levels"));
            int fits = math.max(1, (int)math.floor((pitIns - 3f) / q.BenchW) + 1);
            q.Levels = math.min(wanted, fits);
            if (q.Levels < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Quarry.Issue.FewerBenches", q.Levels, wanted);
            float refH = float.MaxValue;
            foreach (var p in q.Pit.Ring) refH = math.min(refH, ctx.H(p));
            q.Ref = math.min(refH, ctx.H(deep));
        }

        /// <summary>Spiral haul road: gate → rim → along each bench ring, dropping one bench per lap section.</summary>
        private static void PlanRoad(Q q)
        {
            var ctx = q.Ctx;
            q.RoadHalf = ctx.F("roadWidth") * 0.5f;
            float grade = math.max(0.03f, ctx.F("grade") / 100f);
            var start = q.Gate + q.Inward * 1.5f;
            var e0 = q.Pit.ClosestOnBoundary(q.Gate, out _, out _);
            q.RoadP.Add(start); q.RoadH.Add(float.NaN);
            if (math.distance(start, e0) > 3f && !q.Region.CrossesBoundary(start, e0, false)) { q.RoadP.Add(e0); q.RoadH.Add(float.NaN); }
            q.Entry = e0;
            float h = q.Ref;
            var last = e0;
            for (int k = 1; k <= q.Levels; k++)
            {
                ctx.Check();
                var ring = SiteKit.Inset(q.Pit.Ring, (k - 0.5f) * q.BenchW);
                if (ring == null || ring.Length < 3) break;
                float per = Poly.Perimeter(ring);
                float s0 = SiteKit.ArcOf(ring, last);
                var p0 = SiteKit.AlongRing(ring, s0, out _);
                if (q.Pit.CrossesBoundary(last, p0, true) && k > 1) break;
                q.RoadP.Add(p0); q.RoadH.Add(h);
                float len = math.min(q.BenchH / grade, per * 0.85f);
                int n = math.max(2, (int)math.ceil(len / 5f));
                for (int j = 1; j <= n; j++)
                {
                    q.RoadP.Add(SiteKit.AlongRing(ring, s0 + len * j / n, out _));
                    q.RoadH.Add(h - q.BenchH * j / n);
                }
                h -= q.BenchH;
                last = q.RoadP[q.RoadP.Count - 1];
            }
        }

        /// <summary>Terraform target at p: the haul road's height near the road, the bench level in the pit, NaN outside.</summary>
        private static float Target(Q q, float2 p)
        {
            if (q.RoadP.Count > 1)
            {
                float best = (q.RoadHalf + 4f) * (q.RoadHalf + 4f); float bh = float.NaN;
                for (int i = 0; i + 1 < q.RoadP.Count; i++)
                {
                    if (float.IsNaN(q.RoadH[i + 1])) continue;
                    var a = q.RoadP[i]; var b = q.RoadP[i + 1];
                    var ab = b - a; float l2 = math.lengthsq(ab);
                    float t = l2 > 1e-6f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                    float d = math.distancesq(p, a + ab * t);
                    if (d < best)
                    {
                        best = d;
                        float ha = float.IsNaN(q.RoadH[i]) ? q.Ref : q.RoadH[i];
                        bh = math.lerp(ha, q.RoadH[i + 1], t);
                    }
                }
                if (!float.IsNaN(bh) && q.Pit.Contains(p)) return bh;
            }
            if (!q.Pit.Contains(p)) return float.NaN;
            return q.Ref - q.PitDepthAt(p);
        }

        /// <summary>
        /// Floods the pit to a level below every containing bench: the lake terraform covers the floor
        /// (kept at floor level), the water source fills the benches below the level.
        /// </summary>
        private static void Flood(Q q)
        {
            var ctx = q.Ctx;
            float floor = q.Ref - q.Levels * q.BenchH;
            float depth = q.Levels * q.BenchH;
            float w = floor + math.saturate(ctx.F("waterFill") / 100f) * depth;
            int dry = math.clamp((int)math.floor((q.Ref - w) / q.BenchH), 0, q.Levels - 1);
            w = math.min(w, q.Ref - dry * q.BenchH - (dry == 0 ? 0.5f : 0.8f));
            var footprint = dry == 0 ? q.Pit.Ring : SiteKit.Inset(q.Pit.Ring, dry * q.BenchW);
            if (footprint == null) { ctx.Issue(IssueSeverity.Info, "Placemaker.Quarry.Issue.NoWater"); return; }
            // The wall that holds the water: the dry bench just outside the footprint, or the untouched
            // ground beyond the outermost terraform cells (a square cell reaches half a diagonal past
            // its centre, so sample beyond that).
            var wall = dry == 0 ? SiteKit.Inset(q.Pit.Ring, -(q.Cell * 0.75f + 1f)) : SiteKit.Inset(q.Pit.Ring, (dry - 0.5f) * q.BenchW);
            if (wall != null)
            {
                float lowest = float.MaxValue;
                foreach (var p in Poly.Resample(wall, 4f))
                    if (q.Region.Contains(p)) lowest = math.min(lowest, q.After(p));
                if (lowest < float.MaxValue) w = math.min(w, lowest - 0.4f);
            }
            if (w - floor < 1f) { ctx.Issue(IssueSeverity.Info, "Placemaker.Quarry.Issue.NoWater"); return; }
            var floorRing = q.Levels <= 1 ? q.Pit.Ring : SiteKit.Inset(q.Pit.Ring, (q.Levels - 1) * q.BenchW) ?? footprint;
            var floorRegion = LayerRegion.From(floorRing);
            if (floorRegion == null) return;
            var anchor = floorRegion.DeepestPoint(out _, null, 16);
            ctx.Plan.Terraform.Add(new PlannedTerraform
            {
                Ring = Poly.To3D(floorRing, ctx.H), Kind = TerraformKind.Lower, TargetHeight = floor, Permanent = true,
                WaterLevel = w, WaterAnchor = new float3(anchor.x, w, anchor.y),
            });
            q.Water = w;
            q.WaterRing = footprint;
            ctx.Issue(IssueSeverity.Warning, LakeIssue);
            float area = Poly.Area(footprint);
            ctx.Stat(Keys.StatLakeArea, Math.Round(area), Keys.UnitM2);
            ctx.Plan.Facts["lakeArea"] = area;
            ctx.Plan.Facts["waterLevel"] = w;
            ctx.Plan.Facts["waterDepth"] = w - floor;
        }

        /// <summary>Haul road surfaces over its dry runs (the wet end simply disappears into the lake).</summary>
        private static List<float2[]> EmitRoad(Q q, int paintLayer)
        {
            var ctx = q.Ctx;
            var zones = new List<float2[]>();
            string cat = string.IsNullOrEmpty(ctx.S("roadSurface")) ? ConstructionSiteLayer.SurfaceCategory(ctx, q.Subtype == "sandPit" ? "sand" : "gravel") : AssetCategories.SurfaceNatural;
            var run = new List<float2>();
            void Flush()
            {
                if (run.Count >= 2) zones.AddRange(SiteKit.Track(ctx, q.Occ, q.Region, run, q.RoadHalf * 2f, cat, AssetMix.One(ctx.S("roadSurface")), paintLayer));
                run.Clear();
            }
            for (int i = 0; i < q.RoadP.Count; i++)
            {
                bool wet = !float.IsNaN(q.Water) && !float.IsNaN(q.RoadH[i]) && q.RoadH[i] < q.Water + 0.3f;
                if (wet) { if (run.Count > 0) run.Add(q.RoadP[i]); Flush(); continue; }
                run.Add(q.RoadP[i]);
            }
            Flush();
            return zones;
        }

        // ───────────────────────── rim ─────────────────────────

        private static float2 Outward(Q q, float2 onPit) => math.normalizesafe(onPit - q.Pit.DeepestPoint(out _, null, 8));

        private static void Lookout(Q q)
        {
            var ctx = q.Ctx;
            // The rim point farthest from the gate (the view across the whole pit / lake).
            float best = -1f; float2 spot = default, toward = default;
            var ring = Poly.Resample(q.Pit.Ring, 8f);
            var centre = q.Pit.DeepestPoint(out _, null, 10);
            foreach (var p in ring)
            {
                var o = math.normalizesafe(p - centre);
                var c = p + o * 6f;
                if (!q.Region.ContainsDisc(c, 4.5f) || !q.Occ.Free(c, 3f, 4f)) continue;
                float d = math.distance(c, q.Gate);
                if (d > best) { best = d; spot = c; toward = centre; }
            }
            if (best < 0f) return;
            // Footpath to the nearest outline point that does not cross the pit.
            var foot = q.Region.ClosestOnBoundary(spot, out _, out _);
            if (math.distance(foot, spot) > 3f && !Crosses(q.Pit.Ring, foot, spot))
                LeisureKit.Path(ctx, q.Occ, foot, spot + math.normalizesafe(foot - spot) * 3.5f, NetRole.Pathway, AssetMix.One(""));
            string lookout = SiteKit.Cat(ctx, null, AssetCategories.Lookout, AssetCategories.Shelter, AssetCategories.Bench);
            float yaw = LeisureKit.Yaw(spot, toward);
            SiteKit.Put(ctx, q.Occ, spot, yaw, lookout, null, 2.5f, 0x100Cu);
            var side = math.normalizesafe(new float2(-(toward - spot).y, (toward - spot).x));
            string bench = SiteKit.Cat(ctx, null, AssetCategories.Bench);
            SiteKit.Put(ctx, q.Occ, spot + side * 4.5f, yaw, bench, null, 1.3f, 0x100Du);
            SiteKit.Put(ctx, q.Occ, spot - side * 4.5f, yaw, bench, null, 1.3f, 0x100Eu);
            ctx.Plan.Facts["lookout"] = 1;
        }

        private static bool Crosses(float2[] ring, float2 a, float2 b)
        {
            for (int i = 0; i < ring.Length; i++)
                if (Poly.SegmentsIntersect(a, b, ring[i], ring[(i + 1) % ring.Length])) return true;
            return false;
        }

        /// <summary>Rim safety: a fence 3 m outside the pit edge (gap at the haul road) and warning signs every ~45 m.</summary>
        private static void Warnings(Q q)
        {
            var ctx = q.Ctx;
            var line = SiteKit.Inset(q.Pit.Ring, -3f);
            if (line == null) return;
            var pts = Poly.Resample(line, 12f);
            var fence = AssetMix.One(ctx.S("fenceAsset"));
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                if (!q.Region.ContainsDisc(a, 0.5f) || !q.Region.ContainsDisc(b, 0.5f)) continue;
                if (q.RoadP.Count > 1 && SegmentNearRoad(q, a, b, q.RoadHalf + 3f)) continue;
                if (q.Occ.Corridors.Overlaps((a + b) * 0.5f, 0.5f, 0.2f)) continue;
                ctx.StraightNet(a, b, NetRole.Fence, fence);
                q.Occ.TakeCorridor(a, b, 0.4f, SiteKit.KindProp);
            }
            string sign = SiteKit.Cat(ctx, null, AssetCategories.ConstructionSign);
            if (sign == null) return;
            var signs = Poly.Resample(SiteKit.Inset(q.Pit.Ring, -5f) ?? line, 45f);
            var c = q.Pit.DeepestPoint(out _, null, 8);
            for (int i = 0; i < signs.Length; i++)
                SiteKit.Put(ctx, q.Occ, signs[i], LeisureKit.Yaw(c, signs[i]), sign, null, 0.6f, 0x5167u + (uint)i, 1f);
        }

        private static bool SegmentNearRoad(Q q, float2 a, float2 b, float dist)
        {
            for (int i = 0; i + 1 < q.RoadP.Count; i++)
            {
                var p0 = q.RoadP[i]; var p1 = q.RoadP[i + 1];
                if (q.Pit.Contains(p0) && q.Pit.Contains(p1)) continue;   // only the rim part crosses the fence line
                if (Poly.SegmentsIntersect(a, b, p0, p1)) return true;
                foreach (var p in new[] { a, b, (a + b) * 0.5f })
                    if (SegmentIndex.DistSq(p, p0, p1) < dist * dist) return true;
            }
            return false;
        }

        /// <summary>A free spot of radius r in the rim yard (outside the pit by a margin), seeded scan.</summary>
        private static bool RimSpot(Q q, float r, uint salt, Func<float2, float> score, out float2 spot)
        {
            spot = default;
            float best = float.MinValue; int seen = 0;
            foreach (var p in Scatter.Candidates(q.Region, math.max(2f, r * 0.8f), Hashing.Mix(q.Ctx.Seed, salt), 900, q.Ctx.Cancel))
            {
                if (q.InPit(p, r + 3f) || !q.Occ.Free(p, r, 1.5f, 0.5f)) continue;
                float s = score == null ? 0f : score(p);
                if (s > best) { best = s; spot = p; }
                if (++seen >= 60) break;
            }
            return best > float.MinValue;
        }

        private static void Plant(Q q, List<float2[]> mud)
        {
            var ctx = q.Ctx;
            var ids = Or(ctx.L("plantAssets"), OwnPlant);
            string plant = SiteKit.Cat(ctx, ids, AssetCategories.QuarryPlant, AssetCategories.ConstructionMachine);
            if (plant == null) return;
            var target = q.Pit != null ? q.Entry : q.Region.Centroid;
            if (!RimSpot(q, 9f, 0x9A47u, p => -math.distance(p, target), out var c)) return;
            var t = math.normalizesafe(new float2(-(target - c).y, (target - c).x));
            int n = 0;
            foreach (var o in new[] { 0f, -7f, 7f })
                if (SiteKit.Put(ctx, q.Occ, c + t * o, LeisureKit.Yaw(c, target), plant, ids, 3f, 0x9A48u + (uint)n++)) { }
            mud.Add(LayerRegion.Circle(c, 14f, 14));
            // Site office by the gate.
            string cabin = SiteKit.Cat(ctx, null, AssetCategories.ConstructionCabin, AssetCategories.GardenShed, AssetCategories.Shelter);
            if (RimSpot(q, 4f, 0x0FF1u, p => -math.distance(p, q.Gate + q.Inward * 10f), out var office))
                SiteKit.Put(ctx, q.Occ, office, LeisureKit.Yaw(office, q.Gate), cabin, null, 3f, 0x0FF2u);
            // Lake dig: dredgers at the water edge and a conveyor line to the plant.
            if (q.Subtype == "lakeDig" && q.WaterRing != null)
            {
                var edge = Poly.ClosestPointOnBoundary(q.WaterRing, c, out _, out _);
                var outward = math.normalizesafe(c - edge);
                var dredge = edge + outward * 4f;
                var mids = Or(ctx.L("machineAssets"), OwnMachines);
                string machine = SiteKit.Cat(ctx, mids, AssetCategories.ConstructionMachine);
                if (SiteKit.Put(ctx, q.Occ, dredge, LeisureKit.Yaw(dredge, edge), machine, mids, 3f, 0xD8E0u)) q.Machines++;
                if (AssetAvailability.Has(AssetCategories.QuarryPlant) || ids.Length > 0)
                    SiteKit.Row(ctx, q.Occ, dredge + outward * 6f, c - outward * 6f, 7f, AssetCategories.QuarryPlant, ids, 1.5f, 0xC0B0u);
            }
        }

        private static void Stockpiles(Q q, List<float2[]> mud)
        {
            var ctx = q.Ctx;
            int wanted = ctx.I("stockpiles");
            if (wanted <= 0) return;
            var chosen = ctx.L("pileAssets");
            // Real bulk heaps (the game's rubble heaps, 9 m) when automatic; the player's own piles keep the old spacing.
            bool bulk = Or(chosen, null) == null;
            var ids = Or(chosen, OwnPiles(q.Subtype));
            string pile = SiteKit.Cat(ctx, ids, AssetCategories.ConstructionPile, AssetCategories.Rock);
            if (pile == null) return;
            float r = bulk ? 7f : q.Subtype == "gravelPit" || q.Subtype == "lakeDig" ? 4f : 3f;
            // Each bulk heap holds what several truck-load cones did.
            if (bulk) wanted = math.max(2, (wanted + 1) / 2);
            // Rows of conical heaps along the rim near the road entry (bays a loader can reach).
            var target = q.Pit != null ? q.Entry : q.Region.Centroid;
            int placed = 0;
            for (int g = 0; g < 8 && placed < wanted; g++)
            {
                ctx.Check();
                if (!RimSpot(q, r * 2f, 0x57C0u + (uint)g * 13u, p => -math.distance(p, target) * 0.5f, out var c)) break;
                var dir = q.Pit != null ? math.normalizesafe(new float2(-(c - target).y, (c - target).x)) : new float2(1, 0);
                int n = math.min(4, wanted - placed);
                float pitch = bulk ? 10.5f : r * 2.4f;
                placed += SiteKit.Row(ctx, q.Occ, c - dir * (n - 1) * pitch * 0.5f, c + dir * (n - 1) * pitch * 0.5f, pitch, pile, ids, bulk ? 5f : r, 0x57D0u + (uint)g * 17u, bulk);
                mud.Add(LayerRegion.Circle(c, r * (n + 1.5f), 12));
            }
            q.Heaps += placed;
        }

        // ───────────────────────── pit dressing ─────────────────────────

        private static void Banks(Q q, List<float2[]> mud)
        {
            var ctx = q.Ctx;
            var outer = SiteKit.Inset(q.WaterRing, -1.8f);
            if (outer == null) return;
            var pts = Poly.Resample(outer, ctx.S("banks") == "reeds" ? 3.5f : 7f);
            switch (ctx.S("banks"))
            {
                case "rocky":
                {
                    var ids = ctx.L("rockAssets");
                    string rock = SiteKit.Cat(ctx, ids, AssetCategories.Rock);
                    for (int i = 0; i < pts.Length; i++)
                        if (q.Pit.Contains(pts[i]) && (Hashing.Mix(ctx.Seed, 0xBA0u + (uint)i) & 3u) != 0
                            && SiteKit.Put(ctx, q.Occ, pts[i], Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i)) * 6.28f, rock, ids, 1.2f, 0xBA1u + (uint)i, 0.3f)) q.Rocks++;
                    break;
                }
                case "reeds":
                {
                    string bush = SiteKit.Cat(ctx, null, AssetCategories.Bush);
                    for (int i = 0; i < pts.Length; i++)
                        if (q.Pit.Contains(pts[i]) && ValueNoise.Clumps(pts[i], 12f, Hashing.Mix(ctx.Seed, 0x4EEu)) > 0.35f)
                            SiteKit.Put(ctx, q.Occ, pts[i], Hashing.Unit(Hashing.Mix(ctx.Seed, 0x4EFu + (uint)i)) * 6.28f, bush, null, 1f, 0x4F0u + (uint)i, 0.3f);
                    break;
                }
                default:
                {
                    // Muddy: a dark band of paint round the water and mud decals on it.
                    var band = SiteKit.Inset(q.WaterRing, -6f);
                    if (band != null)
                    {
                        TerrainPaint.Patch(ctx, band, math.clamp(ctx.I("paintLayer") % 4 + 1, 1, 4), 0.9f, 3f);
                        mud.Add(band);
                    }
                    break;
                }
            }
        }

        /// <summary>Scree: rocks at the foot of each bench wall (just inside every bench's outer edge), never under water.</summary>
        private static void Scree(Q q)
        {
            var ctx = q.Ctx;
            int wanted = math.min(240, (int)(ctx.F("scree") * q.Pit.Area / 10000f));
            if (wanted <= 0) return;
            var ids = ctx.L("rockAssets");
            string rock = SiteKit.Cat(ctx, ids, AssetCategories.Rock);
            if (rock == null) return;
            uint clump = Hashing.Mix(ctx.Seed, 0x5C4Eu);
            int n = 0;
            foreach (var p in Scatter.Candidates(q.Pit, 2f, Hashing.Mix(ctx.Seed, 0x5C4Fu), wanted * 25 + 100, ctx.Cancel))
            {
                if (n >= wanted) break;
                float d = q.Pit.BoundaryDistance(p, q.Levels * q.BenchW + q.BenchW);
                float into = d - math.floor(d / q.BenchW) * q.BenchW;   // distance from the toe of the wall above
                if (into > 3.5f || d > q.Levels * q.BenchW || q.Wet(p)) continue;
                if (ValueNoise.Clumps(p, 18f, clump) < 0.3f) continue;
                if (SiteKit.Put(ctx, q.Occ, p, Hashing.Unit(Hashing.Mix(clump, (int)p.x, (int)p.y)) * 6.28f, rock, ids, 1.1f, 0x5C50u + (uint)n, 0.3f)) n++;
            }
            q.Rocks += n;
        }

        private static void Machines(Q q)
        {
            var ctx = q.Ctx;
            int wanted = ctx.I("machines");
            if (wanted <= q.Machines) return;
            var ids = Or(ctx.L("machineAssets"), OwnMachines);
            string machine = SiteKit.Cat(ctx, ids, AssetCategories.ConstructionMachine);
            if (machine == null) return;
            var rng = Hashing.Rng(ctx.Seed, 0x3AC4u);
            // Trucks on the ramp (they drive the haul road: placed on it, not beside it).
            if (q.Active && q.RoadP.Count > 4)
            {
                for (int i = q.RoadP.Count / 3; i < q.RoadP.Count && q.Machines < wanted && q.Machines < wanted / 3 + 1; i += math.max(3, q.RoadP.Count / 4))
                {
                    var p = q.RoadP[i];
                    if (q.Wet(p) || !q.Region.ContainsDisc(p, 2f) || q.Occ.Discs.Overlaps(p, 3f, 0.5f) || q.Occ.HitsPolygon(p, 3f)) continue;
                    var next = q.RoadP[math.min(q.RoadP.Count - 1, i + 1)];
                    uint v = Hashing.Mix(ctx.Seed, 0x3AC5u + (uint)i);
                    ctx.Object(p, LeisureKit.Yaw(p, next), ObjectRole.Furniture, AssetMix.Pick(ids, v), v, -1, machine);
                    q.Occ.Take(p, 3f, SiteKit.KindProp);
                    q.Machines++;
                }
            }
            // Excavators at the working face (lowest dry bench), drills on the upper benches; abandoned: anywhere.
            for (int i = 0; i < 400 && q.Machines < wanted; i++)
            {
                var p = q.Pit != null && (q.Active || q.Subtype == "abandoned")
                    ? q.Pit.Min + new float2(rng.NextFloat(), rng.NextFloat()) * (q.Pit.Max - q.Pit.Min)
                    : q.Region.Min + new float2(rng.NextFloat(), rng.NextFloat()) * (q.Region.Max - q.Region.Min);
                if (q.Pit != null && (q.Active || q.Subtype == "abandoned"))
                {
                    if (!q.Pit.Contains(p) || q.Wet(p)) continue;
                    float d = q.Pit.BoundaryDistance(p, q.Levels * q.BenchW + q.BenchW);
                    float into = d - math.floor(d / q.BenchW) * q.BenchW;
                    if (into < 4f && d < q.Levels * q.BenchW) continue;   // keep off the toe (scree, wall)
                    if (q.Pit.BoundaryDistance(p, 3f) < 2.5f) continue;
                }
                else if (q.InPit(p, 3f)) continue;
                if (SiteKit.Put(ctx, q.Occ, p, rng.NextFloat(0f, 6.28f), machine, ids, 3f, 0x3AD0u + (uint)i)) q.Machines++;
            }
        }

        /// <summary>Pioneer trees and bushes on the rim and the dry benches (old quarries go green from the ledges).</summary>
        private static void Overgrowth(Q q)
        {
            var ctx = q.Ctx;
            float pct = ctx.F("overgrowth") / 100f;
            if (pct <= 0f) return;
            int trees = (int)(q.Region.Area / 10000f * 70f * pct);
            int bushes = trees * 2;
            var treeIds = ctx.L("treeSpecies");
            Func<float2, bool> reject = p => q.Wet(p) || (q.Pit != null && q.Pit.Contains(p) && q.Active);
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                LeisureKit.Plant(ctx, q.Occ, q.Region, trees, ObjectRole.Tree, treeIds, 2.5f, 22f, 0.25f, 0x7EE0u, reject, 1, 3);
            if (bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, null, Keys.Category(AssetCategories.Bush)))
                LeisureKit.Plant(ctx, q.Occ, q.Region, bushes, ObjectRole.Bush, null, 1.1f, 14f, 0.3f, 0x7EE1u, reject);
        }
    }
}
