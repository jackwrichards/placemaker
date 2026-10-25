// (perf) Point-in-polygon for a ring tested many times (a raster over the outline, every planting candidate): the edges
// bucketed by horizontal bands, so a test looks only at the edges that cross its row instead of every corner. A painted
// outline has hundreds of corners; the plain even-odd loop over all of them was the largest shared cost of planning
// (ground wear masks, site harmony's raster). Same edges, same arithmetic as Poly.ContainsD and PolygonShape's own loop,
// so the answers are exactly theirs: an edge left out of a band cannot cross a row in it.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Geometry
{
    public sealed class RingBands
    {
        private readonly float2[] _ring;
        private readonly float _y0, _inv;
        private readonly int _bands;
        private readonly int[] _start, _edges;   // band b's edges: _edges[_start[b] .. _start[b + 1]), each the index i of edge (i, i - 1)

        /// <summary><paramref name="ring"/> is kept, not copied: it must not change while this is used.</summary>
        public RingBands(float2[] ring)
        {
            _ring = ring ?? Array.Empty<float2>();
            int n = _ring.Length;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in _ring) { lo = math.min(lo, p.y); hi = math.max(hi, p.y); }
            _bands = n < 16 || !(hi > lo) ? 1 : math.clamp(n / 2, 1, 2048);
            _y0 = n > 0 ? lo : 0f;
            _inv = _bands > 1 ? _bands / (hi - lo) : 0f;
            var count = new int[_bands + 1];
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Span(_ring[i].y, _ring[j].y, out int b0, out int b1);
                for (int b = b0; b <= b1; b++) count[b + 1]++;
            }
            _start = new int[_bands + 1];
            for (int b = 0; b < _bands; b++) _start[b + 1] = _start[b] + count[b + 1];
            _edges = new int[_start[_bands]];
            var fill = new int[_bands];
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Span(_ring[i].y, _ring[j].y, out int b0, out int b1);
                for (int b = b0; b <= b1; b++) _edges[_start[b] + fill[b]++] = i;
            }
        }

        /// <summary>The bands an edge from y = a to y = b touches, one band of slack each side (rounding at band borders).</summary>
        private void Span(float a, float b, out int b0, out int b1)
        {
            b0 = Band(math.min(a, b)) - 1; b1 = Band(math.max(a, b)) + 1;
            b0 = math.max(0, b0); b1 = math.min(_bands - 1, b1);
        }

        private int Band(float y) => _bands == 1 ? 0 : math.clamp((int)math.floor((y - _y0) * _inv), 0, _bands - 1);

        /// <summary>Exactly Poly.Contains(ring, p) (even-odd, in doubles).</summary>
        public bool Contains(float2 p)
        {
            int n = _ring.Length;
            if (n == 0) return false;
            double2 q = p;
            int b = Band(p.y);
            bool inside = false;
            for (int k = _start[b], end = _start[b + 1]; k < end; k++)
            {
                int i = _edges[k], j = i == 0 ? n - 1 : i - 1;
                double2 a = _ring[i], c = _ring[j];
                if ((a.y > q.y) != (c.y > q.y) && q.x < (c.x - a.x) * (q.y - a.y) / (c.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Exactly the even-odd test in floats (PolygonShape's).</summary>
        public bool ContainsF(float2 p)
        {
            int n = _ring.Length;
            if (n == 0) return false;
            int b = Band(p.y);
            bool c = false;
            for (int k = _start[b], end = _start[b + 1]; k < end; k++)
            {
                int i = _edges[k], j = i == 0 ? n - 1 : i - 1;
                var a = _ring[i]; var e = _ring[j];
                if ((a.y > p.y) != (e.y > p.y) && p.x < (e.x - a.x) * (p.y - a.y) / (e.y - a.y) + a.x) c = !c;
            }
            return c;
        }
    }
}
