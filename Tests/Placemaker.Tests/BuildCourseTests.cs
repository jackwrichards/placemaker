using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class BuildCourseTests
    {
        private static void AssertContinuous(List<Cubic> pieces, Cubic original)
        {
            Assert.NotEmpty(pieces);
            Assert.Equal(original.A.xz, pieces[0].A.xz);
            Assert.Equal(original.D.xz, pieces[pieces.Count - 1].D.xz);
            for (int i = 1; i < pieces.Count; i++)
                Assert.True(math.distance(pieces[i - 1].D, pieces[i].A) < 1e-4f, "pieces must join exactly");
        }


        [Fact]
        public void ABridgeEndOnHigherGroundRisesToIt()
        {
            // a footbridge from the bank (deck 0.8 m) to an island raised to 1.5 m: its island end stood 0.7 m under the
            // ground and the game dug a notch there
            System.Func<float2, float> ground = p => p.x > 28f ? 1.5f : (p.x < 2f ? 0.5f : -3f);
            var deck = new Cubic(new float3(0, 0.8f, 0), new float3(10, 2.4f, 0), new float3(20, 2.4f, 0), new float3(30, 0.8f, 0));
            var landed = CourseFitting.LandEnds(deck, ground);
            Assert.Equal(0.8f, landed.A.y, 4);   // the bank end stays above its ground
            Assert.Equal(1.5f, landed.D.y, 4);   // the island end on it
            Assert.True(landed.Position(0.5f).y > deck.Position(0.5f).y);   // the arch keeps its shape, lifted a little
            var level = CourseFitting.LandEnds(deck, _ => -3f);
            Assert.Equal(deck.A, level.A); Assert.Equal(deck.D, level.D);   // over water: as planned
        }

        [Fact]
        public void BridgeKeepsPlannedDeckAndIsNeverPinnedToTheBed()
        {
            // Arched footbridge 50 m over a dry basin 4 m deep (water comes after the build).
            System.Func<float2, float> basin = p => p.x > 5f && p.x < 45f ? -4f : 0f;
            var deck = new Cubic(new float3(0, 0, 0), new float3(16.7f, 1.6f, 0), new float3(33.3f, 1.6f, 0), new float3(50, 0, 0));
            var s = CourseFitSettings.Default; s.FollowTerrain = false; s.MaxLength = 200f;
            var one = CourseFitting.Fit(deck, basin, s);
            Assert.Single(one);   // never split for terrain, never pinned
            Assert.Equal(deck.Position(0.5f).y, one[0].Position(0.5f).y, 4);
            var pieces = CourseFitting.FitElevated(deck, 12f);
            Assert.True(pieces.Count >= 5);
            AssertContinuous(pieces, deck);
            float t = 0f;
            foreach (var piece in pieces)
            {
                Assert.True(piece.Length() <= 12.5f);
                // Every node lies on the planned deck, well above the bed.
                Assert.True(piece.D.y > -0.01f);
                Assert.True(CourseFitting.TerrainDeviation(piece, p => deck.Position(math.saturate(p.x / 50f)).y, out _) < 0.1f);
                t += piece.Length();
            }
            Assert.Equal(deck.Length(), t, 0);
        }

        [Fact]
        public void InnerProfileFollowsGroundEvenWhenPlanHeightsAreStale()
        {
            // Plan heights from the ground BEFORE terraform (flat 0); the real ground is a bowl.
            System.Func<float2, float> bowl = p => 2f + 0.02f * (p.x - 6f) * (p.x - 6f);
            var c = Cubic.Straight(new float3(0, 0, 0), new float3(12, 0, 0));
            var r = CourseFitting.Fit(c, bowl, CourseFitSettings.Default);
            Assert.Single(r);   // a quadratic profile fits one cubic exactly: no split needed
            Assert.True(CourseFitting.TerrainDeviation(r[0], bowl, out _) < 0.01f);
        }

        [Fact]
        public void ShortBumpyPiecesAreSplitToo()
        {
            System.Func<float2, float> bumps = p => 1.5f * math.sin(p.x / 3f);
            var c = Cubic.Straight(new float3(0, 0, 0), new float3(40, 0, 0));
            var s = CourseFitSettings.Default;
            var r = CourseFitting.Fit(c, bumps, s);
            AssertContinuous(r, c);
            foreach (var piece in r)
                if (piece.Length() >= s.MinSplitLength)
                    Assert.True(CourseFitting.TerrainDeviation(piece, bumps, out _) <= s.MaxTerrainDeviation + 1e-3f);
        }

        [Fact]
        public void MaxLengthPiecesAreTerrainFittedAsWell()
        {
            System.Func<float2, float> slope = p => 0.001f * p.x * p.x;
            var c = Cubic.Straight(new float3(0, 0, 0), new float3(300, 0, 0));
            var s = CourseFitSettings.Default; s.MaxLength = 100;
            var r = CourseFitting.Fit(c, slope, s);
            AssertContinuous(r, c);
            foreach (var piece in r) Assert.True(CourseFitting.TerrainDeviation(piece, slope, out _) <= s.MaxTerrainDeviation + 1e-3f);
        }
        [Fact]
        public void FlatStraightCourseStaysOnePiece()
        {
            var c = Cubic.Straight(new float3(0, 0, 0), new float3(100, 0, 0));
            var r = CourseFitting.Fit(c, _ => 0f, CourseFitSettings.Default);
            Assert.Single(r);
            AssertContinuous(r, c);
        }

        [Fact]
        public void EndpointsArePinnedToTerrain()
        {
            var c = Cubic.Straight(new float3(0, 50, 0), new float3(10, 50, 0));
            var r = CourseFitting.Fit(c, p => 3f, CourseFitSettings.Default);
            Assert.Single(r);
            Assert.Equal(3f, r[0].A.y, 3);
            Assert.Equal(3f, r[0].D.y, 3);
            Assert.Equal(3f, r[0].Position(0.5f).y, 3);
        }

        [Fact]
        public void HillSplitsUntilCurveHugsGround()
        {
            System.Func<float2, float> hill = p => 8f * math.exp(-math.lengthsq(p - new float2(60, 0)) / 400f);
            var c = Cubic.Straight(new float3(0, 0, 0), new float3(120, 0, 0));
            var s = CourseFitSettings.Default;
            var r = CourseFitting.Fit(c, hill, s);
            Assert.True(r.Count > 1);
            AssertContinuous(r, c);
            foreach (var piece in r)
            {
                Assert.Equal(hill(piece.A.xz), piece.A.y, 3);
                if (piece.Length() >= s.MinSplitLength)
                    Assert.True(CourseFitting.TerrainDeviation(piece, hill, out _) <= s.MaxTerrainDeviation + 1e-3f);
            }
        }

        [Fact]
        public void KeepsPlannedHeightsWhenNotFollowingTerrain()
        {
            var c = Cubic.Straight(new float3(0, 10, 0), new float3(100, 20, 0));
            var s = CourseFitSettings.Default; s.FollowTerrain = false;
            var r = CourseFitting.Fit(c, _ => 0f, s);
            Assert.Single(r);
            Assert.Equal(10f, r[0].A.y);
            Assert.Equal(20f, r[0].D.y);
        }

        [Fact]
        public void LongCoursesAreSplitByMaxLength()
        {
            var c = Cubic.Straight(new float3(0, 0, 0), new float3(500, 0, 0));
            var s = CourseFitSettings.Default; s.MaxLength = 200;
            var r = CourseFitting.Fit(c, _ => 0f, s);
            Assert.Equal(3, r.Count);
            AssertContinuous(r, c);
            foreach (var p in r) Assert.True(p.Length() <= 200.5f);
        }

        [Fact]
        public void NonFiniteInputYieldsNothing()
        {
            var c = new Cubic(new float3(float.NaN, 0, 0), 0, 0, new float3(1, 0, 0));
            Assert.Empty(CourseFitting.Fit(c, _ => 0f, CourseFitSettings.Default));
        }

        [Fact]
        public void SplitHalvesTraceOriginal()
        {
            var c = new Cubic(new float3(0, 0, 0), new float3(10, 0, 30), new float3(40, 0, 30), new float3(50, 0, 0));
            c.Split(0.3f, out var l, out var r);
            Assert.True(math.distance(l.Position(0.5f), c.Position(0.15f)) < 1e-3f);
            Assert.True(math.distance(r.Position(0.5f), c.Position(0.65f)) < 1e-3f);
        }

        [Fact]
        public void DistanceXZFindsParameter()
        {
            var c = Cubic.Straight(new float3(0, 0, 0), new float3(100, 0, 0));
            float d = c.DistanceXZ(new float2(25, 3), out float t);
            Assert.Equal(3f, d, 2);
            Assert.Equal(0.25f, t, 2);
        }

        [Fact]
        public void NetCostChargesPerStartedEightMetreCell()
        {
            Assert.Equal(1 * 100.0, CourseFitting.NetCost(2f, 100));
            Assert.Equal(13 * 100.0, CourseFitting.NetCost(104f, 100));
        }

        [Fact]
        public void PlanIndexMatchesOnlySamePrefabAndNearGeometry()
        {
            var idx = new PlanIndex();
            idx.AddCurve(1, 100, Cubic.Straight(new float3(0, 0, 0), new float3(100, 0, 0)));
            idx.AddPoint(2, 200, new float2(50, 20));
            Assert.True(idx.TryMatch(new float2(50, 0.5f), 100, 1f, out var k, out _));
            Assert.Equal(1, k);
            Assert.False(idx.TryMatch(new float2(50, 0.5f), 999, 1f, out _, out _));   // other prefab = foreign road piece
            Assert.False(idx.TryMatch(new float2(50, 5f), 100, 1f, out _, out _));
            Assert.True(idx.TryMatch(new float2(50.5f, 20), 0, 1f, out k, out _));
            Assert.Equal(2, k);
            Assert.False(idx.TryMatch(new float2(-500, -500), 0, 2f, out _, out _));
        }
    }
}
