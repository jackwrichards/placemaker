// Flowerbeds add-on: planted borders (rows of identical bed props on a soil strip), by default lining paths (where
// they are seen), otherwise scattered over lawns with value-noise clustering.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns
{
    [AreaGenerator(150)]
    public sealed class FlowerbedsAddOn : AddOnLayer
    {
        protected override string Area => "AddOn.Flowerbeds";
        public override string Id => "flowerbeds";
        public override string Icon => "Media/Game/Icons/ParksAndRecreation.svg";

        private const float ItemRadius = 0.6f;

        protected override void DefineControls(ControlSet c)
        {
            c.Slider("density", 8, 1, 40, 1, "perHa", ControlTier.Essential, "flowers").Tip();
            c.AssetMulti("assets", AssetCategories.Flowerbed, ControlTier.Essential, "flowers");
            c.Slider("bedSize", 5, 2, 12, 1, "count", ControlTier.More, "flowers").Tip();
            c.Toggle("nearPaths", true, ControlTier.More, "flowers").Tip();
            c.RandomRange("density", 4, 20);
            c.RandomRange("bedSize", 3, 8);
            c.Chance("nearPaths", 0.75f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var ids = ctx.L("assets");
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Flowerbed, ids, Keys.Category(AssetCategories.Flowerbed))) return;
            var occ = Occupancy(ctx);
            int beds = math.max(1, (int)math.round(ctx.LayerRegion.Area / 10000f * ctx.F("density")));
            int perBed = ctx.I("bedSize");
            bool chosen = ids != null && System.Array.Exists(ids, id => !string.IsNullOrEmpty(id));
            var kit = chosen ? ids : AddOnAssets.Flowerbeds;
            float itemRadius = chosen ? AssetMix.MaxFootprint(ctx.Site, ids, ItemRadius) : AddOnAssets.Radius(ctx.Site, "", AddOnAssets.Flowerbeds, ItemRadius);
            // Bed props are squares: the half-edge is ~0.7 of the footprint radius (the half-diagonal).
            float half = math.max(0.5f, itemRadius * 0.72f);
            float pitch = half * 2f + 0.15f;
            var soil = AssetMix.One("", AddOnAssets.BedSoil);

            // Planted borders (2026-10-13 art pass: clusters at random angles read as confetti): each bed
            // is a straight row of identical beds, squared to the path it lines (or the outline), on a
            // strip of soil with a hand's width of margin. Away from paths: the same row along the edge.
            var anchors = ctx.B("nearPaths") && occ.Paths.Count > 0 ? PathAnchors(occ, half + 0.9f) : new List<(float2 p, float2 t)>();
            uint clump = Hashing.Mix(ctx.Seed, 0xF1u);
            foreach (var p in Scatter.Candidates(ctx.LayerRegion, 6f, ctx.Seed, 4000, ctx.Cancel))
                if (Hashing.Unit(Hashing.Mix(clump, (uint)anchors.Count)) < Scatter.Clustering(p, 25f, clump, 0.1f))
                    anchors.Add((p, BoundaryTangent(ctx.LayerRegion.Ring, p)));

            var natural = ctx.Input.Reserved != null && (ctx.Input.Reserved.Natural.Count > 0 || ctx.Input.Reserved.NaturalOnly) ? ctx.Input.Reserved : null;
            int placedBeds = 0, placedItems = 0;
            var used = new List<float2>();
            float minGap = perBed * pitch * 0.5f + 10f;
            foreach (var (a, t) in anchors)
            {
                if (placedBeds >= beds) break;
                bool near = false;
                foreach (var u in used) if (math.distancesq(u, a) < minGap * minGap) { near = true; break; }
                if (near) continue;
                // Longest run of free slots centred on the anchor (up to the bed size).
                var slots = new List<float2>();
                bool fwdOpen = true, backOpen = true;
                for (int i = 0; i < perBed * 2 && slots.Count < perBed && (fwdOpen || backOpen); i++)
                {
                    int k = (i & 1) == 0 ? i / 2 : -(i + 1) / 2;
                    if (k >= 0 ? !fwdOpen : !backOpen) continue;
                    var p = a + t * k * pitch;
                    // (P2-8) a biome's natural ground gets no formal beds
                    if (!occ.Free(p, half, 0.35f, 0.1f) || !ctx.LayerRegion.ContainsDisc(p, half + 0.3f) || natural != null && natural.InNatural(p))
                    {
                        // A row stays unbroken: the first blocked slot ends it on that side.
                        if (k >= 0) fwdOpen = false; else backOpen = false;
                        if (k == 0) backOpen = false;
                        continue;
                    }
                    slots.Add(p);
                }
                if (slots.Count < math.min(2, perBed)) continue;
                slots.Sort((x, y) => math.dot(x - a, t).CompareTo(math.dot(y - a, t)));
                uint bs = Hashing.Mix(ctx.Seed, (uint)placedBeds + 101u);
                var asset = AssetMix.Pick(kit, bs);
                float yaw = math.atan2(t.x, t.y);
                var first = slots[0]; var last = slots[slots.Count - 1];
                ctx.Surface(LayerRegion.Rect((first + last) * 0.5f, t, math.distance(first, last) * 0.5f + half + 0.35f, half + 0.35f), SurfaceRole.Ground, soil, AssetCategories.SurfaceNatural);
                for (int i = 0; i < slots.Count; i++)
                {
                    ctx.Object(slots[i], yaw, ObjectRole.Planter, asset, Hashing.Mix(bs, (uint)i), -1, AssetCategories.Flowerbed);
                    occ.Take(slots[i], half, 0);
                }
                used.Add(a);
                placedBeds++;
                placedItems += slots.Count;
            }
            if (placedBeds == 0 && natural == null) NoSpace(ctx, "Placemaker.AddOn.Flowerbeds.Name");
            ctx.Stat(Keys.StatFlowerbeds, placedBeds, Keys.UnitCount);
            ctx.Plan.Facts["flowerbedItems"] = placedItems;
        }

        /// <summary>Points beside reserved paths every ~20 m, alternating sides, with the path direction.</summary>
        private static List<(float2 p, float2 t)> PathAnchors(Occupancy occ, float offset)
        {
            var result = new List<(float2, float2)>();
            float carry = 0f;
            int side = 0;
            foreach (var s in occ.Paths.Segments)
            {
                float len = math.distance(s.A, s.B);
                if (len < 0.1f) continue;
                var t = (s.B - s.A) / len;
                var n = new float2(-t.y, t.x);
                for (float d = 20f - carry; d < len - 4f; d += 20f)
                {
                    if (d < 4f) continue;   // keep clear of junctions
                    float sign = (side++ & 1) == 0 ? 1f : -1f;
                    result.Add((s.A + t * d + n * sign * (s.HalfWidth + offset), t));
                }
                carry = (carry + len) % 20f;
            }
            return result;
        }

        /// <summary>Direction of the outline edge nearest to <paramref name="p"/> (beds run parallel to it).</summary>
        private static float2 BoundaryTangent(float2[] ring, float2 p)
        {
            float best = float.MaxValue; float2 t = new float2(1, 0);
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[j]; var b = ring[i];
                var ab = b - a; float l2 = math.lengthsq(ab);
                if (l2 < 1e-6f) continue;
                float u = math.saturate(math.dot(p - a, ab) / l2);
                float d = math.distancesq(p, a + ab * u);
                if (d < best) { best = d; t = ab / math.sqrt(l2); }
            }
            return t;
        }
    }
}
