// Game runner — group records across loads (GitHub issue #6: "duplicate group id", then a crash when a yard was rebuilt
// after loading a city a second time in one game). GPL-3.0.
//   {"do":"lab","op":"groupRecords","label":"after the load"}
//       every group record in the world, by id: how many records carry it (more than one: a duplicate), how many of them
//       point at a live owner lot, carrier or companion, whether one is being removed, and how many members carry the id.
//   {"do":"lab","op":"oldDuplicate"}
//       what builds before the fix left in a city saved after it was loaded twice in one game: a second record for every
//       group (the same receipt; its owner lot, carrier and companion gone with the earlier load) and the record of a
//       group this city never had (another city's, loaded earlier in the same game). Save and load after it.

using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private void LabGroupRecords(JObject step, JObject rec)
        {
            var em = _world.EntityManager;
            string label = (string)step["label"] ?? "";
            var byId = new SortedDictionary<int, List<Entity>>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerGroup>(), ComponentType.Exclude<Deleted>()))
            using (var records = q.ToEntityArray(Allocator.Temp))
                foreach (var r in records)
                {
                    int id = em.GetComponentData<PlacemakerGroup>(r).GroupId;
                    if (!byId.TryGetValue(id, out var list)) byId[id] = list = new List<Entity>();
                    list.Add(r);
                }
            var members = new Dictionary<int, int>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var tags = q.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < tags.Length; i++) members[tags[i].GroupId] = (members.TryGetValue(tags[i].GroupId, out var n) ? n : 0) + 1;
            bool Live(Entity e) => e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e);

            var groups = new JArray();
            var parts = new List<string>();
            int total = 0, duplicated = 0, memberless = 0;
            foreach (var kv in byId)
            {
                int live = 0, removing = 0;
                foreach (var r in kv.Value)
                {
                    var g = em.GetComponentData<PlacemakerGroup>(r);
                    if (Live(g.Owner) || Live(g.Carrier) || Live(g.Companion)) live++;
                    if (em.HasComponent<PlacemakerRemoval>(r)) removing++;
                }
                int m = members.TryGetValue(kv.Key, out var mm) ? mm : 0;
                total += kv.Value.Count;
                if (kv.Value.Count > 1) duplicated++;
                if (m == 0) memberless++;
                // (P2-1) the hand edits its receipt keeps (Core/Touches.cs): they must come back after a load
                int touches = 0;
                var gr = Services.Groups is GroupRegistrySystem reg ? reg.GetRecord(kv.Key) : null;
                if (gr?.Recipe != null) foreach (var w in Placemaker.Core.Tool.RecipePaths.Walk(gr.Recipe)) touches += w.Value.Touches.Count;
                groups.Add(new JObject { ["id"] = kv.Key, ["records"] = kv.Value.Count, ["withLiveRefs"] = live, ["removing"] = removing, ["members"] = m, ["handEdits"] = touches });
                parts.Add($"group {kv.Key}: {kv.Value.Count} record(s), {live} with a live owner/carrier/companion, {m} members, {touches} hand edits" + (removing > 0 ? $", {removing} being removed" : ""));
            }
            int strays = 0;
            foreach (var kv in members) if (!byId.ContainsKey(kv.Key)) strays += kv.Value;
            rec["groups"] = groups;
            rec["records"] = total;
            rec["duplicatedIds"] = duplicated;
            rec["memberlessIds"] = memberless;
            rec["membersWithoutRecord"] = strays;
            rec["listed"] = Services.Groups?.AllGroups.Count ?? 0;
            Mod.Log.Info($"Placemaker lab: group records ({label}): {total} record(s) for {byId.Count} id(s), {duplicated} id(s) with more than one record, "
                + $"{memberless} without members, {strays} member(s) of no record; the registry lists {rec["listed"]}. " + string.Join("; ", parts));
        }

        private void LabOldDuplicate(JObject rec)
        {
            var em = _world.EntityManager;
            int copies = 0, maxId = 0;
            Entity first = Entity.Null;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlacemakerGroup>(), ComponentType.Exclude<Deleted>()))
            using (var records = q.ToEntityArray(Allocator.Temp))
                foreach (var r in records)
                {
                    var copy = em.Instantiate(r);
                    var g = em.GetComponentData<PlacemakerGroup>(copy);
                    g.Owner = Entity.Null; g.Carrier = Entity.Null; g.Companion = Entity.Null;
                    em.SetComponentData(copy, g);
                    if (g.GroupId > maxId) maxId = g.GroupId;
                    if (first == Entity.Null) first = r;
                    copies++;
                }
            int foreign = 0;
            if (first != Entity.Null)
            {
                var other = em.Instantiate(first);
                var g = em.GetComponentData<PlacemakerGroup>(other);
                g.GroupId = foreign = maxId + 50;
                g.Owner = Entity.Null; g.Carrier = Entity.Null; g.Companion = Entity.Null;
                em.SetComponentData(other, g);
            }
            GroupRegistrySystem.Instance?.MarkDirty();
            rec["copies"] = copies;
            rec["foreignId"] = foreign;
            Mod.Log.Info($"Placemaker lab: old duplicates: {copies} record(s) copied without their owner lot, carrier and companion, and a record of group {foreign} that has no members");
        }
    }
}
