// AI builder (docs/AI-BUILDER.md), phase one: the agent's tools, as OpenAI-style function definitions and their
// execution against AiPlan.Current. Coordinates are the site brief's local metres; every placing tool answers with the
// problems it caused (AiChecks), so the AI fixes them as it goes. Runs on the agent's worker: data only, no game state.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Ai;
using Unity.Mathematics;

namespace Placemaker.Game.Ai
{
    public static class AiTools
    {
        public sealed class Result
        {
            public string Text = "";      // to the AI
            public string LineKey;        // to the player's chat: a step line (localisation key), or null
            public object[] LineArgs = new object[0];
            public string Note;           // the AI's note to the player (finish)
            public bool Map;              // attach a fresh map after this round
            public List<(string label, AiPictures.Picture pic)> Pictures;   // asset pictures to show after this round
            public bool Finished;
        }

        private static string F(float v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        private static JObject Fn(string name, string description, JObject properties, params string[] required) => new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JArray(required) },
            },
        };

        private static JObject Num(string d) => new JObject { ["type"] = "number", ["description"] = d };
        private static JObject Str(string d) => new JObject { ["type"] = "string", ["description"] = d };

        private static JArray s_defs;

        public static JArray Definitions() => s_defs ?? (s_defs = new JArray
        {
            Fn("search_assets", "Find assets in the player's game by words: every placeable object it has loaded, vanilla and mods (props, trees, plants, fountains, statues, decals...; no buildings). Words match the id (split at capitals), the name, the category and tags. Returns id, name, category and real size (width x depth x height, metres). Use only ids it returns.",
                new JObject
                {
                    ["query"] = Str("words, e.g. 'oak tree', 'modern bench', 'street lamp', 'fountain'"),
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "optional: tree, bush, hedge, flowerbed, planter, bench, lamp, bin, centerpiece, playground, sports, kiosk, shelter, picnic, decoration, fence, bollard, rock, decal, stage, lookout, surface (areas for place_surface), path (line assets for place_path), or object (everything not sorted into these)" },
                    ["group"] = Str("optional: one of the game's menu groups from asset_groups"),
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "how many (default 25, max 60)" },
                }, "query"),
            Fn("asset_groups", "What kinds of assets this game has: every category and every menu group of the game (vanilla and mods) with how many assets each holds. Browse it before searching, then search_assets with category or group.", new JObject()),
            Fn("look_at_assets", "See what assets look like: a picture of each (up to 12 per call) is attached after this round, labelled with its id, size and how its front points. Use it to choose between candidates and to know which side is the front before you turn things. Assets without a picture are listed.",
                new JObject { ["ids"] = new JObject { ["type"] = "array", ["description"] = "asset ids from search_assets", ["items"] = new JObject { ["type"] = "string" } } }, "ids"),
            Fn("place_objects", "Place objects (trees, bushes, benches, lamps, props). Each sits on the ground unless elevation is given. Answers with the ids and the problems each one caused.",
                new JObject
                {
                    ["objects"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["asset"] = Str("asset id from search_assets"),
                                ["x"] = Num("local x, metres east"),
                                ["z"] = Num("local z, metres north"),
                                ["yaw"] = Num("degrees, 0 = facing north, 90 = facing east"),
                                ["elevation"] = Num("optional: metres above the ground (stacking); negative sinks it into the ground"),
                                ["scale"] = Num("optional: size factor, 1 = as made (0.2-5; e.g. 2.5 for a monumental statue)"),
                                ["roll"] = Num("optional: tilt in degrees about its length (90 = on its side)"),
                                ["pitch"] = Num("optional: tilt in degrees about its width"),
                            },
                            ["required"] = new JArray("asset", "x", "z"),
                        },
                    },
                }, "objects"),
            Fn("place_path", "Place a line along a polyline of points: a footpath by default, or any line asset from search_assets (category path: narrow, wide, gravel, dirt trails...; category fence: fences, railings, hedges made as lines). Paths should join a road, another path or a surface at their ends, or close into a loop. height raises it level above the ground (a boardwalk, a pier, a bridge).",
                new JObject
                {
                    ["points"] = new JObject { ["type"] = "array", ["description"] = "[[x, z], ...] local metres, at least 2", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["wide"] = new JObject { ["type"] = "boolean", ["description"] = "the game's wide path instead of the normal one (when no asset is given)" },
                    ["asset"] = Str("optional: a line asset id (path or fence) from search_assets"),
                    ["height"] = Num("optional: a fixed height, m above sea level, for a raised level line (boardwalk, pier)"),
                    ["smooth"] = new JObject { ["type"] = "boolean", ["description"] = "draw a smooth curve through the points instead of straight lines between them (default false)" },
                }, "points"),
            Fn("scatter_objects", "Scatter many objects at once inside an area you give (a wood, a clump, a meadow of flowers, rocks): random positions at least 'spacing' apart, random yaw, the assets mixed evenly. With clumps > 0 they gather in patches (a noise field about 5 spacings across) with gaps between. Points that would sit outside the selection, on a road, path or building, or overlap something already placed are skipped. The answer lists every object placed (id, asset, x, z, yaw) and how many were skipped for what, so you can move or remove any of them.",
                new JObject
                {
                    ["assets"] = new JObject { ["type"] = "array", ["description"] = "asset ids to mix (from search_assets)", ["items"] = new JObject { ["type"] = "string" } },
                    ["polygon"] = new JObject { ["type"] = "array", ["description"] = "the area: [[x, z], ...] local metres (or give circle; neither = the whole selection)", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["circle"] = new JObject { ["type"] = "array", ["description"] = "the area as [x, z, radius]", ["items"] = new JObject { ["type"] = "number" } },
                    ["count"] = new JObject { ["type"] = "integer", ["description"] = "how many at most (up to 400)" },
                    ["spacing"] = Num("minimum distance between them, metres"),
                    ["clumps"] = Num("0 = spread evenly, up to 1 = strongly in patches"),
                    ["seed"] = new JObject { ["type"] = "integer", ["description"] = "change it for a different arrangement" },
                }, "assets", "count", "spacing"),
            Fn("shape_ground", "Shape the terrain inside a polygon: level it to a height, raise only (fill up to it), lower only (dig down to it), or slope it through a height per corner. Heights are metres above sea level (the brief's heights; the map's contour lines). Everything placed sits on the new ground. The answer says the ground before (lowest, highest inside) and the steps it makes at its edges, so you can soften them with sloped patches around it.",
                new JObject
                {
                    ["polygon"] = new JObject { ["type"] = "array", ["description"] = "[[x, z], ...] local metres", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("level", "raise", "lower"), ["description"] = "level (default): exactly to height; raise: only where the ground is lower; lower: only where it is higher" },
                    ["height"] = Num("target height, m above sea level"),
                    ["corner_heights"] = new JObject { ["type"] = "array", ["description"] = "optional: one height per polygon corner for a slope (the ground becomes the surface through them)", ["items"] = new JObject { ["type"] = "number" } },
                    ["water_level"] = Num("optional: fill the shaped basin with water up to this height (m above sea level; set it below the lowest shore, and the basin floor below it)"),
                }, "polygon"),
            Fn("paint_ground", "Paint the ground's texture inside a polygon with one of the map's ground paints (listed in the brief: e.g. dry grass, lush grass, dirt, sand; 0 erases). Soft edge and edge noise make it look natural. Permanent in the city, like terrain shaping.",
                new JObject
                {
                    ["polygon"] = new JObject { ["type"] = "array", ["description"] = "[[x, z], ...] local metres", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["layer"] = new JObject { ["type"] = "integer", ["description"] = "1-4: the map's ground paints from the brief; 0 = erase" },
                    ["strength"] = Num("0-1 (default 1)"),
                    ["feather"] = Num("soft edge, metres (default 3)"),
                    ["noise"] = Num("ragged edge, metres (default 0)"),
                }, "polygon", "layer"),
            Fn("place_surface", "Lay a surface (an area of paving, gravel, grass, sand... from search_assets with category surface) inside a polygon, e.g. a plaza, a terrace, a sandpit. The polygon must be simple; keep it off roads.",
                new JObject
                {
                    ["polygon"] = new JObject { ["type"] = "array", ["description"] = "[[x, z], ...] local metres", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["asset"] = Str("surface asset id"),
                }, "polygon", "asset"),
            Fn("remove", "Remove placed objects or paths by id.", new JObject { ["ids"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } } }, "ids"),
            Fn("list_placed", "List everything placed so far (id, asset, position, yaw).", new JObject()),
            Fn("check", "Check everything placed: overlaps, outside the selection, on roads or buildings, dead-end paths.", new JObject()),
            Fn("view_map", "See the map again with everything placed so far drawn on it (attached after this round).", new JObject()),
            Fn("finish", "Done: hand the result to the player for review.", new JObject { ["note"] = Str("two sentences for the player: what you built") }, "note"),
            Fn("wish", "Note a tool or a piece of information you needed and did not have.", new JObject { ["text"] = Str("what was missing and what it would have let you do") }, "text"),
        });

        /// <summary>The short progress line shown while a tool runs (localisation key and its arguments).</summary>
        public static (string key, object[] args) Doing(string name, JObject args)
        {
            switch (name)
            {
                case "search_assets": return ("Placemaker.Ai.Doing.Search", new object[] { (string)args["query"] ?? "" });
                case "look_at_assets": return ("Placemaker.Ai.Doing.Look", new object[] { (args["ids"] as JArray)?.Count ?? 0 });
                case "place_objects": return ("Placemaker.Ai.Doing.Place", new object[] { (args["objects"] as JArray)?.Count ?? 0 });
                case "place_path": return ("Placemaker.Ai.Doing.Path", new object[0]);
                case "shape_ground": return ("Placemaker.Ai.Doing.Shape", new object[0]);
                case "paint_ground": return ("Placemaker.Ai.Doing.Paint", new object[0]);
                case "place_surface": return ("Placemaker.Ai.Doing.Surface", new object[0]);
                case "scatter_objects": return ("Placemaker.Ai.Doing.Place", new object[] { (int?)args["count"] ?? 0 });
                case "remove": return ("Placemaker.Ai.Doing.Remove", new object[] { (args["ids"] as JArray)?.Count ?? 0 });
                case "check": return ("Placemaker.Ai.Doing.Check", new object[0]);
                case "view_map": return ("Placemaker.Ai.Doing.Map", new object[0]);
                case "finish": return ("Placemaker.Ai.Doing.Finish", new object[0]);
                default: return ("Placemaker.Ai.Doing.Think", new object[0]);
            }
        }

        public static ObjectRole RoleFor(string category)
        {
            switch (category)
            {
                case AssetCategories.Tree: return ObjectRole.Tree;
                case AssetCategories.Bush: case AssetCategories.Hedge: return ObjectRole.Bush;
                case AssetCategories.Bench: return ObjectRole.Bench;
                case AssetCategories.Lamp: return ObjectRole.Lamp;
                case AssetCategories.Bin: return ObjectRole.Bin;
                case AssetCategories.Planter: return ObjectRole.Planter;
                default:
                    if (category != null && category.StartsWith("decal")) return ObjectRole.Decal;
                    if (category != null && category.StartsWith("centerpiece")) return ObjectRole.Centerpiece;
                    return ObjectRole.Furniture;
            }
        }

        public static Result Run(string name, JObject args, AiSite site, List<AiAsset> assets, List<string> wishes)
        {
            try
            {
                switch (name)
                {
                    case "search_assets": return Search(args, assets);
                    case "look_at_assets": return Look(args, site);
                    case "asset_groups":
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("categories: " + string.Join(", ", assets.GroupBy(a => a.Category).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count())));
                        sb.AppendLine("game menu groups: " + string.Join(", ", assets.Where(a => !string.IsNullOrEmpty(a.Group)).GroupBy(a => a.Group).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count())));
                        sb.AppendLine($"{assets.Count(a => !a.Vanilla)} of {assets.Count} assets come from mods.");
                        return new Result { Text = sb.ToString() };
                    }
                    case "place_objects": return PlaceObjects(args, site);
                    case "place_path": return PlacePath(args, site);
                    case "shape_ground": return ShapeGround(args, site);
                    case "paint_ground": return PaintGround(args, site);
                    case "place_surface": return PlaceSurface(args, site);
                    case "scatter_objects": return Scatter(args, site);
                    case "remove":
                    {
                        var ids = (args["ids"] as JArray)?.Select(t => (int)t).ToList() ?? new List<int>();
                        int n = ids.Count(id => AiPlan.Current.Remove(id));
                        return new Result { Text = $"removed {n} of {ids.Count}" };
                    }
                    case "list_placed":
                    {
                        var all = AiPlan.Current.Snapshot();
                        if (all.Count == 0) return new Result { Text = "nothing placed yet" };
                        var sb = new StringBuilder();
                        foreach (var p in all)
                        {
                            if (p.Kind == AiPieceKind.Object)
                            {
                                var l = site.Local(p.Position);
                                sb.AppendLine($"#{p.Id} {p.Asset} ({F(l.x)},{F(l.y)}) yaw {F(math.degrees(p.Yaw))}{(p.Elevation != 0 ? " elev " + F(p.Elevation) : "")}");
                            }
                            else if (p.Kind == AiPieceKind.Path) sb.AppendLine($"#{p.Id} path{(p.NetRole == NetRole.PathwayWide ? " (wide)" : "")} " + string.Join(" ", p.Points.Select(q => { var l = site.Local(q); return $"({F(l.x)},{F(l.y)})"; })));
                            else sb.AppendLine($"#{p.Id} {p.Kind.ToString().ToLowerInvariant()}{(p.Kind == AiPieceKind.Surface ? " " + p.Asset : p.Kind == AiPieceKind.Paint ? " layer " + p.Layer : " " + p.Shape.ToString().ToLowerInvariant() + (p.CornerHeights != null ? " sloped" : " to " + F(p.Height) + " m"))} " + string.Join(" ", p.Ring.Select(q => { var l = site.Local(q); return $"({F(l.x)},{F(l.y)})"; })));
                        }
                        return new Result { Text = sb.ToString() };
                    }
                    case "check":
                    {
                        var lines = AiChecks.All(site, AiPlan.Current.Snapshot());
                        return new Result
                        {
                            Text = lines.Count == 0 ? $"no problems ({AiPlan.Current.Count} pieces)" : $"{lines.Count} pieces with problems:\n" + string.Join("\n", lines),
                            LineKey = lines.Count == 0 ? "Placemaker.Ai.Line.CheckClean" : "Placemaker.Ai.Line.CheckProblems", LineArgs = new object[] { lines.Count },
                        };
                    }
                    case "view_map": return new Result { Text = "the map is attached below", Map = true };
                    case "finish":
                        return new Result { Text = "handed to the player", Note = (string)args["note"] ?? "", Finished = true };
                    case "wish":
                    {
                        var w = (string)args["text"] ?? "";
                        lock (wishes) wishes.Add(w);
                        return new Result { Text = "noted", LineKey = "Placemaker.Ai.Line.Wish", LineArgs = new object[] { w } };
                    }
                    default: return new Result { Text = "unknown tool " + name };
                }
            }
            catch (Exception e) { return new Result { Text = "error: " + e.Message }; }
        }

        private static Result Look(JObject args, AiSite site)
        {
            var ids = (args["ids"] as JArray)?.Select(t => (string)t).Where(t => !string.IsNullOrEmpty(t)).Distinct().Take(12).ToList() ?? new List<string>();
            var pics = new List<(string, AiPictures.Picture)>();
            var none = new List<string>();
            foreach (var id in ids)
            {
                if (!site.Assets.TryGetValue(id, out var a)) { none.Add(id + " (unknown id)"); continue; }
                var p = AiPictures.For(id, a.Icon);
                if (p == null) { none.Add(id); continue; }
                pics.Add(($"{id}: {F(a.Size.x)} x {F(a.Size.z)} x {F(a.Size.y)} m, {a.Category}; picture from its front-left (its front is its local +z: the direction its yaw points)", p));
            }
            string text = pics.Count > 0 ? $"{pics.Count} picture(s) attached below" : "no pictures";
            if (none.Count > 0) text += "; no picture for: " + string.Join(", ", none);
            return new Result { Text = text, Pictures = pics, LineKey = "Placemaker.Ai.Line.Looked", LineArgs = new object[] { pics.Count } };
        }

        private static Result Search(JObject args, List<AiAsset> assets)
        {
            string query = ((string)args["query"] ?? "").ToLowerInvariant();
            string category = (string)args["category"];
            string group = (string)args["group"];
            int limit = math.clamp((int?)args["limit"] ?? 25, 1, 60);
            var words = query.Split(new[] { ' ', ',', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(AiAsset a, int score)>();
            foreach (var a in assets)
            {
                if (!string.IsNullOrEmpty(category) && a.Category != category && !a.Category.StartsWith(category + ".")) continue;
                if (!string.IsNullOrEmpty(group) && !string.Equals(a.Group, group, StringComparison.OrdinalIgnoreCase)) continue;
                string hay = (a.Group + " " + a.Id + " " + System.Text.RegularExpressions.Regex.Replace(a.Id, "([a-z])([A-Z])", "$1 $2") + " " + a.Name + " " + a.Category + " " + string.Join(" ", a.Tags)).ToLowerInvariant();
                int score = words.Count(w => hay.Contains(w)) * 10;
                if (words.Length > 0 && score == 0 && string.IsNullOrEmpty(category) && string.IsNullOrEmpty(group)) continue;
                if (a.Vanilla) score += 1;
                if (a.Pick >= 0) score += 5 - math.min(4, a.Pick / 3);   // the premade types' own choices first
                scored.Add((a, score));
            }
            var top = scored.OrderByDescending(t => t.score).ThenBy(t => t.a.Id, StringComparer.Ordinal).Take(limit).ToList();
            if (top.Count == 0) return new Result { Text = "no matches; try other words or a category" };
            var sb = new StringBuilder();
            sb.AppendLine($"{top.Count} of {scored.Count} matches, the premade types' own choices first (id | name | category | width x depth x height m; plants: how wide the crown grows):");
            foreach (var (a, _) in top) sb.AppendLine($"{a.Id} | {a.Name} | {a.Category}{(a.Line ? " line" : "")}{(a.Vanilla ? "" : " (mod)")}{(a.HasPicture ? "" : " (no picture)")} | {F(a.Size.x)} x {F(a.Size.z)} x {F(a.Size.y)}" + (a.Plant ? $" | crown about {F(a.Reach * 2f)} m across" : ""));
            return new Result { Text = sb.ToString() };
        }

        private static Result PlaceObjects(JObject args, AiSite site)
        {
            var items = args["objects"] as JArray;
            if (items == null || items.Count == 0) return new Result { Text = "no objects given" };
            var placed = new List<AiPiece>();
            var sb = new StringBuilder();
            int refused = 0;
            foreach (var it in items.Take(200))
            {
                string asset = (string)it["asset"] ?? "";
                if (!site.Assets.TryGetValue(asset, out var a)) { refused++; sb.AppendLine($"refused '{asset}': unknown asset id (use search_assets)"); continue; }
                if (a.Line) { refused++; sb.AppendLine($"refused '{asset}': a line asset (use place_path with asset)"); continue; }
                var p = new AiPiece
                {
                    Kind = AiPieceKind.Object, Asset = asset, Category = a.Category, Role = RoleFor(a.Category),
                    Position = site.World(new float2((float?)it["x"] ?? 0f, (float?)it["z"] ?? 0f)),
                    Yaw = math.radians((float?)it["yaw"] ?? 0f), Elevation = (float?)it["elevation"] ?? 0f,
                    Scale = math.clamp((float?)it["scale"] ?? 1f, 0.2f, 5f), Roll = math.radians((float?)it["roll"] ?? 0f), Pitch = math.radians((float?)it["pitch"] ?? 0f),
                };
                AiPlan.Current.Add(p);
                placed.Add(p);
            }
            var all = AiPlan.Current.Snapshot();
            int withProblems = 0;
            foreach (var p in placed)
            {
                var pr = AiChecks.Problems(site, p, all);
                if (pr.Count == 0) continue;
                withProblems++;
                var l = site.Local(p.Position);
                sb.AppendLine($"#{p.Id} {p.Asset} at ({F(l.x)},{F(l.y)}): {string.Join("; ", pr)}");
            }
            string head = $"placed {placed.Count} (ids {(placed.Count > 0 ? placed.First().Id + "-" + placed.Last().Id : "none")}), {withProblems} with problems{(refused > 0 ? ", " + refused + " refused" : "")}";
            return new Result { Text = head + (sb.Length > 0 ? ":\n" + sb : ""), LineKey = "Placemaker.Ai.Line.Placed", LineArgs = new object[] { placed.Count, withProblems } };
        }

        private static Result Scatter(JObject args, AiSite site)
        {
            var ids = (args["assets"] as JArray)?.Select(t => (string)t).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>();
            var unknown = ids.Where(id => !site.Assets.ContainsKey(id)).ToList();
            ids = ids.Where(id => site.Assets.ContainsKey(id)).ToList();
            if (ids.Count == 0) return new Result { Text = "no known asset ids" + (unknown.Count > 0 ? " (unknown: " + string.Join(", ", unknown) + ")" : "") };
            float2[] area;
            if (args["polygon"] is JArray poly && poly.Count >= 3) area = poly.Select(t => site.World(new float2((float)t[0], (float)t[1]))).ToArray();
            else if (args["circle"] is JArray c && c.Count >= 3) area = AiShapes.Circle(site.World(new float2((float)c[0], (float)c[1])), (float)c[2]);
            else area = site.Ring;
            int count = math.clamp((int?)args["count"] ?? 10, 1, 400);
            float spacing = math.max(0.5f, (float?)args["spacing"] ?? 6f);
            float clumps = math.saturate((float?)args["clumps"] ?? 0f);
            uint seed = (uint)math.max(1, (int?)args["seed"] ?? 1);
            var rng = new Unity.Mathematics.Random(seed * 7919u + 13u);
            var existing = AiPlan.Current.Snapshot();
            var placed = new List<AiPiece>();
            var skipped = new Dictionary<string, int>();
            int k = 0;
            AiShapes.Scatter(area, count, spacing, clumps, seed, p =>
            {
                var id = ids[k++ % ids.Count];
                var a = site.Assets[id];
                var piece = new AiPiece { Kind = AiPieceKind.Object, Asset = id, Category = a.Category, Role = RoleFor(a.Category), Position = p, Yaw = rng.NextFloat(0f, 2f * math.PI) };
                var pr = AiChecks.Problems(site, piece, existing.Concat(placed).ToList());
                if (pr.Count > 0)
                {
                    // the first word group of the first problem names the reason ("outside the selection", "overlaps", "on path")
                    string why = pr[0].Split('(')[0].Split('#')[0].Trim();
                    skipped[why] = (skipped.TryGetValue(why, out var n) ? n : 0) + 1;
                    return false;
                }
                placed.Add(piece);
                return true;
            });
            foreach (var p in placed) AiPlan.Current.Add(p);
            var sb = new StringBuilder();
            sb.AppendLine($"scattered {placed.Count} of {count}" + (placed.Count < count ? " (the area had no room for more at that spacing)" : "") + ":");
            foreach (var p in placed)
            {
                var l = site.Local(p.Position);
                sb.AppendLine($"#{p.Id} {p.Asset} ({F(l.x)},{F(l.y)}) yaw {F(math.degrees(p.Yaw))}");
            }
            if (skipped.Count > 0) sb.AppendLine("points skipped: " + string.Join(", ", skipped.Select(kv => kv.Value + " " + kv.Key)));
            if (unknown.Count > 0) sb.AppendLine("unknown ids ignored: " + string.Join(", ", unknown));
            return new Result { Text = sb.ToString(), LineKey = "Placemaker.Ai.Line.Placed", LineArgs = new object[] { placed.Count, 0 } };
        }

        private static float2[] Polygon(JObject args, AiSite site) =>
            (args["polygon"] as JArray)?.Select(t => site.World(new float2((float)t[0], (float)t[1]))).ToArray();

        private static string Problems(AiSite site, AiPiece p)
        {
            var pr = AiChecks.Problems(site, p, AiPlan.Current.Snapshot());
            return pr.Count == 0 ? "no problems" : string.Join("; ", pr);
        }

        private static Result ShapeGround(JObject args, AiSite site)
        {
            var ring = Polygon(args, site);
            if (ring == null || ring.Length < 3) return new Result { Text = "a polygon needs at least 3 corners" };
            if (Placemaker.Core.Geometry.Poly.SignedArea(ring) < 0) Array.Reverse(ring);
            string mode = ((string)args["mode"] ?? "level").ToLowerInvariant();
            var corners = (args["corner_heights"] as JArray)?.Select(t => (float)t).ToArray();
            if (corners != null && corners.Length != ring.Length) return new Result { Text = $"corner_heights has {corners.Length} heights for {ring.Length} corners" };
            if (corners != null && Placemaker.Core.Geometry.Poly.SignedArea((args["polygon"] as JArray).Select(t => new float2((float)t[0], (float)t[1])).ToArray()) < 0) Array.Reverse(corners);
            float? height = (float?)args["height"];
            if (corners == null && height == null) return new Result { Text = "give height, or corner_heights for a slope" };
            var p = new AiPiece
            {
                Kind = AiPieceKind.Ground, Ring = ring, CornerHeights = corners, Height = height ?? corners.Average(),
                Shape = mode == "raise" ? TerraformKind.Raise : mode == "lower" ? TerraformKind.Lower : TerraformKind.Level,
                WaterLevel = (float?)args["water_level"] ?? float.NaN,
            };
            // the ground before, inside and along the edge (the steps it makes)
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var q in ring) { lo = math.min(lo, q); hi = math.max(hi, q); }
            float gmin = float.MaxValue, gmax = float.MinValue;
            for (float x = lo.x; x <= hi.x; x += 2f)
                for (float z = lo.y; z <= hi.y; z += 2f)
                {
                    var q = new float2(x, z);
                    if (!Placemaker.Core.Geometry.Poly.Contains(ring, q)) continue;
                    float g = site.Ground(q);
                    gmin = math.min(gmin, g); gmax = math.max(gmax, g);
                }
            float maxStep = 0f; float2 stepAt = ring[0];
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k]; var b = ring[(k + 1) % ring.Length];
                float ta = corners != null ? corners[k] : p.Height, tb = corners != null ? corners[(k + 1) % ring.Length] : p.Height;
                for (int i = 0; i <= 8; i++)
                {
                    var q = math.lerp(a, b, i / 8f);
                    float target = math.lerp(ta, tb, i / 8f), g = site.Ground(q);
                    float step = p.Shape == TerraformKind.Raise ? math.max(0f, target - g) : p.Shape == TerraformKind.Lower ? math.max(0f, g - target) : math.abs(target - g);
                    if (step > maxStep) { maxStep = step; stepAt = q; }
                }
            }
            AiPlan.Current.Add(p);
            var at = site.Local(stepAt);
            string water = "";
            if (math.isfinite(p.WaterLevel))
            {
                float floor = p.Shape == TerraformKind.Raise ? math.max(p.Height, gmin) : p.Height;
                water = floor < p.WaterLevel - 0.3f ? $"; water at {F(p.WaterLevel)} m over a floor at {F(floor)} m"
                    : $"; WARNING: the floor ({F(floor)} m) is not below the water level ({F(p.WaterLevel)} m): no water will appear (lower the basin, and keep islands small so the deepest part stays open water)";
            }
            string ground = gmin <= gmax ? $"ground before: {F(gmin)} to {F(gmax)} m" : "ground before: unknown";
            return new Result
            {
                Text = $"ground #{p.Id} ({mode}{(corners != null ? ", sloped" : $" to {F(p.Height)} m")}): {ground}; largest step at its edge {F(maxStep)} m near ({F(at.x)},{F(at.y)}){water}; {Problems(site, p)}",
                LineKey = "Placemaker.Ai.Line.Shape", LineArgs = new object[] { (int)math.round(maxStep * 10f) / 10f },
            };
        }

        private static Result PaintGround(JObject args, AiSite site)
        {
            var ring = Polygon(args, site);
            if (ring == null || ring.Length < 3) return new Result { Text = "a polygon needs at least 3 corners" };
            if (Placemaker.Core.Geometry.Poly.SignedArea(ring) < 0) Array.Reverse(ring);
            int layer = math.clamp((int?)args["layer"] ?? 1, 0, 4);
            var p = new AiPiece
            {
                Kind = AiPieceKind.Paint, Ring = ring, Layer = layer, Strength = math.saturate((float?)args["strength"] ?? 1f),
                Feather = math.max(0f, (float?)args["feather"] ?? 3f), Noise = math.max(0f, (float?)args["noise"] ?? 0f),
            };
            AiPlan.Current.Add(p);
            float area = math.abs(Placemaker.Core.Geometry.Poly.SignedArea(ring));
            return new Result { Text = $"paint #{p.Id}, layer {layer}, {F(area)} m²; {Problems(site, p)}", LineKey = "Placemaker.Ai.Line.Paint", LineArgs = new object[] { (int)area } };
        }

        public static SurfaceRole SurfaceRoleFor(string category)
        {
            switch (category)
            {
                case AssetCategories.SurfaceGrass: return SurfaceRole.Grass;
                case AssetCategories.SurfaceAsphalt: return SurfaceRole.Asphalt;
                case AssetCategories.SurfaceWater: return SurfaceRole.Water;
                case AssetCategories.SurfacePaving: return SurfaceRole.Paving;
                default: return category != null && category.StartsWith("surface.") && category != AssetCategories.SurfacePaving ? SurfaceRole.Ground : SurfaceRole.Paving;
            }
        }

        private static Result PlaceSurface(JObject args, AiSite site)
        {
            var ring = Polygon(args, site);
            if (ring == null || ring.Length < 3) return new Result { Text = "a polygon needs at least 3 corners" };
            if (Placemaker.Core.Geometry.Poly.SignedArea(ring) < 0) Array.Reverse(ring);
            string asset = (string)args["asset"] ?? "";
            if (!site.Assets.TryGetValue(asset, out var a) || a.Category == null || !a.Category.StartsWith("surface"))
                return new Result { Text = $"refused '{asset}': not a surface asset (search_assets with category surface)" };
            var p = new AiPiece { Kind = AiPieceKind.Surface, Ring = ring, Asset = asset, Category = a.Category, SurfaceRole = SurfaceRoleFor(a.Category) };
            AiPlan.Current.Add(p);
            float area = math.abs(Placemaker.Core.Geometry.Poly.SignedArea(ring));
            return new Result { Text = $"surface #{p.Id} {asset}, {F(area)} m²; {Problems(site, p)}", LineKey = "Placemaker.Ai.Line.Surface", LineArgs = new object[] { (int)area } };
        }

        private static Result PlacePath(JObject args, AiSite site)
        {
            var pts = (args["points"] as JArray)?.Select(t => new float2((float)t[0], (float)t[1])).Select(site.World).ToArray();
            if (pts == null || pts.Length < 2) return new Result { Text = "a path needs at least 2 points" };
            if ((bool?)args["smooth"] == true && pts.Length >= 3) pts = AiShapes.Smooth(pts, 5f);
            string asset = (string)args["asset"] ?? "";
            AiAsset la = null;
            if (asset.Length > 0 && (!site.Assets.TryGetValue(asset, out la) || !la.Line)) return new Result { Text = $"refused '{asset}': not a line asset (search_assets with category path or fence)" };
            bool fence = la != null && la.Category == AssetCategories.Fence;
            var p = new AiPiece
            {
                Kind = AiPieceKind.Path, Points = pts, Asset = asset, Category = fence ? AssetCategories.Fence : AssetCategories.Pathway,
                NetRole = fence ? NetRole.Fence : (bool?)args["wide"] == true ? NetRole.PathwayWide : NetRole.Pathway,
                LineHeight = (float?)args["height"] ?? float.NaN,
            };
            AiPlan.Current.Add(p);
            var pr = AiChecks.Problems(site, p, AiPlan.Current.Snapshot());
            float len = 0;
            for (int k = 0; k + 1 < pts.Length; k++) len += math.distance(pts[k], pts[k + 1]);
            string head = $"path #{p.Id}, {F(len)} m";
            return new Result { Text = head + (pr.Count > 0 ? ": " + string.Join("; ", pr) : ", no problems"), LineKey = "Placemaker.Ai.Line.Path", LineArgs = new object[] { (int)math.round(len), pr.Count } };
        }
    }
}
