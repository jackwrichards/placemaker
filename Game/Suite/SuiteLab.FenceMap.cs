// Game runner lab: Outgrow's fences as the player sees them, measured and written down (the user: "fence continuity
// errors ... crossover errors"). Every fence and hedge in the land (the houses' own fence lanes and the pieces Outgrow
// closes gardens with), drawn or hidden by the game, the gardens, roads and buildings, and what is wrong:
//   * gaps: two loose ends of drawn fences close to each other (a missing piece, a corner that does not meet, a step
//     between two runs), and what lies in the gap (a hidden piece, a building, a road);
//   * crossings: two drawn fences crossing each other (not meeting end to side);
//   * doubles: two drawn fences lying on one another;
//   * fences standing inside a garden (another house's, or their own house's away from its edge);
//   * gardens over each other.
//   {"do":"lab","op":"fenceMap","label":"settled","closeups":4}
// The record has the counts and the worst spots; <run>/fencemap-<NN>-<label>.json has everything (tools/suite/
// FenceMap.js draws it), close-ups are named fence-<kind>-<n>.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Game.Neighbours;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private sealed class FenceBit
        {
            public int Index;
            public Entity E;
            public string Name;
            public bool Lane, Hidden, Ours;
            public Entity Owner;
            public float2[] Pts;
            public float2 Lo, Hi;
            public float Width;
        }

        // each map's close-up spots by its label (a later map takes the same ones: "spotsFrom")
        private readonly Dictionary<string, List<(string Kind, float2 At, string Why)>> _fenceSpots = new Dictionary<string, List<(string, float2, string)>>();

        private IEnumerator LabFenceMap(ScenarioState st, JObject step, JObject rec)
        {
            yield return Frames(30);
            var em = _world.EntityManager;
            var sys = _world.GetExistingSystemManaged<NeighbourPolishSystem>();
            var prefabs = _world.GetOrCreateSystemManaged<PrefabSystem>();
            var names = new Dictionary<Entity, string>();
            string NameOf(Entity p)
            {
                if (names.TryGetValue(p, out var n)) return n;
                try { n = prefabs.GetPrefab<PrefabBase>(p)?.name ?? ""; } catch (Exception) { n = ""; }
                return names[p] = n;
            }
            var b = st.S.Block;
            var lo = new float2(b.X0, b.Z0) - 40f;
            var hi = new float2(b.X1, b.Z1) + 40f;
            // a loaded city: around a street by its name ("street", "margin") or a box of the map ("box": [x0, z0, x1, z1])
            if (step["box"] is JArray box && box.Count == 4)
            {
                lo = new float2((float)box[0], (float)box[1]);
                hi = new float2((float)box[2], (float)box[3]);
            }
            else if ((string)step["box"] == "gardens" && sys != null)
            {
                // every house Outgrow gave a garden (a loaded city)
                var glo = new float2(float.MaxValue);
                var ghi = new float2(float.MinValue);
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PolishGarden>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                    {
                        if (!sys.IsYes(e)) continue;
                        var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                        glo = math.min(glo, p);
                        ghi = math.max(ghi, p);
                    }
                if (glo.x > ghi.x) { rec["error"] = "lab: no Outgrow gardens in the city"; yield break; }
                lo = glo - 60f;
                hi = ghi + 60f;
            }
            else if (!string.IsNullOrEmpty((string)step["street"]))
            {
                string street = (string)step["street"];
                var nameSystem = _world.GetOrCreateSystemManaged<global::Game.UI.NameSystem>();
                var slo = new float2(float.MaxValue);
                var shi = new float2(float.MinValue);
                var named = new Dictionary<Entity, string>();
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Aggregated>(), ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                    {
                        var agg = em.GetComponentData<Aggregated>(e).m_Aggregate;
                        if (agg == Entity.Null) continue;
                        if (!named.TryGetValue(agg, out var nm)) { try { nm = nameSystem.GetRenderedLabelName(agg); } catch (Exception) { nm = ""; } named[agg] = nm; }
                        if (!string.Equals(nm, street, StringComparison.OrdinalIgnoreCase)) continue;
                        var cb = MathUtils.Bounds(em.GetComponentData<Curve>(e).m_Bezier);
                        slo = math.min(slo, cb.min.xz);
                        shi = math.max(shi, cb.max.xz);
                    }
                if (slo.x > shi.x)
                {
                    var seen = named.Values.Where(v => !string.IsNullOrEmpty(v)).Distinct().OrderBy(v => v).ToList();
                    rec["streets"] = new JArray(seen.Take(400));
                    Mod.Log.Info($"Placemaker lab: fence map: no street named '{street}'; {seen.Count} names: {string.Join(" | ", seen.Take(400))}");
                    rec["error"] = $"lab: no street named '{street}'";
                    yield break;
                }
                float margin = (float?)step["margin"] ?? 90f;
                lo = slo - margin;
                hi = shi + margin;
                rec["street"] = street;
            }
            rec["box"] = new JArray(Math.Round(lo.x, 1), Math.Round(lo.y, 1), Math.Round(hi.x, 1), Math.Round(hi.y, 1));
            bool InBox(float2 a, float2 c) => c.x >= lo.x && c.y >= lo.y && a.x <= hi.x && a.y <= hi.y;
            string label = (string)step["label"] ?? "map";

            // roads, paths and tracks on the ground
            var roads = new List<(float2[] Ring, float2 Lo, float2 Hi)>();
            var pieces = new List<(float2[] Ring, bool Round)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var cb = MathUtils.Bounds(em.GetComponentData<Curve>(e).m_Bezier);
                    if (!InBox(cb.min.xz - 30f, cb.max.xz + 30f)) continue;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<Road>(e) && !em.HasComponent<PathwayData>(prefab) && !em.HasComponent<TrainTrack>(e) && !em.HasComponent<TramTrack>(e)) continue;
                    if (em.HasComponent<Owner>(e) || !em.HasComponent<Composition>(e)) continue;
                    pieces.Clear();
                    RoadSurfaces.Pieces(em, e, pieces);
                    foreach (var (ring, _) in pieces) { Poly.Bounds(ring, out var a, out var c); roads.Add((ring, a, c)); }
                }

            // buildings: lots and the houses' own outlines (their meshes, not their lots), and the sheds, pools and decks on them
            var buildings = new List<(Entity E, float2[] Lot, float2[] Body)>();
            var props = new List<float2[]>();
            float2[] Quad(global::Game.Objects.Transform t, Bounds3 gb)
            {
                var f = global::Game.Objects.ObjectUtils.CalculateBaseCorners(t.m_Position, t.m_Rotation, gb);
                return new[] { f.a.xz, f.b.xz, f.c.xz, f.d.xz };
            }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var t = em.GetComponentData<global::Game.Objects.Transform>(e);
                    if (!InBox(t.m_Position.xz, t.m_Position.xz)) continue;
                    BuildingFootprints.Of(em, e, out var lot, out _);
                    float2[] body = null;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    Bounds3? mesh = null;
                    try
                    {
                        if (prefabs.TryGetPrefab<ObjectGeometryPrefab>(prefab, out var gp) && gp?.m_Meshes != null)
                            foreach (var mi in gp.m_Meshes)
                            {
                                if (!(mi?.m_Mesh is RenderPrefab rp)) continue;
                                var mb = new Bounds3(rp.bounds.min + mi.m_Position, rp.bounds.max + mi.m_Position);
                                mesh = mesh.HasValue ? new Bounds3(math.min(mesh.Value.min, mb.min), math.max(mesh.Value.max, mb.max)) : mb;
                            }
                    }
                    catch (Exception) { mesh = null; }
                    if (mesh.HasValue) body = Quad(t, mesh.Value);
                    else if (em.HasComponent<ObjectGeometryData>(prefab)) body = Quad(t, em.GetComponentData<ObjectGeometryData>(prefab).m_Bounds);
                    buildings.Add((e, lot, body));
                    if (!em.HasBuffer<global::Game.Objects.SubObject>(e)) continue;
                    foreach (var so in em.GetBuffer<global::Game.Objects.SubObject>(e, true))
                    {
                        var o = so.m_SubObject;
                        if (!em.Exists(o) || em.HasComponent<global::Game.Objects.Tree>(o) || em.HasComponent<global::Game.Objects.Plant>(o) || em.HasComponent<PolishPiece>(o)) continue;
                        if (!em.HasComponent<global::Game.Objects.Transform>(o) || !em.HasComponent<PrefabRef>(o)) continue;
                        var op = em.GetComponentData<PrefabRef>(o).m_Prefab;
                        if (!em.HasComponent<ObjectGeometryData>(op)) continue;
                        var ob = em.GetComponentData<ObjectGeometryData>(op).m_Bounds;
                        var size = ob.max - ob.min;
                        if (size.x * size.z < 1.5f || size.y < 0.25f || NameOf(op).IndexOf("Piece", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        props.Add(Quad(em.GetComponentData<global::Game.Objects.Transform>(o), ob));
                    }
                }

            // fences and hedges: the houses' lanes and every fence or hedge piece, drawn or hidden
            var bits = new List<FenceBit>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<global::Game.Net.UtilityLane>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<UtilityLaneData>(prefab) || (em.GetComponentData<UtilityLaneData>(prefab).m_UtilityTypes & UtilityTypes.Fence) == 0) continue;
                    var curve = em.GetComponentData<Curve>(e);
                    if (curve.m_Length < 0.3f) continue;   // a collapsed (hidden on purpose) line
                    var cb = MathUtils.Bounds(curve.m_Bezier);
                    if (!InBox(cb.min.xz, cb.max.xz)) continue;
                    bool straight = curve.m_Length - math.distance(curve.m_Bezier.a.xz, curve.m_Bezier.d.xz) < 0.05f;
                    int k = straight ? 1 : 8;
                    var pts = new float2[k + 1];
                    for (int i = 0; i <= k; i++) pts[i] = MathUtils.Position(curve.m_Bezier, i / (float)k).xz;
                    var w = em.HasComponent<NetLaneData>(prefab) ? em.GetComponentData<NetLaneData>(prefab).m_Width : 0.3f;
                    bits.Add(new FenceBit
                    {
                        E = e, Name = NameOf(prefab), Lane = true, Hidden = em.HasComponent<Overridden>(e),
                        Owner = em.HasComponent<Owner>(e) ? em.GetComponentData<Owner>(e).m_Owner : Entity.Null, Pts = pts, Width = w,
                    });
                }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var t = em.GetComponentData<global::Game.Objects.Transform>(e);
                    if (!InBox(t.m_Position.xz, t.m_Position.xz)) continue;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var n = NameOf(prefab);
                    if (n.IndexOf("Piece", StringComparison.OrdinalIgnoreCase) < 0 || (n.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) < 0 && n.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    if (!em.HasComponent<ObjectGeometryData>(prefab)) continue;
                    var gb = em.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
                    var size = gb.max - gb.min;
                    var axis = size.z >= size.x ? new float3(0f, 0f, 1f) : new float3(1f, 0f, 0f);
                    var mid = (gb.min + gb.max) * 0.5f;
                    float half = math.max(size.x, size.z) * 0.5f;
                    bits.Add(new FenceBit
                    {
                        E = e, Name = n, Lane = false, Hidden = em.HasComponent<Overridden>(e), Ours = em.HasComponent<PolishPiece>(e),
                        Owner = em.HasComponent<Owner>(e) ? em.GetComponentData<Owner>(e).m_Owner : Entity.Null,
                        Pts = new[] { global::Game.Objects.ObjectUtils.LocalToWorld(t, mid - axis * half).xz, global::Game.Objects.ObjectUtils.LocalToWorld(t, mid + axis * half).xz },
                        Width = math.min(size.x, size.z),
                    });
                }
            for (int i = 0; i < bits.Count; i++)
            {
                var f = bits[i];
                f.Index = i;
                f.Lo = f.Pts.Aggregate(new float2(float.MaxValue), math.min);
                f.Hi = f.Pts.Aggregate(new float2(float.MinValue), math.max);
            }

            // gardens (Outgrow's, as last written: this session's, else the one saved with the house)
            var gardens = new List<(Entity House, float2[] Ring, float2 Lo, float2 Hi)>();
            if (sys != null)
                foreach (var (house, _, _) in buildings)
                {
                    if (!sys.IsYes(house)) continue;
                    float2[] g = sys.Gardens.TryGetValue(house, out var mem) ? mem : null;
                    if (g == null && em.HasBuffer<PolishGarden>(house))
                    {
                        var buf = em.GetBuffer<PolishGarden>(house, true);
                        g = new float2[buf.Length];
                        for (int i = 0; i < g.Length; i++) g[i] = buf[i].Position;
                    }
                    if (g == null || g.Length < 3) continue;
                    var ring = Poly.EnsureCcw(g.ToArray());
                    Poly.Bounds(ring, out var a, out var c);
                    gardens.Add((house, ring, a, c));
                }

            // ── analysis: drawn fences only ──
            var drawn = bits.Where(f => !f.Hidden).ToList();
            const float cell = 6f;
            var grid = new Dictionary<long, List<FenceBit>>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            foreach (var f in drawn)
                for (int x = (int)math.floor(f.Lo.x / cell); x <= (int)math.floor(f.Hi.x / cell); x++)
                    for (int z = (int)math.floor(f.Lo.y / cell); z <= (int)math.floor(f.Hi.y / cell); z++)
                    {
                        if (!grid.TryGetValue(Key(x, z), out var l)) grid[Key(x, z)] = l = new List<FenceBit>();
                        l.Add(f);
                    }
            IEnumerable<FenceBit> Near(float2 p, float r)
            {
                var seen = new HashSet<int>();
                for (int x = (int)math.floor((p.x - r) / cell); x <= (int)math.floor((p.x + r) / cell); x++)
                    for (int z = (int)math.floor((p.y - r) / cell); z <= (int)math.floor((p.y + r) / cell); z++)
                        if (grid.TryGetValue(Key(x, z), out var l))
                            foreach (var f in l) if (seen.Add(f.Index)) yield return f;
            }
            float DistTo(FenceBit f, float2 p)
            {
                float best = float.MaxValue;
                for (int i = 0; i + 1 < f.Pts.Length; i++) best = math.min(best, SegmentDistance(p, f.Pts[i], f.Pts[i + 1]));
                return best;
            }
            bool InAnyRing(IEnumerable<float2[]> rings, float2 p) => rings.Any(r => r != null && Poly.Contains(r, p));
            bool OnRoad(float2 p) => roads.Any(r => p.x >= r.Lo.x && p.x <= r.Hi.x && p.y >= r.Lo.y && p.y <= r.Hi.y && Poly.Contains(r.Ring, p));
            bool InBody(float2 p, float grow) => buildings.Any(bd => bd.Body != null && (Poly.Contains(bd.Body, p) || grow > 0f && DistanceToRing(bd.Body, p) < grow))
                || props.Any(r => Poly.Contains(r, p) || grow > 0f && DistanceToRing(r, p) < grow);
            bool NearRoad(float2 p, float d) => roads.Any(r => p.x >= r.Lo.x - d && p.x <= r.Hi.x + d && p.y >= r.Lo.y - d && p.y <= r.Hi.y + d && (Poly.Contains(r.Ring, p) || DistanceToRing(r.Ring, p) < d));

            // loose ends: an end of a drawn fence that meets no other drawn fence
            const float meet = 0.35f;
            var loose = new List<(FenceBit F, float2 P, float2 Out)>();
            foreach (var f in drawn)
                for (int end = 0; end < 2; end++)
                {
                    var p = end == 0 ? f.Pts[0] : f.Pts[f.Pts.Length - 1];
                    var q = end == 0 ? f.Pts[1] : f.Pts[f.Pts.Length - 2];
                    // a hedge is wide: its end meets what it overlaps
                    float tol = meet + f.Width * 0.5f;
                    bool met = false;
                    foreach (var g in Near(p, tol + 1f))
                    {
                        if (g == f) continue;
                        if (DistTo(g, p) <= tol + g.Width * 0.5f) { met = true; break; }
                    }
                    if (!met) loose.Add((f, p, math.normalizesafe(p - q)));
                }
            // gaps: two loose ends close to each other
            var gaps = new List<JObject>();
            var gapSpots = new List<(float2 At, float Len, string Kind)>();
            int houseGaps = 0;
            var used = new HashSet<int>();
            var looseGrid = loose.Select((l, i) => (l, i)).ToList();
            for (int i = 0; i < loose.Count; i++)
            {
                if (used.Contains(i)) continue;
                var (f, p, o) = loose[i];
                int best = -1;
                float bestD = 8f;
                for (int j = 0; j < loose.Count; j++)
                {
                    if (j == i || used.Contains(j) || loose[j].F == f) continue;
                    float d = math.distance(p, loose[j].P);
                    if (d < bestD) { bestD = d; best = j; }
                }
                if (best < 0) continue;
                used.Add(i);
                used.Add(best);
                var (g, pg, og) = loose[best];
                var mid = (p + pg) * 0.5f;
                // how the two meet: in line (a missing piece), side by side but apart (a step), at an angle (a corner)
                var df = math.normalizesafe(f.Pts[f.Pts.Length - 1] - f.Pts[0]);
                var dg = math.normalizesafe(g.Pts[g.Pts.Length - 1] - g.Pts[0]);
                float par = math.abs(math.dot(df, dg));
                var gapDir = math.normalizesafe(pg - p);
                float off = math.abs(math.dot(pg - p, new float2(-df.y, df.x)));
                string kind = par < 0.9f ? "corner" : off < 0.5f ? "inline" : "step";
                // two fences coming up to the street side by side (the street front between them is open on purpose): no gap
                if (NearRoad(p, 2.5f) && NearRoad(pg, 2.5f) && math.abs(math.dot(df, gapDir)) < 0.5f && math.abs(math.dot(dg, gapDir)) < 0.5f) { used.Remove(i); used.Remove(best); continue; }
                // what is in the gap
                var what = new List<string>();
                var hiddenThere = bits.Where(h => h.Hidden && math.all(mid >= h.Lo - 2f) && math.all(mid <= h.Hi + 2f) && DistTo(h, mid) < 1f).ToList();
                if (hiddenThere.Count > 0) what.Add("hidden " + string.Join(", ", hiddenThere.Select(h => h.Name + (h.Ours ? " (Outgrow)" : "") + (h.Lane ? " lane" : ""))));
                if (InBody(mid, 0.5f)) what.Add("a building");
                if (OnRoad(mid)) what.Add("a road");
                bool sameOwner = f.Owner == g.Owner;
                var j0 = new JObject
                {
                    ["at"] = new JArray(Math.Round(mid.x, 1), Math.Round(mid.y, 1)), ["len"] = Math.Round(bestD, 2), ["kind"] = kind,
                    ["a"] = $"{f.Name}{(f.Ours ? " (Outgrow)" : f.Lane ? " lane" : "")} of {f.Owner.Index}", ["b"] = $"{g.Name}{(g.Ours ? " (Outgrow)" : g.Lane ? " lane" : "")} of {g.Owner.Index}",
                    ["sameHouse"] = sameOwner, ["in"] = what.Count > 0 ? string.Join("; ", what) : "nothing",
                };
                // two of a house's own fence lanes: the game's design (a gate, the driveway's opening), counted apart
                if (f.Lane && g.Lane && sameOwner && bestD >= 0.5f) { houseGaps++; continue; }
                if (bestD >= 0.5f)
                {
                    gaps.Add(j0);
                    if (what.All(x => x.StartsWith("hidden"))) gapSpots.Add((mid, bestD, kind));
                }
            }
            // ends with no other end near: open runs (shown on the map only)
            var openEnds = loose.Where((l, i) => !used.Contains(i)).Select(l => l.P).ToList();

            // crossings: two drawn fences crossing (each carries on past the other by more than a metre)
            var crossings = new List<JObject>();
            var crossSpots = new List<float2>();
            var doubles = new List<JObject>();
            var pairSeen = new HashSet<(int, int)>();
            foreach (var f in drawn)
                foreach (var g in Near((f.Lo + f.Hi) * 0.5f, math.cmax(f.Hi - f.Lo) * 0.5f + 1f))
                {
                    if (g.Index <= f.Index || !pairSeen.Add((f.Index, g.Index))) continue;
                    if (g.Hi.x < f.Lo.x - 0.5f || g.Lo.x > f.Hi.x + 0.5f || g.Hi.y < f.Lo.y - 0.5f || g.Lo.y > f.Hi.y + 0.5f) continue;
                    float lenF = 0f, lenG = 0f;
                    for (int i = 0; i + 1 < f.Pts.Length; i++) lenF += math.distance(f.Pts[i], f.Pts[i + 1]);
                    for (int i = 0; i + 1 < g.Pts.Length; i++) lenG += math.distance(g.Pts[i], g.Pts[i + 1]);
                    float atF = 0f;
                    bool crossed = false;
                    for (int i = 0; i + 1 < f.Pts.Length && !crossed; i++)
                    {
                        float atG = 0f;
                        for (int k = 0; k + 1 < g.Pts.Length && !crossed; k++)
                        {
                            if (SegmentHit(f.Pts[i], f.Pts[i + 1], g.Pts[k], g.Pts[k + 1], out float tf, out float tg))
                            {
                                float sf = atF + tf * math.distance(f.Pts[i], f.Pts[i + 1]);
                                float sg = atG + tg * math.distance(g.Pts[k], g.Pts[k + 1]);
                                var d1 = math.normalizesafe(f.Pts[i + 1] - f.Pts[i]);
                                var d2 = math.normalizesafe(g.Pts[k + 1] - g.Pts[k]);
                                float clearF = math.min(sf, lenF - sf), clearG = math.min(sg, lenG - sg);
                                if (math.abs(math.dot(d1, d2)) < 0.97f && clearF > 1f + g.Width * 0.5f && clearG > 1f + f.Width * 0.5f)
                                {
                                    var at = math.lerp(f.Pts[i], f.Pts[i + 1], tf);
                                    crossed = true;
                                    crossSpots.Add(at);
                                    crossings.Add(new JObject
                                    {
                                        ["at"] = new JArray(Math.Round(at.x, 1), Math.Round(at.y, 1)),
                                        ["a"] = $"{f.Name}{(f.Ours ? " (Outgrow)" : f.Lane ? " lane" : "")} of {f.Owner.Index}", ["b"] = $"{g.Name}{(g.Ours ? " (Outgrow)" : g.Lane ? " lane" : "")} of {g.Owner.Index}",
                                        ["sameHouse"] = f.Owner == g.Owner, ["angle"] = Math.Round(math.degrees(math.acos(math.saturate(math.abs(math.dot(d1, d2))))), 0),
                                    });
                                }
                            }
                            atG += math.distance(g.Pts[k], g.Pts[k + 1]);
                        }
                        atF += math.distance(f.Pts[i], f.Pts[i + 1]);
                    }
                    if (crossed) continue;
                    // lying on one another: nearly parallel, within half a metre, over more than a metre
                    if (f.Pts.Length == 2 && g.Pts.Length == 2)
                    {
                        var d = math.normalizesafe(f.Pts[1] - f.Pts[0]);
                        var e2 = math.normalizesafe(g.Pts[1] - g.Pts[0]);
                        if (math.abs(math.dot(d, e2)) < 0.97f) continue;
                        var nrm = new float2(-d.y, d.x);
                        if (math.abs(math.dot((g.Pts[0] + g.Pts[1]) * 0.5f - f.Pts[0], nrm)) > 0.5f) continue;
                        float t0 = math.dot(g.Pts[0] - f.Pts[0], d), t1 = math.dot(g.Pts[1] - f.Pts[0], d);
                        float over = math.min(lenF, math.max(t0, t1)) - math.max(0f, math.min(t0, t1));
                        if (over > 1f)
                            doubles.Add(new JObject { ["at"] = new JArray(Math.Round((f.Pts[0].x + f.Pts[1].x) * 0.5f, 1), Math.Round((f.Pts[0].y + f.Pts[1].y) * 0.5f, 1)), ["over"] = Math.Round(over, 1), ["a"] = f.Name, ["b"] = g.Name, ["sameHouse"] = f.Owner == g.Owner });
                    }
                }

            // fences inside a garden: more than a metre in from its edge
            var inside = new List<JObject>();
            var insideSpots = new List<float2>();
            var inner = gardens.Select(gd => (gd.House, Rings: Poly.Offset(gd.Ring, 1f), gd.Lo, gd.Hi)).ToList();
            foreach (var f in drawn)
            {
                float2 m = f.Pts.Length == 2 ? (f.Pts[0] + f.Pts[1]) * 0.5f : f.Pts[f.Pts.Length / 2];
                foreach (var gd in inner)
                {
                    if (m.x < gd.Lo.x || m.y < gd.Lo.y || m.x > gd.Hi.x || m.y > gd.Hi.y || !InAnyRing(gd.Rings, m)) continue;
                    // the house's own lot keeps its own lines (a fence round the back yard inside the lot)
                    var lot = buildings.FirstOrDefault(bd => bd.E == gd.House).Lot;
                    if (f.Owner == gd.House && lot != null && Poly.Contains(lot, m)) continue;
                    inside.Add(new JObject { ["at"] = new JArray(Math.Round(m.x, 1), Math.Round(m.y, 1)), ["fence"] = $"{f.Name}{(f.Ours ? " (Outgrow)" : f.Lane ? " lane" : "")} of {f.Owner.Index}", ["garden"] = gd.House.Index, ["own"] = f.Owner == gd.House });
                    insideSpots.Add(m);
                    break;
                }
            }

            // gardens over each other
            var overlaps = new List<JObject>();
            for (int i = 0; i < gardens.Count; i++)
                for (int j = i + 1; j < gardens.Count; j++)
                {
                    var (ha, ra, la, ua) = gardens[i];
                    var (hb, rb, lb, ub) = gardens[j];
                    if (ub.x < la.x || lb.x > ua.x || ub.y < la.y || lb.y > ua.y) continue;
                    var inter = Poly.Boolean(new[] { (IReadOnlyList<float2>)ra }, new[] { (IReadOnlyList<float2>)rb }, BoolOp.Intersection, FillRule.NonZero);
                    float area = inter.Sum(r => r.Area);
                    if (area < 1f) continue;
                    var c = Poly.Centroid(inter.OrderByDescending(r => r.Area).First().Outer);
                    overlaps.Add(new JObject { ["a"] = ha.Index, ["b"] = hb.Index, ["m2"] = Math.Round(area, 1), ["at"] = new JArray(Math.Round(c.x, 1), Math.Round(c.y, 1)) });
                }

            int hiddenOurs = bits.Count(f => f.Hidden && f.Ours), hiddenLanes = bits.Count(f => f.Hidden && f.Lane);
            rec["fences"] = bits.Count;
            rec["drawn"] = drawn.Count;
            rec["hiddenOutgrowPieces"] = hiddenOurs;
            rec["hiddenLanes"] = hiddenLanes;
            rec["gaps"] = gaps.Count;
            rec["houseOwnGaps"] = houseGaps;
            rec["gapsByKind"] = new JObject(gaps.GroupBy(g => (string)g["kind"]).Select(g => new JProperty(g.Key, g.Count())));
            rec["gapsOverHidden"] = gaps.Count(g => ((string)g["in"]).StartsWith("hidden"));
            rec["crossings"] = crossings.Count;
            rec["doubles"] = doubles.Count;
            rec["insideGardens"] = inside.Count;
            rec["gardenOverlaps"] = overlaps.Count;
            rec["openEnds"] = openEnds.Count;
            rec["gapList"] = new JArray(gaps.OrderByDescending(g => (double)g["len"]).Take(30));
            rec["crossingList"] = new JArray(crossings.Take(30));
            rec["insideList"] = new JArray(inside.Take(30));
            rec["overlapList"] = new JArray(overlaps.OrderByDescending(o => (double)o["m2"]).Take(20));
            Mod.Log.Info($"Placemaker lab: fence map ({label}): {bits.Count} fences ({drawn.Count} drawn, {hiddenOurs} of Outgrow's pieces and {hiddenLanes} lanes hidden by the game); "
                + $"{gaps.Count} gaps ({string.Join(", ", gaps.GroupBy(g => (string)g["kind"]).Select(g => $"{g.Count()} {g.Key}"))}; {rec["gapsOverHidden"]} over a hidden piece), "
                + $"{crossings.Count} crossings, {doubles.Count} doubles, {houseGaps} gaps between a house's own lanes (its design), {inside.Count} fences inside a garden, {overlaps.Count} gardens over each other, {openEnds.Count} other open ends");
            foreach (var g in gaps.OrderByDescending(x => (double)x["len"]).Take(25)) Mod.Log.Info("Placemaker lab: fence map: gap " + g.ToString(Formatting.None));
            foreach (var c in crossings.Take(25)) Mod.Log.Info("Placemaker lab: fence map: crossing " + c.ToString(Formatting.None));
            foreach (var x in inside.Take(15)) Mod.Log.Info("Placemaker lab: fence map: inside " + x.ToString(Formatting.None));
            foreach (var o in overlaps.OrderByDescending(o => (double)o["m2"]).Take(15)) Mod.Log.Info("Placemaker lab: fence map: gardens over each other " + o.ToString(Formatting.None));

            // everything, for the picture (tools/suite/FenceMap.js)
            JArray P(float2 p) => new JArray(Math.Round(p.x, 2), Math.Round(p.y, 2));
            JArray R(IEnumerable<float2> r) => new JArray(r.Select(P));
            var dump = new JObject
            {
                ["label"] = label, ["scenario"] = st.S.Id, ["box"] = new JArray(P(lo), P(hi)),
                ["roads"] = new JArray(roads.Select(r => R(r.Ring))),
                ["buildings"] = new JArray(buildings.Select(bd => new JObject { ["id"] = bd.E.Index, ["lot"] = bd.Lot != null ? R(bd.Lot) : null, ["body"] = bd.Body != null ? R(bd.Body) : null })),
                ["props"] = new JArray(props.Select(R)),
                ["gardens"] = new JArray(gardens.Select(gd => new JObject { ["house"] = gd.House.Index, ["ring"] = R(gd.Ring) })),
                ["fences"] = new JArray(bits.Select(f => new JObject { ["id"] = f.E.Index, ["name"] = f.Name, ["lane"] = f.Lane, ["hidden"] = f.Hidden, ["ours"] = f.Ours, ["owner"] = f.Owner.Index, ["w"] = Math.Round(f.Width, 2), ["pts"] = R(f.Pts) })),
                ["gaps"] = new JArray(gaps), ["crossings"] = new JArray(crossings), ["doubles"] = new JArray(doubles), ["inside"] = new JArray(inside), ["overlaps"] = new JArray(overlaps),
                ["openEnds"] = new JArray(openEnds.Select(P)),
            };
            try
            {
                var file = Path.Combine(_out, $"fencemap-{Safe(st.S.Id)}-{Safe(label)}.json");
                File.WriteAllText(file, dump.ToString(Formatting.None));
                rec["file"] = file;
            }
            catch (Exception e) { rec["fileError"] = e.Message; }

            // where gardens meet (the user: "the nitty-gritty of two lots interacting ... around something ... a weird angle"):
            // a corner of one garden on another's edge, scored by how odd it is: a sharp or wide angle, a house, shed or road
            // close by, a problem found there
            var meets = new List<(float2 At, float Score, string Why)>();
            for (int i = 0; i < gardens.Count; i++)
            {
                var ring = gardens[i].Ring;
                for (int k = 0; k < ring.Length; k++)
                {
                    var v = ring[k];
                    var others = new List<int>();
                    for (int j = 0; j < gardens.Count; j++)
                    {
                        if (j == i) continue;
                        var (_, r2, l2, h2) = gardens[j];
                        if (v.x < l2.x - 1f || v.y < l2.y - 1f || v.x > h2.x + 1f || v.y > h2.y + 1f) continue;
                        if (Poly.Contains(r2, v) || DistanceToRing(r2, v) < 1f) others.Add(j);
                    }
                    if (others.Count == 0) continue;
                    var a = ring[(k + ring.Length - 1) % ring.Length];
                    var c = ring[(k + 1) % ring.Length];
                    float ang = math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(a - v), math.normalizesafe(c - v)), -1f, 1f)));
                    if (ang > 172f) continue;   // a point on a straight edge, not a corner
                    float odd = math.min(math.abs(ang - 90f), 90f) / 90f;
                    var why = new List<string> { $"{gardens[i].House.Index} meets {string.Join("/", others.Select(j => gardens[j].House.Index))} at {ang:F0}°" };
                    float score = odd * 2f + others.Count - 1;
                    if (roads.Any(r => v.x >= r.Lo.x - 6f && v.x <= r.Hi.x + 6f && v.y >= r.Lo.y - 6f && v.y <= r.Hi.y + 6f && (Poly.Contains(r.Ring, v) || DistanceToRing(r.Ring, v) < 6f))) { score += 1f; why.Add("by a road"); }
                    if (InBody(v, 5f)) { score += 1.5f; why.Add("by a building"); }
                    int probs = gaps.Count(g => math.distance(new float2((float)g["at"][0], (float)g["at"][1]), v) < 8f) + crossSpots.Count(p => math.distance(p, v) < 8f);
                    if (probs > 0) { score += 2f * probs; why.Add($"{probs} problems near"); }
                    meets.Add((v, score, string.Join(", ", why)));
                }
            }
            // garden edges along roads, paths and turning circles (a fence that follows the street)
            var edges = new List<(float2 At, float Score, string Why)>();
            foreach (var gd in gardens)
                for (int k = 0; k < gd.Ring.Length; k++)
                {
                    var a = gd.Ring[k];
                    var c = gd.Ring[(k + 1) % gd.Ring.Length];
                    if (math.distance(a, c) < 2f) continue;
                    var m = (a + c) * 0.5f;
                    if (!roads.Any(r => m.x >= r.Lo.x - 4f && m.x <= r.Hi.x + 4f && m.y >= r.Lo.y - 4f && m.y <= r.Hi.y + 4f && DistanceToRing(r.Ring, m) < 4f)) continue;
                    // a bend in the edge (the garden follows a curve) is the interesting part
                    var nx = gd.Ring[(k + 2) % gd.Ring.Length];
                    float bend = math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(c - a), math.normalizesafe(nx - c)), -1f, 1f)));
                    int probs = gaps.Count(g => math.distance(new float2((float)g["at"][0], (float)g["at"][1]), m) < 10f);
                    edges.Add((m, bend / 30f + probs * 2f, $"{gd.House.Index} along a road, bend {bend:F0}°{(probs > 0 ? $", {probs} gaps near" : "")}"));
                }

            // close-ups: the worst spots, then where gardens meet, then edges along roads
            int closeups = (int?)step["closeups"] ?? 0;
            int junctions = (int?)step["junctions"] ?? 0;
            int roadEdges = (int?)step["edges"] ?? 0;
            var focus = new List<(string Kind, float2 At, string Why)>();
            void Pick(string kind, IEnumerable<(float2 At, string Why)> spots, int max, float apart = 20f)
            {
                int n = 0;
                foreach (var (p, why) in spots)
                {
                    if (n >= max) break;
                    if (focus.Any(f => math.distance(f.At, p) < apart)) continue;
                    focus.Add((kind, p, why));
                    n++;
                }
            }
            // "spotsFrom": the close-ups an earlier map of this run took (the same spots before and after a change), then its own
            if (!string.IsNullOrEmpty((string)step["spotsFrom"]) && _fenceSpots.TryGetValue((string)step["spotsFrom"], out var earlier))
                foreach (var e in earlier) focus.Add((e.Kind, e.At, "as in " + (string)step["spotsFrom"] + ": " + e.Why));
            Pick("crossing", crossings.Select(c => (new float2((float)c["at"][0], (float)c["at"][1]), c.ToString(Formatting.None))), math.max(1, closeups / 3));
            Pick("gap", gaps.Where(g => !((string)g["in"]).Contains("building") && !((string)g["in"]).Contains("road")).OrderByDescending(g => (double)g["len"]).Select(g => (new float2((float)g["at"][0], (float)g["at"][1]), g.ToString(Formatting.None))), math.max(1, closeups / 3));
            Pick("inside", inside.Select(x => (new float2((float)x["at"][0], (float)x["at"][1]), x.ToString(Formatting.None))), 1);
            Pick("gap", gaps.OrderByDescending(g => (double)g["len"]).Select(g => (new float2((float)g["at"][0], (float)g["at"][1]), g.ToString(Formatting.None))), math.max(0, closeups - focus.Count));
            Pick("meet", meets.OrderByDescending(x => x.Score).Select(x => (x.At, x.Why)), junctions, 14f);
            Pick("edge", edges.OrderByDescending(x => x.Score).Select(x => (x.At, x.Why)), roadEdges, 25f);
            rec["meetSpots"] = meets.Count;
            rec["roadEdgeSpots"] = edges.Count;
            _fenceSpots[label] = focus.ToList();
            var shots = new JArray();
            for (int i = 0; i < focus.Count; i++)
            {
                var sr = new JObject { ["at"] = new JArray(Math.Round(focus[i].At.x, 1), Math.Round(focus[i].At.y, 1)), ["kind"] = focus[i].Kind, ["what"] = focus[i].Why };
                // low enough to see a fence stand (a fence seen from straight above is a line), close enough to see a gap
                var cam = new JObject { ["target"] = "point", ["at"] = new JArray(focus[i].At.x, focus[i].At.y), ["abs"] = true, ["view"] = "oblique", ["pitch"] = 55, ["yaw"] = (i * 77) % 360, ["distance"] = 32 };
                yield return StepShot(st, new JObject { ["camera"] = cam, ["shotMode"] = "render" }, sr, $"fence-{focus[i].Kind}-{i + 1}");
                Mod.Log.Info($"Placemaker lab: fence map close-up {i + 1} ({focus[i].Kind}) at ({focus[i].At.x:F1}, {focus[i].At.y:F1}): {focus[i].Why}");
                shots.Add(sr);
            }
            rec["closeupShots"] = shots;
        }

        /// <summary>Segments p0-p1 and q0-q1 cross (not at their very ends): where along each (0..1).</summary>
        private static bool SegmentHit(float2 p0, float2 p1, float2 q0, float2 q1, out float tp, out float tq)
        {
            tp = tq = 0f;
            var r = p1 - p0;
            var s = q1 - q0;
            float den = r.x * s.y - r.y * s.x;
            if (math.abs(den) < 1e-6f) return false;
            var w = q0 - p0;
            tp = (w.x * s.y - w.y * s.x) / den;
            tq = (w.x * r.y - w.y * r.x) / den;
            return tp >= 0f && tp <= 1f && tq >= 0f && tq <= 1f;
        }
    }
}
