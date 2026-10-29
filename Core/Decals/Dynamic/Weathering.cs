// Placemaker — dynamic decals: weathering grows from causes, not one patch per object (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// After the whole plan is composed and graded, this pass reads what was built and asks why each place would get dirty:
//   play       goalmouths, the centre spot, the spine down the middle, the linesmen's tracks (one each half, opposite
//              touchlines), warm-up wear behind the busy end, the turf in front of dugouts and stands
//   feet       mud where players come off the pitch onto a path, prints fading along it, shortcuts trodden into the grass
//   water      puddles in the real low points of the built ground (paths and car park; 2-3 cm of micro-unevenness on hard
//              ground), the deepest wet, the rest dry tidemarks
//   wind       one prevailing wind: leaves blown from groups of trees pile against the first kerb, fence or stand
//              downwind (never a disc under each tree); court corners downwind collect dust and leaves
//   wheels     the car park's wheel paths and lane oil line, oil under the busy stalls, kerb grime in broken runs, a few
//              cracks with tar seals, the low corner's puddle, the tone of the whole asphalt
//   objects    rust at the foot of a few steel masts and lamps
// Heavy-tailed on purpose: a few hot spots carry most of it, most of the ground stays clean (Budget), and one or two
// "signature flaws" per site are exaggerated. Deterministic from the step's seed.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public static class Weathering
    {
        /// <summary>The player's amount (percent, 0..300): sports park "Grime".</summary>
        public const string GrimeId = "grime";
        /// <summary>The prevailing wind (direction it blows toward), one for every city: from the south-west.</summary>
        public static readonly float2 Wind = math.normalize(new float2(0.62f, 0.78f));
        /// <summary>Weather of the last days for puddles and mud (0 dry .. 1 soaked). A later knob.</summary>
        public const float RecentRain = 0.6f;
        public const int MaxStrokes = 400;

        /// <summary>The types (and their styles) that get weathering. v1: the everyday sports park.</summary>
        public static bool Wants(string layerId, SettingsBag values)
        {
            if (layerId != "sportsPark") return false;
            string subtype = values?.S("subtype", "community") ?? "community";
            return subtype == "community";
        }

        public static float Amount(SettingsBag values)
        {
            double v = values == null ? 100 : values.Num(GrimeId, 100);
            if (double.IsNaN(v)) v = 100;
            return (float)Math.Max(0, Math.Min(300, v)) / 100f;
        }

        public sealed class Report
        {
            public readonly Dictionary<string, int> ByCause = new Dictionary<string, int>(StringComparer.Ordinal);
            public int Strokes;
            /// <summary>Share of the site any stroke touches, and where one is strong (≥ 0.6).</summary>
            public float Touched, Strong;
            public readonly List<string> Flaws = new List<string>();
            public double Ms;

            public override string ToString()
            {
                var parts = new List<string>();
                foreach (var kv in ByCause) parts.Add(kv.Key + " " + kv.Value);
                return $"{Strokes} strokes ({string.Join(", ", parts)}), {Touched * 100f:F0} % of the site touched, {Strong * 100f:F1} % strong, flaws [{string.Join(", ", Flaws)}], {Ms:F0} ms";
            }
        }

        /// <summary>Runs over a composed, graded plan (after SiteHarmony and SiteSafety). Strokes go into the host layer's plan.</summary>
        public static Report Apply(ComposedPlan plan, AreaShape area, SiteContext site, IReadOnlyDictionary<AreaPlan, DressSettings> dress)
        {
            var total = new Report();
            if (plan?.Layers == null || dress == null) return total;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var groups = new Dictionary<DressSettings, List<AreaPlan>>();
            var order = new List<DressSettings>();
            foreach (var l in plan.Layers)
            {
                if (l == null || !dress.TryGetValue(l, out var s) || s == null || !s.Weathering || s.Grime <= 0f) continue;
                if (!groups.TryGetValue(s, out var list)) { groups[s] = list = new List<AreaPlan>(); order.Add(s); }
                list.Add(l);
            }
            if (order.Count == 0) return total;
            Func<float2, float> ground;
            try { ground = SiteHarmony.FinalGround(plan, site?.HeightAt ?? (p => 0f)); }
            catch (Exception) { ground = site?.HeightAt ?? (p => 0f); }
            foreach (var s in order)
            {
                var layers = groups[s];
                AreaPlan host = layers.Find(l => l.GeneratorId == s.LayerId) ?? layers[0];
                var ring = s.Ring != null && s.Ring.Length >= 3 ? s.Ring : area?.Ring2D();
                if (ring == null || ring.Length < 3) continue;
                var scene = new Scene(layers, ring, site, ground, Hashing.Mix(host.Seed == 0 ? 1u : host.Seed, 0xD1Au), s.Grime);
                var r = scene.Run();
                host.Decals.AddRange(scene.Out);
                total.Strokes += r.Strokes;
                foreach (var kv in r.ByCause) total.ByCause[kv.Key] = (total.ByCause.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                total.Flaws.AddRange(r.Flaws);
                total.Touched = math.max(total.Touched, r.Touched);
                total.Strong = math.max(total.Strong, r.Strong);
                host.Facts["decalStrokes"] = r.Strokes;
                host.Facts["decalTouchedPct"] = Math.Round(r.Touched * 100f, 1);
                host.Facts["decalStrongPct"] = Math.Round(r.Strong * 100f, 1);
            }
            total.Ms = clock.Elapsed.TotalMilliseconds;
            return total;
        }

        // ───────────────────────── the scene: causes read from the plan ─────────────────────────

        internal sealed class PathLine
        {
            public float2[] P;
            public float Half;
            public bool Drive;
        }

        internal struct Thing { public float2 P; public float Yaw; public string Id; }

        internal sealed class Scene
        {
            private readonly List<AreaPlan> _layers;
            private readonly float2[] _ring;
            private readonly SiteContext _site;
            private readonly Func<float2, float> _ground;
            private readonly uint _seed;
            private readonly float _amount;
            private readonly Report _r = new Report();
            public readonly List<DecalStroke> Out = new List<DecalStroke>();

            public readonly List<PathLine> Paths = new List<PathLine>(), Aisles = new List<PathLine>(), Fences = new List<PathLine>();
            public readonly List<float2> Trees = new List<float2>();
            public readonly List<Thing> Goals = new List<Thing>(), Stands = new List<Thing>(), Benches = new List<Thing>(), Steel = new List<Thing>(), Bays = new List<Thing>();
            public readonly List<float2[]> Asphalt = new List<float2[]>(), Courts = new List<float2[]>(), Hard = new List<float2[]>(), Pitches = new List<float2[]>();
            public readonly List<float2> Gates = new List<float2>();
            private float _pitchWidth = 0f;

            public Scene(List<AreaPlan> layers, float2[] ring, SiteContext site, Func<float2, float> ground, uint seed, float amount)
            {
                _layers = layers; _ring = ring; _site = site; _ground = ground; _seed = seed; _amount = amount;
                Read();
            }

            private uint H(int a, int b = 0) => Hashing.Mix(_seed, a, b);
            private float U(int a, int b = 0) => Hashing.Unit(H(a, b));
            private float Range(float lo, float hi, int a, int b = 0) => math.lerp(lo, hi, U(a, b));

            private static float2[] Sample(PlannedNet n, float step)
            {
                float2 a = n.A.xz, b = n.B.xz, c = n.C.xz, d = n.D.xz;
                float len = math.distance(a, b) + math.distance(b, c) + math.distance(c, d);
                int k = math.clamp((int)math.ceil(len / step), 1, 400);
                var pts = new float2[k + 1];
                for (int i = 0; i <= k; i++)
                {
                    float t = i / (float)k, u = 1f - t;
                    pts[i] = u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
                }
                return pts;
            }

            private void Read()
            {
                foreach (var l in _layers)
                {
                    bool court = l.GeneratorId == "tennisCourts" || l.GeneratorId == "basketballCourt" || l.GeneratorId == "skateBowl";
                    bool pitch = l.GeneratorId == "pitch";
                    if (pitch && l.Facts.TryGetValue("fieldWidth", out var fw)) _pitchWidth = math.max(_pitchWidth, (float)fw);
                    foreach (var n in l.Nets)
                    {
                        if (n == null) continue;
                        if (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide)
                            Paths.Add(new PathLine { P = Sample(n, 1.5f), Half = SiteHarmony.HalfWidth(n, _site) });
                        else if (n.Role == NetRole.DriveAisle || n.Role == NetRole.DriveConnector || n.Role == NetRole.Alley)
                            Aisles.Add(new PathLine { P = Sample(n, 2f), Half = SiteHarmony.HalfWidth(n, _site), Drive = true });
                        else if (n.Role == NetRole.Fence)
                            Fences.Add(new PathLine { P = Sample(n, 2f), Half = 0.2f });
                    }
                    foreach (var s in l.Surfaces)
                    {
                        if (s?.Ring == null || s.Ring.Length < 3) continue;
                        var r2 = Poly.To2D(s.Ring);
                        float a = Poly.Area(r2);
                        if (court && a > 120f) Courts.Add(r2);
                        else if (pitch && a > 800f) Pitches.Add(r2);
                        if (s.Role == SurfaceRole.Asphalt) Asphalt.Add(r2);
                        else if (s.Role == SurfaceRole.Paving || s.Role == SurfaceRole.Plaza) Hard.Add(r2);
                    }
                    foreach (var o in l.Objects)
                    {
                        if (o == null) continue;
                        string id = o.Asset.Id ?? "";
                        var t = new Thing { P = o.Position.xz, Yaw = o.Yaw, Id = id };
                        if (o.Role == ObjectRole.Tree) Trees.Add(o.Position.xz);
                        else if (id.StartsWith("SportParkFootballGoal", StringComparison.Ordinal)) Goals.Add(t);
                        else if (id.StartsWith("SportParkStand", StringComparison.Ordinal) || id.StartsWith("SportParkBench", StringComparison.Ordinal)) Stands.Add(t);
                        else if (o.Role == ObjectRole.Bench) Benches.Add(t);
                        else if (o.Role == ObjectRole.ParkingBay || o.Category == AssetCategories.ParkingDecal) Bays.Add(t);
                        if (o.Role == ObjectRole.Lamp || id.StartsWith("Lightpole", StringComparison.Ordinal)) Steel.Add(t);
                    }
                }
                // Gates: path ends at the outline (the street side), where people come in.
                foreach (var p in Paths)
                    foreach (var e in new[] { p.P[0], p.P[p.P.Length - 1] })
                        if (Poly.DistanceToBoundary(_ring, e) < 4f && !Gates.Exists(g => math.distancesq(g, e) < 36f)) Gates.Add(e);
            }

            private void Add(DecalStroke s, string cause)
            {
                if (Out.Count >= MaxStrokes) return;
                s.Strength = math.saturate(s.Strength * math.min(2f, _amount));
                if (s.Strength < 0.04f) return;
                s.Cause = cause;
                s.Age = math.saturate(s.Age);
                try { s.Bounds(out var lo, out var hi); s.Y = _ground((lo + hi) * 0.5f); } catch (Exception) { }
                Out.Add(s);
                _r.ByCause[cause] = (_r.ByCause.TryGetValue(cause, out var n) ? n : 0) + 1;
            }

            private bool Inside(float2 p, float inset = 0f) => Poly.Contains(_ring, p) && (inset <= 0f || Poly.DistanceToBoundary(_ring, p) >= inset);

            public Report Run()
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var flaws = PickFlaws();
                Play(flaws);
                Feet(flaws);
                Water(flaws);
                Leaves(flaws);
                Wheels();
                Objects();
                Budget();
                _r.Flaws.AddRange(flaws);
                _r.Strokes = Out.Count;
                _r.Ms = clock.Elapsed.TotalMilliseconds;
                return _r;
            }

            private List<string> PickFlaws()
            {
                // one or two things this site is known for
                var all = new List<string> { "baldGoalmouth", "bigPuddle", "leafChokedCorner", "shortcut" };
                var pick = new List<string>();
                int first = (int)(U(901) * all.Count) % all.Count;
                pick.Add(all[first]);
                if (U(902) < 0.6f) pick.Add(all[(first + 1 + (int)(U(903) * (all.Count - 1))) % all.Count]);
                return pick;
            }

            // ───────────── play: the pitch ─────────────

            private void Play(List<string> flaws)
            {
                var used = new HashSet<int>();
                int pitchNo = 0;
                for (int i = 0; i < Goals.Count; i++)
                {
                    if (used.Contains(i)) continue;
                    int best = -1; float bd = float.MaxValue;
                    for (int j = 0; j < Goals.Count; j++)
                    {
                        if (j == i || used.Contains(j)) continue;
                        float d = math.distance(Goals[i].P, Goals[j].P);
                        if (d > 25f && d < 140f && d < bd) { bd = d; best = j; }
                    }
                    if (best < 0) continue;
                    used.Add(i); used.Add(best);
                    Pitch(Goals[i].P, Goals[best].P, pitchNo++, flaws);
                }
                // In front of the stands and dugouts: the turf (or paving) people stand on, broken, not a band. The busiest few:
                // the ones nearest a pitch (a row of spectator benches is one place, not one patch per bench).
                var busy = new List<Thing>(Stands);
                busy.Sort((a, b) => NearestPitch(a.P).CompareTo(NearestPitch(b.P)));
                var taken = new List<float2>();
                foreach (var st in busy)
                {
                    if (taken.Count >= 5 || taken.Exists(q => math.distancesq(q, st.P) < 12f * 12f)) continue;
                    taken.Add(st.P);
                    var front = Front(st);
                    if (front.Equals(float2.zero)) continue;
                    bool dugout = st.Id.StartsWith("SportParkBench", StringComparison.Ordinal);
                    float len = dugout ? 7f : 18f;
                    var c = st.P + front * (dugout ? 2.2f : 8f);
                    var along = new float2(front.y, -front.x);
                    int k = Out.Count;
                    var pts = new[] { c - along * len * 0.5f, c - along * len * 0.15f + front * Range(-0.6f, 0.6f, 31, k), c + along * len * 0.2f + front * Range(-0.6f, 0.6f, 32, k), c + along * len * 0.5f };
                    var s = DecalStroke.Path("wornGrass", H(33, k), pts, dugout ? 3.2f : 4.5f, dugout ? 0.75f : 0.5f);
                    Add(s, dugout ? "dugout" : "stand");
                }
            }

            /// <summary>Where a stand or bench looks: toward the nearest pitch or court, else its own facing.</summary>
            private float2 Front(Thing t)
            {
                float2 target = default; float bd = 80f * 80f; bool found = false;
                foreach (var g in PitchCentres)
                {
                    float d = math.distancesq(g, t.P);
                    if (d < bd) { bd = d; target = g; found = true; }
                }
                if (found) return math.normalizesafe(target - t.P);
                return new float2(math.sin(t.Yaw), math.cos(t.Yaw));
            }

            private readonly List<float2> PitchCentres = new List<float2>();

            private float NearestPitch(float2 p)
            {
                float best = float.MaxValue;
                foreach (var c in PitchCentres) best = math.min(best, math.distance(c, p));
                return best;
            }

            private void Pitch(float2 g1, float2 g2, int no, List<string> flaws)
            {
                var centre = (g1 + g2) * 0.5f;
                PitchCentres.Add(centre);
                float len = math.distance(g1, g2);
                var axis = math.normalizesafe(g2 - g1);
                var side = new float2(-axis.y, axis.x);
                float halfW = (_pitchWidth > 10f ? _pitchWidth : len * 0.64f) * 0.5f;
                float yawAxis = math.atan2(-axis.y, axis.x);         // local +x along the axis
                float yawSide = math.atan2(-side.y, side.x);         // local +x across
                // the busy end: nearer the stands / dugouts (warm-ups happen there)
                float2 near = Stands.Count > 0 ? Stands[0].P : centre + side * 50f;
                int busy = math.distancesq(near, g1) < math.distancesq(near, g2) ? 0 : 1;
                bool bald = flaws.Contains("baldGoalmouth");
                int baldEnd = (int)(U(41, no) * 2f);
                for (int e = 0; e < 2; e++)
                {
                    var g = e == 0 ? g1 : g2;
                    var inward = e == 0 ? axis : -axis;
                    bool worst = bald && e == baldEnd;
                    float k = worst ? 1.35f : 1f;
                    // goalmouth: wide and shallow, skewed off-centre (keepers dive one way more than the other)
                    var gm = g + inward * Range(4.5f, 6f, 42, e + no * 4) + side * Range(-1.2f, 1.2f, 43, e + no * 4);
                    Add(DecalStroke.Box("wornGrass", H(44, e + no * 4), gm, yawSide, new float2(Range(13f, 17f, 45, e) * k, Range(8f, 11f, 46, e) * k), (worst ? 1f : Range(0.8f, 0.95f, 47, e))), "goalmouth");
                    // the crease: worn through to the soil, damp after rain
                    var cr = g + inward * Range(1.6f, 2.4f, 48, e) + side * Range(-0.5f, 0.5f, 49, e);
                    var crease = DecalStroke.Box("bareEarth", H(50, e + no * 4), cr, yawSide, new float2(Range(5.5f, 7.5f, 51, e) * k, Range(2.6f, 3.6f, 52, e) * k), worst ? 0.95f : 0.78f);
                    crease.Wetness = RecentRain * Range(0.6f, 1f, 53, e);
                    Add(crease, "goalCrease");
                    // warm-up wear behind the busy end
                    if (e == busy)
                    {
                        var wu = g - inward * Range(4f, 6f, 54, e) + side * Range(-6f, 6f, 55, e);
                        Add(DecalStroke.Box("wornGrass", H(56, e + no * 4), wu, yawSide + Range(-0.3f, 0.3f, 57, e), new float2(Range(11f, 15f, 58, e), Range(4.5f, 6.5f, 59, e)), 0.62f), "warmUp");
                    }
                }
                // centre spot and the spine: midfield takes the most running
                Add(DecalStroke.Box("wornGrass", H(60, no), centre + side * Range(-2f, 2f, 61, no), yawAxis + Range(-0.4f, 0.4f, 62, no), new float2(Range(9f, 12f, 63, no), Range(8f, 11f, 64, no)), 0.7f), "centreSpot");
                {
                    var a = g1 + axis * 11f; var b = g2 - axis * 11f;
                    var pts = new float2[7];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float t = i / (float)(pts.Length - 1);
                        pts[i] = math.lerp(a, b, t) + side * (Range(-2.5f, 2.5f, 65, i + no * 8) * math.sin(t * 3.14159f));
                    }
                    var spine = DecalStroke.Path("wornGrass", H(66, no), pts, 13f, 0.5f);
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float t = i / (float)(pts.Length - 1);
                        spine.Widths[i] = math.lerp(7f, 15f, math.sin(t * 3.14159f));
                    }
                    Add(spine, "spine");
                }
                // the linesmen: each runs one half, on opposite touchlines, just outside the line
                for (int e = 0; e < 2; e++)
                {
                    float sd = e == 0 ? 1f : -1f;
                    var from = centre + side * sd * (halfW + 1.3f) + axis * (e == 0 ? 1f : -1f) * 2f;
                    var to = centre + side * sd * (halfW + 1.3f) + axis * (e == 0 ? 1f : -1f) * (len * 0.5f - 12f);
                    var pts = new[] { from, math.lerp(from, to, 0.33f) + side * Range(-0.4f, 0.4f, 70, e), math.lerp(from, to, 0.66f) + side * Range(-0.4f, 0.4f, 71, e), to };
                    if (!Inside(to) || !Inside(from)) continue;
                    Add(DecalStroke.Path("desirePath", H(72, e + no * 4), pts, 1.5f, 0.75f), "linesman");
                }
            }

            // ───────────── feet: mud off the pitch, prints, shortcuts ─────────────

            private (PathLine line, int idx, float d) NearestPath(float2 p)
            {
                PathLine bl = null; int bi = -1; float bd = float.MaxValue;
                foreach (var l in Paths)
                    for (int i = 0; i < l.P.Length; i++)
                    {
                        float d = math.distancesq(l.P[i], p);
                        if (d < bd) { bd = d; bl = l; bi = i; }
                    }
                return (bl, bi, math.sqrt(bd));
            }

            private void Feet(List<string> flaws)
            {
                // Mud where players step off the pitch onto a path: the path points nearest each pitch corner on the busy side.
                int n = 0;
                foreach (var c in PitchCentres)
                {
                    var targets = new List<float2>();
                    foreach (var st in Stands) if (math.distance(st.P, c) < 90f) targets.Add(st.P);
                    if (targets.Count == 0) targets.Add(c);
                    var seen = new List<float2>();
                    foreach (var t in targets)
                    {
                        var (line, idx, d) = NearestPath(math.lerp(c, t, 0.75f));
                        if (line == null || d > 40f) continue;
                        var at = line.P[idx];
                        if (seen.Exists(q => math.distancesq(q, at) < 400f)) continue;
                        seen.Add(at);
                        var toPitch = math.normalizesafe(c - at);
                        var mudC = at + toPitch * (line.Half + Range(0.8f, 1.8f, 80, n));
                        var mud = DecalStroke.Box("mud", H(81, n), mudC, math.atan2(-toPitch.y, toPitch.x), new float2(Range(4.5f, 6.5f, 82, n), Range(3.5f, 5f, 83, n)), 0.85f);
                        mud.Wetness = RecentRain; mud.Layers = DecalLayerBits.Ground;
                        Add(mud, "gatewayMud");
                        // prints fading along the path, toward the nearer end
                        int dir = idx < line.P.Length / 2 ? 1 : -1;
                        var pts = new List<float2>();
                        float walked = 0f;
                        for (int i = idx; i >= 0 && i < line.P.Length && walked < 14f; i += dir)
                        {
                            if (pts.Count > 0) walked += math.distance(pts[pts.Count - 1], line.P[i]);
                            pts.Add(line.P[i]);
                        }
                        if (pts.Count >= 2)
                        {
                            var pr = DecalStroke.Path("muddyPrints", H(84, n), pts.ToArray(), math.max(1.2f, line.Half * 1.4f), 0.8f);
                            pr.Layers = DecalLayerBits.Ground;
                            Add(pr, "prints");
                        }
                        n++;
                        if (n >= 3) break;
                    }
                }
                // Shortcuts: from a gate to where people go, straight across the grass, when the paths take the long way round.
                var dests = new List<float2>();
                foreach (var st in Stands) dests.Add(st.P);
                foreach (var cr in Courts) dests.Add(Poly.Centroid(cr));
                var cands = new List<(float2 a, float2 b, float score)>();
                foreach (var g in Gates)
                    foreach (var dst in dests)
                    {
                        float L = math.distance(g, dst);
                        if (L < 25f || L > 110f) continue;
                        var dir = math.normalizesafe(dst - g);
                        var a = g + dir * 3f; var b = dst - dir * 9f;
                        if (!Clear(a, b)) continue;
                        // how far the paths are from the straight line in its middle: the bigger, the more tempting the shortcut
                        float off = NearestPath(math.lerp(a, b, 0.5f)).d;
                        if (off < 9f) continue;
                        cands.Add((a, b, off * L));
                    }
                cands.Sort((x, y) => y.score.CompareTo(x.score));
                int take = flaws.Contains("shortcut") ? 2 : 1;
                for (int i = 0; i < cands.Count && i < take; i++)
                {
                    var (a, b, _) = cands[i];
                    var side = math.normalizesafe(new float2(-(b - a).y, (b - a).x));
                    var pts = new[] { a, math.lerp(a, b, 0.3f) + side * Range(-2f, 2f, 90, i), math.lerp(a, b, 0.65f) + side * Range(-2f, 2f, 91, i), b };
                    Add(DecalStroke.Path("desirePath", H(92, i), pts, Range(1.1f, 1.5f, 93, i), flaws.Contains("shortcut") && i == 0 ? 0.75f : 0.5f), "shortcut");
                }
            }

            /// <summary>A straight walk from a to b crosses no field, court, hard surface or tree trunk and stays in the site.</summary>
            private bool Clear(float2 a, float2 b)
            {
                for (int i = 0; i <= 12; i++)
                {
                    var p = math.lerp(a, b, i / 12f);
                    if (!Inside(p, 1f)) return false;
                    foreach (var r in Pitches) if (Poly.Contains(r, p)) return false;
                    foreach (var r in Courts) if (Poly.Contains(r, p)) return false;
                    foreach (var r in Hard) if (Poly.Contains(r, p)) return false;
                    foreach (var r in Asphalt) if (Poly.Contains(r, p)) return false;
                }
                foreach (var t in Trees) if (SegDist(t, a, b) < 1.5f) return false;
                return true;
            }

            private static float SegDist(float2 p, float2 a, float2 b)
            {
                var ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
                return math.distance(p, a + ab * t);
            }

            // ───────────── water: puddles in the real low points ─────────────

            /// <summary>The built ground plus the few centimetres of unevenness any hard surface has.</summary>
            private float Level(float2 p) => _ground(p) + 0.028f * DecalNoise.Fbm(p.x / 7f, p.y / 7f, 3, _seed + 5u) + 0.01f * DecalNoise.Perlin(p.x / 1.8f, p.y / 1.8f, _seed + 6u);

            private void Water(List<string> flaws)
            {
                var pits = new List<(float2 p, float2 dir, float depth, float half, bool lot)>();
                // along the paths: local minima of the level along each line
                foreach (var l in Paths)
                {
                    var hgt = new float[l.P.Length];
                    for (int i = 0; i < hgt.Length; i++) hgt[i] = Level(l.P[i]);
                    for (int i = 2; i < hgt.Length - 2; i++)
                    {
                        float lo = hgt[i];
                        bool min = true; float rise = float.MaxValue;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            float peak = lo;
                            for (int k = 1; k <= 6 && i + side * k >= 0 && i + side * k < hgt.Length; k++)
                            {
                                float h = hgt[i + side * k];
                                if (h < lo - 1e-4f) { min = false; break; }
                                peak = math.max(peak, h);
                            }
                            rise = math.min(rise, peak - lo);
                            if (!min) break;
                        }
                        if (!min || rise < 0.006f) continue;
                        var dir = math.normalizesafe(l.P[math.min(hgt.Length - 1, i + 1)] - l.P[math.max(0, i - 1)]);
                        pits.Add((l.P[i], dir, rise, l.Half, false));
                    }
                }
                // the car park: minima of a 2 m grid over the asphalt
                foreach (var r in Asphalt)
                {
                    Poly.Bounds(r, out var lo, out var hi);
                    const float step = 2f;
                    int nx = math.min(120, (int)((hi.x - lo.x) / step) + 1), nz = math.min(120, (int)((hi.y - lo.y) / step) + 1);
                    var g = new float[nx * nz];
                    var inside = new bool[nx * nz];
                    for (int j = 0; j < nz; j++)
                        for (int i = 0; i < nx; i++)
                        {
                            var p = lo + new float2(i, j) * step;
                            inside[j * nx + i] = Poly.Contains(r, p) && Poly.DistanceToBoundary(r, p) > 1.5f;
                            g[j * nx + i] = Level(p);
                        }
                    for (int j = 2; j < nz - 2; j++)
                        for (int i = 2; i < nx - 2; i++)
                        {
                            if (!inside[j * nx + i]) continue;
                            float h = g[j * nx + i]; bool min = true; float peak = h;
                            for (int dj = -3; dj <= 3 && min; dj++)
                                for (int di = -3; di <= 3; di++)
                                {
                                    int x = i + di, z = j + dj;
                                    if (x < 0 || z < 0 || x >= nx || z >= nz || (di == 0 && dj == 0)) continue;
                                    if (g[z * nx + x] < h - 1e-4f) { min = false; break; }
                                    peak = math.max(peak, g[z * nx + x]);
                                }
                            if (!min || peak - h < 0.006f) continue;
                            pits.Add((lo + new float2(i, j) * step, new float2(1f, 0f), (peak - h) * 0.8f, 2.2f, true));
                        }
                }
                pits.Sort((a, b) => b.depth.CompareTo(a.depth));
                bool big = flaws.Contains("bigPuddle");
                int wet = 0, dry = 0, k2 = 0;
                var placed = new List<float2>();
                foreach (var (p, dir, depth, half, lot) in pits)
                {
                    if (placed.Exists(q => math.distancesq(q, p) < 15f * 15f)) continue;
                    bool isWet = wet < 2;
                    if (!isWet && dry >= 3) break;
                    float scale = math.saturate(depth / 0.03f) * 0.6f + 0.6f;
                    if (big && k2 == 0) scale *= 1.8f;
                    float yaw = math.atan2(-dir.y, dir.x) + Range(-0.25f, 0.25f, 100, k2);
                    var size = lot ? new float2(Range(3f, 5.5f, 101, k2), Range(2.2f, 4f, 102, k2)) * scale
                                   : new float2(Range(2.4f, 4.4f, 103, k2) * scale, math.max(1.4f, half * 2f * Range(0.75f, 1.05f, 104, k2)));
                    var s = DecalStroke.Box("puddle", H(105, k2), p + new float2(Range(-0.3f, 0.3f, 106, k2), Range(-0.3f, 0.3f, 107, k2)), yaw, size, 0.95f);
                    s.Wetness = isWet ? (big && k2 == 0 ? 1f : RecentRain + 0.3f) : 0f;
                    s.Layers = lot ? DecalLayerBits.Terrain : DecalLayerBits.Roads;
                    Add(s, isWet ? "puddle" : "tidemark");
                    placed.Add(p);
                    if (isWet) wet++; else dry++;
                    k2++;
                }
            }

            // ───────────── wind: leaves drift until something stops them ─────────────

            private struct Barrier { public float2 A, B; public int Group; }

            private void Leaves(List<string> flaws)
            {
                // the barriers leaves meet: path edges, asphalt and court edges, fences, the fronts and backs of stands
                var bars = new List<Barrier>();
                int gid = 0;
                foreach (var l in Paths)
                {
                    for (int sideSign = -1; sideSign <= 1; sideSign += 2, gid++)
                        for (int i = 0; i + 1 < l.P.Length; i++)
                        {
                            var d = math.normalizesafe(l.P[i + 1] - l.P[i]);
                            var nrm = new float2(-d.y, d.x) * sideSign * l.Half;
                            bars.Add(new Barrier { A = l.P[i] + nrm, B = l.P[i + 1] + nrm, Group = gid });
                        }
                }
                foreach (var set in new[] { Asphalt, Courts, Hard })
                    foreach (var r in set)
                    {
                        for (int i = 0; i < r.Length; i++) bars.Add(new Barrier { A = r[i], B = r[(i + 1) % r.Length], Group = gid });
                        gid++;
                    }
                foreach (var f in Fences) { for (int i = 0; i + 1 < f.P.Length; i++) bars.Add(new Barrier { A = f.P[i], B = f.P[i + 1], Group = gid }); gid++; }
                if (bars.Count == 0 || Trees.Count == 0) return;
                // every tree sheds downwind; the first barrier on the way catches (with some spread across the wind)
                var hits = new List<(int bar, float2 p, float w)>();
                var across = new float2(-Wind.y, Wind.x);
                for (int t = 0; t < Trees.Count; t++)
                {
                    for (int ray = 0; ray < 3; ray++)
                    {
                        var start = Trees[t] + across * (ray - 1) * 2.5f;
                        var dir = math.normalizesafe(Wind + across * Range(-0.35f, 0.35f, 110 + ray, t));
                        var end = start + dir * 24f;
                        int best = -1; float bt = 2f; float2 bp = default;
                        for (int b = 0; b < bars.Count; b++)
                        {
                            if (Poly.SegmentIntersection(start, end, bars[b].A, bars[b].B, out var pt, out float ta, out _) && ta < bt && ta > 0.02f) { bt = ta; best = b; bp = pt; }
                        }
                        if (best >= 0) hits.Add((best, bp, (1f - bt * 0.7f) / 3f));
                    }
                }
                // cluster hits per barrier group along it: each cluster becomes one drift
                var used = new bool[hits.Count];
                var drifts = new List<(float2[] pts, float w)>();
                for (int i = 0; i < hits.Count; i++)
                {
                    if (used[i]) continue;
                    used[i] = true;
                    var members = new List<int> { i };
                    int g = bars[hits[i].bar].Group;
                    for (bool grew = true; grew;)
                    {
                        grew = false;
                        for (int j = 0; j < hits.Count; j++)
                        {
                            if (used[j] || bars[hits[j].bar].Group != g) continue;
                            foreach (int m in members)
                                if (math.distancesq(hits[m].p, hits[j].p) < 7f * 7f) { used[j] = true; members.Add(j); grew = true; break; }
                        }
                    }
                    float w = 0f;
                    foreach (int m in members) w += hits[m].w;
                    if (w < 0.45f) continue;   // a single tree's leaves blow on
                    // the drift follows the barrier through its hits (sorted along it), a little upwind of it, running on past the
                    // outermost hits along the barrier's own direction there
                    var bar = bars[hits[i].bar];
                    var tan = math.normalizesafe(bar.B - bar.A);
                    float2 o = hits[i].p;
                    members.Sort((x, y) => math.dot(hits[x].p - o, tan).CompareTo(math.dot(hits[y].p - o, tan)));
                    var line = new List<float2>();
                    foreach (int m in members)
                        if (line.Count == 0 || math.distance(line[line.Count - 1], hits[m].p) > 1.8f) line.Add(hits[m].p);
                    float2 t0 = math.normalizesafe(bars[hits[members[0]].bar].B - bars[hits[members[0]].bar].A, tan);
                    float2 t1 = math.normalizesafe(bars[hits[members[members.Count - 1]].bar].B - bars[hits[members[members.Count - 1]].bar].A, tan);
                    if (math.dot(t0, tan) < 0f) t0 = -t0;
                    if (math.dot(t1, tan) < 0f) t1 = -t1;
                    line.Insert(0, line[0] - t0 * Range(2f, 3.5f, 118, drifts.Count));
                    line.Add(line[line.Count - 1] + t1 * Range(2f, 3.5f, 119, drifts.Count));
                    var pts = new float2[line.Count];
                    for (int k = 0; k < line.Count; k++)
                    {
                        var tk = math.normalizesafe(line[math.min(line.Count - 1, k + 1)] - line[math.max(0, k - 1)], tan);
                        var nrm = new float2(-tk.y, tk.x);
                        if (math.dot(nrm, Wind) > 0f) nrm = -nrm;   // the upwind side
                        pts[k] = line[k] + nrm * (0.55f + Range(-0.2f, 0.3f, 120 + k, drifts.Count));
                    }
                    drifts.Add((pts, w));
                }
                drifts.Sort((a, b) => b.w.CompareTo(a.w));
                bool choked = flaws.Contains("leafChokedCorner");
                for (int i = 0; i < drifts.Count && i < 10; i++)
                {
                    var (pts, w) = drifts[i];
                    bool hero = choked && i == 0;
                    float width = math.clamp(0.9f + w * 0.5f, 1f, 2.4f) * (hero ? 1.6f : 1f);
                    // the drift's "across > 0" side must be the barrier: across is the left of the direction of travel
                    var dir = math.normalizesafe(pts[pts.Length - 1] - pts[0]);
                    var left = new float2(-dir.y, dir.x);
                    if (math.dot(left, Wind) < 0f) Array.Reverse(pts);
                    var s = DecalStroke.Path("leafDrift", H(130, i), pts, width, math.saturate(0.45f + w * 0.2f) * (hero ? 1.3f : 1f));
                    s.Layers = DecalLayerBits.Ground;
                    Add(s, "leafDrift");
                }
                // thin litter downwind of the bigger groups of trees (one sheet per group, never one per tree)
                var groups = TreeGroups(9f);
                groups.Sort((a, b) => b.Count.CompareTo(a.Count));
                for (int i = 0; i < groups.Count && i < 4; i++)
                {
                    var gr = groups[i];
                    if (gr.Count < 4) break;
                    float2 c = default; foreach (var p in gr) c += p; c /= gr.Count;
                    float ext = 0f; foreach (var p in gr) ext = math.max(ext, math.distance(p, c));
                    var at = c + Wind * Range(3f, 6f, 140, i);
                    var size = new float2(ext * 1.5f + 7f, ext + 5f);
                    float yaw = math.atan2(-Wind.y, Wind.x);
                    Add(DecalStroke.Box("leafLitter", H(141, i), at, yaw, size, Range(0.55f, 0.8f, 142, i)), "leafLitter");
                }
            }

            private List<List<float2>> TreeGroups(float link)
            {
                var res = new List<List<float2>>();
                var done = new bool[Trees.Count];
                for (int i = 0; i < Trees.Count; i++)
                {
                    if (done[i]) continue;
                    done[i] = true;
                    var g = new List<float2> { Trees[i] };
                    var q = new Queue<int>(); q.Enqueue(i);
                    while (q.Count > 0)
                    {
                        int a = q.Dequeue();
                        for (int j = 0; j < Trees.Count; j++)
                            if (!done[j] && math.distancesq(Trees[a], Trees[j]) < link * link) { done[j] = true; g.Add(Trees[j]); q.Enqueue(j); }
                    }
                    res.Add(g);
                }
                return res;
            }

            // ───────────── wheels: the car park ─────────────

            private void Wheels()
            {
                if (Asphalt.Count == 0 && Aisles.Count == 0) return;
                // the busy end is the one nearest the park's paths (people park close to where they go)
                float2 walk = Gates.Count > 0 ? Gates[0] : float2.zero;
                foreach (var a in Asphalt)
                {
                    var c = Poly.Centroid(a);
                    float best = float.MaxValue;
                    foreach (var l in Paths) foreach (var p in l.P) { float d = math.distancesq(p, c); if (d < best) { best = d; walk = p; } }
                    Add(DecalStroke.Outline("asphaltTone", H(150, 0), a, 0.9f), "asphaltTone");
                }
                int n = 0;
                foreach (var l in Aisles)
                {
                    if (l.P.Length < 2) continue;
                    float L = 0f; for (int i = 1; i < l.P.Length; i++) L += math.distance(l.P[i - 1], l.P[i]);
                    if (L < 8f) continue;
                    float near = math.saturate(1f - math.distance(l.P[l.P.Length / 2], walk) / 80f);
                    var wp = DecalStroke.Path("wheelPath", H(151, n), l.P, math.min(3.4f, l.Half * 1.2f), 0.55f + near * 0.35f);
                    wp.Layers = DecalLayerBits.Terrain;
                    Add(wp, "wheelPath");
                    var ol = DecalStroke.Path("oilLine", H(152, n), l.P, 0.8f, 0.45f + near * 0.35f);
                    Add(ol, "oilLine");
                    n++;
                }
                // oil under the busy stalls: the ones nearest the walk-in carry most of it
                var bays = new List<(Thing t, float d)>();
                foreach (var b in Bays) bays.Add((b, math.distance(b.P, walk)));
                bays.Sort((x, y) => x.d.CompareTo(y.d));
                int oil = 0;
                for (int i = 0; i < bays.Count && oil < 18; i++)
                {
                    float p = math.lerp(0.75f, 0.12f, i / math.max(1f, bays.Count - 1f));
                    if (U(160, i) > p) continue;
                    var b = bays[i].t;
                    // the engine end faces the aisle: toward the nearest aisle point
                    float2 toAisle = new float2(math.sin(b.Yaw), math.cos(b.Yaw));
                    float bd = float.MaxValue;
                    foreach (var l in Aisles) foreach (var q in l.P) { float d = math.distancesq(q, b.P); if (d < bd) { bd = d; toAisle = math.normalizesafe(q - b.P, toAisle); } }
                    var at = b.P + toAisle * Range(0.6f, 1.4f, 161, i) + new float2(Range(-0.3f, 0.3f, 162, i), Range(-0.3f, 0.3f, 163, i));
                    Add(DecalStroke.Box("oilStain", H(164, i), at, Range(0f, 6.28f, 165, i), new float2(Range(1.3f, 2.6f, 166, i), Range(1.0f, 1.9f, 167, i)), Range(0.5f, 0.95f, 168, i)), "stallOil");
                    oil++;
                }
                // kerb grime in broken runs along the lot's edges, cracks near the edges and corners
                int kg = 0, ck = 0;
                foreach (var r in Asphalt)
                {
                    bool ccw = Poly.IsCcw(r);
                    for (int i = 0; i < r.Length && kg < 14; i++)
                    {
                        var a = r[i]; var b = r[(i + 1) % r.Length];
                        float L = math.distance(a, b);
                        if (L < 6f) continue;
                        var tan = (b - a) / L;
                        var inward = ccw ? new float2(-tan.y, tan.x) : new float2(tan.y, -tan.x);
                        int runs = L > 40f ? 2 : 1;
                        for (int k = 0; k < runs; k++)
                        {
                            float t0 = Range(0f, 0.45f, 170 + k, i), t1 = math.min(1f, t0 + Range(0.25f, 0.55f, 172 + k, i));
                            var p0 = math.lerp(a, b, t0) + inward * 0.45f; var p1 = math.lerp(a, b, t1) + inward * 0.45f;
                            var pts = new[] { p0, math.lerp(p0, p1, 0.5f) + inward * Range(-0.15f, 0.2f, 174, i), p1 };
                            // across > 0 is the left of p0→p1; the kerb must be there: left = outward
                            var left = new float2(-tan.y, tan.x);
                            if (math.dot(left, -inward) < 0f) Array.Reverse(pts);
                            Add(DecalStroke.Path("kerbGrime", H(175 + k, i), pts, Range(0.8f, 1.2f, 176, i), Range(0.55f, 0.85f, 177, i)), "kerbGrime");
                            kg++;
                        }
                        if (ck < 3 && U(180, i) < 0.35f)
                        {
                            var at = math.lerp(a, b, Range(0.2f, 0.8f, 181, i)) + inward * Range(2.5f, 5f, 182, i);
                            Add(DecalStroke.Box("cracksTar", H(183, i), at, math.atan2(-tan.y, tan.x), new float2(Range(6f, 10f, 184, i), Range(4f, 7f, 185, i)), 0.85f), "cracks");
                            ck++;
                        }
                    }
                }
            }

            // ───────────── objects: corners, rust ─────────────

            private void Objects()
            {
                // court corners downwind collect dust and leaves
                int n = 0;
                foreach (var r in Courts)
                {
                    var c = Poly.Centroid(r);
                    var corners = new List<(float2 p, float score)>();
                    for (int i = 0; i < r.Length; i++)
                    {
                        var prev = r[(i + r.Length - 1) % r.Length]; var next = r[(i + 1) % r.Length];
                        var d0 = math.normalizesafe(r[i] - prev); var d1 = math.normalizesafe(next - r[i]);
                        if (math.abs(math.dot(d0, d1)) > 0.5f) continue;   // not a corner
                        corners.Add((r[i], math.dot(math.normalizesafe(r[i] - c), Wind)));
                    }
                    corners.Sort((a, b) => b.score.CompareTo(a.score));
                    for (int k = 0; k < corners.Count && k < 2; k++)
                    {
                        if (corners[k].score < 0.1f) break;
                        var p = corners[k].p;
                        var inward = math.normalizesafe(c - p);
                        // u runs toward the corner: local +x points from the inside to the corner
                        var toCorner = -inward;
                        var at = p + inward * 1.6f;
                        Add(DecalStroke.Box("cornerDebris", H(190, n), at, math.atan2(-toCorner.y, toCorner.x), new float2(4.2f, 3.4f), k == 0 ? 0.9f : 0.6f), "courtCorner");
                        n++;
                    }
                }
                // rust at the foot of a few steel masts and lamps (a third of them, the ones that would have rusted)
                int r2 = 0;
                for (int i = 0; i < Steel.Count && r2 < 8; i++)
                {
                    if (U(200, i) > 0.33f) continue;
                    var s = Steel[i];
                    bool mast = s.Id.StartsWith("LightpoleIndustrial", StringComparison.Ordinal);
                    float sz = mast ? Range(1.6f, 2.4f, 201, i) : Range(0.9f, 1.4f, 202, i);
                    Add(DecalStroke.Box("rustStain", H(203, i), s.P + Wind * sz * 0.2f, Range(0f, 6.28f, 204, i), new float2(sz, sz * 0.8f), Range(0.5f, 0.9f, 205, i)), "rust");
                    r2++;
                }
                // grime under the most used benches (near the gates and the pitch): a few, not all
                int b2 = 0;
                for (int i = 0; i < Benches.Count && b2 < 4; i++)
                {
                    if (U(210, i) > 0.3f) continue;
                    var b = Benches[i];
                    var front = new float2(math.sin(b.Yaw), math.cos(b.Yaw));
                    Add(DecalStroke.Box("stain", H(211, i), b.P + front * 0.8f, b.Yaw, new float2(2.2f, 1.4f), 0.7f), "benchStain");
                    b2++;
                }
            }

            // ───────────── budget: most of the site stays clean ─────────────

            private void Budget()
            {
                Measure(out float touched, out float strong);
                // over budget (a small site crowded by its own pitch): soften the broad strokes until it fits
                for (int pass = 0; pass < 4 && (touched > 0.4f || strong > 0.1f); pass++)
                {
                    foreach (var s in Out)
                        if (s.Kind == "wornGrass" || s.Kind == "leafLitter" || s.Kind == "asphaltTone" || s.Kind == "bareEarth") s.Strength *= 0.8f;
                    if (touched > 0.4f) Out.RemoveAll(s => s.Cause == "leafLitter" || s.Cause == "spine" || s.Cause == "warmUp");
                    Measure(out touched, out strong);
                }
                _r.Touched = touched; _r.Strong = strong;
            }

            /// <summary>Share of the site any stroke touches (strength over 0.05), and where the strongest is 0.6 or more (1.5 m cells).</summary>
            internal void Measure(out float touchedShare, out float strongShare)
            {
                touchedShare = strongShare = 0f;
                Poly.Bounds(_ring, out var lo, out var hi);
                const float cell = 1.5f;
                int nx = math.max(1, (int)((hi.x - lo.x) / cell) + 1), nz = math.max(1, (int)((hi.y - lo.y) / cell) + 1);
                if ((long)nx * nz > 2_000_000) return;
                var strength = new float[nx * nz];
                var isIn = new bool[nx * nz];
                int inside = 0;
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                        if (Poly.Contains(_ring, lo + new float2(i + 0.5f, j + 0.5f) * cell)) { isIn[j * nx + i] = true; inside++; }
                if (inside == 0) return;
                foreach (var s in Out)
                {
                    s.Bounds(out var a, out var b);
                    int i0 = math.max(0, (int)((a.x - lo.x) / cell)), i1 = math.min(nx - 1, (int)((b.x - lo.x) / cell));
                    int j0 = math.max(0, (int)((a.y - lo.y) / cell)), j1 = math.min(nz - 1, (int)((b.y - lo.y) / cell));
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            var p = lo + new float2(i + 0.5f, j + 0.5f) * cell;
                            if (Covers(s, p)) strength[j * nx + i] = math.max(strength[j * nx + i], s.Strength);
                        }
                }
                int touched = 0, strong = 0;
                for (int i = 0; i < strength.Length; i++)
                {
                    if (!isIn[i]) continue;
                    if (strength[i] > 0.05f) touched++;
                    if (strength[i] >= 0.6f) strong++;
                }
                touchedShare = touched / (float)inside;
                strongShare = strong / (float)inside;
            }

            /// <summary>The stroke's plain footprint (no warp) covers p: for the budget.</summary>
            internal static bool Covers(DecalStroke s, float2 p)
            {
                switch (s.Shape)
                {
                    case StrokeShape.Box:
                    {
                        float c = math.cos(s.Yaw), sn = math.sin(s.Yaw);
                        var d = p - s.Center;
                        var q = new float2(d.x * c - d.y * sn, d.x * sn + d.y * c) / math.max(new float2(0.05f), s.Size * 0.5f);
                        return math.lengthsq(q) <= 1f;
                    }
                    case StrokeShape.Path:
                        for (int i = 0; i + 1 < s.Points.Length; i++)
                        {
                            float w = 0.5f * (i < s.Widths.Length ? s.Widths[i] : 1f);
                            if (SegDist(p, s.Points[i], s.Points[i + 1]) <= w) return true;
                        }
                        return false;
                    default:
                        return s.Points.Length >= 3 && Poly.Contains(s.Points, p);
                }
            }
        }
    }
}
