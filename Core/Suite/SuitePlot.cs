// Game runner: a plot, test land made to order for one build (its "land" in the scenario). Plots lie in a row east of
// the block grid, each with the shape the build is drawn as (rectangle, circle, pinch, L or any outline), any size,
// the ground it asks for (flat, a slope, hills, bumps, mountains or its own heights) and, unless it asks for none, a
// ring road around it so entrances have a street, and (rf-beach) optionally a lake on one side for the shore types.
// Pure geometry: the game layer writes the ground into the heightmap
// and lays the ring roads like the grid's.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Suite
{
    public sealed class SuitePlot
    {
        public string Name = "P1";
        /// <summary>rect | circle | pinch | l | points</summary>
        public string Shape = "rect";
        /// <summary>The shape's bounding size (m): width (x) and depth (z). A circle of this size is an ellipse.</summary>
        public float2 Size = new float2(130f, 120f);
        /// <summary>For shape "points": the outline relative to the plot's centre (m).</summary>
        public float2[] Points;
        /// <summary>For shape "pinch": the waist's width as a share of the depth.</summary>
        public float Neck = 0.3f;

        /// <summary>flat | slope | hills | bumpy | mountains | heights</summary>
        public string Ground = "flat";
        /// <summary>Slope: rise per metre (0.06 = 6 %), climbing towards north | south | east | west.</summary>
        public float Grade = 0.06f;
        public string Toward = "north";
        /// <summary>Hills / bumpy / mountains: height of the bumps (m) and their spacing (m); 0 = the ground's default.</summary>
        public float Amplitude, Wavelength;
        /// <summary>Ground "heights": metres above the base, rows from north to south, columns west to east, stretched over the shape's bounding box.</summary>
        public float[][] Heights;

        /// <summary>A ring road around the land (so entrances have a street), or open land.</summary>
        public bool RoadsAround = true;
        /// <summary>(shops2) roads "follow": the ring road follows the outline (a round block round a circle, an L-shaped block round an L), Gap outside it.</summary>
        public bool RoadsFollow;
        public string Road = SuiteLayout.SmallRoad;
        /// <summary>From the shape's bounding box to the ring road's centre line (m).</summary>
        public float Gap = 16f;

        /// <summary>
        /// (area tools) Extra roads and paths on the land ("nets": [{"prefab", "points"}]), points relative to the plot's centre:
        /// a dead end with its turning circle, a footpath across the block, a gap between two streets. Laid after the ring road;
        /// an end on a ring node joins it, an end on the middle of a ring edge splits it, as the vanilla road tool does.
        /// </summary>
        public readonly List<(string prefab, float2[] points)> Nets = new List<(string, float2[])>();

        /// <summary>The extra nets in world coordinates, one segment per pair of points.</summary>
        public List<SuiteRoadSegment> NetSegments()
        {
            var r = new List<SuiteRoadSegment>();
            foreach (var (prefab, pts) in Nets)
                for (int k = 0; k + 1 < (pts?.Length ?? 0); k++)
                    r.Add(new SuiteRoadSegment { Prefab = prefab, Line = -2, A = Center + pts[k], B = Center + pts[k + 1], Vertical = math.abs(pts[k + 1].x - pts[k].x) < math.abs(pts[k + 1].y - pts[k].y) });
            return r;
        }

        /// <summary>
        /// (rf-beach) A lake beside the land, for the shore types: "" none, or the side it lies on (north | south | east |
        /// west). The waterline runs along that side of the shape's bounding box (ShoreIn metres inside it), the ground
        /// falls from the land to the water over a bank of BankGrade, the lake floor falls at UnderGrade to WaterDepth and
        /// rises again WaterWidth out. The ring road leaves that side out; the game layer adds the water.
        /// </summary>
        public string Water = "";
        public float WaterWidth = 90f, WaterDepth = 4f, Freeboard = 2f, BankGrade = 0.12f, ShoreIn = 0f;
        public const float UnderGrade = 0.10f, FarBank = 20f;
        public bool HasWater => !string.IsNullOrEmpty(Water);
        /// <summary>The water's surface above the base (m): the land meets it Freeboard higher.</summary>
        public float WaterRise => -Freeboard;
        /// <summary>The bank's run from the land down to the waterline (m).</summary>
        public float BankRun => Freeboard / math.max(0.05f, BankGrade);

        /// <summary>Placed by the layout.</summary>
        public float2 Center;

        /// <summary>The ground follows the plot's own heights this far beyond the ring, then eases back to the base over Fade.</summary>
        public const float Hold = 20f, Fade = 50f;

        public float2 LandMin => Center - Size * 0.5f;
        public float2 LandMax => Center + Size * 0.5f;
        /// <summary>The ring road's centre lines (also the plot's block bounds); on a lake's side the ring takes in the lake.</summary>
        public float2 RingMin => LandMin - new float2(GapOn("west"), GapOn("south"));
        public float2 RingMax => LandMax + new float2(GapOn("east"), GapOn("north"));
        private float GapOn(string side) => side == Water ? math.max(Gap, WaterWidth - ShoreIn + FarBank) : Gap;
        /// <summary>Everything the plot changes.</summary>
        public float2 OuterMin => RingMin - (Hold + Fade);
        public float2 OuterMax => RingMax + (Hold + Fade);
        public float OuterWidth => OuterMax.x - OuterMin.x;

        public float DefaultAmplitude => Ground == "bumpy" ? 1.5f : Ground == "mountains" ? 30f : 4f;
        public float DefaultWavelength => Ground == "bumpy" ? 18f : Ground == "mountains" ? 320f : 60f;

        // ───────────── shape ─────────────

        /// <summary>The outline the build is drawn as, in world coordinates, counter-clockwise.</summary>
        public float2[] Outline()
        {
            float2 h = Size * 0.5f;
            var r = new List<float2>();
            switch (Shape)
            {
                case "circle":
                {
                    // corners about 9 m apart (12 to 40 of them): a person clicking a round outline
                    float perimeter = math.PI * (h.x + h.y);
                    int n = math.clamp((int)math.round(perimeter / 9f), 12, 40);
                    for (int k = 0; k < n; k++)
                    {
                        float a = 2f * math.PI * k / n;
                        r.Add(new float2(math.cos(a) * h.x, math.sin(a) * h.y));
                    }
                    break;
                }
                case "pinch":
                {
                    float w = h.y * math.clamp(Neck, 0.05f, 1f);
                    r.Add(new float2(-h.x, -h.y)); r.Add(new float2(0f, -w)); r.Add(new float2(h.x, -h.y));
                    r.Add(new float2(h.x, h.y)); r.Add(new float2(0f, w)); r.Add(new float2(-h.x, h.y));
                    break;
                }
                case "l":
                    r.Add(new float2(-h.x, -h.y)); r.Add(new float2(h.x, -h.y)); r.Add(new float2(h.x, 0f));
                    r.Add(new float2(0f, 0f)); r.Add(new float2(0f, h.y)); r.Add(new float2(-h.x, h.y));
                    break;
                case "points":
                    if (Points != null) r.AddRange(Points);
                    break;
                default:
                    r.Add(new float2(-h.x, -h.y)); r.Add(new float2(h.x, -h.y)); r.Add(new float2(h.x, h.y)); r.Add(new float2(-h.x, h.y));
                    break;
            }
            for (int k = 0; k < r.Count; k++) r[k] += Center;
            return r.ToArray();
        }

        /// <summary>For shape "points": the bounding size of the given outline (so Size need not be given).</summary>
        public void SizeFromPoints()
        {
            if (Points == null || Points.Length < 3) return;
            float2 lo = Points[0], hi = Points[0];
            foreach (var p in Points) { lo = math.min(lo, p); hi = math.max(hi, p); }
            Size = math.max(hi - lo, 10f);
            // centre the outline on the plot
            var mid = (lo + hi) * 0.5f;
            for (int k = 0; k < Points.Length; k++) Points[k] -= mid;
        }

        // ───────────── ground ─────────────

        /// <summary>1 where the plot's ground applies (the ring + Hold), easing to 0 over Fade beyond it.</summary>
        public float Weight(float x, float z)
        {
            float2 lo = RingMin - Hold, hi = RingMax + Hold;
            float dx = math.max(math.max(lo.x - x, x - hi.x), 0f), dz = math.max(math.max(lo.y - z, z - hi.y), 0f);
            float d = math.sqrt(dx * dx + dz * dz);
            return 1f - math.smoothstep(0f, Fade, d);
        }

        /// <summary>The plot's ground above (or below) the base at (x, z), with its lake when it has one.</summary>
        public float Rise(float x, float z)
        {
            if (!HasWater) return LandRise(x, z);
            float s = IntoWater(x, z, out float2 shore);
            float land = LandRise(x, z) - LandRise(shore.x, shore.y);   // the land meets the bank at the base
            float level = WaterRise, bottom = level - WaterDepth;
            if (s <= 0f) return math.lerp(land, level, math.smoothstep(-BankRun, 0f, s));
            float far = WaterWidth;
            if (s >= far) return math.lerp(level, 0f, math.smoothstep(far, far + FarBank, s));
            return math.max(bottom, level - UnderGrade * math.min(s, far - s));
        }

        private float2 WaterNormal => Water == "south" ? new float2(0f, -1f) : Water == "east" ? new float2(1f, 0f) : Water == "west" ? new float2(-1f, 0f) : new float2(0f, 1f);

        /// <summary>(rf-beach) Signed metres from the waterline into the lake (negative on land), and the waterline point opposite (x, z).</summary>
        public float IntoWater(float x, float z, out float2 shore)
        {
            float2 lo = LandMin, hi = LandMax;
            switch (Water)
            {
                case "south": shore = new float2(x, lo.y + ShoreIn); return shore.y - z;
                case "east": shore = new float2(hi.x - ShoreIn, z); return x - shore.x;
                case "west": shore = new float2(lo.x + ShoreIn, z); return shore.x - x;
                default: shore = new float2(x, hi.y - ShoreIn); return z - shore.y;
            }
        }

        /// <summary>(rf-beach) Water sources down the middle of the lake, about every 1.6 radii, each with its radius.</summary>
        public List<(float2 at, float radius)> WaterSources()
        {
            var r = new List<(float2, float)>();
            if (!HasWater) return r;
            float radius = math.clamp(WaterWidth * 0.4f, 8f, 45f);
            bool alongX = Water == "north" || Water == "south";
            float a0 = alongX ? RingMin.x - Hold : RingMin.y - Hold, a1 = alongX ? RingMax.x + Hold : RingMax.y + Hold;
            int n = math.max(1, (int)math.ceil((a1 - a0) / (radius * 1.6f)));
            IntoWater(Center.x, Center.y, out float2 shore);
            for (int k = 0; k < n; k++)
            {
                float a = math.lerp(a0, a1, (k + 0.5f) / n);
                float2 p = shore + WaterNormal * (WaterWidth * 0.5f);
                if (alongX) p.x = a; else p.y = a;
                r.Add((p, radius));
            }
            return r;
        }

        /// <summary>(rf-beach) Points a few metres into the lake along the shore, with the depth expected there once it is full.</summary>
        public List<(float2 at, float depth)> WaterProbes()
        {
            var r = new List<(float2, float)>();
            if (!HasWater) return r;
            float s = math.min(WaterWidth * 0.5f, 15f);
            IntoWater(Center.x, Center.y, out float2 shore);
            bool alongX = Water == "north" || Water == "south";
            for (int k = -1; k <= 1; k++)
            {
                float2 p = shore + WaterNormal * s;
                if (alongX) p.x = Center.x + k * Size.x * 0.35f; else p.y = Center.y + k * Size.y * 0.35f;
                r.Add((p, math.min(WaterDepth, UnderGrade * s)));
            }
            return r;
        }

        /// <summary>The land's own ground (flat, slope, hills...) above the base at (x, z).</summary>
        private float LandRise(float x, float z)
        {
            float u = x - Center.x, v = z - Center.y;
            float a = Amplitude > 0f ? Amplitude : DefaultAmplitude;
            float l = math.max(4f, Wavelength > 0f ? Wavelength : DefaultWavelength);
            float k = 2f * math.PI / l;
            switch (Ground)
            {
                case "slope":
                {
                    float along = Toward == "south" ? -v : Toward == "east" ? u : Toward == "west" ? -u : v;
                    float reach = (Toward == "east" || Toward == "west" ? RingMax.x - RingMin.x : RingMax.y - RingMin.y) * 0.5f + Hold;
                    return Grade * math.clamp(along, -reach, reach);
                }
                case "hills":
                    return a * math.sin(u * k) * math.cos(v * k);
                case "bumpy":
                    return a * (0.7f * math.sin(u * k) * math.cos(v * k * 1.3f) + 0.3f * math.sin((u * 0.6f + v * 0.8f) * k * 2.1f));
                case "mountains":
                    return a * (0.55f * math.sin((u * 0.8f + v * 0.6f) * k) + 0.3f * math.sin((-u * 0.5f + v * 0.9f) * k * 2f + 1.3f)
                        + 0.15f * math.sin((u * 0.3f - v) * k * 4f + 0.7f));
                case "heights":
                    return Sample(u, v);
            }
            return 0f;
        }

        /// <summary>Bilinear sample of Heights over the land's bounding box (clamped at its edges).</summary>
        private float Sample(float u, float v)
        {
            if (Heights == null || Heights.Length == 0 || Heights[0] == null || Heights[0].Length == 0) return 0f;
            int rows = Heights.Length, cols = Heights[0].Length;
            float fx = math.saturate(u / Size.x + 0.5f) * (cols - 1);
            float fy = math.saturate(0.5f - v / Size.y) * (rows - 1);   // row 0 is the north edge
            int x0 = (int)math.floor(fx), y0 = (int)math.floor(fy);
            int x1 = math.min(cols - 1, x0 + 1), y1 = math.min(rows - 1, y0 + 1);
            float tx = fx - x0, ty = fy - y0;
            float At(int r, int c) => Heights[r] != null && c < Heights[r].Length ? Heights[r][c] : 0f;
            return math.lerp(math.lerp(At(y0, x0), At(y0, x1), tx), math.lerp(At(y1, x0), At(y1, x1), tx), ty);
        }

        // ───────────── ring road ─────────────

        /// <summary>The ring road around the land as segments (short ones on uneven ground, so the road follows it).</summary>
        public List<SuiteRoadSegment> RingRoads()
        {
            var r = new List<SuiteRoadSegment>();
            if (!RoadsAround) return r;
            float piece = Ground == "flat" ? 150f : 30f;
            if (RoadsFollow && !HasWater && Shape != "rect") return FollowRoads(piece);
            float2 a = RingMin, b = RingMax;
            // (rf-beach) with a lake the ring leaves its side out and its two legs stop on the land, short of the bank
            if (HasWater)
            {
                float stop = BankRun + 6f;
                IntoWater(Center.x, Center.y, out float2 shore);
                if (Water == "north") b.y = shore.y - stop;
                else if (Water == "south") a.y = shore.y + stop;
                else if (Water == "east") b.x = shore.x - stop;
                else a.x = shore.x + stop;
            }
            string[] names = { "south", "east", "north", "west" };
            var corners = new[] { new float2(a.x, a.y), new float2(b.x, a.y), new float2(b.x, b.y), new float2(a.x, b.y) };
            for (int side = 0; side < 4; side++)
            {
                if (names[side] == Water) continue;
                float2 p = corners[side], q = corners[(side + 1) % 4];
                int n = math.max(1, (int)math.ceil(math.distance(p, q) / piece));
                for (int k = 0; k < n; k++)
                    r.Add(new SuiteRoadSegment
                    {
                        Prefab = Road, Vertical = side % 2 == 1, Line = -1,
                        A = k == 0 ? p : math.lerp(p, q, k / (float)n), B = k == n - 1 ? q : math.lerp(p, q, (k + 1) / (float)n),
                    });
            }
            return r;
        }

        /// <summary>(shops2) The ring road along the outline, Gap outside it (a circle: a polygon of ~12 m pieces at the grown radii).</summary>
        private List<SuiteRoadSegment> FollowRoads(float piece)
        {
            var r = new List<SuiteRoadSegment>();
            var pts = new List<float2>();
            if (Shape == "circle")
            {
                float2 h = Size * 0.5f + Gap;
                int n = math.clamp((int)math.round(math.PI * (h.x + h.y) / 24f), 16, 64);
                for (int k = 0; k < n; k++) { float a = 2f * math.PI * k / n; pts.Add(Center + new float2(math.cos(a) * h.x, math.sin(a) * h.y)); }
            }
            else
            {
                var outline = Outline();
                var grown = Placemaker.Core.Geometry.Poly.Offset(Placemaker.Core.Geometry.Poly.EnsureCcw(outline), -Gap);
                float2[] best = null; float area = 0f;
                foreach (var g in grown) { float a = Placemaker.Core.Geometry.Poly.Area(g); if (a > area) { area = a; best = g; } }
                if (best == null) return r;
                foreach (var p in best)
                {
                    // long sides in pieces, as the box ring has them
                    if (pts.Count > 0) { var q = pts[pts.Count - 1]; int m = math.max(1, (int)math.ceil(math.distance(q, p) / piece)); for (int k = 1; k < m; k++) pts.Add(math.lerp(q, p, k / (float)m)); }
                    pts.Add(p);
                }
                { var q = pts[pts.Count - 1]; var p0 = pts[0]; int m = math.max(1, (int)math.ceil(math.distance(q, p0) / piece)); for (int k = 1; k < m; k++) pts.Add(math.lerp(q, p0, k / (float)m)); }
            }
            for (int k = 0; k < pts.Count; k++)
            {
                float2 a = pts[k], b = pts[(k + 1) % pts.Count];
                r.Add(new SuiteRoadSegment { Prefab = Road, Vertical = math.abs(b.x - a.x) < math.abs(b.y - a.y), Line = -1, A = a, B = b });
            }
            return r;
        }

        /// <summary>The plot as a block: its ring road's centre lines as the bounds, the ring's road on every side.</summary>
        public SuiteBlock Block() => new SuiteBlock
        {
            Name = Name, Kind = SuiteBlockKind.Plot, I = -1, J = -1,
            X0 = RingMin.x, X1 = RingMax.x, Z0 = RingMin.y, Z1 = RingMax.y,
            West = RoadsAround && Water != "west" ? Road : "", East = RoadsAround && Water != "east" ? Road : "",
            South = RoadsAround && Water != "south" ? Road : "", North = RoadsAround && Water != "north" ? Road : "",
        };
    }
}
