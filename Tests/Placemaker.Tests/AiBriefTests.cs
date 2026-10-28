// AI builder (docs/AI-BUILDER.md): the site brief and its map for an odd shape (a C), a slope, a road and a building.

using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Ai;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class AiBriefTests
    {
        private readonly ITestOutputHelper _out;
        public AiBriefTests(ITestOutputHelper output) { _out = output; }

        private static SiteBriefInput CShape()
        {
            // a C opening east, at world (1000, 2000): 100 x 80 m, the gap 40 m wide and 60 m deep
            var o = new float2(1000, 2000);
            var ring = new[] { new float2(0, 0), new float2(100, 0), new float2(100, 20), new float2(40, 20), new float2(40, 60), new float2(100, 60), new float2(100, 80), new float2(0, 80) }
                .Select(p => p + o).ToArray();
            var (origin, cell, nx, nz) = SiteBrief.GroundGrid(ring, 30f);
            var g = new BriefGround { Origin = origin, Cell = cell, Nx = nx, Nz = nz, Height = new float[nx * nz] };
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++) g.Height[j * nx + i] = 400f + 0.05f * (g.CellCentre(i, j).x - o.x);   // rises 5 % to the east
            var s = new SiteBriefInput { Ring = ring, Ground = g, Prompt = "a park" };
            s.Nets.Add(new NearbyNet { A = new float3(o.x - 20, 400, o.y - 10), B = new float3(o.x + 30, 400, o.y - 10), C = new float3(o.x + 80, 405, o.y - 10), D = new float3(o.x + 130, 406, o.y - 10), Width = 12, Category = NetCategory.Road, HasSidewalk = true });
            s.Buildings.Add(new Obstacle { A = o + new float2(60, 30), B = o + new float2(80, 30), C = o + new float2(80, 50), D = o + new float2(60, 50), Kind = ObstacleKind.Building });
            return s;
        }

        [Fact]
        public void BriefDescribesTheShapeGroundRoadAndBuilding()
        {
            var s = CShape();
            var text = SiteBrief.Write(s);
            _out.WriteLine(text.Substring(0, System.Math.Min(text.Length, 3000)));
            Assert.Contains("Bounding box 100 x 80 m, 8 corners, area 5600 m²", text);
            Assert.Contains("(40,20)", text);   // outline in local metres
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            int mask = lines.IndexOf("Mask (# = inside the selection, . = outside):");
            Assert.True(mask > 0);
            var rows = lines.Skip(mask + 1).Take(s.Ground.Nz).ToList();
            Assert.All(rows, r => Assert.Equal(s.Ground.Nx, r.Length));
            Assert.Contains(rows, r => r.Contains('#'));
            Assert.Contains("road, 12 m wide, sidewalks, outside", text);
            Assert.Contains("B1: 20 x 20 m centred at (70,40)", text);   // in the C's gap: outside
            Assert.Contains("outside;", lines.First(l => l.StartsWith("B1:")));
            Assert.Contains("steepest 5 %", text);
            Assert.Contains("a park", text);
        }

        [Fact]
        public void MapCoversTheGroundGrid()
        {
            var s = CShape();
            var pic = BriefMap.Draw(s);
            float size = math.max(s.Ground.Nx, s.Ground.Nz) * s.Ground.Cell;
            Assert.True(pic.W <= 1000 && pic.H <= 1000);
            Assert.Equal(pic.W * pic.H * 4, pic.Rgba.Length);
            Assert.InRange(pic.MetresPerPixel, 0.25f, size / 100f);
            // the outline is drawn white somewhere
            bool white = false;
            for (int k = 0; k < pic.Rgba.Length; k += 4) white |= pic.Rgba[k] == 255 && pic.Rgba[k + 1] == 255 && pic.Rgba[k + 2] == 255;
            Assert.True(white);
        }

        [Fact]
        public void AlongSpacesStationsEvenlyAndYawPointsTheWay()
        {
            // a 40 m line east: stations 2 m in from each end, spaced evenly (36 m / 9 m = 4 gaps, 5 stations)
            var st = AiShapes.Along(new[] { new float2(0, 0), new float2(40, 0) }, false, 9f, 2f);
            Assert.Equal(5, st.Count);
            Assert.Equal(2f, st[0].at.x, 3);
            Assert.Equal(38f, st[4].at.x, 3);
            Assert.Equal(90f, math.degrees(AiShapes.YawOf(st[0].dir)), 3);   // east = yaw 90
            // a closed square, 10 m sides, every 5 m: 8 stations round it
            Assert.Equal(8, AiShapes.Along(AiShapes.Rect(new float2(0, 0), new float2(10, 10)), true, 5f, 0f).Count);
        }

        [Fact]
        public void FeatheredShapeBlendsBackAndLineFollowsItsHeights()
        {
            // a bay: lowered 3 m in a 20 m circle, feather 10 m, on flat ground at 100 m
            var bay = new AiPiece { Kind = AiPieceKind.Ground, Ring = AiShapes.Circle(new float2(0, 0), 20f), Height = 97f, Shape = TerraformKind.Lower, Feather = 10f };
            Assert.Equal(97f, AiTerrain.HeightAt(bay, new float2(0, 0), 100f), 3);
            float mid = AiTerrain.HeightAt(bay, new float2(25, 0), 100f);
            Assert.InRange(mid, 97.5f, 99.5f);                                   // half way out the feather: half way up
            Assert.Equal(100f, AiTerrain.HeightAt(bay, new float2(31, 0), 100f), 3);   // past it: untouched
            var cells = AiTerrain.Cells(bay, _ => 100f, out var deepest);
            Assert.InRange(cells.Count, 100, 600);
            Assert.True(deepest.TargetHeight < 97.1f);
            Assert.InRange(AiTerrain.SteepestEdge(bay, _ => 100f), 20f, 60f);  // a 3 m drop over 10 m: about 30-45 %
            // a channel falling from 100 to 98 along 40 m, 6 m wide
            var ch = new AiPiece { Kind = AiPieceKind.Ground, LinePoints = new[] { new float2(0, 0), new float2(40, 0) }, LineWidth = 6f, LineHeights = new[] { 100f, 98f }, Shape = TerraformKind.Lower, Feather = 4f };
            ch.Ring = AiTerrain.LineRing(ch.LinePoints, ch.LineWidth);
            Assert.Equal(99f, AiTerrain.HeightAt(ch, new float2(20, 2), 101f), 2);
            Assert.Equal(101f, AiTerrain.HeightAt(ch, new float2(20, 8), 101f), 2);
        }
        [Fact]
        public void PathMeetingAnotherAtAShallowAngleArrivesSquareOn()
        {
            // the Opus park (27 Oct): a path joining the lake loop at about 60 degrees drew a notch of paving
            var loop = AiShapes.Smooth(new[] { new float2(106,95), new float2(110,120), new float2(128,137), new float2(160,140), new float2(190,132), new float2(208,112), new float2(211,86), new float2(202,62), new float2(182,48), new float2(150,45), new float2(124,53), new float2(107,72), new float2(106,95) }, 5f);
            var spur = AiShapes.Smooth(new[] { new float2(46,136), new float2(62,150), new float2(88,156), new float2(110,150), new float2(128,137) }, 5f);
            var others = new System.Collections.Generic.List<AiPiece> { new AiPiece { Id = 9, Kind = AiPieceKind.Path, Points = loop, NetRole = Placemaker.Core.NetRole.PathwayWide } };
            var outp = Placemaker.Core.Layers.Ai.AiLayer.Approach(spur, others);
            var end = outp[outp.Length - 1]; var before = outp[outp.Length - 2];
            _out.WriteLine(string.Join(" ", outp.Skip(outp.Length - 4).Select(q => $"({q.x:F1},{q.y:F1})")));
            // the end lies on the loop, and the last stretch meets it at 75 degrees or more
            float bestD = float.MaxValue; float2 dir = default;
            for (int k = 0; k + 1 < loop.Length; k++) { var a = loop[k]; var ab = loop[k + 1] - a; float tt = math.saturate(math.dot(end - a, ab) / math.lengthsq(ab)); float d = math.distance(a + ab * tt, end); if (d < bestD) { bestD = d; dir = math.normalize(ab); } }
            Assert.True(bestD < 0.01f);
            Assert.True(math.abs(math.dot(math.normalize(end - before), dir)) < 0.27f);
            Assert.Equal(spur[0], outp[0]);
        }
    }
}
