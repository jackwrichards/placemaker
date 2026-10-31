// s25 — grading a parking lot on sloped ground.
//
// Playtest (Parking type and the commercial lots): "It's not flat — shouldn't each individual place be flat, and
// going up/down to the next level terrace?" The planner put every bay, aisle and lamp on the raw terrain: lumps
// under the stalls, rows tilting with the hill, and (in the commercial types) the lanes' own road cross-fall
// pushing embankments ("white cones") into the lot.
//
// Model, in the layout's row frame (x along the rows):
//   * A tier = the bays of the aisles that run along x (an aisle and the one or two rows it serves; overlapping
//     y-bands merge). Every tier is ONE plane: level across the bays within MaxFall (≤ 2 %), and a cross-fall along the
//     rows shared by the whole lot (≤ MaxRowGrade, 5 % since s29: rows follow the ground the streets follow) so the
//     ramps between tiers stay planar.
//   * (s29) Where the outline faces an existing road, the edge is tied to that road's height (the road the edge faces,
//     not the raw terrain) and the surface bends to it over a blend as wide as a gentle ramp needs (8–30 m).
//   * Between two tiers (medians, cross lanes, the ring road ends) the ground ramps linearly; the step between tiers is
//     held to MaxRamp (8 %) over the gap, so cross lanes and the ring road stay drivable.
//   * Outside the outermost tiers the tier level continues to the lot edge (ring road, perimeter planting); the
//     build's raster banks the edge back to the original ground (4 m), the commercial kit blends further out.
// Emitted as a handful of SLOPED terraform polygons (y-bands of the lot, vertex heights on the plane), so the raster
// reproduces the planes exactly (mean-value interpolation has linear precision) — no 4 m cell staircase. A lot whose
// ground already lies within Tolerance of the graded surface gets no terraform at all (flat sites stay untouched).

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal sealed class GradingResult
    {
        public readonly List<PlannedTerraform> Pieces = new List<PlannedTerraform>();
        /// <summary>Ground after grading (world); null when nothing changes.</summary>
        public Func<double2, float> Height;
        public int Tiers;
        public float MaxCut, MaxFill, RowGrade, MaxStep;
        public int DroppedCross;
        /// <summary>(s29) Outline points tied to an existing road's height, and the largest correction the blend applied.</summary>
        public int Ties;
        public float MaxTie;
        /// <summary>(P2-5) Terraced: the retaining walls between tiers (frame-local y, x from/to, the lower and upper heights).</summary>
        public readonly List<(double Y, double X0, double X1, double Low, double High)> Walls = new List<(double, double, double, double, double)>();
    }

    internal static class LotGrading
    {
        /// <summary>Largest fall across a bay (front to back) and of a tier plane across the rows.</summary>
        public const float MaxFall = 0.02f;
        /// <summary>Largest grade along the rows (cross-fall of every tier, shared by the lot).</summary>
        public const float MaxRowGrade = 0.05f;
        /// <summary>Largest grade of the perimeter road ramping from tier to tier (between tier middles).</summary>
        public const float MaxRamp = 0.08f;
        /// <summary>Largest grade of the planted embankment between two tiers (1 : 2.5).</summary>
        public const float MaxBank = 0.4f;
        /// <summary>A cross lane survives a terrace edge up to this grade.</summary>
        public const float MaxCrossLane = 0.12f;
        /// <summary>Width (m) over which the terraces blend into the ramping perimeter road at the row ends.</summary>
        public const float Transition = 4f;
        /// <summary>Distance (m) past the row ends before the terraces start blending into the ramp.</summary>
        public const float EndClear = 2f;
        /// <summary>(s29) Outline sampling for road ties (m), how far beside a road's edge an outline point still ties to it,
        /// the blend width inside the lot and its cell size.</summary>
        public const double TieStep = 2.0, TieReach = 6.0, TieBlend = 8.0, BlendCell = 2.0, TieSigma = 6.0,
            TieBlendMax = 30.0, TieGrade = 0.025;
        /// <summary>Below this deviation from the graded surface the lot keeps its ground (m).</summary>
        public const float Tolerance = 0.15f;
        /// <summary>(P2-5) Terraced: the largest step a retaining wall takes between two tiers (m), the perimeter road's ramp
        /// between tier middles, and where the wall stands past the lower tier's edge (m).</summary>
        public const double TerraceStep = 2.7, TerraceRamp = 0.15, WallSet = 0.45;
        private const float Sample = 3f;

        private sealed class Tier
        {
            public double Y0, Y1, X0, X1;
            public double A, By, Yc;
            public double At(double x, double y, double bx) => A + bx * x + By * (y - Yc);
        }

        /// <param name="outline">World outline the grading may cover (the lot plus its setback strip); null = the lot only.</param>
        /// <param name="roads">(s29) Existing roads: where one runs along the outline the graded edge meets the road's own height.</param>
        public static GradingResult Grade(Layout layout, Func<double2, float> ground, CancellationToken cancel, IReadOnlyList<double2> outline = null,
            List<RoadLine> roads = null, bool terraced = false)
        {
            var res = new GradingResult();
            if (layout?.Lot == null || layout.Lot.Count < 3 || ground == null) return res;
            var f = layout.Frame;
            var lot = layout.Lot;
            var lotGrid = new RingGrid(lot);   // (perf) inside tests by grid cell (same answers)
            PGeo.Bounds(lot, out var lo, out var hi);

            // Smoothed ground: a 5-point average over ±3 m (single lumps do not tilt a tier).
            double G(double x, double y)
            {
                double s = 0;
                s += ground(f.ToWorld(new double2(x, y)));
                s += ground(f.ToWorld(new double2(x + 3, y))) + ground(f.ToWorld(new double2(x - 3, y)));
                s += ground(f.ToWorld(new double2(x, y + 3))) + ground(f.ToWorld(new double2(x, y - 3)));
                return s / 5.0;
            }

            // ── tiers: bays grouped by the aisle serving them, aisles along x only ──
            // A row = the bays of one Row index along x (a perimeter line serves rows on several sides: Row tells them apart).
            var mods = new Dictionary<long, Tier>();
            var rowLine = new Dictionary<long, int>();
            foreach (var b in layout.LiveBays)
            {
                if (Math.Abs(b.RowDir.x) < 0.95) continue;
                long key = ((long)b.Row << 20) ^ (b.Line & 0xFFFFF);
                if (!mods.TryGetValue(key, out var m))
                {
                    mods[key] = m = new Tier { Y0 = double.MaxValue, Y1 = double.MinValue, X0 = double.MaxValue, X1 = double.MinValue };
                    rowLine[key] = b.Line;
                }
                foreach (var q in b.Quad)
                {
                    m.Y0 = Math.Min(m.Y0, q.y); m.Y1 = Math.Max(m.Y1, q.y);
                    m.X0 = Math.Min(m.X0, q.x); m.X1 = Math.Max(m.X1, q.x);
                }
            }
            // The aisle in front of a row belongs to its tier (level across the drive lane).
            foreach (var kv in rowLine)
            {
                if (kv.Value < 0 || kv.Value >= layout.Lines.Count) continue;
                var line = layout.Lines[kv.Value];
                var d = PGeo.Norm(line.B - line.A);
                if (Math.Abs(d.x) < 0.95) continue;
                var m = mods[kv.Key];
                double ly = (line.A.y + line.B.y) * 0.5, hw = line.Width * 0.5;
                double near = Math.Min(Math.Abs(ly - m.Y0), Math.Abs(ly - m.Y1));
                if (near > hw + 1.0) continue;
                m.Y0 = Math.Min(m.Y0, ly - hw); m.Y1 = Math.Max(m.Y1, ly + hw);
            }
            var list = new List<Tier>(mods.Values);
            list.Sort((a, b) => a.Y0.CompareTo(b.Y0));
            var tiers = new List<Tier>();
            foreach (var m in list)
            {
                var last = tiers.Count > 0 ? tiers[tiers.Count - 1] : null;
                if (last != null && m.Y0 <= last.Y1 + 0.5)
                {
                    last.Y1 = Math.Max(last.Y1, m.Y1);
                    last.X0 = Math.Min(last.X0, m.X0); last.X1 = Math.Max(last.X1, m.X1);
                }
                else tiers.Add(m);
            }
            if (tiers.Count == 0) tiers.Add(new Tier { Y0 = lo.y, Y1 = hi.y, X0 = lo.x, X1 = hi.x });
            cancel.ThrowIfCancellationRequested();

            // ── samples inside the lot ──
            var pts = new List<double3>();
            for (double y = lo.y + Sample * 0.5; y < hi.y; y += Sample)
                for (double x = lo.x + Sample * 0.5; x < hi.x; x += Sample)
                {
                    var p = new double2(x, y);
                    if (!lotGrid.Contains(p)) continue;
                    pts.Add(new double3(x, y, G(x, y)));
                }
            if (pts.Count < 4) return res;

            // ── (s29) ties: outline points next to an existing road take the road's height ──
            // Playtest: a lot between two lanes met them with torn edges and slumped into a bowl. The lanes sit on their own
            // levels (raised or cut into the terrain); the edge facing one must come out at that level, not at the raw ground.
            var clipLocal = new List<double2>();
            if (outline != null && outline.Count >= 3) foreach (var w in outline) clipLocal.Add(f.ToLocal(w));
            else clipLocal.AddRange(lot);
            var ties = new List<double3>();
            double ccw = 0;
            for (int i = 0, j = clipLocal.Count - 1; i < clipLocal.Count; j = i++) ccw += clipLocal[j].x * clipLocal[i].y - clipLocal[i].x * clipLocal[j].y;
            if (roads != null && roads.Count > 0)
                for (int e = 0; e < clipLocal.Count; e++)
                {
                    var a = clipLocal[e]; var b = clipLocal[(e + 1) % clipLocal.Count];
                    double len = PGeo.Len(b - a);
                    if (len < 0.5) continue;
                    var dir = (b - a) / len;
                    var inward = ccw >= 0 ? new double2(-dir.y, dir.x) : new double2(dir.y, -dir.x);
                    int n = Math.Max(1, (int)Math.Ceiling(len / TieStep));
                    for (int k = 0; k < n; k++)
                    {
                        var q = a + (b - a) * ((k + 0.5) / n);
                        // The road this edge faces (straight out of it), not merely the nearest one: at a corner two roads
                        // on different levels would otherwise alternate along the edge.
                        if (!Roads.Cast(roads, f.ToWorld(q), f.DirToWorld(-inward), 40, out var hit)) continue;
                        if (hit.Distance > roads[hit.Road].Width * 0.5 + TieReach) continue;
                        double h = Roads.HeightAt(roads, hit);
                        if (!double.IsNaN(h) && !double.IsInfinity(h)) ties.Add(new double3(q.x, q.y, h));
                    }
                }
            res.Ties = ties.Count;
            // The planes are fitted to the ground and (weighted) to the road edges, so the blend at the edges stays small.
            var fit = new List<double3>(pts);
            foreach (var t in ties) for (int k = 0; k < 4; k++) fit.Add(t);

            // ── one cross-fall along the rows for the whole lot (planar ramps between tiers) ──
            double bx = Clamp(FitSlopeX(fit), MaxRowGrade);
            res.RowGrade = (float)bx;

            // ── a plane per tier ──
            foreach (var t in tiers)
            {
                double sy = 0, sr = 0; int n = 0;
                foreach (var q in fit) if (q.y >= t.Y0 - 1 && q.y <= t.Y1 + 1) { sy += q.y; sr += q.z - bx * q.x; n++; }
                if (n == 0)
                {
                    t.Yc = (t.Y0 + t.Y1) * 0.5;
                    t.A = G((t.X0 + t.X1) * 0.5, t.Yc) - bx * (t.X0 + t.X1) * 0.5;
                    continue;
                }
                t.Yc = sy / n;
                double mr = sr / n, num = 0, den = 0;
                foreach (var q in fit)
                    if (q.y >= t.Y0 - 1 && q.y <= t.Y1 + 1) { double dy = q.y - t.Yc; num += dy * (q.z - bx * q.x - mr); den += dy * dy; }
                t.By = Clamp(den > 1e-6 ? num / den : 0, MaxFall);
                t.A = mr;
            }

            // ── steps between neighbouring tiers ──
            // Across the rows the step is taken by a planted embankment in the gap (≤ MaxBank); the perimeter road at the
            // row ends ramps from tier to tier over the whole pitch (≤ MaxRamp between tier middles). Cross lanes that
            // would have to climb a step steeper than MaxCrossLane are dropped (a terrace edge is no place for a lane).
            // Chain from the first tier: every step takes the fitted step clamped to what an embankment and the ramping
            // perimeter road allow; then the whole chain shifts so cut and fill balance (no one-sided cliff at an edge).
            {
                double desired0 = tiers[0].A;
                var desired = new double[tiers.Count];
                for (int i = 0; i < tiers.Count; i++) desired[i] = tiers[i].A;
                for (int i = 0; i + 1 < tiers.Count; i++)
                {
                    var a = tiers[i]; var b = tiers[i + 1];
                    double gap = Math.Max(0.5, b.Y0 - a.Y1);
                    double pitch = Math.Max(gap, (b.Y0 + b.Y1) * 0.5 - (a.Y0 + a.Y1) * 0.5);
                    // edge step d (bank) and mid step d + c (perimeter ramp), c from the tiers' own falls
                    double baseB = desired[i + 1] + b.By * (b.Y0 - b.Yc), baseA = desired[i] + a.By * (a.Y1 - a.Yc);
                    double want = baseB - baseA;
                    double c = (b.By * ((b.Y0 + b.Y1) * 0.5 - b.Y0)) - (a.By * ((a.Y0 + a.Y1) * 0.5 - a.Y1));
                    // (P2-5) terraced: a retaining wall takes the step (up to TerraceStep), the perimeter road ramps steeper
                    double bank = terraced ? TerraceStep : MaxBank * gap, ramp = terraced ? TerraceRamp : MaxRamp;
                    double lo2 = Math.Max(-bank, -ramp * pitch - c), hi2 = Math.Min(bank, ramp * pitch - c);
                    double d = lo2 <= hi2 ? Math.Max(lo2, Math.Min(hi2, want)) : 0.5 * (lo2 + hi2);
                    b.A = d + a.A + a.By * (a.Y1 - a.Yc) - b.By * (b.Y0 - b.Yc);
                }
                double sum = 0; int cnt = 0;
                foreach (var q in pts)
                    foreach (var t in tiers)
                        if (q.y >= t.Y0 && q.y <= t.Y1) { sum += q.z - t.At(q.x, q.y, bx); cnt++; break; }
                double shift = cnt > 0 ? sum / cnt : 0;
                foreach (var t in tiers) t.A += shift;
            }
            for (int i = 0; i + 1 < tiers.Count; i++)
                res.MaxStep = (float)Math.Max(res.MaxStep, Math.Abs(tiers[i + 1].At(0, tiers[i + 1].Y0, bx) - tiers[i].At(0, tiers[i].Y1, bx)));

            // Row ends: x of the outermost bays of the tiers; beyond them (perimeter road, ends of the lot) the ground ramps.
            double xa = double.MaxValue, xb = double.MinValue;
            foreach (var t in tiers) { xa = Math.Min(xa, t.X0); xb = Math.Max(xb, t.X1); }
            // The innermost row ends: longer perimeter rows reach into the ramp (their tier and the ramp differ by their small fall only).
            if (tiers.Count > 2) { xa = double.MinValue; xb = double.MaxValue; foreach (var t in tiers) { xa = Math.Max(xa, t.X0); xb = Math.Min(xb, t.X1); } if (xb - xa < 10) { xa = double.MaxValue; xb = double.MinValue; foreach (var t in tiers) { xa = Math.Min(xa, t.X0); xb = Math.Max(xb, t.X1); } } }

            double Terrace(double x, double y)
            {
                var first = tiers[0]; var last = tiers[tiers.Count - 1];
                if (y <= first.Y0) return first.At(x, first.Y0, bx);
                if (y >= last.Y1) return last.At(x, last.Y1, bx);
                for (int i = 0; i < tiers.Count; i++)
                {
                    var t = tiers[i];
                    if (y >= t.Y0 && y <= t.Y1) return t.At(x, y, bx);
                    if (i + 1 < tiers.Count && y > t.Y1 && y < tiers[i + 1].Y0)
                    {
                        var u = tiers[i + 1];
                        // (P2-5) terraced: a sharp step at the wall line, each side on its own tier
                        // (the middle of the median: the raster smears a step over a cell or two, keep it off the stalls; game runner 1031-131742)
                        if (terraced) return y < t.Y1 + Math.Max(WallSet, (u.Y0 - t.Y1) * 0.5) ? t.At(x, t.Y1, bx) : u.At(x, u.Y0, bx);
                        double k = (y - t.Y1) / Math.Max(1e-6, u.Y0 - t.Y1);
                        return t.At(x, t.Y1, bx) * (1 - k) + u.At(x, u.Y0, bx) * k;
                    }
                }
                return last.At(x, last.Y1, bx);
            }

            double Ramp(double x, double y)
            {
                var first = tiers[0]; var last = tiers[tiers.Count - 1];
                double m0 = (first.Y0 + first.Y1) * 0.5, m1 = (last.Y0 + last.Y1) * 0.5;
                if (y <= m0) return first.At(x, m0, bx);
                if (y >= m1) return last.At(x, m1, bx);
                for (int i = 0; i + 1 < tiers.Count; i++)
                {
                    var t = tiers[i]; var u = tiers[i + 1];
                    double a = (t.Y0 + t.Y1) * 0.5, b = (u.Y0 + u.Y1) * 0.5;
                    if (y < a || y > b) continue;
                    double k = (y - a) / Math.Max(1e-6, b - a);
                    return t.At(x, a, bx) * (1 - k) + u.At(x, b, bx) * k;
                }
                return last.At(x, m1, bx);
            }

            // The blend starts a little past the last bay so the end stalls stay on their terrace (raster cells are 1.75 m).
            double ra = xa - EndClear, rb = xb + EndClear;
            if (terraced)
                for (int i = 0; i + 1 < tiers.Count; i++)
                {
                    var a = tiers[i]; var b = tiers[i + 1];
                    double y = a.Y1 + Math.Max(WallSet, (b.Y0 - a.Y1) * 0.5), x0 = Math.Max(ra, Math.Max(a.X0, b.X0)), x1 = Math.Min(rb, Math.Min(a.X1, b.X1));
                    double ha = a.At((x0 + x1) * 0.5, a.Y1, bx), hb = b.At((x0 + x1) * 0.5, b.Y0, bx);
                    if (Math.Abs(hb - ha) >= 0.3 && x1 - x0 > 3) res.Walls.Add((y, x0, x1, Math.Min(ha, hb), Math.Max(ha, hb)));
                }
            double W(double x) => x < ra ? Math.Min(1, (ra - x) / Transition) : x > rb ? Math.Min(1, (x - rb) / Transition) : 0;

            double F(double x, double y)
            {
                double w = W(x);
                if (w <= 0) return Terrace(x, y);
                if (w >= 1) return Ramp(x, y);
                return Terrace(x, y) * (1 - w) + Ramp(x, y) * w;
            }

            // Edge blend: the surface bends to the road heights over TieBlend metres from the tied outline points.
            // The blend is as wide as the correction needs to stay a gentle ramp (≤ TieGrade), TieBlend at least.
            var corr = new double[ties.Count];
            var reach = new double[ties.Count];
            for (int i = 0; i < ties.Count; i++)
            {
                corr[i] = ties[i].z - F(ties[i].x, ties[i].y);
                reach[i] = Math.Max(TieBlend, Math.Min(TieBlendMax, 1.5 * Math.Abs(corr[i]) / TieGrade));
            }
            double C(double x, double y)
            {
                if (ties.Count == 0) return 0;
                double sw = 0, sc = 0, sr = 0, dmin = double.MaxValue;
                for (int i = 0; i < ties.Count; i++)
                {
                    double dx = x - ties[i].x, dy = y - ties[i].y, d2 = dx * dx + dy * dy;
                    if (d2 > reach[i] * reach[i]) continue;
                    double d = Math.Sqrt(d2);
                    dmin = Math.Min(dmin, d);
                    // Weighted by distance relative to the nearest tie: the field follows the edge it is closest to.
                    double w = Math.Exp(-d2 / (2 * TieSigma * TieSigma));
                    sw += w; sc += w * corr[i]; sr += w * reach[i];
                }
                if (sw <= 1e-300 || dmin == double.MaxValue) return 0;
                double t = Math.Max(0, Math.Min(1, 1 - dmin / (sr / sw)));
                return sc / sw * t * t * (3 - 2 * t);
            }
            // The blend sampled once on a BlendCell grid (bilinear between nodes): ~220 ties × thousands of lookups was 0.5 s.
            PGeo.Bounds(clipLocal, out var gLo, out var gHi);
            gLo -= 2 * BlendCell; gHi += 2 * BlendCell;
            int gnx = (int)Math.Ceiling((gHi.x - gLo.x) / BlendCell) + 1, gny = (int)Math.Ceiling((gHi.y - gLo.y) / BlendCell) + 1;
            double[] cg = null;
            double Cg(double x, double y)
            {
                if (ties.Count == 0) return 0;
                if (cg == null)
                {
                    cg = new double[gnx * gny];
                    for (int j = 0; j < gny; j++)
                        for (int i = 0; i < gnx; i++) cg[j * gnx + i] = C(gLo.x + i * BlendCell, gLo.y + j * BlendCell);
                }
                double fx = Math.Max(0, Math.Min(gnx - 1.000001, (x - gLo.x) / BlendCell)), fy = Math.Max(0, Math.Min(gny - 1.000001, (y - gLo.y) / BlendCell));
                int ix = (int)fx, iy = (int)fy; double tx = fx - ix, ty = fy - iy;
                int k = iy * gnx + ix;
                return (cg[k] * (1 - tx) + cg[k + 1] * tx) * (1 - ty) + (cg[k + gnx] * (1 - tx) + cg[k + gnx + 1] * tx) * ty;
            }
            double F2(double x, double y) => F(x, y) + Cg(x, y);
            foreach (var c in corr) res.MaxTie = (float)Math.Max(res.MaxTie, Math.Abs(c));

            // ── nothing to do on ground that already is the graded surface ──
            double worst = 0, cut = 0, fill = 0;
            foreach (var t in ties) worst = Math.Max(worst, Math.Abs(t.z - ground(f.ToWorld(new double2(t.x, t.y)))));
            foreach (var q in pts)
            {
                double raw = ground(f.ToWorld(new double2(q.x, q.y)));
                double dz = F2(q.x, q.y) - raw;
                worst = Math.Max(worst, Math.Abs(dz));
                cut = Math.Max(cut, -dz); fill = Math.Max(fill, dz);
            }
            res.Tiers = tiers.Count;
            if (worst < Tolerance) return res;
            res.MaxCut = (float)cut; res.MaxFill = (float)fill;
            cancel.ThrowIfCancellationRequested();

            // Cross lanes over a terrace edge steeper than a lane may be: dropped (the gap stays paved, a walkway).
            for (int i = 0; i + 1 < tiers.Count; i++)
            {
                var a = tiers[i]; var b = tiers[i + 1];
                double gap = Math.Max(0.5, b.Y0 - a.Y1);
                double step = Math.Abs(b.At(0, b.Y0, bx) - a.At(0, a.Y1, bx));
                if (step <= MaxCrossLane * gap) continue;
                foreach (var l in layout.Lines)
                {
                    if (l.Removed || l.Kind != LineKind.Cross) continue;
                    double y0 = Math.Min(l.A.y, l.B.y), y1 = Math.Max(l.A.y, l.B.y);
                    if (y0 < b.Y0 - 0.1 && y1 > a.Y1 + 0.1 && l.A.x >= xa - 0.5 && l.A.x <= xb + 0.5) { l.Removed = true; res.DroppedCross++; }
                }
            }

            // ── emit: cells of the lot between the tier/ramp breaks, sloped polygons (planes are exact) ──
            var ys = new List<double> { lo.y - 1, hi.y + 1 };
            foreach (var t in tiers) { ys.Add(t.Y0); ys.Add(t.Y1); ys.Add((t.Y0 + t.Y1) * 0.5); }
            if (terraced) for (int i = 0; i + 1 < tiers.Count; i++) ys.Add(tiers[i].Y1 + Math.Max(WallSet, (tiers[i + 1].Y0 - tiers[i].Y1) * 0.5));
            var xs = new List<double> { lo.x - 1, hi.x + 1, ra, rb };
            for (double k = 1; k <= Transition / 2.0 + 1e-6; k++) { xs.Add(ra - k * 2.0); xs.Add(rb + k * 2.0); }
            ys.Sort(); xs.Sort();
            Dedupe(ys); Dedupe(xs);
            // The graded surface reaches the outline (the setback strip too): the build's 4 m bank then starts there, not
            // under the outer row of stalls.
            var lotRing = new float2[clipLocal.Count];
            for (int i = 0; i < clipLocal.Count; i++) lotRing[i] = new float2((float)clipLocal[i].x, (float)clipLocal[i].y);
            PGeo.Bounds(clipLocal, out var clo, out var chi);
            lo = math.min(lo, clo); hi = math.max(hi, chi);
            for (int j = 0; j + 1 < ys.Count; j++)
                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    double x0 = xs[i], x1 = xs[i + 1], y0 = ys[j], y1 = ys[j + 1];
                    if (x1 < lo.x - 0.5 || x0 > hi.x + 0.5 || y1 < lo.y - 0.5 || y0 > hi.y + 0.5) continue;
                    var piece = Sculpt.ClipToRect(lotRing, new float2((float)x0, (float)y0), new float2((float)x1, (float)y1));
                    if (piece == null) continue;
                    var clean = Poly.Clean(piece, 0.05f);
                    if (clean.Length < 3 || Poly.Area(clean) < 0.25f) continue;
                    var ring = new float3[clean.Length];
                    double mean = 0;
                    for (int v = 0; v < clean.Length; v++)
                    {
                        // Height from this cell's own piece of the surface (a vertex on a break belongs to this cell).
                        double x = Math.Max(x0, Math.Min(x1, clean[v].x)), y = Math.Max(y0, Math.Min(y1, clean[v].y));
                        double h = F(x, y);
                        var w = f.ToWorld(new double2(clean[v].x, clean[v].y));
                        ring[v] = new float3((float)w.x, (float)h, (float)w.y);
                        mean += h - ground(w);
                    }
                    double signed = 0;
                    for (int v = 0, u = ring.Length - 1; v < ring.Length; u = v++) signed += (double)ring[u].x * ring[v].z - (double)ring[v].x * ring[u].z;
                    if (signed < 0) Array.Reverse(ring);   // CCW in world x/z like every other ring
                    var c = Poly.Centroid(Poly.To2D(ring));
                    res.Pieces.Add(new PlannedTerraform
                    {
                        Ring = ring, Kind = mean >= 0 ? TerraformKind.Raise : TerraformKind.Lower,
                        TargetHeight = Build.TerraformSurface.At(ring, c), Permanent = true, Sloped = true,
                    });
                }
            // Edge blend cells (2 m, sloped quads, after the planes so they win in the raster) where the correction matters.
            if (ties.Count > 0)
                for (double y = gLo.y; y < gHi.y; y += BlendCell)
                    for (double x = gLo.x; x < gHi.x; x += BlendCell)
                    {
                        double cm = Math.Max(Math.Max(Math.Abs(Cg(x, y)), Math.Abs(Cg(x + BlendCell, y))), Math.Max(Math.Abs(Cg(x, y + BlendCell)), Math.Abs(Cg(x + BlendCell, y + BlendCell))));
                        if (cm < 0.02) continue;
                        var piece = Sculpt.ClipToRect(lotRing, new float2((float)x, (float)y), new float2((float)(x + BlendCell), (float)(y + BlendCell)));
                        if (piece == null) continue;
                        var clean = Poly.Clean(piece, 0.05f);
                        if (clean.Length < 3 || Poly.Area(clean) < 0.1f) continue;
                        var ring = new float3[clean.Length];
                        for (int v = 0; v < clean.Length; v++)
                        {
                            var w = f.ToWorld(new double2(clean[v].x, clean[v].y));
                            ring[v] = new float3((float)w.x, (float)F2(clean[v].x, clean[v].y), (float)w.y);
                        }
                        double signed = 0;
                        for (int v = 0, u = ring.Length - 1; v < ring.Length; u = v++) signed += (double)ring[u].x * ring[v].z - (double)ring[v].x * ring[u].z;
                        if (signed < 0) Array.Reverse(ring);
                        var c = Poly.Centroid(Poly.To2D(ring));
                        float th = Build.TerraformSurface.At(ring, c);
                        res.Pieces.Add(new PlannedTerraform
                        {
                            Ring = ring, Kind = th >= ground(f.ToWorld(new double2(x + BlendCell * 0.5, y + BlendCell * 0.5))) ? TerraformKind.Raise : TerraformKind.Lower,
                            TargetHeight = th, Permanent = true, Sloped = true,
                        });
                    }
            var clipGrid = new RingGrid(clipLocal);
            res.Height = w =>
            {
                var l = f.ToLocal(w);
                return clipGrid.Contains(l) ? (float)F2(l.x, l.y) : ground(w);
            };
            return res;
        }

        private static void Dedupe(List<double> v)
        {
            for (int i = v.Count - 1; i > 0; i--) if (v[i] - v[i - 1] < 0.05) v.RemoveAt(i);
        }

        private static double FitSlopeX(List<double3> pts)
        {
            // Least squares plane z = a + bx·x + by·y; returns bx.
            double n = pts.Count, sx = 0, sy = 0, sz = 0;
            foreach (var p in pts) { sx += p.x; sy += p.y; sz += p.z; }
            double mx = sx / n, my = sy / n, mz = sz / n;
            double xx = 0, xy = 0, yy = 0, xz = 0, yz = 0;
            foreach (var p in pts)
            {
                double dx = p.x - mx, dy = p.y - my, dz = p.z - mz;
                xx += dx * dx; xy += dx * dy; yy += dy * dy; xz += dx * dz; yz += dy * dz;
            }
            double det = xx * yy - xy * xy;
            if (Math.Abs(det) < 1e-9) return xx > 1e-9 ? xz / xx : 0;
            return (xz * yy - yz * xy) / det;
        }

        private static double Clamp(double v, double m) => double.IsNaN(v) ? 0 : Math.Max(-m, Math.Min(m, v));
    }
}
