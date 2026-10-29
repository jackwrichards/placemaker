// The strip mall's split between shops and parking (the user, 2026-10-18: "strip malls look like they're 90 % parking lot, 10 %
// buildings. Shouldn't it be kind of more of a split?"; docs/refactor/STRIP-MALL.md, "The split"). Composed with the game's shop
// lanes (the vanilla Alley carries no zone blocks there, so the shops front a 16 m Small Road). With PM_PROBE_OUT set, Probe writes
// every preset x site x entrance set's numbers and a picture of each plan (SVG) to that folder.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Commercial;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class StripMallSplitTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>
        /// The user's block (about 190 x 180 m, two car entrances near the ends of one street and a footpath between, the plaza in the
        /// row): shops on a fifth of the site at least, asphalt on two fifths at most (it was 11 % shops and 58 % asphalt).
        /// </summary>
        [Theory]
        [InlineData("plazaRow")]
        [InlineData("classic")]
        public void TheUsersBlockIsSplitBetweenShopsAndParking(string preset)
        {
            var ring = TestKit.Rect(190, 180);
            var (plan, _) = Compose(preset, ring, "corners");
            Assert.False(plan.HasErrors, preset);
            var (shops, asphalt) = Shares(plan, ring);
            Assert.True(shops >= 0.2f, $"{preset}: shops {shops:P0} of the site");
            Assert.True(asphalt <= 0.4f, $"{preset}: asphalt {asphalt:P0} of the site");
        }

        private static (ComposedPlan plan, long ms) Compose(string preset, float3[] ring, string ents)
        {
            var site = ZoningCutoutTests.Streets(ring);
            site.Assets["Alley"] = new AssetInfo { ZoneBlocks = 2, NetWidth = 8f };
            site.Assets["Small Road"] = new AssetInfo { ZoneBlocks = 1, NetWidth = 16f };
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("stripMall"), 5);
            Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            if (ents == "mid") ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            else if (ents == "corner")
            {
                // a corner lot: one car entrance on the south street, one on the west street, both near the corner they share
                var p = Poly.To2D(ring);
                float2 mn = p[0], mx = p[0]; foreach (var v in p) { mn = math.min(mn, v); mx = math.max(mx, v); }
                r.Root.Markers.Add(ShopTestEntrances.At(ring, new float2(math.lerp(mn.x, mx.x, 0.3f), mn.y), ShopEntrances.TwoWay));
                r.Root.Markers.Add(ShopTestEntrances.At(ring, new float2(mn.x, math.lerp(mn.y, mx.y, 0.3f)), ShopEntrances.TwoWay));
            }
            else
            {
                // as in the user's screenshot: two car entrances near the ends of the east street, a footpath between
                var p = Poly.To2D(ring);
                float2 mn = p[0], mx = p[0]; foreach (var v in p) { mn = math.min(mn, v); mx = math.max(mx, v); }
                r.Root.Markers.Add(ShopTestEntrances.At(ring, new float2(mx.x, math.lerp(mn.y, mx.y, 0.06f)), ShopEntrances.TwoWay));
                r.Root.Markers.Add(ShopTestEntrances.At(ring, new float2(mx.x, math.lerp(mn.y, mx.y, 0.94f)), ShopEntrances.TwoWay));
                r.Root.Markers.Add(ShopTestEntrances.At(ring, new float2(mx.x, math.lerp(mn.y, mx.y, 0.5f)), ShopEntrances.Footpath));
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            return (plan, sw.ElapsedMilliseconds);
        }

        /// <summary>The shop plots' and the asphalt's shares of the site.</summary>
        private static (float shops, float asphalt) Shares(ComposedPlan plan, float3[] ring)
        {
            float siteA = Poly.Area(Poly.To2D(ring));
            float shops = Cutouts.Of(plan).Sum(c => Poly.Area(c));
            var host = plan.Layers.First(l => l.GeneratorId == "stripMall");
            float asphalt = host.Surfaces.Where(s => s.Role == SurfaceRole.Asphalt && s.Ring?.Length >= 3).Sum(s => Poly.Area(Poly.To2D(s.Ring)));
            return (shops / siteA, asphalt / siteA);
        }

        [Fact]
        public void ProbeGrid()
        {
            string dir = Environment.GetEnvironmentVariable("PM_PROBE_OUT");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (var (w, d) in new[] { (360f, 110f), (150f, 140f), (300f, 280f) })
            {
                var ring = TestKit.Rect(w, d);
                var site = ZoningCutoutTests.Streets(ring);
                foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
                var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("stripMall"), 5);
                Editor.ApplyPreset(r, RecipePaths.Root, Environment.GetEnvironmentVariable("PM_PROBE_PRESET") ?? "classic");
                ShopTestEntrances.Place(r, ring, site, footpath: true);
                var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
                var host = plan.Layers.First(l => l.GeneratorId == "stripMall");
                sb.AppendLine($"{w}x{d}: " + string.Join(" ", plan.AllIssues().Select(i => i.Key + (i.At.HasValue ? "@" + i.At.Value.x.ToString("0") + "," + i.At.Value.z.ToString("0") : ""))) + " | " + string.Join(" ", host.Facts.Where(kv => kv.Key.EndsWith("Row") || kv.Key.EndsWith("Cells")).Select(kv => kv.Key + "=" + kv.Value)));
                File.WriteAllText(Path.Combine(dir, $"grid-{w}x{d}.svg"), Svg(ring, plan, Cutouts.Of(plan)));
            }
            File.WriteAllText(Path.Combine(dir, "grid.txt"), sb.ToString());
        }

        [Fact]
        public void Probe()
        {
            string dir = Environment.GetEnvironmentVariable("PM_PROBE_OUT");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var sites = new (string name, float3[] ring)[]
            {
                ("150x140", TestKit.Rect(150, 140)), ("190x180", TestKit.Rect(190, 180)), ("220x220", TestKit.Rect(220, 220)),
                ("300x280", TestKit.Rect(300, 280)), ("400x160", TestKit.Rect(400, 160)), ("L200", TestKit.LShape(200)), ("360x110", TestKit.Rect(360, 110)),
            };
            foreach (var preset in (Environment.GetEnvironmentVariable("PM_PROBE_PRESETS") ?? "classic,plazaRow").Split(','))
                foreach (var (name, ring) in sites)
                    foreach (var ents in (Environment.GetEnvironmentVariable("PM_PROBE_ENTS") ?? "mid,corners").Split(','))
                    {
                        var (plan, ms) = Compose(preset, ring, ents);
                        var (shops, asphalt) = Shares(plan, ring);
                        var cut = Cutouts.Of(plan);
                        var host = plan.Layers.First(l => l.GeneratorId == "stripMall");
                        double stalls = host.Facts.TryGetValue("capacity", out var c) ? c : -1;
                        string err = string.Join(",", plan.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key));
                        sb.AppendLine($"{preset}/{name}/{ents}: shops {100 * shops:F0}% asphalt {100 * asphalt:F0}% stalls {stalls} plots {cut.Count} {ms} ms {err} | " + string.Join(" ", host.Facts.Where(kv => kv.Key.StartsWith("compact") || kv.Key.StartsWith("est") || kv.Key.StartsWith("even") || kv.Key.EndsWith("Cells") || kv.Key.StartsWith("double") || kv.Key.StartsWith("u") || kv.Key == "lRow" || kv.Key == "pylon" || kv.Key == "frontHedge" || kv.Key == "serviceYards" || kv.Key == "stripRows" || kv.Key == "square").Select(kv => kv.Key + "=" + kv.Value.ToString("0.#", CultureInfo.InvariantCulture))));
                        File.WriteAllText(Path.Combine(dir, $"{preset}-{name}-{ents}.svg"), Svg(ring, plan, cut));
                    }
            File.WriteAllText(Path.Combine(dir, "probe.txt"), sb.ToString());
        }

        private static string Svg(float3[] ring, ComposedPlan plan, List<float2[]> cut)
        {
            var r2 = Poly.To2D(ring);
            float minX = r2.Min(p => p.x) - 20, minY = r2.Min(p => p.y) - 20, maxX = r2.Max(p => p.x) + 20, maxY = r2.Max(p => p.y) + 20;
            string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
            string Pts(float2[] ps) => string.Join(" ", ps.Select(p => F(p.x) + "," + F(maxY + minY - p.y)));
            string Pts3(float3[] ps) => Pts(Poly.To2D(ps));
            float sc = 600f / math.max(maxX - minX, maxY - minY);
            var s = new StringBuilder();
            s.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='{F(minX)} {F(minY)} {F(maxX - minX)} {F(maxY - minY)}' width='{F((maxX - minX) * sc)}' height='{F((maxY - minY) * sc)}'><rect x='{F(minX)}' y='{F(minY)}' width='{F(maxX - minX)}' height='{F(maxY - minY)}' fill='#cfe3c0'/>");
            s.Append($"<polygon points='{Pts3(ring)}' fill='#b9d9a5' stroke='#333' stroke-width='0.6'/>");
            foreach (var l in plan.Layers)
                foreach (var a in l.Surfaces) if (a.Ring?.Length >= 3) s.Append($"<polygon points='{Pts3(a.Ring)}' fill='{(a.Role == SurfaceRole.Asphalt ? "#8a8f99" : "none")}' stroke='#6a6' stroke-width='0.2'/>");
            foreach (var c in cut) s.Append($"<polygon points='{Pts(c)}' fill='#5b8def' fill-opacity='0.75' stroke='#1a3f9e' stroke-width='0.4'/>");
            foreach (var l in plan.Layers)
                foreach (var nt in l.Nets)
                {
                    string role = nt.Role.ToString();
                    string col = nt.Role == NetRole.Alley ? "#222" : role.Contains("Path") || role.Contains("Foot") ? "#c80" : "#555";
                    float w = nt.Role == NetRole.Alley ? 2.5f : 0.8f;
                    s.Append($"<path d='M{F(nt.A.x)},{F(maxY + minY - nt.A.z)} C{F(nt.B.x)},{F(maxY + minY - nt.B.z)} {F(nt.C.x)},{F(maxY + minY - nt.C.z)} {F(nt.D.x)},{F(maxY + minY - nt.D.z)}' stroke='{col}' stroke-width='{F(w)}' fill='none'/>");
                }
            s.Append("</svg>");
            return s.ToString();
        }
    }
}
