// Placemaker — the lawn: a grass SURFACE or bare terrain + ground paint (s33, docs/TERRAIN-PAINT.md). GPL-3.0.
//
// A grass surface is one uniformly tiled texture that hides the terrain: no wear, no mulch, no lush/dry variation shows
// through it (s28 audit). CS2's own terrain already IS grass on flat ground (procedural base, "Grass_temperate"), with
// height/slope variation, and the paint materials blend into it softly. So naturalistic types now leave their lawn as
// bare terrain and let the Ground paint pass (Decals/GroundWear) tint and wear it; manicured types (formal gardens,
// memorials, cemeteries, botanical gardens, sports turf, fairgrounds) keep the grass surface. Every type that lays a
// lawn offers the choice ("dressLawn" in its Ground paint section: Painted terrain / Grass surface). A map whose base
// terrain is not grass (desert, rock) should use Grass surface — the panel says so in the tooltip.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class Lawn
    {
        public const string Painted = "painted";
        public const string Surface = "surface";
        /// <summary>Control id (Decals.DressingControls.LawnId).</summary>
        public const string ControlId = "dressLawn";
        /// <summary>The park's older on/off toggle ("Grass surface"): off still means no surface.</summary>
        public const string LegacyToggle = "groundSurface";

        /// <summary>Default lawn per fill type that lays a whole-area lawn. Types not listed have no lawn choice.</summary>
        private static readonly Dictionary<string, string> TypeDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Naturalistic, lived-in: bare terrain + paint.
            ["park"] = Painted,
            ["picnicGrounds"] = Painted,
            ["dogPark"] = Painted,
            ["greenwayPark"] = Painted,
            ["adventurePlayground"] = Painted,
            ["animalPark"] = Painted,
            ["communityGarden"] = Painted,
            ["orchardPark"] = Painted,
            // (2026-10-29, the user) the sports park's ground between its pitches and courts is painted terrain with the
            // shared decal library on it (Decals/Dynamic/Weathering); its turf, courts and paving stay surfaces.
            ["sportsPark"] = Painted,
            // Manicured: the grass surface.
            ["formalGarden"] = Surface,
            // (parks makeover, 2026-10-29) the botanical garden and the memorial park are painted terrain with a quiet decal ground
            // (ParkGround): the whole-site grass surface read as plastic in every picture (run show-22-builds-1029-232925-79c4).
            ["botanicalGarden"] = Painted,
            ["cemeteryGrounds"] = Surface,
            ["memorialPark"] = Painted,
            ["fairground"] = Surface,
            ["amphitheatre"] = Surface,
            ["skatePark"] = Surface,
            ["urbanFarm"] = Surface,
        };

        /// <summary>Presets that promise a manicured (or a worn) look different from their type's default.</summary>
        private static readonly Dictionary<string, string> PresetDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["park/formal"] = Surface,
            ["park/victorian"] = Surface,
            ["park/modern"] = Surface,
        };

        /// <summary>The default lawn of a type (and preset), or null when the type lays no lawn.</summary>
        public static string DefaultFor(string layerId, string presetId)
        {
            if (layerId == null || !TypeDefaults.TryGetValue(layerId, out var d)) return null;
            if (presetId != null && PresetDefaults.TryGetValue(layerId + "/" + presetId, out var p)) return p;
            return d;
        }

        public static IEnumerable<string> Types => TypeDefaults.Keys;

        /// <summary>True when this step's lawn is bare terrain + paint.</summary>
        public static bool IsPainted(LayerContext ctx)
        {
            if (ctx?.ControlOf?.Invoke(ControlId) == null) return false;
            return ctx.Settings.S(ControlId, Surface) == Painted;
        }

        /// <summary>
        /// Lays the lawn over <paramref name="ring"/>: the grass surface when the step's Lawn is "Grass surface", nothing
        /// when it is "Painted terrain" (the Ground paint pass tints and wears the bare terrain). Returns true when a
        /// surface was emitted.
        /// </summary>
        public static bool Cover(LayerContext ctx, IReadOnlyList<float2> ring, AssetRef asset, string category = null)
        {
            if (IsPainted(ctx)) return false;
            return ctx.Surface(ring, SurfaceRole.Grass, asset, category);
        }
    }
}
