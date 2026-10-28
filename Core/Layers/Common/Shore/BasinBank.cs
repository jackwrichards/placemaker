// Banks round a dug basin on sloping ground (27 Oct, game runner on hills). A pond or lake takes one water level, set
// under its lowest bank so it never spills; on a slope the basin's uphill side was then cut straight down from the hill
// to the water: a wall many metres high (the wetland's pits, the park lake's cliffs), which the game's ground could not
// even reach, so the paths and bridges along it broke. Where the ground stands well above the water, a sloped bank now
// runs from just above the water up to the ground (no steeper than 1 : 4), as sloped terraform quads round the basin.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common.Shore
{
    public static class BasinBank
    {
        /// <summary>The bank's grade (rise / run): 1 : 4.</summary>
        public const float Grade = 0.25f;
        /// <summary>The bank starts this far above the water (a dry rim).</summary>
        public const float Rim = 0.3f;
        /// <summary>Ground less than this above the rim keeps its own shore.</summary>
        public const float MinRise = 0.6f;
        /// <summary>The widest bank (m): steeper land than that keeps a (lower) cut.</summary>
        public const float MaxWidth = 30f;

        /// <summary>
        /// Sloped bank quads round <paramref name="basin"/> wherever <paramref name="ground"/> stands more than
        /// <see cref="MinRise"/> above the rim. Returns them (Kind Lower, Permanent, Sloped); none on level ground.
        /// </summary>
        public static List<PlannedTerraform> Quads(float2[] basin, float level, System.Func<float2, float> ground, float step = 4f)
        {
            var result = new List<PlannedTerraform>();
            if (basin == null || basin.Length < 3 || !math.isfinite(level)) return result;
            var ring = Poly.Resample(Poly.EnsureCcw(basin), step);
            int n = ring.Length;
            if (n < 3) return result;
            float rim = level + Rim;
            var outer = new float2[n];
            var outerY = new float[n];
            var wide = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var p = ring[i];
                var a = ring[(i - 1 + n) % n]; var b = ring[(i + 1) % n];
                var t = math.normalizesafe(b - a);
                var o = new float2(t.y, -t.x);   // outward of a CCW ring
                // the bank's foot meets the ground where it is Grade × width above the rim
                float w = 0f;
                for (int it = 0; it < 4; it++)
                    w = math.clamp((ground(p + o * w) - rim) / Grade, 0f, MaxWidth);
                wide[i] = w * Grade > MinRise;
                outer[i] = p + o * math.max(w, 0.5f);
                outerY[i] = ground(outer[i]);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (!wide[i] && !wide[j]) continue;
                var quad = new[]
                {
                    new float3(ring[i].x, rim, ring[i].y), new float3(ring[j].x, rim, ring[j].y),
                    new float3(outer[j].x, outerY[j], outer[j].y), new float3(outer[i].x, outerY[i], outer[i].y),
                };
                // a corner turned inside out (a tight inner bend) is left to the neighbours
                if (Poly.Area(new[] { quad[0].xz, quad[1].xz, quad[2].xz, quad[3].xz }) < 0.5f) continue;
                result.Add(new PlannedTerraform
                {
                    Ring = quad, Kind = TerraformKind.Lower, TargetHeight = (rim + (outerY[i] + outerY[j]) * 0.5f) * 0.5f,
                    Permanent = true, Sloped = true,
                });
            }
            return result;
        }

        /// <summary>Adds the bank round a basin to the layer's plan; the number of quads.</summary>
        public static int Add(LayerContext ctx, float2[] basin, float level)
        {
            var quads = Quads(basin, level, ctx.H);
            ctx.Plan.Terraform.AddRange(quads);
            if (quads.Count == 0) return 0;
            ctx.Plan.Facts["basinBankQuads"] = (ctx.Plan.Facts.TryGetValue("basinBankQuads", out var k) ? k : 0) + quads.Count;
            // what the layer places afterwards stands on the bank (a bridge landed at the old ground height, above the bank:
            // a notch beside its end; the user, 27 Oct)
            var ring = Poly.EnsureCcw(basin);
            Poly.Bounds(ring, out var min, out var max);
            float rim = level + Rim;
            System.Func<float2, float> before = ctx.H;   // the original ground (ctx.G would read this patch back)
            ctx.Ground.Override(min - MaxWidth, max + MaxWidth,
                p => math.min(before(p), rim + Grade * Poly.DistanceToBoundary(ring, p)),
                p => !Poly.Contains(ring, p) && Poly.DistanceToBoundary(ring, p) <= MaxWidth);
            return quads.Count;
        }
    }
}
