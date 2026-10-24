// Outgrow tuner (--serve PORT label=pass.json ...): a local page with sliders for the planner's settings, each recorded pass
// planned live as installed and with the sliders, side by side; a grade, a note and circles drawn on the picture saved to
// <worktree>\.forensics\grades for the next round of fixes (the user in the loop: "I can give you a letter grading and circle
// things").

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Placemaker.Core.Neighbours;

namespace Placemaker.OutgrowBench
{
    internal static class Serve
    {
        private sealed class KnobInfo
        {
            public string id { get; set; }
            public string kind { get; set; }
            public float min { get; set; }
            public float max { get; set; }
            public float step { get; set; }
            public float def { get; set; }
            public string group { get; set; }
            public string tip { get; set; }
        }

        // the settings that shape where gardens end (the rest is planting and paint)
        private static readonly KnobInfo[] Knobs =
        {
            new KnobInfo { id = "sideReach", kind = "slider", min = 0, max = 300, step = 1, def = 100, group = "reach", tip = "how far a garden reaches sideways to meet the neighbour beside it" },
            new KnobInfo { id = "maxRearGap", kind = "slider", min = 0, max = 300, step = 1, def = 60, group = "reach", tip = "two gardens back to back this far apart meet in the middle" },
            new KnobInfo { id = "maxDeepen", kind = "slider", min = 0, max = 300, step = 1, def = 60, group = "reach", tip = "a shallow garden reaches back to its row's deepest by up to this much" },
            new KnobInfo { id = "maxFacingGap", kind = "slider", min = 0, max = 300, step = 1, def = 50, group = "reach", tip = "lots at an angle share the land between them this far apart" },
            new KnobInfo { id = "hugReach", kind = "slider", min = 0, max = 300, step = 1, def = 80, group = "reach", tip = "a garden side nobody else has reaches out to a road, path or building this far" },
            new KnobInfo { id = "openReach", kind = "slider", min = 0, max = 300, step = 1, def = 20, group = "reach", tip = "how far a garden reaches into open land" },
            new KnobInfo { id = "maxFrontGap", kind = "slider", min = 0, max = 300, step = 1, def = 100, group = "reach", tip = "side neighbours this far apart at the street join" },
            new KnobInfo { id = "maxBackGap", kind = "slider", min = 0, max = 300, step = 1, def = 150, group = "reach", tip = "side neighbours this far apart at the back join" },
            new KnobInfo { id = "hug", kind = "toggle", def = 1, group = "rules", tip = "garden sides reach out to a road, path or building" },
            new KnobInfo { id = "wrap", kind = "toggle", def = 1, group = "rules", tip = "two deep gardens meet behind a shallow one" },
            new KnobInfo { id = "closeBacks", kind = "toggle", def = 1, group = "rules", tip = "open backs close with a freed fence" },
            new KnobInfo { id = "alleys", kind = "toggle", def = 1, group = "rules", tip = "walkways between some houses" },
            new KnobInfo { id = "closeGardens", kind = "toggle", def = 1, group = "rules", tip = "every open garden edge gets fence pieces" },
            new KnobInfo { id = "shareBetween", kind = "toggle", def = 1, group = "planner", tip = "lots no joining rule fits share the land between them" },
            new KnobInfo { id = "capNarrow", kind = "toggle", def = 1, group = "planner", tip = "a garden ends straight across where, behind its lot, it gets narrow or splits into prongs" },
            new KnobInfo { id = "minGardenWidth", kind = "slider", min = 0, max = 40, step = 1, def = 8, group = "reach", tip = "narrower than this (or half its lot) behind its lot, a garden ends" },
            new KnobInfo { id = "squareCorners", kind = "toggle", def = 0, group = "planner", tip = "two lots round a block corner split along one's side line (else the 45° middle line, shared fairly)" },
            new KnobInfo { id = "squareSplits", kind = "toggle", def = 1, group = "planner", tip = "two gardens growing into each other on one grid split along the grid (else square to their corners: a slant)" },
            new KnobInfo { id = "shareGrid", kind = "toggle", def = 1, group = "planner", tip = "lots on one grid share a box split along the grid (else a slanted hull)" },
            new KnobInfo { id = "fillGaps", kind = "toggle", def = 1, group = "planner", tip = "strips between gardens nobody took go to the garden beside them" },
            new KnobInfo { id = "separate", kind = "toggle", def = 1, group = "planner", tip = "gardens over each other split by the nearer house" },
            new KnobInfo { id = "steps", kind = "toggle", def = 1, group = "planner", tip = "a step where two neighbours' backs are at different depths" },
            new KnobInfo { id = "walkwayMeet", kind = "toggle", def = 1, group = "planner", tip = "behind a walkway the two gardens meet on the middle line" },
        };

        public static int Run(int port, List<(string Label, string Path)> passes, string gradesDir)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            Directory.CreateDirectory(gradesDir);
            Console.WriteLine($"outgrow tuner on http://localhost:{port}/ ({passes.Count} passes; grades to {gradesDir})");
            var page = Path.Combine(AppContext.BaseDirectory, "tuner.html");
            while (true)
            {
                var ctx = listener.GetContext();
                try { Handle(ctx, passes, gradesDir, page); }
                catch (Exception e)
                {
                    try { Reply(ctx, 500, "text/plain", e.ToString()); } catch (Exception) { }
                }
            }
        }

        private static void Reply(HttpListenerContext ctx, int code, string type, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = type + "; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void Handle(HttpListenerContext ctx, List<(string Label, string Path)> passes, string gradesDir, string page)
        {
            var path = ctx.Request.Url.AbsolutePath;
            if (path == "/" || path == "/index.html") { Reply(ctx, 200, "text/html", File.ReadAllText(page)); return; }
            if (path == "/api/passes") { Reply(ctx, 200, "application/json", JsonSerializer.Serialize(passes.Select((p, i) => new { i, label = p.Label }))); return; }
            if (path == "/api/knobs") { Reply(ctx, 200, "application/json", JsonSerializer.Serialize(Knobs)); return; }
            if (path == "/api/plan")
            {
                int i = int.Parse(ctx.Request.QueryString["pass"] ?? "0");
                var knobs = string.IsNullOrEmpty(ctx.Request.QueryString["knobs"]) ? new Dictionary<string, float>() : JsonSerializer.Deserialize<Dictionary<string, float>>(ctx.Request.QueryString["knobs"]);
                var (houses, settings) = StitchDump.Read(File.ReadAllText(passes[i].Path));
                Score.Apply(houses, settings, knobs);
                var clock = Stopwatch.StartNew();
                var plan = YardStitch.Plan(houses, settings);
                double ms = clock.Elapsed.TotalMilliseconds;
                var notes = plan.Notes.Where(n => !n.Contains("a line inside its garden goes") && !n.StartsWith("alloc") && !n.Contains("has no right neighbour")).ToList();
                Reply(ctx, 200, "application/json", JsonSerializer.Serialize(new { svg = Svg.Draw(houses, settings, plan), notes, ms = Math.Round(ms) }));
                return;
            }
            if (path == "/api/grade" && ctx.Request.HttpMethod == "POST")
            {
                string body;
                using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                var stamp = DateTime.Now.ToString("MMdd-HHmmss");
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("png", out var png) && png.GetString() is string data && data.StartsWith("data:image/png;base64,"))
                        File.WriteAllBytes(Path.Combine(gradesDir, $"grade-{stamp}.png"), Convert.FromBase64String(data.Substring("data:image/png;base64,".Length)));
                    var rest = new Dictionary<string, object>();
                    foreach (var p in doc.RootElement.EnumerateObject()) if (p.Name != "png") rest[p.Name] = p.Value.Clone();
                    File.WriteAllText(Path.Combine(gradesDir, $"grade-{stamp}.json"), JsonSerializer.Serialize(rest, new JsonSerializerOptions { WriteIndented = true }));
                }
                Console.WriteLine($"grade saved: grade-{stamp}");
                Reply(ctx, 200, "application/json", JsonSerializer.Serialize(new { saved = $"grade-{stamp}" }));
                return;
            }
            Reply(ctx, 404, "text/plain", "not found");
        }
    }
}
