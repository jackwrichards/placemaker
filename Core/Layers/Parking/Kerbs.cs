// P2-5 Parking looks: raised kerbs round every island, median, planter and the lot's paved edge (docs/runbooks/parking-looks.md).
//
// The game has no kerb prop or kerbed net (asset research 2026-10-31: no vanilla curb, no kerbed pedestrian piece; the
// road kerbs are part of the road meshes). Our plazas already make real stone edges from the game's garden walls sunk
// into the ground until a lip shows (PlazaDesigns.Kit.Wall: the theatre's tiers, the stair nosings). A kerb is the same:
// a 0.2 m wall run along the edge, sunk until 0.14-0.16 m shows, its face flush with the asphalt. It casts a real shadow,
// has a real height step, bulldozes with the build (a prop of the group) and, set into the ground with the game's
// negative Elevation, collides with nothing (Declutter leaves it alone).
//
// Edges: every edge of a planted surface whose outside is asphalt inside the lot, and every edge of the lot outline
// whose inside is asphalt. Kerbs drop (no wall) where a drive connector, a footpath or a drawn walkway crosses, and at the
// back of an accessible stall (a dropped kerb, with tactile paving painted by ParkingGround). On a slope the pieces get
// shorter so their ends never stand proud of or sink under the graded ground by more than a few centimetres.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    /// <summary>One straight run of kerb in world space: the line of its asphalt face, the asphalt side's normal.</summary>
    internal struct KerbRun
    {
        public double2 A, B;
        /// <summary>Unit normal pointing from the kerb onto the asphalt.</summary>
        public double2 Out;
        /// <summary>True on the lot's outline (no planting behind it).</summary>
        public bool Outline;
        /// <summary>Dropped (flush) here: a crossing or an accessible stall.</summary>
        public bool Dropped;
    }

    internal static class Kerbs
    {
        /// <summary>The garden walls' size (lab, 2026-10-31): 0.2 m thick, 0.9 m tall, along local Z.</summary>
        public const float WallH = 0.9f, WallT = 0.2f;

        /// <summary>The wall family per kerb kind (dark masonry reads as granite, the pale walls as concrete).</summary>
        public static string WallFor(KerbKind k)
        {
            switch (k)
            {
                case KerbKind.Granite: return "EU_Wall05";
                case KerbKind.None: case KerbKind.Flush: return null;
                default: return "EU_Wall02";
            }
        }

        /// <summary>How much of the kerb shows above the asphalt (m).</summary>
        public static float Show(KerbKind k) => k == KerbKind.Granite ? 0.16f : 0.14f;

        /// <summary>
        /// The kerb runs of a layout (world space). <paramref name="isGreen"/> takes frame-local points. Dropped stretches are
        /// returned as their own runs (Dropped = true) so the ground can paint them.
        /// </summary>
        public static List<KerbRun> Find(Layout layout, Func<double2, bool> isGreen)
        {
            var f = layout.Frame;
            var runs = new List<KerbRun>();
            if (layout.Lot == null || layout.Lot.Count < 3) return runs;
            // openings: connectors, footpaths and drawn walkways, as local segments with their half width + margin
            var gaps = new List<(double2 a, double2 b, double r)>();
            foreach (var l in layout.LiveLines)
                if (l.Kind == LineKind.Connector || l.Kind == LineKind.Footpath || l.Kind == LineKind.Walk)
                    gaps.Add((l.A, l.B, l.Width * 0.5 + (l.Kind == LineKind.Connector ? 1.2 : 0.6)));
            // dropped kerbs at the back of accessible stalls
            var drops = new List<(double2 a, double2 b)>();
            foreach (var b in layout.LiveBays)
                if (b.Role == BayRole.Disabled && b.Quad != null && b.Quad.Length >= 4) drops.Add((b.Quad[2], b.Quad[3]));

            bool InLot(double2 p) => PGeo.Contains(layout.Lot, p);
            void Edge(double2 a, double2 b, double2 outward, bool outline)
            {
                double len = PGeo.Len(b - a);
                if (len < 0.6) return;
                var d = (b - a) / len;
                // walk the edge in 0.5 m steps, splitting where an opening or a drop starts or ends
                const double step = 0.5;
                int n = Math.Max(1, (int)Math.Ceiling(len / step));
                int state = -1; double start = 0;
                for (int i = 0; i <= n; i++)
                {
                    double t = Math.Min(len, i * step);
                    var p = a + d * t;
                    int s = 0;
                    foreach (var g in gaps) if (PGeo.DistPointSeg(p, g.a, g.b) < g.r) { s = 2; break; }
                    if (s == 0) foreach (var dr in drops) if (PGeo.DistPointSeg(p, dr.a, dr.b) < 0.6) { s = 1; break; }
                    if (i == 0) { state = s; start = 0; continue; }
                    if (s != state || i == n)
                    {
                        double end = s != state ? Math.Max(start, t - step * 0.5) : t;
                        if (state != 2 && end - start > 0.4)
                            runs.Add(new KerbRun { A = f.ToWorld(a + d * start), B = f.ToWorld(a + d * end), Out = f.DirToWorld(outward), Outline = outline, Dropped = state == 1 });
                        state = s; start = end;
                    }
                }
            }

            // planted surfaces against asphalt
            foreach (var sp in layout.Surfaces)
            {
                if (sp.Asphalt || sp.Ring == null || sp.Ring.Count < 3) continue;
                var ring = Collinear(sp.Ring);
                bool ccw = PGeo.SignedArea(ring) > 0;
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                    var d = PGeo.Norm(b - a);
                    var outward = ccw ? new double2(d.y, -d.x) : new double2(-d.y, d.x);
                    // probe along the edge: the stretch whose outside is asphalt in the lot
                    SplitByProbe(a, b, outward, p => InLot(p) && !isGreen(p), (x, y) => Edge(x, y, outward, false));
                }
            }
            // the lot's own paved edge (the outline is CCW in the frame: inward is the left normal)
            var lot = layout.Lot;
            bool lotCcw = PGeo.SignedArea(lot) > 0;
            for (int i = 0; i < lot.Count; i++)
            {
                var a = lot[i]; var b = lot[(i + 1) % lot.Count];
                var d = PGeo.Norm(b - a);
                var inward = lotCcw ? PGeo.Perp(d) : -PGeo.Perp(d);
                SplitByProbe(a, b, inward, p => !isGreen(p) && InLot(p), (x, y) => Edge(x, y, inward, true));
            }
            return runs;
        }

        /// <summary>Calls <paramref name="emit"/> for every stretch of a-b whose probe 0.45 m along <paramref name="side"/> passes.</summary>
        private static void SplitByProbe(double2 a, double2 b, double2 side, Func<double2, bool> ok, Action<double2, double2> emit)
        {
            double len = PGeo.Len(b - a);
            if (len < 0.6) return;
            var d = (b - a) / len;
            const double step = 0.5;
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            bool on = false; double s0 = 0;
            for (int i = 0; i <= n; i++)
            {
                double t = Math.Min(len, i * step + (i == 0 ? 0.25 : 0) - (i == n ? 0.25 : 0));
                bool good = ok(a + d * t + side * 0.45);
                double at = Math.Min(len, i * step);
                if (good && !on) { on = true; s0 = i == 0 ? 0 : at - step * 0.5; }
                else if (!good && on) { on = false; emit(a + d * s0, a + d * (at - step * 0.5)); }
            }
            if (on) emit(a + d * s0, b);
        }

        /// <summary>Drops vertices on straight lines (merged surface cells leave many).</summary>
        private static List<double2> Collinear(List<double2> ring)
        {
            var r = new List<double2>(ring.Count);
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var p = ring[(i - 1 + n) % n]; var c = ring[i]; var q = ring[(i + 1) % n];
                if (PGeo.Len(c - p) < 1e-4) continue;
                if (Math.Abs(PGeo.Cross(PGeo.Norm(c - p), PGeo.Norm(q - c))) < 1e-3 && PGeo.Dot(c - p, q - c) > 0) continue;
                r.Add(c);
            }
            return r.Count >= 3 ? r : ring;
        }

        /// <summary>
        /// The wall pieces of the raised runs: 5, 3 and 1 m pieces (the longest that keeps its ends within 3 cm of the ground on
        /// a slope), centred on the run, sunk until the kerb's lip shows.
        /// </summary>
        public static int Emit(AreaPlan plan, List<KerbRun> runs, KerbKind kind, Func<double2, float> H)
        {
            string fam = WallFor(kind);
            if (fam == null) return 0;
            float sink = WallH - Show(kind);
            int made = 0;
            foreach (var r in runs)
            {
                if (r.Dropped) continue;
                double len = PGeo.Len(r.B - r.A);
                if (len < 0.4) continue;
                var d = (r.B - r.A) / len;
                // the wall's centre line sits half its thickness behind the asphalt face
                var off = -r.Out * (WallT * 0.5);
                double grade = Math.Abs(H(r.B) - H(r.A)) / Math.Max(0.5, len);
                double maxPiece = grade < 1e-3 ? 5 : Math.Min(5, 0.06 / grade);
                var pieces = new List<(double l, string id)>();
                double rest = len;
                while (rest >= 5 && maxPiece >= 5) { pieces.Add((5, fam + "Long")); rest -= 5; }
                while (rest >= 3 && maxPiece >= 3) { pieces.Add((3, fam)); rest -= 3; }
                while (rest >= 0.95) { pieces.Add((1, fam + "Short")); rest -= 1; }
                if (rest >= 0.3) { pieces.Add((0.5, fam + "ExtraShort")); rest -= 0.5; }
                double total = 0; foreach (var pc in pieces) total += pc.l;
                double t = (len - total) * 0.5;
                float yaw = (float)Math.Atan2(d.x, d.y);
                foreach (var pc in pieces)
                {
                    var p = r.A + d * (t + pc.l * 0.5) + off;
                    plan.Objects.Add(new PlannedObject
                    {
                        Position = new float3((float)p.x, H(p), (float)p.y), Yaw = yaw, Role = ObjectRole.Furniture,
                        Asset = new AssetRef(pc.id, fam),
                        Elevation = -sink, Category = AssetCategories.RuinsWall, Variation = (uint)made,
                    });
                    made++;
                    t += pc.l;
                }
            }
            return made;
        }

        /// <summary>
        /// Wheel stops: a 1 m concrete block at the back of every plain stall (lip 0.12 m), the classic kerb-side stop.
        /// </summary>
        public static int WheelStops(AreaPlan plan, Layout layout, Func<double2, float> H)
        {
            var f = layout.Frame;
            int made = 0;
            foreach (var b in layout.LiveBays)
            {
                if (b.Quad == null || b.Quad.Length < 4 || b.Role == BayRole.Blank) continue;
                var back = (b.Quad[2] + b.Quad[3]) * 0.5;
                var along = f.DirToWorld(b.RowDir);
                // two 1 m blocks end to end: a 2 m stop
                foreach (double side in new[] { -0.5, 0.5 })
                {
                    var p = f.ToWorld(back - b.Axis * 0.75 + b.RowDir * side);
                    plan.Objects.Add(new PlannedObject
                    {
                        Position = new float3((float)p.x, H(p), (float)p.y), Yaw = (float)Math.Atan2(along.x, along.y), Role = ObjectRole.Furniture,
                        Asset = new AssetRef("EU_Wall02Short", "EU_Wall01Short"), Elevation = -(WallH - 0.12f), Category = AssetCategories.RuinsWall,
                        Variation = (uint)made,
                    });
                    made++;
                }
            }
            return made;
        }
    }
}
