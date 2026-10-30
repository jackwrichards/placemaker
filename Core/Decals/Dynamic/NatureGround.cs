// Placemaker — dynamic decals: the designed ground of the nature types (wetland, lookout hill, nature reserve, hiking trail,
// greenway, campground). GPL-3.0. docs/runbooks/nature.md.
//
// The sports park's design (Weathering.Scene) reads a lawn with pitches and paths; wild ground has other causes. This pass reads
// the plan AND the built ground (the heightmap after the type's own terraform) and lays the shared library where each cause
// would really leave its mark:
//   land       the built ground on a grid: slope, the downhill direction, how much ground drains through each cell, and
//              how deep each hollow would hold water
//   water      runoff rills down the real flow lines (dry stream beds where much ground drains), gravel fans where they
//              spread out on the flat, marsh mud in the real hollows, muddy banks, sedge litter and prints along ponds and
//              lakes (the type's own ponds and the water already there)
//   rock       scree at the foot of steep ground and downhill of boulders, grit and moss round rocks and fallen logs
//   trees      each tree's own leaves (summer set), needles under conifers, woodland floor and moss under groups, twigs
//              and bark under dead trees
//   feet       worn trail edges, roots bared where a trail passes big trees, puddles and mud in the trail's dips, trodden
//              corners at junctions and gates, animal trails from the woods to water
//   ground     the open ground's own texture: heather on a heath, dry grass on knolls and sunny slopes, damp in the lows
//   camp       tent pitches trampled, ash in the fire rings, feet worn bare round them, a path trodden to each pitch
// No pattern: every piece is placed where its cause is. Deterministic from the step's seed; thinned evenly on huge sites
// (Weathering.Apply's budget loop).

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public static class NatureGround
    {
        private static readonly HashSet<string> Types = new HashSet<string>(StringComparer.Ordinal)
        { "wetland", "lookoutHill", "natureReserve", "hikingTrail", "greenwayPark", "campground" };

        /// <summary>The nature types (all their styles) get this designed ground.</summary>
        public static bool Wants(string layerId) => layerId != null && Types.Contains(layerId);

        /// <summary>The tag wetland and other water types give their ponds' regions.</summary>
        public const string WaterTag = "place:water";
        /// <summary>The tag of a fire ring's spot (CampgroundLayer.FireTag).</summary>
        public const string FireTag = "place:fire";

        internal sealed class Design
        {
            private readonly List<AreaPlan> _layers;
            private readonly float2[] _ring;
            private readonly SiteContext _site;
            private readonly Func<float2, float> _ground;
            private readonly uint _seed;
            private readonly float _amount;
            private readonly string _type;
            private readonly BrushContext _b;
            private readonly Weathering.Report _r = new Weathering.Report();
            private List<DecalInstance> Out => _b.Out;

            private sealed class Line { public float2[] P; public float Half; public bool Deck; }
            private struct Tree { public float2 P; public float Crown; public string Leaves; public bool Dead; public bool Conifer; }

            private readonly List<Line> _paths = new List<Line>();
            private readonly List<Tree> _trees = new List<Tree>();
            private readonly List<float2> _bushes = new List<float2>();
            private readonly List<(float2 p, float r)> _rocks = new List<(float2, float)>();
            private readonly List<(float2 p, float yaw)> _logs = new List<(float2, float)>();
            private readonly List<(float2 p, float yaw)> _tents = new List<(float2, float)>(), _fires = new List<(float2, float)>(), _tables = new List<(float2, float)>(), _benches = new List<(float2, float)>();
            private readonly List<float2[]> _water = new List<float2[]>(), _hard = new List<float2[]>(), _pads = new List<float2[]>();
            private readonly List<float2> _gates = new List<float2>();
            private PointGrid _treeGrid, _pathGrid;
            private readonly List<(float2 a, float2 b, float half)> _pathSegs = new List<(float2, float2, float)>();
            private BrushArea _wild;

            // the land grid
            private float _cell;
            private float2 _lo;
            private int _nx, _nz;
            private float[] _h, _slope, _acc, _fill;
            private float2[] _down;
            private int[] _recv;
            private bool[] _in, _wet;

            public Design(List<AreaPlan> layers, float2[] ring, SiteContext site, Func<float2, float> ground, uint seed, float amount, string type,
                DecalLibrary lib, List<DecalInstance> into, float density, int cap)
            {
                _layers = layers; _ring = Poly.EnsureCcw((float2[])ring.Clone()); _site = site; _ground = ground ?? (p => 0f); _seed = seed; _amount = amount; _type = type ?? "";
                _b = new BrushContext(lib, seed, into) { Budget = (into?.Count ?? 0) + (int)(cap * 1.6f) + 200, Density = density };
            }

            private uint H(int a, int b = 0) => Hashing.Mix(_seed, a, b);
            private float U(int a, int b = 0) => Hashing.Unit(H(a, b));
            private float A => math.min(2.2f, _amount);
            private string Leaf(string kind) => kind != null && _b.Lib.Has(kind + Weathering.Season) ? kind + Weathering.Season : kind;
            private float2 Op(float lo, float hi) => new float2(math.saturate(lo * math.min(1.4f, 0.55f + 0.45f * _amount)), math.saturate(hi * math.min(1.3f, 0.6f + 0.4f * _amount)));

            public Weathering.Report Run()
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                int before = Out.Count;
                var lap = System.Diagnostics.Stopwatch.StartNew();
                void Lap(string what) { _r.Timing[what] = lap.Elapsed.TotalMilliseconds; lap.Restart(); }
                Read(); Lap("read");
                Land(); Lap("land");
                Ground_(); Lap("ground");
                if (_amount > 0f)
                {
                    Water(); Lap("water");
                    Rock(); Lap("rock");
                    Trees(); Lap("trees");
                    Feet(); Lap("feet");
                    Camp(); Lap("camp");
                }
                foreach (var kv in _b.ByCause) _r.ByCause[kv.Key] = kv.Value;
                _r.Instances = Out.Count - before;
                _r.Ms = clock.Elapsed.TotalMilliseconds;
                return _r;
            }

            // ───────────── read the plan ─────────────

            private static float2[] Sample(PlannedNet n, float step)
            {
                float2 a = n.A.xz, b = n.B.xz, c = n.C.xz, d = n.D.xz;
                float len = math.distance(a, b) + math.distance(b, c) + math.distance(c, d);
                int k = math.clamp((int)math.ceil(len / step), 1, 400);
                var pts = new float2[k + 1];
                for (int i = 0; i <= k; i++)
                {
                    float t = i / (float)k, u = 1f - t;
                    pts[i] = u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
                }
                return pts;
            }

            private static string LeavesOf(PlannedObject o)
            {
                string id = ((o.Asset.Id ?? "") + " " + (o.Asset.Fallbacks != null && o.Asset.Fallbacks.Length > 0 ? o.Asset.Fallbacks[0] : "") + " " + (o.Category ?? "")).ToLowerInvariant();
                if (DressingWorld.IsConifer(o)) return "pineNeedles";
                // (nature) birch, alder and poplar drop the mixed brown-olive set: the birch set read as a yellow carpet in summer
                if (id.Contains("birch") || id.Contains("alder") || id.Contains("poplar")) return "leafMixed";
                if (id.Contains("oak")) return "leafOak";
                if (id.Contains("plane") || id.Contains("linden")) return "leafPlane";
                if (id.Contains("chestnut") || id.Contains("hickory") || id.Contains("maple")) return "leafMaple";
                if (id.Contains("apple") || id.Contains("cherry") || id.Contains("blossom")) return "blossomPetals";
                if (id.Contains("palm")) return null;
                return "leafMixed";
            }

            private void Read()
            {
                foreach (var l in _layers)
                {
                    foreach (var n in l.Nets)
                    {
                        if (n == null || (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide)) continue;
                        var line = new Line { P = Sample(n, 1.5f), Half = SiteHarmony.HalfWidth(n, _site), Deck = !n.FollowTerrain };
                        _paths.Add(line);
                        for (int i = 0; i + 1 < line.P.Length; i++) _pathSegs.Add((line.P[i], line.P[i + 1], line.Half));
                    }
                    foreach (var s in l.Surfaces)
                    {
                        if (s?.Ring == null || s.Ring.Length < 3) continue;
                        var r2 = Poly.EnsureCcw(Poly.To2D(s.Ring));
                        if (s.Role == SurfaceRole.Water) _water.Add(r2);
                        else if (s.Role == SurfaceRole.Ground && l.GeneratorId == "campground") _pads.Add(r2);
                        else _hard.Add(r2);
                    }
                    foreach (var reg in l.Regions)
                    {
                        if (reg?.Tags == null || reg.Ring.Length < 3) continue;
                        if (Array.IndexOf(reg.Tags, WaterTag) >= 0) _water.Add(Poly.EnsureCcw(Poly.To2D(reg.Ring)));
                        else if (Array.IndexOf(reg.Tags, FireTag) >= 0) _fires.Add((Poly.Centroid(Poly.To2D(reg.Ring)), 0f));
                    }
                    foreach (var o in l.Objects)
                    {
                        if (o == null) continue;
                        string id = o.Asset.Id ?? "";
                        string cat = o.Category ?? "";
                        if (o.Role == ObjectRole.Tree)
                            _trees.Add(new Tree { P = o.Position.xz, Crown = DressingWorld.CrownOf(o, _site), Leaves = LeavesOf(o), Dead = o.Age == PlannedAge.Dead, Conifer = DressingWorld.IsConifer(o) });
                        else if (o.Role == ObjectRole.Bush) _bushes.Add(o.Position.xz);
                        else if ((cat == AssetCategories.Rock || id.StartsWith("Boulder", StringComparison.Ordinal) || id.StartsWith("Rock", StringComparison.Ordinal)) && o.Scale >= 0.5f)
                            _rocks.Add((o.Position.xz, (id.StartsWith("Boulder", StringComparison.Ordinal) ? 2.4f : 0.7f) * math.max(0.3f, o.Scale)));
                        else if (id.StartsWith("Log", StringComparison.Ordinal) && id.Length <= 6) { if (o.Scale >= 0.5f) _logs.Add((o.Position.xz, o.Yaw)); }   // short logs are seats by a fire
                        else if (cat == AssetCategories.CampingPitch) _tents.Add((o.Position.xz, o.Yaw));
                        else if (cat == AssetCategories.CampingFire) _fires.Add((o.Position.xz, o.Yaw));
                        else if (cat == AssetCategories.Picnic) _tables.Add((o.Position.xz, o.Yaw));
                        else if (o.Role == ObjectRole.Bench) _benches.Add((o.Position.xz, o.Yaw));
                    }
                }
                foreach (var p in _paths)
                {
                    if (p.Deck) continue;
                    foreach (var e in new[] { p.P[0], p.P[p.P.Length - 1] })
                        if (Poly.DistanceToBoundary(_ring, e) < 4f && !_gates.Exists(g => math.distancesq(g, e) < 36f)) _gates.Add(e);
                }
                _treeGrid = new PointGrid(16f);
                for (int i = 0; i < _trees.Count; i++) _treeGrid.Add(_trees[i].P, i);
                _pathGrid = new PointGrid(8f);
                for (int i = 0; i < _pathSegs.Count; i++) _pathGrid.Add((_pathSegs[i].a + _pathSegs[i].b) * 0.5f, i);
                var holes = new List<float2[]>(_hard);
                _wild = BrushArea.Of(_ring, holes);
                _wild.Keep = p => PathDistance(p) > 0.4f && !WetAt(p);
            }

            /// <summary>Metres from p to the nearest path edge (negative on a path; 99 when none is near).</summary>
            private float PathDistance(float2 p)
            {
                float best = 99f;
                foreach (int i in _pathGrid.Near(p, 1))
                {
                    var s = _pathSegs[i];
                    var ab = s.b - s.a;
                    float t = math.saturate(math.dot(p - s.a, ab) / math.max(1e-4f, math.lengthsq(ab)));
                    best = math.min(best, math.distance(p, s.a + ab * t) - s.half);
                }
                return best;
            }

            /// <summary>Water at p: the land grid first (fast), the exact outlines only on a cell at the water's edge.</summary>
            private bool WetAt(float2 p)
            {
                if (_wet == null) return InWater(p);
                int k = CellOf(p);
                if (k < 0) return false;
                int ci = k % _nx, cj = k / _nx, wet = 0, all = 0;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int x = ci + di, z = cj + dj;
                        if (x < 0 || z < 0 || x >= _nx || z >= _nz) continue;
                        all++; if (_wet[Idx(x, z)]) wet++;
                    }
                if (wet == 0) return false;
                if (wet == all) return true;
                return InWater(p) || (_site?.WaterDepthAt != null && _site.WaterDepthAt(p) > 0.1f);
            }

            /// <summary>A wet cell within <paramref name="cells"/> cells of p.</summary>
            private bool NearWet(float2 p, int cells)
            {
                int k = CellOf(p);
                if (k < 0 || _wet == null) return false;
                int ci = k % _nx, cj = k / _nx;
                for (int dj = -cells; dj <= cells; dj++)
                    for (int di = -cells; di <= cells; di++)
                    {
                        int x = ci + di, z = cj + dj;
                        if (x >= 0 && z >= 0 && x < _nx && z < _nz && _wet[Idx(x, z)]) return true;
                    }
                return false;
            }

            private bool InWater(float2 p, float margin = 0f)
            {
                foreach (var w in _water) if (Poly.Contains(w, p) || (margin > 0f && Poly.DistanceToBoundary(w, p) < margin)) return true;
                return false;
            }

            private float Shade(float2 p)
            {
                float s = 0f;
                foreach (int i in _treeGrid.Near(p, 1))
                {
                    var t = _trees[i];
                    if (t.Dead) continue;
                    float d = math.distance(t.P, p);
                    if (d < t.Crown * 1.6f) s += 1f - d / (t.Crown * 1.6f);
                }
                return math.saturate(s);
            }

            // ───────────── the land: slope, flow, hollows ─────────────

            private int Idx(int i, int j) => j * _nx + i;
            private float2 Pos(int i, int j) => _lo + (new float2(i, j) + 0.5f) * _cell;
            private int CellOf(float2 p)
            {
                int i = (int)math.floor((p.x - _lo.x) / _cell), j = (int)math.floor((p.y - _lo.y) / _cell);
                return i < 0 || j < 0 || i >= _nx || j >= _nz ? -1 : Idx(i, j);
            }

            private void Land()
            {
                Poly.Bounds(_ring, out var lo, out var hi);
                float area = math.max(1f, (hi.x - lo.x) * (hi.y - lo.y));
                // about 6000 cells whatever the size (3 m on a block, 13 m on a square kilometre)
                _cell = math.clamp(math.sqrt(area / 6000f), 2.5f, 16f);
                _lo = lo - _cell;
                _nx = (int)math.ceil((hi.x - lo.x) / _cell) + 2;
                _nz = (int)math.ceil((hi.y - lo.y) / _cell) + 2;
                int n = _nx * _nz;
                _h = new float[n]; _slope = new float[n]; _acc = new float[n]; _fill = new float[n];
                _down = new float2[n]; _recv = new int[n]; _in = new bool[n]; _wet = new bool[n];
                for (int j = 0; j < _nz; j++)
                    for (int i = 0; i < _nx; i++)
                    {
                        var p = Pos(i, j);
                        int k = Idx(i, j);
                        _h[k] = _ground(p);
                        _in[k] = Poly.Contains(_ring, p);
                        _wet[k] = _in[k] && (InWater(p) || (_site?.WaterDepthAt != null && _site.WaterDepthAt(p) > 0.1f));
                    }
                // slope and the steepest way down (8 neighbours)
                for (int j = 0; j < _nz; j++)
                    for (int i = 0; i < _nx; i++)
                    {
                        int k = Idx(i, j);
                        float hx = (_h[Idx(math.min(_nx - 1, i + 1), j)] - _h[Idx(math.max(0, i - 1), j)]) / (_cell * (i > 0 && i < _nx - 1 ? 2f : 1f));
                        float hz = (_h[Idx(i, math.min(_nz - 1, j + 1))] - _h[Idx(i, math.max(0, j - 1))]) / (_cell * (j > 0 && j < _nz - 1 ? 2f : 1f));
                        _slope[k] = math.sqrt(hx * hx + hz * hz);
                        _down[k] = math.normalizesafe(-new float2(hx, hz));
                        int best = -1; float drop = 0f;
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                if (di == 0 && dj == 0) continue;
                                int x = i + di, z = j + dj;
                                if (x < 0 || z < 0 || x >= _nx || z >= _nz) continue;
                                float d = (_h[k] - _h[Idx(x, z)]) / (di != 0 && dj != 0 ? 1.4142f : 1f);
                                if (d > drop + 1e-5f) { drop = d; best = Idx(x, z); }
                            }
                        _recv[k] = best;
                    }
                // ground drained through each cell (m²): highest first, each passes its water to its receiver
                var order = new int[n];
                for (int k = 0; k < n; k++) { order[k] = k; _acc[k] = _in[k] ? _cell * _cell : 0f; }
                var hs = _h;
                Array.Sort(order, (a, b) => hs[b].CompareTo(hs[a]));
                foreach (int k in order) if (_recv[k] >= 0 && _in[k]) _acc[_recv[k]] += _acc[k];
                // hollows: priority flood from the outline inward; how much water each cell would hold
                var done = new bool[n];
                var heap = new MinHeap(n / 4 + 16);
                for (int j = 0; j < _nz; j++)
                    for (int i = 0; i < _nx; i++)
                    {
                        int k = Idx(i, j);
                        if (!_in[k] || _wet[k] || i == 0 || j == 0 || i == _nx - 1 || j == _nz - 1 || !_in[Idx(i - 1, j)] || !_in[Idx(i + 1, j)] || !_in[Idx(i, j - 1)] || !_in[Idx(i, j + 1)])
                        { _fill[k] = _h[k]; done[k] = true; heap.Push(k, _h[k]); }
                    }
                while (heap.Count > 0)
                {
                    int k = heap.Pop(out float level);
                    int i = k % _nx, j = k / _nx;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int x = i + di, z = j + dj;
                            if ((di == 0 && dj == 0) || x < 0 || z < 0 || x >= _nx || z >= _nz) continue;
                            int q = Idx(x, z);
                            if (done[q]) continue;
                            done[q] = true;
                            _fill[q] = math.max(_h[q], level);
                            heap.Push(q, _fill[q]);
                        }
                }
                _r.Timing["cells"] = n;
            }

            private float Depth(int k) => k < 0 ? 0f : _fill[k] - _h[k];

            /// <summary>The land at p: slope (rise over run), the way down, drained area (m²) and hollow depth (m).</summary>
            private void At(float2 p, out float slope, out float2 down, out float acc, out float depth)
            {
                int k = CellOf(p);
                if (k < 0) { slope = 0f; down = float2.zero; acc = 0f; depth = 0f; return; }
                slope = _slope[k]; down = _down[k]; acc = _acc[k]; depth = Depth(k);
            }

            // ───────────── ground: the open land's own texture ─────────────

            private bool Heath => _type == "natureReserve" && _trees.Count < Poly.Area(_ring) / 10000f * 80f;

            private void Ground_()
            {
                // faint mottling over all the open ground: the big variation is the paint's, this is the grain
                Brushes.Scatter(_b, _wild, "groundMottle", 0.14f, new ScatterOptions { Weight = p => 0.3f + 0.7f * ValueNoise.Clumps(p, 34f, H(1)), Opacity = new float2(0.18f, 0.35f), Clump = 2.5f, Spill = 2f, Level = ColourLevel.Quiet, Cause = "ground:mottle" });
                // dry grass where the ground drains and the sun falls: knolls, the south-facing upper slopes, open ground away from shade
                Brushes.Scatter(_b, _wild, "dryGrass", 0.08f, new ScatterOptions { Weight = Dry, Opacity = new float2(0.15f, 0.3f), Clump = 3f, Spill = 2f, Level = ColourLevel.Quiet, Cause = "ground:dry" });
                // damp lows: where water gathers but does not stand
                Brushes.Scatter(_b, _wild, "dampPatch", 0.06f, new ScatterOptions { Weight = p => Damp(p) * (1f - Dry(p)), Opacity = new float2(0.25f, 0.45f), Clump = 2.5f, Loners = 0.2f, Cause = "ground:damp" });
                if (_amount > 0f) Terracettes();
                if (Heath)
                {
                    // heather over the open heath, thinning under the few trees and along the trails
                    Brushes.Scatter(_b, _wild, "heathPatch", 0.75f, new ScatterOptions { Weight = p => (0.35f + 0.65f * ValueNoise.Clumps(p, 22f, H(3))) * (1f - Shade(p) * 0.8f) * math.saturate(PathDistance(p) / 2.5f), Opacity = new float2(0.7f, 1f), Clump = 4f, Spread = 0.6f, Loners = 0.2f, Spill = 1f, Cause = "ground:heath" });
                }
            }

            /// <summary>
            /// Terracettes: on open grassy slopes, the narrow level tracks that grazing feet tread along the contours, in bands of
            /// three to five a step apart (any grass hill steeper than about 1 in 7). Read from the built ground's slope.
            /// </summary>
            private void Terracettes()
            {
                int n = _nx * _nz, bands = 0, maxBands = math.clamp((int)(Poly.Area(_ring) / 4500f), 1, 18);
                for (int k0 = 0; k0 < n && bands < maxBands && !_b.Full; k0++)
                {
                    int k = (int)((k0 * 7919L + 13) % n);
                    if (!_in[k] || _wet[k] || _slope[k] < 0.19f || _slope[k] > 0.7f || U(60, k) > 0.12f) continue;
                    var seed = Pos(k % _nx, k / _nx);
                    if (Shade(seed) > 0.15f || PathDistance(seed) < 3f) continue;
                    var down = _down[k];
                    int lines = 3 + (int)(U(61, k) * 3f);
                    float gap = 1.6f + U(62, k) * 1.1f;
                    bool any = false;
                    for (int li = 0; li < lines; li++)
                    {
                        var start = seed + down * (li * gap) + new float2(-down.y, down.x) * (U(63, li + k) - 0.5f) * 4f;
                        var pts = new List<float2>();
                        for (int dir = -1; dir <= 1; dir += 2)
                        {
                            var p = start; var seg = new List<float2>();
                            float len = 8f + U(64, k * 7 + li * 2 + dir) * 22f;
                            for (float s = 0f; s < len; s += 2f)
                            {
                                At(p, out float sl, out var dn, out _, out _);
                                if (sl < 0.12f || !Poly.Contains(_ring, p) || WetAt(p) || PathDistance(p) < 1.5f || Shade(p) > 0.3f) break;
                                seg.Add(p);
                                p += new float2(-dn.y, dn.x) * dir * 2f;
                            }
                            if (dir < 0) { seg.Reverse(); pts.AddRange(seg); }
                            else if (seg.Count > 1) pts.AddRange(seg.GetRange(1, seg.Count - 1));
                        }
                        if (pts.Count < 4) continue;
                        Brushes.EdgeStrip(_b, Smooth(pts, 2), "animalTrail", new StripOptions { Gaps = 0.35f, Run = new float2(5f, 16f), Opacity = Op(0.28f, 0.5f), Width = new float2(0.6f, 0.85f), CornerDeg = 60f, Cause = "ground:terracette" });
                        any = true;
                    }
                    if (any) bands++;
                }
            }

            /// <summary>0..1: high, convex, sunny (south-facing in a north-up city: −z), open ground.</summary>
            private float Dry(float2 p)
            {
                At(p, out float slope, out var down, out float acc, out float depth);
                float south = math.saturate(-down.y) * math.saturate(slope * 6f);
                float drain = 1f - math.saturate(math.log(math.max(1f, acc / (_cell * _cell))) / 3f);
                float noise = ValueNoise.Clumps(p, 30f, H(2));
                return math.saturate((0.15f + 0.35f * south + 0.3f * drain + 0.45f * noise - 0.25f - depth * 3f) * (1f - Shade(p)));
            }

            /// <summary>0..1: lows that collect water (drained area, shallow hollows, near the water's edge).</summary>
            private float Damp(float2 p)
            {
                At(p, out float slope, out _, out float acc, out float depth);
                float gather = math.saturate(math.log(math.max(1f, acc / (_cell * _cell))) / 4f - 0.25f);
                float shore = NearWet(p, 2) ? 0.6f : 0f;
                return math.saturate(gather * (1f - math.saturate(slope * 4f)) + depth * 4f + shore);
            }

            // ───────────── water: rills, beds, fans, marsh, banks ─────────────

            private void Water()
            {
                Rills();
                Marsh();
                Banks();
            }

            private void Rills()
            {
                float cellA = _cell * _cell;
                // a rill starts where enough ground drains on a slope steep enough to cut it
                float startA = math.max(70f, cellA * 7f);
                // rills are the site's signature: never thinned on a big site (half a rill reads as a smudge)
                float keepDensity = _b.Density; _b.Density = 1f;
                int n = _nx * _nz;
                var used = new bool[n];
                var sources = new List<int>();
                for (int k = 0; k < n; k++)
                    if (_in[k] && !_wet[k] && _acc[k] >= startA && _slope[k] > 0.05f && _recv[k] >= 0 && _acc[_recv[k]] > _acc[k]) sources.Add(k);
                // the biggest flows on open ground first (the main channels where they show), then the side rills that join them
                var score = new Dictionary<int, float>();
                foreach (int k0 in sources) score[k0] = _acc[k0] * (1.15f - Shade(Pos(k0 % _nx, k0 / _nx)));
                sources.Sort((a, b) => score[b].CompareTo(score[a]));
                int lines = 0, maxLines = math.clamp((int)(Poly.Area(_ring) / 1500f), 6, 90);
                foreach (int s0 in sources)
                {
                    if (lines >= maxLines || _b.Full) break;
                    if (used[s0]) continue;
                    // walk up to where this flow line starts to carry startA (the head of the rill)
                    var pts = new List<float2>();
                    var accs = new List<float>();
                    int k = s0, steps = 0;
                    bool end = false; float2 endDir = float2.zero;
                    while (k >= 0 && steps++ < 400)
                    {
                        if (!_in[k] || _wet[k]) break;
                        if (used[k]) { pts.Add(Pos(k % _nx, k / _nx)); accs.Add(_acc[k]); break; }   // joins an earlier channel
                        used[k] = true;
                        var p = Pos(k % _nx, k / _nx);
                        pts.Add(p); accs.Add(_acc[k]);
                        if (_slope[k] < 0.018f) { end = true; endDir = _down[k]; break; }   // it spreads out on the flat
                        k = _recv[k];
                    }
                    if (pts.Count < 4) continue;
                    // mark a band round the channel used, so the next rill is not a parallel twin of this one
                    foreach (var p in pts)
                    {
                        int c = CellOf(p); if (c < 0) continue;
                        int ci = c % _nx, cj = c / _nx;
                        for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { int x = ci + di, z = cj + dj; if (x >= 0 && z >= 0 && x < _nx && z < _nz) used[Idx(x, z)] = true; }
                    }
                    var line = Smooth(pts, 2);
                    float big = accs[accs.Count - 1];
                    bool bed = big > startA * 14f && _cell <= 8f;
                    // laid where it shows: off the trails (it runs to a path's edge and on beyond it) and out from under the canopy
                    // (under the crowns nobody sees it; it emerges at the wood's edge)
                    int laidHere = 0;
                    foreach (var piece in Visible(line))
                    {
                        if (piece.Length < 2) continue;
                        float len = 0f; for (int i = 1; i < piece.Length; i++) len += math.distance(piece[i - 1], piece[i]);
                        if (len < 6f) continue;
                        laidHere += Brushes.EdgeStrip(_b, piece, bed ? "dryStreamBed" : "runoffRill", new StripOptions
                        {
                            Gaps = bed ? 0.04f : 0.1f, Run = bed ? new float2(30f, 80f) : new float2(14f, 40f), CornerDeg = 70f,
                            Opacity = Op(bed ? 0.75f : 0.7f, bed ? 0.95f : 0.95f), Width = bed ? new float2(1f, 1.3f) : new float2(1f, 1.4f), Cause = bed ? "water:bed" : "water:rill",
                        });
                    }
                    // where it crosses a trail: the water washed grit across the path and fanned it out on the downhill side
                    for (int i = 1; i < line.Length; i++)
                    {
                        if (PathDistance(line[i - 1]) > 0f && PathDistance(line[i]) <= 0f)
                        {
                            var dir = math.normalizesafe(line[math.min(line.Length - 1, i + 1)] - line[i - 1]);
                            Brushes.At(_b, line[i], dir, "pebbles", 4.5f, Op(0.6f, 0.9f), 1, ColourLevel.Any, "water:washout");
                            int j = i; while (j < line.Length - 1 && PathDistance(line[j]) <= 0.5f) j++;
                            if (j < line.Length && !NearWet(line[j], 1)) Brushes.At(_b, line[j] + dir * 1.5f, dir, "gravelFan", bed ? 8f : 5.5f, Op(0.55f, 0.85f), 1, ColourLevel.Any, "water:washout");
                        }
                    }
                    // the fan where it spreads out, and washed pebbles in it
                    if (end && math.lengthsq(endDir) > 0.1f)
                    {
                        var at = line[line.Length - 1];
                        if (!NearWet(at, 1) && PathDistance(at) > 1f)
                            Brushes.At(_b, at + endDir * 2f, endDir, "gravelFan", bed ? 10f : 6.5f, Op(0.55f, 0.85f), 1, ColourLevel.Any, "water:fan");
                    }
                    lines++;
                }
                SlopeRills(used);
                _b.Density = keepDensity;
            }

            /// <summary>
            /// Rills on open, steep slopes: on a convex hill the water spreads instead of gathering, so the drained-area test finds
            /// no channel, yet rain cuts short parallel rills down any bare steep slope. Seeds on open ground (no canopy, off the
            /// paths), 14 m apart at least, each walked straight down the steepest way while the slope holds.
            /// </summary>
            private void SlopeRills(bool[] used)
            {
                int n = _nx * _nz, laid = 0;
                float open = 0f;
                for (int k = 0; k < n; k++) if (_in[k] && !_wet[k] && _slope[k] > 0.12f) open += _cell * _cell;
                int want = math.clamp((int)(open / 2200f), 0, 24);
                var seeds = new List<float2>();
                for (int k0 = 0; k0 < n && laid < want && !_b.Full; k0++)
                {
                    int k = (int)((k0 * 104729L + 7) % n);
                    if (!_in[k] || _wet[k] || used[k] || _slope[k] < 0.12f) continue;
                    var p0 = Pos(k % _nx, k / _nx);
                    if (Shade(p0) > 0.15f || PathDistance(p0) < 3f) continue;
                    bool far = true;
                    foreach (var s in seeds) if (math.distancesq(s, p0) < 14f * 14f) { far = false; break; }
                    if (!far) continue;
                    var pts = new List<float2> { p0 };
                    var p = p0;
                    for (int step = 0; step < 30; step++)
                    {
                        At(p, out float sl, out var dn, out _, out _);
                        if (sl < 0.06f || math.lengthsq(dn) < 0.1f) break;
                        // a little meander: rills wander round tussocks
                        float wob = (ValueNoise.Clumps(p, 7f, H(66, laid)) - 0.5f) * 0.7f;
                        dn = new float2(dn.x * math.cos(wob) - dn.y * math.sin(wob), dn.x * math.sin(wob) + dn.y * math.cos(wob));
                        p += dn * 1.6f;
                        if (!Poly.Contains(_ring, p) || WetAt(p) || PathDistance(p) < 0.3f || Shade(p) > 0.5f) break;
                        pts.Add(p);
                    }
                    if (pts.Count < 7) continue;
                    seeds.Add(p0);
                    Brushes.EdgeStrip(_b, Smooth(pts, 1), "runoffRill", new StripOptions { Gaps = 0f, Run = new float2(60f, 90f), CornerDeg = 70f, Opacity = Op(0.65f, 0.9f), Width = new float2(0.85f, 1.15f), Cause = "water:rill" });
                    At(p, out float endSlope, out var endDown, out _, out _);
                    if (endSlope < 0.06f && PathDistance(p) > 1f && !NearWet(p, 1) && math.lengthsq(endDown) > 0.1f)
                        Brushes.At(_b, p + endDown, endDown, "gravelFan", 4.5f, Op(0.5f, 0.8f), 0, ColourLevel.Any, "water:fan");
                    laid++;
                }
            }

            /// <summary>The line cut into the pieces that show: clear of every trail by 0.3 m, out of the water and from under the canopy.</summary>
            private List<float2[]> Visible(float2[] line)
            {
                var res = new List<float2[]>();
                var cur = new List<float2>();
                foreach (var p in line)
                {
                    if (PathDistance(p) > 0.3f && !WetAt(p) && Shade(p) < 0.55f) cur.Add(p);
                    else { if (cur.Count >= 2) res.Add(cur.ToArray()); cur.Clear(); }
                }
                if (cur.Count >= 2) res.Add(cur.ToArray());
                return res;
            }

            /// <summary>A polyline smoothed by Chaikin corner cutting (keeps its ends).</summary>
            private static float2[] Smooth(List<float2> pts, int passes)
            {
                var cur = new List<float2>(pts);
                for (int p = 0; p < passes && cur.Count > 2; p++)
                {
                    var nxt = new List<float2> { cur[0] };
                    for (int i = 0; i + 1 < cur.Count; i++)
                    {
                        nxt.Add(math.lerp(cur[i], cur[i + 1], 0.25f));
                        nxt.Add(math.lerp(cur[i], cur[i + 1], 0.75f));
                    }
                    nxt.Add(cur[cur.Count - 1]);
                    cur = nxt;
                }
                return cur.ToArray();
            }

            /// <summary>The line cut into the pieces that keep <paramref name="clear"/> metres off every path.</summary>
            private List<float2[]> OffPaths(float2[] line, float clear)
            {
                var res = new List<float2[]>();
                var cur = new List<float2>();
                foreach (var p in line)
                {
                    if (PathDistance(p) > clear && !WetAt(p)) cur.Add(p);
                    else { if (cur.Count >= 2) res.Add(cur.ToArray()); cur.Clear(); }
                }
                if (cur.Count >= 2) res.Add(cur.ToArray());
                return res;
            }

            private void Marsh()
            {
                // the real hollows of the built ground (cells that would hold water), grouped into basins
                int n = _nx * _nz;
                var seen = new bool[n];
                var basins = new List<List<int>>();
                for (int k = 0; k < n; k++)
                {
                    if (seen[k] || !_in[k] || _wet[k] || Depth(k) < 0.06f) continue;
                    var b = new List<int>();
                    var q = new Queue<int>(); q.Enqueue(k); seen[k] = true;
                    while (q.Count > 0 && b.Count < 4000)
                    {
                        int c = q.Dequeue(); b.Add(c);
                        int ci = c % _nx, cj = c / _nx;
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                int x = ci + di, z = cj + dj;
                                if (x < 0 || z < 0 || x >= _nx || z >= _nz) continue;
                                int t = Idx(x, z);
                                if (!seen[t] && _in[t] && !_wet[t] && Depth(t) >= 0.03f) { seen[t] = true; q.Enqueue(t); }
                            }
                    }
                    basins.Add(b);
                }
                basins.Sort((a, b) => b.Count.CompareTo(a.Count));
                int laid = 0;
                foreach (var b in basins)
                {
                    if (laid >= 40 || _b.Full) break;
                    float deepest = 0f; foreach (int c in b) deepest = math.max(deepest, Depth(c));
                    if (b.Count * _cell * _cell < 12f) continue;
                    // the basin's cells: the deep ones are marsh, the rim damp, sedge litter round the edge
                    var marsh = new BrushArea { Keep = p => { int c = CellOf(p); return c >= 0 && Depth(c) > math.max(0.05f, deepest * 0.3f) && PathDistance(p) > 0.3f; } };
                    var rim = new BrushArea { Keep = p => { int c = CellOf(p); return c >= 0 && Depth(c) > 0.02f && PathDistance(p) > 0.3f; } };
                    float2 blo = new float2(float.MaxValue), bhi = new float2(float.MinValue);
                    foreach (int c in b) { var p = Pos(c % _nx, c / _nx); blo = math.min(blo, p); bhi = math.max(bhi, p); }
                    blo -= _cell; bhi += _cell;
                    marsh.Outer.Add(_ring); rim.Outer.Add(_ring);
                    marsh.ClipLo = rim.ClipLo = blo; marsh.ClipHi = rim.ClipHi = bhi;
                    Brushes.Scatter(_b, marsh, "marshMud", 0.7f, new ScatterOptions { Opacity = Op(0.55f, 0.9f), Clump = 3f, Spread = 0.5f, Loners = 0.15f, Cause = "water:marsh" });
                    Brushes.Scatter(_b, rim, "sedgeLitter", 0.25f, new ScatterOptions { Opacity = Op(0.5f, 0.85f), Clump = 3f, Loners = 0.3f, Cause = "water:sedge" });
                    Brushes.Scatter(_b, rim, "dampPatch", 0.2f, new ScatterOptions { Opacity = Op(0.3f, 0.5f), Clump = 2f, Cause = "water:damp" });
                    laid++;
                }
            }

            private void Banks()
            {
                // the type's own ponds: a muddy margin on the bank, sedge litter, prints where animals come to drink
                foreach (var w in _water)
                {
                    if (Poly.Area(w) < 30f) continue;
                    Brushes.EdgeStrip(_b, w, "shoreMud", new StripOptions { Side = 1, Offset = -0.7f, Closed = true, Gaps = 0.4f, Run = new float2(8f, 26f), Opacity = Op(0.5f, 0.85f), CornerDeg = 60f, Cause = "water:bank" });
                    var band = new BrushArea { Keep = p => { if (Poly.Contains(w, p)) return false; float d = Poly.DistanceToBoundary(w, p); return d < 5f && PathDistance(p) > 0.3f; } };
                    band.Outer.Add(_ring);
                    Poly.Bounds(w, out var lo, out var hi);
                    band.ClipLo = lo - 6f; band.ClipHi = hi + 6f;
                    Brushes.Scatter(_b, band, "sedgeLitter", 0.3f, new ScatterOptions { Opacity = Op(0.5f, 0.85f), Clump = 3f, Loners = 0.35f, Cause = "water:sedge" });
                }
                // water already there (a river or lake inside the outline): its dry rim
                if (_site?.WaterDepthAt != null)
                {
                    bool any = false;
                    for (int k = 0; k < _wet.Length && !any; k++) if (_wet[k]) any = true;
                    if (any)
                    {
                        var rim = new BrushArea { Keep = p => { int c = CellOf(p); if (c < 0 || _wet[c]) return false; int ci = c % _nx, cj = c / _nx; for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { int x = ci + di, z = cj + dj; if (x >= 0 && z >= 0 && x < _nx && z < _nz && _wet[Idx(x, z)]) return PathDistance(p) > 0.3f; } return false; } };
                        rim.Outer.Add(_ring);
                        Brushes.Scatter(_b, rim, "sedgeLitter", 0.35f, new ScatterOptions { Opacity = Op(0.5f, 0.85f), Clump = 3f, Cause = "water:sedge" });
                        Brushes.Scatter(_b, rim, "mudPatch", 0.15f, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 2f, Cause = "water:bank" });
                    }
                }
            }

            // ───────────── rock ─────────────

            private void Rock()
            {
                // scree at the foot of steep ground: a steep cell whose way down flattens out
                int n = _nx * _nz, laid = 0;
                for (int k = 0; k < n && laid < 160 && !_b.Full; k++)
                {
                    if (!_in[k] || _wet[k] || _slope[k] < 0.32f) continue;
                    int r = _recv[k];
                    if (r < 0 || _slope[r] > 0.2f || !_in[r]) continue;
                    var p = Pos(r % _nx, r / _nx);
                    if (PathDistance(p) < 1f || U(70, k) > 0.6f) continue;
                    Brushes.At(_b, p, _down[k], "screeApron", math.clamp(_cell * 2.6f, 6f, 13f), Op(0.6f, 0.9f), 1, ColourLevel.Any, "rock:scree");
                    laid++;
                }
                // every boulder: stones it shed downhill, grit round its foot, moss on its shady (north, +z) side
                for (int i = 0; i < _rocks.Count && !_b.Full; i++)
                {
                    var (p, r) = _rocks[i];
                    if (r < 1.5f) continue;
                    At(p, out float slope, out var down, out _, out _);
                    var dir = slope > 0.02f ? down : math.normalizesafe(new float2(U(71, i) - 0.5f, U(72, i) - 0.5f), new float2(1f, 0f));
                    if (PathDistance(p) > r)
                    {
                        Brushes.At(_b, p + dir * r * 0.8f, dir, "screeApron", r * 3.2f, Op(0.55f, 0.85f), 0, ColourLevel.Any, "rock:apron");
                        if (U(73, i) < 0.6f) Brushes.At(_b, p + new float2(0f, r * 0.7f), new float2(0f, 1f), "mossPatch", r * 2.2f, Op(0.4f, 0.7f), 0, ColourLevel.Any, "rock:moss");
                    }
                }
                // small stones: a few pebbles round them, never on the path
                var small = new List<(float2, float)>();
                foreach (var rk in _rocks) if (rk.r < 1.5f) small.Add((rk.p, 1.6f));
                if (small.Count > 0) Brushes.Under(_b, small, "pebbles", 0.35f * A, float2.zero, _wild, new ScatterOptions { Opacity = Op(0.5f, 0.85f), Clump = 2f, Loners = 0.4f, Cause = "rock:pebbles" });
                // fallen logs: bark crumbs and moss along them, a little woodland floor
                for (int i = 0; i < _logs.Count && !_b.Full; i++)
                {
                    var (p, yaw) = _logs[i];
                    var along = new float2(math.sin(yaw), math.cos(yaw));
                    var side = new float2(-along.y, along.x);
                    Brushes.At(_b, p + side * 0.6f, along, "mossPatch", 5.5f, Op(0.4f, 0.7f), 1, ColourLevel.Any, "rock:log");
                    Brushes.At(_b, p - side * 0.5f, along, "barkMulch", 5f, Op(0.35f, 0.6f), 0, ColourLevel.Any, "rock:log");
                }
            }

            // ───────────── trees ─────────────

            private void Trees()
            {
                if (_trees.Count == 0) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var fall = BrushArea.Of(_ring); fall.Keep = p => !WetAt(p);
                var bySpecies = new Dictionary<string, List<(float2, float)>>(StringComparer.Ordinal);
                var dead = new List<(float2, float)>();
                foreach (var t in _trees)
                {
                    if (t.Dead) { dead.Add((t.P, t.Crown)); continue; }
                    if (t.Leaves == null) continue;
                    if (!bySpecies.TryGetValue(t.Leaves, out var l)) bySpecies[t.Leaves] = l = new List<(float2, float)>();
                    l.Add((t.P, t.Crown));
                }
                // nature is thinner per tree than a park (a forest of 2000 trees is not 2000 park trees' worth of leaves):
                // the cover goes down as the wood gets denser, the carpet stays continuous because crowns overlap
                float ha = math.max(0.1f, Poly.Area(_ring) / 10000f);
                float perHa = _trees.Count / ha;
                float thin = math.clamp(80f / math.max(1f, perHa), 0.35f, 1f);
                foreach (var kv in bySpecies)
                {
                    // broadleaf trees drop the wild litter (dark brown and olive); needles and petals keep their own
                    string kind = kv.Key == "pineNeedles" || kv.Key == "blossomPetals" ? Leaf(kv.Key) : _b.Lib.Has("leafLitterWild") ? "leafLitterWild" : Leaf(kv.Key);
                    float cov = (kind == "pineNeedles" ? 0.5f : kind == "blossomPetals" ? 0.3f : 0.52f) * A * thin;
                    Brushes.Under(_b, Decimate(kv.Value, perHa), kind, cov, Weathering.Wind, fall, new ScatterOptions { Opacity = Op(0.45f, 0.85f), Scale = new float2(0.9f, 1.4f), Clump = 5f, Spread = 0.55f, Loners = 0.25f, Spill = 2f, Level = ColourLevel.Quiet, Cause = "leaves" });
                }
                _r.Timing["trees:leaves"] = sw.Elapsed.TotalMilliseconds; sw.Restart();
                // groups: woodland floor and moss in the deep shade, a few sticks
                // all groups in one brush call per kind (a call per group sampled its area again for each of hundreds of groups:
                // 1.1-1.8 s on a 42 ha forest)
                var groups = Groups(9f);
                var needleCrowns = new List<(float2, float)>(); var floorCrowns = new List<(float2, float)>(); var mossCrowns = new List<(float2, float)>();
                foreach (var g in groups)
                {
                    if (g.Count < 3) continue;
                    bool conifer = true;
                    foreach (var t in g) conifer &= t.Conifer;
                    // small groups lay a thinner floor: their crowns count for less
                    float k = g.Count >= 12 ? 0.9f : 0.75f;
                    foreach (var t in g)
                    {
                        (conifer ? needleCrowns : floorCrowns).Add((t.P, t.Crown * k));
                        if (g.Count >= 5) mossCrowns.Add((t.P, t.Crown * 0.8f));
                    }
                }
                needleCrowns = Decimate(needleCrowns, perHa); floorCrowns = Decimate(floorCrowns, perHa); mossCrowns = Decimate(mossCrowns, perHa);
                Brushes.Under(_b, needleCrowns, "pineNeedles", 0.2f * A, Weathering.Wind * 0.3f, _wild, new ScatterOptions { Opacity = Op(0.35f, 0.7f), Clump = 3f, Cause = "woodland" });
                Brushes.Under(_b, floorCrowns, "woodlandFloor", 0.16f * A, Weathering.Wind * 0.3f, _wild, new ScatterOptions { Opacity = Op(0.35f, 0.7f), Clump = 3f, Cause = "woodland" });
                Brushes.Under(_b, mossCrowns, "mossPatch", 0.05f * A, float2.zero, _wild, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 2f, Loners = 0.4f, Cause = "woodland:moss" });
                var allCrowns = new List<(float2, float)>(needleCrowns); allCrowns.AddRange(floorCrowns);
                Brushes.Under(_b, allCrowns, "twigs", 0.04f * A, Weathering.Wind * 0.3f, _wild, new ScatterOptions { Opacity = Op(0.5f, 0.85f), Clump = 2f, Loners = 0.5f, Cause = "woodland:twigs" });
                _r.Timing["trees:groups"] = sw.Elapsed.TotalMilliseconds; sw.Restart();
                // dead trees: their bark and branches on the ground round them
                if (dead.Count > 0)
                {
                    Brushes.Under(_b, dead, "twigs", 0.5f * A, Weathering.Wind * 0.4f, _wild, new ScatterOptions { Opacity = Op(0.55f, 0.9f), Clump = 2f, Cause = "woodland:deadwood" });
                    Brushes.Under(_b, dead, "barkMulch", 0.2f * A, float2.zero, _wild, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 2f, Cause = "woodland:deadwood" });
                }
            }

            /// <summary>
            /// A dense wood's crowns thinned for the leaf field: every k-th crown, k growing past 90 trees a hectare, each grown by
            /// √k. The field saturates where crowns overlap anyway; the brush's cost was the crowns it looked at per sample
            /// (1.1-1.7 s on a 42 ha forest, now a fraction).
            /// </summary>
            private static List<(float2, float)> Decimate(List<(float2 p, float r)> crowns, float perHa)
            {
                int k = (int)math.ceil(perHa / 90f);
                if (k <= 1 || crowns.Count < 8) return crowns;
                float grow = math.sqrt(k) * 0.85f;
                var o = new List<(float2, float)>(crowns.Count / k + 1);
                for (int i = 0; i < crowns.Count; i += k) o.Add((crowns[i].p, crowns[i].r * grow));
                return o;
            }

            private List<List<Tree>> Groups(float link)
            {
                var res = new List<List<Tree>>();
                var done = new bool[_trees.Count];
                var grid = new PointGrid(link);
                for (int i = 0; i < _trees.Count; i++) grid.Add(_trees[i].P, i);
                for (int i = 0; i < _trees.Count; i++)
                {
                    if (done[i] || _trees[i].Dead) continue;
                    done[i] = true;
                    var g = new List<Tree> { _trees[i] };
                    var q = new Queue<int>(); q.Enqueue(i);
                    while (q.Count > 0 && g.Count < 300)
                    {
                        int a = q.Dequeue();
                        foreach (int j in grid.Near(_trees[a].P, 1))
                            if (!done[j] && !_trees[j].Dead && math.distancesq(_trees[a].P, _trees[j].P) < link * link) { done[j] = true; g.Add(_trees[j]); q.Enqueue(j); }
                    }
                    res.Add(g);
                }
                return res;
            }

            // ───────────── feet ─────────────

            private void Feet()
            {
                // the trail itself: packed earth and grit over the game's paved path, laid along whole chains of pieces so it never
                // fades out at a joint (the greenway's spine stays a made path: it is a cycle route). Never thinned: half a dirt
                // trail is a broken look, not a lighter one.
                if (_type != "greenwayPark")
                {
                    var dirt = _b.Lib.Get("trailDirt");
                    float w = dirt == null ? 3.3f : DecalVariants.FootprintOf(dirt, 0).y;
                    float keep = _b.Density; _b.Density = 1f;
                    foreach (var (chain, half) in Chains())
                    {
                        if (half >= 2.2f) continue;
                        float sz = (half * 2f + 0.5f) / math.max(1f, w);
                        Brushes.EdgeStrip(_b, chain, "trailDirt", new StripOptions { Gaps = 0f, Run = new float2(400f, 900f), Opacity = Op(0.85f, 0.95f), Width = new float2(sz, sz * 1.05f), CornerDeg = 80f, Cause = "feet:trail" });
                    }
                    _b.Density = keep;
                }
                foreach (var l in _paths)
                {
                    if (l.Deck) continue;
                    // the trail's edge trodden into the ground on both sides, broken often (a wild trail is uneven)
                    for (int side = -1; side <= 1; side += 2)
                        Brushes.EdgeStrip(_b, Offset(l.P, side * (l.Half + 0.55f)), "pathEdgeWear", new StripOptions { Side = side > 0 ? -1 : 1, Gaps = 0.5f, Run = new float2(8f, 26f), Opacity = Op(0.28f, 0.55f), Width = new float2(0.65f, 0.95f), Level = ColourLevel.Quiet, Cause = "feet:edge" });
                    // roots bared where the trail passes close to a big tree
                    for (int i = 0; i < l.P.Length; i += 3)
                    {
                        var p = l.P[i];
                        foreach (int ti in _treeGrid.Near(p, 1))
                        {
                            var t = _trees[ti];
                            if (t.Dead || t.Crown < 2.8f) continue;
                            float d = math.distance(t.P, p);
                            if (d > l.Half + 2.6f || d < l.Half * 0.5f) continue;
                            if (U(80, ti) > 0.55f) continue;
                            var toTree = math.normalizesafe(t.P - p);
                            Brushes.At(_b, p + toTree * (l.Half + 0.9f), toTree, "exposedRoots", 3.6f, Op(0.55f, 0.85f), 0, ColourLevel.Any, "feet:roots");
                            break;
                        }
                    }
                    // puddles and mud where the trail dips
                    Dips(l);
                }
                // gates and junctions: trodden ground where feet turn
                foreach (var g in _gates)
                {
                    var inward = math.normalizesafe(Poly.Centroid(_ring) - g);
                    Brushes.At(_b, g + inward * 3f, inward, "barePatch", 6f, Op(0.3f, 0.55f), 1, ColourLevel.Any, "feet:gate");
                    Brushes.At(_b, g + inward * 2f, inward, "pebbles", 4.5f, Op(0.45f, 0.75f), 0, ColourLevel.Any, "feet:gate");
                }
                // benches on trails: a bare patch where feet rest
                for (int i = 0; i < _benches.Count && i < 30; i++)
                {
                    var (p, yaw) = _benches[i];
                    var fwd = new float2(math.sin(yaw), math.cos(yaw));
                    Brushes.At(_b, p + fwd * 1.2f, fwd, "barePatch", 3.5f, Op(0.3f, 0.55f), 0, ColourLevel.Any, "feet:bench");
                }
                AnimalTrails();
            }

            /// <summary>The trails joined end to end where exactly two pieces meet (a junction of three ends the chains there).</summary>
            private List<(float2[] pts, float half)> Chains()
            {
                var lines = _paths.FindAll(l => !l.Deck && l.P.Length >= 2);
                int n = lines.Count;
                var used = new bool[n];
                int Degree(float2 p) { int c = 0; foreach (var l in lines) { if (math.distancesq(l.P[0], p) < 0.25f) c++; if (math.distancesq(l.P[l.P.Length - 1], p) < 0.25f) c++; } return c; }
                int Next(float2 end, int skip, out bool reversed)
                {
                    reversed = false;
                    if (Degree(end) != 2) return -1;
                    for (int j = 0; j < n; j++)
                    {
                        if (used[j] || j == skip) continue;
                        if (math.distancesq(lines[j].P[0], end) < 0.25f) return j;
                        if (math.distancesq(lines[j].P[lines[j].P.Length - 1], end) < 0.25f) { reversed = true; return j; }
                    }
                    return -1;
                }
                var res = new List<(float2[], float)>();
                for (int i = 0; i < n; i++)
                {
                    if (used[i]) continue;
                    used[i] = true;
                    var pts = new List<float2>(lines[i].P);
                    float half = lines[i].Half;
                    // forward from the end, then backward from the start
                    for (int dir = 0; dir < 2; dir++)
                    {
                        while (true)
                        {
                            var end = dir == 0 ? pts[pts.Count - 1] : pts[0];
                            int j = Next(end, -1, out bool rev);
                            if (j < 0) break;
                            used[j] = true;
                            half = math.max(half, lines[j].Half);
                            var add = new List<float2>(lines[j].P);
                            if (dir == 0) { if (rev) add.Reverse(); pts.AddRange(add.GetRange(1, add.Count - 1)); }
                            else { if (!rev) add.Reverse(); add.RemoveAt(add.Count - 1); pts.InsertRange(0, add); }
                        }
                    }
                    res.Add((pts.ToArray(), half));
                }
                return res;
            }

            private static float2[] Offset(float2[] line, float d)
            {
                var o = new float2[line.Length];
                for (int i = 0; i < line.Length; i++)
                {
                    var t = math.normalizesafe(line[math.min(line.Length - 1, i + 1)] - line[math.max(0, i - 1)]);
                    o[i] = line[i] + new float2(-t.y, t.x) * d;
                }
                return o;
            }

            private void Dips(Line l)
            {
                var hgt = new float[l.P.Length];
                for (int i = 0; i < hgt.Length; i++) hgt[i] = _ground(l.P[i]);
                int laid = 0;
                for (int i = 3; i < hgt.Length - 3 && laid < 6; i++)
                {
                    float lo = hgt[i]; bool min = true; float rise = float.MaxValue;
                    for (int side = -1; side <= 1 && min; side += 2)
                    {
                        float peak = lo;
                        for (int k = 1; k <= 6 && i + side * k >= 0 && i + side * k < hgt.Length; k++)
                        {
                            float h = hgt[i + side * k];
                            if (h < lo - 1e-4f) { min = false; break; }
                            peak = math.max(peak, h);
                        }
                        rise = math.min(rise, peak - lo);
                    }
                    if (!min || rise < 0.05f) continue;
                    var dir = math.normalizesafe(l.P[i + 1] - l.P[i - 1]);
                    Brushes.At(_b, l.P[i], dir, "puddlePiece", math.clamp(l.Half * 2.6f, 3f, 6f), Op(0.55f, 0.85f), 1, ColourLevel.Any, "feet:puddle");
                    Brushes.At(_b, l.P[i] + new float2(-dir.y, dir.x) * (l.Half + 0.6f), dir, "mudPatch", 5f, Op(0.35f, 0.6f), 1, ColourLevel.Any, "feet:mud");
                    laid++;
                    i += 6;
                }
            }

            private void AnimalTrails()
            {
                // from the woods to water (a pond, the end of a rill, a marsh), else from wood to wood across the open ground
                var groups = Groups(10f);
                groups.RemoveAll(g => g.Count < 6);
                if (groups.Count == 0) return;
                var targets = new List<float2>();
                foreach (var w in _water) if (Poly.Area(w) > 60f) targets.Add(Poly.ClosestPointOnBoundary(w, Poly.Centroid(_ring), out _, out _));
                int want = math.clamp((int)(Poly.Area(_ring) / 25000f) + 1, 1, 6);
                for (int t = 0; t < want && !_b.Full; t++)
                {
                    var g = groups[(int)(U(90, t) * groups.Count) % groups.Count];
                    var from = g[(int)(U(91, t) * g.Count) % g.Count].P;
                    float2 to;
                    if (targets.Count > 0) to = targets[(int)(U(92, t) * targets.Count) % targets.Count];
                    else
                    {
                        var g2 = groups[(int)(U(93, t) * groups.Count) % groups.Count];
                        if (ReferenceEquals(g2, g) && groups.Count > 1) g2 = groups[(groups.IndexOf(g) + 1) % groups.Count];
                        to = g2[(int)(U(94, t) * g2.Count) % g2.Count].P;
                    }
                    float dist = math.distance(from, to);
                    if (dist < 25f || dist > 260f) continue;
                    // a wandering walk toward the target, keeping to the easy ground (lower slope)
                    var pts = new List<float2> { from };
                    var p = from;
                    var heading = math.normalizesafe(to - from);
                    for (int s = 0; s < 200 && math.distance(p, to) > 4f; s++)
                    {
                        var want2 = math.normalizesafe(to - p);
                        float wob = (ValueNoise.Clumps(p, 18f, H(95, t)) - 0.5f) * 1.4f;
                        var w = new float2(want2.x * math.cos(wob) - want2.y * math.sin(wob), want2.x * math.sin(wob) + want2.y * math.cos(wob));
                        heading = math.normalizesafe(heading * 0.6f + w * 0.4f);
                        p += heading * 3f;
                        if (!Poly.Contains(_ring, p)) break;
                        pts.Add(p);
                    }
                    if (pts.Count < 6) continue;
                    foreach (var piece in OffPaths(Smooth(pts, 1), 0.8f))
                        Brushes.EdgeStrip(_b, piece, "animalTrail", new StripOptions { Gaps = 0.3f, Run = new float2(12f, 40f), Opacity = Op(0.45f, 0.75f), CornerDeg = 70f, Cause = "feet:animal" });
                    if (targets.Count > 0)
                        Brushes.At(_b, pts[pts.Count - 1], heading, "pawPrints", 3f, Op(0.5f, 0.8f), 1, ColourLevel.Any, "feet:prints");
                }
            }

            // ───────────── camp ─────────────

            private void Camp()
            {
                for (int i = 0; i < _tents.Count && !_b.Full; i++)
                {
                    var (p, yaw) = _tents[i];
                    var fwd = new float2(math.sin(yaw), math.cos(yaw));
                    // the tent's floor flattened the grass; the door side trodden more
                    Brushes.At(_b, p + fwd * 1.2f, fwd, "trampledGrass", 6.5f, Op(0.45f, 0.75f), 1, ColourLevel.Any, "camp:pitch");
                    // a line trodden from the tent to the nearest path
                    var near = NearestOnPath(p, 30f, out bool found);
                    if (found && math.distance(near, p) > 4f)
                        Brushes.EdgeStrip(_b, new[] { p + fwd * 1.8f, math.lerp(p, near, 0.55f) + new float2(-fwd.y, fwd.x) * 0.8f, near }, "wornPath", new StripOptions { Gaps = 0f, Opacity = Op(0.4f, 0.65f), Width = new float2(0.55f, 0.7f), CornerDeg = 80f, Cause = "camp:track" });
                }
                for (int i = 0; i < _fires.Count && !_b.Full; i++)
                {
                    var (p, _) = _fires[i];
                    Brushes.At(_b, p, new float2(1f, 0f), "barePatch", 5.5f, Op(0.45f, 0.7f), 1, ColourLevel.Any, "camp:fire");
                    Brushes.At(_b, p, new float2(1f, 0f), "ashScorch", 2.8f, Op(0.8f, 1f), 0, ColourLevel.Any, "camp:ash");
                }
                for (int i = 0; i < _tables.Count && i < 60 && !_b.Full; i++)
                    Brushes.At(_b, _tables[i].p, new float2(1f, 0f), "barePatch", 3.6f, Op(0.3f, 0.5f), 0, ColourLevel.Any, "camp:table");
                foreach (var pad in _pads)
                {
                    // a worn pitch: the gravel spread, its edge ragged into the grass
                    var c = Poly.Centroid(pad);
                    float r = math.sqrt(Poly.Area(pad) / math.PI);
                    Brushes.At(_b, c, new float2(1f, 0f), "pebbles", r * 1.6f, Op(0.45f, 0.75f), 1, ColourLevel.Any, "camp:pad");
                }
            }

            private float2 NearestOnPath(float2 p, float reach, out bool found)
            {
                found = false; float best = reach; float2 q = p;
                foreach (var l in _paths)
                {
                    if (l.Deck) continue;
                    for (int i = 0; i + 1 < l.P.Length; i++)
                    {
                        var ab = l.P[i + 1] - l.P[i];
                        float t = math.saturate(math.dot(p - l.P[i], ab) / math.max(1e-4f, math.lengthsq(ab)));
                        var c = l.P[i] + ab * t;
                        float d = math.distance(c, p) - l.Half;
                        if (d < best) { best = d; q = c + math.normalizesafe(p - c) * l.Half; found = true; }
                    }
                }
                return q;
            }
        }

        /// <summary>A small binary min-heap of cell indices by level (the priority flood).</summary>
        private sealed class MinHeap
        {
            private int[] _k; private float[] _v; public int Count;
            public MinHeap(int cap) { _k = new int[math.max(16, cap)]; _v = new float[_k.Length]; }
            public void Push(int k, float v)
            {
                if (Count == _k.Length) { Array.Resize(ref _k, Count * 2); Array.Resize(ref _v, Count * 2); }
                int i = Count++;
                while (i > 0) { int p = (i - 1) >> 1; if (_v[p] <= v) break; _k[i] = _k[p]; _v[i] = _v[p]; i = p; }
                _k[i] = k; _v[i] = v;
            }
            public int Pop(out float v)
            {
                int top = _k[0]; v = _v[0];
                int lastK = _k[--Count]; float lastV = _v[Count];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1;
                    if (c >= Count) break;
                    if (c + 1 < Count && _v[c + 1] < _v[c]) c++;
                    if (_v[c] >= lastV) break;
                    _k[i] = _k[c]; _v[i] = _v[c]; i = c;
                }
                if (Count > 0) { _k[i] = lastK; _v[i] = lastV; }
                return top;
            }
        }
    }
}
