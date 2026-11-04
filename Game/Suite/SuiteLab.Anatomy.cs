// Game runner lab (exploration): what a grown house is made of, so neighbour stitching knows what it can edit.
//   {"do":"lab","op":"anatomy"}  for every house in the scenario's land: its prefab, lot, and every part the game made for
//   it — surfaces (sub-areas), props (sub-objects), fence/hedge lines (sub-lanes with a mesh), paths (sub-nets) — with
//   names, counts and where they sit on the lot. One summary line per part kind across all houses, three houses in full.

using System.Collections.Generic;
using System.Linq;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private void LabAnatomy(ScenarioState st, JObject rec)
        {
            var b = st.S.Block;
            var em = _world.EntityManager;
            var houses = new List<Entity>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    if (p.x >= b.X0 && p.x <= b.X1 && p.z >= b.Z0 && p.z <= b.Z1) houses.Add(e);
                }
            var kinds = new Dictionary<string, int>();
            void Count(string k) => kinds[k] = (kinds.TryGetValue(k, out var n) ? n : 0) + 1;
            var detail = new JArray();
            int underConstruction = 0;
            foreach (var h in houses)
            {
                var prefab = em.GetComponentData<PrefabRef>(h).m_Prefab;
                var t = em.GetComponentData<global::Game.Objects.Transform>(h);
                var inv = global::Game.Objects.ObjectUtils.InverseTransform(t);
                var lot = em.HasComponent<BuildingData>(prefab) ? em.GetComponentData<BuildingData>(prefab).m_LotSize : int2.zero;
                bool building = em.HasComponent<global::Game.Objects.UnderConstruction>(h);
                if (building) underConstruction++;
                var parts = new JArray();
                JArray Local(float3 w) { var l = global::Game.Objects.ObjectUtils.WorldToLocal(inv, w); return new JArray(System.Math.Round(l.x, 1), System.Math.Round(l.z, 1)); }
                if (em.HasBuffer<global::Game.Areas.SubArea>(h))
                    foreach (var sa in em.GetBuffer<global::Game.Areas.SubArea>(h, true))
                    {
                        var a = sa.m_Area;
                        if (!em.Exists(a)) continue;
                        string name = _fixture.NameOf(em.GetComponentData<PrefabRef>(a).m_Prefab);
                        string kind = em.HasComponent<global::Game.Areas.Surface>(a) ? "surface" : em.HasComponent<global::Game.Areas.Lot>(a) ? "lot area" : "area";
                        Count(kind + ": " + name);
                        int n = em.HasBuffer<global::Game.Areas.Node>(a) ? em.GetBuffer<global::Game.Areas.Node>(a, true).Length : 0;
                        parts.Add(new JObject { ["kind"] = kind, ["name"] = name, ["nodes"] = n });
                    }
                if (em.HasBuffer<global::Game.Objects.SubObject>(h))
                    foreach (var so in em.GetBuffer<global::Game.Objects.SubObject>(h, true))
                    {
                        var o = so.m_SubObject;
                        if (!em.Exists(o) || !em.HasComponent<PrefabRef>(o)) continue;
                        string name = _fixture.NameOf(em.GetComponentData<PrefabRef>(o).m_Prefab);
                        Count("prop: " + name);
                        var at = em.HasComponent<global::Game.Objects.Transform>(o) ? Local(em.GetComponentData<global::Game.Objects.Transform>(o).m_Position) : null;
                        parts.Add(new JObject { ["kind"] = "prop", ["name"] = name, ["at"] = at });
                    }
                if (em.HasBuffer<global::Game.Net.SubLane>(h))
                    foreach (var sl in em.GetBuffer<global::Game.Net.SubLane>(h, true))
                    {
                        var l = sl.m_SubLane;
                        if (!em.Exists(l) || !em.HasComponent<PrefabRef>(l)) continue;
                        var lp = em.GetComponentData<PrefabRef>(l).m_Prefab;
                        bool drawn = em.HasComponent<NetLaneGeometryData>(lp);
                        if (!drawn) { Count("lane (not drawn)"); continue; }
                        string name = _fixture.NameOf(lp);
                        Count("line: " + name);
                        var c = em.HasComponent<global::Game.Net.Curve>(l) ? em.GetComponentData<global::Game.Net.Curve>(l) : default;
                        parts.Add(new JObject { ["kind"] = "line", ["name"] = name, ["from"] = Local(c.m_Bezier.a), ["to"] = Local(c.m_Bezier.d), ["length"] = System.Math.Round(c.m_Length, 1) });
                    }
                if (em.HasBuffer<global::Game.Net.SubNet>(h))
                    foreach (var sn in em.GetBuffer<global::Game.Net.SubNet>(h, true))
                    {
                        var n = sn.m_SubNet;
                        if (!em.Exists(n) || !em.HasComponent<PrefabRef>(n)) continue;
                        Count("net: " + _fixture.NameOf(em.GetComponentData<PrefabRef>(n).m_Prefab));
                    }
                if (detail.Count < 3 && !building)
                    detail.Add(new JObject { ["house"] = _fixture.NameOf(prefab), ["lot"] = new JArray(lot.x, lot.y), ["parts"] = parts });
            }
            rec["houses"] = houses.Count;
            rec["underConstruction"] = underConstruction;
            rec["partKinds"] = new JArray(kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value}"));
            rec["examples"] = detail;
            Mod.Log.Info($"Placemaker lab: anatomy of {houses.Count} houses ({underConstruction} under construction): {string.Join("; ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} x{k.Value}"))}");
            foreach (var d in detail) Mod.Log.Info("Placemaker lab: anatomy example " + d.ToString(Newtonsoft.Json.Formatting.None));
        }
    }
}
