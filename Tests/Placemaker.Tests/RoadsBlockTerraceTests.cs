using System;
using System.Collections.Generic;
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
    /// Terraced blocks (Roads "Land: Terraced", every pattern but the hillside terraces): each zoning street's frontage level
    /// across the street, banks or retaining walls at the back of the lots, zone cells only on level land, no curtains, the
    /// earthwork cap held, flat ground and "Natural" land left alone. Sites: the roads design test sites (34 m dome, stream
    /// valley, 13 % hillside, flat) and plain planes.
    /// </summary>
    public class RoadsBlockTerraceTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsBlockTerraceTests(ITestOutputHelper o) { _out = o; }

        private static readonly string[] Patterns = { "grid", "organic", "superblock" };
        private static readonly string[] Slopes = { "hill", "valley", "steep" };

        private static SettingsBag Roads(string pattern, params (string id, object v)[] values)
        {
            var s = new RoadsLayer().Defaults();
            s.Values[RoadsIds.Pattern] = pattern;
            s.Values[RoadsIds.PreZone] = Placemaker.Core.Layers.Common.ZoneKinds.Default;
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

        private static Result Plan(SettingsBag s, string kind)
        {
            var input = RoadsDesignTests.SiteInput(s, kind, out var site);
            return Plan(input, site);
        }

        /// <summary>A plane rising <paramref name="grade"/> to the north over a w × d rectangle (no existing roads).</summary>
        private static Result Plane(SettingsBag s, float grade, float w = 500f, float d = 400f)
        {
            var site = new SiteContext { HeightAt = p => 100f + grade * p.y };
            var ring = new[] { new float3(0, 0, 0), new float3(w, 0, 0), new float3(w, 0, d), new float3(0, 0, d) }
                .Select(p => new float3(p.x, site.HeightAt(p.xz), p.z)).ToArray();
            var input = new AreaInput { Shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn }, Site = site, Settings = s, Seed = 1 };
            return Plan(input, site);
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

        private static float SlopeAlong(Func<float2, float> h, float2 p, float2 dir, float e = 3.5f)
        {
            dir = math.normalizesafe(dir);
            return math.abs(h(p + dir * e) - h(p - dir * e)) / (2f * e);
        }

        // ───────────────────────── the ground ─────────────────────────

        [Fact]
        public void EveryPatternTerracesEverySlope()
        {
            var failures = new List<string>();
            foreach (var pattern in Patterns)
                foreach (var kind in Slopes)
                {
                    var r = Plan(Roads(pattern), kind);
                    var f = r.Plan.Facts;
                    double F(string k) => f.TryGetValue(k, out double v) ? v : -1;
                    _out.WriteLine($"{pattern}/{kind}: terraced {F("blocks.area"):0} m², cut {F("blocks.cut"):0} fill {F("blocks.fill"):0} m³, max cut {F("blocks.maxCut"):0.0} fill {F("blocks.maxFill"):0.0} m, walls {F("blocks.walls")} ({F("blocks.wallLength"):0} m, highest {F("blocks.wallHighest"):0.0} m), paints {F("blocks.paints")}, trees {F("blocks.trees")}, {r.Ground?.Ms[3]:0} ms");
                    if (r.Plan.HasErrors) failures.Add($"{pattern}/{kind}: errors");
                    if (r.Plan.Terraform.Count == 0) failures.Add($"{pattern}/{kind}: nothing terraced");
                    // (on the steep site a superblock's climbing streets are ramps through the terraces: less level frontage)
                    if (F("blocks.area") < 5000) failures.Add($"{pattern}/{kind}: only {F("blocks.area"):0} m² of level frontage");
                    if (r.Plan.HeightAfter == null) failures.Add($"{pattern}/{kind}: no ground after");
                    if (r.Ground != null && (r.Ground.Earth.MaxCut > 8.05f || r.Ground.Earth.MaxFill > 8.05f))
                        failures.Add($"{pattern}/{kind}: earthwork past the cap ({r.Ground.Earth})");
                }
            foreach (var x in failures) _out.WriteLine("FAIL " + x);
            Assert.Empty(failures);
        }

        [Fact]
        public void NoTerrainCurtains()
        {
            var failures = new List<string>();
            foreach (var pattern in Patterns)
                foreach (var kind in Slopes)
                {
                    var r = Plan(Roads(pattern), kind);
                    var ring = r.Input.Shape.Ring.Select(p => p.xz).ToArray();
                    Poly.Bounds(ring, out float2 mn, out float2 mx);
                    var nets = r.Plan.Nets.Select(n => (n.A, n.B, n.C, n.D, Composer.DefaultHalfWidth(n.Role), TerrainMetrics.SeatFollow)).ToList();
                    // Known limit: where a wall ends, its full step has to turn into a bank within a few metres; the ground right
                    // at a wall's end is left out (the rest of the build is held to no curtains at all).
                    var offNets = TerrainMetrics.OffNets(nets);
                    var ends = r.Ground?.Walls.SelectMany(w => new[] { w.Line[0], w.Line[w.Line.Length - 1] }).ToList() ?? new List<float2>();
                    Func<float2, bool> mask = p => offNets(p) && ends.All(e => math.distance(e, p) > 10f);
                    var c = TerrainMetrics.Curtains(Built(r), r.Site.HeightAt, mn - 10f, mx + 10f, TerrainMetrics.Texel, mask);
                    _out.WriteLine($"{pattern}/{kind}: {c}");
                    if (c.Count > 0) failures.Add($"{pattern}/{kind}: {c.Count} sheets, worst {c.Max:0.00} m at {c.At}");
                }
            foreach (var x in failures) _out.WriteLine("FAIL " + x);
            Assert.Empty(failures);
        }

        [Fact(Skip = "Roads no longer zone (the user, 26 Oct: the Zoning category zones); RoadPreZone.Offered is false")]
        public void ZoneCellsLieLevelAcrossTheirStreet()
        {
            foreach (var (pattern, kind) in new[] { ("grid", "steep"), ("grid", "hill"), ("organic", "steep"), ("superblock", "valley") })
            {
                var s = Roads(pattern);
                var r = Plan(s, kind);
                var o = TerraceOptions.Read(s, new RoadsLayer().Controls, null);
                Assert.NotEmpty(r.Plan.Zoning);
                Assert.Equal(o.Cells, r.Plan.Facts[RoadPreZone.FactDepth]);
                // (the rule allows 5 cm over 3.5 m on top of its slopes)
                float level = (float)r.Plan.Facts[RoadPreZone.FactLevel] + 0.02f;
                float along = (float)r.Plan.Facts[RoadPreZone.FactAlong] + 0.02f;
                int pts = 0, tilted = 0;
                foreach (var z in r.Plan.Zoning)
                {
                    var q = new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz };
                    float2 c = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
                    float2 u = q[1] - q[0], v = q[3] - q[0];
                    foreach (var corner in q.Append(c))
                    {
                        float d = math.distance(corner, c);
                        var p = d > 4.3f ? corner + (c - corner) * (4.3f / d) : c;
                        pts++;
                        // One axis of the lot lies level (across the street), the other no steeper than the street may climb.
                        float su = SlopeAlong(r.Final, p, u), sv = SlopeAlong(r.Final, p, v);
                        if (math.min(su, sv) > level || math.max(su, sv) > along) tilted++;
                    }
                }
                _out.WriteLine($"{pattern}/{kind}: {r.Plan.Zoning.Count} zoning quads, {pts} points, {tilted} tilted");
                Assert.True(tilted <= pts * 0.03, $"{pattern}/{kind}: {tilted} of {pts} zoned points not level across");
            }
        }

        [Fact]
        public void FlatGroundAndNaturalLandAreLeftAlone()
        {
            var flat = Plan(Roads("grid"), "flat");
            Assert.Empty(flat.Plan.Terraform);
            Assert.Null(flat.Plan.HeightAfter);
            foreach (var kind in Slopes)
            {
                var r = Plan(Roads("grid", (RoadsIds.LandShape, "natural")), kind);
                Assert.Empty(r.Plan.Terraform);
                Assert.Null(r.Plan.HeightAfter);
                Assert.DoesNotContain(r.Plan.Nets, n => n.Category == AssetCategories.NetQuay);
            }
            // Ignore terrain: the strict pattern, no terracing either.
            var strict = Plan(Roads("grid", (RoadsIds.FollowTerrain, false)), "steep");
            Assert.Empty(strict.Plan.Terraform);
        }

        // ───────────────────────── no retaining walls ─────────────────────────

        [Fact]
        public void NoQuaysEvenOnSteepHills()
        {
            // The retaining walls (the game's quays) are gone (the user: "they never work"): a 15 % plane is all banks, also
            // for a design saved with "retaining".
            var r = Plane(Roads("grid", (RoadsIds.GridSpacing, 90.0), (RoadsIds.TerraceBank, "retaining")), 0.15f);
            Assert.NotNull(r.Ground);
            Assert.Empty(r.Ground.Walls);
            Assert.DoesNotContain(r.Plan.Nets, n => n.Category == AssetCategories.NetQuay);
            Assert.NotEmpty(r.Plan.Terraform);
            Assert.Empty(r.Plan.Zoning);   // roads never zone (the user, 26 Oct)
        }

        // The old wall checks, kept for the wall code (no road plan builds walls now): skipped.
        [Fact(Skip = "the retaining walls (quays) are gone from road plans")]
        public void WallsStandWhereTheCutIsDeep()
        {
            // A 15 % plane, streets 90 m apart: the back of every block is cut several metres into the hill.
            var r = Plane(Roads("grid", (RoadsIds.GridSpacing, 90.0)), 0.15f);
            Assert.NotNull(r.Ground);
            var walls = r.Ground.Walls;
            var quays = r.Plan.Nets.Where(n => n.Category == AssetCategories.NetQuay).ToList();
            _out.WriteLine($"walls {walls.Count}, {walls.Sum(w => w.Length):0} m, quays {quays.Count}, facts walls {r.Plan.Facts["blocks.walls"]}");
            Assert.NotEmpty(walls);
            Assert.Equal(walls.Sum(w => w.Line.Length - 1), quays.Count);
            Assert.All(quays, n => { Assert.False(n.FollowTerrain); Assert.Equal(NetRole.PathwayWide, n.Role); });
            var built = Built(r);
            int pts = 0, low = 0, buried = 0;
            foreach (var w in walls)
            {
                Assert.True(w.Length >= BlockField.MinWallLength - 0.1f);
                for (int i = 0; i < w.Line.Length; i++)
                {
                    pts++;
                    // The game raises the quay's low side 2 m or more below its deck: the wall.
                    float below = w.Deck[i] - built(w.Line[i] - w.Up[i] * (BlockField.QuayHalf + 0.25f));
                    // Its uphill side sits on the ground (no wall there, no deck floating over a dip).
                    float above = w.Deck[i] - built(w.Line[i] + w.Up[i] * (BlockField.QuayHalf + 0.25f));
                    if (below < 2.05f) low++;
                    if (math.abs(above) > 1.5f) buried++;
                }
            }
            _out.WriteLine($"{pts} wall points, {low} faces under 2 m, {buried} decks off the upper ground");
            Assert.True(low <= pts * 0.05, $"{low} of {pts} wall points would not raise the quay's side");
            Assert.True(buried <= pts * 0.05, $"{buried} of {pts} decks off the upper ground");
            // The quays keep clear of the zoned lots.
            int clash = 0;
            foreach (var z in r.Plan.Zoning)
            {
                float2 c = (z.A.xz + z.B.xz + z.C.xz + z.D.xz) * 0.25f;
                foreach (var n in quays)
                    if (math.sqrt(Seg.DistSq(c, n.A.xz, n.D.xz, out _)) < BlockField.QuayHalf + 2f) { clash++; break; }
            }
            Assert.True(clash <= r.Plan.Zoning.Count * 0.02, $"{clash} of {r.Plan.Zoning.Count} zone cells under a wall's quay");
        }

        [Fact]
        public void BanksInsteadOfWallsWhenChosen()
        {
            var r = Plane(Roads("grid", (RoadsIds.GridSpacing, 90.0), (RoadsIds.TerraceBank, "bank")), 0.15f);
            Assert.Empty(r.Ground.Walls);
            Assert.DoesNotContain(r.Plan.Nets, n => n.Category == AssetCategories.NetQuay);
            Assert.NotEmpty(r.Plan.Terraform);
        }

        [Fact]
        public void GentleSlopesNeedNoWalls()
        {
            // 6 %: the steps at the back of the lots stay under the wall height, banks take them.
            var r = Plane(Roads("grid", (RoadsIds.GridSpacing, 90.0)), 0.06f);
            Assert.Empty(r.Ground.Walls);
        }
    }
}
