// Farmstead, the farm as a working place (P2-3, 2026-10-30; docs/runbooks/farmstead.md): what a real farm has besides its
// fields, all of it settings the styles preset (never add-ons):
//   landscape   the Biome mix: the farm's own fields (host), orchards in rows, rough grazing, woods, wet ground, ponds, rock;
//               the land decides where (orchards on the warm slopes, woods on the steep ground, wet ground in the hollows)
//   drainage    field ditches along the field boundaries, or a ravine (barranca) cut down the land's fall line
//   homestead   a machinery shed with the tractor, the pickup by the sheds, water tanks, firewood, the manure heap and the
//               compost windrow, bale stacks; how worked the yard is (Yard: tidy .. muddy)
//   hill land   rows along the contours on a slope, terraced fields on steep ground
//   season      the fields' paint: spring drilling, summer crop, harvest stubble, autumn ploughing
// Everything the ground design needs (gates, heaps, field kinds and rows, the season, the mud) goes into the plan as regions
// and facts (Core/Decals/Dynamic/FarmGround.cs lays the decals).

using System;
using System.Collections.Generic;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Biomes;
using Placemaker.Core.Layers.Types.Yards;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Farmstead
{
    public sealed partial class FarmsteadLayer
    {
        /// <summary>The farm's landscape: its own fields first, then the natural ground a farm keeps.</summary>
        public static readonly BiomeOffer Biomes = new BiomeOffer(
            ("fields", 100), ("orchard", 0), ("pasture", 0), ("forest", 0), ("wetland", 0), ("water", 0), ("rocky", 0)) { Scale = 70 };

        /// <summary>Tells the site harmony this plan shaped its own landforms (its regions "shaped:*" keep their shape).</summary>
        private static void Shaped(LayerContext ctx, int cells)
        {
            if (cells <= 0) return;
            ctx.Plan.Facts.TryGetValue(Build.SiteHarmony.ShapedFact, out var had);
            ctx.Plan.Facts[Build.SiteHarmony.ShapedFact] = had + cells;
        }

        private static int SeasonIndex(string s) => s == "spring" ? 0 : s == "harvest" ? 2 : s == "autumn" ? 3 : 1;

        private static void GateMark(LayerContext ctx, float2 p, string kind, int i) =>
            ctx.AddRegion(LayerRegion.Circle(p, 3f, 8), FarmGround.GateTag + kind + ":" + i, FarmGround.GateTag + kind);

        private static void HeapMark(LayerContext ctx, float2 p, string kind, int i) =>
            ctx.AddRegion(LayerRegion.Circle(p, 3f, 8), FarmGround.HeapTag + kind + ":" + i, FarmGround.HeapTag + kind);

        // ───────────── fields: paint by season, rows on the contour ─────────────

        /// <summary>The way a field's rows run on a slope: along the contour (ploughing across the fall line holds the soil).</summary>
        private static bool Contour(LayerContext ctx, LayerRegion f, out float2 dir, out float grade)
        {
            var c = f.Centroid;
            float r = math.clamp(math.sqrt(f.Area) * 0.35f, 6f, 40f);
            var g = new float2(ctx.H(c + new float2(r, 0)) - ctx.H(c - new float2(r, 0)), ctx.H(c + new float2(0, r)) - ctx.H(c - new float2(0, r))) / (2f * r);
            grade = math.length(g);
            dir = grade > 1e-4f ? math.normalize(new float2(-g.y, g.x)) : new float2(1, 0);
            return grade > 0.04f;
        }

        /// <summary>
        /// A field's ground in its season, painted (the four terrain materials: 2 dark forest floor, 3 light earth, 4 sand):
        /// spring = drilled earth with dark seed rows, summer = the green crop with darker rows, harvest = pale stubble (earth and
        /// a little sand) with straw windrows (decals), autumn = dark ploughed furrows.
        /// </summary>
        private static int PaintField(LayerContext ctx, float2[] ring, string kind, float2 rows, float spacing, int season, int budget)
        {
            int n = 0;
            const int Lush = TerrainMaterials.Lush, Dirt = TerrainMaterials.Dirt, Sand = TerrainMaterials.Sand;
            switch (kind)
            {
                case "Grain":
                    if (season == 0) { TerrainPaint.Patch(ctx, ring, Lush, 0.8f, 2.5f); TerrainPaint.Patch(ctx, ring, Dirt, 0.55f, 2.5f); n += Stripes(ctx, ring, rows, spacing, Lush, 1f, budget); }
                    else if (season == 1) { TerrainPaint.Patch(ctx, ring, Lush, 0.35f, 3f); n += Stripes(ctx, ring, rows, spacing, Lush, 0.55f, budget); }
                    else if (season == 2) { TerrainPaint.Patch(ctx, ring, Dirt, 0.7f, 3f); TerrainPaint.Patch(ctx, ring, Sand, 0.28f, 3f); n += Stripes(ctx, ring, rows, spacing * 1.5f, Dirt, 0.95f, budget); }
                    // ploughed: the dark forest floor carries it (the light earth at strength read as pale blotches, run show-7-builds-1031-000929-4a92)
                    else { TerrainPaint.Patch(ctx, ring, Lush, 1f, 2.5f); TerrainPaint.Patch(ctx, ring, Dirt, 0.35f, 2.5f); n += Stripes(ctx, ring, rows, spacing, Dirt, 0.35f, budget); }
                    break;
                case "Veg":
                    TerrainPaint.Patch(ctx, ring, Dirt, 0.9f, 2f);
                    n += Stripes(ctx, ring, rows, math.max(2f, spacing * 0.6f), Lush, season == 3 ? 0.4f : 0.85f, budget);
                    break;
                case "Fallow":
                    TerrainPaint.Patch(ctx, ring, Lush, 1f, 2.5f); TerrainPaint.Patch(ctx, ring, Dirt, 0.3f, 2.5f);
                    n += Stripes(ctx, ring, rows, spacing, Dirt, 0.3f, budget);
                    break;
                case "Hay":
                    // mown swaths: the cut rows a little paler, the uncut green between
                    n += Stripes(ctx, ring, rows, math.max(4f, spacing * 1.5f), season == 2 ? Dirt : Lush, season == 2 ? 0.45f : 0.3f, budget);
                    break;
            }
            return n;
        }

        // ───────────── terraces ─────────────

        /// <summary>
        /// Terraced fields on steep ground (a hill farm): each field's ground stepped into level benches with short banks. The
        /// tracks keep their own ground (the change dies out 4-9 m from them), the banks are held under 34 % (no curtain).
        /// </summary>
        private static int Terraces(LayerContext ctx, List<LayerRegion> fields, Occupancy occ, float step)
        {
            if (fields.Count == 0 || step <= 0f) return 0;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            var steep = new List<LayerRegion>();
            foreach (var f in fields)
            {
                if (!Contour(ctx, f, out _, out float grade) || grade < 0.07f || f.Area < 900f) continue;
                steep.Add(f);
                mn = math.min(mn, f.Min); mx = math.max(mx, f.Max);
            }
            if (steep.Count == 0) return 0;
            var region = ctx.LayerRegion;
            var sc = new Sculpt(ctx.H, mn, mx, 3f, q =>
            {
                if (!region.ContainsDisc(q, 4f) || occ.Corridors.Clearance(q, 3f) < 2.5f) return false;
                foreach (var f in steep) if (f.Contains(q)) return true;
                return false;
            });
            sc.Shape((q, b, t) =>
            {
                LayerRegion home = null;
                foreach (var f in steep) if (f.Contains(q)) { home = f; break; }
                if (home == null) return b;
                // level benches: the ground is held at the step below, rising to the next one over the last fifth (the bank)
                float u = b / step, k = math.floor(u), fr = u - k;
                float target = step * (k + Sculpt.Smooth01((fr - 0.78f) / 0.22f));
                float edge = Sculpt.Smooth01(home.BoundaryDistance(q, 8f) / 6f);
                float off = (target - b) * edge * Sculpt.Smooth01((occ.Corridors.Clearance(q, 10f) - 4f) / 5f);
                return b + off;
            });
            sc.LimitGrade(0.34f, 14, absolute: true);
            var res = sc.Emit(ctx, null);
            ctx.Plan.Facts["terracedFields"] = steep.Count;
            // (P2-3) the banks: a broken line of scrub along each riser (the Welsh hedge bank, the Mediterranean terrace's rough
            // edge). Stepped ground alone read as a lawn from 45° (run show-3-builds-1031-005331-af71: benches only on the terrain map).
            var bankIds = new AssetRef("GreenBushWild01", "GreenBushWildTeen01", "GreenBushWild02");
            int banks = 0;
            uint bv = Hashing.Mix(ctx.Seed, 0xBA9Cu);
            for (float y = mn.y + 1.25f; y < mx.y && banks < 600; y += 2.5f)
                for (float x = mn.x + 1.25f; x < mx.x && banks < 600; x += 2.5f)
                {
                    var q = new float2(x, y);
                    LayerRegion home = null;
                    foreach (var f in steep) if (f.Contains(q)) { home = f; break; }
                    if (home == null || home.BoundaryDistance(q, 6f) < 4f) continue;
                    float fr = ctx.H(q) / step; fr -= math.floor(fr);
                    if (fr < 0.82f || fr > 0.94f) continue;
                    uint h = Hashing.Mix(bv, (int)x, (int)y);
                    if (Hashing.Unit(h) < 0.25f) continue;
                    if (LeisureKit.Put(ctx, occ, q, Hashing.Unit(h ^ 0x55u) * 6.28f, ObjectRole.Bush, bankIds, null, 0.9f, h, 0.5f, 0.2f, 4)) banks++;
                }
            ctx.Plan.Facts["terraceBankBushes"] = banks;
            Shaped(ctx, res.Cells);
            for (int i = 0; i < steep.Count; i++) ctx.AddRegion(steep[i].Ring, "shaped:terrace:" + i, Build.SiteHarmony.ShapedTag + "terrace");
            return res.Cells;
        }

        // ───────────── drainage: ditches and the ravine ─────────────

        /// <summary>The line water would take across the site: from the high edge down the fall line, meandering; on flat land
        /// across the farm's short side in gentle bends. Stops at the outline; steps round the yard and the paddocks.</summary>
        private static List<float2> FallLine(LayerContext ctx, LayerRegion region, Footprints hard, float2 side, uint seed)
        {
            var line = new List<float2>();
            // the start: the outline's highest point (flat land: a seeded point on one long side)
            float best = float.MinValue; float2 start = region.Centroid;
            int n = math.max(24, (int)(region.Perimeter / 6f));
            float flatness = 0f;
            float hmin = float.MaxValue, hmax = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var p = region.PointAt((i + 0.5f) * region.Perimeter / n, out _, out var inward, out _) + inward * 4f;
                float h = ctx.H(p);
                hmin = math.min(hmin, h); hmax = math.max(hmax, h);
            }
            flatness = hmax - hmin;
            bool flat = flatness < 3f;
            for (int i = 0; i < n; i++)
            {
                var p = region.PointAt((i + 0.5f) * region.Perimeter / n, out _, out var inward, out _) + inward * 4f;
                float score = flat ? math.dot(p - region.Centroid, side) + Hashing.Unit(Hashing.Mix(seed, (uint)i)) * 8f : ctx.H(p);
                if (score > best && !hard.Hits(p, 6f)) { best = score; start = p; }
            }
            var dir = flat ? -math.normalizesafe(side) : float2.zero;
            var p0 = start;
            line.Add(p0);
            for (int k = 0; k < 400; k++)
            {
                float e = 4f;
                var g = new float2(ctx.H(p0 + new float2(e, 0)) - ctx.H(p0 - new float2(e, 0)), ctx.H(p0 + new float2(0, e)) - ctx.H(p0 - new float2(0, e)));
                var down = flat || math.lengthsq(g) < 1e-6f ? (math.lengthsq(dir) > 0 ? dir : -math.normalizesafe(side)) : -math.normalize(g);
                // meander: a slow swing across the way down
                float wob = (ValueNoise.Sample(new float2(k * 0.09f, 3.7f), seed) - 0.5f) * 1.6f;
                var nrm = new float2(-down.y, down.x);
                var want = math.normalizesafe(down + nrm * wob);
                dir = math.lengthsq(dir) > 0 ? math.normalizesafe(dir * 0.6f + want * 0.4f) : want;
                var next = p0 + dir * 4f;
                if (hard.Hits(next, 5f))
                {
                    // steer round the yard and the paddocks: the nearest free heading, turning further each try
                    bool found = false;
                    foreach (float ang in new[] { 0.5f, -0.5f, 1f, -1f, 1.5f, -1.5f })
                    {
                        float cs = math.cos(ang), sn = math.sin(ang);
                        var d2 = new float2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
                        if (hard.Hits(p0 + d2 * 4f, 5f)) continue;
                        dir = d2; next = p0 + d2 * 4f; found = true; break;
                    }
                    if (!found) break;
                }
                if (!region.Contains(next)) { line.Add(region.ClosestOnBoundary(next, out _, out _)); break; }
                line.Add(next);
                p0 = next;
            }
            return line;
        }

        private static float LineDist(List<float2> line, float2 q, out float t)
        {
            float best = float.MaxValue, acc = 0f, total = 0f, at = 0f;
            for (int i = 0; i + 1 < line.Count; i++) total += math.distance(line[i], line[i + 1]);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i]; var b = line[i + 1];
                var ab = b - a; float l2 = math.max(1e-6f, math.lengthsq(ab));
                float u = math.saturate(math.dot(q - a, ab) / l2);
                float d = math.distance(q, a + ab * u);
                float seg = math.sqrt(l2);
                if (d < best) { best = d; at = acc + u * seg; }
                acc += seg;
            }
            t = total > 0f ? at / total : 0f;
            return best;
        }

        /// <summary>
        /// A ravine (barranca) down the fall line: a channel 3-5 m deep with a gravel floor, ragged banks, scrub and trees on its
        /// sides, stones on its floor; or field ditches (1 m) along the field boundaries running across the farm. Returns the
        /// footprints the fields keep out of (the ravine only: ditches are in the headlands).
        /// </summary>
        private static List<float2[]> Drainage(LayerContext ctx, LayerRegion region, Occupancy occ, Footprints hard, float2 origin, float2 axis, float2 side,
            float Lf, float Wf, HashSet<int> trackLinesB, string[] treeIds, out int channels)
        {
            channels = 0;
            var keep = new List<float2[]>();
            string mode = ctx.S("drainage");
            ctx.Plan.Facts["drainageMode"] = mode == "ravine" ? 2 : mode == "ditches" ? 1 : 0;
            if (mode == "none") return keep;
            var lines = new List<(List<float2> line, float depth, float floor, float top)>();
            if (mode == "ravine")
            {
                var line = FallLine(ctx, region, hard, side, Hashing.Mix(ctx.Seed, 0x4AF1u));
                float len = 0f;
                for (int i = 0; i + 1 < line.Count; i++) len += math.distance(line[i], line[i + 1]);
                ctx.Plan.Facts["ravineLineLen"] = math.round(len);
                if (line.Count >= 4 && len > 40f)
                {
                    float depth = math.clamp(len / 40f, 2.5f, 5f);
                    lines.Add((line, depth, 1.6f, depth / 0.34f + 1.6f));
                }
            }
            else
            {
                // ditches along the field boundaries that run along the farm (the lines between field rows, not under a track)
                Lattice(region, origin, axis, side, Lf, Wf, out _, out _, out int ib0, out int ib1);
                for (int ib = ib0 + 1; ib <= ib1; ib++)
                {
                    if (trackLinesB.Contains(ib)) continue;
                    foreach (var (a, b) in Line(region, origin + side * (ib * Wf) + side * 1.2f, axis))
                        foreach (var (ra, rb) in Runs(a, b, p => hard.Hits(p, 3f) || !region.ContainsDisc(p, 4f), 2f, 20f))
                            lines.Add((new List<float2> { ra, math.lerp(ra, rb, 0.5f), rb }, 0.9f, 0.35f, 2.4f));
                }
            }
            if (lines.Count == 0) return keep;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            foreach (var l in lines) foreach (var q in l.line) { mn = math.min(mn, q - l.top - 4f); mx = math.max(mx, q + l.top + 4f); }
            var sc = new Sculpt(ctx.H, mn, mx, mode == "ravine" ? 2.5f : 2f, q => region.ContainsDisc(q, 3f) && !hard.Hits(q, 2f));
            uint ns = Hashing.Mix(ctx.Seed, 0xD17Cu);
            sc.Shape((q, b, t) =>
            {
                float off = 0f;
                foreach (var l in lines)
                {
                    float d = LineDist(l.line, q, out float tt);
                    if (d > l.top + 2f) continue;
                    float ends = mode == "ravine" ? Sculpt.Smooth01(tt / 0.06f) * 0.6f + 0.4f : Sculpt.Smooth01(tt / 0.1f) * Sculpt.Smooth01((1f - tt) / 0.1f);
                    float prof = 1f - Sculpt.Smooth01((d - l.floor) / math.max(0.5f, l.top - l.floor));
                    float rag = mode == "ravine" ? (ValueNoise.Sample(q / 4f, ns) - 0.5f) * 0.6f * prof * (1f - prof) * 4f : 0f;
                    // the floor falls on along the channel (water must leave it)
                    off = math.min(off, -l.depth * (0.85f + 0.3f * tt) * ends * prof + rag);
                }
                // the tracks keep their ground: under a track the channel dies out (a culvert)
                if (off != 0f) off *= Sculpt.Smooth01((occ.Corridors.Clearance(q, 8f) - 3f) / 4f);
                return b + off;
            });
            sc.LimitGrade(mode == "ravine" ? 0.45f : 0.5f, 14);
            sc.LimitGrade(mode == "ravine" ? 0.48f : 0.55f, 14, absolute: true);
            var res = sc.Emit(ctx, null);
            ctx.Plan.Facts["drainageCells"] = res.Cells;
            if (res.Cells == 0) return keep;
            channels = lines.Count;
            Shaped(ctx, res.Cells);
            ctx.Plan.Facts["drainage" + (mode == "ravine" ? "Ravine" : "Ditches")] = lines.Count;
            ctx.Plan.Facts["drainageDepth"] = math.round(-res.MinOffset * 10f) / 10f;
            int ri = 0;
            foreach (var l in lines)
            {
                // the channel as a region (its floor and banks: the ground design), held out of the fields and the hedges
                var band = Band(l.line, l.top + 0.5f);
                if (band != null)
                {
                    ctx.AddRegion(band, "farmChannel:" + ri, "farmChannel:" + mode, Build.SiteHarmony.ShapedTag + "channel");
                    if (mode == "ravine") { keep.Add(band); hard.Add(band); }
                }
                for (int i = 0; i + 1 < l.line.Count; i++) occ.TakeCorridor(l.line[i], l.line[i + 1], mode == "ravine" ? l.floor + 1f : 0.8f, 6);
                ri++;
            }
            if (mode == "ravine")
            {
                // scrub and thorn trees on the banks, stones on the floor: a wild strip through the farm
                var lr = keep.Count > 0 ? LayerRegion.From(keep[0]) : null;
                if (lr != null)
                {
                    var line = lines[0].line; float top = lines[0].top, floor = lines[0].floor;
                    var scrub = new[] { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildTeen01" };
                    var trees = treeIds != null && treeIds.Length > 0 ? treeIds : new[] { "EU_AlderTree01", "BirchTree01", "OakTree01", "EU_AlderTree01" };
                    float ha = lr.Area / 10000f;
                    int t = NatureKit.Plant(ctx, occ, lr, (int)(ha * 90f), ObjectRole.Tree, trees, 2.4f, 14f, 0x4AF2u, p => { float d = LineDist(line, p, out _); return d < floor + 1.5f || d > top; }, 0.1f);
                    int b = NatureKit.Plant(ctx, occ, lr, (int)(ha * 380f), ObjectRole.Bush, scrub, 1.1f, 6f, 0x4AF3u, p => { float d = LineDist(line, p, out _); return d < floor + 0.8f || d > top + 0.5f; }, 0.2f);
                    int r = NatureKit.Rocks(ctx, occ, lr, (int)math.max(2f, ha * 30f), 0x4AF4u, p => LineDist(line, p, out _) < floor + 1f ? 1f : 0f, null, 0.7f);
                    ctx.Plan.Facts["ravineTrees"] = t; ctx.Plan.Facts["ravineBushes"] = b; ctx.Plan.Facts["ravineRocks"] = r;
                }
            }
            return keep;
        }

        /// <summary>A band round a polyline (a channel's outline), simple or null.</summary>
        private static float2[] Band(List<float2> line, float half)
        {
            if (line.Count < 2) return null;
            var left = new List<float2>(); var right = new List<float2>();
            for (int i = 0; i < line.Count; i++)
            {
                var d = i + 1 < line.Count ? line[i + 1] - line[i] : line[i] - line[i - 1];
                if (i > 0 && i + 1 < line.Count) d = line[i + 1] - line[i - 1];
                var nrm = math.normalizesafe(new float2(-d.y, d.x));
                left.Add(line[i] + nrm * half); right.Add(line[i] - nrm * half);
            }
            right.Reverse(); left.AddRange(right);
            var ring = Poly.Simplify(left.ToArray(), 0.5f);
            if (ring.Length < 3) return null;
            if (!Poly.IsSimple(ring)) ring = Poly.Clean(ring, 1.5f);
            return ring.Length >= 3 && Poly.IsSimple(ring) ? Poly.EnsureCcw(ring) : null;
        }

        // ───────────── the homestead ─────────────

        /// <summary>
        /// What makes the yard a homestead: a machinery shed (carport) with the tractor under it, the farm pickup by the sheds,
        /// tractors and trailers parked where work happens, water tanks, firewood, the muck heap and a compost windrow beside the
        /// yard, bale stacks. Vehicles are Placemaker's own props of the game's vehicles (VehicleProps), pack props, then nothing.
        /// </summary>
        private static void Homestead(LayerContext ctx, LayerRegion region, Occupancy yo, Occupancy occ, OrientedRect yard, float frontSign, out int vehicles)
        {
            vehicles = 0;
            float back = -frontSign;
            var face = yard.Side * frontSign;
            float yawFront = math.atan2(face.x, face.y), yawBack = math.atan2(-face.x, -face.y);
            int wantVehicles = ctx.I("vehicles");
            // machinery shed at the -u end of the back row (the barns start after it), the farm tractor under it
            if (wantVehicles > 0)
            {
                var shedAt = yard.Local(-yard.HalfLength + 5f, frontSign * (yard.HalfWidth * 0.1f));
                var shed = new AssetRef("Carport03", "Carport04", "Carport01", "Carport02");
                if (LeisureKit.Put(ctx, yo, shedAt, yawFront, ObjectRole.Furniture, shed, AssetCategories.LogisticsShed, 4.2f, Hashing.Mix(ctx.Seed, 0x5ED1u), 0.3f, 0.2f))
                {
                    ctx.Object(shedAt, yawFront + math.PI * 0.5f, ObjectRole.Furniture, FarmArt.Tractor, Hashing.Mix(ctx.Seed, 0x5ED2u));
                    vehicles++;
                }
            }
            // the pickup by the yard front, nose to the sheds; a second tractor out in the yard with its trailer
            if (wantVehicles > 1)
            {
                var p = yard.Local(yard.HalfLength * 0.55f, frontSign * (yard.HalfWidth - 4f));
                if (LeisureKit.Put(ctx, yo, p, yawBack + 0.25f, ObjectRole.Furniture, FarmArt.Pickup, null, 2.8f, Hashing.Mix(ctx.Seed, 0x5ED3u), 0.4f, 0.2f)) vehicles++;
            }
            if (wantVehicles > 2)
            {
                var p = yard.Local(-yard.HalfLength * 0.1f, frontSign * yard.HalfWidth * 0.35f);
                if (LeisureKit.Put(ctx, yo, p, yard.Yaw + 0.4f, ObjectRole.Furniture, FarmArt.Tractor, null, 3.2f, Hashing.Mix(ctx.Seed, 0x5ED4u), 0.4f, 0.2f)) vehicles++;
            }
            // ATV / quad stand-in: a small car by the house gate is the farm runabout; more vehicles park along the barns
            for (int k = 3; k < wantVehicles; k++)
            {
                var p = yard.Local(-yard.HalfLength + 8f + (k - 3) * 7f, back * (yard.HalfWidth * 0.2f));
                var v = k % 2 == 0 ? FarmArt.Tractor : FarmArt.Pickup;
                if (LeisureKit.Put(ctx, yo, p, yawFront, ObjectRole.Furniture, v, null, 3f, Hashing.Mix(ctx.Seed, 0x5ED5u + (uint)k), 0.4f, 0.2f)) vehicles++;
            }
            // water tanks beside the barns (the farm's water store)
            int tanks = AssetAvailability.Has(AssetCategories.GardenShed) ? ctx.I("tanks") : 0;
            for (int k = 0; k < tanks; k++)
            {
                var p = yard.Local(yard.HalfLength + 4.5f + (k / 2) * 5f, back * (yard.HalfWidth - 4f - (k % 2) * 5f));
                if (!region.ContainsDisc(p, 3f)) p = yard.Local(yard.HalfLength - 3f - (k / 2) * 5f, frontSign * (yard.HalfWidth * 0.4f - (k % 2) * 5f));
                LeisureKit.Put(ctx, region.ContainsDisc(p, 3f) && !Poly.Contains(yard.Ring, p) ? occ : yo, p, Hashing.Unit(Hashing.Mix(ctx.Seed, 0x7A4Eu + (uint)k)) * 6.28f, ObjectRole.Furniture,
                    FarmArt.Tank(k), AssetCategories.GardenShed, 2.4f, Hashing.Mix(ctx.Seed, 0x7A4Fu + (uint)k), 0.3f, 0.3f);
            }
            // firewood at the back of the yard
            if (ctx.B("heaps"))
            {
                var fw = yard.Local(-yard.HalfLength * 0.55f, back * (yard.HalfWidth - 2f));
                LeisureKit.Put(ctx, yo, fw, yawFront, ObjectRole.Furniture, FarmArt.Firewood, null, 2f, Hashing.Mix(ctx.Seed, 0xF12Eu), 0.3f, 0.2f);
            }
        }

        /// <summary>The muck heap beside the yard (the cattle barns' end) and a compost windrow: low dark mounds (terraform and a
        /// dark crisp surface), straw round them (decals). Returns how many.</summary>
        private static int Heaps(LayerContext ctx, LayerRegion region, Occupancy occ, Footprints hard, OrientedRect yard, float frontSign)
        {
            if (!ctx.B("heaps")) return 0;
            int made = 0;
            float back = -frontSign;
            var spots = new[]
            {
                (yard.Local(yard.HalfLength + 9f, back * (yard.HalfWidth * 0.3f)), 5.5f, 1.4f, 1f, "manure"),
                (yard.Local(-yard.HalfLength - 10f, back * (yard.HalfWidth * 0.2f)), 9f, 1.1f, 0.35f, "compost"),
            };
            var sculptSpots = new List<(float2 c, float hl, float hw, float h, float2 ax)>();
            foreach (var (c, hl, h, aspect, kind) in spots)
            {
                float hw = math.max(2.2f, hl * aspect);
                if (!region.ContainsDisc(c, hl + 3f) || hard.Hits(c, hl + 1f) || !occ.Free(c, hl, 0.5f, 0.5f)) continue;
                var rect = new OrientedRect { Center = c, Axis = yard.Axis, HalfLength = hl, HalfWidth = hw };
                var blob = LeisureKit.Blob(c, 1f, Hashing.Mix(ctx.Seed, 0x4EA9u + (uint)made), 14, 0.15f);
                for (int i = 0; i < blob.Length; i++) { var d = blob[i] - c; blob[i] = c + yard.Axis * math.dot(d, yard.Axis) * hl * 0.9f + yard.Side * math.dot(d, yard.Side) * hw * 0.9f; }
                if (Poly.IsSimple(blob)) ctx.Surface(blob, SurfaceRole.Decorative, new AssetRef("Forestry Surface 01", "Forestry Surface 01"), AssetCategories.SurfaceNatural);
                occ.Take(c, hl, 1);
                hard.Add(rect.Ring);
                HeapMark(ctx, c, kind, made);
                sculptSpots.Add((c, hl, hw, h, yard.Axis));
                made++;
            }
            if (sculptSpots.Count > 0)
            {
                float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                foreach (var s in sculptSpots) { mn = math.min(mn, s.c - s.hl - 3f); mx = math.max(mx, s.c + s.hl + 3f); }
                var sc = new Sculpt(ctx.H, mn, mx, 1.5f, q => region.ContainsDisc(q, 2f));
                sc.Shape((q, b, t) =>
                {
                    float off = 0f;
                    foreach (var s in sculptSpots)
                    {
                        var d = q - s.c;
                        float u = math.dot(d, s.ax) / (s.hl + 1f), v = math.dot(d, new float2(-s.ax.y, s.ax.x)) / (s.hw + 1f);
                        float r = math.sqrt(u * u + v * v);
                        off = math.max(off, s.h * (1f - Sculpt.Smooth01(r)));
                    }
                    return b + off;
                });
                Shaped(ctx, sc.Emit(ctx, null).Cells);
                foreach (var s in sculptSpots) ctx.AddRegion(LayerRegion.Circle(s.c, s.hl + 1.5f, 12), "shaped:heap", Build.SiteHarmony.ShapedTag + "heap");
            }
            ctx.Plan.Facts["farmHeaps"] = made;
            return made;
        }

        // ───────────── the farmhouse and the gates ─────────────

        public const string HouseTag = "farmHouse";

        /// <summary>The farmhouse plot (32 × 34 m: the house's 24 m lot at the front, a garden behind) beside the yard, its front on
        /// the yard's front line (the road side): the -u end first, else the +u end. Taken into the hard footprints.</summary>
        private static bool HousePlot(LayerContext ctx, LayerRegion region, ReservedSpace res, Footprints hard, OrientedRect yard, float frontSign, out OrientedRect plot)
        {
            plot = default;
            if (!ctx.B("farmhouse")) return false;
            const float hl = 16f, hw = 17f;
            var reserved = new Footprints();
            if (res != null) foreach (var pg in res.Polygons) if (pg != null && pg.Length >= 3) reserved.Add(pg);
            foreach (float end in new[] { -1f, 1f })
                foreach (float gap in new[] { 3f, 10f })
                {
                    var r = new OrientedRect { Center = yard.Local(end * (yard.HalfLength + gap + hl), frontSign * (yard.HalfWidth - hw)), Axis = yard.Axis, HalfLength = hl, HalfWidth = hw };
                    bool ok = true;
                    foreach (var q in r.Ring) if (!region.ContainsDisc(q, 1f)) { ok = false; break; }
                    if (!ok || hard.HitsRect(r, 1f) || reserved.HitsRect(r, 0.5f)) continue;
                    // reserved paths and discs (a path the player keeps through the farm): a 4 m grid over the plot
                    if (res != null)
                        for (float a = -hl; a <= hl && ok; a += 4f)
                            for (float b = -hw; b <= hw && ok; b += 4f)
                                if (res.Blocks(r.Local(a, b), 1.5f)) ok = false;
                    if (!ok) continue;
                    plot = r;
                    hard.Add(r.Ring);
                    return true;
                }
            return false;
        }

        /// <summary>
        /// The farmhouse: a house (HouseProps: the game's own low-density houses as props) on a levelled lot facing the road, a
        /// low wooden fence along the road, a hedge round the garden with a gate toward the yard, fruit trees and a tool shed
        /// at the back. The game has no farmhouse prop; without the house the farm read as a barn yard in a field.
        /// </summary>
        private static void DressHouse(LayerContext ctx, Occupancy occ, OrientedRect plot, OrientedRect yard, float frontSign, string[] treeIds)
        {
            var face = plot.Side * frontSign;
            float yaw = math.atan2(face.x, face.y);
            uint v = Hashing.Mix(ctx.Seed, 0x40E5u);
            var lot = new OrientedRect { Center = plot.Local(0f, frontSign * (plot.HalfWidth - 12.5f)), Axis = plot.Axis, HalfLength = 12f, HalfWidth = 12f };
            var lr = LayerRegion.From(lot.Ring);
            if (lr != null) TerrainShaper.Shape(ctx, lr, new TerrainSpec { Mode = TerrainMode.Flatten, MaxSlopePct = 20f, Falloff = 6f, Seed = ctx.Seed });
            // the house: a slot by the seed, the others after it (the first the game has is built), a barn when none is
            int slots = Yards.HouseProps.Sources.Length;
            int k0 = (int)(v % (uint)slots);
            var ids = new List<string>();
            for (int k = 0; k < slots; k++) ids.Add(Yards.HouseProps.House((k0 + k) % slots + 1));
            ids.Add("Barn02");
            ctx.Object(lot.Center, yaw, ObjectRole.Furniture, new AssetRef(ids[0], ids.GetRange(1, ids.Count - 1).ToArray()), v);
            occ.Take(lot.Center, 11f, 1);
            // a low wooden fence along the road, the garden hedge round the other three sides with a gate toward the yard
            var ring = plot.Ring;
            var inner = LayerRegion.From(ring)?.InsetVertices(0.8f) ?? ring;
            float2 yardDir = math.normalizesafe(yard.Center - plot.Center);
            var fence = AssetMix.One("FenceResidentialLow01", "FenceIndustrialLow01");
            var line = new HedgeLine(ctx, occ);
            for (int e = 0; e < 4; e++)
            {
                var a = inner[e]; var b = inner[(e + 1) % 4];
                var mid = (a + b) * 0.5f; var d = math.normalizesafe(b - a);
                var outward = math.normalizesafe(mid - plot.Center);
                if (math.dot(outward, face) > 0.7f)
                {
                    // the road fence with the front gate in its middle (a path out to the street would cut the boundary hedge)
                    if (math.distance(a, mid) > 2f) ctx.StraightNet(a, mid - d * 0.9f, NetRole.Fence, fence);
                    if (math.distance(mid, b) > 2f) ctx.StraightNet(mid + d * 0.9f, b, NetRole.Fence, fence);
                    occ.TakeCorridor(a, b, 0.4f, 5);
                    GateLeaf(ctx, fence, mid - d * 0.9f, d, -outward, 1.8f, Hashing.Mix(v, 8u));
                    continue;
                }
                bool gateSide = math.dot(outward, yardDir) > 0.7f;
                // the garden gate toward the yard, in the back half (the house fills the front)
                var gateAt = mid - face * (plot.HalfWidth * 0.55f);
                float len = math.distance(a, b);
                for (float s = 1.2f; s < len; s += 2.4f)
                {
                    var p = a + d * s;
                    line.Add(p, d, !(gateSide && math.distance(p, gateAt) < 1.6f));
                }
                line.Add(b, d, false);
                if (gateSide)
                {
                    var hinge = gateAt + face * 1.1f;
                    GateLeaf(ctx, fence, hinge, -face, -outward, 2f, Hashing.Mix(v, 7u));
                    GateMark(ctx, gateAt, "house", 0);
                }
            }
            line.Flush();
            // fruit trees and the tool shed in the back garden
            var backward = -face;
            var trees = new AssetRef("AppleTree01", treeIds != null && treeIds.Length > 0 ? treeIds[0] : "OakTree01");
            for (int t = 0; t < 2; t++)
            {
                var p = plot.Center + plot.Axis * ((t == 0 ? -1f : 1f) * (plot.HalfLength - 4.5f)) + backward * (plot.HalfWidth - 4.5f);
                LeisureKit.Put(ctx, occ, p, Hashing.Unit(Hashing.Mix(v, 20u + (uint)t)) * 6.28f, ObjectRole.Tree, trees, null, 2.2f, Hashing.Mix(v, 20u + (uint)t), 0.3f, 0.2f, 3, 2);
            }
            // the kitchen garden: a dug bed in the back garden (dark soil)
            var bedC = plot.Center - plot.Axis * (plot.HalfLength * 0.35f) + backward * (plot.HalfWidth - 5f);
            TerrainPaint.Patch(ctx, LayerRegion.Rect(bedC, plot.Axis, 4.5f, 2.2f), TerrainMaterials.Lush, 1f, 0.5f);
            occ.Take(bedC, 3f, 2);
            var shedAt = plot.Center + plot.Axis * (plot.HalfLength * 0.15f) + backward * (plot.HalfWidth - 3.2f);
            LeisureKit.Put(ctx, occ, shedAt, yaw, ObjectRole.Furniture, new AssetRef("ToolShed01", "ToolShed02", "ToolShed03"), null, 2f, Hashing.Mix(v, 30u), 0.2f, 0.2f);
            ctx.AddRegion(ring, HouseTag, HouseTag);
            ctx.Plan.Facts["farmHouse"] = 1;
            // where it stands from the outline's box centre (the game runner frames a close look there: camera target point)
            var box = (ctx.LayerRegion.Min + ctx.LayerRegion.Max) * 0.5f;
            ctx.Plan.Facts["farmHouseX"] = math.round(lot.Center.x - box.x);
            ctx.Plan.Facts["farmHouseZ"] = math.round(lot.Center.y - box.y);
        }

        /// <summary>The 4 m (or 2 m) piece of a fence kind ("FenceIndustrialLow01" → "FenceIndustrialPieceLow01"), null for hedges.</summary>
        private static string GatePiece(AssetRef fence, float len)
        {
            string id = string.IsNullOrEmpty(fence.Id) && fence.Fallbacks != null && fence.Fallbacks.Length > 0 ? fence.Fallbacks[0] : fence.Id ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(id, @"^(.*Fence[A-Za-z]*?)(High|Low)(\d+)$");
            if (!m.Success) return null;
            return m.Groups[1].Value + "Piece" + m.Groups[2].Value + (len < 3f ? "Short" : "") + m.Groups[3].Value;
        }

        /// <summary>A gate hung in a fence gap: the fence kind's own piece, swung open 35-85° from its hinge into the field (the
        /// game has no gate object; a fence piece standing open reads as the gate).</summary>
        private static bool GateLeaf(LayerContext ctx, AssetRef fence, float2 hinge, float2 along, float2 inward, float len, uint v)
        {
            string piece = GatePiece(fence, len);
            if (piece == null) return false;
            float ang = math.radians(35f + Hashing.Unit(v) * 50f);
            var dir = math.normalizesafe(along * math.cos(ang) + inward * math.sin(ang));
            if (ctx.Input.Reserved != null && ctx.Input.Reserved.Blocks(hinge + dir * len * 0.5f, len * 0.5f)) return false;
            string fallback = len < 3f ? "FenceIndustrialPieceLowShort01" : "FenceIndustrialPieceLow01";
            ctx.Object(hinge + dir * len * 0.5f, math.atan2(dir.x, dir.y), ObjectRole.Furniture, new AssetRef(piece, fallback), v);
            ctx.Plan.Facts["farmGates"] = (ctx.Plan.Facts.TryGetValue("farmGates", out var n) ? n : 0) + 1;
            return true;
        }
    }
}
