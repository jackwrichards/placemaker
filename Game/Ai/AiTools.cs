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
            Fn("search_assets", "Find assets in the player's game (vanilla and mods) by words: name, kind, style. Returns id, name, category and real size (width x depth x height, metres). Use only ids it returns.",
                new JObject
                {
                    ["query"] = Str("words, e.g. 'oak tree', 'modern bench', 'street lamp', 'fountain'"),
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "optional: tree, bush, hedge, flowerbed, planter, bench, lamp, bin, centerpiece, centerpiece.fountain, centerpiece.statue, playground, sports, kiosk, shelter, picnic, decoration, fence, bollard, rock, decal, stage, lookout" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "how many (default 25, max 60)" },
                }, "query"),
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
                                ["elevation"] = Num("optional: metres above the ground (stacking)"),
                            },
                            ["required"] = new JArray("asset", "x", "z"),
                        },
                    },
                }, "objects"),
            Fn("place_path", "Place a footpath along a polyline of points. It should join a road or another path at its ends (within 3 m), or close into a loop.",
                new JObject
                {
                    ["points"] = new JObject { ["type"] = "array", ["description"] = "[[x, z], ...] local metres, at least 2", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["wide"] = new JObject { ["type"] = "boolean", ["description"] = "a wide path (6 m) instead of a normal one (4 m)" },
                }, "points"),
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
                case "place_objects": return ("Placemaker.Ai.Doing.Place", new object[] { (args["objects"] as JArray)?.Count ?? 0 });
                case "place_path": return ("Placemaker.Ai.Doing.Path", new object[0]);
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
                    case "place_objects": return PlaceObjects(args, site);
                    case "place_path": return PlacePath(args, site);
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
                            else sb.AppendLine($"#{p.Id} path{(p.NetRole == NetRole.PathwayWide ? " (wide)" : "")} " + string.Join(" ", p.Points.Select(q => { var l = site.Local(q); return $"({F(l.x)},{F(l.y)})"; })));
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

        private static Result Search(JObject args, List<AiAsset> assets)
        {
            string query = ((string)args["query"] ?? "").ToLowerInvariant();
            string category = (string)args["category"];
            int limit = math.clamp((int?)args["limit"] ?? 25, 1, 60);
            var words = query.Split(new[] { ' ', ',', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(AiAsset a, int score)>();
            foreach (var a in assets)
            {
                if (!string.IsNullOrEmpty(category) && a.Category != category && !a.Category.StartsWith(category + ".")) continue;
                string hay = (a.Id + " " + a.Name + " " + a.Category + " " + string.Join(" ", a.Tags)).ToLowerInvariant();
                int score = words.Count(w => hay.Contains(w)) * 10;
                if (words.Length > 0 && score == 0 && string.IsNullOrEmpty(category)) continue;
                if (a.Vanilla) score += 1;
                scored.Add((a, score));
            }
            var top = scored.OrderByDescending(t => t.score).ThenBy(t => t.a.Id, StringComparer.Ordinal).Take(limit).ToList();
            if (top.Count == 0) return new Result { Text = "no matches; try other words or a category" };
            var sb = new StringBuilder();
            sb.AppendLine($"{top.Count} of {scored.Count} matches (id | name | category | width x depth x height m):");
            foreach (var (a, _) in top) sb.AppendLine($"{a.Id} | {a.Name} | {a.Category} | {F(a.Size.x)} x {F(a.Size.z)} x {F(a.Size.y)}");
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
                var p = new AiPiece
                {
                    Kind = AiPieceKind.Object, Asset = asset, Category = a.Category, Role = RoleFor(a.Category),
                    Position = site.World(new float2((float?)it["x"] ?? 0f, (float?)it["z"] ?? 0f)),
                    Yaw = math.radians((float?)it["yaw"] ?? 0f), Elevation = (float?)it["elevation"] ?? 0f,
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

        private static Result PlacePath(JObject args, AiSite site)
        {
            var pts = (args["points"] as JArray)?.Select(t => new float2((float)t[0], (float)t[1])).Select(site.World).ToArray();
            if (pts == null || pts.Length < 2) return new Result { Text = "a path needs at least 2 points" };
            var p = new AiPiece { Kind = AiPieceKind.Path, Points = pts, NetRole = (bool?)args["wide"] == true ? NetRole.PathwayWide : NetRole.Pathway, Category = AssetCategories.Pathway };
            AiPlan.Current.Add(p);
            var pr = AiChecks.Problems(site, p, AiPlan.Current.Snapshot());
            float len = 0;
            for (int k = 0; k + 1 < pts.Length; k++) len += math.distance(pts[k], pts[k + 1]);
            string head = $"path #{p.Id}, {F(len)} m";
            return new Result { Text = head + (pr.Count > 0 ? ": " + string.Join("; ", pr) : ", no problems"), LineKey = "Placemaker.Ai.Line.Path", LineArgs = new object[] { (int)math.round(len), pr.Count } };
        }
    }
}
