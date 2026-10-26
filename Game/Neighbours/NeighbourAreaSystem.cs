// Placemaker neighbourhood polish — ground surfaces on shared land no house takes (GPL-3.0).
//
// The middle a ring of houses leaves (YardStitch StitchPlan.Commons) becomes a shared green: a lawn and a mulch bed, vanilla
// surfaces (the base game has no decorative ground decals: docs/DECALS.md). A house with land no lawn of its own reaches
// (StitchPlan.NewLawns: a gravel yard, the far side of a driveway) gets a lawn OWNED by the house: it is listed among the
// house's own areas (SubAreaReferencesSystem) and deleted with it. Both are created the way the game creates permanent
// pieces (ZoneSpawnSystem): a CreationDefinition flagged Permanent (with the owner, when there is one) and the ring as nodes,
// Updated and Deleted, just before GenerateAreasSystem (Modification1) turns it into a real surface in the same frame. One
// per spot (an existing surface of the same kind with the same centre is left alone).

using System;
using System.Collections.Generic;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourAreaSystem : GameSystemBase
    {
        public sealed class Request
        {
            /// <summary>The surface prefab; when Entity.Null, the vanilla one called <see cref="Name"/>.</summary>
            public Entity Prefab;
            public string Name;
            public float2[] Ring;
            /// <summary>The house that owns it (Entity.Null: free-standing).</summary>
            public Entity Owner;
            /// <summary>What it is to the polish (tagged PolishPiece when the game made it, so Unpolish finds it after a load).</summary>
            public PolishPieceKind Kind;
        }

        public static readonly List<Request> Queue = new List<Request>();
        public static int TotalMade;

        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private Dictionary<string, Entity> _byName;
        private readonly HashSet<long> _made = new HashSet<long>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // (its main thread time, for the game runner's outgrowFrames)
            long timed = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateTimed(); }
            finally { NeighbourPolishSystem.NoteSystem(3, timed); }
        }

        private void UpdateTimed()
        {
            if (Queue.Count == 0) return;
            try
            {
                if (_byName == null) Index();
                var heights = _terrain.GetHeightData(true);
                _centres = new Dictionary<Entity, List<(float2 Centre, float2[] Ring)>>();
                // a few dozen a frame (each is triangulated by the game)
                int take = Math.Min(Queue.Count, MaxPerFrame);
                for (int qi = 0; qi < take; qi++)
                {
                    var r = Queue[qi];
                    var ring = r.Ring;
                    var prefab = r.Prefab;
                    if (ring == null || ring.Length < 3 || (prefab == Entity.Null && (string.IsNullOrEmpty(r.Name) || !_byName.TryGetValue(r.Name, out prefab)))) continue;
                    if (r.Owner != Entity.Null && (!EntityManager.Exists(r.Owner) || EntityManager.HasComponent<Deleted>(r.Owner))) continue;
                    var c = float2.zero;
                    foreach (var p in ring) c += p;
                    c /= ring.Length;
                    long key = ((long)math.round(c.x) << 32) ^ (uint)(int)math.round(c.y) ^ ((long)prefab.Index << 20);
                    // free-standing ones once per spot; owned ones whenever the house has lost theirs (a rebuild deletes it)
                    if ((r.Owner == Entity.Null && !_made.Add(key)) || Exists(prefab, c, r.Kind == PolishPieceKind.Common ? null : ring)) continue;
                    if (!_centres.TryGetValue(prefab, out var mine)) _centres[prefab] = mine = new List<(float2, float2[])>();
                    mine.Add((c, ring));
                    var def = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_Owner = r.Owner, m_Flags = CreationFlags.Permanent });
                    var nodes = EntityManager.AddBuffer<Node>(def);
                    foreach (var p in ring) nodes.Add(new Node(new float3(p.x, TerrainUtils.SampleHeight(ref heights, new float3(p.x, 0f, p.y)), p.y), float.MinValue));
                    nodes.Add(nodes[0]);
                    EntityManager.AddComponent<Updated>(def);
                    EntityManager.AddComponent<Deleted>(def);
                    if (r.Owner != Entity.Null)
                        NeighbourPolishSystem.PendingTags.Add(new NeighbourPolishSystem.PendingTag { Owner = r.Owner, Prefab = prefab, At = PieceIdentity.AreaCentre(ring, out _), Area = true, Kind = r.Kind, Frame = NeighbourPolishSystem.LotFrame });
                    TotalMade++;
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker neighbourhood surfaces failed: " + e.Message); Queue.Clear(); return; }
            Queue.RemoveRange(0, Math.Min(Queue.Count, MaxPerFrame));
        }

        // the surfaces made this frame (not in the game's search tree yet), by kind
        private Dictionary<Entity, List<(float2 Centre, float2[] Ring)>> _centres;
        private const int MaxPerFrame = 40;
        private SearchSystem _areaSearch;

        private struct NearIterator : Colossal.Collections.INativeQuadTreeIterator<AreaSearchItem, global::Game.Common.QuadTreeBoundsXZ>
        {
            public Colossal.Mathematics.Bounds2 Bounds;
            public NativeList<Entity> Results;
            public bool Intersect(global::Game.Common.QuadTreeBoundsXZ bounds) => Colossal.Mathematics.MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);
            public void Iterate(global::Game.Common.QuadTreeBoundsXZ bounds, AreaSearchItem item)
            {
                if (Colossal.Mathematics.MathUtils.Intersect(bounds.m_Bounds.xz, Bounds)) Results.Add(item.m_Area);
            }
        }

        /// <summary>A surface of this kind already there: with this very outline (a garden's piece: PieceIdentity), or for a
        /// shared green (ring null) centred here. The ones standing are looked for around the spot in the game's area search
        /// tree: every surface of the city read for each frame with requests was 15 to 80 ms in a city of 15 000.</summary>
        private bool Exists(Entity prefab, float2 centre, float2[] ring)
        {
            bool Same(float2 p, float2[] r) => ring == null ? math.distance(p, centre) < 1.5f : r != null && math.distance(p, centre) < 2f && PieceIdentity.SameRing(ring, r);
            if (_centres.TryGetValue(prefab, out var mine))
                foreach (var (p, r) in mine) if (Same(p, r)) return true;
            var lo = centre - 2f;
            var hi = centre + 2f;
            if (ring != null) foreach (var p in ring) { lo = math.min(lo, p - 2f); hi = math.max(hi, p + 2f); }
            _areaSearch ??= World.GetOrCreateSystemManaged<SearchSystem>();
            var found = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                var tree = _areaSearch.GetSearchTree(true, out var deps);
                deps.Complete();
                var it = new NearIterator { Bounds = new Colossal.Mathematics.Bounds2(lo, hi), Results = found };
                tree.Iterate(ref it);
                var seen = new HashSet<Entity>();
                for (int i = 0; i < found.Length; i++)
                {
                    var e = found[i];
                    if (!seen.Add(e) || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Temp>(e)) continue;
                    if (!EntityManager.HasComponent<Surface>(e) || !EntityManager.HasBuffer<Node>(e) || EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != prefab) continue;
                    var nodes = EntityManager.GetBuffer<Node>(e, true);
                    if (nodes.Length < 3) continue;
                    var r = new float2[nodes.Length];
                    for (int k = 0; k < r.Length; k++) r[k] = nodes[k].m_Position.xz;
                    if (Same(PieceIdentity.Centre(r), r)) return true;
                }
            }
            finally { found.Dispose(); }
            return false;
        }

        private void Index()
        {
            _byName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
            var vanillaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<AreaData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    string n;
                    bool vanilla;
                    try { var p = _prefabs.GetPrefab<PrefabBase>(e); n = p?.name; vanilla = p != null && p.isBuiltin; } catch (Exception) { continue; }
                    // a vanilla surface wins its name (a mod that reuses the name never does)
                    if (!string.IsNullOrEmpty(n) && (!_byName.ContainsKey(n) || vanilla)) { if (!_byName.ContainsKey(n) || !vanillaNames.Contains(n)) _byName[n] = e; if (vanilla) vanillaNames.Add(n); }
                }
        }
    }
}
