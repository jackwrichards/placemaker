// Ported from ParkingLotTool (KruemelGames, GPL-3.0) — Geometry/ParkingGeometry.SpecialStalls.cs
// (FindDisabledGroups, AssignBayRoles) and Geometry/ParkingBayDecals.cs (charger position).
// Translated; the shares became settings instead of constants; static trace lists dropped.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class SpecialStalls
    {
        /// <summary>Target width of an accessible stall (PLT DisabledWidth, decal 5.0 x 6.2 m).</summary>
        public const double AccessibleWidth = 5.0;

        internal sealed class Group
        {
            public int Fields;     // normal stalls replaced
            public int Stalls;     // accessible stalls created
            public double Width;   // their width
            public double Error;
        }

        /// <summary>
        /// Ways to replace 3–8 normal stalls by fewer stalls close to 5.0 m wide (accepted 4.7–5.6 m),
        /// best fit first. With 3.0 m stalls: 5 fields → 3 stalls of exactly 5.0 m.
        /// </summary>
        internal static List<Group> Groups(double stallWidth)
        {
            var all = new List<Group>();
            for (int fields = 3; fields <= 8; fields++)
            {
                double span = fields * stallWidth;
                int stalls = (int)Math.Floor(span / AccessibleWidth + 0.5);
                if (stalls < 2 || stalls >= fields) continue;
                double width = span / stalls;
                if (width < 4.7 || width > 5.6) continue;
                all.Add(new Group { Fields = fields, Stalls = stalls, Width = width, Error = Math.Abs(width - AccessibleWidth) });
            }
            all.Sort((a, b) => a.Error.CompareTo(b.Error));
            return all;
        }

        /// <summary>
        /// The stalls of each row in row order (rows ascending). (perf) Sorted once: the accessible stalls only remove
        /// stalls and add wider ones that are not normal, and Runs skips both (a removed slice leaves a gap wider than a
        /// stall, which ends the run there just as the wider stalls did).
        /// </summary>
        private static List<List<int>> SortedRows(Layout layout)
        {
            var byRow = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < layout.Bays.Count; i++)
            {
                var b = layout.Bays[i];
                if (b.Removed) continue;
                if (!byRow.TryGetValue(b.Row, out var l)) byRow[b.Row] = l = new List<int>();
                l.Add(i);
            }
            var rows = new List<List<int>>();
            foreach (var kv in byRow)
            {
                var list = kv.Value;
                var dir = layout.Bays[list[0]].RowDir;
                var keys = new double[list.Count];
                for (int k = 0; k < list.Count; k++) keys[k] = PGeo.Dot(layout.Bays[list[k]].Center, dir);
                var arr = list.ToArray();
                Array.Sort(keys, arr);
                rows.Add(new List<int>(arr));
            }
            return rows;
        }

        /// <summary>Contiguous runs of normal stalls per row, ordered along the row.</summary>
        private static List<List<int>> Runs(Layout layout, List<List<int>> rows)
        {
            var runs = new List<List<int>>();
            foreach (var all in rows)
            {
                var dir = layout.Bays[all[0]].RowDir;
                List<int> run = null;
                Bay prev = null;
                foreach (int i in all)
                {
                    var b = layout.Bays[i];
                    if (b.Removed) continue;
                    bool join = prev != null && b.Role == BayRole.Normal && prev.Role == BayRole.Normal &&
                        Math.Abs(PGeo.Dot(b.Center - prev.Center, dir) - LayoutEngine.HalfPitch(prev, dir) - LayoutEngine.HalfPitch(b, dir)) <= 0.05;
                    if (!join) { run = new List<int>(); runs.Add(run); }
                    if (b.Role == BayRole.Normal) run.Add(i);
                    prev = b;
                }
            }
            runs.RemoveAll(r => r.Count == 0);
            return runs;
        }

        public static void Assign(Layout layout, ParkingSettings s, Func<double2, bool> chargerSpotOk)
        {
            layout.ElectricPairs.Clear();
            int total = layout.StallCount;
            if (total == 0) return;

            // Anchors: the entrances (accessible and EV stalls move to the way in), else the lot centre.
            var targets = new List<double2>();
            foreach (var e in layout.Entrances) if (e.Connected) targets.Add(e.Junction);
            if (targets.Count == 0) targets.Add(PGeo.Centroid(layout.Lot));

            // ── accessible stalls ──
            var rows = SortedRows(layout);
            var sizes = Groups(s.StallWidth);
            if (s.AccessibleShare > 0 && sizes.Count > 0)
            {
                int wanted = (int)Math.Floor(total * s.AccessibleShare / 100.0 + 0.5);
                int groups = Math.Max(1, (int)Math.Floor((double)wanted / sizes[0].Stalls + 0.5));
                for (int gi = 0; gi < groups; gi++)
                {
                    var anchor = targets[gi % targets.Count];
                    var runs = Runs(layout, rows);
                    int bestRun = -1, bestStart = -1; Group bestSize = null; double bestNear = double.MaxValue;
                    for (int ri = 0; ri < runs.Count; ri++)
                    {
                        var run = runs[ri];
                        // A wider stall at 45°/60° grows deeper than its row and would poke into the
                        // aisle, so accessible stalls only replace square (90°) stalls.
                        var b0 = layout.Bays[run[0]];
                        if (Math.Abs(PGeo.Dot(b0.Axis, b0.RowDir)) > 1e-6) continue;
                        foreach (var size in sizes)
                        {
                            for (int k = 0; k + size.Fields <= run.Count; k++)
                            {
                                double near = double.MaxValue;
                                for (int m = 0; m < size.Fields; m++)
                                    near = Math.Min(near, math.distance(layout.Bays[run[k + m]].Center, anchor));
                                if (near < bestNear - 1e-9 || (Math.Abs(near - bestNear) <= 1e-9 && bestSize != null && size.Error < bestSize.Error))
                                { bestNear = near; bestRun = ri; bestStart = k; bestSize = size; }
                            }
                        }
                    }
                    if (bestRun < 0)
                    {
                        if (gi == 0) layout.Issues.Add(new LayoutIssue(IssueSeverity.Info, "Placemaker.Parking.Issue.NoRoomForAccessible"));
                        break;
                    }
                    Replace(layout, runs[bestRun].GetRange(bestStart, bestSize.Fields), bestSize);
                }
            }

            // ── EV pairs ──
            if (s.EvShare > 0 && layout.StallCount >= 2)
            {
                int pairs = Math.Max(1, (int)Math.Floor(layout.StallCount * s.EvShare / 100.0 / 2 + 0.5));
                var blue = new List<double2>();
                foreach (var b in layout.Bays) if (!b.Removed && b.Role == BayRole.Disabled) blue.Add(b.Center);
                var refs = blue.Count > 0 ? blue : targets;
                double Ref(double2 c)
                {
                    double best = double.MaxValue;
                    foreach (var p in refs) best = Math.Min(best, math.distance(c, p));
                    return best;
                }
                var candidates = new List<int2>();
                foreach (var run in Runs(layout, rows))
                {
                    if (run.Count < 2) continue;
                    bool reverse = Ref(layout.Bays[run[run.Count - 1]].Center) < Ref(layout.Bays[run[0]].Center);
                    var walk = new List<int>(run);
                    if (reverse) walk.Reverse();
                    for (int k = 0; k + 1 < walk.Count; k += 2) candidates.Add(new int2(walk[k], walk[k + 1]));
                }
                // (perf) each pair's distance once (the comparison asked every reference point twice per compare)
                var dist = new Dictionary<int, double>(candidates.Count);
                foreach (var p in candidates) dist[p.x] = Math.Min(Ref(layout.Bays[p.x].Center), Ref(layout.Bays[p.y].Center));
                candidates.Sort((p, q) =>
                {
                    int c = dist[p.x].CompareTo(dist[q.x]);
                    return c != 0 ? c : p.x.CompareTo(q.x);
                });
                foreach (var pr in candidates)
                {
                    if (pairs <= 0) break;
                    var a = layout.Bays[pr.x]; var b = layout.Bays[pr.y];
                    if (a.Role != BayRole.Normal || b.Role != BayRole.Normal) continue;
                    var spot = ChargerSpot(a, b, out _);
                    if (chargerSpotOk != null && !chargerSpotOk(spot)) continue;
                    a.Role = BayRole.Electric; b.Role = BayRole.Electric;
                    layout.ElectricPairs.Add(pr);
                    pairs--;
                }
                if (layout.ElectricPairs.Count == 0)
                    layout.Issues.Add(new LayoutIssue(IssueSeverity.Info, "Placemaker.Parking.Issue.NoRoomForChargers"));
            }
        }

        /// <summary>Replaces a run slice by wider accessible stalls covering the same span.</summary>
        private static void Replace(Layout layout, List<int> slice, Group size)
        {
            var first = layout.Bays[slice[0]];
            var dir = first.RowDir;
            double pitch = 2 * LayoutEngine.HalfPitch(first, dir);
            double newPitch = size.Fields * pitch / size.Stalls;
            double newWidth = size.Fields * first.Width / size.Stalls;
            var start = first.Center - dir * (pitch / 2);
            foreach (int i in slice) layout.Bays[i].Removed = true;
            for (int k = 0; k < size.Stalls; k++)
            {
                var c = start + dir * (newPitch * (k + 0.5));
                var axis = first.Axis;
                var w = PGeo.Perp(axis);
                double hl = first.Length / 2, hw = newWidth / 2;
                layout.Bays.Add(new Bay
                {
                    Center = c, Axis = axis, RowDir = dir, Width = newWidth, Length = first.Length,
                    Kind = first.Kind, Role = BayRole.Disabled, Row = first.Row, Order = first.Order, Line = first.Line,
                    Quad = new[] { c - axis * hl - w * hw, c - axis * hl + w * hw, c + axis * hl + w * hw, c + axis * hl - w * hw },
                });
            }
        }

        /// <summary>
        /// Centre of the pair's back edge, set back 0.6 m into the planting; facing toward the aisle.
        /// </summary>
        public static double2 ChargerSpot(Bay a, Bay b, out double2 facing)
        {
            var backMid = ((a.Quad[2] + a.Quad[3]) * 0.5 + (b.Quad[2] + b.Quad[3]) * 0.5) * 0.5;
            var axis = PGeo.Norm(a.Axis + b.Axis);
            facing = -axis;
            return backMid + axis * 0.6;
        }
    }
}
