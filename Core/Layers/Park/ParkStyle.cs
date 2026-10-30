// Composition parameters per park style. The "style" control picks one row; densities and
// assets stay separate controls so a player can have e.g. a dense formal park.
//
// Natural row = ParkManager's tuned values (grove coverage 28 %, grove/belt/solitaire shares
// 55/30/12 % for trees). Other rows are variations measured against the same 1–5 ha test
// shapes so the tree count still follows the density slider.

namespace Placemaker.Core.Layers.Park
{
    public sealed class ParkStyle
    {
        public string Id;
        /// <summary>Share of the park area covered by grove outlines.</summary>
        public float GroveCoverage;
        public float TreeGroveShare, TreeBeltShare, TreeSolitaireCap;
        public float BushGroveShare, BushBeltShare;
        /// <summary>Formal planting: trees as avenues along paths, bushes as hedges along the boundary.</summary>
        public bool Formal;
        /// <summary>Clearing radius relative to the largest lawn's inscribed radius (0 = no protected clearing).</summary>
        public float ClearingFactor;
        /// <summary>Default for curved paths when the player has not set it.</summary>
        public bool Curved;
        /// <summary>(round 5) Share of trees / bushes planted beside the paths (lined walks, shrubbery borders).</summary>
        public float TreePathShare, BushPathShare;
        /// <summary>(round 5) Modern planting: trees in a strict grid (bosque) on the lawns, bushes as clipped boundary rows.</summary>
        public bool Grid;
        /// <summary>(round 5) The tree belt as one regular row along the outline (street-tree style) instead of a natural band.</summary>
        public bool RowBelt;
        /// <summary>(round 5) Grove size multiplier and the flattest grove outline allowed (1 = circle).</summary>
        public float GroveScale = 1f, GroveAspectMin = 0.55f;
        /// <summary>(round 5) "Natural ground" patch multiplier (wildflower meadows paint much more of the lawn).</summary>
        public float GroundPatches = 1f;

        /// <summary>Every style id, in control order.</summary>
        public static readonly string[] Ids = { "natural", "formal", "woodland", "meadow", "pocket", "victorian", "landscape", "modern", "wildflower", "urban", "riverside", "picnic", "garden", "wild", "neglected" };

        public static readonly ParkStyle Natural = new ParkStyle
        {
            Id = "natural", GroveCoverage = 0.28f, TreeGroveShare = 0.55f, TreeBeltShare = 0.30f, TreeSolitaireCap = 0.12f,
            BushGroveShare = 0.5f, BushBeltShare = 0.4f, ClearingFactor = 0.7f, Curved = true,
        };

        public static readonly ParkStyle Woodland = new ParkStyle
        {
            Id = "woodland", GroveCoverage = 0.6f, TreeGroveShare = 0.72f, TreeBeltShare = 0.2f, TreeSolitaireCap = 0.08f,
            BushGroveShare = 0.65f, BushBeltShare = 0.3f, ClearingFactor = 0.45f, Curved = true,
        };

        public static readonly ParkStyle Meadow = new ParkStyle
        {
            Id = "meadow", GroveCoverage = 0.1f, TreeGroveShare = 0.35f, TreeBeltShare = 0.25f, TreeSolitaireCap = 0.4f,
            BushGroveShare = 0.35f, BushBeltShare = 0.45f, ClearingFactor = 0.85f, Curved = true,
        };

        public static readonly ParkStyle Pocket = new ParkStyle
        {
            Id = "pocket", GroveCoverage = 0.15f, TreeGroveShare = 0.3f, TreeBeltShare = 0.55f, TreeSolitaireCap = 0.15f,
            BushGroveShare = 0.3f, BushBeltShare = 0.6f, ClearingFactor = 0.6f, Curved = true,
        };

        public static readonly ParkStyle FormalStyle = new ParkStyle
        {
            Id = "formal", Formal = true, ClearingFactor = 0.8f, Curved = false,
        };

        // ───── round 5 (type wave, builder 11): six historic / contemporary park idioms ─────

        /// <summary>Victorian: specimen trees, shrubbery borders along serpentine walks, an open lawn for the bandstand.</summary>
        public static readonly ParkStyle Victorian = new ParkStyle
        {
            Id = "victorian", GroveCoverage = 0.14f, TreeGroveShare = 0.3f, TreeBeltShare = 0.3f, TreeSolitaireCap = 0.3f, TreePathShare = 0.25f,
            BushGroveShare = 0.25f, BushBeltShare = 0.3f, BushPathShare = 0.4f, ClearingFactor = 0.75f, Curved = true,
        };

        /// <summary>English landscape (Brown/Repton): big round clumps on open rolling lawn, a deep perimeter belt.</summary>
        public static readonly ParkStyle Landscape = new ParkStyle
        {
            Id = "landscape", GroveCoverage = 0.15f, GroveScale = 1.6f, GroveAspectMin = 0.8f, TreeGroveShare = 0.45f, TreeBeltShare = 0.45f,
            TreeSolitaireCap = 0.1f, BushGroveShare = 0.4f, BushBeltShare = 0.55f, ClearingFactor = 0.95f, Curved = true,
        };

        /// <summary>Modern minimal: a strict tree grid (bosque) and clipped boundary rows; straight lines.</summary>
        public static readonly ParkStyle Modern = new ParkStyle
        {
            Id = "modern", Grid = true, ClearingFactor = 0.8f, Curved = false,
        };

        /// <summary>Wildflower meadow: almost no trees, painted natural ground, a scrubby edge.</summary>
        public static readonly ParkStyle Wildflower = new ParkStyle
        {
            Id = "wildflower", GroveCoverage = 0.04f, TreeGroveShare = 0.3f, TreeBeltShare = 0.3f, TreeSolitaireCap = 0.4f,
            BushGroveShare = 0.3f, BushBeltShare = 0.6f, ClearingFactor = 0f, Curved = true, GroundPatches = 3f,
        };

        /// <summary>Urban pocket: street-tree rows along the edge, trees beside the walks, little else.</summary>
        public static readonly ParkStyle Urban = new ParkStyle
        {
            Id = "urban", GroveCoverage = 0.06f, RowBelt = true, TreeGroveShare = 0.2f, TreeBeltShare = 0.5f, TreeSolitaireCap = 0.1f,
            TreePathShare = 0.3f, BushGroveShare = 0.2f, BushBeltShare = 0.4f, BushPathShare = 0.4f, ClearingFactor = 0.6f, Curved = true,
        };

        /// <summary>Riverside: trees lining the walks (willow walk), loose groves, open views across the lawn.</summary>
        public static readonly ParkStyle Riverside = new ParkStyle
        {
            Id = "riverside", GroveCoverage = 0.18f, TreeGroveShare = 0.35f, TreeBeltShare = 0.15f, TreeSolitaireCap = 0.15f, TreePathShare = 0.4f,
            BushGroveShare = 0.4f, BushBeltShare = 0.3f, BushPathShare = 0.25f, ClearingFactor = 0.6f, Curved = true, GroundPatches = 1.5f,
        };

        /// <summary>(rf-parks) Picnic lawn (picnic grounds folded into the park): a big open lawn ringed by broad shade-tree clumps.</summary>
        public static readonly ParkStyle Picnic = new ParkStyle
        {
            Id = "picnic", GroveCoverage = 0.16f, GroveScale = 1.3f, GroveAspectMin = 0.7f, TreeGroveShare = 0.45f, TreeBeltShare = 0.4f, TreeSolitaireCap = 0.15f,
            BushGroveShare = 0.45f, BushBeltShare = 0.5f, ClearingFactor = 1f, Curved = true,
        };

        /// <summary>(rf-parks) Garden walk: natural groves, but the walks are lined with flowering shrub borders and a few specimen trees.</summary>
        public static readonly ParkStyle Garden = new ParkStyle
        {
            Id = "garden", GroveCoverage = 0.2f, TreeGroveShare = 0.45f, TreeBeltShare = 0.25f, TreeSolitaireCap = 0.15f, TreePathShare = 0.15f,
            BushGroveShare = 0.2f, BushBeltShare = 0.2f, BushPathShare = 0.55f, ClearingFactor = 0.6f, Curved = true,
        };

        /// <summary>(makeover) Country park: a rough, genuine park of the edge of town: native groves, long grass, trails, few lamps.</summary>
        public static readonly ParkStyle Wild = new ParkStyle
        {
            Id = "wild", GroveCoverage = 0.24f, GroveScale = 1.2f, TreeGroveShare = 0.62f, TreeBeltShare = 0.25f, TreeSolitaireCap = 0.13f,
            BushGroveShare = 0.55f, BushBeltShare = 0.4f, ClearingFactor = 0.55f, Curved = true, GroundPatches = 2f,
        };

        /// <summary>(makeover) Neglected: a park left to itself: thickets closing in, overgrown edges, old and dead trees.</summary>
        public static readonly ParkStyle Neglected = new ParkStyle
        {
            Id = "neglected", GroveCoverage = 0.34f, TreeGroveShare = 0.5f, TreeBeltShare = 0.35f, TreeSolitaireCap = 0.15f,
            BushGroveShare = 0.45f, BushBeltShare = 0.45f, BushPathShare = 0.1f, ClearingFactor = 0.5f, Curved = true, GroundPatches = 1.5f,
        };

        public static ParkStyle Get(string id)
        {
            switch (id)
            {
                case "victorian": return Victorian;
                case "landscape": return Landscape;
                case "modern": return Modern;
                case "wildflower": return Wildflower;
                case "urban": return Urban;
                case "riverside": return Riverside;
                case "picnic": return Picnic;
                case "garden": return Garden;
                case "wild": return Wild;
                case "neglected": return Neglected;
                case "formal": return FormalStyle;
                case "woodland": return Woodland;
                case "meadow": return Meadow;
                case "pocket": return Pocket;
                default: return Natural;
            }
        }
    }
}
