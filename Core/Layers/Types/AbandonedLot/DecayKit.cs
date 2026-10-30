// Building blocks for places that are falling apart (abandoned lot, urban ruins). Decay reads as a
// STORY only when it has gradients and causes, not uniform noise:
//   * a collapse point: rubble piles near it, chunks further out, dust/dirt decals at the fringe;
//   * old paving breaks into shards along cracks; weeds come up in the cracks first;
//   * overgrowth reclaims from the edges and corners inwards (fences catch seeds, nobody walks there);
//   * fences rot in pieces, never all at once; desire paths are worn where people cut across.
// Everything here places through Occupancy (input.Reserved + what the layer placed), so the
// shared invariants hold: inside the outline, clear of paths and of reserved space.

using Placemaker.Core.Layers.Types.Scrapyard;
using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Ruins
{
    /// <summary>Hard zones a layer laid out itself (slabs, pads, modules): kept out of scatters, never handed to Occupancy.</summary>
    public sealed class Zones
    {
        private readonly List<float2[]> _rings = new List<float2[]>();
        private readonly List<float4> _boxes = new List<float4>();

        public IReadOnlyList<float2[]> Rings => _rings;

        public void Add(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return;
            Poly.Bounds(ring, out var mn, out var mx);
            _rings.Add(ring);
            _boxes.Add(new float4(mn, mx));
        }

        /// <summary>True when p lies inside a zone or within <paramref name="margin"/> of one.</summary>
        public bool Inside(float2 p, float margin = 0f)
        {
            for (int i = 0; i < _rings.Count; i++)
            {
                var b = _boxes[i];
                if (p.x < b.x - margin || p.y < b.y - margin || p.x > b.z + margin || p.y > b.w + margin) continue;
                if (Poly.Contains(_rings[i], p)) return true;
                if (margin > 0f && Poly.DistanceToBoundary(_rings[i], p) < margin) return true;
            }
            return false;
        }

        /// <summary>True when the ring overlaps no zone (vertex/centre containment both ways + edge crossings).</summary>
        public bool Clear(float2[] ring, float margin = 0.5f)
        {
            foreach (var p in ring) if (Inside(p, margin)) return false;
            if (Inside(Poly.Centroid(ring), margin)) return false;
            foreach (var z in _rings)
            {
                foreach (var q in z) if (Poly.Contains(ring, q)) return false;
                for (int i = 0; i < z.Length; i++)
                    for (int j = 0; j < ring.Length; j++)
                        if (Poly.SegmentsIntersect(z[i], z[(i + 1) % z.Length], ring[j], ring[(j + 1) % ring.Length])) return false;
            }
            return true;
        }
    }

    public static class DecayKit
    {
        /// <summary>Gives a reused feature a type-specific label ("Surviving old trees").</summary>
        public static FeatureInfo Relabel(FeatureInfo f, string labelKey) { f.LabelKey = labelKey; return f; }

        /// <summary>
        /// First category of the chain that can be placed (the first when the player chose assets).
        /// Adds the shared "no assets" Info issue once per category when nothing in the chain exists.
        /// </summary>
        public static string Cat(LayerContext ctx, string[] chosen, params string[] chain)
        {
            if (chosen != null) foreach (var id in chosen) if (!string.IsNullOrEmpty(id)) return chain[0];
            foreach (var c in chain) if (AssetAvailability.Has(c)) return c;
            var label = Keys.Category(chain[0]);
            foreach (var i in ctx.Plan.Issues)
                if (i.Key == Keys.NoAssetsIssue && i.Args.Length > 0 && (i.Args[0] as string) == label) return null;
            ctx.Issue(IssueSeverity.Info, Keys.NoAssetsIssue, label);
            return null;
        }

        /// <summary>A decal at p (decals never reserve space; they only keep out of reserved space and stay inside).</summary>
        public static bool Decal(LayerContext ctx, Occupancy guard, float2 p, float yaw, string category, uint v)
        {
            if (category == null || !guard.Free(p, 0.6f, 0.4f, 0f)) return false;
            ctx.Object(p, yaw, ObjectRole.Decal, AssetMix.One(""), v, -1, category);
            return true;
        }

        /// <summary>Spiral search for a free oriented rectangle near <paramref name="near"/> (both orientations).</summary>
        public static bool FindRect(LayerRegion region, Occupancy occ, Zones zones, float2 near, float2 axis, float halfLength, float halfWidth,
            float margin, uint seed, out OrientedRect rect, int tries = 160)
        {
            rect = default;
            if (halfLength <= 0.5f || halfWidth <= 0.5f) return false;
            const float golden = 2.39996323f;
            // Fine spiral around the wish point first, then a coarse one reaching across the whole region.
            float fine = math.max(2f, math.min(halfWidth, 8f) * 0.6f);
            float coarse = math.max(fine, math.length(region.Max - region.Min) / math.sqrt(tries) * 0.7f);
            float phase = Hashing.Unit(seed) * 6.283f;
            for (int pass = 0; pass < 4; pass++)
            {
                var a = (pass & 1) == 0 ? axis : new float2(-axis.y, axis.x);
                float step = pass < 2 ? fine : coarse;
                if (pass >= 2 && coarse <= fine * 1.01f) break;
                for (int i = pass < 2 ? 0 : 1; i < tries; i++)
                {
                    float r = step * math.sqrt(i);
                    var c = near + new float2(math.cos(phase + i * golden), math.sin(phase + i * golden)) * r;
                    if (!region.Contains(c)) continue;
                    var cand = new OrientedRect { Center = c, Axis = a, HalfLength = halfLength, HalfWidth = halfWidth };
                    bool corners = true;
                    foreach (var q in cand.Ring) if (region.BoundaryDistance(q, margin + 0.01f) < margin || !region.Contains(q)) { corners = false; break; }
                    if (!corners) continue;
                    if (zones != null && !zones.Clear(cand.Ring, 1f)) continue;
                    if (!LeisureKit.RectFits(region, occ, cand, margin)) continue;
                    rect = cand;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Largest rectangle along <paramref name="axis"/> through <paramref name="c"/> that fits (shrinks length, then width).</summary>
        public static bool FitRect(LayerRegion region, Occupancy occ, float2 c, float2 axis, float halfLength, float halfWidth, float margin, out OrientedRect rect,
            float minHalfLength = 4f, float minHalfWidth = 2f)
        {
            rect = new OrientedRect { Center = c, Axis = axis, HalfLength = halfLength, HalfWidth = halfWidth };
            for (int k = 0; k < 14; k++)
            {
                if (rect.HalfLength < minHalfLength || rect.HalfWidth < minHalfWidth) return false;
                if (LeisureKit.RectFits(region, occ, rect, margin)) return true;
                if ((k & 1) == 0) rect.HalfLength *= 0.85f; else rect.HalfWidth *= 0.92f;
            }
            return false;
        }

        // ───────────── paving that breaks up ─────────────

        public sealed class Slab
        {
            public readonly List<float2[]> Shards = new List<float2[]>();
            public readonly List<(float2 a, float2 b)> Cracks = new List<(float2 a, float2 b)>();
            public int Missing;
        }

        /// <summary>
        /// An old slab broken into shards along slanted cracks (gap grows with <paramref name="breakup"/>),
        /// a few shards missing entirely at high breakup. Emits the shard surfaces; returns shards and cracks
        /// (the interior crack lines plus the slab outline, where weeds come up first).
        /// </summary>
        public static Slab BrokenSlab(LayerContext ctx, OrientedRect r, float breakup, SurfaceRole role, AssetRef asset, string category, uint seed)
        {
            var slab = new Slab();
            breakup = math.saturate(breakup);
            int nu = math.clamp(1 + (int)(breakup * r.HalfLength * 2f / 9f), 1, 6);
            int nv = math.clamp(1 + (int)(breakup * r.HalfWidth * 2f / 11f), 1, 3);
            while (nu > 1 && r.HalfLength * 2f / nu < 3f) nu--;
            while (nv > 1 && r.HalfWidth * 2f / nv < 3f) nv--;
            // Cut lines: u-lines run across (u = a + (b-a)·t), v-lines along (v = c + (d-c)·s).
            var ua = new float[nu + 1]; var ub = new float[nu + 1];
            var vc = new float[nv + 1]; var vd = new float[nv + 1];
            float cellU = r.HalfLength * 2f / nu, cellV = r.HalfWidth * 2f / nv;
            for (int i = 0; i <= nu; i++)
            {
                float u = -r.HalfLength + i * cellU;
                bool edge = i == 0 || i == nu;
                uint h = Hashing.Mix(seed, (uint)i * 7u + 1u);
                ua[i] = u + (edge ? 0f : (Hashing.Unit(h) - 0.5f) * cellU * 0.35f);
                ub[i] = u + (edge ? 0f : (Hashing.Unit(Hashing.Mix(h)) - 0.5f) * cellU * 0.35f);
            }
            for (int j = 0; j <= nv; j++)
            {
                float v = -r.HalfWidth + j * cellV;
                bool edge = j == 0 || j == nv;
                uint h = Hashing.Mix(seed, (uint)j * 13u + 5u);
                vc[j] = v + (edge ? 0f : (Hashing.Unit(h) - 0.5f) * cellV * 0.3f);
                vd[j] = v + (edge ? 0f : (Hashing.Unit(Hashing.Mix(h)) - 0.5f) * cellV * 0.3f);
            }
            float2 Corner(int i, int j)
            {
                // Intersect u-line i with v-line j (two fixed-point steps; slopes are small).
                float v = (vc[j] + vd[j]) * 0.5f, u = 0f;
                for (int k = 0; k < 3; k++)
                {
                    u = math.lerp(ua[i], ub[i], (v + r.HalfWidth) / (2f * r.HalfWidth));
                    v = math.lerp(vc[j], vd[j], (u + r.HalfLength) / (2f * r.HalfLength));
                }
                return r.Local(u, v);
            }
            float gap = math.lerp(0.25f, 1.1f, breakup);
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    uint h = Hashing.Mix(seed, i * 31 + 3, j * 17 + 9);
                    if (nu * nv > 2 && Hashing.Unit(h) < breakup * 0.18f) { slab.Missing++; continue; }
                    var q = new[] { Corner(i, j), Corner(i + 1, j), Corner(i + 1, j + 1), Corner(i, j + 1) };
                    var c = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
                    for (int k = 0; k < 4; k++)
                    {
                        // Shrink towards the centre: half the crack gap on each side (none on the outline).
                        bool outerU = (k == 0 || k == 3) ? i == 0 : i + 1 == nu;
                        bool outerV = (k == 0 || k == 1) ? j == 0 : j + 1 == nv;
                        float shrink = (outerU && outerV) ? 0f : gap * 0.5f;
                        var d = c - q[k];
                        float len = math.length(d);
                        if (len > shrink + 0.5f) q[k] += d / len * shrink;
                    }
                    var ring = Poly.EnsureCcw(q);
                    if (Poly.MinEdgeLength(ring) < 1.5f) continue;
                    if (ctx.Surface(ring, role, asset, category)) slab.Shards.Add(ring);
                }
            for (int i = 1; i < nu; i++) slab.Cracks.Add((r.Local(ua[i], -r.HalfWidth), r.Local(ub[i], r.HalfWidth)));
            for (int j = 1; j < nv; j++) slab.Cracks.Add((r.Local(-r.HalfLength, vc[j]), r.Local(r.HalfLength, vd[j])));
            var outline = r.Ring;
            for (int k = 0; k < 4; k++) slab.Cracks.Add((outline[k], outline[(k + 1) % 4]));
            return slab;
        }

        /// <summary>Cracks that wander across a slab from its edges (frost, roots): short legs that turn a little at each step and
        /// stop inside, as segments (a, b). For an old car park or yard, where a grid of shards would read as paving slabs.</summary>
        public static List<(float2 a, float2 b)> WanderingCracks(OrientedRect r, int count, uint seed)
        {
            var list = new List<(float2, float2)>();
            for (int i = 0; i < count; i++)
            {
                uint h = Hashing.Mix(seed, (uint)i * 31u + 7u);
                float side = Hashing.Unit(h), along = Hashing.Unit(Hashing.Mix(h, 1u)) * 2f - 1f;
                float2 p, dir;
                if (side < 0.25f) { p = r.Local(-r.HalfLength, along * r.HalfWidth); dir = r.Axis; }
                else if (side < 0.5f) { p = r.Local(r.HalfLength, along * r.HalfWidth); dir = -r.Axis; }
                else if (side < 0.75f) { p = r.Local(along * r.HalfLength, -r.HalfWidth); dir = r.Side; }
                else { p = r.Local(along * r.HalfLength, r.HalfWidth); dir = -r.Side; }
                float ang = math.atan2(dir.y, dir.x) + (Hashing.Unit(Hashing.Mix(h, 2u)) - 0.5f) * 1.2f;
                float len = math.lerp(8f, math.max(10f, math.min(r.HalfLength, r.HalfWidth) * 1.6f), Hashing.Unit(Hashing.Mix(h, 3u)));
                int legs = math.max(2, (int)(len / 4.5f));
                for (int k = 0; k < legs; k++)
                {
                    ang += (Hashing.Unit(Hashing.Mix(h, 10u + (uint)k)) - 0.5f) * 0.9f;
                    var q = p + new float2(math.cos(ang), math.sin(ang)) * (len / legs);
                    var lq = q - r.Center;
                    if (math.abs(math.dot(lq, r.Axis)) > r.HalfLength - 0.5f || math.abs(math.dot(lq, r.Side)) > r.HalfWidth - 0.5f) break;
                    list.Add((p, q));
                    p = q;
                }
            }
            return list;
        }

        /// <summary>Weeds coming up along cracks (spacing ~1.4 m, chance = <paramref name="amount"/>). Returns the count.</summary>
        public static int CrackWeeds(LayerContext ctx, Occupancy occ, List<(float2 a, float2 b)> cracks, float amount, string[] ids, string category, uint salt)
        {
            int n = 0;
            if (amount <= 0f) return 0;
            for (int s = 0; s < cracks.Count; s++)
            {
                var (a, b) = cracks[s];
                foreach (var p0 in LeisureKit.Along(a, b, 1.4f))
                {
                    uint v = Hashing.Mix(ctx.Seed, salt + (uint)s * 131u + (uint)n * 7u + (uint)(p0.x * 3f));
                    // (rundown) the slab's own rim (its last four segments) gets a third: a full row of bushes round a slab read robotic
                    float along = s >= cracks.Count - 4 && cracks.Count > 4 ? amount * 0.33f : amount;
                    if (Hashing.Unit(v) > along * (0.35f + 0.65f * ValueNoise.Clumps(p0, 6f, Hashing.Mix(ctx.Seed, salt)))) continue;
                    var p = p0 + (new float2(Hashing.Unit(Hashing.Mix(v, 1u)), Hashing.Unit(Hashing.Mix(v, 2u))) - 0.5f) * 0.6f;
                    if (!occ.Free(p, 0.5f, 0.6f, 0.05f)) continue;
                    var o = ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Bush, AssetMix.Pick(ids, v), v, -1, category);
                    o.Scale = 0.6f + 0.5f * Hashing.Unit(Hashing.Mix(v, 3u));
                    occ.Take(p, 0.5f, 4);
                    n++;
                }
            }
            return n;
        }

        // ───────────── rubble gradient ─────────────

        public struct RubbleResult { public int Piles, Chunks, Decals; }

        /// <summary>
        /// Rubble around a collapse point: big piles near it, smaller chunks further out, dirt decals at the
        /// fringe. Density falls off as (1-t)^1.6 with value-noise clumping, scaled by <paramref name="intensity"/>.
        /// </summary>
        /// <summary>Small debris round rubble heaps: broken stone, flat gravel spills, rebar stumps.</summary>
        public static readonly GritAssets.Set Chunks = new GritAssets.Set(false,
            // (rundown, 2026-10-29) only real low stone heaps: the small pylon stumps read as gravestones and the flat rocks as pale discs
            // on the lawn in the game; the chunks and grit between the heaps are library decals now (RundownGround.Rubble)
            "Prop_CoalTruck01_StonePile01_Partial1|CoalTruck01_StonePile01", "CoalTruck01_StonePile01", "Prop_CoalTruck01_StonePile01_Partial2|CoalTruck01_StonePile01");

        public static RubbleResult RubbleField(LayerContext ctx, Occupancy occ, Occupancy guard, LayerRegion region, float2 focus, float radius, float intensity,
            string[] ids, string pileCategory, float pileScale, uint salt, Func<float2, bool> reject, int maxItems = 500)
        {
            var res = new RubbleResult();
            if (intensity <= 0f || radius < 2f) return res;
            string dirt = AssetAvailability.Has(AssetCategories.DecalDirt) ? AssetCategories.DecalDirt : null;
            uint seed = Hashing.Mix(ctx.Seed, salt);
            float reach = radius * 1.25f;
            // Scatter only over the reach disc (clipped to the outline): candidates over the whole region
            // would leave a small collapse point with almost nothing.
            var disc = LeisureKit.Intersect(region, LayerRegion.Circle(focus, reach, 20)) ?? region;
            int max = math.clamp((int)(disc.Area / 3f), 50, 9000);
            // Art pass: automatic piles are the game's real rubble heaps (8–10 m) and chunks are small
            // debris — the same 9 m heap as a "0.8 m chunk" stamped hundreds of overlapping mountains.
            bool auto = ids == null || ids.Length == 0;
            float pileR = auto ? 4.2f : 1.8f * pileScale;
            foreach (var p in Scatter.Candidates(disc, 1.8f, seed, max, ctx.Cancel))
            {
                if (res.Piles + res.Chunks >= maxItems) break;
                float d = math.distance(p, focus);
                if (d > reach) continue;
                if (reject != null && reject(p)) continue;
                float t = d / radius;
                uint v = Hashing.Mix(seed, (int)(p.x * 7f), (int)(p.y * 11f));
                float clump = Scatter.Clustering(p, 7f, Hashing.Mix(seed, 3u), 0.35f);
                float chance = intensity * math.pow(math.saturate(1f - t), 1.6f) * clump * 0.85f;
                if (t < 1f && pileCategory != null && Hashing.Unit(v) < chance)
                {
                    bool pile = t < 0.4f && Hashing.Unit(Hashing.Mix(v, 9u)) < 0.6f; if (!pile && Hashing.Unit(Hashing.Mix(v, 10u)) > 0.3f) continue;   // (rundown) fewer loose heaps: decals carry the chunks
                    float r = pile ? pileR : 0.8f;
                    if (!occ.Free(p, r, r * 0.5f + 0.3f, 0.15f)) continue;
                    var asset = !auto ? AssetMix.Pick(ids, v) : pile ? GritAssets.For(pileCategory, v, seed) : GritAssets.Ref(Chunks, v);
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 4u)) * 6.283f, ObjectRole.Furniture, asset, v, -1, pileCategory);
                    // Real assets carry the size now; scale only nudges (piles a touch larger than chunks).
                    o.Scale = auto ? (pile ? 1f : 0.8f) + 0.15f * Hashing.Unit(Hashing.Mix(v, 5u))
                        : pile ? pileScale * (0.9f + 0.6f * Hashing.Unit(Hashing.Mix(v, 5u))) : 0.45f + 0.4f * Hashing.Unit(Hashing.Mix(v, 5u));
                    occ.Take(p, r, 2);
                    if (pile) res.Piles++; else res.Chunks++;
                }
                else if (dirt != null && t > 0.3f && Hashing.Unit(Hashing.Mix(v, 6u)) < intensity * 0.06f * (1.3f - math.abs(t - 0.9f)))
                {
                    if (Decal(ctx, guard, p, Hashing.Unit(Hashing.Mix(v, 7u)) * 6.283f, dirt, v)) res.Decals++;
                }
            }
            return res;
        }

        // ───────────── overgrowth ─────────────

        /// <summary>
        /// Wild plants reclaiming from the edges: weeds/bushes clumped and edge-biased (chance falls from
        /// 1 at the outline to 0.3 at <paramref name="edgeReach"/> m inside), young self-seeded trees
        /// (age 0–1) mostly near the outline. Returns (weeds, trees).
        /// </summary>
        public static (int weeds, int trees) Overgrowth(LayerContext ctx, Occupancy occ, LayerRegion region, float amount, float treesPerHa,
            string[] weedIds, string weedCategory, string[] treeIds, uint salt, Func<float2, bool> reject, float edgeReach = 18f, float thicket = 0f)
        {
            if (amount <= 0f) return (0, 0);
            float ha = region.Area / 10000f;
            uint seed = Hashing.Mix(ctx.Seed, salt);
            bool Edgy(float2 p, float floor)
            {
                if (reject != null && reject(p)) return true;
                float e = region.BoundaryDistance(p, edgeReach + 1f);
                float keep = math.lerp(1f, floor, math.saturate(e / edgeReach));
                return Hashing.Unit(Hashing.Mix(seed, (int)(p.x * 5f), (int)(p.y * 5f))) > keep;
            }
            // (rundown) thicket 0..1: wild scrub grows in dense patches with open ground between (evenly spread bushes read as a park)
            int weedsWanted = math.min(2500, (int)(region.Area / 100f * amount * 1.4f * (1f + 0.4f * thicket)));
            int weeds = LeisureKit.Plant(ctx, occ, region, weedsWanted, ObjectRole.Bush, weedIds, 0.8f, 7f + 5f * thicket, 0.25f * (1f - 0.9f * thicket), salt + 11u,
                p => Edgy(p, 0.05f + 0.25f * amount), 1, 3, weedCategory);
            int trees = 0;
            int treesWanted = math.min(600, (int)(ha * treesPerHa * math.saturate(amount * 1.2f)));
            if (treesWanted > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                trees = LeisureKit.Plant(ctx, occ, region, treesWanted, ObjectRole.Tree, treeIds, 2.2f, 14f, 0.3f, salt + 23u, p => Edgy(p, 0.15f), 0, 1);
            return (weeds, trees);
        }

        // ───────────── boundary ─────────────

        /// <summary>
        /// A fence that rotted in pieces: each run is cut into ~8 m panels and a share
        /// <paramref name="gaps"/> (0..1) of them is missing; gates stay open. Corners keep exact
        /// endpoints so surviving neighbours still join. Runs are reserved as corridors.
        /// </summary>
        public static float BrokenFence(LayerContext ctx, Occupancy occ, LayerRegion region, List<Gate> gates, float gaps, AssetRef asset, uint salt, float gateHalfWidth = 3.5f)
        {
            float kept = 0f; int k = 0;
            foreach (var run in Fences.Runs(region, gates, gateHalfWidth))
            {
                var a = run.c0; var b = run.c1;
                float len = math.distance(a, b);
                int panels = math.max(1, (int)math.round(len / 8f));
                for (int i = 0; i < panels; i++, k++)
                {
                    uint v = Hashing.Mix(ctx.Seed, salt + (uint)k * 977u);
                    // Missing panels come in runs (a fallen stretch), not as a regular dash pattern.
                    float n = ValueNoise.Clumps(math.lerp(a, b, (i + 0.5f) / panels), 18f, Hashing.Mix(ctx.Seed, salt));
                    if (gaps > 0f && math.lerp(Hashing.Unit(v), 1f - n, 0.5f) < gaps) continue;
                    var p0 = i == 0 ? a : math.lerp(a, b, (float)i / panels);
                    var p1 = i == panels - 1 ? b : math.lerp(a, b, (float)(i + 1) / panels);
                    ctx.StraightNet(p0, p1, NetRole.Fence, asset);
                    occ.TakeCorridor(p0, p1, 0.6f, 30);
                    kept += math.distance(p0, p1);
                }
            }
            return kept;
        }

        // ───────────── junk & desire paths ─────────────

        /// <summary>A heap of junk around a centre (spiral, items 0.7 m). Returns the count placed.</summary>
        public static int JunkCluster(LayerContext ctx, Occupancy occ, float2 c, int items, string[] ids, string category, uint salt)
        {
            if (category == null || items <= 0) return 0;
            const float golden = 2.39996323f;
            int n = 0;
            uint s = Hashing.Mix(ctx.Seed, salt);
            for (int i = 0; i < items * 3 && n < items; i++)
            {
                float r = 0.9f * math.sqrt(i + 0.3f);
                var p = c + new float2(math.cos(i * golden + s % 11), math.sin(i * golden + s % 11)) * r;
                uint v = Hashing.Mix(s, (uint)i);
                if (!LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(ids, v), category, 0.7f, v, 0.6f, 0.1f, 2)) continue;
                n++;
            }
            return n;
        }

        /// <summary>A gently bent strip polygon between a and b (worn desire path), or null when it is not simple.</summary>
        public static float2[] Strip(float2 a, float2 b, float halfWidth, float bend)
        {
            var dir = math.normalizesafe(b - a);
            var nrm = new float2(-dir.y, dir.x);
            var mid = math.lerp(a, b, 0.5f) + nrm * bend;
            const int n = 8;
            var line = new float2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                line[i] = u * u * a + 2f * u * t * mid + t * t * b;
            }
            var left = new List<float2>(); var right = new List<float2>();
            for (int i = 0; i <= n; i++)
            {
                var tan = math.normalizesafe(line[math.min(n, i + 1)] - line[math.max(0, i - 1)]);
                var off = new float2(-tan.y, tan.x) * halfWidth;
                left.Add(line[i] + off); right.Add(line[i] - off);
            }
            right.Reverse();
            left.AddRange(right);
            var ring = Poly.EnsureCcw(left.ToArray());
            return Poly.IsSimple(ring) ? ring : null;
        }

        /// <summary>Paints a desire path between two points (inset from the outline) and keeps objects off it.</summary>
        public static bool DesirePath(LayerContext ctx, Occupancy occ, LayerRegion region, float2 a, float2 b, int layer, uint salt) => DesirePath(ctx, occ, region, a, b, layer, salt, null);

        /// <summary>As above; the worn line (gate to gate, bent) is added to <paramref name="lines"/> for the ground design.</summary>
        public static bool DesirePath(LayerContext ctx, Occupancy occ, LayerRegion region, float2 a, float2 b, int layer, uint salt, List<float2[]> lines)
        {
            var d = math.normalizesafe(b - a);
            a += d * 1.5f; b -= d * 1.5f;
            if (math.distance(a, b) < 6f) return false;
            float bend = (Hashing.Unit(Hashing.Mix(ctx.Seed, salt)) - 0.5f) * math.distance(a, b) * 0.25f;
            var strip = Strip(a, b, 0.9f, bend);
            if (strip == null) return false;
            var mid = math.lerp(a, b, 0.5f) + new float2(-d.y, d.x) * bend * 0.5f;
            if (!region.Contains(mid) || region.CrossesBoundary(a, mid, true) || region.CrossesBoundary(mid, b, true)) return false;
            TerrainPaint.Patch(ctx, strip, layer, 0.8f, 0f);
            lines?.Add(new[] { a, math.lerp(a, mid, 0.5f) + new float2(-d.y, d.x) * bend * 0.12f, mid, math.lerp(mid, b, 0.5f) + new float2(-d.y, d.x) * bend * 0.12f, b });
            occ.TakeCorridor(a, mid, 0.9f, 60);
            occ.TakeCorridor(mid, b, 0.9f, 60);
            return true;
        }

        /// <summary>Graffiti decals beside a line (e.g. a wall), on the given side, chance per slot.</summary>
        public static int GraffitiAlong(LayerContext ctx, Occupancy guard, float2 a, float2 b, float2 normal, float chance, uint salt, float spacing = 3f)
        {
            if (chance <= 0f || !AssetAvailability.Has(AssetCategories.DecalGraffiti)) return 0;
            int n = 0;
            float yaw = math.atan2(normal.x, normal.y);
            foreach (var p in LeisureKit.Along(a, b, spacing))
            {
                uint v = Hashing.Mix(ctx.Seed, salt + (uint)(p.x * 13f) * 31u + (uint)(p.y * 7f));
                if (Hashing.Unit(v) > chance) continue;
                if (Decal(ctx, guard, p + normal * 0.9f, yaw, AssetCategories.DecalGraffiti, v)) n++;
            }
            return n;
        }

        /// <summary>The collapse/attention point: the deepest point, near the street-side entrance, or a far corner.</summary>
        public static float2 Focus(LayerRegion region, List<Gate> gates, string mode)
        {
            var deep = region.DeepestPoint(out float ins, null, 16);
            if (mode == "street" && gates.Count > 0)
            {
                var g = gates[0].Position;
                var dir = math.normalizesafe(deep - g);
                var p = g + dir * math.min(ins, 22f);
                return region.Contains(p) ? p : deep;
            }
            if (mode == "corner")
            {
                float best = -1f; float2 far = deep;
                foreach (var v in region.Ring)
                {
                    float m = float.MaxValue;
                    foreach (var g in gates) m = math.min(m, math.distance(v, g.Position));
                    if (gates.Count == 0) m = math.distance(v, deep);
                    if (m > best) { best = m; far = v; }
                }
                var p = math.lerp(far, deep, 0.4f);
                return region.Contains(p) ? p : deep;
            }
            return deep;
        }

        /// <summary>Region outline inset (simple) or the outline itself.</summary>
        public static float2[] Inset(LayerRegion region, float d)
        {
            var r = region.InsetVertices(d);
            return Poly.IsSimple(r) && Poly.SignedArea(r) > 20f ? r : null;
        }
    }
}
