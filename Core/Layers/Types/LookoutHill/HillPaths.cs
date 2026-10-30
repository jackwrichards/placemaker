// The way up: a walker that climbs the planned hill at a constant grade.
//
// At every step it takes the contour direction (perpendicular to the slope) and tilts it uphill just
// enough to rise at the wanted grade — on a round hill that is a spiral, on any other outline a path
// that follows the hill's own contours. Switchback style reverses the contour direction after each
// leg, with a short steeper step up at the hairpin so the legs never run on top of each other; spiral
// style only reverses when the outline or something reserved blocks the way. Where the ground is
// gentler than the grade it walks straight for the summit. Points are never allowed close to earlier
// legs, so the path cannot cross itself.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.LookoutHill
{
    public struct ClimbSpec
    {
        public float Grade;
        /// <summary>"spiral", "switchback" or "direct".</summary>
        public string Style;
        public float LegLength;
        public float HalfWidth;
        public float Spacing;
        public float2 Summit;
        public float SummitRadius;
        public uint Seed;
    }

    public static class HillPaths
    {
        private const float Step = 4f;
        private const int MaxSteps = 700;

        public static List<float2> Climb(HillField field, LayerRegion region, Occupancy occ, float2 start, float2 entry, ClimbSpec spec)
        {
            var pts = new List<float2> { start };
            var p = entry;
            pts.Add(p);
            float side = (spec.Seed & 1u) == 0 ? 1f : -1f;
            float leg = 0f;
            float margin = spec.HalfWidth + 2f;
            float arrive = Arrive(spec);
            for (int stepNo = 0; stepNo < MaxSteps; stepNo++)
            {
                // (rf-nature) Close enough to walk straight onto the plaza: stop here. The plaza disc is reserved, so
                // the walker could never step inside SummitRadius + margin and circled the plaza (the hook and the
                // S-bend at the top of the hilltop and kite-hill paths).
                if (math.distance(p, spec.Summit) <= arrive) break;
                var toSummit = math.normalizesafe(spec.Summit - p);
                var g = field.Gradient(p);
                float gl = math.length(g);
                float2 q = p; bool moved = false;
                if (spec.Style == "direct" || gl <= spec.Grade * 1.05f)
                {
                    q = p + toSummit * math.min(Step, math.max(0.5f, math.distance(p, spec.Summit) - arrive + 0.5f));
                    moved = Ok(region, occ, q, margin, spec) && FarFromEarlier(pts, q, spec.Spacing);
                }
                if (!moved && spec.Style != "direct")
                {
                    var up = g / math.max(1e-4f, gl);
                    // Try: on grade, on grade the other way (switchback turn), steeper, then straight up.
                    for (int attempt = 0; attempt < 5 && !moved; attempt++)
                    {
                        // (rf-nature) Before turning back on the spot (a V: the new leg leaves at a hair's breadth from
                        // the old one, an acute node the game dug a pit beside), try a round hairpin.
                        if (attempt == 1 && UTurn(region, occ, pts, ref p, up, new float2(-up.y, up.x) * side, margin, spec))
                        {
                            side = -side;
                            leg = 0f;
                            q = p;
                            moved = true;
                            break;
                        }
                        float grade = spec.Grade * (attempt >= 2 ? 1.8f : 1f);
                        if (attempt == 1 || attempt == 3) side = -side;
                        float sin = math.saturate(grade / math.max(1e-4f, gl));
                        var contour = new float2(-up.y, up.x) * side;
                        var d = attempt == 4 ? math.normalizesafe(up + toSummit) : math.normalizesafe(contour * math.sqrt(1f - sin * sin) + up * sin);
                        q = p + d * Step;
                        moved = Ok(region, occ, q, margin, spec) && FarFromEarlier(pts, q, spec.Spacing);
                        if (moved && (attempt == 1 || attempt == 3)) leg = 0f;
                    }
                }
                if (!moved && spec.Style == "direct")
                {
                    // Direct but blocked: sidestep along the contour.
                    var up = g / math.max(1e-4f, gl);
                    q = p + new float2(-up.y, up.x) * side * Step;
                    moved = Ok(region, occ, q, margin, spec) && FarFromEarlier(pts, q, spec.Spacing);
                }
                if (!moved) break;
                if (math.distancesq(p, q) > 1e-6f) { p = q; pts.Add(p); }
                leg += Step;
                if (spec.Style == "switchback" && leg >= spec.LegLength && gl > spec.Grade * 1.05f)
                {
                    // Hairpin: a round U-turn up the slope (rf-nature: was one straight 7.5 m step straight uphill, a
                    // 30 % piece between two sharp corners), then back the other way.
                    var up = g / math.max(1e-4f, gl);
                    if (!UTurn(region, occ, pts, ref p, up, new float2(-up.y, up.x) * side, margin, spec))
                    {
                        var hp = p + up * (spec.Spacing + 1.5f);
                        if (Ok(region, occ, hp, margin, spec) && FarFromEarlier(pts, hp, spec.Spacing)) { p = hp; pts.Add(p); }
                    }
                    side = -side;
                    leg = 0f;
                }
            }
            // Finish at the summit plaza edge when close enough.
            if (math.distance(p, spec.Summit) > spec.SummitRadius + 0.5f && math.distance(p, spec.Summit) < arrive + 30f)
            {
                var end = spec.Summit + math.normalizesafe(p - spec.Summit) * spec.SummitRadius;
                pts.Add(end);
            }
            Arrival(region, pts, spec);
            return pts;
        }

        /// <summary>
        /// (nature makeover) The walker reaches the plaza going round it (along the contour), and the last step turned 90°
        /// straight in: a hook at the top of every climb (the user: "some of the roads get a little weird"). The last stretch
        /// is redrawn as one curve that leaves the walker's heading and meets the plaza head on, when the curve stays inside
        /// the outline and clear of the legs below.
        /// </summary>
        private static void Arrival(LayerRegion region, List<float2> pts, ClimbSpec spec)
        {
            if (pts.Count < 5 || !Reached(pts, spec)) return;
            float reach = spec.SummitRadius + math.max(24f, spec.SummitRadius * 2f);
            int i = -1;
            for (int k = 2; k < pts.Count - 1; k++)
                if (math.distance(pts[k], spec.Summit) < reach && math.dot(math.normalizesafe(pts[k] - pts[k - 1]), math.normalizesafe(spec.Summit - pts[k])) > -0.3f) { i = k; break; }
            if (i < 2) return;
            var a = pts[i];
            var t = math.normalizesafe(pts[i] - pts[i - 1]);
            float l0 = math.distance(a, spec.Summit) - spec.SummitRadius;
            if (l0 < 3f) return;
            // the arrival point: where the heading, swung toward the summit, meets the plaza rim
            // the rim point facing the walker, nudged a little along its heading: the turn is spread over the whole stretch
            var aim = math.normalizesafe(math.normalizesafe(a - spec.Summit) + t * 0.45f);
            var e = spec.Summit + aim * spec.SummitRadius;
            float len = math.distance(a, e);
            var b = a + t * len * 0.4f;
            var c = e + aim * len * 0.4f;
            var curve = new List<float2>();
            int n = math.clamp((int)(len / 3f), 4, 16);
            for (int k = 1; k <= n; k++)
            {
                float u = k / (float)n, v = 1f - u;
                var q = v * v * v * a + 3f * v * v * u * b + 3f * v * u * u * c + u * u * u * e;
                if (!region.ContainsDisc(q, spec.HalfWidth + 1f)) return;
                if (k < n && math.distance(q, spec.Summit) < spec.SummitRadius + 0.5f) return;
                for (int j = 0; j < i - 3; j++) if (math.distancesq(pts[j], q) < spec.Spacing * spec.Spacing) return;
                curve.Add(q);
            }
            pts.RemoveRange(i + 1, pts.Count - i - 1);
            pts.AddRange(curve);
        }

        public static bool Reached(List<float2> pts, ClimbSpec spec) =>
            pts.Count > 1 && math.distance(pts[pts.Count - 1], spec.Summit) <= spec.SummitRadius + Step;

        /// <summary>
        /// A half circle from <paramref name="p"/> that leaves along <paramref name="dir"/>, swings up the slope and comes
        /// back the other way one leg spacing higher. Adds its points and moves p to its end; false (nothing added) when
        /// any point is blocked.
        /// </summary>
        private static bool UTurn(LayerRegion region, Occupancy occ, List<float2> pts, ref float2 p, float2 up, float2 dir, float margin, ClimbSpec spec)
        {
            float r = (spec.Spacing + 1.5f) * 0.5f;
            var c = p + up * r;
            var arc = new float2[4];
            for (int k = 1; k <= 4; k++)
            {
                float a = k * math.PI / 4f;
                arc[k - 1] = c + (-up * math.cos(a) + dir * math.sin(a)) * r;
                if (!Ok(region, occ, arc[k - 1], margin, spec)) return false;
            }
            if (!FarFromEarlier(pts, arc[3], spec.Spacing)) return false;
            foreach (var q in arc) pts.Add(q);
            p = arc[3];
            return true;
        }

        /// <summary>Distance from the summit at which the path turns straight onto the plaza.</summary>
        private static float Arrive(ClimbSpec spec) => spec.SummitRadius + spec.HalfWidth + 3f;

        private static bool Ok(LayerRegion region, Occupancy occ, float2 q, float margin, ClimbSpec spec)
        {
            if (!region.ContainsDisc(q, margin)) return false;
            if (math.distance(q, spec.Summit) < Arrive(spec) + 0.5f) return true;   // arriving on the plaza
            return occ.Free(q, spec.HalfWidth, margin, 0.3f);
        }

        /// <summary>Clear of the path so far (the last few points, i.e. the current leg's tail, excepted).</summary>
        private static bool FarFromEarlier(List<float2> pts, float2 q, float spacing)
        {
            float s2 = spacing * spacing;
            for (int i = 0; i < pts.Count - 4; i++)
                if (math.distancesq(pts[i], q) < s2) return false;
            return true;
        }
    }
}
