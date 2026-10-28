// Placemaker — Ungroup dissolves a build for good (GPL-3.0).
// The user, 27 Oct: once ungrouped it must be as if the player had built the pieces by hand, with nothing of Placemaker's
// own left and nothing that keeps costing money. What a build leaves besides its pieces, and what Ungroup does with it:
//  * the record (Manage, the receipt, park stats, the parking fee)              → gone
//  * the owner lot "Placemaker Area" (the one selectable thing; it carries the
//    lot attractiveness when there is no park building)                         → deleted
//  * the park companion building (park stats, the park's upkeep)                → deleted
//  * the parking companion building (experimental: 48 × stalls + 438 upkeep, workers) → deleted
//  * the bare carrier, the shared owner of the props and trees (it keeps the game's OverrideSystem from hiding our own
//    pieces against each other) → they stand on their own, without the Updated that would re-run that check, so nothing is
//    hidden; the carrier is deleted
//  * the PlacemakerMember tags                                                  → removed
//  * zone block lots (invisible, they keep zoned buildings off the build) and the water source of a lake → stay, untagged:
//    zoning is the exception (the user: "except probably roads and zones"; without them zoning painted before comes back
//    and zoned buildings would grow over the pieces), the water is the lake's. Roads and the build's zoning are city parts
//    already. RaycastLiftPatch never picks a zone block lot (it is invisible).
//  * stall markings: their parking lanes find the driving lanes through the carrier (LaneConnectionSystem walks the
//    marking's owner and reads that owner's SubNet), which the game does not keep across loads for owner-less edges. A lot
//    with stall markings keeps that frame: the carrier, the markings and their driving lanes (GroupFlags.Dissolved, a bare
//    record that keeps the SubNet right after loads, after splits and bulldozed lanes). It is no build any more: not in
//    Manage, no lot, no fee, no upkeep. The markings cannot be bulldozed one by one (the bulldozer sends a click on an owned
//    object to its owner, the carrier: a click on one picks nothing); they go with their last driving lane, and when the
//    last marking is gone the driving lanes are plain paths and the frame goes.
// Runs in PreTool (GroupRemovalSystem), like removal: Deleted marked there is seen by everything after it.

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;

namespace Placemaker.Game.Groups
{
    public sealed partial class GroupRegistrySystem
    {
        /// <summary>Ungrouped builds still to dissolve (next PreTool once they are built).</summary>
        private readonly HashSet<int> _dissolvePending = new HashSet<int>();
        /// <summary>What is left of dissolved lots with stall markings (GroupFlags.Dissolved).</summary>
        private readonly HashSet<int> _frames = new HashSet<int>();
        private readonly Dictionary<int, int> _dissolveWaits = new Dictionary<int, int>();
        private EntityQuery _tempOriginals;
        private const int ToolReleaseFrames = 30;

        internal bool HasPendingDissolves => _dissolvePending.Count > 0;

        private OwnerAreaPrefabSystem _lotPrefabs;

        /// <summary>An untagged "Placemaker Zone Block" lot (what a dissolved build leaves to keep zoned buildings off its pieces).</summary>
        internal bool IsLooseZoneBlockLot(Entity e)
        {
            if (!EntityManager.HasComponent<global::Game.Areas.Area>(e) || EntityManager.HasComponent<PlacemakerMember>(e)
                || !EntityManager.HasComponent<global::Game.Prefabs.PrefabRef>(e)) return false;
            _lotPrefabs ??= World.GetExistingSystemManaged<OwnerAreaPrefabSystem>();
            return _lotPrefabs != null && _lotPrefabs.IsBlockerPrefab(EntityManager.GetComponentData<global::Game.Prefabs.PrefabRef>(e).m_Prefab);
        }

        /// <summary>A dissolved lot's parking frame: no build any more (not in Manage, no info panel section).</summary>
        internal bool IsFrame(int groupId) { EnsureIndex(); return _frames.Contains(groupId); }

        public bool Ungroup(int groupId)
        {
            EnsureIndex();
            if (_frames.Contains(groupId) || !_records.TryGetValue(groupId, out var entity) || !EntityManager.Exists(entity)) return false;
            if (EntityManager.HasComponent<PlacemakerRemoval>(entity)) return false;
            var rec = GetRecord(groupId);
            if (rec != null)
            {
                rec.Flags |= GroupFlags.Ungrouped;
                WriteRecord(entity, rec);   // kept with the record: a save before the dissolve dissolves it after the load
            }
            else
            {
                var g = EntityManager.GetComponentData<PlacemakerGroup>(entity);
                g.Flags |= (int)GroupFlags.Ungrouped;
                EntityManager.SetComponentData(entity, g);
            }
            _ungrouped.Add(groupId);
            _dissolvePending.Add(groupId);
            Mod.Log.Info($"Placemaker: group {groupId} ungrouped: it is dissolved into plain city pieces.");
            Bump();
            return true;
        }

        /// <summary>PreTool (GroupRemovalSystem): dissolves the pending builds, one per frame.</summary>
        internal void RunDissolves()
        {
            if (_dissolvePending.Count == 0) return;
            EnsureIndex();
            foreach (var id in new List<int>(_dissolvePending))
            {
                if (!_records.TryGetValue(id, out var record) || !EntityManager.Exists(record) || EntityManager.HasComponent<PlacemakerRemoval>(record)
                    || _frames.Contains(id))
                {
                    _dissolvePending.Remove(id); _dissolveWaits.Remove(id);
                    continue;
                }
                if (EntityManager.HasComponent<PlacemakerUnderConstruction>(record)) continue;   // after its build
                if (Services.Build is Build.BuildService b && b.IsBuildingGroup(id)) continue;
                try
                {
                    if (!Dissolve(id, record)) continue;
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, $"Placemaker: dissolving group {id} failed; it stays as it was");
                }
                _dissolvePending.Remove(id); _dissolveWaits.Remove(id);
                break;
            }
        }

        private bool Dissolve(int id, Entity record)
        {
            var g = EntityManager.GetComponentData<PlacemakerGroup>(record);
            var members = new List<Entity>();
            if (_members.TryGetValue(id, out var list))
                foreach (var e in list) if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e)) members.Add(e);
            Entity carrier = g.Carrier != Entity.Null && EntityManager.Exists(g.Carrier) && !EntityManager.HasComponent<Deleted>(g.Carrier) ? g.Carrier : Entity.Null;

            var delete = new List<Entity>();
            int bays = 0, aisles = 0;
            foreach (var e in members)
            {
                var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                if (tag.Role == MemberRole.OwnerArea || tag.Role == MemberRole.ParkCompanion || tag.Role == MemberRole.Companion) delete.Add(e);
                else if (tag.Role == MemberRole.ParkingBay && carrier != Entity.Null && EntityManager.HasComponent<Owner>(e)
                    && EntityManager.GetComponentData<Owner>(e).m_Owner == carrier) bays++;
                else if (tag.Role == MemberRole.ParkingAisle && tag.Kind == MemberKind.Edge) aisles++;
            }
            if (g.Owner != Entity.Null && EntityManager.Exists(g.Owner) && !EntityManager.HasComponent<Deleted>(g.Owner) && !delete.Contains(g.Owner)) delete.Add(g.Owner);
            if (g.Companion != Entity.Null && EntityManager.Exists(g.Companion) && !EntityManager.HasComponent<Deleted>(g.Companion) && !delete.Contains(g.Companion)) delete.Add(g.Companion);
            bool frame = bays > 0 && aisles > 0;

            // A tool's temporary copy of what goes (the bulldozer hovering the lot) would be left reading a dead entity: wait for
            // the tool to let go, then switch to the default tool once (as removal does).
            if (ReferencedByTemporaries(delete))
            {
                int waited = _dissolveWaits.TryGetValue(id, out var w) ? w + 1 : 1;
                _dissolveWaits[id] = waited;
                var tools = World.GetExistingSystemManaged<ToolSystem>();
                if (waited == ToolReleaseFrames && tools?.activeTool != null && !(tools.activeTool is DefaultToolSystem))
                    tools.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
                return false;
            }

            // the owner lot and the companion buildings
            int lots = 0, companions = 0;
            foreach (var e in delete)
            {
                if (EntityManager.HasComponent<global::Game.Companies.WorkProvider>(e))
                {
                    // employees leave cleanly instead of pointing at a dead workplace
                    var wp = EntityManager.GetComponentData<global::Game.Companies.WorkProvider>(e);
                    wp.m_MaxWorkers = 0;
                    EntityManager.SetComponentData(e, wp);
                }
                if (EntityManager.HasComponent<global::Game.Areas.Area>(e)) lots++; else companions++;
                if (EntityManager.HasComponent<PlacemakerMember>(e)) EntityManager.RemoveComponent<PlacemakerMember>(e);   // no cascade, no "bulldozed" notice
                EntityManager.AddComponent<Deleted>(e);
            }

            // the pieces: plain city pieces (the frame's markings and driving lanes keep their tags)
            var freed = new HashSet<Entity>();
            int pieces = 0, kept = 0, zoneLots = 0, water = 0, framed = 0;
            var untag = new List<Entity>();
            foreach (var e in members)
            {
                if (delete.Contains(e) || e == carrier) continue;
                var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                if (frame && (tag.Role == MemberRole.ParkingBay || (tag.Role == MemberRole.ParkingAisle && tag.Kind == MemberKind.Edge))) { framed++; continue; }
                if (carrier != Entity.Null && EntityManager.HasComponent<Owner>(e) && EntityManager.GetComponentData<Owner>(e).m_Owner == carrier
                    && EntityManager.HasComponent<global::Game.Objects.Object>(e))
                {
                    // shared the carrier as owner (no Updated: that would re-run the game's override check)
                    freed.Add(e);
                    EntityManager.RemoveComponent<Owner>(e);
                    if (EntityManager.HasComponent<global::Game.Objects.Attached>(e)) EntityManager.RemoveComponent<global::Game.Objects.Attached>(e);
                }
                if (tag.Role == MemberRole.ZoneBlocker) zoneLots++;
                else if (tag.Role == MemberRole.WaterSource) water++;
                else if (tag.Kind == MemberKind.Auxiliary) kept++;
                else pieces++;
                untag.Add(e);
            }
            foreach (var e in untag) EntityManager.RemoveComponent<PlacemakerMember>(e);

            if (carrier != Entity.Null)
            {
                if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(carrier))
                {
                    var subs = EntityManager.GetBuffer<global::Game.Objects.SubObject>(carrier);
                    for (int i = subs.Length - 1; i >= 0; i--) if (!frame || freed.Contains(subs[i].m_SubObject)) subs.RemoveAt(i);
                }
                if (!frame)
                {
                    if (EntityManager.HasBuffer<global::Game.Net.SubNet>(carrier)) EntityManager.GetBuffer<global::Game.Net.SubNet>(carrier).Clear();
                    if (EntityManager.HasBuffer<global::Game.Areas.SubArea>(carrier)) EntityManager.GetBuffer<global::Game.Areas.SubArea>(carrier).Clear();
                    EntityManager.RemoveComponent<PlacemakerMember>(carrier);
                    EntityManager.AddComponent<Deleted>(carrier);
                }
            }

            string recipe = GetRecord(id)?.RecipeNameKey ?? "";
            if (frame)
            {
                // the lot's own fee and comfort go: the game works them out again for its parking lanes, as for hand-placed ones
                int lanes = ResetParkingLanes(id, members);
                var bare = new GroupRecord { GroupId = id, RecipeNameKey = recipe, Flags = GroupFlags.Ungrouped | GroupFlags.Dissolved };
                g.Owner = Entity.Null; g.Companion = Entity.Null;
                EntityManager.SetComponentData(record, g);
                WriteRecord(record, bare);
                _frames.Add(id);
                World.GetOrCreateSystemManaged<Economy.ParkingEconomySystem>().Detach(id);
                Mod.Log.Info($"Placemaker dissolved group {id} ({recipe}): {lots} owner lot(s) and {companions} companion building(s) deleted, {pieces} piece(s) "
                    + $"are plain city pieces now ({freed.Count} prop(s)/tree(s) off the carrier), {zoneLots} zone block lot(s) and {water} water source(s) stay; "
                    + $"its parking frame stays: the carrier with {bays} stall marking(s) and {aisles} driving lane(s) ({lanes} parking lane(s) back to the game's fee and comfort).");
            }
            else
            {
                EntityManager.AddComponent<Deleted>(record);
                Mod.Log.Info($"Placemaker dissolved group {id} ({recipe}): {lots} owner lot(s) and {companions} companion building(s) deleted, {pieces} piece(s) "
                    + $"are plain city pieces now ({freed.Count} prop(s)/tree(s) off the carrier{(carrier != Entity.Null ? ", carrier deleted" : "")}), "
                    + $"{zoneLots} zone block lot(s) and {water} water source(s) stay; nothing else of Placemaker's is left.");
            }
            if (kept > 0) Mod.Log.Warn($"Placemaker: dissolving group {id} left {kept} other helper(s) untagged.");
            _ungrouped.Remove(id);
            _dirty = true;
            Bump();
            return true;
        }

        private int ResetParkingLanes(int id, List<Entity> members)
        {
            int n = 0;
            foreach (var e in members)
            {
                if (!EntityManager.Exists(e) || !EntityManager.HasBuffer<global::Game.Net.SubLane>(e) || !EntityManager.HasComponent<PlacemakerMember>(e)
                    || EntityManager.GetComponentData<PlacemakerMember>(e).Role != MemberRole.ParkingBay) continue;
                var lanes = EntityManager.GetBuffer<global::Game.Net.SubLane>(e, true);
                var copy = new List<Entity>(lanes.Length);
                for (int i = 0; i < lanes.Length; i++) copy.Add(lanes[i].m_SubLane);
                foreach (var lane in copy)
                {
                    if (!EntityManager.Exists(lane) || !EntityManager.HasComponent<global::Game.Net.ParkingLane>(lane) || EntityManager.HasComponent<PathfindUpdated>(lane)) continue;
                    EntityManager.AddComponent<PathfindUpdated>(lane);
                    n++;
                }
            }
            return n;
        }

        private bool ReferencedByTemporaries(List<Entity> entities)
        {
            if (entities.Count == 0) return false;
            if (_tempOriginals == default) _tempOriginals = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.Exclude<Deleted>());
            if (_tempOriginals.IsEmptyIgnoreFilter) return false;
            var set = new HashSet<Entity>(entities);
            using var temps = _tempOriginals.ToComponentDataArray<Temp>(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
                if (temps[i].m_Original != Entity.Null && set.Contains(temps[i].m_Original)) return true;
            return false;
        }

        /// <summary>
        /// A parking frame after some of it was deleted: without driving lanes its markings have nothing to park on (they go,
        /// with the carrier); without markings its driving lanes are plain paths (the tags and the carrier go).
        /// </summary>
        private void CheckFrame(int id)
        {
            if (!_records.TryGetValue(id, out var record) || !EntityManager.Exists(record) || EntityManager.HasComponent<PlacemakerRemoval>(record)) return;
            int bays = 0, aisles = 0;
            var rest = new List<Entity>();
            if (_members.TryGetValue(id, out var list))
                foreach (var e in list)
                {
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                    var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                    if (tag.Role == MemberRole.ParkingBay) bays++;
                    else if (tag.Role == MemberRole.ParkingAisle && tag.Kind == MemberKind.Edge) aisles++;
                    if (tag.Role != MemberRole.Carrier) rest.Add(e);
                }
            if (bays > 0 && aisles > 0) return;
            if (bays > 0)
            {
                Mod.Log.Info($"Placemaker: the parking frame of dissolved group {id} lost its last driving lane; its {bays} stall marking(s) go with it.");
                RemoveInternal(id);
                return;
            }
            // no markings left: the driving lanes (and the nodes between them) are plain paths, the carrier and the record go
            foreach (var e in rest) EntityManager.RemoveComponent<PlacemakerMember>(e);
            var carrier = EntityManager.GetComponentData<PlacemakerGroup>(record).Carrier;
            if (carrier != Entity.Null && EntityManager.Exists(carrier) && !EntityManager.HasComponent<Deleted>(carrier))
            {
                if (EntityManager.HasBuffer<global::Game.Net.SubNet>(carrier)) EntityManager.GetBuffer<global::Game.Net.SubNet>(carrier).Clear();
                if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(carrier)) EntityManager.GetBuffer<global::Game.Objects.SubObject>(carrier).Clear();
                EntityManager.RemoveComponent<PlacemakerMember>(carrier);
                EntityManager.AddComponent<Deleted>(carrier);
            }
            EntityManager.AddComponent<Deleted>(record);
            _frames.Remove(id);
            Mod.Log.Info($"Placemaker: the parking frame of dissolved group {id} has no stall markings left; its {aisles} driving lane(s) are plain paths now.");
        }
    }
}
