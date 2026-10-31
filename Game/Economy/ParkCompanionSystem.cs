// Placemaker — companion park building: the build's park stats made real (s17, docs/PARK-STATS.md §5) (GPL-3.0).
//
// Leisure, park coverage (→ wellbeing), land value from attractiveness, tourist trips and upkeep only work on a
// park BUILDING with a road connection, and AttractionSystem / ServiceCoverageSystem / ParkAISystem read the
// PREFAB. So each build gets one hidden 1-cell park building at its road frontage, made from a runtime clone of
// the vanilla Tiny Park whose components carry the build's (quantised) stats. Pattern: ParkingCompanionSystem.
//
// Engine rules (RESEARCH-NOTES 1–3, 14, 16; ParkingCompanionSystem notes):
//  * Clones are registered in PrefabUpdate BEFORE PrefabInitializeSystem and used ≥ 3 frames later. Saves reference
//    them by name: every name ever used is kept in ModsData/Placemaker/park-companions.txt and registered at startup,
//    so a save with companions resolves its prefabs. A name missing from the list (file deleted) is repaired on load:
//    the companion is removed and created again from the receipt.
//  * Never mesh-less: the clone keeps the Tiny Park meshes; the entity carries `Hidden` (not rendered, simulation
//    unaffected — only rendering/notification/tool systems look at Hidden).
//  * UpdateFrame only on this real Building. Add before set. Removal only by Deleted in PreTool (group removal or
//    ParkCompanionCleanupSystem).
//  * m_AllowHomeless = false; no electricity, water, garbage or telecom need; no menu entry, unlock, sub elements.

using System;
using System.Collections.Generic;
using System.IO;
using Game;
using Game.Buildings;
using Game.Areas;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Placemaker.Core.Build;
using Placemaker.Game.Build;
using Placemaker.Game.Groups;
using Placemaker.Game.Settings;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Economy
{
    /// <summary>Registers companion park prefabs. Phase: PrefabUpdate, before PrefabInitializeSystem.</summary>
    public sealed partial class ParkCompanionPrefabSystem : GameSystemBase
    {
        private static readonly string[] Sources = { "PocketPark01", "PocketPark02", "CityPark05", "CityPark01" };
        private const int ReadyDelayFrames = 3;
        private const int PerFrame = 8;
        private const string ListFile = "park-companions.txt";

        private PrefabSystem _prefabs;
        private EntityQuery _parks;
        private BuildingPrefab _source;
        private bool _failed, _listLoaded;
        private int _attempts;
        private readonly Dictionary<string, (Entity prefab, int frame)> _registered = new Dictionary<string, (Entity, int)>(StringComparer.Ordinal);
        private readonly List<string> _wanted = new List<string>();
        private readonly HashSet<string> _listed = new HashSet<string>(StringComparer.Ordinal);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _parks = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<ParkData>());
        }

        /// <summary>Asks for the clone with this name (registered on the next PrefabUpdate).</summary>
        public void Want(string name)
        {
            if (string.IsNullOrEmpty(name) || _registered.ContainsKey(name) || _wanted.Contains(name)) return;
            _wanted.Add(name);
        }

        /// <summary>The clone's prefab entity once it can be instantiated, else Entity.Null.</summary>
        public Entity Ready(string name)
        {
            if (name == null || !_registered.TryGetValue(name, out var r) || UnityEngine.Time.frameCount - r.frame < ReadyDelayFrames) return Entity.Null;
            if (!EntityManager.Exists(r.prefab) || !EntityManager.HasComponent<ObjectData>(r.prefab)
                || !EntityManager.GetComponentData<ObjectData>(r.prefab).m_Archetype.Valid) return Entity.Null;
            return r.prefab;
        }

        public bool IsCompanionPrefab(Entity prefab)
        {
            foreach (var kv in _registered) if (kv.Value.prefab == prefab) return true;
            return false;
        }

        public bool Failed => _failed;

        [Preserve]
        protected override void OnUpdate()
        {
            if (_failed) return;
            try
            {
                if (_source == null && !FindSource()) return;
                if (!_listLoaded) LoadList();
                for (int n = 0; n < PerFrame && _wanted.Count > 0; n++)
                {
                    var name = _wanted[0];
                    _wanted.RemoveAt(0);
                    if (!_registered.ContainsKey(name)) Register(name);
                }
            }
            catch (Exception e)
            {
                _failed = true;
                Mod.Log.Error(e, "Placemaker park companion prefabs unavailable; builds keep attractiveness on their lot");
            }
        }

        private bool FindSource()
        {
            if (_parks.IsEmptyIgnoreFilter || ++_attempts > 1200) return false;
            using var list = _parks.ToEntityArray(Allocator.Temp);
            foreach (var name in Sources)
                for (int i = 0; i < list.Length; i++)
                    if (_prefabs.TryGetPrefab<BuildingPrefab>(list[i], out var b) && b != null && b.name == name) { _source = b; return true; }
            return false;
        }

        private string ListPath => Path.Combine(ModPaths.DataFolder, ListFile);

        private void LoadList()
        {
            _listLoaded = true;
            try
            {
                if (!File.Exists(ListPath)) return;
                foreach (var line in File.ReadAllLines(ListPath))
                {
                    var name = line.Trim();
                    if (!ParkPrefabKey.TryParse(name, out _) || !_listed.Add(name)) continue;
                    Want(name);
                }
                Mod.Log.Info($"Placemaker: {_listed.Count} park companion prefabs to register from {ListFile}.");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: reading " + ListFile + " failed: " + e.Message); }
        }

        private void Remember(string name)
        {
            if (!_listed.Add(name)) return;
            try
            {
                var names = new List<string>(_listed);
                names.Sort(StringComparer.Ordinal);
                ModPaths.WriteAllTextAtomic(ListPath, string.Join("\n", names) + "\n");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: writing " + ListFile + " failed: " + e.Message); }
        }

        private void Register(string name)
        {
            if (!ParkPrefabKey.TryParse(name, out var key)) return;
            // After a world reload the prefab may already exist: just map it.
            if (_prefabs.TryGetPrefab(new PrefabID(nameof(BuildingPrefab), name), out var existing) && existing != null
                && _prefabs.TryGetEntity(existing, out var existingEntity))
            {
                _registered[name] = (existingEntity, UnityEngine.Time.frameCount);
                Remember(name);
                return;
            }
            var p = (BuildingPrefab)_source.Clone(name);
            p.m_LotWidth = 1;
            p.m_LotDepth = 1;
            var attraction = p.AddOrGetComponent<Attraction>();
            attraction.m_Attractiveness = key.Attractiveness;
            var leisure = p.AddOrGetComponent<global::Game.Prefabs.LeisureProvider>();
            leisure.m_Efficiency = Math.Max(1, key.Leisure);
            leisure.m_LeisureType = key.Beach ? global::Game.Agents.LeisureType.CityBeach : global::Game.Agents.LeisureType.CityPark;
            var coverage = p.AddOrGetComponent<global::Game.Prefabs.ServiceCoverage>();
            coverage.m_Range = key.Range;
            coverage.m_Capacity = key.Capacity;
            coverage.m_Magnitude = key.Magnitude;
            var consumption = p.AddOrGetComponent<ServiceConsumption>();
            consumption.m_Upkeep = key.Upkeep;
            consumption.m_ElectricityConsumption = 0;
            consumption.m_WaterConsumption = 0;
            consumption.m_GarbageAccumulation = 0;
            consumption.m_TelecomNeed = 0f;
            var park = p.AddOrGetComponent<global::Game.Prefabs.Park>();
            park.m_AllowHomeless = false;
            if (park.m_MaintenancePool <= 0) park.m_MaintenancePool = 250;
            var placeable = p.AddOrGetComponent<PlaceableObject>();
            placeable.m_ConstructionCost = 0;   // Placemaker charges the build itself
            placeable.m_XPReward = 0;
            var terraform = p.AddOrGetComponent<BuildingTerraformOverride>();
            terraform.m_DontRaise = true;
            terraform.m_DontLower = true;
            // Not in menus, never locked, no own sub objects/nets/lanes/areas or effects (the build is the park).
            p.Remove<UIObject>();
            p.Remove<Unlockable>();
            p.Remove<ObjectSubObjects>();
            p.Remove<ObjectSubNets>();
            p.Remove<ObjectSubLanes>();
            p.Remove<ObjectSubAreas>();
            p.Remove<EffectSource>();
            p.Remove<ObsoleteIdentifiers>();
            p.Remove<DefaultPolicies>();
            if (!Placemaker.Game.Assets.AssetCatalogSystem.AddOwnPrefab(_prefabs, p))
            {
                UnityEngine.Object.Destroy(p);
                Mod.Log.Warn($"Placemaker could not add the park companion prefab '{name}'.");
                return;
            }
            _registered[name] = (_prefabs.GetEntity(p), UnityEngine.Time.frameCount);
            Remember(name);
            Mod.Log.Info($"Placemaker registered park companion prefab '{name}' (from {_source.name}).");
        }
    }

    /// <summary>
    /// Creates companion park buildings. Phase: Modification2, before Game.Buildings.InitializeSystem (every
    /// component must exist before Building becomes visible to other systems).
    /// </summary>
    public sealed partial class ParkCompanionSystem : GameSystemBase
    {
        /// <summary>Setting "Real park stats (companion building)" (default on).</summary>
        public static bool Enabled = true;
        private const int GiveUpFrames = 900;

        private struct Pending { public int GroupId; public string Name; public float2 Position; public float Yaw; public int Since; }

        private ParkCompanionPrefabSystem _prefabs;
        private GroupRegistrySystem _registry;
        private TerrainSystem _terrain;
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly HashSet<Entity> _mutated = new HashSet<Entity>();
        private bool _failed;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<ParkCompanionPrefabSystem>();
            _registry = World.GetOrCreateSystemManaged<GroupRegistrySystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        public bool Available => Enabled && !_failed && !_prefabs.Failed;

        public void Request(int groupId, string prefabName, float2 position, float yaw)
        {
            if (!Available || groupId == 0) return;
            _prefabs.Want(prefabName);
            _pending.RemoveAll(p => p.GroupId == groupId);
            _pending.Add(new Pending { GroupId = groupId, Name = prefabName, Position = position, Yaw = yaw, Since = UnityEngine.Time.frameCount });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_pending.Count == 0) return;
            if (!Available) { _pending.Clear(); return; }
            try
            {
                var p = _pending[0];
                var prefab = _prefabs.Ready(p.Name);
                if (prefab == Entity.Null)
                {
                    if (UnityEngine.Time.frameCount - p.Since > GiveUpFrames)
                    {
                        _pending.RemoveAt(0);
                        Mod.Log.Warn($"Placemaker: park companion prefab '{p.Name}' never became ready; group {p.GroupId} keeps attractiveness on its lot.");
                    }
                    return;
                }
                if (_mutated.Add(prefab)) { MutatePrefab(prefab); return; }   // one frame between prefab edits and the first instance
                _pending.RemoveAt(0);
                Create(p, prefab);
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker park companion failed; companions disabled for this session (builds keep attractiveness on their lot)");
                _failed = true;
                _pending.Clear();
            }
        }

        private void SetOrAdd<T>(Entity e, T value) where T : unmanaged, IComponentData
        {
            if (EntityManager.HasComponent<T>(e)) EntityManager.SetComponentData(e, value);
            else EntityManager.AddComponentData(e, value);
        }

        private void MutatePrefab(Entity prefab)
        {
            if (EntityManager.HasBuffer<AdditionalBuildingTerraformElement>(prefab)) EntityManager.RemoveComponent<AdditionalBuildingTerraformElement>(prefab);
            if (EntityManager.HasComponent<BuildingTerraformData>(prefab))
            {
                // DontRaise/DontLower only touch the limits; the inherited flat zone would still level the terrain.
                var t = EntityManager.GetComponentData<BuildingTerraformData>(prefab);
                t.m_FlatX0 = t.m_FlatX1 = t.m_FlatZ0 = t.m_FlatZ1 = default;
                EntityManager.SetComponentData(prefab, t);
            }
        }

        private void Create(Pending p, Entity prefab)
        {
            if (!_registry.TryGetRecordEntity(p.GroupId, out var record) || EntityManager.HasComponent<PlacemakerRemoval>(record)) return;
            if (FindCompanion(p.GroupId) != Entity.Null) return;
            var heights = _terrain.GetHeightData(true);
            var position = new float3(p.Position.x, 0f, p.Position.y);
            position.y = TerrainUtils.SampleHeight(ref heights, position);
            var archetype = EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype;
            var e = EntityManager.CreateEntity(archetype);
            EntityManager.AddComponent<global::Game.Tools.Hidden>(e);
            SetOrAdd(e, new PrefabRef { m_Prefab = prefab });
            SetOrAdd(e, new global::Game.Objects.Transform { m_Position = position, m_Rotation = quaternion.RotateY(p.Yaw) });
            if (EntityManager.HasComponent<CurrentDistrict>(e)) EntityManager.SetComponentData(e, new CurrentDistrict(Entity.Null));
            if (EntityManager.HasComponent<PseudoRandomSeed>(e))
                EntityManager.SetComponentData(e, new PseudoRandomSeed((ushort)math.clamp(e.Index, 1, ushort.MaxValue)));
            if (EntityManager.HasComponent<UpdateFrame>(e))
                EntityManager.SetSharedComponent(e, new UpdateFrame((uint)e.Index & 0xFu));
            EntityManager.AddComponentData(e, new PlacemakerMember { GroupId = p.GroupId, Kind = MemberKind.Auxiliary, Role = MemberRole.ParkCompanion });
            // The building now carries attractiveness (vanilla AttractionSystem, with maintenance and terrain): drop the lot's.
            var g = EntityManager.GetComponentData<PlacemakerGroup>(record);
            if (g.Owner != Entity.Null) BuildService.ApplyAttractiveness(EntityManager, g.Owner, 0, 0);
            var rec = _registry.GetRecord(p.GroupId);
            if (rec?.ParkStats != null)
            {
                rec.ParkStats.Applied[ParkStatIds.Companion] = 1;
                rec.ParkStats.Applied[GroupRegistrySystem.CompanionX] = p.Position.x;
                rec.ParkStats.Applied[GroupRegistrySystem.CompanionZ] = p.Position.y;
                rec.ParkStats.Applied[GroupRegistrySystem.CompanionYaw] = p.Yaw;
                _registry.WriteRecord(record, rec);
            }
            _registry.MarkDirty();
            Mod.Log.Info($"Placemaker created the park companion of group {p.GroupId} ('{p.Name}').");
        }

        /// <summary>Live park companion of a group, or Entity.Null.</summary>
        public Entity FindCompanion(int groupId)
        {
            var list = new List<Entity>();
            _registry.GetMembers(groupId, list);
            foreach (var m in list)
                if (EntityManager.Exists(m) && !EntityManager.HasComponent<Deleted>(m) && EntityManager.HasComponent<PlacemakerMember>(m)
                    && EntityManager.GetComponentData<PlacemakerMember>(m).Role == MemberRole.ParkCompanion) return m;
            return Entity.Null;
        }
    }

    /// <summary>
    /// Removes park companions outside group removal: all of them when the setting is switched off (applied on the
    /// next load), or broken ones (prefab unresolved) so they can be recreated. Phase: PreTool (RESEARCH-NOTES 11).
    /// </summary>
    public sealed partial class ParkCompanionCleanupSystem : GameSystemBase
    {
        private readonly List<Entity> _queue = new List<Entity>();

        public void Remove(Entity companion)
        {
            if (companion != Entity.Null && !_queue.Contains(companion)) _queue.Add(companion);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_queue.Count == 0) return;
            try
            {
                int n = 0;
                while (_queue.Count > 0 && n < 32)
                {
                    var e = _queue[0];
                    _queue.RemoveAt(0);
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                    EntityManager.AddComponent<Deleted>(e);
                    n++;
                }
                if (n > 0) Mod.Log.Info($"Placemaker removed {n} park companion buildings.");
                World.GetOrCreateSystemManaged<GroupRegistrySystem>().MarkDirty();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker park companion cleanup failed");
                _queue.Clear();
            }
        }
    }
}
