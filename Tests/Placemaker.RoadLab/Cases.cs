// Placemaker road lab — the synthetic sites: land (flat, slopes, a hill), the selection, and the network already there
// (city streets with their junctions, a highway with its ramps, an avenue through the area, a small two-lane road).
// Existing roads are built the way the game stores them: one edge between two nodes, nodes shared bit-for-bit.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Unity.Mathematics;

namespace Placemaker.RoadLab
{
    public sealed class LabCase
    {
        public string Id = "", Title = "", Expect = "";
        public float3[] Ring = Array.Empty<float3>();
        public AreaSource Source = AreaSource.Drawn;
        public Func<float2, float> Height = _ => 0f;
        public List<NearbyNet> Nets = new List<NearbyNet>();
        public List<Obstacle> Obstacles = new List<Obstacle>();
    }

    /// <summary>Builds an existing network: straight or curved edges between shared nodes.</summary>
    public sealed class NetBuilder
    {
        private readonly List<NearbyNet> _nets;
        private readonly Func<float2, float> _h;
        private long _id;

        public NetBuilder(List<NearbyNet> nets, Func<float2, float> height, long firstId = 1000)
        {
            _nets = nets; _h = height; _id = firstId;
        }

        private float3 At(float2 p) => new float3(p.x, _h(p), p.y);

        public NetBuilder Street(float2 a, float2 b, float width = 16f) => Add(a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b, width, false, true, 50f, false);

        public NetBuilder Curve(float2 a, float2 b, float2 c, float2 d, float width = 16f) => Add(a, b, c, d, width, false, true, 50f, false);

        public NetBuilder Highway(float2 a, float2 b, float2 c, float2 d, float width = 15f, float speed = 110f) => Add(a, b, c, d, width, true, false, speed, true);

        public NetBuilder Highway(float2 a, float2 b, float width = 15f) => Highway(a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b, width);

        /// <summary>A straight road through the given nodes (one edge between each consecutive pair).</summary>
        public NetBuilder Chain(float width, params float2[] nodes)
        {
            for (int i = 0; i + 1 < nodes.Length; i++) Street(nodes[i], nodes[i + 1], width);
            return this;
        }

        public NetBuilder Add(float2 a, float2 b, float2 c, float2 d, float width, bool highway, bool sidewalk, float speed, bool oneWay)
        {
            _nets.Add(new NearbyNet
            {
                ExternalId = _id++, A = At(a), B = At(b), C = At(c), D = At(d), Width = width, Category = NetCategory.Road,
                HasSidewalk = sidewalk, IsHighway = highway, SpeedLimit = speed, IsOneWay = oneWay,
            });
            return this;
        }

        /// <summary>A cubic curve split into <paramref name="pieces"/> edges at equal parameter steps (shared nodes).</summary>
        public NetBuilder SplitCurve(float2 a, float2 b, float2 c, float2 d, int pieces, float width, bool highway = false)
        {
            var curve = new Placemaker.Core.Layers.Roads.RoadCurve(new float3(a.x, 0, a.y), new float3(b.x, 0, b.y), new float3(c.x, 0, c.y), new float3(d.x, 0, d.y));
            for (int i = 0; i < pieces; i++)
            {
                var sub = curve.Sub((float)i / pieces, (float)(i + 1) / pieces);
                Add(sub.A.xz, sub.B.xz, sub.C.xz, sub.D.xz, width, highway, !highway, highway ? 110f : 50f, highway);
            }
            return this;
        }
    }

    public static class Cases
    {
        public static readonly string[] All = { "open", "city", "highway", "avenue", "smallroad", "hill", "steep", "irregular", "rotated", "everything", "desire", "bighill" };

        public static float3[] Rect(float w, float d, float rotDeg = 0f, float2 centre = default)
        {
            var pts = new[] { new float2(0, 0), new float2(w, 0), new float2(w, d), new float2(0, d) };
            if (rotDeg != 0f)
            {
                float a = math.radians(rotDeg), c = math.cos(a), s = math.sin(a);
                float2 o = new float2(w, d) * 0.5f;
                pts = pts.Select(p => { var q = p - o; return new float2(q.x * c - q.y * s, q.x * s + q.y * c) + o + centre; }).ToArray();
            }
            return pts.Select(p => new float3(p.x, 0, p.y)).ToArray();
        }

        private static float Noise(float2 p) => 0.6f * math.sin(p.x / 37f + 1.3f) * math.cos(p.y / 29f) + 0.4f * math.sin(p.x / 13f) * math.sin(p.y / 17f + 0.7f);

        /// <summary>Gentle 2-3 % tilt.</summary>
        public static Func<float2, float> Gentle => p => 0.022f * p.x + 0.012f * p.y + Noise(p);

        /// <summary>A 34 m dome a little east of the centre of a 500 x 400 site on a 1.5 % tilt (grades up to ~14 %).</summary>
        public static Func<float2, float> Hill(float2 top, float sigma, float rise = 34f) =>
            p => 0.015f * p.x + rise * math.exp(-math.lengthsq(p - top) / (2f * sigma * sigma)) + Noise(p);

        /// <summary>A 10 % hillside rising to the north.</summary>
        public static Func<float2, float> Steep => p => 0.1f * p.y + 0.02f * p.x + Noise(p);

        /// <summary>City streets along the south and west sides with side streets every 110 m, a street 55 m north.</summary>
        private static void CityAround(NetBuilder b, float w, float d, bool south = true, bool west = true, bool north = true)
        {
            if (south)
            {
                var xs = new List<float> { -200f };
                for (float x = -90f; x < w + 120f; x += 110f) xs.Add(x);
                xs.Add(w + 200f);
                b.Chain(16f, xs.Select(x => new float2(x, -40f)).ToArray());
                foreach (var x in xs.Skip(1).Take(xs.Count - 2)) b.Street(new float2(x, -40f), new float2(x, -180f));
            }
            if (west)
            {
                var zs = new List<float> { south ? -40f : -200f };
                for (float z = 70f; z < d + 40f; z += 110f) zs.Add(z);
                if (north) zs.Add(d + 55f);
                zs.Add(d + 200f);
                b.Chain(16f, zs.Select(z => new float2(-40f, z)).ToArray());
                foreach (var z in zs.Skip(1).Take(zs.Count - 2)) if (!(north && math.abs(z - (d + 55f)) < 1f)) b.Street(new float2(-40f, z), new float2(-180f, z));
            }
            if (north)
            {
                var xs = new List<float> { west ? -40f : -200f };
                for (float x = 70f; x < w + 120f; x += 110f) xs.Add(x);
                b.Chain(16f, xs.Select(x => new float2(x, d + 55f)).ToArray());
                foreach (var x in xs.Skip(1)) b.Street(new float2(x, d + 55f), new float2(x, d + 190f));
            }
        }

        public static LabCase Make(string id)
        {
            var c = new LabCase { Id = id };
            NetBuilder B() => new NetBuilder(c.Nets, c.Height);
            switch (id)
            {
                case "open":
                    c.Title = "Open land, flat, nothing around";
                    c.Expect = "The perimeter hugs the rectangle; a clean grid.";
                    c.Ring = Rect(500, 360);
                    break;
                case "city":
                    c.Title = "Flat land in a street grid (streets on 3 sides)";
                    c.Expect = "Links square to the streets, in line with the grid inside, on the existing junctions or well clear of them.";
                    c.Ring = Rect(500, 360);
                    CityAround(B(), 500, 360);
                    break;
                case "highway":
                {
                    c.Title = "A highway and its ramps north of the area";
                    c.Expect = "No link to the highway or a ramp; links only to the south and east streets.";
                    c.Ring = Rect(500, 360);
                    var b = B();
                    // eastbound / westbound carriageways 70 m and 95 m north of the area
                    b.Highway(new float2(-300, 430), new float2(260, 430));
                    b.Highway(new float2(260, 430), new float2(800, 430));
                    b.Highway(new float2(800, 430), new float2(1100, 430));
                    b.Highway(new float2(1100, 455), new float2(-300, 455));
                    // off-ramp down to the east street, on-ramp back up
                    b.Highway(new float2(260, 430), new float2(400, 428), new float2(520, 412), new float2(560, 380), 8f, 70f);
                    b.Highway(new float2(560, 380), new float2(600, 410), new float2(700, 430), new float2(800, 430), 8f, 70f);
                    b.Chain(16f, new float2(560, -200), new float2(560, -40), new float2(560, 120), new float2(560, 380));
                    b.Chain(16f, new float2(-200, -40), new float2(-90, -40), new float2(20, -40), new float2(130, -40), new float2(240, -40), new float2(350, -40), new float2(460, -40), new float2(560, -40));
                    foreach (var x in new[] { -90f, 20f, 130f, 240f, 350f, 460f }) b.Street(new float2(x, -40), new float2(x, -180));
                    b.Street(new float2(560, 120), new float2(700, 120));
                    break;
                }
                case "avenue":
                {
                    c.Title = "A four-lane avenue straight through the area (20 degrees off the outline)";
                    c.Expect = "The avenue is the spine: the grid turns to it, streets meet it square and not too often, the existing crossroads stub is used.";
                    c.Ring = Rect(560, 420);
                    c.Height = p => 0.01f * p.x + Noise(p);
                    var b = B();
                    float2 mid = new float2(280, 210), d = new float2(math.cos(math.radians(20f)), math.sin(math.radians(20f)));
                    float2 n = new float2(-d.y, d.x);
                    float[] ts = { -560f, -400f, 70f, 400f, 560f };
                    b.Chain(24f, ts.Select(t => mid + d * t).ToArray());
                    // side streets off the avenue outside the area, and an existing crossroads inside (two short stubs)
                    b.Street(mid + d * -400f, mid + d * -400f + n * 150f).Street(mid + d * -400f, mid + d * -400f - n * 150f);
                    b.Street(mid + d * 400f, mid + d * 400f + n * 150f).Street(mid + d * 400f, mid + d * 400f - n * 150f);
                    b.Street(mid + d * 70f, mid + d * 70f + n * 60f).Street(mid + d * 70f, mid + d * 70f - n * 45f);
                    b.Chain(16f, new float2(-200, -40), new float2(-60, -40), new float2(60, -40), new float2(180, -40), new float2(300, -40), new float2(420, -40), new float2(540, -40), new float2(700, -40));
                    break;
                }
                case "smallroad":
                {
                    c.Title = "A small two-lane road winding through the area";
                    c.Expect = "The road stays a small road (no avenue made of it); the grid joins it square, junctions not crowded.";
                    c.Ring = Rect(500, 360);
                    c.Height = Gentle;
                    var b = B();
                    b.SplitCurve(new float2(-200, 70), new float2(180, 30), new float2(260, 330), new float2(700, 300), 4, 16f);
                    b.Chain(16f, new float2(-200, -40), new float2(-60, -40), new float2(60, -40), new float2(180, -40), new float2(300, -40), new float2(420, -40), new float2(540, -40), new float2(700, -40));
                    b.Chain(16f, new float2(540, -40), new float2(540, 100), new float2(540, 220));
                    break;
                }
                case "hill":
                    c.Title = "A 34 m hill, streets south and west";
                    c.Expect = "Few steep pieces; the grid turned or bent so the climbs stay gentle.";
                    c.Ring = Rect(500, 400);
                    c.Height = Hill(new float2(290, 208), 135f);
                    CityAround(B(), 500, 400, north: false);
                    break;
                case "steep":
                    c.Title = "A 10 % hillside rising north";
                    c.Expect = "Long streets along the slope, short climbs; few pieces over the design grade.";
                    c.Ring = Rect(460, 380);
                    c.Height = Steep;
                    CityAround(B(), 460, 380, north: false);
                    break;
                case "irregular":
                {
                    c.Title = "An irregular drawn area on gentle land";
                    c.Expect = "The perimeter follows every edge; no slivers, no knife-edge junctions at the odd corners.";
                    float2[] bas = { new float2(0, 0), new float2(140, -6), new float2(152, 58), new float2(118, 102), new float2(46, 96), new float2(6, 70) };
                    float s = 3.6f;
                    c.Ring = bas.Select(p => new float3(p.x * s, 0, p.y * s)).ToArray();
                    c.Height = Gentle;
                    var b = B();
                    b.Chain(16f, new float2(-150, -50), new float2(-30, -40), new float2(120, -48), new float2(280, -58), new float2(440, -64), new float2(620, -60));
                    b.Chain(16f, new float2(-30, -40), new float2(-50, 100), new float2(-60, 260), new float2(-40, 420));
                    break;
                }
                case "rotated":
                {
                    c.Title = "A rotated area (25 degrees) in a north-south city grid";
                    c.Expect = "Links still square to the city streets; the inside grid follows the outline; the dead ends around are picked up.";
                    c.Ring = Rect(480, 340, 25f);
                    var ring2 = c.Ring.Select(p => p.xz).ToArray();
                    var b = B();
                    float sp = 110f;
                    float lo = -330f, hi = 800f;
                    bool Clear(float2 p) => !Placemaker.Core.Geometry.Poly.Contains(ring2, p) && Placemaker.Core.Geometry.Poly.DistanceToBoundary(ring2, p) > 30f;
                    for (float x = lo; x <= hi; x += sp)
                        for (float z = lo; z + sp <= hi; z += sp)
                        {
                            var a = new float2(x, z); var e = new float2(x, z + sp);
                            if (Clear(a) && Clear(e) && Clear(math.lerp(a, e, 0.5f))) b.Street(a, e);
                        }
                    for (float z = lo; z <= hi; z += sp)
                        for (float x = lo; x + sp <= hi; x += sp)
                        {
                            var a = new float2(x, z); var e = new float2(x + sp, z);
                            if (Clear(a) && Clear(e) && Clear(math.lerp(a, e, 0.5f))) b.Street(a, e);
                        }
                    break;
                }
                case "everything":
                {
                    c.Title = "Hills, a highway with a ramp, an avenue through, a small road crossing";
                    c.Expect = "No highway link; the avenue is the spine with spaced junctions; the small road stays small and is joined square.";
                    c.Ring = Rect(600, 450);
                    c.Height = Hill(new float2(170, 300), 150f, 22f);
                    var b = B();
                    b.Highway(new float2(-300, 560), new float2(240, 560));
                    b.Highway(new float2(240, 560), new float2(900, 560));
                    b.Highway(new float2(900, 585), new float2(-300, 585));
                    // the avenue N-S (slightly turned), its ramp to the highway, an existing crossroads inside with the small road
                    float2 a0 = new float2(300, -220), a1 = new float2(316, -45), a2 = new float2(332, 130), a3 = new float2(355, 380), a4 = new float2(366, 500);
                    b.Chain(24f, a0, a1, a2, a3, a4);
                    b.Highway(a4, new float2(330, 530), new float2(280, 556), new float2(240, 560), 8f, 70f);
                    b.Street(a1, new float2(150, -45)).Street(a1, new float2(560, -45)).Street(new float2(150, -45), new float2(-200, -45));
                    b.Street(new float2(560, -45), new float2(800, -45));
                    b.SplitCurve(new float2(-200, 90), new float2(20, 120), new float2(180, 150), a2, 2, 16f);
                    b.Street(a2, new float2(520, 160)).Street(new float2(520, 160), new float2(780, 170));
                    break;
                }
                case "desire":
                {
                    c.Title = "Town to station: two streets meet the area at opposite corners";
                    c.Expect = "Auto lays a diagonal along the desire line between the two streets (the grid would make people zig-zag); strict does not.";
                    c.Ring = Rect(640, 480);
                    var b = B();
                    // the old town street from the south-west, the station street from the north-east (dead ends at the area)
                    b.Chain(16f, new float2(-260, -170), new float2(-140, -95), new float2(-40, -30));
                    b.Street(new float2(-140, -95), new float2(-140, -230)).Street(new float2(-140, -95), new float2(-300, -60));
                    b.Chain(16f, new float2(900, 680), new float2(780, 600), new float2(680, 530));
                    b.Street(new float2(780, 600), new float2(780, 760)).Street(new float2(780, 600), new float2(950, 560));
                    // a street along the south, one along the west
                    b.Chain(16f, new float2(-60, -50), new float2(120, -50), new float2(300, -50), new float2(480, -50), new float2(700, -50));
                    b.Chain(16f, new float2(-50, -40), new float2(-50, 160), new float2(-50, 360), new float2(-50, 560));
                    break;
                }
                case "bighill":
                {
                    c.Title = "A big area over a 45 m hill (grades to ~20 %)";
                    c.Expect = "The grid gives way: junctions bend, the steepest streets become steps, the steep top a hillside park; strict keeps every street.";
                    c.Ring = Rect(700, 560);
                    c.Height = Hill(new float2(380, 300), 150f, 45f);
                    CityAround(B(), 700, 560, north: false);
                    break;
                }
                default: throw new ArgumentException("unknown case " + id);
            }
            // Existing roads lie on the ground.
            for (int i = 0; i < c.Nets.Count; i++)
            {
                var n = c.Nets[i];
                n.A.y = c.Height(n.A.xz); n.B.y = c.Height(n.B.xz); n.C.y = c.Height(n.C.xz); n.D.y = c.Height(n.D.xz);
                c.Nets[i] = n;
            }
            c.Ring = c.Ring.Select(p => new float3(p.x, c.Height(p.xz), p.z)).ToArray();
            return c;
        }
    }
}
