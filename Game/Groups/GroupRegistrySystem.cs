// Placemaker — registry of built groups (GPL-3.0).
// Ownership approach credited to ParkManager (tomkpunkt, GPL-3.0: headless build record +
// ParkPathMember tags) with ParkingLotTool's lessons (KruemelGames, GPL-3.0: carrier SubNet is
// lost on load, orphans after saving without the mod) built in.
//
// Phase: ModificationEnd. Deleted members of this frame are still alive there (CleanUp runs at the
// end of the frame), so dangling carrier SubNet entries are removed before anything reads them
// next frame, and manual bulldozing is noticed the same frame. Runs while paused.

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Groups
{
    public sealed partial class GroupRegistrySystem : GameSystemBase, IGroupRegistry
    {
        private EntityQuery _recordQuery;
        private EntityQuery _memberQuery;
        private EntityQuery _changedMemberQuery;
        private EntityQuery _changedRecordQuery;
        private EntityQuery _deletedMemberQuery;
        private EntityQuery _createdNetQuery;

        private readonly Dictionary<int, Entity> _records = new Dictionary<int, Entity>();
        private readonly Dictionary<int, GroupRecord> _decoded = new Dictionary<int, GroupRecord>();
        private readonly Dictionary<int, List<Entity>> _members = new Dictionary<int, List<Entity>>();
        private readonly Dictionary<int, List<string>> _missing = new Dictionary<int, List<string>>();
        private readonly List<int> _allIds = new List<int>();
        private readonly Dictionary<int, Entity> _owners = new Dictionary<int, Entity>();
        private readonly HashSet<int> _ungrouped = new HashSet<int>();
        /// <summary>Ungrouped builds whose props and trees still share the carrier owner (released next update, ReleasePending).</summary>
        private readonly HashSet<int> _releasePending = new HashSet<int>();
        private readonly HashSet<int> _duplicateWarned = new HashSet<int>();

        /// <summary>For Harmony patches (static context, main thread only).</summary>
        internal static GroupRegistrySystem Instance { get; private set; }
        private bool _dirty = true;
        private bool _postLoadPending;
        private int _missingCheckedCatalogVersion = -1;
        private int _version;

        /// <summary>Bumped whenever groups, names or member counts change (UI refresh hint).</summary>
        public int Version => _version;

        public event Action Changed;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _recordQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerGroup>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _memberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _changedMemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            _deletedMemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Temp>() },
            });
            _changedRecordQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerGroup>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>() },
            });
            _createdNetQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Created>() },
                Any = new[] { ComponentType.ReadOnly<global::Game.Net.Edge>(), ComponentType.ReadOnly<global::Game.Net.Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PlacemakerMember>() },
            });
            Services.Groups = this;
            Instance = this;
            Patches.PlacemakerHarmony.Install();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (ReferenceEquals(Services.Groups, this)) Services.Groups = null;
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
                Patches.PlacemakerHarmony.Uninstall();
            }
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            ClearCaches();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            ClearCaches();
            _postLoadPending = mode.IsGame();
        }

        private void ClearCaches()
        {
            _records.Clear(); _decoded.Clear(); _members.Clear(); _missing.Clear(); _allIds.Clear(); _owners.Clear(); _duplicateWarned.Clear();
            _dirty = true;
            _missingCheckedCatalogVersion = -1;
            Bump();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                if (_postLoadPending)
                {
                    _postLoadPending = false;
                    PostLoadRepair();
                }
                if (!_changedMemberQuery.IsEmptyIgnoreFilter || !_changedRecordQuery.IsEmptyIgnoreFilter)
                {
                    if (!_deletedMemberQuery.IsEmptyIgnoreFilter) HandleDeletedMembers();
                    _dirty = true;
                    Bump();
                }
                CheckMissingAssets();
                if (_releasePending.Count > 0) ReleasePending();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker group registry update failed");
            }
        }

        // ───────────────────────── IGroupRegistry ─────────────────────────

        public int GroupOf(Entity e)
        {
            if (e == Entity.Null || !EntityManager.Exists(e) || !EntityManager.HasComponent<PlacemakerMember>(e)) return 0;
            int id = EntityManager.GetComponentData<PlacemakerMember>(e).GroupId;
            EnsureIndex();
            return _records.ContainsKey(id) ? id : 0;
        }

        public GroupInfo Get(int groupId)
        {
            EnsureIndex();
            if (!_records.ContainsKey(groupId)) return null;
            var rec = GetRecord(groupId);
            var info = new GroupInfo { Id = groupId };
            if (rec != null)
            {
                info.RecipeId = rec.RecipeId ?? "";
                info.RecipeNameKey = rec.RecipeNameKey ?? "";
                info.CustomName = rec.CustomName ?? "";
                info.Stats = new List<PlanStat>(rec.Stats);
                info.Upkeep = rec.Upkeep;
            }
            info.MemberCount = CountMembers(groupId, false);
            return info;
        }

        public GroupReceipt GetReceipt(int groupId)
        {
            var rec = GetRecord(groupId);
            if (rec == null || rec.Recipe == null || rec.Area == null) return null;
            var copy = rec.Clone();
            return new GroupReceipt { FormatVersion = ReceiptCodec.CurrentVersion, Recipe = copy.Recipe, Area = copy.Area };
        }

        public IReadOnlyCollection<int> AllGroups
        {
            get { EnsureIndex(); return _allIds; }
        }

        public void GetMembers(int groupId, List<Entity> into)
        {
            if (into == null) return;
            EnsureIndex();
            if (!_members.TryGetValue(groupId, out var list)) return;
            foreach (var e in list)
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e)) into.Add(e);
        }

        public bool RequestRemove(int groupId)
        {
            // The group a build is creating right now cannot be removed from outside; the build
            // rolls it back itself on failure.
            if (Services.Build is Build.BuildService b && b.IsBuildingGroup(groupId)) return false;
            return RemoveInternal(groupId);
        }

        internal bool RemoveInternal(int groupId)
        {
            EnsureIndex();
            if (!_records.TryGetValue(groupId, out var record) || !EntityManager.Exists(record)) return false;
            if (EntityManager.HasComponent<PlacemakerUnderConstruction>(record)) EntityManager.RemoveComponent<PlacemakerUnderConstruction>(record);
            if (!EntityManager.HasComponent<PlacemakerRemoval>(record))
                EntityManager.AddComponentData(record, new PlacemakerRemoval());
            Mod.Log.Info($"Placemaker: removal of group {groupId} requested ({CountMembers(groupId, true)} members).");
            Bump();
            return true;
        }

        /// <summary>The player ungrouped this build: its pieces are picked and bulldozed one by one.</summary>
        public bool IsUngrouped(int groupId)
        {
            EnsureIndex();
            return _ungrouped.Contains(groupId);
        }

        /// <summary>
        /// Ungroup (or group again): clicks on a part pick that part, the bulldozer removes single parts, and the owner lot is
        /// never hit, so nothing removes the whole build by accident. Kept on the record (ECS flags and receipt) so it survives
        /// a save. Remove/Edit in Placemaker's panels still work on the whole build.
        /// </summary>
        public bool SetUngrouped(int groupId, bool ungrouped)
        {
            EnsureIndex();
            if (!_records.TryGetValue(groupId, out var entity) || !EntityManager.Exists(entity)) return false;
            if (EntityManager.HasComponent<PlacemakerRemoval>(entity)) return false;
            var g = EntityManager.GetComponentData<PlacemakerGroup>(entity);
            int flags = ungrouped ? g.Flags | (int)GroupFlags.Ungrouped : g.Flags & ~(int)GroupFlags.Ungrouped;
            if (flags == g.Flags) return true;
            g.Flags = flags;
            EntityManager.SetComponentData(entity, g);
            var rec = GetRecord(groupId);
            if (rec != null)
            {
                rec.Flags = ungrouped ? rec.Flags | GroupFlags.Ungrouped : rec.Flags & ~GroupFlags.Ungrouped;
                WriteRecord(entity, rec);
            }
            if (ungrouped) { _ungrouped.Add(groupId); _releasePending.Add(groupId); } else _ungrouped.Remove(groupId);
            Mod.Log.Info($"Placemaker: group {groupId} {(ungrouped ? "ungrouped (its parts are picked one by one)" : "grouped again")}.");
            // in pieces it is no park (no park building, no upkeep); grouped again, the park building comes back
            var counts = new ParkSyncCounts();
            SyncParkStatsOf(groupId, entity, ref counts);
            Bump();
            return true;
        }

        /// <summary>
        /// Raycast of an ungrouped build: <paramref name="owner"/> is what the game picked (the top owner of <paramref name="hit"/>).
        /// True when it must change: the build's owner lot → nothing (<paramref name="pick"/> = Null); its carrier or another
        /// auxiliary → the member that was hit (walking up from <paramref name="hit"/>), or nothing. False: leave the pick as it is.
        /// </summary>
        internal bool TryUngroupedPick(Entity owner, Entity hit, out Entity pick)
        {
            pick = Entity.Null;
            if (owner == Entity.Null || !EntityManager.Exists(owner) || !EntityManager.HasComponent<PlacemakerMember>(owner)) return false;
            var tag = EntityManager.GetComponentData<PlacemakerMember>(owner);
            if (!IsUngrouped(tag.GroupId)) return false;
            if (tag.Role == MemberRole.OwnerArea) return true;
            if (tag.Kind != MemberKind.Auxiliary) return false;
            var e = hit;
            for (int depth = 0; depth < 4 && e != Entity.Null && e != owner && EntityManager.Exists(e); depth++)
            {
                if (EntityManager.HasComponent<PlacemakerMember>(e))
                {
                    var t = EntityManager.GetComponentData<PlacemakerMember>(e);
                    if (t.GroupId == tag.GroupId && t.Kind != MemberKind.Auxiliary) { pick = e; return true; }
                }
                if (!EntityManager.HasComponent<Owner>(e)) break;
                e = EntityManager.GetComponentData<Owner>(e).m_Owner;
            }
            return true;
        }

        /// <summary>
        /// Ungrouped builds: their props and trees leave the build's bare carrier (the shared owner that keeps the game's
        /// OverrideSystem from hiding our own pieces against each other) and stand on their own. The bulldozer sends a click on a
        /// static object with an owner to that owner (BulldozeToolSystem, MainElements), so a carrier-owned bench, bin, lamp or tree
        /// could be selected but never bulldozed (the user, 22 Oct). Stall markings keep the carrier (their parking lanes need it).
        /// Runs in ModificationEnd, once the build is applied; the owner's SubObject list is kept in step. Grouping again keeps them
        /// standalone (the lift to the owner lot does not need the carrier).
        /// </summary>
        private void ReleasePending()
        {
            EnsureIndex();
            foreach (var id in new List<int>(_releasePending))
            {
                if (!TryGetRecordEntity(id, out var rec) || !_ungrouped.Contains(id) || EntityManager.HasComponent<PlacemakerRemoval>(rec)) { _releasePending.Remove(id); continue; }
                if (EntityManager.HasComponent<PlacemakerUnderConstruction>(rec)) continue;   // after its build
                var carrier = EntityManager.GetComponentData<PlacemakerGroup>(rec).Carrier;
                var free = new HashSet<Entity>();
                if (carrier != Entity.Null && EntityManager.Exists(carrier) && _members.TryGetValue(id, out var members))
                    foreach (var e in members)
                    {
                        if (!EntityManager.Exists(e) || EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                        if (!EntityManager.HasComponent<global::Game.Objects.Object>(e) || !EntityManager.HasComponent<Owner>(e)) continue;
                        if (EntityManager.GetComponentData<Owner>(e).m_Owner != carrier) continue;
                        var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                        if (tag.Kind == MemberKind.Auxiliary || tag.Role == MemberRole.ParkingBay) continue;
                        free.Add(e);
                    }
                if (free.Count > 0)
                {
                    if (EntityManager.HasBuffer<global::Game.Objects.SubObject>(carrier))
                    {
                        var subs = EntityManager.GetBuffer<global::Game.Objects.SubObject>(carrier);
                        for (int i = subs.Length - 1; i >= 0; i--) if (free.Contains(subs[i].m_SubObject)) subs.RemoveAt(i);
                    }
                    foreach (var e in free) EntityManager.RemoveComponent<Owner>(e);
                }
                _releasePending.Remove(id);
                Mod.Log.Info($"Placemaker: ungrouped group {id}: {free.Count} prop(s) and tree(s) now stand on their own, so the bulldozer takes them one by one.");
            }
        }

        public void Rename(int groupId, string name)
        {
            var rec = GetRecord(groupId);
            if (rec == null || !_records.TryGetValue(groupId, out var entity)) return;
            name = (name ?? "").Trim();
            if (name.Length > 120) name = name.Substring(0, 120);
            rec.CustomName = name;
            WriteRecord(entity, rec);
        }

        // ───────────────────────── internal API (build/removal/economy) ─────────────────────────

        /// <summary>The live owner lot of a group (Entity.Null for groups without one).</summary>
        public Entity OwnerAreaOf(int groupId)
        {
            EnsureIndex();
            if (!_owners.TryGetValue(groupId, out var owner)) return Entity.Null;
            return owner != Entity.Null && EntityManager.Exists(owner) && !EntityManager.HasComponent<Deleted>(owner)
                && !EntityManager.HasComponent<Temp>(owner) ? owner : Entity.Null;
        }

        /// <summary>
        /// Raycast lift: the owner lot of the group <paramref name="hit"/> (or one of its owners up to
        /// 3 levels, e.g. a street light of one of our roads) belongs to. Main thread only.
        /// </summary>
        internal bool TryLiftToOwner(Entity hit, out Entity owner)
        {
            owner = Entity.Null;
            var e = hit;
            for (int depth = 0; depth < 4 && e != Entity.Null && EntityManager.Exists(e); depth++)
            {
                if (EntityManager.HasComponent<PlacemakerMember>(e))
                {
                    var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                    if (tag.Role == MemberRole.OwnerArea) return false;   // already the owner
                    if (IsUngrouped(tag.GroupId)) return false;            // ungrouped: the part itself
                    owner = OwnerAreaOf(tag.GroupId);
                    return owner != Entity.Null && owner != hit;
                }
                if (!EntityManager.HasComponent<Owner>(e)) return false;
                e = EntityManager.GetComponentData<Owner>(e).m_Owner;
            }
            return false;
        }

        /// <summary>
        /// Fallback when a group has no owner lot (the game rejected it): a tagged top-level object/area of
        /// a live, finished group without a live owner. Bulldozing it removes the whole group (playtest:
        /// "deleting something does not delete the whole thing").
        /// </summary>
        private bool BulldozerActive()
        {
            var tools = World.GetExistingSystemManaged<global::Game.Tools.ToolSystem>();
            return tools != null && tools.activeTool is global::Game.Tools.BulldozeToolSystem;
        }

        internal bool IsOwnerlessGroupPart(Entity entity, out int groupId)
        {
            groupId = 0;
            if (entity == Entity.Null || !EntityManager.Exists(entity) || !EntityManager.HasComponent<PlacemakerMember>(entity)) return false;
            var tag = EntityManager.GetComponentData<PlacemakerMember>(entity);
            if (tag.Role == MemberRole.OwnerArea || tag.Kind == MemberKind.Edge || tag.Kind == MemberKind.Node || tag.Kind == MemberKind.Auxiliary) return false;
            if (IsUngrouped(tag.GroupId)) return false;   // ungrouped: bulldozing a part removes that part only
            if (!TryGetRecordEntity(tag.GroupId, out var rec) || EntityManager.HasComponent<PlacemakerRemoval>(rec)
                || EntityManager.HasComponent<PlacemakerUnderConstruction>(rec)) return false;
            if (OwnerAreaOf(tag.GroupId) != Entity.Null) return false;
            groupId = tag.GroupId;
            return true;
        }

        /// <summary>True when <paramref name="entity"/> is the owner lot of a Placemaker group.</summary>
        internal bool IsOwnerArea(Entity entity) =>
            entity != Entity.Null && EntityManager.Exists(entity) && EntityManager.HasComponent<PlacemakerMember>(entity)
            && EntityManager.GetComponentData<PlacemakerMember>(entity).Role == MemberRole.OwnerArea;

        /// <summary>Owners need SubArea/SubNet/SubObject buffers even when empty (RESEARCH-NOTES 8).</summary>
        internal void EnsureOwnerBuffers(Entity owner)
        {
            if (owner == Entity.Null || !EntityManager.Exists(owner)) return;
            if (!EntityManager.HasBuffer<global::Game.Areas.SubArea>(owner)) EntityManager.AddBuffer<global::Game.Areas.SubArea>(owner);
            if (!EntityManager.HasBuffer<global::Game.Net.SubNet>(owner)) EntityManager.AddBuffer<global::Game.Net.SubNet>(owner);
            if (!EntityManager.HasBuffer<global::Game.Objects.SubObject>(owner)) EntityManager.AddBuffer<global::Game.Objects.SubObject>(owner);
        }

        public bool IsRemoving(int groupId) =>
            _records.TryGetValue(groupId, out var e) && EntityManager.Exists(e) && EntityManager.HasComponent<PlacemakerRemoval>(e);

        /// <summary>Prefab names of this group that are not loaded (empty = complete).</summary>
        public IReadOnlyList<string> MissingAssets(int groupId) =>
            _missing.TryGetValue(groupId, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>();

        public bool HasMissingAssets(int groupId) => _missing.TryGetValue(groupId, out var l) && l.Count > 0;

        public int AllocateId()
        {
            EnsureIndex();
            int max = 0;
            foreach (var id in _records.Keys) if (id > max) max = id;
            // Also scan member tags: a record lost to a "saved without the mod" round trip must not
            // have its id reused while stale tags still carry it.
            using (var members = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < members.Length; i++) if (members[i].GroupId > max) max = members[i].GroupId;
            return max + 1;
        }

        public Entity CreateRecord(GroupRecord rec, bool underConstruction)
        {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new PlacemakerGroup { GroupId = rec.GroupId, Flags = (int)rec.Flags });
            EntityManager.AddBuffer<PlacemakerGroupBlob>(e);
            if (underConstruction) EntityManager.AddComponent<PlacemakerUnderConstruction>(e);
            WriteRecord(e, rec);
            _records[rec.GroupId] = e;
            if (!_allIds.Contains(rec.GroupId)) _allIds.Add(rec.GroupId);
            return e;
        }

        public void WriteRecord(Entity e, GroupRecord rec)
        {
            if (!EntityManager.Exists(e) || !EntityManager.HasComponent<PlacemakerGroup>(e)) return;
            var bytes = ReceiptCodec.Encode(rec);
            var buffer = EntityManager.GetBuffer<PlacemakerGroupBlob>(e);
            buffer.ResizeUninitialized(bytes.Length);
            for (int i = 0; i < bytes.Length; i++) buffer[i] = new PlacemakerGroupBlob { Value = bytes[i] };
            var g = EntityManager.GetComponentData<PlacemakerGroup>(e);
            g.Flags = (int)rec.Flags;
            EntityManager.SetComponentData(e, g);
            if ((rec.Flags & GroupFlags.Ungrouped) != 0) { if (_ungrouped.Add(rec.GroupId)) _releasePending.Add(rec.GroupId); }
            else _ungrouped.Remove(rec.GroupId);
            _decoded[rec.GroupId] = rec;
            Bump();
        }

        public bool TryGetRecordEntity(int groupId, out Entity e)
        {
            EnsureIndex();
            return _records.TryGetValue(groupId, out e) && EntityManager.Exists(e);
        }

        /// <summary>Decoded record (cached; callers that modify it must call WriteRecord).</summary>
        public GroupRecord GetRecord(int groupId)
        {
            EnsureIndex();
            if (_decoded.TryGetValue(groupId, out var rec)) return rec;
            if (!_records.TryGetValue(groupId, out var e) || !EntityManager.HasBuffer<PlacemakerGroupBlob>(e)) return null;
            var buffer = EntityManager.GetBuffer<PlacemakerGroupBlob>(e, true);
            var bytes = new byte[buffer.Length];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = buffer[i].Value;
            rec = ReceiptCodec.Decode(bytes);
            if (rec == null)
            {
                // Unreadable blob: keep the group manageable (remove still works) with a neutral name.
                Mod.Log.Warn($"Placemaker: receipt of group {groupId} is unreadable; the group can be removed but not edited.");
                rec = new GroupRecord { GroupId = groupId, RecipeNameKey = "Placemaker.Group.Unknown" };
            }
            rec.GroupId = groupId;
            _decoded[groupId] = rec;
            return rec;
        }

        public int CountMembers(int groupId, bool includeAuxiliary)
        {
            EnsureIndex();
            if (!_members.TryGetValue(groupId, out var list)) return 0;
            int n = 0;
            foreach (var e in list)
            {
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                if (!includeAuxiliary && EntityManager.GetComponentData<PlacemakerMember>(e).Kind == MemberKind.Auxiliary) continue;
                n++;
            }
            return n;
        }

        public void MarkDirty() { _dirty = true; Bump(); }

        private void Bump()
        {
            _version++;
            try { Changed?.Invoke(); } catch (Exception e) { Mod.Log.Warn("Placemaker group listener failed: " + e.Message); }
        }

        // ───────────────────────── index ─────────────────────────

        private void EnsureIndex()
        {
            if (!_dirty) return;
            _dirty = false;
            _records.Clear();
            _members.Clear();
            _allIds.Clear();
            _owners.Clear();
            _ungrouped.Clear();
            using (var records = _recordQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < records.Length; i++)
                {
                    var g = EntityManager.GetComponentData<PlacemakerGroup>(records[i]);
                    if (g.GroupId <= 0) continue;
                    if (_records.ContainsKey(g.GroupId))
                    {
                        // once per id: the index is rebuilt every frame something changes (issue #6: thousands of lines)
                        if (_duplicateWarned.Add(g.GroupId)) Mod.Log.Warn($"Placemaker: duplicate group id {g.GroupId}; keeping the first record.");
                        continue;
                    }
                    _records[g.GroupId] = records[i];
                    if (g.Owner != Entity.Null) _owners[g.GroupId] = g.Owner;
                    if ((g.Flags & (int)GroupFlags.Ungrouped) != 0) _ungrouped.Add(g.GroupId);
                    _allIds.Add(g.GroupId);
                }
            _allIds.Sort();
            foreach (var id in new List<int>(_decoded.Keys)) if (!_records.ContainsKey(id)) _decoded.Remove(id);
            using (var entities = _memberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!_members.TryGetValue(tags[i].GroupId, out var list)) _members[tags[i].GroupId] = list = new List<Entity>();
                    list.Add(entities[i]);
                }
        }

        // ───────────────────────── maintenance ─────────────────────────

        /// <summary>
        /// Members deleted this frame by anyone (player bulldozer, other tools, our own removal).
        /// Carrier SubNet entries of deleted parking aisles are removed now: the carrier has no
        /// Owner-based bookkeeping, and a dangling SubNet entry is read by lane connection jobs.
        /// </summary>
        private void HandleDeletedMembers()
        {
            using var deleted = _deletedMemberQuery.ToEntityArray(Allocator.Temp);
            var touchedGroups = new HashSet<int>();
            for (int i = 0; i < deleted.Length; i++)
            {
                var e = deleted[i];
                var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                touchedGroups.Add(tag.GroupId);
                if (tag.Role == MemberRole.OwnerArea)
                {
                    // The player bulldozed the ONE thing (US7): cascade to every member. Removal marks
                    // Deleted in PreTool of the next frame (RESEARCH-NOTES 11–12), never here.
                    if (TryGetRecordEntity(tag.GroupId, out var ownerRec) && !EntityManager.HasComponent<PlacemakerRemoval>(ownerRec)
                        && !EntityManager.HasComponent<PlacemakerUnderConstruction>(ownerRec) && IsUngrouped(tag.GroupId))
                        Mod.Log.Warn($"Placemaker: the owner area of ungrouped group {tag.GroupId} was deleted by another tool; its parts stay.");
                    else if (TryGetRecordEntity(tag.GroupId, out ownerRec) && !EntityManager.HasComponent<PlacemakerRemoval>(ownerRec)
                        && !EntityManager.HasComponent<PlacemakerUnderConstruction>(ownerRec))
                    {
                        Mod.Log.Info($"Placemaker: owner area of group {tag.GroupId} was bulldozed; removing the whole group.");
                        RemoveInternal(tag.GroupId);
                    }
                    continue;
                }
                if (IsOwnerlessGroupPart(e, out int ownerless) && BulldozerActive())
                {
                    // No owner lot to bulldoze: the player bulldozed a part instead (confirmed by the
                    // bulldoze confirmation patch). Remove the whole group, phase-correct, next PreTool.
                    Mod.Log.Info($"Placemaker: a part of group {ownerless} (no owner lot) was bulldozed; removing the whole group.");
                    RemoveInternal(ownerless);
                    continue;
                }
                if (tag.Kind != MemberKind.Edge) continue;
                if (!_records.TryGetValue(tag.GroupId, out var rec) && !TryGetRecordEntity(tag.GroupId, out rec)) continue;
                if (!EntityManager.Exists(rec)) continue;
                var g = EntityManager.GetComponentData<PlacemakerGroup>(rec);
                if (g.Carrier == Entity.Null || !EntityManager.Exists(g.Carrier) || !EntityManager.HasBuffer<global::Game.Net.SubNet>(g.Carrier)) continue;
                var subNets = EntityManager.GetBuffer<global::Game.Net.SubNet>(g.Carrier);
                for (int k = subNets.Length - 1; k >= 0; k--) if (subNets[k].m_SubNet == e) subNets.RemoveAt(k);
            }
            AdoptReplacements(deleted);
            foreach (var id in touchedGroups)
            {
                if (!TryGetRecordEntity(id, out var rec)) continue;
                if (EntityManager.HasComponent<PlacemakerRemoval>(rec) || EntityManager.HasComponent<PlacemakerUnderConstruction>(rec)) continue;
                var r = GetRecord(id);
                if (r != null && (r.Flags & GroupFlags.Modified) == 0)
                {
                    r.Flags |= GroupFlags.Modified;
                    WriteRecord(rec, r);
                }
            }
            // Empty groups (everything bulldozed by hand) clean up their record and auxiliaries.
            _dirty = true;
            EnsureIndex();
            foreach (var id in touchedGroups)
            {
                if (!_records.TryGetValue(id, out var rec)) continue;
                if (EntityManager.HasComponent<PlacemakerRemoval>(rec) || EntityManager.HasComponent<PlacemakerUnderConstruction>(rec)) continue;
                if (CountLiveNonAuxiliaryExcluding(id) == 0)
                {
                    EntityManager.AddComponentData(rec, new PlacemakerRemoval());
                    Mod.Log.Info($"Placemaker: group {id} lost its last element; removing its record.");
                }
            }
        }

        /// <summary>
        /// Follows vanilla replacements of member edges: when another tool splits one of our edges
        /// (a crossing road) or replaces it (road upgrade creating a new entity), the old edge is
        /// deleted and new untagged edges appear along the same curve in the same frame. Adopting
        /// them keeps the group whole, so Remove still removes everything and no untagged node is
        /// left between two of our edges. Geometry-only match (the prefab may change on upgrades):
        /// both ends and the midpoint must lie on the deleted curve.
        /// </summary>
        private int AdoptReplacements(NativeArray<Entity> deleted)
        {
            if (_createdNetQuery.IsEmptyIgnoreFilter) return 0;
            var lost = new List<(Colossal.Mathematics.Bezier4x3 Curve, PlacemakerMember Tag)>();
            for (int i = 0; i < deleted.Length; i++)
            {
                var e = deleted[i];
                var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                if (tag.Kind != MemberKind.Edge || !EntityManager.HasComponent<global::Game.Net.Curve>(e)) continue;
                if (!TryGetRecordEntity(tag.GroupId, out var rec) || EntityManager.HasComponent<PlacemakerRemoval>(rec)) continue;
                lost.Add((EntityManager.GetComponentData<global::Game.Net.Curve>(e).m_Bezier, tag));
            }
            if (lost.Count == 0) return 0;
            const float tolerance = 0.75f;
            int adopted = 0;
            using var created = _createdNetQuery.ToEntityArray(Allocator.Temp);
            var adoptedEdges = new Dictionary<Entity, int>();
            foreach (var c in created)
            {
                if (!EntityManager.HasComponent<global::Game.Net.Edge>(c) || !EntityManager.HasComponent<global::Game.Net.Curve>(c)) continue;
                var curve = EntityManager.GetComponentData<global::Game.Net.Curve>(c).m_Bezier;
                foreach (var l in lost)
                {
                    if (Colossal.Mathematics.MathUtils.Distance(l.Curve.xz, curve.a.xz, out _) > tolerance
                        || Colossal.Mathematics.MathUtils.Distance(l.Curve.xz, curve.d.xz, out _) > tolerance
                        || Colossal.Mathematics.MathUtils.Distance(l.Curve.xz, Colossal.Mathematics.MathUtils.Position(curve, 0.5f).xz, out _) > tolerance) continue;
                    EntityManager.AddComponentData(c, l.Tag);
                    adoptedEdges[c] = l.Tag.GroupId;
                    if (l.Tag.Role == MemberRole.ParkingAisle && TryGetRecordEntity(l.Tag.GroupId, out var rec))
                    {
                        var g = EntityManager.GetComponentData<PlacemakerGroup>(rec);
                        if (g.Carrier != Entity.Null && EntityManager.Exists(g.Carrier) && EntityManager.HasBuffer<global::Game.Net.SubNet>(g.Carrier))
                            EntityManager.GetBuffer<global::Game.Net.SubNet>(g.Carrier).Add(new global::Game.Net.SubNet(c));
                    }
                    adopted++;
                    break;
                }
            }
            if (adopted == 0) return 0;
            // New nodes whose every edge is ours (the split point between two adopted pieces).
            foreach (var n in created)
            {
                if (!EntityManager.HasComponent<global::Game.Net.Node>(n) || EntityManager.HasComponent<PlacemakerMember>(n)
                    || !EntityManager.HasBuffer<global::Game.Net.ConnectedEdge>(n)) continue;
                var edges = EntityManager.GetBuffer<global::Game.Net.ConnectedEdge>(n, true);
                int group = 0; bool all = edges.Length > 0;
                for (int k = 0; k < edges.Length && all; k++)
                {
                    var edge = edges[k].m_Edge;
                    int g = adoptedEdges.TryGetValue(edge, out var ag) ? ag
                        : EntityManager.HasComponent<PlacemakerMember>(edge) ? EntityManager.GetComponentData<PlacemakerMember>(edge).GroupId : 0;
                    if (g == 0 || (group != 0 && g != group)) all = false;
                    group = g;
                }
                if (all) EntityManager.AddComponentData(n, new PlacemakerMember { GroupId = group, Kind = MemberKind.Node, Role = MemberRole.Generic });
            }
            Mod.Log.Info($"Placemaker kept {adopted} replaced/split edges in their group.");
            return adopted;
        }

        private int CountLiveNonAuxiliaryExcluding(int groupId)
        {
            if (!_members.TryGetValue(groupId, out var list)) return 0;
            int n = 0;
            foreach (var e in list)
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e)
                    && EntityManager.GetComponentData<PlacemakerMember>(e).Kind != MemberKind.Auxiliary) n++;
            return n;
        }

        /// <summary>
        /// After a load: (1) tags whose record is gone (save edited without the mod, or a record
        /// lost to a crash) are removed so the entities degrade to plain vanilla objects; (2) the
        /// carrier SubNet that CS2 does not persist for owner-less edges is rebuilt (PLT measured
        /// 109 → 0 entries after load); (3) records are decoded once.
        /// </summary>
        private void PostLoadRepair()
        {
            DropStaleRecords();
            _dirty = true;
            EnsureIndex();
            foreach (var id in _ungrouped) _releasePending.Add(id);   // builds ungrouped before their props were released (or in an older build)
            int orphans = 0;
            var orphanEntities = new List<Entity>();
            foreach (var kv in _members)
                if (!_records.ContainsKey(kv.Key)) orphanEntities.AddRange(kv.Value);
            foreach (var e in orphanEntities)
            {
                if (!EntityManager.Exists(e)) continue;
                EntityManager.RemoveComponent<PlacemakerMember>(e);
                orphans++;
            }
            if (orphans > 0)
                Mod.Log.Warn($"Placemaker: {orphans} built elements reference groups that no longer exist (saved without Placemaker?). They stay as normal city objects.");

            int restored = 0;
            foreach (var kv in _records)
            {
                var rec = kv.Value;
                var g = EntityManager.GetComponentData<PlacemakerGroup>(rec);
                if (g.Carrier != Entity.Null && !EntityManager.Exists(g.Carrier)) { g.Carrier = Entity.Null; EntityManager.SetComponentData(rec, g); }
                if (g.Companion != Entity.Null && !EntityManager.Exists(g.Companion)) { g.Companion = Entity.Null; EntityManager.SetComponentData(rec, g); }
                if (g.Owner != Entity.Null)
                {
                    if (!EntityManager.Exists(g.Owner) || !EntityManager.HasComponent<global::Game.Areas.Area>(g.Owner))
                    {
                        g.Owner = Entity.Null;
                        EntityManager.SetComponentData(rec, g);
                        _owners.Remove(kv.Key);
                    }
                    else EnsureOwnerBuffers(g.Owner);
                }
                if (g.Carrier == Entity.Null || !_members.TryGetValue(kv.Key, out var list)) continue;
                if (!EntityManager.HasBuffer<global::Game.Net.SubNet>(g.Carrier)) EntityManager.AddBuffer<global::Game.Net.SubNet>(g.Carrier);
                if (!EntityManager.HasBuffer<global::Game.Objects.SubObject>(g.Carrier)) EntityManager.AddBuffer<global::Game.Objects.SubObject>(g.Carrier);
                var subNets = EntityManager.GetBuffer<global::Game.Net.SubNet>(g.Carrier);
                var present = new HashSet<Entity>();
                for (int i = subNets.Length - 1; i >= 0; i--)
                {
                    var s = subNets[i].m_SubNet;
                    if (!EntityManager.Exists(s) || EntityManager.HasComponent<Deleted>(s)) subNets.RemoveAt(i);
                    else present.Add(s);
                }
                foreach (var m in list)
                {
                    var tag = EntityManager.GetComponentData<PlacemakerMember>(m);
                    if (tag.Role != MemberRole.ParkingAisle || tag.Kind != MemberKind.Edge || present.Contains(m)) continue;
                    subNets = EntityManager.GetBuffer<global::Game.Net.SubNet>(g.Carrier);
                    subNets.Add(new global::Game.Net.SubNet(m));
                    restored++;
                }
            }
            if (restored > 0) Mod.Log.Info($"Placemaker restored {restored} parking aisle links after load.");
            RestoreOwnHidden();
            foreach (var id in _records.Keys) GetRecord(id);
            SyncParkStats();
            Mod.Log.Info($"Placemaker loaded {_records.Count} groups.");
            Bump();
        }

        /// <summary>
        /// (GitHub issue #6) A city saved after it was loaded a second time in one game (builds before OwnEntityClearSystem)
        /// holds two records of each of its groups; one saved after another city was loaded holds that city's records too.
        /// The copies point at nothing (their owner lot, carrier and companion were the earlier load's) and every member tag
        /// fits both. Per id the record with live references stays (on a tie, the one whose outline holds more of the
        /// members); the others go, and so does every record no member carries. Destroyed outright, never removed: a
        /// removal would take the members with it.
        /// </summary>
        private void DropStaleRecords()
        {
            var byId = new Dictionary<int, List<Entity>>();
            using (var records = _recordQuery.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < records.Length; i++)
                {
                    int id = EntityManager.GetComponentData<PlacemakerGroup>(records[i]).GroupId;
                    if (!byId.TryGetValue(id, out var list)) byId[id] = list = new List<Entity>();
                    list.Add(records[i]);
                }
            if (byId.Count == 0) return;
            var members = new Dictionary<int, List<Entity>>();
            using (var entities = _memberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!members.TryGetValue(tags[i].GroupId, out var list)) members[tags[i].GroupId] = list = new List<Entity>();
                    list.Add(entities[i]);
                }
            var drop = new List<Entity>();
            int copies = 0, empty = 0;
            foreach (var kv in byId)
            {
                if (!members.TryGetValue(kv.Key, out var own))
                {
                    drop.AddRange(kv.Value);
                    empty += kv.Value.Count;
                    continue;
                }
                if (kv.Value.Count == 1) continue;
                Entity keep = Entity.Null;
                int bestRefs = -1, bestInside = -1;
                foreach (var r in kv.Value)
                {
                    int refs = LiveReferences(r), inside = MembersInsideOutline(r, own);
                    if (refs > bestRefs || (refs == bestRefs && inside > bestInside)) { keep = r; bestRefs = refs; bestInside = inside; }
                }
                foreach (var r in kv.Value) if (r != keep) { drop.Add(r); copies++; }
            }
            if (drop.Count == 0) return;
            foreach (var r in drop) EntityManager.DestroyEntity(r);
            Mod.Log.Warn($"Placemaker load: {copies} duplicate group record(s) and {empty} record(s) of groups this city does not have were dropped "
                + "(the city was saved after a second load in the same game; GitHub issue #6).");
        }

        /// <summary>A record's live references: owner lot 4, carrier 2, companion 1.</summary>
        private int LiveReferences(Entity record)
        {
            var g = EntityManager.GetComponentData<PlacemakerGroup>(record);
            bool Live(Entity e) => e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);
            return (Live(g.Owner) && EntityManager.HasComponent<global::Game.Areas.Area>(g.Owner) ? 4 : 0)
                + (Live(g.Carrier) ? 2 : 0) + (Live(g.Companion) ? 1 : 0);
        }

        /// <summary>How many of the members (up to 200) stand inside the outline of this record's receipt.</summary>
        private int MembersInsideOutline(Entity record, List<Entity> members)
        {
            if (!EntityManager.HasBuffer<PlacemakerGroupBlob>(record)) return 0;
            var buffer = EntityManager.GetBuffer<PlacemakerGroupBlob>(record, true);
            var bytes = new byte[buffer.Length];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = buffer[i].Value;
            var ring = ReceiptCodec.Decode(bytes)?.Area?.Ring2D();
            if (ring == null || ring.Length < 3) return 0;
            int inside = 0;
            for (int i = 0; i < members.Count && i < 200; i++)
            {
                var m = members[i];
                Unity.Mathematics.float2 p;
                if (EntityManager.HasComponent<global::Game.Objects.Transform>(m)) p = EntityManager.GetComponentData<global::Game.Objects.Transform>(m).m_Position.xz;
                else if (EntityManager.HasComponent<global::Game.Net.Node>(m)) p = EntityManager.GetComponentData<global::Game.Net.Node>(m).m_Position.xz;
                else if (EntityManager.HasComponent<global::Game.Net.Curve>(m))
                    p = Colossal.Mathematics.MathUtils.Position(EntityManager.GetComponentData<global::Game.Net.Curve>(m).m_Bezier, 0.5f).xz;
                else continue;
                if (Placemaker.Core.Geometry.Poly.Contains(ring, p)) inside++;
            }
            return inside;
        }

        /// <summary>
        /// (s58) After a load: an object of a build that shares the group owner (the carrier) is never hidden (Overridden) by our own
        /// pieces, so one that is hidden was hidden by an older overlap pass or a stale state. The ones that sit clear of every other
        /// building are shown again (BatchesUpdated, no Updated: that would re-run the override check). Guarded: setting "Ignore
        /// collisions in my builds", owner == the group's carrier, and no other building within its lot radius.
        /// </summary>
        private void RestoreOwnHidden()
        {
            try
            {
                if (!Placemaker.Game.Build.BuildService.IgnoreOwnCollisions) return;
                var q = GetEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<Overridden>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                    ComponentType.ReadOnly<Owner>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                if (q.IsEmptyIgnoreFilter) return;
                var bq = GetEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                    ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>());
                var near = new List<(Unity.Mathematics.float2 p, float r)>();
                using (var buildings = bq.ToEntityArray(Allocator.Temp))
                    foreach (var b in buildings)
                    {
                        if (EntityManager.HasComponent<PlacemakerMember>(b)) continue;
                        float r = 12f;
                        try
                        {
                            var prefab = EntityManager.GetComponentData<global::Game.Prefabs.PrefabRef>(b).m_Prefab;
                            if (EntityManager.HasComponent<global::Game.Prefabs.BuildingData>(prefab))
                            {
                                var lot = EntityManager.GetComponentData<global::Game.Prefabs.BuildingData>(prefab).m_LotSize;
                                r = Unity.Mathematics.math.max(lot.x, lot.y) * 4f + 1f;
                            }
                        }
                        catch (Exception) { }
                        near.Add((EntityManager.GetComponentData<global::Game.Objects.Transform>(b).m_Position.xz, r));
                    }
                int shown = 0, kept = 0;
                using var entities = q.ToEntityArray(Allocator.Temp);
                foreach (var e in entities)
                {
                    var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                    if (tag.Kind != MemberKind.Object || tag.Role == MemberRole.ParkingBay) continue;
                    if (!_records.TryGetValue(tag.GroupId, out var rec)) continue;
                    var carrier = EntityManager.GetComponentData<PlacemakerGroup>(rec).Carrier;
                    if (carrier == Entity.Null || EntityManager.GetComponentData<Owner>(e).m_Owner != carrier) continue;
                    var pos = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                    bool blocked = false;
                    foreach (var b in near) if (Unity.Mathematics.math.distance(b.p, pos) < b.r) { blocked = true; break; }
                    if (blocked) { kept++; continue; }
                    EntityManager.RemoveComponent<Overridden>(e);
                    EntityManager.AddComponent<BatchesUpdated>(e);
                    shown++;
                }
                if (shown > 0 || kept > 0) Mod.Log.Info($"Placemaker load: {shown} hidden object(s) of our own builds shown again, {kept} stay hidden (next to another building).");
            }
            catch (Exception ex) { Mod.Log.Warn("Placemaker: restoring hidden objects after load failed: " + ex.Message); }
        }

        private void CheckMissingAssets()
        {
            var catalog = Services.Assets as Assets.AssetCatalogSystem;
            if (catalog == null || !catalog.Ready || catalog.Version == _missingCheckedCatalogVersion) return;
            _missingCheckedCatalogVersion = catalog.Version;
            EnsureIndex();
            _missing.Clear();
            foreach (var kv in _records)
            {
                var rec = GetRecord(kv.Key);
                if (rec == null) continue;
                var missing = new List<string>();
                foreach (var name in rec.PrefabNames)
                    if (!string.IsNullOrEmpty(name) && !catalog.PrefabExists(name)
                        // (issue #2) see-through decal copies are gone for good: OldDecalCleanupSystem removes them after a load
                        && !name.StartsWith(Core.Decals.DecalLook.ClonePrefix, StringComparison.Ordinal)) missing.Add(name);
                bool was = (rec.Flags & GroupFlags.MissingAssets) != 0;
                if (missing.Count > 0) _missing[kv.Key] = missing;
                if (was != (missing.Count > 0))
                {
                    if (missing.Count > 0) rec.Flags |= GroupFlags.MissingAssets; else rec.Flags &= ~GroupFlags.MissingAssets;
                    WriteRecord(kv.Value, rec);
                }
                if (missing.Count > 0)
                    Mod.Log.Warn($"Placemaker group {kv.Key} uses {missing.Count} assets that are not loaded (e.g. '{missing[0]}'); offer repair via rebuild.");
            }
        }
    }
}
