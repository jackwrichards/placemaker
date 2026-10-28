// Placemaker — lift raycast hits on any member of a build to its owner lot (GPL-3.0).
// Uses Harmony 2.2.2 (MIT, Andreas Pardeike) as a library; see Library/Harmony/LICENSE.
// Adapted from ParkingLotTool's ParkingLotRaycastPatch (KruemelGames, GPL-3.0).
//
// Why a postfix on ToolRaycastSystem.GetRaycastResult: every ToolBaseSystem reads the returned owner
// immediately for hover, selection and bulldozing. A later system could only redirect the info panel.
//
// Guards (all from PLT):
//  * RaycastFlags.SubElements: in that mode vanilla deliberately returns intermediate owners; a lift
//    would rob net/editor tools of the sub-elements they ask for.
//  * Only queries that can select whole things (StaticObjects or Areas in the type mask: default
//    tool, bulldozer). Net tools (upgrades, road building) keep their road hits.
//  * Transport stops stay themselves (line tool asks StaticObjects without SubElements).
//  * Only the query result changes; no world component is touched.

using System;
using Game.Common;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;

namespace Placemaker.Game.Groups.Patches
{
    [HarmonyPatch(typeof(ToolRaycastSystem), nameof(ToolRaycastSystem.GetRaycastResult))]
    internal static class RaycastLiftPatch
    {
        /// <summary>Setting: lift clicks on parts to the whole build (default on).</summary>
        public static bool Enabled = true;
        /// <summary>An exception happened: nothing is changed any more this session.</summary>
        private static bool _broken;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(ToolRaycastSystem), nameof(ToolRaycastSystem.GetRaycastResult)) != null) return true;
            Mod.Log.Warn("Placemaker: ToolRaycastSystem.GetRaycastResult no longer exists; builds are selectable per part only.");
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(ToolRaycastSystem __instance, ref bool __result, ref RaycastResult result) => Apply(__instance, ref __result, ref result);

        /// <summary>The lift itself (also called by the game runner's lab "pickPart" with the active tool's raycast settings).</summary>
        internal static void Apply(ToolRaycastSystem __instance, ref bool __result, ref RaycastResult result)
        {
            try
            {
                if (_broken || !__result || result.m_Owner == Entity.Null) return;
                if ((__instance.raycastFlags & RaycastFlags.SubElements) != 0) return;
                if ((__instance.typeMask & (TypeMask.StaticObjects | TypeMask.Areas)) == 0) return;
                var registry = GroupRegistrySystem.Instance;
                if (registry == null || registry.World != __instance.World) return;
                var em = __instance.World.EntityManager;
                var part = result.m_Owner;
                if (!em.Exists(part) || em.HasComponent<Temp>(part)) return;
                // Ungrouped: the part itself. The game hands over the top owner of what was hit: for most of our props and trees
                // that is the build's bare carrier (their shared owner), for the ground between them the build's lot; picking or
                // bulldozing either would take everything. The carrier gives way to the member that was hit, the lot to nothing.
                // A zone block lot left by a dissolved build is invisible: a click on it is a click on nothing.
                if (registry.IsLooseZoneBlockLot(part)) { __result = false; result = default; return; }
                if (registry.TryUngroupedPick(part, result.m_Hit.m_HitEntity, out var pick))
                {
                    if (pick == Entity.Null) { __result = false; result = default; }
                    else result.m_Owner = pick;
                    return;
                }
                if (!Enabled) return;   // the setting: parts are picked as the game picks them (an ungrouped build's are fixed above)
                if (em.HasComponent<global::Game.Routes.TransportStop>(part)) return;
                if (!registry.TryLiftToOwner(part, out var owner)) return;
                result.m_Owner = owner;
            }
            catch (Exception e)
            {
                // Never break the player's tools: give up lifting for this session.
                Enabled = false;
                _broken = true;
                Mod.Log.Error(e, "Placemaker raycast lift failed; disabled for this session");
            }
        }
    }
}
