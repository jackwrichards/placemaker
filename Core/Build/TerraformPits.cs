// s55 — pit guard for the terraform target, and a hollow finder for the live ground.
//
// Playtest 91: a lot's entrance (where its lane meets a street) sat in a deep bowl with a torn rim. A parking lot or mall lot is
// never meant to have a hole a few metres wide, so a build of a lot refuses to terraform one: texels of the target that lie lower
// than the lowest point of the ring around them (rings of 5, 9 and 13 m) by more than minDepth are raised to that rim (a few passes: the
// middle of a wide pit comes up as the edge does). Whatever the cause (a lane profile that cannot reach its pins, ties to two streets
// on different levels, a tier boundary), the ground the build asks the game for has no bowl.
// The same ring test run on the live terrain after the build says whether a bowl exists in the game although the target has none.
// Pure geometry: unit-tested (TerraformPitTests).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class TerraformPits
    {
        /// <summary>Ring radii (m): a bowl up to ~25 m across is a pit; a wider basin is a landform.</summary>
        public static readonly float[] Radii = { 5f, 9f, 13f };
        private const int Directions = 16;

        /// <summary>Layers whose design digs on purpose (excavation, ponds, basins, bunkers): the pit guard leaves their target alone.</summary>
        private static readonly HashSet<string> PitTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "quarry", "pond", "wetland", "landfill", "constructionSite", "amphitheatre", "golf", "animalPark", "farmstead", "zenGarden",
            "waterfront", "beach", "shore", "terrain", "terraceGarden", "lookoutHill", "spoilHeaps",
            "roads",   // Hillside terraces cut benches on purpose; its own field already guarantees no hollows (s52)
            "ai",      // the AI builder digs lakes and ditches on purpose; the guard filled parts of an Opus park's ground
        };

        /// <summary>(s58) True when the plan digs a hollow by design: a type that excavates, a water surface, or a sunken/bowl terrain shape (Fact "intentionalPit").</summary>
        public static bool Intentional(ComposedPlan plan)
        {
            if (plan?.Layers == null) return false;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                if (l.GeneratorId != null && PitTypes.Contains(l.GeneratorId)) return true;
                if (l.Facts != null && l.Facts.ContainsKey("intentionalPit")) return true;
                if (l.Surfaces != null) foreach (var sf in l.Surfaces) if (sf != null && sf.Role == SurfaceRole.Water) return true;
            }
            return false;
        }

        public struct Pit
        {
            public float2 At;
            public float Depth;
            public float Height;
        }

        private static float HeightAt(TerraformRaster r, Func<float2, float> original, float2 p)
        {
            float g = original(p);
            return r.TrySample(p, original, out float h) ? h : g;
        }

        /// <summary>How far texel <paramref name="p"/> lies below the lowest point of the rings round it (0 when it is not a pit); rim = that lowest ring height.</summary>
        private static float PitDepth(Func<float2, float> height, float2 p, float h, out float rim)
        {
            // The smallest ring that clears the bowl decides: all of its points are higher (on a hillside or a valley side some point of
            // every ring is lower, on flat ground or a basin wider than the largest ring the depth is 0).
            rim = h;
            float best = 0f;
            foreach (float rad in Radii)
            {
                float low = float.MaxValue;
                for (int k = 0; k < Directions; k++)
                {
                    float a = k * (2f * math.PI / Directions);
                    low = math.min(low, height(p + rad * new float2(math.cos(a), math.sin(a))));
                }
                if (low - h > best) { best = low - h; rim = low; }
            }
            return best;
        }

        /// <summary>
        /// Raises the pits of the raster's target (see the file comment). Returns how many texels were raised; <paramref name="deepest"/> is the
        /// deepest pit found (m, 0 when none) at <paramref name="at"/>. Only texels the terraform covers (weight &gt; 0.2) are touched.
        /// </summary>
        public static int Fill(TerraformRaster r, Func<float2, float> original, float minDepth, out float deepest, out float2 at, int passes = 4)
        {
            deepest = 0f; at = default;
            if (r == null || original == null || r.Width == 0) return 0;
            int total = 0;
            for (int pass = 0; pass < passes; pass++)
            {
                var fix = new List<(int i, float newHeight)>();
                Func<float2, float> height = p => HeightAt(r, original, p);
                for (int y = 0; y < r.Height; y++)
                    for (int x = 0; x < r.Width; x++)
                    {
                        int i = y * r.Width + x;
                        if (!math.isfinite(r.Target[i]) || r.Weight[i] <= 0.2f) continue;
                        var p = r.Center(x, y);
                        float g = original(p);
                        float h = math.lerp(g, r.Target[i], r.Weight[i]);
                        float depth = PitDepth(height, p, h, out float rim);
                        if (depth <= minDepth) continue;
                        if (depth > deepest) { deepest = depth; at = p; }
                        fix.Add((i, rim));
                    }
                if (fix.Count == 0) break;
                foreach (var (i, nh) in fix)
                {
                    var p = r.Center(i % r.Width, i / r.Width);
                    float g = original(p), w = r.Weight[i];
                    r.Target[i] = w >= 0.999f ? nh : g + (nh - g) / w;
                }
                total += fix.Count;
            }
            return total;
        }

        /// <summary>
        /// (core) The deepest hollows of a ground anywhere in a box (every point <paramref name="inside"/> accepts, on a grid of
        /// <paramref name="step"/> m): the live check after a build covers the whole outline and its rim, not only the texels the terraform
        /// touched (a bowl under a lane or at a lot entrance has no terraform texel; teardown 07, a 6-8 m pit beside the strip mall's lane).
        /// </summary>
        public static List<Pit> Scan(Func<float2, float> ground, float2 min, float2 max, Func<float2, bool> inside, float minDepth, int maxPits = 3, float step = 2f)
        {
            var found = new List<Pit>();
            if (ground == null || !(step > 0f)) return found;
            int nx = (int)math.ceil((max.x - min.x) / step), ny = (int)math.ceil((max.y - min.y) / step);
            if (nx <= 0 || ny <= 0 || (long)nx * ny > 400000) return found;
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    var p = min + new float2(i * step, j * step);
                    if (inside != null && !inside(p)) continue;
                    float h = ground(p);
                    if (!math.isfinite(h)) continue;
                    float depth = PitDepth(ground, p, h, out _);
                    if (depth > minDepth) found.Add(new Pit { At = p, Depth = depth, Height = h });
                }
            found.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            var keep = new List<Pit>();
            foreach (var p in found)
            {
                bool near = false;
                foreach (var k in keep) if (math.distance(k.At, p.At) < 12f) { near = true; break; }
                if (!near) keep.Add(p);
                if (keep.Count >= maxPits) break;
            }
            return keep;
        }

        /// <summary>The deepest hollows of a ground over the texels of a raster that terraform covers (live terrain check after a build).</summary>
        public static List<Pit> Worst(TerraformRaster r, Func<float2, float> ground, float minDepth, int max = 3, int step = 2)
        {
            var found = new List<Pit>();
            if (r == null || ground == null) return found;
            for (int y = 0; y < r.Height; y += step)
                for (int x = 0; x < r.Width; x += step)
                {
                    int i = y * r.Width + x;
                    if (!math.isfinite(r.Target[i]) || r.Weight[i] < 0.999f) continue;
                    var p = r.Center(x, y);
                    float h = ground(p);
                    float depth = PitDepth(ground, p, h, out _);
                    if (depth > minDepth) found.Add(new Pit { At = p, Depth = depth, Height = h });
                }
            found.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            // one per bowl: drop entries within 10 m of a deeper one
            var keep = new List<Pit>();
            foreach (var p in found)
            {
                bool near = false;
                foreach (var k in keep) if (math.distance(k.At, p.At) < 10f) { near = true; break; }
                if (!near) keep.Add(p);
                if (keep.Count >= max) break;
            }
            return keep;
        }
    }
}
