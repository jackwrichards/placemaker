// Game runner (rf-beach): the lake beside a plot of land made to order ("land": {"water": ...}), for the shore types
// (beach, waterfront). The plot's ground already has the lake bed (Core/Suite/SuitePlot); here its water sources go in
// the way Placemaker's own lakes do (BuildService.Terrain CreateWaterSource), and the depth is read back from the water
// simulation to know when it is full.

using System.Collections.Generic;
using Game.Common;
using Game.Simulation;
using Placemaker.Core.Suite;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    internal sealed partial class SuiteFixture
    {
        /// <summary>Adds the plot's lake water sources (constant level at the plot's water surface). Returns how many.</summary>
        public int AddWater(SuitePlot plot)
        {
            if (plot == null || !plot.HasWater) return 0;
            var water = _world.GetOrCreateSystemManaged<WaterSystem>();
            bool legacy = water.UseLegacyWaterSources;
            float level = Layout.Base + plot.WaterRise;
            int n = 0;
            foreach (var (at, radius) in plot.WaterSources())
            {
                float bottom = Layout.Height(at.x, at.y);
                if (level - bottom < 0.3f) continue;
                var position = new float3(at.x, bottom, at.y);
                var source = new WaterSourceData
                {
                    m_ConstantDepth = legacy ? 1 : 0,
                    m_Height = legacy ? level : level - bottom,
                    m_Radius = radius,
                    m_Polluted = 0f,
                    m_Id = water.GetNextSourceId(),
                    m_Modifier = 1f,
                };
                source.m_Multiplier = WaterSystem.CalculateSourceMultiplier(source, position);
                for (int k = 0; k < 200 && source.m_Multiplier == 1f; k++)
                {
                    source.m_Radius += 1f;
                    source.m_Multiplier = WaterSystem.CalculateSourceMultiplier(source, position);
                }
                var e = _em.CreateEntity();
                _em.AddComponentData(e, source);
                _em.AddComponentData(e, new global::Game.Objects.Transform { m_Position = position, m_Rotation = quaternion.identity });
                _em.AddComponent<Created>(e);
                _em.AddComponent<Updated>(e);
                n++;
            }
            Note($"plot {plot.Name}: lake on the {plot.Water} side, {plot.WaterWidth:F0} m wide, {plot.WaterDepth:F1} m deep, surface {level:F1} m "
                + $"({plot.Freeboard:F1} m under the land), bank {plot.BankGrade * 100f:F0} %, waterline {plot.ShoreIn:F0} m inside the outline{(plot.Wave > 0f ? $" (winding up to {plot.Wave:F0} m further in, every {plot.WaveLength:F0} m)" : "")}; {n} water source(s)");
            return n;
        }

        /// <summary>Water depth (m) at the plot's probes next to the shore, and the depth each should have when full.</summary>
        public List<(float have, float want)> WaterProbe(SuitePlot plot)
        {
            var r = new List<(float, float)>();
            if (plot == null || !plot.HasWater) return r;
            var water = _world.GetOrCreateSystemManaged<WaterSystem>();
            var data = water.GetSurfacesData(out var deps);
            deps.Complete();
            foreach (var (at, depth) in plot.WaterProbes())
                r.Add((WaterUtils.SampleDepth(ref data, new float3(at.x, 0f, at.y)), depth));
            return r;
        }
    }
}
