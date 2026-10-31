// Placemaker — Biome mix: the decals of each natural area (docs/runbooks/biomes.md). GPL-3.0.
//
// A type that plans biomes (Core/Layers/Common/Biomes) leaves each area as regions "biome:<id>" (holes "biomeHole:<id>").
// Its ground design calls BiomeGround.Run(scene) and every area gets its biome's decal passes (BiomeDef.Decals, data):
//   All        evenly over the area
//   Core       thicker toward its heart          Rim     a band along its edge (the ecotone)
//   Downhill   the downhill side of the ground    Shade   the north-facing side of every lump (moss)
//   Things     round its own rocks and logs       Spill   just outside it, on its neighbour's ground (scree onto grass)
// Fill passes lay one shape over each area's heart (marsh mud). Read from the plan (regions, objects) and the built ground.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Biomes;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    internal static class BiomeGround
    {
        /// <summary>The scene has biome areas.</summary>
        public static bool Any(Weathering.Scene sc)
        {
            foreach (var l in sc.Layers) foreach (var r in l.Regions)
                    if (r?.Tags != null) foreach (var t in r.Tags) if (t != null && t.StartsWith(BiomeBuilder.RegionPrefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Lays every biome area's decals. <paramref name="quiet"/>: formal types (quiet colours everywhere).</summary>
        public static void Run(Weathering.Scene sc, bool quiet = false)
        {
            var outers = new Dictionary<string, List<float2[]>>(StringComparer.Ordinal);
            var holes = new Dictionary<string, List<float2[]>>(StringComparer.Ordinal);
            var beds = new Dictionary<string, List<float2[]>>(StringComparer.Ordinal);
            var water = new List<float2[]>();
            var things = new List<float2>();
            foreach (var l in sc.Layers)
            {
                foreach (var r in l.Regions)
                {
                    if (r?.Tags == null || r.Ring == null || r.Ring.Length < 3) continue;
                    foreach (var t in r.Tags)
                    {
                        if (t == null) continue;
                        if (t.StartsWith(BiomeBuilder.RegionPrefix, StringComparison.Ordinal)) { Add(outers, t.Substring(BiomeBuilder.RegionPrefix.Length), Poly.EnsureCcw(Poly.To2D(r.Ring))); break; }
                        if (t.StartsWith(BiomeBuilder.HolePrefix, StringComparison.Ordinal)) { Add(holes, t.Substring(BiomeBuilder.HolePrefix.Length), Poly.EnsureCcw(Poly.To2D(r.Ring))); break; }
                        if (t.StartsWith(BiomeBuilder.BedPrefix, StringComparison.Ordinal)) { Add(beds, t.Substring(BiomeBuilder.BedPrefix.Length), Poly.EnsureCcw(Poly.To2D(r.Ring))); break; }
                    }
                }
                foreach (var o in l.Objects)
                    if (o != null && (o.Category == AssetCategories.Rock || o.Category == AssetCategories.PlayNature) && o.Scale >= 0.45f) things.Add(o.Position.xz);
                foreach (var s in l.Surfaces)
                    if (s != null && s.Role == SurfaceRole.Water && s.Ring != null && s.Ring.Length >= 3) water.Add(Poly.EnsureCcw(Poly.To2D(s.Ring)));
            }
            if (outers.Count == 0) return;
            var thingGrid = new PointGrid(8f);
            for (int i = 0; i < things.Count; i++) thingGrid.Add(things[i], i);
            int bi = 0;
            foreach (var kv in outers)
            {
                bi++;
                var d = BiomeLibrary.Get(kv.Key);
                if (d == null || d.Decals.Length == 0) continue;
                holes.TryGetValue(kv.Key, out var hl);
                beds.TryGetValue(kv.Key, out var bl);
                new Area(sc, d, kv.Value, hl ?? new List<float2[]>(), things, thingGrid, quiet, bi) { Beds = bl, Water = water }.Lay();
            }
        }

        private static void Add(Dictionary<string, List<float2[]>> d, string k, float2[] r)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<float2[]>();
            l.Add(r);
        }

        private sealed class Area
        {
            private readonly Weathering.Scene _sc;
            private readonly BiomeDef _d;
            private readonly List<float2[]> _outer, _holes;
            private readonly SegmentIndex _edge = new SegmentIndex(12f);
            private readonly BrushArea _area;
            private readonly List<float2> _things;
            private readonly PointGrid _thingGrid;
            private readonly bool _quiet;
            private readonly int _salt;
            /// <summary>The area's planted beds (mulch) and every pond and pool of the plan.</summary>
            public List<float2[]> Beds, Water;
            // (perf) the area as a raster: the brushes test every candidate, and dozens of rings of hundreds of points made a 36 ha
            // park's weathering take 8.8 s; a lookup in a 1.5 m mask is constant
            private readonly float2 _lo;
            private readonly float _cell;
            private readonly int _nx, _ny;
            private readonly bool[] _mask;
            private readonly float2 _boxLo, _boxHi;

            public Area(Weathering.Scene sc, BiomeDef d, List<float2[]> outer, List<float2[]> holes, List<float2> things, PointGrid grid, bool quiet, int salt)
            {
                _sc = sc; _d = d; _outer = outer; _holes = holes; _things = things; _thingGrid = grid; _quiet = quiet; _salt = salt;
                foreach (var r in outer) for (int i = 0; i < r.Length; i++) _edge.Add(r[i], r[(i + 1) % r.Length], 0f);
                foreach (var r in holes) for (int i = 0; i < r.Length; i++) _edge.Add(r[i], r[(i + 1) % r.Length], 0f);
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                foreach (var r in outer) { Poly.Bounds(r, out var a, out var b); lo = math.min(lo, a); hi = math.max(hi, b); }
                _boxLo = lo; _boxHi = hi;
                lo -= 6f; hi += 6f;
                var ext = hi - lo;
                _cell = math.max(1.5f, math.sqrt(ext.x * ext.y / 400000f));
                _lo = lo;
                _nx = math.max(1, (int)math.ceil(ext.x / _cell)); _ny = math.max(1, (int)math.ceil(ext.y / _cell));
                _mask = new bool[_nx * _ny];
                var xs = new List<float>();
                for (int j = 0; j < _ny; j++)
                {
                    float y = lo.y + (j + 0.5f) * _cell;
                    xs.Clear();
                    foreach (var r in outer) Crossings(r, y, xs);
                    foreach (var r in holes) Crossings(r, y, xs);
                    xs.Sort();
                    for (int q = 0; q + 1 < xs.Count; q += 2)
                    {
                        int i0 = math.max(0, (int)math.ceil((xs[q] - lo.x) / _cell - 0.5f)), i1 = math.min(_nx - 1, (int)math.floor((xs[q + 1] - lo.x) / _cell - 0.5f));
                        for (int i = i0; i <= i1; i++) _mask[j * _nx + i] = true;
                    }
                }
                _area = new BrushArea();
                _area.Outer.Add(new[] { _boxLo, new float2(_boxHi.x, _boxLo.y), _boxHi, new float2(_boxLo.x, _boxHi.y) });
                _area.Keep = Inside;
            }

            private static void Crossings(float2[] r, float y, List<float> xs)
            {
                for (int i = 0, n = r.Length; i < n; i++)
                {
                    var a = r[i]; var b = r[(i + 1) % n];
                    if ((a.y <= y) == (b.y <= y)) continue;
                    xs.Add(a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x));
                }
            }

            private bool Inside(float2 p)
            {
                int i = (int)((p.x - _lo.x) / _cell), j = (int)((p.y - _lo.y) / _cell);
                return i >= 0 && j >= 0 && i < _nx && j < _ny && _mask[j * _nx + i];
            }

            private float Edge(float2 p) => _edge.Clearance(p, 30f);

            private SegmentIndex _waterEdges;
            private SegmentIndex WaterEdges()
            {
                if (_waterEdges != null) return _waterEdges;
                _waterEdges = new SegmentIndex(10f);
                if (Water != null)
                    foreach (var r in Water)
                    {
                        Poly.Bounds(r, out var a, out var b);
                        if (b.x < _boxLo.x - 8f || b.y < _boxLo.y - 8f || a.x > _boxHi.x + 8f || a.y > _boxHi.y + 8f) continue;
                        for (int i = 0; i < r.Length; i++) _waterEdges.Add(r[i], r[(i + 1) % r.Length], 0f);
                    }
                return _waterEdges;
            }

            /// <summary>The lines a strip pass runs along (closed rings repeat their first point).</summary>
            private IEnumerable<float2[]> Lines(BiomeWhere where, int pass)
            {
                switch (where)
                {
                    case BiomeWhere.Water:
                        if (Water != null)
                            foreach (var r in Water)
                            {
                                Poly.Bounds(r, out var a, out var b);
                                if (b.x < _boxLo.x - 6f || b.y < _boxLo.y - 6f || a.x > _boxHi.x + 6f || a.y > _boxHi.y + 6f) continue;
                                var c = Poly.Centroid(r);
                                if (!Inside(c) && !Inside(r[0])) continue;
                                yield return Closed(r);
                            }
                        break;
                    case BiomeWhere.Beds:
                    case BiomeWhere.Rim when Beds != null && Beds.Count > 0:
                        if (Beds != null) foreach (var r in Beds) yield return Closed(r);
                        break;
                    case BiomeWhere.Trails:
                        foreach (var t in Trails(0)) yield return t;   // every trail pass on the same lines (sand under the trodden soil)
                        break;
                    default:
                        foreach (var r in _outer) yield return Closed(r);
                        break;
                }
            }

            private static float2[] Closed(float2[] r)
            {
                var o = new float2[r.Length + 1];
                Array.Copy(r, o, r.Length);
                o[r.Length] = r[0];
                return o;
            }

            /// <summary>
            /// Trodden lines across the area: for each piece of it, one to four wandering walks from one side to the other (a
            /// heath's sandy paths), cut where they leave it.
            /// </summary>
            private IEnumerable<float2[]> Trails(int pass)
            {
                int ri = 0;
                foreach (var r in _outer)
                {
                    ri++;
                    float a = math.abs(Poly.SignedArea(r));
                    if (a < 600f) continue;
                    int n = math.clamp((int)(a / 4500f) + 1, 1, 4);
                    for (int t = 0; t < n; t++)
                    {
                        uint h = Hashing.Mix((uint)_salt * 131u, ri * 17 + t, pass);
                        var p0 = r[(int)(Hashing.Unit(h) * r.Length) % r.Length];
                        var p1 = r[(int)((Hashing.Unit(h) + 0.4f + 0.2f * Hashing.Unit(Hashing.Mix(h, 1u))) * r.Length) % r.Length];
                        float len = math.distance(p0, p1);
                        if (len < 15f) continue;
                        var dir = (p1 - p0) / len;
                        var nrm = new float2(-dir.y, dir.x);
                        var pts = new List<float2>();
                        int steps = (int)(len / 4f);
                        for (int k = 0; k <= steps; k++)
                        {
                            float u = k / (float)steps;
                            float wob = (ValueNoise.Sample(new float2(u * len / 18f, t * 7.3f), h) - 0.5f) * 2f * math.min(12f, len * 0.15f) * math.sin(u * math.PI);
                            var q = math.lerp(p0, p1, u) + nrm * wob;
                            if (Inside(q)) pts.Add(q);
                            else if (pts.Count >= 3) { yield return pts.ToArray(); pts = new List<float2>(); }
                            else pts.Clear();
                        }
                        if (pts.Count >= 3) yield return pts.ToArray();
                    }
                }
            }

            private float2 Gradient(float2 p, float e = 2f) => new float2(
                _sc.GroundAt(p + new float2(e, 0)) - _sc.GroundAt(p - new float2(e, 0)),
                _sc.GroundAt(p + new float2(0, e)) - _sc.GroundAt(p - new float2(0, e))) / (2f * e);

            public void Lay()
            {
                float a = math.min(2.2f, _sc.GrimeAmount);
                int n = 0;
                foreach (var pass in _d.Decals)
                {
                    n++;
                    var op = _sc.Opacity(pass.OpacityLo, pass.OpacityHi);
                    var lv = _quiet || pass.Quiet ? ColourLevel.Quiet : ColourLevel.Any;
                    string cause = "biome:" + _d.Id + ":" + pass.Kind;
                    if (pass.Fill)
                    {
                        foreach (var r in _outer)
                        {
                            var lr = LayerRegion.From(r);
                            if (lr == null || lr.Area < 120f) continue;
                            var core = lr.InsetVertices(math.min(4f, math.sqrt(lr.Area) * 0.12f));
                            if (core.Length >= 3 && Poly.IsSimple(core) && Poly.SignedArea(core) > 40f) Brushes.Fill(_sc.Brush, core, pass.Kind, op, lv, cause);
                        }
                        continue;
                    }
                    var kd = _sc.Brush.Lib.Get(pass.Kind);
                    bool strip = kd != null && (kd.Variants.Tile == VariantTiling.X || kd.Variants.Tile == VariantTiling.Chain);
                    if (strip)
                    {
                        // a strip goes along lines: the water's edge, the beds' edges, trodden trails, the area's own edge
                        foreach (var line in Lines(pass.Where, n))
                            Brushes.EdgeStrip(_sc.Brush, line, pass.Kind, new StripOptions { Side = 1, Offset = pass.Where == BiomeWhere.Water ? 0.4f : 0f, Gaps = 0.2f, Opacity = op, Level = lv, Closed = line.Length > 2 && math.distance(line[0], line[line.Length - 1]) < 0.01f, Cause = cause });
                        continue;
                    }
                    BrushArea area = _area;
                    Func<float2, float> w;
                    switch (pass.Where)
                    {
                        case BiomeWhere.Drift:
                        {
                            uint ds = Hashing.Mix((uint)_salt * 7919u, Seeds.Hash(pass.Kind));
                            w = p => BiomeField.Smooth01((BiomeField.Fbm(p / 9f, ds) - 0.5f) / 0.1f);
                            break;
                        }
                        case BiomeWhere.Water:
                        {
                            var edges = WaterEdges();
                            w = p => 1f - BiomeField.Smooth01(edges.Clearance(p, 6f) / 5f);
                            break;
                        }
                        case BiomeWhere.Beds:
                        {
                            if (Beds == null || Beds.Count == 0) { w = null; area = new BrushArea(); break; }
                            area = new BrushArea();
                            area.Outer.AddRange(Beds);
                            w = null;
                            break;
                        }
                        case BiomeWhere.Core: w = p => 0.15f + 0.85f * BiomeField.Smooth01(Edge(p) / 10f); break;
                        case BiomeWhere.Rim: w = p => 1f - BiomeField.Smooth01((Edge(p) - 1f) / 8f); break;
                        case BiomeWhere.Downhill:
                            w = p =>
                            {
                                var g = Gradient(p);
                                float s = math.length(g);
                                return 0.2f + 0.8f * math.saturate((s - 0.04f) / 0.2f);
                            };
                            break;
                        case BiomeWhere.Shade:
                            // the slope facing north (+z: the low sun is in the south) holds the moss
                            w = p => 0.1f + 0.9f * math.saturate(-Gradient(p).y * 4f + 0.2f);
                            break;
                        case BiomeWhere.Things:
                            w = p =>
                            {
                                float best = 0f;
                                foreach (int i in _thingGrid.Near(p, 1)) best = math.max(best, 1f - math.saturate(math.distance(_things[i], p) / 4.5f));
                                return best;
                            };
                            break;
                        case BiomeWhere.Spill:
                        {
                            // a band 0-5 m outside the area, inside the site
                            area = new BrushArea();
                            var glo = _boxLo - 5f; var ghi = _boxHi + 5f;
                            area.Outer.Add(new[] { glo, new float2(ghi.x, glo.y), ghi, new float2(glo.x, ghi.y) });
                            var site = _sc.Outline;
                            area.Keep = p => !Inside(p) && Edge(p) < 5f && Poly.Contains(site, p);
                            w = p => 1f - BiomeField.Smooth01(Edge(p) / 5f);
                            break;
                        }
                        default: w = null; break;
                    }
                    if (area.IsEmpty) continue;
                    Brushes.Scatter(_sc.Brush, area, pass.Kind, pass.Cover * a, new ScatterOptions { Weight = w, Opacity = op, Clump = pass.Clump, Loners = 0.2f, Level = lv, Cause = cause });
                }
            }
        }
    }
}
