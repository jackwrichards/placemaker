// Placemaker neighbourhood polish — what gardens never cross and like to meet (GPL-3.0).
//
// For each pass the planner gets the barriers around its patch of town (StitchSettings.Barriers): the ground surfaces of
// roads, paths and tracks (each edge's surface as the game draws it, sidewalks, junction corners, turning circles and
// roundabouts included (Selection/RoadSurfaces); tunnelled pieces left out: the ground above a tunnel is open), the decks of
// elevated ones as seen from above (an overpass, a bridge: the ground under it is no garden's; YardStitch.DeckKind), the lots
// of buildings that are not houses (service buildings, Placemaker builds' buildings, houses still
// being built: a building site is no house to plan yet), lot
// areas and every Placemaker build's outline (as the area brush's "Lots" and "My builds": a park in the middle of a block
// stays the park's), and water (sampled every 4 m, merged). Two houses never join across one, no garden reaches onto one,
// and a garden side nobody else has reaches out to one near it (YardStitch.Hug). Read from the game's own search trees, so
// a patch costs the same in a city of any size.

using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core.Neighbours;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        private global::Game.Net.SearchSystem _netSearch;
        private global::Game.Objects.SearchSystem _objectSearch;
        // a road comes as several surfaces (its halves, its ends at the junctions). Only a guard against a runaway box: at 4 000
        // every pass in the user's city of 15 000 (a patch of 600 m) was cut off in its roads, without the other buildings' lots
        // and the water, and gardens grew over the streets left out (180 of 2 500)
        private const int MaxBarriers = 200000;
        private static int s_barrierCapLogged;

        private struct BuildingIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            [Unity.Collections.ReadOnly] public ComponentLookup<global::Game.Buildings.Building> Buildings;
            [Unity.Collections.ReadOnly] public ComponentLookup<global::Game.Buildings.Extension> Extensions;
            [Unity.Collections.ReadOnly] public ComponentLookup<global::Game.Objects.Pillar> Pillars;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds) && (Buildings.HasComponent(e) || Extensions.HasComponent(e) || Pillars.HasComponent(e))) Results.Add(e);
            }
        }

        private struct BoundsIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(QuadTreeBoundsXZ bounds, Entity e)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(e);
            }
        }

        /// <summary>Roads, paths and tracks on the ground and the lots of buildings that are not houses, in a box.</summary>
        private List<float2[]> BarriersIn(float2 min, float2 max) => BarriersIn(min, max, null, null);

        private BlockObstacles _obstacles;
        private readonly List<(float2[] Ring, bool Round)> _roadPieces = new List<(float2[], bool)>();

        /// <summary>A road piece's surfaces as last read, with the geometry they were read from.</summary>
        private sealed class RoadShape
        {
            public EdgeGeometry Edge;
            public EdgeNodeGeometry Start, End;
            public bool HasStart, HasEnd;
            public Entity StartComp, EndComp;
            public List<float2[]> Rings;
            /// <summary>A deck's: how high each ring's road surface is above the ground.</summary>
            public List<float> Heights;
        }

        // every road piece's surfaces, read again only when its geometry changed (the patches of a growing town overlap: the
        // same roads were read for every pass); the decks of elevated ones apart
        private readonly Dictionary<Entity, RoadShape> _roadShapes = new Dictionary<Entity, RoadShape>();
        private readonly Dictionary<Entity, RoadShape> _deckShapes = new Dictionary<Entity, RoadShape>();
        private readonly List<(float2[] Ring, float Height)> _deckPieces = new List<(float2[], float)>();

        /// <summary>The cached shape of the road piece when its geometry has not changed since (else null; <paramref name="now"/>
        /// is then filed in its place, its rings for the caller to fill).</summary>
        private RoadShape Cached(Dictionary<Entity, RoadShape> cache, Entity e, out RoadShape now)
        {
            var g = EntityManager.GetComponentData<EdgeGeometry>(e);
            bool hs = EntityManager.HasComponent<StartNodeGeometry>(e), he = EntityManager.HasComponent<EndNodeGeometry>(e);
            var sg = hs ? EntityManager.GetComponentData<StartNodeGeometry>(e).m_Geometry : default;
            var eg = he ? EntityManager.GetComponentData<EndNodeGeometry>(e).m_Geometry : default;
            var comp = EntityManager.HasComponent<Composition>(e) ? EntityManager.GetComponentData<Composition>(e) : default;
            now = new RoadShape { Edge = g, Start = sg, End = eg, HasStart = hs, HasEnd = he, StartComp = comp.m_StartNode, EndComp = comp.m_EndNode };
            if (cache.TryGetValue(e, out var c) && c.HasStart == hs && c.HasEnd == he && c.StartComp == comp.m_StartNode && c.EndComp == comp.m_EndNode
                && Exact(c.Edge.m_Start, g.m_Start) && Exact(c.Edge.m_End, g.m_End) && (!hs || Exact(c.Start, sg)) && (!he || Exact(c.End, eg)))
                return c;
            if (cache.Count > 50000) cache.Clear();
            cache[e] = now;
            return null;
        }

        /// <summary>The road piece's surfaces (RoadSurfaces; a strip along its curve when it has no geometry yet).</summary>
        private List<float2[]> RoadRings(Entity e, float width)
        {
            var c = Cached(_roadShapes, e, out var now);
            if (c != null) return c.Rings;
            _roadPieces.Clear();
            RoadSurfaces.Pieces(EntityManager, e, _roadPieces);
            var rings = new List<float2[]>(_roadPieces.Count);
            foreach (var (ring, _) in _roadPieces) rings.Add(ring);
            if (rings.Count == 0)
            {
                var strip = Strip(EntityManager.GetComponentData<Curve>(e), width * 0.5f);
                if (strip != null) rings.Add(strip);
            }
            now.Rings = rings;
            return rings;
        }

        /// <summary>An elevated road piece's deck in slices along it, each with how high it is above the ground
        /// (RoadSurfaces.DeckPieces; read again when its geometry changed).</summary>
        private RoadShape DeckRings(Entity e)
        {
            var c = Cached(_deckShapes, e, out var now);
            if (c != null) return c;
            var heights = _terrain.GetHeightData(true);
            _deckPieces.Clear();
            RoadSurfaces.DeckPieces(EntityManager, e, p => global::Game.Simulation.TerrainUtils.SampleHeight(ref heights, p), _deckPieces);
            now.Rings = new List<float2[]>(_deckPieces.Count);
            now.Heights = new List<float>(_deckPieces.Count);
            foreach (var (ring, h) in _deckPieces) { now.Rings.Add(ring); now.Heights.Add(h); }
            return now;
        }

        private static bool Exact(Bezier4x3 x, Bezier4x3 y) => x.a.Equals(y.a) && x.b.Equals(y.b) && x.c.Equals(y.c) && x.d.Equals(y.d);
        private static bool Exact(Segment x, Segment y) => Exact(x.m_Left, y.m_Left) && Exact(x.m_Right, y.m_Right);
        private static bool Exact(EdgeNodeGeometry x, EdgeNodeGeometry y) => Exact(x.m_Left, y.m_Left) && Exact(x.m_Right, y.m_Right) && Exact(x.m_Middle, y.m_Middle) && x.m_MiddleRadius == y.m_MiddleRadius;

        /// <summary>The barriers in a box, what each is (0 a road, path or track, 1 a building's lot, a lot area or a
        /// Placemaker build, 2 water, 3 the deck of an elevated road, path or track, or a pillar under one), which belong
        /// together (a road piece's surfaces: the edge's entity) and how high each stands above the ground (a deck's slices:
        /// the planner grows under the ones high enough). Kept by map cell (NeighbourPolishSystem.BarrierCache).</summary>
        private List<float2[]> BarriersIn(float2 min, float2 max, List<int> kinds, List<long> groups, HashSet<Entity> planned = null, List<float> heights = null) =>
        CachedBarriersIn(min, max, kinds, groups, planned, heights);

        // ───────────── something new built near gardens ─────────────

        private EntityQuery _newNearby, _goneNearby;
        /// <summary>Each polished house's garden as last written (this session): fixed for the passes that do not edit it.</summary>
        private readonly Dictionary<Entity, float2[]> _gardenOf = new Dictionary<Entity, float2[]>();
        private static int s_logPasses;

        /// <summary>Game runner (lab op "outgrowCheck"): each polished house's garden as last written this session.</summary>
        public IReadOnlyDictionary<Entity, float2[]> Gardens => _gardenOf;

        private void CreateNearbyQuery()
        {
            // a road, path, lot area, surface or building that is not a house, made this frame
            _newNearby = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Created>() },
                Any = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<global::Game.Areas.Lot>(), ComponentType.ReadOnly<global::Game.Areas.Surface>(), ComponentType.ReadOnly<global::Game.Buildings.Building>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PolishPiece>() },
            });
            // the same, bulldozed this frame: the player removed it, the gardens around may take the land (first too)
            _goneNearby = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Deleted>() },
                Any = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<global::Game.Areas.Lot>(), ComponentType.ReadOnly<global::Game.Areas.Surface>(), ComponentType.ReadOnly<global::Game.Buildings.Building>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<PolishPiece>() },
            });
        }

        /// <summary>
        /// Something built near polished gardens (a road, a path, a Placemaker build, a park, a service building): the gardens
        /// within reach of it are planned again, so they give that land back (they never grow into a build). Something
        /// bulldozed there likewise: they may take the land it leaves. Both are the player's own work: planned first.
        /// </summary>
        private void CollectNewNearby()
        {
            if (_newNearby.IsEmptyIgnoreFilter && _goneNearby.IsEmptyIgnoreFilter) return;
            var boxes = new List<(float2 Min, float2 Max)>();
            var sites = new List<float2[]>();
            foreach (var query in new[] { _newNearby, _goneNearby })
            {
                if (query.IsEmptyIgnoreFilter) continue;
                using (var all = query.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (boxes.Count > 2000) break;
                    if (EntityManager.HasComponent<global::Game.Buildings.Building>(e))
                    {
                        if (!EntityManager.HasComponent<global::Game.Objects.Transform>(e)) continue;
                        // new houses: their own pass; a house just started (a building site) moves the gardens off its lot now
                        if (IsGrowable(e) && !EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(e)) continue;
                        // (only the gardens over its lot: every garden reaching near one row house's site was 252 plans in the
                        // user's city of 15 000)
                        if (query == _newNearby && IsGrowable(e))
                        {
                            BuildingFootprints.Of(EntityManager, e, out var site, out _);
                            if (site != null && site.Length >= 3) { sites.Add(site); continue; }
                        }
                        var p = EntityManager.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                        boxes.Add((p - 40f, p + 40f));
                        continue;
                    }
                    if (EntityManager.HasComponent<Owner>(e))
                    {
                        var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                        if (owner != Entity.Null && EntityManager.HasComponent<global::Game.Buildings.Building>(owner) && IsGrowable(owner)) continue;   // a house's own lawn or lot
                    }
                    if (EntityManager.HasComponent<Curve>(e))
                    {
                        var b = MathUtils.Bounds(EntityManager.GetComponentData<Curve>(e).m_Bezier);
                        boxes.Add((b.min.xz, b.max.xz));
                    }
                    else if (EntityManager.HasBuffer<global::Game.Areas.Node>(e))
                    {
                        var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(e, true);
                        if (nodes.Length == 0) continue;
                        float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                        for (int k = 0; k < nodes.Length; k++) { lo = math.min(lo, nodes[k].m_Position.xz); hi = math.max(hi, nodes[k].m_Position.xz); }
                        boxes.Add((lo, hi));
                    }
                }
            }
            int marked = 0;
            foreach (var site in sites)
            {
                Placemaker.Core.Geometry.Poly.Bounds(site, out var slo, out var shi);
                foreach (var (house, glo, ghi) in GardenBoxes())
                {
                    if (ghi.x < slo.x || glo.x > shi.x || ghi.y < slo.y || glo.y > shi.y || !EntityManager.Exists(house) || !IsYes(house)) continue;
                    var garden = GardenOf(house);
                    if (garden == null || !Overlap(garden, site)) continue;
                    if (_dirty.Add(house)) { marked++; DirtyBy[2]++; }
                    _first.Add(house);
                }
            }
            if (boxes.Count == 0)
            {
                if (marked > 0) Mod.Log.Info($"Placemaker neighbourhood polish: a building started on land of {marked} gardens: planned again first");
                return;
            }
            EnsureGrid();
            // looked for as far as any garden reaches, each garden then by its own reach (its look's): with the widest reach of
            // any type for all of them, one type at 500 m planned again every outgrown house within 530 m of each new building
            float reach = MostReach() + 30f;
            foreach (var (bmin, bmax) in boxes)
            {
                var lo = bmin - reach;
                var hi = bmax + reach;
                for (float x = math.floor(lo.x / Cell) * Cell; x <= hi.x; x += Cell)
                    for (float z = math.floor(lo.y / Cell) * Cell; z <= hi.y; z += Cell)
                        if (_grid.TryGetValue(Key(new float2(x + 1f, z + 1f)), out var near))
                            foreach (int j in near)
                            {
                                var h = _gridHouses[j];
                                var p = h.T.m_Position.xz;
                                if (p.x < lo.x || p.y < lo.y || p.x > hi.x || p.y > hi.y || !EntityManager.Exists(h.E) || !IsYes(h.E)) continue;
                                float own = LotReach(h.E) + 30f;
                                if (p.x < bmin.x - own || p.y < bmin.y - own || p.x > bmax.x + own || p.y > bmax.y + own) continue;
                                if (_dirty.Add(h.E)) { marked++; DirtyBy[2]++; }
                                _first.Add(h.E);   // the player's build: the gardens it changes go first
                            }
            }
            if (marked > 0)
            {
                var what = new List<string>();
                foreach (var (query, verb) in new[] { (_newNearby, "built"), (_goneNearby, "bulldozed") })
                    using (var all = query.ToEntityArray(Allocator.Temp))
                        for (int k = 0; k < all.Length && what.Count < 4; k++)
                            if (EntityManager.HasComponent<PrefabRef>(all[k])) what.Add(verb + " " + NameOf(EntityManager.GetComponentData<PrefabRef>(all[k]).m_Prefab));
                Mod.Log.Info($"Placemaker neighbourhood polish: something built or bulldozed nearby ({string.Join(", ", what)}): {marked} gardens planned again first");
                s_logPasses = 4;
            }
        }

        /// <summary>An object's footprint (its geometry's bounds turned with it), or null without geometry.</summary>
        private float2[] ObjectBox(Entity e)
        {
            if (!EntityManager.HasComponent<global::Game.Objects.Transform>(e) || !EntityManager.HasComponent<PrefabRef>(e)) return null;
            var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
            if (!EntityManager.HasComponent<ObjectGeometryData>(prefab)) return null;
            var b = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
            if (b.max.x - b.min.x < 0.1f || b.max.z - b.min.z < 0.1f) return null;
            var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
            float2 W(float x, float z) => global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(x, 0f, z)).xz;
            return new[] { W(b.min.x, b.min.z), W(b.max.x, b.min.z), W(b.max.x, b.max.z), W(b.min.x, b.max.z) };
        }

        /// <summary>A net edge's ground as a strip along its curve (its full width; ends square at its nodes).</summary>
        private static float2[] Strip(Curve curve, float half)
        {
            var b = curve.m_Bezier;
            float len = math.max(curve.m_Length, 1f);
            int n = math.clamp((int)(len / 8f), 2, 24);
            var leftSide = new List<float2>(n + 1);
            var rightSide = new List<float2>(n + 1);
            for (int k = 0; k <= n; k++)
            {
                float t = (float)k / n;
                var p = MathUtils.Position(b, t).xz;
                var d = math.normalizesafe(MathUtils.Tangent(b, t).xz, new float2(1f, 0f));
                var nrm = new float2(-d.y, d.x);
                leftSide.Add(p + nrm * half);
                rightSide.Add(p - nrm * half);
            }
            rightSide.Reverse();
            leftSide.AddRange(rightSide);
            return leftSide.ToArray();
        }
    }
}
