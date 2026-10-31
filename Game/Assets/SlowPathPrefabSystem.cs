// Placemaker — slow drive paths inside car parks (GPL-3.0). Port of ParkingLotTool's "Fahrprefabs" (KruemelGames, GPL-3.0):
// Tools/ParkingLotFahrprefabSystem.cs, ParkingLotFahrprefabKopie.cs, ParkingLotFahrregeln.cs, ParkingLotKlonregel.cs.
//
// Each invisible drive path a car park uses gets a copy "Placemaker Slow <name>" at 25 km/h whose car lanes cost more to
// drive (PathfindCostInfo behaviour 0.25, money 0.01, comfort 0.05; vanilla invisible paths 0.01 each), so through traffic
// keeps to the streets instead of cutting across the lot, and parkers still come in (PLT measured 2026-10-02: the cost
// stays under a pedestrian street's, else parking loses to the kerb).
//
// Engine rules (PLT, measured there):
//  * Registered in PrefabUpdate BEFORE PrefabInitializeSystem, always, so saves with these paths resolve them by name.
//    The names are save format: never rename them, nor their parts.
//  * The whole chain is copied: sections, pieces (LOD pieces too: MeshRef.Equals compares piece entities), lanes and the
//    car pathfind prefab. Geometry and surface assets stay shared, read only. Never copy PrefabBase/ComponentBase
//    bookkeeping or render caches (PLT's memory crash 2026-09-02 from a foreign back reference).
//  * Never inherit ObsoleteIdentifiers (a copy could take over a vanilla id in old saves), UI, spawn, placeholder or
//    asset-pack components.
//  * Pedestrian pathfind, aggregates and zone blocks are shared.

using System;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace Placemaker.Game.Assets
{
    /// <summary>Registers the slow copies of the drive paths. Phase: PrefabUpdate, before PrefabInitializeSystem.</summary>
    public sealed partial class SlowPathPrefabSystem : GameSystemBase
    {
        public const float SpeedKmh = 25f;
        public static readonly PathfindCostInfo DrivingCost = new PathfindCostInfo(0f, 0.25f, 0.01f, 0.05f);
        /// <summary>The vanilla paths copied (Core/Layers/Parking/ParkingAssets: the drive paths and the one-way entrance).</summary>
        public static readonly string[] Sources =
        {
            "Invisible Car Path - 1xTwoway",
            "Invisible Road Path - 1xTwoway",
            "Invisible Car Path - 2xTwoway",
            "Invisible Road Path - 2xTwoway",
            "Invisible Road Path - 1xOneway",
        };

        public static string SlowName(string source) => Placemaker.Core.Layers.Parking.ParkingAssets.SlowName(source);
        private static string PartName(string net, PrefabBase source) => net + " :: " + source.GetType().Name + " (" + source.name + ")";

        private PrefabSystem _prefabs;
        private EntityQuery _paths;
        private readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);
        private bool _failed;
        private int _attempts;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _paths = GetEntityQuery(ComponentType.ReadOnly<PathwayData>(), ComponentType.ReadOnly<NetData>(), ComponentType.Exclude<PlaceholderObjectElement>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_failed || _done.Count == Sources.Length || _attempts > 600 || _paths.IsEmptyIgnoreFilter) return;
            _attempts++;
            using var list = _paths.ToEntityArray(Allocator.Temp);
            foreach (var entity in list)
            {
                if (!_prefabs.TryGetPrefab<PathwayPrefab>(entity, out var source) || source == null || !source.isBuiltin
                    || Array.IndexOf(Sources, source.name) < 0 || _done.Contains(source.name)) continue;
                try
                {
                    var copy = ScriptableObject.CreateInstance<PathwayPrefab>();
                    copy.name = SlowName(source.name);
                    // the root without Unity/prefab bookkeeping; Isolate then copies every referenced info object
                    for (var t = source.GetType(); t != null && t != typeof(PrefabBase); t = t.BaseType)
                        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                            if (!f.IsInitOnly && !f.IsLiteral && (f.IsPublic || f.IsDefined(typeof(SerializeField), false)))
                                f.SetValue(copy, f.GetValue(source));
                    foreach (var c in source.components)
                        if (ChainCopy.Inherit(c)) copy.AddComponentFrom(c);
                    new ChainCopy(copy.name, Register).Isolate(copy);
                    Register(source, copy);
                    _done.Add(source.name);
                }
                catch (Exception e)
                {
                    _failed = true;
                    Mod.Log.Error(e, "Placemaker: the slow drive paths could not be registered (" + source.name + "); car parks use the vanilla paths.");
                    return;
                }
            }
            if (_done.Count == Sources.Length)
                Mod.Log.Info($"Placemaker registered {_done.Count} slow drive paths ({SpeedKmh} km/h, through traffic avoids car parks).");
        }

        private void Register(PrefabBase source, PrefabBase copy)
        {
            if (!AssetCatalogSystem.AddOwnPrefab(_prefabs, copy))
                throw new InvalidOperationException("AddPrefab refused: " + copy.name);
        }

        /// <summary>One net's own section/piece/lane/car-pathfind chain (PLT ParkingLotFahrprefabKopie).</summary>
        private sealed class ChainCopy
        {
            private readonly string _net;
            private readonly Action<PrefabBase, PrefabBase> _register;
            private readonly Dictionary<PrefabBase, PrefabBase> _prefabs = new Dictionary<PrefabBase, PrefabBase>();
            private readonly Dictionary<object, object> _values = new Dictionary<object, object>();

            public ChainCopy(string net, Action<PrefabBase, PrefabBase> register) { _net = net; _register = register; }

            /// <summary>Only before AddPrefab of the root net.</summary>
            public void Isolate(NetGeometryPrefab net)
            {
                CopyFields(net, net, typeof(PrefabBase));
                foreach (var c in net.components) CopyFields(c, c, typeof(ComponentBase));
                if (net is PathwayPrefab path) path.m_SpeedLimit = SpeedKmh;
                if (net is RoadPrefab road) road.m_SpeedLimit = SpeedKmh;
            }

            private PrefabBase Clone(PrefabBase source)
            {
                if (_prefabs.TryGetValue(source, out var known)) return known;
                var clone = (PrefabBase)ScriptableObject.CreateInstance(source.GetType());
                clone.name = PartName(_net, source);
                _prefabs.Add(source, clone);   // before recursing: sections and secondary lanes can form cycles
                CopyFields(source, clone, typeof(PrefabBase));
                foreach (var c in source.components)
                {
                    if (!Inherit(c)) continue;
                    var copy = clone.AddComponentFrom(c);
                    CopyFields(c, copy, typeof(ComponentBase));
                }
                var car = clone.GetComponent<CarPathfind>();
                if (car != null) car.m_DrivingCost = DrivingCost;
                _register(source, clone);
                return clone;
            }

            public static bool Inherit(ComponentBase c) =>
                c != null && !(c is ObsoleteIdentifiers) && !(c is UIObject)
                && !(c is SpawnableArea) && !(c is SpawnableObject) && !(c is SpawnableLane) && !(c is SpawnableBuilding)
                && !(c is PlaceholderArea) && !(c is PlaceholderObject) && !(c is PlaceholderLane) && !(c is PlaceholderBuilding)
                && !(c is AssetPackItem) && !(c is EditorAssetCategoryOverride);

            private void CopyFields(object source, object target, Type stop)
            {
                foreach (var f in Fields(source.GetType(), stop)) f.SetValue(target, CopyValue(f.GetValue(source)));
            }

            private object CopyValue(object value)
            {
                if (value == null) return null;
                if (value is PrefabBase prefab)
                {
                    // secondary lanes, net lanes and LOD render prefabs can reference more lanes and pieces
                    if (prefab is NetSectionPrefab || prefab is RenderPrefab || prefab is NetLanePrefab
                        || prefab is PathfindPrefab && prefab.GetComponent<CarPathfind>() != null)
                        return Clone(prefab);
                    return prefab;   // aggregates, zone blocks, pedestrian pathfind: shared
                }
                if (value is UnityEngine.Object) return value;
                var type = value.GetType();
                if (type.IsPrimitive || type.IsEnum || value is string) return value;
                if (_values.TryGetValue(value, out var before)) return before;
                if (value is Array array)
                {
                    var copy = (Array)array.Clone();
                    _values.Add(value, copy);
                    for (int i = 0; i < array.Length; i++) copy.SetValue(CopyValue(array.GetValue(i)), i);
                    return copy;
                }
                // only the game's serialized info objects, never asset database handles
                if (type.Namespace != "Game.Prefabs") return value;
                var fresh = Activator.CreateInstance(type);
                _values.Add(value, fresh);
                CopyFields(value, fresh, typeof(object));
                return fresh;
            }

            private static IEnumerable<FieldInfo> Fields(Type type, Type stop)
            {
                for (; type != null && type != stop; type = type.BaseType)
                    foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!f.IsInitOnly && !f.IsLiteral && !f.IsNotSerialized && (f.IsPublic || f.IsDefined(typeof(SerializeField), false)))
                            yield return f;
            }
        }
    }
}
