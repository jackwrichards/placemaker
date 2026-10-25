// Placemaker — composable terrain-paint strength fields (s33, docs/TERRAIN-PAINT.md). GPL-3.0.
//
// A PaintShape is a pure function world xz → strength 0..1 with a support box (0 outside it). Primitives: feathered
// polygon fill, stroke along a polyline (width, feather, wobble, strength gradient start → end, faded ends), radial
// "sphere" gradient, linear gradient, noise speckle, constant, and a baked grid (masks). Combinators multiply, take
// the max/min, subtract, scale and blend shapes, so "dirt path, fading out at the dead end, frayed edge, but never on
// the paving" is one expression:
//
//     PaintShape.Stroke(line, 2.4f, 0.8f, start: 0.7f, end: 0.2f, wobble: 0.3f, seed: s).Mul(ground)
//
// The game paints a shape as a per-texel mask (Core/Build/PaintRaster → TerrainMaterialSystem.ApplyBrush), so any
// field costs one brush call per 512² stamp. Shapes are IMMUTABLE and thread-safe once built (the preview overlay on
// the main thread and the build's raster worker may evaluate the same plan at the same time): no caches, no shared
// scratch buffers. Deterministic: noise is a hash of the seed and the position.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Paint
{
    /// <summary>How a gradient falls from full (t = 0) to nothing (t = 1).</summary>
    public enum Falloff
    {
        /// <summary>1 − t.</summary>
        Linear = 0,
        /// <summary>1 − smoothstep(t): soft at both ends (default).</summary>
        Smooth = 1,
        /// <summary>(1 − t)²: drops fast, long faint tail (worn ground thinning out).</summary>
        Fast = 2,
        /// <summary>1 − t²: holds, then drops (a crisp-ish disc).</summary>
        Hold = 3,
        /// <summary>√(1 − t²): a dome, the "sphere" gradient.</summary>
        Sphere = 4,
    }

    public abstract class PaintShape
    {
        /// <summary>Support box: the shape is 0 outside it. Unbounded shapes use ±infinity (combine them with a bounded one).</summary>
        public float2 Min = new float2(float.NegativeInfinity), Max = new float2(float.PositiveInfinity);

        /// <summary>Strength at a world xz point, 0..1.</summary>
        public abstract float At(float2 p);

        public bool Bounded => math.all(math.isfinite(Min)) && math.all(math.isfinite(Max));
        public bool Empty => Bounded && (Max.x <= Min.x || Max.y <= Min.y);

        /// <summary>Evaluates with the box test first (cheap reject).</summary>
        public float Sample(float2 p)
        {
            if (p.x < Min.x || p.y < Min.y || p.x > Max.x || p.y > Max.y) return 0f;
            float v = At(p);
            return v > 0f ? (v < 1f ? v : 1f) : 0f;
        }

        // ───────────────────────── falloff ─────────────────────────

        /// <summary>Weight 1 → 0 for t 0 → 1 under the curve.</summary>
        public static float Curve(Falloff f, float t)
        {
            t = math.saturate(t);
            switch (f)
            {
                case Falloff.Linear: return 1f - t;
                case Falloff.Fast: return (1f - t) * (1f - t);
                case Falloff.Hold: return 1f - t * t;
                case Falloff.Sphere: return math.sqrt(math.max(0f, 1f - t * t));
                default: return 1f - t * t * (3f - 2f * t);
            }
        }

        internal static float Smooth01(float x)
        {
            x = math.saturate(x);
            return x * x * (3f - 2f * x);
        }

        // ───────────────────────── primitives ─────────────────────────

        /// <summary>Constant strength everywhere (unbounded: multiply with a bounded shape).</summary>
        public static PaintShape Constant(float v) => new ConstShape(math.saturate(v));

        /// <summary>Nothing (an empty, bounded shape).</summary>
        public static PaintShape None => new EmptyShape();

        private sealed class EmptyShape : PaintShape
        {
            public EmptyShape() { Min = float2.zero; Max = float2.zero; }
            public override float At(float2 p) => 0f;
        }

        /// <summary>
        /// Fill a polygon: full strength <paramref name="feather"/> m inside the outline (smoothstep), 0 on it, never outside.
        /// <paramref name="noise"/> m of value noise pulls the soft edge inward by up to 2× noise so it reads natural.
        /// </summary>
        public static PaintShape Polygon(IReadOnlyList<float2> ring, float feather, float noise = 0f, float noiseScale = 0f, uint seed = 0u)
            => PolygonShape.Make(ring, feather, noise, noiseScale, seed);

        /// <summary>
        /// A stroke along a polyline: full inside half the width minus the feather, soft to 0 at the edge. Strength runs
        /// <paramref name="start"/> → <paramref name="end"/> along the length (curve <paramref name="along"/>);
        /// <paramref name="endFade"/> m tapers both ends (a trail petering out); <paramref name="wobble"/> (0..0.8) varies the
        /// width along the line (feature size <paramref name="wobbleScale"/> m) and frays the edge. Round caps.
        /// </summary>
        public static PaintShape Stroke(IReadOnlyList<float2> line, float width, float feather, float start = 1f, float end = 1f,
            Falloff along = Falloff.Linear, float endFade = 0f, float wobble = 0f, float wobbleScale = 0f, uint seed = 0u)
            => StrokeShape.Make(line, width, feather, start, end, along, endFade, wobble, wobbleScale, seed);

        /// <summary>
        /// Radial ("sphere") gradient: full within <paramref name="inner"/> m of the centre, falling to 0 at
        /// <paramref name="outer"/> m under <paramref name="falloff"/>; <paramref name="noise"/> m wobbles the radius.
        /// <paramref name="aspect"/> &gt; 1 stretches it along <paramref name="angle"/> (radians, from +x) into an ellipse.
        /// </summary>
        public static PaintShape Radial(float2 centre, float inner, float outer, Falloff falloff = Falloff.Smooth, float noise = 0f,
            float noiseScale = 0f, uint seed = 0u, float aspect = 1f, float angle = 0f)
            => new RadialShape(centre, inner, outer, falloff, noise, noiseScale, seed, aspect, angle);

        /// <summary>
        /// Linear gradient: <paramref name="startValue"/> at (and behind) <paramref name="from"/>, <paramref name="endValue"/>
        /// at (and beyond) <paramref name="to"/>, under <paramref name="falloff"/>. Unbounded: multiply with a polygon.
        /// </summary>
        public static PaintShape Linear(float2 from, float2 to, Falloff falloff = Falloff.Linear, float startValue = 1f, float endValue = 0f)
            => new LinearShape(from, to, falloff, startValue, endValue);

        /// <summary>
        /// Speckle / patch mask: value-noise blobs of size ~<paramref name="scale"/> m covering about
        /// <paramref name="coverage"/> (0..1) of the ground, edges soft over <paramref name="softness"/> (0..1 of the noise range).
        /// Unbounded.
        /// </summary>
        public static PaintShape Noise(float scale, float coverage, float softness = 0.15f, uint seed = 0u)
            => new NoiseShape(scale, coverage, softness, seed);

        /// <summary>A baked grid of strengths (bilinear; row 0 = min z), e.g. the "where bare terrain shows" mask.</summary>
        public static PaintShape Grid(float2 origin, float cell, int width, int height, float[] values)
            => new GridShape(origin, cell, width, height, values);

        // ───────────────────────── combinators ─────────────────────────

        /// <summary>this × other (mask). Support = intersection.</summary>
        public PaintShape Mul(PaintShape other) => other == null ? this : new BinaryShape(this, other, BinaryShape.Op.Mul);
        /// <summary>max(this, other) (union). Support = union.</summary>
        public PaintShape Or(PaintShape other) => other == null ? this : new BinaryShape(this, other, BinaryShape.Op.Max);
        /// <summary>min(this, other). Support = intersection.</summary>
        public PaintShape Intersect(PaintShape other) => other == null ? this : new BinaryShape(this, other, BinaryShape.Op.Min);
        /// <summary>this × (1 − other): cut the other shape out (soft where it is soft). Support = this.</summary>
        public PaintShape Subtract(PaintShape other) => other == null ? this : new BinaryShape(this, other, BinaryShape.Op.Subtract);
        /// <summary>this × k.</summary>
        public PaintShape Scale(float k) => new ScaleShape(this, k);
        /// <summary>1 − this (unbounded).</summary>
        public PaintShape Invert() => new InvertShape(this);
        /// <summary>Remaps strength through a curve: 0 → 0, 1 → 1, <paramref name="gamma"/> &lt; 1 fattens, &gt; 1 thins.</summary>
        public PaintShape Gamma(float gamma) => new GammaShape(this, gamma);
        /// <summary>Keeps only the part inside <paramref name="ring"/> (feathered inside by <paramref name="feather"/> m).</summary>
        public PaintShape Clip(IReadOnlyList<float2> ring, float feather = 0.5f) => Mul(Polygon(ring, feather));

        /// <summary>Union of many shapes with an index over their boxes (fast for hundreds of discs).</summary>
        public static PaintShape Union(IReadOnlyList<PaintShape> shapes) => UnionShape.Make(shapes);

        /// <summary>A soft complementary pair for a two-material blend: (A = base × (1 − t), B = base × t).</summary>
        public static void Split(PaintShape area, PaintShape t, out PaintShape a, out PaintShape b)
        {
            a = area.Mul(t.Invert());
            b = area.Mul(t);
        }

        // ───────────────────────── implementations ─────────────────────────

        private sealed class ConstShape : PaintShape
        {
            private readonly float _v;
            public ConstShape(float v) { _v = v; }
            public override float At(float2 p) => _v;
        }

        private sealed class RadialShape : PaintShape
        {
            private readonly float2 _c; private readonly float _in, _out, _noise, _scale, _aspect; private readonly Falloff _f; private readonly uint _seed;
            private readonly float2 _u, _v;
            public RadialShape(float2 c, float inner, float outer, Falloff f, float noise, float scale, uint seed, float aspect, float angle)
            {
                _c = c; _in = math.max(0f, inner); _out = math.max(_in + 0.01f, outer); _f = f; _noise = math.max(0f, noise);
                _scale = scale > 0f ? scale : math.max(2f, _out * 0.6f); _seed = seed; _aspect = math.max(1f, aspect);
                math.sincos(angle, out float s, out float co);
                _u = new float2(co, s); _v = new float2(-s, co);
                float reach = _out * _aspect + _noise;
                Min = c - reach; Max = c + reach;
            }
            public override float At(float2 p)
            {
                var d2 = p - _c;
                float d = math.length(new float2(math.dot(d2, _u) / _aspect, math.dot(d2, _v)));
                if (_noise > 0f) d += (ValueNoise.Clumps(p, _scale, _seed) * 2f - 1f) * _noise * 1.6f;
                return Curve(_f, (d - _in) / (_out - _in));
            }
        }

        private sealed class LinearShape : PaintShape
        {
            private readonly float2 _a, _dir; private readonly float _len, _s, _e; private readonly Falloff _f;
            public LinearShape(float2 a, float2 b, Falloff f, float s, float e)
            {
                _a = a; _len = math.max(0.01f, math.distance(a, b)); _dir = math.normalizesafe(b - a, new float2(1, 0)); _f = f; _s = s; _e = e;
            }
            public override float At(float2 p)
            {
                float t = math.dot(p - _a, _dir) / _len;
                return math.lerp(_e, _s, Curve(_f, t));
            }
        }

        private sealed class NoiseShape : PaintShape
        {
            private readonly float _scale, _lo, _hi; private readonly uint _seed;
            public NoiseShape(float scale, float coverage, float softness, uint seed)
            {
                _scale = math.max(0.5f, scale); _seed = seed;
                float thr = NoiseQuantiles.Threshold(math.saturate(coverage));
                float soft = math.clamp(softness, 0.001f, 1f) * 0.25f;
                _lo = thr - soft * 0.5f; _hi = thr + soft * 0.5f;
                if (coverage <= 0f) { _lo = 2f; _hi = 3f; }
                if (coverage >= 1f) { _lo = -3f; _hi = -2f; }
            }
            public override float At(float2 p)
            {
                float n = ValueNoise.Clumps(p, _scale, _seed);
                return Smooth01((n - _lo) / (_hi - _lo));
            }
        }

        private sealed class GridShape : PaintShape
        {
            private readonly float2 _o; private readonly float _cell; private readonly int _w, _h; private readonly float[] _v;
            public GridShape(float2 origin, float cell, int w, int h, float[] values)
            {
                _o = origin; _cell = math.max(0.01f, cell); _w = math.max(1, w); _h = math.max(1, h);
                _v = values ?? new float[_w * _h];
                Min = origin; Max = origin + new float2(_w, _h) * _cell;
            }
            public override float At(float2 p)
            {
                var g = (p - _o) / _cell - 0.5f;
                int x0 = (int)math.floor(g.x), y0 = (int)math.floor(g.y);
                float fx = g.x - x0, fy = g.y - y0;
                float a = V(x0, y0), b = V(x0 + 1, y0), c = V(x0, y0 + 1), d = V(x0 + 1, y0 + 1);
                return math.lerp(math.lerp(a, b, fx), math.lerp(c, d, fx), fy);
            }
            private float V(int x, int y)
            {
                x = math.clamp(x, 0, _w - 1); y = math.clamp(y, 0, _h - 1);
                return _v[y * _w + x];
            }
        }

        private sealed class BinaryShape : PaintShape
        {
            public enum Op { Mul, Max, Min, Subtract }
            private readonly PaintShape _a, _b; private readonly Op _op;
            public BinaryShape(PaintShape a, PaintShape b, Op op)
            {
                _a = a; _b = b; _op = op;
                switch (op)
                {
                    case Op.Max: Min = math.min(a.Min, b.Min); Max = math.max(a.Max, b.Max); break;
                    case Op.Subtract: Min = a.Min; Max = a.Max; break;
                    default: Min = math.max(a.Min, b.Min); Max = math.min(a.Max, b.Max); break;
                }
            }
            public override float At(float2 p)
            {
                switch (_op)
                {
                    case Op.Max: return math.max(_a.Sample(p), _b.Sample(p));
                    case Op.Min: return math.min(_a.Sample(p), _b.Sample(p));
                    case Op.Subtract: { float a = _a.Sample(p); return a <= 0f ? 0f : a * (1f - _b.Sample(p)); }
                    default: { float a = _a.Sample(p); return a <= 0f ? 0f : a * _b.Sample(p); }
                }
            }
        }

        private sealed class ScaleShape : PaintShape
        {
            private readonly PaintShape _a; private readonly float _k;
            public ScaleShape(PaintShape a, float k) { _a = a; _k = math.max(0f, k); Min = a.Min; Max = a.Max; }
            public override float At(float2 p) => _a.Sample(p) * _k;
        }

        private sealed class InvertShape : PaintShape
        {
            private readonly PaintShape _a;
            public InvertShape(PaintShape a) { _a = a; }
            public override float At(float2 p) => 1f - _a.Sample(p);
        }

        private sealed class GammaShape : PaintShape
        {
            private readonly PaintShape _a; private readonly float _g;
            public GammaShape(PaintShape a, float g) { _a = a; _g = math.clamp(g, 0.05f, 20f); Min = a.Min; Max = a.Max; }
            public override float At(float2 p) { float v = _a.Sample(p); return v <= 0f ? 0f : math.pow(v, _g); }
        }

        private sealed class UnionShape : PaintShape
        {
            private readonly PaintShape[] _items;
            private readonly BoxGrid _grid;
            private UnionShape(PaintShape[] items)
            {
                _items = items;
                Min = new float2(float.MaxValue); Max = new float2(float.MinValue);
                foreach (var s in items) { Min = math.min(Min, s.Min); Max = math.max(Max, s.Max); }
                _grid = new BoxGrid(items);
            }
            public static PaintShape Make(IReadOnlyList<PaintShape> shapes)
            {
                var list = new List<PaintShape>();
                if (shapes != null) foreach (var s in shapes) if (s != null && s.Bounded && !s.Empty) list.Add(s);
                if (list.Count == 0) return new UnionShape(Array.Empty<PaintShape>()) { Min = float2.zero, Max = float2.zero };
                if (list.Count == 1) return list[0];
                return new UnionShape(list.ToArray());
            }
            public override float At(float2 p)
            {
                float v = 0f;
                var ids = _grid.At(p);
                if (ids == null) return 0f;
                foreach (int i in ids) { v = math.max(v, _items[i].Sample(p)); if (v >= 1f) break; }
                return v;
            }
        }

        /// <summary>Read-only bucket grid over shape boxes (immutable after construction).</summary>
        private sealed class BoxGrid
        {
            private readonly float _cell; private readonly Dictionary<long, int[]> _cells = new Dictionary<long, int[]>();
            public BoxGrid(PaintShape[] items)
            {
                float size = 0f;
                foreach (var s in items) size += math.cmax(s.Max - s.Min);
                _cell = math.clamp(size / math.max(1, items.Length) * 1.5f, 4f, 64f);
                var tmp = new Dictionary<long, List<int>>();
                for (int i = 0; i < items.Length; i++)
                {
                    var lo = (int2)math.floor(items[i].Min / _cell); var hi = (int2)math.floor(items[i].Max / _cell);
                    if ((long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) > 65536) hi = lo + 255;
                    for (int y = lo.y; y <= hi.y; y++)
                        for (int x = lo.x; x <= hi.x; x++)
                        {
                            long k = ((long)x << 32) ^ (uint)y;
                            if (!tmp.TryGetValue(k, out var l)) tmp[k] = l = new List<int>(4);
                            l.Add(i);
                        }
                }
                foreach (var kv in tmp) _cells[kv.Key] = kv.Value.ToArray();
            }
            public int[] At(float2 p)
            {
                var c = (int2)math.floor(p / _cell);
                return _cells.TryGetValue(((long)c.x << 32) ^ (uint)c.y, out var l) ? l : null;
            }
        }
    }

    /// <summary>Immutable segment grid: distance to the nearest of many segments without mutable scratch (thread-safe).</summary>
    internal sealed class SegGrid
    {
        public readonly float2[] A, B;
        public readonly float[] S0;   // arc length at A
        private readonly float _cell;
        private readonly Dictionary<long, int[]> _cells = new Dictionary<long, int[]>();

        public SegGrid(IReadOnlyList<(float2 a, float2 b)> segs, float reach)
        {
            int n = segs.Count;
            A = new float2[n]; B = new float2[n]; S0 = new float[n];
            float acc = 0f, total = 0f;
            for (int i = 0; i < n; i++) { A[i] = segs[i].a; B[i] = segs[i].b; S0[i] = acc; float l = math.distance(A[i], B[i]); acc += l; total += l; }
            _cell = math.clamp(math.max(reach * 2f, total / math.max(1, n) * 2f), 2f, 48f);
            if (n <= 24) return;   // brute force is faster for short lines
            var tmp = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var lo = (int2)math.floor((math.min(A[i], B[i]) - reach) / _cell);
                var hi = (int2)math.floor((math.max(A[i], B[i]) + reach) / _cell);
                if ((long)(hi.x - lo.x + 1) * (hi.y - lo.y + 1) > 16384) hi = lo + 127;
                for (int y = lo.y; y <= hi.y; y++)
                    for (int x = lo.x; x <= hi.x; x++)
                    {
                        long k = ((long)x << 32) ^ (uint)y;
                        if (!tmp.TryGetValue(k, out var l)) tmp[k] = l = new List<int>(4);
                        l.Add(i);
                    }
            }
            foreach (var kv in tmp) _cells[kv.Key] = kv.Value.ToArray();
        }

        public int Count => A.Length;

        /// <summary>Nearest segment within the build reach (or anywhere for short lines): distance, segment, t along it.</summary>
        public float Nearest(float2 p, out int seg, out float t)
        {
            seg = -1; t = 0f;
            float best = float.MaxValue;
            if (_cells.Count == 0)
            {
                for (int i = 0; i < A.Length; i++) Test(i, p, ref best, ref seg, ref t);
            }
            else
            {
                var c = (int2)math.floor(p / _cell);
                if (_cells.TryGetValue(((long)c.x << 32) ^ (uint)c.y, out var l))
                    foreach (int i in l) Test(i, p, ref best, ref seg, ref t);
            }
            return best == float.MaxValue ? float.MaxValue : math.sqrt(best);
        }

        private void Test(int i, float2 p, ref float best, ref int seg, ref float t)
        {
            var ab = B[i] - A[i]; float l2 = math.lengthsq(ab);
            float u = l2 > 1e-8f ? math.saturate(math.dot(p - A[i], ab) / l2) : 0f;
            float d = math.distancesq(p, A[i] + ab * u);
            if (d < best) { best = d; seg = i; t = u; }
        }
    }

    internal sealed class PolygonShape : PaintShape
    {
        private readonly float2[] _ring; private readonly SegGrid _edges; private readonly RingBands _bands;
        private readonly float _feather, _noise, _scale; private readonly uint _seed;

        private PolygonShape(float2[] ring, float feather, float noise, float scale, uint seed)
        {
            _ring = ring; _bands = new RingBands(ring); _feather = math.max(0.05f, feather); _noise = math.max(0f, noise); _scale = scale > 0f ? scale : 8f; _seed = seed;
            var segs = new List<(float2, float2)>(ring.Length);
            for (int i = 0; i < ring.Length; i++) segs.Add((ring[i], ring[(i + 1) % ring.Length]));
            _edges = new SegGrid(segs, _feather + 2f * _noise + 1f);
            Min = ring[0]; Max = ring[0];
            foreach (var v in ring) { Min = math.min(Min, v); Max = math.max(Max, v); }
        }

        public static PaintShape Make(IReadOnlyList<float2> ring, float feather, float noise, float scale, uint seed)
        {
            if (ring == null || ring.Count < 3) return None;
            var r = new float2[ring.Count];
            for (int i = 0; i < r.Length; i++) r[i] = ring[i];
            return new PolygonShape(r, feather, noise, scale, seed);
        }

        public override float At(float2 p)
        {
            if (!Inside(p)) return 0f;
            float d = _edges.Nearest(p, out _, out _);
            float reach = _feather + 2f * _noise;
            if (d >= reach) return 1f;
            float pull = _noise > 0f ? 2f * _noise * ValueNoise.Clumps(p, _scale, Hashing.Mix(_seed, 0x7A1Bu)) : 0f;
            return Smooth01((d - pull) / _feather);
        }

        private bool Inside(float2 p) => _bands.ContainsF(p);
    }

    internal sealed class StrokeShape : PaintShape
    {
        private readonly SegGrid _segs; private readonly float _half, _feather, _start, _end, _endFade, _wobble, _wobbleScale, _length;
        private readonly Falloff _along; private readonly uint _seed;

        private StrokeShape(List<(float2, float2)> segs, float length, float width, float feather, float start, float end, Falloff along,
            float endFade, float wobble, float wobbleScale, uint seed)
        {
            _half = math.max(0.05f, width * 0.5f); _feather = math.clamp(feather, 0.05f, _half * 2f);
            _start = math.saturate(start); _end = math.saturate(end); _along = along; _endFade = math.max(0f, endFade);
            _wobble = math.clamp(wobble, 0f, 0.8f); _wobbleScale = wobbleScale > 0f ? wobbleScale : math.max(3f, width * 2.5f); _seed = seed;
            _length = math.max(0.01f, length);
            float reach = _half * (1f + _wobble) + 0.5f;
            _segs = new SegGrid(segs, reach);
            Min = new float2(float.MaxValue); Max = new float2(float.MinValue);
            foreach (var (a, b) in segs) { Min = math.min(Min, math.min(a, b)); Max = math.max(Max, math.max(a, b)); }
            Min -= reach; Max += reach;
        }

        public static PaintShape Make(IReadOnlyList<float2> line, float width, float feather, float start, float end, Falloff along,
            float endFade, float wobble, float wobbleScale, uint seed)
        {
            var segs = new List<(float2, float2)>();
            float len = 0f;
            if (line != null)
                for (int i = 1; i < line.Count; i++)
                {
                    if (math.distancesq(line[i - 1], line[i]) < 1e-6f) continue;
                    segs.Add((line[i - 1], line[i]));
                    len += math.distance(line[i - 1], line[i]);
                }
            if (segs.Count == 0)
            {
                if (line != null && line.Count > 0) return Radial(line[0], math.max(0f, width * 0.5f - feather), width * 0.5f, Falloff.Smooth);
                return None;
            }
            return new StrokeShape(segs, len, width, feather, start, end, along, endFade, wobble, wobbleScale, seed);
        }

        public override float At(float2 p)
        {
            float d = _segs.Nearest(p, out int seg, out float t);
            if (seg < 0) return 0f;
            float s = _segs.S0[seg] + t * math.distance(_segs.A[seg], _segs.B[seg]);
            float half = _half;
            if (_wobble > 0f)
            {
                // 1D noise along the line (width breathing) + a little 2D noise (frayed edge).
                float w1 = ValueNoise.Sample(new float2(s / _wobbleScale, 0.37f), _seed) * 2f - 1f;
                float w2 = ValueNoise.Clumps(p, _wobbleScale * 0.5f, Hashing.Mix(_seed, 0x51u)) * 2f - 1f;
                half *= 1f + _wobble * (0.7f * w1 + 0.3f * w2);
            }
            float inside = half - d;
            if (inside <= 0f) return 0f;
            float edge = Smooth01(inside / math.min(_feather, half));
            float u = s / _length;
            float strength = math.lerp(_end, _start, Curve(_along, u));
            if (_endFade > 0f) strength *= Smooth01(s / _endFade) * Smooth01((_length - s) / _endFade);
            return edge * strength;
        }
    }

    /// <summary>Quantiles of ValueNoise.Clumps (measured once, deterministic) so "coverage" means a share of the ground.</summary>
    internal static class NoiseQuantiles
    {
        private static readonly float[] Q = Build();

        private static float[] Build()
        {
            const int n = 64 * 64;
            var v = new float[n];
            int k = 0;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    v[k++] = ValueNoise.Clumps(new float2(x * 0.731f + 0.13f, y * 0.677f + 0.29f), 1f, 0x51ED27u + (uint)((x / 16) * 4 + y / 16));
            Array.Sort(v);
            var q = new float[101];
            for (int i = 0; i <= 100; i++) q[i] = v[math.min(n - 1, i * (n - 1) / 100)];
            return q;
        }

        /// <summary>Noise value above which a share <paramref name="coverage"/> of points lies.</summary>
        public static float Threshold(float coverage)
        {
            float f = (1f - math.saturate(coverage)) * 100f;
            int i = math.clamp((int)math.floor(f), 0, 99);
            return math.lerp(Q[i], Q[i + 1], f - i);
        }
    }
}
