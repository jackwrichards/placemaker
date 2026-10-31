// Placemaker — Biome mix: where each biome goes (docs/runbooks/biomes.md).
//
// The site's natural ground (inside the outline, off the paths' verges, out of the lake, the plaza and the facilities) is a
// grid of cells (2.5-8 m, ~4000-9000 cells whatever the size). Each offered biome scores every cell:
//   its own noise   domain-warped fBm, one field per biome (seeded), wavelength from Patch size: many small patches .. a few
//                   big areas (at the largest a 50/50 mix is two clear halves)
//   + the land      ridge, hollow (the height against its 20-30 m surroundings), steep, slope, flat, near a path, near the
//                   outline, each weighted by the biome (rock on ridges and steep ground, wetland in hollows, forest on
//                   slopes and belts, meadow and lawn on the open flats by the paths)
// and a cell goes to the best score plus a bias per biome; the biases are solved so each biome gets its share of the cells
// (the player's percentages). Then a majority filter rounds the patches and specks smaller than ~250 m² join their
// neighbour, so the areas read as areas. Hard limits: water and marsh keep off the paths and off steep ground.
// Every cell also knows how deep it lies inside its area (Interior, m), and each biome a soft weight (0..1, 1 inside, fading
// over ~6 m across its edge): the builder fades its rocks and trees to its rim and paints with these fields (no hard lines).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common.Biomes
{
    /// <summary>What the field needs to know about the site.</summary>
    public sealed class BiomeSite
    {
        public LayerContext Ctx;
        public LayerRegion Region;
        /// <summary>The walks (and anything else the biomes keep clear of and border): the host's path corridors.</summary>
        public SegmentIndex Paths;
        /// <summary>Not natural ground: a lake, a plaza, facilities and their clearings, reserved space (stays the host's).</summary>
        public Func<float2, bool> Blocked;
        /// <summary>The verge along every path that stays the host's (mown edge, benches): m beyond the path's half width.</summary>
        public float Verge = 3f;
        /// <summary>Where the builder places its objects (respected, and taken).</summary>
        public Occupancy Occ;
        /// <summary>The player's own species (empty: each biome's own).</summary>
        public string[] Trees = Array.Empty<string>(), Bushes = Array.Empty<string>();
        /// <summary>The host's tree / bush density against its design (1 = as designed; 0 = none).</summary>
        public float TreeAmount = 1f, BushAmount = 1f;
        /// <summary>The host shapes the ground (false: the biomes leave the terrain alone).</summary>
        public bool Shape = true;
        /// <summary>The direction planted rows run (orchards; RowPitch). Zero: the site's long axis.</summary>
        public float2 RowAxis;
        /// <summary>Ground decals on (paint and objects always).</summary>
        public uint Salt = 0xB10Eu;
    }

    public sealed class BiomeField
    {
        public const float MinPatchArea = 250f;
        /// <summary>Score a cell gets for the biome the split before the paths gave it.</summary>
        public const float PriorBonus = 1.6f;

        /// <summary>
        /// (paths) How much a walk would rather go round the ground at p: a pond 25, a marsh's heart 3 (rock fields are crossed: avoiding them pushed the walks onto the fall line on mountains),
        /// a wood 1.15, open ground 1. The park plans this split before its paths and gives it to the path planner.
        /// </summary>
        public float AvoidAt(float2 p)
        {
            var b = BiomeAt(p);
            if (b == null || b.Host || b.Open) return 1f;
            float deep = InteriorAt(p);
            if (b.Water) return deep > 0.5f ? 25f : 4f;
            if (b.Pools > 0f || b.Dip > 0f) return deep > 2f ? 3f : 1.5f;
            if (b.TreesPerHa >= 150f) return 1.15f;
            return 1f;
        }
        /// <summary>Soft weights fade across an edge over ±this (m).</summary>
        public const float Fade = 6f;

        public float2 Origin;
        public float Cell;
        public int Nx, Ny;
        public readonly List<BiomeDef> Biomes = new List<BiomeDef>();
        /// <summary>Per cell: index into Biomes, −1 = not natural ground (outside, verge, blocked).</summary>
        public sbyte[] Label;
        /// <summary>Per cell: how deep inside its own area (m, ≥ 0); 0 off the natural ground.</summary>
        public float[] Interior;
        /// <summary>Per biome, per cell: soft weight 0..1.</summary>
        public float[][] Weight;
        public float[] Height, Grade, Relief, PathDist;
        /// <summary>Per biome: the share of the natural ground it got (0..1), and the share asked.</summary>
        public float[] Got, Asked;
        public int NaturalCells;
        public float Wavelength;

        public int Count => Nx * Ny;
        public float2 Pos(int k) => Origin + new float2(k % Nx, k / Nx) * Cell;
        public float CellArea => Cell * Cell;

        public int IndexOf(string id) { for (int i = 0; i < Biomes.Count; i++) if (Biomes[i].Id == id) return i; return -1; }

        private int CellOf(float2 p)
        {
            int i = (int)math.round((p.x - Origin.x) / Cell), j = (int)math.round((p.y - Origin.y) / Cell);
            if (i < 0 || j < 0 || i >= Nx || j >= Ny) return -1;
            return j * Nx + i;
        }

        /// <summary>The biome index at p (−1: not natural ground).</summary>
        public int LabelAt(float2 p) { int k = CellOf(p); return k < 0 ? -1 : Label[k]; }
        public BiomeDef BiomeAt(float2 p) { int l = LabelAt(p); return l < 0 ? null : Biomes[l]; }

        /// <summary>How deep inside its area p lies (m, bilinear; 0 off the natural ground).</summary>
        public float InteriorAt(float2 p) => Bilinear(Interior, p);

        /// <summary>Soft weight of biome <paramref name="b"/> at p (0..1, bilinear).</summary>
        public float WeightAt(int b, float2 p) => b < 0 || b >= Weight.Length ? 0f : Bilinear(Weight[b], p);

        private float Bilinear(float[] a, float2 p)
        {
            float fx = (p.x - Origin.x) / Cell, fy = (p.y - Origin.y) / Cell;
            int i = (int)math.floor(fx), j = (int)math.floor(fy);
            if (i < 0 || j < 0 || i >= Nx - 1 || j >= Ny - 1) { int k = CellOf(p); return k < 0 ? 0f : a[k]; }
            float tx = fx - i, ty = fy - j;
            int k0 = j * Nx + i;
            return math.lerp(math.lerp(a[k0], a[k0 + 1], tx), math.lerp(a[k0 + Nx], a[k0 + Nx + 1], tx), ty);
        }

        // ───────────────────────── building ─────────────────────────

        /// <summary>Splits the site's natural ground between the biomes by their shares.</summary>
        public static BiomeField Build(BiomeSite site, List<(BiomeDef biome, float share)> shares, float scale, uint seed, BiomeField prior = null)
        {
            var f = new BiomeField();
            var region = site.Region;
            float area = math.max(1f, region.Area);
            f.Cell = math.clamp(math.sqrt(area / 7000f), 2.5f, 8f);
            f.Origin = region.Min + f.Cell * 0.5f;
            f.Nx = math.max(2, (int)math.ceil((region.Max.x - region.Min.x) / f.Cell));
            f.Ny = math.max(2, (int)math.ceil((region.Max.y - region.Min.y) / f.Cell));
            int n = f.Count;
            foreach (var (b, _) in shares) f.Biomes.Add(b);
            int K = f.Biomes.Count;
            f.Label = new sbyte[n];
            f.Interior = new float[n];
            f.Height = new float[n]; f.Grade = new float[n]; f.Relief = new float[n]; f.PathDist = new float[n];
            f.Got = new float[K]; f.Asked = new float[K];
            for (int b = 0; b < K; b++) f.Asked[b] = shares[b].share;
            var ctx = site.Ctx;

            // 1. the natural ground and the land
            var natural = new bool[n];
            var inside = new bool[n];
            // (perf) distances: exact within 8 m (the verge, the keep-offs), a distance transform beyond (a 40 m search per cell
            // was most of the field's time)
            const float near = 8f;
            bool anyPath = site.Paths != null && site.Paths.Count > 0;
            var edge = new float[n];
            for (int k = 0; k < n; k++)
            {
                var p = f.Pos(k);
                f.Height[k] = ctx != null ? ctx.H(p) : 0f;
                f.Label[k] = -1;
                inside[k] = region.Contains(p);
                f.PathDist[k] = anyPath ? site.Paths.Clearance(p, near) : 1e6f;
                edge[k] = inside[k] ? region.BoundaryDistance(p, near) : 0f;
            }
            Spread(f, f.PathDist, near);
            Spread(f, edge, near);
            for (int k = 0; k < n; k++)
            {
                if (!inside[k] || f.PathDist[k] < site.Verge) continue;
                if (site.Blocked != null && site.Blocked(f.Pos(k))) continue;
                natural[k] = true;
            }
            for (int j = 0; j < f.Ny; j++)
                for (int i = 0; i < f.Nx; i++)
                {
                    int k = j * f.Nx + i;
                    int il = math.max(0, i - 1), ir = math.min(f.Nx - 1, i + 1), jd = math.max(0, j - 1), ju = math.min(f.Ny - 1, j + 1);
                    float gx = (f.Height[j * f.Nx + ir] - f.Height[j * f.Nx + il]) / math.max(f.Cell, (ir - il) * f.Cell);
                    float gy = (f.Height[ju * f.Nx + i] - f.Height[jd * f.Nx + i]) / math.max(f.Cell, (ju - jd) * f.Cell);
                    f.Grade[k] = math.sqrt(gx * gx + gy * gy);
                }
            // relief: the height against its surroundings (a box mean over ~25 m), + = knoll / ridge, − = hollow
            int rad = math.max(2, (int)math.round(12f / f.Cell));
            var mean = BoxMean(f.Height, f.Nx, f.Ny, rad);
            for (int k = 0; k < n; k++) f.Relief[k] = f.Height[k] - mean[k];

            int nat = 0;
            for (int k = 0; k < n; k++) if (natural[k]) nat++;
            f.NaturalCells = nat;
            f.Weight = new float[K][];
            for (int b = 0; b < K; b++) f.Weight[b] = new float[n];
            if (nat == 0 || K == 0) return f;

            // 2. the scores: noise + land
            float lmin = 18f, lmax = 1.1f * math.sqrt(area) + 20f;
            f.Wavelength = lmin * math.pow(math.max(1.01f, lmax / lmin), math.saturate(scale));
            var score = new float[K][];
            for (int b = 0; b < K; b++)
            {
                var d = f.Biomes[b];
                var s = score[b] = new float[n];
                uint bs = Hashing.Mix(seed, Seeds.Hash("biome:" + d.Id));
                // (perf) the noise on a lattice of an eighth of its wavelength, read bilinearly (its finest detail is ~λ/8)
                float step = math.max(f.Cell, f.Wavelength / 8f);
                int lx = (int)math.ceil((f.Nx - 1) * f.Cell / step) + 2, ly = (int)math.ceil((f.Ny - 1) * f.Cell / step) + 2;
                var lat = new float[lx * ly];
                for (int j = 0; j < ly; j++) for (int i = 0; i < lx; i++) lat[j * lx + i] = Warped((f.Origin + new float2(i, j) * step) / f.Wavelength, bs);
                float Noise(float2 q)
                {
                    var g = (q - f.Origin) / step;
                    int i0 = math.clamp((int)g.x, 0, lx - 2), j0 = math.clamp((int)g.y, 0, ly - 2);
                    float tx = math.saturate(g.x - i0), ty = math.saturate(g.y - j0);
                    return math.lerp(math.lerp(lat[j0 * lx + i0], lat[j0 * lx + i0 + 1], tx), math.lerp(lat[(j0 + 1) * lx + i0], lat[(j0 + 1) * lx + i0 + 1], tx), ty);
                }
                for (int k = 0; k < n; k++)
                {
                    if (!natural[k]) { s[k] = float.NegativeInfinity; continue; }
                    var p = f.Pos(k);
                    // the keep-off distance wanders (a pond's shore is never a line drawn along the walk)
                    float keep = d.PathKeep > 0f ? d.PathKeep * (0.7f + 0.9f * ValueNoise.Sample(p / 14f, Hashing.Mix(bs, 0x6Bu))) : 0f;
                    if (keep > 0f && f.PathDist[k] < keep || d.MaxGrade > 0f && f.Grade[k] > d.MaxGrade) { s[k] = float.NegativeInfinity; continue; }
                    float v = (Noise(p) - 0.5f) * 3.2f;
                    float g = f.Grade[k];
                    float rel = math.clamp(f.Relief[k] / 1.6f, -1f, 1f);
                    v += d.Ridge * math.max(0f, rel) + d.Hollow * math.max(0f, -rel);
                    v += d.Steep * math.saturate((g - 0.12f) / 0.12f);
                    v += d.Slope * math.saturate((g - 0.03f) / 0.05f) * (1f - math.saturate((g - 0.16f) / 0.08f));
                    v += d.Flat * (1f - math.saturate(g / 0.05f));
                    v += d.NearPath * (1f - math.saturate((f.PathDist[k] - site.Verge) / 22f));
                    v += d.NearEdge * (1f - math.saturate(edge[k] / 28f));
                    // the split planned before the paths (the walks went round its water, marsh and rock): its areas stay put
                    if (prior != null && prior.BiomeAt(p) == d) v += PriorBonus;
                    s[k] = v;
                }
            }

            // 3. the biases: each biome gets its share of the cells
            var bias = new float[K];
            var count = new int[K];
            for (int it = 0; it < 90; it++)
            {
                Assign(f, natural, score, bias, count);
                float eta = 1.6f * (1f - it / 100f);
                for (int b = 0; b < K; b++) bias[b] += eta * (f.Asked[b] - count[b] / (float)nat);
            }
            Assign(f, natural, score, bias, count);

            // 4. round the patches: a majority filter, then specks join their neighbour
            // specks: at least 250 m², and on a big site a share of it (a 10 ha park keeps no patch under ~1200 m²: confetti otherwise)
            int minCells = math.max(3, (int)math.ceil(math.max(MinPatchArea, nat * f.CellArea * 0.012f) / f.CellArea));
            for (int pass = 0; pass < 3; pass++) Majority(f, natural, score);
            Specks(f, natural, minCells, score);

            // 5. depth inside each area, soft weights
            for (int b = 0; b < K; b++)
            {
                var sd = SignedDistance(f, natural, b);
                var w = f.Weight[b];
                for (int k = 0; k < n; k++)
                {
                    w[k] = natural[k] ? Smooth01((sd[k] + Fade) / (2f * Fade)) : 0f;
                    if (f.Label[k] == b) f.Interior[k] = math.max(0f, sd[k]);
                }
            }
            for (int b = 0; b < K; b++) count[b] = 0;
            for (int k = 0; k < n; k++) if (f.Label[k] >= 0) count[f.Label[k]]++;
            for (int b = 0; b < K; b++) f.Got[b] = count[b] / (float)nat;
            return f;
        }

        /// <summary>Distances known exactly below <paramref name="known"/> m, carried on beyond by a chamfer transform (≈ m).</summary>
        private static void Spread(BiomeField f, float[] d, float known)
        {
            int nx = f.Nx, ny = f.Ny;
            float a = f.Cell, c = f.Cell * 1.4142f;
            for (int k = 0; k < d.Length; k++) if (d[k] >= known) d[k] = 1e6f;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i; float v = d[k];
                    if (i > 0) v = math.min(v, d[k - 1] + a);
                    if (j > 0) v = math.min(v, d[k - nx] + a);
                    if (i > 0 && j > 0) v = math.min(v, d[k - nx - 1] + c);
                    if (i < nx - 1 && j > 0) v = math.min(v, d[k - nx + 1] + c);
                    d[k] = v;
                }
            for (int j = ny - 1; j >= 0; j--)
                for (int i = nx - 1; i >= 0; i--)
                {
                    int k = j * nx + i; float v = d[k];
                    if (i < nx - 1) v = math.min(v, d[k + 1] + a);
                    if (j < ny - 1) v = math.min(v, d[k + nx] + a);
                    if (i < nx - 1 && j < ny - 1) v = math.min(v, d[k + nx + 1] + c);
                    if (i > 0 && j < ny - 1) v = math.min(v, d[k + nx - 1] + c);
                    d[k] = math.min(v, 1e5f);
                }
        }

        private static void Assign(BiomeField f, bool[] natural, float[][] score, float[] bias, int[] count)
        {
            int K = score.Length;
            for (int b = 0; b < K; b++) count[b] = 0;
            for (int k = 0; k < f.Count; k++)
            {
                if (!natural[k]) { f.Label[k] = -1; continue; }
                int best = -1; float bv = float.NegativeInfinity;
                for (int b = 0; b < K; b++)
                {
                    float v = score[b][k];
                    if (float.IsNegativeInfinity(v)) continue;
                    v += bias[b];
                    if (v > bv) { bv = v; best = b; }
                }
                if (best < 0) best = Fallback(f);
                f.Label[k] = (sbyte)best;
                if (best >= 0) count[best]++;
            }
        }

        /// <summary>A cell no biome may take (a path verge for water, steep for marsh): the host's ground if offered, else the first.</summary>
        private static int Fallback(BiomeField f)
        {
            for (int b = 0; b < f.Biomes.Count; b++) if (f.Biomes[b].Host) return b;
            for (int b = 0; b < f.Biomes.Count; b++) if (f.Biomes[b].PathKeep <= 0f && f.Biomes[b].MaxGrade <= 0f) return b;
            return -1;
        }

        private static bool Allowed(float[][] score, int b, int k) => !float.IsNegativeInfinity(score[b][k]);

        private static void Majority(BiomeField f, bool[] natural, float[][] score)
        {
            int K = f.Biomes.Count;
            var next = (sbyte[])f.Label.Clone();
            var votes = new int[K];
            for (int j = 0; j < f.Ny; j++)
                for (int i = 0; i < f.Nx; i++)
                {
                    int k = j * f.Nx + i;
                    if (!natural[k]) continue;
                    Array.Clear(votes, 0, K);
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ii = i + di, jj = j + dj;
                            if (ii < 0 || jj < 0 || ii >= f.Nx || jj >= f.Ny) continue;
                            int l = f.Label[jj * f.Nx + ii];
                            if (l >= 0) votes[l]++;
                        }
                    int cur = f.Label[k], best = cur;
                    for (int b = 0; b < K; b++) if (votes[b] > (best >= 0 ? votes[best] : 0) && Allowed(score, b, k)) best = b;
                    if (best >= 0 && votes[best] >= 5) next[k] = (sbyte)best;
                }
            Array.Copy(next, f.Label, next.Length);
        }

        /// <summary>Connected patches smaller than <paramref name="minCells"/> join the neighbour they touch most.</summary>
        private static void Specks(BiomeField f, bool[] natural, int minCells, float[][] score)
        {
            int n = f.Count, K = f.Biomes.Count;
            var comp = new int[n];
            for (int k = 0; k < n; k++) comp[k] = -1;
            var stack = new Stack<int>();
            var cells = new List<int>();
            var touch = new int[K];
            int id = 0;
            for (int s = 0; s < n; s++)
            {
                if (!natural[s] || comp[s] >= 0) continue;
                int l = f.Label[s];
                cells.Clear(); Array.Clear(touch, 0, K);
                stack.Push(s); comp[s] = id;
                while (stack.Count > 0)
                {
                    int k = stack.Pop(); cells.Add(k);
                    int i = k % f.Nx, j = k / f.Nx;
                    for (int d = 0; d < 4; d++)
                    {
                        int ii = i + (d == 0 ? 1 : d == 1 ? -1 : 0), jj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (ii < 0 || jj < 0 || ii >= f.Nx || jj >= f.Ny) continue;
                        int q = jj * f.Nx + ii;
                        if (!natural[q]) continue;
                        if (f.Label[q] == l) { if (comp[q] < 0) { comp[q] = id; stack.Push(q); } }
                        else if (f.Label[q] >= 0) touch[f.Label[q]]++;
                    }
                }
                id++;
                if (cells.Count >= minCells) continue;
                int best = -1;
                for (int b = 0; b < K; b++) if (touch[b] > 0 && (best < 0 || touch[b] > touch[best])) best = b;
                if (best < 0) continue;
                bool ok = true;
                foreach (var c in cells) if (!Allowed(score, best, c)) { ok = false; break; }
                if (!ok) continue;
                foreach (var c in cells) f.Label[c] = (sbyte)best;
            }
        }

        /// <summary>Signed distance (m) to biome b's edge: + inside, − outside (chamfer 3-4).</summary>
        private static float[] SignedDistance(BiomeField f, bool[] natural, int b)
        {
            int n = f.Count;
            var din = Chamfer(f, k => natural[k] && f.Label[k] == b);
            var dout = Chamfer(f, k => !(natural[k] && f.Label[k] == b));
            var sd = new float[n];
            for (int k = 0; k < n; k++) sd[k] = natural[k] && f.Label[k] == b ? din[k] - f.Cell * 0.5f : -(dout[k] - f.Cell * 0.5f);
            return sd;
        }

        /// <summary>Distance (m) from each cell where <paramref name="inside"/> holds to the nearest cell where it does not (0 there).</summary>
        private static float[] Chamfer(BiomeField f, Func<int, bool> inside)
        {
            int nx = f.Nx, ny = f.Ny, n = f.Count;
            const float big = 1e6f;
            var d = new float[n];
            for (int k = 0; k < n; k++) d[k] = inside(k) ? big : 0f;
            float a = 1f, c = 1.4142f;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i; if (d[k] == 0f) continue;
                    float v = d[k];
                    if (i > 0) v = math.min(v, d[k - 1] + a);
                    if (j > 0) v = math.min(v, d[k - nx] + a);
                    if (i > 0 && j > 0) v = math.min(v, d[k - nx - 1] + c);
                    if (i < nx - 1 && j > 0) v = math.min(v, d[k - nx + 1] + c);
                    if (i == 0 || j == 0 || i == nx - 1) v = math.min(v, 1f);   // the grid's edge counts as outside
                    d[k] = v;
                }
            for (int j = ny - 1; j >= 0; j--)
                for (int i = nx - 1; i >= 0; i--)
                {
                    int k = j * nx + i; if (d[k] == 0f) continue;
                    float v = d[k];
                    if (i < nx - 1) v = math.min(v, d[k + 1] + a);
                    if (j < ny - 1) v = math.min(v, d[k + nx] + a);
                    if (i < nx - 1 && j < ny - 1) v = math.min(v, d[k + nx + 1] + c);
                    if (i > 0 && j < ny - 1) v = math.min(v, d[k + nx - 1] + c);
                    if (j == ny - 1) v = math.min(v, 1f);
                    d[k] = v;
                }
            for (int k = 0; k < n; k++) d[k] *= f.Cell;
            return d;
        }

        private static float[] BoxMean(float[] h, int nx, int ny, int r)
        {
            var tmp = new float[h.Length];
            var o = new float[h.Length];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float s = 0f; int c = 0;
                    for (int d = -r; d <= r; d++) { int ii = i + d; if (ii < 0 || ii >= nx) continue; s += h[j * nx + ii]; c++; }
                    tmp[j * nx + i] = s / math.max(1, c);
                }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float s = 0f; int c = 0;
                    for (int d = -r; d <= r; d++) { int jj = j + d; if (jj < 0 || jj >= ny) continue; s += tmp[jj * nx + i]; c++; }
                    o[j * nx + i] = s / math.max(1, c);
                }
            return o;
        }

        // ───────────────────────── noise ─────────────────────────

        /// <summary>fBm, 4 octaves, about 0..1.</summary>
        public static float Fbm(float2 q, uint seed)
        {
            float v = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < 4; o++)
            {
                v += amp * ValueNoise.Sample(q + o * 31.7f, Hashing.Mix(seed, (uint)o));
                norm += amp; amp *= 0.5f; q *= 2.03f;
            }
            return v / norm;
        }

        /// <summary>Domain-warped fBm (organic, flowing shapes), about 0..1.</summary>
        public static float Warped(float2 q, uint seed)
        {
            var w = new float2(Fbm(q * 0.7f + 5.2f, Hashing.Mix(seed, 0xA1u)), Fbm(q * 0.7f + 1.3f, Hashing.Mix(seed, 0xB2u))) - 0.5f;
            float v = Fbm(q + w * 1.6f, seed);
            // stretch the middle out: value noise sits near 0.5, the areas need contrast
            return math.saturate(0.5f + (v - 0.5f) * 2.2f);
        }

        public static float Smooth01(float t) { t = math.saturate(t); return t * t * (3f - 2f * t); }

        // ───────────────────────── outlines ─────────────────────────

        /// <summary>
        /// Biome b's areas as outlines: outer rings (counter-clockwise) and holes (clockwise), traced along the cells' edges,
        /// rounded (Chaikin) and thinned. <paramref name="smooth"/> rounding passes.
        /// </summary>
        public void Outlines(int b, List<float2[]> outers, List<float2[]> holes, int smooth = 3, float tolerance = 0.6f) =>
            Outlines(k => Label[k] == b, outers, holes, smooth, tolerance);

        /// <summary>The outlines of the cells where <paramref name="inside"/> holds (see the other overload).</summary>
        public void Outlines(Func<int, bool> inside, List<float2[]> outers, List<float2[]> holes, int smooth = 3, float tolerance = 0.6f)
        {
            Func<int, int, bool> In = (i, j) => i >= 0 && j >= 0 && i < Nx && j < Ny && inside(j * Nx + i);
            // directed cell edges with the area on their left, keyed by start vertex (vertex (i,j) = the cells' lower-left corner)
            var next = new Dictionary<long, List<long>>();
            long V(int i, int j) => ((long)(i + 1) << 32) | (uint)(j + 1);
            void E(int i0, int j0, int i1, int j1)
            {
                long a = V(i0, j0);
                if (!next.TryGetValue(a, out var l)) next[a] = l = new List<long>(1);
                l.Add(V(i1, j1));
            }
            for (int j = 0; j < Ny; j++)
                for (int i = 0; i < Nx; i++)
                {
                    if (!In(i, j)) continue;
                    if (!In(i, j - 1)) E(i, j, i + 1, j);
                    if (!In(i + 1, j)) E(i + 1, j, i + 1, j + 1);
                    if (!In(i, j + 1)) E(i + 1, j + 1, i, j + 1);
                    if (!In(i - 1, j)) E(i, j + 1, i, j);
                }
            float2 W(long v) => Origin + (new float2((int)(v >> 32) - 1, (int)(uint)v - 1) - 0.5f) * Cell;
            while (next.Count > 0)
            {
                long start = 0; foreach (var kv in next) { start = kv.Key; break; }
                var loop = new List<float2>();
                long prev = start, cur = start;
                float2 dirIn = new float2(1, 0);
                int guard = 0;
                while (guard++ < 200000)
                {
                    if (!next.TryGetValue(cur, out var outs) || outs.Count == 0) break;
                    int pick = 0;
                    if (outs.Count > 1)
                    {
                        // a saddle: turn left (keeps diagonal cells of the area apart, consistently)
                        float best = float.NegativeInfinity;
                        for (int q = 0; q < outs.Count; q++)
                        {
                            var d = W(outs[q]) - W(cur);
                            float cross = dirIn.x * d.y - dirIn.y * d.x;
                            if (cross > best) { best = cross; pick = q; }
                        }
                    }
                    long to = outs[pick];
                    outs.RemoveAt(pick);
                    if (outs.Count == 0) next.Remove(cur);
                    loop.Add(W(cur));
                    dirIn = math.normalizesafe(W(to) - W(cur));
                    prev = cur; cur = to;
                    if (cur == start) break;
                }
                if (loop.Count < 4) continue;
                var ring = Simplify(Collinear(loop), 0.01f);
                for (int s = 0; s < smooth; s++) ring = Chaikin(ring);
                ring = Simplify(ring, tolerance);
                if (ring.Length < 3) continue;
                float a = Poly.SignedArea(ring);
                if (math.abs(a) < CellArea * 1.5f) continue;
                (a > 0f ? outers : holes).Add(ring);
            }
        }

        private static float2[] Collinear(List<float2> pts)
        {
            var o = new List<float2>(pts.Count);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var a = pts[(i - 1 + n) % n]; var b = pts[i]; var c = pts[(i + 1) % n];
                var d1 = b - a; var d2 = c - b;
                if (math.abs(d1.x * d2.y - d1.y * d2.x) > 1e-4f) o.Add(b);
            }
            return o.ToArray();
        }

        private static float2[] Chaikin(float2[] r)
        {
            int n = r.Length;
            var o = new float2[n * 2];
            for (int i = 0; i < n; i++)
            {
                var a = r[i]; var b = r[(i + 1) % n];
                o[2 * i] = math.lerp(a, b, 0.25f);
                o[2 * i + 1] = math.lerp(a, b, 0.75f);
            }
            return o;
        }

        /// <summary>Closed-ring Douglas-Peucker.</summary>
        private static float2[] Simplify(float2[] r, float tol)
        {
            int n = r.Length;
            if (n < 8 || tol <= 0f) return r;
            var keep = new bool[n];
            keep[0] = true; keep[n / 2] = true;
            void Dp(int a, int b)
            {
                if (b - a < 2) return;
                float best = -1f; int idx = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = SegmentIndex.DistSq(r[i], r[a % n], r[b % n]);
                    if (d > best) { best = d; idx = i; }
                }
                if (best > tol * tol) { keep[idx % n] = true; Dp(a, idx); Dp(idx, b); }
            }
            Dp(0, n / 2); Dp(n / 2, n);
            var o = new List<float2>();
            for (int i = 0; i < n; i++) if (keep[i]) o.Add(r[i]);
            return o.ToArray();
        }
    }
}
