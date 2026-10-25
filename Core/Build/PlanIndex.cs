// Placemaker — spatial index over planned items (GPL-3.0).
// Used to (a) recognise which temporary game entities came from OUR definitions (prefab +
// geometry must match — unlike a baseline diff this never adopts another tool's entities or a
// split piece of an existing road), and (b) map game error positions back to plan items for
// GridRoadGenerator-style auto-exclusion.

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public sealed class PlanIndex
    {
        private struct Entry
        {
            public long Key;
            public long Prefab;
            public bool IsCurve;
            public float2 Point;
            public Cubic Curve;
        }

        private readonly float _cell;
        private readonly List<Entry> _entries = new List<Entry>();
        // (perf) the flat cell table of SpatialIndex: a Dictionary of Lists was most of the census of a 5 000-piece build in Mono
        private readonly Placemaker.Core.Layers.Common.CellMap _grid = new Placemaker.Core.Layers.Common.CellMap();

        public PlanIndex(float cellSize = 16f) { _cell = math.max(1f, cellSize); }

        public int Count => _entries.Count;

        public void AddPoint(long key, long prefab, float2 p)
        {
            if (!math.all(math.isfinite(p))) return;
            int i = _entries.Count;
            _entries.Add(new Entry { Key = key, Prefab = prefab, Point = p });
            Insert(Cell(p), i);
        }

        public void AddCurve(long key, long prefab, Cubic c)
        {
            if (!CourseFitting.IsFinite(c)) return;
            int i = _entries.Count;
            _entries.Add(new Entry { Key = key, Prefab = prefab, IsCurve = true, Curve = c });
            var seen = new HashSet<long>();
            int samples = math.max(2, (int)math.ceil(c.LengthXZ() / (_cell * 0.5f)) + 1);
            for (int s = 0; s <= samples; s++)
            {
                var p = c.Position(s / (float)samples).xz;
                // Insert into the 3x3 neighbourhood so queries within one cell of tolerance hit it.
                long baseCell = Cell(p);
                int2 cc = Unpack(baseCell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        long k = Pack(cc + new int2(dx, dz));
                        if (seen.Add(k)) Insert(k, i);
                    }
            }
        }

        /// <summary>
        /// Nearest entry within <paramref name="tolerance"/> of p. prefab = 0 matches any prefab.
        /// Returns false when nothing matches.
        /// </summary>
        public bool TryMatch(float2 p, long prefab, float tolerance, out long key, out float distance)
        {
            key = 0; distance = float.MaxValue;
            if (!math.all(math.isfinite(p))) return false;
            int2 c = Unpack(Cell(p));
            int reach = (int)math.ceil(tolerance / _cell);
            bool found = false;
            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int en = _grid.First(c.x + dx, c.y + dz); en >= 0; en = _grid.Next(en))
                    {
                        int i = _grid.Item(en);
                        var e = _entries[i];
                        if (prefab != 0 && e.Prefab != prefab) continue;
                        float d = e.IsCurve ? e.Curve.DistanceXZ(p, out _) : math.distance(p, e.Point);
                        if (d <= tolerance && d < distance) { distance = d; key = e.Key; found = true; }
                    }
                }
            return found;
        }

        private void Insert(long cell, int index)
        {
            var c = Unpack(cell);
            if (_grid.LastItem(c.x, c.y) != index) _grid.Add(c.x, c.y, index);
        }

        private long Cell(float2 p) => Pack(new int2((int)math.floor(p.x / _cell), (int)math.floor(p.y / _cell)));
        private static long Pack(int2 c) => ((long)c.x << 32) ^ (uint)c.y;
        private static int2 Unpack(long k) => new int2((int)(k >> 32), (int)(uint)k);
    }
}
