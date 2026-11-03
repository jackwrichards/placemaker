// Placemaker — the tool's workflow as a pure decision table.
//
// The game system feeds facts in, the machine answers "which stages may the player jump to",
// "what does the main button (or Enter) do now and why is it disabled", and "what does Esc
// undo next". Keeping this pure means the button never lies: the same facts always give the
// same answer, and every branch is unit-tested (Tests/ToolStageMachineTests.cs).

using System.Collections.Generic;

namespace Placemaker.Core.Tool
{
    public enum ToolStage { Area = 0, Design = 1, Review = 2, Building = 3, Manage = 4 }

    public enum PrimaryKind { None = 0, ToDesign = 1, ToReview = 2, Build = 3, PlaceMarkers = 4, CancelBuild = 5, FinishEdit = 6, Polish = 7 }

    public enum EscapeAction { None = 0, ExitMarkers = 1, CancelGesture = 2, ToDesign = 3, ToArea = 4, ClearSelection = 5, CloseTool = 6 }

    /// <summary>Everything the machine needs to know about the current tool state. Plain data.</summary>
    public sealed class StageFacts
    {
        /// <summary>A closed, valid outline exists.</summary>
        public bool AreaValid;
        /// <summary>Anything selected at all (points placed, blocks picked, paint, nodes) — valid or not.</summary>
        public bool HasSelection;
        /// <summary>The outline is being drawn but not closed yet.</summary>
        public bool AreaInProgress;
        /// <summary>Why the current outline cannot be used (self-intersection, too small…). Null if fine.</summary>
        public LocText AreaProblem;

        public bool HasRecipe;
        /// <summary>A plan for the current inputs is being computed or is outdated.</summary>
        public bool Planning;
        public bool PlanReady;
        public bool PlanHasErrors;

        /// <summary>First recipe step whose generator is not ready (e.g. needs an entrance). Null when all ready.</summary>
        public string UnreadyStepPath;
        public LocText UnreadyNextStep;
        public LocText UnreadyReason;
        /// <summary>Marker kind that would satisfy the unready step (null when markers do not help).</summary>
        public string UnreadyMarkerKind;

        public bool CanAfford = true;
        /// <summary>The plan has permanent effects (lake excavation…) the player has not acknowledged yet.</summary>
        public bool PermanentPending;
        public bool BuildServiceAvailable = true;
        public bool BuildRunning;
        public bool MarkerModeActive;
        public bool Editing;
        /// <summary>A drag (point, edge, brush stroke, slider) is in progress.</summary>
        public bool GestureActive;
        /// <summary>The "Polish gardens" painter is the picked entry: the area is stamped (Polish / Unpolish), never built.</summary>
        public bool Painter;
    }

    public sealed class PrimaryAction
    {
        public PrimaryKind Kind;
        public LocText Label;
        public bool Enabled;
        public LocText Reason;

        /// <summary>Step path / marker kind the action applies to (PlaceMarkers).</summary>
        public string StepPath;
        public string MarkerKind;

        public static string KindId(PrimaryKind k)
        {
            switch (k)
            {
                case PrimaryKind.ToDesign: return "toDesign";
                case PrimaryKind.ToReview: return "toReview";
                case PrimaryKind.Build: return "build";
                case PrimaryKind.PlaceMarkers: return "placeMarkers";
                case PrimaryKind.CancelBuild: return "cancelBuild";
                case PrimaryKind.FinishEdit: return "finishEdit";
                case PrimaryKind.Polish: return "polish";
                default: return "none";
            }
        }
    }

    public static class StageMachine
    {
        public static string Id(ToolStage s)
        {
            switch (s)
            {
                case ToolStage.Design: return "design";
                case ToolStage.Review: return "review";
                case ToolStage.Building: return "building";
                case ToolStage.Manage: return "manage";
                default: return "area";
            }
        }

        public static bool TryParse(string id, out ToolStage stage)
        {
            switch (id)
            {
                case "area": stage = ToolStage.Area; return true;
                case "design": stage = ToolStage.Design; return true;
                case "review": stage = ToolStage.Review; return true;
                case "building": stage = ToolStage.Building; return true;
                case "manage": stage = ToolStage.Manage; return true;
                default: stage = ToolStage.Area; return false;
            }
        }

        /// <summary>Stages the player may jump to now. Building is never a jump target; while it runs nothing else is.</summary>
        public static List<ToolStage> Reachable(StageFacts f)
        {
            var r = new List<ToolStage>(4);
            if (f.BuildRunning) { r.Add(ToolStage.Building); return r; }
            r.Add(ToolStage.Area);
            if (f.AreaValid) r.Add(ToolStage.Design);
            if (f.AreaValid && f.HasRecipe && !f.Painter) r.Add(ToolStage.Review);
            r.Add(ToolStage.Manage);
            return r;
        }

        public static bool CanGoTo(ToolStage target, StageFacts f) => Reachable(f).Contains(target);

        /// <summary>
        /// The stage to actually show. A stage that became unreachable (outline broken while in
        /// Review, recipe removed…) falls back to the nearest earlier one; nothing is discarded,
        /// the player just sees where the problem is.
        /// </summary>
        public static ToolStage Normalize(ToolStage current, StageFacts f)
        {
            if (f.BuildRunning) return ToolStage.Building;
            if (current == ToolStage.Building) return f.AreaValid && f.HasRecipe ? ToolStage.Review : ToolStage.Area;
            if (current == ToolStage.Review && !(f.AreaValid && f.HasRecipe && !f.Painter)) current = ToolStage.Design;
            if (current == ToolStage.Design && !f.AreaValid) current = ToolStage.Area;
            return current;
        }

        public static PrimaryAction Primary(ToolStage stage, StageFacts f)
        {
            if (f.BuildRunning || stage == ToolStage.Building)
                return Make(PrimaryKind.CancelBuild, "Placemaker.Primary.CancelBuild", f.BuildRunning, f.BuildRunning ? null : "Placemaker.Reason.NothingToCancel");

            if (f.MarkerModeActive)
                return Make(PrimaryKind.PlaceMarkers, "Placemaker.Primary.DoneMarkers", true, null);

            // the polish tool: Area (Next) → Polish, its look and Polish (Unpolish is its second button). The lots in the area
            // are stamped, nothing is planned or built.
            if (f.Painter && stage != ToolStage.Manage)
            {
                if (stage == ToolStage.Area || !f.AreaValid) return AreaPrimary(f);
                if (f.AreaProblem == null) return Make(PrimaryKind.Polish, "Placemaker.Primary.Polish", true, null);
                return new PrimaryAction { Kind = PrimaryKind.Polish, Label = new LocText("Placemaker.Primary.Polish"), Enabled = false, Reason = f.AreaProblem };
            }

            switch (stage)
            {
                case ToolStage.Manage:
                    return Make(PrimaryKind.None, "Placemaker.Primary.PickBuilt", false, "Placemaker.Reason.PickBuilt");

                case ToolStage.Area:
                    return AreaPrimary(f);

                case ToolStage.Design:
                {
                    if (!f.AreaValid) return AreaPrimary(f);
                    if (!f.HasRecipe) return Make(PrimaryKind.None, "Placemaker.Primary.ChooseType", false, "Placemaker.Reason.NoRecipe");
                    var markers = MarkerStep(f, "Placemaker.Primary.Review");
                    if (markers != null) return markers;
                    if (f.UnreadyStepPath != null)
                    {
                        var p = Make(PrimaryKind.ToReview, "Placemaker.Primary.Review", false, null);
                        p.Reason = f.UnreadyReason ?? new LocText("Placemaker.Reason.StepNotReady");
                        p.StepPath = f.UnreadyStepPath;
                        return p;
                    }
                    return Make(PrimaryKind.ToReview, "Placemaker.Primary.Review", true, null);
                }

                case ToolStage.Review:
                default:
                {
                    if (!f.AreaValid) return AreaPrimary(f);
                    if (!f.HasRecipe) return Make(PrimaryKind.None, "Placemaker.Primary.ChooseType", false, "Placemaker.Reason.NoRecipe");
                    var kind = f.Editing ? PrimaryKind.FinishEdit : PrimaryKind.Build;
                    string label = f.Editing ? "Placemaker.Primary.ApplyEdit" : "Placemaker.Primary.Build";
                    var markers = MarkerStep(f, label);
                    if (markers != null) return markers;

                    if (f.UnreadyStepPath != null)
                    {
                        var p = Make(kind, label, false, null);
                        p.Reason = f.UnreadyReason ?? new LocText("Placemaker.Reason.StepNotReady");
                        p.StepPath = f.UnreadyStepPath;
                        return p;
                    }
                    if (f.AreaProblem != null)
                    {
                        var bad = Make(kind, label, false, null);
                        bad.Reason = f.AreaProblem;
                        return bad;
                    }
                    if (!f.BuildServiceAvailable) return Make(kind, label, false, "Placemaker.Reason.BuildUnavailable");
                    if (f.Planning || !f.PlanReady) return Make(kind, label, false, "Placemaker.Reason.Planning");
                    if (f.PlanHasErrors) return Make(kind, label, false, "Placemaker.Reason.PlanErrors");
                    if (f.PermanentPending) return Make(kind, label, false, "Placemaker.Reason.AcknowledgePermanent");
                    if (!f.CanAfford) return Make(kind, label, false, "Placemaker.Reason.NoMoney");
                    return Make(kind, label, true, null);
                }
            }
        }

        private static PrimaryAction AreaPrimary(StageFacts f)
        {
            if (f.AreaValid)
            {
                if (f.AreaProblem != null)
                {
                    var bad = Make(PrimaryKind.ToDesign, "Placemaker.Primary.Next", false, null);
                    bad.Reason = f.AreaProblem;
                    return bad;
                }
                return Make(PrimaryKind.ToDesign, "Placemaker.Primary.Next", true, null);
            }
            if (f.AreaProblem != null)
            {
                var p = Make(PrimaryKind.None, "Placemaker.Primary.SelectArea", false, null);
                p.Reason = f.AreaProblem;
                return p;
            }
            if (f.AreaInProgress) return Make(PrimaryKind.None, "Placemaker.Primary.SelectArea", false, "Placemaker.Reason.CloseArea");
            return Make(PrimaryKind.None, "Placemaker.Primary.SelectArea", false, "Placemaker.Reason.NoArea");
        }

        /// <summary>
        /// A step still needs markers (no entrance yet). Entrances are never placed automatically
        /// (FEEDBACK-4b): the button keeps its stage label but stays disabled with "Add at least one
        /// entrance"; pressing it anyway (click or Enter) starts placement on that step, and the UI
        /// scrolls to / opens / highlights its Entrances section.
        /// </summary>
        private static PrimaryAction MarkerStep(StageFacts f, string label)
        {
            if (f.UnreadyStepPath == null || string.IsNullOrEmpty(f.UnreadyMarkerKind)) return null;
            return new PrimaryAction
            {
                Kind = PrimaryKind.PlaceMarkers,
                Label = new LocText(label),
                Enabled = false,
                Reason = new LocText(AddEntranceKey),
                StepPath = f.UnreadyStepPath,
                MarkerKind = f.UnreadyMarkerKind,
            };
        }

        public const string AddEntranceKey = "Placemaker.Reason.AddEntrance";

        private static PrimaryAction Make(PrimaryKind kind, string label, bool enabled, string reasonKey) => new PrimaryAction
        {
            Kind = kind,
            Label = new LocText(label),
            Enabled = enabled,
            Reason = enabled ? null : LocText.Of(reasonKey),
        };

        /// <summary>
        /// What Esc does next. Each press steps back exactly one level: leave marker mode → abort a
        /// running drag → leave Review → leave Design → clear the selection → close the tool.
        /// Going back between stages never discards anything; only ClearSelection does (and it is undoable).
        /// </summary>
        public static EscapeAction Escape(ToolStage stage, StageFacts f)
        {
            if (f.BuildRunning || stage == ToolStage.Building) return EscapeAction.None;
            if (f.MarkerModeActive) return EscapeAction.ExitMarkers;
            if (f.GestureActive) return EscapeAction.CancelGesture;
            switch (stage)
            {
                case ToolStage.Review: return EscapeAction.ToDesign;
                case ToolStage.Design: return EscapeAction.ToArea;
                case ToolStage.Manage: return EscapeAction.ToArea;
                default: return f.HasSelection ? EscapeAction.ClearSelection : EscapeAction.CloseTool;
            }
        }
    }
}
