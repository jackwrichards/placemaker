// Ported from ParkManager (tomkpunkt, GPL-3.0), Geometry/ParkPathPlanner.cs and the
// best-of-N candidate loop of Tools/ParkToolSystem.cs.
//
// The hybrid roadmap idea is unchanged: entrance terminals + triangle-dual nodes + a jittered
// interior grid form a weighted roadmap (cost grows near the boundary); entrances are joined by a
// terminal Steiner MST of shortest paths, coverage loops are added, then the most "stretched"
// pair of network nodes gets a shortcut.
//
// Performance fixes (ParkManager allocated an n×n double matrix per candidate and another one
// per node pair inside AddStretchLoop, i.e. O(n⁴) memory traffic):
//   * sparse adjacency lists + binary-heap Dijkstra,
//   * neighbour candidates from a uniform grid instead of all-pairs scans,
//   * the stretch loop runs one Dijkstra per network node on the network-only graph,
//   * cancellation is checked between candidates and phases.
// Determinism: only Unity.Mathematics.Random / hashes seeded from the layer seed.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    public static class ParkPathPlanner
    {
        // Layout size (FEEDBACK-4): distances are design metres × Layout.Scale; the boundary clearance is the
        // one edge margin plus a wide path's half width (never less than the scaled design value).
        private static double BoundaryClearance => Math.Max(Layout.M(5f), Layout.EdgeMargin + Composer.DefaultHalfWidth(NetRole.Pathway) + 1f);
        private static double PreferredBoundaryClearance => Math.Max(BoundaryClearance + 1.0, Layout.M(12f));
        private const double MinimumGateDirectionCosine = 0.7071067811865476;
        private static double MinimumPathSeparation => Layout.M(8f, 4f);
        private const double MaximumAcuteBranchCosine = 0.8660254037844386;
        private const int MaximumRoadmapNodes = 420;
        public const int CandidateCount = 6;

        private sealed class Node { public float2 Position; public double Clearance; public bool GateApproach; }
        private sealed class Triangle { public int A, B, C; public double Clearance; public float2 Centroid; public bool Inside; }

        private sealed class Path
        {
            public readonly List<int> Nodes = new List<int>();
            public double Cost = double.MaxValue;
            public bool Found => Nodes.Count > 1 && Cost < double.MaxValue;
        }

        /// <summary>Sparse weighted undirected graph.</summary>
        private sealed class Graph
        {
            public readonly List<int>[] Adj;
            /// <summary>(perf) The weight of each Adj entry, at the same index (was a dictionary by node pair: most of the roadmap's time).</summary>
            public readonly List<double>[] W;
            public Graph(int n)
            {
                Adj = new List<int>[n]; W = new List<double>[n];
                for (int i = 0; i < n; i++) { Adj[i] = new List<int>(8); W[i] = new List<double>(8); }
            }
            public int Count => Adj.Length;
            public bool Has(int a, int b) => Adj[a].IndexOf(b) >= 0;
            public double Weight(int a, int b) { int k = Adj[a].IndexOf(b); return k >= 0 ? W[a][k] : 0.0; }
            public void Set(int a, int b, double w)
            {
                int k = Adj[a].IndexOf(b);
                if (k >= 0)
                {
                    if (w < W[a][k]) { W[a][k] = w; W[b][Adj[b].IndexOf(a)] = w; }
                    return;
                }
                Adj[a].Add(b); W[a].Add(w);
                Adj[b].Add(a); W[b].Add(w);
            }
        }

        /// <summary>Settings for one planning run.</summary>
        public struct Options
        {
            /// <summary>Coverage loops to add (−1 = by area, as ParkManager).</summary>
            public int Loops;
            /// <summary>Add the stretch shortcut (only for richer networks).</summary>
            public bool StretchLoop;
            /// <summary>(p1-ground) Ground height for the slope cost; null = <see cref="AmbientHeight"/> (the composer sets it per layer).</summary>
            public Func<float2, float> Height;
            /// <summary>Along-grade a walk takes without strain (rise/run); 0 = TerrainSurvey.WalkGrade (8 %).</summary>
            public float MaxGrade;
        }

        /// <summary>
        /// (p1-ground) The site's ground while a layer plans (Composer.Run), so every type that lays its walks with this planner
        /// weighs the slope without each passing it. Thread-static: planning runs on workers.
        /// </summary>
        [ThreadStatic] public static Func<float2, float> AmbientHeight;
        /// <summary>Tests: the old distance-only roadmap, to compare.</summary>
        [ThreadStatic] public static bool SlopeCostOff;
        [ThreadStatic] private static Func<float2, float> _height;
        [ThreadStatic] private static float _grade;

        /// <summary>Best of <see cref="CandidateCount"/> seeded candidates by naturalness score.</summary>
        public static ParkPathGraph PlanBest(LayerRegion region, IReadOnlyList<float2> gates, Options options, uint seed,
            CancellationToken cancel)
        {
            if (gates == null || gates.Count == 0) return ParkPathGraph.Empty();
            int wantedLoops = options.Loops >= 0 ? options.Loops : LoopsByArea(region.Area);
            ParkPathGraph best = null;
            double bestScore = double.MaxValue;
            var hub = region.DeepestPoint(out _, null, 12);
            // One triangulation for all candidates (its seeded start vertex still varies per park seed);
            // ear clipping a 100+ vertex painted outline six times was a third of the planning time.
            var triangles = Triangulate(region, (int)(seed % (uint)region.Ring.Length));
            var keepH = _height; var keepG = _grade;
            _height = SlopeCostOff ? null : options.Height ?? AmbientHeight;
            _grade = options.MaxGrade > 0.005f ? options.MaxGrade : TerrainSurvey.WalkGrade;
            if (_height != null && IsLevel(region, _height)) _height = null;   // flat land: exactly the old roadmap
            try
            {
                for (int i = 0; i < CandidateCount; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    uint candidateSeed = Hashing.Mix(seed, (uint)(i * 104729 + 1));
                    var candidate = Generate(region, gates, hub, candidateSeed, wantedLoops, options.StretchLoop, triangles, cancel);
                    double score = candidate.NaturalnessScore(region, wantedLoops) + SteepScore(candidate);
                    if (score < bestScore) { best = candidate; bestScore = score; }
                }
            }
            finally { _height = keepH; _grade = keepG; }
            return best ?? ParkPathGraph.Empty();
        }

        // ───────────── slope (p1-ground) ─────────────
        // The user: on a hill "I'm seeing certain pathways not work ... smoothing the paths out, not having them double back on each
        // other and snap". The roadmap knew only distance, so walks went straight up the fall line: grades of 20-40 % that the game
        // rejects (a pathway's max slope) or the site harmony cut metres deep to tame (350-450 steep path pieces per park on the game
        // runner's mountains). Now each roadmap link costs more the steeper it climbs (like the hiking trail's router): the cheapest
        // routes run across the slope and turn back and forth where they must gain height (switchbacks), and on gentle land
        // nothing changes.

        /// <summary>Whether the region's ground varies by less than 1.5 m (no slope cost then).</summary>
        private static bool IsLevel(LayerRegion region, Func<float2, float> h)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            var size = region.Max - region.Min;
            for (int j = 0; j <= 8; j++)
                for (int i = 0; i <= 8; i++)
                {
                    var p = region.Min + size * new float2(i / 8f, j / 8f);
                    float v = h(p);
                    if (!math.isfinite(v)) continue;
                    lo = math.min(lo, v); hi = math.max(hi, v);
                }
            return !(hi - lo >= 1.5f);
        }

        /// <summary>Mean cost multiplier of walking a straight link: 1 on the level, gently more up to the walk grade, steeply beyond.</summary>
        private static double SlopeFactor(float2 a, float2 b, double d)
        {
            if (_height == null || d < 0.5) return 1.0;
            int n = Math.Max(2, (int)Math.Ceiling(d / 6.0));
            double seg = d / n, sum = 0.0;
            float prev = _height(a);
            for (int k = 1; k <= n; k++)
            {
                float h = _height(math.lerp(a, b, k / (float)n));
                double r = Math.Abs(h - prev) / seg / _grade;
                prev = h;
                sum += r <= 1.0 ? 1.0 + 0.4 * r * r : 1.4 + 6.0 * (r - 1.0);
            }
            return sum / n;
        }

        /// <summary>Candidate penalty for walks steeper than the walk grade (metres of path times how far over it they are).</summary>
        private static double SteepScore(ParkPathGraph g)
        {
            if (_height == null) return 0.0;
            double s = 0.0;
            foreach (var e in g.Edges)
            {
                var a = g.Nodes[e.A].Position; var b = g.Nodes[e.B].Position;
                double d = math.distance(a, b);
                if (d < 1.0) continue;
                double r = Math.Abs(_height(b) - _height(a)) / d / _grade;
                if (r > 1.0) s += d * (r - 1.0) * 2.0;
            }
            return s;
        }

        /// <summary>Coverage loops by area, judged at design size (a smaller layout gets more, smaller loops).</summary>
        public static int LoopsByArea(float area)
        {
            float a = Layout.DesignArea(area);
            return a > 12000f ? 2 : a > 1800f ? 1 : 0;
        }

        private static ParkPathGraph Generate(LayerRegion region, IReadOnlyList<float2> gates, float2 hub, uint seed,
            int loops, bool stretch, List<Triangle> triangles, CancellationToken cancel)
        {

            var output = new List<float2>();
            int terminals = gates.Count;
            var nodes = new List<Node>();
            foreach (var g in gates) nodes.Add(new Node { Position = g, Clearance = 0 });

            var triangleNode = new int[triangles.Count];
            for (int ti = 0; ti < triangles.Count; ti++)
            {
                var t = triangles[ti];
                if (!t.Inside) { triangleNode[ti] = -1; continue; }
                triangleNode[ti] = nodes.Count;
                nodes.Add(new Node { Position = t.Centroid, Clearance = t.Clearance });
            }
            double spacing = SamplingSpacing(region);
            AddInteriorSamples(nodes, region, spacing, terminals, seed);
            if (nodes.Count == terminals)
                nodes.Add(new Node { Position = hub, Clearance = region.BoundaryDistance(hub) });
            var approaches = AddGateApproachNodes(nodes, region, terminals, spacing);
            cancel.ThrowIfCancellationRequested();

            var graph = new Graph(nodes.Count);
            var owners = new Dictionary<long, int>();
            for (int ti = 0; ti < triangles.Count; ti++)
            {
                var t = triangles[ti];
                ConnectTriangleEdge(owners, t.A, t.B, triangleNode[ti], graph, nodes, region, seed);
                ConnectTriangleEdge(owners, t.B, t.C, triangleNode[ti], graph, nodes, region, seed);
                ConnectTriangleEdge(owners, t.C, t.A, triangleNode[ti], graph, nodes, region, seed);
            }
            ConnectInteriorRoadmap(graph, nodes, region, terminals, spacing, seed);
            ConnectTerminals(graph, nodes, region, terminals, approaches, seed);
            cancel.ThrowIfCancellationRequested();

            var netNodes = new HashSet<int>();
            var netEdges = new HashSet<long>();
            if (terminals == 1)
            {
                int anchor = SelectCoverageAnchor(nodes, terminals, netNodes, region, seed);
                if (anchor >= 0) AddPath(ShortestPath(0, new HashSet<int> { anchor }, graph, null), nodes, netNodes, netEdges, output);
            }
            else BuildTerminalMst(nodes, graph, terminals, netNodes, netEdges, output);

            for (int i = 0; i < loops; i++)
            {
                cancel.ThrowIfCancellationRequested();
                if (!AddCoverageLoop(nodes, terminals, graph, region, netNodes, netEdges, output, Hashing.Mix(seed, (uint)(i * 7919 + 3))))
                    break;
            }
            if (stretch) AddStretchLoop(nodes, graph, netNodes, netEdges, output);
            return ParkPathGraph.FromSegments(output, gates);
        }

        // ───────────── roadmap construction ─────────────

        private static List<Triangle> Triangulate(float2[] polygon, int start)
        {
            var result = new List<Triangle>();
            var remaining = new List<int>(polygon.Length);
            for (int i = 0; i < polygon.Length; i++) remaining.Add((start + i) % polygon.Length);
            int guard = polygon.Length * polygon.Length + 8;
            int cursor = 0;
            int sinceClip = 0;
            while (remaining.Count > 3 && guard-- > 0)
            {
                // Continue scanning from the last clip position: O(n²) overall instead of restarting at 0.
                if (cursor >= remaining.Count) cursor = 0;
                int prev = remaining[(cursor + remaining.Count - 1) % remaining.Count];
                int cur = remaining[cursor];
                int next = remaining[(cursor + 1) % remaining.Count];
                bool ear = Cross(polygon[prev], polygon[cur], polygon[next]) > 0.0001;
                if (ear)
                    foreach (int test in remaining)
                    {
                        if (test == prev || test == cur || test == next) continue;
                        if (PointInTriangle(polygon[test], polygon[prev], polygon[cur], polygon[next])) { ear = false; break; }
                    }
                if (ear)
                {
                    result.Add(new Triangle { A = prev, B = cur, C = next });
                    remaining.RemoveAt(cursor);
                    sinceClip = 0;
                    if (cursor > 0) cursor--;
                }
                else
                {
                    cursor++;
                    if (++sinceClip > remaining.Count) break;   // no ear left: give up gracefully
                }
            }
            if (remaining.Count == 3) result.Add(new Triangle { A = remaining[0], B = remaining[1], C = remaining[2] });
            return result;
        }

        /// <summary>
        /// Triangle-dual nodes come from a simplified outline: on a painted 400-vertex outline the raw
        /// triangulation adds one roadmap node per sliver along the edge, which tripled planning time
        /// without adding useful routes. Nodes are kept only when inside the real region.
        /// </summary>
        private static List<Triangle> Triangulate(LayerRegion region, int start)
        {
            var ring = region.Ring;
            float tol = math.clamp(math.sqrt(region.Area) / 150f, 0.5f, 3f);
            for (int attempt = 0; attempt < 6 && ring.Length > 120; attempt++, tol *= 1.6f)
            {
                var s = Geometry.Poly.Clean(FreeSpace.DouglasPeuckerClosed(region.Ring, tol), 0.05f);
                if (s.Length >= 3 && Geometry.Poly.IsSimple(s)) ring = s;
            }
            var tris = Triangulate(ring, start % ring.Length);
            foreach (var t in tris)
            {
                t.Centroid = (ring[t.A] + ring[t.B] + ring[t.C]) / 3f;
                t.Inside = region.Contains(t.Centroid);
                t.Clearance = t.Inside ? region.BoundaryDistance(t.Centroid) : 0;
            }
            return tris;
        }

        private static double SamplingSpacing(LayerRegion region)
        {
            var size = region.Max - region.Min;
            double spacing = Math.Max(Layout.M(16f), Math.Min(Layout.M(38f), Math.Sqrt(region.Area) / 5.0));
            double estimated = (double)size.x * size.y / (spacing * spacing);
            if (estimated > MaximumRoadmapNodes) spacing *= Math.Sqrt(estimated / MaximumRoadmapNodes);
            return spacing;
        }

        private static void AddInteriorSamples(List<Node> nodes, LayerRegion region, double spacing, int terminals, uint seed)
        {
            var rng = Hashing.Rng(seed, 0x2d2816feu);
            double phaseX = spacing * (0.25 + rng.NextDouble() * 0.5);
            double phaseY = spacing * (0.25 + rng.NextDouble() * 0.5);
            var near = new DiscIndex((float)spacing);
            int row = 0;
            for (double y = region.Min.y + phaseY; y < region.Max.y; y += spacing)
            {
                double offset = (row++ & 1) == 0 ? 0.0 : spacing * 0.5;
                for (double x = region.Min.x + phaseX + offset; x < region.Max.x; x += spacing)
                {
                    if (nodes.Count - terminals >= MaximumRoadmapNodes) return;
                    double jx = (rng.NextDouble() * 2.0 - 1.0) * spacing * 0.16;
                    double jy = (rng.NextDouble() * 2.0 - 1.0) * spacing * 0.16;
                    var p = new float2((float)(x + jx), (float)(y + jy));
                    if (!region.Contains(p)) continue;
                    double clearance = region.BoundaryDistance(p, (float)PreferredBoundaryClearance * 4f);
                    if (clearance < BoundaryClearance) continue;
                    if (near.Overlaps(p, (float)(spacing * 0.42), 0f)) continue;
                    near.Add(p, 0f, 0);
                    nodes.Add(new Node { Position = p, Clearance = clearance });
                }
            }
        }

        private static int[] AddGateApproachNodes(List<Node> nodes, LayerRegion region, int terminals, double spacing)
        {
            var result = new int[terminals];
            for (int t = 0; t < terminals; t++)
            {
                result[t] = -1;
                var inward = InwardNormal(nodes[t].Position, region);
                double preferred = Math.Max(Layout.M(8f, 4f), Math.Min(Layout.M(14f), spacing * 0.5));
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    double depth = preferred * (1.0 - attempt * 0.22);
                    var p = nodes[t].Position + inward * (float)depth;
                    if (!region.Contains(p) || !SegmentInside(nodes[t].Position, p, region, true)) continue;
                    result[t] = nodes.Count;
                    nodes.Add(new Node { Position = p, Clearance = region.BoundaryDistance(p), GateApproach = true });
                    break;
                }
            }
            return result;
        }

        private static void ConnectTriangleEdge(Dictionary<long, int> owners, int a, int b, int node, Graph graph,
            List<Node> nodes, LayerRegion region, uint seed)
        {
            if (node < 0) return;
            long key = Key(a, b);
            if (!owners.TryGetValue(key, out var neighbour)) { owners[key] = node; return; }
            // The simplified outline may cut a concave corner: verify the dual edge against the real one.
            if (SegmentInside(nodes[node].Position, nodes[neighbour].Position, region, false))
                Connect(graph, nodes, node, neighbour, false, seed);
        }

        private static void ConnectInteriorRoadmap(Graph graph, List<Node> nodes, LayerRegion region, int terminals,
            double spacing, uint seed)
        {
            double maxLink = spacing * 2.35;
            var index = new DiscIndex((float)maxLink);
            for (int i = terminals; i < nodes.Count; i++) index.Add(nodes[i].Position, 0f, i);
            var near = new List<int>();
            var candidates = new List<(double d, int i)>();
            for (int source = terminals; source < nodes.Count; source++)
            {
                index.Query(nodes[source].Position, (float)maxLink, near);
                candidates.Clear();
                foreach (int item in near)
                {
                    int target = index.Items[item].Kind;
                    if (target == source || graph.Has(source, target)) continue;
                    double d = Distance(nodes[source].Position, nodes[target].Position);
                    if (d > maxLink) continue;
                    candidates.Add((d, target));
                }
                candidates.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d) : x.i.CompareTo(y.i));
                int connected = 0;
                for (int k = 0; k < candidates.Count && connected < 6; k++)
                {
                    int target = candidates[k].i;
                    if (!SegmentInside(nodes[source].Position, nodes[target].Position, region, false)) continue;
                    Connect(graph, nodes, source, target, false, seed);
                    connected++;
                }
            }
        }

        private static void ConnectTerminals(Graph graph, List<Node> nodes, LayerRegion region, int terminals,
            int[] approaches, uint seed)
        {
            var candidates = new List<(double d, int i)>();
            for (int t = 0; t < terminals; t++)
            {
                if (approaches[t] >= 0) { Connect(graph, nodes, t, approaches[t], true, seed); continue; }
                var inward = InwardNormal(nodes[t].Position, region);
                candidates.Clear();
                for (int target = terminals; target < nodes.Count; target++)
                {
                    if (!InGateCone(nodes[t].Position, nodes[target].Position, inward)) continue;
                    candidates.Add((Distance(nodes[t].Position, nodes[target].Position), target));
                }
                candidates.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d) : x.i.CompareTo(y.i));
                int connected = 0;
                for (int k = 0; k < candidates.Count && connected < 5; k++)
                {
                    if (!SegmentInside(nodes[t].Position, nodes[candidates[k].i].Position, region, true)) continue;
                    Connect(graph, nodes, t, candidates[k].i, true, seed);
                    connected++;
                }
            }
        }

        private static void Connect(Graph graph, List<Node> nodes, int a, int b, bool gateConnector, uint seed)
        {
            double d = Distance(nodes[a].Position, nodes[b].Position);
            if (d <= 0.01) return;
            double cost = d;
            if (!gateConnector)
            {
                double clearance = Math.Min(nodes[a].Clearance, nodes[b].Clearance);
                double penalty = Math.Max(0.0, (PreferredBoundaryClearance - clearance) / (PreferredBoundaryClearance - BoundaryClearance));
                cost *= 1.0 + 2.0 * penalty * penalty;
            }
            cost *= SlopeFactor(nodes[a].Position, nodes[b].Position, d);
            cost *= EdgeVariation(seed, a, b, gateConnector ? 0.025 : 0.08);
            graph.Set(a, b, cost);
        }

        // ───────────── network selection ─────────────

        private static void BuildTerminalMst(List<Node> nodes, Graph graph, int terminals, HashSet<int> netNodes,
            HashSet<long> netEdges, List<float2> output)
        {
            var connected = new HashSet<int> { 0 };
            while (connected.Count < terminals)
            {
                Path best = null; int bestTerminal = -1;
                for (int t = 0; t < terminals; t++)
                {
                    if (connected.Contains(t)) continue;
                    // Connect to the whole network built so far, not only to terminals: a Steiner tree.
                    var targets = new HashSet<int>(connected);
                    targets.UnionWith(netNodes);
                    var path = ShortestPath(t, targets, graph, null);
                    if (!path.Found || best != null && path.Cost >= best.Cost) continue;
                    best = path; bestTerminal = t;
                }
                if (best == null) break;
                AddPath(best, nodes, netNodes, netEdges, output);
                connected.Add(bestTerminal);
            }
        }

        private static int SelectCoverageAnchor(List<Node> nodes, int terminals, HashSet<int> netNodes, LayerRegion region, uint seed, HashSet<int> tried = null)
        {
            int bestNode = -1; double bestScore = double.MinValue;
            var netList = new List<int>(netNodes);
            netList.Sort();
            for (int c = terminals; c < nodes.Count; c++)
            {
                if (netNodes.Contains(c) || nodes[c].GateApproach || tried != null && tried.Contains(c)) continue;
                double netDistance = double.MaxValue;
                if (netList.Count == 0)
                    for (int t = 0; t < terminals; t++) netDistance = Math.Min(netDistance, Distance(nodes[c].Position, nodes[t].Position));
                else
                    foreach (int n in netList) netDistance = Math.Min(netDistance, Distance(nodes[c].Position, nodes[n].Position));
                double clearance = nodes[c].Clearance;
                double score = (netDistance + Math.Min(clearance, 30.0) * 0.75) * EdgeVariation(seed, c, netList.Count + 7919, 0.08);
                if (score <= bestScore) continue;
                bestScore = score; bestNode = c;
            }
            return bestNode;
        }

        private static bool AddCoverageLoop(List<Node> nodes, int terminals, Graph graph, LayerRegion region,
            HashSet<int> netNodes, HashSet<long> netEdges, List<float2> output, uint seed)
        {
            if (netNodes.Count == 0) return false;
            var original = new HashSet<int>(netNodes);
            // The farthest anchor often cannot close a clean loop (separation / acute branches): try the next few.
            var tried = new HashSet<int>();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int anchor = SelectCoverageAnchor(nodes, terminals, original, region, seed, tried);
                if (anchor < 0) return false;
                tried.Add(anchor);
                if (TryCoverageLoop(anchor, nodes, graph, original, netNodes, netEdges, output)) return true;
            }
            return false;
        }

        private static bool TryCoverageLoop(int anchor, List<Node> nodes, Graph graph, HashSet<int> original,
            HashSet<int> netNodes, HashSet<long> netEdges, List<float2> output)
        {
            var first = ShortestPath(anchor, original, graph, null);
            if (!first.Found) return false;
            var blocked = new HashSet<long>();
            for (int i = 1; i < first.Nodes.Count; i++) blocked.Add(Key(first.Nodes[i - 1], first.Nodes[i]));
            var secondTargets = new HashSet<int>(original);
            secondTargets.Remove(first.Nodes[first.Nodes.Count - 1]);
            if (secondTargets.Count == 0) return false;
            var second = ShortestPath(anchor, secondTargets, graph, blocked);
            if (!second.Found) return false;
            // Validate the complete loop before committing either branch (no dangling spur).
            var staged = new HashSet<long>(netEdges);
            if (!TryStage(first, nodes, staged) || !TryStage(second, nodes, staged)) return false;
            AddPath(first, nodes, netNodes, netEdges, output);
            AddPath(second, nodes, netNodes, netEdges, output);
            return true;
        }

        /// <summary>
        /// Adds the roadmap edge between two network nodes whose walking distance over the network
        /// is most stretched relative to the direct distance (≥ 1.8×). One Dijkstra per network
        /// node on the small network graph replaces ParkManager's per-pair n×n matrix rebuild.
        /// </summary>
        private static void AddStretchLoop(List<Node> nodes, Graph graph, HashSet<int> netNodes, HashSet<long> netEdges,
            List<float2> output)
        {
            var netAdj = new Dictionary<int, List<(int to, double w)>>();
            foreach (long e in netEdges)
            {
                int a = (int)(e >> 32), b = (int)(e & 0xffffffffL);
                double w = Distance(nodes[a].Position, nodes[b].Position);
                if (!netAdj.TryGetValue(a, out var la)) netAdj[a] = la = new List<(int, double)>();
                if (!netAdj.TryGetValue(b, out var lb)) netAdj[b] = lb = new List<(int, double)>();
                la.Add((b, w)); lb.Add((a, w));
            }
            var ordered = new List<int>(netNodes);
            ordered.Sort();
            int bestA = -1, bestB = -1; double bestStretch = 1.8;
            foreach (int a in ordered)
            {
                Dictionary<int, double> dist = null;
                foreach (int b in graph.Adj[a])
                {
                    if (b <= a || !netNodes.Contains(b) || netEdges.Contains(Key(a, b))) continue;
                    dist = dist ?? NetworkDistances(a, netAdj);
                    if (!dist.TryGetValue(b, out var route)) continue;
                    double direct = Distance(nodes[a].Position, nodes[b].Position);
                    if (direct <= 0.01) continue;
                    double stretch = route / direct;
                    if (stretch <= bestStretch) continue;
                    bestStretch = stretch; bestA = a; bestB = b;
                }
            }
            if (bestA < 0) return;
            var path = new Path { Cost = graph.Weight(bestA, bestB) };
            path.Nodes.Add(bestA); path.Nodes.Add(bestB);
            var staged = new HashSet<long>(netEdges);
            if (!TryStage(path, nodes, staged)) return;
            AddPath(path, nodes, netNodes, netEdges, output);
        }

        private static Dictionary<int, double> NetworkDistances(int source, Dictionary<int, List<(int to, double w)>> adj)
        {
            var dist = new Dictionary<int, double> { [source] = 0 };
            var heap = new MinHeap();
            heap.Push(source, 0);
            while (heap.Count > 0)
            {
                heap.Pop(out int n, out double d);
                if (d > dist[n]) continue;
                if (!adj.TryGetValue(n, out var list)) continue;
                foreach (var (to, w) in list)
                {
                    double nd = d + w;
                    if (dist.TryGetValue(to, out var old) && old <= nd) continue;
                    dist[to] = nd; heap.Push(to, nd);
                }
            }
            return dist;
        }

        private static bool TryStage(Path path, List<Node> nodes, HashSet<long> staged)
        {
            if (path == null || !path.Found) return false;
            for (int i = 1; i < path.Nodes.Count; i++)
            {
                int a = path.Nodes[i - 1], b = path.Nodes[i];
                long key = Key(a, b);
                if (staged.Contains(key)) continue;
                if (!HasSeparation(a, b, nodes, staged)) return false;
                staged.Add(key);
            }
            return true;
        }

        private static bool HasSeparation(int a, int b, List<Node> nodes, HashSet<long> existing)
        {
            long self = Key(a, b);
            foreach (long edge in existing)
            {
                if (edge == self) continue;
                int c = (int)(edge >> 32), d = (int)(edge & 0xffffffffL);
                int shared = a == c || a == d ? a : b == c || b == d ? b : -1;
                if (shared >= 0)
                {
                    int newOther = shared == a ? b : a;
                    int oldOther = shared == c ? d : c;
                    if (AcuteBranch(nodes[shared].Position, nodes[newOther].Position, nodes[oldOther].Position)) return false;
                    continue;
                }
                if (SegmentDistance(nodes[a].Position, nodes[b].Position, nodes[c].Position, nodes[d].Position) < MinimumPathSeparation)
                    return false;
            }
            return true;
        }

        private static bool AcuteBranch(float2 centre, float2 a, float2 b)
        {
            var u = a - centre; var v = b - centre;
            double lu = Math.Sqrt((double)u.x * u.x + (double)u.y * u.y), lv = Math.Sqrt((double)v.x * v.x + (double)v.y * v.y);
            if (lu <= 0.0001 || lv <= 0.0001) return true;
            return (u.x * v.x + u.y * v.y) / (lu * lv) > MaximumAcuteBranchCosine;
        }

        private static double SegmentDistance(float2 a, float2 b, float2 c, float2 d)
        {
            if (ProperIntersect(a, b, c, d)) return 0.0;
            return Math.Min(Math.Min(PointSeg(a, c, d), PointSeg(b, c, d)), Math.Min(PointSeg(c, a, b), PointSeg(d, a, b)));
        }

        private static Path ShortestPath(int source, HashSet<int> targets, Graph graph, HashSet<long> blocked)
        {
            var result = new Path();
            if (targets == null || targets.Count == 0) return result;
            int n = graph.Count;
            var dist = new double[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
            dist[source] = 0;
            var heap = new MinHeap();
            heap.Push(source, 0);
            int destination = -1;
            while (heap.Count > 0)
            {
                heap.Pop(out int cur, out double d);
                if (done[cur] || d > dist[cur]) continue;
                done[cur] = true;
                if (cur != source && targets.Contains(cur)) { destination = cur; break; }
                var adj = graph.Adj[cur]; var weights = graph.W[cur];
                for (int k = 0; k < adj.Count; k++)
                {
                    int next = adj[k];
                    if (done[next]) continue;
                    if (blocked != null && blocked.Contains(Key(cur, next))) continue;
                    double nd = d + weights[k];
                    if (nd >= dist[next]) continue;
                    dist[next] = nd; prev[next] = cur; heap.Push(next, nd);
                }
            }
            if (destination < 0) return result;
            result.Cost = dist[destination];
            for (int c = destination; c >= 0; c = prev[c]) { result.Nodes.Add(c); if (c == source) break; }
            result.Nodes.Reverse();
            return result;
        }

        private static void AddPath(Path path, List<Node> nodes, HashSet<int> netNodes, HashSet<long> netEdges, List<float2> output)
        {
            if (path == null || !path.Found) return;
            for (int i = 1; i < path.Nodes.Count; i++)
            {
                int a = path.Nodes[i - 1], b = path.Nodes[i];
                if (netEdges.Add(Key(a, b))) { output.Add(nodes[a].Position); output.Add(nodes[b].Position); }
                netNodes.Add(a); netNodes.Add(b);
            }
        }

        // ───────────── geometry helpers ─────────────

        /// <summary>Inside when it crosses no outline edge and its midpoint is inside (or on the outline).</summary>
        private static bool SegmentInside(float2 a, float2 b, LayerRegion region, bool allowBoundaryStart)
        {
            // Samples catch the rare segment that leaves through an outline vertex (no proper crossing).
            for (int s = 1; s < 4; s++)
            {
                var p = math.lerp(a, b, s / 4f);
                if (!region.Contains(p) && region.BoundaryDistance(p, 0.03f) >= 0.02f) return false;
            }
            return !region.CrossesBoundary(a, b, allowBoundaryStart);
        }

        private static float2 InwardNormal(float2 gate, LayerRegion region)
        {
            region.ClosestOnBoundary(gate, out int seg, out _);
            return region.InwardNormal(seg);
        }

        private static bool InGateCone(float2 gate, float2 target, float2 inward)
        {
            var d = target - gate;
            double len = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y);
            if (len <= 0.0001) return false;
            return (d.x * inward.x + d.y * inward.y) / len >= MinimumGateDirectionCosine;
        }

        private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            const double eps = 0.0001;
            return Cross(a, b, p) >= -eps && Cross(b, c, p) >= -eps && Cross(c, a, p) >= -eps;
        }

        private static bool ProperIntersect(float2 a, float2 b, float2 c, float2 d)
        {
            const double eps = 0.0001;
            double abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
            return ((abC > eps && abD < -eps) || (abC < -eps && abD > eps)) && ((cdA > eps && cdB < -eps) || (cdA < -eps && cdB > eps));
        }

        private static double PointSeg(float2 p, float2 a, float2 b) => Math.Sqrt(SegmentIndex.DistSq(p, a, b));
        private static double Distance(float2 a, float2 b) { double x = (double)a.x - b.x, y = (double)a.y - b.y; return Math.Sqrt(x * x + y * y); }
        private static double Cross(float2 a, float2 b, float2 c) => ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);
        private static long Key(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);

        private static double EdgeVariation(uint seed, int a, int b, double amount)
        {
            uint h = Hashing.Mix(seed, Math.Min(a, b), Math.Max(a, b));
            return 1.0 + (Hashing.Unit(h) * 2.0 - 1.0) * amount;
        }

        /// <summary>Binary min-heap of (node, priority); stale entries are skipped by the caller.</summary>
        private sealed class MinHeap
        {
            private readonly List<(double p, int n)> _items = new List<(double, int)>();
            public int Count => _items.Count;
            public void Push(int n, double p)
            {
                _items.Add((p, n));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (Less(_items[parent], _items[i])) break;
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }
            public void Pop(out int n, out double p)
            {
                var top = _items[0];
                n = top.n; p = top.p;
                var last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count == 0) return;
                _items[0] = last;
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < _items.Count && Less(_items[l], _items[m])) m = l;
                    if (r < _items.Count && Less(_items[r], _items[m])) m = r;
                    if (m == i) break;
                    (_items[m], _items[i]) = (_items[i], _items[m]);
                    i = m;
                }
            }
            // Ties broken by node index so the result never depends on insertion history.
            private static bool Less((double p, int n) a, (double p, int n) b) => a.p < b.p || a.p == b.p && a.n < b.n;
        }
    }
}
