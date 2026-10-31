// Build (via Services.Build, implemented by the build workstream) and the Manage stage
// (hover/click a built group → panel shows it; Edit loads its receipt back into the tool).

using System;
using System.Collections.Generic;
using Game.Common;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private bool _building;
        private BuildStatus _buildStatus;
        private readonly BuildFeedback _feedback = new BuildFeedback();
        private int _hoverGroup, _managedGroup;
        private readonly List<Entity> _members = new List<Entity>();
        private int _membersGroup = -1;

        /// <summary>
        /// What a finished build leaves. KeepArea (Build & keep area, Enter): the area stays and the tool goes back to Design
        /// with no type chosen. Close (Build & close): Placemaker closes. Fresh (an edit applied, the game runner): the area
        /// is cleared and the tool stays open. No type stays chosen after any of them.
        /// </summary>
        public enum AfterBuild { Fresh, KeepArea, Close }
        private AfterBuild _afterBuild;
        private bool _closeAfterBuild;
        /// <summary>The game runner: what its next build leaves (its scenarios expect a fresh tool after a build).</summary>
        public AfterBuild? SuiteNextAfterBuild;

        public bool Building => _building;
        public float BuildProgress => _building ? math.saturate(_buildStatus.Progress) : 0f;
        public PlanIssue BuildFailure => _feedback.Failure;
        public IReadOnlyList<float3> BuildErrorPositions => _feedback.ErrorPositions;
        public int FeedbackVersion => _feedback.Version;
        public IReadOnlyList<PlanIssue> BuildNotes => _feedback.Notes;

        /// <summary>(s53) What the last build left out (null = nothing), and the reason row hovered in the panel ("" = none).</summary>
        public Placemaker.Core.Build.BuildAudit BuildAudit => _feedback.Audit;
        private string _auditHover = "";
        public string AuditHover => _feedback.Audit != null ? _auditHover : "";

        public void HoverAudit(string id)
        {
            id = id ?? "";
            if (id.Length > 96 || _auditHover == id) return;
            _auditHover = id;
            Touch();
        }
        public int HoverGroup => _hoverGroup;
        public int ManagedGroup => _managedGroup;

        /// <summary>UI: the "Build & close" button.</summary>
        public void BuildAndClose()
        {
            var p = StageMachine.Primary(_stage, Facts());
            if (!p.Enabled) { Flash(p.Reason); return; }
            if (p.Kind == PrimaryKind.Build || p.Kind == PrimaryKind.FinishEdit) StartBuild(AfterBuild.Close);
        }

        private void StartBuild(AfterBuild after)
        {
            // a Zoning type (a look): its buildings polished, nothing built
            if (BuildPolish()) return;
            var svc = Services.Build;
            if (svc == null) { Flash(new LocText("Placemaker.Reason.BuildUnavailable")); return; }
            if (_plan == null || _shape == null || _recipe == null) return;
            if (PermanentPending()) { Flash(new LocText("Placemaker.Reason.AcknowledgePermanent")); return; }
            // Price = build: the plan carries concrete prefab names for every automatic asset.
            ResolveAssets();
            _feedback.Started();
            var request = new BuildRequest
            {
                Plan = _plan,
                Recipe = _recipe.Clone(),
                Area = _shape,
                ReplaceGroup = _editingGroup,
                Ungrouped = _buildUngrouped,
            };
            bool started;
            // Instant Build from the ghost when it shows exactly this plan; otherwise the ghost is hidden first.
            bool instant = false;
            try { instant = TryBuildFromGhost(request); started = instant || svc.Start(request); }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker: build service failed to start");
                started = false;
            }
            if (!started)
            {
                BuildStatus st = default;
                try { st = svc.Status; } catch (Exception) { }
                _feedback.Failed(st.MessageKey, st.MessageArgs, st.ErrorPositions, "Placemaker.Issue.BuildRefused");
                Flash(new LocText(_feedback.Failure.Key, _feedback.Failure.Args), 8f);
                Touch();
                return;
            }
            SuiteLastBuildPath = instant ? "instant" : "start";
            _afterBuild = SuiteNextAfterBuild ?? after;
            SuiteNextAfterBuild = null;
            _building = true;
            _buildStatus = default;
            CancelGesture();
            ExitMarkerMode();
            SetStage(ToolStage.Building);
        }

        /// <summary>
        /// Something changed: drop the failed attempt's message and red markers here AND in the build service
        /// (its ErrorPositions). Never while a build runs — those errors are still being collected.
        /// </summary>
        private void ClearBuildFeedback(bool stage)
        {
            if (stage) _feedback.StageChanged(); else _feedback.InputChanged();
            if (_building) return;
            try
            {
                var svc = Services.Build;
                if (svc != null && svc.Status.ErrorPositions != null && svc.Status.ErrorPositions.Count > 0) svc.ClearErrors();
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: clear errors failed: " + e.Message); }
        }

        private void CancelBuild()
        {
            try { Services.Build?.Cancel(); } catch (Exception e) { Mod.Log.Warn("Placemaker: cancel failed: " + e.Message); }
        }

        private void UpdateBuild()
        {
            if (!_building) return;
            var svc = Services.Build;
            if (svc == null)
            {
                _building = false;
                SetStage(ToolStage.Review);
                _feedback.Failed("Placemaker.Reason.BuildUnavailable", null, null);
                return;
            }
            BuildStatus st;
            try { st = svc.Status; }
            catch (Exception e) { Mod.Log.Warn("Placemaker: build status failed: " + e.Message); return; }
            if (st.Progress != _buildStatus.Progress || st.Phase != _buildStatus.Phase) Touch();
            _buildStatus = st;
            switch (st.Phase)
            {
                case BuildPhase.Done:
                    Acknowledge(svc);
                    _building = false;
                    OnBuilt(st.GroupId, st.Warnings, st.Audit);
                    break;
                case BuildPhase.Failed:
                    Acknowledge(svc);
                    _building = false;
                    // Land in Review (fix or retry, Esc goes back to Design) — then record the failure, so the
                    // stage change does not clear it. Any later change clears message and red markers.
                    SetStage(ToolStage.Review);
                    _feedback.Failed(st.MessageKey, st.MessageArgs, st.ErrorPositions);
                    Flash(new LocText(_feedback.Failure.Key, _feedback.Failure.Args), 8f);
                    Touch();
                    break;
                case BuildPhase.Cancelled:
                    Acknowledge(svc);
                    _building = false;
                    SetStage(ToolStage.Review);
                    Flash(new LocText("Placemaker.Hint.BuildCancelled"));
                    break;
                case BuildPhase.Idle:
                    // Service reset without telling us (e.g. game loaded): give the tool back.
                    _building = false;
                    SetStage(ToolStage.Review);
                    break;
            }
        }

        /// <summary>
        /// The build service activates its own invisible tool while it builds (CS2 applies temporaries
        /// through the ACTIVE tool's applyMode — docs/proposals/gamebuild.md), so this tool is stopped
        /// for those frames. Our state is kept (OnStopRunning resets nothing persistent) and the UI
        /// system keeps polling the build through this, so progress and the result still arrive.
        /// </summary>
        public void PollBuildWhileInactive()
        {
            if (!_building || IsActive) return;
            try
            {
                UpdateBuild();
                // Finished while another tool is active: give ignoreErrors back now.
                if (!_building && !IsActive && _anarchyApplied && !(m_ToolSystem.activeTool is global::Placemaker.Game.Build.BuildToolSystem)) ApplyAnarchy(false);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: build poll failed: " + e.Message); }
        }

        /// <summary>True while the build service holds the tool slot for us (we will be reactivated).</summary>
        public bool BuildHoldsTool => _building && Services.Build is global::Placemaker.Game.Build.BuildService bs && bs.HoldsTool;

        private static void Acknowledge(IBuildService svc)
        {
            try { svc.Acknowledge(); } catch (Exception e) { Mod.Log.Warn("Placemaker: acknowledge failed: " + e.Message); }
        }

        /// <summary>
        /// Built: the area kept (back to Design) or cleared (the Area step), or the tool closes (_afterBuild). No type stays
        /// chosen: a kept one planned itself on the next area, often a big one meant for something else, and lagged.
        /// </summary>
        private void OnBuilt(int groupId, List<PlanIssue> warnings, Placemaker.Core.Build.BuildAudit audit = null)
        {
            var after = _afterBuild;
            _afterBuild = AfterBuild.Fresh;
            RememberDesign();   // picking the type again brings these choices back
            _markersEdited.Clear();
            _permanentAck = null;
            _worker.Cancel();
            if (after == AfterBuild.KeepArea) ShapeChanged(alreadyBuilt: true);   // the area stays; what was built is in its site now
            else ClearSelectionState();
            _roads.InvalidateGraph();   // the new roads must be pickable right away
            InvalidateFills();          // and stop the area tools at once
            _recipe = null;
            _recipeId = "";
            _presetLayer = ""; _presetId = "";
            _focus = RecipePaths.Root;
            if (_editingGroup != 0) _buildUngrouped = false;   // the edited build's choice was for that build only
            _markerCrosswalk = false;   // the Crosswalk switch is for the entrances of one build: off again for the next
            SetEditingGroup(0);
            _plan = null;
            _planRevision = -1;
            _requestedRevision = -1;
            DesignChanged();
            _history.Reset(Capture());
            SetStage(after == AfterBuild.KeepArea && _shape != null ? ToolStage.Design : ToolStage.Area);
            if (after == AfterBuild.Close) _closeAfterBuild = true;
            // After the stage change and the cleared selection (both clear feedback): what the build service
            // repaired, skipped or cleared stays in the summary until the next change, and the toast says it.
            _feedback.Succeeded(warnings, audit);
            _auditHover = "";
            var notes = _feedback.Notes;
            if (notes.Count == 0) Flash(new LocText("Placemaker.Hint.Built"), 5f);
            else
            {
                var first = notes[0];
                Flash(new LocText(notes.Count == 1 ? first.Key : "Placemaker.Hint.BuiltWithNotes", notes.Count == 1 ? (first.Args ?? Array.Empty<object>()) : new object[] { notes.Count }), 10f);
            }
            SuiteNoteBuilt(groupId);
            Mod.Log.Info("Placemaker: built group " + groupId);
        }

        // ───────────── manage ─────────────

        private void UpdateManage()
        {
            _cursorValid = _in.HasHit;
            if (_in.HasHit) _cursor = _in.Hit;
            int group = 0;
            var groups = Services.Groups;
            if (groups != null && _in.HasHit)
            {
                // The raycast may return a sub-element; walk a few owners up.
                var e = _in.HitEntity;
                for (int depth = 0; depth < 4 && e != Entity.Null && group == 0; depth++)
                {
                    try { group = groups.GroupOf(e); } catch (Exception) { group = 0; }
                    if (group != 0) break;
                    e = EntityManager.HasComponent<Owner>(e) ? EntityManager.GetComponentData<Owner>(e).m_Owner : Entity.Null;
                }
            }
            if (group != _hoverGroup) { _hoverGroup = group; Touch(); }
            if (_in.LeftPressed) ManageGroup(_hoverGroup);
            else if (_in.RightPressed && _managedGroup != 0) ManageGroup(0);
        }

        public void ManageGroup(int groupId)
        {
            if (groupId != 0 && Services.Groups?.Get(groupId) == null) groupId = 0;
            if (groupId == _managedGroup) return;
            _managedGroup = groupId;
            _membersGroup = -1;
            if (_stage != ToolStage.Manage && groupId != 0) SetStage(ToolStage.Manage);
            Touch();
        }

        /// <summary>Members of the hovered/managed group for highlighting (cached per group).</summary>
        public IReadOnlyList<Entity> GroupMembers(int groupId)
        {
            if (groupId == 0 || Services.Groups == null) { _members.Clear(); _membersGroup = -1; return _members; }
            if (_membersGroup == groupId) return _members;
            _members.Clear();
            try { Services.Groups.GetMembers(groupId, _members); } catch (Exception) { _members.Clear(); }
            _membersGroup = groupId;
            return _members;
        }

        /// <summary>Loads a built group's receipt (recipe + area) for editing; building then replaces the group.</summary>
        public void EditGroup(int groupId)
        {
            var groups = Services.Groups;
            if (groups == null) { Flash(new LocText("Placemaker.Reason.GroupsUnavailable")); return; }
            GroupReceipt receipt;
            try { receipt = groups.GetReceipt(groupId); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: receipt failed: " + e.Message); receipt = null; }
            if (receipt?.Recipe == null || receipt.Area?.Ring == null || receipt.Area.Ring.Length < 3)
            {
                Flash(new LocText("Placemaker.Problem.CannotEdit"));
                return;
            }
            CancelGesture();
            ExitMarkerMode();
            ClearSelectionState();
            // Whatever the original selection mode, the outline comes back as editable corners.
            _mode = SelectionMode.Draw;
            _draw.SetRing(receipt.Area.Ring);
            _shape = receipt.Area;
            _shapeDirty = false;
            ShapeChanged(alreadyBuilt: true);
            _recipe = receipt.Recipe.Clone();
            // Markers of a built group were chosen (or accepted) by the player: keep them as they are.
            _markersEdited.Clear();
            foreach (var kv in RecipePaths.Walk(_recipe)) if (kv.Value.Markers.Count > 0) _markersEdited.Add(kv.Key);
            _permanentAck = null;
            _recipeId = _recipes.Find(receipt.Recipe.Id) != null ? receipt.Recipe.Id : "";
            _presetLayer = ""; _presetId = "";   // a receipt names no style: the panel shows the type's Standard (changed if it differs)
            _focus = RecipePaths.Root;
            SetEditingGroup(groupId);
            _managedGroup = 0;
            _buildUngrouped = groups.IsUngrouped(groupId);   // a rebuild stays as it was: grouped or in pieces
            DesignChanged();
            _history.Reset(Capture());
            SetStage(ToolStage.Design);
            // (Phase 1) Edit from the game's info panel: Placemaker opens on the build (closed, the edit would linger unseen and the
            // next build elsewhere replaced it: 22 Oct). Opening never ends the edit; closing does (OnStopRunning).
            if (m_ToolSystem != null && m_ToolSystem.activeTool != this && !_building)
            {
                if (_painter) SetPainter(false);
                if (_outgrowMode) SetOutgrowMode(false);
                _closeAfterGhost = 0;
                _closeAfterBuild = false;
                m_ToolSystem.activeTool = this;
            }
        }

        public void RemoveGroup(int groupId)
        {
            var groups = Services.Groups;
            if (groups == null) { Flash(new LocText("Placemaker.Reason.GroupsUnavailable")); return; }
            bool ok;
            try { ok = groups.RequestRemove(groupId); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: remove failed: " + e.Message); ok = false; }
            if (!ok) { Flash(new LocText("Placemaker.Problem.RemoveFailed")); return; }
            if (_managedGroup == groupId) _managedGroup = 0;
            if (_hoverGroup == groupId) _hoverGroup = 0;
            if (_editingGroup == groupId) SetEditingGroup(0);
            _membersGroup = -1;
            Flash(new LocText("Placemaker.Hint.Removed"));
            Touch();
        }

        /// <summary>
        /// Review switch "One group" off: the next build is born ungrouped and dissolved right after it is built (separate
        /// pieces, as if built by hand). Off by default.
        /// </summary>
        private bool _buildUngrouped;
        public bool BuildUngrouped => _buildUngrouped;

        /// <summary>Review's "Keep area" switch (on by default): Build keeps the area and the panel open; off, Build closes Placemaker.</summary>
        private bool _keepArea = true;
        public bool KeepArea => _keepArea;

        public void SetKeepArea(bool on)
        {
            if (_keepArea == on) return;
            _keepArea = on;
            Touch();
        }

        public void SetBuildUngrouped(bool on)
        {
            if (_buildUngrouped == on) return;
            _buildUngrouped = on;
            Touch();
        }

        /// <summary>Info panel / Manage: ungroup a finished build for good (it is dissolved into plain city pieces).</summary>
        public void UngroupGroup(int groupId)
        {
            bool ok;
            try { ok = Services.Groups?.Ungroup(groupId) ?? false; }
            catch (Exception e) { Mod.Log.Warn("Placemaker: ungroup failed: " + e.Message); ok = false; }
            if (!ok) { Flash(new LocText("Placemaker.Problem.UngroupFailed")); return; }
            _membersGroup = -1;
            Flash(new LocText("Placemaker.Hint.Ungrouped"));
            Touch();
        }

        public void RenameGroup(int groupId, string name)
        {
            try { Services.Groups?.Rename(groupId, (name ?? "").Trim()); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: rename failed: " + e.Message); }
            Touch();
        }

        /// <summary>Options page: remove every group Placemaker built.</summary>
        public int RemoveEverything()
        {
            var groups = Services.Groups;
            if (groups == null) return 0;
            int n = 0;
            try
            {
                foreach (var id in new List<int>(groups.AllGroups))
                    if (groups.RequestRemove(id)) n++;
            }
            catch (Exception e) { Mod.Log.Error(e, "Placemaker: remove everything failed"); }
            _managedGroup = _hoverGroup = 0; SetEditingGroup(0);
            Touch();
            Mod.Log.Info("Placemaker: requested removal of " + n + " groups");
            return n;
        }
    }
}
