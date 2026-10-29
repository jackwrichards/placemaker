// Placemaker — dynamic decals: one stroke of grime in a plan (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// A plan stores recipes, not pixels: a stroke names a kind (a recipe in the decal library, Recipes/decals/kinds.json),
// a seed, a footprint (a box, a path with widths, or an outline) and a few knobs (strength, wetness, age, the game's
// decal layers it lies on, its draw order). The game turns strokes into textures on worker threads and draws them on
// plain renderers with the game's own decal material; nothing is an entity or a prefab, so a save never names them.
// Strokes are saved on Placemaker's group record (DecalCodec) and regenerated after a load.

using System;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public enum StrokeShape : byte
    {
        /// <summary>A box (centre, yaw, size) with a soft rounded footprint.</summary>
        Box = 0,
        /// <summary>A path through points with a width at every point (a strip: kerb grime, wheel paths, drifts, desire lines).</summary>
        Path = 1,
        /// <summary>An outline (a worn lawn shaped like the lawn, a whole car park's tone).</summary>
        Outline = 2,
    }

    /// <summary>The game's decal layers (DecalLayers): what a decal may lie on. Terrain covers bare ground and every surface
    /// (paving, asphalt lots, grass surfaces); Roads covers nets (paths, streets, sidewalks).</summary>
    public static class DecalLayerBits
    {
        public const byte Terrain = 1, Roads = 2, Buildings = 4, Vehicles = 8, Creatures = 16, Other = 32;
        public const byte Ground = Terrain | Roads;

        public static byte Parse(string s, byte fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            byte m = 0;
            foreach (var p in s.Split(',', '|', ' ', '+'))
                switch (p.Trim().ToLowerInvariant())
                {
                    case "terrain": m |= Terrain; break;
                    case "roads": case "paths": m |= Roads; break;
                    case "ground": m |= Ground; break;
                    case "buildings": m |= Buildings; break;
                    case "other": m |= Other; break;
                }
            return m == 0 ? fallback : m;
        }
    }

    public sealed class DecalStroke
    {
        /// <summary>Recipe id in the decal library (or a kind the plan carries itself, <see cref="DecalLibrary"/>).</summary>
        public string Kind = "";
        public uint Seed;
        public StrokeShape Shape;

        // Box
        public float2 Center;
        /// <summary>Yaw in radians, the game's RotateY: local +x goes to world (cos, −sin).</summary>
        public float Yaw;
        public float2 Size;

        // Path: points (x, z) and a full width at each point. Outline: points form a ring.
        public float2[] Points = Array.Empty<float2>();
        public float[] Widths = Array.Empty<float>();

        /// <summary>Overall opacity 0..1 (the kind's own alpha × this).</summary>
        public float Strength = 1f;
        /// <summary>0 dry .. 1 soaked (a puddle with water, or only its dry tidemark).</summary>
        public float Wetness;
        /// <summary>0 fresh .. 1 old (faded, broken up).</summary>
        public float Age = 0.5f;
        /// <summary>DecalLayerBits; 0 = the kind's default.</summary>
        public byte Layers;
        /// <summary>Draw order among our decals (higher draws over lower); the kind's order is added.</summary>
        public sbyte Order;
        /// <summary>Ground height at the stroke when planned (the game samples the real ground; this is the fallback).</summary>
        public float Y;
        /// <summary>Why it is there (planning only, not saved): "goalmouth", "puddle", "leafDrift" … for logs and tests.</summary>
        public string Cause = "";

        public static DecalStroke Box(string kind, uint seed, float2 c, float yaw, float2 size, float strength = 1f) =>
            new DecalStroke { Kind = kind, Seed = seed, Shape = StrokeShape.Box, Center = c, Yaw = yaw, Size = size, Strength = strength };

        public static DecalStroke Path(string kind, uint seed, float2[] pts, float width, float strength = 1f)
        {
            var w = new float[pts?.Length ?? 0];
            for (int i = 0; i < w.Length; i++) w[i] = width;
            return new DecalStroke { Kind = kind, Seed = seed, Shape = StrokeShape.Path, Points = pts ?? Array.Empty<float2>(), Widths = w, Strength = strength };
        }

        public static DecalStroke Outline(string kind, uint seed, float2[] ring, float strength = 1f) =>
            new DecalStroke { Kind = kind, Seed = seed, Shape = StrokeShape.Outline, Points = ring ?? Array.Empty<float2>(), Strength = strength };

        /// <summary>World bounds of the footprint (x, z), widths included.</summary>
        public void Bounds(out float2 lo, out float2 hi)
        {
            if (Shape == StrokeShape.Box)
            {
                float c = math.abs(math.cos(Yaw)), s = math.abs(math.sin(Yaw));
                var half = new float2(Size.x * 0.5f * c + Size.y * 0.5f * s, Size.x * 0.5f * s + Size.y * 0.5f * c);
                lo = Center - half; hi = Center + half;
                return;
            }
            lo = new float2(float.MaxValue); hi = new float2(float.MinValue);
            float pad = 0f;
            for (int i = 0; i < Points.Length; i++)
            {
                lo = math.min(lo, Points[i]); hi = math.max(hi, Points[i]);
                if (Shape == StrokeShape.Path && i < Widths.Length) pad = math.max(pad, Widths[i] * 0.5f);
            }
            if (Points.Length == 0) { lo = hi = Center; }
            lo -= pad; hi += pad;
        }

        /// <summary>Area of the footprint in m² (paths: length × mean width).</summary>
        public float Area()
        {
            switch (Shape)
            {
                case StrokeShape.Box: return Size.x * Size.y * 0.85f;
                case StrokeShape.Path:
                    float a = 0f;
                    for (int i = 1; i < Points.Length; i++)
                        a += math.distance(Points[i - 1], Points[i]) * 0.5f * (W(i - 1) + W(i));
                    return a;
                default: return Geometry.Poly.Area(Points);
            }
        }

        private float W(int i) => i < Widths.Length ? Widths[i] : (Widths.Length > 0 ? Widths[Widths.Length - 1] : 1f);

        public DecalStroke Clone()
        {
            var c = (DecalStroke)MemberwiseClone();
            c.Points = (float2[])Points.Clone();
            c.Widths = (float[])Widths.Clone();
            return c;
        }

        public override string ToString() => $"{Kind}#{Seed:X} {Shape} {Cause}";
    }
}
