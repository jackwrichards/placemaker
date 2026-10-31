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
        private static readonly string[] Heath = { "PineTree01", "BirchTree01", "PineTree01" };
        private static readonly string[] Ornamental = { "BirchTree01", "NA_LindenTree01", "EU_ChestnutTree01" };
        private static readonly string[] Scrub = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildElderly01", "GreenBushWildTeen01", "GreenBushWildChild01" };
        private static readonly string[] Under = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWildElderly01" };
        private static readonly string[] Reeds = { "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWild01", "GreenBushWildTeen01" };
        // the flower field's flowers are its ground (decals); a few low shrubs, mostly green (massed lilac read as pale clumps from 45°)
        private static readonly string[] Flowers = { "GreenBushWildChild01", "FlowerBushWildChild02", "GreenBushWildTeen01", "GreenBushWildChild01", "FlowerBushWildChild02", "GreenBushWild02" };
        private static readonly string[] Beds = { "FlowerBushWild02", "GreenBushWildAdult01", "FlowerBushWildChild02", "GreenBushWildTeen01", "GreenBushWild02", "FlowerBushWild02", "GreenBushWildElderly01" };
        private static readonly string[] Heather = { "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWildElderly01", "FlowerBushWildChild02" };

        public static readonly BiomeDef Lawn = new BiomeDef
        {
            Id = "lawn", Color = "#8cc152", Host = true, Open = true,
            Flat = 0.6f, NearPath = 0.7f, Ridge = -0.1f, Steep = -0.5f,
        };

        public static readonly BiomeDef Rocky = new BiomeDef
        {
            Id = "rocky", Color = "#9a9187",
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
            Id = "glade", Color = "#5f9a4b",
            Slope = 0.4f, Flat = 0.3f, NearEdge = 0.2f,
            PaintLayer = 2, PaintStrength = 0.45f,
            TreesPerHa = 55f, Trees = GladeTrees, GroveScale = 30f, Dead = 0.04f,
            BushesPerHa = 55f, Bushes = Scrub, RimScrub = 1.8f,
            LogsPerHa = 3f,
            Decals = new[]
            {
                new BiomeDecal("dryGrass", 0.16f, BiomeWhere.Core, 0.25f, 0.5f, 3f, quiet: true),
                new BiomeDecal("meadowFlowers", 0.12f, BiomeWhere.Core, 0.45f, 0.8f, 3f),
                new BiomeDecal("leafLitterWild", 0.18f, BiomeWhere.Rim, 0.45f, 0.8f, 3f, quiet: true),
                new BiomeDecal("mossPatch", 0.06f, BiomeWhere.Things, 0.35f, 0.6f, 2f),
            },
        };

        public static readonly BiomeDef Meadow = new BiomeDef
        {
            Id = "meadow", Color = "#c9b45a", Open = true,
            Flat = 0.9f, Slope = 0.2f, Hollow = 0.1f, NearPath = -0.1f, Steep = -0.3f,
            PaintLayer = 3, PaintStrength = 0.22f, CorePaint = 0, CoreStrength = 0f,
            TreesPerHa = 6f, Trees = Solitary, GroveScale = 60f,
            BushesPerHa = 28f, Bushes = Scrub, RimScrub = 2.2f,
            Decals = new[]
            {
                new BiomeDecal("longGrass", 0.85f, BiomeWhere.All, 0.7f, 1f, 5f),
                new BiomeDecal("dryGrass", 0.3f, BiomeWhere.All, 0.3f, 0.55f, 4f, quiet: true),
                new BiomeDecal("meadowFlowers", 0.22f, BiomeWhere.Core, 0.55f, 0.9f, 3f),
                new BiomeDecal("molehills", 0.02f, BiomeWhere.All, 0.6f, 0.9f, 2f),
            },
        };

        public static readonly BiomeDef FlowerField = new BiomeDef
        {
            Id = "flowers", Color = "#c86fb4", Open = true,
            Flat = 1f, Slope = 0.3f, Steep = -0.6f, Hollow = -0.1f, NearPath = 0.2f,
            PaintLayer = 2, PaintStrength = 0.4f, CorePaint = 1, CoreStrength = 0.8f,
            TreesPerHa = 2f, Trees = Solitary, GroveScale = 60f,
            BushesPerHa = 45f, Bushes = Flowers, RimScrub = 1.6f,
            Decals = new[]
            {
                new BiomeDecal("wildflowerDrift", 0.95f, BiomeWhere.Core, 0.8f, 1f, 5f),
                new BiomeDecal("meadowFlowers", 0.6f, BiomeWhere.All, 0.7f, 1f, 3f),
                new BiomeDecal("dryGrass", 0.12f, BiomeWhere.Rim, 0.25f, 0.45f, 3f, quiet: true),
            },
        };

        public static readonly BiomeDef Mulch = new BiomeDef
        {
            Id = "mulch", Color = "#7b5236",
            NearPath = 0.8f, Flat = 0.4f, Slope = 0.1f, Steep = -0.4f,
            Lift = 0.25f,
            PaintLayer = 2, PaintStrength = 1f, CorePaint = 2, CoreStrength = 1f,
            TreesPerHa = 30f, Trees = Ornamental, GroveScale = 18f,
            BushesPerHa = 230f, Bushes = Beds, RimScrub = 1.2f,
            Decals = new[]
            {
                new BiomeDecal("mulchBed", 0.6f, BiomeWhere.Core, 0.75f, 1f, 5f),
                new BiomeDecal("barkMulch", 0.4f, BiomeWhere.All, 0.6f, 0.95f, 3f),
                new BiomeDecal("woodlandFloor", 0.15f, BiomeWhere.Rim, 0.4f, 0.7f, 3f, quiet: true),
            },
        };

        public static readonly BiomeDef Wetland = new BiomeDef
        {
            Id = "wetland", Color = "#4f8f86",
            Hollow = 1.6f, Flat = 0.3f, Steep = -1.4f, Ridge = -1.1f, Slope = -0.3f, NearPath = -0.4f,
            PathKeep = 3.5f, MaxGrade = 0.14f,
            Dip = 0.55f, Break = 0.12f, Pools = 0.22f,
            PaintLayer = 2, PaintStrength = 1f, CorePaint = 2, CoreStrength = 1f,
            TreesPerHa = 22f, Trees = Wet, GroveScale = 22f, Dead = 0.08f,
            BushesPerHa = 300f, Bushes = Reeds, RimScrub = 0.8f,
            LogsPerHa = 2f, StonesPerHa = 6f,
            Decals = new[]
            {
                new BiomeDecal("marshMud", 0.5f, BiomeWhere.Core, 0.55f, 0.9f, 3f) { Fill = true },
                new BiomeDecal("sedgeLitter", 0.4f, BiomeWhere.All, 0.5f, 0.85f, 3f),
                new BiomeDecal("dampPatch", 0.3f, BiomeWhere.Rim, 0.3f, 0.55f, 3f),
                new BiomeDecal("puddlePiece", 0.16f, BiomeWhere.Core, 0.55f, 0.9f, 1.8f),
                new BiomeDecal("cloverPatch", 0.2f, BiomeWhere.Spill, 0.4f, 0.7f, 3f),
            },
        };

        public static readonly BiomeDef Pond = new BiomeDef
        {
            Id = "water", Color = "#3f7fbf",
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
            Id = "heath", Color = "#c7a46b",
            Ridge = 0.6f, Slope = 0.4f, Flat = 0.2f, Hollow = -0.5f,
            Lift = 0.6f, Break = 0.25f,
            PaintLayer = 2, PaintStrength = 0.6f, CorePaint = 4, CoreStrength = 0.35f,
            TreesPerHa = 12f, Trees = Heath, GroveScale = 26f, Dead = 0.1f,
            BushesPerHa = 230f, Bushes = Heather, RimScrub = 0.8f,
            BouldersPerHa = 3f, StonesPerHa = 25f, RockSize = 0.7f,
            Decals = new[]
            {
                new BiomeDecal("heathPatch", 0.42f, BiomeWhere.All, 0.5f, 0.85f, 4f),
                new BiomeDecal("sandSpill", 0.28f, BiomeWhere.Core, 0.45f, 0.8f, 3f),
                new BiomeDecal("dryGrass", 0.14f, BiomeWhere.Rim, 0.25f, 0.45f, 3f, quiet: true),
                new BiomeDecal("pebbles", 0.08f, BiomeWhere.Core, 0.5f, 0.85f, 2f),
            },
        };

        /// <summary>Every biome, in the panel's order.</summary>
        public static readonly BiomeDef[] All = { Lawn, Meadow, FlowerField, Mulch, Glade, Forest, Rocky, SandHeath, Wetland, Pond };

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
