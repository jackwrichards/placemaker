// Placemaker — ground paint dressing: wear, mulch, lawn variation painted into the terrain texture (s28, s33). GPL-3.0.
//
// User (s28): use the terrain "Paint texture" materials as ground dressing; (s33) "terrain paint is what gives us
// power … draw dark mulch under each tree". Built on the Core/Paint toolkit (PaintShape fields, one PlannedPaint per
// field), always masked by where bare terrain shows (GroundMask: not under any planned surface, net, paving, sand,
// water or cutout), so nothing is painted where it would be invisible:
//   Lawn       (painted lawns only) lush = forest-floor tint in hollows, near water and in noise patches; dry grass on
//              knolls and in other patches — the procedural grass in between stays the lawn
//   Mulch      dark forest floor under every tree crown (radial, noisy edge; needle beds stronger under conifers)
//   Verges     faint dirt feathered 2 m out from every path edge
//   Desire     faint dirt lines where people cut corners and walk across the lawn to benches / play items (knockout)
//   Patches    worn dirt at play items, picnic tables, bench fronts, entrances, with a dry-grass halo
//   Edges      dry-grass (gritty: some dirt) speckle in a soft band inside the outline: the build blends into the land
//   Extras     pitch wear at goals and fitness stations, campground pitch and fire-ring wear, dune grass along sand,
//              headlands round crop fields
// Vanilla materials (docs/TERRAIN-PAINT.md): 1 dry grass, 2 "lush" = dark forest floor, 3 light dirt, 4 grey sand.
// Amount: "Ground paint" (dressPaintWear) × style (Clean 0.6, Lived-in 1, Gritty 1.4); toggles Desire paths, Mulch,
// Lawn variation, Clear old paint. Paint is permanent (TerrainPaint.PermanentKey warning; the permanent-changes summary
// lists it). Deterministic: every decision is a hash of the step seed and the host's position.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Paint;
using Unity.Mathematics;

namespace Placemaker.Core.Decals
{
    public static class GroundWear
    {
        /// <summary>Paints per fill step at most (keeps the raster and the permanent area small).</summary>
        public const int MaxPaints = 360;
        public const string StatKey = "Placemaker.Stat.GroundWear";
        /// <summary>Trees that get mulch per step at most (one union paint; beyond this the crowns overlap into forest floor anyway).</summary>
        public const int MaxMulchTrees = 2500;

        public sealed class Report
        {
            public int Verges, Desire, Patches, Edges, Skipped;
            /// <summary>(s33) Lawn tint fields, mulch fields (trees covered), type extras, erase fields.</summary>
            public int Lawn, Mulch, MulchTrees, Extras, Clears;
            public int Total => Verges + Desire + Patches + Edges + Lawn + Mulch + Extras + Clears;
        }

        /// <summary>The paint seed of a step from its host's seed (the decals of a designed ground read the same fields).</summary>
        public static uint PaintSeed(uint hostSeed) => Hashing.Mix(hostSeed == 0 ? 1u : hostSeed, 0x6A0Bu);

        /// <summary>
        /// The lawn's moisture before drainage (0 dry .. 1 lush, big soft clumps): the painted lush and dry grass follow it, and
        /// so do a designed ground's dry-grass and clover decals (Dynamic/Weathering), so paint and decals agree.
        /// </summary>
        public static float LawnMoisture(uint paintSeed, float2 p) =>
            0.65f * ValueNoise.Clumps(p, 26f, Hashing.Mix(paintSeed, 0x1A3Bu)) + 0.35f * ValueNoise.Clumps(p, 9f, Hashing.Mix(paintSeed, 0x2C4Du));

        internal static float StyleK(DressStyle s) => s == DressStyle.Clean ? 0.6f : s == DressStyle.Gritty ? 1.4f : 1f;

        internal static Report Apply(ComposedPlan plan, DressingWorld w, SiteContext site, IReadOnlyDictionary<AreaPlan, DressSettings> dress)
        {
            var report = new Report();
            if (plan?.Layers == null || w?.Root == null || dress == null) return report;
            // Group the layers by the fill step's settings (features share their parent's instance).
            var groups = new Dictionary<DressSettings, List<AreaPlan>>();
            var order = new List<DressSettings>();
            foreach (var l in plan.Layers)
            {
                if (l == null || l.GeneratorId == Cutouts.ZoningLayer || !dress.TryGetValue(l, out var s) || s == null || s.PaintWear <= 0f) continue;
                if (!groups.TryGetValue(s, out var list)) { groups[s] = list = new List<AreaPlan>(); order.Add(s); }
                list.Add(l);
            }
            foreach (var s in order)
            {
                var layers = groups[s];
                AreaPlan host = layers.Find(l => l.GeneratorId == s.LayerId) ?? layers[0];
                var region = (s.Ring != null && s.Ring.Length >= 3 ? LayerRegion.From(s.Ring) : null) ?? w.Root;
                var g = new Group(w, site, host, region, s, PaintSeed(host.Seed), report);
                g.Clear();
                if (s.LawnPainted && s.LawnTint) g.LawnTint();
                if (s.Weathering)
                {
                    // (dynamic decals) The step is weathered by code-made decals grown from causes (Dynamic/Weathering): the paint
                    // keeps only the soft macro tone. Left out, as "organized dirt": a mulch disc per tree, bullseye patches per
                    // bench and play item, verge bands the full length of every path, edge discs at a fixed step, goalmouth ovals.
                    // The lived-in mulch stays: a soft, ragged forest floor in the shadow of the crowns, the base the leaves lie on.
                    if (s.Mulch && s.Style != DressStyle.Clean) g.Mulch();
                }
                else
                {
                    if (s.Mulch) g.Mulch();
                    g.Verges(layers);
                    if (s.DesirePaths) g.Desire(layers);
                    g.Patches(layers);
                    g.Edges();
                    g.Extras(layers);
                }
                if (g.Count > 0)
                {
                    bool warned = false;
                    foreach (var i in host.Issues) if (i.Key == TerrainPaint.PermanentKey) { warned = true; break; }
                    if (!warned) host.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = TerrainPaint.PermanentKey });
                }
            }
            return report;
        }

        private sealed class Group
        {
            private readonly DressingWorld W;
            private readonly SiteContext Site;
            private readonly AreaPlan Host;
            private readonly LayerRegion Region;
            private readonly DressSettings S;
            private readonly uint Seed;
            private readonly Report R;
            private readonly float K;
            private readonly PaintKit Kit;
            private GroundMask _mask;

            public int Count => Kit.Count;

            public Group(DressingWorld w, SiteContext site, AreaPlan host, LayerRegion region, DressSettings s, uint seed, Report r)
            {
                W = w; Site = site; Host = host; Region = region; S = s; Seed = seed; R = r;
                // In game 100 % read as barely there (user, 2026-10-14): the slider now scales a 3x base strength, and the
                // per-paint caps open up with it (still capped at full strength).
                K = 3f * math.min(3f, s.PaintWear) * StyleK(s.Style);
                Kit = new PaintKit(host.Paint, HeightAt, seed) { MaxPaints = MaxPaints, Amount = 1f, Outline = region.Ring };
            }

            /// <summary>Where bare terrain shows (built on first use: types with nothing to paint never pay for it).</summary>
            private GroundMask Mask => _mask ?? (_mask = GroundMask.Build(W, Region, S.LawnPainted ? 4f : 1.5f, Hashing.Mix(Seed, 0x3A5Bu)));

            private uint H(int a, int b = 0, int c = 0) => Hashing.Mix(Hashing.Mix(Seed, a, b), unchecked((uint)c * 0x9E3779B1u + 11u));
            private static uint H2(uint h, int k) => Hashing.Mix(h, unchecked((uint)k + 0x632BE5ABu));
            private static float U(uint h) => Hashing.Unit(h);

            /// <summary>Bare terrain shows here (mask ≥ 0.5) inside the step.</summary>
            public bool Visible(float2 p) => Mask.At(p) >= 0.5f;

            private float HeightAt(float2 p)
            {
                try
                {
                    if (Host.HeightAfter != null) return Host.HeightAfter(p);
                    return Site?.HeightAt != null ? Site.HeightAt(p) : 0f;
                }
                catch (Exception) { return 0f; }
            }

            /// <summary>Emits a masked field at strength × K (capped); null when it would paint nothing.</summary>
            private PlannedPaint Paint(PaintShape shape, int layer, float strength, float cap = 1f, bool knockout = false, IReadOnlyList<float2> outline = null)
            {
                Kit.Mask = Mask.Shape;
                Kit.MaxStrength = math.min(1f, cap * math.max(1f, K * 0.5f));
                return Kit.Shape(shape, layer, strength * K, outline, knockout);
            }

            private static bool IsPath(NetRole r) => r == NetRole.Pathway || r == NetRole.PathwayWide;

            private static float2[] Polyline(PlannedNet n, int steps = 8)
            {
                var pts = new float2[steps + 1];
                for (int i = 0; i <= steps; i++) pts[i] = Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)steps).xz;
                return pts;
            }

            // ───────────────────────── clear ─────────────────────────

            /// <summary>"Clear old paint": every material back to the procedural base inside the outline (before anything is painted).</summary>
            public void Clear()
            {
                if (!S.ClearFirst) return;
                Kit.Mask = null;
                if (Kit.Erase(Region.Ring, TerrainMaterials.Erase, 1f, 2f) != null) R.Clears++;
            }

            // ───────────────────────── lawn ─────────────────────────

            /// <summary>
            /// Lawn variation on a painted lawn: a moisture field (two octaves of noise, hollows and water make it wetter,
            /// knolls drier) → lush (forest-floor tint) where wet, dry grass where dry, the procedural grass between.
            /// </summary>
            public void LawnTint()
            {
                var m = Mask;
                if (m.OpenShare < 0.05f) return;
                float cell = math.max(3.5f, m.Cell * 2f);
                var size = Region.Max - Region.Min + 2f;
                int gw = math.max(1, (int)math.ceil(size.x / cell)), gh = math.max(1, (int)math.ceil(size.y / cell));
                var origin = Region.Min - 1f;
                var lush = new float[gw * gh];
                var dry = new float[gw * gh];
                // Flat sites skip the drainage term (it costs 7 height samples per cell).
                bool flat = true;
                float h0 = HeightAt((Region.Min + Region.Max) * 0.5f);
                for (int y = 0; y < gh && flat; y += 2)
                    for (int x = 0; x < gw && flat; x += 2)
                        if (math.abs(HeightAt(origin + (new float2(x, y) + 0.5f) * cell) - h0) > 0.6f) flat = false;
                bool water = Site?.WaterDepthAt != null;
                // (P2-8) a plain biome (plain grass, the field) keeps the game's own grass: no lush or dry patches on it
                var plain = Placemaker.Core.Decals.Dynamic.BiomeGround.PlainAt(new[] { Host });
                for (int y = 0; y < gh; y++)
                    for (int x = 0; x < gw; x++)
                    {
                        var p = origin + (new float2(x, y) + 0.5f) * cell;
                        if (m.At(p) <= 0f) continue;
                        if (plain != null && plain(p)) continue;
                        float moist = LawnMoisture(Seed, p);
                        if (!flat)
                        {
                            // Drainage: below the local mean = wetter, above = drier (±1 m ≈ ±0.18).
                            float h = HeightAt(p), mean = 0f;
                            for (int k = 0; k < 6; k++) { math.sincos(k * math.PI / 3f, out float sn, out float cs); mean += HeightAt(p + new float2(cs, sn) * 12f); }
                            moist += math.clamp((mean / 6f - h) * 0.18f, -0.25f, 0.25f);
                        }
                        // Water nearby keeps the grass lush.
                        if (water)
                            for (int k = 0; k < 4; k++)
                            {
                                math.sincos(k * math.PI / 2f + 0.4f, out float sn, out float cs);
                                if (W.IsWater(p + new float2(cs, sn) * 7f)) { moist += 0.12f; break; }
                            }
                        lush[y * gw + x] = PaintShape.Curve(Falloff.Smooth, (0.62f - moist) / 0.12f);
                        dry[y * gw + x] = PaintShape.Curve(Falloff.Smooth, (moist - 0.40f) / 0.12f);
                    }
                var outline = Region.Ring;
                if (Paint(PaintShape.Grid(origin, cell, gw, gh, lush), TerrainMaterials.Lush, 0.42f, 0.55f, false, outline) != null) R.Lawn++;
                if (Paint(PaintShape.Grid(origin, cell, gw, gh, dry), TerrainMaterials.Dry, 0.5f, 0.65f, false, outline) != null) R.Lawn++;
            }

            // ───────────────────────── mulch ─────────────────────────

            /// <summary>Dark forest-floor mulch under every tree crown on bare terrain: one union field (broadleaf) + one (conifers).</summary>
            public void Mulch()
            {
                var broad = new List<PaintShape>(); var needles = new List<PaintShape>();
                bool clean = S.Style == DressStyle.Clean;
                for (int i = 0; i < W.Trees.Count && broad.Count + needles.Count < MaxMulchTrees; i++)
                {
                    var t = W.Trees[i];
                    if (!Region.Contains(t.P) || !Visible(t.P)) continue;
                    uint h = H((int)math.floor(t.P.x * 2f), (int)math.floor(t.P.y * 2f), 41);
                    float crown = t.Crown * (0.9f + 0.2f * U(h));
                    // Clean: a crisp round mulch bed close to the trunk; lived-in: a soft, irregular shadow of the crown.
                    var shape = clean
                        ? PaintShape.Radial(t.P, crown * 0.42f, crown * 0.62f, Falloff.Hold, 0.15f, 2f, h)
                        : PaintShape.Radial(t.P, crown * 0.35f, crown * 1.1f, Falloff.Smooth, 0.35f + crown * 0.08f, math.max(2f, crown), h);
                    (t.Conifer ? needles : broad).Add(shape);
                }
                if (broad.Count > 0 && Paint(PaintShape.Union(broad), TerrainMaterials.Lush, clean ? 0.85f : 0.78f, 0.9f) != null) { R.Mulch++; R.MulchTrees += broad.Count; }
                if (needles.Count > 0 && Paint(PaintShape.Union(needles), TerrainMaterials.Lush, 0.78f, 0.95f) != null) { R.Mulch++; R.MulchTrees += needles.Count; }
                // Shrub beds: a mulched bed under every bush (neat in a clean style, ragged otherwise).
                var beds = new List<PaintShape>();
                foreach (var l in W.Plans)
                    foreach (var o in l.Objects)
                    {
                        if (o == null || o.Role != ObjectRole.Bush || beds.Count >= MaxMulchTrees) continue;
                        var p = o.Position.xz;
                        if (!Region.Contains(p) || !Visible(p)) continue;
                        uint h = H((int)math.floor(p.x * 2f), (int)math.floor(p.y * 2f), 43);
                        float r = 1.9f * (o.Scale > 0.05f ? o.Scale : 1f) * (0.85f + 0.3f * U(h));
                        beds.Add(clean ? PaintShape.Radial(p, r * 0.6f, r, Falloff.Hold, 0.1f, 2f, h) : PaintShape.Radial(p, r * 0.3f, r * 1.2f, Falloff.Smooth, 0.4f, 2.5f, h));
                    }
                if (beds.Count > 0 && Paint(PaintShape.Union(beds), TerrainMaterials.Lush, clean ? 0.8f : 0.6f, 0.85f) != null) R.Mulch++;
            }

            // ───────────────────────── verges ─────────────────────────

            public void Verges(List<AreaPlan> layers)
            {
                int ni = 0;
                var strokes = new List<PaintShape>();
                foreach (var l in layers)
                    foreach (var n in l.Nets)
                    {
                        ni++;
                        if (n == null || !IsPath(n.Role)) continue;
                        float hw = DressingWorld.HalfWidth(n, Site);
                        var line = Polyline(n);
                        // Only where the verge is bare terrain on at least half its length.
                        int seen = 0, ok = 0;
                        for (int i = 0; i < line.Length; i++)
                        {
                            var a = line[math.max(0, i - 1)]; var b = line[math.min(line.Length - 1, i + 1)];
                            var t = math.normalizesafe(b - a, new float2(1, 0)); var nrm = new float2(-t.y, t.x);
                            for (int side = -1; side <= 1; side += 2) { seen++; if (Visible(line[i] + nrm * side * (hw + 1.2f))) ok++; }
                        }
                        if (ok * 2 < seen) { R.Skipped++; continue; }
                        float len = 0f; for (int i = 1; i < line.Length; i++) len += math.distance(line[i - 1], line[i]);
                        if (len < 3f) continue;
                        uint h = H(ni, 1);
                        // Full at the path edge (the path itself is masked out), fading ~2 m into the grass; the width breathes.
                        strokes.Add(PaintShape.Stroke(line, 2f * (hw + 2.2f), 2.1f, 0.8f + 0.2f * U(h), 0.8f + 0.2f * U(H2(h, 2)), Falloff.Linear, 0f, 0.18f, 6f, h));
                        R.Verges++;
                    }
                // One field for every verge of the step (faint: the band runs the whole path).
                if (strokes.Count > 0) Paint(PaintShape.Union(strokes), TerrainMaterials.Dirt, 0.26f, 0.45f);
            }

            // ───────────────────────── desire paths ─────────────────────────

            private bool LineClear(float2 a, float2 b, float endSlack)
            {
                float len = math.distance(a, b);
                if (len < 3f) return false;
                int steps = (int)math.ceil(len / 1.5f);
                for (int i = 0; i <= steps; i++)
                {
                    var p = math.lerp(a, b, i / (float)steps);
                    float d = math.min(i, steps - i) * len / steps;
                    if (d < endSlack) continue;
                    if (!Visible(p)) return false;
                    if (W.NetClearance(p, 1f, out _) < 0.6f) return false;
                    if (W.NearestTree(p, 1.2f, out _) < 1.0f) return false;
                }
                return true;
            }

            private void DesireLine(float2 a, float2 b, uint h)
            {
                // A slight bow: nobody walks a ruler line.
                var mid = math.lerp(a, b, 0.5f) + new float2(-(b - a).y, (b - a).x) * (U(H2(h, 3)) - 0.5f) * 0.12f;
                var line = new[] { a, mid, b };
                float strength = 0.24f + 0.08f * U(H2(h, 7));
                var shape = PaintShape.Stroke(line, 2.0f, 0.8f, 1f, 1f, Falloff.Linear, 1.8f, 0.3f, 4f, h);
                if (Paint(shape, TerrainMaterials.Dirt, strength, 0.5f, knockout: true) != null) R.Desire++;
            }

            public void Desire(List<AreaPlan> layers)
            {
                // Corners: path arms meeting at a junction or bend at 35–115°; people cut the corner ~7–11 m back.
                var arms = new List<(float2 at, float2[] line)>();
                var lines = new List<float2[]>();
                foreach (var l in layers)
                    foreach (var n in l.Nets)
                    {
                        if (n == null || !IsPath(n.Role)) continue;
                        var line = Polyline(n, 12);
                        lines.Add(line);
                        arms.Add((line[0], line));
                        var rev = (float2[])line.Clone(); Array.Reverse(rev);
                        arms.Add((rev[0], rev));
                    }
                var used = new bool[arms.Count];
                for (int i = 0; i < arms.Count; i++)
                {
                    if (used[i]) continue;
                    var group = new List<int> { i };
                    used[i] = true;
                    for (int j = i + 1; j < arms.Count; j++)
                        if (!used[j] && math.distancesq(arms[i].at, arms[j].at) < 0.8f) { group.Add(j); used[j] = true; }
                    if (group.Count < 2) continue;
                    var J = arms[i].at;
                    uint hj = H((int)math.floor(J.x * 2f), (int)math.floor(J.y * 2f), 3);
                    for (int a = 0; a < group.Count; a++)
                        for (int b = a + 1; b < group.Count; b++)
                        {
                            float back = 7f + 4f * U(H2(hj, a * 8 + b));
                            if (!PointAlong(arms[group[a]].line, back, out var pa, out _) || !PointAlong(arms[group[b]].line, back, out var pb, out _)) continue;
                            float ang = math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(pa - J), math.normalizesafe(pb - J)), -1f, 1f)));
                            if (ang < 35f || ang > 115f) continue;
                            uint hc = H2(hj, 100 + a * 8 + b);
                            if (U(hc) > 0.55f * math.min(1.5f, K)) continue;
                            if (!LineClear(pa, pb, 1.6f)) continue;
                            DesireLine(pa, pb, hc);
                        }
                }
                // Features off the path: a trodden line from the nearest path point to benches, play items and picnic tables.
                int oi = 0;
                foreach (var l in layers)
                    foreach (var o in l.Objects)
                    {
                        oi++;
                        if (o == null) continue;
                        string cat = RoleCategories.ForObject(o) ?? "";
                        if (!(cat.StartsWith(AssetCategories.Bench, StringComparison.Ordinal) || cat.StartsWith(AssetCategories.Playground, StringComparison.Ordinal)
                            || cat.StartsWith("play.", StringComparison.Ordinal) || cat.StartsWith(AssetCategories.Picnic, StringComparison.Ordinal))) continue;
                        var p = o.Position.xz;
                        float best = 30f; float2 q = default;
                        foreach (var line in lines)
                            for (int k = 1; k < line.Length; k++)
                            {
                                var c = Closest(line[k - 1], line[k], p);
                                float d = math.distance(c, p);
                                if (d < best) { best = d; q = c; }
                            }
                        if (best < 6f || best >= 30f) continue;
                        uint h = H(oi, 9);
                        if (U(h) > 0.35f * math.min(1.5f, K)) continue;
                        var dir = math.normalizesafe(p - q);
                        var start = q + dir * 1.5f;   // from the path edge, not its centre
                        var end = p - dir * 1.2f;     // stop short of the item (its own patch covers it)
                        if (!LineClear(start, end, 1.0f)) continue;
                        DesireLine(start, end, h);
                    }
            }

            private static float2 Closest(float2 a, float2 b, float2 p)
            {
                var ab = b - a; float l2 = math.lengthsq(ab);
                float t = l2 > 1e-6f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                return a + ab * t;
            }

            private static bool PointAlong(float2[] line, float dist, out float2 p, out float2 t)
            {
                p = default; t = default;
                for (int i = 1; i < line.Length; i++)
                {
                    float seg = math.distance(line[i - 1], line[i]);
                    if (seg >= dist && seg > 1e-4f) { t = (line[i] - line[i - 1]) / seg; p = line[i - 1] + t * dist; return true; }
                    dist -= seg;
                }
                return false;
            }

            // ───────────────────────── patches ─────────────────────────

            public void Patches(List<AreaPlan> layers)
            {
                int oi = 0;
                var dirt = new List<PaintShape>(); var halo = new List<PaintShape>();
                foreach (var l in layers)
                    foreach (var o in l.Objects)
                    {
                        oi++;
                        if (o == null) continue;
                        string cat = RoleCategories.ForObject(o) ?? "";
                        float r; float2 c = o.Position.xz; float s;
                        if (cat.StartsWith(AssetCategories.Playground, StringComparison.Ordinal) || cat.StartsWith("play.", StringComparison.Ordinal)) { r = 3.4f; s = 1f; }
                        else if (cat.StartsWith(AssetCategories.Picnic, StringComparison.Ordinal)) { r = 2.8f; s = 0.8f; }
                        else if (cat.StartsWith(AssetCategories.Bench, StringComparison.Ordinal))
                        {
                            math.sincos(o.Yaw, out float sn, out float cs);
                            c += new float2(sn, cs) * 1.2f; r = 2.0f; s = 0.65f;
                        }
                        else continue;
                        if (!Visible(c)) continue;
                        uint h = H(oi, 21);
                        r *= 0.85f + 0.3f * U(h);
                        // Worn dirt in the middle, trampled dry grass round it: dirt → dry → green.
                        dirt.Add(PaintShape.Radial(c, r * 0.25f, r, Falloff.Smooth, 0.5f, 3f, h).Scale(s));
                        halo.Add(PaintShape.Radial(c, r * 0.6f, r * 1.9f, Falloff.Smooth, 0.7f, 4f, H2(h, 1)).Scale(s));
                        R.Patches++;
                    }
                // Entrances: path ends at the outline get a trodden fan just inside.
                foreach (var l in layers)
                    foreach (var n in l.Nets)
                    {
                        if (n == null || !IsPath(n.Role)) continue;
                        foreach (var (e, inward) in new[] { (n.A.xz, n.B.xz - n.A.xz), (n.D.xz, n.C.xz - n.D.xz) })
                        {
                            if (Region.BoundaryDistance(e, 5f) > 4f) continue;
                            var dir = math.normalizesafe(inward, new float2(1, 0));
                            var nrm = new float2(-dir.y, dir.x);
                            float hw = DressingWorld.HalfWidth(n, Site);
                            uint h = H((int)math.floor(e.x * 2f), (int)math.floor(e.y * 2f), 23);
                            for (int side = -1; side <= 1; side += 2)
                            {
                                var c = e + dir * 2.5f + nrm * side * (hw + 1.4f);
                                if (!Visible(c) || U(H2(h, side + 3)) > 0.7f * math.min(1.4f, K)) continue;
                                dirt.Add(PaintShape.Radial(c, 0.6f, 2.4f, Falloff.Smooth, 0.4f, 3f, H2(h, side + 7)).Scale(0.8f));
                                R.Patches++;
                            }
                        }
                    }
                if (dirt.Count > 0) Paint(PaintShape.Union(dirt), TerrainMaterials.Dirt, 0.32f, 0.5f, knockout: true);
                if (halo.Count > 0) Paint(PaintShape.Union(halo), TerrainMaterials.Dry, 0.26f, 0.45f);
            }

            // ───────────────────────── edge variation ─────────────────────────

            /// <summary>A soft band inside the outline (0–7 m), speckled dry grass (gritty: and dirt): the build fades into the land.</summary>
            public void Edges()
            {
                var ring = Region.Ring;
                if (ring == null || ring.Length < 3) return;
                var band = PaintShape.Stroke(Closed(ring), 14f, 6.5f, 1f, 1f, Falloff.Linear, 0f, 0.25f, 14f, H(0, 31)).Mul(PaintShape.Polygon(ring, 1.5f));
                var dryPatches = band.Mul(PaintShape.Noise(7f, 0.5f, 0.6f, H(0, 32)));
                if (Paint(dryPatches, TerrainMaterials.Dry, 0.3f, 0.45f, false, ring) != null) R.Edges++;
                if (S.Style == DressStyle.Gritty)
                {
                    var dirtPatches = band.Mul(PaintShape.Noise(5f, 0.22f, 0.5f, H(0, 33)));
                    if (Paint(dirtPatches, TerrainMaterials.Dirt, 0.16f, 0.4f, false, ring) != null) R.Edges++;
                }
            }

            private static float2[] Closed(float2[] ring)
            {
                var closed = new float2[ring.Length + 1];
                Array.Copy(ring, closed, ring.Length);
                closed[ring.Length] = ring[0];
                return closed;
            }

            // ───────────────────────── extras by what was planned ─────────────────────────

            public void Extras(List<AreaPlan> layers)
            {
                var pitch = new List<PaintShape>(); var pitchHalo = new List<PaintShape>(); var fire = new List<PaintShape>();
                int oi = 0;
                foreach (var l in layers)
                    foreach (var o in l.Objects)
                    {
                        oi++;
                        if (o == null) continue;
                        string cat = RoleCategories.ForObject(o) ?? "";
                        var c = o.Position.xz;
                        if (!Region.Contains(c)) continue;
                        uint h = H(oi, 51);
                        if (cat.StartsWith(AssetCategories.SportsGoal, StringComparison.Ordinal))
                        {
                            // Goalmouth: an oval of bare dirt across the goal, worn grass round it.
                            pitch.Add(PaintShape.Radial(c, 1.2f, 5.5f, Falloff.Smooth, 0.6f, 3f, h, 1.6f, -o.Yaw));
                            pitchHalo.Add(PaintShape.Radial(c, 3f, 9f, Falloff.Smooth, 1f, 5f, H2(h, 1), 1.4f, -o.Yaw));
                        }
                        else if (cat.StartsWith(AssetCategories.SportsFitness, StringComparison.Ordinal))
                            pitch.Add(PaintShape.Radial(c, 0.8f, 3f, Falloff.Smooth, 0.4f, 2f, h));
                        else if (cat.StartsWith(AssetCategories.CampingFire, StringComparison.Ordinal))
                            fire.Add(PaintShape.Radial(c, 0.8f, 3.2f, Falloff.Fast, 0.4f, 2f, h));
                        else if (cat.StartsWith(AssetCategories.Camping, StringComparison.Ordinal))
                        {
                            // A pitch: flattened, worn ground under and round the tent / caravan.
                            pitch.Add(PaintShape.Radial(c, 1.5f, 4.5f, Falloff.Smooth, 0.7f, 3f, h).Scale(0.8f));
                            pitchHalo.Add(PaintShape.Radial(c, 3f, 7.5f, Falloff.Smooth, 1f, 4f, H2(h, 1)));
                        }
                    }
                // Dog runs: dogs wear a track along the fence line, patchy.
                if (S.LayerId == "dogPark" && Region.Ring != null && Region.Ring.Length >= 3)
                {
                    var inner = Region.InsetVertices(4.5f);
                    if (inner != null && inner.Length >= 3 && Poly.IsSimple(inner) && Poly.SignedArea(inner) > 100f)
                    {
                        var run = PaintShape.Stroke(Closed(inner), 5f, 1.8f, 1f, 1f, Falloff.Linear, 0f, 0.45f, 9f, H(0, 71))
                            .Mul(PaintShape.Noise(4f, 0.7f, 0.6f, H(0, 72)));
                        if (Paint(run, TerrainMaterials.Dirt, 0.4f, 0.55f, true, Region.Ring) != null) R.Extras++;
                    }

                }
                if (pitch.Count > 0 && Paint(PaintShape.Union(pitch), TerrainMaterials.Dirt, 0.42f, 0.6f, knockout: true) != null) R.Extras++;

                if (pitchHalo.Count > 0 && Paint(PaintShape.Union(pitchHalo), TerrainMaterials.Dry, 0.3f, 0.45f) != null) R.Extras++;
                // Fire rings: dark ash (forest floor) round the pit.
                if (fire.Count > 0 && Paint(PaintShape.Union(fire), TerrainMaterials.Lush, 0.7f, 0.85f) != null) R.Extras++;

                // Dune grass: dry grass in a soft band along the landward edge of sand (paint or surface) — sand blends into grass.
                var sandEdges = new List<PaintShape>();
                foreach (var l in layers)
                {
                    foreach (var p in l.Paint)
                        if (p != null && p.Shape == null && !p.Erase && p.Layer == TerrainMaterials.Sand && p.Strength >= 0.35f && p.Ring != null && p.Ring.Length >= 3)
                            sandEdges.Add(EdgeBand(Poly.To2D(p.Ring), 9f));
                    foreach (var s in l.Surfaces)
                        if (s?.Ring != null && s.Ring.Length >= 3 && (s.Category ?? "").StartsWith("surface.sand", StringComparison.Ordinal))
                            sandEdges.Add(EdgeBand(Poly.To2D(s.Ring), 9f));
                }
                if (sandEdges.Count > 0)
                {
                    var dune = PaintShape.Union(sandEdges).Mul(PaintShape.Noise(5f, 0.55f, 0.7f, H(0, 61)));
                    if (Paint(dune, TerrainMaterials.Dry, 0.4f, 0.55f) != null) R.Extras++;
                }

                // Farm fields: a worn headland just outside every crop field (tractors turn there), dry grass beyond.
                var headland = new List<PaintShape>(); var verge = new List<PaintShape>();
                foreach (var l in layers)
                    foreach (var s in l.Surfaces)
                        if (s?.Ring != null && s.Ring.Length >= 3 && (s.Category ?? "").StartsWith("surface.crop", StringComparison.Ordinal))
                        {
                            var r2 = Poly.To2D(s.Ring);
                            headland.Add(EdgeBand(r2, 3.5f));
                            verge.Add(EdgeBand(r2, 8f));
                        }
                if (headland.Count > 0 && Paint(PaintShape.Union(headland), TerrainMaterials.Dirt, 0.4f, 0.55f) != null) R.Extras++;
                if (verge.Count > 0 && Paint(PaintShape.Union(verge).Mul(PaintShape.Noise(6f, 0.6f, 0.6f, H(0, 62))), TerrainMaterials.Dry, 0.3f, 0.45f) != null) R.Extras++;
            }

            /// <summary>A band just OUTSIDE a polygon, full at its outline, fading over <paramref name="width"/> m.</summary>
            private static PaintShape EdgeBand(float2[] ring, float width) =>
                PaintShape.Stroke(Closed(ring), width * 2f, width, 1f, 1f, Falloff.Linear, 0f, 0.2f, 8f, 0u).Subtract(PaintShape.Polygon(ring, 0.5f));
        }
    }
}
