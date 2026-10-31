// s15: disabled-with-reason (schema fields, Availability for the area, the design/state JSON) and the UI/DLL protocol.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ToolAvailabilityTests
    {
        /// <summary>Host with two features and an area rule: "frontage" needs a road along the outline.</summary>
        private sealed class Host : IAreaGenerator, IHasFeatures, IHasAvailability
        {
            private readonly ToolFakeGenerator _inner = new ToolFakeGenerator { Id = "host" };
            public IReadOnlyList<FeatureInfo> Features { get; } = new List<FeatureInfo>
            {
                new FeatureInfo { Layer = "small" },
                new FeatureInfo { Layer = "big" },
            };
            public string Id => "host";
            public string NameKey => _inner.NameKey;
            public string Icon => "";
            public LayerKind Kind => LayerKind.Fill;
            public string[] Accepts => _inner.Accepts;
            public string[] Emits => _inner.Emits;
            public int Version => 1;
            public IReadOnlyList<ControlSchema> Controls => _inner.Controls;
            public IReadOnlyList<PresetInfo> Presets => _inner.Presets;
            public SettingsBag Defaults() => _inner.Defaults();
            public float MinArea => 10;
            public float MaxArea => 1e7f;
            public Readiness CheckReady(AreaInput input) => new Readiness();
            public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();
            public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current.Clone();
            public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();

            public void Unavailable(AreaInput input, List<Unavailable> into)
            {
                if (input.Site.NearbyNets.Count == 0)
                    into.Add(new Unavailable { Id = "style", ReasonKey = "Placemaker.Why.NeedsStreet" });
            }
        }

        private sealed class Sized : IAreaGenerator
        {
            private readonly ToolFakeGenerator _inner = new ToolFakeGenerator();
            public Sized(string id, float min) { Id = id; MinArea = min; }
            public string Id { get; }
            public string NameKey => "L." + Id;
            public string Icon => "";
            public LayerKind Kind => LayerKind.AddOn;
            public string[] Accepts => new string[0];
            public string[] Emits => new string[0];
            public int Version => 1;
            public IReadOnlyList<ControlSchema> Controls => new List<ControlSchema>();
            public IReadOnlyList<PresetInfo> Presets => new List<PresetInfo>();
            public SettingsBag Defaults() => new SettingsBag();
            public float MinArea { get; }
            public float MaxArea => 1e7f;
            public Readiness CheckReady(AreaInput input) => new Readiness();
            public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();
            public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current.Clone();
            public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();
        }

        private readonly Dictionary<string, IAreaGenerator> _gens = new Dictionary<string, IAreaGenerator>
        {
            ["host"] = new Host(),
            ["small"] = new Sized("small", 100),
            ["big"] = new Sized("big", 5000),
        };

        private IAreaGenerator Gen(string id) => id != null && _gens.TryGetValue(id, out var g) ? g : null;

        private static AreaShape Square(float side) => new AreaShape { Ring = TestKit.Rect(side, side) };

        [Fact]
        public void Feature_needing_more_than_the_whole_area_is_unavailable_with_its_minimum()
        {
            var recipe = Recipe.Single(_gens["host"], 1);
            var a = Availability.For(recipe, Square(50), new SiteContext(), Gen);   // 2500 m²
            Assert.True(a.ContainsKey("0"));
            var why = a["0"][Unavailable.Feature("big")];
            Assert.Equal(Availability.AreaTooSmall, why.Key);
            Assert.Equal(5000.0, System.Convert.ToDouble(why.Args[0]));
            Assert.False(a["0"].ContainsKey(Unavailable.Feature("small")));
            // A big enough area: nothing is unavailable for size.
            var b = Availability.For(recipe, Square(100), TestKit.RoadAlongBottom(100), Gen);
            Assert.False(b.ContainsKey("0"));
        }

        [Fact]
        public void Layers_report_area_conditions_through_IHasAvailability()
        {
            var recipe = Recipe.Single(_gens["host"], 1);
            var noRoad = Availability.For(recipe, Square(100), new SiteContext(), Gen);
            Assert.Equal("Placemaker.Why.NeedsStreet", noRoad["0"]["style"].Key);
            var withRoad = Availability.For(recipe, Square(100), TestKit.RoadAlongBottom(100), Gen);
            Assert.False(withRoad.ContainsKey("0"));
        }

        [Fact]
        public void No_area_or_recipe_means_nothing_to_report()
        {
            Assert.Empty(Availability.For(null, Square(10), null, Gen));
            var recipe = Recipe.Single(_gens["host"], 1);
            var a = Availability.For(recipe, null, null, Gen);
            Assert.False(a.ContainsKey("0") && a["0"].Keys.Any(k => k.StartsWith(Unavailable.FeaturePrefix)));
        }

        [Fact]
        public void Design_json_carries_disabled_controls_and_features()
        {
            var recipe = Recipe.Single(_gens["host"], 1);
            var why = Availability.For(recipe, Square(50), new SiteContext(), Gen);
            var json = UiModels.Design(recipe, "host", false, "0", null, null, Gen, unavailable: why).ToString();
            Assert.Contains("\"disabled\":{\"style\":{\"key\":\"Placemaker.Why.NeedsStreet\"", json);
            Assert.Contains("\"disabled\":{\"key\":\"Placemaker.Why.AreaTooSmall\"", json);
            // Without the map nothing is added (old shape of the JSON).
            Assert.DoesNotContain("\"disabled\"", UiModels.Design(recipe, "host", false, "0", null, null, Gen).ToString());
        }

        [Fact]
        public void Control_json_has_the_new_fields_only_when_set()
        {
            var plain = UiModels.Control(new ControlSchema { Id = "a", Kind = ControlKind.Toggle, VisibleWhen = "b=true" }).ToString();
            Assert.Contains("\"visibleWhen\":\"b=true\"", plain);
            Assert.DoesNotContain("hideWhenOff", plain);
            Assert.DoesNotContain("enabledWhen", plain);
            Assert.DoesNotContain("disabledReason", plain);
            var full = UiModels.Control(new ControlSchema
            {
                Id = "a", Kind = ControlKind.Toggle, VisibleWhen = "b=true", HideWhenOff = true, EnabledWhen = "c=x", DisabledReasonKey = "Placemaker.Why.X",
            }).ToString();
            Assert.Contains("\"hideWhenOff\":true", full);
            Assert.Contains("\"enabledWhen\":\"c=x\"", full);
            Assert.Contains("\"disabledReason\":\"Placemaker.Why.X\"", full);
        }

        [Fact]
        public void Roads_hides_only_its_per_pattern_essentials()
        {
            var roads = GeneratorRegistry.Get("roads");
            Assert.NotNull(roads);
            var conditional = roads.Controls.Where(c => !string.IsNullOrEmpty(c.VisibleWhen)).ToList();
            Assert.NotEmpty(conditional);
            // (and the terrace settings on natural land, a look setting's own options while it is off (LookSettings.OnCondition),
            // and pre-zoning where the type does not offer it)
            foreach (var c in conditional)
                Assert.True((c.VisibleWhen.StartsWith("pattern=") || c.VisibleWhen.StartsWith("landShape=") || c.VisibleWhen.StartsWith("look")
                    || c.VisibleWhen == "preZone=offered") == c.HideWhenOff, c.Id + ": " + c.VisibleWhen);
            Assert.Contains(conditional, c => !c.HideWhenOff);   // e.g. cul-de-sac options: greyed, not hidden
        }

        [Fact]
        public void State_json_reports_protocol_and_every_area_mode()
        {
            var json = UiModels.State(new ToolStateInput
            {
                Primary = new PrimaryAction(),
                ModeUnavailable = new Dictionary<SelectionMode, LocText> { [SelectionMode.Perimeter] = new LocText("Placemaker.Why.NoRoads") },
            }).ToString();
            Assert.Contains("\"protocol\":" + UiModels.Protocol, json);
            foreach (var m in SelectionModes.All) Assert.Contains("{\"mode\":\"" + SelectionModes.Id(m) + "\"", json);
            Assert.Contains("{\"mode\":\"blockPaint\",\"enabled\":true}", json);
            Assert.Contains("{\"mode\":\"perimeter\",\"enabled\":false,\"reason\":{\"key\":\"Placemaker.Why.NoRoads\"}}", json);
            Assert.Equal(5, SelectionModes.All.Length);
            Assert.Equal(SelectionModes.All.Length, SelectionModes.All.Distinct().Count());
        }

        [Fact]
        public void Protocol_matches_the_ui_contract()
        {
            // UI/src/contract.ts PROTOCOL must be bumped together with UiModels.Protocol.
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "UI", "src", "contract.ts"))) dir = dir.Parent;
            if (dir == null) return;   // not run from the repository
            var ts = System.IO.File.ReadAllText(System.IO.Path.Combine(dir.FullName, "UI", "src", "contract.ts"));
            Assert.Contains("export const PROTOCOL = " + UiModels.Protocol + ";", ts);
        }
    }
}
