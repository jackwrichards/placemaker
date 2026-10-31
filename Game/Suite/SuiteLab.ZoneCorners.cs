// Game runner — how far each outline corner is from the zone grid (P2-1: corners snap to the zone cells when the rows are
// on the zone grid). GPL-3.0.
//   {"do":"lab","op":"zoneCorners","label":"after drawing"}
// Per corner: the nearest zone block (by its nearest cell), the corner's offset from that block's 8 m grid crossing
// (along and across its road) and the distance to the block. Logs one line per corner and the worst offset.

using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private void LabZoneCorners(JObject step, JObject rec)
        {
            string label = (string)step["label"] ?? "";
            var ring = _tool.Shape?.Ring2D();
            if (ring == null || ring.Length < 3) { rec["error"] = "no outline"; return; }
            var em = _world.EntityManager;
            // (block, the cell nearest a corner is visible): the snap prefers a visible cell under the cursor, else the nearest block
            var blocks = new System.Collections.Generic.List<(global::Game.Zones.Block B, Entity E)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Zones.Block>(), ComponentType.ReadOnly<global::Game.Zones.Cell>(), ComponentType.Exclude<global::Game.Common.Deleted>(), ComponentType.Exclude<global::Game.Tools.Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all) blocks.Add((em.GetComponentData<global::Game.Zones.Block>(e), e));
            var list = new JArray();
            float worst = 0; int near = 0;
            for (int i = 0; i < ring.Length; i++)
            {
                var p = ring[i];
                float best = float.MaxValue, bestVisible = float.MaxValue; global::Game.Zones.Block bb = default, bv = default;
                foreach (var (b, e) in blocks)
                {
                    var idx = math.clamp(global::Game.Zones.ZoneUtils.GetCellIndex(b, p), 0, b.m_Size - 1);
                    float d = math.distance(global::Game.Zones.ZoneUtils.GetCellPosition(b, idx).xz, p);
                    if (d < best) { best = d; bb = b; }
                    var cells = em.GetBuffer<global::Game.Zones.Cell>(e, true);
                    if ((cells[idx.x + idx.y * b.m_Size.x].m_State & global::Game.Zones.CellFlags.Visible) != 0 && d < bestVisible && d < 20f) { bestVisible = d; bv = b; }
                }
                if (bestVisible < float.MaxValue) { best = bestVisible; bb = bv; }
                var o = new JObject { ["corner"] = new JArray(System.Math.Round(p.x, 2), System.Math.Round(p.y, 2)) };
                if (best < float.MaxValue)
                {
                    var idx = math.clamp(global::Game.Zones.ZoneUtils.GetCellIndex(bb, p), 0, bb.m_Size - 1);
                    var c = global::Game.Zones.ZoneUtils.GetCellPosition(bb, idx).xz;
                    var dir = bb.m_Direction; var across = new float2(dir.y, -dir.x);
                    float a = math.dot(p - c, dir), s = math.dot(p - c, across);
                    // grid lines sit 4 m off the cell centres, every 8 m
                    float offA = Off(a), offS = Off(s);
                    o["block"] = System.Math.Round(best, 1);
                    o["offAlong"] = System.Math.Round(offA, 2);
                    o["offAcross"] = System.Math.Round(offS, 2);
                    if (best <= 64f) { worst = math.max(worst, math.max(math.abs(offA), math.abs(offS))); near++; }
                    Mod.Log.Info($"Placemaker lab: zone corners {label}: corner {i} ({p.x:F1},{p.y:F1}) block {best:F1} m away, off the grid {offA:F2} m along and {offS:F2} m across.");
                }
                list.Add(o);
            }
            rec["corners"] = list;
            rec["worst"] = System.Math.Round(worst, 2);
            Mod.Log.Info($"Placemaker lab: zone corners {label}: {ring.Length} corner(s), {near} within 64 m of a zone block, worst offset from the grid {worst:F2} m.");

            static float Off(float v)
            {
                float m = (v - 4f) % 8f;
                if (m < 0) m += 8f;
                return m > 4f ? m - 8f : m;
            }
        }
    }
}
