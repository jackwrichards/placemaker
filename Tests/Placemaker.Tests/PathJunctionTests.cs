// Spiky path junctions (27 Oct): the game draws a path joining another at a slanting angle as a long spike of paving.
// The joining end bends in over its last metres and arrives square-on, exactly on the other path (Layers/Common/PathJoins.Square,
// the premade types' version of the AI builder's AiLayer.Approach).

using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class PathJunctionTests
    {
        private static PlannedNet Line(float2 a, float2 b) => new PlannedNet
        {
            A = new float3(a.x, 0f, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, 0f, math.lerp(a, b, 1f / 3f).y),
            C = new float3(math.lerp(a, b, 2f / 3f).x, 0f, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, 0f, b.y),
            Role = NetRole.Pathway,
        };

        /// <summary>Direction the net leaves the point p (one of its ends) over its first 3 m.</summary>
        private static float2 Leaving(PlannedNet n, float2 p)
        {
            bool start = math.distance(n.A.xz, p) < 0.01f;
            float t = 3f / (float)Pricing.CurveLength(n);
            return math.normalize(Composer.Bezier(n.A, n.B, n.C, n.D, start ? t : 1f - t).xz - p);
        }

        private static float Deg(float2 a, float2 b) => math.degrees(math.acos(math.clamp(math.dot(a, b), -1f, 1f)));

        [Fact]
        public void ASlantingSpurArrivesSquareOnExactlyOnThePath()
        {
            var host = Line(new float2(0, 0), new float2(40, 0));
            var spur = Line(new float2(-10, 12), new float2(20, 0.4f));   // 22° to the path, its end a little off it
            var nets = new List<PlannedNet> { host, spur };
            Assert.Equal(1, PathJoins.JoinAll(nets));
            Assert.Equal(1, PathJoins.Square(nets));
            Assert.Equal(3, nets.Count);
            var node = spur.D.xz;
            Assert.Equal(host.D.x, spur.D.x); Assert.Equal(host.D.z, spur.D.z);   // the node stays shared, bit for bit
            Assert.Equal(nets[2].A.x, spur.D.x); Assert.Equal(nets[2].A.z, spur.D.z);
            var arm = Leaving(spur, node);
            Assert.True(Deg(arm, Leaving(host, node)) >= PathJoins.SquareAngle, $"{Deg(arm, Leaving(host, node)):F0}°");
            Assert.True(Deg(arm, Leaving(nets[2], node)) >= PathJoins.SquareAngle, $"{Deg(arm, Leaving(nets[2], node)):F0}°");
            // the path running through is left as it was
            Assert.True(Deg(Leaving(host, node), Leaving(nets[2], node)) > 179f);
        }

        [Fact]
        public void SquareJunctionsAndContinuationsAreLeftAlone()
        {
            var host = Line(new float2(0, 0), new float2(40, 0));
            var spur = Line(new float2(18, 25), new float2(20, 0));   // about 85°
            var on = Line(new float2(40, 0), new float2(70, 8));       // carries the path on (end to end)
            var nets = new List<PlannedNet> { host, spur, on };
            PathJoins.JoinAll(nets);
            var before = new List<float3>();
            foreach (var n in nets) { before.Add(n.B); before.Add(n.C); }
            Assert.Equal(0, PathJoins.Square(nets));
            int i = 0;
            foreach (var n in nets) { Assert.Equal(before[i++], n.B); Assert.Equal(before[i++], n.C); }
        }

        [Fact]
        public void DecksAndStreetEndsNeverBend()
        {
            var host = Line(new float2(0, 0), new float2(40, 0));
            var deck = Line(new float2(-10, 12), new float2(20, 0));
            deck.FollowTerrain = false;
            var nets = new List<PlannedNet> { host, deck };
            PathJoins.JoinAll(nets);
            Assert.Equal(0, PathJoins.Square(nets));
        }
    }
}
