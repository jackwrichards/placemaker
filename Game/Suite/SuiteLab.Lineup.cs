// Game runner lab: a line-up of the game's own objects, to look at them before a type uses them.
//   {"do":"lab","op":"lineup","match":"^Rock\\d+$","exclude":"Pillar","max":24,"perRow":6,"gap":3,"at":[0,0]}
//   {"do":"lab","op":"lineup","names":["PileSandLarge01","Boulder03"],"sink":0.5,"turn":40,"roll":0}   (a name may repeat)
// Every object whose prefab name matches (sorted by name; buildings left out) stands in rows along +x, rows along +z, a
// flag pole 4 m before the first of each row. The step's record and Placemaker.log list each one's name, size and spot,
// and every name that matched (also past "max"; "max":0 only lists them, all of them). The camera's "area" target frames the line-up.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Common;
using Game.Objects;
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
        private IEnumerator LabLineup(ScenarioState st, JObject step, JObject rec)
        {
            var em = _world.EntityManager;
            var prefabs = _world.GetOrCreateSystemManaged<PrefabSystem>();
            var names = (step["names"] as JArray)?.Select(t => (string)t).ToList();
            var match = string.IsNullOrEmpty((string)step["match"]) ? null : new Regex((string)step["match"], RegexOptions.IgnoreCase);
            var exclude = string.IsNullOrEmpty((string)step["exclude"]) ? null : new Regex((string)step["exclude"], RegexOptions.IgnoreCase);
            int max = (int?)step["max"] ?? 24, perRow = (int?)step["perRow"] ?? 6;
            float gap = (float?)step["gap"] ?? 3f;
            // Optional: lowered into the ground by "sink" m, each turned by i x "turn" degrees, tipped by "roll" degrees.
            float sink = (float?)step["sink"] ?? 0f, turn = (float?)step["turn"] ?? 0f, roll = (float?)step["roll"] ?? 0f;
            // "underground": the sunk item also gets the game's negative Elevation (its collision mask is then Underground only);
            // "pair": a prop of this name stands on each item's centre, to see whether the two hide one another.
            bool underground = (bool?)step["underground"] ?? false;
            string pairName = (string)step["pair"];

            var found = new Dictionary<string, (Entity e, float3 size)>();
            Entity flag = Entity.Null, pair = Entity.Null;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ObjectGeometryData>(), ComponentType.Exclude<BuildingData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (!prefabs.TryGetPrefab<PrefabBase>(e, out var p) || p == null || string.IsNullOrEmpty(p.name)) continue;
                    if (p.name == "FlagPole01") flag = e;
                    if (pairName != null && p.name == pairName) pair = e;
                    bool hit = names != null ? names.Contains(p.name) : match != null && match.IsMatch(p.name);
                    if (!hit || (exclude != null && exclude.IsMatch(p.name)) || found.ContainsKey(p.name)) continue;
                    found[p.name] = (e, em.GetComponentData<ObjectGeometryData>(e).m_Size);
                }
            var order = names != null ? names.Where(found.ContainsKey).ToList() : found.Keys.OrderBy(n => n, System.StringComparer.Ordinal).ToList();
            rec["matched"] = order.Count;
            rec["names"] = new JArray(order.Take(max <= 0 ? 5000 : 400));
            if (names != null) rec["missing"] = new JArray(names.Where(n => !found.ContainsKey(n)));
            if (max <= 0)
            {
                // Names only: every match in the log, 40 to a line, nothing placed.
                for (int k = 0; k < order.Count; k += 40) Mod.Log.Info($"Placemaker lab: lineup names {k}: {string.Join(" | ", order.Skip(k).Take(40))}");
                yield break;
            }
            var pick = order.Take(max).ToList();
            if (pick.Count == 0) { rec["error"] = "lab lineup: no object matched"; yield break; }

            // Rows along +x, spaced by the real sizes; rows along +z, as deep as the deepest in the row.
            var origin = Pt(step["at"], st.S.Block, false);
            var spots = new List<(string name, Entity e, float3 size, float2 at)>();
            var flags = new List<float2>();
            float z = 0f, width = 0f;
            for (int r = 0; r * perRow < pick.Count; r++)
            {
                var row = pick.Skip(r * perRow).Take(perRow).ToList();
                float depth = row.Max(n => math.max(1f, found[n].size.z));
                float x = 0f;
                flags.Add(new float2(-4f, z + depth * 0.5f));
                foreach (var n in row)
                {
                    var s = found[n].size;
                    float w = math.max(1f, s.x);
                    spots.Add((n, found[n].e, s, new float2(x + w * 0.5f, z + depth * 0.5f)));
                    x += w + gap;
                }
                width = math.max(width, x);
                z += depth + gap;
            }
            var shift = origin - new float2(width, z) * 0.5f;

            var items = new JArray();
            int seed = 7100;
            yield return ApplyWithFixtureTool(t =>
            {
                int made = 0;
                int k = 0;
                void Put(Entity prefab, float2 at, bool item)
                {
                    var p = new float3(at.x, _fixture.H(at.x, at.y) - (item ? sink : 0f), at.y);
                    var rot = item ? math.mul(quaternion.RotateY(math.radians(turn * k++)), quaternion.RotateZ(math.radians(roll))) : quaternion.identity;
                    var def = t.NewDefinition();
                    em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = seed++ });
                    em.AddComponent<Updated>(def);
                    em.AddComponentData(def, new ObjectDefinition
                    {
                        m_Position = p, m_LocalPosition = p, m_Rotation = rot, m_LocalRotation = rot, m_Scale = new float3(1f),
                        m_Intensity = 1f, m_Age = 0.5f, m_Probability = 100, m_PrefabSubIndex = -1, m_ParentMesh = -1, m_Elevation = item && underground ? -math.max(0.01f, sink) : 0f,
                    });
                    made++;
                }
                foreach (var s in spots) { Put(s.e, s.at + shift, true); if (pair != Entity.Null) Put(pair, s.at + shift, false); }
                if (flag != Entity.Null) foreach (var f in flags) Put(flag, f + shift, false);
                return made;
            }, "lab lineup");
            yield return Frames(30);

            int i = 0;
            foreach (var s in spots)
            {
                var at = s.at + shift;
                items.Add(new JObject { ["i"] = i, ["name"] = s.name, ["size"] = new JArray(R(s.size.x), R(s.size.y), R(s.size.z)), ["row"] = i / perRow, ["col"] = i % perRow });
                Mod.Log.Info($"Placemaker lab: lineup {i} (row {i / perRow}, col {i % perRow}) {s.name} size {s.size.x:F1} x {s.size.y:F1} x {s.size.z:F1} at ({at.x:F0}, {at.y:F0})");
                i++;
            }
            rec["items"] = items;
            Mod.Log.Info($"Placemaker lab: lineup matched {order.Count}: {string.Join(", ", order.Take(400))}");
            var lo = shift - new float2(6f, 2f);
            var hi = shift + new float2(width + 2f, z + 2f);
            st.Ring = new[] { lo, new float2(hi.x, lo.y), hi, new float2(lo.x, hi.y) };
            Event("build");
        }

        private static double R(float v) => System.Math.Round(v, 1);
    }
}
