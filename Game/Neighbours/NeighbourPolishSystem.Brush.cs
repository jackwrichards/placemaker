// Global outgrow's brush (PlacemakerToolSystem.Painter, UpdateOutgrowBrushInput): which buildings a stroke touches.
// No area, no smart edges: a lot the brush passes over at all is in.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        /// <summary>A stroke starts: the buildings are looked up fresh when the list is more than half a second old (buildings
        /// grown or rebuilt since were missed: "it doesn't always work").</summary>
        public void BrushStart()
        {
            if (_grid == null || _frame - _gridBuilt > 30) _gridStale = true;
        }

        /// <summary>
        /// Every growable building whose lot the stroke from <paramref name="a"/> to <paramref name="b"/> touches (the brush
        /// <paramref name="r"/> each side), into <paramref name="into"/>, and its lot ring into <paramref name="rings"/> the
        /// first time. How many were new.
        /// </summary>
        public int BrushTouch(float2 a, float2 b, float r, HashSet<Entity> into, List<float3[]> rings)
        {
            EnsureGrid();
            if (_grid == null) return 0;
            int added = 0;
            float pad = r + Cell;   // a lot's centre (its grid cell) can be this far from the ground it covers
            float2 lo = math.min(a, b) - pad, hi = math.max(a, b) + pad;
            int steps = math.max(1, (int)math.ceil(math.distance(a, b) / math.max(0.5f, r * 0.5f)));
            for (int cx = (int)math.floor(lo.x / Cell); cx <= (int)math.floor(hi.x / Cell); cx++)
                for (int cz = (int)math.floor(lo.y / Cell); cz <= (int)math.floor(hi.y / Cell); cz++)
                {
                    if (!_grid.TryGetValue(Key(new float2((cx + 0.5f) * Cell, (cz + 0.5f) * Cell)), out var list)) continue;
                    foreach (int j in list)
                    {
                        var h = _gridHouses[j];
                        if (into.Contains(h.E) || !EntityManager.Exists(h.E)) continue;
                        float2 half = (float2)h.Lot * 4f;
                        var inv = math.inverse(h.T.m_Rotation);
                        bool hit = false;
                        // the brush's disc along the stroke against the lot's rectangle (in the lot's own frame)
                        for (int k = 0; k <= steps && !hit; k++)
                        {
                            float2 p = math.lerp(a, b, k / (float)steps);
                            float3 local = math.mul(inv, new float3(p.x, h.T.m_Position.y, p.y) - h.T.m_Position);
                            hit = math.length(math.max(math.abs(local.xz) - half, 0f)) <= r;
                        }
                        if (!hit) continue;
                        into.Add(h.E);
                        float2 q = half - 0.4f;
                        rings.Add(new[]
                        {
                            global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(-q.x, 0f, -q.y)), global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(q.x, 0f, -q.y)),
                            global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(q.x, 0f, q.y)), global::Game.Objects.ObjectUtils.LocalToWorld(h.T, new float3(-q.x, 0f, q.y)),
                        });
                        added++;
                    }
                }
            return added;
        }
    }
}
