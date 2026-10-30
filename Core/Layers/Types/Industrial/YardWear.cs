// Industrial category, the marks of work (docs/runbooks/industrial.md; the user, 2026-10-29: "decals shouldn't do most of
// the work. They should complement the current work and lean into what makes the current work good").
//
// The yards already carry their look in props, heaps, rocks, slabs and painted earth. This pass reads what was built and
// lays the shared library's pieces only where a yard worker would see a reason:
//
//   wheels     ruts pressed into a dirt or gravel yard along every lane, rubber tracks on concrete and asphalt, turning
//              arcs where lanes meet and at the gate, water standing in the ruts at the crossings, mud carried out of
//              the gate onto the hard apron
//   machines   oil under parked lorries, loaders and forklifts (at the engine end), a soaked patch where they refuel
//              (tanks, generators), drips under the odd wreck
//   steel      rust washed off containers, skips, wreck stacks and the metal mountain, running away from the steel
//   heaps      dust blown downwind off stone, sand and rubble heaps and off the plant, grit and spill at the heap's foot,
//              scrap grit round metal, litter and seeps round rubbish, bark round timber
//   fences     weeds and dry grass where no wheel reaches, weedy cracks along the back of a slab
//   slabs      concrete cracks a step in from the slab edge, old yellow lines along the lanes of a hard yard
//   flaws      one or two things this yard is known for: the big puddle at the gate, the oil lake at the fuel point,
//              the rust river under the metal mountain, the corner gone wild
//
// Deterministic from the plan's seed. Budget: about PerHectare instances a hectare, thinned evenly on a huge yard and
// re-run thinner if it still came out over (the sports park's rule). Planning cost: a few tens of milliseconds.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    /// <summary>What a yard layer knows about its own ground (the rest is read from the plan).</summary>
    public sealed class YardBrief
    {
        public string Type = "", Style = "";
        /// <summary>The yard's outline.</summary>
        public float2[] Ring;
        /// <summary>"concrete", "gravel", "dirt" (anything else reads as dirt).</summary>
        public string Ground = "dirt";
        /// <summary>Drive lanes: centre polyline and half width; hard = asphalt or concrete under the wheels.</summary>
        public readonly List<(float2[] pts, float half, bool hard)> Lanes = new List<(float2[], float, bool)>();
        /// <summary>Gates: the point on the outline and the inward direction.</summary>
        public readonly List<(float2 p, float2 inward)> Gates = new List<(float2, float2)>();
        /// <summary>Where water seeps out (the toe of a tip, compost rows).</summary>
        public readonly List<float2> Seeps = new List<float2>();
        /// <summary>Lay painted lines along hard lanes (container terminals, logistics, the tip).</summary>
        public bool Markings;
        /// <summary>How hard the yard is worked (scales every cause; 1 = as designed).</summary>
        public float Grime = 1f;
        public bool Hard => Ground == "concrete";
    }

    public static class YardWear
    {
        public const float PerHectare = 260f;
        public const int MaxInstances = 7000;

        // ───────────── lanes noted while a layer plans (YardKit.Haul and others) ─────────────

        private sealed class Noted { public readonly List<(float2[] pts, float half, bool hard)> Lanes = new List<(float2[], float, bool)>(); }
        private static readonly ConditionalWeakTable<AreaPlan, Noted> _noted = new ConditionalWeakTable<AreaPlan, Noted>();

        /// <summary>A layer drew a drive lane: remembered for its wear (thread-safe; one plan per worker).</summary>
        public static void NoteLane(AreaPlan plan, IReadOnlyList<float2> line, float halfWidth, bool hard)
        {
            if (plan == null || line == null || line.Count < 2) return;
            var pts = new float2[line.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = line[i];
            var n = _noted.GetOrCreateValue(plan);
            lock (n) n.Lanes.Add((pts, halfWidth, hard));
        }

        public static void TakeNoted(AreaPlan plan, YardBrief b)
        {
            if (plan == null || !_noted.TryGetValue(plan, out var n)) return;
            lock (n) { b.Lanes.AddRange(n.Lanes); n.Lanes.Clear(); }
        }

        /// <summary>The brief of a block or organic yard (IndustrialSite): its aisles, gates and ground.</summary>
        public static YardBrief Of(IndustrialSite s, string type, string style, string ground)
        {
            var b = new YardBrief { Type = type, Style = style, Ring = s.Region.Ring, Ground = ground };
            bool hardLanes = !s.Organic;
            // pieces of one bending track (end to end) become one lane, so its bends are not read as crossings
            List<float2> run = null;
            foreach (var (p, q) in s.Aisles)
            {
                if (run != null && math.distancesq(run[run.Count - 1], p) < 0.01f) { run.Add(q); continue; }
                if (run != null) b.Lanes.Add((run.ToArray(), s.Spec.Aisle * 0.5f, hardLanes));
                run = new List<float2> { p, q };
            }
            if (run != null) b.Lanes.Add((run.ToArray(), s.Spec.Aisle * 0.5f, hardLanes));
            foreach (var g in s.Gates) b.Gates.Add((g.Position, s.Region.InwardNormal(g.Segment)));
            b.Markings = hardLanes && ground == "concrete";
            return b;
        }

        public static YardBrief Of(LayerContext ctx, string type, string style, string ground, List<Gate> gates, float2[] ring = null)
        {
            var region = ctx.LayerRegion;
            var b = new YardBrief { Type = type, Style = style, Ring = ring ?? region.Ring, Ground = ground };
            if (gates != null) foreach (var g in gates) b.Gates.Add((g.Position, region.InwardNormal(g.Segment)));
            TakeNoted(ctx.Plan, b);
            return b;
        }

        public sealed class Report
        {
            public int Instances;
            public float Density = 1f;
            public double Ms;
            public readonly List<string> Flaws = new List<string>();
            public readonly Dictionary<string, int> ByCause = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>Lays the marks into the plan's library instances. Never throws (a plan never fails over its grime).</summary>
        public static Report Lay(LayerContext ctx, YardBrief brief, DecalLibrary library = null)
        {
            var rep = new Report();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var lib = library ?? DecalLibrary.EnsureCurrent();
                if (lib == null || lib.Count == 0 || brief?.Ring == null || brief.Ring.Length < 3) return rep;
                var plan = ctx.Plan;
                float ha = Poly.Area(brief.Ring) / 10000f;
                int cap = (int)math.clamp(ha * PerHectare, 120f, MaxInstances);
                float density = math.min(1f, cap / math.max(1f, ha * PerHectare * 1.1f));
                int start = plan.DecalInstances.Count;
                Design d = null;
                for (int pass = 0; pass < 3; pass++)
                {
                    d = new Design(plan, brief, lib, Hashing.Mix(ctx.Seed == 0 ? 1u : ctx.Seed, 0x1D0Du), density, cap, ctx.H);
                    d.Run();
                    int made = plan.DecalInstances.Count - start;
                    if (made <= cap || pass == 2) break;
                    plan.DecalInstances.RemoveRange(start, made);
                    density *= cap / (float)made * 0.95f;
                }
                rep.Instances = plan.DecalInstances.Count - start;
                rep.Density = density;
                rep.Flaws.AddRange(d.Flaws);
                foreach (var kv in d.B.ByCause) rep.ByCause[kv.Key] = kv.Value;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* never fail a plan over its grime */ }
            rep.Ms = clock.Elapsed.TotalMilliseconds;
            ctx.Plan.Facts["decalInstances"] = rep.Instances;
            ctx.Plan.Facts["decalDensityPct"] = Math.Round(rep.Density * 100f);
            ctx.Plan.Facts["yardWearMs"] = Math.Round(rep.Ms, 1);
            return rep;
        }

        // ───────────── the design ─────────────

        private enum Kind { None, Machine, Vehicle, Wreck, Container, Stack, Skip, Barrel, Tank, MetalHeap, Scrap, StoneHeap, Garbage, Tyres, Timber, Mast, Plant }

        private struct Thing { public float2 P, Fwd; public float L, W; public Kind K; public bool Up; }

        private sealed class Design
        {
            private readonly AreaPlan _plan;
            private readonly YardBrief _y;
            private readonly float2[] _ring;
            private readonly uint _seed;
            private readonly float _a;
            private readonly Func<float2, float> _h;
            public readonly BrushContext B;
            public readonly List<string> Flaws = new List<string>();
            private readonly List<Thing> _things = new List<Thing>();
            private readonly List<(float2[] pts, float half, bool hard)> _fences = new List<(float2[], float, bool)>();
            private readonly List<float2[]> _slabs = new List<float2[]>(), _hard = new List<float2[]>();

            /// <summary>On concrete, paving or asphalt (dust and rust there read as paint: the game runner, 2026-10-30).</summary>
            private bool OnHard(float2 p) { if (_y.Hard) return true; foreach (var r in _hard) if (Poly.Contains(r, p)) return true; return false; }
            private readonly List<(float2 p, float2 dir, float half, bool hard)> _crossings = new List<(float2, float2, float, bool)>();
            private static readonly float2 Wind = Weathering.Wind;
            private readonly Dictionary<string, (PointGrid g, List<float2> pts)> _spots = new Dictionary<string, (PointGrid, List<float2>)>(StringComparer.Ordinal);

            /// <summary>One mark of a cause per patch of ground: a heap of 40 props is one heap, not 40 stains.</summary>
            private bool Room(string key, float2 p, float minD)
            {
                if (!_spots.TryGetValue(key, out var e)) _spots[key] = e = (new PointGrid(math.max(2f, minD)), new List<float2>());
                foreach (int i in e.g.Near(p, 1)) if (math.distancesq(e.pts[i], p) < minD * minD) return false;
                e.g.Add(p, e.pts.Count); e.pts.Add(p);
                return true;
            }

            public Design(AreaPlan plan, YardBrief y, DecalLibrary lib, uint seed, float density, int cap, Func<float2, float> h)
            {
                _plan = plan; _y = y; _seed = seed; _a = math.clamp(y.Grime, 0f, 2f); _h = h;
                _ring = Poly.EnsureCcw((float2[])y.Ring.Clone());
                B = new BrushContext(lib, seed, plan.DecalInstances) { Budget = plan.DecalInstances.Count + (int)(cap * 1.6f) + 200, Density = density };
                Read();
            }

            private uint H(int a, int b = 0) => Hashing.Mix(_seed, a, b);
            private float U(int a, int b = 0) => Hashing.Unit(H(a, b));
            private float R(float lo, float hi, int a, int b = 0) => math.lerp(lo, hi, U(a, b));
            private float2 Op(float lo, float hi) => new float2(math.saturate(lo * math.min(1.3f, 0.6f + 0.4f * _a)), math.saturate(hi * math.min(1.2f, 0.7f + 0.3f * _a)));
            private bool In(float2 p, float inset = 0.5f) => Poly.Contains(_ring, p) && Poly.DistanceToBoundary(_ring, p) >= inset;
            private static float2 Fwd(float yaw) => new float2(math.sin(yaw), math.cos(yaw));
            private static float2 Right(float2 f) => new float2(f.y, -f.x);

            // ── reading the plan ──

            private static Kind KindOf(PlannedObject o, out float w, out float l)
            {
                string id = o.Asset.Id ?? "";
                string cat = o.Category ?? "";
                w = 2f; l = 2f;
                if (o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush || o.Role == ObjectRole.Decal) return Kind.None;
                if (id.StartsWith("Placemaker Vehicle", StringComparison.Ordinal))
                {
                    if (cat == AssetCategories.ScrapWreck) { w = 2.2f; l = 5f; return Kind.Wreck; }
                    bool big = id.IndexOf("Truck", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Loader", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Garbage", StringComparison.OrdinalIgnoreCase) >= 0;
                    w = big ? 2.8f : 2.1f; l = big ? 8.5f : 4.8f;
                    return cat == AssetCategories.ScrapMachine || id.IndexOf("Loader", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Tractor", StringComparison.OrdinalIgnoreCase) >= 0 ? Kind.Machine : Kind.Vehicle;
                }
                if (cat == AssetCategories.ScrapWreck) { w = 2.2f; l = 5f; return Kind.Wreck; }
                if (cat == AssetCategories.ScrapMachine || cat == AssetCategories.LogisticsMachine || cat == AssetCategories.ConstructionMachine) { w = 3f; l = 6.5f; return Kind.Machine; }
                if (cat == AssetCategories.LogisticsTruck) { w = 2.8f; l = 8.4f; return Kind.Vehicle; }
                if (cat == AssetCategories.ConstructionGenerator) { w = 2.5f; l = 4.5f; return Kind.Tank; }
                if (id.StartsWith("ShippingContainerPile", StringComparison.Ordinal))
                {
                    w = 2.6f; l = id.Contains("Large") ? 70f : id.Contains("Medium") ? 34.9f : 13.9f;
                    if (id.Contains("Medium") || id.Contains("High") || id.Contains("Large")) w = 5.4f;
                    return Kind.Stack;
                }
                if (id.StartsWith("ShippingContainer", StringComparison.Ordinal) || id.StartsWith("TrainCargoCar", StringComparison.Ordinal)
                    || cat == AssetCategories.ScrapContainer || cat == AssetCategories.LogisticsContainer || cat == "construction.container")
                { w = 2.45f; l = id.StartsWith("TrainCargoCar", StringComparison.Ordinal) ? 12.6f : 6.06f; return Kind.Container; }
                if (cat == AssetCategories.LandfillSkip) { w = 2.4f; l = 5.5f; return Kind.Skip; }
                if (id.StartsWith("OilTank", StringComparison.Ordinal)) { w = l = id.Contains("Medium") ? 14f : 10f; return Kind.Tank; }
                if (cat == AssetCategories.ScrapBarrel || cat == AssetCategories.LogisticsBarrel) { w = 2f; l = 3f; return Kind.Barrel; }
                if (cat == AssetCategories.ScrapTire) { w = 2.5f; l = 5.8f; return Kind.Tyres; }
                if (id.StartsWith("OreMiningTruck01_OrePile", StringComparison.Ordinal)) { w = 8f; l = 10f; return Kind.MetalHeap; }
                if (cat == AssetCategories.ScrapHeap) { w = 2.5f; l = 4f; return Kind.Scrap; }
                if (id.StartsWith("Trashpile", StringComparison.Ordinal) || cat == AssetCategories.LandfillGarbage) { w = 3f; l = 3f; return Kind.Garbage; }
                if (id.StartsWith("RubblePile", StringComparison.Ordinal) || cat == "construction.pile" || cat == "construction.rubble" || cat == AssetCategories.LandfillPile || cat == AssetCategories.LogisticsBulk)
                { w = 7f; l = 8f; return Kind.StoneHeap; }
                if (cat == AssetCategories.LogisticsLumber || id.StartsWith("ForestForwarderTrailer01_LogPile", StringComparison.Ordinal)) { w = 3.5f; l = 4.7f; return Kind.Timber; }
                if (cat == AssetCategories.FloodLight || id.StartsWith("Lightpole", StringComparison.Ordinal)) { w = l = 0.6f; return Kind.Mast; }
                if (cat == "quarry.plant" || id.StartsWith("IndustrialManufacturingDecoration", StringComparison.Ordinal) || id.StartsWith("OreConveyor", StringComparison.Ordinal))
                { w = 8f; l = 12f; return Kind.Plant; }
                return Kind.None;
            }

            private void Read()
            {
                foreach (var o in _plan.Objects)
                {
                    if (o == null) continue;
                    var k = KindOf(o, out float w, out float l);
                    if (k == Kind.None) continue;
                    // sunk pieces are the ground itself; raised ones sit on others (stacked wrecks)
                    if (o.Elevation < -0.05f) continue;
                    var f = Fwd(o.Yaw);
                    _things.Add(new Thing { P = o.Position.xz, Fwd = f, L = l * math.max(0.3f, o.Scale), W = w * math.max(0.3f, o.Scale), K = k, Up = o.Elevation > 0.4f });
                }
                foreach (var n in _plan.Nets)
                    if (n != null && n.Role == NetRole.Fence)
                    {
                        var a = n.A.xz; var d = n.D.xz;
                        if (math.distancesq(a, d) < 1f) continue;
                        _fences.Add((new[] { a, d }, 0.2f, false));
                    }
                foreach (var s in _plan.Surfaces)
                {
                    if (s?.Ring == null || s.Ring.Length < 3) continue;
                    if (s.Role != SurfaceRole.Paving && s.Role != SurfaceRole.Asphalt) continue;
                    var r = Poly.EnsureCcw(Poly.To2D(s.Ring));
                    _hard.Add(r);
                    float area = Poly.Area(r), per = Perimeter(r);
                    // lanes are long thin strips: only the slabs crack and grow weeds
                    if (area < 140f || area / math.max(1f, per) < 3.2f) continue;
                    _slabs.Add(r);
                }
                FindCrossings();
            }

            private static float Perimeter(float2[] r) { float p = 0f; for (int i = 0; i < r.Length; i++) p += math.distance(r[i], r[(i + 1) % r.Length]); return p; }

            private void FindCrossings()
            {
                var segs = new List<(float2 a, float2 b, int lane, float half, bool hard)>();
                for (int i = 0; i < _y.Lanes.Count; i++)
                {
                    var (pts, half, hard) = _y.Lanes[i];
                    for (int j = 0; j + 1 < pts.Length; j++) if (math.distancesq(pts[j], pts[j + 1]) > 1f) segs.Add((pts[j], pts[j + 1], i, half, hard));
                }
                if (segs.Count > 600) return;
                void Add(float2 x, float2 dir, float half, bool hard)
                {
                    foreach (var c in _crossings) if (math.distancesq(c.p, x) < 64f) return;
                    if (In(x, 1f)) _crossings.Add((x, dir, half, hard));
                }
                for (int i = 0; i < segs.Count; i++)
                    for (int j = i + 1; j < segs.Count; j++)
                    {
                        var s = segs[i]; var t = segs[j];
                        if (s.lane == t.lane) continue;
                        var ds = math.normalizesafe(s.b - s.a); var dt = math.normalizesafe(t.b - t.a);
                        if (math.abs(math.dot(ds, dt)) > 0.85f) continue;   // one bending track
                        if (math.distancesq(s.a, t.a) < 0.01f || math.distancesq(s.a, t.b) < 0.01f || math.distancesq(s.b, t.a) < 0.01f || math.distancesq(s.b, t.b) < 0.01f) { Add(math.distancesq(s.a, t.a) < 0.01f || math.distancesq(s.a, t.b) < 0.01f ? s.a : s.b, ds, math.max(s.half, t.half), s.hard && t.hard); continue; }
                        if (Poly.SegmentIntersection(s.a, s.b, t.a, t.b, out var x, out _, out _)) { Add(x, ds, math.max(s.half, t.half), s.hard && t.hard); continue; }
                        foreach (var e in new[] { t.a, t.b })
                            if (SegDist(e, s.a, s.b) < s.half + 0.5f) { Add(Closest(e, s.a, s.b), ds, math.max(s.half, t.half), s.hard && t.hard); break; }
                        foreach (var e in new[] { s.a, s.b })
                            if (SegDist(e, t.a, t.b) < t.half + 0.5f) { Add(Closest(e, t.a, t.b), dt, math.max(s.half, t.half), s.hard && t.hard); break; }
                    }
            }

            private static float2 Closest(float2 p, float2 a, float2 b) { var ab = b - a; float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.lengthsq(ab))); return a + ab * t; }
            private static float SegDist(float2 p, float2 a, float2 b) => math.distance(p, Closest(p, a, b));

            // ── the passes ──

            public void Run()
            {
                PickFlaws();
                Wheels();
                Gates();
                Machines();
                Steel();
                Heaps();
                Fences();
                Slabs();
                Seeps();
            }

            private void PickFlaws()
            {
                var all = new List<string>();
                if (!_y.Hard) all.Add("bigPuddle");
                bool vehicles = false, metal = false;
                foreach (var t in _things) { if (t.K == Kind.Machine || t.K == Kind.Vehicle || t.K == Kind.Tank) vehicles = true; if (t.K == Kind.MetalHeap || t.K == Kind.Stack || t.K == Kind.Container) metal = true; }
                if (vehicles) all.Add("oilLake");
                if (metal) all.Add("rustRiver");
                if (_fences.Count > 0) all.Add("wildCorner");
                if (all.Count == 0) return;
                int first = (int)(U(901) * all.Count) % all.Count;
                Flaws.Add(all[first]);
                if (all.Count > 1 && U(902) < 0.5f) Flaws.Add(all[(first + 1 + (int)(U(903) * (all.Count - 1))) % all.Count]);
            }

            /// <summary>Ruts or rubber along every lane, turning arcs where lanes meet, water in the ruts at the crossings.</summary>
            private void Wheels()
            {
                for (int i = 0; i < _y.Lanes.Count; i++)
                {
                    var (pts, half, hard) = _y.Lanes[i];
                    if (pts.Length < 2) continue;
                    bool onHard = hard || _y.Hard;
                    // two wheel paths, one for each way (a one-lane track has one pair)
                    float off = half >= 3f ? half * 0.42f : half * 0.3f;
                    foreach (int s in new[] { 1, -1 })
                    {
                        if (onHard)
                            Brushes.EdgeStrip(B, pts, "laneGrime", new StripOptions { Side = s, Offset = off, Gaps = 0.35f, Run = new float2(10f, 34f), Opacity = Op(0.6f, 0.95f), Width = new float2(0.9f, 1.15f), Cause = "wheels:grime" });
                        else
                            Brushes.EdgeStrip(B, pts, "yardRut", new StripOptions { Side = s, Offset = off, Gaps = 0.25f, Run = new float2(12f, 40f), Opacity = Op(0.7f, 1f), Width = new float2(0.9f, 1.15f), Cause = "wheels:ruts" });
                    }
                    if (onHard && _y.Markings && half >= 3f)
                        foreach (int s in new[] { 1, -1 })
                            Brushes.EdgeStrip(B, pts, "fadedLineYellow", new StripOptions { Side = s, Offset = half - 0.35f, Gaps = 0.5f, Run = new float2(8f, 30f), Opacity = Op(0.65f, 0.95f), Width = new float2(1f, 1f), Cause = "slabs:lines" });
                }
                // turning aprons: arcs swept by the lorries' rear wheels round the corners they take most
                int n = 0;
                foreach (var c in _crossings)
                {
                    n++;
                    bool onHard = c.hard || _y.Hard;
                    var d = math.normalizesafe(c.dir, new float2(1f, 0f)); var sd = new float2(-d.y, d.x);
                    int arcs = 1 + (U(110, n) < 0.55f ? 1 : 0);
                    for (int k = 0; k < arcs; k++)
                    {
                        int quad = (int)(U(111 + k, n) * 4f) % 4;
                        var ax = quad % 2 == 0 ? d : -d; var ay = quad < 2 ? sd : -sd;
                        float r = c.half + R(2f, 5f, 112 + k, n);
                        var ctr = c.p + (ax + ay) * r;
                        var arc = new List<float2>();
                        for (int t = 0; t <= 8; t++)
                        {
                            float ang = t / 8f * math.PI * 0.5f;
                            var q = ctr - ax * r * math.cos(ang) - ay * r * math.sin(ang);
                            if (In(q, 1f)) arc.Add(q);
                        }
                        if (arc.Count < 4) continue;
                        Brushes.EdgeStrip(B, arc, onHard ? "laneGrime" : "yardRut", new StripOptions { Gaps = 0.15f, Run = new float2(6f, 20f), CornerDeg = 70f, Opacity = onHard ? Op(0.5f, 0.85f) : Op(0.65f, 1f), Width = new float2(0.8f, 1.1f), Cause = "wheels:arc" });
                    }
                    // water stands in the ruts at a crossing (on hard ground: by the drain, smaller and rarer)
                    float chance = onHard ? 0.25f : 0.55f;
                    if (U(120, n) < chance * math.min(1.2f, _a)) Puddle(c.p + sd * c.half * R(-0.45f, 0.45f, 121, n) + d * R(-2f, 2f, 122, n), d, onHard ? R(2.5f, 4f, 123, n) : R(3.5f, 6.5f, 123, n), 1f, "water:puddle");
                    if (!onHard && U(124, n) < 0.5f) Brushes.At(B, c.p + sd * R(-2f, 2f, 125, n), d, "pebbles", R(5f, 8f, 126, n), Op(0.35f, 0.6f), 1, ColourLevel.Quiet, "wheels:grit");
                }
            }

            private void Puddle(float2 c, float2 along, float len, float k, string cause)
            {
                along = math.normalizesafe(along, new float2(1f, 0f));
                var side = new float2(-along.y, along.x);
                float wid = len * 0.42f;
                var ring = new float2[14];
                for (int i = 0; i < ring.Length; i++)
                {
                    float a = i / (float)ring.Length * 2f * math.PI;
                    float wob = 1f + 0.18f * math.sin(a * 3f + U(130, (int)(c.x * 7f)) * 6f);
                    ring[i] = c + along * math.cos(a) * len * 0.5f * wob * k + side * math.sin(a) * wid * 0.5f * wob * k;
                }
                foreach (var q in ring) if (!In(q, 0.5f)) return;
                Brushes.Fill(B, ring, "puddlePiece", Op(0.7f, 1f), ColourLevel.Any, cause);
                Brushes.At(B, c + along * len * 0.2f, along, "dampPatch", len * 1.4f * k, Op(0.3f, 0.5f), 1, ColourLevel.Any, "water:damp");
            }

            /// <summary>The gate apron: lorries swing in both ways, mud carried out onto the street side, the big puddle.</summary>
            private void Gates()
            {
                int n = 0;
                foreach (var (g, inward) in _y.Gates)
                {
                    n++;
                    var side = new float2(-inward.y, inward.x);
                    // swing-in arcs to both sides
                    foreach (int s in new[] { 1, -1 })
                    {
                        var arc = new List<float2>();
                        float r = R(7f, 10f, 140 + s, n);
                        var ctr = g + inward * r + side * s * r * 0.15f;
                        for (int t = 0; t <= 8; t++)
                        {
                            float ang = t / 8f * math.PI * 0.5f;
                            var q = ctr - inward * r * math.cos(ang) + side * s * r * math.sin(ang) * 1.2f;
                            if (In(q, 1f)) arc.Add(q);
                        }
                        if (arc.Count >= 4) Brushes.EdgeStrip(B, arc, _y.Hard ? "laneGrime" : "yardRut", new StripOptions { Gaps = 0.1f, Run = new float2(6f, 20f), CornerDeg = 70f, Opacity = Op(0.6f, 0.95f), Cause = "gate:arc" });
                    }
                    // mud carried out on the tyres: tracks fading toward the street
                    if (!_y.Hard)
                        foreach (int s in new[] { 1, -1 })
                        {
                            var a = g + inward * 6f + side * s * 1.4f; var b = g - inward * 1.5f + side * s * 1.4f;
                            Brushes.EdgeStrip(B, new[] { a, b }, "tyreTrack", new StripOptions { Gaps = 0f, Run = new float2(6f, 9f), Opacity = Op(0.8f, 1f), Cause = "gate:trackout" });
                        }
                    Brushes.At(B, g + inward * 4f, inward, "oilDrips", R(3f, 4.5f, 150, n), Op(0.45f, 0.8f), 0, ColourLevel.Any, "gate:drips");
                    if (n == 1 && Flaws.Contains("bigPuddle")) Puddle(g + inward * R(9f, 13f, 151) + side * R(-2f, 2f, 152), side, R(8f, 11f, 153), 1f, "flaw:bigPuddle");
                }
            }

            /// <summary>Oil where lorries, loaders and forklifts stand, at the engine end; the fuel point soaked; drips under some wrecks.</summary>
            private void Machines()
            {
                int n = 0; int fuel = -1; float fuelD = float.MaxValue;
                var gate = _y.Gates.Count > 0 ? _y.Gates[0].p : Poly.Centroid(_ring);
                for (int i = 0; i < _things.Count; i++)
                {
                    var t = _things[i];
                    n++;
                    switch (t.K)
                    {
                        case Kind.Machine:
                        case Kind.Vehicle:
                            if (t.Up || U(200, n) > 0.85f) break;
                            Brushes.At(B, t.P + t.Fwd * t.L * R(0.12f, 0.3f, 201, n), t.Fwd, U(202, n) < 0.75f ? "oilSpot" : "oilDrips", R(3f, 4.6f, 203, n), Op(0.7f, 1f), U(204, n) < 0.4f ? 1 : 0, ColourLevel.Any, "machines:oil");
                            if (t.K == Kind.Machine && !_y.Hard) Brushes.At(B, t.P - t.Fwd * t.L * 0.4f, -t.Fwd, "scrapGrit", R(4f, 6f, 205, n), Op(0.3f, 0.55f), 0, ColourLevel.Quiet, "machines:grit");
                            break;
                        case Kind.Tank:
                            Brushes.At(B, t.P + Right(t.Fwd) * (t.W * 0.5f + 0.8f), Right(t.Fwd), "oilSpot", R(3f, 4.5f, 206, n), Op(0.6f, 0.95f), 1, ColourLevel.Any, "machines:fuel");
                            float dg = math.distance(t.P, gate);
                            if (dg < fuelD) { fuelD = dg; fuel = i; }
                            break;
                        case Kind.Wreck:
                            if (t.Up || !Room("wreck", t.P, 6f)) break;
                            float u = U(207, n);
                            if (u < 0.22f) Brushes.At(B, t.P + t.Fwd * t.L * 0.25f, t.Fwd, "oilSpot", R(2.4f, 3.6f, 208, n), Op(0.65f, 0.95f), 0, ColourLevel.Any, "wrecks:oil");
                            else if (u < 0.36f) Brushes.At(B, t.P + Right(t.Fwd) * (t.W * 0.5f + 0.4f), Right(t.Fwd), "rustRunoff", R(3f, 4.5f, 209, n), Op(0.65f, 0.95f), 0, ColourLevel.Any, "wrecks:rust");
                            break;
                        case Kind.Barrel:
                            if (Room("drums", t.P, 9f) && U(210, n) < 0.5f) Brushes.At(B, t.P + new float2(R(-1f, 1f, 211, n), R(-1f, 1f, 212, n)), t.Fwd, U(213, n) < 0.5f ? "oilDrips" : "oilSpot", R(1.6f, 2.8f, 214, n), Op(0.5f, 0.85f), 0, ColourLevel.Any, "machines:drums");
                            break;
                    }
                }
                // the fuel point: where every machine fills up, the ground is black
                if (Flaws.Contains("oilLake"))
                {
                    float2 at, dir;
                    if (fuel >= 0) { var t = _things[fuel]; dir = Right(t.Fwd); at = t.P + dir * (t.W * 0.5f + 2f); }
                    else
                    {
                        Thing best = default; bool any = false;
                        foreach (var t in _things) if ((t.K == Kind.Machine || t.K == Kind.Vehicle) && !t.Up && (!any || math.distance(t.P, gate) < math.distance(best.P, gate))) { best = t; any = true; }
                        if (!any) return;
                        dir = best.Fwd; at = best.P + best.Fwd * (best.L * 0.5f + 2.5f);
                    }
                    if (In(at, 2f)) { Brushes.At(B, at, dir, "oilSpot", R(5f, 6.5f, 220), Op(0.7f, 1f), 4, ColourLevel.Any, "flaw:oilLake"); Brushes.At(B, at + dir * 3f, dir, "oilDrips", 5f, Op(0.6f, 0.9f), 2, ColourLevel.Any, "flaw:oilLake"); }
                }
            }

            /// <summary>Rust washed off steel by the rain, running away from it.</summary>
            private void Steel()
            {
                int n = 0;
                float2 riverAt = default; float riverSize = 0f; bool river = Flaws.Contains("rustRiver");
                foreach (var t in _things)
                {
                    n++;
                    if (t.Up) continue;
                    var right = Right(t.Fwd);
                    switch (t.K)
                    {
                        case Kind.Container:
                        case Kind.Skip:
                            if (Room("box", t.P, 5f) && U(300, n) < (t.K == Kind.Skip ? 0.5f : 0.4f) * math.min(1.3f, _a))
                            {
                                float s = U(301, n) < 0.5f ? 1f : -1f;
                                Brushes.At(B, t.P + right * s * (t.W * 0.5f + 0.9f) + t.Fwd * t.L * R(-0.35f, 0.35f, 302, n), right * s, "rustRunoff", R(3.5f, 5.5f, 303, n), OnHard(t.P) ? Op(0.3f, 0.55f) : Op(0.55f, 0.9f), 0, ColourLevel.Any, "steel:rust");
                            }
                            break;
                        case Kind.Stack:
                            for (float u = -t.L * 0.5f + 4f; u < t.L * 0.5f - 3f; u += R(9f, 16f, 304, n + (int)u))
                                foreach (float s in new[] { 1f, -1f })
                                    if (U(305, n * 31 + (int)(u * 3f) + (s > 0 ? 1 : 0)) < 0.45f)
                                        Brushes.At(B, t.P + right * s * (t.W * 0.5f + 0.9f) + t.Fwd * u, right * s, "rustRunoff", R(3.5f, 5.5f, 306, n + (int)u), Op(0.3f, 0.55f), 0, ColourLevel.Any, "steel:rust");
                            break;
                        case Kind.MetalHeap:
                            if (Room("metal", t.P, 8f))
                            {
                                int k = 2 + (int)(U(307, n) * 2f);
                                float r0 = math.max(t.W, t.L) * 0.5f;
                                for (int i = 0; i < k; i++)
                                {
                                    float ang = (i + U(308 + i, n) * 0.6f) / k * 2f * math.PI;
                                    var dir = new float2(math.cos(ang), math.sin(ang));
                                    Brushes.At(B, t.P + dir * (r0 * 0.75f + 1.2f), dir, "rustRunoff", R(4f, 6.5f, 309 + i, n), Op(0.65f, 1f), 0, ColourLevel.Any, "steel:heapRust");
                                }
                                if (r0 > riverSize) { riverSize = r0; riverAt = t.P; }
                            }
                            break;
                        case Kind.Mast:
                            if (U(310, n) < 0.45f) Brushes.At(B, t.P + Wind * 0.3f, Wind, "rustBleed", R(1.4f, 2.4f, 311, n), Op(0.5f, 0.85f), 0, ColourLevel.Any, "steel:mast");
                            break;
                    }
                }
                if (river && riverSize > 0f)
                {
                    // the heap's run-off heads for the nearest lane: one long stain with its fan
                    var to = NearestLane(riverAt, out var lanePt) ? math.normalizesafe(lanePt - riverAt, Wind) : Wind;
                    for (int i = 0; i < 4; i++)
                        Brushes.At(B, riverAt + to * (riverSize * 0.7f + 2f + i * 3.2f) + new float2(-to.y, to.x) * R(-0.8f, 0.8f, 320 + i), to, "rustRunoff", R(4.5f, 6f, 324 + i), Op(0.6f, 0.95f), 1, ColourLevel.Any, "flaw:rustRiver");
                }
            }

            private bool NearestLane(float2 p, out float2 at)
            {
                at = p; float best = float.MaxValue;
                foreach (var (pts, _, _) in _y.Lanes)
                    for (int j = 0; j + 1 < pts.Length; j++)
                    {
                        var q = Closest(p, pts[j], pts[j + 1]);
                        float d = math.distancesq(p, q);
                        if (d < best) { best = d; at = q; }
                    }
                return best < 60f * 60f;
            }

            /// <summary>Dust blown downwind off stone heaps and the plant, spill at their foot, litter and bark where they belong.</summary>
            private void Heaps()
            {
                int n = 0;
                foreach (var t in _things)
                {
                    n++;
                    if (t.Up) continue;
                    float r0 = math.max(t.W, t.L) * 0.5f;
                    switch (t.K)
                    {
                        case Kind.StoneHeap:
                            if (!Room("stone", t.P, 10f)) break;
                            if (U(400, n) < 0.6f && !OnHard(t.P + Wind * (r0 + 5f))) Brushes.At(B, t.P + Wind * (r0 + R(3f, 6f, 401, n)), Wind, "dustDrift", R(10f, 16f, 402, n) * math.clamp(r0 / 4f, 0.7f, 1.4f), Op(0.5f, 0.8f), 1, ColourLevel.Quiet, "heaps:dust");
                            if (U(403, n) < 0.7f) { float a = U(404, n) * 6.283f; var d = new float2(math.cos(a), math.sin(a)); Brushes.At(B, t.P + d * (r0 * 0.8f + 1f), d, "pebbles", R(5f, 8f, 405, n), Op(0.4f, 0.7f), 1, ColourLevel.Quiet, "heaps:spill"); }
                            break;
                        case Kind.Plant:
                            if (!OnHard(t.P + Wind * (r0 + 6f))) Brushes.At(B, t.P + Wind * (r0 + R(4f, 8f, 406, n)), Wind, "dustDrift", R(13f, 18f, 407, n), Op(0.5f, 0.8f), 2, ColourLevel.Quiet, "plant:dust");
                            if (U(408, n) < 0.6f) Brushes.At(B, t.P + t.Fwd * (t.L * 0.5f + 0.8f), t.Fwd, "oilSpot", R(2.4f, 3.6f, 409, n), Op(0.5f, 0.85f), 1, ColourLevel.Any, "plant:oil");
                            break;
                        case Kind.Scrap:
                            if (!Room("scrap", t.P, 9f)) break;
                            Brushes.At(B, t.P + new float2(R(-1.5f, 1.5f, 430, n), R(-1.5f, 1.5f, 431, n)), Wind, "scrapGrit", R(6f, 9f, 432, n), Op(0.65f, 1f), 1, ColourLevel.Any, "heaps:scrapGrit");
                            if (U(433, n) < 0.3f) { float a2 = U(434, n) * 6.283f; var d2 = new float2(math.cos(a2), math.sin(a2)); Brushes.At(B, t.P + d2 * 2.5f, d2, "rustRunoff", R(2.4f, 3.6f, 435, n), Op(0.4f, 0.75f), 0, ColourLevel.Any, "steel:scrapRust"); }
                            break;
                        case Kind.MetalHeap:
                            if (!Room("metalGrit", t.P, 8f)) break;
                            {
                                float a = U(410, n) * 6.283f; var d = new float2(math.cos(a), math.sin(a));
                                Brushes.At(B, t.P + d * (r0 * 0.8f + 1.5f), d, "scrapGrit", R(7f, 10f, 411, n), Op(0.7f, 1f), 2, ColourLevel.Any, "heaps:scrapGrit");
                            }
                            break;
                        case Kind.Garbage:
                            if (!Room("garbage", t.P, 9f)) break;
                            if (U(412, n) < 0.55f) Brushes.At(B, t.P + Wind * (r0 + 1f), Wind, "litterMix", R(3f, 5f, 413, n), Op(0.55f, 0.9f), 2, ColourLevel.Any, "heaps:litter");
                            if (U(414, n) < 0.25f) Brushes.At(B, t.P - Wind * (r0 * 0.7f + 0.6f), -Wind, "leachateSeep", R(3f, 5f, 415, n), Op(0.5f, 0.85f), 0, ColourLevel.Any, "heaps:seep");
                            break;
                        case Kind.Tyres:
                            if (!Room("tyres", t.P, 10f)) break;
                            if (U(416, n) < 0.3f) Brushes.At(B, t.P + Right(t.Fwd) * (t.W * 0.5f + 0.8f), Right(t.Fwd), "scrapGrit", R(3f, 5f, 417, n), Op(0.3f, 0.55f), 0, ColourLevel.Quiet, "heaps:rubber");
                            break;
                        case Kind.Timber:
                            if (!Room("timber", t.P, 8f)) break;
                            if (U(418, n) < 0.6f) Brushes.At(B, t.P + t.Fwd * (t.L * 0.5f + 0.6f), t.Fwd, U(419, n) < 0.5f ? "barkMulch" : "twigs", R(3f, 5f, 420, n), Op(0.45f, 0.8f), 1, ColourLevel.Any, "heaps:bark");
                            break;
                    }
                }
            }

            /// <summary>Weeds and dry grass where no wheel reaches, along the inside of the fence; one corner gone wild.</summary>
            private void Fences()
            {
                var lines = _fences;
                if (lines.Count == 0)
                {
                    // no fence net: the yard's own edge (the walls and bunds stand there)
                    var inset = Brushes.Inset(_ring, 1.2f);
                    if (inset == null) return;
                    lines = new List<(float2[], float, bool)> { (Closed(inset), 0f, true) };
                }
                int n = 0;
                float2 wildAt = default; float wildD = -1f;
                var gate = _y.Gates.Count > 0 ? _y.Gates[0].p : Poly.Centroid(_ring);
                foreach (var (pts, _, isRing) in lines)
                {
                    n++;
                    // which side is the yard: the weeds grow on the inside, heavy against the fence
                    var a = pts[0]; var b = pts[pts.Length > 1 ? 1 : 0];
                    var dir = math.normalizesafe(b - a); var left = new float2(-dir.y, dir.x);
                    var mid = math.lerp(a, b, 0.5f);
                    bool insideLeft = isRing || Poly.Contains(_ring, mid + left * 1.5f);
                    // StripOptions.Side +1 puts the heavy (+z) side on the left: the fence must be the heavy side
                    int side = insideLeft ? -1 : 1;
                    Brushes.EdgeStrip(B, pts, "weedStrip", new StripOptions { Side = side, Offset = -0.55f, Gaps = 0.4f, Run = new float2(8f, 30f), Opacity = Op(0.75f, 1f), Cause = "fence:weeds" });
                    foreach (var p in pts)
                    {
                        float d = math.distance(p, gate);
                        if (d > wildD && In(p + (insideLeft ? left : -left) * 3f, 1f)) { wildD = d; wildAt = p + (insideLeft ? left : -left) * 3f; }
                    }
                }
                if (Flaws.Contains("wildCorner") && wildD > 0f)
                {
                    var d = math.normalizesafe(Poly.Centroid(_ring) - wildAt);
                    Brushes.At(B, wildAt, d, "dryGrass", R(9f, 12f, 500), Op(0.5f, 0.8f), 3, ColourLevel.Any, "flaw:wildCorner");
                    Brushes.At(B, wildAt + d * 2f, d, "weedStrip", R(6f, 8f, 501), Op(0.6f, 0.9f), 2, ColourLevel.Any, "flaw:wildCorner");
                }
            }

            private static float2[] Closed(float2[] r) { var c = new float2[r.Length + 1]; Array.Copy(r, c, r.Length); c[r.Length] = r[0]; return c; }

            /// <summary>Concrete slabs: cracks a step in from the edge, weeds in the cracks at the back of the yard.</summary>
            private void Slabs()
            {
                var gate = _y.Gates.Count > 0 ? _y.Gates[0].p : Poly.Centroid(_ring);
                float far = 0f;
                foreach (var p in _ring) far = math.max(far, math.distance(p, gate));
                int n = 0;
                foreach (var s in _slabs)
                {
                    n++;
                    float area = Poly.Area(s);
                    var c = Poly.Centroid(s);
                    bool back = far > 0f && math.distance(c, gate) > far * 0.55f;
                    var inset = Brushes.Inset(s, 1.6f);
                    if (inset != null && Poly.IsCcw(inset) && Poly.Area(inset) > 40f)
                        Brushes.CracksAlong(B, Closed(inset), back && U(600, n) < 0.45f ? "crackWeeds" : "crackConcrete", new StripOptions { Gaps = 0.72f, Run = new float2(5f, 16f), Opacity = Op(0.65f, 0.95f), Width = new float2(0.8f, 1f), Cause = back ? "slabs:weedyCracks" : "slabs:cracks" });
                    // a slab that has carried boxes for years is not white: faint stains where they stood and drained
                    Brushes.Tone(B, BrushArea.Of(s), "pavingTonePatch", Op(0.3f, 0.45f), 0.25f, ColourLevel.Quiet, "slabs:tone");
                    if (area > 500f) Brushes.Scatter(B, BrushArea.Of(s), "crackWeb", 0.02f * _a, new ScatterOptions { Opacity = Op(0.35f, 0.6f), Clump = 1.5f, Cause = "slabs:crackWeb" });
                }
            }

            /// <summary>Where water leaks from the toe of a tip or a compost row: dark seeps with an iron stain.</summary>
            private void Seeps()
            {
                int n = 0;
                foreach (var p in _y.Seeps)
                {
                    n++;
                    if (!In(p, 1f)) continue;
                    float a = U(700, n) * 6.283f;
                    Brushes.At(B, p, new float2(math.cos(a), math.sin(a)), "leachateSeep", R(3.5f, 6f, 701, n), Op(0.5f, 0.85f), 1, ColourLevel.Any, "seeps");
                }
            }
        }
    }
}
