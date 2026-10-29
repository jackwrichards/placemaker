// Placemaker — keep the city's zoned buildings off a build, and give its zoning cutouts exactly their cells (s19).
//
// Playtest: zoned apartment blocks grew right over a plaza; only the cutouts were meant for buildings. CS2
// (decompiled 1.6.2f1, RESEARCH-NOTES-ADDENDUM 67–72): every road owns zone blocks of 8 m cells up to 6 deep,
// and a cell only becomes unzonable when Game.Zones.CellBlockJobs marks it Blocked — under ground nets with
// CompositionState.BlockZone and under areas whose prefab has GeometryFlags.PhysicalGeometry (lots without
// m_AllowOverlap) or a native ProtectedArea. Our owner lot allows overlap (not physical), surfaces and spaces
// are never physical, so the road blocks stayed zonable under the whole park and zoning the player had painted
// before (or paints later) grew buildings on it. Cells behind a blocked cell are blocked as well
// (CleanBlockedCells), and row 0 is blocked when row 1 is.
//
// The fix computed here (pure): ZONE BLOCKERS = the build's footprint minus the zoned cells (minus existing
// building lots), emitted by the game layer as invisible physical lots after the build is applied; and the
// zoning snapped to the real cells of the captured road blocks: per block column, rows 0 … the deepest cell
// whose centre lies in a cutout quad (the zoning tool's own Marquee rule), at least 2 rows deep (vanilla blocks
// a lone row 0 behind a blocked row 1). Quads whose road has no captured block (a lane this build adds) keep
// their quad and are kept free of blockers as polygons.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public enum ZoneCellState : byte { Zoned = 0, Blocked = 1, Kept = 2 }

    /// <summary>A road zone cell the build zones, blocks, or keeps (existing building) — for the preview.</summary>
    public struct ZoneCellMark
    {
        public float2 A, B, C, D;
        public ZoneCellState State;
        /// <summary>Ground height at the cell centre (set by Apply; 0 from Compute).</summary>
        public float Height;
        /// <summary>Zone prefab of a zoned cell ("" otherwise).</summary>
        public string ZoneId;
        /// <summary>
        /// (s22) Blocked cells: the part of the cell inside the build's footprint (the preview never draws outside the
        /// outline). Null for zoned / kept cells (drawn whole).
        /// </summary>
        public float2[] Clipped;
    }

    /// <summary>(s26) A cutout quad as planned (before ZoneGuard snapped it to the captured cells).</summary>
    public struct ZoneQuad
    {
        public float2[] Quad;
        public string ZoneId;
    }

    public static class ZoneGuard
    {
        /// <summary>Game setting "Keep zoned buildings off builds" (default on). Off = the s14 behaviour (no blockers, quads as planned).</summary>
        public static bool Enabled = true;

        public const float Cell = 8f;
        /// <summary>The blocker keeps this far inside the footprint (off kerbs and sidewalks at the outline).</summary>
        public const float FootprintInset = 0.5f;
        /// <summary>
        /// Distance from the blocker to zoned cells. The game blocks a cell when its quad shrunk 0.02 m touches a
        /// blocker triangle; ring repair (AreaRings, d = 0.375 m) may move the blocker edge by about 0.4 m.
        /// </summary>
        public const float KeepMargin = 0.5f;
        /// <summary>Distance from the blocker to existing building lots (they must keep their cells).</summary>
        public const float LotMargin = 0.5f;
        /// <summary>The zoning quad of a run of cells is shrunk this much: the Marquee tool takes cells by centre.</summary>
        public const float QuadShrink = 1f;
        /// <summary>Smallest blocker piece worth a lot (m²).</summary>
        public const float MinBlockerArea = 4f;
        /// <summary>Minimum zoned depth in cells (vanilla blocks row 0 when row 1 is blocked).</summary>
        public const int MinRows = 2;
        private const int MaxGridCells = 64 * 64;

        /// <summary>One zoned run: a rectangle of cells of one block, zoned with the zone of a source quad.</summary>
        public struct ZonedRect
        {
            public int Quad;          // index into the input quads (its layer and zone)
            public float2[] Corners;  // marquee quad (cells shrunk by QuadShrink), CCW
        }

        public sealed class Result
        {
            public readonly List<float2[]> Blockers = new List<float2[]>();
            public readonly List<ZoneCellMark> Cells = new List<ZoneCellMark>();
            public readonly List<ZonedRect> Zoning = new List<ZonedRect>();
            /// <summary>Per input quad: true when it was replaced by cell rects (false = keep the quad as planned).</summary>
            public bool[] Snapped = Array.Empty<bool>();
            /// <summary>Squares of the zoned cells (park content must stay off them).</summary>
            public readonly List<float2[]> ZonedSquares = new List<float2[]>();
            /// <summary>Blocker regions (may have holes) — the exact area the blockers cover.</summary>
            public readonly List<Region> BlockerRegions = new List<Region>();
            /// <summary>(s54) Zoned cells per input quad.</summary>
            public int[] CellsPerQuad = Array.Empty<int>();
        }

        // ───────────────────────── cell geometry (Game.Zones.ZoneUtils) ─────────────────────────

        /// <summary>MathUtils.Right: the block's column axis.</summary>
        public static float2 Right(float2 d) => new float2(d.y, -d.x);

        /// <summary>Centre of cell (column x, row y); row 0 is next to the road (ZoneUtils.GetCellPosition).</summary>
        public static float2 CellCentre(ZoneGrid g, int x, int y)
        {
            float2 d = math.normalizesafe(g.Direction);
            float2 o = new float2(g.Size.x - 2 * x - 1, g.Size.y - 2 * y - 1) * (Cell * 0.5f);
            return g.Position + d * o.y + Right(d) * o.x;
        }

        /// <summary>Square of a cell, grown by <paramref name="grow"/> (negative shrinks), CCW.</summary>
        public static float2[] CellSquare(ZoneGrid g, int x, int y, float grow = 0f)
        {
            float2 d = math.normalizesafe(g.Direction), r = Right(d), c = CellCentre(g, x, y);
            float h = Cell * 0.5f + grow;
            return Poly.EnsureCcw(new[] { c + d * h + r * h, c + d * h - r * h, c - d * h - r * h, c - d * h + r * h });
        }

        /// <summary>Rectangle of cells [x0..x1] × [y0..y1] grown by <paramref name="grow"/>, CCW.</summary>
        public static float2[] CellRect(ZoneGrid g, int x0, int x1, int y0, int y1, float grow = 0f)
        {
            float2 d = math.normalizesafe(g.Direction), r = Right(d);
            float h = Cell * 0.5f;
            // Local offsets of the centres (u along Right, v along Direction), see CellCentre.
            float u0 = (g.Size.x - 2 * x1 - 1) * h - h - grow, u1 = (g.Size.x - 2 * x0 - 1) * h + h + grow;
            float v0 = (g.Size.y - 2 * y1 - 1) * h - h - grow, v1 = (g.Size.y - 2 * y0 - 1) * h + h + grow;
            var p = g.Position;
            return Poly.EnsureCcw(new[] { p + r * u0 + d * v0, p + r * u1 + d * v0, p + r * u1 + d * v1, p + r * u0 + d * v1 });
        }

        private static bool Usable(ZoneGrid g) =>
            math.all(math.isfinite(g.Position)) && math.all(math.isfinite(g.Direction)) && math.lengthsq(g.Direction) > 1e-6f
            && g.Size.x > 0 && g.Size.y > 0 && g.Size.x * g.Size.y <= MaxGridCells;

        // ───────────────────────── plan entry ─────────────────────────

        /// <summary>
        /// End of Composer.Compose (after SiteSafety): computes the blockers and the cell preview, replaces the
        /// zoning quads that lie on captured road blocks by exact cell rectangles, and clears park content off
        /// zoned cells that reach past a cutout.
        /// </summary>
        public static void Apply(ComposedPlan plan, SiteContext site)
        {
            if (plan == null) return;
            plan.ZoneBlockers.Clear();
            plan.ZoneCells.Clear();
            plan.KerbBlockers.Clear();
            if (!Enabled || plan.Footprints == null || plan.Footprints.Count == 0) return;

            var quads = new List<float2[]>();
            var ids = new List<string>();
            var owners = new List<(AreaPlan layer, PlannedZoning z)>();
            foreach (var l in plan.Layers)
            {
                if (l?.Zoning == null) continue;
                foreach (var z in l.Zoning)
                {
                    if (z == null) continue;
                    quads.Add(Poly.EnsureCcw(new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz }));
                    ids.Add(z.ZoneId ?? "");
                    owners.Add((l, z));
                }
            }
            plan.ZoneQuads.Clear();
            for (int i = 0; i < quads.Count; i++) plan.ZoneQuads.Add(new ZoneQuad { Quad = quads[i], ZoneId = ids[i] });
            // (s26) Roads this build adds get the zone blocks the game will give them (cutouts facing a lane snap to
            // them); the build's own nets block the cells under them (and the game every cell behind).
            // Captured blocks and the predicted blocks of the lanes this build adds. (s40: hosts plan their plots on the same
            // grids — ZoneLots.SiteGrids; without any capture, as in tests and the gallery, the existing streets' blocks are
            // only predicted there, and the guard keeps the plots' quads.)
            var grids = new List<ZoneGrid>();
            if (site?.ZoneGrids != null) grids.AddRange(site.ZoneGrids);
            grids.AddRange(PredictedGrids(plan, site));
            var own = OwnNets(plan, site);
            Result r;
            try
            {
                r = Compute(plan.Footprints, quads, ids, plan.Protected, grids, site?.NearbyNets, true, own);
                // (s26) A plot no road block reaches (blocks exist around it: we know the grid) is not a cutout: it
                // would be drawn floating and never zone. Say so and plan again without it — its land is blocked
                // like the rest of the build.
                bool dropped = false;
                for (int i = quads.Count - 1; i >= 0; i--)
                {
                    if (r.Snapped[i] || !Knowable(quads[i], site, grids)) continue;
                    var c = Poly.Centroid(quads[i]);
                    // A plot facing two roads gets a quad per road; the one the other road's cells already cover is
                    // simply redundant.
                    bool covered = false;
                    foreach (var sq in r.ZonedSquares) if (Poly.Contains(quads[i], Poly.Centroid(sq))) { covered = true; break; }
                    if (!covered)
                        plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = NotZonableKey, At = new float3(c.x, site?.HeightAt?.Invoke(c) ?? 0f, c.y) });
                    owners[i].layer.Zoning.Remove(owners[i].z);
                    quads.RemoveAt(i); ids.RemoveAt(i); owners.RemoveAt(i);
                    plan.ZoneQuads.RemoveAt(i);
                    dropped = true;
                }
                if (dropped) r = Compute(plan.Footprints, quads, ids, plan.Protected, grids, site?.NearbyNets, true, own);
            }
            catch (Exception)
            {
                // Never fail a plan over the guard: build as before (no blockers, quads unchanged).
                return;
            }

            // Zoning: snapped quads are replaced by their cell rectangles (in the quad's layer, so loose/grouped stays).
            Func<float2, float> h = site?.HeightAt ?? (_ => 0f);
            for (int i = 0; i < owners.Count; i++)
                if (r.Snapped[i]) owners[i].layer.Zoning.Remove(owners[i].z);
            foreach (var zr in r.Zoning)
            {
                var c = zr.Corners;
                owners[zr.Quad].layer.Zoning.Add(new PlannedZoning
                {
                    A = new float3(c[0].x, h(c[0]), c[0].y),
                    B = new float3(c[1].x, h(c[1]), c[1].y),
                    C = new float3(c[2].x, h(c[2]), c[2].y),
                    D = new float3(c[3].x, h(c[3]), c[3].y),
                    ZoneId = ids[zr.Quad],
                });
            }

            // Zoned cells reaching past a cutout (a cell centre inside, its square partly outside; the front row
            // between the outline and the road) must be as empty as the cutout itself.
            var cuts = Layers.Common.Cutouts.Of(plan);
            var extra = new List<float2[]>();
            foreach (var sq in r.ZonedSquares)
            {
                bool inside = false;
                foreach (var c in cuts)
                {
                    bool all = true;
                    foreach (var p in Shrink(sq, 0.3f)) if (!Poly.Contains(c, p)) { all = false; break; }
                    if (all) { inside = true; break; }
                }
                if (!inside) extra.Add(sq);
            }
            if (extra.Count > 0) Layers.Common.Cutouts.Enforce(plan, extra, plan.Footprints);

            plan.ZoneBlockers.AddRange(r.Blockers);
            foreach (var c in r.Cells)
            {
                var m = c;
                m.Height = h(0.25f * (c.A + c.B + c.C + c.D));
                plan.ZoneCells.Add(m);
            }
            try { plan.KerbBlockers.AddRange(KerbStrips(plan, site)); } catch (Exception) { /* never fail a plan over it */ }
        }

        /// <summary>Any corner or the middle of <paramref name="strip"/> inside one of the plots' quads.</summary>
        private static bool OnAPlot(float2[] strip, List<float2[]> quads)
        {
            var mid = (strip[0] + strip[1] + strip[2] + strip[3]) * 0.25f;
            foreach (var q in quads)
            {
                if (Poly.Contains(q, mid)) return true;
                foreach (var p in strip) if (Poly.Contains(q, p)) return true;
            }
            return false;
        }

        /// <summary>Width (m) of a kerb strip, and how far inside the footprint every corner must lie.</summary>
        public const float KerbWidth = 2f, KerbInset = 0.5f;

        /// <summary>
        /// (core) Kerb strips (see <see cref="ComposedPlan.KerbBlockers"/>): for every captured block of an existing street, each run of
        /// columns whose first rows lie in the build and that zone no plot planned on that street's own cells gets one thin strip in
        /// row 0 (or row 1, the shallowest band fully inside the build). CellBlockJobs blocks a cell under a physical lot and every
        /// cell behind it, and row 0 when row 1 is (CleanBlockedCells): the whole column dies, nothing outside the build changes.
        /// Only in plans that zone plots.
        /// </summary>
        public static List<float2[]> KerbStrips(ComposedPlan plan, SiteContext site)
        {
            var strips = new List<float2[]>();
            if (plan?.ZoneQuads == null || plan.ZoneQuads.Count == 0 || site?.ZoneGrids == null || plan.Footprints == null) return strips;
            var feet = new List<float2[]>();
            foreach (var f in plan.Footprints) if (f != null && f.Length >= 3) feet.Add(Poly.EnsureCcw(f));
            if (feet.Count == 0) return strips;
            var city = new List<ZoneGrid>();
            foreach (var g in site.ZoneGrids) if (Usable(g) && !g.Predicted && !g.Ours) city.Add(g);
            if (city.Count == 0) return strips;
            var quads = new List<float2[]>();
            var onGrid = new List<int>();
            foreach (var q in plan.ZoneQuads)
            {
                if (q.Quad == null || q.Quad.Length < 3) continue;
                quads.Add(q.Quad);
                onGrid.Add(AlignedGrid(q.Quad, city));
            }
            bool Inside(float2[] poly)
            {
                foreach (var p in poly)
                {
                    bool any = false;
                    foreach (var f in feet) if (Poly.Contains(f, p) && Poly.DistanceToBoundary(f, p) >= KerbInset - 1e-3f) { any = true; break; }
                    if (!any) return false;
                }
                return true;
            }
            for (int gi = 0; gi < city.Count; gi++)
            {
                var g = city[gi];
                float2 d = math.normalizesafe(g.Direction), r = Right(d);
                int sx = g.Size.x, sy = g.Size.y;
                // Per column: the band (offset of its outer edge from the road edge, m) of the strip, or -1.
                var band = new float[sx];
                for (int x = 0; x < sx; x++)
                {
                    band[x] = -1f;
                    bool spared = false;
                    for (int y = 0; y < sy && !spared; y++)
                    {
                        var c = CellCentre(g, x, y);
                        for (int q = 0; q < quads.Count; q++)
                            if (onGrid[q] == gi && Poly.Contains(quads[q], c)) { spared = true; break; }   // a plot planned on this street's cells
                    }
                    if (spared) continue;
                    var c0 = CellCentre(g, x, 0);
                    var edge = c0 + d * (Cell * 0.5f);   // the road side of row 0
                    for (float o = 0.5f; o + KerbWidth <= 2f * Cell - 0.5f + 1e-3f; o += 1f)
                    {
                        var a = edge - d * o + r * (Cell * 0.5f - 0.25f);
                        var b = edge - d * o - r * (Cell * 0.5f - 0.25f);
                        var strip = new[] { a, b, b - d * KerbWidth, a - d * KerbWidth };
                        // (2026-10-28) never on a plot planned on another street's cells: at a corner the side street's strip blocked the
                        // plot's own cells under it (a small strip mall's 24 x 20 m corner plot: "cannot zone (land blocked)")
                        if (Inside(strip) && !OnAPlot(strip, quads)) { band[x] = o; break; }
                        if (Inside(strip)) break;
                    }
                }
                // Runs of neighbouring columns with the same band: one strip each.
                int s = 0;
                while (s < sx)
                {
                    if (band[s] < 0f) { s++; continue; }
                    int e = s;
                    while (e + 1 < sx && math.abs(band[e + 1] - band[s]) < 1e-3f) e++;
                    float o = band[s];
                    var edgeS = CellCentre(g, s, 0) + d * (Cell * 0.5f);
                    var edgeE = CellCentre(g, e, 0) + d * (Cell * 0.5f);
                    var a = edgeS - d * o + r * (Cell * 0.5f - 0.25f);
                    var b = edgeE - d * o - r * (Cell * 0.5f - 0.25f);
                    strips.Add(Poly.EnsureCcw(new[] { a, b, b - d * KerbWidth, a - d * KerbWidth }));
                    s = e + 1;
                }
            }
            return strips;
        }

        /// <summary>(s26) Issue: a planned plot faces no road's zone block (no cell of it can be zoned).</summary>
        public const string NotZonableKey = "Placemaker.Issue.CutoutNotZonable";

        /// <summary>Zone blocks the game will give the roads this plan adds (both sides of every road-class net).</summary>
        public static List<ZoneGrid> PredictedGrids(ComposedPlan plan, SiteContext site)
        {
            var r = new List<ZoneGrid>();
            if (plan?.Layers == null) return r;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                {
                    if (n == null || !ZoneBlockPredictor.Zones(n.Role)) continue;
                    if (site?.Assets != null && !string.IsNullOrEmpty(n.Asset.Id) && site.Assets.TryGetValue(n.Asset.Id, out var ai) && ai.ZoneBlocks == 2) continue;
                    bool leftOff = false, rightOff = false;
                    if (n.Upgrades != null)
                        foreach (var u in n.Upgrades) { if (u == LaneSides.LeftOff) leftOff = true; else if (u == LaneSides.RightOff) rightOff = true; }
                    foreach (var g in ZoneBlockPredictor.Blocks(n.A, n.B, n.C, n.D, NetWidth(n, site)))
                    {
                        // (core) A side switched off (LaneSides, vanilla ZonesDisabled) has no cells in the game.
                        if (leftOff || rightOff)
                        {
                            LaneSides.SideOf(n, g.Position, out int side);
                            if ((side > 0 && rightOff) || (side < 0 && leftOff)) continue;
                        }
                        var p = g;
                        p.Predicted = true;
                        r.Add(p);
                    }
                }
            }
            return r;
        }

        /// <summary>Width of a planned net: the catalog's, else the known vanilla road of its asset (or fallbacks), else the role default.</summary>
        public static float NetWidth(PlannedNet n, SiteContext site)
        {
            var ids = new List<string>();
            if (!string.IsNullOrEmpty(n.Asset.Id)) ids.Add(n.Asset.Id);
            if (n.Asset.Fallbacks != null) foreach (var f in n.Asset.Fallbacks) if (!string.IsNullOrEmpty(f)) ids.Add(f);
            if (site?.Assets != null)
                foreach (var id in ids)
                    if (site.Assets.TryGetValue(id, out var ai) && ai.NetWidth > 0.5f) return ai.NetWidth;
            foreach (var id in ids)
            {
                var k = Layers.Roads.RoadRoles.Find(id);
                if (k != null) return k.Width;
            }
            return Composer.DefaultHalfWidth(n.Role) * 2f;
        }

        /// <summary>The plan's ground nets (paths, lanes, aisles; not fences) as polylines with their half width.</summary>
        public static List<(float2[] line, float half)> OwnNets(ComposedPlan plan, SiteContext site)
        {
            var r = new List<(float2[], float)>();
            if (plan?.Layers == null) return r;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                {
                    if (n == null || n.Role == NetRole.Fence) continue;
                    var line = new float2[9];
                    for (int i = 0; i <= 8; i++) line[i] = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                    r.Add((line, NetWidth(n, site) * 0.5f));
                }
            }
            return r;
        }

        /// <summary>
        /// Do we know the cells this plot could get? Yes when a captured block reaches near it, or when only a road
        /// of this build (predicted blocks) is near and no existing road is (whose blocks were not captured).
        /// </summary>
        private static bool Knowable(float2[] quad, SiteContext site, List<ZoneGrid> grids)
        {
            var captured = new List<ZoneGrid>(); var predicted = new List<ZoneGrid>();
            foreach (var g in grids) (g.Predicted && !g.Existing ? predicted : captured).Add(g);
            if (NearAnyGrid(quad, captured)) return true;
            if (!NearAnyGrid(quad, predicted)) return false;
            var c = Poly.Centroid(quad);
            var nets = site?.NearbyNets;
            if (nets != null)
                foreach (var n in nets)
                {
                    if (n.Category != NetCategory.Road) continue;
                    for (int i = 0; i <= 8; i++)
                        if (math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz, c) < n.Width * 0.5f + 6 * Cell) return false;
                }
            return true;
        }

        private static bool NearAnyGrid(float2[] quad, List<ZoneGrid> grids)
        {
            var c = Poly.Centroid(quad);
            foreach (var g in grids)
            {
                float reach = math.length((float2)g.Size) * Cell * 0.5f + 2f * Cell;
                if (math.distance(g.Position, c) < reach) return true;
            }
            return false;
        }

        // ───────────────────────── s26: refresh against the live blocks after the build ─────────────────────────

        /// <summary>One zoning quad to paint after the refresh.</summary>
        public struct ZoningQuad
        {
            public float2[] Corners;
            public string ZoneId;
        }

        /// <summary>(s54) One line about the last Refresh for the game log: planned vs refreshed cells, grown quads, dropped plots.</summary>
        public static string LastRefreshReport = "";

        /// <summary>(s54) How far a plot quad may grow (m) to find its cells on a live block shifted along the road.</summary>
        private const float RefreshMaxGrow = 4f;
        /// <summary>(s56) How far a plot may shift along the road (half a cell).</summary>
        private const float RefreshMaxShift = 4f;

        /// <summary>
        /// (s26) Playtest: "zoned 4 of 16 cutout cell(s) … blocked 12". The plan snaps cutouts and blockers to the zone
        /// blocks captured at design time — but the build's park paths connect to the streets and SPLIT their edges;
        /// the game then rebuilds the blocks of both pieces with cells aligned to the new edge ends, a metre or two off
        /// the captured grid. The blockers (0.5 m clear of the OLD cells) overlapped the new cells and blocked them;
        /// only a cutout on an edge no path touched survived. After the main Apply the game layer captures the live
        /// blocks (cell flags included: our paths now block their cells for real) and calls this: blockers, zoning
        /// quads and the preview cells are recomputed from the original cutout quads (<see cref="ComposedPlan.ZoneQuads"/>).
        /// Updates plan.ZoneBlockers / plan.ZoneCells; returns the quads to zone (null = nothing to refresh).
        /// </summary>
        public static List<ZoningQuad> Refresh(ComposedPlan plan, IReadOnlyList<ZoneGrid> liveGrids, Func<float2, float> heightAt = null)
        {
            if (plan == null || !Enabled || plan.Footprints == null || plan.Footprints.Count == 0 || liveGrids == null) return null;
            var quads = new List<float2[]>();
            var ids = new List<string>();
            foreach (var q in plan.ZoneQuads)
                if (q.Quad != null && q.Quad.Length >= 3) { quads.Add(q.Quad); ids.Add(q.ZoneId ?? ""); }
            // (s54) Playtest (market square building its own lanes): "10 cutout cells (planned 70)". The lanes' blocks were
            // PREDICTED (ZoneBlockPredictor models free ends; at the junctions the game trims them), so the live cells of the
            // build's own roads sit anywhere along the road, up to half a cell off. The quads are plots of whole planned
            // cells shrunk QuadShrink; cells are taken by centre, so on a shifted grid a plot lost a column or all of
            // them. The planned cells per plot are counted first; every plot then grows (0.25 m steps, ≤ RefreshMaxGrow)
            // until the live cells it takes equal the planned count (closest otherwise).
            var planned = new int[quads.Count];
            foreach (var c in plan.ZoneCells)
            {
                if (c.State != ZoneCellState.Zoned) continue;
                var centre = 0.25f * (c.A + c.B + c.C + c.D);
                for (int i = 0; i < quads.Count; i++) if (Poly.Contains(quads[i], centre)) { planned[i]++; break; }
            }
            Result r;
            var grow = new float[quads.Count];
            var used = new List<float2[]>(quads);
            int nearSnapped = 0;
            try
            {
                r = Compute(plan.Footprints, quads, ids, plan.Protected, liveGrids, null, ownBlockedOpen: false);
                var got = CellsPerQuad(r, quads.Count);
                var best = new int[quads.Count];
                for (int i = 0; i < quads.Count; i++) best[i] = got[i];
                // (s56) Playtest: a mall whose lanes' live blocks moved lost cells on 3 of 11 plots (6->2, 6->2, 8->4) with
                // growing alone. Each deficient plot is re-snapped to the live block: first grown (cells whose centres just
                // missed the shrunk quad), then SHIFTED along the road (+-0.5 ... +-RefreshMaxShift = half a cell, so a
                // live column that moved or a blocked end cell is replaced by the free one beside it), then both.
                var axis = new float2[quads.Count];
                for (int i = 0; i < quads.Count; i++) axis[i] = RoadAxis(quads[i], liveGrids);
                var cand = new List<(float shift, float grow)>();
                for (float g = 0.25f; g <= RefreshMaxGrow + 1e-3f; g += 0.25f) cand.Add((0f, g));
                for (float s = 0.5f; s <= RefreshMaxShift + 1e-3f; s += 0.5f) { cand.Add((s, 0f)); cand.Add((-s, 0f)); }
                for (float s = 0.5f; s <= RefreshMaxShift + 1e-3f; s += 0.5f) { cand.Add((s, 1f)); cand.Add((-s, 1f)); }
                var shift = new float[quads.Count];
                foreach (var (sh, g) in cand)
                {
                    bool open = false;
                    for (int i = 0; i < quads.Count; i++) if (planned[i] > 0 && best[i] != planned[i]) { open = true; break; }
                    if (!open) break;
                    var trial = new List<float2[]>(quads);
                    for (int i = 0; i < quads.Count; i++)
                    {
                        if (planned[i] == 0 || best[i] == planned[i]) continue;
                        trial[i] = Moved(quads[i], axis[i] * sh, g);
                    }
                    var rt = Compute(plan.Footprints, trial, ids, plan.Protected, liveGrids, null, ownBlockedOpen: false);
                    var gt = CellsPerQuad(rt, quads.Count);
                    for (int i = 0; i < quads.Count; i++)
                    {
                        if (planned[i] == 0 || best[i] == planned[i]) continue;
                        if (math.abs(gt[i] - planned[i]) < math.abs(best[i] - planned[i])) { best[i] = gt[i]; grow[i] = g; shift[i] = sh; }
                    }
                }
                for (int i = 0; i < quads.Count; i++) if (grow[i] > 0f || shift[i] != 0f) used[i] = Moved(quads[i], axis[i] * shift[i], grow[i]);
                r = Compute(plan.Footprints, used, ids, plan.Protected, liveGrids, null, ownBlockedOpen: false);
                // (s60) Playtest: a plot with 6 open live cells beside it got none (counts never matched). When counting finds
                // nothing, move the plot onto the open live cells nearest to it: a run of >= 2 columns x 2 rows from row 0 of
                // one block, as many columns as it planned (rows / 2), not on another plot's cells.
                var cur = CellsPerQuad(r, used.Count);
                int snappedNear = 0;
                for (int i = 0; i < used.Count; i++)
                {
                    if (planned[i] == 0 || cur[i] != 0) continue;
                    // (core, teardown 07 F5) A plot planned on one of the build's own lanes moves only along that lane's live cells, never
                    // onto a city street's (that turned lane shops round to face the street).
                    var near = NearestOpenRun(quads[i], planned[i], OwnGridsNear(quads[i], liveGrids) ?? liveGrids, r.ZonedSquares);
                    if (near == null) continue;
                    used[i] = near;
                    snappedNear++;
                    r = Compute(plan.Footprints, used, ids, plan.Protected, liveGrids, null, ownBlockedOpen: false);
                }
                nearSnapped = snappedNear;
                // A plot with no room on the live blocks (no open live cell within half a cell of it) truly cannot zone: its
                // land is returned to the blockers (like ZoneGuard.Apply does at design time) and the preview shows it blocked.
                var drop = new List<int>();
                var causes = new System.Text.StringBuilder();
                for (int i = 0; i < used.Count; i++)
                    if (!r.Snapped[i] && NearAnyGrid(used[i], new List<ZoneGrid>(liveGrids)) && OpenCellsNear(quads[i], liveGrids, Cell * 0.5f + RefreshMaxShift) == 0) drop.Add(i);
                if (drop.Count > 0)
                {
                    for (int k = drop.Count - 1; k >= 0; k--) { int i = drop[k]; used.RemoveAt(i); quads.RemoveAt(i); ids.RemoveAt(i); grow = RemoveAt(grow, i); shift = RemoveAt(shift, i); planned = RemoveAt(planned, i); }
                    r = Compute(plan.Footprints, used, ids, plan.Protected, liveGrids, null, ownBlockedOpen: false);
                }
                var final = CellsPerQuad(r, used.Count);
                for (int i = 0; i < used.Count; i++) if (final[i] != planned[i]) causes.Append($" #{i}: {Diagnose(quads[i], liveGrids)};");
                int p = 0, f = 0, grown = 0;
                for (int i = 0; i < used.Count; i++) { p += planned[i]; f += final[i]; if (grow[i] > 0f || shift[i] != 0f) grown++; }
                LastRefreshReport = $"{p} planned cell(s) in {used.Count} plot(s) -> {f} on the live blocks; {grown} plot(s) matched on a shifted grid; {nearSnapped} moved to the nearest open cells; {drop.Count} plot(s) cannot zone (land blocked)"
                    + (f != p ? MismatchText(planned, final) + causes : ".");
            }
            catch (Exception) { return null; }
            var zoning = new List<ZoningQuad>();
            foreach (var zr in r.Zoning) zoning.Add(new ZoningQuad { Corners = zr.Corners, ZoneId = ids[zr.Quad] });
            for (int i = 0; i < used.Count; i++)
                if (!r.Snapped[i]) zoning.Add(new ZoningQuad { Corners = quads[i], ZoneId = ids[i] });
            plan.ZoneBlockers.Clear();
            plan.ZoneBlockers.AddRange(r.Blockers);
            plan.ZoneCells.Clear();
            var h = heightAt ?? (_ => 0f);
            foreach (var c in r.Cells)
            {
                var m = c;
                m.Height = h(0.25f * (c.A + c.B + c.C + c.D));
                plan.ZoneCells.Add(m);
            }
            return zoning;
        }

        /// <summary>
        /// (s60) The zoning quad (cells shrunk QuadShrink) of the open run of live cells nearest to <paramref name="quad"/>:
        /// at least 2 columns x 2 rows from row 0 of one block, up to the planned cell count (2 rows), free of cells other
        /// plots already take; null when no block has room within 3 cells of the plot.
        /// </summary>
        private static float2[] NearestOpenRun(float2[] quad, int plannedCells, IReadOnlyList<ZoneGrid> grids, IReadOnlyList<float2[]> taken)
        {
            var centre = Poly.Centroid(quad);
            int want = math.max(2, (plannedCells + 1) / 2);
            float bestD = 3f * Cell; float2[] best = null;
            var roads = new List<RoadBand>();
            foreach (var g in grids)
            {
                if (!Usable(g) || g.Size.y < 2) continue;
                var ok = new bool[g.Size.x];
                for (int x = 0; x < g.Size.x; x++)
                {
                    ok[x] = CellOpen(g, x, 0, null, roads) && CellOpen(g, x, 1, null, roads);
                    if (!ok[x]) continue;
                    foreach (var t in taken)
                        if (Poly.Contains(t, CellCentre(g, x, 0)) || Poly.Contains(t, CellCentre(g, x, 1))) { ok[x] = false; break; }
                }
                for (int w = want; w >= 2; w--)
                    for (int x0 = 0; x0 + w <= g.Size.x; x0++)
                    {
                        bool all = true;
                        for (int x = x0; x < x0 + w && all; x++) all = ok[x];
                        if (!all) continue;
                        float d = math.distance(Poly.Centroid(CellRect(g, x0, x0 + w - 1, 0, 1)), centre);
                        // A longer run is preferred at similar distance: closeness counts per missing column.
                        d += (want - w) * 2f;
                        if (d < bestD) { bestD = d; best = CellRect(g, x0, x0 + w - 1, 0, 1, -QuadShrink); }
                    }
            }
            return best;
        }

        /// <summary>(core) The live blocks of the build's own roads near a plot, or null when none is (the plot faces a city street).</summary>
        private static IReadOnlyList<ZoneGrid> OwnGridsNear(float2[] quad, IReadOnlyList<ZoneGrid> grids)
        {
            var own = new List<ZoneGrid>();
            foreach (var g in grids) if (g.Ours) own.Add(g);
            return own.Count > 0 && NearAnyGrid(quad, own) ? own : null;
        }

        private static float2[] Moved(float2[] quad, float2 by, float grow)
        {
            var q = grow > 0f ? Grow(quad, grow) : (float2[])quad.Clone();
            for (int i = 0; i < q.Length; i++) q[i] += by;
            return q;
        }

        /// <summary>(s56) Unit vector along the road (the columns' axis) of the live block nearest the plot.</summary>
        private static float2 RoadAxis(float2[] quad, IReadOnlyList<ZoneGrid> grids)
        {
            var c = Poly.Centroid(quad);
            float best = float.MaxValue; float2 axis = new float2(1, 0);
            foreach (var g in grids)
            {
                if (!Usable(g)) continue;
                float d = math.distance(g.Position, c);
                if (d < best) { best = d; axis = Right(math.normalizesafe(g.Direction)); }
            }
            return axis;
        }

        private static int OpenCellsNear(float2[] quad, IReadOnlyList<ZoneGrid> grids, float margin)
        {
            var g2 = Grow(quad, margin);
            int n = 0;
            foreach (var g in grids)
            {
                if (!Usable(g)) continue;
                for (int x = 0; x < g.Size.x; x++)
                    for (int y = 0; y < g.Size.y; y++)
                        if (CellOpen(g, x, y, null, new List<RoadBand>()) && Poly.Contains(g2, CellCentre(g, x, y))) n++;
            }
            return n;
        }

        /// <summary>(s56) Why a plot lost cells: live cells whose centres lie in it (grown half a cell), by state.</summary>
        private static string Diagnose(float2[] quad, IReadOnlyList<ZoneGrid> grids)
        {
            var g2 = Grow(quad, Cell * 0.5f);
            int open = 0, blocked = 0, hidden = 0, blocks = 0;
            foreach (var g in grids)
            {
                if (!Usable(g)) continue;
                bool any = false;
                for (int x = 0; x < g.Size.x; x++)
                    for (int y = 0; y < g.Size.y; y++)
                    {
                        if (!Poly.Contains(g2, CellCentre(g, x, y))) continue;
                        any = true;
                        byte f = g.Cells != null && x + y * g.Size.x < g.Cells.Length ? g.Cells[x + y * g.Size.x] : (byte)ZoneGrid.CellVisible;
                        if ((f & ZoneGrid.CellBlocked) != 0) blocked++;
                        else if ((f & ZoneGrid.CellVisible) == 0) hidden++;
                        else open++;
                    }
                if (any) blocks++;
            }
            return $"live cells in/near it: {open} open, {blocked} blocked, {hidden} hidden, {blocks} block(s)";
        }

        private static string MismatchText(int[] planned, int[] final)
        {
            var sb = new System.Text.StringBuilder("; MISMATCH (plots ");
            for (int i = 0; i < planned.Length; i++) if (final[i] != planned[i]) sb.Append($"#{i} {planned[i]}->{final[i]} ");
            return sb.Append(").").ToString();
        }

        private static T[] RemoveAt<T>(T[] a, int i)
        {
            var r = new T[a.Length - 1];
            for (int k = 0, j = 0; k < a.Length; k++) if (k != i) r[j++] = a[k];
            return r;
        }

        private static int[] CellsPerQuad(Result r, int quads)
        {
            var n = new int[quads];
            for (int i = 0; i < quads && i < r.CellsPerQuad.Length; i++) n[i] = r.CellsPerQuad[i];
            return n;
        }

        // ───────────────────────── the computation (pure, tested) ─────────────────────────

        /// <summary>
        /// Which cells of the captured road blocks are zoned or blocked for a build footprint, its zoning quads and
        /// the existing building lots, and the blocker rings that make the game block exactly the others.
        /// </summary>
        /// <param name="ownBlockedOpen">
        /// True (design time): a cell the game blocks INSIDE the footprint counts as usable (the build being edited
        /// blocks its own land; it is removed first). False (s26, after the build's Apply): the build's own paths now
        /// block their cells for real, so a blocked cell is closed wherever it is.
        /// </param>
        public static Result Compute(IReadOnlyList<float2[]> footprints, IReadOnlyList<float2[]> quads, IReadOnlyList<string> zoneIds,
            IReadOnlyList<float2[]> protectedLots, IReadOnlyList<ZoneGrid> grids, IReadOnlyList<NearbyNet> nets = null, bool ownBlockedOpen = true,
            IReadOnlyList<(float2[] line, float half)> ownNets = null)
        {
            var result = new Result();
            quads = quads ?? Array.Empty<float2[]>();
            result.Snapped = new bool[quads.Count];
            result.CellsPerQuad = new int[quads.Count];
            // (s32) Quad bounds: the cell loops below test every cell centre against every quad (a mall district has
            // hundreds of plots and blocks); a centre outside a quad's bounds is outside the quad.
            var qb = new float4[quads.Count];
            for (int i = 0; i < quads.Count; i++)
            {
                if (quads[i] == null || quads[i].Length < 3) { qb[i] = new float4(1, 1, -1, -1); continue; }
                Poly.Bounds(quads[i], out var qmn, out var qmx);
                qb[i] = new float4(qmn, qmx);
            }
            bool InQuad(int i, float2 c) => c.x >= qb[i].x && c.x <= qb[i].z && c.y >= qb[i].y && c.y <= qb[i].w && quads[i] != null && quads[i].Length >= 3 && Poly.Contains(quads[i], c);
            var feet = new List<float2[]>();
            if (footprints != null) foreach (var f in footprints) if (f != null && f.Length >= 3) feet.Add(Poly.EnsureCcw(f));
            if (feet.Count == 0) return result;
            var lots = new List<float2[]>();
            if (protectedLots != null) foreach (var p in protectedLots) if (p != null && p.Length >= 3) lots.Add(p);

            Bounds(feet, out var fmin, out var fmax);
            fmin -= 6 * Cell; fmax += 6 * Cell;

            // ── 1. zoned cells per block column ──
            var gridList = new List<ZoneGrid>();
            if (grids != null)
                foreach (var g in grids)
                {
                    if (!Usable(g)) continue;
                    float reach = math.length((float2)g.Size) * Cell * 0.5f + Cell;
                    if (g.Position.x + reach < fmin.x || g.Position.y + reach < fmin.y || g.Position.x - reach > fmax.x || g.Position.y - reach > fmax.y) continue;
                    gridList.Add(g);
                }
            // (s26) The road a plot faces zones it: blocks are tried in the order of the shallowest row at which they
            // reach a cutout (the facing road's row 0/1 before the 4th row of a street behind), captured before
            // predicted, then capture order. Playtest: shops grew off the street BEHIND the plaza's lane.
            if (gridList.Count > 1 && quads.Count > 0)
            {
                var rank = new int[gridList.Count];
                // (s40) Blocks a cell plot was snapped to come first: their plots are zoned exactly as planned.
                var pinned = new bool[gridList.Count];
                for (int q = 0; q < quads.Count; q++) { int ag = AlignedGrid(quads[q], gridList); if (ag >= 0) pinned[ag] = true; }
                for (int gi = 0; gi < gridList.Count; gi++)
                {
                    var g = gridList[gi];
                    int best = 99;
                    for (int x = 0; x < g.Size.x && best > 0; x++)
                        for (int y = 0; y < math.min(g.Size.y, best); y++)
                        {
                            var c = CellCentre(g, x, y);
                            bool hit = false;
                            for (int qi = 0; qi < quads.Count; qi++) if (InQuad(qi, c)) { hit = true; break; }
                            if (hit) { best = y; break; }
                        }
                    // Ties (a plot touching both a street and our lane) go to the build's own roads: the plot was
                    // planned to face its lane.
                    rank[gi] = best * 4 + ((g.Predicted && !g.Existing) || g.Ours ? 0 : 1) - (pinned[gi] ? 1000 : 0);
                }
                var order = new List<int>();
                for (int i = 0; i < gridList.Count; i++) order.Add(i);
                order.Sort((a, b) => rank[a] != rank[b] ? rank[a].CompareTo(rank[b]) : a.CompareTo(b));
                var sorted = new List<ZoneGrid>(gridList.Count);
                foreach (int i in order) sorted.Add(gridList[i]);
                gridList = sorted;
            }
            // (s40) A quad that is exactly a rectangle of one block's cells (a cell plot, zoning first: ZoneLots) is zoned
            // by that block only: at a street corner the crossing street's block must not take the plot's land.
            var quadGrid = new int[quads.Count];
            for (int q = 0; q < quads.Count; q++) quadGrid[q] = AlignedGrid(quads[q], gridList);
            var own = new List<(float2[] line, float half, float2 min, float2 max)>();
            if (ownNets != null)
                foreach (var (line, half) in ownNets)
                {
                    if (line == null || line.Length < 2 || !(half > 0f)) continue;
                    float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                    foreach (var p in line) { lo = math.min(lo, p); hi = math.max(hi, p); }
                    own.Add((line, half, lo - half - Cell, hi + half + Cell));
                }
            var zoned = new List<bool[]>(gridList.Count);          // per grid: [y * Sx + x]
            var zoneQuad = new List<int[]>(gridList.Count);
            var kept = new List<bool[]>(gridList.Count);
            var quadHasCell = new bool[quads.Count];
            var openList = new List<bool[]>(gridList.Count);
            var columnSquares = new List<float2[]>();
            var roads = RoadBands(nets, fmin, fmax);
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var g = gridList[gi];
                int sx = g.Size.x, sy = g.Size.y;
                var z = new bool[sx * sy];
                var zq = new int[sx * sy];
                var k = new bool[sx * sy];
                var open = new bool[sx * sy];
                for (int x = 0; x < sx; x++)
                {
                    // (s22) A column ends at its first closed cell - under a road (the game blocks it), hidden by an
                    // overlapping block (curves), or a captured Blocked cell outside the build - and every cell behind
                    // it is closed too (CleanBlockedCells). Playtest: a column of a neighbouring segment's block ran
                    // across Juniper Street into a cutout and was zoned (and drawn) over the road.
                    int reach = sy;
                    for (int y = 0; y < sy; y++)
                    {
                        if (!CellOpen(g, x, y, ownBlockedOpen ? feet : null, roads) || UnderOwnNet(g, x, y, own)) { reach = y; break; }
                        open[y * sx + x] = true;
                    }
                    var inQuad = new int[sy];
                    var inFoot = new bool[sy];
                    int first = -1, deepest = -1;
                    for (int y = 0; y < sy; y++)
                    {
                        var c = CellCentre(g, x, y);
                        inQuad[y] = -1;
                        inFoot[y] = InAny(feet, c);
                        var sq = CellSquare(g, x, y, -0.05f);
                        foreach (var lot in lots) if (ObstacleCut.Overlaps(sq, lot)) { k[y * sx + x] = true; break; }
                        if (y >= reach) continue;
                        for (int q = 0; q < quads.Count; q++)
                            if ((quadGrid[q] < 0 || quadGrid[q] == gi) && InQuad(q, c)) { inQuad[y] = q; break; }
                        if (inQuad[y] >= 0)
                        {
                            if (first < 0) first = y;
                            deepest = y;
                        }
                    }
                    if (first < 0) continue;
                    // (s26) A column zones ONE plot: its rows end where the first plot ends (a plot behind it, facing
                    // another road, is not reached through the first — that made cutouts "cut into each other").
                    deepest = first;
                    while (deepest + 1 < reach && inQuad[deepest + 1] == inQuad[first]) deepest++;
                    // Rows 0 ... deepest, at least MinRows; the land in front of the first quad row must be outside the
                    // footprint (the strip between the outline and the road), never park; a building lot ends the run.
                    int last = math.min(reach - 1, math.max(deepest, MinRows - 1));
                    int quad = inQuad[first];
                    int end = -1;
                    for (int y = 0; y <= last; y++)
                    {
                        if (k[y * sx + x]) { if (y <= first) end = -1; break; }
                        if (y < first && inFoot[y]) { end = -1; break; }
                        end = y;
                    }
                    if (end < math.min(MinRows - 1, sy - 1)) continue;
                    // Blocks of neighbouring segments may overlap (inside of a curve, junction corners): a piece of
                    // land is zoned once, by the first block that takes it.
                    bool taken = false;
                    for (int y = 0; y <= end && !taken; y++)
                    {
                        // (s26) Any overlap, not only a centre inside: blocks of two roads at a corner cross at angles.
                        var mine = CellSquare(g, x, y, -0.1f);
                        foreach (var sq in columnSquares) if (ObstacleCut.Overlaps(mine, sq)) { taken = true; break; }
                    }
                    if (taken) continue;
                    for (int y = 0; y <= end; y++)
                    {
                        z[y * sx + x] = true;
                        zq[y * sx + x] = inQuad[y] >= 0 ? inQuad[y] : quad;
                        columnSquares.Add(CellSquare(g, x, y, -0.1f));
                    }
                    for (int y = 0; y < sy; y++) if (inQuad[y] >= 0 && y <= end) quadHasCell[inQuad[y]] = true;
                }
                openList.Add(open);
                zoned.Add(z); zoneQuad.Add(zq); kept.Add(k);
            }
            // ── 1a. (s36) at least two columns per plot: the game blocks a lone column (rule below). A plot drawn
            // along a curved road sits at an angle to the cells; its second column may hold less than half a cell. ──
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var g = gridList[gi];
                int sx = g.Size.x, sy = g.Size.y;
                var z = zoned[gi]; var zq = zoneQuad[gi]; var op = openList[gi]; var k = kept[gi];
                for (int x = 0; x < sx; x++)
                {
                    if (!z[x]) continue;
                    int q = zq[x];
                    bool leftSame = x > 0 && z[x - 1] && zq[x - 1] == q, rightSame = x + 1 < sx && z[x + 1] && zq[x + 1] == q;
                    if (leftSame || rightSame) continue;
                    int depth = 0;
                    while (depth < sy && z[depth * sx + x]) depth++;
                    int best = -1; float bestCover = WidenCover;
                    foreach (int nx in new[] { x - 1, x + 1 })
                    {
                        if (nx < 0 || nx >= sx || z[nx]) continue;
                        bool ok = true; float cover = 0f;
                        for (int y = 0; y < depth && ok; y++)
                        {
                            int idx = y * sx + nx;
                            if (!op[idx] || k[idx]) { ok = false; break; }
                            var mine = CellSquare(g, nx, y, -0.1f);
                            foreach (var sq in columnSquares) if (ObstacleCut.Overlaps(mine, sq)) { ok = false; break; }
                            var c = CellCentre(g, nx, y);
                            for (int oq = 0; oq < quads.Count && ok; oq++) if (oq != q && InQuad(oq, c)) ok = false;
                            cover += CellCover(g, nx, y, quads[q], qb[q]);
                        }
                        cover /= math.max(1, depth);
                        if (ok && cover > bestCover) { bestCover = cover; best = nx; }
                    }
                    if (best < 0) continue;
                    for (int y = 0; y < depth; y++)
                    {
                        z[y * sx + best] = true;
                        zq[y * sx + best] = q;
                        columnSquares.Add(CellSquare(g, best, y, -0.1f));
                    }
                }
            }

            // ── 1b. (s36) the game's own depth rule (CellOverlapJobs CalculateLeft/RightDepth + ReduceDepth) ──
            GameDepthPass(gridList, zoned, openList, feet);
            Array.Clear(quadHasCell, 0, quadHasCell.Length);
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var z = zoned[gi]; var zq = zoneQuad[gi];
                for (int i = 0; i < z.Length; i++) if (z[i] && zq[i] >= 0 && zq[i] < quadHasCell.Length) quadHasCell[zq[i]] = true;
            }
            for (int q = 0; q < quads.Count; q++) result.Snapped[q] = quadHasCell[q];

            // ── 2. zoning rectangles: column runs (always from row 0), merged across neighbouring columns ──
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var g = gridList[gi];
                int sx = g.Size.x, sy = g.Size.y;
                var z = zoned[gi]; var zq = zoneQuad[gi];
                int x = 0;
                while (x < sx)
                {
                    int depth = Depth(z, sx, sy, x);
                    if (depth == 0) { x++; continue; }
                    int q = zq[x];
                    int x1 = x;
                    // (s26) One rect per plot (quad): the build logs and checks each plot on its own.
                    while (x1 + 1 < sx && Depth(z, sx, sy, x1 + 1) == depth && zq[x1 + 1] == q) x1++;
                    result.Zoning.Add(new ZonedRect { Quad = q, Corners = CellRect(g, x, x1, 0, depth - 1, -QuadShrink) });
                    if (q >= 0 && q < quads.Count) result.CellsPerQuad[q] += (x1 - x + 1) * depth;
                    for (int xx = x; xx <= x1; xx++)
                        for (int y = 0; y < depth; y++) result.ZonedSquares.Add(CellSquare(g, xx, y));
                    x = x1 + 1;
                }
            }

            // ── 3. blockers: footprint (inset) minus zoned cells, unsnapped quads and building lots ──
            // (s54) Ring repair (CutHoles bridges, PrepareSurface) can move a blocker edge into a kept cell: playtest "3 of
            // the 10 cutout cells touched by a blocker (bug)". The blockers are verified with the game's own test (a cell
            // shrunk 0.03 m toward its centre against the ring); touched -> the keep margin grows, last resort the touching
            // piece is dropped (a little land stays unblocked rather than a zoned cell blocked).
            for (int attempt = 0; attempt < 5; attempt++)
            {
                result.BlockerRegions.Clear();
                result.Blockers.Clear();
                BuildBlockers(feet, result, quads, lots, KeepMargin + 0.3f * attempt);
                if (!TouchesAny(result.Blockers, result.ZonedSquares)) break;
            }
            for (int i = result.Blockers.Count - 1; i >= 0; i--)
                if (TouchesAny(new[] { result.Blockers[i] }, result.ZonedSquares)) result.Blockers.RemoveAt(i);

            // ── 4. what the game will do with every cell near the build (preview / tests) ──
            for (int gi = 0; gi < gridList.Count; gi++)
            {
                var g = gridList[gi];
                int sx = g.Size.x, sy = g.Size.y;
                var z = zoned[gi]; var zq = zoneQuad[gi]; var k = kept[gi]; var op = openList[gi];
                for (int x = 0; x < sx; x++)
                {
                    bool blocked = false;
                    var col = new bool[sy];
                    for (int y = 0; y < sy; y++)
                    {
                        if (!blocked) blocked = Blocks(result.BlockerRegions, CellSquare(g, x, y, -0.02f));
                        col[y] = blocked;   // CleanBlockedCells: every cell behind a blocked one is blocked
                    }
                    if (sy > 1 && col[1]) col[0] = true;   // … and row 0 when row 1 is
                    for (int y = 0; y < sy; y++)
                    {
                        // (s22) Cells under a road or hidden by another block are not the build's business: never
                        // listed, never drawn.
                        if (!op[y * sx + x]) continue;
                        var sq = CellSquare(g, x, y);
                        ZoneCellState state;
                        if (col[y]) state = ZoneCellState.Blocked;
                        else if (z[y * sx + x]) state = ZoneCellState.Zoned;
                        else if (k[y * sx + x] && OverlapsAny(feet, sq)) state = ZoneCellState.Kept;
                        else continue;
                        float2[] clipped = null;
                        if (state == ZoneCellState.Blocked)
                        {
                            // The "blocked" preview stays inside the outline (the game blocks the whole cell, but the
                            // part over the sidewalk is not the build's to show).
                            clipped = ClipToFootprints(sq, feet);
                            if (clipped == null) continue;
                        }
                        result.Cells.Add(new ZoneCellMark
                        {
                            A = sq[0], B = sq[1], C = sq[2], D = sq[3], State = state,
                            ZoneId = state == ZoneCellState.Zoned ? ZoneOf(zoneIds, zq[y * sx + x]) : "",
                            Clipped = clipped,
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>(s54) True when a ring overlaps a kept cell square shrunk 0.03 m toward its centre (the game's blocker test).</summary>
        public static bool TouchesAny(IReadOnlyList<float2[]> rings, IReadOnlyList<float2[]> squares)
        {
            foreach (var sq in squares)
            {
                var c = Poly.Centroid(sq);
                var t = new float2[sq.Length];
                for (int i = 0; i < sq.Length; i++) t[i] = sq[i] + math.normalizesafe(c - sq[i]) * 0.03f;
                foreach (var r in rings) if (ObstacleCut.Overlaps(t, r)) return true;
            }
            return false;
        }

        private static void BuildBlockers(List<float2[]> feet, Result result, IReadOnlyList<float2[]> quads, List<float2[]> lots, float keep)
        {
            var subject = new List<IReadOnlyList<float2>>();
            foreach (var f in feet)
            {
                List<float2[]> inner;
                try { inner = Poly.Offset(f, FootprintInset); } catch (Exception) { inner = null; }
                if (inner != null) foreach (var i in inner) if (i != null && i.Length >= 3) subject.Add(i);
            }
            var holes = new List<IReadOnlyList<float2>>();
            foreach (var sq in result.ZonedSquares) holes.Add(Grow(sq, keep));
            for (int q = 0; q < quads.Count; q++)
                if (!result.Snapped[q] && quads[q] != null && quads[q].Length >= 3) holes.Add(Grow(quads[q], keep));
            foreach (var lot in ObstacleCut.Grow(lots, LotMargin)) holes.Add(lot);
            if (subject.Count == 0) return;
            List<Region> regions;
            try { regions = Poly.Boolean(subject, holes, BoolOp.Difference); }
            catch (Exception) { regions = new List<Region>(); }
            foreach (var reg in regions)
            {
                if (reg?.Outer == null || reg.Outer.Length < 3 || reg.Area < MinBlockerArea) continue;
                result.BlockerRegions.Add(reg);
                List<float2[]> rings;
                try { rings = reg.HasHoles ? Poly.CutHoles(reg) : new List<float2[]> { Poly.EnsureCcw(reg.Outer) }; }
                catch (Exception) { continue; }
                foreach (var ring in rings)
                {
                    if (ring == null || ring.Length < 3 || Poly.Area(ring) < MinBlockerArea) continue;
                    List<float2[]> ok;
                    try { ok = AreaRings.PrepareSurface(ring, AreaRings.SurfaceMinNodeDistance); } catch (Exception) { ok = new List<float2[]>(); }
                    foreach (var o in ok)
                        if (o != null && o.Length >= 3 && Poly.Area(o) >= MinBlockerArea)
                            foreach (var c in ClipToFeet(o, feet)) result.Blockers.Add(Poly.EnsureCcw(c));
                }
            }
        }

        /// <summary>(s57) A blocker never extends past its own build's outline (ring repair / bridges could push an edge out into the street or the neighbour block).</summary>
        private static List<float2[]> ClipToFeet(float2[] ring, List<float2[]> feet)
        {
            var r = new List<float2[]>();
            try
            {
                var subject = new List<IReadOnlyList<float2>> { ring };
                var clip = new List<IReadOnlyList<float2>>();
                foreach (var f in feet) if (f != null && f.Length >= 3) clip.Add(f);
                if (clip.Count == 0) { r.Add(ring); return r; }
                foreach (var reg in Poly.Boolean(subject, clip, BoolOp.Intersection))
                {
                    if (reg?.Outer == null || reg.Outer.Length < 3 || reg.Area < MinBlockerArea) continue;
                    var rings = reg.HasHoles ? Poly.CutHoles(reg) : new List<float2[]> { reg.Outer };
                    foreach (var q in rings) if (q != null && q.Length >= 3 && Poly.Area(q) >= MinBlockerArea) r.Add(q);
                }
            }
            catch (Exception) { r.Clear(); r.Add(ring); }
            return r;
        }

        // ───────────────────────── s22: roads and clipping ─────────────────────────

        /// <summary>A captured road as a polyline with its half width (the zone blocks start at its outer edge).</summary>
        private struct RoadBand
        {
            public float2[] Line;
            public float Half;
            public float2 Min, Max;
        }

        /// <summary>
        /// Cell centres closer than half width + this to a road's centre line count as on the road. Row 0 of a block
        /// lies on the road edge with its centre 4 m out, so it is never caught; a cell overlapping the carriageway
        /// or the sidewalk by more than half a metre is.
        /// </summary>
        public const float RoadCellReach = 3.5f;
        /// <summary>(s60) Extra reach of a net end (junction node) when closing cells under the build's own nets.</summary>
        public const float NodeReach = 1.0f;

        private static List<RoadBand> RoadBands(IReadOnlyList<NearbyNet> nets, float2 min, float2 max)
        {
            var r = new List<RoadBand>();
            if (nets == null) return r;
            foreach (var n in nets)
            {
                // Roads and tracks only: pathways may be the edited build's own (they are replaced), and zone
                // blocks belong to roads.
                if (n.Category != NetCategory.Road && n.Category != NetCategory.Track) continue;
                if (!(n.Width > 0.5f) || !math.all(math.isfinite(n.A)) || !math.all(math.isfinite(n.D))) continue;
                var line = new float2[17];
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                for (int i = 0; i <= 16; i++)
                {
                    float t = i / 16f, u = 1f - t;
                    var p = (u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D).xz;
                    line[i] = p;
                    lo = math.min(lo, p); hi = math.max(hi, p);
                }
                float half = n.Width * 0.5f;
                lo -= half + Cell; hi += half + Cell;
                if (hi.x < min.x || hi.y < min.y || lo.x > max.x || lo.y > max.y) continue;
                r.Add(new RoadBand { Line = line, Half = half, Min = lo, Max = hi });
            }
            return r;
        }

        /// <summary>
        /// (s26) True when one of the build's own nets (paths, lanes: ground nets block the cells under them, and every
        /// cell behind) overlaps the cell square (shrunk 0.02 m, as the game tests it).
        /// </summary>
        private static bool UnderOwnNet(ZoneGrid g, int x, int y, List<(float2[] line, float half, float2 min, float2 max)> own)
        {
            if (own == null || own.Count == 0) return false;
            var c = CellCentre(g, x, y);
            float2 d = math.normalizesafe(g.Direction), r = Right(d);
            const float h = Cell * 0.5f - 0.02f;
            foreach (var n in own)
            {
                if (c.x < n.min.x || c.y < n.min.y || c.x > n.max.x || c.y > n.max.y) continue;
                for (int i = 0; i + 1 < n.line.Length; i++)
                    if (SegmentSquareDistance(n.line[i], n.line[i + 1], c, d, r, h) < n.half - 0.05f) return true;
                // (s60) A net's end is a junction node: the game's node geometry (corners, sidewalks of the connector on the
                // street edge) reaches past the edge's half width, closing cells a path-width test calls open (playtest:
                // 7 blocked cells beside a park's connector, the cutout plot got none). Ends keep NodeReach extra.
                // Walkways only (half width <= 2.5 m): a road lane's block ends are modelled, plots sit flush to them.
                if (n.half <= 2.5f) foreach (var e in new[] { n.line[0], n.line[n.line.Length - 1] })
                    if (SegmentSquareDistance(e, e, c, d, r, h) < n.half + NodeReach) return true;
            }
            return false;
        }

        /// <summary>Distance from a segment to an oriented square (centre c, axes d / r, half size h); 0 when they touch.</summary>
        private static float SegmentSquareDistance(float2 a, float2 b, float2 c, float2 d, float2 r, float h)
        {
            // Local coordinates of the segment; distance from a point to the square = length of the outside excess.
            float2 la = new float2(math.dot(a - c, r), math.dot(a - c, d)), lb = new float2(math.dot(b - c, r), math.dot(b - c, d));
            float best = float.MaxValue;
            int steps = math.clamp((int)math.ceil(math.distance(la, lb) / 0.5f), 1, 400);
            for (int k = 0; k <= steps; k++)
            {
                var p = math.lerp(la, lb, k / (float)steps);
                var e = math.max(math.abs(p) - h, 0f);
                best = math.min(best, math.length(e));
            }
            return best;
        }

        // ───────────────────────── s36: the game's depth rule ─────────────────────────

        /// <summary>A lone column of a plot takes a neighbour column whose cells lie at least this much (share of area) on the plot.</summary>
        public const float WidenCover = 0.2f;

        /// <summary>Share of the cell's area inside <paramref name="quad"/> (convex; 0 when the bounds miss).</summary>
        private static float CellCover(ZoneGrid g, int x, int y, float2[] quad, float4 bounds)
        {
            if (quad == null || quad.Length < 3) return 0f;
            var sq = CellSquare(g, x, y);
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            foreach (var p in sq) { mn = math.min(mn, p); mx = math.max(mx, p); }
            if (mx.x < bounds.x || mx.y < bounds.y || mn.x > bounds.z || mn.y > bounds.w) return 0f;
            var clipped = ClipConvex(sq, Poly.EnsureCcw((float2[])quad.Clone()));
            return clipped == null || clipped.Count < 3 ? 0f : Poly.Area(clipped) / (Cell * Cell);
        }

        /// <summary>Sutherland-Hodgman: <paramref name="subject"/> clipped by the convex CCW <paramref name="clip"/>.</summary>
        private static List<float2> ClipConvex(float2[] subject, float2[] clip)
        {
            var output = new List<float2>(subject);
            for (int i = 0; i < clip.Length && output.Count > 0; i++)
            {
                float2 a = clip[i], b = clip[(i + 1) % clip.Length];
                var input = output;
                output = new List<float2>();
                for (int j = 0; j < input.Count; j++)
                {
                    float2 p = input[j], q = input[(j + 1) % input.Count];
                    float sp = Poly.Cross(b - a, p - a), sq = Poly.Cross(b - a, q - a);
                    if (sp >= 0f) output.Add(p);
                    if ((sp >= 0f) != (sq >= 0f)) output.Add(p + (q - p) * (sp / (sp - sq)));
                }
            }
            return output;
        }

        /// <summary>
        /// (s36) Port of Game.Zones.CellOverlapJobs (the Blocked pass, decompiled 1.6.2f1): after the cells are blocked,
        /// every block evens out its column depths. Walking from both ends at once, column x becomes
        /// CalculateLeftDepth(x) (and CalculateRightDepth from the other end) and ReduceDepth blocks every cell from
        /// that depth on. A column deeper than both neighbours is cut to the deeper neighbour unless a neighbour of
        /// its own depth stands beside it, so a lone 1-column cutout (depth 2 between blocked columns of depth 0) is
        /// blocked entirely: the playtest's "row 0 with row 1 blocked / behind a blocked cell". Neighbour blocks of
        /// the same road continue the row (GetDepth past the end of the block).
        /// <paramref name="depth"/>: per grid, per column, the depth (first blocked row) the game will see.
        /// </summary>
        public static void ReduceDepths(IReadOnlyList<ZoneGrid> grids, List<int[]> depth)
        {
            int Get(int gi, int x)
            {
                var g = grids[gi];
                if (x >= 0 && x < g.Size.x) return depth[gi][x];
                // Past the end of the block: the neighbouring block of the same road whose cell continues the row.
                var at = CellCentre(g, x, 0);
                float2 d = math.normalizesafe(g.Direction);
                for (int hi = 0; hi < grids.Count; hi++)
                {
                    if (hi == gi) continue;
                    var h = grids[hi];
                    if (math.dot(math.normalizesafe(h.Direction), d) < 0.95f) continue;
                    if (math.distance(h.Position, at) > h.Size.x * Cell) continue;
                    for (int hx = 0; hx < h.Size.x; hx++)
                        if (math.distancesq(CellCentre(h, hx, 0), at) < 4f) return depth[hi][hx];
                }
                return 0;
            }
            int Left(int gi, int x)
            {
                int d0 = Get(gi, x - 1), d2 = Get(gi, x);
                if (d2 <= d0) return d2;
                int d3 = Get(gi, x - 2);
                if (d0 != d3 && d0 != 0) return d0;
                int d4 = Get(gi, x + 1);
                if (d4 - d2 < d2 - d0) return math.min(math.max(d0, d4), d2);
                if (Get(gi, x + 2) != d4) return math.min(math.max(d0, d4), d2);
                return d0;
            }
            int Right(int gi, int x)
            {
                int d0 = Get(gi, x + 1), d2 = Get(gi, x);
                if (d2 <= d0) return d2;
                int d3 = Get(gi, x + 2);
                if (d0 != d3 && d0 != 0) return d0;
                int d4 = Get(gi, x - 1);
                if (d4 - d2 < d2 - d0) return math.min(math.max(d4, d0), d2);
                if (Get(gi, x - 2) != d4) return math.min(math.max(d4, d0), d2);
                return d0;
            }
            for (int gi = 0; gi < grids.Count; gi++)
            {
                int n = grids[gi].Size.x;
                int a = 0, b = n - 1;
                while (b >= 0)
                {
                    int l = Left(gi, a);
                    if (l < depth[gi][a]) depth[gi][a] = l;
                    int r = Right(gi, b);
                    if (r < depth[gi][b]) depth[gi][b] = r;
                    a++; b--;
                }
            }
        }

        /// <summary>
        /// Applies <see cref="ReduceDepths"/> to the planned zoning: the depth the game sees per column is the first
        /// row that is closed or will be under a blocker (inside the footprint and not zoned). Zoned columns the rule
        /// cuts shallower keep only the rows above the cut, or go entirely when fewer than <see cref="MinRows"/> rows
        /// remain; repeated until stable (a removed column changes its neighbours).
        /// </summary>
        private static void GameDepthPass(List<ZoneGrid> grids, List<bool[]> zoned, List<bool[]> open, List<float2[]> feet)
        {
            if (grids.Count == 0) return;
            var inner = new List<float2[]>();
            foreach (var f in feet)
            {
                try { foreach (var i in Poly.Offset(f, FootprintInset)) if (i != null && i.Length >= 3) inner.Add(i); }
                catch (Exception) { inner.Add(f); }
            }
            var underBuild = new List<bool[]>();
            for (int gi = 0; gi < grids.Count; gi++)
            {
                var g = grids[gi];
                var u = new bool[g.Size.x * g.Size.y];
                for (int x = 0; x < g.Size.x; x++)
                    for (int y = 0; y < g.Size.y; y++)
                    {
                        var sq = CellSquare(g, x, y, -0.0625f);
                        foreach (var r in inner) if (ObstacleCut.Overlaps(sq, r)) { u[y * g.Size.x + x] = true; break; }
                    }
                underBuild.Add(u);
            }
            for (int round = 0; round < 6; round++)
            {
                // The blocker is the footprint minus every zoned cell (grown KeepMargin): a cell another block zones on
                // the same land (overlapping blocks at corners and curves) is a hole in it, not blocked.
                var holes = new List<(float2[] sq, float2 min, float2 max)>();
                for (int gi = 0; gi < grids.Count; gi++)
                {
                    var g = grids[gi];
                    for (int x = 0; x < g.Size.x; x++)
                        for (int y = 0; y < g.Size.y; y++)
                            if (zoned[gi][y * g.Size.x + x])
                            {
                                var sq = CellSquare(g, x, y, KeepMargin);
                                float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
                                foreach (var p in sq) { mn = math.min(mn, p); mx = math.max(mx, p); }
                                holes.Add((sq, mn, mx));
                            }
                }
                bool InHole(float2[] cell)
                {
                    foreach (var p in cell)
                    {
                        bool inside = false;
                        foreach (var h in holes)
                            if (p.x >= h.min.x && p.y >= h.min.y && p.x <= h.max.x && p.y <= h.max.y && Poly.Contains(h.sq, p)) { inside = true; break; }
                        if (!inside) return false;
                    }
                    return true;
                }
                var depth = new List<int[]>(grids.Count);
                for (int gi = 0; gi < grids.Count; gi++)
                {
                    var g = grids[gi];
                    int sx = g.Size.x, sy = g.Size.y;
                    var d = new int[sx];
                    for (int x = 0; x < sx; x++)
                    {
                        int y = 0;
                        while (y < sy && open[gi][y * sx + x] && (zoned[gi][y * sx + x] || !underBuild[gi][y * sx + x] || InHole(CellSquare(g, x, y, -0.0625f)))) y++;
                        // CleanBlockedCells: row 0 is blocked when row 1 is.
                        if (y == 1) y = 0;
                        d[x] = y;
                    }
                    depth.Add(d);
                }
                ReduceDepths(grids, depth);
                bool changed = false;
                for (int gi = 0; gi < grids.Count; gi++)
                {
                    var g = grids[gi];
                    int sx = g.Size.x, sy = g.Size.y;
                    var z = zoned[gi];
                    for (int x = 0; x < sx; x++)
                    {
                        int zd = 0;
                        while (zd < sy && z[zd * sx + x]) zd++;
                        if (zd == 0 || depth[gi][x] >= zd) continue;
                        int keep = depth[gi][x] >= MinRows ? depth[gi][x] : 0;
                        for (int y = keep; y < zd; y++) z[y * sx + x] = false;
                        changed = true;
                    }
                }
                if (!changed) break;
            }
        }

        /// <summary>
        /// (s40) The block whose cells a zoning quad covers exactly (the quad = whole cells shrunk QuadShrink, edges parallel
        /// to the block, a corner on a cell corner), or −1.
        /// </summary>
        internal static int AlignedGrid(float2[] quad, List<ZoneGrid> grids)
        {
            if (quad == null || quad.Length != 4) return -1;
            var rect = ZoneLots.Unshrink(Poly.EnsureCcw((float2[])quad.Clone()));
            var e = math.normalizesafe(rect[1] - rect[0]);
            for (int gi = 0; gi < grids.Count; gi++)
            {
                var g = grids[gi];
                var d = math.normalizesafe(g.Direction);
                float c = math.abs(math.dot(d, e));
                if (c > 0.002f && c < 0.998f) continue;
                if (math.distance(g.Position, rect[0]) > math.length((float2)g.Size) * Cell * 0.5f + Cell) continue;
                for (int x = 0; x < g.Size.x; x++)
                    for (int y = 0; y < g.Size.y; y++)
                    {
                        var sq = CellSquare(g, x, y);
                        foreach (var p in sq)
                            if (math.distancesq(p, rect[0]) < 0.01f && Poly.Contains(rect, CellCentre(g, x, y))) return gi;
                    }
            }
            return -1;
        }

        /// <summary>True when the cell centre lies on a captured road (within its half width + <see cref="RoadCellReach"/>).</summary>
        private static bool OnRoad(List<RoadBand> roads, float2 c)
        {
            foreach (var b in roads)
            {
                if (c.x < b.Min.x || c.y < b.Min.y || c.x > b.Max.x || c.y > b.Max.y) continue;
                float lim = b.Half + RoadCellReach;
                for (int i = 0; i + 1 < b.Line.Length; i++)
                    if (SegmentDistance(c, b.Line[i], b.Line[i + 1]) < lim) return true;
            }
            return false;
        }

        private static float SegmentDistance(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float l2 = math.lengthsq(ab);
            float t = l2 > 1e-8f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return math.distance(p, a + ab * t);
        }

        /// <summary>
        /// May the build use (zone, or block on purpose) this cell? Not when it lies on a road, is hidden by an
        /// overlapping block, or the game already blocks it outside the build (a sidewalk, another lot). A blocked
        /// cell inside the footprint stays usable: that is the build being edited (its blockers / paths go first).
        /// </summary>
        public static bool CellOpen(ZoneGrid g, int x, int y, IReadOnlyList<float2[]> feet, IReadOnlyList<NearbyNet> nets) =>
            CellOpen(g, x, y, new List<float2[]>(feet ?? new float2[0][]), RoadBands(nets, new float2(float.MinValue), new float2(float.MaxValue)));

        /// <summary>CellOpen for many cells (no footprints): the road bands near min–max worked out once.</summary>
        public static Func<ZoneGrid, int, int, bool> CellOpener(IReadOnlyList<NearbyNet> nets, float2 min, float2 max)
        {
            var roads = RoadBands(nets, min, max);
            var feet = new List<float2[]>();
            return (g, x, y) => CellOpen(g, x, y, feet, roads);
        }

        private static bool CellOpen(ZoneGrid g, int x, int y, List<float2[]> feet, List<RoadBand> roads)
        {
            var c = CellCentre(g, x, y);
            if (OnRoad(roads, c)) return false;
            var cells = g.Cells;
            int i = x + y * g.Size.x;
            if (cells == null || i < 0 || i >= cells.Length) return true;
            byte f = cells[i];
            bool visible = (f & ZoneGrid.CellVisible) != 0, blocked = (f & ZoneGrid.CellBlocked) != 0;
            if (!blocked) return visible;
            return feet != null && InAny(feet, c);
        }

        /// <summary>The part of <paramref name="sq"/> inside the footprints (one ring): the square itself when wholly inside, null when (almost) outside.</summary>
        public static float2[] ClipToFootprints(float2[] sq, IReadOnlyList<float2[]> feet)
        {
            foreach (var f in feet)
            {
                bool all = true;
                foreach (var p in sq) if (!Poly.Contains(f, p)) { all = false; break; }
                if (all) return sq;
            }
            float2[] best = null;
            float bestArea = 0.5f;
            foreach (var f in feet)
            {
                if (!ObstacleCut.Overlaps(sq, f)) continue;
                List<Region> parts;
                try { parts = Poly.Intersection(sq, f); } catch (Exception) { continue; }
                foreach (var part in parts)
                {
                    if (part?.Outer == null || part.Outer.Length < 3) continue;
                    float a = Poly.Area(part.Outer);
                    if (a > bestArea) { bestArea = a; best = Poly.EnsureCcw(part.Outer); }
                }
            }
            return best;
        }

        private static string ZoneOf(IReadOnlyList<string> zoneIds, int q) => zoneIds != null && q >= 0 && q < zoneIds.Count ? zoneIds[q] ?? "" : "";

        private static bool SameZone(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        /// <summary>Zoned rows of a column (they always start at row 0).</summary>
        private static int Depth(bool[] z, int sx, int sy, int x)
        {
            int d = 0;
            while (d < sy && z[d * sx + x]) d++;
            return d;
        }

        /// <summary>True when the (shrunk) cell square overlaps the blocker area — the game's BlockCellsJob test.</summary>
        public static bool Blocks(IReadOnlyList<Region> regions, float2[] quad)
        {
            foreach (var reg in regions)
            {
                if (!ObstacleCut.Overlaps(quad, reg.Outer)) continue;
                bool inHole = false;
                foreach (var hole in reg.Holes)
                    if (InsideRing(hole, quad)) { inHole = true; break; }
                if (!inHole) return true;
            }
            return false;
        }

        /// <summary>Every corner inside <paramref name="ring"/> and no edge crossing it.</summary>
        private static bool InsideRing(float2[] ring, float2[] quad)
        {
            foreach (var p in quad) if (!Poly.Contains(ring, p)) return false;
            for (int i = 0; i < quad.Length; i++)
                for (int j = 0; j < ring.Length; j++)
                    if (Poly.SegmentsIntersect(quad[i], quad[(i + 1) % quad.Length], ring[j], ring[(j + 1) % ring.Length])) return false;
            return true;
        }

        private static bool InAny(List<float2[]> rings, float2 p)
        {
            foreach (var r in rings) if (Poly.Contains(r, p)) return true;
            return false;
        }

        private static bool OverlapsAny(List<float2[]> rings, float2[] quad)
        {
            foreach (var r in rings) if (ObstacleCut.Overlaps(quad, r)) return true;
            return false;
        }

        private static float2[] Grow(float2[] ring, float by)
        {
            try
            {
                var g = Poly.Offset(ring, -by);
                if (g != null && g.Count > 0) return g[0];
            }
            catch (Exception) { }
            return ring;
        }

        private static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            var r = new float2[q.Length];
            for (int i = 0; i < q.Length; i++) r[i] = q[i] + math.normalizesafe(c - q[i]) * by;
            return r;
        }

        private static void Bounds(List<float2[]> rings, out float2 min, out float2 max)
        {
            min = new float2(float.MaxValue); max = new float2(float.MinValue);
            foreach (var r in rings) foreach (var p in r) { min = math.min(min, p); max = math.max(max, p); }
        }
    }
}
