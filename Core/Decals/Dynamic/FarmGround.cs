// Placemaker — the farms' ground design (P2-3, 2026-10-30; docs/runbooks/farmstead.md). GPL-3.0.
//
// Runs after the shared causes of Weathering.Scene (tone, the trees' leaves, path-edge wear, puddles in the low points) and
// lays what a working farm has, every piece from a cause the farm planned (FarmsteadLayer leaves them as regions and facts):
//   yard      the farmyard trodden and driven to mud: churned patches, puddles in its low ground, tractor tyre tracks from the
//             gate to the sheds, straw blown off the bale stacks, the manure heap's black seepage
//   tracks    the farm tracks worn to packed earth (the paved path under them reads as a dirt track), ruts and puddles
//   gates     field gateways churned by wheels, paddock gates and troughs by hooves (hoof-churned mud, dung)
//   fields    tramlines across the crops every 24 m, the headland's turning wheel line, straw windrows after the harvest
//   grazing   dung and molehills over the pastures and paddocks, trodden lines from gate to trough
//   road      mud carried out onto the road on the tractor's tyres, soil spilt at the entrance
// How muddy the yard is follows the farm's Yard setting (fact farmMud); the season (fact farmSeason) decides the fields.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    internal static class FarmGround
    {
        public static readonly string[] Types = { "farmstead" };
        /// <summary>Region tag prefixes the farm leaves for its ground (FarmsteadLayer).</summary>
        public const string GateTag = "farmGate:", HeapTag = "farmHeap:", KindTag = "fieldKind:", RowsTag = "rows:";
        public const string MudFact = "farmMud", SeasonFact = "farmSeason";

        public static Dictionary<string, Action<Weathering.Scene>> Register(Dictionary<string, Action<Weathering.Scene>> d)
        {
            foreach (var t in Types) d[t] = sc => new Design(sc).Go();
            return d;
        }

        private sealed class Field { public float2[] Ring; public string Kind = ""; public float2 Rows = new float2(1, 0); }

        private sealed class Design
        {
            private readonly Weathering.Scene _sc;
            private readonly BrushContext _b;
            private readonly float _a, _mud;
            private readonly int _season;
            private readonly List<float2[]> _yards = new List<float2[]>(), _paddocks = new List<float2[]>();
            private readonly List<Field> _fields = new List<Field>();
            private readonly List<(string kind, float2 p)> _gates = new List<(string, float2)>(), _heaps = new List<(string, float2)>();
            private readonly List<float2[]> _tracks = new List<float2[]>();

            public Design(Weathering.Scene sc)
            {
                _sc = sc; _b = sc.Brush;
                _a = math.min(2.2f, sc.GrimeAmount);
                var f = sc.Host?.Facts;
                _mud = f != null && f.TryGetValue(MudFact, out var m) ? (float)m : 0.5f;
                _season = f != null && f.TryGetValue(SeasonFact, out var s) ? (int)s : 1;
                foreach (var l in sc.Layers)
                {
                    foreach (var r in l.Regions)
                    {
                        if (r?.Tags == null || r.Ring == null || r.Ring.Length < 3) continue;
                        var ring = Poly.EnsureCcw(Poly.To2D(r.Ring));
                        Field fd = null;
                        foreach (var t in r.Tags)
                        {
                            if (t == null) continue;
                            if (t == Layers.Types.Farmstead.FarmsteadLayer.YardTag) _yards.Add(ring);
                            else if (t == Layers.Types.Farmstead.FarmsteadLayer.PaddockPlace) _paddocks.Add(ring);
                            else if (t == Layers.Types.Farmstead.FarmsteadLayer.FieldTag) fd = fd ?? new Field { Ring = ring };
                            else if (t.StartsWith(GateTag, StringComparison.Ordinal)) _gates.Add((t.Substring(GateTag.Length), Poly.Centroid(ring)));
                            else if (t.StartsWith(HeapTag, StringComparison.Ordinal)) _heaps.Add((t.Substring(HeapTag.Length), Poly.Centroid(ring)));
                        }
                        if (fd == null) continue;
                        foreach (var t in r.Tags)
                        {
                            if (t == null) continue;
                            if (t.StartsWith(KindTag, StringComparison.Ordinal)) fd.Kind = t.Substring(KindTag.Length);
                            else if (t.StartsWith(RowsTag, StringComparison.Ordinal) && float.TryParse(t.Substring(RowsTag.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var deg))
                                fd.Rows = new float2(math.cos(math.radians(deg)), math.sin(math.radians(deg)));
                        }
                        _fields.Add(fd);
                    }
                }
                Chain(sc.Paths, _tracks);
            }

            private uint H(int a, int b = 0) => Hashing.Mix(_sc.Seed, 0xFA2B + a, b);
            private float2 Op(float lo, float hi) => _sc.Opacity(lo, hi);

            public void Go()
            {
                if (_sc.GrimeAmount <= 0f) return;
                // (Biome mix) the farm's natural areas: orchard rows, woods, rough grazing, wet ground, ponds
                BiomeGround.Run(_sc);
                Yard();
                Tracks();
                Gates();
                Fields();
                Grazing();
            }

            // ───────────── the farmyard ─────────────

            private void Yard()
            {
                foreach (var ring in _yards)
                {
                    var area = BrushArea.Of(ring);
                    float mud = _mud * _a;
                    var c = Poly.Centroid(ring);
                    // churned mud where wheels and boots go most: toward the gates and the middle, never an even film
                    Brushes.Scatter(_b, area, "hoofChurn", 0.22f * mud, new ScatterOptions
                    {
                        Weight = p => 0.3f + 0.7f * ValueNoise.Clumps(p, 14f, H(1)) * (0.6f + 0.4f * Near(p, _gates, 30f)),
                        Opacity = Op(0.45f, 0.85f), Scale = new float2(0.6f, 1.1f), Clump = 4f, Loners = 0.3f, Cause = "farm:yardMud",
                    });
                    // puddles in the yard's low ground
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var q in ring) { float g = _sc.GroundAt(q); lo = math.min(lo, g); hi = math.max(hi, g); }
                    Brushes.Scatter(_b, area, "puddlePiece", 0.08f * (0.4f + mud), new ScatterOptions
                    {
                        Weight = p => math.saturate(0.25f + 0.75f * ValueNoise.Clumps(p, 10f, H(2))) * (hi - lo > 0.3f ? 1f - math.saturate((_sc.GroundAt(p) - lo) / (hi - lo)) : 1f),
                        Opacity = Op(0.55f, 0.9f), Scale = new float2(0.5f, 1f), Clump = 2.5f, Loners = 0.4f, Cause = "farm:yardPuddles",
                    });
                    // grit and straw over the whole yard, a mottle so the surface never reads as one tile
                    Brushes.Scatter(_b, area, "strawLitter", 0.1f * _a, new ScatterOptions { Weight = p => 0.3f + 0.7f * ValueNoise.Clumps(p, 9f, H(3)), Opacity = Op(0.6f, 1f), Clump = 3f, Loners = 0.4f, Cause = "farm:yardStraw" });
                    Brushes.Scatter(_b, area, "groundMottle", 0.25f, new ScatterOptions { Weight = p => 0.4f + 0.6f * ValueNoise.Clumps(p, 18f, H(4)), Opacity = Op(0.25f, 0.45f), Clump = 3f, Level = ColourLevel.Quiet, Cause = "farm:yardTone" });
                    // tractor tracks: from each gate in the yard's direction, curving into the sheds' side
                    int k = 0;
                    foreach (var (kind, g) in _gates)
                    {
                        if (kind != "road" && kind != "yard") continue;
                        if (math.distance(g, c) > 160f) continue;
                        var line = Arc(g, c, H(10, k++));
                        Twin(line, "mudTyreTrack", 1.1f, Op(0.5f, 0.85f), "farm:yardTyres");
                    }
                    Brushes.EdgeStrip(_b, Closed(ring), "pathEdgeWear", new StripOptions { Side = -1, Offset = 0.3f, Gaps = 0.35f, Opacity = Op(0.4f, 0.7f), Cause = "farm:yardEdge" });
                }
                // heaps: the manure heap's seepage, straw round the bale stacks, hoof churn at the troughs
                int i = 0;
                foreach (var (kind, p) in _heaps)
                {
                    i++;
                    var dir = new float2(math.cos(i * 2.4f), math.sin(i * 2.4f));
                    switch (kind)
                    {
                        case "manure":
                            Brushes.At(_b, p, dir, "manureHeap", 9f, Op(0.75f, 1f), 1, cause: "farm:manure");
                            Brushes.At(_b, p + dir * 4f, dir, "strawLitter", 7f, Op(0.6f, 0.9f), 1, cause: "farm:manure");
                            break;
                        case "bales":
                            Brushes.At(_b, p, dir, "strawLitter", 9f, Op(0.7f, 1f), 2, cause: "farm:bales");
                            break;
                        case "trough":
                            Brushes.At(_b, p, dir, "hoofChurn", 7f, Op(0.6f, 0.9f), 1, cause: "farm:trough");
                            Brushes.At(_b, p + dir * 3f, dir, "dungPats", 6f, Op(0.7f, 1f), 0, cause: "farm:trough");
                            break;
                        case "compost":
                            Brushes.At(_b, p, dir, "manureHeap", 8f, Op(0.6f, 0.9f), 2, cause: "farm:compost");
                            break;
                    }
                }
            }

            // ───────────── tracks ─────────────

            private void Tracks()
            {
                // the farm's tracks are packed earth with two wheel lines: trailDirt over the path, mud ruts, the odd puddle
                foreach (var t in _tracks)
                {
                    Brushes.EdgeStrip(_b, t, "trailDirt", new StripOptions { Side = 1, Offset = 0f, Gaps = 0f, Run = new float2(60f, 200f), Opacity = Op(0.8f, 1f), CornerDeg = 70f, Cause = "farm:track" });
                    Twin(t, "wheelRut", 0.85f, Op(0.35f, 0.6f), "farm:trackRuts", gaps: 0.35f);
                }
                foreach (var t in _tracks)
                    for (int i = 0; i + 1 < t.Length; i++)
                    {
                        float len = math.distance(t[i], t[i + 1]);
                        if (len < 12f) continue;
                        uint h = H(30, i * 31 + t.Length);
                        if (Hashing.Unit(h) > 0.35f * (0.5f + _mud)) continue;
                        var p = math.lerp(t[i], t[i + 1], 0.2f + 0.6f * Hashing.Unit(Hashing.Mix(h, 1u)));
                        Brushes.At(_b, p, math.normalizesafe(t[i + 1] - t[i]), "puddlePiece", 3.5f, Op(0.6f, 0.9f), 1, cause: "farm:trackPuddle");
                    }
            }

            // ───────────── gates ─────────────

            private void Gates()
            {
                int i = 0;
                foreach (var (kind, p) in _gates)
                {
                    i++;
                    var dir = new float2(math.cos(i * 1.7f), math.sin(i * 1.7f));
                    switch (kind)
                    {
                        case "field":
                            Brushes.At(_b, p, dir, "mudPatch", 7f, Op(0.5f, 0.85f), 1, cause: "farm:fieldGate");
                            Brushes.At(_b, p, dir, "mudTyreTrack", 6f, Op(0.5f, 0.8f), 1, cause: "farm:fieldGate");
                            break;
                        case "paddock":
                            Brushes.At(_b, p, dir, "hoofChurn", 8f, Op(0.6f, 0.95f), 2, cause: "farm:paddockGate");
                            Brushes.At(_b, p + dir * 4f, dir, "dungPats", 7f, Op(0.7f, 1f), 1, cause: "farm:paddockGate");
                            break;
                        case "road":
                            // out onto the road: the tractor's mud fades over 20 m, soil spilt at the mouth
                            Brushes.At(_b, p, dir, "soilSpill", 6f, Op(0.5f, 0.8f), 1, cause: "farm:roadMud");
                            Brushes.At(_b, p, dir, "mudPatch", 5f, Op(0.4f, 0.7f), 0, cause: "farm:roadMud");
                            break;
                    }
                }
                // tyre mud onto the street: from each road gate, outward (away from the site's middle), fading
                var mid = Poly.Centroid(_sc.Outline);
                int k = 0;
                foreach (var (kind, p) in _gates)
                {
                    if (kind != "road") continue;
                    var outward = math.normalizesafe(p - mid, new float2(1, 0));
                    var side = new float2(-outward.y, outward.x);
                    float turn = Hashing.Unit(H(40, k++)) < 0.5f ? 1f : -1f;
                    var line = new List<float2>();
                    for (int s = -4; s <= 18; s += 2)
                    {
                        float u = math.max(0, s) / 18f;
                        line.Add(p + outward * math.min(s, 9f) + side * turn * math.max(0f, s - 7f) * 0.9f * u);
                    }
                    Twin(line.ToArray(), "mudTyreTrack", 1.05f, Op(0.35f, 0.75f), "farm:roadTyres");
                }
            }

            // ───────────── fields ─────────────

            private void Fields()
            {
                foreach (var f in _fields)
                {
                    bool crop = f.Kind == "Grain" || f.Kind == "Veg" || f.Kind == "Fallow";
                    var perp = new float2(-f.Rows.y, f.Rows.x);
                    var c = Poly.Centroid(f.Ring);
                    // the headland's turning line: one wheel line round the field 3 m in
                    if (crop || f.Kind == "Hay")
                    {
                        var inset = LayerRegion.From(f.Ring)?.InsetVertices(2.6f);
                        if (inset != null && inset.Length >= 3 && Poly.IsSimple(inset))
                            Brushes.EdgeStrip(_b, Closed(Poly.EnsureCcw(inset)), "wheelRut", new StripOptions { Side = 1, Gaps = 0.3f, Opacity = Op(0.35f, 0.6f), Level = ColourLevel.Quiet, Cause = "farm:headland" });
                    }
                    // tramlines every 24 m across a growing crop (not on ploughed fallow)
                    if (f.Kind == "Grain" && _season != 3 || f.Kind == "Veg" && _season == 1)
                    {
                        float lo = float.MaxValue, hi = float.MinValue;
                        foreach (var q in f.Ring) { float t = math.dot(q - c, perp); lo = math.min(lo, t); hi = math.max(hi, t); }
                        float reach = 2000f;
                        for (float t = lo + 12f; t < hi - 6f; t += 24f)
                            foreach (var (a, b) in Poly.ClipSegmentToPolygon(f.Ring, c + perp * t - f.Rows * reach, c + perp * t + f.Rows * reach))
                            {
                                float len = math.distance(a, b);
                                if (len < 20f) continue;
                                var d = (b - a) / len;
                                Brushes.EdgeStrip(_b, new[] { a + d * 4f, b - d * 4f }, "tramline", new StripOptions { Side = 1, Gaps = 0f, Run = new float2(400f, 800f), Opacity = Op(0.55f, 0.8f), Cause = "farm:tramline" });
                            }
                    }
                    // after the harvest: straw windrows down the stubble
                    if (f.Kind == "Grain" && _season == 2)
                        Brushes.Scatter(_b, BrushArea.Of(f.Ring), "strawLitter", 0.12f * _a, new ScatterOptions
                        {
                            Weight = p => { float t = math.dot(p - c, perp) / 9f; return math.saturate(1f - math.abs(t - math.round(t)) * 4f); },
                            Opacity = Op(0.55f, 0.85f), Clump = 2f, Loners = 0.2f, Cause = "farm:windrows",
                        });
                }
            }

            // ───────────── grazing ─────────────

            private void Grazing()
            {
                var grass = new List<float2[]>(_paddocks);
                foreach (var f in _fields) if (f.Kind == "Pasture") grass.Add(f.Ring);
                int i = 0;
                foreach (var ring in grass)
                {
                    i++;
                    var area = BrushArea.Of(ring);
                    Brushes.Scatter(_b, area, "dungPats", 0.08f * _a, new ScatterOptions { Weight = p => 0.3f + 0.7f * ValueNoise.Clumps(p, 16f, H(50, i)), Opacity = Op(0.7f, 1f), Clump = 3f, Loners = 0.5f, Cause = "farm:dung" });
                    Brushes.Scatter(_b, area, "molehills", 0.02f, new ScatterOptions { Weight = p => ValueNoise.Clumps(p, 20f, H(51, i)), Opacity = Op(0.6f, 0.9f), Clump = 2f, Cause = "farm:molehills" });
                    Brushes.Scatter(_b, area, "trampledGrass", 0.1f * _a, new ScatterOptions { Weight = p => 0.2f + 0.8f * Near(p, _gates, 25f), Opacity = Op(0.4f, 0.7f), Clump = 3f, Cause = "farm:trodden" });
                }
            }

            // ───────────── helpers ─────────────

            /// <summary>Path pieces joined end to end into whole tracks (a strip along each piece left a faded cap at every joint).</summary>
            private static void Chain(List<Weathering.PathLine> pieces, List<float2[]> into)
            {
                var left = new List<List<float2>>();
                foreach (var pl in pieces) if (pl?.P != null && pl.P.Length >= 2) left.Add(new List<float2>(pl.P));
                while (left.Count > 0)
                {
                    var cur = left[left.Count - 1];
                    left.RemoveAt(left.Count - 1);
                    for (bool grew = true; grew;)
                    {
                        grew = false;
                        for (int i = 0; i < left.Count; i++)
                        {
                            var o = left[i];
                            if (math.distancesq(cur[cur.Count - 1], o[0]) < 0.25f) { cur.AddRange(o.GetRange(1, o.Count - 1)); }
                            else if (math.distancesq(cur[cur.Count - 1], o[o.Count - 1]) < 0.25f) { o.Reverse(); cur.AddRange(o.GetRange(1, o.Count - 1)); }
                            else if (math.distancesq(cur[0], o[o.Count - 1]) < 0.25f) { o.RemoveAt(o.Count - 1); cur.InsertRange(0, o); }
                            else if (math.distancesq(cur[0], o[0]) < 0.25f) { o.Reverse(); o.RemoveAt(o.Count - 1); cur.InsertRange(0, o); }
                            else continue;
                            left.RemoveAt(i); grew = true; break;
                        }
                    }
                    into.Add(cur.ToArray());
                }
            }

            private static float Near(float2 p, List<(string kind, float2 p)> pts, float reach)
            {
                float best = 0f;
                foreach (var (_, q) in pts) best = math.max(best, 1f - math.saturate(math.distance(p, q) / reach));
                return best;
            }

            /// <summary>Two parallel wheel lines either side of a line (a tractor's track width).</summary>
            private void Twin(IReadOnlyList<float2> line, string kind, float half, float2 op, string cause, float gaps = 0.15f)
            {
                if (line == null || line.Count < 2) return;
                foreach (int s in new[] { 1, -1 })
                    Brushes.EdgeStrip(_b, line, kind, new StripOptions { Side = s, Offset = half, Gaps = gaps, Opacity = op, CornerDeg = 70f, Cause = cause });
            }

            /// <summary>A gentle curve from a to b (a tractor's line across a yard).</summary>
            private static float2[] Arc(float2 a, float2 b, uint h)
            {
                var d = b - a;
                var n = math.normalizesafe(new float2(-d.y, d.x));
                float bend = (Hashing.Unit(h) - 0.5f) * 0.5f * math.length(d);
                var pts = new float2[9];
                for (int i = 0; i < pts.Length; i++)
                {
                    float u = i / (float)(pts.Length - 1);
                    pts[i] = math.lerp(a, b, u) + n * bend * 4f * u * (1f - u);
                }
                return pts;
            }

            private static float2[] Closed(float2[] r)
            {
                var o = new float2[r.Length + 1];
                Array.Copy(r, o, r.Length);
                o[r.Length] = r[0];
                return o;
            }
        }
    }
}
