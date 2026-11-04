// Placemaker road lab — one SVG picture per case: the land (height tint + contours), the selection, the existing roads
// by class (highways red), the new roads by role at their real width, the problems the rating found, the scores.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Placemaker.Core;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;

namespace Placemaker.RoadLab
{
    public static class Svg
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        /// <summary>--zoom x0,z0,x1,z1: draw only this part of the world (metres).</summary>
        public static float[] Zoom;
        private static string F(float v) => v.ToString("0.#", C);

        public static string Draw(LabCase lc, string variant, AreaPlan plan, RoadsRating rating, RoadsRating before, double ms)
        {
            var ring = lc.Ring.Select(p => p.xz).ToArray();
            float2 mn = ring.Aggregate(new float2(float.MaxValue), math.min) - 110f;
            float2 mx = ring.Aggregate(new float2(float.MinValue), math.max) + 110f;
            if (Zoom != null) { mn = new float2(Zoom[0], Zoom[1]); mx = new float2(Zoom[2], Zoom[3]); }
            float w = mx.x - mn.x, h = mx.y - mn.y;
            const float panel = 300f;
            float2 P(float2 p) => new float2(p.x - mn.x, mx.y - p.y);
            var sb = new StringBuilder();
            float px = 1000f / w;   // display pixels per metre
            sb.AppendFormat(C, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {1}\" width=\"{2}\" height=\"{3}\" font-family=\"Segoe UI, Arial, sans-serif\">\n",
                F(w + panel / px), F(h), F(1000 + panel), F(h * px));
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#1e2a22\"/>\n");

            // ── land: height tint and contours ──
            var heights = new List<float>();
            int nx = 64, nz = (int)math.ceil(64 * h / w);
            float cw = w / nx, ch = h / nz;
            var grid = new float[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    var p = new float2(mn.x + i * cw, mx.y - j * ch);
                    grid[j * (nx + 1) + i] = lc.Height(p);
                }
            float hmin = grid.Min(), hmax = grid.Max();
            float span = math.max(1f, hmax - hmin);
            sb.Append("<g shape-rendering=\"crispEdges\">");
            for (int j = 0; j < nz; j++)
            {
                int i0 = 0;
                string Col(int i) => Tint((grid[j * (nx + 1) + i] + grid[(j + 1) * (nx + 1) + i + 1]) * 0.5f, hmin, span);
                while (i0 < nx)
                {
                    string col = Col(i0);
                    int i1 = i0 + 1;
                    while (i1 < nx && Col(i1) == col) i1++;
                    sb.AppendFormat(C, "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" fill=\"{4}\"/>", F(i0 * cw), F(j * ch), F((i1 - i0) * cw + 0.5f), F(ch + 0.5f), col);
                    i0 = i1;
                }
            }
            sb.Append("</g>\n");
            if (span > 1.5f)
            {
                float step = span > 30f ? 2f : 1f;
                sb.Append("<g fill=\"none\" stroke=\"#ffffff\" stroke-opacity=\"0.13\" stroke-width=\"0.8\">");
                for (float lev = math.ceil(hmin / step) * step; lev < hmax; lev += step)
                {
                    bool major = math.abs(lev / (step * 5f) - math.round(lev / (step * 5f))) < 1e-3f;
                    foreach (var seg in Contour(grid, nx, nz, cw, ch, lev))
                        sb.AppendFormat(C, "<line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{3}\"{4}/>", F(seg.a.x), F(seg.a.y), F(seg.b.x), F(seg.b.y), major ? " stroke-opacity=\"0.3\" stroke-width=\"1.3\"" : "");
                }
                sb.Append("</g>\n");
            }

            // ── ground paint: each field sampled on a 2 m grid (what the game rasterises), in its material's colour ──
            if (plan.Paint.Count > 0)
            {
                const float cell = 2f;
                int gx = (int)math.ceil(w / cell), gz = (int)math.ceil(h / cell);
                var acc = new float[gx * gz * 4];
                foreach (var pp in plan.Paint)
                {
                    if (pp?.Shape == null || pp.Erase) continue;
                    int layer = math.clamp(pp.Layer, 1, 4) - 1;
                    float2 lo = math.max(pp.Shape.Min, mn), hi = math.min(pp.Shape.Max, mx);
                    if (!math.all(math.isfinite(lo)) || !math.all(math.isfinite(hi)) || hi.x <= lo.x || hi.y <= lo.y) continue;
                    int i0 = (int)((lo.x - mn.x) / cell), i1 = (int)((hi.x - mn.x) / cell), j0 = (int)((mx.y - hi.y) / cell), j1 = (int)((mx.y - lo.y) / cell);
                    for (int j = math.max(0, j0); j <= math.min(gz - 1, j1); j++)
                        for (int i = math.max(0, i0); i <= math.min(gx - 1, i1); i++)
                        {
                            float2 q = new float2(mn.x + (i + 0.5f) * cell, mx.y - (j + 0.5f) * cell);
                            float v = pp.Shape.Sample(q) * pp.Strength;
                            int k = (j * gx + i) * 4 + layer;
                            if (v > acc[k]) acc[k] = v;
                        }
                }
                string[] cols = { "#c9c48a", "#2c3a1c", "#b08a5a", "#d8ccb0" };   // dry grass, forest floor, dirt, sand (exaggerated to read)
                sb.Append("<g shape-rendering=\"crispEdges\">");
                for (int layer = 0; layer < 4; layer++)
                    for (int j = 0; j < gz; j++)
                    {
                        int i = 0;
                        while (i < gx)
                        {
                            float v = acc[(j * gx + i) * 4 + layer];
                            int q = (int)math.floor(math.min(1f, v * 1.4f) * 8f);
                            if (q <= 0) { i++; continue; }
                            int i1 = i + 1;
                            while (i1 < gx && (int)math.floor(math.min(1f, acc[(j * gx + i1) * 4 + layer] * 1.4f) * 8f) == q) i1++;
                            sb.AppendFormat(C, "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" fill=\"{4}\" fill-opacity=\"{5}\"/>",
                                F(i * cell), F(j * cell), F((i1 - i) * cell), F(cell), cols[layer], (0.2f + 0.8f * q / 8f).ToString("0.##", C));
                            i = i1;
                        }
                    }
                sb.Append("</g>\n");
            }

            // ── existing roads ──
            sb.Append("<g fill=\"none\" stroke-linecap=\"round\">");
            foreach (var n in lc.Nets.Where(n => n.Category == NetCategory.Road))
            {
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                bool hw = RoadClasses.HighwayLike(n);
                string d = Path(c, P);
                sb.AppendFormat(C, "<path d=\"{0}\" stroke=\"{1}\" stroke-width=\"{2}\"/>", d, hw ? "#7a2c2c" : "#3b3b3b", F(n.Width + 2f));
                sb.AppendFormat(C, "<path d=\"{0}\" stroke=\"{1}\" stroke-width=\"{2}\"/>", d, hw ? "#c0504d" : "#8c8c8c", F(n.Width));
            }
            sb.Append("</g>\n");

            // ── selection ──
            sb.AppendFormat(C, "<polygon points=\"{0}\" fill=\"#4da3ff\" fill-opacity=\"0.06\" stroke=\"#4da3ff\" stroke-width=\"2\" stroke-dasharray=\"8 6\"/>\n",
                string.Join(" ", ring.Select(p => { var q = P(p); return F(q.x) + "," + F(q.y); })));

            // ── blocks (light outline); green blocks (hillside parks, pocket greens, the square) tinted ──
            foreach (var r in plan.Regions.Where(r => r.Tags.Contains(RegionTags.Block) && r.Tags.Contains(RegionTags.Green)))
                sb.AppendFormat(C, "<polygon points=\"{0}\" fill=\"{1}\" fill-opacity=\"0.35\"/>", string.Join(" ", r.Ring.Select(p => { var q = P(p.xz); return F(q.x) + "," + F(q.y); })),
                    r.Tags.Contains(RegionTags.Core) ? "#7fbf6a" : "#4f9a45");
            sb.Append("<g fill=\"none\" stroke=\"#ffffff\" stroke-opacity=\"0.12\" stroke-width=\"1\">");
            foreach (var r in plan.Regions.Where(r => r.Tags.Contains(RegionTags.Block)))
                sb.AppendFormat(C, "<polygon points=\"{0}\"/>", string.Join(" ", r.Ring.Select(p => { var q = P(p.xz); return F(q.x) + "," + F(q.y); })));
            sb.Append("</g>\n");

            // ── new roads ──
            sb.Append("<g fill=\"none\" stroke-linecap=\"butt\">");
            foreach (var n in plan.Nets.OrderBy(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide ? 0 : 1))
            {
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                string role = RoadRoles.Of(n.Role) ?? "street";
                bool walk = n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide;
                string d = Path(c, P);
                if (walk) { sb.AppendFormat(C, "<path d=\"{0}\" stroke=\"{1}\" stroke-width=\"2.5\" stroke-dasharray=\"5 4\"/>", d, RoadRoles.Color(role)); continue; }
                float width = 2f * RoadsRating.PlannedHalfWidth(n.Role);
                sb.AppendFormat(C, "<path d=\"{0}\" stroke=\"#101010\" stroke-opacity=\"0.6\" stroke-width=\"{1}\"/>", d, F(width + 1.5f));
                sb.AppendFormat(C, "<path d=\"{0}\" stroke=\"{1}\" stroke-opacity=\"0.85\" stroke-width=\"{2}\"/>", d, RoadRoles.Color(role), F(width));
            }
            sb.Append("</g>\n");
            // cul-de-sac caps
            foreach (var o in plan.Objects.Where(o => o.Role == ObjectRole.CulDeSacCap))
            {
                var q = P(o.Position.xz);
                sb.AppendFormat(C, "<circle cx=\"{0}\" cy=\"{1}\" r=\"10\" fill=\"#8fe07a\" fill-opacity=\"0.5\"/>", F(q.x), F(q.y));
            }
            // steep pieces outlined red
            sb.Append("<g fill=\"none\" stroke=\"#ff3b30\" stroke-width=\"3\" stroke-opacity=\"0.9\">");
            var h0 = plan.HeightAfter ?? lc.Height;
            foreach (var n in plan.Nets)
            {
                if (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide) continue;
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                if (RoadGraph.MaxGrade(c, h0, 16f, 4f) > 0.105f) sb.AppendFormat(C, "<path d=\"{0}\"/>", Path(c, P));
            }
            sb.Append("</g>\n");

            // ── marks ──
            foreach (var m in rating.Marks)
            {
                var q = P(m.At);
                var (col, r, label) = Style(m);
                if (col == null) continue;
                sb.AppendFormat(C, "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" fill=\"none\" stroke=\"{3}\" stroke-width=\"2.5\"/>", F(q.x), F(q.y), F(r), col);
                if (label.Length > 0)
                    sb.AppendFormat(C, "<text x=\"{0}\" y=\"{1}\" font-size=\"11\" fill=\"{2}\" font-weight=\"600\">{3}</text>", F(q.x + r + 1f), F(q.y - r), col, label);
            }

            // scale bar (100 m)
            sb.AppendFormat(C, "<g><rect x=\"14\" y=\"{0}\" width=\"100\" height=\"5\" fill=\"#ffffff\"/><text x=\"14\" y=\"{1}\" font-size=\"12\" fill=\"#ffffff\">100 m</text></g>\n", F(h - 18f), F(h - 24f));

            // ── side panel ──
            float s = 1f / px;   // metres per display pixel
            float x0 = w + 10f * s;
            sb.AppendFormat(C, "<rect x=\"{0}\" y=\"0\" width=\"{1}\" height=\"{2}\" fill=\"#11161a\"/>\n", F(w), F(panel * s), F(h));
            float y = 22f * s;
            void Line(string text, float size = 13f, string col = "#e8e8e8", bool bold = false)
            {
                sb.AppendFormat(C, "<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\" fill=\"{3}\"{4}>{5}</text>\n", F(x0), F(y), F(size * s), col, bold ? " font-weight=\"700\"" : "", Esc(text));
                y += size * 1.45f * s;
            }
            Line(lc.Id + " · " + variant, 16f, "#ffffff", true);
            foreach (var part in Wrap(lc.Title, 40)) Line(part, 12f, "#b8c4cc");
            y += 6f * s;
            string Delta(float now, float? was) => was.HasValue && math.abs(now - was.Value) >= 0.5f ? string.Format(C, "  ({0}{1:0})", now > was ? "+" : "", now - was.Value) : "";
            Line(string.Format(C, "Total {0:0}/100{1}", rating.Total, Delta(rating.Total, before?.Total)), 20f, ScoreCol(rating.Total), true);
            foreach (var (name, v, b) in new (string, float, float?)[]
            {
                ("Grades", rating.Grades, before?.Grades), ("Junctions", rating.Junctions, before?.Junctions), ("Segments", rating.Segments, before?.Segments),
                ("Perimeter", rating.Perimeter, before?.Perimeter), ("Links", rating.Links, before?.Links), ("Fit", rating.Fit, before?.Fit),
                ("Directness", rating.Directness, before?.Directness),
            })
                Line(string.Format(C, "{0,-10} {1:0}{2}", name, v, Delta(v, b)), 13f, ScoreCol(v));
            y += 6f * s;
            Line(string.Format(C, "Roads {0:0} m in {1} pieces", rating.RoadLength, rating.CarNets), 12f);
            Line(string.Format(C, "Steepest {0:0.0}% · steep {1} ({2:0}% of length)", rating.SteepestGrade * 100f, rating.SteepPieces, rating.SteepShare * 100f), 12f);
            Line(string.Format(C, "Junctions {0} · min angle {1:0}° · awkward {2} · acute {3}", rating.JunctionCount, rating.MinJunctionAngle, rating.AwkwardJunctions, rating.AcuteJunctions), 12f);
            Line(string.Format(C, "Crowded junctions {0} · short {1} · crumbs {2}", rating.CrowdedJunctions, rating.ShortLinks, rating.Crumbs), 12f);
            Line(string.Format(C, "Kinks {0} · hooks {1} · bare ends {2} · slivers {3}", rating.Kinks, rating.Hooks, rating.BareDeadEnds, rating.Slivers), 12f);
            if (rating.HasPerimeter)
                Line(string.Format(C, "Outline gap mean {0:0.0} m · corners {1:0.0} m (max {2:0.0})", rating.OutlineMeanGap, rating.CornerMeanGap, rating.CornerMaxGap), 12f);
            Line(string.Format(C, "City links {0} · aligned {1} · on junction {2}", rating.CityLinks, rating.LinksAligned, rating.LinksOnJunction), 12f);
            Line(string.Format(C, "Skew {0} · crowded {1} · to highway {2}", rating.SkewLinks, rating.LinksCrowded, rating.HighwayLinks), 12f, rating.HighwayLinks > 0 ? "#ff6b6b" : "#e8e8e8");
            Line(string.Format(C, "Crossings {0} (highway {1}) · doubled {2}", rating.Crossings, rating.HighwayCrossings, rating.DoubledPieces), 12f);
            Line(string.Format(C, "Hierarchy breaks {0} · access {1} · spine crowding {2}", rating.HierarchyBreaks, rating.AccessBreaks, rating.SpineCrowding), 12f);
            double F_(string k) => plan.Facts.TryGetValue(k, out var v) ? v : -1;
            double frame = F_(RoadsPlanner.FactLayoutFrame), variantWon = F_(RoadsPlanner.FactVariant), tried = F_(RoadsPlanner.FactVariants);
            string diag = (F_(RoadsPlanner.FactBrick) > 0.5 ? " + brick pattern" : "") + (F_(RoadsPlanner.FactDiagonal) > 0.5 ? " + desire-line diagonal" : "");
            string frameName = variantWon == 2 ? "along the contours" : variantWon == 3 ? "turned against the slope" : frame == 2 ? "on the spine road" : frame == 1 ? "terrain" : frame == 0 ? "outline" : "-";
            Line(string.Format(C, "Layout: {0}{1} · streets lined up {2:0}", frameName + diag, tried > 1 ? string.Format(C, " (best of {0:0})", tried) : "",
                math.max(0, F_(RoadsPlanner.FactStubsAligned))), 12f, "#9fd3ff");
            Line(string.Format(C, "Hill: {0:0} junctions bent · {1:0} streets made steps · {2:0} hillside parks", math.max(0, F_(RoadsPlanner.FactBent)),
                math.max(0, F_(RoadsPlanner.FactStairs)), math.max(0, F_(RoadsPlanner.FactHillParks))), 12f, "#a8d8a0");
            Line(string.Format(C, "Ground: {0:0} desire paths · {1:0} worn corners · {2} paints", math.max(0, F_(RoadsPlanner.FactDesirePaths)),
                math.max(0, F_(RoadsPlanner.FactWornCorners)), plan.Paint.Count), 12f, "#d8c49a");
            Line(string.Format(C, "Planned in {0:0} ms", ms), 11f, "#8a949c");
            y += 8f * s;
            Line("Expect:", 12f, "#ffffff", true);
            foreach (var part in Wrap(lc.Expect, 44)) Line(part, 11.5f, "#b8c4cc");
            y += 8f * s;
            Line("Key", 12f, "#ffffff", true);
            foreach (var (col, text) in new[]
            {
                ("#f2f2f2", "street"), ("#59d9ff", "avenue"), ("#ffa64d", "perimeter"), ("#8fe07a", "local / cul-de-sac"), ("#c9a27a", "lane"),
                ("#8c8c8c", "existing road"), ("#c0504d", "existing highway / ramp"), ("#ff3b30", "steeper than 10 % (red edge)"),
                ("#b08a5a", "painted worn dirt (desire paths, corners)"), ("#c9c48a", "painted dry grass (verges)"),
                ("#4f9a45", "green block (hillside park, pocket green)"), ("#ffd173", "footpath / steps (dashed)"),
            })
            {
                sb.AppendFormat(C, "<rect x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" fill=\"{4}\"/>", F(x0), F(y - 9f * s), F(18f * s), F(8f * s), col);
                sb.AppendFormat(C, "<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\" fill=\"#cfd8dc\">{3}</text>\n", F(x0 + 24f * s), F(y), F(11f * s), Esc(text));
                y += 15f * s;
            }
            foreach (var (col, text) in new[] { ("#ff3b30", "acute / highway / crossing"), ("#ffb020", "awkward, crowded, gap, corner"), ("#5cd65c", "link (filled = in line with a street)") })
            {
                sb.AppendFormat(C, "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" fill=\"none\" stroke=\"{3}\" stroke-width=\"{4}\"/>", F(x0 + 8f * s), F(y - 4f * s), F(6f * s), col, F(2f * s));
                sb.AppendFormat(C, "<text x=\"{0}\" y=\"{1}\" font-size=\"{2}\" fill=\"#cfd8dc\">{3}</text>\n", F(x0 + 24f * s), F(y), F(11f * s), Esc(text));
                y += 15f * s;
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private static (string col, float r, string label) Style(RoadsRating.Mark m)
        {
            switch (m.Kind)
            {
                case RoadsRating.MarkAcute: return ("#ff3b30", 9f, string.Format(C, "{0:0}°", m.Value));
                case RoadsRating.MarkAwkward: return ("#ffb020", 8f, string.Format(C, "{0:0}°", m.Value));
                case RoadsRating.MarkCrowded: return ("#ffb020", 6f, "");
                case RoadsRating.MarkHighwayLink: return ("#ff3b30", 14f, "HWY");
                case RoadsRating.MarkHighwayCross: return ("#ff3b30", 14f, "X HWY");
                case RoadsRating.MarkCross: return ("#ff3b30", 10f, "X");
                case RoadsRating.MarkSkewLink: return ("#ffb020", 10f, "skew");
                case RoadsRating.MarkCrowdedLink: return ("#ffb020", 10f, "");
                case RoadsRating.MarkGoodLink: return ("#5cd65c", m.Value > 0.5f ? 9f : 7f, "");
                case RoadsRating.MarkCrumb: return ("#ffb020", 5f, "");
                case RoadsRating.MarkShort: return ("#ffb020", 6f, "short");
                case RoadsRating.MarkKink: return ("#ffb020", 6f, "kink");
                case RoadsRating.MarkHook: return ("#ffb020", 8f, "hook");
                case RoadsRating.MarkDeadEnd: return ("#ffb020", 6f, "end");
                case RoadsRating.MarkSliver: return ("#ffb020", 10f, "sliver");
                case RoadsRating.MarkCorner: return ("#ffb020", 6f, string.Format(C, "{0:0}m", m.Value));
                case RoadsRating.MarkGap: return (null, 0f, "");
                case RoadsRating.MarkDoubled: return ("#ff3b30", 10f, "on top");
                case RoadsRating.MarkHierarchy: return ("#ff3b30", 11f, "bigger than the road");
                case RoadsRating.MarkAccess: return ("#ffb020", 10f, "too close");
                case RoadsRating.MarkSpine: return ("#ffb020", 10f, "alongside");
                case RoadsRating.MarkSteep: return (null, 0f, "");
                default: return (null, 0f, "");
            }
        }

        private static string ScoreCol(float v) => v >= 85f ? "#7ee081" : v >= 65f ? "#ffd166" : "#ff7b6b";

        private static string Tint(float h, float hmin, float span)
        {
            float t = math.saturate((h - hmin) / span);
            int band = (int)math.floor(t * 9.999f);
            float u = band / 9f;
            float3 lo = new float3(0x24, 0x3a, 0x2b), hi = new float3(0x6b, 0x63, 0x48);
            float3 c = math.lerp(lo, hi, u);
            return string.Format(C, "#{0:x2}{1:x2}{2:x2}", (int)c.x, (int)c.y, (int)c.z);
        }

        private static string Path(RoadCurve c, Func<float2, float2> P)
        {
            var a = P(c.A.xz); var b = P(c.B.xz); var cc = P(c.C.xz); var d = P(c.D.xz);
            return string.Format(C, "M{0} {1}C{2} {3} {4} {5} {6} {7}", F(a.x), F(a.y), F(b.x), F(b.y), F(cc.x), F(cc.y), F(d.x), F(d.y));
        }

        private static IEnumerable<(float2 a, float2 b)> Contour(float[] g, int nx, int nz, float cw, float ch, float lev)
        {
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float v0 = g[j * (nx + 1) + i], v1 = g[j * (nx + 1) + i + 1], v2 = g[(j + 1) * (nx + 1) + i + 1], v3 = g[(j + 1) * (nx + 1) + i];
                    var pts = new List<float2>(4);
                    void Edge(float a, float b, float2 pa, float2 pb)
                    {
                        if ((a < lev) == (b < lev)) return;
                        float t = (lev - a) / (b - a);
                        pts.Add(math.lerp(pa, pb, t));
                    }
                    float2 p0 = new float2(i * cw, j * ch), p1 = new float2((i + 1) * cw, j * ch), p2 = new float2((i + 1) * cw, (j + 1) * ch), p3 = new float2(i * cw, (j + 1) * ch);
                    Edge(v0, v1, p0, p1); Edge(v1, v2, p1, p2); Edge(v2, v3, p2, p3); Edge(v3, v0, p3, p0);
                    if (pts.Count >= 2) yield return (pts[0], pts[1]);
                    if (pts.Count == 4) yield return (pts[2], pts[3]);
                }
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            var words = (text ?? "").Split(' ');
            var line = new StringBuilder();
            foreach (var w in words)
            {
                if (line.Length + w.Length + 1 > width && line.Length > 0) { yield return line.ToString(); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 0) yield return line.ToString();
        }

        public static string Esc(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
