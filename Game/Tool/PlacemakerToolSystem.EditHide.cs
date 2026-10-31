// (P2-1) While a finished build is being edited its visible pieces (objects: trees, props, decals; areas: surfaces, lots)
// are hidden, so the new preview is not drawn on top of the old layout (game-runner run send-parking-touches-save-1031-001049,
// "edit-plan-touched": two car parks on top of each other). Nets stay: traffic keeps flowing and the roads stay pickable.
// Cancel shows them again; a rebuild deletes them anyway. Parking Lot Tool does the same (ParkingLotEditAbriss.cs).
// Game.Tools.Hidden is not saved, so a save made during an edit loads with the build visible.

using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private readonly List<Entity> _hiddenByEdit = new List<Entity>();

        /// <summary>Every write of the edited group goes through here: the old build hides with the edit and shows when it ends.</summary>
        private void SetEditingGroup(int groupId)
        {
            if (_editingGroup == groupId) return;
            ShowEditedBuild();
            _editingGroup = groupId;
            if (groupId != 0) HideEditedBuild(groupId);
        }

        private void HideEditedBuild(int groupId)
        {
            int areas = 0, objects = 0;
            _membersGroup = -1;   // fresh: the cached list can be of an older build
            foreach (var e in GroupMembers(groupId))
            {
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Hidden>(e) || EntityManager.HasComponent<Deleted>(e)) continue;   // companions are hidden already: never ours to show
                bool area = EntityManager.HasComponent<global::Game.Areas.Area>(e);
                if (!area && !EntityManager.HasComponent<global::Game.Objects.Object>(e)) continue;   // nets stay live
                EntityManager.AddComponent<Hidden>(e);
                if (!EntityManager.HasComponent<BatchesUpdated>(e)) EntityManager.AddComponent<BatchesUpdated>(e);
                _hiddenByEdit.Add(e);
                if (area) areas++; else objects++;
            }
            Mod.Log.Info($"Placemaker: editing group {groupId}: {objects} object(s) and {areas} area(s) of the old build hidden (nets stay).");
        }

        private void ShowEditedBuild()
        {
            if (_hiddenByEdit.Count == 0) return;
            int shown = 0;
            foreach (var e in _hiddenByEdit)
            {
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || !EntityManager.HasComponent<Hidden>(e)) continue;   // rebuilt: gone
                EntityManager.RemoveComponent<Hidden>(e);
                if (!EntityManager.HasComponent<BatchesUpdated>(e)) EntityManager.AddComponent<BatchesUpdated>(e);
                shown++;
            }
            _hiddenByEdit.Clear();
            if (shown > 0) Mod.Log.Info($"Placemaker: {shown} hidden piece(s) of the edited build shown again.");
        }
    }
}
