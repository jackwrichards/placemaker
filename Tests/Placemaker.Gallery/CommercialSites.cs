// (s50) Commercial review sites for the gallery (--sites square,halfcurved,curved): the same blocks as
// Tests/Placemaker.Tests/CommercialIrregularSiteTests (streets as cubic pieces round the block, the streets' zone
// blocks captured as in the game). Keep the two in step.
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Gallery
{
    public static class CommercialSites
    {
        public static readonly string[] Kinds = { "square", "halfcurved", "curved" };

        /// <summary>The ring and the existing streets (with captured zone blocks) of a review site; square sites take the job's area.</summary>
        public static float3[] Make(string kind, float area, out SiteContext site)
        {
            float3[] ring;
            // (shops2) The game runner's land made to order (Show.ps1 -Shape/-Size): "land-<shape>-<W>x<D>", e.g. land-l-280x260,
            // land-rect-300x280, land-circle-260x260: the outline as SuitePlot draws it, a Small Road ring 16 m outside its box.
            if (kind.StartsWith("land-")) return Land(kind, out site);
            if (kind.StartsWith("gblock-")) return GameBlock(kind, out site);
            switch (kind)
            {
                case "curved": ring = Round(out site); break;
                case "halfcurved": ring = Block(out site); break;
                default:
                {
                    float a = math.sqrt(area);
                    ring = new[] { new float3(0, 0, 0), new float3(a, 0, 0), new float3(a, 0, a), new float3(0, 0, a) };
                    site = new SiteContext();
                    for (int i = 0; i < ring.Length; i++)
                    {
                        float3 p0 = ring[i], p1 = ring[(i + 1) % ring.Length];
                        float3 d = math.normalize(p1 - p0);
                        float3 n = new float3(d.z, 0, -d.x) * 8f;
                        float3 s = p0 - d * 20f + n, e = p1 + d * 20f + n;
                        site.NearbyNets.Add(new NearbyNet { ExternalId = 500 + i, A = s, B = math.lerp(s, e, 1f / 3f), C = math.lerp(s, e, 2f / 3f), D = e, Width = 12f, Category = NetCategory.Road, HasSidewalk = true });
                    }
                    foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
                    break;
                }
            }
            return ring;
        }

        private static float3[] Land(string kind, out SiteContext site)
        {
            var parts = kind.Split('-');
            var wd = parts[2].Split('x');
            // land-<shape>-<W>x<D>[-<gap>][-f]: gap from the outline to the road centre (8: the outline 2 m off the kerb, as the bucket draws it); f: the road follows the outline
            bool follow = parts.Contains("f");
            var gapPart = parts.Skip(3).FirstOrDefault(x => x != "f");
            var plot = new Placemaker.Core.Suite.SuitePlot { RoadsFollow = follow, Gap = gapPart != null ? float.Parse(gapPart, System.Globalization.CultureInfo.InvariantCulture) : 8f, Shape = parts[1], Size = new float2(float.Parse(wd[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(wd[1], System.Globalization.CultureInfo.InvariantCulture)) };
            // (shops4) land-tri-… and land-odd-…: the game runner's sharp-cornered test outlines (.forensics/shops4/gen.js)
            if (plot.Shape == "tri") { plot.Shape = "points"; plot.Points = new[] { new float2(0, 0), new float2(130, 0), new float2(15, 95) }; plot.SizeFromPoints(); plot.RoadsFollow = true; }
            if (plot.Shape == "odd") { plot.Shape = "points"; plot.Points = new[] { new float2(0, 0), new float2(100, -10), new float2(140, 50), new float2(60, 60), new float2(70, 110), new float2(-10, 80) }; plot.SizeFromPoints(); plot.RoadsFollow = true; }
            site = new SiteContext();
            long id = 700;
            foreach (var seg in plot.RingRoads()) Line(site, ref id, seg.A, seg.B, 12f);
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            return plot.Outline().Select(p => new float3(p.x, 0, p.y)).ToArray();
        }

        /// <summary>
        /// (rf-shops3) A runner's grid block as the bucket fills it: "gblock-<W>x<D>-<wW>_<wS>_<wE>_<wN>" (road centre lines W x D apart,
        /// each street's width; e.g. gblock-150x140-20_20_12_12 is the suite's B4-0, Medium Roads west and south), the outline
        /// 2 m off every kerb with its corners cut 5 m (eight corners, as the bucket draws round the kerb returns).
        /// </summary>
        private static float3[] GameBlock(string kind, out SiteContext site)
        {
            var parts = kind.Split('-');
            var wd = parts[1].Split('x');
            var ws = parts[2].Split('_').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            float W = float.Parse(wd[0], System.Globalization.CultureInfo.InvariantCulture), D = float.Parse(wd[1], System.Globalization.CultureInfo.InvariantCulture);
            site = new SiteContext();
            long id = 800;
            // centre lines: west x=0, south y=0, east x=W, north y=D (each running 40 m past the block)
            Line(site, ref id, new float2(0, -40), new float2(0, D + 40), ws[0]);
            Line(site, ref id, new float2(-40, 0), new float2(W + 40, 0), ws[1]);
            Line(site, ref id, new float2(W, -40), new float2(W, D + 40), ws[2]);
            Line(site, ref id, new float2(-40, D), new float2(W + 40, D), ws[3]);
            float x0 = ws[0] * 0.5f + 2f, y0 = ws[1] * 0.5f + 2f, x1 = W - ws[2] * 0.5f - 2f, y1 = D - ws[3] * 0.5f - 2f, c = 5f;
            var ring = new[]
            {
                new float2(x0 + c, y0), new float2(x1 - c, y0), new float2(x1, y0 + c), new float2(x1, y1 - c),
                new float2(x1 - c, y1), new float2(x0 + c, y1), new float2(x0, y1 - c), new float2(x0, y0 + c),
            };
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var gc = g; gc.Predicted = false; gc.Existing = false; site.ZoneGrids.Add(gc); }
            return ring.Select(p => new float3(p.x, 0, p.y)).ToArray();
        }

        private static NearbyNet Net(long id,float2 a, float2 b, float2 c, float2 d, float w) => new NearbyNet
        {
            ExternalId = id, A = new float3(a.x, 0, a.y), B = new float3(b.x, 0, b.y), C = new float3(c.x, 0, c.y), D = new float3(d.x, 0, d.y),
            Width = w, Category = NetCategory.Road, HasSidewalk = true,
        };

        private static float2 Pol(float2 c, float r, float deg) => c + new float2(math.cos(math.radians(deg)), math.sin(math.radians(deg))) * r;

        /// <summary>Circular-arc street as cubic pieces (about 25 degrees each).</summary>
        private static void Arc(SiteContext site, ref long id, float2 c, float r, float a0, float a1, float w)
        {
            int n = math.max(1, (int)math.ceil(math.abs(a1 - a0) / 25f));
            for (int i = 0; i < n; i++)
            {
                float s = math.lerp(a0, a1, (float)i / n), e = math.lerp(a0, a1, (float)(i + 1) / n);
                float k = 4f / 3f * math.tan(math.radians(e - s) / 4f) * r;
                float2 p0 = Pol(c, r, s), p3 = Pol(c, r, e);
                float2 t0 = new float2(-math.sin(math.radians(s)), math.cos(math.radians(s))), t1 = new float2(-math.sin(math.radians(e)), math.cos(math.radians(e)));
                site.NearbyNets.Add(Net(id++, p0, p0 + t0 * k, p3 - t1 * k, p3, w));
            }
        }

        private static void Line(SiteContext site, ref long id, float2 a, float2 b, float w) =>
            site.NearbyNets.Add(Net(id++, a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b, w));

        /// <summary>
        /// An irregular block of about 3 ha: an angled straight street on the south and the west, a convex curved street on
        /// the east (R 150) and a concave one on the north (R 190); the outline sits 2 m off the kerbs.
        /// </summary>
        public static float3[] Block(out SiteContext site, float step = 6f)
        {
            site = new SiteContext();
            long id = 900;
            float w = 12f, off = w * 0.5f + 2f;
            // Street centre lines (counter-clockwise round the block): south, east arc, north arc, west.
            float2 sw = new float2(-100, -70), se = new float2(5, -88);
            float2 c1 = new float2(-60, 0); float r1 = 150f;              // east arc: centre west of the block, convex outwards
            float2 c2 = new float2(30, 250f); float r2 = 190f;            // north arc: centre far north, concave side faces the block
            float2 nw = new float2(-105, 78);
            Line(site, ref id, sw - math.normalize(se - sw) * 25f, se, w);
            float a0 = math.degrees(math.atan2(se.y - c1.y, se.x - c1.x));
            float2 ne = Pol(c1, r1, 30f);
            Arc(site, ref id, c1, r1, a0, 30f, w);
            // North street from ne towards nw as an arc about c2 (approximated through its end points).
            float aN0 = math.degrees(math.atan2(ne.y - c2.y, ne.x - c2.x)), aN1 = math.degrees(math.atan2(nw.y - c2.y, nw.x - c2.x));
            if (aN1 < aN0) aN1 += 360f;
            // aN0 > aN1 as seen from c2 (the street runs west); use a descending sweep.
            aN1 = math.degrees(math.atan2(nw.y - c2.y, nw.x - c2.x));
            Arc(site, ref id, c2, math.distance(ne, c2), aN0, aN1, w);
            Line(site, ref id, nw, sw - math.normalize(nw - sw) * 25f, w);
            // The outline: inset the street polygon by `off`.
            var pts = new List<float2>();
            pts.Add(sw);
            for (float a = a0; a <= 30.01f; a += step) pts.Add(Pol(c1, r1, a));
            float rn = math.distance(ne, c2);
            int nn = (int)(36f / step); for (int i = 1; i <= nn; i++) pts.Add(Pol(c2, rn, math.lerp(aN0, aN1, (float)i / nn)));
            pts.Add(nw);
            var poly = Poly.EnsureCcw(pts.ToArray());
            var inset = Poly.Offset(poly, off).OrderByDescending(q => Poly.Area(q)).First();
            var ring = inset.Select(p => new float3(p.x, 0, p.y)).ToArray();
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            return ring;
        }

        /// <summary>
        /// A fully curved irregular block (~2.9 ha, like the playtest's between Edgewater and Honeysuckle Street): the
        /// outline is a smooth closed curve sampled every ~<paramref name="spacing"/> m, ringed by streets 8 m outside it
        /// (each a chain of cubic pieces through the curve, 20 m apart).
        /// </summary>
        public static float3[] Round(out SiteContext site, float spacing = 5f)
        {
            site = new SiteContext();
            float R(float t) => 95f + 22f * math.cos(2f * t) + 11f * math.sin(3f * t + 0.6f);
            float2 P(float t, float off) => new float2(math.cos(t), math.sin(t)) * (R(t) + off) + new float2(5f, -4f);
            var ring = new List<float3>();
            float perim = 0f, tt = 0f; float2 prev = P(0, 0);
            for (int i = 1; i <= 720; i++) { float t = i * math.PI * 2f / 720f; var q = P(t, 0); perim += math.distance(prev, q); prev = q; }
            int n = (int)(perim / spacing);
            for (int i = 0; i < n; i++) { var p = P(i * math.PI * 2f / n, 0); ring.Add(new float3(p.x, 0, p.y)); }
            long id = 950;
            int pieces = 24;
            float2 Tan(float t) => (P(t + 0.001f, 8f) - P(t - 0.001f, 8f)) / 0.002f;
            for (int i = 0; i < pieces; i++)
            {
                float t0 = i * math.PI * 2f / pieces, t1 = (i + 1) * math.PI * 2f / pieces;
                float2 p0 = P(t0, 8f), p3 = P(t1, 8f);
                float dt = t1 - t0;
                site.NearbyNets.Add(Net(id++, p0, p0 + Tan(t0) * dt / 3f, p3 - Tan(t1) * dt / 3f, p3, 12f));
            }
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            return ring.ToArray();
        }

    }
}
