// Game runner — what a build leaves of Placemaker's own, before and after Ungroup (GPL-3.0).
//   {"do":"lab","op":"ungroupAudit","label":"built"}
//       the scenario's build: its record (in Manage or not, flags, the panel's upkeep), its tagged pieces by role, the
//       carrier and what it still owns, the owner lot (and its attractiveness), the park and parking companion buildings
//       (and the upkeep the game is asked to charge for them), the zone block lots; and, over every piece the build had at
//       its first audit: how many are alive, hidden (Overridden or Hidden), owned, and still tagged. Global counts of our
//       own lots and companion buildings go along.
//   {"do":"lab","op":"ungroupAudit","label":"bulldoze one","bulldoze":"tree"|"prop"|"bay"}
//       also clicks one piece of that kind with the game's own tool (the raycast lift) and bulldozes what the bulldozer
//       takes for it: the piece alone, or more.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Buildings;
using Game.Common;
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
        /// <summary>The pieces a scenario's build had at its first audit (by scenario id).</summary>
        private readonly Dictionary<string, List<Entity>> _auditPieces = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);

        private IEnumerator LabUngroupAudit(ScenarioState st, JObject step, JObject rec)
        {
            var em = _world.EntityManager;
            string label = (string)step["label"] ?? "";
            int g = st.Group;
            string key = st.S?.Id ?? "";
            if (!_auditPieces.TryGetValue(key, out var pieces))
            {
                pieces = _lookup.MemberSet(g).Where(m => em.Exists(m) && em.HasComponent<PlacemakerMember>(m)
                    && em.GetComponentData<PlacemakerMember>(m).Kind != Core.Build.MemberKind.Auxiliary).ToList();
                _auditPieces[key] = pieces;
            }
            var lines = new List<string>();

            // the record
            var reg = Services.Groups as GroupRegistrySystem;
            bool inManage = g != 0 && (Services.Groups?.AllGroups.Contains(g) ?? false);
            int records = 0;
            Entity carrier = Entity.Null, owner = Entity.Null, companion = Entity.Null;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerGroup>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var r in all)
                {
                    var pg = em.GetComponentData<PlacemakerGroup>(r);
                    if (pg.GroupId != g) continue;
                    records++;
                    carrier = pg.Carrier; owner = pg.Owner; companion = pg.Companion;
                    rec["recordFlags"] = ((Core.Build.GroupFlags)pg.Flags).ToString();
                }
            rec["records"] = records;
            rec["inManage"] = inManage;
            var info = inManage ? Services.Groups.Get(g) : null;
            rec["panelUpkeep"] = info?.Upkeep ?? 0;
            lines.Add($"record: {records} ({(string)rec["recordFlags"] ?? "none"}), in Manage {inManage}, panel upkeep {rec["panelUpkeep"]}");

            // tagged pieces of this id, by role
            var roles = new SortedDictionary<string, int>(StringComparer.Ordinal);
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var t = em.GetComponentData<PlacemakerMember>(e);
                    if (t.GroupId != g) continue;
                    string k = t.Role.ToString();
                    roles[k] = (roles.TryGetValue(k, out var n) ? n : 0) + 1;
                    if (t.Role == MemberRole.Carrier) carrier = e;
                }
            var tags = new JObject();
            foreach (var kv in roles) tags[kv.Key] = kv.Value;
            rec["tagged"] = tags;
            lines.Add("tagged: " + (roles.Count == 0 ? "none" : string.Join(", ", roles.Select(kv => $"{kv.Value} {kv.Key}"))));

            bool Live(Entity e) => e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e);
            // the carrier and what it owns
            if (Live(carrier))
            {
                int owned = 0, bays = 0;
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                        if (em.GetComponentData<Owner>(e).m_Owner == carrier)
                        {
                            owned++;
                            if (em.HasComponent<global::Game.Objects.Attached>(e)) bays++;
                        }
                int subNets = em.HasBuffer<global::Game.Net.SubNet>(carrier) ? em.GetBuffer<global::Game.Net.SubNet>(carrier, true).Length : 0;
                rec["carrier"] = new JObject { ["owns"] = owned, ["stallMarkings"] = bays, ["drivingLanes"] = subNets, ["tagged"] = em.HasComponent<PlacemakerMember>(carrier) };
                lines.Add($"carrier: alive, owns {owned} object(s) ({bays} stall marking(s)), {subNets} driving lane(s), tagged {em.HasComponent<PlacemakerMember>(carrier)}");
            }
            else { rec["carrier"] = "none"; lines.Add("carrier: none"); }
            if (Live(owner))
            {
                rec["ownerLot"] = new JObject { ["attractiveness"] = em.HasComponent<AttractivenessProvider>(owner) };
                lines.Add($"owner lot: alive (attractiveness {em.HasComponent<AttractivenessProvider>(owner)})");
            }
            else { rec["ownerLot"] = "none"; lines.Add("owner lot: none"); }

            // our own lots and companion buildings anywhere, and the upkeep the companions ask for
            var ownPrefabs = new SortedDictionary<string, int>(StringComparer.Ordinal);
            double askedUpkeep = 0;
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<global::Game.Prefabs.PrefabRef>() },
                Any = new[] { ComponentType.ReadOnly<global::Game.Areas.Area>(), ComponentType.ReadOnly<Building>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            }))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    string name = _lookup.PrefabName(e);
                    if (!name.StartsWith("Placemaker ", StringComparison.Ordinal) || name.StartsWith(Core.Decals.DecalLook.ClonePrefix, StringComparison.Ordinal)) continue;
                    bool lot = name == OwnerAreaPrefabSystem.PrefabName || name == OwnerAreaPrefabSystem.BlockerPrefabName;
                    bool building = em.HasComponent<Building>(e);
                    if (!lot && !building) continue;   // our surface copies are the pieces themselves
                    string k = lot ? name : building && name.StartsWith(Core.Build.ParkPrefabKey.Prefix, StringComparison.Ordinal) ? "Placemaker Park (companion)" : name;
                    ownPrefabs[k] = (ownPrefabs.TryGetValue(k, out var n) ? n : 0) + 1;
                    if (building && em.HasComponent<global::Game.Buildings.ServiceUsage>(e))
                        askedUpkeep += em.GetComponentData<global::Game.Buildings.ServiceUsage>(e).m_Usage * global::Placemaker.Game.Economy.ParkingCompanionPrefabSystem.UpkeepBasis;
                }
            var own = new JObject();
            foreach (var kv in ownPrefabs) own[kv.Key] = kv.Value;
            rec["ownInCity"] = own;
            lines.Add("our own lots/buildings in the city: " + (ownPrefabs.Count == 0 ? "none" : string.Join(", ", ownPrefabs.Select(kv => $"{kv.Value} {kv.Key}"))));
            if (Live(companion)) lines.Add("parking companion: alive");

            // the pieces the build had at its first audit
            int alive = 0, hidden = 0, owned2 = 0, tagged = 0;
            foreach (var p in pieces)
            {
                if (!Live(p)) continue;
                alive++;
                if (em.HasComponent<Overridden>(p) || em.HasComponent<global::Game.Tools.Hidden>(p)) hidden++;
                if (em.HasComponent<Owner>(p)) owned2++;
                if (em.HasComponent<PlacemakerMember>(p)) tagged++;
            }
            rec["pieces"] = new JObject { ["first"] = pieces.Count, ["alive"] = alive, ["hidden"] = hidden, ["owned"] = owned2, ["tagged"] = tagged };
            lines.Add($"pieces: {alive} of {pieces.Count} alive, {hidden} hidden, {owned2} with an owner, {tagged} still tagged");
            Mod.Log.Info($"Placemaker lab: ungroup audit ({label}), group {g}: " + string.Join("; ", lines) + ".");

            string dozeKind = (string)step["bulldoze"];
            if (string.IsNullOrEmpty(dozeKind)) yield break;
            bool Fits(Entity p)
            {
                if (!Live(p) || !em.HasComponent<global::Game.Objects.Transform>(p)) return false;
                bool tree = em.HasComponent<global::Game.Objects.Tree>(p), bay = em.HasComponent<global::Game.Objects.Attached>(p);
                return dozeKind == "tree" ? tree : dozeKind == "bay" ? bay : !tree && !bay && em.HasComponent<global::Game.Objects.Static>(p);
            }
            Entity part = pieces.FirstOrDefault(Fits);
            if (part == Entity.Null || !Live(part)) { rec["bulldoze"] = "no " + dozeKind; Mod.Log.Info($"Placemaker lab: ungroup audit ({label}): no {dozeKind} to bulldoze."); yield break; }
            yield return CloseTool();
            yield return Frames(10);
            var trs = _world.GetExistingSystemManaged<ToolRaycastSystem>();
            Entity top = part;
            for (int i = 0; i < 4 && em.HasComponent<Owner>(top); i++) top = em.GetComponentData<Owner>(top).m_Owner;
            bool hit = true;
            var r1 = new RaycastResult { m_Owner = top, m_Hit = { m_HitEntity = part, m_Position = em.GetComponentData<global::Game.Objects.Transform>(part).m_Position } };
            Placemaker.Game.Groups.Patches.RaycastLiftPatch.Apply(trs, ref hit, ref r1);
            // the bulldozer sends a static object with an owner on to that owner (BulldozeToolSystem, MainElements)
            var target = hit ? r1.m_Owner : Entity.Null;
            while (target != Entity.Null && em.HasComponent<global::Game.Objects.Static>(target) && em.HasComponent<Owner>(target)) target = em.GetComponentData<Owner>(target).m_Owner;
            string takes = target == Entity.Null ? "nothing" : target == part ? "the piece" : _lookup.KindOf(target) + " " + _lookup.PrefabName(target);
            int before = pieces.Count(Live);
            if (target == part && em.HasComponent<global::Game.Objects.Transform>(target))
            {
                yield return ApplyWithFixtureTool(t =>
                {
                    var def = t.NewDefinition();
                    em.AddComponentData(def, new CreationDefinition { m_Original = target, m_Flags = CreationFlags.Delete });
                    em.AddComponent<Updated>(def);
                    var tr = em.GetComponentData<global::Game.Objects.Transform>(target);
                    em.AddComponentData(def, new ObjectDefinition { m_Position = tr.m_Position, m_Rotation = tr.m_Rotation, m_LocalPosition = tr.m_Position, m_LocalRotation = tr.m_Rotation, m_ParentMesh = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_PrefabSubIndex = -1 });
                    return 1;
                }, "bulldoze one piece");
                yield return Frames(30);
            }
            int after = pieces.Count(Live);
            rec["bulldoze"] = new JObject { ["kind"] = dozeKind, ["bulldozerTakes"] = takes, ["piecesBefore"] = before, ["piecesAfter"] = after, ["inManage"] = g != 0 && (Services.Groups?.AllGroups.Contains(g) ?? false) };
            Mod.Log.Info($"Placemaker lab: ungroup audit ({label}): a click on a {dozeKind} ({_lookup.PrefabName(part)}), the bulldozer takes {takes}; pieces {before} -> {after}.");
            yield return OpenTool();
        }
    }
}
