// (P2-1) The player's hand edits on stalls (Core/Touches.cs): accessible here, a charger pair here, a plain stall here (no
// automatic accessible or charger stall), an empty paved space, a planted island. Applied after the layout and before the
// automatic accessible and charger stalls, which then fill only what the player did not decide: a hand-placed accessible
// group counts toward the accessible share, so placing one moves the automatic one there.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class StallTouches
    {
        /// <summary>The bay a touch lands on: the stall containing it, else the nearest centre within reach (local frame).</summary>
        internal static int BayAt(Layout layout, double2 local, Func<Bay, bool> ok = null)
        {
            int best = -1; double bestD = Touches.Reach;
            for (int i = 0; i < layout.Bays.Count; i++)
            {
                var b = layout.Bays[i];
                if (b.Removed || (ok != null && !ok(b))) continue;
                if (b.Quad != null && PGeo.Contains(b.Quad, local)) return i;
                double d = math.distance(b.Center, local);
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Applies the stall touches. Returns the number of explicit accessible groups and charger pairs placed (the automatic
        /// ones are reduced by them) and counts the touches that found no stall in <see cref="Layout.TouchesDropped"/>.
        /// </summary>
        public static void Apply(Layout layout, ParkingSettings s, IReadOnlyList<HandTouch> touches, Func<double2, bool> chargerSpotOk,
            out int accessibleGroups, out int chargerPairs)
        {
            accessibleGroups = 0; chargerPairs = 0;
            if (touches == null || touches.Count == 0) return;
            var f = layout.Frame;
            double2 L(HandTouch t) => f.ToLocal(new double2(t.Position.x, t.Position.z));

            // islands and empty spaces first (they take stalls away), then plain (pins), then accessible, then chargers
            foreach (var t in touches)
            {
                if (t.Kind != ParkingIds.TouchIsland && t.Kind != ParkingIds.TouchBlank && t.Kind != ParkingIds.TouchPlain) continue;
                int i = BayAt(layout, L(t));
                if (i < 0) { layout.TouchesDropped++; continue; }
                var b = layout.Bays[i];
                b.TouchedBy = t.Kind; b.TouchAt = t.Position; b.Pinned = true;
                if (t.Kind == ParkingIds.TouchIsland) { b.Removed = true; b.Island = true; }
                else if (t.Kind == ParkingIds.TouchBlank) b.Role = BayRole.Blank;
            }
            var sizes = SpecialStalls.Groups(s.StallWidth);
            foreach (var t in touches)
            {
                if (t.Kind != ParkingIds.TouchAccessible) continue;
                var at = L(t);
                int i = BayAt(layout, at, b => b.Role == BayRole.Normal && !b.Pinned);
                if (i < 0 || sizes.Count == 0 || !SpecialStalls.ReplaceAt(layout, i, at, sizes, t.Position))
                {
                    // already accessible (a hand group of an earlier touch or the same place): it holds, nothing to drop
                    int j = BayAt(layout, at);
                    if (j < 0 || layout.Bays[j].Role != BayRole.Disabled) layout.TouchesDropped++;
                    continue;
                }
                accessibleGroups++;
            }
            foreach (var t in touches)
            {
                if (t.Kind != ParkingIds.TouchCharger) continue;
                var at = L(t);
                int i = BayAt(layout, at, b => b.Role == BayRole.Normal && !b.Pinned);
                int j = i >= 0 ? Neighbour(layout, i, at) : -1;
                if (j < 0) { layout.TouchesDropped++; continue; }
                var a = layout.Bays[i]; var c = layout.Bays[j];
                var spot = SpecialStalls.ChargerSpot(a, c, out _);
                if (chargerSpotOk != null && !chargerSpotOk(spot)) { layout.TouchesDropped++; continue; }
                a.Role = BayRole.Electric; c.Role = BayRole.Electric;
                a.Pinned = c.Pinned = true;
                a.TouchedBy = c.TouchedBy = t.Kind; a.TouchAt = c.TouchAt = t.Position;
                layout.ElectricPairs.Add(new int2(i, j));
                chargerPairs++;
            }
        }

        /// <summary>The touching normal stall beside bay i in its row, on the side of the touch (else the other side).</summary>
        private static int Neighbour(Layout layout, int i, double2 at)
        {
            var b = layout.Bays[i];
            int best = -1; double bestScore = double.MaxValue;
            for (int k = 0; k < layout.Bays.Count; k++)
            {
                if (k == i) continue;
                var o = layout.Bays[k];
                if (o.Removed || o.Row != b.Row || o.Role != BayRole.Normal || o.Pinned) continue;
                double along = PGeo.Dot(o.Center - b.Center, b.RowDir);
                double gap = Math.Abs(along) - LayoutEngine.HalfPitch(b, b.RowDir) - LayoutEngine.HalfPitch(o, b.RowDir);
                if (Math.Abs(gap) > 0.05) continue;
                // prefer the side the player touched toward
                double side = PGeo.Dot(at - b.Center, b.RowDir);
                double score = (Math.Sign(along) == Math.Sign(side) || side == 0) ? 0 : 1;
                if (score < bestScore) { bestScore = score; best = k; }
            }
            return best;
        }
    }
}
