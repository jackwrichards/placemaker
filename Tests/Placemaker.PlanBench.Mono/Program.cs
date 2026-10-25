// (perf) The preview planner timed in the game's own runtime (Unity's Mono, run by tools/monohost): every shipped type ×
// preset (the gallery's default site: an irregular hexagon, flat, no streets), at the given sizes, one plan at a time.
// Prints each plan's time (the fastest of --repeat), its Composer / layer steps (PlanClock) and the garbage collections it
// caused, and writes <out>/bench.csv. The .NET twin with the gallery's sites is `Placemaker.Gallery --bench`.
//   dotnet build Tests/Placemaker.PlanBench.Mono -c Release -o <dir>
//   tools/monohost/bin/monohost.exe <dir>/Placemaker.PlanBench.Mono.exe <repo> [--only park,parking] [--areas 1,5,12.5] [--repeat 2] [--out <dir>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.PlanBench.Mono
{
public static class Program
{
    private static readonly float2[] BaseRing =
    {
        new float2(0, 0), new float2(140, -6), new float2(152, 58), new float2(118, 102), new float2(46, 96), new float2(6, 70),
    };

    public static int Main(string[] args)
    {
        string repo = args[0], outDir = null;
        HashSet<string> only = null, presets = null;
        float[] areas = { 1f, 5f, 12.5f };
        int repeat = 2;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--only": only = new HashSet<string>(args[++i].Split(','), StringComparer.OrdinalIgnoreCase); break;
                case "--presets": presets = new HashSet<string>(args[++i].Split(','), StringComparer.OrdinalIgnoreCase); break;
                case "--areas": areas = args[++i].Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); break;
                case "--repeat": repeat = int.Parse(args[++i]); break;
                case "--out": outDir = args[++i]; break;
                case "--corners": Corners = int.Parse(args[++i]); break;
                case "--heap": HeapMb = int.Parse(args[++i]); break;
            }
        }
        // --heap N: N MB of live small objects held for the whole run, like the game's managed heap (a city's worth of
        // prefabs, UI and mods): the garbage collector must trace all of them at every collection a plan causes.
        var ballast = Ballast(HeapMb);
        if (HeapMb > 0) Console.WriteLine($"ballast: {GC.GetTotalMemory(true) / (1 << 20)} MB live");
        var assets = LoadCatalog(Path.Combine(repo, "Tests", "Placemaker.Tests", "Fixtures", "catalog-bounds.json"), out var source);
        var editor = new DesignEditor(GeneratorRegistry.Get);
        var jobs = new List<(string type, string variant, Recipe recipe)>();
        foreach (var f in Directory.GetFiles(Path.Combine(repo, "Recipes"), "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            Recipe template;
            try { template = RecipeJson.Parse(File.ReadAllText(f)).Recipe; } catch (Exception e) { Console.WriteLine($"{f}: {e.Message}"); continue; }
            string id = string.IsNullOrEmpty(template.Id) ? Path.GetFileNameWithoutExtension(f) : template.Id;
            if (only != null && !only.Contains(id)) continue;
            var gen = GeneratorRegistry.Get(template.Root.Layer);
            if (gen == null) continue;
            Recipe Make(string preset)
            {
                var r = template.Clone();
                editor.ApplyDefaults(r);
                uint n = 0;
                editor.AddDefaultFeatures(r, () => ++n);
                if (preset != null) editor.ApplyPreset(r, RecipePaths.Root, preset);
                uint k = 0;
                foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
                return r;
            }
            jobs.Add((id, "default", Make(null)));
            foreach (var p in gen.Presets ?? Array.Empty<PresetInfo>())
                if (p != null && !string.IsNullOrEmpty(p.Id) && (presets == null || presets.Contains(p.Id))) jobs.Add((id, "preset-" + p.Id, Make(p.Id)));
        }
        Console.WriteLine($"{jobs.Count} designs × {areas.Length} sizes (runtime: {(Type.GetType("Mono.Runtime") != null ? "Mono" : ".NET")})");
        var csv = new StringBuilder("type,variant,ha,totalMs,gc,steps\n");
        double total = 0;
        foreach (var (type, variant, recipe) in jobs)
            foreach (float ha in areas)
            {
                var ring = Site(ha * 10000f);
                double best = double.MaxValue;
                ComposedPlan bestPlan = null;
                int gcs = 0;
                string failure = null;
                for (int r = 0; r < Math.Max(1, repeat); r++)
                {
                    var rec = recipe.Clone();
                    AddMarkersIfNeeded(rec, ring);
                    var site = new SiteContext { Assets = assets, LayoutScale = SiteContext.DefaultLayoutScale };
                    int gc0 = GC.CollectionCount(0);
                    var sw = Stopwatch.StartNew();
                    ComposedPlan plan;
                    try
                    {
                        plan = Composer.Compose(rec, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
                        AutoAssets.Resolve(plan, source);
                    }
                    catch (Exception e) { failure = e.GetType().Name + ": " + e.Message; plan = null; }
                    double ms = sw.Elapsed.TotalMilliseconds;
                    if (ms < best) { best = ms; bestPlan = plan; gcs = GC.CollectionCount(0) - gc0; }
                }
                total += best;
                string steps = failure ?? PlanTimings.Describe(bestPlan);
                Console.WriteLine($"  {type}/{variant} {ha:0.#} ha: {best:0} ms, {gcs} GC | {steps}");
                csv.Append(type).Append(',').Append(variant).Append(',').Append(ha.ToString(CultureInfo.InvariantCulture)).Append(',')
                   .Append(best.ToString("0.#", CultureInfo.InvariantCulture)).Append(',').Append(gcs).Append(",\"").Append(steps.Replace("\"", "'")).Append("\"\n");
            }
        Console.WriteLine($"Total {total / 1000:0.0} s");
        GC.KeepAlive(ballast);
        if (outDir != null) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "bench-mono.csv"), csv.ToString()); }
        return 0;
    }

    private static float3[] Site(float targetArea)
    {
        double a = 0;
        for (int i = 0, j = BaseRing.Length - 1; i < BaseRing.Length; j = i++) a += BaseRing[j].x * BaseRing[i].y - BaseRing[i].x * BaseRing[j].y;
        float s = (float)math.sqrt(targetArea / math.abs(a * 0.5));
        var ring = BaseRing.Select(p => new float3(p.x * s, 0, p.y * s)).ToArray();
        return Corners > ring.Length ? Wobbly(ring, Corners) : ring;
    }

    public static int HeapMb;

    private sealed class Node { public Node Next; public object Payload; public int A, B; }

    private static Node[] Ballast(int mb)
    {
        if (mb <= 0) return null;
        long count = (long)mb * (1 << 20) / 48;   // a node and a boxed int, about 48 bytes
        var roots = new Node[1024];
        for (long i = 0; i < count; i++)
        {
            int r = (int)(i & 1023);
            roots[r] = new Node { Next = roots[r], Payload = (object)(int)i, A = (int)i };
        }
        return roots;
    }

    /// <summary>--corners N: the outline resampled to N corners with a ±0.4 m wobble, like a painted area's traced outline.</summary>
    public static int Corners;

    private static float3[] Wobbly(float3[] ring, int n)
    {
        float per = 0f;
        for (int i = 0; i < ring.Length; i++) per += math.distance(ring[i], ring[(i + 1) % ring.Length]);
        var rng = new Unity.Mathematics.Random(12345u);
        var res = new List<float3>();
        float step = per / n, acc = 0f;
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Length];
            float len = math.distance(a, b);
            var nrm = math.normalize(new float3(-(b.z - a.z), 0, b.x - a.x));
            for (float t = acc; t < len; t += step) res.Add(math.lerp(a, b, t / len) + nrm * rng.NextFloat(-0.4f, 0.4f));
            acc = (acc - len) % step; if (acc < 0) acc += step;
        }
        return res.ToArray();
    }

    /// <summary>A layer that is not ready without markers (parking needs an entrance) gets one on the first edge (as the gallery).</summary>
    private static void AddMarkersIfNeeded(Recipe r, float3[] ring)
    {
        var step = r.Root;
        var gen = GeneratorRegistry.Get(step.Layer);
        if (gen == null) return;
        var input = new AreaInput { Shape = new AreaShape { Ring = ring }, Settings = step.Settings, Markers = step.Markers };
        Readiness ready;
        try { ready = gen.CheckReady(input); } catch (Exception) { return; }
        if (ready == null || ready.Ready) return;
        var kinds = (gen.Controls ?? Array.Empty<ControlSchema>()).Where(c => c.Kind == ControlKind.Markers && c.MarkerKinds != null).SelectMany(c => c.MarkerKinds).Distinct().ToList();
        foreach (var kind in kinds)
        {
            var m = new EdgeMarker { Kind = kind, RingSegment = 0, RingT = 0.5f, Position = (ring[0] + ring[1]) * 0.5f };
            step.Markers.Add(m);
            try { if (gen.CheckReady(input).Ready) return; } catch (Exception) { return; }
            step.Markers.Remove(m);
        }
    }

    /// <summary>The gallery's catalog fixture (footprints, pools, palettes), read with Core's own JSON.</summary>
    private static Dictionary<string, AssetInfo> LoadCatalog(string path, out AutoAssetSource source)
    {
        var root = JsonValue.Parse(File.ReadAllText(path));
        var pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var p in root["pools"].Properties) pools[p.Key] = p.Value.Items.Select(x => x.AsString()).ToList();
        var palettes = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        foreach (var p in root["palettes"].Properties)
            palettes[p.Key] = p.Value.Properties.ToDictionary(q => q.Key, q => q.Value.Items.Select(x => x.AsString()).ToList(), StringComparer.Ordinal);
        var items = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
        foreach (var p in root["items"].Properties)
        {
            var info = new AssetInfo { Id = p.Key };
            var b = p.Value["b"];
            if (b.IsArray)
            {
                var a = b.Items.Select(x => (float)x.AsNumber()).ToArray();
                info.BoundsMin = new float3(a[0], 0, a[1]);
                info.BoundsMax = new float3(a[2], a[4], a[3]);
            }
            if (p.Value.Has("w")) info.NetWidth = (float)p.Value["w"].AsNumber();
            items[p.Key] = info;
        }
        var all = new HashSet<string>(items.Keys.Concat(pools.Values.SelectMany(l => l)), StringComparer.Ordinal);
        IReadOnlyList<string> Pool(string category)
        {
            while (!string.IsNullOrEmpty(category))
            {
                if (pools.TryGetValue(category, out var l)) return l;
                int dot = category.LastIndexOf('.');
                if (dot <= 0) break;
                category = category.Substring(0, dot);
            }
            return Array.Empty<string>();
        }
        var names = palettes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        source = new AutoAssetSource
        {
            Pool = Pool,
            Exists = all.Contains,
            Palettes = names,
            PaletteItems = (p, cat) => p != null && cat != null && palettes.TryGetValue(p, out var m) && m.TryGetValue(cat, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>(),
        };
        return items;
    }
}
}
