// Shallow organic water for the wetland and the zen garden (builder 6): ponds grown as chains of
// overlapping jittered discs in the space the paths left free, islands where a pond is wide enough
// to keep a channel around them, path crossings (boardwalks / garden bridges) and the basins.
//
// Engine notes. CS2 paths cannot float over water, so a crossing is a causeway: the strip under the
// path (and the islands) is cut out of the basin and keeps its original height, while the water
// level sits 0.3 m under the lowest bank — the path reads as a boardwalk across the water and the
// water can never spill over it. Each separated basin piece gets its own terraform + water source
// at the shared level. Basins are a permanent terrain change (warning issue, Permanent = true).
// Every pond keeps EdgeMargin from the outline, so the cut is far inside and the outline's ground
// never changes.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Wetland
{
    public sealed class Pond
    {
        public float2[] Ring;
        public float2 Min, Max;
        public float Level;
        public float Area;
        public readonly List<float2[]> Islands = new List<float2[]>();
        private bool _deepKnown;
        private float2 _deep;
        private float _inner;

        /// <summary>The point farthest from the shore (cached) and its distance.</summary>
        public float2 Deepest(out float inner)
        {
            if (!_deepKnown)
            {
                var shape = LayerRegion.From(Ring);
                _deep = shape != null ? shape.DeepestPoint(out _inner, null, 12) : Poly.Centroid(Ring);
                _deepKnown = true;
            }
            inner = _inner;
            return _deep;
        }

        public Pond(float2[] ring)
        {
            Ring = ring;
            Poly.Bounds(ring, out Min, out Max);
            Area = Poly.Area(ring);
        }

        public bool Near(float2 p, float margin) =>
            p.x >= Min.x - margin && p.y >= Min.y - margin && p.x <= Max.x + margin && p.y <= Max.y + margin;
    }

    public struct GrowSpec
    {
        public int Ponds;
        /// <summary>Total wanted water area (m²).</summary>
        public float TargetArea;
        public float BlobRadius, MinRadius;
        /// <summary>Clearance from the outline / from paths and placed things / between ponds.</summary>
        public float EdgeMargin, PathMargin, PondGap;
        /// <summary>0 = straight chains, 1 = wandering.</summary>
        public float Wander;
        /// <summary>Blob outline jitter (0.1 round … 0.35 ragged).</summary>
        public float Jitter;
        /// <summary>Chains follow Axis (swales).</summary>
        public bool Linear;
        public float2 Axis;
        /// <summary>
        /// Most the ground under one pond may rise above its lowest point (m; 0 = the default <see cref="WaterBodies.MaxRise"/>).
        /// The water level is set by the lowest bank, so a pond laid across a slope dug its uphill side into a pit with a wall,
        /// and the islands and path crossings in it kept the hill's height (cones and walled ledges; wetland on hills, 27 Oct).
        /// </summary>
        public float MaxRise;
    }

    public sealed class WaterBodies
    {
        public readonly List<Pond> Ponds = new List<Pond>();
        /// <summary>Default <see cref="GrowSpec.MaxRise"/>: a pond only grows on ground this level (m).</summary>
        public const float MaxRise = 1.2f;
        /// <summary>Causeway strips and platforms kept dry inside the ponds (CCW rings).</summary>
        public readonly List<float2[]> Dry = new List<float2[]>();

        // Raster of the open water with a chamfer distance transform (Freeze): the thousands of planting
        // and free-space queries then cost one lookup instead of polygon tests. ±half a cell accurate.
        private bool[] _wet;
        private float[] _dist;
        private float2 _origin;
        private float _cell;
        private int _nx, _ny;

        /// <summary>Builds the water raster over <paramref name="region"/>; call after the ponds and islands are final.</summary>
        public void Freeze(LayerRegion region)
        {
            const float pad = 4f;
            var size = region.Max - region.Min + 2f * pad;
            _cell = math.max(1.25f, math.sqrt(size.x * size.y / 250000f));
            _origin = region.Min - pad;
            _nx = math.max(1, (int)math.ceil(size.x / _cell));
            _ny = math.max(1, (int)math.ceil(size.y / _cell));
            var wet = new bool[_nx * _ny];
            foreach (var pond in Ponds)
            {
                Fill(wet, pond.Ring, true);
                foreach (var isl in pond.Islands) Fill(wet, isl, false);
            }
            _dist = Chamfer(wet, true);     // dry cells: distance to the water
            _shore = Chamfer(wet, false);   // wet cells: distance to dry land
            _wet = wet;
        }

        private float[] _shore;

        /// <summary>Chamfer (1, √2) distance from every cell to the nearest seed cell (wet == seedWet), in metres to the seed's edge.</summary>
        private float[] Chamfer(bool[] wet, bool seedWet)
        {
            const float inf = 1e9f, diag = 1.41421356f;
            var d = new float[wet.Length];
            for (int i = 0; i < d.Length; i++) d[i] = wet[i] == seedWet ? 0f : inf;
            for (int y = 0; y < _ny; y++)
                for (int x = 0; x < _nx; x++)
                {
                    int i = y * _nx + x;
                    float v = d[i];
                    if (x > 0) v = math.min(v, d[i - 1] + 1f);
                    if (y > 0)
                    {
                        v = math.min(v, d[i - _nx] + 1f);
                        if (x > 0) v = math.min(v, d[i - _nx - 1] + diag);
                        if (x + 1 < _nx) v = math.min(v, d[i - _nx + 1] + diag);
                    }
                    d[i] = v;
                }
            for (int y = _ny - 1; y >= 0; y--)
                for (int x = _nx - 1; x >= 0; x--)
                {
                    int i = y * _nx + x;
                    float v = d[i];
                    if (x + 1 < _nx) v = math.min(v, d[i + 1] + 1f);
                    if (y + 1 < _ny)
                    {
                        v = math.min(v, d[i + _nx] + 1f);
                        if (x + 1 < _nx) v = math.min(v, d[i + _nx + 1] + diag);
                        if (x > 0) v = math.min(v, d[i + _nx - 1] + diag);
                    }
                    d[i] = v;
                }
            // Cell-centre distances → distance to the seed region's edge (half a cell nearer).
            for (int i = 0; i < d.Length; i++) d[i] = d[i] == 0f ? 0f : math.max(0f, d[i] * _cell - _cell * 0.5f);
            return d;
        }

        /// <summary>Inside open water: distance to the nearest dry land (frozen raster); 0 on land.</summary>
        public float ShoreDistance(float2 p)
        {
            int ci = CellIndex(p);
            return ci >= 0 ? _shore[ci] : 0f;
        }

        private void Fill(bool[] grid, float2[] ring, bool value)
        {
            Poly.Bounds(ring, out var mn, out var mx);
            int y0 = math.max(0, (int)math.floor((mn.y - _origin.y) / _cell)), y1 = math.min(_ny - 1, (int)math.ceil((mx.y - _origin.y) / _cell));
            var xs = new List<float>();
            for (int iy = y0; iy <= y1; iy++)
            {
                float py = _origin.y + (iy + 0.5f) * _cell;
                xs.Clear();
                for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                {
                    var a = ring[i]; var b = ring[j];
                    if ((a.y > py) != (b.y > py)) xs.Add(a.x + (py - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = math.max(0, (int)math.ceil((xs[k] - _origin.x) / _cell - 0.5f));
                    int x1 = math.min(_nx - 1, (int)math.floor((xs[k + 1] - _origin.x) / _cell - 0.5f));
                    for (int ix = x0; ix <= x1; ix++) grid[iy * _nx + ix] = value;
                }
            }
        }

        private int CellIndex(float2 p)
        {
            if (_wet == null) return -1;
            int x = (int)math.floor((p.x - _origin.x) / _cell), y = (int)math.floor((p.y - _origin.y) / _cell);
            return x < 0 || y < 0 || x >= _nx || y >= _ny ? -1 : y * _nx + x;
        }

        /// <summary>True when p lies in open water (not on an island), or within <paramref name="margin"/> of it.</summary>
        public bool InWater(float2 p, float margin = 0f)
        {
            int ci = CellIndex(p);
            if (ci >= 0) return _wet[ci] || (margin > 0f && _dist[ci] < margin);
            foreach (var pond in Ponds)
            {
                if (!pond.Near(p, margin)) continue;
                if (Poly.Contains(pond.Ring, p))
                {
                    bool island = false;
                    foreach (var isl in pond.Islands)
                        if (Poly.Contains(isl, p) && Poly.DistanceToBoundary(isl, p) >= margin) { island = true; break; }
                    if (!island) return true;
                    continue;
                }
                if (margin > 0f && Poly.DistanceToBoundary(pond.Ring, p) < margin) return true;
            }
            return false;
        }

        /// <summary>True when p is on an island (with margin from its shore).</summary>
        public bool OnIsland(float2 p, float margin = 0f)
        {
            foreach (var pond in Ponds)
            {
                if (!pond.Near(p, 0f)) continue;
                foreach (var isl in pond.Islands)
                    if (Poly.Contains(isl, p) && Poly.DistanceToBoundary(isl, p) >= margin) return true;
            }
            return false;
        }

        /// <summary>Distance to the nearest shore (0 inside water), capped.</summary>
        public float Distance(float2 p, float cap)
        {
            int ci = CellIndex(p);
            if (ci >= 0) return math.min(cap, _dist[ci]);
            float best = cap;
            foreach (var pond in Ponds)
            {
                if (!pond.Near(p, best)) continue;
                if (Poly.Contains(pond.Ring, p)) return 0f;
                best = math.min(best, Poly.DistanceToBoundary(pond.Ring, p));
            }
            return best;
        }

        public float TotalArea
        {
            get
            {
                float a = 0f;
                foreach (var p in Ponds) { a += p.Area; foreach (var i in p.Islands) a -= Poly.Area(i); }
                return a;
            }
        }

        // ───────────────────────────── growing ─────────────────────────────

        private struct Blob { public float2 C; public float R; public int Pond; }

        /// <summary>
        /// Grows up to spec.Ponds ponds in the free space of <paramref name="occ"/> (paths, objects,
        /// reserved space) and adds them (with their water level) to this set. Returns how many grew.
        /// </summary>
        public int Grow(LayerContext ctx, Occupancy occ, GrowSpec spec, uint seed, Func<float2, bool> forbidden = null)
        {
            var region = occ.LayerRegion;
            _wet = null;
            var rng = Hashing.Rng(seed, 0x9E7Du);
            var blobs = new List<Blob>();
            foreach (var p in Ponds) blobs.Add(new Blob { C = (p.Min + p.Max) * 0.5f, R = math.cmax(p.Max - p.Min) * 0.5f, Pond = -1 });
            float perPond = spec.TargetArea / math.max(1, spec.Ponds);
            float j = math.clamp(spec.Jitter, 0.05f, 0.4f);
            int grown = 0;
            float maxRise = spec.MaxRise > 0f ? spec.MaxRise : MaxRise, rise = maxRise;
            float lo = float.MaxValue, hi = float.MinValue;   // the ground under the pond growing now

            // the ground under a blob: its centre and its rim
            void Ground(float2 c, float reach, out float min, out float max)
            {
                min = max = ctx.H(c);
                for (int a = 0; a < 12; a++)
                {
                    float an = a * math.PI / 6f;
                    float h = ctx.H(c + new float2(math.cos(an), math.sin(an)) * reach);
                    min = math.min(min, h); max = math.max(max, h);
                }
            }

            bool Fits(float2 c, float r, int pond)
            {
                float reach = r * (1f + j);
                Ground(c, reach, out float gmin, out float gmax);
                if (math.max(hi, gmax) - math.min(lo, gmin) > rise) return false;
                if (region.BoundaryDistance(c, reach + spec.EdgeMargin + 0.01f) < reach + spec.EdgeMargin) return false;
                if (occ.Corridors.Overlaps(c, reach, spec.PathMargin)) return false;
                if (occ.Discs.Overlaps(c, reach, spec.PathMargin * 0.5f)) return false;
                if (occ.HitsPolygon(c, reach + spec.PathMargin)) return false;
                if (forbidden != null && forbidden(c)) return false;
                foreach (var b in blobs)
                {
                    if (b.Pond == pond) continue;
                    float d = reach + b.R * (1f + j) + spec.PondGap;
                    if (math.distancesq(b.C, c) < d * d) return false;
                }
                return true;
            }

            for (int k = 0; k < spec.Ponds; k++)
            {
                ctx.Check();
                int id = 1000 + k;
                lo = float.MaxValue; hi = float.MinValue;
                float rStart = math.max(spec.MinRadius, spec.BlobRadius * rng.NextFloat(0.8f, 1.2f)), r0 = rStart;
                float2 start = default; bool found = false;
                // level ground first; on hilly land ground twice as uneven (the banks soften it) before the pond is given up
                for (int attempt = 0; attempt < 10 && !found; attempt++)
                {
                    if (attempt == 0) rise = maxRise;
                    else if (attempt == 5) { rise = maxRise * 2f; r0 = rStart; }
                    if (r0 < spec.MinRadius) continue;
                    var avoid = new List<float2>();
                    foreach (var b in blobs) avoid.Add(b.C);
                    float reach = r0 * (1f + j);
                    if (Spots.Find(occ, reach + spec.PathMargin, Spots.Preference.Open, Hashing.Mix(seed, (uint)(k * 7 + attempt)), out var c,
                            avoid, reach * 2f + spec.PondGap, math.max(0.5f, spec.EdgeMargin - spec.PathMargin), 900)
                        && Fits(c, r0, id))
                    { start = c; found = true; break; }
                    r0 *= 0.72f;
                }
                // hilly land: the most level free spot (hilltops, valley floors), smallest pond first; last, ground four times as
                // uneven (the basin bank grades its uphill side) rather than no pond at all (a stroll garden without its pond)
                for (int tier = 0; tier < 2 && !found; tier++)
                {
                    rise = maxRise * (tier == 0 ? 2f : 4f);
                    r0 = spec.MinRadius * 1.2f;
                    float reach = r0 * (1f + j), step = math.max(3f, reach);
                    float best = float.MaxValue;
                    for (float y = region.Min.y + reach; y < region.Max.y - reach; y += step)
                        for (float x = region.Min.x + reach; x < region.Max.x - reach; x += step)
                        {
                            var c = new float2(x, y);
                            Ground(c, reach, out float gmin, out float gmax);
                            if (gmax - gmin >= best || !Fits(c, r0, id)) continue;
                            best = gmax - gmin; start = c; found = true;
                        }
                }
                if (!found) continue;

                var mine = new List<float2[]>();
                float est = 0f;
                float2 dir = spec.Linear ? spec.Axis * (rng.NextBool() ? 1f : -1f) : Rotate(new float2(1, 0), rng.NextFloat(0f, 6.283f));
                var cur = start; float r = r0;
                void AddBlob(float2 c, float rr)
                {
                    Ground(c, rr * (1f + j), out float gmin, out float gmax);
                    lo = math.min(lo, gmin); hi = math.max(hi, gmax);
                    blobs.Add(new Blob { C = c, R = rr, Pond = id });
                    mine.Add(Poly.EnsureCcw(LeisureKit.Blob(c, rr, Hashing.Mix(seed, (uint)(blobs.Count * 31 + k)), 14, j)));
                    est += math.PI * rr * rr * (mine.Count == 1 ? 1f : 0.5f);
                }
                AddBlob(cur, r);
                int fails = 0, steps = 0, reversals = 0;
                var origin = start; var originDir = dir;
                while (est < perPond && steps < 48 && fails < 1)
                {
                    steps++;
                    float turn = (rng.NextFloat() * 2f - 1f) * spec.Wander * math.PI * 0.5f;
                    dir = Rotate(dir, turn);
                    if (spec.Linear) dir = math.normalizesafe(math.lerp(dir, spec.Axis * math.sign(math.dot(dir, spec.Axis) + 1e-3f), 0.7f), spec.Axis);
                    bool ok = false;
                    foreach (float da in new[] { 0f, 0.5f, -0.5f, 1f, -1f, 1.6f, -1.6f })
                    {
                        foreach (float shrink in new[] { 1f, 0.75f, 0.55f })
                        {
                            float rr = math.max(spec.MinRadius, r0 * shrink * (0.8f + 0.35f * Hashing.Unit(Hashing.Mix(seed, (uint)(steps * 13 + k)))));
                            var d2 = Rotate(dir, da);
                            var next = cur + d2 * (r + rr) * 0.55f;
                            if (!Fits(next, rr, id)) continue;
                            cur = next; dir = d2; r = rr; AddBlob(cur, rr); ok = true;
                            break;
                        }
                        if (ok) break;
                    }
                    if (!ok)
                    {
                        // Dead end: grow the other way from the start once.
                        if (reversals++ == 0) { cur = origin; dir = -originDir; r = r0; continue; }
                        fails++;
                    }
                }

                var ring = Merge(mine);
                if (ring == null) continue;
                var pond = new Pond(ring);
                float shore = float.MaxValue;
                foreach (var p in Poly.Resample(ring, 4f)) shore = math.min(shore, ctx.H(p));
                if (!math.isfinite(shore)) shore = ctx.H(ring[0]);
                pond.Level = shore - 0.3f;
                Ponds.Add(pond);
                grown++;
            }
            return grown;
        }

        /// <summary>Union of the blobs, lightly smoothed; null when nothing usable remains.</summary>
        private static float2[] Merge(List<float2[]> blobs)
        {
            if (blobs.Count == 0) return null;
            List<Region> merged;
            try { merged = Poly.UnionAll(blobs); } catch (Exception) { return null; }
            if (merged.Count == 0 || merged[0].Outer == null || merged[0].Outer.Length < 3) return null;
            var outer = merged[0].Outer;   // largest; enclosed dry pockets become water (they are tiny)
            // Chaikin multiplies vertices; simplify back (≈0.25 m tolerance) so shore queries stay cheap.
            var smooth = Poly.Clean(Poly.Simplify(Poly.Chaikin(outer, 2), 0.25f), 0.4f);
            var ring = smooth.Length >= 3 && Poly.IsSimple(smooth) ? smooth : Poly.Clean(outer, 0.4f);
            if (ring.Length < 3 || Poly.Area(ring) < 20f || !Poly.IsSimple(ring)) return null;
            return Poly.EnsureCcw(ring);
        }

        private static float2 Rotate(float2 v, float a) => new float2(v.x * math.cos(a) - v.y * math.sin(a), v.x * math.sin(a) + v.y * math.cos(a));

        // ───────────────────────────── islands ─────────────────────────────

        /// <summary>
        /// Adds up to <paramref name="count"/> islands over the ponds (largest pond first, round robin),
        /// each keeping an open channel to the shore and clear of crossings. Returns how many were added.
        /// </summary>
        public int AddIslands(LayerRegion region, int count, float channel, float maxRadius, uint seed, SegmentIndex crossings)
        {
            int added = 0;
            if (count <= 0 || Ponds.Count == 0) return 0;
            if (_wet == null) Freeze(region);
            var order = new List<Pond>(Ponds);
            order.Sort((a, b) => b.Area.CompareTo(a.Area));
            // Room per grid point (distance to the shore and the crossings), computed once per pond.
            var room = new List<List<(float2 p, float room)>>();
            foreach (var pond in order) room.Add(Room(pond, channel, crossings));
            _wet = null;
            for (int round = 0; round < count && added < count; round++)
            {
                bool any = false;
                for (int k = 0; k < order.Count; k++)
                {
                    if (added >= count) break;
                    if (!AddIsland(order[k], room[k], channel, maxRadius, Hashing.Mix(seed, (uint)(round * 17 + added)))) continue;
                    added++; any = true;
                }
                if (!any) break;
            }
            Freeze(region);
            return added;
        }

        private const float IslandJitter = 0.22f;

        private List<(float2, float)> Room(Pond pond, float channel, SegmentIndex crossings)
        {
            var r = new List<(float2, float)>();
            float step = math.max(2f, math.sqrt(pond.Area / 600f));
            for (float y = pond.Min.y + step * 0.5f; y < pond.Max.y; y += step)
                for (float x = pond.Min.x + step * 0.5f; x < pond.Max.x; x += step)
                {
                    var p = new float2(x, y);
                    float room = ShoreDistance(p) - channel;
                    if (room < 2.2f || !Poly.Contains(pond.Ring, p)) continue;
                    if (crossings != null && crossings.Count > 0) room = math.min(room, crossings.Clearance(p, room + 1f) - 2f);
                    if (room >= 2.2f) r.Add((p, room));
                }
            return r;
        }

        private static bool AddIsland(Pond pond, List<(float2 p, float room)> cells, float channel, float maxRadius, uint seed)
        {
            float best = 0f; float2 at = default;
            var centres = new List<(float2 c, float r)>();
            foreach (var isl in pond.Islands) centres.Add((Poly.Centroid(isl), math.sqrt(Poly.Area(isl) / math.PI) * (1f + IslandJitter)));
            foreach (var (p, shore) in cells)
            {
                float room = shore;
                foreach (var (ic, ir) in centres) room = math.min(room, math.distance(p, ic) - ir - channel);
                float r = math.min(maxRadius, room / (1f + IslandJitter));
                r += Hashing.Unit(Hashing.Mix(seed, (int)(p.x * 3f), (int)(p.y * 3f))) * 0.05f;
                if (r > best) { best = r; at = p; }
            }
            if (best < 2.2f) return false;
            var blob = Poly.EnsureCcw(LeisureKit.Blob(at, best, seed, 12, IslandJitter));
            if (!Poly.IsSimple(blob)) return false;
            pond.Islands.Add(blob);
            return true;
        }

        // ───────────────────────────── crossings ─────────────────────────────

        /// <summary>
        /// A crossing of <paramref name="pond"/> from path to path through its widest part (or a spur
        /// from one path to a platform in the middle when only one side reaches a path). Polyline
        /// from the first path foot to the last point; null when nothing works.
        /// </summary>
        public List<float2> Crossing(Pond pond, Occupancy occ, float maxApproach, bool zigzag, uint seed, out bool spur, IReadOnlyList<float2> avoidNear = null)
        {
            spur = false;
            var region = occ.LayerRegion;
            var c = pond.Deepest(out float inner);
            if (inner <= 0f) return null;
            float reach = math.length(pond.Max - pond.Min) + 2f;
            float phase = Hashing.Unit(seed) * math.PI;
            List<float2> best = null; float bestScore = float.MaxValue; bool bestSpur = false;
            for (int i = 0; i < 12; i++)
            {
                float a = phase + i * math.PI / 12f;
                var dir = new float2(math.cos(a), math.sin(a));
                var pieces = Poly.ClipSegmentToPolygon(pond.Ring, c - dir * reach, c + dir * reach);
                bool got = false; float2 sA = c, sB = c;
                foreach (var (p0, p1) in pieces)
                    if (SegmentIndex.DistSq(c, p0, p1) < 0.25f) { sA = p0; sB = p1; got = true; break; }
                if (!got) continue;
                var d = math.normalizesafe(sB - sA);
                bool hitA = Approach(region, occ, sA, -d, maxApproach, out var footA, out float lenA);
                bool hitB = Approach(region, occ, sB, d, maxApproach, out var footB, out float lenB);
                float chord = math.distance(sA, sB);
                float avoidPenalty = 0f;
                if (avoidNear != null)
                    foreach (var q in avoidNear)
                        avoidPenalty += math.max(0f, 25f - math.sqrt(SegmentIndex.DistSq(q, sA, sB))) * 2f;
                if (hitA && hitB)
                {
                    float score = chord * 0.3f + lenA + lenB + avoidPenalty;
                    if (bestSpur || score < bestScore)
                    {
                        bestScore = score; bestSpur = false;
                        best = Line(footA, sA - d * 1f, sB + d * 1f, footB, pond, zigzag);
                    }
                }
                else if ((hitA || hitB) && (best == null || bestSpur) && inner > 4f)
                {
                    float score = 1000f + (hitA ? lenA : lenB) + avoidPenalty;
                    if (score < bestScore)
                    {
                        bestScore = score; bestSpur = true;
                        best = hitA ? new List<float2> { footA, sA - d * 1f, c } : new List<float2> { footB, sB + d * 1f, c };
                    }
                }
            }
            spur = bestSpur;
            return best;
        }

        /// <summary>Walks from a shore point outwards until it meets a path (within maxApproach), over dry land only.</summary>
        private bool Approach(LayerRegion region, Occupancy occ, float2 shore, float2 dir, float maxApproach, out float2 foot, out float length)
        {
            foot = shore; length = 0f;
            for (float t = 1f; t <= maxApproach; t += 1f)
            {
                var q = shore + dir * t;
                if (!region.ContainsDisc(q, 1.5f)) return false;
                if (t > 1.5f && InWater(q)) return false;
                if (occ.Paths.Clearance(q, 1.6f) < 1.5f && occ.Paths.Nearest(q, 1.5f, out var f) >= 0)
                {
                    foot = f; length = t;
                    return true;
                }
            }
            return false;
        }

        private static List<float2> Line(float2 footA, float2 a, float2 b, float2 footB, Pond pond, bool zigzag)
        {
            var pts = new List<float2> { footA, a };
            float len = math.distance(a, b);
            if (zigzag && len > 14f)
            {
                int n = math.clamp((int)(len / 9f), 2, 7);
                var d = (b - a) / len;
                var side = new float2(-d.y, d.x);
                float amp = math.min(2.5f, len * 0.07f);
                for (int i = 1; i < n; i++)
                {
                    var p = math.lerp(a, b, i / (float)n) + side * ((i & 1) == 0 ? -amp : amp);
                    if (Poly.Contains(pond.Ring, p) && Poly.DistanceToBoundary(pond.Ring, p) > 1.5f) pts.Add(p);
                    else pts.Add(math.lerp(a, b, i / (float)n));
                }
            }
            pts.Add(b);
            pts.Add(footB);
            return pts;
        }

        /// <summary>Keeps a causeway strip dry under every segment of a crossing polyline that touches water.</summary>
        public void KeepDry(IReadOnlyList<float2> polyline, float halfWidth)
        {
            for (int i = 1; i < polyline.Count; i++)
            {
                var a = polyline[i - 1]; var b = polyline[i];
                float len = math.distance(a, b);
                if (len < 0.2f) continue;
                var axis = (b - a) / len;
                Dry.Add(Poly.EnsureCcw(LayerRegion.Rect((a + b) * 0.5f, axis, len * 0.5f + halfWidth, halfWidth)));
            }
        }

        public void KeepDryDisc(float2 c, float r) => Dry.Add(Poly.EnsureCcw(LayerRegion.Circle(c, r, 16)));

        // ───────────────────────────── emission ─────────────────────────────

        /// <summary>
        /// Emits the basins: per dry-separated piece one terraform with a water source (the shallow
        /// shelf), plus a deeper inner terraform when <paramref name="shelfWidth"/> leaves room.
        /// Returns the number of basin pieces.
        /// </summary>
        public int EmitBasins(LayerContext ctx, float depth, float shelfDepth, float shelfWidth)
        {
            int pieces = 0;
            foreach (var pond in Ponds)
            {
                ctx.Check();
                Common.Shore.BasinBank.Add(ctx, pond.Ring, pond.Level);   // on a slope: a bank, not a wall cut down to the water
                var clip = new List<IReadOnlyList<float2>>();
                foreach (var isl in pond.Islands) clip.Add(isl);
                foreach (var d in Dry)
                {
                    Poly.Bounds(d, out var mn, out var mx);
                    if (mx.x < pond.Min.x || mx.y < pond.Min.y || mn.x > pond.Max.x || mn.y > pond.Max.y) continue;
                    clip.Add(d);
                }
                List<float2[]> rings = new List<float2[]>();
                if (clip.Count == 0) rings.Add(pond.Ring);
                else
                {
                    List<Region> regions;
                    try { regions = Poly.Boolean(new[] { (IReadOnlyList<float2>)pond.Ring }, clip, BoolOp.Difference); }
                    catch (Exception) { regions = new List<Region> { new Region(pond.Ring) }; }
                    foreach (var r in regions)
                        foreach (var piece in r.HasHoles ? Poly.CutHoles(r) : new List<float2[]> { r.Outer })
                            rings.Add(piece);
                }
                foreach (var ring in rings)
                {
                    var clean = Poly.Clean(ring, 0.2f);
                    if (clean.Length < 3 || Poly.Area(clean) < 12f) continue;
                    var lr = LayerRegion.From(clean);
                    var anchor = lr != null ? lr.DeepestPoint(out _, null, 12) : Poly.Centroid(clean);
                    bool shelf = shelfWidth > 0.2f && depth > shelfDepth + 0.1f;
                    // rf-parks: with a shelf the water source must stand in the deep part. The deepest point of a piece cut round the
                    // islands can lie on the shelf, where the build measured only the shelf depth (0.3 m < 0.5 m) and added no water
                    // ("lake basin not deep enough"; zen stroll garden, wetland bird reserve, 2026-10-16).
                    if (shelf)
                    {
                        float bestIns = -1f;
                        foreach (var deep in Poly.Offset(clean, shelfWidth))
                        {
                            if (deep == null || deep.Length < 3 || Poly.Area(deep) < 8f) continue;
                            var dr = LayerRegion.From(deep);
                            if (dr == null) continue;
                            var p = dr.DeepestPoint(out float ins, null, 12);
                            if (ins > bestIns) { bestIns = ins; anchor = p; }
                        }
                    }
                    ctx.Plan.Terraform.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(clean, ctx.H), Kind = TerraformKind.Lower,
                        TargetHeight = pond.Level - (shelf ? shelfDepth : depth), Permanent = true,
                        WaterLevel = pond.Level, WaterAnchor = new float3(anchor.x, pond.Level, anchor.y),
                    });
                    pieces++;
                    if (!shelf) continue;
                    foreach (var deep in Poly.Offset(clean, shelfWidth))
                    {
                        if (deep == null || deep.Length < 3 || Poly.Area(deep) < 8f) continue;
                        ctx.Plan.Terraform.Add(new PlannedTerraform
                        {
                            Ring = Poly.To3D(deep, ctx.H), Kind = TerraformKind.Lower, TargetHeight = pond.Level - depth, Permanent = true,
                        });
                    }
                }
            }
            return pieces;
        }

        /// <summary>Hands each pond to the composer as a claimed place so features keep out of the water.</summary>
        public void EmitPlaces(LayerContext ctx, string tag)
        {
            for (int i = 0; i < Ponds.Count; i++) ctx.AddRegion(Ponds[i].Ring, "water:" + i, tag);
        }

        /// <summary>
        /// Shore points every <paramref name="spacing"/> m with the outward normal (towards dry land),
        /// of the ponds and (outward = into the water) of the islands.
        /// </summary>
        public List<(float2 p, float2 outward, bool island)> Shore(float spacing)
        {
            var r = new List<(float2, float2, bool)>();
            foreach (var pond in Ponds)
            {
                AddShore(pond.Ring, spacing, false, r);
                foreach (var isl in pond.Islands) AddShore(isl, spacing, true, r);
            }
            return r;
        }

        private static void AddShore(float2[] ring, float spacing, bool island, List<(float2, float2, bool)> into)
        {
            var pts = Poly.Resample(ring, spacing);
            int n = pts.Length;
            if (n < 3) return;
            for (int i = 0; i < n; i++)
            {
                var t = math.normalizesafe(pts[(i + 1) % n] - pts[(i + n - 1) % n]);
                into.Add((pts[i], new float2(t.y, -t.x), island));
            }
        }
    }
}
