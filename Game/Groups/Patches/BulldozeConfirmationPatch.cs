// Placemaker — ask the vanilla bulldoze confirmation before a whole build is removed (GPL-3.0).
// Uses Harmony 2.2.2 (MIT, Andreas Pardeike) as a library; see Library/Harmony/LICENSE.
// Adapted from ParkingLotTool's ParkingLotBulldozeConfirmationPatch (KruemelGames, GPL-3.0).
//
// Vanilla BulldozeToolSystem only asks for (non-spawnable) buildings. Our builds are lot areas, so
// right before Apply we put the tool in its Waiting state and ask in the game's own dialog, with three
// answers (p1-bulldoze, the user: "do you want to bulldoze this or just ungroup it?"): Remove all
// (ConfirmAction(true) → State.Confirmed → Apply), Ungroup (the bulldoze is cancelled and the builds
// are ungrouped: their pieces stay) and Cancel. The game's ConfirmationDialog takes extra buttons
// (otherActions; its UI answers 0 = confirm, 1 = cancel, 2 + i = the i-th extra button). The OnUpdate
// prefix remembers whether this update is the confirmed one, so a confirmation never carries over to a
// later click. Without the app bindings the unchanged vanilla question (Yes / No) is asked instead.
//
// Guarded: m_State (read and written), OnUpdate, Apply and the private applyMode setter. If any is
// missing, Prepare returns false and only the question is skipped: the bulldozer still removes the
// build (and the cascade still removes every member).

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Areas;
using Game.Common;
using Game.Tools;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Placemaker.Game.Groups.Patches
{
    [HarmonyPatch]
    internal static class BulldozeConfirmationPatch
    {
        /// <summary>Setting: ask before bulldozing a whole Placemaker build (default on).</summary>
        public static bool Enabled = true;

        private sealed class Approval { internal bool Confirmed; }
        private static readonly ConditionalWeakTable<BulldozeToolSystem, Approval> Approvals = new ConditionalWeakTable<BulldozeToolSystem, Approval>();
        internal static readonly FieldInfo StateField = AccessTools.Field(typeof(BulldozeToolSystem), "m_State");
        private static readonly MethodInfo ApplyModeSetter = AccessTools.PropertySetter(typeof(ToolBaseSystem), "applyMode");

        [HarmonyPrepare]
        private static bool Prepare()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (StateField == null) missing.Add("BulldozeToolSystem.m_State");
            if (AccessTools.Method(typeof(BulldozeToolSystem), "OnUpdate") == null) missing.Add("BulldozeToolSystem.OnUpdate");
            if (AccessTools.Method(typeof(BulldozeToolSystem), "Apply") == null) missing.Add("BulldozeToolSystem.Apply");
            if (ApplyModeSetter == null) missing.Add("ToolBaseSystem.applyMode");
            if (missing.Count == 0) return true;
            Mod.Log.Warn("Placemaker: bulldoze confirmation disabled, missing " + string.Join(", ", missing.ToArray()));
            return false;
        }

        [HarmonyPatch(typeof(BulldozeToolSystem), "OnUpdate")]
        [HarmonyPrefix]
        private static void BeforeUpdate(BulldozeToolSystem __instance)
        {
            try { Approvals.GetOrCreateValue(__instance).Confirmed = StateField.GetValue(__instance).ToString() == "Confirmed"; }
            catch (Exception) { }
        }

        [HarmonyPatch(typeof(BulldozeToolSystem), "Apply")]
        [HarmonyPrefix]
        private static bool BeforeApply(BulldozeToolSystem __instance, JobHandle inputDeps, ref JobHandle __result)
        {
            try
            {
                var approval = Approvals.GetOrCreateValue(__instance);
                if (approval.Confirmed) { approval.Confirmed = false; return true; }
                if (!Enabled) return true;
                var registry = GroupRegistrySystem.Instance;
                if (registry == null || registry.World != __instance.World) return true;
                inputDeps.Complete();
                var groups = BulldozedGroups(__instance.EntityManager, registry);
                if (groups.Count == 0) return true;
                __result = inputDeps;
                ApplyModeSetter.Invoke(__instance, new object[] { ApplyMode.None });
                if (__instance.EventConfirmationRequested == null)
                {
                    // Without a UI receiver never delete silently.
                    StateField.SetValue(__instance, Enum.Parse(StateField.FieldType, "Cancelled"));
                    Mod.Log.Warn("Placemaker: bulldozing a build was cancelled because the confirmation dialog is unavailable.");
                    return false;
                }
                StateField.SetValue(__instance, Enum.Parse(StateField.FieldType, "Waiting"));
                if (!AskRemoveOrUngroup(__instance, registry, groups)) __instance.EventConfirmationRequested();
                return false;
            }
            catch (Exception e)
            {
                Enabled = false;
                Mod.Log.Error(e, "Placemaker bulldoze confirmation failed; disabled for this session");
                return true;
            }
        }

        /// <summary>
        /// Remove all / Ungroup / Cancel in the game's dialog. False when it cannot be shown (the caller asks the vanilla way).
        /// The answer comes on the main thread (the UI's bindings update); a tool that left its Waiting state meanwhile ignores it.
        /// </summary>
        internal static bool AskRemoveOrUngroup(BulldozeToolSystem tool, GroupRegistrySystem registry, System.Collections.Generic.List<int> groups)
        {
            var bindings = global::Game.SceneFlow.GameManager.instance?.userInterface?.appBindings;
            if (bindings == null) return false;
            var dialog = new global::Game.UI.ConfirmationDialog("Placemaker.Bulldoze.Title", "Placemaker.Bulldoze.Message",
                "Placemaker.Bulldoze.RemoveAll", "Placemaker.Bulldoze.Cancel", "Placemaker.Bulldoze.Ungroup");
            bindings.ShowConfirmationDialog(dialog, answer =>
            {
                try
                {
                    if (answer == 0) { tool.ConfirmAction(true); return; }
                    tool.ConfirmAction(false);
                    if (answer != 2) return;
                    foreach (var id in groups)
                        if (registry.Ungroup(id)) Mod.Log.Info($"Placemaker: group {id} ungrouped from the bulldozer's question; its pieces stay.");
                }
                catch (Exception e) { Mod.Log.Error(e, "Placemaker: the bulldozer's question failed"); }
            });
            return true;
        }

        /// <summary>The groups this bulldoze would remove whole: their owner lot, or a part of a group without one.</summary>
        private static System.Collections.Generic.List<int> BulldozedGroups(EntityManager em, GroupRegistrySystem registry)
        {
            var groups = new System.Collections.Generic.List<int>();
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<Temp>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var temp = em.GetComponentData<Temp>(entities[i]);
                if ((temp.m_Flags & TempFlags.Delete) == 0 || !registry.IsOwnerArea(temp.m_Original)) continue;
                int id = em.GetComponentData<PlacemakerMember>(temp.m_Original).GroupId;
                if (!groups.Contains(id)) groups.Add(id);
            }
            // A group without owner lot: bulldozing any of its objects/areas removes the whole group, so ask too.
            using var parts = em.CreateEntityQuery(ComponentType.ReadOnly<Temp>());
            using var temps = parts.ToComponentDataArray<Temp>(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
                if ((temps[i].m_Flags & TempFlags.Delete) != 0 && registry.IsOwnerlessGroupPart(temps[i].m_Original, out int id) && !groups.Contains(id)) groups.Add(id);
            return groups;
        }
    }
}
