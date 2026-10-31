// Game runner: land made to order that arrives after launch (a queue session gets a scenario with "land" in its
// inbox). The plot is added east of the last one; this writes its ground into the live heightmap the way the launch
// would have, had the plot been there from the start, but only outside what is already laid out, so the grid, the
// earlier plots and every standing build keep their ground exactly.

using Unity.Mathematics;

namespace Placemaker.Core.Suite
{
    public sealed partial class SuiteLayout
    {
        /// <summary>
        /// Patches <paramref name="current"/> (the live heightmap) for a fixture that grew: texels inside the old
        /// fixture rectangle (<paramref name="keepMin"/>..<paramref name="keepMax"/>) are never touched; elsewhere every
        /// texel the grown fixture now reaches gets what <see cref="Heightmap"/> gives it (the layout inside, the ease into
        /// <paramref name="original"/>, the map's heights before the launch, within the blend). Texels whose blend did not
        /// change are left alone, so without <paramref name="original"/> (null: the current heights stand in for it) the
        /// error stays near the new land. Returns the number of texels changed.
        /// </summary>
        public int PatchOutside(ushort[] current, ushort[] original, float2 keepMin, float2 keepMax)
        {
            int n = HeightmapSize;
            if (current == null || current.Length != n * n) throw new System.ArgumentException("heightmap must be " + n + "x" + n);
            if (original != null && original.Length != n * n) throw new System.ArgumentException("original heightmap must be " + n + "x" + n);
            float2 cell = MapExtent / n;
            var lo = FixtureMin - Blend;
            var hi = FixtureMax + Blend;
            int px0 = math.max(0, (int)math.floor((lo.x - MapOffset.x) / cell.x)), px1 = math.min(n - 1, (int)math.ceil((hi.x - MapOffset.x) / cell.x));
            int py0 = math.max(0, (int)math.floor((lo.y - MapOffset.y) / cell.y)), py1 = math.min(n - 1, (int)math.ceil((hi.y - MapOffset.y) / cell.y));
            // (P2-4) What is laid out already: the grid and each earlier plot's own rectangle. The old fixture's bounding box
            // was kept whole, so once a second row of plots began inside it every later plot was left flat (0 texels).
            var kept = new System.Collections.Generic.List<(float2 lo, float2 hi)> { (GridMin, GridMax) };
            for (int k = 0; k < Plots.Count - 1; k++) kept.Add((Plots[k].OuterMin, Plots[k].OuterMax));
            bool Kept(float x, float z) { foreach (var r in kept) if (x >= r.lo.x && x <= r.hi.x && z >= r.lo.y && z <= r.hi.y) return true; return false; }
            float DistKept(float x, float z)
            {
                float best = float.MaxValue;
                foreach (var r in kept)
                {
                    float ex = math.max(math.max(r.lo.x - x, x - r.hi.x), 0f), ez = math.max(math.max(r.lo.y - z, z - r.hi.y), 0f);
                    best = math.min(best, math.sqrt(ex * ex + ez * ez));
                }
                return best;
            }
            int changed = 0;
            for (int py = py0; py <= py1; py++)
            {
                float z = MapOffset.y + (py + 0.5f) * cell.y;
                for (int px = px0; px <= px1; px++)
                {
                    float x = MapOffset.x + (px + 0.5f) * cell.x;
                    if (Kept(x, z)) continue;   // laid out already
                    float d = OutsideFixture(x, z);
                    if (d >= Blend) continue;   // the map's own ground, as before
                    float dOld = DistKept(x, z);
                    if (d >= dOld) continue;     // the grown fixture is no nearer: its ease is what it was
                    int i = py * n + px;
                    float map = Decode(original != null ? original[i] : current[i]);
                    ushort v = Encode(math.lerp(Height(x, z), map, math.smoothstep(0f, Blend, d)));
                    if (current[i] != v) { current[i] = v; changed++; }
                }
            }
            return changed;
        }
    }
}
