// Placemaker roads layer (s34) — "Pre-zone all new roads".
//
// Playtest (image 62): the Roads type zoned its frontage through the edge-zoning cutout feature — quads clipped by
// whatever the layer reserved, then snapped to cells — and the result was a scattered pattern of zoned and empty
// cells. The player wanted the obvious thing: every cell of every new road's zone blocks zoned with one zone type.
// The plan carries one zoning quad per predicted block (Build/ZoneBlockPredictor: the blocks CS2 will give each road);
// after the build the game layer re-reads the live blocks of the roads it just built and zones each one whole
// (FromLive), so splits and junctions the prediction cannot know are exact too. Cells the game blocks (under roads,
// buildings) simply stay empty; Marquee zoning never overwrites what the player zoned before.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    public static class RoadPreZone
    {
        public const string None = "none";
        /// <summary>New roads are not zoned unless the player picks a zone (the user: "that should be off by default"); the
        /// zone picked stays for later builds of the type (the tool remembers its last design) and across style changes.</summary>
        public const string Default = None;
        /// <summary>False: roads never zone, whatever a recipe says (the user, 26 Oct: zoning is the Zoning category's job).</summary>
        public const bool Offered = false;
        /// <summary>AreaPlan fact: the plan's zoning quads are "zone the new roads whole" (the game re-snaps them to the live blocks).</summary>
        public const string Fact = "roads.preZone";

        /// <summary>Choice values of the pre-zone control: none, then the zone types.</summary>
        public static string[] Values()
        {
            var v = new List<string> { None };
            v.AddRange(Common.ZoneKinds.All);
            return v.ToArray();
        }

        /// <summary>Zone prefab of a pre-zone choice ("" = none).</summary>
        public static string ZoneId(string choice) =>
            string.IsNullOrEmpty(choice) || choice == None ? "" : Common.ZoneKinds.PrefabId(choice);

        /// <summary>(s52) AreaPlan facts: zone at most this many rows from the road, and only rows on ground this level (rise/run).</summary>
        public const string FactDepth = "roads.preZoneDepth", FactLevel = "roads.preZoneLevel";

        /// <summary>
        /// (s52) Hillside terraces zone only the level part of each block: per column, the rows from the road whose cells lie
        /// level (<see cref="Level"/>), at most <see cref="MaxDepth"/> (0 = the whole block depth).
        /// </summary>
        public sealed class DepthRule
        {
            public int MaxDepth;
            public Func<float2, bool> Level;
            /// <summary>Terraced blocks: level across the block (the cell's centre and the block's direction to its road).</summary>
            public Func<float2, float2, bool> LevelAcross;
        }

        /// <summary>The steepest the ground may be along a block (terraced blocks: a street may climb).</summary>
        public const string FactAlong = "roads.preZoneAlong";

        /// <summary>
        /// Terraced blocks: a cell is level when, over the whole cell (its centre line and the lines 3 m either side of it, both
        /// ways), the ground 3.5 m either side ACROSS the block (towards and away from its road) differs by no more than
        /// <paramref name="maxCross"/> × 3.5 m (+ 5 cm), and along it by no more than <paramref name="maxAlong"/> × 3.5 m: a lot
        /// along a climbing street lies level across (the game levels its own lot), and no corner of it reaches into a bank.
        /// </summary>
        public static Func<float2, float2, bool> LevelAcross(Func<float2, float> ground, float maxCross, float maxAlong)
        {
            return (c, dir) =>
            {
                var d = math.normalizesafe(dir, new float2(0, 1));
                var t = new float2(d.y, -d.x);
                float limC = maxCross * 3.5f + 0.05f, limA = maxAlong * 3.5f + 0.05f;
                for (int k = -1; k <= 1; k++)
                {
                    var row = c + t * (3f * k);
                    float h = ground(row);
                    if (math.abs(ground(row + d * 3.5f) - h) > limC || math.abs(ground(row - d * 3.5f) - h) > limC) return false;
                    var col = c + d * (3f * k);
                    h = ground(col);
                    if (math.abs(ground(col + t * 3.5f) - h) > limA || math.abs(ground(col - t * 3.5f) - h) > limA) return false;
                }
                return true;
            };
        }

        /// <summary>
        /// The plan's depth rule (null = zone whole blocks): its facts and the ground after its terraform (else
        /// <paramref name="ground"/>). The game layer asks this for the live blocks after the build.
        /// </summary>
        public static DepthRule RuleOf(AreaPlan plan, Func<float2, float> ground = null)
        {
            if (plan?.Facts == null || !plan.Facts.TryGetValue(FactDepth, out double d) || !(d > 0)) return null;
            var g = plan.HeightAfter ?? ground;
            float slope = plan.Facts.TryGetValue(FactLevel, out double s) ? (float)s : 0f;
            if (plan.Facts.TryGetValue(FactAlong, out double along) && along > 0 && slope > 0f && g != null)
                return new DepthRule { MaxDepth = (int)Math.Round(d), LevelAcross = LevelAcross(g, slope, (float)along) };
            return new DepthRule { MaxDepth = (int)Math.Round(d), Level = slope > 0f && g != null ? LevelCell(g, slope) : null };
        }

        /// <summary>
        /// A cell is level when the ground 3.5 m either side of its centre (both axes) differs from the centre by no more than
        /// <paramref name="maxSlope"/> × 3.5 m (+ 5 cm).
        /// </summary>
        public static Func<float2, bool> LevelCell(Func<float2, float> ground, float maxSlope)
        {
            return c =>
            {
                float h = ground(c), lim = maxSlope * 3.5f + 0.05f;
                return math.abs(ground(c + new float2(3.5f, 0)) - h) <= lim && math.abs(ground(c - new float2(3.5f, 0)) - h) <= lim
                    && math.abs(ground(c + new float2(0, 3.5f)) - h) <= lim && math.abs(ground(c - new float2(0, 3.5f)) - h) <= lim;
            };
        }

        /// <summary>(s52) The cell rectangles of a block the rule zones: runs of columns with the same level depth.</summary>
        public static List<float2[]> Rects(ZoneGrid g, DepthRule rule)
        {
            var r = new List<float2[]>();
            if (g.Size.x <= 0 || g.Size.y <= 0) return r;
            if (rule == null) { r.Add(ZoneGuard.CellRect(g, 0, g.Size.x - 1, 0, g.Size.y - 1, -1f)); return r; }
            int cap = rule.MaxDepth > 0 ? math.min(rule.MaxDepth, g.Size.y) : g.Size.y;
            var depth = new int[g.Size.x];
            for (int x = 0; x < g.Size.x; x++)
            {
                int d = 0;
                while (d < cap && (rule.LevelAcross != null ? rule.LevelAcross(ZoneGuard.CellCentre(g, x, d), g.Direction) : rule.Level == null || rule.Level(ZoneGuard.CellCentre(g, x, d)))) d++;
                depth[x] = d;
            }
            int x0 = 0;
            for (int x = 1; x <= g.Size.x; x++)
            {
                if (x < g.Size.x && depth[x] == depth[x0]) continue;
                if (depth[x0] > 0) r.Add(ZoneGuard.CellRect(g, x0, x - 1, 0, depth[x0] - 1, -1f));
                x0 = x;
            }
            return r;
        }

        /// <summary>One zoning quad per predicted zone block of every zoning road in <paramref name="nets"/> (s52: per level part with a rule).</summary>
        public static List<PlannedZoning> Quads(IEnumerable<PlannedNet> nets, SiteContext site, string zoneId, Func<float2, float> height, DepthRule rule = null)
        {
            var r = new List<PlannedZoning>();
            if (string.IsNullOrEmpty(zoneId) || nets == null) return r;
            height = height ?? (_ => 0f);
            foreach (var n in nets)
            {
                if (n == null || !ZoneBlockPredictor.Zones(n.Role)) continue;
                if (site?.Assets != null && !string.IsNullOrEmpty(n.Asset.Id) && site.Assets.TryGetValue(n.Asset.Id, out var ai) && ai.ZoneBlocks == 2) continue;
                foreach (var g in ZoneBlockPredictor.Blocks(n.A, n.B, n.C, n.D, ZoneGuard.NetWidth(n, site)))
                {
                    if (rule == null) { r.Add(Whole(g, zoneId, height)); continue; }
                    foreach (var q in Rects(g, rule))
                    {
                        float3 P(float2 v) => new float3(v.x, height(v), v.y);
                        r.Add(new PlannedZoning { A = P(q[0]), B = P(q[1]), C = P(q[2]), D = P(q[3]), ZoneId = zoneId });
                    }
                }
            }
            return r;
        }

        /// <summary>The whole block as a Marquee quad (1 m inside the outer cell edges: the tool takes cells by centre).</summary>
        public static PlannedZoning Whole(ZoneGrid g, string zoneId, Func<float2, float> height)
        {
            var q = ZoneGuard.CellRect(g, 0, g.Size.x - 1, 0, g.Size.y - 1, -1f);
            float3 P(float2 v) => new float3(v.x, height(v), v.y);
            return new PlannedZoning { A = P(q[0]), B = P(q[1]), C = P(q[2]), D = P(q[3]), ZoneId = zoneId };
        }

        /// <summary>A live zone block and the curve of the road edge that owns it (game layer capture).</summary>
        public struct LiveBlock
        {
            public ZoneGrid Grid;
            public float3 OwnerA, OwnerB, OwnerC, OwnerD;
        }

        /// <summary>
        /// The live blocks that belong to roads of this plan (their owner edge lies on a planned zoning road: both ends and the
        /// middle within <paramref name="tolerance"/> of the same planned curve), each zoned whole.
        /// </summary>
        public static List<ZoneGuard.ZoningQuad> FromLive(IReadOnlyList<PlannedNet> nets, IReadOnlyList<LiveBlock> blocks, string zoneId, float tolerance = 1.5f,
            DepthRule rule = null)
        {
            var r = new List<ZoneGuard.ZoningQuad>();
            if (nets == null || blocks == null || string.IsNullOrEmpty(zoneId)) return r;
            var curves = new List<(RoadCurve c, float2 mn, float2 mx)>();
            foreach (var n in nets)
            {
                if (n == null || !ZoneBlockPredictor.Zones(n.Role)) continue;
                var c = new RoadCurve(n.A, n.B, n.C, n.D);
                float2 mn = math.min(math.min(n.A.xz, n.B.xz), math.min(n.C.xz, n.D.xz)) - tolerance;
                float2 mx = math.max(math.max(n.A.xz, n.B.xz), math.max(n.C.xz, n.D.xz)) + tolerance;
                curves.Add((c, mn, mx));
            }
            foreach (var b in blocks)
            {
                var owner = new RoadCurve(b.OwnerA, b.OwnerB, b.OwnerC, b.OwnerD);
                float2 a = owner.A.xz, m = owner.Position(0.5f).xz, d = owner.D.xz;
                bool ours = false;
                foreach (var (c, mn, mx) in curves)
                {
                    if (!In(a, mn, mx) || !In(d, mn, mx)) continue;
                    if (c.DistanceXZ(a, out _) <= tolerance && c.DistanceXZ(m, out _) <= tolerance && c.DistanceXZ(d, out _) <= tolerance) { ours = true; break; }
                }
                if (!ours || b.Grid.Size.x <= 0 || b.Grid.Size.y <= 0) continue;
                // (s52) Terraces: only the level rows of the block.
                foreach (var q in Rects(b.Grid, rule)) r.Add(new ZoneGuard.ZoningQuad { Corners = q, ZoneId = zoneId });
            }
            return r;
        }

        private static bool In(float2 p, float2 mn, float2 mx) => p.x >= mn.x && p.y >= mn.y && p.x <= mx.x && p.y <= mx.y;
    }
}
