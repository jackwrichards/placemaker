// Placemaker neighbourhood polish — taking away planted pieces a garden no longer has (GPL-3.0).
//
// A bed, path or bush the polish gave a house earlier and a newer plan no longer wants (the garden changed: a neighbour
// came or went) is deleted here, at PostTool, where the game deletes a building's own pieces (SubElementDeleteSystem):
// every system of the frame then sees it go. Tagging it Deleted later in the frame (from the polish pass, Modification4B)
// left the renderer and the area geometry pointing at it, and the game crashed natively.

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourDeleteSystem : GameSystemBase
    {
        public static readonly List<Entity> Queue = new List<Entity>();
        public static int TotalDeleted;
        /// <summary>What this system marked Deleted this frame: the polish tells its own deletions from the game's
        /// (NeighbourPolishSystem.Rebuilt: pieces the game took away are planned or made again).</summary>
        public static readonly HashSet<Entity> Marked = new HashSet<Entity>();

        [Preserve]
        protected override void OnUpdate()
        {
            // (its main thread time, for the game runner's outgrowFrames)
            long timed = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateTimed(); }
            finally { NeighbourPolishSystem.NoteSystem(4, timed); }
        }

        private void UpdateTimed()
        {
            Marked.Clear();
            if (Queue.Count == 0) return;
            try
            {
                foreach (var e in Queue)
                {
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                    EntityManager.AddComponent<Deleted>(e);
                    Marked.Add(e);
                    TotalDeleted++;
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker neighbourhood clean-up failed: " + e.Message); }
            Queue.Clear();
        }
    }
}
