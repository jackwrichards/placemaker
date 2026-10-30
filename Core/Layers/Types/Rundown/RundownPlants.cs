// Placemaker — the wild plants of the city's forgotten places (docs/runbooks/rundown.md). GPL-3.0.
//
// Scrubby, dark and self-seeded: the game's green wild bushes at every age and its dead bush. Never FlowerBushWild01/02,
// FlowerBushWildElderly01 or FlowerBushWildChild02: they render pale teal-blue in the game (the parks agent took them out of
// every park palette; the coordinator saw them all over the rundown pictures, 2026-10-30).

namespace Placemaker.Core.Layers.Types.Rundown
{
    public static class RundownPlants
    {
        /// <summary>Thick wild scrub (an overgrown edge, a ruin taken back).</summary>
        public static readonly string[] Scrub = { "GreenBushWild01", "GreenBushWild02", "GreenBushWildAdult01", "GreenBushWildTeen01", "GreenBushWildElderly01", "GreenBushWildChild01", "FlowerBushWildDead02" };
        /// <summary>Young, sparse and half dead (trampled, dry or burnt ground).</summary>
        public static readonly string[] Sparse = { "GreenBushWildChild01", "GreenBushWildTeen01", "FlowerBushWildDead02", "FlowerBushWildDead02", "GreenBushWildChild01" };
        /// <summary>Burnt ground: mostly dead.</summary>
        public static readonly string[] Burnt = { "FlowerBushWildDead02", "FlowerBushWildDead02", "FlowerBushWildDead02", "GreenBushWildChild01" };
        /// <summary>Self-seeded pioneer trees (birch, poplar, alder take ruins first).</summary>
        public static readonly string[] Trees = { "BirchTree01", "EU_PoplarTree01", "EU_AlderTree01", "BirchTree01" };

        /// <summary>The player's pick when they made one, else ours (never the automatic pool: it holds the teal bushes).</summary>
        public static string[] Or(string[] chosen, string[] ours) => chosen != null && chosen.Length > 0 ? chosen : ours;
    }
}
