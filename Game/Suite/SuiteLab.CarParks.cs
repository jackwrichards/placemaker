// Game runner — what the Manage panel knows about each car park (P2-1: fee, occupancy, the car park list). GPL-3.0.
//   {"do":"lab","op":"carParks","label":"after the build"}

using System.Collections.Generic;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Placemaker.Game.Economy;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private void LabCarParks(JObject step, JObject rec)
        {
            string label = (string)step["label"] ?? "";
            var economy = _world.GetExistingSystemManaged<ParkingEconomySystem>();
            var groups = Services.Groups;
            if (economy == null || groups == null) { rec["error"] = "no parking economy or groups"; return; }
            var list = new JArray();
            foreach (int id in groups.AllGroups)
            {
                bool applies = economy.AppliesTo(id);
                bool fee = economy.TryFee(id, out int amount, out bool on);
                bool occ = economy.TryOccupancy(id, out int spaces, out int taken);
                list.Add(new JObject { ["id"] = id, ["carPark"] = applies, ["fee"] = fee ? amount : -1, ["feeOn"] = on, ["spaces"] = occ ? spaces : -1, ["taken"] = occ ? taken : -1 });
                Mod.Log.Info($"Placemaker lab: car parks {label}: group {id}: car park {applies}, fee feature {ParkingEconomySystem.FeatureEnabled}, fee {(fee ? amount.ToString() : "none")} (on {on}), occupancy {(occ ? taken + " of " + spaces : "none")}; managed {_tool.ManagedGroup}, stage {_tool.Stage}.");
            }
            rec["carParks"] = list;
            // the parking lanes of each group's members, by the member's prefab (what the occupancy counts as spaces)
            var em = _world.EntityManager;
            var prefabs = _world.GetOrCreateSystemManaged<PrefabSystem>();
            foreach (int id in groups.AllGroups)
            {
                var members = new List<Entity>();
                try { groups.GetMembers(id, members); } catch (System.Exception) { continue; }
                var set = new HashSet<Entity>(members);
                var byOwner = new SortedDictionary<string, int>();
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Net.ParkingLane>(), ComponentType.ReadOnly<global::Game.Common.Owner>(), ComponentType.Exclude<global::Game.Common.Deleted>(), ComponentType.Exclude<global::Game.Tools.Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var lane in all)
                    {
                        var e = lane;
                        for (int d = 0; d < 4 && em.HasComponent<global::Game.Common.Owner>(e); d++)
                        {
                            e = em.GetComponentData<global::Game.Common.Owner>(e).m_Owner;
                            if (!set.Contains(e)) continue;
                            string name = em.HasComponent<PrefabRef>(e) && prefabs.TryGetPrefab<PrefabBase>(em.GetComponentData<PrefabRef>(e).m_Prefab, out var pb) && pb != null ? pb.name : "?";
                            byOwner[name] = (byOwner.TryGetValue(name, out var n) ? n : 0) + 1;
                            break;
                        }
                    }
                var parts = new List<string>();
                foreach (var kv in byOwner) parts.Add(kv.Value + " x " + kv.Key);
                Mod.Log.Info($"Placemaker lab: car parks {label}: group {id}: parking lanes by member: {string.Join(", ", parts)}.");
            }
        }
    }
}
