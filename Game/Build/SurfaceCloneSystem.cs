// Placemaker — runtime "overlay" clones of surface prefabs (GPL-3.0).
// Ported from ParkingLotTool's ParkingLotApronPrefab + Flaechenklonname (KruemelGames, GPL-3.0).
//
// Why: vanilla surfaces draw at priority −100…−95 on the Terrain decal layer only, so a parking
// apron would vanish under road decals at its entrances. The clone draws at −94 on Terrain|Roads
// (RESEARCH-NOTES 10).
//
// Engine rules honoured (RESEARCH-NOTES 1–3):
//  * AddPrefab only in PrefabUpdate BEFORE PrefabInitializeSystem, otherwise the clone misses
//    Initialize/LateInitialize and never gets an archetype.
//  * A clone is used only some frames after registration, once its archetype exists (same-frame
//    use = native crash).
//  * Saves reference prefabs by name: every clone ever used must exist before a save loads.
//    Clones of all built-in surfaces are created unconditionally at startup; clones of modded
//    surfaces are remembered in ModsData/Placemaker/surface-overlays.txt and recreated at startup.
//  * Never inherit SpawnableArea (would join "Pavement Surface Placeholder" candidates → zoned
//    houses got road-overdrawing pavement) or UIObject (would appear in the Surfaces menu).
//
// Phase: PrefabUpdate, before Game.Prefabs.PrefabInitializeSystem.

using System;
using System.Collections.Generic;
using System.IO;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Build
{
    public sealed partial class SurfaceCloneSystem : GameSystemBase
    {
        public const string NamePrefix = "Placemaker Overlay (";
        public const int OverlayPriority = -94;
        private const int ReadyDelayFrames = 3;

        private PrefabSystem _prefabs;
        private EntityQuery _surfaceQuery;
        private readonly Dictionary<Entity, Entity> _cloneOf = new Dictionary<Entity, Entity>();
        private readonly Dictionary<Entity, int> _registeredFrame = new Dictionary<Entity, int>();
        private readonly HashSet<string> _requestedNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _queue = new List<string>();
        private readonly HashSet<string> _remembered = new HashSet<string>(StringComparer.Ordinal);
        private bool _builtinsDone;
        // Modded surfaces (EAI packs) arrive 30–75 s after startup. The old limit (600 frames, ~10 s) gave up before them,
        // so a save naming an overlay of a pack surface loaded with that prefab missing (GitHub issue #2, the decal copies'
        // mechanism). Now looked for every half second until all are there.
        private const int RememberedPollFrames = 30;
        private int _rememberedPoll;
        private bool _rememberedAllFound;

        /// <summary>Switch for the feature (setting). Off = parking surfaces use the originals.</summary>
        public static bool FeatureEnabled = true;

        public static string CloneName(string original) => NamePrefix + original + ")";

        public static bool TryParseCloneName(string name, out string original)
        {
            original = null;
            if (name == null || !name.StartsWith(NamePrefix, StringComparison.Ordinal) || !name.EndsWith(")", StringComparison.Ordinal)) return false;
            original = name.Substring(NamePrefix.Length, name.Length - NamePrefix.Length - 1);
            return original.Length > 0;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _surfaceQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<SurfaceData>(), ComponentType.ReadOnly<AreaGeometryData>());
            LoadRemembered();
        }

        /// <summary>The ready overlay clone for a surface prefab, or the original (and a request) when not ready.</summary>
        public Entity OverlayFor(Entity original)
        {
            if (!FeatureEnabled || original == Entity.Null) return original;
            try
            {
                if (_cloneOf.TryGetValue(original, out var clone) && IsReady(clone)) return clone;
                if (_prefabs.TryGetPrefab<SurfacePrefab>(original, out var prefab) && prefab != null)
                {
                    if (TryParseCloneName(prefab.name, out _)) return original;
                    Request(prefab.name);
                    if (!prefab.isBuiltin && _remembered.Add(prefab.name)) SaveRemembered();
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker overlay lookup failed: " + e.Message);
            }
            return original;
        }

        private void Request(string name)
        {
            if (_requestedNames.Add(name)) _queue.Add(name);
        }

        private bool IsReady(Entity clone)
        {
            if (clone == Entity.Null || !EntityManager.Exists(clone)) return false;
            if (!_registeredFrame.TryGetValue(clone, out var frame) || UnityEngine.Time.frameCount - frame < ReadyDelayFrames) return false;
            return EntityManager.HasComponent<AreaData>(clone) && EntityManager.GetComponentData<AreaData>(clone).m_Archetype.Valid
                && EntityManager.HasComponent<AreaGeometryData>(clone)
                && EntityManager.GetComponentData<AreaGeometryData>(clone).m_Type == global::Game.Areas.AreaType.Surface;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!FeatureEnabled) return;
            try
            {
                if (!_builtinsDone && !_surfaceQuery.IsEmptyIgnoreFilter) RequestBuiltins();
                if (!_rememberedAllFound && _remembered.Count > 0 && --_rememberedPoll <= 0) { _rememberedPoll = RememberedPollFrames; RequestRemembered(); }
                int budget = 64;   // spread a large first batch over a few frames
                while (_queue.Count > 0 && budget-- > 0) { var next = _queue[0]; _queue.RemoveAt(0); CreateClone(next); }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker surface overlay clones failed; parking surfaces use the originals");
                FeatureEnabled = false;
            }
        }

        private void RequestBuiltins()
        {
            _builtinsDone = true;
            using var surfaces = _surfaceQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < surfaces.Length; i++)
                if (_prefabs.TryGetPrefab<SurfacePrefab>(surfaces[i], out var p) && p != null && p.isBuiltin && !TryParseCloneName(p.name, out _))
                    Request(p.name);
        }

        private void RequestRemembered()
        {
            bool allFound = true;
            foreach (var name in _remembered)
            {
                if (_requestedNames.Contains(name)) continue;
                if (FindSurface(name, out _)) Request(name); else allFound = false;
            }
            if (allFound) _rememberedAllFound = true;   // a pack that never comes is looked for twice a second (cheap)
        }

        private bool FindSurface(string name, out SurfacePrefab prefab)
        {
            prefab = null;
            using var surfaces = _surfaceQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < surfaces.Length; i++)
                if (_prefabs.TryGetPrefab<SurfacePrefab>(surfaces[i], out var p) && p != null && p.name == name) { prefab = p; return true; }
            return false;
        }

        private void CreateClone(string originalName)
        {
            if (!FindSurface(originalName, out var original)) { _requestedNames.Remove(originalName); return; }
            if (!_prefabs.TryGetEntity(original, out var originalEntity)) return;
            var cloneName = CloneName(original.name);
            // Already registered (e.g. after a world reload): just map it.
            if (_prefabs.TryGetPrefab(new PrefabID(nameof(SurfacePrefab), cloneName), out var existing) && existing != null
                && _prefabs.TryGetEntity(existing, out var existingEntity))
            {
                _cloneOf[originalEntity] = existingEntity;
                if (!_registeredFrame.ContainsKey(existingEntity)) _registeredFrame[existingEntity] = UnityEngine.Time.frameCount;
                return;
            }
            // ScriptableObject.CreateInstance + AddComponentFrom, NOT Object.Instantiate (shares
            // ComponentBase instances with the original — PLT).
            var clone = ScriptableObject.CreateInstance<SurfacePrefab>();
            clone.name = cloneName;
            clone.m_Color = original.m_Color;
            clone.m_EdgeColor = original.m_EdgeColor;
            clone.m_SelectionColor = original.m_SelectionColor;
            clone.m_SelectionEdgeColor = original.m_SelectionEdgeColor;
            foreach (var component in original.components)
            {
                if (component == null || component is SpawnableArea || component is UIObject || component is ObsoleteIdentifiers) continue;
                clone.AddComponentFrom(component);
            }
            var rendered = clone.GetComponent<RenderedArea>();
            if (rendered == null) { UnityEngine.Object.Destroy(clone); return; }
            rendered.m_DecalLayerMask |= DecalLayers.Terrain | DecalLayers.Roads;
            rendered.m_RendererPriority = OverlayPriority;
            if (!Placemaker.Game.Assets.AssetCatalogSystem.AddOwnPrefab(_prefabs, clone))
            {
                UnityEngine.Object.Destroy(clone);
                return;
            }
            var entity = _prefabs.GetEntity(clone);
            _cloneOf[originalEntity] = entity;
            _registeredFrame[entity] = UnityEngine.Time.frameCount;
        }

        // ───────────────────────── persistence of modded originals ─────────────────────────

        private static string RememberedPath =>
            Path.Combine(Application.persistentDataPath, "ModsData", "Placemaker", "surface-overlays.txt");

        private void LoadRemembered()
        {
            try
            {
                if (!File.Exists(RememberedPath)) return;
                foreach (var line in File.ReadAllLines(RememberedPath))
                    if (!string.IsNullOrWhiteSpace(line)) _remembered.Add(line.Trim());
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker could not read the surface overlay list: " + e.Message); }
        }

        private void SaveRemembered()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RememberedPath));
                var list = new List<string>(_remembered);
                list.Sort(StringComparer.Ordinal);
                File.WriteAllLines(RememberedPath, list);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker could not write the surface overlay list: " + e.Message); }
        }
    }
}
