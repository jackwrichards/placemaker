// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/ParkingLanterns.cs.
// Translated; operates on the layer's Layout instead of PLT's ParkingLayout.
//
// Lanterns stand where stall rows face each other back to back (in the planting between them,
// "double" heads) and along row backs that face the outline or a planting strip ("single" heads).
// Spacing S is clamped to 20–40 m (PLT StandardAbstand 30 m; StreetlightSingle02/Double02 reach 18 m).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class Lanterns
    {
        public const double MinSpacing = 20, MaxSpacing = 40;
        private static readonly double[] Quarters = { 0.25, 0.5, 0.75 };

        private enum Kind { Cap, Median, HeadToHead, Edge, Strip }

        private struct Spot { public double2 Pos; public Kind Kind; public double2 Dir; }

        private sealed class BayRef { public double SMin, SMax, T, Width; }
        private sealed class Run { public double T0, T1; public readonly List<BayRef> Bays = new List<BayRef>(); public bool PartnerAbove, PartnerBelow; }
        private sealed class RowGroup { public double SMin, SMax; public readonly List<BayRef> Bays = new List<BayRef>(); public readonly List<Run> Runs = new List<Run>(); }

        public static void Plan(Layout layout, double spacing, Func<double2, bool> isGreen)
        {
            layout.Lanterns.Clear();
            var bays = new List<Bay>(layout.LiveBays);
            if (bays.Count == 0) return;
            double S = Math.Max(MinSpacing, Math.Min(MaxSpacing, spacing));
            var lanes = new List<(double2 A, double2 B)>();
            foreach (var l in layout.LiveLines)
                if (l.Kind != LineKind.Footpath && l.Kind != LineKind.Walk) lanes.Add((l.A, l.B));
            var outline = layout.Lot;
            var outlineGrid = new RingGrid(outline);   // (perf) by grid cell, same answers
            var laneGrid = new SegmentGrid(lanes, 8);

            bool NearLane(double2 p, double tol) => laneGrid.AnyWithin(p, tol);
            bool AtEdge(double2 p, double near)
            {
                if (outline.Count < 3) return false;
                if (!outlineGrid.Contains(p)) return true;
                return outlineGrid.Dist(p, near + 1) < near;
            }

            // group stalls by axis angle (0.5° tolerance)
            var raw = new List<(double Ang, double2 M01, double2 M23, double Width)>();
            foreach (var b in bays)
            {
                var m01 = (b.Quad[0] + b.Quad[1]) * 0.5;
                var m23 = (b.Quad[2] + b.Quad[3]) * 0.5;
                var d = PGeo.Norm(m23 - m01);
                double ang = Math.Atan2(d.y, d.x);
                if (ang < 0) ang += Math.PI;
                if (ang >= Math.PI - 1e-6) ang -= Math.PI;
                raw.Add((ang, m01, m23, PGeo.Len(b.Quad[0] - b.Quad[1])));
            }
            raw.Sort((a, c) => a.Ang.CompareTo(c.Ang));
            const double tolerance = 0.5 * Math.PI / 180;
            double AngDiff(double a, double c) { double d = Math.Abs(a - c) % Math.PI; return Math.Min(d, Math.PI - d); }
            var groups = new List<(double Ang0, List<(double Ang, double2 M01, double2 M23, double Width)> Bays)>();
            foreach (var b in raw)
            {
                int i = groups.FindIndex(g => AngDiff(g.Ang0, b.Ang) < tolerance);
                if (i >= 0) groups[i].Bays.Add(b);
                else groups.Add((b.Ang, new List<(double, double2, double2, double)> { b }));
            }

            var spots = new List<Spot>();
            foreach (var group in groups)
            {
                double sx = 0, sz = 0, a0 = group.Bays[0].Ang;
                foreach (var b in group.Bays)
                {
                    double a = b.Ang;
                    if (a - a0 > Math.PI / 2) a -= Math.PI;
                    if (a0 - a > Math.PI / 2) a += Math.PI;
                    sx += Math.Cos(a); sz += Math.Sin(a);
                }
                double dl = Math.Sqrt(sx * sx + sz * sz);
                var dc = new double2(sx / dl, sz / dl);   // along the stall axis
                var wc = PGeo.Perp(dc);                    // along the row
                var o = group.Bays[0].M01;
                double2 W(double s, double t) => o + dc * s + wc * t;

                var refs = new List<BayRef>();
                foreach (var b in group.Bays)
                {
                    double s1 = PGeo.Dot(b.M01 - o, dc), s2 = PGeo.Dot(b.M23 - o, dc);
                    refs.Add(new BayRef { SMin = Math.Min(s1, s2), SMax = Math.Max(s1, s2), T = PGeo.Dot((b.M01 + b.M23) * 0.5 - o, wc), Width = b.Width });
                }
                refs.Sort((p, q) => p.SMin.CompareTo(q.SMin));
                var rows = new List<RowGroup>();
                foreach (var b in refs)
                {
                    var r = rows.Find(x => Math.Abs(x.SMin - b.SMin) < 0.3);
                    if (r == null) { r = new RowGroup { SMin = b.SMin, SMax = b.SMax }; rows.Add(r); }
                    r.Bays.Add(b);
                }
                foreach (var r in rows)
                {
                    Run run = null;
                    r.Bays.Sort((p, q) => p.T.CompareTo(q.T));
                    foreach (var b in r.Bays)
                    {
                        double t0 = b.T - b.Width / 2, t1 = b.T + b.Width / 2;
                        if (run != null && t0 - run.T1 < 0.5) { run.T1 = t1; run.Bays.Add(b); }
                        else { run = new Run { T0 = t0, T1 = t1 }; run.Bays.Add(b); r.Runs.Add(run); }
                    }
                }
                // rows back to back (gap ≤ 7 m): a double lantern line between them
                foreach (var A in rows)
                    foreach (var B in rows)
                    {
                        if (A == B) continue;
                        double gap = B.SMin - A.SMax;
                        if (gap < -0.3 || gap > 7) continue;
                        double sMid = (A.SMax + B.SMin) / 2;
                        foreach (var la in A.Runs)
                            foreach (var lb in B.Runs)
                            {
                                double u0 = Math.Max(la.T0, lb.T0), u1 = Math.Min(la.T1, lb.T1);
                                if (u1 - u0 < 2.5) continue; // at least one stall; 3.0 sat exactly on the edge
                                if (gap > 0.5 && NearLane(W(sMid, (u0 + u1) / 2), 1.5)) continue;
                                la.PartnerAbove = true; lb.PartnerBelow = true;
                                PairLine(spots, la, lb, sMid, gap > 0.5, S, dc, W, isGreen, NearLane);
                            }
                    }
                // row backs facing the outline or a planting strip: single lanterns
                foreach (var r in rows)
                    foreach (var run in r.Runs)
                        for (int side = 0; side < 2; side++)
                        {
                            double sEdge = side == 0 ? r.SMax : r.SMin;
                            double d = side == 0 ? 1 : -1;
                            bool covered = side == 0 ? run.PartnerAbove : run.PartnerBelow;
                            if (covered) continue;
                            double t0 = run.T0, t1 = run.T1, tm = (t0 + t1) / 2;
                            int behind = 0;
                            foreach (var f in Quarters)
                            {
                                var q = W(sEdge + d * 0.4, t0 + (t1 - t0) * f);
                                if (isGreen(q) || AtEdge(q, 3)) behind++;
                            }
                            if (behind < 2) continue;
                            double depth = 0;
                            for (int k = 1; k <= 160; k++) { if (!isGreen(W(sEdge + d * (k * 0.25 - 0.125), tm))) break; depth = k * 0.25; }
                            double s = sEdge + d * (depth >= 1 ? Math.Min(depth / 2, 2) : 0.5);
                            bool outer = AtEdge(W(sEdge + d * (depth + 0.5), tm), 2);
                            double L = t1 - t0;
                            var at = new List<double>();
                            if (L < S * 0.6 + 0.1) at.Add(t0 + L / 2);
                            else
                            {
                                int m = Math.Max(1, (int)Math.Ceiling((L - 3) / S - 0.01));
                                for (int k = 0; k <= m; k++) at.Add(t0 + 1.5 + (L - 3) * k / m);
                            }
                            foreach (var t in at)
                                spots.Add(outer
                                    ? new Spot { Pos = W(s, t), Kind = Kind.Edge, Dir = -d * dc }
                                    : new Spot { Pos = W(s, t), Kind = Kind.Strip, Dir = dc });
                        }
            }

            // merge inner lanterns closer than 5 m (cap wins), thin edge lanterns
            var inner = new List<Spot>();
            foreach (var lt in spots)
            {
                if (lt.Kind == Kind.Edge) continue;
                int i = inner.FindIndex(f => math.distance(f.Pos, lt.Pos) < 5);
                if (i >= 0) { if (lt.Kind == Kind.Cap) inner[i] = lt; continue; }
                inner.Add(lt);
            }
            var edge = new List<Spot>();
            double edgeGap = Math.Max(4, 0.45 * S - 0.1);
            foreach (var lt in spots)
            {
                if (lt.Kind != Kind.Edge) continue;
                if (edge.Exists(f => math.distance(f.Pos, lt.Pos) < edgeGap)) continue;
                edge.Add(lt);
            }
            foreach (var lt in inner) layout.Lanterns.Add(new Lantern { Position = lt.Pos, Facing = lt.Dir, Double = true });
            foreach (var lt in edge) layout.Lanterns.Add(new Lantern { Position = lt.Pos, Facing = lt.Dir, Double = false });
        }

        private static void PairLine(List<Spot> spots, Run la, Run lb, double sMid, bool withMedian,
            double S, double2 dc, Func<double, double, double2> W, Func<double2, bool> isGreen, Func<double2, double, bool> nearLane)
        {
            var innerKind = withMedian ? Kind.Median : Kind.HeadToHead;
            double t0 = Math.Min(la.T0, lb.T0), t1 = Math.Max(la.T1, lb.T1);
            var edges = new List<double>();
            foreach (var b in la.Bays) { edges.Add(b.T - b.Width / 2); edges.Add(b.T + b.Width / 2); }
            foreach (var b in lb.Bays) { edges.Add(b.T - b.Width / 2); edges.Add(b.T + b.Width / 2); }
            edges.Sort();
            for (int i = edges.Count - 1; i > 0; i--) if (edges[i] - edges[i - 1] <= 0.05) edges.RemoveAt(i);
            double CapLength(double t, double dir)
            {
                double lg = 0;
                for (int k = 1; k <= 48; k++)
                {
                    // half step: boundaries often sit on multiples of 0.25
                    var q = W(sMid, t + dir * (k * 0.25 - 0.125));
                    if (!isGreen(q) || nearLane(q, 2.5)) break;
                    lg = k * 0.25;
                }
                return lg;
            }
            double lgA = CapLength(t0, -1), lgB = CapLength(t1, +1);
            var e0 = lgA >= 1.5 ? (T: t0 - Math.Min(lgA / 2, 3), K: Kind.Cap) : (T: edges.Count > 1 ? edges[1] : t0 + 3, K: innerKind);
            var e1 = lgB >= 1.5 ? (T: t1 + Math.Min(lgB / 2, 3), K: Kind.Cap) : (T: edges.Count > 1 ? edges[edges.Count - 2] : t1 - 3, K: innerKind);
            double L = e1.T - e0.T;
            int n = Math.Max(0, (int)Math.Ceiling(L / S - 0.01) - 1); // L exactly k·S must not tip over
            var ins = new List<double>();
            for (int k = 1; k <= n; k++)
            {
                double target = e0.T + L * k / (n + 1);
                double t = edges[0];
                foreach (var x in edges) if (Math.Abs(x - target) < Math.Abs(t - target) - 0.05) t = x; // tie: the earlier boundary
                if (!ins.Exists(x => Math.Abs(x - t) < 1) && Math.Abs(t - e0.T) > 4 && Math.Abs(t - e1.T) > 4) ins.Add(t);
            }
            spots.Add(new Spot { Pos = W(sMid, e0.T), Kind = e0.K, Dir = dc });
            foreach (var t in ins) spots.Add(new Spot { Pos = W(sMid, t), Kind = innerKind, Dir = dc });
            spots.Add(new Spot { Pos = W(sMid, e1.T), Kind = e1.K, Dir = dc });
        }
    }
}
