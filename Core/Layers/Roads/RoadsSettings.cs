// Placemaker roads layer — control schema and mapping to the generator parameters.
// The ~45 GridParameters of Grid Road Generator (Ti4goc) are exposed as one clean schema:
// a handful of essentials per pattern (VisibleWhen "pattern=…"), every option grouped under a plain heading.
// Control ids are save format: never rename, only add.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    public enum RoadPattern { Grid, Tree, Organic, Contour, Mixed, Loop, Superblock, Concentric, Radial, Terraces }

    /// <summary>Control ids of the roads layer (save format).</summary>
    public static class RoadsIds
    {
        public const string Pattern = "pattern";
        public const string StreetAsset = "streetAsset";
        /// <summary>(s16) Road mix: writes the road-type picks + upgrades; derived back from them (RoadMixes).</summary>
        public const string RoadMix = "roadMix";

        // Grid
        public const string GridSpacing = "gridSpacing";
        public const string GridAngle = "gridAngle";
        public const string GridAlignTerrain = "gridAlignTerrain";
        public const string GridMode = "gridMode";              // "spacing" | "count"
        public const string GridColumns = "gridColumns";
        public const string GridRows = "gridRows";
        public const string CulDeSacs = "culDeSacs";
        public const string CulDeSacAxis = "culDeSacAxis";      // "columns" | "rows" | "both"
        public const string CulDeSacRatio = "culDeSacRatio";
        public const string CulDeSacDepth = "culDeSacDepth";
        public const string CulDeSacStaggered = "culDeSacStaggered";
        public const string AvenueColumn = "avenueColumn";
        public const string AvenueColumnIndex = "avenueColumnIndex";
        public const string AvenueRow = "avenueRow";
        public const string AvenueRowIndex = "avenueRowIndex";
        public const string RoundaboutIsland = "roundaboutIsland";

        // Tree
        public const string TreeBranchSpacing = "treeBranchSpacing";
        public const string TreeCulDeSacLength = "treeCulDeSacLength";
        public const string TreeCulDeSacSpacing = "treeCulDeSacSpacing";
        public const string TreeAngle = "treeAngle";

        // Organic
        public const string OrganicSpacing = "organicSpacing";
        public const string OrganicCurviness = "organicCurviness";
        public const string OrganicLoops = "organicLoops";
        public const string OrganicVariation = "organicVariation";

        // Contour
        public const string ContourSpacing = "contourSpacing";
        public const string ContourLinkSpacing = "contourLinkSpacing";

        // Mixed
        public const string MixedCoreRadius = "mixedCoreRadius";
        public const string MixedAvenues = "mixedAvenues";
        public const string MixedSpacing = "mixedSpacing";
        public const string MixedCurviness = "mixedCurviness";

        // Loop
        public const string LoopCollectorSpacing = "loopCollectorSpacing";
        public const string LoopCulDeSacRatio = "loopCulDeSacRatio";
        public const string LoopCulDeSacDepth = "loopCulDeSacDepth";
        public const string LoopAngle = "loopAngle";
        public const string LoopAlignTerrain = "loopAlignTerrain";

        // Superblock
        public const string SuperblockZone = "superblockZone";
        public const string SuperblockAngle = "superblockAngle";
        public const string SuperblockAlignTerrain = "superblockAlignTerrain";

        // Concentric
        public const string ConcentricRings = "concentricRings";
        public const string ConcentricConnections = "concentricConnections";

        // Radial
        public const string RadialAvenues = "radialAvenues";
        public const string RadialRoundaboutRadius = "radialRoundaboutRadius";
        public const string RadialRings = "radialRings";

        // Network-wide
        public const string PerimeterRoad = "perimeterRoad";        // "auto" | "on" | "off"
        public const string PerimeterCornerRadius = "perimeterCornerRadius";
        public const string FollowTerrain = "followTerrain";
        public const string AvoidObstacles = "avoidObstacles";
        public const string ConnectExisting = "connectExisting";
        /// <summary>(Smarter roads) Which existing roads new roads join: "streets" (all but highways and ramps) | "small" | "any".</summary>
        public const string ConnectTo = "connectTo";
        /// <summary>(Smarter roads) Ground the streets wear in (desire paths, corner wear, verges): "off" | "subtle" | "lived".</summary>
        public const string GroundWear = "roadWear";
        /// <summary>(Smarter roads) Grid character: "strict" (the plain grid) | "auto" (gives way to hills and desire lines) | "lively" (more: brick blocks, a square).</summary>
        public const string GridCharacter = "gridCharacter";
        public const string CapStyle = "capStyle";                  // "none" | "asphalt" | "grass" | "trees"
        public const string CapSize = "capSize";                    // "auto" | "small" | "medium" | "large" | "xl"
        public const string LocalAsset = "localAsset";
        public const string AvenueAsset = "avenueAsset";
        public const string RoundaboutAsset = "roundaboutAsset";
        public const string PathAsset = "pathAsset";
        /// <summary>(s16) Road of the perimeter loop; "" = the street road.</summary>
        public const string PerimeterAsset = "perimeterAsset";
        public const string EdgeDepth = "edgeDepth";

        // Upgrades
        public const string StreetTrees = "streetTrees";
        public const string StreetGrass = "streetGrass";
        public const string StreetWideSidewalks = "streetWideSidewalks";
        public const string StreetBikeLanes = "streetBikeLanes";
        public const string AvenueMedianTrees = "avenueMedianTrees";
        public const string AvenueMedianGrass = "avenueMedianGrass";
        public const string AvenueTrees = "avenueTrees";
        public const string AvenueGrass = "avenueGrass";
        public const string AvenueBikeLanes = "avenueBikeLanes";

        // s34: network design
        /// <summary>Junction spacing on main roads (m), 0 = every side street may join.</summary>
        public const string JunctionSpacing = "junctionSpacing";
        /// <summary>Walkways (cul-de-sac links, cut-throughs) amount, 0..100 %.</summary>
        public const string Walkways = "walkways";
        public const string WalkwayStyle = "walkwayStyle";          // "paved" | "trail" | "mixed"
        /// <summary>Back lanes through the middle of blocks, 0..100 % of the blocks that can take one.</summary>
        public const string BackLanes = "backLanes";
        public const string LaneStyle = "laneStyle";                // "alley" | "gravel" | "mixed"
        /// <summary>Road of the back lanes ("" = Automatic: the vanilla Alley).</summary>
        public const string LaneAsset = "laneAsset";
        public const string TipStyle = "tipStyle";                  // "same" | "gravel" | "mixed"
        /// <summary>"auto" (each pattern's own terrain toggle) | "follow" (streets along the contours) | "ignore" (strict geometry).</summary>
        public const string TerrainFit = "terrainFit";
        /// <summary>Steepest road the layout aims for (%): contour layouts, links and the grade check.</summary>
        public const string MaxGrade = "maxGrade";
        /// <summary>Pre-zone every new road's zone blocks: "none" or a zone kind (ZoneKinds).</summary>
        public const string PreZone = "preZone";

        /// <summary>"terrace" (every pattern: each street's frontage level across, banks or walls between the levels, on
        /// sloped ground only) | "natural" (the ground as it is).</summary>
        public const string LandShape = "landShape";

        // s52: hillside terraces (pattern "terraces", Core/Layers/Roads/Terraces)
        /// <summary>Zone cells (8 m) kept level on each side of a terrace street, 1..6.</summary>
        public const string TerraceCells = "terraceCells";
        /// <summary>Cross-fall of a terrace away from the bank (%), for drainage.</summary>
        public const string TerraceFall = "terraceFall";
        /// <summary>"bank" (graded, rounded) | "wall" (steep straight face, crisp edges).</summary>
        public const string TerraceBank = "terraceBank";
        /// <summary>Steepest bank between terraces (%).</summary>
        public const string TerraceBankSlope = "terraceBankSlope";
        /// <summary>Links between terraces: "direct" | "serpentine" | "spiral".</summary>
        public const string TerraceLinks = "terraceLinks";
        /// <summary>Distance between links along a terrace street (m).</summary>
        public const string TerraceLinkSpacing = "terraceLinkSpacing";
        /// <summary>Links between terraces are avenues.</summary>
        public const string TerraceAvenues = "terraceAvenues";
        /// <summary>Ground flatter than this (%) is never terraced.</summary>
        public const string TerraceFlat = "terraceFlat";
        /// <summary>Deepest cut or highest fill (m).</summary>
        public const string TerraceEarthwork = "terraceEarthwork";
        /// <summary>Paint the banks (dry grass, worn dirt on the steep faces).</summary>
        public const string TerracePaint = "terracePaint";
        /// <summary>Trees on the banks.</summary>
        public const string TerraceTrees = "terraceTrees";
    }

    /// <summary>Schema construction and settings → generator parameters.</summary>
    public static class RoadsSchema
    {
        public const string KeyPrefix = "Placemaker.Roads.";
        /// <summary>Default perimeter corner (m): the tightest a two-lane road turns cleanly (its half width 8 m + 6 m, rounded up).</summary>
        public const float TightCorner = 15f;
        /// <summary>Perimeter corner of the winding styles (m): their streets curve, a tight corner would look out of place.</summary>
        public const float SoftCorner = 30f;

        public static readonly string[] PatternValues =
            { "grid", "tree", "organic", "contour", "mixed", "loop", "superblock", "concentric", "radial", "terraces" };

        public static RoadPattern ParsePattern(string s)
        {
            int i = Array.IndexOf(PatternValues, s ?? "");
            return i < 0 ? RoadPattern.Grid : (RoadPattern)i;
        }

        public static string PatternValue(RoadPattern p) => PatternValues[(int)p];

        public static string Label(string id) => KeyPrefix + "Control." + id;
        public static string Tooltip(string id) => KeyPrefix + "Control." + id + ".Tooltip";
        public static string OptionLabel(string id, string value) => KeyPrefix + "Option." + id + "." + value;
        /// <summary>The panel reads group headings from "Placemaker.UI.Group.{id}" (shared across layers).</summary>
        public static string GroupLabel(string group) => "Placemaker.UI.Group." + group;

        public static List<ControlSchema> Build()
        {
            var c = new List<ControlSchema>();

            // ── Essentials ────────────────────────────────────────────────
            var pattern = Choice(RoadsIds.Pattern, ControlTier.Essential, "layout", "grid", PatternValues);
            // Pattern options are text choices: no "placemaker" coui host exists and vanilla has no
            // pattern icons. Bundled icons are requested in docs/proposals/roads.md.
            c.Add(pattern);
            var mix = Choice(RoadsIds.RoadMix, ControlTier.Essential, "roadTypes", RoadMixes.Automatic, RoadMixes.Values());
            foreach (var o in mix.Options)
            {
                var m = RoadMixes.Find(o.Value);
                o.DetailKey = OptionLabel(RoadsIds.RoadMix, o.Value) + ".Detail";
                if (m != null) o.Swatch = RoadMixes.Swatch(m);
            }
            mix.DescriptionKey = Description(RoadsIds.RoadMix);
            c.Add(mix);
            c.Add(Asset(RoadsIds.StreetAsset, ControlTier.Essential, "roadTypes", AssetCategories.Road));

            c.Add(Slider(RoadsIds.GridSpacing, ControlTier.Essential, "layout", 60, 300, 10, "m", 100, "pattern=grid"));
            c.Add(Slider(RoadsIds.GridAngle, ControlTier.Essential, "layout", -90, 90, 1, "deg", 0, "pattern=grid", label: "angle"));
            c.Add(Choice(RoadsIds.GridCharacter, ControlTier.Essential, "layout", RoadCharacter.Auto, RoadCharacter.Values, "pattern=grid"));
            c.Add(Slider(RoadsIds.TreeBranchSpacing, ControlTier.Essential, "layout",
                GridGenerator.MinTreeBranchSpacing, GridGenerator.MaxTreeBranchSpacing, 10, "m", GridGenerator.TreeBranchSpacingDefault, "pattern=tree"));
            c.Add(Slider(RoadsIds.TreeCulDeSacLength, ControlTier.Essential, "layout",
                GridGenerator.MinTreeCulDeSacLength, GridGenerator.MaxTreeCulDeSacLength, 5, "m", GridGenerator.TreeCulDeSacLengthDefault, "pattern=tree"));
            c.Add(Slider(RoadsIds.TreeAngle, ControlTier.Essential, "layout", -90, 90, 1, "deg", 0, "pattern=tree", label: "angle"));
            c.Add(Slider(RoadsIds.OrganicSpacing, ControlTier.Essential, "layout",
                GridGenerator.MinOrganicStreetSpacing, GridGenerator.MaxOrganicStreetSpacing, 5, "m", GridGenerator.OrganicStreetSpacingDefault, "pattern=organic", label: "streetSpacing"));
            c.Add(Slider(RoadsIds.OrganicCurviness, ControlTier.Essential, "layout", 0, 100, 5, "%", GridGenerator.OrganicCurvinessDefault, "pattern=organic", label: "curviness"));
            c.Add(Slider(RoadsIds.ContourSpacing, ControlTier.Essential, "layout",
                GridGenerator.MinContourSpacing, GridGenerator.MaxContourSpacing, 5, "m", GridGenerator.ContourSpacingDefault, "pattern=contour", label: "streetSpacing"));
            c.Add(Slider(RoadsIds.ContourLinkSpacing, ControlTier.Essential, "layout",
                GridGenerator.MinContourConnectorSpacing, GridGenerator.MaxContourConnectorSpacing, 10, "m", GridGenerator.ContourConnectorSpacingDefault, "pattern=contour"));
            c.Add(Slider(RoadsIds.MixedCoreRadius, ControlTier.Essential, "layout",
                GridGenerator.MinMixedCoreRadius, GridGenerator.MaxMixedCoreRadius, 10, "m", GridGenerator.MixedCoreRadiusDefault, "pattern=mixed"));
            c.Add(Slider(RoadsIds.MixedAvenues, ControlTier.Essential, "layout", 3, 16, 1, "count", 8, "pattern=mixed", label: "avenues"));
            c.Add(Slider(RoadsIds.LoopCollectorSpacing, ControlTier.Essential, "layout", 200, 400, 10, "m", 200, "pattern=loop"));
            c.Add(Slider(RoadsIds.LoopAngle, ControlTier.Essential, "layout", -90, 90, 1, "deg", 0, "pattern=loop", label: "angle"));
            c.Add(Slider(RoadsIds.SuperblockZone, ControlTier.Essential, "layout",
                GridGenerator.SuperblockZoneMetersMin, 400, 10, "m", 150, "pattern=superblock"));
            c.Add(Slider(RoadsIds.SuperblockAngle, ControlTier.Essential, "layout", -90, 90, 1, "deg", 0, "pattern=superblock", label: "angle"));
            c.Add(Slider(RoadsIds.ConcentricRings, ControlTier.Essential, "layout",
                ConcentricGenerator.MinLayers, ConcentricGenerator.MaxLayersLimit, 1, "count", 3, "pattern=concentric", label: "rings"));
            c.Add(Slider(RoadsIds.ConcentricConnections, ControlTier.Essential, "layout",
                ConcentricGenerator.MinConnections, ConcentricGenerator.MaxConnections, 1, "count", 4, "pattern=concentric"));
            c.Add(Slider(RoadsIds.RadialAvenues, ControlTier.Essential, "layout", 3, 16, 1, "count", 8, "pattern=radial", label: "avenues"));
            c.Add(Slider(RoadsIds.RadialRoundaboutRadius, ControlTier.Essential, "layout", 20, 150, 5, "m", 50, "pattern=radial", label: "roundaboutRadius"));
            c.Add(Slider(RoadsIds.RadialRings, ControlTier.Essential, "layout", 0, ConcentricGenerator.MaxLayersLimit, 1, "count", 2, "pattern=radial", label: "rings"));
            // s52: hillside terraces — terrace depth in zone cells, how the terraces are linked.
            c.Add(Slider(RoadsIds.TerraceCells, ControlTier.Essential, "layout", 1, 6, 1, "count", 4, "landShape=terrace"));
            c.Add(Choice(RoadsIds.TerraceLinks, ControlTier.Essential, "layout", "direct", new[] { "direct", "serpentine", "spiral" }, "pattern=terraces"));
            c.Add(Slider(RoadsIds.TerraceLinkSpacing, ControlTier.Essential, "layout", 120, 600, 20, "m", 300, "pattern=terraces"));

            // ── More: per pattern ─────────────────────────────────────────
            c.Add(Toggle(RoadsIds.GridAlignTerrain, ControlTier.More, "layout", false, "pattern=grid", label: "alignTerrain"));
            c.Add(Choice(RoadsIds.GridMode, ControlTier.More, "layout", "spacing", new[] { "spacing", "count" }, "pattern=grid"));
            c.Add(Slider(RoadsIds.GridColumns, ControlTier.More, "layout", 1, 12, 1, "count", 3, "gridMode=count"));
            c.Add(Slider(RoadsIds.GridRows, ControlTier.More, "layout", 1, 12, 1, "count", 3, "gridMode=count"));
            c.Add(Toggle(RoadsIds.CulDeSacs, ControlTier.More, "culDeSacs", false, "pattern=grid"));
            c.Add(Choice(RoadsIds.CulDeSacAxis, ControlTier.More, "culDeSacs", "columns", new[] { "columns", "rows", "both" }, "culDeSacs=true"));
            c.Add(Slider(RoadsIds.CulDeSacRatio, ControlTier.More, "culDeSacs", 0, 100, 5, "%", 100, "culDeSacs=true", label: "culDeSacFrequency"));
            c.Add(Slider(RoadsIds.CulDeSacDepth, ControlTier.More, "culDeSacs", 50, 80, 5, "%", 60, "culDeSacs=true", label: "culDeSacDepth"));
            c.Add(Toggle(RoadsIds.CulDeSacStaggered, ControlTier.More, "culDeSacs", true, "culDeSacs=true"));
            c.Add(Toggle(RoadsIds.AvenueColumn, ControlTier.More, "avenues", false, "pattern=grid"));
            c.Add(Slider(RoadsIds.AvenueColumnIndex, ControlTier.More, "avenues", 0, 23, 1, "count", 1, "avenueColumn=true", label: "avenueIndex"));
            c.Add(Toggle(RoadsIds.AvenueRow, ControlTier.More, "avenues", false, "pattern=grid"));
            c.Add(Slider(RoadsIds.AvenueRowIndex, ControlTier.More, "avenues", 0, 23, 1, "count", 1, "avenueRow=true", label: "avenueIndex"));
            c.Add(Toggle(RoadsIds.RoundaboutIsland, ControlTier.More, "avenues", true, "pattern=grid"));
            c.Add(Slider(RoadsIds.TreeCulDeSacSpacing, ControlTier.More, "layout",
                GridGenerator.MinTreeCulDeSacSpacing, GridGenerator.MaxTreeCulDeSacSpacing, 5, "m", GridGenerator.TreeCulDeSacSpacingDefault, "pattern=tree"));
            c.Add(Slider(RoadsIds.OrganicLoops, ControlTier.More, "layout", 0, 100, 5, "%", GridGenerator.OrganicLoopShareDefault, "pattern=organic", label: "loops"));
            c.Add(Slider(RoadsIds.OrganicVariation, ControlTier.More, "layout",
                GridGenerator.MinOrganicSeed, GridGenerator.MaxOrganicSeed, 1, "count", GridGenerator.MinOrganicSeed, "pattern=organic", label: "variation"));
            c.Add(Slider(RoadsIds.MixedSpacing, ControlTier.More, "layout",
                GridGenerator.MinOrganicStreetSpacing, GridGenerator.MaxOrganicStreetSpacing, 5, "m", GridGenerator.OrganicStreetSpacingDefault, "pattern=mixed", label: "streetSpacing"));
            c.Add(Slider(RoadsIds.MixedCurviness, ControlTier.More, "layout", 0, 100, 5, "%", GridGenerator.OrganicCurvinessDefault, "pattern=mixed", label: "curviness"));
            c.Add(Slider(RoadsIds.LoopCulDeSacRatio, ControlTier.More, "culDeSacs", 0, 100, 5, "%", 50, "pattern=loop", label: "culDeSacFrequency"));
            c.Add(Slider(RoadsIds.LoopCulDeSacDepth, ControlTier.More, "culDeSacs", 50, 80, 5, "%", 60, "pattern=loop", label: "culDeSacDepth"));
            c.Add(Toggle(RoadsIds.LoopAlignTerrain, ControlTier.More, "layout", false, "pattern=loop", label: "alignTerrain"));
            c.Add(Toggle(RoadsIds.SuperblockAlignTerrain, ControlTier.More, "layout", false, "pattern=superblock", label: "alignTerrain"));

            // ── More: network-wide ────────────────────────────────────────
            c.Add(Choice(RoadsIds.PerimeterRoad, ControlTier.More, "connections", "auto", new[] { "auto", "on", "off" }));
            // Smarter roads: the perimeter hugs the selection: the tightest corner the road turns cleanly (winding styles set more).
            c.Add(Slider(RoadsIds.PerimeterCornerRadius, ControlTier.More, "connections", 0, 80, 5, "m", TightCorner));
            c.Add(Toggle(RoadsIds.ConnectExisting, ControlTier.More, "connections", true));
            c.Add(Choice(RoadsIds.ConnectTo, ControlTier.More, "connections", ConnectTo.Streets, ConnectTo.Values, "connectExisting=true"));
            c.Add(Choice(RoadsIds.GroundWear, ControlTier.Essential, "terrain", RoadPaint.Subtle, RoadPaint.Values));
            c.Add(Toggle(RoadsIds.AvoidObstacles, ControlTier.More, "connections", true));
            c.Add(Toggle(RoadsIds.FollowTerrain, ControlTier.More, "connections", true));
            c.Add(Asset(RoadsIds.LocalAsset, ControlTier.More, "roadTypes", AssetCategories.Road));
            c.Add(Asset(RoadsIds.AvenueAsset, ControlTier.More, "roadTypes", AssetCategories.Road));
            c.Add(Asset(RoadsIds.RoundaboutAsset, ControlTier.More, "roadTypes", AssetCategories.Road));
            c.Add(Asset(RoadsIds.PathAsset, ControlTier.More, "roadTypes", AssetCategories.Pathway));   // s34: walkways use it too
            c.Add(Asset(RoadsIds.PerimeterAsset, ControlTier.More, "roadTypes", AssetCategories.Road));
            c.Add(Choice(RoadsIds.CapStyle, ControlTier.More, "culDeSacs", "asphalt", new[] { "none", "asphalt", "grass", "trees" }));
            c.Add(Choice(RoadsIds.CapSize, ControlTier.More, "culDeSacs", "auto", new[] { "auto", "small", "medium", "large", "xl" }));
            c.Add(Slider(RoadsIds.EdgeDepth, ControlTier.More, "frontage", 16, 64, 8, "m", 48, ""));

            // ── s34: network design ───────────────────────────────────────
            c.Add(Slider(RoadsIds.JunctionSpacing, ControlTier.Essential, "connections", 0, 250, 10, "m", 120));
            c.Add(Slider(RoadsIds.Walkways, ControlTier.Essential, "walkways", 0, 100, 5, "%", 50));
            c.Add(Choice(RoadsIds.WalkwayStyle, ControlTier.More, "walkways", "paved", new[] { "paved", "trail", "mixed" }));
            c.Add(Slider(RoadsIds.BackLanes, ControlTier.More, "walkways", 0, 100, 5, "%", 0));
            c.Add(Choice(RoadsIds.LaneStyle, ControlTier.More, "walkways", "alley", new[] { "alley", "gravel", "mixed" }));
            c.Add(Choice(RoadsIds.TipStyle, ControlTier.More, "culDeSacs", "same", new[] { "same", "gravel", "mixed" }));
            c.Add(Asset(RoadsIds.LaneAsset, ControlTier.More, "roadTypes", AssetCategories.Road));
            c.Add(Choice(RoadsIds.TerrainFit, ControlTier.Essential, "terrain", "auto", new[] { "auto", "follow", "ignore" }));
            // Smarter roads: no terracing unless the player asks for it (the user: terrain flattening and terracing off by default).
            c.Add(Choice(RoadsIds.LandShape, ControlTier.Essential, "terrain", "natural", new[] { "terrace", "natural" }));
            c.Add(Slider(RoadsIds.MaxGrade, ControlTier.More, "terrain", 4, 16, 1, "%", 10));
            // No zoning in Roads (the user, 26 Oct: the Zoning category zones): the control stays for old recipes, never shown
            // (no value is "offered") and never used (RoadsPlanner, RoadPreZone.Offered).
            var preZone = Choice(RoadsIds.PreZone, ControlTier.Essential, "zoning", RoadPreZone.Default, RoadPreZone.Values());
            preZone.VisibleWhen = RoadsIds.PreZone + "=offered";
            preZone.HideWhenOff = true;
            // The zone types read like everywhere else (Placemaker.Zone.<kind>).
            foreach (var o in preZone.Options)
                if (o.Value != RoadPreZone.None) { o.LabelKey = "Placemaker.Zone." + o.Value; o.TooltipKey = "Placemaker.Zone." + o.Value + ".Tooltip"; }
            c.Add(preZone);

            // ── s52: hillside terraces (more) ────────────────────────────
            c.Add(Toggle(RoadsIds.TerraceAvenues, ControlTier.More, "layout", false, "pattern=terraces"));
            c.Add(Slider(RoadsIds.TerraceFall, ControlTier.More, "terraces", 0, 4, 0.5, "%", 1, "landShape=terrace"));
            // no "retaining" (the walls were the game's quays: the user, "they never work"); a saved "retaining" is a bank
            c.Add(Choice(RoadsIds.TerraceBank, ControlTier.More, "terraces", "bank", new[] { "bank", "wall" }, "landShape=terrace"));
            c.Add(Slider(RoadsIds.TerraceBankSlope, ControlTier.More, "terraces", 20, 50, 5, "%", 40, "landShape=terrace"));
            c.Add(Slider(RoadsIds.TerraceFlat, ControlTier.More, "terraces", 2, 12, 1, "%", 5, "landShape=terrace"));
            c.Add(Slider(RoadsIds.TerraceEarthwork, ControlTier.More, "terraces", 2, 20, 1, "m", 8, "landShape=terrace"));
            c.Add(Toggle(RoadsIds.TerracePaint, ControlTier.More, "terraces", true, "landShape=terrace"));
            c.Add(Toggle(RoadsIds.TerraceTrees, ControlTier.More, "terraces", true, "landShape=terrace"));

            // ── More: upgrades ────────────────────────────────────────────
            c.Add(Toggle(RoadsIds.StreetTrees, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.StreetGrass, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.StreetWideSidewalks, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.StreetBikeLanes, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.AvenueMedianTrees, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.AvenueMedianGrass, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.AvenueTrees, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.AvenueGrass, ControlTier.More, "streetUpgrades", false));
            c.Add(Toggle(RoadsIds.AvenueBikeLanes, ControlTier.More, "streetUpgrades", false));

            foreach (var ctl in c)
            {
                // Pattern geometry needs a full re-plan; asset/upgrade/cap choices keep the layout.
                ctl.Cheap = ctl.Kind == ControlKind.Asset || ctl.Group == "streetUpgrades" || ctl.Id == RoadsIds.RoadMix
                    || ctl.Id == RoadsIds.CapStyle || ctl.Id == RoadsIds.CapSize || ctl.Id == RoadsIds.PreZone;
            }
            return c;
        }

        private static ControlSchema Base(string id, ControlKind kind, ControlTier tier, string group, string visibleWhen, string label)
        {
            string labelId = string.IsNullOrEmpty(label) ? id : label;
            return new ControlSchema
            {
                Id = id, Kind = kind, Tier = tier, Group = group,
                LabelKey = Label(labelId), TooltipKey = Tooltip(labelId),
                VisibleWhen = visibleWhen ?? "",
                // s15: the per-pattern essentials stay hidden for the other patterns (greyed they would bury the
                // few that apply), as the terrace settings on natural land; every other condition is shown greyed with its reason.
                HideWhenOff = (visibleWhen ?? "").StartsWith("pattern=", System.StringComparison.Ordinal)
                    || (visibleWhen ?? "").StartsWith(RoadsIds.LandShape + "=", System.StringComparison.Ordinal),
            };
        }

        private static ControlSchema Slider(string id, ControlTier tier, string group, double min, double max, double step,
            string unit, double def, string visibleWhen = "", string label = null)
        {
            var s = Base(id, ControlKind.Slider, tier, group, visibleWhen, label);
            s.Min = min; s.Max = max; s.Step = step; s.Unit = unit; s.Default = def;
            return s;
        }

        private static ControlSchema Toggle(string id, ControlTier tier, string group, bool def, string visibleWhen = "", string label = null)
        {
            var s = Base(id, ControlKind.Toggle, tier, group, visibleWhen, label);
            s.Default = def;
            return s;
        }

        private static ControlSchema Choice(string id, ControlTier tier, string group, string def, string[] values, string visibleWhen = "")
        {
            var s = Base(id, ControlKind.Choice, tier, group, visibleWhen, null);
            s.Default = def;
            foreach (var v in values)
                s.Options.Add(new ControlOption { Value = v, LabelKey = OptionLabel(id, v), TooltipKey = OptionLabel(id, v) + ".Tooltip" });
            return s;
        }

        /// <summary>(s16) Visible one-line "what is this role" text under a control.</summary>
        public static string Description(string id) => KeyPrefix + "Control." + id + ".Description";

        private static ControlSchema Asset(string id, ControlTier tier, string group, string category, string visibleWhen = "")
        {
            var s = Base(id, ControlKind.Asset, tier, group, visibleWhen, null);
            s.DescriptionKey = Description(id);
            s.Default = "";
            s.AssetCategory = category;
            return s;
        }

        public static SettingsBag Defaults(IReadOnlyList<ControlSchema> controls)
        {
            var b = new SettingsBag();
            foreach (var ctl in controls) b.Values[ctl.Id] = ctl.Default;
            return b;
        }

        /// <summary>Clamp helper for sliders read from a bag (saved values may predate a range change).</summary>
        public static float Num(SettingsBag s, IReadOnlyList<ControlSchema> controls, string id)
        {
            foreach (var ctl in controls)
            {
                if (ctl.Id != id) continue;
                double def = ctl.Default is IConvertible cv ? cv.ToDouble(System.Globalization.CultureInfo.InvariantCulture) : ctl.Min;
                double v = s.Num(id, def);
                if (double.IsNaN(v) || double.IsInfinity(v)) v = def;
                return (float)Math.Max(ctl.Min, Math.Min(ctl.Max, v));
            }
            return (float)s.Num(id, 0);
        }

        /// <summary>Settings → generator parameters for the chosen pattern.</summary>
        public static GridParameters ToGridParameters(SettingsBag s, IReadOnlyList<ControlSchema> controls, out RoadPattern pattern)
        {
            pattern = ParsePattern(s.S(RoadsIds.Pattern, "grid"));
            float N(string id) => Num(s, controls, id);
            int I(string id) => (int)Math.Round(Num(s, controls, id));

            var p = GridParameters.Default;
            p.PedestrianLinks = false;   // GRG retired them: CS2 deletes free pathway stubs seconds after placement.
            string fit = s.S(RoadsIds.TerrainFit, "auto");
            p.MaxGrade = N(RoadsIds.MaxGrade) / 100f;
            // s34: the organic main street (an avenue) grows its side streets no closer than the junction spacing.
            p.MainJunctionSpacing = N(RoadsIds.JunctionSpacing) * 0.92f;
            switch (pattern)
            {
                case RoadPattern.Grid:
                    p.Mode = s.S(RoadsIds.GridMode, "spacing") == "count" ? SpacingMode.FitToArea : SpacingMode.FixedSpacing;
                    p.SpacingMeters = N(RoadsIds.GridSpacing);
                    p.Columns = I(RoadsIds.GridColumns);
                    p.Rows = I(RoadsIds.GridRows);
                    p.AngleOffsetDegrees = N(RoadsIds.GridAngle);
                    p.AlignToTerrain = s.B(RoadsIds.GridAlignTerrain, false);
                    p.CulDeSacMode = s.B(RoadsIds.CulDeSacs, false);
                    string axis = s.S(RoadsIds.CulDeSacAxis, "columns");
                    p.CulDeSacAxis = axis == "rows" ? Generators.CulDeSacAxis.Rows : axis == "both" ? Generators.CulDeSacAxis.Both : Generators.CulDeSacAxis.Columns;
                    p.CulDeSacRatio = N(RoadsIds.CulDeSacRatio);
                    p.CulDeSacDepth = N(RoadsIds.CulDeSacDepth) / 100f;
                    p.Staggered = s.B(RoadsIds.CulDeSacStaggered, true);
                    p.AvenueColumnEnabled = s.B(RoadsIds.AvenueColumn, false);
                    p.AvenueColumnIndex = I(RoadsIds.AvenueColumnIndex);
                    p.AvenueRowEnabled = s.B(RoadsIds.AvenueRow, false);
                    p.AvenueRowIndex = I(RoadsIds.AvenueRowIndex);
                    break;
                case RoadPattern.Tree:
                    p.TreeMode = true;
                    p.TreeBranchSpacing = N(RoadsIds.TreeBranchSpacing);
                    p.TreeCulDeSacLength = N(RoadsIds.TreeCulDeSacLength);
                    p.TreeCulDeSacSpacing = N(RoadsIds.TreeCulDeSacSpacing);
                    p.AngleOffsetDegrees = N(RoadsIds.TreeAngle);
                    break;
                case RoadPattern.Organic:
                    p.OrganicMode = true;
                    p.OrganicStreetSpacing = N(RoadsIds.OrganicSpacing);
                    p.OrganicCurviness = N(RoadsIds.OrganicCurviness);
                    p.OrganicLoopShare = N(RoadsIds.OrganicLoops);
                    p.OrganicSeed = I(RoadsIds.OrganicVariation);
                    break;
                case RoadPattern.Contour:
                    p.ContourMode = true;
                    p.ContourSpacing = N(RoadsIds.ContourSpacing);
                    p.ContourConnectorSpacing = N(RoadsIds.ContourLinkSpacing);
                    break;
                case RoadPattern.Mixed:
                    p.MixedMode = true;
                    p.MixedCoreRadius = N(RoadsIds.MixedCoreRadius);
                    p.RadialAvenues = I(RoadsIds.MixedAvenues);
                    p.RadialRoundaboutRadius = 40f;
                    p.RadialLayers = 2;
                    p.OrganicStreetSpacing = N(RoadsIds.MixedSpacing);
                    p.OrganicCurviness = N(RoadsIds.MixedCurviness);
                    p.OrganicLoopShare = GridGenerator.OrganicLoopShareDefault;
                    p.OrganicSeed = GridGenerator.MinOrganicSeed;
                    break;
                case RoadPattern.Loop:
                    p.CollectorSpacingMeters = N(RoadsIds.LoopCollectorSpacing);
                    p.LoopCulDeSacRatio = N(RoadsIds.LoopCulDeSacRatio);
                    p.CulDeSacDepth = N(RoadsIds.LoopCulDeSacDepth) / 100f;
                    p.AngleOffsetDegrees = N(RoadsIds.LoopAngle);
                    p.AlignToTerrain = s.B(RoadsIds.LoopAlignTerrain, false);
                    break;
                case RoadPattern.Superblock:
                    p.SuperblockMode = true;
                    p.SuperblockZoneMeters = N(RoadsIds.SuperblockZone);
                    p.AngleOffsetDegrees = N(RoadsIds.SuperblockAngle);
                    p.AlignToTerrain = s.B(RoadsIds.SuperblockAlignTerrain, false);
                    break;
                case RoadPattern.Concentric:
                    p.ConcentricMode = true;
                    p.ConcentricLayers = I(RoadsIds.ConcentricRings);
                    p.ConcentricConnections = I(RoadsIds.ConcentricConnections);
                    break;
                case RoadPattern.Radial:
                    p.ConcentricMode = true;
                    p.RadialMode = true;
                    p.RadialAvenues = I(RoadsIds.RadialAvenues);
                    p.RadialRoundaboutRadius = N(RoadsIds.RadialRoundaboutRadius);
                    p.RadialLayers = I(RoadsIds.RadialRings);
                    break;
            }
            // s34: "Follow contours" turns the straight patterns along the slope and lets the winding ones steer by the
            // terrain; "Ignore terrain" keeps the geometry strict whatever the pattern's own toggle says.
            if (fit == "follow")
            {
                p.FollowContours = true;
                if (pattern == RoadPattern.Grid || pattern == RoadPattern.Loop || pattern == RoadPattern.Superblock) p.AlignToTerrain = true;
            }
            else if (fit == "ignore") p.AlignToTerrain = false;
            return p;
        }

        /// <summary>The "loop family" runs through GenerateLoopGrid, everything else through GenerateGrid.</summary>
        public static bool IsLoopFamily(RoadPattern p) =>
            p == RoadPattern.Loop || p == RoadPattern.Superblock || p == RoadPattern.Concentric || p == RoadPattern.Radial;

        /// <summary>Composition upgrade flags (game flag names, "Side.Flag" per side) for a role.</summary>
        public static string[] Upgrades(SettingsBag s, NetRole role)
        {
            var r = new List<string>();
            void Both(string flag) { r.Add("Left." + flag); r.Add("Right." + flag); }
            if (role == NetRole.Avenue)
            {
                if (s.B(RoadsIds.AvenueMedianGrass, false)) r.Add("General.PrimaryMiddleBeautification");
                if (s.B(RoadsIds.AvenueMedianTrees, false)) r.Add("General.SecondaryMiddleBeautification");
                if (s.B(RoadsIds.AvenueTrees, false)) Both("SecondaryBeautification");
                if (s.B(RoadsIds.AvenueGrass, false)) Both("PrimaryBeautification");
                if (s.B(RoadsIds.AvenueBikeLanes, false)) Both("SecondaryLane");
            }
            else if (role == NetRole.Street || role == NetRole.CulDeSac || role == NetRole.PerimeterRoad || role == NetRole.Roundabout)
            {
                if (s.B(RoadsIds.StreetTrees, false)) Both("SecondaryBeautification");
                bool wide = s.B(RoadsIds.StreetWideSidewalks, false);
                // Grass verge and wide sidewalk share the same strip: the wide sidewalk wins (GRG rule).
                if (s.B(RoadsIds.StreetGrass, false) && !wide) Both("PrimaryBeautification");
                if (wide) Both("WideSidewalk");
                if (s.B(RoadsIds.StreetBikeLanes, false)) Both("SecondaryLane");
            }
            return r.ToArray();
        }

        public static float SafeAngle(float deg) => math.isfinite(deg) ? deg : 0f;
    }
}
