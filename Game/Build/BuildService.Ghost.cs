// Placemaker — ghost preview: the plan as real game temporaries, never applied (GPL-3.0).
//
// Implements Game/Preview/IGhostPreview for the tool (GhostPreviewClient). Same emission code as the
// build (EmitNets/EmitSurface/EmitArea/EmitObject + clearing), so the ghost shows exactly the prefabs,
// seeds and colour variations the build will create. Vanilla tool cadence (decompiled ObjectToolSystem):
// a refresh is ONE Clear frame in which the old definitions are destroyed and the new ones created;
// afterwards the tool keeps applyMode = None and the temporaries stay. The active Placemaker tool calls
// Pump() once per frame and sets applyMode = Clear only when Pump returns true.
//
//  * Debounce: a new revision is emitted after GhostDebounceSeconds of no further Show calls (the first
//    ghost appears immediately). Nets go in the first frame (junctions merge); surfaces/areas/objects
//    in batches of GhostPerFrame. More than GhostMaxDefinitions → objects are sampled evenly and the
//    status says Partial.
//  * No error icon spam: objects get CreationFlags.Optional (ValidationSystem.CancelOptional hides a
//    colliding optional temp with TempFlags.Hidden|Cancel and adds no icon — vanilla brushes do this). (s59) Only with
//    "Ignore collisions in my builds" OFF: under anarchy the ghost is non-optional and untinted — it shows what the build places;
//    remaining Temp icons (nets/areas) get Game.Tools.Hidden, which NotificationIconBufferSystem skips.
//    Counts/positions are reported in GhostDetails instead.
//  * Real surface textures: AreaBatchSystem excludes Temp areas from rendering; GhostSurfaceSystem
//    tags our Temp surfaces and widens AreaBatchSystem's queries to include tagged temporaries.
//  * Never costs money (nothing is applied), never creates permanent entities. Cleanup: Hide (own
//    definitions now, temporaries on the next Clear frame), Start of a build, game load, and a
//    watchdog: if the tool stops pumping while temporaries exist, the service runs the ClearTool
//    phase itself (exactly what ToolOutputSystem does for a Clear frame).
//  * Instant build (TryBuildFromGhost): a complete, unchanged ghost is adopted by the build — the build
//    tool is activated WITHOUT its initial Clear, the census/validation/apply path runs on the ghost's
//    temporaries. Progress through Services.Build.Status as usual.

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Game.Preview;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    /// <summary>Extra ghost facts for UI summaries (errors are never shown as icon spam).</summary>
    public struct GhostDetails
    {
        public bool Partial;
        public int Planned, Emitted;
        /// <summary>Temporaries the game marked as errors (red), and optional objects it dropped (collisions).</summary>
        public int Errors, Dropped;
        public List<float3> ErrorPositions;
    }

    public sealed partial class BuildService : IGhostPreview
    {
        public static float GhostDebounceSeconds = 0.25f;
        public static int GhostPerFrame = 800;
        public static int GhostMaxDefinitions = 6000;
        private const int GhostSettleFrames = 4;
        private const int GhostWatchdogFrames = 3;

        private enum GhostStep { Idle, Preparing, Emitting, Settling, Shown, Failed }

        private GhostStep _gStep;
        private ComposedPlan _gPlan;
        private int _gRequested = -1, _gShown = -1, _gEmitting = -1;
        private float _gChangedAt;
        private bool _gWanted, _gHasContent, _gEmission;
        private int _gLastPump = -100, _gStepFrame;
        private string _gMessage;
        private List<int> _gObjects;      // object indices to emit (all, or an even sample)
        private int _gCursor, _gTotal;
        private GhostDetails _gDetails;
        private bool _instant;
        private EntityQuery _visibleTempIconQuery, _cancelledQuery;

        /// <summary>True while ghost temporaries/definitions of the service exist (GhostSurfaceSystem tags only then).</summary>
        public bool GhostActive => _gHasContent;
        public GhostDetails GhostInfo => _gDetails;

        private void InitGhost()
        {
            _visibleTempIconQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Notifications.Icon>(), ComponentType.ReadOnly<Temp>() },
                None = new[] { ComponentType.ReadOnly<Hidden>(), ComponentType.ReadOnly<Deleted>() },
            });
            _cancelledQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<global::Game.Objects.Object>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            GhostPreview.Register(this);
        }

        private void ResetGhost()
        {
            _gStep = GhostStep.Idle;
            _gPlan = null;
            _gRequested = _gShown = _gEmitting = -1;
            _gWanted = _gHasContent = _gEmission = false;
            _gObjects = null;
            _gMessage = null;
            _gDetails = default;
        }

        // ───────────────────────── IGhostPreview ─────────────────────────

        GhostStatus IGhostPreview.Status
        {
            get
            {
                var s = new GhostStatus { ShownRevision = _gShown, RequestedRevision = _gWanted ? _gRequested : -1 };
                if (_gStep == GhostStep.Failed) { s.Phase = GhostPhase.Failed; s.MessageKey = _gMessage; }
                else if (!_gWanted) s.Phase = GhostPhase.Hidden;
                else if (_gStep == GhostStep.Shown && _gShown == _gRequested) { s.Phase = GhostPhase.Shown; s.Dropped = _gDetails.Dropped; }
                else s.Phase = GhostPhase.Pending;
                return s;
            }
        }

        /// <summary>The ghost service's own state, for the tool's stuck-ghost log line.</summary>
        public string GhostDebug => $"ghost step {_gStep}, wanted {_gWanted}, emitting {_gEmitting}, content {_gHasContent}, build step {_step}, last pump {UnityEngine.Time.frameCount - _gLastPump} frames ago";

        /// <summary>Ghost status (IGhostPreview.Status; explicit because IBuildService.Status is the build's).</summary>
        public GhostStatus GhostState => ((IGhostPreview)this).Status;

        public void Show(ComposedPlan plan, int revision)
        {
            if (plan == null) { Hide(); return; }
            if (_gWanted && revision == _gRequested && ReferenceEquals(plan, _gPlan)) return;
            _gWanted = true;
            _gPlan = plan;
            _gRequested = revision;
            _gChangedAt = UnityEngine.Time.realtimeSinceStartup;
            if (_gStep == GhostStep.Failed) _gStep = GhostStep.Idle;
        }

        public void Hide()
        {
            _gWanted = false;
            _gPlan = null;
            _gRequested = -1;
            // Pending definitions go now; the temporaries on the next Clear frame (Pump returns true).
            if (!Busy) DestroyGhostDefinitions();
            if (_gStep != GhostStep.Failed) _gStep = GhostStep.Idle;
        }

        public bool Pump()
        {
            int frame = UnityEngine.Time.frameCount;
            _gLastPump = frame;
            if (Busy) return false;   // the build owns every temporary now
            long t0 = Now;
            try { return PumpGhost(frame); }
            finally { GhostFrame((Now - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency); }
        }

        private bool PumpGhost(int frame)
        {
            _heightsValid = false;
            try
            {
                long th = Now;
                if (_gHasContent) HideGhostIcons();
                Sub("HideGhostIcons", th);
                if (!_gWanted)
                {
                    if (!_gHasContent) return false;
                    DestroyGhostDefinitions();
                    _gHasContent = false;
                    _gShown = -1;
                    _gStep = _gStep == GhostStep.Failed ? GhostStep.Failed : GhostStep.Idle;
                    return true;   // Clear frame: the temporaries go
                }
                // a revision asked for again after its ghost went (hidden, or a failed build took it) shows again: wanted,
                // nothing shown and nothing emitting never waits for ever
                bool newRevision = _gRequested != _gEmitting || (_gStep == GhostStep.Idle && !_gHasContent);
                if (newRevision)
                {
                    bool first = !_gHasContent;
                    if (!first && UnityEngine.Time.realtimeSinceStartup - _gChangedAt < GhostDebounceSeconds) return false;
                    return BeginGhost(frame);
                }
                switch (_gStep)
                {
                    case GhostStep.Preparing: StartGhostEmission(frame); break;
                    case GhostStep.Emitting: EmitGhostBatch(frame); break;
                    case GhostStep.Settling:
                        if (frame - _gStepFrame >= GhostSettleFrames)
                        {
                            long ts = Now;
                            CollectGhostFacts();
                            Sub("CollectGhostFacts", ts); ts = Now;
                            ClearGhostVerdicts();   // (s59) anarchy: no red refused tint on pieces the build will place
                            Sub("ClearGhostVerdicts", ts);
                            _gShown = _gEmitting;
                            _gStep = GhostStep.Shown;
                            GhostTimingLog();
                        }
                        break;
                }
                return false;
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker ghost preview failed");
                bool had = _gHasContent;
                try { DestroyGhostDefinitions(); } catch (Exception) { }
                ResetGhost();
                _gStep = GhostStep.Failed;
                _gMessage = "Placemaker.Ghost.Failed";
                return had;
            }
        }

        public bool TryBuildFromGhost(BuildRequest request, int revision)
        {
            if (Busy || request?.Plan == null || !_gWanted || _gStep != GhostStep.Shown || _gShown != revision || revision != _gRequested) return false;
            if (_gDetails.Partial || request.ReplaceGroup != 0 || request.Plan.HasErrors) return false;
            if (_catalog == null || !_catalog.Ready || _work == null || _work.Terraform.Count > 0) return false;
            if (_work.DefinitionCount == 0) return false;   // zoning only (a Zoning type): no temporaries to adopt, the normal build zones
            foreach (var l in request.Plan.Layers) if (l?.Paint != null && l.Paint.Count > 0) return false;   // the ghost never paints
            if (!ReferenceEquals(request.Plan, _gPlan)) return false;   // must be the very plan the ghost shows
            // Adopt: keep _work, prefabs, indexes, own definitions and the temporaries.
            _request = request;
            _gEmission = false;
            _instant = true;
            _gHasContent = false;
            _gWanted = false;
            _gShown = _gRequested = _gEmitting = -1;
            _gStep = GhostStep.Idle;
            _gPlan = null;
            _startFrame = UnityEngine.Time.frameCount;
            _status = new BuildStatus { Phase = BuildPhase.Preparing, Progress = 0.1f, ErrorPositions = new List<float3>(), Warnings = new List<PlanIssue>() };
            Go(Step.ActivateTool);
            TimingBegin();
            Mod.Log.Info($"Placemaker instant build from the ghost (revision {revision}).");
            return true;
        }

        // ───────────────────────── emission ─────────────────────────

        private bool BeginGhost(int frame)
        {
            long t0 = Now;
            DestroyGhostDefinitions();   // old ghost: definitions now, temporaries in this Clear frame
            ResetTransaction();          // idle: only clears emission state (no tool is held)
            Sub("Destroy old ghost", t0);
            _instant = false;
            var plan = _gPlan;
            _gEmitting = _gRequested;
            _gHasContent = true;
            _gDetails = new GhostDetails { ErrorPositions = new List<float3>() };
            _request = new BuildRequest { Plan = plan };
            t0 = Now;
            _catalog.ResolveAutomatic(plan);   // idempotent; the tool normally did it already
            Sub("ResolveAutomatic", t0); t0 = Now;
            _work = PlanWork.Flatten(plan);
            Sub("Flatten", t0); t0 = Now;
            ResolvePrefabs();
            Sub("ResolvePrefabs", t0);
            _groupId = _registry.AllocateId();   // the id the build will get → same seeds/variations
            _gEmission = true;
            int rest = _work.Surfaces.Count + _work.Areas.Count;
            int objects = _work.Objects.Count;
            int budget = math.max(0, GhostMaxDefinitions - rest);
            _gObjects = new List<int>(math.min(objects, budget));
            if (objects <= budget) for (int i = 0; i < objects; i++) _gObjects.Add(i);
            else
            {
                _gDetails.Partial = true;
                double stride = objects / (double)math.max(1, budget);
                for (int k = 0; k < budget; k++) _gObjects.Add((int)(k * stride));
            }
            _gTotal = rest + _gObjects.Count;
            _gDetails.Planned = _work.Nets.Count + rest + objects;
            _gCursor = 0;
            t0 = Now;
            _gPrep = PrepareRingsAsync();
            Sub("PrepareRings (start)", t0);
            _gStep = GhostStep.Preparing;
            return true;   // the Clear frame: the old ghost's temporaries go while the rings are prepared
        }

        private System.Threading.Tasks.Task _gPrep;

        /// <summary>The rings are ready (or failed: the emission prepares them itself): the ghost is emitted as before.</summary>
        private void StartGhostEmission(int frame)
        {
            if (_gPrep != null && !_gPrep.IsCompleted) return;
            if (_gPrep != null && _gPrep.IsFaulted) Mod.Log.Warn("Placemaker ghost: ring preparation failed: " + _gPrep.Exception?.GetBaseException().Message);
            _gPrep = null;
            long t0 = Now;
            EmitNets();
            Sub("EmitNets", t0); t0 = Now;
            EmitClearDefinitions();
            Sub("EmitClearDefinitions", t0);
            _gStep = GhostStep.Emitting;
            EmitGhostBatch(frame);
        }

        private void EmitGhostBatch(int frame)
        {
            long t0 = Now;
            try { EmitGhostBatchCore(frame); }
            finally { Sub("Emit definitions", t0); }
        }

        private void EmitGhostBatchCore(int frame)
        {
            int end = math.min(_gTotal, _gCursor + math.max(50, GhostPerFrame));
            int s = _work.Surfaces.Count, a = _work.Areas.Count;
            for (int i = _gCursor; i < end; i++)
            {
                if (i < s) EmitSurface(_work.Surfaces[i]);
                else if (i < s + a) EmitArea(_work.Areas[i - s]);
                else EmitObject(_work.Objects[_gObjects[i - s - a]]);
            }
            _gCursor = end;
            _gDetails.Emitted = _work.Nets.Count + end;
            if (_gCursor >= _gTotal)
            {
                _gStep = GhostStep.Settling;
                _gStepFrame = frame;
            }
        }

        /// <summary>Ghost temporaries keep their error state but never show icons; we count them instead.</summary>
        private void HideGhostIcons()
        {
            if (_visibleTempIconQuery.IsEmptyIgnoreFilter) return;
            var positions = _gDetails.ErrorPositions;
            if (positions != null && positions.Count < 64)
            {
                using var icons = _visibleTempIconQuery.ToComponentDataArray<global::Game.Notifications.Icon>(Allocator.Temp);
                for (int i = 0; i < icons.Length && positions.Count < 64; i++) positions.Add(icons[i].m_Location);
            }
            EntityManager.AddComponent<Hidden>(_visibleTempIconQuery);
        }

        private void CollectGhostFacts()
        {
            _gDetails.Errors = _errorQuery.CalculateEntityCount();
            int dropped = 0;
            using (var temps = _cancelledQuery.ToComponentDataArray<Temp>(Allocator.Temp))
                for (int i = 0; i < temps.Length; i++) if ((temps[i].m_Flags & TempFlags.Cancel) != 0) dropped++;
            _gDetails.Dropped = dropped;
            if (_gDetails.Errors > 0 || dropped > 0)
                Mod.Log.Info($"Placemaker ghost {_gEmitting}: {_gDetails.Emitted}/{_gDetails.Planned} parts, {_gDetails.Errors} rejected, {dropped} optional objects dropped (collisions){(_gDetails.Partial ? ", partial" : "")}.");
        }

        /// <summary>Own definitions; while the Placemaker tool is active every definition is ours (CourseSplit derives unmarked ones).</summary>
        private void DestroyGhostDefinitions()
        {
            foreach (var d in _ownDefinitions)
                if (EntityManager.Exists(d) && !EntityManager.HasComponent<Deleted>(d)) EntityManager.AddComponent<Deleted>(d);
            _ownDefinitions.Clear();
            if (_toolSystem.activeTool is Placemaker.Game.Tool.PlacemakerToolSystem && !_definitionQuery.IsEmptyIgnoreFilter)
                EntityManager.AddComponent<Deleted>(_definitionQuery);
        }

        /// <summary>
        /// Called from OnUpdate while idle: the tool stopped pumping (closed, another tool active) but ghost
        /// temporaries exist → remove them ourselves so no other tool can ever apply them.
        /// </summary>
        private void GhostWatchdog()
        {
            if (!_gHasContent || Busy) return;
            if (UnityEngine.Time.frameCount - _gLastPump <= GhostWatchdogFrames) return;
            try
            {
                DestroyGhostDefinitions();
                World.GetOrCreateSystemManaged<UpdateSystem>().Update(SystemUpdatePhase.ClearTool);
                Mod.Log.Info("Placemaker removed an orphaned ghost preview (the tool stopped driving it).");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker ghost cleanup failed: " + e.Message); }
            ResetTransaction();
            ResetGhost();
        }

        /// <summary>A real build starts: the ghost is dropped (the build's first Clear frame removes the temporaries).</summary>
        private void DropGhostForBuild()
        {
            if (_gHasContent) DestroyGhostDefinitions();
            ResetGhost();
        }
    }
}
