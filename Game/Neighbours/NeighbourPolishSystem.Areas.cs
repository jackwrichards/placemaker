// Placemaker neighbourhood polish — look areas (GPL-3.0).
//
// A Zoning type built with "Zone this area" zones the empty cells and leaves a look area behind (saved with the city: a
// headless entity with PolishLookArea, its outline and its changes). A building that grows inside one later is polished
// with the area's look and changes (FlagNewLots), the newest area first. An area built over an older one wholly covers
// it: the older one goes.

using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        private static readonly List<(float2[] Ring, string Look, IReadOnlyDictionary<string, float> Changes, string Zone, int Batch)> s_newAreas = new List<(float2[], string, IReadOnlyDictionary<string, float>, string, int)>();
        private static int s_batch;

        /// <summary>A look area's look for a zone type whose Outgrow is off: its buildings are not polished.</summary>
        public const string LookOff = "off";

        /// <summary>The start of one build's look areas (one per zone, and one for any other zone): they share an order.</summary>
        public static int NewLookBatch() => ++s_batch;

        /// <summary>An area zoned with a look: buildings of this zone ("" any other zone) that grow in it get the look and these
        /// changes.</summary>
        public static void AddLookArea(float2[] ring, string look, IReadOnlyDictionary<string, float> changes, string zone = "", int batch = 0)
        {
            if (ring == null || ring.Length < 3 || string.IsNullOrEmpty(look)) return;
            s_newAreas.Add((ring, look, changes, zone ?? "", batch == 0 ? NewLookBatch() : batch));
        }

        /// <summary>A zone's name without its region (EU / NA), lower case: the same zone in either theme.</summary>
        public static string ZoneKey(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return "";
            if (zone.StartsWith("EU ") || zone.StartsWith("NA ")) zone = zone.Substring(3);
            return zone.Trim().ToLowerInvariant();
        }

        /// <summary>The zone a building grows in ("" none).</summary>
        public string ZoneOfLot(Entity lot) =>
            EntityManager.Exists(lot) && EntityManager.HasComponent<PrefabRef>(lot) ? ZoneName(EntityManager.GetComponentData<PrefabRef>(lot).m_Prefab) : "";

        private sealed class LookArea
        {
            public float2[] Ring;
            public float2 Min, Max;
            public int Look, Order, Zone;
            public PolishLookOverride[] Changes;
        }

        private EntityQuery _lookAreas;
        private List<LookArea> _areaCache;

        /// <summary>Look areas in the city (the status line).</summary>
        public static int LookAreaCount { get; private set; }

        private void CreateAreaQueries()
        {
            _lookAreas = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PolishLookArea>(), ComponentType.ReadOnly<PolishLookAreaPoint>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
        }

        private void AreasUpdate()
        {
            if (s_newAreas.Count == 0) return;
            var areas = LookAreas();
            int order = 0;
            foreach (var a in areas) order = math.max(order, a.Order);
            int lastBatch = -1, firstOrder = order + 1;
            foreach (var (ring, look, changes, zone, batch) in s_newAreas)
            {
                if (batch != lastBatch) { lastBatch = batch; order++; firstOrder = order; }
                // older areas this one wholly covers go (never one of this build's)
                int gone = 0;
                using (var old = _lookAreas.ToEntityArray(Allocator.Temp))
                    foreach (var e in old)
                    {
                        if (EntityManager.GetComponentData<PolishLookArea>(e).Order >= firstOrder) continue;
                        var pts = EntityManager.GetBuffer<PolishLookAreaPoint>(e);
                        bool inside = pts.Length > 0;
                        for (int k = 0; k < pts.Length && inside; k++) inside = Poly.Contains(ring, pts[k].Position);
                        if (!inside) continue;
                        EntityManager.DestroyEntity(e);
                        gone++;
                    }
                Poly.Bounds(ring, out var min, out var max);
                var area = EntityManager.CreateEntity();
                int zoneHash = string.IsNullOrEmpty(zone) ? 0 : PolishLooks.Hash(ZoneKey(zone));
                EntityManager.AddComponentData(area, new PolishLookArea { Look = PolishLooks.Hash(look), Order = order, Min = min, Max = max, Zone = zoneHash });
                var buf = EntityManager.AddBuffer<PolishLookAreaPoint>(area);
                foreach (var p in ring) buf.Add(new PolishLookAreaPoint { Position = p });
                var ch = EntityManager.AddBuffer<PolishLookOverride>(area);
                if (changes != null) foreach (var kv in changes) ch.Add(new PolishLookOverride { Knob = PolishLooks.Hash(kv.Key), Value = kv.Value });
                Mod.Log.Info($"Placemaker neighbourhood polish: look area {order} ({look}, {(string.IsNullOrEmpty(zone) ? "any zone" : zone)}, {changes?.Count ?? 0} changes, {Poly.Area(ring):F0} m²): buildings that grow in it get its look{(gone > 0 ? $"; {gone} older areas inside it gone" : "")}");
            }
            s_newAreas.Clear();
            _areaCache = null;
        }

        /// <summary>The look areas, newest first.</summary>
        private List<LookArea> LookAreas()
        {
            if (_areaCache != null) return _areaCache;
            var list = new List<LookArea>();
            using (var all = _lookAreas.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var a = EntityManager.GetComponentData<PolishLookArea>(e);
                    var pts = EntityManager.GetBuffer<PolishLookAreaPoint>(e);
                    var ring = new float2[pts.Length];
                    for (int k = 0; k < ring.Length; k++) ring[k] = pts[k].Position;
                    var ch = EntityManager.HasBuffer<PolishLookOverride>(e) ? EntityManager.GetBuffer<PolishLookOverride>(e).AsNativeArray().ToArray() : new PolishLookOverride[0];
                    list.Add(new LookArea { Ring = ring, Min = a.Min, Max = a.Max, Look = a.Look, Order = a.Order, Zone = a.Zone, Changes = ch });
                }
            // newest first; within one build, a zone's own area before the one for any other zone
            list.Sort((x, y) => x.Order != y.Order ? y.Order.CompareTo(x.Order) : (y.Zone != 0).CompareTo(x.Zone != 0));
            LookAreaCount = list.Count;
            return _areaCache = list;
        }

        /// <summary>The newest look area at a point for a building of this zone (its own zone's, else any zone's), or null.</summary>
        private LookArea LookAreaAt(float2 p, string zone = "")
        {
            int zoneHash = string.IsNullOrEmpty(zone) ? 0 : PolishLooks.Hash(ZoneKey(zone));
            foreach (var a in LookAreas())
            {
                if (a.Zone != 0 && a.Zone != zoneHash) continue;
                if (p.x < a.Min.x || p.y < a.Min.y || p.x > a.Max.x || p.y > a.Max.y) continue;
                if (a.Ring.Length >= 3 && Poly.Contains(a.Ring, p)) return a;
            }
            return null;
        }
    }
}
