// Designed plazas, round 2 (2026-10-29, the user: "polish weak plazas ... play more with vanilla assets, large
// overhangs, umbrellas, play a LOT more with terrain height"). The kit's second half:
//
//   Levels    the square's ground as a sum of raised or sunken rectangles on its own axes (u along A, v along S),
//             each with a bank on every side, over a base plane fitted to the ground (flat on flat land, the
//             slope's own plane on a slope, so a hillside keeps its fall and the steps keep their height). It is
//             emitted as sloped terraform patches cut on every bank line (flat floors and planar banks are one quad
//             each, a mitred bank corner two triangles), so the game gets exact planes and crisp fold lines, never
//             cells. Banks stay at or under 45 % (TerrainMetrics.SheetGrade: steeper than 50 % over one heightmap
//             texel renders as a vertical sheet). The objects stand on it (Ground.Override).
//   walls     vanilla EU_Wall pieces (0.9 m, 5/3/1/0.5 m long) laid end to end: a stone plinth at the foot of a bank,
//             a parapet, the drum of a fountain, the cheek of a stair (short pieces stepping up with it).
//   flights   stone stairs across a bank: a paved flight with dark nosings every tread, cheek walls, lamps at the top.
//   props     cafe tables under big parasols, rows of modular pergolas (canopies two bays deep), gazebos, market
//             stalls, flags, raised beds, wall fountains. Every id is vanilla (ModsData catalog-dump, 2026-10-28).
//
// The clean game has no free-standing fountain or statue: a fountain is a drum of walls with wall fountains on it.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        // Vanilla props of the designed squares (bounds from the catalog dump: walls 0.19 x 0.9 m, pergola bays 3 x 3 m).
        private static readonly string[] Walls = { "EU_Wall01", "EU_Wall03", "EU_Wall05" };
        private static readonly string[] WallFountains = { "WallFountain01", "WallFountain03", "WallFountain02" };
        private static readonly string[] Railings = { "EU_Railing03", "EU_Railing01", "EU_Railing05" };
        private static readonly string[] TableSets = { "TableSet01", "TableSet03", "TableSet02" };
        private static readonly string[] SmallTables = { "TableSetCommercial04", "TableSetCommercial03", "TableSetCommercial05" };
        private static readonly string[] BigParasols = { "Parasol01", "Parasol03", "Parasol02" };
        private static readonly string[] Gazebos = { "Gazebo02", "Gazebo01", "Gazebo03" };
        private static readonly string[] StallIds = { "FoodStall01", "FoodStall03", "FoodStall02" };
        private static readonly string[] KioskIds = { "Kiosk01", "Kiosk02", "Kiosk03" };
        private static readonly string[] HighHedges = { "HedgePieceHigh01", "HedgePieceHigh02" };
        private static readonly string[] GreenCityBushes = { "GreenBushCity01", "GreenBushCity02", "GreenBushCity03" };
        private static readonly string[] BoxPlanters = { "GreenBushCity01", "FlowerBushCity01", "GreenBushCity02", "FlowerBushCity02" };
        private static readonly string[] RaisedBedIds = { "GardenBed02WideHigh", "GardenBed03WideHigh", "GardenBed01WideHigh" };
        internal const float WallH = 0.9f;
        /// <summary>Steepest bank (m/m): just under the slope that renders as a sheet over one heightmap texel.</summary>
        internal const float BankGrade = 0.45f;

        internal static float BankFor(float h) => math.max(2f, math.abs(h) / BankGrade);

        /// <summary>The square's designed ground (see the file header).</summary>
        internal sealed class Levels
        {
            // Abs: a level floor at height T (sports grounds, 2026-10-29), its banks blending from the ground under it.
            private struct Plat { public float U0, U1, V0, V1, Dh, T; public float4 B; public bool Abs; }
            private readonly Kit _k;
            private readonly List<Plat> _p = new List<Plat>();
            public readonly float IA, IB;
            public float G0 { get; private set; }
            public float2 Grad { get; private set; }

            public Levels(Kit k, float ia, float ib)
            {
                _k = k; IA = ia; IB = ib;
                double sh = 0, su = 0, sv = 0, suu = 0, svv = 0; int n = 0;
                for (int i = 0; i <= 8; i++)
                    for (int j = 0; j <= 8; j++)
                    {
                        float u = math.lerp(-ia, ia, i / 8f), v = math.lerp(-ib, ib, j / 8f);
                        float h = k.Ctx.H(k.W(u, v));
                        sh += h; su += h * u; sv += h * v; suu += u * u; svv += v * v; n++;
                    }
                G0 = (float)(sh / n);
                var g = new float2(suu > 1e-3 ? (float)(su / suu) : 0f, svv > 1e-3 ? (float)(sv / svv) : 0f);
                // Flat land stays level (a few centimetres of noise must not tilt a square).
                if (math.abs(g.x) * ia + math.abs(g.y) * ib < 0.15f) g = float2.zero;
                Grad = g;
            }

            /// <summary>A rectangle on the square's axes raised (dh &gt; 0) or sunk by dh, its banks inside it (u0 side, u1 side, v0 side, v1 side).</summary>
            public void Add(float u0, float u1, float v0, float v1, float dh, float4 bank)
            {
                if (u1 - u0 < 1f || v1 - v0 < 1f || math.abs(dh) < 0.05f) return;
                _p.Add(new Plat { U0 = u0, U1 = u1, V0 = v0, V1 = v1, Dh = dh, B = math.max(bank, 0f) });
            }

            public void Add(float u0, float u1, float v0, float v1, float dh, float bank) => Add(u0, u1, v0, v1, dh, new float4(bank));

            /// <summary>
            /// (2026-10-29) A level floor at the absolute <paramref name="height"/> (a pitch, a court, a terrace of a sports
            /// ground on a slope), its banks inside it blending from whatever ground lies under it. Later floors win.
            /// </summary>
            public void AddFloor(float u0, float u1, float v0, float v1, float height, float4 bank)
            {
                if (u1 - u0 < 1f || v1 - v0 < 1f || !math.isfinite(height)) return;
                _p.Add(new Plat { U0 = u0, U1 = u1, V0 = v0, V1 = v1, T = height, Abs = true, B = math.max(bank, 0f) });
            }

            /// <summary>The bank a floor at <paramref name="height"/> over (u0..u1, v0..v1) needs to stay at or under 45 % against the ground beneath it.</summary>
            public float FloorBank(float u0, float u1, float v0, float v1, float height, float grade = BankGrade)
            {
                float worst = 0f;
                for (int i = 0; i <= 8; i++)
                {
                    float t = i / 8f;
                    foreach (var (u, v) in new[] { (math.lerp(u0, u1, t), v0), (math.lerp(u0, u1, t), v1), (u0, math.lerp(v0, v1, t)), (u1, math.lerp(v0, v1, t)) })
                        worst = math.max(worst, math.abs(H(u, v) - height));
                }
                return worst < 0.05f ? 0f : math.max(2f, worst / grade);
            }

            /// <summary>Lowest and highest designed ground along the boundary of a box (local).</summary>
            public float2 RimRange(float u0, float u1, float v0, float v1)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i <= 12; i++)
                {
                    float t = i / 12f;
                    foreach (var (u, v) in new[] { (math.lerp(u0, u1, t), v0), (math.lerp(u0, u1, t), v1), (u0, math.lerp(v0, v1, t)), (u1, math.lerp(v0, v1, t)) })
                    {
                        float h = H(u, v);
                        lo = math.min(lo, h); hi = math.max(hi, h);
                    }
                }
                return new float2(lo, hi);
            }

            /// <summary>(2026-10-29) A level base at <paramref name="g0"/> (sports grounds play on level floors, never on the slope's plane).</summary>
            public void LevelBase(float g0) { G0 = g0; Grad = float2.zero; }

            /// <summary>(2026-10-29) Width of the band along the box's edge where the design blends back to the ground as it is (0 = none).</summary>
            public float Skirt;
            /// <summary>(2026-10-29) Non-planar quads as four triangles round their centre (sports grounds) instead of two.</summary>
            public bool FineCorners;

            public int Count => _p.Count;
            public float Base(float u, float v) => G0 + Grad.x * u + Grad.y * v;

            /// <summary>Designed height above (below) the base plane.</summary>
            public float Rel(float u, float v) => H(u, v) - Base(u, v);

            private static float Side(float d, float b) => b <= 0.01f ? 1f : math.saturate(d / b);

            public float H(float u, float v)
            {
                float h = Base(u, v);
                foreach (var q in _p)
                {
                    if (u < q.U0 || u > q.U1 || v < q.V0 || v > q.V1) continue;
                    float r = 1f;
                    r = math.min(r, Side(u - q.U0, q.B.x));
                    r = math.min(r, Side(q.U1 - u, q.B.y));
                    r = math.min(r, Side(v - q.V0, q.B.z));
                    r = math.min(r, Side(q.V1 - v, q.B.w));
                    h = q.Abs ? math.lerp(h, q.T, r) : h + q.Dh * r;
                }
                if (Skirt > 0.01f)
                {
                    // (2026-10-29) The design meets the ground as it is at the box's edge (a sports ground on hills: the base
                    // plane is only the hill's mean, its edge would stand off the ground in a curtain).
                    float d = math.min(IA - math.abs(u), IB - math.abs(v));
                    if (d < Skirt)
                    {
                        // Only the base plane's residual is taken out: the design's own banks keep their grade.
                        float t = 1f - math.saturate(d / Skirt);
                        h -= t * (Base(u, v) - _k.Ctx.H(_k.W(u, v)));
                    }
                }
                return h;
            }
            public float At(float2 p) { var l = _k.Local(p); return H(l.x, l.y); }
            public bool Inside(float2 p) { var l = _k.Local(p); return math.abs(l.x) <= IA + 0.01f && math.abs(l.y) <= IB + 0.01f; }

            /// <summary>Sloped terraform patches for the field and the ground the objects stand on. Returns the patch count.</summary>
            public int Emit()
            {
                if (_p.Count == 0) return 0;
                var us = Breaks(true); var vs = Breaks(false);
                int patches = 0;
                float maxUp = 0f, maxDown = 0f;
                for (int j = 0; j + 1 < vs.Count; j++)
                {
                    if ((j & 7) == 0) _k.Ctx.Check();
                    float v0 = vs[j], v1 = vs[j + 1];
                    // A run of coplanar quads along u becomes one patch.
                    int runStart = -1; float3 plane = default;
                    for (int i = 0; i + 1 <= us.Count - 1; i++)
                    {
                        float u0 = us[i], u1 = us[i + 1];
                        float h00 = H(u0, v0), h10 = H(u1, v0), h11 = H(u1, v1), h01 = H(u0, v1);
                        float hc = H((u0 + u1) * 0.5f, (v0 + v1) * 0.5f);
                        bool planar = math.abs(h00 + h11 - h10 - h01) < 0.01f && math.abs(hc - (h00 + h10 + h11 + h01) * 0.25f) < 0.01f;
                        if (planar)
                        {
                            float du = u1 - u0, dv = v1 - v0;
                            var pl = new float3((h10 - h00) / du, (h01 - h00) / dv, 0f);
                            pl.z = h00 - pl.x * u0 - pl.y * v0;
                            if (runStart >= 0 && math.all(math.abs(pl - plane) < new float3(1e-3f, 1e-3f, 0.01f))) continue;
                            if (runStart >= 0) patches += Patch(us[runStart], u0, v0, v1, ref maxUp, ref maxDown);
                            runStart = i; plane = pl;
                            continue;
                        }
                        if (runStart >= 0) { patches += Patch(us[runStart], u0, v0, v1, ref maxUp, ref maxDown); runStart = -1; }
                        // A mitred corner: two triangles on the diagonal the fold runs along.
                        float e02 = math.abs(H(u0 + (u1 - u0) * 2f / 3f, v0 + (v1 - v0) / 3f) - (h00 + h10 + h11) / 3f)
                                  + math.abs(H(u0 + (u1 - u0) / 3f, v0 + (v1 - v0) * 2f / 3f) - (h00 + h11 + h01) / 3f);
                        float e13 = math.abs(H(u0 + (u1 - u0) / 3f, v0 + (v1 - v0) / 3f) - (h00 + h10 + h01) / 3f)
                                  + math.abs(H(u0 + (u1 - u0) * 2f / 3f, v0 + (v1 - v0) * 2f / 3f) - (h10 + h11 + h01) / 3f);
                        var a = new float2(u0, v0); var b = new float2(u1, v0); var c = new float2(u1, v1); var d = new float2(u0, v1);
                        if (FineCorners)
                        {
                            // (2026-10-29) Four triangles round the true centre: exact on a mitre whichever way its fold runs, and never
                            // the steep plane two crossing banks make when one triangle spans both.
                            var m = (a + c) * 0.5f;
                            patches += Tri(a, b, m, ref maxUp, ref maxDown); patches += Tri(b, c, m, ref maxUp, ref maxDown);
                            patches += Tri(c, d, m, ref maxUp, ref maxDown); patches += Tri(d, a, m, ref maxUp, ref maxDown);
                        }
                        else if (e02 <= e13) { patches += Tri(a, b, c, ref maxUp, ref maxDown); patches += Tri(a, c, d, ref maxUp, ref maxDown); }
                        else { patches += Tri(a, b, d, ref maxUp, ref maxDown); patches += Tri(b, c, d, ref maxUp, ref maxDown); }
                    }
                    if (runStart >= 0) patches += Patch(us[runStart], us[us.Count - 1], v0, v1, ref maxUp, ref maxDown);
                }
                var k = _k;
                k.Ctx.Ground.Override(k.Region.Min, k.Region.Max, p => Inside(p) ? At(p) : float.NaN);
                if (patches > 0)
                {
                    k.Ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
                    // The ground is the design: the site harmony pass must not smooth the banks or pad the walls.
                    k.Ctx.Plan.Facts[Placemaker.Core.Build.SiteHarmony.OwnGroundFact] = 1;
                    k.Ctx.Plan.Facts["designTerrainPatches"] = patches;
                    k.Ctx.Plan.Facts["designRise"] = (float)Math.Round(maxUp, 2);
                    k.Ctx.Plan.Facts["designDrop"] = (float)Math.Round(maxDown, 2);
                }
                return patches;
            }

            private List<float> Breaks(bool u)
            {
                float ext = u ? IA : IB;
                var l = new List<float> { -ext, ext };
                foreach (var q in _p)
                {
                    float lo = u ? q.U0 : q.V0, hi = u ? q.U1 : q.V1;
                    float b0 = u ? q.B.x : q.B.z, b1 = u ? q.B.y : q.B.w;
                    foreach (var x in new[] { lo, lo + b0, hi - b1, hi })
                        if (x > -ext && x < ext) l.Add(x);
                }
                if (Skirt > 0.01f)
                    for (float x = 0f; x <= Skirt + 0.01f; x += math.max(2f, Skirt / 3f)) { l.Add(-ext + x); l.Add(ext - x); }
                // (2026-10-29) Sports grounds: a cut every 8 m as well, so the skirt follows a hill and no patch spans a whole site.
                if (FineCorners)
                    for (float x = -ext + 8f; x < ext - 4f; x += 8f) l.Add(x);
                l.Sort();
                var r = new List<float>();
                float merge = FineCorners ? 0.4f : 0.05f;
                foreach (var x in l) if (x >= -ext - 0.01f && x <= ext + 0.01f && (r.Count == 0 || x - r[r.Count - 1] > merge)) r.Add(x);
                return r;
            }

            private int Patch(float u0, float u1, float v0, float v1, ref float up, ref float down) =>
                Emit(new[] { new float2(u0, v0), new float2(u1, v0), new float2(u1, v1), new float2(u0, v1) }, ref up, ref down);

            private int Tri(float2 a, float2 b, float2 c, ref float up, ref float down) => Emit(new[] { a, b, c }, ref up, ref down);

            private int Emit(float2[] local, ref float up, ref float down)
            {
                // Unchanged ground (the field round the levels on flat land) needs no patch.
                bool same = true; float rel = 0f;
                var ring = new float3[local.Length];
                float2 cen = 0f;
                for (int i = 0; i < local.Length; i++)
                {
                    var w = _k.W(local[i].x, local[i].y);
                    float h = H(local[i].x, local[i].y);
                    ring[i] = new float3(w.x, h, w.y);
                    cen += local[i];
                    float r = Rel(local[i].x, local[i].y);
                    rel += r;
                    up = math.max(up, r); down = math.max(down, -r);
                    if (math.abs(r) > 0.01f || math.abs(h - _k.Ctx.H(w)) > 0.05f) same = false;
                }
                cen /= local.Length;
                if (same && math.abs(H(cen.x, cen.y) - _k.Ctx.H(_k.W(cen.x, cen.y))) <= 0.05f) return 0;
                // CCW in the world (A, S is a right-handed frame: local CCW stays CCW).
                var flat = Poly.To2D(ring);
                if (Poly.SignedArea(flat) < 0f) Array.Reverse(ring);
                var wc = _k.W(cen.x, cen.y);
                float target = H(cen.x, cen.y);
                _k.Ctx.Plan.Terraform.Add(new PlannedTerraform
                {
                    Ring = ring, Kind = target >= _k.Ctx.H(wc) ? TerraformKind.Raise : TerraformKind.Lower,
                    TargetHeight = target, Permanent = true, Sloped = true,
                });
                return 1;
            }
        }

        internal sealed partial class Kit
        {
            public string WallId = "EU_Wall01", RailId = "EU_Railing01";
            public int WallCount, FlightCount, CafeCount, CanopyCount, FountainCount;

            /// <summary>Local (u, v) of a world point.</summary>
            public float2 Local(float2 p) { var d = p - C; return new float2(math.dot(d, A), math.dot(d, S)); }

            /// <summary>Turns the frame half round (A and S negated): a design that rises along +A then climbs the slope.</summary>
            public void Flip() { A = -A; S = -S; Phase += math.PI; }

            /// <summary>(2026-10-29) Turns the frame a quarter (A becomes S): the box's half sizes swap with it.</summary>
            public void TurnQuarter() { var a = A; A = S; S = -a; var h = HA; HA = HB; HB = h; Phase = math.atan2(A.y, A.x); }

            /// <summary>On a slope, points +A uphill (for designs that rise along A).</summary>
            public void FaceUphill()
            {
                float lo = Ctx.H(W(-HA * 0.6f, 0f)), hi = Ctx.H(W(HA * 0.6f, 0f));
                if (lo > hi + 0.3f) Flip();
            }

            // ── walls, railings, stairs ──

            /// <summary>Wall pieces end to end from a to b (5, 3, 1 and 0.5 m pieces), <paramref name="courses"/> high (0.9 m each).</summary>
            public void Wall(float2 a, float2 b, int courses = 1, string id = null, float sink = 0f, bool take = true)
            {
                float len = math.distance(a, b);
                if (len < 0.4f) return;
                var d = (b - a) / len;
                string fam = id ?? WallId;
                var pieces = new List<(float l, string id)>();
                float rest = len;
                while (rest >= 5f) { pieces.Add((5f, fam + "Long")); rest -= 5f; }
                if (rest >= 3f) { pieces.Add((3f, fam)); rest -= 3f; }
                while (rest >= 0.95f) { pieces.Add((1f, fam + "Short")); rest -= 1f; }
                if (rest >= 0.3f) { pieces.Add((0.5f, fam + "ExtraShort")); rest -= 0.5f; }
                float total = 0f; foreach (var pc in pieces) total += pc.l;
                float t = (len - total) * 0.5f;
                float yaw = math.atan2(d.x, d.y);
                foreach (var pc in pieces)
                {
                    var p = a + d * (t + pc.l * 0.5f);
                    for (int c = 0; c < math.max(1, courses); c++)
                    {
                        var o = Ctx.Object(p, yaw, ObjectRole.Furniture, new AssetRef(pc.id, fam + "Long", "EU_Wall01Long"), V(0x3A11u), -1, AssetCategories.RuinsWall);
                        o.Elevation = c * WallH - sink;
                    }
                    WallCount++;
                    t += pc.l;
                }
                if (take) Occ.TakeCorridor(a, b, 0.35f, 2);
            }

            /// <summary>A wall from a to b with openings (centre along the line, half width) left out.</summary>
            public void WallWithGaps(float2 a, float2 b, IEnumerable<(float at, float half)> gaps, int courses = 1, string id = null)
            {
                float len = math.distance(a, b);
                if (len < 0.5f) return;
                var d = (b - a) / len;
                var cuts = new List<(float, float)>();
                if (gaps != null) foreach (var (at, half) in gaps) cuts.Add((at - half, at + half));
                cuts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                float s = 0f;
                foreach (var (c0, c1) in cuts)
                {
                    if (c0 > s + 0.4f) Wall(a + d * s, a + d * math.min(c0, len), courses, id);
                    s = math.max(s, c1);
                }
                if (len - s > 0.4f) Wall(a + d * s, b, courses, id);
            }

            /// <summary>Railing pieces (5, 3 and 1 m) from a to b.</summary>
            public void Railing(float2 a, float2 b, string id = null)
            {
                float len = math.distance(a, b);
                if (len < 0.8f) return;
                var d = (b - a) / len;
                string fam = id ?? RailId;
                float t = 0f;
                float yaw = math.atan2(d.x, d.y);
                while (len - t >= 0.9f)
                {
                    float l = len - t >= 5f ? 5f : len - t >= 3f ? 3f : 1f;
                    string pid = l >= 5f ? fam + "Long" : l >= 3f ? fam : fam + "Short";
                    Ctx.Object(a + d * (t + l * 0.5f), yaw, ObjectRole.Furniture, new AssetRef(pid, fam + "Long", "EU_Railing01Long"), V(0x3A12u), -1, AssetCategories.Fence);
                    t += l;
                }
                Occ.TakeCorridor(a, b, 0.3f, 2);
            }

            /// <summary>
            /// Stone stairs from <paramref name="foot"/> up to <paramref name="head"/>: a flight paved in the path stone,
            /// a dark nosing every tread, short wall pieces stepping up both cheeks, lamps at the top, pots at the foot.
            /// </summary>
            public void Flight(float2 foot, float2 head, float width, bool cheeks = true, bool lamps = true, AssetRef stone = default)
            {
                float len = math.distance(foot, head);
                if (len < 0.8f) return;
                var d = (head - foot) / len;
                var n = new float2(-d.y, d.x);
                var st = stone.Id != null ? stone : P.Path;
                Lay(PFeature - 3, PavingShapes.Band(foot - d * 0.6f, head + d * 0.6f, width), st);
                float tread = math.clamp(len / math.max(1f, math.round(len / 0.9f)), 0.8f, 1.1f);
                for (float t = tread * 0.5f; t < len; t += tread)
                {
                    var l0 = foot + d * t - n * (width * 0.5f - 0.15f); var l1 = foot + d * t + n * (width * 0.5f - 0.15f);
                    Lay(PFeature - 4, PavingShapes.Band(l0, l1, 0.46f), P.Inlay);
                    // (Game runner: a painted nosing on a slope does not read.) A stone lip: a wall laid along the tread's
                    // level line, sunk until 0.18 m shows, so every step is a real edge.
                    Wall(l0, l1, 1, null, WallH - 0.18f, false);
                }
                if (cheeks)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var off = n * (s * (width * 0.5f + 0.15f));
                        for (float t = 0.25f; t < len; t += 0.5f)
                        {
                            var p = foot + d * t + off;
                            var o = Ctx.Object(p, math.atan2(d.x, d.y), ObjectRole.Furniture, new AssetRef(WallId + "ExtraShort", "EU_Wall01ExtraShort"), V(0x3A13u), -1, AssetCategories.RuinsWall);
                            o.Elevation = -0.12f;
                        }
                        if (lamps) Light(head + d * 0.9f + off * 1.05f);
                        Pot(foot - d * 0.8f + off);
                    }
                Occ.TakeCorridor(foot - d * 0.5f, head + d * 0.5f, width * 0.5f + 0.4f, 3);
                FlightCount++;
            }

            // ── props ──

            /// <summary>A cafe table (a set with chairs) under a big parasol; the canopy is centred over the table.</summary>
            public bool CafeSet(float2 p, float yaw, bool parasol = true, bool small = false)
            {
                if (!Occ.Free(p, 1.9f, 0.2f, 0.1f) || NearGate(p, 3.5f)) return false;
                uint v = V(0xCAF5u);
                var table = AssetMix.Pick(small ? SmallTables : TableSets, v);
                Ctx.Object(p, yaw, ObjectRole.Furniture, table, v, -1, AssetCategories.CafeSeating);
                if (parasol)
                {
                    // Parasol01-03: the pole stands at the origin, the canopy reaches 1.0-1.2 m ahead of it (+z).
                    var f = new float2(math.sin(yaw + 0.6f), math.cos(yaw + 0.6f));
                    Ctx.Object(p - f * 1.1f, yaw + 0.6f, ObjectRole.Furniture, AssetMix.Pick(BigParasols, Hashing.Mix(v, 7u)), Hashing.Mix(v, 9u), -1, AssetCategories.CafeSeating);
                }
                Occ.Take(p, 1.9f, 1);
                CafeCount++;
                return true;
            }

            /// <summary>Cafe tables on a grid in a box (u0..u1, v0..v1), skipping what is taken.</summary>
            public int CafeGrid(float u0, float u1, float v0, float v1, float pitch, bool parasol, bool small = false)
            {
                int n = 0;
                int nu = math.max(1, (int)math.floor((u1 - u0) / pitch)), nv = math.max(1, (int)math.floor((v1 - v0) / pitch));
                float su = (u1 - u0) / nu, sv = (v1 - v0) / nv;
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        var p = W(u0 + su * (i + 0.5f), v0 + sv * (j + 0.5f));
                        if (!Region.ContainsDisc(p, 2.2f)) continue;
                        if (CafeSet(p, Phase + ((i + j) & 1) * 0.785f, parasol, small)) n++;
                    }
                return n;
            }

            /// <summary>
            /// A canopy of modular pergola bays (3 x 3 m) along a to b, <paramref name="rows"/> bays deep on the left of
            /// a to b (looking from a). Returns the covered strip. The space under it is taken unless <paramref name="keepUnder"/>.
            /// </summary>
            public OrientedRect Canopy(float2 a, float2 b, int rows, string unit = "PergolaModular07", bool keepUnder = false)
            {
                float len = math.distance(a, b);
                var d = (b - a) / math.max(1e-3f, len);
                var n = new float2(-d.y, d.x);
                int bays = (int)math.floor(len / 3f);
                var strip = new OrientedRect { Center = (a + b) * 0.5f + n * (rows * 1.5f), Axis = d, HalfLength = bays * 1.5f, HalfWidth = rows * 1.5f };
                if (bays < 1) return strip;
                float t0 = (len - bays * 3f) * 0.5f;
                // A bay's x axis runs along the row: its z (yaw) points across it.
                float yaw = math.atan2(n.x, n.y);
                for (int r = 0; r < rows; r++)
                    for (int i = 0; i < bays; i++)
                    {
                        var p = a + d * (t0 + 1.5f + i * 3f) + n * (1.5f + r * 3f);
                        if (NearGate(p, 4.5f)) continue;
                        Ctx.Object(p, yaw, ObjectRole.Furniture, new AssetRef(unit, "PergolaModular07", "PergolaModular01", "Pergola02"), V(0x9E60u), -1, AssetCategories.Shelter);
                    }
                if (!keepUnder) LeisureKit.TakeRect(Occ, strip);
                CanopyCount++;
                return strip;
            }

            public bool Gazebo(float2 p, float yaw, uint salt = 0, bool force = false)
            {
                uint v = V(0x6A2Eu + salt);
                if (force) { Ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(Gazebos, v), v, -1, AssetCategories.Shelter); Occ.Take(p, 3.8f, 0); return true; }
                return LeisureKit.Put(Ctx, Occ, p, yaw, ObjectRole.Furniture, AssetMix.Pick(Gazebos, v), AssetCategories.Shelter, 3.8f, v, 0.5f, 0.2f, 0);
            }

            public bool Stall(float2 p, float2 facing, bool kiosk = false)
            {
                uint v = V(0x57A1u);
                return LeisureKit.Put(Ctx, Occ, p, math.atan2(facing.x, facing.y), ObjectRole.Furniture, AssetMix.Pick(kiosk ? KioskIds : StallIds, v),
                    kiosk ? AssetCategories.Kiosk : AssetCategories.MarketStall, 2.4f, v, 0.3f, 0.1f, 0);
            }

            public bool BigFlag(float2 p, bool tall = false) =>
                LeisureKit.Put(Ctx, Occ, p, 0f, ObjectRole.Furniture, tall ? new AssetRef("FlagPoleCommercial02", "FlagPole01") : new AssetRef("FlagPole01", "FlagPole02", "FlagPoleCommercial01"),
                    AssetCategories.MemorialFlag, 0.4f, V(0xF1A1u), 0.2f, 0.05f);

            /// <summary>A row of raised beds (2 x 2 m) from a to b.</summary>
            public void RaisedBeds(float2 a, float2 b)
            {
                float len = math.distance(a, b);
                if (len < 2f) return;
                var d = (b - a) / len;
                int n = (int)math.floor(len / 2f);
                float t0 = (len - n * 2f) * 0.5f;
                uint v = V(0x6BEDu);
                var id = AssetMix.Pick(RaisedBedIds, v);
                for (int i = 0; i < n; i++)
                    Ctx.Object(a + d * (t0 + 1f + i * 2f), math.atan2(d.x, d.y), ObjectRole.Planter, id, Hashing.Mix(v, (uint)i), -1, AssetCategories.Planter);
                Occ.TakeCorridor(a, b, 1.1f, 2);
            }

            /// <summary>Concrete-box shrub planters every <paramref name="pitch"/> metres from a to b.</summary>
            public void BoxPlanterRow(float2 a, float2 b, float pitch)
            {
                foreach (var p in LeisureKit.Along(a, b, pitch))
                {
                    uint v = V(0xB0C5u);
                    LeisureKit.Put(Ctx, Occ, p, Phase, ObjectRole.Planter, AssetMix.Pick(BoxPlanters, v), AssetCategories.Planter, 0.8f, v, 0.2f, 0.05f);
                }
            }

            public void HighHedge(float2 a, float2 b)
            {
                if (math.distance(a, b) < 2.5f) return;
                HedgeCount += GroundCover.HedgeRow(Ctx, a, b, AssetMix.Pick(HighHedges, V(0x41EDu)), AssetCategories.Hedge, ObjectRole.Bush, V(0x4EE0u), 4f, Occ);
                Occ.TakeCorridor(a, b, 0.8f, 2);
            }

            /// <summary>A wall fountain against a wall at p, spouting towards <paramref name="facing"/>.</summary>
            public void WallFountain(float2 p, float2 facing)
            {
                uint v = V(0xF0F0u);
                Ctx.Object(p, math.atan2(facing.x, facing.y), ObjectRole.Furniture, new AssetRef(AssetMix.Pick(WallFountains, v).Id, "WallFountain01"), v, -1, AssetCategories.RuinsWall);
                Occ.Take(p, 1.2f, 0);
                FountainCount++;
            }

            /// <summary>
            /// A fountain without fountain assets: an octagonal drum of walls round a dark basin, a wall fountain on every
            /// other face spouting outwards, flowers or a flag in the middle. Returns its outer radius.
            /// </summary>
            public float FountainDrum(float2 c, float apothem, bool flag)
            {
                apothem = math.max(2.4f, apothem);
                float side = 2f * apothem * math.tan(math.PI / 8f);
                float rv = apothem / math.cos(math.PI / 8f);
                var oct = new float2[8];
                for (int i = 0; i < 8; i++) { float a = Phase + math.PI / 8f + i * math.PI / 4f; oct[i] = c + new float2(math.cos(a), math.sin(a)) * rv; }
                Lay(PFeature - 6, PavingShapes.Polygon(c, apothem - 0.25f, 8, Phase + math.PI / 8f), P.Inlay);
                Lay(PFeature - 5, PavingShapes.Annulus(c, rv + 0.2f, 1.4f, 32), P.Band);
                for (int i = 0; i < 8; i++)
                {
                    var a = oct[i]; var b = oct[(i + 1) % 8];
                    Wall(a, b, 1);
                    if ((i & 1) == 0)
                    {
                        var m = (a + b) * 0.5f;
                        var outward = math.normalizesafe(m - c);
                        WallFountain(m + outward * 0.75f, outward);
                    }
                }
                if (flag) BigFlag(c, true);
                else
                    for (int i = 0; i < 4; i++)
                    {
                        float an = Phase + i * math.PI * 0.5f;
                    {
                        // (round 3, the lead: the flowerbeds in the basin drew teal-blue) green shrubs in the dry basin
                        uint gv = V(0xF1B0u);
                        Ctx.Object(c + new float2(math.cos(an), math.sin(an)) * math.min(1.4f, apothem * 0.4f), an, ObjectRole.Planter, AssetMix.Pick(GreenCityBushes, gv), gv, -1, AssetCategories.Planter);
                    }
                    }
                Occ.Take(c, rv + 1.6f, 0);
                Ctx.Plan.Facts["fountainDrum"] = 1;
                return rv + 1.6f;
            }

            /// <summary>A double-sided fountain wall from a to b: wall fountains on both faces every 3 m.</summary>
            public void FountainWall(float2 a, float2 b)
            {
                Wall(a, b, 1);
                float len = math.distance(a, b);
                var d = (b - a) / math.max(1e-3f, len);
                var n = new float2(-d.y, d.x);
                foreach (var p in LeisureKit.Along(a, b, 3.2f))
                    foreach (float s in new[] { -1f, 1f }) WallFountain(p + n * (s * 0.8f), n * s);
            }

            /// <summary>
            /// The centre piece every design uses: the player's fountain or statue when installed, else a fountain drum.
            /// Returns the radius it keeps.
            /// </summary>
            public float Centre(float2 c, float room, string kind, bool flag = false)
            {
                if (FountainChild && CentreAvailable) { Occ.Take(c, math.max(3f, room - 0.5f), 0); return room; }
                if (CentreAvailable) { CentrePiece(c, math.max(2f, room - 2f), kind); return room; }
                return FountainDrum(c, math.clamp(room * 0.45f, 2.6f, 4.2f), flag);
            }
        }
    }
}
