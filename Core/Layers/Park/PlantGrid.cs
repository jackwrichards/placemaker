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
// Plain float arithmetic in the loops (no float2 temporaries): the game's Mono runs vector operators as calls.

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

        private readonly float _minX, _minY, _h, _inv, _delta;
        private readonly int _nx, _ny;
        private readonly byte[] _state;
        private float[] _g0, _g1;
        private readonly int[] _nearTree, _nearBush;
        private float[] _tx = new float[256], _ty = new float[256], _bx = new float[256], _by = new float[256];
        private int _trees, _bushes;
        private readonly List<float> _cross = new List<float>();

        public PlantGrid(LayerRegion region)
        {
            var size = math.max(region.Max - region.Min, new float2(1f));
            // ~1 m cells on a 12 ha park, coarser on very big ones (the arrays stay below ~400 000 cells).
            _h = math.clamp(math.sqrt(size.x * size.y / 400000f), 1f, 6f);
            _inv = 1f / _h;
            _delta = _h * 0.70711f + 0.002f;
            _minX = region.Min.x - _h; _minY = region.Min.y - _h;
            _nx = (int)math.ceil(size.x * _inv) + 2;
            _ny = (int)math.ceil(size.y * _inv) + 2;
            int n = _nx * _ny;
            _state = new byte[n];
            _nearTree = new int[n];
            _nearBush = new int[n];
        }

        /// <summary>The cell of p, −1 outside the grid.</summary>
        public int Cell(float2 p)
        {
            float fx = (p.x - _minX) * _inv, fy = (p.y - _minY) * _inv;
            if (!(fx >= 0f && fy >= 0f && fx < _nx && fy < _ny)) return -1;
            return (int)fy * _nx + (int)fx;
        }

        /// <summary>Fail, Pass or Maybe for the fixed checks of a candidate in cell i (Cell; −1: Maybe).</summary>
        public byte Static(int i) => i < 0 ? Maybe : _state[i];

        /// <summary>
        /// True when a tree or bush already planted is too close for this candidate at p in cell i (exactly a conflict TooClose
        /// finds). False says nothing: the full spacing search decides.
        /// </summary>
        public bool Crowded(int i, float2 p, bool tree, float spacing)
        {
            if (i < 0) return false;
            int t = _nearTree[i];
            if (t >= 0)
            {
                float d = tree ? math.max(spacing, 6f) : 6f;
                float dx = p.x - _tx[t], dy = p.y - _ty[t];
                if (dx * dx + dy * dy < d * d) return true;
            }
            int b = _nearBush[i];
            if (b >= 0)
            {
                float d = tree ? 6f : math.max(spacing, 3.5f);
                float dx = p.x - _bx[b], dy = p.y - _by[b];
                if (dx * dx + dy * dy < d * d) return true;
            }
            return false;
        }

        /// <summary>Registers a planted tree or bush.</summary>
        public void Added(float2 p, bool tree)
        {
            int id;
            float[] xs, ys; int[] near;
            if (tree)
            {
                if (_trees == _tx.Length) { Array.Resize(ref _tx, _trees * 2); Array.Resize(ref _ty, _trees * 2); }
                id = _trees++; xs = _tx; ys = _ty; near = _nearTree;
            }
            else
            {
                if (_bushes == _bx.Length) { Array.Resize(ref _bx, _bushes * 2); Array.Resize(ref _by, _bushes * 2); }
                id = _bushes++; xs = _bx; ys = _by; near = _nearBush;
            }
            float px = p.x, py = p.y;
            xs[id] = px; ys[id] = py;
            float reach = (tree ? TreeReach : BushReach) + _delta, r2 = reach * reach;
            int y0 = math.max(0, (int)math.floor((py - reach - _minY) * _inv - 0.5f));
            int y1 = math.min(_ny - 1, (int)math.ceil((py + reach - _minY) * _inv - 0.5f));
            for (int y = y0; y <= y1; y++)
            {
                float cy = _minY + (y + 0.5f) * _h, dy = cy - py, rest = r2 - dy * dy;
                if (rest < 0f) continue;
                float half = math.sqrt(rest);
                int x0 = math.max(0, (int)math.ceil((px - half - _minX) * _inv - 0.5f));
                int x1 = math.min(_nx - 1, (int)math.floor((px + half - _minX) * _inv - 0.5f));
                int row = y * _nx;
                for (int x = x0; x <= x1; x++)
                {
                    float cx = _minX + (x + 0.5f) * _h, dx = cx - px;
                    int k = row + x, e = near[k];
                    if (e >= 0)
                    {
                        float ex = cx - xs[e], ey = cy - ys[e];
                        if (ex * ex + ey * ey <= dx * dx + dy * dy) continue;
                    }
                    near[k] = id;
                }
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

            // g0: signed distance to the outline (inside > 0), the gates' distance − 10 + bc (GateDistanceSq < 100); bc is taken
            // off when the cells are classified.
            // g1: the distances tested against the plant's radius: lake (signed, inside < 0), clearing, paths (+0.35 gap),
            // reserved polygons and site obstacles (0 inside).
            float capB = bc + _delta + 0.01f, cap1 = rMax + _delta + 0.01f;
            for (int i = 0; i < n; i++) { g0[i] = -capB; g1[i] = cap1; }
            InsideRows(region.Ring, 0, _ny - 1, 0, _nx - 1, g0, 0, _nx, capB);
            var ring = region.Ring;
            for (int e = 0; e < ring.Length; e++)
                StampSegment(g0, ring[e], ring[(e + 1) % ring.Length], 0f, capB, true);
            float capG = 10f + _delta + 0.01f;
            foreach (var g in s.Gates) StampDisc(g0, g.Position, 10f - bc, capG);

            if (s.Lake.Length >= 3) StampRing(s.Lake, cap1, true);
            if (s.ClearingRadius > 0f) StampDisc(g1, s.ClearingCenter, s.ClearingRadius, s.ClearingRadius + cap1);
            var segs = s.Occ.Corridors.Segments;
            for (int i = 0; i < segs.Count; i++)
            {
                var sg = segs[i];
                StampSegment(g1, sg.A, sg.B, sg.HalfWidth + 0.35f, sg.HalfWidth + 0.35f + cap1, false);
            }
            foreach (var poly in s.Occ.Polygons) if (poly != null && poly.Length >= 3) StampRing(poly, cap1, false);

            for (int i = 0; i < n; i++)
            {
                float a = g0[i] - bc, b = g1[i];
                _state[i] = a + _delta < 0f || b + _delta < rMin ? Fail : a - _delta >= 0f && b - _delta >= rMax ? Pass : Maybe;
                _nearTree[i] = -1; _nearBush[i] = -1;
            }

            // The plants already there (trees of the first pass, rows, avenues).
            _trees = 0; _bushes = 0;
            foreach (var d in s.Occ.Discs.Items)
                if (d.Kind == (int)ParkItemKind.Tree) Added(d.P, true);
                else if (d.Kind == (int)ParkItemKind.Bush) Added(d.P, false);
        }

        /// <summary>
        /// f = min(f, distance to segment a→b − <paramref name="sub"/>) for the cells within <paramref name="reach"/> of it; with
        /// <paramref name="signed"/>, |f| instead, keeping f's sign (the outline: inside &gt; 0).
        /// </summary>
        private void StampSegment(float[] f, float2 a, float2 b, float sub, float reach, bool signed)
        {
            float ax = a.x, ay = a.y, abx = b.x - a.x, aby = b.y - a.y, len = abx * abx + aby * aby, r2 = reach * reach;
            int x0 = math.max(0, (int)math.floor((math.min(ax, b.x) - reach - _minX) * _inv - 0.5f));
            int x1 = math.min(_nx - 1, (int)math.ceil((math.max(ax, b.x) + reach - _minX) * _inv - 0.5f));
            int y0 = math.max(0, (int)math.floor((math.min(ay, b.y) - reach - _minY) * _inv - 0.5f));
            int y1 = math.min(_ny - 1, (int)math.ceil((math.max(ay, b.y) + reach - _minY) * _inv - 0.5f));
            for (int y = y0; y <= y1; y++)
            {
                float cy = _minY + (y + 0.5f) * _h;
                int row = y * _nx;
                for (int x = x0; x <= x1; x++)
                {
                    float cx = _minX + (x + 0.5f) * _h;
                    float t = len > 1e-9f ? math.saturate(((cx - ax) * abx + (cy - ay) * aby) / len) : 0f;
                    float qx = cx - (ax + abx * t), qy = cy - (ay + aby * t), d2 = qx * qx + qy * qy;
                    if (d2 > r2) continue;
                    float d = math.sqrt(d2) - sub;
                    int k = row + x;
                    float v = f[k];
                    if (signed) { if (d < math.abs(v)) f[k] = v < 0f ? -d : d; }
                    else if (d < v) f[k] = d;
                }
            }
        }

        /// <summary>f = min(f, distance to c − <paramref name="sub"/>) for the cells within <paramref name="reach"/> of c.</summary>
        private void StampDisc(float[] f, float2 c, float sub, float reach)
        {
            float r2 = reach * reach;
            int x0 = math.max(0, (int)math.floor((c.x - reach - _minX) * _inv - 0.5f));
            int x1 = math.min(_nx - 1, (int)math.ceil((c.x + reach - _minX) * _inv - 0.5f));
            int y0 = math.max(0, (int)math.floor((c.y - reach - _minY) * _inv - 0.5f));
            int y1 = math.min(_ny - 1, (int)math.ceil((c.y + reach - _minY) * _inv - 0.5f));
            for (int y = y0; y <= y1; y++)
            {
                float dy = _minY + (y + 0.5f) * _h - c.y;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = _minX + (x + 0.5f) * _h - c.x, d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;
                    int k = y * _nx + x;
                    f[k] = math.min(f[k], math.sqrt(d2) - sub);
                }
            }
        }

        /// <summary>
        /// g1 = min(g1, distance to the ring's area): signed (inside &lt; 0, the lake: InLake) or 0 inside (a reserved polygon:
        /// Occupancy.PolygonDistance). Exact within <paramref name="cap"/> of the outline, ±cap beyond it.
        /// </summary>
        private void StampRing(float2[] ring, float cap, bool signed)
        {
            float lx = float.MaxValue, ly = float.MaxValue, hx = float.MinValue, hy = float.MinValue;
            foreach (var p in ring) { lx = math.min(lx, p.x); ly = math.min(ly, p.y); hx = math.max(hx, p.x); hy = math.max(hy, p.y); }
            int x0 = math.max(0, (int)math.floor((lx - cap - _minX) * _inv - 0.5f));
            int x1 = math.min(_nx - 1, (int)math.ceil((hx + cap - _minX) * _inv - 0.5f));
            int y0 = math.max(0, (int)math.floor((ly - cap - _minY) * _inv - 0.5f));
            int y1 = math.min(_ny - 1, (int)math.ceil((hy + cap - _minY) * _inv - 0.5f));
            int w = x1 - x0 + 1, hgt = y1 - y0 + 1;
            if (w <= 0 || hgt <= 0) return;
            // The ring's own field over its box (f's index = (y − y0)·w + x − x0), then folded into g1.
            var f = new float[w * hgt];
            for (int i = 0; i < f.Length; i++) f[i] = cap;
            InsideRows(ring, y0, y1, x0, x1, f, -(y0 * w + x0), w, signed ? -cap : 0f);
            float saveMinX = _minX;
            for (int e = 0; e < ring.Length; e++)
            {
                var a = ring[e]; var b = ring[(e + 1) % ring.Length];
                float ax = a.x, ay = a.y, abx = b.x - a.x, aby = b.y - a.y, len = abx * abx + aby * aby, r2 = cap * cap;
                int ex0 = math.max(x0, (int)math.floor((math.min(ax, b.x) - cap - _minX) * _inv - 0.5f));
                int ex1 = math.min(x1, (int)math.ceil((math.max(ax, b.x) + cap - _minX) * _inv - 0.5f));
                int ey0 = math.max(y0, (int)math.floor((math.min(ay, b.y) - cap - _minY) * _inv - 0.5f));
                int ey1 = math.min(y1, (int)math.ceil((math.max(ay, b.y) + cap - _minY) * _inv - 0.5f));
                for (int y = ey0; y <= ey1; y++)
                {
                    float cy = _minY + (y + 0.5f) * _h;
                    for (int x = ex0; x <= ex1; x++)
                    {
                        int k = (y - y0) * w + (x - x0);
                        float v = f[k];
                        if (!signed && v == 0f) continue;
                        float cx = saveMinX + (x + 0.5f) * _h;
                        float t = len > 1e-9f ? math.saturate(((cx - ax) * abx + (cy - ay) * aby) / len) : 0f;
                        float qx = cx - (ax + abx * t), qy = cy - (ay + aby * t), d2 = qx * qx + qy * qy;
                        if (d2 > r2) continue;
                        float d = math.sqrt(d2);
                        if (d < math.abs(v)) f[k] = v < 0f ? -d : d;
                    }
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

        /// <summary>
        /// Sets f[y·stride + x + offset] = <paramref name="value"/> for every cell of rows y0..y1, columns x0..x1, whose centre is
        /// inside the ring (even-odd).
        /// </summary>
        private void InsideRows(float2[] ring, int y0, int y1, int x0, int x1, float[] f, int offset, int stride, float value)
        {
            for (int y = y0; y <= y1; y++)
            {
                float py = _minY + (y + 0.5f) * _h;
                _cross.Clear();
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    float ay = ring[i].y, by = ring[j].y;
                    if ((ay > py) != (by > py)) _cross.Add(ring[i].x + (py - ay) * (ring[j].x - ring[i].x) / (by - ay));
                }
                if (_cross.Count < 2) continue;
                _cross.Sort();
                for (int c = 0; c + 1 < _cross.Count; c += 2)
                {
                    // Centres x with cross[c] < x < cross[c+1].
                    int from = math.max(x0, (int)math.ceil((_cross[c] - _minX) * _inv - 0.5f));
                    int to = math.min(x1, (int)math.floor((_cross[c + 1] - _minX) * _inv - 0.5f));
                    for (int x = from; x <= to; x++) f[y * stride + x + offset] = value;
                }
            }
        }
    }
}
