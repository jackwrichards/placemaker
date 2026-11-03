// Placemaker roads layer (s34) — measures a finished road plan the way a reviewer would: is everything connected (to
// itself and to the city), are there bare dead ends, how close are the junctions on the main roads, do walkways land
// on real nodes, how steep are the roads, are there crumbs (very short pieces) or knife-edge junctions. Used by the
// tests (every pattern × preset × site) and the gallery summary. Works on the plan's nets only (what the game builds).

using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    public sealed class RoadsAudit
    {
        public int CarNets, Walkways, Lanes, Caps;
        /// <summary>Connected pieces of car road (1 = one network).</summary>
        public int Components;
        /// <summary>Pieces with no end on an existing road (when the site has existing roads, should be 0).</summary>
        public int Unanchored;
        /// <summary>Dead ends with no turning circle and no existing road there.</summary>
        public int BareDeadEnds;
        /// <summary>Walkway ends that are not a node of the car network or another walkway.</summary>
        public int LooseWalkEnds;
        /// <summary>Smallest distance between two side junctions on main roads (m; +∞ when fewer than two).</summary>
        public float MinMainJunctionGap = float.PositiveInfinity;
        public float SteepestGrade;
        public int SteepRoads;
        public int ShortPieces;
        /// <summary>Junction angles under 40°.</summary>
        public int AcuteJunctions;
        public float RoadLength, WalkLength;

        public bool Connected => Components <= 1;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "parts {0}{1} · bare ends {2} · caps {3} · main gap {4} · walks {5} ({6:0} m, {7} loose) · lanes {8} · steepest {9:0.0}% ({10} over) · crumbs {11} · acute {12}",
            Components, Unanchored > 0 ? " (" + Unanchored + " not on city)" : "", BareDeadEnds, Caps,
            float.IsPositiveInfinity(MinMainJunctionGap) ? "-" : MinMainJunctionGap.ToString("0", CultureInfo.InvariantCulture) + " m",
            Walkways, WalkLength, LooseWalkEnds, Lanes, SteepestGrade * 100f, SteepRoads, ShortPieces, AcuteJunctions);

        private static bool IsWalk(PlannedNet n) => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide;

        /// <param name="plan">The roads layer's plan.</param>
        /// <param name="height">Ground height (null = no grade check).</param>
        /// <param name="maxGrade">Grade above which a road counts as steep (rise/run).</param>
        /// <param name="cityRoads">The site has existing roads the plan should join.</param>
        public static RoadsAudit Measure(AreaPlan plan, Func<float2, float> height, float maxGrade, bool cityRoads, float2[] outline = null)
        {
            var a = new RoadsAudit();
            if (plan == null) return a;
            var nodes = new Dictionary<long, int>();
            var pos = new List<float2>();
            int Node(float3 p)
            {
                long k = ((long)math.round(p.x * 20f) << 32) ^ (uint)(int)math.round(p.z * 20f);
                if (!nodes.TryGetValue(k, out int id)) { id = pos.Count; nodes[k] = id; pos.Add(p.xz); }
                return id;
            }
            var ends = new List<(int a, int d, PlannedNet n)>();
            foreach (var n in plan.Nets)
            {
                // a retaining wall's quay (terraced blocks) stands on its own: not part of the street network
                if (n == null || n.Category == AssetCategories.NetQuay) continue;
                ends.Add((Node(n.A), Node(n.D), n));
            }
            int count = pos.Count;
            var carDeg = new int[count];
            var allDeg = new int[count];
            var attached = new bool[count];
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var dirs = new List<float2>[count];
            for (int i = 0; i < count; i++) dirs[i] = new List<float2>(4);
            var mainSide = new bool[count];
            var mainAt = new bool[count];
            var sideAt = new bool[count];
            foreach (var (na, nd, n) in ends)
            {
                allDeg[na]++; allDeg[nd]++;
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                float len = c.LengthXZ(12);
                if (IsWalk(n))
                {
                    a.Walkways++;
                    a.WalkLength += len;
                    continue;
                }
                a.CarNets++;
                a.RoadLength += len;
                if (n.Role == NetRole.Alley) a.Lanes++;
                carDeg[na]++; carDeg[nd]++;
                if (n.StartAttach >= 0) attached[na] = true;
                if (n.EndAttach >= 0) attached[nd] = true;
                int ra = Find(na), rd = Find(nd);
                if (ra != rd) parent[math.max(ra, rd)] = math.min(ra, rd);
                dirs[na].Add(c.TangentXZ(0f));
                dirs[nd].Add(-c.TangentXZ(1f));
                bool main = n.Role == NetRole.Avenue;
                if (main) { mainAt[na] = mainAt[nd] = true; }
                else { sideAt[na] = sideAt[nd] = true; }
                if (len < 6f) a.ShortPieces++;
                if (height != null)
                {
                    float g = RoadGraph.MaxGrade(c, height, 16f, 4f);
                    a.SteepestGrade = math.max(a.SteepestGrade, g);
                    if (g > maxGrade + 0.005f) a.SteepRoads++;
                }
            }
            var caps = new List<float2>();
            foreach (var o in plan.Objects)
                if (o != null && o.Role == ObjectRole.CulDeSacCap) caps.Add(o.Position.xz);
            a.Caps = caps.Count;

            var compAnchored = new Dictionary<int, bool>();
            for (int i = 0; i < count; i++)
            {
                if (carDeg[i] == 0) continue;
                int r = Find(i);
                compAnchored[r] = (compAnchored.TryGetValue(r, out bool b) && b) || attached[i];
                mainSide[i] = mainAt[i] && sideAt[i] && carDeg[i] >= 3;
                if (carDeg[i] == 1 && allDeg[i] == 1 && !attached[i])
                {
                    bool capped = false;
                    foreach (var cp in caps) if (math.distance(cp, pos[i]) < 0.5f) { capped = true; break; }
                    bool onOutline = outline != null && outline.Length >= 3 && Geometry.Poly.DistanceToBoundary(outline, pos[i]) < 6f;
                    if (!capped && !onOutline) a.BareDeadEnds++;
                }
                if (dirs[i].Count >= 3)
                {
                    float worst = 180f;
                    for (int p = 0; p < dirs[i].Count; p++)
                        for (int q = p + 1; q < dirs[i].Count; q++) worst = math.min(worst, RoadGraph.Angle(dirs[i][p], dirs[i][q]));
                    if (worst < 40f) a.AcuteJunctions++;
                }
            }
            // Pieces joined to the city are joined to each other through it.
            int anchoredPieces = 0, loose = 0;
            foreach (var kv in compAnchored) if (kv.Value) anchoredPieces++; else loose++;
            a.Components = (anchoredPieces > 0 ? 1 : 0) + loose;
            if (cityRoads) a.Unanchored = loose;

            var sides = new List<float2>();
            for (int i = 0; i < count; i++) if (mainSide[i] && !attached[i]) sides.Add(pos[i]);
            for (int i = 0; i < sides.Count; i++)
                for (int j = i + 1; j < sides.Count; j++)
                    a.MinMainJunctionGap = math.min(a.MinMainJunctionGap, math.distance(sides[i], sides[j]));

            foreach (var (na, nd, n) in ends)
            {
                if (!IsWalk(n)) continue;
                if (allDeg[na] < 2 && n.StartAttach < 0) a.LooseWalkEnds++;
                if (allDeg[nd] < 2 && n.EndAttach < 0) a.LooseWalkEnds++;
            }
            return a;
        }
    }
}
