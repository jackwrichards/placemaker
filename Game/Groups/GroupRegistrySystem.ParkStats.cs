// Placemaker — keeps the park stats of loaded builds in line with their receipts and the settings (GPL-3.0).
// Attractiveness lives either on the owner lot (vanilla AttractivenessProvider, BuildService.ParkStats.cs) or,
// with "Real park stats", on the companion park building (Economy/ParkCompanionSystem). After a load we repair:
//  * companion setting off → companions are removed (PreTool) and the lot gets attractiveness back;
//  * a companion whose prefab did not resolve (clone list lost) → removed and created again from the receipt;
//  * the lot carries attractiveness only while there is no companion and "counts for tourism" is on.

using System;
using System.Collections.Generic;
using Game.Buildings;
using Game.Prefabs;
using Placemaker.Core.Build;
using Placemaker.Game.Build;
using Placemaker.Game.Economy;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Groups
{
    public sealed partial class GroupRegistrySystem
    {
        /// <summary>Where the group's park building stands (ParkStatsRecord.Applied; kept when it is taken away, for regrouping).</summary>
        public const string CompanionX = "companion.x", CompanionZ = "companion.z", CompanionYaw = "companion.yaw";

        private struct ParkSyncCounts { public int LotAdded, LotRemoved, Removed, Requested; }

        private void SyncParkStats()
        {
            var n = new ParkSyncCounts();
            foreach (var kv in _records) SyncParkStatsOf(kv.Key, kv.Value, ref n);
            if (n.LotAdded + n.LotRemoved + n.Removed + n.Requested > 0)
                Mod.Log.Info($"Placemaker park stats after load: lot attractiveness {n.LotAdded} restored / {n.LotRemoved} removed, "
                    + $"{n.Removed} companions removed, {n.Requested} recreated.");
        }

        /// <summary>
        /// One build's park building and lot attractiveness, in line with its receipt, the settings and its grouping: a build in
        /// pieces (ungrouped, until it is dissolved) is no park, so no park building (no upkeep) and no attractiveness on its lot
        /// (the user, 26 Oct: a Placemaker park acts like one vanilla park).
        /// </summary>
        private void SyncParkStatsOf(int groupId, Entity recordEntity, ref ParkSyncCounts n)
        {
            try
            {
                var companions = World.GetOrCreateSystemManaged<ParkCompanionSystem>();
                var cleanup = World.GetOrCreateSystemManaged<ParkCompanionCleanupSystem>();
                var g = EntityManager.GetComponentData<PlacemakerGroup>(recordEntity);
                var rec = GetRecord(groupId);
                if (rec?.ParkStats == null) return;
                var applied = rec.ParkStats.Applied;
                bool ungrouped = _ungrouped.Contains(groupId);

                Entity companion = Entity.Null;
                var members = new List<Entity>();
                GetMembers(groupId, members);
                foreach (var m in members)
                    if (EntityManager.Exists(m) && EntityManager.GetComponentData<PlacemakerMember>(m).Role == MemberRole.ParkCompanion) { companion = m; break; }
                bool wanted = BuildService.ApplyParkStats && companions.Available && applied.ContainsKey(ParkStatIds.Companion) && !ungrouped;
                string name = ParkPrefabKey.From(applied, rec.ParkStats.LeisureType == (int)LeisureKind.CityBeach).Name;
                if (companion != Entity.Null)
                {
                    var prefab = EntityManager.HasComponent<PrefabRef>(companion) ? EntityManager.GetComponentData<PrefabRef>(companion).m_Prefab : Entity.Null;
                    bool broken = prefab == Entity.Null || !EntityManager.Exists(prefab) || !EntityManager.HasComponent<ParkData>(prefab);
                    if (!wanted || broken)
                    {
                        float2 at = default; float yaw = 0;
                        if (EntityManager.HasComponent<global::Game.Objects.Transform>(companion))
                        {
                            var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(companion);
                            at = t.m_Position.xz;
                            var f = math.mul(t.m_Rotation, new float3(0, 0, 1));
                            yaw = math.atan2(f.x, f.z);
                            applied[CompanionX] = at.x; applied[CompanionZ] = at.y; applied[CompanionYaw] = yaw;
                            WriteRecord(recordEntity, rec);
                        }
                        cleanup.Remove(companion);
                        companion = Entity.Null;
                        n.Removed++;
                        if (ungrouped) Mod.Log.Info($"Placemaker: group {groupId} is in pieces: its park building (upkeep, park stats) is taken away.");
                        if (wanted && broken)
                        {
                            companions.Request(groupId, name, at, yaw);
                            n.Requested++;
                        }
                    }
                }
                else if (wanted && applied.TryGetValue(CompanionX, out var x) && applied.TryGetValue(CompanionZ, out var z))
                {
                    // grouped (again), or gone: the park building where it stood
                    companions.Request(groupId, name, new float2((float)x, (float)z), applied.TryGetValue(CompanionYaw, out var yw) ? (float)yw : 0f);
                    n.Requested++;
                }
                else if (wanted && applied.TryGetValue(ParkStatIds.Companion, out var state) && state == 1)
                    Mod.Log.Warn($"Placemaker: the park companion of group {groupId} is gone (removed by another tool?); its lot carries attractiveness again.");

                var owner = g.Owner;
                if (owner == Entity.Null || !EntityManager.Exists(owner)) return;
                bool has = applied.TryGetValue(ParkStatIds.InGameAttractiveness, out double value) && value > 0;
                bool present = EntityManager.HasComponent<AttractivenessProvider>(owner);
                bool lotShould = BuildService.ApplyParkStats && has && companion == Entity.Null && !ungrouped;
                if (present && !lotShould)
                {
                    EntityManager.RemoveComponent<AttractivenessProvider>(owner);
                    n.LotRemoved++;
                }
                else if (lotShould && !present && BuildService.ApplyAttractiveness(EntityManager, owner, value, 0) > 0)
                    n.LotAdded++;
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Placemaker: park stats of group {groupId} not synced: {e.Message}");
            }
        }
    }
}
