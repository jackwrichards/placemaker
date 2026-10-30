// Placemaker — dynamic decals: one instance of a shared-library variant in a plan (docs/DYNAMIC-DECALS.md, "The shared
// library"; docs/DECALS-PLAYBOOK.md section 3). GPL-3.0.
//
// A plan names library pieces, it never carries pixels: a kind ("leafOak"), which of its variants (0..count-1), where
// (x, z), how it is turned, stretched and faded. Brushes (Brushes.cs) make these from shapes and causes; the build writes
// them on the group record (DecalCodec v2) and the game draws them with SharedDecals (one texture set per variant for the
// whole city). Nothing is an entity or a prefab: without Placemaker the record is dropped and nothing is left.

using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public struct DecalInstance
    {
        /// <summary>Library kind id ("leafOak").</summary>
        public string Kind;
        /// <summary>Variant index of the kind (0..count-1).</summary>
        public int Variant;
        /// <summary>Centre (x, z) in world metres.</summary>
        public float2 P;
        /// <summary>Yaw in radians, the game's RotateY: local +x goes to world (cos, −sin); a strip's heavy side (+z) to (sin, cos).</summary>
        public float Yaw;
        /// <summary>Stretch of the variant's box along its x and z (1 = as baked). Strips stretch x to fit a run.</summary>
        public float2 Scale;
        /// <summary>0..1 times the kind's own opacity.</summary>
        public float Opacity;
        /// <summary>Why it is there (planning only, not saved): "leaves", "seam:path" … for logs and tests.</summary>
        public string Cause;

        public DecalInstance(string kind, int variant, float2 p, float yaw, float2 scale, float opacity, string cause = null)
        {
            Kind = kind; Variant = variant; P = p; Yaw = yaw; Scale = scale; Opacity = opacity; Cause = cause;
        }

        public string Id => DecalVariants.Id(Kind, Variant);

        /// <summary>World half extents of the box (x, z) for a variant span (metres).</summary>
        public float2 HalfExtent(float2 span)
        {
            var h = span * Scale * 0.5f;
            float c = math.abs(math.cos(Yaw)), s = math.abs(math.sin(Yaw));
            return new float2(h.x * c + h.y * s, h.x * s + h.y * c);
        }

        public override string ToString() => $"{Id} @({P.x:F1},{P.y:F1}) {Cause}";
    }
}
