// Placemaker neighbourhood polish — the ground and the water around a patch, read off the frame (GPL-3.0).
//
// A plan needs the ground's height on a 4 m raster over its patch (gardens stop at cliffs) and where the water is (gardens
// never reach into it). Read on the main thread that was 40 000 terrain samples and as many water samples every pass,
// most of a pass's time on the frame (frames of 50-250 ms while a town was outgrown). Both are now jobs, read the way the
// game's own systems read them (TerrainSystem.AddCPUHeightReader, WaterSystem.AddSurfaceReader: never while the game
// writes them); the plan starts on its worker once they are done, a frame or two later. The water is sampled a water gap
// further out than the ground (gardens keep that far off it) and its outlines are smoothed (Core/Neighbours/WaterEdge: no
// staircase along a shore).

using System.Collections.Generic;
using Game.Simulation;
using Placemaker.Core.Neighbours;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        private WaterSystem _waterSystem;
        private const float WaterSampleStep = 4f;

        [UnityEngine.Scripting.Preserve]
        protected override void OnDestroy()
        {
            DropSampling();
            base.OnDestroy();
        }

        private struct HeightSampleJob : IJobParallelFor
        {
            [ReadOnly] public TerrainHeightData Heights;
            public float2 Min;
            public float Cell;
            public int Nx;
            [WriteOnly] public NativeArray<float> Out;

            public void Execute(int index)
            {
                int i = index % Nx, j = index / Nx;
                var p = Min + new float2(i, j) * Cell;
                Out[index] = TerrainUtils.SampleHeight(ref Heights, new float3(p.x, 0f, p.y));
            }
        }

        private struct WaterSampleJob : IJobParallelFor
        {
            [ReadOnly] public WaterSurfacesData Water;
            public float2 Min;
            public float Step;
            public int Nx;
            [WriteOnly] public NativeArray<byte> Wet;

            public void Execute(int index)
            {
                int i = index % Nx, j = index / Nx;
                float d = WaterUtils.SampleDepth(ref Water, new float3(Min.x + (i + 0.5f) * Step, 0f, Min.y + (j + 0.5f) * Step));
                Wet[index] = d > BlockObstacles.MaxWaterDepth ? (byte)1 : (byte)0;
            }
        }

        /// <summary>What a pass waits for before its plan starts: the ground and water samples being read by jobs.</summary>
        private sealed class Sampling
        {
            public JobHandle Handle;
            public NativeArray<float> Heights;
            public NativeArray<byte> Wet;
            public float2 Min, Max, WetMin, WetMax;
            public float Cell;
            public int Nx, Ny, WetNx, WetNz;
            public bool HasWater;

            public void Dispose()
            {
                Handle.Complete();
                if (Heights.IsCreated) Heights.Dispose();
                if (Wet.IsCreated) Wet.Dispose();
            }
        }

        /// <summary>The ground raster (as HeightRaster lays it out) over [lo, hi] and the water grid (as BlockObstacles.Water) over
        /// it grown by <paramref name="wetGrow"/>, as jobs.</summary>
        private Sampling StartSampling(float2 lo, float2 hi, float wetGrow)
        {
            var s = new Sampling { Min = lo, Max = hi, WetMin = lo - wetGrow, WetMax = hi + wetGrow };
            var (nx, ny, cell) = HeightRaster.Layout(lo, hi, math.max(4f, math.cmax(hi - lo) / 200f));
            s.Nx = nx; s.Ny = ny; s.Cell = cell;
            s.Heights = new NativeArray<float>(nx * ny, Allocator.Persistent);
            var heights = _terrain.GetHeightData(true);
            var hJob = new HeightSampleJob { Heights = heights, Min = lo, Cell = cell, Nx = nx, Out = s.Heights }.Schedule(nx * ny, 256);
            _terrain.AddCPUHeightReader(hJob);
            s.Handle = hJob;
            _waterSystem ??= World.GetExistingSystemManaged<WaterSystem>();
            int wnx = (int)math.ceil((s.WetMax.x - s.WetMin.x) / WaterSampleStep) + 1, wnz = (int)math.ceil((s.WetMax.y - s.WetMin.y) / WaterSampleStep) + 1;
            if (_waterSystem != null && (long)wnx * wnz <= 250_000)
            {
                s.WetNx = wnx; s.WetNz = wnz;
                s.Wet = new NativeArray<byte>(wnx * wnz, Allocator.Persistent);
                var water = _waterSystem.GetSurfacesData(out var deps);
                var wJob = new WaterSampleJob { Water = water, Min = s.WetMin, Step = WaterSampleStep, Nx = wnx, Wet = s.Wet }.Schedule(wnx * wnz, 256, deps);
                _waterSystem.AddSurfaceReader(wJob);
                s.Handle = JobHandle.CombineDependencies(hJob, wJob);
                s.HasWater = true;
            }
            return s;
        }

        /// <summary>The samples as the plan uses them: the raster (bad samples as 0, as HeightRaster reads them) and the water
        /// as smooth outlines (WaterEdge), worked out on the plan's worker.</summary>
        private static (HeightRaster Raster, List<float2[]> Water) Sampled(float2 lo, int nx, int ny, float cell, float[] heights, byte[] wet, float2 wlo, float2 whi, int wnx, int wnz)
        {
            for (int k = 0; k < heights.Length; k++) if (float.IsNaN(heights[k]) || float.IsInfinity(heights[k])) heights[k] = 0f;
            var raster = new HeightRaster(lo, cell, nx, ny, heights);
            int cx = (int)math.ceil((whi.x - wlo.x) / WaterSampleStep), cz = (int)math.ceil((whi.y - wlo.y) / WaterSampleStep);
            return (raster, WaterEdge.Outlines(wet, wnx, wnz, wlo, WaterSampleStep, cx, cz));
        }
    }
}
