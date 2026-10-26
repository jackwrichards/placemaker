// Placemaker — ordering of automatic pools (GPL-3.0). Pure, shared by the game catalog
// (Game/Assets/AssetCatalogSystem.BuildAutomatic) and the curation test that replays a real
// catalog-dump.json (Tests/Placemaker.Tests/AssetCurationTests.cs), so docs/ASSET-CURATION.md shows
// exactly what the game picks.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Build
{
    public readonly struct PoolCandidate
    {
        public readonly string Id;
        public readonly bool Vanilla;
        /// <summary>A network fence (continuous, corner-joined): always before prop fence pieces.</summary>
        public readonly bool NetFence;
        public readonly float NetWidth;
        /// <summary>Largest horizontal size of the prefab in metres (0 = unknown).</summary>
        public readonly float Footprint;

        public PoolCandidate(string id, bool vanilla, bool netFence = false, float netWidth = 0f, float footprint = 0f)
        {
            Id = id; Vanilla = vanilla; NetFence = netFence; NetWidth = netWidth; Footprint = footprint;
        }
    }

    public static class AutoPoolRanking
    {
        public const int PoolSize = 12;

        /// <summary>Sort key: lower tier first (vanilla, then modded; prop fences after net fences), then higher score.</summary>
        public static (int tier, int score) Rank(string category, in PoolCandidate c)
        {
            int score = AssetHeuristics.DefaultScore(category, c.Id, c.NetWidth);
            int tier = c.Vanilla ? 0 : 1;
            if (category == AssetCategories.Fence && !c.NetFence) tier += 2;
            // Scatter decals read as wear only when small: 30-60 m "ground texture" sheets painted the
            // whole park (playtest 4b, gallery). Big ones stay pickable, just last.
            if (c.Footprint > 0f && (category == AssetCategories.Decal || category.StartsWith("decal.", StringComparison.Ordinal)))
            {
                if (c.Footprint > 12f) score -= 300;
                else if (c.Footprint > 7f) score -= 60;
            }
            return (tier, score);
        }

        public static int Compare(string category, in PoolCandidate a, in PoolCandidate b)
        {
            var ra = Rank(category, a); var rb = Rank(category, b);
            if (ra.tier != rb.tier) return ra.tier.CompareTo(rb.tier);
            if (ra.score != rb.score) return rb.score.CompareTo(ra.score);
            return string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>
        /// The automatic pool of a category from its eligible items: ranked, distinct ids, vanilla only (the user, 26 Oct:
        /// "everything should be vanilla by default"; a category vanilla has nothing for stays empty and is left out; the
        /// player can still pick any installed asset).
        /// </summary>
        public static List<string> Build(string category, IEnumerable<PoolCandidate> eligible, int size = PoolSize)
        {
            var ranked = new List<PoolCandidate>(eligible);
            ranked.Sort((a, b) => Compare(category, a, b));
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in ranked)
            {
                if (!c.Vanilla) continue;
                if (string.IsNullOrEmpty(c.Id) || !AssetCuration.FitsSize(category, c.Footprint) || !seen.Add(c.Id)) continue;
                names.Add(c.Id);
                if (names.Count >= size) break;
            }
            return names;
        }
    }
}
