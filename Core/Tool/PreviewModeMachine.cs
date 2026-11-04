// Preview mode rules (contract: state.previewMode / state.ghostPending / setPreviewMode). Pure, unit-tested.
//
// "sketch" = the fast overlay (lines, dots, filled role colours); "ghost" = real translucent game
// models and surface textures produced by the ghost preview service (build workstream), which never
// applies anything. The tool asks this machine once per frame what to do with the ghost.
//
// Rules:
// - Each stage has its own choice for the session: Design starts as sketch (fast while editing),
//   Review as ghost. setPreviewMode changes the choice of the current stage and it sticks.
// - While something is dragged (slider previewValue, outline/handle gesture) the effective mode is
//   always sketch; when the drag ends the ghost comes back. Hovering an asset in a picker is not a
//   drag: the ghost stays and follows the hovered asset.
// - Leaving the tool, going back to Area (or Manage), a build starting, no plan, or no ghost service
//   → Hide.
// - The reported previewMode is the player's choice (the toggle does not flicker during a drag);
//   ghostPending says the ghost is wanted but does not yet show the current plan.
// - While a ghost is visible the sketch's objects (nets, objects, surface fills) are hidden when the
//   ghost shows the current plan, dimmed while it is catching up; outline, markers and handles stay.

namespace Placemaker.Core.Tool
{
    public enum PreviewMode { Sketch = 0, Ghost = 1 }

    /// <summary>What a pending previewValue is: a slider drag (sketch) or a hovered asset (ghost follows).</summary>
    public enum ValuePreviewKind { None = 0, Drag = 1, Hover = 2 }

    /// <summary>How the sketch draws the plan's content (outline, markers and handles are always drawn).</summary>
    public enum SketchObjects { Full = 0, Dimmed = 1, Hidden = 2 }

    public enum GhostCommand { None = 0, Show = 1, Hide = 2 }

    public static class PreviewModes
    {
        public static string Id(PreviewMode m) => m == PreviewMode.Ghost ? "ghost" : "sketch";

        public static bool TryParse(string id, out PreviewMode mode)
        {
            switch (id)
            {
                case "ghost": mode = PreviewMode.Ghost; return true;
                case "sketch": mode = PreviewMode.Sketch; return true;
                default: mode = PreviewMode.Sketch; return false;
            }
        }
    }

    public struct PreviewInput
    {
        public ToolStage Stage;
        /// <summary>The Placemaker tool is the active tool (not closed, not handed to the build service).</summary>
        public bool ToolActive;
        public bool BuildRunning;
        public ValuePreviewKind ValuePreview;
        /// <summary>Outline/handle/marker drag in progress.</summary>
        public bool Dragging;
        /// <summary>A composed plan exists (possibly stale).</summary>
        public bool PlanAvailable;
        public int PlanRevision;
        /// <summary>The plan is older than the input (re-planning in the background).</summary>
        public bool PlanStale;
        /// <summary>(shops4) The plan is refused (an error: Build waits). Nothing to show, so no ghost and nothing to wait for: the
        /// panel said "Updating preview…" for ever on a too-small area and the tool never settled.</summary>
        public bool PlanRefused;
        /// <summary>The ghost service is registered and usable.</summary>
        public bool GhostAvailable;
        /// <summary>Revision the ghost currently shows on the map, -1 = nothing.</summary>
        public int GhostShownRevision;
        /// <summary>The service is still building the requested ghost.</summary>
        public bool GhostBusy;
    }

    public struct PreviewDecision
    {
        /// <summary>The player's choice for the current stage (state.previewMode).</summary>
        public PreviewMode Selected;
        /// <summary>What is actually on the map this frame.</summary>
        public PreviewMode Effective;
        public GhostCommand Command;
        /// <summary>Revision to pass with Show.</summary>
        public int ShowRevision;
        /// <summary>state.ghostPending.</summary>
        public bool GhostPending;
        public SketchObjects Objects;
    }

    public sealed class PreviewModeMachine
    {
        public const PreviewMode DesignDefault = PreviewMode.Sketch;
        public const PreviewMode ReviewDefault = PreviewMode.Ghost;

        private PreviewMode _design = DesignDefault;
        private PreviewMode _review = ReviewDefault;
        private int _requested = -1;     // revision last sent with Show, -1 = hidden / nothing requested

        /// <summary>Revision last requested from the ghost service (-1 = hidden).</summary>
        public int RequestedRevision => _requested;

        public PreviewMode DesignChoice => _design;
        public PreviewMode ReviewChoice => _review;

        public static bool HasPreview(ToolStage s) => s == ToolStage.Design || s == ToolStage.Review;

        /// <summary>The player's choice for a stage; stages without a plan preview are always sketch.</summary>
        public PreviewMode Selected(ToolStage stage)
        {
            switch (stage)
            {
                case ToolStage.Design: return _design;
                case ToolStage.Review: return _review;
                case ToolStage.Building: return _review;
                default: return PreviewMode.Sketch;
            }
        }

        /// <summary>setPreviewMode: changes the current stage's choice for the rest of the session. False = nothing changed.</summary>
        public bool Set(ToolStage stage, PreviewMode mode)
        {
            switch (stage)
            {
                case ToolStage.Design:
                    if (_design == mode) return false;
                    _design = mode;
                    return true;
                case ToolStage.Review:
                    if (_review == mode) return false;
                    _review = mode;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Back to session defaults (new game session).</summary>
        public void ResetChoices()
        {
            _design = DesignDefault;
            _review = ReviewDefault;
        }

        /// <summary>The ghost was hidden by someone else (tool switched, build took over): the next Step shows again.</summary>
        public void Forget() => _requested = -1;

        /// <summary>One frame: what the map should show and which command to send to the ghost service.</summary>
        public PreviewDecision Step(PreviewInput i)
        {
            var d = new PreviewDecision { Selected = Selected(i.Stage), Effective = PreviewMode.Sketch, ShowRevision = -1 };
            bool wantGhost = d.Selected == PreviewMode.Ghost
                             && HasPreview(i.Stage)
                             && i.ToolActive && !i.BuildRunning
                             && i.ValuePreview != ValuePreviewKind.Drag && !i.Dragging
                             && i.GhostAvailable && i.PlanAvailable && !i.PlanRefused;
            if (!wantGhost)
            {
                if (_requested >= 0) { d.Command = GhostCommand.Hide; _requested = -1; }
                d.Objects = SketchObjects.Full;
                return d;
            }

            d.Effective = PreviewMode.Ghost;
            // A stale plan is the one already shown (or being shown); wait for the fresh one.
            if (!i.PlanStale && _requested != i.PlanRevision)
            {
                d.Command = GhostCommand.Show;
                d.ShowRevision = i.PlanRevision;
                _requested = i.PlanRevision;
            }
            bool current = i.GhostShownRevision >= 0 && i.GhostShownRevision == i.PlanRevision && !i.PlanStale;
            d.GhostPending = !current || i.GhostBusy;
            d.Objects = i.GhostShownRevision < 0 ? SketchObjects.Full : current ? SketchObjects.Hidden : SketchObjects.Dimmed;
            return d;
        }
    }
}

namespace Placemaker.Core.Tool
{
    public static class ValuePreviews
    {
        /// <summary>previewValue on an asset picker is a hover (the ghost follows it, endPreview reverts); anything else is a drag.</summary>
        public static ValuePreviewKind KindFor(ControlSchema control)
        {
            if (control == null) return ValuePreviewKind.Drag;
            return control.Kind == ControlKind.Asset || control.Kind == ControlKind.AssetMulti ? ValuePreviewKind.Hover : ValuePreviewKind.Drag;
        }
    }
}
