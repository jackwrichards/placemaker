// Placemaker — dynamic decals: the ground of a paved square (the plazas makeover, 2026-10-29, docs/runbooks/plazas.md).
// GPL-3.0.
//
// The sports park's design (Weathering.Scene) reads a park: lawns, pitches, paths. A square is the other way round: almost
// all hard ground, a few lawns and beds, trees in pits, people everywhere. This pass reads the composed plaza (plaza,
// market square, amphitheatre, transit plaza, fairground, mall park) and lays the shared library's pieces from causes:
//
//   tone      faint paving tone over the hard ground (few, big, quiet), the odd lawn tone
//   joints    the seams between paving pieces: grime, and weeds and moss where the square is not swept; soil spilled off
//             every bed and lawn onto the paving beside it; grime and moss along wall feet (the shady north side)
//   trees     leaves of each tree's species under and downwind of it (summer kinds), drifts against the walls downwind,
//             bird droppings under some crowns
//   feet      the lanes people polish from every entrance to the centre, gum fields and butts where they wait (entrances,
//             benches, kiosks, stalls), cafe stains under the tables, litter at bins, spills at stalls
//   water     puddles and damp in the real low spots of the built ground (or the low corner of a flat square), drain
//             streaks toward them
//   age       crack chains across the big slabs, concrete stains, rust at lamp and bollard feet
//   neglect   (Gritty, or the grungy square) skate scuffs along ledges and benches, weed tufts in cracks and corners,
//             litter blown into the corners, concrete stains everywhere
//
// The player's "Wear" choice (Clean / Lived-in / Gritty, DressingControls.StyleId) is the setting: it scales every cause
// but the tone. A grungy square is its own design with its own contents (PlazaDesigns.Grungy): it sets the plan's fact
// "grungy" and this pass then lays its neglect too. Deterministic from the step's seed.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public sealed class PlazaGround
    {
        /// <summary>The types whose ground this pass designs.</summary>
        public static readonly string[] Types = { "plaza", "marketSquare", "amphitheatre", "transitPlaza", "fairground", "mallPark" };

        /// <summary>A square is hard ground and people everywhere: more detail a hectare than a park (still far under the cost that shows).</summary>
        public const float BudgetFactor = 1.6f;

        public static bool Handles(string layerId) => layerId != null && Array.IndexOf(Types, layerId) >= 0;

        /// <summary>The Wear choice as an amount: a swept square, an everyday one, a neglected one.</summary>
        public static float StyleFactor(DressStyle s) => s == DressStyle.Clean ? 0.4f : s == DressStyle.Gritty ? 1.7f : 1f;

        private struct Thing { public float2 P; public float Yaw; public string Id; }
        private struct TreeAt { public float2 P; public float Crown; public string Leaves; }
        private struct Surf { public float2[] R; public float2 Lo, Hi; public bool Green; public bool Hard; }

        private readonly List<AreaPlan> _layers;
        private readonly float2[] _ring;
        private readonly SiteContext _site;
        private readonly Func<float2, float> _ground;
        private readonly uint _seed;
        private readonly float _amount;
        private readonly BrushContext _b;
        private readonly Weathering.Report _r = new Weathering.Report();
        private bool _grungy;
        private readonly int _cap;

        private readonly List<Surf> _surfs = new List<Surf>();
        private readonly List<float2[]> _green = new List<float2[]>(), _all = new List<float2[]>();
        private readonly List<TreeAt> _trees = new List<TreeAt>();
        private readonly List<Thing> _benches = new List<Thing>(), _bins = new List<Thing>(), _lamps = new List<Thing>(), _cafes = new List<Thing>(),
            _stalls = new List<Thing>(), _gates = new List<Thing>(), _skate = new List<Thing>(), _bollards = new List<Thing>();
        private readonly List<(float2 a, float2 b)> _walls = new List<(float2, float2)>();
        private float2 _centre;
        private BrushArea _hard, _site2;
        private PointGrid _surfGrid;

        public PlazaGround(List<AreaPlan> layers, float2[] ring, SiteContext site, Func<float2, float> ground, uint seed, float amount, DecalLibrary lib,
            List<DecalInstance> into, float density, int cap)
        {
            _layers = layers; _ring = Poly.EnsureCcw((float2[])ring.Clone()); _site = site; _ground = ground ?? (p => 0f); _seed = seed; _amount = amount; _cap = cap;
            _b = new BrushContext(lib, seed, into) { Budget = (into?.Count ?? 0) + (int)(cap * 1.6f) + 200, Density = density };
            Read();
        }

        private uint H(int a, int b = 0) => Hashing.Mix(_seed, a, b);
        private float U(int a, int b = 0) => Hashing.Unit(H(a, b));
        private float Range(float lo, float hi, int a, int b = 0) => math.lerp(lo, hi, U(a, b));
        /// <summary>Coverage of the causes (the Wear amount; the grungy square is always neglected).</summary>
        private float A => math.min(2.4f, _grungy ? math.max(1.5f, _amount * 1.5f) : _amount);
        private float2 Op(float lo, float hi) => new float2(math.saturate(lo * math.min(1.35f, 0.6f + 0.4f * A)), math.saturate(hi * math.min(1.25f, 0.65f + 0.35f * A)));
        private string Leaf(string kind) => kind != null && _b.Lib.Has(kind + Weathering.Season) ? kind + Weathering.Season : kind;

        // ───────────── reading the square ─────────────

        private static string LeavesOf(PlannedObject o)
        {
            string id = ((o.Asset.Id ?? "") + " " + (o.Asset.Fallbacks != null && o.Asset.Fallbacks.Length > 0 ? o.Asset.Fallbacks[0] : "") + " " + (o.Category ?? "")).ToLowerInvariant();
            if (DressingWorld.IsConifer(o)) return "pineNeedles";
            if (id.Contains("birch") || id.Contains("alder") || id.Contains("poplar")) return "leafBirch";
            if (id.Contains("oak")) return "leafOak";
            if (id.Contains("plane") || id.Contains("linden") || id.Contains("lime")) return "leafPlane";
            if (id.Contains("chestnut") || id.Contains("hickory") || id.Contains("maple")) return "leafMaple";
            if (id.Contains("apple") || id.Contains("cherry") || id.Contains("blossom")) return "blossomPetals";
            if (id.Contains("palm")) return null;
            return "leafMixed";
        }

        private void Read()
        {
            var wallPieces = new List<Thing>();
            foreach (var l in _layers)
            {
                if (l.Facts.TryGetValue("grungy", out var g) && g > 0) _grungy = true;
                foreach (var s in l.Surfaces)
                {
                    if (s?.Ring == null || s.Ring.Length < 3) continue;
                    var r2 = Poly.EnsureCcw(Poly.To2D(s.Ring));
                    if (Poly.Area(r2) < 1.5f) continue;
                    bool green = s.Role == SurfaceRole.Grass;
                    bool hard = s.Role == SurfaceRole.Paving || s.Role == SurfaceRole.Plaza || s.Role == SurfaceRole.Decorative || s.Role == SurfaceRole.Asphalt;
                    Poly.Bounds(r2, out var lo, out var hi);
                    _surfs.Add(new Surf { R = r2, Lo = lo, Hi = hi, Green = green, Hard = hard });
                    _all.Add(r2);
                    if (green) _green.Add(r2);
                }
                foreach (var o in l.Objects)
                {
                    if (o == null) continue;
                    string id = o.Asset.Id ?? "";
                    var t = new Thing { P = o.Position.xz, Yaw = o.Yaw, Id = id };
                    string cat = o.Category ?? "";
                    if (o.Role == ObjectRole.Tree) { _trees.Add(new TreeAt { P = o.Position.xz, Crown = DressingWorld.CrownOf(o, _site), Leaves = LeavesOf(o) }); continue; }
                    if (o.Role == ObjectRole.AccessPoint) { _gates.Add(t); continue; }
                    if (o.Role == ObjectRole.Bench) _benches.Add(t);
                    else if (o.Role == ObjectRole.Bin) _bins.Add(t);
                    else if (o.Role == ObjectRole.Lamp) _lamps.Add(t);
                    else if (cat == AssetCategories.CafeSeating && id.StartsWith("Table", StringComparison.Ordinal)) _cafes.Add(t);
                    else if (cat == AssetCategories.MarketStall || cat == AssetCategories.Kiosk || id.StartsWith("FoodCart", StringComparison.Ordinal) || id.StartsWith("FoodStall", StringComparison.Ordinal) || id.StartsWith("Kiosk", StringComparison.Ordinal)) _stalls.Add(t);
                    else if (cat == AssetCategories.Bollard || id.StartsWith("Bollard", StringComparison.Ordinal)) _bollards.Add(t);
                    else if (id.StartsWith("SkatePark", StringComparison.Ordinal)) _skate.Add(t);
                    else if (cat == AssetCategories.RuinsWall && id.StartsWith("EU_Wall", StringComparison.Ordinal) && o.Elevation > -0.5f) wallPieces.Add(t);
                }
            }
            // wall pieces (5, 3, 1 m; stacked courses once) joined end to end into wall lines
            var segs = new List<(float2 a, float2 b)>();
            var seenWall = new HashSet<long>();
            foreach (var w in wallPieces)
            {
                float len = w.Id.EndsWith("Long", StringComparison.Ordinal) ? 5f : w.Id.EndsWith("ExtraShort", StringComparison.Ordinal) ? 0.5f : w.Id.EndsWith("Short", StringComparison.Ordinal) ? 1f : 3f;
                if (len < 0.9f) continue;
                long key = ((long)math.round(w.P.x * 4f) << 32) ^ (uint)(int)math.round(w.P.y * 4f);
                if (!seenWall.Add(key)) continue;
                var d = new float2(math.sin(w.Yaw), math.cos(w.Yaw));
                segs.Add((w.P - d * len * 0.5f, w.P + d * len * 0.5f));
            }
            var used = new bool[segs.Count];
            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var (a0, b0) = segs[i];
                var dir = math.normalizesafe(b0 - a0);
                for (bool grew = true; grew;)
                {
                    grew = false;
                    for (int j = 0; j < segs.Count; j++)
                    {
                        if (used[j]) continue;
                        var (c, e) = segs[j];
                        if (math.abs(math.dot(math.normalizesafe(e - c), dir)) < 0.98f) continue;
                        if (math.distancesq(b0, c) < 0.16f) { b0 = e; used[j] = grew = true; }
                        else if (math.distancesq(b0, e) < 0.16f) { b0 = c; used[j] = grew = true; }
                        else if (math.distancesq(a0, e) < 0.16f) { a0 = c; used[j] = grew = true; }
                        else if (math.distancesq(a0, c) < 0.16f) { a0 = e; used[j] = grew = true; }
                    }
                }
                if (math.distance(a0, b0) >= 2.5f) _walls.Add((a0, b0));
            }
            _centre = Poly.Centroid(_ring);
            foreach (var l in _layers) foreach (var o in l.Objects) if (o != null && o.Role == ObjectRole.Centerpiece) { _centre = o.Position.xz; break; }
            if (!Poly.Contains(_ring, _centre)) _centre = _ring[0] * 0.5f + _ring[_ring.Length / 2] * 0.5f;
            // (planning time) only lawns big enough to matter are cut out of the paving tone
            _hard = BrushArea.Of(_ring, _green.FindAll(g => Poly.Area(g) > 30f));
            _site2 = BrushArea.Of(_ring);
            _surfGrid = new PointGrid(12f);
            for (int i = 0; i < _surfs.Count; i++)
            {
                var s = _surfs[i];
                for (float x = s.Lo.x; x <= s.Hi.x + 11.9f; x += 12f)
                    for (float z = s.Lo.y; z <= s.Hi.y + 11.9f; z += 12f)
                        _surfGrid.Add(new float2(math.min(x, s.Hi.x), math.min(z, s.Hi.y)), i);
            }
        }

        /// <summary>0 bare ground, 1 hard, 2 green (a lawn or bed surface), -1 outside the site.</summary>
        private int What(float2 p)
        {
            if (!Poly.Contains(_ring, p)) return -1;
            int found = 0;
            foreach (int i in _surfGrid.Near(p, 1))
            {
                var s = _surfs[i];
                if (p.x < s.Lo.x || p.y < s.Lo.y || p.x > s.Hi.x || p.y > s.Hi.y) continue;
                if (!Poly.Contains(s.R, p)) continue;
                if (s.Green) return 2;
                if (s.Hard) found = 1;
            }
            return found;
        }

        // ───────────── the run ─────────────

        public Weathering.Report Run()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int before = _b.Out.Count;
            var lap = System.Diagnostics.Stopwatch.StartNew();
            void Lap(string what) { _r.Timing[what] = lap.Elapsed.TotalMilliseconds; lap.Restart(); }
            var flaws = PickFlaws();
            Tone(); Lap("tone");
            if (_amount > 0f)
            {
                Joints(); Lap("joints");
                Trees(flaws); Lap("trees");
                Feet(flaws); Lap("feet");
                Water(flaws); Lap("water");
                Age(); Lap("age");
                if (flaws.Contains("mossyCorner")) MossyCorner();
                if (_grungy || _amount > 1.35f) { Neglect(flaws); Lap("neglect"); }
            }
            foreach (var kv in _b.ByCause) _r.ByCause[kv.Key] = kv.Value;
            _r.Flaws.AddRange(flaws);
            _r.Instances = _b.Out.Count - before;
            _r.Ms = clock.Elapsed.TotalMilliseconds;
            return _r;
        }

        private List<string> PickFlaws()
        {
            var all = new List<string> { "bigPuddle", "gumCorner", "leafChokedCorner", "mossyCorner" };
            if (_trees.Count == 0) all.Remove("leafChokedCorner");
            var pick = new List<string>();
            int first = (int)(U(901) * all.Count) % all.Count;
            pick.Add(all[first]);
            if (U(902) < 0.55f) pick.Add(all[(first + 1 + (int)(U(903) * (all.Count - 1))) % all.Count]);
            return pick;
        }

        // ───────────── tone ─────────────

        private void Tone()
        {
            // few, big, quiet: the paving's own tile already varies; the tone only breaks up its evenness from afar
            Brushes.Tone(_b, _hard, "pavingToneSquare", new float2(0.26f, 0.42f), 0.2f, ColourLevel.Quiet, "tone:paving");
            // crisp old stains over the slabs (not soft smudges): water marks, rust, the places puddles always sit
            Brushes.Scatter(_b, _hard, "concreteStain", 0.03f * math.min(1.6f, math.max(0.4f, A)), new ScatterOptions { Weight = p => 0.25f + 0.75f * ValueNoise.Clumps(p, 26f, H(3)), Opacity = new float2(0.4f, 0.75f), Clump = 2.5f, Loners = 0.4f, Level = ColourLevel.Quiet, Cause = "tone:stains" });
            foreach (var g in _green)
                if (Poly.Area(g) > 250f) Brushes.Tone(_b, BrushArea.Of(g), "lawnTonePatch", new float2(0.14f, 0.26f), 0.15f, ColourLevel.Quiet, "tone:lawn");
            // bare ground the paving leaves (a fair's field, an amphitheatre's lawn): the sports park's lawn tone, faint
            var bare = BrushArea.Of(_ring, _all);
            if (bare.Area() > 300f)
            {
                Brushes.Tone(_b, bare, "lawnTonePatch", new float2(0.16f, 0.3f), 0.2f, ColourLevel.Quiet, "tone:lawn");
                Brushes.Scatter(_b, bare, "groundMottle", 0.14f, new ScatterOptions { Weight = p => 0.35f + 0.65f * ValueNoise.Clumps(p, 30f, H(1)), Opacity = new float2(0.22f, 0.4f), Clump = 2.5f, Level = ColourLevel.Quiet, Cause = "tone:mottle" });
                Brushes.Scatter(_b, bare, "dryGrass", 0.1f, new ScatterOptions { Weight = p => math.saturate(1f - Poly.DistanceToBoundary(_ring, p) / 20f), Opacity = new float2(0.3f, 0.5f), Clump = 3f, Cause = "tone:dry" });
            }
        }

        // ───────────── joints and seams ─────────────

        private void Joints()
        {
            // every piece edge once (two pieces share their seam): long enough to read, classified by what lies either side
            var seen = new HashSet<long>();
            var seams = new List<(float2 a, float2 b)>();
            var spills = new List<(float2 a, float2 b, int side)>();
            int budget = 2400;
            foreach (var s in _surfs)
            {
                for (int i = 0; i < s.R.Length && budget > 0; i++)
                {
                    var a = s.R[i]; var b = s.R[(i + 1) % s.R.Length];
                    float len = math.distance(a, b);
                    if (len < 2f) continue;
                    var m = (a + b) * 0.5f;
                    long key = ((long)math.round(m.x * 2f) << 32) ^ (uint)(int)math.round(m.y * 2f);
                    if (!seen.Add(key)) continue;
                    budget--;
                    var d = (b - a) / len; var n = new float2(-d.y, d.x);
                    int left = What(m + n * 0.6f), right = What(m - n * 0.6f);
                    if (left < 0 || right < 0) continue;                       // the outline: done below
                    if (left == 1 && right == 1) seams.Add((a, b));
                    else if (left == 1 && right == 2) spills.Add((a, b, -1));  // green on the right
                    else if (left == 2 && right == 1) spills.Add((a, b, 1));
                }
            }
            // hard-to-hard seams: a share gets grime, a smaller share weeds (more when nobody sweeps)
            float weedShare = math.saturate(0.08f + 0.22f * (A - 0.4f));
            // a budget in metres of strip: a square of many small pieces (garden rooms) must not spend everything on its joints
            float seamMetres = _cap * 0.1f * 8f * math.min(1.3f, A) * _b.Density, spillMetres = _cap * 0.065f * 8f * _b.Density;
            Shuffle(seams, 305); Shuffle(spills, 306);
            for (int i = 0; i < seams.Count && seamMetres > 0f; i++)
            {
                float u = U(300, i);
                if (u < weedShare + 0.35f * math.min(1f, A)) seamMetres -= math.distance(seams[i].a, seams[i].b) * 0.55f;
                if (u < weedShare)
                    Brushes.EdgeStrip(_b, new[] { seams[i].a, seams[i].b }, "jointWeeds", new StripOptions { Gaps = 0.45f, Run = new float2(3f, 9f), Opacity = Op(0.5f, 0.9f), Cause = "joints:weeds" });
                else if (u < weedShare + 0.35f * math.min(1f, A))
                    Brushes.EdgeStrip(_b, new[] { seams[i].a, seams[i].b }, "pavingGrimeStrip", new StripOptions { Side = U(301, i) < 0.5f ? 1 : -1, Offset = -0.2f, Gaps = 0.45f, Run = new float2(4f, 12f), Width = new float2(0.7f, 1f), Opacity = Op(0.45f, 0.8f), Cause = "joints:grime" });
            }
            // paving against lawns and beds: soil spilled onto the paving, worn grass on the lawn's side
            for (int i = 0; i < spills.Count && spillMetres > 0f; i++)
            {
                var (a, b, side) = spills[i];
                spillMetres -= math.distance(a, b) * 0.7f;
                Brushes.EdgeStrip(_b, new[] { a, b }, "bedSpillStrip", new StripOptions { Side = side, Offset = -0.45f, Gaps = 0.3f, Run = new float2(4f, 14f), Opacity = Op(0.45f, 0.85f), Cause = "joints:spill" });
                if (U(310, i) < 0.5f)
                    Brushes.EdgeStrip(_b, new[] { a, b }, "pathEdgeWear", new StripOptions { Side = -side, Offset = -0.6f, Gaps = 0.5f, Run = new float2(4f, 12f), Opacity = Op(0.3f, 0.55f), Level = ColourLevel.Quiet, Cause = "joints:lawnEdge" });
            }
            // the outline: grime where the square meets the pavement and the street
            Brushes.EdgeStrip(_b, _ring, "pavingGrimeStrip", new StripOptions { Side = 1, Offset = -0.35f, Closed = true, Gaps = 0.5f, Opacity = Op(0.4f, 0.75f), Cause = "joints:outline" });
            // wall feet: grime both sides, moss and weeds on the shady (north, +z) side
            for (int i = 0; i < _walls.Count; i++)
            {
                var (a, b) = _walls[i];
                var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x);
                int shade = n.y >= 0f ? 1 : -1;   // the left normal points north: the shady side is the left
                if (U(320, i) < 0.25f)
                    Brushes.EdgeStrip(_b, new[] { a, b }, "pavingGrimeStrip", new StripOptions { Side = shade, Offset = 0.35f, Gaps = 0.35f, Run = new float2(2f, 6f), Opacity = Op(0.4f, 0.75f), Cause = "walls:grime" });
                if (U(321, i) < 0.15f + 0.2f * math.min(1f, A - 0.4f))
                    Brushes.EdgeStrip(_b, new[] { a + n * shade * 0.3f, b + n * shade * 0.3f }, "jointWeeds", new StripOptions { Gaps = 0.4f, Run = new float2(2f, 5f), Opacity = Op(0.5f, 0.9f), Cause = "walls:moss" });
            }
        }

        private void Shuffle<T>(List<T> l, int salt)
        {
            for (int i = l.Count - 1; i > 0; i--) { int j = (int)(U(salt, i) * (i + 1)) % (i + 1); var t = l[i]; l[i] = l[j]; l[j] = t; }
        }

        // ───────────── trees ─────────────

        private void Trees(List<string> flaws)
        {
            if (_trees.Count == 0) return;
            var by = new Dictionary<string, List<(float2, float)>>(StringComparer.Ordinal);
            foreach (var t in _trees)
            {
                if (t.Leaves == null) continue;
                if (!by.TryGetValue(t.Leaves, out var l)) by[t.Leaves] = l = new List<(float2, float)>();
                l.Add((t.P, t.Crown));
            }
            foreach (var kv in by)
            {
                // (game runner) the park's summer leaves read as yellow stains on pale paving: squares use their own darker set (needles and petals stay)
                string kind = kv.Key == "pineNeedles" || kv.Key == "blossomPetals" ? Leaf(kv.Key) : Weathering.Season == "Summer" && _b.Lib.Has("leafSquareSummer") ? "leafSquareSummer" : Leaf(kv.Key);
                // a square is swept: fewer leaves than a park, most in the lee of the crowns
                float cov = (kind == "pineNeedles" ? 0.55f : kind == "blossomPetals" ? 0.45f : 0.85f) * math.min(1.6f, math.max(0.5f, A));
                Brushes.Under(_b, kv.Value, kind, cov, Weathering.Wind, _site2, new ScatterOptions { Opacity = Op(0.7f, 1f), Scale = new float2(0.95f, 1.45f), Clump = 4f, Spread = 0.55f, Loners = 0.3f, Spill = 1f, Level = ColourLevel.Full, Cause = "leaves" });
            }
            // on the paving most leaves lie wet and dark, stuck flat (a square is swept: the dry ones are gone)
            var crownsAll = new List<(float2, float)>();
            foreach (var t in _trees) if (t.Leaves != null && t.Leaves != "pineNeedles") crownsAll.Add((t.P, t.Crown));
            if (crownsAll.Count > 0 && Weathering.Season != "Summer") Brushes.Under(_b, crownsAll, "leafWet", 0.3f * math.min(1.6f, math.max(0.5f, A)), Weathering.Wind, _hard, new ScatterOptions { Opacity = Op(0.6f, 0.95f), Clump = 3f, Loners = 0.4f, Cause = "leaves:wet" });
            // the trunk: a ring of moss and soil round the pit, droppings under some crowns
            for (int i = 0; i < _trees.Count; i++)
            {
                var t = _trees[i];
                if (What(t.P + new float2(1.6f, 0f)) == 1 && U(400, i) < 0.8f) Brushes.At(_b, t.P, Weathering.Wind, U(404, i) < 0.6f ? "mulchSquare" : "mossSquare", Range(2.8f, 3.8f, 401, i), Op(0.55f, 0.9f), 1, ColourLevel.Any, "trees:pit");
                if (U(402, i) < 0.18f * A) Brushes.At(_b, t.P + Weathering.Wind * t.Crown * 0.3f, Weathering.Wind, "birdDroppings", Range(1.6f, 2.4f, 403, i), Op(0.6f, 0.9f), 1, ColourLevel.Any, "trees:birds");
            }
            // drifts against the walls downwind of trees
            bool choked = flaws.Contains("leafChokedCorner");
            int drifts = 0;
            for (int i = 0; i < _walls.Count && drifts < 18; i++)
            {
                var (a, b) = _walls[i];
                var m = (a + b) * 0.5f;
                var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x);
                // the upwind face: leaves blowing with the wind pile on it
                var face = math.dot(n, Weathering.Wind) < 0f ? n : -n;
                bool treeUpwind = false;
                foreach (var t in _trees) { var q = m + face * 0.1f - t.P; if (math.lengthsq(q) < 24f * 24f && math.dot(math.normalizesafe(q), Weathering.Wind) > 0.3f) { treeUpwind = true; break; } }
                if (!treeUpwind || U(410, i) > 0.55f) continue;
                int side = math.dot(n, face) > 0f ? 1 : -1;
                Brushes.EdgeStrip(_b, new[] { a + face * 0.15f, b + face * 0.15f }, Leaf("leafDriftStrip"), new StripOptions { Side = -side, Offset = -0.2f, Gaps = 0.3f, Run = new float2(2f, 6f), Opacity = Op(0.55f, 0.95f), Cause = "leaves:drift" });
                drifts++;
                if (choked && drifts == 1) Brushes.At(_b, m + face * 0.8f, face, Leaf("leafPile"), 3.2f, Op(0.7f, 1f), 2, ColourLevel.Any, "flaw:leafChokedCorner");
            }
        }

        // ───────────── feet ─────────────

        private void Feet(List<string> flaws)
        {
            // the lanes polished from every entrance to the centre, on the hard ground only
            for (int i = 0; i < _gates.Count; i++)
            {
                var g = _gates[i].P;
                var to = _centre;
                float len = math.distance(g, to);
                if (len < 8f) continue;
                var dir = (to - g) / len;
                var run = new List<float2>();
                for (float t = 1f; t <= len - 4f; t += 1.5f)
                {
                    var p = g + dir * t;
                    bool ok = What(p) == 1;
                    if (ok) run.Add(p);
                    if ((!ok || t + 1.5f > len - 4f) && run.Count >= 4)
                    {
                        Brushes.EdgeStrip(_b, run.ToArray(), "pavingWearStrip", new StripOptions { Gaps = 0.15f, Run = new float2(10f, 30f), Opacity = Op(0.45f, 0.8f), Width = new float2(0.9f, 1.1f), Level = ColourLevel.Quiet, Cause = "feet:lane" });
                        run.Clear();
                    }
                    else if (!ok) run.Clear();
                }
                // where people stop at the entrance: gum, butts
                if (What(g + dir * 2.5f) == 1)
                {
                    bool hero = flaws.Contains("gumCorner") && i == 0;
                    Brushes.At(_b, g + dir * 2.5f, dir, "gumField", hero ? 7f : Range(4f, 5.5f, 500, i), Op(0.6f, 0.95f), hero ? 3 : 1, ColourLevel.Any, hero ? "flaw:gumCorner" : "feet:gate");
                    if (U(501, i) < 0.5f * A) Brushes.At(_b, g + dir * 1.5f, dir, "cigaretteButts", 2.2f, Op(0.6f, 0.95f), 1, ColourLevel.Any, "feet:butts");
                }
            }
            // benches: the busiest get gum and crumbs in front, butts at some
            for (int i = 0; i < _benches.Count && i < 40; i++)
            {
                var t = _benches[i];
                var f = new float2(math.sin(t.Yaw), math.cos(t.Yaw));
                if (What(t.P + f * 1.2f) != 1 || U(510, i) > 0.6f * math.min(1.5f, A)) continue;
                Brushes.At(_b, t.P + f * 1.2f, f, U(511, i) < 0.6f ? "gumField" : "litterMix", Range(2.8f, 3.8f, 512, i), Op(0.6f, 0.95f), 0, ColourLevel.Any, "feet:bench");
                if (U(513, i) < 0.3f * A) Brushes.At(_b, t.P + f * 0.8f, f, "cigaretteButts", 1.6f, Op(0.55f, 0.9f), 0, ColourLevel.Any, "feet:butts");
            }
            // cafe tables: stains under most, a spill here and there
            for (int i = 0; i < _cafes.Count && i < 70; i++)
            {
                var t = _cafes[i];
                if (U(520, i) > 0.7f * math.min(1.4f, A)) continue;
                Brushes.At(_b, t.P, new float2(math.sin(t.Yaw), math.cos(t.Yaw)), "cafeStains", Range(2.8f, 3.8f, 521, i), Op(0.55f, 0.9f), 0, ColourLevel.Any, "feet:cafe");
                if (U(522, i) < 0.25f * A) Brushes.At(_b, t.P + new float2(Range(-1f, 1f, 523, i), Range(-1f, 1f, 524, i)), Weathering.Wind, "drinkSpill", 1.4f, Op(0.5f, 0.85f), 0, ColourLevel.Any, "feet:spill");
            }
            // stalls and kiosks: the queue's gum, spills and scraps in front
            for (int i = 0; i < _stalls.Count && i < 60; i++)
            {
                var t = _stalls[i];
                var f = new float2(math.sin(t.Yaw), math.cos(t.Yaw));
                var q = t.P + f * 2.6f;
                if (What(q) < 0) continue;
                if (U(530, i) < 0.75f * math.min(1.3f, A)) Brushes.At(_b, q, f, "gumField", Range(3.5f, 5f, 531, i), Op(0.5f, 0.85f), 0, ColourLevel.Any, "feet:stall");
                if (U(532, i) < 0.5f * A) Brushes.At(_b, q + f * 0.5f, f, "drinkSpill", 1.5f, Op(0.5f, 0.85f), 1, ColourLevel.Any, "feet:spill");
                if (U(533, i) < 0.4f * A) Brushes.At(_b, t.P - f * 1.2f, -f, "litterMix", 2.2f, Op(0.5f, 0.85f), 0, ColourLevel.Any, "feet:stallBack");
            }
            // bins: litter round them, a spill, rust at the foot of the steel ones
            for (int i = 0; i < _bins.Count && i < 30; i++)
            {
                var t = _bins[i];
                if (U(540, i) < 0.55f * A) Brushes.At(_b, t.P, Weathering.Wind, "litterMix", Range(2f, 2.8f, 541, i), Op(0.55f, 0.9f), 1, ColourLevel.Any, "feet:bin");
                if (U(542, i) < 0.3f * A) Brushes.At(_b, t.P, Weathering.Wind, "drinkSpill", 1.2f, Op(0.5f, 0.85f), 0, ColourLevel.Any, "feet:spill");
            }
        }

        // ───────────── water ─────────────

        private void Water(List<string> flaws)
        {
            Poly.Bounds(_ring, out var lo, out var hi);
            const float step = 3f;
            int nx = math.min(90, (int)((hi.x - lo.x) / step) + 1), nz = math.min(90, (int)((hi.y - lo.y) / step) + 1);
            var g = new float[nx * nz];
            var ok = new bool[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    var p = lo + new float2(i * (hi.x - lo.x) / math.max(1, nx - 1), j * (hi.y - lo.y) / math.max(1, nz - 1));
                    ok[j * nx + i] = What(p) == 1 && Poly.DistanceToBoundary(_ring, p) > 2f;
                    // the built ground plus a few centimetres of settling (no square is perfectly flat)
                    g[j * nx + i] = _ground(p) + 0.025f * (ValueNoise.Sample(p / 9f, H(600)) - 0.5f);
                }
            var pits = new List<(float2 p, float depth)>();
            for (int j = 1; j < nz - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                {
                    if (!ok[j * nx + i]) continue;
                    float h = g[j * nx + i]; bool min = true; float peak = h;
                    for (int dj = -2; dj <= 2 && min; dj++)
                        for (int di = -2; di <= 2; di++)
                        {
                            int x = i + di, z = j + dj;
                            if (x < 0 || z < 0 || x >= nx || z >= nz || (di == 0 && dj == 0)) continue;
                            if (g[z * nx + x] < h - 1e-4f) { min = false; break; }
                            peak = math.max(peak, g[z * nx + x]);
                        }
                    if (min && peak - h > 0.004f) pits.Add((lo + new float2(i * (hi.x - lo.x) / math.max(1, nx - 1), j * (hi.y - lo.y) / math.max(1, nz - 1)), peak - h));
                }
            pits.Sort((a, b) => b.depth.CompareTo(a.depth));
            bool big = flaws.Contains("bigPuddle");
            float area = Poly.Area(_ring);
            int want = math.clamp((int)(area / 4000f * math.min(1.6f, A)) + 1, 1, 6);
            var placed = new List<float2>();
            int n = 0;
            foreach (var (p, depth) in pits)
            {
                if (n >= want) break;
                if (placed.Exists(q => math.distancesq(q, p) < 18f * 18f)) continue;
                placed.Add(p);
                bool hero = big && n == 0;
                float L = Range(3.5f, 6f, 610, n) * (hero ? 1.8f : 1f);
                var hollow = new float2[10];
                float rot = U(611, n) * math.PI;
                var ax = new float2(math.cos(rot), math.sin(rot)); var sd = new float2(-ax.y, ax.x);
                for (int i = 0; i < hollow.Length; i++)
                {
                    float t = i / (float)hollow.Length * 2f * math.PI;
                    float wob = 1f + 0.2f * (ValueNoise.Sample(new float2(math.cos(t), math.sin(t)) * 1.5f + n * 3f, H(612)) - 0.5f) * 2f;
                    hollow[i] = p + (ax * math.cos(t) * L * 0.5f + sd * math.sin(t) * L * 0.32f) * wob;
                }
                bool wet = n < 2 || hero;
                if (wet) Brushes.Fill(_b, hollow, "puddlePiece", new float2(0.6f, 0.95f), ColourLevel.Any, hero ? "flaw:bigPuddle" : "water:puddle");
                else Brushes.Fill(_b, hollow, U(613, n) < 0.5f ? "dryTidemark" : "dampPatch", new float2(0.45f, 0.75f), ColourLevel.Any, "water:dry");
                // the streak of silt the run-off leaves on its way in
                var dirIn = math.normalizesafe(p - _centre, ax);
                Brushes.At(_b, p - dirIn * L * 0.9f, dirIn, "drainStreak", L * 1.1f, Op(0.45f, 0.8f), 0, ColourLevel.Any, "water:streak");
                n++;
            }
            // damp under the densest crowns
            for (int i = 0; i < _trees.Count && i < 60; i++)
                if (U(620, i) < 0.12f * A && What(_trees[i].P) == 1)
                    Brushes.At(_b, _trees[i].P - Weathering.Wind * 1.5f, Weathering.Wind, "dampPatch", Range(4f, 6f, 621, i), Op(0.3f, 0.5f), 0, ColourLevel.Quiet, "water:shade");
        }

        // ───────────── age ─────────────

        private void Age()
        {
            // crack chains across the bigger slabs: a couple per piece, more with wear, never on lawns
            int cracks = 0, cap = (int)(Poly.Area(_ring) / 220f * math.min(1.8f, A)) + 2;
            for (int i = 0; i < _surfs.Count && cracks < cap; i++)
            {
                var s = _surfs[i];
                if (!s.Hard) continue;
                float a = Poly.Area(s.R);
                if (a < 40f) continue;
                int k = math.min(4, (int)(a / 260f * A + U(700, i)));
                for (int c = 0; c < k && cracks < cap; c++)
                {
                    var p0 = s.Lo + (s.Hi - s.Lo) * new float2(U(701, i * 8 + c), U(702, i * 8 + c));
                    if (!Poly.Contains(s.R, p0)) continue;
                    float ang = U(703, i * 8 + c) * math.PI * 2f;
                    var d = new float2(math.cos(ang), math.sin(ang));
                    float L = Range(7f, 18f, 704, i * 8 + c);
                    var line = new List<float2> { p0 };
                    var q = p0;
                    for (float t = 0f; t < L; t += 3f)
                    {
                        d = math.normalizesafe(d + new float2(-d.y, d.x) * Range(-0.25f, 0.25f, 705, (int)(t * 10f) + c * 97 + i * 13));
                        q += d * 3f;
                        if (!Poly.Contains(s.R, q) || Poly.DistanceToBoundary(s.R, q) < 0.6f) break;
                        line.Add(q);
                    }
                    if (line.Count < 3) continue;
                    Brushes.CracksAlong(_b, line, "crackPaving", new StripOptions { Opacity = Op(0.45f, 0.8f), Cause = "age:crack" });
                    cracks++;
                    // weeds take a crack that nobody sweeps
                    if (A > 1.2f && U(706, i * 8 + c) < 0.5f) Brushes.At(_b, line[line.Count / 2], d, "weedTufts", Range(1.6f, 2.4f, 707, i), Op(0.7f, 1f), 1, ColourLevel.Any, "age:weeds");
                }
            }
            // rust at the feet of some lamps and bollards
            for (int i = 0; i < _lamps.Count; i++)
                if (U(710, i) < 0.22f * A) Brushes.At(_b, _lamps[i].P + Weathering.Wind * 0.2f, Weathering.Wind, "rustBleed", Range(1.1f, 1.6f, 711, i), Op(0.5f, 0.85f), 0, ColourLevel.Any, "age:rust");
            for (int i = 0; i < _bollards.Count && i < 60; i++)
                if (U(712, i) < 0.15f * A) Brushes.At(_b, _bollards[i].P, Weathering.Wind, "rustBleed", 1f, Op(0.5f, 0.85f), 0, ColourLevel.Any, "age:rust");
            // birds on the lamp heads
            for (int i = 0; i < _lamps.Count; i++)
                if (U(714, i) < 0.12f * A) Brushes.At(_b, _lamps[i].P + new float2(0.6f, 0.3f), Weathering.Wind, "birdDroppings", 1.6f, Op(0.55f, 0.85f), 0, ColourLevel.Any, "age:birds");
        }

        private void MossyCorner()
        {
                // the corner that stays wet: moss over the paving
                var c = _ring[(int)(U(840) * _ring.Length) % _ring.Length];
                var inward = math.normalizesafe(_centre - c);
                Brushes.At(_b, c + inward * 4f, inward, "mossSquare", 6f, Op(0.5f, 0.85f), 3, ColourLevel.Any, "flaw:mossyCorner");
        }

        // ───────────── neglect ─────────────

        private void Neglect(List<string> flaws)
        {
            float k = _grungy ? 1f : math.saturate((_amount - 1.35f) / 0.5f);
            // concrete's own stains over the hard ground, crisp, in loose groups
            Brushes.Scatter(_b, _hard, "concreteStain", 0.06f * k * math.min(1.6f, A), new ScatterOptions { Weight = p => 0.3f + 0.7f * ValueNoise.Clumps(p, 22f, H(800)), Opacity = Op(0.45f, 0.85f), Clump = 3f, Loners = 0.35f, Cause = "neglect:stains" });
            // skate scuffs along every ledge, wall and bench the skaters ride, and round the skate pieces
            for (int i = 0; i < _walls.Count; i++)
                if (U(810, i) < 0.4f * k)
                {
                    var (a, b) = _walls[i];
                    var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x) * (U(811, i) < 0.5f ? 1f : -1f);
                    Brushes.At(_b, (a + b) * 0.5f + n * 1.1f, d, "skateScuffs", Range(4f, 6.5f, 812, i), Op(0.55f, 0.9f), 1, ColourLevel.Any, "neglect:skate");
                }
            for (int i = 0; i < _benches.Count && i < 30; i++)
                if (U(815, i) < 0.3f * k)
                {
                    var t = _benches[i];
                    var d = new float2(math.cos(t.Yaw), -math.sin(t.Yaw));
                    Brushes.At(_b, t.P + new float2(math.sin(t.Yaw), math.cos(t.Yaw)) * 0.9f, d, "skateScuffs", 4f, Op(0.5f, 0.85f), 0, ColourLevel.Any, "neglect:skate");
                }
            for (int i = 0; i < _skate.Count; i++)
            {
                var t = _skate[i];
                var d = new float2(math.sin(t.Yaw), math.cos(t.Yaw));
                Brushes.At(_b, t.P + d * Range(1.5f, 3f, 820, i), d, "skateScuffs", Range(5f, 7f, 821, i), Op(0.6f, 0.95f), 2, ColourLevel.Any, "neglect:skate");
            }
            // weeds where the square meets its edges and in the corners nobody walks
            Brushes.Scatter(_b, _hard, "weedTufts", 0.05f * k * math.min(1.6f, A), new ScatterOptions { Weight = p => math.saturate(1f - Poly.DistanceToBoundary(_ring, p) / 6f) * (0.4f + 0.6f * ValueNoise.Clumps(p, 14f, H(830))), Opacity = Op(0.7f, 1f), Clump = 3f, Loners = 0.4f, Cause = "neglect:weeds" });
            Brushes.EdgeStrip(_b, _ring, "jointWeeds", new StripOptions { Side = 1, Offset = -0.25f, Closed = true, Gaps = 0.55f, Run = new float2(3f, 10f), Opacity = Op(0.55f, 0.95f), Cause = "neglect:edgeWeeds" });
            // litter blown into the corners and along the downwind edge
            Brushes.Scatter(_b, _hard, "litterMix", 0.04f * k * math.min(1.6f, A), new ScatterOptions { Weight = p => math.saturate(1f - Poly.DistanceToBoundary(_ring, p) / 8f), Opacity = Op(0.55f, 0.9f), Clump = 2f, Loners = 0.5f, Cause = "neglect:litter" });
        }
    }
}
