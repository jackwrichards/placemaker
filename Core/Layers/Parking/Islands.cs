// P2-5 Parking looks: what grows on the islands and medians (the "Islands" setting; docs/runbooks/parking-looks.md).
//
//   grass      lawn, the line of trees and shrubs (rf-parking's planting lines)
//   shrubs     no trees: a low line of shrubs only (a lot that keeps its sight lines)
//   hedge      a clipped low hedge down every long median, the trees standing in it (German Parkplatz)
//   bioswale   a planted rain channel: dark wet soil, wild sedgy shrubs, the trees kept (the green and eco lots)
//   gravel     no grass at all: gravel over packed soil, the trees kept in it (dry climates, a hard-wearing lot)

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class Islands
    {
        public const string Soil = "Forestry Surface 01";

        /// <summary>The surface of the planted areas for a style (null = the green surface the player chose).</summary>
        public static string SurfaceFor(IslandStyle s) => s == IslandStyle.Bioswale || s == IslandStyle.Gravel ? Soil : null;

        /// <summary>Before the objects are made: the planting changes its kinds.</summary>
        public static void Before(Layout layout, ParkingLook k, int bushSpecies)
        {
            if (k.Islands == IslandStyle.Shrubs)
            {
                // the trees give way to shrubs
                foreach (var p in layout.Plants) if (p.Tree) { p.Tree = false; p.Species = bushSpecies; }
            }
            else if (k.Islands == IslandStyle.Gravel || k.Islands == IslandStyle.Hedge)
                layout.Plants.RemoveAll(p => !p.Tree);   // gravel: bare round the trunks; hedge: the hedge takes the shrubs' place
        }

        /// <summary>After the objects: hedges along the long medians, the swale's planting.</summary>
        public static int After(AreaPlan plan, Layout layout, ParkingLook k, Func<double2, float> H, uint seed)
        {
            if (k.Islands != IslandStyle.Hedge && k.Islands != IslandStyle.Bioswale) return 0;
            var f = layout.Frame;
            int n = 0;
            foreach (var sp in layout.Surfaces)
            {
                if (sp.Asphalt || sp.Ring == null || sp.Ring.Count < 3) continue;
                // the strip's long axis and its extent across
                var ring = sp.Ring;
                var axis = new double2(1, 0); double longest = 0;
                for (int i = 0; i < ring.Count; i++)
                {
                    var d = ring[(i + 1) % ring.Count] - ring[i];
                    double l2 = PGeo.Dot(d, d);
                    if (l2 > longest) { longest = l2; axis = PGeo.Norm(d); }
                }
                var across = PGeo.Perp(axis);
                double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                foreach (var p in ring)
                {
                    double x = PGeo.Dot(p, axis), y = PGeo.Dot(p, across);
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
                double w = y1 - y0, len = x1 - x0;
                if (len < 6 || w < 1.2) continue;
                double ym = (y0 + y1) * 0.5;
                if (k.Islands == IslandStyle.Hedge)
                {
                    // a low hedge 0.6 m off the middle (the trees stand on the middle line), 4 m pieces, ends 1.2 m short
                    double off = w > 3.2 ? 0.7 : 0;
                    for (double x = x0 + 1.2 + 2; x + 2 <= x1 - 1.2 + 1e-6; x += 4)
                    {
                        var c = axis * x + across * (ym + off);
                        if (!PGeo.Contains(ring, c)) continue;
                        var wpt = f.ToWorld(c); var dir = f.DirToWorld(axis);
                        plan.Objects.Add(new PlannedObject
                        {
                            Position = new float3((float)wpt.x, H(wpt), (float)wpt.y), Yaw = (float)Math.Atan2(dir.x, dir.y), Role = ObjectRole.Bush,
                            Asset = new AssetRef("HedgePieceLow01", "HedgePieceLow02"), Category = AssetCategories.Hedge, Variation = (uint)n++,
                        });
                    }
                }
                else
                {
                    // the swale: wild shrubs in clumps down its length
                    for (double x = x0 + 1.5; x <= x1 - 1.5; x += 2.6)
                    {
                        uint hsh = Common.Hashing.Mix(seed, n + 1, 0x5A1E);
                        if (Common.Hashing.Unit(hsh) < 0.35f) { n++; continue; }
                        double yy = ym + (Common.Hashing.Unit(hsh ^ 0x77u) - 0.5) * Math.Max(0, w - 1.4);
                        var c = axis * x + across * yy;
                        if (!PGeo.Contains(ring, c)) continue;
                        var wpt = f.ToWorld(c);
                        plan.Objects.Add(new PlannedObject
                        {
                            Position = new float3((float)wpt.x, H(wpt), (float)wpt.y), Yaw = Common.Hashing.Unit(hsh ^ 0x99u) * 6.283f, Role = ObjectRole.Bush,
                            Asset = new AssetRef("FlowerBushWild02", "GreenBushWild01"), Category = AssetCategories.Bush, Variation = (uint)n++,
                        });
                    }
                }
            }
            plan.Facts["islandPieces"] = n;
            return n;
        }

        /// <summary>The centre lines (frame-local) and widths of the planted strips at least <paramref name="minWidth"/> wide and 6 m long.</summary>
        public static List<(double2 a, double2 b, double w)> Strips(Layout layout, double minWidth)
        {
            var r = new List<(double2, double2, double)>();
            foreach (var sp in layout.Surfaces)
            {
                if (sp.Asphalt || sp.Ring == null || sp.Ring.Count < 3) continue;
                var ring = sp.Ring;
                var axis = new double2(1, 0); double longest = 0;
                for (int i = 0; i < ring.Count; i++)
                {
                    var d = ring[(i + 1) % ring.Count] - ring[i];
                    double l2 = PGeo.Dot(d, d);
                    if (l2 > longest) { longest = l2; axis = PGeo.Norm(d); }
                }
                var across = PGeo.Perp(axis);
                double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                foreach (var p in ring)
                {
                    double x = PGeo.Dot(p, axis), y = PGeo.Dot(p, across);
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
                double w = y1 - y0;
                if (x1 - x0 < 6 || w < minWidth || w > 14) continue;
                double ym = (y0 + y1) * 0.5;
                var a = axis * (x0 + 1.2) + across * ym; var b = axis * (x1 - 1.2) + across * ym;
                if (!PGeo.Contains(ring, (a + b) * 0.5)) continue;
                r.Add((a, b, w));
            }
            return r;
        }

        /// <summary>The dry ravine's boulders: sunk stones in clumps down the strips its stony bed runs along.</summary>
        public static int Ravine(AreaPlan plan, Layout layout, Func<double2, float> H, uint seed)
        {
            var f = layout.Frame;
            int n = 0;
            foreach (var (a, b, w) in Strips(layout, 2.0))
            {
                double len = PGeo.Len(b - a);
                var d = (b - a) / len; var nn = PGeo.Perp(d);
                for (double t = 1.5; t < len - 1; t += 2.2)
                {
                    uint h = Common.Hashing.Mix(seed, n + 1, 0x2A7E);
                    if (Common.Hashing.Unit(h) < 0.45f) { n++; continue; }
                    var p = a + d * t + nn * ((Common.Hashing.Unit(h ^ 0x13u) - 0.5) * Math.Max(0, w - 1.6) * 0.6);
                    var wp = f.ToWorld(p);
                    float scale = 0.35f + 0.35f * Common.Hashing.Unit(h ^ 0x27u);
                    // (parks: Boulder01-08 already sit 1.3-1.8 m below their origin) a little more sunk: stones in the bed
                    plan.Objects.Add(new PlannedObject
                    {
                        Position = new float3((float)wp.x, H(wp), (float)wp.y), Yaw = Common.Hashing.Unit(h ^ 0x31u) * 6.283f, Role = ObjectRole.Furniture,
                        Asset = new AssetRef("Boulder0" + (1 + (int)(Common.Hashing.Unit(h ^ 0x45u) * 7.99f)), "Boulder03"), Category = AssetCategories.RuinsRubble,
                        Scale = scale, Elevation = -0.45f * 1.6f * scale, Variation = (uint)n++,
                    });
                }
            }
            plan.Facts["ravineStones"] = n;
            return n;
        }
    }
}
