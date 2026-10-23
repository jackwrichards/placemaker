// Placemaker neighbourhood polish — bushes planted in stitched gardens, owned by the house (GPL-3.0).
//
// YardStitch plans beds along the new fences and hedges with bushes on them (StitchPlan.Bushes). Each bush is created the
// way the game spawns a growable's pieces (ZoneSpawnSystem): a CreationDefinition flagged Permanent, with the house as
// owner, and an ObjectDefinition, Updated and Deleted, just before GenerateObjectsSystem (Modification1) makes the object
// in the same frame. Owned by the house: it is one of the house's own pieces, bulldozed with it; a rebuild (level-up)
// removes it with the house's other pieces and the house is planned again, and the game's SubObjectSystem removes it
// whenever people or a company move in or out (it rebuilds the house's objects): it is made again where it stood
// (NeighbourPolishSystem.Rebuilt). One per spot: a piece of the same kind the house already owns at the same spot and
// angle is left alone.

using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourObjectSystem : GameSystemBase
    {
        public sealed class Request
        {
            public string Name;
            /// <summary>The piece's prefab when known (a piece the game took away, made again as it was); else by <see cref="Name"/>.</summary>
            public Entity Prefab;
            public float2 Position;
            public float Yaw;
            public Entity Owner;
            /// <summary>The look of the piece (its colour and mesh variation); 0: by where it stands.</summary>
            public int Seed;
        }

        public static readonly List<Request> Queue = new List<Request>();
        public static int TotalMade;
        private const int MaxPerFrame = 250;

        private PrefabSystem _prefabs;
        private TerrainSystem _terrain;
        private Dictionary<string, Entity> _byName;
        private static HashSet<string> _pieces;

        /// <summary>A piece the game has (its fence kind's; a 2 m piece the kind lacks: its 4 m one), else a low hedge piece.</summary>
        public static string PieceName(string name)
        {
            if (_pieces == null || _pieces.Contains(name)) return name;
            var full = name?.Replace("Short", "");
            return full != null && _pieces.Contains(full) ? full : "HedgePieceLow01";
        }
        private EntityArchetype _definition;

        /// <summary>A hair under the ground for a piece whose prefab takes zone cells: a car park's stall marks (the game
        /// gives an object with lanes of its own OccupyZone) kept land zoned under a car park from ever growing a building
        /// (the user, 22 Oct; the game runner: 108 of 108 cells zoned inside shop yards taken by ParkingLotDecal01). The game
        /// skips an object below the ground when it marks cells taken (CellOccupyJobs); it is drawn the same
        /// (BuildService.DecalSink).</summary>
        public const float ZoneSink = -0.01f;
        /// <summary>Off: such pieces laid on the ground as before (the game runner's lab op zoneSink: a city saved before).</summary>
        public static bool SinkZonePieces = true;

        public static bool TakesZone(EntityManager em, Entity prefab) =>
            em.HasComponent<ObjectGeometryData>(prefab) && !em.HasComponent<BuildingData>(prefab)
            && (em.GetComponentData<ObjectGeometryData>(prefab).m_Flags & global::Game.Objects.GeometryFlags.OccupyZone) != 0;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
            _definition = EntityManager.CreateArchetype(ComponentType.ReadWrite<CreationDefinition>(), ComponentType.ReadWrite<ObjectDefinition>(), ComponentType.ReadWrite<Updated>(), ComponentType.ReadWrite<Deleted>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // (its main thread time, for the game runner's outgrowFrames)
            long timed = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateTimed(); }
            finally { NeighbourPolishSystem.NoteSystem(5, timed); }
        }

        private void UpdateTimed()
        {
            if (Queue.Count == 0) return;
            try
            {
                if (_byName == null) Index();
                var heights = _terrain.GetHeightData(true);
                // a few hundred a frame: a whole city of closed gardens never lands in one frame
                int take = Math.Min(Queue.Count, MaxPerFrame);
                for (int qi = 0; qi < take; qi++)
                {
                    var r = Queue[qi];
                    if (r.Owner == Entity.Null || !EntityManager.Exists(r.Owner) || EntityManager.HasComponent<Deleted>(r.Owner)) continue;
                    var prefab = r.Prefab;
                    if (prefab == Entity.Null && (string.IsNullOrEmpty(r.Name) || !_byName.TryGetValue(r.Name, out prefab))) continue;
                    if (!EntityManager.Exists(prefab)) continue;
                    if (Owns(r.Owner, prefab, r.Position, r.Yaw)) continue;
                    var pos = new float3(r.Position.x, TerrainUtils.SampleHeight(ref heights, new float3(r.Position.x, 0f, r.Position.y)), r.Position.y);
                    var rot = quaternion.RotateY(r.Yaw);
                    var def = EntityManager.CreateEntity(_definition);
                    EntityManager.SetComponentData(def, new CreationDefinition
                    {
                        m_Prefab = prefab, m_Owner = r.Owner, m_Flags = CreationFlags.Permanent,
                        m_RandomSeed = r.Seed != 0 ? r.Seed : (int)math.hash(new float2(math.round(r.Position.x * 4f), math.round(r.Position.y * 4f))),
                    });
                    EntityManager.SetComponentData(def, new ObjectDefinition
                    {
                        m_ParentMesh = -1, m_Position = pos, m_Rotation = rot, m_LocalPosition = pos, m_LocalRotation = rot,
                        m_Scale = new float3(1f), m_Intensity = 1f, m_Probability = 100, m_PrefabSubIndex = -1,
                        m_Age = 0.425f,   // grown, not a sapling (ToolUtils.GetRandomAge stage 2)
                        m_Elevation = SinkZonePieces && TakesZone(EntityManager, prefab) ? ZoneSink : 0f,
                    });
                    NeighbourPolishSystem.PendingTags.Add(new NeighbourPolishSystem.PendingTag { Owner = r.Owner, Prefab = prefab, At = r.Position, Kind = PolishPieceKind.Garden, Frame = NeighbourPolishSystem.LotFrame });
                    TotalMade++;
                }
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker neighbourhood bushes failed: " + e.Message); Queue.Clear(); return; }
            Queue.RemoveRange(0, Math.Min(Queue.Count, MaxPerFrame));
        }

        /// <summary>The house already owns this piece: of this kind, at this spot, at this angle (PieceIdentity; after a load our
        /// list of what we made is empty).</summary>
        private bool Owns(Entity owner, Entity prefab, float2 at, float yaw)
        {
            if (!EntityManager.HasBuffer<global::Game.Objects.SubObject>(owner)) return false;
            var subs = EntityManager.GetBuffer<global::Game.Objects.SubObject>(owner, true);
            for (int i = 0; i < subs.Length; i++)
            {
                var o = subs[i].m_SubObject;
                if (!EntityManager.Exists(o) || EntityManager.HasComponent<Deleted>(o) || !EntityManager.HasComponent<PrefabRef>(o)) continue;
                if (EntityManager.GetComponentData<PrefabRef>(o).m_Prefab != prefab || !EntityManager.HasComponent<Transform>(o)) continue;
                var t = EntityManager.GetComponentData<Transform>(o);
                if (PieceIdentity.SameSpot(t.m_Position.xz, PieceIdentity.YawOf(t.m_Rotation), at, yaw)) return true;
            }
            return false;
        }

        private void Index()
        {
            _byName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(Placemaker.Core.Neighbours.YardStitch.BushKinds, StringComparer.OrdinalIgnoreCase);
            _pieces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<ObjectGeometryData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    string n;
                    try { n = _prefabs.GetPrefab<PrefabBase>(e)?.name; } catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(n) || _byName.ContainsKey(n)) continue;
                    // fence and hedge pieces (what fences are made of): every kind, for closing gardens
                    bool piece = n.IndexOf("Piece", StringComparison.OrdinalIgnoreCase) >= 0 && (n.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (piece) _pieces.Add(n);
                    if (wanted.Contains(n) || piece) _byName[n] = e;
                }
            Mod.Log.Info($"Placemaker neighbourhood bushes: {_byName.Count} of {wanted.Count} kinds found");
        }
    }
}
