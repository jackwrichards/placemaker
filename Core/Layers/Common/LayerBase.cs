// Shared plumbing for park/plaza/split/add-on layers so each concrete layer only declares its
// controls, presets and planning logic. Adding a new layer type = subclass + [AreaGenerator] +
// Lang keys; nothing here needs to change.
//
// Localisation key convention (checked by tests):
//   layer name      Placemaker.<Area>.Name
//   control label   Placemaker.<Area>.<controlId>
//   control tooltip Placemaker.<Area>.<controlId>.Tooltip   (only when declared with .Tip())
//   option label    Placemaker.<Area>.<controlId>.<optionValue>
//   preset label    Placemaker.<Area>.Preset.<presetId>
//   issues          Placemaker.<Area>.Issue.<Name>  /  shared ones in Keys
//   stats           Placemaker.Stat.<Name> (shared so the composer can merge them)

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Placemaker.Core.Layers.Common
{
    public static class Keys
    {
        public const string NoAssetsIssue = "Placemaker.Common.Issue.NoAssets";
        public const string InvalidArea = "Placemaker.Common.Issue.InvalidArea";
        public const string AreaTooSmall = "Placemaker.Common.Issue.AreaTooSmall";
        public const string AreaTooLarge = "Placemaker.Common.Issue.AreaTooLarge";
        public const string SurfaceDropped = "Placemaker.Common.Issue.SurfaceDropped";
        public const string NoSpace = "Placemaker.Common.Issue.NoSpace";

        public const string StatArea = "Placemaker.Stat.Area";
        public const string StatPathLength = "Placemaker.Stat.PathLength";
        public const string StatTrees = "Placemaker.Stat.Trees";
        public const string StatBushes = "Placemaker.Stat.Bushes";
        public const string StatBenches = "Placemaker.Stat.Benches";
        public const string StatLamps = "Placemaker.Stat.Lamps";
        public const string StatBins = "Placemaker.Stat.Bins";
        public const string StatFountains = "Placemaker.Stat.Fountains";
        public const string StatPlayItems = "Placemaker.Stat.PlayItems";
        public const string StatRestrooms = "Placemaker.Stat.Restrooms";
        public const string StatPicnicTables = "Placemaker.Stat.PicnicTables";
        public const string StatFlowerbeds = "Placemaker.Stat.Flowerbeds";
        public const string StatPitches = "Placemaker.Stat.Pitches";
        public const string StatParts = "Placemaker.Stat.Parts";
        public const string StatLakeArea = "Placemaker.Stat.LakeArea";

        /// <summary>Category label keys used as issue arguments ("no {0} installed").</summary>
        public static string Category(string category) => "Placemaker.Category." + category;

        public const string UnitM2 = "m2";
        public const string UnitM = "m";
        public const string UnitCount = "count";
    }

    /// <summary>Fluent builder for a layer's control schema and randomisation ranges.</summary>
    public sealed class ControlSet
    {
        private readonly string _area;
        public readonly List<ControlSchema> List = new List<ControlSchema>();
        internal readonly Dictionary<string, double2> RandomRanges = new Dictionary<string, double2>(StringComparer.Ordinal);
        internal readonly HashSet<string> NoRandom = new HashSet<string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, float> ToggleChance = new Dictionary<string, float>(StringComparer.Ordinal);

        public ControlSet(string area) { _area = area; }

        public string Key(string id) => "Placemaker." + _area + "." + id;

        private ControlSchema Add(string id, ControlKind kind, ControlTier tier, string group, object def)
        {
            var c = new ControlSchema { Id = id, Kind = kind, Tier = tier, Group = group, LabelKey = Key(id), Default = def };
            List.Add(c);
            return c;
        }

        public ControlSchema Slider(string id, double def, double min, double max, double step, string unit,
            ControlTier tier = ControlTier.Essential, string group = "")
        {
            var c = Add(id, ControlKind.Slider, tier, group, def);
            c.Min = min; c.Max = max; c.Step = step; c.Unit = unit;
            return c;
        }

        public ControlSchema Toggle(string id, bool def, ControlTier tier = ControlTier.More, string group = "") =>
            Add(id, ControlKind.Toggle, tier, group, def);

        public ControlSchema Choice(string id, string def, ControlTier tier, string group, params string[] values)
        {
            var c = Add(id, ControlKind.Choice, tier, group, def);
            foreach (var v in values) c.Options.Add(new ControlOption { Value = v, LabelKey = Key(id) + "." + v });
            return c;
        }

        public ControlSchema Asset(string id, string category, ControlTier tier = ControlTier.More, string group = "")
        {
            var c = Add(id, ControlKind.Asset, tier, group, "");
            c.AssetCategory = category;
            NoRandom.Add(id);
            return c;
        }

        public ControlSchema AssetMulti(string id, string category, ControlTier tier = ControlTier.More, string group = "")
        {
            var c = Add(id, ControlKind.AssetMulti, tier, group, Array.Empty<string>());
            c.AssetCategory = category;
            NoRandom.Add(id);
            return c;
        }

        public ControlSchema Markers(string id, ControlTier tier, params string[] kinds)
        {
            var c = Add(id, ControlKind.Markers, tier, "entrances", null);
            c.MarkerKinds = kinds;
            NoRandom.Add(id);
            return c;
        }

        public ControlSchema Arrangement(string id, string category, string[] def, ControlTier tier = ControlTier.More, string group = "")
        {
            var c = Add(id, ControlKind.Arrangement, tier, group, def);
            c.AssetCategory = category;
            return c;
        }

        /// <summary>Restricts Randomize for a slider to a sub-range (keeps rolls tasteful).</summary>
        public void RandomRange(string id, double min, double max) => RandomRanges[id] = new double2(min, max);
        public void Fixed(string id) => NoRandom.Add(id);
        public void Chance(string id, float probabilityTrue) => ToggleChance[id] = probabilityTrue;
    }

    public static class ControlSchemaExtensions
    {
        public static ControlSchema Tip(this ControlSchema c) { c.TooltipKey = c.LabelKey + ".Tooltip"; return c; }
        /// <summary>Puts the control in a section (heading "Placemaker.UI.Group.{group}").</summary>
        public static ControlSchema In(this ControlSchema c, string group) { c.Group = group ?? ""; return c; }
        public static ControlSchema When(this ControlSchema c, string condition) { c.VisibleWhen = condition; return c; }
        public static ControlSchema Cheap(this ControlSchema c) { c.Cheap = true; return c; }
        /// <summary>A layout distance: read scaled by the layout size, never below <paramref name="realMin"/> metres.</summary>
        public static ControlSchema Layout(this ControlSchema c, double realMin = 0) { c.Layout = true; c.LayoutMin = realMin; return c; }
        public static ControlSchema WithIcon(this ControlSchema c, string icon) { c.Icon = icon; return c; }
    }

    /// <summary>Everything a layer's planning step needs, with settings already merged over defaults.</summary>
    public sealed class LayerContext
    {
        public AreaInput Input;
        public LayerRegion LayerRegion;
        public AreaPlan Plan;
        public SettingsBag Settings;
        public CancellationToken Cancel;
        public uint Seed;

        public SiteContext Site => Input.Site;
        /// <summary>Ground BEFORE this layer's own terraform (for a feature: its parent's reshaped ground).</summary>
        public float H(float2 p) => Site?.HeightAt != null ? Site.HeightAt(p) : 0f;

        private TerrainField _ground;
        /// <summary>
        /// Ground AFTER this layer's own terraform. TerrainShaper registers what it shapes; layers with
        /// their own sculptors may register too (Ground.Override). Published to the children as
        /// AreaPlan.HeightAfter when anything was overridden.
        /// </summary>
        public TerrainField Ground => _ground ?? (_ground = new TerrainField(H));
        public bool Reshaped => _ground != null && _ground.HasOverrides;
        /// <summary>Planned ground height: the reshaped ground where this layer terraformed, else H.</summary>
        public float G(float2 p) => Reshaped ? _ground.HeightAt(p) : H(p);
        /// <summary>A point on the planned ground (objects, nets, surfaces and regions use it).</summary>
        public float3 P3(float2 p) => new float3(p.x, G(p), p.y);

        /// <summary>The layer's control schema lookup (set by LayerBase): F scales controls declared .Layout().</summary>
        public Func<string, ControlSchema> ControlOf;

        /// <summary>Layout size (SiteContext.LayoutScale, clamped): multiplies layout distances, never prop sizes.</summary>
        public float Scale => Site?.Scale ?? 1f;
        /// <summary>Design metres → real metres at this plan's layout size.</summary>
        public float M(float designMetres) => designMetres * Scale;
        /// <summary>Design metres → real metres, never below a real-size floor (a footprint, a regulation size).</summary>
        public float M(float designMetres, float realMin) => math.max(realMin, designMetres * Scale);
        /// <summary>The one edge margin between the outline and the layout (real metres, = SiteContext.EdgeMargin × scale).</summary>
        public float EdgeMargin => math.max(0f, Site?.EdgeMargin ?? SiteContext.DefaultEdgeMargin) * Scale;

        /// <summary>Slider value; controls declared .Layout() come back multiplied by the layout scale (floored at LayoutMin).</summary>
        public float F(string id)
        {
            float v = (float)Settings.Num(id, 0);
            var c = ControlOf?.Invoke(id);
            if (c == null || !c.Layout) return v;
            return v <= 0f ? v : math.max((float)c.LayoutMin, v * Scale);
        }

        /// <summary>Slider value as set, never scaled.</summary>
        public float Raw(string id) => (float)Settings.Num(id, 0);
        public int I(string id) => Settings.I(id, 0);
        public bool B(string id) => Settings.B(id, false);
        public string S(string id) => Settings.S(id, "");
        public string[] L(string id) => Settings.L(id);

        public Random Rng(string salt) => Hashing.Rng(Seed, salt);
        public void Check() => Cancel.ThrowIfCancellationRequested();

        public void Issue(IssueSeverity severity, string key, params object[] args) =>
            Plan.Issues.Add(new PlanIssue { Severity = severity, Key = key, Args = args ?? Array.Empty<object>() });

        public void Stat(string key, double value, string unit) => Plan.Stats.Add(new PlanStat(key, value, unit));

        /// <param name="category">Asset category (AssetCategories) when the role does not imply it — required for ObjectRole.Furniture.</param>
        public PlannedObject Object(float2 p, float yaw, ObjectRole role, AssetRef asset, uint variation, int age = -1, string category = null)
        {
            var o = new PlannedObject { Position = P3(p), Yaw = yaw, Role = role, Asset = asset, Variation = variation, Age = age, Category = category };
            Plan.Objects.Add(o);
            return o;
        }

        /// <summary>Straight net segment (Bézier controls at thirds).</summary>
        public PlannedNet StraightNet(float2 a, float2 b, NetRole role, AssetRef asset)
        {
            var n = new PlannedNet
            {
                A = P3(a), B = P3(math.lerp(a, b, 1f / 3f)), C = P3(math.lerp(a, b, 2f / 3f)), D = P3(b),
                Role = role, Asset = asset,
            };
            Plan.Nets.Add(n);
            return n;
        }

        public PlannedNet CurvedNet(float2 a, float2 b, float2 c, float2 d, NetRole role, AssetRef asset)
        {
            var n = new PlannedNet { A = P3(a), B = P3(b), C = P3(c), D = P3(d), Role = role, Asset = asset };
            Plan.Nets.Add(n);
            return n;
        }

        /// <summary>
        /// Emits a surface only in a form CS2 keeps (SurfaceAcceptance). Returns false and adds a
        /// warning when the ring cannot be made acceptable — never emits something the game drops silently.
        /// </summary>
        public bool Surface(IReadOnlyList<float2> ring, SurfaceRole role, AssetRef asset, string category = null)
        {
            var pieces = SurfaceAcceptance.MakeAcceptable(ring, G);
            int added = 0;
            foreach (var r in pieces)
            {
                if (r == null || r.Length < 3 || !SurfaceAcceptance.IsAccepted(Poly.To2D(r))) continue;
                Plan.Surfaces.Add(new PlannedSurface { Ring = r, Role = role, Asset = asset, Category = category });
                added++;
            }
            if (added == 0) Issue(IssueSeverity.Warning, Keys.SurfaceDropped);
            return added > 0;
        }

        public bool NavigationArea(IReadOnlyList<float2> ring, AreaRole role, AssetRef asset)
        {
            var pieces = SurfaceAcceptance.MakeAcceptable(ring, G);
            int added = 0;
            foreach (var r in pieces)
            {
                if (r == null || r.Length < 3 || !SurfaceAcceptance.IsAccepted(Poly.To2D(r))) continue;
                Plan.Areas.Add(new PlannedArea { Ring = r, Role = role, Asset = asset });
                added++;
            }
            if (added == 0) Issue(IssueSeverity.Warning, Keys.SurfaceDropped);
            return added > 0;
        }

        public void AddRegion(IReadOnlyList<float2> ring, string key, params string[] tags)
        {
            var clean = Poly.Clean(ring, 0.05f);
            if (clean.Length < 3) return;
            Plan.Regions.Add(new PlannedRegion { Ring = Poly.To3D(clean, G), Key = key, Tags = tags });
        }
    }

    public abstract class LayerBase : IAreaGenerator
    {
        private readonly List<ControlSchema> _controls;
        private readonly List<PresetInfo> _presets = new List<PresetInfo>();
        private readonly ControlSet _set;

        protected LayerBase()
        {
            _set = new ControlSet(Area);
            DefineControls(_set);
            AddDressingControls(_set);
            _controls = _set.List;
            DefinePresets();
        }

        /// <summary>Localisation area ("Park", "Plaza", "AddOn.Fountain"…).</summary>
        protected abstract string Area { get; }
        public abstract string Id { get; }
        public virtual string NameKey => "Placemaker." + Area + ".Name";
        public abstract string Icon { get; }
        public abstract LayerKind Kind { get; }
        public virtual string[] Accepts => Array.Empty<string>();
        public virtual string[] Emits => Array.Empty<string>();
        public virtual int Version => 1;
        public virtual float MinArea => 50f;
        public virtual float MaxArea => 4_000_000f;

        public IReadOnlyList<ControlSchema> Controls => _controls;
        public IReadOnlyList<PresetInfo> Presets => _presets;

        protected abstract void DefineControls(ControlSet c);

        /// <summary>
        /// (s21) Decal dressing controls for fill types with a dressing profile: Decals amount + Style in the "decals"
        /// section, one amount per dressing family in the section of what it complements (Core/Decals/DressingProfiles).
        /// </summary>
        private void AddDressingControls(ControlSet c)
        {
            if (Kind != LayerKind.Fill) return;
            if (!c.List.Exists(x => x.Id == Decals.DressingControls.GroundDecalsId)) c.List.Add(Decals.DressingControls.GroundDecals());
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var x in c.List) if (!string.IsNullOrEmpty(x.Group)) groups.Add(x.Group);
            foreach (var d in Decals.DressingControls.For(Id, groups.Contains))
            {
                if (c.List.Exists(x => x.Id == d.Id)) continue;
                c.List.Add(d);
                c.Fixed(d.Id);
            }
        }
        protected virtual void DefinePresets() { }

        /// <summary>Declares a preset; values not given keep the layer defaults.</summary>
        protected void Preset(string id, params (string key, object value)[] values)
        {
            var p = new PresetInfo { Id = id, LabelKey = "Placemaker." + Area + ".Preset." + id, Values = Defaults() };
            // s21: a preset promises a look, decals included (an abandoned lot is gritty, a formal garden clean).
            if (Control(Decals.DressingControls.StyleId) != null)
                p.Values.Values[Decals.DressingControls.StyleId] = Decals.DressingControls.Styles[(int)Decals.DressingProfiles.StyleFor(Id, id)];
            // s33: and its lawn (a formal park keeps the grass surface, a natural one paints the bare terrain).
            if (Control(Decals.DressingControls.LawnId) != null && Lawn.DefaultFor(Id, id) is string lawn)
                p.Values.Values[Decals.DressingControls.LawnId] = lawn;
            foreach (var (k, v) in values) p.Values.Values[k] = v;
            _presets.Add(p);
        }

        /// <summary>Feature layers preset <paramref name="id"/> turns on ("-layer" = off) on top of the DefaultOn ones.</summary>
        protected void PresetFeatures(string id, params string[] features)
        {
            foreach (var p in _presets)
                if (p.Id == id)
                {
                    var list = new List<string>(p.Features);
                    list.AddRange(features);
                    p.Features = list.ToArray();
                }
        }

        /// <summary>(rf-parks) Settings preset <paramref name="id"/> gives its <paramref name="feature"/> child (over FeatureInfo.Preset).</summary>
        protected void PresetFeatureValues(string id, string feature, params (string key, object value)[] values)
        {
            foreach (var p in _presets)
                if (p.Id == id)
                {
                    if (!p.FeatureValues.TryGetValue(feature, out var bag)) p.FeatureValues[feature] = bag = new SettingsBag();
                    foreach (var (k, v) in values) bag.Values[k] = v;
                }
        }

        private Dictionary<string, ControlSchema> _byId;

        public ControlSchema Control(string id)
        {
            if (id == null) return null;
            var map = _byId;
            if (map == null)
            {
                map = new Dictionary<string, ControlSchema>(StringComparer.Ordinal);
                foreach (var c in _controls) if (!map.ContainsKey(c.Id)) map[c.Id] = c;
                _byId = map;
            }
            return map.TryGetValue(id, out var r) ? r : null;
        }

        public SettingsBag Defaults()
        {
            var b = new SettingsBag();
            foreach (var c in _controls)
            {
                if (c.Kind == ControlKind.Markers || c.Default == null) continue;
                b.Values[c.Id] = c.Default is string[] a ? (string[])a.Clone() : c.Default;
            }
            return b;
        }

        /// <summary>Defaults overlaid with the given values (unknown ids are kept, wrong types fall back).</summary>
        public SettingsBag Merge(SettingsBag values)
        {
            var b = Defaults();
            if (values == null) return b;
            foreach (var kv0 in values.Values)
            {
                var kv = new KeyValuePair<string, object>(kv0.Key, MigrateValue(kv0.Key, kv0.Value));
                var c = Control(kv.Key);
                if (c == null) { b.Values[kv.Key] = kv.Value; continue; }
                if (TypeMatches(c, kv.Value)) b.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
            }
            return b;
        }

        /// <summary>
        /// Saved values whose option was retired map to their successor (settings stability: ids never change, old
        /// saves load). s14: the Zoning feature's "shopLayout" kept only Row/Speckled; s12's clusters, facing in and
        /// alley values plan as Row.
        /// </summary>
        public object Migrate(string id, object value) => MigrateValue(id, value);

        protected virtual object MigrateValue(string id, object value)
        {
            if (HostsStreetShops && id == StreetShops.LayoutId && value is string s) return StreetShops.FeatureLayout(s);
            return value;
        }

        private static bool TypeMatches(ControlSchema c, object v)
        {
            switch (c.Kind)
            {
                case ControlKind.Slider: return v is IConvertible && !(v is string) && !(v is bool);
                case ControlKind.Toggle: return v is bool;
                case ControlKind.Choice: return v is string s && c.Options.Exists(o => o.Value == s);
                case ControlKind.Asset: return v is string;
                case ControlKind.AssetMulti:
                case ControlKind.Arrangement: return v is string[];
                default: return false;
            }
        }

        public virtual Readiness CheckReady(AreaInput input) => new Readiness();

        public virtual List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();

        /// <summary>
        /// Rolls every unlocked, randomisable control from its own seeded stream (so locking one
        /// control never changes what the others roll). Locked ids are copied unchanged — the fix
        /// for ParkManager's "Variant overwrites explicit choices".
        /// </summary>
        public virtual SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked)
        {
            var result = Merge(current);
            foreach (var c in _controls)
            {
                if (locked != null && locked.Contains(c.Id)) continue;
                if (_set.NoRandom.Contains(c.Id)) continue;
                var rng = Hashing.Rng(seed, Id + "|" + c.Id);
                if (RandomizeControl(c, ref rng, result)) continue;
                switch (c.Kind)
                {
                    case ControlKind.Slider:
                    {
                        var range = _set.RandomRanges.TryGetValue(c.Id, out var r) ? r : new double2(c.Min, c.Max);
                        double step = c.Step > 0 ? c.Step : 1;
                        int steps = (int)Math.Floor((range.y - range.x) / step + 1e-9);
                        double v = range.x + step * rng.NextInt(0, steps + 1);
                        result.Values[c.Id] = Math.Min(c.Max, Math.Max(c.Min, Math.Round(v / step) * step));
                        break;
                    }
                    case ControlKind.Toggle:
                    {
                        float chance = _set.ToggleChance.TryGetValue(c.Id, out var p) ? p : 0.5f;
                        result.Values[c.Id] = rng.NextFloat() < chance;
                        break;
                    }
                    case ControlKind.Choice:
                        if (c.Options.Count > 0) result.Values[c.Id] = c.Options[rng.NextInt(0, c.Options.Count)].Value;
                        break;
                }
            }
            return result;
        }

        /// <summary>Layer-specific roll for one control. Return true when handled.</summary>
        protected virtual bool RandomizeControl(ControlSchema control, ref Random rng, SettingsBag into) => false;

        public AreaPlan Plan(AreaInput input, CancellationToken cancel)
        {
            var plan = new AreaPlan { GeneratorId = Id };
            if (input == null) { plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = Keys.InvalidArea }); return plan; }
            cancel.ThrowIfCancellationRequested();
            // A bad selected area blocks the build (Error). A bad or tiny sub-region (one lawn, one split
            // part) must not block the whole recipe: that layer just skips it with a Warning.
            var tags = input.RegionTags;
            var severity = tags == null || tags.Length == 0 || Array.IndexOf(tags, RegionTags.Root) >= 0 ? IssueSeverity.Error : IssueSeverity.Warning;
            var region = Common.LayerRegion.From(input.Shape?.Ring);
            if (region == null)
            {
                plan.Issues.Add(new PlanIssue { Severity = severity, Key = Keys.InvalidArea });
                return plan;
            }
            if (region.Area < MinArea)
            {
                plan.Issues.Add(new PlanIssue { Severity = severity, Key = Keys.AreaTooSmall, Args = new object[] { Math.Round(region.Area), MinArea } });
                return plan;
            }
            if (region.Area > MaxArea)
            {
                plan.Issues.Add(new PlanIssue { Severity = severity, Key = Keys.AreaTooLarge, Args = new object[] { Math.Round(region.Area), MaxArea } });
                return plan;
            }
            var ctx = new LayerContext
            {
                Input = input, LayerRegion = region, Plan = plan, Settings = Merge(input.Settings),
                Cancel = cancel, Seed = Hashing.Mix(input.Seed == 0 ? 1u : input.Seed, Seeds.Hash(Id)),
                ControlOf = Control,
            };
            float unplacedInset;
            using (Common.Layout.Use(input.Site))
            {
                var shops = HostsStreetShops && input.HasChild(StreetShopsLayer) && StreetShopsFit(ctx) ? CarveStreetShops(ctx, input) : null;
                PlanCore(ctx);
                if (shops != null) FinishStreetShops(ctx, shops);
                unplacedInset = Entrances.UnplacedInset;
            }
            // s8: ground before props — no plan may bury the site in thousands of props (ObjectBudget).
            ObjectBudget.Enforce(plan, region.Area, ObjectsPerHectare);
            // Only the selected area itself: inside a parent, ends on the outline may meet the parent's paths.
            if (tags == null || tags.Length == 0 || Array.IndexOf(tags, RegionTags.Root) >= 0)
            {
                // s9: no entrance placed yet (park family) → no connectors: paths stop inside the outline.
                // With entrances placed, a path meets the outline only at one of them: other ends on the
                // outline stop just inside the edge band.
                if (!CheckReady(input).Ready) TrimOutlineEnds(plan, region, unplacedInset);
                else if (!CheckReady(new AreaInput { Shape = input.Shape, Settings = input.Settings, Markers = new List<EdgeMarker>() }).Ready)
                {
                    var markers = new List<float2>();
                    foreach (var m in input.Markers) markers.Add(m.Position.xz);
                    TrimOutlineEnds(plan, region, PathQuality.Band + 0.5f, p =>
                    {
                        foreach (var m in markers) if (math.distancesq(m, p) < 25f) return true;
                        return false;
                    });
                }
                KeepPathEndsInside(plan, region);
            }
            // Children plan on the ground this layer leaves: the composer derives it from
            // plan.Terraform (every sculptor emits PlannedTerraform), unless the layer set HeightAfter.
            return plan;
        }

        /// <summary>
        /// (FEEDBACK-4 / gallery) A walk that ends on the outline without joining a road would stick out of the
        /// area by its half width (the rounded end cap). Pull such free ends back along the path so the cap
        /// stays inside; ends attached to a road keep their node on the road edge.
        /// </summary>
        public static void KeepPathEndsInside(AreaPlan plan, LayerRegion region)
        {
            if (plan?.Nets == null || region == null) return;
            foreach (var n in plan.Nets)
            {
                // (s20) Decks (quays, piers, boardwalks: explicit heights) are built at the water, which may lie past the outline.
                if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide || !n.FollowTerrain) continue;
                float hw = Composer.DefaultHalfWidth(n.Role);
                if (n.StartAttach < 0) n.A = PullIn(n.A, n.B, n.D, hw, region);
                if (n.EndAttach < 0) n.D = PullIn(n.D, n.C, n.A, hw, region);
            }
        }

        /// <summary>
        /// (s9) While no entrance is placed, no path may run out to the outline: every free path end on (or
        /// within 1 m of) the outline is cut back to <paramref name="inset"/> m inside; a path lying wholly in
        /// that band is dropped. Benches, lamps and bins left beside a removed stretch go with it.
        /// </summary>
        public static void TrimOutlineEnds(AreaPlan plan, LayerRegion region, float inset, Func<float2, bool> keepEnd = null)
        {
            if (plan?.Nets == null || region == null || inset <= 0f) return;
            const int samples = 48;
            var cut = new List<float2>();
            var removed = new List<int>();
            var trimmed = new List<(PlannedNet net, bool isA, float2 was)>();
            float Depth(float2 p) => region.Contains(p) ? region.BoundaryDistance(p, inset + 1f) : 0f;
            for (int i = 0; i < plan.Nets.Count; i++)
            {
                var n = plan.Nets[i];
                if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide || !n.FollowTerrain) continue;   // s20: decks stay
                bool atA = n.StartAttach < 0 && Depth(n.A.xz) <= 1f && (keepEnd == null || !keepEnd(n.A.xz));
                bool atD = n.EndAttach < 0 && Depth(n.D.xz) <= 1f && (keepEnd == null || !keepEnd(n.D.xz));
                if (!atA && !atD) continue;
                float tA = 0f, tD = 1f;
                if (atA)
                {
                    tA = -1f;
                    for (int k = 1; k <= samples; k++)
                        if (Depth(Composer.Bezier(n.A, n.B, n.C, n.D, k / (float)samples).xz) >= inset) { tA = k / (float)samples; break; }
                }
                if (atD)
                {
                    tD = -1f;
                    for (int k = samples - 1; k >= 0; k--)
                        if (Depth(Composer.Bezier(n.A, n.B, n.C, n.D, k / (float)samples).xz) >= inset) { tD = k / (float)samples; break; }
                }
                bool drop = tA < 0f || tD < 0f || tD - tA < 0.05f;
                for (int k = 0; k <= samples; k++)
                {
                    float t = k / (float)samples;
                    if (drop || t < tA || t > tD) cut.Add(Composer.Bezier(n.A, n.B, n.C, n.D, t).xz);
                }
                if (drop) { removed.Add(i); continue; }
                if (atD) trimmed.Add((n, false, n.D.xz));
                if (atA) trimmed.Add((n, true, n.A.xz));
                if (atD && tD < 1f) PathJoins.Split(n, tD);
                if (atA && tA > 0f)
                {
                    var rest = PathJoins.Split(n, tA / tD);
                    n.A = rest.A; n.B = rest.B; n.C = rest.C; n.D = rest.D;
                    n.StartAttach = -1; n.EndAttach = rest.EndAttach; n.EndAttachT = rest.EndAttachT;
                }
            }
            if (cut.Count == 0) return;
            // Ends left free by the cut (new ends, and the inner ends of dropped stubs) that lie within 3 m of
            // each other were one entrance junction: join them at one node so the paths still meet (and never
            // cross just short of it).
            var exposed = new List<float2>();
            foreach (var t in trimmed) exposed.Add(t.isA ? t.net.A.xz : t.net.D.xz);
            foreach (int r in removed) { exposed.Add(plan.Nets[r].A.xz); exposed.Add(plan.Nets[r].D.xz); }
            var ends = new List<(PlannedNet net, bool isA)>();
            for (int k = 0; k < plan.Nets.Count; k++)
            {
                var o = plan.Nets[k];
                if (removed.Contains(k) || (o.Role != NetRole.Pathway && o.Role != NetRole.PathwayWide)) continue;
                foreach (bool isA in new[] { true, false })
                {
                    var e = isA ? o.A.xz : o.D.xz;
                    if (Depth(e) <= 1f) continue;   // ends kept on the outline (entrances) never move
                    foreach (var x in exposed) if (math.distancesq(x, e) < 0.01f) { ends.Add((o, isA)); break; }
                }
            }
            var parent = new int[ends.Count];
            for (int k = 0; k < ends.Count; k++) parent[k] = k;
            int Find(int k) { while (parent[k] != k) k = parent[k] = parent[parent[k]]; return k; }
            float3 End((PlannedNet net, bool isA) e) => e.isA ? e.net.A : e.net.D;
            for (int a = 0; a < ends.Count; a++)
                for (int b = a + 1; b < ends.Count; b++)
                    if (math.distancesq(End(ends[a]).xz, End(ends[b]).xz) < 4f) parent[Find(a)] = Find(b);
            var sums = new Dictionary<int, (float3 sum, int count)>();
            for (int k = 0; k < ends.Count; k++)
            {
                int root = Find(k);
                sums.TryGetValue(root, out var s);
                sums[root] = (s.sum + End(ends[k]), s.count + 1);
            }
            for (int k = 0; k < ends.Count; k++)
            {
                var (sum, count) = sums[Find(k)];
                if (count < 2) continue;
                var c = sum / count;
                var (n, isA) = ends[k];
                if (isA) { var d = c - n.A; n.A = c; n.B += d; }
                else { var d = c - n.D; n.D = c; n.C += d; }
            }
            for (int r = removed.Count - 1; r >= 0; r--)
            {
                int idx = removed[r];
                plan.Nets.RemoveAt(idx);
                foreach (var o in plan.Objects)
                    if (o.AttachedNet == idx) o.AttachedNet = -1;
                    else if (o.AttachedNet > idx) o.AttachedNet--;
            }
            // Furniture that lined a removed stretch (and is not beside a remaining path).
            plan.Objects.RemoveAll(o =>
            {
                if (o.Role != ObjectRole.Bench && o.Role != ObjectRole.Lamp && o.Role != ObjectRole.Bin) return false;
                var p = o.Position.xz;
                bool nearCut = false;
                foreach (var c in cut) if (math.distancesq(c, p) < 16f) { nearCut = true; break; }
                if (!nearCut) return false;
                foreach (var n in plan.Nets)
                {
                    if (n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide) continue;
                    for (int k = 0; k <= 16; k++)
                        if (math.distancesq(Composer.Bezier(n.A, n.B, n.C, n.D, k / 16f).xz, p) < 12.25f) return false;
                }
                return true;
            });
        }

        private static float3 PullIn(float3 end, float3 control, float3 other, float hw, LayerRegion region)
        {
            var p = end.xz;
            float inside = region.Contains(p) ? region.BoundaryDistance(p, hw + 1f) : -region.BoundaryDistance(p, 50f);
            if (inside > 0.5f) return end;   // only ends ON (or beyond) the outline: entrances, edge-to-edge walks
            float need = hw - inside;
            if (need <= 0.05f) return end;
            var toward = math.lengthsq(control.xz - p) > 0.01f ? control.xz : other.xz;
            float len = math.distance(toward, p);
            if (len < need * 2f + 0.5f) return end;   // too short to shorten: leave it
            var q = p + (toward - p) / len * need;
            return new float3(q.x, end.y, q.y);
        }

        // ───────────── "Shops along the street" (edgeZoning feature on a "shops" target) ─────────────

        public const string StreetShopsLayer = "edgeZoning";
        public const string ShopsPlace = Composer.PlacePrefix + StreetFrontage.ShopsTag;

        /// <summary>
        /// True for types that offer "Shops along the street": when the edgeZoning feature is on, the
        /// layer plans on its outline minus a band of zone cells along the road frontage (gaps at the
        /// entrances) and hands each band piece to the feature as a "shops" region.
        /// </summary>
        protected virtual bool HostsStreetShops => false;

        /// <summary>
        /// (s18) Whether this area is big enough to give land to street shops at all (a small square would be eaten
        /// by them). Hosts that say no add their own Info issue.
        /// </summary>
        protected virtual bool StreetShopsFit(LayerContext ctx) => true;

        /// <summary>The host controls shown under the feature toggle (FeatureInfo.Options = ShopOptions).</summary>
        public static readonly string[] ShopOptions = { StreetShops.AmountId, StreetShops.LayoutId, StreetShops.DepthId };

        /// <summary>Shared label keys: the street-shop controls read the same on every host.</summary>
        private const string ShopKey = "Placemaker.Shops.";

        /// <summary>
        /// The street-shop controls (amount, layout, depth) with this type's defaults (StreetShops.Defaults).
        /// Ids are save format: "shopLayout" kept its old values "band" (Row) and "pads" (Speckled).
        /// </summary>
        protected void DefineStreetShopControls(ControlSet c)
        {
            var d = StreetShops.DefaultFor(Id);
            Shared(c.Choice(StreetShops.AmountId, d.amount, ControlTier.More, "shops", StreetShops.Amounts), false);
            Shared(c.Choice(StreetShops.LayoutId, StreetShops.FeatureLayout(d.layout), ControlTier.More, "shops", StreetShops.FeatureLayouts), true);
            var depth = c.Slider(StreetShops.DepthId, 24, 8, 48, 8, "m", ControlTier.More, "shops");
            depth.LabelKey = ShopKey + StreetShops.DepthId;
            depth.TooltipKey = depth.LabelKey + ".Tooltip";
            c.Fixed(StreetShops.AmountId);
            c.Fixed(StreetShops.LayoutId);
            c.Fixed(StreetShops.DepthId);
        }

        private static void Shared(ControlSchema c, bool optionTips)
        {
            c.LabelKey = ShopKey + c.Id;
            c.TooltipKey = c.LabelKey + ".Tooltip";
            foreach (var o in c.Options)
            {
                o.LabelKey = c.LabelKey + "." + o.Value;
                if (optionTips) o.TooltipKey = o.LabelKey + ".Tooltip";
            }
        }

        /// <summary>FeatureInfo for "Shops along the street" on a host; on by default where StreetShops.Defaults says so.</summary>
        public static FeatureInfo StreetShopsFeature(string area, string layerId = null) => new FeatureInfo
        {
            Layer = StreetShopsLayer, Target = StreetFrontage.ShopsTag, LabelKey = "Placemaker.Feature.streetShops",
            TooltipKey = "Placemaker.Feature.streetShops.Tooltip", Options = ShopOptions,
            Preset = new SettingsBag { Values = { ["depth"] = 48.0, [AddOns.EdgeZoningAddOn.CutoutSetting] = true } },
            DefaultOn = StreetShops.DefaultFor(layerId).on,
        };

        /// <summary>Planned by CarveStreetShops, emitted after PlanCore (so the host never mistakes them for its own paths).</summary>
        private sealed class ShopWork
        {
            public StreetShops.Result Layout;
        }

        private ShopWork CarveStreetShops(LayerContext ctx, AreaInput input)
        {
            var region = ctx.LayerRegion;
            if (ctx.S(StreetShops.AmountId) == StreetShops.None) return null;
            var gates = new List<float2>();
            foreach (var m in input.Markers) gates.Add(m.Position.xz);
            if (gates.Count == 0)
                foreach (var m in SuggestMarkers(input)) gates.Add(m.Position.xz);
            var layout = StreetShops.Plan(region, input, gates, StreetShops.FeatureLayout(ctx.S(StreetShops.LayoutId)), ctx.S(StreetShops.AmountId), ctx.F(StreetShops.DepthId),
                Hashing.Mix(ctx.Seed, Seeds.Hash("streetShops")));
            if (layout.Shops.Count == 0) { ctx.Issue(IssueSeverity.Info, "Placemaker.Feature.streetShops.Issue.NoFrontage"); return null; }
            var carved = StreetShops.Carve(region, layout.Carve, MinArea);
            var rest = carved == null ? null : Common.LayerRegion.From(carved);
            if (rest == null) { ctx.Issue(IssueSeverity.Info, "Placemaker.Feature.streetShops.Issue.TooSmall"); return null; }
            ctx.LayerRegion = rest;
            // "place:shops" makes the composer reserve the shop land for the host's other features (big trees,
            // picnic…, which plan on the whole outline); the zoning feature filling a region still sees it free.
            for (int i = 0; i < layout.Shops.Count; i++)
                if (layout.CellPlots) ctx.AddRegion(layout.Shops[i], StreetFrontage.ShopsTag + ":" + i, StreetFrontage.ShopsTag, ShopsPlace, Build.ZoneLots.CellsTag);
                else ctx.AddRegion(layout.Shops[i], StreetFrontage.ShopsTag + ":" + i, StreetFrontage.ShopsTag, ShopsPlace);
            ctx.Plan.Facts["shopBands"] = layout.Shops.Count;
            ctx.Plan.Facts["shopAlleys"] = layout.Alleys.Count;
            // A lane's dead end is where the park's paths should continue it: an extra entrance there (only
            // once the player placed entrances — without any, the host keeps its paths internal anyway).
            if (layout.Doors.Count > 0 && input.Markers.Count > 0)
            {
                string kind = input.Markers[0].Kind;
                foreach (var m in input.Markers) if (m.Kind == EntranceKinds.Pedestrian) { kind = m.Kind; break; }
                if (kind == EntranceKinds.Main || kind == EntranceKinds.Service) kind = EntranceKinds.Pedestrian;
                var markers = new List<EdgeMarker>(input.Markers);
                foreach (var p in layout.Doors)
                {
                    rest.ClosestOnBoundary(p, out int seg, out float t);
                    markers.Add(new EdgeMarker { Kind = kind, RingSegment = seg, RingT = t, Position = new float3(p.x, ctx.H(p), p.y) });
                }
                ctx.Input = new AreaInput
                {
                    Shape = input.Shape, Site = input.Site, Markers = markers, Settings = input.Settings, Seed = input.Seed,
                    RegionTags = input.RegionTags, Reserved = input.Reserved, ChildLayers = input.ChildLayers, ChildTargets = input.ChildTargets,
                };
            }
            return new ShopWork { Layout = layout };
        }

        /// <summary>
        /// After the host planned: its objects keep off the shop land (a crown reaching over a zone cell would
        /// make the zoning feature drop that cell), path ends at a lane's dead end sit exactly on the alley's
        /// end node (CS2 joins nets only at identical nodes), and the alleys are emitted.
        /// </summary>
        private static void FinishStreetShops(LayerContext ctx, ShopWork work)
        {
            var layout = work.Layout;
            var plan = ctx.Plan;
            plan.Objects.RemoveAll(o => o.Role != ObjectRole.Decal && StreetShops.Overlaps(layout.Shops, o.Position.xz, Composer.DefaultRadius(o.Role)));
            foreach (var door in layout.Doors)
                foreach (var n in plan.Nets)
                {
                    if (!PathJoins.IsPath(n)) continue;
                    if (n.StartAttach < 0 && math.distancesq(n.A.xz, door) < 4f) { var d = ctx.P3(door) - n.A; n.A += d; n.B += d; }
                    if (n.EndAttach < 0 && math.distancesq(n.D.xz, door) < 4f) { var d = ctx.P3(door) - n.D; n.D += d; n.C += d; }
                }
            var asset = new AssetRef(StreetShops.AlleyAsset, StreetShops.AlleyFallback);
            foreach (var alley in layout.Alleys)
            {
                var pts = alley.Points;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var net = ctx.StraightNet(pts[i], pts[i + 1], NetRole.Alley, asset);
                    if (i == 0 && alley.StartAttach >= 0) { net.StartAttach = alley.StartAttach; net.StartAttachT = alley.StartT; }
                    if (i + 2 == pts.Count && alley.EndAttach >= 0) { net.EndAttach = alley.EndAttach; net.EndAttachT = alley.EndT; }
                }
            }
        }

        protected abstract void PlanCore(LayerContext ctx);

        /// <summary>Object cap per hectare for this layer's own plan (ObjectBudget); 0 = no cap.</summary>
        protected virtual float ObjectsPerHectare => ObjectBudget.DefaultPerHectare;
    }
}
