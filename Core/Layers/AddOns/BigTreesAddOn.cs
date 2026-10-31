// Big trees add-on: sparse specimen trees (oldest growth stage) standing alone on open lawn,
// far from other trees so their crowns read as landmarks. Candidates come from the shared
// scatter sampler and are taken most-open-first.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(140)]
    public sealed class BigTreesAddOn : AddOnLayer
    {
        protected override string Area => "AddOn.BigTrees";
        public override string Id => "bigtrees";
        public override string Icon => "Media/Game/Icons/Forest.svg";

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("density", 2, 0.5, 10, 0.5, "perHa", ControlTier.Essential, "trees").Tip();
            c.AssetMulti("species", AssetCategories.Tree, ControlTier.Essential, "trees");
            c.Slider("spacing", 25, 12, 60, 1, "m", ControlTier.More, "trees").Layout(8).Tip();
            c.Slider("clearance", 6, 3, 15, 0.5, "m", ControlTier.More, "trees").Tip();
            c.RandomRange("density", 1, 5);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var ids = ctx.L("species");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, ids, Keys.Category(AssetCategories.Tree))) return;
            var occ = Occupancy(ctx);
            int target = math.max(1, (int)math.round(ctx.LayerRegion.Area / 10000f * ctx.F("density")));
            float spacing = ctx.F("spacing"), clearance = ctx.F("clearance");
            var candidates = Scatter.Candidates(ctx.LayerRegion, math.max(3f, spacing * 0.35f), ctx.Seed, 6000, ctx.Cancel);
            // Rank by openness (with a little seeded noise so equal lawns do not always pick the same corner).
            var ranked = new List<(float score, float2 p)>(candidates.Count);
            foreach (var p in candidates)
            {
                float open = occ.Clearance(p, clearance * 3f);
                if (open < clearance) continue;
                ranked.Add((open + Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)ranked.Count)) * clearance * 0.5f, p));
            }
            ranked.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : a.p.x != b.p.x ? a.p.x.CompareTo(b.p.x) : a.p.y.CompareTo(b.p.y));
            var placed = new List<float2>();
            foreach (var (_, p) in ranked)
            {
                if (placed.Count >= target) break;
                bool tooClose = false;
                foreach (var q in placed) if (math.distancesq(p, q) < spacing * spacing) { tooClose = true; break; }
                if (tooClose || !occ.Free(p, clearance, clearance * 0.5f, 0f)) continue;
                // (P2-8) specimen trees stand on the park's own lawn: a biome's ground is the biome's (100 % meadow has no trees)
                if (ctx.Input.Reserved != null && ctx.Input.Reserved.InNatural(p)) continue;
                uint v = Hashing.Mix(ctx.Seed, (uint)placed.Count + 1u);
                ctx.Object(p, Hashing.Unit(v) * 2f * math.PI, ObjectRole.Tree, AssetMix.Pick(ids, v), v, 3);
                occ.Take(p, 2.5f, 0);
                placed.Add(p);
            }
            if (placed.Count == 0 && (ctx.Input.Reserved == null || ctx.Input.Reserved.Natural.Count == 0 && !ctx.Input.Reserved.NaturalOnly)) NoSpace(ctx, "Placemaker.AddOn.BigTrees.Name");
            ctx.Stat(Keys.StatTrees, placed.Count, Keys.UnitCount);
        }
    }
}
