// Placemaker — what blocks live zone cells at the cutouts, BEFORE the refresh re-snaps (s60, GPL-3.0).
//
// Playtest s60: a cutout plot had 6 open and 7 blocked live cells nearby and zoned nothing; the final check only classifies
// cells after the zoning. This runs right after the main Apply with the planned cells and reports the source of every
// blocked live cell within half a cell of one (our nets / lots / zone block lots / the game's depth rule or a city net).

using System;
using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Placemaker.Core.Build;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private void LogBlockedPlanCells()
        {
            if (!DebugBuildChecks) return;
            try
            {
                var plan = _request?.Plan;
                if (plan?.ZoneCells == null) return;
                var planned = new List<float2>();
                foreach (var c in plan.ZoneCells) if (c.State == ZoneCellState.Zoned) planned.Add(0.25f * (c.A + c.B + c.C + c.D));
                if (planned.Count == 0) return;
                float2 min = new float2(float.MaxValue), max = new float2(float.MinValue);
                foreach (var p in planned) { min = math.min(min, p); max = math.max(max, p); }
                var search = World.GetOrCreateSystemManaged<global::Game.Zones.SearchSystem>();
                var tree = search.GetSearchTree(true, out var deps);
                deps.Complete();
                var results = new NativeList<Entity>(64, Allocator.Temp);
                var causes = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var examples = new List<string>();
                int blocked = 0;
                try
                {
                    var it = new ZoneBlockIterator { Bounds = new Colossal.Mathematics.Bounds2(min - 12f, max + 12f), Results = results };
                    tree.Iterate(ref it);
                    var seen = new HashSet<Entity>();
                    for (int i = 0; i < results.Length; i++)
                    {
                        var e = results[i];
                        if (!seen.Add(e) || !EntityManager.HasComponent<global::Game.Zones.Block>(e) || !EntityManager.HasBuffer<global::Game.Zones.Cell>(e)) continue;
                        if (EntityManager.HasComponent<Temp>(e) || EntityManager.HasComponent<Deleted>(e)) continue;
                        var block = EntityManager.GetComponentData<global::Game.Zones.Block>(e);
                        var cells = EntityManager.GetBuffer<global::Game.Zones.Cell>(e, true);
                        if (cells.Length < block.m_Size.x * block.m_Size.y) continue;
                        for (int y = 0; y < block.m_Size.y; y++)
                            for (int x = 0; x < block.m_Size.x; x++)
                            {
                                if ((cells[x + y * block.m_Size.x].m_State & global::Game.Zones.CellFlags.Blocked) == 0) continue;
                                var at = global::Game.Zones.ZoneUtils.GetCellPosition(block, new int2(x, y)).xz;
                                bool near = false;
                                foreach (var p in planned) if (math.distancesq(at, p) <= 36f) { near = true; break; }
                                if (!near) continue;
                                blocked++;
                                string why = BlockedCause(e, new int2(x, y));
                                causes.TryGetValue(why, out int n);
                                causes[why] = n + 1;
                                if (examples.Count < 8) examples.Add($"{why} at ({at.x:F0}, {at.y:F0}) row {y}");
                            }
                    }
                }
                finally { results.Dispose(); }
                if (blocked == 0) { Mod.Log.Info("Placemaker zoning (before refresh): no live cell near a planned cutout cell is blocked."); return; }
                var sb = new System.Text.StringBuilder();
                foreach (var kv in causes) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(" x").Append(kv.Value);
                Mod.Log.Warn($"Placemaker zoning (before refresh): {blocked} live cell(s) within 6 m of a planned cutout cell are blocked by: {sb}. e.g. {string.Join("; ", examples)}.");
            }
            catch (Exception ex) { Mod.Log.Warn("Placemaker zoning: blocked-cell classification failed: " + ex.Message); }
        }
    }
}
