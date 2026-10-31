// P2-4 (2026-10-30): the shared decal library's memory with every category at once.
//
// The library test (DecalLibraryTests.TheWholeLibraryFitsTheBudget) counts every variant of every kind; a city only makes the
// variants its builds use, and SharedDecals keeps 200 MiB on the GPU (beyond it, unused variants are freed; variants in use
// never are). This plans every place type of the shelf in every style it shows, on one flat block and on rolling land, and adds
// up the distinct variants their grounds use: the most a city that builds one of everything can hold at once.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class DecalBudgetTests
    {
        private readonly ITestOutputHelper _out;
        public DecalBudgetTests(ITestOutputHelper o) { _out = o; }

        /// <summary>SharedDecals.BudgetBytes (Game): what the library may hold on the GPU.</summary>
        private const long GameBudget = 200L * 1024 * 1024;

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        [Fact]
        public void EveryCategoryAtOnceFitsTheGameBudget()
        {
            GroundDesignTests.Lib();   // the planners' grounds read the library (as the game's first plan does)
            var lib = DecalLibraryTests.Library();
            var shelf = ControlEffectTests.ProductShelf();
            var used = new HashSet<string>(StringComparer.Ordinal);
            var byType = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var id in ControlEffectTests.PlaceTypes())
            {
                var g = GeneratorRegistry.Get(id);
                foreach (var style in ControlEffectTests.Styles(g, shelf))
                    foreach (bool hills in new[] { false, true })
                    {
                        var ring = TestKit.Rect(220f, 180f);
                        var site = TestKit.RoadAlongBottom(220f);
                        if (hills) site.HeightAt = p => 0.06f * p.y + 2.5f * math.sin(p.x / 37f) * math.cos(p.y / 29f);
                        var r = Editor.SingleLayerRecipe(g, 7);
                        uint seed = 9;
                        if (style == null) Editor.AddDefaultFeatures(r, () => seed++);
                        else
                        {
                            foreach (var kv in style.Values.Values) r.Root.Settings.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
                            foreach (var f in Features.Of(g))
                                if (style.FeatureOn(f)) r.Root.Children.Add(Editor.FeatureStep(f, seed++, DesignEditor.PresetFeatureValues(style, f.Layer)));
                        }
                        r = TestKit.Entered(r, ring, site);
                        ComposedPlan plan;
                        try { plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None); }
                        catch (Exception e) { _out.WriteLine($"{id}/{style?.Id ?? "standard"}: {e.GetType().Name}"); continue; }
                        if (!byType.TryGetValue(id, out var mine)) byType[id] = mine = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var l in plan.Layers)
                            foreach (var d in l.DecalInstances) { used.Add(d.Id); mine.Add(d.Id); }
                    }
            }
            var bytesOf = new Dictionary<string, long>(StringComparer.Ordinal);
            long total = 0;
            var byFamily = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var v in used)
            {
                Assert.True(DecalVariants.TryParseId(v, out var kind, out int index), v);
                var k = lib.Get(kind);
                Assert.True(k != null, "a plan uses " + kind + ", which is not in the library");
                var b = DecalVariants.Bake(k, index);
                long bytes = DecalVariants.GpuBytes(b.Img.W, b.Img.H);
                bytesOf[v] = bytes;
                total += bytes;
                byFamily[k.Family ?? ""] = (byFamily.TryGetValue(k.Family ?? "", out var f) ? f : 0) + bytes;
            }
            _out.WriteLine($"{used.Count} variants used by {byType.Count} types, {total / 1048576.0:F1} MiB of the {GameBudget / 1048576} MiB budget");
            foreach (var kv in byFamily) _out.WriteLine($"  family {kv.Key}: {kv.Value / 1048576.0:F1} MiB");
            foreach (var kg in used.GroupBy(v => v.Split('#')[0]).OrderByDescending(g => g.Sum(v => bytesOf[v])).Take(40))
            {
                var any = DecalVariants.Bake(lib.Get(kg.Key), 0);
                _out.WriteLine($"  kind {kg.Key}: {kg.Count()} variants, {kg.Sum(v => bytesOf[v]) / 1048576.0:F1} MiB (variant 0 {any.Img.W}x{any.Img.H} px)");
            }
            foreach (var kv in byType.OrderByDescending(kv => kv.Value.Sum(v => bytesOf[v])))
                _out.WriteLine($"  type {kv.Key}: {kv.Value.Count} variants, {kv.Value.Sum(v => bytesOf[v]) / 1048576.0:F1} MiB");
            Assert.True(total <= GameBudget * 0.9, $"every category at once uses {total / 1048576.0:F1} MiB: over 90 % of the game's {GameBudget / 1048576} MiB");
        }
    }
}
