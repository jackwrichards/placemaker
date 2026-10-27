// AI builder (docs/AI-BUILDER.md), phase one step 1: the site brief and its map of the current selection, sent to the AI
// with the player's prompt; the reply, the tokens and the cost logged and saved to ModsData/Placemaker/ai/<time>/
// (brief.txt, map.png, request.json without the image or key, reply.txt, call.json). The capture runs on the main
// thread (ground, water, roads, buildings), the call on a worker.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Simulation;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Ai;
using Placemaker.Game.Settings;
using Placemaker.Game.Tool;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Ai
{
    public static class AiBuilder
    {
        public enum State { Idle, Working, Done, Failed }

        private static volatile State s_State = State.Idle;
        public static State Status => s_State;
        public static string LastFolder { get; private set; }
        public static string LastError { get; private set; }
        public static AiReply LastReply { get; private set; }
        public static int Runs { get; private set; }

        public const string DefaultEndpoint = "https://openrouter.ai/api/v1/chat/completions";
        public const string DefaultModel = "anthropic/claude-sonnet-5.5";

        /// <summary>The key: the setting, else %USERPROFILE%\.placemaker\ai-key.txt (for test games). Never logged.</summary>
        private static string Key(PlacemakerSettings s, out string from)
        {
            from = "the setting";
            var k = s?.AiKey?.Trim();
            if (!string.IsNullOrEmpty(k)) return k;
            try
            {
                var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".placemaker", "ai-key.txt");
                from = "ai-key.txt";
                if (File.Exists(f)) return File.ReadAllText(f).Trim();
            }
            catch (Exception) { }
            from = "nowhere";
            return "";
        }

        /// <summary>Starts one call for the current selection. False (and why) when it cannot start. <paramref name="probe"/> (the game runner):
        /// a deliberately invalid key, to prove the network and the endpoint without anyone's key (expect HTTP 401).</summary>
        public static bool Start(string prompt, out string why, bool probe = false)
        {
            why = null;
            if (s_State == State.Working) { why = "a call is already running"; return false; }
            var world = World.DefaultGameObjectInjectionWorld;
            var tool = world?.GetExistingSystemManaged<PlacemakerToolSystem>();
            var shape = tool?.Shape;
            if (shape?.Ring == null || shape.Ring.Length < 3) { why = "no area selected"; return false; }
            var settings = ToolRegistration.Settings;
            string keyFrom = "the probe (an invalid key)";
            string key = probe ? "pm-probe-not-a-key" : Key(settings, out keyFrom);
            if (string.IsNullOrEmpty(key)) { why = "no API key (the AI builder setting, or ai-key.txt)"; return false; }
            string endpoint = string.IsNullOrWhiteSpace(settings?.AiEndpoint) ? DefaultEndpoint : settings.AiEndpoint.Trim();
            string model = string.IsNullOrWhiteSpace(settings?.AiModel) ? DefaultModel : settings.AiModel.Trim();

            string folder, briefText;
            byte[] png;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var input = Capture(world, tool, shape.Ring2D(), prompt ?? "");
                var pic = BriefMap.Draw(input);
                input.MapMetresPerPixel = pic.MetresPerPixel;
                briefText = SiteBrief.Write(input);
                png = Encode(pic);
                folder = Path.Combine(ModPaths.DataFolder, "ai", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "brief.txt"), briefText);
                File.WriteAllBytes(Path.Combine(folder, "map.png"), png);
                Mod.Log.Info($"Placemaker AI: brief of the selection ({briefText.Length} chars, map {pic.W}x{pic.H} px, {input.Nets.Count} nets, {input.Buildings.Count} building quads) in {sw.ElapsedMilliseconds} ms -> {folder}");
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "Placemaker AI: the site brief failed");
                why = "the site brief failed: " + e.Message;
                return false;
            }

            var body = AiClient.Body(model, AiPrompts.System, briefText, Convert.ToBase64String(png), 2000);
            var saved = (JObject)body.DeepClone();
            foreach (var c in saved["messages"][1]["content"].Where(c => (string)c["type"] == "image_url").ToList())
                c["image_url"]["url"] = "(map.png)";
            File.WriteAllText(Path.Combine(folder, "request.json"), saved.ToString());

            s_State = State.Working;
            LastFolder = folder;
            LastError = null;
            LastReply = null;
            Runs++;
            Mod.Log.Info($"Placemaker AI: calling {model} at {endpoint} (key from {keyFrom}, prompt {AiPrompts.Version})");
            Task.Run(async () =>
            {
                AiReply r;
                try { r = await AiClient.Send(endpoint, key, body, CancellationToken.None); }
                catch (Exception e) { r = new AiReply { Error = e.Message }; }
                try
                {
                    File.WriteAllText(Path.Combine(folder, "reply.txt"), r.Text ?? "");
                    var call = new JObject
                    {
                        ["model"] = r.Model, ["endpoint"] = endpoint, ["prompt"] = AiPrompts.Version, ["status"] = r.Status, ["error"] = r.Error,
                        ["ms"] = r.Ms, ["promptTokens"] = r.PromptTokens, ["replyTokens"] = r.ReplyTokens, ["costUsd"] = r.Cost,
                    };
                    File.WriteAllText(Path.Combine(folder, "call.json"), call.ToString());
                    if (r.Error != null) File.WriteAllText(Path.Combine(folder, "response.json"), r.RawResponse ?? "");
                }
                catch (Exception e) { Mod.Log.Warn("Placemaker AI: saving the reply failed: " + e.Message); }
                LastReply = r;
                LastError = r.Error;
                if (r.Error == null)
                    Mod.Log.Info($"Placemaker AI: reply from {r.Model} in {r.Ms} ms, {r.PromptTokens} + {r.ReplyTokens} tokens, cost {(r.Cost.HasValue ? "$" + r.Cost.Value.ToString("0.0000") : "unknown")}, {r.Text.Length} chars -> {folder}");
                else
                    Mod.Log.Warn($"Placemaker AI: the call failed after {r.Ms} ms (HTTP {r.Status}): {r.Error}");
                s_State = r.Error == null ? State.Done : State.Failed;
            });
            return true;
        }

        /// <summary>Main thread: the ground on the heightmap's own spacing, the water, the roads and buildings around the selection.</summary>
        private static SiteBriefInput Capture(World world, PlacemakerToolSystem tool, float2[] ring, string prompt)
        {
            var (origin, cell, nx, nz) = SiteBrief.GroundGrid(ring, margin: 30f);
            var g = new BriefGround { Origin = origin, Cell = cell, Nx = nx, Nz = nz, Height = new float[nx * nz] };
            var terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
            var hd = terrain.GetHeightData(true);
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    var c = g.CellCentre(i, j);
                    g.Height[j * nx + i] = TerrainUtils.SampleHeight(ref hd, new float3(c.x, 0f, c.y));
                }
            var site = tool.AiSite();
            if (site != null)
            {
                bool any = false;
                var w = new float[nx * nz];
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++) { w[j * nx + i] = site.WaterDepthAt(g.CellCentre(i, j)); any |= w[j * nx + i] > 0.05f; }
                if (any) g.Water = w;
            }
            var input = new SiteBriefInput { Ring = ring, Ground = g, Prompt = prompt };
            if (site != null)
            {
                input.Nets.AddRange(site.NearbyNets);
                // lot and footprint quads come in pairs that often coincide: keep one of each
                foreach (var o in site.Obstacles)
                {
                    if (o.Kind != Core.ObstacleKind.Building) continue;
                    if (input.Buildings.Any(b => math.distance(b.A, o.A) + math.distance(b.C, o.C) < 0.5f)) continue;
                    input.Buildings.Add(o);
                }
            }
            return input;
        }

        private static byte[] Encode(BriefMap.Picture pic)
        {
            var tex = new Texture2D(pic.W, pic.H, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixelData(pic.Rgba, 0);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }
    }
}
