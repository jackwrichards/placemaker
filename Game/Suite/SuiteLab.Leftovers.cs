// Game runner — what a removed build leaves behind (GPL-3.0).
//   {"do":"lab","op":"leftovers","label":"before","baseline":true}
//       counts everything in the scenario's area (its outline's bounds plus 12 m): objects, buildings, trees, net edges and
//       nodes, lots and surfaces, by prefab; hidden or overridden pieces; and, city-wide, our own prefabs (owner lots, zone
//       blocks, companions, surface copies), tagged pieces, group records, decals drawn by code. Kept as the baseline.
//   {"do":"lab","op":"leftovers","label":"after the bulldozer"}
//       the same count, and what differs from the baseline, prefab by prefab ("leftovers: none" when nothing does).

using System;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Decals;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private readonly Dictionary<string, SortedDictionary<string, int>> _leftoverBaselines = new Dictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);

        private void LabLeftovers(ScenarioState st, JObject step, JObject rec)
        {
            string label = (string)step["label"] ?? "";
            string key = st.S?.Id ?? "";
            var counts = LeftoverCensus(st);
            var all = new JObject();
            foreach (var kv in counts) all[kv.Key] = kv.Value;
            rec["census"] = all;
            if ((bool?)step["baseline"] == true || !_leftoverBaselines.ContainsKey(key))
            {
                _leftoverBaselines[key] = counts;
                Mod.Log.Info($"Placemaker lab: leftovers ({label}): baseline taken, {counts.Count} kind(s): "
                    + string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key}")) + ".");
                return;
            }
            var basis = _leftoverBaselines[key];
            var diff = new List<string>();
            var jd = new JObject();
            foreach (var k in basis.Keys.Union(counts.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                int a = basis.TryGetValue(k, out var x) ? x : 0, b = counts.TryGetValue(k, out var y) ? y : 0;
                if (a == b) continue;
                diff.Add($"{k} {(b > a ? "+" : "")}{b - a}");
                jd[k] = b - a;
            }
            rec["diff"] = jd;
            rec["leftovers"] = diff.Count;
            Mod.Log.Info($"Placemaker lab: leftovers ({label}), group {st.Group}: " + (diff.Count == 0 ? "none" : string.Join("; ", diff)) + ".");
        }

        private SortedDictionary<string, int> LeftoverCensus(ScenarioState st)
        {
            var em = _world.EntityManager;
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Add(string k) => counts[k] = (counts.TryGetValue(k, out var n) ? n : 0) + 1;
            float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
            if (st.Ring != null) foreach (var p in st.Ring) { min = math.min(min, p); max = math.max(max, p); }
            min -= 12f; max += 12f;
            bool In(float3 p) => st.Ring != null && p.x >= min.x && p.x <= max.x && p.z >= min.y && p.z <= max.y;

            var desc = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Prefabs.PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            };
            using (var q = em.CreateEntityQuery(desc))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    string name = _lookup.PrefabName(e) ?? "?";
                    bool own = name.StartsWith("Placemaker", StringComparison.Ordinal);
                    float3 at; string kind;
                    if (em.HasComponent<global::Game.Net.Edge>(e) && em.HasComponent<global::Game.Net.Curve>(e))
                    { kind = "edge"; at = global::Colossal.Mathematics.MathUtils.Position(em.GetComponentData<global::Game.Net.Curve>(e).m_Bezier, 0.5f); }
                    else if (em.HasComponent<global::Game.Net.Node>(e)) { kind = "node"; at = em.GetComponentData<global::Game.Net.Node>(e).m_Position; }
                    else if (em.HasComponent<global::Game.Areas.Area>(e) && em.HasBuffer<global::Game.Areas.Node>(e))
                    {
                        var nodes = em.GetBuffer<global::Game.Areas.Node>(e, true);
                        if (nodes.Length == 0) continue;
                        float3 c = default; for (int i = 0; i < nodes.Length; i++) c += nodes[i].m_Position; at = c / nodes.Length;
                        kind = em.HasComponent<global::Game.Areas.Lot>(e) ? "lot" : em.HasComponent<global::Game.Areas.Surface>(e) ? "surface" : "area";
                    }
                    else if (em.HasComponent<global::Game.Objects.Transform>(e))
                    {
                        at = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                        kind = em.HasComponent<global::Game.Buildings.Building>(e) ? "building" : em.HasComponent<global::Game.Objects.Tree>(e) ? "tree"
                            : em.HasComponent<global::Game.Vehicles.Vehicle>(e) || em.HasComponent<global::Game.Creatures.Creature>(e) ? null : "object";
                        if (kind == null) continue;   // traffic and people come and go
                    }
                    else if (own) { Add("city-wide: " + name + " (no position)"); continue; }
                    else continue;
                    if (own && (kind == "lot" || kind == "building" || kind == "surface")) Add("city-wide: own " + kind + " " + name);
                    if (!In(at)) continue;
                    Add(kind + " " + name);
                    if (em.HasComponent<global::Game.Tools.Hidden>(e)) Add("hidden " + kind);
                    if (em.HasComponent<Overridden>(e)) Add("overridden " + kind);
                }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.Exclude<Deleted>()))
            { int n = q.CalculateEntityCount(); if (n > 0) counts["city-wide: tagged pieces"] = n; }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerGroup>(), ComponentType.Exclude<Deleted>()))
            { int n = q.CalculateEntityCount(); if (n > 0) counts["city-wide: group records"] = n; }
            if (SharedDecals.InstanceCount > 0) counts["city-wide: code decals"] = SharedDecals.InstanceCount;
            return counts;
        }
    }
}
