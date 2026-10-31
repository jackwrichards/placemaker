// Test kit for the parking layer: shape builders, fixture loading (real user polygons from
// ParkingLotTool's Tests/GeometryParity/Berichtsfaelle, KruemelGames, GPL-3.0), invariant checks and an
// SVG dump for eyeballing layouts (set PM_PARKING_SVG=<dir>).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    internal sealed class ParkingRun
    {
        public AreaPlan Plan;
        public Layout Layout;
        public double Millis;
        public AreaInput Input;
        public int Stalls => (int)Plan.Facts.GetValueOrDefault("capacity", 0);
    }

    internal sealed class Fixture
    {
        public string Name;
        public float3[] Ring;
        public SettingsBag Settings;
        public List<EdgeMarker> Markers;
        public int PltStalls;
        public double PltMillis;
    }

    internal static class ParkingKit
    {
        public static float3[] Ring(params double[] xz)
        {
            var r = new float3[xz.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new float3((float)xz[2 * i], 0, (float)xz[2 * i + 1]);
            return r;
        }

        public static float3[] Rect(double w, double h, double ox = 0, double oz = 0) =>
            Ring(ox, oz, ox + w, oz, ox + w, oz + h, ox, oz + h);

        /// <summary>Marker on edge i of the ring, at distance along from its start.</summary>
        public static EdgeMarker Marker(float3[] ring, int edge, double along, string kind = "entrance")
        {
            var a = ring[edge]; var b = ring[(edge + 1) % ring.Length];
            float len = math.distance(a.xz, b.xz);
            float t = (float)Math.Max(0, Math.Min(1, along / len));
            return new EdgeMarker { Kind = kind, RingSegment = edge, RingT = t, Position = math.lerp(a, b, t) };
        }

        /// <summary>PLT LayoutSettings.Cs2 expressed as Placemaker settings.</summary>
        public static SettingsBag Cs2()
        {
            var b = new ParkingLayer().Defaults();
            return b;
        }

        public static ParkingRun Run(float3[] ring, SettingsBag settings = null, List<EdgeMarker> markers = null,
            SiteContext site = null, CancellationToken cancel = default, uint seed = 7, ReservedSpace reserved = null, List<HandTouch> touches = null)
        {
            var gen = new ParkingLayer();
            var input = new AreaInput
            {
                Shape = new AreaShape { Ring = ring },
                Site = site ?? new SiteContext(),
                Settings = settings ?? gen.Defaults(),
                Markers = markers ?? new List<EdgeMarker>(),
                Seed = seed,
                Reserved = reserved ?? new ReservedSpace(),
                Touches = touches ?? new List<HandTouch>(),
            };
            var r = new ParkingRun { Input = input };
            var sw = Stopwatch.StartNew();
            r.Plan = PlanBuilder.Build(input, new ParkingSettings(input.Settings), gen, cancel, l => r.Layout = l);
            r.Millis = sw.Elapsed.TotalMilliseconds;
            return r;
        }

        // ───────────── fixtures ─────────────

        public static string FixtureDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                var p = Path.Combine(d.FullName, "Tests", "Placemaker.Tests", "Fixtures", "parking");
                if (Directory.Exists(p)) return p;
                p = Path.Combine(d.FullName, "Fixtures", "parking");
                if (Directory.Exists(p)) return p;
                d = d.Parent;
            }
            throw new DirectoryNotFoundException("Fixtures/parking not found above " + AppContext.BaseDirectory);
        }

        public static List<Fixture> Fixtures()
        {
            var list = new List<Fixture>();
            foreach (var file in Directory.GetFiles(FixtureDir(), "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var pts = root.GetProperty("polygonXZ").EnumerateArray().Select(p => new float3(p[0].GetSingle(), 0, p[1].GetSingle())).ToArray();
                var st = root.GetProperty("settings");
                var bag = new ParkingLayer().Defaults();
                bag.Values["setback"] = st.GetProperty("setback").GetDouble();
                bag.Values["aisleWidth"] = st.GetProperty("aisleWidth").GetDouble();
                bag.Values["stallLength"] = st.GetProperty("stallLength").GetDouble();
                bag.Values["stallWidth"] = st.GetProperty("stallWidth").GetDouble();
                double md = st.GetProperty("medianDepth").GetDouble();
                bag.Values["greenery"] = md > 0 ? "medians" : "edges";
                if (md > 0) bag.Values["medianDepth"] = md;
                bool caps = st.GetProperty("rowEndPlanting").GetBoolean();
                bag.Values["rowEndPlanting"] = caps;
                double cr = st.GetProperty("crossEveryMeters").GetDouble();
                double sw = st.GetProperty("stallWidth").GetDouble();
                bag.Values["crossEvery"] = cr > 0 ? Math.Max(1, Math.Round((cr - (caps ? 2 * sw : 0)) / sw)) : 0.0;
                bag.Values["perimeterRoad"] = st.GetProperty("perimeterRoad").GetBoolean();
                var markers = new List<EdgeMarker>();
                foreach (var e in root.GetProperty("entrances").EnumerateArray())
                    markers.Add(Marker(pts, e.GetProperty("edge").GetInt32(), e.GetProperty("along").GetDouble()));
                list.Add(new Fixture
                {
                    Name = Path.GetFileNameWithoutExtension(file), Ring = pts, Settings = bag, Markers = markers,
                    PltStalls = root.GetProperty("pltStalls").GetInt32(), PltMillis = root.GetProperty("pltMilliseconds").GetDouble(),
                });
            }
            return list;
        }

        // ───────────── PLT L-shape matrix (Program.LFormen.cs) ─────────────

        public sealed class LShape { public string Name; public float3[] Ring; }

        /// <summary>
        /// 3 base shapes (L, T, U) × 4 sizes × 4 mirrored corners × 6 rotations × 2 locations × 2 winding
        /// = 1152 shapes with one or two reflex corners — PLT's systematic reflex-corner sweep.
        /// </summary>
        public static List<LShape> LShapes()
        {
            double Arm(double b) => Math.Max(45, b * 0.28);
            var bases = new (string Name, Func<double, double, double, double, List<double[]>> Make)[]
            {
                ("L", (b, h, ab, ah) => new List<double[]> { new[] { 0.0, 0.0 }, new[] { b, 0.0 }, new[] { b, h - ah }, new[] { b - ab, h - ah }, new[] { b - ab, h }, new[] { 0.0, h } }),
                ("T", (b, h, ab, ah) => { var w = Arm(b); return new List<double[]> { new[] { (b - w) / 2, 0.0 }, new[] { (b + w) / 2, 0.0 }, new[] { (b + w) / 2, h - ah }, new[] { b, h - ah }, new[] { b, h }, new[] { 0.0, h }, new[] { 0.0, h - ah }, new[] { (b - w) / 2, h - ah } }; }),
                ("U", (b, h, ab, ah) => { var w = Arm(b); return new List<double[]> { new[] { 0.0, 0.0 }, new[] { b, 0.0 }, new[] { b, h }, new[] { b - w, h }, new[] { b - w, ah }, new[] { w, ah }, new[] { w, h }, new[] { 0.0, h } }; }),
            };
            var sizes = new (string Name, double B, double H, double Ab, double Ah)[]
            {
                ("narrow", 140, 100, 70, 50), ("thick", 200, 140, 50, 45), ("long", 300, 110, 150, 50), ("tight", 160, 120, 55, 50),
            };
            var angles = new[] { 0, 7, 23, 45, 61, 137 };
            var places = new (string Name, double X, double Y)[] { ("origin", 0, 0), ("far", -1300, 200) };
            var shapes = new List<LShape>();
            foreach (var bs in bases)
                foreach (var sz in sizes)
                    foreach (var corner in new[] { 0, 1, 2, 3 })
                        foreach (var deg in angles)
                            foreach (var pl in places)
                                foreach (var dirn in new[] { "CCW", "CW" })
                                {
                                    var pts = bs.Make(sz.B, sz.H, sz.Ab, sz.Ah);
                                    if ((corner & 1) != 0) pts = pts.Select(p => new[] { sz.B - p[0], p[1] }).ToList();
                                    if ((corner & 2) != 0) pts = pts.Select(p => new[] { p[0], sz.H - p[1] }).ToList();
                                    if (((corner & 1) != 0) ^ ((corner & 2) != 0)) pts.Reverse();
                                    double rad = deg * Math.PI / 180, c = Math.Cos(rad), s = Math.Sin(rad);
                                    double mx = pts.Average(p => p[0]), my = pts.Average(p => p[1]);
                                    var rot = pts.Select(p =>
                                    {
                                        double x = p[0] - mx, y = p[1] - my;
                                        return new float3((float)(pl.X + x * c - y * s), 0, (float)(pl.Y + x * s + y * c));
                                    }).ToList();
                                    if (dirn == "CW") rot.Reverse();
                                    shapes.Add(new LShape { Name = $"{bs.Name} {sz.Name} corner{corner} {deg}deg {pl.Name} {dirn}", Ring = rot.ToArray() });
                                }
            return shapes;
        }

        // ───────────── invariants ─────────────

        public sealed class Quality
        {
            public int Stalls;
            public int Overlaps, Outside, OnLanes, Unreachable;
            public double UncoveredFraction, OverlapFraction;
            public double MinSurfaceEdge = double.MaxValue;
            public int RejectedSurfaces;
            public List<string> Problems = new List<string>();
            public override string ToString() =>
                $"stalls {Stalls}, overlaps {Overlaps}, outside {Outside}, onLanes {OnLanes}, unreachable {Unreachable}, " +
                $"uncovered {UncoveredFraction:P3}, overlapSamples {OverlapFraction:P3}, minEdge {MinSurfaceEdge:F3}, rejected {RejectedSurfaces}";
        }

        public static Quality Check(ParkingRun run, bool sampleCoverage = true)
        {
            var q = new Quality();
            var L = run.Layout;
            var plan = run.Plan;
            if (L == null) { q.Problems.Add("no layout"); return q; }
            var bays = L.LiveBays.ToList();
            q.Stalls = bays.Count;

            // bays: pairwise overlap (grid), inside lot
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < bays.Count; i++)
            {
                var c = bays[i].Center;
                var key = ((int)Math.Floor(c.x / 8), (int)Math.Floor(c.y / 8));
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (grid.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var near))
                            foreach (int j in near)
                                if (PGeo.ConvexOverlap(bays[i].Quad, bays[j].Quad, 1e-3)) { q.Overlaps++; q.Problems.Add($"bays overlap at {c}"); }
                if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                l.Add(i);
                if (!PGeo.ConvexInside(bays[i].Quad, L.Lot, 1e-3)) { q.Outside++; q.Problems.Add($"bay outside lot at {c}"); }
            }
            // bays vs lanes (corridors slightly narrower than the lane)
            foreach (var line in L.LiveLines)
            {
                if (line.Kind == LineKind.Footpath) continue;
                var cor = PGeo.Corridor(line.A, line.B, Math.Max(0.1, line.Width / 2 - 0.05));
                PGeo.Bounds(cor, out var mn, out var mx);
                foreach (var b in bays)
                {
                    if (b.Center.x < mn.x - 8 || b.Center.x > mx.x + 8 || b.Center.y < mn.y - 8 || b.Center.y > mx.y + 8) continue;
                    if (PGeo.ConvexOverlap(b.Quad, cor, 1e-3)) { q.OnLanes++; q.Problems.Add($"bay on {line.Kind} lane at {b.Center}"); }
                }
            }

            // reachability over the emitted nets (world space)
            q.Unreachable = UnreachableBays(run, bays);
            if (q.Unreachable > 0) q.Problems.Add($"{q.Unreachable} bays unreachable");

            // surfaces: acceptance and edges
            foreach (var s in plan.Surfaces)
            {
                var r2 = s.Ring.Select(p => p.xz).ToArray();
                if (!SurfaceAcceptance.IsAccepted(r2)) q.RejectedSurfaces++;
                for (int i = 0; i < r2.Length; i++) q.MinSurfaceEdge = Math.Min(q.MinSurfaceEdge, math.distance(r2[i], r2[(i + 1) % r2.Length]));
            }
            q.UncoveredFraction = L.UncoveredArea / Math.Max(1, L.LotArea);
            if (sampleCoverage) SampleCoverage(run, q);
            return q;
        }

        private static void SampleCoverage(ParkingRun run, Quality q)
        {
            var lot = run.Layout.Lot.Select(p => run.Layout.Frame.ToWorld(p)).ToList();
            PGeo.Bounds(lot, out var mn, out var mx);
            double area = (mx.x - mn.x) * (mx.y - mn.y);
            double step = Math.Max(0.7, Math.Sqrt(area / 6000));
            var rings = run.Plan.Surfaces.Select(s => s.Ring.Select(p => new double2(p.x, p.z)).ToList()).ToList();
            var bounds = rings.Select(r => { PGeo.Bounds(r, out var a, out var b); return (a, b); }).ToList();
            int inside = 0, over = 0;
            for (double y = mn.y + step / 2; y < mx.y; y += step)
                for (double x = mn.x + step / 2; x < mx.x; x += step)
                {
                    var p = new double2(x + 0.0123, y + 0.0177);
                    if (!PGeo.Contains(lot, p) || PGeo.DistToBoundary(lot, p) < 0.2) continue;
                    inside++;
                    int hits = 0;
                    for (int i = 0; i < rings.Count; i++)
                    {
                        var b = bounds[i];
                        if (p.x < b.a.x || p.x > b.b.x || p.y < b.a.y || p.y > b.b.y) continue;
                        // float rings at world coordinates: ignore millimetre contact along shared edges
                        if (PGeo.Contains(rings[i], p) && PGeo.DistToBoundary(rings[i], p) > 0.01) hits++;
                    }
                    if (hits > 1) over++;
                }
            q.OverlapFraction = inside > 0 ? (double)over / inside : 0;
        }

        private static int UnreachableBays(ParkingRun run, List<Bay> bays)
        {
            var nets = run.Plan.Nets.Where(n => n.Role == NetRole.DriveAisle || n.Role == NetRole.DriveConnector).ToList();
            if (!nets.Any(n => n.Role == NetRole.DriveConnector)) return 0; // no entrance: nothing to reach from
            var nodes = new List<float2>();
            int Node(float2 p)
            {
                for (int i = 0; i < nodes.Count; i++) if (math.distance(nodes[i], p) < 1e-2f) return i;
                nodes.Add(p); return nodes.Count - 1;
            }
            var ends = nets.Select(n => (Node(n.A.xz), Node(n.D.xz))).ToList();
            var dsu = new LayoutEngine.Dsu(nodes.Count);
            foreach (var e in ends) dsu.Union(e.Item1, e.Item2);
            var good = new HashSet<int>();
            for (int i = 0; i < nets.Count; i++) if (nets[i].Role == NetRole.DriveConnector) good.Add(dsu.Find(ends[i].Item1));
            int bad = 0;
            var s = run.Layout;
            double tol = new ParkingSettings(run.Input.Settings).Aisle / 2 + new ParkingSettings(run.Input.Settings).StallWidth + 0.2;
            foreach (var b in bays)
            {
                var front = s.Frame.ToWorld((b.Quad[0] + b.Quad[1]) * 0.5);
                var fp = new float2((float)front.x, (float)front.y);
                bool ok = false;
                for (int i = 0; i < nets.Count && !ok; i++)
                {
                    var n = nets[i];
                    var d = PGeo.DistPointSeg(new double2(fp.x, fp.y), new double2(n.A.x, n.A.z), new double2(n.D.x, n.D.z));
                    if (d <= tol && good.Contains(dsu.Find(ends[i].Item1))) ok = true;
                }
                if (!ok) bad++;
            }
            return bad;
        }

        // ───────────── SVG ─────────────

        public static void Svg(ParkingRun run, string name)
        {
            var dir = Environment.GetEnvironmentVariable("PM_PARKING_SVG");
            if (string.IsNullOrEmpty(dir) || run.Layout == null) return;
            Directory.CreateDirectory(dir);
            var L = run.Layout;
            var lot = L.Lot;
            PGeo.Bounds(lot, out var mn, out var mx);
            double pad = 5, sc = 4;
            string P(double2 p) => ((p.x - mn.x + pad) * sc).ToString("F1", CultureInfo.InvariantCulture) + "," + ((mx.y - p.y + pad) * sc).ToString("F1", CultureInfo.InvariantCulture);
            string Poly(IEnumerable<double2> pts, string style) => $"<polygon points=\"{string.Join(" ", pts.Select(P))}\" style=\"{style}\"/>";
            var sb = new StringBuilder();
            double w = (mx.x - mn.x + 2 * pad) * sc, h = (mx.y - mn.y + 2 * pad) * sc;
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w:F0}\" height=\"{h:F0}\" style=\"background:#fff\">");
            foreach (var s in L.Surfaces.Where(x => !x.Asphalt)) sb.Append(Poly(s.Ring, "fill:#9fd39a;stroke:#2d6b2a;stroke-width:0.6"));
            foreach (var s in L.Surfaces.Where(x => x.Asphalt)) sb.Append(Poly(s.Ring, "fill:#b9bcc2;fill-opacity:0.85;stroke:#555;stroke-width:0.6"));
            sb.Append(Poly(lot, "fill:none;stroke:#000;stroke-width:1.5"));
            foreach (var b in L.LiveBays)
                sb.Append(Poly(b.Quad, b.Role == BayRole.Disabled ? "fill:#5b8def;stroke:#fff;stroke-width:0.8" : b.Role == BayRole.Electric ? "fill:#f2c94c;stroke:#fff;stroke-width:0.8" : "fill:#7d8188;stroke:#fff;stroke-width:0.8"));
            foreach (var l in L.LiveLines)
            {
                string col = l.Kind == LineKind.Connector ? "#d00" : l.Kind == LineKind.Cross ? "#e67e22" : l.Kind == LineKind.Footpath ? "#8e44ad" : "#1f4fbf";
                sb.Append($"<line x1=\"{P(l.A).Split(',')[0]}\" y1=\"{P(l.A).Split(',')[1]}\" x2=\"{P(l.B).Split(',')[0]}\" y2=\"{P(l.B).Split(',')[1]}\" style=\"stroke:{col};stroke-width:2\"/>");
            }
            foreach (var c in L.Chargers) sb.Append($"<circle cx=\"{P(c.Position).Split(',')[0]}\" cy=\"{P(c.Position).Split(',')[1]}\" r=\"4\" style=\"fill:#f39c12;stroke:#000\"/>");
            foreach (var t in L.Lanterns) sb.Append($"<circle cx=\"{P(t.Position).Split(',')[0]}\" cy=\"{P(t.Position).Split(',')[1]}\" r=\"3\" style=\"fill:#ff0;stroke:#000\"/>");
            foreach (var t in L.Plants) sb.Append($"<circle cx=\"{P(t.Position).Split(',')[0]}\" cy=\"{P(t.Position).Split(',')[1]}\" r=\"{(t.Tree ? 6 : 2.5)}\" style=\"fill:{(t.Tree ? "#1e7b34" : "#4caf50")};opacity:0.8\"/>");
            sb.Append("</svg>");
            File.WriteAllText(Path.Combine(dir, name + ".svg"), sb.ToString());
        }
    }

    public class ParkingKitTests
    {
        private readonly ITestOutputHelper _out;
        public ParkingKitTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void FixturesLoad()
        {
            var fx = ParkingKit.Fixtures();
            Assert.Equal(4, fx.Count);
            Assert.All(fx, f => Assert.True(f.Ring.Length >= 6));
        }

        [Fact]
        public void LShapeMatrixHas1152Shapes()
        {
            Assert.Equal(1152, ParkingKit.LShapes().Count);
        }

        [Fact]
        public void DumpSvgWhenRequested()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PM_PARKING_SVG"))) return;
            var rect = ParkingKit.Rect(160, 110);
            var r = ParkingKit.Run(rect, null, new List<EdgeMarker> { ParkingKit.Marker(rect, 0, 60) });
            ParkingKit.Svg(r, "rect");
            _out.WriteLine("rect: " + ParkingKit.Check(r) + $" {r.Millis:F0} ms");
            foreach (var f in ParkingKit.Fixtures())
            {
                var run = ParkingKit.Run(f.Ring, f.Settings, f.Markers);
                ParkingKit.Svg(run, f.Name);
                _out.WriteLine($"{f.Name}: PLT {f.PltStalls} | " + ParkingKit.Check(run) + $" {run.Millis:F0} ms");
                foreach (var i in run.Plan.Issues) _out.WriteLine("   " + i.Severity + " " + i.Key + " " + string.Join(",", i.Args));
            }
            var shapes = ParkingKit.LShapes();
            foreach (int k in new[] { 0, 100, 300, 500, 777, 1000 })
            {
                var s = shapes[k];
                var run = ParkingKit.Run(s.Ring, null, new List<EdgeMarker> { ParkingKit.Marker(s.Ring, 0, 20) });
                ParkingKit.Svg(run, "lshape" + k);
                _out.WriteLine($"{s.Name}: " + ParkingKit.Check(run) + $" {run.Millis:F0} ms");
            }
            foreach (var angle in new[] { "60", "45" })
            {
                var b = new ParkingLayer().Defaults(); b.Values["stallAngle"] = angle;
                var run = ParkingKit.Run(rect, b, new List<EdgeMarker> { ParkingKit.Marker(rect, 0, 60) });
                ParkingKit.Svg(run, "rect" + angle);
                var qq = ParkingKit.Check(run); _out.WriteLine($"rect {angle}: " + qq + $" {run.Millis:F0} ms"); foreach (var pr in qq.Problems.Take(5)) _out.WriteLine("   " + pr);
            }
            {
                var b = new ParkingLayer().Defaults(); b.Values["perimeterRoad"] = false;
                var run = ParkingKit.Run(rect, b, new List<EdgeMarker> { ParkingKit.Marker(rect, 0, 60) });
                ParkingKit.Svg(run, "rect-noring");
                _out.WriteLine($"rect no ring: " + ParkingKit.Check(run) + $" {run.Millis:F0} ms");
            }
        }
    }
}
