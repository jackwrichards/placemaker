// Game runner lab: how well outgrown gardens fit (Outgrow, Game/Neighbours). Measures, per polished house in the land,
// what the user saw going wrong, so a fix is compared in numbers as well as pictures:
//   * a garden over a road, path or track surface (round ends, turning circles and roundabouts, counted apart);
//   * a garden edge with no fence or hedge along it (its street front left out), and fences that stand there but the game
//     hides (Overridden: drawn only when hovered);
//   * a garden whose lawn or paving stops short of its fence (bare land inside the garden past the lot; a painted garden,
//     with no surface of its own, is left out);
//   * a garden, or any of Outgrow's pieces, under a deck overhead (an elevated road, path or track: an overpass) too low to
//     grow under (lower than the look's "deckClearance": a crack), and any of its trees under or beside any deck.
//   {"do":"lab","op":"outgrowCheck","closeups":6}
// The record has the totals, the worst houses and a close-up of the worst spots (shot names outgrow-<kind>-<n>).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
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
        private sealed class OutgrowFence
        {
            public float2[] Pts;
            public bool Hidden;
            public float2 Lo, Hi;
        }

        // the gardens at the last outgrowCheck: the next one says how many changed since (a change that must leave them as they are)
        private static Dictionary<Entity, float2[]> s_checkedGardens;

        private IEnumerator LabOutgrowCheck(ScenarioState st, JObject step, JObject rec)
        {
            yield return Frames(30);
            var em = _world.EntityManager;
            var sys = _world.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (sys == null) { rec["error"] = "lab: Outgrow is not registered"; yield break; }
            {
                int changed = 0, same = 0;
                var now = new Dictionary<Entity, float2[]>();
                foreach (var kv in sys.Gardens) if (kv.Value != null && kv.Value.Length >= 3 && em.Exists(kv.Key)) now[kv.Key] = kv.Value;
                if (s_checkedGardens != null)
                {
                    foreach (var kv in now)
                    {
                        if (!s_checkedGardens.TryGetValue(kv.Key, out var was) || was.Length != kv.Value.Length) { changed++; continue; }
                        bool moved = false;
                        for (int i = 0; i < was.Length && !moved; i++) moved = math.distance(was[i], kv.Value[i]) > 0.1f;
                        if (moved) changed++; else same++;
                    }
                    foreach (var k in s_checkedGardens.Keys) if (!now.ContainsKey(k) && em.Exists(k)) changed++;
                    rec["gardensChangedSinceLastCheck"] = changed;
                    rec["gardensSameSinceLastCheck"] = same;
                    Mod.Log.Info($"Placemaker lab: outgrow check: since the last check {changed} garden(s) changed, {same} the same");
                }
                s_checkedGardens = now;
                rec["coreAcrossRoad"] = NeighbourPolishSystem.CoreAcrossRoad;
                rec["coreNotTouching"] = NeighbourPolishSystem.CoreNotTouching;
                rec["coreEdited"] = NeighbourPolishSystem.CoreEdited;
                rec["levelUpChecks"] = string.Join(" ", NeighbourPolishSystem.LevelUpChecks);
                rec["sameLandPlans"] = NeighbourPolishSystem.SameLandPlans;
                rec["passes"] = NeighbourPolishSystem.TotalPasses;
            }
            var prefabs = _world.GetOrCreateSystemManaged<PrefabSystem>();
            var names = new Dictionary<Entity, string>();
            string NameOf(Entity p)
            {
                if (names.TryGetValue(p, out var n)) return n;
                try { n = prefabs.GetPrefab<PrefabBase>(p)?.name ?? ""; } catch (Exception) { n = ""; }
                return names[p] = n;
            }
            var b = st.S.Block;
            var lo = new float2(b.X0, b.Z0) - 60f;
            var hi = new float2(b.X1, b.Z1) + 60f;
            if ((string)step["box"] == "gardens")
            {
                // every house Outgrow gave a garden (a loaded city), as fenceMap's "box": "gardens"
                lo = new float2(float.MaxValue);
                hi = new float2(float.MinValue);
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PolishGarden>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                    {
                        if (!sys.IsYes(e)) continue;
                        var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                        lo = math.min(lo, p - 150f);
                        hi = math.max(hi, p + 150f);
                    }
                if (lo.x > hi.x) { rec["error"] = "lab: no Outgrow gardens in the city"; yield break; }
            }
            bool InBox(float2 a, float2 c) => c.x >= lo.x && c.y >= lo.y && a.x <= hi.x && a.y <= hi.y;

            // road, path and track surfaces (exact: the round ends too)
            var roads = new List<(float2[] Ring, bool Round, float2 Lo, float2 Hi)>();
            // and the decks overhead (elevated roads, paths and tracks; a tunnel is no deck) in slices, each with its height
            // above the ground: lower than the room Outgrow leaves under a deck it is a crack
            var decks = new List<(float2[] Ring, float2 Lo, float2 Hi, float Height)>();
            var deckPieces = new List<(float2[] Ring, float Height)>();
            float room = NeighbourPolishSystem.Settings.Knob("deckClearance", Placemaker.Core.Neighbours.PolishLooks.Defaults["deckClearance"]);
            var terrainHeights = _world.GetOrCreateSystemManaged<global::Game.Simulation.TerrainSystem>().GetHeightData(true);
            var pieces = new List<(float2[] Ring, bool Round)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<EdgeGeometry>(), ComponentType.ReadOnly<Curve>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var cb = MathUtils.Bounds(em.GetComponentData<Curve>(e).m_Bezier);
                    if (!InBox(cb.min.xz - 30f, cb.max.xz + 30f)) continue;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    bool ground = em.HasComponent<Road>(e) || em.HasComponent<PathwayData>(prefab) || em.HasComponent<TrainTrack>(e) || em.HasComponent<TramTrack>(e);
                    if (!ground) continue;
                    // as Outgrow's barriers: a building's own (invisible) paths and anything under 1 m wide or off the ground are no road
                    if (em.HasComponent<Owner>(e) || !em.HasComponent<Composition>(e)) continue;
                    var comp = em.GetComponentData<Composition>(e).m_Edge;
                    if (comp == Entity.Null || !em.HasComponent<NetCompositionData>(comp)) continue;
                    var cd = em.GetComponentData<NetCompositionData>(comp);
                    if ((cd.m_Flags.m_General & CompositionFlags.General.Tunnel) != 0 || cd.m_Width < 1f) continue;
                    if ((cd.m_Flags.m_General & CompositionFlags.General.Elevated) != 0)
                    {
                        deckPieces.Clear();
                        RoadSurfaces.DeckPieces(em, e, p => global::Game.Simulation.TerrainUtils.SampleHeight(ref terrainHeights, p), deckPieces);
                        foreach (var (ring, h) in deckPieces)
                        {
                            Poly.Bounds(ring, out var a, out var c);
                            decks.Add((ring, a, c, h));
                        }
                        continue;
                    }
                    pieces.Clear();
                    RoadSurfaces.Pieces(em, e, pieces);
                    foreach (var (ring, round) in pieces)
                    {
                        Poly.Bounds(ring, out var a, out var c);
                        roads.Add((ring, round, a, c));
                    }
                }

            // every surface (lawns, paving, beds, paths: the house's own and Outgrow's)
            var surfaces = new List<(float2[] Ring, float2 Lo, float2 Hi)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Areas.Surface>(), ComponentType.ReadOnly<global::Game.Areas.Node>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var nodes = em.GetBuffer<global::Game.Areas.Node>(e, true);
                    if (nodes.Length < 3) continue;
                    var ring = new float2[nodes.Length];
                    for (int i = 0; i < ring.Length; i++) ring[i] = nodes[i].m_Position.xz;
                    Poly.Bounds(ring, out var a, out var c);
                    if (InBox(a, c)) surfaces.Add((ring, a, c));
                }

            // fences and hedges: utility lanes and fence/hedge pieces, drawn or hidden by the game
            var fences = new List<OutgrowFence>();
            int lanes = 0, lanesHidden = 0, piecesAll = 0, piecesHidden = 0, ourPieces = 0, ourPiecesHidden = 0;
            var hiddenAt = new List<float2>();
            var whyHidden = new List<string>();
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
                    bool hidden = em.HasComponent<Overridden>(e);
                    lanes++;
                    if (hidden) { lanesHidden++; hiddenAt.Add(MathUtils.Position(curve.m_Bezier, 0.5f).xz); }
                    var pts = new float2[9];
                    for (int i = 0; i < pts.Length; i++) pts[i] = MathUtils.Position(curve.m_Bezier, i / 8f).xz;
                    fences.Add(new OutgrowFence { Pts = pts, Hidden = hidden, Lo = cb.min.xz, Hi = cb.max.xz });
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
                    var a = global::Game.Objects.ObjectUtils.LocalToWorld(t, mid - axis * half).xz;
                    var c = global::Game.Objects.ObjectUtils.LocalToWorld(t, mid + axis * half).xz;
                    bool hidden = em.HasComponent<Overridden>(e);
                    bool ours = em.HasComponent<PolishPiece>(e);
                    piecesAll++;
                    if (hidden) { piecesHidden++; hiddenAt.Add(t.m_Position.xz); }
                    if (ours) { ourPieces++; if (hidden) { ourPiecesHidden++; if (whyHidden.Count < 40) whyHidden.Add(sys.WhyHidden(e)); } }
                    fences.Add(new OutgrowFence { Pts = new[] { a, c }, Hidden = hidden, Lo = math.min(a, c), Hi = math.max(a, c) });
                }

            // Outgrow's pieces (bushes, trees, rocks, props, fence pieces) standing in a crack (under a deck too low to grow
            // under), under a high deck (allowed), and its trees under or within 3 m of any deck (never allowed)
            int piecesUnderDecks = 0, piecesUnderHighDecks = 0, treesByDecks = 0;
            var underDeckSpots = new List<(float2 At, float Area)>();
            var underDeckNames = new Dictionary<string, int>();
            var underHighNames = new Dictionary<string, int>();
            bool Near((float2[] Ring, float2 Lo, float2 Hi, float Height) d, float2 p, float r) =>
                p.x >= d.Lo.x - r && p.y >= d.Lo.y - r && p.x <= d.Hi.x + r && p.y <= d.Hi.y + r && (Poly.Contains(d.Ring, p) || (r > 0f && DistanceToRing(d.Ring, p) < r));
            if (decks.Count > 0)
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PolishPiece>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                    {
                        var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz;
                        if (!InBox(p, p)) continue;
                        var n = NameOf(em.GetComponentData<PrefabRef>(e).m_Prefab);
                        if (n.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0 && decks.Any(d => Near(d, p, 3f)))
                        {
                            treesByDecks++;
                            if (underDeckSpots.Count < 200) underDeckSpots.Add((p, 2f));
                        }
                        if (decks.Any(d => d.Height < room && Near(d, p, 0f)))
                        {
                            piecesUnderDecks++;
                            underDeckNames[n] = (underDeckNames.TryGetValue(n, out var c) ? c : 0) + 1;
                            if (underDeckSpots.Count < 200) underDeckSpots.Add((p, 1f));
                        }
                        else if (decks.Any(d => Near(d, p, 0f)))
                        {
                            piecesUnderHighDecks++;
                            underHighNames[n] = (underHighNames.TryGetValue(n, out var c) ? c : 0) + 1;
                        }
                    }

            // (by 8 m cell: a city has tens of thousands of fences and every garden edge is sampled every half metre)
            var fenceCells = new Dictionary<long, List<int>>();
            long FenceCell(int x, int y) => ((long)x << 32) ^ (uint)y;
            for (int i = 0; i < fences.Count; i++)
            {
                var f = fences[i];
                for (int x = (int)math.floor((f.Lo.x - 1f) / 8f); x <= (int)math.floor((f.Hi.x + 1f) / 8f); x++)
                    for (int y = (int)math.floor((f.Lo.y - 1f) / 8f); y <= (int)math.floor((f.Hi.y + 1f) / 8f); y++)
                    {
                        if (!fenceCells.TryGetValue(FenceCell(x, y), out var list)) fenceCells[FenceCell(x, y)] = list = new List<int>();
                        list.Add(i);
                    }
            }
            float FenceDistance(float2 p, bool hidden)
            {
                float best = float.MaxValue;
                if (!fenceCells.TryGetValue(FenceCell((int)math.floor(p.x / 8f), (int)math.floor(p.y / 8f)), out var inCell)) return best;
                foreach (int fi in inCell)
                {
                    var f = fences[fi];
                    if (f.Hidden != hidden || p.x < f.Lo.x - 1f || p.y < f.Lo.y - 1f || p.x > f.Hi.x + 1f || p.y > f.Hi.y + 1f) continue;
                    for (int i = 0; i + 1 < f.Pts.Length; i++) best = math.min(best, SegmentDistance(p, f.Pts[i], f.Pts[i + 1]));
                }
                return best;
            }

            // each polished house's garden
            var gardens = sys.Gardens.Where(kv => em.Exists(kv.Key) && !em.HasComponent<Deleted>(kv.Key) && sys.IsYes(kv.Key) && kv.Value != null && kv.Value.Length >= 3).ToList();
            bool city = (string)step["box"] == "gardens";
            if (city)
            {
                // a loaded city: every garden saved with its house (the ones this session wrote are saved with it too)
                gardens.Clear();
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PolishGarden>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                using (var all = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in all)
                    {
                        if (!sys.IsYes(e)) continue;
                        var buf = em.GetBuffer<PolishGarden>(e, true);
                        if (buf.Length < 3) continue;
                        var ring = new float2[buf.Length];
                        for (int i = 0; i < ring.Length; i++) ring[i] = buf[i].Position;
                        gardens.Add(new KeyValuePair<Entity, float2[]>(e, ring));
                    }
            }
            var houses = new JArray();
            float overRoad = 0f, overRound = 0f, open = 0f, hiddenFence = 0f, edge = 0f, bare = 0f, covered = 0f, landPastLot = 0f, underDeck = 0f, underHigh = 0f;
            int overCount = 0, roundCount = 0, misfits = 0, painted = 0, considered = 0, deckCount = 0, highCount = 0;
            var roadSpots = new List<(float2 At, float Area, bool Round)>();
            var gapSpots = new List<(float2 At, float Len)>();
            var bareSpots = new List<(float2 At, float Area)>();
            int looked = 0;
            foreach (var (house, garden) in gardens)
            {
                // (a city's thousands of gardens: a frame now and then, the game must not look hung)
                if (++looked % 150 == 0) yield return null;
                if (!em.Exists(house) || em.HasComponent<Deleted>(house)) continue;
                var t = em.GetComponentData<global::Game.Objects.Transform>(house);
                if (!city && (t.m_Position.x < b.X0 || t.m_Position.x > b.X1 || t.m_Position.z < b.Z0 || t.m_Position.z > b.Z1)) continue;
                var prefab = em.GetComponentData<PrefabRef>(house).m_Prefab;
                if (!em.HasComponent<BuildingData>(prefab)) continue;
                considered++;
                var halfLot = (float2)em.GetComponentData<BuildingData>(prefab).m_LotSize * 4f;
                var fwd = math.normalizesafe(math.mul(t.m_Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                var right = new float2(fwd.y, -fwd.x);
                float2 Local(float2 w) { var d = w - t.m_Position.xz; return new float2(math.dot(d, right), math.dot(d, fwd)); }
                bool InLot(float2 w) { var l = Local(w); return math.abs(l.x) <= halfLot.x && math.abs(l.y) <= halfLot.y; }
                var ring = Poly.EnsureCcw(garden.ToArray());
                Poly.Bounds(ring, out var glo, out var ghi);

                // over roads
                float hr = 0f, hrRound = 0f;
                foreach (var r in roads)
                {
                    if (r.Hi.x < glo.x || r.Lo.x > ghi.x || r.Hi.y < glo.y || r.Lo.y > ghi.y) continue;
                    var inter = Poly.Boolean(new[] { (IReadOnlyList<float2>)ring }, new[] { (IReadOnlyList<float2>)r.Ring }, BoolOp.Intersection, FillRule.NonZero);
                    float area = inter.Sum(x => x.Area);
                    if (area < 0.5f) continue;
                    hr += area;
                    if (r.Round) hrRound += area;
                    roadSpots.Add((Poly.Centroid(inter.OrderByDescending(x => x.Area).First().Outer), area, r.Round));
                }
                if (hr > 0f) { overCount++; overRoad += hr; }
                if (hrRound > 0f) { roundCount++; overRound += hrRound; }

                // under decks: in a crack (too low), under a high one (allowed)
                float hd = 0f, hh = 0f;
                foreach (var d in decks)
                {
                    if (d.Hi.x < glo.x || d.Lo.x > ghi.x || d.Hi.y < glo.y || d.Lo.y > ghi.y) continue;
                    var inter = Poly.Boolean(new[] { (IReadOnlyList<float2>)ring }, new[] { (IReadOnlyList<float2>)d.Ring }, BoolOp.Intersection, FillRule.NonZero);
                    float area = inter.Sum(x => x.Area);
                    if (area < 0.5f) continue;
                    if (d.Height >= room) { hh += area; continue; }
                    hd += area;
                    underDeckSpots.Insert(0, (Poly.Centroid(inter.OrderByDescending(x => x.Area).First().Outer), area));
                }
                if (hd > 0f) { deckCount++; underDeck += hd; }
                if (hh > 0f) { highCount++; underHigh += hh; }

                // fence along every garden edge but its street front
                float hOpen = 0f, hHidden = 0f, hEdge = 0f;
                for (int k = 0; k < ring.Length; k++)
                {
                    var a = ring[k];
                    var c = ring[(k + 1) % ring.Length];
                    float len = math.distance(a, c);
                    int steps = math.max(1, (int)math.ceil(len / 0.5f));
                    float run = 0f;
                    float2 runStart = a;
                    for (int s = 0; s <= steps; s++)
                    {
                        var p = math.lerp(a, c, s / (float)steps);
                        bool front = Local(p).y >= halfLot.y - 1.2f;
                        bool isOpen = false;
                        if (!front && s < steps)
                        {
                            float piece = len / steps;
                            hEdge += piece;
                            if (FenceDistance(p, false) >= 0.9f)
                            {
                                if (FenceDistance(p, true) < 0.9f) hHidden += piece;
                                else { hOpen += piece; isOpen = true; }
                            }
                        }
                        if (isOpen) { if (run == 0f) runStart = p; run += len / steps; }
                        else if (run > 0f) { if (run >= 2f) gapSpots.Add(((runStart + p) * 0.5f, run)); run = 0f; }
                    }
                    if (run >= 2f) gapSpots.Add(((runStart + c) * 0.5f, run));
                }
                open += hOpen; hiddenFence += hHidden; edge += hEdge;

                // lawn or paving up to the fence: the garden past the lot, on a 1 m grid
                int land = 0, cov = 0, bareCells = 0;
                var bareSum = float2.zero;
                var near = surfaces.Where(sf => sf.Hi.x >= glo.x && sf.Lo.x <= ghi.x && sf.Hi.y >= glo.y && sf.Lo.y <= ghi.y).ToList();
                var nearRoads = roads.Where(r => r.Hi.x >= glo.x && r.Lo.x <= ghi.x && r.Hi.y >= glo.y && r.Lo.y <= ghi.y).ToList();
                for (float x = math.floor(glo.x) + 0.5f; x < ghi.x; x += 1f)
                    for (float z = math.floor(glo.y) + 0.5f; z < ghi.y; z += 1f)
                    {
                        var p = new float2(x, z);
                        if (!Poly.Contains(ring, p) || InLot(p)) continue;
                        if (nearRoads.Any(r => Poly.Contains(r.Ring, p))) continue;
                        land++;
                        if (near.Any(sf => p.x >= sf.Lo.x && p.x <= sf.Hi.x && p.y >= sf.Lo.y && p.y <= sf.Hi.y && Poly.Contains(sf.Ring, p))) cov++;
                        else { bareCells++; bareSum += p; }
                    }
                landPastLot += land;
                bool isPainted = cov < 5;
                if (isPainted) painted++;
                else
                {
                    bare += bareCells;
                    covered += cov;
                    if (bareCells >= 10) { misfits++; bareSpots.Add((bareSum / bareCells, bareCells)); }
                }
                if (hr > 0f || hd > 0f || hOpen >= 2f || hHidden >= 2f || (!isPainted && bareCells >= 10))
                    houses.Add(new JObject
                    {
                        ["house"] = $"{house.Index}:{house.Version}", ["at"] = new JArray(Math.Round(t.m_Position.x, 1), Math.Round(t.m_Position.z, 1)),
                        ["overRoadM2"] = Math.Round(hr, 1), ["overRoundM2"] = Math.Round(hrRound, 1), ["underDeckM2"] = Math.Round(hd, 1), ["underHighDeckM2"] = Math.Round(hh, 1), ["openFenceM"] = Math.Round(hOpen, 1), ["hiddenFenceM"] = Math.Round(hHidden, 1),
                        ["landPastLotM2"] = land, ["coveredM2"] = cov, ["bareM2"] = isPainted ? 0 : bareCells, ["painted"] = isPainted,
                    });
            }
            rec["houses"] = considered;
            rec["gardensOverRoads"] = overCount;
            rec["overRoadM2"] = Math.Round(overRoad, 1);
            rec["gardensOverRoundEnds"] = roundCount;
            rec["overRoundEndM2"] = Math.Round(overRound, 1);
            rec["decks"] = decks.Count;
            rec["lowDecks"] = decks.Count(d => d.Height < room);
            rec["deckClearance"] = room;
            rec["deckHeights"] = decks.Count > 0 ? $"{decks.Min(d => d.Height):F1}-{decks.Max(d => d.Height):F1} m" : "";
            rec["gardensUnderDecks"] = deckCount;
            rec["underDeckM2"] = Math.Round(underDeck, 1);
            rec["outgrowPiecesUnderDecks"] = piecesUnderDecks;
            rec["outgrowPiecesUnderDecksByName"] = new JObject(underDeckNames.OrderByDescending(kv => kv.Value).Select(kv => new JProperty(kv.Key, kv.Value)));
            rec["gardensUnderHighDecks"] = highCount;
            rec["underHighDeckM2"] = Math.Round(underHigh, 1);
            rec["outgrowPiecesUnderHighDecks"] = piecesUnderHighDecks;
            rec["outgrowPiecesUnderHighDecksByName"] = new JObject(underHighNames.OrderByDescending(kv => kv.Value).Select(kv => new JProperty(kv.Key, kv.Value)));
            rec["outgrowTreesByDecks"] = treesByDecks;
            rec["fenceEdgeM"] = Math.Round(edge, 0);
            rec["openFenceM"] = Math.Round(open, 1);
            rec["hiddenFenceM"] = Math.Round(hiddenFence, 1);
            rec["fenceLanes"] = lanes;
            rec["fenceLanesHidden"] = lanesHidden;
            rec["fencePieces"] = piecesAll;
            rec["fencePiecesHidden"] = piecesHidden;
            rec["outgrowPieces"] = ourPieces;
            rec["outgrowPiecesHidden"] = ourPiecesHidden;
            rec["paintedGardens"] = painted;
            rec["gardensShortOfFence"] = misfits;
            rec["bareInsideFenceM2"] = Math.Round(bare, 0);
            rec["coveredPastLotM2"] = Math.Round(covered, 0);
            rec["landPastLotM2"] = Math.Round(landPastLot, 0);
            rec["worst"] = new JArray(houses.OrderByDescending(h => ((double)h["overRoadM2"] + (double)h["underDeckM2"]) * 4 + (double)h["openFenceM"] + (double)h["hiddenFenceM"] + (int)h["bareM2"] * 0.2).Take(12));
            Mod.Log.Info($"Placemaker lab: outgrow check: {considered} gardens; {overCount} over roads ({overRoad:F1} m², {roundCount} over round ends {overRound:F1} m²); "
                + $"fence {edge:F0} m of edges, {open:F1} m open, {hiddenFence:F1} m hidden by the game; fence lanes {lanes} ({lanesHidden} hidden), fence pieces {piecesAll} ({piecesHidden} hidden; Outgrow's {ourPieces}, {ourPiecesHidden} hidden); "
                + $"{painted} painted gardens, {misfits} with lawn or paving short of the fence ({bare:F0} m² bare inside, {covered:F0} m² covered past the lot)");
            if (decks.Count > 0)
                Mod.Log.Info($"Placemaker lab: outgrow check: {decks.Count} deck pieces overhead ({decks.Min(d => d.Height):F1}-{decks.Max(d => d.Height):F1} m up; {decks.Count(d => d.Height < room)} lower than {room:F1} m: cracks); "
                    + $"in cracks: {deckCount} gardens ({underDeck:F1} m²), {piecesUnderDecks} Outgrow pieces"
                    + (underDeckNames.Count > 0 ? " (" + string.Join(", ", underDeckNames.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} x{kv.Value}")) + ")" : "")
                    + $"; under high decks: {highCount} gardens ({underHigh:F1} m²), {piecesUnderHighDecks} Outgrow pieces"
                    + (underHighNames.Count > 0 ? " (" + string.Join(", ", underHighNames.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} x{kv.Value}")) + ")" : "")
                    + $"; {treesByDecks} Outgrow trees under or within 3 m of a deck");
            foreach (var h in rec["worst"]) Mod.Log.Info("Placemaker lab: outgrow check: " + h.ToString(Newtonsoft.Json.Formatting.None));
            foreach (var w in whyHidden) Mod.Log.Info("Placemaker lab: outgrow check: hidden " + w);
            rec["whyHidden"] = new JArray(whyHidden);

            // close-ups of the worst spots
            int closeups = (int?)step["closeups"] ?? 6;
            var focus = new List<(string Kind, float2 At)>();
            void Pick(string kind, IEnumerable<float2> spots, int max)
            {
                int n = 0;
                foreach (var p in spots)
                {
                    if (n >= max || focus.Count >= closeups) break;
                    if (focus.Any(f => math.distance(f.At, p) < 25f)) continue;
                    focus.Add((kind, p));
                    n++;
                }
            }
            // "hiddenFirst": close-ups of hidden fences before anything else
            if ((bool?)step["hiddenFirst"] == true) Pick("hidden", hiddenAt, 3);
            Pick("deck", underDeckSpots.OrderByDescending(x => x.Area).Select(x => x.At), 2);
            Pick("road", roadSpots.OrderByDescending(r => r.Round ? 1 : 0).ThenByDescending(r => r.Area).Select(r => r.At), 2);
            Pick("gap", gapSpots.OrderByDescending(g => g.Len).Select(g => g.At), 2);
            Pick("hidden", hiddenAt, 1);
            Pick("bare", bareSpots.OrderByDescending(x => x.Area).Select(x => x.At), 2);
            var shots = new JArray();
            for (int i = 0; i < focus.Count; i++)
            {
                var sr = new JObject { ["at"] = new JArray(Math.Round(focus[i].At.x, 1), Math.Round(focus[i].At.y, 1)) };
                var cam = new JObject { ["target"] = "point", ["at"] = new JArray(focus[i].At.x, focus[i].At.y), ["abs"] = true, ["view"] = "oblique", ["pitch"] = 60, ["distance"] = 65 };
                yield return StepShot(st, new JObject { ["camera"] = cam, ["shotMode"] = "render" }, sr, $"outgrow-{focus[i].Kind}-{i + 1}");
                shots.Add(sr);
            }
            rec["closeupShots"] = shots;
        }

        private static float SegmentDistance(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
            return math.distance(p, a + ab * t);
        }
    }
}
