// Game runner — what the drive paths of the builds really are (P2-1, slow paths). GPL-3.0.
//   {"do":"lab","op":"driveLanes","label":"after the build"}
//       per group: its path nets by prefab, and their car lanes' speed limit and driving cost (time, behaviour, money,
//       comfort) as the game holds them; plus how many of its objects and areas are hidden (Edit hides the old build).
//       The test world has no traffic, so this is the proof that through traffic is steered off: the same speed and cost
//       as Parking Lot Tool's paths (25 km/h; 0, 0.25, 0.01, 0.05) against the vanilla invisible paths.

using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private string VanillaLaneCost(PrefabSystem prefabs, string laneName)
        {
            var em = _world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NetLaneData>(), ComponentType.ReadOnly<PrefabData>());
            using var list = q.ToEntityArray(Allocator.Temp);
            foreach (var e in list)
            {
                if (!prefabs.TryGetPrefab<PrefabBase>(e, out var p) || p == null || p.name != laneName) continue;
                var pf = em.GetComponentData<NetLaneData>(e).m_PathfindPrefab;
                if (pf == Entity.Null || !em.HasComponent<PathfindCarData>(pf)) return "no car cost";
                var c = em.GetComponentData<PathfindCarData>(pf).m_DrivingCost.m_Value;
                return $"{laneName}: cost {c.x:0.##}/{c.y:0.##}/{c.z:0.##}/{c.w:0.##}";
            }
            return laneName + ": not found";
        }

        private void LabDriveLanes(JObject step, JObject rec)
        {
            var em = _world.EntityManager;
            var prefabs = _world.GetOrCreateSystemManaged<PrefabSystem>();
            string label = (string)step["label"] ?? "";
            var byGroup = new SortedDictionary<int, List<Entity>>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var list = q.ToEntityArray(Allocator.Temp))
                foreach (var e in list)
                {
                    int g = em.GetComponentData<PlacemakerMember>(e).GroupId;
                    if (!byGroup.TryGetValue(g, out var l)) byGroup[g] = l = new List<Entity>();
                    l.Add(e);
                }
            var groups = new JArray();
            foreach (var kv in byGroup)
            {
                var nets = new SortedDictionary<string, int>();
                var lanes = new SortedDictionary<string, int>();
                int hiddenObjects = 0, hiddenAreas = 0, objects = 0, areas = 0;
                foreach (var e in kv.Value)
                {
                    bool hidden = em.HasComponent<Hidden>(e);
                    if (em.HasComponent<global::Game.Areas.Area>(e)) { areas++; if (hidden) hiddenAreas++; }
                    else if (em.HasComponent<global::Game.Objects.Object>(e)) { objects++; if (hidden) hiddenObjects++; }
                    if (!em.HasComponent<global::Game.Net.Edge>(e) || !em.HasComponent<PrefabRef>(e)) continue;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<PathwayData>(prefab)) continue;
                    string name = prefabs.TryGetPrefab<PrefabBase>(prefab, out var pb) && pb != null ? pb.name : "?";
                    nets[name] = (nets.TryGetValue(name, out var n) ? n : 0) + 1;
                    if (!em.HasBuffer<global::Game.Net.SubLane>(e)) continue;
                    foreach (var sl in em.GetBuffer<global::Game.Net.SubLane>(e, true))
                    {
                        var lane = sl.m_SubLane;
                        if (!em.HasComponent<global::Game.Net.CarLane>(lane) || !em.HasComponent<PrefabRef>(lane)) continue;
                        float kmh = em.GetComponentData<global::Game.Net.CarLane>(lane).m_SpeedLimit * 3.6f;
                        var lp = em.GetComponentData<PrefabRef>(lane).m_Prefab;
                        string cost = "?";
                        if (em.HasComponent<NetLaneData>(lp))
                        {
                            var pf = em.GetComponentData<NetLaneData>(lp).m_PathfindPrefab;
                            if (pf != Entity.Null && em.HasComponent<PathfindCarData>(pf))
                            {
                                var c = em.GetComponentData<PathfindCarData>(pf).m_DrivingCost.m_Value;
                                cost = $"{c.x:0.##}/{c.y:0.##}/{c.z:0.##}/{c.w:0.##}";
                            }
                        }
                        string key = $"{kmh:0} km/h, cost {cost}";
                        // a slow copy: name the vanilla lane it was copied from and that lane's own cost, for the comparison
                        string laneName = prefabs.TryGetPrefab<PrefabBase>(lp, out var lpb) && lpb != null ? lpb.name : "";
                        int open = laneName.LastIndexOf('('), close = laneName.LastIndexOf(')');
                        if (laneName.Contains(" :: ") && open >= 0 && close > open)
                            key += $" (vanilla {VanillaLaneCost(prefabs, laneName.Substring(open + 1, close - open - 1))})";
                        lanes[key] = (lanes.TryGetValue(key, out var m) ? m : 0) + 1;
                    }
                }
                var g = new JObject { ["group"] = kv.Key, ["objects"] = objects, ["hiddenObjects"] = hiddenObjects, ["areas"] = areas, ["hiddenAreas"] = hiddenAreas };
                var jn = new JObject(); foreach (var x in nets) jn[x.Key] = x.Value; g["paths"] = jn;
                var jl = new JObject(); foreach (var x in lanes) jl[x.Key] = x.Value; g["carLanes"] = jl;
                groups.Add(g);
                var netText = new List<string>(); foreach (var x in nets) netText.Add($"{x.Value} x {x.Key}");
                var laneText = new List<string>(); foreach (var x in lanes) laneText.Add($"{x.Value} x {x.Key}");
                Mod.Log.Info($"Placemaker lab: drive lanes ({label}): group {kv.Key}: {hiddenObjects} of {objects} object(s) and {hiddenAreas} of {areas} area(s) hidden; paths {string.Join(", ", netText)}; car lanes {string.Join(", ", laneText)} (cost = time/behaviour/money/comfort).");
            }
            rec["groups"] = groups;
            if (byGroup.Count == 0) Mod.Log.Info($"Placemaker lab: drive lanes ({label}): no builds.");
        }
    }
}
