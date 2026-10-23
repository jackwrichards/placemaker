// Placemaker neighbourhood polish — the fence and hedge pieces the game hides, shown again before they are ever drawn
// hidden (GPL-3.0).
//
// Game.Objects.OverrideSystem (Modification5) hides any overridable object touching another one or a neighbour's lot, and
// does it again whenever something beside it changes: in a growing town, all the time. This runs every frame at
// ModificationEnd, after its barrier, before culling and rendering, so a garden's own pieces it hid this frame are back in
// the same frame (NeighbourPolishSystem.ShowHiddenPieces: the rules for the ones left hidden on purpose).

using System;
using Game;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourShowSystem : GameSystemBase
    {
        private NeighbourPolishSystem _polish;
        private int _errors;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _polish = World.GetOrCreateSystemManaged<NeighbourPolishSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // (its main thread time, for the game runner's outgrowFrames)
            long timed = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateTimed(); }
            finally { NeighbourPolishSystem.NoteSystem(6, timed); }
        }

        private void UpdateTimed()
        {
            try { _polish.ShowHiddenPieces(); }
            catch (Exception e) { if (_errors++ < 5) Mod.Log.Error(e, "Placemaker neighbourhood polish (hidden pieces) failed"); }
        }
    }
}
