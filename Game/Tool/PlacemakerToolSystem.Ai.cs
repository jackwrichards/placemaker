// AI builder (docs/AI-BUILDER.md): what the AI builder reads from the tool, and the re-plan when the AI placed something.

using Placemaker.Core;
using Placemaker.Core.Ai;
using Placemaker.Core.Layers.Ai;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private int _aiPlanSeen = -1;

        /// <summary>The captured surroundings of the current selection (roads, buildings, water), as the planners get them; null without one.</summary>
        internal SiteContext AiSite() => _shape?.Ring != null && _shape.Ring.Length >= 3 ? SiteFor(_shape) : null;

        public bool AiTypePicked => _recipeId == AiLayer.LayerId;

        /// <summary>Every frame: the AI's plan changed (its tools run on a worker) -> re-plan, so the ghost and the sketch follow it.</summary>
        private void AiFrame()
        {
            int rev = AiPlan.Current.Revision;
            if (rev == _aiPlanSeen) return;
            _aiPlanSeen = rev;
            if (AiTypePicked) DesignChanged();
        }
    }
}
