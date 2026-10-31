// Placemaker — a stall without its painted marking (GPL-3.0). Port of ParkingLotTool's "PLT Invisible Parking Bay"
// (Tools/ParkingLotBayObjects.cs, KruemelGames, GPL-3.0).
//
// The vanilla stall decal does two things: its SubMesh paints the lines, its SubLane spawns the parking lane cars park on.
// A copy with no meshes keeps the lane and loses the paint: the parking layer's "Space markings" off uses it for every
// stall (Core/Layers/Parking/PlanBuilder.cs). PLT's lessons:
//  * A new prefab with each component copied by AddComponentFrom (a shallow Instantiate shares components with the
//    original: "referenced from another prefab", then a native crash on building).
//  * Never skip SubObjectDefaultProbability: it alone brings PlaceableObjectData.
//  * Registered before the game builds archetypes (here PrefabUpdate, before PrefabInitializeSystem, like our other
//    copies), never during play: a prefab added mid-game was never complete and crashed the build natively.
//  * Never inherit ObsoleteIdentifiers. The name is save format.

using System;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Assets
{
    /// <summary>Registers the invisible stall. Phase: PrefabUpdate, before PrefabInitializeSystem.</summary>
    public sealed partial class InvisibleBayPrefabSystem : GameSystemBase
    {
        public const string PrefabName = Placemaker.Core.Layers.Parking.ParkingAssets.InvisibleBay;
        private const string SourceName = Placemaker.Core.Layers.Parking.ParkingAssets.Decal;

        private PrefabSystem _prefabs;
        private EntityQuery _objects;
        private bool _done;
        private int _attempts;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _objects = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ObjectGeometryData>(), ComponentType.ReadOnly<SubLane>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_done || _attempts > 600 || _objects.IsEmptyIgnoreFilter) return;
            _attempts++;
            try
            {
                ObjectGeometryPrefab source = null;
                using (var list = _objects.ToEntityArray(Allocator.Temp))
                    for (int i = 0; i < list.Length && source == null; i++)
                        if (_prefabs.TryGetPrefab<ObjectGeometryPrefab>(list[i], out var p) && p != null && p.isBuiltin && p.name == SourceName) source = p;
                if (source == null) return;
                var copy = ScriptableObject.CreateInstance<StaticObjectPrefab>();
                copy.name = PrefabName;
                copy.m_Meshes = new ObjectMeshInfo[0];
                copy.m_Circular = source.m_Circular;
                var parts = new System.Collections.Generic.List<string>();
                foreach (var c in source.components)
                {
                    if (c == null || c is ObsoleteIdentifiers) continue;
                    copy.AddComponentFrom(c);
                    parts.Add(c.GetType().Name);
                }
                _done = true;
                if (!AssetCatalogSystem.AddOwnPrefab(_prefabs, copy))
                {
                    UnityEngine.Object.Destroy(copy);
                    Mod.Log.Warn($"Placemaker: '{PrefabName}' was refused; stalls keep their markings.");
                    return;
                }
                Mod.Log.Info($"Placemaker registered '{PrefabName}' (a copy of {SourceName} without its mesh; parts: {string.Join(", ", parts)}).");
            }
            catch (Exception e)
            {
                _done = true;
                Mod.Log.Warn("Placemaker: the invisible stall is unavailable (" + e.Message + "); stalls keep their markings.");
            }
        }
    }
}
