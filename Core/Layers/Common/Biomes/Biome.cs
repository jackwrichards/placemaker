// Placemaker — Biome mix: a site split into natural areas of different character (2026-10-30, docs/runbooks/biomes.md).
//
// The user: "I want almost biomes inside of a park that I can tweak ... percentages of each ... a super rocky area or a super
// forest area or just a mulch or a flower field ... each subtype could tweak them to their own desire or be a split or just be
// one or the other." Earlier pieces each did a part (the parks' moods, the rocky-outcrop setting, the nature groves); this is the
// one system that replaces them.
//
// A biome is DATA: a complete ground design (terrain shape, terrain paint, planting, rocks and logs, its decals) described by
// numbers, so any agent adds one by adding an entry below (farms: orchard, field; beaches: dune, shingle; golf: rough, heath).
// What a biome does is read by BiomeBuilder (objects, ground, paint) and BiomeGround (decals); where it goes by BiomeField
// (noise shaped by the land). A type offers some biomes and their default shares (BiomeMix.Offer), its styles preset them.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common.Biomes
{
    /// <summary>Where a decal pass of a biome lays its pieces.</summary>
    public enum BiomeWhere : byte
    {
        /// <summary>Evenly over the whole area.</summary>
        All = 0,
        /// <summary>Thicker toward the area's heart (deep inside), fading out at its edge.</summary>
        Core = 1,
        /// <summary>A band along the area's edge (the ecotone: where it gives way to its neighbour).</summary>
        Rim = 2,
        /// <summary>Downhill side of the ground (scree below rock, silt below a bank).</summary>
        Downhill = 3,
        /// <summary>The shady side (+z) of lumps: moss.</summary>
        Shade = 4,
        /// <summary>Round the area's own rocks and logs.</summary>
        Things = 5,
        /// <summary>Just outside the area, on its neighbour's side of the edge (scree spilling onto grass, reeds' litter on the bank).</summary>
        Spill = 6,
        /// <summary>In drifts of its own: a noise field per pass picks where (flower colours each in their own clumps).</summary>
        Drift = 7,
        /// <summary>Along the water of the area's ponds and pools (0.3-5 m from it): reed beds, mud.</summary>
        Water = 8,
        /// <summary>Inside the area's beds (mulch beds round the shrubs).</summary>
        Beds = 9,
        /// <summary>Along a few trodden lines wandering through the area (sandy paths over a heath).</summary>
        Trails = 10,
        /// <summary>Along the area's planted rows (RowPitch): the bare strip under an orchard's trees, windfalls.</summary>
        Rows = 11,
        /// <summary>The grass alleys between the planted rows: mown strips, the mower's and the tractor's wheel lines.</summary>
        Alleys = 12,
    }

    /// <summary>A layer of low plants of a biome: tussocks, reeds, heather, flowering drifts (vanilla bushes, scaled).</summary>
    public sealed class BiomePlants
    {
        public string[] Ids = Array.Empty<string>();
        public float PerHa;
        public float ScaleLo = 0.5f, ScaleHi = 1f;
        /// <summary>Clump field scale (m): small = tight drifts with open ground between.</summary>
        public float Drift = 8f;
        /// <summary>All, Core, Rim, Water (0.3-5 m from the area's water), Beds, Drift (its own noise drifts).</summary>
        public BiomeWhere Where;
        /// <summary>Spacing radius (m).</summary>
        public float Radius = 0.8f;

        public BiomePlants(float perHa, float lo, float hi, BiomeWhere where, float drift, params string[] ids)
        { PerHa = perHa; ScaleLo = lo; ScaleHi = hi; Where = where; Drift = drift; Ids = ids; Radius = math.max(0.35f, 0.9f * hi); }

        public BiomePlants Copy() => (BiomePlants)MemberwiseClone();
    }

    /// <summary>One decal pass of a biome (shared library kinds, Recipes/decals/library).</summary>
    public sealed class BiomeDecal
    {
        public string Kind = "";
        /// <summary>Share of the weighted area covered (Brushes.Scatter coverage; overlaps count, 0.5 already reads full).</summary>
        public float Cover;
        public float OpacityLo = 0.5f, OpacityHi = 0.9f;
        public float Clump = 3f;
        public BiomeWhere Where;
        /// <summary>Quiet colour level (odd variants) instead of any.</summary>
        public bool Quiet;
        /// <summary>Brush: Scatter (default), or Fill (one shape filling the area's heart: marsh mud, silt).</summary>
        public bool Fill;

        public BiomeDecal(string kind, float cover, BiomeWhere where = BiomeWhere.All, float lo = 0.5f, float hi = 0.9f, float clump = 3f, bool quiet = false)
        { Kind = kind; Cover = cover; Where = where; OpacityLo = lo; OpacityHi = hi; Clump = clump; Quiet = quiet; }

        public BiomeDecal Copy() => (BiomeDecal)MemberwiseClone();
    }

    /// <summary>A biome: a complete ground design as numbers (see the file's header).</summary>
    public sealed class BiomeDef
    {
        /// <summary>Id: the control id is "biome" + Id with a capital (biomeRocky), the label "Placemaker.Biome.&lt;id&gt;".</summary>
        public string Id = "";
        /// <summary>The panel's colour for its part of the mix bar.</summary>
        public string Color = "#888888";
        /// <summary>The host's own ground: nothing is laid, the type plants and furnishes it as its style says (a park's lawn).</summary>
        public bool Host;
        /// <summary>Facilities, play areas and clearings may stand here (lawn, meadow).</summary>
        public bool Open;
        /// <summary>
        /// (P2-8) Clean grass: the shared lawn variation (GroundWear's lush and dry paint patches, Weathering's lawn tone, mottle,
        /// dry grass and clover decals) stays off it, so it reads as the game's own grass plus only its own decals.
        /// </summary>
        public bool Plain;

        // ── where it likes to be (added to its noise; about -2 .. 2) ──
        /// <summary>High ground: ridges, knolls, the top of a slope.</summary>
        public float Ridge;
        /// <summary>Low ground: hollows, the foot of a slope, where water gathers.</summary>
        public float Hollow;
        /// <summary>Steep ground (grade 15 % and more).</summary>
        public float Steep;
        /// <summary>A moderate slope (5-15 %).</summary>
        public float Slope;
        /// <summary>Level open ground.</summary>
        public float Flat;
        /// <summary>Near the paths (+) or away from them (−).</summary>
        public float NearPath;
        /// <summary>Near the site's outline (+: belts, screens) or in the middle (−).</summary>
        public float NearEdge;
        /// <summary>Never closer to a path than this (m; water and marsh keep off the walks).</summary>
        public float PathKeep;
        /// <summary>Not on ground steeper than this (grade; 0 = any).</summary>
        public float MaxGrade;

        // ── the ground ──
        /// <summary>Raise (m) of its broken heaps and knolls at the heart of the area (rocky ground).</summary>
        public float Lift;
        /// <summary>Small-scale roughness (m) laid over the area.</summary>
        public float Break;
        /// <summary>Shallow dip (m) of its hollows (marsh).</summary>
        public float Dip;
        /// <summary>The area is dug into ponds with water (water).</summary>
        public bool Water;
        /// <summary>Share of its heart that stands in shallow water pools (marsh).</summary>
        public float Pools;
        /// <summary>Share of the area laid out as planted beds (mulch beds with lawn between); 0 = none.</summary>
        public float Beds;
        /// <summary>
        /// Mean radius (m) of island beds: round, separate beds with mown lawn between (each ±35 %, a wobbling edge) instead of
        /// noise patches that run into each other. 0 = noise patches.
        /// </summary>
        public float BedRadius;
        /// <summary>Trees stand only inside the beds (a specimen tree in its bed of shrubs).</summary>
        public bool TreesInBeds;
        /// <summary>Trees stand only in the rim band this wide (m): a glade's ring of trees round its open heart. 0 = anywhere.</summary>
        public float TreeRim;
        /// <summary>Trees in straight planted rows this far apart (m; an orchard, a vineyard, a tree nursery), along the site's
        /// row axis (BiomeSite.RowAxis, else the site's long axis); 0 = natural groves (NatureKit). Rows keep TreesPerHa unused.</summary>
        public float RowPitch;
        /// <summary>Spacing of the trees along a row (m).</summary>
        public float RowSpacing = 5f;
        /// <summary>Gaps in the rows (share of trees missing: an old orchard's dead or grubbed trees).</summary>
        public float RowGaps;
        /// <summary>Tree scale range of the rows (orchard trees are kept low by pruning).</summary>
        public float RowScaleLo = 0.75f, RowScaleHi = 1f;
        /// <summary>Low plant layers (tussocks, reeds, heather, flower drifts).</summary>
        public BiomePlants[] Plants = Array.Empty<BiomePlants>();

        // ── terrain paint (TerrainMaterials 1 dry, 2 forest floor, 3 earth, 4 sand) ──
        public int PaintLayer;
        public float PaintStrength;
        /// <summary>A second paint at the heart (earth under the rocks, mud in the marsh).</summary>
        public int CorePaint;
        public float CoreStrength;

        // ── what grows and lies there (per hectare of the area) ──
        public float TreesPerHa;
        public string[] Trees = Array.Empty<string>();
        /// <summary>Grove scale (m) of the clump field: small = a closed wood, big = groves and glades.</summary>
        public float GroveScale = 28f;
        /// <summary>Share of the old trees left as dead snags.</summary>
        public float Dead;
        public float BushesPerHa;
        public string[] Bushes = Array.Empty<string>();
        /// <summary>Bushes thicken this much at the area's rim (the forest edge's scrub: 2 = twice as many in the rim band).</summary>
        public float RimScrub = 1f;
        /// <summary>Rock outcrops (crowns of massed big boulders) per hectare.</summary>
        public float CrownsPerHa;
        /// <summary>Loose boulders per hectare (half sunk, broken stones at their foot).</summary>
        public float BouldersPerHa;
        /// <summary>Small stones per hectare (Rock01-30), thinning toward the edge: rock giving way to scree, then grass.</summary>
        public float StonesPerHa;
        /// <summary>Boulder size factor.</summary>
        public float RockSize = 1f;
        public float LogsPerHa;

        // ── decals (BiomeGround) ──
        public BiomeDecal[] Decals = Array.Empty<BiomeDecal>();

        /// <summary>(P2-8) The biome's own settings in the panel (null: made from what it has, BiomeKnobs.Of).</summary>
        public BiomeKnob[] Knobs;

        /// <summary>A copy to tune (BiomeKnobs.Apply replaces the arrays it changes, never edits them).</summary>
        public BiomeDef Copy() => (BiomeDef)MemberwiseClone();

        public string ControlId => BiomeMix.ControlIdOf(Id);
        public string LabelKey => "Placemaker.Biome." + Id;
        public string TooltipKey => "Placemaker.Biome." + Id + ".Tooltip";
    }

    /// <summary>
    /// The biome library. Starters for parks; Phase 2 agents add theirs here (one entry each) and offer them on their types
    /// (BiomeMix.Offer). Ids are saved in designs (as control ids): never rename one, hide it by not offering it.
    /// </summary>
    public static class BiomeLibrary
    {
        // vanilla planting and rocks (ParkArt, NatureKit and the moods use the same; never FlowerBushWild01: pale teal in the game)
        private static readonly string[] Conifers = { "PineTree01", "SpruceTree01", "PineTree01", "EuropeanSpruce01", "BirchTree01" };
        private static readonly string[] Woods = { "OakTree01", "BirchTree01", "SpruceTree01", "EU_AlderTree01", "NA_HickoryTree01", "PineTree01", "OakTree01", "EU_ChestnutTree01", "SpruceTree01" };
        private static readonly string[] GladeTrees = { "OakTree01", "BirchTree01", "EU_ChestnutTree01", "OakTree01", "PineTree01" };
        private static readonly string[] Solitary = { "OakTree01", "EU_ChestnutTree01", "NA_LindenTree01" };
        private static readonly string[] Wet = { "EU_AlderTree01", "EU_PoplarTree01", "BirchTree01", "EU_AlderTree01" };
        private static readonly string[] Heath = { "BirchTree01", "PineTree01", "BirchTree01", "BirchTree01", "PineTree01" };
        private static readonly string[] Ornamental = { "BirchTree01", "NA_LindenTree01", "EU_ChestnutTree01" };
        private static readonly string[] Scrub = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildElderly01", "GreenBushWildTeen01", "GreenBushWildChild01" };
        private static readonly string[] Under = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWildElderly01" };
        private static readonly string[] Reeds = { "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWild01", "GreenBushWildTeen01" };
        // the flower field's flowers are its ground (decals); a few low shrubs, mostly green (massed lilac read as pale clumps from 45°)
        private static readonly string[] Flowers = { "GreenBushWildChild01", "FlowerBushWildChild02", "GreenBushWildTeen01", "GreenBushWildChild01", "FlowerBushWildChild02", "GreenBushWild02" };
        // P2-4 (the lead: meadow, mulch and heath all read as scattered wild bushes): beds take the city's clipped garden shrubs,
        // heather is only low dark wild clumps (the lilac FlowerBushWildChild02 read as pale blobs on the heath)
        // P2-8: no flowering city shrubs (pastel teal balls from 45°): clipped greens of three sizes
        private static readonly string[] Beds_ = { "GreenBushCity01", "GreenBushCity03", "GreenBushCity02", "GreenBushCity01", "GreenBushWildAdult01", "GreenBushCity03" };
        private static readonly string[] Heather = { "GreenBushWildChild01", "GreenBushWildElderly01", "GreenBushWildChild01" };

        public static readonly BiomeDef Lawn = new BiomeDef
        {
            Id = "lawn", Color = "#8cc152", Host = true, Open = true,
            Flat = 0.6f, NearPath = 0.7f, Ridge = -0.1f, Steep = -0.5f,
        };

        public static readonly BiomeDef Rocky = new BiomeDef
        {
            Id = "rocky", Color = "#9a9187",
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Rocks, 100, 200), BiomeKnob.Slider(BiomeKnobs.Relief, 100, 200).Named("height"), BiomeKnob.Slider(BiomeKnobs.Trees, 36, 120, "perHa") },
            Ridge = 1.2f, Steep = 1.4f, Slope = 0.3f, Hollow = -0.9f, Flat = -0.2f, NearPath = -0.3f,
            Lift = 3.4f, Break = 0.6f,
            PaintLayer = 3, PaintStrength = 0.7f, CorePaint = 3, CoreStrength = 1f,
            TreesPerHa = 36f, Trees = Conifers, GroveScale = 12f, Dead = 0.15f,
            BushesPerHa = 70f, Bushes = Scrub, RimScrub = 1.6f,
            CrownsPerHa = 12f, BouldersPerHa = 60f, StonesPerHa = 200f, RockSize = 1.3f,
            Decals = new[]
            {
                new BiomeDecal("rockOutcrop", 0.55f, BiomeWhere.Core, 0.7f, 1f, 3f),
                new BiomeDecal("stonesCarpet", 0.32f, BiomeWhere.Core, 0.55f, 0.9f, 4f),
                new BiomeDecal("screeApron", 0.4f, BiomeWhere.Downhill, 0.6f, 0.95f, 2.5f),
                new BiomeDecal("screeFan", 0.3f, BiomeWhere.Rim, 0.55f, 0.9f, 2.5f),
                new BiomeDecal("gravelCarpet", 0.18f, BiomeWhere.Spill, 0.4f, 0.75f, 3f, quiet: true),
                new BiomeDecal("mossPatch", 0.14f, BiomeWhere.Shade, 0.35f, 0.65f, 2f),
                new BiomeDecal("dryGrass", 0.18f, BiomeWhere.Rim, 0.25f, 0.5f, 3f, quiet: true),
            },
        };

        public static readonly BiomeDef Forest = new BiomeDef
        {
            Id = "forest", Color = "#2f6b3a",
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Trees, 260, 400, "perHa"), BiomeKnob.Choice(BiomeKnobs.Kind, 0, "mixed", "broadleaf", "conifer", "birch"), BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200).Named("undergrowth"), BiomeKnob.Slider(BiomeKnobs.DeadWood, 100, 200) },
            Slope = 0.8f, Steep = 0.4f, NearEdge = 0.6f, Flat = -0.1f, Ridge = 0.1f, NearPath = -0.1f,
            Lift = 0.45f, Break = 0.15f,
            PaintLayer = 2, PaintStrength = 0.85f, CorePaint = 2, CoreStrength = 1f,
            TreesPerHa = 260f, Trees = Woods, GroveScale = 9f, Dead = 0.06f,
            BushesPerHa = 150f, Bushes = Under, RimScrub = 2.6f,
            LogsPerHa = 9f, BouldersPerHa = 4f, StonesPerHa = 10f, RockSize = 0.8f,
            Decals = new[]
            {
                new BiomeDecal("woodlandFloorCarpet", 0.5f, BiomeWhere.Core, 0.55f, 0.9f, 5f),
                new BiomeDecal("leafLitterWild", 0.4f, BiomeWhere.All, 0.5f, 0.85f, 4f, quiet: true),
                new BiomeDecal("twigs", 0.16f, BiomeWhere.Core, 0.5f, 0.85f, 3f),
                new BiomeDecal("mossPatch", 0.12f, BiomeWhere.Things, 0.4f, 0.7f, 2f),
                new BiomeDecal("exposedRoots", 0.05f, BiomeWhere.Rim, 0.5f, 0.85f, 2f),
            },
        };

        public static readonly BiomeDef Glade = new BiomeDef
        {
            // an open, sunny heart ringed by trees: the trees keep to a 12 m rim band, flowers and long grass in the middle
            Id = "glade", Color = "#5f9a4b",
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Trees, 70, 150, "perHa"), BiomeKnob.Slider(BiomeKnobs.Flowers, 100, 200), BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200) },
            Slope = 0.4f, Flat = 0.3f, NearEdge = 0.2f,
            PaintLayer = 2, PaintStrength = 0.35f,
            TreesPerHa = 70f, Trees = GladeTrees, GroveScale = 14f, Dead = 0.04f, TreeRim = 12f,
            BushesPerHa = 40f, Bushes = Scrub, RimScrub = 2.4f,
            LogsPerHa = 3f,
            Plants = new[]
            {
                new BiomePlants(220f, 0.45f, 0.7f, BiomeWhere.Core, 7f, "GreenBushWildChild01", "GreenBushWildTeen01"),
            },
            Decals = new[]
            {
                new BiomeDecal("longGrass", 0.55f, BiomeWhere.Core, 0.55f, 0.85f, 5f),
                new BiomeDecal("flowerDriftCream", 0.25f, BiomeWhere.Drift, 0.7f, 1f, 4f),
                new BiomeDecal("flowerDriftPurple", 0.2f, BiomeWhere.Drift, 0.7f, 1f, 4f),
                new BiomeDecal("leafLitterWild", 0.3f, BiomeWhere.Rim, 0.45f, 0.8f, 3f, quiet: true),
                new BiomeDecal("mossPatch", 0.06f, BiomeWhere.Things, 0.35f, 0.6f, 2f),
            },
        };

        public static readonly BiomeDef Meadow = new BiomeDef
        {
            // open long grass: the sward itself is the look (long grass over the whole area, flowers in it), only a scatter of
            // tussocks and a shrub or two at its edge, one or two old solitary trees. P2-4: 520 tussocks a hectare read as bushes.
            Id = "meadow", Color = "#c9b45a", Open = true, Plain = true,   // P2-8: the sward is its look (the shared lush paint under it read as dark blotches)
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Trees, 0, 20, "perHa"), BiomeKnob.Slider(BiomeKnobs.Flowers, 100, 200), BiomeKnob.Choice(BiomeKnobs.Grass, 1, "short", "long", "tall"), BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200) },
            Flat = 0.9f, Slope = 0.2f, Hollow = 0.1f, NearPath = -0.1f, Steep = -0.3f,
            // P2-8 (the user: "even when I turn it to full meadow, I still get some trees"): a meadow is open grass, no trees or
            // scrub of its own; a player who wants a lone oak in it sets the biome's Trees
            TreesPerHa = 0f, Trees = Solitary, GroveScale = 60f,
            BushesPerHa = 0f, Bushes = Scrub, RimScrub = 3f,
            Plants = new[]
            {
                // P2-8: a few tussocks at the rim (the flowering city shrubs read as pale teal balls in the open grass: the flowers are the decals)
                new BiomePlants(18f, 0.35f, 0.55f, BiomeWhere.Rim, 6f, "GreenBushWildChild01", "GreenBushWildTeen01"),
            },
            Decals = new[]
            {
                // the sward carries the look; long grass under it only faintly (at full strength its pale tufts read as white blotches)
                new BiomeDecal("meadowSward", 1.1f, BiomeWhere.All, 0.65f, 0.85f, 6f),
                new BiomeDecal("longGrass", 0.45f, BiomeWhere.Core, 0.4f, 0.65f, 6f, quiet: true),
                new BiomeDecal("flowerDriftCream", 0.14f, BiomeWhere.Drift, 0.6f, 0.9f, 4f),
                new BiomeDecal("flowerDriftPurple", 0.1f, BiomeWhere.Drift, 0.6f, 0.9f, 4f),
                new BiomeDecal("flowerDriftYellow", 0.06f, BiomeWhere.Drift, 0.5f, 0.8f, 4f),
                new BiomeDecal("molehills", 0.02f, BiomeWhere.All, 0.6f, 0.9f, 2f),
            },
        };

        public static readonly BiomeDef Grass = new BiomeDef
        {
            // (P2-8, the user: "maybe one that is just simpler, literally grass with some leaves, more vanilla") the game's own
            // grass as it is: no paint, no wear, no flowers, no long edges; only a light scatter of summer leaves and a faint
            // tone so it is not a flat sheet. Trees only if the player adds a few (then they stand as solitary trees).
            Id = "grass", Color = "#5fae6e", Open = true, Plain = true,
            Flat = 1f, Slope = 0.4f, NearPath = 0.3f, Steep = -0.4f,
            TreesPerHa = 0f, Trees = Solitary, GroveScale = 40f,
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Leaves, 100, 300), BiomeKnob.Slider(BiomeKnobs.Trees, 0, 20, "perHa") },
            Decals = new[]
            {
                new BiomeDecal("leafLitterSummer", 0.16f, BiomeWhere.All, 0.35f, 0.65f, 5f, quiet: true),
            },
        };

        public static readonly BiomeDef Field = new BiomeDef
        {
            // (P2-8, the user: "sometimes I just want a park to be a field ... but even a field should have good looking decals")
            // a big mown field, nothing fancy: short grass with a gentle tone, an unmown long-grass margin round its edge, worn
            // desire lines across it, bald and trampled patches where people play, clover and daisies, molehills, damp in the
            // low ground; no stripes (the shared rule: stripes only on bounded pitches), no trees unless the player wants a few
            // at its edge (Trees), and then leaves under them. Real fields: Clapham Common, Hampstead Heath's Parliament Hill
            // fields, the Sheep Meadow (Central Park), the Tempelhofer Feld, a village recreation ground.
            Id = "field", Color = "#c3dc6c", Open = true, Plain = true,
            Flat = 1.3f, Slope = 0.2f, Steep = -0.9f, Hollow = 0.1f, Ridge = 0.05f, NearPath = 0.1f, NearEdge = -0.2f,
            TreesPerHa = 0f, Trees = Solitary, GroveScale = 40f, TreeRim = 7f,
            // round 3 (the user, 2026-10-31: on an open field the decal spots "look like a piss stain or a poop stain or smudge ...
            // with the subtle terrain paint, it looks great"): the field's character is terrain paint only, soft and wide: a faint
            // lush tone in big drifts, a worn heart (Wear) where people play; a long-grass band along its edge (a strip, not spots);
            // no stamped spots on the open grass. Leaves only under edge trees.
            PaintLayer = 2, PaintStrength = 0.3f, CorePaint = 3, CoreStrength = 0.18f,   // round 4: 0.22 / 0.14 barely showed (show-2-builds-1031-135837)
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Wear, 100, 200), BiomeKnob.Slider(BiomeKnobs.Edges, 100, 200), BiomeKnob.Slider(BiomeKnobs.Trees, 0, 20, "perHa").Named("edgeTrees") },
            Decals = new[]
            {
                new BiomeDecal("longGrass", 0.6f, BiomeWhere.Rim, 0.35f, 0.55f, 4f, quiet: true),
                new BiomeDecal("leafLitterSummer", 0.14f, BiomeWhere.Rim, 0.4f, 0.7f, 3f, quiet: true),   // only with edge trees (BiomeKnobs.Apply)
            },
        };

        public static readonly BiomeDef FlowerField = new BiomeDef
        {
            // a sown wildflower field: drifts of single colours the size real drifts are (purple, red, blue, yellow, cream), the
            // flowering shrubs and low green clumps massed in them
            Id = "flowers", Color = "#c86fb4", Open = true,
            Knobs = new[] { BiomeKnob.Choice(BiomeKnobs.Colours, 0, "mixed", "warm", "cool", "purple", "yellow", "white"), BiomeKnob.Slider(BiomeKnobs.Flowers, 100, 200), BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200) },
            Flat = 1f, Slope = 0.3f, Steep = -0.6f, Hollow = -0.1f, NearPath = 0.2f,
            PaintLayer = 2, PaintStrength = 0.3f,
            TreesPerHa = 0f, Trees = Solitary, GroveScale = 60f,
            BushesPerHa = 0f, Bushes = Flowers,
            Plants = new[]
            {
                // P2-8 (the game: pastel teal and yellow balls everywhere, read as confetti): the flowers are the decals; only low green clumps in the drifts
                new BiomePlants(60f, 0.35f, 0.55f, BiomeWhere.Drift, 8f, "GreenBushWildChild01", "GreenBushWildTeen01"),
                new BiomePlants(70f, 0.4f, 0.6f, BiomeWhere.All, 6f, "GreenBushWildChild01", "GreenBushWildTeen01"),
            },
            Decals = new[]
            {
                new BiomeDecal("longGrass", 0.5f, BiomeWhere.All, 0.5f, 0.8f, 5f),
                new BiomeDecal("flowerDriftPurple", 0.45f, BiomeWhere.Drift, 0.8f, 1f, 5f),
                new BiomeDecal("flowerDriftRed", 0.35f, BiomeWhere.Drift, 0.8f, 1f, 5f),
                new BiomeDecal("flowerDriftBlue", 0.3f, BiomeWhere.Drift, 0.8f, 1f, 5f),
                new BiomeDecal("flowerDriftYellow", 0.35f, BiomeWhere.Drift, 0.8f, 1f, 5f),
                new BiomeDecal("flowerDriftCream", 0.3f, BiomeWhere.Drift, 0.8f, 1f, 5f),
            },
        };

        public static readonly BiomeDef Mulch = new BiomeDef
        {
            // planted beds on bark mulch with mown lawn between: each bed an organic shape round its shrubs and small trees,
            // the mulch dark and closed inside it, a clean edge
            // P2-4: island beds (round, separate, a mown lawn between them), a specimen tree in the bigger beds, clipped garden
            // shrubs packed inside; the lawn between is the host's (no paint), so each bed reads as a defined shape.
            Id = "mulch", Color = "#7b5236",
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Beds, 36, 80), BiomeKnob.Slider(BiomeKnobs.Trees, 22, 60, "perHa"), BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200) },
            NearPath = 0.8f, Flat = 0.4f, Slope = 0.1f, Steep = -0.4f,
            Beds = 0.36f, BedRadius = 5f, TreesInBeds = true,
            TreesPerHa = 22f, Trees = Ornamental, GroveScale = 18f,
            BushesPerHa = 0f, Bushes = Beds_,
            Plants = new[]
            {
                new BiomePlants(1500f, 0.65f, 1.1f, BiomeWhere.Beds, 6f, Beds_),
                new BiomePlants(300f, 0.4f, 0.6f, BiomeWhere.Beds, 5f, "GreenBushCity03", "GreenBushWildChild01"),
            },
            Decals = new[]
            {
                new BiomeDecal("mulchBed", 1f, BiomeWhere.Beds, 0.9f, 1f, 5f),
            },
        };

        public static readonly BiomeDef Wetland = new BiomeDef
        {
            // a marsh: pools of standing water in its heart, reed beds massed along them, tussocks on the dark wet ground, mud,
            // alder carr in groups
            Id = "wetland", Color = "#4f8f86",
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Water, 30, 70), BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200).Named("reeds"), BiomeKnob.Slider(BiomeKnobs.Trees, 30, 80, "perHa") },
            Hollow = 1.6f, Flat = 0.3f, Steep = -1.4f, Ridge = -1.1f, Slope = -0.3f, NearPath = -0.4f,
            PathKeep = 3.5f, MaxGrade = 0.14f,
            Dip = 0.55f, Break = 0.12f, Pools = 0.3f,
            PaintLayer = 2, PaintStrength = 1f, CorePaint = 2, CoreStrength = 1f,
            TreesPerHa = 30f, Trees = Wet, GroveScale = 16f, Dead = 0.1f,
            BushesPerHa = 0f, Bushes = Reeds,
            LogsPerHa = 2f, StonesPerHa = 4f,
            Plants = new[]
            {
                new BiomePlants(2200f, 0.6f, 1.15f, BiomeWhere.Water, 4f, "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWild01"),
                new BiomePlants(400f, 0.45f, 0.7f, BiomeWhere.All, 5f, "GreenBushWildChild01", "GreenBushWildTeen01"),
                new BiomePlants(60f, 0.8f, 1.2f, BiomeWhere.Rim, 8f, "GreenBushWild01", "GreenBushWildAdult01"),
            },
            Decals = new[]
            {
                new BiomeDecal("marshMud", 0.5f, BiomeWhere.Core, 0.6f, 0.95f, 3f) { Fill = true },
                new BiomeDecal("shoreMud", 0.6f, BiomeWhere.Water, 0.65f, 0.95f, 3f),
                new BiomeDecal("sedgeLitter", 0.45f, BiomeWhere.All, 0.55f, 0.9f, 3f),
                new BiomeDecal("dampPatch", 0.3f, BiomeWhere.Rim, 0.3f, 0.55f, 3f),
                new BiomeDecal("puddlePiece", 0.2f, BiomeWhere.Core, 0.6f, 0.95f, 1.8f),
            },
        };

        public static readonly BiomeDef Pond = new BiomeDef
        {
            Id = "water", Color = "#3f7fbf",
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200).Named("reeds"), BiomeKnob.Slider(BiomeKnobs.Trees, 30, 80, "perHa") },
            Hollow = 2f, Flat = 0.6f, Steep = -2.2f, Ridge = -1.4f, NearPath = -0.9f,
            PathKeep = 7f, MaxGrade = 0.1f,
            Water = true,
            PaintLayer = 2, PaintStrength = 0.9f,
            TreesPerHa = 30f, Trees = Wet, GroveScale = 18f,
            BushesPerHa = 160f, Bushes = Reeds,
            StonesPerHa = 20f,
            Decals = new[]
            {
                new BiomeDecal("shoreMud", 0.35f, BiomeWhere.Spill, 0.5f, 0.85f, 3f),
                new BiomeDecal("sedgeLitter", 0.3f, BiomeWhere.Spill, 0.45f, 0.8f, 3f),
            },
        };

        public static readonly BiomeDef SandHeath = new BiomeDef
        {
            // heather in mounds, gorse in drifts, birch and pine, sandy paths worn through it
            Id = "heath", Color = "#c7a46b",
            Knobs = new[] { BiomeKnob.Slider(BiomeKnobs.Shrubs, 100, 200).Named("heather"), BiomeKnob.Slider(BiomeKnobs.Trees, 16, 60, "perHa"), BiomeKnob.Slider(BiomeKnobs.Relief, 100, 200).Named("height") },
            Ridge = 0.6f, Slope = 0.4f, Flat = 0.2f, Hollow = -0.5f,
            // P2-4: the ground is the look: a dark mauve-brown heather carpet, low heather mounds in tight clumps (small dark wild
            // clumps, nothing pale), gorse in a few drifts, birch-led trees, grey-brown sandy trails (not the beach's pale sand)
            Lift = 0.6f, Break = 0.25f,
            PaintLayer = 2, PaintStrength = 0.6f, CorePaint = 3, CoreStrength = 0.3f,
            TreesPerHa = 16f, Trees = Heath, GroveScale = 26f, Dead = 0.08f,
            BushesPerHa = 0f, Bushes = Heather,
            BouldersPerHa = 3f, StonesPerHa = 25f, RockSize = 0.7f,
            Plants = new[]
            {
                new BiomePlants(1700f, 0.3f, 0.55f, BiomeWhere.Drift, 4f, Heather),
                new BiomePlants(90f, 0.6f, 1f, BiomeWhere.Drift, 14f, "GreenBushCity01", "GreenBushCity01", "GreenBushCity02"),
            },
            Decals = new[]
            {
                new BiomeDecal("heathPatch", 0.8f, BiomeWhere.All, 0.7f, 1f, 5f),
                new BiomeDecal("heathSand", 0.75f, BiomeWhere.Trails, 0.75f, 1f, 3f),
                new BiomeDecal("animalTrail", 0.3f, BiomeWhere.Trails, 0.6f, 0.9f, 3f),
                new BiomeDecal("pebbles", 0.06f, BiomeWhere.Core, 0.5f, 0.85f, 2f),
            },
        };

        // ── farms (P2-3, docs/runbooks/farmstead.md) ──

        public static readonly BiomeDef Fields = new BiomeDef
        {
            // the farm's own fields (the farmstead's lattice of crops, leys and hay meadows, with its hedges and gates)
            Id = "fields", Color = "#c9a94e", Host = true, Open = true,
            Flat = 0.9f, Slope = 0.3f, Steep = -0.9f, Hollow = -0.2f, NearPath = 0.4f, Ridge = 0.1f,
        };

        public static readonly BiomeDef Orchard = new BiomeDef
        {
            // fruit trees in straight rows 7 m apart, a bare strip down each row, mown grass alleys with the tractor's wheel
            // lines; on the warm slopes and the flats, never in a frost hollow (real orchards: Kent, the Altes Land, Normandy)
            Id = "orchard", Color = "#b8453a",
            Slope = 0.7f, Flat = 0.5f, Ridge = 0.2f, Hollow = -0.7f, Steep = -0.6f, NearPath = 0.3f, MaxGrade = 0.24f,
            // (P2-3) trees kept apart: at 4.6 m and full size the crowns merged into one wood from close (run 20261031-102933-farm-close)
            // mown alleys 9 m apart so the rows read from 45° (at 7-7.5 m the canopies closed over the alleys)
            RowPitch = 9f, RowSpacing = 5f, RowGaps = 0.04f, RowScaleLo = 0.6f, RowScaleHi = 0.78f,
            TreesPerHa = 300f, Trees = new[] { "AppleTree01" },
            Decals = new[]
            {
                new BiomeDecal("orchardStrip", 1f, BiomeWhere.Rows, 0.7f, 0.95f),
                new BiomeDecal("windfalls", 0.22f, BiomeWhere.Rows, 0.7f, 1f, 2.5f),
                new BiomeDecal("wheelRut", 1f, BiomeWhere.Alleys, 0.35f, 0.6f, quiet: true),
                new BiomeDecal("longGrass", 0.3f, BiomeWhere.Rim, 0.45f, 0.75f, 4f),
            },
        };

        public static readonly BiomeDef Pasture = new BiomeDef
        {
            // rough grazing: tussocky grass, gorse and thorn scrub in clumps, a lone thorn tree, stones, sheep tracks and dung;
            // the steep and the poor ground a farm never ploughs (a Welsh or Pennine hill farm's "ffridd", a Dales pasture)
            Id = "pasture", Color = "#6f9440",
            Slope = 0.8f, Steep = 0.9f, Ridge = 0.5f, Hollow = 0.2f, Flat = -0.3f, NearEdge = 0.3f, NearPath = -0.2f,
            Break = 0.12f,
            TreesPerHa = 4f, Trees = Solitary, GroveScale = 50f,
            BushesPerHa = 45f, Bushes = Scrub, RimScrub = 1.8f,
            BouldersPerHa = 5f, StonesPerHa = 40f, RockSize = 0.8f,
            Plants = new[]
            {
                new BiomePlants(420f, 0.45f, 0.75f, BiomeWhere.All, 6f, "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildChild01"),
                new BiomePlants(90f, 0.6f, 1f, BiomeWhere.Drift, 12f, "GreenBushCity01", "GreenBushCity02", "GreenBushWildElderly01"),
            },
            Decals = new[]
            {
                new BiomeDecal("longGrass", 0.45f, BiomeWhere.All, 0.5f, 0.8f, 5f),
                new BiomeDecal("animalTrail", 0.45f, BiomeWhere.Trails, 0.55f, 0.85f, 3f),
                new BiomeDecal("dungPats", 0.12f, BiomeWhere.All, 0.7f, 1f, 3f),
                new BiomeDecal("molehills", 0.03f, BiomeWhere.All, 0.6f, 0.9f, 2f),
                new BiomeDecal("pebbles", 0.06f, BiomeWhere.Core, 0.5f, 0.8f, 2f),
            },
        };

        public static readonly BiomeDef Dunes = new BiomeDef
        {
            // (p2-beaches) grey dunes behind a beach: knobbly sand hills, marram grass massed in clumps on them, bare sand where the
            // wind cuts in, a stunted pine or two; ripples on the open sand, sand blowing onto the neighbour's ground
            Id = "dunes", Color = "#e3cf9a",
            Ridge = 0.7f, Flat = 0.5f, Slope = 0.3f, Hollow = -0.4f, NearPath = -0.2f, Steep = -0.3f,
            Lift = 2.4f, Break = 0.7f,
            PaintLayer = 4, PaintStrength = 0.35f, CorePaint = 4, CoreStrength = 0.6f,
            TreesPerHa = 4f, Trees = new[] { "PineTree01", "PineTree01", "BirchTree01" }, GroveScale = 40f, Dead = 0.15f,
            BushesPerHa = 0f, Bushes = Heather,
            StonesPerHa = 6f, RockSize = 0.6f, LogsPerHa = 1f,
            Plants = new[]
            {
                new BiomePlants(1100f, 0.42f, 0.78f, BiomeWhere.Drift, 5f, "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWildChild01"),
                new BiomePlants(140f, 0.5f, 0.85f, BiomeWhere.Rim, 8f, "GreenBushWildTeen01", "GreenBushWild02", "GreenBushWildElderly01"),
            },
            Decals = new[]
            {
                new BiomeDecal("sandRipples", 0.45f, BiomeWhere.Core, 0.35f, 0.6f, 3f),
                new BiomeDecal("sandFan", 0.12f, BiomeWhere.Spill, 0.55f, 0.85f, 2f),
                new BiomeDecal("dryGrass", 0.16f, BiomeWhere.Rim, 0.25f, 0.45f, 3f, quiet: true),
                new BiomeDecal("driftwood", 0.02f, BiomeWhere.All, 0.85f, 1f, 1.5f),
            },
        };

        public static readonly BiomeDef Shingle = new BiomeDef
        {
            // (p2-beaches) vegetated shingle (Dungeness, Orford Ness): a flat of grey pebbles in low ridges, sea kale and broom in
            // sparse clumps, lichen-dark stones, a weathered post or log; open, wind-bare
            Id = "shingle", Color = "#a39d93",
            Flat = 0.8f, Ridge = 0.3f, Hollow = -0.2f, Steep = -0.8f, NearPath = -0.1f,
            Lift = 0.45f, Break = 0.3f,
            PaintLayer = 3, PaintStrength = 0.75f, CorePaint = 3, CoreStrength = 0.95f,
            TreesPerHa = 0f, Trees = Heath,
            BushesPerHa = 24f, Bushes = new[] { "GreenBushWild02", "GreenBushCity01", "GreenBushWildElderly01" }, RimScrub = 2f,
            StonesPerHa = 320f, RockSize = 0.55f, BouldersPerHa = 2f, LogsPerHa = 1.5f,
            Plants = new[]
            {
                new BiomePlants(140f, 0.4f, 0.7f, BiomeWhere.Drift, 12f, "GreenBushWildChild01", "GreenBushWildTeen01", "FlowerBushWildChild02"),
            },
            Decals = new[]
            {
                new BiomeDecal("shinglePatch", 0.85f, BiomeWhere.All, 0.75f, 1f, 3f),
                new BiomeDecal("strandPebbles", 0.25f, BiomeWhere.Rim, 0.6f, 0.9f, 2f),
                new BiomeDecal("driftwood", 0.03f, BiomeWhere.All, 0.85f, 1f, 1.5f),
                new BiomeDecal("dryGrass", 0.12f, BiomeWhere.Spill, 0.25f, 0.45f, 3f, quiet: true),
            },
        };

        /// <summary>Every biome, in the panel's order.</summary>
        public static readonly BiomeDef[] All = { Lawn, Grass, Field, Meadow, FlowerField, Mulch, Glade, Forest, Rocky, SandHeath, Dunes, Shingle, Wetland, Pond, Fields, Orchard, Pasture };

        private static readonly Dictionary<string, BiomeDef> ById = Index();

        private static Dictionary<string, BiomeDef> Index()
        {
            var d = new Dictionary<string, BiomeDef>(StringComparer.Ordinal);
            foreach (var b in All) d[b.Id] = b;
            return d;
        }

        public static BiomeDef Get(string id) => id != null && ById.TryGetValue(id, out var b) ? b : null;
    }
}
