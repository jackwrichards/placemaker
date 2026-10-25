// Placemaker UI mock host.
//
// Serves the browser harness (UI/mock) and exposes the REAL engine-free Core over HTTP so the
// mock panel shows genuine plans while UX is iterated in a browser instead of the game:
//   GET  /api/catalog    every layer in GeneratorRegistry.All with its control schema, presets
//                        (incl. values) and defaults; plus single-layer recipes
//   POST /api/compose    { recipe: Step, ring: [[x,z]…], markers?: … } → plan geometry + summary
//   POST /api/randomize  { layer, values, locked, seed } → values (IAreaGenerator.Randomize)
//   POST /api/ready      { layer, values, ring } → Readiness
//   GET  /api/lang       merged Lang/en-US*.json from the repository
// Works with any number of registered layers, including zero (the mock then falls back to its
// own fake catalog).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Layers.Roads;
using Unity.Mathematics;

namespace Placemaker.MockHost
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            int port = 5123;
            string root = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--port") port = int.Parse(args[i + 1]);
                if (args[i] == "--root") root = args[i + 1];
            }
            root ??= FindMockRoot();
            string repo = Path.GetFullPath(Path.Combine(root, "..", ".."));
            Api.Repo = repo;

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");

            var files = new PhysicalFileProvider(root);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                ServeUnknownFileTypes = true,
                OnPrepareResponse = c => c.Context.Response.Headers["Cache-Control"] = "no-store",
            });

            app.MapGet("/api/health", () => Results.Text("ok"));
            app.MapGet("/api/catalog", () => Json(Api.Catalog(repo)));
            app.MapGet("/api/lang", () => Json(Api.Lang(repo)));
            app.MapPost("/api/compose", async (HttpRequest req) =>
            {
                var body = await JsonNode.ParseAsync(req.Body);
                return Json(Api.Compose(body));
            });
            app.MapPost("/api/linked", async (HttpRequest req) =>
            {
                var body = await JsonNode.ParseAsync(req.Body);
                return Json(Api.Linked(body));
            });
            app.MapPost("/api/randomize", async (HttpRequest req) =>
            {
                var body = await JsonNode.ParseAsync(req.Body);
                return Json(Api.Randomize(body));
            });
            app.MapPost("/api/ready", async (HttpRequest req) =>
            {
                var body = await JsonNode.ParseAsync(req.Body);
                return Json(Api.Ready(body));
            });

            Console.WriteLine($"Placemaker mock host: http://127.0.0.1:{port}/  (layers: {GeneratorRegistry.All.Count}, root: {root})");
            app.Run();
        }

        internal static string SitePlanOf(ComposedPlan plan, AreaShape shape, SiteContext site, Recipe recipe)
        {
            var entrances = new List<Unity.Mathematics.float2>();
            if (recipe != null)
                foreach (var kv in Placemaker.Core.Tool.RecipePaths.Walk(recipe))
                    foreach (var m in kv.Value.Markers) entrances.Add(m.Position.xz);
            var sp = Placemaker.Core.Build.SitePlan.Compute(new Placemaker.Core.Build.SitePlan.Input { Plan = plan, Outline = Placemaker.Core.Geometry.Poly.To2D(shape.Ring), Assets = site.Assets, Entrances = entrances });
            var json = sp.ToUi(DateTime.Now.Ticks.ToString());
            json.Set("style", "siteplan");
            return json.ToString();
        }

        private static IResult Json(JsonNode node) => Results.Content(node?.ToJsonString() ?? "null", "application/json");

        private static string FindMockRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "UI", "mock");
                if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
                if (File.Exists(Path.Combine(dir.FullName, "index.html")) && dir.Name == "mock") return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("UI/mock not found; pass --root <path to UI/mock>.");
        }
    }

    internal static class Api
    {
        // ───────────── catalog ─────────────

        /// <summary>(core) The shelf, read as the game reads it (Core/Tool/Shelf.cs). (shops2) The mock's own fixed test shelf
        /// (UI/mock/shelf/*.json) when it is there, so the product's shelf decisions (Recipes/shelf: hidden types and styles) never
        /// change what the UI tests see; else the product's.</summary>
        private static Placemaker.Core.Tool.Shelf Shelf(string repo)
        {
            var files = new List<KeyValuePair<string, string>>();
            var dir = Path.Combine(repo ?? ".", "UI", "mock", Placemaker.Core.Tool.Shelf.FolderName);
            if (!Directory.Exists(dir)) dir = Path.Combine(repo ?? ".", "Recipes", Placemaker.Core.Tool.Shelf.FolderName);
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    files.Add(new KeyValuePair<string, string>(Path.GetFileName(f), File.ReadAllText(f)));
            var shelf = Placemaker.Core.Tool.Shelf.Parse(files);
            Placemaker.Core.Tool.Shelf.Current = shelf;
            return shelf;
        }

        public static JsonNode Catalog(string repo)
        {
            var shelf = Shelf(repo);
            var layers = new JsonArray();
            var recipes = Recipes(repo);
            var shippedRoots = new HashSet<string>(recipes.Select(r => (string)r["root"]?["layer"] ?? ""));
            var shippedIds = new HashSet<string>(recipes.Select(r => (string)r["id"] ?? ""));
            foreach (var g in GeneratorRegistry.All)
            {
                try
                {
                    layers.Add(Layer(g));
                    // Single-layer recipe for fills without a shipped recipe (so every type is reachable).
                    if (g.Kind == LayerKind.Fill && !shippedIds.Contains(g.Id) && !shippedRoots.Contains(g.Id))
                        recipes.Add(new JsonObject
                        {
                            ["id"] = g.Id, ["name"] = g.NameKey, ["icon"] = g.Icon, ["custom"] = false,
                            ["root"] = new JsonObject { ["layer"] = g.Id },
                        });
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Layer {g?.Id} failed to describe itself: {e.Message}");
                }
            }
            var all = new JsonArray();
            foreach (var r in recipes)
            {
                var copy = r.DeepClone();
                // Make-row category (contract RecipeInfo.group), as Core UiModels.Catalog sends it.
                var grp = GeneratorGroups.Of(GeneratorRegistry.Get((string)r["root"]?["layer"] ?? (string)r["id"] ?? ""));
                if (!string.IsNullOrEmpty(grp)) copy["group"] = grp;
                // (core) The shelf's category and hidden flag, as Core UiModels.Catalog sends them.
                if (!string.IsNullOrEmpty(grp) && (bool?)r["custom"] != true)
                {
                    var id = (string)r["id"] ?? "";
                    copy["category"] = shelf.CategoryOf(id, grp);
                    if (shelf.IsTypeShelved(id, grp)) copy["shelved"] = true;
                }
                all.Add(copy);
            }
            var cats = new JsonArray();
            foreach (var c in shelf.Categories)
            {
                var co = new JsonObject { ["id"] = c.Id, ["name"] = c.Key, ["en"] = c.Name, ["order"] = c.Order };
                if (c.Hidden) co["hidden"] = true;
                cats.Add(co);
            }
            return new JsonObject { ["layers"] = layers, ["recipes"] = all, ["categories"] = cats };
        }

        /// <summary>Shipped recipes (Recipes/*.json, docs/proposals/recipe-json.md), as mock templates.</summary>
        private static List<JsonObject> Recipes(string repo)
        {
            var list = new List<(int order, string file, JsonObject r)>();
            var dir = Path.Combine(repo, "Recipes");
            if (!Directory.Exists(dir)) return new List<JsonObject>();
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(f)) is not JsonObject o) continue;
                    if ((o["formatVersion"]?.GetValue<int>() ?? 1) > 1) continue;
                    var rootLayer = (string)o["root"]?["layer"] ?? "";
                    var gen = GeneratorRegistry.Get(rootLayer);
                    int order = int.MaxValue;
                    if (gen != null)
                    {
                        var attr = (AreaGeneratorAttribute)Attribute.GetCustomAttribute(gen.GetType(), typeof(AreaGeneratorAttribute));
                        order = attr?.Order ?? order;
                    }
                    var r = new JsonObject
                    {
                        ["id"] = (string)o["id"] ?? Path.GetFileNameWithoutExtension(f),
                        ["name"] = (string)o["nameKey"] ?? (string)o["name"] ?? "",
                        ["icon"] = (string)o["icon"] ?? gen?.Icon ?? "",
                        ["custom"] = false,
                        ["root"] = Template(o["root"]),
                    };
                    if (o["descriptionKey"] != null) r["description"] = (string)o["descriptionKey"];
                    list.Add((order, Path.GetFileName(f), r));
                }
                catch (Exception e) { Console.WriteLine($"Recipe {f}: {e.Message}"); }
            }
            return list.OrderBy(x => x.order).ThenBy(x => x.file, StringComparer.Ordinal).Select(x => x.r).ToList();
        }

        /// <summary>Recipe-file step → mock template { layer, target, values, children, overrides }.</summary>
        private static JsonObject Template(JsonNode step)
        {
            var t = new JsonObject { ["layer"] = (string)step?["layer"] ?? "", ["target"] = (string)step?["target"] ?? "" };
            if (step?["settings"] is JsonObject set) t["values"] = set.DeepClone();
            if (step?["enabled"] != null) t["enabled"] = step["enabled"].DeepClone();
            var ch = new JsonArray();
            if (step?["children"] is JsonArray a) foreach (var c in a) ch.Add(Template(c));
            t["children"] = ch;
            if (step?["regionOverrides"] is JsonObject ov)
            {
                var o = new JsonObject();
                foreach (var kv in ov) o[kv.Key] = Template(kv.Value);
                t["overrides"] = o;
            }
            return t;
        }

        private static JsonObject Layer(IAreaGenerator g)
        {
            var controls = new JsonArray();
            foreach (var c in g.Controls ?? Array.Empty<ControlSchema>())
            {
                var o = new JsonObject
                {
                    ["id"] = c.Id,
                    ["kind"] = Camel(c.Kind.ToString()),
                    ["tier"] = c.Tier == ControlTier.Essential ? "essential" : "more",
                    ["group"] = c.Group ?? "",
                    ["label"] = c.LabelKey ?? "",
                    ["min"] = c.Min, ["max"] = c.Max, ["step"] = c.Step,
                    ["unit"] = c.Unit ?? "",
                    ["cheap"] = c.Cheap,
                    ["default"] = Value(c.Default),
                };
                if (!string.IsNullOrEmpty(c.TooltipKey)) o["tooltip"] = c.TooltipKey;
                if (!string.IsNullOrEmpty(c.DescriptionKey)) o["description"] = c.DescriptionKey;
                // s15 disabled-with-reason fields, as UiModels.Control sends them.
                if (c.HideWhenOff) o["hideWhenOff"] = true;
                if (!string.IsNullOrEmpty(c.EnabledWhen)) o["enabledWhen"] = c.EnabledWhen;
                if (!string.IsNullOrEmpty(c.DisabledReasonKey)) o["disabledReason"] = c.DisabledReasonKey;
                if (!string.IsNullOrEmpty(c.Icon)) o["icon"] = c.Icon;
                if (!string.IsNullOrEmpty(c.AssetCategory)) o["assetCategory"] = c.AssetCategory;
                if (!string.IsNullOrEmpty(c.VisibleWhen)) o["visibleWhen"] = c.VisibleWhen;
                if (c.MarkerKinds != null && c.MarkerKinds.Length > 0) o["markerKinds"] = Arr(c.MarkerKinds);
                if (c.Options != null && c.Options.Count > 0)
                {
                    var opts = new JsonArray();
                    foreach (var op in c.Options)
                    {
                        var oo = new JsonObject { ["value"] = op.Value, ["label"] = op.LabelKey };
                        if (!string.IsNullOrEmpty(op.Icon)) oo["icon"] = op.Icon;
                        if (!string.IsNullOrEmpty(op.TooltipKey)) oo["tooltip"] = op.TooltipKey;
                        if (!string.IsNullOrEmpty(op.DetailKey)) oo["detail"] = op.DetailKey;
                        if (op.Swatch != null && op.Swatch.Count > 0)
                            oo["swatch"] = new JsonArray(op.Swatch.Select(st => (JsonNode)new JsonObject
                            {
                                ["role"] = st.Role, ["color"] = st.Color, ["width"] = st.Width, ["green"] = st.Green, ["bike"] = st.Bike,
                            }).ToArray());
                        opts.Add(oo);
                    }
                    o["options"] = opts;
                }
                controls.Add(o);
            }
            var presets = new JsonArray();
            foreach (var p in g.Presets ?? Array.Empty<PresetInfo>())
            {
                var po = new JsonObject { ["id"] = p.Id, ["name"] = p.LabelKey, ["icon"] = p.Icon ?? "", ["values"] = Bag(p.Values) };
                if (Placemaker.Core.Tool.Shelf.Current.IsStyleShelved(g.Id, GeneratorGroups.Of(g), p.Id)) po["shelved"] = true;
                presets.Add(po);
            }
            // Built-in features (Core.IHasFeatures) and Make-row visibility (AreaGenerator Hidden).
            var features = new JsonArray();
            if (g is IHasFeatures hf)
                foreach (var f in hf.Features ?? Array.Empty<FeatureInfo>())
                    features.Add(new JsonObject
                    {
                        ["layer"] = f.Layer, ["target"] = f.Target ?? "", ["defaultOn"] = f.DefaultOn,
                        ["labelKey"] = f.LabelKey ?? "", ["tooltip"] = f.TooltipKey ?? "",
                        ["options"] = Arr(f.Options ?? Array.Empty<string>()), ["preset"] = Bag(f.Preset),
                    });
            var gattr = (AreaGeneratorAttribute)Attribute.GetCustomAttribute(g.GetType(), typeof(AreaGeneratorAttribute));
            return new JsonObject
            {
                ["hidden"] = gattr?.Hidden ?? false,
                ["features"] = features,
                ["id"] = g.Id,
                ["name"] = g.NameKey,
                ["icon"] = g.Icon ?? "",
                ["kind"] = Camel(g.Kind.ToString()),
                ["accepts"] = Arr(g.Accepts ?? Array.Empty<string>()),
                ["emits"] = Arr(g.Emits ?? Array.Empty<string>()),
                ["controls"] = controls,
                ["presets"] = presets,
                ["defaults"] = Bag(g.Defaults()),
                ["minArea"] = g.MinArea,
                ["maxArea"] = g.MaxArea,
            };
        }

        // ───────────── compose ─────────────

        public static JsonNode Compose(JsonNode body)
        {
            var sw = Stopwatch.StartNew();
            var recipe = new Recipe { Id = "mock", Root = Step(body?["recipe"]) };
            // s17 park stats: the player's overrides travel with each compose request.
            if (body?["parkStats"] is JsonObject ps)
            {
                recipe.ParkStats = new Placemaker.Core.Build.ParkStatsSettings { KeepVanilla = ps["keepVanilla"]?.GetValue<bool>() ?? true };
                if (ps["overrides"] is JsonObject ov)
                    foreach (var kv in ov) if (kv.Value != null) recipe.ParkStats.Overrides[kv.Key] = kv.Value.GetValue<double>();
            }
            var shape = new AreaShape { Ring = Ring(body?["ring"]), Source = AreaSource.Drawn };
            var site = new SiteContext { Assets = Fixture().items };
            ComposedPlan plan;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    plan = Composer.Compose(recipe, shape, site, cts.Token);
                    // s16: resolve automatic assets like the game does (fixture pools), so the panel can say what Automatic became.
                    var fx = Fixture();
                    AutoAssets.Resolve(plan, new AutoAssetSource
                    {
                        Pool = c => fx.pools.TryGetValue(c ?? "", out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>(),
                        Exists = id => fx.items.ContainsKey(id),
                    });
                }
                catch (OperationCanceledException)
                {
                    plan = new ComposedPlan();
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = "Placemaker.UI.Mock.Timeout" });
                }
            }

            var layers = new JsonArray();
            double cost = 0, upkeep = 0;
            var perLayer = new JsonArray();
            foreach (var l in plan.Layers)
            {
                var (c, u) = FakePrice(l);
                cost += c; upkeep += u;
                layers.Add(new JsonObject
                {
                    ["layer"] = l.GeneratorId,
                    ["stepPath"] = l.StepPath,
                    ["regions"] = new JsonArray(l.Regions.Select(r => (JsonNode)new JsonObject
                    {
                        ["key"] = r.Key, ["tags"] = Arr(r.Tags), ["ring"] = Ring2(r.Ring), ["areaM2"] = Pricing.PolygonArea(r.Ring),
                    }).ToArray()),
                    ["surfaces"] = new JsonArray(l.Surfaces.Select(s => (JsonNode)new JsonObject
                    {
                        ["role"] = s.Role.ToString(), ["ring"] = Ring2(s.Ring), ["lg"] = Placemaker.Core.Build.PreviewLegend.Of(s),
                    }).ToArray()),
                    ["areas"] = new JsonArray(l.Areas.Select(s => (JsonNode)new JsonObject
                    {
                        ["role"] = s.Role.ToString(), ["ring"] = Ring2(s.Ring),
                    }).ToArray()),
                    ["nets"] = new JsonArray(l.Nets.Select(n => (JsonNode)new JsonObject
                    {
                        ["role"] = n.Role.ToString(),
                        ["pts"] = new JsonArray(P(n.A), P(n.B), P(n.C), P(n.D)),
                        ["width"] = !string.IsNullOrEmpty(n.Asset.Id) && site.Assets.TryGetValue(n.Asset.Id, out var ai) && ai.NetWidth > 0.5f ? ai.NetWidth : Composer.DefaultHalfWidth(n.Role) * 2,
                        // s16: road role colour (same as the in-game preview and the panel legend); null for other nets.
                        ["roleColor"] = RoadRoles.Of(n.Role) != null && (n.Role != NetRole.Pathway || n.Control != null) ? RoadRoles.Color(RoadRoles.Of(n.Role)) : null,
                        // s22: the preview legend layer (Core PreviewLegend), so the mock map isolates / hides exactly like the game.
                        ["lg"] = Placemaker.Core.Build.PreviewLegend.Of(n, l.GeneratorId),
                    }).ToArray()),
                    ["objects"] = new JsonArray(l.Objects.Select(o => (JsonNode)new JsonObject
                    {
                        ["role"] = o.Role.ToString(), ["p"] = P(o.Position), ["yaw"] = o.Yaw,
                        ["r"] = Composer.DefaultRadius(o.Role), ["lg"] = Placemaker.Core.Build.PreviewLegend.Of(o),
                    }).ToArray()),
                    ["zoning"] = new JsonArray(l.Zoning.Select(z => (JsonNode)new JsonObject
                    {
                        ["zone"] = z.ZoneId, ["quad"] = new JsonArray(P(z.A), P(z.B), P(z.C), P(z.D)),
                    }).ToArray()),
                    ["terraform"] = new JsonArray(l.Terraform.Select(t => (JsonNode)new JsonObject
                    {
                        ["kind"] = t.Kind.ToString(), ["permanent"] = t.Permanent, ["ring"] = Ring2(t.Ring),
                    }).ToArray()),
                    ["issues"] = Issues(l.Issues, l.StepPath),
                });
                var at = l.StepPath.IndexOf('@');
                perLayer.Add(new JsonObject
                {
                    ["stepPath"] = at >= 0 ? l.StepPath.Substring(0, at) : l.StepPath,
                    ["layer"] = l.GeneratorId,
                    ["regionKey"] = at >= 0 ? l.StepPath.Substring(at + 1) : "",
                    ["stats"] = Stats(l.Stats),
                    ["cost"] = Math.Round(c), ["upkeep"] = Math.Round(u),
                });
            }

            var permanent = new JsonArray();
            foreach (var l in plan.Layers)
                foreach (var t in l.Terraform)
                    if (t.Permanent) permanent.Add(new JsonObject { ["key"] = "Placemaker.UI.Permanent.Terraform" });

            JsonNode parkStats = null;
            var stats = Placemaker.Core.Build.ParkStatsEstimator.Estimate(
                Placemaker.Core.Build.ParkStatsInput.From(plan, Pricing.PolygonArea(shape.Ring), recipe), recipe.ParkStats);
            // No nearby roads in the mock site: the companion has no frontage, like a build without a road edge.
            Placemaker.Core.Build.ParkStatsEstimator.WithCompanion(stats, true,
                Placemaker.Core.Build.ParkCompanionPlanner.Find(Placemaker.Core.Geometry.Poly.To2D(shape.Ring), site.NearbyNets));
            if (stats.Applicable) parkStats = JsonNode.Parse(Placemaker.Core.Tool.UiModels.ParkStats(stats).ToString());


            // s16: per UI step path, what Automatic became and the roads by role (UiModels, as the game sends them).
            var steps = new JsonObject();
            foreach (var l in plan.Layers)
            {
                string path = l.StepPath ?? "";
                int at = path.IndexOf('@');
                if (at >= 0) path = path.Substring(0, at);
                if (path.Length == 0 || steps.ContainsKey(path)) continue;
                var gen = GeneratorRegistry.Get(l.GeneratorId);
                Func<string, string, Placemaker.Core.Tool.AssetLabel> label = (cat, id) => new Placemaker.Core.Tool.AssetLabel { Name = RoadRoles.NameOf(id) };
                steps[path] = new JsonObject
                {
                    ["resolvedAssets"] = JsonNode.Parse(Placemaker.Core.Tool.UiModels.ResolvedAssets(plan, path, gen, label).ToString()),
                    ["netLegend"] = JsonNode.Parse(Placemaker.Core.Tool.UiModels.NetLegend(plan, path, label).ToString()),
                };
            }

            return new JsonObject
            {
                ["steps"] = steps,
                ["parkStats"] = parkStats,
                ["layers"] = layers,
                ["issues"] = Issues(plan.Issues, ""),
                ["stats"] = Stats(plan.MergedStats()),
                ["perLayer"] = perLayer,
                ["cost"] = Math.Round(cost),
                ["upkeep"] = Math.Round(upkeep),
                ["costBuckets"] = FakeBuckets(plan),
                ["permanent"] = permanent,
                ["legend"] = JsonNode.Parse(Placemaker.Core.Tool.UiModels.Legend(plan, recipe, (cat, id) => new Placemaker.Core.Tool.AssetLabel { Name = RoadRoles.NameOf(id) }).ToString()),
                // (sketch study) the top view, as the game sends it on the "sitePlan" binding
                ["sitePlan"] = JsonNode.Parse(Program.SitePlanOf(plan, shape, site, recipe)),
                ["ms"] = sw.Elapsed.TotalMilliseconds,
            };
        }

        /// <summary>(s45) summary.costBuckets with the stand-in prices below, bucketed like the game (Core CostBuckets).</summary>
        private static JsonArray FakeBuckets(ComposedPlan plan)
        {
            var sums = new Dictionary<string, double>();
            void Add(string b, double c) { if (c > 0) sums[b] = (sums.TryGetValue(b, out var v) ? v : 0) + c; }
            foreach (var l in plan.Layers)
            {
                foreach (var o in l.Objects)
                    Add(CostBuckets.Of(o), o.Role switch
                    {
                        ObjectRole.Tree => 150, ObjectRole.Bush => 40, ObjectRole.Centerpiece => 4500,
                        ObjectRole.Bench => 90, ObjectRole.Lamp => 120, ObjectRole.Furniture => 600, ObjectRole.Planter => 70,
                        _ => 60,
                    });
                foreach (var n in l.Nets)
                    Add(CostBuckets.Of(n), Pricing.CurveLength(n) * (n.Role switch
                    {
                        NetRole.Avenue => 220, NetRole.Street or NetRole.PerimeterRoad or NetRole.Roundabout or NetRole.CulDeSac => 120,
                        NetRole.Pathway => 18, NetRole.PathwayWide => 30, NetRole.Fence => 12, _ => 60,
                    }));
                foreach (var sf in l.Surfaces) Add(CostBuckets.Of(sf), Pricing.PolygonArea(sf.Ring) * (sf.Role == SurfaceRole.Grass ? 0.5 : 3));
            }
            var arr = new JsonArray();
            foreach (var kv in sums.OrderByDescending(x => x.Value))
                arr.Add(new JsonObject { ["id"] = kv.Key, ["cost"] = Math.Round(kv.Value), ["upkeep"] = Math.Round(kv.Value * 0.012) });
            return arr;
        }

        /// <summary>Stand-in prices (the game catalog supplies real AssetInfo costs).</summary>
        private static (double cost, double upkeep) FakePrice(AreaPlan l)
        {
            double c = 0;
            foreach (var o in l.Objects)
                c += o.Role switch
                {
                    ObjectRole.Tree => 150, ObjectRole.Bush => 40, ObjectRole.Centerpiece => 4500,
                    ObjectRole.Bench => 90, ObjectRole.Lamp => 120, ObjectRole.Furniture => 600, ObjectRole.Planter => 70,
                    _ => 60,
                };
            foreach (var n in l.Nets)
            {
                double len = Pricing.CurveLength(n);
                c += len * (n.Role switch
                {
                    NetRole.Avenue => 220, NetRole.Street or NetRole.PerimeterRoad or NetRole.Roundabout or NetRole.CulDeSac => 120,
                    NetRole.Pathway => 18, NetRole.PathwayWide => 30, NetRole.Fence => 12, _ => 60,
                });
            }
            foreach (var s in l.Surfaces) c += Pricing.PolygonArea(s.Ring) * (s.Role == SurfaceRole.Grass ? 0.5 : 3);
            return (c, c * 0.012);
        }

        // ───────────── randomize / ready ─────────────

        public static string Repo;
        private static (Dictionary<string, List<string>> pools, Dictionary<string, AssetInfo> items) _fixture;

        /// <summary>The gallery's catalog fixture (automatic pools + net widths), loaded once.</summary>
        private static (Dictionary<string, List<string>> pools, Dictionary<string, AssetInfo> items) Fixture()
        {
            if (_fixture.items != null) return _fixture;
            var pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var items = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(Path.Combine(Repo ?? ".", "Tests", "Placemaker.Tests", "Fixtures", "catalog-bounds.json")));
                foreach (var kv in root["pools"].AsObject()) pools[kv.Key] = kv.Value.AsArray().Select(x => (string)x).ToList();
                foreach (var kv in root["items"].AsObject())
                {
                    var info = new AssetInfo { Id = kv.Key };
                    if (kv.Value?["w"] != null) info.NetWidth = (float)(double)kv.Value["w"];
                    items[kv.Key] = info;
                }
            }
            catch (Exception) { }
            _fixture = (pools, items);
            return _fixture;
        }

        /// <summary>s16: linked controls (road mix) — the values after setting one control, as DesignEditor does in the game.</summary>
        public static JsonNode Linked(JsonNode body)
        {
            var g = GeneratorRegistry.Get((string)body?["layer"] ?? "");
            if (g == null) return new JsonObject { ["error"] = "unknown layer" };
            var bag = Settings(body?["values"]);
            var driven = Array.Empty<string>();
            if (g is ILinkedControls linked)
            {
                driven = linked.OnValueSet(bag, (string)body?["controlId"] ?? "") ?? Array.Empty<string>();
                linked.Derive(bag);
            }
            return new JsonObject { ["values"] = Bag(bag), ["driven"] = Arr(driven) };
        }

        public static JsonNode Randomize(JsonNode body)
        {
            var g = GeneratorRegistry.Get((string)body?["layer"] ?? "");
            if (g == null) return null;
            var current = Settings(body["values"]);
            var locked = new HashSet<string>((body["locked"] as JsonArray)?.Select(x => (string)x) ?? Array.Empty<string>());
            uint seed = (uint)(body["seed"]?.GetValue<double>() ?? 1);
            try { return Bag(g.Randomize(current, seed, locked)); }
            catch (Exception e) { return new JsonObject { ["error"] = e.Message }; }
        }

        public static JsonNode Ready(JsonNode body)
        {
            var g = GeneratorRegistry.Get((string)body?["layer"] ?? "");
            if (g == null) return new JsonObject { ["ready"] = true };
            try
            {
                var r = g.CheckReady(new AreaInput
                {
                    Shape = new AreaShape { Ring = Ring(body["ring"]) },
                    Settings = Settings(body["values"]),
                    Markers = Markers(body["markers"]),
                });
                return new JsonObject
                {
                    ["ready"] = r.Ready, ["nextStep"] = r.NextStepKey ?? "", ["reason"] = r.ReasonKey ?? "",
                    ["args"] = new JsonArray((r.ReasonArgs ?? Array.Empty<object>()).Select(a => (JsonNode)JsonValue.Create(a?.ToString())).ToArray()),
                };
            }
            catch (Exception e) { return new JsonObject { ["ready"] = true, ["error"] = e.Message }; }
        }

        // ───────────── lang ─────────────

        public static JsonNode Lang(string repo)
        {
            var merged = new JsonObject();
            var dir = Path.Combine(repo, "Lang");
            if (!Directory.Exists(dir)) return merged;
            foreach (var f in Directory.GetFiles(dir, "en-US*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(f)) is JsonObject o)
                        foreach (var kv in o) merged[kv.Key] = kv.Value?.DeepClone();
                }
                catch (Exception e) { Console.WriteLine($"Lang file {f}: {e.Message}"); }
            }
            return merged;
        }

        // ───────────── JSON ⇄ Core ─────────────

        private static RecipeStep Step(JsonNode n)
        {
            var s = new RecipeStep();
            if (n == null) return s;
            s.Layer = (string)n["layer"] ?? "";
            s.Target = (string)n["target"] ?? "";
            s.Enabled = n["enabled"]?.GetValue<bool>() ?? true;
            s.Seed = (uint)(n["seed"]?.GetValue<double>() ?? 1);
            s.Settings = Settings(n["values"]);
            if (n["locked"] is JsonArray locked) foreach (var l in locked) s.Locked.Add((string)l);
            s.Markers = Markers(n["markers"]);
            if (n["children"] is JsonArray ch) foreach (var c in ch) s.Children.Add(Step(c));
            if (n["overrides"] is JsonObject ov) foreach (var kv in ov) s.RegionOverrides[kv.Key] = Step(kv.Value);
            return s;
        }

        private static List<EdgeMarker> Markers(JsonNode n)
        {
            var r = new List<EdgeMarker>();
            if (n is JsonArray a)
                foreach (var m in a)
                {
                    var p = m?["p"] as JsonArray;
                    if (p == null || p.Count < 2) continue;
                    r.Add(new EdgeMarker { Kind = (string)m["kind"] ?? "", Position = new float3((float)p[0].GetValue<double>(), 0, (float)p[1].GetValue<double>()) });
                }
            return r;
        }

        private static SettingsBag Settings(JsonNode n)
        {
            var bag = new SettingsBag();
            if (n is not JsonObject o) return bag;
            foreach (var kv in o)
            {
                switch (kv.Value?.GetValueKind())
                {
                    case JsonValueKind.Number: bag.Values[kv.Key] = kv.Value.GetValue<double>(); break;
                    case JsonValueKind.True: bag.Values[kv.Key] = true; break;
                    case JsonValueKind.False: bag.Values[kv.Key] = false; break;
                    case JsonValueKind.String: bag.Values[kv.Key] = kv.Value.GetValue<string>(); break;
                    case JsonValueKind.Array: bag.Values[kv.Key] = ((JsonArray)kv.Value).Select(x => x?.ToString() ?? "").ToArray(); break;
                }
            }
            return bag;
        }

        private static float3[] Ring(JsonNode n)
        {
            if (n is not JsonArray a) return Array.Empty<float3>();
            var pts = new List<float3>();
            foreach (var p in a)
                if (p is JsonArray xy && xy.Count >= 2)
                    pts.Add(new float3((float)xy[0].GetValue<double>(), 0, (float)xy[1].GetValue<double>()));
            // Core expects counter-clockwise in XZ (positive shoelace on x,z).
            double s = 0;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++) s += (double)pts[j].x * pts[i].z - (double)pts[i].x * pts[j].z;
            if (s < 0) pts.Reverse();
            return pts.ToArray();
        }

        private static JsonNode Value(object v) => v switch
        {
            null => null,
            bool b => JsonValue.Create(b),
            string s => JsonValue.Create(s),
            string[] a => Arr(a),
            IConvertible c => JsonValue.Create(c.ToDouble(System.Globalization.CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(v.ToString()),
        };

        private static JsonObject Bag(SettingsBag b)
        {
            var o = new JsonObject();
            if (b == null) return o;
            foreach (var kv in b.Values) o[kv.Key] = Value(kv.Value);
            return o;
        }

        private static JsonArray Stats(IEnumerable<PlanStat> stats) =>
            new JsonArray(stats.Select(s => (JsonNode)new JsonObject { ["key"] = s.Key, ["value"] = s.Value, ["unit"] = s.Unit ?? "" }).ToArray());

        private static JsonArray Issues(IEnumerable<PlanIssue> issues, string stepPath) =>
            new JsonArray(issues.Select(i =>
            {
                var o = new JsonObject
                {
                    ["severity"] = i.Severity switch { IssueSeverity.Error => "error", IssueSeverity.Warning => "warning", _ => "info" },
                    ["text"] = new JsonObject
                    {
                        ["key"] = i.Key,
                        ["args"] = new JsonArray((i.Args ?? Array.Empty<object>()).Select(a => (JsonNode)JsonValue.Create(a?.ToString() ?? "")).ToArray()),
                    },
                };
                if (!string.IsNullOrEmpty(stepPath)) { int at = stepPath.IndexOf('@'); o["stepPath"] = at >= 0 ? stepPath.Substring(0, at) : stepPath; }
                if (i.At.HasValue) o["at"] = new JsonArray(i.At.Value.x, i.At.Value.y, i.At.Value.z);
                return (JsonNode)o;
            }).ToArray());

        private static JsonArray Arr(IEnumerable<string> s) => new JsonArray(s.Select(x => (JsonNode)JsonValue.Create(x)).ToArray());
        private static JsonArray P(float3 p) => new JsonArray(Math.Round(p.x, 2), Math.Round(p.z, 2));
        private static JsonArray Ring2(float3[] ring) => new JsonArray((ring ?? Array.Empty<float3>()).Select(p => (JsonNode)P(p)).ToArray());
        private static string Camel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
