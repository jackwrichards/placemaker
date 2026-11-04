// Placemaker roads layer — rates a finished road plan the way a road designer would, as numbers the planner can
// compare and a person can read. Six parts, each 0–100, and a total:
//
//   Grades     how steep the roads are: the steepest 16 m window of every piece, the share of road over the design grade.
//   Junctions  angles at every junction (with the existing roads it joins), junctions crowding each other.
//   Segments   crumbs of road, junction-to-junction pieces too short to be a block, kinks (bends with no curve), hooks,
//              bare dead ends, sliver blocks.
//   Perimeter  how closely the new roads hug the selected outline (mean and worst gap, the corners).
//   Links      the joins to the city: never onto a highway or ramp, square to the road they meet, in line with a
//              street inside, on an existing junction or well clear of one.
//   Fit        living with the existing network: no crossing without a junction, no road laid on top of one, no road
//              crowding alongside a spine, no small road continued as a bigger one, the junction spacing on big roads.
//
// Works on the plan's nets (what the game builds) and the site snapshot only. Marks says where each problem is, for the
// harness pictures (Tests/Placemaker.RoadLab) and the tests.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    public sealed class RoadsRating
    {
        public sealed class Options
        {
            /// <summary>Design grade (rise/run): a piece steeper than this is steep.</summary>
            public float MaxGrade = 0.1f;
            /// <summary>Junction spacing wanted on big roads (m).</summary>
            public float JunctionSpacing = 120f;
            /// <summary>The plan is expected to run a road along the outline (drawn / painted areas).</summary>
            public bool Perimeter = true;
            /// <summary>Ground the roads lie on (null: the plan's HeightAfter, else the site's).</summary>
            public Func<float2, float> Height;
        }

        /// <summary>A problem (or a good link) at a place, for the pictures.</summary>
        public struct Mark
        {
            public string Kind;
            public float2 At;
            public float Value;
            public Mark(string kind, float2 at, float value = 0f) { Kind = kind; At = at; Value = value; }
        }

        // ── scores (0..100) ──
        public float Grades = 100f, Junctions = 100f, Segments = 100f, Perimeter = 100f, Links = 100f, Fit = 100f, Directness = 100f, Total = 100f;
        /// <summary>Mean road distance / straight distance between far-apart points of the network (1 = straight there).</summary>
        public float DetourMean = 1f;

        // ── raw numbers ──
        public int CarNets;
        public float RoadLength;
        public float SteepestGrade, MeanGrade, SteepShare;
        public int SteepPieces, VerySteepPieces;
        public int JunctionCount, AwkwardJunctions, AcuteJunctions, Overloaded, CrowdedJunctions;
        public float MinJunctionAngle = 180f;
        public int Crumbs, ShortLinks, Kinks, Hooks, BareDeadEnds, Slivers;
        /// <summary>Dead ends stopping within 30 m of another road they do not join (a stub beside a road).</summary>
        public int StrayStubs;
        /// <summary>Streets of ours running alongside another road with no room for a building row between.</summary>
        public int Alongside;
        /// <summary>Two of our streets crossing with no junction where they cross (they would merge into one in the game).</summary>
        public int SelfCrossings;
        public bool HasPerimeter;
        public float OutlineMeanGap, OutlineMaxGap, CornerMeanGap, CornerMaxGap, OutlineCovered = 1f;
        public bool CityNear;
        public int CityLinks, HighwayLinks, SkewLinks, LinksOnJunction, LinksAligned, LinksCrowded;
        public float LinkMeanLength;
        public int HighwayCrossings, Crossings, DoubledPieces, HierarchyBreaks, AccessBreaks, SpineCrowding;
        public float DoubledLength;
        public readonly List<Mark> Marks = new List<Mark>();

        public const string MarkSteep = "steep", MarkAwkward = "awkward", MarkAcute = "acute", MarkCrowded = "crowded", MarkCrumb = "crumb",
            MarkShort = "short", MarkKink = "kink", MarkHook = "hook", MarkDeadEnd = "deadEnd", MarkSliver = "sliver", MarkCorner = "corner",
            MarkGap = "gap", MarkStub = "stub", MarkAlongside = "alongside", MarkSelfCross = "selfCross", MarkHighwayLink = "highwayLink", MarkSkewLink = "skewLink", MarkGoodLink = "goodLink", MarkCrowdedLink = "crowdedLink",
            MarkHighwayCross = "highwayCross", MarkCross = "cross", MarkDoubled = "doubled", MarkHierarchy = "hierarchy", MarkAccess = "access",
            MarkSpine = "spineCrowd";

        public override string ToString()
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendFormat(c, "total {0:0} | grades {1:0} junctions {2:0} segments {3:0} perimeter {4:0} links {5:0} fit {6:0} directness {7:0}",
                Total, Grades, Junctions, Segments, Perimeter, Links, Fit, Directness);
            return sb.ToString();
        }

        /// <summary>The raw numbers as name → value (harness tables, baselines).</summary>
        public Dictionary<string, double> Numbers()
        {
            return new Dictionary<string, double>
            {
                ["total"] = Total, ["grades"] = Grades, ["junctions"] = Junctions, ["segments"] = Segments, ["perimeter"] = Perimeter,
                ["links"] = Links, ["fit"] = Fit, ["directness"] = Directness, ["detourMean"] = DetourMean,
                ["roadLength"] = RoadLength, ["carNets"] = CarNets,
                ["steepestGrade"] = SteepestGrade * 100f, ["meanGrade"] = MeanGrade * 100f, ["steepShare"] = SteepShare * 100f,
                ["steepPieces"] = SteepPieces, ["verySteepPieces"] = VerySteepPieces,
                ["junctionCount"] = JunctionCount, ["minJunctionAngle"] = MinJunctionAngle, ["awkwardJunctions"] = AwkwardJunctions,
                ["acuteJunctions"] = AcuteJunctions, ["overloaded"] = Overloaded, ["crowdedJunctions"] = CrowdedJunctions,
                ["crumbs"] = Crumbs, ["shortLinks"] = ShortLinks, ["kinks"] = Kinks, ["hooks"] = Hooks, ["bareDeadEnds"] = BareDeadEnds, ["slivers"] = Slivers,
                ["strayStubs"] = StrayStubs, ["alongside"] = Alongside, ["selfCrossings"] = SelfCrossings,
                ["outlineMeanGap"] = OutlineMeanGap, ["outlineMaxGap"] = OutlineMaxGap, ["cornerMeanGap"] = CornerMeanGap, ["cornerMaxGap"] = CornerMaxGap,
                ["outlineCovered"] = OutlineCovered * 100f,
                ["cityLinks"] = CityLinks, ["highwayLinks"] = HighwayLinks, ["skewLinks"] = SkewLinks, ["linksOnJunction"] = LinksOnJunction,
                ["linksAligned"] = LinksAligned, ["linksCrowded"] = LinksCrowded, ["linkMeanLength"] = LinkMeanLength,
                ["highwayCrossings"] = HighwayCrossings, ["crossings"] = Crossings, ["doubledPieces"] = DoubledPieces, ["doubledLength"] = DoubledLength,
                ["hierarchyBreaks"] = HierarchyBreaks, ["accessBreaks"] = AccessBreaks, ["spineCrowding"] = SpineCrowding,
            };
        }

        // ───────────────────────────── measuring ─────────────────────────────

        private sealed class NNode
        {
            public float2 P;
            public readonly List<(float2 dir, int net)> Out = new List<(float2, int)>();
            /// <summary>Existing roads meeting here (directions leaving), filled for nodes on an existing road.</summary>
            public readonly List<float2> Existing = new List<float2>();
            public int ExistingRoad = -1;
            public bool OnExistingNode;
            public int ExistingDegree;
            public bool Attached;
            public int Degree => Out.Count + Existing.Count;
        }

        private sealed class NNet
        {
            public RoadCurve Curve;
            public NetRole Role;
            public int A, D;
            public float Length;
            /// <summary>How far the piece turns along its length (degrees).</summary>
            public float Turning;
        }

        private sealed class XRoad
        {
            public RoadCurve Curve;
            public float HalfWidth;
            public RoadRank Rank;
            public long Id;
            public List<float2> Line = new List<float2>();
        }

        private sealed class XNode
        {
            public float2 P;
            public readonly List<(float2 dir, int road)> Out = new List<(float2, int)>();
        }

        private static bool IsWalk(NetRole r) => r == NetRole.Pathway || r == NetRole.PathwayWide;

        /// <summary>Half width of a planned role as the automatic picks build it (Two-Lane 16 m, Four-Lane 24 m, Alley 8 m).</summary>
        public static float PlannedHalfWidth(NetRole role)
        {
            switch (role)
            {
                case NetRole.Avenue: return 12f;
                case NetRole.Alley: return 4f;
                case NetRole.Pathway: case NetRole.PathwayWide: return 2f;
                default: return 8f;
            }
        }

        public static RoadsRating Measure(AreaPlan plan, float2[] outline, SiteContext site, Options o = null)
        {
            o = o ?? new Options();
            var r = new RoadsRating();
            if (plan == null) return r;
            site = site ?? new SiteContext();
            outline = outline ?? Array.Empty<float2>();
            Func<float2, float> height = o.Height ?? plan.HeightAfter ?? site.HeightAt;

            // Existing roads and their nodes.
            var roads = new List<XRoad>();
            if (site.NearbyNets != null)
                foreach (var n in site.NearbyNets)
                {
                    if (n.Category != NetCategory.Road) continue;
                    if (!MathEx.Finite(n.A) || !MathEx.Finite(n.D)) continue;
                    var x = new XRoad
                    {
                        Curve = new RoadCurve(n.A, n.B, n.C, n.D), Rank = RoadClasses.Rank(n), Id = n.ExternalId,
                        HalfWidth = n.Width > 0f && math.isfinite(n.Width) ? n.Width * 0.5f : 8f,
                    };
                    var pts = new List<float3>();
                    x.Curve.Flatten(pts, null, 4f, 5f);
                    foreach (var p in pts) x.Line.Add(p.xz);
                    roads.Add(x);
                }
            var xnodes = new List<XNode>();
            int XNodeAt(float2 p, float tol)
            {
                for (int i = 0; i < xnodes.Count; i++) if (math.distance(xnodes[i].P, p) <= tol) return i;
                return -1;
            }
            for (int i = 0; i < roads.Count; i++)
            {
                foreach (bool start in new[] { true, false })
                {
                    float2 p = start ? roads[i].Curve.A.xz : roads[i].Curve.D.xz;
                    int k = XNodeAt(p, 1f);
                    if (k < 0) { xnodes.Add(new XNode { P = p }); k = xnodes.Count - 1; }
                    xnodes[k].Out.Add((start ? roads[i].Curve.TangentXZ(0f) : -roads[i].Curve.TangentXZ(1f), i));
                }
            }

            // Planned car nets and their nodes.
            var nodes = new List<NNode>();
            var keyed = new Dictionary<long, int>();
            int NodeOf(float3 p)
            {
                long k = ((long)math.round(p.x * 20f) << 32) ^ (uint)(int)math.round(p.z * 20f);
                if (!keyed.TryGetValue(k, out int id)) { id = nodes.Count; keyed[k] = id; nodes.Add(new NNode { P = p.xz }); }
                return id;
            }
            var nets = new List<NNet>();
            foreach (var n in plan.Nets)
            {
                if (n == null || IsWalk(n.Role) || n.Role == NetRole.Fence || n.Category == AssetCategories.NetQuay) continue;
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                var e = new NNet { Curve = c, Role = n.Role, A = NodeOf(n.A), D = NodeOf(n.D), Length = c.LengthXZ(12) };
                nets.Add(e);
                nodes[e.A].Out.Add((c.TangentXZ(0f), nets.Count - 1));
                nodes[e.D].Out.Add((-c.TangentXZ(1f), nets.Count - 1));
                if (n.StartAttach >= 0) nodes[e.A].Attached = true;
                if (n.EndAttach >= 0) nodes[e.D].Attached = true;
            }
            r.CarNets = nets.Count;
            foreach (var e in nets) r.RoadLength += e.Length;

            // Which planned nodes sit on an existing road (geometry decides, the attach ids only confirm).
            foreach (var nd in nodes)
            {
                int xn = XNodeAt(nd.P, 1f);
                if (xn >= 0)
                {
                    nd.OnExistingNode = true;
                    nd.ExistingDegree = xnodes[xn].Out.Count;
                    nd.ExistingRoad = xnodes[xn].Out[0].road;
                    foreach (var (dir, _) in xnodes[xn].Out) nd.Existing.Add(dir);
                    nd.Attached = true;
                    continue;
                }
                float best = 1f;
                for (int i = 0; i < roads.Count; i++)
                {
                    float d = roads[i].Curve.DistanceXZ(nd.P, out float t);
                    if (d >= best) continue;
                    best = d;
                    nd.ExistingRoad = i;
                    nd.Existing.Clear();
                    float2 tan = roads[i].Curve.TangentXZ(t);
                    nd.Existing.Add(tan);
                    nd.Existing.Add(-tan);
                    nd.ExistingDegree = 2;
                    nd.Attached = true;
                }
            }

            MeasureGrades(r, nets, height, o);
            var junctions = MeasureJunctions(r, nodes, xnodes, nets, roads);
            MeasureSegments(r, plan, nets, nodes, roads, outline, o);
            MeasurePerimeter(r, nets, roads, outline, o);
            MeasureLinks(r, nets, nodes, roads, xnodes, outline, o);
            MeasureFit(r, nets, nodes, roads, xnodes, outline, o, junctions);
            MeasureDirectness(r, nets, nodes);
            Score(r, o);
            return r;
        }

        private static void MeasureGrades(RoadsRating r, List<NNet> nets, Func<float2, float> height, Options o)
        {
            if (height == null || nets.Count == 0) return;
            float steepLen = 0f, weighted = 0f;
            foreach (var e in nets)
            {
                float g = RoadGraph.MaxGrade(e.Curve, height, 16f, 4f);
                r.SteepestGrade = math.max(r.SteepestGrade, g);
                weighted += RoadGraph.MeanGrade(e.Curve, height) * e.Length;
                if (g > o.MaxGrade + 0.005f)
                {
                    r.SteepPieces++;
                    steepLen += e.Length;
                    if (g > o.MaxGrade * 1.5f) r.VerySteepPieces++;
                    r.Marks.Add(new Mark(MarkSteep, e.Curve.Position(0.5f).xz, g));
                }
            }
            r.SteepShare = r.RoadLength > 1f ? steepLen / r.RoadLength : 0f;
            r.MeanGrade = r.RoadLength > 1f ? weighted / r.RoadLength : 0f;
        }

        private static List<float2> MeasureJunctions(RoadsRating r, List<NNode> nodes, List<XNode> xnodes, List<NNet> nets, List<XRoad> roads)
        {
            var at = new List<float2>();
            var arms = new List<List<float2>>();
            var atNode = new List<int>();
            for (int ni = 0; ni < nodes.Count; ni++)
            {
                var nd = nodes[ni];
                if (nd.Out.Count == 0 || nd.Degree < 3) continue;
                atNode.Add(ni);
                var a = new List<float2>(nd.Existing);
                foreach (var (d, _) in nd.Out) a.Add(d);
                arms.Add(a);
                var dirs = new List<float>();
                foreach (var (d, _) in nd.Out) dirs.Add(math.atan2(d.y, d.x));
                foreach (var d in nd.Existing) dirs.Add(math.atan2(d.y, d.x));
                dirs.Sort();
                float worst = 360f;
                for (int i = 0; i < dirs.Count; i++)
                {
                    float gap = i + 1 < dirs.Count ? dirs[i + 1] - dirs[i] : dirs[0] + 2f * math.PI - dirs[i];
                    worst = math.min(worst, math.degrees(gap));
                }
                r.JunctionCount++;
                at.Add(nd.P);
                r.MinJunctionAngle = math.min(r.MinJunctionAngle, worst);
                if (worst < 40f) { r.AcuteJunctions++; r.Marks.Add(new Mark(MarkAcute, nd.P, worst)); }
                else if (worst < 60f) { r.AwkwardJunctions++; r.Marks.Add(new Mark(MarkAwkward, nd.P, worst)); }
                if (nd.Degree >= 5) r.Overloaded++;
            }
            // Junctions crowding each other: two of ours (or ours and an existing junction) closer than 45 m along one road
            // (the hard rule), or a connecting street under 30 m; only with road between them (two junctions on parallel roads
            // across a strip of land do not crowd each other).
            float OffRoad(float2 p)
            {
                float best = float.MaxValue;
                foreach (var e in nets) best = math.min(best, e.Curve.DistanceXZ(p, out _) - PlannedHalfWidth(e.Role));
                foreach (var x in roads) best = math.min(best, x.Curve.DistanceXZ(p, out _) - x.HalfWidth);
                return best;
            }
            var all = new List<float2>(at);
            var allArms = new List<List<float2>>(arms);
            foreach (var x in xnodes)
                if (x.Out.Count >= 3)
                {
                    all.Add(x.P);
                    var a = new List<float2>();
                    foreach (var (d, _) in x.Out) a.Add(d);
                    allArms.Add(a);
                }
            for (int i = 0; i < at.Count; i++)
            {
                float worst = 0f, nearest = 0f;
                for (int j = 0; j < all.Count; j++)
                {
                    if (j == i) continue;
                    float d = math.distance(at[i], all[j]);
                    if (d <= 0.5f || d >= RoadTopology.MinJunctionGap) continue;
                    // two junctions of ours keep the full gap; against a city junction a connecting street needs the rung
                    // gap, and so does a link onto a city road (its two junctions set by the selection and the city)
                    float need = j < at.Count ? RoadTopology.MinJunctionGap : RoadTopology.GapNeeded(at[i], arms[i], all[j], allArms[j]);
                    if (j < at.Count && (nodes[atNode[i]].Attached || nodes[atNode[j]].Attached))
                        foreach (var (_, k) in nodes[atNode[i]].Out)
                            if (nets[k].A == atNode[j] || nets[k].D == atNode[j]) need = math.min(need, RoadTopology.RungGap);
                    if (d < need - RoadTopology.GapSlack && need - d > worst && RoadTopology.RoadBetween(at[i], all[j], OffRoad)) { worst = need - d; nearest = d; }
                }
                if (worst > 0f) { r.CrowdedJunctions++; r.Marks.Add(new Mark(MarkCrowded, at[i], nearest)); }
            }
            return at;
        }

        private static void MeasureSegments(RoadsRating r, AreaPlan plan, List<NNet> nets, List<NNode> nodes, List<XRoad> roads, float2[] outline, Options o)
        {
            var caps = new List<float2>();
            foreach (var ob in plan.Objects)
                if (ob != null && ob.Role == ObjectRole.CulDeSacCap) caps.Add(ob.Position.xz);
            foreach (var e in nets)
            {
                if (e.Length < 8f) { r.Crumbs++; r.Marks.Add(new Mark(MarkCrumb, e.Curve.Position(0.5f).xz, e.Length)); }
                // total turning along the piece
                float total = 0f;
                float2 prev = e.Curve.TangentXZ(0f);
                for (int i = 1; i <= 16; i++)
                {
                    float2 d = e.Curve.TangentXZ(i / 16f);
                    total += RoadGraph.Angle(prev, d);
                    prev = d;
                }
                e.Turning = total;
            }
            // Hooks: a short street (junction to junction, under 150 m) turning back on itself, bends and corners together.
            {
                bool RealH(int n) => nodes[n].Degree != 2 || nodes[n].Attached;
                var seenH = new HashSet<int>();
                for (int s = 0; s < nodes.Count; s++)
                {
                    if (nodes[s].Out.Count == 0 || !RealH(s)) continue;
                    foreach (var (_, start) in nodes[s].Out)
                    {
                        if (seenH.Contains(start)) continue;
                        float len = 0f, turn = 0f;
                        int cur = s, ei = start, t = -1;
                        bool ring = false, winding = true;
                        float2 mid = nets[start].Curve.Position(0.5f).xz;
                        for (int guard = 0; guard < 64; guard++)
                        {
                            seenH.Add(ei);
                            len += nets[ei].Length;
                            turn += nets[ei].Turning;
                            ring |= nets[ei].Role == NetRole.Roundabout;
                            winding &= nets[ei].Role == NetRole.CulDeSac;
                            int nxt = nets[ei].A == cur ? nets[ei].D : nets[ei].A;
                            if (RealH(nxt) || nodes[nxt].Out.Count != 2) { t = nxt; break; }
                            int next = nodes[nxt].Out[0].net == ei ? nodes[nxt].Out[1].net : nodes[nxt].Out[0].net;
                            float2 dIn = nodes[nxt].Out[0].net == ei ? nodes[nxt].Out[0].dir : nodes[nxt].Out[1].dir;
                            float2 dOut = nodes[nxt].Out[0].net == next ? nodes[nxt].Out[0].dir : nodes[nxt].Out[1].dir;
                            turn += 180f - RoadGraph.Angle(dIn, dOut);
                            if (turn > RoadTopology.HookTurn) mid = nodes[nxt].P;
                            cur = nxt; ei = next;
                        }
                        // (the winding lanes of the organic styles, cul-de-sac roads, may curl as they like)
                        if (t < 0 || ring || winding || len > RoadTopology.HookChain || turn <= RoadTopology.HookTurn) continue;
                        r.Hooks++;
                        r.Marks.Add(new Mark(MarkHook, mid, turn));
                    }
                }
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                var nd = nodes[i];
                if (nd.Out.Count == 2 && nd.Existing.Count == 0)
                {
                    float a = RoadGraph.Angle(nd.Out[0].dir, nd.Out[1].dir);
                    // a bend with no curve: the game draws a clipped corner
                    if (a < 155f && nets[nd.Out[0].net].Role != NetRole.Roundabout) { r.Kinks++; r.Marks.Add(new Mark(MarkKink, nd.P, 180f - a)); }
                }
                if (nd.Out.Count == 1 && !nd.Attached)
                {
                    bool capped = false;
                    foreach (var c in caps) if (math.distance(c, nd.P) < 1f) { capped = true; break; }
                    if (!capped) { r.BareDeadEnds++; r.Marks.Add(new Mark(MarkDeadEnd, nd.P)); }   // a street stopping at the edge with nothing there too
                }
            }
            // Two of our streets crossing with no junction where they cross.
            {
                var lines = new List<List<float2>>(nets.Count);
                foreach (var e in nets) lines.Add(RoadGraph.Sample(e.Curve, 3f));
                for (int i = 0; i < nets.Count; i++)
                    for (int k = i + 1; k < nets.Count; k++)
                    {
                        var a = nets[i]; var b = nets[k];
                        if (a.A == b.A || a.A == b.D || a.D == b.A || a.D == b.D) continue;
                        bool hit = false;
                        for (int p = 0; p + 1 < lines[i].Count && !hit; p++)
                            for (int q = 0; q + 1 < lines[k].Count && !hit; q++)
                                if (Seg.Intersect(lines[i][p], lines[i][p + 1], lines[k][q], lines[k][q + 1], out float ta, out float tb)
                                    && ta > 0.001f && ta < 0.999f && tb > 0.001f && tb < 0.999f)
                                {
                                    hit = true;
                                    r.SelfCrossings++;
                                    r.Marks.Add(new Mark(MarkSelfCross, math.lerp(lines[i][p], lines[i][p + 1], ta)));
                                }
                    }
            }
            // Streets running alongside another road with no room for a building row between (not the ring or a main road).
            for (int i = 0; i < nets.Count; i++)
            {
                var e = nets[i];
                if (e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue || e.Role == NetRole.Roundabout || e.Length < 20f) continue;
                float Gap(float2 p, float2 tan)
                {
                    float best = float.MaxValue;
                    for (int k = 0; k < nets.Count; k++)
                    {
                        var o = nets[k];
                        if (k == i || o.A == e.A || o.A == e.D || o.D == e.A || o.D == e.D) continue;
                        float d = o.Curve.DistanceXZ(p, out float t);
                        if (t < 0.01f || t > 0.99f) continue;   // end on: the same street going on, not one beside it
                        if (math.abs(math.dot(o.Curve.TangentXZ(t), tan)) < 0.85f) continue;
                        best = math.min(best, d - PlannedHalfWidth(o.Role));
                    }
                    foreach (var x in roads)
                    {
                        float d = x.Curve.DistanceXZ(p, out float t);
                        if (t < 0.01f || t > 0.99f) continue;
                        if (math.abs(math.dot(x.Curve.TangentXZ(t), tan)) < 0.85f) continue;
                        best = math.min(best, d - x.HalfWidth);
                    }
                    return best;
                }
                if (RoadTopology.RunsAlongside(e.Curve, PlannedHalfWidth(e.Role), Gap)) { r.Alongside++; r.Marks.Add(new Mark(MarkAlongside, e.Curve.Position(0.5f).xz)); }
            }
            // Dead ends stopping right beside another road (ours or the city's) they do not join: a stub left over.
            for (int i = 0; i < nodes.Count; i++)
            {
                var nd = nodes[i];
                if (nd.Out.Count != 1 || nd.Attached || nd.Existing.Count > 0 || IsWalk(nets[nd.Out[0].net].Role)) continue;
                // the tip's own chain (back to its junction) does not count
                var own = new HashSet<int>();
                int cur = i, ei = nd.Out[0].net;
                for (int guard = 0; guard < 64; guard++)
                {
                    own.Add(ei);
                    int nxt = nets[ei].A == cur ? nets[ei].D : nets[ei].A;
                    if (nodes[nxt].Out.Count != 2) { foreach (var (_, k) in nodes[nxt].Out) own.Add(k); break; }
                    int next = nodes[nxt].Out[0].net == ei ? nodes[nxt].Out[1].net : nodes[nxt].Out[0].net;
                    cur = nxt; ei = next;
                }
                float near = float.MaxValue;
                for (int k = 0; k < nets.Count; k++)
                {
                    if (own.Contains(k)) continue;
                    if (IsWalk(nets[k].Role)) continue;
                    near = math.min(near, nets[k].Curve.DistanceXZ(nd.P, out _) - PlannedHalfWidth(nets[k].Role));
                }
                foreach (var x in roads) near = math.min(near, x.Curve.DistanceXZ(nd.P, out _) - x.HalfWidth);
                if (near < 30f - 8f) { r.StrayStubs++; r.Marks.Add(new Mark(MarkStub, nd.P, near)); }
            }
            // Junction to junction pieces too short to hold a building row (chains through plain bends).
            bool Real(int n) => nodes[n].Degree != 2 || nodes[n].Attached;
            var seen = new HashSet<int>();
            for (int s = 0; s < nodes.Count; s++)
            {
                if (!Real(s) || nodes[s].Degree < 3) continue;
                foreach (var (_, start) in nodes[s].Out)
                {
                    if (seen.Contains(start)) continue;
                    float len = 0f;
                    int cur = s, ei = start, guard = 0;
                    var chain = new List<int>();
                    while (guard++ < 200)
                    {
                        chain.Add(ei);
                        len += nets[ei].Length;
                        int nxt = nets[ei].A == cur ? nets[ei].D : nets[ei].A;
                        if (Real(nxt) || nodes[nxt].Out.Count != 2) { cur = nxt; break; }
                        int next = nodes[nxt].Out[0].net == ei ? nodes[nxt].Out[1].net : nodes[nxt].Out[0].net;
                        cur = nxt; ei = next;
                    }
                    foreach (int k in chain) seen.Add(k);
                    if (nodes[cur].Degree >= 3 && cur != s && len < 25f && nets[start].Role != NetRole.Roundabout)
                    {
                        r.ShortLinks++;
                        r.Marks.Add(new Mark(MarkShort, math.lerp(nodes[s].P, nodes[cur].P, 0.5f), len));
                    }
                }
            }
            foreach (var reg in plan.Regions)
            {
                if (reg?.Ring == null || reg.Ring.Length < 3 || Array.IndexOf(reg.Tags, RegionTags.Block) < 0) continue;
                if (Array.IndexOf(reg.Tags, RegionTags.Green) >= 0) continue;   // a planned pocket green (a diagonal's triangle), not a leftover
                var ring = Poly.To2D(reg.Ring);
                float area = Poly.Area(ring), per = Poly.Perimeter(ring);
                if (per <= 1f) continue;
                float width = 2f * area / per;   // mean width of a thin shape
                if (RoadsPlanner.IsSliver(ring)) { r.Slivers++; r.Marks.Add(new Mark(MarkSliver, Poly.Centroid(ring), area)); }
            }
        }

        private static void MeasurePerimeter(RoadsRating r, List<NNet> nets, List<XRoad> roads, float2[] outline, Options o)
        {
            r.HasPerimeter = o.Perimeter && outline.Length >= 3;
            if (!r.HasPerimeter || nets.Count == 0) return;
            var index = new SegmentIndex(24f);
            foreach (var e in nets)
            {
                var pts = new List<float3>();
                e.Curve.Flatten(pts, null, 3f, 4f);
                for (int i = 0; i + 1 < pts.Count; i++) index.Add(pts[i].xz, pts[i + 1].xz, 0);
            }
            var scratch = new List<int>();
            var seenIds = new HashSet<int>();
            float Gap(float2 p)
            {
                float best = 60f;
                index.Query(p - 60f, p + 60f, scratch, seenIds);
                foreach (int sid in scratch) best = math.min(best, Seg.Dist(p, index.A[sid], index.B[sid]));
                // an existing road along the outline serves it as well
                foreach (var x in roads)
                {
                    if (x.Rank == RoadRank.Highway) continue;
                    float d = x.Curve.DistanceXZ(p, out _) - x.HalfWidth - 22f;   // the planner leaves an edge to a road this close (RemoveOverlapping)
                    best = math.min(best, math.max(0f, d));
                }
                return best;
            }
            var samples = Poly.Resample(outline, 4f);
            float sum = 0f; int covered = 0;
            foreach (var p in samples)
            {
                float g = Gap(p);
                sum += g;
                r.OutlineMaxGap = math.max(r.OutlineMaxGap, g);
                if (g <= 3f) covered++;
                else if (g > 12f) r.Marks.Add(new Mark(MarkGap, p, g));
            }
            if (samples.Length > 0) { r.OutlineMeanGap = sum / samples.Length; r.OutlineCovered = (float)covered / samples.Length; }
            int corners = 0; float csum = 0f;
            int n = outline.Length;
            for (int i = 0; i < n; i++)
            {
                float2 d0 = math.normalizesafe(outline[i] - outline[(i + n - 1) % n]), d1 = math.normalizesafe(outline[(i + 1) % n] - outline[i]);
                if (RoadGraph.Angle(d0, d1) < 35f) continue;
                float g = Gap(outline[i]);
                corners++; csum += g;
                r.CornerMaxGap = math.max(r.CornerMaxGap, g);
                if (g > 3f) r.Marks.Add(new Mark(MarkCorner, outline[i], g));
            }
            if (corners > 0) r.CornerMeanGap = csum / corners;
        }

        private static void MeasureLinks(RoadsRating r, List<NNet> nets, List<NNode> nodes, List<XRoad> roads, List<XNode> xnodes, float2[] outline, Options o)
        {
            // Is there city to join at all (an ordinary road within reach of the outline)?
            if (outline.Length >= 3)
                foreach (var x in roads)
                {
                    if (x.Rank == RoadRank.Highway) continue;
                    foreach (var p in x.Line)
                        if (Poly.Contains(outline, p) || Poly.DistanceToBoundary(outline, p) < 120f) { r.CityNear = true; break; }
                    if (r.CityNear) break;
                }
            var attachAt = new List<float2>();
            float lenSum = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                var nd = nodes[i];
                if (nd.Out.Count == 0 || nd.ExistingRoad < 0) continue;
                var road = roads[nd.ExistingRoad];
                r.CityLinks++;
                attachAt.Add(nd.P);
                if (road.Rank == RoadRank.Highway)
                {
                    r.HighwayLinks++;
                    r.Marks.Add(new Mark(MarkHighwayLink, nd.P));
                    continue;
                }
                bool onJunction = nd.OnExistingNode && nd.ExistingDegree >= 3;
                bool deadEnd = nd.OnExistingNode && nd.ExistingDegree == 1;
                if (onJunction) r.LinksOnJunction++;
                // squareness to the road it meets (a dead end is continued, not met)
                bool skew = false;
                if (!deadEnd)
                    foreach (var (dir, _) in nd.Out)
                    {
                        float worst = 180f;
                        foreach (var xd in nd.Existing) worst = math.min(worst, RoadGraph.Angle(dir, xd));
                        if (worst < 65f) skew = true;
                    }
                else
                    foreach (var (dir, _) in nd.Out)
                        if (RoadGraph.Angle(dir, nd.Existing[0]) < 150f) skew = true;
                if (skew) r.SkewLinks++;
                // crowding an existing junction it is not on
                bool crowded = false;
                if (!onJunction)
                    foreach (var x in xnodes)
                    {
                        if (x.Out.Count < 3) continue;
                        float d = math.distance(x.P, nd.P);
                        if (d > 1f && d < 35f) { crowded = true; break; }
                    }
                if (crowded) r.LinksCrowded++;
                // the link chain into the plan: does it continue a street inside?
                bool aligned = false;
                float linkLen = 0f;
                foreach (var (dir0, start) in nd.Out)
                {
                    int cur = i, ei = start, guard = 0;
                    float2 arriving = dir0;
                    float len = 0f;
                    while (guard++ < 50)
                    {
                        var e = nets[ei];
                        len += e.Length;
                        int nxt = e.A == cur ? e.D : e.A;
                        arriving = e.A == cur ? e.Curve.TangentXZ(1f) : -e.Curve.TangentXZ(0f);
                        var nn = nodes[nxt];
                        if (nn.Out.Count != 2 || nn.Existing.Count > 0)
                        {
                            if (nn.Out.Count >= 3 || (nn.Out.Count >= 2 && nn.Existing.Count > 0))
                                foreach (var (d2, k) in nn.Out)
                                    if (k != ei && RoadGraph.Angle(d2, arriving) < 20f) aligned = true;
                            break;
                        }
                        int next = nn.Out[0].net == ei ? nn.Out[1].net : nn.Out[0].net;
                        cur = nxt; ei = next;
                    }
                    linkLen = math.max(linkLen, len);
                }
                // a street arriving straight at the road (no separate link piece) is the best join there is
                if (!aligned && nd.Out.Count == 1)
                {
                    var e = nets[nd.Out[0].net];
                    if (e.Role != NetRole.PerimeterRoad && e.Length > 40f && !skew) aligned = true;
                }
                if (aligned) r.LinksAligned++;
                lenSum += linkLen;
                r.Marks.Add(new Mark(skew ? MarkSkewLink : crowded ? MarkCrowdedLink : MarkGoodLink, nd.P, aligned ? 1f : 0f));
            }
            // two of our joins on the same stretch of road, too close together
            for (int i = 0; i < attachAt.Count; i++)
                for (int j = i + 1; j < attachAt.Count; j++)
                {
                    float d = math.distance(attachAt[i], attachAt[j]);
                    if (d > 1f && d < 35f) { r.LinksCrowded++; r.Marks.Add(new Mark(MarkCrowdedLink, attachAt[j], d)); }
                }
            if (r.CityLinks > 0) r.LinkMeanLength = lenSum / r.CityLinks;
        }

        private static void MeasureFit(RoadsRating r, List<NNet> nets, List<NNode> nodes, List<XRoad> roads, List<XNode> xnodes, float2[] outline,
            Options o, List<float2> junctions)
        {
            var lines = new List<List<float2>>();
            foreach (var e in nets)
            {
                var pts = new List<float3>();
                e.Curve.Flatten(pts, null, 3f, 4f);
                var l = new List<float2>(pts.Count);
                foreach (var p in pts) l.Add(p.xz);
                lines.Add(l);
            }
            for (int ni = 0; ni < nets.Count; ni++)
            {
                var e = nets[ni];
                var line = lines[ni];
                float hw = PlannedHalfWidth(e.Role);
                for (int xi = 0; xi < roads.Count; xi++)
                {
                    var x = roads[xi];
                    // crossings away from the piece's own ends (a crossing with no junction: the game splits or refuses)
                    bool crossed = false;
                    for (int i = 0; i + 1 < line.Count && !crossed; i++)
                        for (int k = 0; k + 1 < x.Line.Count; k++)
                        {
                            if (!Seg.Intersect(line[i], line[i + 1], x.Line[k], x.Line[k + 1], out float ta, out _)) continue;
                            float2 hit = math.lerp(line[i], line[i + 1], ta);
                            if (math.distance(hit, line[0]) < 2f || math.distance(hit, line[line.Count - 1]) < 2f) continue;
                            crossed = true;
                            if (x.Rank == RoadRank.Highway) { r.HighwayCrossings++; r.Marks.Add(new Mark(MarkHighwayCross, hit)); }
                            else { r.Crossings++; r.Marks.Add(new Mark(MarkCross, hit)); }
                            break;
                        }
                    // laid on top of / alongside the existing road
                    int inside = 0, near = 0;
                    const int samples = 9;
                    for (int s = 1; s <= samples; s++)
                    {
                        float t = s / (samples + 1f);
                        float2 p = e.Curve.Position(t).xz;
                        float d = x.Curve.DistanceXZ(p, out float xt);
                        float par = math.abs(math.dot(e.Curve.TangentXZ(t), x.Curve.TangentXZ(xt)));
                        if (par < 0.9f || !RoadConnections.Beside(x.Curve, p, xt)) continue;   // in line beyond its end: a continuation
                        if (d < (hw + x.HalfWidth) * 0.8f) inside++;
                        else if (d < hw + x.HalfWidth + 24f) near++;
                    }
                    if (inside >= 5) { r.DoubledPieces++; r.DoubledLength += e.Length; r.Marks.Add(new Mark(MarkDoubled, e.Curve.Position(0.5f).xz)); }
                    else if (near >= 6 && x.Rank >= RoadRank.Medium && e.Length > 30f) { r.SpineCrowding++; r.Marks.Add(new Mark(MarkSpine, e.Curve.Position(0.5f).xz)); }
                }
            }
            // A small road continued (end-on) by a bigger one; a big road joined too often.
            for (int i = 0; i < nodes.Count; i++)
            {
                var nd = nodes[i];
                if (nd.ExistingRoad < 0 || nd.Out.Count == 0) continue;
                var x = roads[nd.ExistingRoad];
                if (x.Rank == RoadRank.Highway) continue;
                foreach (var (dir, k) in nd.Out)
                {
                    var e = nets[k];
                    RoadRank planned = RoadClasses.RankOf(e.Role, PlannedHalfWidth(e.Role));
                    bool continues = false;
                    foreach (var xd in nd.Existing) if (RoadGraph.Angle(dir, xd) > 150f) continues = true;
                    if (continues && nd.Existing.Count <= 2 && planned > x.Rank && planned >= RoadRank.Medium)
                    {
                        r.HierarchyBreaks++;
                        r.Marks.Add(new Mark(MarkHierarchy, nd.P));
                    }
                }
                if (x.Rank >= RoadRank.Medium && !(nd.OnExistingNode && nd.ExistingDegree >= 3))
                {
                    float limit = math.max(40f, o.JunctionSpacing * 0.5f);
                    bool tooClose = false;
                    foreach (var xn in xnodes)
                    {
                        if (xn.Out.Count < 3) continue;
                        bool sameRoad = false;
                        foreach (var (_, ri) in xn.Out) if (ri == nd.ExistingRoad) sameRoad = true;
                        float d = math.distance(xn.P, nd.P);
                        if (sameRoad && d > 1f && d < limit) tooClose = true;
                    }
                    for (int j = 0; j < nodes.Count && !tooClose; j++)
                    {
                        if (j == i || nodes[j].ExistingRoad != nd.ExistingRoad || nodes[j].Out.Count == 0) continue;
                        float d = math.distance(nodes[j].P, nd.P);
                        if (d > 1f && d < limit) tooClose = true;
                    }
                    if (tooClose) { r.AccessBreaks++; r.Marks.Add(new Mark(MarkAccess, nd.P)); }
                }
            }
        }

        /// <summary>
        /// How directly the network crosses the district: from up to eight far-apart points (farthest-point sampling) to every
        /// junction or end far from them, road distance over straight distance. A plain grid is about 1.27 (the diagonal
        /// trips go round two sides); a diagonal street or a network following the desire lines brings it down.
        /// </summary>
        private static void MeasureDirectness(RoadsRating r, List<NNet> nets, List<NNode> nodes)
        {
            var ids = new List<int>();
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].Out.Count > 0 && nodes[i].Out.Count != 2) ids.Add(i);
            if (ids.Count < 8) return;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            foreach (int i in ids) { mn = math.min(mn, nodes[i].P); mx = math.max(mx, nodes[i].P); }
            float diameter = math.distance(mn, mx);
            if (diameter < 150f) return;
            // sources: farthest-point sampling
            var sources = new List<int>();
            float2 centre = (mn + mx) * 0.5f;
            int first = ids[0];
            foreach (int i in ids) if (math.distance(nodes[i].P, centre) > math.distance(nodes[first].P, centre)) first = i;
            sources.Add(first);
            while (sources.Count < 8)
            {
                int far = -1; float fd = -1f;
                foreach (int i in ids)
                {
                    float d = float.MaxValue;
                    foreach (int s in sources) d = math.min(d, math.distance(nodes[i].P, nodes[s].P));
                    if (d > fd) { fd = d; far = i; }
                }
                if (far < 0 || fd < 1f) break;
                sources.Add(far);
            }
            double sum = 0; int count = 0;
            foreach (int s in sources)
            {
                var dist = Dijkstra(nets, nodes, s);
                foreach (int t in ids)
                {
                    float straight = math.distance(nodes[s].P, nodes[t].P);
                    if (straight < 0.35f * diameter || float.IsPositiveInfinity(dist[t])) continue;
                    sum += math.min(3f, dist[t] / straight);
                    count++;
                }
            }
            if (count > 0) r.DetourMean = (float)(sum / count);
        }

        private static float[] Dijkstra(List<NNet> nets, List<NNode> nodes, int source)
        {
            var dist = new float[nodes.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.PositiveInfinity;
            dist[source] = 0f;
            var open = new SortedSet<(float, int)> { (0f, source) };
            while (open.Count > 0)
            {
                var (d, n) = open.Min;
                open.Remove(open.Min);
                if (d > dist[n]) continue;
                foreach (var (_, k) in nodes[n].Out)
                {
                    var e = nets[k];
                    int o = e.A == n ? e.D : e.A;
                    float nd = d + e.Length;
                    if (nd >= dist[o]) continue;
                    if (!float.IsPositiveInfinity(dist[o])) open.Remove((dist[o], o));
                    dist[o] = nd;
                    open.Add((nd, o));
                }
            }
            return dist;
        }

        private static float Clamp(float v) => math.clamp(v, 0f, 100f);

        private static void Score(RoadsRating r, Options o)
        {
            r.Grades = Clamp(100f - 300f * r.SteepShare - 3f * r.VerySteepPieces);
            if (r.JunctionCount > 0)
            {
                float bad = (r.AwkwardJunctions * 0.5f + r.AcuteJunctions + r.Overloaded) / r.JunctionCount;
                r.Junctions = Clamp(100f - 150f * bad - 80f * (float)r.CrowdedJunctions / r.JunctionCount);
            }
            float km = math.max(1f, r.RoadLength / 1000f);
            float segBad = r.Crumbs * 1f + r.ShortLinks * 1f + r.Kinks * 1.2f + r.Hooks * 1.5f + r.BareDeadEnds * 1f + r.Slivers * 0.8f + r.StrayStubs * 1.2f + r.Alongside * 1.2f + r.SelfCrossings * 3f;
            r.Segments = Clamp(100f - 12f * segBad / km);
            if (r.HasPerimeter)
                r.Perimeter = Clamp(100f - 8f * r.OutlineMeanGap - 1.2f * r.CornerMeanGap - 60f * (1f - r.OutlineCovered));
            if (r.CityLinks > 0)
            {
                float n = r.CityLinks;
                r.Links = Clamp(100f - 60f * r.SkewLinks / n - 40f * r.LinksCrowded / n - 25f * (1f - r.LinksAligned / n));
                if (r.HighwayLinks > 0) r.Links = 0f;
            }
            else if (r.CityNear) r.Links = 35f;
            r.Fit = Clamp(100f - 40f * r.HighwayCrossings - 15f * r.Crossings - 10f * r.DoubledPieces - 20f * r.HierarchyBreaks
                - 8f * r.AccessBreaks - 12f * r.SpineCrowding);
            r.Directness = Clamp(100f - 200f * (r.DetourMean - 1.15f));
            r.Total = 0.18f * r.Grades + 0.18f * r.Junctions + 0.13f * r.Segments + 0.14f * r.Perimeter + 0.14f * r.Links + 0.13f * r.Fit + 0.10f * r.Directness;
            if (r.HighwayLinks > 0 || r.HighwayCrossings > 0) r.Total = math.min(r.Total, 50f);
            if (r.CarNets == 0) r.Total = 0f;
        }
    }
}
