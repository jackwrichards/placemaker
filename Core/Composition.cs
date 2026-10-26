// Recipes: composable layers. A recipe is data (serialisable), so built-in types are just
// shipped recipes and player-made recipes later are just saved files.
//
//   Park                     = park
//   Park + restrooms         = park ─┬─ restrooms (add-on, same region)
//   Roads with parks         = roads ─ park (target "block")
//   Split park | plaza       = split ─┬─ park  (target "part:0")
//                                     └─ plaza (target "part:1")
//   Plaza with edge zoning   = plaza ─ edgeZoning (target "edge")
//   Park with central plaza  = park ─ plaza (target "place:plaza"; the park carves the place out
//                              because a child targets it — see AreaInput.ChildTargets)

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core
{
    public sealed class RecipeStep
    {
        /// <summary>IAreaGenerator.Id of the layer.</summary>
        public string Layer = "";
        public SettingsBag Settings = new SettingsBag();
        /// <summary>Ids of settings the player set explicitly (Randomize must not touch them).</summary>
        public HashSet<string> Locked = new HashSet<string>(StringComparer.Ordinal);
        public uint Seed;
        /// <summary>
        /// Which regions of the parent this step runs in. "" = the parent's own region (add-ons).
        /// Otherwise a tag ("block") or an exact region key ("part:1", "block:3").
        /// </summary>
        public string Target = "";
        public bool Enabled = true;
        /// <summary>Markers placed by the player for this step (entrances, access points, split lines…).</summary>
        public List<EdgeMarker> Markers = new List<EdgeMarker>();
        public List<RecipeStep> Children = new List<RecipeStep>();
        /// <summary>
        /// Per-region replacement: region key → step that runs there instead of the matching child.
        /// Lets the player turn "block:3" of a road grid into a plaza while other blocks stay parks.
        /// </summary>
        public Dictionary<string, RecipeStep> RegionOverrides = new Dictionary<string, RecipeStep>(StringComparer.Ordinal);

        public RecipeStep Clone()
        {
            var c = new RecipeStep
            {
                Layer = Layer, Settings = Settings.Clone(), Seed = Seed, Target = Target, Enabled = Enabled,
                Locked = new HashSet<string>(Locked, StringComparer.Ordinal),
                Markers = new List<EdgeMarker>(Markers),
            };
            foreach (var ch in Children) c.Children.Add(ch.Clone());
            foreach (var kv in RegionOverrides) c.RegionOverrides[kv.Key] = kv.Value.Clone();
            return c;
        }
    }

    public sealed class Recipe
    {
        /// <summary>Stable id for shipped recipes ("park", "roads-with-parks"); a GUID for player recipes.</summary>
        public string Id = "";
        public string NameKey = "";
        /// <summary>Player-facing name for player recipes (NameKey empty).</summary>
        public string Name = "";
        public string Icon = "";
        public int FormatVersion = 1;
        public RecipeStep Root = new RecipeStep();
        /// <summary>
        /// (s17) The player's park-stats choices for this build (overrides + "keep within vanilla range"). Null =
        /// suggested values. Per build: saved in the receipt (ReceiptCodec v2), not in recipe JSON or presets, and
        /// not part of the "modified" fingerprint.
        /// </summary>
        public Build.ParkStatsSettings ParkStats;

        public Recipe Clone() => new Recipe { Id = Id, NameKey = NameKey, Name = Name, Icon = Icon, FormatVersion = FormatVersion, Root = Root.Clone(), ParkStats = ParkStats?.Clone() };

        /// <summary>The simplest recipe: one layer with its defaults.</summary>
        public static Recipe Single(IAreaGenerator g, uint seed) => new Recipe
        {
            Id = g.Id, NameKey = g.NameKey, Icon = g.Icon,
            Root = new RecipeStep { Layer = g.Id, Settings = g.Defaults(), Seed = seed },
        };
    }

    /// <summary>Result of running a recipe: one AreaPlan per executed step × region.</summary>
    public sealed class ComposedPlan
    {
        public List<AreaPlan> Layers = new List<AreaPlan>();
        /// <summary>Every text value the player's design holds (Compose, from the recipe): the assets the player picked. Placemaker
        /// uses a non-vanilla asset only when it is one of these (AutoAssets; the user, 26 Oct).</summary>
        public HashSet<string> Picks = new HashSet<string>(StringComparer.Ordinal);
        public List<PlanIssue> Issues = new List<PlanIssue>();
        /// <summary>
        /// Set by Build/Declutter once the objects were fitted with real footprints (runs once per plan,
        /// right after automatic assets are resolved; preview, price and build then agree).
        /// </summary>
        public bool Decluttered;
        /// <summary>
        /// (s15) Existing building lots / footprints nothing planned may touch (Build/SiteSafety). Set by
        /// Compose from the site; Declutter re-checks objects against it after nudging them.
        /// </summary>
        public List<float2[]> Protected = new List<float2[]>();
        /// <summary>
        /// (s59) Existing city roads/paths around the site (captured, ground level, full width with sidewalks). Set by Compose
        /// (SiteSafety.Apply); Declutter keeps objects off them so the game finds nothing of ours on a street or sidewalk.
        /// </summary>
        public List<NearbyNet> ExistingNets = new List<NearbyNet>();
        /// <summary>
        /// (s19) Land the build occupies as one place (CCW rings): the region of every top-most non-loose fill/split
        /// step. Road grids (loose) have none. Build/ZoneGuard blocks the road zone cells on it outside the zoning.
        /// </summary>
        public List<float2[]> Footprints = new List<float2[]>();
        /// <summary>(s19) Rings of the zone-blocking lots (physical lot areas, see Build/ZoneGuard). Set by Compose.</summary>
        public List<float2[]> ZoneBlockers = new List<float2[]>();
        /// <summary>
        /// (core) Kerb strips: thin zone-blocking lots inside the first row of the CITY streets' zone blocks along the build, built
        /// right after Apply and before the zoning refresh, so the city's older blocks lose every column that reaches into the build
        /// and the build's own lanes zone its shop plots (docs/teardown/08 §4.3). Set by ZoneGuard.Apply.
        /// </summary>
        public List<float2[]> KerbBlockers = new List<float2[]>();
        /// <summary>(s19) Zone cells of existing roads the build zones or blocks (preview). Set by Compose.</summary>
        public List<Build.ZoneCellMark> ZoneCells = new List<Build.ZoneCellMark>();
        /// <summary>(s26) The cutout quads before snapping (ZoneGuard.Refresh re-snaps them to the live blocks after Apply).</summary>
        public List<Build.ZoneQuad> ZoneQuads = new List<Build.ZoneQuad>();
        /// <summary>(s22) Height change of the terraform as a heat map over the area (preview / legend). Set by Compose; null = none.</summary>
        public Build.TerrainHeat TerrainHeat;
        /// <summary>
        /// (s31) The outline each fill/split step planned in (its AreaShape), by plan. Not persisted. Build/SiteHarmony grades
        /// the land of the layers that take part (parks, leisure types) and leaves the rest alone.
        /// </summary>
        public Dictionary<AreaPlan, float2[]> FillRings = new Dictionary<AreaPlan, float2[]>();
        /// <summary>(zoning speed) Where the plan's time went: each layer and each step after them, ms (PlanTimings.Describe). Not persisted.</summary>
        public List<KeyValuePair<string, double>> Timings = new List<KeyValuePair<string, double>>();
        /// <summary>Entrances the player marked "with crosswalk" (Layers/Common/StreetJoins). Set by Compose; not persisted.</summary>
        public List<float2> Crosswalks = new List<float2>();
        /// <summary>Footpath ends at a city street: stopped at the kerb (the game joins them) / split the street with a crosswalk. Set by StreetJoins.</summary>
        public int KerbJoins, CrosswalkJoins;
        /// <summary>Footpath ends attached to a net StreetJoins does not know as a captured road (left as they were).</summary>
        public List<string> JoinNotes = new List<string>();

        public bool HasErrors
        {
            get
            {
                foreach (var i in Issues) if (i.Severity == IssueSeverity.Error) return true;
                foreach (var l in Layers) if (l.HasErrors) return true;
                return false;
            }
        }

        /// <summary>Stats summed across layers by (key, unit), in first-seen order.</summary>
        public List<PlanStat> MergedStats()
        {
            var order = new List<(string, string)>();
            var sums = new Dictionary<(string, string), double>();
            foreach (var l in Layers)
                foreach (var s in l.Stats)
                {
                    var k = (s.Key, s.Unit ?? "");
                    if (!sums.ContainsKey(k)) { sums[k] = 0; order.Add(k); }
                    sums[k] += s.Value;
                }
            var r = new List<PlanStat>(order.Count);
            foreach (var k in order) r.Add(new PlanStat(k.Item1, sums[k], k.Item2));
            return r;
        }

        public IEnumerable<PlanIssue> AllIssues()
        {
            foreach (var i in Issues) yield return i;
            foreach (var l in Layers) foreach (var i in l.Issues) yield return i;
        }
    }

    /// <summary>
    /// Runs a recipe tree over an area. Pure and deterministic; safe on a worker thread.
    /// Failure of one layer never kills the recipe: the failing step reports an Error issue and
    /// its children are skipped.
    /// </summary>
    public static class Composer
    {
        public const int MaxDepth = 6;
        public const int MaxSteps = 512;

        /// <summary>The text values of every step's settings (single and lists): the assets the player picked are among them.</summary>
        public static void CollectPicks(RecipeStep step, HashSet<string> into)
        {
            if (step == null) return;
            foreach (var v in step.Settings.Values.Values)
            {
                if (v is string s) { if (s.Length > 0) into.Add(s); }
                else if (v is System.Collections.IEnumerable list) foreach (var x in list) if (x is string xs && xs.Length > 0) into.Add(xs);
            }
            foreach (var c in step.Children) CollectPicks(c, into);
            foreach (var o in step.RegionOverrides.Values) CollectPicks(o, into);
        }

        public static ComposedPlan Compose(Recipe recipe, AreaShape area, SiteContext site, CancellationToken cancel)
        {
            var result = new ComposedPlan();
            CollectPicks(recipe?.Root, result.Picks);
            var reserved = new ReservedSpace();
            int budget = MaxSteps;
            var dress = new Dictionary<AreaPlan, Decals.DressSettings>();
            Run(recipe.Root, area, new[] { RegionTags.Root }, "root", site, reserved, null, "0", 0, result, ref budget, cancel, false, null, dress);
            var clock = new PlanClock(result.Timings);
            // CS2 joins paths only at identical nodes: split a path where a spur (often a feature's
            // "path to it") ends on it (g5).
            Layers.Common.PathJoins.JoinAll(result);
            // Footpaths meet city streets at the kerb (the game joins them, no crosswalk), unless the entrance asks for a crosswalk.
            Layers.Common.StreetJoins.Apply(result, site);
            clock.Lap("path joins");
            // s21: decals complement what was planned (leaves under trees, oil in bays, wear at junctions…). Before
            // cutouts and building lots are enforced, so those rules apply to decals too.
            Decals.Dressing.Apply(result, area, site, dress, cancel);
            clock.Lap("decals");
            // (s33) Paint intents (1 dry, 2 lush, 3 dirt, 4 sand) → this map's material slots (identity on vanilla maps).
            Paint.TerrainPalette.Remap(result, site?.Terrain);
            // s14: zoning cutouts stay empty whatever a feature placed (Layers/Common/Cutouts.cs).
            Layers.Common.Cutouts.Enforce(result, area?.Ring != null && area.Ring.Length >= 3 ? new[] { area.Ring2D() } : null);
            clock.Lap("cutouts");
            // (s31) Site harmony: one graded ground for everything planned. Paths level across and smooth along, pads and hard
            // surfaces flat, decorative mounds kept off them, all terraform merged into one smooth field (no curtains).
            Build.SiteHarmony.Apply(result, area, site, cancel);
            clock.Lap("site harmony");
            // (s15) Never build on existing buildings: drop / cut whatever would touch a building lot.
            Build.SiteSafety.Apply(result, area, site);
            clock.Lap("site safety");
            // (core) Our roads zone only on the sides that hold the build's shops: the vanilla per-side "no zoning" flag elsewhere.
            try { Build.LaneSides.Apply(result, site); } catch (Exception) { /* never fail a plan over it */ }
            clock.Lap("lane sides");
            // (s19) Road zone cells under the build are blocked outside the zoning; the zoning snaps to the cells.
            Build.ZoneGuard.Apply(result, site);
            clock.Lap("zone guard");
            // (s22) Terraform preview: a heat map of the height change instead of dashed boxes.
            try { result.TerrainHeat = Build.TerrainHeat.Build(result, area?.Ring2D(), site?.HeightAt); }
            catch (Exception) { result.TerrainHeat = null; }
            clock.Lap("height map");
            return result;
        }

        private static void Run(RecipeStep step, AreaShape shape, string[] tags, string regionKey, SiteContext site,
            ReservedSpace reserved, List<float2[]> ownPlaces, string path, int depth, ComposedPlan result, ref int budget, CancellationToken cancel, bool covered,
            Decals.DressSettings parentDress = null, Dictionary<AreaPlan, Decals.DressSettings> dress = null)
        {
            cancel.ThrowIfCancellationRequested();
            if (!step.Enabled) return;
            if (depth > MaxDepth || --budget < 0)
            {
                result.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = "Placemaker.Issue.RecipeTooDeep" });
                return;
            }

            var gen = GeneratorRegistry.Get(step.Layer);
            if (gen == null)
            {
                result.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = "Placemaker.Issue.UnknownLayer", Args = new object[] { step.Layer } });
                return;
            }

            var input = new AreaInput
            {
                Shape = shape,
                Site = site,
                Settings = step.Settings,
                Seed = Seeds.Mix(step.Seed, Seeds.Hash(path + "|" + regionKey)),
                Markers = MarkersOn(step.Markers, shape.Ring),
                RegionTags = tags,
                Reserved = reserved.Clone(),
                ChildLayers = EnabledChildren(step, true),
                ChildTargets = EnabledChildren(step, false),
            };
            // Layers inside a place (the one filling it and its own children) must not see it as taken.
            if (ownPlaces != null) foreach (var mine in ownPlaces) input.Reserved.Polygons.Remove(mine);
            foreach (var m in input.Markers) if (m.Crosswalk) result.Crosswalks.Add(m.Position.xz);

            AreaPlan plan;
            var layerClock = new PlanClock(result.Timings);
            try
            {
                plan = gen.Plan(input, cancel) ?? new AreaPlan();
                layerClock.Lap(gen.Id, plan.Timings);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                result.Issues.Add(new PlanIssue
                {
                    Severity = IssueSeverity.Error, Key = "Placemaker.Issue.LayerFailed",
                    Args = new object[] { gen.Id, e.GetType().Name + ": " + e.Message },
                });
                return;
            }

            plan.GeneratorId = gen.Id;
            plan.StepPath = path + "@" + regionKey;
            plan.Seed = input.Seed;
            result.Layers.Add(plan);
            if (gen.Kind != LayerKind.AddOn && shape?.Ring != null && shape.Ring.Length >= 3) result.FillRings[plan] = shape.Ring2D();
            // (s19) The land this build occupies as one place: the region of the top-most non-loose fill/split.
            if (!covered && gen.Kind != LayerKind.AddOn && !GeneratorRegistry.IsLoose(gen.Id) && shape?.Ring != null && shape.Ring.Length >= 3)
            {
                result.Footprints.Add(Geometry.Poly.EnsureCcw(Geometry.Poly.To2D(shape.Ring)));
                covered = true;
            }
            // Dressing settings: a fill step with decal controls owns its own; features and children share their parent's.
            var ownDress = Decals.DressingControls.Read(gen, step.Settings);
            if (ownDress != null) ownDress.Ring = shape.Ring2D();
            var stepDress = ownDress ?? parentDress;
            if (dress != null && stepDress != null) dress[plan] = stepDress;
            Reserve(plan, reserved);
            var places = ReservePlaces(plan, reserved);
            // Features of a terraforming layer plan on the NEW ground (g5: TerrainField).
            var childSite = ChildSite(plan, site);

            for (int ci = 0; ci < step.Children.Count; ci++)
            {
                var child = step.Children[ci];
                string childPath = path + "/" + ci;
                if (string.IsNullOrEmpty(child.Target))
                {
                    Run(child, shape, tags, regionKey, childSite, reserved, ownPlaces, childPath, depth + 1, result, ref budget, cancel, covered, stepDress, dress);
                    continue;
                }
                foreach (var region in plan.Regions)
                {
                    if (!Matches(child.Target, region)) continue;
                    var effective = step.RegionOverrides.TryGetValue(region.Key, out var ov) ? ov : child;
                    var sub = new AreaShape { Ring = region.Ring, Source = shape.Source, Attachments = AttachmentsOn(shape.Attachments, region.Ring) };
                    var inside = ownPlaces;
                    if (places.TryGetValue(region, out var own)) inside = new List<float2[]>(ownPlaces ?? new List<float2[]>()) { own };
                    Run(effective, sub, region.Tags, region.Key, childSite, reserved, inside, childPath, depth + 1, result, ref budget, cancel, covered, stepDress, dress);
                }
            }
        }

        /// <summary>
        /// The site children of <paramref name="plan"/> see: the same snapshot with the ground after the
        /// plan's terraform (AreaPlan.HeightAfter, else its terraform polygons), or <paramref name="site"/>
        /// unchanged when the plan does not reshape the ground.
        /// </summary>
        public static SiteContext ChildSite(AreaPlan plan, SiteContext site)
        {
            if (plan == null || site == null) return site;
            var after = plan.HeightAfter ?? Layers.Common.TerrainField.FromTerraform(plan.Terraform, site.HeightAt);
            return after == null ? site : site.WithHeight(after);
        }

        /// <summary>Layer ids (or targets) of the enabled children of a step.</summary>
        public static string[] EnabledChildren(RecipeStep step, bool layers)
        {
            var r = new List<string>();
            foreach (var c in step.Children)
                if (c != null && c.Enabled) r.Add(layers ? c.Layer ?? "" : c.Target ?? "");
            return r.ToArray();
        }

        /// <summary>Region tag prefix of places a parent carved out for a feature ("place:plaza").</summary>
        public const string PlacePrefix = "place:";

        public static bool IsPlace(PlannedRegion region)
        {
            foreach (var t in region.Tags) if (t != null && t.StartsWith(PlacePrefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Places (regions tagged "place:*") are fully claimed: siblings and later add-ons keep out of
        /// them; only the layer that fills a place sees it as free (see ownPlace in Run).
        /// </summary>
        public static Dictionary<PlannedRegion, float2[]> ReservePlaces(AreaPlan plan, ReservedSpace reserved)
        {
            var map = new Dictionary<PlannedRegion, float2[]>();
            foreach (var region in plan.Regions)
            {
                if (region?.Ring == null || region.Ring.Length < 3 || !IsPlace(region)) continue;
                var ring = new float2[region.Ring.Length];
                for (int i = 0; i < ring.Length; i++) ring[i] = region.Ring[i].xz;
                reserved.Polygons.Add(ring);
                map[region] = ring;
            }
            return map;
        }

        public static bool Matches(string target, PlannedRegion region)
        {
            if (target == region.Key) return true;
            foreach (var t in region.Tags) if (t == target) return true;
            return false;
        }

        /// <summary>Claims space for everything a plan builds so later layers avoid it.</summary>
        public static void Reserve(AreaPlan plan, ReservedSpace reserved)
        {
            foreach (var n in plan.Nets)
            {
                float hw = DefaultHalfWidth(n.Role);
                // Approximate the Bézier by 4 chords — enough for clearance checks.
                float3 prev = n.A;
                for (int i = 1; i <= 4; i++)
                {
                    float t = i / 4f;
                    float3 p = Bezier(n.A, n.B, n.C, n.D, t);
                    reserved.AddCorridor(prev.xz, p.xz, hw, n.Role.ToString());
                    prev = p;
                }
            }
            foreach (var o in plan.Objects)
                if (o.Role != ObjectRole.Decal) reserved.AddDisc(o.Position.xz, DefaultRadius(o.Role), o.Role.ToString());
            foreach (var a in plan.Areas)
                if (a.Role == AreaRole.Lot)
                {
                    var r = new float2[a.Ring.Length];
                    for (int i = 0; i < r.Length; i++) r[i] = a.Ring[i].xz;
                    reserved.Polygons.Add(r);
                }
        }

        public static float DefaultHalfWidth(NetRole role)
        {
            switch (role)
            {
                case NetRole.Avenue: return 14f;
                case NetRole.Street: case NetRole.PerimeterRoad: case NetRole.Roundabout: return 8f;
                case NetRole.CulDeSac: return 6f;
                case NetRole.DriveAisle: case NetRole.DriveConnector: case NetRole.Alley: return 3.5f;
                case NetRole.PathwayWide: return 4f;   // the automatic wide walk is the vanilla 8 m PedestrianPathWide01 (FEEDBACK-4: offsets use the real width)
                case NetRole.Pathway: return 1.5f;
                case NetRole.Fence: return 0.3f;
                default: return 2f;
            }
        }

        public static float DefaultRadius(ObjectRole role)
        {
            switch (role)
            {
                case ObjectRole.Tree: return 2.5f;
                case ObjectRole.Bush: return 1f;
                case ObjectRole.Centerpiece: return 6f;
                case ObjectRole.ParkingBay: return 1.5f;
                case ObjectRole.Bench: case ObjectRole.Bin: case ObjectRole.Lamp: return 0.8f;
                default: return 1f;
            }
        }

        public static float3 Bezier(float3 a, float3 b, float3 c, float3 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        /// <summary>Keeps markers lying on (within 1.5 m of) the region outline, re-indexed to its segments.</summary>
        public static List<EdgeMarker> MarkersOn(List<EdgeMarker> markers, float3[] ring)
        {
            var r = new List<EdgeMarker>();
            if (markers == null || ring.Length < 2) return r;
            foreach (var m in markers)
            {
                if (ClosestSegment(ring, m.Position.xz, out int seg, out float t, out float d) && d <= 1.5f)
                {
                    var mm = MarkerVariants.Normalize(m); mm.RingSegment = seg; mm.RingT = t; r.Add(mm);
                }
            }
            return r;
        }

        public static List<BoundaryAttachment> AttachmentsOn(List<BoundaryAttachment> atts, float3[] ring)
        {
            var r = new List<BoundaryAttachment>();
            if (atts == null || ring.Length < 2) return r;
            foreach (var a in atts)
            {
                if (ClosestSegment(ring, a.Position.xz, out int seg, out float t, out float d) && d <= 2f)
                {
                    var aa = a; aa.RingSegment = seg; aa.RingT = t; r.Add(aa);
                }
            }
            return r;
        }

        public static bool ClosestSegment(float3[] ring, float2 p, out int seg, out float t, out float dist)
        {
            seg = -1; t = 0; dist = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                float2 a = ring[i].xz, b = ring[(i + 1) % ring.Length].xz;
                var ab = b - a;
                float len = math.lengthsq(ab);
                float tt = len > 1e-9f ? math.saturate(math.dot(p - a, ab) / len) : 0f;
                float d = math.distance(p, a + ab * tt);
                if (d < dist) { dist = d; seg = i; t = tt; }
            }
            return seg >= 0;
        }
    }

    /// <summary>Deterministic seed helpers (no System.Random state shared across threads).</summary>
    public static class Seeds
    {
        /// <summary>FNV-1a 32-bit — stable across runtimes (unlike string.GetHashCode).</summary>
        public static uint Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return h;
            }
        }

        /// <summary>Mix two values into an independent stream seed (splitmix-style finaliser).</summary>
        public static uint Mix(uint a, uint b)
        {
            unchecked
            {
                uint x = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u + (a << 6) + (a >> 2));
                x ^= x >> 16; x *= 0x85EBCA6Bu; x ^= x >> 13; x *= 0xC2B2AE35u; x ^= x >> 16;
                return x == 0 ? 1u : x;
            }
        }

        public static uint Mix(uint a, string salt) => Mix(a, Hash(salt));

        public static Unity.Mathematics.Random Rng(uint seed, string salt) => new Unity.Mathematics.Random(Mix(seed, salt));
    }

    /// <summary>
    /// Price estimate from the catalog's AssetInfo. Units: objects per item, nets per metre,
    /// surfaces/areas per m². Pure, so the UI summary and tests agree.
    /// </summary>
    public static class Pricing
    {
        public struct Estimate { public double Cost; public double Upkeep; }

        public static Estimate Of(ComposedPlan plan, Dictionary<string, AssetInfo> assets)
        {
            var e = new Estimate();
            foreach (var l in plan.Layers)
            {
                foreach (var o in l.Objects) Add(ref e, assets, o.Asset.Id, 1);
                foreach (var n in l.Nets) Add(ref e, assets, n.Asset.Id, BillableNetLength(CurveLength(n)));
                foreach (var s in l.Surfaces) Add(ref e, assets, s.Asset.Id, PolygonArea(s.Ring));
                foreach (var a in l.Areas) Add(ref e, assets, a.Asset.Id, PolygonArea(a.Ring));
            }
            return e;
        }

        private static void Add(ref Estimate e, Dictionary<string, AssetInfo> assets, string id, double qty)
        {
            if (string.IsNullOrEmpty(id) || assets == null || !assets.TryGetValue(id, out var info)) return;
            e.Cost += info.Cost * qty;
            e.Upkeep += info.Upkeep * qty;
        }

        /// <summary>
        /// The game charges nets per (rounded) 8 m cell: NetUtils.GetConstructionCost =
        /// max(1, round(length / 8)) × composition cost (decompiled, CS2 1.6.2). AssetInfo.Cost is per
        /// metre (= cost per cell / 8), so the billable length is whole cells × 8.
        /// </summary>
        public static double BillableNetLength(double length) => Math.Max(1.0, Math.Round(length / 8.0)) * 8.0;

        public static double CurveLength(PlannedNet n)
        {
            double len = 0; float3 prev = n.A;
            for (int i = 1; i <= 8; i++) { var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f); len += math.distance(prev, p); prev = p; }
            return len;
        }

        public static double PolygonArea(float3[] ring)
        {
            double a = 0;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                a += (double)ring[j].x * ring[i].z - (double)ring[i].x * ring[j].z;
            return Math.Abs(a) * 0.5;
        }
    }
}
