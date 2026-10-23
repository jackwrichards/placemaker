// Placemaker neighbourhood polish — the fewest neighbours a change plans again (GPL-3.0).
//
// A building that goes (or a lot unpolished) frees its land only to the gardens that touch it: its side neighbours and the
// ones behind, never the houses across a street (a road between them). Before, every house within 70 m was planned again,
// and every unpolished one among them planned the polished ones within 70 m of itself: a row of 20 across the street for 4
// houses bulldozed (the user, 23 Oct). Zoned or not, the gardens that touch it take its land (the user: "If I delete an
// area, it should re-outgrow to the missing space, even if that area is zoned").
// A plan takes along the houses near the ones it is asked for (core): never one across a road, path or track (two gardens
// never join across one), and for a level-up of the same lot (its garden comes out the same) only the gardens touching it.

using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Placemaker.Core.Geometry;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        // two lands closer than this touch (gardens meet exactly, lots in a zone block share their edges; a street keeps the
        // lots on its two sides 8 m apart or more)
        private const float TouchGap = 2.5f;

        /// <summary>The house's land: its garden (polished), else its lot.</summary>
        private float2[] LandOf(Entity house)
        {
            var garden = GardenOf(house);
            if (garden != null) return garden;
            if (!EntityManager.Exists(house) || !EntityManager.HasComponent<PrefabRef>(house) || !EntityManager.HasComponent<global::Game.Objects.Transform>(house)) return null;
            BuildingFootprints.Of(EntityManager, house, out var lot, out _);
            return lot;
        }

        /// <summary>The two lands touch or overlap (closer than gap).</summary>
        private static bool Touches(float2[] a, float2[] b, float gap)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3) return false;
            Poly.Bounds(a, out var alo, out var ahi);
            Poly.Bounds(b, out var blo, out var bhi);
            if (ahi.x + gap < blo.x || bhi.x + gap < alo.x || ahi.y + gap < blo.y || bhi.y + gap < alo.y) return false;
            // (two edges side by side: an end of one of them lies across from the other)
            foreach (var p in a) if (Poly.Contains(b, p) || Poly.DistanceToBoundary(b, p) <= gap) return true;
            foreach (var p in b) if (Poly.Contains(a, p) || Poly.DistanceToBoundary(a, p) <= gap) return true;
            return false;
        }

        /// <summary>The polished houses whose land touches this land: planned again (first: before other waiting houses).
        /// reason: the DirtyBy count it adds to (-1 none). Returns how many were put in line.</summary>
        private int TouchingDirty(Entity gone, float2[] land, bool first, int reason)
        {
            if (land == null || land.Length < 3) return 0;
            Poly.Bounds(land, out var lo, out var hi);
            int n = 0;
            void Mark(Entity h)
            {
                if (_dirty.Add(h)) { n++; if (reason >= 0) DirtyBy[reason]++; }
                if (first) _first.Add(h);
            }
            // the houses with a garden (looked for by their gardens' bounds: a garden reaches up to 500 m)
            var seen = new HashSet<Entity>();
            foreach (var (house, glo, ghi) in GardenBoxes())
            {
                if (house == gone || ghi.x + TouchGap < lo.x || glo.x - TouchGap > hi.x || ghi.y + TouchGap < lo.y || glo.y - TouchGap > hi.y) continue;
                seen.Add(house);
                if (!EntityManager.Exists(house) || EntityManager.HasComponent<Deleted>(house) || !IsYes(house)) continue;
                if (Touches(GardenOf(house), land, TouchGap)) Mark(house);
            }
            // polished houses still without a garden (waiting for their first plan): by their lot
            EnsureGrid();
            for (float x = math.floor(lo.x / Cell - 1f) * Cell; x <= hi.x + Cell; x += Cell)
                for (float z = math.floor(lo.y / Cell - 1f) * Cell; z <= hi.y + Cell; z += Cell)
                {
                    if (!_grid.TryGetValue(Key(new float2(x, z)), out var near)) continue;
                    foreach (int j in near)
                    {
                        var e = _gridHouses[j].E;
                        if (e == gone || seen.Contains(e) || !EntityManager.Exists(e) || !IsYes(e) || GardenOf(e) != null) continue;
                        seen.Add(e);
                        BuildingFootprints.Of(EntityManager, e, out var lot, out _);
                        if (Touches(lot, land, TouchGap)) Mark(e);
                    }
                }
            return n;
        }

        /// <summary>The houses the game removed or the player bulldozed this frame: their land to the gardens that touch it.</summary>
        private void CollectGone()
        {
            using (var gone = _goneHouses.ToEntityArray(Allocator.Temp))
            {
                var byTool = BulldozedByPlayer();
                int kept = 0, removed = 0, marked = 0;
                foreach (var g in gone)
                {
                    _dirty.Remove(g);
                    _first.Remove(g);
                    _described.Remove(g);
                    _plannedModel.Remove(g);
                    // removed by the game (collapsed, condemned, demolished) in a city that plans nothing for that: the
                    // gardens around keep theirs (the player's bulldozer always plans)
                    if (!PolishGameRemovals && IsGrowable(g) && !byTool.Contains(g)) { TotalRemovalsKept++; kept++; continue; }
                    removed++;
                    marked += TouchingDirty(g, LandOf(g), true, 1);
                }
                _gridStale = true;
                if (removed > 0)
                    Mod.Log.Info($"Placemaker neighbourhood polish: {removed} building(s) removed: {marked} garden(s) that touch them planned again{(kept > 0 ? $" ({kept} removed by the game kept as they are)" : "")}");
            }
        }

        /// <summary>For the game runner: houses near a levelled-up one left out of its plan (not touching it), and the houses
        /// plans edited in all.</summary>
        public static int CoreNotTouching, CoreEdited;

        // the model each polished house was last planned with (edited by a plan; after a load, the one it was saved with). Not
        // _prefabSeen: a plan reads the models of every house around it, so with a street levelling up at once the houses
        // looked planned with their new models before their own plans (1 of 32 level-ups in the game runner)
        private readonly Dictionary<Entity, Entity> _plannedModel = new Dictionary<Entity, Entity>();

        /// <summary>A polished house with a garden the game levelled up to a model of the same lot size since it was last planned
        /// (its garden comes out the same: its plan takes along only the gardens that touch it).</summary>
        private bool LevelUpSameLand(Entity house)
        {
            if (!IsYes(house) || GardenOf(house) == null) { LevelUpChecks[0]++; return false; }
            if (!_plannedModel.TryGetValue(house, out var was) || !EntityManager.HasComponent<PrefabRef>(house)) { LevelUpChecks[1]++; return false; }
            var now = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            if (now == was) { LevelUpChecks[2]++; return false; }
            if (!EntityManager.HasComponent<BuildingData>(now) || !EntityManager.HasComponent<BuildingData>(was)
                || !math.all(EntityManager.GetComponentData<BuildingData>(now).m_LotSize == EntityManager.GetComponentData<BuildingData>(was).m_LotSize)) { LevelUpChecks[3]++; return false; }
            LevelUpChecks[4]++;
            return true;
        }

        /// <summary>For the game runner: why houses put in line were or were not a level-up of the same lot (not polished or
        /// no garden, never planned, the same model, another lot size, a level-up of the same lot), and plans of one.</summary>
        public static readonly int[] LevelUpChecks = new int[5];
        public static int SameLandPlans;

        /// <summary>For the game runner: houses near an asked one left out of its plan, a road, path or track between them.</summary>
        public static int CoreAcrossRoad;

        /// <summary>A road, path or track on the ground (not one a building or build owns) lies across the straight line between
        /// two points: two lots it parts never share a border.</summary>
        private bool RoadBetween(float2 a, float2 b)
        {
            var lo = math.min(a, b);
            var hi = math.max(a, b);
            int x0 = (int)math.floor(lo.x / BarrierCellSize), x1 = (int)math.floor(hi.x / BarrierCellSize), y0 = (int)math.floor(lo.y / BarrierCellSize), y1 = (int)math.floor(hi.y / BarrierCellSize);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    long key = CellKey(x, y);
                    if (!_barrierCells.TryGetValue(key, out var cell)) _barrierCells[key] = cell = ReadBarrierCell(x, y);
                    foreach (var item in cell)
                    {
                        if (item.Category != 0 || item.Hi.x < lo.x || item.Lo.x > hi.x || item.Hi.y < lo.y || item.Lo.y > hi.y) continue;
                        if (item.E == Entity.Null || EntityManager.HasComponent<Owner>(item.E)) continue;
                        for (int k = 0; k < item.Rings.Count; k++)
                        {
                            if (item.Kinds[k] != 0) continue;   // (a deck: the ground under it is open)
                            var ring = item.Rings[k];
                            if (ring == null || ring.Length < 3) continue;
                            if (Poly.Contains(ring, a) || Poly.Contains(ring, b)) return true;
                            for (int m = 0, n = ring.Length - 1; m < ring.Length; n = m++)
                                if (Poly.SegmentsIntersect(a, b, ring[n], ring[m])) return true;
                        }
                    }
                }
            return false;
        }
    }
}
