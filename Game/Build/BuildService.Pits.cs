// Placemaker — the live pit check after every build (core, GPL-3.0).
//
// The user: "one of the major bugs ... a certain dip" (docs/refactor/GRADES.md). The game runner's terrain map showed bowls 4-9 m
// deep next to lanes and in lots (strip mall, mall district) right after builds whose own check said "no bowl": that check only
// looked at the texels the terraform covered (TerraformPits.Worst), and a bowl at a lot entrance or under a lane has none.
// This scan covers the whole outline and a rim around it, for every build path, and names what stands at each bowl (the nets with
// their heights against the ground), so a bowl is never silent.

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Game.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        /// <summary>A live bowl deeper than this (m) is reported (PitDepth is the planning guard's 0.35 m; the live ground is rougher).</summary>
        private const float LivePitDepth = 1.0f;
        private const float LivePitRim = 12f;

        /// <summary>Bowls found by the last scan (world XZ, depth m); the game runner and the repair read them.</summary>
        private readonly List<TerraformPits.Pit> _livePits = new List<TerraformPits.Pit>();

        /// <summary>
        /// (perf) Set from PlacemakerSettings.DebugBuildChecks (off): the build's checks that only write to the log — the live pit
        /// scans, the sagging edges, the hollows and the zoning checks. Each pit scan alone froze a frame for 135–190 ms at 1.5 ha
        /// and 1.6 s at 16 ha, three times per build (teardown 14).
        /// </summary>
        public static bool DebugBuildChecks;

        /// <summary>
        /// (core) Every bowl deeper than <see cref="LivePitDepth"/> in the live ground of the build's outline and a rim of
        /// <see cref="LivePitRim"/> m, on a 2 m grid (the ring test of TerraformPits). Logs each with the nets standing at it.
        /// Types that dig on purpose (quarry, ponds, ...) are scanned too, but only reported.
        /// </summary>
        private List<TerraformPits.Pit> ScanLivePits(string when)
        {
            _livePits.Clear();
            try
            {
                if (!DebugBuildChecks || LooseBuild || _request?.Plan == null) return _livePits;
                var outline = BuildOutline();
                if (outline == null || outline.Length < 3) return _livePits;
                float2 min = outline[0], max = outline[0];
                foreach (var p in outline) { min = math.min(min, p); max = math.max(max, p); }
                min -= LivePitRim; max += LivePitRim;
                _heightsValid = false;
                bool Inside(float2 p) => Poly.Contains(outline, p) || Poly.DistanceToBoundary(outline, p) <= LivePitRim;
                var pits = TerraformPits.Scan(Height, min, max, Inside, LivePitDepth, 4);
                _livePits.AddRange(pits);
                bool intentional = TerraformPits.Intentional(_request.Plan);
                if (pits.Count == 0)
                {
                    Mod.Log.Info($"Placemaker pit check ({when}): no bowl deeper than {LivePitDepth:F1} m in the live ground of the build and {LivePitRim:F0} m around it.");
                    return _livePits;
                }
                foreach (var p in pits)
                    Mod.Log.Warn($"Placemaker pit check ({when}): bowl {p.Depth:F2} m deep at ({p.At.x:F0}, {p.At.y:F0}), live ground {p.Height:F2} m"
                        + (intentional ? " (this type digs on purpose)" : "") + ". Terraform there: " + TerraformAt(p.At) + ". Nearby: " + NetsNear(p.At, 18f) + ".");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker pit check failed: " + e.Message); }
            return _livePits;
        }

        /// <summary>
        /// (core) The dip next to lanes and at lot entrances (strip mall 4-7 m, mall district 6-7 m): measured in the game, our edges are
        /// straight when applied and, within 40 frames, a lot's drive connector (an invisible road path: a marker net that does not flatten
        /// the ground) sags several metres in the middle and the ground with it. Game.Simulation.GroundHeightSystem re-fits every net
        /// without FlattenTerrain to the terrain whenever the terrain changes near it (NetUtils.AdjustPosition), and the junction with the
        /// lane (a real road: FlattenTerrain | ClipTerrain) clips the ground to the connector's height: each pass digs a little deeper.
        /// Vanilla breaks that loop only for a building's own sub-nets (GroundHeightSystem.IsFixedNode, lot owners). Our nets have no
        /// owner, so they get the same protection the way the game marks a net "not on the ground": a Game.Net.Elevation of 0 on the edge
        /// (its middle stays linear between its ends) and on its nodes (the ends are fixed). Elevation 0 adds no composition flag
        /// (NetCompositionHelpers.GetElevationFlags: no elevated, raised, lowered or tunnel), so the nets look and work the same.
        /// Every grouped marker edge of every build path gets it on the Apply frame (the temporaries, so the first terrain pass after
        /// Apply already finds them pinned) and again right after Apply (pieces the game split or replaced while applying).
        /// </summary>
        private int PinMarkerNets(bool temporaries = false)
        {
            int edges = 0, nodes = 0;
            try
            {
                if (_groupId == 0) return 0;
                using var q = EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = temporaries
                        ? new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Temp>() }
                        : new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PlacemakerMember>(), ComponentType.ReadOnly<PrefabRef>() },
                    None = temporaries ? new[] { ComponentType.ReadOnly<Deleted>() } : new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
                });
                using var list = q.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < list.Length; i++)
                {
                    var e = list[i];
                    if (EntityManager.GetComponentData<PlacemakerMember>(e).GroupId != _groupId) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<NetGeometryData>(prefab)) continue;
                    if ((EntityManager.GetComponentData<NetGeometryData>(prefab).m_Flags & global::Game.Net.GeometryFlags.FlattenTerrain) != 0) continue;
                    if (!EntityManager.HasComponent<global::Game.Net.Elevation>(e)) { EntityManager.AddComponentData(e, new global::Game.Net.Elevation(float2.zero)); edges++; }
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    foreach (var n in new[] { edge.m_Start, edge.m_End })
                        if (n != Entity.Null && EntityManager.Exists(n) && !EntityManager.HasComponent<global::Game.Net.Elevation>(n))
                        {
                            EntityManager.AddComponentData(n, new global::Game.Net.Elevation(float2.zero));
                            nodes++;
                        }
                }
                if (edges + nodes > 0) Mod.Log.Info($"Placemaker: pinned {edges} marker edge(s) and {nodes} node(s) of group {_groupId}{(temporaries ? " (temporaries, Apply frame)" : "")} to their built heights (no terrain sag at lot entrances).");
            }
            catch (Exception ex) { Mod.Log.Warn("Placemaker: pinning marker nets failed: " + ex.Message); }
            return edges;
        }

        /// <summary>
        /// (core, pit diagnosis) Every edge in the build's box (temporaries included) whose curve leaves the straight line between its
        /// ends by more than 1 m: a sag digs the ground under it (the game conforms the terrain to its nets).
        /// </summary>
        private void LogSaggingEdges(string when)
        {
            if (!DebugBuildChecks) return;
            try
            {
                var outline = BuildOutline();
                if (outline == null) return;
                float2 min = outline[0], max = outline[0];
                foreach (var p in outline) { min = math.min(min, p); max = math.max(max, p); }
                min -= 20f; max += 20f;
                using var q = EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() },
                    None = new[] { ComponentType.ReadOnly<Deleted>() },
                });
                using var edges = q.ToEntityArray(Allocator.Temp);
                int n = 0, scanned = 0;
                for (int i = 0; i < edges.Length && n < 8; i++)
                {
                    var e = edges[i];
                    var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    var mid = Colossal.Mathematics.MathUtils.Position(b, 0.5f);
                    if (mid.x < min.x || mid.z < min.y || mid.x > max.x || mid.z > max.y) continue;
                    scanned++;
                    float dev = 0f, at = 0f;
                    foreach (float t in new[] { 0.25f, 0.5f, 0.75f })
                    {
                        float y = Colossal.Mathematics.MathUtils.Position(b, t).y, line = math.lerp(b.a.y, b.d.y, t);
                        if (math.abs(y - line) > math.abs(dev)) { dev = y - line; at = t; }
                    }
                    if (math.abs(dev) <= 1f) continue;
                    n++;
                    bool temp = EntityManager.HasComponent<Temp>(e), ours = EntityManager.HasComponent<PlacemakerMember>(e);
                    var p = Colossal.Mathematics.MathUtils.Position(b, at);
                    Mod.Log.Warn($"Placemaker net sag ({when}): {(temp ? "temporary" : ours ? "our" : "city")} {_prefabs.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)} "
                        + $"{(dev < 0 ? "sags" : "humps")} {math.abs(dev):F1} m at ({p.x:F0}, {p.z:F0}): a {b.a.y:F1} b {b.b.y:F1} c {b.c.y:F1} d {b.d.y:F1}, "
                        + $"from ({b.a.x:F0}, {b.a.z:F0}) to ({b.d.x:F0}, {b.d.z:F0}), length {Colossal.Mathematics.MathUtils.Length(b):F1} m, ground there {Height(p.xz):F1}.");
                }
                if (n == 0) Mod.Log.Info($"Placemaker net sag ({when}): none of {scanned} edge(s) in the build's box leaves its end-to-end line by more than 1 m.");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker net sag check failed: " + e.Message); }
        }

        /// <summary>The planned terraform items covering a point (layer, kind, target, sloped/apron) and the raster's target and weight there.</summary>
        private string TerraformAt(float2 at)
        {
            var parts = new List<string>();
            try
            {
                if (_tfRaster != null)
                {
                    float g = Height(at);
                    if (_tfRaster.TrySample(at, _ => g, out float t)) parts.Add($"raster target {t:F2} m");
                    else parts.Add("no raster target");
                }
                var plan = _request?.Plan;
                if (plan?.Layers != null)
                    foreach (var l in plan.Layers)
                    {
                        if (l?.Terraform == null) continue;
                        foreach (var t in l.Terraform)
                        {
                            if (t?.Ring == null || t.Ring.Length < 3) continue;
                            var r2 = Poly.To2D(t.Ring);
                            if (!Poly.Contains(r2, at) && Poly.DistanceToBoundary(r2, at) > 2f) continue;
                            float lo = float.MaxValue, hi = float.MinValue;
                            foreach (var v in t.Ring) { lo = math.min(lo, v.y); hi = math.max(hi, v.y); }
                            parts.Add($"{l.GeneratorId} {t.Kind} {t.TargetHeight:F2} m" + (t.Sloped ? $" sloped {lo:F1}..{hi:F1}" : "") + (t.Apron ? " apron" : "") + $" ({Poly.Area(r2):F0} m2)");
                            if (parts.Count > 8) break;
                        }
                    }
                // The planned nets through it (their control point heights: the game's curve follows them).
                if (plan?.Layers != null)
                    foreach (var l in plan.Layers)
                    {
                        if (l?.Nets == null) continue;
                        foreach (var n in l.Nets)
                        {
                            if (n == null) continue;
                            float best = float.MaxValue;
                            for (int k = 0; k <= 8; k++) best = math.min(best, math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, k / 8f).xz, at));
                            if (best > 6f) continue;
                            parts.Add($"planned {l.GeneratorId} {n.Role} y {n.A.y:F1}/{n.B.y:F1}/{n.C.y:F1}/{n.D.y:F1}{(n.FollowTerrain ? "" : " (deck)")} {best:F0} m away");
                        }
                    }
            }
            catch (Exception e) { parts.Add("(" + e.Message + ")"); }
            return parts.Count > 0 ? string.Join("; ", parts) : "none";
        }

        /// <summary>The live nets within <paramref name="radius"/> m of a point: prefab, ours or the city's, curve heights against the ground there.</summary>
        private string NetsNear(float2 at, float radius)
        {
            var parts = new List<string>();
            try
            {
                using var q = EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() },
                    None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
                });
                using var edges = q.ToEntityArray(Allocator.Temp);
                var hits = new List<(float d, string text)>();
                for (int i = 0; i < edges.Length; i++)
                {
                    var e = edges[i];
                    var b = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    float best = float.MaxValue; float tBest = 0f;
                    for (int k = 0; k <= 16; k++)
                    {
                        float t = k / 16f;
                        float d = math.distance(Colossal.Mathematics.MathUtils.Position(b, t).xz, at);
                        if (d < best) { best = d; tBest = t; }
                    }
                    if (best > radius) continue;
                    var name = _prefabs.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab);
                    bool ours = EntityManager.HasComponent<PlacemakerMember>(e);
                    var near = Colossal.Mathematics.MathUtils.Position(b, tBest);
                    string elev = "";
                    var edge = EntityManager.GetComponentData<Edge>(e);
                    foreach (var n in new[] { edge.m_Start, edge.m_End })
                        if (EntityManager.HasComponent<global::Game.Net.Elevation>(n))
                        {
                            var el = EntityManager.GetComponentData<global::Game.Net.Elevation>(n).m_Elevation;
                            if (math.any(math.abs(el) > 0.05f)) elev += $" node elevation {el.x:F1}/{el.y:F1}";
                        }
                    hits.Add((best, $"{(ours ? "our" : "city")} {name} {best:F0} m away, curve y {b.a.y:F1}..{near.y:F1}..{b.d.y:F1} (ground {Height(b.a.xz):F1}/{Height(near.xz):F1}/{Height(b.d.xz):F1}){elev}"));
                }
                hits.Sort((x, y) => x.d.CompareTo(y.d));
                for (int i = 0; i < hits.Count && i < 6; i++) parts.Add(hits[i].text);
            }
            catch (Exception e) { parts.Add("(nets unreadable: " + e.Message + ")"); }
            return parts.Count > 0 ? string.Join("; ", parts) : "no net";
        }
    }
}
