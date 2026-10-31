// Placemaker — the one Ground section of every place type (Phase 1, 2026-10-30).
//
// The user could not find the decal switch and did not see the difference between "forest floor" and "Ground paint". Every
// fill type now shows one Ground section (the panel draws it under Parameters, the decal switch on its heading line) made of
// three parts, each a control group:
//   groundPaint  "Terrain paint": what is painted into the map's terrain texture (wear, lawn, mulch, the type's floor paint)
//   ground       "Ground cover":  what is laid on the ground (grass and paving surfaces, gravel, scree, rubble, stones)
//   terrain      "Shape":         how the ground is shaped (mounds, hills, rocky areas, ground patches)
// A type adds a ground control by putting it in one of these groups. Older type controls that paint the floor but sat in
// another section are moved here by id (Moves).

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Layers.Common
{
    public static class GroundSection
    {
        public const string Paint = Decals.DressingControls.PaintGroup;
        public const string Surfaces = "ground";
        public const string Shape = LookSettings.ShapeGroup;

        /// <summary>The groups that make up the Ground section, in display order.</summary>
        public static readonly string[] Groups = { Paint, Surfaces, Shape };

        public static bool IsGround(string group) => group == Paint || group == Surfaces || group == Shape;

        /// <summary>Floor paint controls of single types that sat in another section: (type, control) → Terrain paint.</summary>
        private static readonly Dictionary<string, string[]> Moves = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["natureReserve"] = new[] { "forestFloor", "floorLayer" },
            ["park"] = new[] { "naturalGround", "naturalLayer" },
            ["animalPark"] = new[] { "wornGround", "groundLayer" },
            ["fairground"] = new[] { "ground", "paintLayer" },
            ["golf"] = new[] { "roughPaint" },
            ["zenGarden"] = new[] { "mossPaint" },
            ["stripMall"] = new[] { "wornGround" },
            ["mallPark"] = new[] { "wornGround" },
            ["mallDistrict"] = new[] { "wornGround" },
        };

        /// <summary>Every (type, control) moved into Terrain paint (tests check each still exists).</summary>
        public static IEnumerable<(string Layer, string Control)> MovedIds
        {
            get { foreach (var kv in Moves) foreach (var c in kv.Value) yield return (kv.Key, c); }
        }

        /// <summary>A Ground cover control that paints the terrain texture ("paint", "marshPaint", "paintLayer"…).</summary>
        private static bool PaintsTerrain(string id) =>
            id == "paint" || id == "groundPaint" || id == "wornGround" || id == "desirePaths"
            || id.EndsWith("Paint", StringComparison.Ordinal) || id.EndsWith("Layer", StringComparison.Ordinal);

        /// <summary>
        /// Puts a fill type's floor paint controls into the Ground section's Terrain paint: the listed ones from other sections,
        /// and the terrain paint of its Ground cover section. (Zoning and outgrow types draw their own panel: untouched.)
        /// </summary>
        public static void Normalize(string layerId, LayerKind kind, List<ControlSchema> list)
        {
            if (kind != LayerKind.Fill || list == null || layerId == null) return;
            if (layerId.StartsWith("zoning", StringComparison.Ordinal) || layerId.StartsWith("polish", StringComparison.Ordinal)) return;
            Moves.TryGetValue(layerId, out var ids);
            foreach (var c in list)
            {
                if (c == null) continue;
                if (ids != null && Array.IndexOf(ids, c.Id) >= 0) c.Group = Paint;
                else if (c.Group == Surfaces && PaintsTerrain(c.Id)) c.Group = Paint;
            }
        }
    }
}
