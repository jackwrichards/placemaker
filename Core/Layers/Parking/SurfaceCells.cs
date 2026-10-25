// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — the idea of the cell engine
// (Geometry/Zellen: Zerlegung, Polygonteiler, Vereinigung, Lochtrennung): "material is a label per
// cell instead of a clipping result; neighbouring cells share identical points, so no tolerance and
// no repair is needed". PLT measured the old clip-and-repair path at 12.5 minutes on a slanted
// L-shape; the cell path built the same shape in 15.4 ms with 0.00 % uncovered.
//
// Implementation (new code): the lot is cut into convex cells. Zone contours (setback line, ring
// road edges) and convex asphalt pieces are subtracted by successive half-plane splits, which keeps
// every cell convex and exact. Cells are then welded, T-junctions resolved, slivers that CS2 would
// drop are relabelled to their neighbour's material, and same-material cells are grown greedily
// into simple, hole-free rings (CS2 areas cannot have holes).

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class SurfaceCells
    {
        /// <summary>Zone bit index (matches LayoutEngine.Z* masks: 1 &lt;&lt; zone).</summary>
        private const int ZoneRing = 2;

        private sealed class Cell
        {
            public List<double2> P;
            public int Zone;
            public bool Asphalt;
            public double2 Min, Max;
            public void UpdateBounds() { PGeo.Bounds(P, out Min, out Max); }
        }

        /// <summary>Upper bound on the vertices of one merged ring (keeps CS2's ear clipping cheap).</summary>
        private const int MaxRingVertices = 240;
        /// <summary>Cells thinner than this (2·area/perimeter) take their neighbour's material.</summary>
        private const double SliverWidth = 0.4;

        public static List<SurfacePoly> Build(List<double2> lot, List<(List<double2> Ring, int Zone)> zones,
            List<AsphaltPiece> pieces, bool allAsphalt, CancellationToken cancel, List<double2[]> zoneChain = null, int[] chainZones = null)
        {
            var result = new List<SurfacePoly>();
            if (allAsphalt)
            {
                var cells0 = Decompose(lot) ?? PGeo.ConvexDecompose(lot);
                result.Add(new SurfacePoly { Ring = new List<double2>(lot), Asphalt = true, Cells = cells0 });
                return result;
            }

            // (perf) Zone cells straight from the bands between the nested outlines (one quad per outline edge) and a
            // decomposition of the innermost ring that keeps its wobble in small cells along the edge. Cutting the lot's
            // convex pieces by every zone's convex pieces instead made thousands of slivers right across a painted lot
            // (13 800 cells on 12.5 ha with 400 corners; the plain hexagon has 2 000).
            var cells = ZoneCells(lot, zoneChain, chainZones);
            if (cells == null)
            {
                cells = new List<Cell>();
                foreach (var part in PGeo.ConvexDecompose(lot))
                {
                    var c = new Cell { P = part, Zone = 0 };
                    c.UpdateBounds();
                    cells.Add(c);
                }
                if (cells.Count == 0) return result;
                foreach (var z in zones)
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach (var part in PGeo.ConvexDecompose(z.Ring))
                        Subtract(cells, part, -1, c => c.Zone = z.Zone);
                }
            }
            if (cells.Count == 0) return result;
            foreach (var c in cells) if (c.Zone == ZoneRing) c.Asphalt = true;

            cancel.ThrowIfCancellationRequested();
            foreach (var piece in pieces)
            {
                if (piece.Poly == null || piece.Poly.Count < 3) continue;
                var poly = new List<double2>(piece.Poly);
                PGeo.EnsureCcw(poly);
                if (PGeo.Area(poly) < 1e-6) continue;
                Subtract(cells, poly, piece.ZoneMask, c => c.Asphalt = true);
            }
            cancel.ThrowIfCancellationRequested();
            return Merge(cells, lot, cancel, MaxRingVertices, true);
        }

        /// <summary>
        /// Re-merges the cells of one surface into smaller rings (at most maxRingVertices boundary
        /// edges each). Used when the game model rejects or trims a large ring: smaller pieces of
        /// the same cells usually pass as they are.
        /// </summary>
        public static List<List<double2>> Regroup(List<List<double2>> cellRings, int maxRingVertices, CancellationToken cancel)
        {
            var cells = new List<Cell>(cellRings.Count);
            foreach (var r in cellRings)
            {
                var c = new Cell { P = new List<double2>(r), Asphalt = true };
                c.UpdateBounds();
                cells.Add(c);
            }
            var result = new List<List<double2>>();
            foreach (var sp in Merge(cells, new List<double2>(), cancel, maxRingVertices, false)) result.Add(sp.Ring);
            return result;
        }

        /// <summary>
        /// Splits every cell overlapping the convex polygon q into its inside part (passed to onInside)
        /// and convex outside fragments. Only cells whose zone bit is in zoneMask take part (-1 = all).
        /// </summary>
        private static void Subtract(List<Cell> cells, List<double2> q, int zoneMask, Action<Cell> onInside)
        {
            PGeo.Bounds(q, out var qmin, out var qmax);
            int count = cells.Count;
            for (int i = 0; i < count; i++)
            {
                var c = cells[i];
                if (zoneMask != -1 && (zoneMask & (1 << c.Zone)) == 0) continue;
                if (c.Max.x <= qmin.x || c.Min.x >= qmax.x || c.Max.y <= qmin.y || c.Min.y >= qmax.y) continue;
                var inside = c.P;
                var fragments = new List<List<double2>>();
                bool empty = false;
                for (int e = 0; e < q.Count && inside != null; e++)
                {
                    var a = q[e]; var b = q[(e + 1) % q.Count];
                    if (PGeo.Len(b - a) < 1e-12) continue;
                    PGeo.SplitConvex(inside, a, b - a, out var left, out var right);
                    if (right != null && PGeo.Area(right) > 1e-9) fragments.Add(right);
                    inside = left;
                    if (inside == null) empty = true;
                }
                if (empty || inside == null || PGeo.Area(inside) < 1e-9) continue;
                int oldZone = c.Zone; bool oldAsphalt = c.Asphalt;
                if (fragments.Count > 0) { c.P = inside; c.UpdateBounds(); }
                onInside(c);
                foreach (var f in fragments)
                {
                    var nc = new Cell { P = f, Zone = oldZone, Asphalt = oldAsphalt };
                    nc.UpdateBounds();
                    cells.Add(nc);
                }
            }
        }

        // ───────────────────────────── zone cells ─────────────────────────────

        /// <summary>
        /// (perf) The zone cells from the nested outlines: between ring k and ring k+1 one quad per edge (outer edge i, the
        /// inset's edge i and the two corner joins), the innermost ring decomposed by <see cref="Decompose"/>. When every
        /// piece is convex and counter-clockwise the quads cover every point between the two rings exactly once (their joins
        /// cancel), so the cells are exact; a quad with one reflex corner goes in as the two triangles beside it. Null when a
        /// band does not tile that way (a quad folds or turns inside out: an eroded inset whose corners jumped far), and
        /// the caller cuts the zones the general way.
        /// </summary>
        private static List<Cell> ZoneCells(List<double2> lot, List<double2[]> chain, int[] zones)
        {
            if (chain == null || zones == null || chain.Count < 1 || zones.Length != chain.Count) return null;
            int n = lot.Count;
            if (n < 3 || chain[0].Length != n) return null;
            for (int i = 0; i < n; i++) if (!chain[0][i].Equals(lot[i])) return null;
            var cells = new List<Cell>();
            var q = new List<double2>(4);
            void AddCell(List<double2> pts, int zone)
            {
                var c = new Cell { P = pts, Zone = zone };
                c.UpdateBounds();
                cells.Add(c);
            }
            for (int k = 0; k + 1 < chain.Count; k++)
            {
                var o = chain[k]; var r = chain[k + 1];
                if (r == null || r.Length != n) return null;
                for (int i = 0; i < n; i++)
                {
                    int j = i + 1 == n ? 0 : i + 1;
                    q.Clear();
                    void Add(double2 p) { if (q.Count == 0 || PGeo.Len(p - q[q.Count - 1]) > 1e-12) q.Add(p); }
                    Add(o[i]); Add(o[j]); Add(r[j]); Add(r[i]);
                    while (q.Count > 1 && PGeo.Len(q[0] - q[q.Count - 1]) <= 1e-12) q.RemoveAt(q.Count - 1);
                    if (q.Count < 3) continue;
                    if (Flat(q)) continue;
                    if (PGeo.IsConvex(q)) { AddCell(new List<double2>(q), zones[k]); continue; }
                    // a quad with one reflex corner (an eroded inset's corner that jumped along a notch): the two triangles
                    // either side of the diagonal from that corner, when both are counter-clockwise
                    if (q.Count == 4)
                    {
                        bool done = false;
                        for (int d = 0; d < 2 && !done; d++)
                        {
                            var t1 = new List<double2> { q[d], q[d + 1], q[d + 2] };
                            var t2 = new List<double2> { q[d], q[d + 2], q[(d + 3) % 4] };
                            if (PGeo.SignedArea(t1) > 1e-12 && PGeo.SignedArea(t2) > 1e-12 && PGeo.IsConvex(t1) && PGeo.IsConvex(t2))
                            {
                                AddCell(t1, zones[k]); AddCell(t2, zones[k]);
                                done = true;
                            }
                        }
                        if (done) continue;
                    }
                    return null;
                }
            }
            var inner = Decompose(PGeo.Dedup(chain[chain.Count - 1], 1e-12));
            if (inner == null) return null;
            foreach (var part in inner)
            {
                var c = new Cell { P = part, Zone = zones[zones.Length - 1] };
                c.UpdateBounds();
                cells.Add(c);
            }
            return cells;
        }

        /// <summary>All points within a micrometre of the line through the two farthest: no area at all.</summary>
        private static bool Flat(List<double2> q)
        {
            double best = -1; int ia = 0, ib = 0;
            for (int a = 0; a < q.Count; a++)
                for (int b = a + 1; b < q.Count; b++)
                {
                    double d = PGeo.Len(q[b] - q[a]);
                    if (d > best) { best = d; ia = a; ib = b; }
                }
            if (best < 1e-9) return true;
            var u = (q[ib] - q[ia]) / best;
            foreach (var p in q) if (Math.Abs(PGeo.Cross(u, p - q[ia])) > 1e-6) return false;
            return true;
        }

        /// <summary>
        /// (perf) Convex cells of a simple ring that keep a painted outline's wobble local: every round cuts off the
        /// convex pockets between consecutive reflex corners (each is a run of convex corners closed by a chord that
        /// stays inside the ring), so the ring left is the one through the reflex corners, about half as many; a convex
        /// ring is the last cell. Ear clipping with merging (PGeo.ConvexDecompose) instead fans long thin pieces right
        /// across the ring, and every stall row then cuts each of them. Cells use the ring's own points. Null when the
        /// ring is not usable; a ring that stops shrinking is finished by PGeo.ConvexDecompose.
        /// </summary>
        internal static List<List<double2>> Decompose(List<double2> ringIn)
        {
            var ring = PGeo.Dedup(ringIn, 1e-9);
            if (ring.Count < 3 || PGeo.SignedArea(ring) == 0) return null;
            PGeo.EnsureCcw(ring);
            var result = new List<List<double2>>();
            for (int round = 0; round < 64; round++)
            {
                int n = ring.Count;
                if (n < 3) return result;
                if (PGeo.IsConvex(ring)) { result.Add(ring); return result; }
                var reflex = new bool[n];
                int first = -1, count = 0;
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i == 0 ? n - 1 : i - 1]; var b = ring[i]; var c = ring[i + 1 == n ? 0 : i + 1];
                    if (PGeo.Cross(b - a, c - b) < -1e-10 * Math.Max(1, PGeo.Len(b - a) * PGeo.Len(c - b))) { reflex[i] = true; count++; if (first < 0) first = i; }
                }
                if (count < 2) break;
                var grid = new RingGrid(ring);
                var core = new List<double2>(count * 2);
                double cut = 0;
                int at = first;
                do
                {
                    int next = at + 1 == n ? 0 : at + 1;
                    while (!reflex[next]) next = next + 1 == n ? 0 : next + 1;
                    core.Add(ring[at]);
                    int span = (next - at + n) % n;   // corners from at to next, next included
                    if (span >= 2)
                    {
                        var pocket = new List<double2>(span + 1);
                        for (int k = 0; k <= span; k++) pocket.Add(ring[(at + k) % n]);
                        if (Pocket(pocket, ring, grid, at, next))
                        {
                            result.Add(pocket);
                            cut += PGeo.SignedArea(pocket);
                        }
                        else for (int k = 1; k < span; k++) core.Add(ring[(at + k) % n]);
                    }
                    at = next;
                } while (at != first);
                if (core.Count == n) break;
                double left = PGeo.SignedArea(core);
                if (core.Count < 3 || left <= 1e-9)
                {
                    // everything went into pockets: they must account for the whole ring
                    return Math.Abs(cut - PGeo.SignedArea(ring)) < 1e-6 * Math.Max(1, PGeo.SignedArea(ring)) ? result : null;
                }
                ring = core;
            }
            var rest = PGeo.ConvexDecompose(ring);
            if (rest.Count == 0) return null;
            // The ear clipper can lose a hairline triangle at a nearly straight reflex corner (its last triangle comes out
            // clockwise and is dropped) and lay a neighbour over it: then the pieces do not tile the ring, and the caller
            // cuts the zones the general way.
            double sum = 0, area = PGeo.SignedArea(ring);
            foreach (var part in rest)
            {
                if (!PGeo.IsConvex(part)) return null;
                sum += PGeo.SignedArea(part);
            }
            if (Math.Abs(sum - area) > 1e-9 * Math.Max(1, area)) return null;
            result.AddRange(rest);
            return result;
        }

        /// <summary>A pocket (ring corners from..to, closed by the chord to → from) that is convex and lies inside the ring.</summary>
        private static bool Pocket(List<double2> pocket, List<double2> ring, RingGrid grid, int from, int to)
        {
            if (!PGeo.IsConvex(pocket)) return false;
            var a = ring[from]; var b = ring[to];
            if (PGeo.Len(b - a) < 1e-6) return false;
            PGeo.Bounds(pocket, out var mn, out var mx);
            int n = ring.Count, span = (to - from + n) % n;
            bool InPocket(int v) => (v - from + n) % n <= span;
            bool ok = true;
            var pad = new double2(1e-6, 1e-6);
            grid.Edges(mn - pad, mx + pad, e =>
            {
                int f = e + 1 == n ? 0 : e + 1;
                // a corner of the rest of the ring inside or on the pocket
                if (!InPocket(e))
                {
                    var p = ring[e];
                    if (p.x >= mn.x - pad.x && p.x <= mx.x + pad.x && p.y >= mn.y - pad.y && p.y <= mx.y + pad.y && InsideOrOn(pocket, p)) { ok = false; return false; }
                }
                // an edge of the rest of the ring touching the chord
                if (!InPocket(e) || !InPocket(f) || (e == to && f != from))
                {
                    if (e == to || f == from) return true;   // the ring's edges at the chord's own corners
                    if (PGeo.SegmentsIntersect(a, b, ring[e], ring[f], 1e-9)) { ok = false; return false; }
                }
                return true;
            });
            return ok;
        }

        private static bool InsideOrOn(List<double2> convex, double2 p)
        {
            for (int i = 0; i < convex.Count; i++)
            {
                var a = convex[i]; var b = convex[(i + 1) % convex.Count];
                var e = b - a; double l = PGeo.Len(e);
                if (l < 1e-12) continue;
                if (PGeo.Cross(e, p - a) / l < -1e-7) return false;
            }
            return true;
        }

        // ───────────────────────────── merge ─────────────────────────────

        private static List<SurfacePoly> Merge(List<Cell> cells, List<double2> lot, CancellationToken cancel, int maxRingVertices, bool collapse)
        {
            // weld vertices (1e-5 m grid, neighbours within 2e-5 m merge)
            var verts = new List<double2>();
            var grid = new Dictionary<long, List<int>>();
            const double q = 1e-5;
            long Key(long x, long y) => (x * 73856093L) ^ (y * 19349663L);
            int Weld(double2 p)
            {
                long gx = (long)Math.Round(p.x / q), gy = (long)Math.Round(p.y / q);
                for (long dx = -2; dx <= 2; dx++)
                    for (long dy = -2; dy <= 2; dy++)
                        if (grid.TryGetValue(Key(gx + dx, gy + dy), out var list))
                            foreach (int id in list)
                                if (math.distance(verts[id], p) <= 2e-5) return id;
                int nid = verts.Count;
                verts.Add(p);
                long k = Key(gx, gy);
                if (!grid.TryGetValue(k, out var l2)) grid[k] = l2 = new List<int>();
                l2.Add(nid);
                return nid;
            }

            var polys = new List<List<int>>();
            var mats = new List<bool>();
            foreach (var c in cells)
            {
                var ids = new List<int>(c.P.Count);
                foreach (var p in c.P)
                {
                    int id = Weld(p);
                    if (ids.Count == 0 || ids[ids.Count - 1] != id) ids.Add(id);
                }
                while (ids.Count > 1 && ids[0] == ids[ids.Count - 1]) ids.RemoveAt(ids.Count - 1);
                if (ids.Count < 3) continue;
                polys.Add(ids);
                mats.Add(c.Asphalt);
            }
            cancel.ThrowIfCancellationRequested();

            // resolve T-junctions: vertices lying on another cell's edge are inserted into it
            const double bucket = 2.0;
            var vgrid = new Dictionary<long, List<int>>();
            for (int i = 0; i < verts.Count; i++)
            {
                long k = Key((long)Math.Floor(verts[i].x / bucket), (long)Math.Floor(verts[i].y / bucket));
                if (!vgrid.TryGetValue(k, out var l)) vgrid[k] = l = new List<int>();
                l.Add(i);
            }
            for (int pi = 0; pi < polys.Count; pi++)
            {
                var ring = polys[pi];
                var outRing = new List<int>(ring.Count + 4);
                for (int e = 0; e < ring.Count; e++)
                {
                    int u = ring[e], v = ring[(e + 1) % ring.Count];
                    outRing.Add(u);
                    var a = verts[u]; var b = verts[v];
                    var ab = b - a; double l2 = PGeo.Dot(ab, ab);
                    if (l2 < 1e-12) continue;
                    var hits = new List<(double T, int Id)>();
                    long x0 = (long)Math.Floor(Math.Min(a.x, b.x) / bucket), x1 = (long)Math.Floor(Math.Max(a.x, b.x) / bucket);
                    long y0 = (long)Math.Floor(Math.Min(a.y, b.y) / bucket), y1 = (long)Math.Floor(Math.Max(a.y, b.y) / bucket);
                    if ((x1 - x0 + 1) * (y1 - y0 + 1) > 4000) continue;
                    for (long gx = x0; gx <= x1; gx++)
                        for (long gy = y0; gy <= y1; gy++)
                            if (vgrid.TryGetValue(Key(gx, gy), out var list))
                                foreach (int w in list)
                                {
                                    if (w == u || w == v) continue;
                                    var p = verts[w];
                                    double t = PGeo.Dot(p - a, ab) / l2;
                                    if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                                    if (PGeo.Len(a + ab * t - p) > 3e-5) continue;
                                    hits.Add((t, w));
                                }
                    if (hits.Count == 0) continue;
                    hits.Sort((h1, h2) => h1.T.CompareTo(h2.T));
                    int last = -1;
                    foreach (var h in hits) if (h.Id != last) { outRing.Add(h.Id); last = h.Id; }
                }
                polys[pi] = outRing;
            }
            cancel.ThrowIfCancellationRequested();

            if (collapse) CollapseShortEdges(polys, mats, verts, lot);
            cancel.ThrowIfCancellationRequested();

            // (perf) edge → cell adjacency as arrays (was a dictionary of directed edges): every directed edge, sorted by its
            // key, finds its reverse (the cell across) by binary search
            int P = polys.Count;
            var start = new int[P + 1];
            for (int pi = 0; pi < P; pi++) start[pi + 1] = start[pi] + polys[pi].Count;
            int E = start[P];
            var eFrom = new int[E]; var eTo = new int[E];
            var keys = new long[E]; var owner = new int[E];
            for (int pi = 0; pi < P; pi++)
            {
                var r = polys[pi];
                for (int e = 0; e < r.Count; e++)
                {
                    int k = start[pi] + e;
                    eFrom[k] = r[e]; eTo[k] = r[(e + 1) % r.Count];
                    keys[k] = EKey(eFrom[k], eTo[k]); owner[k] = pi;
                }
            }
            Array.Sort(keys, owner);
            // a directed edge two cells hold (overlapping input) is the later cell's, as the dictionary kept it
            for (int k = 1; k < E; k++) if (keys[k] == keys[k - 1] && owner[k - 1] > owner[k]) owner[k] = owner[k - 1];
            for (int k = E - 2; k >= 0; k--) if (keys[k] == keys[k + 1] && owner[k + 1] > owner[k]) owner[k] = owner[k + 1];
            var twin = new int[E];
            for (int e = 0; e < E; e++)
            {
                int k = Array.BinarySearch(keys, EKey(eTo[e], eFrom[e]));
                twin[e] = k >= 0 ? owner[k] : -1;
            }
            var group = new int[P];
            for (int i = 0; i < P; i++) group[i] = -1;
            // the neighbours of cell pi (each once, in edge order) that pass the filter, appended
            void Neighbours(int pi, List<int> into, int seedMat)
            {
                int from = into.Count;
                for (int e = start[pi]; e < start[pi + 1]; e++)
                {
                    int o = twin[e];
                    if (o < 0 || o == pi) continue;
                    if (seedMat >= 0 && (group[o] >= 0 || (mats[o] ? 1 : 0) != seedMat)) continue;
                    bool dup = false;
                    for (int k = from; k < into.Count; k++) if (into[k] == o) { dup = true; break; }
                    if (!dup) into.Add(o);
                }
            }

            // slivers take the material of the neighbour they share the longest edge with
            var thin = new bool[P];
            for (int pi = 0; pi < P; pi++)
            {
                AreaPerimeter(polys[pi], verts, out double area, out double per);
                thin[pi] = per > 0 && 2 * area / per < SliverWidth;
            }
            for (int pass = 0; pass < 3; pass++)
            {
                bool changed = false;
                for (int pi = 0; pi < P; pi++)
                {
                    if (!thin[pi]) continue;
                    double same = 0, other = 0;
                    for (int e = start[pi]; e < start[pi + 1]; e++)
                    {
                        int o = twin[e];
                        if (o < 0 || o == pi) continue;
                        double len = PGeo.Len(verts[eTo[e]] - verts[eFrom[e]]);
                        if (mats[o] == mats[pi]) same += len; else other += len;
                    }
                    if (other > same) { mats[pi] = !mats[pi]; changed = true; }
                }
                if (!changed) break;
            }

            // greedy growth into simple hole-free rings
            int nv = verts.Count;
            var vcnt = new int[nv]; var vstamp = new int[nv];
            for (int i = 0; i < nv; i++) vstamp[i] = -1;
            var tries = new int[P]; var triesStamp = new int[P];
            for (int i = 0; i < P; i++) triesStamp[i] = -1;
            var shared = new bool[16];
            int gid = -1, stamp = -1, boundaryCount = 0;
            int VCount(int v) => vstamp[v] == stamp ? vcnt[v] : 0;
            void VAdd(int v, int d) { if (vstamp[v] != stamp) { vstamp[v] = stamp; vcnt[v] = 0; } vcnt[v] += d; }
            bool TryAdd(int pi, List<int> members)
            {
                int lo = start[pi], n = start[pi + 1] - lo;
                if (shared.Length < n) shared = new bool[n * 2];
                int sharedCount = 0;
                for (int e = 0; e < n; e++)
                {
                    int o = twin[lo + e];
                    shared[e] = o >= 0 && o != pi && group[o] == gid;
                    if (shared[e]) sharedCount++;
                }
                if (members.Count > 0)
                {
                    if (sharedCount == 0 || sharedCount == n) return false;
                    int transitions = 0;
                    for (int e = 0; e < n; e++) if (shared[e] && !shared[(e + 1) % n]) transitions++;
                    if (transitions != 1) return false;
                    // vertices not on the shared chain must be new to the boundary (else a pinch/hole)
                    for (int e = 0; e < n; e++)
                    {
                        bool onChain = shared[e] || shared[e == 0 ? n - 1 : e - 1];
                        if (!onChain && VCount(eFrom[lo + e]) > 0) return false;
                    }
                    if (boundaryCount + n - 2 * sharedCount > maxRingVertices) return false;
                }
                for (int e = 0; e < n; e++)
                {
                    int a = eFrom[lo + e], b = eTo[lo + e];
                    int d = shared[e] ? -1 : 1;
                    VAdd(a, d); VAdd(b, d);
                }
                boundaryCount += n - 2 * sharedCount;
                members.Add(pi);
                group[pi] = gid;
                return true;
            }
            var groups = new List<List<int>>();
            var groupAsphalt = new List<bool>();
            var queue = new List<int>();
            for (int seed = 0; seed < P; seed++)
            {
                if (group[seed] >= 0) continue;
                cancel.ThrowIfCancellationRequested();
                gid = groups.Count; stamp++; boundaryCount = 0;
                var members = new List<int>();
                int seedMat = mats[seed] ? 1 : 0;
                TryAdd(seed, members);
                queue.Clear();
                Neighbours(seed, queue, -1);
                for (int qi = 0; qi < queue.Count; qi++)
                {
                    int c = queue[qi];
                    if (group[c] >= 0 || mats[c] != mats[seed]) continue;
                    if (TryAdd(c, members)) Neighbours(c, queue, seedMat);
                    else
                    {
                        int t = triesStamp[c] == gid ? tries[c] : 0;
                        if (t < 3) { tries[c] = t + 1; triesStamp[c] = gid; queue.Add(c); }
                    }
                    if (queue.Count > 20 * P + 100) break;
                }
                groups.Add(members);
                groupAsphalt.Add(mats[seed]);
            }

            // (perf/look) A sliver left on its own (it could not join its own material without enclosing the other: a thin
            // strip of grass cut off between stalls and a wobbly outline) is offered to the other material's ring beside it:
            // alone it is a hairline ring the game refuses (AreaRings rule 1), leaving a gap.
            for (int g = 0; g < groups.Count; g++)
            {
                if (groups[g].Count != 1) continue;
                int pi = groups[g][0];
                if (!thin[pi]) continue;
                for (int e = start[pi]; e < start[pi + 1]; e++)
                {
                    int o = twin[e];
                    if (o < 0 || o == pi || mats[o] == mats[pi] || group[o] < 0 || groups[group[o]].Count == 0) continue;
                    int target = group[o];
                    // the target ring's boundary, rebuilt
                    gid = target; stamp++; boundaryCount = 0;
                    foreach (int m in groups[target])
                        for (int k = start[m]; k < start[m + 1]; k++)
                        {
                            int t = twin[k];
                            if (t >= 0 && t != m && group[t] == target) continue;
                            boundaryCount++; VAdd(eFrom[k], 1); VAdd(eTo[k], 1);
                        }
                    group[pi] = -1;
                    if (TryAdd(pi, groups[target]))
                    {
                        mats[pi] = !mats[pi];
                        groups[g].Clear();
                        break;
                    }
                    group[pi] = g;
                }
            }

            var result = new List<SurfacePoly>();
            var nextOf = new int[nv]; var nextStamp = new int[nv];
            for (int i = 0; i < nv; i++) nextStamp[i] = -1;
            for (int g = 0; g < groups.Count; g++)
            {
                var members = groups[g];
                if (members.Count == 0) continue;
                var ring = TraceRing(g, members);
                var cellPts = new List<List<double2>>(members.Count);
                foreach (int m in members) cellPts.Add(Points(polys[m], verts));
                if (ring == null)
                {
                    foreach (var cp in cellPts)
                        result.Add(new SurfacePoly { Ring = cp, Asphalt = groupAsphalt[g], Cells = new List<List<double2>> { cp } });
                    continue;
                }
                result.Add(new SurfacePoly { Ring = ring, Asphalt = groupAsphalt[g], Cells = cellPts });
            }
            return result;

            // the group's boundary (member edges with no member across) walked into one ring; null when it is not one loop
            List<double2> TraceRing(int g, List<int> members)
            {
                int count = 0, first = int.MaxValue;
                foreach (int m in members)
                    for (int k = start[m]; k < start[m + 1]; k++)
                    {
                        int t = twin[k];
                        if (t >= 0 && t != m && group[t] == g) continue;
                        int a = eFrom[k];
                        if (nextStamp[a] == g) return null;
                        nextStamp[a] = g; nextOf[a] = eTo[k];
                        count++;
                        if (a < first) first = a;
                    }
                if (count < 3) return null;
                var ids = new List<int>(count);
                int cur = first;
                for (int guard = 0; guard <= count; guard++)
                {
                    ids.Add(cur);
                    if (nextStamp[cur] != g) return null;
                    cur = nextOf[cur];
                    if (cur == first) break;
                }
                if (ids.Count != count) return null;
                var simple = PGeo.Simplify(Points(ids, verts), 1e-6, 1e-7);
                if (simple.Count < 3) return null;
                PGeo.EnsureCcw(simple);
                return simple;
            }
        }

        private static long EKey(int a, int b) => ((long)a << 32) | (uint)b;

        private static void AreaPerimeter(List<int> ids, List<double2> verts, out double area, out double perimeter)
        {
            double s = 0, p = 0;
            int n = ids.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var a = verts[ids[j]]; var b = verts[ids[i]];
                s += a.x * b.y - b.x * a.y;
            }
            for (int i = 0; i < n; i++) p += PGeo.Len(verts[ids[(i + 1) % n]] - verts[ids[i]]);
            area = Math.Abs(s * 0.5); perimeter = p;
        }

        private static double SignedArea(List<int> ids, List<double2> verts)
        {
            double s = 0;
            int n = ids.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                s += verts[ids[j]].x * verts[ids[i]].y - verts[ids[i]].x * verts[ids[j]].y;
            return s * 0.5;
        }

        /// <summary>
        /// Collapses every edge shorter than CS2's minimum (PLT Cs2Mindestkante 0.375 m) into one
        /// shared vertex. Doing it on the welded vertex graph keeps neighbours consistent: CS2-side
        /// cleaning would move one ring's corner and open a gap or an overlap with the next ring.
        /// Clusters are capped at 0.8 m across so a chain of short edges cannot drag a corner far.
        /// (perf) Only the vertices of clusters and the cells holding one are visited after the first pass; the overlap
        /// check keeps the other cells' edges in a grid built once.
        /// </summary>
        private static void CollapseShortEdges(List<List<int>> polys, List<bool> mats, List<double2> verts, List<double2> lot)
        {
            const double minEdge = 0.4, maxSpread = 0.8, droppable = 0.05;
            int n = verts.Count;
            var orig = verts.ToArray();
            var parent = new int[n];
            var lo = new double2[n]; var hi = new double2[n];
            for (int i = 0; i < n; i++) { parent[i] = i; lo[i] = hi[i] = verts[i]; }
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            bool any = false;
            foreach (var r in polys)
                for (int e = 0; e < r.Count; e++)
                {
                    int a0 = Find(r[e]), b0 = Find(r[(e + 1) % r.Count]);
                    if (a0 == b0) continue;
                    if (PGeo.Len(verts[r[e]] - verts[r[(e + 1) % r.Count]]) >= minEdge) continue;
                    var l2 = math.min(lo[a0], lo[b0]); var h2 = math.max(hi[a0], hi[b0]);
                    if (PGeo.Len(h2 - l2) > maxSpread) continue;
                    int root = Math.Min(a0, b0), other = Math.Max(a0, b0);
                    parent[other] = root; lo[root] = l2; hi[root] = h2;
                    any = true;
                }
            if (!any) return;

            // the vertices in a cluster (clusters only ever break up below) and the cells that hold one
            var members = new List<int>();
            {
                var size0 = new int[n];
                for (int i = 0; i < n; i++) size0[Find(i)]++;
                for (int i = 0; i < n; i++) if (size0[Find(i)] > 1) members.Add(i);
            }
            var inCluster = new bool[n];
            foreach (int m in members) inCluster[m] = true;
            var touchedPolys = new List<int>();
            var isTouched = new bool[polys.Count];
            for (int pi = 0; pi < polys.Count; pi++)
                foreach (int v in polys[pi]) if (inCluster[v]) { touchedPolys.Add(pi); isTouched[pi] = true; break; }

            // The outline must not move: a cluster holding a lot corner collapses onto it, one holding
            // outline points collapses onto their mean (they lie on the same outline edge).
            var weight = new int[n];
            var lotGrid = lot.Count >= 3 ? new RingGrid(lot) : null;
            foreach (int i in members)
            {
                if (lotGrid == null) continue;
                var p = orig[i];
                bool corner = false;
                var pad = new double2(2e-6, 2e-6);
                lotGrid.Edges(p - pad, p + pad, e => { if (PGeo.Len(lot[e] - p) < 1e-6) { corner = true; return false; } return true; });
                weight[i] = corner ? 2 : lotGrid.Dist(p, 1e-5) < 1e-6 ? 1 : 0;
            }

            List<int> Remap(List<int> r)
            {
                var nr = new List<int>(r.Count);
                foreach (int v in r) { int q = Find(v); if (nr.Count == 0 || nr[nr.Count - 1] != q) nr.Add(q); }
                while (nr.Count > 1 && nr[0] == nr[nr.Count - 1]) nr.RemoveAt(nr.Count - 1);
                return nr;
            }
            // choice[root]: -1 = mean of the cluster's highest-weight members, k ≥ 0 = its k-th such member
            var choice = new int[n];
            for (int i = 0; i < n; i++) choice[i] = -1;
            var top = new int[n]; var sum = new double2[n]; var cnt = new int[n];
            var pick = new double2[n]; var hasPick = new bool[n];
            void Place()
            {
                foreach (int i in members) { int r = Find(i); top[r] = 0; sum[r] = new double2(0, 0); cnt[r] = 0; hasPick[r] = false; }
                foreach (int i in members) { int r = Find(i); top[r] = Math.Max(top[r], weight[i]); }
                foreach (int i in members)
                {
                    int r = Find(i);
                    if (weight[i] != top[r]) continue;
                    if (choice[r] >= 0 && cnt[r] == choice[r]) { pick[r] = orig[i]; hasPick[r] = true; }
                    sum[r] += orig[i]; cnt[r]++;
                }
                foreach (int i in members)
                {
                    int r = Find(i);
                    verts[i] = r == i && cnt[r] > 0 ? (hasPick[r] ? pick[r] : sum[r] / cnt[r]) : orig[i];
                }
            }
            bool HasCluster(int root) => !verts[root].Equals(orig[root]);

            // the overlap check's grid: 2 m cells, an undirected edge once (as (a, b) with a ≤ b), edges over more than 2 000
            // cells left out; the edges of untouched cells never move and are filed once
            const double bucket = 2.0;
            PGeo.Bounds(orig, out var vmin, out var vmax);
            long gx0 = (long)Math.Floor(vmin.x / bucket) - 2, gy0 = (long)Math.Floor(vmin.y / bucket) - 2;
            int gnx = (int)((long)Math.Floor(vmax.x / bucket) + 2 - gx0 + 1), gny = (int)((long)Math.Floor(vmax.y / bucket) + 2 - gy0 + 1);
            int[] sStart = null, sItems = null;
            var sEdges = new List<int2>();
            var dEdges = new List<int2>();
            var dCount = new int[gnx * gny + 1]; var dStart = new int[gnx * gny + 1];
            var dItems = new int[256];
            bool CellRange(double2 p0, double2 p1, out long x0, out long x1, out long y0, out long y1)
            {
                x0 = (long)Math.Floor(Math.Min(p0.x, p1.x) / bucket); x1 = (long)Math.Floor(Math.Max(p0.x, p1.x) / bucket);
                y0 = (long)Math.Floor(Math.Min(p0.y, p1.y) / bucket); y1 = (long)Math.Floor(Math.Max(p0.y, p1.y) / bucket);
                return (x1 - x0 + 1) * (y1 - y0 + 1) <= 2000;
            }
            void BuildStatic()
            {
                for (int pi = 0; pi < polys.Count; pi++)
                {
                    if (isTouched[pi]) continue;
                    var r = polys[pi];
                    for (int e = 0; e < r.Count; e++)
                    {
                        int a = r[e], b = r[(e + 1) % r.Count];
                        if (a > b) continue;
                        if (!CellRange(orig[a], orig[b], out _, out _, out _, out _)) continue;
                        sEdges.Add(new int2(a, b));
                    }
                }
                var count = new int[gnx * gny + 1];
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1)
                    {
                        for (int i = 1; i < count.Length; i++) count[i] += count[i - 1];
                        sStart = (int[])count.Clone();
                        sItems = new int[count[count.Length - 1]];
                    }
                    for (int k = 0; k < sEdges.Count; k++)
                    {
                        CellRange(orig[sEdges[k].x], orig[sEdges[k].y], out long x0, out long x1, out long y0, out long y1);
                        for (long x = x0; x <= x1; x++)
                            for (long y = y0; y <= y1; y++)
                            {
                                long cx = x - gx0, cy = y - gy0;
                                if (cx < 0 || cy < 0 || cx >= gnx || cy >= gny) continue;
                                int c = (int)(cx + cy * gnx);
                                if (pass == 0) count[c + 1]++; else sItems[count[c]++] = k;
                            }
                    }
                }
            }

            // Moving vertices can fold a neighbouring cell over its neighbour (seen as 2 cm overlaps on
            // a rotated T-shape). A cluster that folds a cell first tries each member's own position,
            // then gives up (keeps its short edges; CS2-side cleaning handles those).
            var badRoots = new HashSet<int>();
            var giveUp = new HashSet<int>();
            var size = new int[n]; var roots = new int[n];
            var dirty = new bool[polys.Count];
            var polysOf = new List<int>[n];
            foreach (int m in members) polysOf[m] = new List<int>(4);
            foreach (int pi in touchedPolys)
            {
                dirty[pi] = true;
                foreach (int v in polys[pi])
                    if (inCluster[v] && (polysOf[v].Count == 0 || polysOf[v][polysOf[v].Count - 1] != pi)) polysOf[v].Add(pi);
            }
            for (int round = 0; round < 12; round++)
            {
                Place();
                bool changed = false;
                badRoots.Clear();
                foreach (int pi in touchedPolys)
                {
                    // (perf) a cell none of whose clusters changed since it was found good is still good
                    if (!dirty[pi]) continue;
                    dirty[pi] = false;
                    var r = polys[pi];
                    bool touched = false;
                    foreach (int v in r) if (Find(v) != v || !verts[v].Equals(orig[v])) { touched = true; break; }
                    if (!touched) continue;
                    var nr = Remap(r);
                    double area0 = PGeo.SignedArea(Points(r, orig));
                    bool ok;
                    // Fully collapsed (fewer than 3 distinct vertices): its neighbours moved with it, so
                    // nothing opens up. A remnant must stay a valid ring; thin is fine (the sliver
                    // relabelling and merging below absorb it).
                    if (nr.Count < 3) ok = true;
                    else
                    {
                        var pts = Points(nr, verts);
                        bool dup = new HashSet<int>(nr).Count != nr.Count;
                        ok = !dup && PGeo.SignedArea(pts) > 1e-9 && PGeo.IsSimple(pts);
                        if (!ok && area0 < droppable) ok = true; // a tiny cell may vanish
                    }
                    if (ok) continue;
                    foreach (int v in r) { int root = Find(v); if (root != v || HasCluster(root)) badRoots.Add(root); }
                    changed = true;
                }
                // A moved corner can also push a cell across a neighbour that did not move (no fold,
                // but an overlap): look for edges of moved cells crossing any other edge.
                if (!changed)
                {
                    if (sStart == null) BuildStatic();
                    // the touched cells' edges as they are now, in the same cells (flat tables)
                    var remapped = new List<int>[polys.Count];
                    dEdges.Clear();
                    foreach (int pi in touchedPolys)
                    {
                        var r = Remap(polys[pi]);
                        remapped[pi] = r;
                        for (int e = 0; e < r.Count; e++)
                        {
                            int a = r[e], b = r[(e + 1) % r.Count];
                            if (a > b) continue;
                            if (!CellRange(verts[a], verts[b], out _, out _, out _, out _)) continue;
                            dEdges.Add(new int2(a, b));
                        }
                    }
                    Array.Clear(dCount, 0, dCount.Length);
                    for (int pass = 0; pass < 2; pass++)
                    {
                        if (pass == 1)
                        {
                            for (int i = 1; i < dCount.Length; i++) dCount[i] += dCount[i - 1];
                            Array.Copy(dCount, dStart, dCount.Length);
                            if (dItems.Length < dCount[dCount.Length - 1]) dItems = new int[dCount[dCount.Length - 1] * 2];
                        }
                        for (int k = 0; k < dEdges.Count; k++)
                        {
                            CellRange(verts[dEdges[k].x], verts[dEdges[k].y], out long x0, out long x1, out long y0, out long y1);
                            for (long x = x0; x <= x1; x++)
                                for (long y = y0; y <= y1; y++)
                                {
                                    long cx = x - gx0, cy = y - gy0;
                                    if (cx < 0 || cy < 0 || cx >= gnx || cy >= gny) continue;
                                    int c = (int)(cx + cy * gnx);
                                    if (pass == 0) dCount[c + 1]++; else dItems[dCount[c]++] = k;
                                }
                        }
                    }
                    foreach (int pi in touchedPolys)
                    {
                        var r = remapped[pi];
                        bool moved = false;
                        foreach (int v in r) if (!verts[v].Equals(orig[v])) { moved = true; break; }
                        if (!moved || r.Count < 2) continue;
                        bool cross = false;
                        for (int e = 0; e < r.Count && !cross; e++)
                        {
                            int a = r[e], b = r[(e + 1) % r.Count];
                            if (!CellRange(verts[a], verts[b], out long x0, out long x1, out long y0, out long y1)) continue;
                            for (long x = x0; x <= x1 && !cross; x++)
                                for (long y = y0; y <= y1 && !cross; y++)
                                {
                                    long cx = x - gx0, cy = y - gy0;
                                    if (cx < 0 || cy < 0 || cx >= gnx || cy >= gny) continue;
                                    int c = (int)(cx + cy * gnx);
                                    for (int k = sStart[c]; k < sStart[c + 1]; k++)
                                    {
                                        var cd = sEdges[sItems[k]];
                                        if (cd.x == a || cd.x == b || cd.y == a || cd.y == b) continue;
                                        if (PGeo.SegmentsCross(verts[a], verts[b], verts[cd.x], verts[cd.y], 1e-12)) { cross = true; break; }
                                    }
                                    for (int k = dStart[c]; k < dStart[c + 1] && !cross; k++)
                                    {
                                        var cd = dEdges[dItems[k]];
                                        if (cd.x == a || cd.x == b || cd.y == a || cd.y == b) continue;
                                        if (PGeo.SegmentsCross(verts[a], verts[b], verts[cd.x], verts[cd.y], 1e-12)) { cross = true; break; }
                                    }
                                }
                        }
                        if (!cross) continue;
                        foreach (int v in polys[pi]) { int root = Find(v); if (root != v || HasCluster(root)) badRoots.Add(root); }
                        changed = true;
                    }
                }
                if (!changed) break;
                // cluster sizes and roots: only cluster vertices can be in a bad cluster
                foreach (int i in members) size[i] = 0;
                foreach (int i in members) { int r = Find(i); roots[i] = r; size[r]++; }
                // every cell holding a vertex of a cluster that changes is checked again next round
                foreach (int i in members)
                    if (badRoots.Contains(roots[i])) foreach (int pi in polysOf[i]) dirty[pi] = true;
                giveUp.Clear();
                foreach (int r in badRoots)
                {
                    choice[r]++;
                    if (choice[r] >= size[r] || round >= 10) giveUp.Add(r);
                }
                if (giveUp.Count > 0)
                    foreach (int i in members) if (giveUp.Contains(roots[i])) { parent[i] = i; choice[i] = -1; }
            }
            Place();

            var keptPolys = new List<List<int>>(polys.Count);
            var keptMats = new List<bool>(polys.Count);
            var seen = new int[n];
            for (int i = 0; i < n; i++) seen[i] = -1;
            for (int pi = 0; pi < polys.Count; pi++)
            {
                var nr = isTouched[pi] ? Remap(polys[pi]) : polys[pi];
                bool ok = nr.Count >= 3;
                if (ok) foreach (int v in nr) { if (seen[v] == pi) { ok = false; break; } seen[v] = pi; }
                ok = ok && SignedArea(nr, verts) > 1e-9;
                if (!ok) continue;
                keptPolys.Add(nr); keptMats.Add(mats[pi]);
            }
            polys.Clear(); polys.AddRange(keptPolys);
            mats.Clear(); mats.AddRange(keptMats);
        }

        private static List<double2> Points(List<int> ids, List<double2> verts)
        {
            var r = new List<double2>(ids.Count);
            foreach (int i in ids) r.Add(verts[i]);
            return r;
        }

        private static List<double2> Points(List<int> ids, double2[] verts)
        {
            var r = new List<double2>(ids.Count);
            foreach (int i in ids) r.Add(verts[i]);
            return r;
        }
    }
}
