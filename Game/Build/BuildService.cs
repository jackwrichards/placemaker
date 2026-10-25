// Placemaker — build transaction: ComposedPlan → game entities → one group (GPL-3.0).
//
// The transaction shape follows ParkManager's build workflow (tomkpunkt, GPL-3.0): definitions →
// temporaries → game validation → apply → adopt. Improvements over the sources:
//  * Ownership follows the game's own Temp→permanent mechanism: Apply turns a Temp entity with no
//    original into the permanent entity IN PLACE (ApplyObjects/Net/AreasSystem.Create), so a tag
//    added to our temporaries before Apply survives. Temporaries are recognised by prefab AND
//    geometry against the plan (PlanIndex) — never by a baseline diff that could adopt foreign
//    entities or split pieces of an existing road.
//  * (s59) Validation is read once (BuildService.Verdicts): Apply never reads Error, so under anarchy everything is applied;
//    vanilla rules leave refused decorative pieces out in place, once. Money is charged by vanilla ToolApplySystem from Temp.m_Cost.
//  * Every step is frame-bounded with timeouts and wrapped in try/catch; a failure before Apply
//    clears the temporaries, a failure after Apply rolls the group back through the removal system.
//
// Phase: ToolUpdate, before BuildToolSystem (it hands our requested applyMode to the game in the
// same frame). Runs while paused (ToolSystem updates every frame).

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Game.Assets;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService : GameSystemBase, IBuildService
    {
        // Frame budgets (measured in source mods: definitions materialise next frame; validation
        // marks errors within ~3 frames; zone blocks of new roads appear within ~20 frames).
        private const int ToolSwitchFrames = 2;
        private const int MaterializeTimeoutFrames = 180;
        private const int MaterializeStableFrames = 12;
        private const int ValidationSettleFrames = 3;
        private const int ApplySettleFrames = 3;
        private const int ApplyTimeoutFrames = 90;
        private const int ReplaceTimeoutFrames = 900;
        private const int TerraformTimeoutFrames = 900;
        private const int ZoningWaitFrames = 24;
        /// <summary>Objects/areas emitted per frame; nets always go in one frame so junctions merge.</summary>
        public const int DefinitionsPerFrame = 1500;

        /// <summary>
        /// Setting "Leave out parts that collide" — only with "Ignore collisions in my builds" OFF (vanilla rules): decorative
        /// pieces the game refuses are left out in place, once (s59: no exclude-and-retry loop any more). Off: the build stops instead.
        /// </summary>
        public static bool AutoExcludeCollisions = true;

        /// <summary>Setting "Fix or skip rejected pieces" — same role as <see cref="AutoExcludeCollisions"/> (either one enables it).</summary>
        public static bool RecoverRejectedPieces = true;

        private enum Step
        {
            Idle, Prepare, WaitReplaced, ActivateTool, Terraform, Paint, EmitNets, EmitRest, WaitTemps, Validate,
            ResetForRetry, OwnerLot, Apply, Settle, ZoneRefresh, ZoneBlock, ZoningWait, ZoningEmit, ZoningApply, Finish, Cleanup,
            RoadPreZone,
        }

        private ToolSystem _toolSystem;
        private BuildToolSystem _tool;
        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private WaterSystem _water;
        private AssetCatalogSystem _catalog;
        private GroupRegistrySystem _registry;
        private SurfaceCloneSystem _clones;

        private EntityQuery _tempQuery;
        private EntityQuery _errorQuery;
        private EntityQuery _iconQuery;
        private EntityQuery _definitionQuery;

        private BuildStatus _status;
        private Step _step;
        private int _stepFrame;
        /// <summary>
        /// Set by Go: the next OnUpdate is the step's first frame (_stepFrame = that frame). Go is usually
        /// called from inside the previous step, so without this "frame == _stepFrame" never held and the
        /// first-frame work of ResetForRetry (destroy definitions + Clear), Paint and Cleanup was SKIPPED:
        /// a retry kept the first attempt's temporaries and Apply built everything twice (playtest 4b).
        /// </summary>
        private bool _stepFresh;
        private int _startFrame;
        private BuildRequest _request;
        private BuildWork _work;
        private ToolBaseSystem _previousTool;
        private bool _toolActivated;
        private bool _applied;
        private int _groupId;
        private Entity _record;
        private Entity _carrier;
        private BuildPhase _cleanupPhase;
        private string _cleanupKey;
        private object[] _cleanupArgs;
        private int _emitCursor;
        private int _lastMatched = -1;
        private int _stableSince;
        private int _dropped;
        private readonly List<string> _warnings = new List<string>();

        public BuildStatus Status => _status;

        /// <summary>True while a build is between Start and Done/Failed/Cancelled.</summary>
        public bool Busy => _step != Step.Idle;

        public bool IsBuildingGroup(int groupId) => Busy && groupId != 0 && groupId == _groupId;

        /// <summary>
        /// True while the build has taken over the active tool. A tool that is deactivated for the
        /// build should keep its state when this is true (it gets reactivated afterwards).
        /// </summary>
        public bool HoldsTool => _toolActivated;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _tool = World.GetOrCreateSystemManaged<BuildToolSystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _water = World.GetOrCreateSystemManaged<WaterSystem>();
            _catalog = World.GetOrCreateSystemManaged<AssetCatalogSystem>();
            _registry = World.GetOrCreateSystemManaged<GroupRegistrySystem>();
            _clones = World.GetOrCreateSystemManaged<SurfaceCloneSystem>();
            InitOwner();
            InitFastReads();
            InitGhost();
            InitProtect();
            InitVerdicts();
            _tempQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _errorQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Error>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _iconQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Notifications.Icon>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _definitionQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CreationDefinition>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _status = new BuildStatus { Phase = BuildPhase.Idle };
            Services.Build = this;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (ReferenceEquals(Services.Build, this)) Services.Build = null;
            Placemaker.Game.Preview.GhostPreview.Unregister(this);
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // The world is being replaced: nothing to clean up, just forget the transaction (and do
            // not touch the tool system while it is being reset for the new world).
            _toolActivated = false;
            _previousTool = null;
            ResetTransaction();
            ResetGhost();   // the temporaries die with the old world
            _status = new BuildStatus { Phase = BuildPhase.Idle };
        }

        // ───────────────────────── IBuildService ─────────────────────────

        public bool Start(BuildRequest request)
        {
            if (Busy)
            {
                _status.MessageKey = "Placemaker.Build.Busy";
                return false;
            }
            if (request?.Plan == null)
            {
                SetFailed("Placemaker.Build.NothingToBuild");
                return false;
            }
            if (request.Plan.HasErrors)
            {
                SetFailed("Placemaker.Build.PlanHasErrors");
                return false;
            }
            if (_catalog == null || !_catalog.Ready)
            {
                SetFailed("Placemaker.Build.CatalogNotReady");
                return false;
            }
            DropGhostForBuild();   // the build's first Clear frame removes the ghost's temporaries
            ResetTransaction();
            _request = request;
            _startFrame = UnityEngine.Time.frameCount;
            TimingBegin();
            DumpBegin();   // s30: "Debug: dump terrain after build" (ground before the build)
            _status = new BuildStatus { Phase = BuildPhase.Preparing, Progress = 0f, ErrorPositions = new List<float3>(), Warnings = new List<PlanIssue>() };
            // US5: the same concrete assets as preview and price (idempotent if the tool already did it).
            int auto = _catalog.ResolveAutomatic(request.Plan);
            if (auto > 0) Mod.Log.Info($"Placemaker resolved {auto} automatic assets at build start (the tool should do this before preview/pricing).");
            Go(Step.Prepare);
            Mod.Log.Info($"Placemaker build started: {request.Plan.Layers.Count} layers, replace={request.ReplaceGroup}{(request.Ungrouped ? ", ungrouped" : "")}.");
            if (request.Plan.KerbJoins + request.Plan.CrosswalkJoins > 0 || request.Plan.JoinNotes.Count > 0)
                Mod.Log.Info($"Placemaker street joins: {request.Plan.KerbJoins} footpath end(s) stop at the kerb (the game joins them), {request.Plan.CrosswalkJoins} split the street with a crosswalk."
                    + (request.Plan.JoinNotes.Count > 0 ? " " + string.Join("; ", request.Plan.JoinNotes) : ""));
            return true;
        }

        public void Cancel()
        {
            if (!Busy || _step == Step.Cleanup) return;
            if (_step == Step.Finish) return;   // last frame: let it finish
            BeginCleanup(BuildPhase.Cancelled, "Placemaker.Build.Cancelled");
        }

        public void Acknowledge()
        {
            if (Busy) return;
            if (_status.Phase == BuildPhase.Done || _status.Phase == BuildPhase.Failed || _status.Phase == BuildPhase.Cancelled)
                _status = new BuildStatus { Phase = BuildPhase.Idle, ErrorPositions = new List<float3>(), Warnings = new List<PlanIssue>() };
        }

        /// <summary>US8: red markers vanish as soon as the user changes anything (tool calls this).</summary>
        public void ClearErrors()
        {
            _status.ErrorPositions = new List<float3>();
        }

        // ───────────────────────── fixed / skipped pieces (US8) ─────────────────────────

        private sealed class SkipInfo { public int Count; public float3? At; public IssueSeverity Severity; }
        private readonly Dictionary<string, SkipInfo> _skipped = new Dictionary<string, SkipInfo>(StringComparer.Ordinal);

        /// <summary>Counts a fixed (Info) or skipped (Warning) piece under a localisation key; arg 0 = count.</summary>
        private void NoteSkip(string key, float3? at, IssueSeverity severity = IssueSeverity.Warning, int count = 1)
        {
            if (!_skipped.TryGetValue(key, out var s)) _skipped[key] = s = new SkipInfo { Severity = severity };
            s.Count += count;
            if (!s.At.HasValue && at.HasValue) s.At = at;
        }

        private List<PlanIssue> ComposeWarnings(int dropped)
        {
            var list = new List<PlanIssue>();
            foreach (var kv in _skipped)
                list.Add(new PlanIssue { Severity = kv.Value.Severity, Key = kv.Key, Args = new object[] { kv.Value.Count }, At = kv.Value.At });
            foreach (var key in _warnings)
            {
                bool dup = false;
                foreach (var i in list) if (i.Key == key) { dup = true; break; }
                if (!dup) list.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = key, Args = new object[] { dropped } });
            }
            // Warnings (left out) before infos (repaired/cleared).
            list.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            return list;
        }

        /// <summary>Warnings of the last build (e.g. pieces the game dropped), as localisation keys.</summary>
        public IReadOnlyList<string> LastWarnings => _warnings;

        // ───────────────────────── state machine ─────────────────────────

        [Preserve]
        protected override void OnUpdate()
        {
            if (_step == Step.Idle) { GhostWatchdog(); return; }
            int frame = UnityEngine.Time.frameCount;
            _heightsValid = false;   // terrain data handles are only valid within the frame they were fetched
            if (_stepFresh) { _stepFresh = false; _stepFrame = frame; }
            var ran = _step;
            _frameClock.Restart();
            try
            {
                if (_toolActivated && _step != Step.Cleanup && _toolSystem.activeTool != _tool)
                {
                    // The player (or a mod hotkey) switched tools mid-build.
                    if (!_applied) { BeginCleanup(BuildPhase.Failed, "Placemaker.Build.Interrupted"); return; }
                    _toolSystem.activeTool = _tool;   // after Apply we only need a few quiet frames
                }
                switch (_step)
                {
                    case Step.Prepare: DoPrepare(); break;
                    case Step.WaitReplaced: DoWaitReplaced(frame); break;
                    case Step.ActivateTool: DoActivateTool(frame); break;
                    case Step.Terraform: DoTerraform(frame); break;
                    case Step.Paint: DoPaint(frame); break;
                    case Step.EmitNets: DoEmitNets(); break;
                    case Step.EmitRest: DoEmitRest(); break;
                    case Step.WaitTemps: DoWaitTemps(frame); break;
                    case Step.Validate: DoValidate(frame); break;
                    case Step.ResetForRetry: DoResetForRetry(frame); break;
                    case Step.OwnerLot: DoOwnerLot(frame); break;
                    case Step.Apply: DoApply(); break;
                    case Step.Settle: DoSettle(frame); break;
                    case Step.ZoneRefresh: DoZoneRefresh(frame); break;
                    case Step.RoadPreZone: DoRoadPreZone(frame); break;
                    case Step.ZoneBlock: DoZoneBlock(frame); break;
                    case Step.ZoningWait: if (frame - _stepFrame >= ZoningWaitFrames) Go(Step.ZoningEmit); break;
                    case Step.ZoningEmit: DoZoningEmit(); break;
                    case Step.ZoningApply: DoZoningApply(frame); break;
                    case Step.Finish: DoFinish(); break;
                    case Step.Cleanup: DoCleanup(frame); break;
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, $"Placemaker build failed in step {_step}");
                if (_step == Step.Cleanup) ForceIdle(BuildPhase.Failed, "Placemaker.Build.Failed");
                else BeginCleanup(BuildPhase.Failed, "Placemaker.Build.Failed", e.GetType().Name);
            }
            TimingFrame(ran, frame, _frameClock.Elapsed.TotalMilliseconds);
            if (_step == Step.Idle) TimingLog(_status.Phase.ToString());
        }

        private void Go(Step step)
        {
            _step = step;
            _stepFrame = UnityEngine.Time.frameCount;
            _stepFresh = true;
        }

        private void DoPrepare()
        {
            SnapshotPreviewZoning();
            _work = PlanWork.Flatten(_request.Plan);
            if (_work.DefinitionCount == 0 && _work.Terraform.Count == 0 && _work.Zoning.Count == 0 && !HasPaint())
            {
                ForceIdle(BuildPhase.Failed, "Placemaker.Build.NothingToBuild");
                return;
            }
            ResolvePrefabs();
            if (_request.ReplaceGroup != 0 && _registry.TryGetRecordEntity(_request.ReplaceGroup, out _))
            {
                // Edit = remove the old group, then build the new one. The old receipt stays in the
                // tool's hands (GetReceipt before Start) so a failed rebuild can be retried/restored.
                if (!_registry.RequestRemove(_request.ReplaceGroup))
                {
                    ForceIdle(BuildPhase.Failed, "Placemaker.Build.ReplaceFailed");
                    return;
                }
                Go(Step.WaitReplaced);
                return;
            }
            Go(Step.ActivateTool);
        }

        private void DoWaitReplaced(int frame)
        {
            SetProgress(0.05f);
            if (!_registry.TryGetRecordEntity(_request.ReplaceGroup, out _)) { Go(Step.ActivateTool); return; }
            if (frame - _stepFrame > ReplaceTimeoutFrames) ForceIdle(BuildPhase.Failed, "Placemaker.Build.ReplaceFailed");
        }

        private void DoActivateTool(int frame)
        {
            if (!_toolActivated)
            {
                _previousTool = _toolSystem.activeTool;
                _toolSystem.activeTool = _tool;
                _toolActivated = true;
                _stepFrame = frame;
                return;
            }
            if (_toolSystem.activeTool != _tool) { BeginCleanup(BuildPhase.Failed, "Placemaker.Build.Interrupted"); return; }
            if (_instant)
            {
                // Instant build: the ghost's temporaries ARE the build — no Clear, no re-emission.
                if (frame - _stepFrame < ToolSwitchFrames + 1) return;
                CreateRecord();   // the owner lot follows after validation (Step.OwnerLot)
                _status.Phase = BuildPhase.Materializing;
                SetProgress(0.4f);
                _lastMatched = -1;
                Go(Step.WaitTemps);
                return;
            }
            if (frame - _stepFrame == 1)
            {
                // Clear leftovers of the previous tool so our single Apply never applies them.
                long tf = Now;
                DestroyForeignDefinitions();
                Sub("DestroyForeignDefinitions", tf);
                _tool.Requested = ApplyMode.Clear;
                return;
            }
            if (frame - _stepFrame < ToolSwitchFrames + 1) return;
            long tr = Now;
            CreateRecord();
            Sub("CreateRecord", tr);
            _status.Phase = BuildPhase.Materializing;
            Go(_work.Terraform.Count > 0 ? Step.Terraform : AfterTerraform());
            if (_step == Step.Terraform) BeginTerraform();
        }

        private void DoEmitNets()
        {
            SetProgress(0.2f);
            EmitNets();
            _emitCursor = 0;
            Go(Step.EmitRest);
        }

        private void DoEmitRest()
        {
            int total = _work.Surfaces.Count + _work.Areas.Count + _work.Objects.Count;
            if (_emitCursor == 0)
            {
                // Same transaction as the build: old trees/props vanish on the one Apply, or not at all.
                // The owner lot is emitted after validation (Step.OwnerLot) so its overlaps never reject the plan.
                long tc = Now;
                EmitClearDefinitions();
                Sub("EmitClearDefinitions", tc);
            }
            long te = Now;
            int end = math.min(total, _emitCursor + DefinitionsPerFrame);
            for (int i = _emitCursor; i < end; i++)
            {
                if (i < _work.Surfaces.Count) EmitSurface(_work.Surfaces[i]);
                else if (i < _work.Surfaces.Count + _work.Areas.Count) EmitArea(_work.Areas[i - _work.Surfaces.Count]);
                else EmitObject(_work.Objects[i - _work.Surfaces.Count - _work.Areas.Count]);
            }
            _emitCursor = end;
            Sub("Emit definitions", te);
            SetProgress(0.2f + 0.2f * (total == 0 ? 1f : end / (float)total));
            if (_emitCursor >= total)
            {
                _lastMatched = -1;
                if (_itemByKey.Count == 0)
                {
                    if (_work.DefinitionCount > 0) { BeginCleanup(BuildPhase.Failed, "Placemaker.Build.AssetsUnavailable"); return; }
                    // Terraform/zoning-only plan: nothing to adopt, nothing to apply.
                    _applied = true;
                    AfterApply();
                    GoAfterApply();
                    return;
                }
                Go(Step.WaitTemps);
            }
        }

        private void DoWaitTemps(int frame)
        {
            if (frame - _stepFrame < 2) return;
            long t0 = Now;
            var census = Census();
            Sub("Census", t0);
            SetProgress(0.4f + 0.15f * (census.Expected == 0 ? 1f : math.saturate(census.Matched / (float)census.Expected)));
            if (census.Matched >= census.Expected)
            {
                Go(Step.Validate);
                _status.Phase = BuildPhase.Validating;
                return;
            }
            if (census.Matched != _lastMatched) { _lastMatched = census.Matched; _stableSince = frame; }
            bool stable = frame - _stableSince >= MaterializeStableFrames && census.Matched > 0;
            if (stable)
            {
                // Some pieces never materialised (the game silently drops e.g. surfaces it cannot
                // triangulate). Build what exists and report the gap.
                _dropped = census.Expected - census.Matched;
                _warnings.Add("Placemaker.Build.PartsDropped");
                Mod.Log.Warn($"Placemaker: {_dropped} of {census.Expected} planned parts did not materialise; continuing.");
                Go(Step.Validate);
                _status.Phase = BuildPhase.Validating;
                return;
            }
            if (frame - _stepFrame > MaterializeTimeoutFrames)
            {
                CollectErrors();
                BeginCleanup(BuildPhase.Failed, _status.ErrorPositions.Count > 0 ? _errorKey : "Placemaker.Build.Timeout");
            }
        }

        /// <summary>
        /// (s59) ONE validation pass. The game's verdicts are read once (BuildVerdicts.Decide): under anarchy nothing is refused
        /// — every planned piece is applied; with vanilla rules the refused decorative pieces are cancelled in place. Existing
        /// buildings are never demolished (ProtectBuildings + NeutralizeVerdicts). No re-emission, no retry loop.
        /// </summary>
        private void DoValidate(int frame)
        {
            long t0 = Now;
            CollectErrors();
            Sub("CollectErrors", t0);
            if (frame - _stepFrame < ValidationSettleFrames) return;
            if (_tool.OriginalDeleted)
            {
                // Something we modify (an existing road we connect to) was removed meanwhile: nothing sensible to apply.
                Mod.Log.Warn("Placemaker build stopped: an existing piece this build changes was removed meanwhile.");
                BeginCleanup(BuildPhase.Failed, "Placemaker.Build.Interrupted");
                return;
            }
            t0 = Now;
            bool verdictsOk = ApplyVerdicts();
            Sub("ApplyVerdicts", t0);
            if (!verdictsOk)
            {
                BeginCleanup(BuildPhase.Failed, _errorKey, _errorArgs);
                return;
            }
            // (s15) Never demolish an existing building: its touching pieces are left out in place (nets: see ProtectBuildings).
            t0 = Now;
            int protectedN = ProtectBuildings();
            Sub("ProtectBuildings", t0);
            if (protectedN < 0) { BeginCleanup(BuildPhase.Failed, "Placemaker.Build.WouldDemolish"); return; }
            t0 = Now;
            int tagged = TagTemporaries();
            Sub("TagTemporaries", t0);
            t0 = Now;
            bool duplicated = TemporariesDuplicated();
            Sub("TemporariesDuplicated", t0);
            if (duplicated)
            {
                // Never apply a doubled set: clear everything and emit once more, then give up.
                if (!_dupRetried) { _dupRetried = true; Go(Step.ResetForRetry); return; }
                BeginCleanup(BuildPhase.Failed, "Placemaker.Build.Duplicated");
                return;
            }
            if (tagged > 0 && !CanAffordNets(out int netCost))
            {
                Mod.Log.Warn($"Placemaker: the roads/paths of this build cost {netCost}, more than the city has.");
                BeginCleanup(BuildPhase.Failed, BuildErrorKeys.For("NotEnoughMoney"), 1, "NotEnoughMoney");
                return;
            }
            if (tagged == 0)
            {
                BeginCleanup(BuildPhase.Failed, "Placemaker.Build.NothingMaterialized");
                return;
            }
            _status.ErrorPositions.Clear();   // transient errors seen while settling are not real
            _status.Phase = BuildPhase.Applying;
            SetProgress(0.65f);
            Go(Step.OwnerLot);
        }

        private void DoResetForRetry(int frame)
        {
            if (frame == _stepFrame)
            {
                DestroyOwnDefinitions();
                ClearCarrierSubNets();
                _tool.Requested = ApplyMode.Clear;
                return;
            }
            if (frame - _stepFrame < 2) return;
            ResetIndexes();
            ResetVerdicts();
            _errorTypeName = "";
            _work = PlanWork.Flatten(_request.Plan);
            ResolvePrefabs();
            if (_work.DefinitionCount == 0) { BeginCleanup(BuildPhase.Failed, "Placemaker.Build.NothingToBuild"); return; }
            _status.Phase = BuildPhase.Materializing;
            _status.ErrorPositions.Clear();
            Go(Step.EmitNets);
        }

        private void DoApply()
        {
            ScanLivePits("before Apply, temporaries in place");
            LogSaggingEdges("before Apply");
            long t0 = Now;
            TagLateTemporaries();
            Sub("TagLateTemporaries", t0); t0 = Now;
            PinMarkerNets(temporaries: true);   // (core) pinned before the first terrain pass after Apply
            Sub("PinMarkerNets", t0); t0 = Now;
            NeutralizeVerdicts();
            Sub("NeutralizeVerdicts", t0);   // (s59) nothing of ours deleted/hidden by a verdict under anarchy; no existing building demolished
            _tool.Requested = ApplyMode.Apply;
            _applied = true;   // from here on, failures roll back through group removal
            SetProgress(0.75f);
            _status.Phase = BuildPhase.Settling;
            Go(Step.Settle);
        }

        private void DoSettle(int frame)
        {
            if (frame - _stepFrame < ApplySettleFrames) return;
            if (frame - _stepFrame == ApplySettleFrames) DestroyOwnDefinitions();
            long t0 = Now;
            int stillTemp = CountTaggedTemporaries();
            Sub("CountTaggedTemporaries", t0);
            if (stillTemp > 0 && frame - _stepFrame < ApplyTimeoutFrames) return;
            if (stillTemp > 0) Mod.Log.Warn($"Placemaker: {stillTemp} parts stayed temporary after Apply; they are discarded.");
            t0 = Now;
            AdoptStrays();
            Sub("AdoptStrays", t0);
            _registry.MarkDirty();
            // Loose builds (road grids) have no group: count what this build claimed and Apply kept.
            t0 = Now;
            int members = LooseBuild ? CountAppliedClaimed() : _registry.CountMembers(_groupId, false);
            Sub("CountMembers", t0);
            if (members == 0)
            {
                CollectErrors();
                BeginCleanup(BuildPhase.Failed, _status.ErrorPositions.Count > 0 ? _errorKey : "Placemaker.Build.ApplyRejected", _errorArgs);
                return;
            }
            t0 = Now;
            ChargeNets();
            Sub("ChargeNets", t0); t0 = Now;
            AfterApply();
            Sub("AfterApply", t0); t0 = Now;
            PinMarkerNets();
            Sub("PinMarkerNets", t0);   // (core) no sag of marker nets (lot drives) at lane junctions: the dip
            LogSaggingEdges("applied");
            SetProgress(0.85f);
            GoAfterApply();
        }

        private void DoFinish()
        {
            _registry.MarkDirty();
            long t0 = Now;
            bool twice = VerifyMembers();
            Sub("VerifyMembers", t0);
            if (twice)
            {
                // Built twice despite the pre-Apply check: roll the whole group back (Cleanup after Apply
                // removes it through the phase-correct removal system) and report.
                BeginCleanup(BuildPhase.Failed, "Placemaker.Build.Duplicated");
                return;
            }
            if (LooseBuild) Mod.Log.Info($"Placemaker loose build: {CountAppliedClaimed()} parts are ordinary city content (no group).");
            else { t0 = Now; FinishRecord(); Sub("FinishRecord", t0); }
            t0 = Now;
            RestoreTool();
            Sub("RestoreTool", t0);
            var groupId = _groupId;
            int dropped = _dropped;
            if (_cleared > 0) NoteSkip("Placemaker.Build.Cleared", null, IssueSeverity.Info, _cleared);
            if (_buildingsProtected > 0) NoteSkip("Placemaker.Build.BuildingsKept", null, IssueSeverity.Info, _buildingsProtected);
            if (_ownerTemp == Entity.Null) NoteSkip("Placemaker.Build.OwnerUnavailable", null, IssueSeverity.Info, 0);
            var issues = ComposeWarnings(dropped);
            var warnings = new List<string>(_warnings);
            DumpFinish(groupId);   // s30: live ground, water, nets and objects to ModsData/Placemaker/debug (when enabled)
            VerifyLiveHollows();   // s55: a bowl in the built ground, with the target's height next to it
            ScanLivePits("after the build");
            LogSaggingEdges("after the build");   // (core) every bowl in the whole outline and its rim, with the nets standing at it
            t0 = Now;
            AuditSurvivors();   // s55: count what the game really left visible (Overridden = hidden), not the temporaries
            Sub("AuditSurvivors", t0); t0 = Now;
            var audit = FinishAudit(groupId);
            Sub("FinishAudit", t0);
            ResetTransaction();
            _warnings.AddRange(warnings);
            PlanIssue headline = null;
            foreach (var i in issues) if (i.Severity == IssueSeverity.Warning) { headline = i; break; }
            _status = new BuildStatus
            {
                Phase = BuildPhase.Done,
                Progress = 1f,
                GroupId = groupId,
                MessageKey = headline != null ? headline.Key : "Placemaker.Build.Done",
                MessageArgs = headline != null ? headline.Args : new object[] { dropped },
                // A successful build never shows red markers (US8).
                ErrorPositions = new List<float3>(),
                Warnings = issues,
                Audit = audit,
            };
            Mod.Log.Info($"Placemaker build finished: group {groupId}, {_registry.CountMembers(groupId, true)} members, "
                + $"{issues.Count} notes, {UnityEngine.Time.frameCount - _startFrame} frames.");
        }

        // ───────────────────────── failure / cleanup ─────────────────────────

        private void BeginCleanup(BuildPhase phase, string key, params object[] args)
        {
            _cleanupPhase = phase;
            _cleanupKey = string.IsNullOrEmpty(key) ? "Placemaker.Build.Failed" : key;
            _cleanupArgs = args ?? Array.Empty<object>();
            Go(Step.Cleanup);
            Mod.Log.Warn($"Placemaker build {phase}: {_cleanupKey}");
        }

        /// <summary>
        /// Frame 0: destroy definitions, request Clear (removes our temporaries) unless Apply already
        /// ran; after Apply, roll back through the regular group removal. Frame 2: restore the tool.
        /// </summary>
        private void DoCleanup(int frame)
        {
            if (frame == _stepFrame)
            {
                DestroyOwnDefinitions();
                ClearCarrierSubNets();
                DiscardTerraformBrushes();
                if (_toolActivated && _toolSystem.activeTool == _tool) _tool.Requested = ApplyMode.Clear;
                if (_applied && _groupId != 0)
                {
                    RemoveUnderConstruction();
                    _registry.MarkDirty();
                    _registry.RemoveInternal(_groupId);   // bypasses the "being built" guard on purpose
                }
                return;
            }
            if (frame - _stepFrame < 2) return;
            if (!_applied) DeleteRecordAndCarrier();
            ForceIdle(_cleanupPhase, _cleanupKey, _cleanupArgs);
        }

        private void ForceIdle(BuildPhase phase, string key, params object[] args)
        {
            RestoreTool();
            var errors = phase == BuildPhase.Failed ? _status.ErrorPositions ?? new List<float3>() : new List<float3>();
            var issues = ComposeWarnings(_dropped);
            ResetTransaction();
            _status = new BuildStatus
            {
                Phase = phase, Progress = 0f, MessageKey = key, MessageArgs = args ?? Array.Empty<object>(), ErrorPositions = errors,
                Warnings = issues,
            };
        }

        private void SetFailed(string key)
        {
            _status = new BuildStatus { Phase = BuildPhase.Failed, MessageKey = key, MessageArgs = Array.Empty<object>(), ErrorPositions = new List<float3>(), Warnings = new List<PlanIssue>() };
        }

        private void RestoreTool()
        {
            if (!_toolActivated) return;
            _toolActivated = false;
            try
            {
                if (_toolSystem.activeTool == _tool)
                {
                    var prev = _previousTool;
                    _toolSystem.activeTool = prev != null && prev != _tool && prev.World == World ? prev : World.GetOrCreateSystemManaged<DefaultToolSystem>();
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker could not restore the previous tool: " + e.Message);
            }
            _previousTool = null;
        }

        private void ResetTransaction()
        {
            _step = Step.Idle;
            _request = null;
            _work = null;
            _zrJob = null;   // a running zoning refresh disposes its own height copy; its result is dropped
            ResetAudit();
            ResetVerdicts();
            _applied = false;
            _groupId = 0;
            _record = Entity.Null;
            _carrier = Entity.Null;
            _emitCursor = 0;
            _lastMatched = -1;
            _dropped = 0;
            _refusedLots = 0;
            _warnings.Clear();
            _ownDefinitions.Clear();
            _itemPrefab.Clear();
            _itemByKey.Clear();
            _tagged.Clear();
            _claimed.Clear();
            _usedPrefabNames.Clear();
            _usedPrefabs.Clear();
            _preexisting.Clear();
            ResetIndexes();
            _errorKey = BuildErrorKeys.Other;
            _errorArgs = Array.Empty<object>();
            _errorTypeName = "";
            ResetTerraform();
            ResetOwner();
            ResetProtect();
            ResetZoneBlock();
            _ownerSuppressed = false;
            _skipped.Clear();
            _notedRings.Clear();
            _dupRetried = false;
            _instant = false;
            if (_toolActivated) RestoreTool();
        }

        private void DestroyForeignDefinitions()
        {
            using var defs = _definitionQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < defs.Length; i++)
                if (!_ownDefinitions.Contains(defs[i])) EntityManager.AddComponent<Deleted>(defs[i]);
        }

        private void DestroyOwnDefinitions()
        {
            foreach (var d in _ownDefinitions)
                if (EntityManager.Exists(d) && !EntityManager.HasComponent<Deleted>(d)) EntityManager.AddComponent<Deleted>(d);
            _ownDefinitions.Clear();
            // CourseSplitSystem derives extra definitions from ours without any mark (PLT measured
            // this). While our tool is active every definition is ours, so remove them all.
            if (_toolActivated && _toolSystem.activeTool == _tool) DestroyForeignDefinitions();
        }
    }
}
