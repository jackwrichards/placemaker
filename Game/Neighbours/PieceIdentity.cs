// Placemaker neighbourhood polish — when a piece a plan wants is one the house already has (GPL-3.0).
//
// One rule for both sides: Finish keeps a piece the plan still wants and deletes the rest; the object and surface systems
// make a wanted piece only when the house has no such piece. An object (bush, rock, fence or hedge piece) is the same when
// it is of the same kind, within 25 cm and at the same angle; a surface (lawn, bed, path, paving) when it has the very same
// outline. Before, a piece was kept when a wanted one was within 1.2 m but made again unless one was within 0.5 m (objects)
// or centred within 1.5 m (surfaces, anyone's): moved pieces doubled up (the game showed one or the other: flicker) and
// moved surfaces could go missing.

using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public static class PieceIdentity
    {
        /// <summary>A piece a plan wants (Yaw NaN: any angle; Ring set: a surface).</summary>
        public struct Wish
        {
            public string Name;
            public float2 At;
            public float Yaw;
            public float2[] Ring;
        }

        /// <summary>A piece the house has.</summary>
        public struct Have
        {
            public Entity Entity;
            public string Name;
            public float2 At;
            public float Yaw;
            public float2[] Ring;
        }

        private const float Spot = 0.25f, Angle = 0.05f, Node = 0.05f;

        public static bool Same(Wish w, Have h)
        {
            if (!string.Equals(w.Name, h.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (w.Ring != null || h.Ring != null) return w.Ring != null && h.Ring != null && SameRing(w.Ring, h.Ring);
            return SameSpot(h.At, h.Yaw, w.At, w.Yaw);
        }

        /// <summary>Within 25 cm and (both angles known) at the same angle.</summary>
        public static bool SameSpot(float2 a, float yawA, float2 b, float yawB)
        {
            if (math.distance(a, b) >= Spot) return false;
            if (float.IsNaN(yawA) || float.IsNaN(yawB)) return true;
            float d = math.abs(yawA - yawB) % (2f * math.PI);
            return math.min(d, 2f * math.PI - d) < Angle;
        }

        /// <summary>The angle about the vertical a rotation turns +z to (as quaternion.RotateY(yaw) does).</summary>
        public static float YawOf(quaternion q)
        {
            var f = math.mul(q, new float3(0f, 0f, 1f));
            return math.atan2(f.x, f.z);
        }

        /// <summary>The mean of a ring's points (a closing copy of the first left out).</summary>
        public static float2 Centre(float2[] ring)
        {
            int n = Count(ring);
            var c = float2.zero;
            for (int i = 0; i < n; i++) c += ring[i];
            return n > 0 ? c / n : c;
        }

        /// <summary>A surface's middle by its area (not its points: the game's copy of an outline may have a point more, its
        /// first one again, which moved the points' average of a big wrapping piece past the tag's reach); the points' average
        /// when it has no area. And its area (m²).</summary>
        public static float2 AreaCentre(float2[] ring, out float area)
        {
            int n = Count(ring);
            double a2 = 0, cx = 0, cy = 0;
            for (int i = 0; i < n; i++)
            {
                var p = ring[i];
                var q = ring[(i + 1) % n];
                double cross = (double)p.x * q.y - (double)q.x * p.y;
                a2 += cross;
                cx += (p.x + q.x) * cross;
                cy += (p.y + q.y) * cross;
            }
            area = (float)math.abs(a2 * 0.5);
            if (math.abs(a2) < 1e-3) return Centre(ring);
            return new float2((float)(cx / (3.0 * a2)), (float)(cy / (3.0 * a2)));
        }

        /// <summary>The same outline: as many points, each within 5 cm of the other's, in order (from any start).</summary>
        public static bool SameRing(float2[] a, float2[] b)
        {
            int na = Count(a), nb = Count(b);
            if (na != nb || na < 3) return false;
            for (int k = 0; k < nb; k++)
            {
                if (math.distance(a[0], b[k]) >= Node) continue;
                bool all = true;
                for (int i = 1; i < na && all; i++) all = math.distance(a[i], b[(k + i) % nb]) < Node;
                if (all) return true;
            }
            return false;
        }

        /// <summary>A ring's points without a closing copy of the first.</summary>
        private static int Count(float2[] ring)
        {
            if (ring == null) return 0;
            int n = ring.Length;
            if (n > 1 && math.distance(ring[0], ring[n - 1]) < 1e-4f) n--;
            return n;
        }
    }
}
