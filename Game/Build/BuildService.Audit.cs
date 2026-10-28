// Placemaker — s53: build audit; s59: the anarchy decision moved to Core/Build/BuildVerdicts (one pass, no retry loop).
//
// Anarchy ("Ignore collisions in my builds", default on) builds every planned piece whatever the game's collision checks say;
// the audit lists what was left out (with the reason) and what was kept despite a game error (AuditReason.Anarchy).

using System;
using System.Collections.Generic;
using System.IO;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Game.Settings;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Groups;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>Setting "Ignore collisions in my builds" (default on).</summary>
        public static bool IgnoreOwnCollisions = true;

        private BuildAudit _audit;
        private readonly HashSet<long> _auditMissing = new HashSet<long>();
        private OwnOverlap _ownOverlap;

        /// <summary>(s53) An object that overlaps another planned one (Core/Build/OwnOverlap, real catalog bounds).</summary>
        private bool SelfOverlaps(WorkItem w)
        {
            try
            {
                if (_request?.Plan == null) return false;
                if (_ownOverlap == null) _ownOverlap = new OwnOverlap(_request.Plan, SafeAssetInfo());
                return _ownOverlap.Overlaps(w.Layer, w.Index);
            }
            catch (Exception) { return false; }
        }

        private BuildAudit Audit => _audit ?? (_audit = new BuildAudit());

        private void ResetAudit()
        {
            _audit = null;
            _ownOverlap = null;
            _auditMissing.Clear();
        }

        private static string AuditKind(ComposedPlan plan, WorkItem w)
        {
            switch (w.Kind)
            {
                case WorkKind.Net: return "net";
                case WorkKind.Surface: return "surface";
                case WorkKind.Area: return "area";
                case WorkKind.Terraform: return "terraform";
                case WorkKind.Zoning: return "zoning";
            }
            var o = ItemObject(plan, w);
            return o != null && o.Role == ObjectRole.Decal ? "decal" : "object";
        }

        private static PlannedObject ItemObject(ComposedPlan plan, WorkItem w)
        {
            if (w.Kind != WorkKind.Object || plan?.Layers == null || w.Layer < 0 || w.Layer >= plan.Layers.Count) return null;
            var objs = plan.Layers[w.Layer]?.Objects;
            return objs != null && w.Index >= 0 && w.Index < objs.Count ? objs[w.Index] : null;
        }

        private void AuditItem(WorkItem w, string reason)
        {
            try
            {
                string name = null;
                try { if (_itemPrefab.TryGetValue(PlanWork.Key(w), out var pe)) name = _catalog?.NameOf(pe); } catch (Exception) { }
                if (string.IsNullOrEmpty(name)) name = w.Asset.Id;
                Audit.Add(AuditKind(_request?.Plan, w), name, new float3(w.Anchor.x, Height(w.Anchor), w.Anchor.y), reason);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: audit entry failed: " + e.Message); }
        }

        private void AuditMissing(WorkItem w)
        {
            if (_auditMissing.Add(PlanWork.Key(w))) AuditItem(w, AuditReason.AssetMissing);
        }

        /// <summary>Error type (OverlapExisting...) per owning temporary, from the notification icons.</summary>
        private Dictionary<Entity, string> ErrorTypeByOwner()
        {
            var map = new Dictionary<Entity, string>();
            if (_iconQuery.IsEmptyIgnoreFilter) return map;
            using var icons = _iconQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < icons.Length; i++)
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(icons[i]).m_Prefab;
                if (!EntityManager.HasComponent<ToolErrorData>(prefab) || !BelongsToTemporary(icons[i])) continue;
                var owner = EntityManager.HasComponent<Owner>(icons[i]) ? EntityManager.GetComponentData<Owner>(icons[i]).m_Owner : Entity.Null;
                if (owner == Entity.Null || !EntityManager.Exists(owner)) continue;
                string et = EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error.ToString();
                if (!map.TryGetValue(owner, out var prev)) map[owner] = et;
                else if (prev != et) map[owner] = "Mixed";
            }
            return map;
        }

        private string ErrorTypeOf(Dictionary<Entity, string> byOwner, Entity e)
        {
            if (byOwner != null && byOwner.TryGetValue(e, out var t)) return t;
            return string.IsNullOrEmpty(_errorTypeName) ? "Unknown" : _errorTypeName;
        }

        /// <summary>
        /// (s55) What the build really leaves standing. After Apply the game's OverrideSystem hides (Overridden) any overridable object
        /// that meets another one, so "built" must not count temporaries: this scans the group's objects for Overridden, reports them
        /// as "hidden" (left out, "hidden by the game: overlap") and brings back those that are hidden only because of our own pieces.
        /// Runs in DoFinish, ~50 frames after Apply (OverrideSystem decides within a few frames).
        /// </summary>
        private void AuditSurvivors()
        {
            try
            {
                if (_groupId == 0 || LooseBuild || _request?.Plan == null) return;
                var q = GetEntityQuery(ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<global::Game.Common.Overridden>(),
                    ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                int hiddenNow = 0, restored = 0, unmasked = 0;
                var byPrefab = new Dictionary<string, int>(StringComparer.Ordinal);
                if (!q.IsEmptyIgnoreFilter)
                {
                    var assets = SafeAssetInfo();
                    var buildings = ExistingBuildingPolys();
                    using var entities = q.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var tag = EntityManager.GetComponentData<PlacemakerMember>(e);
                        if (tag.GroupId != _groupId || tag.Kind != MemberKind.Object) continue;
                        var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                        var pos = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                        string name = null;
                        try { name = _catalog?.NameOf(prefab); } catch (Exception) { }
                        if (string.IsNullOrEmpty(name)) name = "?";
                        // (s59) Anarchy: every planned piece of ours that the game hid after Apply is shown again unless it touches an existing building.
                        bool own = false;
                        if (IgnoreOwnCollisions && _objectIndex != null && _objectIndex.TryMatch(pos.xz, PrefabId(prefab), ObjectMatchTolerance, out long key, out _)
                            && _itemByKey.TryGetValue(key, out var w) && w.Kind == WorkKind.Object)
                        {
                            var obj = ItemObject(_request.Plan, w);
                            own = obj != null && !TouchesBuilding(obj, assets, buildings);
                        }
                        hiddenNow++;
                        byPrefab[name] = (byPrefab.TryGetValue(name, out int n) ? n : 0) + 1;
                        if (own)
                        {
                            // Only our own pieces are in the way: show it again (no Updated: that would re-run the override check).
                            // (core) OverrideSystem also zeroed the object's mask in the static search tree (UpdateObjectOverrideJob:
                            // m_Mask = 0), and the renderer culls through that tree: removing Overridden alone left about 300 of 650
                            // parking pieces "shown again" but never drawn. The mask goes back exactly as OverrideSystem restores it.
                            EntityManager.RemoveComponent<global::Game.Common.Overridden>(e);
                            EntityManager.AddComponent<global::Game.Common.BatchesUpdated>(e);
                            if (RestoreSearchMask(World, e, prefab)) unmasked++;
                            restored++;
                        }
                        else Audit.Add("object", name, pos, AuditReason.Hidden);
                    }
                }
                if (hiddenNow > 0)
                {
                    var top = new List<KeyValuePair<string, int>>(byPrefab);
                    top.Sort((a, b) => b.Value.CompareTo(a.Value));
                    var parts = new List<string>();
                    for (int i = 0; i < top.Count && i < 6; i++) parts.Add(top[i].Key + " x" + top[i].Value);
                    Mod.Log.Warn($"Placemaker survivors: {hiddenNow} object(s) of group {_groupId} were hidden by the game (Overridden): {restored} shown again (anarchy: planned pieces stay visible; {unmasked} back in the render search tree), {hiddenNow - restored} stay hidden [{string.Join("; ", parts)}].");
                }
                else Mod.Log.Info($"Placemaker survivors: no object of group {_groupId} is hidden by the game (Overridden); {_carrierOwned} share the carrier owner.");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: survivor check failed: " + e.Message); }
        }

        /// <summary>
        /// (core) Puts an un-hidden object back into the static object search tree with the mask OverrideSystem gives a visible one
        /// (Game.Objects.OverrideSystem.GetBoundsMask: NotOverridden | the prefab's mesh layers). Without it the object stays culled.
        /// Also used by Ungroup's dissolve (GroupRegistrySystem.Dissolve.cs).
        /// </summary>
        internal static bool RestoreSearchMask(World world, Entity e, Entity prefab)
        {
            var EntityManager = world.EntityManager;
            try
            {
                if (!EntityManager.HasComponent<ObjectGeometryData>(prefab)) return false;
                var layers = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Layers;
                var search = world.GetOrCreateSystemManaged<global::Game.Objects.SearchSystem>();
                var tree = search.GetStaticSearchTree(false, out var deps);
                deps.Complete();
                bool ok = false;
                if (tree.TryGet(e, out var bounds))
                {
                    var mask = global::Game.Common.BoundsMask.NotOverridden | global::Game.Common.CommonUtils.GetBoundsMask(layers);
                    bounds.m_Mask = (bounds.m_Mask & ~(global::Game.Common.BoundsMask.AllLayers | global::Game.Common.BoundsMask.NotOverridden)) | mask;
                    tree.Update(e, bounds);
                    ok = true;
                }
                search.AddStaticSearchTreeWriter(default);
                return ok;
            }
            catch (Exception ex) { Mod.Log.Warn("Placemaker: search tree restore failed: " + ex.Message); return false; }
        }

        private static void Tally(Dictionary<string, int> d, string key) => d[key] = (d.TryGetValue(key, out int n) ? n : 0) + 1;

        private static bool TouchesBuilding(PlannedObject o, Dictionary<string, AssetInfo> assets, List<float2[]> polys)
        {
            foreach (var p in polys) if (SiteSafety.ObjectTouches(o, assets, p, 0.3f)) return true;
            return false;
        }

        private List<float2[]> ExistingBuildingPolys()
        {
            var polys = new List<float2[]>();
            foreach (var p in _request.Plan.Protected) if (p != null) polys.Add(p);   // the capture's lots / footprints
            using var all = GetEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()).ToEntityArray(Allocator.Temp);
            var bounds = PlanBounds();
            for (int i = 0; i < all.Length; i++)
            {
                var pos = EntityManager.GetComponentData<global::Game.Objects.Transform>(all[i]).m_Position.xz;
                if (pos.x < bounds.x || pos.y < bounds.y || pos.x > bounds.z || pos.y > bounds.w) continue;
                BuildingFootprints.Of(EntityManager, all[i], out var lot, out var fp);
                if (lot != null) polys.Add(lot);
                if (fp != null) polys.Add(fp);
            }
            return polys;
        }

        /// <summary>min.x, min.z, max.x, max.z of the planned objects plus a margin (existing buildings further away cannot be touched).</summary>
        private float4 PlanBounds()
        {
            float4 b = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            foreach (var l in _request.Plan.Layers)
                if (l?.Objects != null)
                    foreach (var o in l.Objects)
                        if (o != null && math.all(math.isfinite(o.Position))) b = new float4(math.min(b.x, o.Position.x), math.min(b.y, o.Position.z), math.max(b.z, o.Position.x), math.max(b.w, o.Position.z));
            return b + new float4(-60f, -60f, 60f, 60f);
        }

        private Dictionary<string, AssetInfo> SafeAssetInfo()
        {
            try { return Services.Assets?.InfoMap(); } catch (Exception) { return null; }
        }

        // ───────────── zoning per plot: preview vs planned vs live vs built ─────────────

        private readonly List<float2> _previewZoned = new List<float2>();

        private static float2[] ExpandRing(float2[] ring, float by)
        {
            var c = Poly.Centroid(ring);
            var r = new float2[ring.Length];
            for (int i = 0; i < r.Length; i++) { var d = ring[i] - c; float l = math.length(d); r[i] = l > 1e-4f ? c + d * ((l + by) / l) : ring[i]; }
            return r;
        }

        /// <summary>The zoned cells the Design preview showed (before the post-Apply re-snap changes plan.ZoneCells).</summary>
        private void SnapshotPreviewZoning()
        {
            _previewZoned.Clear();
            var cells = _request?.Plan?.ZoneCells;
            if (cells == null) return;
            foreach (var c in cells) if (c.State == ZoneCellState.Zoned) _previewZoned.Add(0.25f * (c.A + c.B + c.C + c.D));
        }

        /// <summary>
        /// (s53) Per plot (original cutout quad): cells shown in the preview, cells planned for zoning (after the re-snap), cells the
        /// game really has zoned (state 4 of VerifyZoning) and buildings / construction sites standing on it. One log line per plot;
        /// a plot whose preview cells are not all live-zoned is an audit entry ("shown in preview, not zoned").
        /// </summary>
        private void AuditZonePlots(ComposedPlan plan, List<float2> planned, int[] state)
        {
            if (plan?.ZoneQuads == null || plan.ZoneQuads.Count == 0) return;
            var buildings = new List<float2>();
            using (var all = GetEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()).ToEntityArray(Allocator.Temp))
                for (int i = 0; i < all.Length; i++) buildings.Add(EntityManager.GetComponentData<global::Game.Objects.Transform>(all[i]).m_Position.xz);
            int bad = 0;
            for (int q = 0; q < plan.ZoneQuads.Count; q++)
            {
                var quad = ExpandRing(plan.ZoneQuads[q].Quad, 1.5f);
                int preview = 0, plannedN = 0, live = 0, built = 0;
                foreach (var p in _previewZoned) if (Poly.Contains(quad, p)) preview++;
                for (int k = 0; k < planned.Count; k++)
                    if (Poly.Contains(quad, planned[k])) { plannedN++; if (state[k] == 4) live++; }
                foreach (var b in buildings) if (Poly.Contains(quad, b)) built++;
                var c = Poly.Centroid(plan.ZoneQuads[q].Quad);
                bool mismatch = preview != plannedN || plannedN != live;
                if (q < 40) Mod.Log.Info($"Placemaker zoning plot {q} at ({c.x:F0}, {c.y:F0}) {plan.ZoneQuads[q].ZoneId}: preview {preview}, planned {plannedN}, live zoned {live}, buildings {built}{(mismatch ? "  <-- differs" : "")}.");
                if (preview > live || preview > 0 && plannedN == 0)
                {
                    bad++;
                    Audit.Add("zoning", $"plot {q}: preview {preview}, planned {plannedN}, live {live}, buildings {built}", new float3(c.x, Height(c), c.y), AuditReason.ZoningMismatch);
                }
            }
            if (bad > 0) Mod.Log.Warn($"Placemaker zoning: {bad} plot(s) shown in the preview but not zoned as shown (details per plot above).");
        }

        /// <summary>Finish: totals, plan-time drops (declutter), the log block and the JSON file.</summary>
        private BuildAudit FinishAudit(int groupId)
        {
            try
            {
                var a = Audit;
                var plan = _request?.Plan;
                int planned = 0, declutter = 0;
                if (plan != null)
                {
                    planned = PlanWork.Flatten(plan, null).DefinitionCount;
                    foreach (var i in plan.Issues)
                    {
                        if (i?.Key == null || !i.Key.StartsWith("Placemaker.Issue.Declutter.", StringComparison.Ordinal)) continue;
                        int n = i.Args != null && i.Args.Length > 0 && i.Args[0] is int c ? c : 1;
                        a.Add("object", i.Key.Substring("Placemaker.Issue.Declutter.".Length), i.At ?? default, AuditReason.Declutter, n);
                        declutter += n;
                    }
                }
                a.Planned = planned + declutter;
                a.Built = Math.Max(0, a.Planned - a.LeftOut);
                Mod.Log.Info("Placemaker " + a.Summary());
                if (a.Entries.Count > 0)
                {
                    var dir = Path.Combine(ModPaths.DataFolder, "audit");
                    Directory.CreateDirectory(dir);
                    var now = DateTime.Now;
                    File.WriteAllText(Path.Combine(dir, $"group-{groupId}-{now:yyyyMMdd-HHmmss}.json"), a.ToJson(groupId, now.ToString("s")));
                }
                return a;
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker: build audit failed: " + e.Message); return null; }
        }
    }
}
