// Placemaker neighbourhood polish — automatic yard stitching on the game's own houses (GPL-3.0).
//
// Every house the game grows is edited after it is placed, never replaced: its own fence/hedge lines and its own lawn are
// reshaped so neighbours join up (Core/Neighbours/YardStitch decides what; docs/research/custom-assets-on-the-fly.md).
// How the game's parts behave (decompiled 1.6.2f1):
//   * fences and hedges are utility lanes (UtilityTypes.Fence) owned by the building, made by LaneSystem (Modification4)
//     from the prefab's SubLane list whenever the building gets Updated (construction end, level-up, move): the same lane
//     entity is reused and its Curve reset, or recreated (Created) after construction. LaneSystem matches lanes by path
//     nodes, never by curve, so a rewritten Curve survives until the next rebuild — and is saved with the game.
//   * a lane collapsed to a point draws nothing and never hides another fence (Net.OverrideSystem tests parallel lines).
//   * a lawn is a surface area owned by the building; its nodes can be rewritten (+Updated) and are saved; construction end
//     and level-up recreate it from the prefab.
// So this runs in Modification4B (after LaneSystem's barrier, before OverrideSystem, culling and rendering: no flicker),
// picks up fence lanes the game created or reset, and re-stitches their houses and neighbours from the prefab's ORIGINAL
// geometry. Our own writes are recognised (the curve we wrote) and never trigger another pass.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Colossal.Mathematics;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core.Neighbours;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem : GameSystemBase
    {
        /// <summary>The polish can run this session (false after repeated errors). The player's master switch is Paused.</summary>
        public static bool FeatureEnabled;
        /// <summary>"Outgrow new buildings", this city's (NeighbourPolishSystem.Switches): a lot that spawns gets the flag yes
        /// (off: no; the painter decides). Only yes lots are ever edited (PolishedLot, NeighbourPolishSystem.Lots).</summary>
        public static bool PolishNew;

        // the look a lot is polished with (PolishedLot.Parts): never renumber
        public const byte PartLawns = 1, PartPlanting = 2, PartFeatures = 4, PartPaths = 8, PartRocks = 16, PartPaint = 32, PartShopPaving = 64, PartsAll = 127;

        /// <summary>The look Polish gives now (the panel's options, saved in the settings): stamped on the lots it polishes
        /// and on new buildings.</summary>
        public static byte LookParts = PartsAll, LookPainted = 33;

        /// <summary>A lot's look as planner knobs (StitchHouse.Options); null: the settings (a lot polished before looks were saved).</summary>
        private static Dictionary<string, float> OptionsOf(PolishedLot f)
        {
            if (!f.Own) return null;
            bool On(byte bit) => (f.Parts & bit) != 0;
            return new Dictionary<string, float>
            {
                ["lawns"] = On(PartLawns) ? 1f : 0f, ["paintedGardens"] = f.Painted, ["plant"] = On(PartPlanting) ? 1f : 0f,
                ["features"] = On(PartFeatures) ? 1f : 0f, ["pathChance"] = On(PartPaths) ? 45f : 0f, ["alleyChance"] = On(PartPaths) ? 45f : 0f,
                ["rockChance"] = On(PartRocks) ? 12f : 0f, ["paint"] = On(PartPaint) ? 1f : 0f, ["pavedFill"] = On(PartShopPaving) ? 1f : 0f,
            };
        }

        /// <summary>The lot's theme byte as a name (PolishedLot.Theme; 0 = by the zone).</summary>
        public static readonly string[] ThemeNames = { "zone", "garden", "natural", "tidy", "yard" };

        /// <summary>A kind's setting "by zone type": each zone type its own built-in look (PolishLooks.DefaultZoneLook).</summary>
        public const string KindAuto = "auto";

        /// <summary>Settings: the look each kind of building gets: KindAuto (each zone type its own), a look id (every zone of the
        /// kind without a pick of its own) or "off" (new ones of that kind are not polished).</summary>
        private static readonly Dictionary<string, string> s_zoneTheme = new Dictionary<string, string>
        {
            ["home"] = KindAuto, ["shop"] = KindAuto, ["office"] = KindAuto, ["industry"] = KindAuto,
        };

        public static string ZoneTheme(string zone) => s_zoneTheme.TryGetValue(zone ?? "home", out var t) ? t : KindAuto;

        /// <summary>
        /// Settings: the theme for each kind of building. Given to buildings polished from now on (each lot keeps the theme it
        /// was polished with); "Redo polished buildings" gives it to the ones already polished. Never a city-wide change by
        /// itself: a big city would replan thousands of houses.
        /// </summary>
        public static void SetZoneThemes(string homes, string shops, string offices, string industry)
        {
            foreach (var (k, v) in new[] { ("home", homes), ("shop", shops), ("office", offices), ("industry", industry) })
            {
                var t = string.IsNullOrEmpty(v) ? ZoneTheme(k) : v;
                if (ZoneTheme(k) != t) { s_zoneTheme[k] = t; SettingsChanged = true; }
            }
        }

        /// <summary>A setting changed since the polished buildings were last fitted (the status line says so).</summary>
        public static bool SettingsChanged { get; internal set; }

        /// <summary>The theme byte for a building: its zone's look's base theme (Off: its zone type's built-in look's).</summary>
        private byte ThemeByte(Entity prefab)
        {
            var id = LookOfZone(prefab);
            if (id == "off") id = PolishLooks.DefaultZoneLook(ZoneKey(ZoneName(prefab)), ZoneKind(prefab));
            return (byte)Math.Max(1, Array.IndexOf(ThemeNames, LookById(id)?.Theme ?? "garden"));
        }

        /// <summary>What a building prefab is: home, shop, office or industry.</summary>
        private string ZoneKind(Entity prefab)
        {
            if (!EntityManager.HasComponent<SpawnableBuildingData>(prefab)) return "home";
            var zone = EntityManager.GetComponentData<SpawnableBuildingData>(prefab).m_ZonePrefab;
            if (zone == Entity.Null || !EntityManager.HasComponent<ZoneData>(zone)) return "home";
            var zd = EntityManager.GetComponentData<ZoneData>(zone);
            return zd.m_AreaType == global::Game.Zones.AreaType.Residential ? "home" : zd.m_AreaType == global::Game.Zones.AreaType.Commercial ? "shop" : zd.IsOffice() ? "office" : "industry";
        }

        /// <summary>The theme the planner uses for a lot: its own (every lot gets one when it is polished), else its zone's.</summary>
        private string ThemeFor(Entity lot, string zone)
        {
            byte t = ThemeOfLot(lot);
            if (t > 0 && t < ThemeNames.Length) return ThemeNames[t];
            var z = ZoneTheme(zone);
            if (z == "off") return null;
            return LookById(z == KindAuto ? PolishLooks.DefaultZoneLook("", zone) : z)?.Theme ?? "garden";
        }
        /// <summary>Game runner: which parts are edited (both by default).</summary>
        public static bool EditLines = true, EditLawns = true;
        /// <summary>The stitching rules (the game runner can change them live and ask for a redo).</summary>
        public static StitchSettings Settings = new StitchSettings();
        /// <summary>The planner (the game runner's lab op "hotload" swaps in a newer YardStitch without a relaunch).</summary>
        public static Func<IReadOnlyList<StitchHouse>, StitchSettings, StitchPlan> Planner = YardStitch.Plan;
        private static bool s_redo;

        /// <summary>Every house again with the current rules (lines and lawns no longer planned go back to the game's own).</summary>
        public static void Redo() => s_redo = true;

        /// <summary>
        /// The options panel's choices (lawns, painted gardens, planting, paths, rocks, paint) as planner knobs, for every plan
        /// from now on (new buildings, the painter, buildings the game changes). Never a city-wide replan by itself: "Redo
        /// polished buildings" does that, a few houses at a time. A new dictionary: plans on workers read the old one.
        /// </summary>
        public static void SetOptions(Dictionary<string, float> options)
        {
            var extra = new Dictionary<string, float>(Settings.Extra);
            bool changed = false;
            foreach (var kv in options)
                if (!extra.TryGetValue(kv.Key, out var was) || was != kv.Value) { extra[kv.Key] = kv.Value; changed = true; }
            if (!changed) return;
            Settings.Extra = extra;
            SettingsChanged = true;
        }

        // a pass costs ~0.6 ms per house asked (its neighbourhood included): 16 keeps a pass near 10 ms, every 4th frame
        // a pass takes this many changed houses (more when many wait: a whole city polished or redone); describing them is the
        // part on the frame, so a big pass stays small enough to never stutter
        private const int HousesPerPass = 16, HousesPerBigPass = 32;
        // a plot depends on houses up to ~2 lots away (and a ring's shared green on the whole ring): the pass plans with
        // everything within ClusterRadius but edits only houses within CoreRadius of a dirty one, so a house at the edge of
        // a pass (its neighbours not in it) is never planned half-blind and reset
        private const float ClusterRadiusMin = 120f, ClusterRadiusMax = 400f;
        /// <summary>The biggest reach of any look in use (the looks, the polish tool's changed values): a plan looks this far
        /// (plus a margin) around its houses, so far-reaching gardens see what they reach for.</summary>
        public static float ReachInUse = 100f;

        /// <summary>The polish tool's values changed: the reach in use again.</summary>
        public static void NoteReach(IReadOnlyDictionary<string, float> working)
        {
            float r = 0f;
            foreach (var look in Looks) { var v = look.Values(); foreach (var k in PolishLooks.ReachKnobs) if (v.TryGetValue(k, out var x)) r = math.max(r, x); }
            if (working != null) foreach (var k in PolishLooks.ReachKnobs) if (working.TryGetValue(k, out var x)) r = math.max(r, x);
            ReachInUse = r;
        }

        private static float ClusterRadius => math.clamp(ReachInUse + 60f, ClusterRadiusMin, ClusterRadiusMax);
        private const float CoreRadius = 40f;
        // a bulldozed house: the houses this near it are planned again (a neighbour's line on the shared side goes back)
        private const float GoneRadius = 70f;
        private const float Cell = 64f;

        private EntityQuery _changedFences;
        private EntityQuery _houses;
        private EntityQuery _goneHouses;
        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private readonly Dictionary<Entity, Bezier4x3> _written = new Dictionary<Entity, Bezier4x3>();
        private readonly Dictionary<Entity, float2[]> _lawnOriginal = new Dictionary<Entity, float2[]>();
        private readonly Dictionary<Entity, bool> _grassPrefab = new Dictionary<Entity, bool>();
        private readonly Dictionary<Entity, string> _names = new Dictionary<Entity, string>();
        private readonly HashSet<Entity> _dirty = new HashSet<Entity>();
        // the player's own work goes first (the user: "the player wants to see what they're building"): brand-new buildings
        // (never a level-up), lots they outgrew or put back, what they built or bulldozed nearby, and the neighbours those
        // change. Planned before every other waiting house; level-ups and Redo wait behind them.
        private readonly HashSet<Entity> _first = new HashSet<Entity>();
        private readonly HashSet<long> _painted = new HashSet<long>();
        // the planner runs off the frame: a pass gathers the houses here, plans on a worker, and is applied when it is done
        // plans worked out on workers at once, each for its own patch of town (patches far enough apart never edit the same house)
        private readonly List<Pending> _inFlight = new List<Pending>();
        // (half the threads less one, the rest left to the game's own jobs: 7 on 16 threads, where it was 3, and those hardly
        // ever ran at once: passes kept apart by the widest reach of any type, used or not)
        private static readonly int MaxInFlight = math.clamp(Environment.ProcessorCount / 2 - 1, 1, 7);
        // a patch: a waiting house and every waiting house this near it, planned together with the houses around them; a bigger
        // patch while many wait (a whole town outgrown or redone): fewer plans, and a house is planned again as a neighbour of
        // the next patch less often
        private const float PatchRadius = 50f, BigPatchRadius = 110f;
        private const int ManyWaiting = 60;

        /// <summary>A house's fence lane with its place as the game made it (the prefab's curve on the house).</summary>
        private struct LaneRef
        {
            public Entity Lane;
            public float3 A, B;
            public Bezier4x3 Original;
        }

        private sealed class Pending
        {
            public Task<StitchPlan> Task;
            public float2 Seed;
            public List<StitchHouse> Stitch;
            public Dictionary<long, LaneRef> LanesOf;
            public Dictionary<long, Entity> LawnsOf;
            /// <summary>Lane and lawn id → the id of the house they belong to (a house unpolished meanwhile is left alone).</summary>
            public Dictionary<long, long> OwnerOf;
            public HashSet<long> CoreIds;
            /// <summary>The houses this pass took from the waiting ones (asked, and waiting neighbours it edits anyway; the rest of
            /// the core are neighbours it re-plans).</summary>
            public HashSet<long> DoneIds;
            public Dictionary<long, Entity> HouseOf;
            public Dictionary<long, Entity> LawnPrefabOf;
            public int Asked, Cluster;
            /// <summary>A plan of the player's own work: the neighbours it changes go first too.</summary>
            public bool First;
            public Stopwatch Clock;
            /// <summary>How long the planner took on its worker.</summary>
            public double PlanMs;
            /// <summary>Until its samples are in (Task null): what the plan starts with.</summary>
            public Sampling Samples;
            public StitchSettings Settings;
            /// <summary>How far the gardens of this pass reach.</summary>
            public float Reach;
            /// <summary>How far from its seed the houses it edits may stand (its patch and their neighbours): another pass at
            /// once keeps its own that far again away, so no house is in two plans at once.</summary>
            public float Extent;
            /// <summary>The gardens written when it gathered its houses (_writeSerial): what other plans wrote after that, it
            /// did not see (CheckWrites).</summary>
            public int StartSerial;
            public Func<IReadOnlyList<StitchHouse>, StitchSettings, StitchPlan> Planner;
        }
        private int _frame, _errors;
        // totals since the last log line
        private int _pairs, _moved, _hidden, _restored, _lawns, _passes;
        private float _lawnM2;
        private double _ms;

        /// <summary>For the game runner: houses still waiting, or a plan still being worked out.</summary>
        public static bool Busy { get; private set; }
        public static int Waiting { get; private set; }

        /// <summary>For the game runner: totals since the game started.</summary>
        public static int TotalPairs, TotalEdgePairs, TotalMoved, TotalHidden, TotalLawns, TotalPasses;
        public static float TotalLawnM2;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _changedFences = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<global::Game.Net.UtilityLane>(), ComponentType.ReadOnly<Owner>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            // bulldozed (or replaced) buildings: the houses around them are planned again (their fences go back)
            _goneHouses = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            _houses = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Net.SubLane>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            CreateLotQueries();
            CreateAreaQueries();
            CreateSwitchesQuery();
            CreateNearbyQuery();
            CreateBarrierCacheQuery();
            CreateHiddenQuery();
            CreateRebuiltQueries();
        }

        [Preserve]
        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            // another city: everything kept in memory names entities of the old one (their indices are reused)
            ForgetCity();
            ForgetSwitches();
            // a loaded city is as it was saved: nothing to plan until something changes, but the outgrown buildings an older
            // build left empty (NeighbourPolishSystem.Rebuilt)
            StartHeal();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var clock = Stopwatch.StartNew();
            try { UpdateBody(); }
            finally { NoteMain(clock.Elapsed.TotalMilliseconds, ref UpdateMs, ref UpdateMaxMs); }
        }

        private void UpdateBody()
        {
            try
            {
                // this city's switches first: new lots below are flagged by them
                SwitchesUpdate();
            }
            catch (Exception e) { if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish (switches) failed"); }
            try { DropChangedBarrierCells(); }
            catch (Exception e) { if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish (barrier cells) failed"); }
            try
            {
                // the painter's stamps, "Remove all polish", new lots' flags and our new pieces' tags: also with the polish
                // off (putting a lot back never hurts, and a flag given now is used once it is switched on)
                LotsUpdate();
            }
            catch (Exception e) { if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish (lots) failed"); }
            // (our own pieces the game hid: shown again by NeighbourShowSystem, after the game's check of this frame)
            if (!FeatureEnabled || Paused)
            {
                // off: nothing is planned or written. Polished houses the game rebuilds meanwhile (a level-up gives back its
                // own fences) are remembered and fitted again once it is on: switching on never replans the whole city
                try { CollectChanged(); }
                catch (Exception e) { if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish (off) failed"); }
                Busy = _restoreQueue.Count > 0 || s_stamps.Count > 0;
                Waiting = 0;
                return;
            }
            try
            {
                if (s_redo)
                {
                    // "Redo polished buildings" (and the game runner): every yes lot again with the current rules, a patch
                    // of town per frame
                    s_redo = false;
                    int yes = 0;
                    using (var all = _flaggedLots.ToEntityArray(Allocator.Temp))
                        foreach (var e in all)
                            if (EntityManager.GetComponentData<PolishedLot>(e).Yes) { _dirty.Add(e); yes++; }
                    _described.Clear();
                    _gridStale = true;
                    SettingsChanged = false;
                    Mod.Log.Info($"Placemaker neighbourhood polish: every polished lot again, {yes} to look at.");
                }
                CollectChanged();
                HealEmpty();
                SinkOldPieces();
                ++_frame;
                Busy = _dirty.Count > 0 || _inFlight.Count > 0 || _healPending || _restoreQueue.Count > 0 || s_stamps.Count > 0 || NeighbourObjectSystem.Queue.Count > 0 || NeighbourAreaSystem.Queue.Count > 0;
                Waiting = _dirty.Count;
                // one finished plan written per frame (writing is the part on the frame), one new patch started per frame
                StartSampledPlans();
                double finishMs = 0;
                for (int k = 0; k < _inFlight.Count; k++)
                    if (_inFlight[k].Task != null && _inFlight[k].Task.IsCompleted)
                    {
                        var done = _inFlight[k];
                        _inFlight.RemoveAt(k);
                        var finishClock = Stopwatch.StartNew();
                        Finish(done);
                        finishMs = finishClock.Elapsed.TotalMilliseconds;
                        break;
                    }
                // (a big plan written this frame: the next patch is gathered on the next, never both on one frame)
                if (_dirty.Count > 0 && _inFlight.Count < (int)Settings.Knob("maxPlans", MaxInFlight) && finishMs < 6.0) Pass();
                if (_passes > 0 && (_frame & 255) == 0) Report();
            }
            catch (Exception e)
            {
                if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish failed");
                if (_errors >= 5) { FeatureEnabled = false; Mod.Log.Warn("Placemaker neighbourhood polish switched off for this session after repeated errors."); }
            }
        }

        /// <summary>Fence lanes the game created or reset (not the ones we wrote): their houses need stitching.</summary>
        private void CollectChanged()
        {
            try { CollectNewNearby(); }
            catch (Exception e) { if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish (new things nearby) failed"); }
            if (!_goneHouses.IsEmptyIgnoreFilter)
                using (var gone = _goneHouses.ToEntityArray(Allocator.Temp))
                {
                    EnsureGrid();
                    foreach (var g in gone)
                    {
                        var at = EntityManager.GetComponentData<global::Game.Objects.Transform>(g).m_Position.xz;
                        _dirty.Remove(g);
                        _first.Remove(g);
                        _described.Remove(g);
                        for (int dx = -2; dx <= 2; dx++)
                            for (int dz = -2; dz <= 2; dz++)
                                if (_grid.TryGetValue(Key(at + new float2(dx, dz) * Cell), out var near))
                                    foreach (int j in near)
                                    {
                                        var h = _gridHouses[j];
                                        if (h.E != g && math.distance(h.T.m_Position.xz, at) < GoneRadius && EntityManager.Exists(h.E)) { if (_dirty.Add(h.E)) DirtyBy[1]++; _first.Add(h.E); }
                                    }
                    }
                    _gridStale = true;
                }
            if (!_changedFences.IsEmptyIgnoreFilter)
            using (var lanes = _changedFences.ToEntityArray(Allocator.Temp))
                foreach (var lane in lanes)
                {
                    var owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                    if (owner == Entity.Null || !EntityManager.HasComponent<Building>(owner)) continue;
                    var curve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    if (_written.TryGetValue(lane, out var w) && Same(curve, w)) continue;
                    if (!IsFence(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab)) continue;
                    // marked Updated but where it was, on a house that is still the same building: the game only looked at it
                    // again (it hides a fence another one lies along, and says so with Updated). Planning its house again for
                    // that set off a cascade along a street, each plan's fences marking the next house's (2 148 plans for 244
                    // houses). A fence that moved, a new or rebuilt house, a level-up (a new prefab) are still planned again.
                    if (_laneCurve.TryGetValue(lane, out var known) && SameXZ(curve, known)
                        && _prefabSeen.TryGetValue(owner, out var seenPrefab) && seenPrefab == EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab)
                    { DirtySkipped++; continue; }
                    // (why, for the game runner: a lane never seen, one the game moved, or one where it was on another building)
                    LinesChangedHow[!_laneCurve.TryGetValue(lane, out var seen) ? 0 : !SameXZ(curve, seen) ? (_written.ContainsKey(lane) ? 1 : 2) : 3]++;
                    _written.Remove(lane);
                    if (_dirty.Add(owner)) DirtyBy[0]++;
                }
            // buildings the game finished or rebuilt (fences or not) and pieces it took away (after the lanes: a house planned
            // again anyway is not refilled piece by piece)
            try { CollectRebuilt(); }
            catch (Exception e) { if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish (rebuilt buildings) failed"); }
        }

        // ───────────── one pass: a batch of dirty houses with their neighbourhoods ─────────────

        private struct House
        {
            public Entity E;
            public global::Game.Objects.Transform T;
            public Entity Prefab;
            public int2 Lot;
        }

        /// <summary>Game runner (lab op "polishDump"): every finished house in a box as the planner sees it, for offline replays.</summary>
        public List<StitchHouse> DescribeIn(float2 min, float2 max)
        {
            var list = new List<StitchHouse>();
            using (var all = _houses.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(e)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<SpawnableBuildingData>(prefab) || !EntityManager.HasComponent<BuildingData>(prefab)) continue;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    if (math.any(t.m_Position.xz < min) || math.any(t.m_Position.xz > max)) continue;
                    var sh = Describe(new House { E = e, T = t, Prefab = prefab, Lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize }, null, null);
                    sh.Polished = IsYes(e);
                    list.Add(sh);
                }
            return list;
        }

        // every finished growable in a coarse grid, kept between passes: rebuilt when buildings come or go (a city of 100 000
        // lots is read once, not on every pass)
        private List<House> _gridHouses;
        private Dictionary<long, List<int>> _grid;
        private Dictionary<Entity, int> _gridIndex;
        private bool _gridStale = true;
        private int _gridBuilt;
        // what each house looks like to the planner, kept until the house changes (or we gave it something new)
        private readonly Dictionary<Entity, Described> _described = new Dictionary<Entity, Described>();

        private sealed class Described
        {
            public StitchHouse House;
            public List<(long Id, LaneRef Lane)> Lanes;
            public List<(long Id, Entity Lawn)> Lawns;
            public Entity LawnPrefab;
        }

        private void EnsureGrid()
        {
            if (!_gridStale && _grid != null && _frame - _gridBuilt < 3600) return;
            _gridStale = false;
            _gridBuilt = _frame;
            var houses = new List<House>();
            var grid = new Dictionary<long, List<int>>();
            using (var all = _houses.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(e)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<SpawnableBuildingData>(prefab) || !EntityManager.HasComponent<BuildingData>(prefab)) continue;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    houses.Add(new House { E = e, T = t, Prefab = prefab, Lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize });
                    long key = Key(t.m_Position.xz);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(houses.Count - 1);
                }
            var index = new Dictionary<Entity, int>();
            for (int i = 0; i < houses.Count; i++) index[houses[i].E] = i;
            _gridHouses = houses;
            _grid = grid;
            _gridIndex = index;
        }

        private void Pass()
        {
            var sw = Stopwatch.StartNew();
            _passes++;
            // a changed house the grid does not know (just built): the grid again
            foreach (var e in _dirty) if (_gridIndex == null || !_gridIndex.ContainsKey(e)) { _gridStale = true; break; }
            EnsureGrid();
            PassPart(0, sw);
            var houses = _gridHouses;
            var grid = _grid;
            var index = _gridIndex;
            bool many = _dirty.Count > ManyWaiting;
            int batch = many ? HousesPerBigPass : HousesPerPass;
            float patch = many ? Settings.Knob("bigPatch", BigPatchRadius) : PatchRadius;

            // an unpolished lot is never planned itself: a change of it (rebuilt, levelled up, just unpolished) re-plans the
            // polished lots around it
            List<Entity> noLots = null;
            foreach (var e in _dirty) if (index.ContainsKey(e) && !IsYes(e)) (noLots ??= new List<Entity>()).Add(e);
            if (noLots != null)
                foreach (var e in noLots)
                {
                    _dirty.Remove(e);
                    _described.Remove(e);
                    NeighboursDirty(e, houses[index[e]].T.m_Position.xz, _first.Remove(e));
                }

            // this pass: one patch of town. A waiting house far enough from the patches still being planned, and every
            // waiting house near it (a compact patch: its neighbourhood is small, so its plan is quick)
            var cluster = new HashSet<int>();
            var core = new HashSet<int>();
            var done = new List<Entity>();
            int seed = -1;
            var unknown = new List<Entity>();
            // Plans at once: no house is in two of them (their patches and neighbours apart), and the one finishing later is
            // planned again if its gardens come near gardens another wrote meanwhile (CheckWrites): no garden ever lies over
            // another, every fence closes against what really stands. Before, plans kept apart by the reach of both: about
            // 490 m with the looks as they ship, one plan at a time in a town (on 20 Oct, 190 m and nothing checked). (Kept
            // apart by the houses each edits, 30 m, they met far more often: twice the plans in the game runner)
            float extent = patch + CoreRadius;
            float Gap(float2 at)
            {
                float gap = float.MaxValue;
                foreach (var f in _inFlight) gap = math.min(gap, math.distance(f.Seed, at) - f.Extent - extent - 10f);
                return gap;
            }
            // while a whole town waits, a pass edits only waiting houses near its own (knob coreAll 0): the ones planned
            // already keep their gardens, the patches meet on them
            bool onlyWaiting = many && Settings.Knob("coreAll", 1f) < 0.5f;
            // a patch from a seed: the waiting houses near it (done), and every yes house near those (core: only yes lots are
            // edited)
            void Patch(int s)
            {
                var seedAt0 = houses[s].T.m_Position.xz;
                int patchCells = (int)math.ceil(patch / Cell);
                for (int dx = -patchCells; dx <= patchCells; dx++)
                    for (int dz = -patchCells; dz <= patchCells; dz++)
                        if (grid.TryGetValue(Key(seedAt0 + new float2(dx, dz) * Cell), out var near0))
                            foreach (int j in near0)
                                if (done.Count < batch && _dirty.Contains(houses[j].E) && math.distance(houses[j].T.m_Position.xz, seedAt0) <= patch) done.Add(houses[j].E);
                if (!done.Contains(houses[s].E)) done.Add(houses[s].E);
                foreach (var e in done)
                {
                    if (!index.TryGetValue(e, out int i)) continue;
                    var p = houses[i].T.m_Position.xz;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                            if (grid.TryGetValue(Key(p + new float2(dx, dz) * Cell), out var near))
                                foreach (int j in near)
                                    if (math.distance(houses[j].T.m_Position.xz, p) <= CoreRadius && IsYes(houses[j].E) && (!onlyWaiting || _dirty.Contains(houses[j].E))) core.Add(j);
                }
            }
            // the player's own work first; a house still being built waits in _first until its lanes come (then it is dirty)
            foreach (var e in _first)
                if (_dirty.Contains(e) && index.TryGetValue(e, out int i) && Gap(houses[i].T.m_Position.xz) >= 0f) { seed = i; break; }
            if (seed < 0)
            {
                // then, while others are planned, the waiting house furthest from them (their gardens seldom meet)
                float best = -1f;
                foreach (var e in _dirty)
                {
                    if (!index.TryGetValue(e, out int i)) { unknown.Add(e); continue; }   // gone, or still being built: its lanes come back as Created later
                    float gap = Gap(houses[i].T.m_Position.xz);
                    if (gap < 0f || gap <= best) continue;
                    seed = i;
                    best = gap;
                    if (_inFlight.Count == 0) break;
                }
            }
            if (seed >= 0) Patch(seed);
            foreach (var e in unknown) _dirty.Remove(e);
            TotalDropped += unknown.Count;
            if (seed < 0) return;
            var seedAt = houses[seed].T.m_Position.xz;
            // (as far around the edited houses as their gardens reach: the pass's own reach, not the widest of every type in
            // the city: a type at 500 m nobody used made every pass take in 400 m of town)
            float coreReach = 0f;
            foreach (int j in core) coreReach = math.max(coreReach, LotReach(houses[j].E));
            float clusterRadius = math.clamp(coreReach + 60f, ClusterRadiusMin, ClusterRadiusMax);
            foreach (var e in done)
            {
                if (!index.TryGetValue(e, out int i)) continue;
                var p = houses[i].T.m_Position.xz;
                int cells = (int)math.ceil(clusterRadius / Cell) + 1;
                for (int dx = -cells; dx <= cells; dx++)
                    for (int dz = -cells; dz <= cells; dz++)
                        if (grid.TryGetValue(Key(p + new float2(dx, dz) * Cell), out var near))
                            foreach (int j in near)
                                if (math.distance(houses[j].T.m_Position.xz, p) <= clusterRadius) cluster.Add(j);
            }
            // a waiting house this plan edits anyway (a neighbour of the asked ones) is planned now, not again on its own later:
            // a town outgrown at once planned most houses twice or more, once as a neighbour and once asked
            var taken = new List<Entity>(done);
            foreach (int j in core) if (_dirty.Contains(houses[j].E) && !done.Contains(houses[j].E)) taken.Add(houses[j].E);
            bool first = false;
            foreach (var e in taken) { _dirty.Remove(e); _described.Remove(e); if (_first.Remove(e)) first = true; }
            TotalTaken += taken.Count;
            PassPart(1, sw);
            if (cluster.Count == 0) return;

            var stitch = new List<StitchHouse>();
            var lanesOf = new Dictionary<long, LaneRef>();
            var lawnsOf = new Dictionary<long, Entity>();
            var ownerOf = new Dictionary<long, long>();
            _houseOf.Clear();
            _lawnPrefabOf.Clear();
            var coreIds = new HashSet<long>();
            void Add(int i)
            {
                var e = houses[i].E;
                if (!_described.TryGetValue(e, out var d) || !EntityManager.Exists(e))
                {
                    var describeClock = Stopwatch.StartNew();
                    var lanes = new Dictionary<long, LaneRef>();
                    var lawns = new Dictionary<long, Entity>();
                    var sh = Describe(houses[i], lanes, lawns);
                    d = new Described
                    {
                        House = sh, Lanes = lanes.Select(kv => (kv.Key, kv.Value)).ToList(),
                        Lawns = lawns.Select(kv => (kv.Key, kv.Value)).ToList(), LawnPrefab = _lawnPrefabOf.TryGetValue(sh.Id, out var lp) ? lp : Entity.Null,
                    };
                    _described[e] = d;
                    PassPartMs[2] += describeClock.Elapsed.TotalMilliseconds;
                }
                bool isCore = core.Contains(i);
                _houseOf[d.House.Id] = e;
                if (d.LawnPrefab != Entity.Null) _lawnPrefabOf[d.House.Id] = d.LawnPrefab;
                if (isCore)
                {
                    foreach (var (id, lane) in d.Lanes) { lanesOf[id] = lane; ownerOf[id] = d.House.Id; }
                    foreach (var (id, lawn) in d.Lawns) { lawnsOf[id] = lawn; ownerOf[id] = d.House.Id; }
                    coreIds.Add(d.House.Id);
                }
                var flag = EntityManager.HasComponent<PolishedLot>(e) ? EntityManager.GetComponentData<PolishedLot>(e) : default;
                var (options, theme) = flag.Yes ? PlannerLook(e, flag, d.House.Zone) : (null, ThemeFor(e, d.House.Zone));
                var copy = Copy(d.House, isCore, flag.Yes, theme, options);
                // a polished neighbour this pass does not edit keeps the garden it was last given (kept with the house, so also
                // after a load) and its fences and hedges as they stand: fixed for this plan, the edited gardens meet them
                if (!isCore && flag.Yes)
                {
                    var garden = GardenOf(e);
                    if (garden != null) { copy.Garden = garden; copy.Standing = StandingOf(e); }
                }
                stitch.Add(copy);
            }
            foreach (int i in cluster) Add(i);
            // the roads, paths, tracks and other buildings around the patch: never joined across, never reached onto, met
            // around every house as far as any garden here reaches (the looks in use, these lots' own changes, the tool's
            // values), plus a margin: nothing further out changes a plan (it was the whole cluster radius again around each)
            // (the gardens this pass edits: a neighbour it only plans around keeps the garden it has)
            float reach = coreReach;
            int nearCount = stitch.Count;
            // polished houses further out whose gardens reach into what this pass may give its houses: fixed for the plan, so
            // the gardens it edits keep off them. A garden reaches further than the cluster radius (up to 500 m), and two big
            // gardens planned in different passes lay over each other, the fences of one standing in the other (the game
            // runner with every reach at its maximum: 18 gardens over each other, 269 fences inside another's garden)
            {
                var clo = new float2(float.MaxValue);
                var chi = new float2(float.MinValue);
                foreach (var h in stitch)
                {
                    if (!h.Edit) continue;
                    float r = math.length(h.HalfLot) + reach + 10f;
                    clo = math.min(clo, h.Position - r);
                    chi = math.max(chi, h.Position + r);
                }
                if (clo.x <= chi.x)
                {
                    var far = new List<int>();
                    foreach (var (house, glo, ghi) in GardenBoxes())
                    {
                        if (ghi.x < clo.x || glo.x > chi.x || ghi.y < clo.y || glo.y > chi.y) continue;
                        if (!index.TryGetValue(house, out int j) || cluster.Contains(j) || !IsYes(house)) continue;
                        far.Add(j);
                    }
                    foreach (int j in far) { cluster.Add(j); Add(j); }
                    FarGardens += far.Count;
                }
            }
            var planner = Planner;
            PassPart(3, sw);
            var lo = new float2(float.MaxValue);
            var hi = new float2(float.MinValue);
            // (around the houses of the neighbourhood: a far house is only there with its garden as it stands)
            foreach (var h in stitch.Take(nearCount))
            {
                float r = math.length(h.HalfLot) + reach + 20f;
                lo = math.min(lo, h.Position - r);
                hi = math.max(hi, h.Position + r);
            }
            // the water is looked for a water gap further out (the widest of the looks here): a garden keeps that far off it
            float wetGap = Settings.Knob("waterGap", PolishLooks.Defaults["waterGap"]);
            foreach (var h in stitch) if (h.Options != null && h.Options.TryGetValue("waterGap", out var w)) wetGap = math.max(wetGap, w);
            var kinds = new List<int>();
            var groups = new List<long>();
            var heights = new List<float>();
            var planned = new HashSet<Entity>();
            foreach (var h in stitch) if (_houseOf.TryGetValue(h.Id, out var he)) planned.Add(he);
            var settings = Settings.WithBarriers(stitch.Count > 0 ? BarriersIn(lo, hi, kinds, groups, planned, heights) : null, kinds, groups, heights);
            PassPart(4, sw);
            // the ground (gardens stop where it gets too steep or too high) and the water: read by jobs, the plan starts when
            // they are done (StartSampledPlans)
            var pend = new Pending
            {
                Stitch = stitch, LanesOf = lanesOf, LawnsOf = lawnsOf, OwnerOf = ownerOf, CoreIds = coreIds, DoneIds = new HashSet<long>(taken.Select(Id)), HouseOf = new Dictionary<long, Entity>(_houseOf),
                LawnPrefabOf = new Dictionary<long, Entity>(_lawnPrefabOf), Asked = done.Count, Cluster = cluster.Count, Clock = sw, First = first, Seed = seedAt,
                Settings = settings, Planner = planner, Reach = reach, Extent = extent, StartSerial = _writeSerial, Samples = StartSampling(lo, hi, math.clamp(wetGap, 0f, 300f) + 4f),
            };
            PassPart(5, sw);
            _inFlight.Add(pend);
            NoteMain(sw.Elapsed.TotalMilliseconds, ref PassMainMs, ref PassMainMaxMs);
        }

        /// <summary>Passes whose ground and water samples are in: their plans start on a worker.</summary>
        private void StartSampledPlans()
        {
            foreach (var pend in _inFlight)
            {
                if (pend.Task != null || pend.Samples == null || !pend.Samples.Handle.IsCompleted) continue;
                var clock = Stopwatch.StartNew();
                var smp = pend.Samples;
                smp.Handle.Complete();
                var heights = smp.Heights.ToArray();
                var wet = smp.HasWater ? smp.Wet.ToArray() : null;
                smp.Dispose();
                pend.Samples = null;
                var settings = pend.Settings;
                var stitch = pend.Stitch;
                var planner = pend.Planner;
                bool dump = PassDumpDir != null && s_passDumps < PassDumpMax;
                if (dump) s_passDumps++;
                int dumpNo = s_passDumps;
                pend.Task = Task.Run(() =>
                {
                    var planClock = Stopwatch.StartNew();
                    var (raster, water) = Sampled(smp.Min, smp.Nx, smp.Ny, smp.Cell, heights, wet, smp.WetMin, smp.WetMax, smp.WetNx, smp.WetNz);
                    settings.Ground = raster.Sample;
                    // water last, as BarriersIn added it (one group each)
                    if (settings.Barriers != null)
                        for (int k = 0; k < water.Count && settings.Barriers.Count < MaxBarriers; k++)
                        {
                            settings.Barriers.Add(water[k]);
                            settings.BarrierKinds?.Add(2);
                            settings.BarrierGroups?.Add(long.MinValue / 2 + k);
                            settings.BarrierHeights?.Add(0f);
                        }
                    if (dump) DumpPass(stitch, settings, raster, dumpNo);
                    var result = planner(stitch, settings);
                    pend.PlanMs = planClock.Elapsed.TotalMilliseconds;
                    return result;
                });
                PassPartMs[6] += clock.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>Passes still waiting for samples: their jobs finished and their buffers freed (a city unloaded).</summary>
        private void DropSampling()
        {
            foreach (var pend in _inFlight) { pend.Samples?.Dispose(); pend.Samples = null; }
        }

        /// <summary>A plan the worker finished: written into the houses (anything gone since is skipped).</summary>
        private void Finish(Pending pend)
        {
            var finishClock = Stopwatch.StartNew();
            try { FinishPlan(pend); }
            finally
            {
                NoteMain(finishClock.Elapsed.TotalMilliseconds, ref FinishMainMs, ref FinishMainMaxMs);
                if (pend.Task.IsCompleted && !pend.Task.IsFaulted) { PlanWorkerMs += pend.PlanMs; PlanWorkerMaxMs = Math.Max(PlanWorkerMaxMs, pend.PlanMs); PlansTimed++; }
            }
        }

        private void FinishPlan(Pending pend)
        {
            var task = pend.Task;
            if (task.IsFaulted || task.IsCanceled || pend == null)
            {
                if (_errors++ < 5) Mod.Log.Warn("Placemaker neighbourhood polish: a plan failed: " + task.Exception?.GetBaseException().Message);
                return;
            }
            var plan = task.Result;
            var stitch = pend.Stitch;
            var lanesOf = pend.LanesOf;
            var lawnsOf = pend.LawnsOf;
            var coreIds = pend.CoreIds;
            // gardens another plan wrote meanwhile near these: planned again, nothing of this plan written
            if (!CheckWrites(pend, plan)) return;
            // the gardens written now are the ones the next passes keep off; neighbours whose garden lies over a lot here
            // are planned again
            // (a house the plan left as it was: its garden is its lot)
            _writeSerial++;
            foreach (var id in coreIds)
                if (pend.HouseOf.TryGetValue(id, out var he) && EntityManager.Exists(he) && !EntityManager.HasComponent<Deleted>(he) && IsYes(he))
                {
                    var garden = plan.Plots.TryGetValue(id, out var plotNow) ? plotNow : LotRing(stitch.Find(h => h.Id == id));
                    if (garden == null) continue;
                    NoteWrite(id, GardenOf(he) ?? LotRing(stitch.Find(h => h.Id == id)), garden);
                    KeepGarden(he, garden);
                }
            int clashes = 0;
            foreach (var id in plan.GardenClashes)
                if (pend.HouseOf.TryGetValue(id, out var ce) && EntityManager.Exists(ce) && IsYes(ce))
                {
                    if (!ClashAgain(ce)) continue;
                    if (_dirty.Add(ce)) { clashes++; DirtyBy[3]++; }
                    if (pend.First) _first.Add(ce);
                }
            if (clashes > 0) Mod.Log.Info($"Placemaker neighbourhood polish: {clashes} gardens over a new lot planned again");
            var sw = pend.Clock;
            int asked = pend.Asked, clusterCount = pend.Cluster;
            _houseOf.Clear();
            foreach (var kv in pend.HouseOf) _houseOf[kv.Key] = kv.Value;
            // a house unpolished (or gone) while its plan was worked out keeps what Unpolish gave it back
            var skip = new HashSet<long>();
            foreach (var id in coreIds) if (!_houseOf.TryGetValue(id, out var ph) || !EntityManager.Exists(ph) || !IsYes(ph)) skip.Add(id);
            bool Skipped(long part) => pend.OwnerOf.TryGetValue(part, out var house) && skip.Contains(house);
            _lawnPrefabOf.Clear();
            foreach (var kv in pend.LawnPrefabOf) _lawnPrefabOf[kv.Key] = kv.Value;
            // the first passes of a city, and the passes right after something was built near gardens, are logged in full
            bool logPass = TotalPasses < 6 || s_logPasses > 0;
            if (s_logPasses > 0) s_logPasses--;
            if (logPass)
                // (one write for all of them: a call per note was most of the frame a big plan was written on)
                if (plan.Notes.Count > 0) Mod.Log.Info("Placemaker neighbourhood polish: " + string.Join("\nPlacemaker neighbourhood polish: ", plan.Notes));
            if (logPass)
                Mod.Log.Info($"Placemaker neighbourhood polish pass {TotalPasses + 1}: {asked} houses asked, {clusterCount} in their neighbourhoods ({stitch.Count(h => !h.Polished)} unpolished), {plan.Pairs.Count} pairs ({plan.Pairs.Count(p => p.Edge)} against an unpolished lot), "
                    + $"{plan.MoveLines.Count} lines to move, {plan.HideLines.Count} to hide, {plan.Lawns.Count} lawns{(EditLines ? "" : " (lines off)")}{(EditLawns ? "" : " (lawns off)")}{(skip.Count > 0 ? $", {skip.Count} unpolished meanwhile" : "")}");

            var heights = _terrain.GetHeightData(true);
            float3 Ground(float2 p) => new float3(p.x, TerrainUtils.SampleHeight(ref heights, new float3(p.x, 0f, p.y)), p.y);
            foreach (var kv in lanesOf)
            {
                if (!EditLines) break;
                if (Skipped(kv.Key)) continue;
                var lr = kv.Value;
                var lane = lr.Lane;
                if (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane)) continue;   // gone while it was planned
                var cur = EntityManager.GetComponentData<Curve>(lane);
                Bezier4x3 want;
                bool ours = true;
                if (plan.HideLines.Contains(kv.Key))
                {
                    // a 2 cm stub: drawn as nothing (lanes under 0.1 m are skipped) and never a zero tangent. 8 m under the
                    // ground: Net.OverrideSystem cuts a fence lane where another one lies along it within about 2 m (3D), and a
                    // stub on the border cut a gap into the line it doubled whenever its kind ranked higher
                    var mid = (lr.A + lr.B) * 0.5f - new float3(0f, 8f, 0f);
                    var dir = math.normalizesafe(lr.B - lr.A, new float3(1f, 0f, 0f)) * 0.01f;
                    want = NetUtils.StraightCurve(mid - dir, mid + dir);
                    _hidden++;
                }
                else if (plan.MoveLines.TryGetValue(kv.Key, out var m)) { want = NetUtils.StraightCurve(Ground(m.A), Ground(m.B)); _moved++; }
                else if (_written.ContainsKey(lane) || !IsOriginal(cur.m_Bezier, lr.Original)) { want = lr.Original; ours = false; _restored++; }   // no longer stitched (a neighbour left)
                else continue;
                if (Same(cur.m_Bezier, want)) { if (ours) _written[lane] = want; else _written.Remove(lane); continue; }
                cur.m_Bezier = want;
                cur.m_Length = MathUtils.Length(want);
                EntityManager.SetComponentData(lane, cur);
                EntityManager.AddComponent<Updated>(lane);
                _laneCurve[lane] = want;
                if (ours) _written[lane] = want; else _written.Remove(lane);
            }
            foreach (var kv in lawnsOf)
            {
                if (!EditLawns) break;
                if (Skipped(kv.Key)) continue;
                var lawn = kv.Value;
                if (!EntityManager.Exists(lawn) || EntityManager.HasComponent<Deleted>(lawn)) continue;
                if (plan.Lawns.TryGetValue(kv.Key, out var grown))
                {
                    // written next frame before Areas.GeometrySystem (Modification2B), which re-triangulates Updated areas:
                    // nodes changed here, after it ran, would leave triangles pointing past the new ring (native crash).
                    // The lawn's own nodes are kept with it the first time (PolishLawnOriginal), so it can always go back.
                    var nodes = new float3[grown.Length];
                    for (int i = 0; i < grown.Length; i++) nodes[i] = Ground(grown[i]);
                    NeighbourLawnSystem.Queue.Add(new NeighbourLawnSystem.Write { Lawn = lawn, Nodes = nodes });
                    _lawns++;
                }
                else if (EntityManager.HasBuffer<PolishLawnOriginal>(lawn))
                    NeighbourLawnSystem.Queue.Add(new NeighbourLawnSystem.Write { Lawn = lawn, Restore = true });   // no longer grown: as the game made it
            }
            // ground paint: once per stitched pair (painting again would change nothing, but costs a raster)
            foreach (var paint in plan.Paints)
            {
                long key = (long)math.round(paint.Ring[0].x * 4f) * 73856093L ^ (long)math.round(paint.Ring[0].z * 4f) * 19349663L ^ paint.Layer;
                if (_painted.Add(key)) NeighbourPaintSystem.Queue.Add(paint);
            }
            // what each house is given: planted beds and their bushes, paths and paving, lawns of its own; what it got from an
            // earlier plan (its own pieces, tagged PolishPiece) and no longer has (its garden changed: a neighbour came or went)
            // is taken away
            if (EditLawns)
            {
                var usual = _lawnPrefabOf.Values.GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                var want = new Dictionary<long, List<PieceIdentity.Wish>>();
                void Want(long house, string name, float2 at, float yaw = float.NaN, float2[] ring = null)
                {
                    if (!want.TryGetValue(house, out var l)) want[house] = l = new List<PieceIdentity.Wish>();
                    l.Add(new PieceIdentity.Wish { Name = name, At = at, Yaw = yaw, Ring = ring });
                }
                var newLawns = new List<(long House, float2[] Ring, Entity Prefab)>();
                foreach (var (house, ring) in plan.NewLawns)
                {
                    var prefab = _lawnPrefabOf.TryGetValue(house, out var own) ? own : usual;
                    if (prefab == Entity.Null) continue;
                    newLawns.Add((house, ring, prefab));
                    Want(house, NameOf(prefab), Centre(ring), ring: ring);
                }
                foreach (var (house, ring) in plan.Beds) Want(house, "Forestry Surface 01", Centre(ring), ring: ring);
                foreach (var (house, ring, surface) in plan.Paths) Want(house, surface, Centre(ring), ring: ring);
                foreach (var (house, at, yaw, kind) in plan.Bushes) if (kind >= 0 && kind < YardStitch.BushKinds.Length) Want(house, YardStitch.BushKinds[kind], at, yaw);
                foreach (var (house, at, yaw, name) in plan.FencePieces) Want(house, NeighbourObjectSystem.PieceName(name), at, yaw);
                // a piece the house has that this plan still wants (the same kind, spot and angle; a surface: the same outline)
                // stays as it is; any other goes. The surface and object systems make a wanted piece only when no such one is
                // there (the same rule): before, a piece kept within 1.2 m but made again unless within 0.5 m doubled up, and
                // the game showed one or the other ("flickering, indecisiveness")
                foreach (var id in coreIds)
                {
                    if (skip.Contains(id) || !_houseOf.TryGetValue(id, out var house)) continue;
                    var now = want.TryGetValue(id, out var w) ? w : new List<PieceIdentity.Wish>();
                    var haves = OwnPieceShapes(house, PolishPieceKind.Garden);
                    if (haves.Count > 0 && now.Count == 0) NoteEmptied(id, house, pend.DoneIds != null && pend.DoneIds.Contains(id), plan.Plots.ContainsKey(id), haves.Count, stitch.Find(h => h.Id == id)?.Zone);
                    foreach (var have in haves)
                        if (!now.Exists(x => PieceIdentity.Same(x, have))) NeighbourDeleteSystem.Queue.Add(have.Entity);
                }
                foreach (var (house, ring, prefab) in newLawns)
                    if (coreIds.Contains(house) && !skip.Contains(house) && _houseOf.TryGetValue(house, out var owner))
                    {
                        NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Prefab = prefab, Name = "Grass Surface 01", Ring = ring, Owner = owner });
                        _newLawns++;
                    }
                foreach (var (house, ring) in plan.Beds)
                    if (coreIds.Contains(house) && !skip.Contains(house) && _houseOf.TryGetValue(house, out var owner))
                        NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Name = "Forestry Surface 01", Ring = ring, Owner = owner });
                foreach (var (house, ring, surface) in plan.Paths)
                    if (coreIds.Contains(house) && !skip.Contains(house) && _houseOf.TryGetValue(house, out var owner))
                        NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Name = surface, Ring = ring, Owner = owner });
                foreach (var (house, at, yaw, kind) in plan.Bushes)
                    if (coreIds.Contains(house) && !skip.Contains(house) && _houseOf.TryGetValue(house, out var owner) && kind >= 0 && kind < YardStitch.BushKinds.Length)
                        NeighbourObjectSystem.Queue.Add(new NeighbourObjectSystem.Request { Name = YardStitch.BushKinds[kind], Position = at, Yaw = yaw, Owner = owner });
                // the fence and hedge pieces closing the gardens
                foreach (var (house, at, yaw, name) in plan.FencePieces)
                    if (coreIds.Contains(house) && !skip.Contains(house) && _houseOf.TryGetValue(house, out var owner))
                        NeighbourObjectSystem.Queue.Add(new NeighbourObjectSystem.Request
                        {
                            Name = NeighbourObjectSystem.PieceName(name), Position = at, Yaw = yaw, Owner = owner,
                            Seed = PieceSeed(owner, name),   // one look for the whole garden
                        });
            }
            // shared land in the middle of a ring (polished all round): a lawn with a mulch bed in its centre, owned by the
            // ring's nearest house (bulldozed or unpolished with it; the next pass makes it again with the ring as it is then)
            foreach (var common in plan.Commons)
            {
                // already half a metre in from the fences and cleaned into a shape the game draws (YardStitch.Commons)
                var commonOwner = NearestHouse(stitch, common);
                if (commonOwner == Entity.Null) continue;
                NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Name = Settings.Extra.ContainsKey("noCommonLawn") ? "" : "Grass Surface 01", Ring = common, Owner = commonOwner, Kind = PolishPieceKind.Common });
                var c = float2.zero;
                foreach (var p in common) c += p;
                c /= common.Length;
                float r = math.clamp(math.sqrt(Placemaker.Core.Geometry.Poly.Area(common)) / 6f, 2f, 5f);
                var bed = new float2[10];
                for (int i = 0; i < bed.Length; i++) { float ang = i * math.PI * 2f / bed.Length; bed[i] = c + new float2(math.cos(ang), math.sin(ang)) * r * (i % 2 == 0 ? 1f : 0.82f); }
                NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Name = "Forestry Surface 01", Ring = bed, Owner = commonOwner, Kind = PolishPieceKind.Common });
            }
            _pairs += plan.Pairs.Count;
            _lawnM2 += plan.LawnAddedM2;
            TotalPairs += plan.Pairs.Count; TotalEdgePairs += plan.Pairs.Count(p => p.Edge); TotalMoved = TotalMoved + plan.MoveLines.Count; TotalHidden += plan.HideLines.Count; TotalLawns += plan.Lawns.Count; TotalLawnM2 += plan.LawnAddedM2; TotalPasses++;
            _ms += sw.Elapsed.TotalMilliseconds;
            Prune();
        }

        // gardens planned again for lying over a lot (GardenClashes), and when: a garden whose new plan still lies over it would
        // be planned again for ever, every third of a second, its pieces made and taken away each time (seen in the user's city
        // Brecon: 620 times in 150 s, about 45 fence pieces a second, fences and outlines flickering)
        private readonly Dictionary<Entity, (int Since, int Times)> _clashed = new Dictionary<Entity, (int, int)>();
        private const int ClashWindow = 3600, ClashMax = 3;
        /// <summary>For the game runner: clash re-plans held back (the same garden again and again).</summary>
        public static int TotalClashesHeld;

        /// <summary>The garden may be planned again for lying over a lot: not a fourth time within a minute.</summary>
        private bool ClashAgain(Entity house)
        {
            if (_clashed.Count > 4096) _clashed.Clear();
            var (since, times) = _clashed.TryGetValue(house, out var c) && LotFrame - c.Since < ClashWindow ? c : (LotFrame, 0);
            if (times >= ClashMax)
            {
                if (TotalClashesHeld++ < 20) Mod.Log.Info($"Placemaker neighbourhood polish: house {house.Index}'s garden still lies over a lot after {times} plans: left as it is for a while");
                return false;
            }
            _clashed[house] = (since, times + 1);
            return true;
        }

        /// <summary>A house as YardStitch sees it: its lot, its fence lines at their ORIGINAL (prefab) places, its lawns as first seen.</summary>
        private StitchHouse Describe(House h, Dictionary<long, LaneRef> lanesOf, Dictionary<long, Entity> lawnsOf)
        {
            var fwd = math.mul(h.T.m_Rotation, new float3(0f, 0f, 1f));
            var sh = new StitchHouse
            {
                Id = Id(h.E), Position = h.T.m_Position.xz, Forward = math.normalizesafe(fwd.xz, new float2(0f, 1f)), HalfLot = (float2)h.Lot * 4f,
            };
            // the house itself: its mesh's ground outline (no moved fence may cross it)
            if (ModelBounds(h.Prefab, out var bnd))
            {
                sh.Footprint = new[]
                {
                    global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.min.x, 0f, bnd.min.z)).xz, global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.max.x, 0f, bnd.min.z)).xz,
                    global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.max.x, 0f, bnd.max.z)).xz, global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(bnd.min.x, 0f, bnd.max.z)).xz,
                };
            }
            bool hasPrefabLanes = EntityManager.HasBuffer<global::Game.Prefabs.SubLane>(h.Prefab);
            var prefabLanes = hasPrefabLanes ? EntityManager.GetBuffer<global::Game.Prefabs.SubLane>(h.Prefab, true) : default;
            foreach (var sl in EntityManager.GetBuffer<global::Game.Net.SubLane>(h.E, true))
            {
                var lane = sl.m_SubLane;
                if (!EntityManager.Exists(lane) || !EntityManager.HasComponent<global::Game.Net.UtilityLane>(lane) || !EntityManager.HasComponent<Lane>(lane)) continue;
                var lanePrefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                if (!IsFence(lanePrefab)) continue;
                // the original curve: the prefab's sub-lane this lane was made from (LaneSystem: middle node = 65535 - index)
                float3 a, b;
                Bezier4x3 original;
                int k = 65535 - EntityManager.GetComponentData<Lane>(lane).m_MiddleNode.GetLaneIndex();
                if (hasPrefabLanes && k >= 0 && k < prefabLanes.Length)
                {
                    // as LaneSystem places it (every control point through the house transform)
                    var c = prefabLanes[k].m_Curve;
                    original = new Bezier4x3(global::Game.Objects.ObjectUtils.LocalToWorld(h.T, c.a), global::Game.Objects.ObjectUtils.LocalToWorld(h.T, c.b),
                        global::Game.Objects.ObjectUtils.LocalToWorld(h.T, c.c), global::Game.Objects.ObjectUtils.LocalToWorld(h.T, c.d));
                    a = original.a; b = original.d;
                }
                else if (!_written.ContainsKey(lane))
                {
                    original = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                    a = original.a; b = original.d;
                }
                else continue;   // ours, and its original is unknown: leave it
                long id = Id(lane);
                _laneCurve[lane] = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                if (lanesOf != null) lanesOf[id] = new LaneRef { Lane = lane, A = a, B = b, Original = original };
                sh.Lines.Add(new StitchLine { Id = id, Name = NameOf(lanePrefab), A = a.xz, B = b.xz });
            }
            _houseOf[sh.Id] = h.E;
            _prefabSeen[h.E] = h.Prefab;
            // its other built pieces (shed, pool, greenhouse, deck): no moved fence crosses them. Plants and our own bushes
            // are not obstacles.
            if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(h.E))
                foreach (var so in EntityManager.GetBuffer<global::Game.Objects.SubObject>(h.E, true))
                {
                    var o = so.m_SubObject;
                    if (!EntityManager.Exists(o) || EntityManager.HasComponent<global::Game.Objects.Tree>(o) || EntityManager.HasComponent<global::Game.Objects.Plant>(o)) continue;
                    if (!EntityManager.HasComponent<global::Game.Objects.Transform>(o) || !EntityManager.HasComponent<PrefabRef>(o) || EntityManager.HasComponent<PolishPiece>(o)) continue;
                    var op = EntityManager.GetComponentData<PrefabRef>(o).m_Prefab;
                    if (!EntityManager.HasComponent<ObjectGeometryData>(op) || Array.IndexOf(YardStitch.BushKinds, NameOf(op), 0, YardStitch.GardenKinds) >= 0) continue;   // not our own rocks (a shop's own bins and bay marks are obstacles)
                    var gb = EntityManager.GetComponentData<ObjectGeometryData>(op).m_Bounds;
                    var size = gb.max - gb.min;
                    if (size.x * size.z < 1.5f || size.y < 0.25f) continue;
                    var ot = EntityManager.GetComponentData<global::Game.Objects.Transform>(o);
                    sh.Props.Add(new[]
                    {
                        global::Game.Objects.ObjectUtils.LocalToWorld(ot, new float3(gb.min.x, 0f, gb.min.z)).xz, global::Game.Objects.ObjectUtils.LocalToWorld(ot, new float3(gb.max.x, 0f, gb.min.z)).xz,
                        global::Game.Objects.ObjectUtils.LocalToWorld(ot, new float3(gb.max.x, 0f, gb.max.z)).xz, global::Game.Objects.ObjectUtils.LocalToWorld(ot, new float3(gb.min.x, 0f, gb.max.z)).xz,
                    });
                }
            // a shop, office or works: which surface covers most of its ground (its car park)
            bool home = true;
            if (EntityManager.HasComponent<SpawnableBuildingData>(h.Prefab))
            {
                var zone = EntityManager.GetComponentData<SpawnableBuildingData>(h.Prefab).m_ZonePrefab;
                if (zone != Entity.Null && EntityManager.HasComponent<ZoneData>(zone))
                {
                    var zd = EntityManager.GetComponentData<ZoneData>(zone);
                    home = zd.m_AreaType == global::Game.Zones.AreaType.Residential;
                    sh.Zone = home ? "home" : zd.m_AreaType == global::Game.Zones.AreaType.Commercial ? "shop" : zd.IsOffice() ? "office" : "industry";
                }
            }
            var surfaceArea = new Dictionary<string, float>();
            if (EntityManager.HasBuffer<global::Game.Areas.SubArea>(h.E))
                foreach (var sa in EntityManager.GetBuffer<global::Game.Areas.SubArea>(h.E, true))
                {
                    var area = sa.m_Area;
                    if (!EntityManager.Exists(area) || !EntityManager.HasComponent<global::Game.Areas.Surface>(area) || !EntityManager.HasBuffer<global::Game.Areas.Node>(area)) continue;
                    if (EntityManager.HasComponent<PolishPiece>(area) || EntityManager.HasComponent<Deleted>(area)) continue;   // our own pieces are not the house's
                    if (Ours(h, area)) continue;   // after a load without tags, or a piece made this session but not tagged yet
                    if (!home)
                    {
                        var sn = EntityManager.GetBuffer<global::Game.Areas.Node>(area, true);
                        float a2 = 0f;
                        for (int i = 0; i < sn.Length; i++) { var p = sn[i].m_Position.xz; var q = sn[(i + 1) % sn.Length].m_Position.xz; a2 += p.x * q.y - q.x * p.y; }
                        var nm = NameOf(EntityManager.GetComponentData<PrefabRef>(area).m_Prefab);
                        surfaceArea[nm] = (surfaceArea.TryGetValue(nm, out var had) ? had : 0f) + math.abs(a2) * 0.5f;
                    }
                    if (!IsGrass(EntityManager.GetComponentData<PrefabRef>(area).m_Prefab))
                    {
                        // driveway, paving, sand: grown lawns go around it
                        var hard = EntityManager.GetBuffer<global::Game.Areas.Node>(area, true);
                        if (hard.Length >= 3)
                        {
                            var r = new float2[hard.Length];
                            for (int i = 0; i < r.Length; i++) r[i] = hard[i].m_Position.xz;
                            sh.Hard.Add(r);
                        }
                        continue;
                    }
                    if (!_lawnOriginal.TryGetValue(area, out var ring))
                    {
                        // as the game made it: kept with the lawn once the polish reshaped it (also after a load)
                        if (EntityManager.HasBuffer<PolishLawnOriginal>(area))
                        {
                            var kept = EntityManager.GetBuffer<PolishLawnOriginal>(area, true);
                            if (kept.Length < 3) continue;
                            ring = new float2[kept.Length];
                            for (int i = 0; i < ring.Length; i++) ring[i] = kept[i].Position.xz;
                        }
                        else
                        {
                            var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(area, true);
                            if (nodes.Length < 3) continue;
                            ring = new float2[nodes.Length];
                            for (int i = 0; i < ring.Length; i++) ring[i] = nodes[i].m_Position.xz;
                        }
                        _lawnOriginal[area] = ring;
                    }
                    long id = Id(area);
                    if (!_lawnPrefabOf.ContainsKey(sh.Id)) _lawnPrefabOf[sh.Id] = EntityManager.GetComponentData<PrefabRef>(area).m_Prefab;
                    if (lawnsOf != null) lawnsOf[id] = area;
                    sh.Lawns.Add(new StitchLawn { Id = id, Ring = ring });
                }
            // not a home: its most-used paved surface (a shop with more lawn than car park still joins its neighbours in paving)
            if (!home)
                sh.MainSurface = surfaceArea.Where(kv => kv.Key.IndexOf("grass", StringComparison.OrdinalIgnoreCase) < 0 && kv.Key.IndexOf("lawn", StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
            return sh;
        }

        // ───────────── helpers ─────────────

        private readonly Dictionary<Entity, Bounds3?> _modelBounds = new Dictionary<Entity, Bounds3?>();
        private readonly Dictionary<long, Entity> _houseOf = new Dictionary<long, Entity>();
        /// <summary>The cached description with this pass's Edit flag and the lot's flag (the lists are shared: the planner only reads them).</summary>
        private static StitchHouse Copy(StitchHouse h, bool edit, bool polished, string theme, Dictionary<string, float> options = null) => new StitchHouse
        {
            Id = h.Id, Position = h.Position, Forward = h.Forward, HalfLot = h.HalfLot, Lines = h.Lines, Lawns = h.Lawns, Hard = h.Hard,
            Footprint = h.Footprint, Props = h.Props, Edit = edit, MainSurface = h.MainSurface, Polished = polished, Zone = h.Zone, Theme = theme, Options = options,
        };

        /// <summary>
        /// A surface the polish made for this house, not one of the house's own: one the last plan recorded (by kind and centre),
        /// or one centred outside the house's lot (the game's own pieces always lie inside it; after a load the record is empty).
        /// </summary>
        private bool Ours(House h, Entity area)
        {
            if (_lawnOriginal.ContainsKey(area)) return false;   // the house's own lawn, grown by the polish
            var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(area, true);
            if (nodes.Length < 3) return false;
            var c = float2.zero;
            for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position.xz;
            c /= nodes.Length;
            var fwd = math.normalizesafe(math.mul(h.T.m_Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
            var right = new float2(fwd.y, -fwd.x);
            var d = c - h.T.m_Position.xz;
            var half = (float2)h.Lot * 4f;
            if (math.abs(math.dot(d, right)) > half.x + 0.5f || math.abs(math.dot(d, fwd)) > half.y + 0.5f) return true;
            // paving reaching past the lot (a shop yard wrapped round its building has its middle inside the lot)
            return !IsGrass(EntityManager.GetComponentData<PrefabRef>(area).m_Prefab) && PastLot(h.T, h.Lot, nodes);
        }

        /// <summary>A point of the outline more than a metre outside the lot (the game's own surfaces lie inside it).</summary>
        private static bool PastLot(global::Game.Objects.Transform t, int2 lot, DynamicBuffer<global::Game.Areas.Node> nodes)
        {
            var fwd = math.normalizesafe(math.mul(t.m_Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
            var right = new float2(fwd.y, -fwd.x);
            var half = (float2)lot * 4f + 1f;
            for (int i = 0; i < nodes.Length; i++)
            {
                var d = nodes[i].m_Position.xz - t.m_Position.xz;
                if (math.abs(math.dot(d, right)) > half.x || math.abs(math.dot(d, fwd)) > half.y) return true;
            }
            return false;
        }

        /// <summary>A surface of the house without our tag that is ours all the same: paving (not a lawn, which the polish
        /// grows past the lot) reaching past the lot.</summary>
        private bool UntaggedOurs(Entity house, Entity area)
        {
            if (!EntityManager.HasComponent<global::Game.Areas.Surface>(area) || !EntityManager.HasComponent<PrefabRef>(area)) return false;
            if (_lawnOriginal.ContainsKey(area) || IsGrass(EntityManager.GetComponentData<PrefabRef>(area).m_Prefab)) return false;
            if (!EntityManager.HasComponent<global::Game.Objects.Transform>(house) || !EntityManager.HasComponent<PrefabRef>(house)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            if (!EntityManager.HasComponent<BuildingData>(prefab)) return false;
            return PastLot(EntityManager.GetComponentData<global::Game.Objects.Transform>(house), EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize,
                EntityManager.GetBuffer<global::Game.Areas.Node>(area, true));
        }

        private static float2 Centre(float2[] ring)
        {
            var c = float2.zero;
            foreach (var p in ring) c += p;
            return c / math.max(1, ring.Length);
        }

        private Entity NearestHouse(List<StitchHouse> stitch, float2[] ring)
        {
            var c = float2.zero;
            foreach (var p in ring) c += p;
            c /= ring.Length;
            Entity best = Entity.Null;
            float d = float.MaxValue;
            foreach (var h in stitch)
                if (h.Polished && _houseOf.TryGetValue(h.Id, out var e) && math.distance(h.Position, c) < d) { d = math.distance(h.Position, c); best = e; }
            return best;
        }
        private readonly Dictionary<long, Entity> _lawnPrefabOf = new Dictionary<long, Entity>();
        private int _newLawns;

        /// <summary>The house model's own bounds (its meshes), not the collision box (which covers the whole lot).</summary>
        private bool ModelBounds(Entity prefab, out Bounds3 bounds)
        {
            if (!_modelBounds.TryGetValue(prefab, out var cached))
            {
                cached = null;
                try
                {
                    if (_prefabs.TryGetPrefab<ObjectGeometryPrefab>(prefab, out var p) && p != null && p.m_Meshes != null)
                        foreach (var mi in p.m_Meshes)
                        {
                            if (!(mi?.m_Mesh is RenderPrefab rp)) continue;
                            var b = new Bounds3(rp.bounds.min + mi.m_Position, rp.bounds.max + mi.m_Position);
                            cached = cached.HasValue ? new Bounds3(math.min(cached.Value.min, b.min), math.max(cached.Value.max, b.max)) : b;
                        }
                }
                catch (Exception) { cached = null; }
                _modelBounds[prefab] = cached;
            }
            bounds = cached ?? default;
            return cached.HasValue;
        }

        private bool IsFence(Entity lanePrefab) =>
            EntityManager.HasComponent<UtilityLaneData>(lanePrefab) && (EntityManager.GetComponentData<UtilityLaneData>(lanePrefab).m_UtilityTypes & UtilityTypes.Fence) != 0;

        private bool IsGrass(Entity prefab)
        {
            if (_grassPrefab.TryGetValue(prefab, out bool g)) return g;
            g = NameOf(prefab).IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 || NameOf(prefab).IndexOf("lawn", StringComparison.OrdinalIgnoreCase) >= 0;
            _grassPrefab[prefab] = g;
            return g;
        }

        private string NameOf(Entity prefab)
        {
            if (_names.TryGetValue(prefab, out var n)) return n;
            try { n = _prefabs.GetPrefab<PrefabBase>(prefab)?.name ?? ""; } catch (Exception) { n = ""; }
            _names[prefab] = n;
            return n;
        }

        /// <summary>The lane lies where the game put it (on the ground, so only its plan view is compared).</summary>
        private static bool IsOriginal(Bezier4x3 cur, Bezier4x3 original) =>
            math.all(math.abs(cur.a.xz - original.a.xz) < 0.05f) && math.all(math.abs(cur.b.xz - original.b.xz) < 0.05f)
            && math.all(math.abs(cur.c.xz - original.c.xz) < 0.05f) && math.all(math.abs(cur.d.xz - original.d.xz) < 0.05f);

        /// <summary>The same curve seen from above (the game may set a fence's heights again).</summary>
        private static bool SameXZ(Bezier4x3 x, Bezier4x3 y) =>
            math.all(math.abs(x.a.xz - y.a.xz) < 0.01f) && math.all(math.abs(x.b.xz - y.b.xz) < 0.01f) && math.all(math.abs(x.c.xz - y.c.xz) < 0.01f) && math.all(math.abs(x.d.xz - y.d.xz) < 0.01f);

        // each fence lane where we last saw or put it, and each house's prefab when it was described: a lane marked Updated
        // with neither changed is no change (CollectChanged)
        private readonly Dictionary<Entity, Bezier4x3> _laneCurve = new Dictionary<Entity, Bezier4x3>();
        private readonly Dictionary<Entity, Entity> _prefabSeen = new Dictionary<Entity, Entity>();
        /// <summary>For the game runner: fence lanes the game marked Updated without moving them (no plan for that).</summary>
        public static int DirtySkipped;

        private static bool Same(Bezier4x3 x, Bezier4x3 y) =>
            math.all(math.abs(x.a - y.a) < 0.01f) && math.all(math.abs(x.b - y.b) < 0.01f) && math.all(math.abs(x.c - y.c) < 0.01f) && math.all(math.abs(x.d - y.d) < 0.01f);

        private static long Id(Entity e) => ((long)e.Index << 32) | (uint)e.Version;

        private static long Key(float2 p) => ((long)(int)math.floor(p.x / Cell) << 32) ^ (uint)(int)math.floor(p.y / Cell);

        private void Prune()
        {
            if (_written.Count + _lawnOriginal.Count + _laneCurve.Count + _prefabSeen.Count < 4096) return;
            var gone = new List<Entity>();
            foreach (var e in _written.Keys) if (!EntityManager.Exists(e)) gone.Add(e);
            foreach (var e in gone) _written.Remove(e);
            gone.Clear();
            foreach (var e in _laneCurve.Keys) if (!EntityManager.Exists(e)) gone.Add(e);
            foreach (var e in gone) _laneCurve.Remove(e);
            gone.Clear();
            foreach (var e in _prefabSeen.Keys) if (!EntityManager.Exists(e)) gone.Add(e);
            foreach (var e in gone) _prefabSeen.Remove(e);
            gone.Clear();
            foreach (var e in _lawnOriginal.Keys) if (!EntityManager.Exists(e)) gone.Add(e);
            foreach (var e in gone) _lawnOriginal.Remove(e);
        }

        private void Report()
        {
            Mod.Log.Info($"Placemaker neighbourhood polish: {_passes} passes, {_pairs} neighbour pairs, {_moved} fence/hedge lines moved onto shared borders, "
                + $"{_hidden} doubled lines hidden, {_restored} restored, {_lawns} lawns grown (+{_lawnM2:F0} m²), {_ms:F1} ms in total; {_dirty.Count} houses waiting.");
            _passes = _pairs = _moved = _hidden = _restored = _lawns = 0;
            _lawnM2 = 0f;
            _ms = 0;
        }
    }
}
