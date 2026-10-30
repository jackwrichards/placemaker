// Placemaker — recognising our temporaries, game validation, adoption into the group (GPL-3.0).
// Error capture adapted from ParkManager's ParkBuildValidation (tomkpunkt, GPL-3.0). (s59) The error-to-piece
// exclusion/retry loop is gone: BuildService.Verdicts decides once.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private const float NetMatchTolerance = 1.0f;
        private const float ObjectMatchTolerance = 0.75f;
        private const float AreaMatchTolerance = 2.0f;
        // (perf) Index cells sized to the match tolerance: 16 m cells made every match of a dense lot scan a 48 m square (~150
        // pieces); the census and the tagging of a 5 000-piece lot took ~85 ms each.
        private const float NetIndexCell = 8f, ObjectIndexCell = 2f, AreaIndexCell = 4f;
        private const int MaxErrorMarkers = 64;

        private readonly List<Entity> _tagged = new List<Entity>();
        private string _errorKey = BuildErrorKeys.Other;
        private object[] _errorArgs = Array.Empty<object>();
        private string _errorTypeName = "";

        private struct CensusResult { public int Expected, Matched; }

        // (perf) Census and TagTemporaries read a few components of every temporary (thousands for a big lot): EntityManager.Get /
        // Has complete the jobs and look the entity up again on every call (~90 ms a frame for 5 000 pieces). Inside those
        // loops the reads go through component lookups instead, the jobs completed once (FastReads). No structural change
        // happens while they are on (TagTemporaries defers its own).
        private bool _fast;
        private ComponentLookup<Temp> _lkTemp;
        private ComponentLookup<PrefabRef> _lkPrefab;
        private ComponentLookup<Edge> _lkEdge;
        private ComponentLookup<Curve> _lkCurve;
        private ComponentLookup<global::Game.Net.Node> _lkNode;
        private ComponentLookup<Owner> _lkOwner;
        private ComponentLookup<global::Game.Objects.Object> _lkObject;
        private ComponentLookup<global::Game.Objects.Transform> _lkTransform;
        private ComponentLookup<global::Game.Areas.Area> _lkArea;
        private BufferLookup<global::Game.Areas.Node> _lkAreaNodes;

        private void InitFastReads()
        {
            _lkTemp = GetComponentLookup<Temp>(true);
            _lkPrefab = GetComponentLookup<PrefabRef>(true);
            _lkEdge = GetComponentLookup<Edge>(true);
            _lkCurve = GetComponentLookup<Curve>(true);
            _lkNode = GetComponentLookup<global::Game.Net.Node>(true);
            _lkOwner = GetComponentLookup<Owner>(true);
            _lkObject = GetComponentLookup<global::Game.Objects.Object>(true);
            _lkTransform = GetComponentLookup<global::Game.Objects.Transform>(true);
            _lkArea = GetComponentLookup<global::Game.Areas.Area>(true);
            _lkAreaNodes = GetBufferLookup<global::Game.Areas.Node>(true);
        }

        /// <summary>Lookups current for this frame and every job writing what they read completed: _fast on until FastReadsEnd.</summary>
        private void FastReads()
        {
            EntityManager.CompleteDependencyBeforeRO<Temp>();
            EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
            EntityManager.CompleteDependencyBeforeRO<Edge>();
            EntityManager.CompleteDependencyBeforeRO<Curve>();
            EntityManager.CompleteDependencyBeforeRO<global::Game.Net.Node>();
            EntityManager.CompleteDependencyBeforeRO<Owner>();
            EntityManager.CompleteDependencyBeforeRO<global::Game.Objects.Object>();
            EntityManager.CompleteDependencyBeforeRO<global::Game.Objects.Transform>();
            EntityManager.CompleteDependencyBeforeRO<global::Game.Areas.Area>();
            EntityManager.CompleteDependencyBeforeRO<global::Game.Areas.Node>();
            _lkTemp.Update(this); _lkPrefab.Update(this); _lkEdge.Update(this); _lkCurve.Update(this); _lkNode.Update(this);
            _lkOwner.Update(this); _lkObject.Update(this); _lkTransform.Update(this); _lkArea.Update(this); _lkAreaNodes.Update(this);
            _fast = true;
        }

        private void FastReadsEnd() => _fast = false;

        private bool IsOwnCandidate(Entity e, out Temp temp)
        {
            temp = _fast ? _lkTemp[e] : EntityManager.GetComponentData<Temp>(e);
            if (temp.m_Original != Entity.Null || (temp.m_Flags & (TempFlags.Delete | TempFlags.Cancel)) != 0) return false;
            return true;
        }

        private bool TryMatch(Entity e, out long key, out WorkKind kind)
        {
            if (_fast) return TryMatchFast(e, out key, out kind);
            key = 0; kind = WorkKind.Net;
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            long pid = PrefabId(prefab);
            if (EntityManager.HasComponent<Edge>(e))
            {
                if (_netIndex == null || !EntityManager.HasComponent<Curve>(e)) return false;
                var mid = MathUtils.Position(EntityManager.GetComponentData<Curve>(e).m_Bezier, 0.5f);
                kind = WorkKind.Net;
                return _netIndex.TryMatch(mid.xz, pid, NetMatchTolerance, out key, out _);
            }
            if (EntityManager.HasComponent<global::Game.Net.Node>(e)) return false;
            if (EntityManager.HasComponent<Owner>(e)) return false;   // sub-elements go with their owner
            if (EntityManager.HasComponent<global::Game.Objects.Object>(e) && EntityManager.HasComponent<global::Game.Objects.Transform>(e))
            {
                if (_objectIndex == null) return false;
                var p = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                kind = WorkKind.Object;
                return _objectIndex.TryMatch(p.xz, pid, ObjectMatchTolerance, out key, out _);
            }
            if (EntityManager.HasComponent<global::Game.Areas.Area>(e) && EntityManager.HasBuffer<global::Game.Areas.Node>(e))
            {
                if (_areaIndex == null) return false;
                kind = WorkKind.Area;
                return _areaIndex.TryMatch(AreaCentroid(e), pid, AreaMatchTolerance, out key, out _);
            }
            return false;
        }

        /// <summary>TryMatch through the lookups (the same tests in the same order).</summary>
        private bool TryMatchFast(Entity e, out long key, out WorkKind kind)
        {
            key = 0; kind = WorkKind.Net;
            long pid = PrefabId(_lkPrefab[e].m_Prefab);
            if (_lkEdge.HasComponent(e))
            {
                if (_netIndex == null || !_lkCurve.HasComponent(e)) return false;
                var mid = MathUtils.Position(_lkCurve[e].m_Bezier, 0.5f);
                return _netIndex.TryMatch(mid.xz, pid, NetMatchTolerance, out key, out _);
            }
            if (_lkNode.HasComponent(e)) return false;
            if (_lkOwner.HasComponent(e)) return false;
            if (_lkObject.HasComponent(e) && _lkTransform.HasComponent(e))
            {
                if (_objectIndex == null) return false;
                kind = WorkKind.Object;
                return _objectIndex.TryMatch(_lkTransform[e].m_Position.xz, pid, ObjectMatchTolerance, out key, out _);
            }
            if (_lkArea.HasComponent(e) && _lkAreaNodes.HasBuffer(e))
            {
                if (_areaIndex == null) return false;
                kind = WorkKind.Area;
                return _areaIndex.TryMatch(AreaCentroid(e), pid, AreaMatchTolerance, out key, out _);
            }
            return false;
        }

        private float2 AreaCentroid(Entity e)
        {
            var nodes = _fast ? _lkAreaNodes[e] : EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
            int n = nodes.Length;
            // Ignore a closing duplicate of the first node (it would bias the centroid of small rings).
            if (n > 1 && math.distancesq(nodes[0].m_Position.xz, nodes[n - 1].m_Position.xz) < 1e-4f) n--;
            float2 c = 0;
            for (int i = 0; i < n; i++) c += nodes[i].m_Position.xz;
            return n > 0 ? c / n : c;
        }

        private CensusResult Census()
        {
            var matched = new HashSet<long>();
            FastReads();
            try
            {
                using (var temps = _tempQuery.ToEntityArray(Allocator.Temp))
                    for (int i = 0; i < temps.Length; i++)
                    {
                        if (!IsOwnCandidate(temps[i], out _)) continue;
                        if (TryMatch(temps[i], out long key, out _) && key != OwnerKey) matched.Add(key);
                    }
            }
            finally { FastReadsEnd(); }
            return new CensusResult { Expected = _itemByKey.Count, Matched = matched.Count };
        }

        // ───────────────────────── errors ─────────────────────────

        private void CollectErrors()
        {
            var positions = _status.ErrorPositions ?? (_status.ErrorPositions = new List<float3>());
            int count = 0;
            if (!_errorQuery.IsEmptyIgnoreFilter)
            {
                using var errors = _errorQuery.ToEntityArray(Allocator.Temp);
                count = errors.Length;
                for (int i = 0; i < errors.Length && positions.Count < MaxErrorMarkers; i++)
                    if (TryGetPosition(errors[i], out var p) && !ContainsNear(positions, p)) positions.Add(p);
            }
            // The error TYPE lives on the notification icon whose prefab carries ToolErrorData.
            if (string.IsNullOrEmpty(_errorTypeName) && !_iconQuery.IsEmptyIgnoreFilter)
            {
                using var icons = _iconQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < icons.Length; i++)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(icons[i]).m_Prefab;
                    if (!EntityManager.HasComponent<ToolErrorData>(prefab) || !BelongsToTemporary(icons[i])) continue;
                    _errorTypeName = EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error.ToString();
                    var at = EntityManager.GetComponentData<global::Game.Notifications.Icon>(icons[i]).m_Location;
                    if (positions.Count < MaxErrorMarkers && !ContainsNear(positions, at)) positions.Add(at);
                    break;
                }
            }
            if (count > 0 || !string.IsNullOrEmpty(_errorTypeName))
            {
                _errorKey = BuildErrorKeys.For(_errorTypeName);
                _errorArgs = new object[] { math.max(count, 1), _errorTypeName };
            }
        }

        private static bool ContainsNear(List<float3> list, float3 p)
        {
            foreach (var q in list) if (math.distancesq(q, p) < 1f) return true;
            return false;
        }

        private bool BelongsToTemporary(Entity icon)
        {
            if (EntityManager.HasComponent<Temp>(icon)) return true;
            if (!EntityManager.HasComponent<Owner>(icon)) return false;
            var owner = EntityManager.GetComponentData<Owner>(icon).m_Owner;
            return owner != Entity.Null && EntityManager.Exists(owner) && EntityManager.HasComponent<Temp>(owner);
        }

        private bool TryGetPosition(Entity e, out float3 p)
        {
            p = default;
            if (EntityManager.HasComponent<global::Game.Objects.Transform>(e)) { p = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position; return true; }
            if (EntityManager.HasComponent<Curve>(e)) { p = MathUtils.Position(EntityManager.GetComponentData<Curve>(e).m_Bezier, 0.5f); return true; }
            if (EntityManager.HasComponent<global::Game.Net.Node>(e)) { p = EntityManager.GetComponentData<global::Game.Net.Node>(e).m_Position; return true; }
            if (EntityManager.HasBuffer<global::Game.Areas.Node>(e))
            {
                var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
                if (nodes.Length == 0) return false;
                var c = AreaCentroid(e);
                p = new float3(c.x, nodes[0].m_Position.y, c.y);
                return true;
            }
            return false;
        }

        private int _taggedBays;

        // ───────────────────────── adoption ─────────────────────────

        /// <summary>
        /// Tags our temporaries with the group id right before Apply. Apply converts a Temp with no
        /// original into the permanent entity in place, so the tag (and Owner/Attached of parking
        /// decals) survives. Nodes are ours only when every temporary edge on them is ours — a node
        /// splitting an existing road stays the city's.
        /// </summary>
        private int TagTemporaries()
        {
            _claimed.Clear();
            _tagged.Clear();
            int unmatched = 0, unmatchedEdges = 0;
            _taggedCounts = default;
            _taggedBays = 0;
            _carrierOwned = 0;
            var nodeRefs = new Dictionary<Entity, int2>();   // x = all temp edges, y = our edges
            bool carrier = _carrier != Entity.Null && EntityManager.Exists(_carrier);
            using var temps = _tempQuery.ToEntityArray(Allocator.Temp);
            _deferStructural = true;
            FastReads();
            try
            {
            for (int i = 0; i < temps.Length; i++)
            {
                var e = temps[i];
                bool isEdge = EntityManager.HasComponent<Edge>(e);
                bool own = IsOwnCandidate(e, out _);
                long key = 0; WorkKind kind = WorkKind.Net;
                bool matched = own && TryMatch(e, out key, out kind);
                if (isEdge)
                {
                    // Nodes are grouped only when every edge on them is a GROUPED edge of ours (loose road edges are city roads).
                    bool grouped = matched && _itemByKey.TryGetValue(key, out var ew) && !ew.Loose;
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    CountNode(nodeRefs, edge.m_Start, grouped);
                    CountNode(nodeRefs, edge.m_End, grouped);
                }
                if (matched && key == OwnerKey) { AdoptOwnerTemp(e); continue; }
                if (!matched || !_itemByKey.TryGetValue(key, out var w))
                {
                    // Playtest 4b: an object/area of ours the plan index could not place (random
                    // variant prefab, moved by the game, matched elsewhere) was applied UNTAGGED and
                    // survived removal. While our tool is active every top-level temporary without an
                    // original is created from our definitions, so it is ours. Edges stay match-only:
                    // pieces of an existing road split by our junctions are the city's.
                    if (own && !isEdge && IsAdoptable(e, out var adoptKind)) { Tag(e, adoptKind, MemberRole.Generic, LooseBuild); unmatched++; }
                    else if (own && isEdge) unmatchedEdges++;
                    continue;
                }
                var role = RoleFor(w);
                var memberKind = kind == WorkKind.Net ? MemberKind.Edge : kind == WorkKind.Area ? MemberKind.Area : MemberKind.Object;
                Tag(e, memberKind, role, w.Loose);
                // FEEDBACK-4: an overridable object (prop, plant, decal) that still overlaps something gets
                // Override instead of Error; after Apply the game's OverrideSystem hides it ("Overridden").
                // Never silent: count it (docs/RESEARCH-NOTES-ADDENDUM.md, object collisions).
                if (memberKind == MemberKind.Object && EntityManager.HasComponent<global::Game.Tools.Override>(e))
                {
                    var at = EntityManager.HasComponent<global::Game.Objects.Transform>(e)
                        ? EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position : (float3?)null;
                    NoteSkip("Placemaker.Build.Hidden.Object", at);
                }
                if (role == MemberRole.ParkingAisle && carrier)
                    EntityManager.GetBuffer<global::Game.Net.SubNet>(_carrier).Add(new global::Game.Net.SubNet(e));
                if (role == MemberRole.ParkingBay) _taggedBays++;
                if (carrier && memberKind == MemberKind.Object && role != MemberRole.ParkingBay) ShareCarrierOwner(e);
                if (role == MemberRole.ParkingBay && carrier)
                {
                    // Bare carrier owner (PLT): LaneConnectionSystem walks Owner/Attached to find the
                    // aisles in the carrier's SubNet; an area owner would scatter the decals and an
                    // edge owner would cull them (RESEARCH-NOTES 13).
                    _pendingBays.Add(e);
                }
            }
            foreach (var kv in nodeRefs)
            {
                var node = kv.Key;
                if (kv.Value.y == 0 || kv.Value.x != kv.Value.y) continue;
                if (!EntityManager.Exists(node) || !EntityManager.HasComponent<Temp>(node) || !IsOwnCandidate(node, out _)) continue;
                Tag(node, MemberKind.Node, MemberRole.Generic);
            }
            }
            finally
            {
                FastReadsEnd();
                _deferStructural = false;
                FlushStructural();
            }
            if (_carrierOwned > 0) Mod.Log.Info($"Placemaker: {_carrierOwned} props/trees/chargers share the parking carrier owner (the game does not pair them with the markings).");
            Mod.Log.Info($"Placemaker tagged {_tagged.Count} temporary parts for group {_groupId}"
                + (unmatched > 0 ? $" ({unmatched} objects/areas adopted without a plan match)" : "")
                + (unmatchedEdges > 0 ? $"; {unmatchedEdges} unmatched new edges left to the city" : "") + ".");
            int markings = PlanWork.StallMarkings(_request?.Plan);
            if (markings > 0)
            {
                string msg = $"Placemaker parking: {_taggedBays} of {markings} stall markings built" + (carrier ? "" : " (no carrier)") + (_keptDespite > 0 ? $"; {_keptDespite} piece(s) built despite game errors" : "") + ".";
                if (_taggedBays < markings) Mod.Log.Warn(msg); else Mod.Log.Info(msg);
            }
            return _tagged.Count;
        }

        private int _carrierOwned;

        // (perf) TagTemporaries changed every piece's archetype one at a time (its tag; a stall marking's Owner and Attached, a
        // prop's carrier Owner): a few thousand structural changes in one frame for a big lot (~110 ms). They are collected and
        // made in one batch per component set at the end; nothing in between reads them.
        private bool _deferStructural;
        private readonly List<(Entity e, PlacemakerMember tag)> _pendingTags = new List<(Entity, PlacemakerMember)>();
        private readonly List<Entity> _pendingOwners = new List<Entity>(), _pendingBays = new List<Entity>();

        private void FlushStructural()
        {
            try
            {
                if (_pendingTags.Count > 0)
                {
                    var list = new List<Entity>(_pendingTags.Count);
                    foreach (var (e, _) in _pendingTags) list.Add(e);
                    using var arr = ToArray(list);
                    EntityManager.AddComponent<PlacemakerMember>(arr);
                    foreach (var (e, tag) in _pendingTags) EntityManager.SetComponentData(e, tag);
                }
                if (_pendingOwners.Count > 0)
                {
                    using var arr = ToArray(_pendingOwners);
                    EntityManager.AddComponent<Owner>(arr);
                    var owner = new Owner(_carrier);
                    foreach (var e in _pendingOwners) EntityManager.SetComponentData(e, owner);
                }
                if (_pendingBays.Count > 0)
                {
                    using var arr = ToArray(_pendingBays);
                    EntityManager.AddComponent(arr, new ComponentTypeSet(ComponentType.ReadWrite<Owner>(), ComponentType.ReadWrite<global::Game.Objects.Attached>()));
                    var owner = new Owner(_carrier);
                    var attached = new global::Game.Objects.Attached(_carrier, Entity.Null, 0f);
                    foreach (var e in _pendingBays) { EntityManager.SetComponentData(e, owner); EntityManager.SetComponentData(e, attached); }
                }
            }
            finally
            {
                _pendingTags.Clear();
                _pendingOwners.Clear();
                _pendingBays.Clear();
            }
        }

        private static NativeArray<Entity> ToArray(List<Entity> list)
        {
            var arr = new NativeArray<Entity>(list.Count, Allocator.Temp);
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }

        /// <summary>
        /// (s55) Playtest 89/90: a lot built "successfully" had no island trees, bushes, lamps or chargers. OverrideSystem (decompiled
        /// 1.6.2f1) hides (Overridden) every overridable object that meets another overridable one (the entity with the higher index
        /// loses), and the stall markings are not sunk any more, so every prop that touches a marking box vanished. The game skips an
        /// object whose top-level owner is the same (ObjectIterator: m_TopLevelEntity == entity), which is how the markings already
        /// coexist with each other. Props, trees, lamps and chargers get the parking carrier as Owner as well (no SubObject entry:
        /// the group removal owns their lifetime, exactly like the bays), so the game never pairs them with the markings or each other.
        /// Only plain overridable objects: buildings and anything already owned or attached stay untouched.
        /// </summary>
        private void ShareCarrierOwner(Entity e)
        {
            try
            {
                if (EntityManager.HasComponent<Owner>(e) || EntityManager.HasComponent<global::Game.Objects.Attached>(e)
                    || EntityManager.HasComponent<global::Game.Buildings.Building>(e) || !EntityManager.HasComponent<PrefabRef>(e)) return;
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!EntityManager.HasComponent<ObjectGeometryData>(prefab)) return;
                var flags = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Flags;
                const global::Game.Objects.GeometryFlags mask = global::Game.Objects.GeometryFlags.Overridable | global::Game.Objects.GeometryFlags.DeleteOverridden;
                if ((flags & mask) != global::Game.Objects.GeometryFlags.Overridable) return;
                if (_deferStructural) _pendingOwners.Add(e);
                else EntityManager.AddComponentData(e, new Owner(_carrier));
                _carrierOwned++;
            }
            catch (Exception ex) { Mod.Log.Warn("Placemaker: carrier owner failed: " + ex.Message); }
        }

        /// <summary>Top-level object or area (sub-elements go with their owner; nodes are decided by their edges).</summary>
        private bool IsAdoptable(Entity e, out MemberKind kind)
        {
            kind = MemberKind.Object;
            if (!EntityManager.HasComponent<PrefabRef>(e) || EntityManager.HasComponent<Owner>(e)) return false;
            if (EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<global::Game.Net.Node>(e)) return false;
            if (EntityManager.HasComponent<global::Game.Objects.Object>(e) && EntityManager.HasComponent<global::Game.Objects.Transform>(e)) return true;
            if (EntityManager.HasComponent<global::Game.Areas.Area>(e)) { kind = MemberKind.Area; return true; }
            return false;
        }

        /// <summary>
        /// Apply frame: tags temporaries that appeared or changed after validation (an optional
        /// object whose collision cleared, a late CourseSplit piece) so nothing is applied untagged.
        /// </summary>
        private int TagLateTemporaries()
        {
            int late = 0;
            using var temps = _tempQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < temps.Length; i++)
            {
                var e = temps[i];
                if (EntityManager.HasComponent<PlacemakerMember>(e) || !IsOwnCandidate(e, out _)) continue;
                if (EntityManager.HasComponent<Edge>(e))
                {
                    if (TryMatch(e, out long key, out _) && key != OwnerKey && _itemByKey.TryGetValue(key, out var lw)) { Tag(e, MemberKind.Edge, MemberRole.Generic, lw.Loose); late++; }
                    continue;
                }
                if (IsAdoptable(e, out var kind)) { Tag(e, kind, MemberRole.Generic, LooseBuild); late++; }
            }
            if (late > 0) Mod.Log.Info($"Placemaker tagged {late} late temporary parts on the Apply frame.");
            return late;
        }

        // ───────────────────────── strays and verification ─────────────────────────

        private readonly HashSet<Entity> _preexisting = new HashSet<Entity>();
        private EntityQuery _strayQuery;
        private float2[] _strayOutline;

        private EntityQuery StrayQuery()
        {
            if (_strayQuery == default)
                _strayQuery = GetEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                    None = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
                });
            return _strayQuery;
        }

        /// <summary>Remembers every top-level object inside the outline before the build touches anything.</summary>
        private void SnapshotPreexisting()
        {
            _preexisting.Clear();
            _strayOutline = BuildOutline();
            if (_strayOutline == null) return;
            ForObjectsInOutline((e, _) => _preexisting.Add(e));
        }

        private void ForObjectsInOutline(Action<Entity, Entity> visit)
        {
            var outline = _strayOutline;
            if (outline == null) return;
            float2 min = outline[0], max = outline[0];
            foreach (var p in outline) { min = math.min(min, p); max = math.max(max, p); }
            min -= 2f; max += 2f;
            var q = StrayQuery();
            using var entities = q.ToEntityArray(Allocator.Temp);
            using var transforms = q.ToComponentDataArray<global::Game.Objects.Transform>(Allocator.Temp);
            using var prefabs = q.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var p = transforms[i].m_Position.xz;
                if (p.x < min.x || p.y < min.y || p.x > max.x || p.y > max.y) continue;
                if (!ReservedSpace.PointInPolygon(p, outline) && TerraformDabs.DistanceToBoundary(outline, p) > 2f) continue;
                visit(entities[i], prefabs[i].m_Prefab);
            }
        }

        /// <summary>
        /// After Apply: a permanent, untagged, top-level object inside the outline that did not exist
        /// before the build and uses one of the prefabs this build placed was created by this build
        /// (e.g. a temporary the game replaced on Apply). Adopt it so removal deletes it too.
        /// </summary>
        private int AdoptStrays()
        {
            if (LooseBuild) return 0;
            if (_strayOutline == null || _usedPrefabs.Count == 0) return 0;
            var strays = new List<Entity>();
            ForObjectsInOutline((e, prefab) =>
            {
                if (_preexisting.Contains(e) || !_usedPrefabs.Contains(prefab) || EntityManager.HasComponent<PlacemakerMember>(e)) return;
                strays.Add(e);
            });
            foreach (var e in strays) Tag(e, MemberKind.Object, MemberRole.Generic);
            if (strays.Count > 0) Mod.Log.Warn($"Placemaker adopted {strays.Count} untagged objects this build created (tag lost on Apply).");
            return strays.Count;
        }

        /// <summary>Counts permanent members per kind and logs mismatches against the plan.</summary>
        private MemberCounts _taggedCounts;
        private bool _dupRetried;

        private PlannedCounts PlannedNow() => new PlannedCounts
        {
            Objects = _emittedObjects,
            OptionalDropped = _dropped,
            Areas = _emittedAreas,
            NetPieces = _work?.Nets.Count ?? 0,
        };

        /// <summary>Pre-Apply invariant: true when the temporaries are a duplicate set (the caller retries once, then fails).</summary>
        private bool TemporariesDuplicated()
        {
            if (!BuildVerification.Duplicated(PlannedNow(), _taggedCounts, out var why)) return false;
            Mod.Log.Warn($"Placemaker: temporaries look duplicated before Apply ({why}; {_taggedCounts}).");
            return true;
        }

        /// <summary>Post-Apply check. Returns true when the built group is a duplicate set and must be rolled back.</summary>
        private bool VerifyMembers()
        {
            try
            {
                // Everything this build claimed (grouped members AND loose city parts) that survived Apply.
                var counts = new MemberCounts();
                foreach (var e in _tagged)
                {
                    if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e)) continue;
                    if (e == _ownerTemp) continue;
                    MemberKind kind;
                    if (EntityManager.HasComponent<Edge>(e)) kind = MemberKind.Edge;
                    else if (EntityManager.HasComponent<global::Game.Net.Node>(e)) kind = MemberKind.Node;
                    else if (EntityManager.HasComponent<global::Game.Areas.Area>(e)) kind = MemberKind.Area;
                    else if (EntityManager.HasComponent<global::Game.Objects.Object>(e)) kind = MemberKind.Object;
                    else kind = MemberKind.Auxiliary;
                    counts.Add(kind);
                }
                var planned = PlannedNow();
                var issues = BuildVerification.Compare(planned, counts, looseNets: _work != null && !_work.AnyGrouped);
                if (issues.Count == 0) Mod.Log.Info($"Placemaker verified group {_groupId}: {counts}.");
                else Mod.Log.Warn($"Placemaker group {_groupId} member check ({counts}): {string.Join("; ", issues)}.");
                if (BuildVerification.Duplicated(planned, counts, out var why))
                {
                    if (LooseBuild)
                    {
                        // Loose parts are city content without a group to roll back; the pre-Apply check is the guard.
                        Mod.Log.Warn($"Placemaker loose build came out doubled ({why}); parts are ordinary city roads now.");
                        return false;
                    }
                    Mod.Log.Warn($"Placemaker group {_groupId} was built twice ({why}); rolling it back.");
                    return true;
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker member verification failed: " + e.Message); }
            return false;
        }

        private static void CountNode(Dictionary<Entity, int2> refs, Entity node, bool own)
        {
            if (node == Entity.Null) return;
            refs.TryGetValue(node, out var v);
            v.x++;
            if (own) v.y++;
            refs[node] = v;
        }

        private MemberRole RoleFor(WorkItem w)
        {
            var layer = _request.Plan.Layers[w.Layer];
            switch (w.Kind)
            {
                case WorkKind.Net:
                    var role = layer.Nets[w.Index].Role;
                    return w.GeneratorId == "parking" && (role == NetRole.DriveAisle || role == NetRole.DriveConnector)
                        ? MemberRole.ParkingAisle : MemberRole.Generic;
                case WorkKind.Object:
                    return layer.Objects[w.Index].Role == ObjectRole.ParkingBay ? MemberRole.ParkingBay : MemberRole.Generic;
                case WorkKind.Surface: return MemberRole.Surface;
                default: return MemberRole.Generic;
            }
        }

        /// <summary>True when the whole build is loose (only [AreaGenerator(Loose = true)] layers, e.g. a road grid).</summary>
        private readonly HashSet<Entity> _claimed = new HashSet<Entity>();

        private bool LooseBuild => _work != null && _work.AllLoose;

        /// <summary>
        /// Claims a temporary for this build: counted (exactly-once/duplicate checks, costs) and, unless it
        /// comes from a loose layer, tagged as a group member. Loose parts become ordinary city content.
        /// </summary>
        private void Tag(Entity e, MemberKind kind, MemberRole role, bool loose = false)
        {
            if (!_claimed.Add(e)) return;   // exactly once per entity (late passes revisit untagged loose parts)
            if (!loose && _groupId != 0)
            {
                var tag = new PlacemakerMember { GroupId = _groupId, Kind = kind, Role = role };
                if (EntityManager.HasComponent<PlacemakerMember>(e)) EntityManager.SetComponentData(e, tag);
                else if (_deferStructural) _pendingTags.Add((e, tag));
                else EntityManager.AddComponentData(e, tag);
            }
            _tagged.Add(e);
            _taggedCounts.Add(kind);
        }

        /// <summary>Claimed parts that Apply turned into permanent entities.</summary>
        private int CountAppliedClaimed()
        {
            int n = 0;
            foreach (var e in _tagged)
                if (EntityManager.Exists(e) && !EntityManager.HasComponent<Temp>(e) && !EntityManager.HasComponent<Deleted>(e)) n++;
            return n;
        }

        private int CountTaggedTemporaries()
        {
            int n = 0;
            foreach (var e in _tagged)
                if (EntityManager.Exists(e) && EntityManager.HasComponent<Temp>(e) && !EntityManager.HasComponent<Deleted>(e)) n++;
            return n;
        }

        // ───────────────────────── record ─────────────────────────

        private void CreateRecord()
        {
            if (LooseBuild)
            {
                // A road grid is just roads: no record, no owner lot, no tags (user request).
                _groupId = 0;
                _record = Entity.Null;
                return;
            }
            SnapshotPreexisting();
            _groupId = _registry.AllocateId();
            var rec = new GroupRecord
            {
                GroupId = _groupId,
                RecipeId = _request.Recipe?.Id ?? "",
                RecipeNameKey = _request.Recipe?.NameKey ?? "",
                CustomName = _request.CustomName ?? "",
                BuiltAtTicks = DateTime.UtcNow.Ticks,
            };
            _record = _registry.CreateRecord(rec, true);
            WriteDecals();

            // Carrier for parking decals; s58: every build gets one, it is the shared owner of the build's overridable objects
            // (so the game's OverrideSystem never hides our own pieces against each other), stall markings or not.
            Entity decal = Entity.Null;
            foreach (var w in _work.Objects)
                if (w.Category == AssetCategories.ParkingDecal && (decal = PrefabOf(w)) != Entity.Null) break;
            if (decal == Entity.Null)
                foreach (var w in _work.Objects)
                    if ((decal = PrefabOf(w)) != Entity.Null) break;
            if (decal == Entity.Null) return;
            _carrier = EntityManager.CreateEntity();
            // A PrefabRef is required: validation jobs walk Owner chains and read PrefabRef unchecked.
            EntityManager.AddComponentData(_carrier, new PrefabRef { m_Prefab = decal });
            // Owner buffers must exist even when empty (Sub*ReferencesSystem index them unchecked).
            EntityManager.AddBuffer<global::Game.Objects.SubObject>(_carrier);
            EntityManager.AddBuffer<global::Game.Net.SubNet>(_carrier);
            EntityManager.AddBuffer<global::Game.Areas.SubArea>(_carrier);
            EntityManager.AddComponentData(_carrier, new PlacemakerMember { GroupId = _groupId, Kind = MemberKind.Auxiliary, Role = MemberRole.Carrier });
            // Deliberately no Created/Updated: CS2 would try to regenerate the bare carrier (PLT).
            var g = EntityManager.GetComponentData<PlacemakerGroup>(_record);
            g.Carrier = _carrier;
            EntityManager.SetComponentData(_record, g);
        }

        /// <summary>
        /// (dynamic decals) The plan's grime strokes go onto the record as bytes; Game/Decals/DynamicDecalSystem draws them once the
        /// record is finished (no longer under construction) and drops them with the record (undo, bulldoze, ungroup).
        /// </summary>
        private void WriteDecals()
        {
            if (_record == Entity.Null || _request?.Plan?.Layers == null) return;
            var set = new Core.Decals.Dynamic.DecalSet();
            foreach (var l in _request.Plan.Layers) if (l?.Decals != null) set.Strokes.AddRange(l.Decals);
            foreach (var l in _request.Plan.Layers) if (l?.DecalInstances != null) set.Instances.AddRange(l.DecalInstances);
            if (set.Strokes.Count == 0 && set.Instances.Count == 0) return;
            // kinds the plan defined itself travel with it (a builder's own look); library kinds are named only
            var lib = Core.Decals.Dynamic.DecalLibrary.Current;
            foreach (var l in _request.Plan.Layers)
                if (l?.DecalKinds != null)
                    foreach (var k in l.DecalKinds)
                        if (k != null && !lib.Has(k.Id) && !string.IsNullOrEmpty(k.Json) && !set.Kinds.Contains(k.Json)) set.Kinds.Add(k.Json);
            var bytes = Core.Decals.Dynamic.DecalCodec.Encode(set);
            var buffer = EntityManager.AddBuffer<PlacemakerDecalBlob>(_record);
            buffer.ResizeUninitialized(bytes.Length);
            for (int i = 0; i < bytes.Length; i++) buffer[i] = new PlacemakerDecalBlob { Value = bytes[i] };
            Mod.Log.Info($"Placemaker dynamic decals: group {_groupId}: {set.Strokes.Count} strokes and {set.Instances.Count} library instances ({bytes.Length} bytes) on the record; weathering {_request.Plan.Weathering}");
        }

        private void ClearCarrierSubNets()
        {
            if (_carrier == Entity.Null || !EntityManager.Exists(_carrier) || !EntityManager.HasBuffer<global::Game.Net.SubNet>(_carrier)) return;
            var buffer = EntityManager.GetBuffer<global::Game.Net.SubNet>(_carrier);
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                var s = buffer[i].m_SubNet;
                if (!EntityManager.Exists(s) || EntityManager.HasComponent<Temp>(s) || EntityManager.HasComponent<Deleted>(s)) buffer.RemoveAt(i);
            }
        }

        private void RemoveUnderConstruction()
        {
            if (_record != Entity.Null && EntityManager.Exists(_record) && EntityManager.HasComponent<PlacemakerUnderConstruction>(_record))
                EntityManager.RemoveComponent<PlacemakerUnderConstruction>(_record);
        }

        private void DeleteRecordAndCarrier()
        {
            if (_carrier != Entity.Null && EntityManager.Exists(_carrier) && !EntityManager.HasComponent<Deleted>(_carrier))
                EntityManager.AddComponent<Deleted>(_carrier);
            if (_record != Entity.Null && EntityManager.Exists(_record) && !EntityManager.HasComponent<Deleted>(_record))
                EntityManager.AddComponent<Deleted>(_record);
            _registry.MarkDirty();
        }

        private void FinishRecord()
        {
            var rec = _registry.GetRecord(_groupId) ?? new GroupRecord { GroupId = _groupId };
            var plan = _request.Plan;
            rec.Stats = plan.MergedStats();
            rec.Facts.Clear();
            foreach (var l in plan.Layers)
                foreach (var kv in l.Facts)
                    rec.Facts[kv.Key] = (rec.Facts.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            long t0 = Now;
            var estimate = Pricing.Of(plan, _catalog.InfoMap());
            Sub("Record: pricing", t0);
            rec.Cost = estimate.Cost;
            rec.Upkeep = estimate.Upkeep;
            rec.PrefabNames = new List<string>(_usedPrefabNames);
            rec.PrefabNames.Sort(StringComparer.Ordinal);
            if (_work.HasPermanentTerrain || HasPaint()) rec.Flags |= GroupFlags.PermanentTerrain;   // terraform and paint are never undone
            if (_work.HasParking && _carrier != Entity.Null) rec.Flags |= GroupFlags.ParkingEconomy;
            if (_request.Ungrouped) rec.Flags |= GroupFlags.Ungrouped;   // built as separate pieces (the Review switch)
            if (_request.Recipe != null) rec.Recipe = _request.Recipe.Clone();
            if (_request.Area != null)
            {
                rec.Area = new AreaShape
                {
                    Ring = (float3[])_request.Area.Ring.Clone(),
                    Source = _request.Area.Source,
                    Attachments = new List<BoundaryAttachment>(_request.Area.Attachments ?? new List<BoundaryAttachment>()),
                };
            }
            t0 = Now;
            FinishParkStats(rec);
            Sub("Record: park stats", t0); t0 = Now;
            if (EntityManager.Exists(_record))
            {
                _registry.WriteRecord(_record, rec);
                Sub("Record: write", t0); t0 = Now;
                var g = EntityManager.GetComponentData<PlacemakerGroup>(_record);
                g.BuiltMembers = _registry.CountMembers(_groupId, false);
                EntityManager.SetComponentData(_record, g);
                Sub("Record: count members", t0);
            }
            t0 = Now;
            RemoveUnderConstruction();
            Sub("Record: under construction", t0); t0 = Now;
            _registry.MarkDirty();
            try { World.GetOrCreateSystemManaged<Economy.ParkingEconomySystem>().OnGroupBuilt(_groupId); }
            catch (Exception e) { Mod.Log.Warn("Placemaker parking economy setup failed (build kept): " + e.Message); }
            Sub("Record: parking economy", t0);
        }
    }
}
