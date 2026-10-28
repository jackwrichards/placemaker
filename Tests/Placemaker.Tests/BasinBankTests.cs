// A basin on sloping ground (27 Oct): its water level is set by the lowest bank, so the uphill side was cut straight down
// to the water, a wall many metres high. Layers/Common/Shore/BasinBank lays a 1 : 4 bank there instead.

using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common.Shore;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class BasinBankTests
    {
        private static readonly float2[] Pond = { new float2(0, 0), new float2(30, 0), new float2(30, 30), new float2(0, 30) };

        [Fact]
        public void TheUphillSideGetsABankNotAWall()
        {
            System.Func<float2, float> ground = p => 0.15f * p.x;   // 4.5 m up across the pond
            float level = -0.5f;                                      // under the lowest bank
            var quads = BasinBank.Quads(Pond, level, ground);
            Assert.NotEmpty(quads);
            foreach (var q in quads)
            {
                Assert.True(q.Sloped && q.Permanent && q.Kind == TerraformKind.Lower);
                // inner edge just above the water, outer edge on the ground, no steeper than the grade
                Assert.Equal(level + BasinBank.Rim, q.Ring[0].y, 3);
                Assert.Equal(ground(q.Ring[3].xz), q.Ring[3].y, 3);
                float run = math.distance(q.Ring[0].xz, q.Ring[3].xz), rise = q.Ring[3].y - q.Ring[0].y;
                if (run < BasinBank.MaxWidth - 0.1f) Assert.True(rise <= BasinBank.Grade * run + 0.6f, $"rise {rise:F1} m over {run:F1} m");
            }
            // the low (west) side keeps its own shore
            Assert.DoesNotContain(quads, q => q.Ring.Take(2).All(v => v.x < 1f));
        }

        [Fact]
        public void LevelGroundKeepsItsShore()
        {
            Assert.Empty(BasinBank.Quads(Pond, -0.5f, _ => 0f));
        }
    }
}
