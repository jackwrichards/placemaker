// Game runner lab (exploration, not a Placemaker feature): "yard stitching". The game's houses are fixed models on
// rectangular lots, so neighbours never join up: bare strips between lots, wedges on curves, lawns that stop short of
// the neighbour's fence. The user fixes this by hand with the vanilla Surface Area tool (dragging a house's own lawn
// across the gap). This does the same automatically, for every house, whatever its asset:
//   {"do":"lab","op":"street","shape":"arc","radius":90,"from":0,"to":180}   a curved (or "straight") street, zoned both sides
//     ("elevation": 10 lays it that high above the ground: an elevated road, an overpass; [2, 12] a ramp climbing along it;
//     with "zone":"none")
//   {"do":"lab","op":"street","shape":"polygon","points":[[x,z],...]}          a closed loop of streets: a block of any shape
//   {"do":"grow", ...}                                                         houses grow
//   {"do":"lab","op":"yards","reach":10,"depth":56,"closeups":2}               each house's own lawn grows into the bare land
// Bare land = inside the street's zoning depth, not road, not under any surface; each piece goes to the nearest lot within
// `reach` (a Voronoi split at 1 m), is merged with that house's own lawn (same surface prefab, so it joins seamlessly), cut
// exactly around every other surface, and written into the lawn's own nodes (its owner stays the house).
//   {"do":"lab","op":"yardsCheck"}  how many stretched lawns still have their new shape (a level-up rebuilds them).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ street: a curved or straight road, zoned on both sides â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private IEnumerator LabStreet(ScenarioState st, JObject step, JObject rec)
        {
            var b = st.S.Block;
            string shape = ((string)step["shape"] ?? "arc").ToLowerInvariant();
            string roadName = (string)step["road"] ?? Placemaker.Core.Suite.SuiteLayout.SmallRoad;
            if (!_fixture.Prefabs.TryGetValue(roadName, out var road)) { rec["error"] = "lab: no road prefab " + roadName; yield break; }
            var curves = new List<Bezier4x3>();
            float elevation = 0f, elevationTo = 0f;
            if (step["elevation"] is JArray ramp && ramp.Count >= 2) { elevation = (float)ramp[0]; elevationTo = (float)ramp[1]; }
            else elevation = elevationTo = (float?)step["elevation"] ?? 0f;
            float3 P(float2 p) => new float3(p.x, _fixture.H(p.x, p.y), p.y);
            if (shape == "block")
            {
                // a square loop of streets: houses on all four sides meet back to back in the middle
                float side = (float?)step["length"] ?? 130f, h = side * 0.5f;
                var c = Pt(step["at"], b, false);
                var corners = new[] { c + new float2(-h, -h), c + new float2(h, -h), c + new float2(h, h), c + new float2(-h, h) };
                for (int k = 0; k < 4; k++)
                {
                    float2 p = corners[k], q = corners[(k + 1) % 4];
                    int n = (int)math.ceil(side / 75f);
                    for (int i = 0; i < n; i++) curves.Add(NetUtils.StraightCurve(P(math.lerp(p, q, i / (float)n)), P(math.lerp(p, q, (i + 1) / (float)n))));
                }
            }
            else if (shape == "polygon")
            {
                // a closed loop of streets through the given corners ("points", from the land's middle): a block of any shape,
                // e.g. one corner cut off by a street at 45 degrees
                var corners = new List<float2>();
                if (step["points"] is JArray pts) foreach (var p in pts) corners.Add(Pt(p, b, false));
                if (corners.Count < 3) { rec["error"] = "lab: a polygon street needs 3 or more points"; yield break; }
                for (int k = 0; k < corners.Count; k++)
                {
                    float2 p = corners[k], q = corners[(k + 1) % corners.Count];
                    int n = (int)math.ceil(math.distance(p, q) / 75f);
                    for (int i = 0; i < n; i++) curves.Add(NetUtils.StraightCurve(P(math.lerp(p, q, i / (float)n)), P(math.lerp(p, q, (i + 1) / (float)n))));
                }
            }
            else if (shape == "culdesac")
            {
                // a dead-end street off a cross street (a T): the game rounds the dead end into a turning circle
                float len = (float?)step["length"] ?? 110f, cross = (float?)step["cross"] ?? 180f, yaw = math.radians((float?)step["yaw"] ?? 90f);
                var c = Pt(step["at"], b, false);
                var dir = new float2(math.cos(yaw), math.sin(yaw));
                var side = new float2(-dir.y, dir.x);
                var start = c - dir * (len * 0.5f);
                var end = c + dir * (len * 0.5f);
                int ns = (int)math.ceil(len / 75f);
                for (int k = 0; k < ns; k++) curves.Add(NetUtils.StraightCurve(P(math.lerp(start, end, k / (float)ns)), P(math.lerp(start, end, (k + 1) / (float)ns))));
                curves.Add(NetUtils.StraightCurve(P(start - side * (cross * 0.5f)), P(start)));
                curves.Add(NetUtils.StraightCurve(P(start), P(start + side * (cross * 0.5f))));
            }
            else if (shape == "straight")
            {
                float len = (float?)step["length"] ?? 220f, yaw = math.radians((float?)step["yaw"] ?? 0f);
                var c = Pt(step["at"], b, false);
                var dir = new float2(math.cos(yaw), math.sin(yaw));
                int n = (int)math.ceil(len / 75f);
                for (int k = 0; k < n; k++)
                    curves.Add(NetUtils.StraightCurve(P(c + dir * (len * (k / (float)n - 0.5f))), P(c + dir * (len * ((k + 1) / (float)n - 0.5f)))));
            }
            else
            {
                float r = (float?)step["radius"] ?? 90f;
                bool circle = shape == "circle";
                float a0 = math.radians((float?)step["from"] ?? 0f), a1 = math.radians((float?)step["to"] ?? (circle ? 360f : 180f));
                var c = step["at"] != null ? Pt(step["at"], b, false) : circle ? b.Center : b.Center - new float2(0f, r * 0.45f);
                int n = math.max(1, (int)math.ceil(math.abs(a1 - a0) / math.radians(30f)));
                for (int k = 0; k < n; k++)
                {
                    float t0 = math.lerp(a0, a1, k / (float)n), t1 = math.lerp(a0, a1, (k + 1) / (float)n);
                    float kk = 4f / 3f * math.tan((t1 - t0) / 4f) * r;
                    float2 p0 = c + r * new float2(math.cos(t0), math.sin(t0)), p3 = c + r * new float2(math.cos(t1), math.sin(t1));
                    float2 p1 = p0 + kk * new float2(-math.sin(t0), math.cos(t0)), p2 = p3 - kk * new float2(-math.sin(t1), math.cos(t1));
                    curves.Add(new Bezier4x3(P(p0), P(p1), P(p2), P(p3)));
                }
            }
            // lifted off the ground: each piece's start and end as far along the climb as they lie along the street
            var lift = new List<float2>(curves.Count);
            float total = 0f, run = 0f;
            foreach (var c in curves) total += MathUtils.Length(c.xz);
            for (int i = 0; i < curves.Count; i++)
            {
                float len = MathUtils.Length(curves[i].xz);
                var el = total > 0f ? new float2(math.lerp(elevation, elevationTo, run / total), math.lerp(elevation, elevationTo, (run + len) / total)) : new float2(elevation);
                run += len;
                var c = curves[i];
                c.a.y += el.x; c.b.y += math.lerp(el.x, el.y, 1f / 3f); c.c.y += math.lerp(el.x, el.y, 2f / 3f); c.d.y += el.y;
                curves[i] = c;
                lift.Add(el);
            }
            var em = _world.EntityManager;
            yield return ApplyWithFixtureTool(t =>
            {
                var random = new Unity.Mathematics.Random(0x1AB5u);
                for (int i = 0; i < curves.Count; i++)
                {
                    var curve = curves[i];
                    var el = lift[i];
                    CoursePos End(float3 at, float3 tangent, float delta, float up, CoursePosFlags f) => new CoursePos
                    {
                        m_Entity = Entity.Null, m_Position = at, m_Rotation = NetUtils.GetNodeRotation(tangent), m_CourseDelta = delta,
                        m_Elevation = new float2(up), m_Flags = f | CoursePosFlags.IsLeft | CoursePosFlags.IsRight, m_ParentMesh = -1, m_SplitPosition = 0f,
                    };
                    var def = t.NewDefinition();
                    em.AddComponentData(def, new CreationDefinition { m_Prefab = road, m_RandomSeed = random.NextInt() });
                    em.AddComponent<Updated>(def);
                    em.AddComponentData(def, new NetCourse
                    {
                        m_Curve = curve, m_Length = MathUtils.Length(curve), m_FixedIndex = -1, m_Elevation = new float2((el.x + el.y) * 0.5f),
                        m_StartPosition = End(curve.a, MathUtils.StartTangent(curve), 0f, el.x, CoursePosFlags.IsFirst),
                        m_EndPosition = End(curve.d, MathUtils.EndTangent(curve), 1f, el.y, CoursePosFlags.IsLast),
                    });
                }
                return curves.Count;
            }, "lab street");
            yield return Frames(40);

            // zoning: every cell of the new street's blocks (marquee over the street and its zoning depth)
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var c in curves) for (int i = 0; i <= 16; i++) { var p = MathUtils.Position(c, i / 16f).xz; lo = math.min(lo, p); hi = math.max(hi, p); }
            lo -= 62f; hi += 62f;
            string zoneName = (string)step["zone"] ?? "NA Residential Low";
            if (zoneName == "none")
            {
                // the street's blocks left unzoned (a Zoning type zones them)
                rec["pieces"] = curves.Count;
                st.Ring = new[] { lo + 30f, new float2(hi.x - 30f, lo.y + 30f), hi - 30f, new float2(lo.x + 30f, hi.y - 30f) };
                Event("build");
                Mod.Log.Info($"Placemaker lab: street {shape} ({curves.Count} pieces of {roadName}{(elevation != elevationTo ? $", climbing {elevation:F0} to {elevationTo:F0} m" : elevation != 0f ? $", {elevation:F0} m up" : "")}), not zoned");
                yield break;
            }
            // the same zone in the other theme when this one has none (NA and EU name most zones alike)
            string other = zoneName.StartsWith("NA ") ? "EU " + zoneName.Substring(3) : zoneName.StartsWith("EU ") ? "NA " + zoneName.Substring(3) : zoneName;
            if (!_fixture.Prefabs.TryGetValue(zoneName, out var zone) && !_fixture.Prefabs.TryGetValue(other, out zone)) { rec["error"] = "lab: no zone prefab " + zoneName; yield break; }
            yield return ApplyWithFixtureTool(t =>
            {
                var def = t.NewDefinition();
                em.AddComponentData(def, new CreationDefinition { m_Prefab = zone });
                em.AddComponentData(def, new Zoning { m_Position = new Quad3(P3(lo), P3(new float2(hi.x, lo.y)), P3(hi), P3(new float2(lo.x, hi.y))), m_Flags = ZoningFlags.Zone | ZoningFlags.Marquee | ZoningFlags.Overwrite });
                em.AddComponent<Updated>(def);
                return 1;
            }, "lab zoning");
            yield return Frames(40);
            rec["pieces"] = curves.Count;
            rec["zonedCellsTotal"] = _fixture.CountZonedCells();
            st.Ring = new[] { lo + 30f, new float2(hi.x - 30f, lo.y + 30f), hi - 30f, new float2(lo.x + 30f, hi.y - 30f) };
            Event("build");
            Mod.Log.Info($"Placemaker lab: street {shape} ({curves.Count} pieces of {roadName}), zoned {zoneName} over ({lo.x:F0},{lo.y:F0})-({hi.x:F0},{hi.y:F0})");
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ yards â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private sealed class YardPlan
        {
            public Entity Building, Lawn, LawnPrefab;
            public string LawnName;
            public float2[] Before;
            public float2[] Main;
            public readonly List<float2[]> Extra = new List<float2[]>();
            public float AddedM2;
            public float2 Focus;
        }

        private readonly List<YardPlan> _yardPlans = new List<YardPlan>();

        private static readonly string[] HardSurfaceWords = { "pavement", "concrete", "asphalt", "gravel", "tile", "parking", "construction", "dirt", "sand", "road", "drive", "stone", "brick" };
        private static readonly string[] GrassWords = { "grass", "lawn", "garden", "green" };

        private IEnumerator LabYards(ScenarioState st, JObject step, JObject rec)
        {
            var b = st.S.Block;
            var em = _world.EntityManager;
            float reach = (float?)step["reach"] ?? 10f, depth = (float?)step["depth"] ?? 56f;
            bool apply = (bool?)step["apply"] ?? true;
            int closeups = (int?)step["closeups"] ?? 2;
            float2 bmin = new float2(b.X0, b.Z0), bmax = new float2(b.X1, b.Z1);
            bool In(float2 lo, float2 hi) => hi.x >= bmin.x && lo.x <= bmax.x && hi.y >= bmin.y && lo.y <= bmax.y;

            // what stands there: lots, surfaces (with their owners), roads
            var lots = new List<(Entity E, float2[] Quad, float2 Lo, float2 Hi)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<BuildingData>(prefab)) continue;
                    var quad = BuildingUtils.CalculateCorners(em.GetComponentData<global::Game.Objects.Transform>(e), em.GetComponentData<BuildingData>(prefab).m_LotSize);
                    var r = new[] { quad.a.xz, quad.b.xz, quad.c.xz, quad.d.xz };
                    var (lo, hi) = Bounds(r, 0f);
                    if (In(lo, hi)) lots.Add((e, r, lo, hi));
                }
            var surfaces = new List<(Entity E, Entity Owner, Entity Prefab, string Name, float2[] Ring, float2 Lo, float2 Hi)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Areas.Surface>(), ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<global::Game.Areas.Node>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var nodes = em.GetBuffer<global::Game.Areas.Node>(e, true);
                    if (nodes.Length < 3) continue;
                    var r = new float2[nodes.Length];
                    for (int i = 0; i < r.Length; i++) r[i] = nodes[i].m_Position.xz;
                    var (lo, hi) = Bounds(r, 0f);
                    if (!In(lo, hi)) continue;
                    var ownerOf = em.HasComponent<Owner>(e) ? em.GetComponentData<Owner>(e).m_Owner : Entity.Null;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    surfaces.Add((e, ownerOf, prefab, _fixture.NameOf(prefab), r, lo, hi));
                }
            var roads = new List<(Bezier4x2 C, float Hw, float2 Lo, float2 Hi)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<global::Game.Net.Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<NetGeometryData>(prefab)) continue;
                    var c = em.GetComponentData<global::Game.Net.Curve>(e).m_Bezier.xz;
                    var bb = MathUtils.Bounds(c);
                    if (!In(bb.min - depth, bb.max + depth)) continue;
                    roads.Add((c, em.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth * 0.5f, bb.min, bb.max));
                }

            // each house's lawn: its own surface with a grass-like name (largest), never a paved one
            int Score(string n) { var s = n.ToLowerInvariant(); return GrassWords.Any(s.Contains) ? 2 : HardSurfaceWords.Any(s.Contains) ? 0 : 1; }
            var ownedNames = surfaces.Where(s => s.Owner != Entity.Null).GroupBy(s => s.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()} ({(Score(g.Key) == 2 ? "lawn" : Score(g.Key) == 0 ? "paved" : "other")})").ToList();
            rec["ownedSurfaceKinds"] = new JArray(ownedNames);
            var lawnOf = new Dictionary<Entity, int>();
            foreach (var g in surfaces.Select((s, i) => (s, i)).Where(x => x.s.Owner != Entity.Null).GroupBy(x => x.s.Owner))
            {
                var best = g.Where(x => Score(x.s.Name) >= 1).OrderByDescending(x => Score(x.s.Name)).ThenByDescending(x => Poly.Area(x.s.Ring)).FirstOrDefault();
                if (best.s.Ring != null) lawnOf[g.Key] = best.i;
            }

            // the bare land, split between the nearest lots (1 m cells)
            const float cell = 1f;
            float2 rlo = new float2(float.MaxValue), rhi = new float2(float.MinValue);
            foreach (var l in lots) { rlo = math.min(rlo, l.Lo - reach); rhi = math.max(rhi, l.Hi + reach); }
            rlo = math.max(rlo, bmin); rhi = math.min(rhi, bmax);
            if (lots.Count == 0 || rhi.x <= rlo.x || rhi.y <= rlo.y) { rec["error"] = "lab: no houses in the land (grow first)"; yield break; }
            int w = (int)math.ceil((rhi.x - rlo.x) / cell), h = (int)math.ceil((rhi.y - rlo.y) / cell);
            var owner = new int[w * h];
            int roadCells = 0, coveredCells = 0, outsideCells = 0, bare = 0, farCells = 0;
            for (int j = 0; j < h; j++)
            {
                if (j % 64 == 0) yield return null;
                for (int i = 0; i < w; i++)
                {
                    int idx = j * w + i;
                    owner[idx] = -1;
                    var p = rlo + new float2(i + 0.5f, j + 0.5f) * cell;
                    float near = float.MaxValue, edgeGap = float.MaxValue;
                    foreach (var r in roads)
                    {
                        if (p.x < r.Lo.x - depth || p.x > r.Hi.x + depth || p.y < r.Lo.y - depth || p.y > r.Hi.y + depth) continue;
                        float d = MathUtils.Distance(r.C, p, out _);
                        near = math.min(near, d);
                        edgeGap = math.min(edgeGap, d - r.Hw);
                    }
                    if (edgeGap < 0.3f) { roadCells++; continue; }
                    if (near > depth) { outsideCells++; continue; }
                    bool covered = false;
                    foreach (var s in surfaces)
                        if (p.x >= s.Lo.x && p.x <= s.Hi.x && p.y >= s.Lo.y && p.y <= s.Hi.y && Poly.Contains(s.Ring, p)) { covered = true; break; }
                    if (covered) { coveredCells++; continue; }
                    bare++;
                    int best = -1;
                    float bd = reach;
                    for (int k = 0; k < lots.Count; k++)
                    {
                        var l = lots[k];
                        if (p.x < l.Lo.x - reach || p.x > l.Hi.x + reach || p.y < l.Lo.y - reach || p.y > l.Hi.y + reach) continue;
                        float d = Poly.Contains(l.Quad, p) ? 0f : DistanceToRing(l.Quad, p);
                        if (d <= bd) { bd = d; best = k; }
                    }
                    if (best < 0) farCells++;
                    owner[idx] = best;
                }
            }

            // per house: its share, simplified, cut exactly around every other surface, merged with its lawn
            _yardPlans.Clear();
            int noLawn = 0, tooSmall = 0, failed = 0;
            for (int k = 0; k < lots.Count; k++)
            {
                if (k % 8 == 0) yield return null;
                var mask = new bool[w * h];
                int count = 0;
                for (int i = 0; i < mask.Length; i++) if (owner[i] == k) { mask[i] = true; count++; }
                if (count < 8) { if (count > 0) tooSmall++; continue; }
                if (!lawnOf.TryGetValue(lots[k].E, out int li)) { noLawn++; continue; }
                var lawn = surfaces[li];
                try
                {
                    var share = Poly.Boolean(CellTrace.Rings(mask, w, h, rlo, cell), null, BoolOp.Union, FillRule.NonZero)
                        .Select(r => new Region(Poly.Simplify(r.Outer, 0.45f), r.Holes.Select(x => Poly.Simplify(x, 0.45f)))).Where(r => r.Outer.Length >= 3).ToList();
                    var (slo, shi) = Bounds(share.SelectMany(r => r.Outer).ToArray(), 1f);
                    var others = surfaces.Where(s => s.E != lawn.E && s.Hi.x >= slo.x && s.Lo.x <= shi.x && s.Hi.y >= slo.y && s.Lo.y <= shi.y).Select(s => new Region(Poly.IsCcw(s.Ring) ? s.Ring : s.Ring.Reverse().ToArray())).ToList();
                    var ext = others.Count > 0 ? Poly.Boolean(share, others, BoolOp.Difference) : share;
                    var lawnRegion = new Region(Poly.IsCcw(lawn.Ring) ? lawn.Ring : lawn.Ring.Reverse().ToArray());
                    var merged = Poly.Boolean(new[] { lawnRegion }, ext, BoolOp.Union);
                    if (merged.Count == 0) { failed++; continue; }
                    var pieces = new List<float2[]>();
                    foreach (var region in merged)
                        foreach (var ring in region.HasHoles ? Poly.CutHoles(region, 0f) : new List<float2[]> { region.Outer })
                            pieces.AddRange(AreaRings.PrepareSurface(ring, AreaRings.SurfaceMinNodeDistance));
                    pieces = pieces.Where(p => p.Length >= 3 && Poly.Area(p) >= 4f).OrderByDescending(p => Poly.Area(p)).ToList();
                    if (pieces.Count == 0) { failed++; continue; }
                    // the piece holding the old lawn replaces it; the rest become free-standing pieces of the same surface
                    var lawnC = Poly.Centroid(lawn.Ring);
                    int mainIdx = pieces.FindIndex(p => Poly.Contains(p, lawnC));
                    if (mainIdx < 0) mainIdx = 0;
                    var plan = new YardPlan { Building = lots[k].E, Lawn = lawn.E, LawnPrefab = lawn.Prefab, LawnName = lawn.Name, Before = lawn.Ring, Main = pieces[mainIdx] };
                    for (int i = 0; i < pieces.Count; i++) if (i != mainIdx) plan.Extra.Add(pieces[i]);
                    plan.AddedM2 = pieces.Sum(p => Poly.Area(p)) - Poly.Area(lawn.Ring);
                    var biggest = ext.OrderByDescending(r => r.Area).FirstOrDefault();
                    plan.Focus = biggest != null ? Poly.Centroid(biggest.Outer) : lawnC;
                    if (plan.AddedM2 > 2f) _yardPlans.Add(plan);
                }
                catch (Exception e)
                {
                    failed++;
                    if (failed <= 3) Mod.Log.Warn("Placemaker lab: yard of " + lots[k].E + " failed: " + e.Message);
                }
            }
            float m2 = cell * cell;
            rec["houses"] = lots.Count;
            rec["housesWithLawn"] = lawnOf.Count(kv => lots.Any(l => l.E == kv.Key));
            rec["stretched"] = _yardPlans.Count;
            rec["noLawn"] = noLawn;
            rec["tooSmall"] = tooSmall;
            rec["failed"] = failed;
            rec["bareM2"] = bare * m2;
            rec["bareBeyondReachM2"] = farCells * m2;
            rec["coveredM2"] = coveredCells * m2;
            rec["addedLawnM2"] = Math.Round(_yardPlans.Sum(p => p.AddedM2));
            rec["extraPieces"] = _yardPlans.Sum(p => p.Extra.Count);
            Mod.Log.Info($"Placemaker lab: yards: {lots.Count} houses, {_yardPlans.Count} lawns to stretch (+{_yardPlans.Sum(p => p.AddedM2):F0} mÂ², {_yardPlans.Sum(p => p.Extra.Count)} extra pieces), "
                + $"{noLawn} without a lawn, {tooSmall} with too little bare land, {failed} failed; bare land {bare * m2:F0} mÂ² ({farCells * m2:F0} mÂ² beyond {reach:F0} m of any lot); owned surfaces: {string.Join(", ", ownedNames)}");

            // the spots that change most, for before/after close-ups
            var focus = new List<float2>();
            foreach (var p in _yardPlans.OrderByDescending(p => p.AddedM2))
            {
                if (focus.Count >= closeups) break;
                if (focus.All(f => math.distance(f, p.Focus) > 40f)) focus.Add(p.Focus);
            }
            rec["closeupsAt"] = new JArray(focus.Select(f => new JArray(Math.Round(f.x, 1), Math.Round(f.y, 1))));
            JObject Cam(float2 f) => new JObject { ["target"] = "point", ["at"] = new JArray(f.x, f.y), ["abs"] = true, ["view"] = "oblique", ["pitch"] = 42, ["distance"] = 70 };
            var shots = new JArray();
            for (int i = 0; i < focus.Count; i++)
            {
                var sr = new JObject();
                yield return StepShot(st, new JObject { ["camera"] = Cam(focus[i]), ["shotMode"] = "render" }, sr, "yards-before-" + (i + 1));
                shots.Add(sr);
            }
            if (!apply) { rec["closeupShots"] = shots; yield break; }

            int written = 0, cached = 0;
            yield return ApplyWithFixtureTool(t =>
            {
                foreach (var p in _yardPlans)
                {
                    if (!em.Exists(p.Lawn)) continue;
                    var buf = em.GetBuffer<global::Game.Areas.Node>(p.Lawn);
                    buf.Clear();
                    foreach (var q in p.Main) buf.Add(new global::Game.Areas.Node(new float3(q.x, _fixture.H(q.x, q.y), q.y), float.MinValue));
                    if (em.HasBuffer<LocalNodeCache>(p.Lawn) && em.HasComponent<global::Game.Objects.Transform>(p.Building))
                    {
                        var inv = global::Game.Objects.ObjectUtils.InverseTransform(em.GetComponentData<global::Game.Objects.Transform>(p.Building));
                        var cache = em.GetBuffer<LocalNodeCache>(p.Lawn);
                        cache.Clear();
                        foreach (var q in p.Main) cache.Add(new LocalNodeCache { m_Position = global::Game.Objects.ObjectUtils.WorldToLocal(inv, new float3(q.x, _fixture.H(q.x, q.y), q.y)), m_ParentMesh = -1 });
                        cached++;
                    }
                    em.AddComponent<Updated>(p.Lawn);
                    written++;
                    foreach (var x in p.Extra)
                    {
                        var def = t.NewDefinition();
                        em.AddComponentData(def, new CreationDefinition { m_Prefab = p.LawnPrefab });
                        em.AddComponent<Updated>(def);
                        var nodes = em.AddBuffer<global::Game.Areas.Node>(def);
                        foreach (var q in x) nodes.Add(new global::Game.Areas.Node(new float3(q.x, _fixture.H(q.x, q.y), q.y), float.MinValue));
                        nodes.Add(nodes[0]);
                    }
                }
                return written;
            }, "lab yards");
            yield return Frames(40);
            int vanished = 0;
            foreach (var p in _yardPlans)
                if (em.Exists(p.Lawn) && em.HasComponent<Area>(p.Lawn) && (em.GetComponentData<Area>(p.Lawn).m_Flags & AreaFlags.NoTriangles) != 0) vanished++;
            rec["lawnsWritten"] = written;
            rec["lawnsWithLocalCache"] = cached;
            rec["lawnsNotDrawn"] = vanished;
            Event("build");
            for (int i = 0; i < focus.Count; i++)
            {
                var sr = new JObject();
                yield return StepShot(st, new JObject { ["camera"] = Cam(focus[i]), ["shotMode"] = "render" }, sr, "yards-after-" + (i + 1));
                shots.Add(sr);
            }
            rec["closeupShots"] = shots;
            Mod.Log.Info($"Placemaker lab: yards applied: {written} lawns reshaped ({cached} with a local node cache), {vanished} not drawn (no triangles)");
        }

        /// <summary>How many stretched lawns still have the shape the lab gave them (a level-up or rebuild resets them).</summary>
        private void LabYardsCheck(JObject rec)
        {
            var em = _world.EntityManager;
            int kept = 0, reset = 0, gone = 0;
            foreach (var p in _yardPlans)
            {
                if (!em.Exists(p.Lawn) || em.HasComponent<Deleted>(p.Lawn)) { gone++; continue; }
                var n = em.GetBuffer<global::Game.Areas.Node>(p.Lawn, true);
                if (n.Length == p.Main.Length && math.distance(n[0].m_Position.xz, p.Main[0]) < 0.05f) kept++; else reset++;
            }
            rec["kept"] = kept;
            rec["reset"] = reset;
            rec["gone"] = gone;
            Mod.Log.Info($"Placemaker lab: yards check: {kept} stretched lawns kept, {reset} reset, {gone} gone (house rebuilt or levelled up)");
        }

        private static float DistanceToRing(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                float2 a = ring[i], b = ring[(i + 1) % ring.Length], ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
                best = math.min(best, math.distance(p, a + ab * t));
            }
            return best;
        }
    }
}
