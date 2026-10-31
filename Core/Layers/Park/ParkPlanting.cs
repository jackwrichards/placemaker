// Park furnishing: vegetation, furniture, fence and animal spawners.
//
// Ported from ParkManager (tomkpunkt, GPL-3.0), Geometry/ParkDecorationPlanner.cs:
// groves (wobbly ellipses, single species), a boundary belt with noise-driven open views,
// solitaires, path-side benches/lamps, bins at gates/junctions/benches, fence runs with gate gaps,
// and animal spawners in the largest groves.
// Merged with ParkingLotTool (KruemelGames, GPL-3.0), Geometry/ParkingVegetation.cs: solitaires
// come from a permuted jittered grid with value-noise clustering and an edge ramp instead of
// uniform random throws, which reads as natural clumps on open lawns.
// Changes: every placement goes through Occupancy (input.Reserved, site obstacles, own paths),
// grid-indexed neighbour checks, a protected clearing on the largest lawn so add-ons find room,
// formal style (avenues + hedges), counts from "per hectare" / "per 100 m" controls.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    public enum ParkItemKind { Tree = 0, Bush = 1, Bench = 2, Lamp = 3, Bin = 4, Spawner = 5 }

    public struct ParkItem
    {
        public ParkItemKind Kind;
        public float2 Position;
        public float Yaw;
        public float Radius;
        public uint Variant;
        public int Age;
    }

    /// <summary>Shared state of one park planning run.</summary>
    public sealed class ParkSite
    {
        public LayerRegion LayerRegion;
        public Occupancy Occ;
        public List<Gate> Gates = new List<Gate>();
        public ParkPathGraph Graph = ParkPathGraph.Empty();
        public List<PathCurve> Curves = new List<PathCurve>();
        public float2[] Lake = Array.Empty<float2>();
        public float2 ClearingCenter;
        public float ClearingRadius;
        /// <summary>(makeover) The shrub drifts: how many, and the shrubs wanted and planted (plan facts).</summary>
        public int BushDrifts, BushesWanted, BushesPlanted;
        public readonly List<ParkItem> Items = new List<ParkItem>();
        public readonly List<Grove> Groves = new List<Grove>();
        public uint Seed;
        public CancellationToken Cancel;
        /// <summary>Reusable query buffer (one planning run = one thread).</summary>
        public readonly List<int> Scratch = new List<int>();
        /// <summary>(perf) Quick planting checks (PlantGrid.cs), made by the first natural planting pass.</summary>
        internal PlantGrid PlantGrid;

        public int Count(ParkItemKind k) { int n = 0; foreach (var i in Items) if (i.Kind == k) n++; return n; }

        public bool InLake(float2 p, float radius) =>
            Lake.Length >= 3 && (Geometry.Poly.Contains(Lake, p) || MinDistToRing(Lake, p) < radius);

        public bool InClearing(float2 p, float radius) =>
            ClearingRadius > 0f && math.distance(p, ClearingCenter) < ClearingRadius + radius;

        public float GateDistanceSq(float2 p)
        {
            float best = float.MaxValue;
            foreach (var g in Gates) best = math.min(best, math.distancesq(p, g.Position));
            return best;
        }

        public void Add(ParkItem item)
        {
            Items.Add(item);
            Occ.Take(item.Position, item.Radius, (int)item.Kind);
            if (PlantGrid != null && PlantGrid.Ready && (item.Kind == ParkItemKind.Tree || item.Kind == ParkItemKind.Bush))
                PlantGrid.Added(item.Position, item.Kind == ParkItemKind.Tree);
        }

        internal static float MinDistToRing(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++) best = math.min(best, SegmentIndex.DistSq(p, ring[i], ring[(i + 1) % ring.Length]));
            return math.sqrt(best);
        }
    }

    /// <summary>Irregular planting group: a rotated ellipse whose outline wobbles with two waves.</summary>
    public sealed class Grove
    {
        public float2 Center;
        public float Radius, Aspect, Rotation, Phase, FinePhase, Spacing;
        public int Lobes, FineLobes;
        public uint Species;
        public float Area => math.PI * Radius * Radius * Aspect;

        public float2 Point(float angle, float radial)
        {
            float outline = Radius * (1f + 0.22f * math.sin(Lobes * angle + Phase) + 0.12f * math.sin(FineLobes * angle + FinePhase));
            var local = new float2(math.cos(angle), math.sin(angle) * Aspect) * outline * radial;
            // (perf) The rotation's sine and cosine once (the same values every call).
            if (!_turned) { math.sincos(Rotation, out _sin, out _cos); _turned = true; }
            float s = _sin, c = _cos;
            return Center + new float2(local.x * c - local.y * s, local.x * s + local.y * c);
        }

        private bool _turned;
        private float _sin, _cos;
    }

    public static class ParkPlanting
    {
        private const int MaxTrees = 4000, MaxBushes = 6000, MaxFurniture = 1200, MaxBins = 250, MaxGroves = 120;
        private const float AccentSpeciesChance = 0.1f;
        public const float BenchRadius = 1.75f, LampRadius = 0.85f, BinRadius = 0.75f;

        // ───────────── vegetation ─────────────

        public static void BuildGroves(ParkSite s, ParkStyle style)
        {
            if (style.GroveCoverage <= 0f) return;
            var rng = Hashing.Rng(s.Seed, 0x3c6ef372u);
            var region = s.LayerRegion;
            float side = math.sqrt(region.Area);
            float baseRadius = math.clamp(side * 0.16f, Layout.M(12f, 6f), Layout.M(30f, 6f)) * style.GroveScale;
            float largest = math.max(Layout.M(8f, 6f), side * 0.28f) * style.GroveScale;
            float budget = region.Area * style.GroveCoverage;
            float covered = 0;
            for (int attempt = 0; attempt < 4000 && s.Groves.Count < MaxGroves && (covered < budget || s.Groves.Count < 2); attempt++)
            {
                var g = new Grove
                {
                    Radius = math.clamp(baseRadius * (0.45f + 1.25f * math.pow(rng.NextFloat(), 1.8f)), 6f, largest),
                    Aspect = rng.NextFloat(style.GroveAspectMin, 1f),
                    Rotation = rng.NextFloat(0f, math.PI),
                    Lobes = rng.NextInt(2, 4),
                    FineLobes = rng.NextInt(4, 7),
                    Phase = rng.NextFloat(0f, math.PI * 2f),
                    FinePhase = rng.NextFloat(0f, math.PI * 2f),
                    Spacing = rng.NextFloat(0.85f, 1.35f),
                    Center = rng.NextFloat2(region.Min, region.Max),
                    Species = rng.NextUInt(),
                };
                if (!region.Contains(g.Center) || s.InLake(g.Center, g.Radius * 0.5f)) continue;
                // Groves never swallow the protected clearing: that is where add-ons and the eye go.
                if (s.InClearing(g.Center, g.Radius * 0.6f)) continue;
                bool ok = true;
                foreach (var other in s.Groves)
                {
                    float gap = math.max(10f, (g.Radius + other.Radius) * 0.8f);
                    if (math.distancesq(g.Center, other.Center) < gap * gap) { ok = false; break; }
                }
                if (!ok) continue;
                s.Groves.Add(g);
                covered += g.Area;
            }
        }

        public static void Vegetation(ParkSite s, ParkStyle style, int target, bool tree, bool belt)
        {
            if (target <= 0) return;
            float boundaryClearance = tree ? 4f : 2.2f;
            // (perf) The planting grid (PlantGrid.cs) for this pass: every PlantFits below answers from it where it can.
            var grid = s.PlantGrid ??= new PlantGrid(s.LayerRegion);
            if (tree) grid.Prepare(s, boundaryClearance, 2.5f, 8.5f * 0.45f);
            else grid.Prepare(s, boundaryClearance, 1.2f, 4.2f * 0.45f);
            if (style.Formal) { FormalVegetation(s, target, tree, belt); return; }
            if (style.Grid) { GridVegetation(s, target, tree, belt); return; }
            if (!tree) { BushDrifts(s, style, target, belt, boundaryClearance); return; }
            if (style.RowBelt && belt && tree)
            {
                // Street-tree row first (its share of the target); the rest plants naturally without a band.
                var rowRng = Hashing.Rng(s.Seed, 0xB017u);
                target -= Row(s, (int)math.round(target * style.TreeBeltShare), 5f, 9f, true, rowRng.NextUInt(), ref rowRng);
                belt = false;
                if (target <= 0) return;
            }
            var rng = Hashing.Rng(s.Seed, tree ? 0x51f15e21u : 0x9e3779b9u);
            var region = s.LayerRegion;
            var beltShape = belt ? new Belt(region, Hashing.Mix(s.Seed, 0x1234567u)) : null;
            float baseSpacing = tree ? 7.5f : 4f;
            float groveShare = s.Groves.Count > 0 ? (tree ? style.TreeGroveShare : style.BushGroveShare) : 0f;
            float beltShare = belt ? (tree ? style.TreeBeltShare : style.BushBeltShare) : 0f;
            float pathShare = s.Curves.Count > 0 ? (tree ? style.TreePathShare : style.BushPathShare) : 0f;
            var weights = new float[s.Groves.Count];
            float totalWeight = 0;
            for (int i = 0; i < s.Groves.Count; i++) weights[i] = totalWeight += s.Groves[i].Area;
            uint beltSpecies = rng.NextUInt();
            int maxSolitaires = Math.Max(1, (int)Math.Round(target * (tree ? style.TreeSolitaireCap : 0.25f)));
            int solitaires = 0, accepted = 0;
            // Solitaire candidates: PLT's permuted jittered grid, consumed in order.
            var scatter = Scatter.Candidates(region, tree ? 6f : 3.5f, Hashing.Mix(s.Seed, tree ? 11u : 13u),
                math.min(40000, target * 30 + 200), s.Cancel);
            int scatterNext = 0;
            uint clumpSeed = Hashing.Mix(s.Seed, tree ? 0x77u : 0x99u);
            int attempts = Math.Max(80, target * 90);
            for (int attempt = 0; attempt < attempts && accepted < target; attempt++)
            {
                if ((attempt & 1023) == 0) s.Cancel.ThrowIfCancellationRequested();
                float2 p; uint variant; float spacing; bool solitaire = false;
                float source = rng.NextFloat();
                bool accent = rng.NextFloat() < AccentSpeciesChance;
                uint randomVariant = rng.NextUInt();
                if (source < groveShare)
                {
                    int gi = WeightedIndex(weights, rng.NextFloat(0f, totalWeight));
                    var g = s.Groves[gi];
                    float angle = rng.NextFloat(0f, math.PI * 2f);
                    // Trees crowd towards the core; bushes form the understorey fringe.
                    float radial = tree ? math.pow(rng.NextFloat(), 0.6f) : rng.NextFloat(0.85f, 1.3f);
                    p = g.Point(angle, radial);
                    variant = accent ? randomVariant : g.Species;
                    spacing = baseSpacing * g.Spacing;
                }
                else if (source < groveShare + beltShare && beltShape != null && beltShape.Perimeter > 0f)
                {
                    float along = rng.NextFloat(0f, beltShape.Perimeter);
                    if (rng.NextFloat() > beltShape.Coverage(along)) continue;
                    p = beltShape.Point(along, boundaryClearance + beltShape.MaxDepth(tree) * beltShape.DepthFactor(along) * math.pow(rng.NextFloat(), 0.8f));
                    variant = accent ? randomVariant : beltShape.SectionVariant(along, beltSpecies);
                    spacing = baseSpacing;
                }
                else if (source < groveShare + beltShare + pathShare && s.Curves.Count > 0)
                {
                    // Lined walks: a tree (or shrub) a few metres off a path, one species per path.
                    int ci = rng.NextInt(0, s.Curves.Count);
                    var curve = s.Curves[ci];
                    var on = curve.At(rng.NextFloat(0.1f, 0.9f) * curve.Length, out var tangent);
                    float sideSign = rng.NextFloat() < 0.5f ? -1f : 1f;
                    p = on + new float2(-tangent.y, tangent.x) * sideSign * (curve.HalfWidth + (tree ? rng.NextFloat(3f, 5f) : rng.NextFloat(1.6f, 2.8f)));
                    variant = accent ? randomVariant : Hashing.Mix(beltSpecies, (uint)ci + 0x5A7Bu);
                    spacing = tree ? 8f : 3.2f;
                }
                else
                {
                    if (solitaires >= maxSolitaires || scatterNext >= scatter.Count) continue;
                    p = scatter[scatterNext++];
                    float edge = math.min(region.BoundaryDistance(p, 8f), s.Occ.Corridors.Clearance(p, 8f));
                    float chance = Scatter.EdgeRamp(edge, tree) * Scatter.Clustering(p, tree ? 18f : 7f, clumpSeed);
                    if (Hashing.Unit(Hashing.Mix(clumpSeed, (uint)scatterNext)) > chance) continue;
                    variant = randomVariant;
                    spacing = tree ? 14f : 6f;
                    solitaire = true;
                }
                float size = tree ? rng.NextFloat(5f, 8.5f) : rng.NextFloat(2.3f, 4.2f);
                float radius = tree ? math.max(2.5f, size * 0.45f) : math.max(1.2f, size * 0.45f);
                if (!QuickFits(grid, s, p, tree, radius, spacing, boundaryClearance)) continue;
                accepted++;
                if (solitaire) solitaires++;
                s.Add(new ParkItem
                {
                    Kind = tree ? ParkItemKind.Tree : ParkItemKind.Bush, Position = p, Radius = radius,
                    Yaw = rng.NextFloat(0f, math.PI * 2f), Variant = variant, Age = tree ? TreeAge(ref rng) : -1,
                });
            }
        }

        /// <summary>
        /// (makeover) Shrubs the way a gardener plants them: drifts of 3-7 of mixed species and sizes, massed on the inside of the
        /// paths' bends, under the trees' canopies, round the groves and along the outline, with real open lawn between. The old
        /// scatter spread one size 3.5 m apart over every lawn: polka dots (run show-23-builds-1030-033711-0b14, park garden).
        /// </summary>
        private static void BushDrifts(ParkSite s, ParkStyle style, int target, bool belt, float boundaryClearance)
        {
            var rng = Hashing.Rng(s.Seed, 0x9e3779b9u);
            var region = s.LayerRegion;
            var grid = s.PlantGrid;
            bool thicket = style.Id == "neglected" || style.Id == "wild";
            // (an anchor beside a path is a border: long and narrow in the strip between the walk and the lawn room, which may be
            // a place with a mood of its own; elsewhere a drift is a broad group)
            var anchors = new List<(float2 p, float2 axis, float key, float wide)>();
            float wideNext = 1.8f;
            void Anchor(float2 p, float2 axis, float w) => anchors.Add((p, axis, math.pow(math.max(1e-4f, rng.NextFloat()), 1f / math.max(0.05f, w)), wideNext));
            // the inside of every bend (the bend's reason to be there)
            foreach (var c in s.Curves)
            {
                if (c.Length < 12f || c.Tier == PathTier.Desire) continue;
                for (float a = 5f; a < c.Length - 5f; a += 3f)
                {
                    c.At(a - 4f, out var t0); var p1 = c.At(a, out var t1); c.At(a + 4f, out var t2);
                    float turn = t0.x * t2.y - t0.y * t2.x;
                    if (math.abs(turn) < 0.22f) continue;
                    var inside = new float2(-t1.y, t1.x) * math.sign(turn);
                    wideNext = 0.9f;
                    Anchor(p1 + inside * (c.HalfWidth + rng.NextFloat(1.9f, 2.6f)), t1, 1.6f + 2f * math.abs(turn));
                    a += 5f;
                }
                // and here and there along the walk, set back from its edge (the second choice, after the bends)
                for (float a = rng.NextFloat(4f, 14f); a < c.Length - 4f; a += rng.NextFloat(12f, 22f))
                {
                    var p = c.At(a, out var t);
                    float side = rng.NextFloat() < 0.5f ? -1f : 1f;
                    Anchor(p + new float2(-t.y, t.x) * side * (c.HalfWidth + rng.NextFloat(1.9f, 2.6f)), t, 0.6f);
                }
                // the corners where paths meet
                if (rng.NextFloat() < 0.5f) { var e = c.At(math.min(6f, c.Length * 0.3f), out var te); Anchor(e + new float2(-te.y, te.x) * (rng.NextFloat() < 0.5f ? -1f : 1f) * (c.HalfWidth + 2.2f), te, 1.2f); }
            }
            wideNext = 1.8f;
            // under the trees, at the canopy's rim
            foreach (var it in s.Items)
            {
                if (it.Kind != ParkItemKind.Tree || rng.NextFloat() > 0.7f) continue;
                float ang = rng.NextFloat(0f, math.PI * 2f);
                var d = new float2(math.cos(ang), math.sin(ang));
                Anchor(it.Position + d * rng.NextFloat(2.8f, 4.8f), new float2(-d.y, d.x), 1.1f);
            }
            // round the groves (the understorey fringe) and along the outline
            foreach (var g in s.Groves)
            {
                int k = math.clamp((int)(g.Area / 250f), 2, 12);
                for (int i = 0; i < k; i++)
                {
                    float ang = rng.NextFloat(0f, math.PI * 2f);
                    var p = g.Point(ang, rng.NextFloat(0.9f, 1.15f));
                    var q = g.Point(ang + 0.15f, 1f);
                    Anchor(p, math.normalizesafe(q - p, new float2(1f, 0f)), 1.3f);
                }
            }
            if (belt && region.Perimeter > 0f)
                for (float along = rng.NextFloat(0f, 20f); along < region.Perimeter; along += rng.NextFloat(14f, 30f))
                {
                    var b = region.PointAt(along, out var tan, out var inward, out _);
                    Anchor(b + inward * (boundaryClearance + rng.NextFloat(0.5f, 4f)), tan, 0.9f);
                }
            anchors.Sort((x, y) => y.key.CompareTo(x.key));
            var used = new List<float2>();
            int accepted = 0;
            foreach (var an in anchors)
            {
                if (accepted >= target) break;
                if ((accepted & 255) == 0) s.Cancel.ThrowIfCancellationRequested();
                bool near = false;
                foreach (var u in used) if (math.distancesq(u, an.p) < 30f) { near = true; break; }
                if (near) continue;
                // massed: a few small groups, most of 5-11 (a lone pair read as dots from 45°, run show-7-builds-1030-042140-997c)
                int n = thicket ? rng.NextInt(6, 15) : rng.NextFloat() < 0.25f ? rng.NextInt(3, 5) : rng.NextInt(5, 12);
                uint lead = rng.NextUInt(), second = rng.NextUInt();
                var axis = math.normalizesafe(an.axis, new float2(1f, 0f));
                var across = new float2(-axis.y, axis.x);
                float step = rng.NextFloat(1.4f, 1.9f);
                int got = 0;
                for (int b = 0, tries = 0; b < n && accepted < target && tries < n * 3; tries++)
                {
                    float along = (b - (n - 1) * 0.5f) * step + rng.NextFloat(-0.8f, 0.8f);
                    var p = an.p + axis * along + across * rng.NextFloat(-an.wide, an.wide) * (1f - 0.6f * math.abs(along) / (n * step));
                    if (tries >= n * 2) b++;   // the last tries move on to the next shrub of the drift
                    // one or two species lead the drift, a third (often a smaller one) mixes in
                    float u = rng.NextFloat();
                    uint variant = u < 0.55f ? lead : u < 0.85f ? second : rng.NextUInt();
                    float radius = rng.NextFloat(0.9f, 1.4f);
                    int cell = grid != null && grid.Ready ? grid.Cell(p) : -1;
                    byte fx = cell >= 0 ? grid.Static(cell) : PlantGrid.Maybe;
                    if (fx == PlantGrid.Fail || fx == PlantGrid.Maybe && !FixedFits(s, p, radius, boundaryClearance)) continue;
                    if (TooClose(s, p, false, 1.4f, radius, 1.4f, 2.6f)) continue;
                    s.Add(new ParkItem { Kind = ParkItemKind.Bush, Position = p, Radius = radius, Yaw = rng.NextFloat(0f, math.PI * 2f), Variant = variant, Age = -1 });
                    accepted++; got++;
                    if (tries < n * 2) b++;
                }
                if (got > 0) { used.Add(an.p); s.BushDrifts++; }
            }
            s.BushesWanted = target; s.BushesPlanted = accepted;
        }

        /// <summary>
        /// (makeover) Benches where people sit: facing a view across the path (a place with a mood, the lake, the playground,
        /// in pairs there), at the outside of a bend, at the shady edge of a grove, near a junction; long stretches have none.
        /// The old benches stood one every n metres along every path: a metronome.
        /// </summary>
        public static void SeatBenches(ParkSite s, float interval, List<float2> views, float footprint)
        {
            if (interval <= 0f || float.IsInfinity(interval) || s.Curves.Count == 0) return;
            var rng = Hashing.Rng(s.Seed, 0x7f4a7c15u);
            float total = 0f;
            foreach (var c in s.Curves) if (c.Tier != PathTier.Desire) total += c.Length;
            int budget = math.min(MaxFurniture, math.max(1, (int)math.round(total / interval)));
            var cands = new List<(float2 q, float yaw, float score, float2 tan, bool play)>();
            foreach (var c in s.Curves)
            {
                if (c.Length < 8f || c.Tier == PathTier.Desire) continue;
                for (float a = 3f; a < c.Length - 3f; a += 4f)
                {
                    c.At(math.max(0f, a - 4f), out var t0); var p = c.At(a, out var tan); c.At(math.min(c.Length, a + 4f), out var t2);
                    float turn = t0.x * t2.y - t0.y * t2.x;
                    var normal = new float2(-tan.y, tan.x);
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        float side = sd;
                        var q = p + normal * (c.HalfWidth + footprint + 0.2f) * side;
                        var facing = -normal * side;
                        float score = 0f; bool play = false;
                        foreach (var v in views)
                        {
                            var to = v - q; float d = math.length(to);
                            if (d < 4f || d > 38f) continue;
                            float dot = math.dot(to / d, facing);
                            if (dot > 0.55f) score = math.max(score, 2.2f * dot * (1f - d / 42f));
                        }
                        if (s.ClearingRadius > 0f)
                        {
                            float d = math.distance(q, s.ClearingCenter) - s.ClearingRadius;
                            if (d > 0f && d < 9f && math.dot(math.normalizesafe(s.ClearingCenter - q), facing) > 0.4f) { score = math.max(score, 2.6f); play = true; }
                        }
                        // the outside of a bend looks along both arms of the path
                        if (math.abs(turn) > 0.25f && math.sign(turn) == -side) score += 1.2f * math.abs(turn);
                        // shade: the rim of a grove
                        foreach (var g in s.Groves)
                        {
                            float d = math.distance(q, g.Center);
                            if (d > g.Radius * 0.55f && d < g.Radius * 1.15f) { score += 0.9f; break; }
                        }
                        // not at the gate, a little near the junctions
                        if (s.GateDistanceSq(q) < 225f) score -= 1.5f;
                        if (a < 12f || a > c.Length - 12f) score += 0.4f;
                        score += rng.NextFloat(0f, 0.5f);
                        cands.Add((q, Wrap(PathSideYaw(normal, side)), score, tan, play));
                    }
                }
            }
            cands.Sort((x, y) => y.score.CompareTo(x.score));
            int count = 0;
            for (int pass = 0; pass < 2 && count < budget; pass++)
            {
                float min = pass == 0 ? 1.1f : -99f, gap = pass == 0 ? Layout.M(24f, 6f) : Layout.M(48f, 8f);
                int want = pass == 0 ? budget : math.max(1, budget / 2);
                foreach (var cd in cands)
                {
                    if (count >= want) break;
                    if (cd.score < min) break;
                    if (!FurnitureFits(s, cd.q, footprint, ParkItemKind.Bench, gap)) continue;
                    s.Add(new ParkItem { Kind = ParkItemKind.Bench, Position = cd.q, Radius = footprint, Yaw = cd.yaw, Variant = rng.NextUInt(), Age = -1 });
                    count++;
                    // a pair by the playground or at a fine view: two benches side by side
                    if ((cd.play && rng.NextFloat() < 0.7f || cd.score > 2.6f && rng.NextFloat() < 0.3f) && count < budget)
                    {
                        var q2 = cd.q + cd.tan * (rng.NextFloat() < 0.5f ? -1f : 1f) * (footprint * 2f + 0.6f);
                        if (FurnitureFits(s, q2, footprint, ParkItemKind.Bench, footprint * 2f))
                        {
                            s.Add(new ParkItem { Kind = ParkItemKind.Bench, Position = q2, Radius = footprint, Yaw = cd.yaw, Variant = rng.NextUInt(), Age = -1 });
                            count++;
                        }
                    }
                }
            }
        }

        /// <summary>(perf) PlantFits with the grid's quick answers first: always the same answer as PlantFits.</summary>
        private static bool QuickFits(PlantGrid grid, ParkSite s, float2 p, bool tree, float radius, float spacing, float boundaryClearance)
        {
            int cell = grid.Cell(p);
            byte fixedChecks = grid.Static(cell);
            if (fixedChecks == PlantGrid.Fail) return false;
            if (grid.Crowded(cell, p, tree, spacing)) return false;
            if (fixedChecks == PlantGrid.Maybe && !FixedFits(s, p, radius, boundaryClearance)) return false;
            return !TooClose(s, p, tree, spacing, radius);
        }

        private static bool PlantFits(ParkSite s, float2 p, bool tree, float radius, float spacing, float boundaryClearance)
        {
            var grid = s.PlantGrid;
            if (grid == null || !grid.Ready) return FixedFits(s, p, radius, boundaryClearance) && !TooClose(s, p, tree, spacing, radius);
            // (perf) The grid's quick answers first (rows, avenues, grids): the same answer.
            int cell = grid.Cell(p);
            byte fixedChecks = grid.Static(cell, boundaryClearance, radius);
            if (fixedChecks == PlantGrid.Fail) return false;
            if (grid.Crowded(cell, p, tree, spacing)) return false;
            if (fixedChecks == PlantGrid.Maybe && !FixedFits(s, p, radius, boundaryClearance)) return false;
            return !TooClose(s, p, tree, spacing, radius);
        }

        /// <summary>The checks that do not depend on what was planted: outline, gates, lake, clearing, paths, reserved polygons.</summary>
        private static bool FixedFits(ParkSite s, float2 p, float radius, float boundaryClearance)
        {
            if (!s.LayerRegion.ContainsDisc(p, boundaryClearance)) return false;
            if (s.GateDistanceSq(p) < 100f) return false;
            if (s.InLake(p, radius) || s.InClearing(p, radius)) return false;
            if (s.Occ.Corridors.Overlaps(p, radius, 0.35f)) return false;
            return !s.Occ.HitsPolygon(p, radius);   // obstacles / reserved polygons
        }

        /// <summary>ParkManager's spacing rules: own layer keeps `spacing`, others their collision gaps.</summary>
        private static bool TooClose(ParkSite s, float2 p, bool tree, float spacing, float radius, float bushGap = 3.5f, float bushTreeGap = 6f)
        {
            var near = s.Scratch;
            var discs = s.Occ.Discs;
            discs.Query(p, math.max(spacing, math.max(radius + 1.75f, 6f)), near);
            int kind = tree ? (int)ParkItemKind.Tree : (int)ParkItemKind.Bush;
            foreach (int i in near)
            {
                var o = discs.Items[i];
                float dsq = math.distancesq(p, o.P);
                float combined;
                if (o.Kind == kind && dsq < spacing * spacing) return true;
                switch (o.Kind)
                {
                    case (int)ParkItemKind.Bench: combined = radius + 1.75f; break;
                    case (int)ParkItemKind.Lamp: combined = radius + 0.85f; break;
                    case (int)ParkItemKind.Bin: combined = radius + 0.75f; break;
                    case (int)ParkItemKind.Tree:
                    case (int)ParkItemKind.Bush:
                        combined = tree ? 6f : o.Kind == (int)ParkItemKind.Tree ? bushTreeGap : bushGap; break;
                    default: combined = radius + o.R + 0.3f; break;   // reserved / spawner / anything else
                }
                if (dsq < combined * combined) return true;
            }
            return false;
        }

        /// <summary>Formal planting: tree avenues along main paths, then regular rows; bushes as boundary hedges.</summary>
        private static void FormalVegetation(ParkSite s, int target, bool tree, bool belt)
        {
            int placed = 0;
            var rng = Hashing.Rng(s.Seed, tree ? 0xF0F0u : 0x0F0Fu);
            uint species = rng.NextUInt();
            if (tree)
            {
                float interval = Layout.M(9f, 6f);
                foreach (var c in s.Curves)
                {
                    if (placed >= target) break;
                    if (c.Kind == PathEdgeKind.Secondary && c.Role == NetRole.Pathway && s.Curves.Count > 3) continue;
                    float offset = c.HalfWidth + 3f;
                    int n = (int)math.floor((c.Length - 6f) / interval);
                    for (int i = 0; i <= n && placed < target; i++)
                    {
                        float along = 3f + (c.Length - 6f) * (n > 0 ? i / (float)n : 0.5f);
                        var p = c.At(along, out var t);
                        var normal = new float2(-t.y, t.x);
                        for (int side = -1; side <= 1 && placed < target; side += 2)
                        {
                            var q = p + normal * offset * side;
                            if (!PlantFits(s, q, true, 2.5f, interval * 0.8f, 3f)) continue;
                            s.Add(new ParkItem { Kind = ParkItemKind.Tree, Position = q, Radius = 2.5f, Yaw = rng.NextFloat(0, 6.283f), Variant = species, Age = 3 });
                            placed++;
                        }
                    }
                }
                if (belt) placed += Row(s, target - placed, 5f, 10f, true, species, ref rng);
            }
            else if (belt) placed += Row(s, target, 2.2f, 2.4f, false, species, ref rng);
            // (P2-8) whatever the avenues and the edge rows did not take stays unplanted: a grid of the rest on the open lawn read as
            // an orchard in the formal green's lawn panels (game run show-6-builds-1031-135541)
        }

        /// <summary>
        /// Modern planting: trees in one strict grid (a bosque, one species, 10 m pitch, aligned to the
        /// park's axes) wherever the lawn is free; bushes only as a clipped hedge row along the outline.
        /// </summary>
        private static void GridVegetation(ParkSite s, int target, bool tree, bool belt)
        {
            var rng = Hashing.Rng(s.Seed, tree ? 0x6D0Du : 0x6D0Eu);
            uint species = rng.NextUInt();
            if (tree) BosqueFill(s, target, species);
            else if (belt) Row(s, target, 2.2f, 2.2f, false, species, ref rng);
        }

        /// <summary>
        /// One regular grid over the whole park: the pitch widens (10 → 30 m) until the free grid points
        /// roughly match the wanted count, so a low density still reads as a complete, even grid.
        /// </summary>
        private static void BosqueFill(ParkSite s, int target, uint species)
        {
            var region = s.LayerRegion;
            region.PrincipalAxes(out var c, out var axis, out _, out _);
            var n = new float2(-axis.y, axis.x);
            float ext = math.length(region.Max - region.Min) * 0.5f + 2f;
            var points = new List<float2>();
            for (float pitch = Layout.M(10f, 6f); pitch <= Layout.M(30f, 6.5f); pitch *= 1.12f)
            {
                s.Cancel.ThrowIfCancellationRequested();
                points.Clear();
                for (float u = -math.floor(ext / pitch) * pitch; u <= ext; u += pitch)
                    for (float v = -math.floor(ext / pitch) * pitch; v <= ext; v += pitch)
                    {
                        var p = c + axis * u + n * v;
                        if (region.Contains(p) && PlantFits(s, p, true, 2.5f, pitch * 0.9f, 4f)) points.Add(p);
                    }
                if (points.Count <= target * 1.15f) break;
            }
            int placed = 0;
            foreach (var p in points)
            {
                if (placed >= target) break;
                if (!PlantFits(s, p, true, 2.5f, 8f, 4f)) continue;
                s.Add(new ParkItem { Kind = ParkItemKind.Tree, Position = p, Radius = 2.5f, Variant = species, Age = 3 });
                placed++;
            }
        }

        private static int Row(ParkSite s, int target, float inset, float interval, bool tree, uint species, ref Unity.Mathematics.Random rng)
        {
            int placed = 0;
            float radius = tree ? 2.5f : 1.0f;
            for (float along = interval * 0.5f; along < s.LayerRegion.Perimeter && placed < target; along += interval)
            {
                var p = s.LayerRegion.PointAt(along, out _, out var inward, out _) + inward * inset;
                if (!PlantFits(s, p, tree, radius, interval * 0.9f, inset * 0.8f)) continue;
                s.Add(new ParkItem { Kind = tree ? ParkItemKind.Tree : ParkItemKind.Bush, Position = p, Radius = radius, Yaw = 0f, Variant = species, Age = tree ? 3 : -1 });
                placed++;
            }
            return placed;
        }

        private static void GridFill(ParkSite s, int target, bool tree, uint species, ref Unity.Mathematics.Random rng)
        {
            s.LayerRegion.PrincipalAxes(out var c, out var axis, out _, out _);
            var n = new float2(-axis.y, axis.x);
            float pitch = tree ? Layout.M(12f, 6f) : Layout.M(5f, 2.4f);
            float radius = tree ? 2.5f : 1.0f;
            var region = s.LayerRegion;
            float ext = math.length(region.Max - region.Min);
            int placed = 0;
            for (float u = -ext; u <= ext && placed < target; u += pitch)
                for (float v = -ext; v <= ext && placed < target; v += pitch)
                {
                    var p = c + axis * u + n * v;
                    if (!region.Contains(p)) continue;
                    if (!PlantFits(s, p, tree, radius, pitch * 0.9f, tree ? 4f : 2f)) continue;
                    s.Add(new ParkItem { Kind = tree ? ParkItemKind.Tree : ParkItemKind.Bush, Position = p, Radius = radius, Variant = species, Age = tree ? 3 : -1 });
                    placed++;
                }
        }

        private static int WeightedIndex(float[] cumulative, float value)
        {
            int lo = 0, hi = cumulative.Length - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (cumulative[mid] > value) hi = mid; else lo = mid + 1; }
            return lo;
        }

        private static int TreeAge(ref Unity.Mathematics.Random rng)
        {
            // An established park: mostly mature trees with some replanting (ParkManager 8 % / 20 %).
            float roll = rng.NextFloat();
            return roll < 0.08f ? 1 : roll < 0.28f ? 2 : 3;
        }

        /// <summary>
        /// Planted band along the boundary: periodic noise varies coverage (open views), depth and
        /// dominant species per section (ParkManager BoundaryBelt).
        /// </summary>
        private sealed class Belt
        {
            private readonly LayerRegion _region;
            private readonly uint _seed;
            private readonly int _coverageCells, _depthCells, _sectionCells;
            private readonly float _depthScale;
            public readonly float Perimeter;

            public Belt(LayerRegion region, uint seed)
            {
                _region = region; _seed = seed; Perimeter = region.Perimeter;
                _coverageCells = Math.Max(1, (int)Math.Round(Perimeter / 70f));
                _depthCells = Math.Max(1, (int)Math.Round(Perimeter / 45f));
                _sectionCells = Math.Max(1, (int)Math.Round(Perimeter / 90f));
                _depthScale = math.clamp(math.sqrt(region.Area) / 250f, 1f, 2.2f);
                MakeLattices();
            }

            public float Coverage(float along) => math.smoothstep(0.42f, 0.72f, CoverageNoise(along));
            public float DepthFactor(float along) => 0.25f + 0.75f * (0.6f * CoverageNoise(along) + 0.4f * Noise(along, _depthCells, 2u));

            // (perf) A candidate asks Coverage and then DepthFactor at the same point: the coverage noise once.
            private float _lastAlong = float.NaN, _lastCoverage;
            private float CoverageNoise(float along)
            {
                if (along != _lastAlong) { _lastCoverage = Noise(along, _coverageCells, 1u); _lastAlong = along; }
                return _lastCoverage;
            }
            public float MaxDepth(bool tree) => Layout.M((tree ? 7f : 9f) * _depthScale, tree ? 3f : 2f);
            public uint SectionVariant(float along, uint layerSeed) =>
                Hashing.Mix(layerSeed, (uint)((int)(along / Perimeter * _sectionCells) % _sectionCells));
            public float2 Point(float along, float inset) => _region.PointAt(along, out _, out var inward, out _) + inward * inset;

            private float Noise(float along, int cells, uint salt)
            {
                float t = along / Perimeter;
                var coarse = salt == 1u ? _cov : _depth;
                var fine = salt == 1u ? _covFine : _depthFine;
                if (coarse == null || coarse.Period != Math.Max(1, cells)) return 0.7f * ValueNoise.Periodic(t * cells, cells, _seed ^ salt) + 0.3f * ValueNoise.Periodic(t * cells * 3, cells * 3, _seed ^ (salt * 0x9e3779b9u));
                return 0.7f * coarse.At(t * cells) + 0.3f * fine.At(t * cells * 3);
            }

            // (perf) The noise's lattice values once (a belt candidate asked for 4–6 noise samples, each hashing two of them).
            private Lattice _cov, _covFine, _depth, _depthFine;

            private void MakeLattices()
            {
                _cov = new Lattice(_coverageCells, _seed ^ 1u); _covFine = new Lattice(_coverageCells * 3, _seed ^ (1u * 0x9e3779b9u));
                _depth = new Lattice(_depthCells, _seed ^ 2u); _depthFine = new Lattice(_depthCells * 3, _seed ^ unchecked(2u * 0x9e3779b9u));
            }

            /// <summary>ValueNoise.Periodic(x, period, seed) with its lattice values looked up: the same arithmetic, the same values.</summary>
            private sealed class Lattice
            {
                public readonly int Period;
                private readonly float[] _v;

                public Lattice(int period, uint seed)
                {
                    Period = math.max(1, period);
                    _v = new float[Period];
                    for (int i = 0; i < Period; i++) _v[i] = Hashing.Unit(Hashing.Mix(seed, (uint)i));
                }

                public float At(float x)
                {
                    int cell = (int)math.floor(x);
                    float f = x - cell;
                    f = f * f * (3f - 2f * f);
                    int a = ((cell % Period) + Period) % Period, b = (a + 1) % Period;
                    return math.lerp(_v[a], _v[b], f);
                }
            }
        }

        // ───────────── furniture ─────────────

        /// <summary>Benches and lamps along paths at intervals derived from "per 100 m".</summary>
        public static void PathFurniture(ParkSite s, ParkItemKind kind, float interval, float footprint, uint salt, Func<PathCurve, bool> only = null)
        {
            if (interval <= 0f || float.IsInfinity(interval)) return;
            var rng = Hashing.Rng(s.Seed, salt);
            int count = 0;
            int sidePhase = kind == ParkItemKind.Lamp ? 1 : kind == ParkItemKind.Bin ? 2 : 0;
            for (int ci = 0; ci < s.Curves.Count && count < MaxFurniture; ci++)
            {
                var c = s.Curves[ci];
                if (c.Length < interval * 0.55f || only != null && !only(c)) continue;
                int pieces = Math.Max(1, (int)Math.Floor(c.Length / interval));
                for (int i = 0; i < pieces && count < MaxFurniture; i++)
                {
                    float t = (i + 1f) / (pieces + 1f);
                    t = math.clamp(t + rng.NextFloat(-0.06f, 0.06f), 0.15f, 0.85f);
                    var p = c.At(t * c.Length, out var tangent);
                    var normal = new float2(-tangent.y, tangent.x);
                    float side = ((i + ci + sidePhase) & 1) == 0 ? 1f : -1f;
                    var q = p + normal * (c.HalfWidth + footprint + 0.2f) * side;
                    if (!FurnitureFits(s, q, footprint, kind, kind == ParkItemKind.Bench ? Layout.M(14f, 4f) : kind == ParkItemKind.Lamp ? Layout.M(10f, 2f) : Layout.M(12f, 2f))) continue;
                    // Facing the path (PlannedObject.Yaw: 0 = facing +Z, the front of CS2 benches/lamps).
                    // ParkManager stored the tangent and added a quarter turn at emission; the port
                    // lost that turn, so benches stood sideways/backwards (playtest 4b).
                    float yaw = PathSideYaw(normal, side);
                    s.Add(new ParkItem { Kind = kind, Position = q, Radius = footprint, Yaw = Wrap(yaw), Variant = rng.NextUInt(), Age = -1 });
                    count++;
                }
            }
        }

        /// <summary>Yaw of path furniture standing at p + normal·side: it faces back toward the path.</summary>
        public static float PathSideYaw(float2 normal, float side)
        {
            var facing = -normal * (side < 0f ? -1f : 1f);
            return Wrap(math.atan2(facing.x, facing.y));
        }

        private static bool FurnitureFits(ParkSite s, float2 p, float footprint, ParkItemKind kind, float sameKindSpacing)
        {
            if (!s.LayerRegion.ContainsDisc(p, 1.5f)) return false;
            if (s.GateDistanceSq(p) < 64f) return false;
            if (s.InLake(p, footprint + 0.5f)) return false;
            if (s.Occ.Corridors.Overlaps(p, footprint, 0.1f)) return false;
            var near = s.Scratch;
            s.Occ.Discs.Query(p, math.max(sameKindSpacing, footprint + 4f), near);
            foreach (int i in near)
            {
                var o = s.Occ.Discs.Items[i];
                float dsq = math.distancesq(p, o.P);
                if (o.Kind == (int)kind && dsq < sameKindSpacing * sameKindSpacing) return false;
                if (dsq < (footprint + o.R + 0.35f) * (footprint + o.R + 0.35f)) return false;
            }
            return s.Occ.Free(p, footprint, 0f, 0.1f);
        }

        /// <summary>Bins where they are useful: gates, then junctions, then beside benches (ParkManager SampleTrashBins).</summary>
        public static void Bins(ParkSite s, float densityScale)
        {
            if (s.Graph.IsEmpty || s.Curves.Count == 0) return;
            var rng = Hashing.Rng(s.Seed, 0xa54ff53au);
            var adj = s.Graph.Adjacency();
            int important = 0;
            for (int i = 0; i < s.Graph.Nodes.Count; i++)
                if (s.Graph.Nodes[i].Kind == PathNodeKind.Gate || adj[i].Count >= 3) important++;
            int target = math.clamp((int)Math.Round(Math.Max(important, s.Graph.TotalLength / Layout.M(90f)) * densityScale), 1, MaxBins);
            int placed = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < s.Graph.Nodes.Count && placed < target; i++)
                {
                    var node = s.Graph.Nodes[i];
                    bool gate = node.Kind == PathNodeKind.Gate;
                    if (pass == 0 ? !gate : gate || adj[i].Count < 3) continue;
                    if (TryBinAtNode(s, adj, i, gate ? 3.5f : 2.5f, gate ? 6f : 4.5f, ref rng)) placed++;
                }
            int benchCount = s.Items.Count;
            for (int i = 0; i < benchCount && placed < target; i++)
            {
                var b = s.Items[i];
                if (b.Kind != ParkItemKind.Bench) continue;
                var facing = new float2(math.sin(b.Yaw), math.cos(b.Yaw));
                var tangent = new float2(facing.y, -facing.x);   // along the path
                float first = rng.NextBool() ? 1f : -1f;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var p = b.Position + tangent * (attempt == 0 ? first : -first) * rng.NextFloat(2.7f, 4.2f);
                    if (TryBin(s, p, tangent, ref rng)) { placed++; break; }
                }
            }
            if (placed == 0) PathFurniture(s, ParkItemKind.Bin, 70f, BinRadius, 0xa54ff53bu);
        }

        private static bool TryBinAtNode(ParkSite s, List<int>[] adj, int node, float minAlong, float maxAlong, ref Unity.Mathematics.Random rng)
        {
            var edges = adj[node];
            if (edges.Count == 0) return false;
            int start = rng.NextInt(0, edges.Count);
            float firstSide = rng.NextBool() ? 1f : -1f;
            for (int k = 0; k < edges.Count; k++)
            {
                var e = s.Graph.Edges[edges[(start + k) % edges.Count]];
                int other = e.A == node ? e.B : e.A;
                var tangent = math.normalizesafe(s.Graph.Nodes[other].Position - s.Graph.Nodes[node].Position);
                if (math.lengthsq(tangent) < 0.5f) continue;
                var normal = new float2(-tangent.y, tangent.x);
                float along = rng.NextFloat(minAlong, maxAlong);
                float offset = Composer.DefaultHalfWidth(NetRole.PathwayWide) + BinRadius + 0.2f;
                for (int side = 0; side < 2; side++)
                {
                    var p = s.Graph.Nodes[node].Position + tangent * along + normal * offset * (side == 0 ? firstSide : -firstSide);
                    if (TryBin(s, p, tangent, ref rng)) return true;
                }
            }
            return false;
        }

        private static bool TryBin(ParkSite s, float2 p, float2 tangent, ref Unity.Mathematics.Random rng)
        {
            if (!s.LayerRegion.ContainsDisc(p, 1f) || s.InLake(p, 1f)) return false;
            if (s.Occ.Corridors.Overlaps(p, BinRadius, 0.1f)) return false;
            var near = s.Scratch;
            s.Occ.Discs.Query(p, 9f, near);
            foreach (int i in near)
            {
                var o = s.Occ.Discs.Items[i];
                float dsq = math.distancesq(p, o.P);
                if (o.Kind == (int)ParkItemKind.Bin && dsq < 81f) return false;
                if (dsq < (BinRadius + o.R + 0.35f) * (BinRadius + o.R + 0.35f)) return false;
            }
            if (!s.Occ.Free(p, BinRadius, 0f, 0.1f)) return false;
            s.Add(new ParkItem { Kind = ParkItemKind.Bin, Position = p, Radius = BinRadius, Yaw = Wrap(math.atan2(tangent.x, tangent.y)), Variant = rng.NextUInt(), Age = -1 });
            return true;
        }

        // ───────────── fence & wildlife ─────────────

        /// <summary>Forest animal spawners in the largest groves: one per 2 ha, at most 8 (ParkManager).</summary>
        public static void AnimalSpawners(ParkSite s)
        {
            int target = math.min(8, (int)Math.Round(s.LayerRegion.Area / 20000.0));
            if (target <= 0) return;
            var order = new List<Grove>(s.Groves);
            order.Sort((a, b) => b.Area.CompareTo(a.Area));
            int placed = 0;
            foreach (var g in order)
            {
                if (placed >= target) break;
                if (!s.LayerRegion.Contains(g.Center) || s.InLake(g.Center, 4f) || s.Occ.Corridors.Overlaps(g.Center, 4f, 0f)) continue;
                s.Items.Add(new ParkItem { Kind = ParkItemKind.Spawner, Position = g.Center, Radius = 0.5f, Age = -1 });
                placed++;
            }
        }

        internal static float Wrap(float a)
        {
            float full = math.PI * 2f;
            a %= full;
            return a < 0 ? a + full : a;
        }
    }
}
