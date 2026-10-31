using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class BuildRemovalTests
    {
        /// <summary>Simulates the game: applies passes until done, checking the engine invariants each pass.</summary>
        private static int RunToCompletion(List<RemovalMember> members, Dictionary<long, List<long>> nodeEdges, int max = 32)
        {
            int passes = 0;
            while (passes < 1000)
            {
                var pass = RemovalPlanner.NextPass(members, nodeEdges, max, max);
                passes++;
                Assert.True(pass.Delete.Count <= max || pass.Delete.Count <= 3, "pass too large");
                var deleted = new HashSet<long>(pass.Delete);
                // Invariant: a deleted node has no surviving edge (member or foreign).
                foreach (var id in pass.Delete)
                {
                    var m = members.First(x => x.Id == id);
                    if (m.Kind != MemberKind.Node) continue;
                    foreach (var e in nodeEdges[id]) Assert.Contains(e, deleted);
                }
                // Invariant: objects go before any edge.
                bool anyObjectsLeft = members.Any(x => (x.Kind == MemberKind.Object || x.Kind == MemberKind.Area) && !deleted.Contains(x.Id));
                if (anyObjectsLeft) Assert.DoesNotContain(members.Where(x => x.Kind == MemberKind.Edge), x => deleted.Contains(x.Id));

                members.RemoveAll(x => deleted.Contains(x.Id) || pass.Release.Contains(x.Id));
                foreach (var list in nodeEdges.Values) list.RemoveAll(deleted.Contains);
                if (pass.Final) return passes;
            }
            Assert.Fail("never finished");
            return passes;
        }

        [Fact]
        public void ObjectsGoInLargeBatchesEdgesStayBounded()
        {
            // p1-bulldoze: 5000 objects and 100 edges in a chain; objects in two passes, edges at most 32 per pass.
            var members = new List<RemovalMember>();
            var graph = new Dictionary<long, List<long>>();
            for (int i = 0; i < 5000; i++) members.Add(new RemovalMember { Id = 10000 + i, Kind = MemberKind.Object });
            for (int n = 0; n <= 100; n++) { members.Add(new RemovalMember { Id = 1 + n, Kind = MemberKind.Node }); graph[1 + n] = new List<long>(); }
            for (int e = 0; e < 100; e++)
            {
                members.Add(new RemovalMember { Id = 500 + e, Kind = MemberKind.Edge, StartNode = 1 + e, EndNode = 2 + e });
                graph[1 + e].Add(500 + e); graph[2 + e].Add(500 + e);
            }
            var first = RemovalPlanner.NextPass(members, graph);
            Assert.Equal(RemovalPlanner.MaxObjectsPerPass, first.Delete.Count);
            Assert.All(first.Delete, id => Assert.True(id >= 10000));
            members.RemoveAll(x => first.Delete.Contains(x.Id));
            var second = RemovalPlanner.NextPass(members, graph);
            Assert.Equal(5000 - RemovalPlanner.MaxObjectsPerPass, second.Delete.Count);
            members.RemoveAll(x => second.Delete.Contains(x.Id));
            var third = RemovalPlanner.NextPass(members, graph);
            Assert.InRange(third.Delete.Count, 1, RemovalPlanner.MaxPerPass);
            Assert.Contains(third.Delete, id => id >= 500 && id < 600);
        }

        [Fact]
        public void ChainIsRemovedWithNodesAndForeignNodeIsReleased()
        {
            // n1 -e1- n2 -e2- n3, n3 also has foreign edge f9 (a city road).
            var members = new List<RemovalMember>
            {
                new RemovalMember { Id = 1, Kind = MemberKind.Node },
                new RemovalMember { Id = 2, Kind = MemberKind.Node },
                new RemovalMember { Id = 3, Kind = MemberKind.Node },
                new RemovalMember { Id = 11, Kind = MemberKind.Edge, StartNode = 1, EndNode = 2 },
                new RemovalMember { Id = 12, Kind = MemberKind.Edge, StartNode = 2, EndNode = 3 },
                new RemovalMember { Id = 50, Kind = MemberKind.Object },
                new RemovalMember { Id = 60, Kind = MemberKind.Auxiliary },
            };
            var graph = new Dictionary<long, List<long>>
            {
                [1] = new List<long> { 11 }, [2] = new List<long> { 11, 12 }, [3] = new List<long> { 12, 99 },
            };
            var first = RemovalPlanner.NextPass(members, graph);
            Assert.Equal(new long[] { 50 }, first.Delete);   // objects first

            var released = new List<long>();
            var all = new List<RemovalMember>(members);
            int passes = 0;
            while (true)
            {
                var p = RemovalPlanner.NextPass(all, graph);
                released.AddRange(p.Release);
                all.RemoveAll(x => p.Delete.Contains(x.Id) || p.Release.Contains(x.Id));
                foreach (var l in graph.Values) l.RemoveAll(p.Delete.Contains);
                if (p.Final || ++passes > 20) break;
            }
            Assert.Contains(3L, released);
            Assert.Empty(all);
            Assert.Contains(99L, graph[3]);   // foreign edge untouched
        }

        [Fact]
        public void LargeGridRespectsBudgetAndInvariants()
        {
            // 20x20 grid of nodes with edges between neighbours, plus 100 objects.
            var members = new List<RemovalMember>();
            var graph = new Dictionary<long, List<long>>();
            long NodeId(int x, int z) => 1 + x * 100 + z;
            long eid = 100000;
            for (int x = 0; x < 20; x++)
                for (int z = 0; z < 20; z++)
                {
                    members.Add(new RemovalMember { Id = NodeId(x, z), Kind = MemberKind.Node });
                    graph[NodeId(x, z)] = new List<long>();
                }
            for (int x = 0; x < 20; x++)
                for (int z = 0; z < 20; z++)
                {
                    if (x < 19) AddEdge(NodeId(x, z), NodeId(x + 1, z));
                    if (z < 19) AddEdge(NodeId(x, z), NodeId(x, z + 1));
                }
            for (int i = 0; i < 100; i++) members.Add(new RemovalMember { Id = 900000 + i, Kind = MemberKind.Object });
            void AddEdge(long a, long b)
            {
                long id = eid++;
                members.Add(new RemovalMember { Id = id, Kind = MemberKind.Edge, StartNode = a, EndNode = b });
                graph[a].Add(id); graph[b].Add(id);
            }
            int passes = RunToCompletion(members, graph);
            Assert.Empty(members);
            Assert.True(passes > (760 + 400 + 100) / 32);
        }

        [Fact]
        public void LeftoverNodesAfterManualBulldozeAreRemoved()
        {
            var members = new List<RemovalMember> { new RemovalMember { Id = 1, Kind = MemberKind.Node } };
            var graph = new Dictionary<long, List<long>> { [1] = new List<long>() };
            var p = RemovalPlanner.NextPass(members, graph);
            Assert.Equal(new long[] { 1 }, p.Delete);
        }

        [Fact]
        public void EmptyGroupIsFinalImmediately()
        {
            var p = RemovalPlanner.NextPass(new List<RemovalMember>(), new Dictionary<long, List<long>>());
            Assert.True(p.Final);
            Assert.Empty(p.Delete);
        }

        [Fact]
        public void PlanWorkFlattensAndExcludes()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan { GeneratorId = "parking" };
            l.Nets.Add(new PlannedNet { A = 0, B = 1, C = 2, D = new float3(3, 0, 0), Role = NetRole.DriveAisle });
            l.Objects.Add(new PlannedObject { Position = new float3(1, 0, 1), Role = ObjectRole.ParkingBay });
            l.Objects.Add(new PlannedObject { Position = new float3(float.NaN, 0, 1), Role = ObjectRole.Tree });
            l.Surfaces.Add(new PlannedSurface { Ring = new[] { new float3(0, 0, 0), new float3(1, 0, 0), new float3(0, 0, 1) }, Role = SurfaceRole.Asphalt });
            l.Zoning.Add(new PlannedZoning { ZoneId = "" });
            plan.Layers.Add(l);
            var w = PlanWork.Flatten(plan);
            Assert.Single(w.Nets);
            Assert.Single(w.Objects);   // NaN dropped
            Assert.Empty(w.Zoning);     // empty zone id dropped
            Assert.True(w.HasParking);
            Assert.Equal(AssetCategories.InvisiblePath, w.Nets[0].Category);
            Assert.Equal(AssetCategories.ParkingDecal, w.Objects[0].Category);
            Assert.Equal(AssetCategories.SurfaceAsphalt, w.Surfaces[0].Category);

            var excluded = new HashSet<long> { PlanWork.Key(w.Nets[0]) };
            var w2 = PlanWork.Flatten(plan, excluded);
            Assert.Empty(w2.Nets);
            Assert.Single(w2.Objects);
        }

        [Fact]
        public void BatchesCoverRangeExactly()
        {
            var b = PlanWork.Batches(70, 32);
            Assert.Equal(3, b.Count);
            Assert.Equal(new int2(0, 32), b[0]);
            Assert.Equal(new int2(64, 70), b[2]);
            Assert.Empty(PlanWork.Batches(0, 32));
        }

        [Fact]
        public void ErrorKeysFallBack()
        {
            Assert.Equal("Placemaker.Build.Error.OverlapExisting", BuildErrorKeys.For("OverlapExisting"));
            Assert.Equal(BuildErrorKeys.Other, BuildErrorKeys.For("SomethingNew"));
            Assert.True(BuildErrorKeys.IsGlobal("NotEnoughMoney"));
        }

        [Fact]
        public void HeuristicsClassifyByTraitsFirst()
        {
            var mesh = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(AssetCategories.Tree, AssetHeuristics.Classify("Oak01", mesh | AssetTraits.Plant | AssetTraits.Tree));
            Assert.Empty(AssetHeuristics.Classify("OakStump01", mesh | AssetTraits.Plant | AssetTraits.Tree));
            Assert.Contains(AssetCategories.Bench, AssetHeuristics.Classify("Whatever", mesh | AssetTraits.BenchActivity));
            Assert.DoesNotContain(AssetCategories.Bench, AssetHeuristics.Classify("BenchAdvert01", mesh));
            Assert.Contains(AssetCategories.ParkingDecal, AssetHeuristics.Classify("ParkingLotDecal01", AssetTraits.StaticObject | AssetTraits.ParkingLaneSubLane));
            Assert.Empty(AssetHeuristics.Classify("Mystery", AssetTraits.StaticObject));   // mesh-less: never placeable
            Assert.Contains(AssetCategories.SurfaceGrass, AssetHeuristics.Classify("Grass Surface 01", AssetTraits.Surface));
            Assert.Contains(AssetCategories.InvisiblePath, AssetHeuristics.Classify("Invisible Road Path - 1xTwoway", AssetTraits.Net | AssetTraits.Pathway));
            Assert.Empty(AssetHeuristics.Classify("Fountain Placeholder", mesh));
            Assert.True(AssetHeuristics.DefaultScore(AssetCategories.ParkingDecal, "ParkingLotDecal01")
                        > AssetHeuristics.DefaultScore(AssetCategories.ParkingDecal, "ParkingLotElectricDecal01"));
        }
    }
}
