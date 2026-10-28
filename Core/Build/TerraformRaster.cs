// Placemaker — direct terraform/paint rasters (GPL-3.0).
//
// Playtest 4b: emitting hundreds of vanilla level-brush definitions per frame crawled (2 FPS, purple
// brush previews, "stuck at 18%"). The build now applies the terrain directly through
// TerrainSystem.ApplyBrush (Shift kernel) with ONE generated mask texture per tile; see
// docs/RESEARCH-NOTES-ADDENDUM.md "Direct terraform". This file is the engine-free part:
//  * the desired ground after terraform (later layers/items win, a smooth bank outside the rings),
//  * the per-texel height change for a pass (raise and lower masks + the brush strength),
//  * the soft-edged paint coverage of a polygon,
//  * square tiling of a raster for the brush (the game maps one brush texture onto a square).
// Pure so it can be unit tested; the game layer only samples the terrain and uploads textures.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public sealed class TerraformRaster
    {
        /// <summary>World xz of the lower-left corner of texel (0,0).</summary>
        public float2 Origin;
        public float Cell;
        public int Width, Height;
        /// <summary>Target height of the nearest/covering terraform item; NaN where the ground stays.</summary>
        public float[] Target;
        /// <summary>1 inside a ring, 0..1 in the bank outside (blend toward the target), 0 elsewhere.</summary>
        public float[] Weight;

        public int Count => Width * Height;

        public float2 Center(int x, int y) => Origin + new float2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

        /// <summary>Desired ground at texel i given the current ground there.</summary>
        public float Desired(int i, float current)
        {
            float t = Target[i], w = Weight[i];
            if (!math.isfinite(t) || w <= 0f) return current;
            return math.lerp(current, t, w);
        }

        /// <summary>
        /// Ground after terraform at a world point: bilinear blend of the four nearest texels' desired
        /// heights (each blended with <paramref name="original"/> by its weight). False when no texel
        /// around p is touched by terraform (callers then use the real ground).
        /// </summary>
        public bool TrySample(float2 p, Func<float2, float> original, out float h)
        {
            h = 0;
            if (Target == null || Width == 0) return false;
            float fx = (p.x - Origin.x) / Cell - 0.5f, fy = (p.y - Origin.y) / Cell - 0.5f;
            int x0 = (int)math.floor(fx), y0 = (int)math.floor(fy);
            float tx = fx - x0, ty = fy - y0;
            bool any = false;
            float sum = 0, wsum = 0;
            float g = original != null ? original(p) : 0f;
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    float bw = (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty);
                    if (bw <= 0) continue;
                    float v = g;
                    if (x >= 0 && y >= 0 && x < Width && y < Height)
                    {
                        int i = y * Width + x;
                        if (math.isfinite(Target[i]) && Weight[i] > 0f) { any = true; v = math.lerp(g, Target[i], Weight[i]); }
                    }
                    sum += v * bw; wsum += bw;
                }
            if (!any || wsum <= 0) return false;
            h = sum / wsum;
            return true;
        }

        /// <summary>
        /// Rasterises terraform items. Later layers win, and within a layer later items win (the same
        /// rule as TerraformDabs.Merge / TerrainField). Outside the union of rings the ground blends
        /// back to the original over <paramref name="bank"/> metres (smoothstep). Null when nothing
        /// valid is given. The cell grows when the area would exceed <paramref name="maxCells"/>.
        /// (s20) <paramref name="vertexHeights"/>: optional, parallel to <paramref name="items"/>; a non-null entry makes
        /// that item SLOPED (target per texel = TerraformSurface mean-value interpolation of the ring's vertex heights).
        /// </summary>
        public static TerraformRaster Build(IReadOnlyList<(float2[] ring, float target, int layer)> items, float cell, float bank, int maxCells = 1 << 22,
            IReadOnlyList<float[]> vertexHeights = null, Func<float2, float> original = null)
        {
            if (items == null || items.Count == 0 || !(cell > 0.05f)) return null;
            var order = new List<int>();
            float2 min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < items.Count; i++)
            {
                var r = items[i].ring;
                if (r == null || r.Length < 3 || !math.isfinite(items[i].target)) continue;
                order.Add(i);
                foreach (var p in r) { min = math.min(min, p); max = math.max(max, p); }
            }
            if (order.Count == 0) return null;
            // Stable by layer: plan order inside a layer is kept.
            order.Sort((a, b) => items[a].layer != items[b].layer ? items[a].layer.CompareTo(items[b].layer) : a.CompareTo(b));
            bank = math.max(0f, bank);
            min -= bank + cell; max += bank + cell;
            float2 size = max - min;
            while ((long)math.ceil(size.x / cell) * (long)math.ceil(size.y / cell) > maxCells) cell *= 1.25f;
            var r0 = new TerraformRaster
            {
                Origin = min, Cell = cell,
                Width = math.max(1, (int)math.ceil(size.x / cell)),
                Height = math.max(1, (int)math.ceil(size.y / cell)),
            };
            int n = r0.Count;
            r0.Target = new float[n];
            r0.Weight = new float[n];
            for (int i = 0; i < n; i++) r0.Target[i] = float.NaN;
            var slopedTex = new bool[n];
            // 1) Inside: later items overwrite.
            foreach (int k in order)
            {
                var ring = items[k].ring;
                var vh = vertexHeights != null && k < vertexHeights.Count ? vertexHeights[k] : null;
                float3[] ring3 = null;
                if (vh != null && vh.Length == ring.Length)
                {
                    ring3 = new float3[ring.Length];
                    for (int v = 0; v < ring.Length; v++) ring3[v] = new float3(ring[v].x, vh[v], ring[v].y);
                }
                float2 a = ring[0], b = ring[0];
                foreach (var p in ring) { a = math.min(a, p); b = math.max(b, p); }
                int x0 = math.max(0, (int)math.floor((a.x - min.x) / cell)), x1 = math.min(r0.Width - 1, (int)math.ceil((b.x - min.x) / cell));
                int y0 = math.max(0, (int)math.floor((a.y - min.y) / cell)), y1 = math.min(r0.Height - 1, (int)math.ceil((b.y - min.y) / cell));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (ReservedSpace.PointInPolygon(r0.Center(x, y), ring))
                        {
                            int i = y * r0.Width + x;
                            r0.Target[i] = ring3 != null ? TerraformSurface.At(ring3, r0.Center(x, y)) : items[k].target;
                            slopedTex[i] = ring3 != null;
                            r0.Weight[i] = 1f;
                        }
            }
            // 2) Bank: outside texels take the target of the nearest inside texel within the bank width.
            if (bank > 0.01f)
            {
                int reach = (int)math.ceil(bank / cell);
                var target = (float[])r0.Target.Clone();
                for (int y = 0; y < r0.Height; y++)
                    for (int x = 0; x < r0.Width; x++)
                    {
                        int i = y * r0.Width + x;
                        if (r0.Weight[i] >= 1f) continue;
                        float best = float.MaxValue, bestT = float.NaN; int bestJ = -1;
                        for (int dy = -reach; dy <= reach; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= r0.Height) continue;
                            for (int dx = -reach; dx <= reach; dx++)
                            {
                                int xx = x + dx;
                                if (xx < 0 || xx >= r0.Width) continue;
                                int j = yy * r0.Width + xx;
                                if (r0.Weight[j] < 1f) continue;
                                // Distance to the inside texel's edge (half a cell nearer than its centre).
                                float d = math.max(0f, math.sqrt(dx * dx + dy * dy) * cell - 0.5f * cell);
                                if (d < best) { best = d; bestT = r0.Target[j]; bestJ = j; }
                            }
                        }
                        if (best > bank || !math.isfinite(bestT)) continue;
                        float s = math.saturate(best / bank);
                        // (s20) Around a SLOPED patch the bank carries its change (target − ground), not its absolute height: on a
                        // slope the nearest texel's height belongs to another level and made a lip.
                        if (original != null && bestJ >= 0 && slopedTex[bestJ])
                            bestT = original(r0.Center(x, y)) + (bestT - original(r0.Center(bestJ % r0.Width, bestJ / r0.Width)));
                        target[i] = bestT;
                        r0.Weight[i] = 1f - s * s * (3f - 2f * s);
                    }
                r0.Target = target;
            }
            return r0;
        }

        /// <summary>
        /// Soft-edged coverage (0..1) of a polygon on this raster's grid: 1 deeper than half the
        /// <paramref name="edge"/> inside, 0 half the edge outside, smoothstep between.
        /// </summary>
        public static float[] Coverage(float2[] ring, float2 origin, float cell, int width, int height, float edge)
        {
            var result = new float[width * height];
            if (ring == null || ring.Length < 3) return result;
            float half = math.max(0.01f, edge * 0.5f);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var p = origin + new float2((x + 0.5f) * cell, (y + 0.5f) * cell);
                    float d = TerraformDabs.DistanceToBoundary(ring, p);
                    if (d > half + cell && !ReservedSpace.PointInPolygon(p, ring)) continue;
                    float signed = ReservedSpace.PointInPolygon(p, ring) ? d : -d;
                    float s = math.saturate((signed + half) / (2f * half));
                    result[y * width + x] = s * s * (3f - 2f * s);
                }
            return result;
        }

        // ───────────────────────── shift passes ─────────────────────────

        /// <summary>Residual statistics of a pass (inside texels only count for "done").</summary>
        public struct Residual
        {
            public int Inside, InsideOff;
            public float MaxAbs, Rms;
            /// <summary>(s29) Largest |desired − current| over the inside texels.</summary>
            public float InsideMaxAbs;
            /// <summary>
            /// Every texel within tolerance, or only a small fraction of the inside texels off AND none of them far off.
            /// (s29) Before, "2 % of the texels off" was done whatever they were off by: a lot graded over a 3 m deep bowl
            /// (1 % of its texels) "reached its target in 0 passes" and the bowl stayed under the asphalt (playtest 53).
            /// </summary>
            public bool Done(float tolerance, float allowedOffFraction) =>
                MaxAbs <= tolerance || (InsideOff <= allowedOffFraction * Inside && InsideMaxAbs <= FarOff * tolerance);

            /// <summary>An inside texel this many tolerances off is never "close enough".</summary>
            public const float FarOff = 2.5f;
        }

        /// <summary>
        /// Height change per texel (desired − current) for every touched texel (0 elsewhere), plus
        /// statistics. <paramref name="current"/> is the live ground at a world point.
        /// </summary>
        public float[] Deltas(Func<float2, float> current, float tolerance, out Residual stats)
        {
            stats = default;
            var d = new float[Count];
            double sq = 0; int m = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    if (!math.isfinite(Target[i]) || Weight[i] <= 0f) continue;
                    float g = current(Center(x, y));
                    if (!math.isfinite(g)) continue;
                    float v = Desired(i, g) - g;
                    d[i] = v;
                    sq += v * v; m++;
                    stats.MaxAbs = math.max(stats.MaxAbs, math.abs(v));
                    if (Weight[i] >= 1f)
                    {
                        stats.Inside++;
                        stats.InsideMaxAbs = math.max(stats.InsideMaxAbs, math.abs(v));
                        if (math.abs(v) > tolerance) stats.InsideOff++;
                    }
                }
            stats.Rms = m == 0 ? 0f : (float)math.sqrt(sq / m);
            return d;
        }

        /// <summary>
        /// Where the inside texels off by more than <paramref name="tolerance"/> are: grouped in <paramref name="bin"/> m squares,
        /// the <paramref name="top"/> squares with the most, each with its centre, count and mean change still wanted (+ raise).
        /// </summary>
        public List<(float2 at, int count, float mean)> OffClusters(float[] deltas, float tolerance, float bin = 20f, int top = 6)
        {
            var bins = new Dictionary<long, (int n, float sum, float2 sumAt)>();
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    if (Weight[i] < 1f || math.abs(deltas[i]) <= tolerance) continue;
                    var c = Center(x, y);
                    long k = ((long)(int)math.floor(c.x / bin) << 32) ^ (uint)(int)math.floor(c.y / bin);
                    bins.TryGetValue(k, out var b);
                    bins[k] = (b.n + 1, b.sum + deltas[i], b.sumAt + c);
                }
            var list = new List<(float2, int, float)>();
            foreach (var b in bins.Values) list.Add((b.sumAt / b.n, b.n, b.sum / b.n));
            list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            if (list.Count > top) list.RemoveRange(top, list.Count - top);
            return list;
        }

        /// <summary>A square brush tile: texel window [X, X+Size) × [Y, Y+Size) of the raster.</summary>
        public struct Tile { public int X, Y, Size; }

        /// <summary>Square tiles of at most <paramref name="maxTexels"/> per side covering the raster.</summary>
        public List<Tile> Tiles(int maxTexels)
        {
            maxTexels = math.max(8, maxTexels);
            int side = math.min(maxTexels, math.max(Width, Height));
            var list = new List<Tile>();
            for (int y = 0; y < Height; y += side)
                for (int x = 0; x < Width; x += side)
                    list.Add(new Tile { X = x, Y = y, Size = side });
            return list;
        }

        /// <summary>World centre and side length of a tile.</summary>
        public void TileWorld(Tile t, out float2 center, out float size)
        {
            size = t.Size * Cell;
            center = Origin + new float2(t.X * Cell + size * 0.5f, t.Y * Cell + size * 0.5f);
        }

        /// <summary>
        /// Raise and lower masks (0..1, row-major, row 0 = lowest z) of one tile, scaled by the larger
        /// of the two maxima. <paramref name="flipX"/>/<paramref name="flipY"/> mirror the texture for
        /// the brush orientation the game uses (found by calibration). Returns the scale (metres at
        /// mask 1); 0 when the tile needs no change above <paramref name="minChange"/>.
        /// </summary>
        public static float Masks(float[] deltas, int width, int height, Tile t, bool flipX, bool flipY, float minChange,
            out float[] raise, out float[] lower, out bool anyRaise, out bool anyLower)
        {
            int n = t.Size;
            raise = new float[n * n];
            lower = new float[n * n];
            anyRaise = anyLower = false;
            float max = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx = t.X + x, sy = t.Y + y;
                    if (sx >= width || sy >= height) continue;
                    max = math.max(max, math.abs(deltas[sy * width + sx]));
                }
            if (max < minChange) return 0f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx = t.X + x, sy = t.Y + y;
                    if (sx >= width || sy >= height) continue;
                    float v = deltas[sy * width + sx] / max;
                    int tx = flipX ? n - 1 - x : x, ty = flipY ? n - 1 - y : y;
                    int k = ty * n + tx;
                    if (v > 0) { raise[k] = v; anyRaise = true; }
                    else if (v < 0) { lower[k] = -v; anyLower = true; }
                }
            return max;
        }

        /// <summary>
        /// Brush strength for TerrainSystem.ApplyBrush(Shift) so that mask 1 moves the ground by
        /// <paramref name="metres"/>: change = clamp(dt, 0.05, 1) · strength · toolStrength · mask
        /// (decompiled 1.6.2f1 ApplyToTerrain, height-normalised). Strengths below 0.002 are bent by
        /// the game (pow 0.055), so the strength is kept above and the mask is scaled down instead
        /// (<paramref name="maskScale"/> ≤ 1).
        /// </summary>
        public static float ShiftStrength(float metres, float deltaTime, float toolStrength, out float maskScale)
        {
            float cd = math.clamp(deltaTime, 0.05f, 1f);
            float tool = toolStrength > 0 ? toolStrength : 2000f;
            float strength = metres / (cd * tool);
            const float MinStrength = 0.0021f;
            maskScale = 1f;
            if (strength < MinStrength)
            {
                maskScale = metres / (MinStrength * cd * tool);
                strength = MinStrength;
            }
            return strength;
        }
    }
}
