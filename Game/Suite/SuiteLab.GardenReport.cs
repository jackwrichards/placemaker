// Game runner lab: every outgrown garden in the city measured (GardenReport): its edges (shared ones counted by each garden),
// its corner angles, its flags, and the breakdown in all and by place.
//   {"do":"lab","op":"gardenReport","label":"after","max":8,"places":[{"name":"grid","at":[x,z],"size":520}, ...]}
// writes gardens-<label>.json (every garden) and gardens-<label>.txt (the breakdown) to the run's folder; "places" (world
// x, z and a square's size) name the parts of town the breakdown groups by; "max": more edges than this is flagged.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Neighbours;
using Placemaker.Game.Neighbours;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private IEnumerator LabGardenReport(ScenarioState st, JObject step, JObject rec)
        {
            yield return Frames(10);
            var em = _world.EntityManager;
            var places = new List<(string Name, float2 Lo, float2 Hi)>();
            if (step["places"] is JArray ps)
                foreach (var p in ps)
                {
                    // ("rel": true, from the scenario's land's middle)
                    var at = new float2((float)p["at"][0], (float)p["at"][1]) + ((bool?)step["rel"] == true ? st.S.Block.Center : float2.zero);
                    float half = ((float?)p["size"] ?? 500f) * 0.5f;
                    places.Add(((string)p["name"], at - half, at + half));
                }
            var input = new List<GardenReport.Input>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PolishedLot>(), ComponentType.ReadOnly<PolishGarden>(), ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var entities = q.ToEntityArray(Allocator.Temp))
                foreach (var e in entities)
                {
                    if (!em.GetComponentData<PolishedLot>(e).Yes) continue;
                    var buf = em.GetBuffer<PolishGarden>(e, true);
                    if (buf.Length < 3) continue;
                    var ring = new float2[buf.Length];
                    for (int k = 0; k < ring.Length; k++) ring[k] = buf[k].Position;
                    var t = em.GetComponentData<Transform>(e);
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var half = em.HasComponent<BuildingData>(prefab) ? (float2)em.GetComponentData<BuildingData>(prefab).m_LotSize * 4f : float2.zero;
                    var fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
                    string place = null;
                    foreach (var (name, lo, hi) in places)
                        if (t.m_Position.x >= lo.x && t.m_Position.z >= lo.y && t.m_Position.x <= hi.x && t.m_Position.z <= hi.y) { place = name; break; }
                    input.Add(new GardenReport.Input
                    {
                        Id = ((long)e.Index << 32) | (uint)e.Version, Position = t.m_Position.xz, Forward = math.normalizesafe(fwd.xz, new float2(0f, 1f)),
                        HalfLot = half, Ring = ring, Place = place,
                    });
                }
            // the roads (their surfaces) in the places, for the land Outgrow should cover
            var roads = new List<float2[]>();
            var pieces = new List<(float2[] Ring, bool Round)>();
            if (places.Count > 0)
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                    {
                        var cb = MathUtils.Bounds(em.GetComponentData<Curve>(e).m_Bezier);
                        bool near = false;
                        foreach (var (_, lo, hi) in places) if (cb.max.x >= lo.x && cb.min.x <= hi.x && cb.max.z >= lo.y && cb.min.z <= hi.y) { near = true; break; }
                        if (!near || !em.HasComponent<Road>(e) || em.HasComponent<Owner>(e) || !em.HasComponent<Composition>(e)) continue;
                        pieces.Clear();
                        RoadSurfaces.Pieces(em, e, pieces);
                        foreach (var (ring, _) in pieces) roads.Add(ring);
                    }
            var o = new GardenReport.Options { MaxEdges = (int?)step["max"] ?? 8 };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var gardens = GardenReport.Measure(input, o);
            // (the lots of every outgrown building in the places)
            var lots = new List<float2[]>();
            foreach (var g in input) if (math.any(g.HalfLot > 0f)) { var r = new float2(g.Forward.y, -g.Forward.x); lots.Add(new[] { g.Position - r * g.HalfLot.x - g.Forward * g.HalfLot.y, g.Position + r * g.HalfLot.x - g.Forward * g.HalfLot.y, g.Position + r * g.HalfLot.x + g.Forward * g.HalfLot.y, g.Position - r * g.HalfLot.x + g.Forward * g.HalfLot.y }); }
            var covers = GardenReport.Coverage(places.ConvertAll(p => new GardenReport.Place { Name = p.Name, Lo = p.Lo, Hi = p.Hi }), roads, input.ConvertAll(g => g.Ring), lots);
            var text = GardenReport.CoverText(covers) + GardenReport.Text(gardens, o);
            string label = (string)step["label"] ?? "now";
            try
            {
                File.WriteAllText(Path.Combine(_out, $"gardens-{label}.json"), GardenReport.Json(gardens));
                File.WriteAllText(Path.Combine(_out, $"gardens-{label}.txt"), text);
                if (covers.Count > 0) File.WriteAllText(Path.Combine(_out, $"coverage-{label}.json"), GardenReport.CoverJson(covers));
                // (the road surfaces it measured from: the offline score, OutgrowBench --bootcamp, measures from the same)
                // (the places as measured, in world terms: the offline score reads them)
                File.WriteAllText(Path.Combine(_out, $"places-{label}.json"), "[" + string.Join(",", places.ConvertAll(p => $"{{\"name\":\"{p.Name}\",\"at\":[{((p.Lo.x + p.Hi.x) * 0.5f).ToString(System.Globalization.CultureInfo.InvariantCulture)},{((p.Lo.y + p.Hi.y) * 0.5f).ToString(System.Globalization.CultureInfo.InvariantCulture)}],\"size\":{(p.Hi.x - p.Lo.x).ToString(System.Globalization.CultureInfo.InvariantCulture)}}}")) + "]");
                if (roads.Count > 0) File.WriteAllText(Path.Combine(_out, $"roads-{label}.json"), "[" + string.Join(",", roads.ConvertAll(r => "[" + string.Join(",", Array.ConvertAll(r, p => $"[{p.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{p.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}]")) + "]")) + "]");
            }
            catch (Exception ex) { rec["error"] = "lab: garden report not written: " + ex.Message; }
            rec["gardens"] = gardens.Count;
            rec["ms"] = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
            rec["file"] = Path.Combine(_out, $"gardens-{label}.txt");
            foreach (var line in text.Split('\n')) if (line.Length > 0) Mod.Log.Info("Placemaker lab: gardens (" + label + ") " + line);
        }
    }
}
