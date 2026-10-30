// Placemaker — dynamic decals: brushes, one call that lays many shared-library pieces organically (docs/DECALS-PLAYBOOK.md
// sections 3 and 7, docs/DYNAMIC-DECALS.md "Brushes"). GPL-3.0.
//
// A builder never places single decals; it calls a brush with a shape and a cause:
//   Scatter     patches by a cause field over an area: clumps of clumps thinning away from the cause, loners between
//   Tone        big faint patches laid over a whole area, overlapping, so no swath is untouched and none is a pattern
//   EdgeStrip   a repeating strip end to end along a seam (path edge, kerb, fence, court edge), broken into runs that
//               start and end in faded caps, cut at corners
//   CracksAlong chain pieces end to end along a line (their main crack joins at the middle of each short side)
//   Fill        overlapping pieces fill a hollow's outline (a puddle in the real low point)
//   Under       patches under and downwind of canopies (leaves), heavier where crowns overlap
//   At          a hot spot at a feature (gate, goalmouth, bench feet): a main piece and satellites skewed one way
// Rules every brush follows (section 7): overlap, never a grid or even spacing, neighbours never the same variant, any
// turn for patches (strips follow their line), scale 0.7-1.4, opacity 0.6-1.0, colour level chosen per site.
// Pure and deterministic from the context's seed and the order of calls.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    /// <summary>Which colour level of a kind to use (the library gives full colour at even indices, toned down at odd).</summary>
    public enum ColourLevel : byte { Any = 0, Full = 1, Quiet = 2 }

    /// <summary>A small, fast, deterministic random source for brushes.</summary>
    public struct BrushRng
    {
        private uint _s;
        public BrushRng(uint seed) { _s = Hashing.NonZero(Hashing.Mix(seed)); }
        public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
        /// <summary>0..1.</summary>
        public float F() => (Next() >> 8) * (1f / 16777216f);
        public float R(float a, float b) => a + (b - a) * F();
        public float R(float2 r) => R(r.x, r.y);
        public int I(int n) => n <= 1 ? 0 : (int)(Next() % (uint)n);
        /// <summary>A standard normal number (Box-Muller).</summary>
        public float Gauss() { float u = math.max(1e-6f, F()), v = F(); return math.sqrt(-2f * math.log(u)) * math.cos(2f * math.PI * v); }
        public float2 Disc() { float a = F() * 2f * math.PI, r = math.sqrt(F()); return new float2(math.cos(a), math.sin(a)) * r; }
    }

    /// <summary>Where a brush may put its pieces: outer rings minus holes (centres are tested; pieces fade over their edges).</summary>
    public sealed class BrushArea
    {
        public readonly List<float2[]> Outer = new List<float2[]>();
        public readonly List<float2[]> Holes = new List<float2[]>();
        /// <summary>Optional extra test (inside the site, off a pitch …).</summary>
        public Func<float2, bool> Keep;
        /// <summary>Optional box the area is cut to (a brush near a few crowns never samples the whole site).</summary>
        public float2 ClipLo = new float2(float.MinValue), ClipHi = new float2(float.MaxValue);
        private float _area = -1f;

        public static BrushArea Of(float2[] ring, IEnumerable<float2[]> holes = null)
        {
            var a = new BrushArea();
            if (ring != null && ring.Length >= 3) a.Outer.Add(ring);
            if (holes != null) foreach (var h in holes) if (h != null && h.Length >= 3) a.Holes.Add(h);
            return a;
        }

        public bool IsEmpty => Outer.Count == 0;

        public void Bounds(out float2 lo, out float2 hi)
        {
            lo = new float2(float.MaxValue); hi = new float2(float.MinValue);
            foreach (var r in Outer) { Poly.Bounds(r, out var a, out var b); lo = math.min(lo, a); hi = math.max(hi, b); }
            if (Outer.Count == 0) lo = hi = float2.zero;
            lo = math.max(lo, ClipLo); hi = math.max(lo, math.min(hi, ClipHi));
        }

        public bool Contains(float2 p)
        {
            if (math.any(p < ClipLo) || math.any(p > ClipHi)) return false;
            bool inside = false;
            foreach (var r in Outer) if (Poly.Contains(r, p)) { inside = true; break; }
            if (!inside) return false;
            foreach (var h in Holes) if (Poly.Contains(h, p)) return false;
            return Keep == null || Keep(p);
        }

        /// <summary>Metres from p to the nearest edge (outer or hole).</summary>
        public float EdgeDistance(float2 p)
        {
            float d = float.MaxValue;
            foreach (var r in Outer) d = math.min(d, Poly.DistanceToBoundary(r, p));
            foreach (var h in Holes) d = math.min(d, Poly.DistanceToBoundary(h, p));
            return d;
        }

        /// <summary>Area in m² (sampled on a grid, holes and Keep included).</summary>
        public float Area()
        {
            if (_area >= 0f) return _area;
            Bounds(out var lo, out var hi);
            var ext = hi - lo;
            float cell = math.max(1f, math.sqrt(ext.x * ext.y / 6000f));
            int n = 0;
            for (float z = lo.y + cell * 0.5f; z < hi.y; z += cell)
                for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                    if (Contains(new float2(x, z))) n++;
            return _area = n * cell * cell;
        }
    }

    /// <summary>The state every brush call of one plan shares: the library, the seed, the output, the budget.</summary>
    public sealed class BrushContext
    {
        public readonly DecalLibrary Lib;
        public readonly uint Seed;
        public readonly List<DecalInstance> Out;
        /// <summary>Most instances this context may place (the rest of a brush call is dropped).</summary>
        public int Budget = 6000;
        /// <summary>How much of everything (1 = as asked): a huge site is thinned evenly, every cause alike (fewer patches, longer
        /// gaps between strip runs, a looser tone lattice), instead of the last causes getting nothing when the budget runs out.</summary>
        public float Density = 1f;
        /// <summary>Colour level for every call that doesn't choose one.</summary>
        public ColourLevel Level = ColourLevel.Any;
        /// <summary>Fades never used (game runner: the hollow "ring" patches read as donuts, robotic from above).</summary>
        public readonly HashSet<VariantFade> Avoid = new HashSet<VariantFade> { VariantFade.Ring };
        /// <summary>Instances per cause (logs, tests).</summary>
        public readonly Dictionary<string, int> ByCause = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _calls;
        private readonly Dictionary<string, List<(float2 p, int v, float r)>> _near = new Dictionary<string, List<(float2, int, float)>>(StringComparer.Ordinal);

        public BrushContext(DecalLibrary lib, uint seed, List<DecalInstance> into = null)
        {
            Lib = lib; Seed = seed; Out = into ?? new List<DecalInstance>();
        }

        public bool Full => Out.Count >= Budget;

        internal BrushRng Rng(string kind) => new BrushRng(Hashing.Mix(Seed, (int)DecalVariants.Fnv(kind), ++_calls));

        /// <summary>The variants of a kind at a colour level (all of them when the level has none).</summary>
        internal int[] Variants(DecalKind k, ColourLevel level)
        {
            if (level == ColourLevel.Any) level = Level;
            int n = k.Variants.Count;
            var list = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                var dv = DecalVariants.Get(k, i);
                bool quiet = dv.Saturation < 0.95f;
                if (dv.Tile == VariantTiling.None && Avoid.Contains(dv.Fade)) continue;
                if (level == ColourLevel.Any || (level == ColourLevel.Quiet) == quiet) list.Add(i);
            }
            if (list.Count == 0) for (int i = 0; i < n; i++) if (DecalVariants.Get(k, i).Tile != VariantTiling.None || !Avoid.Contains(DecalVariants.Get(k, i).Fade)) list.Add(i);
            if (list.Count == 0) for (int i = 0; i < n; i++) list.Add(i);
            return list.ToArray();
        }

        /// <summary>A variant from <paramref name="pool"/> that differs from the nearest instance of the same kind within reach.</summary>
        internal int Pick(DecalKind k, int[] pool, float2 p, float reach, ref BrushRng r)
        {
            int v = pool[r.I(pool.Length)];
            if (pool.Length < 2 || !_near.TryGetValue(k.Id, out var list)) return v;
            int best = -1; float bd = float.MaxValue;
            for (int i = list.Count - 1, seen = 0; i >= 0 && seen < 400; i--, seen++)
            {
                float d = math.distancesq(list[i].p, p);
                float rr = math.max(reach, list[i].r);
                if (d < rr * rr && d < bd) { bd = d; best = i; }
            }
            if (best < 0 || list[best].v != v) return v;
            int at = Array.IndexOf(pool, v);
            return pool[(at + 1 + r.I(pool.Length - 1)) % pool.Length];
        }

        internal bool Add(DecalKind k, int variant, float2 p, float yaw, float2 scale, float opacity, string cause)
        {
            if (Full || opacity < 0.02f || !math.all(math.isfinite(p))) return false;
            Out.Add(new DecalInstance(k.Id, variant, p, yaw, scale, math.saturate(opacity), cause));
            if (!_near.TryGetValue(k.Id, out var list)) _near[k.Id] = list = new List<(float2, int, float)>();
            var span = DecalVariants.SpanOf(k, variant) * scale;
            list.Add((p, DecalVariants.BaseIndex(k, variant), math.cmax(span) * 0.6f));
            if (cause != null) ByCause[cause] = (ByCause.TryGetValue(cause, out var n) ? n : 0) + 1;
            return true;
        }
    }

    public sealed class ScatterOptions
    {
        /// <summary>Cause field 0..1 (null = 1 everywhere): parents land, children survive and opacity grows with it.</summary>
        public Func<float2, float> Weight;
        /// <summary>Scale range of a clump's main pieces (children shrink away from their parent).</summary>
        public float2 Scale = new float2(0.8f, 1.35f);
        public float2 Opacity = new float2(0.6f, 1f);
        /// <summary>Mean pieces per clump (a parent and its children).</summary>
        public float Clump = 4f;
        /// <summary>Spread of a clump in patch lengths.</summary>
        public float Spread = 0.75f;
        /// <summary>Share of pieces placed alone (thin, even cover between the clumps).</summary>
        public float Loners = 0.3f;
        /// <summary>Metres a centre may lie outside the area (patches fade, so a little spill reads natural).</summary>
        public float Spill = 0f;
        public ColourLevel Level = ColourLevel.Any;
        public string Cause = "scatter";
    }

    public sealed class StripOptions
    {
        /// <summary>+1: the heavy side (+z) on the left of the line's direction of travel; −1: on the right.</summary>
        public int Side = 1;
        /// <summary>Metres the strip's centre line moves toward its heavy side (negative: away).</summary>
        public float Offset;
        /// <summary>Share of the line left bare between runs (0 = one unbroken run per stretch).</summary>
        public float Gaps = 0.25f;
        /// <summary>Length of the runs (m), spread.</summary>
        public float2 Run = new float2(14f, 40f);
        public float2 Opacity = new float2(0.65f, 1f);
        /// <summary>Width stretch (the strip's z) per run.</summary>
        public float2 Width = new float2(0.85f, 1.2f);
        /// <summary>Corners sharper than this (degrees) cut the line into separate stretches.</summary>
        public float CornerDeg = 40f;
        public bool Closed;
        /// <summary>Every piece of a run its own variant (crack chains); strips keep one variant per run so they join.</summary>
        public bool MixVariants;
        public ColourLevel Level = ColourLevel.Any;
        public string Cause = "strip";
    }

    /// <summary>Points in square cells, for "what is near p" without walking every point (thousands of trees on a big site).</summary>
    public sealed class PointGrid
    {
        private readonly float _cell;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        public PointGrid(float cell) { _cell = math.max(1f, cell); }
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        public void Add(float2 p, int index)
        {
            long k = Key((int)math.floor(p.x / _cell), (int)math.floor(p.y / _cell));
            if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<int>();
            l.Add(index);
        }
        /// <summary>Indices in the cell of p and <paramref name="ring"/> cells around it.</summary>
        public IEnumerable<int> Near(float2 p, int ring)
        {
            int cx = (int)math.floor(p.x / _cell), cz = (int)math.floor(p.y / _cell);
            for (int z = cz - ring; z <= cz + ring; z++)
                for (int x = cx - ring; x <= cx + ring; x++)
                    if (_cells.TryGetValue(Key(x, z), out var l))
                        foreach (int i in l) yield return i;
        }
    }

    public static class Brushes
    {
        private static float MeanFootArea(DecalKind k, int[] pool)
        {
            float a = 0f;
            foreach (int v in pool) { var f = DecalVariants.FootprintOf(k, v); a += f.x * f.y * 0.6f; }
            return math.max(0.25f, a / math.max(1, pool.Length));
        }

        private static float MeanLength(DecalKind k, int[] pool)
        {
            float a = 0f;
            foreach (int v in pool) a += DecalVariants.FootprintOf(k, v).x;
            return math.max(0.5f, a / math.max(1, pool.Length));
        }

        /// <summary>
        /// Patches of <paramref name="kind"/> over <paramref name="area"/> by a cause field: clumps (a parent and children
        /// shrinking away from it) where the field is strong, loners between, until about <paramref name="coverage"/> of the
        /// weighted area is covered (overlaps count twice: 0.5 already reads as a full carpet). Returns pieces placed.
        /// </summary>
        public static int Scatter(BrushContext ctx, BrushArea area, string kind, float coverage, ScatterOptions o = null)
        {
            o = o ?? new ScatterOptions();
            var k = ctx.Lib.Get(kind);
            if (k == null || area == null || area.IsEmpty || coverage <= 0f || ctx.Full) return 0;
            var pool = ctx.Variants(k, o.Level);
            var r = ctx.Rng(kind);
            area.Bounds(out var lo, out var hi);
            float len = MeanLength(k, pool);
            float meanScale = (o.Scale.x + o.Scale.y) * 0.5f;
            float piece = MeanFootArea(k, pool) * meanScale * meanScale * 0.8f;   // children are smaller
            // the weighted area (sampled)
            float wsum = 0f; int samples = 0;
            for (int i = 0; i < 400; i++)
            {
                var p = new float2(r.R(lo.x, hi.x), r.R(lo.y, hi.y));
                if (!area.Contains(p)) continue;
                samples++;
                wsum += o.Weight == null ? 1f : math.saturate(o.Weight(p));
            }
            if (samples == 0) return 0;
            float wArea = area.Area() * wsum / samples;
            coverage *= math.saturate(ctx.Density);
            int n = (int)math.round(coverage * wArea / piece);
            if (n <= 0) n = r.F() < coverage * wArea / piece ? 1 : 0;
            int loners = (int)math.round(n * o.Loners);
            int clumps = math.max(n > loners ? 1 : 0, (int)math.round((n - loners) / math.max(1f, o.Clump)));
            int placed = 0;
            float W(float2 p) => o.Weight == null ? 1f : math.saturate(o.Weight(p));
            bool Ok(float2 p) => area.Contains(p) || (o.Spill > 0f && area.EdgeDistance(p) < o.Spill && InsideOuter(area, p));
            bool Land(ref BrushRng rr, out float2 at)
            {
                for (int t = 0; t < 40; t++)
                {
                    var p = new float2(rr.R(lo.x, hi.x), rr.R(lo.y, hi.y));
                    if (area.Contains(p) && rr.F() <= W(p)) { at = p; return true; }
                }
                at = default; return false;
            }
            void Put(ref BrushRng rr, float2 p, float s, float w)
            {
                int v = ctx.Pick(k, pool, p, len * s * 0.9f, ref rr);
                var sc = new float2(s * rr.R(0.92f, 1.08f), s * rr.R(0.92f, 1.08f));
                float op = rr.R(o.Opacity) * (0.7f + 0.3f * w);
                if (ctx.Add(k, v, p, rr.F() * 2f * math.PI, sc, op, o.Cause)) placed++;
            }
            for (int c = 0; c < clumps && !ctx.Full; c++)
            {
                if (!Land(ref r, out var parent)) continue;
                float w0 = W(parent);
                float s0 = r.R(o.Scale) ;
                Put(ref r, parent, s0, w0);
                // children: a clump of clumps (some children seed a sub-clump)
                int kids = (int)math.round(r.R(0.4f, 1.6f) * (o.Clump - 1f));
                float spread = o.Spread * len * s0;
                var sub = parent;
                for (int i = 0; i < kids && !ctx.Full; i++)
                {
                    if (r.F() < 0.3f) sub = parent + new float2(r.Gauss(), r.Gauss()) * spread * 0.8f;
                    var off = new float2(r.Gauss(), r.Gauss()) * spread * 0.6f;
                    var p = sub + off;
                    if (!Ok(p)) continue;
                    float w = W(p);
                    if (r.F() > math.max(0.15f, w)) continue;
                    float d = math.distance(p, parent) / math.max(0.5f, spread * 1.6f);
                    float s = math.clamp(s0 * math.lerp(0.95f, 0.65f, math.saturate(d)) * r.R(0.85f, 1.1f), o.Scale.x * 0.75f, o.Scale.y);
                    Put(ref r, p, s, w);
                }
            }
            for (int i = 0; i < loners && !ctx.Full; i++)
            {
                if (!Land(ref r, out var p)) continue;
                Put(ref r, p, r.R(o.Scale.x * 0.8f, o.Scale.y * 0.9f), W(p));
            }
            return placed;
        }

        private static bool InsideOuter(BrushArea a, float2 p)
        {
            // the spill zone: just outside the outer ring is fine, inside a hole only near its edge
            foreach (var h in a.Holes) if (Poly.Contains(h, p) && Poly.DistanceToBoundary(h, p) > 0f) return Poly.DistanceToBoundary(h, p) < 1.5f;
            return true;
        }

        /// <summary>
        /// A faint tone over the whole area: big patches on a jittered, turned lattice (the lattice never shows: each point
        /// moves up to half a step and the patches overlap by half their size), so the whole lawn or paving varies a little.
        /// </summary>
        public static int Tone(BrushContext ctx, BrushArea area, string kind, float2 opacity, float overlap = 0.5f, ColourLevel level = ColourLevel.Any, string cause = "tone")
        {
            var k = ctx.Lib.Get(kind);
            if (k == null || area == null || area.IsEmpty || ctx.Full) return 0;
            var pool = ctx.Variants(k, level);
            var r = ctx.Rng(kind);
            float len = MeanLength(k, pool);
            float narrow = 0f;
            foreach (int v in pool) narrow += math.cmin(DecalVariants.FootprintOf(k, v));
            narrow /= math.max(1, pool.Length);
            // the step follows the patches' narrow side, so the soft edges overlap everywhere
            float step = math.max(2f, narrow * (1.15f - math.saturate(overlap))) / math.sqrt(math.clamp(ctx.Density, 0.05f, 1f));
            area.Bounds(out var lo, out var hi);
            float ang = r.F() * math.PI;
            var ax = new float2(math.cos(ang), math.sin(ang)); var az = new float2(-ax.y, ax.x);
            var c = (lo + hi) * 0.5f;
            float reach = math.length(hi - lo) * 0.5f + step;
            int placed = 0;
            for (float v = -reach; v <= reach; v += step * 0.87f)
            {
                float shift = r.F() * step;
                for (float u = -reach + shift; u <= reach; u += step)
                {
                    var p = c + ax * u + az * v + r.Disc() * step * 0.5f;
                    // centres inside, or just over the outer edge (the patch fades out there), never deep in a hole
                    if (!area.Contains(p) && !(NearOuter(area, p, len * 0.25f) && InsideOuter(area, p) && (area.Keep == null || area.Keep(p)))) continue;
                    int vi = ctx.Pick(k, pool, p, step, ref r);
                    float s = r.R(0.85f, 1.25f);
                    if (ctx.Add(k, vi, p, r.F() * 2f * math.PI, new float2(s * r.R(0.9f, 1.1f), s * r.R(0.9f, 1.1f)), r.R(opacity), cause)) placed++;
                }
            }
            return placed;
        }

        private static bool NearOuter(BrushArea a, float2 p, float d)
        {
            foreach (var r in a.Outer) if (Poly.Contains(r, p) || Poly.DistanceToBoundary(r, p) < d) return true;
            return false;
        }

        /// <summary>
        /// A strip (or crack chain) end to end along <paramref name="line"/>: stretches cut at sharp corners, broken into runs
        /// with bare gaps between, each run a whole number of repeats stretched to fit, starting and ending in faded caps.
        /// </summary>
        public static int EdgeStrip(BrushContext ctx, IReadOnlyList<float2> line, string kind, StripOptions o = null)
        {
            o = o ?? new StripOptions();
            var k = ctx.Lib.Get(kind);
            if (k == null || line == null || line.Count < 2 || ctx.Full) return 0;
            bool tiled = k.Variants.Tile == VariantTiling.X || k.Variants.Tile == VariantTiling.Chain;
            var pool = ctx.Variants(k, o.Level);
            var r = ctx.Rng(kind);
            int placed = 0;
            foreach (var stretch in Stretches(line, o.Closed, o.CornerDeg))
            {
                // a thinned site drops whole stretches too (many short court and kerb sides would each keep a run otherwise)
                if (ctx.Density < 0.999f && r.F() > math.sqrt(math.clamp(ctx.Density, 0.05f, 1f))) continue;
                var arc = Arc(stretch);
                float total = arc[arc.Length - 1];
                if (total < 1.5f) continue;
                // runs and gaps along the stretch
                // a thinned site keeps the same runs and leaves longer gaps between them
                float gaps = ctx.Density < 0.999f ? 1f - (1f - o.Gaps) * math.sqrt(math.clamp(ctx.Density, 0.05f, 1f)) : o.Gaps;
                float at = gaps > 0f ? r.R(0f, o.Run.x * 0.5f) * gaps : 0f;
                while (at < total - 1.5f && !ctx.Full)
                {
                    float runLen = math.min(r.R(o.Run), total - at);
                    if (gaps <= 0f) runLen = total - at;
                    if (total - (at + runLen) < 4f) runLen = total - at;   // no stub at the end
                    placed += Run(ctx, k, pool, stretch, arc, at, at + runLen, tiled, o, ref r);
                    at += runLen;
                    if (gaps > 0f) at += runLen * gaps / math.max(0.05f, 1f - gaps) * r.R(0.4f, 1.6f);
                }
            }
            return placed;
        }

        /// <summary>Crack chain pieces end to end along a line (every piece its own variant: chains join any to any).</summary>
        public static int CracksAlong(BrushContext ctx, IReadOnlyList<float2> line, string kind, StripOptions o = null)
        {
            o = o ?? new StripOptions { Gaps = 0.45f, Run = new float2(12f, 36f), Width = new float2(0.9f, 1.15f), Cause = "cracks" };
            o.MixVariants = true;
            return EdgeStrip(ctx, line, kind, o);
        }

        private static int Run(BrushContext ctx, DecalKind k, int[] pool, float2[] pts, float[] arc, float a, float b, bool tiled, StripOptions o, ref BrushRng r)
        {
            float len = b - a;
            if (len < 1f) return 0;
            int v0 = ctx.Pick(k, pool, Along(pts, arc, (a + b) * 0.5f, out _), len * 0.5f, ref r);
            float L = DecalVariants.SpanOf(k, v0).x;
            int n = math.max(tiled ? 2 : 1, (int)math.round(len / L));
            float sx = len / (n * L);
            if (sx > 1.45f) { n++; sx = len / (n * L); }
            if (sx < 0.6f && n > 1) { n = math.max(1, n - 1); sx = len / (n * L); }
            if (tiled && n == 1) { n = 2; sx = len / (2f * L); }
            float sz = r.R(o.Width);
            float op = r.R(o.Opacity);
            int placed = 0;
            for (int i = 0; i < n && !ctx.Full; i++)
            {
                float s0 = a + i * len / n, s1 = a + (i + 1) * len / n;
                var p0 = Along(pts, arc, s0, out _); var p1 = Along(pts, arc, s1, out _);
                var d = math.normalizesafe(p1 - p0, new float2(1f, 0f));
                var left = new float2(-d.y, d.x);
                // local +x along the direction of travel puts +z (the heavy side) on the left; turned half round for the right
                var xd = o.Side >= 0 ? d : -d;
                float yaw = math.atan2(-xd.y, xd.x);
                var c = (p0 + p1) * 0.5f + left * (o.Side >= 0 ? 1f : -1f) * o.Offset;
                int v = o.MixVariants && i > 0 ? ctx.Pick(k, pool, c, L, ref r) : v0;
                if (tiled)
                {
                    // the run's first piece fades toward the run's start, the last toward its end
                    bool first = i == 0, last = i == n - 1;
                    if (first || last)
                    {
                        bool towardPlusX = first ? o.Side < 0 : o.Side >= 0;   // where that end lies in the piece's own x
                        v = (towardPlusX ? DecalVariants.CapEnd : DecalVariants.CapStart) + v;
                    }
                }
                float chord = math.distance(p0, p1);
                float stretch = tiled ? chord / L : math.max(0.7f, chord / L);
                if (ctx.Add(k, v, c, yaw, new float2(stretch, sz), op, o.Cause)) placed++;
            }
            return placed;
        }

        /// <summary>
        /// Overlapping pieces fill a hollow's outline: one large piece over the middle, then smaller ones where the ring is
        /// wide enough, each sized to its spot (a puddle that follows the real low ground, not a stamped oval).
        /// </summary>
        public static int Fill(BrushContext ctx, float2[] hollow, string kind, float2 opacity, ColourLevel level = ColourLevel.Any, string cause = "fill")
        {
            var k = ctx.Lib.Get(kind);
            if (k == null || hollow == null || hollow.Length < 3 || ctx.Full) return 0;
            var pool = ctx.Variants(k, level);
            var r = ctx.Rng(kind);
            var area = BrushArea.Of(hollow);
            Poly.Bounds(hollow, out var lo, out var hi);
            float baseLen = MeanLength(k, pool);
            int placed = 0;
            var centre = Poly.Centroid(hollow);
            if (!Poly.Contains(hollow, centre)) centre = hollow[0] * 0.5f + hollow[hollow.Length / 2] * 0.5f;
            int target = math.clamp((int)math.round(area.Area() / math.max(1f, baseLen * baseLen * 0.35f)) + 1, 1, 12);
            for (int i = 0, tries = 0; i < target && tries < target * 8 && !ctx.Full; tries++)
            {
                var p = i == 0 ? centre : new float2(r.R(lo.x, hi.x), r.R(lo.y, hi.y));
                if (!area.Contains(p)) continue;
                float room = math.max(0.6f, area.EdgeDistance(p)) * 2.4f;
                int v = ctx.Pick(k, pool, p, room * 0.5f, ref r);
                var foot = DecalVariants.FootprintOf(k, v);
                float s = math.clamp(room / math.max(0.3f, foot.x), 0.25f, 1.5f);
                // a long hollow: the piece follows its long side
                var ext = hi - lo;
                float yaw = (ext.x >= ext.y ? 0f : math.PI * 0.5f) + r.R(-0.35f, 0.35f);
                if (ctx.Add(k, v, p, yaw, new float2(s, s * r.R(0.85f, 1.1f)), r.R(opacity) * (i == 0 ? 1f : 0.85f), cause)) { placed++; i++; }
            }
            return placed;
        }

        /// <summary>
        /// Patches under canopies (centre, crown radius), shifted downwind and heaviest where crowns overlap: leaves, needles,
        /// petals. <paramref name="limit"/> keeps them in the ground they fall on (lawn, not the pitch).
        /// </summary>
        public static int Under(BrushContext ctx, IReadOnlyList<(float2 p, float r)> canopies, string kind, float coverage, float2 wind, BrushArea limit, ScatterOptions o = null)
        {
            if (canopies == null || canopies.Count == 0 || limit == null) return 0;
            o = o ?? new ScatterOptions { Cause = "under" };
            var crowns = new List<(float2 p, float r)>(canopies);
            var grid = new PointGrid(24f);
            for (int i = 0; i < crowns.Count; i++) grid.Add(crowns[i].p, i);
            Func<float2, float> field = p =>
            {
                // leaves land within about two and a half crowns, centred a crown downwind; crowded crowns add up
                float w = 0f;
                foreach (int i in grid.Near(p, 1))
                {
                    var c = crowns[i];
                    var q = c.p + wind * c.r * 0.8f;
                    float d = math.distance(p, q) / (c.r * 2.6f + 3f);
                    if (d < 1f) w += 1f - d * d;
                }
                return math.saturate(w);
            };
            var inner = o.Weight;
            o.Weight = inner == null ? field : (p => field(p) * inner(p));
            // the area: the limit, but only near crowns (fast rejection)
            var a = new BrushArea { Keep = p => { foreach (int i in grid.Near(p, 1)) { var c = crowns[i]; float R = c.r * 3.4f + 3f; if (math.distancesq(p, c.p) < R * R) return limit.Keep == null || limit.Keep(p); } return false; } };
            a.Outer.AddRange(limit.Outer);
            a.Holes.AddRange(limit.Holes);
            float2 clo = new float2(float.MaxValue), chi = new float2(float.MinValue);
            foreach (var c in crowns) { float R = c.r * 3.4f + 3f; clo = math.min(clo, c.p - R); chi = math.max(chi, c.p + R); }
            a.ClipLo = math.max(clo, limit.ClipLo); a.ClipHi = math.min(chi, limit.ClipHi);
            int n = Scatter(ctx, a, kind, coverage, o);
            o.Weight = inner;
            return n;
        }

        /// <summary>
        /// The wear on a path or road itself: hairline cracks along it, a few cross cracks, damp smudges, grit, a puddle now
        /// and then, and leaves where a tree stands over it (<paramref name="leafAt"/>: the leaf kind at a point, or null).
        /// Never a strip that redraws the path (the user, 2026-10-30: dirt trails laid over curved paths read as rectangles;
        /// paths carry cracks, smudges, puddles, dirt and leaves, more than before). <paramref name="amount"/> 1 = a park path.
        /// </summary>
        public static int PathWear(BrushContext ctx, IReadOnlyList<float2> line, float half, float amount, Func<float2, string> leafAt = null, string cause = "path")
        {
            if (line == null || line.Count < 2 || half <= 0.2f || amount <= 0f) return 0;
            // the path's own surface as a ribbon (the brushes sample inside it)
            var left = new List<float2>(); var right = new List<float2>();
            for (int i = 0; i < line.Count; i++)
            {
                var d = math.normalizesafe(line[math.min(line.Count - 1, i + 1)] - line[math.max(0, i - 1)], new float2(1f, 0f));
                var n = new float2(-d.y, d.x) * (half * 0.92f);
                left.Add(line[i] + n); right.Add(line[i] - n);
            }
            right.Reverse(); left.AddRange(right);
            var area = BrushArea.Of(left.ToArray());
            float a = math.saturate(amount) + math.max(0f, amount - 1f) * 0.5f;
            int n0 = ctx.Out.Count;
            var rng = ctx.Rng("pathWear");
            // hairline cracks along the walk, broken often; a few across it
            CracksAlong(ctx, line, half > 2.5f ? "crackAsphalt" : "crackConcrete", new StripOptions { Gaps = 0.65f, Run = new float2(4f, 12f), Opacity = new float2(0.55f, 0.85f), Width = new float2(0.7f, 1f), Cause = cause + ":crack" });
            Scatter(ctx, area, "crackWeb", 0.025f * a, new ScatterOptions { Opacity = new float2(0.5f, 0.85f), Clump = 1.5f, Loners = 0.7f, Scale = new float2(0.5f, 0.9f), Cause = cause + ":crack" });
            // damp smudges and grit, in a few clusters, never an even film
            Scatter(ctx, area, "dampPatch", 0.05f * a, new ScatterOptions { Opacity = new float2(0.35f, 0.65f), Clump = 2.5f, Loners = 0.35f, Scale = new float2(0.4f, 0.8f), Level = ColourLevel.Quiet, Cause = cause + ":smudge" });
            Scatter(ctx, area, "pebbles", 0.04f * a, new ScatterOptions { Opacity = new float2(0.45f, 0.8f), Clump = 3f, Loners = 0.4f, Scale = new float2(0.5f, 0.9f), Level = ColourLevel.Quiet, Cause = cause + ":grit" });
            // a puddle every 50-90 m or so, where a walk dips or water stands
            float len = 0f; for (int i = 1; i < line.Count; i++) len += math.distance(line[i - 1], line[i]);
            int puddles = (int)math.round(len / 70f * a * (0.6f + rng.F() * 0.8f));
            for (int k = 0; k < puddles; k++)
            {
                float at = rng.F() * len, run = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    float s = math.distance(line[i - 1], line[i]);
                    if (run + s >= at)
                    {
                        var p = math.lerp(line[i - 1], line[i], (at - run) / math.max(0.01f, s));
                        var dir = math.normalizesafe(line[i] - line[i - 1]);
                        At(ctx, p + new float2(-dir.y, dir.x) * (rng.F() - 0.5f) * half, dir, "puddlePiece", math.min(half * 1.6f, 3.2f), new float2(0.6f, 0.9f), 1, ColourLevel.Any, cause + ":puddle");
                        break;
                    }
                    run += s;
                }
            }
            // leaves on the walk under the trees beside it
            if (leafAt != null)
            {
                var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < line.Count; i++) { var k = leafAt(line[i]); if (k != null) kinds[k] = 1; }
                foreach (var k in kinds.Keys)
                {
                    string kind = k;
                    Scatter(ctx, area, kind, 0.08f * a, new ScatterOptions { Weight = p => leafAt(p) == kind ? 1f : 0f, Opacity = new float2(0.5f, 0.85f), Clump = 2f, Loners = 0.5f, Scale = new float2(0.5f, 0.9f), Level = ColourLevel.Quiet, Cause = cause + ":leaves" });
                }
            }
            return ctx.Out.Count - n0;
        }

        /// <summary>
        /// A hot spot at a feature: one main piece of about <paramref name="size"/> metres along <paramref name="dir"/>, then
        /// 2-4 satellites thrown further along dir and to the sides, smaller and fainter (never a lone disc).
        /// </summary>
        public static int At(BrushContext ctx, float2 p, float2 dir, string kind, float size, float2 opacity, int satellites = 3, ColourLevel level = ColourLevel.Any, string cause = "at")
        {
            var k = ctx.Lib.Get(kind);
            if (k == null || ctx.Full) return 0;
            var pool = ctx.Variants(k, level);
            var r = ctx.Rng(kind);
            // a thinned site keeps fewer hot spots (60 courts' corners are not 60 signature spots)
            if (ctx.Density < 0.999f && r.F() > ctx.Density) return 0;
            satellites = (int)math.round(satellites * math.min(1f, ctx.Density));
            dir = math.normalizesafe(dir, new float2(1f, 0f));
            var side = new float2(-dir.y, dir.x);
            int placed = 0;
            int v = ctx.Pick(k, pool, p, size, ref r);
            float s = size / math.max(0.3f, DecalVariants.FootprintOf(k, v).x);
            if (ctx.Add(k, v, p, math.atan2(-dir.y, dir.x) + r.R(-0.3f, 0.3f), new float2(s, s * r.R(0.8f, 1.05f)), r.R(opacity), cause)) placed++;
            for (int i = 0; i < satellites && !ctx.Full; i++)
            {
                var q = p + dir * size * r.R(0.15f, 0.65f) + side * size * r.R(-0.55f, 0.55f);
                int vi = ctx.Pick(k, pool, q, size * 0.6f, ref r);
                float si = size * r.R(0.35f, 0.7f) / math.max(0.3f, DecalVariants.FootprintOf(k, vi).x);
                if (ctx.Add(k, vi, q, r.F() * 2f * math.PI, new float2(si, si * r.R(0.85f, 1.1f)), r.R(opacity) * r.R(0.6f, 0.9f), cause)) placed++;
            }
            return placed;
        }

        // ───────────── line helpers ─────────────

        /// <summary>The line cut at corners sharper than <paramref name="cornerDeg"/> (a closed ring starts at its sharpest corner).</summary>
        internal static List<float2[]> Stretches(IReadOnlyList<float2> line, bool closed, float cornerDeg)
        {
            var pts = new List<float2>();
            foreach (var p in line) if (pts.Count == 0 || math.distancesq(pts[pts.Count - 1], p) > 1e-4f) pts.Add(p);
            if (closed && pts.Count > 2 && math.distancesq(pts[0], pts[pts.Count - 1]) < 1e-4f) pts.RemoveAt(pts.Count - 1);
            var res = new List<float2[]>();
            if (pts.Count < 2) return res;
            float cosLim = math.cos(math.radians(cornerDeg));
            bool Sharp(int i)
            {
                int n = pts.Count;
                var a = pts[(i - 1 + n) % n]; var b = pts[i]; var c = pts[(i + 1) % n];
                return math.dot(math.normalizesafe(b - a), math.normalizesafe(c - b)) < cosLim;
            }
            if (closed && pts.Count > 2)
            {
                int start = -1;
                for (int i = 0; i < pts.Count; i++) if (Sharp(i)) { start = i; break; }
                var ring = new List<float2>();
                int s = start < 0 ? 0 : start;
                for (int i = 0; i <= pts.Count; i++) ring.Add(pts[(s + i) % pts.Count]);
                pts = ring;
                closed = start >= 0;   // a smooth ring is one stretch round
            }
            var cur = new List<float2> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                cur.Add(pts[i]);
                bool corner = i < pts.Count - 1 && math.dot(math.normalizesafe(pts[i] - pts[i - 1]), math.normalizesafe(pts[i + 1] - pts[i])) < cosLim;
                if (corner) { res.Add(cur.ToArray()); cur = new List<float2> { pts[i] }; }
            }
            if (cur.Count >= 2) res.Add(cur.ToArray());
            return res;
        }

        internal static float[] Arc(float2[] pts)
        {
            var a = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) a[i] = a[i - 1] + math.distance(pts[i - 1], pts[i]);
            return a;
        }

        internal static float2 Along(float2[] pts, float[] arc, float s, out float2 dir)
        {
            int n = pts.Length;
            if (s <= 0f) { dir = math.normalizesafe(pts[1] - pts[0]); return pts[0]; }
            for (int i = 1; i < n; i++)
                if (arc[i] >= s)
                {
                    float t = (s - arc[i - 1]) / math.max(1e-5f, arc[i] - arc[i - 1]);
                    dir = math.normalizesafe(pts[i] - pts[i - 1]);
                    return math.lerp(pts[i - 1], pts[i], t);
                }
            dir = math.normalizesafe(pts[n - 1] - pts[n - 2]);
            return pts[n - 1];
        }

        /// <summary>A ring's edges offset inward (CCW ring, positive d) as a closed polyline: a seam line inside a surface.</summary>
        public static float2[] Inset(float2[] ring, float d)
        {
            if (ring == null || ring.Length < 3) return ring;
            var r = Poly.EnsureCcw((float2[])ring.Clone());
            int n = r.Length;
            var o = new float2[n];
            for (int i = 0; i < n; i++)
            {
                var a = r[(i - 1 + n) % n]; var b = r[i]; var c = r[(i + 1) % n];
                var n0 = math.normalizesafe(new float2(-(b - a).y, (b - a).x));
                var n1 = math.normalizesafe(new float2(-(c - b).y, (c - b).x));
                var m = math.normalizesafe(n0 + n1, n0);
                float k = math.max(0.35f, math.dot(m, n0));
                o[i] = b + m * d / k;
            }
            return o;
        }
    }
}
