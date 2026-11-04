// Placemaker roads layer (s34) — the network design passes between "generated + connected to the city" and output:
//
//   merge junctions that crowd each other → access management on main roads → connectivity guarantee (islands linked
//   or dropped) → dead ends (stubs go, long ones become cul-de-sacs) → back lanes → square junctions → round bends →
//   gravel tips → walkways → grade check.
//
// Each pass is its own file (RoadAccess, RoadConnectivity, RoadWalkways, RoadTopology); this one wires them with the
// layer's settings and reports what happened (stats, issues, tests, gallery).

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadDesign
    {
        public sealed class Context
        {
            public float2[] Boundary = Array.Empty<float2>();
            public List<ExistingRoad> Existing = new List<ExistingRoad>();
            /// <summary>Existing roads the plan never joins nor crosses (highways, ramps, roads the player keeps free).</summary>
            public List<ExistingRoad> Barriers = new List<ExistingRoad>();
            public RoadObstacles Obstacles;
            public Func<float2, float> Height;
            public Func<NetRole, float> HalfWidth = r => 8f;
            public bool Perimeter;
            /// <summary>(s61) Corner rounding (m) for perimeter bends; negative = the road class default.</summary>
            public float PerimeterCorner = -1f;
            /// <summary>Junction spacing on main roads (m), 0 = off.</summary>
            public float JunctionSpacing = 120f;
            /// <summary>"Follow contours": streets steeper than the design grade go wherever the network stays connected.</summary>
            public bool FollowContours;
            /// <summary>Steepest road the design aims for (rise/run).</summary>
            public float MaxGrade = 0.1f;
            public float Walkways = 0.5f;
            public string WalkwayStyle = "paved";
            public float BackLanes;
            public string LaneStyle = "alley";
            public string TipStyle = "same";
            public uint Seed = 1;
            /// <summary>Loop-family layouts make their own collectors: links between pieces are streets there too.</summary>
            public NetRole LinkRole = NetRole.Street;
        }

        public sealed class Report
        {
            public int MergedJunctions, AccessCuts, Fillets, Squared, Lanes, GravelTips, SteepRemoved;
            /// <summary>(Smarter roads) Streets turned to meet an existing road square; streets cut back from one.</summary>
            public int SquaredAtExisting, CutAtExisting;
            /// <summary>(Smarter roads) Dead ends stopped where they turned steep.</summary>
            public int SteepTipsTrimmed;
            /// <summary>(Smarter roads) Pieces given up so that no two junctions are closer than 45 m.</summary>
            public int GivenUp;
            /// <summary>(s61) Perimeter ring closure: joins made, free ends left.</summary>
            public RoadPerimeter.Result PerimeterClose = new RoadPerimeter.Result();
            /// <summary>(s61) Where side streets were cut back from a main road (junction spacing), for the issue markers.</summary>
            public List<float3> AccessCutAt = new List<float3>();
            public RoadConnectivity.Result Connectivity = new RoadConnectivity.Result();
            /// <summary>(Smarter roads) The designed joins to the city (RoadCityLinks).</summary>
            public RoadCityLinks.Result CityLinks = new RoadCityLinks.Result();
            public RoadWalkways.Result Walkways = new RoadWalkways.Result();
            /// <summary>Car road runs steeper than the design grade: worst grade and where.</summary>
            public List<(float grade, float3 at)> Steep = new List<(float, float3)>();
            public float SteepestGrade;
        }

        public static Report Run(RoadNetwork net, Context c, CancellationToken cancel)
        {
            var r = new Report();
            if (net.Edges.Count == 0) return r;
            net.Weld();

            r.MergedJunctions = RoadTopology.MergeCloseJunctions(net, 18f);
            if (c.Perimeter) CloseRing(net, c, r);
            cancel.ThrowIfCancellationRequested();

            var co = new RoadConnectivity.Options
            {
                Boundary = c.Boundary, Existing = c.Existing, Barriers = c.Barriers, Obstacles = c.Obstacles, Height = c.Height, HalfWidth = c.HalfWidth,
                MaxGrade = c.MaxGrade, Perimeter = c.Perimeter, CanJoin = RoadAccess.JoinRule(net, c.JunctionSpacing),
                LinkRole = (a, b) => LinkRole(a, b, c.LinkRole), CityLinks = CityLinksFor(c.Boundary), CityLinkSpacing = 180f,
            };
            var trial = new RoadConnectivity.Options
            {
                Boundary = co.Boundary, Existing = co.Existing, Barriers = co.Barriers, Obstacles = co.Obstacles, Height = co.Height, HalfWidth = co.HalfWidth,
                MaxGrade = co.MaxGrade, Perimeter = co.Perimeter, CanJoin = co.CanJoin, LinkRole = co.LinkRole, Drop = false,
            };

            // (Smarter roads) The joins to the city, placed by design: in line with the streets inside, onto existing
            // junctions, square, clear of other junctions, never onto a highway. The connectivity pass only fills gaps.
            if (c.Existing.Count > 0)
            {
                r.CityLinks = RoadCityLinks.Plan(net, new RoadCityLinks.Options
                {
                    Boundary = c.Boundary, Existing = c.Existing, Barriers = c.Barriers, Obstacles = c.Obstacles, Height = c.Height,
                    HalfWidth = c.HalfWidth, MaxGrade = c.MaxGrade, JunctionSpacing = c.JunctionSpacing,
                });
                if (r.CityLinks.Links > 0) co.CityLinks = 1;
                cancel.ThrowIfCancellationRequested();
            }

            float capR = CapRadius(2f * c.HalfWidth(NetRole.Street));
            float setback = c.HalfWidth(NetRole.Avenue) + capR + 12f;
            var cuts = RoadAccess.Apply(net, c.JunctionSpacing, setback, 40f, n => RoadConnectivity.Ensure(n, trial));
            r.AccessCuts = cuts.Count;
            foreach (var cut in cuts) r.AccessCutAt.Add(cut.To);
            cancel.ThrowIfCancellationRequested();
            RoadConnectivity.Ensure(net, co, r.Connectivity);
            if (c.FollowContours && c.Height != null) r.SteepRemoved = PruneSteep(net, c.Height, c.MaxGrade);
            RoadTopology.RemoveHairpins(net);
            RoadConnectivity.CleanDeadEnds(net, co, r.Connectivity);
            // (Smarter roads) dead ends stop where the climb starts
            r.SteepTipsTrimmed = TrimSteepTips(net, c.Height, c.MaxGrade);
            if (c.Perimeter) CloseRing(net, c, r);
            // Removing stubs never splits the network, but a dead end that lost its cul-de-sac can leave a piece: check again.
            RoadConnectivity.Ensure(net, co, r.Connectivity);
            cancel.ThrowIfCancellationRequested();

            r.Lanes = RoadWalkways.Lanes(net, new RoadWalkways.LaneOptions
            {
                Density = c.BackLanes, Style = c.LaneStyle, Seed = c.Seed ^ 0x51A3u, Boundary = c.Boundary, Height = c.Height,
                Obstacles = c.Obstacles, HalfWidth = c.HalfWidth, MaxGrade = c.MaxGrade,
                WalkDensity = c.Walkways * 0.5f, WalkStyle = c.WalkwayStyle,
            });
            cancel.ThrowIfCancellationRequested();

            r.Squared = RoadTopology.SquareJunctions(net, c.Obstacles, c.HalfWidth);
            // (Smarter roads) ... and where they meet the existing roads (which never move)
            var sq = RoadTopology.SquareAtExisting(net, c.Existing, c.Obstacles, c.HalfWidth);
            r.SquaredAtExisting = sq.bent;
            r.CutAtExisting = sq.cut;
            if (sq.cut > 0) RoadConnectivity.CleanDeadEnds(net, co, r.Connectivity);
            r.Fillets = RoadTopology.FilletBends(net, c.HalfWidth, c.PerimeterCorner);
            // Crumbs of road (a trimmed bend next to a junction, a generator sliver) merge into their neighbours.
            net.CollapseShortEdges(8f);
            net.Weld();
            // (Smarter roads) The hard rule: no two junctions closer than 45 m. A crowding piece is given up, not squeezed in.
            r.GivenUp = RoadTopology.EnforceJunctionGap(net, c.Existing, c.HalfWidth, RoadTopology.MinJunctionGap, math.max(40f, c.JunctionSpacing * 0.5f)) + RoadTopology.GiveUpKnifeEdges(net, 40f, c.Existing);
            if (r.GivenUp > 0)
            {
                RoadConnectivity.CleanDeadEnds(net, co, r.Connectivity);
                net.Weld();
            }
            r.GravelTips = RoadWalkways.GravelTips(net, c.TipStyle, c.Seed ^ 0x6A09u);
            cancel.ThrowIfCancellationRequested();

            r.Walkways = RoadWalkways.Build(net, new RoadWalkways.Options
            {
                Density = c.Walkways, Style = c.WalkwayStyle, Seed = c.Seed ^ 0xBB67u, Boundary = c.Boundary, Height = c.Height,
                Obstacles = c.Obstacles, HalfWidth = c.HalfWidth, MaxGrade = math.min(RoadWalkways.MaxWalkGrade, c.MaxGrade),
                CapRadius = CapRadius, AccessCuts = cuts,
            });
            // nothing the passes added may cross a barrier (a walk over a highway, a lane over a ramp)
            if (RoadConnections.DropBarrierCrossings(net, c.Barriers) > 0) RoadConnectivity.Ensure(net, co, r.Connectivity);
            // (Smarter roads) a back lane or a walkway never crosses an existing road (it would make a junction on it)
            RoadConnections.DropBarrierCrossings(net, c.Existing, e => e.Walk || e.Role == NetRole.Alley || e.Role == NetRole.Pathway);
            RoadConnectivity.PruneLoosePaths(net);
            // ...and once more after everything that may still add a junction (city links of late dead ends)
            int late = RoadTopology.EnforceJunctionGap(net, c.Existing, c.HalfWidth, RoadTopology.MinJunctionGap, math.max(40f, c.JunctionSpacing * 0.5f)) + RoadTopology.GiveUpKnifeEdges(net, 40f, c.Existing);
            if (late > 0) { r.GivenUp += late; RoadConnectivity.PruneLoosePaths(net); }
            // a piece left floating on its own (no city, short) is no network: it goes
            if (net.DropFragments(150f) > 0) RoadConnectivity.PruneLoosePaths(net);
            r.Fillets += Tidy(net, c);
            cancel.ThrowIfCancellationRequested();

            Grades(net, c, r);
            return r;
        }

        /// <summary>
        /// The last tidy after any pass that gives roads up: a dead end stopping right beside a road it does not join is cut
        /// back or goes, every bare corner left (a street that lost its far side) is rounded, every dead end gets its turning
        /// circle. Returns the corners rounded.
        /// </summary>
        public static int Tidy(RoadNetwork net, Context c)
        {
            // anything added since (a late link onto the city) keeps the junction gap too
            if (RoadTopology.EnforceJunctionGap(net, c.Existing, c.HalfWidth, RoadTopology.MinJunctionGap, math.max(40f, c.JunctionSpacing * 0.5f))
                + RoadTopology.GiveUpKnifeEdges(net, 40f, c.Existing) > 0)
                RoadConnectivity.PruneLoosePaths(net);
            if (RoadTopology.GiveUpAlongside(net, c.HalfWidth, c.Existing) > 0) RoadConnectivity.PruneLoosePaths(net);
            RoadTopology.ExtendTips(net, c.HalfWidth, c.Existing, c.Barriers, c.Obstacles, c.Height, c.MaxGrade);
            if (RoadTopology.PruneStubsBesideRoads(net, c.HalfWidth, c.Existing, c.Barriers) > 0)
            {
                RoadConnectivity.PruneLoosePaths(net);
                // a street cut back from beside a road may still meet the road ahead of it cleanly
                RoadTopology.ExtendTips(net, c.HalfWidth, c.Existing, c.Barriers, c.Obstacles, c.Height, c.MaxGrade);
            }
            int fillets = RoadTopology.FilletBends(net, c.HalfWidth, c.PerimeterCorner, carOnly: true);
            // a short street turning back on itself is straightened or goes (then its neighbours' bends are rounded)
            if (RoadTopology.StraightenHooks(net, c.Existing, c.Barriers, c.Obstacles, c.HalfWidth) > 0)
            {
                RoadConnectivity.PruneLoosePaths(net);
                RoadTopology.PruneStubsBesideRoads(net, c.HalfWidth, c.Existing, c.Barriers);
                fillets += RoadTopology.FilletBends(net, c.HalfWidth, c.PerimeterCorner, carOnly: true);
            }
            RoadConnectivity.CapAllDeadEnds(net);
            return fillets;
        }

        private static void CloseRing(RoadNetwork net, Context c, Report r)
        {
            var res = RoadPerimeter.Close(net, c.Obstacles, c.Boundary, c.HalfWidth, c.Existing, c.Barriers);
            r.PerimeterClose.Closed += res.Closed;
            r.PerimeterClose.Joined += res.Joined;
            r.PerimeterClose.OpenAt.Clear();
            r.PerimeterClose.OpenAt.AddRange(res.OpenAt);
        }

        /// <summary>City connections wanted: one per ~450 m of outline (1–6), 180 m apart (CityLinkSpacing).</summary>
        private static int CityLinksFor(float2[] boundary)
        {
            if (boundary == null || boundary.Length < 3) return 1;
            return math.clamp((int)math.round(Geometry.Poly.Perimeter(boundary) / 450f), 1, 6);
        }

        public static float CapRadius(float roadWidth) => math.max(roadWidth * 0.8f, roadWidth * 0.5f + 4f);

        /// <summary>
        /// (s34) Follow contours: whole streets (junction to junction) that climb more than 15 % over the design grade are
        /// removed, steepest first, as long as the network stays in one piece — the contour streets stay, the links between
        /// them keep only the gentle ones. Main roads and the perimeter are never removed. Returns the count.
        /// </summary>
        private static int PruneSteep(RoadNetwork net, Func<float2, float> height, float maxGrade)
        {
            var inc = RoadGraph.Incidence(net, RoadGraph.Car);
            var chains = RoadWalkways.Chains(net, inc);
            var steep = new List<(float g, List<int> edges)>();
            foreach (var ch in chains)
            {
                var first = net.Edges[ch.edges[0]];
                if (first.Role == NetRole.Avenue || first.Role == NetRole.PerimeterRoad || first.Role == NetRole.Roundabout) continue;
                // Only links between junctions: a steep cul-de-sac still serves its houses (the grade check reports it).
                var last = net.Edges[ch.edges[ch.edges.Count - 1]];
                if (inc[first.N0].Count == 1 || inc[first.N1].Count == 1 || inc[last.N0].Count == 1 || inc[last.N1].Count == 1) continue;
                float g = 0f;
                foreach (int k in ch.edges) g = math.max(g, RoadGraph.MaxGrade(net.Edges[k].Curve, height, 16f, 4f));
                if (g > maxGrade * 1.15f) steep.Add((g, ch.edges));
            }
            steep.Sort((a, b) => b.g.CompareTo(a.g));
            int removed = 0;
            RoadGraph.Components(net, RoadGraph.Car, out int before);
            foreach (var (_, edges) in steep)
            {
                foreach (int k in edges) net.Edges[k].Removed = true;
                RoadGraph.Components(net, RoadGraph.Car, out int after);
                if (after > before)
                {
                    foreach (int k in edges) net.Edges[k].Removed = false;
                    continue;
                }
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// (Smarter roads) A dead end that turns steep towards its tip stops where the climb starts (a turning circle there):
        /// it still serves the gentle land and no car road climbs past 1.25× the design grade to serve a few lots. A dead end
        /// steep right from its junction and shorter than 40 m once trimmed goes. Returns the trimmed count.
        /// </summary>
        internal static int TrimSteepTips(RoadNetwork net, Func<float2, float> height, float maxGrade)
        {
            if (height == null) return 0;
            int trimmed = 0;
            for (int pass = 0; pass < 32; pass++)
            {
                var inc = RoadGraph.Incidence(net, RoadGraph.Car);
                bool any = false;
                for (int n = 0; n < net.Nodes.Count && !any; n++)
                {
                    if (inc[n].Count != 1 || RoadConnectivity.Attached(net.Nodes[n])) continue;
                    int ei = inc[n][0];
                    var e = net.Edges[ei];
                    if (e.Role == NetRole.Roundabout) continue;
                    // orient the tip edge junction → tip
                    var c = e.N1 == n ? e.Curve : e.Curve.Reversed();
                    float len = c.LengthXZ(12);
                    if (len < 30f || RoadGraph.MaxGrade(c, height, 16f, 4f) <= maxGrade * 1.25f) continue;
                    // walk from the junction end to the first steep window
                    float keep = 0f;
                    const float step = 4f;
                    for (float s = 0f; s + 16f <= len; s += step)
                    {
                        float t0 = RoadGraph.ParamAt(c, s), t1 = RoadGraph.ParamAt(c, s + 16f);
                        float2 a = c.Position(t0).xz, b = c.Position(t1).xz;
                        if (math.abs(height(b) - height(a)) / 16f > maxGrade * 1.25f) break;
                        keep = s + 16f;
                    }
                    if (keep >= len - 8f) continue;
                    if (keep < 40f)
                    {
                        // too steep from the start: the whole tip goes when it is a short spur (its junction stays a junction)
                        if (inc[RoadGraph.Other(e, n)].Count < 3 || len > 120f) continue;
                        e.Removed = true;
                    }
                    else
                    {
                        float t = RoadGraph.ParamAt(c, keep);
                        c.Split(math.clamp(t, 0.01f, 0.99f), out var kept, out _);
                        e.Curve = kept;
                        e.CapAtEnd = true;
                    }
                    net.Weld();
                    trimmed++;
                    any = true;
                }
                if (!any) break;
            }
            return trimmed;
        }

        private static NetRole LinkRole(RoadEdge a, RoadEdge b, NetRole fallback)
        {
            // A link continues the lesser of the two roads it joins (a local street stays local), never as a main road.
            NetRole ra = a?.Role ?? fallback, rb = b?.Role ?? fallback;
            if (ra == NetRole.CulDeSac || rb == NetRole.CulDeSac) return NetRole.CulDeSac;
            if (ra == NetRole.Alley && rb == NetRole.Alley) return NetRole.Alley;
            return fallback == NetRole.Avenue ? NetRole.Street : fallback;
        }

        /// <summary>Runs of car road steeper than the design grade (one entry per run, its steepest point).</summary>
        private static void Grades(RoadNetwork net, Context c, Report r)
        {
            if (c.Height == null) return;
            foreach (var e in net.Edges)
            {
                if (!RoadGraph.Car(e)) continue;
                float g = RoadGraph.MaxGrade(e.Curve, c.Height, 16f, 4f);
                r.SteepestGrade = math.max(r.SteepestGrade, g);
                if (g <= c.MaxGrade + 0.005f) continue;
                float3 at = e.Curve.Position(0.5f);
                at.y = c.Height(at.xz);
                r.Steep.Add((g, at));
            }
            r.Steep.Sort((a, b) => b.grade.CompareTo(a.grade));
        }
    }
}
