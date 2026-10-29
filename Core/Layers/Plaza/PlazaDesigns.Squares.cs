// Designed plazas, round 2: five squares built on their own ground (Levels), after famous real ones.
//
//   sunkenPlaza  Rockefeller Center: a court sunk 2.7 m, seating steps down its long sides, a wide stair from the
//                channel gardens (long beds with fountain pools between, lime trees and benches either side), a fountain
//                wall at the far end under a tall flag, flags round the railed rim, cafe tables under parasols below.
//   waterSteps   a cascading water plaza (Lovejoy, Ira Keller): four levels rising 0.9 m each along the square's
//                axis (up the slope on a hillside), a channel of dark-stone pools down the middle with wall fountains
//                spilling from every step, stone stairs either side, lawns with trees on the terraces, a pergola
//                overlook on top, cafe tables round the last pool.
//   marketHall   a market hall square: a stone plinth 0.9 m high with stairs on every side, long halls of modular
//                pergola bays (two bays deep) with tables under them, food stalls in the aisles, a pavilion where the
//                aisles cross, flags on the corners.
//   dining       a dining terrace: a raised terrace 1.8 m high along one side, railed, rows of tables under big
//                parasols, a deep pergola canopy along its back with tables under it, a grand stair and two side
//                stairs down to a cafe grove (tables under a grid of trees) round a fountain.
//   grandSteps   the Spanish Steps: three flights of 1.5 m narrowing as they climb, landings with flowers and pots,
//                planted terraced banks either side, a fountain in the piazza at the foot with cafes along it, a
//                belvedere on top (pavilion, trees, a tall flag on the axis, benches looking down).
//
// Every level is walkable: the walk round the edge stays on the street's level and joins every entrance, and stairs
// lead from it to every floor. Every change of level is a bank with a stone wall at its foot and a hedge or a railing
// on top, or a flight of stairs: no bare slopes.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        /// <summary>An opening in a level's edge: side (0: u0, 1: u1, 2: v0, 3: v1), centre along that side (u or v), half width.</summary>
        private struct Gap { public int Side; public float At, Half; public Gap(int s, float at, float half) { Side = s; At = at; Half = half; } }

        /// <summary>Head of a bank: what stands on its top edge.</summary>
        private enum Head { None, Hedge, Rail, Wall }

        /// <summary>
        /// The edges of one raised (or sunk) rectangle: its banks laid in grass (unless <paramref name="paved"/>), a stone
        /// wall along every foot (on the lower side), a hedge, railing or parapet along every head, all open at the gaps.
        /// Sides with a zero bank are left alone.
        /// </summary>
        private static void Edges(Kit k, float u0, float u1, float v0, float v1, float4 b, bool sunk, Head head, List<Gap> gaps, AssetRef paved = default, int courses = 1)
        {
            float[] bw = { b.x, b.y, b.z, b.w };
            for (int s = 0; s < 4; s++)
            {
                if (bw[s] <= 0.01f) continue;
                // Outer (rect) edge and inner (head or floor) edge, both in local (u, v), running along the side.
                float2 o0, o1, i0, i1, outN;
                switch (s)
                {
                    case 0: o0 = new float2(u0, v0); o1 = new float2(u0, v1); i0 = new float2(u0 + b.x, v0 + b.z); i1 = new float2(u0 + b.x, v1 - b.w); outN = new float2(-1, 0); break;
                    case 1: o0 = new float2(u1, v0); o1 = new float2(u1, v1); i0 = new float2(u1 - b.y, v0 + b.z); i1 = new float2(u1 - b.y, v1 - b.w); outN = new float2(1, 0); break;
                    case 2: o0 = new float2(u0, v0); o1 = new float2(u1, v0); i0 = new float2(u0 + b.x, v0 + b.z); i1 = new float2(u1 - b.y, v0 + b.z); outN = new float2(0, -1); break;
                    default: o0 = new float2(u0, v1); o1 = new float2(u1, v1); i0 = new float2(u0 + b.x, v1 - b.w); i1 = new float2(u1 - b.y, v1 - b.w); outN = new float2(0, 1); break;
                }
                var quad = Poly.EnsureCcw(new[] { k.W(o0.x, o0.y), k.W(o1.x, o1.y), k.W(i1.x, i1.y), k.W(i0.x, i0.y) });
                if (paved.Id != null) k.Lay(PFeature + 4, quad, paved);
                else k.Lay(PGreen - 6, quad, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                // Gaps on this side, as distances along the outer and inner edges.
                bool alongV = s < 2;
                var gOut = new List<(float, float)>(); var gIn = new List<(float, float)>();
                if (gaps != null)
                    foreach (var g in gaps)
                        if (g.Side == s)
                        {
                            float start = alongV ? o0.y : o0.x, startIn = alongV ? i0.y : i0.x;
                            gOut.Add((g.At - start, g.Half + 0.3f));
                            gIn.Add((g.At - startIn, g.Half + 0.3f));
                        }
                // The low side of the bank: the foot for a raised rectangle, the rim for a sunk one.
                var n = k.A * outN.x + k.S * outN.y;
                if (!sunk)
                {
                    k.WallWithGaps(k.W(o0.x, o0.y) + n * 0.15f, k.W(o1.x, o1.y) + n * 0.15f, gOut, courses);
                    HeadLine(k, k.W(i0.x, i0.y) - n * 0.6f, k.W(i1.x, i1.y) - n * 0.6f, gIn, head);
                }
                else
                {
                    // Sunk: the wall stands at the foot on the floor (inner edge), the head is the rim (outer edge).
                    k.WallWithGaps(k.W(i0.x, i0.y) - n * 0.15f, k.W(i1.x, i1.y) - n * 0.15f, gIn, courses);
                    HeadLine(k, k.W(o0.x, o0.y) + n * 0.5f, k.W(o1.x, o1.y) + n * 0.5f, gOut, head);
                }
            }
        }

        private static void HeadLine(Kit k, float2 a, float2 b, List<(float at, float half)> gaps, Head head)
        {
            if (head == Head.None) return;
            float len = math.distance(a, b);
            if (len < 1f) return;
            var d = (b - a) / len;
            var cuts = new List<(float, float)>();
            foreach (var (at, half) in gaps) cuts.Add((at - half, at + half));
            cuts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            float s = 0.6f;
            void Run(float t0, float t1)
            {
                if (t1 - t0 < 1.2f) return;
                var p0 = a + d * t0; var p1 = a + d * t1;
                if (head == Head.Hedge) k.Hedge(p0, p1);
                else if (head == Head.Rail) k.Railing(p0, p1);
                else k.Wall(p0, p1, 1);
            }
            foreach (var (c0, c1) in cuts) { Run(s, math.min(c0, len - 0.6f)); s = math.max(s, c1); }
            Run(s, len - 0.6f);
        }

        private static float4 B4(float b) => new float4(b);

        // ───────────── sunken plaza (Rockefeller Center) ─────────────

        private static void SunkenPlaza(Kit k)
        {
            float fw = k.Fw, prom = 5f;
            k.Leftover(fw + prom + 1f, "bosque");
            k.WallId = "EU_Wall03";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            k.RingTrees(fw + 1.6f, 8.5f, true, 0, true);
            float ia = k.EA - fw - prom - 0.5f, ib = k.EB - fw - prom - 0.5f;
            if (ia < 26f || ib < 17f) { Piazza(k); return; }
            const float D = 2.7f;
            float bs = D / 0.34f, bst = BankFor(D), be = BankFor(D);
            float cv = math.clamp(ib * 0.62f, bs + 6f, 44f);
            float cu = math.clamp(ia * 0.5f, 14f, 46f);
            float uB = ia - 1.5f, uA = uB - 2f * cu;
            if (uA + ia < 14f) { uA = -ia + 14f; cu = (uB - uA) * 0.5f; }
            var lv = new Levels(k, ia, ib);
            var bank = new float4(bst, be, bs, bs);
            lv.Add(uA, uB, -cv, cv, -D, bank);
            lv.Emit();
            k.Ctx.Plan.Facts["sunkenDepth"] = D;
            float fu0 = uA + bst, fu1 = uB - be, fv = cv - bs;

            // The floor: cafe tables under parasols round a dark centre, the fountain wall at the far end.
            k.Lay(PFeature + 3, k.Box(fu0, fu1, -fv, fv).Ring, k.P.Focal);
            k.Lay(PKerb, PavingShapes.Frame(k.Box(fu0, fu1, -fv, fv).Ring, 0.8f), k.P.Band);
            float rl = math.min((fu1 - fu0) * 0.18f, 7f), rw = math.min(fv * 0.32f, 5f);
            var rink = k.Box((fu0 + fu1) * 0.5f - rl, (fu0 + fu1) * 0.5f + rl, -rw, rw);
            if (rink.HalfLength > 3f && rink.HalfWidth > 2f)
            {
                k.Lay(PFeature + 1, rink.Ring, k.P.Inlay);
                k.Lay(PFeature, PavingShapes.Frame(rink.Ring, 0.6f), k.P.Band);
                LeisureKit.TakeRect(k.Occ, rink, 0);
            }
            // The far end: a wall of fountains at the foot of a planted bank, a tall flag above it on the axis.
            var fwA = k.W(fu1 + 0.15f, -fv + 1f); var fwB = k.W(fu1 + 0.15f, fv - 1f);
            k.Wall(fwA, fwB, 1);
            foreach (var p in LeisureKit.Along(fwA, fwB, 3.2f)) k.WallFountain(p - k.A * 0.8f, -k.A);
            k.BigFlag(k.W(uB + 1.6f, 0f), true);
            // Grass on the far bank, hedges along its top.
            k.Lay(PGreen - 6, Poly.EnsureCcw(new[] { k.W(fu1, -fv), k.W(uB, -cv + bs * 0.4f), k.W(uB, cv - bs * 0.4f), k.W(fu1, fv) }), k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
            k.Hedge(k.W(uB - 0.8f, -fv + 0.5f), k.W(uB - 0.8f, fv - 0.5f));
            k.CafeGrid(fu0 + 1.5f, fu1 - 2.2f, -fv + 1.2f, fv - 1.2f, 5.4f, true);

            // The near end: one wide stair down from the channel gardens.
            k.Flight(k.W(fu0 + 0.3f, 0f), k.W(uA - 0.3f, 0f), math.min(fv * 2f - 2f, 16f));
            // Seating steps down the long sides: stone treads with dark risers, rows of benches looking at the floor.
            foreach (float sd in new[] { -1f, 1f })
            {
                var steps = Poly.EnsureCcw(new[] { k.W(uA + bst * 0.5f, sd * cv), k.W(uB - be * 0.5f, sd * cv), k.W(fu1, sd * fv), k.W(fu0, sd * fv) });
                k.Lay(PFeature + 2, steps, k.P.Accent);
                for (float d = 1.1f; d < bs - 0.3f; d += 1.1f)
                {
                    float v = sd * (cv - d);
                    k.Lay(PFeature - 1, PavingShapes.Band(k.W(fu0 + 0.5f, v), k.W(fu1 - 0.5f, v), 0.46f), k.P.Band);
                    k.Wall(k.W(fu0 + 0.5f, v), k.W(fu1 - 0.5f, v), 1, null, WallH - 0.22f, false);
                }
                foreach (float d in new[] { 1.9f, 4.1f, 6.3f })
                {
                    if (d > bs - 1f) continue;
                    float v = sd * (cv - d);
                    foreach (var p in LeisureKit.Along(k.W(fu0 + 2f, v), k.W(fu1 - 2f, v), 3.4f)) k.Seat(p, -k.S * sd, PlazaArt.ModernBenches);
                }
            }
            // The rim: railings round three sides, a ring of flags behind them (the plaza's famous flags).
            k.Railing(k.W(uA + 0.5f, -cv - 0.4f), k.W(uB + 0.4f, -cv - 0.4f));
            k.Railing(k.W(uA + 0.5f, cv + 0.4f), k.W(uB + 0.4f, cv + 0.4f));
            k.Railing(k.W(uB + 0.4f, -cv - 0.4f), k.W(uB + 0.4f, cv + 0.4f));
            foreach (float sd in new[] { -1f, 1f })
                foreach (var p in LeisureKit.Along(k.W(uA + 1f, sd * (cv + 1.3f)), k.W(uB + 1.3f, sd * (cv + 1.3f)), 4.6f)) k.BigFlag(p);
            foreach (var p in LeisureKit.Along(k.W(uB + 1.3f, -cv + 2f), k.W(uB + 1.3f, cv - 2f), 4.6f)) if (math.abs(math.dot(p - k.C, k.S)) > 3f) k.BigFlag(p);
            foreach (float sd in new[] { -1f, 1f })
            {
                k.Light(k.W(uA - 1f, sd * (math.min(fv, 8f) + 1.5f)));
                k.Light(k.W(uB + 2.6f, sd * (cv + 2.6f)));
            }

            // The channel gardens: long beds down the axis, fountain pools between them, walks and lime trees either side.
            float c0 = -ia + 1f, c1 = uA - 2.5f;
            float wb = math.clamp(ib * 0.2f, 5f, 8f);
            float len = c1 - c0;
            int beds = math.max(1, (int)math.floor((len + 5f) / 16f));
            float pool = 5f, bed = (len - (beds - 1) * pool) / beds;
            k.Lay(PWalk, k.Box(c0, c1, -wb * 0.5f - 4.5f, wb * 0.5f + 4.5f).Ring, k.P.Path);
            for (int i = 0; i < beds; i++)
            {
                float b0 = c0 + i * (bed + pool), b1 = b0 + bed;
                var r = k.Box(b0, b1, -wb * 0.5f, wb * 0.5f);
                k.Lay(PKerb, PavingShapes.Frame(r.Ring, 0.6f), k.P.Band);
                var inner = k.Box(b0 + 0.6f, b1 - 0.6f, -wb * 0.5f + 0.6f, wb * 0.5f - 0.6f);
                k.Lay(PGreen, inner.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                foreach (float sd in new[] { -1f, 1f }) k.Hedge(k.W(b0 + 1.2f, sd * (wb * 0.5f - 1.1f)), k.W(b1 - 1.2f, sd * (wb * 0.5f - 1.1f)));
                foreach (var p in LeisureKit.Along(k.W(b0 + 1.5f, 0f), k.W(b1 - 1.5f, 0f), 2.8f)) k.Flowerbed(p, k.Phase);
                if (i + 1 < beds)
                {
                    // A fountain pool between two beds: dark water in a stone kerb, a fountain wall across it.
                    var pr = k.Box(b1 + 0.8f, b1 + pool - 0.8f, -wb * 0.5f, wb * 0.5f);
                    k.Lay(PFeature, pr.Ring, k.P.Inlay);
                    k.Lay(PKerb - 1, PavingShapes.Frame(k.Box(b1 + 0.2f, b1 + pool - 0.2f, -wb * 0.5f - 0.4f, wb * 0.5f + 0.4f).Ring, 0.6f), k.P.Band);
                    k.FountainWall(k.W(b1 + pool * 0.5f, -wb * 0.5f + 1f), k.W(b1 + pool * 0.5f, wb * 0.5f - 1f));
                }
            }
            foreach (float sd in new[] { -1f, 1f })
            {
                float vt = sd * (wb * 0.5f + 5.6f);
                int t = 0;
                foreach (var p in LeisureKit.Along(k.W(c0 + 1f, vt), k.W(c1, vt), 7f))
                {
                    if (k.Tree(p, true, PlazaArt.LimeTrees) && (t & 1) == 0) k.Seat(p - k.S * (sd * 2.2f), -k.S * sd);
                    else k.Light(p - k.S * (sd * 1.8f));
                    t++;
                }
            }
            // Either side of the channel: lawns under a loose grid of trees, benches along the walks looking at the beds.
            foreach (float sd in new[] { -1f, 1f })
            {
                float l0 = wb * 0.5f + 8f, l1 = math.min(ib - 1.5f, wb * 0.5f + 34f);
                if (l1 - l0 < 6f || c1 - c0 < 10f) continue;
                var lawn = k.Box(c0 + 1f, c1 - 1f, sd > 0 ? l0 : -l1, sd > 0 ? l1 : -l0);
                k.Lay(PGreen, lawn.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                k.ShrubBorder(lawn.Ring, 1.2f, 4.5f);
                for (float u = c0 + 5f; u < c1 - 3f; u += 13f)
                    for (float v = l0 + 4f; v < l1 - 2f; v += 11f) k.Tree(k.W(u + ((int)(v / 11f) & 1) * 6.5f, sd * v), false, PlazaArt.GardenTrees);
            }
            // Either side of the court: a row of trees and benches looking at it.
            if (ib - cv > 7f)
                foreach (float sd in new[] { -1f, 1f })
                {
                    float vt = sd * (cv + (ib - cv) * 0.6f);
                    foreach (var p in LeisureKit.Along(k.W(uA, vt), k.W(uB, vt), 8f))
                        if (k.Tree(p, true, PlazaArt.PlaneTrees)) k.Seat(p - k.S * (sd * 2.2f), -k.S * sd);
                }
            k.Core = 0f;
        }

        // ───────────── water steps (a cascading water plaza) ─────────────

        private static void WaterSteps(Kit k)
        {
            k.FaceUphill();
            float fw = k.Fw, prom = 4f;
            k.Leftover(fw + prom + 1f, "hedge");
            k.WallId = "EU_Wall03";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            k.RingTrees(fw + 1.5f, 9f, true, 0, true);
            float ia = k.EA - fw - prom - 0.5f, ib = k.EB - fw - prom - 0.5f;
            if (ia < 26f || ib < 16f) { Terraces(k); return; }
            const float h = WallH;
            float b = BankFor(h);
            float span = 2f * ia;
            float lb = math.clamp(span * 0.24f, 12f, 30f), lt = math.clamp(span * 0.18f, 10f, 22f);
            int n = 4;
            float lf = (span - lb - lt - n * b) / (n - 1);
            while (n > 2 && lf < 7f) { n--; lf = (span - lb - lt - n * b) / math.max(1, n - 1); }
            lf = math.min(lf, 16f);
            lt = span - lb - n * b - (n - 1) * lf;
            k.Ctx.Plan.Facts["waterLevels"] = n + 1;
            var lv = new Levels(k, ia, ib);
            var U = new float[n + 1];
            for (int i = 1; i <= n; i++)
            {
                U[i] = -ia + lb + (i - 1) * (b + lf);
                float e = (i - 1) * b;
                lv.Add(U[i], ia - e, -ib + e, ib - e, h, new float4(b, b, b, b));
            }
            lv.Emit();
            float wc = math.clamp(ib * 0.42f, 8f, 20f);
            float sw = 4f, sv = wc * 0.5f + 1.2f + sw * 0.5f;
            // Edges: a stone wall along the foot of every step (the channel's walls carry the fountains), hedges on top,
            // open at the stairs and the channel; the stepped side and end banks planted.
            for (int i = 1; i <= n; i++)
            {
                float e = (i - 1) * b;
                var gaps = new List<Gap> { new Gap(0, -sv, sw * 0.5f), new Gap(0, sv, sw * 0.5f), new Gap(0, 0f, wc * 0.5f) };
                Edges(k, U[i], ia - e, -ib + e, ib - e, new float4(b, b, b, b), false, Head.Hedge, gaps);
                // The channel's riser: dark water down the step, a wall at its foot with wall fountains spilling forward.
                var riser = Poly.EnsureCcw(new[] { k.W(U[i] - 0.3f, -wc * 0.5f), k.W(U[i] + b + 0.3f, -wc * 0.5f), k.W(U[i] + b + 0.3f, wc * 0.5f), k.W(U[i] - 0.3f, wc * 0.5f) });
                k.Lay(PFeature - 2, riser, k.P.Inlay);
                var w0 = k.W(U[i] - 0.15f, -wc * 0.5f); var w1 = k.W(U[i] - 0.15f, wc * 0.5f);
                k.Wall(w0, w1, 1);
                foreach (var p in LeisureKit.Along(w0, w1, 3.1f)) k.WallFountain(p - k.A * 0.8f, -k.A);
                foreach (float s in new[] { -1f, 1f }) k.Flight(k.W(U[i] - 0.2f, s * sv), k.W(U[i] + b + 0.2f, s * sv), sw);
            }
            // Pools on every floor between the steps (and the last pool at the bottom), framed in pale stone.
            for (int i = 0; i <= n; i++)
            {
                float p0 = i == 0 ? -ia + lb * 0.35f : U[i] + b + 0.3f;
                float p1 = i < n ? U[i + 1] - 1.6f : U[n] + b + math.clamp(lt * 0.4f, 6f, 16f);
                if (p1 - p0 < 2.5f) continue;
                var pool = k.Box(p0, p1, -wc * 0.5f + 0.4f, wc * 0.5f - 0.4f);
                k.Lay(PFeature - 1, pool.Ring, k.P.Inlay);
                k.Lay(PKerb, PavingShapes.Frame(k.Box(p0 - 0.8f, p1 + (i < n ? 0.8f : 0.8f), -wc * 0.5f - 0.6f, wc * 0.5f + 0.6f).Ring, 0.9f), k.P.Focal);
                LeisureKit.TakeRect(k.Occ, pool, 0);
                if (i == n) k.FountainWall(k.W(p1 - 0.6f, -wc * 0.5f + 1.2f), k.W(p1 - 0.6f, wc * 0.5f - 1.2f));
            }
            // Terraces either side of the water: lawns with a row of trees and benches looking at the cascade.
            for (int i = 1; i <= n; i++)
            {
                float e = i * b;
                float f0 = U[i] + b + 0.5f, f1 = i < n ? U[i + 1] - 0.5f : ia - n * b - 8.5f;
                foreach (float s in new[] { -1f, 1f })
                {
                    float vIn = sv + sw * 0.5f + 1.2f, vOut = ib - e - 1.4f;
                    if (vOut - vIn < 4f || f1 - f0 < 4f) continue;
                    var lawn = k.Box(f0 + 0.4f, f1 - 1.2f, s > 0 ? vIn : -vOut, s > 0 ? vOut : -vIn);
                    k.Lay(PGreen, lawn.Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                    float vt = s * (vIn + (vOut - vIn) * 0.55f);
                    foreach (var p in LeisureKit.Along(k.W(f0 + 1f, vt), k.W(f1 - 1f, vt), 7f)) k.Tree(p, false, PlazaArt.GardenTrees);
                    if (vOut - vIn > 12f)
                        foreach (var p in LeisureKit.Along(k.W(f0 + 4.5f, s * (vIn + 2.6f)), k.W(f1 - 4.5f, s * (vIn + 2.6f)), 2.8f)) k.Flowerbed(p, k.Phase);
                    k.ShrubBorder(lawn.Ring, 0.9f, 4f);
                    foreach (var p in LeisureKit.Along(k.W(f0 + 1.5f, s * (vIn - 0.9f)), k.W(f1 - 1.5f, s * (vIn - 0.9f)), 4.5f)) k.Seat(p, -k.S * s);
                }
            }
            // The top: an overlook terrace with a pergola canopy facing down the cascade, benches under it.
            {
                float e = (n - 1) * b;
                float t0 = U[n] + b, t1 = ia - e - b;
                float cu = t1 - 1.2f;
                if (cu - t0 > 9f)
                {
                    float half = math.min(ib - e - b - 2f, wc * 0.5f + 10f);
                    var cA = k.W(cu, -half); var cB = k.W(cu, half);
                    var strip = k.Canopy(cA, cB, 2, "PergolaModular02", true);
                    foreach (var p in LeisureKit.Along(k.W(cu - 1.5f, -half + 1.5f), k.W(cu - 1.5f, half - 1.5f), 3f)) k.Seat(p, -k.A, PlazaArt.Benches);
                    LeisureKit.TakeRect(k.Occ, strip);
                    foreach (float s in new[] { -1f, 1f }) k.Light(k.W(cu - 6.8f, s * (wc * 0.5f + 1.5f)));
                }
            }
            // The bottom: cafe tables under parasols either side of the last pool, benches looking up the water.
            {
                float b0 = -ia + 1f, b1 = U[1] - 2f;
                foreach (float s in new[] { -1f, 1f })
                {
                    float v0 = wc * 0.5f + 2.5f, v1 = math.min(ib - 2f, wc * 0.5f + 24f);
                    if (v1 - v0 > 5f) k.CafeGrid(b0 + 1f, b1, s > 0 ? v0 : -v1, s > 0 ? v1 : -v0, 5.2f, true);
                }
                foreach (var p in LeisureKit.Along(k.W(b0 + 0.6f, -wc * 0.5f + 1.5f), k.W(b0 + 0.6f, wc * 0.5f - 1.5f), 3.2f)) k.Seat(p, k.A);
            }
            k.Core = 0f;
        }

        // ───────────── market hall square ─────────────

        private static void MarketHall(Kit k)
        {
            float fw = k.Fw, prom = 5f;
            k.Leftover(fw + prom + 1f, "bosque");
            k.WallId = "EU_Wall05";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            k.RingTrees(fw + 1.6f, 8.5f, true, 0, true);
            float ia = k.EA - fw - prom - 0.5f, ib = k.EB - fw - prom - 0.5f;
            if (ia < 22f || ib < 16f) { Piazza(k); return; }
            const float h = WallH;
            float b = BankFor(h);
            float pu = ia - 3f, pv = ib - 3f;
            var lv = new Levels(k, ia, ib);
            lv.Add(-pu, pu, -pv, pv, h, b);
            lv.Emit();
            float tu = pu - b, tv = pv - b;
            // Halls 6 m wide (two pergola bays) with 9 m aisles between, a cross aisle in the middle.
            const float hall = 6f, aisle = 9f, cross = 11f;
            int nh = math.clamp((int)math.floor((2f * (tv - 2f) + aisle) / (hall + aisle)), 1, 4);
            float used = nh * hall + (nh - 1) * aisle;
            float vStart = -used * 0.5f;
            var gaps = new List<Gap>();
            for (int i = 0; i + 1 < nh; i++)
            {
                float va = vStart + i * (hall + aisle) + hall + aisle * 0.5f;
                gaps.Add(new Gap(0, va, 3f)); gaps.Add(new Gap(1, va, 3f));
            }
            if (nh == 1) { gaps.Add(new Gap(0, 0f, 3f)); gaps.Add(new Gap(1, 0f, 3f)); }
            gaps.Add(new Gap(2, 0f, cross * 0.5f - 1f)); gaps.Add(new Gap(3, 0f, cross * 0.5f - 1f));
            Edges(k, -pu, pu, -pv, pv, B4(b), false, Head.None, gaps, k.P.Band);
            // Stairs up the plinth at every gap.
            foreach (var g in gaps)
            {
                float w = g.Half * 2f - 0.6f;
                switch (g.Side)
                {
                    case 0: k.Flight(k.W(-pu - 0.3f, g.At), k.W(-pu + b + 0.3f, g.At), w); break;
                    case 1: k.Flight(k.W(pu + 0.3f, g.At), k.W(pu - b - 0.3f, g.At), w); break;
                    case 2: k.Flight(k.W(g.At, -pv - 0.3f), k.W(g.At, -pv + b + 0.3f), w); break;
                    default: k.Flight(k.W(g.At, pv + 0.3f), k.W(g.At, pv - b - 0.3f), w); break;
                }
            }
            // Box planters along the plinth's top edge between the stairs.
            foreach (float s in new[] { -1f, 1f })
            {
                k.BoxPlanterRow(k.W(-tu + 1f, s * (tv - 0.9f)), k.W(-cross * 0.5f - 1f, s * (tv - 0.9f)), 4f);
                k.BoxPlanterRow(k.W(cross * 0.5f + 1f, s * (tv - 0.9f)), k.W(tu - 1f, s * (tv - 0.9f)), 4f);
            }
            // The plinth's floor in the focal stone, the halls' floors in the accent, the aisles in the path stone.
            k.Lay(PField - 2, k.Box(-tu, tu, -tv, tv).Ring, k.P.Focal);
            int halls = 0, stalls = 0;
            for (int i = 0; i < nh; i++)
            {
                float v0 = vStart + i * (hall + aisle), v1 = v0 + hall;
                foreach (var (h0, h1) in new[] { (-tu + 2f, -cross * 0.5f), (cross * 0.5f, tu - 2f) })
                {
                    if (h1 - h0 < 6f) continue;
                    var floor = k.Box(h0, h1, v0, v1);
                    k.Lay(PFeature + 2, floor.Ring, k.P.Accent);
                    k.Lay(PKerb, PavingShapes.Frame(floor.Ring, 0.5f), k.P.Band);
                    // Two rows of tables under the hall, then the roof of pergola bays over them.
                    foreach (float vr in new[] { v0 + 1.6f, v1 - 1.6f })
                        foreach (var p in LeisureKit.Along(k.W(h0 + 1.5f, vr), k.W(h1 - 1.5f, vr), 4.2f))
                            k.CafeSet(p, k.Phase, false, true);
                    k.Canopy(k.W(h0, v0), k.W(h1, v0), 2, "PergolaModularOpen07", true);
                    LeisureKit.TakeRect(k.Occ, floor);
                    halls++;
                }
                // Stalls along the aisle on this hall's far side, facing into it.
                if (i + 1 < nh)
                {
                    float va = v1 + 2.6f;
                    foreach (var (h0, h1) in new[] { (-tu + 3f, -cross * 0.5f - 1f), (cross * 0.5f + 1f, tu - 3f) })
                    {
                        int j = 0;
                        foreach (var p in LeisureKit.Along(k.W(h0, va), k.W(h1, va), 6.5f))
                            if (k.Stall(p, k.S, (j++ % 3) == 2)) stalls++;
                    }
                    foreach (var p in LeisureKit.Along(k.W(-tu + 3f, v1 + aisle - 1.4f), k.W(tu - 3f, v1 + aisle - 1.4f), 9f))
                        if (math.abs(math.dot(p - k.C, k.A)) > cross * 0.5f + 1f) k.Light(p);
                }
            }
            k.Ctx.Plan.Facts["marketHalls"] = halls;
            k.Ctx.Plan.Facts["marketStalls"] = stalls;
            // The crossing: a pavilion (or the square's centre piece) with cafe tables under parasols round it.
            if (!k.Gazebo(k.C, k.Phase)) k.Centre(k.C, 6f, "fountain");
            foreach (float s in new[] { -1f, 1f })
                foreach (float t in new[] { -1f, 1f })
                    k.CafeSet(k.W(s * 3.4f, t * (6.4f + (nh > 1 ? 0f : 2f))), k.Phase + 0.4f);
            // Benches either side of the cross aisle, looking at the pavilion.
            foreach (float s in new[] { -1f, 1f })
                foreach (var p in LeisureKit.Along(k.W(s * (cross * 0.5f - 1f), -tv + 2f), k.W(s * (cross * 0.5f - 1f), tv - 2f), 3.4f))
                    if (math.abs(math.dot(p - k.C, k.S)) > 9f) k.Seat(p, -k.A * s);
            // Flags on the plinth's corners.
            foreach (float s in new[] { -1f, 1f })
                foreach (float t in new[] { -1f, 1f })
                {
                    k.BigFlag(k.W(s * (tu - 1.2f), t * (tv - 1.2f)), true);
                    k.Light(k.W(s * (pu + 1.4f), t * (pv + 1.4f)));
                }
            // The field round the plinth: benches facing it between the trees, cafe tables at the far ends.
            foreach (float s in new[] { -1f, 1f })
                foreach (var p in LeisureKit.Along(k.W(-pu + 4f, s * (pv + 2.2f)), k.W(pu - 4f, s * (pv + 2.2f)), 7f))
                    if (math.abs(math.dot(p - k.C, k.A)) > cross * 0.5f + 2f) k.Seat(p, -k.S * s);
            k.Core = 0f;
        }

        // ───────────── dining terrace ─────────────

        private static void Dining(Kit k)
        {
            float fw = k.Fw, prom = 4f;
            k.Leftover(fw + prom + 1f, "bosque");
            k.WallId = "EU_Wall01";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            k.RingTrees(fw + 1.5f, 9f, true, 0, true);
            float ia = k.EA - fw - prom - 0.5f, ib = k.EB - fw - prom - 0.5f;
            if (ia < 22f || ib < 18f) { Piazza(k); return; }
            const float h = 1.8f;
            float b = BankFor(h);
            float vt = math.max(-ib + 2f * ib * 0.42f, ib - 30f);
            // A big square gets a second terrace facing the first across the grove (2026-10-29, game runner: on a 220 x 200
            // site one terrace left a forest of 150 trees below it).
            bool two = vt + ib > 70f;
            var lv = new Levels(k, ia, ib);
            lv.Add(-ia, ia, vt, ib, h, b);
            if (two) lv.Add(-ia, ia, -ib, -vt, h, b);
            lv.Emit();
            float g = math.min(7f, ia * 0.12f);
            DiningTerrace(k, ia, ib, vt, b, g);
            if (two) { k.Flip(); DiningTerrace(k, ia, ib, vt, b, g); k.Flip(); }
            k.Ctx.Plan.Facts["diningTerraces"] = two ? 2 : 1;

            // The lower square: a cafe grove (tables under a grid of trees) round a fountain, planters along the wall.
            float l0 = two ? -vt + 1.5f : -ib + 1f, l1 = vt - 1.5f;
            var cc = k.W(0f, (l0 + l1) * 0.5f);
            float room = math.min((l1 - l0) * 0.5f - 1f, 9f);
            float keep = k.Centre(cc, room, "fountain");
            k.SeatRing(cc, keep + 1.2f, 8, true);
            k.Lay(PFeature + 2, PavingShapes.Polygon(cc, keep + 4f, 32, k.Phase), k.P.Focal);
            k.Lay(PKerb, PavingShapes.Annulus(cc, keep + 4f, 0.9f), k.P.Band);
            const float pitch = 12f;
            int trees = 0;
            for (float u = -ia + 4f; u <= ia - 4f + 0.01f; u += pitch)
                for (float v = l0 + 3f; v <= l1 - 2f + 0.01f; v += pitch)
                {
                    var p = k.W(u, v);
                    if (math.distance(p, cc) < keep + 6f || math.abs(u) < g + 1f) continue;
                    if (k.Tree(p, true, PlazaArt.PlaneTrees)) trees++;
                    if (((int)math.round(u / pitch) + (int)math.round(v / pitch) & 1) == 0) k.CafeSet(p + k.A * (pitch * 0.5f) + k.S * (pitch * 0.5f), k.Phase + 0.3f, false, true);
                }
            k.Ctx.Plan.Facts["groveTrees"] = trees;
            k.Core = keep;
        }

        /// <summary>One raised dining terrace along the +S side of the frame (Dining flips the frame for the second).</summary>
        private static void DiningTerrace(Kit k, float ia, float ib, float vt, float b, float g)
        {
            float su = ia * 0.62f;
            var gaps = new List<Gap> { new Gap(2, 0f, g), new Gap(2, -su, 2.8f), new Gap(2, su, 2.8f) };
            Edges(k, -ia, ia, vt, ib, B4(b), false, Head.Rail, gaps, default, 2);
            k.Flight(k.W(0f, vt - 0.3f), k.W(0f, vt + b + 0.3f), g * 2f - 0.6f);
            foreach (float s in new[] { -1f, 1f }) k.Flight(k.W(s * su, vt - 0.3f), k.W(s * su, vt + b + 0.3f), 5f);
            // Flowers on the bank either side of the stairs, planters along the foot of the wall.
            foreach (float s in new[] { -1f, 1f })
            {
                foreach (var p in LeisureKit.Along(k.W(s * (g + 2f), vt + b * 0.5f), k.W(s * (ia - b - 1f), vt + b * 0.5f), 3.2f))
                    if (math.abs(math.abs(math.dot(p - k.C, k.A)) - su) > 4.5f) k.Flowerbed(p, k.Phase);
                k.BoxPlanterRow(k.W(s * (g + 2f), vt - 1.4f), k.W(s * (ia - 2f), vt - 1.4f), 4.5f);
            }
            // The terrace: rows of tables under big parasols, a deep pergola canopy along the back with tables under it.
            float t0 = vt + b, t1 = ib - b;
            k.Lay(PField - 2, k.Box(-ia + b, ia - b, t0, t1).Ring, k.P.Focal);
            float canopyV = t1 - 0.6f;
            if (t1 - t0 > 14f)
            {
                float cA = -ia + b + 2f, cB = ia - b - 2f;
                foreach (float vr in new[] { canopyV - 1.6f, canopyV - 4.4f })
                    foreach (var p in LeisureKit.Along(k.W(cA + 1.5f, vr), k.W(cB - 1.5f, vr), 3f)) k.CafeSet(p, k.Phase, false, true);
                var strip = k.Canopy(k.W(cB, canopyV), k.W(cA, canopyV), 2, "PergolaModular02", true);
                LeisureKit.TakeRect(k.Occ, strip);
                k.Lay(PFeature + 2, strip.Ring, k.P.Accent);
                canopyV -= 7f;
            }
            k.CafeGrid(-ia + b + 1.5f, ia - b - 1.5f, t0 + 1.8f, canopyV, 6.2f, true);
            foreach (float s in new[] { -1f, 1f }) k.Light(k.W(s * (g + 1.2f), t0 + 1f));
        }

        // ───────────── grand stairs (the Spanish Steps) ─────────────

        private static void GrandSteps(Kit k)
        {
            k.FaceUphill();
            float fw = k.Fw, prom = 4f;
            k.Leftover(fw + prom + 1f, "hedge");
            k.WallId = "EU_Wall03";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + prom), k.P.Path);
            k.RingTrees(fw + 1.5f, 9f, true, 0, true);
            float ia = k.EA - fw - prom - 0.5f, ib = k.EB - fw - prom - 0.5f;
            if (ia < 30f || ib < 22f) { Terraces(k); return; }
            const float rise = 1.5f;
            float r = BankFor(rise);
            const int n = 3;
            const float land = 5f;
            float lb = math.clamp(2f * ia * 0.34f, 18f, 44f);
            float top = 2f * ia - lb - n * r - (n - 1) * land;
            if (top < 16f) lb = math.max(14f, lb - (16f - top));
            var U = new float[n + 1];
            var lv = new Levels(k, ia, ib);
            for (int i = 1; i <= n; i++)
            {
                U[i] = -ia + lb + (i - 1) * (r + land);
                float e = (i - 1) * r;
                lv.Add(U[i], ia - e, -ib + e, ib - e, rise, B4(r));
            }
            lv.Emit();
            k.Ctx.Plan.Facts["grandFlights"] = n;
            // Flights narrowing as they climb, landings between them; the banks either side planted, walls at their feet.
            float sw0 = math.min(ib * 0.34f, 12f);
            for (int i = 1; i <= n; i++)
            {
                float e = (i - 1) * r;
                float sw = sw0 * (1f - 0.17f * (i - 1));
                Edges(k, U[i], ia - e, -ib + e, ib - e, B4(r), false, Head.Hedge, new List<Gap> { new Gap(0, 0f, sw) });
                k.Flight(k.W(U[i] - 0.3f, 0f), k.W(U[i] + r + 0.3f, 0f), sw * 2f, true, true, k.P.Focal);
                // The landing above this flight: paved, pots along its edges, flowerbeds in its corners.
                float l0 = U[i] + r + 0.3f, l1 = i < n ? U[i + 1] - 0.3f : U[i] + r + 6f;
                float half = i < n ? sw0 * (1f - 0.17f * i) + 2f : sw + 2f;
                k.Lay(PFeature + 1, k.Box(l0, l1, -half, half).Ring, k.P.Focal);
                foreach (float s in new[] { -1f, 1f })
                {
                    foreach (var p in LeisureKit.Along(k.W(l0 + 0.6f, s * (half - 0.6f)), k.W(l1 - 0.6f, s * (half - 0.6f)), 1.6f)) k.Pot(p);
                    if (i < n) k.Flowerbed(k.W((l0 + l1) * 0.5f, s * (half - 2.4f)), k.Phase);
                }
            }
            // The belvedere on top: a tall flag on the axis at the head of the steps, a pavilion, two rows of trees, benches.
            {
                float e = (n - 1) * r;
                float t0 = U[n] + r, t1 = ia - e - r, tv = ib - e - r;
                k.BigFlag(k.W(t0 + 3.2f, 0f), true);
                if (t1 - t0 > 16f) k.Gazebo(k.W(t1 - 6f, 0f), k.Phase);
                foreach (float s in new[] { -1f, 1f })
                {
                    foreach (float tr in new[] { 4f, 11f })
                        foreach (var p in LeisureKit.Along(k.W(t0 + 6f, s * (tv - tr)), k.W(t1 - 3f, s * (tv - tr)), 7.5f)) k.Tree(p, true, PlazaArt.PlaneTrees);
                    if (tv - 14f > sw0 + 6f) k.CafeGrid(t0 + 6f, math.min(t1 - 10f, t0 + 23f), s > 0 ? sw0 + 4f : -(tv - 15f), s > 0 ? tv - 15f : -(sw0 + 4f), 5.4f, true);
                    foreach (var p in LeisureKit.Along(k.W(t0 + 2.9f, s * (sw0 * 0.66f + 3f)), k.W(t0 + 2.9f, s * (tv - 1f)), 3.2f)) k.Seat(p, -k.A);
                    k.Light(k.W(t0 + 1f, s * (sw0 * 0.66f + 1.5f)));
                }
                // Behind the cafes: a bosque of plane trees in pits (the Pincio's groves), the axis to the pavilion left open.
                for (float u = t0 + 31f; u < t1 - 12f; u += 9f)
                    for (float v = -tv + 6f; v <= tv - 6f; v += 9f)
                        if (math.abs(v) > 6f) k.Tree(k.W(u, v), true, PlazaArt.PlaneTrees);
                k.Lay(PField - 2, k.Box(t0, t1, -tv, tv).Ring, k.P.Path);
            }
            // The piazza at the foot: a fountain on the axis, cafe tables under parasols along its sides, benches facing the steps.
            {
                float p0 = -ia + 1f, p1 = U[1] - 1f;
                var fc = k.W((p0 + p1) * 0.5f - 1f, 0f);
                float room = math.min((p1 - p0) * 0.5f - 2f, 10f);
                float keep = k.Centre(fc, room, "fountain");
                k.Lay(PFeature + 2, PavingShapes.Polygon(fc, keep + 3.5f, 32, k.Phase), k.P.Focal);
                k.Lay(PKerb, PavingShapes.Annulus(fc, keep + 3.5f, 0.9f), k.P.Band);
                k.SeatRing(fc, keep + 1.6f, 8, true);
                foreach (float s in new[] { -1f, 1f })
                {
                    float v0 = math.max(sw0 + 2.5f, keep + 5f), v1 = math.min(ib - 1.5f, v0 + 17f);
                    if (v1 - v0 > 5f) k.CafeGrid(p0 + 1f, p1 - 2f, s > 0 ? v0 : -v1, s > 0 ? v1 : -v0, 5.2f, true);
                    k.Light(k.W(p1 - 0.8f, s * (sw0 + 1f)));
                }
            }
            k.Core = 0f;
        }
    }
}
