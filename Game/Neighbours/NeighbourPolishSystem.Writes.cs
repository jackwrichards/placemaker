// Placemaker neighbourhood polish — plans at once, never over each other (GPL-3.0).
//
// Several plans are worked out at once, each for its own patch of town (no house is in two). Each plan sees its neighbours'
// gardens and fences as they stood when it gathered its houses. The plan that finishes later checks what the others wrote in
// the meantime: if any garden written since (as it was, or as it is now) comes within a couple of metres of a garden this
// plan would write, it is planned again with what stands now, and nothing of it is written. So no garden ever lies over
// another and every fence closes against the fences that really stand, while patches far apart are planned side by side.
// Before, plans kept apart by the reach of both their gardens (about 490 m with the looks as they ship), which in a town meant
// one plan at a time.

using System.Collections.Generic;
using Placemaker.Core.Neighbours;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        /// <summary>How many finished plans have written their gardens (a plan notes it when it gathers its houses).</summary>
        private int _writeSerial;

        /// <summary>A garden a plan wrote: the house, the garden before and after, both with their bounds.</summary>
        private struct GardenWrite
        {
            public int Serial;
            public long House;
            public float2[] Was, Now;
            public float2 Lo, Hi;
        }

        // the gardens written while any plan still being worked out was gathered (older ones are dropped)
        private readonly List<GardenWrite> _writes = new List<GardenWrite>();

        /// <summary>For the game runner: plans worked out again because another wrote a garden near theirs meanwhile.</summary>
        public static int TotalRetries;

        /// <summary>How near two gardens planned apart may come (m): nearer, they may share a border, a fence or land.</summary>
        private const float WriteClearance = 2f;

        private void NoteWrite(long house, float2[] was, float2[] now)
        {
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            if (was != null) foreach (var p in was) { lo = math.min(lo, p); hi = math.max(hi, p); }
            foreach (var p in now) { lo = math.min(lo, p); hi = math.max(hi, p); }
            _writes.Add(new GardenWrite { Serial = _writeSerial, House = house, Was = was, Now = now, Lo = lo, Hi = hi });
        }

        /// <summary>True when nothing another plan wrote since this one gathered its houses comes near the gardens it would
        /// write; else its asked houses wait to be planned again (first if they were the player's) and false.</summary>
        private bool CheckWrites(Pending pend, StitchPlan plan)
        {
            // only what was written after this plan gathered its houses is new to it
            int oldest = int.MaxValue;
            foreach (var f in _inFlight) oldest = math.min(oldest, f.StartSerial);
            oldest = math.min(oldest, pend.StartSerial);
            _writes.RemoveAll(w => w.Serial <= oldest);
            bool clear = true;
            if (_writes.Count > 0)
            {
                var mine = new List<(float2[] Ring, float2 Lo, float2 Hi)>();
                foreach (var id in pend.CoreIds)
                {
                    var ring = plan.Plots.TryGetValue(id, out var p) ? p : LotRing(pend.Stitch.Find(h => h.Id == id));
                    if (ring == null || ring.Length < 3) continue;
                    float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                    foreach (var q in ring) { lo = math.min(lo, q); hi = math.max(hi, q); }
                    mine.Add((ring, lo, hi));
                }
                foreach (var w in _writes)
                {
                    if (w.Serial <= pend.StartSerial || pend.CoreIds.Contains(w.House)) continue;
                    foreach (var (ring, lo, hi) in mine)
                    {
                        if (w.Hi.x < lo.x - WriteClearance || w.Lo.x > hi.x + WriteClearance || w.Hi.y < lo.y - WriteClearance || w.Lo.y > hi.y + WriteClearance) continue;
                        if (Near(ring, w.Now, WriteClearance) || (w.Was != null && Near(ring, w.Was, WriteClearance))) { clear = false; break; }
                    }
                    if (!clear) break;
                }
            }
            if (clear) return true;
            int again = 0;
            if (pend.DoneIds != null)
                foreach (var id in pend.DoneIds)
                    if (pend.HouseOf.TryGetValue(id, out var e) && EntityManager.Exists(e) && IsYes(e))
                    {
                        if (_dirty.Add(e)) again++;
                        if (pend.First) _first.Add(e);
                    }
            if (TotalRetries++ < 10) Mod.Log.Info($"Placemaker neighbourhood polish: a plan of {pend.CoreIds.Count} gardens met gardens another plan wrote meanwhile: {again} houses planned again");
            return false;
        }

        /// <summary>Two rings come within <paramref name="d"/> of each other, cross, or one lies in the other.</summary>
        private static bool Near(float2[] a, float2[] b, float d)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3) return false;
            float2 alo = new float2(float.MaxValue), ahi = new float2(float.MinValue), blo = new float2(float.MaxValue), bhi = new float2(float.MinValue);
            foreach (var p in a) { alo = math.min(alo, p); ahi = math.max(ahi, p); }
            foreach (var p in b) { blo = math.min(blo, p); bhi = math.max(bhi, p); }
            if (ahi.x + d < blo.x || alo.x - d > bhi.x || ahi.y + d < blo.y || alo.y - d > bhi.y) return false;
            if (Placemaker.Core.Geometry.Poly.Contains(b, a[0]) || Placemaker.Core.Geometry.Poly.Contains(a, b[0])) return true;
            float d2 = d * d;
            for (int i = 0; i < a.Length; i++)
            {
                var p = a[i];
                var q = a[(i + 1) % a.Length];
                var slo = math.min(p, q) - d;
                var shi = math.max(p, q) + d;
                if (shi.x < blo.x || slo.x > bhi.x || shi.y < blo.y || slo.y > bhi.y) continue;
                for (int j = 0; j < b.Length; j++)
                    if (SegmentDistanceSq(p, q, b[j], b[(j + 1) % b.Length]) <= d2) return true;
            }
            return false;
        }

        private static float SegmentDistanceSq(float2 p, float2 q, float2 r, float2 s)
        {
            float C(float2 o, float2 x, float2 y) => (x.x - o.x) * (y.y - o.y) - (x.y - o.y) * (y.x - o.x);
            if (C(p, q, r) * C(p, q, s) < 0f && C(r, s, p) * C(r, s, q) < 0f) return 0f;
            float PointSeg(float2 x, float2 a, float2 b)
            {
                var ab = b - a;
                float t = math.saturate(math.dot(x - a, ab) / math.max(1e-12f, math.dot(ab, ab)));
                return math.distancesq(x, a + ab * t);
            }
            return math.min(math.min(PointSeg(p, r, s), PointSeg(q, r, s)), math.min(PointSeg(r, p, q), PointSeg(s, p, q)));
        }
    }
}
