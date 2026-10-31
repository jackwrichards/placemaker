// rf-parking (2026-10-16): the parking lot's planting is tidy. In the game the old jittered fill put a 10 m crown every
// 4 m anywhere in a 2.5 m median (the catalog's plant bounds are the collision core): clumps over the aisles and stalls.
// Now the strips behind the stall rows are planted in lines (Vegetation.PlanRows): trees evenly spaced on the strip's
// centre line at their crown size, shrubs between them. Bounds below are the game's (Fixtures/catalog-bounds.json).
// PM_PLANT_SVG=<folder> writes a plan drawing per case (green, asphalt, stalls, lamps, trees, shrubs).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class ParkingPlantingTests
    {
        private readonly ITestOutputHelper _out;
        public ParkingPlantingTests(ITestOutputHelper o) { _out = o; }

        private static SiteContext GameSite()
        {
            var site = new SiteContext();
            site.Assets["NA_LondonPlaneTree01"] = new AssetInfo { Id = "NA_LondonPlaneTree01", BoundsMin = new float3(-1.14f, 0, -1.26f), BoundsMax = new float3(1.14f, 7.3f, 1.26f) };
            site.Assets["GreenBushWild02"] = new AssetInfo { Id = "GreenBushWild02", BoundsMin = new float3(-0.92f, 0, -1.11f), BoundsMax = new float3(0.92f, 3.99f, 1.11f) };
            site.Assets["FlowerBushWild01"] = new AssetInfo { Id = "FlowerBushWild01", BoundsMin = new float3(-0.81f, 0, -0.79f), BoundsMax = new float3(0.81f, 1.38f, 0.79f) };
            return site;
        }

        private static ParkingRun Build(string preset, float3[] ring, int edge = 0, double along = 64)
        {
            var gen = new ParkingLayer();
            var bag = gen.Defaults();
            if (preset != null)
                foreach (var kv in gen.Presets.First(p => p.Id == preset).Values.Values) bag.Values[kv.Key] = kv.Value;
            return ParkingKit.Run(ring, bag, new List<EdgeMarker> { ParkingKit.Marker(ring, edge, along) }, GameSite());
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var p in new[] { "standard", "compact", "green", "angled" })
            {
                yield return new object[] { p, "block" };
                yield return new object[] { p, "L" };
                yield return new object[] { p, "small" };
            }
        }

        private static float3[] Shape(string shape) => shape switch
        {
            "L" => ParkingKit.Ring(0, 0, 160, 0, 160, 60, 70, 60, 70, 120, 0, 120),
            "small" => ParkingKit.Rect(62, 38),
            _ => ParkingKit.Rect(128, 118),
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void PlantingIsTidy(string preset, string shape)
        {
            var ring = Shape(shape);
            var r = Build(preset, ring, 0, shape == "small" ? 30 : 50);
            var l = r.Layout;
            Assert.NotNull(l);
            Dump(l, preset + "-" + shape);
            var trees = l.Plants.Where(p => p.Tree).ToList();
            _out.WriteLine($"{preset}/{shape}: {r.Stalls} stalls, {trees.Count} trees ({trees.Count(t => t.Row)} in lines), {l.Plants.Count(p => !p.Tree)} shrubs");

            // nothing on the asphalt or in a stall
            foreach (var p in l.Plants)
            {
                Assert.False(l.Surfaces.Any(s => s.Asphalt && PGeo.Contains(s.Ring, p.Position)), $"plant on asphalt at {p.Position}");
                Assert.False(l.LiveBays.Any(b => PGeo.Contains(b.Quad, p.Position)), $"{(p.Tree ? "tree" : "shrub")} (line {p.Row}) in a stall at {p.Position}, in green {l.Surfaces.Count(s => !s.Asphalt && PGeo.Contains(s.Ring, p.Position))}");
            }
            // trees never crowd: no two trunks closer than ~0.85 of the tree spacing (7 m for the London plane at 50 %)
            double minTree = double.MaxValue;
            for (int i = 0; i < trees.Count; i++)
                for (int j = i + 1; j < trees.Count; j++)
                    minTree = Math.Min(minTree, math.distance(trees[i].Position, trees[j].Position));
            if (trees.Count > 1) Assert.True(minTree >= 5.0, $"two trees {minTree:F1} m apart");
            // a lot with planting strips gets trees, and they stand in lines
            if (shape != "small") Assert.True(trees.Count(t => t.Row) >= 8, "too few trees in planting lines");
        }

        [Fact]
        public void LineTreesShareARhythm()
        {
            // standard 128 x 118: every median line's trees are evenly spaced (one multiple of the stall pitch)
            var r = Build("standard", Shape("block"));
            var row = r.Layout.Plants.Where(p => p.Tree && p.Row).ToList();
            var byLine = row.GroupBy(p => Math.Round(p.Position.y, 1)).Where(g => g.Count() >= 2);
            var all = new List<double>();
            int lines = 0;
            foreach (var g in byLine)
            {
                var xs = g.Select(p => p.Position.x).OrderBy(x => x).ToList();
                all.AddRange(xs.Zip(xs.Skip(1), (a, b) => b - a));
                lines++;
            }
            Assert.True(lines >= 4, "expected several planted median lines");
            // within a run the gap is one fixed step (twice it where a lamp took a spot); only the jumps between runs differ
            all.Sort();
            double usual = all[0];
            var near = all.Where(d => d < 20).ToList();
            int even = near.Count(d => Math.Abs(d - usual * Math.Round(d / usual)) < 0.6);
            _out.WriteLine($"usual gap {usual:F1} m: {even} of {near.Count} gaps under 20 m");
            Assert.True(usual >= 5.9, $"trees {usual:F1} m apart");   // P2-5: denser lines; two stalls (6 m) is the closest a crown allows
            Assert.True(even == near.Count, $"gaps {string.Join(", ", all.Select(x => x.ToString("F1")))}");
        }

        [Fact]
        public void DensitySetsTheSpacingNotGaps()
        {
            var ring = Shape("block");
            var gen = new ParkingLayer();
            int Trees(double density)
            {
                var bag = gen.Defaults();
                bag.Values[ParkingIds.PlantingDensity] = density;
                var r = ParkingKit.Run(ring, bag, new List<EdgeMarker> { ParkingKit.Marker(ring, 0, 50) }, GameSite());
                return r.Layout.Plants.Count(p => p.Tree && p.Row);
            }
            int sparse = Trees(20), normal = Trees(50), dense = Trees(100);
            _out.WriteLine($"line trees at 20/50/100 %: {sparse}/{normal}/{dense}");
            // (P2-5) the lines are denser: by 50 % the test species stands every second stall, the closest the crown allows, so 100 % may equal it
            Assert.True(sparse < normal && normal <= dense, $"{sparse} / {normal} / {dense}");
            Assert.Equal(0, Trees(0));
        }

        private static void Dump(Layout l, string name)
        {
            var dir = Environment.GetEnvironmentVariable("PM_PLANT_SVG");
            if (string.IsNullOrEmpty(dir)) return;
            PGeo.Bounds(l.Lot, out var mn, out var mx);
            var sb = new StringBuilder();
            double w = mx.x - mn.x + 20, h = mx.y - mn.y + 20;
            sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='{mn.x - 10:F1} {-mx.y - 10:F1} {w:F1} {h:F1}' width='1400' height='{h * 1400 / w:F0}'><g transform='scale(1,-1)'>");
            foreach (var sp in l.Surfaces.OrderBy(s => s.Asphalt ? 1 : 0))
                sb.Append($"<polygon points='{string.Join(" ", sp.Ring.Select(p => $"{p.x:F2},{p.y:F2}"))}' fill='{(sp.Asphalt ? "#888" : "#7a5")}' fill-rule='evenodd'/>");
            foreach (var b in l.LiveBays)
                sb.Append($"<polygon points='{string.Join(" ", b.Quad.Select(p => $"{p.x:F2},{p.y:F2}"))}' fill='none' stroke='#fff' stroke-width='0.1'/>");
            foreach (var la in l.Lanterns) sb.Append($"<circle cx='{la.Position.x:F2}' cy='{la.Position.y:F2}' r='0.5' fill='yellow'/>");
            foreach (var p in l.Plants)
                sb.Append(p.Tree
                    ? $"<circle cx='{p.Position.x:F2}' cy='{p.Position.y:F2}' r='3.6' fill='{(p.Row ? "#063" : "#036")}' fill-opacity='0.6'/><circle cx='{p.Position.x:F2}' cy='{p.Position.y:F2}' r='0.4' fill='#320'/>"
                    : $"<circle cx='{p.Position.x:F2}' cy='{p.Position.y:F2}' r='0.9' fill='{(p.Row ? "#3a3" : "#3cc")}'/>");
            sb.Append("</g></svg>");
            File.WriteAllText(Path.Combine(dir, "plant-" + name + ".svg"), sb.ToString());
        }
    }
}
