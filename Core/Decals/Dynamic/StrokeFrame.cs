// Placemaker — dynamic decals: the geometry of one stroke, evaluated anywhere in world metres (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// Every texel of every tile asks the stroke the same questions (how far inside the footprint, how far along and across
// it), in world coordinates: a long path cut into several boxes is one continuous thing, and noise never repeats at a
// tile border. Thread-safe once built (read-only), so the tiles of one stroke generate in parallel.

using System;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public sealed class StrokeFrame
    {
        public readonly DecalStroke S;
        public readonly DecalKind K;
        private readonly float2[] _p;
        private readonly float[] _arc, _w;
        private readonly float _len;
        private readonly float2 _centroid;
        private readonly uint _seed;

        public float Length => _len;

        /// <summary>The shared library's fade for a box (VariantFade); applied to the edge distance, so coverage, scatter
        /// density and erosion all follow it.</summary>
        public VariantFade Fade { get; set; }

        /// <summary>Chain cracks (the shared library's crack pieces): the main crack runs from ChainA to ChainB.</summary>
        public bool HasChain { get; private set; }
        public float2 ChainA { get; private set; }
        public float2 ChainB { get; private set; }

        public void SetChain(float2 a, float2 b) { ChainA = a; ChainB = b; HasChain = true; }

        /// <summary>Repeating strips and sheets (the shared library): stamps are wrapped into one period starting at
        /// <see cref="PeriodOrigin"/> (0 on an axis = no repeat), so a stamp cut by one end comes back in at the other.</summary>
        public float2 PeriodOrigin { get; private set; }
        public float2 Period { get; private set; }
        public bool HasPeriod => Period.x > 0f || Period.y > 0f;

        public void SetPeriod(float2 origin, float2 period) { PeriodOrigin = origin; Period = period; }

        public float2 Wrap(float2 p)
        {
            if (Period.x > 0f) p.x = PeriodOrigin.x + (p.x - PeriodOrigin.x) - math.floor((p.x - PeriodOrigin.x) / Period.x) * Period.x;
            if (Period.y > 0f) p.y = PeriodOrigin.y + (p.y - PeriodOrigin.y) - math.floor((p.y - PeriodOrigin.y) / Period.y) * Period.y;
            return p;
        }

        /// <summary>0 at the footprint's middle, 1 at its edge (box: ellipse over the half size; path: across over half the width).</summary>
        public float Radial(float along, float across)
        {
            switch (S.Shape)
            {
                case StrokeShape.Box: return math.length(new float2(along, across) / math.max(new float2(0.05f), S.Size * 0.5f));
                case StrokeShape.Path: return math.abs(across) / math.max(0.05f, MaxWidth() * 0.5f);
                default: return math.length(new float2(along, across)) / math.max(0.5f, math.sqrt(math.max(1f, Geometry.Poly.Area(_p))) * 0.5f);
            }
        }

        /// <summary>Per-stroke work shared by its tiles (crack networks, stamp lists), built once under the lock.</summary>
        internal readonly System.Collections.Generic.Dictionary<DecalOp, object> Cache = new System.Collections.Generic.Dictionary<DecalOp, object>();
        internal readonly object Lock = new object();

        public StrokeFrame(DecalStroke s, DecalKind k)
        {
            S = s; K = k;
            _seed = DecalNoise.Hash(s.Seed, 0xD3CA1u);
            if (s.Shape == StrokeShape.Path)
            {
                int n = s.Points.Length;
                _p = s.Points;
                _arc = new float[n];
                _w = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _arc[i] = i == 0 ? 0f : _arc[i - 1] + math.distance(_p[i - 1], _p[i]);
                    _w[i] = i < s.Widths.Length ? s.Widths[i] : (s.Widths.Length > 0 ? s.Widths[s.Widths.Length - 1] : 1f);
                }
                _len = n > 0 ? _arc[n - 1] : 0f;
            }
            else if (s.Shape == StrokeShape.Outline)
            {
                _p = s.Points;
                _centroid = _p.Length >= 3 ? Geometry.Poly.Centroid(_p) : float2.zero;
            }
        }

        /// <summary>The footprint's soft coverage from a signed distance (metres inside).</summary>
        public float Shape(float edge) => K.Feather > 1e-4f ? math.smoothstep(0f, K.Feather, edge) : (edge > 0f ? 1f : 0f);

        /// <summary>
        /// Signed distance into the footprint (warped edge), along / across in metres, u 0..1 along, and the arc length of
        /// the nearest path point (paths; else 0).
        /// </summary>
        public float Eval(float2 wp, out float along, out float across, out float u, out float arc)
        {
            float2 q = wp;
            if (K.Warp > 1e-4f)
            {
                float ws = 1f / K.WarpScale;
                q += K.Warp * new float2(DecalNoise.Fbm(wp.x * ws, wp.y * ws, 3, _seed), DecalNoise.Fbm(wp.x * ws + 31.7f, wp.y * ws - 11.3f, 3, _seed + 7u));
            }
            switch (S.Shape)
            {
                case StrokeShape.Box: return EvalBox(wp, q, out along, out across, out u, out arc);
                case StrokeShape.Path: return EvalPath(wp, q, out along, out across, out u, out arc);
                default: return EvalOutline(wp, q, out along, out across, out u, out arc);
            }
        }

        private float EvalBox(float2 wp, float2 q, out float along, out float across, out float u, out float arc)
        {
            float c = math.cos(S.Yaw), s = math.sin(S.Yaw);
            float2 d = wp - S.Center, dq = q - S.Center;
            along = d.x * c - d.y * s; across = d.x * s + d.y * c;
            var lq = new float2(dq.x * c - dq.y * s, dq.x * s + dq.y * c);
            var h = math.max(new float2(0.05f), S.Size * 0.5f);
            float rect = math.min(h.x - math.abs(lq.x), h.y - math.abs(lq.y));
            float ell = (1f - math.length(lq / h)) * math.min(h.x, h.y);
            u = math.saturate(along / math.max(0.1f, S.Size.x) + 0.5f);
            arc = 0f;
            float e = math.lerp(rect, ell, K.Round);
            if (Fade == VariantFade.Soft) return e;
            float full = math.max(0.3f, K.Feather) * 1.25f;
            // a ragged, wandering fade line: never a straight cut across the patch
            float wob = full * 0.9f * DecalNoise.Fbm(wp.x / math.max(0.5f, h.x * 0.35f), wp.y / math.max(0.5f, h.y * 0.35f), 3, _seed + 57u);
            switch (Fade)
            {
                case VariantFade.Linear: return math.min(e, full * (h.x - lq.x) / (2f * h.x) + wob);
                case VariantFade.Side: return math.min(e, full * (h.y - lq.y) / (2f * h.y) + wob);
                case VariantFade.Ring:
                {
                    float r = math.length(lq / h);
                    return math.min(e, full * (1f - math.abs(r - 0.62f) * 2.4f) + wob * 0.6f);
                }
                case VariantFade.Islands:
                {
                    float sc = 1f / math.max(0.5f, math.min(h.x, h.y) * 0.55f);
                    return e + full * (1.4f * DecalNoise.Fbm(wp.x * sc, wp.y * sc, 3, _seed + 41u) - 0.35f);
                }
                default: return math.min(e * 0.35f, e);   // wide: the whole patch is one long fade
            }
        }

        private float EvalPath(float2 wp, float2 q, out float along, out float across, out float u, out float arc)
        {
            along = across = u = arc = 0f;
            int n = _p.Length;
            if (n < 2) return -1000f;
            // nearest segment to the unwarped point (along/across), and to the warped one (the edge)
            float best = float.MaxValue, bestQ = float.MaxValue; int bi = 0, bq = 0; float bt = 0f, btq = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float2 a = _p[i], ab = _p[i + 1] - a;
                float l2 = math.max(1e-6f, math.dot(ab, ab));
                float t = math.saturate(math.dot(wp - a, ab) / l2);
                float d2 = math.distancesq(wp, a + ab * t);
                if (d2 < best) { best = d2; bi = i; bt = t; }
                float tq = math.saturate(math.dot(q - a, ab) / l2);
                float dq2 = math.distancesq(q, a + ab * tq);
                if (dq2 < bestQ) { bestQ = dq2; bq = i; btq = tq; }
            }
            {
                float2 a = _p[bi], ab = _p[bi + 1] - a;
                float2 tan = math.normalizesafe(ab, new float2(1f, 0f));
                float2 foot = a + ab * bt;
                arc = math.lerp(_arc[bi], _arc[bi + 1], bt);
                along = arc;
                across = math.dot(wp - foot, new float2(-tan.y, tan.x));
                u = _len > 1e-3f ? arc / _len : 0f;
            }
            float sq = math.lerp(_arc[bq], _arc[bq + 1], btq);
            float half = 0.5f * math.lerp(_w[bq], _w[bq + 1], btq);
            if (K.WidthNoise > 1e-4f) half *= math.max(0.15f, 1f + K.WidthNoise * DecalNoise.Fbm(sq / (K.WarpScale * 1.7f), 0.5f, 3, _seed + 3u));
            if (K.Taper > 1e-4f) half *= math.smoothstep(0f, K.Taper, sq) * math.smoothstep(0f, K.Taper, _len - sq) * 0.85f + 0.15f;
            return half - math.sqrt(bestQ);
        }

        private float EvalOutline(float2 wp, float2 q, out float along, out float across, out float u, out float arc)
        {
            along = wp.x - _centroid.x; across = wp.y - _centroid.y; u = 0.5f; arc = 0f;
            if (_p == null || _p.Length < 3) return -1000f;
            bool inside = false;
            float best = float.MaxValue;
            for (int i = 0, j = _p.Length - 1; i < _p.Length; j = i++)
            {
                float2 a = _p[j], b = _p[i];
                if ((b.y > q.y) != (a.y > q.y) && q.x < (a.x - b.x) * (q.y - b.y) / (a.y - b.y + 1e-12f) + b.x) inside = !inside;
                float2 ab = b - a;
                float t = math.saturate(math.dot(q - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
                best = math.min(best, math.distancesq(q, a + ab * t));
            }
            float d = math.sqrt(best);
            return inside ? d : -d;
        }

        /// <summary>The point and direction at arc length s along a path.</summary>
        public void PathAt(float s, out float2 p, out float2 dir)
        {
            p = float2.zero; dir = new float2(1f, 0f);
            if (S.Shape != StrokeShape.Path || _p.Length < 2) { p = S.Center; return; }
            s = math.clamp(s, 0f, _len);
            int i = Array.BinarySearch(_arc, s);
            if (i < 0) i = ~i;
            i = math.clamp(i, 1, _p.Length - 1);
            float f = (s - _arc[i - 1]) / math.max(1e-5f, _arc[i] - _arc[i - 1]);
            p = math.lerp(_p[i - 1], _p[i], f);
            dir = math.normalizesafe(_p[i] - _p[i - 1], new float2(1f, 0f));
        }

        public float MaxWidth()
        {
            if (S.Shape != StrokeShape.Path) return 0f;
            float m = 0f;
            foreach (var w in _w) m = math.max(m, w);
            return m;
        }
    }
}
