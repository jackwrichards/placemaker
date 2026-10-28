// Game runner — footpaths meeting city streets, and picking a part of an ungrouped build (GPL-3.0).
//   {"do":"lab","op":"streetJoins","label":"..."}
//       every footpath end of the scenario's build that lies by a city road: joined at the kerb by the game (a middle
//       connection: the path's dead-end node in the road edge's ConnectedNode buffer, no node on the road), split into the
//       road at a node (and whether that node draws a crosswalk across the road), or a dead end the game did not join.
//   {"do":"lab","op":"pickPart","label":"..."}
//       with Placemaker closed (the game's own default tool), clicks one of the build's trees or props like a person and
//       reads what the info panel selected: the part itself, the build's owner lot (the whole build), or something else.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private void LabStreetJoins(ScenarioState st, JObject step, JObject rec)
        {
            var em = _world.EntityManager;
            string label = (string)step["label"] ?? "";
            if (st.Group == 0) { rec["error"] = "no build"; return; }
            var members = _lookup.MemberSet(st.Group);
            bool IsRoad(Entity e) => em.HasComponent<PrefabRef>(e) && em.HasComponent<RoadData>(em.GetComponentData<PrefabRef>(e).m_Prefab);
            bool IsPath(Entity e) => em.HasComponent<PrefabRef>(e) && em.HasComponent<PathwayData>(em.GetComponentData<PrefabRef>(e).m_Prefab);
            // City road edges around the build, and the nodes each joins in its middle (ConnectedNode: LocalConnect).
            var roads = new List<(Entity e, Bezier b, float half)>();
            var middle = new Dictionary<Entity, Entity>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<global::Game.Net.Road>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var edges = q.ToEntityArray(Allocator.Temp))
                foreach (var e in edges)
                {
                    var c = em.GetComponentData<Curve>(e).m_Bezier;
                    float half = 6f;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (em.HasComponent<NetGeometryData>(prefab)) half = em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth * 0.5f;
                    roads.Add((e, new Bezier(c.a, c.b, c.c, c.d), half));
                    if (!em.HasBuffer<ConnectedNode>(e)) continue;
                    var cn = em.GetBuffer<ConnectedNode>(e, true);
                    for (int i = 0; i < cn.Length; i++) middle[cn[i].m_Node] = e;
                }
            var ends = new JArray();
            int kerb = 0, split = 0, crossings = 0, free = 0;
            var seen = new HashSet<Entity>();
            foreach (var m in members)
            {
                if (!em.Exists(m) || !em.HasComponent<Edge>(m) || !IsPath(m)) continue;
                var edge = em.GetComponentData<Edge>(m);
                foreach (var node in new[] { edge.m_Start, edge.m_End })
                {
                    if (!seen.Add(node) || !em.Exists(node) || !em.HasComponent<global::Game.Net.Node>(node)) continue;
                    var at = em.GetComponentData<global::Game.Net.Node>(node).m_Position;
                    // nearest city road: distance from its edge (full width)
                    float gap = float.MaxValue; Entity near = Entity.Null;
                    foreach (var r in roads)
                    {
                        float d = r.b.DistanceXZ(at.xz) - r.half;
                        if (d < gap) { gap = d; near = r.e; }
                    }
                    var connected = new List<Entity>();
                    if (em.HasBuffer<ConnectedEdge>(node))
                    {
                        var ce = em.GetBuffer<ConnectedEdge>(node, true);
                        for (int i = 0; i < ce.Length; i++) connected.Add(ce[i].m_Edge);
                    }
                    // ConnectedEdge also lists edges joined in their middle (a LocalConnect): a split is a road edge that starts or ends here
                    var roadEdges = connected.Where(e => em.Exists(e) && IsRoad(e) && (em.GetComponentData<Edge>(e).m_Start == node || em.GetComponentData<Edge>(e).m_End == node)).ToList();
                    bool middleJoin = middle.ContainsKey(node) || connected.Any(e => em.Exists(e) && IsRoad(e) && !roadEdges.Contains(e));
                    string how;
                    bool crosswalk = false;
                    if (roadEdges.Count > 0)
                    {
                        how = "split";
                        split++;
                        foreach (var re in roadEdges)
                        {
                            if (!em.HasComponent<Composition>(re)) continue;
                            var re2 = em.GetComponentData<Edge>(re);
                            var comp = em.GetComponentData<Composition>(re);
                            var nc = re2.m_Start == node ? comp.m_StartNode : comp.m_EndNode;
                            if (nc != Entity.Null && em.HasComponent<NetCompositionData>(nc)
                                && (em.GetComponentData<NetCompositionData>(nc).m_Flags.m_General & CompositionFlags.General.Crosswalk) != 0) crosswalk = true;
                        }
                        if (crosswalk) crossings++;
                    }
                    else if (middleJoin) { how = "kerb"; kerb++; }
                    else if (connected.Count(e => em.Exists(e) && IsPath(e)) == 1 && gap < 8f) { how = "free"; free++; }
                    else continue;   // a node inside the build
                    ends.Add(new JObject
                    {
                        ["at"] = new JArray(Math.Round(at.x, 1), Math.Round(at.z, 1)),
                        ["how"] = how,
                        ["crosswalk"] = crosswalk,
                        ["fromRoadEdge"] = Math.Round(gap, 2),
                        ["road"] = near != Entity.Null ? _lookup.PrefabName(near) : "",
                    });
                }
            }
            rec["ends"] = ends;
            rec["kerb"] = kerb;
            rec["split"] = split;
            rec["crosswalks"] = crossings;
            rec["free"] = free;
            Mod.Log.Info($"Placemaker lab: street joins ({label}): {kerb} footpath end(s) joined at the kerb by the game, {split} split into a road ({crossings} with a crosswalk), "
                + $"{free} dead end(s) by a street not joined. " + string.Join("; ", ends.Select(x => $"{x["how"]} at ({x["at"][0]}, {x["at"][1]}) {x["fromRoadEdge"]} m off the road edge{((bool)x["crosswalk"] ? ", crosswalk" : "")}")));
        }

        private IEnumerator LabPickPart(ScenarioState st, JObject step, JObject rec)
        {
            var em = _world.EntityManager;
            string label = (string)step["label"] ?? "";
            if (st.Group == 0) { rec["error"] = "no build"; yield break; }
            var members = _lookup.MemberSet(st.Group);
            // a tree that shares the build's carrier owner (the usual case), else any tree or top-level prop of the build
            Entity part = members.Where(m => em.Exists(m) && em.HasComponent<global::Game.Objects.Tree>(m) && em.HasComponent<Owner>(m)).FirstOrDefault();
            if (part == Entity.Null) part = members.Where(m => em.Exists(m) && em.HasComponent<global::Game.Objects.Static>(m) && em.HasComponent<global::Game.Objects.Transform>(m)).FirstOrDefault();
            if (part == Entity.Null) { rec["error"] = "the build has no tree or prop"; yield break; }
            Entity lot = members.FirstOrDefault(m => em.Exists(m) && em.HasComponent<PlacemakerMember>(m) && em.GetComponentData<PlacemakerMember>(m).Role == MemberRole.OwnerArea);
            rec["part"] = _lookup.KindOf(part) + " " + _lookup.PrefabName(part);
            // Placemaker closed: the game's default tool sets the raycast (its type mask: static objects, areas, nets…).
            yield return CloseTool();
            yield return Frames(10);
            var trs = _world.GetExistingSystemManaged<ToolRaycastSystem>();
            var tools = _world.GetExistingSystemManaged<ToolSystem>();
            rec["tool"] = tools?.activeTool?.GetType().Name ?? "";
            rec["mask"] = trs.typeMask.ToString();
            // What the game hands the tools for a click on the tree: the top of its owner chain (Objects.RaycastJobs.ValidateResult).
            Entity top = part;
            for (int i = 0; i < 4 && em.HasComponent<Owner>(top); i++) top = em.GetComponentData<Owner>(top).m_Owner;
            string What(bool hit, RaycastResult r)
            {
                if (!hit || r.m_Owner == Entity.Null) return "nothing";
                if (r.m_Owner == part) return "the part";
                if (r.m_Owner == lot) return "the whole build (its owner lot)";
                if (em.HasComponent<PlacemakerMember>(r.m_Owner) && em.GetComponentData<PlacemakerMember>(r.m_Owner).Role == MemberRole.Carrier) return "the carrier (every prop of the build)";
                return _lookup.KindOf(r.m_Owner) + " " + _lookup.PrefabName(r.m_Owner);
            }
            bool h1 = true;
            var r1 = new RaycastResult { m_Owner = top, m_Hit = { m_HitEntity = part, m_Position = em.GetComponentData<global::Game.Objects.Transform>(part).m_Position } };
            Placemaker.Game.Groups.Patches.RaycastLiftPatch.Apply(trs, ref h1, ref r1);
            rec["clickPart"] = What(h1, r1);
            // what the bulldozer then deletes: it sends a static object with an owner on to that owner (BulldozeToolSystem, MainElements)
            var dozed = h1 ? r1.m_Owner : Entity.Null;
            while (dozed != Entity.Null && em.HasComponent<global::Game.Objects.Static>(dozed) && em.HasComponent<Owner>(dozed)) dozed = em.GetComponentData<Owner>(dozed).m_Owner;
            rec["bulldozerTakes"] = dozed == Entity.Null ? "nothing" : dozed == part ? "the part" : What(true, new RaycastResult { m_Owner = dozed });
            rec["gameHands"] = top == part ? "the part" : _lookup.KindOf(top) + " (its owner)";
            string ground = "no lot";
            if (lot != Entity.Null)
            {
                bool h2 = true;
                var r2 = new RaycastResult { m_Owner = lot, m_Hit = { m_HitEntity = lot } };
                Placemaker.Game.Groups.Patches.RaycastLiftPatch.Apply(trs, ref h2, ref r2);
                ground = What(h2, r2);
            }
            rec["clickGround"] = ground;
            rec["ungrouped"] = Services.Groups?.IsUngrouped(st.Group) ?? false;
            Mod.Log.Info($"Placemaker lab: pick a part ({label}): group {st.Group} (ungrouped: {rec["ungrouped"]}), the game's {rec["tool"]} (mask {rec["mask"]}): "
                + $"a click on {rec["part"]} (the game hands over {rec["gameHands"]}) picks {rec["clickPart"]}, the bulldozer there takes {rec["bulldozerTakes"]}; a click on the ground between its parts picks {ground}.");

            if ((bool?)step["bulldoze"] ?? false)
            {
                // The bulldozer on that part: the delete definition it applies for what it takes.
                var target = dozed;
                int before = Services.Groups is GroupRegistrySystem reg ? reg.CountMembers(st.Group, false) : -1;
                if (h1 && target != Entity.Null && em.HasComponent<global::Game.Objects.Transform>(target))
                {
                    yield return ApplyWithFixtureTool(t =>
                    {
                        var def = t.NewDefinition();
                        em.AddComponentData(def, new CreationDefinition { m_Original = target, m_Flags = CreationFlags.Delete });
                        em.AddComponent<Updated>(def);
                        var tr = em.GetComponentData<global::Game.Objects.Transform>(target);
                        em.AddComponentData(def, new ObjectDefinition { m_Position = tr.m_Position, m_Rotation = tr.m_Rotation, m_LocalPosition = tr.m_Position, m_LocalRotation = tr.m_Rotation, m_ParentMesh = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_PrefabSubIndex = -1 });
                        return 1;
                    }, "bulldoze the picked part");
                    yield return Frames(30);
                }
                bool gone = !em.Exists(part) || em.HasComponent<Deleted>(part);
                bool groupLeft = Services.Groups?.Get(st.Group) != null;
                int after = Services.Groups is GroupRegistrySystem reg2 && groupLeft ? reg2.CountMembers(st.Group, false) : 0;
                rec["bulldozed"] = new JObject { ["partGone"] = gone, ["buildStays"] = groupLeft, ["membersBefore"] = before, ["membersAfter"] = after };
                Mod.Log.Info($"Placemaker lab: bulldoze the picked part ({label}): the part {(gone ? "is gone" : "is still there")}, the build {(groupLeft ? "stays" : "is gone")}, members {before} -> {after}.");
                yield return Frames(5);
            }
            yield return OpenTool();
        }

        /// <summary>Info panel: Ungroup (then OK), the way a person does after clicking the build; it is dissolved for good.</summary>
        private IEnumerator StepInfoUngroup(ScenarioState st, JObject step, JObject rec)
        {
            if (!st.Built || st.Group == 0) { rec["error"] = "nothing built"; yield break; }
            int g = st.Group;
            yield return CloseTool();
            var em = _world.EntityManager;
            var members = _lookup.MemberSet(g);
            Entity owner = members.FirstOrDefault(m => em.Exists(m) && em.HasComponent<PlacemakerMember>(m) && em.GetComponentData<PlacemakerMember>(m).Role == MemberRole.OwnerArea);
            if (owner == Entity.Null) owner = members.FirstOrDefault(m => em.Exists(m) && !em.HasComponent<Deleted>(m));
            var sel = _world.GetExistingSystemManaged<global::Game.UI.InGame.SelectedInfoUISystem>();
            try { sel?.SetSelection(owner); } catch (Exception e) { rec["selectError"] = e.Message; }
            yield return Frames(20);
            bool Done() => Services.Groups?.Get(g) == null || (Services.Groups?.IsUngrouped(g) ?? false);
            yield return UiAct(st, rec, "info Ungroup", "info-ungroup", null, null, null, null, mustChange: false, effectFrames: 4);
            yield return UiAct(st, rec, "OK (ungroup)", "info-confirm-ok", "ungroupGroup", new object[] { (double)g }, Done, () => _tool.UngroupGroup(g), mustChange: false, effectFrames: 10);
            yield return Frames(10);
            rec["dissolved"] = Services.Groups?.Get(g) == null;
            if (!Done()) rec["error"] = "the build is still grouped";
            try { sel?.SetSelection(Entity.Null); } catch (Exception) { }
            yield return OpenTool();
        }

        /// <summary>A road centre line for distance tests.</summary>
        private readonly struct Bezier
        {
            private readonly float3 _a, _b, _c, _d;
            public Bezier(float3 a, float3 b, float3 c, float3 d) { _a = a; _b = b; _c = c; _d = d; }
            public float DistanceXZ(float2 p)
            {
                float best = float.MaxValue;
                for (int i = 0; i <= 32; i++)
                {
                    float t = i / 32f, u = 1f - t;
                    var q = u * u * u * _a + 3f * u * u * t * _b + 3f * u * t * t * _c + t * t * t * _d;
                    best = math.min(best, math.distance(q.xz, p));
                }
                return best;
            }
        }
    }
}
