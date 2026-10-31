// Placemaker — Biome mix: building the areas (docs/runbooks/biomes.md).
//
// Once BiomeField has split the natural ground, each biome lays its ground design over its own cells, in this order:
//   ground    one Sculpt over the site: rocky ground lifted into broken heaps and knolls, a little roughness, marsh dipped
//             into shallow hollows; every change fades out across the area's edge and 4-9 m off the paths (they keep their
//             ground), no steeper than the site harmony accepts (28 % change, 34 % over the ground as it is)
//   water     the water biome's areas become ponds (dug, banked where the land rises, a water source), reeds and wet trees on
//             their rims
//   rocks     outcrops (crowns of 4-7 massed boulders, the big ones tilted against each other) on the heaps' tops, loose
//             half-sunk boulders, and small stones that thin toward the edge: rock gives way to scree, then grass
//   planting  trees in groves by the biome's clump scale (a closed wood .. lone trees in a meadow), the odd dead snag, bushes
//             thickening at the rim (the forest edge's scrub), fallen logs in the wood
//   paint     the biome's terrain paint as a soft field over its cells (no hard line: each fades across its edge), a second
//             paint at the heart (earth under the rocks, mud in the marsh)
//   regions   each area as regions "biome:<id>" (holes "biomeHole:<id>"): BiomeGround lays its decals there, the site harmony
//             keeps its smoothing off the shaped ones
// Then the host keeps its planting and furniture off the areas (their outlines are taken in its occupancy), so a park's own
// trees, drifts and benches stay on its lawn and verges.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common.Shore;
using Placemaker.Core.Paint;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common.Biomes
{
    public sealed class BiomeResult
    {
        public BiomeField Field;
        public int Trees, Bushes, Boulders, Stones, Logs, Crowns, Ponds, Pools;
        /// <summary>Natural ground the non-host biomes took (m²): the host plants that much less of its own.</summary>
        public float Taken;
        /// <summary>
        /// (P2-8) The host's own share of the mix (0..1): its own planting on the ground the areas leave (verges, entrances,
        /// clearings) scales with it, so a biome at 100 % has no host trees at all ("full means full").
        /// </summary>
        public float HostShare = 1f;
        /// <summary>Outer outlines of the non-host areas (taken in the host's occupancy).</summary>
        public readonly List<float2[]> Areas = new List<float2[]>();
        /// <summary>The ponds dug.</summary>
        public readonly List<float2[]> Water = new List<float2[]>();
        /// <summary>The water's edges (quick distance to the nearest water).</summary>
        public readonly SegmentIndex WaterEdges = new SegmentIndex(10f);
        /// <summary>Per biome index: its planted beds' cells (mulch), or null.</summary>
        public bool[][] Beds;
        public int Plants;

        internal void AddWater(float2[] ring)
        {
            Water.Add(ring);
            for (int i = 0; i < ring.Length; i++) WaterEdges.Add(ring[i], ring[(i + 1) % ring.Length], 0f);
        }
    }

    public static class BiomeBuilder
    {
        public const string RegionPrefix = "biome:";
        public const string HolePrefix = "biomeHole:";
        public const string BedPrefix = "biomeBed:";
        /// <summary>The most objects the biomes place on one site (thinned evenly past it).</summary>
        public const int MaxObjects = 9000;

        /// <summary>A region tag of a biome that shapes its ground (the site harmony keeps its smoothing off it).</summary>
        public static bool Shaped(string tag)
        {
            if (tag == null || !tag.StartsWith(RegionPrefix, StringComparison.Ordinal)) return false;
            var d = BiomeLibrary.Get(tag.Substring(RegionPrefix.Length));
            return d != null && (d.Lift > 0f || d.Dip > 0f);
        }

        /// <summary>Splits the site and builds every biome's ground (see the file's header). Nothing natural: null.</summary>
        public static BiomeResult Plan(BiomeSite site, List<(BiomeDef biome, float share)> shares, float scale, uint seed, BiomeField prior = null)
        {
            var ctx = site.Ctx;
            if (shares == null || !BiomeMix.AnyNatural(shares)) return null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var field = BiomeField.Build(site, shares, scale, seed, prior);
            var res = new BiomeResult { Field = field, HostShare = 0f };
            foreach (var (b, sh) in shares) if (b.Host) res.HostShare += sh;
            ctx.Plan.Facts["biomeHostShare"] = math.round(res.HostShare * 100f);
            // (P2-8) the biomes' own settings, for the decals (BiomeGround rebuilds the tuned biomes from them)
            foreach (var (b, sh) in shares) if (sh > 0f) BiomeKnobs.WriteFacts(ctx.Plan.Facts, ctx.Settings, BiomeLibrary.Get(b.Id) ?? b);
            if (field.NaturalCells == 0) return res;
            int wi = -1;
            for (int b = 0; b < field.Biomes.Count; b++) if (field.Biomes[b].Water) wi = b;
            if (wi >= 0) FillHoles(field, wi);
            ctx.Check();

            // outlines of every biome (regions, the host's occupancy, the ponds)
            var outers = new List<float2[]>[field.Biomes.Count];
            var holes = new List<float2[]>[field.Biomes.Count];
            for (int b = 0; b < field.Biomes.Count; b++)
            {
                outers[b] = new List<float2[]>(); holes[b] = new List<float2[]>();
                field.Outlines(b, outers[b], holes[b]);
                for (int i = 0; i < outers[b].Count; i++) outers[b][i] = Inside(site.Region, outers[b][i]);
                for (int i = 0; i < holes[b].Count; i++) holes[b][i] = Inside(site.Region, holes[b][i]);
            }
            long tField = clock.ElapsedMilliseconds;

            if (site.Shape) Sculpt(site, field, seed);
            long tShape = clock.ElapsedMilliseconds;
            if (wi >= 0) Ponds(site, field, wi, res, seed);
            for (int b = 0; b < field.Biomes.Count; b++) if (field.Biomes[b].Pools > 0f && field.Got[b] > 0f) Pools(site, field, b, res, seed);
            ctx.Check();

            // object budget: every biome's wanted count, thinned evenly when the site would pass MaxObjects
            float naturalArea = field.NaturalCells * field.CellArea;
            float wanted = 0f;
            for (int b = 0; b < field.Biomes.Count; b++)
            {
                var d = field.Biomes[b];
                if (d.Host) continue;
                float ha = field.Got[b] * naturalArea / 10000f;
                wanted += ha * (d.TreesPerHa * site.TreeAmount + d.BushesPerHa * math.max(1f, d.RimScrub * 0.7f) * site.BushAmount + d.CrownsPerHa * 7f + d.BouldersPerHa * 3.5f + d.StonesPerHa + d.LogsPerHa);
                foreach (var pl in d.Plants) wanted += ha * pl.PerHa * site.BushAmount * (pl.Where == BiomeWhere.Water ? 0.3f : pl.Where == BiomeWhere.Beds ? d.Beds : 1f);
            }
            float thin = wanted > MaxObjects ? MaxObjects / wanted : 1f;
            res.Beds = new bool[field.Biomes.Count][];
            for (int b = 0; b < field.Biomes.Count; b++) if (field.Biomes[b].Beds > 0f && field.Got[b] > 0f) res.Beds[b] = BedCells(field, b, seed);
            for (int b = 0; b < field.Biomes.Count; b++)
            {
                var d = field.Biomes[b];
                if (d.Host || field.Got[b] <= 0f) continue;
                ctx.Check();
                var bb = new Builder(site, field, b, seed, thin, res);
                // trees first (a pine grows out of the rock field), then the rocks round them, then the bushes in the gaps
                if (d.RowPitch > 0f) bb.Rows(); else bb.Planting(trees: true, bushes: false);
                bb.Rocks();
                bb.Planting(trees: false, bushes: true);
                bb.PlantLayers();
                if (d.LogsPerHa > 0f) bb.Logs();
            }
            long tObjects = clock.ElapsedMilliseconds;

            Paint(site, field, seed, res);
            // the beds as regions of their own (their decals fill them)
            for (int b = 0; b < field.Biomes.Count; b++)
            {
                if (res.Beds[b] == null) continue;
                var bedRings = new List<float2[]>();
                var bm = res.Beds[b];
                field.Outlines(k => bm[k], bedRings, new List<float2[]>(), 3, 0.4f);
                int n = 0;
                foreach (var r0 in bedRings)
                {
                    var r = Simple(Inside(site.Region, r0));
                    ctx.AddRegion(r, BedPrefix + field.Biomes[b].Id + ":" + n++, BedPrefix + field.Biomes[b].Id);
                    // a bed is a real material: a crisp-edged mulch surface (the modded mulch, else the vanilla forest floor) under its plants
                    if (r.Length >= 3 && Poly.IsSimple(r) && math.abs(Poly.SignedArea(r)) > 12f) ctx.Surface(r, SurfaceRole.Decorative, new AssetRef(MulchSurfaces[0], MulchSurfaces), AssetCategories.SurfaceNatural);
                }
            }
            for (int b = 0; b < field.Biomes.Count; b++)
            {
                var d = field.Biomes[b];
                int n = 0;
                foreach (var r in outers[b]) ctx.AddRegion(r, RegionPrefix + d.Id + ":" + n++, RegionPrefix + d.Id);
                n = 0;
                foreach (var r in holes[b]) ctx.AddRegion(r, HolePrefix + d.Id + ":" + n++, HolePrefix + d.Id);
                if (d.Host) continue;
                foreach (var r in outers[b])
                {
                    // the host keeps off (a coarse outline: its planting checks every polygon it holds)
                    var coarse = Coarse(r, math.max(1.2f, field.Cell * 0.5f));
                    if (coarse.Length >= 3) { site.Occ?.TakePolygon(coarse); res.Areas.Add(coarse); }
                }
                res.Taken += field.Got[b] * naturalArea;
            }

            // facts: what each biome got (% of the natural ground), what was placed
            foreach (var key in new List<string>(ctx.Plan.Facts.Keys)) if (key.StartsWith(BiomeMix.FactPrefix, StringComparison.Ordinal)) ctx.Plan.Facts.Remove(key);
            for (int b = 0; b < field.Biomes.Count; b++) ctx.Plan.Facts[BiomeMix.FactPrefix + field.Biomes[b].Id] = math.round(field.Got[b] * 1000f) / 10f;
            ctx.Plan.Facts["biomeNaturalArea"] = math.round(naturalArea);
            ctx.Plan.Facts["biomeTrees"] = res.Trees;
            ctx.Plan.Facts["biomeBushes"] = res.Bushes;
            ctx.Plan.Facts["biomePlants"] = res.Plants;
            ctx.Plan.Facts["biomeBoulders"] = res.Boulders;
            ctx.Plan.Facts["biomeCrowns"] = res.Crowns;
            ctx.Plan.Facts["biomeStones"] = res.Stones;
            ctx.Plan.Facts["biomeLogs"] = res.Logs;
            if (res.Ponds > 0) ctx.Plan.Facts["biomePonds"] = res.Ponds;
            if (res.Pools > 0) ctx.Plan.Facts["biomePools"] = res.Pools;
            if (thin < 1f) ctx.Plan.Facts["biomeThinned"] = math.round(thin * 100f) / 100f;
            ctx.Plan.Facts["biomeMs"] = clock.ElapsedMilliseconds;
            ctx.Plan.Facts["biomeMsField"] = tField;
            ctx.Plan.Facts["biomeMsShape"] = tShape - tField;
            ctx.Plan.Facts["biomeMsObjects"] = tObjects - tShape;
            return res;
        }

        /// <summary>The bed under a pond or a marsh pool: dark forest floor (the water role's default pool starts with sand: beige basins until they fill).</summary>
        private static readonly string[] MulchSurfaces = { "EAI - Surface Pack Ground Mulch Surface", "Forestry Surface 01" };

        private static AssetRef MudBed => new AssetRef("Forestry Surface 01", new[] { "Forestry Surface 01" });

        /// <summary>A ring that crosses itself cleaned until it does not (coarser each try); null when it never does.</summary>
        private static float2[] Simple(float2[] r)
        {
            if (r == null || r.Length < 3) return r;
            if (Poly.IsSimple(r)) return r;
            for (float eps = 0.6f; eps <= 3f; eps *= 1.6f)
            {
                var c = Poly.Clean(r, eps);
                if (c.Length >= 3 && Poly.IsSimple(c)) return c;
            }
            return r;
        }

        /// <summary>An outline traced along the cells kept inside the site: points past the outline go onto it, 0.3 m in.</summary>
        internal static float2[] Inside(LayerRegion region, float2[] ring)
        {
            var o = (float2[])ring.Clone();
            for (int i = 0; i < o.Length; i++)
            {
                if (region.ContainsDisc(o[i], 0.3f)) continue;
                var c = region.ClosestOnBoundary(o[i], out int seg, out _);
                o[i] = c + region.InwardNormal(seg) * 0.3f;
            }
            return o;
        }

        /// <summary>
        /// A mulch area's beds: the share BiomeDef.Beds of its cells where a 9 m noise is highest, 2 m or more inside it (each bed
        /// an organic shape, lawn between them).
        /// </summary>
        private static bool[] BedCells(BiomeField f, int b, uint seed)
        {
            var d = f.Biomes[b];
            uint s = Hashing.Mix(seed, Seeds.Hash("beds:" + d.Id));
            if (d.BedRadius > 0f) return IslandBeds(f, b, s);
            var v = new float[f.Count];
            var vals = new List<float>();
            for (int k = 0; k < f.Count; k++)
            {
                if (f.Label[k] != b || f.Interior[k] < 2f) { v[k] = -1f; continue; }
                v[k] = BiomeField.Fbm(f.Pos(k) / 9f, s);
                vals.Add(v[k]);
            }
            var bed = new bool[f.Count];
            if (vals.Count == 0) return bed;
            vals.Sort();
            float cut = vals[math.clamp((int)((1f - d.Beds) * vals.Count), 0, vals.Count - 1)];
            for (int k = 0; k < f.Count; k++) bed[k] = v[k] >= cut;
            return bed;
        }

        /// <summary>
        /// Island beds (P2-4): bed centres on a jittered grid spaced so the beds take about BiomeDef.Beds of the area, each a disc
        /// of BedRadius ±35 % with a wobbling edge, 2.5 m or more inside the area; a mown gap of 2.5 m or more between beds.
        /// </summary>
        private static bool[] IslandBeds(BiomeField f, int b, uint s)
        {
            var d = f.Biomes[b];
            var bed = new bool[f.Count];
            float r0 = d.BedRadius, spacing = math.max(r0 * 2f + 3f, r0 * math.sqrt(math.PI / math.max(0.05f, d.Beds)));
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            for (int k = 0; k < f.Count; k++)
                if (f.Label[k] == b) { var p = f.Pos(k); lo = math.min(lo, p); hi = math.max(hi, p); }
            if (lo.x > hi.x) return bed;
            var beds = new List<float3>();   // x, z, radius
            int gi = 0;
            for (float z = lo.y; z <= hi.y + spacing; z += spacing * 0.87f, gi++)
                for (float x = lo.x + ((gi & 1) != 0 ? spacing * 0.5f : 0f); x <= hi.x + spacing; x += spacing)
                {
                    uint h = Hashing.Mix(s, (int)math.floor(x * 7f), (int)math.floor(z * 7f));
                    var c = new float2(x, z) + (new float2(Hashing.Unit(h), Hashing.Unit(Hashing.Mix(h, 1u))) - 0.5f) * spacing * 0.4f;
                    float r = r0 * math.lerp(0.65f, 1.35f, Hashing.Unit(Hashing.Mix(h, 2u)));
                    // the bed shrinks to the room it has: its centre at least r + 2.5 m inside the area
                    float deep = f.InteriorAt(c);
                    if (f.LabelAt(c) != b) continue;
                    r = math.min(r, deep - 2.5f);
                    if (r < r0 * 0.5f) continue;
                    beds.Add(new float3(c, r));
                }
            for (int k = 0; k < f.Count; k++)
            {
                if (f.Label[k] != b || f.Interior[k] < 2f) continue;
                var p = f.Pos(k);
                foreach (var c in beds)
                {
                    float wob = 1f + 0.22f * BiomeField.Fbm(p / 3.5f, s);
                    if (math.distance(p, c.xy) <= c.z * wob) { bed[k] = true; break; }
                }
            }
            return bed;
        }

        /// <summary>Cells of other biomes the water encloses become water (an island in a pond would be dug out anyway).</summary>
        private static void FillHoles(BiomeField f, int b)
        {
            int n = f.Count;
            var reach = new bool[n];
            var stack = new Stack<int>();
            for (int i = 0; i < f.Nx; i++) { Seed(i); Seed((f.Ny - 1) * f.Nx + i); }
            for (int j = 0; j < f.Ny; j++) { Seed(j * f.Nx); Seed(j * f.Nx + f.Nx - 1); }
            void Seed(int k) { if (!reach[k] && f.Label[k] != b) { reach[k] = true; stack.Push(k); } }
            while (stack.Count > 0)
            {
                int k = stack.Pop(); int i = k % f.Nx, j = k / f.Nx;
                if (i > 0) Seed(k - 1); if (i < f.Nx - 1) Seed(k + 1); if (j > 0) Seed(k - f.Nx); if (j < f.Ny - 1) Seed(k + f.Nx);
            }
            for (int k = 0; k < n; k++) if (!reach[k] && f.Label[k] != b && f.Label[k] >= 0) f.Label[k] = (sbyte)b;
        }

        private static float2[] Coarse(float2[] ring, float step)
        {
            var o = new List<float2>();
            float acc = step;
            for (int i = 0; i < ring.Length; i++)
            {
                if (i > 0) acc += math.distance(ring[i], ring[i - 1]);
                if (acc >= step) { o.Add(ring[i]); acc = 0f; }
            }
            return o.Count >= 3 ? o.ToArray() : ring;
        }

        // ───────────────────────── the ground ─────────────────────────

        /// <summary>Broken heaps of the rocky ground (0..1): ridged noise, peaks where the outcrops stand.</summary>
        internal static float Heap(float2 p, uint seed)
        {
            // knolls 15-30 m across with saddles and hollows between (value noise stretched: it sits near 0.5)
            float a = BiomeField.Fbm(p / 24f, Hashing.Mix(seed, 0x4EA9u));
            float k = BiomeField.Fbm(p / 9f, Hashing.Mix(seed, 0x4EAAu));
            float v = (a - 0.5f) * 2.6f + (k - 0.5f) * 0.9f + 0.5f;
            return BiomeField.Smooth01((v - 0.42f) / 0.45f);
        }

        /// <summary>Where the loose rock gathers (0..1): boulder fields with open, grassy ground between them.</summary>
        internal static float Clump(float2 p, uint seed) =>
            BiomeField.Smooth01((BiomeField.Fbm(p / 17f, Hashing.Mix(seed, 0xC1u)) - 0.44f) / 0.14f);

        private static void Sculpt(BiomeSite site, BiomeField f, uint seed)
        {
            var ctx = site.Ctx;
            bool any = false;
            foreach (var d in f.Biomes) if (d.Lift > 0f || d.Break > 0f || d.Dip > 0f) any = true;
            if (!any) return;
            var region = site.Region;
            var paths = site.Paths;
            float cell = math.max(3f, f.Cell * 0.75f);
            var sc = new Sculpt(ctx.H, region.Min, region.Max, cell, q =>
            {
                if (!region.ContainsDisc(q, 4f + cell)) return false;
                if (paths != null && paths.Count > 0 && paths.Clearance(q, 3f) < 2.5f) return false;
                if (ctx.Input?.Reserved != null && ctx.Input.Reserved.Blocks(q, 2f)) return false;
                return f.LabelAt(q) >= 0 || f.InteriorAt(q) > 0f;
            });
            sc.Shape((q, b, t) =>
            {
                float off = 0f;
                for (int i = 0; i < f.Biomes.Count; i++)
                {
                    var d = f.Biomes[i];
                    if (d.Lift <= 0f && d.Break <= 0f && d.Dip <= 0f) continue;
                    float w = f.WeightAt(i, q);
                    if (w <= 0.001f) continue;
                    uint bs = Hashing.Mix(seed, Seeds.Hash("shape:" + d.Id));
                    // the heart lifts most: heaps grow from the edge inward (12 m to full)
                    float deep = BiomeField.Smooth01(f.InteriorAt(q) / 12f) * (f.LabelAt(q) == i ? 1f : 0f);
                    // a low swell over the whole area, broken knolls rising from it toward the heart (rock never reads as a plateau)
                    if (d.Lift > 0f) off += d.Lift * w * (0.12f + 0.88f * deep * Heap(q, bs));
                    if (d.Break > 0f) off += d.Break * w * (BiomeField.Fbm(q / 4.5f, Hashing.Mix(bs, 7u)) - 0.5f) * 2f;
                    if (d.Dip > 0f) off -= d.Dip * w * (0.55f + 0.45f * BiomeField.Fbm(q / 9f, Hashing.Mix(bs, 9u)));
                }
                // the paths keep their level band and shoulders: the change dies out from 4 m to 9 m off every path
                if (off != 0f && paths != null && paths.Count > 0) off *= Common.Sculpt.Smooth01((paths.Clearance(q, 10f) - 4f) / 5f);
                return b + off;
            });
            sc.LimitGrade(0.28f, 14);
            sc.LimitGrade(0.34f, 14, absolute: true);
            var res = sc.Emit(ctx, null);
            if (res.Cells > 0)
            {
                ctx.Plan.Facts["biomeTerrainCells"] = res.Cells;
                ctx.Plan.Facts["biomeTerrainRaise"] = math.round(res.MaxOffset * 10f) / 10f;
                ctx.Plan.Facts["biomeTerrainLower"] = math.round(-res.MinOffset * 10f) / 10f;
            }
        }

        // ───────────────────────── water ─────────────────────────

        private static void Ponds(BiomeSite site, BiomeField f, int wi, BiomeResult res, uint seed)
        {
            var ctx = site.Ctx;
            int made = 0;
            // the water itself: the area's cells 1.5 m or more inside it (its rim stays dry ground for the reeds), biggest first
            var rings = new List<float2[]>();
            f.Outlines(k => f.Label[k] == wi && f.Interior[k] >= 1.5f + f.Cell * 0.6f, rings, new List<float2[]>(), 3, 0.4f);   // a cell further in: the outline stays inside the site (pulling it in made big ponds cross themselves)
            rings.Sort((a, b) => Poly.SignedArea(b).CompareTo(Poly.SignedArea(a)));
            foreach (var r0 in rings)
            {
                var pond = Simple(r0);
                if (made >= 8) break;
                if (pond.Length < 3 || !Poly.IsSimple(pond) || Poly.SignedArea(pond) < 120f) continue;
                float shore = float.MaxValue;
                foreach (var p in pond) shore = math.min(shore, ctx.H(p));
                if (!math.isfinite(shore)) continue;
                float level = shore - 0.4f;
                float depth = math.clamp(math.sqrt(Poly.SignedArea(pond)) * 0.06f, 1.2f, 2.4f);
                var anchor = Poly.Centroid(pond);
                if (!Poly.Contains(pond, anchor)) anchor = LayerRegion.From(pond)?.DeepestPoint(out _, null, 12) ?? anchor;
                ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = Poly.To3D(pond, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - depth,
                    Permanent = true, WaterLevel = level, WaterAnchor = new float3(anchor.x, level, anchor.y),
                });
                BasinBank.Add(ctx, pond, level);
                ctx.Surface(pond, SurfaceRole.Water, MudBed);
                site.Occ?.TakePolygon(pond);
                res.AddWater(pond);
                made++;
            }
            if (made > 0)
            {
                res.Ponds = made;
                if (!ctx.Plan.Issues.Exists(i => i.Key == "Placemaker.Park.Issue.LakePermanent")) ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.LakePermanent");
            }
        }

        /// <summary>
        /// Shallow pools of standing water in a marsh's heart (the share BiomeDef.Pools of its cells, where its noise is highest,
        /// 3.5 m or more inside it): dug 1.3 m below their lowest bank (deep enough to read as dark water), water just under it (shallower pools stayed dry in the game).
        /// </summary>
        private static void Pools(BiomeSite site, BiomeField f, int b, BiomeResult res, uint seed)
        {
            var ctx = site.Ctx;
            var d = f.Biomes[b];
            uint ps = Hashing.Mix(seed, Seeds.Hash("pools:" + d.Id));
            var noise = new float[f.Count];
            var vals = new List<float>();
            int cells = 0;
            for (int k = 0; k < f.Count; k++)
            {
                if (f.Label[k] != b) continue;
                cells++;
                if (f.Interior[k] < 3.5f) continue;
                noise[k] = BiomeField.Fbm(f.Pos(k) / 11f, ps) + f.Interior[k] * 0.01f;
                vals.Add(noise[k]);
            }
            if (vals.Count == 0) return;
            vals.Sort();
            int want = math.min(vals.Count - 1, (int)(cells * d.Pools));
            float cut = vals[math.max(0, vals.Count - 1 - want)];
            var rings = new List<float2[]>();
            f.Outlines(k => f.Label[k] == b && f.Interior[k] >= 3.5f + f.Cell * 0.6f && noise[k] >= cut, rings, new List<float2[]>(), 2, 0.6f);
            int made = 0;
            foreach (var r0 in rings)
            {
                var pool = Simple(r0);
                if (made >= 24) break;
                if (pool.Length < 3 || !Poly.IsSimple(pool) || Poly.SignedArea(pool) < 40f) continue;
                float shore = float.MaxValue;
                foreach (var p in pool) shore = math.min(shore, ctx.H(p));
                if (!math.isfinite(shore)) continue;
                float level = shore - 0.08f;
                var anchor = LayerRegion.From(pool)?.DeepestPoint(out _, null, 10) ?? Poly.Centroid(pool);
                ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = Poly.To3D(pool, ctx.H), Kind = TerraformKind.Lower, TargetHeight = level - 1.3f,
                    Permanent = true, WaterLevel = level, WaterAnchor = new float3(anchor.x, level, anchor.y),
                });
                ctx.Surface(pool, SurfaceRole.Water, MudBed);
                site.Occ?.TakePolygon(pool);
                res.AddWater(pool);
                made++;
            }
            if (made > 0)
            {
                res.Pools += made;
                if (!ctx.Plan.Issues.Exists(i => i.Key == "Placemaker.Park.Issue.LakePermanent")) ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.LakePermanent");
            }
        }

        // ───────────────────────── paint ─────────────────────────

        private static void Paint(BiomeSite site, BiomeField f, uint seed, BiomeResult res)
        {
            var ctx = site.Ctx;
            var kit = new PaintKit(ctx.Plan.Paint, ctx.H, Hashing.Mix(seed, 0x9A17u)) { Outline = site.Region.Ring };
            var origin = f.Origin - f.Cell * 0.5f;
            bool any = false;
            for (int b = 0; b < f.Biomes.Count; b++)
            {
                var d = f.Biomes[b];
                if (d.Host || f.Got[b] <= 0f) continue;
                if (d.PaintLayer > 0 && d.PaintStrength > 0f)
                {
                    var v = new float[f.Count];
                    var w = f.Weight[b];
                    var bm = res.Beds?[b];
                    for (int k = 0; k < f.Count; k++) v[k] = bm != null ? (bm[k] ? 1f : 0f) : w[k];
                    var shape = PaintShape.Grid(origin, f.Cell, f.Nx, f.Ny, v).Mul(PaintShape.Noise(10f, 0.9f, 0.45f, Hashing.Mix(seed, (uint)b * 7u + 1u)));
                    if (kit.Shape(shape, d.PaintLayer, d.PaintStrength) != null) any = true;
                }
                if (d.CorePaint > 0 && d.CoreStrength > 0f)
                {
                    var v = new float[f.Count];
                    for (int k = 0; k < f.Count; k++) v[k] = f.Label[k] == b ? BiomeField.Smooth01((f.Interior[k] - 2f) / 9f) : 0f;
                    var shape = PaintShape.Grid(origin, f.Cell, f.Nx, f.Ny, v).Mul(PaintShape.Noise(6f, 0.7f, 0.35f, Hashing.Mix(seed, (uint)b * 7u + 2u)));
                    if (kit.Shape(shape, d.CorePaint, d.CoreStrength, null, d.CorePaint == TerrainMaterials.Dirt) != null) any = true;
                }
            }
            if (any) TerrainPaint.Warn(ctx);
        }

        // ───────────────────────── objects ─────────────────────────

        private sealed class Builder
        {
            private readonly BiomeSite _s;
            private readonly LayerContext _ctx;
            private readonly BiomeField _f;
            private readonly int _b;
            private readonly BiomeDef _d;
            private readonly uint _seed, _shapeSeed;
            private readonly float _ha, _thin;
            private readonly BiomeResult _res;
            private readonly LayerRegion _box;
            private readonly Occupancy _occ;
            private int _n;

            public Builder(BiomeSite site, BiomeField f, int b, uint seed, float thin, BiomeResult res)
            {
                _s = site; _ctx = site.Ctx; _f = f; _b = b; _d = f.Biomes[b]; _thin = thin; _res = res;
                _seed = Hashing.Mix(seed, Seeds.Hash("build:" + _d.Id));
                _shapeSeed = Hashing.Mix(seed, Seeds.Hash("shape:" + _d.Id));
                _ha = f.Got[b] * f.NaturalCells * f.CellArea / 10000f;
                _occ = site.Occ ?? new Occupancy(site.Region, site.Ctx?.Input?.Reserved, site.Ctx?.Site);
                // the biome's cells' box (the candidates of its planting cover only that)
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                for (int k = 0; k < f.Count; k++) if (f.Label[k] == b) { var p = f.Pos(k); lo = math.min(lo, p); hi = math.max(hi, p); }
                lo -= f.Cell * 1.5f; hi += f.Cell * 1.5f;
                if (_d.Water) { lo -= 8f; hi += 8f; }
                lo = math.max(lo, site.Region.Min); hi = math.min(hi, site.Region.Max);
                _box = hi.x > lo.x + 2f && hi.y > lo.y + 2f ? LayerRegion.From(new[] { lo, new float2(hi.x, lo.y), hi, new float2(lo.x, hi.y) }) : site.Region;
            }

            private uint V(int a) => Hashing.Mix(_seed, a, ++_n);
            private float U(int a) => Hashing.Unit(V(a));
            private int Count(float perHa) => (int)math.round(perHa * _ha * _thin);
            private bool Mine(float2 p) => _f.LabelAt(p) == _b;
            private float Deep(float2 p) => Mine(p) ? _f.InteriorAt(p) : 0f;
            private bool Reserved(float2 q, float r) => _ctx.Input?.Reserved != null && _ctx.Input.Reserved.Blocks(q, r);
            private bool PathClear(float2 q, float r) => _s.Paths == null || _s.Paths.Count == 0 || !_s.Paths.Overlaps(q, r, 0.6f);
            private bool InWater(float2 q, float r)
            {
                if (_res.Water.Count == 0) return false;
                if (_res.WaterEdges.Clearance(q, r + 0.01f) < r) return true;
                foreach (var w in _res.Water) if (Poly.Contains(w, q)) return true;
                return false;
            }
            private static float ParkDist(float2[] ring, float2 p)
            {
                float best = float.MaxValue;
                for (int i = 0; i < ring.Length; i++) best = math.min(best, SegmentIndex.DistSq(p, ring[i], ring[(i + 1) % ring.Length]));
                return math.sqrt(best);
            }

            private float2 Down(float2 c)
            {
                float e = 2f;
                var g = new float2(_ctx.G(c + new float2(e, 0)) - _ctx.G(c - new float2(e, 0)), _ctx.G(c + new float2(0, e)) - _ctx.G(c - new float2(0, e)));
                return math.lengthsq(g) < 1e-6f ? new float2(0, -1) : -math.normalize(g);
            }

            // ── rocks ──

            public void Rocks()
            {
                if (_d.CrownsPerHa <= 0f && _d.BouldersPerHa <= 0f && _d.StonesPerHa <= 0f) return;
                if (!AssetAvailability.Has(AssetCategories.Rock)) return;
                // crowns: on the heaps' tops, deep inside, 11 m+ apart
                int crowns = Count(_d.CrownsPerHa);
                if (crowns > 0)
                {
                    var cands = Scatter.Candidates(_box, 4f, V(1), 20000, _ctx.Cancel, true);
                    var picked = new List<float2>();
                    var scored = new List<(float s, float2 p)>();
                    foreach (var c in cands)
                    {
                        float deep = Deep(c);
                        if (deep < 3f) continue;
                        float s = BiomeField.Smooth01((deep - 3f) / 10f) * (0.35f + 0.65f * Heap(c, _shapeSeed)) + 0.25f * Hashing.Unit(Hashing.Mix(_seed, (int)(c.x * 3f), (int)(c.y * 3f)));
                        scored.Add((s, c));
                    }
                    scored.Sort((a, b) => b.s.CompareTo(a.s));
                    foreach (var (_, c) in scored)
                    {
                        if (picked.Count >= crowns) break;
                        bool far = true;
                        foreach (var o in picked) if (math.distancesq(o, c) < 121f) { far = false; break; }
                        if (!far || !PathClear(c, 5f) || Reserved(c, 4f) || !_occ.Free(c, 2.5f, 3f, 0.3f)) continue;
                        if (Crown(c)) picked.Add(c);
                    }
                }
                // loose boulders: thinning toward the edge
                int boulders = Count(_d.BouldersPerHa);
                if (boulders > 0)
                {
                    int made = 0;
                    foreach (var c in Scatter.Candidates(_box, 3.2f, V(2), 30000, _ctx.Cancel, true))
                    {
                        if (made >= boulders) break;
                        float deep = Deep(c);
                        if (deep <= 0f) continue;
                        // boulder fields, not an even spread: thick where the rock gathers and on the knolls, rare between
                        float gather = math.max(Clump(c, _shapeSeed), Heap(c, _shapeSeed) * 0.8f);
                        float chance = (0.08f + 0.92f * gather) * (0.2f + 0.8f * BiomeField.Smooth01(deep / 8f));
                        if (U(3) > chance) continue;
                        float sc = _d.RockSize * math.lerp(0.45f, 1.25f, U(4) * U(4) + 0.2f * BiomeField.Smooth01(deep / 15f));
                        if (!PathClear(c, 2.4f * sc * 0.6f + 0.5f) || Reserved(c, 2f) || InWater(c, 1.5f) || !_occ.Free(c, 2.4f * sc * 0.55f, 1.5f, 0.2f)) continue;
                        Boulder(c, sc, math.lerp(0.3f, 0.5f, U(5)), 0.5f, true);
                        made++;
                    }
                }
                // small stones: scree giving way to grass at the edge (a few spill just outside it)
                int stones = Count(_d.StonesPerHa);
                if (stones > 0)
                {
                    int made = 0;
                    foreach (var c in Scatter.Candidates(_box, 1.6f, V(6), 60000, _ctx.Cancel, true))
                    {
                        if (made >= stones) break;
                        float deep = Deep(c);
                        float w = _f.WeightAt(_b, c);
                        if (w < 0.25f) continue;
                        float gather = math.max(Clump(c, _shapeSeed), Heap(c, _shapeSeed) * 0.7f);
                        float chance = (deep > 0f ? 0.35f + 0.65f * BiomeField.Smooth01(deep / 7f) : 0.3f * w) * (0.2f + 0.8f * gather);
                        if (U(7) > chance) continue;
                        if (!PathClear(c, 0.6f) || Reserved(c, 0.6f) || InWater(c, 0.5f) || !_occ.Free(c, 0.45f, 0.8f, 0.1f)) continue;
                        Stone(c, math.lerp(0.55f, 1.6f, U(8) * U(8)) * math.lerp(0.8f, 1.2f, w));
                        made++;
                    }
                }
            }


            /// <summary>An outcrop: one big boulder (or two) with others massed against it, tilted inward, sunk, broken stones round it.</summary>
            private bool Crown(float2 c)
            {
                int n = 5 + (int)(U(10) * 4.99f);
                float big = _d.RockSize * math.lerp(1.6f, 2.5f, U(11));
                if (!Boulder(c, big, math.lerp(0.32f, 0.45f, U(12)), 0.35f, false)) return false;
                _res.Crowns++;
                float a0 = U(13) * 6.283f;
                for (int i = 1; i < n; i++)
                {
                    float a = a0 + i * (6.283f / n) + (U(14) - 0.5f) * 0.9f;
                    float r = big * math.lerp(1.0f, 2.0f, U(15)) * (i % 3 == 0 ? 1.5f : 1f);
                    var q = c + new float2(math.cos(a), math.sin(a)) * r;
                    float sc = big * math.lerp(0.45f, 0.85f, U(16));
                    if (!PathClear(q, 2.4f * sc * 0.5f) || Reserved(q, 1.5f) || InWater(q, 1f)) continue;
                    var o = Boulder(q, sc, math.lerp(0.3f, 0.45f, U(17)), 0f, false);
                    if (!o) continue;
                    // leaning into the crown
                    var last = _ctx.Plan.Objects[_lastBoulder];
                    var to = math.normalizesafe(c - q);
                    float lean = math.lerp(0.12f, 0.32f, U(18));
                    float yaw = last.Yaw;
                    var fwd = new float2(math.sin(yaw), math.cos(yaw));
                    var right = new float2(fwd.y, -fwd.x);
                    last.Pitch = math.dot(to, fwd) * lean;
                    last.Roll = -math.dot(to, right) * lean;
                }
                _occ.Take(c, big * 2.4f, 5);
                return true;
            }

            private int _lastBoulder = -1;

            /// <summary>A boulder coming up through the ground: scaled, turned, tilted and sunk by a share of its height, broken pieces at its foot.</summary>
            private bool Boulder(float2 q, float scale, float sink, float tilt, bool check)
            {
                float r = 2.4f * scale;
                if (check && !_occ.Free(q, r * 0.5f, 1f, 0.1f)) return false;
                if (!_s.Region.ContainsDisc(q, r * 0.5f + 1f)) return false;
                uint v = V(20);
                var o = _ctx.Object(q, Hashing.Unit(v) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(NatureKit.Boulders, v), v, -1, AssetCategories.Rock);
                o.Scale = scale;
                o.Elevation = -sink * 1.6f * scale;   // the game's negative elevation: buried, colliding with nothing
                o.Pitch = (U(21) - 0.5f) * tilt; o.Roll = (U(22) - 0.5f) * tilt;
                _lastBoulder = _ctx.Plan.Objects.Count - 1;
                _occ.Take(q, r * 0.5f, 5);
                _res.Boulders++;
                // a boulder never sits alone on the turf: broken pieces at its foot, more downhill
                var down = Down(q);
                int bits = 1 + (int)(U(23) * 2.99f);
                for (int i = 0; i < bits; i++)
                {
                    float a = math.atan2(down.y, down.x) + (U(24) - 0.5f) * 2.6f;
                    float d = r * math.lerp(0.55f, 1.05f, U(25));
                    var p = q + new float2(math.cos(a), math.sin(a)) * d;
                    if (!PathClear(p, 0.6f) || Reserved(p, 0.6f) || InWater(p, 0.4f) || !_s.Region.ContainsDisc(p, 0.8f)) continue;
                    Stone(p, math.lerp(0.7f, 1.8f, U(26)) * math.saturate(scale + 0.3f));
                }
                return true;
            }

            private void Stone(float2 q, float scale)
            {
                uint v = V(30);
                var o = _ctx.Object(q, Hashing.Unit(v) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(NatureKit.Stones, v), v, -1, AssetCategories.Rock);
                o.Scale = scale;
                o.Elevation = -0.12f * scale;
                o.Pitch = (U(31) - 0.5f) * 0.5f; o.Roll = (U(32) - 0.5f) * 0.5f;
                _occ.Take(q, 0.45f * scale, 5);
                _res.Stones++;
            }

            // ── planting ──

            /// <summary>The row lines of a planted biome (orchard): axis, a point on row 0, pitch. Shared with BiomeGround through the plan's facts.</summary>
            public static void RowFrame(BiomeSite s, BiomeDef d, out float2 axis, out float2 origin)
            {
                axis = math.lengthsq(s.RowAxis) > 1e-6f ? math.normalize(s.RowAxis) : float2.zero;
                if (math.lengthsq(axis) < 1e-6f) { s.Region.PrincipalAxes(out _, out var major, out _, out _); axis = math.normalizesafe(major, new float2(1, 0)); }
                origin = s.Region.Centroid;
            }

            /// <summary>Trees in straight rows (RowPitch apart, RowSpacing along), only where the area is, a few gaps, off the paths.</summary>
            public void Rows()
            {
                var treeIds = _s.Trees != null && _s.Trees.Length > 0 ? _s.Trees : _d.Trees;
                if (treeIds.Length == 0 || !AssetAvailability.Has(AssetCategories.Tree) && _s.Trees.Length == 0) return;
                RowFrame(_s, _d, out var axis, out var origin);
                var perp = new float2(-axis.y, axis.x);
                _ctx.Plan.Facts["biomeRows." + _d.Id + ".ax"] = axis.x; _ctx.Plan.Facts["biomeRows." + _d.Id + ".ay"] = axis.y;
                _ctx.Plan.Facts["biomeRows." + _d.Id + ".ox"] = origin.x; _ctx.Plan.Facts["biomeRows." + _d.Id + ".oy"] = origin.y;
                float lo = float.MaxValue, hi = float.MinValue, alo = float.MaxValue, ahi = float.MinValue;
                foreach (var q in _box.Ring) { var dq = q - origin; lo = math.min(lo, math.dot(dq, perp)); hi = math.max(hi, math.dot(dq, perp)); alo = math.min(alo, math.dot(dq, axis)); ahi = math.max(ahi, math.dot(dq, axis)); }
                float pitch = _d.RowPitch, step = math.max(2f, _d.RowSpacing);
                int wanted = Count(_d.TreesPerHa * _s.TreeAmount);
                int placed = 0;
                for (float t = math.ceil(lo / pitch) * pitch; t <= hi && placed < wanted; t += pitch)
                {
                    // rows start on a whole step from the row frame, so neighbouring rows line up in both directions
                    for (float a = math.ceil(alo / step) * step; a <= ahi && placed < wanted; a += step)
                    {
                        var p = origin + perp * t + axis * a;
                        if (!Mine(p) || Deep(p) < 2.2f || !PathClear(p, 2.2f) || InWater(p, 2f) || Reserved(p, 1.5f)) continue;
                        uint v = V(70);
                        if (Hashing.Unit(v) < _d.RowGaps) continue;
                        if (!_occ.Free(p, 1.6f, 1f, 0.3f)) continue;
                        int age = Hashing.Unit(Hashing.Mix(v, 3u)) < 0.12f ? 2 : 3;   // (young apple trees have the wider crowns: run 20261031-110042-farm-close)
                        var po = _ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 5u)) * 6.283f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), v, age);
                        po.Scale = math.lerp(_d.RowScaleLo, _d.RowScaleHi, Hashing.Unit(Hashing.Mix(v, 9u)));
                        _occ.Take(p, 1.6f, 3);
                        placed++;
                    }
                }
                _res.Trees += placed;
            }

            public void Planting(bool trees = true, bool bushes = true)
            {
                bool doTrees = trees, doBushes = bushes;
                var treeIds = _s.Trees != null && _s.Trees.Length > 0 ? _s.Trees : _d.Trees;
                var bushIds = _s.Bushes != null && _s.Bushes.Length > 0 ? _s.Bushes : _d.Bushes;
                int nt = doTrees ? Count(_d.TreesPerHa * _s.TreeAmount) : 0;
                int nb = doBushes ? Count(_d.BushesPerHa * _s.BushAmount) : 0;
                Func<float2, bool> rejectTree, rejectBush, rejectRim;
                if (_d.Water)
                {
                    // the pond's rim: wet trees 2.5-9 m from the water, reeds 0.3-5 m
                    rejectTree = p => !Rim(p, 2.5f, 9f);
                    rejectBush = p => !Rim(p, 0.3f, 5f);
                    rejectRim = null;
                    nt = (int)math.round(nt * 0.6f);
                }
                else
                {
                    // a ring round an open heart: at most 45 % of how deep the area gets (narrow rooms keep a clearing too)
                    float rim = _d.TreeRim > 0f ? math.min(_d.TreeRim, DeepQuantile(0.9f) * 0.45f) : 0f;
                    bool inBeds = _d.TreesInBeds && _res.Beds?[_b] != null;
                    rejectTree = p => !Mine(p) || rim > 0f && Deep(p) > rim || inBeds && !InBed(p) || !PathClear(p, 2.6f) || InWater(p, 2f);
                    rejectBush = p => !Mine(p) || !PathClear(p, 1.3f) || InWater(p, 0.8f);
                    rejectRim = p => !Mine(p) || Deep(p) > 9f || !PathClear(p, 1.3f) || InWater(p, 0.8f);
                }
                if (nt > 0 && treeIds.Length > 0 && (AssetAvailability.Has(AssetCategories.Tree) || _s.Trees.Length > 0))
                {
                    float radius = _d.TreesPerHa >= 150f ? 2.3f : 2.7f;
                    _res.Trees += NatureKit.Plant(_ctx, _occ, _box, nt, ObjectRole.Tree, treeIds, radius, _d.GroveScale, V(40), rejectTree, 0.06f, _d.Dead);
                }
                if (nb > 0 && bushIds.Length > 0 && (AssetAvailability.Has(AssetCategories.Bush) || _s.Bushes.Length > 0))
                {
                    var ids = bushIds;
                    _res.Bushes += NatureKit.Plant(_ctx, _occ, _box, nb, ObjectRole.Bush, ids, 1.1f, math.max(6f, _d.GroveScale * 0.45f), V(41), rejectBush, 0.12f);
                    // the edge's scrub: the rim band of the area gets more (a forest edge, rock giving way to scrub)
                    if (rejectRim != null && _d.RimScrub > 1.05f)
                    {
                        float rimShare = RimShare();
                        int extra = (int)math.round(nb * (_d.RimScrub - 1f) * rimShare);
                        if (extra > 0) _res.Bushes += NatureKit.Plant(_ctx, _occ, _box, extra, ObjectRole.Bush, ids, 1.0f, 5f, V(42), rejectRim, 0.2f);
                    }
                }
            }

            private float DeepQuantile(float q)
            {
                var v = new List<float>();
                for (int k = 0; k < _f.Count; k++) if (_f.Label[k] == _b) v.Add(_f.Interior[k]);
                if (v.Count == 0) return 0f;
                v.Sort();
                return v[math.clamp((int)(q * v.Count), 0, v.Count - 1)];
            }

            private float RimShare()
            {
                int all = 0, rim = 0;
                for (int k = 0; k < _f.Count; k++) if (_f.Label[k] == _b) { all++; if (_f.Interior[k] <= 9f) rim++; }
                return all == 0 ? 0f : rim / (float)all;
            }

            private bool Rim(float2 p, float lo, float hi)
            {
                if (_res.Water.Count == 0 || !PathClear(p, 1.2f)) return false;
                float d = _res.WaterEdges.Clearance(p, hi + 0.5f);
                if (d < lo || d > hi) return false;
                foreach (var w in _res.Water) if (Poly.Contains(w, p)) return false;
                return true;
            }

            private bool InBed(float2 p)
            {
                var bm = _res.Beds?[_b];
                if (bm == null) return false;
                int i = (int)math.round((p.x - _f.Origin.x) / _f.Cell), j = (int)math.round((p.y - _f.Origin.y) / _f.Cell);
                return i >= 0 && j >= 0 && i < _f.Nx && j < _f.Ny && bm[j * _f.Nx + i];
            }

            /// <summary>The biome's low plant layers (tussocks, reeds, heather, flower drifts), each a vanilla bush scaled down.</summary>
            public void PlantLayers()
            {
                if (_d.Plants.Length == 0) return;
                if (!AssetAvailability.Has(AssetCategories.Bush) && (_s.Bushes == null || _s.Bushes.Length == 0)) return;
                int li = 0;
                foreach (var pl in _d.Plants)
                {
                    li++;
                    float share = pl.Where == BiomeWhere.Water ? WaterShare() : pl.Where == BiomeWhere.Beds ? _d.Beds : 1f;
                    int n = Count(pl.PerHa * _s.BushAmount * share);
                    if (n <= 0 || pl.Ids.Length == 0) continue;
                    uint ds = Hashing.Mix(_seed, (uint)(li * 977));
                    Func<float2, bool> reject;
                    switch (pl.Where)
                    {
                        case BiomeWhere.Water: reject = p => !Rim(p, 0.3f, 5f); break;
                        case BiomeWhere.Beds: reject = p => !InBed(p) || !PathClear(p, 1.2f); break;
                        case BiomeWhere.Core: reject = p => !Mine(p) || Deep(p) < 4f || !PathClear(p, 1f) || InWater(p, 0.6f); break;
                        case BiomeWhere.Rim: reject = p => !Mine(p) || Deep(p) > 8f || !PathClear(p, 1f) || InWater(p, 0.6f); break;
                        case BiomeWhere.Drift: reject = p => !Mine(p) || BiomeField.Fbm(p / pl.Drift, ds) < 0.56f || !PathClear(p, 1f) || InWater(p, 0.6f); break;
                        default: reject = p => !Mine(p) || !PathClear(p, 1f) || InWater(p, 0.6f); break;
                    }
                    int before = _ctx.Plan.Objects.Count;
                    var ids = _s.Bushes != null && _s.Bushes.Length > 0 ? _s.Bushes : pl.Ids;
                    int placed = NatureKit.Plant(_ctx, _occ, _box, n, ObjectRole.Bush, ids, pl.Radius, pl.Drift, V(60 + li), reject, 0.15f);
                    // their own size: tussocks small, reeds tall
                    for (int i = before; i < _ctx.Plan.Objects.Count; i++)
                    {
                        var o = _ctx.Plan.Objects[i];
                        if (o.Role != ObjectRole.Bush) continue;
                        o.Scale = math.lerp(pl.ScaleLo, pl.ScaleHi, Hashing.Unit(Hashing.Mix(o.Variation, 0x5CA1u)));
                    }
                    _res.Plants += placed;
                }
            }

            /// <summary>Share of the area's cells within 5 m of water (the reed band's room).</summary>
            private float WaterShare()
            {
                if (_res.Water.Count == 0) return 0f;
                int all = 0, near = 0;
                for (int k = 0; k < _f.Count; k++)
                {
                    if (_f.Label[k] != _b) continue;
                    all++;
                    if (_res.WaterEdges.Clearance(_f.Pos(k), 5.5f) <= 5f) near++;
                }
                return all == 0 ? 0f : near / (float)all;
            }

            public void Logs()
            {
                int n = Count(_d.LogsPerHa);
                if (n <= 0 || !AssetAvailability.Has(AssetCategories.PlayNature)) return;   // the logs are of that category (NatureKit.Fallen)
                _res.Logs += NatureKit.Fallen(_ctx, _occ, _box, n, V(50), p => Mine(p) && Deep(p) > 3f && PathClear(p, 6f) && !InWater(p, 6f));
            }
        }
    }
}
