// What is already taken inside a region: the recipe's ReservedSpace (parent layers, earlier
// siblings) plus whatever the current layer placed so far. Every placement in this workstream
// goes through Occupancy, which is how "respect input.Reserved" is guaranteed uniformly.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public sealed class Occupancy
    {
        public const int KindReserved = -1;

        public readonly LayerRegion LayerRegion;
        public readonly DiscIndex Discs = new DiscIndex(8f);
        public readonly SegmentIndex Corridors = new SegmentIndex(10f);
        /// <summary>Reserved corridors whose tag says they are walkable paths (useful anchors for add-ons).</summary>
        public readonly SegmentIndex Paths = new SegmentIndex(10f);
        private readonly List<float2[]> _polygons = new List<float2[]>();
        /// <summary>Reserved polygons and site obstacles (read only: HitsPolygon's polygons).</summary>
        public IReadOnlyList<float2[]> Polygons => _polygons;
        /// <summary>
        /// (s20, optional) Terrain check for placements: (centre, radius) → the ground there can take it (e.g. not steeper
        /// than a bench tolerates). Null = any ground. Set by layers that survey their terrain (water types).
        /// </summary>
        public Func<float2, float, bool> GroundOk;

        public Occupancy(LayerRegion region, ReservedSpace reserved, SiteContext site = null)
        {
            LayerRegion = region;
            // Existing buildings/utilities inside the selection are hard obstacles for placements.
            if (site?.Obstacles != null)
                foreach (var o in site.Obstacles)
                {
                    var quad = new[] { o.A, o.B, o.C, o.D };
                    Poly.Bounds(quad, out var mn, out var mx);
                    if (mx.x < region.Min.x || mx.y < region.Min.y || mn.x > region.Max.x || mn.y > region.Max.y) continue;
                    _polygons.Add(Poly.EnsureCcw(quad));
                }
            if (reserved == null) return;
            foreach (var d in reserved.Discs)
                if (Near(d.Center, d.Radius)) Discs.Add(d.Center, d.Radius, KindReserved);
            foreach (var c in reserved.Corridors)
            {
                if (!NearSegment(c.A, c.B, c.HalfWidth)) continue;
                Corridors.Add(c.A, c.B, c.HalfWidth, KindReserved);
                if (c.Tag == nameof(NetRole.Pathway) || c.Tag == nameof(NetRole.PathwayWide))
                    Paths.Add(c.A, c.B, c.HalfWidth, KindReserved);
            }
            foreach (var p in reserved.Polygons) _polygons.Add(p);
        }

        /// <summary>
        /// (perf) Another region's view of the same placed discs and corridors (shared, not copied: what a copy of them made now
        /// would hold, without rebuilding their grids for every search), with the site's obstacles; no reserved space, no paths.
        /// </summary>
        public Occupancy(LayerRegion region, SiteContext site, DiscIndex discs, SegmentIndex corridors) : this(region, null, site)
        {
            Discs = discs;
            Corridors = corridors;
        }

        private bool Near(float2 p, float r) =>
            p.x + r >= LayerRegion.Min.x && p.y + r >= LayerRegion.Min.y && p.x - r <= LayerRegion.Max.x && p.y - r <= LayerRegion.Max.y;

        private bool NearSegment(float2 a, float2 b, float hw) =>
            math.max(a.x, b.x) + hw >= LayerRegion.Min.x && math.max(a.y, b.y) + hw >= LayerRegion.Min.y &&
            math.min(a.x, b.x) - hw <= LayerRegion.Max.x && math.min(a.y, b.y) - hw <= LayerRegion.Max.y;

        /// <summary>
        /// A disc of radius r at p fits: inside the region with <paramref name="boundaryMargin"/>, and
        /// clear of every reserved/placed disc and corridor by <paramref name="gap"/>.
        /// </summary>
        public bool Free(float2 p, float r, float boundaryMargin, float gap = 0.3f)
        {
            if (!LayerRegion.ContainsDisc(p, math.max(0f, boundaryMargin))) return false;
            if (GroundOk != null && !GroundOk(p, r)) return false;
            if (Discs.Overlaps(p, r, gap)) return false;
            if (Corridors.Overlaps(p, r, gap)) return false;
            foreach (var poly in _polygons)
                if (PolygonDistance(p, poly) < r + gap) return false;
            return true;
        }

        /// <summary>Distance to the nearest occupied thing or the outline, capped.</summary>
        public float Clearance(float2 p, float cap)
        {
            if (!LayerRegion.Contains(p)) return -1f;
            float c = LayerRegion.BoundaryDistance(p, cap);
            c = math.min(c, Discs.Clearance(p, c));
            c = math.min(c, Corridors.Clearance(p, c));
            foreach (var poly in _polygons)
            {
                float d = PolygonDistance(p, poly);
                if (d <= 0f) return -1f;
                c = math.min(c, d);
            }
            return c;
        }

        /// <summary>Distance to a polygon's outline, 0 when inside.</summary>
        private static float PolygonDistance(float2 p, float2[] poly)
        {
            if (ReservedSpace.PointInPolygon(p, poly)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Length; i++)
                best = math.min(best, SegmentIndex.DistSq(p, poly[i], poly[(i + 1) % poly.Length]));
            return math.sqrt(best);
        }

        /// <summary>True when a disc touches a reserved polygon or site obstacle.</summary>
        public bool HitsPolygon(float2 p, float r)
        {
            foreach (var poly in _polygons)
                if (PolygonDistance(p, poly) < r) return true;
            return false;
        }

        public void Take(float2 p, float r, int kind) => Discs.Add(p, r, kind);
        public void TakeCorridor(float2 a, float2 b, float hw, int kind) => Corridors.Add(a, b, hw, kind);
    }

    /// <summary>Finds good spots for compact features (fountain, playground, picnic group…).</summary>
    public static class Spots
    {
        public enum Preference { Centre = 0, NearPaths = 1, Open = 2, NearBoundary = 3 }

        /// <summary>
        /// Best centre for a disc of <paramref name="radius"/> that is entirely free. Candidates on a grid
        /// (≤ ~2500), scored by preference with a small seeded jitter so equal spots break ties
        /// deterministically. Returns false when nothing fits.
        /// </summary>
        public static bool Find(Occupancy occ, float radius, Preference pref, uint seed, out float2 spot,
            IReadOnlyList<float2> avoid = null, float avoidDistance = 0f, float boundaryMargin = 0.5f, int budget = 0)
        {
            var region = occ.LayerRegion;
            spot = region.Centroid;
            var size = region.Max - region.Min;
            float step = math.max(1.5f, math.sqrt(size.x * size.y / 2500f));
            step = math.min(step, math.max(1.5f, radius * 0.75f));
            // Optional candidate budget for callers that search many times (dressing, scattered features).
            if (budget > 0) step = math.max(step, math.sqrt(size.x * size.y / budget));
            int nx = (int)math.ceil(size.x / step), ny = (int)math.ceil(size.y / step);
            if ((long)nx * ny > 10000) { step = math.sqrt(size.x * size.y / 10000f); nx = (int)math.ceil(size.x / step); ny = (int)math.ceil(size.y / step); }
            var centre = region.Centroid;
            float scale = math.max(10f, math.sqrt(region.Area));
            float bestScore = float.MinValue; bool found = false;
            for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    var p = region.Min + new float2(x, y) * step;
                    if (!region.Contains(p)) continue;
                    // (perf) The cheap tests first (each only skips the spot, so the order does not change the answer).
                    if (avoid != null)
                    {
                        bool tooClose = false;
                        foreach (var a in avoid) if (math.distancesq(a, p) < avoidDistance * avoidDistance) { tooClose = true; break; }
                        if (tooClose) continue;
                    }
                    if (!region.FarFromOutline(p, radius + boundaryMargin)) continue;   // BoundaryDistance(p, t + 0.01) >= t
                    float clear = occ.Clearance(p, radius * 2f + 5f);
                    if (clear < radius) continue;
                    float score = math.min(clear, radius * 2f) / math.max(1f, radius) * 0.5f;
                    switch (pref)
                    {
                        case Preference.Centre: score -= math.distance(p, centre) / scale * 2f; break;
                        case Preference.Open: score += math.min(clear, radius * 4f) / math.max(1f, radius); break;
                        case Preference.NearPaths:
                        {
                            float d = occ.Paths.Count > 0 ? occ.Paths.Clearance(p, scale) : scale;
                            // Best just beyond the feature's own radius: visible and reachable from the path.
                            score -= math.abs(d - (radius + 3f)) / scale * 3f;
                            break;
                        }
                        case Preference.NearBoundary:
                            score -= region.BoundaryDistance(p, scale) / scale * 2f; break;
                    }
                    score += Hashing.Unit(Hashing.Mix(seed, x, y)) * 0.02f;
                    if (score > bestScore) { bestScore = score; spot = p; found = true; }
                }
            return found;
        }
    }
}
