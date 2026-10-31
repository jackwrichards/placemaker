// Manual touches: small hand edits a player makes on a placed design ("this stall is accessible", "no stall here",
// "the grid lines up with this kerb"), kept in the design so they survive Edit, rebuilds, outline changes and reloads.
// docs/ARCHITECTURE.md, "Manual touches", is the guide for a type that wants them.
//
// A touch is data on its recipe step (RecipeStep.Touches, saved with the receipt like the markers) and reaches the layer as
// AreaInput.Touches. The layer applies the touches after its own layout, each to the thing nearest its position, and tells
// the tool what can be touched (AreaPlan.TouchTargets). A touch that finds nothing within reach any more (the outline moved,
// a setting removed the row) is left out of the plan with one note; it stays in the design, so it comes back when the thing
// does. Pure: no game types.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core
{
    /// <summary>One hand edit. Kinds are the layer's own ("stall.accessible"); positions are world metres.</summary>
    public struct HandTouch
    {
        /// <summary>Format version of a touch. Readers skip touches newer than they know.</summary>
        public const int CurrentVersion = 1;

        public string Kind;
        /// <summary>Where the player touched: the centre of the thing it applies to.</summary>
        public float3 Position;
        /// <summary>A direction for touches that have one (an alignment), radians around +Y like PlannedObject.Yaw; NaN = none.</summary>
        public float Yaw;
        /// <summary>Optional value of the touch (an angle, a count), "" = none.</summary>
        public string Arg;
        public int Version;

        public HandTouch(string kind, float3 position, float yaw = float.NaN, string arg = "")
        {
            Kind = kind ?? ""; Position = position; Yaw = yaw; Arg = arg ?? ""; Version = CurrentVersion;
        }

        public bool HasYaw => !float.IsNaN(Yaw) && !float.IsInfinity(Yaw);
    }

    /// <summary>
    /// Something in the plan the player can touch (a stall). Not saved: the layer lists them on every plan. The tool draws the
    /// outline of the one under the cursor and puts the touch at its centre.
    /// </summary>
    public sealed class TouchTarget
    {
        /// <summary>What it is ("stall"): the touch kinds that apply to it start with this and a dot.</summary>
        public string Group = "";
        public float3 Center;
        /// <summary>The outline to draw (world XZ), at least 3 points.</summary>
        public float2[] Outline = Array.Empty<float2>();
        /// <summary>Its state now, after the touches ("normal", "accessible", "ev", "removed"): the panel's legend and the overlay colour.</summary>
        public string State = "";
        /// <summary>The touch kind already applied here, "" = none (clicking it again with the same kind takes the touch off).</summary>
        public string Touched = "";
        /// <summary>Where that touch is (it may lie a little off <see cref="Center"/>: a group made from one touch).</summary>
        public float3 TouchAt;
    }

    public static class Touches
    {
        /// <summary>How far a touch may be from the thing it was made on and still apply (metres).</summary>
        public const float Reach = 1.6f;

        public const string DroppedKey = "Placemaker.Touch.Dropped";

        /// <summary>The recipe step a plan layer came from: its StepPath without the region ("0@root" → "0").</summary>
        public static string StepOf(string planPath)
        {
            if (string.IsNullOrEmpty(planPath)) return planPath ?? "";
            int at = planPath.LastIndexOf('@');
            return at < 0 ? planPath : planPath.Substring(0, at);
        }

        /// <summary>Group of a kind: "stall.accessible" → "stall".</summary>
        public static string GroupOf(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            int dot = kind.IndexOf('.');
            return dot > 0 ? kind.Substring(0, dot) : kind;
        }

        /// <summary>The touches a layer can read: known version, a kind, a finite position.</summary>
        public static List<HandTouch> Valid(IEnumerable<HandTouch> touches)
        {
            var r = new List<HandTouch>();
            if (touches == null) return r;
            foreach (var t in touches)
                if (!string.IsNullOrEmpty(t.Kind) && t.Version >= 1 && t.Version <= HandTouch.CurrentVersion && math.all(math.isfinite(t.Position)))
                    r.Add(t);
            return r;
        }

        /// <summary>
        /// Index of the candidate nearest to <paramref name="at"/> within <paramref name="reach"/>, or -1. <paramref name="pos"/> gives a
        /// candidate's position; <paramref name="skip"/> (optional) rules some out.
        /// </summary>
        public static int Nearest<T>(IReadOnlyList<T> candidates, float2 at, Func<T, float2> pos, float reach = Reach, Func<T, bool> skip = null)
        {
            int best = -1;
            float bestD = reach * reach;
            if (candidates == null) return -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (skip != null && skip(c)) continue;
                float d = math.distancesq(pos(c), at);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Adds a touch, or takes it off. <paramref name="target"/> (optional) is what was clicked: a touch already applied to it is
        /// replaced, and clicking it again with the same kind removes it (a toggle). Without a target, a touch of the same group
        /// within reach counts as the one applied there. <paramref name="single"/>: one touch of this group per design (a picked
        /// edge): the others go. Returns true when the list changed.
        /// </summary>
        public static bool Toggle(List<HandTouch> list, HandTouch touch, TouchTarget target = null, bool single = false, float reach = Reach)
        {
            if (list == null || string.IsNullOrEmpty(touch.Kind)) return false;
            string group = GroupOf(touch.Kind);
            int found = -1;
            if (target != null && !string.IsNullOrEmpty(target.Touched))
            {
                float best = 0.75f;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Kind != target.Touched) continue;
                    float d = math.distance(list[i].Position.xz, target.TouchAt.xz);
                    if (d <= best) { best = d; found = i; }
                }
            }
            else if (target == null)
            {
                float best = reach;
                for (int i = 0; i < list.Count; i++)
                {
                    if (GroupOf(list[i].Kind) != group) continue;
                    float d = math.distance(list[i].Position.xz, touch.Position.xz);
                    if (d <= best) { best = d; found = i; }
                }
            }
            if (found >= 0)
            {
                if (list[found].Kind == touch.Kind) { list.RemoveAt(found); return true; }
                list[found] = touch;
            }
            else list.Add(touch);
            if (single)
            {
                var keep = touch;
                list.RemoveAll(t => GroupOf(t.Kind) == group && !(t.Kind == keep.Kind && math.all(t.Position == keep.Position)));
            }
            return true;
        }

        /// <summary>One note for the touches a plan left out (they stay in the design).</summary>
        public static void ReportDropped(AreaPlan plan, int dropped, float3? at = null)
        {
            if (plan == null || dropped <= 0) return;
            plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = DroppedKey, Args = new object[] { dropped }, At = at });
            plan.Facts["touches.dropped"] = dropped;
        }
    }
}
