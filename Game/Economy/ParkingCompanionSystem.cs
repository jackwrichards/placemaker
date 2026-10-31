// Placemaker — hidden companion building for parking upkeep and employees (GPL-3.0).
// Port of ParkingLotTool's "Wirtschaftsbegleiter" (ParkingLotBuildingEconomy*, ParkingLotEmployee-
// System, ParkingLotEconomySystem — KruemelGames, GPL-3.0), reduced to the measured recipe.
//
// ON by default since P2-5 (2026-10-31, game runner send-parking-companion-1031-132310: made for a German lot, the city ran,
// removed with nothing left, no exception). Before: EXPERIMENTAL and OFF (CompanionEnabled): the recipe crashed natively several times
// during PLT's development before it settled; it needs in-game verification on 1.6.2f1 before it
// may default on. Fees/comfort/capacity (ParkingEconomySystem) work without it.
//
// Engine rules (RESEARCH-NOTES 1–3, PLT notes):
//  * The clone prefab is registered in PrefabUpdate BEFORE PrefabInitializeSystem, always (even
//    when the feature is off), so saves that contain companions resolve their prefab by name.
//  * Prefab-entity mutations happen once, in Modification2 before Game.Buildings.InitializeSystem,
//    and only ≥ 3 frames after registration when the archetype exists.
//  * Never put UpdateFrame on anything but a real Building (UpdateGroupSystem kills Mono otherwise).
//  * Never mark an incomplete companion Updated; remove by adding Deleted only (via group removal).

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core.Build;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Economy
{
    /// <summary>Registers the companion building prefab. Phase: PrefabUpdate, before PrefabInitializeSystem.</summary>
    public sealed partial class ParkingCompanionPrefabSystem : GameSystemBase
    {
        public const string PrefabName = "Placemaker Parking Companion";
        private static readonly string[] Sources = { "ParkingLot04", "ParkingLot01", "ParkingLot02", "ParkingLot03" };
        /// <summary>Upkeep basis: ServiceUsage = upkeep / basis (scale-with-usage money entry).</summary>
        public const int UpkeepBasis = 65536;

        private PrefabSystem _prefabs;
        private EntityQuery _buildings;
        private int _attempts;

        public Entity Prefab { get; private set; }
        public int RegisteredFrame { get; private set; } = -1;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _buildings = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<ParkingFacilityData>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (Prefab != Entity.Null || _attempts > 600 || _buildings.IsEmptyIgnoreFilter) return;
            _attempts++;
            try
            {
                BuildingPrefab source = null;
                using (var list = _buildings.ToEntityArray(Allocator.Temp))
                    foreach (var name in Sources)
                    {
                        for (int i = 0; i < list.Length && source == null; i++)
                            if (_prefabs.TryGetPrefab<BuildingPrefab>(list[i], out var b) && b != null && b.name == name) source = b;
                        if (source != null) break;
                    }
                if (source == null) return;
                var companion = (BuildingPrefab)source.Clone(PrefabName);
                companion.m_LotWidth = 1;
                companion.m_LotDepth = 1;
                var terraform = companion.AddOrGetComponent<BuildingTerraformOverride>();
                terraform.m_DontRaise = true;
                terraform.m_DontLower = true;
                companion.m_Meshes = new ObjectMeshInfo[0];
                if (companion.TryGet<ServiceConsumption>(out var c))
                {
                    c.m_Upkeep = 0; c.m_ElectricityConsumption = 0; c.m_WaterConsumption = 0; c.m_GarbageAccumulation = 0; c.m_TelecomNeed = 0f;
                }
                companion.Remove<CityServiceBuilding>();
                var service = companion.AddComponent<CityServiceBuilding>();
                service.m_Upkeeps = new[]
                {
                    new ServiceUpkeepItem
                    {
                        m_Resources = new ResourceStackInEditor { m_Resource = global::Game.Economy.ResourceInEditor.Money, m_Amount = UpkeepBasis },
                        m_ScaleWithUsage = true,
                    },
                };
                // No own parking, policies, effects, menu entry or sub elements.
                companion.Remove<global::Game.Prefabs.ParkingFacility>();
                companion.Remove<DefaultPolicies>();
                companion.Remove<EffectSource>();
                companion.Remove<UIObject>();
                companion.Remove<ObjectSubObjects>();
                companion.Remove<ObjectSubNets>();
                companion.Remove<ObjectSubLanes>();
                companion.Remove<ObjectSubAreas>();
                companion.Remove<ObsoleteIdentifiers>();
                if (!Placemaker.Game.Assets.AssetCatalogSystem.AddOwnPrefab(_prefabs, companion))
                {
                    UnityEngine.Object.Destroy(companion);
                    _attempts = int.MaxValue;
                    return;
                }
                Prefab = _prefabs.GetEntity(companion);
                RegisteredFrame = UnityEngine.Time.frameCount;
                Mod.Log.Info($"Placemaker registered the parking companion prefab (from {source.name}).");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Placemaker parking companion prefab unavailable: " + e.Message);
                _attempts = int.MaxValue;
            }
        }
    }

    /// <summary>
    /// Creates companions for parking groups. Phase: Modification2, before Game.Buildings.InitializeSystem
    /// (all companion components must exist before Building becomes visible to other systems).
    /// </summary>
    public sealed partial class ParkingCompanionSystem : GameSystemBase
    {
        public static bool CompanionEnabled = true;
        private const int ReadyDelayFrames = 3;

        private ParkingCompanionPrefabSystem _prefabSystem;
        private GroupRegistrySystem _registry;
        private TerrainSystem _terrain;
        private readonly List<int> _pending = new List<int>();
        private bool _prefabMutated;
        private bool _failed;

        public void Request(int groupId)
        {
            if (CompanionEnabled && !_failed) { if (!_pending.Contains(groupId)) _pending.Add(groupId); }
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<ParkingCompanionPrefabSystem>();
            _registry = World.GetOrCreateSystemManaged<GroupRegistrySystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!CompanionEnabled || _failed || _pending.Count == 0) return;
            try
            {
                var prefab = _prefabSystem.Prefab;
                if (prefab == Entity.Null || UnityEngine.Time.frameCount - _prefabSystem.RegisteredFrame < ReadyDelayFrames) return;
                if (!EntityManager.HasComponent<ObjectData>(prefab) || !EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype.Valid) return;
                if (!_prefabMutated) { MutatePrefab(prefab); _prefabMutated = true; return; }
                int groupId = _pending[0]; _pending.RemoveAt(0);
                CreateCompanion(groupId, prefab);
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker parking companion failed; companions disabled for this session");
                _failed = true;
                _pending.Clear();
            }
        }

        private void SetOrAdd<T>(Entity e, T value) where T : unmanaged, IComponentData
        {
            // Add first, then set: SetComponentData on a missing type can corrupt memory (rule 14).
            if (EntityManager.HasComponent<T>(e)) EntityManager.SetComponentData(e, value);
            else EntityManager.AddComponentData(e, value);
        }

        private void MutatePrefab(Entity prefab)
        {
            var bd = EntityManager.GetComponentData<BuildingData>(prefab);
            bd.m_LotSize = new int2(1, 1);
            // RoadConnectionSystem exits early with NoRoadConnection; it is what links the building to
            // its road edge (rebuilding that by hand crashed in PLT).
            bd.m_Flags &= ~global::Game.Prefabs.BuildingFlags.NoRoadConnection;
            EntityManager.SetComponentData(prefab, bd);
            SetOrAdd(prefab, new PollutionData { m_NoisePollution = 0f, m_ScaleWithRenters = false });
            SetOrAdd(prefab, new DestructibleObjectData { m_FireHazard = 0f, m_StructuralIntegrity = float.MaxValue });
            SetOrAdd(prefab, new ObjectGeometryData
            {
                m_Bounds = new Bounds3(new float3(-0.5f), new float3(0.5f)),
                m_Size = new float3(1f),
                m_Flags = global::Game.Objects.GeometryFlags.Marker,
                m_Layers = MeshLayer.Marker,
            });
            SetOrAdd(prefab, new WorkplaceData { m_MaxWorkers = 4, m_MinimumWorkersLimit = 0, m_WorkConditions = 0 });
            if (EntityManager.HasBuffer<AdditionalBuildingTerraformElement>(prefab)) EntityManager.RemoveComponent<AdditionalBuildingTerraformElement>(prefab);
            if (EntityManager.HasComponent<BuildingTerraformData>(prefab))
            {
                // DontRaise/DontLower only touch the limits; the inherited flat zone would still level terrain.
                var t = EntityManager.GetComponentData<BuildingTerraformData>(prefab);
                t.m_FlatX0 = t.m_FlatX1 = t.m_FlatZ0 = t.m_FlatZ1 = default;
                EntityManager.SetComponentData(prefab, t);
            }
            // Initialize never ran for the money entry when registered by hand (PLT): make sure it exists.
            bool hasMoney = false;
            if (EntityManager.HasBuffer<ServiceUpkeepData>(prefab))
            {
                var upkeep = EntityManager.GetBuffer<ServiceUpkeepData>(prefab, true);
                for (int i = 0; i < upkeep.Length; i++)
                    if (upkeep[i].m_Upkeep.m_Resource == global::Game.Economy.Resource.Money && upkeep[i].m_ScaleWithUsage) hasMoney = true;
            }
            else EntityManager.AddBuffer<ServiceUpkeepData>(prefab);
            if (!hasMoney)
                EntityManager.GetBuffer<ServiceUpkeepData>(prefab).Add(new ServiceUpkeepData
                {
                    m_Upkeep = new ResourceStack { m_Resource = global::Game.Economy.Resource.Money, m_Amount = ParkingCompanionPrefabSystem.UpkeepBasis },
                    m_ScaleWithUsage = true,
                });
        }

        private void CreateCompanion(int groupId, Entity prefab)
        {
            if (!_registry.TryGetRecordEntity(groupId, out var record) || EntityManager.HasComponent<global::Placemaker.Game.Groups.PlacemakerRemoval>(record)) return;
            var g = EntityManager.GetComponentData<PlacemakerGroup>(record);
            if (g.Companion != Entity.Null && EntityManager.Exists(g.Companion)) return;
            if (!TryFindRoadSide(groupId, out var position, out var rotation)) return;

            var archetype = EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype;
            var companion = EntityManager.CreateEntity(archetype);
            EntityManager.AddComponent<Hidden>(companion);
            SetOrAdd(companion, new PrefabRef { m_Prefab = prefab });
            SetOrAdd(companion, new global::Game.Objects.Transform { m_Position = position, m_Rotation = rotation });
            if (g.Carrier != Entity.Null && EntityManager.Exists(g.Carrier))
                SetOrAdd(companion, new Attached(g.Carrier, Entity.Null, 0f));
            if (EntityManager.HasComponent<CurrentDistrict>(companion)) EntityManager.SetComponentData(companion, new CurrentDistrict(Entity.Null));
            if (EntityManager.HasComponent<PseudoRandomSeed>(companion))
                EntityManager.SetComponentData(companion, new PseudoRandomSeed((ushort)math.clamp(companion.Index, 1, ushort.MaxValue)));
            // UpdateFrame only because this is a real Building (UpdateGroupSystem supports buildings).
            if (EntityManager.HasComponent<UpdateFrame>(companion))
                EntityManager.SetSharedComponent(companion, new UpdateFrame((uint)companion.Index & 0xFu));
            EntityManager.AddComponentData(companion, new PlacemakerMember { GroupId = groupId, Kind = MemberKind.Auxiliary, Role = MemberRole.Companion });
            g.Companion = companion;
            EntityManager.SetComponentData(record, g);
            Mod.Log.Info($"Placemaker created the parking companion for group {groupId}.");
        }

        /// <summary>A dead-end aisle node (where the lot meets the road), facing away from the lot.</summary>
        private bool TryFindRoadSide(int groupId, out float3 position, out quaternion rotation)
        {
            position = default; rotation = quaternion.identity;
            var members = new List<Entity>();
            _registry.GetMembers(groupId, members);
            float3 centroid = 0; int n = 0;
            var degree = new Dictionary<Entity, int>();
            foreach (var m in members)
            {
                if (!EntityManager.HasComponent<Edge>(m)) continue;
                var tag = EntityManager.GetComponentData<PlacemakerMember>(m);
                if (tag.Role != MemberRole.ParkingAisle) continue;
                var edge = EntityManager.GetComponentData<Edge>(m);
                degree[edge.m_Start] = (degree.TryGetValue(edge.m_Start, out var a) ? a : 0) + 1;
                degree[edge.m_End] = (degree.TryGetValue(edge.m_End, out var b) ? b : 0) + 1;
                centroid += MathUtils.Position(EntityManager.GetComponentData<Curve>(m).m_Bezier, 0.5f);
                n++;
            }
            if (n == 0) return false;
            centroid /= n;
            foreach (var kv in degree)
            {
                if (kv.Value != 1 || !EntityManager.Exists(kv.Key) || !EntityManager.HasComponent<global::Game.Net.Node>(kv.Key)) continue;
                position = EntityManager.GetComponentData<global::Game.Net.Node>(kv.Key).m_Position;
                var heights = _terrain.GetHeightData(true);
                position.y = TerrainUtils.SampleHeight(ref heights, position);
                var dir = math.normalizesafe(new float3(position.x - centroid.x, 0, position.z - centroid.z), new float3(0, 0, 1));
                rotation = quaternion.LookRotationSafe(dir, math.up());
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Upkeep and employees of companions. Phase: GameSimulation, before CityServiceUpkeepSystem and
    /// WorkProviderSystem (vanilla then charges upkeep and hires/fires).
    /// </summary>
    public sealed partial class ParkingCompanionUpkeepSystem : GameSystemBase
    {
        /// <summary>Upkeep per month = 48 × stalls + 438 (PLT's calibration against vanilla lots) × factor.</summary>
        public static float UpkeepFactor = 1f;
        private EntityQuery _companions;
        private ParkingEconomySystem _economy;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 256;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _economy = World.GetOrCreateSystemManaged<ParkingEconomySystem>();
            _companions = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<Building>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_companions.IsEmptyIgnoreFilter) return;
            try
            {
                using var list = _companions.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < list.Length; i++)
                {
                    var e = list[i];
                    var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                    if (tag.Role != MemberRole.Companion) continue;
                    int capacity = _economy.CapacityOf(tag.GroupId);
                    bool on = ParkingCompanionSystem.CompanionEnabled && ParkingEconomySystem.FeatureEnabled;
                    float upkeep = on ? (48f * capacity + 438f) * math.max(0f, UpkeepFactor) : 0f;
                    // Only set what the archetype provides; never add CityServiceUpkeep/Resources later (PLT).
                    if (EntityManager.HasComponent<ServiceUsage>(e))
                        EntityManager.SetComponentData(e, new ServiceUsage { m_Usage = upkeep / ParkingCompanionPrefabSystem.UpkeepBasis });
                    int workers = !on || capacity <= 40 ? 0 : capacity < 300 ? 2 : 4;
                    if (!EntityManager.HasBuffer<Employee>(e)) EntityManager.AddBuffer<Employee>(e);
                    if (EntityManager.HasComponent<WorkProvider>(e))
                    {
                        var wp = EntityManager.GetComponentData<WorkProvider>(e);
                        if (wp.m_MaxWorkers != workers) { wp.m_MaxWorkers = workers; EntityManager.SetComponentData(e, wp); }
                    }
                    else EntityManager.AddComponentData(e, new WorkProvider { m_MaxWorkers = workers });
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Error(ex, "Placemaker companion upkeep failed; companions disabled");
                ParkingCompanionSystem.CompanionEnabled = false;
            }
        }
    }
}
