// Placemaker — the game's own vehicles as static props (Industrial category, 2026-10-19) (GPL-3.0).
//
// The base game has no car-shaped prop at all: a car yard, a breakers' yard or a staff car park could only be built from
// stand-ins. Every vehicle prefab carries its meshes, so a static object prefab that shows the same meshes is a parked
// (or wrecked) vehicle: "Placemaker Vehicle Car 1".."Car 10", "Van 1".., "Truck", "Garbage", "Tractor", "Camper",
// "Loader" (Core/Layers/Types/Industrial/IndustrialAssets.cs, VehicleProps). Each slot takes a vehicle of its kind from
// the playset, sorted by name, so a slot keeps its vehicle from one load to the next (saves reference the slot by name).
// A kind with fewer vehicles than slots repeats them; a kind the playset lacks makes no slot and the plan's fallback id
// (pack props, then a vanilla stand-in) is built instead.
//
// Engine rules (as ParkingCompanionPrefabSystem / DecalOpacitySystem):
//  * registered in PrefabUpdate BEFORE PrefabInitializeSystem, as soon as the vehicle prefabs exist, before a save loads;
//  * CreateInstance + our own fields, never Object.Instantiate (shared ComponentBase instances); the RenderPrefabs are the
//    vehicle's own (shared, not copied); only the meshes every state shows (m_RequireState None) are kept;
//  * a PlaceableObject component at no cost so the object tool path accepts it; no UI entry (not a catalogue item:
//    AssetCatalogSystem.IsOwnPrefabName), never a building, never mesh-less.

using System;
using System.Collections.Generic;
using Game;
using Game.Objects;
using Game.Prefabs;
using Placemaker.Core.Layers.Types.Yards;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Assets
{
    public sealed partial class VehiclePropSystem : GameSystemBase
    {
        private PrefabSystem _prefabs;
        private EntityQuery _cars;
        private bool _done;
        private int _attempts, _quietFrames, _lastCount = -1;

        /// <summary>Slot name → prop prefab entity (for the log and the suite).</summary>
        public static readonly Dictionary<string, string> Made = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float3> _sizes = new Dictionary<string, float3>(StringComparer.Ordinal);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _cars = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<CarData>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_done || _attempts > 1200) return;
            _attempts++;
            // Wait until the vehicle prefabs stop arriving (asset packs load in batches), then make every slot once.
            int count = _cars.CalculateEntityCount();
            if (count == 0) return;
            if (count != _lastCount) { _lastCount = count; _quietFrames = 0; return; }
            if (++_quietFrames < 30) return;
            _done = true;
            try { MakeAll(); }
            catch (Exception e) { Mod.Log.Warn("Placemaker vehicle props unavailable: " + e.Message); }
        }

        private void MakeAll()
        {
            var byKind = new Dictionary<string, List<ObjectGeometryPrefab>>(StringComparer.Ordinal);
            foreach (var (kind, _) in VehicleProps.Slots) byKind[kind] = new List<ObjectGeometryPrefab>();
            using (var list = _cars.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < list.Length; i++)
                {
                    var e = list[i];
                    if (!_prefabs.TryGetPrefab<PrefabBase>(e, out var p) || !(p is ObjectGeometryPrefab g) || g.m_Meshes == null || g.m_Meshes.Length == 0) continue;
                    if (!p.isBuiltin) continue;   // vanilla vehicles only (the user, 26 Oct: Placemaker's own choices are vanilla)
                    var kind = KindOf(e, g);
                    if (kind != null && byKind.TryGetValue(kind, out var l))
                    {
                        l.Add(g);
                        if (EntityManager.HasComponent<ObjectGeometryData>(e)) _sizes[g.name] = EntityManager.GetComponentData<ObjectGeometryData>(e).m_Size;
                    }
                }
            int made = 0;
            var summary = new List<string>();
            foreach (var (kind, slots) in VehicleProps.Slots)
            {
                var l = byKind[kind];
                if (l.Count == 0) { summary.Add(kind + ": none"); continue; }
                l.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                var names = new List<string>();
                for (int i = 1; i <= slots; i++)
                {
                    var src = l[(i - 1) % l.Count];
                    string slot = VehicleProps.Prefix + kind + " " + i;
                    if (Make(slot, src))
                    {
                        made++; Made[slot] = src.name;
                        // The size goes in the log: the yards space their rows by it (IndustrialAssets).
                        if (i <= l.Count) names.Add(_sizes.TryGetValue(src.name, out var z) ? $"{src.name} {z.x:0.0}x{z.z:0.0}x{z.y:0.0} m" : src.name);
                    }
                }
                summary.Add($"{kind}: {string.Join(", ", names)}");
            }
            Mod.Log.Info($"Placemaker made {made} vehicle props from the vanilla vehicles ({string.Join("; ", summary)}).");
            if (made > 0) World.GetExistingSystemManaged<AssetCatalogSystem>()?.RequestRescan();
        }

        /// <summary>What a vehicle is for the yards (null = not wanted): by its data first, then by its name.</summary>
        private string KindOf(Entity e, ObjectGeometryPrefab g)
        {
            string n = g.name ?? "";
            var em = EntityManager;
            bool trailer = em.HasComponent<CarTrailerData>(e);
            float3 size = em.HasComponent<ObjectGeometryData>(e) ? em.GetComponentData<ObjectGeometryData>(e).m_Size : float3.zero;
            if (Has(n, "Camper", "Caravan") && trailer) return "Camper";
            if (trailer || Has(n, "Motorbike", "Bicycle", "Scooter", "Moped")) return null;
            if (em.HasComponent<GarbageTruckData>(e)) return "Garbage";
            // A loader fits a 5 × 9 m slot; the mining excavator (25 m) does not.
            if (Has(n, "FrontendLoader", "WheelLoader")) return "Loader";
            if (Has(n, "Tractor") && !Has(n, "TruckTractor", "Trailer")) return Has(n, "Mining", "Ore") ? "Tractor" : "FarmTractor";
            if (em.HasComponent<PersonalCarData>(e))
            {
                // Ordinary cars only: no motorbikes, no limousines or buses in private use.
                if (size.z < 3.4f || size.z > 5.6f || size.x < 1.5f) return null;
                return "Car";
            }
            if (em.HasComponent<DeliveryTruckData>(e))
                // By name: the geometry size of a cab or a van says little (the 2026-10-19 run sorted a truck cab as a van).
                return Has(n, "DeliveryVan", "Van0") ? "Van" : Has(n, "Truck") ? "Truck" : null;
            return null;
        }

        private static bool Has(string s, params string[] parts)
        {
            foreach (var p in parts) if (s.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private bool Make(string name, ObjectGeometryPrefab source)
        {
            if (_prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), name), out var existing) && existing != null) return true;
            var meshes = new List<ObjectMeshInfo>();
            foreach (var m in source.m_Meshes)
                if (m != null && m.m_Mesh != null && m.m_RequireState == ObjectState.None)
                    meshes.Add(new ObjectMeshInfo { m_Mesh = m.m_Mesh, m_Position = m.m_Position, m_Rotation = m.m_Rotation, m_RequireState = m.m_RequireState });
            if (meshes.Count == 0) return false;
            var prop = ScriptableObject.CreateInstance<StaticObjectPrefab>();
            prop.name = name;
            prop.m_Meshes = meshes.ToArray();
            prop.m_Circular = false;
            var placeable = prop.AddComponent<PlaceableObject>();
            placeable.m_ConstructionCost = 0;
            placeable.m_XPReward = 0;
            if (!AssetCatalogSystem.AddOwnPrefab(_prefabs, prop)) { UnityEngine.Object.Destroy(prop); return false; }
            return true;
        }
    }
}
