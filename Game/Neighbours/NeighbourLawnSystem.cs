// Placemaker neighbourhood polish — writes the lawns NeighbourPolishSystem planned (GPL-3.0).
//
// A surface's nodes may only change before Game.Areas.GeometrySystem (Modification2B) in a frame: that system
// re-triangulates Updated areas, and everything after it (Areas.SearchSystem, SurfaceExpandSystem, the area batches)
// reads the triangles. NeighbourPolishSystem plans in Modification4B (after the houses' fence lanes are rebuilt), so its
// lawn edits wait here for the next frame. A first version wrote them in 4B and crashed the game natively on the
// first pass (triangles pointing past the new ring).
//
// The first time a house's own lawn is reshaped its nodes as the game made them are kept with it (PolishLawnOriginal,
// saved with the city): Unpolish, or a plan that no longer grows it, writes them back and drops the copy.

using System.Collections.Generic;
using Game;
using Game.Common;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourLawnSystem : GameSystemBase
    {
        public struct Write
        {
            public Entity Lawn;
            /// <summary>The new ring (ignored when <see cref="Restore"/>).</summary>
            public float3[] Nodes;
            /// <summary>Back to the nodes the game made (the kept copy), which is then dropped.</summary>
            public bool Restore;
        }

        public static readonly List<Write> Queue = new List<Write>();
        /// <summary>For the game runner: lawns written (reshaped or put back).</summary>
        public static int TotalWritten;

        [Preserve]
        protected override void OnUpdate()
        {
            // (its main thread time, for the game runner's outgrowFrames)
            long timed = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateTimed(); }
            finally { NeighbourPolishSystem.NoteSystem(1, timed); }
        }

        private void UpdateTimed()
        {
            if (Queue.Count == 0) return;
            foreach (var w in Queue)
            {
                var lawn = w.Lawn;
                if (!EntityManager.Exists(lawn) || EntityManager.HasComponent<Deleted>(lawn) || !EntityManager.HasBuffer<global::Game.Areas.Node>(lawn)) continue;
                bool kept = EntityManager.HasBuffer<PolishLawnOriginal>(lawn);
                float3[] ring;
                if (w.Restore)
                {
                    if (!kept) continue;
                    var original = EntityManager.GetBuffer<PolishLawnOriginal>(lawn, true);
                    ring = new float3[original.Length];
                    for (int i = 0; i < ring.Length; i++) ring[i] = original[i].Position;
                    EntityManager.RemoveComponent<PolishLawnOriginal>(lawn);
                    if (ring.Length < 3) continue;
                }
                else
                {
                    ring = w.Nodes;
                    if (ring == null || ring.Length < 3) continue;
                    // the same outline it has: nothing to write (writing makes the game build the lawn again)
                    if (kept)
                    {
                        var have = EntityManager.GetBuffer<global::Game.Areas.Node>(lawn, true);
                        var now = new float2[have.Length];
                        for (int i = 0; i < now.Length; i++) now[i] = have[i].m_Position.xz;
                        var next = new float2[ring.Length];
                        for (int i = 0; i < next.Length; i++) next[i] = ring[i].xz;
                        if (PieceIdentity.SameRing(next, now)) continue;
                    }
                    if (!kept)
                    {
                        var now = EntityManager.GetBuffer<global::Game.Areas.Node>(lawn, true);
                        var copy = new float3[now.Length];
                        for (int i = 0; i < copy.Length; i++) copy[i] = now[i].m_Position;
                        var keep = EntityManager.AddBuffer<PolishLawnOriginal>(lawn);
                        foreach (var p in copy) keep.Add(new PolishLawnOriginal { Position = p });
                    }
                }
                var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(lawn);
                nodes.Clear();
                foreach (var p in ring) nodes.Add(new global::Game.Areas.Node(p, float.MinValue));
                EntityManager.AddComponent<Updated>(lawn);
                TotalWritten++;
            }
            Queue.Clear();
        }
    }
}
