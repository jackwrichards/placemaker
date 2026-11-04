// In-game test suite: builds the fixture world fresh every run (docs/teardown/10-ingame-suite.md §5, revised): the game's
// flat default world, one heightmap replacement (slope rows, rough blocks), a grid of vanilla roads, residential zoning
// on the zoned row's south street, two plopped park buildings in the "survive" block. Roads, zoning and buildings go in
// as ordinary vanilla definitions through SuiteFixtureToolSystem — nothing of Placemaker's own build code is used, so the
// fixture does not depend on the code under test.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Placemaker.Core.Suite;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    internal sealed partial class SuiteFixture
    {
        public readonly SuiteLayout Layout = new SuiteLayout();
        public List<SuiteBlock> Blocks;
        public readonly Dictionary<string, Entity> Prefabs = new Dictionary<string, Entity>(StringComparer.Ordinal);
        public readonly Dictionary<string, float> HalfWidths = new Dictionary<string, float>(StringComparer.Ordinal);
        public readonly List<Entity> PlantedBuildings = new List<Entity>();
        public readonly List<string> Log = new List<string>();

        private readonly World _world;
        private readonly EntityManager _em;
        private PrefabSystem _prefabSystem;

        public SuiteFixture(World world, int nx = 0, int nz = 0)
        {
            _world = world;
            _em = world.EntityManager;
            if (nx >= 5) Layout.NX = nx;
            if (nz >= 5) Layout.NZ = nz;
            Blocks = Layout.Blocks();
        }

        private void Note(string s)
        {
            Log.Add(s);
            Mod.Log.Info("Placemaker suite fixture: " + s);
        }

        public float HalfWidth(string road) => HalfWidths.TryGetValue(road ?? "", out var w) ? w : 8f;

        /// <summary>Test land made to order for one scenario: laid out east of the grid, its block handed to that scenario only.</summary>
        public SuiteBlock AddPlot(SuitePlot plot)
        {
            Layout.AddPlot(plot);
            var b = plot.Block();
            Blocks.Add(b);
            _used.Add(b.Id);   // never handed out by Take()
            Note($"plot {plot.Name}: {plot.Shape} {plot.Size.x:F0} x {plot.Size.y:F0} m, ground {plot.Ground}, roads {(plot.RoadsAround ? plot.Road + " around" : "none")}, centre ({plot.Center.x:F0}, {plot.Center.y:F0})");
            return b;
        }

        public SuiteBlock PlotBlock(SuitePlot plot) => Blocks.FirstOrDefault(b => b.Kind == SuiteBlockKind.Plot && b.Name == plot.Name && Layout.Plots.Contains(plot));

        // ───────────── prefabs ─────────────

        public void IndexPrefabs()
        {
            _prefabSystem = _world.GetOrCreateSystemManaged<PrefabSystem>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    string name;
                    try { name = _prefabSystem.GetPrefab<PrefabBase>(e)?.name; } catch (Exception) { continue; }
                    if (string.IsNullOrEmpty(name) || Prefabs.ContainsKey(name)) continue;
                    Prefabs[name] = e;
                }
            }
            foreach (var road in new[] { SuiteLayout.SmallRoad, SuiteLayout.MediumRoad, SuiteLayout.PedestrianStreet })
            {
                if (Prefabs.TryGetValue(road, out var p) && _em.HasComponent<NetGeometryData>(p))
                {
                    var g = _em.GetComponentData<NetGeometryData>(p);
                    HalfWidths[road] = g.m_DefaultWidth * 0.5f;
                    Note($"road '{road}' width {g.m_DefaultWidth:F1} m, edge length {g.m_EdgeLengthRange.min:F0}-{g.m_EdgeLengthRange.max:F0} m");
                }
                else Note($"road '{road}' NOT FOUND");
            }
            Note($"indexed {Prefabs.Count} prefabs");
        }

        public string NameOf(Entity prefab)
        {
            try { return _prefabSystem.GetPrefab<PrefabBase>(prefab)?.name ?? ""; } catch (Exception) { return ""; }
        }

        private Entity Road(string name)
        {
            if (Prefabs.TryGetValue(name, out var e) && _em.HasComponent<NetGeometryData>(e)) return e;
            return Prefabs.TryGetValue(SuiteLayout.SmallRoad, out var s) ? s : Entity.Null;
        }

        // ───────────── terrain ─────────────

        /// <summary>
        /// Takes the live map's terrain encoding and playable area, and sets the fixture's Base to the median ground of the
        /// fixture area (least earth moved), at least 8 m above the sea.
        /// </summary>
        public void ConfigureFromWorld()
        {
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            Layout.HeightScaleM = terrain.heightScaleOffset.x;
            Layout.HeightOffset = terrain.heightScaleOffset.y;
            Layout.MapOffset = terrain.playableOffset;
            Layout.MapExtent = terrain.playableArea;
            var heights = new List<float>();
            var lo = Layout.FixtureMin;
            var hi = Layout.FixtureMax;
            for (float x = lo.x; x <= hi.x; x += 20f)
                for (float z = lo.y; z <= hi.y; z += 20f)
                    heights.Add(SampleHeight(new float2(x, z)));
            heights.Sort();
            float median = SuiteLayout.Median(heights);
            float sea = _world.GetOrCreateSystemManaged<WaterSystem>().SeaLevel;
            Layout.Base = math.max(median, sea + 8f);
            Note($"map terrain: scale {Layout.HeightScaleM:F0} m, offset {Layout.HeightOffset:F1} m, playable {Layout.MapExtent.x:F0} m at {Layout.MapOffset}; "
                + $"fixture ground {heights[0]:F1}..{heights[heights.Count - 1]:F1} m, median {median:F1} m, sea {sea:F1} m -> base {Layout.Base:F1} m");
        }

        /// <summary>Removes every water source (the flattened fixture must never flood); the water itself is reset after the heightmap.</summary>
        public int RemoveWaterSources()
        {
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Simulation.WaterSourceData>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            {
                int n = q.CalculateEntityCount();
                if (n > 0) _em.AddComponent<Deleted>(q);
                Note($"water sources removed: {n}");
                return n;
            }
        }

        public void ResetWater()
        {
            var water = _world.GetOrCreateSystemManaged<WaterSystem>();
            water.Reset();
            Note("water reset (no water left on the map)");
        }

        /// <summary>
        /// Bulldozes what the map has in the fixture area (top-level nets and static objects: trees, props, buildings) through
        /// vanilla delete definitions, the way the bulldozer does.
        /// </summary>
        public int EmitClear(SuiteFixtureToolSystem tool, out string detail, float2? min = null, float2? max = null)
        {
            var lo = (min ?? Layout.FixtureMin) - 20f;   // a plot laid out later clears only its own ground
            var hi = (max ?? Layout.FixtureMax) + 20f;
            bool Inside(float3 p) => p.x >= lo.x && p.x <= hi.x && p.z >= lo.y && p.z <= hi.y;
            int edges = 0, objects = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Owner>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var c = _em.GetComponentData<Curve>(e).m_Bezier;
                    bool hit = false;
                    for (int k = 0; k <= 8 && !hit; k++) hit = Inside(MathUtils.Position(c, k / 8f));
                    if (!hit) continue;
                    var edge = _em.GetComponentData<Edge>(e);
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Original = e, m_Flags = CreationFlags.Delete });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new NetCourse
                    {
                        m_Curve = c, m_Length = MathUtils.Length(c), m_FixedIndex = -1,
                        m_StartPosition = new CoursePos { m_Entity = edge.m_Start, m_Position = c.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(c)), m_CourseDelta = 0f, m_ParentMesh = -1 },
                        m_EndPosition = new CoursePos { m_Entity = edge.m_End, m_Position = c.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(c)), m_CourseDelta = 1f, m_ParentMesh = -1 },
                    });
                    edges++;
                }
            }
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Objects.Static>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Owner>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var t = _em.GetComponentData<global::Game.Objects.Transform>(e);
                    if (!Inside(t.m_Position)) continue;
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Original = e, m_Flags = CreationFlags.Delete });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new ObjectDefinition { m_Position = t.m_Position, m_Rotation = t.m_Rotation, m_LocalPosition = t.m_Position, m_LocalRotation = t.m_Rotation, m_ParentMesh = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_PrefabSubIndex = -1 });
                    objects++;
                }
            }
            detail = $"{edges} edges, {objects} static objects";
            Note("clear fixture area: " + detail);
            return edges + objects;
        }

        public int CountInFixture()
        {
            var lo = Layout.FixtureMin;
            var hi = Layout.FixtureMax;
            int n = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Objects.Static>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Owner>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    if (p.x >= lo.x && p.x <= hi.x && p.z >= lo.y && p.z <= hi.y) n++;
                }
            return n;
        }

        /// <summary>
        /// The base heightmap as the terrain system holds it (GPU readback of TerrainSystem.heightmap: without the road,
        /// building and lot modifications the CPU heights include), else the CPU heights, else null.
        /// </summary>
        public ushort[] ReadHeightmap(out string source)
        {
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            int n = SuiteLayout.HeightmapSize;
            source = null;
            try
            {
                if (terrain.heightmap is RenderTexture rt && rt.width == n && rt.height == n)
                {
                    var req = UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0);
                    req.WaitForCompletion();
                    if (!req.hasError)
                    {
                        var px = req.GetData<ushort>();
                        if (px.Length == n * n) { source = "gpu"; return px.ToArray(); }
                    }
                }
            }
            catch (Exception e) { Note("heightmap readback failed: " + e.Message); }
            var hd = terrain.GetHeightData(true);
            if (hd.isCreated && hd.heights.Length == n * n) { source = "cpu"; return hd.heights.ToArray(); }
            return null;
        }

        private void Replace(ushort[] data)
        {
            int n = SuiteLayout.HeightmapSize;
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            var tex = new Texture2D(n, n, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            try
            {
                tex.SetPixelData(data, 0);
                tex.Apply(false, false);
                terrain.ReplaceHeightmap(tex);
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }

        public string ApplyHeightmap()
        {
            var existing = ReadHeightmap(out var source);
            _mapHeights = existing;   // the map's own ground, for land laid out later (SuiteFixture.Grow.cs)
            Note(existing != null ? "map heightmap read from " + source : "map heightmap unavailable: the whole map becomes the layout");
            Replace(Layout.Heightmap(existing));
            return null;
        }

        /// <summary>Puts one block's ground back to the layout (after a scenario deleted its build). Returns null or the problem.</summary>
        public string RestoreBlock(SuiteBlock b)
        {
            var current = ReadHeightmap(out _);
            if (current == null) return "heightmap unavailable";
            int changed = Layout.PatchBlock(current, b);
            if (changed > 0) Replace(current);
            Note($"restore {b.Id}: {changed} texels back to the layout");
            return null;
        }

        /// <summary>Live ground height (equals the layout once the heightmap is in; the default world otherwise).</summary>
        public float H(float x, float z) => SampleHeight(new float2(x, z));

        public float SampleHeight(float2 p)
        {
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            var h = terrain.GetHeightData(true);
            return TerrainUtils.SampleHeight(ref h, new float3(p.x, 0f, p.y));
        }

        /// <summary>Compares sampled terrain with the layout at a few probe points. Returns the worst error (m).</summary>
        public float CheckTerrain(out string detail)
        {
            var probes = new[]
            {
                new float2(Layout.LineX(5) + 40f, Layout.LineZ(2) + 50f),                    // flat
                new float2(Layout.LineX(3) + 75f, Layout.LineZ(Layout.NZ - 1) + 70f),        // slope
                new float2(Layout.LineX(3) + 75f, Layout.LineZ(Layout.NZ)),                  // slope top road
                new float2(Layout.LineX(1) + 75f, Layout.LineZ(Layout.RoughRow) + 70f),      // rough
                new float2(Layout.LineX(0) + 60f, Layout.LineZ(Layout.RoughRow) + 52f),      // rough
            };
            float worst = 0f;
            var sb = new System.Text.StringBuilder();
            foreach (var p in probes)
            {
                float want = Layout.Height(p.x, p.y), got = SampleHeight(p);
                worst = math.max(worst, math.abs(want - got));
                sb.Append($"({p.x:F0},{p.y:F0}) want {want:F2} got {got:F2}; ");
            }
            detail = sb.ToString();
            return worst;
        }

        // ───────────── roads ─────────────

        /// <summary>
        /// Emits the road segments the filter picks. Ends that land on an existing node attach to it (CoursePos.m_Entity, as the
        /// vanilla net tool snaps), so the grid goes in as batches: vertical lines first, then the cross streets onto their nodes.
        /// </summary>
        public int EmitRoads(SuiteFixtureToolSystem tool, Func<SuiteRoadSegment, bool> filter)
        {
            int courses = 0;
            var random = new Unity.Mathematics.Random(0x5EED5u);
            var nodes = ExistingNodes();
            foreach (var seg in Layout.Roads())
            {
                if (filter != null && !filter(seg)) continue;
                var prefab = Road(seg.Prefab);
                if (prefab == Entity.Null) continue;
                var g = _em.GetComponentData<NetGeometryData>(prefab);
                float len = math.distance(seg.A, seg.B);
                float max = g.m_EdgeLengthRange.max > 1f ? g.m_EdgeLengthRange.max : 200f;
                int pieces = math.max(1, (int)math.ceil(len / (max * 0.95f)));
                for (int k = 0; k < pieces; k++)
                {
                    float2 a2 = math.lerp(seg.A, seg.B, k / (float)pieces), b2 = math.lerp(seg.A, seg.B, (k + 1) / (float)pieces);
                    if (k == 0) a2 = seg.A;
                    if (k == pieces - 1) b2 = seg.B;
                    var a = new float3(a2.x, H(a2.x, a2.y), a2.y);
                    var b = new float3(b2.x, H(b2.x, b2.y), b2.y);
                    var curve = NetUtils.StraightCurve(a, b);
                    var start = new CoursePos
                    {
                        m_Entity = Entity.Null, m_Position = a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                        m_CourseDelta = 0f, m_Elevation = float2.zero, m_Flags = CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                        m_ParentMesh = -1, m_SplitPosition = 0f,
                    };
                    var end = new CoursePos
                    {
                        m_Entity = Entity.Null, m_Position = b, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                        m_CourseDelta = 1f, m_Elevation = float2.zero, m_Flags = CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                        m_ParentMesh = -1, m_SplitPosition = 0f,
                    };
                    Attach(ref start, nodes);
                    Attach(ref end, nodes);
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = random.NextInt() });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new NetCourse
                    {
                        m_Curve = curve, m_Length = MathUtils.Length(curve), m_FixedIndex = -1, m_Elevation = float2.zero,
                        m_StartPosition = start, m_EndPosition = end,
                    });
                    courses++;
                }
            }
            return courses;
        }

        /// <summary>
        /// (area tools) Emits a plot's extra roads and paths (SuitePlot.Nets) after its ring road: an end on an existing node
        /// joins it, an end within a metre of an existing edge splits that edge there (CoursePos.m_Entity = the edge,
        /// m_SplitPosition = its curve position, as the vanilla road tool snaps onto a road), consecutive points of one net
        /// join each other.
        /// </summary>
        public int EmitNets(SuiteFixtureToolSystem tool, IEnumerable<SuiteRoadSegment> segments)
        {
            int courses = 0;
            var random = new Unity.Mathematics.Random(0xA4EA5u);
            var nodes = ExistingNodes();
            var edges = ExistingEdges();
            var made = new Dictionary<long, bool>();
            foreach (var seg in segments)
            {
                var prefab = Road(seg.Prefab);
                if (prefab == Entity.Null) continue;
                var a = new float3(seg.A.x, H(seg.A.x, seg.A.y), seg.A.y);
                var b = new float3(seg.B.x, H(seg.B.x, seg.B.y), seg.B.y);
                var curve = NetUtils.StraightCurve(a, b);
                var start = new CoursePos
                {
                    m_Entity = Entity.Null, m_Position = a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve)),
                    m_CourseDelta = 0f, m_Elevation = float2.zero, m_Flags = CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                    m_ParentMesh = -1, m_SplitPosition = 0f,
                };
                var end = new CoursePos
                {
                    m_Entity = Entity.Null, m_Position = b, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve)),
                    m_CourseDelta = 1f, m_Elevation = float2.zero, m_Flags = CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
                    m_ParentMesh = -1, m_SplitPosition = 0f,
                };
                // Points this batch already starts or ends a course at are left to the game to join (same position).
                if (!made.ContainsKey(Key(seg.A))) AttachOrSplit(ref start, nodes, edges);
                if (!made.ContainsKey(Key(seg.B))) AttachOrSplit(ref end, nodes, edges);
                made[Key(seg.A)] = true;
                made[Key(seg.B)] = true;
                var def = tool.NewDefinition();
                _em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = random.NextInt() });
                _em.AddComponent<Updated>(def);
                _em.AddComponentData(def, new NetCourse
                {
                    m_Curve = curve, m_Length = MathUtils.Length(curve), m_FixedIndex = -1, m_Elevation = float2.zero,
                    m_StartPosition = start, m_EndPosition = end,
                });
                courses++;
            }
            return courses;
        }

        private List<Entity> ExistingEdges()
        {
            var r = new List<Entity>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<global::Game.Net.Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all) r.Add(e);
            return r;
        }

        private void AttachOrSplit(ref CoursePos pos, Dictionary<long, Entity> nodes, List<Entity> edges)
        {
            Attach(ref pos, nodes);
            if (pos.m_Entity != Entity.Null) return;
            float best = 1f;
            foreach (var e in edges)
            {
                var c = _em.GetComponentData<global::Game.Net.Curve>(e).m_Bezier;
                float d = MathUtils.Distance(c.xz, pos.m_Position.xz, out float t);
                if (d >= best) continue;
                best = d;
                pos.m_Entity = e;
                pos.m_SplitPosition = t;
                pos.m_Position = MathUtils.Position(c, t);
            }
        }

        private static long Key(float2 p) => ((long)math.round(p.x * 4f) << 32) ^ (uint)(int)math.round(p.y * 4f);

        private Dictionary<long, Entity> ExistingNodes()
        {
            var r = new Dictionary<long, Entity>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Net.Node>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all) r[Key(_em.GetComponentData<global::Game.Net.Node>(e).m_Position.xz)] = e;
            return r;
        }

        private void Attach(ref CoursePos pos, Dictionary<long, Entity> nodes)
        {
            if (!nodes.TryGetValue(Key(pos.m_Position.xz), out var node)) return;
            pos.m_Entity = node;
            pos.m_Position = _em.GetComponentData<global::Game.Net.Node>(node).m_Position;
        }

        public int CountEdges(out int temps)
        {
            temps = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var t = _em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>()))
            {
                temps = t.CalculateEntityCount();
                return q.CalculateEntityCount();
            }
        }

        // ───────────── zoning ─────────────

        public const string ZonePrefab = "EU Residential Low";
        public const float ZoneDepth = 17f;

        /// <summary>Residential zoning along the south street of every Zoned block (2 cells deep).</summary>
        public int EmitZoning(SuiteFixtureToolSystem tool)
        {
            if (!Prefabs.TryGetValue(ZonePrefab, out var zone) && !Prefabs.TryGetValue("NA Residential Low", out zone)) { Note("zone prefab not found"); return 0; }
            int n = 0;
            foreach (var b in Blocks)
            {
                if (b.Kind != SuiteBlockKind.Zoned) continue;
                float z0 = b.Z0 + HalfWidth(b.South) + 0.5f, z1 = z0 + ZoneDepth;
                float x0 = b.X0 + HalfWidth(b.West) + 0.5f, x1 = b.X1 - HalfWidth(b.East) - 0.5f;
                float3 P(float x, float z) => new float3(x, H(x, z), z);
                var def = tool.NewDefinition();
                _em.AddComponentData(def, new CreationDefinition { m_Prefab = zone });
                _em.AddComponentData(def, new Zoning { m_Position = new Quad3(P(x0, z0), P(x1, z0), P(x1, z1), P(x0, z1)), m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee | ZoningFlags.Overwrite });
                _em.AddComponent<Updated>(def);
                n++;
            }
            return n;
        }

        public int CountZonedCells()
        {
            int zoned = 0;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var cells = _em.GetBuffer<Cell>(e, true);
                    for (int i = 0; i < cells.Length; i++)
                        if (cells[i].m_Zone.m_Index != 0 && (cells[i].m_State & CellFlags.Visible) != 0) zoned++;
                }
            }
            return zoned;
        }

        // ───────────── plopped buildings ─────────────

        /// <summary>Two small park buildings along the south street of the Survive block, fronts on the street.</summary>
        public int EmitBuildings(SuiteFixtureToolSystem tool)
        {
            var parks = new List<(string Name, Entity Prefab, int2 Lot)>();
            foreach (var kv in Prefabs)
            {
                var e = kv.Value;
                if (!_em.HasComponent<ParkData>(e) || !_em.HasComponent<BuildingData>(e)) continue;
                var lot = _em.GetComponentData<BuildingData>(e).m_LotSize;
                if (lot.x < 2 || lot.y < 2 || lot.x > 5 || lot.y > 5) continue;
                parks.Add((kv.Key, e, lot));
            }
            parks.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            if (parks.Count == 0) { Note("no small park building prefab found"); return 0; }
            int n = 0;
            foreach (var b in Blocks)
            {
                if (b.Kind != SuiteBlockKind.Survive) continue;
                float roadEdge = b.Z0 + HalfWidth(b.South);
                float[] xs = { b.X0 + HalfWidth(b.West) + 30f, b.X1 - HalfWidth(b.East) - 30f };
                for (int k = 0; k < 2; k++)
                {
                    var park = parks[(k * 7) % parks.Count];
                    float z = roadEdge + park.Lot.y * 4f + 0.5f;
                    var pos = new float3(xs[k], H(xs[k], z), z);
                    var rot = quaternion.RotateY(math.PI);   // front (+Z local) faces the south street
                    var def = tool.NewDefinition();
                    _em.AddComponentData(def, new CreationDefinition { m_Prefab = park.Prefab, m_RandomSeed = 1234 + k });
                    _em.AddComponent<Updated>(def);
                    _em.AddComponentData(def, new ObjectDefinition
                    {
                        m_Position = pos, m_LocalPosition = pos, m_Rotation = rot, m_LocalRotation = rot, m_Scale = new float3(1f),
                        m_Intensity = 1f, m_Age = 0.5f, m_Probability = 100, m_PrefabSubIndex = -1, m_ParentMesh = -1, m_Elevation = 0f,
                    });
                    Note($"plop '{park.Name}' lot {park.Lot.x}x{park.Lot.y} at ({pos.x:F0},{pos.z:F0}) in {b.Id}");
                    n++;
                }
            }
            return n;
        }

        /// <summary>Buildings standing in a rectangle (not temp, not deleted).</summary>
        public List<Entity> BuildingsIn(float2 min, float2 max)
        {
            var r = new List<Entity>();
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in all)
                {
                    var p = _em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    if (p.x >= min.x && p.x <= max.x && p.z >= min.y && p.z <= max.y) r.Add(e);
                }
            }
            return r;
        }

        public int CountAllBuildings()
        {
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                return q.CalculateEntityCount();
        }

        // ───────────── block assignment ─────────────

        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _free = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _dirty = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _uses = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _lastUser = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Spacing = "checker";

        public static SuiteBlockKind? KindOf(string site)
        {
            switch ((site ?? "any").ToLowerInvariant())
            {
                case "flat": return SuiteBlockKind.Flat;
                case "zoned": return SuiteBlockKind.Zoned;
                case "slope": return SuiteBlockKind.Slope;
                case "rough": return SuiteBlockKind.Rough;
                case "survive": return SuiteBlockKind.Survive;
            }
            return null;
        }

        /// <summary>Checker spacing: flat blocks only on one colour of the board, so no scenario's neighbours hide or shade it.</summary>
        private bool Preferred(SuiteBlock b) => !string.Equals(Spacing, "checker", StringComparison.OrdinalIgnoreCase) || ((b.I + b.J) & 1) == 0;

        private bool Allowed(SuiteBlock b) => b.Kind != SuiteBlockKind.Flat || Preferred(b);

        private bool Open(SuiteBlock b) => !_dirty.ContainsKey(b.Id) && (!_used.Contains(b.Id) || _free.Contains(b.Id));

        private static bool StreetsMatch(SuiteBlock b, Dictionary<string, string> want)
        {
            if (want == null) return true;
            foreach (var kv in want)
            {
                string have = kv.Key.ToLowerInvariant() switch { "west" => b.West, "east" => b.East, "south" => b.South, "north" => b.North, _ => null };
                if (have == null || !string.Equals(have, kv.Value, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// A block for a scenario: the given block id, else the next open block of the wanted kind (and streets) — never a
        /// block of another kind unless the scenario allows a fallback (recorded in note).
        /// </summary>
        public SuiteBlock Take(string site, string blockId, Dictionary<string, string> streets, bool fallback, string user, out string note)
        {
            note = null;
            SuiteBlock pick = null;
            var want = KindOf(site);
            if (!string.IsNullOrEmpty(blockId))
            {
                pick = Blocks.FirstOrDefault(b => string.Equals(b.Id, blockId, StringComparison.OrdinalIgnoreCase));
                if (pick == null) { note = $"no block {blockId} in this grid"; return null; }
                if (_dirty.TryGetValue(pick.Id, out var why)) { note = $"block {blockId} is quarantined: {why}"; return null; }
                if (!Open(pick)) { note = $"block {blockId} is in use by {(_lastUser.TryGetValue(pick.Id, out var u) ? u : "?")}"; return null; }
            }
            else
            {
                IEnumerable<SuiteBlock> cands = Blocks.Where(b => Open(b) && StreetsMatch(b, streets) && Allowed(b));
                if (want.HasValue) cands = cands.Where(b => b.Kind == want.Value);
                else cands = cands.Where(b => b.Kind == SuiteBlockKind.Flat);
                pick = cands.OrderBy(b => _used.Contains(b.Id) ? 1 : 0).ThenBy(b => Preferred(b) ? 0 : 1).ThenBy(b => b.J).ThenBy(b => b.I).FirstOrDefault();
                if (pick == null && (fallback || !want.HasValue))
                {
                    pick = Blocks.Where(b => Open(b) && StreetsMatch(b, streets) && Allowed(b) && b.Kind != SuiteBlockKind.Survive && b.Kind != SuiteBlockKind.Zoned)
                        .OrderBy(b => _used.Contains(b.Id) ? 1 : 0).ThenBy(b => b.Kind == SuiteBlockKind.Flat ? 0 : 1).ThenBy(b => b.J).ThenBy(b => b.I).FirstOrDefault();
                    if (pick != null) note = $"wanted {site}, none left: fell back to {pick.Kind} (siteFallback)";
                }
                if (pick == null)
                {
                    int total = Blocks.Count(b => (!want.HasValue || b.Kind == want.Value) && Allowed(b));
                    note = $"no block: wanted {site}{(streets != null ? " with streets " + string.Join(",", streets.Select(kv => kv.Key + "=" + kv.Value)) : "")}, none left ({total} in this grid, {_dirty.Count} quarantined)";
                    return null;
                }
            }
            _used.Add(pick.Id);
            _free.Remove(pick.Id);
            _uses[pick.Id] = TimesUsed(pick) + 1;
            if (_lastUser.TryGetValue(pick.Id, out var prev)) note = (note == null ? "" : note + "; ") + "reused after " + prev;
            _lastUser[pick.Id] = user ?? "";
            return pick;
        }

        /// <summary>How many blocks of each kind the grid offers to scenarios (with the spacing).</summary>
        public int Capacity(SuiteBlockKind kind) => Blocks.Count(b => b.Kind == kind && Allowed(b));

        /// <summary>The block's build was deleted, nothing was left behind and the ground is back: later scenarios may use it again.</summary>
        public void Release(SuiteBlock b)
        {
            if (b.Kind == SuiteBlockKind.Zoned || b.Kind == SuiteBlockKind.Survive || _dirty.ContainsKey(b.Id)) return;
            _free.Add(b.Id);
        }

        /// <summary>The block is not clean (leftovers, ground not restored, delete timed out): nobody gets it again.</summary>
        public void Quarantine(SuiteBlock b, string why)
        {
            _dirty[b.Id] = why;
            _free.Remove(b.Id);
        }

        public IReadOnlyDictionary<string, string> Quarantined => _dirty;

        public int TimesUsed(SuiteBlock b) => _uses.TryGetValue(b.Id, out int n) ? n : 0;

        /// <summary>Largest difference between the live ground and the layout over the block (sampled every 6 m inside the kerbs).</summary>
        public float BlockGroundError(SuiteBlock b, Func<string, float> halfWidth, out float2 worstAt)
        {
            float worst = 0f;
            worstAt = b.Center;
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            var h = terrain.GetHeightData(true);
            // inside the kerbs (+4 m): roads level the ground under themselves, that is not the block's ground
            float x0 = b.X0 + halfWidth(b.West) + 4f, x1 = b.X1 - halfWidth(b.East) - 4f, z0 = b.Z0 + halfWidth(b.South) + 4f, z1 = b.Z1 - halfWidth(b.North) - 4f;
            for (float x = x0; x <= x1; x += 6f)
                for (float z = z0; z <= z1; z += 6f)
                {
                    float d = math.abs(TerrainUtils.SampleHeight(ref h, new float3(x, 0f, z)) - Layout.Height(x, z));
                    if (d > worst) { worst = d; worstAt = new float2(x, z); }
                }
            return worst;
        }

    }
}
