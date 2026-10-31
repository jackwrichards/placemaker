// Placemaker — phase-correct removal of a whole group (GPL-3.0).
// Ordering comes from Core/Build/RemovalPlanner; the engine reasons are from ParkingLotTool's
// measured crash notes (KruemelGames, GPL-3.0) and ParkManager's ParkBundleCleanup (tomkpunkt):
//
// Ungroup's dissolve runs here too (GroupRegistrySystem.RunDissolves).
//
// Phase: PreTool. Deleted marked here is seen by SubElementDeleteSystem (PostTool, cascades to
// sub-objects/lanes of the edge), by Game.Net.ReferencesSystem (Modification2B, removes the edge
// from its nodes' ConnectedEdge buffers) and by everything after it. Marking later than 2B leaves
// dangling node references → native crash (RESEARCH-NOTES 11–12).

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Tools;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace Placemaker.Game.Groups
{
    public sealed partial class GroupRemovalSystem : GameSystemBase
    {
        private const int MaxPassesWithoutProgress = 600;
        private EntityQuery _requestQuery;
        private EntityQuery _memberQuery;
        private const int ToolReleaseFrames = 30;
        private GroupRegistrySystem _registry;
        private ToolSystem _toolSystem;
        private EntityQuery _tempQuery;
        /// <summary>Per record being removed: when it started, its frames, its main-thread time (logged when it ends).</summary>
        /// <summary>
        /// (p1-bulldoze) Each removal's members, found once: a pass used to scan every tagged piece of every group in the city,
        /// every frame. Refreshed by a full scan when the plan says nothing is left (members adopted meanwhile) and every
        /// <see cref="RescanPasses"/> passes.
        /// </summary>
        private readonly Dictionary<Entity, List<Entity>> _members = new Dictionary<Entity, List<Entity>>();
        private const int RescanPasses = 60;
        private readonly Dictionary<Entity, (System.Diagnostics.Stopwatch Wall, int Frames, double Ms, double MaxMs, int Members, float WorstFrame)> _timing
            = new Dictionary<Entity, (System.Diagnostics.Stopwatch, int, double, double, int, float)>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _registry = World.GetOrCreateSystemManaged<GroupRegistrySystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _tempQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _requestQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerGroup>(), ComponentType.ReadWrite<PlacemakerRemoval>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _memberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PlacemakerMember>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            // keyed by entity: a load reuses entity ids, so nothing carries over (a removal saved half done starts afresh)
            _members.Clear();
            _timing.Clear();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // Ungroup: builds dissolved into plain city pieces, in this phase for the same reasons (GroupRegistrySystem.Dissolve.cs)
            if (_registry.HasPendingDissolves) _registry.RunDissolves();
            else if (_registry.HasPendingUninstall) _registry.RunUninstallStrip();   // "ready to remove Placemaker", after its ungroups
            if (_requestQuery.IsEmptyIgnoreFilter) return;
            using var requests = _requestQuery.ToEntityArray(Allocator.Temp);
            if (requests.Length == 0) return;
            // One group per frame: each pass is ≤ 32 net parts, or one batch of objects (RemovalPlanner).
            var record = requests[0];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                ProcessGroup(record);
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker group removal pass failed; will retry next frame");
                var r = EntityManager.GetComponentData<PlacemakerRemoval>(record);
                r.Passes++;
                EntityManager.SetComponentData(record, r);
                if (r.Passes > MaxPassesWithoutProgress) GiveUp(record);
            }
            Time(record, sw.Elapsed.TotalMilliseconds);
        }

        private void Time(Entity record, double ms)
        {
            if (!_timing.TryGetValue(record, out var t)) t = (System.Diagnostics.Stopwatch.StartNew(), 0, 0, 0, 0, 0f);
            t.Frames++; t.Ms += ms; if (ms > t.MaxMs) t.MaxMs = ms;
            if (t.Frames > 1) t.WorstFrame = Math.Max(t.WorstFrame, UnityEngine.Time.unscaledDeltaTime * 1000f);   // the game's whole previous frame
            if (EntityManager.Exists(record) && !EntityManager.HasComponent<Deleted>(record)) { _timing[record] = t; return; }
            _timing.Remove(record);
            _members.Remove(record);
            Mod.Log.Info($"Placemaker removal timing: {t.Members} member(s) in {t.Frames} frame(s), {t.Wall.Elapsed.TotalMilliseconds:F0} ms from start to end, "
                + $"{t.Ms:F1} ms of main-thread work, worst frame {t.MaxMs:F1} ms (the game's longest frame meanwhile {t.WorstFrame:F0} ms).");
        }

        private void NoteMembers(Entity record, int members)
        {
            if (!_timing.TryGetValue(record, out var t)) t = (System.Diagnostics.Stopwatch.StartNew(), 0, 0, 0, 0, 0f);
            if (members > t.Members) t.Members = members;
            _timing[record] = t;
        }

        private void ProcessGroup(Entity record)
        {
            var group = EntityManager.GetComponentData<PlacemakerGroup>(record);
            var removal = EntityManager.GetComponentData<PlacemakerRemoval>(record);

            bool full = !_members.ContainsKey(record) || removal.Passes > 0 && removal.Passes % RescanPasses == 0;
            var entities = Members(record, group.GroupId, full, out bool anyTemp);
            if (!full && entities.Count == 0 && !anyTemp) entities = Members(record, group.GroupId, true, out anyTemp);
            if (anyTemp && entities.Count == 0) return;
            NoteMembers(record, entities.Count);

            // Crash guard (playtest 4b: native crash right after bulldozing a group): a tool's temporary
            // copy (Temp.m_Original) of a member — e.g. the bulldozer still hovering a path, or a node
            // preview — keeps referencing it. Deleting the original under it leaves the tool's jobs
            // reading a dead entity. Wait for the tool to drop its copies; if it does not within
            // ToolReleaseFrames, switch to the default tool once (its next Clear removes them).
            if (ReferencedByTemporaries(entities))
            {
                removal.Passes++;
                // never the build's own tool: a build in progress releases its temporaries when it ends (switching away breaks it)
                if (removal.Passes == ToolReleaseFrames && _toolSystem.activeTool != null && !(_toolSystem.activeTool is DefaultToolSystem)
                    && !(_toolSystem.activeTool is global::Placemaker.Game.Build.BuildToolSystem))
                {
                    Mod.Log.Info($"Placemaker: a tool still previews parts of group {group.GroupId}; switching to the default tool to remove it safely.");
                    _toolSystem.activeTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
                }
                if (removal.Passes > MaxPassesWithoutProgress) { GiveUp(record); return; }
                EntityManager.SetComponentData(record, removal);
                return;
            }

            var byId = new Dictionary<long, Entity>(entities.Count);
            var members = new List<RemovalMember>(entities.Count);
            var nodeEdges = new Dictionary<long, List<long>>();
            foreach (var e in entities)
            {
                long id = Id(e);
                byId[id] = e;
                var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                var kind = Classify(e, tag);
                var m = new RemovalMember { Id = id, Kind = kind };
                if (kind == MemberKind.Edge)
                {
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    m.StartNode = Id(edge.m_Start);
                    m.EndNode = Id(edge.m_End);
                }
                else if (kind == MemberKind.Node)
                {
                    var list = new List<long>();
                    if (EntityManager.HasBuffer<ConnectedEdge>(e))
                    {
                        var buf = EntityManager.GetBuffer<ConnectedEdge>(e, true);
                        for (int i = 0; i < buf.Length; i++)
                        {
                            var ce = buf[i].m_Edge;
                            if (ce == Entity.Null || !EntityManager.Exists(ce) || EntityManager.HasComponent<Deleted>(ce)) continue;
                            list.Add(Id(ce));
                        }
                    }
                    nodeEdges[id] = list;
                }
                members.Add(m);
            }

            var pass = RemovalPlanner.NextPass(members, nodeEdges);
            var refresh = new HashSet<Entity>();
            var doomed = new List<Entity>(pass.Delete.Count);
            var seen = new HashSet<Entity>();
            foreach (var id in pass.Delete)
            {
                if (!byId.TryGetValue(id, out var e) || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) continue;   // idempotent
                if (EntityManager.HasComponent<Edge>(e))
                {
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    refresh.Add(edge.m_Start);
                    refresh.Add(edge.m_End);
                }
                PrepareDelete(e);
                if (seen.Add(e)) doomed.Add(e);
                AddOwnedSubObjects(e, doomed, seen);
            }
            if (doomed.Count > 0)
            {
                // one structural change for the whole batch
                using var batch = new NativeArray<Entity>(doomed.ToArray(), Allocator.Temp);
                EntityManager.AddComponent<Deleted>(batch);
            }
            foreach (var id in pass.Release)
                if (byId.TryGetValue(id, out var e) && EntityManager.Exists(e))
                {
                    EntityManager.RemoveComponent<PlacemakerMember>(e);
                    refresh.Add(e);
                }
            RefreshSurvivors(refresh);

            if (pass.Delete.Count > 0 || pass.Release.Count > 0) removal.Passes = 0;
            else removal.Passes++;

            if (pass.Final && !anyTemp)
            {
                // Record goes one pass after the last member so both cascades never stack (PM).
                if (removal.Passes >= 1)
                {
                    // (p1-bulldoze) its ground paint wiped first (BuildService.Unpaint), a stamp per frame; the record waits
                    if (!Unpainted(group.GroupId))
                    {
                        removal.Passes = 1;
                        EntityManager.SetComponentData(record, removal);
                        return;
                    }
                    EntityManager.AddComponent<Deleted>(record);
                    Mod.Log.Info($"Placemaker removed group {group.GroupId}.");
                    ReportLeftovers(group.GroupId);
                    _registry.MarkDirty();
                    return;
                }
            }
            if (removal.Passes > MaxPassesWithoutProgress) { GiveUp(record); return; }
            EntityManager.SetComponentData(record, removal);
            _registry.MarkDirty();
        }

        /// <summary>True when the group's ground paint is wiped, or it had none (only builds that painted or terraformed carry PermanentTerrain).</summary>
        private bool Unpainted(int groupId)
        {
            var rec = _registry.GetRecord(groupId);
            if (rec == null || (rec.Flags & GroupFlags.PermanentTerrain) == 0 || rec.Area?.Ring == null) return true;
            return !(Services.Build is global::Placemaker.Game.Build.BuildService build) || build.StepUnpaint(groupId, rec.Area.Ring);
        }

        /// <summary>This group's live members (not Temp copies); <paramref name="full"/> scans every tagged piece, else the cached list is filtered.</summary>
        private List<Entity> Members(Entity record, int groupId, bool full, out bool anyTemp)
        {
            anyTemp = false;
            var entities = new List<Entity>();
            if (full)
            {
                using (var all = _memberQuery.ToEntityArray(Allocator.Temp))
                using (var tags = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (tags[i].GroupId != groupId) continue;
                        // A member currently being edited by a tool (Temp copy) would race with the tool.
                        if (EntityManager.HasComponent<Temp>(all[i])) { anyTemp = true; continue; }
                        entities.Add(all[i]);
                    }
                _members[record] = new List<Entity>(entities);
                return entities;
            }
            var list = _members[record];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || !EntityManager.HasComponent<PlacemakerMember>(e)
                    || EntityManager.GetComponentData<PlacemakerMember>(e).GroupId != groupId) { list.RemoveAt(i); continue; }
                if (EntityManager.HasComponent<Temp>(e)) { anyTemp = true; continue; }
                entities.Add(e);
            }
            return entities;
        }

        /// <summary>
        /// (p1-bulldoze) The sub-objects an object member owns: the decorations of a kiosk, food stall or barn (vents, signs,
        /// racks, wall lights, spawn spots). The game deletes the sub-objects of a deleted building, but an owned object that is
        /// not a building is skipped (SubObjectSystem returns early for Object + Owner without Building), and our members are
        /// owned by the carrier. Marking the member Deleted alone left them standing (fairground, market square, farmstead,
        /// beach, sports park, junk yard). Recursive; other members and secondary objects go their own way.
        /// </summary>
        private void AddOwnedSubObjects(Entity owner, List<Entity> into, HashSet<Entity> seen, int depth = 0)
        {
            if (depth > 4 || !EntityManager.HasComponent<global::Game.Objects.Object>(owner) || !EntityManager.HasBuffer<global::Game.Objects.SubObject>(owner)) return;
            var buf = EntityManager.GetBuffer<global::Game.Objects.SubObject>(owner, true);
            if (buf.Length == 0) return;
            var subs = new List<Entity>(buf.Length);
            for (int i = 0; i < buf.Length; i++) subs.Add(buf[i].m_SubObject);
            foreach (var sub in subs)
            {
                if (sub == Entity.Null || !EntityManager.Exists(sub) || EntityManager.HasComponent<Deleted>(sub) || EntityManager.HasComponent<Temp>(sub)
                    || EntityManager.HasComponent<PlacemakerMember>(sub) || EntityManager.HasComponent<global::Game.Objects.Secondary>(sub)) continue;
                if (!EntityManager.HasComponent<Owner>(sub) || EntityManager.GetComponentData<Owner>(sub).m_Owner != owner) continue;
                if (!seen.Add(sub)) continue;
                into.Add(sub);
                AddOwnedSubObjects(sub, into, seen, depth + 1);
            }
        }

        private bool ReferencedByTemporaries(List<Entity> members)
        {
            if (_tempQuery.IsEmptyIgnoreFilter) return false;
            var set = new HashSet<Entity>(members);
            using var temps = _tempQuery.ToComponentDataArray<Temp>(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
                if (temps[i].m_Original != Entity.Null && set.Contains(temps[i].m_Original)) return true;
            return false;
        }

        private MemberKind Classify(Entity e, PlacemakerMember tag)
        {
            // Trust live components over the stored kind: the tag survives replacements by tools.
            if (EntityManager.HasComponent<Edge>(e)) return MemberKind.Edge;
            if (EntityManager.HasComponent<global::Game.Net.Node>(e)) return MemberKind.Node;
            if (tag.Kind == MemberKind.Auxiliary) return MemberKind.Auxiliary;
            if (EntityManager.HasComponent<global::Game.Areas.Area>(e)) return MemberKind.Area;
            if (EntityManager.HasComponent<global::Game.Objects.Object>(e)) return MemberKind.Object;
            return MemberKind.Other;
        }

        /// <summary>Per-kind preparation before Deleted (PLT lessons).</summary>
        private void PrepareDelete(Entity e)
        {
            var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
            if (tag.Role == MemberRole.Companion && EntityManager.HasComponent<global::Game.Companies.WorkProvider>(e))
            {
                // Employees leave cleanly instead of pointing at a dead workplace.
                var wp = EntityManager.GetComponentData<global::Game.Companies.WorkProvider>(e);
                wp.m_MaxWorkers = 0;
                EntityManager.SetComponentData(e, wp);
            }
            if (EntityManager.HasBuffer<global::Game.Net.SubLane>(e) && tag.Role == MemberRole.ParkingBay)
            {
                // Parking lanes owned by the decal go with it this frame (no orphan lane for LaneSystem).
                var lanes = EntityManager.GetBuffer<global::Game.Net.SubLane>(e, true);
                var copy = new List<Entity>(lanes.Length);
                for (int i = 0; i < lanes.Length; i++) copy.Add(lanes[i].m_SubLane);
                foreach (var lane in copy)
                    if (EntityManager.Exists(lane) && !EntityManager.HasComponent<Deleted>(lane))
                        EntityManager.AddComponent<Deleted>(lane);
            }
        }

        /// <summary>
        /// Surviving nodes (foreign split nodes, released nodes) and their remaining edges get
        /// Updated so CS2 recomputes node geometry/composition once our edges are gone.
        /// The ConnectedEdge buffer is copied first: structural changes invalidate it.
        /// </summary>
        private void RefreshSurvivors(HashSet<Entity> nodes)
        {
            foreach (var n in nodes)
            {
                if (n == Entity.Null || !EntityManager.Exists(n) || EntityManager.HasComponent<Deleted>(n)
                    || EntityManager.HasComponent<Temp>(n) || !EntityManager.HasComponent<global::Game.Net.Node>(n)) continue;
                var edges = new List<Entity>();
                if (EntityManager.HasBuffer<ConnectedEdge>(n))
                {
                    var buf = EntityManager.GetBuffer<ConnectedEdge>(n, true);
                    for (int i = 0; i < buf.Length; i++) edges.Add(buf[i].m_Edge);
                }
                EntityManager.AddComponent<Updated>(n);
                foreach (var edge in edges)
                    if (EntityManager.Exists(edge) && !EntityManager.HasComponent<Deleted>(edge) && !EntityManager.HasComponent<Temp>(edge))
                        EntityManager.AddComponent<Updated>(edge);
            }
        }

        /// <summary>
        /// Playtest 4b: removal must account for everything. Anything still tagged with the group after
        /// its record goes (a member a tool held as Temp copy, a kind removal does not handle) is
        /// logged per kind and released, so it never points at a dead group.
        /// </summary>
        private void ReportLeftovers(int groupId)
        {
            var left = new Dictionary<MemberKind, int>();
            var release = new List<Entity>();
            using (var all = _memberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < all.Length; i++)
                {
                    if (tags[i].GroupId != groupId) continue;
                    if (EntityManager.HasComponent<Deleted>(all[i])) continue;
                    var k = Classify(all[i], tags[i]);
                    left[k] = (left.TryGetValue(k, out var n) ? n : 0) + 1;
                    if (!EntityManager.HasComponent<Temp>(all[i])) release.Add(all[i]);
                }
            if (left.Count == 0) return;
            var parts = new List<string>();
            foreach (var kv in left) parts.Add($"{kv.Value} {kv.Key}");
            Mod.Log.Warn($"Placemaker could not remove everything of group {groupId}: {string.Join(", ", parts)} left in the city.");
            foreach (var e in release) EntityManager.RemoveComponent<PlacemakerMember>(e);
        }

        private void GiveUp(Entity record)
        {
            // Something keeps the remaining members alive (e.g. edited forever by another tool).
            // Drop the record and leave the rest as vanilla objects rather than looping forever.
            var g = EntityManager.GetComponentData<PlacemakerGroup>(record);
            Mod.Log.Warn($"Placemaker could not finish removing group {g.GroupId}; remaining elements stay in the city.");
            using (var all = _memberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = _memberQuery.ToComponentDataArray<PlacemakerMember>(Allocator.Temp))
                for (int i = 0; i < all.Length; i++)
                    if (tags[i].GroupId == g.GroupId && !EntityManager.HasComponent<Temp>(all[i]))
                        EntityManager.RemoveComponent<PlacemakerMember>(all[i]);
            EntityManager.AddComponent<Deleted>(record);
            _registry.MarkDirty();
        }

        internal static long Id(Entity e) => e == Entity.Null ? 0 : ((long)e.Version << 32) | (uint)e.Index;
    }
}
