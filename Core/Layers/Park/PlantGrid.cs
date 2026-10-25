// (perf) Quick answers for the park's planting checks (ParkPlanting.PlantFits), on a grid over the park.
//
// Planting is rejection sampling: a big park throws ~1 million candidates to keep ~4 000 plants, and each candidate ran
// every check in full (inside the outline, gates, lake, clearing, paths, reserved polygons, spacing to the other plants).
// Two grids answer most of them in a lookup, and only ever with the answer the full check gives:
//   * the fixed checks per cell: every distance they test is 1-Lipschitz, so its value at the cell centre ± the cell's half
//     diagonal bounds it anywhere in the cell. A cell where every candidate fails (or passes) for every radius of this pass is
//     marked so; the others ("maybe": a band along each outline, path and gate) still run the full checks;
//   * per cell the nearest tree and the nearest bush (to the cell centre): when that plant is too close to the candidate,
//     TooClose would have found it too (a real conflict), so the candidate fails without the full spacing search. When it is
//     not, the full search runs as before.
// Same candidates, same order, same answers: the plans are exactly the ones the full checks make.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    internal sealed class PlantGrid
    {
        public const byte Fail = 0, Pass = 1, Maybe = 2;
        /// <summary>Plants are registered in the cells within this distance (+ the half diagonal): the spacing tests reach that far.</summary>
        private const float TreeReach = 8f, BushReach = 6f;

        private readonly float2 _min;
        private readonly float _h, _inv, _delta;
        private readonly int _nx, _ny;
        private readonly byte[] _state;
        private float[] _g0, _g1;
        private readonly int[] _nearTree, _nearBush;
        private readonly List<float2> _trees = new List<float2>(), _bushes = new List<float2>();
        private readonly List<float> _cross = new List<float>();

        public PlantGrid(LayerRegion region)
        {
            var size = math.max(region.Max - region.Min, new float2(1f));
            // ~1 m cells on a 12 ha park, coarser on very big ones (the arrays stay below ~400 000 cells).
            _h = math.clamp(math.sqrt(size.x * size.y / 400000f), 1f, 6f);
            _inv = 1f / _h;
            _delta = _h * 0.70711f + 0.002f;
            _min = region.Min - _h;
            _nx = (int)math.ceil(size.x * _inv) + 2;
            _ny = (int)math.ceil(size.y * _inv) + 2;
            int n = _nx * _ny;
            _state = new byte[n];
            _nearTree = new int[n];
            _nearBush = new int[n];
        }

        /// <summary>The cell of p, −1 outside the grid.</summary>
        private int Index(float2 p)
        {
            float fx = (p.x - _min.x) * _inv, fy = (p.y - _min.y) * _inv;
            if (!(fx >= 0f && fy >= 0f && fx < _nx && fy < _ny)) return -1;
            return (int)fy * _nx + (int)fx;
        }

        private float2 Centre(int x, int y) => _min + (new float2(x, y) + 0.5f) * _h;

        /// <summary>Fail, Pass or Maybe for the fixed checks of a candidate at p.</summary>
        public byte Static(float2 p)
        {
            int i = Index(p);
            return i < 0 ? Maybe : _state[i];
        }

        /// <summary>
        /// True when a tree or bush already planted is too close for this candidate (exactly a conflict TooClose finds).
        /// False says nothing: the full spacing search decides.
        /// </summary>
        public bool Crowded(float2 p, bool tree, float spacing)
        {
            int i = Index(p);
            if (i < 0) return false;
            int t = _nearTree[i];
            if (t >= 0)
            {
                float d = tree ? math.max(spacing, 6f) : 6f;
                if (math.distancesq(p, _trees[t]) < d * d) return true;
            }
            int b = _nearBush[i];
            if (b >= 0)
            {
                float d = tree ? 6f : math.max(spacing, 3.5f);
                if (math.distancesq(p, _bushes[b]) < d * d) return true;
            }
            return false;
        }

        /// <summary>Registers a planted tree or bush.</summary>
        public void Added(float2 p, bool tree)
        {
            var list = tree ? _trees : _bushes;
            var near = tree ? _nearTree : _nearBush;
            int id = list.Count;
            list.Add(p);
            float reach = (tree ? TreeReach : BushReach) + _delta;
            Cells(p, p, reach, out int x0, out int y0, out int x1, out int y1);
            float r2 = reach * reach;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var c = Centre(x, y);
                    float d2 = math.distancesq(c, p);
                    if (d2 > r2) continue;
                    int k = y * _nx + x;
                    int e = near[k];
                    if (e < 0 || d2 < math.distancesq(c, list[e])) near[k] = id;
                }
        }

        /// <summary>
        /// Prepares one planting pass: the fixed checks of PlantFits for radii in [rMin, rMax] and the boundary clearance bc,
        /// and the trees and bushes already in the site's occupancy.
        /// </summary>
        public void Prepare(ParkSite s, float bc, float rMin, float rMax)
        {
            int n = _nx * _ny;
            if (_g0 == null || _g0.Length < n) { _g0 = new float[n]; _g1 = new float[n]; }
            var g0 = _g0; var g1 = _g1;
            var region = s.LayerRegion;

            // g0: signed distance to the outline (inside > 0) − bc, and the gates' distance − 10 (GateDistanceSq < 100).
            float capB = bc + _delta + 0.01f;
            for (int i = 0; i < n; i++) g0[i] = -capB;
            InsideRows(region.Ring, 0, _ny - 1, (k) => g0[k] = capB);
            var ring = region.Ring;
            for (int e = 0; e < ring.Length; e++)
            {
                var a = ring[e]; var b = ring[(e + 1) % ring.Length];
                Cells(a, b, capB, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int k = y * _nx + x;
                        float d = math.sqrt(SegmentIndex.DistSq(Centre(x, y), a, b));
                        float v = g0[k];
                        if (d < math.abs(v)) g0[k] = v < 0f ? -d : d;
                    }
            }
            for (int i = 0; i < n; i++) g0[i] -= bc;
            float capG = 10f + _delta + 0.01f;
            foreach (var g in s.Gates)
            {
                Cells(g.Position, g.Position, capG, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int k = y * _nx + x;
                        g0[k] = math.min(g0[k], math.distance(Centre(x, y), g.Position) - 10f);
                    }
            }

            // g1: the distances tested against the plant's radius: lake (signed, inside < 0), clearing, paths (+0.35 gap),
            // reserved polygons and site obstacles (0 inside).
            float cap1 = rMax + _delta + 0.01f;
            for (int i = 0; i < n; i++) g1[i] = cap1;
            if (s.Lake.Length >= 3) StampRing(s.Lake, cap1, true);
            if (s.ClearingRadius > 0f)
            {
                float reach = s.ClearingRadius + cap1;
                Cells(s.ClearingCenter, s.ClearingCenter, reach, out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int k = y * _nx + x;
                        g1[k] = math.min(g1[k], math.distance(Centre(x, y), s.ClearingCenter) - s.ClearingRadius);
                    }
            }
            var segs = s.Occ.Corridors.Segments;
            for (int i = 0; i < segs.Count; i++)
            {
                var sg = segs[i];
                float reach = sg.HalfWidth + 0.35f + cap1;
                Cells(sg.A, sg.B, reach, out int x0, out int y0, out int x1, out int y1);
                float r2 = reach * reach;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d2 = SegmentIndex.DistSq(Centre(x, y), sg.A, sg.B);
                        if (d2 > r2) continue;
                        int k = y * _nx + x;
                        g1[k] = math.min(g1[k], math.sqrt(d2) - sg.HalfWidth - 0.35f);
                    }
            }
            foreach (var poly in s.Occ.Polygons) if (poly != null && poly.Length >= 3) StampRing(poly, cap1, false);

            for (int i = 0; i < n; i++)
            {
                float a = g0[i], b = g1[i];
                _state[i] = a + _delta < 0f || b + _delta < rMin ? Fail : a - _delta >= 0f && b - _delta >= rMax ? Pass : Maybe;
            }

            // The plants already there (trees of the first pass, rows, avenues).
            _trees.Clear(); _bushes.Clear();
            for (int i = 0; i < n; i++) { _nearTree[i] = -1; _nearBush[i] = -1; }
            foreach (var d in s.Occ.Discs.Items)
                if (d.Kind == (int)ParkItemKind.Tree) Added(d.P, true);
                else if (d.Kind == (int)ParkItemKind.Bush) Added(d.P, false);
        }

        /// <summary>
        /// g1 = min(g1, distance to the ring's area): signed (inside &lt; 0, the lake: InLake) or 0 inside (a reserved polygon:
        /// Occupancy.PolygonDistance). Exact within <paramref name="cap"/> of the outline, ±cap beyond it.
        /// </summary>
        private void StampRing(float2[] ring, float cap, bool signed)
        {
            Poly(ring, out var lo, out var hi);
            Cells(lo, hi, cap, out int x0, out int y0, out int x1, out int y1);
            int w = x1 - x0 + 1, hgt = y1 - y0 + 1;
            if (w <= 0 || hgt <= 0) return;
            var f = new float[w * hgt];
            for (int i = 0; i < f.Length; i++) f[i] = cap;
            InsideRows(ring, y0, y1, k => { int x = k % _nx - x0, y = k / _nx - y0; if (x >= 0 && x < w) f[y * w + x] = signed ? -cap : 0f; });
            for (int e = 0; e < ring.Length; e++)
            {
                var a = ring[e]; var b = ring[(e + 1) % ring.Length];
                Cells(a, b, cap, out int ex0, out int ey0, out int ex1, out int ey1);
                for (int y = ey0; y <= ey1; y++)
                    for (int x = ex0; x <= ex1; x++)
                    {
                        int k = (y - y0) * w + (x - x0);
                        float v = f[k];
                        if (!signed && v == 0f) continue;
                        float d = math.sqrt(SegmentIndex.DistSq(Centre(x, y), a, b));
                        if (d < math.abs(v)) f[k] = v < 0f ? -d : d;
                    }
            }
            var g1 = _g1;
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = (y + y0) * _nx + x + x0;
                    g1[k] = math.min(g1[k], f[y * w + x]);
                }
        }

        /// <summary>Calls <paramref name="inside"/> with the index of every cell in rows y0..y1 whose centre is inside the ring (even-odd).</summary>
        private void InsideRows(float2[] ring, int y0, int y1, Action<int> inside)
        {
            for (int y = y0; y <= y1; y++)
            {
                float py = _min.y + (y + 0.5f) * _h;
                _cross.Clear();
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    var a = ring[i]; var b = ring[j];
                    if ((a.y > py) != (b.y > py)) _cross.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                if (_cross.Count < 2) continue;
                _cross.Sort();
                for (int c = 0; c + 1 < _cross.Count; c += 2)
                {
                    // Centres x with cross[c] < x < cross[c+1].
                    int from = math.max(0, (int)math.ceil((_cross[c] - _min.x) * _inv - 0.5f));
                    int to = math.min(_nx - 1, (int)math.floor((_cross[c + 1] - _min.x) * _inv - 0.5f));
                    for (int x = from; x <= to; x++) inside(y * _nx + x);
                }
            }
        }

        private static void Poly(float2[] ring, out float2 lo, out float2 hi)
        {
            lo = new float2(float.MaxValue); hi = new float2(float.MinValue);
            foreach (var p in ring) { lo = math.min(lo, p); hi = math.max(hi, p); }
        }

        /// <summary>The cells whose centre may lie within <paramref name="reach"/> of the box a..b, clamped to the grid.</summary>
        private void Cells(float2 a, float2 b, float reach, out int x0, out int y0, out int x1, out int y1)
        {
            var lo = (math.min(a, b) - reach - _min) * _inv - 0.5f;
            var hi = (math.max(a, b) + reach - _min) * _inv - 0.5f;
            x0 = math.max(0, (int)math.floor(lo.x)); y0 = math.max(0, (int)math.floor(lo.y));
            x1 = math.min(_nx - 1, (int)math.ceil(hi.x)); y1 = math.min(_ny - 1, (int)math.ceil(hi.y));
        }
    }
}
