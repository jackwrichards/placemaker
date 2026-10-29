// Placemaker — dynamic decals: the interpreter that turns a kind (recipe) and a stroke into a tile's textures
// (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// Pure .NET + Unity.Mathematics, no Unity engine API: runs on worker threads in the game and in the tests. Deterministic
// from the stroke's seed. Output: three RGBA32 maps per tile, row 0 = the box's −z edge (v 0), as the game's decal
// shader reads them: albedo (sRGB, alpha = opacity), normal (RG = xy, A = 1) and mask (R metal, G AO, B 1, A smoothness).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public sealed class DecalImage
    {
        public int W, H;
        public byte[] Albedo, Normal, Mask;
        /// <summary>Texels with any opacity, and with opacity over 0.5.</summary>
        public int Covered, Strong;
        public double Ms;
    }

    public static class DecalGenerator
    {
        private struct Stamp { public float2 P; public float Ang, Len, Wid, A, H, Sm; public float3 Col; public StampKind Kind; public uint Hash; }
        private struct Seg { public float2 A, B; public float W; }

        public static DecalImage Generate(StrokeFrame f, DecalTile t)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var k = f.K; var s = f.S;
            int W = t.W, H = t.H, N = W * H;
            var fields = new float[k.Fields.Count][];
            for (int i = 0; i < DecalKind.BuiltinCount; i++) fields[i] = new float[N];
            var wx = new float[N]; var wz = new float[N];
            var own = new bool[N];
            // texels well outside the footprint (or owned by a neighbouring tile) draw nothing: every op skips them
            var act = new bool[N];
            float c = math.cos(t.Yaw), sn = math.sin(t.Yaw);
            float2 World(float x, float y)
            {
                float lx = ((x + 0.5f) / W - 0.5f) * t.Size.x, lz = ((y + 0.5f) / H - 0.5f) * t.Size.y;
                return t.Center + new float2(lx * c + lz * sn, -lx * sn + lz * c);
            }
            // The stroke's geometry (warped edge, along, across) is smooth at texel scale: evaluate it every 4th texel and
            // interpolate (16x fewer evaluations; the warp's finest detail is about half a metre).
            const int Step = 4;
            int gw = (W - 1) / Step + 2, gh = (H - 1) / Step + 2;
            var ge = new float[gw * gh]; var ga = new float[gw * gh]; var gc = new float[gw * gh]; var gu = new float[gw * gh]; var gr = new float[gw * gh];
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    int g = gy * gw + gx;
                    ge[g] = f.Eval(World(math.min(gx * Step, W - 1), math.min(gy * Step, H - 1)), out ga[g], out gc[g], out gu[g], out gr[g]);
                }
            for (int y = 0; y < H; y++)
            {
                int gy = y / Step;
                int y0 = gy * Step, y1 = math.min(y0 + Step, H - 1);
                float fy = y1 > y0 ? (y - y0) / (float)(y1 - y0) : 0f;
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int gx = x / Step;
                    int x0 = gx * Step, x1 = math.min(x0 + Step, W - 1);
                    float fx = x1 > x0 ? (x - x0) / (float)(x1 - x0) : 0f;
                    int g00 = gy * gw + gx, g10 = g00 + 1, g01 = g00 + gw, g11 = g01 + 1;
                    float e = Bilerp(ge, g00, g10, g01, g11, fx, fy), arc = Bilerp(gr, g00, g10, g01, g11, fx, fy);
                    var wp = World(x, y);
                    wx[i] = wp.x; wz[i] = wp.y;
                    own[i] = arc >= t.S0 - 0.05f && arc <= t.S1 + 0.05f;
                    act[i] = own[i] && e > -0.75f;
                    fields[DecalKind.BuiltinShape][i] = f.Shape(e);
                    fields[DecalKind.BuiltinEdge][i] = e;
                    fields[DecalKind.BuiltinAlong][i] = Bilerp(ga, g00, g10, g01, g11, fx, fy);
                    fields[DecalKind.BuiltinAcross][i] = Bilerp(gc, g00, g10, g01, g11, fx, fy);
                    fields[DecalKind.BuiltinU][i] = Bilerp(gu, g00, g10, g01, g11, fx, fy);
                    fields[DecalKind.BuiltinOne][i] = 1f;
                }
            }
            // composite
            var R = new float[N]; var G = new float[N]; var B = new float[N]; var A = new float[N];
            var Hh = new float[N]; var Sm = new float[N]; var Ao = new float[N];
            float3 base0 = new float3(0.35f, 0.3f, 0.25f);
            foreach (var op in k.Ops) if (op.Kind == DecalOpKind.Layer) { base0 = op.Color; break; }
            for (int i = 0; i < N; i++) { R[i] = base0.x; G[i] = base0.y; B[i] = base0.z; Sm[i] = 0.25f; Ao[i] = 1f; }

            uint seed = DecalNoise.Hash(s.Seed, 0x9A11u);
            foreach (var op in k.Ops)
            {
                float[] o = op.Out >= 0 ? (fields[op.Out] ?? (fields[op.Out] = new float[N])) : null;
                switch (op.Kind)
                {
                    case DecalOpKind.Fbm:
                    {
                        float inv = 1f / math.max(0.01f, op.Scale.Eval(s)), warp = op.Warp.Eval(s), ws = 1f / math.max(0.05f, op.WarpScale.Eval(s)), gain = op.Gain.Eval(s);
                        uint sd = seed + op.Salt;
                        bool stretch = op.Stretch.x != 1f || op.Stretch.y != 1f;
                        for (int i = 0; i < N; i++)
                        {
                            if (!act[i]) continue;
                            float px = wx[i], pz = wz[i];
                            if (stretch)
                            {
                                // anisotropy in the stroke's frame: along / across
                                px = fields[DecalKind.BuiltinAlong][i] / op.Stretch.x; pz = fields[DecalKind.BuiltinAcross][i] / op.Stretch.y;
                            }
                            if (warp > 0f)
                            {
                                float qx = DecalNoise.Fbm(px * ws, pz * ws, 2, sd + 11u), qz = DecalNoise.Fbm(px * ws + 13.1f, pz * ws - 7.7f, 2, sd + 12u);
                                px += warp * qx; pz += warp * qz;
                            }
                            o[i] = op.Ridged ? DecalNoise.Ridged(px * inv, pz * inv, op.Octaves, sd, gain) : DecalNoise.Fbm(px * inv, pz * inv, op.Octaves, sd, gain);
                        }
                        break;
                    }
                    case DecalOpKind.Worley:
                    {
                        float sc = math.max(0.02f, op.Scale.Eval(s)), inv = 1f / sc, warp = op.Warp.Eval(s), ws = 1f / math.max(0.05f, op.WarpScale.Eval(s));
                        uint sd = seed + op.Salt;
                        for (int i = 0; i < N; i++)
                        {
                            if (!act[i]) continue;
                            float px = wx[i], pz = wz[i];
                            if (warp > 0f) { px += warp * DecalNoise.Fbm(px * ws, pz * ws, 2, sd + 3u); pz += warp * DecalNoise.Fbm(px * ws + 9f, pz * ws, 2, sd + 4u); }
                            var w = DecalNoise.Worley(px * inv, pz * inv, sd, op.Jitter);
                            o[i] = (op.Edge ? w.y - w.x : w.x) * sc;   // metres
                        }
                        break;
                    }
                    case DecalOpKind.Remap:
                    {
                        float lo = op.Lo.Eval(s), hi = op.Hi.Eval(s), kk = op.K.Eval(s);
                        for (int i = 0; i < N; i++)
                        {
                            if (!act[i]) continue;
                            float v = In(op.A, fields, i);
                            o[i] = (lo <= hi ? math.smoothstep(lo, hi, v) : 1f - math.smoothstep(hi, lo, v)) * kk;
                        }
                        break;
                    }
                    case DecalOpKind.Band:
                    {
                        float at = op.Lo.Eval(s), w = math.max(1e-4f, op.Width.Eval(s)), kk = op.K.Eval(s);
                        for (int i = 0; i < N; i++) if (act[i]) o[i] = (1f - math.smoothstep(0f, w, math.abs(In(op.A, fields, i) - at))) * kk;
                        break;
                    }
                    case DecalOpKind.Mul: case DecalOpKind.Add: case DecalOpKind.Sub: case DecalOpKind.Max: case DecalOpKind.Min:
                    {
                        float kk = op.K.Eval(s);
                        bool third = !float.IsNaN(op.C.Const) || op.C.Field >= 0;
                        for (int i = 0; i < N; i++)
                        {
                            if (!act[i]) continue;
                            float a = In(op.A, fields, i), b = In(op.B, fields, i), r;
                            switch (op.Kind)
                            {
                                case DecalOpKind.Mul: r = a * b; if (third) r *= In(op.C, fields, i); break;
                                case DecalOpKind.Add: r = a + b; if (third) r += In(op.C, fields, i); break;
                                case DecalOpKind.Sub: r = a - b; break;
                                case DecalOpKind.Max: r = math.max(a, b); if (third) r = math.max(r, In(op.C, fields, i)); break;
                                default: r = math.min(a, b); if (third) r = math.min(r, In(op.C, fields, i)); break;
                            }
                            o[i] = r * kk;
                        }
                        break;
                    }
                    case DecalOpKind.Erode:
                    {
                        // height-blend edge erosion: soft coverage breaks into grains that fill the low spots first
                        float sharp = op.Sharp.Eval(s);
                        for (int i = 0; i < N; i++)
                        {
                            if (!act[i]) continue;
                            float cov = In(op.A, fields, i), g = math.saturate(In(op.B, fields, i));
                            o[i] = math.saturate((cov - (1f - g)) * sharp + cov * cov * 0.15f);
                        }
                        break;
                    }
                    case DecalOpKind.Cracks:
                        Cracks(f, t, op, fields, o, wx, wz, seed, act);
                        break;
                    case DecalOpKind.Scatter:
                        Scatter(f, t, op, R, G, B, A, Hh, Sm, Ao, seed);
                        break;
                    case DecalOpKind.Layer:
                    {
                        float ak = op.AlphaK.Eval(s), vary = op.Vary.Eval(s), vs = 1f / math.max(0.05f, op.VaryScale.Eval(s));
                        float sm0 = op.Smooth.Eval(s), sm1 = op.Smooth2.Eval(s), hk = op.HeightK.Eval(s), ao = op.Ao.Eval(s);
                        uint sd = seed + op.Salt + 5u;
                        for (int i = 0; i < N; i++)
                        {
                            if (!act[i]) continue;
                            float a = math.saturate(In(op.Alpha, fields, i) * ak);
                            if (a <= 1e-4f) continue;
                            float tn = math.saturate(In(op.Tone, fields, i) * 0.5f + 0.5f);
                            float3 col = math.lerp(op.Color, op.Color2, tn);
                            if (vary > 0f) col *= 1f + vary * DecalNoise.Perlin(wx[i] * vs, wz[i] * vs, sd);
                            float sm = math.lerp(sm0, sm1, math.saturate(In(op.SmoothBy, fields, i)));
                            float h = In(op.Height, fields, i) * hk;
                            Over(i, col, a, h, sm, ao, R, G, B, A, Hh, Sm, Ao);
                        }
                        break;
                    }
                }
            }
            float strength = math.saturate(s.Strength) * k.Opacity;
            var img = new DecalImage { W = W, H = H, Albedo = new byte[N * 4], Normal = new byte[N * 4], Mask = new byte[N * 4] };
            float tx = t.Size.x / W, tz = t.Size.y / H;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x, q = i * 4;
                    float a = own[i] ? math.saturate(A[i] * strength) : 0f;
                    if (a > 0.004f) img.Covered++;
                    if (a > 0.5f) img.Strong++;
                    img.Albedo[q] = Byte(R[i]); img.Albedo[q + 1] = Byte(G[i]); img.Albedo[q + 2] = Byte(B[i]); img.Albedo[q + 3] = Byte(a);
                    float hl = Hh[y * W + math.max(0, x - 1)], hr = Hh[y * W + math.min(W - 1, x + 1)];
                    float hd = Hh[math.max(0, y - 1) * W + x], hu = Hh[math.min(H - 1, y + 1) * W + x];
                    var n = math.normalize(new float3(-(hr - hl) * k.Bump / (2f * tx), -(hu - hd) * k.Bump / (2f * tz), 1f));
                    img.Normal[q] = Byte(n.x * 0.5f + 0.5f); img.Normal[q + 1] = Byte(n.y * 0.5f + 0.5f); img.Normal[q + 2] = Byte(n.z * 0.5f + 0.5f); img.Normal[q + 3] = 255;
                    img.Mask[q] = 0; img.Mask[q + 1] = Byte(Ao[i]); img.Mask[q + 2] = 255; img.Mask[q + 3] = Byte(Sm[i]);
                }
            img.Ms = clock.Elapsed.TotalMilliseconds;
            return img;
        }

        private static float Bilerp(float[] v, int g00, int g10, int g01, int g11, float fx, float fy) =>
            math.lerp(math.lerp(v[g00], v[g10], fx), math.lerp(v[g01], v[g11], fx), fy);

        private static float In(DecalInput d, float[][] fields, int i) => d.Field >= 0 ? (fields[d.Field] != null ? fields[d.Field][i] : 0f) : d.Const;

        private static byte Byte(float v) => (byte)math.clamp((int)(v * 255f + 0.5f), 0, 255);

        /// <summary>Porter-Duff "over" of one material onto the texel; transparent texels keep their colour (clean filtering).</summary>
        private static void Over(int i, float3 col, float a, float h, float sm, float ao, float[] R, float[] G, float[] B, float[] A, float[] Hh, float[] Sm, float[] Ao)
        {
            float a0 = A[i], ao2 = a + a0 * (1f - a);
            if (ao2 > 1e-5f)
            {
                float w0 = a0 * (1f - a) / ao2, w1 = a / ao2;
                R[i] = col.x * w1 + R[i] * w0; G[i] = col.y * w1 + G[i] * w0; B[i] = col.z * w1 + B[i] * w0;
                if (a0 <= 1e-4f) { R[i] = col.x; G[i] = col.y; B[i] = col.z; }
            }
            A[i] = ao2;
            Hh[i] = math.lerp(Hh[i], h, a);
            Sm[i] = a0 <= 1e-4f ? sm : math.lerp(Sm[i], sm, a);
            Ao[i] = math.lerp(Ao[i], ao, a);
        }

        // ───────────── cracks: random walks that end in T-junctions ─────────────

        private sealed class CrackNet
        {
            public readonly List<Seg> Segs = new List<Seg>();
            public readonly Dictionary<long, List<int>> Grid = new Dictionary<long, List<int>>();
            public float MaxW;
            public static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

            public void Add(Seg sg)
            {
                int id = Segs.Count;
                Segs.Add(sg);
                MaxW = math.max(MaxW, sg.W);
                var lo = math.min(sg.A, sg.B); var hi = math.max(sg.A, sg.B);
                for (int y = (int)math.floor(lo.y); y <= (int)math.floor(hi.y); y++)
                    for (int x = (int)math.floor(lo.x); x <= (int)math.floor(hi.x); x++)
                    {
                        long kk = Key(x, y);
                        if (!Grid.TryGetValue(kk, out var l)) Grid[kk] = l = new List<int>();
                        l.Add(id);
                    }
            }

            /// <summary>Where a new segment p→q first meets an existing one (not the last few of its own crack).</summary>
            public bool Hits(float2 p, float2 q, int skipFrom, out float2 at)
            {
                at = q;
                float bestT = 2f;
                var lo = math.min(p, q); var hi = math.max(p, q);
                for (int y = (int)math.floor(lo.y); y <= (int)math.floor(hi.y); y++)
                    for (int x = (int)math.floor(lo.x); x <= (int)math.floor(hi.x); x++)
                        if (Grid.TryGetValue(Key(x, y), out var l))
                            foreach (int id in l)
                            {
                                if (id >= skipFrom) continue;
                                var sg = Segs[id];
                                if (Geometry.Poly.SegmentIntersection(p, q, sg.A, sg.B, out var pt, out float tp, out _) && tp > 1e-3f && tp < bestT) { bestT = tp; at = pt; }
                            }
                return bestT <= 1f;
            }
        }

        private static CrackNet Net(StrokeFrame f, DecalOp op, uint seed)
        {
            lock (f.Lock)
            {
                if (f.Cache.TryGetValue(op, out var o)) return (CrackNet)o;
                var s = f.S;
                var net = new CrackNet();
                uint h = seed + op.Salt * 31u;
                float Rnd() { h = DecalNoise.Hash(h, 0x2545F491u); return DecalNoise.Unit(h); }
                float Gauss() { float a = math.max(1e-6f, Rnd()), b = Rnd(); return math.sqrt(-2f * math.log(a)) * math.cos(6.2831853f * b); }
                s.Bounds(out var lo, out var hi);
                float area = math.max(1f, s.Area());
                int seeds = math.clamp((int)math.round(area * op.Seeds.Eval(s)), 1, 160);
                float step = math.max(0.1f, op.Step.Eval(s)), turn = op.Turn.Eval(s), branch = op.Branch.Eval(s);
                var stack = new Stack<(float2 p, float ang, float len, float w, int depth)>();
                for (int n = 0; n < seeds; n++)
                {
                    float2 p0 = default; bool found = false;
                    for (int tries = 0; tries < 24 && !found; tries++)
                    {
                        p0 = math.lerp(lo, hi, new float2(Rnd(), Rnd()));
                        found = f.Eval(p0, out _, out _, out _, out _) > 0.3f;
                    }
                    if (!found) continue;
                    float w0 = math.lerp(op.CrackWidth.x, op.CrackWidth.y, Rnd());
                    float l0 = math.lerp(op.Len.x, op.Len.y, Rnd());
                    float a0 = Rnd() * 6.2831853f;
                    // a crack grows both ways from its start
                    stack.Push((p0, a0, l0 * 0.55f, w0, 0));
                    stack.Push((p0, a0 + 3.14159f, l0 * 0.45f, w0, 0));
                    while (stack.Count > 0 && net.Segs.Count < 6000)
                    {
                        var (p, ang, len, w, depth) = stack.Pop();
                        int start = net.Segs.Count;
                        float walked = 0f;
                        while (walked < len)
                        {
                            ang += Gauss() * turn * math.sqrt(step);
                            float2 q = p + new float2(math.cos(ang), math.sin(ang)) * step;
                            if (f.Eval(q, out _, out _, out _, out _) < -0.2f) break;
                            float wt = w * math.lerp(1f, 0.35f, walked / len);
                            if (net.Hits(p, q, math.max(0, start - 0), out var at) && walked > step * 0.5f)
                            {
                                net.Add(new Seg { A = p, B = at, W = wt });   // T-junction: stops where it meets an older crack
                                break;
                            }
                            net.Add(new Seg { A = p, B = q, W = wt });
                            p = q; walked += step;
                            if (depth < 2 && Rnd() < branch * step / math.max(1f, len) * 2.2f)
                                stack.Push((p, ang + (Rnd() < 0.5f ? -1f : 1f) * math.lerp(0.9f, 1.6f, Rnd()), (len - walked) * math.lerp(0.4f, 0.8f, Rnd()) + 1f, wt * 0.7f, depth + 1));
                        }
                    }
                }
                f.Cache[op] = net;
                return net;
            }
        }

        private static void Cracks(StrokeFrame f, DecalTile t, DecalOp op, float[][] fields, float[] o, float[] wx, float[] wz, uint seed, bool[] act)
        {
            var net = Net(f, op, seed);
            var s = f.S;
            int N = o.Length;
            float texel = math.min(t.Size.x / t.W, t.Size.y / t.H);
            float warp = op.Warp.Eval(s);
            float halo = 0.3f;
            for (int i = 0; i < N; i++)
            {
                if (!act[i]) continue;
                var p = new float2(wx[i], wz[i]);
                if (warp > 0f) p += warp * new float2(DecalNoise.Fbm(p.x * 2.1f, p.y * 2.1f, 2, seed + 91u), DecalNoise.Fbm(p.x * 2.1f + 5f, p.y * 2.1f, 2, seed + 92u));
                int cx = (int)math.floor(p.x), cy = (int)math.floor(p.y);
                float best = 99f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (net.Grid.TryGetValue(CrackNet.Key(cx + dx, cy + dy), out var l))
                            foreach (int id in l)
                            {
                                var sg = net.Segs[id];
                                float2 ab = sg.B - sg.A;
                                float tt = math.saturate(math.dot(p - sg.A, ab) / math.max(1e-6f, math.dot(ab, ab)));
                                float d = math.distance(p, sg.A + ab * tt) - sg.W * 0.5f;
                                if (d < best) best = d;
                            }
                // thin lines stay visible: coverage over at least ~0.7 texel
                o[i] = 1f - math.smoothstep(-texel * 0.35f, texel * 0.75f, best);
                if (op.B.Field > 0)
                {
                    var hf = fields[op.B.Field] ?? (fields[op.B.Field] = new float[N]);
                    hf[i] = 1f - math.smoothstep(0f, halo, best);
                }
            }
        }

        // ───────────── scatter: stamps in clusters of clusters ─────────────

        private static List<Stamp> Stamps(StrokeFrame f, DecalOp op, uint seed)
        {
            lock (f.Lock)
            {
                if (f.Cache.TryGetValue(op, out var o)) return (List<Stamp>)o;
                var s = f.S;
                var list = new List<Stamp>();
                uint h = seed + op.Salt * 131u;
                float Rnd() { h = DecalNoise.Hash(h, 0x68E31DA4u); return DecalNoise.Unit(h); }
                float Gauss() { float a = math.max(1e-6f, Rnd()), b = Rnd(); return math.sqrt(-2f * math.log(a)) * math.cos(6.2831853f * b); }
                s.Bounds(out var lo, out var hi);
                float pad = f.K.Warp + 0.5f;
                lo -= pad; hi += pad;
                float area = math.max(0.5f, s.Area());
                float density = op.Density.Eval(s);
                int target = math.min(op.MaxStamps, (int)(area * density));
                int nClusters = math.max(1, (int)math.round(area / 100f * op.Clusters.Eval(s)));
                float share = math.saturate(op.ClusterShare.Eval(s));
                var centres = new List<(float2 p, float r)>();
                for (int n = 0; n < nClusters * 4 && centres.Count < nClusters; n++)
                {
                    var p = math.lerp(lo, hi, new float2(Rnd(), Rnd()));
                    if (Weight(f, op, p, seed) > Rnd() * 0.8f) centres.Add((p, math.lerp(op.ClusterRadius.x, op.ClusterRadius.y, Rnd() * Rnd())));
                }
                int attempts = target * 4 + 16;
                for (int n = 0; n < attempts && list.Count < target; n++)
                {
                    float2 p;
                    if (centres.Count > 0 && Rnd() < share)
                    {
                        var cc = centres[(int)(Rnd() * centres.Count) % centres.Count];
                        // a cluster has sub-clusters: two levels of gaussian offsets
                        var off = new float2(Gauss(), Gauss()) * cc.r * 0.6f + new float2(Gauss(), Gauss()) * cc.r * 0.25f;
                        if (s.Shape == StrokeShape.Path)
                        {
                            // along a path a cluster is a streak: long along it, thin across (drifts, ruts, spills)
                            f.Eval(cc.p, out float ca, out _, out _, out _);
                            f.PathAt(ca, out _, out var dir);
                            var nrm = new float2(-dir.y, dir.x);
                            off = dir * (off.x * 2.2f) + nrm * (off.y * 0.45f);
                        }
                        p = cc.p + off;
                    }
                    else p = math.lerp(lo, hi, new float2(Rnd(), Rnd()));
                    float w = Weight(f, op, p, seed);
                    if (Rnd() >= w) continue;
                    float len = math.lerp(op.Size.x, op.Size.y, Rnd());
                    float wid = len * math.lerp(op.Aspect.x, op.Aspect.y, Rnd());
                    float ang = Rnd() * 6.2831853f;
                    if (op.AlongPath && s.Shape == StrokeShape.Path)
                    {
                        f.Eval(p, out float along, out _, out _, out _);
                        f.PathAt(along, out _, out var dir);
                        ang = math.atan2(dir.y, dir.x) + Gauss() * 0.12f;
                    }
                    var col = op.Palette[(int)(Rnd() * op.Palette.Length) % op.Palette.Length] * (1f + (Rnd() * 2f - 1f) * op.ValueVary);
                    list.Add(new Stamp
                    {
                        P = p, Ang = ang, Len = len, Wid = wid, Col = col, A = math.lerp(op.StampAlpha.x, op.StampAlpha.y, Rnd()) * math.saturate(0.35f + w),
                        H = op.StampHeight, Sm = op.StampSmooth, Kind = op.Stamp, Hash = h,
                    });
                }
                f.Cache[op] = list;
                return list;
            }
        }

        /// <summary>Where stamps land: inside the footprint, thinned by noise, along the path (ramp), toward one side or the edge.</summary>
        private static float Weight(StrokeFrame f, DecalOp op, float2 p, uint seed)
        {
            float e = f.Eval(p, out _, out float across, out float u, out _);
            if (e < -0.2f) return 0f;
            float w = math.saturate(f.Shape(e) * 1.2f);
            if (op.NoiseScale > 0f)
                w *= math.smoothstep(op.NoiseLo, op.NoiseHi, DecalNoise.Fbm(p.x / op.NoiseScale, p.y / op.NoiseScale, 3, seed + op.Salt + 77u));
            if (op.Ramp.x != 1f || op.Ramp.y != 1f) w *= math.saturate(math.lerp(op.Ramp.x, op.Ramp.y, u));
            if (op.Side != 0f)
            {
                // drifts pile against one side of a path (a kerb, a fence): across > 0 is the left of the direction of travel
                float half = 0.5f * math.max(0.3f, f.MaxWidth());
                float side = math.saturate(across / half * math.sign(op.Side) * 0.5f + 0.5f);
                w *= math.lerp(1f, side * side * 2f, math.saturate(math.abs(op.Side)));
            }
            if (op.EdgeBias != 0f) w *= math.lerp(1f, 1f - math.saturate(e / math.max(0.2f, math.abs(op.EdgeBias))), op.EdgeBias > 0f ? 0.85f : 0f);
            return math.saturate(w);
        }

        private static void Scatter(StrokeFrame f, DecalTile t, DecalOp op, float[] R, float[] G, float[] B, float[] A, float[] Hh, float[] Sm, float[] Ao, uint seed)
        {
            var stamps = Stamps(f, op, seed);
            int W = t.W, H = t.H;
            float tx = t.Size.x / W, tz = t.Size.y / H;
            float halfX = t.Size.x * 0.5f, halfZ = t.Size.y * 0.5f;
            float tc = math.cos(t.Yaw), ts = math.sin(t.Yaw);
            foreach (var st in stamps)
            {
                var lp = t.ToLocal(st.P);
                float r = st.Len * 0.6f + tx;
                if (lp.x < -halfX - r || lp.x > halfX + r || lp.y < -halfZ - r || lp.y > halfZ + r) continue;
                // the stamp's axis in tile-local coordinates (world angle → local)
                float2 wa = new float2(math.cos(st.Ang), math.sin(st.Ang));
                float2 ax = new float2(wa.x * tc - wa.y * ts, wa.x * ts + wa.y * tc);
                float2 ay = new float2(-ax.y, ax.x);
                int x0 = math.max(0, (int)((lp.x - r + halfX) / tx)), x1 = math.min(W - 1, (int)((lp.x + r + halfX) / tx));
                int y0 = math.max(0, (int)((lp.y - r + halfZ) / tz)), y1 = math.min(H - 1, (int)((lp.y + r + halfZ) / tz));
                float hl = st.Len * 0.5f, hw = math.max(1e-3f, st.Wid * 0.5f);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var d = new float2((x + 0.5f) * tx - halfX, (y + 0.5f) * tz - halfZ) - lp;
                        float a = math.dot(d, ax) / hl, b = math.dot(d, ay) / hw;
                        float cov, height, shade = 1f;
                        switch (st.Kind)
                        {
                            case StampKind.Leaf:
                            {
                                float e = a * a + b * b * (1f + 0.6f * a * a);
                                if (e > 1f) continue;
                                cov = math.smoothstep(1f, 0.7f, e);
                                shade = 1f - (1f - math.smoothstep(0.02f, 0.12f, math.abs(b))) * 0.25f;
                                height = st.H * (1f - e) + 0.15f;
                                break;
                            }
                            case StampKind.Pebble:
                            {
                                float wob = 1f + 0.18f * DecalNoise.Perlin(a * 1.7f + st.Hash % 97, b * 1.7f, st.Hash);
                                float e = (a * a + b * b) / (wob * wob);
                                if (e > 1f) continue;
                                cov = math.smoothstep(1f, 0.6f, e);
                                height = st.H * math.sqrt(math.max(0f, 1f - e));
                                shade = 0.8f + 0.35f * math.saturate(-a * 0.5f - b * 0.5f + 0.4f);
                                break;
                            }
                            case StampKind.Print:
                            {
                                // a shoe: sole (front 60 %) and heel, pressed in
                                float sole = (a + 0.3f) * (a + 0.3f) / 0.5f + b * b / 0.95f;
                                float heel = (a - 0.62f) * (a - 0.62f) / 0.12f + b * b / 0.7f;
                                float e = math.min(sole, heel);
                                if (e > 1f) continue;
                                cov = math.smoothstep(1f, 0.55f, e) * (0.75f + 0.25f * DecalNoise.Perlin(a * 4f, b * 4f, st.Hash));
                                height = -st.H * (1f - e);
                                break;
                            }
                            case StampKind.Twig:
                            {
                                if (math.abs(a) > 1f || math.abs(b) > 0.25f) continue;
                                cov = math.smoothstep(0.25f, 0.08f, math.abs(b)) * math.smoothstep(1f, 0.85f, math.abs(a));
                                height = st.H;
                                break;
                            }
                            case StampKind.Tread:
                            {
                                float e = a * a + b * b;
                                if (e > 1f) continue;
                                float bars = 0.5f + 0.5f * math.sin(a * 18f);
                                cov = math.smoothstep(1f, 0.6f, e) * math.lerp(0.35f, 1f, bars);
                                height = -st.H * bars;
                                break;
                            }
                            case StampKind.Litter:
                            {
                                if (math.abs(a) > 1f || math.abs(b) > 1f) continue;
                                cov = math.smoothstep(1f, 0.85f, math.abs(a)) * math.smoothstep(1f, 0.8f, math.abs(b));
                                height = st.H * 0.3f;
                                break;
                            }
                            default:   // blob: a soft round drop with a ragged rim
                            {
                                float wob = 1f + 0.3f * DecalNoise.Perlin(a * 1.3f + (st.Hash & 255), b * 1.3f, st.Hash);
                                float e = (a * a + b * b) / (wob * wob);
                                if (e > 1f) continue;
                                cov = math.smoothstep(1f, 0.25f, e);
                                height = st.H * (1f - e);
                                break;
                            }
                        }
                        float al = cov * st.A;
                        if (al <= 1e-3f) continue;
                        Over(y * W + x, st.Col * shade, al, height, st.Sm, 1f - cov * 0.15f, R, G, B, A, Hh, Sm, Ao);
                    }
            }
        }
    }
}
