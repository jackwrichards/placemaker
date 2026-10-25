// Placemaker — the companion park building of a build (s17, docs/PARK-STATS.md §5): which prefab clone it uses
// (quantised stats → a stable prefab name) and where it stands (a road frontage inside the outline). Pure.
//
// Why a building: leisure, park coverage (wellbeing), land value and upkeep only work on a Building with a road
// connection, and the game reads them from the PREFAB, so each distinct set of stats needs its own clone. Clones
// are referenced by name in saves; quantising keeps their number small and the names stable.

using System;
using System.Collections.Generic;
using System.Globalization;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    /// <summary>Quantised park stats of one companion prefab. The name is save format: never change its layout.</summary>
    public struct ParkPrefabKey : IEquatable<ParkPrefabKey>
    {
        public const string Prefix = "Placemaker Park ";
        public int Attractiveness, Leisure, Range, Capacity, Magnitude, Upkeep;
        public bool Beach;

        public static ParkPrefabKey From(IDictionary<string, double> values, bool beach)
        {
            double V(string id, double fallback) => values != null && values.TryGetValue(id, out var v) && !double.IsNaN(v) ? v : fallback;
            return new ParkPrefabKey
            {
                Attractiveness = QuantiseCount(V(ParkStatIds.Attractiveness, 1)),
                Leisure = QuantiseCount(V(ParkStatIds.Leisure, 1)),
                Range = QuantiseRange(V(ParkStatIds.Range, 2500)),
                Capacity = QuantiseCapacity(V(ParkStatIds.Capacity, 800)),
                Magnitude = (int)Math.Max(1, Math.Min(30, Math.Round(V(ParkStatIds.Entertainment, 10)))),
                Upkeep = QuantiseUpkeep(V(ParkStatIds.Upkeep, 2000)),
                Beach = beach,
            };
        }

        /// <summary>Attractiveness / leisure: exact to 20, then steps of 5 to 100, then 25.</summary>
        public static int QuantiseCount(double v)
        {
            v = Math.Max(0, v);
            if (v <= 20) return (int)Math.Round(v);
            if (v <= 100) return (int)(Math.Round(v / 5) * 5);
            return (int)(Math.Round(v / 25) * 25);
        }

        public static int QuantiseRange(double v) => (int)Math.Max(250, Math.Round(Math.Max(0, v) / 250) * 250);

        public static int QuantiseCapacity(double v)
        {
            v = Math.Max(100, v);
            return v <= 2000 ? (int)(Math.Round(v / 100) * 100) : (int)(Math.Round(v / 500) * 500);
        }

        public static int QuantiseUpkeep(double v)
        {
            v = Math.Max(0, v);
            if (v <= 5000) return (int)(Math.Round(v / 250) * 250);
            if (v <= 50000) return (int)(Math.Round(v / 1000) * 1000);
            return (int)(Math.Round(v / 5000) * 5000);
        }

        public string Name => string.Format(CultureInfo.InvariantCulture, "{0}a{1} l{2} r{3} c{4} m{5} u{6}{7}",
            Prefix, Attractiveness, Leisure, Range, Capacity, Magnitude, Upkeep, Beach ? " b" : "");

        public static bool TryParse(string name, out ParkPrefabKey key)
        {
            key = default;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var parts = name.Substring(Prefix.Length).Split(' ');
            int found = 0;
            foreach (var p in parts)
            {
                if (p == "b") { key.Beach = true; continue; }
                if (p.Length < 2 || !int.TryParse(p.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < 0) return false;
                switch (p[0])
                {
                    case 'a': key.Attractiveness = v; break;
                    case 'l': key.Leisure = v; break;
                    case 'r': key.Range = v; break;
                    case 'c': key.Capacity = v; break;
                    case 'm': key.Magnitude = v; break;
                    case 'u': key.Upkeep = v; break;
                    default: return false;
                }
                found++;
            }
            return found == 6 && key.Name == name;
        }

        public bool Equals(ParkPrefabKey o) => Name == o.Name;
        public override bool Equals(object obj) => obj is ParkPrefabKey k && Equals(k);
        public override int GetHashCode() => Name.GetHashCode();
    }

    /// <summary>Where the companion stands: centre of its 8 m × 8 m lot and the yaw that faces the road.</summary>
    public struct ParkCompanionSite
    {
        public bool Found;
        public float2 Position;
        /// <summary>Radians around +Y, 0 = facing +Z (the building's front, which must face the road).</summary>
        public float Yaw;
        /// <summary>NearbyNet.ExternalId of the road it fronts.</summary>
        public long Road;
        /// <summary>Why none was found (localisation key), "" when found.</summary>
        public string ReasonKey;
    }

    public static class ParkCompanionPlanner
    {
        /// <summary>One cell, like the vanilla Tiny Park it is cloned from.</summary>
        public const float LotSize = 8f;
        /// <summary>Gap between the road's edge and the lot's front (the building's front must touch the road for RoadConnectionSystem).</summary>
        public const float CurbGap = 0.5f;
        /// <summary>The outline counts as fronting a road when it runs this close to the road's edge.</summary>
        public const float MaxFrontageGap = 6f;
        public const string NoRoadKey = "Placemaker.ParkStats.NoRoadEdge";

        /// <summary>
        /// Best spot for the companion: on a stretch of the outline that runs along a road, the lot fully inside the
        /// outline, clear of other roads, and as far as possible from the build's objects and paths (≥ 3 m) and corners.
        /// Deterministic. <paramref name="objects"/> are planned object positions, <paramref name="paths"/> planned
        /// path segments (a, b) with half width.
        /// </summary>
        public static ParkCompanionSite Find(IReadOnlyList<float2> outline, IReadOnlyList<NearbyNet> nets,
            IReadOnlyList<float2> objects = null, IReadOnlyList<(float2 a, float2 b, float halfWidth)> paths = null)
        {
            var none = new ParkCompanionSite { ReasonKey = NoRoadKey };
            if (outline == null || outline.Count < 3 || nets == null || nets.Count == 0) return none;
            var roads = new List<(float2 a, float2 b, float half, long id)>();
            foreach (var n in nets)
            {
                if (n.Category != NetCategory.Road) continue;
                float half = Math.Max(2f, n.Width * 0.5f);
                float2 prev = n.A.xz;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f, u = 1 - t;
                    var p = (u * u * u * n.A + 3 * u * u * t * n.B + 3 * u * t * t * n.C + t * t * t * n.D).xz;
                    roads.Add((prev, p, half, n.ExternalId));
                    prev = p;
                }
            }
            if (roads.Count == 0) return none;

            float half8 = LotSize * 0.5f;
            // (perf) Only objects and paths closer than the clearance cap can change a candidate's clearance: the objects in a
            // grid of that size, each path with its reach box (a park's ~2 000 plants were measured from every candidate, ~180 ms
            // in the game's Finish frame). The same minimum over the same values that matter.
            float reach = 12f + half8;
            var objGrid = new Dictionary<long, List<float2>>();
            if (objects != null)
                foreach (var o in objects)
                {
                    long k = CellKey((int)math.floor(o.x / reach), (int)math.floor(o.y / reach));
                    if (!objGrid.TryGetValue(k, out var l)) objGrid[k] = l = new List<float2>();
                    l.Add(o);
                }
            var pathBoxes = new List<(float2 min, float2 max)>();
            if (paths != null)
                foreach (var pa in paths)
                {
                    float r = pa.halfWidth + reach + 0.01f;
                    pathBoxes.Add((math.min(pa.a, pa.b) - r, math.max(pa.a, pa.b) + r));
                }
            bool found = false;
            double bestScore = double.MinValue;
            var best = none;
            int count = outline.Count;
            for (int e = 0; e < count; e++)
            {
                float2 a = outline[e], b = outline[(e + 1) % count];
                float len = math.distance(a, b);
                if (len < LotSize) continue;
                int steps = Math.Max(1, (int)(len / 2f));
                for (int s = 0; s <= steps; s++)
                {
                    float2 p = math.lerp(a, b, s / (float)steps);
                    // nearest road point
                    int ri = -1; float rd = float.MaxValue; float2 rq = default;
                    for (int r = 0; r < roads.Count; r++)
                    {
                        var q = Closest(roads[r].a, roads[r].b, p);
                        float d = math.distance(q, p);
                        if (d < rd) { rd = d; ri = r; rq = q; }
                    }
                    if (ri < 0) continue;
                    var road = roads[ri];
                    if (rd - road.half > MaxFrontageGap) continue;
                    float2 dir = rd > 1e-3f ? (p - rq) / rd : Perp(road.b - road.a);
                    float2 centre = rq + dir * (road.half + CurbGap + half8);
                    // lot inside the outline
                    float2 fwd = -dir, side = new float2(fwd.y, -fwd.x);
                    bool inside = Poly.Contains(outline, centre);
                    for (int c = 0; c < 4 && inside; c++)
                    {
                        float2 corner = centre + fwd * (c < 2 ? half8 : -half8) + side * (c % 2 == 0 ? half8 : -half8);
                        if (!Poly.Contains(outline, corner) && Poly.DistanceToBoundary(outline, corner) > 0.75f) inside = false;
                    }
                    if (!inside) continue;
                    // clear of every road (the lot must not sit on one)
                    bool onRoad = false;
                    foreach (var rr in roads)
                        if (math.distance(Closest(rr.a, rr.b, centre), centre) < rr.half + half8 - 0.25f) { onRoad = true; break; }
                    if (onRoad) continue;
                    float clear = 12f;
                    if (objects != null)
                    {
                        int cx = (int)math.floor(centre.x / reach), cy = (int)math.floor(centre.y / reach);
                        for (int gy = cy - 1; gy <= cy + 1; gy++)
                            for (int gx = cx - 1; gx <= cx + 1; gx++)
                                if (objGrid.TryGetValue(CellKey(gx, gy), out var near))
                                    foreach (var o in near) clear = Math.Min(clear, math.distance(o, centre) - half8);
                    }
                    if (paths != null)
                        for (int pi = 0; pi < paths.Count; pi++)
                        {
                            var box = pathBoxes[pi];
                            if (centre.x < box.min.x || centre.y < box.min.y || centre.x > box.max.x || centre.y > box.max.y) continue;
                            var pa = paths[pi];
                            clear = Math.Min(clear, math.distance(Closest(pa.a, pa.b, centre), centre) - pa.halfWidth - half8);
                        }
                    if (clear < 0f) continue;
                    float corner2 = float.MaxValue;
                    foreach (var v in outline) corner2 = Math.Min(corner2, math.distance(v, centre));
                    double score = Math.Min(clear, 12f) + 0.25 * Math.Min(corner2, 30f) - 0.5 * Math.Max(0, rd - road.half);
                    if (!found || score > bestScore + 1e-6)
                    {
                        found = true;
                        bestScore = score;
                        best = new ParkCompanionSite { Found = true, Position = centre, Yaw = math.atan2(fwd.x, fwd.y), Road = road.id, ReasonKey = "" };
                    }
                }
            }
            return best;
        }

        private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        private static float2 Perp(float2 v)
        {
            float l = math.length(v);
            return l > 1e-5f ? new float2(-v.y, v.x) / l : new float2(0, 1);
        }

        private static float2 Closest(float2 a, float2 b, float2 p)
        {
            float2 ab = b - a;
            float l2 = math.dot(ab, ab);
            float t = l2 > 1e-8f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return a + ab * t;
        }

        /// <summary>Object positions and path segments of a composed plan (what the companion keeps clear of).</summary>
        public static void Obstacles(ComposedPlan plan, List<float2> objects, List<(float2, float2, float)> paths)
        {
            if (plan == null) return;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var o in l.Objects)
                    if (o != null && o.Role != ObjectRole.Decal && o.Role != ObjectRole.Marker) objects?.Add(o.Position.xz);
                foreach (var n in l.Nets)
                    if (n != null && n.Role != NetRole.Fence) paths?.Add((n.A.xz, n.D.xz, Composer.DefaultHalfWidth(n.Role)));
                // (s22) Zoning cutouts are empty land at the road edge — exactly where the companion liked to go (far
                // from every object), and a building there takes the cutout's cells. Fill them (grown by a lot's half
                // size, see CutoutPoints) with obstacle points so the companion keeps out.
                if (objects != null)
                    foreach (var z in l.Zoning)
                        if (z != null) CutoutPoints(new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz }, objects);
            }
        }

        /// <summary>Obstacle points every 2 m over a zoning quad grown by 1 m (Find keeps the lot centre a half lot from every point).</summary>
        public static void CutoutPoints(float2[] quad, List<float2> into)
        {
            float2[] grown;
            try
            {
                var g = Geometry.Poly.Offset(Geometry.Poly.EnsureCcw(quad), -1f);
                grown = g != null && g.Count > 0 ? g[0] : quad;
            }
            catch (System.Exception) { grown = quad; }
            Geometry.Poly.Bounds(grown, out var min, out var max);
            if (math.any(max - min > 400f)) return;
            for (float x = min.x; x <= max.x; x += 2f)
                for (float y = min.y; y <= max.y; y += 2f)
                {
                    var p = new float2(x, y);
                    if (Geometry.Poly.Contains(grown, p)) into.Add(p);
                }
        }
    }
}
