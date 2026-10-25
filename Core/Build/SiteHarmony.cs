// Site harmony (s31): one graded ground for everything a park-like build plans.
//
// Playtest (a park on an already bumpy hill with the Terrain feature on): paths and bike paths warped and folded,
// grass tore up through the paving, white "curtains" where path edges met ground at another height, a bike path curled
// over a bump, picnic tables and play equipment tilted on a lumpy slope. Causes (docs/proposals/s31.md):
//  * the Terrain feature ran AFTER the park (a child step) and shaped 12 m LEVEL cells straight under the paths; inside
//    the union of cells the build's raster has no bank, so neighbouring cells at different levels are vertical steps;
//  * nothing graded paths: a terrain-following path was split wherever it left the ground by 0.35 m and pinned to it,
//    so it followed every lump; CS2 levels a net across its width per piece, so on a cross-slope each piece cut one
//    side and filled the other (curtains), and pieces meeting at a junction sat at different heights;
//  * playground mulch, paving and picnic surfaces draped over the raw ground; furniture stood on whatever slope it got.
//
// The pass, after every layer (Composer.Compose, before SiteSafety / ZoneGuard), over the land of the layers that take
// part (parks and leisure types; water, parking, roads, commercial and the types whose ground IS the design keep their
// own grading and are locked):
//  1. The intended ground of the participating layers (their terraform through the build's raster) split into the
//     owners' grading and the decorative terrain (the Terrain feature), the latter smoothed and masked out of path
//     corridors, surfaces and furniture (mounds never cross paths).
//  2. Hard / feature surfaces (paving, plaza, mulch, sand, courts…) are planar-fitted (≤ 2 % small pads, ≤ 4 % large).
//  3. Paths: one graph over every terrain-following path net (junctions are shared vertices), a profile smoothed along
//     the paths like a vanilla net and limited to the type's grade, then a corridor level ACROSS the path (+ shoulders)
//     terraformed to it with a smooth bank (≤ 1 : 4) back to the ground.
//  4. Furniture (benches, tables, play equipment, centrepieces) on more than a few cm of relief gets a level pad; a
//     cluster shares one level unless the slope makes that a big step (then each item is terraced on its own).
//  5. The CHANGE is smoothed outside protected flats (no curtains), capped towards the outline, earthwork-limited, and
//     emitted once as a grid of sloped quads (one field, no overlapping patches at different heights).
//  6. Objects, surfaces and terrain-following nets of the participating layers are snapped to the final ground (the
//     build's own raster), so the plan, the preview and the build agree.
// Pure and deterministic.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    /// <summary>What the harmony pass did (facts are also written to the host plan as "harmony*").</summary>
    public sealed class HarmonyReport
    {
        public bool Ran;
        public int Cells, Paths, PathVertices, Surfaces, Pads, Resnapped;
        public float MaxCut, MaxFill;
        /// <summary>Decorative change left inside a path's level band (m). 0 by construction.</summary>
        public float MaxDecorInCorridor;
        /// <summary>Path edges still steeper than their limit (ground too steep for the earthwork cap).</summary>
        public int SteepEdges;
        /// <summary>Decorative terrain removed from corridors/pads (share of its volume, 0..1).</summary>
        public float DecorMasked;
    }

    /// <summary>Quality of the final ground under a plan (tests, gallery, facts).</summary>
    public sealed class HarmonyMetrics
    {
        /// <summary>Largest step of the terraform CHANGE between adjacent 1.75 m cells inside the site (m). A curtain is a big one.</summary>
        public float MaxJump;
        /// <summary>Largest cross-slope between a path's two edges (rise/run), junctions excepted.</summary>
        public float MaxPathCross, P95PathCross;
        /// <summary>Steepest along-grade of a path over 4 m (rise/run) and the largest ratio to that path's limit.</summary>
        public float MaxPathGrade, MaxPathGradeRatio;
        /// <summary>Largest deviation of a graded pad/surface from a plane (m) and its steepest tilt (rise/run).</summary>
        public float MaxPadDeviation, MaxPadTilt;
        /// <summary>Largest |planned height − final ground| of an object (m) and the steepest ground under furniture.</summary>
        public float MaxObjectOff, MaxFurnitureGrade;
        public int PathSamples, Pads, Furniture;
        /// <summary>Furniture standing on ground steeper than SiteHarmony.FurnitureGrade.</summary>
        public int FurnitureSteep;
        public string Worst = "";

        public override string ToString() =>
            $"jump {MaxJump:0.00} m, cross {MaxPathCross * 100:0.0}% (p95 {P95PathCross * 100:0.0}%), grade {MaxPathGrade * 100:0.0}% (x{MaxPathGradeRatio:0.00}), " +
            $"pads ±{MaxPadDeviation:0.00} m tilt {MaxPadTilt * 100:0.0}%, objects off {MaxObjectOff:0.00} m, furniture {MaxFurnitureGrade * 100:0.0}% ({FurnitureSteep} steep) [{PathSamples} path samples, {Pads} pads, {Furniture} furniture] {Worst}";
    }

    public static class SiteHarmony
    {
        /// <summary>Node spacing of the graded field (m); grows on very large sites.</summary>
        public const float Cell = 2f;
        private const int MaxNodes = 120000;
        /// <summary>Raster the build uses (BuildService.Terrain: 1.75 m texels, 4 m bank).</summary>
        public const float RasterCell = 1.75f, RasterBank = 4f;
        /// <summary>Level shoulder beside a path's edge (m).</summary>
        public const float Shoulder = 1f;
        /// <summary>Steepest bank a blend may make towards the ground it meets (rise/run).</summary>
        public const float MaxBank = 0.25f;
        /// <summary>Steepest change towards the outline / locked ground (rise/run).</summary>
        public const float BorderBank = 0.33f;
        /// <summary>Changes smaller than this are not emitted (the ground stays).</summary>
        public const float MinChange = 0.12f;
        /// <summary>Under paths, pads and graded surfaces: emitted from this change on.</summary>
        public const float MinProtectedChange = 0.02f;
        /// <summary>Cut / fill limit of the pass (m).</summary>
        public const float MaxEarthwork = 3.5f;
        /// <summary>Furniture ground (TerrainSurvey.FurnitureGrade) and pad tilt limits.</summary>
        public const float FurnitureGrade = 0.12f, SmallPadTilt = 0.02f, LargePadTilt = 0.03f, SmallPadArea = 600f;
        /// <summary>Surfaces larger than this are ground cover, not pads: left alone.</summary>
        public const float MaxPadArea = 20000f, MaxDecorativePadArea = 2500f;
        /// <summary>Surfaces larger than this are smoothed (bumps out, hill kept) instead of planar-fitted.</summary>
        public const float LargeSurfaceArea = 2000f, LongSurface = 45f;
        /// <summary>A cluster of furniture shares one level when its ground spans at most this (m); else each item is terraced.</summary>
        public const float ClusterSpan = 0.8f;
        /// <summary>Metrics: path samples within half width + this of a junction or crossing are judged as the junction (bands blend there).</summary>
        public const float JunctionZone = 5f;
        /// <summary>(s51) A bed-sized pad this close to a pad of another kind joins it (one plane, no step between their margins).</summary>
        public const float PadMergeReach = 3f;
        /// <summary>A sloping cluster is terraced as one plane of at most this tilt when that stays within the deviation (m).</summary>
        public const float ClusterTilt = 0.05f, ClusterPlaneDeviation = 1.5f;
        /// <summary>Surfaces up to this size touching a path's level band (beds, link strips) take the path's cross-level instead of their own plane.</summary>
        public const float ShoulderSurfaceArea = 150f;
        /// <summary>Paths crossing a graded surface at least this big lie on its plane (plazas, play areas).</summary>
        public const float PinSurfaceArea = 300f;

        public const string IssueSteepPath = "Placemaker.Harmony.Issue.SteepPath";

        /// <summary>Test / gallery switch (thread-static so parallel renders do not share it).</summary>
        [ThreadStatic] private static bool _disabled;
        public static bool Disabled { get => _disabled; set => _disabled = value; }
        /// <summary>Debug aid for tests: when set, decisions near matching points are logged.</summary>
        [ThreadStatic] public static Func<float2, bool> DebugAt;
        [ThreadStatic] public static List<string> DebugLog;

        /// <summary>
        /// Layers whose ground is their own design or another pass's job (water: s30, parking: s29, roads, commercial
        /// grading, landforms, rough-by-design yards). Their terraform, nets, surfaces and props are locked.
        /// </summary>
        private static readonly HashSet<string> OwnGround = new HashSet<string>(StringComparer.Ordinal)
        {
            "parking", "beach", "beachBar", "waterfront", "shore", "wetland", "wildlifeHabitat", "pond", "shopsPark",
            "roads", "split", "edgeZoning", "stripMall", "shoppingPlaza", "outdoorMall", "highStreet", "retailPark", "mallPark", "mallDistrict",
            "lookoutHill", "viewpoint", "golf", "puttingGreen", "quarry", "landfill", "hikingTrail", "trailCamp",
            "amphitheatre", "skateBowl", "dirtJumps", "urbanRuins", "spoilHeaps", "constructionSite", "abandonedLot", "debris",
            "logisticsYard", "scrapyard", "junkPiles", "skipRow", "yardBreak",
            // Lot-graded like parking (bays on tier planes): parking grading is its own pass (s25 / s29).
            "restArea",
        };

        /// <summary>The decorative terrain layer: its change is masked out of corridors, surfaces and pads.</summary>
        public const string DecorLayer = "terrain";

        /// <summary>
        /// (s35) Every Commercial type grades itself (MallGrading: plots level at the grade of the road they face, lanes on
        /// their profiles, lots in tiers) — the whole group is locked, not just the ids listed above, so a new commercial type
        /// can never be smoothed by this pass (s32's high street / retail park / mall park / mall district were).
        /// </summary>
        public static bool Participates(string generatorId) =>
            !string.IsNullOrEmpty(generatorId) && !OwnGround.Contains(generatorId) && GeneratorGroups.Of(GeneratorRegistry.Get(generatorId)) != "commercial"
            // the Zoning tool levels its own lots (LotLevel): its ground is the design
            && !(GeneratorRegistry.Get(generatorId) is Layers.Types.Zoning.PolishLookLayer);

        // ───────────────────────── context ─────────────────────────

        private sealed class PathNet
        {
            public PlannedNet Net;
            public float Hw, Grade;
            public List<int> V = new List<int>();
        }

        private sealed class Ctx
        {
            public ComposedPlan Plan;
            public Func<float2, float> Orig;
            public SiteContext Site;
            public bool[] Part;               // per layer index
            public List<float2[]> Rings = new List<float2[]>();
            public float2 Origin; public float C; public int Nx, Ny;
            public bool[] Free, Lock;
            public float[] K;                 // ground after the kept (locked) terraform
            public float[] Scratch;           // StampRings (float.MaxValue between uses)
            public int Count => Nx * Ny;
            public float2 Pos(int k) => Origin + new float2(k % Nx, k / Nx) * C;

            public bool Locate(float2 p, out int i, out int j, out float tx, out float ty)
            {
                float fx = (p.x - Origin.x) / C, fy = (p.y - Origin.y) / C;
                i = (int)math.floor(fx); j = (int)math.floor(fy);
                tx = fx - i; ty = fy - j;
                return i >= 0 && j >= 0 && i < Nx - 1 && j < Ny - 1;
            }

            public float Sample(float[] a, float2 p, float fallback)
            {
                if (!Locate(p, out int i, out int j, out float tx, out float ty)) return fallback;
                int k = j * Nx + i;
                return math.lerp(math.lerp(a[k], a[k + 1], tx), math.lerp(a[k + Nx], a[k + Nx + 1], tx), ty);
            }

            public int Nearest(float2 p)
            {
                int i = (int)math.round((p.x - Origin.x) / C), j = (int)math.round((p.y - Origin.y) / C);
                if (i < 0 || j < 0 || i >= Nx || j >= Ny) return -1;
                return j * Nx + i;
            }

            public bool FreeAt(float2 p) { int k = Nearest(p); return k >= 0 && Free[k]; }

            public void ForNodes(float2 mn, float2 mx, Action<int> f)
            {
                int i0 = math.max(0, (int)math.floor((mn.x - Origin.x) / C)), i1 = math.min(Nx - 1, (int)math.ceil((mx.x - Origin.x) / C));
                int j0 = math.max(0, (int)math.floor((mn.y - Origin.y) / C)), j1 = math.min(Ny - 1, (int)math.ceil((mx.y - Origin.y) / C));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++) f(j * Nx + i);
            }
        }

        // ───────────────────────── entry ─────────────────────────

        /// <summary>Grades the participating layers' land in place. Never throws for geometry; returns what it did.</summary>
        public static HarmonyReport Apply(ComposedPlan plan, AreaShape area, SiteContext site, CancellationToken cancel = default)
        {
            var report = new HarmonyReport();
            if (_disabled || plan == null || site?.HeightAt == null) return report;
            try { Run(plan, site, cancel, report); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = "Placemaker.Harmony.Issue.Failed", Args = new object[] { e.GetType().Name + ": " + e.Message } });
            }
            return report;
        }

        private static Ctx Prepare(ComposedPlan plan, SiteContext site, bool forMeasure)
        {
            var c = new Ctx { Plan = plan, Orig = site.HeightAt, Site = site, Part = new bool[plan.Layers.Count] };
            bool any = false;
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                c.Part[li] = l != null && Participates(l.GeneratorId);
                if (!c.Part[li] || l == null || !plan.FillRings.TryGetValue(l, out var ring) || ring == null || ring.Length < 3) continue;
                c.Rings.Add(Poly.EnsureCcw((float2[])ring.Clone()));
                any = true;
            }
            if (!any) return null;
            // Nested rings (a plaza place inside its park) add nothing but duplicate clipped cells.
            for (int a = c.Rings.Count - 1; a >= 0; a--)
                for (int b = 0; b < c.Rings.Count; b++)
                {
                    if (a == b || c.Rings[b] == null) continue;
                    bool inside = true;
                    foreach (var q in c.Rings[a]) if (!Poly.Contains(c.Rings[b], q) && Poly.DistanceToBoundary(c.Rings[b], q) > 0.05f) { inside = false; break; }
                    if (inside && (Poly.Area(c.Rings[a]) < Poly.Area(c.Rings[b]) || a > b)) { c.Rings.RemoveAt(a); break; }
                }
            float2 mn = float.MaxValue, mx = float.MinValue;
            foreach (var r in c.Rings) foreach (var q in r) { mn = math.min(mn, q); mx = math.max(mx, q); }
            float cell = Cell;
            while ((long)(math.ceil((mx.x - mn.x) / cell) + 5) * (long)(math.ceil((mx.y - mn.y) / cell) + 5) > MaxNodes) cell *= 1.2f;
            c.C = cell;
            c.Origin = mn - 2f * cell;
            c.Nx = (int)math.ceil((mx.x - mn.x) / cell) + 5;
            c.Ny = (int)math.ceil((mx.y - mn.y) / cell) + 5;
            int n = c.Count;
            c.Free = new bool[n]; c.Lock = new bool[n];

            // Locked: the kept terraform (+ its bank), the nets / surfaces / props of layers with their own ground.
            var kept = new List<(float2[] ring, float target, int layer)>();
            var keptV = new List<float[]>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l == null) continue;
                foreach (var t in l.Terraform)
                {
                    if (!Valid(t)) continue;
                    bool keep = !c.Part[li] || IsWater(t) || (!forMeasure && !InsideRings(c, t));
                    if (forMeasure && c.Part[li] && !IsWater(t)) keep = false;
                    if (!keep) continue;
                    var r2 = Poly.To2D(t.Ring);
                    kept.Add((r2, t.TargetHeight, li));
                    keptV.Add(TerraformSurface.VertexHeights(t));
                    LockRing(c, r2, RasterBank);
                }
                if (c.Part[li]) continue;
                foreach (var net in l.Nets)
                {
                    if (net == null) continue;
                    float hw = HalfWidth(net, site) + 3f;
                    LockCurve(c, net, hw);
                }
                foreach (var s in l.Surfaces) if (s?.Ring != null && s.Ring.Length >= 3) LockRing(c, Poly.To2D(s.Ring), 2f);
                foreach (var a in l.Areas) if (a?.Ring != null && a.Ring.Length >= 3) LockRing(c, Poly.To2D(a.Ring), 2f);
                foreach (var o in l.Objects)
                    if (o != null && o.Role != ObjectRole.Decal) c.ForNodes(o.Position.xz - 2.5f, o.Position.xz + 2.5f, k => { if (math.distance(c.Pos(k), o.Position.xz) <= 2.5f) c.Lock[k] = true; });
            }
            // Bridges, decks and boardwalks of participating layers keep their heights: the ground under them is not ours.
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                if (!c.Part[li] || plan.Layers[li] == null) continue;
                foreach (var net in plan.Layers[li].Nets)
                    if (net != null && !net.FollowTerrain && net.Role != NetRole.Fence) LockCurve(c, net, HalfWidth(net, site) + 1.5f);
            }
            var rK = BuildRaster(kept, keptV, c.Orig);
            c.K = new float[n];
            // (perf) The rings' edges by band: a painted outline has hundreds of corners, tested at every node.
            var bands = new List<RingBands>(c.Rings.Count);
            foreach (var r in c.Rings) bands.Add(new RingBands(r));
            for (int k = 0; k < n; k++)
            {
                var p = c.Pos(k);
                c.K[k] = S(rK, c.Orig, p);
                if (c.Lock[k]) continue;
                foreach (var b in bands) if (b.Contains(p)) { c.Free[k] = true; break; }
            }
            return c;
        }

        private static void Run(ComposedPlan plan, SiteContext site, CancellationToken cancel, HarmonyReport report)
        {
            var swatch = System.Diagnostics.Stopwatch.StartNew();
            void Lap(string what) { if (DebugLog != null) DebugLog.Add($"{what} done at {swatch.Elapsed.TotalMilliseconds:0} ms"); }
            var c = Prepare(plan, site, false);
            Lap("prepare");
            if (c == null) return;
            cancel.ThrowIfCancellationRequested();
            // Nothing to harmonise: no participating terraform and level, even ground (a flat lot) — the common case costs nothing.
            if (QuietSite(c, plan)) return;
            int n = c.Count;

            Lap("start");
            // ── 1. the intended ground: owners' grading (N) and decoration (D) ──
            var nonDecor = new List<(float2[] ring, float target, int layer)>(); var nonDecorV = new List<float[]>();
            var all = new List<(float2[] ring, float target, int layer)>(); var allV = new List<float[]>();
            var removed = new HashSet<PlannedTerraform>();
            // What the layers planned their objects on: the build raster of everything, or (layer view) later patches win.
            var allItems = new List<PlannedTerraform>();
            foreach (var l in plan.Layers) if (l != null) allItems.AddRange(l.Terraform);
            var wasField = Layers.Common.TerrainField.FromTerraform(allItems, c.Orig);
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l == null) continue;
                bool decor = c.Part[li] && l.GeneratorId == DecorLayer;
                foreach (var t in l.Terraform)
                {
                    if (!Valid(t)) continue;
                    var it = (Poly.To2D(t.Ring), t.TargetHeight, li);
                    var vh = TerraformSurface.VertexHeights(t);
                    all.Add(it); allV.Add(vh);
                    if (!decor) { nonDecor.Add(it); nonDecorV.Add(vh); }
                    if (c.Part[li] && !IsWater(t) && InsideRings(c, t)) removed.Add(t);
                }
            }
            var rN = BuildRaster(nonDecor, nonDecorV, c.Orig);
            var rA = BuildRaster(all, allV, c.Orig);
            var N = new float[n]; var D = new float[n];
            var before = new float[n];
            for (int k = 0; k < n; k++)
            {
                var p = c.Pos(k);
                N[k] = S(rN, c.Orig, p);
                before[k] = S(rA, c.Orig, p);
                D[k] = c.Free[k] ? before[k] - N[k] : 0f;
                if (!c.Free[k]) N[k] = c.K[k];
            }
            cancel.ThrowIfCancellationRequested();
            // Decoration may have come as level cells: smooth it into a landform first.
            SmoothArray(c, D, 6, 0.5f, null);

            Lap("intended ground");
            // ── features ──
            var paths = CollectPaths(c, plan, site);
            var surfaces = CollectSurfaces(c, plan, N);
            var furniture = CollectFurniture(c, plan, site);

            // Mask: decoration dies out towards paths, pads and furniture (never crosses them).
            var mask = new float[n];
            foreach (var pn in paths)
            {
                float clear = pn.Hw + Shoulder + 3f;
                StampCurve(c, pn.Net, clear + 8f, (k, d) => mask[k] = math.max(mask[k], 1f - Smooth01((d - clear) / 8f)));
            }
            // Surfaces: inside marked per ring, the distance by one chamfer transform (hundreds of paving pieces stay cheap).
            var onPad = new bool[n];
            foreach (var s in surfaces) foreach (var r in s.rings) StampRing(c, r, 0f, (k, d) => onPad[k] = true);
            var padDist = Chamfer(c, onPad);
            for (int k = 0; k < n; k++) if (padDist[k] < 11f) mask[k] = math.max(mask[k], 1f - Smooth01((padDist[k] - 3f) / 8f));
            foreach (var f in furniture) c.ForNodes(f.p - f.r - 8f, f.p + f.r + 8f, k =>
            {
                float d = math.distance(c.Pos(k), f.p) - f.r;
                mask[k] = math.max(mask[k], 1f - Smooth01((d - 2f) / 6f));
            });
            double dAll = 0, dKept = 0;
            var T = new float[n];
            for (int k = 0; k < n; k++)
            {
                float d = D[k] * (1f - mask[k]);
                dAll += math.abs(D[k]); dKept += math.abs(d);
                T[k] = c.Free[k] ? N[k] + d : c.K[k];
            }
            report.DecorMasked = dAll > 1e-3 ? (float)(1.0 - dKept / dAll) : 0f;
            var protect = new float[n];
            var pathW = new float[n];

            if (DebugLog != null) Lap($"features ({surfaces.Count} pads, {surfaces.Sum(q => q.rings.Length)} rings, {paths.Count} paths, {furniture.Count} furniture)");
            // ── 2. surfaces: planar fit (smallest first: where surfaces overlap the larger one owns the ground, a link strip
            //    or a pattern conforms to the pad / plaza it joins) ──
            surfaces.Sort((a, b) => a.area.CompareTo(b.area));
            var owner = new int[n];
            for (int k = 0; k < n; k++) owner[k] = -1;
            for (int si = 0; si < surfaces.Count; si++)
            {
                var s = surfaces[si];
                // Graded by its owner: kept as it is (owned, so pads and paths treat it as ground) but not protected from the
                // smoothing, which only acts where the owner left a step (level cells meeting at different heights).
                if (s.graded) { int id = si; StampRings(c, s.rings, 0f, (k, d) => owner[k] = id); continue; }
                if (GradeSurface(c, s, T, protect, owner, si, null)) report.Surfaces++;
            }
            cancel.ThrowIfCancellationRequested();

            Lap("surfaces");
            // ── 3. paths: profile along, level across ──
            var areas = new float[surfaces.Count];
            for (int si = 0; si < surfaces.Count; si++) areas[si] = surfaces[si].area;
            var graph = BuildGraph(c, paths, plan, site, T, owner, areas);
            report.Paths = paths.Count; report.PathVertices = graph.Pos.Count;
            var profile = new float[n];
            for (int k = 0; k < n; k++) profile[k] = float.NaN;
            if (graph.Pos.Count > 0)
            {
                report.SteepEdges = Profile(graph);
                profile = StampCorridors(c, paths, graph, T, protect, pathW);
                // A surface the path runs into (a link strip, a plaza edge) is refitted with the level band as part of
                // it, so it meets the path without a crease; the band itself is never changed.
                for (int si = 0; si < surfaces.Count; si++)
                {
                    var s = surfaces[si];
                    if (s.graded) continue;
                    bool touches = false;
                    StampRings(c, s.rings, 0f, (k, d) => { if (pathW[k] >= 1f) touches = true; });

                    if (!touches || s.area > LargeSurfaceArea || s.Extent > LongSurface) continue;
                    // A bed or link strip along a path lies on the path's shoulder: level across, at the path's height.
                    if (s.area <= ShoulderSurfaceArea) { ShoulderSurface(c, s.rings, T, protect, owner, si, pathW, profile); continue; }
                    GradeSurface(c, s, T, protect, owner, si, pathW);
                }
            }
            cancel.ThrowIfCancellationRequested();

            Lap("paths");
            // ── 4. furniture pads ──
            report.Pads = PadFurniture(c, furniture, T, protect, pathW, owner, profile);

            Lap("pads");
            // ── 5. one smooth change field ──
            var O = new float[n];
            for (int k = 0; k < n; k++) O[k] = c.Free[k] ? T[k] - c.K[k] : 0f;
            SmoothArray(c, O, 20, 0.5f, protect);
            var dist = LockedDistance(c);
            for (int k = 0; k < n; k++)
            {
                if (!c.Free[k]) { O[k] = 0f; continue; }
                // What this pass adds over the owners' intended ground dies out towards the outline (and locked ground) no
                // steeper than 1 : 2.2 and stays within the earthwork limit; the owners' own grading is theirs (already blended).
                float own = N[k] - c.K[k];
                float cap = math.min(MaxEarthwork, BorderBank * math.max(0f, dist[k] - 0.5f * c.C));
                if (DebugAt != null && DebugAt(c.Pos(k)) && math.distance(c.Pos(k), math.round(c.Pos(k))) < 9f) DebugLog?.Add($"node {c.Pos(k)} K {c.K[k]:0.00} N {N[k]:0.00} T {T[k]:0.00} O {O[k]:0.00} own {own:0.00} cap {cap:0.00} dist {dist[k]:0.0} prot {protect[k]:0.00}");
                O[k] = own + math.clamp(O[k] - own, -cap, cap);
                report.MaxCut = math.max(report.MaxCut, -O[k]); report.MaxFill = math.max(report.MaxFill, O[k]);
                T[k] = c.K[k] + O[k];
            }
            foreach (var pn in paths)
                StampCurve(c, pn.Net, pn.Hw + Shoulder, (k, d) => { if (c.Free[k]) report.MaxDecorInCorridor = math.max(report.MaxDecorInCorridor, math.abs(D[k] * (1f - mask[k]))); });

            Lap("field");
            // ── emit: replace the participating layers' terraform with the one field ──
            int host = -1;
            // The field belongs to the first participating fill layer (the park, not its last feature): features keep only
            // what is theirs, and the receipt / overlay show one terraform owner. Overlaps with later kept layers agree (locked).
            for (int li = 0; li < plan.Layers.Count && host < 0; li++) if (c.Part[li] && plan.Layers[li] != null && plan.FillRings.ContainsKey(plan.Layers[li])) host = li;
            for (int li = plan.Layers.Count - 1; li >= 0 && host < 0; li--) if (c.Part[li] && plan.Layers[li] != null) host = li;
            if (host < 0) return;
            bool hadChange = removed.Count > 0;
            for (int li = 0; li < plan.Layers.Count; li++)
                if (c.Part[li] && plan.Layers[li] != null) plan.Layers[li].Terraform.RemoveAll(t => removed.Contains(t));
            var cells = Emit(c, T, O, protect, cancel);
            var hp = plan.Layers[host];
            hp.Terraform.AddRange(cells);
            report.Cells = cells.Count;
            report.Ran = true;
            if (cells.Count > 0 || hadChange)
            {
                bool warned = false;
                foreach (var l in plan.Layers) if (l != null) foreach (var i in l.Issues) if (i.Key == Layers.Common.TerrainShaper.PermanentKey) warned = true;
                if (!warned && cells.Count > 0) hp.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = Layers.Common.TerrainShaper.PermanentKey });
            }
            if (report.SteepEdges > 0) hp.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueSteepPath });

            Lap("emit");
            // ── 6. everything onto the final ground ──
            var final = FinalGround(plan, c.Orig);
            Func<float2, float> was = p => S(rA, c.Orig, p);
            report.Resnapped = Resnap(c, plan, final, was, wasField);

            Lap("resnap");
            hp.Facts["harmonyCells"] = report.Cells;
            hp.Facts["harmonyPaths"] = report.Paths;
            hp.Facts["harmonySurfaces"] = report.Surfaces;
            hp.Facts["harmonyPads"] = report.Pads;
            hp.Facts["harmonyMaxCut"] = report.MaxCut;
            hp.Facts["harmonyMaxFill"] = report.MaxFill;
            hp.Facts["harmonySteepEdges"] = report.SteepEdges;
            hp.Facts["harmonyDecorInCorridor"] = report.MaxDecorInCorridor;
            hp.Facts["harmonyDecorMasked"] = report.DecorMasked;
            hp.HeightAfter = final;
        }

        /// <summary>
        /// True when the pass has nothing to do: the participating layers reshape nothing (no terraform besides water) and the
        /// ground under them is a plane of at most 2 % with no bumps (second differences ≤ 2 cm per node).
        /// </summary>
        private static bool QuietSite(Ctx c, ComposedPlan plan)
        {
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                if (!c.Part[li] || plan.Layers[li] == null) continue;
                foreach (var t in plan.Layers[li].Terraform) if (Valid(t) && !IsWater(t)) return false;
            }
            int nx = c.Nx;
            float maxStep = 0.02f * c.C;
            for (int j = 1; j < c.Ny - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                {
                    int k = j * nx + i;
                    if (!c.Free[k]) continue;
                    float h = c.K[k];
                    if (math.abs(c.K[k + 1] - h) > maxStep || math.abs(c.K[k + nx] - h) > maxStep) return false;
                    if (math.abs(c.K[k + 1] - 2f * h + c.K[k - 1]) > 0.02f || math.abs(c.K[k + nx] - 2f * h + c.K[k - nx]) > 0.02f) return false;
                }
            return true;
        }

        // ───────────────────────── collection ─────────────────────────

        private static bool Valid(PlannedTerraform t) => t?.Ring != null && t.Ring.Length >= 3 && math.isfinite(t.TargetHeight);
        private static bool IsWater(PlannedTerraform t) => math.isfinite(t.WaterLevel);

        private static bool InsideRings(Ctx c, PlannedTerraform t)
        {
            foreach (var v in t.Ring)
            {
                bool ok = false;
                foreach (var r in c.Rings) if (Poly.Contains(r, v.xz) || Poly.DistanceToBoundary(r, v.xz) <= 0.6f) { ok = true; break; }
                if (!ok) return false;
            }
            return true;
        }

        public static bool IsPathNet(PlannedNet n) => n != null && n.FollowTerrain && n.Role != NetRole.Fence;

        /// <summary>Half width of a net: the asset's width when known, else the role's (bike paths 2 m).</summary>
        public static float HalfWidth(PlannedNet n, SiteContext site)
        {
            if (site?.Assets != null && !string.IsNullOrEmpty(n.Asset.Id) && site.Assets.TryGetValue(n.Asset.Id, out var info) && info.NetWidth > 0.5f)
                return info.NetWidth * 0.5f;
            if (n.Role == NetRole.Pathway) return n.Category != null && n.Category.Contains("bike") ? 2f : 1.5f;
            return Composer.DefaultHalfWidth(n.Role);
        }

        /// <summary>Along-grade limit of a path (rise/run): walks and wide walks 8 % (TerrainSurvey.WalkGrade), drives 8 %, bike paths 6 %, trails 15 %.</summary>
        public static float GradeLimit(PlannedNet n)
        {
            string cat = n.Category ?? "";
            if (cat.Contains("bike")) return 0.06f;
            if (cat.Contains("trail")) return 0.15f;
            switch (n.Role)
            {
                case NetRole.Pathway: return Layers.Common.TerrainSurvey.WalkGrade;
                default: return 0.08f;
            }
        }

        private static List<PathNet> CollectPaths(Ctx c, ComposedPlan plan, SiteContext site)
        {
            var list = new List<PathNet>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                if (!c.Part[li] || plan.Layers[li] == null) continue;
                foreach (var net in plan.Layers[li].Nets)
                {
                    if (!IsPathNet(net)) continue;
                    var mid = Composer.Bezier(net.A, net.B, net.C, net.D, 0.5f).xz;
                    if (!c.FreeAt(mid) && !c.FreeAt(net.A.xz) && !c.FreeAt(net.D.xz)) continue;
                    list.Add(new PathNet { Net = net, Hw = HalfWidth(net, site), Grade = GradeLimit(net) });
                }
            }
            return list;
        }

        /// <summary>A pad: one surface, or several touching ones graded as one (a plaza's paving pieces, a strip and its pad).</summary>
        private sealed class Surf
        {
            public float2[][] rings;
            public bool graded;
            public float area;
            public float2 mn, mx;
            public SurfaceRole role;
            public float Extent => math.cmax(mx - mn);
        }

        /// <summary>Surfaces that are pads: paving, plaza, asphalt, ground (sand, courts…) up to MaxPadArea, and feature-sized decorative ones (play mulch, decks).</summary>
        public static bool IsPadSurface(PlannedSurface s)
        {
            if (s?.Ring == null || s.Ring.Length < 3) return false;
            if (s.Role == SurfaceRole.Grass || s.Role == SurfaceRole.Water) return false;
            // Crop fields follow the land; a decorative surface is a pad when it is feature-sized (play mulch, a deck, a court key).
            if (s.Category != null && s.Category.StartsWith("surface.crop", StringComparison.Ordinal)) return false;
            float a = Poly.Area(Poly.To2D(s.Ring));
            if (s.Role == SurfaceRole.Decorative && a > MaxDecorativePadArea) return false;
            return a >= 4f && a <= MaxPadArea;
        }

        private static List<Surf> CollectSurfaces(Ctx c, ComposedPlan plan, float[] N)
        {
            var list = new List<Surf>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                if (!c.Part[li] || plan.Layers[li] == null) continue;
                foreach (var s in plan.Layers[li].Surfaces)
                {
                    if (!IsPadSurface(s)) continue;
                    var ring = Poly.To2D(s.Ring);
                    int inside = 0, owned = 0, free = 0;
                    StampRing(c, ring, 0f, (k, d) =>
                    {
                        inside++;
                        if (c.Free[k]) free++;
                        if (math.abs(N[k] - c.K[k]) > 0.1f) owned++;
                    });
                    var cen = Poly.Centroid(ring);
                    if (free == 0 && !c.FreeAt(cen)) continue;
                    // Graded by its owner (a plaza's levels, a field's pad): keep that, just protect it from smoothing.
                    Poly.Bounds(ring, out var mn, out var mx);
                    list.Add(new Surf { rings = new[] { ring }, graded = inside > 0 && owned * 2 >= inside, area = Poly.Area(ring), mn = mn, mx = mx, role = s.Role });
                }
            }
            return GroupPads(list);
        }

        /// <summary>Touching (or overlapping) pads of one kind are one pad: a plaza made of paving pieces gets one plane, not a patchwork.</summary>
        private static List<Surf> GroupPads(List<Surf> list)
        {
            int m = list.Count;
            var parent = new int[m];
            for (int i = 0; i < m; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int i = 0; i < m; i++)
                for (int j = i + 1; j < m; j++)
                {
                    var a = list[i]; var b = list[j];
                    // Same kind only: a link strip stays its own (it conforms to the path), a plaza's paving pieces merge.
                    // (s51) A bed-sized pad of ANOTHER kind within a bank's reach of a pad is part of it: two planes a few metres apart
                    // would leave a step between them (their margins meet at different heights).
                    bool bed = math.min(a.area, b.area) <= SmallPadArea && a.graded == b.graded;
                    float reach = a.role == b.role ? 1f : bed ? PadMergeReach : -1f;
                    if (a.graded != b.graded || reach < 0f || math.any(a.mn - reach > b.mx) || math.any(b.mn - reach > a.mx)) continue;
                    // Boxes within reach (checked above): pieces of one paved place tile it; a cheap test that scales to hundreds.
                    if ((a.role == b.role && (a.area > 2f * SmallPadArea || b.area > 2f * SmallPadArea)) || Touch(a.rings[0], b.rings[0], reach)) parent[Find(i)] = Find(j);
                }
            var groups = new Dictionary<int, Surf>();
            var order = new List<int>();
            for (int i = 0; i < m; i++)
            {
                int r = Find(i);
                if (!groups.TryGetValue(r, out var g)) { groups[r] = g = new Surf { rings = new float2[0][], graded = list[i].graded, mn = list[i].mn, mx = list[i].mx }; order.Add(r); }
                var rs = new float2[g.rings.Length + 1][];
                Array.Copy(g.rings, rs, g.rings.Length); rs[g.rings.Length] = list[i].rings[0];
                g.rings = rs; g.area += list[i].area; g.mn = math.min(g.mn, list[i].mn); g.mx = math.max(g.mx, list[i].mx);
            }
            var result = new List<Surf>(order.Count);
            foreach (int r in order) result.Add(groups[r]);
            return result;
        }

        private static bool Touch(float2[] a, float2[] b, float tol)
        {
            foreach (var p in a) if (Poly.Contains(b, p) || Poly.DistanceToBoundary(b, p) <= tol) return true;
            foreach (var p in b) if (Poly.Contains(a, p) || Poly.DistanceToBoundary(a, p) <= tol) return true;
            return false;
        }

        private struct Furn { public float2 p; public float r; }

        /// <summary>Things people sit at, play on or stand by that must stand level: benches, tables, grills, play equipment,
        /// kiosks, centrepieces, bins. Hedges, bollards, stepping stones, planters and fences follow the ground.</summary>
        public static bool IsFurniture(PlannedObject o)
        {
            if (o == null || !IsFurniture(o.Role)) return false;
            string cat = o.Category ?? "";
            foreach (var w in FollowsGround) if (cat.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        private static readonly string[] FollowsGround = { "hedge", "bollard", "stone", "planter", "flower", "fence", "rock", "crop", "marker", "sign", "grave" };

        public static bool IsFurniture(ObjectRole role) =>
            role == ObjectRole.Bench || role == ObjectRole.Furniture || role == ObjectRole.Centerpiece || role == ObjectRole.Bin;

        public static float FootprintRadius(PlannedObject o, SiteContext site)
        {
            float s = o.Scale > 0.05f ? o.Scale : 1f;
            if (site?.Assets != null && !string.IsNullOrEmpty(o.Asset.Id) && site.Assets.TryGetValue(o.Asset.Id, out var info))
            {
                var e = info.BoundsMax.xz - info.BoundsMin.xz;
                if (e.x > 0.05f && e.y > 0.05f) return math.clamp(0.5f * math.length(e) * s, 0.4f, 12f);
            }
            return math.min(6f, Composer.DefaultRadius(o.Role));
        }

        private static List<Furn> CollectFurniture(Ctx c, ComposedPlan plan, SiteContext site)
        {
            var list = new List<Furn>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                if (!c.Part[li] || plan.Layers[li] == null) continue;
                foreach (var o in plan.Layers[li].Objects)
                    if (o != null && IsFurniture(o) && c.FreeAt(o.Position.xz)) list.Add(new Furn { p = o.Position.xz, r = FootprintRadius(o, site) });
            }
            return list;
        }

        // ───────────────────────── surfaces ─────────────────────────

        /// <summary>
        /// A small surface along a path (a bed, a link strip): its own ground and a margin take one plane through the band
        /// heights it touches (the walk's along-grade, level across), so it joins the walk without a crease; a bed between
        /// two walks at different heights takes the one it lies on, not a crease where the nearer walk changes.
        /// </summary>
        private static void ShoulderSurface(Ctx c, float2[][] rings, float[] T, float[] protect, int[] owner, int id, float[] keep, float[] profile)
        {
            float flat = c.C * 1.5f;
            var pts = new List<float3>();
            StampRings(c, rings, flat, (k, d) => { if (keep[k] >= 1f && math.isfinite(profile[k])) pts.Add(new float3(c.Pos(k).x, profile[k], c.Pos(k).y)); });
            var cen = Centroid(rings);
            if (pts.Count < 3 || !FitPlane(pts, cen, 0.10f, out float a0, out float2 g)) return;
            StampRings(c, rings, flat, (k, d) =>
            {
                if (!c.Free[k] || keep[k] >= 1f) return;
                if (owner[k] >= 0 && owner[k] != id) return;   // never into another surface
                float w = d <= 0f ? 1f : 1f - Smooth01(d / flat);
                if (w <= 0f) return;
                T[k] = math.lerp(T[k], a0 + math.dot(g, c.Pos(k) - cen), w);
                protect[k] = math.max(protect[k], w);
            });
        }

        /// <summary>
        /// Large surface: the ground inside (plus a 3 m margin) is low-passed (≈ 10 m) with the outside held, so it reads as
        /// one smooth paved ground (the bumps go, the hill stays). Owned and protected like a planar pad.
        /// </summary>
        private static bool SmoothSurface(Ctx c, float2[][] rings, float[] T, float[] protect, int[] owner, int id)
        {
            var inside = new List<int>();
            float margin = c.C * 1.5f;
            StampRings(c, rings, margin, (k, d) =>
            {
                if (!c.Free[k] || (owner[k] >= 0 && owner[k] != id && d > 0f)) return;
                inside.Add(k);
            });
            if (inside.Count < 4) return false;
            var tmp = (float[])T.Clone();
            var cur = (float[])T.Clone();
            int nx = c.Nx;
            for (int pass = 0; pass < 30; pass++)
            {
                foreach (int k in inside)
                {
                    float s = 0f; int m = 0;
                    int i = k % nx, j = k / nx;
                    if (i > 0) { s += cur[k - 1]; m++; }
                    if (i < nx - 1) { s += cur[k + 1]; m++; }
                    if (j > 0) { s += cur[k - nx]; m++; }
                    if (j < c.Ny - 1) { s += cur[k + nx]; m++; }
                    tmp[k] = m > 0 ? math.lerp(cur[k], s / m, 0.6f) : cur[k];
                }
                foreach (int k in inside) cur[k] = tmp[k];
            }
            foreach (int k in inside)
            {
                T[k] = cur[k];
                protect[k] = 1f;
                if (Inside(rings, c.Pos(k))) owner[k] = id;
            }
            return true;
        }

        /// <summary>
        /// Least-squares plane of T over the pad (tilt clamped), stamped inside with a bank outside. The bank (and the
        /// margin just outside the rings) gives way to what earlier surfaces and paths protected; nodes in <paramref name="keep"/>
        /// (a path's level band) are never changed but still steer the fit. False when nothing changed.
        /// </summary>
        private static bool GradeSurface(Ctx c, Surf s, float[] T, float[] protect, int[] owner, int id, float[] keep)
        {
            var rings = s.rings;
            // A large hard surface (a whole plaza, a forecourt) follows the land smoothly instead of one plane: one plane
            // across a 100 m square on a hill would mean metres of cut and fill; a 150 m forecourt strip is not one plane either.
            if ((s.area > LargeSurfaceArea || s.Extent > LongSurface) && keep == null) return SmoothSurface(c, rings, T, protect, owner, id);
            var pts = new List<float3>();
            StampRings(c, rings, 0f, (k, d) => { if (c.Free[k]) pts.Add(new float3(c.Pos(k).x, T[k], c.Pos(k).y)); });
            foreach (var ring in rings) foreach (var q in ring) if (c.FreeAt(q)) pts.Add(new float3(q.x, c.Sample(T, q, 0f), q.y));
            var cen = Centroid(rings);
            if (c.FreeAt(cen)) pts.Add(new float3(cen.x, c.Sample(T, cen, 0f), cen.y));
            if (pts.Count < 3) return false;
            // A refit beside a path may tilt with the path (no crease at the band). At the outline the surface meets the
            // street / neighbour as it is: it may fall with it (a forecourt along a sloping street).
            bool atEdge = false;
            foreach (var ring in rings) foreach (var q in ring) foreach (var r in c.Rings) if (Poly.DistanceToBoundary(r, q) <= 3f) atEdge = true;
            float maxTilt = keep != null || atEdge ? 0.10f : s.area <= SmallPadArea ? SmallPadTilt : LargePadTilt;
            if (!FitPlane(pts, cen, maxTilt, out float a0, out float2 g)) return false;
            float dev = 0f;
            foreach (var q in pts) dev = math.max(dev, math.abs(a0 + math.dot(g, q.xz - cen) - q.y));
            if (dev < 0.03f) { int own = id; StampRings(c, rings, 0f, (k, d) => { protect[k] = 1f; if (keep == null) owner[k] = own; }); return false; }
            float bw = math.clamp(dev / MaxBank, 2.5f, 14f);
            // Level a margin outside the rings too: the build rasters 1.75 m texels from 2 m node quads, so the edge stays flat.
            float flat = c.C * 1.5f;
            StampRings(c, rings, flat + bw, (k, d) =>
            {
                if (!c.Free[k] || (keep != null && keep[k] >= 1f)) return;
                // Refit (keep != null): only the ground this surface owns; a larger surface it overlaps keeps its plane.
                if (keep != null && owner[k] >= 0 && owner[k] != id) return;
                if (d <= 0f) owner[k] = id;
                float w = d <= 0f ? 1f : d <= flat ? 1f - protect[k] : (1f - Smooth01((d - flat) / bw)) * (1f - protect[k]);
                if (w <= 0f) return;
                float h = a0 + math.dot(g, c.Pos(k) - cen);
                if (DebugAt != null && DebugAt(c.Pos(k)) && DebugLog != null && DebugLog.Count < 400) DebugLog.Add($"surfwrite {c.Pos(k)} h {h:0.00} w {w:0.00} at {cen} area {s.area:0} keep {keep != null}");
                T[k] = math.lerp(T[k], h, w);
                protect[k] = math.max(protect[k], w);
            });
            return true;
        }

        private static bool Inside(float2[][] rings, float2 p) { foreach (var r in rings) if (Poly.Contains(r, p)) return true; return false; }

        private static float2 Centroid(float2[][] rings)
        {
            if (rings.Length == 1) return Poly.Centroid(rings[0]);
            float2 sum = 0f; float wsum = 0f;
            foreach (var r in rings) { float a = math.max(1e-3f, Poly.Area(r)); sum += Poly.Centroid(r) * a; wsum += a; }
            return sum / wsum;
        }

        /// <summary>StampRing over several rings: distance 0 inside any, else the smallest distance to one of them. Each ring is
        /// stamped over its own neighbourhood only (a plaza of a hundred pieces must not scan its whole box per piece).</summary>
        private static void StampRings(Ctx c, float2[][] rings, float reach, Action<int, float> f)
        {
            if (rings.Length == 1) { StampRing(c, rings[0], reach, f); return; }
            if (c.Scratch == null)
            {
                c.Scratch = new float[c.Count];
                for (int k = 0; k < c.Scratch.Length; k++) c.Scratch[k] = float.MaxValue;
            }
            var best = c.Scratch;
            var touched = new List<int>();
            foreach (var r in rings)
                StampRing(c, r, reach, (k, d) =>
                {
                    if (best[k] == float.MaxValue) touched.Add(k);
                    if (d < best[k]) best[k] = d;
                });
            foreach (int k in touched)
            {
                float d = best[k];
                best[k] = float.MaxValue;
                f(k, d);
            }
        }


        /// <summary>z = a0 + g·(p − c); g clamped to <paramref name="maxTilt"/>, then a0 re-centred.</summary>
        public static bool FitPlane(List<float3> pts, float2 c, float maxTilt, out float a0, out float2 g)
        {
            a0 = 0f; g = 0f;
            if (pts == null || pts.Count == 0) return false;
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sz = 0, sxz = 0, syz = 0; int n = pts.Count;
            foreach (var q in pts)
            {
                double x = q.x - c.x, y = q.z - c.y, z = q.y;
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sz += z; sxz += x * z; syz += y * z;
            }
            // Centred normal equations for the slope.
            double mx = sx / n, my = sy / n, mz = sz / n;
            double cxx = sxx - n * mx * mx, cxy = sxy - n * mx * my, cyy = syy - n * my * my, cxz = sxz - n * mx * mz, cyz = syz - n * my * mz;
            double det = cxx * cyy - cxy * cxy;
            double gx = 0, gy = 0;
            if (math.abs(det) > 1e-6) { gx = (cxz * cyy - cyz * cxy) / det; gy = (cyz * cxx - cxz * cxy) / det; }
            g = new float2((float)gx, (float)gy);
            float len = math.length(g);
            if (len > maxTilt) g *= maxTilt / len;
            double s = 0;
            foreach (var q in pts) s += q.y - math.dot(g, q.xz - c);
            a0 = (float)(s / n);
            return math.isfinite(a0) && math.all(math.isfinite(g));
        }

        // ───────────────────────── paths ─────────────────────────

        private sealed class Graph
        {
            public List<float2> Pos = new List<float2>();
            public List<float> H0 = new List<float>(), H = new List<float>();
            public List<bool> Pinned = new List<bool>();
            public List<List<int>> Nb = new List<List<int>>();
            public List<(int a, int b, float len, float grade)> Edges = new List<(int, int, float, float)>();
            public Dictionary<long, List<int>> Ends = new Dictionary<long, List<int>>();
            public List<float2> Crossings = new List<float2>();
            /// <summary>Close vertices of other walks (smoothing neighbours only).</summary>
            public List<int>[] Soft;
        }

        private static long Key(float2 p) => ((long)(int)math.floor(p.x * 2f) << 32) ^ (uint)(int)math.floor(p.y * 2f);

        private static int Vertex(Graph g, float2 p, bool end)
        {
            if (end)
            {
                int cx = (int)math.floor(p.x * 2f), cy = (int)math.floor(p.y * 2f);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        long key = ((long)(cx + dx) << 32) ^ (uint)(cy + dy);
                        if (!g.Ends.TryGetValue(key, out var l)) continue;
                        foreach (int v in l) if (math.distance(g.Pos[v], p) <= 0.6f) return v;
                    }
            }
            int id = g.Pos.Count;
            g.Pos.Add(p); g.H0.Add(0f); g.H.Add(0f); g.Pinned.Add(false); g.Nb.Add(new List<int>(2));
            if (end)
            {
                long k = Key(p);
                if (!g.Ends.TryGetValue(k, out var l)) g.Ends[k] = l = new List<int>(2);
                l.Add(id);
            }
            return id;
        }

        private static Graph BuildGraph(Ctx c, List<PathNet> paths, ComposedPlan plan, SiteContext site, float[] T, int[] owner, float[] pinArea)
        {
            var g = new Graph();
            // Ends of decks/bridges (explicit heights) pin the paths that meet them.
            var decks = new List<float3>();
            foreach (var l in plan.Layers)
                if (l != null) foreach (var n in l.Nets) if (n != null && !n.FollowTerrain && n.Role != NetRole.Fence) { decks.Add(n.A); decks.Add(n.D); }
            foreach (var pn in paths)
            {
                var net = pn.Net;
                float len = 0f; float2 prev = net.A.xz;
                for (int i = 1; i <= 16; i++) { var q = Composer.Bezier(net.A, net.B, net.C, net.D, i / 16f).xz; len += math.distance(prev, q); prev = q; }
                int segs = math.max(1, (int)math.ceil(len / 2f));
                int last = Vertex(g, net.A.xz, true);
                pn.V.Add(last);
                for (int i = 1; i <= segs; i++)
                {
                    float t = i / (float)segs;
                    var q = Composer.Bezier(net.A, net.B, net.C, net.D, t).xz;
                    int v = Vertex(g, q, i == segs);
                    if (v == last) continue;
                    g.Nb[last].Add(v); g.Nb[v].Add(last);
                    g.Edges.Add((last, v, math.max(0.05f, math.distance(g.Pos[last], q)), pn.Grade));
                    pn.V.Add(v);
                    last = v;
                }
            }
            // Paths that cross without a shared node (an X crossing PathJoins did not split) are tied together at the
            // crossing, so both profiles meet at one height there (no step where one band crosses the other).
            var buckets = new Dictionary<long, List<(int pi, int i)>>();
            const float Bk = 8f;
            for (int pi = 0; pi < paths.Count; pi++)
            {
                var vs = paths[pi].V;
                for (int i = 1; i < vs.Count; i++)
                {
                    float2 a = g.Pos[vs[i - 1]], b = g.Pos[vs[i]];
                    int x0 = (int)math.floor(math.min(a.x, b.x) / Bk), x1 = (int)math.floor(math.max(a.x, b.x) / Bk);
                    int y0 = (int)math.floor(math.min(a.y, b.y) / Bk), y1 = (int)math.floor(math.max(a.y, b.y) / Bk);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            long key = ((long)x << 32) ^ (uint)y;
                            if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<(int, int)>();
                            l.Add((pi, i));
                        }
                }
            }
            var tied = new HashSet<long>();
            foreach (var l in buckets.Values)
                for (int s = 0; s < l.Count; s++)
                    for (int t = s + 1; t < l.Count; t++)
                    {
                        var (pa, ia) = l[s]; var (pb, ib) = l[t];
                        if (pa == pb) continue;
                        var va = paths[pa].V; var vb = paths[pb].V;
                        int a0 = va[ia - 1], a1 = va[ia], b0 = vb[ib - 1], b1 = vb[ib];
                        if (a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1) continue;   // a real junction
                        if (!Poly.SegmentIntersection(g.Pos[a0], g.Pos[a1], g.Pos[b0], g.Pos[b1], out var x, out _, out _)) continue;
                        int u = math.distance(g.Pos[a0], x) <= math.distance(g.Pos[a1], x) ? a0 : a1;
                        int v = math.distance(g.Pos[b0], x) <= math.distance(g.Pos[b1], x) ? b0 : b1;
                        long key = ((long)math.min(u, v) << 32) ^ (uint)math.max(u, v);
                        if (u == v || !tied.Add(key)) continue;
                        g.Nb[u].Add(v); g.Nb[v].Add(u);
                        g.Edges.Add((u, v, math.max(0.3f, math.distance(g.Pos[u], g.Pos[v])), math.min(paths[pa].Grade, paths[pb].Grade)));
                        g.Crossings.Add(x);
                    }
            // Walks that pass close to each other (their level bands touch) are smoothed together, so they settle at
            // matching heights instead of meeting in a crease between them. Smoothing neighbours only, no grade edge.
            var owners = new int[g.Pos.Count];
            var hws = new float[g.Pos.Count];
            for (int v = 0; v < owners.Length; v++) owners[v] = -1;
            for (int pi = 0; pi < paths.Count; pi++)
                foreach (int v in paths[pi].V) { if (owners[v] < 0) owners[v] = pi; hws[v] = math.max(hws[v], paths[pi].Hw); }
            var vbk = new Dictionary<long, List<int>>();
            const float Vb = 6f;
            for (int v = 0; v < g.Pos.Count; v++)
            {
                long key = ((long)(int)math.floor(g.Pos[v].x / Vb) << 32) ^ (uint)(int)math.floor(g.Pos[v].y / Vb);
                if (!vbk.TryGetValue(key, out var l)) vbk[key] = l = new List<int>();
                l.Add(v);
            }
            g.Soft = new List<int>[g.Pos.Count];
            for (int v = 0; v < g.Pos.Count; v++)
            {
                int cx = (int)math.floor(g.Pos[v].x / Vb), cy = (int)math.floor(g.Pos[v].y / Vb);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!vbk.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cy + dy), out var l)) continue;
                        foreach (int u in l)
                        {
                            if (u == v || owners[u] == owners[v] || g.Nb[v].Contains(u)) continue;
                            if (math.distance(g.Pos[u], g.Pos[v]) > hws[u] + hws[v] + 2f * Shoulder + 1f) continue;
                            (g.Soft[v] ?? (g.Soft[v] = new List<int>(2))).Add(u);
                        }
                    }
            }
            for (int v = 0; v < g.Pos.Count; v++)
            {
                var p = g.Pos[v];
                float h = c.Sample(T, p, c.Orig(p));
                // On a graded surface (a plaza, a pad) the path lies on its plane: it is the ground there.
                // On a large graded surface (a plaza, a play area) the path lies on its plane; small pads and strips conform
                // to the path instead (refitted after it).
                int ok = c.Nearest(p);
                bool pin = !c.FreeAt(p) || (ok >= 0 && owner[ok] >= 0 && pinArea[owner[ok]] >= PinSurfaceArea);
                if (g.Nb[v].Count <= 1)
                {
                    // A dangling end at the outline meets a street (or the ground outside): it stays on the ground there.
                    foreach (var r in c.Rings) if (Poly.DistanceToBoundary(r, p) <= 4f) { pin = true; h = c.Sample(c.K, p, c.Orig(p)); break; }
                }
                foreach (var d in decks) if (math.distance(d.xz, p) <= 0.6f) { pin = true; h = d.y; break; }
                g.H0[v] = h; g.H[v] = h; g.Pinned[v] = pin;
            }
            return g;
        }

        /// <summary>Smooth profile along the graph (like a vanilla net) within each edge's grade limit. Returns edges still too steep.</summary>
        private static int Profile(Graph g)
        {
            int n = g.Pos.Count;
            var tmp = new float[n];
            var h = g.H.ToArray();
            for (int pass = 0; pass < 140; pass++)
            {
                for (int v = 0; v < n; v++)
                {
                    if (g.Pinned[v] || g.Nb[v].Count == 0) { tmp[v] = g.Pinned[v] ? g.H0[v] : h[v]; continue; }
                    float s = 0f, wsum = 0f; foreach (int u in g.Nb[v]) { s += h[u]; wsum += 1f; }
                    var soft = g.Soft?[v];
                    if (soft != null) foreach (int u in soft) { s += 0.5f * h[u]; wsum += 0.5f; }
                    tmp[v] = math.lerp(s / wsum, g.H0[v], 0.025f);
                }
                Array.Copy(tmp, h, n);
            }
            for (int pass = 0; pass < 80; pass++)
            {
                bool changed = false;
                foreach (var e in g.Edges)
                {
                    float lim = e.grade * e.len, d = h[e.b] - h[e.a];
                    if (math.abs(d) <= lim + 1e-4f) continue;
                    float excess = math.abs(d) - lim, s = math.sign(d);
                    bool pa = g.Pinned[e.a], pb = g.Pinned[e.b];
                    if (pa && pb) continue;
                    if (pa) h[e.b] -= s * excess;
                    else if (pb) h[e.a] += s * excess;
                    else { h[e.a] += s * excess * 0.5f; h[e.b] -= s * excess * 0.5f; }
                    changed = true;
                }
                if (!changed) break;
            }
            int steep = 0;
            for (int v = 0; v < n; v++) h[v] = math.clamp(h[v], g.H0[v] - MaxEarthwork, g.H0[v] + MaxEarthwork);
            foreach (var e in g.Edges) if (math.abs(h[e.b] - h[e.a]) > e.grade * e.len * 1.05f + 0.02f) steep++;
            for (int v = 0; v < n; v++) g.H[v] = h[v];
            return steep;
        }

        /// <summary>
        /// Terraforms every path's corridor to its profile: level ACROSS the path plus shoulders (the level band), a bank
        /// back to the ground beyond (≤ 1 : 4, giving way to graded surfaces). Near a junction the two nearest paths'
        /// heights are blended over a few metres so their bands meet without a crease. Returns the profile height per node
        /// (NaN where no path is near) for pads and strips that continue a path's shoulder.
        /// </summary>
        private static float[] StampCorridors(Ctx c, List<PathNet> paths, Graph g, float[] T, float[] protect, float[] pathW)
        {
            int n = c.Count;
            var d1 = new float[n]; var h1 = new float[n]; var f1 = new float[n]; var p1 = new int[n];
            var d2 = new float[n]; var h2 = new float[n]; var p2 = new int[n];
            for (int k = 0; k < n; k++) { d1[k] = d2[k] = float.MaxValue; p1[k] = p2[k] = -1; }
            float extra = c.C * 0.75f;
            for (int pi = 0; pi < paths.Count; pi++)
            {
                var pn = paths[pi];
                float flat = pn.Hw + Shoulder + extra, reach = flat + 14f;
                int id = pi;
                for (int i = 1; i < pn.V.Count; i++)
                {
                    int va = pn.V[i - 1], vb = pn.V[i];
                    float2 a = g.Pos[va], b = g.Pos[vb];
                    float ha = g.H[va], hb = g.H[vb];
                    c.ForNodes(math.min(a, b) - reach, math.max(a, b) + reach, k =>
                    {
                        var p = c.Pos(k);
                        var ab = b - a; float l2 = math.lengthsq(ab);
                        float t = l2 > 1e-9f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
                        float d = math.distance(p, a + ab * t);
                        if (d > reach) return;
                        float h = math.lerp(ha, hb, t);
                        if (d < d1[k])
                        {
                            if (p1[k] != id) { d2[k] = d1[k]; h2[k] = h1[k]; p2[k] = p1[k]; }
                            d1[k] = d; h1[k] = h; f1[k] = flat; p1[k] = id;
                        }
                        else if (id != p1[k] && d < d2[k]) { d2[k] = d; h2[k] = h; p2[k] = id; }
                    });
                }
            }
            const float JoinBlend = 3f, JoinReach = 9f;
            // Only around junctions and crossings (not between two parallel walks that merely pass close).
            var near = new float[n];
            var joins = new List<float2>(g.Crossings);
            for (int v = 0; v < g.Pos.Count; v++) if (g.Nb[v].Count >= 3) joins.Add(g.Pos[v]);
            foreach (var j in joins)
                c.ForNodes(j - JoinReach, j + JoinReach, k => near[k] = math.max(near[k], 1f - Smooth01((math.distance(c.Pos(k), j) - 3f) / (JoinReach - 3f))));
            var profile = new float[n];
            for (int k = 0; k < n; k++)
            {
                profile[k] = float.NaN;
                if (d1[k] == float.MaxValue) continue;
                float h = h1[k];
                if (p2[k] >= 0 && d2[k] - d1[k] < JoinBlend && near[k] > 0f)
                    h = math.lerp(h, h2[k], 0.5f * near[k] * (1f - Smooth01((d2[k] - d1[k]) / JoinBlend)));
                profile[k] = h;
                if (!c.Free[k]) continue;
                float delta = math.abs(h - T[k]);
                float bw = math.clamp(delta / MaxBank, 2.5f, 14f);
                bool level = d1[k] <= f1[k];
                float w = level ? 1f : 1f - Smooth01((d1[k] - f1[k]) / bw);
                // A graded surface beside the path keeps its plane: the bank gives way to it (the level band never does).
                if (!level) w *= 1f - protect[k];
                if (w <= 0f) continue;
                T[k] = math.lerp(T[k], h, w);
                if (DebugAt != null && DebugAt(c.Pos(k)) && DebugLog != null && DebugLog.Count < 400) DebugLog.Add($"pathwrite {c.Pos(k)} h {h:0.00} w {w:0.00} level {level} d1 {d1[k]:0.0} p1 {p1[k]} d2 {d2[k]:0.0}");
                protect[k] = math.max(protect[k], w);
                pathW[k] = level ? 1f : 0f;   // pads may reshape the bank, never the level band
            }
            return profile;
        }

        // ───────────────────────── furniture ─────────────────────────

        private static bool InsideOwned(Ctx c, int[] owner, float2 p) { int k = c.Nearest(p); return k >= 0 && owner[k] >= 0; }

        private static int PadFurniture(Ctx c, List<Furn> items, float[] T, float[] protect, float[] pathW, int[] owner, float[] profile)
        {
            int m = items.Count;
            if (m == 0 || m > 4000) return 0;
            var parent = new int[m];
            for (int i = 0; i < m; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            // Neighbours within (r_i + r_j + 2.5 m) form one cluster (picnic tables, a playground's equipment).
            var grid = new Dictionary<long, List<int>>();
            const float B = 8f;
            for (int i = 0; i < m; i++)
            {
                long key = ((long)(int)math.floor(items[i].p.x / B) << 32) ^ (uint)(int)math.floor(items[i].p.y / B);
                if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                l.Add(i);
            }
            for (int i = 0; i < m; i++)
            {
                int cx = (int)math.floor(items[i].p.x / B), cy = (int)math.floor(items[i].p.y / B);
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        if (!grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cy + dy), out var l)) continue;
                        foreach (int j in l)
                            // Pads whose level discs (r + 1.75 m) would touch belong to one cluster (one level or one plane).
                            if (j > i && math.distance(items[i].p, items[j].p) <= items[i].r + items[j].r + 4.5f) parent[Find(i)] = Find(j);
                    }
            }
            var clusters = new Dictionary<int, List<int>>();
            for (int i = 0; i < m; i++) { int r = Find(i); if (!clusters.TryGetValue(r, out var l)) clusters[r] = l = new List<int>(); l.Add(i); }
            var keys = new List<int>(clusters.Keys); keys.Sort();
            int pads = 0;
            foreach (int key in keys)
            {
                var members = clusters[key];
                var hs = new float[members.Count];
                float lo = float.MaxValue, hi = float.MinValue, sum = 0f;
                for (int q = 0; q < members.Count; q++)
                {
                    hs[q] = c.Sample(T, items[members[q]].p, 0f);
                    lo = math.min(lo, hs[q]); hi = math.max(hi, hs[q]); sum += hs[q];
                }
                bool shared = hi - lo <= ClusterSpan;
                float level = sum / members.Count;
                // A bigger cluster on a slope (a picnic lawn, rows of graves, a playground) is terraced as ONE gentle plane
                // (≤ ClusterTilt) so neighbouring pads never meet at different levels; failing that, each item on its own.
                bool planar = false; float pa = 0f; float2 pg = 0f, pc = 0f;
                if (!shared && members.Count >= 3)
                {
                    var pts = new List<float3>(members.Count);
                    for (int q = 0; q < members.Count; q++) { pts.Add(new float3(items[members[q]].p.x, hs[q], items[members[q]].p.y)); pc += items[members[q]].p; }
                    pc /= members.Count;
                    // First a gentle terrace, then a plane as steep as furniture may stand (a row of stepping stones down a slope).
                    foreach (float tilt in new[] { ClusterTilt, FurnitureGrade * 0.8f })
                    {
                        if (planar || !FitPlane(pts, pc, tilt, out pa, out pg)) continue;
                        float dev = 0f;
                        foreach (var q in pts) dev = math.max(dev, math.abs(pa + math.dot(pg, q.xz - pc) - q.y));
                        planar = dev <= ClusterPlaneDeviation;
                    }
                }
                // A cluster that touches a path's level band continues that band (level across, the path's height) as a whole,
                // so its pads never meet the band, or each other, at different levels.
                bool clusterShoulder = false;
                foreach (int mi in members)
                {
                    var mp = items[mi].p; float reach = items[mi].r + 0.75f + c.C * 0.5f;
                    c.ForNodes(mp - reach, mp + reach, k => { if (pathW[k] >= 1f && math.distance(c.Pos(k), mp) <= reach) clusterShoulder = true; });
                }
                for (int q = 0; q < members.Count; q++)
                {
                    var it = items[members[q]];
                    float h = shared ? level : planar ? pa + math.dot(pg, it.p - pc) : hs[q];
                    float Target(float2 at) => planar ? pa + math.dot(pg, at - pc) : h;
                    float relief = 0f;
                    for (int a = 0; a <= 8; a++)
                    {
                        var at = a == 8 ? it.p : it.p + new float2(math.cos(a * math.PI / 4f), math.sin(a * math.PI / 4f)) * (it.r + 0.5f);
                        relief = math.max(relief, math.abs(c.Sample(T, at, h) - Target(at)));
                    }
                    // On a graded surface the surface is the pad (play equipment on its mulch, tables on their paving).
                    if (DebugAt != null && DebugAt(it.p)) DebugLog?.Add($"pad {it.p} r {it.r} cluster {members.Count} shared {shared} planar {planar} h {h:0.00} relief {relief:0.00} owned {InsideOwned(c, owner, it.p)}");
                    if (InsideOwned(c, owner, it.p)) continue;
                    // Already level: no pad, but the spot is still protected so later smoothing cannot tilt it.
                    bool needed = relief > 0.08f;
                    bool shoulder = clusterShoulder;
                    // Items levelled one by one keep their flats small so neighbours do not cut into each other.
                    float flat = (shared || planar ? it.r + 0.75f + c.C * 0.5f : it.r + 0.5f), bw = math.clamp(relief / MaxBank, 2f, 8f);
                    if (DebugAt != null && DebugLog != null) { var dp = it.p; for (float dd = 0; dd <= flat + bw; dd += 1f) if (DebugAt(dp + new float2(dd, 0)) || DebugAt(dp - new float2(dd, 0)) || DebugAt(dp + new float2(0, dd)) || DebugAt(dp - new float2(0, dd))) { DebugLog.Add($"  reaching pad at {it.p} r {it.r} h {h:0.00} planar {planar} shared {shared} n {members.Count} shoulder {shoulder}"); break; } }
                    c.ForNodes(it.p - flat - bw, it.p + flat + bw, k =>
                    {
                        if (!c.Free[k]) return;
                        float d = math.distance(c.Pos(k), it.p);
                        // The bank gives way to pads already stamped (neighbours keep their level); the flat always wins.
                        float w = d <= flat ? 1f : (1f - Smooth01((d - flat) / bw)) * (1f - protect[k]);
                        // Never inside a level band or a graded surface's own ring (their margins and banks may give way).
                        w *= (1f - pathW[k]) * (owner[k] >= 0 ? 0f : 1f);
                        if (w <= 0f) return;
                        // Beside a path the pad continues its shoulder: level across at the path's height, no crease at the band.
                        float target = shoulder && math.isfinite(profile[k]) ? profile[k] : Target(c.Pos(k));
                        T[k] = math.lerp(T[k], target, w);
                        if (DebugAt != null && DebugAt(c.Pos(k)) && DebugLog != null && DebugLog.Count < 400) DebugLog.Add($"padwrite {c.Pos(k)} target {target:0.00} w {w:0.00} from {it.p} planar {planar} shoulder {shoulder} n {members.Count}");
                        protect[k] = math.max(protect[k], w);
                    });
                    if (needed) pads++;
                }
            }
            return pads;
        }

        // ───────────────────────── field ops ─────────────────────────

        /// <summary>Laplacian smoothing of a node array over free nodes (locked nodes hold); weight = 1 − protect.</summary>
        private static void SmoothArray(Ctx c, float[] a, int passes, float strength, float[] protect)
        {
            var tmp = new float[a.Length];
            int nx = c.Nx;
            for (int pass = 0; pass < passes; pass++)
            {
                Array.Copy(a, tmp, a.Length);
                for (int j = 1; j < c.Ny - 1; j++)
                    for (int i = 1; i < nx - 1; i++)
                    {
                        int k = j * nx + i;
                        if (!c.Free[k]) continue;
                        float s = strength * (protect != null ? 1f - protect[k] : 1f);
                        if (s <= 0f) continue;
                        float avg = (tmp[k - 1] + tmp[k + 1] + tmp[k - nx] + tmp[k + nx]) * 0.25f;
                        a[k] = math.lerp(tmp[k], avg, s);
                    }
            }
        }

        /// <summary>Distance (m) from every node to the nearest marked node (two-pass chamfer; 1e9 when none).</summary>
        private static float[] Chamfer(Ctx c, bool[] seed)
        {
            var far = new bool[seed.Length];
            for (int k = 0; k < seed.Length; k++) far[k] = !seed[k];
            return LockedDistance(c, far);
        }

        /// <summary>Distance (m) from every node to the nearest non-free node (two-pass chamfer).</summary>
        private static float[] LockedDistance(Ctx c, bool[] free = null)
        {
            int nx = c.Nx, ny = c.Ny;
            var d = new float[c.Count];
            free = free ?? c.Free;
            for (int k = 0; k < d.Length; k++) d[k] = free[k] ? 1e9f : 0f;
            float a = c.C, b = c.C * 1.41421356f;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    if (i > 0) d[k] = math.min(d[k], d[k - 1] + a);
                    if (j > 0) d[k] = math.min(d[k], d[k - nx] + a);
                    if (i > 0 && j > 0) d[k] = math.min(d[k], d[k - nx - 1] + b);
                    if (i < nx - 1 && j > 0) d[k] = math.min(d[k], d[k - nx + 1] + b);
                }
            for (int j = ny - 1; j >= 0; j--)
                for (int i = nx - 1; i >= 0; i--)
                {
                    int k = j * nx + i;
                    if (i < nx - 1) d[k] = math.min(d[k], d[k + 1] + a);
                    if (j < ny - 1) d[k] = math.min(d[k], d[k + nx] + a);
                    if (i < nx - 1 && j < ny - 1) d[k] = math.min(d[k], d[k + nx + 1] + b);
                    if (i > 0 && j < ny - 1) d[k] = math.min(d[k], d[k + nx - 1] + b);
                }
            return d;
        }

        private static List<PlannedTerraform> Emit(Ctx c, float[] T, float[] O, float[] protect, CancellationToken cancel)
        {
            var list = new List<PlannedTerraform>();
            int nx = c.Nx;
            for (int j = 0; j < c.Ny - 1; j++)
            {
                if ((j & 15) == 0) cancel.ThrowIfCancellationRequested();
                for (int i = 0; i < nx - 1; i++)
                {
                    int k00 = j * nx + i, k10 = k00 + 1, k01 = k00 + nx, k11 = k01 + 1;
                    if (!(c.Free[k00] || c.Free[k10] || c.Free[k01] || c.Free[k11])) continue;
                    float big = math.max(math.max(math.abs(O[k00]), math.abs(O[k10])), math.max(math.abs(O[k01]), math.abs(O[k11])));
                    // Under a level band, pad or graded surface even a small change matters (2 % across a 3 m path is 6 cm).
                    float prot = math.max(math.max(protect[k00], protect[k10]), math.max(protect[k01], protect[k11]));
                    if (big < (prot >= 0.5f ? MinProtectedChange : MinChange)) continue;
                    float2 p00 = c.Pos(k00), p11 = c.Pos(k11);
                    float mean = (O[k00] + O[k10] + O[k01] + O[k11]) * 0.25f;
                    int whole = -1;
                    for (int r = 0; r < c.Rings.Count && whole < 0; r++)
                    {
                        var ring = c.Rings[r];
                        if (!Poly.Contains(ring, p00) || !Poly.Contains(ring, p11) || !Poly.Contains(ring, c.Pos(k10)) || !Poly.Contains(ring, c.Pos(k01))) continue;
                        bool vertexInside = false;
                        foreach (var q in ring) if (q.x > p00.x && q.x < p11.x && q.y > p00.y && q.y < p11.y) { vertexInside = true; break; }
                        if (!vertexInside) whole = r;
                    }
                    if (whole >= 0)
                    {
                        list.Add(Quad(new[] { p00, c.Pos(k10), p11, c.Pos(k01) }, new[] { T[k00], T[k10], T[k11], T[k01] }, mean));
                        continue;
                    }
                    foreach (var ring in c.Rings)
                    {
                        var piece = Layers.Common.Sculpt.ClipToRect(ring, p00, p11);
                        if (piece == null) continue;
                        var clean = Poly.Clean(piece, 0.15f);
                        if (clean.Length < 3 || Poly.Area(clean) < 0.5f) continue;
                        var hs = new float[clean.Length];
                        for (int v = 0; v < clean.Length; v++) hs[v] = c.Sample(T, clean[v], c.K[k00]);
                        list.Add(Quad(clean, hs, mean));
                    }
                }
            }
            return list;
        }

        private static PlannedTerraform Quad(float2[] ring, float[] h, float meanOffset)
        {
            var r3 = new float3[ring.Length];
            for (int v = 0; v < ring.Length; v++) r3[v] = new float3(ring[v].x, h[v], ring[v].y);
            var cen = Poly.Centroid(ring);
            return new PlannedTerraform
            {
                Ring = r3, Kind = meanOffset > 0f ? TerraformKind.Raise : TerraformKind.Lower,
                TargetHeight = TerraformSurface.At(r3, cen), Permanent = true, Sloped = true,
            };
        }

        // ───────────────────────── resnap ─────────────────────────

        private static int Resnap(Ctx c, ComposedPlan plan, Func<float2, float> final, Func<float2, float> was, Func<float2, float> wasField)
        {
            int count = 0;
            // On the ground before the pass: the original ground or any layer's planned ground (a park plans before its Terrain feature).
            const float Tol = 1.5f;   // deliberately raised things (decks, bridges) are on locked ground anyway
            bool OnGround(float3 p) => c.FreeAt(p.xz) && (math.abs(p.y - was(p.xz)) <= Tol || math.abs(p.y - c.Orig(p.xz)) <= Tol
                || (wasField != null && math.abs(p.y - wasField(p.xz)) <= Tol));
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (!c.Part[li] || l == null) continue;
                foreach (var o in l.Objects)
                    if (o != null && OnGround(o.Position)) { o.Position.y = final(o.Position.xz); count++; }
                foreach (var s in l.Surfaces)
                    if (s?.Ring != null) for (int v = 0; v < s.Ring.Length; v++) if (OnGround(s.Ring[v])) s.Ring[v].y = final(s.Ring[v].xz);
                foreach (var a in l.Areas)
                    if (a?.Ring != null) for (int v = 0; v < a.Ring.Length; v++) if (OnGround(a.Ring[v])) a.Ring[v].y = final(a.Ring[v].xz);
                foreach (var n in l.Nets)
                {
                    if (!IsPathNet(n)) continue;
                    if (!c.FreeAt(n.A.xz) && !c.FreeAt(n.D.xz)) continue;
                    n.A.y = final(n.A.xz); n.D.y = final(n.D.xz);
                    var fit = CourseFitting.FitProfile(new Cubic(n.A, n.B, n.C, n.D), final);
                    n.B.y = fit.B.y; n.C.y = fit.C.y;
                }
            }
            return count;
        }

        // ───────────────────────── rasters ─────────────────────────

        private static TerraformRaster BuildRaster(List<(float2[] ring, float target, int layer)> items, List<float[]> vh, Func<float2, float> orig)
        {
            if (items == null || items.Count == 0) return null;
            return TerraformRaster.Build(items, RasterCell, RasterBank, 1 << 21, vh, orig);
        }

        private static float S(TerraformRaster r, Func<float2, float> orig, float2 p) =>
            r != null && r.TrySample(p, orig, out float h) ? h : orig(p);

        /// <summary>The ground the build leaves: every layer's terraform through the build's raster (1.75 m, 4 m bank).</summary>
        public static Func<float2, float> FinalGround(ComposedPlan plan, Func<float2, float> original)
        {
            var items = new List<(float2[] ring, float target, int layer)>();
            var vh = new List<float[]>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (l == null) continue;
                foreach (var t in l.Terraform)
                {
                    if (!Valid(t)) continue;
                    items.Add((Poly.To2D(t.Ring), t.TargetHeight, li));
                    vh.Add(TerraformSurface.VertexHeights(t));
                }
            }
            var r = BuildRaster(items, vh, original);
            if (r == null) return original;
            return p => S(r, original, p);
        }

        // ───────────────────────── stamping helpers ─────────────────────────

        private static void LockRing(Ctx c, float2[] ring, float margin)
        {
            Poly.Bounds(ring, out var mn, out var mx);
            c.ForNodes(mn - margin, mx + margin, k =>
            {
                if (c.Lock[k]) return;
                var p = c.Pos(k);
                if (Poly.Contains(ring, p) || (margin > 0f && Poly.DistanceToBoundary(ring, p) <= margin)) c.Lock[k] = true;
            });
        }

        private static void LockCurve(Ctx c, PlannedNet n, float radius) => StampCurve(c, n, radius, (k, d) => c.Lock[k] = true);

        /// <summary>Calls f(node, distance) for nodes within <paramref name="reach"/> of the net's curve (distance to the polyline of 16 chords).</summary>
        private static void StampCurve(Ctx c, PlannedNet n, float reach, Action<int, float> f)
        {
            var pts = new float2[17];
            for (int i = 0; i <= 16; i++) pts[i] = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz;
            float2 mn = pts[0], mx = pts[0];
            foreach (var q in pts) { mn = math.min(mn, q); mx = math.max(mx, q); }
            c.ForNodes(mn - reach, mx + reach, k =>
            {
                var p = c.Pos(k);
                float best = float.MaxValue;
                for (int i = 1; i <= 16; i++) best = math.min(best, math.sqrt(ReservedSpace.DistSqToSegment(p, pts[i - 1], pts[i])));
                if (best <= reach) f(k, best);
            });
        }

        /// <summary>f(node, distance outside the ring; 0 inside) for nodes inside the ring or within <paramref name="reach"/> of it.</summary>
        private static void StampRing(Ctx c, float2[] ring, float reach, Action<int, float> f)
        {
            Poly.Bounds(ring, out var mn, out var mx);
            c.ForNodes(mn - reach, mx + reach, k =>
            {
                var p = c.Pos(k);
                if (Poly.Contains(ring, p)) { f(k, 0f); return; }
                if (reach <= 0f) return;
                float d = Poly.DistanceToBoundary(ring, p);
                if (d <= reach) f(k, d);
            });
        }

        public static float Smooth01(float t)
        {
            t = math.saturate(t);
            return t * t * (3f - 2f * t);
        }

        // ───────────────────────── metrics ─────────────────────────

        /// <summary>
        /// Quality of the ground the build leaves under the participating layers: curtains (steps of the change between
        /// adjacent raster cells), path cross-slope and along-grade, pad flatness, objects on the final ground, furniture
        /// slope. Works on any composed plan (with the pass disabled it measures the old behaviour).
        /// </summary>
        public static HarmonyMetrics Measure(ComposedPlan plan, SiteContext site)
        {
            var m = new HarmonyMetrics();
            if (plan == null || site?.HeightAt == null) return m;
            var c = Prepare(plan, site, true);
            if (c == null) return m;
            var orig = site.HeightAt;
            var F = FinalGround(plan, orig);
            // Curtains: the CHANGE (final − original) between neighbouring raster cells, inside the site.
            float2 mn = float.MaxValue, mx = float.MinValue;
            foreach (var r in c.Rings) foreach (var q in r) { mn = math.min(mn, q); mx = math.max(mx, q); }
            float st = RasterCell;
            bool In(float2 p) { if (!c.FreeAt(p)) return false; foreach (var r in c.Rings) if (Poly.Contains(r, p) && Poly.DistanceToBoundary(r, p) > 1f) return true; return false; }
            // Curtains are judged more than 3 m inside: at the outline the change must meet the untouched ground (≤ 1 : 3).
            // ... and more than 4 m from ground other designs keep (a pond basin and its bank, a quay, a lot): theirs to judge.
            var lockedDist = LockedDistance(c);
            bool Deep(float2 p, float by = 3f)
            {
                int k = c.Nearest(p);
                if (k < 0 || lockedDist[k] < 4f + c.C) return false;
                foreach (var r in c.Rings) if (Poly.Contains(r, p) && Poly.DistanceToBoundary(r, p) > by) return true;
                return false;
            }
            for (float y = mn.y; y <= mx.y; y += st)
                for (float x = mn.x; x <= mx.x; x += st)
                {
                    var p = new float2(x, y);
                    if (!In(p) || !Deep(p)) continue;
                    float dp = F(p) - orig(p);
                    foreach (var q in new[] { p + new float2(st, 0), p + new float2(0, st) })
                    {
                        if (!In(q) || !Deep(q)) continue;
                        float j = math.abs(F(q) - orig(q) - dp);
                        if (j > m.MaxJump) { m.MaxJump = j; if (j > 0.8f) m.Worst = $"jump at {p}"; }
                    }
                }
            // Paths: cross-slope at the edges, along-grade over 4 m; junctions (shared ends) excepted.
            var ends = new List<(float2 p, float r)>();
            var paths = CollectPaths(c, plan, site);
            foreach (var pn in paths) { ends.Add((pn.Net.A.xz, pn.Hw + JunctionZone)); ends.Add((pn.Net.D.xz, pn.Hw + JunctionZone)); }
            // X crossings (no shared node) are junctions too.
            var polys = new List<(float2[] pts, float2 mn, float2 mx, float hw)>();
            foreach (var pn in paths)
            {
                var pts = new float2[25];
                for (int i = 0; i <= 24; i++) pts[i] = Composer.Bezier(pn.Net.A, pn.Net.B, pn.Net.C, pn.Net.D, i / 24f).xz;
                float2 lo = pts[0], hi = pts[0];
                foreach (var q in pts) { lo = math.min(lo, q); hi = math.max(hi, q); }
                polys.Add((pts, lo, hi, pn.Hw));
            }
            for (int a = 0; a < polys.Count; a++)
                for (int b = a + 1; b < polys.Count; b++)
                {
                    var A = polys[a]; var B = polys[b];
                    if (math.any(A.mn > B.mx) || math.any(B.mn > A.mx)) continue;
                    for (int i = 1; i < A.pts.Length; i++)
                        for (int j = 1; j < B.pts.Length; j++)
                            if (Poly.SegmentIntersection(A.pts[i - 1], A.pts[i], B.pts[j - 1], B.pts[j], out var x, out _, out _))
                                ends.Add((x, math.max(A.hw, B.hw) + JunctionZone));
                }
            var cross = new List<float>();
            var padRings = new List<float2[]>();
            for (int li = 0; li < plan.Layers.Count; li++)
                if (c.Part[li] && plan.Layers[li] != null)
                    foreach (var s in plan.Layers[li].Surfaces) if (IsPadSurface(s)) padRings.Add(Poly.To2D(s.Ring));
            foreach (var pn in paths)
            {
                var net = pn.Net;
                var cubic = new Cubic(net.A, net.B, net.C, net.D);
                float len = cubic.LengthXZ(32);
                int steps = math.max(2, (int)math.ceil(len / 1.5f));
                float prevH = float.NaN; float2 prevP = 0; float acc = 0f;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    var p = cubic.Position(t).xz;
                    var tan = cubic.Tangent(t).xz;
                    if (math.lengthsq(tan) < 1e-8f) continue;
                    tan = math.normalize(tan);
                    var nrm = new float2(-tan.y, tan.x);
                    bool nearEnd = false;
                    foreach (var e in ends) if (math.distance(e.p, p) < e.r) { nearEnd = true; break; }
                    bool inside = c.FreeAt(p) && c.FreeAt(p + nrm * pn.Hw) && c.FreeAt(p - nrm * pn.Hw) && In(p);
                    // On a pad / plaza the surface's plane is the ground (its tilt is judged as a pad).
                    if (inside) foreach (var pr in padRings) if (Poly.Contains(pr, p)) { inside = false; break; }
                    if (inside && !nearEnd)
                    {
                        float cs = math.abs(F(p + nrm * pn.Hw) - F(p - nrm * pn.Hw)) / (2f * pn.Hw);
                        cross.Add(cs);
                        if (cs > m.MaxPathCross) { m.MaxPathCross = cs; if (cs > 0.02f) m.Worst += $" cross {cs:0.000} at {p}"; }
                        m.PathSamples++;
                    }
                    // Junction blends are judged as junctions; within 6 m of the outline a walk meets the street at its height.
                    if (!inside || nearEnd || !Deep(p, 6f)) { prevH = float.NaN; continue; }
                    if (float.IsNaN(prevH)) { prevH = F(p); prevP = p; acc = 0f; continue; }
                    acc = math.distance(prevP, p);
                    if (acc >= 4f)
                    {
                        float h = F(p);
                        float gr = math.abs(h - prevH) / acc;
                        m.MaxPathGrade = math.max(m.MaxPathGrade, gr);
                        m.MaxPathGradeRatio = math.max(m.MaxPathGradeRatio, gr / pn.Grade);
                        prevH = h; prevP = p;
                    }
                }
            }
            if (cross.Count > 0) { cross.Sort(); m.P95PathCross = cross[(int)math.floor(0.95f * (cross.Count - 1))]; }
            // Pads: graded surfaces (grouped like the pass groups them) are planes within tolerance, judged more than 1 m inside
            // and off the paths' level bands. Not pads: large or long ones (smoothed), ones a path runs into (part of the path),
            // ones at the outline (they meet the street / neighbour as it is).
            var cubics = new List<(Cubic cu, float band)>();
            foreach (var pn in paths) cubics.Add((new Cubic(pn.Net.A, pn.Net.B, pn.Net.C, pn.Net.D), pn.Hw + Shoulder + 0.75f * c.C + 1f));
            bool InBand(float2 p) { foreach (var q in cubics) if (q.cu.DistanceXZ(p, out _, 16) < q.band) return true; return false; }
            var padList = new List<Surf>();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                if (!c.Part[li] || plan.Layers[li] == null) continue;
                foreach (var s in plan.Layers[li].Surfaces)
                {
                    if (!IsPadSurface(s)) continue;
                    var ring = Poly.To2D(s.Ring);
                    Poly.Bounds(ring, out var bmn, out var bmx);
                    padList.Add(new Surf { rings = new[] { ring }, area = Poly.Area(ring), mn = bmn, mx = bmx, role = s.Role });
                }
            }
            foreach (var grp in GroupPads(padList))
            {
                if (grp.area > LargeSurfaceArea || grp.Extent > LongSurface) continue;
                bool atEdge = false;
                foreach (var ring in grp.rings) foreach (var q in ring) foreach (var rr in c.Rings) if (Poly.DistanceToBoundary(rr, q) <= 3f) atEdge = true;
                if (atEdge) continue;
                var pts = new List<float3>();
                bool touchesPath = false;
                for (float y = grp.mn.y + 0.5f; y < grp.mx.y; y += 1f)
                    for (float x = grp.mn.x + 0.5f; x < grp.mx.x; x += 1f)
                    {
                        var p = new float2(x, y);
                        if (!Inside(grp.rings, p)) continue;
                        if (InBand(p)) { touchesPath = true; continue; }
                        bool deep = Inside(grp.rings, p + new float2(1f, 0f)) && Inside(grp.rings, p - new float2(1f, 0f))
                            && Inside(grp.rings, p + new float2(0f, 1f)) && Inside(grp.rings, p - new float2(0f, 1f));
                        if (In(p) && deep) pts.Add(new float3(p.x, F(p), p.y));
                    }
                if (touchesPath) continue;
                var cen = Centroid(grp.rings);
                if (pts.Count < 4 || !FitPlane(pts, cen, 1f, out float a0, out float2 g)) continue;
                float dev = 0f;
                foreach (var q in pts) dev = math.max(dev, math.abs(a0 + math.dot(g, q.xz - cen) - q.y));
                m.MaxPadDeviation = math.max(m.MaxPadDeviation, dev);
                m.MaxPadTilt = math.max(m.MaxPadTilt, math.length(g));
                m.Pads++;
            }
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li];
                if (!c.Part[li] || l == null) continue;
                foreach (var o in l.Objects)
                {
                    if (o == null || o.Role == ObjectRole.Decal || !In(o.Position.xz)) continue;
                    float off = math.abs(o.Position.y - F(o.Position.xz));
                    if (off > 2.5f) continue;   // deliberately elevated (deck props)
                    m.MaxObjectOff = math.max(m.MaxObjectOff, off);
                    if (!IsFurniture(o)) continue;
                    // Within 3 m of the outline the ground must meet the street / neighbour as it is: not judged.
                    bool edge = false;
                    foreach (var rr in c.Rings) if (Poly.Contains(rr, o.Position.xz) && Poly.DistanceToBoundary(rr, o.Position.xz) < 3f) edge = true;
                    if (edge) continue;
                    float r = math.max(0.75f, math.min(2f, FootprintRadius(o, site)));
                    float gr = Layers.Common.TerrainSurvey.Grade(F, o.Position.xz, r);
                    m.MaxFurnitureGrade = math.max(m.MaxFurnitureGrade, gr);
                    if (gr > FurnitureGrade) m.FurnitureSteep++;
                    m.Furniture++;
                }
            }
            return m;
        }
    }
}
