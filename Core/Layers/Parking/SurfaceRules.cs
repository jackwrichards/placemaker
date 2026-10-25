// (correctness) Does the game build keep a parking surface ring? The build pipeline (BuildService.EmitPreparedRings)
// sends a ring as it is when it passes the game's area shape rules (AreaRings, d = the surface prefab's 0.375 m plus
// margin), else what AreaRings.PrepareSurface repairs it into, else nothing: the lot shows bare ground there. The planner
// asks the same before emitting, so a ring the build would skip is known in time to regroup its cells instead. The ring
// itself is emitted (the repair drops nodes on one ring and not on its neighbour, so planned surfaces would overlap).

using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class SurfaceRules
    {
        /// <summary>The surface prefabs' minimum node distance (snap 0.75 × 0.5).</summary>
        public const float MinNodeDistance = AreaRings.SurfaceMinNodeDistance;

        /// <summary>True when the build keeps this (accepted) surface ring: as it is, or repaired.</summary>
        public static bool GameKeeps(float3[] ring)
        {
            var r2 = Poly.To2D(ring);
            if (AreaRings.IsValid(r2, MinNodeDistance + AreaRings.Margin)) return true;
            return AreaRings.PrepareSurface(r2, MinNodeDistance).Count > 0;
        }
    }
}
