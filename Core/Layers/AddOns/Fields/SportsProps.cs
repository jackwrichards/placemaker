// Sports parks, round 2 (2026-10-29, the user: "channel this effort into making sports parks just as amazing"): the
// game's own sports props, named explicitly. The clean game ships real grandstands, dugouts, scoreboards, full-size
// goals and 22 m light masts, but none of them sat in a category with an automatic pool (they are catalogued as
// "decoration"), so every stand was a garden bench and every floodlight a 3 m park lamp.
//
//   stands      SportParkStandSmall01-03 (19-21 m long, 8-14 m deep, 5-9 m high) and SportParkStandLarge01-04
//               (22 or 41 m long, 24 m deep, 11-16 m high: covered grandstands), laid end to end along a front line.
//               The seats look along the prop's −Z (StandFacing); the front row is the −Z edge of its bounds.
//   corners     SportParkStandSmallCorner01/02, SportParkStandLargeCorner01/02: the quarter pieces between two sides.
//   masts       LightpoleIndustrial03/04 (22 m) for pitches and tracks, LightpoleCommercial03/04 (11 m) for courts.
//   dugouts     SportParkBench02 (a covered team bench, 6.9 m), SportParkBench01 (an open one, 6.4 m).
//   goals       SportParkFootballGoal01 (7.8 m, full size) and 02 (4 m, small-sided), SportParkAmericanFootballGoal01.
//   details     SportParkScoreboard01, SportParkFlag01 (corner flags), training cones, hurdles, dummies and poles.
//
// Bounds from the ModsData catalog dump (2026-10-29, clean profile).

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    public static class SportsProps
    {
        public struct Stand
        {
            public string Id;
            /// <summary>Length along the row (prop X), the front (+Z) and back (−Z) reach from the origin, the height.</summary>
            public float Length, Front, Back, Height;
            public float Depth => Front + Back;
            public Stand(string id, float length, float front, float back, float height) { Id = id; Length = length; Front = front; Back = back; Height = height; }
        }

        // Front = −(−Z bound), Back = +Z bound: the front row looks along −Z (StandFacing).
        public static readonly Stand Small02 = new Stand("SportParkStandSmall02", 19.34f, 4.23f, 4.17f, 5.0f);
        public static readonly Stand Small01 = new Stand("SportParkStandSmall01", 20.64f, 5.32f, 4.44f, 7.79f);
        public static readonly Stand Small03 = new Stand("SportParkStandSmall03", 19.95f, 7.57f, 6.71f, 8.72f);
        public static readonly Stand Large02 = new Stand("SportParkStandLarge02", 21.26f, 13.31f, 10.67f, 11.26f);
        public static readonly Stand Large01 = new Stand("SportParkStandLarge01", 21.98f, 14.11f, 9.84f, 16.19f);
        public static readonly Stand Large03 = new Stand("SportParkStandLarge03", 41.36f, 10.79f, 13.20f, 11.77f);
        public static readonly Stand Large04 = new Stand("SportParkStandLarge04", 41.60f, 14.31f, 10.15f, 16.19f);
        public static readonly Stand SmallCorner01 = new Stand("SportParkStandSmallCorner01", 22.26f, 5.86f, 4.81f, 7.79f);
        public static readonly Stand SmallCorner02 = new Stand("SportParkStandSmallCorner02", 20.76f, 4.69f, 4.55f, 5.0f);
        public static readonly Stand LargeCorner01 = new Stand("SportParkStandLargeCorner01", 26.34f, 15.08f, 9.59f, 16.19f);
        public static readonly Stand LargeCorner02 = new Stand("SportParkStandLargeCorner02", 22.52f, 14.65f, 10.17f, 11.26f);
        /// <summary>A row of spectator benches when no stand fits (the game's 6.4 m sports bench).</summary>
        public static readonly Stand BenchRow = new Stand("SportParkBench01", 6.35f, 0.35f, 0.26f, 1.08f);

        /// <summary>Yaw offset of a stand: its seats look along −Z (game runner, 2026-10-29: turned along +Z they faced away from the track).</summary>
        public static float StandFacing = math.PI;

        public const string MastBig = "LightpoleIndustrial04", MastBigAlt = "LightpoleIndustrial03";
        public const string MastCourt = "LightpoleCommercial04", MastCourtAlt = "LightpoleCommercial03";
        public const string Dugout = "SportParkBench02", OpenBench = "SportParkBench01";
        public const string GoalFull = "SportParkFootballGoal01", GoalSmall = "SportParkFootballGoal02", GoalPosts = "SportParkAmericanFootballGoal01";
        public const string Scoreboard = "SportParkScoreboard01", CornerFlag = "SportParkFlag01";
        public static readonly string[] Training = { "SportParkTrainingCone02", "SportParkTrainingHurdle01", "SportParkTrainingPole01", "SportParkTrainingDummy02", "SportParkTrainingDummy01" };

        /// <summary>The deepest small stand that fits <paramref name="depth"/> (front row to back wall), else the bench row.</summary>
        public static Stand SmallFor(float depth, float height = 99f)
        {
            if (depth >= Small03.Depth + 0.1f && height >= Small03.Height) return Small03;
            if (depth >= Small01.Depth + 0.1f && height >= Small01.Height) return Small01;
            if (depth >= Small02.Depth + 0.1f) return Small02;
            return BenchRow;
        }

        /// <summary>The stand family for a row: covered grandstands when <paramref name="depth"/> allows, else small stands, else benches.</summary>
        public static Stand[] FamilyFor(float depth, bool grand)
        {
            if (grand && depth >= Large04.Depth + 0.1f) return new[] { Large04, Large03, Large01, Large02 };
            if (grand && depth >= Large02.Depth + 0.1f) return new[] { Large03, Large02 };
            var s = SmallFor(depth);
            if (s.Id == BenchRow.Id) return new[] { BenchRow };
            return s.Id == Small03.Id ? new[] { Small03, Small01, Small02 } : s.Id == Small01.Id ? new[] { Small01, Small02 } : new[] { Small02 };
        }

        /// <summary>
        /// Stands end to end along the front line a → b, their seats looking at <paramref name="toward"/>, at most
        /// <paramref name="depth"/> deep. Long pieces first, centred on the line; a piece that does not fit the occupancy is
        /// left out. Returns the stands placed (and their footprint in <paramref name="taken"/>).
        /// </summary>
        public static int StandRow(LayerContext ctx, Occupancy occ, float2 a, float2 b, float2 toward, float depth, bool grand, uint seed,
            List<OrientedRect> taken = null, bool checkFree = true)
        {
            float len = math.distance(a, b);
            if (len < 5f) return 0;
            var d = (b - a) / len;
            var n = new float2(-d.y, d.x);
            if (math.dot(toward - (a + b) * 0.5f, n) < 0f) n = -n;      // n: towards the field
            var fam = FamilyFor(depth, grand);
            // Fill the line: long pieces while they fit, then the shorter ones; centred.
            var pieces = new List<Stand>();
            float rest = len;
            foreach (var s in fam)
                while (rest >= s.Length + 0.4f && pieces.Count < 24) { pieces.Add(s); rest -= s.Length + 0.4f; }
            if (pieces.Count == 0) return 0;
            // Alternate the long and short pieces so the row reads as one grandstand with a central block.
            pieces.Sort((x, y) => y.Length.CompareTo(x.Length));
            var order = new List<Stand>();
            for (int i = 0; i < pieces.Count; i++) { if ((i & 1) == 0) order.Add(pieces[i]); else order.Insert(0, pieces[i]); }
            float total = 0f; foreach (var s in order) total += s.Length + 0.4f;
            float t = (len - total) * 0.5f + 0.2f;
            float yaw = math.atan2(n.x, n.y);
            int placed = 0;
            for (int i = 0; i < order.Count; i++)
            {
                var s = order[i];
                var front = a + d * (t + s.Length * 0.5f);
                var origin = front - n * s.Front;
                var foot = new OrientedRect { Center = origin + n * ((s.Front - s.Back) * 0.5f), Axis = d, HalfLength = s.Length * 0.5f, HalfWidth = s.Depth * 0.5f };
                t += s.Length + 0.4f;
                if (checkFree && !RectFree(occ, foot)) continue;
                bool bench = s.Id == BenchRow.Id;
                // The game's sports benches (SportParkBench01/02) look along −Z like the stands, unlike park benches (lab
                // send-lab-sports-seats-1030-111839: turned like a park bench they sat backwards, the user 2026-10-30).
                ctx.Object(origin, yaw + StandFacing, bench ? ObjectRole.Bench : ObjectRole.Furniture, new AssetRef(s.Id, bench ? OpenBench : Small02.Id),
                    Hashing.Mix(seed, (uint)i), -1, bench ? null : AssetCategories.SportsStand);
                // A polygon, not a corridor: the next stand of the row stands right beside it.
                occ.TakePolygon(foot.Ring);
                taken?.Add(foot);
                placed++;
            }
            return placed;
        }

        /// <summary>A corner stand at <paramref name="corner"/> (the corner of the two front lines), facing the field's centre.</summary>
        public static bool CornerStand(LayerContext ctx, Occupancy occ, float2 corner, float2 centre, bool grand, uint seed)
        {
            var s = grand ? LargeCorner02 : SmallCorner02;
            var n = math.normalizesafe(centre - corner);
            var origin = corner - n * s.Front * 0.7f;
            var foot = new OrientedRect { Center = origin, Axis = new float2(n.y, -n.x), HalfLength = s.Length * 0.45f, HalfWidth = s.Depth * 0.4f };
            if (!RectFree(occ, foot)) return false;
            ctx.Object(origin, math.atan2(n.x, n.y) + StandFacing, ObjectRole.Furniture, new AssetRef(s.Id, Small02.Id), seed, -1, AssetCategories.SportsStand);
            occ.TakePolygon(foot.Ring);
            return true;
        }

        /// <summary>The footprint lies inside the occupancy's ground and off its paths, discs and polygons.</summary>
        public static bool RectFree(Occupancy occ, OrientedRect r)
        {
            foreach (var q in r.Ring) if (!occ.LayerRegion.Contains(q)) return false;
            if (!FieldKit.QuickFree(occ, r, 0.2f)) return false;
            for (float u = -r.HalfLength + 1f; u <= r.HalfLength - 1f + 1e-3f; u += 2.5f)
                for (float v = -r.HalfWidth + 1f; v <= r.HalfWidth - 1f + 1e-3f; v += 2.5f)
                {
                    var p = r.Local(u, v);
                    if (occ.Discs.Overlaps(p, 0.9f, 0f) || occ.HitsPolygon(p, 0.9f)) return false;
                }
            return true;
        }

        /// <summary>A light mast (22 m for fields, 11 m for courts) aimed at <paramref name="aim"/>.</summary>
        public static bool Mast(LayerContext ctx, Occupancy occ, float2 p, float2 aim, bool big, uint seed, string chosen = null, bool check = true)
        {
            // No lamps at all (an asset set without any): no masts either.
            if (string.IsNullOrEmpty(chosen) && !AssetAvailability.Has(AssetCategories.Lamp)) return false;
            var asset = AssetMix.One(chosen ?? "", big ? MastBig : MastCourt, big ? MastBigAlt : MastCourtAlt);
            if (check && !occ.Free(p, 0.9f, 0.2f, 0.1f)) return false;
            ctx.Object(p, LeisureKit.Yaw(p, aim), ObjectRole.Lamp, asset, seed, -1, AssetCategories.Lamp);
            occ.Take(p, 0.9f, 1);
            return true;
        }

        /// <summary>A prop at an exact spot (scoreboard, flag, training kit, dugout); <paramref name="facing"/> = where its +Z looks.</summary>
        public static bool Prop(LayerContext ctx, Occupancy occ, float2 p, float2 facing, string id, ObjectRole role, float radius, uint seed, string category = null, bool check = true, params string[] fallbacks)
        {
            if (check && !occ.Free(p, radius, 0.1f, 0.05f)) return false;
            var f = math.normalizesafe(facing, new float2(0f, 1f));
            ctx.Object(p, math.atan2(f.x, f.y), role, new AssetRef(id, fallbacks), seed, -1, category);
            occ.Take(p, radius, 1);
            return true;
        }
    }
}
