// Sports amenities: volleyball sand pit, basketball court, table tennis, fitness stations.
// Real dimensions: volleyball court 16 × 8 m with ≥ 2 m sand run-off; basketball half court
// 15 × 14 m (full 28 × 15 m); a table-tennis table 2.74 × 1.53 m with ~3 m of play space at each
// end; outdoor gym stations ~3–4 m apart.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(161)]
    public sealed class VolleyballAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.Volleyball";
        public override string Id => "volleyball";

        protected override void DefineControls(ControlSet c)
        {
            c.Asset("net", AssetCategories.SportsVolleyball, ControlTier.Essential, "volleyball");
            c.Toggle("sand", true, ControlTier.Essential, "volleyball");
            c.Asset("sandAsset", AssetCategories.SurfaceSand, ControlTier.More, "ground").When("sand=true");
            c.Toggle("lines", true, ControlTier.More, "volleyball").Tip();
            c.Toggle("benches", true, ControlTier.More, "extras");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "extras").When("benches=true");
            c.Toggle("lights", false, ControlTier.More, "extras");
            c.Asset("lightAsset", AssetCategories.FloodLight, ControlTier.More, "extras").When("lights=true");
            c.Chance("lights", 0.25f);
        }

        protected override void DefinePresets()
        {
            Preset("beach", ("sand", true), ("benches", true), ("lights", false));
            Preset("floodlit", ("sand", true), ("benches", true), ("lights", true));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            // Court 16 × 8 m when it fits (it does in the park's place), else the largest that does.
            float cl = math.min(16f, site.HalfLength * 2f - 4f), cw = math.min(8f, site.HalfWidth * 2f - 4f);
            if (cl < 8f || cw < 4f) { NoSpace(ctx, NameKey); return; }
            if (cl < 16f) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Volleyball.Issue.Smaller");
            var court = new OrientedRect { Center = site.Center, Axis = site.Axis, HalfLength = cl * 0.5f, HalfWidth = cw * 0.5f };
            if (ctx.B("sand"))
                Pad(ctx, LayerRegion.Rect(site.Center, site.Axis, math.min(site.HalfLength - 1f, court.HalfLength + 3f), math.min(site.HalfWidth - 1f, court.HalfWidth + 3f)),
                    SurfaceRole.Ground, ctx.S("sandAsset"), AssetCategories.SurfaceSand, AssetCategories.SurfaceNatural);
            int nets = 0;
            string netCat = LeisureKit.Category(ctx, ctx.S("net"), AssetCategories.SportsVolleyball, AssetCategories.SportsTennis);
            // The net spans the court's width at the centre line: the asset faces along the court axis (its long X across).
            // The clean game has no volleyball net: the tennis category's automatic pool deals benches and umpire chairs as
            // often as its net, so the game's tennis net is named (2026-10-29: a bench stood where the net should be).
            if (netCat != null && Put(ctx, occ, site.Center, site.Yaw, ObjectRole.Furniture, AssetMix.One(ctx.S("net"), "TennisNet01"), netCat, 0.6f, Hashing.Mix(ctx.Seed, 1u))) nets++;
            if (ctx.B("lines"))
                Markings(ctx, court, new[] { float2.zero }, ctx.Seed);
            float side = SideSign(site, toward);
            int benches = ctx.B("benches") ? BenchRow(ctx, occ, site, side, site.HalfWidth - 1.3f, court.HalfLength, 2, ctx.Seed) : 0;
            if (ctx.B("lights"))
            {
                string lightCat = LeisureKit.Category(ctx, ctx.S("lightAsset"), AssetCategories.FloodLight, AssetCategories.Lamp);
                if (lightCat != null)
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var p = site.Local(k * (court.HalfLength + 1.5f), -side * k * (court.HalfWidth + 1.5f));
                        Put(ctx, occ, p, LeisureKit.Yaw(p, site.Center), ObjectRole.Lamp, AssetMix.One(ctx.S("lightAsset")), lightCat, 0.5f, Hashing.Mix(ctx.Seed, 0x1170u + (uint)(k + 1)));
                    }
            }
            ctx.Stat("Placemaker.Stat.Courts", 1, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
            ctx.Plan.Facts["nets"] = nets;
        }
    }

    [AreaGenerator(162)]
    public sealed class BasketballAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.Basketball";
        public override string Id => "basketball";

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("court", "half", ControlTier.Essential, "basketball", "half", "full").Tip();
            c.Asset("hoopAsset", AssetCategories.SportsBasketball, ControlTier.Essential, "basketball");
            c.Toggle("surface", true, ControlTier.More, "ground");
            c.Asset("surfaceAsset", AssetCategories.SurfaceAsphalt, ControlTier.More, "ground").When("surface=true");
            c.Toggle("lines", true, ControlTier.More, "basketball").Tip();
            c.Toggle("fence", false, ControlTier.More, "boundary").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "boundary").When("fence=true");
            c.Toggle("lights", false, ControlTier.More, "extras");
            c.Asset("lightAsset", AssetCategories.FloodLight, ControlTier.More, "extras").When("lights=true");
            c.Toggle("benches", true, ControlTier.More, "extras");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "extras").When("benches=true");
            c.Fixed("court");
            c.Chance("fence", 0.4f);
            c.Chance("lights", 0.3f);
        }

        protected override void DefinePresets()
        {
            Preset("streetball", ("court", "half"), ("fence", false), ("lights", false));
            Preset("caged", ("court", "half"), ("fence", true), ("lights", true));
            Preset("full", ("court", "full"), ("fence", false), ("lights", true));
        }

        /// <summary>A full court needs 28 × 15 m plus run-off when the court finds its own spot.</summary>
        protected override float2 Footprint(LayerContext ctx) => ctx.S("court") == "full" ? new float2(31f, 18f) : base.Footprint(ctx);

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            bool full = ctx.S("court") == "full";
            if (full && site.HalfLength * 2f < 30f) { full = false; ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Basketball.Issue.Half"); }
            float cl = math.min(full ? 28f : 15f, site.HalfLength * 2f - 2.5f), cw = math.min(full ? 15f : 14f, site.HalfWidth * 2f - 2.5f);
            if (cl < 9f || cw < 8f) { NoSpace(ctx, NameKey); return; }
            var court = new OrientedRect { Center = site.Center, Axis = site.Axis, HalfLength = cl * 0.5f, HalfWidth = cw * 0.5f };
            if (ctx.B("surface"))
                Pad(ctx, LayerRegion.Rect(site.Center, site.Axis, math.min(site.HalfLength - 0.5f, court.HalfLength + 1f), math.min(site.HalfWidth - 0.5f, court.HalfWidth + 1f)),
                    SurfaceRole.Asphalt, ctx.S("surfaceAsset"), AssetCategories.SurfaceAsphalt, AssetCategories.SurfacePaving);
            // Half court: the hoop at the end away from the arrival side, so players face the path.
            float end = -EndSign(site, toward);
            int hoops = 0;
            string hoopCat = LeisureKit.Category(ctx, ctx.S("hoopAsset"), AssetCategories.SportsBasketball);
            var hoopAsset = AssetMix.One(ctx.S("hoopAsset"));
            var marks = new List<float2>();
            foreach (float e in full ? new[] { -1f, 1f } : new[] { end })
            {
                var p = court.Local(e * (court.HalfLength - 1.2f), 0f);
                if (hoopCat != null && Put(ctx, occ, p, LeisureKit.Yaw(p, court.Center), ObjectRole.Furniture, hoopAsset, hoopCat, 0.8f, Hashing.Mix(ctx.Seed, 0x400u + (uint)hoops)))
                    hoops++;
                marks.Add(new float2(e * (court.HalfLength - 4.5f), 0f));
            }
            if (full) marks.Add(float2.zero);
            if (ctx.B("lines")) Markings(ctx, court, marks, ctx.Seed);
            if (ctx.B("fence"))
                RectFence(ctx, new OrientedRect { Center = site.Center, Axis = site.Axis, HalfLength = site.HalfLength - 0.4f, HalfWidth = site.HalfWidth - 0.4f },
                    toward, 2.5f, AssetMix.One(ctx.S("fenceAsset")));
            if (ctx.B("lights"))
            {
                string lightCat = LeisureKit.Category(ctx, ctx.S("lightAsset"), AssetCategories.FloodLight, AssetCategories.Lamp);
                if (lightCat != null)
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var p = site.Local(0f, k * (court.HalfWidth + 0.8f));
                        Put(ctx, occ, p, LeisureKit.Yaw(p, site.Center), ObjectRole.Lamp, AssetMix.One(ctx.S("lightAsset")), lightCat, 0.5f, Hashing.Mix(ctx.Seed, 0x1170u + (uint)(k + 1)));
                    }
            }
            int benches = 0;
            if (ctx.B("benches") && site.HalfWidth - court.HalfWidth > 1.6f)
                benches = BenchRow(ctx, occ, site, SideSign(site, toward), court.HalfWidth + 1.2f, court.HalfLength, 2, ctx.Seed);
            ctx.Stat("Placemaker.Stat.Courts", 1, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
            ctx.Plan.Facts["hoops"] = hoops;
        }
    }

    [AreaGenerator(163)]
    public sealed class TableTennisAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.TableTennis";
        public override string Id => "tableTennis";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("tables", 2, 1, 4, 1, "count", ControlTier.Essential, "tableTennis");
            c.Asset("tableAsset", AssetCategories.SportsTableTennis, ControlTier.Essential, "tableTennis");
            c.Toggle("paving", true, ControlTier.More, "ground");
            c.Asset("pavingAsset", AssetCategories.SurfacePaving, ControlTier.More, "ground").When("paving=true");
            c.Toggle("benches", true, ControlTier.More, "extras");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "extras").When("benches=true");
            c.Toggle("shade", true, ControlTier.More, "extras").Tip();
            c.RandomRange("tables", 1, 3);
        }

        protected override void DefinePresets()
        {
            Preset("pair", ("tables", 2.0), ("benches", true), ("shade", true));
            Preset("club", ("tables", 4.0), ("benches", true), ("shade", false));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            string cat = LeisureKit.Category(ctx, ctx.S("tableAsset"), AssetCategories.SportsTableTennis);
            if (cat == null) return;
            // Tables across the place (players at both ends need ~3 m), 5.5 m apart along it.
            const float pitch = 5.5f;
            bool benches = ctx.B("benches");
            float usable = site.HalfLength * 2f - (benches ? 3f : 1f);
            int n = math.min(ctx.I("tables"), math.max(0, (int)math.floor(usable / pitch)));
            if (n == 0 || site.HalfWidth * 2f < 7.5f) { NoSpace(ctx, NameKey); return; }
            if (n < ctx.I("tables")) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.TableTennis.Issue.Fewer", n, ctx.I("tables"));
            if (ctx.B("paving"))
                Pad(ctx, LayerRegion.Rect(site.Center, site.Axis, math.min(site.HalfLength - 0.5f, n * pitch * 0.5f + 0.8f), site.HalfWidth - 0.5f),
                    SurfaceRole.Paving, ctx.S("pavingAsset"), AssetCategories.SurfacePaving);
            var asset = AssetMix.One(ctx.S("tableAsset"));
            int placed = 0;
            for (int i = 0; i < n; i++)
            {
                var p = site.Local((i - (n - 1) * 0.5f) * pitch, 0f);
                if (Put(ctx, occ, p, site.Yaw + math.PI * 0.5f, ObjectRole.Furniture, asset, cat, 1.6f, Hashing.Mix(ctx.Seed, (uint)i + 1u))) placed++;
            }
            int seats = 0;
            if (benches)
            {
                var bench = AssetMix.One(ctx.S("benchAsset"));
                for (int e = -1; e <= 1; e += 2)
                {
                    var p = site.Local(e * (n * pitch * 0.5f + 0.4f), 0f);
                    if (Put(ctx, occ, p, LeisureKit.Yaw(p, site.Center), ObjectRole.Bench, bench, null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE0u + (uint)(e + 1)))) seats++;
                }
            }
            if (ctx.B("shade") && AssetAvailability.Has(AssetCategories.Tree))
            {
                // One shade tree off a corner on the side away from the path (never on the play space).
                float side = -SideSign(site, toward);
                var p = site.Local(EndSign(site, toward) * (site.HalfLength - 2.6f), side * (site.HalfWidth - 2.6f));
                Put(ctx, occ, p, Hashing.Unit(ctx.Seed) * 6.283f, ObjectRole.Tree, new AssetRef(""), null, 2.4f, Hashing.Mix(ctx.Seed, 0x7EEu));
            }
            ctx.Stat("Placemaker.Stat.GameTables", placed, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, seats, Keys.UnitCount);
        }
    }

    [AreaGenerator(165)]
    public sealed class FitnessAddOn : AmenityAddOn
    {
        protected override string Area => "AddOn.Fitness";
        public override string Id => "fitness";

        private static readonly string[] Layouts = { "cluster", "row", "ring" };

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("stations", 6, 3, 10, 1, "count", ControlTier.Essential, "fitness");
            c.Choice("layout", "cluster", ControlTier.Essential, "fitness", Layouts).Tip();
            c.AssetMulti("equipment", AssetCategories.SportsFitness, ControlTier.More, "fitness");
            c.Toggle("surface", true, ControlTier.More, "ground").Tip();
            c.Asset("surfaceAsset", AssetCategories.SurfaceNatural, ControlTier.More, "ground").When("surface=true");
            c.Toggle("benches", true, ControlTier.More, "extras");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "extras").When("benches=true");
            c.RandomRange("stations", 4, 8);
        }

        protected override void DefinePresets()
        {
            Preset("gym", ("layout", "cluster"), ("stations", 8.0));
            Preset("calisthenics", ("layout", "row"), ("stations", 5.0));
            Preset("circle", ("layout", "ring"), ("stations", 6.0));
        }

        protected override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            var ids = ctx.L("equipment");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.SportsFitness, ids, Keys.Category(AssetCategories.SportsFitness))) return;
            int wanted = ctx.I("stations");
            string layout = ctx.S("layout");
            float hl = site.HalfLength - 1.5f, hw = site.HalfWidth - 1.5f;
            var spots = new List<(float2 p, float yaw)>();
            switch (layout)
            {
                case "ring":
                {
                    float r = math.min(hl, hw) - 0.5f;
                    int n = math.min(wanted, math.max(3, (int)math.floor(2f * math.PI * r / 3.6f)));
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * 2f * math.PI / n;
                        var p = site.Center + new float2(math.cos(a), math.sin(a)) * r;
                        spots.Add((p, LeisureKit.Yaw(site.Center, p)));   // stations face outward, users inside the ring
                    }
                    break;
                }
                case "row":
                {
                    int n = math.min(wanted, (int)math.floor(hl * 2f / 3.4f) + 1);
                    float v = SideSign(site, toward) * math.min(hw * 0.5f, 2f);
                    for (int i = 0; i < n; i++)
                    {
                        var p = site.Local(n == 1 ? 0f : (i / (float)(n - 1) - 0.5f) * math.min(hl * 2f, (n - 1) * 3.4f), v);
                        spots.Add((p, site.Yaw + math.PI * 0.5f));
                    }
                    break;
                }
                default:
                {
                    int cols = math.max(1, (int)math.floor(hl * 2f / 4f) + 1), rows = math.max(1, (int)math.floor(hw * 2f / 4f) + 1);
                    int n = math.min(wanted, cols * rows);
                    rows = math.min(rows, (n + cols - 1) / cols);
                    for (int i = 0; i < n; i++)
                    {
                        int cx = i % cols, cy = i / cols;
                        int inRow = math.min(cols, n - cy * cols);
                        float u = (cx - (inRow - 1) * 0.5f) * 4f, v = (cy - (rows - 1) * 0.5f) * 4f;
                        // Alternate facings so neighbouring users do not stare at each other.
                        spots.Add((site.Local(u, v), site.Yaw + ((cx + cy) & 1) * math.PI));
                    }
                    break;
                }
            }
            if (ctx.B("surface"))
            {
                float2[] ring = layout == "ring"
                    ? Disc(site.Center, math.min(site.HalfLength, site.HalfWidth) - 0.5f)
                    : LayerRegion.Rect(site.Center, site.Axis, site.HalfLength - 0.5f, site.HalfWidth - 0.5f);
                // Fine gravel floor (the station set's natural ground); it reaches the place edge where the path arrives.
                ctx.Surface(ring, SurfaceRole.Ground, AddOnAssets.Or(ctx.S("surfaceAsset"), AddOnAssets.FineGravel), AssetCategories.SurfaceNatural);
            }
            int placed = 0;
            foreach (var (p, yaw) in spots)
            {
                // Every station a different machine: the fitness pool only knows the chin-up bar, so the
                // automatic default is the curated vanilla GymPark set, dealt round in order.
                uint v = (uint)placed;
                var station = AddOnAssets.PickOr(ids, AddOnAssets.Fitness, ids != null && ids.Length > 0 ? Hashing.Mix(ctx.Seed, (uint)placed * 7919u + 3u) : v);
                if (Put(ctx, occ, p, yaw, ObjectRole.Furniture, station, AssetCategories.SportsFitness, 1.2f, Hashing.Mix(ctx.Seed, (uint)placed * 7919u + 3u))) placed++;
            }
            int benches = 0;
            if (ctx.B("benches"))
            {
                var p = layout == "ring" ? site.Center : site.Local(0f, SideSign(site, toward) * (site.HalfWidth - 1.2f));
                if (layout == "row") p = site.Local(0f, -SideSign(site, toward) * (site.HalfWidth - 1.3f));
                if (Put(ctx, occ, p, LeisureKit.Yaw(p, site.Center), ObjectRole.Bench, AssetMix.One(ctx.S("benchAsset")), null, 1.2f, Hashing.Mix(ctx.Seed, 0xBE5u))) benches++;
            }
            if (placed < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.AddOn.Fitness.Issue.Fewer", placed, wanted);
            ctx.Stat("Placemaker.Stat.FitnessStations", placed, Keys.UnitCount);
            ctx.Stat(Keys.StatBenches, benches, Keys.UnitCount);
        }
    }
}
