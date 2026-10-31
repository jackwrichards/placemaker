// Placemaker — emitting CreationDefinitions for each plan element (GPL-3.0).
// Net courses follow ParkManager's CreateNetCourseDefinition and GridRoadGenerator's
// CreateGridDefinitions/MakeCoursePos (attach to an existing edge via m_Entity + m_SplitPosition,
// Upgraded composition flags on the definition). Object definitions follow ParkingLotTool's
// "field by field like ObjectToolBaseSystem" ObjectDefinition. Credits: tomkpunkt, Ti4goc,
// KruemelGames (GPL-3.0).

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private readonly HashSet<Entity> _ownDefinitions = new HashSet<Entity>();
        private readonly HashSet<long> _notedRings = new HashSet<long>();

        /// <summary>Setting: paved surfaces (paving/asphalt/plaza/decorative) draw on top (priority −94).</summary>
        public static bool OverlayPavedSurfaces = true;

        /// <summary>
        /// (s21) Decals take no space. A decal-only prop still has a 1 m tall collision box (a leaves decal: 6×1×6 m) and
        /// meets every prop, tree and path it lies on: the ghost cancels both, the build hides one (OverrideSystem).
        /// ObjectUtils.GetCollisionMask gives an object with Elevation &lt; 0 (and no Lowered flag) only the Underground
        /// mask, which no ground prop, tree or surface net shares (decompiled 1.6.2f1: Objects.ValidationHelpers
        /// ObjectIterator/NetIterator, OverrideSystem — all use the elevation-aware mask). So decals are emitted with a
        /// hair of negative elevation; the decal still projects onto the ground (its box spans ±0.5 m).
        /// Needs in-game confirmation (docs/DECALS.md, open problems).
        /// </summary>
        public static bool SinkDecals = true;
        public const float DecalSink = -0.01f;
        /// <summary>(s44) Stall markings and arrows sink like decals (PlanWork.SinkInBuild). Off = s25 behaviour.</summary>
        public static bool SinkStallMarkings = false;   // 2026-10-14 in game: sunk markings were rejected (3 of 18 built); plain markings built 401/401 before

        private static bool IsPaved(SurfaceRole r) => r == SurfaceRole.Paving || r == SurfaceRole.Asphalt || r == SurfaceRole.Plaza || r == SurfaceRole.Decorative;
        /// <summary>Resolved prefab per work item key.</summary>
        private readonly Dictionary<long, Entity> _itemPrefab = new Dictionary<long, Entity>();
        /// <summary>Emitted keys (plan keys and synthetic keys for fence pieces) → work item.</summary>
        private readonly Dictionary<long, WorkItem> _itemByKey = new Dictionary<long, WorkItem>();
        private PlanIndex _netIndex, _objectIndex, _areaIndex;
        private long _syntheticKey = 1L << 62;
        private readonly HashSet<string> _usedPrefabNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<Entity> _usedPrefabs = new HashSet<Entity>();
        private int _emittedObjects, _emittedAreas;
        private TerrainHeightData _heights;
        private bool _heightsValid;

        // ───────────────────────── prefab resolution ─────────────────────────

        private void ResolvePrefabs()
        {
            _itemPrefab.Clear();
            int missing = 0;
            foreach (var w in _work.All())
            {
                if (w.Kind == WorkKind.Terraform) continue;
                var prefab = _catalog.Resolve(w.Asset, w.Category);
                if (prefab == Entity.Null) { missing++; AuditMissing(w); continue; }
                _itemPrefab[PlanWork.Key(w)] = prefab;
            }
            if (missing > 0)
            {
                _warnings.Add("Placemaker.Build.AssetsMissing");
                Mod.Log.Warn($"Placemaker: {missing} planned parts have no usable asset in this playset and are skipped.");
            }
        }

        private void ResetIndexes()
        {
            _emittedObjects = _emittedAreas = 0;
            _netIndex = _objectIndex = _areaIndex = null;
            _itemByKey.Clear();
            _heightsValid = false;
        }

        private Entity PrefabOf(WorkItem w) => _itemPrefab.TryGetValue(PlanWork.Key(w), out var e) ? e : Entity.Null;

        private void NoteUsed(Entity prefab)
        {
            var n = _catalog.NameOf(prefab);
            if (!string.IsNullOrEmpty(n)) _usedPrefabNames.Add(n);
            _usedPrefabs.Add(prefab);
        }

        private static long PrefabId(Entity e) => ((long)e.Version << 32) | (uint)e.Index;

        private float Height(float2 p)
        {
            if (!_heightsValid)
            {
                _heights = _terrain.GetHeightData(true);
                _heightsValid = true;
            }
            float h = TerrainUtils.SampleHeight(ref _heights, new float3(p.x, 0f, p.y));
            return math.isfinite(h) ? h : 0f;
        }

        private int _sagNotes, _splitNotes;

        /// <summary>The curve parameter of a planned net nearest a point (16 samples).</summary>
        private static float ClosestT(PlannedNet n, float2 p)
        {
            float best = float.MaxValue, bt = 0.5f;
            for (int k = 0; k <= 16; k++) { float t = k / 16f; float d = math.distancesq(Composer.Bezier(n.A, n.B, n.C, n.D, t).xz, p); if (d < best) { best = d; bt = t; } }
            return bt;
        }

        private Entity NewDefinition()
        {
            var e = EntityManager.CreateEntity();
            _ownDefinitions.Add(e);
            return e;
        }

        // ───────────────────────── nets ─────────────────────────

        private void EmitNets()
        {
            _heightsValid = false;
            _sagNotes = _splitNotes = _wheelNotes = 0;
            // (2026-10-30) room in the terrain's road cull list before our nets come: TerrainCullGuard.cs (the CullForCascades crash)
            TerrainCullGuard.Reserve(_terrain, _work.Nets.Count);
            if (!_gEmission) ScanLivePits("before the nets");
            _netIndex = new PlanIndex(NetIndexCell);
            _objectIndex = _objectIndex ?? new PlanIndex(ObjectIndexCell);
            var random = new Unity.Mathematics.Random(Seeds.Mix(0x5EEDu, (uint)_groupId));
            var pathEnds = PlannedPathEnds();
            var groundEnds = GroundPathEnds();
            int clearNotes = 0;
            foreach (var w in _work.Nets)
            {
                var prefab = PrefabOf(w);
                if (prefab == Entity.Null) continue;
                var net = _request.Plan.Layers[w.Layer].Nets[w.Index];
                if (net.Role == NetRole.Fence && !EntityManager.HasComponent<NetData>(prefab))
                {
                    EmitFencePieces(w, net, prefab);
                    continue;
                }
                if (!EntityManager.HasComponent<NetGeometryData>(prefab)) continue;
                var settings = CourseFitSettings.Default;
                settings.FollowTerrain = net.FollowTerrain;
                settings.MaxLength = _catalog.MaxEdgeLength(prefab);
                var course = new Cubic(net.A, net.B, net.C, net.D);
                // A visible footpath's dead end whose round end would touch a city street is cut into it by the game, crosswalk
                // and all (Core/Layers/Common/StreetJoins): keep it off by the real path width.
                if (net.FollowTerrain && EntityManager.HasComponent<PathwayData>(prefab) && EntityManager.HasComponent<NetData>(prefab)
                    && (EntityManager.GetComponentData<NetData>(prefab).m_RequiredLayers & Layer.MarkerPathway) == 0 && _request.Plan.ExistingNets.Count > 0)
                {
                    var probe = new PlannedNet
                    {
                        A = net.A, B = net.B, C = net.C, D = net.D, Role = net.Role,
                        StartAttach = net.StartAttach >= 0 || pathEnds.Shared(net.A) ? 0 : -1,   // joined ends are not moved
                        EndAttach = net.EndAttach >= 0 || pathEnds.Shared(net.D) ? 0 : -1,
                    };
                    float half = EntityManager.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth * 0.5f;
                    if (Placemaker.Core.Layers.Common.StreetJoins.ClearOfRoads(probe, _request.Plan.ExistingNets, half) > 0)
                    {
                        course = new Cubic(probe.A, probe.B, probe.C, probe.D);
                        if (clearNotes++ < 12)
                            Mod.Log.Info($"Placemaker: a footpath end near a street was kept {half:F1} m (its half width) + {Placemaker.Core.Layers.Common.StreetJoins.KerbGap} m off it, so the game joins it to the sidewalk instead of cutting the street: ({net.A.x:F1}, {net.A.z:F1})-({net.D.x:F1}, {net.D.z:F1}) now ({probe.A.x:F1}, {probe.A.z:F1})-({probe.D.x:F1}, {probe.D.z:F1}).");
                    }
                }
                // Bridges/piers/boardwalks keep their planned deck: short pieces, each node with its own
                // elevation and forced elevated (CourseFitting.FitElevated explains why not one course).
                bool elevated = !net.FollowTerrain;
                // An end on ground higher than the deck rises to it (else the game digs a notch there); the paths that meet
                // it are pinned to the same ground, so the junction stays one node.
                if (elevated) course = CourseFitting.LandEnds(course, GroundHeight);
                // (p1-ground) a deck end on a ground path's end becomes that end exactly (one node, no overhang): see JoinEnds
                float2? joinA = elevated ? groundEnds.Find(net.A.xz) : null, joinD = elevated ? groundEnds.Find(net.D.xz) : null;
                if (joinA != null || joinD != null) course = CourseFitting.JoinEnds(course, GroundHeight, joinA, joinD);
                var pieces = elevated
                    ? CourseFitting.FitElevated(course, settings.MaxLength > 1f ? math.min(ElevatedPieceLength, settings.MaxLength) : ElevatedPieceLength)
                    : CourseFitting.Fit(course, GroundHeight, settings);
                long key = PlanWork.Key(w);
                bool any = false;
                for (int p = 0; p < pieces.Count; p++)
                {
                    var c = pieces[p];
                    if (c.Length() < CourseFitting.MinCourseLength) continue;
                    // (core) Pit diagnosis: a course that sags or humps more than 1 m away from the plan at its middle.
                    if (!elevated && _sagNotes < 12)
                    {
                        var mid = c.Position(0.5f);
                        float planned = Composer.Bezier(net.A, net.B, net.C, net.D, ClosestT(net, mid.xz)).y;
                        if (math.abs(mid.y - planned) > 1f)
                        {
                            _sagNotes++;
                            Mod.Log.Warn($"Placemaker net course off its plan: {net.Role} piece {p + 1}/{pieces.Count} at ({mid.x:F0}, {mid.z:F0}) y {c.A.y:F1}/{c.B.y:F1}/{c.C.y:F1}/{c.D.y:F1} (middle {mid.y:F1}), planned {planned:F1}; "
                                + $"ground {GroundHeight(c.Position(1f / 3f).xz):F1}/{GroundHeight(c.Position(2f / 3f).xz):F1} at 1/3, 2/3 (live {Height(c.Position(1f / 3f).xz):F1}/{Height(c.Position(2f / 3f).xz):F1}), length {c.Length():F1} m, {(_gEmission ? "ghost" : "build")}.");
                        }
                    }
                    var start = MakeCoursePos(c.A, c.Tangent(0), 0f, CoursePosFlags.IsFirst, p == 0 ? net.StartAttach : -1, prefab);
                    var end = MakeCoursePos(c.D, -c.Tangent(1), 1f, CoursePosFlags.IsLast, p == pieces.Count - 1 ? net.EndAttach : -1, prefab);
                    if (elevated) Elevate(ref start, ref end, c, p > 0, p < pieces.Count - 1);
                    if (p == 0 && joinA != null) start.m_Elevation = float2.zero;              // on the ground: the path's own node
                    if (p == pieces.Count - 1 && joinD != null) end.m_Elevation = float2.zero;
                    // Attached ends snap onto the existing net: rebuild the curve to the snapped points.
                    var curve = new Bezier4x3(c.A, c.B, c.C, c.D);
                    if (!start.m_Position.Equals(c.A) || !end.m_Position.Equals(c.D))
                    {
                        float3 da = start.m_Position - c.A, dd = end.m_Position - c.D;
                        curve = new Bezier4x3(start.m_Position, c.B + da, c.C + dd, end.m_Position);
                    }
                    start.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve));
                    end.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve));
                    var def = NewDefinition();
                    // Fences: one random seed for the whole build, so lane objects / colour variations of the
                    // fence net are the same on every segment (FEEDBACK-2 #1: "every segment a different fence").
                    int seed = net.Role == NetRole.Fence ? AutoAssets.UniformSeed(w.Category, (uint)_groupId) : random.NextInt();
                    EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = seed });
                    EntityManager.AddComponent<Updated>(def);
                    EntityManager.AddComponentData(def, new NetCourse
                    {
                        m_Curve = curve,
                        m_Length = MathUtils.Length(curve),
                        m_FixedIndex = -1,
                        m_Elevation = float2.zero,
                        m_StartPosition = start,
                        m_EndPosition = end,
                    });
                    var upgraded = ParseUpgrades(net.Upgrades);
                    // CourseSplitSystem copies Upgraded from the definition onto the edge (GRG);
                    // writing it on a finished edge crashed CS2 natively (PLT).
                    if (!upgraded.Equals(default(CompositionFlags))) EntityManager.AddComponentData(def, new Upgraded { m_Flags = upgraded });
                    _netIndex.AddCurve(key, PrefabId(prefab), new Cubic(curve.a, curve.b, curve.c, curve.d));
                    any = true;
                }
                if (any)
                {
                    _itemByKey[key] = w;
                    NoteUsed(prefab);
                }
            }
        }

        /// <summary>Every planned net end, to tell a dead end from an end other nets of the build join.</summary>
        private sealed class EndSet
        {
            private readonly Dictionary<long, int> _count = new Dictionary<long, int>();
            private static long Key(float3 p) => ((long)math.round(p.x * 10f) << 32) ^ (uint)(int)math.round(p.z * 10f);
            public void Add(float3 p) { long k = Key(p); _count[k] = (_count.TryGetValue(k, out var n) ? n : 0) + 1; }
            /// <summary>Two or more ends at p (within 0.1 m grid): a junction.</summary>
            public bool Shared(float3 p) => _count.TryGetValue(Key(p), out var n) && n > 1;
        }

        /// <summary>(p1-ground) The exact x, z of every ground-following net end, found from a point within <see cref="Reach"/> m.</summary>
        private sealed class GroundEndSet
        {
            public const float Reach = 0.15f;
            private readonly Dictionary<long, List<float2>> _cells = new Dictionary<long, List<float2>>();
            private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            public void Add(float2 p)
            {
                long k = Key((int)math.floor(p.x), (int)math.floor(p.y));
                if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<float2>(2);
                l.Add(p);
            }
            public float2? Find(float2 p)
            {
                int x = (int)math.floor(p.x), z = (int)math.floor(p.y);
                float best = Reach * Reach; float2? hit = null;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (_cells.TryGetValue(Key(x + dx, z + dz), out var l))
                            foreach (var q in l) { float d = math.distancesq(p, q); if (d <= best) { best = d; hit = q; } }
                return hit;
            }
        }

        private GroundEndSet GroundPathEnds()
        {
            var set = new GroundEndSet();
            foreach (var l in _request.Plan.Layers)
                if (l?.Nets != null)
                    foreach (var n in l.Nets)
                        if (n.FollowTerrain && n.Role != NetRole.Fence) { set.Add(n.A.xz); set.Add(n.D.xz); }
            return set;
        }

        private EndSet PlannedPathEnds()
        {
            var set = new EndSet();
            foreach (var l in _request.Plan.Layers)
                if (l?.Nets != null)
                    foreach (var n in l.Nets) { set.Add(n.A); set.Add(n.D); }
            return set;
        }

        /// <summary>
        /// Course end. Roads attach to an existing edge/node through CoursePos.m_Entity (GRG). Paths
        /// and invisible drivable paths do NOT: they join roads through LocalConnect at a dead end
        /// (PLT, RESEARCH-NOTES 15), so the node simply ends at the planned point.
        /// FreeHeight is left off on purpose: CourseSplitSystem would recompute heights (PLT).
        /// </summary>
        private CoursePos MakeCoursePos(float3 position, float3 tangent, float delta, CoursePosFlags flags, long attach, Entity prefab)
        {
            var pos = new CoursePos
            {
                m_Entity = Entity.Null,
                m_Position = position,
                m_Rotation = NetUtils.GetNodeRotation(tangent),
                m_CourseDelta = delta,
                m_Elevation = float2.zero,
                m_Flags = flags,
                m_ParentMesh = -1,
                m_SplitPosition = 0f,
            };
            if (EntityManager.HasComponent<RoadData>(prefab)) pos.m_Flags |= CoursePosFlags.IsLeft | CoursePosFlags.IsRight;
            if (attach < 0) return pos;
            var target = DecodeEntity(attach);
            if (!CanAttach(target, prefab)) return pos;
            if (EntityManager.HasComponent<Edge>(target))
            {
                var curve = EntityManager.GetComponentData<Curve>(target).m_Bezier;
                float d = MathUtils.Distance(curve.xz, position.xz, out float t);
                if (d > AttachReach(target, prefab)) return pos;
                var edge = EntityManager.GetComponentData<Edge>(target);
                if (t <= 0.01f) { pos.m_Entity = edge.m_Start; pos.m_SplitPosition = 0f; }
                else if (t >= 0.99f) { pos.m_Entity = edge.m_End; pos.m_SplitPosition = 1f; }
                else { pos.m_Entity = target; pos.m_SplitPosition = t; }
                pos.m_Position = MathUtils.Position(curve, t);
                if (EntityManager.HasComponent<PathwayData>(prefab) && _splitNotes++ < 12)
                    Mod.Log.Info($"Placemaker: a footpath end at ({position.x:F1}, {position.z:F1}) splits '{_catalog.NameOf(EntityManager.GetComponentData<PrefabRef>(target).m_Prefab)}' {d:F1} m from its centre line (an entrance with a crosswalk).");
            }
            else if (EntityManager.HasComponent<global::Game.Net.Node>(target))
            {
                var node = EntityManager.GetComponentData<global::Game.Net.Node>(target);
                if (math.distance(node.m_Position.xz, position.xz) > AttachTolerance) return pos;
                pos.m_Entity = target;
                pos.m_Position = node.m_Position;
            }
            return pos;
        }

        private const float ElevatedPieceLength = 12f;
        private const float ElevatedMin = 0.75f;

        /// <summary>
        /// Elevation (planned height above the live terrain) on both course ends; interior nodes and
        /// pieces whose deck is clearly above the ground are forced elevated, so CS2 builds a bridge at
        /// the planned heights instead of re-deriving a ground course from the terrain/water bed.
        /// </summary>
        private void Elevate(ref CoursePos start, ref CoursePos end, Cubic c, bool startInterior, bool endInterior)
        {
            float ea = c.A.y - Height(c.A.xz), ed = c.D.y - Height(c.D.xz);
            var mid = c.Position(0.5f);
            float em = mid.y - Height(mid.xz);
            start.m_Elevation = new float2(math.max(0f, ea));
            end.m_Elevation = new float2(math.max(0f, ed));
            if (startInterior && ea > ElevatedMin) start.m_Flags |= CoursePosFlags.ForceElevatedNode;
            if (endInterior && ed > ElevatedMin) end.m_Flags |= CoursePosFlags.ForceElevatedNode;
            if (em > ElevatedMin)
            {
                start.m_Flags |= CoursePosFlags.ForceElevatedEdge;
                end.m_Flags |= CoursePosFlags.ForceElevatedEdge;
            }
        }

        /// <summary>Attachments are stale after save/load (entity ids change); verify by geometry too.</summary>
        private const float AttachTolerance = 4f;

        /// <summary>
        /// How far from the target's centre line a planned end may lie and still attach. A footpath with a crosswalk
        /// (Core/Layers/Common/StreetJoins) ends at its entrance on the outline, beside the street: up to the street's half
        /// width plus the planner's frontage reach (Entrances.FrontageReach, 6 m) and a metre; it is carried onto the centre
        /// line, where the split gets the crosswalk. Everything else keeps the old 4 m.
        /// </summary>
        private float AttachReach(Entity target, Entity prefab)
        {
            if (!EntityManager.HasComponent<PathwayData>(prefab) || !EntityManager.HasComponent<PrefabRef>(target)) return AttachTolerance;
            var other = EntityManager.GetComponentData<PrefabRef>(target).m_Prefab;
            if (!EntityManager.HasComponent<RoadData>(other) || !EntityManager.HasComponent<NetGeometryData>(other)) return AttachTolerance;
            float half = EntityManager.GetComponentData<NetGeometryData>(other).m_DefaultWidth * 0.5f;
            return math.isfinite(half) && half > 0f ? math.max(AttachTolerance, half + 7f) : AttachTolerance;
        }

        private bool CanAttach(Entity target, Entity prefab)
        {
            if (target == Entity.Null || !EntityManager.Exists(target) || EntityManager.HasComponent<Deleted>(target)
                || EntityManager.HasComponent<Temp>(target) || !EntityManager.HasComponent<PrefabRef>(target)) return false;
            if (!EntityManager.HasComponent<Edge>(target) && !EntityManager.HasComponent<global::Game.Net.Node>(target)) return false;
            var other = EntityManager.GetComponentData<PrefabRef>(target).m_Prefab;
            bool mineRoad = EntityManager.HasComponent<RoadData>(prefab), otherRoad = EntityManager.HasComponent<RoadData>(other);
            bool minePath = EntityManager.HasComponent<PathwayData>(prefab), otherPath = EntityManager.HasComponent<PathwayData>(other);
            // Footpath entrances (parking US3/US4) may end on a road like a vanilla pedestrian path does.
            return (mineRoad && otherRoad) || (minePath && (otherPath || otherRoad));
        }

        /// <summary>Inverse of <see cref="NetRefs.Encode"/>.</summary>
        private Entity DecodeEntity(long id) => NetRefs.Decode(EntityManager, id);

        private static CompositionFlags ParseUpgrades(string[] names)
        {
            var flags = default(CompositionFlags);
            if (names == null) return flags;
            foreach (var raw in names)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var name = raw.Trim();
                string side = null;
                int colon = name.IndexOf(':');
                if (colon > 0) { side = name.Substring(0, colon).ToLowerInvariant(); name = name.Substring(colon + 1); }
                if (side == null || side == "general")
                {
                    if (Enum.TryParse(name, true, out CompositionFlags.General g) && g != 0) { flags.m_General |= g; continue; }
                    if (side == "general") continue;
                }
                if (Enum.TryParse(name, true, out CompositionFlags.Side s) && s != 0)
                {
                    if (side == null || side == "left") flags.m_Left |= s;
                    if (side == null || side == "right") flags.m_Right |= s;
                }
            }
            return flags;
        }

        private void EmitFencePieces(WorkItem w, PlannedNet net, Entity prefab)
        {
            if (!EntityManager.HasComponent<ObjectGeometryData>(prefab)) return;
            var size = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Size;
            float piece = math.max(0.5f, math.max(size.x, size.z));
            var c = new Cubic(net.A, net.B, net.C, net.D);
            float len = c.LengthXZ();
            // FEEDBACK-4: never overlapping — the game cancels (ghost) or hides (build) one of every two
            // overlapping props, which left every other hedge piece missing. Small even gaps instead.
            int count = Placemaker.Core.Layers.Common.Fences.SpacedPieceCount(len, piece);
            int seed = AutoAssets.UniformSeed(w.Category, (uint)_groupId);   // one look for every piece
            // Pieces run along their longer local axis (fences usually X; vanilla hedge pieces are long in Z).
            bool alongZ = size.z > size.x;
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                var p = c.Position(t);
                var tan = c.Tangent(t);
                float yaw = math.atan2(tan.x, tan.z) - (alongZ ? 0f : math.PI * 0.5f);
                long key = _syntheticKey++;
                _itemByKey[key] = w;
                EmitObjectDefinition(prefab, p, yaw, 1f, -1, (uint)i, key, true, seed);
            }
            NoteUsed(prefab);
        }

        // ───────────────────────── objects ─────────────────────────

        private void EmitObject(WorkItem w)
        {
            var prefab = PrefabOf(w);
            if (prefab == Entity.Null) return;
            var o = _request.Plan.Layers[w.Layer].Objects[w.Index];
            long key = PlanWork.Key(w);
            _itemByKey[key] = w;
            // Uniform categories (benches, lamps, bins…): one random seed per build, so colour/mesh
            // variations do not turn a row of one bench into a mix (FEEDBACK-2 #1).
            int? seed = AutoAssets.IsUniform(w.Category) ? AutoAssets.UniformSeed(w.Category, (uint)_groupId) : o.ColorSeed >= 0 ? o.ColorSeed : (int?)null;
            float elevation = SinkDecals && (o.Role == ObjectRole.Decal || SinkStallMarkings && PlanWork.SinkInBuild(o.Role)) ? DecalSink : 0f;
            if (o.Elevation > 0.01f && o.Role != ObjectRole.Decal) elevation = o.Elevation;   // (Industrial) stacked on another prop
            // (Industrial) set into the ground: lowered, with the game's negative Elevation, so its collision mask is Underground
            // only (ObjectUtils.GetCollisionMask) and it hides nothing standing on it (seen in the game runner: a car on sunk rubble,
            // barrels in it, all drawn; without it the game hid one of each pair).
            float sink = o.Elevation < -0.01f && o.Role != ObjectRole.Decal ? -o.Elevation : 0f;
            if (sink > 0f) elevation = -sink;
            // (p1-ground) an upright vehicle prop stands on its four wheels: one height at its middle left a 5-9 m car hanging over
            // the slope or the terrace step under one end (the user: floating cars in the industrial car parks)
            float3 pos = o.Position; float roll = o.Roll, pitch = o.Pitch; bool snap = true;
            if (roll == 0f && pitch == 0f && math.abs(o.Elevation) < 0.01f && IsVehicleProp(prefab) && OnWheels(prefab, o.Yaw, o.Scale, ref pos, out pitch, out roll))
                snap = false;
            EmitObjectDefinition(prefab, pos, o.Yaw, o.Scale, o.Age, o.Variation, key, snap, seed, elevation, optional: !IgnoreOwnCollisions && !PlanWork.IsFunctional(o.Role), roll: roll, pitch: pitch, sink: sink);   // (s59) anarchy: the ghost shows what the build places — nothing is cancelled
            NoteUsed(prefab);
        }

        /// <summary>Placemaker's own vehicle props (Game/Assets/VehiclePropSystem: parked cars, trucks, machines).</summary>
        private bool IsVehicleProp(Entity prefab)
        {
            var n = _catalog.NameOf(prefab);
            return n != null && n.StartsWith(Placemaker.Core.Layers.Types.Yards.VehicleProps.Prefix, StringComparison.Ordinal);
        }

        /// <summary>Most a parked vehicle tilts to its ground (rad, about 14 degrees): steeper ground is a step, and the car sits low.</summary>
        private const float MaxVehicleTilt = 0.25f;
        private int _wheelNotes;

        /// <summary>
        /// Height, pitch and roll that put a vehicle on its wheels: the ground under the four wheels (live terrain: the build has
        /// shaped it by now, and the game's height map rounds the terraform's steps), the body tilted to their plane and set on
        /// their mean, a little low so no wheel floats where the ground bends.
        /// </summary>
        private bool OnWheels(Entity prefab, float yaw, float scale, ref float3 pos, out float pitch, out float roll)
        {
            pitch = roll = 0f;
            if (!EntityManager.HasComponent<ObjectGeometryData>(prefab)) return false;
            var size = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Size * (scale > 0.05f ? scale : 1f);
            if (!(size.z > 1f) || !(size.x > 0.5f)) return false;
            float2 fwd = new float2(math.sin(yaw), math.cos(yaw)), right = new float2(math.cos(yaw), -math.sin(yaw));
            float l = size.z * 0.36f, w = size.x * 0.38f;
            var c = pos.xz;
            float fl = Height(c + fwd * l - right * w), fr = Height(c + fwd * l + right * w);
            float bl = Height(c - fwd * l - right * w), br = Height(c - fwd * l + right * w);
            pitch = -math.clamp(math.atan(((fl + fr) - (bl + br)) * 0.5f / (2f * l)), -MaxVehicleTilt, MaxVehicleTilt);
            roll = math.clamp(math.atan(((fr + br) - (fl + bl)) * 0.5f / (2f * w)), -MaxVehicleTilt, MaxVehicleTilt);
            float mean = (fl + fr + bl + br) * 0.25f, low = math.min(math.min(fl, fr), math.min(bl, br));
            pos.y = math.max(low, mean - 0.06f);
            // the old way stood it at one height at its middle (the terraform target there): how far its lowest wheel hung over the ground
            float gap = GroundHeight(c) - low;
            if (gap > 0.15f && _wheelNotes++ < 10 && !_gEmission)
                Mod.Log.Info($"Placemaker: vehicle prop at ({c.x:F0}, {c.y:F0}) set on its wheels: one height at its middle would have left a wheel {gap:F2} m over the ground (tilt {math.degrees(pitch):F1} / {math.degrees(roll):F1} degrees).");
            if (pitch == 0f) pitch = 1e-6f;   // a non-zero tilt is what makes the emit tilt the body
            return true;
        }

        private static readonly float[] AgeStages = { 0f, 0.17500001f, 0.425f, 0.77500004f, 0.97f };   // ToolUtils.GetRandomAge; [4] = dead (ObjectUtils.InitializeTreeState: age ≥ 0.95)

        private void EmitObjectDefinition(Entity prefab, float3 position, float yaw, float scale, int age, uint variation, long key, bool snapToGround, int? randomSeed = null, float elevation = 0f, bool optional = true, float roll = 0f, float pitch = 0f, float sink = 0f)
        {
            _objectIndex = _objectIndex ?? new PlanIndex(ObjectIndexCell);
            var p = position;
            if (snapToGround || !math.isfinite(p.y)) p.y = GroundHeight(p.xz);
            if (elevation > 0f) p.y += elevation;   // raised props (stacked containers) stand this high above the ground
            p.y -= sink;                            // sunk props (boulders, rubble half in the earth) stand this deep in it
            var rotation = quaternion.RotateY(yaw);
            // (Industrial) a tilted prop: a wreck on its roof or its side.
            if (roll != 0f || pitch != 0f) rotation = math.mul(rotation, math.mul(quaternion.RotateX(pitch), quaternion.RotateZ(roll)));
            var def = NewDefinition();
            EntityManager.AddComponentData(def, new CreationDefinition
            {
                m_Prefab = prefab,
                m_RandomSeed = randomSeed ?? (int)Seeds.Mix(variation, (uint)key),
                // Ghost: a colliding object is silently cancelled (hidden, no error icon) like a vanilla brush.
                // (s25) Never for functional parking pieces (PlanWork.IsFunctional): overlapping optional bays cancel each other.
                m_Flags = _gEmission && optional ? CreationFlags.Optional : 0,
            });
            EntityManager.AddComponent<Updated>(def);
            EntityManager.AddComponentData(def, new ObjectDefinition
            {
                m_Position = p,
                m_LocalPosition = p,
                m_Rotation = rotation,
                m_LocalRotation = rotation,
                m_Scale = new float3(math.isfinite(scale) && scale > 0.05f ? scale : 1f),
                m_Intensity = 1f,
                m_Age = age >= 0 && age < AgeStages.Length ? AgeStages[age] : AgeStages[2],
                m_Probability = 100,
                m_PrefabSubIndex = -1,
                m_ParentMesh = -1,
                m_Elevation = elevation,
            });
            _objectIndex.AddPoint(key, PrefabId(prefab), p.xz);
            _emittedObjects++;
            _usedPrefabs.Add(prefab);
        }

        // ───────────────────────── surfaces and areas ─────────────────────────

        private void EmitSurface(WorkItem w)
        {
            var s = _request.Plan.Layers[w.Layer].Surfaces[w.Index];
            // Parking surfaces must draw over roads (priority −94, Terrain|Roads decal layers): use
            // the runtime overlay clone when it is ready; otherwise the original still builds.
            // US6: paved surfaces of parks/plazas also use the clone, so they stack deterministically
            // above grass/ground (vanilla −100…−95) instead of z-fighting where they overlap.
            var prefab = SurfacePrefab(w, s);
            if (prefab == Entity.Null) return;
            EmitPreparedRings(prefab, s.Ring, w, true);
        }

        private void EmitArea(WorkItem w)
        {
            var prefab = PrefabOf(w);
            if (prefab == Entity.Null) return;
            if (VanillaIndustryLotRefused(prefab, w)) return;
            var a = _request.Plan.Layers[w.Layer].Areas[w.Index];
            EmitPreparedRings(prefab, a.Ring, w, false);
        }

        /// <summary>
        /// (core) No vanilla extractor or storage lot ("Agriculture Lot - Field", "Extractor Lot", warehouse yards) in a build that is not a
        /// farm or industry type: they carry their own economy, a crop surface, a border fence across entrances, and CanOverrideObjects,
        /// which hides every tree, bush and lamp inside them (Game.Prefabs.AreaInitializeSystem, Game.Objects.OverrideSystem.AreaIterator).
        /// </summary>
        private bool VanillaIndustryLotRefused(Entity prefab, WorkItem w)
        {
            if (!EntityManager.HasComponent<ExtractorAreaData>(prefab) && !EntityManager.HasComponent<StorageAreaData>(prefab)) return false;
            var group = GeneratorGroups.Of(GeneratorRegistry.Get(w.GeneratorId ?? ""));
            if (group == "farms" || group == "industrial") return false;
            if (_refusedLots++ == 0)
                Mod.Log.Warn($"Placemaker: left out the vanilla industry lot '{_prefabs.GetPrefabName(prefab)}' planned by '{w.GeneratorId}' (no farm or extractor lot outside farm and industry builds).");
            return true;
        }

        private int _refusedLots;

        /// <summary>
        /// US8 root-cause fix for ErrorType.InvalidShape: every ring is checked against the game's own
        /// shape rules for its prefab (AreaRings, d = m_SnapDistance / 2) and, for surfaces, the
        /// triangulation model. Offending nodes are removed; a ring that cannot be repaired is split
        /// into acceptable pieces (extra pieces get synthetic keys mapped to the same work item) or,
        /// as the last resort, skipped and reported.
        /// </summary>
        private float RingNodeDistance(Entity prefab)
        {
            float d = AreaRings.SurfaceMinNodeDistance;
            if (EntityManager.HasComponent<AreaGeometryData>(prefab))
            {
                float snap = EntityManager.GetComponentData<AreaGeometryData>(prefab).m_SnapDistance;
                if (math.isfinite(snap) && snap > 0f) d = AreaRings.MinNodeDistanceForSnap(snap);
            }
            return d;
        }

        /// <summary>The prefab a surface work item is emitted with (EmitSurface's choice, overlay clone included).</summary>
        private Entity SurfacePrefab(WorkItem w, PlannedSurface s)
        {
            var prefab = PrefabOf(w);
            if (prefab == Entity.Null) return prefab;
            if (w.GeneratorId == "parking" || (OverlayPavedSurfaces && IsPaved(s.Role))) prefab = _clones.OverlayFor(prefab);
            return prefab;
        }

        /// <summary>
        /// (perf) Every surface and area ring of _work checked and repaired on a worker (Prepared, kept with each ring), so the
        /// emission that follows only copies them: the first ghost batch of a big lot spent 323 ms of one frame on it.
        /// </summary>
        private System.Threading.Tasks.Task PrepareRingsAsync()
        {
            var jobs = new List<(float3[] ring, float d, bool surface)>();
            foreach (var w in _work.Surfaces)
            {
                var s = _request.Plan.Layers[w.Layer].Surfaces[w.Index];
                var prefab = SurfacePrefab(w, s);
                if (prefab != Entity.Null && s.Ring != null && s.Ring.Length >= 3) jobs.Add((s.Ring, RingNodeDistance(prefab), true));
            }
            foreach (var w in _work.Areas)
            {
                var a = _request.Plan.Layers[w.Layer].Areas[w.Index];
                var prefab = PrefabOf(w);
                if (prefab != Entity.Null && a.Ring != null && a.Ring.Length >= 3) jobs.Add((a.Ring, RingNodeDistance(prefab), false));
            }
            if (jobs.Count == 0) return System.Threading.Tasks.Task.CompletedTask;
            return System.Threading.Tasks.Task.Run(() => { foreach (var (ring, d, surface) in jobs) Prepared(ring, d, surface); });
        }

        private void EmitPreparedRings(Entity prefab, float3[] ring, WorkItem w, bool surface)
        {
            if (ring == null || ring.Length < 3) return;
            float d = RingNodeDistance(prefab);
            var prepared = Prepared(ring, d, surface);
            var ring2 = prepared.Ring2;
            List<float2[]> rings = prepared.Rings;
            if (!prepared.AsIs)
            {
                var at = new float3(w.Anchor.x, Height(w.Anchor), w.Anchor.y);
                bool first = _notedRings.Add(PlanWork.Key(w));   // retries re-emit: count each piece once
                if (rings.Count == 0)
                {
                    if (first) NoteSkip(surface ? "Placemaker.Build.Skipped.Surface" : "Placemaker.Build.Skipped.Area", at);
                    if (first && !_gEmission) Mod.Log.Info($"Placemaker skipped a {(surface ? "surface" : "area")} ring the game would reject ({ring.Length} nodes, d={d:0.###}; {w.GeneratorId} layer {w.Layer} #{w.Index}, first violation at node {AreaRings.FirstViolation(ring2, d + AreaRings.Margin)}): {string.Join(" ", System.Linq.Enumerable.Select(ring2, p => $"({p.x:F2},{p.y:F2})"))}.");
                    return;
                }
                if (first) NoteSkip(surface ? "Placemaker.Build.Repaired.Surface" : "Placemaker.Build.Repaired.Area", at, IssueSeverity.Info);
            }
            long key = PlanWork.Key(w);
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i];
                var r3 = new float3[r.Length];
                for (int k = 0; k < r.Length; k++) r3[k] = new float3(r[k].x, 0f, r[k].y);
                long k2 = i == 0 ? key : _syntheticKey++;
                EmitAreaDefinition(prefab, r3, k2, w);
            }
        }

        // (perf) The checks and repairs of a ring (AreaRings, SurfaceAcceptance) depend on its corners and d only, and the preview
        // and the build emit the very same plan rings: worked out once per ring and kept with it (a big lot: ~0.5 s per emission
        // for 740 rings, on the game's thread). Corners changed in place: worked out again.
        private sealed class PreparedRing
        {
            public float D; public bool Surface; public double Sum;
            public float2[] Ring2; public bool AsIs; public List<float2[]> Rings;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<float3[], PreparedRing> s_prepared =
            new System.Runtime.CompilerServices.ConditionalWeakTable<float3[], PreparedRing>();

        private static PreparedRing Prepared(float3[] ring, float d, bool surface)
        {
            double sum = 0;
            for (int i = 0; i < ring.Length; i++) sum += ring[i].x * (i + 1.0) + ring[i].z * (i + 7.0);
            if (s_prepared.TryGetValue(ring, out var p) && p.D == d && p.Surface == surface && p.Sum == sum) return p;
            p = new PreparedRing { D = d, Surface = surface, Sum = sum, Ring2 = Placemaker.Core.Geometry.Poly.To2D(ring) };
            if (AreaRings.IsValid(p.Ring2, d + AreaRings.Margin) && (!surface || Placemaker.Core.Geometry.SurfaceAcceptance.IsAccepted(p.Ring2)))
            {
                p.AsIs = true;
                p.Rings = new List<float2[]> { p.Ring2 };
            }
            else if (surface) p.Rings = AreaRings.PrepareSurface(p.Ring2, d);
            else
            {
                var v = AreaRings.MakeValid(p.Ring2, d);
                p.Rings = v != null ? new List<float2[]> { v } : new List<float2[]>();
            }
            s_prepared.Remove(ring);
            s_prepared.Add(ring, p);
            return p;
        }

        private Entity EmitAreaDefinition(Entity prefab, float3[] ring, long key, WorkItem w)
        {
            if (ring == null || ring.Length < 3) return Entity.Null;
            if (!EntityManager.HasComponent<AreaData>(prefab) || !EntityManager.GetComponentData<AreaData>(prefab).m_Archetype.Valid) return Entity.Null;
            _areaIndex = _areaIndex ?? new PlanIndex(AreaIndexCell);
            var def = NewDefinition();
            EntityManager.AddComponentData(def, new CreationDefinition { m_Prefab = prefab });
            EntityManager.AddComponent<Updated>(def);
            var nodes = EntityManager.AddBuffer<global::Game.Areas.Node>(def);
            nodes.ResizeUninitialized(ring.Length + 1);
            for (int i = 0; i < ring.Length; i++)
            {
                var p = ring[i];
                // float.MinValue elevation = "on the ground" (vanilla AreaToolSystem).
                nodes[i] = new global::Game.Areas.Node(new float3(p.x, GroundHeight(p.xz), p.z), float.MinValue);
            }
            nodes[ring.Length] = nodes[0];   // vanilla closes the ring by repeating the first node
            _areaIndex.AddPoint(key, PrefabId(prefab), PlanWork.Centroid(ring));
            _emittedAreas++;
            _itemByKey[key] = w;
            NoteUsed(prefab);
            return def;
        }
    }

    /// <summary>
    /// Encoding of existing net entities in Core's opaque ids (BoundaryAttachment.ExternalId,
    /// NearbyNet.ExternalId). The tool workstream must use the same encoding.
    /// </summary>
    public static class NetRefs
    {
        public static long Encode(Entity e) => e == Entity.Null ? -1 : ((long)e.Version << 32) | (uint)e.Index;

        public static Entity Decode(EntityManager em, long id)
        {
            if (id < 0) return Entity.Null;
            var e = new Entity { Index = (int)(uint)(id & 0xFFFFFFFF), Version = (int)(id >> 32) };
            return e.Index >= 0 && e.Version > 0 && em.Exists(e) ? e : Entity.Null;
        }
    }
}
