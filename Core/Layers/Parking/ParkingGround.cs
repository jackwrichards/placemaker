// P2-5 Parking looks: the ground of a car park, laid from causes (docs/runbooks/parking-looks.md; docs/GROUND-RUNDOWN.md).
//
// The user (2026-10-31): "Decals need to be very adamant." The asphalt itself should say how old and how used the lot is,
// and every mark has a reason a driver would recognise:
//
//   coat      the sealcoat's black down every aisle band, its strength the coat's age; faint older patches
//   paint     stall lines in the chosen country's style (when not the game's own markings), crossings for people, stop
//             bars at the exits, hatched blank spaces, painted or flush kerbs, tactile paving at dropped kerbs, lock plates
//   cars      oil gathered in the middle of every stall (darker in the busy rows near the entrances), tyre polish down
//             every aisle, skid marks at the turns
//   age       crack chains along the aisles and the kerbs, tar-sealed cracks on a kept lot, alligator cracking and
//             potholes on a neglected one, cut-and-filled patches
//   kerbs     grime packed against every kerb, leaves from the lot's trees blown against them
//   water     storm grates at the low points with silt round them, puddles and drain streaks on a tired lot
//
// Laid by the parking layer at the end of its plan (as YardWear), so it knows every stall, aisle, kerb and entrance
// exactly. Paint is placed first and never thinned (a missing stall line is a defect, not a lighter look); the wear
// passes follow the budget loop (thinned evenly on a huge lot). Deterministic from the plan's seed.

using System;
using System.Collections.Generic;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class ParkingGround
    {
        /// <summary>Instances a hectare of car park (paint + wear; a lot is all detail, like a square).</summary>
        public const float PerHectare = 1400f;
        public const int MaxInstances = 9000;

        public sealed class Report
        {
            public int Instances, Paint;
            public float Density = 1f;
            public double Ms;
            public readonly Dictionary<string, int> ByCause = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>Lays the ground into <paramref name="plan"/>'s library instances. Never throws.</summary>
        public static Report Lay(AreaPlan plan, Layout layout, ParkingSettings s, List<KerbRun> kerbs, Func<double2, float> H,
            Func<double2, bool> isGreenLocal, uint seed, SettingsBag bag, DecalLibrary library = null)
        {
            var rep = new Report();
            if (!Decals.DressingControls.GroundDecalsOn(bag)) return rep;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var lib = library ?? DecalLibrary.EnsureCurrent();
                if (lib == null || lib.Count == 0 || layout.Lot.Count < 3) return rep;
                float ha = (float)layout.LotArea / 10000f;
                int cap = (int)math.clamp(ha * PerHectare, 300f, MaxInstances);
                float density = math.min(1f, cap / math.max(1f, ha * PerHectare * 1.05f));
                int start = plan.DecalInstances.Count;
                Design d = null;
                for (int pass = 0; pass < 3; pass++)
                {
                    d = new Design(plan, layout, s, kerbs, H, isGreenLocal, lib, Hashing.Mix(seed == 0 ? 1u : seed, 0x9A2Cu), density, cap);
                    d.Run();
                    // the cap is the wear's: paint (one stall line each) is never thinned (game runner 1031-125942: a Japanese lot's
                    // 2890 lines took the whole cap and left its wear at 3 %)
                    int made = plan.DecalInstances.Count - start, wear = made - d.PaintCount;
                    if (wear <= cap || pass == 2) break;
                    plan.DecalInstances.RemoveRange(start, made);
                    density *= math.max(0.2f, cap / (float)math.max(1, wear)) * 0.95f;
                }
                rep.Instances = plan.DecalInstances.Count - start;
                rep.Paint = d.PaintCount;
                rep.Density = density;
                foreach (var kv in d.B.ByCause) rep.ByCause[kv.Key] = kv.Value;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* never fail a plan over its grime */ }
            rep.Ms = clock.Elapsed.TotalMilliseconds;
            plan.Facts["parkingGroundDecals"] = rep.Instances;
            plan.Facts["parkingGroundPaint"] = rep.Paint;
            plan.Facts["parkingGroundDensityPct"] = Math.Round(rep.Density * 100f);
            plan.Facts["parkingGroundMs"] = Math.Round(rep.Ms, 1);
            return rep;
        }

        /// <summary>
        /// The stall lines are ours (InvisibleBay stalls): a country style, or Classic with old paint (the game's own marking
        /// cannot fade, so a worn Classic lot paints its single lines worn).
        /// </summary>
        public static bool OwnLines(ParkingSettings s) =>
            s.Markings && s.Look.Paved && (s.Look.Markings != MarkingStyle.Classic || s.Look.PaintAge > 0.6f);

        private sealed class Design
        {
            private readonly AreaPlan _plan;
            private readonly Layout _l;
            private readonly ParkingSettings _s;
            private readonly ParkingLook _k;
            private readonly List<KerbRun> _kerbs;
            private readonly Func<double2, float> _h;
            private readonly Func<double2, bool> _green;
            private readonly DecalLibrary _lib;
            private readonly uint _seed;
            public readonly BrushContext B;
            public int PaintCount;
            private readonly List<double2> _anchors = new List<double2>();
            private readonly double _diag;
            private BrushRng _r;

            public Design(AreaPlan plan, Layout l, ParkingSettings s, List<KerbRun> kerbs, Func<double2, float> h, Func<double2, bool> green,
                DecalLibrary lib, uint seed, float density, int cap)
            {
                _plan = plan; _l = l; _s = s; _k = s.Look; _kerbs = kerbs ?? new List<KerbRun>(); _h = h; _green = green; _lib = lib; _seed = seed;
                B = new BrushContext(lib, seed, plan.DecalInstances) { Density = density, Budget = plan.DecalInstances.Count + cap + 4000 };
                _r = new BrushRng(Hashing.Mix(seed, 0x51A7));
                foreach (var e in l.Entrances) if (e.Connected && EntranceKinds.ForCars(e.Kind)) _anchors.Add(e.Junction);
                if (_anchors.Count == 0) _anchors.Add(PGeo.Centroid(l.Lot));
                PGeo.Bounds(l.Lot, out var lo, out var hi);
                _diag = Math.Max(20, PGeo.Len(hi - lo));
            }

            private float2 W(double2 local) { var w = _l.Frame.ToWorld(local); return new float2((float)w.x, (float)w.y); }
            private float2 Dir(double2 local) { var d = _l.Frame.DirToWorld(local); return new float2((float)d.x, (float)d.y); }
            private static float2 F(double2 w) => new float2((float)w.x, (float)w.y);
            private static float YawOf(float2 dir) => math.atan2(-dir.y, dir.x);

            /// <summary>0 (a far corner) .. 1 (by an entrance): how busy a spot is.</summary>
            private float Busy(double2 local)
            {
                double best = double.MaxValue;
                foreach (var a in _anchors) best = Math.Min(best, PGeo.Len(a - local));
                return (float)math.saturate(1 - best / (_diag * 0.55));
            }

            /// <summary>One instance of a kind: <paramref name="len"/> metres along <paramref name="dir"/> (strips stretch to it), centred at p.</summary>
            private bool Put(string kind, float2 p, float2 dir, float len, float opacity, string cause, float widthScale = 1f, int variant = -1, bool paint = true)
            {
                var k = _lib.Get(kind);
                if (k == null) return false;
                int v = variant >= 0 ? variant % k.Variants.Count : _r.I(k.Variants.Count);
                var fp = DecalVariants.FootprintOf(k, v);
                float sx = len > 0 ? len / math.max(0.1f, fp.x) : 1f;
                bool ok = B.Add(k, v, p, YawOf(math.normalizesafe(dir, new float2(1, 0))), new float2(sx, widthScale * (len > 0 && k.Variants.Tile == VariantTiling.None ? sx : 1f)), opacity, cause);
                if (ok && paint) PaintCount++;
                return ok;
            }

            /// <summary>A line from a to b in pieces no longer than the kind's repeat.</summary>
            private void Line(string kind, float2 a, float2 b, float opacity, string cause, float widthScale = 1f)
            {
                var k = _lib.Get(kind);
                if (k == null) return;
                float len = math.distance(a, b);
                if (len < 0.2f) return;
                float rep = DecalVariants.FootprintOf(k, 0).x;
                int n = math.max(1, (int)math.ceil(len / rep));
                var d = (b - a) / len;
                for (int i = 0; i < n; i++)
                {
                    var p = a + d * (len * (i + 0.5f) / n);
                    Put(kind, p, d, len / n, opacity, cause, widthScale);
                }
            }

            public void Run()
            {
                Coat();
                Paint();
                Cars();
                Age();
                KerbSide();
                Water();
            }

            // ───────────── coat: the asphalt's colour ─────────────

            private void Coat()
            {
                if (!_k.Paved)
                {
                    // gravel or packed earth: a sheet over the whole lot, wheel-packed lanes darker
                    var ring = new float2[_l.Lot.Count];
                    for (int i = 0; i < ring.Length; i++) ring[i] = W(_l.Lot[i]);
                    GravelTiles(_l.Lot, x => true, _k.Paving == Paving.Dirt ? 0.75f : 1f, "coat:gravel");
                    // the repeat broken by loose stones and darker packed patches
                    Brushes.Scatter(B, BrushArea.Of(ring), "pebbles", 0.08f, new ScatterOptions { Cause = "coat:stones" });
                    Brushes.Scatter(B, BrushArea.Of(ring), "mudPatch", 0.06f, new ScatterOptions { Cause = "coat:packed", Opacity = new float2(0.25f, 0.45f) });
                    // the wheels' ruts down every drive lane, puddles in them on a tired lot
                    foreach (var ln in _l.LiveLines)
                    {
                        if (ln.Kind == LineKind.Footpath || ln.Kind == LineKind.Walk || ln.Length < 4) continue;
                        Brushes.EdgeStrip(B, new[] { W(ln.A), W(ln.B) }, "yardRut", new StripOptions { Side = 1, Gaps = 0.2f, Run = new float2(10f, 30f), Opacity = new float2(0.6f, 0.95f), Cause = "coat:ruts" });
                        if (_r.F() < 0.5f * _k.Grime)
                            Put("puddlePiece", W((ln.A + ln.B) * 0.5), Dir(PGeo.Norm(ln.B - ln.A)), _r.R(2f, 3.5f), _r.R(0.6f, 0.9f), "coat:puddle", 1f, -1, false);
                    }
                    return;
                }
                // the works: the unbuilt end of the lot is graded earth and gravel, rutted by the machines
                if (_l.Works != null)
                {
                    var wk = _l.Works;
                    PGeo.Bounds(_l.Lot, out var wlo, out var whi);
                    double x0 = Math.Max(wlo.x, wk.MinX), x1 = Math.Min(whi.x, wk.MaxX);
                    GravelTiles(_l.Lot, p => p.x >= x0 && p.x <= x1, 1f, "works:ground");
                    for (int i = 0; i < 6; i++)
                    {
                        var a = new double2(x0 + _r.F() * (x1 - x0), wlo.y + _r.F() * (whi.y - wlo.y));
                        var b = a + new double2(_r.R(-1f, 1f), _r.R(-1f, 1f)) * 18;
                        Brushes.EdgeStrip(B, new[] { W(a), W(b) }, "mudTyreTrack", new StripOptions { Side = 1, Gaps = 0.1f, Cause = "works:ruts" });
                    }
                }
                // walkways (drawn by the player, footpath entrances): their own paving, grey block pavers in 2 m squares
                // (P2-1 hand-over: drawn walkways were paved like the aisles)
                foreach (var ln in _l.LiveLines)
                {
                    if (ln.Kind != LineKind.Walk && ln.Kind != LineKind.Footpath) continue;
                    double len = ln.Length;
                    if (len < 1) continue;
                    var d = PGeo.Norm(ln.B - ln.A);
                    double w = Math.Max(2.0, ln.Width + 0.4);
                    int n = Math.Max(1, (int)Math.Round(len / w));
                    for (int i = 0; i < n; i++)
                        Put("paverBand", W(ln.A + d * (len * (i + 0.5) / n)), Dir(d), (float)(len / n) + 0.05f, 1f, "coat:walk", (float)(w / (len / n)));
                }
                // stalls on their own material (pavers in the stalls, asphalt in the aisles): one slab per stall
                if (_k.StallPaving != StallPaving.Same)
                {
                    string band = _k.StallPaving == StallPaving.Pavers ? "paverBand" : _k.StallPaving == StallPaving.GrassPavers ? "grassPaverBand" : "concreteBand";
                    foreach (var b in _l.LiveBays)
                        if (b.Quad != null && b.Role != BayRole.Disabled && b.Role != BayRole.Electric)
                            Put(band, W(b.Center), Dir(b.Axis), (float)b.Length, 1f, "coat:stall", (float)(b.Width / b.Length));
                }
                if (_k.Paving == Paving.Pavers) return;
                if (_k.Paving == Paving.Concrete)
                {
                    // the game's concrete is paper white: a warm grey tone over it, saw-cut joints along the lanes and across
                    // them every 6 m (game runner 1031-125942)
                    Bands("concreteTone", 0.85f);
                    foreach (var ln in _l.LiveLines)
                    {
                        if (ln.Kind != LineKind.Aisle && ln.Kind != LineKind.Ring && ln.Kind != LineKind.Cross) continue;
                        var dl = PGeo.Norm(ln.B - ln.A); var nl = PGeo.Perp(dl);
                        double hw = ln.Width * 0.5;
                        Line("sawCut", W(ln.A + nl * hw), W(ln.B + nl * hw), 0.9f, "coat:joint");
                        Line("sawCut", W(ln.A - nl * hw), W(ln.B - nl * hw), 0.9f, "coat:joint");
                        for (double s = 6; s < ln.Length - 3; s += 6)
                            Line("sawCut", W(ln.A + dl * s - nl * hw), W(ln.A + dl * s + nl * hw), 0.9f, "coat:joint");
                    }
                    return;
                }
                // sealcoat: one band per drive lane, as wide as the asphalt either side of it (probed), strength from the age
                float strength = math.saturate((1f - _k.PavingAge) * 0.95f + (_k.Paving == Paving.DarkAsphalt ? 0.35f : 0f));
                if (strength >= 0.05f) Bands("sealcoat", strength);
                // older and newer patches: faint, few (soft tone is the riskiest decal)
                var asphalt = AsphaltArea();
                if (asphalt != null)
                    Brushes.Tone(B, asphalt, "asphaltTonePatch", new float2(0.16f, 0.3f), 0.25f, ColourLevel.Quiet, "coat:tone");
            }

            /// <summary>
            /// Gravel sheets edge to edge on the frame's grid (a seamless tile: never turned, never overlapped: game runner
            /// 1031-125942/130713 showed every seam and every turned corner), only where the whole tile lies in the lot.
            /// </summary>
            private void GravelTiles(List<double2> lot, Func<double2, bool> where, float opacity, string cause)
            {
                var k = _lib.Get("gravelLot");
                if (k == null) return;
                double t = DecalVariants.SpanOf(k, 0).x;
                PGeo.Bounds(lot, out var lo, out var hi);
                var ax = Dir(new double2(1, 0));

                for (double x = lo.x + t * 0.5; x < hi.x; x += t)
                    for (double y = lo.y + t * 0.5; y < hi.y; y += t)
                    {
                        var p = new double2(x, y);
                        if (!where(p)) continue;
                        bool inside = true;
                        foreach (var o in new[] { new double2(-1, -1), new double2(1, -1), new double2(1, 1), new double2(-1, 1) })
                            if (!PGeo.Contains(lot, p + o * (t * 0.5))) { inside = false; break; }
                        if (!inside) continue;
                        Put("gravelLot", W(p), ax, 0, opacity, cause, 1f, 0, false);   // one variant: seamless
                    }
            }

            /// <summary>
            /// A coat of <paramref name="kind"/> over the paving: one band down every drive lane (its own width) and one patch per
            /// stall (game runner 1031-130713: bands as wide as the lane and both rows also coated the row-end islands).
            /// </summary>
            private void Bands(string kind, float strength)
            {
                foreach (var ln in _l.LiveLines)
                {
                    if (ln.Kind == LineKind.Footpath || ln.Kind == LineKind.Walk || ln.Length < 2) continue;
                    var d = PGeo.Norm(ln.B - ln.A);
                    // run past the ends by half the width so crossings and corners are coated
                    Line(kind, W(ln.A - d * (ln.Width * 0.5)), W(ln.B + d * (ln.Width * 0.5)), strength, "coat:seal", (float)((ln.Width + 0.6) / 20.0));
                }
                foreach (var b in _l.LiveBays)
                    if (b.Quad != null) Put(kind, W(b.Center), Dir(b.Axis), (float)b.Length + 0.3f, strength, "coat:seal", (float)((b.Width + 0.1) / 20.0));
            }

            /// <summary>Metres of asphalt from p along dir (≤ 14 m), in the frame.</summary>
            private double Reach(double2 p, double2 dir)
            {
                for (double t = 0.5; t <= 14; t += 0.5)
                {
                    var q = p + dir * t;
                    if (_green(q) || !PGeo.Contains(_l.Lot, q)) return t - 0.25;
                }
                return 14;
            }

            private BrushArea _asphalt;
            private BrushArea AsphaltArea()
            {
                if (_asphalt != null) return _asphalt;
                var ring = new float2[_l.Lot.Count];
                for (int i = 0; i < ring.Length; i++) ring[i] = W(_l.Lot[i]);
                var f = _l.Frame;
                _asphalt = BrushArea.Of(ring);
                _asphalt.Keep = p => !_green(f.ToLocal(new double2(p.x, p.y)));
                return _asphalt;
            }

            // ───────────── paint ─────────────

            private void Paint()
            {
                if (!_k.Paved) return;
                float fresh = 1f - _k.PaintAge;
                string white = _k.PaintAge > 0.6f ? "paintLineWorn" : "paintLineWhite";
                float op = math.lerp(0.55f, 1f, fresh);
                // stall lines in the chosen style (the game's own markings are left as they are for Classic)
                if (OwnLines(_s)) Stalls(white, op);
                // lock plates (Japanese coin park): one under the middle of every plain stall
                if (_k.Markings == MarkingStyle.Japan)
                    foreach (var b in _l.LiveBays)
                        if (b.Role == BayRole.Normal) Put("lockPlate", W(b.Center + b.Axis * 0.2), Dir(b.Axis), 0, 1f, "paint:lock");
                // blank spaces the player kept free: hatched
                foreach (var b in _l.LiveBays)
                    if (b.Role == BayRole.Blank) Put("hatchWhite", W(b.Center), Dir(b.Axis), (float)b.Length * 0.9f, op, "paint:hatch", (float)(b.Width / b.Length));
                // crossings for people: across every connector at the lot edge, and where walks cross the drive lanes
                if (_k.Crossings != Crossings.None)
                {
                    string kind = _k.Crossings == Crossings.Zebra ? "zebraCrossing" : "dashedCrossing";
                    foreach (var e in _l.Entrances)
                    {
                        if (!e.Connected || e.Kind == EntranceKind.Footpath) continue;
                        var at = e.Point + e.Inward * 2.2;
                        var across = PGeo.Perp(e.Inward);
                        Put(kind, W(at), Dir(across), (float)e.Width + 0.4f, op, "paint:crossing");
                    }
                    foreach (var w in _l.LiveLines)
                    {
                        if (w.Kind != LineKind.Footpath && w.Kind != LineKind.Walk) continue;
                        foreach (var dl in _l.LiveLines)
                        {
                            if (dl.Kind == LineKind.Footpath || dl.Kind == LineKind.Walk || dl.Kind == LineKind.Connector) continue;
                            if (!PGeo.LineLine(w.A, w.B, dl.A, dl.B, out double t, out double u) || t < 0 || t > 1 || u < 0.02 || u > 0.98) continue;
                            var p = w.A + (w.B - w.A) * t;
                            Put(kind, W(p), Dir(PGeo.Norm(dl.B - dl.A)), (float)dl.Width + 0.4f, op, "paint:crossing");
                        }
                    }
                }
                // stop bars at the exits
                if (_k.AisleMarks)
                    foreach (var e in _l.Entrances)
                    {
                        if (!e.Connected || !EntranceKinds.Out(e.Kind)) continue;
                        var across = PGeo.Perp(e.Inward);
                        double half = EntranceKinds.OneWay(e.Kind) ? e.Width * 0.5 : e.Width * 0.25;
                        var mid = e.Point + e.Inward * 4.2 + (EntranceKinds.OneWay(e.Kind) ? double2.zero : across * (e.Width * 0.25 * (_s.Look != null ? 1 : 1)));
                        Line(white, W(mid - across * half), W(mid + across * half), op, "paint:stop", 2.4f);
                    }
                // kerb paint and flush kerbs, tactile paving at the dropped kerbs
                foreach (var kr in _kerbs)
                {
                    var a = F(kr.A); var b = F(kr.B);
                    var o = F(kr.Out);
                    if (kr.Dropped)
                    {
                        var m = (a + b) * 0.5f + o * 0.45f;
                        Put("tactilePaving", m, math.normalizesafe(b - a), 0, 1f, "paint:tactile");
                        continue;
                    }
                    if (_k.Kerbs == KerbKind.RedWhite || _k.Kerbs == KerbKind.Yellow)
                    {
                        string kk = _k.Kerbs == KerbKind.RedWhite ? "kerbPaintRedWhite" : "kerbPaintYellow";
                        StripAlong(kk, a + o * 0.14f, b + o * 0.14f, o, op);
                    }
                    else if (_k.Kerbs == KerbKind.Flush)
                        StripAlong("flushKerb", a - o * 0.15f, b - o * 0.15f, o, 1f);
                }
            }

            /// <summary>A strip from a to b whose heavy side (+z) faces away from <paramref name="towardAsphalt"/> (onto the kerb).</summary>
            private void StripAlong(string kind, float2 a, float2 b, float2 towardAsphalt, float opacity)
            {
                var k = _lib.Get(kind);
                if (k == null) return;
                float len = math.distance(a, b);
                if (len < 0.3f) return;
                var d = (b - a) / len;
                // +z of an instance is (sin yaw, cos yaw) = left of +x turned: for yaw = atan2(-d.y, d.x), +z = (-d.y, d.x)·(-1)? keep +z off the asphalt
                var plusZ = new float2(math.sin(YawOf(d)), math.cos(YawOf(d)));
                if (math.dot(plusZ, towardAsphalt) > 0) { var t = a; a = b; b = t; d = -d; }
                float rep = DecalVariants.FootprintOf(k, 0).x;
                int n = math.max(1, (int)math.ceil(len / rep));
                for (int i = 0; i < n; i++) Put(kind, a + d * (len * (i + 0.5f) / n), d, len / n, opacity, "paint:kerb");
            }

            /// <summary>Stall lines per country (InvisibleBay stalls carry no paint of their own).</summary>
            private void Stalls(string white, float op)
            {
                string line = _k.Markings == MarkingStyle.Yellow ? "paintLineYellow" : white;
                foreach (var b in _l.LiveBays)
                {
                    if (b.Quad == null || b.Quad.Length < 4 || b.Role == BayRole.Disabled || b.Role == BayRole.Electric) continue;
                    // the quad: 0,1 the mouth on the aisle; 2,3 the back (Vegetation's convention)
                    var m0 = W(b.Quad[0]); var m1 = W(b.Quad[1]); var k2 = W(b.Quad[2]); var k3 = W(b.Quad[3]);
                    // a side line from mouth corner to back corner (each stall draws the side at its corner 0; the row's last one both)
                    void Side(float2 mouth, float2 back)
                    {
                        switch (_k.Markings)
                        {
                            case MarkingStyle.Tee:
                                // short marks at the mouth and the back, a T at the back
                                var d = math.normalizesafe(back - mouth); float L = math.distance(mouth, back);
                                Line(line, mouth, mouth + d * math.min(1.2f, L * 0.25f), op, "paint:stall");
                                Line(line, back - d * math.min(0.9f, L * 0.2f), back, op, "paint:stall");
                                break;
                            case MarkingStyle.Double:
                                // a hairpin: two lines 0.45 m apart, joined at the mouth
                                var dd = math.normalizesafe(back - mouth); var n = new float2(-dd.y, dd.x) * 0.225f;
                                Line(line, mouth + n, back + n - dd * 0.4f, op, "paint:stall");
                                Line(line, mouth - n, back - n - dd * 0.4f, op, "paint:stall");
                                Line(line, mouth + n, mouth - n, op, "paint:stall");
                                break;
                            default:
                                Line(line, mouth, back, op, "paint:stall");
                                break;
                        }
                    }
                    // very old paint: some lines are simply gone
                    if (_k.PaintAge > 0.7f && _r.F() < (_k.PaintAge - 0.7f) * 1.6f) continue;
                    Side(m0, k3);
                    if (!HasNeighbour(b, true)) Side(m1, k2);
                    if (_k.Markings == MarkingStyle.Boxes || _k.Markings == MarkingStyle.Japan)
                        Line(line, k3, k2, op, "paint:stall");
                    if (_k.Markings == MarkingStyle.Japan)
                        Line("paintLineYellow", m0 + (k3 - m0) * 0.04f, m1 + (k2 - m1) * 0.04f, op, "paint:stall");
                }
            }

            /// <summary>Is there a live stall sharing this stall's side (the corner 1 side when <paramref name="right"/>)?</summary>
            private bool HasNeighbour(Bay b, bool right)
            {
                var c = right ? b.Quad[1] : b.Quad[0];
                foreach (var o in _l.LiveBays)
                    if (o != b && o.Quad != null && o.Row == b.Row && PGeo.Len(o.Quad[0] - c) < 0.15) return true;
                return false;
            }

            // ───────────── cars ─────────────

            private void Cars()
            {
                float g = _k.Grime;
                if (g < 0.05f) return;
                // oil in the middle of the stalls: the engine of a nose-in car stands over the back half
                foreach (var b in _l.LiveBays)
                {
                    if (b.Role == BayRole.Blank || b.Quad == null) continue;
                    float busy = Busy(b.Center);
                    float chance = math.saturate((0.5f + 0.45f * busy) * g) * B.Density;
                    if (_r.F() > chance) continue;
                    var p = b.Center + b.Axis * (b.Length * 0.14) + b.RowDir * ((_r.F() - 0.5f) * 0.5);
                    Put("stallOil", W(p), Dir(b.Axis), (float)(b.Length * _r.R(0.26f, 0.4f)), math.saturate(_r.R(0.75f, 1f) * (0.75f + 0.35f * busy) * math.min(1.3f, 0.4f + 0.7f * g)), "cars:oil", 1f, -1, false);
                    if (_r.F() < 0.25f * g) Put("oilDrips", W(p + b.Axis * 0.6), Dir(b.Axis), 0, _r.R(0.5f, 0.9f), "cars:drips", 1f, -1, false);
                }
                // tyre polish down every drive lane, stronger near the entrances
                foreach (var ln in _l.LiveLines)
                {
                    if (ln.Kind == LineKind.Footpath || ln.Kind == LineKind.Walk) continue;
                    var a = W(ln.A); var bb = W(ln.B);
                    float busy = Busy((ln.A + ln.B) * 0.5);
                    var line = new[] { a, bb };
                    Brushes.EdgeStrip(B, line, "tyrePolish", new StripOptions
                    {
                        Side = 1, Gaps = math.lerp(0.25f, 0.05f, busy), Run = new float2(12f, 36f),
                        Opacity = new float2(0.7f, 1f) * math.min(1.2f, 0.6f + 0.5f * g) * (0.8f + 0.3f * busy), Width = new float2(0.95f, 1.1f), Cause = "cars:tyres",   // lead review: read clearly at 45 degrees
                    });
                }
                // skid marks where the lanes meet (turning wheels scrub there)
                var joints = new List<double2>();
                foreach (var ln in _l.LiveLines)
                {
                    if (ln.Kind == LineKind.Footpath || ln.Kind == LineKind.Walk) continue;
                    foreach (var p in new[] { ln.A, ln.B })
                    {
                        bool seen = false;
                        foreach (var q in joints) if (PGeo.Len(q - p) < 3) { seen = true; break; }
                        if (!seen) joints.Add(p);
                    }
                }
                foreach (var j in joints)
                {
                    if (_r.F() > math.saturate(0.55f * g) * B.Density) continue;
                    int n = 1 + _r.I(2);
                    for (int i = 0; i < n; i++)
                    {
                        var off = new double2(_r.R(-2.5f, 2.5f), _r.R(-2.5f, 2.5f));
                        float a = _r.F() * 6.283f;
                        Put("skidMark", W(j + off), new float2(math.cos(a), math.sin(a)), _r.R(2.5f, 4.5f), _r.R(0.45f, 0.8f), "cars:skid", 1f, -1, false);
                    }
                }
            }

            // ───────────── age: cracks and repairs ─────────────

            private void Age()
            {
                if (!_k.Paved) return;
                float g = _k.Grime;
                float age = _k.Paving == Paving.Asphalt || _k.Paving == Paving.DarkAsphalt ? math.saturate(0.35f + 0.65f * _k.PavingAge) : 0.6f;
                bool concrete = _k.Paving == Paving.Concrete || _k.Paving == Paving.Pavers;
                string crack = concrete ? "crackConcrete" : "crackAsphalt";
                // chains along the aisles a wheel's width off their middle, and along the kerbs
                foreach (var ln in _l.LiveLines)
                {
                    if (ln.Kind != LineKind.Aisle && ln.Kind != LineKind.Ring && ln.Kind != LineKind.Cross) continue;
                    if (_r.F() > math.saturate(0.25f + 0.6f * age * g)) continue;
                    var nrm = PGeo.Perp(PGeo.Norm(ln.B - ln.A)) * (ln.Width * _r.R(-0.3f, 0.3f));
                    Brushes.CracksAlong(B, new[] { W(ln.A + nrm), W(ln.B + nrm) }, crack, new StripOptions { Gaps = math.lerp(0.75f, 0.35f, age), Run = new float2(8f, 24f), Width = new float2(0.9f, 1.15f), Cause = "age:cracks" });
                    // a kept lot seals its cracks with tar snakes
                    if (!concrete && _k.Cleanliness > 0.45f && _r.F() < 0.5f)
                        Brushes.CracksAlong(B, new[] { W(ln.A - nrm * 0.6), W(ln.B - nrm * 0.6) }, "crackSealed", new StripOptions { Gaps = 0.6f, Run = new float2(6f, 18f), Cause = "age:sealed" });
                }
                foreach (var kr in _kerbs)
                {
                    if (kr.Dropped || _r.F() > 0.35f * age * g) continue;
                    var o = F(kr.Out) * 0.7f;
                    Brushes.CracksAlong(B, new[] { F(kr.A) + o, F(kr.B) + o }, crack, new StripOptions { Gaps = 0.6f, Run = new float2(5f, 14f), Cause = "age:kerbcracks" });
                }
                var asphalt = AsphaltArea();
                if (asphalt == null) return;
                // repairs: cut-and-filled patches, more on an old lot; alligator cracking and potholes on a neglected one
                float lotHa = (float)_l.LotArea / 10000f;
                int patches = (int)math.round(lotHa * 14f * age * math.min(1.5f, g) * B.Density);
                for (int i = 0; i < patches; i++)
                {
                    var p = AsphaltSpot(asphalt);
                    if (!p.HasValue) break;
                    var ax = Dir(new double2(1, 0));
                    if (_r.F() < 0.5f) ax = new float2(-ax.y, ax.x);
                    Put(concrete ? "concreteStain" : "asphaltPatch", p.Value, ax, 0, _r.R(0.7f, 1f), "age:patch", 1f, -1, false);
                }
                float neglect = math.saturate((0.5f - _k.Cleanliness) * 2f) * math.min(1.5f, g);
                if (neglect > 0.02f)
                {
                    Brushes.Scatter(B, asphalt, "crackWeb", 0.025f * neglect, new ScatterOptions { Weight = BusyField(), Cause = "age:alligator" });
                    Brushes.Scatter(B, asphalt, "potholePatch", 0.012f * neglect, new ScatterOptions { Weight = BusyField(), Cause = "age:pothole" });
                    Brushes.Scatter(B, asphalt, "weedyCrack", 0.02f * neglect, new ScatterOptions { Cause = "age:weeds" });
                    Brushes.Scatter(B, asphalt, "litterSprinkle", 0.015f * neglect, new ScatterOptions { Cause = "age:litter" });
                }
            }

            private static float DistSeg(float2 p, float2 a, float2 b)
            {
                var ab = b - a; float l2 = math.lengthsq(ab);
                float t = l2 < 1e-6f ? 0f : math.saturate(math.dot(p - a, ab) / l2);
                return math.distance(p, a + ab * t);
            }

            private Func<float2, float> BusyField()
            {
                var f = _l.Frame;
                return p => 0.3f + 0.7f * Busy(f.ToLocal(new double2(p.x, p.y)));
            }

            private float2? AsphaltSpot(BrushArea a)
            {
                a.Bounds(out var lo, out var hi);
                for (int t = 0; t < 30; t++)
                {
                    var p = new float2(_r.R(lo.x, hi.x), _r.R(lo.y, hi.y));
                    if (a.Contains(p) && a.EdgeDistance(p) > 2f) return p;
                }
                return null;
            }

            // ───────────── kerbs: grime and leaves ─────────────

            private void KerbSide()
            {
                float g = _k.Grime;
                foreach (var kr in _kerbs)
                {
                    if (kr.Dropped) continue;
                    var a = F(kr.A); var b = F(kr.B); var o = F(kr.Out);
                    var d = math.normalizesafe(b - a);
                    var left = new float2(-d.y, d.x);
                    int side = math.dot(left, o) > 0 ? -1 : 1;   // the heavy side (+z) is the kerb
                    Brushes.EdgeStrip(B, new[] { a, b }, "kerbGrimeStrip", new StripOptions
                    {
                        Side = side, Offset = -0.28f, Gaps = math.lerp(0.6f, 0.15f, math.saturate(g * 0.7f)), Run = new float2(6f, 20f),
                        Opacity = new float2(0.6f, 1f) * math.min(1.2f, 0.4f + 0.6f * g), Cause = "kerb:grime",
                    });
                }
                // leaves from the lot's own trees, only in the gutter along the kerbs (game runner 1031-125114: leaves loose in
                // the stalls read as yellow blotches)
                var crowns = new List<(float2 p, float r)>();
                foreach (var pl in _l.Plants) if (pl.Tree) crowns.Add((W(pl.Position), 4.5f));
                if (crowns.Count > 0 && _k.Cleanliness < 0.9f && _kerbs.Count > 0)
                {
                    var grid = new PointGrid(6f);
                    var segs = new List<(float2 a, float2 b)>();
                    foreach (var kr in _kerbs)
                    {
                        if (kr.Dropped) continue;
                        var a = F(kr.A) + F(kr.Out) * 0.5f; var b = F(kr.B) + F(kr.Out) * 0.5f;
                        float len = math.distance(a, b);
                        for (float t = 0; t <= len; t += 3f) grid.Add(math.lerp(a, b, len > 0 ? t / len : 0), segs.Count);
                        segs.Add((a, b));
                    }
                    var asphalt = AsphaltArea();
                    var gutter = new BrushArea { Keep = p =>
                    {
                        if (asphalt.Keep != null && !asphalt.Keep(p)) return false;
                        foreach (int i in grid.Near(p, 1)) if (DistSeg(p, segs[i].a, segs[i].b) < 0.9f) return true;
                        return false;
                    } };
                    gutter.Outer.AddRange(asphalt.Outer);
                    Brushes.Under(B, crowns, "leafSquareSummer", 0.25f * math.min(1.5f, 0.3f + g), new float2(0.6f, 0.35f), gutter, new ScatterOptions { Cause = "kerb:leaves", Scale = new float2(0.5f, 0.8f) });
                }
            }

            // ───────────── water: grates, puddles, streaks ─────────────

            private void Water()
            {
                if (!_k.Paved || _k.Drainage == Drainage.None) return;
                float g = _k.Grime;
                if (_k.Drainage == Drainage.Channel)
                {
                    // a concrete channel down the middle of every aisle, stopping short of the junctions
                    foreach (var ln in _l.LiveLines)
                    {
                        if (ln.Kind != LineKind.Aisle && ln.Kind != LineKind.Ring) continue;
                        if (ln.Length < 12) continue;
                        var dirL = PGeo.Norm(ln.B - ln.A);
                        Line("drainChannel", W(ln.A + dirL * 4.5), W(ln.B - dirL * 4.5), 1f, "water:channel");
                    }
                    return;
                }
                if (_k.Drainage == Drainage.Barranca)
                {
                    // the dry ravine: a stony bed down the long planted strips (Islands.Ravine adds its boulders)
                    foreach (var (a, b, w) in Islands.Strips(_l, 2.0))
                        Line("dryStreamBed", W(a), W(b), 0.95f, "water:ravine", (float)math.min(1.6, w / 2.6));
                    return;
                }
                // a grate every ~35 m down every aisle, at its lowest point when it falls (else spaced evenly)
                foreach (var ln in _l.LiveLines)
                {
                    if (ln.Kind != LineKind.Aisle && ln.Kind != LineKind.Ring) continue;
                    double len = ln.Length;
                    int n = Math.Max(1, (int)Math.Round(len / 35));
                    var dirL = PGeo.Norm(ln.B - ln.A);
                    for (int i = 0; i < n; i++)
                    {
                        double s0 = len * i / n, s1 = len * (i + 1) / n;
                        double best = (s0 + s1) * 0.5; float bh = float.MaxValue;
                        for (double s = s0 + 2; s <= s1 - 2; s += 2)
                        {
                            float hh = _h(_l.Frame.ToWorld(ln.A + dirL * s));
                            if (hh < bh - 0.02f) { bh = hh; best = s; }
                        }
                        var p = ln.A + dirL * best;
                        var wp = W(p);
                        Put("stormGrate", wp, Dir(dirL), 0, 1f, "water:grate", 1f, -1, false);
                        if (_r.F() < 0.6f * g) Put("drainStreak", wp + Dir(dirL) * _r.R(-1.5f, 1.5f), Dir(PGeo.Perp(dirL)), _r.R(2f, 3.5f), _r.R(0.4f, 0.75f), "water:streak", 1f, -1, false);
                        if (_r.F() < 0.45f * g - 0.15f) Put("puddlePiece", wp + Dir(PGeo.Perp(dirL)) * _r.R(-1.2f, 1.2f), Dir(dirL), _r.R(1.6f, 3.2f), _r.R(0.6f, 0.95f), "water:puddle", 1f, -1, false);
                    }
                }
            }
        }
    }
}
