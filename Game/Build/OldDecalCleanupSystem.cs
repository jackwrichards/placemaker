// Placemaker — clears the see-through decal copies that older builds left in saves (GPL-3.0).
//
// GitHub issue #2 ("white boxes around props after loading"): builds up to 0.1.1 placed decorative decals (all from decal
// packs; the base game has only markings and Chirper logos) as see-through copies made at runtime ("Placemaker Decal
// <tier> - <name>", DecalOpacitySystem). A save names them; when a copy did not exist at load (the packs arrive 30–75 s
// after startup and the copies were given up after 600 frames) the game drew its missing-object box in its place
// (ResolvePrefabsSystem keeps an obsolete placeholder, InitializeObsoleteSystem gives it MeshSettingsData
// .m_MissingObjectMesh). The user (2026-10-19) dropped decorative decals: a copy can never outlive the mod. So no copies
// are made any more, and after a load every object still on a missing copy is deleted; saved again, the city no longer
// names them. Decals a build placed at full strength (the pack's own prefab) are left alone.
//
// Phase: PreTool, as GroupRemovalSystem: Deleted marked here is seen by every system of the frame (RESEARCH-NOTES 11–12).

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Build
{
    public sealed partial class OldDecalCleanupSystem : GameSystemBase
    {
        private PrefabSystem _prefabs;
        private EntityQuery _prefabQuery;
        private EntityQuery _objectQuery;
        private bool _pending;

        /// <summary>Objects deleted after the last load (the game runner reads it).</summary>
        public static int LastRemoved;
        /// <summary>Game runner only: the clean-up skipped, for the "before" picture of a reload.</summary>
        public static bool SuiteSkip;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            // Obsolete placeholders have PrefabData disabled (ResolvePrefabsSystem): look at every prefab entity.
            _prefabQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabData>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            _objectQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _pending = mode.IsGame();
            LastRemoved = 0;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!_pending) return;
            _pending = false;
            if (SuiteSkip) { Mod.Log.Info("Placemaker load: old see-through decals left as they are (game runner)"); return; }
            try { Clean(); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: clearing old see-through decals after load failed: " + e.Message); }
        }

        private void Clean()
        {
            var placeholders = new HashSet<Entity>();
            var names = new List<string>();
            using (var prefabs = _prefabQuery.ToEntityArray(Allocator.Temp))
            using (var data = _prefabQuery.ToComponentDataArray<PrefabData>(Allocator.Temp))
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (data[i].m_Index >= 0) continue;   // a loaded prefab; only obsolete placeholders are negative
                    string name;
                    try { name = _prefabs.GetObsoleteID(data[i]).GetName(); }
                    catch (Exception) { continue; }   // a placeholder of an earlier load
                    if (name == null || !name.StartsWith(Core.Decals.DecalLook.ClonePrefix, StringComparison.Ordinal)) continue;
                    placeholders.Add(prefabs[i]);
                    names.Add(name);
                }
            }
            if (placeholders.Count == 0) return;

            var doomed = new NativeList<Entity>(Allocator.Temp);
            try
            {
                using (var objects = _objectQuery.ToEntityArray(Allocator.Temp))
                using (var refs = _objectQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    for (int i = 0; i < objects.Length; i++)
                        if (placeholders.Contains(refs[i].m_Prefab)) doomed.Add(objects[i]);
                }
                if (doomed.Length > 0) EntityManager.AddComponent<Deleted>(doomed.AsArray());
                LastRemoved = doomed.Length;
            }
            finally { doomed.Dispose(); }
            World.GetExistingSystemManaged<Groups.GroupRegistrySystem>()?.MarkDirty();
            Mod.Log.Info($"Placemaker load: removed {LastRemoved} see-through decal(s) of older builds ({names.Count} missing copies, e.g. '{names[0]}'); "
                + "Placemaker no longer places decorative decals (GitHub issue #2).");
        }
    }
}
