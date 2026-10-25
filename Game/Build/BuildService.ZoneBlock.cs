// Placemaker — zone blocking lots after the build is applied (s19, GPL-3.0).
//
// Playtest: zoned buildings grew over a plaza. CS2 only blocks zone cells under ground nets and PHYSICAL areas
// (Game.Zones.CellBlockJobs, RESEARCH-NOTES-ADDENDUM 67–72); the owner lot overlaps by design and surfaces are
// never physical. Core/Build/ZoneGuard plans ComposedPlan.ZoneBlockers = the footprint minus the zoned cells
// and the existing building lots. They are built here as invisible physical "Placemaker Zone Block" lots, in
// their own transaction once everything else is permanent:
//  * a physical lot reports overlaps with our nets / non-overridable objects / lots; they must not make the
//    main build exclude its own parts, and Apply builds errored temporaries anyway (the tool gate is ours);
//  * it would demolish a spawnable building it overlaps (Warning): such pieces are dropped and the rest
//    re-emitted (≤ 3 rounds); if a warning stays, no blocker is built at all;
//  * the zoning step waits after them, so the Marquee zoning only finds the cells that stay visible.
// Members: Auxiliary / ZoneBlocker, so removing the build removes the blocking (the game unblocks the cells;
// zoning the player painted before comes back, as when a vanilla road is removed; grown shops stay).

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Groups;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Setting "Keep zoned buildings off builds" (default on); forwarded to Core ZoneGuard too.</summary>
        public static bool BlockZoning
        {
            get => ZoneGuard.Enabled;
            set => ZoneGuard.Enabled = value;
        }

        private const int BlockerMaterializeFrames = 60;
        private const int BlockerValidateFrames = 4;
        private const int BlockerRounds = 3;

        private readonly List<float2[]> _blockerRings = new List<float2[]>();
        private readonly List<Entity> _blockerTemps = new List<Entity>();
        private Entity _blockerPrefab;
        private int _blockerRound;
        private int _blockerPhase;     // 0 emit, 1 wait temps, 2 validated → apply, 3 settle, 4 clear for a retry
        private int _blockerSince;
        private int _blockersBuilt;

        private bool HasBlockers =>
            BlockZoning && !LooseBuild && _groupId != 0 && _request?.Plan?.ZoneBlockers != null && _request.Plan.ZoneBlockers.Count > 0;

        /// <summary>(s26) Zoning quads recomputed against the live blocks after Apply (null = use the plan's).</summary>
        private List<ZoneGuard.ZoningQuad> _refreshedZoning;
        /// <summary>Frames after the main Apply before the live zone blocks are read (split edges get new blocks, cells re-checked).</summary>
        private const int ZoneRefreshFrames = 40;

        private bool GuardsZoning =>
            BlockZoning && !LooseBuild && _groupId != 0 && _request?.Plan?.Footprints != null && _request.Plan.Footprints.Count > 0
            && (_request.Plan.ZoneQuads.Count > 0 || _request.Plan.ZoneBlockers.Count > 0);

        /// <summary>(core) True while the zone-block machine builds the kerb strips (before the zoning refresh), not the main blockers.</summary>
        private bool _kerbStage;

        private bool HasKerbs =>
            BlockZoning && !LooseBuild && _groupId != 0 && _request?.Plan?.KerbBlockers != null && _request.Plan.KerbBlockers.Count > 0;

        /// <summary>The step after the main Apply settled: (kerb strips,) refresh against the live blocks, blockers, zoning, finish.</summary>
        private void GoAfterApply()
        {
            _refreshedZoning = null;
            _kerbStage = false;
            if (GuardsZoning && HasKerbs)
            {
                // (core) The city streets' columns that reach into the build are blocked first, so the refresh and the zoning find the
                // build's own lanes holding the shop plots (the older visible city block wins every overlap otherwise: teardown 08 §3).
                _blockerRings.Clear();
                double m2 = 0;
                foreach (var r in _request.Plan.KerbBlockers) if (r != null && r.Length >= 3) { _blockerRings.Add(r); m2 += Poly.Area(r); }
                Mod.Log.Info($"Placemaker kerb blocking: {_blockerRings.Count} strip(s), {m2:F0} m², in the first row of the city streets' zone blocks along the build (before the zoning refresh).");
                _kerbStage = true;
                _blockerRound = 0;
                _blockerPhase = 0;
                Go(Step.ZoneBlock);
                return;
            }
            if (GuardsZoning) { Go(Step.ZoneRefresh); return; }
            // (s34) A roads build that pre-zones: zone the live blocks of the roads it just built.
            if (PreZonesRoads) { Go(Step.RoadPreZone); return; }
            ContinueAfterApply();
        }

        /// <summary>
        /// (s26) The build's paths split the streets' edges at their entrances; the game rebuilds those edges' zone
        /// blocks with cells shifted along the road. Re-snap cutouts and blockers to the live cells (ZoneGuard.Refresh).
        /// </summary>
        // (perf) ZoneGuard.Refresh tries up to ~50 full zone computations: 1–5 s in one frame for a big shop build (teardown 14).
        // It runs on a worker against a stand-in plan holding what it reads; what it writes is copied back here.
        private System.Threading.Tasks.Task<(List<ZoneGuard.ZoningQuad> quads, ComposedPlan shadow, string report, double ms)> _zrJob;
        private List<Placemaker.Core.ZoneGrid> _zrGrids;
        private List<float2> _zrBefore;

        private void DoZoneRefresh(int frame)
        {
            if (_zrJob != null) { if (_zrJob.IsCompleted) FinishZoneRefresh(); return; }
            if (frame - _stepFrame < ZoneRefreshFrames) return;
            LogSaggingEdges("before the zoning refresh");
            var plan = _request.Plan;
            try
            {
                float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
                foreach (var f in plan.Footprints) foreach (var p in f) { min = math.min(min, p); max = math.max(max, p); }
                var grids = new List<Placemaker.Core.ZoneGrid>();
                Placemaker.Game.Preview.SiteCapture.CaptureZoneBlocks(EntityManager, World.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>(), min - 60f, max + 60f, grids);
                var before = new List<float2>();
                foreach (var c in plan.ZoneCells) if (c.State == ZoneCellState.Zoned) before.Add(0.25f * (c.A + c.B + c.C + c.D));
                _heightsValid = false;
                LogBlockedPlanCells();
                if (TryCopyHeights(out var copy))
                {
                    var shadow = new ComposedPlan
                    {
                        Footprints = plan.Footprints, ZoneQuads = plan.ZoneQuads, Protected = plan.Protected,
                        ZoneCells = new List<ZoneCellMark>(plan.ZoneCells), ZoneBlockers = new List<float2[]>(plan.ZoneBlockers),
                    };
                    _zrGrids = grids;
                    _zrBefore = before;
                    _zrJob = System.Threading.Tasks.Task.Run(() =>
                    {
                        var d = copy;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var quads = ZoneGuard.Refresh(shadow, grids, p => { float h = global::Game.Simulation.TerrainUtils.SampleHeight(ref d, new float3(p.x, 0f, p.y)); return math.isfinite(h) ? h : 0f; });
                            return (quads, shadow, ZoneGuard.LastRefreshReport, sw.Elapsed.TotalMilliseconds);
                        }
                        finally { DisposeHeights(d); }
                    });
                    return;
                }
                var refreshed = ZoneGuard.Refresh(plan, grids, p => Height(p));
                ReportZoneRefresh(refreshed, grids, before, ZoneGuard.LastRefreshReport);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker zoning refresh failed (using the plan's cells): " + e.Message);
                _refreshedZoning = null;
            }
            ContinueAfterApply();
        }

        private void FinishZoneRefresh()
        {
            var job = _zrJob;
            _zrJob = null;
            try
            {
                if (job.Status != System.Threading.Tasks.TaskStatus.RanToCompletion) throw job.Exception?.GetBaseException() ?? new Exception("cancelled");
                var (quads, shadow, report, ms) = job.Result;
                var plan = _request.Plan;
                if (quads != null)
                {
                    plan.ZoneBlockers.Clear(); plan.ZoneBlockers.AddRange(shadow.ZoneBlockers);
                    plan.ZoneCells.Clear(); plan.ZoneCells.AddRange(shadow.ZoneCells);
                }
                Mod.Log.Info($"Placemaker zoning refresh made in the background in {ms:0} ms.");
                ReportZoneRefresh(quads, _zrGrids, _zrBefore, report);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker zoning refresh failed (using the plan's cells): " + e.Message);
                _refreshedZoning = null;
            }
            _zrGrids = null;
            _zrBefore = null;
            ContinueAfterApply();
        }

        private void ReportZoneRefresh(List<ZoneGuard.ZoningQuad> refreshed, List<Placemaker.Core.ZoneGrid> grids, List<float2> before, string report)
        {
            var plan = _request.Plan;
                if (refreshed != null)
                {
                    _refreshedZoning = refreshed;
                    int after = 0, moved = 0;
                    foreach (var c in plan.ZoneCells)
                    {
                        if (c.State != ZoneCellState.Zoned) continue;
                        after++;
                        var centre = 0.25f * (c.A + c.B + c.C + c.D);
                        bool same = false;
                        foreach (var b in before) if (math.distancesq(b, centre) < 0.25f) { same = true; break; }
                        if (!same) moved++;
                    }
                    int ours = 0;
                    foreach (var g in grids) if (g.Ours) ours++;
                    Mod.Log.Info($"Placemaker zoning refresh: {grids.Count} live zone block(s) ({ours} of the build's own roads); {after} cutout cell(s) (planned {before.Count}), "
                        + $"{moved} on a moved grid (road edges split by the build), {refreshed.Count} zoning quad(s).");
                    Mod.Log.Info("Placemaker zoning refresh (s54): " + report);
                    // Which road each plot zones from (playtest: shops grew facing the street behind the plaza's lane).
                    if (DebugBuildChecks)
                        for (int i = 0; i < refreshed.Count && i < 40; i++)
                        {
                            var c = Poly.Centroid(refreshed[i].Corners);
                            Mod.Log.Info($"Placemaker zoning: plot {i} at ({c.x:F0}, {c.y:F0}) zones from {RoadOfCell(c)}.");
                        }
                }
        }

        /// <summary>(s26) The road whose live zone block holds the cell at <paramref name="p"/> (prefab name, ours or the city's).</summary>
        private string RoadOfCell(float2 p)
        {
            var search = World.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
            var tree = search.GetSearchTree(true, out var deps);
            deps.Complete();
            var results = new Unity.Collections.NativeList<Entity>(16, Unity.Collections.Allocator.Temp);
            try
            {
                var it = new ZoneBlockIterator { Bounds = new Colossal.Mathematics.Bounds2(p - 1f, p + 1f), Results = results };
                tree.Iterate(ref it);
                string fallback = "no zone block";
                for (int i = 0; i < results.Length; i++)
                {
                    var e = results[i];
                    if (!EntityManager.HasComponent<global::Game.Zones.Block>(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e)) continue;
                    var b = EntityManager.GetComponentData<global::Game.Zones.Block>(e);
                    var q = global::Game.Zones.ZoneUtils.CalculateCorners(b);
                    if (!Poly.Contains(new[] { q.a, q.b, q.c, q.d }, p)) continue;
                    string road = "a road";
                    if (EntityManager.HasComponent<global::Game.Common.Owner>(e))
                    {
                        var owner = EntityManager.GetComponentData<global::Game.Common.Owner>(e).m_Owner;
                        if (owner != Entity.Null && EntityManager.Exists(owner) && EntityManager.HasComponent<global::Game.Prefabs.PrefabRef>(owner))
                            road = "'" + _prefabs.GetPrefabName(EntityManager.GetComponentData<global::Game.Prefabs.PrefabRef>(owner).m_Prefab) + "'"
                                + (EntityManager.HasComponent<PlacemakerMember>(owner) ? " (built by Placemaker)" : " (city)");
                    }
                    // Prefer the block whose cell there is visible (the one the game zones).
                    var cells = EntityManager.GetBuffer<global::Game.Zones.Cell>(e, true);
                    bool visible = false;
                    for (int k = 0; k < cells.Length && k < b.m_Size.x * b.m_Size.y; k++)
                    {
                        int x = k % b.m_Size.x, y = k / b.m_Size.x;
                        if (math.distancesq(global::Game.Zones.ZoneUtils.GetCellPosition(b, new int2(x, y)).xz, p) > 32f) continue;
                        if ((cells[k].m_State & global::Game.Zones.CellFlags.Visible) != 0) { visible = true; break; }
                    }
                    if (visible) return road;
                    fallback = road + " (hidden there)";
                }
                return fallback;
            }
            finally { results.Dispose(); }
        }

        private void ContinueAfterApply()
        {
            if (HasBlockers)
            {
                _blockerRings.Clear();
                foreach (var r in _request.Plan.ZoneBlockers) if (r != null && r.Length >= 3) _blockerRings.Add(r);
                // (s22) What the blocking keeps free, for the in-game check (cutout cells must never be under a blocker).
                int zonedCells = 0, touched = 0;
                double m2 = 0;
                foreach (var r in _blockerRings) m2 += Poly.Area(r);
                if (_request.Plan.ZoneCells != null)
                    foreach (var c in _request.Plan.ZoneCells)
                    {
                        if (c.State != ZoneCellState.Zoned) continue;
                        zonedCells++;
                        var sq = new[] { c.A, c.B, c.C, c.D };
                        var centre = 0.25f * (c.A + c.B + c.C + c.D);
                        for (int i = 0; i < 4; i++) sq[i] += math.normalizesafe(centre - sq[i]) * 0.03f;
                        foreach (var r in _blockerRings) if (ObstacleCut.Overlaps(sq, r)) { touched++; break; }
                    }
                Mod.Log.Info($"Placemaker zone blocking: {_blockerRings.Count} lot ring(s), {m2:F0} m², keeping {zonedCells} cutout cell(s) free"
                    + (touched > 0 ? $" — {touched} of them touched by a blocker (bug)." : "."));
                _blockerRound = 0;
                _blockerPhase = 0;
                Go(Step.ZoneBlock);
                return;
            }
            GoZoning();
        }

        private void GoZoning()
        {
            // (core) The kerb strips are done: now the refresh against the live blocks, then the main blockers and the zoning.
            if (_kerbStage) { _kerbStage = false; Go(Step.ZoneRefresh); return; }
            Go((_refreshedZoning != null ? _refreshedZoning.Count : _work.Zoning.Count) > 0 ? Step.ZoningWait : Step.Finish);
        }

        private void ResetZoneBlock()
        {
            _kerbStage = false;
            _blockerRings.Clear();
            _blockerTemps.Clear();
            _blockerPrefab = Entity.Null;
            _blockerRound = 0;
            _blockerPhase = 0;
            _blockersBuilt = 0;
        }

        private void DoZoneBlock(int frame)
        {
            switch (_blockerPhase)
            {
                case 0:
                {
                    _blockerPrefab = _ownerPrefabs != null ? _ownerPrefabs.ReadyBlockerPrefab : Entity.Null;
                    if (_blockerPrefab == Entity.Null || _blockerRings.Count == 0)
                    {
                        if (_blockerPrefab == Entity.Null) Mod.Log.Warn("Placemaker: the zone block lot prefab is not available; zone cells under this build stay as they are.");
                        GoZoning();
                        return;
                    }
                    _heightsValid = false;
                    foreach (var ring in _blockerRings) EmitBlockerDefinition(ring);
                    _blockerSince = frame;
                    _blockerPhase = 1;
                    return;
                }
                case 1:
                {
                    CollectBlockerTemps();
                    bool all = _blockerTemps.Count >= _blockerRings.Count;
                    if (!all && frame - _blockerSince < BlockerMaterializeFrames) return;
                    if (_blockerTemps.Count == 0)
                    {
                        Mod.Log.Warn("Placemaker: no zone block lot materialised; zone cells under this build stay as they are.");
                        ClearBlockers();
                        return;
                    }
                    _blockerSince = frame;
                    _blockerPhase = 2;
                    return;
                }
                case 2:
                {
                    if (frame - _blockerSince < BlockerValidateFrames) return;
                    CollectBlockerTemps();
                    // A piece that would demolish a building (spawnable buildings are DeleteOverridden): drop it.
                    var lots = WarnedBuildingLots();
                    if (lots.Count > 0)
                    {
                        int before = _blockerRings.Count;
                        _blockerRings.RemoveAll(r => lots.Exists(l => ObstacleCut.Overlaps(r, l)));
                        int dropped = before - _blockerRings.Count;
                        if (_blockerRound++ < BlockerRounds && (dropped > 0 || _blockerRings.Count > 0))
                        {
                            Mod.Log.Info($"Placemaker: {lots.Count} building(s) stood under the zone blocking; left out {dropped} piece(s), retrying.");
                            DestroyOwnDefinitions();
                            _tool.Requested = ApplyMode.Clear;
                            _blockerSince = frame;
                            _blockerPhase = 4;
                            return;
                        }
                        Mod.Log.Warn("Placemaker: zone blocking would remove existing buildings; it is left out for this build.");
                        ClearBlockers();
                        return;
                    }
                    foreach (var t in _blockerTemps)
                        if (EntityManager.Exists(t)) EntityManager.AddComponentData(t, new PlacemakerMember { GroupId = _groupId, Kind = MemberKind.Auxiliary, Role = MemberRole.ZoneBlocker });
                    _tool.Requested = ApplyMode.Apply;
                    _blockerSince = frame;
                    _blockerPhase = 3;
                    return;
                }
                case 3:
                {
                    if (frame - _blockerSince < ApplySettleFrames) return;
                    if (frame - _blockerSince == ApplySettleFrames) DestroyOwnDefinitions();
                    int still = 0;
                    foreach (var t in _blockerTemps) if (EntityManager.Exists(t) && EntityManager.HasComponent<Temp>(t)) still++;
                    if (still > 0 && frame - _blockerSince < ApplyTimeoutFrames) return;
                    _blockersBuilt = 0;
                    foreach (var t in _blockerTemps) if (EntityManager.Exists(t) && !EntityManager.HasComponent<Temp>(t) && !EntityManager.HasComponent<Deleted>(t)) _blockersBuilt++;
                    _registry.MarkDirty();
                    Mod.Log.Info(_kerbStage ? $"Placemaker: {_blockersBuilt} kerb strip(s) block the city streets' zone cells along group {_groupId}."
                        : $"Placemaker: {_blockersBuilt} zone block lot(s) keep zoned buildings off group {_groupId}.");
                    GoZoning();
                    return;
                }
                case 4:
                {
                    if (frame - _blockerSince < 2) return;
                    _blockerTemps.Clear();
                    _blockerPhase = 0;
                    return;
                }
            }
        }

        /// <summary>Drops every blocker temporary (Clear) and goes on with the zoning.</summary>
        private void ClearBlockers()
        {
            DestroyOwnDefinitions();
            _tool.Requested = ApplyMode.Clear;
            _blockerTemps.Clear();
            GoZoning();
        }

        private void EmitBlockerDefinition(float2[] ring)
        {
            var def = NewDefinition();
            EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = _blockerPrefab });
            EntityManager.AddComponent<Updated>(def);
            var nodes = EntityManager.AddBuffer<global::Game.Areas.Node>(def);
            nodes.ResizeUninitialized(ring.Length + 1);
            for (int i = 0; i < ring.Length; i++)
                nodes[i] = new global::Game.Areas.Node(new float3(ring[i].x, GroundHeight(ring[i]), ring[i].y), float.MinValue);
            nodes[ring.Length] = nodes[0];
            NoteUsed(_blockerPrefab);
        }

        /// <summary>Our blocker temporaries (while our tool is active every temporary is ours).</summary>
        private void CollectBlockerTemps()
        {
            _blockerTemps.Clear();
            using var temps = _tempQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
            {
                var e = temps[i];
                if (!EntityManager.HasComponent<global::Game.Areas.Area>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                if (!EntityManager.HasComponent<global::Game.Prefabs.PrefabRef>(e)) continue;
                if (EntityManager.GetComponentData<global::Game.Prefabs.PrefabRef>(e).m_Prefab != _blockerPrefab) continue;
                _blockerTemps.Add(e);
            }
        }

        /// <summary>Lots (grown 1 m) of live buildings the game marked for removal by our temporaries.</summary>
        private List<float2[]> WarnedBuildingLots()
        {
            var polys = new List<float2[]>();
            if (_warnedBuildingQuery == null || _warnedBuildingQuery.IsEmptyIgnoreFilter) return polys;
            using var warned = _warnedBuildingQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < warned.Length; i++)
            {
                var e = warned[i];
                if (!BuildingFootprints.IsBuilding(EntityManager, e)) continue;
                BuildingFootprints.Of(EntityManager, e, out var lot, out var footprint);
                if (lot != null) polys.Add(lot);
                if (footprint != null) polys.Add(footprint);
            }
            return ObstacleCut.Grow(polys, 1f);
        }
    }
}
