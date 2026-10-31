// Placemaker — dynamic decals: the ground of a weathered type, designed top to bottom from its causes (docs/DECALS-PLAYBOOK.md,
// docs/runbooks/sportsPark.md). GPL-3.0.
//
// After the whole plan is composed and graded, this pass reads what was built and lays the shared library's pieces with
// brushes (Brushes.cs), in the playbook's order:
//   tone       faint big patches over every lawn, pitch, paving and asphalt (no swath untouched), the lawn mottled and
//              drier toward the open edges
//   trees      leaves of each tree's kind under and downwind of the crowns (carpets where crowns crowd), woodland floor
//              under groups, drifts piled against the first kerb, fence or court edge downwind, a leaf pile or two
//   seams      every boundary the plan knows: path edges worn, kerb grime on paving and car park edges, grime along court
//              lines, wear under fences
//   play       goalmouths worn bald, the crease muddy, the centre spot and the spine, the linesmen's tracks, the turf in
//              front of stands and dugouts, leaves and grit in the downwind court corners, clay spilling off its courts
//   feet       mud at the gates and prints fading along the path, shortcuts trodden across the grass, bare ground and
//              litter at the busiest benches
//   water      puddles filling the real low points of the built ground (paths and car park), the deepest wet, the rest
//              tidemarks and damp
//   wheels     the car park's tone, tyre tracks along the aisles, oil under the busy stalls, cracks along its edges
//   objects    rust at the feet of masts and lamps
// One or two "signature flaws" per site are exaggerated. The player's Grime amount scales every cause (not the tone).
// Deterministic from the step's seed. v1 strokes (per-build textures) are no longer made here; old saves still draw theirs.

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
        /// <summary>Most library instances one weathered build places.</summary>
        public const int MaxInstances = 9000;
        /// <summary>Library instances a hectare may have before the ground is thinned (a 220 x 200 m park lays about 270).</summary>
        public const float PerHectare = 320f;
        /// <summary>The leaves' colours (the user, 2026-10-29: autumn leaves under green trees looked wrong): "Summer" kinds are olive,
        /// green and brown; "" is the autumn library. A later knob (the city's season).</summary>
        public static string Season = "Summer";

        /// <summary>The types (and their styles) that get a designed ground: every sports park style.</summary>
        public static bool Wants(string layerId, SettingsBag values) => layerId == "sportsPark" || NatureGround.Wants(layerId) || PlazaGround.Handles(layerId) || layerId != null && Designs.ContainsKey(layerId);

        /// <summary>
        /// (makeover, 2026-10-29) Per-type ground designs run on top of the shared causes above (tone, trees, seams, feet,
        /// water, objects): a type listed here gets the whole Scene and then its own pass (parks: ParkGround.cs).
        /// </summary>
        internal static readonly Dictionary<string, Action<Scene>> Designs = FarmGround.Register(ParkGround.Register(new Dictionary<string, Action<Scene>>(StringComparer.Ordinal)));

        public static float Amount(SettingsBag values)
        {
            double v = values == null ? 100 : values.Num(GrimeId, 100);
            if (double.IsNaN(v)) v = 100;
            return (float)Math.Max(0, Math.Min(300, v)) / 100f;
        }

        public sealed class Report
        {
            public readonly Dictionary<string, int> ByCause = new Dictionary<string, int>(StringComparer.Ordinal);
            /// <summary>Library instances placed.</summary>
            public int Instances;
            /// <summary>Share of the site any cause (not the tone) touches, and where one is strong (opacity ≥ 0.6).</summary>
            public float Touched, Strong;
            public readonly List<string> Flaws = new List<string>();
            public double Ms;
            /// <summary>How much of everything was laid (1 = all; below 1 a huge site was thinned to its budget).</summary>
            public float Density = 1f;
            /// <summary>Milliseconds per pass (tone, trees, seams …), the last run.</summary>
            public readonly Dictionary<string, double> Timing = new Dictionary<string, double>(StringComparer.Ordinal);

            public override string ToString()
            {
                var parts = new List<string>();
                foreach (var kv in ByCause) parts.Add(kv.Key + " " + kv.Value);
                return $"{Instances} library instances at {Density * 100f:F0} % density ({string.Join(", ", parts)}), {Touched * 100f:F0} % of the site touched by causes, {Strong * 100f:F1} % strong, flaws [{string.Join(", ", Flaws)}], {Ms:F0} ms ({string.Join(", ", System.Linq.Enumerable.Select(Timing, kv => kv.Key + " " + kv.Value.ToString("F0")))})";
            }
        }

        /// <summary>Runs over a composed, graded plan (after SiteHarmony and SiteSafety). Instances go into the host layer's plan.</summary>
        public static Report Apply(ComposedPlan plan, AreaShape area, SiteContext site, IReadOnlyDictionary<AreaPlan, DressSettings> dress, DecalLibrary library = null)
        {
            var total = new Report();
            if (plan?.Layers == null || dress == null) return total;
            var lib = library ?? DecalLibrary.EnsureCurrent();
            if (lib == null || lib.Count == 0) return total;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var groups = new Dictionary<DressSettings, List<AreaPlan>>();
            var order = new List<DressSettings>();
            foreach (var l in plan.Layers)
            {
                if (l == null || !dress.TryGetValue(l, out var s) || s == null || !s.Weathering) continue;
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
                // the budget follows the site's size: about PerHectare a hectare, never more than MaxInstances; a huge site is
                // thinned evenly (every cause alike) from the start, and once more if it still came out over
                float ha = Poly.Area(ring) / 10000f;
                // (makeover) a type with its own design lays more causes on top of the shared ones: its own budget a hectare
                float perHa = s.LayerId != null && Designs.ContainsKey(s.LayerId) ? ParkGround.PerHectare : PerHectare;
                int cap = (int)math.clamp(ha * perHa, 150f, MaxInstances);
                if (PlazaGround.Handles(s.LayerId)) cap = (int)math.min(MaxInstances, cap * PlazaGround.BudgetFactor);   // a square is all detail
                float density = math.min(1f, cap / math.max(1f, ha * 300f));
                int start = host.DecalInstances.Count;
                Scene scene = null; Report r = null;
                for (int pass = 0; pass < 3; pass++)
                {
                    if (NatureGround.Wants(s.LayerId))
                    {
                        // (nature makeover) wild ground has its own causes: flow, hollows, rock, trails, water (NatureGround.cs)
                        r = new NatureGround.Design(layers, ring, site, ground, Hashing.Mix(host.Seed == 0 ? 1u : host.Seed, 0xD1Bu), s.Grime, s.LayerId, lib, host.DecalInstances, density, cap).Run();
                        if (r.Instances <= cap || pass == 2) break;
                        host.DecalInstances.RemoveRange(start, host.DecalInstances.Count - start);
                        density *= cap / (float)r.Instances * 0.95f;
                        continue;
                    }
                    // (plazas makeover) paved squares have their own design; the Wear choice is their setting
                    if (PlazaGround.Handles(s.LayerId)) r = new PlazaGround(layers, ring, site, ground, Hashing.Mix(host.Seed == 0 ? 1u : host.Seed, 0xD1Au), s.Grime * PlazaGround.StyleFactor(s.Style), lib, host.DecalInstances, density, cap).Run();
                    else
                    {
                        scene = new Scene(layers, ring, site, ground, Hashing.Mix(host.Seed == 0 ? 1u : host.Seed, 0xD1Au), s.Grime, lib, host.DecalInstances, density, cap)
                        {
                            // the painted lawn's moisture (GroundWear.LawnTint): dry-grass and clover decals land where the paint is dry or lush
                            PaintSeed = s.LawnPainted ? GroundWear.PaintSeed(host.Seed) : 0u,
                            Design = Designs.TryGetValue(s.LayerId ?? "", out var design) ? design : null,
                            Host = host,
                        };
                        if (scene.Design != null) ParkGround.Prepare(scene);
                        r = scene.Run();
                    }
                    if (r.Instances <= cap || pass == 2) break;
                    host.DecalInstances.RemoveRange(start, host.DecalInstances.Count - start);
                    density *= cap / (float)r.Instances * 0.95f;
                }
                r.Density = density;   // large sites are always handled, quietly (the user: warn only when a site is too small)
                total.Instances += r.Instances;
                foreach (var kv in r.ByCause) total.ByCause[kv.Key] = (total.ByCause.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                foreach (var kv in r.Timing) total.Timing[kv.Key] = (total.Timing.TryGetValue(kv.Key, out var tm) ? tm : 0) + kv.Value;
                total.Flaws.AddRange(r.Flaws);
                total.Touched = math.max(total.Touched, r.Touched);
                total.Strong = math.max(total.Strong, r.Strong);
                total.Density = math.min(total.Density, density);
                host.Facts["decalInstances"] = r.Instances;
                host.Facts["decalDensityPct"] = Math.Round(density * 100f);
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

        internal struct TreeAt { public float2 P; public float Crown; public string Leaves; }

        internal sealed class Scene
        {
            private readonly List<AreaPlan> _layers;
            private readonly float2[] _ring;
            private readonly SiteContext _site;
            private readonly Func<float2, float> _ground;
            private readonly uint _seed;
            private readonly float _amount;
            private readonly Report _r = new Report();
            private readonly BrushContext _b;
            public List<DecalInstance> Out => _b.Out;

            public readonly List<PathLine> Paths = new List<PathLine>(), Aisles = new List<PathLine>(), Fences = new List<PathLine>();
            public readonly List<TreeAt> Trees = new List<TreeAt>();
            public readonly List<Thing> Goals = new List<Thing>(), Stands = new List<Thing>(), Benches = new List<Thing>(), Steel = new List<Thing>(), Bays = new List<Thing>(), Bins = new List<Thing>();
            public readonly List<float2[]> Asphalt = new List<float2[]>(), Courts = new List<float2[]>(), Clay = new List<float2[]>(), Hard = new List<float2[]>(),
                Pitches = new List<float2[]>(), Turf = new List<float2[]>(), Sand = new List<float2[]>(), All = new List<float2[]>();
            public readonly List<float2> Gates = new List<float2>();
            private float _pitchWidth;
            private BrushArea _lawn;
            private PointGrid _treeGrid;
            /// <summary>The paint's seed when the lawn is painted (0: no painted moisture to follow).</summary>
            public uint PaintSeed;
            /// <summary>(makeover) The type's own ground design, run after the shared causes (null: the sports park's alone).</summary>
            public Action<Scene> Design;
            /// <summary>The plan the instances go to (its Facts say what the design needs to know).</summary>
            public AreaPlan Host;
            /// <summary>(makeover) Leaf cover of the trees pass (1 = the sports park's) and a leaf kind laid under every broadleaf instead
            /// of its species' own (null: the species' kind). Set by a design's Prepare (Designs.Prepare).</summary>
            public float LeafScale = 1f;
            public string LeafOverride;
            // what a type's design (Designs) reads and lays with
            internal BrushContext Brush => _b;
            internal BrushArea Lawn => _lawn;
            internal IReadOnlyList<AreaPlan> Layers => _layers;
            internal float2[] Outline => _ring;
            internal float GrimeAmount => _amount;
            internal uint Seed => _seed;
            internal float GroundAt(float2 p) => _ground(p);
            internal float DrynessAt(float2 p) => Dryness(p);
            internal string LeafKind(string kind) => Leaf(kind);
            internal float2 Opacity(float lo, float hi) => Op(lo, hi);

            public Scene(List<AreaPlan> layers, float2[] ring, SiteContext site, Func<float2, float> ground, uint seed, float amount, DecalLibrary lib, List<DecalInstance> into, float density = 1f, int cap = MaxInstances)
            {
                _layers = layers; _ring = Poly.EnsureCcw((float2[])ring.Clone()); _site = site; _ground = ground; _seed = seed; _amount = amount;
                // the hard stop sits well above the cap: the pass is re-run thinner if it comes out over, never cut short
                _b = new BrushContext(lib, seed, into) { Budget = (into?.Count ?? 0) + (int)(cap * 1.6f) + 200, Density = density };
                Read();
            }

            private uint H(int a, int b = 0) => Hashing.Mix(_seed, a, b);
            private float U(int a, int b = 0) => Hashing.Unit(H(a, b));
            private float Range(float lo, float hi, int a, int b = 0) => math.lerp(lo, hi, U(a, b));
            /// <summary>Coverage and opacity of the causes follow the player's Grime amount; the tone does not.</summary>
            private float A => math.min(2.2f, _amount);
            /// <summary>The season's version of a leaf kind when the library has it.</summary>
            private PointGrid _leafGrid;
            private string LeafOnPath(float2 p)
            {
                if (_leafGrid == null) { _leafGrid = new PointGrid(16f); for (int i = 0; i < Trees.Count; i++) _leafGrid.Add(Trees[i].P, i); }
                foreach (int i in _leafGrid.Near(p, 1))
                {
                    var t = Trees[i];
                    if (t.Leaves == null || math.distancesq(t.P, p) > t.Crown * t.Crown * 1.6f) continue;
                    return Leaf(t.Leaves);
                }
                return null;
            }

            private string Leaf(string kind) => kind != null && _b.Lib.Has(kind + Season) ? kind + Season : kind;
            private float2 Op(float lo, float hi) => new float2(math.saturate(lo * math.min(1.4f, 0.55f + 0.45f * _amount)), math.saturate(hi * math.min(1.3f, 0.6f + 0.4f * _amount)));

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

            private static string LeavesOf(PlannedObject o)
            {
                string id = ((o.Asset.Id ?? "") + " " + (o.Asset.Fallbacks != null && o.Asset.Fallbacks.Length > 0 ? o.Asset.Fallbacks[0] : "") + " " + (o.Category ?? "")).ToLowerInvariant();
                if (DressingWorld.IsConifer(o)) return "pineNeedles";
                if (id.Contains("birch") || id.Contains("alder") || id.Contains("poplar")) return "leafBirch";
                if (id.Contains("oak")) return "leafOak";
                if (id.Contains("plane") || id.Contains("linden")) return "leafPlane";
                if (id.Contains("chestnut") || id.Contains("hickory") || id.Contains("maple")) return "leafMaple";
                if (id.Contains("apple") || id.Contains("cherry") || id.Contains("blossom")) return "blossomPetals";
                if (id.Contains("palm")) return null;
                return "leafMixed";
            }

            private void Read()
            {
                foreach (var l in _layers)
                {
                    bool court = l.GeneratorId == "tennisCourts" || l.GeneratorId == "basketballCourt" || l.GeneratorId == "basketball" || l.GeneratorId == "skateBowl" || l.GeneratorId == "volleyball";
                    bool pitch = l.GeneratorId == "pitch" || l.GeneratorId == "runningTrack";
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
                        var r2 = Poly.EnsureCcw(Poly.To2D(s.Ring));
                        float a = Poly.Area(r2);
                        if (a < 3f) continue;
                        All.Add(r2);
                        string cat = (s.Category ?? "") + " " + (s.Asset.Id ?? "");
                        bool clay = cat.IndexOf("dirt", StringComparison.OrdinalIgnoreCase) >= 0 || cat.IndexOf("natural", StringComparison.OrdinalIgnoreCase) >= 0 || cat.IndexOf("clay", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool sand = cat.IndexOf("sand", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (s.Role == SurfaceRole.Grass) { Turf.Add(r2); if (pitch && a > 800f) Pitches.Add(r2); }
                        else if (court && a > 120f) { Courts.Add(r2); if (clay) Clay.Add(r2); }
                        else if (sand) Sand.Add(r2);
                        else if (s.Role == SurfaceRole.Asphalt) Asphalt.Add(r2);
                        else if (s.Role == SurfaceRole.Paving || s.Role == SurfaceRole.Plaza || s.Role == SurfaceRole.Decorative) Hard.Add(r2);
                        else if (s.Role == SurfaceRole.Ground && clay) Clay.Add(r2);
                    }
                    foreach (var o in l.Objects)
                    {
                        if (o == null) continue;
                        string id = o.Asset.Id ?? "";
                        var t = new Thing { P = o.Position.xz, Yaw = o.Yaw, Id = id };
                        if (o.Role == ObjectRole.Tree) Trees.Add(new TreeAt { P = o.Position.xz, Crown = DressingWorld.CrownOf(o, _site), Leaves = LeavesOf(o) });
                        else if (id.StartsWith("SportParkFootballGoal", StringComparison.Ordinal) || id.StartsWith("FootballGoal", StringComparison.Ordinal)) Goals.Add(t);
                        else if (id.StartsWith("SportParkStand", StringComparison.Ordinal) || id.StartsWith("SportParkBench", StringComparison.Ordinal)) Stands.Add(t);
                        else if (o.Role == ObjectRole.Bench) Benches.Add(t);
                        else if (o.Role == ObjectRole.Bin) Bins.Add(t);
                        else if (o.Role == ObjectRole.ParkingBay || o.Category == AssetCategories.ParkingDecal) Bays.Add(t);
                        if (o.Role == ObjectRole.Lamp || id.StartsWith("Lightpole", StringComparison.Ordinal)) Steel.Add(t);
                    }
                }
                // Gates: path ends at the outline (the street side), where people come in.
                foreach (var p in Paths)
                    foreach (var e in new[] { p.P[0], p.P[p.P.Length - 1] })
                        if (Poly.DistanceToBoundary(_ring, e) < 4f && !Gates.Exists(g => math.distancesq(g, e) < 36f)) Gates.Add(e);
                // the lawn: the site minus every surface
                _lawn = BrushArea.Of(_ring, All);
            }

            private bool Inside(float2 p, float inset = 0f) => Poly.Contains(_ring, p) && (inset <= 0f || Poly.DistanceToBoundary(_ring, p) >= inset);

            public Report Run()
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                int before = Out.Count;
                var flaws = PickFlaws();
                var lap = System.Diagnostics.Stopwatch.StartNew();
                void Lap(string what) { _r.Timing[what] = lap.Elapsed.TotalMilliseconds; lap.Restart(); }
                Tone(); Lap("tone");
                if (_amount > 0f)
                {
                    Trees_(flaws); Lap("trees");
                    Seams(); Lap("seams");
                    Play(flaws); Lap("play");
                    Feet(flaws); Lap("feet");
                    Water(flaws); Lap("water");
                    Wheels(); Lap("wheels");
                    Objects(); Lap("objects");
                }
                if (Design != null) { Design(this); Lap("design"); }
                Measure(out _r.Touched, out _r.Strong); Lap("measure");
                foreach (var kv in _b.ByCause) _r.ByCause[kv.Key] = kv.Value;
                _r.Flaws.AddRange(flaws);
                _r.Instances = Out.Count - before;
                _r.Ms = clock.Elapsed.TotalMilliseconds;
                return _r;
            }

            private List<string> PickFlaws()
            {
                // one or two things this site is known for
                var all = new List<string> { "baldGoalmouth", "bigPuddle", "leafChokedCorner", "shortcut" };
                if (Design != null && Goals.Count == 0) all.Remove("baldGoalmouth");   // (makeover) a park without goals
                var pick = new List<string>();
                int first = (int)(U(901) * all.Count) % all.Count;
                pick.Add(all[first]);
                if (U(902) < 0.6f) pick.Add(all[(first + 1 + (int)(U(903) * (all.Count - 1))) % all.Count]);
                return pick;
            }

            // ───────────── tone: no swath untouched ─────────────

            private void Tone()
            {
                // the lawn: big faint patches everywhere, mottled from above, drier toward the open edges and away from trees
                Brushes.Tone(_b, _lawn, "lawnTonePatch", new float2(0.18f, 0.32f), 0.2f, ColourLevel.Quiet, "tone:lawn");
                Brushes.Scatter(_b, _lawn, "groundMottle", 0.16f, new ScatterOptions { Weight = p => 0.35f + 0.65f * ValueNoise.Clumps(p, 30f, H(1)), Opacity = new float2(0.25f, 0.45f), Clump = 2.5f, Spill = 3f, Level = ColourLevel.Quiet, Cause = "tone:mottle" });
                Brushes.Scatter(_b, _lawn, "dryGrass", 0.12f, new ScatterOptions { Weight = Dryness, Opacity = new float2(0.3f, 0.55f), Clump = 3f, Spill = 2f, Cause = "tone:dry" });
                Brushes.Scatter(_b, _lawn, "cloverPatch", 0.05f, new ScatterOptions { Weight = p => 1f - Dryness(p), Opacity = new float2(0.4f, 0.7f), Clump = 3f, Loners = 0.2f, Cause = "tone:clover" });
                // the pitches and turf: mowing stripes and fine clippings, no tone patches (the user, 2026-10-29: the big soft tone
                // on the turf looked smudgy, "a waste of decals"; groundMottle there read as yellow blotches)
                foreach (var t in Turf)
                {
                    // stripes only on a real, bounded pitch (football, rugby, cricket square …), never on open turf or a huge
                    // field: a mowing row has its own length and width, and big areas starved the rest of the budget (the user)
                    if (Pitches.Contains(t) && StripePitch(t)) Stripes(t);
                    Brushes.Scatter(_b, BrushArea.Of(t), "grassClippings", 0.12f, new ScatterOptions { Opacity = new float2(0.45f, 0.8f), Clump = 2f, Loners = 0.5f, Level = ColourLevel.Quiet, Cause = "tone:clippings" });
                }
                foreach (var h in Hard) Brushes.Tone(_b, BrushArea.Of(h), "pavingTonePatch", new float2(0.3f, 0.5f), 0.3f, ColourLevel.Quiet, "tone:paving");
                foreach (var h in Asphalt) Brushes.Tone(_b, BrushArea.Of(h), "asphaltTonePatch", new float2(0.45f, 0.7f), 0.35f, ColourLevel.Quiet, "tone:asphalt");
                foreach (var c in Courts)
                {
                    bool clay = Clay.Contains(c);
                    Brushes.Tone(_b, BrushArea.Of(c), clay ? "dirtTonePatch" : "asphaltTonePatch", new float2(0.35f, 0.6f), 0.35f, ColourLevel.Quiet, "tone:court");
                }
            }

            /// <summary>Drier where the painted lawn is dry, toward the site's open edges and away from shade (0..1).</summary>
            private float Dryness(float2 p)
            {
                float edge = 1f - math.saturate(Poly.DistanceToBoundary(_ring, p) / 25f);
                float shade = 0f;
                if (_treeGrid == null) { _treeGrid = new PointGrid(20f); for (int i = 0; i < Trees.Count; i++) _treeGrid.Add(Trees[i].P, i); }
                foreach (int i in _treeGrid.Near(p, 1)) { var t = Trees[i]; float d = math.distance(t.P, p); if (d < t.Crown * 2.5f) { shade = math.max(shade, 1f - d / (t.Crown * 2.5f)); if (shade > 0.9f) break; } }
                // the same moisture the paint used (GroundWear.LawnMoisture: its dry paint starts at 0.40, its lush at 0.62)
                float dry = PaintSeed != 0u ? math.saturate((0.58f - GroundWear.LawnMoisture(PaintSeed, p)) / 0.22f) : ValueNoise.Clumps(p, 35f, H(2));
                return math.saturate((0.15f + 0.35f * edge + 0.7f * dry - 0.15f) * (1f - 0.8f * shade));
            }

            /// <summary>A pitch with real bounds: roughly rectangular, at most 130 x 95 m (a full-size pitch with its run-off).</summary>
            private static bool StripePitch(float2[] t)
            {
                var axis = LongAxis(t); var side = new float2(-axis.y, axis.x); var c = Poly.Centroid(t);
                float hl = 0f, hw = 0f;
                foreach (var q in t) { hl = math.max(hl, math.abs(math.dot(q - c, axis))); hw = math.max(hw, math.abs(math.dot(q - c, side))); }
                return hl * 2f <= 130f && hw * 2f <= 95f && Poly.Area(t) >= 0.85f * (hl * 2f) * (hw * 2f);
            }

            /// <summary>Mowing stripes along the pitch's long side: strips laid end to end across it.</summary>
            private void Stripes(float2[] turf)
            {
                var k = _b.Lib.Get("mowingStripes");
                if (k == null) return;
                Poly.Bounds(turf, out var lo, out var hi);
                // the long axis of the turf's best box
                var axis = LongAxis(turf);
                var side = new float2(-axis.y, axis.x);
                var c = Poly.Centroid(turf);
                float halfL = 0f, halfW = 0f;
                foreach (var q in turf) { halfL = math.max(halfL, math.abs(math.dot(q - c, axis))); halfW = math.max(halfW, math.abs(math.dot(q - c, side))); }
                // rows exactly one stripe pair wide, side by side (the bands carry on across the rows); one run a row, end to end:
                // a run fades at its ends, so short runs broke every band (the user, 2026-10-30)
                float width = DecalVariants.FootprintOf(k, 0).y;
                int rows = math.max(1, (int)math.floor(halfW * 2f / width));
                // never thinned: a huge or busy site drops whole strip pieces, and a mowing row with a piece missing reads broken
                float keep = _b.Density; _b.Density = 1f;
                for (int i = 0; i < rows; i++)
                {
                    float v = (i - (rows - 1) * 0.5f) * width;
                    var a = c + side * v - axis * (halfL + 1.5f); var b = c + side * v + axis * (halfL + 1.5f);
                    Brushes.EdgeStrip(_b, new[] { a, b }, "mowingStripes", new StripOptions { Gaps = 0f, Run = new float2(1000f, 1000f), Opacity = new float2(0.9f, 0.9f), Width = new float2(1f, 1f), Level = ColourLevel.Quiet, Cause = "tone:stripes" });
                }
                _b.Density = keep;
            }

            private static float2 LongAxis(float2[] ring)
            {
                float best = 0f; float2 axis = new float2(1f, 0f);
                for (int i = 0; i < ring.Length; i++)
                {
                    var e = ring[(i + 1) % ring.Length] - ring[i];
                    float l = math.length(e);
                    if (l > best) { best = l; axis = e / l; }
                }
                return axis;
            }

            // ───────────── trees: leaves of each kind, downwind, piled against barriers ─────────────

            private void Trees_(List<string> flaws)
            {
                if (Trees.Count == 0) return;
                bool choked = flaws.Contains("leafChokedCorner");
                // the ground leaves fall on: the lawn and the hard ground (they blow onto paths and courts too), never the pitch
                var fall = BrushArea.Of(_ring, Pitches);
                var bySpecies = new Dictionary<string, List<(float2, float)>>(StringComparer.Ordinal);
                foreach (var t in Trees)
                {
                    if (t.Leaves == null) continue;
                    if (!bySpecies.TryGetValue(t.Leaves, out var l)) bySpecies[t.Leaves] = l = new List<(float2, float)>();
                    l.Add((t.P, t.Crown));
                }
                foreach (var kv in bySpecies)
                {
                    string kind = Leaf(kv.Key);
                    // (makeover) a design may lay its own summer litter for broadleaves (parks: darker, sparser) and fewer of them
                    bool broad = kind != "pineNeedles" && kind != "blossomPetals";
                    if (broad && LeafOverride != null && _b.Lib.Has(LeafOverride)) kind = LeafOverride;
                    // smothered, never a blotch: overlapping patches over the whole fall zone, heaviest where crowns crowd
                    float cov = (kind == "pineNeedles" ? 0.55f : kind == "blossomPetals" ? 0.4f : 0.85f) * A * LeafScale;
                    Brushes.Under(_b, kv.Value, kind, cov, Wind, fall, new ScatterOptions { Opacity = Op(0.55f, 1f), Scale = new float2(0.9f, 1.45f), Clump = 5f, Spread = 0.55f, Loners = 0.25f, Spill = 2f, Cause = "leaves" });
                    // mixed leaves blown further on, thinner
                    if (kind != "pineNeedles")
                        Brushes.Under(_b, Shift(kv.Value, Wind, 1.8f), broad && LeafOverride != null && _b.Lib.Has(LeafOverride) ? LeafOverride : Leaf("leafMixed"), 0.2f * A * LeafScale, Wind, fall, new ScatterOptions { Opacity = Op(0.4f, 0.8f), Clump = 2.5f, Loners = 0.45f, Level = ColourLevel.Quiet, Cause = "leaves:blown" });
                }
                // under groups of trees: woodland floor and a little bare earth, overlapping the leaves
                var groups = TreeGroups(9f);
                int gi = 0;
                foreach (var g in groups)
                {
                    if (g.Count < 3) continue;
                    var crowns = new List<(float2, float)>();
                    foreach (var t in g) crowns.Add((t.P, t.Crown * 0.9f));
                    // most groves smother their ground in last year's leaves, some are left grassy (the user, 2026-10-30: "more
                    // actual brown mulch that smothers under trees or in certain areas, but not in others")
                    if (U(520, gi++) < 0.7f)
                        Brushes.Under(_b, crowns, "leafLitterDeep", 2.8f * A, Wind * 0.3f, _lawn, new ScatterOptions { Opacity = Op(0.75f, 1f), Scale = new float2(0.9f, 1.3f), Clump = 4f, Spread = 0.5f, Loners = 0.15f, Level = ColourLevel.Quiet, Cause = "woodland:litter" });
                    Brushes.Under(_b, crowns, "woodlandFloor", 0.22f * A, Wind * 0.3f, _lawn, new ScatterOptions { Opacity = Op(0.35f, 0.65f), Clump = 3f, Cause = "woodland" });
                    Brushes.Under(_b, crowns, "barePatch", 0.08f * A, Wind * 0.2f, _lawn, new ScatterOptions { Opacity = Op(0.3f, 0.6f), Clump = 2f, Loners = 0.5f, Cause = "woodland:bare" });
                }
                // drifts: leaves blown downwind pile against the first barrier
                Drifts(choked);
            }

            private static List<(float2, float)> Shift(List<(float2 p, float r)> c, float2 wind, float k)
            {
                var o = new List<(float2, float)>(c.Count);
                foreach (var x in c) o.Add((x.p + wind * x.r * k, x.r * 1.2f));
                return o;
            }

            private List<List<TreeAt>> TreeGroups(float link)
            {
                var res = new List<List<TreeAt>>();
                var done = new bool[Trees.Count];
                var grid = new PointGrid(link);
                for (int i = 0; i < Trees.Count; i++) grid.Add(Trees[i].P, i);
                for (int i = 0; i < Trees.Count; i++)
                {
                    if (done[i]) continue;
                    done[i] = true;
                    var g = new List<TreeAt> { Trees[i] };
                    var q = new Queue<int>(); q.Enqueue(i);
                    while (q.Count > 0)
                    {
                        int a = q.Dequeue();
                        foreach (int j in grid.Near(Trees[a].P, 1))
                            if (!done[j] && math.distancesq(Trees[a].P, Trees[j].P) < link * link) { done[j] = true; g.Add(Trees[j]); q.Enqueue(j); }
                    }
                    res.Add(g);
                }
                return res;
            }

            private struct Barrier { public float2 A, B; public int Group; }

            private void Drifts(bool choked)
            {
                var bars = new List<Barrier>();
                int gid = 0;
                foreach (var l in Paths)
                    for (int sideSign = -1; sideSign <= 1; sideSign += 2, gid++)
                        for (int i = 0; i + 1 < l.P.Length; i++)
                        {
                            var d = math.normalizesafe(l.P[i + 1] - l.P[i]);
                            var nrm = new float2(-d.y, d.x) * sideSign * l.Half;
                            bars.Add(new Barrier { A = l.P[i] + nrm, B = l.P[i + 1] + nrm, Group = gid });
                        }
                foreach (var set in new[] { Asphalt, Courts, Hard })
                    foreach (var r in set)
                    {
                        for (int i = 0; i < r.Length; i++) bars.Add(new Barrier { A = r[i], B = r[(i + 1) % r.Length], Group = gid });
                        gid++;
                    }
                foreach (var f in Fences) { for (int i = 0; i + 1 < f.P.Length; i++) bars.Add(new Barrier { A = f.P[i], B = f.P[i + 1], Group = gid }); gid++; }
                if (bars.Count == 0) return;
                // every barrier in the cells it crosses (a ray only tests the barriers near it: thousands of trees on a big site)
                const float cell = 16f;
                var barGrid = new PointGrid(cell);
                for (int b = 0; b < bars.Count; b++)
                {
                    float len = math.distance(bars[b].A, bars[b].B);
                    int steps = math.max(1, (int)math.ceil(len / (cell * 0.5f)));
                    long lastKey = long.MinValue;
                    for (int k = 0; k <= steps; k++)
                    {
                        var q = math.lerp(bars[b].A, bars[b].B, k / (float)steps);
                        long key = ((long)math.floor(q.x / cell) << 32) ^ (uint)(int)math.floor(q.y / cell);
                        if (key == lastKey) continue;
                        lastKey = key;
                        barGrid.Add(q, b);
                    }
                }
                var seenBar = new int[bars.Count];
                int stamp = 0;
                var hits = new List<(int bar, float2 p, float w)>();
                var across = new float2(-Wind.y, Wind.x);
                for (int t = 0; t < Trees.Count; t++)
                {
                    if (Trees[t].Leaves == null) continue;
                    for (int ray = 0; ray < 3; ray++)
                    {
                        var start = Trees[t].P + across * (ray - 1) * 2.5f;
                        var dir = math.normalizesafe(Wind + across * Range(-0.35f, 0.35f, 110 + ray, t));
                        var end = start + dir * 26f;
                        int best = -1; float bt = 2f; float2 bp = default;
                        stamp++;
                        for (int k = 0; k <= 4; k++)
                            foreach (int b in barGrid.Near(math.lerp(start, end, k / 4f), 1))
                            {
                                if (seenBar[b] == stamp) continue;
                                seenBar[b] = stamp;
                                if (Poly.SegmentIntersection(start, end, bars[b].A, bars[b].B, out var pt, out float ta, out _) && ta < bt && ta > 0.02f) { bt = ta; best = b; bp = pt; }
                            }
                        if (best >= 0) hits.Add((best, bp, (1f - bt * 0.7f) / 3f));
                    }
                }
                var used = new bool[hits.Count];
                var byGroup = new Dictionary<int, List<int>>();
                for (int j = 0; j < hits.Count; j++)
                {
                    int gj = bars[hits[j].bar].Group;
                    if (!byGroup.TryGetValue(gj, out var lj)) byGroup[gj] = lj = new List<int>();
                    lj.Add(j);
                }
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
                        foreach (int j in byGroup[g])
                        {
                            if (used[j]) continue;
                            foreach (int m in members)
                                if (math.distancesq(hits[m].p, hits[j].p) < 8f * 8f) { used[j] = true; members.Add(j); grew = true; break; }
                        }
                    }
                    float w = 0f;
                    foreach (int m in members) w += hits[m].w;
                    if (w < 0.3f) continue;
                    var bar = bars[hits[i].bar];
                    var tan = math.normalizesafe(bar.B - bar.A);
                    float2 o = hits[i].p;
                    members.Sort((x, y) => math.dot(hits[x].p - o, tan).CompareTo(math.dot(hits[y].p - o, tan)));
                    var line = new List<float2>();
                    foreach (int m in members)
                        if (line.Count == 0 || math.distance(line[line.Count - 1], hits[m].p) > 1.8f) line.Add(hits[m].p);
                    line.Insert(0, line[0] - tan * Range(3f, 6f, 118, drifts.Count));
                    line.Add(line[line.Count - 1] + tan * Range(3f, 6f, 119, drifts.Count));
                    var pts = new float2[line.Count];
                    for (int k = 0; k < line.Count; k++)
                    {
                        var tk = math.normalizesafe(line[math.min(line.Count - 1, k + 1)] - line[math.max(0, k - 1)], tan);
                        var nrm = new float2(-tk.y, tk.x);
                        if (math.dot(nrm, Wind) > 0f) nrm = -nrm;   // the upwind side
                        pts[k] = line[k] + nrm * 0.6f;
                    }
                    // the heavy side (left of travel) must face the barrier, which lies downwind
                    var dir = math.normalizesafe(pts[pts.Length - 1] - pts[0]);
                    if (math.dot(new float2(-dir.y, dir.x), Wind) < 0f) Array.Reverse(pts);
                    drifts.Add((pts, w));
                }
                drifts.Sort((a, b) => b.w.CompareTo(a.w));
                for (int i = 0; i < drifts.Count && i < 24; i++)
                {
                    var (pts, w) = drifts[i];
                    bool hero = choked && i == 0;
                    Brushes.EdgeStrip(_b, pts, Leaf("leafDriftStrip"), new StripOptions { Gaps = 0.15f, Run = new float2(10f, 30f), Opacity = Op(0.55f + math.min(0.3f, w * 0.15f), 1f), Width = hero ? new float2(1.3f, 1.6f) : new float2(0.8f, 1.15f), Cause = "leaves:drift" });
                    if (hero || (i < 3 && w > 1.2f))
                    {
                        var mid = pts[pts.Length / 2];
                        Brushes.At(_b, mid - Wind * 1.5f, -Wind, Leaf("leafPile"), hero ? 9f : 6f, Op(0.7f, 1f), hero ? 5 : 3, ColourLevel.Any, hero ? "flaw:leafChoked" : "leaves:pile");
                    }
                }
            }

            // ───────────── seams: every boundary the plan knows ─────────────

            private void Seams()
            {
                // the paths themselves: cracks, smudges, grit, puddles, leaves under the trees (never a strip redrawing them)
                foreach (var l in Paths)
                    if (!l.Drive) Brushes.PathWear(_b, l.P, l.Half, 1f, LeafOnPath, "seam:pathWear");
                // path edges: worn grass on the grass side (pathEdgeWear lies on terrain only: it never paints the path)
                foreach (var l in Paths)
                    for (int side = -1; side <= 1; side += 2)
                        Brushes.EdgeStrip(_b, l.P, "pathEdgeWear", new StripOptions { Side = -side, Offset = -(l.Half + 0.8f), Gaps = 0.5f, Run = new float2(10f, 30f), Opacity = Op(0.6f, 0.9f), Cause = "seam:path" });
                // kerb grime: inside paving and the car park, against their edges
                foreach (var set in new[] { Hard, Asphalt })
                    foreach (var r in set)
                        Brushes.EdgeStrip(_b, Ring(r), "kerbGrimeStrip", new StripOptions { Side = -1, Offset = -0.3f, Gaps = 0.55f, Closed = true, Opacity = Op(0.55f, 0.9f), Cause = "seam:kerb" });
                // courts: grime along the line where the court meets the grass, wear just outside it
                foreach (var c in Courts)
                {
                    Brushes.EdgeStrip(_b, Ring(c), "seamGrime", new StripOptions { Side = -1, Offset = -0.15f, Gaps = 0.2f, Closed = true, Opacity = Op(0.6f, 0.95f), Cause = "seam:court" });
                    Brushes.EdgeStrip(_b, Ring(c), "pathEdgeWear", new StripOptions { Side = 1, Offset = -1.1f, Gaps = 0.4f, Closed = true, Opacity = Op(0.45f, 0.8f), Cause = "seam:courtEdge" });
                }
                // (no strip round the turf: outside a pitch is often a running track or paving, where dry-grass wear read as pale haze)
                // paving: a few long cracks across each slab of paving, a crack web here and there (the user: more cracks)
                int pc = 0;
                foreach (var r in Hard)
                {
                    float area = Poly.Area(r);
                    if (area < 60f) continue;
                    var c = Poly.Centroid(r);
                    var axis = LongAxis(r); var side = new float2(-axis.y, axis.x);
                    float halfL = 0f, halfW = 0f;
                    foreach (var q in r) { halfL = math.max(halfL, math.abs(math.dot(q - c, axis))); halfW = math.max(halfW, math.abs(math.dot(q - c, side))); }
                    int lines = math.clamp((int)(halfL / 14f), 1, 6);
                    for (int i = 0; i < lines; i++, pc++)
                    {
                        if (U(330, pc) > 0.7f) continue;
                        float u = math.lerp(-halfL, halfL, (i + 0.3f + 0.4f * U(331, pc)) / lines);
                        var a = c + axis * u - side * (halfW - 0.6f); var b = c + axis * (u + Range(-3f, 3f, 332, pc)) + side * (halfW - 0.6f);
                        if (!Poly.Contains(r, a) || !Poly.Contains(r, b)) continue;
                        Brushes.CracksAlong(_b, new[] { a, b }, "crackConcrete", new StripOptions { Gaps = 0.35f, Run = new float2(6f, 20f), Opacity = Op(0.45f, 0.8f), Width = new float2(0.75f, 1f), Cause = "seam:pavingCrack" });
                    }
                    if (area > 300f) Brushes.Scatter(_b, BrushArea.Of(r), "crackWeb", 0.03f * A, new ScatterOptions { Opacity = Op(0.35f, 0.65f), Clump = 1.5f, Cause = "seam:pavingCrack" });
                }
                // grit and small stones kicked off the paths and paving onto the grass, thin and even (never one cluster)
                Brushes.Scatter(_b, _lawn, "pebbles", 0.03f * A, new ScatterOptions { Weight = NearHard, Opacity = Op(0.3f, 0.55f), Clump = 2f, Loners = 0.5f, Level = ColourLevel.Quiet, Cause = "seam:grit" });
                // fences: the strip of rough grass and grime the mower never reaches
                foreach (var f in Fences)
                    Brushes.EdgeStrip(_b, f.P, "fenceLine", new StripOptions { Side = U(300, f.P.Length) < 0.5f ? 1 : -1, Gaps = 0.4f, Run = new float2(16f, 45f), Opacity = Op(0.6f, 1f), Cause = "seam:fence" });
            }

            /// <summary>1 right beside a path or paving, fading to 0 by 6 m (where grit lands).</summary>
            private float NearHard(float2 p)
            {
                float d = 6f;
                foreach (var l in Paths) foreach (var q in l.P) { float x = math.distance(q, p) - l.Half; if (x < d) d = x; }
                foreach (var r in Hard) { float x = Poly.DistanceToBoundary(r, p); if (x < d) d = x; }
                return math.saturate(1f - d / 6f);
            }

            /// <summary>A CCW ring as a closed polyline (first point repeated).</summary>
            private static float2[] Ring(float2[] r)
            {
                var o = new float2[r.Length + 1];
                Array.Copy(r, o, r.Length);
                o[r.Length] = r[0];
                return o;
            }

            // ───────────── play: pitches, stands, courts ─────────────

            private readonly List<float2> PitchCentres = new List<float2>();

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
                // in front of the stands and dugouts: the turf people stand on, the busiest few
                var busy = new List<Thing>(Stands);
                busy.Sort((a, b) => NearestPitch(a.P).CompareTo(NearestPitch(b.P)));
                var taken = new List<float2>();
                foreach (var st in busy)
                {
                    if (taken.Count >= 8 || taken.Exists(q => math.distancesq(q, st.P) < 12f * 12f)) continue;
                    taken.Add(st.P);
                    var front = Front(st);
                    bool dugout = st.Id.StartsWith("SportParkBench", StringComparison.Ordinal);
                    var c = st.P + front * (dugout ? 2.4f : 9f);
                    var along = new float2(front.y, -front.x);
                    Brushes.At(_b, c, along, dugout ? "pebbles" : "barePatch", dugout ? 6f : 12f, Op(0.3f, 0.55f), 1, ColourLevel.Any, dugout ? "play:dugout" : "play:stand");
                }
                // courts: grit and leaves in the downwind corners, clay spilling off its courts, a few cracks on hard courts
                int n = 0;
                foreach (var r in Courts)
                {
                    var c = Poly.Centroid(r);
                    bool clay = Clay.Contains(r);
                    var corners = new List<(float2 p, float score)>();
                    for (int i = 0; i < r.Length; i++)
                    {
                        var prev = r[(i + r.Length - 1) % r.Length]; var next = r[(i + 1) % r.Length];
                        var d0 = math.normalizesafe(r[i] - prev); var d1 = math.normalizesafe(next - r[i]);
                        if (math.abs(math.dot(d0, d1)) > 0.5f) continue;
                        corners.Add((r[i], math.dot(math.normalizesafe(r[i] - c), Wind)));
                    }
                    corners.Sort((a, b) => b.score.CompareTo(a.score));
                    for (int k = 0; k < corners.Count && k < 2; k++)
                    {
                        if (corners[k].score < 0.05f) break;
                        var p = corners[k].p; var inward = math.normalizesafe(c - p);
                        Brushes.At(_b, p + inward * 2.2f, -inward, Trees.Count > 0 ? Leaf("leafPile") : "pebbles", k == 0 ? 6f : 4.5f, Op(0.55f, 0.95f), 2, ColourLevel.Any, "play:courtCorner");
                    }
                    if (clay)
                    {
                        // clay carried out onto the grass at the gates and kicked over the edges
                        Brushes.EdgeStrip(_b, Ring(r), "claySpillStrip", new StripOptions { Side = 1, Offset = -0.9f, Gaps = 0.45f, Closed = true, Opacity = Op(0.5f, 0.9f), Cause = "play:claySpill" });
                    }
                    else
                    {
                        // a hard court: one or two long cracks across, sealed or open
                        Poly.Bounds(r, out var lo, out var hi);
                        var axis = LongAxis(r);
                        var side = new float2(-axis.y, axis.x);
                        float halfW = 0f; foreach (var q in r) halfW = math.max(halfW, math.abs(math.dot(q - c, side)));
                        int cracks = U(320, n) < 0.5f ? 2 : 3;
                        for (int i = 0; i < cracks; i++)
                        {
                            float u = Range(-0.35f, 0.35f, 321 + i, n) * math.length(hi - lo) * 0.5f;
                            var a = c + axis * u - side * (halfW - 0.8f); var b = c + axis * (u + Range(-2f, 2f, 323 + i, n)) + side * (halfW - 0.8f);
                            Brushes.CracksAlong(_b, new[] { a, b }, U(325 + i, n) < 0.5f ? "crackSealed" : "crackAsphalt", new StripOptions { Gaps = 0.2f, Run = new float2(8f, 30f), Opacity = Op(0.5f, 0.85f), Width = new float2(0.8f, 1f), Cause = "play:courtCrack" });
                        }
                    }
                    n++;
                }
            }

            /// <summary>Where a stand or bench looks: toward the nearest pitch, else its own facing.</summary>
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
                bool bald = flaws.Contains("baldGoalmouth");
                int baldEnd = (int)(U(41, no) * 2f);
                for (int e = 0; e < 2; e++)
                {
                    var g = e == 0 ? g1 : g2;
                    var inward = e == 0 ? axis : -axis;
                    bool worst = bald && e == baldEnd;
                    float k = worst ? 1.35f : 1f;
                    // the goalmouth: wide and shallow, skewed off-centre (keepers dive one way more than the other)
                    var gm = g + inward * Range(4.5f, 6.5f, 42, e + no * 4) + side * Range(-1.5f, 1.5f, 43, e + no * 4);
                    // one bald area hugging the six-yard box, in a halo of thinned grass (game runner: scattered satellites read as spots);
                    // smaller and fainter after the true-colour fix (2026-10-30: the old sizes read as big tan blobs, run show-6-builds-1030-050913-1d67)
                    Brushes.At(_b, gm, side, "barePatch", Range(9f, 12f, 45, e) * k, Op(worst ? 0.3f : 0.2f, 0.34f), worst ? 2 : 1, ColourLevel.Any, worst ? "flaw:baldGoalmouth" : "play:goalmouth");
                    Brushes.At(_b, gm + inward * 3f, side, "dryGrass", Range(14f, 18f, 46, e) * k, Op(0.1f, 0.2f), 1, ColourLevel.Quiet, "play:goalmouthHalo");
                    Brushes.At(_b, gm, side, "pebbles", Range(7f, 9f, 47, e), Op(0.45f, 0.75f), 1, ColourLevel.Quiet, "play:goalmouthGrit");
                    // the crease: worn through to the soil, muddy after rain
                    var cr = g + inward * Range(1.6f, 2.6f, 48, e) + side * Range(-0.6f, 0.6f, 49, e);
                    Brushes.At(_b, cr, side, "barePatch", Range(5f, 7f, 51, e) * k, Op(0.22f, 0.34f), 0, ColourLevel.Any, "play:crease");
                    Brushes.At(_b, cr, side, "pebbles", Range(5f, 7f, 50, e), Op(0.45f, 0.75f), 0, ColourLevel.Quiet, "play:crease");
                    // the penalty spot and the arc a keeper's kicks churn
                    Brushes.At(_b, g + inward * Range(10f, 12f, 52, e), side, "barePatch", Range(4f, 6f, 53, e), Op(0.12f, 0.22f), 0, ColourLevel.Any, "play:spot");
                }
                // centre spot and the spine: midfield takes the most running
                Brushes.At(_b, centre + side * Range(-2f, 2f, 61, no), axis, "barePatch", Range(7f, 9f, 63, no), Op(0.14f, 0.24f), 1, ColourLevel.Any, "play:centre");
                var corridor = new[] { g1 + axis * 10f - side * 10f, g2 - axis * 10f - side * 10f, g2 - axis * 10f + side * 10f, g1 + axis * 10f + side * 10f };
                float half = len * 0.5f;
                // the spine: a continuous band of thinned grass down the middle (many faint overlapping pieces), bare only
                // here and there near midfield
                Func<float2, float> spine = p => { float u = math.abs(math.dot(p - centre, axis)) / half, v = math.abs(math.dot(p - centre, side)) / 10f; return math.saturate((1f - u * u) * (1f - v * 0.7f)); };
                Brushes.Scatter(_b, BrushArea.Of(corridor), "dryGrass", 0.8f * A, new ScatterOptions { Weight = spine, Opacity = Op(0.22f, 0.4f), Clump = 6f, Spread = 0.4f, Loners = 0.1f, Scale = new float2(0.8f, 1.2f), Level = ColourLevel.Quiet, Cause = "play:spine" });
                Brushes.Scatter(_b, BrushArea.Of(corridor), "barePatch", 0.1f * A, new ScatterOptions { Weight = p => spine(p) * spine(p), Opacity = Op(0.14f, 0.26f), Clump = 3f, Spread = 0.35f, Loners = 0f, Scale = new float2(0.6f, 0.9f), Cause = "play:spine" });
                // the linesmen: each runs one half, on opposite touchlines, just outside the line
                for (int e = 0; e < 2; e++)
                {
                    float sd = e == 0 ? 1f : -1f;
                    var from = centre + side * sd * (halfW + 1.4f) + axis * sd * 2f;
                    var to = centre + side * sd * (halfW + 1.4f) + axis * sd * (half - 12f);
                    if (!Inside(to) || !Inside(from)) continue;
                    Brushes.EdgeStrip(_b, new[] { from, math.lerp(from, to, 0.5f) + side * Range(-0.5f, 0.5f, 70, e), to }, "wornPath", new StripOptions { Gaps = 0.35f, Run = new float2(10f, 25f), Opacity = Op(0.45f, 0.8f), Width = new float2(0.6f, 0.8f), Cause = "play:linesman" });
                }
            }

            // ───────────── feet: gates, prints, shortcuts, benches ─────────────

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
                // mud and prints at every gate: people come in off the street here
                int n = 0;
                foreach (var g in Gates)
                {
                    var (line, idx, _) = NearestPath(g);
                    if (line == null) continue;
                    int dir = idx < line.P.Length / 2 ? 1 : -1;
                    var inward = math.normalizesafe(line.P[math.clamp(idx + dir * 3, 0, line.P.Length - 1)] - line.P[idx]);
                    var side = new float2(-inward.y, inward.x) * (U(400, n) < 0.5f ? 1f : -1f);
                    Brushes.At(_b, g + inward * 3f + side * (line.Half + 1.2f), inward, "barePatch", Range(5f, 7f, 401, n), Op(0.3f, 0.52f), 0, ColourLevel.Any, "feet:gate");
                    Brushes.At(_b, g + inward * 2f + side * (line.Half + 0.8f), inward, "pebbles", Range(4f, 6f, 403, n), Op(0.45f, 0.8f), 0, ColourLevel.Quiet, "feet:gate");
                    Brushes.At(_b, g + inward * 6f, inward, "footprintsMud", Range(7f, 10f, 402, n), Op(0.5f, 0.85f), 1, ColourLevel.Any, "feet:prints");
                    n++;
                }
                // mud where players step off the pitch onto a path
                foreach (var c in PitchCentres)
                {
                    var (line, idx, d) = NearestPath(c);
                    if (line == null || d > 60f) continue;
                    var at = line.P[idx];
                    var toPitch = math.normalizesafe(c - at);
                    Brushes.At(_b, at + toPitch * (line.Half + 1.5f), toPitch, "pebbles", Range(5f, 7f, 410, n), Op(0.4f, 0.7f), 1, ColourLevel.Any, "feet:offPitch");
                    Brushes.At(_b, at, math.normalizesafe(line.P[math.min(line.P.Length - 1, idx + 1)] - line.P[math.max(0, idx - 1)]), "footprintsMud", 9f, Op(0.45f, 0.8f), 1, ColourLevel.Any, "feet:prints");
                    n++;
                }
                // shortcuts trodden from a gate to where people go, when the paths take the long way round
                var dests = new List<float2>();
                foreach (var st in Stands) dests.Add(st.P);
                foreach (var cr in Courts) dests.Add(Poly.Centroid(cr));
                var cands = new List<(float2 a, float2 b, float score)>();
                foreach (var g in Gates)
                    foreach (var dst in dests)
                    {
                        float L = math.distance(g, dst);
                        if (L < 25f || L > 120f) continue;
                        var dir = math.normalizesafe(dst - g);
                        var a = g + dir * 3f; var b = dst - dir * 9f;
                        if (!Clear(a, b)) continue;
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
                    bool hero = flaws.Contains("shortcut") && i == 0;
                    Brushes.EdgeStrip(_b, pts, "wornPath", new StripOptions { Gaps = hero ? 0f : 0.15f, Run = new float2(15f, 40f), Opacity = Op(hero ? 0.75f : 0.5f, hero ? 1f : 0.8f), Width = new float2(0.7f, 1f), Cause = hero ? "flaw:shortcut" : "feet:shortcut" });
                }
                // the busiest benches on the grass (never every seat of a terrace): bare ground in front, a little litter;
                // bins: litter round their feet
                var lawnBenches = new List<(Thing b, float score)>();
                for (int i = 0; i < Benches.Count; i++)
                {
                    var b = Benches[i];
                    bool paved = false;
                    foreach (var r in All) if (Poly.Contains(r, b.P)) { paved = true; break; }
                    if (paved) continue;
                    float near = float.MaxValue;
                    foreach (var g in Gates) near = math.min(near, math.distance(g, b.P));
                    lawnBenches.Add((b, U(214, i) * 40f - math.min(near, 120f) * 0.3f));
                }
                lawnBenches.Sort((x, y) => y.score.CompareTo(x.score));
                int bn = 0;
                for (int i = 0; i < lawnBenches.Count && bn < 14; i++)
                {
                    if (U(210, i) > 0.6f) continue;
                    var b = lawnBenches[i].b;
                    var front = new float2(math.sin(b.Yaw), math.cos(b.Yaw));
                    Brushes.At(_b, b.P + front * 1.2f, new float2(front.y, -front.x), "barePatch", Range(3f, 4.5f, 211, i), Op(0.3f, 0.55f), 0, ColourLevel.Any, "feet:bench");
                    if (U(212, i) < 0.4f) Brushes.At(_b, b.P + front * 0.8f, front, "litterMix", Range(2.5f, 4f, 213, i), Op(0.5f, 0.9f), 0, ColourLevel.Any, "feet:litter");
                    bn++;
                }
                for (int i = 0; i < Bins.Count; i++)
                    if (U(220, i) < 0.6f) Brushes.At(_b, Bins[i].P + new float2(Range(-0.6f, 0.6f, 221, i), Range(-0.6f, 0.6f, 222, i)), Wind, "litterMix", Range(2.5f, 4f, 223, i), Op(0.5f, 0.9f), 1, ColourLevel.Any, "feet:bin");
            }

            /// <summary>A straight walk from a to b crosses no field, court, hard surface or tree trunk and stays in the site.</summary>
            private bool Clear(float2 a, float2 b)
            {
                for (int i = 0; i <= 12; i++)
                {
                    var p = math.lerp(a, b, i / 12f);
                    if (!Inside(p, 1f)) return false;
                    foreach (var r in All) if (Poly.Contains(r, p)) return false;
                }
                foreach (var t in Trees) if (SegDist(t.P, a, b) < 1.5f) return false;
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
                    if (placed.Exists(q => math.distancesq(q, p) < 14f * 14f)) continue;
                    bool isWet = wet < 3;
                    if (!isWet && dry >= 4) break;
                    float scale = math.saturate(depth / 0.03f) * 0.6f + 0.7f;
                    bool hero = big && k2 == 0;
                    if (hero) scale *= 1.9f;
                    // the hollow: an ellipse along the path (or loose on the lot), pieces fill it
                    float L = (lot ? Range(4f, 7f, 101, k2) : Range(3.5f, 6f, 103, k2)) * scale, W = lot ? L * Range(0.55f, 0.8f, 102, k2) : math.max(1.6f, half * 2f * Range(0.8f, 1.1f, 104, k2));
                    var ax = dir; var sd = new float2(-ax.y, ax.x);
                    var hollow = new float2[10];
                    for (int i = 0; i < hollow.Length; i++)
                    {
                        float t = i / (float)hollow.Length * 2f * math.PI;
                        float wob = 1f + 0.18f * DecalNoise.Perlin(math.cos(t) * 1.3f + k2, math.sin(t) * 1.3f, _seed + 7u);
                        hollow[i] = p + ax * math.cos(t) * L * 0.5f * wob + sd * math.sin(t) * W * 0.5f * wob;
                    }
                    if (isWet) Brushes.Fill(_b, hollow, "puddlePiece", Op(0.7f, 1f), ColourLevel.Any, hero ? "flaw:bigPuddle" : "water:puddle");
                    else Brushes.Fill(_b, hollow, k2 % 2 == 0 ? "dryTidemark" : "dampPatch", Op(0.45f, 0.75f), ColourLevel.Any, "water:tidemark");
                    if (isWet) Brushes.At(_b, p + ax * L * 0.3f, ax, "dampPatch", L * 1.4f, Op(0.3f, 0.55f), 1, ColourLevel.Any, "water:damp");
                    placed.Add(p);
                    if (isWet) wet++; else dry++;
                    k2++;
                }
            }

            // ───────────── wheels: the car park ─────────────

            private void Wheels()
            {
                if (Asphalt.Count == 0 && Aisles.Count == 0) return;
                float2 walk = Gates.Count > 0 ? Gates[0] : float2.zero;
                foreach (var a in Asphalt)
                {
                    var c = Poly.Centroid(a);
                    float best = float.MaxValue;
                    foreach (var l in Paths) foreach (var p in l.P) { float d = math.distancesq(p, c); if (d < best) { best = d; walk = p; } }
                    // cracks along the lot, a step in from its edges, and the odd pothole
                    var inset = Brushes.Inset(a, 3.5f);
                    if (inset != null) Brushes.CracksAlong(_b, Ring(inset), "crackAsphalt", new StripOptions { Gaps = 0.45f, Run = new float2(10f, 26f), Closed = true, Opacity = Op(0.5f, 0.85f), Cause = "wheels:cracks" });
                    var inner = Brushes.Inset(a, 9f);
                    if (inner != null && Poly.Area(inner) > 200f && Poly.IsCcw(inner)) Brushes.CracksAlong(_b, Ring(inner), U(501) < 0.5f ? "crackSealed" : "crackAsphalt", new StripOptions { Gaps = 0.6f, Run = new float2(8f, 22f), Closed = true, Opacity = Op(0.45f, 0.8f), Cause = "wheels:cracks" });
                    Brushes.Scatter(_b, BrushArea.Of(a), "crackWeb", 0.08f * A, new ScatterOptions { Opacity = Op(0.45f, 0.8f), Clump = 2f, Cause = "wheels:crackWeb" });
                    if (U(500) < 0.6f) Brushes.Scatter(_b, BrushArea.Of(a), "potholePatch", 0.01f * A, new ScatterOptions { Opacity = Op(0.6f, 0.9f), Clump = 1.5f, Cause = "wheels:pothole" });
                }
                int n = 0;
                foreach (var l in Aisles)
                {
                    if (l.P.Length < 2) continue;
                    float near = math.saturate(1f - math.distance(l.P[l.P.Length / 2], walk) / 80f);
                    // tyre tracks in both lanes, faint, stronger near the walk-in
                    for (int s = -1; s <= 1; s += 2)
                        Brushes.EdgeStrip(_b, l.P, "tyreTrack", new StripOptions { Side = s, Offset = l.Half * 0.45f, Gaps = 0.5f, Run = new float2(10f, 30f), Opacity = Op(0.35f + near * 0.3f, 0.7f + near * 0.3f), Cause = "wheels:tyres" });
                    n++;
                }
                // oil under the busy stalls: the ones nearest the walk-in carry most of it
                var bays = new List<(Thing t, float d)>();
                foreach (var b in Bays) bays.Add((b, math.distance(b.P, walk)));
                bays.Sort((x, y) => x.d.CompareTo(y.d));
                for (int i = 0; i < bays.Count; i++)
                {
                    float p = math.lerp(0.55f, 0.08f, i / math.max(1f, bays.Count - 1f)) * math.min(1.3f, A);
                    if (U(160, i) > p) continue;
                    var b = bays[i].t;
                    float2 toAisle = new float2(math.sin(b.Yaw), math.cos(b.Yaw));
                    float bd = float.MaxValue;
                    foreach (var l in Aisles) foreach (var q in l.P) { float d = math.distancesq(q, b.P); if (d < bd) { bd = d; toAisle = math.normalizesafe(q - b.P, toAisle); } }
                    var at = b.P + toAisle * Range(0.6f, 1.4f, 161, i);
                    Brushes.At(_b, at, toAisle, U(162, i) < 0.7f ? "oilSpot" : "oilDrips", Range(1.6f, 3f, 163, i), Op(0.5f, 0.95f), U(164, i) < 0.3f ? 1 : 0, ColourLevel.Any, "wheels:oil");
                }
            }

            // ───────────── objects: rust at the feet of steel ─────────────

            private void Objects()
            {
                for (int i = 0; i < Steel.Count; i++)
                {
                    if (U(200, i) > 0.3f) continue;
                    var s = Steel[i];
                    bool mast = s.Id.StartsWith("LightpoleIndustrial", StringComparison.Ordinal);
                    float sz = mast ? Range(2f, 3f, 201, i) : Range(1.2f, 1.8f, 202, i);
                    Brushes.At(_b, s.P + Wind * sz * 0.15f, Wind, "rustBleed", sz, Op(0.55f, 0.9f), 0, ColourLevel.Any, "objects:rust");
                }
            }

            // ───────────── measure: how much the causes touch ─────────────

            /// <summary>Share of the site any cause instance (not the tone) touches, and where one is strong (1.5 m cells).</summary>
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
                foreach (var d in Out)
                {
                    if (d.Cause != null && d.Cause.StartsWith("tone", StringComparison.Ordinal)) continue;
                    // leaves are meant to smother the ground under trees: they count as touched, never as a strong blotch
                    bool leaves = d.Cause != null && (d.Cause.StartsWith("leaves", StringComparison.Ordinal) || d.Cause.StartsWith("woodland", StringComparison.Ordinal));
                    var k = _b.Lib.Get(d.Kind);
                    if (k == null) continue;
                    var foot = DecalVariants.FootprintOf(k, d.Variant) * d.Scale * 0.5f;
                    float op = d.Opacity * k.Opacity;
                    var he = d.HalfExtent(DecalVariants.FootprintOf(k, d.Variant));
                    int i0 = math.max(0, (int)((d.P.x - he.x - lo.x) / cell)), i1 = math.min(nx - 1, (int)((d.P.x + he.x - lo.x) / cell));
                    int j0 = math.max(0, (int)((d.P.y - he.y - lo.y) / cell)), j1 = math.min(nz - 1, (int)((d.P.y + he.y - lo.y) / cell));
                    float c = math.cos(d.Yaw), s = math.sin(d.Yaw);
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            var q = lo + new float2(i + 0.5f, j + 0.5f) * cell - d.P;
                            var l = new float2(q.x * c - q.y * s, q.x * s + q.y * c) / math.max(new float2(0.05f), foot);
                            if (math.lengthsq(l) <= 1f) strength[j * nx + i] = math.max(strength[j * nx + i], leaves ? math.min(op, 0.5f) : op);
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
        }
    }
}
