// Guard against the native crash in TerrainSystem.CullForCascades (parks makeover, 2026-10-30; docs/runbooks/parks.md).
//
// The crash (runs 20261030-001417-bushes and the one in AGENTS.md, both on the 4th build of a fresh game): a heap free fails
// inside NativeList<LaneSection>.set_Capacity in TerrainSystem.CullForCascades. Decompiled (Game.Simulation.TerrainSystem):
// the road cull list is sized to 6 sections per road entity (m_RoadsGroup count x 6, + max(n/4, 250)) and filled by
// CullRoadsJob with AddNoResize, which does not check the capacity in a release build. One edge writes up to 18 sections
// (AddSegment writes 1-3 per segment, 2 segments for the edge and up to 2 for each end node); terrain-shifting nets (paths that
// flatten the ground, with offsets) write the extra ones. A build of many short park paths pushes the list past its capacity:
// the job writes over the next heap block and the crash surfaces at the next resize.
//
// Before our nets are emitted (build and ghost), the list gets room for what is already there twice over plus 18 sections for
// every net we add: the vanilla code still grows it by its own rule, we only make sure it never overflows. Every job that reads or
// writes the list is completed first (the cull, the clip-map and the cascade lane jobs), as the game does before it resizes.

using System;
using System.Collections;
using System.Reflection;
using Game.Simulation;
using Unity.Jobs;

namespace Placemaker.Game.Build
{
    internal static class TerrainCullGuard
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo CullFinished = typeof(TerrainSystem).GetField("m_CullFinished", Any);
        private static readonly FieldInfo LaneCull = typeof(TerrainSystem).GetField("m_LaneCull", Any);
        private static readonly FieldInfo ClipMapCull = typeof(TerrainSystem).GetField("m_ClipMapCull", Any);
        private static readonly FieldInfo Cascades = typeof(TerrainSystem).GetField("m_CascadeCulling", Any);
        private static FieldInfo _laneHandle;
        private static bool _warned;
        public static int Grown;

        /// <summary>Makes room in the terrain's road cull list for <paramref name="newNets"/> more nets. Main thread only.</summary>
        public static void Reserve(TerrainSystem terrain, int newNets)
        {
            if (terrain == null) return;
            try
            {
                Complete(CullFinished, terrain);
                Complete(LaneCull, terrain);
                Complete(ClipMapCull, terrain);
                if (Cascades?.GetValue(terrain) is IList list)
                    foreach (var info in list)
                    {
                        if (info == null) continue;
                        _laneHandle ??= info.GetType().GetField("m_LaneHandle", Any);
                        if (_laneHandle?.GetValue(info) is JobHandle h) h.Complete();
                    }
                var roads = terrain.GetRoads();   // completes the cull job too
                if (!roads.IsCreated) return;
                int want = roads.Length * 2 + Math.Max(0, newNets) * 18 + 2000;
                // the evidence: a list longer than its capacity has already been written past its end
                if (roads.Length > roads.Capacity) Mod.Log.Warn($"Placemaker terrain cull guard: the road cull list overflowed ({roads.Length} sections in a capacity of {roads.Capacity}).");
                if (roads.Capacity < want)
                {
                    int was = roads.Capacity;
                    roads.Capacity = want;
                    Grown++;
                    Mod.Log.Info($"Placemaker terrain cull guard: road cull list {roads.Length} sections, capacity {was} -> {want} (+{newNets} nets).");
                }
            }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                Mod.Log.Warn($"Placemaker terrain cull guard: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void Complete(FieldInfo f, object owner)
        {
            if (f?.GetValue(owner) is JobHandle h) h.Complete();
        }
    }
}
