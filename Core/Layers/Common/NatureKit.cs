// Placemaker — the nature makeover's planting and rock kit (docs/runbooks/nature.md). GPL-3.0.
//
// Wild planting reads as groves, not as a lawn of evenly spaced trees: trees stand in clumps with open ground between,
// young ones at a grove's sunny edge, the odd dead snag in an old wood. Rocks are the game's own boulders (Boulder01-08) in
// groups with the small stones (Rock01-30) they shed round them, on the steep ground and at the edges of the woods; fallen
// logs (Log01-18) lie in the wood. Every nature type uses these instead of the even scatter.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class NatureKit
    {
        public static readonly string[] Boulders = { "Boulder01", "Boulder02", "Boulder03", "Boulder04", "Boulder05", "Boulder06", "Boulder07", "Boulder08" };
        public static readonly string[] Stones =
        {
            "Rock01", "Rock02", "Rock03", "Rock04", "Rock05", "Rock06", "Rock07", "Rock08", "Rock09", "Rock10", "Rock11", "Rock12", "Rock13", "Rock14", "Rock15",
            "Rock17", "Rock18", "Rock19", "Rock20", "Rock21", "Rock24", "Rock25", "Rock26", "Rock27", "Rock28", "Rock29", "Rock30",
        };
        /// <summary>Flat stones that sit in the ground (good on a trail edge and in a stream bed).</summary>
        public static readonly string[] FlatStones = { "Rock16", "Rock22", "Rock23" };
        public static readonly string[] Logs = { "Log01", "Log02", "Log04", "Log05", "Log07", "Log08", "Log10", "Log11", "Log13", "Log14", "Log16", "Log17" };

        /// <summary>
        /// Trees or bushes in groves: shuffled candidates (no bands), accepted by a clump field squared so the woods gather and
        /// the glades stay open; trees at a grove's edge are younger. <paramref name="dead"/> is the share left as standing
        /// dead snags (old woods only). Returns the count placed.
        /// </summary>
        public static int Plant(LayerContext ctx, Occupancy occ, LayerRegion region, int wanted, ObjectRole role, string[] ids, float radius,
            float groveScale, uint salt, Func<float2, bool> reject = null, float floor = 0.06f, float dead = 0f, string category = null)
        {
            if (wanted <= 0 || ids == null || ids.Length == 0) return 0;
            int placed = 0;
            uint clump = Hashing.Mix(ctx.Seed, salt);
            uint clump2 = Hashing.Mix(ctx.Seed, salt + 7u);
            var cands = Scatter.Candidates(region, math.max(1.4f, radius * 1.25f), Hashing.Mix(ctx.Seed, salt + 1u), math.min(80000, wanted * 40 + 300), ctx.Cancel, true);
            if (cands.Count == 0) return 0;
            // How much of the ground the woods may cover: the wanted count against what the ground holds packed. A random
            // acceptance alone saturates (every candidate gets its turn, the gaps fill up, the wood comes out even); the groves
            // are the share of the ground where the clump field is above its quantile, the rest is glade with a lone tree.
            // The ground that can take a tree at all (not water, not a path, not reserved): what the candidates say, each standing
            // for its cell. A random sequential packing reaches about half of the hexagonal one: that is what the groves hold.
            var fieldVals = new float[cands.Count];
            var usableVals = new List<float>(cands.Count);
            var ok = new bool[cands.Count];
            for (int i = 0; i < cands.Count; i++)
            {
                fieldVals[i] = ValueNoise.Clumps(cands[i], groveScale, clump) * (0.75f + 0.25f * ValueNoise.Clumps(cands[i], groveScale * 0.35f, clump2));
                ok[i] = (reject == null || !reject(cands[i])) && !occ.Corridors.Overlaps(cands[i], radius, 0.5f);
                if (ok[i]) usableVals.Add(fieldVals[i]);
            }
            if (usableVals.Count == 0) return 0;
            float cellArea = region.Area / math.max(1, cands.Count);
            float usable = usableVals.Count * cellArea;
            float perTree = math.PI * (radius * 2f + 0.4f) * (radius * 2f + 0.4f) * 0.25f / 0.45f;
            float capacity = usable / perTree;
            // never wall to wall: a wood always has its glades (the woods' edges are what reads from the player's height)
            float cover = math.clamp(wanted / math.max(1f, capacity), 0.06f, 0.78f);
            var sorted = usableVals.ToArray();
            Array.Sort(sorted);
            float cut = cover >= 0.999f ? -1f : sorted[math.clamp((int)((1f - cover) * sorted.Length), 0, sorted.Length - 1)];
            for (int ci = 0; ci < cands.Count; ci++)
            {
                var p = cands[ci];
                if (placed >= wanted) break;
                if (!ok[ci]) continue;
                float g = fieldVals[ci];
                // inside a grove: (nearly) always; at its rim: thinning; in the glade: the odd lone tree
                float chance = cut < 0f ? 1f : g >= cut ? math.lerp(0.55f, 1f, math.saturate((g - cut) / 0.08f)) : floor * 0.08f * math.saturate(1f - (cut - g) / 0.2f) + floor * 0.015f;   // per candidate (one every ~7 m²): a lone tree now and then, not a lawn of them
                uint h = Hashing.Mix(clump, (int)(p.x * 13f), (int)(p.y * 7f));
                if (Hashing.Unit(h) > chance) continue;
                if (reject != null && reject(p)) continue;
                // crowns of every size: a spacing that varies from tree to tree (a fixed one packs into a lattice)
                float rr = radius * math.lerp(0.8f, 1.2f, Hashing.Unit(Hashing.Mix(h, 77u)));
                if (!occ.Free(p, rr, rr * 0.6f, 0.4f) || occ.Corridors.Overlaps(p, radius, role == ObjectRole.Tree ? 1.6f : 0.5f)) continue;
                uint v = Hashing.Mix(ctx.Seed, salt * 31u + (uint)placed);
                int age = -1;
                if (role == ObjectRole.Tree)
                {
                    // the grove's heart is old, its edge young (saplings where the light reaches)
                    float heart = cut < 0f ? 0.7f : math.saturate((g - cut) / 0.15f);
                    float r = Hashing.Unit(Hashing.Mix(v, 3u));
                    age = r < 0.12f + 0.3f * (1f - heart) ? 1 : r < 0.45f ? 2 : 3;
                    if (heart > 0.5f && Hashing.Unit(Hashing.Mix(v, 5u)) < dead) age = PlannedAge.Dead;
                    if (age == 1 && Hashing.Unit(Hashing.Mix(v, 6u)) < 0.35f) age = 0;
                }
                var po = ctx.Object(p, Hashing.Unit(v) * 6.283f, role, AssetMix.Pick(ids, v), v, age, category);
                // bushes of every size: big in a thicket's heart, small at its edge and alone (one size everywhere read as polka dots)
                if (role == ObjectRole.Bush) po.Scale = math.lerp(0.5f, 1.35f, Hashing.Unit(Hashing.Mix(v, 9u)) * (cut < 0f ? 1f : 0.45f + 0.55f * math.saturate((g - cut) / 0.15f + 0.3f)));
                occ.Take(p, age == 0 ? rr * 0.6f : rr, role == ObjectRole.Tree ? 3 : 4);
                placed++;
            }
            return placed;
        }

        /// <summary>
        /// Boulder groups: each a big boulder or two half sunk, smaller ones beside, and a spill of small stones downhill.
        /// <paramref name="where"/> scores a spot (0 never .. 1 best: steep ground, a grove edge, a summit). Returns boulders placed.
        /// </summary>
        public static int Rocks(LayerContext ctx, Occupancy occ, LayerRegion region, int groups, uint salt, Func<float2, float> where = null, Func<float2, float> height = null, float size = 1f)
        {
            if (groups <= 0 || !AssetAvailability.Has(AssetCategories.Rock)) return 0;
            int boulders = 0, made = 0;
            var centres = new List<float2>();
            height = height ?? ctx.H;
            foreach (var c in Scatter.Candidates(region, 6f, Hashing.Mix(ctx.Seed, salt), 4000, ctx.Cancel, true))
            {
                if (made >= groups) break;
                uint h = Hashing.Mix(Hashing.Mix(ctx.Seed, salt + 11u), made * 977, (int)(c.x * 3f));
                float score = where == null ? 0.5f : where(c);
                if (Hashing.Unit(h) > score) continue;
                if (!region.ContainsDisc(c, 5f) || !occ.Free(c, 3.2f, 1f, 0.5f)) continue;
                bool far = true;
                foreach (var o in centres) if (math.distancesq(o, c) < 30f * 30f) { far = false; break; }
                if (!far) continue;
                centres.Add(c);
                made++;
                // downhill from the group (stones roll that way)
                float e = 2f;
                var down = -new float2(height(c + new float2(e, 0)) - height(c - new float2(e, 0)), height(c + new float2(0, e)) - height(c - new float2(0, e)));
                down = math.lengthsq(down) < 1e-5f ? new float2(math.cos(h * 1e-3f), math.sin(h * 1e-3f)) : math.normalize(down);
                int big = 1 + (int)(Hashing.Unit(Hashing.Mix(h, 1u)) * 2.4f);
                for (int i = 0; i < big; i++)
                {
                    uint v = Hashing.Mix(h, (uint)(10 + i));
                    var p = c + (i == 0 ? float2.zero : new float2(Hashing.Unit(v) - 0.5f, Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 7f);
                    float s = size * (i == 0 ? 0.75f + 0.5f * Hashing.Unit(Hashing.Mix(v, 2u)) : 0.35f + 0.35f * Hashing.Unit(Hashing.Mix(v, 2u)));
                    if (!occ.Free(p, 2.4f * s, 0.5f, 0.3f)) continue;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(Boulders, v), v, -1, AssetCategories.Rock);
                    o.Scale = s;
                    // half sunk: the boulder's foot below the ground, so it reads as bedrock coming through, not a dropped prop
                    o.Elevation = -0.35f * s;
                    occ.Take(p, 2.4f * s, 5);
                    boulders++;
                }
                // the small stones: a spill downhill, a few round the sides
                int stones = 5 + (int)(Hashing.Unit(Hashing.Mix(h, 2u)) * 7f);
                for (int i = 0; i < stones; i++)
                {
                    uint v = Hashing.Mix(h, (uint)(40 + i));
                    float along = 2.5f + Hashing.Unit(v) * 7f;
                    float across = (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * (3f + along * 0.8f);
                    var p = c + down * along + new float2(-down.y, down.x) * across;
                    if (!region.ContainsDisc(p, 1f) || !occ.Free(p, 0.5f, 0.6f, 0.3f)) continue;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(Stones, v), v, -1, AssetCategories.Rock);
                    o.Scale = 0.6f + 0.9f * Hashing.Unit(Hashing.Mix(v, 3u));
                    occ.Take(p, 0.5f, 5);
                }
            }
            ctx.Plan.Facts["boulders"] = boulders;
            return boulders;
        }

        /// <summary>Fallen trunks lying in the wood (near trees, never on a trail or in a clearing). Returns logs placed.</summary>
        public static int Fallen(LayerContext ctx, Occupancy occ, LayerRegion region, int count, uint salt, Func<float2, bool> ok = null)
        {
            int placed = 0;
            // a trunk falls where trees stand: at least two trees within 9 m (logs alone on a meadow read as dropped props)
            var trees = new List<float2>();
            foreach (var o in ctx.Plan.Objects) if (o != null && o.Role == ObjectRole.Tree) trees.Add(o.Position.xz);
            bool InWood(float2 q) { int c = 0; foreach (var t in trees) if (math.distancesq(t, q) < 81f && ++c >= 2) return true; return false; }
            for (int tries = 0; placed < count && tries < count * 14; tries++)
            {
                uint v = Hashing.Mix(ctx.Seed, salt + (uint)tries);
                var p = region.Min + new float2(Hashing.Unit(v), Hashing.Unit(Hashing.Mix(v, 1u))) * (region.Max - region.Min);
                float yaw = Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f;
                var along = new float2(math.sin(yaw), math.cos(yaw));
                if (!region.ContainsDisc(p, 6f) || (ok != null && !ok(p)) || !InWood(p)) continue;
                // the game draws its logs at their full 10 m whatever the scale
                if (!occ.Free(p, 1.2f, 0f, 0.2f) || !occ.Free(p + along * 4.8f, 0.8f, 0f, 0.2f) || !occ.Free(p - along * 4.8f, 0.8f, 0f, 0.2f) || !occ.Free(p + along * 2.5f, 0.8f, 0f, 0.2f) || !occ.Free(p - along * 2.5f, 0.8f, 0f, 0.2f)) continue;
                var o = ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(Logs, v), v, -1, AssetCategories.PlayNature);

                occ.TakeCorridor(p - along * 5.2f, p + along * 5.2f, 0.6f, 5);
                placed++;
            }
            return placed;
        }
    }
}
