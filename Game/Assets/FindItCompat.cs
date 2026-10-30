// Placemaker — keeps our own helper prefabs out of Find It (GPL-3.0).
//
// Placemaker registers prefabs the game needs but a player must never place by hand: companion park buildings (one per
// distinct set of park stats), the parking companion, zone-block lots, surface overlay copies and the vehicle props made
// from vanilla vehicles. They have no menu entry in the game, but Find It indexes every prefab by its components, so they
// showed up there (the user, 2026-10-30). Find It skips any prefab whose name is in its blacklist
// (FindIt.Systems.PrefabIndexingSystem._blackList, read once in its OnCreate) and removes listed ones through
// FindIt.Utilities.FindItUtil.RemoveItem(Entity). Both are reached by reflection, only when Find It is loaded; any
// failure is logged once and ignored (Find It changed, nothing breaks).

using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Prefabs;
using Unity.Entities;

namespace Placemaker.Game.Assets
{
    public static class FindItCompat
    {
        private static readonly List<PrefabBase> s_own = new List<PrefabBase>();
        private static bool s_failed, s_logged;

        /// <summary>Remembers one of our own prefabs and hides it from Find It now, if Find It is running.</summary>
        public static void Track(PrefabSystem prefabs, PrefabBase prefab)
        {
            if (prefab == null) return;
            lock (s_own) s_own.Add(prefab);
            Hide(prefabs, new[] { prefab });
        }

        /// <summary>Hides every prefab we registered (after Find It has indexed a loaded game, and again later).</summary>
        public static void Sweep(PrefabSystem prefabs)
        {
            PrefabBase[] all;
            lock (s_own) all = s_own.ToArray();
            if (all.Length > 0) Hide(prefabs, all);
        }

        private static void Hide(PrefabSystem prefabs, IReadOnlyList<PrefabBase> list)
        {
            if (s_failed || prefabs == null) return;
            try
            {
                var world = prefabs.World;
                Type indexer = null, util = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "FindIt") continue;
                    indexer = asm.GetType("FindIt.Systems.PrefabIndexingSystem");
                    util = asm.GetType("FindIt.Utilities.FindItUtil");
                    break;
                }
                if (indexer == null) return;   // Find It is not installed
                var system = world.GetExistingSystemManaged(indexer);
                var black = system == null ? null : indexer.GetField("_blackList", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(system) as HashSet<string>;
                var remove = util?.GetMethod("RemoveItem", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Entity) }, null);
                int n = 0;
                foreach (var p in list)
                {
                    if (p == null || string.IsNullOrEmpty(p.name)) continue;
                    if (black != null && black.Add(p.name)) n++;
                    if (remove != null && prefabs.TryGetEntity(p, out var e))
                        try { remove.Invoke(null, new object[] { e }); } catch (Exception) { }
                }
                if (!s_logged && n > 0) { s_logged = true; Mod.Log.Info($"Placemaker: {n} helper prefab(s) hidden from Find It."); }
            }
            catch (Exception e)
            {
                s_failed = true;
                Mod.Log.Warn("Placemaker: could not hide helper prefabs from Find It: " + e.Message);
            }
        }
    }
}
