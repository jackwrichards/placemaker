// Placemaker roads layer (s52) — "Hillside terraces": the settings of one terrace plan, read once from the bag.
// See docs/proposals/s52.md for the design. Pure.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads.Terraces
{
    internal enum TerraceLinkStyle { Direct, Serpentine, Spiral }

    internal sealed class TerraceOptions
    {
        /// <summary>Zone cells (8 m) kept level on each side of a terrace street.</summary>
        public int Cells = 4;
        /// <summary>Cross-fall of the terrace away from the bank (rise/run).</summary>
        public float CrossFall = 0.01f;
        /// <summary>Steep straight banks with crisp edges instead of rounded graded ones.</summary>
        public bool Wall;
        /// <summary>Terraced blocks: real retaining walls (a quay along the top of each deep cut), rounded banks elsewhere.</summary>
        public bool Retaining;
        /// <summary>Steepest bank (rise/run).</summary>
        public float BankSlope = 0.4f;
        public TerraceLinkStyle Links = TerraceLinkStyle.Direct;
        public float LinkSpacing = 300f;
        public bool AvenueLinks;
        /// <summary>Natural ground flatter than this (rise/run) is never terraced.</summary>
        public float FlatSlope = 0.05f;
        /// <summary>Deepest cut / highest fill (m).</summary>
        public float Earthwork = 8f;
        /// <summary>Steepest car road (rise/run): links between terraces aim just under it.</summary>
        public float MaxGrade = 0.1f;
        public bool Paint = true, Trees = true;
        /// <summary>Bank walks: 0 = none, 1 = densest.</summary>
        public float Walks = 0.5f;
        public string WalkStyle = "paved";
        /// <summary>Half width of a terrace street (m).</summary>
        public float StreetHalf = 6f;
        /// <summary>Half width of a link road (m).</summary>
        public float LinkHalf = 6f;
        public uint Seed = 1;

        /// <summary>Level shoulder between the zoned cells and the bank (m).</summary>
        public const float Shoulder = 2f;
        /// <summary>Level stretch of a link where it meets a terrace street (m): junctions on terraces are level.</summary>
        public const float Landing = 12f;
        /// <summary>Steepest bank walk (vanilla has no stairs: RoadWalkways.MaxWalkGrade).</summary>
        public const float WalkGrade = 0.075f;

        /// <summary>Zone depth on each side of a terrace street (m).</summary>
        public float ZoneDepth => Cells * 8f;
        /// <summary>Full half width of a terrace band: road, zone cells, shoulder.</summary>
        public float Band => StreetHalf + ZoneDepth + Shoulder;
        /// <summary>Centre-line radius of a hairpin (m): a two-lane street turns comfortably, an avenue wider.</summary>
        public float HairpinRadius => math.max(16f, LinkHalf * 2.6f);
        /// <summary>Smallest centre-line radius of a terrace street or a link outside the hairpins (m).</summary>
        public float StreetRadius => math.max(40f, StreetHalf * 6f);
        /// <summary>
        /// Mean bank slope the spacing is planned with: a rounded bank's steepest point is about 1.15× its mean, a wall's about
        /// its mean (straight face). The grade limit then holds every bank at <see cref="BankSlope"/>.
        /// </summary>
        public float MeanBank => BankSlope / (Wall ? 1.0f : 1.15f);

        public static TerraceOptions Read(SettingsBag s, IReadOnlyList<ControlSchema> controls, Func<NetRole, float> halfWidth)
        {
            float N(string id) => RoadsSchema.Num(s, controls, id);
            var o = new TerraceOptions
            {
                Cells = (int)math.clamp(math.round(N(RoadsIds.TerraceCells)), 1f, 6f),
                CrossFall = N(RoadsIds.TerraceFall) / 100f,
                Wall = s.S(RoadsIds.TerraceBank, "bank") == "wall",
                // the retaining walls (the game's quays) are gone: a saved "retaining" is a bank
                Retaining = false,
                BankSlope = N(RoadsIds.TerraceBankSlope) / 100f,
                LinkSpacing = N(RoadsIds.TerraceLinkSpacing),
                AvenueLinks = s.B(RoadsIds.TerraceAvenues, false),
                FlatSlope = N(RoadsIds.TerraceFlat) / 100f,
                Earthwork = N(RoadsIds.TerraceEarthwork),
                MaxGrade = N(RoadsIds.MaxGrade) / 100f,
                Paint = s.B(RoadsIds.TerracePaint, true),
                Trees = s.B(RoadsIds.TerraceTrees, true),
                Walks = N(RoadsIds.Walkways) / 100f,
                WalkStyle = s.S(RoadsIds.WalkwayStyle, "paved"),
            };
            string links = s.S(RoadsIds.TerraceLinks, "direct");
            o.Links = links == "serpentine" ? TerraceLinkStyle.Serpentine : links == "spiral" ? TerraceLinkStyle.Spiral : TerraceLinkStyle.Direct;
            if (halfWidth != null)
            {
                o.StreetHalf = math.clamp(halfWidth(NetRole.Street), 2f, 20f);
                o.LinkHalf = math.clamp(halfWidth(o.AvenueLinks ? NetRole.Avenue : NetRole.Street), 2f, 24f);
            }
            if (!(o.BankSlope > 0.05f)) o.BankSlope = 0.4f;
            // CS2 draws a jump steeper than ~50 % over one 3.5 m heightmap texel as a torn vertical sheet (RESEARCH-NOTES-
            // ADDENDUM 85): a "1 in 2" bank is held just under it.
            o.BankSlope = math.min(o.BankSlope, 0.46f);
            if (!(o.MaxGrade > 0.01f)) o.MaxGrade = 0.1f;
            return o;
        }
    }
}
