// Building blocks shared by the industrial yard types (scrapyard, landfill): a height-field
// sculptor for heaps, mounds and levelled pads (always blended back to the original ground at the
// outline), gravel/concrete strips for haul roads, heaps and rows of props, grime decals, the gate
// compound (weighbridge, office cabin, barrier, floodlight), concrete-block material bays, screening
// tree belts, scrubby weeds along the fence and broken fences.
//
// Every placement goes through Occupancy (input.Reserved + site obstacles + what the type placed),
// and every category goes through a fallback chain so a type still builds something sensible with
// whatever is installed — an empty chain is an Info issue, never an error.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Scrapyard
{
    /// <summary>A round heap/mound in the height field: full height inside <c>Flat·R</c>, smooth to 0 at R.</summary>
    public struct Mound
    {
        public float2 Center;
        public float Radius, Height, Flat;
        /// <summary>Optional benches (stepped sides) every <c>Bench</c> metres of height; 0 = smooth dome.</summary>
        public float Bench;
        /// <summary>Working-face cut: sector direction, half angle (rad) and how much lower the sector stays (0..1).</summary>
        public float2 CutDir;
        public float CutHalfAngle, CutDepth;
        /// <summary>Optional ellipse: long axis and its stretch (≥ 1; Radius is the short half axis).</summary>
        public float2 Axis;
        public float Ratio;

        /// <summary>Normalised-ellipse distance from the centre (equals metres along the short axis).</summary>
        public float Distance(float2 p)
        {
            var d = p - Center;
            if (Ratio <= 1.01f || math.lengthsq(Axis) < 1e-6f) return math.length(d);
            var ax = math.normalize(Axis);
            float u = math.dot(d, ax) / Ratio, v = math.dot(d, new float2(-ax.y, ax.x));
            return math.sqrt(u * u + v * v);
        }

        /// <summary>True when p lies within the given fraction of the mound's footprint.</summary>
        public bool Covers(float2 p, float fraction = 1f) => Distance(p) < Radius * fraction;

        /// <summary>Point on the footprint at a fraction of the radius in direction <paramref name="dir"/> (unit).</summary>
        public float2 At(float2 dir, float fraction)
        {
            // Solve |dir·t| in ellipse space for the boundary distance.
            float unit = Distance(Center + dir);
            return Center + dir * (Radius * fraction / math.max(1e-4f, unit));
        }

        public float Offset(float2 p)
        {
            float r = Distance(p);
            if (r >= Radius || Radius <= 0f) return 0f;
            float t = r / Radius;
            float k = t <= Flat ? 1f : 1f - YardKit.Smooth(Flat, 1f, t);
            float h = Height * k;
            if (Bench > 0.5f && h > 0f)
            {
                float lv = math.floor(h / Bench) * Bench;
                h = lv + (h - lv) * 0.3f;
            }
            if (CutHalfAngle > 0f && CutDepth > 0f && r > Radius * 0.2f && math.lengthsq(CutDir) > 0f)
            {
                float a = math.acos(math.clamp(math.dot(math.normalizesafe(p - Center), math.normalizesafe(CutDir)), -1f, 1f));
                float w = 1f - YardKit.Smooth(CutHalfAngle * 0.6f, CutHalfAngle, a);
                h *= 1f - CutDepth * w;
            }
            return h;
        }
    }

    public static class YardKit
    {
        public const int KindHaul = 6;
        public const int KindStuff = 2;

        /// <summary>Feature with its own label key (for shared add-ons whose generic label is not in the language files).</summary>
        public static FeatureInfo Labelled(FeatureInfo f, string labelKey) { f.LabelKey = labelKey; return f; }

        /// <summary>
        /// Layout frame for yards: rows and blocks follow the outline's dominant edge direction (an
        /// L-shaped lot gets rows parallel to its sides, not diagonal ones along the principal axis).
        /// Painted outlines without a dominant direction keep the principal axis.
        /// </summary>
        public static void GridFrame(LayerRegion region, out float2 centre, out float2 axis, out float2 side, out float major, out float minor)
        {
            LeisureKit.Frame(region, out centre, out axis, out side, out major, out minor);
            const int bins = 36;   // 2.5° bins over 90°
            var weight = new float[bins];
            var ring = region.Ring;
            for (int i = 0; i < ring.Length; i++)
            {
                var d = ring[(i + 1) % ring.Length] - ring[i];
                float len = math.length(d);
                if (len < 0.5f) continue;
                float a = math.atan2(d.y, d.x);
                float m = a - math.floor(a / (math.PI * 0.5f)) * (math.PI * 0.5f);
                weight[math.min(bins - 1, (int)(m / (math.PI * 0.5f) * bins))] += len;
            }
            int best = 0;
            for (int b = 1; b < bins; b++) if (weight[b] > weight[best]) best = b;
            float total = region.Perimeter;
            float w = weight[best] + weight[(best + 1) % bins] + weight[(best + bins - 1) % bins];
            if (w < total * 0.4f) return;
            // Length-weighted mean angle within the winning bin and its neighbours.
            double sx = 0, sy = 0;
            for (int i = 0; i < ring.Length; i++)
            {
                var d = ring[(i + 1) % ring.Length] - ring[i];
                float len = math.length(d);
                if (len < 0.5f) continue;
                float a = math.atan2(d.y, d.x);
                float m = a - math.floor(a / (math.PI * 0.5f)) * (math.PI * 0.5f);
                int b = math.min(bins - 1, (int)(m / (math.PI * 0.5f) * bins));
                int diff = math.abs(b - best); diff = math.min(diff, bins - diff);
                if (diff > 1) continue;
                // Average on the 4-fold circle so 0° and 89° agree.
                sx += len * math.cos(4f * a); sy += len * math.sin(4f * a);
            }
            float ang = (float)math.atan2(sy, sx) * 0.25f;
            var ax = new float2(math.cos(ang), math.sin(ang));
            var sd = new float2(-ax.y, ax.x);
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            foreach (var v in ring)
            {
                var d = v - centre;
                minA = math.min(minA, math.dot(d, ax)); maxA = math.max(maxA, math.dot(d, ax));
                minB = math.min(minB, math.dot(d, sd)); maxB = math.max(maxB, math.dot(d, sd));
            }
            float spanA = maxA - minA, spanB = maxB - minB;
            if (spanB > spanA) { ax = sd; float t = spanA; spanA = spanB; spanB = t; }
            axis = ax; side = new float2(-ax.y, ax.x);
            major = spanA; minor = spanB;
        }

        /// <summary>
        /// Candidates in a hashed order: the sampler's stride order walks the region in long diagonal
        /// sweeps, which strings "first come" heaps out in a line.
        /// </summary>
        public static List<float2> Shuffled(List<float2> pts, uint seed)
        {
            var keyed = new List<(uint k, float2 p)>(pts.Count);
            foreach (var p in pts) keyed.Add((Hashing.Mix(seed, (int)math.floor(p.x * 10f), (int)math.floor(p.y * 10f)), p));
            keyed.Sort((a, b) => a.k.CompareTo(b.k));
            var r = new List<float2>(keyed.Count);
            foreach (var kp in keyed) r.Add(kp.p);
            return r;
        }

        public static float Smooth(float e0, float e1, float x)
        {
            if (e1 <= e0) return x >= e0 ? 1f : 0f;
            float t = math.saturate((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// First category in the chain that is available (the player's explicit picks count as the
        /// first); null + one Info issue when none is.
        /// </summary>
        public static string Cat(LayerContext ctx, string[] chosen, params string[] chain)
        {
            if (chosen != null) foreach (var id in chosen) if (!string.IsNullOrEmpty(id)) return chain[0];
            return LeisureKit.Category(ctx, "", chain);
        }

        /// <summary>Like <see cref="Cat"/> but silent when nothing is available (optional details).</summary>
        public static string CatQuiet(params string[] chain)
        {
            foreach (var c in chain) if (AssetAvailability.Has(c)) return c;
            return null;
        }

        // ───────────── terrain ─────────────

        /// <summary>
        /// Height-field terraform in square cells. <paramref name="offset"/>(p, h0) gives the wanted
        /// change at a cell centre; it is blended to 0 over <paramref name="falloff"/> metres at the
        /// outline and clamped so no cell differs from the original ground by more than the max slope
        /// times its distance to the outline (never a cliff at the border), then relaxed between
        /// neighbours. Adds the shared permanent-terrain warning when anything changes.
        /// </summary>
        public static TerrainResult Sculpt(LayerContext ctx, LayerRegion region, Func<float2, float, float> offset, float maxSlopePct, float falloff, float cellSize = 10f)
        {
            var result = new TerrainResult { HeightAfter = ctx.H };
            if (region == null || offset == null) return result;
            float cell = math.max(cellSize, math.sqrt(region.Area / TerrainShaper.MaxCells));
            int nx = math.max(1, (int)math.ceil((region.Max.x - region.Min.x) / cell));
            int ny = math.max(1, (int)math.ceil((region.Max.y - region.Min.y) / cell));
            var off = new float[nx * ny];
            var baseH = new float[nx * ny];
            var inside = new bool[nx * ny];
            var dist = new float[nx * ny];
            falloff = math.max(cell, falloff);
            float slope = math.max(0.05f, maxSlopePct / 100f);
            for (int iy = 0; iy < ny; iy++)
            {
                if ((iy & 15) == 0) ctx.Check();
                for (int ix = 0; ix < nx; ix++)
                {
                    int i = iy * nx + ix;
                    var c = region.Min + (new float2(ix, iy) + 0.5f) * cell;
                    if (!region.Contains(c)) continue;
                    inside[i] = true;
                    float h0 = ctx.H(c);
                    baseH[i] = h0;
                    float o = offset(c, h0);
                    if (!math.isfinite(o)) o = 0f;
                    // Only look as far for the outline as this cell's change needs (keeps the search cheap).
                    float d = region.BoundaryDistance(c, math.max(falloff, math.abs(o) / slope) + 1f);
                    dist[i] = d;
                    o *= Smooth(0f, falloff, d);
                    off[i] = math.clamp(o, -slope * d, slope * d);
                }
            }
            float maxStep = slope * cell;
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                for (int iy = 0; iy < ny; iy++)
                    for (int ix = 0; ix < nx; ix++)
                    {
                        int i = iy * nx + ix;
                        if (!inside[i]) continue;
                        float t = baseH[i] + off[i];
                        for (int k = 0; k < 4; k++)
                        {
                            int jx = ix + (k == 0 ? 1 : k == 1 ? -1 : 0), jy = iy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (jx < 0 || jy < 0 || jx >= nx || jy >= ny) continue;
                            int j = jy * nx + jx;
                            if (!inside[j]) continue;
                            float tj = baseH[j] + off[j];
                            if (math.abs(t - tj) <= maxStep || math.abs(t - baseH[i]) <= math.abs(off[j])) continue;
                            t = tj + math.sign(t - tj) * maxStep;
                            changed = true;
                        }
                        // Relaxation may only pull a cell towards its original ground, never past the edge clamp.
                        off[i] = math.clamp(t - baseH[i], -slope * dist[i], slope * dist[i]);
                    }
                if (!changed) break;
            }
            int cells = 0; float minO = 0f, maxO = 0f;
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int i = iy * nx + ix;
                    if (!inside[i] || math.abs(off[i]) < 0.2f) continue;
                    var c = region.Min + (new float2(ix, iy) + 0.5f) * cell;
                    var square = LayerRegion.Rect(c, new float2(1, 0), cell * 0.5f, cell * 0.5f);
                    float2[] ring = square;
                    bool whole = true;
                    foreach (var q in square) if (!region.Contains(q)) { whole = false; break; }
                    if (!whole)
                    {
                        var pieces = Poly.Intersection(square, region.Ring);
                        if (pieces.Count == 0 || pieces[0].Outer == null || pieces[0].Outer.Length < 3) continue;
                        ring = pieces[0].Outer;
                    }
                    ctx.Plan.Terraform.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(ring, ctx.H),
                        Kind = off[i] > 0f ? TerraformKind.Raise : TerraformKind.Lower,
                        TargetHeight = baseH[i] + off[i],
                        Permanent = true,
                    });
                    minO = math.min(minO, off[i]); maxO = math.max(maxO, off[i]);
                    cells++;
                }
            result.Cells = cells; result.MinOffset = minO; result.MaxOffset = maxO;
            if (cells > 0)
            {
                bool warned = false;
                foreach (var i in ctx.Plan.Issues) if (i.Key == TerrainShaper.PermanentKey) { warned = true; break; }
                if (!warned) ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
                var min = region.Min; float cs = cell; int w = nx, h = ny;
                result.HeightAfter = p =>
                {
                    int ix = (int)math.floor((p.x - min.x) / cs), iy = (int)math.floor((p.y - min.y) / cs);
                    if (ix < 0 || iy < 0 || ix >= w || iy >= h) return ctx.H(p);
                    int i = iy * w + ix;
                    return ctx.H(p) + (inside[i] ? off[i] : 0f);
                };
                ctx.Plan.Facts["terrainCells"] = cells;
                ctx.Plan.Facts["terrainMaxRaise"] = maxO;
                ctx.Plan.Facts["terrainMaxLower"] = -minO;
            }
            return result;
        }

        /// <summary>Objects sit on the shaped ground (the preview draws them where they will end up).</summary>
        public static void Settle(LayerContext ctx, TerrainResult terrain)
        {
            if (terrain == null || terrain.Cells == 0) return;
            foreach (var o in ctx.Plan.Objects) o.Position.y = terrain.HeightAfter(o.Position.xz);
        }

        // ───────────── ground ─────────────

        /// <summary>A strip a→b of the given half width, clipped to the region, emitted as an accepted surface.</summary>
        public static bool Strip(LayerContext ctx, float2 a, float2 b, float halfWidth, SurfaceRole role, AssetRef asset, string category)
        {
            float len = math.distance(a, b);
            if (len < 1f) return false;
            var dir = (b - a) / len;
            // Overlap the ends by a half width so consecutive strips of a polyline join without gaps.
            var rect = LayerRegion.Rect(math.lerp(a, b, 0.5f), dir, len * 0.5f + halfWidth * 0.5f, halfWidth);
            bool any = false;
            List<Region> pieces;
            try { pieces = Poly.Intersection(rect, ctx.LayerRegion.Ring); }
            catch (Exception) { return false; }
            foreach (var piece in pieces)
            {
                if (piece.Outer == null || piece.Outer.Length < 3 || Poly.Area(piece.Outer) < 4f) continue;
                any |= ctx.Surface(piece.Outer, role, asset, category);
            }
            return any;
        }

        /// <summary>Haul road along a polyline: surface strips + corridors (nothing else is placed on it).</summary>
        public static void Haul(LayerContext ctx, Occupancy occ, IReadOnlyList<float2> line, float width, SurfaceRole role, AssetRef asset, string category)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Strip(ctx, line[i], line[i + 1], width * 0.5f, role, asset, category);
                occ.TakeCorridor(line[i], line[i + 1], width * 0.5f, KindHaul);
            }
            Yards.YardWear.NoteLane(ctx.Plan, line, width * 0.5f, role == SurfaceRole.Asphalt || role == SurfaceRole.Paving);
        }

        /// <summary>Clipped blob/polygon as a surface (dropped quietly when nothing of it survives).</summary>
        public static void Patch(LayerContext ctx, float2[] ring, SurfaceRole role, AssetRef asset, string category)
        {
            List<Region> pieces;
            try { pieces = Poly.Intersection(ring, ctx.LayerRegion.Ring); }
            catch (Exception) { return; }
            foreach (var piece in pieces)
                if (piece.Outer != null && piece.Outer.Length >= 3 && Poly.Area(piece.Outer) > 6f)
                    ctx.Surface(piece.Outer, role, asset, category);
        }

        // ───────────── props ─────────────

        /// <summary>
        /// A heap: props packed on a golden spiral inside radius <paramref name="r"/>, bigger towards the
        /// middle (reads as a pile rising to a peak). Returns the count placed.
        /// </summary>
        public static int Heap(LayerContext ctx, Occupancy occ, float2 c, float r, int count, string[] ids, string category, float propR, uint salt,
            float minScale = 0.7f, float maxScale = 1.4f)
        {
            if (category == null || count <= 0) return 0;
            int placed = 0;
            const float golden = 2.39996323f;
            float phase = Hashing.Unit(salt) * 6.283f;
            for (int i = 0; i < count * 3 && placed < count; i++)
            {
                float t = math.sqrt((i + 0.5f) / (count * 1.3f));
                if (t > 1f) break;
                var p = c + new float2(math.cos(i * golden + phase), math.sin(i * golden + phase)) * r * t;
                uint v = Hashing.Mix(salt, (uint)i);
                float scale = math.lerp(maxScale, minScale, t) * (0.9f + 0.2f * Hashing.Unit(Hashing.Mix(v, 3u)));
                // Heap items may touch each other (gap < 0): a pile, not a showroom.
                // Never into a road or path though.
                float pr = math.max(0.6f, propR * scale * 0.6f);
                // Art pass: automatic heaps get their curated asset now, spaced by its real footprint —
                // in game overlapping props are culled, so a "pile" of 5.8 m loads on 1.3 m discs lost
                // half its items.
                var asset = AssetMix.Pick(ids, v);
                if (string.IsNullOrEmpty(asset.Id))
                {
                    asset = GritAssets.For(category, v, salt);
                    pr = math.max(pr, AssetMix.FootprintRadius(ctx.Site, asset, 0f) * 0.7f);
                }
                if (!ctx.LayerRegion.ContainsDisc(p, pr) || occ.Discs.Overlaps(p, pr, -0.2f) || occ.Corridors.Overlaps(p, pr, 0.2f) || occ.HitsPolygon(p, pr)) continue;
                var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 7u)) * 6.283f, ObjectRole.Furniture, asset, v, -1, category);
                o.Scale = scale;
                occ.Take(p, pr, KindStuff);
                placed++;
            }
            return placed;
        }

        /// <summary>A row of props a→b every <paramref name="spacing"/> m, facing along the row (± jitter).</summary>
        public static int Row(LayerContext ctx, Occupancy occ, float2 a, float2 b, float spacing, string[] ids, string category, float propR, uint salt,
            float yawJitter = 0.1f, float yawOffset = 0f, float gap = 0.1f, ObjectRole role = ObjectRole.Furniture)
        {
            if (category == null && role == ObjectRole.Furniture) return 0;
            int n = 0, i = 0;
            float baseYaw = LeisureKit.Yaw(a, b) + yawOffset;
            foreach (var p in LeisureKit.Along(a, b, spacing))
            {
                uint v = Hashing.Mix(salt, (uint)i++);
                float yaw = baseYaw + (Hashing.Unit(v) - 0.5f) * 2f * yawJitter;
                if (LeisureKit.Put(ctx, occ, p, yaw, role, AssetMix.Pick(ids, v), category, propR, v, 0.4f, gap)) n++;
            }
            return n;
        }

        /// <summary>Decals in a disc (never reserve space; only inside the region).</summary>
        public static int Grime(LayerContext ctx, Occupancy reservedOnly, float2 c, float r, int count, string category, uint salt)
        {
            if (category == null || count <= 0) return 0;
            int n = 0;
            for (int i = 0; i < count * 2 && n < count; i++)
            {
                uint v = Hashing.Mix(salt, (uint)i);
                float a = Hashing.Unit(v) * 6.283f, rr = r * math.sqrt(Hashing.Unit(Hashing.Mix(v, 1u)));
                var p = c + new float2(math.cos(a), math.sin(a)) * rr;
                if (!ctx.LayerRegion.ContainsDisc(p, 0.5f)) continue;
                // Decals may lie on our own roads, never on what earlier layers reserved.
                if (reservedOnly != null && !reservedOnly.Free(p, 0.6f, 0.5f, 0f)) continue;
                ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Decal, AssetMix.One(""), v, -1, category);
                n++;
            }
            return n;
        }

        // ───────────── compounds ─────────────

        /// <summary>
        /// Gate compound just inside a gate: a weighbridge slab across the haul road, a site office
        /// cabin beside it, a barrier and a floodlight. Returns the point where the yard road starts.
        /// </summary>
        public static float2 GateCompound(LayerContext ctx, Occupancy occ, Gate gate, float roadWidth, bool office, bool weighbridge, bool lights,
            string[] cabinIds, uint salt, out int cabins)
        {
            cabins = 0;
            var region = ctx.LayerRegion;
            var inward = region.InwardNormal(gate.Segment);
            var side = new float2(inward.y, -inward.x);
            float depth = math.min(22f, math.max(6f, region.BoundaryDistance(gate.Position + inward * 30f, 30f)));
            if (weighbridge)
            {
                var c = gate.Position + inward * 9f;
                if (region.ContainsDisc(c, 4f))
                    Patch(ctx, LayerRegion.Rect(c, inward, 7f, 1.9f), SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
            }
            float off = roadWidth * 0.5f + 4f;
            if (office)
            {
                string cat = CatQuiet(ScrapyardCategories.Cabin, AssetCategories.GardenShed, AssetCategories.Kiosk);
                if (cat != null)
                    for (int k = 0; k < 6 && cabins == 0; k++)
                    {
                        float along = 9f + k * 3f;
                        foreach (float s in new[] { 1f, -1f })
                        {
                            var p = gate.Position + inward * along + side * off * s;
                            if (LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, p - side * s), ObjectRole.Furniture, AssetMix.Pick(cabinIds, salt), cat, 3.2f,
                                Hashing.Mix(salt, 1u), 1f, 0.3f)) { cabins++; break; }
                        }
                    }
            }
            // Barrier/cones at the gate mouth, on both sides of the road.
            string barrier = CatQuiet(ScrapyardCategories.Barrier, AssetCategories.Bollard);
            if (barrier != null)
                foreach (float s in new[] { 1f, -1f })
                {
                    var p = gate.Position + inward * 2.5f + side * (roadWidth * 0.5f + 1f) * s;
                    LeisureKit.Put(ctx, occ, p, LeisureKit.Yaw(p, p + inward), ObjectRole.Furniture, AssetMix.One(""), barrier, 0.7f, Hashing.Mix(salt, 2u + (uint)(s + 1)), 0.3f, 0.1f);
                }
            if (lights)
            {
                string flood = CatQuiet(AssetCategories.FloodLight, AssetCategories.Lamp);
                if (flood != null)
                {
                    var p = gate.Position + inward * 4f - side * (roadWidth * 0.5f + 2f);
                    LeisureKit.Put(ctx, occ, p, 0f, flood == AssetCategories.Lamp ? ObjectRole.Lamp : ObjectRole.Furniture, AssetMix.One(""),
                        flood == AssetCategories.Lamp ? null : flood, 0.5f, Hashing.Mix(salt, 9u), 0.3f, 0.1f);
                }
            }
            return gate.Position + inward * math.min(depth, 16f);
        }

        /// <summary>
        /// Material bays along the outline: three-sided concrete-block bays (open towards the yard)
        /// every <paramref name="bayWidth"/> m, each holding a heap. <paramref name="fill"/>(index, centre,
        /// inward, halfWidth, halfDepth) fills a bay. Returns the bay count.
        /// </summary>
        public static int Bays(LayerContext ctx, Occupancy occ, float inset, float bayWidth, float bayDepth, List<Gate> gates, float gateClear,
            Func<float2, bool> allowed, Action<int, float2, float2, float, float> fill, uint salt)
        {
            var region = ctx.LayerRegion;
            string wall = CatQuiet(ScrapyardCategories.Barrier, AssetCategories.Bollard);
            int bays = 0;
            for (float s = bayWidth * 0.5f; s < region.Perimeter - bayWidth * 0.5f; s += bayWidth + 1.5f)
            {
                var e = region.PointAt(s, out var tangent, out var inward, out _);
                var c = e + inward * (inset + bayDepth * 0.5f);
                bool nearGate = false;
                foreach (var g in gates) if (math.distance(g.Position, e) < gateClear) { nearGate = true; break; }
                if (nearGate || (allowed != null && !allowed(c))) continue;
                var rect = new OrientedRect { Center = c, Axis = tangent, HalfLength = bayWidth * 0.5f, HalfWidth = bayDepth * 0.5f };
                // Walls sit on the rectangle's edges: test a grown copy so the blocks keep clear too.
                var grown = new OrientedRect { Center = c, Axis = tangent, HalfLength = rect.HalfLength + 0.7f, HalfWidth = rect.HalfWidth + 0.7f };
                if (!LeisureKit.RectFits(region, occ, grown, 0.1f, 0.2f)) continue;
                if (wall != null)
                {
                    var back0 = rect.Local(-rect.HalfLength, -rect.HalfWidth); var back1 = rect.Local(rect.HalfLength, -rect.HalfWidth);
                    var s0a = back0; var s0b = rect.Local(-rect.HalfLength, rect.HalfWidth * 0.8f);
                    var s1a = back1; var s1b = rect.Local(rect.HalfLength, rect.HalfWidth * 0.8f);
                    uint v = Hashing.Mix(salt, (uint)bays * 31u);
                    // Blocks are ~1.6 m long: lay them nose to tail along each wall.
                    PlaceWall(ctx, back0, back1, wall, v);
                    PlaceWall(ctx, s0a, s0b, wall, v + 1u);
                    PlaceWall(ctx, s1a, s1b, wall, v + 2u);
                }
                fill?.Invoke(bays, c, inward, rect.HalfLength, rect.HalfWidth);
                LeisureKit.TakeRect(occ, rect, KindStuff);
                bays++;
            }
            return bays;
        }

        private static void PlaceWall(LayerContext ctx, float2 a, float2 b, string category, uint salt)
        {
            float yaw = LeisureKit.Yaw(a, b) + math.PI * 0.5f;
            int i = 0;
            foreach (var p in LeisureKit.Along(a, b, 1.7f))
            {
                if (!ctx.LayerRegion.ContainsDisc(p, 0.3f)) continue;
                ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.One(""), Hashing.Mix(salt, (uint)i++), -1, category);
            }
        }

        /// <summary>Screening trees just inside the fence (gaps at the gates). Returns the count.</summary>
        public static int Screen(LayerContext ctx, Occupancy occ, List<Gate> gates, float inset, float spacing, string[] ids, uint salt)
        {
            var region = ctx.LayerRegion;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, ids, Keys.Category(AssetCategories.Tree))) return 0;
            int n = 0, i = 0;
            for (float s = spacing * 0.5f; s < region.Perimeter; s += spacing, i++)
            {
                var e = region.PointAt(s, out _, out var inward, out _);
                bool nearGate = false;
                foreach (var g in gates) if (math.distance(g.Position, e) < 12f) { nearGate = true; break; }
                if (nearGate) continue;
                uint v = Hashing.Mix(salt, (uint)i);
                var p = e + inward * (inset + (Hashing.Unit(v) - 0.5f) * 1.2f);
                if (!occ.Free(p, 2.4f, inset * 0.6f, 0.2f)) continue;
                ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Tree, AssetMix.Pick(ids, v), v, 2 + (int)(v % 2u));
                occ.Take(p, 2.4f, 3);
                n++;
            }
            return n;
        }

        /// <summary>Scrubby weeds (bushes) hugging the outline and the corners. Returns the count.</summary>
        public static int Weeds(LayerContext ctx, Occupancy occ, float perHa, float band, uint salt)
        {
            var region = ctx.LayerRegion;
            int wanted = (int)(region.Area / 10000f * perHa);
            if (wanted <= 0 || !AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, null, Keys.Category(AssetCategories.Bush))) return 0;
            return LeisureKit.Plant(ctx, occ, region, wanted, ObjectRole.Bush, null, 1.1f, 9f, 0.15f, salt, p => region.BoundaryDistance(p, band + 1f) > band);
        }

        /// <summary>
        /// Fence along the outline. "broken" leaves random gaps and a few leaning stubs: a fence nobody
        /// has mended in years.
        /// </summary>
        public static int Fence(LayerContext ctx, List<Gate> gates, string mode, AssetRef asset, uint salt)
        {
            if (mode == "none") return 0;
            int n = 0, i = 0;
            foreach (var run in Fences.Runs(ctx.LayerRegion, gates, 4.5f))
            {
                if (mode == "broken")
                {
                    var a = run.c0; var b = run.c1;
                    float len = math.distance(a, b);
                    uint v = Hashing.Mix(salt, (uint)i++);
                    if (Hashing.Unit(v) < 0.22f) continue;   // whole panel run gone
                    if (len > 12f && Hashing.Unit(Hashing.Mix(v, 1u)) < 0.6f)
                    {
                        // A hole somewhere in the run.
                        float g0 = 0.25f + 0.4f * Hashing.Unit(Hashing.Mix(v, 2u));
                        float g1 = math.min(0.95f, g0 + 3.5f / len + 0.1f * Hashing.Unit(Hashing.Mix(v, 3u)));
                        ctx.StraightNet(a, math.lerp(a, b, g0), NetRole.Fence, asset);
                        if (g1 < 0.97f) ctx.StraightNet(math.lerp(a, b, g1), b, NetRole.Fence, asset);
                        n += 2;
                        continue;
                    }
                }
                ctx.StraightNet(run.c0, run.c1, NetRole.Fence, asset);
                n++;
            }
            return n;
        }

        /// <summary>Chords of lines parallel to <paramref name="dir"/> across a region, every <paramref name="pitch"/> m.</summary>
        public static List<(float2 a, float2 b, int k)> Lines(LayerRegion region, float2 centre, float2 dir, float pitch, float offset, float inset)
        {
            var result = new List<(float2, float2, int)>();
            var across = new float2(-dir.y, dir.x);
            float minB = float.MaxValue, maxB = float.MinValue;
            foreach (var v in region.Ring) { float d = math.dot(v - centre, across); minB = math.min(minB, d); maxB = math.max(maxB, d); }
            float reach = math.length(region.Max - region.Min) + 10f;
            int k0 = (int)math.floor((minB - offset) / pitch), k1 = (int)math.ceil((maxB - offset) / pitch);
            for (int k = k0; k <= k1; k++)
            {
                var p = centre + across * (offset + k * pitch);
                foreach (var (a, b) in Poly.ClipSegmentToPolygon(region.Ring, p - dir * reach, p + dir * reach))
                {
                    float len = math.distance(a, b);
                    if (len < inset * 2f + 4f) continue;
                    var d = (b - a) / len;
                    result.Add((a + d * inset, b - d * inset, k));
                }
            }
            return result;
        }
        /// <summary>Clamps a point into the region (towards the given anchor) — for computed targets on odd outlines.</summary>
        public static float2 Inside(LayerRegion region, float2 p, float2 anchor, float margin)
        {
            if (region.ContainsDisc(p, margin)) return p;
            for (int k = 1; k <= 10; k++)
            {
                var q = math.lerp(p, anchor, k / 10f);
                if (region.ContainsDisc(q, margin)) return q;
            }
            return anchor;
        }

        /// <summary>Polyline from a to b bending around a centre at radius r (haul road around a mound).</summary>
        public static List<float2> ArcAround(float2 a, float2 b, float2 centre, float r, float stepDeg = 15f)
        {
            var pts = new List<float2> { a };
            float a0 = math.atan2(a.y - centre.y, a.x - centre.x);
            float a1 = math.atan2(b.y - centre.y, b.x - centre.x);
            float d = a1 - a0;
            while (d > math.PI) d -= 2f * math.PI;
            while (d < -math.PI) d += 2f * math.PI;
            int steps = math.max(1, (int)math.ceil(math.abs(math.degrees(d)) / stepDeg));
            for (int i = 1; i < steps; i++)
            {
                float ang = a0 + d * i / steps;
                pts.Add(centre + new float2(math.cos(ang), math.sin(ang)) * r);
            }
            pts.Add(b);
            return pts;
        }
    }
}
