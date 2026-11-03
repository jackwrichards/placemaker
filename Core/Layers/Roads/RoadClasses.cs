// Placemaker roads layer — what kind of road an existing road is (its place in the hierarchy).
//
// The roads type works with the network that is already there: it never links to a highway, it uses a big straight
// road through the area as its spine, and a small two-lane road stays a small road. All of that needs the class of an
// existing road, read from what the site snapshot carries: the game's highway flag (highways and their ramps), the
// speed limit, and the width (Two-Lane 16 m, Four-Lane 24 m, Six-Lane 32 m).

using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    /// <summary>Place of a road in the hierarchy, smallest first.</summary>
    public enum RoadRank { Lane = 0, Minor = 1, Medium = 2, Major = 3, Highway = 4 }

    public static class RoadClasses
    {
        /// <summary>Widest lane-class road (alleys, gravel roads: 8 m).</summary>
        public const float LaneWidth = 10.5f;
        /// <summary>Widest minor road (the two-lane roads: 16 m).</summary>
        public const float MinorWidth = 18.5f;
        /// <summary>Widest medium road (the four-lane roads: 24 m); wider is a major road (six lanes, 32 m).</summary>
        public const float MediumWidth = 27f;

        /// <summary>
        /// A highway or anything built like one: the game's highway rules (motorways and their ramps), or a fast road without
        /// sidewalks (a modded expressway).
        /// </summary>
        public static bool HighwayLike(NearbyNet n) => n.IsHighway || (n.SpeedLimit >= 90f && !n.HasSidewalk);

        public static RoadRank Rank(NearbyNet n) => HighwayLike(n) ? RoadRank.Highway : RankOfWidth(n.Width);

        public static RoadRank RankOfWidth(float width)
        {
            if (!(width > 0f) || !math.isfinite(width)) return RoadRank.Minor;
            if (width <= LaneWidth) return RoadRank.Lane;
            if (width <= MinorWidth) return RoadRank.Minor;
            if (width <= MediumWidth) return RoadRank.Medium;
            return RoadRank.Major;
        }

        /// <summary>The rank a planned road of this role has (what it will be in the game with the automatic picks).</summary>
        public static RoadRank RankOf(NetRole role, float halfWidth)
        {
            switch (role)
            {
                case NetRole.Alley: return RoadRank.Lane;
                case NetRole.Avenue: return halfWidth * 2f <= MinorWidth ? RoadRank.Minor : RankOfWidth(halfWidth * 2f);
                default: return RankOfWidth(halfWidth * 2f);
            }
        }
    }
}
