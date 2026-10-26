// Placemaker visual review gallery (FEEDBACK-4 decision 5: "nobody looks at the result").
//
// For every shipped recipe (Recipes/*.json) and every fill type without one: the default design,
// each built-in preset, and feature combos (all features on / none). Each is composed with the REAL
// Core (Composer + AutoAssets with the catalog's automatic pools and park palettes) on a fixed
// irregular site, then drawn top-down:
//   surfaces in role colours, path/road nets at their real width, objects at their real footprint
//   (catalog bounds × scale, rotated by yaw) coloured by role, tree crowns at real crown size,
//   terraform as height tint + 1 m contours, plan issues as red/amber rings, a 10 m scale bar.
// Writes <out>/<type>/<variant>.png, <out>/index.html and <out>/gallery.json (metrics per image).
//
//   dotnet run -c Release --project Tests/Placemaker.Gallery -- --out docs/gallery [--only park,memorialPark] [--water beach,waterfront] [--decals]
//   (s30) ... -- --terrain-dump <ModsData/Placemaker/debug/x.json or folder> --out <dir>: renders in-game terrain dumps
//   --decals: also "decals-on" (100 %), "decals-max" (300 %) and "decals-gritty" per type (decals are off by default); decals are drawn in category colours with a legend.
//   --paint: ground paint variants per type (lawn painted / surface, gritty 200 %, off, painted trails, arid map) + _paint/toolkit;
//   --map arid|mediterranean|tropic|alpine|shuffled: plan and draw with that map's paint materials. Terrain (bare ground and
//   paint) is drawn in the census colours at a fixed exposure (PaintView.cs).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Gallery
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string outDir = "docs/gallery", repo = null, fixture = null, dump = null, terrainDump = null;
            HashSet<string> only = null;
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--out": outDir = next; i++; break;
                    case "--repo": repo = next; i++; break;
                    case "--catalog": fixture = next; i++; break;
                    case "--only": only = new HashSet<string>(next.Split(','), StringComparer.OrdinalIgnoreCase); i++; break;
                    case "--showcase": Renderer.Showcase = true; break;
                    case "--shops": Variants.ShopVariants = true; break;
                    case "--decals": Variants.DecalVariants = true; break;
                    case "--paint": Variants.PaintVariants = true; break;
                    case "--map": Renderer.MapPalette = next; i++; break;
                    case "--water": Renderer.WaterTypes = new HashSet<string>(next.Split(','), StringComparer.OrdinalIgnoreCase); i++; break;
                    case "--shores": Renderer.Shores = next.Split(','); i++; break;
                    case "--slopes": Renderer.Slopes = next.Split(','); i++; break;
                    case "--plan-json": Renderer.PlanJson = true; break;   // sketch research: <variant>.plan.json beside each picture
                    case "--bench": Renderer.BenchOnly = true; break;   // (perf) plan only, one job at a time, timings per step (Bench.cs)
                    case "--repeat": Renderer.BenchRepeat = int.Parse(next); i++; break;
                    case "--corners": Renderer.Corners = int.Parse(next); i++; break;   // (perf) the site outline resampled to N wobbly corners (a painted outline)
                    case "--size": Renderer.ImageSize = float.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "--zoom": Renderer.Zoom = next.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); i++; break;
                    case "--roads":
                        bool listed = next != null && !next.StartsWith("--");
                        Renderer.RoadSiteKinds = listed ? next.Split(',') : RoadSites.Kinds;
                        if (listed) i++;
                        break;
                    case "--scale": Renderer.LayoutScale = float.Parse(next, System.Globalization.CultureInfo.InvariantCulture); i++; break;
                    case "--areas": Variants.Areas = next.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); i++; break;
                    case "--shapes": Variants.Shapes = next.Split(','); i++; break;
                    case "--sites": Variants.Sites = next.Split(','); i++; break;   // (s50) commercial review sites: square, halfcurved, curved
                    case "--palettes": Variants.Palettes = true; break;
                    case "--presets": Variants.PresetFilter = new HashSet<string>(next.Split(','), StringComparer.OrdinalIgnoreCase); i++; break;
                    case "--no-features": Variants.NoFeatureVariants = true; break;
                    case "--terrain": Variants.TerrainVariants = true; break;
                    case "--no-harmony": Variants.NoHarmony = true; break;
                    case "--no-shops": Variants.NoShops = true; break;
                    case "--dump": dump = next; i++; break;
                    case "--markers": Renderer.MarkerSpec = next; i++; break;   // (rf-shops3) entrances placed on every job
                    case "--terrain-dump": terrainDump = next; i++; break;   // s30: render in-game terrain dumps (DumpView)
                }
            }
            repo ??= FindRepo();
            if (!Path.IsPathRooted(outDir)) outDir = Path.GetFullPath(Path.Combine(repo, outDir));
            if (terrainDump != null) return DumpView.Run(terrainDump, outDir);
            fixture ??= Path.Combine(repo, "Tests", "Placemaker.Tests", "Fixtures", "catalog-bounds.json");
            var catalog = Catalog.Load(fixture);
            // s18: the player's full catalog dump (when present) decides which explicit ids exist, so designed
            // palettes resolve to the modded surfaces the player has instead of their vanilla fallbacks.
            dump ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II", "ModsData", "Placemaker", "catalog-dump.json");
            if (File.Exists(dump)) catalog.LoadDumpIds(dump);
            Directory.CreateDirectory(outDir);

            var jobs = Variants.All(repo, only);
            Console.WriteLine($"Gallery: {jobs.Count} images, {GeneratorRegistry.All.Count} layers, catalog {catalog.Items.Count} items → {outDir}");
            var results = new Result[jobs.Count];
            var sw = Stopwatch.StartNew();
            if (Renderer.BenchOnly) return Bench.Run(jobs, catalog, outDir);
            Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
            {
                results[i] = Renderer.Run(jobs[i], catalog, outDir);
                Console.WriteLine($"  {jobs[i].Type}/{jobs[i].Variant}: {results[i].Summary}");
            });
            Report.Write(outDir, jobs, results);
            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0.0}s. Open {Path.Combine(outDir, "index.html")}");
            return 0;
        }

        private static string FindRepo()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) return dir.FullName;
                dir = dir.Parent;
            }
            dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Placemaker.csproj"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Repository root (Placemaker.csproj) not found; pass --repo.");
        }
    }

    // ───────────────────────── catalog fixture ─────────────────────────

    public sealed class Catalog
    {
        public Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, List<string>>> Palettes = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        public Dictionary<string, AssetInfo> Items = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
        public List<string> PaletteNames = new List<string>();
        /// <summary>Every prefab id of the player's catalog dump (s18), empty without one.</summary>
        public HashSet<string> DumpIds = new HashSet<string>(StringComparer.Ordinal);

        public void LoadDumpIds(string path)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var cat in doc.RootElement.GetProperty("categories").EnumerateObject())
                    foreach (var it in cat.Value.EnumerateArray())
                        if (it.TryGetProperty("id", out var id) && id.GetString() is string s) DumpIds.Add(s);
            }
            catch (Exception e) { Console.WriteLine("catalog dump: " + e.Message); }
        }

        public static Catalog Load(string path)
        {
            var c = new Catalog();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            foreach (var p in root.GetProperty("pools").EnumerateObject())
                c.Pools[p.Name] = p.Value.EnumerateArray().Select(x => x.GetString()).ToList();
            foreach (var p in root.GetProperty("palettes").EnumerateObject())
            {
                var byCat = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var q in p.Value.EnumerateObject()) byCat[q.Name] = q.Value.EnumerateArray().Select(x => x.GetString()).ToList();
                c.Palettes[p.Name] = byCat;
            }
            c.PaletteNames = c.Palettes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var p in root.GetProperty("items").EnumerateObject())
            {
                var info = new AssetInfo { Id = p.Name };
                if (p.Value.TryGetProperty("b", out var b))
                {
                    var a = b.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
                    info.BoundsMin = new float3(a[0], 0, a[1]);
                    info.BoundsMax = new float3(a[2], a[4], a[3]);
                }
                if (p.Value.TryGetProperty("w", out var w)) info.NetWidth = (float)w.GetDouble();
                c.Items[p.Name] = info;
            }
            return c;
        }

        public IReadOnlyList<string> Pool(string category)
        {
            while (!string.IsNullOrEmpty(category))
            {
                if (Pools.TryGetValue(category, out var l)) return l;
                int dot = category.LastIndexOf('.');
                if (dot <= 0) break;
                category = category.Substring(0, dot);
            }
            return Array.Empty<string>();
        }

        public AutoAssetSource Source() => new AutoAssetSource
        {
            Pool = Pool,
            Exists = id => Items.ContainsKey(id) || DumpIds.Contains(id) || Pools.Values.Any(l => l.Contains(id)),
            Palettes = PaletteNames,
            PaletteItems = (p, cat) => p != null && cat != null && Palettes.TryGetValue(p, out var m) && m.TryGetValue(cat, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>(),
        };
    }

    // ───────────────────────── what to render ─────────────────────────

    public sealed class Job
    {
        public string Type, Variant, Title;
        /// <summary>(s20) Shore site kind for water renders (ShoreSites.Kinds), null = the old flat fixture.</summary>
        public string Shore;
        /// <summary>(s25) Sloped land site kind (SlopeSites.Kinds), null = flat ground.</summary>
        public string Slope;
        /// <summary>(s34) Road-scale site kind (RoadSites.Kinds) for the roads type, null = the default site.</summary>
        public string RoadSite;
        public Recipe Recipe;
        public float TargetArea;
        /// <summary>Site shape (--shapes): "" = the default irregular site, else triangle, sliver, square, L.</summary>
        public string Shape = "";
        /// <summary>(s50) Commercial review site (CommercialSites.Kinds), null = the default / shaped site.</summary>
        public string Site;
        /// <summary>(s33) Map palette for this job (PaintView.ForMap), null = --map / vanilla.</summary>
        public string Map;
        /// <summary>(s33) A hand-built plan instead of a recipe (the toolkit sheet).</summary>
        public Func<(float3[] ring, ComposedPlan plan)> Custom;
    }

    public static class Variants
    {
        /// <summary>--areas 1,3,6: render every job at each of these sizes (hectares) instead of the default size.</summary>
        public static float[] Areas;
        /// <summary>--shapes triangle,sliver,square,L: render every job on each of these site shapes (s18).</summary>
        public static string[] Shapes;
        /// <summary>(s50) --sites square,halfcurved,curved: every job on each of these commercial review sites (CommercialSites: streets all round, captured zone blocks).</summary>
        public static string[] Sites;
        /// <summary>--palettes: one image per ground palette for every type with a "palette" control (s18).</summary>
        public static bool Palettes;
        /// <summary>--presets a,b: only these preset variants (plus default) — keeps size/shape sweeps small.</summary>
        public static HashSet<string> PresetFilter;
        /// <summary>--no-features: skip the all/none feature variants.</summary>
        public static bool NoFeatureVariants;
        /// <summary>(s31) --terrain: every job also with the Terrain feature on ("rolling" hills), suffix "+terrain".</summary>
        public static bool TerrainVariants;
        /// <summary>(s31) --no-harmony: compose without the site harmony pass (to see the old behaviour).</summary>
        public static bool NoHarmony;
        /// <summary>--no-shops: street shops off everywhere (judge a type's own layout on a site with streets all round).</summary>
        public static bool NoShops;
        /// <summary>--shops: one image per street-shop layout/amount (s12) for every type that hosts street shops.</summary>
        public static bool ShopVariants;
        /// <summary>--decals: Decals amount at 300 % and 0 % for every type with decal dressing (s21).</summary>
        public static bool DecalVariants;
        /// <summary>(s33) --paint: ground paint variants (lawn surface, gritty 200 %, off, arid map) per type with Ground paint, plus the toolkit sheet.</summary>
        public static bool PaintVariants;
        private static readonly (string layout, string amount)[] ShopCombos =
        {
            // s14: the Zoning feature is plain cutouts (Row / Speckled); the elaborate layouts are the Commercial types.
            ("band", "few"), ("band", "some"), ("band", "lots"), ("pads", "some"), ("pads", "lots"),
        };

        /// <summary>(s33) Paint views hide the decals (their squares would cover the ground paint being reviewed).</summary>
        private static void NoDecals(Recipe r) => r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.AmountId] = 0.0;

        public static List<Job> All(string repo, HashSet<string> only)
        {

            var editor = new DesignEditor(GeneratorRegistry.Get);
            var recipes = new List<(string id, Recipe r)>();
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(Path.Combine(repo, "Recipes"), "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    var rf = RecipeJson.Parse(File.ReadAllText(f));
                    recipes.Add((string.IsNullOrEmpty(rf.Recipe.Id) ? Path.GetFileNameWithoutExtension(f) : rf.Recipe.Id, rf.Recipe));
                    roots.Add(rf.Recipe.Root.Layer);
                }
                catch (Exception e) { Console.WriteLine($"Recipe {f}: {e.Message}"); }
            }
            foreach (var g in GeneratorRegistry.All)
            {
                var attr = (AreaGeneratorAttribute)Attribute.GetCustomAttribute(g.GetType(), typeof(AreaGeneratorAttribute));
                if (g.Kind != LayerKind.Fill || (attr?.Hidden ?? false) || roots.Contains(g.Id) || recipes.Any(r => r.id == g.Id)) continue;
                recipes.Add((g.Id, Recipe.Single(g, 1)));
            }

            var jobs = new List<Job>();
            foreach (var (id, template) in recipes)
            {
                if (only != null && !only.Contains(id)) continue;
                var gen = GeneratorRegistry.Get(template.Root.Layer);
                if (gen == null) continue;
                float area = TargetArea(gen);
                Job Make(string variant, string title, Action<Recipe> edit)
                {
                    var r = template.Clone();
                    editor.ApplyDefaults(r);
                    uint n = 0;
                    editor.AddDefaultFeatures(r, () => ++n);
                    edit?.Invoke(r);
                    if (NoShops)
                        foreach (var f in Features.Of(gen))
                            if (f.Target == Placemaker.Core.Layers.Common.StreetFrontage.ShopsTag) editor.SetFeature(r, RecipePaths.Root, f.Layer, false, 0);
                    uint k = 0;
                    foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
                    return new Job { Type = id, Variant = variant, Title = title, Recipe = r, TargetArea = area };
                }
                jobs.Add(Make("default", "Default", null));
                IReadOnlyList<PresetInfo> presets;
                try { presets = gen.Presets ?? Array.Empty<PresetInfo>(); } catch (Exception) { presets = Array.Empty<PresetInfo>(); }
                foreach (var p in presets)
                {
                    if (p == null || string.IsNullOrEmpty(p.Id)) continue;
                    if (PresetFilter != null && !PresetFilter.Contains(p.Id)) continue;
                    jobs.Add(Make("preset-" + Safe(p.Id), "Preset " + p.Id, r => editor.ApplyPreset(r, RecipePaths.Root, p.Id)));
                }
                if (DecalVariants && gen.Controls.Any(c => c.Id == Placemaker.Core.Decals.DressingControls.AmountId))
                {
                    jobs.Add(Make("decals-max", "Decals 300 %", r => r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.AmountId] = 300.0));
                    // s23: decals are opt-in (default 0): "on" = what a player gets switching them on at 100 %.
                    jobs.Add(Make("decals-on", "Decals 100 %", r => r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.AmountId] = 100.0));
                    jobs.Add(Make("decals-gritty", "Decals gritty", r => { r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.AmountId] = 100.0; r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.StyleId] = "gritty"; }));
                    // s28: terrain-paint ground wear only shows on bare terrain: "wear-bare" turns the type's lawn surface off.
                    jobs.Add(Make("wear-off", "Ground wear 0 %", r => r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.PaintWearId] = 0.0));
                    if (gen.Controls.Any(c => c.Id == Placemaker.Core.Decals.DressingControls.LawnId))
                        jobs.Add(Make("wear-bare", "Ground wear, bare lawn", r => r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.LawnId] = "painted"));
                }
                // s33: ground paint — the lawn choice both ways, gritty and heavy, off, and the same design on an arid map.
                if (PaintVariants && gen.Controls.Any(c => c.Id == Placemaker.Core.Decals.DressingControls.PaintWearId))
                {
                    string lawnId = Placemaker.Core.Decals.DressingControls.LawnId;
                    if (gen.Controls.Any(c => c.Id == lawnId))
                    {
                        jobs.Add(Make("paint-painted", "Lawn: painted terrain", r => { NoDecals(r); r.Root.Settings.Values[lawnId] = "painted"; }));
                        jobs.Add(Make("paint-surface", "Lawn: grass surface", r => { NoDecals(r); r.Root.Settings.Values[lawnId] = "surface"; }));
                    }
                    jobs.Add(Make("paint-gritty", "Ground paint 200 %, gritty", r =>
                    {
                        NoDecals(r);
                        r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.PaintWearId] = 200.0;
                        r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.StyleId] = "gritty";
                    }));
                    if (gen.Controls.Any(c => c.Id == "trailStyle"))
                        jobs.Add(Make("paint-trails", "Painted trails + worn shortcuts", r =>
                        {
                            NoDecals(r);
                            r.Root.Settings.Values["trails"] = 3.0;
                            r.Root.Settings.Values["trailStyle"] = "painted";
                            r.Root.Settings.Values["desireLines"] = "worn";
                        }));
                    jobs.Add(Make("paint-off", "Ground paint 0 %"
, r => { NoDecals(r); r.Root.Settings.Values[Placemaker.Core.Decals.DressingControls.PaintWearId] = 0.0; }));
                    var arid = Make("paint-arid", "Arid map (custom slots)", NoDecals);
                    arid.Map = "arid";
                    jobs.Add(arid);
                }
                var paletteControl = (gen.Controls ?? Array.Empty<ControlSchema>()).FirstOrDefault(c => c.Id == "palette" && c.Kind == ControlKind.Choice);

                if (Palettes && paletteControl != null)
                    foreach (var o in paletteControl.Options.Where(o => o.Value != "auto" && o.Value != "custom"))
                    {
                        string pv = o.Value;
                        jobs.Add(Make("palette-" + Safe(pv), "Palette " + pv, r => editor.SetValue(r, RecipePaths.Root, "palette", pv, true)));
                    }
                // s16: every road mix on a grid with an avenue column + row (so avenues and a roundabout show up).
                if (gen is ILinkedControls && gen.Controls.Any(c => c.Id == RoadsIds.RoadMix))
                    foreach (var mix in RoadMixes.All)
                        jobs.Add(Make("mix-" + Safe(mix.Id), "Road mix " + mix.Id, r =>
                        {
                            editor.SetValue(r, RecipePaths.Root, RoadsIds.Pattern, "grid", true);
                            editor.SetValue(r, RecipePaths.Root, RoadsIds.AvenueColumn, true, true);
                            editor.SetValue(r, RecipePaths.Root, RoadsIds.AvenueRow, true, true);
                            editor.SetValue(r, RecipePaths.Root, RoadsIds.RoadMix, mix.Id, true);
                        }));
                // s34: "Pre-zone new roads" (the village preset with low housing).
                if (gen.Controls.Any(c => c.Id == RoadsIds.PreZone) && gen.Presets.Any(p => p.Id == "village"))
                    jobs.Add(Make("prezone", "Village, pre-zoned (low housing)", r =>
                    {
                        editor.ApplyPreset(r, RecipePaths.Root, "village");
                        editor.SetValue(r, RecipePaths.Root, RoadsIds.PreZone, "residentialLow", true);
                    }));
                var features = Features.Of(gen);
                if (ShopVariants && features.Any(f => f.Layer == "edgeZoning" && f.Target == "shops"))
                    foreach (var (layout, amount) in ShopCombos)
                        jobs.Add(Make("shops-" + layout + "-" + amount, "Zoning " + layout + " / " + amount, r =>
                        {
                            editor.SetFeature(r, RecipePaths.Root, "edgeZoning", true, 91);
                            r.Root.Settings.Values["shopLayout"] = layout;
                            r.Root.Settings.Values["shopAmount"] = amount;
                        }));
                if (features.Count > 0 && !Renderer.Showcase && !NoFeatureVariants)
                {
                    jobs.Add(Make("features-all", "All features on", r =>
                    {
                        foreach (var f in features) editor.SetFeature(r, RecipePaths.Root, f.Layer, true, 77);
                    }));
                    jobs.Add(Make("features-none", "No features", r =>
                    {
                        foreach (var f in features) editor.SetFeature(r, RecipePaths.Root, f.Layer, false, 0);
                    }));
                }
            }
            if (PaintVariants && (only == null || only.Contains("_paint")))
                jobs.Add(new Job { Type = "_paint", Variant = "toolkit", Title = "Paint toolkit", Custom = PaintView.Toolkit, TargetArea = 17600f });
            if (Areas != null && Areas.Length > 0)
            {
                var expanded = new List<Job>();
                foreach (var j in jobs)
                    foreach (var ha in Areas)
                        expanded.Add(new Job { Type = j.Type, Variant = j.Variant + "-" + ha.ToString("0.###", CultureInfo.InvariantCulture) + "ha", Title = j.Title + " " + ha + " ha", Recipe = j.Recipe.Clone(), TargetArea = ha * 10000f });
                jobs = expanded;
            }
            if (Sites != null && Sites.Length > 0)
            {
                // (s50) The commercial review sites: square (the job's area, 2.9 ha unless --areas), half curved, fully curved.
                var expanded = new List<Job>();
                foreach (var j in jobs)
                    foreach (var s in Sites)
                        expanded.Add(new Job { Type = j.Type, Variant = s + "-" + j.Variant, Title = s + " " + j.Title, Recipe = j.Recipe.Clone(), TargetArea = Areas != null && Areas.Length > 0 ? j.TargetArea : 29000f, Site = s });
                jobs = expanded;
            }
            if (Shapes != null && Shapes.Length > 0)
            {
                var expanded = new List<Job>();
                foreach (var j in jobs)
                    foreach (var sh in Shapes)
                        expanded.Add(new Job { Type = j.Type, Variant = sh + "-" + j.Variant, Title = sh + " " + j.Title, Recipe = j.Recipe.Clone(), TargetArea = j.TargetArea, Shape = sh });
                jobs = expanded;
            }
            if (Renderer.Shores != null && Renderer.Shores.Length > 0 && Renderer.WaterTypes != null)
            {
                var expanded = new List<Job>();
                foreach (var j in jobs)
                {
                    if (!Renderer.WaterTypes.Contains(j.Type)) { expanded.Add(j); continue; }
                    foreach (var s in Renderer.Shores)
                        expanded.Add(new Job { Type = j.Type, Variant = j.Variant + "~" + s, Title = j.Title + " (" + s + " shore)", Recipe = j.Recipe.Clone(), TargetArea = j.TargetArea, Shore = s });
                }
                jobs = expanded;
            }
            if (TerrainVariants)
            {
                var expanded = new List<Job>();
                foreach (var j in jobs)
                {
                    expanded.Add(j);
                    var r = j.Recipe.Clone();
                    var ed = new DesignEditor(GeneratorRegistry.Get);
                    if (!ed.SetFeature(r, RecipePaths.Root, "terrain", true, 4242u))
                    {
                        var tg = GeneratorRegistry.Get("terrain");
                        if (tg == null) continue;
                        r.Root.Children.Add(new RecipeStep { Layer = "terrain", Seed = 4242u, Settings = tg.Defaults() });
                    }
                    foreach (var st in r.Root.Children)
                        if (st.Layer == "terrain")
                        {
                            st.Enabled = true;
                            st.Settings.Values["shape"] = "hills"; st.Settings.Values["height"] = 3.0; st.Settings.Values["scale"] = 45.0;
                        }
                    expanded.Add(new Job { Type = j.Type, Variant = j.Variant + "+terrain", Title = j.Title + " + rolling terrain", Recipe = r, TargetArea = j.TargetArea, Shape = j.Shape, Shore = j.Shore });
                }
                jobs = expanded;
            }
            if (Renderer.RoadSiteKinds != null && Renderer.RoadSiteKinds.Length > 0)
            {
                // (s34) Roads on road-scale sites: 30 ha (unless --areas), two existing streets, a farm, real relief.
                var expanded = new List<Job>();
                foreach (var j in jobs)
                {
                    if (j.Type != "roads") { expanded.Add(j); continue; }
                    if (j.Variant.StartsWith("mix-") && (PresetFilter == null || !PresetFilter.Contains("mixes"))) continue;
                    foreach (var k in Renderer.RoadSiteKinds)
                        expanded.Add(new Job { Type = j.Type, Variant = j.Variant + "~" + k, Title = j.Title + " (" + k + ")", Recipe = j.Recipe.Clone(),
                            TargetArea = Areas != null && Areas.Length > 0 ? j.TargetArea : 300000f, Shape = j.Shape, RoadSite = k });
                }
                jobs = expanded;
            }
            if (Renderer.Slopes != null && Renderer.Slopes.Length > 0)
            {
                var expanded = new List<Job>();
                foreach (var j in jobs)
                {
                    if (j.Shore != null) { expanded.Add(j); continue; }
                    foreach (var s in Renderer.Slopes)
                        expanded.Add(new Job { Type = j.Type, Variant = j.Variant + "~" + s, Title = j.Title + " (" + s + " slope)", Recipe = j.Recipe.Clone(), TargetArea = j.TargetArea, Shape = j.Shape, Slope = s, Site = j.Site });
                }
                jobs = expanded;
            }
            return jobs;
        }

        private static float TargetArea(IAreaGenerator g)
        {
            float min = g.MinArea > 0 ? g.MinArea : 100f, max = g.MaxArea > 0 ? g.MaxArea : 1e7f;
            return math.clamp(math.max(12000f, min * 1.3f), min, max);
        }

        public static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            return sb.ToString();
        }
    }

    // ───────────────────────── compose + draw ─────────────────────────

    public sealed class Result
    {
        public string Summary = "", File = "";
        /// <summary>(perf) --bench: the composed plan (its Timings) and the site's area in m².</summary>
        public ComposedPlan Plan;
        public double Area;
        public double Ms;
        public int Objects, Nets, Surfaces, Errors, Warnings, BenchesOffPath, Overlaps, Outside, Decals, EdgeSpikes, BadSurfaces;
        /// <summary>(shops2) The first layer plan's facts (the composition's own numbers).</summary>
        public Dictionary<string, double> Facts = new Dictionary<string, double>();
        /// <summary>(s30) Glitch metrics of the final ground (curtain, net seats) for terrain renders.</summary>
        public string Glitch;
        public double PathMetres;
        public Dictionary<string, int> ByRole = new Dictionary<string, int>();
        public Dictionary<string, int> ByCategory = new Dictionary<string, int>();
        public List<string> IssueKeys = new List<string>();
        public string Failure;
        /// <summary>s16: the plan's roads by role (same legend as the panel).</summary>
        public List<RoadRoles.LegendEntry> RoadLegend = new List<RoadRoles.LegendEntry>();
        /// <summary>(s34) RoadsAudit line for roads jobs on road sites.</summary>
        public string RoadAudit;
    }

    public static class Renderer
    {
        private static readonly float2[] BaseRing =
        {
            new float2(0, 0), new float2(140, -6), new float2(152, 58), new float2(118, 102), new float2(46, 96), new float2(6, 70),
        };

        public static float3[] Site(float targetArea)
        {
            double a = 0;
            for (int i = 0, j = BaseRing.Length - 1; i < BaseRing.Length; j = i++) a += BaseRing[j].x * BaseRing[i].y - BaseRing[i].x * BaseRing[j].y;
            float s = (float)math.sqrt(targetArea / math.abs(a * 0.5));
            var ring = BaseRing.Select(p => new float3(p.x * s, 0, p.y * s)).ToArray();
            return Corners > ring.Length ? Wobbly(ring, Corners) : ring;
        }

        /// <summary>(perf) --corners N: the default site resampled to N corners with a ±0.4 m wobble, like a painted area's traced outline.</summary>
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

        /// <summary>Layout size the gallery renders at (--scale; default = the game option default, 0.5).</summary>
        public static float LayoutScale = SiteContext.DefaultLayoutScale;

        /// <summary>--showcase: square site with streets on all four sides; entrances from the planner's own suggestions.</summary>
        public static bool Showcase;
        /// <summary>
        /// --water a,b/preset-x (test-only fixture; the gallery site is otherwise dry): for these types (or type/variant) the south strip of the
        /// site (z &lt; 18 % of its depth) and everything beyond it is a lake. Land stays at 0 m, the water surface is at
        /// -0.6 m and the lake bed shelves down to -5 m; the street on that side is dropped. Lets shore types
        /// (beach, waterfront, wetland, lakeshore trails) find real water instead of an assumed side.
        /// </summary>
        public static HashSet<string> WaterTypes;
        /// <summary>(s20) --shores flat,bank,headland,cliff: every water job is rendered once per shore site (sloped ground, curved coast, 8 m water snapshot).</summary>
        public static string[] Shores;
        /// <summary>(s25) --slopes gentle,cross,steep,hilly: every land job is rendered once per sloped site (final ground, 3D view, sections).</summary>
        public static string[] Slopes;
        /// <summary>(s34) --roads flat,gentle,hill,valley,steep: road-scale sites for the roads type.</summary>
        public static string[] RoadSiteKinds;
        /// <summary>(perf) --bench: compose only (no drawing), one job at a time, timings per step (Bench.cs).</summary>
        public static bool BenchOnly;
        public static int BenchRepeat = 2;
        /// <summary>(s34) --size N: longest image side in pixels (default 880).</summary>
        public static float ImageSize = 880f;
        /// <summary>(sketch research) Also write each plan's geometry as &lt;variant&gt;.plan.json for the sketch mock-ups.</summary>
        public static bool PlanJson;
        /// <summary>(s34) --zoom fx0,fz0,fx1,fz1: render only this part of the site (fractions of its bounds).</summary>
        public static float[] Zoom;
        [ThreadStatic] private static float _contourStep;
        [ThreadStatic] private static List<Obstacle> _obstacles;
        [ThreadStatic] private static bool _sloped;
        [ThreadStatic] private static float _level;
        [ThreadStatic] private static Func<float2, float> _original;
        public const float WaterSurface = -0.6f;
        [ThreadStatic] private static float _waterZ;
        [ThreadStatic] private static List<NearbyNet> _roads;
        [ThreadStatic] private static Placemaker.Core.Layers.Roads.Terraces.TerraceGround _terraced;
        [ThreadStatic] private static List<EdgeMarker> _markers;
        [ThreadStatic] private static Placemaker.Core.Paint.TerrainPalette _palette;
        [ThreadStatic] private static bool _toolkit;
        [ThreadStatic] private static bool _paintView;
        /// <summary>(s33) --map arid|mediterranean|tropic|alpine|shuffled: plan and draw with that map's paint materials.</summary>
        public static string MapPalette;
        /// <summary>
        /// (rf-shops3) --markers kind@side:t,…: the entrances a player places, on every job (side s/e/n/w of the site's box, t 0..1
        /// west→east or south→north, snapped to the outline), e.g. access@s:0.5,entrance@e:0.4,alley@w:0.6.
        /// </summary>
        public static string MarkerSpec;

        private static void ApplyMarkerSpec(Recipe r, float3[] ring)
        {
            if (string.IsNullOrEmpty(MarkerSpec) || r?.Root == null || r.Root.Markers.Count > 0) return;
            var region = Placemaker.Core.Layers.Common.LayerRegion.From(ring);
            if (region == null) return;
            foreach (var part in MarkerSpec.Split(','))
            {
                var at = part.Split('@');
                if (at.Length != 2) continue;
                var st = at[1].Split(':');
                float t = st.Length > 1 ? float.Parse(st[1], CultureInfo.InvariantCulture) : 0.5f;
                float2 lo = region.Min, hi = region.Max, p;
                switch (st[0])
                {
                    case "n": p = new float2(math.lerp(lo.x, hi.x, t), hi.y); break;
                    case "e": p = new float2(hi.x, math.lerp(lo.y, hi.y, t)); break;
                    case "w": p = new float2(lo.x, math.lerp(lo.y, hi.y, t)); break;
                    default: p = new float2(math.lerp(lo.x, hi.x, t), lo.y); break;
                }
                var q = region.ClosestOnBoundary(p, out int seg, out float rt);
                r.Root.Markers.Add(new EdgeMarker { Kind = at[0], RingSegment = seg, RingT = rt, Position = new float3(q.x, 0f, q.y) });
            }
        }

        /// <summary>(s18) Odd sites: a triangle, a long thin trapezoid, a square, an L (CCW in x/z, scaled to the area).</summary>
        public static float3[] Shaped(string shape, float targetArea)
        {
            float2[] b;
            switch ((shape ?? "").ToLowerInvariant())
            {
                case "triangle": b = new[] { new float2(0, 0), new float2(100, 0), new float2(28, 72) }; break;
                case "sliver": b = new[] { new float2(0, 0), new float2(120, 0), new float2(104, 26), new float2(14, 26) }; break;
                case "square": return Square(targetArea);
                case "l": b = new[] { new float2(0, 0), new float2(60, 0), new float2(60, 26), new float2(26, 26), new float2(26, 60), new float2(0, 60) }; break;
                default: return Site(targetArea);
            }
            double a = 0;
            for (int i = 0, j = b.Length - 1; i < b.Length; j = i++) a += b[j].x * b[i].y - b[i].x * b[j].y;
            float s = (float)math.sqrt(targetArea / math.abs(a * 0.5));
            return b.Select(p => new float3(p.x * s, 0, p.y * s)).ToArray();
        }

        public static float3[] Square(float targetArea)
        {
            float a = (float)math.sqrt(targetArea);
            return new[] { new float3(0, 0, 0), new float3(a, 0, 0), new float3(a, 0, a), new float3(0, 0, a) };
        }

        /// <summary>A 12 m road with sidewalks just outside each side (centre 8 m off the outline).</summary>
        public static List<NearbyNet> RoadsAround(float3[] ring)
        {
            var nets = new List<NearbyNet>();
            for (int i = 0; i < ring.Length; i++)
            {
                float3 p0 = ring[i], p1 = ring[(i + 1) % ring.Length];
                float3 d = math.normalize(p1 - p0);
                float3 n = new float3(d.z, 0, -d.x) * 8f;   // outward for a CCW ring in XZ
                float3 a = p0 - d * 20f + n, b = p1 + d * 20f + n;
                nets.Add(new NearbyNet { ExternalId = 9000 + i, A = a, B = math.lerp(a, b, 1f / 3f), C = math.lerp(a, b, 2f / 3f), D = b, Width = 12f, Category = NetCategory.Road, HasSidewalk = true });
            }
            return nets;
        }

        /// <summary>Use the layer's own suggestions as the placed markers (what a player gets by accepting the hints).</summary>
        private static void ApplySuggestedMarkers(Recipe r, float3[] ring, SiteContext site)
        {
            var step = r.Root;
            var gen = GeneratorRegistry.Get(step.Layer);
            if (gen == null || step.Markers.Count > 0) return;
            try
            {
                var input = new AreaInput { Shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn }, Site = site, Settings = step.Settings, Markers = step.Markers };
                var sug = gen.SuggestMarkers(input);
                if (sug != null) step.Markers.AddRange(sug);
            }
            catch (Exception) { }
        }

        public static Result Run(Job job, Catalog catalog, string outDir)
        {
            var res = new Result();
            bool shaped = !string.IsNullOrEmpty(job.Shape);
            SiteContext siteKind = null;
            var ring = job.Site != null ? CommercialSites.Make(job.Site, job.TargetArea, out siteKind) : shaped ? Shaped(job.Shape, job.TargetArea) : Showcase ? Square(job.TargetArea) : Site(job.TargetArea);
            (float3[] ring, ComposedPlan plan) custom = default;
            if (job.Custom != null) { custom = job.Custom(); ring = custom.ring; }
            _toolkit = job.Custom != null;
            _paintView = _toolkit || (job.Variant ?? "").StartsWith("paint-", StringComparison.Ordinal);
            var shape = new AreaShape { Ring = ring, Source = AreaSource.Drawn };
            var site = new SiteContext { Assets = catalog.Items, LayoutScale = LayoutScale };
            string map = job.Map ?? MapPalette;
            _palette = PaintView.ForMap(map);
            if (!string.IsNullOrEmpty(map)) site.Terrain = _palette;
            if (siteKind != null) { site.NearbyNets = siteKind.NearbyNets; site.ZoneGrids = siteKind.ZoneGrids; }
            else if (Showcase || shaped) site.NearbyNets = RoadsAround(ring);
            _waterZ = float.NaN;
            if (WaterTypes != null && (WaterTypes.Contains(job.Type) || WaterTypes.Contains(job.Type + "/" + job.Variant)))
            {
                float zMin = ring.Min(p => p.z), zMax = ring.Max(p => p.z), wz = zMin + (zMax - zMin) * 0.18f;
                _waterZ = wz;
                Func<float2, float> bed = p => p.y >= wz ? 0f : math.max(-5f, -1f - (wz - p.y) * 0.12f);
                site.HeightAt = bed;
                site.WaterDepthAt = p => p.y >= wz ? 0f : WaterSurface - bed(p);
                site.NearbyNets = site.NearbyNets.Where(n => math.max(n.A.z, n.D.z) > zMin + 0.5f).ToList();
                if (job.Shore != null)
                {
                    // s20: a real shore — curved coast, sloped ground, the game's coarse water snapshot.
                    var g = ShoreSites.Ground(ring, job.Shore);
                    site.HeightAt = g;
                    site.WaterDepthAt = ShoreSites.Depth(ring, g, job.Shore);
                }
            }
            _sloped = (job.Slope != null || (job.RoadSite != null && job.RoadSite != "flat")) && float.IsNaN(_waterZ);
            _contourStep = job.RoadSite != null ? 2f : 1f;
            _obstacles = null;
            if (job.RoadSite != null)
            {
                site.NearbyNets = RoadSites.Streets(ring);
                site.Obstacles = RoadSites.Farm(ring);
                _obstacles = site.Obstacles;
            }
            if (_sloped)
            {
                var g = job.RoadSite != null ? RoadSites.Ground(ring, job.RoadSite) : SlopeSites.Ground(ring, job.Slope);
                site.HeightAt = g;
                if (site.NearbyNets != null)
                    for (int k = 0; k < site.NearbyNets.Count; k++)
                    {
                        var n = site.NearbyNets[k];
                        // (s41) fill / cut: flat ground, the streets on a 1.2 m embankment / in a 1 m cutting.
                        float off = job.Slope == "fill" ? 1.2f : job.Slope == "cut" ? -1f : 0f;
                        n.A.y = g(n.A.xz) + off; n.B.y = g(n.B.xz) + off; n.C.y = g(n.C.xz) + off; n.D.y = g(n.D.xz) + off;
                        site.NearbyNets[k] = n;
                    }
            }
            _level = float.IsNaN(_waterZ) ? float.NaN : job.Shore != null ? ShoreSites.Level : WaterSurface;
            _original = site.HeightAt;
            _roads = site.NearbyNets;
            ComposedPlan plan;
            var sw = Stopwatch.StartNew();
            try
            {
                if (job.Custom != null) plan = custom.plan;
                else
                {
                    ApplyMarkerSpec(job.Recipe, ring);
                    if (Showcase || shaped || siteKind != null) ApplySuggestedMarkers(job.Recipe, ring, site);
                    AddMarkersIfNeeded(job.Recipe, ring);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    Placemaker.Core.Build.SiteHarmony.Disabled = Variants.NoHarmony;
                    // (s52) Keep the terraced ground of a roads plan for the protection map.
                    _terraced = null;
                    Placemaker.Core.Layers.Roads.Terraces.TerraceField.Inspect = g => _terraced = g;
                    plan = Composer.Compose(job.Recipe, shape, site, cts.Token);
                    AutoAssets.Resolve(plan, catalog.Source());
                    Placemaker.Core.Decals.DecalFit.Orient(plan, catalog.Items);
                }
            }
            catch (Exception e)
            {
                res.Failure = e.GetType().Name + ": " + e.Message;
                res.Summary = "FAILED " + res.Failure;
                plan = new ComposedPlan();
            }
            res.Ms = sw.Elapsed.TotalMilliseconds;
            if (plan.Layers.Count > 0) foreach (var kv in plan.Layers[0].Facts) res.Facts[kv.Key] = kv.Value;
            if (BenchOnly)
            {
                res.Plan = plan;
                res.Area = Placemaker.Core.Geometry.Poly.Area(ring.Select(p => p.xz).ToArray());
                return res;
            }

            var dir = Path.Combine(outDir, job.Type);
            Directory.CreateDirectory(dir);
            res.File = job.Type + "/" + job.Variant + ".png";
            _markers = job.Recipe?.Root?.Markers;
            Draw(plan, ring, catalog, Path.Combine(dir, job.Variant + ".png"), res);
            if (res.Failure == null)
                res.Summary = $"{res.Objects} obj, {res.Nets} nets ({res.PathMetres:0} m), {res.Surfaces} surf, {res.Errors} err, {res.Warnings} warn, {res.EdgeSpikes} spikes, {res.Ms:0} ms";
            if (res.Failure == null && job.RoadSite != null)
            {
                // (s34) The roads review numbers: connected?, bare dead ends, main-road junction gaps, walkways, grades.
                var rl = plan.Layers.FirstOrDefault(l => l != null && l.GeneratorId == "roads");
                if (rl != null)
                {
                    // Grades on the ground the build leaves (s52: terraced roads lie on the terraces, not the hillside).
                    var audit = RoadsAudit.Measure(rl, job.RoadSite == "flat" ? null : rl.HeightAfter ?? site.HeightAt, 0.1f, site.NearbyNets != null && site.NearbyNets.Count > 0, ring.Select(p => p.xz).ToArray());
                    res.Summary += " | " + audit + " | " + string.Join(" ", rl.Facts.Where(kv => kv.Key == "roads.accessCuts" || kv.Key == "roads.links" || kv.Key == "roads.lanes").Select(kv => kv.Key.Substring(6) + "=" + kv.Value)) + " | " + string.Join(" ", rl.Issues.Select(i => i.Key.Replace("Placemaker.Roads.Issue.", "") + (i.Args != null && i.Args.Length > 0 ? "(" + string.Join(",", i.Args) + ")" : "")).Distinct());
                    res.RoadAudit = audit.ToString();
                    // (s52) Hillside terraces: the terrace facts and the glitch metrics of the ground they leave.
                    if (rl.Facts.ContainsKey("terraces.sloped"))
                        res.Summary += " | " + string.Join(" ", rl.Facts.Where(kv => kv.Key.StartsWith("terraces.")).Select(kv => kv.Key.Substring(9) + "=" + kv.Value.ToString("0.##", CultureInfo.InvariantCulture)))
                            + (res.Glitch != null ? " | " + res.Glitch : "")
                            + (_terraced?.Layout != null ? " | planned walks " + _terraced.Layout.Walks.Count + " | rejects " + string.Join(", ", _terraced.Layout.Rejects.OrderByDescending(kv => kv.Key.Contains("dropped") ? 1000 : kv.Value).Take(8).Select(kv => kv.Key + " " + kv.Value)) : "");
                }
            }
            if (res.Failure == null && job.Slope != null)
            {
                var gf = plan.Layers.Where(l => l != null).SelectMany(l => l.Facts).Where(kv => kv.Key.StartsWith("grade") || kv.Key.StartsWith("terrain")).Select(kv => kv.Key + "=" + kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
                res.Summary += " | " + string.Join(" ", gf) + " tf " + plan.Layers.Where(l => l != null).Sum(l => l.Terraform.Count);
                // (s31) Site harmony metrics on the ground the build leaves.
                try { res.Summary += " | " + Placemaker.Core.Build.SiteHarmony.Measure(plan, site); } catch (Exception) { }
            }
            if (res.Failure == null && job.Shore != null && plan.Layers.Count > 0 && plan.Layers[0] != null)
            {
                var fx = plan.Layers[0].Facts;
                string F(string k) => fx.TryGetValue(k, out var v) ? v.ToString("0.##", CultureInfo.InvariantCulture) : "-";
                res.Summary += $" | walk r {F("walkMinRadius")} wiggles {F("walkWiggles")} grade {F("walkMaxGrade")} edge r {F("edgeMinRadius")} runs {F("edgeRuns")} rails {F("railingRuns")} wall {F("hillsideWall")}/{F("wallHeightMax")} | {res.Glitch} | steps {F("steps")} links {F("edgeLinks")} beds {F("beds")} piers {F("piers")} | cliff {F("shoreCliffShare")} lookouts {F("lookouts")} cut {F("earthMaxCut")} fill {F("earthMaxFill")} rough {F("terrainRoughness")} | " + string.Join(" ", plan.AllIssues().Where(i => i != null && i.Severity != IssueSeverity.Info || (i != null && i.Key.Contains("Shore"))).Select(i => i.Key.Replace("Placemaker.", "")).Distinct());
            }
            return res;
        }

        /// <summary>A layer that is not ready without markers (parking needs an entrance) gets one on the first edge.</summary>
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
                // Water sites: the entrance on the landward edge (the first edge runs along the water).
                int seg = 0;
                if (!float.IsNaN(_waterZ)) { float best = float.MinValue; for (int e = 0; e < ring.Length; e++) { float z = (ring[e].z + ring[(e + 1) % ring.Length].z) * 0.5f; if (z > best) { best = z; seg = e; } } }
                var m = new EdgeMarker { Kind = kind, RingSegment = seg, RingT = 0.5f, Position = (ring[seg] + ring[(seg + 1) % ring.Length]) * 0.5f };
                step.Markers.Add(m);
                try { if (gen.CheckReady(input).Ready) return; } catch (Exception) { return; }
                step.Markers.Remove(m);
            }
        }

        // ─── palette ───


        /// <summary>
        /// Distinct stone tones for paved surfaces: one per paving ASSET (art pass a2), so a plaza pattern of
        /// several pavings reads as a pattern instead of one flat colour. Light enough for objects on top.
        /// </summary>
        private static readonly Rgba[] PavingTones =
        {
            Rgba.Hex("#d6cfbf"), Rgba.Hex("#c88a5e"), Rgba.Hex("#a9adb3"), Rgba.Hex("#d8c08a"), Rgba.Hex("#b07a63"), Rgba.Hex("#8f949b"),
            Rgba.Hex("#e8dcc0"), Rgba.Hex("#9fae9a"), Rgba.Hex("#c6a6a0"), Rgba.Hex("#b9a58a"), Rgba.Hex("#7f7a72"), Rgba.Hex("#cdb4d6"),
        };

        private static bool IsPaved(PlannedSurface s)
        {
            string cat = s.Category ?? "";
            if (cat.StartsWith("surface.paving") || cat.StartsWith("surface.clay") || cat.StartsWith("surface.gravel") || cat.StartsWith("surface.rock")) return true;
            if (cat.StartsWith("surface.crop") || cat.StartsWith("surface.sand") || cat.StartsWith("surface.dirt") || cat.StartsWith("surface.water")
                || cat.StartsWith("surface.grass") || cat.StartsWith("surface.bark")) return false;
            return s.Role == SurfaceRole.Paving || s.Role == SurfaceRole.Plaza || s.Role == SurfaceRole.Decorative;
        }

        /// <summary>The surface's asset id (automatic surfaces: its category and role, one tone per automatic kind).</summary>
        private static string SurfaceKey(PlannedSurface s) =>
            !string.IsNullOrEmpty(s.Asset.Id) ? s.Asset.Id : "auto:" + (s.Category ?? "") + ":" + s.Role;

        /// <summary>
        /// Colour of a surface. Paved surfaces get a tone per asset: a stable hash picks the start tone, and
        /// linear probing keeps every asset of one image distinct (up to the palette size).
        /// </summary>
        private static Rgba SurfaceColor(PlannedSurface s, Dictionary<string, int> tones)
        {
            // s18: a known paving asset draws in its real tone (icon mean, lifted so objects on top stay visible).
            if (IsPaved(s) && Placemaker.Core.Layers.Common.GroundSurfaces.Tone(s.Asset.Id) is string tone)
                return Lift(Rgba.Hex(tone));
            if (tones != null && IsPaved(s))
            {
                var key = SurfaceKey(s);
                if (!tones.TryGetValue(key, out int idx))
                {
                    uint h = 2166136261u;
                    foreach (char ch in key) h = (h ^ ch) * 16777619u;
                    idx = (int)(h % (uint)PavingTones.Length);
                    var used = new HashSet<int>(tones.Values);
                    for (int n = 0; n < PavingTones.Length && used.Contains(idx); n++) idx = (idx + 1) % PavingTones.Length;
                    tones[key] = idx;
                }
                return PavingTones[idx];
            }
            return SurfaceColor(s);
        }

        private static Rgba Lift(Rgba c) => new Rgba(c.R + (1f - c.R) * 0.3f, c.G + (1f - c.G) * 0.3f, c.B + (1f - c.B) * 0.3f, 1f);

        private static Rgba SurfaceColor(PlannedSurface s)
        {
            string cat = s.Category ?? "";
            if (cat.StartsWith("surface.crop"))
            {
                switch (cat)
                {
                    case "surface.crop.soil": return Rgba.Hex("#8d6c4c");
                    case "surface.crop.plowed": return Rgba.Hex("#76573b");
                    case "surface.crop.rows": return Rgba.Hex("#7d8a4e");
                    case "surface.crop.green": return Rgba.Hex("#86b856");
                    case "surface.crop.straw": return Rgba.Hex("#dcc276");
                    case "surface.crop.lavender": return Rgba.Hex("#9a86c8");
                    case "surface.crop.yellow": return Rgba.Hex("#e3d24e");
                    case "surface.crop.flower": return Rgba.Hex("#d39cc0");
                    default: return Rgba.Hex("#a99063");
                }
            }
            if (cat.StartsWith("surface.sand")) return Rgba.Hex("#ead9a2");
            if (cat.StartsWith("surface.gravel")) return Rgba.Hex("#c9c2b2");
            if (cat.StartsWith("surface.dirt")) return Rgba.Hex("#b9976a");
            if (cat.StartsWith("surface.bark")) return Rgba.Hex("#93704c");
            if (cat.StartsWith("surface.clay")) return Rgba.Hex("#c88a5e");
            if (cat.StartsWith("surface.rock")) return Rgba.Hex("#9e9e9e");
            if (cat.StartsWith("surface.water")) return Rgba.Hex("#7fb2e5");
            switch (s.Role)
            {
                case SurfaceRole.Grass: return Rgba.Hex("#b5d99a");
                case SurfaceRole.Paving: return Rgba.Hex("#d6cfbf");
                case SurfaceRole.Plaza: return Rgba.Hex("#e2d6bb");
                case SurfaceRole.Asphalt: return Rgba.Hex("#76767c");
                case SurfaceRole.Water: return Rgba.Hex("#7fb2e5");
                case SurfaceRole.Decorative: return Rgba.Hex("#e3aacb");
                default: return Rgba.Hex("#cdb98f");
            }
        }

        private static bool Invisible(PlannedNet n) => n.Category == AssetCategories.InvisiblePath && (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide);

        private static (Rgba fill, Rgba edge) NetColors(PlannedNet n)

        {
            string cat = n.Category ?? "";
            if (cat == "net.bike") return (Rgba.Hex("#e6b3a8"), Rgba.Hex("#9c5a4c"));
            if (cat.Contains("trail")) return (Rgba.Hex("#d9c39a"), Rgba.Hex("#8c7550"));
            switch (n.Role)
            {
                case NetRole.Avenue: case NetRole.Street: case NetRole.PerimeterRoad: case NetRole.CulDeSac: case NetRole.Roundabout: case NetRole.Alley:
                    return (Rgba.Hex("#8d8d93"), Rgba.Hex("#4f4f55"));
                case NetRole.DriveAisle: case NetRole.DriveConnector: return (Rgba.Hex("#a3adc4"), Rgba.Hex("#56648a"));
                case NetRole.Fence: return (Rgba.Hex("#7a5a3c"), Rgba.Hex("#7a5a3c"));
                default: return (Rgba.Hex("#f4e7c6"), Rgba.Hex("#a88c58"));
            }
        }

        private static Rgba ObjectColor(PlannedObject o, string cat)
        {
            if (cat.StartsWith("hedge")) return Rgba.Hex("#3f7f37");
            if (cat.StartsWith("flowerbed")) return Rgba.Hex("#d470a8");
            if (cat.StartsWith("playground") || cat.StartsWith("play")) return Rgba.Hex("#e8833a");
            if (cat.StartsWith("sports")) return Rgba.Hex("#d65a5a");
            if (cat.StartsWith("picnic")) return Rgba.Hex("#b06f3c");
            if (cat.StartsWith("rock")) return Rgba.Hex("#8a8a8a");
            if (cat.StartsWith("cemetery")) return Rgba.Hex("#9a9aa8");
            if (cat.StartsWith("memorial") || cat.StartsWith("monument")) return Rgba.Hex("#6c63b5");
            if (cat.StartsWith("kiosk") || cat.StartsWith("shelter") || cat.StartsWith("restroom")) return Rgba.Hex("#c0703f");
            switch (o.Role)
            {
                case ObjectRole.Tree: return Rgba.Hex("#3c8a3c");
                case ObjectRole.Bush: return Rgba.Hex("#6cb04c");
                case ObjectRole.Planter: return Rgba.Hex("#a07850");
                case ObjectRole.Bench: return Rgba.Hex("#7b4a1e");
                case ObjectRole.Lamp: return Rgba.Hex("#f0c419");
                case ObjectRole.Bin: return Rgba.Hex("#3a3a3a");
                case ObjectRole.Centerpiece: return Rgba.Hex("#2c78c8");
                case ObjectRole.Decal: return Rgba.Hex("#8f7a5a");
                case ObjectRole.ParkingBay: return Rgba.Hex("#ffffff");
                case ObjectRole.Charger: return Rgba.Hex("#22aa66");
                default: return Rgba.Hex("#7a6fb0");
            }
        }

        // ─── drawing ───

        /// <summary>
        /// (sketch research) The plan's geometry as JSON for the sketch mock-ups (tools/sketch-mockups): outline, streets
        /// around, nets at their real width, surfaces, areas, objects with footprint, zoning, markers, issues; each element
        /// with its legend layer (PreviewLegend.Of).
        /// </summary>
        private static void WritePlanJson(ComposedPlan plan, float3[] ring, Catalog cat, string file)
        {
            static float[] P(float2 p) => new[] { (float)Math.Round(p.x, 2), (float)Math.Round(p.y, 2) };
            static float[][] R(IEnumerable<float2> r) => r.Select(P).ToArray();
            var nets = new List<object>();
            var objects = new List<object>();
            var surfaces = new List<object>();
            var areas = new List<object>();
            var zoning = new List<object>();
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var n in l.Nets)
                    if (n != null)
                        nets.Add(new { role = n.Role.ToString(), layer = PreviewLegend.Of(n, l.GeneratorId), gen = l.GeneratorId, hw = HalfWidth(n, cat), pts = R(Flatten(n)), asset = n.Asset.Id ?? "" });
                foreach (var s in l.Surfaces)
                    if (s?.Ring != null && s.Ring.Length >= 3) surfaces.Add(new { role = s.Role.ToString(), layer = PreviewLegend.Of(s), ring = R(s.Ring.Select(p => p.xz)) });
                foreach (var a in l.Areas)
                    if (a?.Ring != null && a.Ring.Length >= 3) areas.Add(new { role = a.Role.ToString(), ring = R(a.Ring.Select(p => p.xz)) });
                foreach (var z in l.Zoning)
                    if (z != null) zoning.Add(new { zone = z.ZoneId ?? "", quad = R(new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz }) });
                foreach (var o in l.Objects)
                {
                    if (o == null) continue;
                    var (poly, r) = Footprint(o, cat);
                    objects.Add(new { role = o.Role.ToString(), layer = PreviewLegend.Of(o), cat = RoleCategories.ForObject(o) ?? "", pos = P(o.Position.xz), yaw = (float)Math.Round(o.Yaw, 3), r = (float)Math.Round(r, 2), poly = R(poly) });
                }
            }
            var cells = (plan.ZoneCells ?? new List<ZoneCellMark>()).Select(c => new { state = c.State.ToString(), zone = c.ZoneId ?? "", quad = R(c.Clipped ?? new[] { c.A, c.B, c.C, c.D }) }).ToList();
            var roads = (_roads ?? new List<NearbyNet>()).Select(r => new { w = r.Width, pts = R(new[] { r.A.xz, r.B.xz, r.C.xz, r.D.xz }) }).ToList();
            var markers = (_markers ?? new List<EdgeMarker>()).Select(m => new { kind = m.Kind ?? "", pos = P(m.Position.xz), crosswalk = m.Crosswalk }).ToList();
            var issues = plan.AllIssues().Where(i => i?.At != null).Select(i => new { key = i.Key, red = i.Dropped || i.Severity == IssueSeverity.Error, sev = i.Severity.ToString(), pos = P(i.At.Value.xz) }).ToList();
            var heat = plan.TerrainHeat?.Rings.Select(h => new { band = h.band, ring = R(h.ring) }).ToList();
            var legend = PreviewLegend.Compute(plan, markers.Count).Select(e => new { id = e.Id, color = e.Color, count = e.Count, length = e.LengthM, area = e.AreaM2 }).ToList();
            var doc = new { outline = R(ring.Select(p => p.xz)), roads, nets, surfaces, areas, objects, zoning, cells, markers, issues, heat, legend };
            File.WriteAllText(file, JsonSerializer.Serialize(doc));
        }

        private static void Draw(ComposedPlan plan, float3[] ring, Catalog cat, string file, Result res)
        {
            if (PlanJson) WritePlanJson(plan, ring, cat, Path.ChangeExtension(file, ".plan.json"));
            if (PlanJson)
            {
                // the site plan itself, as the panel's top view receives it (tools/sketch-mockups/siteplan-svg.js draws it)
                var entrances = (_markers ?? new List<EdgeMarker>()).Select(m => m.Position.xz).ToList();
                var sp = SitePlan.Compute(new SitePlan.Input { Plan = plan, Outline = ring.Select(p => p.xz).ToArray(), Assets = cat.Items, Entrances = entrances });
                File.WriteAllText(Path.ChangeExtension(file, ".siteplan.json"), sp.ToUi("gallery").ToString());
            }
            var ring2 = ring.Select(p => p.xz).ToArray();
            float2 min = ring2[0], max = ring2[0];
            foreach (var p in ring2) { min = math.min(min, p); max = math.max(max, p); }
            float margin = math.max(Showcase ? 26f : 10f, math.cmax(max - min) * 0.06f);
            min -= margin; max += margin;
            if (Zoom != null)
            {
                // (s34) --zoom fx0,fz0,fx1,fz1: a detail of the site (fractions of its bounds).
                float2 span = max - min;
                float2 zmin = min + span * new float2(Zoom[0], Zoom[1]), zmax = min + span * new float2(Zoom[2], Zoom[3]);
                min = zmin; max = zmax;
            }
            float2 ext = max - min;
            float ppm = math.min(6f, ImageSize / math.cmax(ext));
            int w = (int)math.ceil(ext.x * ppm), h = (int)math.ceil(ext.y * ppm);
            var pal = _palette ?? Placemaker.Core.Paint.TerrainPalette.Vanilla();
            var cv = new Canvas(w, h, min, ppm, PaintView.Outside(pal));
            HashSet<int> painted = null;
            // Surrounding streets (showcase): grey 12 m band with a dashed centre line.
            if (_roads != null)
                foreach (var r in _roads)
                {
                    var rp = new[] { r.A.xz, r.D.xz };
                    cv.StrokePolyline(rp, r.Width, Rgba.Hex("#9a9aa0"));
                    cv.DashedPolyline(rp, cv.Px(1f), 4f, 4f, Rgba.Hex("#ffffff", 0.8f));
                }
            // s33: the bare ground is the map's procedural grass (census colour at the gallery exposure).
            cv.FillPolygon(ring2, PaintView.Ground(pal));
            Func<float2, float> final = null;
            if (!float.IsNaN(_waterZ) || _sloped)
            {
                // s20: the ground the build leaves (real raster over sloped patches), hillshaded, water where it is
                // below the level, 1 m contours; paint goes under the water tint so sand visibly continues under it.
                var original = _original;
                final = TerrainView.Final(plan, original);
                var fin = final;
                float level = _level;
                cv.FillPolygon(new[] { min, new float2(max.x, min.y), max, new float2(min.x, max.y) }, Rgba.Hex("#d5dcc6"));
                painted = PaintView.Draw(cv, plan, ring2, min, max, pal, _waterZ, false, true);
                float px = cv.Px(1f);
                cv.Shade(p =>
                {
                    float sh = TerrainView.Shade(fin, p);
                    return sh < 1f ? new Rgba(0.05f, 0.08f, 0.12f, (1f - sh) * 0.85f) : new Rgba(1f, 1f, 0.95f, (sh - 1f) * 0.6f);
                }, min, max);
                cv.Shade(p =>
                {
                    float h = fin(p);
                    if (h < level - 0.02f)
                    {
                        float d = math.saturate((level - h) / 3.5f);
                        return Rgba.Hex("#3f86c6", 0.28f + 0.6f * d);
                    }
                    float hx = fin(p + new float2(px * 1.5f, 0)), hz = fin(p + new float2(0, px * 1.5f));
                    float cs = _contourStep > 0f ? _contourStep : 1f;
                    if (math.floor(h / cs) != math.floor(hx / cs) || math.floor(h / cs) != math.floor(hz / cs))
                        return new Rgba(0.25f, 0.2f, 0.15f, ((int)math.floor(h / cs)) % 5 == 0 ? 0.6f : 0.28f);
                    return default;
                }, min, max);
                // Original waterline (dashed) for reference.
                cv.Shade(p =>
                {
                    float h = original(p);
                    float hx = original(p + new float2(px * 1.2f, 0)), hz = original(p + new float2(0, px * 1.2f));
                    bool edge = (h < level) != (hx < level) || (h < level) != (hz < level);
                    return edge && ((int)math.floor((p.x + p.y) / 2f) & 1) == 0 ? Rgba.Hex("#1d4f8a", 0.9f) : default;
                }, min, max);
            }

            // (s34) The surrounding streets again on top of the relief (the sloped ground view repaints the canvas).
            if (final != null && _roads != null)
                foreach (var r in _roads)
                {
                    var rp = new[] { r.A.xz, r.D.xz };
                    cv.StrokePolyline(rp, r.Width, Rgba.Hex("#9a9aa0"));
                    cv.DashedPolyline(rp, cv.Px(1f), 4f, 4f, Rgba.Hex("#ffffff", 0.8f));
                }
            // Terraform: height after all layers (last plan with HeightAfter wins), tinted + 1 m contours.
            Func<float2, float> height = null;
            foreach (var l in plan.Layers) if (l?.HeightAfter != null) height = l.HeightAfter;
            if (final != null) { }
            else if (height == null && plan.Layers.Any(l => l != null && l.Terraform.Count > 0))
            {
                var tf = plan.Layers.Where(l => l != null).SelectMany(l => l.Terraform).Where(t => t?.Ring != null && t.Ring.Length >= 3).ToList();
                height = p => { float hh = 0; foreach (var t in tf) if (Inside(t.Ring, p)) hh = t.TargetHeight; return hh; };
            }
            if (height != null && final == null)
            {
                var hf = height;
                float px = cv.Px(1f);
                cv.Shade(p =>
                {
                    float hh;
                    try { hh = hf(p); } catch (Exception) { return default; }
                    if (math.abs(hh) < 0.05f) return default;
                    float a = math.clamp(math.abs(hh) / 6f, 0.08f, 0.45f);
                    var c = hh > 0 ? new Rgba(0.75f, 0.45f, 0.2f, a) : new Rgba(0.2f, 0.4f, 0.8f, a);
                    // contour every metre
                    float hx = Safe(hf, p + new float2(px * 1.5f, 0)), hz = Safe(hf, p + new float2(0, px * 1.5f));
                    if (math.floor(hh) != math.floor(hx) || math.floor(hh) != math.floor(hz)) return new Rgba(0.25f, 0.2f, 0.15f, 0.55f);
                    return c;
                }, min, max);
            }

            // Terrain texture paint (s13): the real per-point strength field the game paints (PaintRaster stamps: feather,
            // gradient, edge noise), in the vanilla material colours, alpha = strength; fainter under water.
            if (final == null) painted = PaintView.Draw(cv, plan, ring2, min, max, pal, _waterZ, true, false);

            // Surfaces: translucent fill + crisp darker outline; paved surfaces one stone tone per paving asset.
            var tones = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var s in l.Surfaces)
                {
                    if (s?.Ring == null || s.Ring.Length < 3) continue;
                    res.Surfaces++;
                    var c = SurfaceColor(s, tones);
                    var r2 = s.Ring.Select(p => p.xz).ToArray();
                    cv.FillPolygon(r2, c.WithA(0.85f));
                    cv.StrokePolyline(r2, cv.Px(1f), c.Darker(0.7f), closed: true);
                }
                foreach (var a in l.Areas)
                {
                    if (a?.Ring == null || a.Ring.Length < 3) continue;
                    cv.DashedPolyline(Close(a.Ring.Select(p => p.xz).ToArray()), cv.Px(1f), 2f, 1.5f, Rgba.Hex("#5a7fa8", 0.8f));
                }
                foreach (var t in l.Terraform)
                    if (t?.Ring != null && t.Ring.Length >= 3 && !t.Sloped)
                        cv.DashedPolyline(Close(t.Ring.Select(p => p.xz).ToArray()), cv.Px(1f), 3f, 2f, t.Permanent ? Rgba.Hex("#d08a00", 0.35f) : Rgba.Hex("#4f86c6", 0.5f));
            }
            // s20: sculpted (sloped) terraform shows as its outline, not a dashed grid.
            foreach (var (ea, eb) in TerraformSurface.BoundaryEdges(plan.Layers.Where(l => l != null).SelectMany(l => l.Terraform).Where(t => t != null && t.Sloped)))
                cv.DashedPolyline(new[] { ea.xz, eb.xz }, cv.Px(1f), 3f, 2f, Rgba.Hex("#d08a00", 0.6f));

            // Zoning (street shops, edge zoning): commercial blue, the 8 m cells the game will zone.
            foreach (var l in plan.Layers.Where(l => l != null))
                foreach (var z in l.Zoning)
                {
                    var q = new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz };
                    // (s34) A roads layer's pre-zoning: housing green, the whole blocks (8 m cells as a light grid).
                    bool roadsZoning = l.GeneratorId == "roads";
                    cv.FillPolygon(q, roadsZoning ? Rgba.Hex("#7cc96b", 0.45f) : Rgba.Hex("#8fb4e8", 0.75f));
                    cv.StrokePolyline(q, cv.Px(1f), roadsZoning ? Rgba.Hex("#3f8f3a", 0.8f) : Rgba.Hex("#3d6db5"), closed: true);
                }
            // (s26) The cells ZoneGuard zones (snapped to the roads' blocks, incl. the predicted blocks of the lanes this
            // build adds): what the game will zone, one outline per cell.
            if (plan.ZoneCells != null)
                foreach (var c in plan.ZoneCells.Where(c => c.State == Placemaker.Core.Build.ZoneCellState.Zoned))
                    cv.StrokePolyline(new[] { c.A, c.B, c.C, c.D }, cv.Px(0.8f), Rgba.Hex("#1f4f99", 0.8f), closed: true);
            foreach (var l in plan.Layers.Where(l => l != null))
                foreach (var g in l.Regions)
                    if (g?.Ring != null && g.Tags != null && g.Tags.Contains("shops"))
                        cv.StrokePolyline(g.Ring.Select(v => v.xz).ToArray(), cv.Px(1.4f), Rgba.Hex("#1f4f99"), closed: true);

            // (s34) Existing buildings of the site (road sites: the farm the roads must go around).
            if (_obstacles != null)
                foreach (var o in _obstacles)
                {
                    var q = new[] { o.A, o.B, o.C, o.D };
                    cv.FillPolygon(q, Rgba.Hex("#b5653a", 0.9f));
                    cv.StrokePolyline(q, cv.Px(1f), Rgba.Hex("#5a2d12"), closed: true);
                }
            // (s34) Turning circles under the roads: an asphalt (or grass) disc of the cap's real size.
            foreach (var o in plan.Layers.Where(l => l != null).SelectMany(l => l.Objects))
            {
                if (o == null || o.Role != ObjectRole.CulDeSacCap) continue;
                string id = o.Asset.Id ?? "";
                float cr = id.Contains("XL") ? 17f : id.Contains("Large") ? 14f : id.Contains("Medium") ? 11.5f : 8f;
                cv.FillCircle(o.Position.xz, cr + cv.Px(0.8f), Rgba.Hex("#4f4f55"));
                cv.FillCircle(o.Position.xz, cr, Rgba.Hex("#8d8d93"));
                if (id.EndsWith("02") || id.EndsWith("03")) cv.FillCircle(o.Position.xz, cr * 0.45f, Rgba.Hex(id.EndsWith("03") ? "#3c8a3c" : "#9ccc7a"));
            }
            // Nets: edges at real width first (so junctions merge), then fill, then a thin centre line.
            var nets = plan.Layers.Where(l => l != null).SelectMany(l => l.Nets).Where(n => n != null).ToList();
            var polylines = nets.Select(n => (n, pts: Flatten(n), hw: HalfWidth(n, cat))).ToList();
            foreach (var (n, pts, hw) in polylines)
            {
                res.Nets++;
                for (int i = 0; i + 1 < pts.Length; i++) res.PathMetres += math.distance(pts[i], pts[i + 1]);
                if (n.Role == NetRole.Fence || Invisible(n)) continue;
                cv.StrokePolyline(pts, hw * 2f + cv.Px(1.6f), NetColors(n).edge);
            }
            foreach (var (n, pts, hw) in polylines)
            {
                var (fill, edge) = NetColors(n);
                if (n.Role == NetRole.Fence) { cv.StrokePolyline(pts, math.max(0.4f, cv.Px(1.5f)), fill); continue; }
                // s33: an invisible (walkable) path under a painted trail: only a fine dotted line.
                if (Invisible(n)) { cv.DashedPolyline(pts, cv.Px(1f), 0.8f, 1.6f, Rgba.Hex("#ffffff", 0.75f)); continue; }
                cv.StrokePolyline(pts, hw * 2f, fill);
            }
            foreach (var (n, pts, hw) in polylines)
            {
                if (n.Role == NetRole.Fence || Invisible(n)) continue;
                // s16: roads of the roads layer carry their role colour as a centre band (same colours as the
                // in-game preview and the panel legend); other nets keep the thin dashed centre line.
                string role = RoadRoles.Of(n.Role);
                bool roadPlan = role != null && (role != RoadRoles.CarFree || n.Control == RoadsIds.PathAsset);
                if (roadPlan)
                {
                    var rc = RoadRoles.Rgb(role);
                    cv.StrokePolyline(pts, math.max(cv.Px(3f), hw * 0.55f), new Rgba(rc.x, rc.y, rc.z, 1f));
                }
                else if (hw * 2f * cv.PixelsPerMetre > 8f)
                    cv.DashedPolyline(pts, cv.Px(0.8f), 2f, 2f, NetColors(n).edge.WithA(0.5f));
            }
            res.RoadLegend = RoadRoles.Legend(nets.Where(n => n.Role != NetRole.Pathway || n.Control == RoadsIds.PathAsset));

            // Area outline on top of surfaces.
            cv.StrokePolyline(ring2, cv.Px(2f), Rgba.Hex("#33415c"), closed: true);
            // Placed entrances / markers: magenta discs on the outline.
            if (_markers != null)
                foreach (var m in _markers)
                {
                    cv.FillCircle(m.Position.xz, cv.Px(5f), Rgba.Hex("#d01fd0"));
                    cv.StrokeCircle(m.Position.xz, cv.Px(5f), cv.Px(1.2f), Rgba.Hex("#ffffff"));
                }

            // Objects: decals first (faint), then ground cover, then furniture, then trees (crowns over everything).
            var objs = plan.Layers.Where(l => l != null).SelectMany(l => l.Objects).Where(o => o != null).ToList();
            res.Objects = objs.Count;
            var fps = new List<(PlannedObject o, string cat, float2[] poly, float r)>();
            foreach (var o in objs)
            {
                string c = RoleCategories.ForObject(o) ?? "";
                res.ByRole[o.Role.ToString()] = res.ByRole.TryGetValue(o.Role.ToString(), out var k) ? k + 1 : 1;
                res.ByCategory[c] = res.ByCategory.TryGetValue(c, out var kc) ? kc + 1 : 1;
                var (poly, r) = Footprint(o, cat);
                fps.Add((o, c, poly, r));
            }
            int Order(PlannedObject o) => o.Role == ObjectRole.Decal ? 0 : o.Role == ObjectRole.ParkingBay ? 1 : o.Role == ObjectRole.Bush ? 3 : o.Role == ObjectRole.Tree ? 5 : 2;
            foreach (var f in fps.OrderBy(f => Order(f.o)))
                DrawObject(cv, f.o, f.cat, f.poly, f.r);

            DrawDecalLegend(cv, objs);
            PaintView.Legend(cv, pal, painted);
            if (_toolkit)
                foreach (var (at, text) in PaintView.Labels)
                {
                    var px = cv.OutPx(at);
                    cv.RectPx(px.x - 2, px.y - 2, text.Length * 12 + 4, 18, Rgba.Hex("#ffffff", 0.8f));
                    cv.TextPx(px.x, px.y, text, Rgba.Hex("#202020"), 2);
                }


            // Issues
            foreach (var i in plan.AllIssues())
            {
                if (i == null) continue;
                if (i.Severity == IssueSeverity.Error) res.Errors++;
                res.IssueKeys.Add(i.Severity.ToString()[0] + ":" + i.Key + (i.Args != null && i.Args.Length > 0 ? "(" + string.Join(",", i.Args) + ")" : ""));
                res.IssueKeys.Add(i.Severity.ToString()[0] + ":" + i.Key);
                if (i.At.HasValue)
                {
                    var c = i.Severity == IssueSeverity.Error ? Rgba.Hex("#e02020") : Rgba.Hex("#e0a020");
                    cv.StrokeCircle(i.At.Value.xz, 5f, cv.Px(2.5f), c);
                }
            }

            // Scale bar: 10 m, bottom-left.
            var sb = new float2(min.x + margin * 0.3f, min.y + margin * 0.3f);
            cv.FillPolygon(new[] { sb, sb + new float2(10, 0), sb + new float2(10, cv.Px(4)), sb + new float2(0, cv.Px(4)) }, Rgba.Hex("#222222"));
            cv.FillPolygon(new[] { sb + new float2(10, 0), sb + new float2(20, 0), sb + new float2(20, cv.Px(4)), sb + new float2(10, cv.Px(4)) }, Rgba.Hex("#ffffff"));
            cv.StrokePolyline(new[] { sb, sb + new float2(20, 0), sb + new float2(20, cv.Px(4)), sb + new float2(0, cv.Px(4)) }, cv.Px(1), Rgba.Hex("#222222"), closed: true);

            Metrics(res, fps, polylines.Select(p => (p.n, p.pts, p.hw)).ToList(), ring2);
            // s9: edge spikes (paths hugging or leaving the outline) drawn as red bars.
            var spikes = Placemaker.Core.Layers.Common.PathQuality.EdgeSpikes(plan, ring);
            res.EdgeSpikes = spikes.Count;
            // (shops4) Surfaces the game would refuse (the build skips them: a lot's asphalt missing under its stalls). Same rule as
            // BuildService.EmitPreparedRings with the surface prefabs' snap distance (d = 0.375).
            foreach (var l in plan.Layers)
                foreach (var sf in l.Surfaces)
                {
                    var r2 = Placemaker.Core.Geometry.Poly.To2D(sf.Ring);
                    if (Placemaker.Core.Build.AreaRings.IsValid(r2, 0.375f + Placemaker.Core.Build.AreaRings.Margin) && Placemaker.Core.Geometry.SurfaceAcceptance.IsAccepted(r2)) continue;
                    if (Placemaker.Core.Build.AreaRings.PrepareSurface(r2, 0.375f).Count > 0) continue;
                    res.BadSurfaces++;
                    if (System.Environment.GetEnvironmentVariable("PM_DEBUG") == "1") System.Console.Error.WriteLine($"bad surface {l.GeneratorId} {sf.Role} area {Placemaker.Core.Geometry.Poly.Area(r2):F1}: {string.Join(" ", r2.Select(p => $"({p.x:F2},{p.y:F2})"))}");
                }
            foreach (var sp in spikes) cv.StrokePolyline(new[] { sp.From, sp.To }, 1.6f, Rgba.Hex("#e01010", 0.85f));
            if (final != null)
            {
                res.Glitch = TerrainView.Glitches(plan, _original, final, min, max);
                // s30: the worst curtain (red) and the worst net seat (magenta) circled.
                foreach (var (at, col) in new[] { (TerrainView.LastCurtainAt, "#e01010"), (TerrainView.LastSeatAt, "#d020d0") })
                    if (math.isfinite(at.x))
                    {
                        var circ = new float2[24];
                        for (int k = 0; k < 24; k++) circ[k] = at + new float2(math.cos(k * 0.2618f), math.sin(k * 0.2618f)) * 5f;
                        cv.StrokePolyline(circ, cv.Px(2.5f), Rgba.Hex(col), closed: true);
                    }
            }
            cv.SavePng(file);
            if (final != null)
                try { TerrainView.Oblique(file.Replace(".png", "-3d.png"), cv, plan, cat, _original, final, _level, min, max); }
                catch (Exception e) { Console.WriteLine("  3d view failed: " + e.Message); }
            // (s52) Terraced roads: the cut/fill map (blue cut, red fill, 0.5 m bands) under the nets.
            if (final != null && plan.Layers.Any(l => l != null && l.Facts.ContainsKey("terraces.sloped")))
                try { TerrainView.CutMap(file.Replace(".png", "-cut.png"), plan, _original, final, min, max, ppm, _terraced); }
                catch (Exception e) { Console.WriteLine("  cut view failed: " + e.Message); }
        }

        /// <summary>Category colour of a decal (s21 gallery legend). Functional markings white.</summary>
        internal static Rgba DecalColor(string cat)
        {
            switch (cat ?? "")
            {
                case "decal.leaves": return Rgba.Hex("#d9822b");
                case "decal.leaves.edge": return Rgba.Hex("#f0a030");
                case "decal.mulch": return Rgba.Hex("#6b3e1e");
                case "decal.moss": return Rgba.Hex("#3f8f3a");
                case "decal.wear": case "decal.dirt": return Rgba.Hex("#a07850");
                case "decal.mud": return Rgba.Hex("#5c4330");
                case "decal.sand": return Rgba.Hex("#e3c66d");
                case "decal.wetsand": return Rgba.Hex("#9a8a5a");
                case "decal.wet": case "decal.puddles": return Rgba.Hex("#3d7fd6");
                case "decal.rocks": return Rgba.Hex("#7d7d85");
                case "decal.stain": return Rgba.Hex("#4a4a4a");
                case "decal.oil": return Rgba.Hex("#101010");
                case "decal.cracks": return Rgba.Hex("#6a5acd");
                case "decal.patch": return Rgba.Hex("#2f3f5f");
                case "decal.tyre": return Rgba.Hex("#c02060");
                case "decal.litter": return Rgba.Hex("#e040e0");
                case "decal.glass": return Rgba.Hex("#20d0d0");
                case "decal.beach": return Rgba.Hex("#1f9e8f");
                case "decal.footprints": return Rgba.Hex("#b0a080");
                case "decal.graffiti": return Rgba.Hex("#ff3070");
                default: return Placemaker.Core.Decals.DecalCatalog.IsFunctionalCategory(cat) ? Rgba.Hex("#ffffff") : Rgba.Hex("#8f7a5a");
            }
        }

        /// <summary>Top-left legend: one swatch + category + count per decal category present.</summary>
        private static void DrawDecalLegend(Canvas cv, List<PlannedObject> objs)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var o in objs)
            {
                if (o.Role != ObjectRole.Decal) continue;
                string c = RoleCategories.ForObject(o) ?? "decal";
                counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
            }
            if (counts.Count == 0) return;
            int total = counts.Values.Sum();
            int x = 6, y = 6, row = 13, w = 172;
            cv.RectPx(x - 3, y - 3, w, row * (counts.Count + 1) + 4, Rgba.Hex("#ffffff", 0.82f));
            cv.TextPx(x, y, "DECALS " + total, Rgba.Hex("#202020"), 1);
            int i = 1;
            foreach (var kv in counts)
            {
                int yy = y + i * row;
                cv.RectPx(x, yy, 9, 9, DecalColor(kv.Key));
                string label = kv.Key.StartsWith("decal.", StringComparison.Ordinal) ? kv.Key.Substring(6) : kv.Key;
                cv.TextPx(x + 14, yy + 1, label + " " + kv.Value, Rgba.Hex("#202020"), 1);
                i++;
            }
        }

        private static float Safe(Func<float2, float> f, float2 p) { try { return f(p); } catch (Exception) { return 0; } }

        private static float2[] Close(float2[] r) => r.Concat(new[] { r[0] }).ToArray();

        private static float HalfWidth(PlannedNet n, Catalog cat)
        {
            if (!string.IsNullOrEmpty(n.Asset.Id) && cat.Items.TryGetValue(n.Asset.Id, out var info) && info.NetWidth > 0.5f) return info.NetWidth * 0.5f;
            return Composer.DefaultHalfWidth(n.Role);
        }

        private static float2[] Flatten(PlannedNet n)
        {
            int seg = math.clamp((int)(math.distance(n.A.xz, n.D.xz) / 2f), 1, 24);
            if (math.lengthsq(math.cross(n.B - n.A, n.D - n.A)) < 1e-3f && math.lengthsq(math.cross(n.C - n.A, n.D - n.A)) < 1e-3f) seg = 1;
            var pts = new float2[seg + 1];
            for (int i = 0; i <= seg; i++) pts[i] = Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)seg).xz;
            return pts;
        }

        /// <summary>World footprint polygon (catalog bounds × scale, rotated by yaw) and a crown/fallback radius.</summary>
        private static (float2[] poly, float r) Footprint(PlannedObject o, Catalog cat)
        {
            float s = o.Scale > 0.05f ? o.Scale : 1f;
            float2 bmin, bmax;
            if (!string.IsNullOrEmpty(o.Asset.Id) && cat.Items.TryGetValue(o.Asset.Id, out var info) && math.cmax(info.BoundsMax.xz - info.BoundsMin.xz) > 0.05f)
            {
                bmin = info.BoundsMin.xz * s; bmax = info.BoundsMax.xz * s;
                // Same crown estimate as the in-game sketch (PlacemakerOverlaySystem.CrownRadius): tree bounds
                // are near the trunk box, the visible crown is about 0.28 x height.
                if (o.Role == ObjectRole.Tree)
                {
                    float crown = math.clamp(math.max(math.cmax(info.BoundsMax.xz - info.BoundsMin.xz) * 0.5f, (info.BoundsMax.y - info.BoundsMin.y) * 0.28f), 1f, 7f) * s;
                    bmin = new float2(-crown); bmax = new float2(crown);
                }
            }
            else
            {
                float r0 = Composer.DefaultRadius(o.Role) * s;
                bmin = new float2(-r0); bmax = new float2(r0);
            }
            float c = math.cos(o.Yaw), sn = math.sin(o.Yaw);
            float2 Rot(float2 v) => new float2(v.x * c + v.y * sn, -v.x * sn + v.y * c) + o.Position.xz;
            var poly = new[] { Rot(bmin), Rot(new float2(bmax.x, bmin.y)), Rot(bmax), Rot(new float2(bmin.x, bmax.y)) };
            return (poly, math.cmax(bmax - bmin) * 0.5f);
        }

        private static void DrawObject(Canvas cv, PlannedObject o, string cat, float2[] poly, float r)
        {
            var col = ObjectColor(o, cat);
            var p = o.Position.xz;
            float minR = cv.Px(2f);
            switch (o.Role)
            {
                case ObjectRole.Tree:
                    cv.FillCircle(p, math.max(r, minR), col.WithA(_paintView ? 0.22f : 0.5f));   // s33: paint views see the mulch under the crowns
                    cv.StrokeCircle(p, math.max(r, minR), cv.Px(1f), col.Darker(0.6f).WithA(0.8f));
                    cv.FillCircle(p, math.max(r * 0.22f, cv.Px(1.5f)), col.Darker(0.45f));
                    return;
                case ObjectRole.Bush:
                    if (cat.StartsWith("hedge")) break;
                    cv.FillCircle(p, math.max(r, minR), col.WithA(0.85f));
                    cv.StrokeCircle(p, math.max(r, minR), cv.Px(0.8f), col.Darker(0.6f));
                    return;
                case ObjectRole.Centerpiece:
                    cv.FillPolygon(poly, col.WithA(0.35f));
                    cv.StrokeCircle(p, math.max(r, cv.Px(4f)), cv.Px(2f), col);
                    cv.FillCircle(p, cv.Px(2f), col.Darker(0.6f));
                    return;
                case ObjectRole.Lamp:
                    cv.FillCircle(p, math.max(r, cv.Px(2.5f)), col);
                    cv.StrokeCircle(p, math.max(r, cv.Px(2.5f)), cv.Px(0.8f), Rgba.Hex("#5a4a00"));
                    return;
                case ObjectRole.Decal:
                {
                    // s21: faint fill + outline in the decal's category colour (legend top-left), so dressing reads by kind.
                    // s23: drawn with the opacity the game builds them with (see-through clones per tier).
                    var dc = DecalColor(cat);
                    float op = Placemaker.Core.Decals.DecalLook.Tier(Placemaker.Core.Decals.DecalLook.OpacityOf(o)) / 100f;
                    cv.FillPolygon(poly, dc.WithA(0.75f * op));
                    cv.StrokePolyline(poly, cv.Px(0.6f), dc.WithA(0.5f * op), closed: true);
                    return;
                }
                case ObjectRole.ParkingBay:
                    cv.StrokePolyline(poly, cv.Px(1f), Rgba.Hex("#ffffff", 0.9f), closed: true);
                    return;
                case ObjectRole.CulDeSacCap: return;   // (s34) drawn as a disc under the roads
                case ObjectRole.Marker: case ObjectRole.AccessPoint: case ObjectRole.AnimalSpawner:
                    cv.FillCircle(p, cv.Px(2f), Rgba.Hex("#c02cc0"));
                    return;
            }
            // Footprint rectangle, enlarged to stay visible.
            if (math.cmax(math.abs(poly[2] - poly[0])) * cv.PixelsPerMetre < 3f)
            {
                cv.FillCircle(p, cv.Px(1.8f), col);
                return;
            }
            cv.FillPolygon(poly, col.WithA(0.9f));
            cv.StrokePolyline(poly, cv.Px(0.8f), col.Darker(0.55f), closed: true);
        }

        private static bool Inside(float3[] ring, float2 p)
        {
            bool c = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].z > p.y) != (ring[j].z > p.y) && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].z) / (ring[j].z - ring[i].z) + ring[i].x) c = !c;
            return c;
        }

        private static bool Inside(float2[] ring, float2 p)
        {
            bool c = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y) && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x) c = !c;
            return c;
        }

        private static float SegDist(float2 p, float2 a, float2 b)
        {
            var ab = b - a; float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.lengthsq(ab)));
            return math.distance(p, a + ab * t);
        }

        /// <summary>Quick QA numbers: benches far from any path, overlapping solid footprints, objects outside the area.</summary>
        private static void Metrics(Result res, List<(PlannedObject o, string cat, float2[] poly, float r)> fps, List<(PlannedNet n, float2[] pts, float hw)> nets, float2[] ring)
        {
            var paths = nets.Where(n => n.n.Role != NetRole.Fence).ToList();
            foreach (var f in fps)
            {
                if (f.o.Role == ObjectRole.Decal) { res.Decals++; continue; }
                if (!Inside(ring, f.o.Position.xz)) res.Outside++;
                if (f.o.Role == ObjectRole.Bench)
                {
                    float best = float.MaxValue;
                    foreach (var n in paths)
                        for (int i = 0; i + 1 < n.pts.Length; i++) best = math.min(best, SegDist(f.o.Position.xz, n.pts[i], n.pts[i + 1]) - n.hw);
                    if (best > 3f) res.BenchesOffPath++;
                }
            }
            // Solid objects (not trees: crowns may overlap; not decals/bays) whose cores overlap.
            var solid = fps.Where(f => f.o.Role != ObjectRole.Tree && f.o.Role != ObjectRole.Decal && f.o.Role != ObjectRole.ParkingBay
                                       && f.o.Role != ObjectRole.Marker && f.o.Role != ObjectRole.AccessPoint && f.o.Role != ObjectRole.AnimalSpawner
                                       && !f.cat.StartsWith("hedge") && f.o.Role != ObjectRole.FencePiece).ToList();
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < solid.Count; i++)
            {
                var k = ((int)math.floor(solid[i].o.Position.x / 8f), (int)math.floor(solid[i].o.Position.z / 8f));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                l.Add(i);
            }
            for (int i = 0; i < solid.Count; i++)
            {
                var a = solid[i];
                int gx = (int)math.floor(a.o.Position.x / 8f), gz = (int)math.floor(a.o.Position.z / 8f);
                bool hit = false;
                for (int dx = -1; dx <= 1 && !hit; dx++)
                    for (int dz = -1; dz <= 1 && !hit; dz++)
                        if (grid.TryGetValue((gx + dx, gz + dz), out var l))
                            foreach (var j in l)
                            {
                                if (j == i) continue;
                                var b = solid[j];
                                if (math.distance(a.o.Position.xz, b.o.Position.xz) < (a.r + b.r) * 0.5f)
                                {
                                    hit = true;
                                    if (System.Environment.GetEnvironmentVariable("PM_DEBUG") == "1") System.Console.Error.WriteLine($"overlap {a.o.Role}/{a.o.Asset.Id} r{a.r:F2} at ({a.o.Position.x:F1},{a.o.Position.z:F1}) with {b.o.Role}/{b.o.Asset.Id} r{b.r:F2} d {math.distance(a.o.Position.xz, b.o.Position.xz):F2}");
                                    break;
                                }
                            }
                if (hit) res.Overlaps++;
            }
        }
    }

    // ───────────────────────── index.html + gallery.json ─────────────────────────

    public static class Report
    {
        public static void Write(string outDir, List<Job> jobs, Result[] results)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>Placemaker gallery</title><style>");
            sb.Append(":root{--bg:#f6f5f1;--fg:#1d2330;--muted:#5b6475;--card:#fff;--line:#d9d6ce}");
            sb.Append("@media (prefers-color-scheme:dark){:root{--bg:#15181e;--fg:#e8eaee;--muted:#9aa3b2;--card:#1f232b;--line:#333a46}}");
            sb.Append("body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif}h1{margin:0 0 4px}h2{margin:28px 0 8px;border-bottom:1px solid var(--line)}");
            sb.Append(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:12px}.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:8px}");
            sb.Append(".card img{width:100%;height:auto;border-radius:4px;display:block}.m{color:var(--muted);font-size:12px}.bad{color:#d33}.leg span{display:inline-block;margin-right:12px}.sw{display:inline-block;width:12px;height:12px;border-radius:2px;vertical-align:-1px;margin-right:4px;border:1px solid #0003}");
            sb.Append("</style></head><body><h1>Placemaker visual review gallery</h1>");
            sb.Append($"<p class=\"m\">Generated {DateTime.Now:yyyy-MM-dd HH:mm} by Tests/Placemaker.Gallery from the real Core planners and catalog footprints. {jobs.Count} images. See <a href=\"REVIEW.md\">REVIEW.md</a>.</p>");
            var legend = new (string c, string t)[] {
                ("#b5d99a", "grass"),
                ("#d6cfbf", "paving: each paving asset its own stone tone (pale stone, terracotta, granite, ochre, brick…)"),
                ("#e2d6bb", "plaza paving"),
                ("#76767c", "asphalt"),
                ("#7fb2e5", "water"),
                ("#cdb98f", "ground / natural"),
                ("#ead9a2", "sand"),
                ("#b9976a", "dirt"),
                ("#9e9e9e", "rock ground"),
                ("#e3aacb", "decorative surface"),
                ("#8a8172", "terrain paint (grey tint)"),
                ("#f4e7c6", "footpath (real width)"),
                ("#e6b3a8", "bike path"),
                ("#d9c39a", "trail"),
                ("#8d8d93", "road / track (asphalt at real width)"),
                (RoadRoles.Color(RoadRoles.Street), "road role: street (centre band)"),
                (RoadRoles.Color(RoadRoles.Avenue), "road role: avenue"),
                (RoadRoles.Color(RoadRoles.Local), "road role: local / cul-de-sac"),
                (RoadRoles.Color(RoadRoles.Roundabout), "road role: roundabout ring"),
                (RoadRoles.Color(RoadRoles.Perimeter), "road role: perimeter road"),
                (RoadRoles.Color(RoadRoles.CarFree), "road role: car-free street"),
                ("#a3adc4", "parking aisle"),
                ("#7a5a3c", "fence"),
                ("#9a9aa0", "surrounding street"),
                ("#3c8a3c", "tree (crown)"),
                ("#6cb04c", "bush"),
                ("#3f7f37", "hedge"),
                ("#d470a8", "flowerbed"),
                ("#a07850", "planter"),
                ("#8a8a8a", "rock"),
                ("#7b4a1e", "bench"),
                ("#f0c419", "lamp"),
                ("#3a3a3a", "bin"),
                ("#2c78c8", "centerpiece (ring)"),
                ("#6c63b5", "monument / memorial"),
                ("#e8833a", "playground"),
                ("#d65a5a", "sports"),
                ("#b06f3c", "picnic"),
                ("#c0703f", "kiosk / shelter / restroom"),
                ("#22aa66", "EV charger"),
                ("#ffffff", "parking bay (outline)"),
                ("#8f7a5a", "decal (very faint)"),
                ("#7a6fb0", "OTHER prop (no own colour: farm, yard, industrial...)"),
                ("#d01fd0", "entrance / marker (big disc on outline)"),
                ("#c02cc0", "spawner / access point (small dot)"),
                ("#e02020", "issue")
            };
            sb.Append("<div class=\"leg m\" style=\"position:sticky;top:0;z-index:5;background:var(--bg);padding:6px 0;border-bottom:1px solid var(--line)\">");
            sb.Append("<b>How to read:</b> top-down view, 2x10 m scale bar bottom-left. Dark outline = selected area; grey bands = streets around it. Orange tint with contour lines = ground raised, blue = lowered (dashed orange outline = permanent change). Everything is drawn at its real footprint. ");
            foreach (var (c, t) in legend) sb.Append($"<span><i class=\"sw\" style=\"background:{c}\"></i>{t}</span>");
            sb.Append("</div>");
            var json = new List<object>();
            foreach (var group in jobs.Select((j, i) => (j, r: results[i])).GroupBy(x => x.j.Type))
            {
                sb.Append($"<h2 id=\"{group.Key}\">{group.Key}</h2><div class=\"grid\">");
                foreach (var (j, r) in group)
                {
                    sb.Append($"<div class=\"card\"><a href=\"{r.File}\"><img loading=\"lazy\" src=\"{r.File}\" alt=\"{j.Type} {j.Variant}\"></a><b>{j.Title}</b> <span class=\"m\">({j.Variant})</span><div class=\"m\">");
                    if (r.Failure != null) sb.Append($"<span class=\"bad\">FAILED: {Esc(r.Failure)}</span>");
                    else
                    {
                        sb.Append($"{r.Objects} objects · {r.Nets} nets / {r.PathMetres:0} m · {r.Surfaces} surfaces · {r.Ms:0} ms<br>");
                        sb.Append(string.Join(", ", r.ByRole.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")) + "<br>");
                        foreach (var e in r.RoadLegend)
                            sb.Append($"<span><i class=\"sw\" style=\"background:{RoadRoles.Color(e.Role)}\"></i>{e.Role} · {Esc(RoadRoles.NameOf(e.AssetId))} · {(e.LengthM >= 1000 ? (e.LengthM / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km" : e.LengthM.ToString("0", CultureInfo.InvariantCulture) + " m")} ({e.Count})</span><br>");
                        var flags = new List<string>();
                        if (r.BenchesOffPath > 0) flags.Add($"{r.BenchesOffPath} benches off-path");
                        if (r.Overlaps > 0) flags.Add($"{r.Overlaps} overlapping objects");
                        if (r.Outside > 0) flags.Add($"{r.Outside} outside area");
                        if (r.EdgeSpikes > 0) flags.Add($"{r.EdgeSpikes} edge spikes");
                        if (r.Errors > 0) flags.Add($"{r.Errors} errors");
                        if (flags.Count > 0) sb.Append($"<span class=\"bad\">{string.Join(" · ", flags)}</span><br>");
                        if (r.RoadAudit != null) sb.Append(Esc(r.RoadAudit) + "<br>");
                        if (r.IssueKeys.Count > 0) sb.Append(Esc(string.Join(", ", r.IssueKeys.Distinct().Take(6))));
                    }
                    sb.Append("</div></div>");
                    json.Add(new
                    {
                        type = j.Type, variant = j.Variant, file = r.File, ms = Math.Round(r.Ms), failure = r.Failure, objects = r.Objects, nets = r.Nets,
                        pathMetres = Math.Round(r.PathMetres), surfaces = r.Surfaces, errors = r.Errors, warnings = r.Warnings,
                        benchesOffPath = r.BenchesOffPath, overlaps = r.Overlaps, outside = r.Outside, edgeSpikes = r.EdgeSpikes, badSurfaces = r.BadSurfaces, decals = r.Decals, byRole = r.ByRole, byCategory = r.ByCategory.OrderByDescending(k => k.Value).ToDictionary(k => k.Key, k => k.Value), issues = r.IssueKeys.Distinct().ToList(), facts = r.Facts,
                        roads = r.RoadLegend.Select(e => new { role = e.Role, asset = e.AssetId, lengthM = e.LengthM, count = e.Count }).ToList(),
                    });
                }
                sb.Append("</div>");
            }
            sb.Append("</body></html>");
            File.WriteAllText(Path.Combine(outDir, "index.html"), sb.ToString());
            File.WriteAllText(Path.Combine(outDir, "gallery.json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string Esc(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    }
}
