using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Layers.Roads.Terraces;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    /// <summary>
    /// s52 hillside terraces (Roads pattern "terraces"): contour streets on level terraces as deep as their zone cells, banks
    /// between them no steeper than the setting, links up the hill within the steepest-road grade (switchbacks with proper
    /// hairpins), one continuous terraform field without curtains, flat ground left alone, zone cells only on level ground.
    /// Sites: the roads design test sites (34 m dome, stream valley, 13 % hillside, flat; two existing streets and a farm).
    /// </summary>
    public class RoadsTerraceTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsTerraceTests(ITestOutputHelper o) { _out = o; }

        private static readonly string[] TerracePresets = { "terraced-hillside", "serpentine-village", "vineyard-terraces", "hill-town" };
        private static readonly string[] Slopes = { "hill", "valley", "steep" };

        private static SettingsBag Preset(string id) => new RoadsLayer().Presets.Single(p => p.Id == id).Values.Clone();

        private static SettingsBag Terraces(params (string id, object v)[] values)
        {
            var s = new RoadsLayer().Defaults();
            s.Values[RoadsIds.Pattern] = "terraces";
            s.Values[RoadsIds.LandShape] = "terrace";   // (smarter roads) terracing is the player's choice now, off by default
            foreach (var (id, v) in values) s.Values[id] = v;
            return s;
        }

        private sealed class Result
        {
            public AreaPlan Plan;
            public TerraceGround Ground;
            public AreaInput Input;
            public SiteContext Site;
            public Func<float2, float> Final => Plan.HeightAfter ?? Site.HeightAt;
        }

        private static Result Plan(SettingsBag s, string kind, float area = 200000f)
        {
            var input = RoadsDesignTests.SiteInput(s, kind, out var site, area);
            return Plan(input, site);
        }

        private static Result Plan(AreaInput input, SiteContext site)
        {
            TerraceGround g = null;
            TerraceField.Inspect = x => g = x;
            try
            {
                var plan = new RoadsLayer().Plan(input, CancellationToken.None);
                return new Result { Plan = plan, Ground = g, Input = input, Site = site };
            }
            finally { TerraceField.Inspect = null; }
        }

        /// <summary>A plane rising <paramref name="grade"/> to the north over a w × d rectangle (no existing roads).</summary>
        private static (AreaInput input, SiteContext site) Plane(SettingsBag s, float grade, float w = 600f, float d = 400f)
        {
            var site = new SiteContext { HeightAt = p => 100f + grade * p.y };
            var ring = new[] { new float3(0, 0, 0), new float3(w, 0, 0), new float3(w, 0, d), new float3(0, 0, d) }
                .Select(p => new float3(p.x, site.HeightAt(p.xz), p.z)).ToArray();
            return (new AreaInput { Shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn }, Site = site, Settings = s, Seed = 1 }, site);
        }

        /// <summary>The ground the build leaves: the plan's terraform through the build's own raster (1.75 m, 4 m bank).</summary>
        private static Func<float2, float> Built(Result r)
        {
            var items = new List<(float2[] ring, float target, int layer)>();
            var vh = new List<float[]>();
            foreach (var t in r.Plan.Terraform)
            {
                items.Add((t.Ring.Select(v => v.xz).ToArray(), t.TargetHeight, 0));
                vh.Add(TerraformSurface.VertexHeights(t));
            }
            if (items.Count == 0) return r.Site.HeightAt;
            var raster = TerraformRaster.Build(items, 1.75f, 4f, 1 << 22, vh, r.Site.HeightAt);
            var original = r.Site.HeightAt;
            return p => raster.TrySample(p, original, out float h) ? h : original(p);
        }

        private static float Slope(Func<float2, float> h, float2 p, float e = 2f)
        {
            float gx = (h(p + new float2(e, 0)) - h(p - new float2(e, 0))) / (2f * e);
            float gz = (h(p + new float2(0, e)) - h(p - new float2(0, e))) / (2f * e);
            return math.sqrt(gx * gx + gz * gz);
        }

        // ───────────────────────── the terraces ─────────────────────────

        [Fact]
        public void EveryPresetTerracesEverySlope()
        {
            var failures = new List<string>();
            foreach (var preset in TerracePresets)
                foreach (var kind in Slopes)
                {
                    var r = Plan(Preset(preset), kind);
                    var f = r.Plan.Facts;
                    _out.WriteLine($"{preset}/{kind}: levels {f["terraces.levels"]} streets {f["terraces.streets"]} links {f["terraces.links"]} hairpins {f["terraces.hairpins"]} walks {f["terraces.walks"]} terraced {f["terraces.area"]:0} m² cut {f["terraces.cut"]:0} fill {f["terraces.fill"]:0} m³");
                    if (r.Plan.HasErrors) failures.Add($"{preset}/{kind}: errors");
                    if (r.Plan.Terraform.Count == 0) failures.Add($"{preset}/{kind}: nothing terraced");
                    if (f["terraces.levels"] < 1) failures.Add($"{preset}/{kind}: no terrace street");
                    if (f["terraces.area"] < 20000) failures.Add($"{preset}/{kind}: only {f["terraces.area"]:0} m² of level terrace");
                    if (!r.Plan.Stats.Any(st => st.Key == RoadsPlanner.StatCut) || !r.Plan.Stats.Any(st => st.Key == RoadsPlanner.StatFill))
                        failures.Add($"{preset}/{kind}: no cut/fill readout");
                    if (!r.Plan.Issues.Any(i => i.Key == Placemaker.Core.Layers.Common.TerrainShaper.PermanentKey))
                        failures.Add($"{preset}/{kind}: no permanent-terrain warning");
                }
            foreach (var x in failures) _out.WriteLine("FAIL " + x);
            Assert.Empty(failures);
        }

        [Fact]
        public void TerracesAreLevelAndBanksStayWithinTheSlope()
        {
            var failures = new List<string>();
            foreach (var preset in TerracePresets)
                foreach (var kind in Slopes)
                {
                    var s = Preset(preset);
                    var r = Plan(s, kind);
                    var g = r.Ground;
                    var o = TerraceOptions.Read(s, new RoadsLayer().Controls, null);
                    // Banks: never steeper than the setting (the field's own measure over every changed node step).
                    if (g.SteepestBank > o.BankSlope + 0.02f) failures.Add($"{preset}/{kind}: bank {g.SteepestBank * 100:0}% > {o.BankSlope * 100:0}%");
                    // Terraces: every level terrace node is level with its neighbours on the terrace (cross-fall + 1.5 %).
                    var sc = g.Grid;
                    int steps = 0, tilted = 0;
                    for (int j = 0; j + 1 < sc.Ny; j++)
                        for (int i = 0; i + 1 < sc.Nx; i++)
                        {
                            int k = j * sc.Nx + i;
                            if (!g.Terrace[k]) continue;
                            foreach (int m in new[] { k + 1, k + sc.Nx })
                            {
                                if (!g.Terrace[m]) continue;
                                steps++;
                                if (math.abs(sc.Target[k] - sc.Target[m]) / sc.Cell > o.CrossFall + 0.015f) tilted++;
                            }
                        }
                    _out.WriteLine($"{preset}/{kind}: steepest bank {g.SteepestBank * 100:0.0}%, terrace steps {steps}, tilted {tilted}");
                    if (steps == 0) failures.Add($"{preset}/{kind}: no terrace");
                    else if (tilted > steps * 0.01) failures.Add($"{preset}/{kind}: {tilted} of {steps} terrace steps tilted");
                }
            foreach (var x in failures) _out.WriteLine("FAIL " + x);
            Assert.Empty(failures);
        }

        [Fact]
        public void NoTerrainCurtains()
        {
            var failures = new List<string>();
            foreach (var preset in TerracePresets)
                foreach (var kind in Slopes)
                {
                    var r = Plan(Preset(preset), kind);
                    var ring = r.Input.Shape.Ring.Select(p => p.xz).ToArray();
                    Poly.Bounds(ring, out float2 mn, out float2 mx);
                    var nets = r.Plan.Nets.Select(n => (n.A, n.B, n.C, n.D, Composer.DefaultHalfWidth(n.Role), TerrainMetrics.SeatFollow)).ToList();
                    var c = TerrainMetrics.Curtains(Built(r), r.Site.HeightAt, mn - 10f, mx + 10f, TerrainMetrics.Texel, TerrainMetrics.OffNets(nets));
                    _out.WriteLine($"{preset}/{kind}: {c}");
                    if (c.Count > 0) failures.Add($"{preset}/{kind}: {c.Count} sheets, worst {c.Max:0.00} m at {c.At}");
                }
            foreach (var x in failures) _out.WriteLine("FAIL " + x);
            Assert.Empty(failures);
        }

        [Fact(Skip = "Roads no longer zone (the user, 26 Oct: the Zoning category zones); RoadPreZone.Offered is false")]
        public void ZoneCellsLieLevelOnTheTerraces()
        {
            foreach (var (preset, kind) in new[] { ("terraced-hillside", "steep"), ("terraced-hillside", "hill"), ("hill-town", "steep") })
            {
                var s = Preset(preset);
                var r = Plan(s, kind);
                var o = TerraceOptions.Read(s, new RoadsLayer().Controls, null);
                Assert.NotEmpty(r.Plan.Zoning);
                Assert.Equal(o.Cells, r.Plan.Facts[RoadPreZone.FactDepth]);
                float level = math.max(o.CrossFall + 0.025f, o.FlatSlope) + 0.01f;
                int pts = 0, tilted = 0;
                foreach (var z in r.Plan.Zoning)
                {
                    var q = new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz };
                    float2 c = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
                    // The quad is the cell rectangle shrunk by 1 m: the outer cell centres are ~3 m in from its corners.
                    foreach (var corner in q.Append(c))
                    {
                        float d = math.distance(corner, c);
                        var p = d > 4.3f ? corner + (c - corner) * (4.3f / d) : c;
                        pts++;
                        if (Slope(r.Final, p, 3.5f) > level) tilted++;
                    }
                }
                _out.WriteLine($"{preset}/{kind}: {r.Plan.Zoning.Count} zoning quads, {pts} points, {tilted} on sloped ground");
                Assert.True(tilted <= pts * 0.03, $"{preset}/{kind}: {tilted} of {pts} zoned points not level");
            }
        }

        [Fact]
        public void ZoneRuleTakesOnlyTheLevelRowsUpToTheTerraceDepth()
        {
            // A block of 4 × 6 cells along a road on the x axis, the road on its south side (Direction points to the road).
            var g = new ZoneGrid { Position = new float2(16f, 28f), Direction = new float2(0, -1), Size = new int2(4, 6) };
            // Level up to 30 m from the road, a bank beyond; at x > 24 the bank starts at 14 m.
            bool Level(float2 p) => p.y < (p.x > 24f ? 14f : 30f);
            var rects = RoadPreZone.Rects(g, new RoadPreZone.DepthRule { MaxDepth = 3, Level = Level });
            Assert.Equal(2, rects.Count);
            float Depth(float2[] r) => r.Max(v => v.y) - r.Min(v => v.y);
            var deep = rects.Single(r => r.Min(v => v.x) < 10f);
            var shallow = rects.Single(r => r.Min(v => v.x) > 20f);
            Assert.InRange(Depth(deep), 3 * 8f - 2.5f, 3 * 8f);        // capped at three cells (1 m inside the cell edges)
            Assert.InRange(Depth(shallow), 1 * 8f - 2.5f, 1 * 8f);     // one cell lies level there
            // No rule: the whole block as before.
            var whole = RoadPreZone.Rects(g, null);
            Assert.Single(whole);
            Assert.InRange(Depth(whole[0]), 6 * 8f - 2.5f, 6 * 8f);
            // The live refresh after the build uses the same rule.
            var road = new PlannedNet { A = new float3(0, 0, 0), B = new float3(16, 0, 0), C = new float3(32, 0, 0), D = new float3(48, 0, 0), Role = NetRole.Street };
            var live = new List<RoadPreZone.LiveBlock> { new RoadPreZone.LiveBlock { Grid = g, OwnerA = road.A, OwnerB = road.B, OwnerC = road.C, OwnerD = road.D } };
            var quads = RoadPreZone.FromLive(new[] { road }, live, "EU Residential Low", 1.5f, new RoadPreZone.DepthRule { MaxDepth = 3, Level = Level });
            Assert.Equal(2, quads.Count);
            // The plan facts carry the rule to the game.
            var plan = new AreaPlan { HeightAfter = p => p.y < 30f ? 0f : (p.y - 30f) * 0.4f };
            plan.Facts[RoadPreZone.FactDepth] = 4; plan.Facts[RoadPreZone.FactLevel] = 0.035;
            var rule = RoadPreZone.RuleOf(plan);
            Assert.Equal(4, rule.MaxDepth);
            Assert.True(rule.Level(new float2(10, 10)));
            Assert.False(rule.Level(new float2(10, 40)));
            Assert.Null(RoadPreZone.RuleOf(new AreaPlan()));
        }

        // ───────────────────────── roads, links, hairpins ─────────────────────────

        [Fact]
        public void RoadsOnTheTerracesStayWithinTheGrade()
        {
            // The roads the plan designed (terrace streets, links up the hill, bank walks) on the ground the build leaves.
            // Links the network's connectivity pass adds between pieces are straight and are reported (Steep issue), not designed.
            var failures = new List<string>();
            foreach (var preset in TerracePresets)
                foreach (var kind in Slopes)
                {
                    var s = Preset(preset);
                    var r = Plan(s, kind);
                    float maxG = (float)s.Num(RoadsIds.MaxGrade, 10) / 100f;
                    var len = new float[4]; var over = new float[4];
                    var ring = r.Input.Shape.Ring.Select(p => p.xz).ToArray();
                    foreach (var (c, k) in r.Ground.Roads)
                    {
                        // Where a road meets the area outline the ground there stays the city's: not ours to grade.
                        if (Poly.DistanceToBoundary(ring, c.A.xz) < 25f || Poly.DistanceToBoundary(ring, c.D.xz) < 25f) continue;
                        float l = c.LengthXZ(12);
                        float g = RoadGraph.MaxGrade(c, r.Final, 16f, 4f);
                        len[k] += l;
                        // Terrace streets are level (where the gate leaves flat ground alone they follow it: under the flat threshold).
                        float lim = k == 2 ? RoadWalkways.MaxWalkGrade + 0.01f : k == 0 ? 0.065f : maxG + 0.015f;
                        if (g > lim) { over[k] += l; _out.WriteLine($"   kind {k}: {l:0} m at {g * 100:0.0}% from {c.A.xz} to {c.D.xz}"); }
                    }
                    _out.WriteLine($"{preset}/{kind}: streets {over[0]:0}/{len[0]:0} m over 6.5 %, links {over[1]:0}/{len[1]:0} m over {maxG * 100:0}%, walks {over[2]:0}/{len[2]:0} m over 8 %, other {len[3]:0} m");
                    if (over[0] > 0.03f * len[0]) failures.Add($"{preset}/{kind}: terrace streets not level ({over[0]:0} of {len[0]:0} m)");
                    if (over[1] > 0.05f * math.max(1f, len[1])) failures.Add($"{preset}/{kind}: links too steep ({over[1]:0} of {len[1]:0} m)");
                    if (over[2] > 0.1f * math.max(1f, len[2])) failures.Add($"{preset}/{kind}: walks too steep ({over[2]:0} of {len[2]:0} m)");
                }
            foreach (var x in failures) _out.WriteLine("FAIL " + x);
            Assert.Empty(failures);
        }

        [Fact]
        public void LinksClimbWithinTheGradeWithLevelLandings()
        {
            foreach (var preset in TerracePresets)
                foreach (var kind in Slopes)
                {
                    var s = Preset(preset);
                    var (lay, o) = Layout(s, kind);
                    if (lay.Streets.Select(x => x.Level).Distinct().Count() > 1)
                        Assert.True(lay.Links.Count > 0, $"{preset}/{kind}: no links between {lay.Streets.Count} streets; " + string.Join(", ", lay.Rejects.Select(kv => kv.Key + " " + kv.Value)));
                    foreach (var l in lay.Links)
                    {
                        Assert.True(l.Grade <= o.MaxGrade * 1.05f + 1e-4f, $"{preset}/{kind}: {l.Kind} link climbs {l.Grade * 100:0.0}%");
                        // Junctions on terraces are level: the first and last landing metres do not climb.
                        var st = Lines.Stations(l.P);
                        float total = st[st.Length - 1];
                        for (int i = 1; i < l.P.Count; i++)
                            if (st[i] <= TerraceOptions.Landing - 0.5f || st[i - 1] >= total - TerraceOptions.Landing + 0.5f)
                                Assert.True(math.abs(l.H[i] - l.H[i - 1]) < 0.01f, $"{preset}/{kind}: {l.Kind} landing climbs");
                        Assert.True(math.abs(l.H[0] - lay.Streets[l.From].HeightAt(l.FromS)) < 0.01f);
                        Assert.True(math.abs(l.H[l.H.Count - 1] - lay.Streets[l.To].HeightAt(l.ToS)) < 0.01f);
                    }
                    foreach (var w in lay.Walks)
                        for (int i = 1; i < w.P.Count; i++)
                        {
                            float d = math.distance(w.P[i - 1], w.P[i]);
                            if (d > 0.5f) Assert.True(math.abs(w.H[i] - w.H[i - 1]) / d <= RoadWalkways.MaxWalkGrade + 1e-3f, $"{preset}/{kind}: walk too steep");
                        }
                }
        }

        private static (TerraceLayout lay, TerraceOptions o) Layout(SettingsBag s, string kind)
        {
            var input = RoadsDesignTests.SiteInput(s, kind, out var site);
            return Layout(input, site);
        }

        private static (TerraceLayout lay, TerraceOptions o) Layout(AreaInput input, SiteContext site)
        {
            var layer = new RoadsLayer();
            var o = TerraceOptions.Read(input.Settings, layer.Controls, RoadsPlanner.HalfWidths(input.Settings, site));
            var ring = input.Shape.Ring.Select(p => p.xz).ToArray();
            Poly.Bounds(ring, out float2 mn, out float2 mx);
            var obstacles = new RoadObstacles(site, mn, mx);
            var lay = TerraceLayout.Build(ring, site.HeightAt, o, obstacles, CancellationToken.None);
            TerraceRoutes.Plan(lay, o, obstacles, CancellationToken.None);
            return (lay, o);
        }

        [Fact]
        public void SwitchbacksKeepTheirHairpinRadius()
        {
            // A 9 % hillside with deep terraces: a link straight up would be too steep; stacked switchback legs fit between them.
            var s = Terraces((RoadsIds.TerraceLinks, "serpentine"), (RoadsIds.TerraceCells, 4.0), (RoadsIds.MaxGrade, 10.0));
            var (input, site) = Plane(s, 0.09f, 700f, 450f);
            var (lay, o) = Layout(input, site);
            var serp = lay.Links.Where(l => l.Kind == "serpentine").ToList();
            _out.WriteLine($"step {lay.LevelStep:0.0} spacing {lay.Spacing:0} streets {lay.Streets.Count}: " + string.Join(" ", lay.Links.Select(l => $"{l.Kind}({l.Hairpins} hairpins r{l.MinRadius:0} {l.Grade * 100:0.0}%)")));
            _out.WriteLine("rejects " + string.Join(", ", lay.Rejects.Select(kv => kv.Key + " " + kv.Value)));
            Assert.NotEmpty(serp);
            Assert.Contains(serp, l => l.Hairpins >= 1);
            foreach (var l in serp)
            {
                Assert.True(l.MinRadius >= o.HairpinRadius - 0.01f, $"hairpin radius {l.MinRadius:0.0} < {o.HairpinRadius:0.0}");
                Assert.True(l.Grade <= o.MaxGrade * 1.05f, $"switchback climbs {l.Grade * 100:0.0}%");
            }
            // The built switchbacks keep their profile on the ground: the legs climb at about the designed grade.
            var r = Plan(input, site);
            float maxG = 0.1f, total = 0f, over = 0f;
            foreach (var n in r.Plan.Nets)
            {
                if (n.Role == NetRole.Pathway) continue;
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                float len = c.LengthXZ(12);
                total += len;
                if (RoadGraph.MaxGrade(c, r.Final, 16f, 4f) > maxG + 0.02f) over += len;
            }
            _out.WriteLine($"{over:0} of {total:0} m over the grade");
            Assert.True(over <= 0.05f * total, $"{over:0} of {total:0} m of road over the grade");
        }

        [Fact]
        public void LinkStylesAndAvenues()
        {
            var direct = Layout(Terraces((RoadsIds.TerraceLinks, "direct")), "steep").lay;
            Assert.Contains(direct.Links, l => l.Kind == "direct" || l.Kind == "diagonal");
            var spiral = Layout(Terraces((RoadsIds.TerraceLinks, "spiral"), (RoadsIds.TerraceCells, 3.0)), "steep").lay;
            Assert.NotEmpty(spiral.Links);
            Assert.Contains(spiral.Links, l => l.Kind == "spiral");
            var avenues = Plan(Terraces((RoadsIds.TerraceAvenues, true)), "steep");
            Assert.Contains(avenues.Plan.Nets, n => n.Role == NetRole.Avenue);
            var streets = Plan(Terraces(), "steep");
            Assert.DoesNotContain(streets.Plan.Nets, n => n.Role == NetRole.Avenue);
        }

        // ───────────────────────── flat ground, limits, settings ─────────────────────────

        [Fact]
        public void FlatGroundIsNeverTerraced()
        {
            // A flat site: rows of streets, nothing terraced, and the player is told.
            var flat = Plan(Terraces(), "flat");
            Assert.NotEmpty(flat.Plan.Nets);
            Assert.Empty(flat.Plan.Terraform);
            Assert.Contains(flat.Plan.Issues, i => i.Key == RoadsPlanner.IssueTerraceFlat);
            // A 3 % slope is under the 5 % threshold: no terraform either.
            var (gi, gs) = Plane(Terraces(), 0.03f);
            Assert.Empty(Plan(gi, gs).Plan.Terraform);
            // An 8 % slope is terraced at the default threshold and left alone above it.
            var (si, ss) = Plane(Terraces(), 0.08f);
            Assert.NotEmpty(Plan(si, ss).Plan.Terraform);
            var (hi, hs) = Plane(Terraces((RoadsIds.TerraceFlat, 10.0)), 0.08f);
            Assert.Empty(Plan(hi, hs).Plan.Terraform);
            // The valley floor (≈1 % along the stream) stays as it is while its sides are terraced.
            var valley = Plan(Terraces(), "valley");
            Assert.NotEmpty(valley.Plan.Terraform);
            var ring = valley.Input.Shape.Ring.Select(p => p.xz).ToArray();
            Poly.Bounds(ring, out float2 mn, out float2 mx);
            int floor = 0, changed = 0;
            for (float z = mn.y; z <= mx.y; z += 6f)
                for (float x = mn.x; x <= mx.x; x += 6f)
                {
                    var p = new float2(x, z);
                    if (!Poly.Contains(ring, p) || Poly.DistanceToBoundary(ring, p) < 5f) continue;
                    // Ground that is flat at the survey's scale (≥ 25 m around), away from the roads (a road keeps its profile).
                    if (valley.Ground.Roads.Any(rd => rd.curve.DistanceXZ(p, out _) < 35f)) continue;
                    if (Slope(valley.Site.HeightAt, p, 12f) > 0.025f) continue;
                    floor++;
                    if (math.abs(valley.Final(p) - valley.Site.HeightAt(p)) > 0.15f) changed++;
                }
            _out.WriteLine($"valley floor points {floor}, changed {changed}");
            Assert.True(floor > 50);
            Assert.True(changed <= floor * 0.02, $"{changed} of {floor} flat valley points changed");
        }

        [Fact]
        public void EarthworkStaysWithinTheLimit()
        {
            foreach (var limit in new[] { 3.0, 8.0 })
            {
                var r = Plan(Terraces((RoadsIds.TerraceEarthwork, limit), (RoadsIds.TerraceCells, 5.0)), "steep");
                double cut = r.Plan.Facts["terraces.maxCut"], fill = r.Plan.Facts["terraces.maxFill"];
                _out.WriteLine($"limit {limit}: deepest cut {cut:0.00} m, highest fill {fill:0.00} m, issues {string.Join(",", r.Plan.Issues.Select(i => i.Key))}");
                Assert.True(cut <= limit + 0.05 && fill <= limit + 0.05);
                if (limit < 4) Assert.Contains(r.Plan.Issues, i => i.Key == RoadsPlanner.IssueTerraceEarthwork);
                // The readout: volumes in m³ and the deepest cut / highest fill.
                Assert.Contains(r.Plan.Stats, st => st.Key == RoadsPlanner.StatCut && st.Unit == "m³" && st.Value > 0);
                Assert.Contains(r.Plan.Stats, st => st.Key == RoadsPlanner.StatDeepest && st.Value <= limit + 0.05);
            }
        }

        [Fact]
        public void SettingsShapeThePlan()
        {
            var narrow = Plan(Terraces((RoadsIds.TerraceCells, 2.0)), "steep");
            var deep = Plan(Terraces((RoadsIds.TerraceCells, 6.0)), "steep");
            _out.WriteLine($"2 cells: {narrow.Plan.Facts["terraces.levels"]} levels, step {narrow.Plan.Facts["terraces.step"]:0.0} m; 6 cells: {deep.Plan.Facts["terraces.levels"]} levels, step {deep.Plan.Facts["terraces.step"]:0.0} m");
            Assert.True(narrow.Plan.Facts["terraces.levels"] > deep.Plan.Facts["terraces.levels"]);
            Assert.True(narrow.Plan.Facts["terraces.spacing"] < deep.Plan.Facts["terraces.spacing"]);
            // A gentler bank needs a wider gap between the terraces.
            var standard = Plan(Terraces(), "steep");
            var gentle = Plan(Terraces((RoadsIds.TerraceBankSlope, 25.0)), "steep");
            Assert.True(gentle.Plan.Facts["terraces.spacing"] > standard.Plan.Facts["terraces.spacing"]);
            Assert.True(gentle.Ground.SteepestBank <= 0.27f);
            // Walks, paint and trees can each be switched off.
            var bare = Plan(Terraces((RoadsIds.Walkways, 0.0), (RoadsIds.TerracePaint, false), (RoadsIds.TerraceTrees, false), (RoadsIds.GroundWear, "off")), "steep");
            Assert.DoesNotContain(bare.Plan.Nets, n => n.Role == NetRole.Pathway);
            Assert.Empty(bare.Plan.Paint);
            Assert.DoesNotContain(bare.Plan.Objects, o => o.Role == ObjectRole.Tree);
            var (di, ds) = Plane(Terraces((RoadsIds.Walkways, 100.0), (RoadsIds.TerraceCells, 3.0)), 0.1f);
            var dressed = Plan(di, ds);
            Assert.Contains(dressed.Plan.Nets, n => n.Role == NetRole.Pathway);
            Assert.NotEmpty(dressed.Plan.Paint);
            Assert.Contains(dressed.Plan.Objects, o => o.Role == ObjectRole.Tree);
            Assert.Contains(dressed.Plan.Paint, p => p.Layer == TerrainMaterials.Dry);
            // Trees stand on the banks, on the built ground.
            foreach (var t in dressed.Plan.Objects.Where(o => o.Role == ObjectRole.Tree))
                Assert.True(math.abs(t.Position.y - dressed.Final(t.Position.xz)) < 0.05f);
            // The "wall" style: steeper straight faces painted as worn dirt.
            var wall = Plan(Terraces((RoadsIds.TerraceBank, "wall"), (RoadsIds.TerraceBankSlope, 45.0)), "steep");
            Assert.Contains(wall.Plan.Paint, p => p.Layer == TerrainMaterials.Dirt);
            Assert.True(wall.Ground.SteepestBank > standard.Ground.SteepestBank);
        }

        [Fact]
        public void PresetsAreTerracesWithTheirOwnCharacter()
        {
            var layer = new RoadsLayer();
            foreach (var id in TerracePresets)
                Assert.Equal("terraces", layer.Presets.Single(x => x.Id == id).Values.S(RoadsIds.Pattern, ""));
            Assert.Equal("serpentine", Preset("serpentine-village").S(RoadsIds.TerraceLinks, ""));
            Assert.Equal("spiral", Preset("hill-town").S(RoadsIds.TerraceLinks, ""));
            Assert.True(Preset("hill-town").B(RoadsIds.TerraceAvenues, false));
            Assert.Equal("wall", Preset("vineyard-terraces").S(RoadsIds.TerraceBank, ""));
            Assert.Equal(RoadPreZone.Default, new RoadsLayer().Defaults().S(RoadsIds.PreZone, ""));
            // no road style zones any more (the user, 26 Oct)
            foreach (var p in layer.Presets) Assert.Equal(RoadPreZone.None, p.Values.S(RoadsIds.PreZone, RoadPreZone.None));
            // The links show for the terraces pattern only; the ground controls for every pattern on terraced land.
            var linkIds = new[] { RoadsIds.TerraceLinks, RoadsIds.TerraceLinkSpacing, RoadsIds.TerraceAvenues };
            foreach (var c in layer.Controls.Where(c => c.Id.StartsWith("terrace")))
                Assert.Equal(linkIds.Contains(c.Id) ? "pattern=terraces" : "landShape=terrace", c.VisibleWhen);
        }

        [Fact]
        public void TwentyHectaresPlanWithinBudget()
        {
            var s = Preset("terraced-hillside");
            new RoadsLayer().Plan(RoadsDesignTests.SiteInput(s, "steep", out _), CancellationToken.None);
            var sw = Stopwatch.StartNew();
            new RoadsLayer().Plan(RoadsDesignTests.SiteInput(s, "hill", out _), CancellationToken.None);
            sw.Stop();
            _out.WriteLine($"{sw.ElapsedMilliseconds} ms");
            Assert.True(sw.ElapsedMilliseconds < TimingBudget.Ms(2000), $"{sw.ElapsedMilliseconds} ms for 20 ha of terraces");
        }
    }
}
