// Placemaker roads layer ("roads"): street patterns from Grid Road Generator (Ti4goc) behind the
// IAreaGenerator contract. Emits PlannedNets (with connections to existing roads), cul-de-sac caps,
// roundabout islands, and the city blocks between the roads as "block" regions plus frontage
// strips as "edge" regions, so recipes like "roads + parks" or "roads + zoning" compose.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    [AreaGenerator(400, Group = "roads", Loose = true)]
    public sealed class RoadsLayer : IAreaGenerator, IHasFeatures, ILinkedControls, IAutoAssetHints
    {
        public const string LayerId = "roads";

        private readonly List<ControlSchema> _controls;
        private readonly List<PresetInfo> _presets;

        public RoadsLayer()
        {
            _controls = RoadsSchema.Build();
            _presets = BuildPresets();
        }

        public string Id => LayerId;
        public string NameKey => "Placemaker.Roads.Name";
        public string Icon => "Media/Game/Icons/Roads.svg";
        public LayerKind Kind => LayerKind.Fill;
        public string[] Accepts => Array.Empty<string>();
        public string[] Emits => new[] { RegionTags.Block, RegionTags.Edge, RegionTags.Core };
        public int Version => 1;

        /// <summary>
        /// (s34) No features: the old "Zoning" feature (edge-zoning cutouts along the frontage) is replaced by the
        /// "Pre-zone new roads" control — every cell of every new road's zone blocks (playtest image 62). Saved recipes
        /// with the old feature child are migrated on load (<see cref="Migrate"/>).
        /// </summary>
        private static readonly FeatureInfo[] FeatureList = Array.Empty<FeatureInfo>();

        public IReadOnlyList<FeatureInfo> Features => FeatureList;

        /// <summary>
        /// (s34) Loaded roads step with the retired edge-zoning feature child (target "edge"): an enabled one becomes
        /// "Pre-zone new roads" with its zone type (unless the step already pre-zones); the child goes. Idempotent.
        /// </summary>
        public static int Migrate(RecipeStep step)
        {
            if (step == null || step.Layer != LayerId) return 0;
            int n = 0;
            // the retaining walls (quays) are gone: a design saved with them gets banks
            if (step.Settings.S(RoadsIds.TerraceBank, "") == "retaining") { step.Settings.Values[RoadsIds.TerraceBank] = "bank"; n++; }
            for (int i = step.Children.Count - 1; i >= 0; i--)
            {
                var c = step.Children[i];
                if (c == null || c.Layer != AddOns.EdgeZoningAddOn.LayerId || (c.Target ?? "") != RegionTags.Edge) continue;
                // the old feature predates the control: it decides (on: its zone; off: no zoning, not the new default)
                if (c.Enabled)
                {
                    string kind = c.Settings?.S(AddOns.EdgeZoningAddOn.KindControl, Common.ZoneKinds.Default) ?? Common.ZoneKinds.Default;
                    step.Settings.Values[RoadsIds.PreZone] = Array.IndexOf(Common.ZoneKinds.All, kind) >= 0 ? kind : Common.ZoneKinds.Default;
                }
                else step.Settings.Values[RoadsIds.PreZone] = RoadPreZone.None;
                step.Children.RemoveAt(i);
                n++;
            }
            return n;
        }

        public IReadOnlyList<ControlSchema> Controls => _controls;
        public IReadOnlyList<PresetInfo> Presets => _presets;
        public SettingsBag Defaults() => RoadsSchema.Defaults(_controls);

        /// <summary>(s16) Picking a road mix writes every road-type pick and upgrade toggle.</summary>
        public string[] OnValueSet(SettingsBag settings, string controlId) =>
            controlId == RoadsIds.RoadMix ? RoadMixes.Apply(settings, settings?.S(RoadsIds.RoadMix, RoadMixes.Automatic)) : Array.Empty<string>();

        /// <summary>(s16) What Automatic would place for a road role the current layout does not use.</summary>
        public string[] AutoCandidates(string controlId, SettingsBag settings)
        {
            settings = settings ?? Defaults();
            NetRole role;
            switch (controlId)
            {
                case RoadsIds.StreetAsset: role = NetRole.Street; break;
                case RoadsIds.AvenueAsset: role = NetRole.Avenue; break;
                case RoadsIds.LocalAsset: role = NetRole.CulDeSac; break;
                case RoadsIds.RoundaboutAsset: role = NetRole.Roundabout; break;
                case RoadsIds.PerimeterAsset: role = NetRole.PerimeterRoad; break;
                case RoadsIds.LaneAsset: role = NetRole.Alley; break;
                case RoadsIds.PathAsset: return new[] { RoadRoles.PavementPath };
                default: return Array.Empty<string>();
            }
            var a = RoadsPlanner.AssetFor(role, settings, settings.S(RoadsIds.StreetAsset, ""));
            var r = new List<string>();
            if (!string.IsNullOrEmpty(a.Id)) r.Add(a.Id);
            foreach (var f in a.Fallbacks ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(f) && !r.Contains(f)) r.Add(f);
            return r.ToArray();
        }

        /// <summary>(s16) The mix always reads what the picks are: a matching mix, else "custom".</summary>
        public void Derive(SettingsBag settings)
        {
            if (settings != null) settings.Values[RoadsIds.RoadMix] = RoadMixes.Match(settings);
        }

        /// <summary>A few city blocks at least; GRG refused free areas under 2000 m².</summary>
        public float MinArea => 2000f;
        /// <summary>16 km²: the largest painted districts GRG's sweep covers stay interactive.</summary>
        public float MaxArea => 16_000_000f;

        public Readiness CheckReady(AreaInput input)
        {
            var r = new Readiness();
            var ring = input?.Shape?.Ring;
            if (ring == null || ring.Length < 3)
            {
                r.Ready = false;
                r.ReasonKey = "Placemaker.Roads.Reason.NoArea";
                return r;
            }
            var settings = input.Settings ?? new SettingsBag();
            if (RoadsSchema.ParsePattern(settings.S(RoadsIds.Pattern, "grid")) == RoadPattern.Contour)
            {
                Func<float2, float> h = input.Site?.HeightAt;
                if (h == null || GridGenerator.IsTerrainFlat(ring, h))
                {
                    r.Ready = false;
                    r.ReasonKey = RoadsPlanner.IssueFlat;
                }
            }
            return r;
        }

        public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();

        /// <summary>
        /// Varies the current pattern's geometry (spacings, angle, curviness, variation) within the
        /// control ranges. The pattern itself, assets, upgrades and network options are the player's
        /// choices and stay untouched.
        /// </summary>
        public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked)
        {
            var result = current?.Clone() ?? Defaults();
            var rng = Seeds.Rng(seed, "roads.randomize");
            string pattern = result.S(RoadsIds.Pattern, "grid");
            foreach (var c in _controls)
            {
                if (c.Kind != ControlKind.Slider || c.Group != "layout") continue;
                if (locked != null && locked.Contains(c.Id)) continue;
                if (c.VisibleWhen != "pattern=" + pattern) continue;
                double steps = Math.Floor((c.Max - c.Min) / c.Step);
                double v = c.Min + c.Step * Math.Floor(rng.NextDouble() * (steps + 1));
                if (c.Unit == "deg") v = Math.Round(rng.NextDouble() * 30 - 15); // keep grids roughly aligned to the area
                result.Values[c.Id] = Math.Max(c.Min, Math.Min(c.Max, v));
            }
            return result;
        }

        public AreaPlan Plan(AreaInput input, CancellationToken cancel)
        {
            if (input == null) return new AreaPlan();
            return RoadsPlanner.Plan(input, _controls, cancel);
        }

        /// <summary>
        /// (s34) Presets are whole designs: pattern, road mix, terrain fit, junction spacing on the main roads, walkways,
        /// lanes, cul-de-sac tips. Ids are save format (downtown, suburb, village, hillside, garden-city stay).
        /// (Smarter roads) No preset terraces the land except the four terrace designs, whose look is the terraces; the winding
        /// styles round the perimeter's corners more (SoftCorner), the straight ones hug the selection (TightCorner).
        /// </summary>
        private List<PresetInfo> BuildPresets()
        {
            PresetInfo P(string id, string mix, params (string k, object v)[] values)
            {
                var b = Defaults();
                if (mix != null) RoadMixes.Apply(b, mix);
                foreach (var (k, v) in values) b.Values[k] = v;
                Derive(b);   // the mix reads what the preset picks (no false "modified")
                return new PresetInfo { Id = id, LabelKey = "Placemaker.Roads.Preset." + id, Values = b };
            }
            return new List<PresetInfo>
            {
                // A tight downtown grid: avenues every few blocks keep all their junctions, alleys behind the blocks.
                P("downtown", "boulevards", (RoadsIds.Pattern, "grid"), (RoadsIds.GridSpacing, 90.0), (RoadsIds.AvenueColumn, true), (RoadsIds.AvenueRow, true),
                    (RoadsIds.AvenueColumnIndex, 2.0), (RoadsIds.AvenueRowIndex, 2.0), (RoadsIds.JunctionSpacing, 0.0), (RoadsIds.BackLanes, 60.0),
                    (RoadsIds.Walkways, 20.0), (RoadsIds.TerrainFit, "ignore")),
                // Cul-de-sac branches off a collector, footpaths between the bulbs.
                P("suburb", "quiet", (RoadsIds.PerimeterCornerRadius, (double)RoadsSchema.SoftCorner), (RoadsIds.Pattern, "tree"), (RoadsIds.CapStyle, "grass"), (RoadsIds.Walkways, 75.0), (RoadsIds.JunctionSpacing, 150.0),
                    (RoadsIds.TerrainFit, "follow")),
                // Winding lanes that settle into the hill, a quiet main street, gravel at some tips, paths and trails everywhere.
                P("village", "quiet", (RoadsIds.PerimeterCornerRadius, (double)RoadsSchema.SoftCorner), (RoadsIds.Pattern, "organic"), (RoadsIds.OrganicSpacing, 75.0), (RoadsIds.OrganicCurviness, 55.0),
                    (RoadsIds.OrganicLoops, 50.0), (RoadsIds.TerrainFit, "follow"), (RoadsIds.JunctionSpacing, 140.0), (RoadsIds.Walkways, 70.0),
                    (RoadsIds.WalkwayStyle, "mixed"), (RoadsIds.TipStyle, "mixed"), (RoadsIds.CapStyle, "grass"), (RoadsIds.MaxGrade, 9.0)),
                // Streets along the contour lines, gentle climbs between them, dirt trails straight up the slope where it is walkable.
                P("hillside", "quiet", (RoadsIds.PerimeterCornerRadius, (double)RoadsSchema.SoftCorner), (RoadsIds.Pattern, "contour"), (RoadsIds.TerrainFit, "follow"), (RoadsIds.Walkways, 60.0),
                    (RoadsIds.WalkwayStyle, "mixed"), (RoadsIds.MaxGrade, 9.0)),
                // A planted centre: avenues with trees around a roundabout, ring streets, footpaths.
                P("garden-city", "boulevards", (RoadsIds.PerimeterCornerRadius, (double)RoadsSchema.SoftCorner), (RoadsIds.Pattern, "radial"), (RoadsIds.RadialRings, 2.0), (RoadsIds.RadialAvenues, 6.0),
                    (RoadsIds.Walkways, 60.0), (RoadsIds.CapStyle, "trees"), (RoadsIds.JunctionSpacing, 0.0), (RoadsIds.TerrainFit, "ignore")),
                // Small blocks, paths through the long ones, lanes behind: everything a short walk away.
                P("walkable-grid", "bike", (RoadsIds.Pattern, "grid"), (RoadsIds.GridSpacing, 80.0), (RoadsIds.Walkways, 100.0), (RoadsIds.BackLanes, 40.0),
                    (RoadsIds.JunctionSpacing, 0.0), (RoadsIds.TerrainFit, "ignore"), (RoadsIds.StreetTrees, true)),
                // A grid turned along the hill: long streets on the contours, short cross streets up the slope.
                P("terraced-grid", "quiet", (RoadsIds.Pattern, "grid"), (RoadsIds.GridSpacing, 90.0), (RoadsIds.TerrainFit, "follow"),
                    (RoadsIds.Walkways, 60.0), (RoadsIds.WalkwayStyle, "mixed"), (RoadsIds.JunctionSpacing, 0.0)),
                // Collector loops with cul-de-sacs inside, footpath greenways linking every close to the next.
                P("suburban-loops", "quiet", (RoadsIds.PerimeterCornerRadius, (double)RoadsSchema.SoftCorner), (RoadsIds.Pattern, "loop"), (RoadsIds.LoopCollectorSpacing, 260.0), (RoadsIds.LoopCulDeSacRatio, 70.0),
                    (RoadsIds.Walkways, 90.0), (RoadsIds.CapStyle, "trees"), (RoadsIds.JunctionSpacing, 150.0), (RoadsIds.TerrainFit, "follow")),
                // A loose hamlet: gravel lanes, a paved through road, trails.
                P("rural-village", "country", (RoadsIds.PerimeterCornerRadius, (double)RoadsSchema.SoftCorner), (RoadsIds.Pattern, "organic"), (RoadsIds.OrganicSpacing, 100.0), (RoadsIds.OrganicCurviness, 45.0),
                    (RoadsIds.OrganicLoops, 20.0), (RoadsIds.TerrainFit, "follow"), (RoadsIds.TipStyle, "gravel"), (RoadsIds.Walkways, 40.0),
                    (RoadsIds.WalkwayStyle, "trail"), (RoadsIds.CapStyle, "grass"), (RoadsIds.JunctionSpacing, 160.0), (RoadsIds.MaxGrade, 10.0)),
                // Collectors around groups of blocks, car-free streets inside.
                P("superblocks", null, (RoadsIds.Pattern, "superblock"), (RoadsIds.SuperblockZone, 100.0), (RoadsIds.Walkways, 60.0),
                    (RoadsIds.JunctionSpacing, 0.0), (RoadsIds.TerrainFit, "ignore")),
                // Lanes radiating from a small central circus, crooked narrow streets around it, paths through everything.
                P("old-town", "alleys", (RoadsIds.PerimeterCornerRadius, (double)RoadsSchema.SoftCorner), (RoadsIds.Pattern, "mixed"), (RoadsIds.MixedCoreRadius, 110.0), (RoadsIds.MixedAvenues, 6.0),
                    (RoadsIds.MixedSpacing, 60.0), (RoadsIds.MixedCurviness, 85.0), (RoadsIds.Walkways, 80.0), (RoadsIds.JunctionSpacing, 0.0),
                    (RoadsIds.TerrainFit, "follow"), (RoadsIds.CapStyle, "none")),
                // The straight grid, whatever the ground does.
                P("strict-grid", null, (RoadsIds.Pattern, "grid"), (RoadsIds.GridSpacing, 100.0), (RoadsIds.TerrainFit, "ignore"),
                    (RoadsIds.Walkways, 30.0), (RoadsIds.JunctionSpacing, 0.0)),
                // (s52) Hillside terraces. Contour streets on level terraces four cells deep, graded banks, links straight up or
                // diagonal, stair-free bank walks.
                P("terraced-hillside", "quiet", (RoadsIds.LandShape, "terrace"), (RoadsIds.Pattern, "terraces"), (RoadsIds.TerraceCells, 4.0), (RoadsIds.TerraceLinks, "direct"),
                    (RoadsIds.TerraceLinkSpacing, 300.0), (RoadsIds.Walkways, 60.0), (RoadsIds.WalkwayStyle, "mixed"), (RoadsIds.MaxGrade, 9.0)),
                // Narrower terraces climbed by switchbacks, dirt trails down the banks, grassy turning circles.
                P("serpentine-village", "quiet", (RoadsIds.LandShape, "terrace"), (RoadsIds.Pattern, "terraces"), (RoadsIds.TerraceCells, 3.0), (RoadsIds.TerraceLinks, "serpentine"),
                    (RoadsIds.TerraceLinkSpacing, 260.0), (RoadsIds.Walkways, 70.0), (RoadsIds.WalkwayStyle, "trail"), (RoadsIds.MaxGrade, 8.0),
                    (RoadsIds.CapStyle, "grass")),
                // Many narrow terraces on gravel lanes, steep worn faces, switchbacks: the hill reads as a vineyard.
                P("vineyard-terraces", "country", (RoadsIds.LandShape, "terrace"), (RoadsIds.Pattern, "terraces"), (RoadsIds.TerraceCells, 2.0), (RoadsIds.TerraceLinks, "serpentine"),
                    (RoadsIds.TerraceBank, "wall"), (RoadsIds.TerraceBankSlope, 50.0), (RoadsIds.TerraceFall, 0.5), (RoadsIds.Walkways, 40.0),
                    (RoadsIds.WalkwayStyle, "trail"), (RoadsIds.TerraceTrees, false), (RoadsIds.MaxGrade, 10.0)),
                // Deep terraces for apartments, one tree-lined avenue winding up round the hill, paved walks everywhere.
                P("hill-town", "boulevards", (RoadsIds.LandShape, "terrace"), (RoadsIds.Pattern, "terraces"), (RoadsIds.TerraceCells, 5.0), (RoadsIds.TerraceLinks, "spiral"),
                    (RoadsIds.TerraceAvenues, true), (RoadsIds.TerraceLinkSpacing, 240.0), (RoadsIds.Walkways, 80.0), (RoadsIds.WalkwayStyle, "paved"),
                    (RoadsIds.MaxGrade, 8.0)),
            };
        }
    }
}
