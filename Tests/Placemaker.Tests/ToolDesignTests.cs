using System.Collections.Generic;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class ToolDesignTests
    {
        private readonly ToolFakeGenerator _gen = new ToolFakeGenerator();
        private readonly ToolFakeGenerator _plaza = new ToolFakeGenerator { Id = "plaza" };
        private readonly Dictionary<string, SettingsBag> _saved = new Dictionary<string, SettingsBag>();

        private DesignEditor Editor() => new DesignEditor(
            id => id == "fake" ? _gen : id == "plaza" ? _plaza : null,
            id => _saved.TryGetValue(id, out var b) ? b : null);

        private Recipe NewRecipe() => Editor().SingleLayerRecipe(_gen, 7);

        [Fact]
        public void PathsResolveChildrenAndOverrides()
        {
            var e = Editor();
            var r = NewRecipe();
            var child = e.AddLayer(r, "0", "fake", "block", 1);
            Assert.Equal("0/0", child);
            Assert.True(e.OverrideRegion(r, "0", "block:3", "plaza", 2));
            Assert.Same(r.Root, RecipePaths.Resolve(r, "0"));
            Assert.Same(r.Root.Children[0], RecipePaths.Resolve(r, "0/0"));
            Assert.Equal("plaza", RecipePaths.Resolve(r, "0/0@block:3").Layer);
            Assert.Null(RecipePaths.Resolve(r, "1"));
            Assert.Null(RecipePaths.Resolve(r, "0/5"));
            Assert.Null(RecipePaths.Resolve(r, "0/0@block:9"));
            Assert.Null(RecipePaths.Resolve(r, "0/x"));
            Assert.Null(RecipePaths.Resolve(r, ""));
        }

        [Fact]
        public void WalkVisitsOverridesWithResolvablePaths()
        {
            var e = Editor();
            var r = NewRecipe();
            e.AddLayer(r, "0", "fake", "block", 1);
            e.OverrideRegion(r, "0", "block:3", "plaza", 2);
            e.AddLayer(r, "0/0@block:3", "fake", "", 3);
            var paths = new List<string>();
            foreach (var kv in RecipePaths.Walk(r))
            {
                paths.Add(kv.Key);
                Assert.Same(kv.Value, RecipePaths.Resolve(r, kv.Key));
            }
            Assert.Equal(new[] { "0", "0/0", "0/0@block:3", "0/0@block:3/0" }, paths.ToArray());
        }

        [Fact]
        public void OwnerOfPlanPrefersOverride()
        {
            var e = Editor();
            var r = NewRecipe();
            e.AddLayer(r, "0", "fake", "block", 1);
            e.OverrideRegion(r, "0", "block:3", "plaza", 2);
            Assert.Equal("0/0@block:3", RecipePaths.OwnerOfPlan(r, "0/0@block:3"));
            Assert.Equal("0/0", RecipePaths.OwnerOfPlan(r, "0/0@block:1"));
            Assert.Equal("0", RecipePaths.OwnerOfPlan(r, "0@root"));
        }

        [Fact]
        public void RemoveAndMoveSteps()
        {
            var e = Editor();
            var r = NewRecipe();
            e.AddLayer(r, "0", "fake", "", 1);
            e.AddLayer(r, "0", "plaza", "", 2);
            Assert.False(e.RemoveStep(r, "0"));
            Assert.Equal("0/0", e.MoveStep(r, "0/1", -1));
            Assert.Equal("plaza", r.Root.Children[0].Layer);
            Assert.Null(e.MoveStep(r, "0/0", -1));
            Assert.True(e.RemoveStep(r, "0/0"));
            Assert.Single(r.Root.Children);
            Assert.Equal("fake", r.Root.Children[0].Layer);
        }

        [Fact]
        public void SetValueCoercesClampsAndLocks()
        {
            var e = Editor();
            var r = NewRecipe();
            Assert.True(e.SetValue(r, "0", "size", 500.0, true));
            Assert.Equal(100.0, r.Root.Settings.Values["size"]);
            Assert.Contains("size", r.Root.Locked);
            Assert.False(e.SetValue(r, "0", "style", "baroque", true));     // not an option
            Assert.True(e.SetValue(r, "0", "style", "wild", true));
            Assert.False(e.SetValue(r, "0", "unknown", 1.0, true));        // not a control of this layer
            Assert.True(e.SetValue(r, "0", "benches", "bench01", true));
            Assert.Equal(new[] { "bench01" }, (string[])r.Root.Settings.Values["benches"]);
            Assert.False(e.SetValue(r, "0", "size", 100.0, true));         // unchanged and already locked
            Assert.True(e.UnlockValue(r, "0", "size"));
        }

        [Fact]
        public void RandomizeRespectsLockedEvenIfGeneratorDoesNot()
        {
            var e = Editor();
            var r = NewRecipe();
            e.SetValue(r, "0", "size", 33.0, true);
            for (uint s = 1; s < 20; s++)
            {
                e.Randomize(r, "", s);
                Assert.Equal(33.0, r.Root.Settings.Values["size"]);
            }
        }

        [Fact]
        public void RandomizeIsDeterministicAndChangesSeed()
        {
            var e = Editor();
            var a = NewRecipe();
            var b = NewRecipe();
            e.Randomize(a, "", 42);
            e.Randomize(b, "", 42);
            Assert.True(a.Root.Settings.ContentEquals(b.Root.Settings));
            Assert.Equal(a.Root.Seed, b.Root.Seed);
            Assert.NotEqual(7u, a.Root.Seed);
        }

        [Fact]
        public void PresetAppliesAndLocks()
        {
            var e = Editor();
            var r = NewRecipe();
            Assert.True(e.ApplyPreset(r, "0", "big"));
            Assert.Equal(90.0, r.Root.Settings.Values["size"]);
            Assert.Equal("wild", r.Root.Settings.Values["style"]);
            Assert.Contains("size", r.Root.Locked);
            Assert.False(e.ApplyPreset(r, "0", "missing"));
        }

        [Fact]
        public void ResetStepUsesSavedDefaults()
        {
            var saved = new SettingsBag();
            saved.Values["size"] = 70.0;
            saved.Values["gone"] = 1.0;   // stale id, must be ignored
            _saved["fake"] = saved;
            var e = Editor();
            var r = NewRecipe();
            Assert.Equal(70.0, r.Root.Settings.Values["size"]);
            Assert.False(r.Root.Settings.Values.ContainsKey("gone"));
            e.SetValue(r, "0", "size", 20.0, true);
            Assert.True(e.ResetStep(r, "0"));
            Assert.Equal(70.0, r.Root.Settings.Values["size"]);
            Assert.Empty(r.Root.Locked);
        }

        [Fact]
        public void FileSettingsOverlayDefaults()
        {
            var r = RecipeJson.Parse("{\"id\":\"x\",\"root\":{\"layer\":\"fake\",\"settings\":{\"size\":500,\"future\":3},"
                + "\"children\":[{\"layer\":\"plaza\",\"settings\":{\"style\":\"wild\"}}]}}").Recipe;
            Editor().ApplyDefaults(r);
            Assert.Equal(100.0, r.Root.Settings.Values["size"]);          // clamped override
            Assert.Equal("formal", r.Root.Settings.Values["style"]);      // default
            Assert.Equal(3.0, r.Root.Settings.Values["future"]);          // unknown id kept
            Assert.Equal(true, r.Root.Children[0].Settings.Values["trees"]);
            Assert.Equal("wild", r.Root.Children[0].Settings.Values["style"]);
        }

        [Fact]
        public void MarkersAddRemoveClear()
        {
            var e = Editor();
            var r = NewRecipe();
            Assert.True(e.AddMarker(r, "0", new EdgeMarker { Kind = "entrance", Position = new float3(5, 0, 0) }));
            Assert.True(e.AddMarker(r, "0", new EdgeMarker { Kind = "exit", Position = new float3(50, 0, 0) }));
            Assert.False(e.RemoveMarkerNear(r, "0", new float2(20, 0), 3f));
            Assert.True(e.RemoveMarkerNear(r, "0", new float2(6, 0), 3f));
            Assert.Single(r.Root.Markers);
            Assert.True(e.ClearMarkers(r, "0", "exit"));
            Assert.Empty(r.Root.Markers);
            Assert.False(e.ClearMarkers(r, "0", "exit"));
        }

        [Fact]
        public void RightClickTakesBackTheLastMarkerPlaced()
        {
            var e = Editor();
            var r = NewRecipe();
            Assert.False(e.RemoveLastMarker(r, "0"));
            e.AddMarker(r, "0", new EdgeMarker { Kind = "exit", Position = new float3(50, 0, 0) });
            e.AddMarker(r, "0", new EdgeMarker { Kind = "entrance", Position = new float3(5, 0, 0) });
            Assert.True(e.RemoveLastMarker(r, "0"));
            Assert.Equal("exit", Assert.Single(r.Root.Markers).Kind);
            Assert.True(e.RemoveLastMarker(r, "0"));
            Assert.Empty(r.Root.Markers);
        }

        [Fact]
        public void FirstUnreadyFindsMarkerStep()
        {
            _gen.NeedsEntrance = true;
            var e = Editor();
            var r = NewRecipe();
            var area = new AreaShape { Ring = ToolTestData.Square(100) };
            var u = e.FirstUnready(r, area, new SiteContext());
            Assert.NotNull(u);
            Assert.Equal("0", u.StepPath);
            Assert.Equal("entrance", u.MarkerKind);
            e.AddMarker(r, "0", new EdgeMarker { Kind = "entrance", Position = new float3(50, 0, 0) });
            Assert.Null(e.FirstUnready(r, area, new SiteContext()));
        }

        [Fact]
        public void DisabledStepsAreNotCheckedForReadiness()
        {
            var e = Editor();
            var r = NewRecipe();
            e.AddLayer(r, "0", "plaza", "", 1);
            _plaza.NeedsEntrance = true;
            var area = new AreaShape { Ring = ToolTestData.Square(100) };
            Assert.Equal("0/0", e.FirstUnready(r, area, null).StepPath);
            e.SetEnabled(r, "0/0", false);
            Assert.Null(e.FirstUnready(r, area, null));
            Assert.False(e.SetEnabled(r, "0", false));   // root stays enabled
        }

        [Fact]
        public void SetTargetAndOverrideRemoval()
        {
            var e = Editor();
            var r = NewRecipe();
            e.AddLayer(r, "0", "fake", "", 1);
            Assert.True(e.SetTarget(r, "0/0", "block"));
            Assert.False(e.SetTarget(r, "0/0", "block"));
            Assert.False(e.SetTarget(r, "0", "block"));
            e.OverrideRegion(r, "0", "block:1", "plaza", 1);
            Assert.False(e.OverrideRegion(r, "0", "block:1", "plaza", 2));
            Assert.True(e.OverrideRegion(r, "0", "block:1", "", 0));
            Assert.Empty(r.Root.RegionOverrides);
        }
    }
}
