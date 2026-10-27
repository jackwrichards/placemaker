// AI builder (docs/AI-BUILDER.md): what the AI builder reads from the tool.

using Placemaker.Core;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>The captured surroundings of the current selection (roads, buildings, water), as the planners get them; null without one.</summary>
        internal SiteContext AiSite() => _shape?.Ring != null && _shape.Ring.Length >= 3 ? SiteFor(_shape) : null;
    }
}
