using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    /// <summary>Sketch study: the site plan (what the map sketch and the panel's top view draw) and its legend agree.</summary>
    public class SitePlanTests
    {
        // a T: the label point must be inside it, not in the empty corner beside the stem
        private static readonly float2[] T =
        {
            new float2(0, 0), new float2(60, 0), new float2(60, 110), new float2(140, 110), new float2(140, 170),
            new float2(-80, 170), new float2(-80, 110), new float2(0, 110),
        };

        private static ComposedPlan Plan()
        {
            var l = new AreaPlan { GeneratorId = "parking" };
            l.Nets.Add(new PlannedNet { A = new float3(10, 0, 20), B = new float3(20, 0, 20), C = new float3(40, 0, 20), D = new float3(50, 0, 20), Role = NetRole.DriveAisle });
            l.Nets.Add(new PlannedNet { A = new float3(30, 0, 5), B = new float3(30, 0, 60), C = new float3(30, 0, 100), D = new float3(30, 0, 140), Role = NetRole.Pathway });
            for (int i = 0; i < 6; i++)
                l.Objects.Add(new PlannedObject { Position = new float3(12 + i * 2.5f, 0, 26.3f), Role = ObjectRole.ParkingBay });
            l.Objects.Add(new PlannedObject { Position = new float3(-40, 0, 140), Role = ObjectRole.Tree, Age = PlannedAge.Adult });
            l.Objects.Add(new PlannedObject { Position = new float3(100, 0, 140), Role = ObjectRole.Tree, Age = PlannedAge.Young });
            l.Objects.Add(new PlannedObject { Position = new float3(20, 0, 60), Role = ObjectRole.Lamp });
            l.Surfaces.Add(new PlannedSurface { Role = SurfaceRole.Asphalt, Ring = new[] { new float3(5, 0, 10), new float3(55, 0, 10), new float3(55, 0, 40), new float3(5, 0, 40) } });
            var plan = new ComposedPlan();
            plan.Layers.Add(l);
            return plan;
        }

        [Fact]
        public void LegendCountsWhatIsDrawn()
        {
            var sp = SitePlan.Compute(new SitePlan.Input { Plan = Plan(), Outline = T, Entrances = new[] { new float2(30, 0) } });
            var rows = sp.Legend.ToDictionary(r => r.Id);
            Assert.Equal(6, rows[SitePlan.Parking].Count);
            Assert.Equal(sp.TreeCrowns.Count, (int)rows[SitePlan.Trees].Count);
            Assert.Equal(1, rows[SitePlan.Furniture].Count);
            Assert.Equal(1, rows[SitePlan.Entrances].Count);
            Assert.Equal(135, rows[SitePlan.Paths].Count, 0);
            Assert.False(rows.ContainsKey(SitePlan.Roads));   // a car park's aisles are not roads
            // stalls: a back line each (the side away from the aisle) and the separators, a shared one drawn once: 6 + 7
            Assert.Equal(13, sp.Stalls.Count);
            Assert.All(sp.Legend, r => Assert.True(Poly.Contains(T, r.Anchor) || r.Id == SitePlan.Entrances, r.Id + " note outside the outline"));
        }

        [Fact]
        public void LabelPointInsideAnOddShape()
        {
            var sp = SitePlan.Compute(new SitePlan.Input { Plan = new ComposedPlan(), Outline = T });
            Assert.True(Poly.Contains(T, sp.Centre));
            Assert.InRange(sp.AreaM2, 60 * 110 + 220 * 60 - 1, 60 * 110 + 220 * 60 + 1);
        }

        [Fact]
        public void TreesNeverSmallerThanTheirGrowthStage()
        {
            // a tree prefab whose bounds are only its trunk (1 × 1 × 4 m): the crown comes from its growth stage
            var plan = Plan();
            foreach (var o in plan.Layers[0].Objects) if (o.Role == ObjectRole.Tree) o.Asset = new AssetRef("trunk");
            var assets = new System.Collections.Generic.Dictionary<string, AssetInfo> { ["trunk"] = new AssetInfo { Id = "trunk", BoundsMin = new float3(-0.5f, 0, -0.5f), BoundsMax = new float3(0.5f, 4, 0.5f) } };
            var sp = SitePlan.Compute(new SitePlan.Input { Plan = plan, Outline = T, Assets = assets });
            Assert.Equal(3.4f, sp.TreeCrowns[0].R, 2);   // adult
            Assert.Equal(1.8f, sp.TreeCrowns[1].R, 2);   // young
        }

        [Fact]
        public void UiJsonIsCompactAndComplete()
        {
            var sp = SitePlan.Compute(new SitePlan.Input { Plan = Plan(), Outline = T, Entrances = new[] { new float2(30, 0) } });
            var json = JsonValue.Parse(sp.ToUi("7").ToString());
            Assert.Equal("7", json["stamp"].AsString());
            Assert.Equal(T.Length * 2, json["outline"].Count);
            Assert.Equal(13 * 4, json["stalls"].Count);
            Assert.Equal(sp.Legend.Count, json["legend"].Count);
            Assert.Equal(3, json["entrances"].Count);
        }
    }
}
