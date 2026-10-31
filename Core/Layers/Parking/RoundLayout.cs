// P2-5 Parking looks: the round car park (the "Shape: round" setting; docs/runbooks/parking-looks.md).
//
// A circle inside the outline (the largest one round the outline's middle, less the setback): from the outside in, a row of
// spaces facing out, a ring lane, a row facing in, a planted ring median, and again, down to a planted centre island.
// Spaces stand radially; each row's pitch is set at its narrow side (the outer row's mouth, the inner row's back) so no
// two spaces overlap. The ring lanes are straight pieces of about 9 m (nets are straight); spokes join the rings to each
// other and the entrances to the outer ring. The planting outside the circle is the city's own ground; the lot plants a
// ring of trees round the circle and a grove on the island.
//
// The output is a Layout like the row engine's (frame = the circle's centre, world axes), so everything downstream
// (surfaces, kerbs, planting lines, lanterns, ground, equipment) works on it unchanged.

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class RoundLayout
    {
        /// <summary>Straight ring pieces of about this length (m).</summary>
        public const double Piece = 9.0;

        public static Layout Build(LayoutRequest rq, CancellationToken cancel)
        {
            var s = rq.Settings;
            var lotW = PGeo.EnsureCcw(new List<double2>(rq.LotWorld));
            var c = PGeo.Centroid(lotW);
            var l = new Layout { Frame = new Frame(c, new double2(1, 0)) };
            foreach (var p in lotW) l.Lot.Add(p - c);
            l.LotArea = PGeo.Area(l.Lot);
            if (!PGeo.Contains(l.Lot, double2.zero)) { l.Issues.Add(new LayoutIssue(IssueSeverity.Warning, "Placemaker.Parking.Issue.NoStalls")); return l; }
            double R = PGeo.DistToBoundary(l.Lot, double2.zero) - Math.Max(1.0, s.Setback);
            double L = s.StallLength, W = s.StallWidth, A = s.Aisle, M = Math.Max(1.5, s.Median > 0 ? s.Median : 2.5);
            double module = 2 * L + A;
            if (R < module + 5) { l.Issues.Add(new LayoutIssue(IssueSeverity.Warning, "Placemaker.Parking.Issue.NoStalls")); return l; }

            // ring lanes from the outside in, while a centre island of at least 5 m radius is left
            var rings = new List<double>();
            for (double outer = R; outer - module >= 5; outer -= module + M) rings.Add(outer - L - A * 0.5);
            int line = 0;
            double2 P(double r, double a) => new double2(Math.Cos(a), Math.Sin(a)) * r;
            var ringPts = new List<double2[]>();
            foreach (double ra in rings)
            {
                int n = Math.Max(12, (int)Math.Ceiling(2 * Math.PI * ra / Piece));
                var pts = new double2[n];
                for (int i = 0; i < n; i++) pts[i] = P(ra, 2 * Math.PI * i / n);
                ringPts.Add(pts);
                for (int i = 0; i < n; i++)
                    l.Lines.Add(new DriveLine { Id = line++, A = pts[i], B = pts[(i + 1) % n], Kind = LineKind.Aisle, Width = A });
                l.AisleLength += 2 * Math.PI * ra;
            }
            cancel.ThrowIfCancellationRequested();

            // entrances: to the outer ring's nearest vertex, straight in
            var spokesAt = new List<double>();
            foreach (var e in rq.Entrances)
            {
                var local = e.World - c;
                var onEdge = ClosestOnRing(l.Lot, local, out int edge, out double along);
                var inward = PGeo.Norm(-onEdge);
                double width = e.Kind == EntranceKind.Footpath ? ParkingSettings.FootpathWidth : EntranceKinds.OneWay(e.Kind) ? ParkingSettings.OneWayWidth : A;
                var ep = new EntrancePlan { Kind = e.Kind, Point = onEdge, Inward = inward, Edge = edge, Along = along, Width = width };
                var outer = ringPts[0];
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < outer.Length; i++) { double d = PGeo.Len(outer[i] - onEdge); if (d < bd) { bd = d; best = i; } }
                ep.Junction = outer[best];
                var kind = e.Kind == EntranceKind.Footpath ? LineKind.Footpath : LineKind.Connector;
                var dl = new DriveLine { Id = line++, A = onEdge, B = ep.Junction, Kind = kind, Width = width, OneWay = EntranceKinds.OneWay(e.Kind), Entrance = l.Entrances.Count };
                l.Lines.Add(dl);
                if (kind == LineKind.Footpath) ep.FootLine = dl.Id; else ep.ConnectorLine = dl.Id;
                ep.Connected = true;
                l.Entrances.Add(ep);
                spokesAt.Add(Math.Atan2(ep.Junction.y, ep.Junction.x));
            }
            // spokes between the rings: at the entrances' angles and opposite them (at least two)
            if (spokesAt.Count == 0) spokesAt.Add(0);
            var angles = new List<double>(spokesAt);
            foreach (var a in spokesAt) angles.Add(a + Math.PI);
            for (int k = 0; k + 1 < ringPts.Count; k++)
                foreach (var a in Distinct(angles, 0.35))
                {
                    var p0 = Nearest(ringPts[k], a); var p1 = Nearest(ringPts[k + 1], a);
                    l.Lines.Add(new DriveLine { Id = line++, A = p0, B = p1, Kind = LineKind.Cross, Width = ParkingSettings.CrossWidth });
                }

            // the rows: outer (facing out) and inner (facing in) of every ring lane
            int row = 0, bayIndex = 0;
            foreach (double ra in rings)
            {
                foreach (int side in new[] { 1, -1 })
                {
                    double mouth = ra + side * A * 0.5, back = mouth + side * L, mid = (mouth + back) * 0.5;
                    double narrow = Math.Min(mouth, back);
                    if (narrow < 2) continue;
                    double step = W / narrow;
                    int n = (int)Math.Floor(2 * Math.PI / step);
                    double a0 = 0.5 * (2 * Math.PI - n * step) / Math.Max(1, n);
                    for (int i = 0; i < n; i++)
                    {
                        double a = a0 + i * (2 * Math.PI / n);
                        var axis = new double2(Math.Cos(a), Math.Sin(a)) * side;      // aisle -> back
                        var rowDir = new double2(-Math.Sin(a), Math.Cos(a));
                        var center = P(mid, a);
                        var q = new[]
                        {
                            center - axis * (L * 0.5) - rowDir * (W * 0.5), center - axis * (L * 0.5) + rowDir * (W * 0.5),
                            center + axis * (L * 0.5) + rowDir * (W * 0.5), center + axis * (L * 0.5) - rowDir * (W * 0.5),
                        };
                        if (!PGeo.Contains(l.Lot, center) || q.Length != 4) continue;
                        bool inLot = true;
                        foreach (var v in q) if (!PGeo.Contains(l.Lot, v)) { inLot = false; break; }
                        if (!inLot) continue;
                        var bay = new Bay { Quad = q, Center = center, Axis = axis, RowDir = rowDir, Width = W, Length = L, Kind = BayKind.Inner, Role = BayRole.Normal, Row = row, Order = i, Line = -1 };
                        // keep clear of connectors and spokes
                        bool clash = false;
                        foreach (var dl in l.Lines)
                        {
                            if (dl.Kind == LineKind.Aisle) continue;
                            foreach (var v in q) if (PGeo.DistPointSeg(v, dl.A, dl.B) < dl.Width * 0.5 + 0.2) { clash = true; break; }
                            if (!clash && PGeo.DistPointSeg(center, dl.A, dl.B) < dl.Width * 0.5 + W * 0.5) clash = true;
                            if (clash) break;
                        }
                        if (clash || rq.Blocked(l.Frame.ToWorld(center), W * 0.5)) continue;
                        l.Bays.Add(bay);
                        bayIndex++;
                    }
                    row++;
                }
            }
            cancel.ThrowIfCancellationRequested();

            // surfaces: the paved annulus of each module (sectors), the ring medians and the centre island planted
            for (int k = 0; k < rings.Count; k++)
            {
                double ro = rings[k] + A * 0.5 + L, ri = rings[k] - A * 0.5 - L;
                AddAnnulus(l, ri, ro, true);
                if (k + 1 < rings.Count) AddAnnulus(l, rings[k + 1] + A * 0.5 + L, ri, false);
                else AddDisc(l, ri, false);
            }
            if (l.Bays.Count == 0) l.Issues.Add(new LayoutIssue(IssueSeverity.Warning, "Placemaker.Parking.Issue.NoStalls"));
            l.RowAngleDeg = 0;
            SpecialStalls.Assign(l, s, p => true);
            return l;
        }

        /// <summary>A ring of trees round the circle (on the city's ground outside it) and a grove on the centre island.</summary>
        public static void RingTrees(Layout l, IReadOnlyList<Species> species, uint seed)
        {
            int tree = -1;
            for (int i = 0; i < species.Count; i++) if (species[i].Tree) { tree = i; break; }
            if (tree < 0) return;
            double rOut = 0, rIn = double.MaxValue;
            foreach (var b in l.LiveBays) foreach (var v in b.Quad) { double d = PGeo.Len(v); rOut = Math.Max(rOut, d); rIn = Math.Min(rIn, d); }
            if (rOut <= 0) return;
            double spacing = Math.Max(7, species[tree].Spacing * 0.9);
            bool Clear(double2 p)
            {
                if (!PGeo.Contains(l.Lot, p) || PGeo.DistToBoundary(l.Lot, p) < 1.5) return false;
                foreach (var dl in l.LiveLines) if (PGeo.DistPointSeg(p, dl.A, dl.B) < dl.Width * 0.5 + 2) return false;
                foreach (var q in l.Plants) if (q.Tree && PGeo.Len(q.Position - p) < spacing * 0.8) return false;
                return true;
            }
            uint n = 0;
            double r1 = rOut + 3.5;
            int count = Math.Max(8, (int)(2 * Math.PI * r1 / spacing));
            for (int i = 0; i < count; i++)
            {
                double a = 2 * Math.PI * i / count;
                var p = new double2(Math.Cos(a), Math.Sin(a)) * r1;
                if (Clear(p)) l.Plants.Add(new Plant { Position = p, Tree = true, Species = tree, Seed = Common.Hashing.Mix(seed, (int)n++, 0x7ED), Row = true });
            }
            // the island: a ring of trees inside its edge and one in the middle
            if (rIn > 6)
            {
                double r2 = Math.Max(0, rIn - 3.5);
                int ci = Math.Max(4, (int)(2 * Math.PI * r2 / spacing));
                for (int i = 0; i < ci && r2 > 3; i++)
                {
                    double a = 2 * Math.PI * (i + 0.5) / ci;
                    var p = new double2(Math.Cos(a), Math.Sin(a)) * r2;
                    if (Clear(p)) l.Plants.Add(new Plant { Position = p, Tree = true, Species = tree, Seed = Common.Hashing.Mix(seed, (int)n++, 0x7ED), Row = true });
                }
                if (Clear(double2.zero)) l.Plants.Add(new Plant { Position = double2.zero, Tree = true, Species = tree, Seed = Common.Hashing.Mix(seed, (int)n++, 0x7ED), Row = true });
            }
        }

        private static IEnumerable<double> Distinct(List<double> angles, double minGap)
        {
            var r = new List<double>();
            foreach (var a in angles)
            {
                bool near = false;
                foreach (var b in r) { double d = Math.Abs(Math.IEEERemainder(a - b, 2 * Math.PI)); if (d < minGap) { near = true; break; } }
                if (!near) r.Add(a);
            }
            return r;
        }

        private static double2 Nearest(double2[] ring, double angle)
        {
            var dir = new double2(Math.Cos(angle), Math.Sin(angle));
            double2 best = ring[0]; double bd = double.MinValue;
            foreach (var p in ring) { double d = PGeo.Dot(PGeo.Norm(p), dir); if (d > bd) { bd = d; best = p; } }
            return best;
        }

        private static double2 ClosestOnRing(List<double2> ring, double2 p, out int edge, out double along)
        {
            edge = 0; along = 0; double2 best = ring[0]; double bd = double.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                var q = PGeo.ClosestOnSeg(p, ring[i], ring[(i + 1) % ring.Count], out double t);
                double d = PGeo.Len(q - p);
                if (d < bd) { bd = d; best = q; edge = i; along = t * PGeo.Len(ring[(i + 1) % ring.Count] - ring[i]); }
            }
            return best;
        }

        /// <summary>An annulus as sector polygons (each a simple ring, ~10 m of arc on the outside).</summary>
        private static void AddAnnulus(Layout l, double ri, double ro, bool asphalt)
        {
            if (ro - ri < 0.4) return;
            int n = Math.Max(8, (int)Math.Ceiling(2 * Math.PI * ro / 30.0));
            int sub = Math.Max(2, (int)Math.Ceiling(2 * Math.PI * ro / n / 3.0));
            for (int k = 0; k < n; k++)
            {
                double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
                var ring = new List<double2>();
                for (int j = 0; j <= sub; j++) { double a = a0 + (a1 - a0) * j / sub; ring.Add(new double2(Math.Cos(a), Math.Sin(a)) * ro); }
                for (int j = sub; j >= 0; j--) { double a = a0 + (a1 - a0) * j / sub; ring.Add(new double2(Math.Cos(a), Math.Sin(a)) * Math.Max(0.01, ri)); }
                l.Surfaces.Add(new SurfacePoly { Ring = PGeo.EnsureCcw(ring), Asphalt = asphalt });
            }
        }

        private static void AddDisc(Layout l, double r, bool asphalt)
        {
            if (r < 1) return;
            int n = Math.Max(12, (int)Math.Ceiling(2 * Math.PI * r / 3.0));
            var ring = new List<double2>();
            for (int i = 0; i < n; i++) { double a = 2 * Math.PI * i / n; ring.Add(new double2(Math.Cos(a), Math.Sin(a)) * r); }
            l.Surfaces.Add(new SurfacePoly { Ring = ring, Asphalt = asphalt });
        }
    }
}
