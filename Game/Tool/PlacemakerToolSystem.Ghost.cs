// Preview mode (sketch / ghost) and the ghost preview service: the rules are in
// Core/Tool/PreviewModeMachine (pure, tested); this file feeds it the tool's facts once per frame,
// forwards Show/Hide to the ghost service (Game/Preview/GhostPreviewClient) and owns applyMode:
// None keeps the ghost's temporaries, Clear only on the frames the service replaces or removes them,
// never Apply (the build applies through its own BuildToolSystem).

using System;
using Unity.Entities;
using Game.Tools;
using Placemaker.Core.Tool;
using Placemaker.Game.Preview;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private readonly PreviewModeMachine _previewModes = new PreviewModeMachine();
        private readonly GhostPreviewClient _ghost = new GhostPreviewClient();
        private PreviewDecision _preview = new PreviewDecision { Objects = SketchObjects.Full, ShowRevision = -1 };
        /// <summary>Frames left before Close switches tools (the ghost's Clear frame runs first). 0 = no close pending.</summary>
        private int _closeAfterGhost;

        // Ghost revision: bumps when the plan or its resolved assets change (assets are resolved in place).
        private int _ghostRevision;
        private int _ghostPlanRevision = int.MinValue, _ghostAssetsStamp = int.MinValue;

        /// <summary>contract state.previewMode: the player's choice for the current stage.</summary>
        public PreviewMode PreviewModeSelected => _previewModes.Selected(_stage);
        /// <summary>(shops4) The current plan is refused (an error): no ghost is shown for it.</summary>
        public bool PlanRefused => _plan != null && _plan.HasErrors;
        /// <summary>contract state.ghostPending.</summary>
        public bool GhostPending => _preview.GhostPending;
        /// <summary>The ghost's state in one line (the runner's idle timeout says why the preview never settled).</summary>
        public string GhostDebug { get { var st = _ghost.Status; return $"stage {_stage}, selected {_preview.Selected}, effective {_preview.Effective}, phase {st.Phase}, shown {st.ShownRevision}, requested {st.RequestedRevision}, ghost revision {_ghostRevision}, plan {_planRevision}/{_inputRevision}, plan errors {(_plan != null && _plan.HasErrors)}, available {_ghost.Available}, active {IsActive}, building {_building}"; } }
        /// <summary>What is on the map this frame (sketch during drags even when ghost is selected).</summary>
        public PreviewMode PreviewModeEffective => _preview.Effective;
        /// <summary>How the overlay draws the plan's nets/objects/surfaces (outline, markers, handles always drawn).</summary>
        public SketchObjects SketchObjects => _preview.Objects;
        public bool GhostAvailable => _ghost.Available;

        // Stuck-ghost safety net: a ghost that stays pending this long with no build running is asked for again
        // (twice), then this plan revision falls back to the sketch, so the panel never says "Updating preview…" for
        // ever (a failed instant build once left every later scenario of a game-runner batch waiting on it).
        private const float GhostStuckSeconds = 30f;
        private const int GhostStuckRetries = 2;
        private float _ghostPendingSince = -1f;
        private int _ghostKicks;
        private int _ghostGaveUpRevision = -2;

        private int _ghostDropped;
        /// <summary>Objects the game cancelled in the shown ghost (collisions); 0 when no ghost is shown.</summary>
        public int GhostDropped => _ghostDropped;

        /// <summary>Review note for objects the ghost could not place, or null (FEEDBACK-4: never silent).</summary>
        public Placemaker.Core.PlanIssue GhostDroppedIssue => _ghostDropped > 0
            ? new Placemaker.Core.PlanIssue { Severity = Placemaker.Core.IssueSeverity.Warning, Key = "Placemaker.Issue.GhostObjectsDropped", Args = new object[] { _ghostDropped } }
            : null;

        // The game outlines every temporary a tool places (BatchInstanceSystem: TempFlags.Create puts the mesh on the
        // outline layer too, ManagedBatchSystem draws that on Unity's "Outline" layer). The Real preview is all temporaries,
        // so the whole build was drawn in blue outlines ("I just want to actually see it"). Making the hover colour
        // see-through did not work (the outline ignores alpha: black lines). Leaving the Outline layer out of the camera did
        // not either: the game's outline pass asks for that layer (OutlinesWorldUIPass.AggregateCullingParameters), so HDRP
        // culled the whole scene a second time every frame for it (CustomPassVolume.Cull) and outlines everywhere flickered
        // while the camera moved. While the Real preview shows, the outline pass itself draws no layer (it culls nothing
        // extra and its buffer stays empty); its layer is put back the moment the preview goes (Sketch, Outgrow, another
        // stage, the tool closed). The build's own outline is drawn thick meanwhile (the overlay).
        private global::Game.Rendering.OutlinesWorldUIPass _outlinePass;
        private UnityEngine.LayerMask _outlineLayerSaved;
        private bool _outlineHidden;

        private void UpdateGhostOutline(bool hide)
        {
            if (hide == _outlineHidden) return;
            var pass = OutlinePass();
            if (pass == null) return;
            if (hide)
            {
                _outlineLayerSaved = pass.m_OutlineLayer;
                pass.m_OutlineLayer = 0;
            }
            else pass.m_OutlineLayer = _outlineLayerSaved;
            _outlineHidden = hide;
        }

        /// <summary>The game's outline pass (DebugCustomPass finds it the same way: by name, after post-processing).</summary>
        private global::Game.Rendering.OutlinesWorldUIPass OutlinePass()
        {
            if (_outlinePass != null) return _outlinePass;
            var volumes = new System.Collections.Generic.List<UnityEngine.Rendering.HighDefinition.CustomPassVolume>();
            UnityEngine.Rendering.HighDefinition.CustomPassVolume.GetActivePassVolumes(UnityEngine.Rendering.HighDefinition.CustomPassInjectionPoint.AfterPostProcess, volumes);
            foreach (var v in volumes)
                foreach (var p in v.customPasses)
                    if (p is global::Game.Rendering.OutlinesWorldUIPass o) return _outlinePass = o;
            return null;
        }

        /// <summary>UI: setPreviewMode("sketch" | "ghost") — for the current stage, sticks for the session.</summary>
        public void SetPreviewMode(string id)
        {
            if (!PreviewModes.TryParse(id, out var mode)) return;
            var stage = _stage == ToolStage.Building ? ToolStage.Review : _stage;
            if (_previewModes.Set(stage, mode)) Touch();
        }

        private int GhostRevision()
        {
            if (_plan == null) return -1;
            if (_ghostPlanRevision != _planRevision || _ghostAssetsStamp != _assetsResolvedStamp)
            {
                _ghostPlanRevision = _planRevision;
                _ghostAssetsStamp = _assetsResolvedStamp;
                _ghostRevision++;
            }
            return _ghostRevision;
        }

        /// <summary>End of every tool update: decide, talk to the service, return this frame's apply mode.</summary>
        private ApplyMode GhostFrame()
        {
            bool clear = false;
            try
            {
                var status = _ghost.Status;
                int dropped = status.Phase == GhostPhase.Shown ? status.Dropped : 0;
                if (dropped != _ghostDropped) { _ghostDropped = dropped; Touch(); }
                int rev = GhostRevision();
                var input = new PreviewInput
                {
                    Stage = _stage,
                    ToolActive = IsActive && _closeAfterGhost == 0,
                    BuildRunning = _building,
                    ValuePreview = ValuePreviewKind,
                    Dragging = _gesture != Gesture.None,
                    PlanAvailable = _plan != null,
                    PlanRevision = rev,
                    PlanStale = PlanStale,
                    PlanRefused = PlanRefused,
                    GhostAvailable = _ghost.Available && rev != _ghostGaveUpRevision,
                    GhostShownRevision = status.ShownRevision,
                    GhostBusy = status.Phase == GhostPhase.Pending,
                };
                var d = _previewModes.Step(input);
                if (d.Command == GhostCommand.Show) _ghost.Show(_plan, d.ShowRevision);
                else if (d.Command == GhostCommand.Hide) _ghost.Hide();
                WatchStuckGhost(d.GhostPending, rev, status);
                if (d.Selected != _preview.Selected || d.GhostPending != _preview.GhostPending || d.Objects != _preview.Objects || d.Effective != _preview.Effective) Touch();
                _preview = d;
                clear = _ghost.Pump();
            }
            catch (Exception e)
            {
                if (_errorCount++ < 20) Mod.Log.Error(e, "Placemaker: ghost preview frame failed");
            }
            if (_closeAfterGhost > 0 && --_closeAfterGhost == 0 && m_ToolSystem != null && m_ToolSystem.activeTool == this)
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            return clear ? ApplyMode.Clear : ApplyMode.None;
        }

        private void WatchStuckGhost(bool pending, int rev, GhostStatus status)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!pending || _building)
            {
                _ghostPendingSince = -1f;
                if (!pending) _ghostKicks = 0;
                return;
            }
            if (_ghostPendingSince < 0f) { _ghostPendingSince = now; return; }
            if (now - _ghostPendingSince < GhostStuckSeconds) return;
            bool retry = _ghostKicks < GhostStuckRetries;
            Mod.Log.Warn($"Placemaker: the ghost preview has been pending for {now - _ghostPendingSince:F0} s (stage {_stage}, plan revision {rev}, "
                + $"service {status.Phase}, requested {status.RequestedRevision}, shown {status.ShownRevision}"
                + $"{(GhostPreview.Service is Placemaker.Game.Build.BuildService b ? ", " + b.GhostDebug : "")}): "
                + (retry ? "asking for it again" : "showing the sketch for this plan instead"));
            if (retry) { _ghost.Hide(); _previewModes.Forget(); _ghostKicks++; }
            else { _ghostGaveUpRevision = rev; _ghostKicks = 0; }
            _ghostPendingSince = -1f;
            Touch();
        }

        /// <summary>Hide now (leaving the tool, build start). The service clears its definitions; the next tool's Clear removes the temporaries.</summary>
        private void HideGhost()
        {
            _ghost.Hide();
            _previewModes.Forget();
            _preview.Effective = PreviewMode.Sketch;
            _preview.Objects = SketchObjects.Full;
            _preview.GhostPending = false;
        }

        /// <summary>Close with a ghost on the map: hide it, keep the tool for its Clear frame, then switch. True = deferred.</summary>
        private bool BeginGhostHideForClose()
        {
            if (_closeAfterGhost > 0) return true;
            if (_ghost.Hidden || !_ghost.Available) return false;
            HideGhost();
            _closeAfterGhost = 2;   // frame 1: the service's Clear frame; frame 2: switch
            return true;
        }

        /// <summary>
        /// Build: when the ghost already shows exactly this plan and the service can apply it, build from it
        /// (instant); otherwise hide it (the build tool's first Clear frame removes the temporaries).
        /// </summary>
        private bool TryBuildFromGhost(BuildRequest request)
        {
            var status = _ghost.Status;
            int rev = GhostRevision();
            if (_preview.Effective == PreviewMode.Ghost && !PlanStale && status.Phase == GhostPhase.Shown && status.ShownRevision == rev
                && _ghost.TryBuildFromGhost(request, rev))
            {
                _previewModes.Forget();   // the service now owns those temporaries
                return true;
            }
            HideGhost();
            return false;
        }
    }
}
