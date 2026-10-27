// AI builder (docs/AI-BUILDER.md), phase one: the agent loop behind the "AI" category. The player writes what they want;
// the AI gets the site brief and its map, then calls tools (search assets, place objects and paths, remove, check, view
// the map, finish, wish) until it finishes or answers the player. What it places goes into AiPlan.Current, which the
// "ai" layer turns into the normal plan: ghost preview, site plan sketch, Review and Build like any type.
// Main thread: Send (captures the selection and the asset list). Everything else on a worker. Every step is saved to
// ModsData/Placemaker/ai/<time>/ (transcript.jsonl without images, map-N.png, summary.json); the key never is.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Ai;
using Placemaker.Game.Settings;
using Placemaker.Game.Tool;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Placemaker.Game.Ai
{
    public static class AiAgent
    {
        public enum State { Idle, Working, Waiting, Done, Failed, Stopped }

        public sealed class Line
        {
            public string Who;   // you | ai | step | error
            public string Text;  // you, ai, error: as written
            public string Key;   // step: a localisation key with Args
            public object[] Args;
        }

        private static readonly object s_lock = new object();
        private static readonly List<Line> s_lines = new List<Line>();
        private static volatile State s_state = State.Idle;
        private static volatile int s_version;
        private static string s_status = "";
        private static object[] s_statusArgs = new object[0];
        private static double s_cost;
        private static int s_tokens;

        // the session: one selection, one conversation
        private static JArray s_messages;
        private static AiSite s_site;
        private static SiteBriefInput s_brief;
        private static List<AiAsset> s_assets;
        private static string s_folder;
        private static string s_shapeKey;
        private static int s_maps;
        private static CancellationTokenSource s_cancel;
        private static State s_end;
        private static readonly List<string> s_wishes = new List<string>();

        public const int MaxTurns = 40;

        public static State Status => s_state;
        public static int Version => s_version;
        public static bool Busy => s_state == State.Working;
        public static string Folder => s_folder;
        public static double Cost => s_cost;
        public static IReadOnlyList<string> Wishes { get { lock (s_lock) return s_wishes.ToList(); } }

        public static JObject View()
        {
            lock (s_lock)
            {
                return new JObject
                {
                    ["status"] = s_state.ToString().ToLowerInvariant(),
                    ["busy"] = s_state == State.Working,
                    ["step"] = s_status,
                    ["stepArgs"] = new JArray(s_statusArgs.Select(a => JToken.FromObject(a ?? ""))),
                    ["cost"] = Math.Round(s_cost, 4),
                    ["tokens"] = s_tokens,
                    ["pieces"] = AiPlan.Current.Count,
                    ["lines"] = new JArray(s_lines.Select(l => l.Key != null
                        ? new JObject { ["who"] = l.Who, ["key"] = l.Key, ["args"] = new JArray(l.Args.Select(a => JToken.FromObject(a ?? ""))) }
                        : new JObject { ["who"] = l.Who, ["text"] = l.Text })),
                };
            }
        }

        private static void Say(string who, string text)
        {
            lock (s_lock) s_lines.Add(new Line { Who = who, Text = text });
            s_version++;
            Mod.Log.Info($"Placemaker AI [{who}]: {(text.Length > 400 ? text.Substring(0, 400) + "..." : text)}");
        }

        private static void SayKey(string key, params object[] args)
        {
            lock (s_lock) s_lines.Add(new Line { Who = "step", Key = key, Args = args ?? new object[0] });
            s_version++;
            Mod.Log.Info($"Placemaker AI [step]: {key} {string.Join(", ", args ?? new object[0])}");
        }

        /// <summary>The progress line while it works (localisation key; "" = none).</summary>
        private static void Step(string key, params object[] args)
        {
            lock (s_lock) { s_status = key; s_statusArgs = args ?? new object[0]; }
            s_version++;
        }

        /// <summary>Forget the conversation and what was placed.</summary>
        public static void Reset()
        {
            s_cancel?.Cancel();
            lock (s_lock) { s_lines.Clear(); s_wishes.Clear(); s_status = ""; }
            s_messages = null;
            s_shapeKey = null;
            s_cost = 0;
            s_tokens = 0;
            AiPlan.Current.Clear();
            s_state = State.Idle;
            s_version++;
        }

        public static void Stop()
        {
            s_cancel?.Cancel();
        }

        private static string ShapeKey(float2[] ring) => string.Join(";", ring.Select(p => ((int)math.round(p.x)) + "," + ((int)math.round(p.y))));

        /// <summary>Main thread. The player's message: starts the session for the current selection, or continues it.</summary>
        public static bool Send(string text, out string why)
        {
            why = null;
            text = (text ?? "").Trim();
            if (text.Length == 0) { why = "nothing written"; return false; }
            if (s_state == State.Working) { why = "the AI is still working"; return false; }
            var world = World.DefaultGameObjectInjectionWorld;
            var tool = world?.GetExistingSystemManaged<PlacemakerToolSystem>();
            var shape = tool?.Shape;
            if (shape?.Ring == null || shape.Ring.Length < 3) { why = "no area selected"; return false; }
            var settings = ToolRegistration.Settings;
            string key = AiBuilder.Key(settings, out var keyFrom);
            if (string.IsNullOrEmpty(key)) { why = "no API key"; SayKey("Placemaker.Ai.Line.NoKey"); return false; }
            string endpoint = string.IsNullOrWhiteSpace(settings?.AiEndpoint) ? AiBuilder.DefaultEndpoint : settings.AiEndpoint.Trim();
            string model = string.IsNullOrWhiteSpace(settings?.AiModel) ? AiBuilder.DefaultModel : settings.AiModel.Trim();

            var ring = shape.Ring2D();
            bool fresh = s_messages == null || s_shapeKey != ShapeKey(ring);
            JObject first = null;
            if (fresh)
            {
                // a new selection: a new session (the plan of the old one is dropped)
                AiPlan.Current.Clear();
                lock (s_lock) { s_lines.Clear(); s_wishes.Clear(); }
                s_cost = 0; s_tokens = 0; s_maps = 0;
                try
                {
                    s_brief = AiBuilder.Capture(world, tool, ring, text);
                    var (lo, _) = SiteBrief.Box(ring);
                    s_site = new AiSite { Ring = ring, Origin = lo, Nets = s_brief.Nets, Buildings = s_brief.Buildings, Ground = GroundOf(s_brief.Ground) };
                    s_assets = AssetIndex();
                    foreach (var a in s_assets) if (!s_site.Assets.ContainsKey(a.Id)) s_site.Assets[a.Id] = a;
                    s_folder = Path.Combine(ModPaths.DataFolder, "ai", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    Directory.CreateDirectory(s_folder);
                    s_shapeKey = ShapeKey(ring);
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e, "Placemaker AI: capturing the selection failed");
                    why = "capturing the selection failed: " + e.Message;
                    return false;
                }
                // the instructions and the design guide are the same every call: marked for the provider's prompt cache
                s_messages = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "text", ["text"] = AiPrompts.Agent + "\nThe player's game language is " + Locale() + ": write every message to the player in it." },
                            new JObject { ["type"] = "text", ["text"] = AiHandbook.Text, ["cache_control"] = new JObject { ["type"] = "ephemeral" } },
                        },
                    },
                };
                Mod.Log.Info($"Placemaker AI: new session for {model} at {endpoint} (key from {keyFrom}, prompt {AiPrompts.AgentVersion}, {AiHandbook.Version}), {s_assets.Count} assets, {s_brief.Nets.Count} nets -> {s_folder}");
            }
            Say("you", text);
            s_state = State.Working;
            s_end = State.Waiting;
            s_cancel = new CancellationTokenSource();
            var cancel = s_cancel.Token;
            Task.Run(async () =>
            {
                try
                {
                    if (fresh)
                    {
                        Step("Placemaker.Ai.Doing.Read");
                        var map = Map();
                        var brief = SiteBrief.Write(s_brief);
                        File.WriteAllText(Path.Combine(s_folder, "brief.txt"), brief);
                        var firstMessage = UserWithImage(brief, map);
                        firstMessage["content"][1]["cache_control"] = new JObject { ["type"] = "ephemeral" };   // the brief and its map: the same in every later call
                        s_messages.Add(firstMessage);
                    }
                    else s_messages.Add(new JObject { ["role"] = "user", ["content"] = "The player says: " + text });
                    await Loop(endpoint, key, model, cancel);
                }
                catch (OperationCanceledException) { s_end = State.Stopped; SayKey("Placemaker.Ai.Line.Stopped"); }
                catch (Exception e)
                {
                    s_end = State.Failed;
                    Say("error", e.GetType().Name + ": " + e.Message);
                    Mod.Log.Error(e, "Placemaker AI: the loop failed");
                }
                finally
                {
                    Step("");
                    Summary(model);
                    s_state = s_end;
                    s_version++;
                }
            });
            return true;
        }

        private static JObject UserWithImage(string text, byte[] png) => new JObject
        {
            ["role"] = "user",
            ["content"] = new JArray
            {
                new JObject { ["type"] = "text", ["text"] = text },
                new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(png) } },
            },
        };

        private static async Task Loop(string endpoint, string key, string model, CancellationToken cancel)
        {
            for (int turn = 0; turn < MaxTurns; turn++)
            {
                cancel.ThrowIfCancellationRequested();
                Step("Placemaker.Ai.Doing.Think");
                var body = new JObject
                {
                    ["model"] = model,
                    ["max_tokens"] = 8000,
                    ["messages"] = s_messages,
                    ["tools"] = AiTools.Definitions(),
                    ["usage"] = new JObject { ["include"] = true },
                };
                var r = await AiClient.Send(endpoint, key, body, cancel);
                s_cost += r.Cost ?? 0;
                s_tokens += r.PromptTokens + r.ReplyTokens;
                Log(new JObject { ["turn"] = turn, ["ms"] = r.Ms, ["status"] = r.Status, ["promptTokens"] = r.PromptTokens, ["replyTokens"] = r.ReplyTokens, ["cost"] = r.Cost, ["error"] = r.Error });
                if (r.Error != null) { s_end = State.Failed; Say("error", r.Error); return; }
                var msg = JObject.Parse(r.RawResponse)["choices"]?[0]?["message"] as JObject;
                if (msg == null) { s_end = State.Failed; Say("error", "no message in the reply"); return; }
                var assistant = new JObject { ["role"] = "assistant", ["content"] = msg["content"] ?? "" };
                var calls = msg["tool_calls"] as JArray;
                if (calls != null && calls.Count > 0) assistant["tool_calls"] = calls;
                s_messages.Add(assistant);
                Log(new JObject { ["assistant"] = assistant });
                string said = (string)msg["content"];
                if (!string.IsNullOrWhiteSpace(said)) Say("ai", said.Trim());
                if (calls == null || calls.Count == 0) { s_end = State.Waiting; return; }

                bool finished = false;
                byte[] mapAfter = null;
                var pictures = new List<(string label, AiPictures.Picture pic)>();
                int revBefore = AiPlan.Current.Revision;
                foreach (var c in calls)
                {
                    cancel.ThrowIfCancellationRequested();
                    string name = (string)c["function"]?["name"] ?? "";
                    JObject args;
                    try { args = JObject.Parse((string)c["function"]?["arguments"] ?? "{}"); }
                    catch (Exception) { args = new JObject(); }
                    var doing = AiTools.Doing(name, args);
                    Step(doing.key, doing.args);
                    var res = AiTools.Run(name, args, s_site, s_assets, s_wishes);
                    if (res.Map) mapAfter = Map();
                    if (res.Pictures != null) pictures.AddRange(res.Pictures);
                    if (res.Finished) finished = true;
                    if (res.LineKey != null) SayKey(res.LineKey, res.LineArgs);
                    if (!string.IsNullOrEmpty(res.Note)) Say("ai", res.Note);
                    s_messages.Add(new JObject { ["role"] = "tool", ["tool_call_id"] = (string)c["id"], ["content"] = res.Text });
                    Log(new JObject { ["tool"] = name, ["args"] = args, ["result"] = res.Text });
                }
                if (pictures.Count > 0)
                {
                    var content = new JArray { new JObject { ["type"] = "text", ["text"] = "The asset pictures you asked for:" } };
                    foreach (var (label, pic) in pictures)
                    {
                        content.Add(new JObject { ["type"] = "text", ["text"] = label });
                        content.Add(new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = "data:" + pic.Mime + ";base64," + Convert.ToBase64String(pic.Bytes) } });
                    }
                    s_messages.Add(new JObject { ["role"] = "user", ["content"] = content });
                    Log(new JObject { ["pictures"] = new JArray(pictures.Select(p => p.label + " [" + p.pic.Source + ", " + p.pic.Bytes.Length + " bytes]")) });
                }
                if (mapAfter == null && !finished && AiPlan.Current.Revision != revBefore) mapAfter = Map();
                if (mapAfter != null) s_messages.Add(UserWithImage($"The map now (map-{s_maps}.png): what you placed is drawn on it, ids next to everything but plants.", mapAfter));
                if (finished) { s_end = State.Done; return; }
            }
            s_end = State.Waiting;
            SayKey("Placemaker.Ai.Line.MaxSteps", MaxTurns);
        }

        /// <summary>Bilinear height from the brief's ground grid (world xz).</summary>
        private static Func<float2, float> GroundOf(BriefGround g) => p =>
        {
            float fx = (p.x - g.Origin.x) / g.Cell - 0.5f, fz = (p.y - g.Origin.y) / g.Cell - 0.5f;
            int i = (int)math.floor(fx), j = (int)math.floor(fz);
            float tx = fx - i, tz = fz - j;
            return math.lerp(math.lerp(g.HeightAt(i, j), g.HeightAt(i + 1, j), tx), math.lerp(g.HeightAt(i, j + 1), g.HeightAt(i + 1, j + 1), tx), tz);
        };

        private static string Locale()
        {
            try { return global::Game.SceneFlow.GameManager.instance?.localizationManager?.activeLocaleId ?? "en-US"; }
            catch (Exception) { return "en-US"; }
        }

        /// <summary>The map with what is placed, saved as map-N.png.</summary>
        private static byte[] Map()
        {
            var pieces = AiPlan.Current.Snapshot();
            var pic = BriefMap.Draw(s_brief, 1000, pieces, p => AiChecks.RadiusOf(s_site, p));
            s_brief.MapMetresPerPixel = pic.MetresPerPixel;
            var png = ImageConversion.EncodeArrayToPNG(pic.Rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)pic.W, (uint)pic.H);
            try { File.WriteAllBytes(Path.Combine(s_folder, $"map-{s_maps}.png"), png); } catch (Exception) { }
            s_maps++;
            return png;
        }

        private static void Log(JObject o)
        {
            try { File.AppendAllText(Path.Combine(s_folder, "transcript.jsonl"), o.ToString(Newtonsoft.Json.Formatting.None) + "\n"); }
            catch (Exception) { }
        }

        private static void Summary(string model)
        {
            try
            {
                var s = new JObject
                {
                    ["model"] = model, ["prompt"] = AiPrompts.AgentVersion, ["handbook"] = AiHandbook.Version, ["state"] = s_end.ToString(), ["costUsd"] = Math.Round(s_cost, 4), ["tokens"] = s_tokens,
                    ["pieces"] = AiPlan.Current.Count, ["maps"] = s_maps, ["wishes"] = new JArray(Wishes), ["problems"] = new JArray(AiChecks.All(s_site, AiPlan.Current.Snapshot())),
                };
                File.WriteAllText(Path.Combine(s_folder, "summary.json"), s.ToString());
                Mod.Log.Info($"Placemaker AI: {s_end}, {AiPlan.Current.Count} pieces, {s_tokens} tokens, ${s_cost:0.0000} so far -> {s_folder}");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker AI: summary failed: " + e.Message); }
        }

        /// <summary>Main thread: every object asset the game has loaded, with its real size (the catalogued ones with their category).</summary>
        private static List<AiAsset> AssetIndex()
        {
            var list = new List<AiAsset>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cat = Services.Assets;
            if (cat == null) return list;
            foreach (var f in typeof(AssetCategories).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
                var c = (string)f.GetRawConstantValue();
                if (c.StartsWith("net.") || c.StartsWith("parking") || c == "zone") continue;
                IReadOnlyList<CatalogItem> items;
                try { items = cat.Items(c); } catch (Exception) { continue; }
                if (items == null) continue;
                foreach (var it in items)
                {
                    if (string.IsNullOrEmpty(it.Id) || !seen.Add(it.Id)) continue;
                    var a = new AiAsset { Id = it.Id, Name = it.DisplayName ?? it.Id, Category = c, Tags = it.Tags ?? Array.Empty<string>(), Icon = it.Icon ?? "" };
                    if (it.Info.BoundsMax.x > it.Info.BoundsMin.x)
                    {
                        var size = it.Info.BoundsMax - it.Info.BoundsMin;
                        a.Size = new float3(size.x, size.y, size.z);
                        var mid = (it.Info.BoundsMax + it.Info.BoundsMin) * 0.5f;
                        a.Offset = new float2(mid.x, mid.z);
                    }
                    list.Add(a);
                }
            }
            // everything else the game has loaded (not sorted into a category by the mod): the whole library, mods included
            if (cat is Placemaker.Game.Assets.AssetCatalogSystem sys)
            {
                foreach (var o in sys.AllPlaceableObjects())
                {
                    if (!seen.Add(o.Name)) continue;
                    var size = o.Max - o.Min;
                    var mid = (o.Max + o.Min) * 0.5f;
                    // sorted by what the game says it is: its own menu group, else its kind
                    string category = o.Tree ? AssetCategories.Tree : o.Plant ? AssetCategories.Bush : o.StreetLight ? AssetCategories.Lamp : o.Decal ? AssetCategories.Decal : "object";
                    list.Add(new AiAsset
                    {
                        Id = o.Name, Name = o.DisplayName, Category = category, Group = o.Group, Icon = o.Icon ?? "",
                        Tags = string.IsNullOrEmpty(o.Group) ? Array.Empty<string>() : new[] { System.Text.RegularExpressions.Regex.Replace(o.Group, "([a-z])([A-Z])", "$1 $2") },
                        Size = size, Offset = new float2(mid.x, mid.z),
                    });
                }
                foreach (var x in list) x.Vanilla = sys.IsVanillaName(x.Id);
                var ail = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    var dir = Path.Combine(global::Colossal.PSI.Environment.EnvPath.kUserDataPath, "ModsData", "AssetIconLibrary", ".Thumbnails");
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir, "StaticObjectPrefab.*.png")) ail.Add(Path.GetFileNameWithoutExtension(f).Substring("StaticObjectPrefab.".Length));
                }
                catch (Exception) { }
                foreach (var x in list)
                    x.HasPicture = ail.Contains(x.Id) || (x.Icon ?? "").StartsWith("assetdb://", StringComparison.OrdinalIgnoreCase) || (x.Icon ?? "").StartsWith("coui://ail/", StringComparison.OrdinalIgnoreCase);
                Mod.Log.Info($"Placemaker AI: {list.Count(x => x.HasPicture)} of {list.Count} assets have a picture ({ail.Count} Asset Icon Library files)");
            }
            return list;
        }
    }
}
