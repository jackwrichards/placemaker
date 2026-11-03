// What the outline was cut against, for the overlay (red: the stops it touched) and the area problem ("only stops
// here"). s27 kept Draw / Road nodes / Block outlines off other Placemaker builds here; since the area overhaul every
// tool follows the shared stops itself: the Paint bucket's fill and Paint area's fence never enter them, and Points are
// cut by them (PlacemakerToolSystem.Points, CutByStops).

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        /// <summary>The whole outline lies on stops: no area (AreaProblem says why).</summary>
        private bool _clipCoveredAll;
        private List<float3[]> _clipShown = new List<float3[]>();
        private int _clipShownVersion;

        /// <summary>The stops the current outline was cut against (overlay: "won't paint" shading).</summary>
        public IReadOnlyList<float3[]> ClippedObstacles => _clipShown;
        public int ClippedObstaclesVersion => _clipShownVersion;
    }
}
