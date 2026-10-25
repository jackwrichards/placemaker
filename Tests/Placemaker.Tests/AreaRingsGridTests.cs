// (perf) AreaRings.FirstViolation checks big rings through a grid of edge boxes; it must return exactly what the full loop
// over every pair returns (the same first violation, or none), on painted-style outlines and on random jagged rings.

using System.Collections.Generic;
using Placemaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class AreaRingsGridTests
    {
        private static List<float2> Jagged(int n, float radius, float wobble, uint seed)
        {
            var rng = new Random(seed);
            var ring = new List<float2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = i * 2f * math.PI / n;
                float r = radius * (1f + rng.NextFloat(-wobble, wobble));
                ring.Add(new float2(math.cos(a), math.sin(a)) * r);
            }
            return ring;
        }

        [Fact]
        public void TheGridFindsTheSameFirstViolationAsTheFullLoop()
        {
            int checkedRings = 0, violating = 0;
            for (uint seed = 1; seed <= 400; seed++)
            {
                var rng = new Random(seed * 7919u);
                int n = rng.NextInt(40, 700);
                var ring = Jagged(n, rng.NextFloat(20f, 300f), rng.NextFloat(0f, 0.08f), seed);
                foreach (float d in new[] { 0.1f, 0.375f, 1.5f, 4f })
                {
                    int expected = AreaRings.FirstViolationLoop(ring, d);
                    Assert.Equal(expected, AreaRings.FirstViolation(ring, d));
                    checkedRings++;
                    if (expected >= 0) violating++;
                }
            }
            Assert.True(violating > 100 && violating < checkedRings - 100, $"{violating} of {checkedRings} rings violate: the test must see both cases");
        }

        [Fact]
        public void MakeValidIsUnchangedOnBigRings()
        {
            for (uint seed = 1; seed <= 60; seed++)
            {
                var ring = Jagged(500, 150f, 0.05f, seed);
                var made = AreaRings.MakeValid(ring, 0.375f);
                Assert.True(made == null || AreaRings.FirstViolationLoop(made, 0.375f + AreaRings.Margin) < 0);
            }
        }
    }
}
