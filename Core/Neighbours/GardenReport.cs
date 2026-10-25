// Placemaker neighbourhood polish — what Outgrow's gardens look like, measured (pure geometry, engine-free).
//
// The user, 24 Oct: "flag every garden that has more than a certain number of edges. And if it's a shared edge, that counts
// as well ... a breakdown of all garden edge amounts for each garden ... a breakdown of all the angles of a garden ... If a
// garden has really tight angles, I want to know about it." A garden is the outline its last plan wrote (PolishGarden in the
// game, plan.Plots offline); every edge of it counts, shared or not (a garden shares an edge with each neighbour it touches:
// both have it). The game runner's lab op gardenReport and OutgrowBench --gardens both measure with this.
//
// Per garden: its corners (points where the outline turns by 2 degrees or more; nearer than 5 cm: one point) = its edges;
// each corner's inside angle; each edge's length and the neighbours it is shared with; how far it turns in all (a garden with
// no notch turns 360 degrees; every degree past that is a bend out and back); flags. In all: how many edges gardens have,
// how their angles fall, the worst gardens and why.
//
// A garden's side that follows a curved road is one edge (the user, 24 Oct: "Should a garden edge that follows a curved road
// count as one edge? Yes ... But that's only where I would say that"): slight bends whose two edges both run along a road's
// edge (within 2 m) are not corners.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static class GardenReport
    {
        public sealed class Input
        {
            public long Id;
            public float2 Position, Forward, HalfLot;
            public float2[] Ring;
            /// <summary>Which test case or part of town it is in (the report groups by it); null: none.</summary>
            public string Place;
        }

        public sealed class Corner
        {
            public float2 At;
            /// <summary>The inside angle (degrees, 0-360: under 180 a corner out, over 180 a corner in).</summary>
            public float Angle;
        }

        public sealed class Edge
        {
            public float2 A, B;
            public float Length;
            /// <summary>The neighbours whose gardens run along it (within 0.3 m, for 1 m or more).</summary>
            public List<long> SharedWith = new List<long>();
        }

        public sealed class Garden
        {
            public long Id;
            public string Place;
            public float2 Position;
            public int RawPoints;
            public List<Corner> Corners = new List<Corner>();
            public List<Edge> Edges = new List<Edge>();
            public float Area, Perimeter, Turning;
            public List<string> Flags = new List<string>();
            public float Score;
        }

        public sealed class Options
        {
            /// <summary>More edges than this: flagged.</summary>
            public int MaxEdges = 8;
            /// <summary>An inside angle under this, or over 360 less it: a tight corner (a spike or a deep notch).</summary>
            public float TightAngle = 45f;
            /// <summary>An edge shorter than this (m): a fence piece sticks out of it.</summary>
            public float ShortEdge = 2f;
            /// <summary>A turn under this (degrees, past the 2 that count as a corner): a slight bend, a kink in a line.</summary>
            public float SlightBend = 15f;
            /// <summary>An edge whose ends and middle lie this close to a road's edge (m) runs along the road: a slight bend between
            /// two such edges follows the road's curve and is no corner.</summary>
            public float AlongRoad = 2f;
        }

        /// <param name="roads">The road surfaces (null: none; then every bend counts).</param>
        public static List<Garden> Measure(IReadOnlyList<Input> gardens, Options o = null, IReadOnlyList<float2[]> roads = null)
        {
            o = o ?? new Options();
            var roadBoxes = roads?.Select(r => (Lo: r.Aggregate(new float2(float.MaxValue), math.min) - o.AlongRoad, Hi: r.Aggregate(new float2(float.MinValue), math.max) + o.AlongRoad)).ToList();
            bool NearRoad(float2 p)
            {
                for (int i = 0; i < roads.Count; i++)
                {
                    var (lo, hi) = roadBoxes[i];
                    if (p.x < lo.x || p.y < lo.y || p.x > hi.x || p.y > hi.y) continue;
                    var r = roads[i];
                    for (int k = 0; k < r.Length; k++)
                    {
                        var a = r[k];
                        var ab = r[(k + 1) % r.Length] - a;
                        float u = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.lengthsq(ab)));
                        if (math.distancesq(p, a + ab * u) <= o.AlongRoad * o.AlongRoad) return true;
                    }
                }
                return false;
            }
            var result = new List<Garden>(gardens.Count);
            foreach (var g in gardens)
            {
                if (g.Ring == null || g.Ring.Length < 3) continue;
                var ring = Clean(g.Ring);
                if (ring.Count < 3) continue;
                if (roads != null && roads.Count > 0) ring = FollowRoads(ring, o, NearRoad);
                if (ring.Count < 3) continue;
                var m = new Garden { Id = g.Id, Place = g.Place, Position = g.Position, RawPoints = g.Ring.Length };
                double area2 = 0;
                for (int k = 0; k < ring.Count; k++) { var p = ring[k]; var q = ring[(k + 1) % ring.Count]; area2 += (double)p.x * q.y - (double)q.x * p.y; }
                bool ccw = area2 > 0;
                m.Area = (float)math.abs(area2) * 0.5f;
                for (int k = 0; k < ring.Count; k++)
                {
                    var prev = ring[(k + ring.Count - 1) % ring.Count];
                    var p = ring[k];
                    var next = ring[(k + 1) % ring.Count];
                    // the turn at p (left positive), as the outline runs counter-clockwise: inside angle = 180 - turn
                    var din = math.normalizesafe(p - prev);
                    var dout = math.normalizesafe(next - p);
                    float turn = math.degrees(math.atan2(din.x * dout.y - din.y * dout.x, math.dot(din, dout)));
                    if (!ccw) turn = -turn;
                    m.Corners.Add(new Corner { At = p, Angle = 180f - turn });
                    m.Turning += math.abs(turn);
                    var e = new Edge { A = p, B = next, Length = math.distance(p, next) };
                    m.Edges.Add(e);
                    m.Perimeter += e.Length;
                }
                result.Add(m);
            }
            // shared edges: another garden's edge along it
            var boxes = result.Select(r => (Lo: r.Corners.Aggregate(new float2(float.MaxValue), (a, c) => math.min(a, c.At)) - 1f, Hi: r.Corners.Aggregate(new float2(float.MinValue), (a, c) => math.max(a, c.At)) + 1f)).ToList();
            for (int i = 0; i < result.Count; i++)
                for (int j = 0; j < result.Count; j++)
                {
                    if (i == j || boxes[j].Lo.x > boxes[i].Hi.x || boxes[j].Hi.x < boxes[i].Lo.x || boxes[j].Lo.y > boxes[i].Hi.y || boxes[j].Hi.y < boxes[i].Lo.y) continue;
                    foreach (var e in result[i].Edges)
                        foreach (var f in result[j].Edges)
                            if (Along(e, f) && !e.SharedWith.Contains(result[j].Id)) e.SharedWith.Add(result[j].Id);
                }
            foreach (var m in result) Flag(m, o);
            return result;
        }

        /// <summary>The outline with repeated points and points where it runs straight on (turns under 2 degrees) left out.</summary>
        private static List<float2> Clean(float2[] ring)
        {
            var pts = new List<float2>();
            foreach (var p in ring) if (pts.Count == 0 || math.distance(pts[pts.Count - 1], p) >= 0.05f) pts.Add(p);
            if (pts.Count > 1 && math.distance(pts[0], pts[pts.Count - 1]) < 0.05f) pts.RemoveAt(pts.Count - 1);
            bool changed = true;
            while (changed && pts.Count > 3)
            {
                changed = false;
                for (int k = 0; k < pts.Count && pts.Count > 3; k++)
                {
                    var prev = pts[(k + pts.Count - 1) % pts.Count];
                    var next = pts[(k + 1) % pts.Count];
                    var din = math.normalizesafe(pts[k] - prev);
                    var dout = math.normalizesafe(next - pts[k]);
                    if (math.dot(din, dout) >= math.cos(math.radians(2f))) { pts.RemoveAt(k); changed = true; k--; }
                }
            }
            return pts;
        }

        /// <summary>The outline with the slight bends between two edges along a road left out (a side following a curved road).</summary>
        private static List<float2> FollowRoads(List<float2> ring, Options o, Func<float2, bool> nearRoad)
        {
            int n = ring.Count;
            var along = new bool[n];
            for (int k = 0; k < n; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % n];
                along[k] = nearRoad(a) && nearRoad(b) && nearRoad((a + b) * 0.5f);
            }
            var kept = new List<float2>(n);
            for (int k = 0; k < n; k++)
            {
                if (along[(k + n - 1) % n] && along[k])
                {
                    var din = math.normalizesafe(ring[k] - ring[(k + n - 1) % n]);
                    var dout = math.normalizesafe(ring[(k + 1) % n] - ring[k]);
                    if (math.dot(din, dout) >= math.cos(math.radians(o.SlightBend))) continue;
                }
                kept.Add(ring[k]);
            }
            return kept.Count >= 3 ? kept : ring;
        }

        private static bool Along(Edge e, Edge f)
        {
            var d = e.B - e.A;
            float len = math.length(d);
            if (len < 0.5f) return false;
            d /= len;
            var g = f.B - f.A;
            float glen = math.length(g);
            if (glen < 0.5f || math.abs(math.dot(d, g / glen)) < 0.995f) return false;
            var n = new float2(-d.y, d.x);
            if (math.abs(math.dot(f.A - e.A, n)) > 0.3f || math.abs(math.dot(f.B - e.A, n)) > 0.3f) return false;
            float u0 = math.dot(f.A - e.A, d), u1 = math.dot(f.B - e.A, d);
            return math.min(len, math.max(u0, u1)) - math.max(0f, math.min(u0, u1)) >= 1f;
        }

        private static void Flag(Garden m, Options o)
        {
            int edges = m.Edges.Count;
            int spikes = m.Corners.Count(c => c.Angle < o.TightAngle);
            int notches = m.Corners.Count(c => c.Angle > 360f - o.TightAngle);
            int slight = m.Corners.Count(c => math.abs(180f - c.Angle) < o.SlightBend);
            int shortEdges = m.Edges.Count(e => e.Length < o.ShortEdge);
            float extraTurn = math.max(0f, m.Turning - 360f);
            if (edges > o.MaxEdges) m.Flags.Add($"{edges} edges");
            if (spikes > 0) m.Flags.Add($"{spikes} spike{(spikes > 1 ? "s" : "")} (corner under {o.TightAngle:0} deg)");
            if (notches > 0) m.Flags.Add($"{notches} deep notch{(notches > 1 ? "es" : "")} (corner over {360f - o.TightAngle:0} deg)");
            if (slight > 0) m.Flags.Add($"{slight} slight bend{(slight > 1 ? "s" : "")} (under {o.SlightBend:0} deg)");
            if (shortEdges > 0) m.Flags.Add($"{shortEdges} edge{(shortEdges > 1 ? "s" : "")} under {o.ShortEdge:0.#} m");
            if (extraTurn > 1f) m.Flags.Add($"turns {extraTurn:0} deg more than a plain shape (bends out and back)");
            m.Score = math.max(0, edges - 4) + 4 * spikes + 4 * notches + 2 * slight + 2 * shortEdges + extraTurn / 45f;
        }

        // ── the breakdown ──

        public static string Text(List<Garden> gardens, Options o = null, int worst = 20)
        {
            o = o ?? new Options();
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.Append($"{gardens.Count} gardens\n");
            if (gardens.Count == 0) return sb.ToString();
            var edges = gardens.Select(g => g.Edges.Count).OrderBy(x => x).ToList();
            sb.Append($"edges per garden (shared ones counted by each garden): mean {edges.Average():F1}, median {edges[edges.Count / 2]}, most {edges.Max()}\n");
            sb.Append("  how many gardens have n edges:  " + string.Join("  ", edges.GroupBy(x => x).OrderBy(gr => gr.Key).Select(gr => $"{gr.Key}: {gr.Count()}")) + "\n");
            sb.Append($"  over {o.MaxEdges} edges: {gardens.Count(g => g.Edges.Count > o.MaxEdges)} gardens\n");
            var shared = gardens.SelectMany(g => g.Edges).ToList();
            sb.Append($"shared edges: {shared.Count(e => e.SharedWith.Count > 0)} of {shared.Count} ({100f * shared.Count(e => e.SharedWith.Count > 0) / math.max(1, shared.Count):F0}%); per garden {gardens.Average(g => g.Edges.Count(e => e.SharedWith.Count > 0)):F1} shared, {gardens.Average(g => g.Edges.Count(e => e.SharedWith.Count == 0)):F1} its own\n");
            var angles = gardens.SelectMany(g => g.Corners.Select(c => c.Angle)).ToList();
            (string Name, float Lo, float Hi)[] bins =
            {
                ("spike, under 45", 0f, 45f), ("sharp, 45-75", 45f, 75f), ("square, 75-105", 75f, 105f), ("wide, 105-165", 105f, 165f),
                ("slight bend out, 165-178", 165f, 178f), ("slight bend in, 182-195", 182f, 195f), ("wide in, 195-255", 195f, 255f),
                ("square in, 255-285", 255f, 285f), ("sharp in, 285-315", 285f, 315f), ("deep notch, over 315", 315f, 360.01f),
            };
            sb.Append($"corner angles (inside; {angles.Count} corners):\n");
            foreach (var (name, lo, hi) in bins)
            {
                int c = angles.Count(a => a >= lo && a < hi);
                sb.Append($"  {name,-26} {c,5}  {new string('#', (int)math.round(60f * c / math.max(1, angles.Count)))}\n");
            }
            sb.Append($"edges under {o.ShortEdge:0.#} m: {shared.Count(e => e.Length < o.ShortEdge)}; under 4 m (one fence piece): {shared.Count(e => e.Length < 4f)}\n");
            sb.Append($"turning past a plain shape: {gardens.Count(g => g.Turning > 361f)} gardens bend out and back (mean extra {gardens.Average(g => math.max(0f, g.Turning - 360f)):F0} deg)\n");
            var places = gardens.Where(g => g.Place != null).GroupBy(g => g.Place).OrderBy(gr => gr.Key).ToList();
            if (places.Count > 0)
            {
                sb.Append("by place:  place: gardens, edges mean / most, flagged, spikes, notches, slight bends, short edges\n");
                foreach (var gr in places)
                    sb.Append($"  {gr.Key,-12} {gr.Count(),4}, {gr.Average(g => g.Edges.Count),4:F1} / {gr.Max(g => g.Edges.Count),2}, {gr.Count(g => g.Flags.Count > 0),3}, {gr.Sum(g => g.Corners.Count(c => c.Angle < o.TightAngle)),3}, {gr.Sum(g => g.Corners.Count(c => c.Angle > 360f - o.TightAngle)),3}, {gr.Sum(g => g.Corners.Count(c => math.abs(180f - c.Angle) < o.SlightBend)),3}, {gr.Sum(g => g.Edges.Count(e => e.Length < o.ShortEdge)),3}\n");
            }
            sb.Append($"flagged: {gardens.Count(g => g.Flags.Count > 0)} of {gardens.Count}\n");
            sb.Append($"the {worst} worst (score: extra edges, 4 per spike or notch, 2 per slight bend or short edge, extra turning / 45):\n");
            foreach (var g in gardens.OrderByDescending(g => g.Score).Take(worst))
                sb.Append($"  {g.Id % 100000,6} {(g.Place ?? ""),-10} at {g.Position.x.ToString("F0", inv)},{g.Position.y.ToString("F0", inv)}: score {g.Score:F1}, {g.Edges.Count} edges ({g.Edges.Count(e => e.SharedWith.Count > 0)} shared), angles {string.Join(" ", g.Corners.Select(c => c.Angle.ToString("F0", inv)))}; {string.Join("; ", g.Flags)}\n");
            return sb.ToString();
        }

        // ── how much of the land Outgrow should cover it does cover ──

        public sealed class Place
        {
            public string Name;
            public float2 Lo, Hi;
        }

        public sealed class Cover
        {
            public string Place;
            public float2 Origin;
            public float Cell;
            public int W, H;
            public int Expected, Covered;
            public float Percent => Expected > 0 ? 100f * Covered / Expected : 0f;
            /// <summary>Row by row from Origin (x, then z up), one character a cell: '.' not expected, 'r' road, 'c' covered,
            /// 'U' expected and not covered.</summary>
            public string Mask;
        }

        /// <summary>
        /// The land Outgrow is expected to cover in each place: the lots, and the land between lots (two lots within
        /// <paramref name="nearLot"/> m of it, on roughly opposite sides of it, with no road between them), within
        /// <paramref name="band"/> m of a road, not the roads themselves; and how much of it lies in a garden. The user, 24 Oct: "I
        /// really just care about the in-between of lots, not like their outgrowing expansion": land past a row's end, round a
        /// lone house, in a block with no house, is nobody's to cover (covering it scores nothing). A trend, not a target.
        /// </summary>
        public static List<Cover> Coverage(IReadOnlyList<Place> places, IReadOnlyList<float2[]> roads, IReadOnlyList<float2[]> gardens, IReadOnlyList<float2[]> lots = null, float band = 52f, float nearLot = 26f, float cell = 1f)
        {
            var result = new List<Cover>();
            foreach (var place in places)
            {
                int w = (int)math.ceil((place.Hi.x - place.Lo.x) / cell), h = (int)math.ceil((place.Hi.y - place.Lo.y) / cell);
                if (w <= 0 || h <= 0 || (long)w * h > 9_000_000) continue;
                var road = new bool[w * h];
                foreach (var r in roads) Fill(road, w, h, place.Lo, cell, r);
                // distance (cells, 8 ways, a diagonal 1.4) to the nearest road cell
                var d = new float[w * h];
                for (int i = 0; i < d.Length; i++) d[i] = road[i] ? 0f : float.MaxValue;
                const float Diag = 1.41421f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (x > 0) d[i] = math.min(d[i], d[i - 1] + 1f);
                        if (y > 0)
                        {
                            d[i] = math.min(d[i], d[i - w] + 1f);
                            if (x > 0) d[i] = math.min(d[i], d[i - w - 1] + Diag);
                            if (x < w - 1) d[i] = math.min(d[i], d[i - w + 1] + Diag);
                        }
                    }
                for (int y = h - 1; y >= 0; y--)
                    for (int x = w - 1; x >= 0; x--)
                    {
                        int i = y * w + x;
                        if (x < w - 1) d[i] = math.min(d[i], d[i + 1] + 1f);
                        if (y < h - 1)
                        {
                            d[i] = math.min(d[i], d[i + w] + 1f);
                            if (x < w - 1) d[i] = math.min(d[i], d[i + w + 1] + Diag);
                            if (x > 0) d[i] = math.min(d[i], d[i + w - 1] + Diag);
                        }
                    }
                float bandCells = band / cell;
                var expect = new bool[w * h];
                if (lots == null)
                    for (int i = 0; i < d.Length; i++) expect[i] = !road[i] && d[i] <= bandCells;
                else
                {
                    var inLot = new bool[w * h];
                    foreach (var r in lots) Fill(inLot, w, h, place.Lo, cell, r);
                    var boxes = lots.Select(r => (Lo: r.Aggregate(new float2(float.MaxValue), math.min) - nearLot, Hi: r.Aggregate(new float2(float.MinValue), math.max) + nearLot)).ToList();
                    // the lots that may be near each 16-cell tile
                    const int Tile = 16;
                    int tw = (w + Tile - 1) / Tile, th = (h + Tile - 1) / Tile;
                    var byTile = new List<int>[tw * th];
                    for (int k = 0; k < lots.Count; k++)
                    {
                        int x0 = math.max(0, (int)math.floor((boxes[k].Lo.x - place.Lo.x) / cell / Tile)), x1 = math.min(tw - 1, (int)math.floor((boxes[k].Hi.x - place.Lo.x) / cell / Tile));
                        int y0 = math.max(0, (int)math.floor((boxes[k].Lo.y - place.Lo.y) / cell / Tile)), y1 = math.min(th - 1, (int)math.floor((boxes[k].Hi.y - place.Lo.y) / cell / Tile));
                        for (int ty = y0; ty <= y1; ty++) for (int tx = x0; tx <= x1; tx++) (byTile[ty * tw + tx] ??= new List<int>()).Add(k);
                    }
                    bool RoadOn(float2 a, float2 b)
                    {
                        int steps = (int)math.ceil(math.distance(a, b) / (cell * 0.5f));
                        for (int s = 1; s < steps; s++)
                        {
                            var p = math.lerp(a, b, (float)s / steps);
                            int x = (int)math.floor((p.x - place.Lo.x) / cell), y = (int)math.floor((p.y - place.Lo.y) / cell);
                            if (x >= 0 && y >= 0 && x < w && y < h && road[y * w + x]) return true;
                        }
                        return false;
                    }
                    var near = new List<float2>();
                    float reach2 = nearLot * nearLot;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * w + x;
                            if (road[i] || d[i] > bandCells) continue;
                            if (inLot[i]) { expect[i] = true; continue; }
                            var list = byTile[(y / Tile) * tw + x / Tile];
                            if (list == null || list.Count < 2) continue;
                            var c = place.Lo + new float2(x + 0.5f, y + 0.5f) * cell;
                            near.Clear();
                            foreach (var k in list)
                            {
                                var r = lots[k];
                                float best = float.MaxValue; var bp = c;
                                for (int j = 0; j < r.Length; j++)
                                {
                                    var a = r[j];
                                    var ab = r[(j + 1) % r.Length] - a;
                                    var q = a + ab * math.saturate(math.dot(c - a, ab) / math.max(1e-6f, math.lengthsq(ab)));
                                    float dd = math.distancesq(c, q);
                                    if (dd < best) { best = dd; bp = q; }
                                }
                                if (best <= reach2) near.Add(bp);
                            }
                            // between two of them: on roughly opposite sides (over 90 degrees apart), no road across
                            for (int a = 0; a < near.Count && !expect[i]; a++)
                                for (int b = a + 1; b < near.Count; b++)
                                    if (math.dot(near[a] - c, near[b] - c) < 0f && !RoadOn(near[a], near[b])) { expect[i] = true; break; }
                        }
                }
                var covered = new bool[w * h];
                foreach (var g in gardens) Fill(covered, w, h, place.Lo, cell, g);
                var mask = new char[w * h];
                int expected = 0, cov = 0;
                for (int i = 0; i < mask.Length; i++)
                {
                    bool exp = expect[i];
                    if (road[i]) mask[i] = 'r';
                    else if (!exp) mask[i] = '.';
                    else { expected++; if (covered[i]) { cov++; mask[i] = 'c'; } else mask[i] = 'U'; }
                }
                result.Add(new Cover { Place = place.Name, Origin = place.Lo, Cell = cell, W = w, H = h, Expected = expected, Covered = cov, Mask = new string(mask) });
            }
            return result;
        }

        /// <summary>Marks the cells whose middle lies inside a ring (even-odd, by rows).</summary>
        private static void Fill(bool[] mask, int w, int h, float2 lo, float cell, IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count < 3) return;
            float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (var p in ring) { minY = math.min(minY, p.y); maxY = math.max(maxY, p.y); minX = math.min(minX, p.x); maxX = math.max(maxX, p.x); }
            if (maxX < lo.x || minX > lo.x + w * cell || maxY < lo.y || minY > lo.y + h * cell) return;
            int y0 = math.max(0, (int)math.floor((minY - lo.y) / cell - 0.5f)), y1 = math.min(h - 1, (int)math.ceil((maxY - lo.y) / cell - 0.5f));
            var xs = new List<float>();
            for (int y = y0; y <= y1; y++)
            {
                float py = lo.y + (y + 0.5f) * cell;
                xs.Clear();
                for (int k = 0; k < ring.Count; k++)
                {
                    var a = ring[k];
                    var b = ring[(k + 1) % ring.Count];
                    if ((a.y > py) == (b.y > py)) continue;
                    xs.Add(a.x + (py - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = math.max(0, (int)math.ceil((xs[k] - lo.x) / cell - 0.5f)), x1 = math.min(w - 1, (int)math.floor((xs[k + 1] - lo.x) / cell - 0.5f));
                    for (int x = x0; x <= x1; x++) mask[y * w + x] = true;
                }
            }
        }

        public static string CoverText(List<Cover> covers)
        {
            if (covers.Count == 0) return "";
            var sb = new StringBuilder("coverage (expected: the lots and the land between them, within 52 m of a road; land past a row's end or round a lone house not counted):\n");
            foreach (var c in covers) sb.Append($"  {c.Place,-12} {c.Percent,5:F1}%  ({c.Covered * c.Cell * c.Cell:F0} of {c.Expected * c.Cell * c.Cell:F0} m², {(c.Expected - c.Covered) * c.Cell * c.Cell:F0} m² uncovered)\n");
            long e = covers.Sum(c => (long)c.Expected), v = covers.Sum(c => (long)c.Covered);
            sb.Append($"  {"all",-12} {(e > 0 ? 100f * v / e : 0f),5:F1}%\n");
            return sb.ToString();
        }

        public static string CoverJson(List<Cover> covers)
        {
            var inv = CultureInfo.InvariantCulture;
            return "[" + string.Join(",", covers.Select(c => $"\n{{\"place\":\"{c.Place}\",\"origin\":[{c.Origin.x.ToString(inv)},{c.Origin.y.ToString(inv)}],\"cell\":{c.Cell.ToString(inv)},\"w\":{c.W},\"h\":{c.H},\"expected\":{c.Expected},\"covered\":{c.Covered},\"percent\":{c.Percent.ToString("F2", inv)},\"mask\":\"{c.Mask}\"}}")) + "]";
        }

        /// <summary>Every garden, written out (for drawing and for comparing two runs).</summary>
        public static string Json(List<Garden> gardens)
        {
            var inv = CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("0.##", inv);
            string P(float2 p) => $"[{F(p.x)},{F(p.y)}]";
            var sb = new StringBuilder("{\"gardens\":[");
            for (int k = 0; k < gardens.Count; k++)
            {
                var g = gardens[k];
                if (k > 0) sb.Append(',');
                sb.Append($"\n{{\"id\":{g.Id.ToString(inv)},\"place\":{(g.Place == null ? "null" : "\"" + g.Place + "\"")},\"at\":{P(g.Position)},\"edges\":{g.Edges.Count},\"rawPoints\":{g.RawPoints},\"area\":{F(g.Area)},\"perimeter\":{F(g.Perimeter)},\"turning\":{F(g.Turning)},\"score\":{F(g.Score)}");
                sb.Append(",\"corners\":[" + string.Join(",", g.Corners.Select(c => $"{{\"at\":{P(c.At)},\"angle\":{F(c.Angle)}}}")) + "]");
                sb.Append(",\"edgeList\":[" + string.Join(",", g.Edges.Select(e => $"{{\"a\":{P(e.A)},\"b\":{P(e.B)},\"length\":{F(e.Length)},\"sharedWith\":[{string.Join(",", e.SharedWith.Select(x => x.ToString(inv)))}]}}")) + "]");
                sb.Append(",\"flags\":[" + string.Join(",", g.Flags.Select(f => "\"" + f + "\"")) + "]}");
            }
            sb.Append("\n]}");
            return sb.ToString();
        }
    }
}
