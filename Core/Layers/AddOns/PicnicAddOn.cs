// Picnic add-on: groups of picnic tables, each around an optional shelter with a few shade
// trees on the outside, spread over the open lawn.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(130)]
    public sealed class PicnicAddOn : AddOnLayer
    {
        protected override string Area => "AddOn.Picnic";
        public override string Id => "picnic";
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("groups", 3, 1, 10, 1, "count", ControlTier.Essential, "picnic");
            c.Slider("tables", 3, 1, 6, 1, "count", ControlTier.Essential, "picnic").Tip();
            c.AssetMulti("tableAssets", AssetCategories.Picnic, ControlTier.More, "picnic");
            c.Toggle("shelter", true, ControlTier.More, "extras");
            c.Asset("shelterAsset", AssetCategories.Shelter, ControlTier.More, "extras").When("shelter=true");
            c.Toggle("shadeTrees", true, ControlTier.More, "extras");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "extras").When("shadeTrees=true");
            c.RandomRange("groups", 1, 5);
            c.RandomRange("tables", 2, 5);
            c.Chance("shelter", 0.6f);
            c.Chance("shadeTrees", 0.8f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var tableIds = ctx.L("tableAssets");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Picnic, tableIds, Keys.Category(AssetCategories.Picnic))) return;
            var occ = Occupancy(ctx);
            bool chosenTables = tableIds != null && System.Array.Exists(tableIds, id => !string.IsNullOrEmpty(id));
            float tableRadius = chosenTables ? AssetMix.MaxFootprint(ctx.Site, tableIds, 1.6f) : AddOnAssets.Radius(ctx.Site, "", AddOnAssets.PicnicTables, 1.5f);
            bool shelter = ctx.B("shelter") && AssetAvailability.Require(ctx.Plan, AssetCategories.Shelter, new[] { ctx.S("shelterAsset") }, Keys.Category(AssetCategories.Shelter));
            var shelterAsset = AddOnAssets.Or(ctx.S("shelterAsset"), PicnicShelters);
            float shelterRadius = shelter ? AddOnAssets.Radius(ctx.Site, ctx.S("shelterAsset"), PicnicShelters, 4f) : 0f;
            int tables = ctx.I("tables");
            // Without a shelter the tables stand in one straight, evenly spaced row (2026-10-13: a ring
            // with a random phase read as tables dropped at random angles).
            float ring = shelter ? shelterRadius + tableRadius + 1.2f : (tables > 1 ? (tables - 1) * (tableRadius * 2f + 1.4f) * 0.5f : 0f);
            float groupRadius = ring + tableRadius + (ctx.B("shadeTrees") ? 4f : 1f);
            var spots = Spread(occ, groupRadius, ctx.I("groups"), Spots.Preference.Open, ctx.Seed, groupRadius * 2.5f);
            bool compact = false;
            if (spots.Count == 0)
            {
                // No room for a full group: one compact group of tables, no shelter or shade trees, beats "no space".
                shelter = false; compact = true;
                ring = tables > 1 ? (tables - 1) * (tableRadius * 2f + 1.4f) * 0.5f : 0f;
                groupRadius = ring + tableRadius + 1f;
                spots = Spread(occ, groupRadius, 1, Spots.Preference.Open, ctx.Seed, groupRadius * 2.5f);
            }
            int placedTables = 0, placedTrees = 0;
            var treeIds = ctx.L("treeSpecies");
            bool trees = !compact && ctx.B("shadeTrees") && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree));
            for (int g = 0; g < spots.Count; g++)
            {
                var c = spots[g];
                uint gs = Hashing.Mix(ctx.Seed, (uint)g + 1u);
                float phase = Hashing.Unit(gs) * 2f * math.PI;
                if (shelter && occ.Free(c, shelterRadius, 0.5f, 0.3f))
                {
                    ctx.Object(c, phase, ObjectRole.Furniture, shelterAsset, gs, -1, AssetCategories.Shelter);
                    occ.Take(c, shelterRadius, 0);
                }
                // Row direction: along the nearest path (tables parallel to the walk), else the group's phase.
                var rowDir = new float2(math.cos(phase), math.sin(phase));
                if (occ.Paths.Count > 0 && occ.Paths.Nearest(c, 60f, out var onPath) >= 0)
                {
                    var away = math.normalizesafe(c - onPath, rowDir);
                    rowDir = new float2(-away.y, away.x);
                }
                List<float2> positions;
                if (tables == 1 || ring <= 0f) positions = new List<float2> { c };
                else if (shelter) positions = RingPoints(c, ring, tables, math.atan2(rowDir.y, rowDir.x) + math.PI / tables);
                else
                {
                    positions = new List<float2>();
                    for (int t = 0; t < tables; t++) positions.Add(c + rowDir * (t / (float)(tables - 1) * 2f - 1f) * ring);
                }
                float rowYaw = math.atan2(rowDir.x, rowDir.y) + math.PI * 0.5f;
                for (int t = 0; t < positions.Count; t++)
                {
                    var p = positions[t];
                    if (!occ.Free(p, tableRadius, 0.5f, 0.4f)) continue;
                    uint v = Hashing.Mix(gs, (uint)t);
                    // Round a shelter: tangential; in a row: all parallel, benches to the walk.
                    float yaw = shelter ? Facing(p, c) + math.PI * 0.5f : rowYaw;
                    ctx.Object(p, yaw, ObjectRole.Furniture, AddOnAssets.PickOr(tableIds, AddOnAssets.PicnicTables, chosenTables ? v : gs), v, -1, AssetCategories.Picnic);
                    occ.Take(p, tableRadius, 1);
                    placedTables++;
                }
                // A barbecue grill at the downwind end of the group and a bin beside it.
                if (placedTables > 0)
                {
                    float reach = (shelter ? ring : math.max(ring, 0f)) + tableRadius + 1.8f;
                    var gp = c + (shelter ? new float2(-rowDir.y, rowDir.x) : rowDir) * reach;
                    if (LeisureKit.Put(ctx, occ, gp, Facing(gp, c), ObjectRole.Furniture, AssetMix.One("", AddOnAssets.Grills), AssetCategories.Picnic, 0.8f, Hashing.Mix(gs, 0x6E1u), 0.4f, 0.3f))
                        LeisureKit.Put(ctx, occ, gp + math.normalizesafe(gp - c) * 1.3f, 0f, ObjectRole.Bin, AssetMix.One("", AddOnAssets.Bin), null, 0.4f, Hashing.Mix(gs, 0xB1u), 0.2f, 0.1f);
                }
                if (!trees) continue;
                // Two or three trees on one side of the ring: a shaded edge, while the rest stays open lawn.
                int count = 2 + (int)(Hashing.Mix(gs, 3u) % 2u);
                for (int k = 0; k < count; k++)
                {
                    float a = math.radians(200f + k * 35f) + (Hashing.Unit(Hashing.Mix(gs, (uint)k + 9u)) - 0.5f) * 0.4f;
                    var p = c + new float2(math.cos(a), math.sin(a)) * (ring + tableRadius + 3.2f);
                    if (!occ.Free(p, 2.5f, 1f, 0.5f)) continue;
                    if (ctx.Input.Reserved != null && ctx.Input.Reserved.InNatural(p)) continue;   // (P2-8) a meadow stays a meadow
                    uint v = Hashing.Mix(gs, 0x7000u + (uint)k);
                    ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), v, 3);
                    occ.Take(p, 2.5f, 2);
                    placedTrees++;
                }
            }
            if (spots.Count == 0) NoSpace(ctx, "Placemaker.AddOn.Picnic.Name");
            ctx.Stat(Keys.StatPicnicTables, placedTables, Keys.UnitCount);
            ctx.Stat(Keys.StatTrees, placedTrees, Keys.UnitCount);
        }

        /// <summary>Open timber picnic shelters (campground shelters, then a pergola).</summary>
        private static readonly string[] PicnicShelters = { "CampgroundShelter02", "CampgroundShelter01", "Pergola02" };
    }
}
