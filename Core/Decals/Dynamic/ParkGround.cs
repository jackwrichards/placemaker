// Placemaker — the parks' ground design (makeover, 2026-10-29; docs/runbooks/parks.md). GPL-3.0.
//
// Runs after the shared causes of Weathering.Scene (tone, leaves of each tree, drifts, path-edge wear, gates, shortcuts,
// benches, bins, puddles in the low points) and adds what a park has that a sports ground does not:
//   moods     each place the park gave a mood (ParkMoods: regions "mood:<id>") gets its own ground: washed gravel and a
//             damp streak down a gully's floor, moss on its shady bank, scree below a rocky knoll, wet silt in a hollow,
//             flowers in the long grass of a meadow, forest floor and twigs in a copse, a worn ring round standing stones
//   runoff    rain running off the paths and down the slopes: rills that follow the built ground downhill until it
//             flattens, into the gully and the hollow (read from the heightmap, never a pattern)
//   edges     grit and dust shed onto every path's lip (the user: "dirt on the edge of paths")
//   play      bare, scuffed ground under the play equipment and round the picnic tables
//   neglect   a neglected park (its own style, not a slider): litter, cracked and mossy paths, molehills, dead leaves
// Formal types (memorial, botanical, zen) keep a quiet hand: no runoff on their lawns, little play wear, quiet colours.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    internal static class ParkGround
    {
        /// <summary>The types that get the park ground (the Parks category's types, shown and shelved).</summary>
        public static readonly string[] Types = { "park", "botanicalGarden", "memorialPark", "adventurePlayground", "zenGarden", "picnicGrounds", "dogPark", "communityGarden", "formalGarden", "orchardPark", "cemeteryGrounds" };
        private static readonly HashSet<string> Formal = new HashSet<string>(StringComparer.Ordinal) { "botanicalGarden", "memorialPark", "zenGarden", "formalGarden", "cemeteryGrounds" };
        /// <summary>Plan fact set by a neglected park (ParkLayer).</summary>
        public const string NeglectedFact = "neglected";

        public static Dictionary<string, Action<Weathering.Scene>> Register(Dictionary<string, Action<Weathering.Scene>> d)
        {
            foreach (var t in Types) d[t] = Run;
            return d;
        }

        /// <summary>
        /// Before the shared causes run. Summer leaves under green park trees: the sports park's leaf carpets read as mustard
        /// yellow under birches in the game (run show-6-builds-1029-235043-0788); a park lays a sparse, dark litter of
        /// olive-brown leaves and twigs instead, about 40 % as much (conifers keep their needles, fruit trees their petals).
        /// </summary>
        public static void Prepare(Weathering.Scene sc)
        {
            sc.LeafScale = 0.42f;
            sc.LeafOverride = "leafLitterSummer";
        }

        public static void Run(Weathering.Scene sc)
        {
            var g = new Design(sc);
            g.Go();
        }

        private sealed class Design
        {
            private readonly Weathering.Scene _sc;
            private readonly BrushContext _b;
            private readonly bool _formal, _neglect;
            private readonly float _a;
            private readonly List<(string mood, float2[] ring)> _moods = new List<(string, float2[])>();
            private readonly List<float2[]> _floors = new List<float2[]>();
            private readonly List<(float2 p, float yaw, string cat, string id)> _things = new List<(float2, float, string, string)>();

            public Design(Weathering.Scene sc)
            {
                _sc = sc; _b = sc.Brush;
                string id = sc.Host?.GeneratorId ?? "";
                _formal = Formal.Contains(id);
                _neglect = sc.Host != null && sc.Host.Facts.ContainsKey(NeglectedFact);
                _a = math.min(2.2f, sc.GrimeAmount);
                foreach (var l in sc.Layers)
                {
                    foreach (var r in l.Regions)
                    {
                        if (r?.Tags == null || r.Ring == null || r.Ring.Length < 3) continue;
                        foreach (var t in r.Tags)
                        {
                            if (t == Layers.Park.ParkMoods.FloorTag) { _floors.Add(Poly.To2D(r.Ring)); break; }
                            if (t.StartsWith(Layers.Park.ParkMoods.RegionPrefix, StringComparison.Ordinal)) { _moods.Add((t.Substring(Layers.Park.ParkMoods.RegionPrefix.Length), Poly.EnsureCcw(Poly.To2D(r.Ring)))); break; }
                        }
                    }
                    foreach (var o in l.Objects)
                    {
                        if (o == null || o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush) continue;
                        _things.Add((o.Position.xz, o.Yaw, o.Category ?? "", o.Asset.Id ?? ""));
                    }
                }
            }

            private uint H(int a, int b = 0) => Hashing.Mix(_sc.Seed, 0x9A4B + a, b);
            private float U(int a, int b = 0) => Hashing.Unit(H(a, b));
            private float R(float lo, float hi, int a, int b = 0) => math.lerp(lo, hi, U(a, b));
            private float2 Op(float lo, float hi) => _sc.Opacity(lo, hi);
            private ColourLevel Lv => _formal ? ColourLevel.Quiet : ColourLevel.Any;

            public void Go()
            {
                if (_sc.GrimeAmount <= 0f) return;
                Moods();
                Edges();
                if (!_formal) Runoff();
                Play();
                if (_neglect) Neglect();
            }

            // ───────────── moods ─────────────

            private void Moods()
            {
                int i = 0;
                foreach (var (mood, ring) in _moods)
                {
                    i++;
                    var area = BrushArea.Of(ring);
                    float2 c = Poly.Centroid(ring);
                    float r = math.sqrt(math.abs(Poly.Area(ring)) / math.PI);
                    switch (mood)
                    {
                        case "gully": Gully(area, c, r, i); break;
                        case "outcrop":
                        {
                            // scree on the downhill side, rock breaking the turf round the stones, moss on the shady (north) side
                            var down = Downhill(c, r * 0.6f);
                            Brushes.Scatter(_b, area, "screeFan", 0.4f * _a, new ScatterOptions { Weight = p => 0.25f + math.saturate(math.dot(math.normalizesafe(p - c), down) * 0.8f + 0.2f) * math.saturate(math.distance(p, c) / r), Opacity = Op(0.55f, 0.9f), Clump = 2.5f, Loners = 0.2f, Cause = "mood:scree" });
                            Rocky(area, c, r, i);
                            Brushes.Scatter(_b, area, "mossPatch", 0.12f * _a, new ScatterOptions { Weight = p => math.saturate(0.5f + (p.y - c.y) / r), Opacity = Op(0.35f, 0.65f), Clump = 2f, Cause = "mood:moss" });
                            Brushes.Scatter(_b, area, "dryGrass", 0.3f, new ScatterOptions { Opacity = Op(0.3f, 0.55f), Clump = 3f, Level = ColourLevel.Quiet, Cause = "mood:dry" });
                            break;
                        }
                        case "hollow":
                        {
                            var core = Shrink(ring, c, 0.55f);
                            Brushes.Fill(_b, core, "wetSilt", Op(0.55f, 0.85f), ColourLevel.Any, "mood:silt");
                            Brushes.Scatter(_b, BrushArea.Of(Shrink(ring, c, 0.35f)), "puddlePiece", 0.25f * _a, new ScatterOptions { Opacity = Op(0.55f, 0.9f), Clump = 1.6f, Loners = 0.1f, Cause = "mood:puddle" });
                            Brushes.Scatter(_b, area, "dampPatch", 0.3f * _a, new ScatterOptions { Weight = p => 1f - math.saturate(math.distance(p, c) / r), Opacity = Op(0.3f, 0.55f), Clump = 2.5f, Cause = "mood:damp" });
                            Brushes.Scatter(_b, area, "cloverPatch", 0.3f, new ScatterOptions { Weight = p => math.saturate(math.distance(p, c) / r), Opacity = Op(0.45f, 0.75f), Clump = 3f, Cause = "mood:lush" });
                            Brushes.Scatter(_b, area, _sc.LeafKind("leafWet") ?? "leafWet", 0.12f * _a, new ScatterOptions { Opacity = Op(0.45f, 0.8f), Clump = 2f, Cause = "mood:wetLeaves" });
                            break;
                        }
                        case "meadow":
                            Brushes.Scatter(_b, area, "meadowFlowers", 0.65f, new ScatterOptions { Opacity = Op(0.5f, 0.85f), Clump = 4f, Spread = 0.6f, Loners = 0.15f, Level = ColourLevel.Any, Cause = "mood:flowers" });
                            Brushes.Scatter(_b, area, "dryGrass", 0.45f, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 3.5f, Level = ColourLevel.Quiet, Cause = "mood:longGrass" });
                            if (!_formal) Brushes.Scatter(_b, area, "molehills", 0.04f * _a, new ScatterOptions { Opacity = Op(0.55f, 0.9f), Clump = 2f, Cause = "mood:molehills" });
                            break;
                        case "glade":
                            Brushes.Scatter(_b, area, "woodlandFloor", 0.5f * _a, new ScatterOptions { Weight = p => 0.3f + math.saturate(math.distance(p, c) / r), Opacity = Op(0.4f, 0.7f), Clump = 3.5f, Cause = "mood:forestFloor" });
                            Brushes.Scatter(_b, area, "twigs", 0.18f * _a, new ScatterOptions { Opacity = Op(0.55f, 0.9f), Clump = 2f, Loners = 0.4f, Cause = "mood:twigs" });
                            Brushes.Scatter(_b, area, "mossPatch", 0.08f * _a, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 2f, Cause = "mood:moss" });
                            foreach (var t in _things) if (t.id.StartsWith("Log", StringComparison.Ordinal) && area.Contains(t.p))
                                    Brushes.At(_b, t.p, new float2(math.sin(t.yaw), math.cos(t.yaw)), "woodlandFloor", 8f, Op(0.45f, 0.7f), 1, ColourLevel.Any, "mood:logFloor");
                            break;
                        case "stones":
                        {
                            // feet walk round the ring
                            var ringLine = new List<float2>();
                            for (int k = 0; k <= 24; k++) { float a = k / 24f * 6.283f; ringLine.Add(c + new float2(math.cos(a), math.sin(a)) * r * 0.62f); }
                            Brushes.EdgeStrip(_b, ringLine, "wornPath", new StripOptions { Gaps = 0.35f, Opacity = Op(0.4f, 0.7f), Width = new float2(0.6f, 0.85f), Cause = "mood:stoneRing" });
                            Brushes.Scatter(_b, area, "dryGrass", 0.4f, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 3f, Level = ColourLevel.Quiet, Cause = "mood:longGrass" });
                            Rocky(area, c, r, i);
                            break;
                        }
                    }
                }
            }

            private void Gully(BrushArea area, float2 c, float r, int salt)
            {
                foreach (var f in _floors)
                {
                    if (!area.Contains(Poly.Centroid(f)) && !Poly.Contains(f, c)) continue;
                    var line = CentreLine(f);
                    if (line.Count < 2) continue;
                    // the floor: washed gravel down its length, a damp streak where the last water ran, leaves caught in it
                    Brushes.Scatter(_b, BrushArea.Of(f), "gullyGravel", 0.9f * _a, new ScatterOptions { Opacity = Op(0.65f, 0.95f), Clump = 3f, Spread = 0.6f, Loners = 0.1f, Scale = new float2(0.8f, 1.2f), Cause = "mood:gullyFloor" });
                    Brushes.EdgeStrip(_b, line, "runoffRill", new StripOptions { Gaps = 0.15f, Run = new float2(12f, 40f), Opacity = Op(0.6f, 0.95f), Width = new float2(1f, 1.4f), Cause = "mood:gullyRill" });
                    if (_sc.Trees.Count > 0)
                        Brushes.EdgeStrip(_b, Offset(line, R(-0.8f, 0.8f, 40, salt)), _sc.LeafKind("leafDriftStrip"), new StripOptions { Gaps = 0.55f, Run = new float2(6f, 16f), Opacity = Op(0.5f, 0.85f), Cause = "mood:gullyLeaves" });
                    // the banks: scree at their feet, moss on the shady side, rock breaking through, rills down the sides
                    Brushes.Scatter(_b, area, "screeFan", 0.25f * _a, new ScatterOptions { Weight = p => { float d = Dist(line, p); return math.saturate(1f - math.abs(d - r * 0.32f) / (r * 0.25f)); }, Opacity = Op(0.5f, 0.85f), Clump = 2.5f, Cause = "mood:gullyScree" });
                    Brushes.Scatter(_b, area, "mossPatch", 0.14f * _a, new ScatterOptions { Weight = p => math.saturate(0.4f + (p.y - c.y) / r), Opacity = Op(0.35f, 0.65f), Clump = 2.5f, Cause = "mood:moss" });
                    Rocky(area, c, r, salt);
                    int rills = 2 + (int)(U(41, salt) * 4f);
                    for (int k = 0; k < rills; k++)
                    {
                        float t = R(0.15f, 0.85f, 42 + k, salt);
                        var at = PointAlong(line, t, out var tan);
                        var nrm = new float2(-tan.y, tan.x) * (U(50 + k, salt) < 0.5f ? -1f : 1f);
                        var start = at + nrm * r * R(0.45f, 0.7f, 60 + k, salt);
                        var trace = Trace(start, 18, 1.4f, 0.01f);
                        if (trace.Count >= 4) Brushes.EdgeStrip(_b, trace, "runoffRill", new StripOptions { Gaps = 0.2f, Opacity = Op(0.45f, 0.8f), Width = new float2(0.65f, 0.95f), Cause = "mood:bankRill" });
                    }
                }
            }

            /// <summary>Rock coming through the turf round the place's boulders and stones.</summary>
            private void Rocky(BrushArea area, float2 c, float r, int salt)
            {
                int n = 0;
                foreach (var t in _things)
                {
                    if (t.cat != AssetCategories.Rock || !area.Contains(t.p) || t.id.StartsWith("Log", StringComparison.Ordinal)) continue;
                    if (!t.id.StartsWith("Boulder", StringComparison.Ordinal) && U(70 + n, salt) > 0.25f) { n++; continue; }
                    Brushes.At(_b, t.p, Downhill(t.p, 3f), U(71 + n, salt) < 0.6f ? "rockOutcrop" : "rocks", R(5f, 8f, 72 + n, salt), Op(0.45f, 0.8f), 1, ColourLevel.Any, "mood:rock");
                    n++;
                }
            }

            // ───────────── runoff: rain off the paths and down the slopes ─────────────

            private void Runoff()
            {
                int made = 0;
                int max = (int)math.clamp(Poly.Area(_sc.Outline) / 900f, 4f, 60f);
                for (int pi = 0; pi < _sc.Paths.Count && made < max; pi++)
                {
                    var l = _sc.Paths[pi];
                    for (int i = 2; i + 2 < l.P.Length && made < max; i += 9)
                    {
                        var p = l.P[i];
                        var down = Downhill(p, 2.5f, out float slope);
                        if (slope < 0.025f || U(80 + i, pi) > 0.55f) continue;
                        var tan = math.normalizesafe(l.P[i + 1] - l.P[i - 1]);
                        var nrm = new float2(-tan.y, tan.x);
                        if (math.dot(nrm, down) < 0f) nrm = -nrm;
                        if (math.abs(math.dot(nrm, down)) < 0.35f) continue;   // water runs along a path that follows the fall, not off it
                        var start = p + nrm * (l.Half + 0.6f);
                        var trace = Trace(start, 22, 1.5f, 0.012f);
                        if (trace.Count < 5) continue;
                        Brushes.EdgeStrip(_b, trace, "runoffRill", new StripOptions { Gaps = 0.25f, Run = new float2(8f, 30f), Opacity = Op(0.4f + math.saturate(slope * 4f) * 0.3f, 0.85f), Width = new float2(0.7f, 1.1f), Cause = "runoff:path" });
                        // where it stops it leaves its silt
                        Brushes.At(_b, trace[trace.Count - 1], down, "wetSilt", R(3.5f, 6f, 90 + made), Op(0.3f, 0.55f), 0, ColourLevel.Any, "runoff:fan");
                        made++;
                    }
                }
            }

            /// <summary>Downhill over the built ground from p, step by step, until it flattens, leaves the site or meets a path.</summary>
            private List<float2> Trace(float2 start, int steps, float step, float flat)
            {
                var pts = new List<float2> { start };
                var p = start;
                float2 prev = float2.zero;
                for (int k = 0; k < steps; k++)
                {
                    var d = Downhill(p, 1.5f, out float s);
                    if (s < flat) break;
                    if (math.lengthsq(prev) > 0f) d = math.normalizesafe(d * 0.7f + prev * 0.3f);
                    var q = p + d * step;
                    if (!Poly.Contains(_sc.Outline, q) || Poly.DistanceToBoundary(_sc.Outline, q) < 1.5f) break;
                    if (k > 1 && NearPath(q, 0.8f)) break;
                    pts.Add(q); p = q; prev = d;
                }
                return pts;
            }

            private bool NearPath(float2 q, float extra)
            {
                foreach (var l in _sc.Paths)
                    for (int i = 0; i + 1 < l.P.Length; i++)
                    {
                        var a = l.P[i]; var b = l.P[i + 1];
                        if (math.distancesq(q, a) > 400f && math.distancesq(q, b) > 400f) { i += 4; continue; }
                        var ab = b - a; float u = math.saturate(math.dot(q - a, ab) / math.max(1e-5f, math.lengthsq(ab)));
                        if (math.distance(q, a + ab * u) < l.Half + extra) return true;
                    }
                return false;
            }

            private float2 Downhill(float2 p, float d) => Downhill(p, d, out _);

            private float2 Downhill(float2 p, float d, out float slope)
            {
                float hx = _sc.GroundAt(p + new float2(d, 0)) - _sc.GroundAt(p - new float2(d, 0));
                float hz = _sc.GroundAt(p + new float2(0, d)) - _sc.GroundAt(p - new float2(0, d));
                var g = new float2(hx, hz) / (2f * d);
                slope = math.length(g);
                return slope > 1e-5f ? -g / slope : new float2(0f, -1f);
            }

            // ───────────── path edges ─────────────

            private void Edges()
            {
                for (int pi = 0; pi < _sc.Paths.Count; pi++)
                {
                    var l = _sc.Paths[pi];
                    if (l.P.Length < 3) continue;
                    for (int side = -1; side <= 1; side += 2)
                        Brushes.EdgeStrip(_b, l.P, "pathGrit", new StripOptions { Side = side, Offset = l.Half * 0.85f, Gaps = _formal ? 0.75f : _neglect ? 0.2f : 0.5f, Run = new float2(8f, 26f), Opacity = Op(0.45f, 0.8f), Width = new float2(0.8f, 1.1f), Level = Lv, Cause = "edge:grit" });
                }
            }

            // ───────────── play and picnic ─────────────

            private void Play()
            {
                int n = 0;
                foreach (var t in _things)
                {
                    bool play = t.cat.StartsWith("play", StringComparison.Ordinal) && !t.id.StartsWith("Log", StringComparison.Ordinal);
                    bool picnic = t.cat == "picnic";
                    if (!play && !picnic) continue;
                    if (_formal && U(100 + n) > 0.4f) { n++; continue; }
                    var dir = new float2(math.sin(t.yaw), math.cos(t.yaw));
                    if (play)
                    {
                        // feet scuff the ground bare under swings and at the foot of slides; grit kicked out of the sand
                        Brushes.At(_b, t.p + dir * R(0.5f, 1.5f, 101 + n), dir, "barePatch", R(4f, 7f, 102 + n), Op(0.35f, 0.6f), U(103 + n) < 0.4f ? 1 : 0, ColourLevel.Any, "play:scuff");
                        if (U(104 + n) < 0.5f) Brushes.At(_b, t.p, dir, "pebbles", R(3f, 5f, 105 + n), Op(0.4f, 0.7f), 0, ColourLevel.Quiet, "play:grit");
                    }
                    else
                    {
                        Brushes.At(_b, t.p, dir, "barePatch", R(4.5f, 7f, 106 + n), Op(0.3f, 0.5f), 1, ColourLevel.Any, "play:picnic");
                        if (U(107 + n) < (_neglect ? 0.9f : 0.35f)) Brushes.At(_b, t.p + dir * 1.2f, dir, "litterMix", R(2.5f, 3.5f, 108 + n), Op(0.5f, 0.85f), 0, ColourLevel.Any, "play:picnicLitter");
                    }
                    n++;
                }
                foreach (var s in _sc.Sand)
                    Brushes.EdgeStrip(_b, Closed(s), "pathGrit", new StripOptions { Side = 1, Offset = -0.6f, Gaps = 0.4f, Closed = true, Opacity = Op(0.4f, 0.75f), Cause = "play:sandEdge" });
            }

            // ───────────── neglect ─────────────

            private void Neglect()
            {
                Brushes.Scatter(_b, _sc.Lawn, "litterMix", 0.025f * _a, new ScatterOptions { Opacity = Op(0.55f, 0.9f), Clump = 2f, Loners = 0.5f, Cause = "neglect:litter" });
                Brushes.Scatter(_b, _sc.Lawn, "molehills", 0.03f * _a, new ScatterOptions { Opacity = Op(0.55f, 0.9f), Clump = 2.5f, Cause = "neglect:molehills" });
                Brushes.Scatter(_b, _sc.Lawn, _sc.LeafKind("leafDry") ?? "leafDry", 0.08f * _a, new ScatterOptions { Opacity = Op(0.45f, 0.8f), Clump = 3f, Cause = "neglect:deadLeaves" });
                Brushes.Scatter(_b, _sc.Lawn, "dryGrass", 0.18f, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 3f, Cause = "neglect:dry" });
                for (int pi = 0; pi < _sc.Paths.Count; pi++)
                {
                    var l = _sc.Paths[pi];
                    if (l.P.Length < 4) continue;
                    Brushes.CracksAlong(_b, l.P, "crackConcrete", new StripOptions { Gaps = 0.45f, Run = new float2(6f, 18f), Opacity = Op(0.5f, 0.85f), Width = new float2(0.5f, 0.7f), Cause = "neglect:cracks" });
                    for (int side = -1; side <= 1; side += 2)
                        Brushes.EdgeStrip(_b, l.P, "kerbGrimeStrip", new StripOptions { Side = -side, Offset = -l.Half * 0.6f, Gaps = 0.5f, Run = new float2(6f, 20f), Opacity = Op(0.4f, 0.75f), Width = new float2(0.7f, 1f), Cause = "neglect:pathMoss" });
                }
            }

            // ───────────── geometry ─────────────

            /// <summary>A channel ring (ParkMoods.Channel: one bank forward, the other back) → its centreline.</summary>
            private static List<float2> CentreLine(float2[] ring)
            {
                int n = ring.Length / 2;
                var line = new List<float2>(n);
                for (int i = 0; i < n; i++) line.Add((ring[i] + ring[ring.Length - 1 - i]) * 0.5f);
                return line;
            }

            private static List<float2> Offset(List<float2> line, float d)
            {
                var o = new List<float2>(line.Count);
                for (int i = 0; i < line.Count; i++)
                {
                    var t = math.normalizesafe(line[math.min(line.Count - 1, i + 1)] - line[math.max(0, i - 1)]);
                    o.Add(line[i] + new float2(-t.y, t.x) * d);
                }
                return o;
            }

            private static float Dist(List<float2> line, float2 p)
            {
                float best = float.MaxValue;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var a = line[i]; var ab = line[i + 1] - a;
                    float u = math.saturate(math.dot(p - a, ab) / math.max(1e-5f, math.lengthsq(ab)));
                    best = math.min(best, math.distance(p, a + ab * u));
                }
                return best;
            }

            private static float2 PointAlong(List<float2> line, float t, out float2 tan)
            {
                float total = 0f; for (int i = 0; i + 1 < line.Count; i++) total += math.distance(line[i], line[i + 1]);
                float a = t * total;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    float l = math.distance(line[i], line[i + 1]);
                    if (a <= l || i + 2 == line.Count) { tan = math.normalizesafe(line[i + 1] - line[i]); return math.lerp(line[i], line[i + 1], math.saturate(a / math.max(1e-4f, l))); }
                    a -= l;
                }
                tan = new float2(1, 0); return line[0];
            }

            private static float2[] Shrink(float2[] ring, float2 c, float k)
            {
                var o = new float2[ring.Length];
                for (int i = 0; i < ring.Length; i++) o[i] = c + (ring[i] - c) * k;
                return o;
            }

            private static float2[] Closed(float2[] r)
            {
                var o = new float2[r.Length + 1];
                Array.Copy(r, o, r.Length); o[r.Length] = r[0];
                return o;
            }
        }
    }
}
