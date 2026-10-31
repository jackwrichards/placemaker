// Placemaker — parking economy: capacity, fee and comfort on real parking lanes (GPL-3.0).
// Ported from ParkingLotTool's ParkingLotComfortSystem/ParkingLotEconomySystem (KruemelGames, GPL-3.0).
//
// Each parking decal of a "parking" layer spawns a real ParkingLane (prefab SubLane). Vanilla
// PersonalCarAISystem charges whenever ParkingLane.m_ParkingFee > 0: the household pays, the city
// books it under PlayerResource.Parking. ParkingLaneDataSystem recomputes fee and comfort from the
// owner chain and zeroes them for our lanes (their root owner is not a parking building), so we
// write ours right after it (RESEARCH-NOTES 17).
//
// Phase: ModificationEnd, after Game.Pathfind.ParkingLaneDataSystem. Isolated: any exception
// disables the economy for the session; building and removal never depend on it.

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Tools;
using Placemaker.Core.Build;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Economy
{
    public sealed partial class ParkingEconomySystem : GameSystemBase, IEffectProvider
    {
        public const string FeeFact = "parking.fee";
        /// <summary>(P2-1) 1 = the fee is switched off on the click panel; the amount is kept for when it is switched on again (as PLT).</summary>
        public const string FeeOffFact = "parking.feeOff";
        public const string CapacityFact = "parking.capacity.real";
        /// <summary>Comfort of our stalls (PLT used 0.5: between street parking and a garage).</summary>
        public const float ComfortFactor = 0.5f;

        /// <summary>Setting: fee/comfort/capacity on Placemaker parking lots.</summary>
        public static bool FeatureEnabled = true;
        /// <summary>Setting: default fee for new lots when the plan does not set one (money per park).</summary>
        public static int DefaultFee = 0;

        private EntityQuery _laneQuery;
        private EntityQuery _allLanes;
        private GroupRegistrySystem _registry;
        private readonly Dictionary<int, int> _fees = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _capacity = new Dictionary<int, int>();
        private int _feesVersion = -1;
        private readonly HashSet<int> _refreshGroups = new HashSet<int>();

        public string Id => "parking";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _registry = World.GetOrCreateSystemManaged<GroupRegistrySystem>();
            _laneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<global::Game.Net.ParkingLane>(), ComponentType.ReadOnly<Owner>() },
                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<global::Game.Common.PathfindUpdated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _allLanes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<global::Game.Net.ParkingLane>(), ComponentType.ReadOnly<Owner>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
        }

        // ───────────────────────── public API ─────────────────────────

        public bool AppliesTo(int groupId)
        {
            var rec = _registry.GetRecord(groupId);
            return rec != null && (rec.Flags & GroupFlags.ParkingEconomy) != 0;
        }

        public void Attach(int groupId) => OnGroupBuilt(groupId);

        public void Detach(int groupId) { _fees.Remove(groupId); _capacity.Remove(groupId); }

        /// <summary>Called by BuildService after a successful build.</summary>
        public void OnGroupBuilt(int groupId)
        {
            if (!FeatureEnabled || !AppliesTo(groupId)) return;
            var rec = _registry.GetRecord(groupId);
            if (!rec.Facts.ContainsKey(FeeFact) && _registry.TryGetRecordEntity(groupId, out var entity))
            {
                rec.Facts[FeeFact] = DefaultFee;
                _registry.WriteRecord(entity, rec);
            }
            _feesVersion = -1;
            _refreshGroups.Add(groupId);
            World.GetOrCreateSystemManaged<ParkingCompanionSystem>().Request(groupId);
        }

        public int FeeOf(int groupId) { EnsureFees(); return _fees.TryGetValue(groupId, out var f) ? f : 0; }

        /// <summary>(P2-1) The fee amount the group keeps (also while switched off), and whether it is on.</summary>
        public bool TryFee(int groupId, out int amount, out bool on)
        {
            amount = 0; on = false;
            if (!FeatureEnabled || !AppliesTo(groupId)) return false;
            var rec = _registry.GetRecord(groupId);
            amount = rec.Facts.TryGetValue(FeeFact, out var f) ? (int)math.clamp(f, 0, 1000) : DefaultFee;
            on = !(rec.Facts.TryGetValue(FeeOffFact, out var off) && off > 0) && amount > 0;
            return true;
        }

        /// <summary>(P2-1) Click panel: switch the fee on or off, keeping the amount.</summary>
        public void SetFeeOn(int groupId, bool on)
        {
            var rec = _registry.GetRecord(groupId);
            if (rec == null || !_registry.TryGetRecordEntity(groupId, out var entity)) return;
            if (on) { rec.Facts.Remove(FeeOffFact); if (!rec.Facts.TryGetValue(FeeFact, out var f) || f <= 0) rec.Facts[FeeFact] = DefaultFee > 0 ? DefaultFee : 10; }
            else rec.Facts[FeeOffFact] = 1;
            _registry.WriteRecord(entity, rec);
            _feesVersion = -1;
            _refreshGroups.Add(groupId);
        }

        private readonly Dictionary<int, (int Spaces, int Taken)> _occupancy = new Dictionary<int, (int, int)>();
        private int _occupancyFrame = -1000;

        /// <summary>
        /// (P2-1) Spaces and parked cars per car park (PLT's "x of y taken"): every parking lane of a group and the parked cars
        /// on it (Game.Net.LaneObject). Counted at most once a second, on demand (the panel asks).
        /// </summary>
        public bool TryOccupancy(int groupId, out int spaces, out int taken)
        {
            RefreshOccupancy();
            spaces = taken = 0;
            if (!_occupancy.TryGetValue(groupId, out var o)) return false;
            spaces = o.Spaces; taken = o.Taken;
            return true;
        }

        /// <summary>Every car park with its occupancy (ids in build order).</summary>
        public IEnumerable<KeyValuePair<int, (int Spaces, int Taken)>> AllOccupancy()
        {
            RefreshOccupancy();
            return _occupancy;
        }

        private void RefreshOccupancy()
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame - _occupancyFrame < 60 && frame >= _occupancyFrame) return;
            _occupancyFrame = frame;
            _occupancy.Clear();
            using var lanes = _allLanes.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < lanes.Length; i++)
            {
                int group = GroupOfLane(lanes[i]);
                if (group == 0 || !AppliesTo(group)) continue;
                int cars = 0;
                if (EntityManager.HasBuffer<global::Game.Net.LaneObject>(lanes[i]))
                    foreach (var o in EntityManager.GetBuffer<global::Game.Net.LaneObject>(lanes[i], true))
                        if (EntityManager.HasComponent<global::Game.Vehicles.ParkedCar>(o.m_LaneObject)) cars++;
                _occupancy.TryGetValue(group, out var n);
                _occupancy[group] = (n.Spaces + 1, n.Taken + cars);
            }
        }

        /// <summary>Real stall count from the lanes (0 until counted).</summary>
        public int CapacityOf(int groupId) => _capacity.TryGetValue(groupId, out var c) ? c : 0;

        public void SetFee(int groupId, int fee)
        {
            fee = math.clamp(fee, 0, 1000);
            var rec = _registry.GetRecord(groupId);
            if (rec == null || !_registry.TryGetRecordEntity(groupId, out var entity)) return;
            rec.Facts[FeeFact] = fee;
            if (fee > 0) rec.Facts.Remove(FeeOffFact);
            _registry.WriteRecord(entity, rec);
            _feesVersion = -1;
            _refreshGroups.Add(groupId);
        }

        // ───────────────────────── update ─────────────────────────

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _fees.Clear(); _capacity.Clear(); _feesVersion = -1;
            _refreshAll = true;
        }

        private bool _refreshAll;

        [Preserve]
        protected override void OnUpdate()
        {
            if (!FeatureEnabled) return;
            try
            {
                EnsureFees();
                if (_fees.Count == 0) return;
                if (_refreshAll || _refreshGroups.Count > 0)
                {
                    ApplyTo(_allLanes, all: _refreshAll);
                    _refreshAll = false;
                    _refreshGroups.Clear();
                }
                else if (!_laneQuery.IsEmptyIgnoreFilter) ApplyTo(_laneQuery, all: false);
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker parking economy failed; disabled for this session (lots keep working without fees)");
                FeatureEnabled = false;
            }
        }

        private void EnsureFees()
        {
            if (_feesVersion == _registry.Version) return;
            _feesVersion = _registry.Version;
            _fees.Clear();
            foreach (var id in _registry.AllGroups)
            {
                var rec = _registry.GetRecord(id);
                if (rec == null || (rec.Flags & GroupFlags.ParkingEconomy) == 0) continue;
                bool off = rec.Facts.TryGetValue(FeeOffFact, out var o) && o > 0;
                _fees[id] = off ? 0 : rec.Facts.TryGetValue(FeeFact, out var f) ? (int)math.clamp(f, 0, 1000) : DefaultFee;
            }
        }

        private void ApplyTo(EntityQuery query, bool all)
        {
            using var lanes = query.ToEntityArray(Allocator.Temp);
            var counted = new Dictionary<int, int>();
            for (int i = 0; i < lanes.Length; i++)
            {
                int group = GroupOfLane(lanes[i]);
                if (group == 0 || !_fees.TryGetValue(group, out int fee)) continue;
                // Writing is idempotent and adds no Updated/PathfindUpdated, so it cannot loop with
                // ParkingLaneDataSystem.
                var lane = EntityManager.GetComponentData<global::Game.Net.ParkingLane>(lanes[i]);
                lane.m_ComfortFactor = (ushort)math.round(ComfortFactor * 65535f);
                lane.m_ParkingFee = (ushort)fee;
                EntityManager.SetComponentData(lanes[i], lane);
                counted.TryGetValue(group, out int n);
                counted[group] = n + 1;
            }
            if (query == _allLanes)
                foreach (var kv in counted) _capacity[kv.Key] = kv.Value;
        }

        /// <summary>Lane → decal (Owner) → PlacemakerMember. Bounded walk (≤ 4 owners).</summary>
        private int GroupOfLane(Entity lane)
        {
            var e = lane;
            for (int depth = 0; depth < 4; depth++)
            {
                if (!EntityManager.HasComponent<Owner>(e)) return 0;
                e = EntityManager.GetComponentData<Owner>(e).m_Owner;
                if (e == Entity.Null || !EntityManager.Exists(e)) return 0;
                if (EntityManager.HasComponent<PlacemakerMember>(e)) return EntityManager.GetComponentData<PlacemakerMember>(e).GroupId;
            }
            return 0;
        }
    }
}
