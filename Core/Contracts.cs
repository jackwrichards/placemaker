// Placemaker — engine-free contracts shared by every area generator.
//
// Everything in Core/ is pure .NET + Unity.Mathematics: no Game.*, Colossal.*, Unity.Entities.
// That keeps generators runnable off the main thread and testable without the game.
// The game layer (Game/) turns an AreaPlan into CreationDefinitions; generators never
// touch ECS.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core
{
    // ───────────────────────────── Input ─────────────────────────────

    public enum AreaSource { Drawn = 0, Block = 1, Painted = 2, Perimeter = 3 }

    /// <summary>
    /// The selected area. All selection modes (draw, block pick, paint, node perimeter)
    /// reduce to this. Ring is counter-clockwise in the XZ plane, implicitly closed
    /// (last point != first), simple (no self intersections), ≥3 points. Y = terrain height.
    /// </summary>
    public sealed class AreaShape
    {
        public float3[] Ring = Array.Empty<float3>();
        public AreaSource Source;

        /// <summary>
        /// Points where existing networks touch or cross the outline. Produced by the game
        /// layer (block pick / perimeter mode know the real road entities; draw/paint get
        /// crossings computed against nearby roads).
        /// </summary>
        public List<BoundaryAttachment> Attachments = new List<BoundaryAttachment>();

        public float2[] Ring2D()
        {
            var r = new float2[Ring.Length];
            for (int i = 0; i < Ring.Length; i++) r[i] = Ring[i].xz;
            return r;
        }
    }

    public enum AttachmentKind { RoadAlongEdge = 0, RoadCrossing = 1, RoadNode = 2 }

    public struct BoundaryAttachment
    {
        public AttachmentKind Kind;
        /// <summary>Opaque id of the existing net element (edge or node) — the game layer maps it back to an Entity.</summary>
        public long ExternalId;
        /// <summary>For edges: curve parameter of the touch point on that edge.</summary>
        public float ExternalT;
        public float3 Position;
        /// <summary>Unit direction of the road at the touch point (XZ, y=0).</summary>
        public float3 Direction;
        public float RoadWidth;
        /// <summary>Index of the ring segment (Ring[i] → Ring[i+1]) and parameter along it.</summary>
        public int RingSegment;
        public float RingT;
    }

    /// <summary>Road/path edge near the area, for entrances, access points and connections.</summary>
    public struct NearbyNet
    {
        public long ExternalId;
        public float3 A, B, C, D;          // cubic Bézier control points
        public float Width;
        public NetCategory Category;
        public bool HasSidewalk;
        public bool IsOneWay;
        /// <summary>Carries bicycles on their own lane: a bike path, a pathway with bike lanes or a road with bike lanes.</summary>
        public bool HasBikeLane;
        /// <summary>(appended) A road under highway rules (motorways, highway ramps): new roads never join or cross it at grade.</summary>
        public bool IsHighway;
        /// <summary>(appended) Speed limit in km/h, 0 = unknown.</summary>
        public float SpeedLimit;
    }

    public enum NetCategory { Road = 0, Pathway = 1, Track = 2, Other = 3 }

    /// <summary>Footprint of something the generator should avoid (building lot, water, utility).</summary>
    public struct Obstacle
    {
        public float2 A, B, C, D;   // oriented quad
        public ObstacleKind Kind;
    }

    public enum ObstacleKind { Building = 0, Water = 1, Utility = 2, Other = 3 }

    /// <summary>
    /// (s19) An existing zone block of a road (Game.Zones.Block, XZ only): <see cref="Size"/>.x cells along the
    /// road, .y cells deep (≤ 6), 8 m cells, centred on <see cref="Position"/>; <see cref="Direction"/> points
    /// from the block towards its road. Cell geometry: Core/Build/ZoneGuard (mirrors ZoneUtils.GetCellPosition).
    /// </summary>
    public struct ZoneGrid
    {
        public float2 Position;
        public float2 Direction;
        public int2 Size;
        /// <summary>
        /// (s22, appended) The game's per-cell state (Game.Zones.Cell, index x + y·Size.x): <see cref="CellVisible"/>
        /// and/or <see cref="CellBlocked"/>. Null = unknown (tests, older captures): every cell counts as open. On a
        /// curved road the blocks of neighbouring segments overlap and the game hides the losing cells (not Visible);
        /// cells under a road are Blocked. ZoneGuard never zones those.
        /// </summary>
        public byte[] Cells;
        /// <summary>(s26, appended) Predicted from a road this build adds (Build/ZoneBlockPredictor), not captured.</summary>
        public bool Predicted;
        /// <summary>(s26, appended) A live block of a road this build built (its lanes): preferred like a predicted one.</summary>
        public bool Ours;
        /// <summary>(s40, appended) Predicted for an EXISTING road (nothing captured: tests, gallery); ranks like a captured block.</summary>
        public bool Existing;

        public const byte CellVisible = 1, CellBlocked = 2;
    }

    /// <summary>
    /// Thread-safe snapshot of the world around the area, captured on the main thread before
    /// planning. Generators may run on a worker thread; they must only read this.
    /// </summary>
    public sealed class SiteContext
    {
        /// <summary>Terrain height at an XZ point. Must be safe to call from any thread.</summary>
        public Func<float2, float> HeightAt = _ => 0f;
        /// <summary>Water depth at an XZ point (0 = dry). Thread-safe.</summary>
        public Func<float2, float> WaterDepthAt = _ => 0f;
        public List<NearbyNet> NearbyNets = new List<NearbyNet>();
        public List<Obstacle> Obstacles = new List<Obstacle>();
        /// <summary>(s19) Zone blocks of existing roads near the area: ZoneGuard snaps the zoning and the blocking to their cells.</summary>
        public List<ZoneGrid> ZoneGrids = new List<ZoneGrid>();
        public bool LeftHandTraffic;
        /// <summary>Asset metadata the generator may need for footprints/spacing (keyed by AssetRef.Id).</summary>
        public Dictionary<string, AssetInfo> Assets = new Dictionary<string, AssetInfo>();

        /// <summary>
        /// (FEEDBACK-4) Global "Layout size": multiplies layout DISTANCES (path loop sizes, spacing between
        /// objects, plaza/feature footprints, grid pitches, margins). Props, path widths and real-dimension
        /// layouts (parking stalls, roads, sports fields, golf greens/tees) keep their real size. 1 = the
        /// design size the generators were written at; the game passes the player's option (default 0.5).
        /// Read through <see cref="Scale"/> (clamped). See docs/proposals/size.md.
        /// </summary>
        public float LayoutScale = 1f;
        /// <summary>
        /// (FEEDBACK-4) The one edge margin (design metres, multiplied by the scale) every type keeps between
        /// the outline and its paths/layout. Object clearances stay footprint-based on top of it.
        /// </summary>
        public float EdgeMargin = DefaultEdgeMargin;
        /// <summary>
        /// (s33) The map's four terrain paint materials (texture names, measured colours, blend settings), captured by the
        /// game; null = assume the vanilla slots. See Core/Paint/TerrainPalette and docs/TERRAIN-PAINT.md.
        /// </summary>
        public Paint.TerrainPalette Terrain;

        public const float LayoutScaleMin = 0.25f, LayoutScaleMax = 1.5f, DefaultEdgeMargin = 2f, DefaultLayoutScale = 0.5f;

        /// <summary>LayoutScale clamped to its valid range (NaN/≤0 → 1).</summary>
        public float Scale => LayoutScale > 0f && !float.IsNaN(LayoutScale) ? Math.Min(LayoutScaleMax, Math.Max(LayoutScaleMin, LayoutScale)) : 1f;

        /// <summary>Same snapshot with another layout scale / edge margin (shallow copy).</summary>
        public SiteContext WithLayout(float layoutScale, float edgeMargin)
        {
            var c = WithHeight(HeightAt);
            c.LayoutScale = layoutScale;
            c.EdgeMargin = edgeMargin;
            return c;
        }

        /// <summary>
        /// (g5) Same snapshot with another ground height: the composer hands children of a terraforming
        /// layer the ground after that layer's terraform (AreaPlan.HeightAfter). Shallow copy.
        /// </summary>
        public SiteContext WithHeight(Func<float2, float> heightAt) => new SiteContext
        {
            HeightAt = heightAt ?? HeightAt, WaterDepthAt = WaterDepthAt, NearbyNets = NearbyNets, Obstacles = Obstacles, ZoneGrids = ZoneGrids,
            LeftHandTraffic = LeftHandTraffic, Assets = Assets, LayoutScale = LayoutScale, EdgeMargin = EdgeMargin, Terrain = Terrain,
        };
    }

    /// <summary>Static facts about a game asset, resolved by the game-layer catalog.</summary>
    public struct AssetInfo
    {
        public string Id;
        public float3 BoundsMin, BoundsMax;   // object-space bounds
        public float NetWidth;                // nets only
        /// <summary>Construction cost: per item (objects), per metre (nets), per m² (surfaces/areas). 0 = free/unknown.</summary>
        public float Cost;
        /// <summary>Monthly upkeep in the same units as Cost. 0 = none/unknown.</summary>
        public float Upkeep;
        /// <summary>(s26, appended) Roads: 1 = the prefab carries zone blocks (RoadData.m_ZoneBlockPrefab), 2 = it does not, 0 = unknown.</summary>
        public byte ZoneBlocks;
    }

    /// <summary>A user-placed marker on the outline (entrance, access point, exit…).</summary>
    public struct EdgeMarker
    {
        public string Kind;        // generator-defined, e.g. "entrance", "exit", "access"
        public int RingSegment;
        public float RingT;
        public float3 Position;
        /// <summary>
        /// The player wants a crosswalk where this entrance's path meets the street (Layers/Common/StreetJoins). Recipes,
        /// receipts and the tool keep it as the kind's <see cref="MarkerVariants.CrosswalkSuffix"/>; Composer.MarkersOn turns
        /// that into this flag and the plain kind, so layers only ever see the kinds they know.
        /// </summary>
        public bool Crosswalk;
    }

    /// <summary>
    /// Entrance variants stored in the marker kind (no save format change: an older Placemaker sees an unknown kind and
    /// leaves the marker out). "pedestrian+crosswalk" = a pedestrian entrance whose path splits the street and gets a crosswalk.
    /// </summary>
    public static class MarkerVariants
    {
        public const string CrosswalkSuffix = "+crosswalk";

        public static bool HasCrosswalk(string kind) => kind != null && kind.EndsWith(CrosswalkSuffix, StringComparison.Ordinal);

        /// <summary>The kind without its variant.</summary>
        public static string BaseKind(string kind) => HasCrosswalk(kind) ? kind.Substring(0, kind.Length - CrosswalkSuffix.Length) : kind;

        public static string WithCrosswalk(string kind, bool on) => on ? BaseKind(kind) + CrosswalkSuffix : BaseKind(kind);

        /// <summary>The marker as layers see it: plain kind, <see cref="EdgeMarker.Crosswalk"/> set from the suffix.</summary>
        public static EdgeMarker Normalize(EdgeMarker m)
        {
            if (!HasCrosswalk(m.Kind)) return m;
            m.Kind = BaseKind(m.Kind);
            m.Crosswalk = true;
            return m;
        }

        /// <summary>
        /// Entrance kinds people walk in through, whose path can meet a street: the crosswalk choice applies to them.
        /// "entrance" walks only for the park family (where it is the old pedestrian kind); with car kinds beside it
        /// (parking, malls, markets) it is the two-way car entrance.
        /// </summary>
        public static bool Walks(string kind, IEnumerable<string> stepKinds)
        {
            switch (BaseKind(kind))
            {
                case "pedestrian": case "bike": case "main": case "footpath": case "access": return true;
                case "entrance":
                    if (stepKinds != null)
                        foreach (var k in stepKinds)
                            if (k == "entry" || k == "exit" || k == "alley") return false;
                    return true;
                default: return false;
            }
        }
    }

    public sealed class AreaInput
    {
        public AreaShape Shape = new AreaShape();
        public SiteContext Site = new SiteContext();
        public List<EdgeMarker> Markers = new List<EdgeMarker>();
        public SettingsBag Settings = new SettingsBag();
        public uint Seed;

        /// <summary>Tags of the region this layer runs in ("root", "block", "green", "edge"…). See RegionTags.</summary>
        public string[] RegionTags = Array.Empty<string>();
        /// <summary>
        /// Space already used by earlier layers in this recipe (nets, objects, surfaces of the parent
        /// and of sibling add-ons). Layers must not place things that overlap it.
        /// </summary>
        public ReservedSpace Reserved = new ReservedSpace();

        /// <summary>
        /// Layer ids and targets of the enabled child steps (features) of this step, filled by the
        /// composer. A parent uses them to make room for what its features need — e.g. the park
        /// carves out the "place:plaza" region only when a child will fill it.
        /// </summary>
        public string[] ChildLayers = Array.Empty<string>();
        public string[] ChildTargets = Array.Empty<string>();

        /// <summary>The same input over another site snapshot (shallow copy): a layer that carves the ground before it plans
        /// (ShoreCarve) plans against the carved site.</summary>
        public AreaInput WithSite(SiteContext site)
        {
            var c = (AreaInput)MemberwiseClone();
            c.Site = site;
            return c;
        }

        public bool HasChild(string layerId) => ChildLayers != null && Array.IndexOf(ChildLayers, layerId) >= 0;
        public bool HasChildTarget(string target) => ChildTargets != null && Array.IndexOf(ChildTargets, target) >= 0;
    }

    /// <summary>
    /// Footprints already claimed by earlier layers. Pure geometry; the composer fills it and
    /// child layers query it. Kept simple on purpose (linear scans with a coarse grid index
    /// built lazily) — recipes produce hundreds to low thousands of items.
    /// </summary>
    public sealed class ReservedSpace
    {
        public struct Corridor { public float2 A, B; public float HalfWidth; public string Tag; }
        public struct Disc { public float2 Center; public float Radius; public string Tag; }

        public readonly List<Corridor> Corridors = new List<Corridor>();
        public readonly List<Disc> Discs = new List<Disc>();
        /// <summary>Polygons that are fully claimed (e.g. a parking lot inside a mixed recipe).</summary>
        public readonly List<float2[]> Polygons = new List<float2[]>();

        public void AddCorridor(float2 a, float2 b, float halfWidth, string tag = "") =>
            Corridors.Add(new Corridor { A = a, B = b, HalfWidth = halfWidth, Tag = tag });
        public void AddDisc(float2 c, float r, string tag = "") =>
            Discs.Add(new Disc { Center = c, Radius = r, Tag = tag });

        /// <summary>True when a disc at p with radius r would overlap anything reserved.</summary>
        public bool Blocks(float2 p, float r)
        {
            foreach (var d in Discs)
                if (math.distancesq(p, d.Center) < (r + d.Radius) * (r + d.Radius)) return true;
            foreach (var c in Corridors)
                if (DistSqToSegment(p, c.A, c.B) < (r + c.HalfWidth) * (r + c.HalfWidth)) return true;
            foreach (var poly in Polygons)
                if (PointInPolygon(p, poly)) return true;
            return false;
        }

        public ReservedSpace Clone()
        {
            var r = new ReservedSpace();
            r.Corridors.AddRange(Corridors);
            r.Discs.AddRange(Discs);
            r.Polygons.AddRange(Polygons);
            return r;
        }

        internal static float DistSqToSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float len = math.lengthsq(ab);
            float t = len > 1e-9f ? math.saturate(math.dot(p - a, ab) / len) : 0f;
            return math.distancesq(p, a + ab * t);
        }

        internal static bool PointInPolygon(float2 p, float2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                    (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>Well-known region tags. Generators may define more; these are the shared vocabulary.</summary>
    public static class RegionTags
    {
        public const string Root = "root";        // the whole selected area
        public const string Block = "block";      // a block enclosed by generated roads
        public const string Green = "green";      // unpaved open space (lawn between paths, medians…)
        public const string Paved = "paved";      // plaza-like hard surface
        public const string Edge = "edge";        // strip along the outer boundary (zoning frontage, hedges)
        public const string Part = "part";        // a user split part ("part:0", "part:1" also emitted)
        public const string Lot = "lot";          // parking lot interior
        public const string Core = "core";        // focal centre (plaza centre, park lawn)
    }

    // ───────────────────────────── Settings & schema ─────────────────────────────

    /// <summary>
    /// Generator settings as plain values so the UI, undo history, presets and the build
    /// receipt can all handle any generator uniformly. Values: double, bool, string, string[].
    /// </summary>
    public sealed class SettingsBag
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>
        /// (Phase 1) While planning, the ids read through Num/F/I/B/S/L (null = not recorded). A control the plan never read does
        /// nothing in this style here: the panel greys it out with a reason (Tool/ControlsShown.Unused). Never cloned.
        /// </summary>
        public HashSet<string> Reads;

        private void Note(string id) { var r = Reads; if (r != null && id != null) r.Add(id); }

        public double Num(string id, double fallback)
        {
            Note(id);
            return Values.TryGetValue(id, out var v) && v is IConvertible c ? c.ToDouble(System.Globalization.CultureInfo.InvariantCulture) : fallback;
        }
        public float F(string id, float fallback) => (float)Num(id, fallback);
        public int I(string id, int fallback) => (int)Math.Round(Num(id, fallback));
        public bool B(string id, bool fallback) { Note(id); return Values.TryGetValue(id, out var v) && v is bool b ? b : fallback; }
        public string S(string id, string fallback) { Note(id); return Values.TryGetValue(id, out var v) && v is string s ? s : fallback; }
        public string[] L(string id) { Note(id); return Values.TryGetValue(id, out var v) && v is string[] a ? a : Array.Empty<string>(); }

        public SettingsBag Clone()
        {
            var c = new SettingsBag();
            foreach (var kv in Values)
                c.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
            return c;
        }

        public bool ContentEquals(SettingsBag other)
        {
            if (other == null || other.Values.Count != Values.Count) return false;
            foreach (var kv in Values)
            {
                if (!other.Values.TryGetValue(kv.Key, out var o)) return false;
                if (kv.Value is string[] a && o is string[] b)
                {
                    if (a.Length != b.Length) return false;
                    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                }
                else if (!Equals(kv.Value, o)) return false;
            }
            return true;
        }
    }

    public enum ControlKind
    {
        Slider = 0,      // numeric: Min/Max/Step/Unit
        Toggle = 1,      // bool
        Choice = 2,      // string: one of Options (segmented buttons / icons)
        Asset = 3,       // string: one asset id from AssetCategory ("" = automatic)
        AssetMulti = 4,  // string[]: several asset ids from AssetCategory (empty = automatic mix)
        Markers = 5,     // edge markers of MarkerKinds — tells the UI/tool this generator uses markers
        Arrangement = 6, // string[]: ordered sequence editor (plaza furniture)
    }

    public enum ControlTier { Essential = 0, More = 1 }

    public sealed class ControlOption
    {
        public string Value = "";
        public string LabelKey = "";
        public string Icon = "";      // coui:// path or "" for text only
        public string TooltipKey = "";
        /// <summary>(s16) One visible line under the option label ("Streets: Two-Lane Road · Avenues: Four-Lane Road"); "" = none.</summary>
        public string DetailKey = "";
        /// <summary>(s16) Tiny generated picture of the option: stripes drawn by role in their colours and relative widths. Empty = none.</summary>
        public List<OptionSwatch> Swatch = new List<OptionSwatch>();
    }

    /// <summary>(s16) One stripe of an option swatch (e.g. a road mix: the street, avenue and local road it places).</summary>
    public sealed class OptionSwatch
    {
        /// <summary>Role id: roads "street", "avenue", "local", "roundabout", "perimeter", "carFree"; (s18) ground palettes "field", "border", "focal", "path", "inlay".</summary>
        public string Role = "";
        /// <summary>CSS hex colour, "#rrggbb".</summary>
        public string Color = "";
        /// <summary>Real width in metres (the UI scales stripes relative to each other).</summary>
        public float Width;
        /// <summary>Street trees / green verges on this road (drawn as a green edge).</summary>
        public bool Green;
        /// <summary>Bike lanes on this road.</summary>
        public bool Bike;
    }

    /// <summary>Declarative description of one control. The UI renders controls purely from this.</summary>
    public sealed class ControlSchema
    {
        public string Id = "";
        public ControlKind Kind;
        public ControlTier Tier;
        public string Group = "";        // section id within the tier, e.g. "layout", "planting"
        public string LabelKey = "";
        public string TooltipKey = "";
        /// <summary>(s16) Plain one-line explanation shown under the label (what the control is FOR); "" = none.</summary>
        public string DescriptionKey = "";
        public string Icon = "";
        public double Min, Max = 1, Step = 1;
        public string Unit = "";         // "m", "%", "deg", "count", "money"…
        public object Default;
        public List<ControlOption> Options = new List<ControlOption>();
        public string AssetCategory = "";            // for Asset/AssetMulti/Arrangement
        public string[] MarkerKinds = Array.Empty<string>();
        /// <summary>
        /// The control applies only when another control has a value, e.g. "fence=true" or "angleMode=fixed"
        /// ("a|b" alternatives, "!=" negation, several joined with "&amp;"). s15: while it does not apply the UI
        /// shows the control GREYED OUT with a reason ("Only when Fence is on") instead of hiding it — unless
        /// <see cref="HideWhenOff"/> is set. Older UIs hide it (additive change).
        /// </summary>
        public string VisibleWhen = "";
        /// <summary>
        /// (s15) Exception to disable-with-reason: hide the control while <see cref="VisibleWhen"/> is false. Only
        /// for sets where greyed controls would bury the useful ones (e.g. Roads' per-pattern essentials).
        /// </summary>
        public bool HideWhenOff;
        /// <summary>(s15) Optional condition (VisibleWhen syntax) for the control to be ENABLED; false = shown greyed with a reason.</summary>
        public string EnabledWhen = "";
        /// <summary>
        /// (s15) Localisation key of the greyed-out reason ("Only for Alley layout"). Empty = the UI words it from
        /// the condition ("Only when Layout is Alley").
        /// </summary>
        public string DisabledReasonKey = "";
        /// <summary>Changing this control keeps the current seed/layout (true) or needs a full re-plan (false). Hint for preview cost.</summary>
        public bool Cheap;
        /// <summary>
        /// (FEEDBACK-4) A layout distance (spacing, footprint, pitch): LayerContext.F reads it multiplied by
        /// the site's layout scale, never below <see cref="LayoutMin"/> real metres. Real dimensions (net
        /// widths, heights, depths, stall sizes, regulation fields) leave this false.
        /// </summary>
        public bool Layout;
        /// <summary>Real-metre floor of a Layout control after scaling (0 = none).</summary>
        public double LayoutMin;
    }

    public sealed class PresetInfo
    {
        public string Id = "";
        public string LabelKey = "";
        public string Icon = "";
        public SettingsBag Values = new SettingsBag();
        /// <summary>
        /// Feature layers this preset turns on besides the type's DefaultOn features; "-layer" turns a
        /// DefaultOn feature off (playtest 4b: presets should look complete).
        /// </summary>
        public string[] Features = Array.Empty<string>();
        /// <summary>
        /// (rf-parks) Settings this preset gives its feature children, per feature layer, over the feature's own preset
        /// (FeatureInfo.Preset): a picnic park has eight picnic groups where another park style has two.
        /// </summary>
        public Dictionary<string, SettingsBag> FeatureValues = new Dictionary<string, SettingsBag>(StringComparer.Ordinal);

        /// <summary>Whether the preset has feature <paramref name="f"/> on.</summary>
        public bool FeatureOn(FeatureInfo f)
        {
            if (f == null) return false;
            if (Array.IndexOf(Features, "-" + f.Layer) >= 0) return false;
            return f.DefaultOn || Array.IndexOf(Features, f.Layer) >= 0;
        }
    }

    /// <summary>
    /// What the generator needs from the user before Build can be pressed beyond a valid area.
    /// Drives the primary button's label ("Place an entrance") and disabled reason.
    /// </summary>
    public sealed class Readiness
    {
        public bool Ready = true;
        public string NextStepKey = "";        // e.g. "Placemaker.Step.PlaceEntrance"
        public string ReasonKey = "";          // tooltip when not ready
        public object[] ReasonArgs = Array.Empty<object>();
    }

    // ───────────────────────────── Generator ─────────────────────────────

    /// <summary>
    /// What a layer does inside a recipe.
    ///  * Fill     — turns a region into something (park, plaza, parking, roads). May emit sub-regions.
    ///  * Split    — divides a region into parts without building much itself (divider line, ratio, grid).
    ///  * AddOn    — adds to a region another layer already filled (fountain, restrooms, playground,
    ///               big trees, edge zoning). Must respect Reserved.
    /// </summary>
    public enum LayerKind { Fill = 0, Split = 1, AddOn = 2 }

    /// <summary>
    /// A built-in feature a Fill layer offers (playground in a park, trees in a plaza, zoning along
    /// new streets). Turning a feature on adds a child RecipeStep running FeatureLayer on Target;
    /// the UI shows features as toggles with their own (collapsed) options. This replaces free-form
    /// mixing in the UI (decision 2026-10-13: refine each type with features, shelve split/mix).
    /// </summary>
    public sealed class FeatureInfo
    {
        /// <summary>IAreaGenerator.Id of the add-on (or fill) layer that implements the feature.</summary>
        public string Layer = "";
        /// <summary>Region of the parent it runs in ("" = whole parent region, or a tag like "core", "green", "edge").</summary>
        public string Target = "";
        public bool DefaultOn;
        /// <summary>Label key override; empty = the feature layer's NameKey.</summary>
        public string LabelKey = "";
        /// <summary>Settings applied on top of the feature layer's defaults when enabled from this parent.</summary>
        public SettingsBag Preset = new SettingsBag();
        /// <summary>
        /// Ids of the PARENT's controls that belong to this feature (e.g. the park's plaza shape /
        /// position / size). The UI shows them under the feature toggle, only while it is on.
        /// </summary>
        public string[] Options = Array.Empty<string>();
        /// <summary>Tooltip key ("" = none).</summary>
        public string TooltipKey = "";
    }

    /// <summary>
    /// (s16) Optional: a generator whose controls are linked — one control writes several others (a road
    /// "mix" sets every road-type pick) and its own value is derived from them (any other combination
    /// reads "custom"). DesignEditor calls it after every value change; UiModels derives before display.
    /// </summary>
    public interface ILinkedControls
    {
        /// <summary>
        /// <paramref name="controlId"/> was just set in <paramref name="settings"/>: write the controls it
        /// drives and return their ids (they get locked like the control itself). Empty = nothing linked.
        /// </summary>
        string[] OnValueSet(SettingsBag settings, string controlId);
        /// <summary>Recomputes derived controls (e.g. the mix matching the current picks) in place.</summary>
        void Derive(SettingsBag settings);
    }

    /// <summary>
    /// (s16) Optional: what Automatic WOULD place for an asset control the current plan does not use (an
    /// avenue road in a layout without avenues), best first. The panel shows it as "Automatic · Four-Lane
    /// Road" marked "not in this layout", so the player knows what a pattern change will bring.
    /// </summary>
    public interface IAutoAssetHints
    {
        string[] AutoCandidates(string controlId, SettingsBag settings);
    }

    /// <summary>(s15) Something the player can see but not use right now, and why (shown greyed with this reason).</summary>
    public sealed class Unavailable
    {
        /// <summary>Control id of the step, or "feature:&lt;layerId&gt;" for a built-in feature toggle.</summary>
        public string Id = "";
        public string ReasonKey = "";
        public object[] ReasonArgs = Array.Empty<object>();

        public const string FeaturePrefix = "feature:";
        public static string Feature(string layerId) => FeaturePrefix + layerId;
    }

    /// <summary>
    /// (s15, optional) A layer whose controls or features depend on the AREA (not on other values — that is
    /// <see cref="ControlSchema.VisibleWhen"/>): e.g. "Needs a street edge", "Area too small". Cheap, main
    /// thread, called when the outline or the step's settings change (never per frame). Anything listed is
    /// shown greyed out with the reason instead of being hidden.
    /// </summary>
    public interface IHasAvailability
    {
        void Unavailable(AreaInput input, List<Unavailable> into);
    }

    /// <summary>Optional: Fill layers that offer built-in features implement this too.</summary>
    public interface IHasFeatures
    {
        IReadOnlyList<FeatureInfo> Features { get; }
    }

    /// <summary>
    /// A layer of a recipe. Historic name kept: every buildable thing — park, plaza, parking,
    /// roads, campground, playground, restrooms, edge zoning — implements this. Adding a new type
    /// = one class with [AreaGenerator] + its localisation keys; the registry, UI, presets,
    /// undo, receipts and composer pick it up automatically.
    /// </summary>
    public interface IAreaGenerator
    {
        /// <summary>Stable id: "parking", "plaza", "park", "roads". Stored in saves — never change.</summary>
        string Id { get; }
        string NameKey { get; }
        string Icon { get; }
        LayerKind Kind { get; }
        /// <summary>
        /// Region tags this layer can run in. Empty = any. E.g. restrooms: {"green","paved"};
        /// park: {} (anywhere). Used by the UI to offer only sensible add-ons/children.
        /// </summary>
        string[] Accepts { get; }
        /// <summary>Region tags this layer emits for children (so the UI can offer "then fill blocks with…").</summary>
        string[] Emits { get; }
        /// <summary>Order of generator schema/receipt format; bump when settings meaning changes.</summary>
        int Version { get; }

        IReadOnlyList<ControlSchema> Controls { get; }
        IReadOnlyList<PresetInfo> Presets { get; }
        SettingsBag Defaults();

        /// <summary>Minimum/maximum area this generator accepts (m²).</summary>
        float MinArea { get; }
        float MaxArea { get; }

        /// <summary>Cheap check run every frame on the main thread.</summary>
        Readiness CheckReady(AreaInput input);

        /// <summary>
        /// Suggest markers (e.g. parking entrances on adjacent roads) when the user has placed none.
        /// May return an empty list. Must be fast (&lt;5 ms).
        /// </summary>
        List<EdgeMarker> SuggestMarkers(AreaInput input);

        /// <summary>
        /// Randomize settings within control ranges, never overriding ids listed in <paramref name="locked"/>
        /// (choices the user made explicitly). Deterministic for a given seed.
        /// </summary>
        SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked);

        /// <summary>
        /// Plan the area. Pure: may run on a worker thread; must honour cancellation
        /// (check token between phases) and never throw for bad geometry — report issues instead.
        /// Deterministic for identical input.
        /// </summary>
        AreaPlan Plan(AreaInput input, System.Threading.CancellationToken cancel);
    }

    // ───────────────────────────── Output ─────────────────────────────

    /// <summary>
    /// Reference to a game asset. Id is the prefab name; the game catalog resolves it and
    /// falls back through Fallbacks, then to the role default. Empty Id = "automatic".
    /// </summary>
    public struct AssetRef
    {
        public string Id;
        public string[] Fallbacks;
        public AssetRef(string id, params string[] fallbacks) { Id = id ?? ""; Fallbacks = fallbacks ?? Array.Empty<string>(); }
        public override string ToString() => Id;
    }

    public enum SurfaceRole { Ground = 0, Paving = 1, Asphalt = 2, Grass = 3, Plaza = 4, Water = 5, Decorative = 6 }

    public sealed class PlannedSurface
    {
        /// <summary>CCW ring, simple, no holes, already verified to survive CS2 triangulation (see SurfaceAcceptance).</summary>
        public float3[] Ring = Array.Empty<float3>();
        public SurfaceRole Role;
        public AssetRef Asset;
        /// <summary>(s16) Asset control (ControlSchema.Id) that chose this asset, or null: lets the panel say what "Automatic" became.</summary>
        public string Control;
        /// <summary>Asset category for automatic resolution, or null/"" = the role's default (RoleCategories.ForSurface).</summary>
        public string Category;
    }

    public enum NetRole
    {
        Street = 0, Avenue = 1, CulDeSac = 2, Roundabout = 3, PerimeterRoad = 4,
        Pathway = 10, PathwayWide = 11,
        DriveAisle = 20, DriveConnector = 21, Alley = 22,
        Fence = 30,
    }

    /// <summary>One net segment, emitted as a NetCourse. Curve is a cubic Bézier (straight = colinear controls).</summary>
    public sealed class PlannedNet
    {
        public float3 A, B, C, D;
        public NetRole Role;
        public AssetRef Asset;
        /// <summary>(s16) Asset control (ControlSchema.Id) that chose this asset, or null: lets the panel say what "Automatic" became.</summary>
        public string Control;
        /// <summary>Connect this end to an existing net (BoundaryAttachment/NearbyNet ExternalId) instead of a free node.</summary>
        public long StartAttach = -1, EndAttach = -1;
        public float StartAttachT, EndAttachT;
        /// <summary>Generic composition upgrades (trees, grass, bike lanes…) by game flag name.</summary>
        public string[] Upgrades = Array.Empty<string>();
        /// <summary>Elevation behaviour: true = follow terrain exactly (split where needed).</summary>
        public bool FollowTerrain = true;
        /// <summary>
        /// (g5) Asset category for automatic resolution, or null/"" = the role's default (RoleCategories.ForNet),
        /// e.g. "net.bike" / "net.trail" for park path tiers. An empty category falls back via CategoryFallbacks.
        /// </summary>
        public string Category;
    }

    public enum ObjectRole
    {
        Tree = 0, Bush = 1, Planter = 2,
        Bench = 10, Lamp = 11, Bin = 12, Centerpiece = 13, Furniture = 14, FencePiece = 15,
        /// <summary>Flat decal prop (leaves, cracks, markings…): never reserves space, may lie on paths.</summary>
        Decal = 16,
        ParkingBay = 20, Charger = 21, Arrow = 22,
        Marker = 30, AnimalSpawner = 31, AccessPoint = 32, CulDeSacCap = 33, RoundaboutIsland = 34,
    }

    public sealed class PlannedObject
    {
        public float3 Position;
        /// <summary>Yaw in radians around +Y (0 = facing +Z).</summary>
        public float Yaw;
        public ObjectRole Role;
        public AssetRef Asset;
        /// <summary>(s16) Asset control (ControlSchema.Id) that chose this asset, or null: lets the panel say what "Automatic" became.</summary>
        public string Control;
        public float Scale = 1f;
        /// <summary>
        /// (Industrial) Height above the ground in metres for a prop standing on another (a container on a container): the
        /// build raises it this high and gives it the game's object Elevation. 0 = on the ground. Negative: set this deep into
        /// the ground (a boulder half buried, rubble strewn in the earth), with the game's negative Elevation: it then collides
        /// with nothing (Underground mask), so it may lie under other props.
        /// </summary>
        public float Elevation;
        /// <summary>(Industrial) Tilt in radians: Roll about the prop's own length (π = on its roof, ±π/2 = on its side), Pitch
        /// about its width. 0 = upright (every other type). The build turns the prop by yaw, then pitch, then roll.</summary>
        public float Roll, Pitch;
        /// <summary>(Industrial) The game's random seed for this prop (its colour variation) when >= 0: boxes of one row share a
        /// couple of seeds so a row reads as one shipping line's boxes. -1 = the build's own per-object seed.</summary>
        public int ColorSeed = -1;
        /// <summary>Tree growth stage 0..3 (young..old), 4 = dead (PlannedAge.Dead), -1 = n/a.</summary>
        public int Age = -1;
        public uint Variation;
        /// <summary>Optional: which planned net this object belongs to (index into AreaPlan.Nets), -1 none.</summary>
        public int AttachedNet = -1;
        /// <summary>
        /// Asset category (AssetCategories), or null/"" = the role's default (RoleCategories.ForObject).
        /// Generic roles such as Furniture MUST set it (playground equipment, picnic tables, restrooms…);
        /// otherwise automatic resolution falls back to "decoration" (FEEDBACK-2 #1: sandwich boards).
        /// </summary>
        public string Category;
    }

    /// <summary>Tree growth stages for PlannedObject.Age (the build maps them to ObjectDefinition.m_Age, ObjectUtils.InitializeTreeState).</summary>
    public static class PlannedAge
    {
        public const int Young = 0, Teen = 1, Adult = 2, Old = 3;
        /// <summary>A dead tree (m_Age ≥ 0.95: TreeState.Dead, bare branches).</summary>
        public const int Dead = 4;
    }

    public enum AreaRole { PedestrianArea = 0, Hangaround = 1, Lot = 2 }

    /// <summary>Non-surface areas (navigation/space/lot polygons).</summary>
    public sealed class PlannedArea
    {
        public float3[] Ring = Array.Empty<float3>();
        public AreaRole Role;
        public AssetRef Asset;
        /// <summary>(s16) Asset control (ControlSchema.Id) that chose this asset, or null: lets the panel say what "Automatic" became.</summary>
        public string Control;
    }

    public enum TerraformKind { Level = 0, Lower = 1, Raise = 2 }

    public sealed class PlannedTerraform
    {
        public float3[] Ring = Array.Empty<float3>();   // footprint
        public TerraformKind Kind;
        public float TargetHeight;
        /// <summary>True = permanent change the player must be warned about (e.g. lake basin).</summary>
        public bool Permanent;
        /// <summary>Optional water source after terraform (lake). NaN = none.</summary>
        public float WaterLevel = float.NaN;
        public float3 WaterAnchor;
        /// <summary>
        /// (s20, appended) A sloped patch: the target ground is the surface through the ring's vertex heights
        /// (Ring[i].y, mean-value interpolation — bilinear-like on a quad, exact on a triangle, linear along every
        /// edge), not one flat level. Sculpted terrain (Common/Sculpt) emits a grid of sloped quads whose shared
        /// corners carry the same height, so the result is continuous: no terraced steps. TargetHeight stays the
        /// centre height for legacy readers. See Core/Build/TerraformSurface.
        /// </summary>
        public bool Sloped;
        /// <summary>(s58, not persisted) The strip between a lot edge and the kerb of the street its connector joins: fixed ground for the host's own grading.</summary>
        public bool Apron;
    }

    /// <summary>
    /// (gamebuild round 4, proposal) Terrain texture painting: CS2's terrain material brush paints one of the
    /// map's four extra terrain layers (TerrainMaterialType Extra1..Extra4 — on vanilla maps Dry grass, Lush
    /// grass, Dirt, Sand; see <see cref="TerrainMaterials"/>) into the user splatmap, which is
    /// saved with the city. Layer 0 erases back to the procedural grass/dirt/rock. Removing the group wipes its
    /// paint over its outline (Game/Build/BuildService.Unpaint); loose builds keep it, like terraform.
    /// </summary>
    public sealed class PlannedPaint
    {
        public float3[] Ring = Array.Empty<float3>();   // footprint
        /// <summary>1..4 = Extra1..Extra4, 0 = erase. Vanilla maps: see <see cref="TerrainMaterials"/> (1 Dry, 2 Lush, 3 Dirt, 4 Sand).</summary>
        public int Layer = 1;
        /// <summary>Brush strength 0..1 (1 = fully painted).</summary>
        public float Strength = 1f;

        // ── s13: texture fields (all optional; defaults = the round-4 behaviour) ──
        // The game paints a per-texel strength mask (Core/Build/PaintRaster), so a paint is a FIELD, not a flat
        // polygon: strength(p) = Strength × edge(p) × gradient(p). See RESEARCH-NOTES-ADDENDUM "Terrain texture painting".

        /// <summary>Soft edge inside the ring (m): 0 at the ring, full strength this far inside (smoothstep). 0 = legacy ±1.5 m edge on the ring.</summary>
        public float Feather;
        /// <summary>Edge noise (m): value noise pulls the feathered edge (and shifts the gradient) by up to 2× this, so edges read natural. Never paints outside the ring.</summary>
        public float Noise;
        /// <summary>Noise feature size (m); 0 = 8 m.</summary>
        public float NoiseScale;
        public uint Seed;
        /// <summary>
        /// Optional gradient source (xz polyline, e.g. the waterline): full strength within <see cref="GradientFull"/> m of it,
        /// then fading over <see cref="GradientFade"/> m to <see cref="GradientMin"/> × Strength. Null = uniform.
        /// </summary>
        public float2[] GradientFrom;
        public float GradientFull;
        public float GradientFade;
        public float GradientMin;

        // ── s33: paint toolkit (Core/Paint; docs/TERRAIN-PAINT.md). All optional; not persisted. ──

        /// <summary>
        /// (s33) Strength field of this paint: strength(p) = Strength × Shape(p). When set, Feather/Noise/Gradient are ignored
        /// (the shape carries them) and <see cref="Ring"/> is only the support outline (bounds, permanent-area estimate,
        /// cutting). Immutable and thread-safe (see Core/Paint/PaintShape).
        /// </summary>
        public Paint.PaintShape Shape;
        /// <summary>
        /// (s33) Erase instead of paint: lowers this material's weight to at most 1 − strength (Layer 1..4: that material only,
        /// CS2's EraseDither with the material's channel; Layer 0 always clears every material). Erases run before paints.
        /// </summary>
        public bool Erase;
        /// <summary>
        /// (s33) Where this paint is strong, OTHER materials of the plan are weakened (other × (1 − this)): a dirt path
        /// through a lush lawn tint stays dirt instead of a muddy mix. The splatmap keeps one 4-bit weight per material.
        /// </summary>
        public bool Knockout;
        /// <summary>(s33) Area (m²) a shape paint actually covers (Core/Paint/PaintKit probes it); 0 = use the ring's area.</summary>
        public float AreaEstimate;

        /// <summary>(s33) Copy with another outline (cutting); the shape, flags and field settings are kept.</summary>
        public PlannedPaint WithRing(float3[] ring)
        {
            var c = (PlannedPaint)MemberwiseClone();
            c.Ring = ring ?? Array.Empty<float3>();
            return c;
        }
    }

    /// <summary>
    /// The terrain texture layers of CS2's "Paint texture" tool (TerraformingPrefabs "Terrain Material Extra 1..4" and
    /// "Terrain Material Clear"). The four textures come from the map's TerrainRenderSettingsPrefab; on vanilla maps
    /// (DefaultTerrainRenderSettings, s33 census) they are 1 Dry grass (grey-olive), 2 "Lush" = a dark FOREST FLOOR
    /// (darker than the base grass: mulch, woodland, shade), 3 light Dirt (grey-brown, like the procedural base dirt) and
    /// 4 Sand (grey-beige temperate sand, not yellow). There is no rock material: rock is procedural (slope). Custom maps
    /// may put other textures in the slots: plans write these vanilla intents and Paint.TerrainPalette.Remap sends them to
    /// the matching slot of the current map. See docs/TERRAIN-PAINT.md.
    /// </summary>
    public static class TerrainMaterials
    {
        public const int Erase = 0;
        public const int Dry = 1;
        public const int Lush = 2;
        public const int Dirt = 3;
        public const int Sand = 4;
    }

    public sealed class PlannedZoning
    {
        public float3 A, B, C, D;   // zoning quad
        public string ZoneId = "";
    }

    public enum IssueSeverity { Info = 0, Warning = 1, Error = 2 }

    public sealed class PlanIssue
    {
        public IssueSeverity Severity;
        public string Key = "";
        public object[] Args = Array.Empty<object>();
        /// <summary>Optional location to mark on the map.</summary>
        public float3? At;
        /// <summary>(s61) The setting (control id of the issue's layer) that made this happen, "" = none: the UI names it and jumps to it.</summary>
        public string Cause = "";
        /// <summary>(s61) That setting's value as the player sees it ("120 m", "On"); shown beside its name.</summary>
        public string CauseValue = "";
        /// <summary>(s61) Something was left out or blocked (red marker); false = a warning (orange) or a note.</summary>
        public bool Dropped;
    }

    /// <summary>One line in the summary card, e.g. ("Placemaker.Stat.Stalls", 312, "count").</summary>
    public struct PlanStat
    {
        public string Key;
        public double Value;
        public string Unit;
        public PlanStat(string key, double value, string unit) { Key = key; Value = value; Unit = unit; }
    }

    /// <summary>A sub-polygon a layer hands to its children (blocks between roads, lawns between paths…).</summary>
    public sealed class PlannedRegion
    {
        public float3[] Ring = Array.Empty<float3>();
        public string[] Tags = Array.Empty<string>();
        /// <summary>Stable key within the parent plan (e.g. "block:3") so per-region overrides survive re-plans when possible.</summary>
        public string Key = "";
    }

    public sealed class AreaPlan
    {
        public string GeneratorId = "";
        /// <summary>Path of the recipe step that produced this (set by the composer), e.g. "0/2".</summary>
        public string StepPath = "";
        /// <summary>Seed the step was planned with (set by the composer). Automatic assets pick one palette per step from it.</summary>
        public uint Seed;
        public List<PlannedRegion> Regions = new List<PlannedRegion>();
        public List<PlannedSurface> Surfaces = new List<PlannedSurface>();
        public List<PlannedNet> Nets = new List<PlannedNet>();
        public List<PlannedObject> Objects = new List<PlannedObject>();
        public List<PlannedArea> Areas = new List<PlannedArea>();
        public List<PlannedTerraform> Terraform = new List<PlannedTerraform>();
        /// <summary>(gamebuild round 4) Terrain texture painting, applied right after terraform.</summary>
        public List<PlannedPaint> Paint = new List<PlannedPaint>();
        public List<PlannedZoning> Zoning = new List<PlannedZoning>();
        /// <summary>(dynamic decals) Grime strokes the game draws from recipes (Core/Decals/Dynamic, docs/DYNAMIC-DECALS.md): never entities, saved on the group record.</summary>
        public List<Decals.Dynamic.DecalStroke> Decals = new List<Decals.Dynamic.DecalStroke>();
        /// <summary>(dynamic decals) Kinds this plan defines itself (a builder's own look, DecalLibrary.ParseKind): saved with the strokes.</summary>
        public List<Decals.Dynamic.DecalKind> DecalKinds = new List<Decals.Dynamic.DecalKind>();
        /// <summary>(dynamic decals v2) Instances of the shared decal library (Brushes, GroundDesign): saved on the group record, drawn by SharedDecals.</summary>
        public List<Decals.Dynamic.DecalInstance> DecalInstances = new List<Decals.Dynamic.DecalInstance>();
        public List<PlanStat> Stats = new List<PlanStat>();
        public List<PlanIssue> Issues = new List<PlanIssue>();
        /// <summary>(Phase 1) Setting ids the layer read while planning (SettingsBag.Reads); null = not recorded.</summary>
        public HashSet<string> SettingsRead;
        /// <summary>Generator-specific facts the game layer needs (e.g. parking capacity for the economy companion).</summary>
        public Dictionary<string, double> Facts = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>
        /// (g5, optional) Ground height after this plan's terraform. The composer passes it to the
        /// children's SiteContext.HeightAt so features plan on the reshaped ground. Null = derived from
        /// <see cref="Terraform"/> (each polygon levelled to its TargetHeight), else the original ground.
        /// Not persisted.
        /// </summary>
        public Func<float2, float> HeightAfter;

        /// <summary>(zoning speed) Where this layer's planning time went: ms per step, in order (PlanClock). For the log; not persisted.</summary>
        public List<KeyValuePair<string, double>> Timings = new List<KeyValuePair<string, double>>();

        public bool HasErrors
        {
            get { foreach (var i in Issues) if (i.Severity == IssueSeverity.Error) return true; return false; }
        }
    }
}
