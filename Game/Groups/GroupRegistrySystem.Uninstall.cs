// Placemaker — a city made ready for removing the mod (GPL-3.0).
//
// p1-bulldoze (docs/research/dynamic-decals.md, "Saves and mod removal"): a save names every prefab it uses, and a prefab
// the game no longer has loads as the missing-mesh placeholder. Without Placemaker, the prefabs it makes at runtime come
// back as white boxes (park and parking companions, vehicle props) or the missing-area material (surface copies). Our own
// components are skipped cleanly. The settings button "Get this city ready to remove Placemaker" keeps every build as
// plain city pieces and leaves nothing of ours that needs the mod:
//   1. every build is ungrouped (Dissolve: no record, owner lot, companions or upkeep; its pieces stay);
//   2. then, in PreTool like every removal: each surface copy ("Placemaker Overlay (X)") is switched to the game's own X;
//      every other entity on one of our runtime prefabs (companions, vehicle props) is deleted. Zone blocks stay: without
//      the mod they still block the zone cells under the builds, which is what they are for.
// The player saves afterwards; the city then loads the same with or without Placemaker (minus the vehicle props).

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Game.Build;
using Unity.Collections;
using Unity.Entities;

namespace Placemaker.Game.Groups
{
    public sealed partial class GroupRegistrySystem
    {
        private bool _uninstallPending;

        /// <summary>Settings button: ungroup everything, then strip our runtime prefabs (<see cref="RunUninstallStrip"/>).</summary>
        public void PrepareForUninstall()
        {
            EnsureIndex();
            int n = 0;
            foreach (var id in new List<int>(_allIds)) if (Ungroup(id)) n++;
            _uninstallPending = true;
            Mod.Log.Info($"Placemaker: getting the city ready to remove the mod: {n} build(s) ungrouped; our own prefabs go once they are dissolved.");
        }

        internal bool HasPendingUninstall => _uninstallPending && _dissolvePending.Count == 0;

        /// <summary>PreTool (GroupRemovalSystem), after the dissolves: surface copies → the game's own surfaces; the rest of our runtime prefabs deleted.</summary>
        internal void RunUninstallStrip()
        {
            _uninstallPending = false;
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            var ownPrefab = new Dictionary<Entity, Entity>();   // our prefab → the vanilla one to switch to (Null: delete)
            using (var q = GetEntityQuery(ComponentType.ReadOnly<PrefabData>()).ToEntityArray(Allocator.Temp))
                foreach (var p in q)
                {
                    if (!prefabs.TryGetPrefab<PrefabBase>(p, out var prefab) || prefab == null || prefab.name == null) continue;
                    if (!prefab.name.StartsWith("Placemaker", StringComparison.Ordinal)) continue;
                    if (prefab.name == OwnerAreaPrefabSystem.BlockerPrefabName) continue;   // zone blocks stay (header)
                    Entity target = Entity.Null;
                    if (SurfaceCloneSystem.TryParseCloneName(prefab.name, out var original)
                        && prefabs.TryGetPrefab(new PrefabID(prefab.GetType().Name, original), out var vanilla) && vanilla != null)
                        target = prefabs.GetEntity(vanilla);
                    ownPrefab[p] = target;
                }
            int switched = 0, deleted = 0;
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            using (var q = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!ownPrefab.TryGetValue(prefab, out var target)) continue;
                    string name = prefabs.GetPrefabName(prefab);
                    counts[name] = (counts.TryGetValue(name, out var c) ? c : 0) + 1;
                    if (target != Entity.Null)
                    {
                        EntityManager.SetComponentData(e, new PrefabRef { m_Prefab = target });
                        if (!EntityManager.HasComponent<Updated>(e)) EntityManager.AddComponent<Updated>(e);
                        switched++;
                        continue;
                    }
                    if (EntityManager.HasComponent<global::Game.Companies.WorkProvider>(e))
                    {
                        var wp = EntityManager.GetComponentData<global::Game.Companies.WorkProvider>(e);
                        wp.m_MaxWorkers = 0;
                        EntityManager.SetComponentData(e, wp);
                    }
                    if (EntityManager.HasComponent<PlacemakerMember>(e)) EntityManager.RemoveComponent<PlacemakerMember>(e);
                    EntityManager.AddComponent<Deleted>(e);
                    deleted++;
                }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Value} {kv.Key}");
            Mod.Log.Info($"Placemaker: city ready to remove the mod: {switched} surface(s) switched to the game's own, {deleted} piece(s) on our own prefabs deleted"
                + (parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : "") + ". Save the city now.");
            MarkDirty();
        }
    }
}
