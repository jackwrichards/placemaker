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
            public string Phase;          // the work enters a phase (review, detail, fix): a divider in the chat instead of a tool line
            public string[] Vision;       // the vision tool: name, story, signature, ideas (shown in the chat as a card)
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

        /// <summary>The area properties every area tool takes (polygon, rect, circle, ellipse, wobble), added to its own.</summary>
        private static JObject WithArea(JObject props, string polygonNote = null)
        {
            var pts = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } };
            var nums = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } };
            var area = new JObject
            {
                ["polygon"] = new JObject(pts) { ["description"] = "[[x, z], ...] local metres" + (polygonNote ?? "") },
                ["rect"] = new JObject(nums) { ["description"] = "[x1, z1, x2, z2]: a rectangle from two opposite corners, for built things (courts, car parks, fields, yards, terraces); its corners in order: south-west, south-east, north-east, north-west" },
                ["circle"] = new JObject(nums) { ["description"] = "[x, z, radius]" },
                ["ellipse"] = new JObject(nums) { ["description"] = "[x, z, radius_x, radius_z, turn_degrees (optional, clockwise)]" },
                ["wobble"] = Num("optional: metres of smooth random variation of the edge, for any polygon, circle or ellipse (an organic pond, grove, meadow or bed; 2-5 is natural)"),
                ["strip"] = new JObject(pts) { ["description"] = "a band along a line [[x, z], ...] (give strip_width): worn tracks, flower borders, verges, a gravel edge" },
                ["strip_width"] = Num("with strip: its full width in metres"),
            };
            foreach (var kv in props) area[kv.Key] = kv.Value;
            return area;
        }

        /// <summary>The area an area tool was given (world ring), and its local corners when given as polygon or rect (for
        /// corner heights). Null with a reason when none or broken.</summary>
        private static float2[] Area(JObject args, AiSite site, out float2[] localCorners, out string error)
        {
            localCorners = null; error = null;
            float2[] local = null;
            if (args["polygon"] is JArray poly && poly.Count >= 3) local = poly.Select(t => new float2((float)t[0], (float)t[1])).ToArray();
            else if (args["rect"] is JArray r && r.Count >= 4) local = AiShapes.Rect(new float2((float)r[0], (float)r[1]), new float2((float)r[2], (float)r[3]));
            float wob = math.max(0f, (float?)args["wobble"] ?? 0f);
            if (local != null && args["polygon"] != null && wob > 0.05f) return AiShapes.Roughen(local.Select(site.World).ToArray(), wob, Seed((JArray)args["polygon"][0]));
            if (local != null) { localCorners = local; return local.Select(site.World).ToArray(); }
            if (args["strip"] is JArray st && st.Count >= 2)
                return AiTerrain.LineRing(st.Select(t => site.World(new float2((float)t[0], (float)t[1]))).ToArray(), math.max(0.5f, (float?)args["strip_width"] ?? 3f));
            float wobble = math.max(0f, (float?)args["wobble"] ?? 0f);
            if (args["circle"] is JArray c && c.Count >= 3)
                return AiShapes.Ellipse(site.World(new float2((float)c[0], (float)c[1])), (float)c[2], (float)c[2], 0f, wobble, Seed(c));
            if (args["ellipse"] is JArray e && e.Count >= 4)
                return AiShapes.Ellipse(site.World(new float2((float)e[0], (float)e[1])), (float)e[2], (float)e[3], e.Count >= 5 ? (float)e[4] : 0f, wobble, Seed(e));
            error = "give the area: polygon (at least 3 corners), rect, circle, ellipse or strip";
            return null;
        }

        private static uint Seed(JArray a) => (uint)(math.abs((float)a[0] * 73.1f + (float)a[1] * 19.7f) % 65521f) + 1u;

        private static JArray s_defs;

        public static JArray Definitions() => s_defs ?? (s_defs = new JArray
        {
            Fn("search_assets", "Find assets in the player's game by words: every placeable object it has loaded, vanilla and mods (props, trees, plants, fountains, statues, decals...; no buildings). Words match the id (split at capitals), the name, the category and tags. Returns id, name, category and real size (width x depth x height, metres). Use only ids it returns.",
                new JObject
                {
                    ["query"] = Str("words, e.g. 'oak tree', 'modern bench', 'street lamp', 'fountain'"),
                    ["category"] = new JObject { ["type"] = "string", ["description"] = "optional: tree, bush, hedge, flowerbed, planter, bench, lamp, bin, centerpiece, playground, sports, kiosk, shelter, picnic, decoration, fence, bollard, rock, decal, stage, lookout, surface (areas for place_surface), path or road (line assets for place_path), or object (everything not sorted into these)" },
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
            Fn("place_path", "Place a line along a polyline of points: a footpath by default, or any line asset from search_assets (category path: narrow, wide, gravel, dirt trails...; category road: roads, alleys, and the invisible car lanes parking lots are made of; category fence: fences, railings, hedges made as lines). A road-type line whose end lies on an existing road (within 4 m of its centre line) is joined to that road there, so cars can drive in. Paths should join a road, another path or a surface at their ends, or close into a loop. height raises it level above the ground (a boardwalk, a pier, a bridge).",
                new JObject
                {
                    ["points"] = new JObject { ["type"] = "array", ["description"] = "[[x, z], ...] local metres, at least 2", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["wide"] = new JObject { ["type"] = "boolean", ["description"] = "the game's wide path instead of the normal one (when no asset is given)" },
                    ["asset"] = Str("optional: a line asset id (path or fence) from search_assets"),
                    ["height"] = Num("optional: a fixed height, m above sea level, for a raised level line (boardwalk, pier)"),
                    ["smooth"] = new JObject { ["type"] = "boolean", ["description"] = "draw a smooth curve through the points instead of straight lines between them (default false)" },
                }, "points"),
            Fn("place_along", "Place objects in a row along a line or around an outline, every 'spacing' metres: an avenue of trees, lamps and benches along a path, a hedge of bushes, bollards, parking stall markings along a drive aisle, benches around a fountain, a ring of trees. offset puts them beside the line (both sides, or left or right of its direction). face turns them: along the line, toward it (benches facing a path, stalls facing an aisle), away from it, or a fixed yaw. Objects that would sit outside the selection, on a road, path or building or overlap something are skipped. The answer lists every object placed (id, asset, x, z, yaw) and the skips.",
                new JObject
                {
                    ["assets"] = new JObject { ["type"] = "array", ["description"] = "asset ids, used in turn (e.g. [lamp, bench, bench])", ["items"] = new JObject { ["type"] = "string" } },
                    ["points"] = new JObject { ["type"] = "array", ["description"] = "the line: [[x, z], ...] local metres (or give circle or rect for an outline)", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["circle"] = new JObject { ["type"] = "array", ["description"] = "an outline [x, z, radius] (benches or trees around a centre)", ["items"] = new JObject { ["type"] = "number" } },
                    ["rect"] = new JObject { ["type"] = "array", ["description"] = "an outline [x1, z1, x2, z2] (trees around a square)", ["items"] = new JObject { ["type"] = "number" } },
                    ["closed"] = new JObject { ["type"] = "boolean", ["description"] = "points form a loop (circle and rect always do)" },
                    ["spacing"] = Num("metres between neighbours"),
                    ["offset"] = Num("metres beside the line (default 0 = on it; e.g. 3.5 for lamps beside a 4 m path; for a closed outline, positive = outside, negative = inside)"),
                    ["side"] = new JObject { ["type"] = "string", ["enum"] = new JArray("both", "left", "right"), ["description"] = "open lines with offset: both sides (default) or one side, seen walking from the first point to the last" },
                    ["face"] = new JObject { ["type"] = "string", ["enum"] = new JArray("along", "toward", "away", "fixed"), ["description"] = "along the line (default), toward the line (on an outline: toward its inside), away from it, or fixed (use yaw)" },
                    ["yaw"] = Num("with face fixed: degrees; otherwise added to the facing"),
                    ["inset"] = Num("metres kept free at each end of an open line (default spacing / 2)"),
                    ["scale"] = Num("optional: size factor for all of them, 1 = as made (parking stalls: 0.93)"),
                }, "assets", "spacing"),
            Fn("scatter_objects", "Scatter many objects at once inside an area you give (a wood, a clump, a meadow of flowers, rocks): random positions at least 'spacing' apart, random yaw, the assets mixed evenly. With clumps > 0 they gather in patches (a noise field about 5 spacings across) with gaps between. Points that would sit outside the selection, on a road, path or building, or overlap something already placed are skipped. The answer lists every object placed (id, asset, x, z, yaw) and how many were skipped for what, so you can move or remove any of them.",
                WithArea(new JObject
                {
                    ["assets"] = new JObject { ["type"] = "array", ["description"] = "asset ids to mix (from search_assets)", ["items"] = new JObject { ["type"] = "string" } },
                    ["count"] = new JObject { ["type"] = "integer", ["description"] = "how many at most (up to 400)" },
                    ["spacing"] = Num("minimum distance between them, metres"),
                    ["clumps"] = Num("0 = spread evenly, up to 1 = strongly in patches"),
                    ["seed"] = new JObject { ["type"] = "integer", ["description"] = "change it for a different arrangement" },
                }, " (no area = the whole selection)"), "assets", "count", "spacing"),
            Fn("shape_ground", "Shape the terrain inside an area or along a line: level it to a height, raise only (fill up to it), lower only (dig down to it), or slope it through a height per corner or per line point. Heights are metres above sea level (the brief's heights; the map's contour lines). The edge blends smoothly back into the ground around it over feather metres (default 5; 0 = a hard edge, a wall). A line (points, width, optional heights per point) makes channels, ditches, berms, embankments, ramps and shelves. Each call shapes the ground the earlier calls left (a pond dug into a terrace). Everything placed sits on the new ground. The answer says the ground before and the steepest slope the shaping makes.",
                WithArea(new JObject
                {
                    ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("level", "raise", "lower"), ["description"] = "level (default): exactly to height; raise: only where the ground is lower; lower: only where it is higher" },
                    ["height"] = Num("target height, m above sea level"),
                    ["corner_heights"] = new JObject { ["type"] = "array", ["description"] = "optional, with polygon or rect: one height per corner for a slope (the ground becomes the surface through them)", ["items"] = new JObject { ["type"] = "number" } },
                    ["water_level"] = Num("optional: fill the shaped basin with water up to this height (m above sea level; set it below the lowest shore, and the basin floor below it). Not for ground lowered next to open water: that fills from it"),
                    ["feather"] = Num("metres over which the edge blends back into the ground around it (default 5; 10-20 for a natural shore, a hill or a valley; 0 = a hard vertical edge)"),
                    ["line"] = new JObject { ["type"] = "array", ["description"] = "instead of an area: the centre line [[x, z], ...] of a channel, ditch, berm, embankment, ramp or shelf (with width)", ["items"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "number" } } },
                    ["width"] = Num("with line: full width in metres at the target height (the feather is added on both sides)"),
                    ["line_heights"] = new JObject { ["type"] = "array", ["description"] = "with line, optional: one height per line point (a ramp, a channel falling to the water); else height", ["items"] = new JObject { ["type"] = "number" } },
                })),
            Fn("paint_ground", "Paint the ground's texture inside an area with one of the map's ground paints (listed in the brief: e.g. dry grass, lush grass, dirt, sand; 0 erases). Soft edge and edge noise make it look natural. Permanent in the city, like terrain shaping.",
                WithArea(new JObject
                {
                    ["layer"] = new JObject { ["type"] = "integer", ["description"] = "1-4: the map's ground paints from the brief; 0 = erase" },
                    ["strength"] = Num("0-1 (default 1)"),
                    ["feather"] = Num("soft edge, metres (default 3)"),
                    ["noise"] = Num("ragged edge, metres (default 0)"),
                }), "layer"),
            Fn("place_surface", "Lay a surface (an area of paving, gravel, grass, sand... from search_assets with category surface) inside an area, e.g. a plaza, a terrace, a sandpit. A polygon must be simple; keep it off roads.",
                WithArea(new JObject
                {
                    ["asset"] = Str("surface asset id"),
                }), "asset"),
            Fn("remove", "Remove placed objects or paths by id.", new JObject { ["ids"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "integer" } } }, "ids"),
            Fn("list_placed", "List everything placed so far (id, asset, position, yaw).", new JObject()),
            Fn("check", "Check everything placed: overlaps, outside the selection, on roads or buildings, dead-end paths.", new JObject()),
            Fn("view_map", "See the map again with everything placed so far drawn on it (attached after this round).", new JObject()),
            Fn("vision", "Your vision for this place, before you build: its name, the concept story, the signature moment, at least three ideas specific to this site, and the zones with their mood. The player sees it in the chat; your second look holds the build against it. You may call it again if the idea grows.",
                new JObject
                {
                    ["name"] = Str("the place's name, e.g. 'Willow Hollow'"),
                    ["story"] = Str("the concept in one or two sentences"),
                    ["signature"] = Str("the one thing people would remember or photograph"),
                    ["ideas"] = new JObject { ["type"] = "array", ["description"] = "three or more distinctive ideas specific to this site", ["items"] = new JObject { ["type"] = "string" } },
                    ["zones"] = new JObject { ["type"] = "array", ["description"] = "the zones and their mood, e.g. 'north: a shady birch grove with a hidden bench'", ["items"] = new JObject { ["type"] = "string" } },
                }, "name", "story", "signature", "ideas"),
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
                case "place_along": return ("Placemaker.Ai.Doing.Place", new object[] { (args["assets"] as JArray)?.Count ?? 0 });
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
                case AssetCategories.ParkingDecal: return ObjectRole.Decal;
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
                bool placing = name == "place_objects" || name == "place_along" || name == "scatter_objects" || name == "place_path" || name == "shape_ground" || name == "paint_ground" || name == "place_surface";
                if (placing && site.Vision == null && !site.VisionAsked)
                {
                    site.VisionAsked = true;
                    return new Result { Text = "Not yet: write your vision first with the vision tool (name, story, signature moment, ideas specific to this site, zones), then build it. Nothing was placed." };
                }
                switch (name)
                {
                    case "vision":
                    {
                        var ideas = (args["ideas"] as JArray)?.Select(t => (string)t).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
                        var zones = (args["zones"] as JArray)?.Select(t => (string)t).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
                        string vName = ((string)args["name"] ?? "").Trim(), story = ((string)args["story"] ?? "").Trim(), sig = ((string)args["signature"] ?? "").Trim();
                        site.Vision = string.Join("\n", new[] { vName, story, sig }.Concat(ideas).Concat(zones));
                        return new Result { Text = "vision noted: now build it, boldly, in many rounds", Vision = new[] { vName, story, sig }.Concat(ideas).ToArray() };
                    }
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
                    case "place_along": return PlaceAlong(args, site);
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
                        // the first finish of a request is one second look (the user, 27 Oct: fewer correction rounds, more
                        // building rounds): the numbers, the bare spots, the problems and the vision, then it builds on
                        if (!site.Reviewed)
                        {
                            site.Reviewed = true;
                            site.DetailShown = true;
                            var now = AiPlan.Current.Snapshot();
                            var issues = AiChecks.All(site, now);
                            string vision = site.Vision != null ? "Your vision: " + site.Vision.Replace("\n", " | ") + "\n" : "";
                            return new Result
                            {
                                Text = vision + AiChecks.Stats(site, now) + AiChecks.Details(site, now)
                                    + (issues.Count > 0 ? $"Problems to fix ({issues.Count}):\n" + string.Join("\n", issues.Take(40)) + "\n" : "")
                                    + "\nYour second look (the map is attached). Walk through the place as a visitor arriving from the street. Does it live up to your vision? What do you see first, what pulls you in, where do you sit, what would you photograph, what feels empty, flat or generic? Then make it unforgettable: strengthen the signature moment, give every zone its own character, shape the ground, layer the planting, add the life and small details, fix the problems. Take as many rounds as it needs, then finish with your note for the player.",
                                Map = true, LineKey = "Placemaker.Ai.Line.Review", Phase = "review",
                            };
                        }
                        // the detail pass: what it looks like up close (the user: "B- from high up, D- zoomed in")
                        if (!site.DetailShown)
                        {
                            site.DetailShown = true;
                            return new Result
                            {
                                Text = AiChecks.Details(site, AiPlan.Current.Snapshot()) + "\nNot yet: the detail pass. Look at your map as the player will see it zoomed in, a few metres away, and fix the gaps listed above one by one: a bin and a bush beside a lonely bench, a setting round a bare feature, planting and lamps along a bare path. Every addition must belong exactly where it goes, as the design guide's close-up detail says: never shrubs scattered over crop fields, lawns kept for play or open paving kept for crowds. If a gap is meant to be open, leave it. Then finish again.",
                                Map = true, LineKey = "Placemaker.Ai.Line.Review", Phase = "detail",
                            };
                        }
                        // problems left: sent back once with the list (eval: Sonnet finished with lamps on its lanes and paths under surfaces)
                        var left = AiChecks.All(site, AiPlan.Current.Snapshot());
                        if (left.Count > 0 && !site.ProblemsShown)
                        {
                            site.ProblemsShown = true;
                            return new Result { Text = $"Not yet: {left.Count} pieces still have problems. Fix each (move, reshape or remove it), then finish again:\n" + string.Join("\n", left), LineKey = "Placemaker.Ai.Line.CheckProblems", LineArgs = new object[] { left.Count }, Phase = "fix" };
                        }
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

        /// <summary>
        /// The asset essentials: per category, the assets the premade types use (their automatic choices, vanilla first), with
        /// size and crown, plus the road and path lines and the surfaces. Sent with the brief; the AI searches only beyond it.
        /// </summary>
        public static string Essentials(List<AiAsset> assets)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Asset essentials (a head start, not a limit)");
            sb.AppendLine("The assets this game's own premade types use, by category, best first: id | size (width x depth x height m) | crown for plants. Use search_assets, asset_groups and look_at_assets for anything else, mods included; look at what you will repeat or feature.");
            var cats = new[]
            {
                "tree", "bush", "hedge", "flowerbed", "planter", "bench", "lamp", "bin", "centerpiece", "centerpiece.fountain", "centerpiece.statue",
                "playground", "sports", "sports.goal", "sports.basketball", "sports.tennis", "sports.fitness", "sports.skate", "picnic", "camping", "camping.pitch", "camping.fire",
                "kiosk", "shelter", "restroom", "stage", "lookout", "seating.cafe", "decoration", "rock", "bollard", "fence", "garden.glasshouse", "garden.shed", "garden.crop",
                "decal", "parking.decal", "water.jets", "cemetery.grave", "dog.agility", "lamp.flood",
            };
            foreach (var c in cats)
            {
                var top = assets.Where(a => a.Category == c && !a.Line && a.Pick >= 0).OrderBy(a => a.Pick).Take(c == "tree" ? 12 : 6).ToList();
                if (top.Count == 0) continue;
                sb.AppendLine($"{c}: " + string.Join("; ", top.Select(a => $"{a.Id} {F(a.Size.x)}x{F(a.Size.z)}x{F(a.Size.y)}" + (a.Plant ? $" crown {F(a.Reach * 2f)}" : ""))));
            }
            var lines = assets.Where(a => a.Line).OrderBy(a => a.Vanilla ? 0 : 1).ThenBy(a => a.Size.x).ToList();
            bool Ground(AiAsset a) => a.Id.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) < 0 && a.Id.IndexOf("Pier", StringComparison.OrdinalIgnoreCase) < 0 && a.Id.IndexOf("Quay", StringComparison.OrdinalIgnoreCase) < 0;
            var paths = lines.Where(a => a.Category == "path" && a.Vanilla && Ground(a)).Take(12).ToList();
            var piers = lines.Where(a => a.Category == "path" && a.Vanilla && !Ground(a) && !IsQuay(a.Id)).Take(8).ToList();
            if (piers.Count > 0) sb.AppendLine("piers, boardwalks and bridges (place_path asset, with height; never quays): " + string.Join("; ", piers.Select(a => a.Id)));
            if (paths.Count > 0) sb.AppendLine("path lines (place_path asset; none = the normal footpath): " + string.Join("; ", paths.Select(a => a.Size.x > 0f ? $"{a.Id} {F(a.Size.x)} m" : a.Id)));
            var roads = lines.Where(a => a.Category == "road" && a.Vanilla && (a.Id.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0 || a.Id.IndexOf("Alley", StringComparison.OrdinalIgnoreCase) >= 0 || a.Size.x <= 16f)).Take(12).ToList();
            if (roads.Count > 0) sb.AppendLine("road lines (place_path asset; the invisible car lanes are drive aisles you cannot see: paint or pave the ground under them and mark the stalls with parking decals): " + string.Join("; ", roads.Select(a => a.Size.x > 0f ? $"{a.Id} {F(a.Size.x)} m" : a.Id)));
            var fences = lines.Where(a => a.Category == AssetCategories.Fence && a.Vanilla).Take(8).ToList();
            if (fences.Count > 0) sb.AppendLine("fence lines (place_path asset): " + string.Join("; ", fences.Select(a => a.Id)));
            // the premade car park's own kit (Core/Layers/Parking/ParkingAssets), the first of each that this game has
            var ids = new HashSet<string>(assets.Select(a => a.Id), StringComparer.Ordinal);
            string First(params string[] c) => c.FirstOrDefault(ids.Contains);
            var kit = new List<string>();
            void Kit(string what, string id) { if (id != null) kit.Add(what + " " + id); }
            Kit("asphalt surface", First(Placemaker.Core.Layers.Parking.ParkingAssets.Asphalt, Placemaker.Core.Layers.Parking.ParkingAssets.AsphaltEai, Placemaker.Core.Layers.Parking.ParkingAssets.AsphaltVanilla));
            Kit("grass surface", First(Placemaker.Core.Layers.Parking.ParkingAssets.Grass));
            Kit("car lane", First("Invisible Road Path - 2xTwoway"));
            Kit("narrow car lane", First("Invisible Car Path - 1xTwoway"));
            Kit("stall", First(Placemaker.Core.Layers.Parking.ParkingAssets.Decal));
            Kit("EV stall", First(Placemaker.Core.Layers.Parking.ParkingAssets.ElectricDecal));
            Kit("disabled stall", First(Placemaker.Core.Layers.Parking.ParkingAssets.DisabledDecal));
            Kit("charger", First(Placemaker.Core.Layers.Parking.ParkingAssets.Charger));
            Kit("lamp", First(Placemaker.Core.Layers.Parking.ParkingAssets.LampDouble));
            Kit("lamp at the edge", First(Placemaker.Core.Layers.Parking.ParkingAssets.LampSingle));
            Kit("tree", First(Placemaker.Core.Layers.Parking.ParkingAssets.DefaultTree));
            Kit("bushes", string.Join(", ", Placemaker.Core.Layers.Parking.ParkingAssets.DefaultBushes.Where(ids.Contains)));
            if (kit.Count > 0) sb.AppendLine("car park kit (what the premade car park uses; see the design guide): " + string.Join("; ", kit));
            var surfaces = assets.Where(a => a.Category != null && a.Category.StartsWith("surface") && a.Pick >= 0).OrderBy(a => a.Pick).Take(16).ToList();
            // the decals that are built (markings, court and field lines, parking stalls): no pack decals
            var decals = assets.Where(a => a.Category != null && a.Category.StartsWith("decal", StringComparison.Ordinal) && !NotBuilt(a)).OrderBy(a => a.Pick < 0 ? 99 : a.Pick).ThenBy(a => a.Id).Take(24).ToList();
            var stalls = assets.Where(a => a.Category == AssetCategories.ParkingDecal).Take(6).ToList();
            if (decals.Count + stalls.Count > 0) sb.AppendLine("decals that are built (place_objects or place_along; flat on the ground, rotate with yaw, scale to fit): " + string.Join("; ", decals.Concat(stalls).Select(a => $"{a.Id} ({a.Category}) {F(a.Size.x)}x{F(a.Size.z)}")));
            if (surfaces.Count > 0) sb.AppendLine("surfaces (place_surface): " + string.Join("; ", surfaces.Select(a => $"{a.Id} ({a.Category.Replace("surface.", "")})")));
            return sb.ToString();
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
                if (IsQuay(a.Id)) continue;
                if (!string.IsNullOrEmpty(category) && a.Category != category && !a.Category.StartsWith(category + ".")) continue;
                if (!string.IsNullOrEmpty(group) && !string.Equals(a.Group, group, StringComparison.OrdinalIgnoreCase)) continue;
                string hay = (a.Group + " " + a.Id + " " + System.Text.RegularExpressions.Regex.Replace(a.Id, "([a-z])([A-Z0-9])", "$1 $2") + " " + a.Name + " " + a.Category + " " + string.Join(" ", a.Tags)).ToLowerInvariant();
                var tokens = hay.Split(new[] { ' ', ',', '-', '_', '.', '(', ')', '/' }, StringSplitOptions.RemoveEmptyEntries);
                bool Has(string w) { string stem = w.Length > 3 && w.EndsWith("s") ? w.Substring(0, w.Length - 1) : w; return tokens.Any(t => t.StartsWith(stem, StringComparison.Ordinal)); }
                int score = words.Count(Has) * 10;
                if (words.Length > 0 && score == 0 && string.IsNullOrEmpty(category) && string.IsNullOrEmpty(group)) continue;
                if (a.Vanilla) score += 1;
                if (a.Pick >= 0) score += 5 - math.min(4, a.Pick / 3);   // the premade types' own choices first
                scored.Add((a, score));
            }
            var top = scored.OrderByDescending(t => t.score).ThenBy(t => t.a.Id, StringComparer.Ordinal).Take(limit).ToList();
            if (top.Count == 0) return new Result { Text = "no matches; try other words or a category" };
            var sb = new StringBuilder();
            sb.AppendLine($"{top.Count} of {scored.Count} matches, the premade types' own choices first (id | name | category | width x depth x height m; plants: how wide the crown grows):");
            foreach (var (a, _) in top) sb.AppendLine($"{a.Id} | {a.Name} | {a.Category}{(a.Line ? $" line, {F(a.Size.x)} m wide" : "")}{(a.Vanilla ? "" : " (mod)")}{(a.HasPicture ? "" : " (no picture)")} | {F(a.Size.x)} x {F(a.Size.z)} x {F(a.Size.y)}" + (a.Plant ? $" | crown about {F(a.Reach * 2f)} m across" : ""));
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
                if (NotBuilt(a)) { refused++; sb.AppendLine($"refused '{asset}': {NotBuiltWhy}"); continue; }
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
            var unknown = ids.Where(id => !site.Assets.ContainsKey(id) || NotBuilt(site.Assets[id])).ToList();
            ids = ids.Where(id => site.Assets.ContainsKey(id) && !NotBuilt(site.Assets[id])).ToList();
            if (ids.Count == 0) return new Result { Text = "no known asset ids" + (unknown.Count > 0 ? " (unknown: " + string.Join(", ", unknown) + ")" : "") };
            float2[] area;
            if (args["polygon"] != null || args["rect"] != null || args["circle"] != null || args["ellipse"] != null || args["strip"] != null)
            {
                area = Area(args, site, out _, out var err);
                if (area == null) return new Result { Text = err };
            }
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


        /// <summary>A decal plans drop (Core/Decals/PackDecals: only the base game's markings, stalls and court lines are built).</summary>
        private static bool NotBuilt(AiAsset a) => a.Category != null && a.Category.StartsWith("decal", StringComparison.Ordinal) && !Placemaker.Core.Decals.PackDecals.Kept(a.Category);
        private const string NotBuiltWhy = "only the base game's markings, parking stalls and court lines are built; other decals are not";

        /// <summary>The user (27 Oct): never use quays. Quay lines are left out of the asset list and refused.</summary>
        public static bool IsQuay(string id) => (id ?? "").IndexOf("Quay", StringComparison.OrdinalIgnoreCase) >= 0;

        private static Result PlaceAlong(JObject args, AiSite site)
        {
            var ids = (args["assets"] as JArray)?.Select(t => (string)t).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>();
            var unknown = ids.Where(id => !site.Assets.ContainsKey(id) || site.Assets[id].Line || NotBuilt(site.Assets[id])).ToList();
            ids = ids.Where(id => site.Assets.ContainsKey(id) && !site.Assets[id].Line && !NotBuilt(site.Assets[id])).ToList();
            if (ids.Count == 0) return new Result { Text = "no known object asset ids" + (unknown.Count > 0 ? " (unknown or line assets: " + string.Join(", ", unknown) + ")" : "") };
            float2[] line; bool closed;
            if (args["circle"] is JArray c && c.Count >= 3) { line = AiShapes.Circle(site.World(new float2((float)c[0], (float)c[1])), (float)c[2], 48); closed = true; }
            else if (args["rect"] is JArray r && r.Count >= 4) { line = AiShapes.Rect(new float2((float)r[0], (float)r[1]), new float2((float)r[2], (float)r[3])).Select(site.World).ToArray(); closed = true; }
            else
            {
                line = (args["points"] as JArray)?.Select(t => site.World(new float2((float)t[0], (float)t[1]))).ToArray();
                if (line == null || line.Length < 2) return new Result { Text = "give points (at least 2), circle or rect" };
                closed = (bool?)args["closed"] == true;
            }
            // outlines run counter-clockwise: their left is the inside
            if (closed && Placemaker.Core.Geometry.Poly.SignedArea(line) < 0) Array.Reverse(line);
            float spacing = math.max(0.5f, (float?)args["spacing"] ?? 8f);
            float offset = (float?)args["offset"] ?? 0f;
            string side = ((string)args["side"] ?? "both").ToLowerInvariant();
            string face = ((string)args["face"] ?? "along").ToLowerInvariant();
            float yawAdd = math.radians((float?)args["yaw"] ?? 0f);
            float inset = math.max(0f, (float?)args["inset"] ?? spacing * 0.5f);
            float scale = math.clamp((float?)args["scale"] ?? 1f, 0.2f, 5f);
            var stations = AiShapes.Along(line, closed, spacing, inset);
            // the signed distances to the left of the direction: closed outlines take offset as outside (= right)
            var lefts = math.abs(offset) < 0.01f ? new[] { 0f } : closed ? new[] { -offset }
                : side == "left" ? new[] { math.abs(offset) } : side == "right" ? new[] { -math.abs(offset) } : new[] { math.abs(offset), -math.abs(offset) };
            var existing = AiPlan.Current.Snapshot();
            var placed = new List<AiPiece>();
            var skipped = new Dictionary<string, int>();
            int k = 0;
            foreach (float l in lefts)
                foreach (var (at, dir) in stations)
                {
                    var left = new float2(-dir.y, dir.x);
                    var pos = at + left * l;
                    // toward the line; on the line itself (or an outline), toward the inside
                    var toward = l > 0f ? -left : l < 0f ? left : left;
                    float yaw;
                    switch (face)
                    {
                        case "toward": yaw = AiShapes.YawOf(toward) + yawAdd; break;
                        case "away": yaw = AiShapes.YawOf(-toward) + yawAdd; break;
                        case "fixed": yaw = yawAdd; break;
                        default: yaw = AiShapes.YawOf(dir) + yawAdd; break;
                    }
                    var id = ids[k++ % ids.Count];
                    var a = site.Assets[id];
                    var piece = new AiPiece { Kind = AiPieceKind.Object, Asset = id, Category = a.Category, Role = RoleFor(a.Category), Position = pos, Yaw = yaw, Scale = scale };
                    var pr = AiChecks.Problems(site, piece, existing.Concat(placed).ToList());
                    if (pr.Count > 0)
                    {
                        string why = pr[0].Split('(')[0].Split('#')[0].Trim();
                        skipped[why] = (skipped.TryGetValue(why, out var n) ? n : 0) + 1;
                        continue;
                    }
                    placed.Add(piece);
                }
            foreach (var p in placed) AiPlan.Current.Add(p);
            var sb = new StringBuilder();
            sb.AppendLine($"placed {placed.Count} of {stations.Count * lefts.Length}:");
            foreach (var p in placed)
            {
                var l = site.Local(p.Position);
                sb.AppendLine($"#{p.Id} {p.Asset} ({F(l.x)},{F(l.y)}) yaw {F((math.degrees(p.Yaw) % 360f + 360f) % 360f)}");
            }
            if (skipped.Count > 0) sb.AppendLine("skipped: " + string.Join(", ", skipped.Select(kv => kv.Value + " " + kv.Key)));
            if (unknown.Count > 0) sb.AppendLine("ignored (unknown ids, line assets, or decals that are not built: " + NotBuiltWhy + "): " + string.Join(", ", unknown));
            return new Result { Text = sb.ToString(), LineKey = "Placemaker.Ai.Line.Placed", LineArgs = new object[] { placed.Count, 0 } };
        }

        private static string Problems(AiSite site, AiPiece p)
        {
            var pr = AiChecks.Problems(site, p, AiPlan.Current.Snapshot());
            return pr.Count == 0 ? "no problems" : string.Join("; ", pr);
        }

        private static Result ShapeGround(JObject args, AiSite site)
        {
            float feather = math.clamp((float?)args["feather"] ?? 5f, 0f, 60f);
            string mode0 = ((string)args["mode"] ?? "level").ToLowerInvariant();
            var kind0 = mode0 == "raise" ? TerraformKind.Raise : mode0 == "lower" ? TerraformKind.Lower : TerraformKind.Level;
            if (args["line"] is JArray lineArg)
            {
                var pts = lineArg.Select(t => site.World(new float2((float)t[0], (float)t[1]))).ToArray();
                if (pts.Length < 2) return new Result { Text = "a line needs at least 2 points" };
                float width = math.max(1f, (float?)args["width"] ?? 6f);
                var lh = (args["line_heights"] as JArray)?.Select(t => (float)t).ToArray();
                if (lh != null && lh.Length != pts.Length) return new Result { Text = $"line_heights has {lh.Length} heights for {pts.Length} points" };
                float? lheight = (float?)args["height"];
                if (lh == null && lheight == null) return new Result { Text = "give height, or line_heights" };
                var lp = new AiPiece
                {
                    Kind = AiPieceKind.Ground, LinePoints = pts, LineWidth = width, LineHeights = lh, Height = lheight ?? lh.Average(),
                    Ring = AiTerrain.LineRing(pts, width), Shape = kind0, Feather = feather, WaterLevel = (float?)args["water_level"] ?? float.NaN,
                };
                return Shaped(lp, site, mode0, $"along a line {F(width)} m wide");
            }
            var ring = Area(args, site, out var localCorners, out var err);
            if (ring == null) return new Result { Text = err };
            string mode = ((string)args["mode"] ?? "level").ToLowerInvariant();
            var corners = (args["corner_heights"] as JArray)?.Select(t => (float)t).ToArray();
            if (corners != null && localCorners == null) return new Result { Text = "corner_heights needs the area as polygon or rect" };
            if (corners != null && corners.Length != ring.Length) return new Result { Text = $"corner_heights has {corners.Length} heights for {ring.Length} corners" };
            if (Placemaker.Core.Geometry.Poly.SignedArea(ring) < 0) { Array.Reverse(ring); if (corners != null) Array.Reverse(corners); }
            float? height = (float?)args["height"];
            if (corners == null && height == null) return new Result { Text = "give height, or corner_heights for a slope" };
            var p = new AiPiece
            {
                Kind = AiPieceKind.Ground, Ring = ring, CornerHeights = corners, Height = height ?? corners.Average(),
                Shape = mode == "raise" ? TerraformKind.Raise : mode == "lower" ? TerraformKind.Lower : TerraformKind.Level,
                WaterLevel = (float?)args["water_level"] ?? float.NaN, Feather = feather,
            };
            if (feather > 0.05f) return Shaped(p, site, mode, corners != null ? "sloped" : $"to {F(p.Height)} m");
            // a hard edge: the ground before, inside and along the edge (the steps it makes)
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
            if (gmin <= gmax && math.abs(p.Height - (gmin + gmax) * 0.5f) > 30f)
                return new Result { Text = $"refused: height {F(p.Height)} m is far from the ground here ({F(gmin)} to {F(gmax)} m above sea level). Heights are metres above sea level, not relative. Nothing was shaped." };
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

        /// <summary>A soft-edged or line shape: added, and answered with the ground before and the steepest slope it makes.</summary>
        private static Result Shaped(AiPiece p, AiSite site, string mode, string what)
        {
            AiTerrain.Box(p, out var lo, out var hi);
            float gmin = float.MaxValue, gmax = float.MinValue, low = float.MaxValue;
            for (float x = lo.x; x <= hi.x; x += 2f)
                for (float z = lo.y; z <= hi.y; z += 2f)
                {
                    var q = new float2(x, z);
                    AiTerrain.Core(p, q, out _, out float e);
                    if (e > 0f) continue;
                    float g = site.Ground(q);
                    gmin = math.min(gmin, g); gmax = math.max(gmax, g);
                    low = math.min(low, AiTerrain.HeightAt(p, q, g));
                }
            // heights are above sea level: a target far from the ground here is a mistake (eval: "height 6" meant 6 m up and
            // dug a 490 m chasm), refused with the right number
            float target = p.LineHeights != null ? p.LineHeights.Average() : p.CornerHeights != null ? p.CornerHeights.Average() : p.Height;
            if (gmin <= gmax && math.abs(target - (gmin + gmax) * 0.5f) > 30f)
                return new Result { Text = $"refused: height {F(target)} m is {F(math.abs(target - (gmin + gmax) * 0.5f))} m {(target < gmin ? "below" : "above")} the ground here ({F(gmin)} to {F(gmax)} m above sea level). Heights are metres above sea level, not relative: for a 6 m hill here give about {F(gmax + 6f)}. Nothing was shaped." };
            // no cliffs by accident: the soft edge is at least wide enough for a 60 % bank
            float rise = gmin <= gmax ? math.max(math.abs(target - gmin), math.abs(target - gmax)) : 0f;
            string widened = "";
            if (p.Feather > 0.05f && rise / p.Feather > 0.6f) { float fw = math.ceil(rise / 0.6f); widened = $"; feather widened from {F(p.Feather)} to {F(fw)} m so its bank is at most 60 %"; p.Feather = fw; }
            AiPlan.Current.Add(p);
            float steep = AiTerrain.SteepestEdge(p, site.Ground);
            string water = "";
            if (math.isfinite(p.WaterLevel))
                water = low < p.WaterLevel - 0.3f ? $"; water at {F(p.WaterLevel)} m over a floor down to {F(low)} m"
                    : $"; WARNING: the floor ({F(low)} m) is not below the water level ({F(p.WaterLevel)} m): no water will appear";
            else if (math.isfinite(site.WaterLevel) && low < site.WaterLevel - 0.2f && p.Ring.Any(q => site.ByWater(q) || site.Water(q) > 0.05f))
            {
                // how far past the shape's edge its soft edge stays under the water: the new waterline
                float reach = 0f;
                var all = AiPlan.Current.Snapshot();
                var c = AiTerrain.Centre(p);
                for (int k = 0; k < 16; k++)
                {
                    var d = new float2(math.cos(k * 0.3927f), math.sin(k * 0.3927f));
                    float edge = 0f;
                    for (float r = 0f; r < 200f; r += 1f) { AiTerrain.Core(p, c + d * r, out _, out float e); if (e > 0f) { edge = r; break; } }
                    for (float r = edge; r < edge + p.Feather + 2f; r += 1f)
                        if (!AiChecks.UnderWater(site, all, c + d * r, out _)) { reach = math.max(reach, r - edge); break; }
                }
                water = $"; it reaches below the open water ({F(site.WaterLevel)} m), so the water flows in and covers it and about {F(reach)} m of its soft edge: the new waterline (blue on the map). Keep sand, umbrellas and paths on the dry ground beyond it";
            }
            string ground = gmin <= gmax ? $"ground before: {F(gmin)} to {F(gmax)} m" : "ground before: unknown";
            return new Result
            {
                Text = $"ground #{p.Id} ({mode} {what}, feather {F(p.Feather)} m): {ground}; steepest slope it makes {F(math.round(steep))} %{(steep > 45f ? " (a steep bank: a wider feather softens it)" : "")}{widened}{water}; {Problems(site, p)}",
                LineKey = "Placemaker.Ai.Line.Shape", LineArgs = new object[] { 0f },
            };
        }

        private static Result PaintGround(JObject args, AiSite site)
        {
            var ring = Area(args, site, out _, out var err);
            if (ring == null) return new Result { Text = err };
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
            var ring = Area(args, site, out _, out var err);
            if (ring == null) return new Result { Text = err };
            if (Placemaker.Core.Geometry.Poly.SignedArea(ring) < 0) Array.Reverse(ring);
            string asset = (string)args["asset"] ?? "";
            if (!site.Assets.TryGetValue(asset, out var a) || a.Category == null || !a.Category.StartsWith("surface"))
                return new Result { Text = $"refused '{asset}': not a surface asset (search_assets with category surface)" };
            var p = new AiPiece { Kind = AiPieceKind.Surface, Ring = ring, Asset = asset, Category = a.Category, SurfaceRole = SurfaceRoleFor(a.Category) };
            // the game draws a surface over the paths under it: refused, so the paths stay (eval: whole-site grass hid a park's paths, 27 Oct)
            var hidden = AiPlan.Current.Snapshot().Where(o => AiChecks.HiddenUnder(o, ring) > 4f).Select(o => "#" + o.Id).ToList();
            if (hidden.Count > 0)
                return new Result { Text = $"refused: it would cover path {string.Join(", ", hidden)}, and the game draws a surface over the paths under it. Keep surfaces to plazas, pads and yards beside or at the ends of paths (cut the polygon around them), and use paint_ground for a grass or dirt base." };
            var under = AiPlan.Current.Snapshot().Where(o => o.Kind == AiPieceKind.Surface && o.Ring != null && AiChecks.SurfacesOverlap(ring, o.Ring)).Select(o => "#" + o.Id).ToList();
            if (under.Count > 0)
                return new Result { Text = $"refused: it overlaps surface {string.Join(", ", under)}, and where surfaces overlap the game shows only one of them. Surfaces sit side by side, never on top of each other: cut them into pieces that meet at their edges (e.g. asphalt strips between grass medians), or use paint_ground for a base under everything." };
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
            if (IsQuay(asset)) return new Result { Text = $"refused '{asset}': quays are not used (the player's rule); build the water's edge with a timber boardwalk or pier line (place_path with height), or a sloped bank" };
            bool fence = la != null && la.Category == AssetCategories.Fence;
            // the invisible car lanes are filed with the paths (their names say Path): cars drive on them, so they are road lanes, joined to the streets
            bool road = la != null && (la.Category == "road" || (la.Id.IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0 && (la.Id.IndexOf("Road", StringComparison.OrdinalIgnoreCase) >= 0 || la.Id.IndexOf(" Car ", StringComparison.OrdinalIgnoreCase) >= 0)));
            var p = new AiPiece
            {
                Kind = AiPieceKind.Path, Points = pts, Asset = asset, Category = fence ? AssetCategories.Fence : road ? AssetCategories.Road : AssetCategories.Pathway,
                NetRole = fence ? NetRole.Fence : road ? NetRole.DriveAisle : (bool?)args["wide"] == true ? NetRole.PathwayWide : NetRole.Pathway,
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
