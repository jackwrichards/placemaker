// Offline replay of neighbourhood polish (a tool, not a check): the game runner's lab op "polishDump" records every house
// as the planner sees it; this replays YardStitch.Plan on them and draws the plan as an SVG, so the planner can be tried
// in seconds instead of a game launch. Runs only when PM_POLISH_REPLAY names a scenario result file (or a folder of them):
//   $env:PM_POLISH_REPLAY = "<run>\scenarios"; dotnet test Tests/Placemaker.Tests --filter NeighbourReplay
// Pictures go to PM_POLISH_OUT (default: next to the input), one per dump: polish-<scenario>.svg.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class NeighbourReplay
    {
        [Fact]
        public void Replay()
        {
            var input = Environment.GetEnvironmentVariable("PM_POLISH_REPLAY");
            if (string.IsNullOrEmpty(input)) return;
            var files = Directory.Exists(input) ? Directory.GetFiles(input, "*.json") : new[] { input };
            var outDir = Environment.GetEnvironmentVariable("PM_POLISH_OUT");
            var settings = new StitchSettings();
            foreach (var kv in (Environment.GetEnvironmentVariable("PM_POLISH_KNOBS") ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = kv.Split('=');
                if (p.Length == 2) settings.Extra[p[0].Trim()] = float.Parse(p[1], CultureInfo.InvariantCulture);
            }
            foreach (var file in files)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var dumps = new List<JsonElement>();
                Find(doc.RootElement, dumps);
                int k = 0;
                foreach (var dump in dumps)
                {
                    var houses = dump.EnumerateArray().Select(ReadHouse).ToList();
                    // PM_POLISH_ONLY="843,35": only these houses (by the last three digits the picture labels them with)
                    var only = (Environment.GetEnvironmentVariable("PM_POLISH_ONLY") ?? "").Split(',').Where(x => x.Trim().Length > 0).Select(x => long.Parse(x.Trim())).ToHashSet();
                    if (only.Count > 0) houses = houses.Where(h => only.Contains(h.Id % 1000)).ToList();
                    if (houses.Count == 0) continue;
                    var plan = YardStitch.Plan(houses, settings);
                    var name = Path.GetFileNameWithoutExtension(file) + (dumps.Count > 1 ? "-" + k : "");
                    var dir = string.IsNullOrEmpty(outDir) ? Path.GetDirectoryName(file) : outDir;
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "polish-" + name + ".svg"), Draw(houses, plan));
                    var pairs = plan.Pairs.Select(p => $"pair {p.Left % 1000} {(p.Rear ? "behind/facing" : "beside")} {p.Right % 1000}: gaps {p.FrontGap:F1} / {p.BackGap:F1}");
                    File.WriteAllText(Path.Combine(dir, "polish-" + name + ".txt"), string.Join("\n", plan.Notes.Concat(pairs)));
                    k++;
                }
            }
        }

        private static void Find(JsonElement e, List<JsonElement> found)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "houses" && p.Value.ValueKind == JsonValueKind.Array) found.Add(p.Value);
                    else Find(p.Value, found);
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Find(x, found);
        }

        private static float2 P(JsonElement e) => new float2(e[0].GetSingle(), e[1].GetSingle());
        private static float2[] Ring(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(P).ToArray() : null;

        private static StitchHouse ReadHouse(JsonElement e)
        {
            var h = new StitchHouse { Id = e.GetProperty("id").GetInt64(), Position = P(e.GetProperty("pos")), Forward = P(e.GetProperty("fwd")), HalfLot = P(e.GetProperty("half")) };
            foreach (var l in e.GetProperty("lines").EnumerateArray())
                h.Lines.Add(new StitchLine { Id = l.GetProperty("id").GetInt64(), Name = l.GetProperty("name").GetString(), A = P(l.GetProperty("a")), B = P(l.GetProperty("b")) });
            foreach (var l in e.GetProperty("lawns").EnumerateArray()) h.Lawns.Add(new StitchLawn { Id = l.GetProperty("id").GetInt64(), Ring = Ring(l.GetProperty("ring")) });
            foreach (var r in e.GetProperty("hard").EnumerateArray()) { var ring = Ring(r); if (ring != null) h.Hard.Add(ring); }
            if (e.TryGetProperty("footprint", out var fp)) h.Footprint = Ring(fp);
            if (e.TryGetProperty("main", out var main) && main.ValueKind == JsonValueKind.String) h.MainSurface = main.GetString();
            if (e.TryGetProperty("zone", out var zone) && zone.ValueKind == JsonValueKind.String) h.Zone = zone.GetString();
            if (e.TryGetProperty("props", out var props)) foreach (var r in props.EnumerateArray()) { var ring = Ring(r); if (ring != null) h.Props.Add(ring); }
            return h;
        }

        private static string Draw(List<StitchHouse> houses, StitchPlan plan)
        {
            var all = houses.SelectMany(h => new[] { h.FrontLeft, h.FrontRight, h.BackLeft, h.BackRight }).ToList();
            var lo = all.Aggregate(new float2(float.MaxValue), math.min) - 10f;
            var hi = all.Aggregate(new float2(float.MinValue), math.max) + 10f;
            var sb = new StringBuilder();
            string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            string Pt(float2 p) => F(p.x - lo.x) + "," + F(hi.y - p.y);
            string Poly(float2[] r, string style) => r == null || r.Length < 3 ? "" : $"<polygon points=\"{string.Join(" ", r.Select(Pt))}\" style=\"{style}\"/>\n";
            string Line(float2 a, float2 b, string style) => $"<line x1=\"{F(a.x - lo.x)}\" y1=\"{F(hi.y - a.y)}\" x2=\"{F(b.x - lo.x)}\" y2=\"{F(hi.y - b.y)}\" style=\"{style}\"/>\n";
            var size = hi - lo;
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(size.x)} {F(size.y)}\" width=\"1000\" height=\"{F(1000f * size.y / size.x)}\">\n");
            sb.Append($"<rect width=\"{F(size.x)}\" height=\"{F(size.y)}\" fill=\"#e8e4d0\"/>\n");
            foreach (var c in plan.Commons) sb.Append(Poly(c, "fill:#2e8b57;fill-opacity:0.55;stroke:#145;stroke-width:0.3"));
            foreach (var h in houses)
            {
                var lawnsDone = new HashSet<long>();
                foreach (var l in h.Lawns)
                    sb.Append(Poly(plan.Lawns.TryGetValue(l.Id, out var grown) ? grown : l.Ring, plan.Lawns.ContainsKey(l.Id) ? "fill:#5cb85c;fill-opacity:0.75;stroke:#2a6;stroke-width:0.15" : "fill:#8fd18f;fill-opacity:0.6"));
                foreach (var r in h.Hard) sb.Append(Poly(r, "fill:#a9a9a9;fill-opacity:0.8"));
                sb.Append(Poly(h.Footprint, "fill:#7a4a3a;fill-opacity:0.85"));
                sb.Append(Poly(new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft }, "fill:none;stroke:#555;stroke-width:0.15;stroke-dasharray:1,1"));
                // the street side
                sb.Append(Line(h.FrontLeft, h.FrontRight, "stroke:#333;stroke-width:0.6"));
            }
            foreach (var h in houses) foreach (var r in h.Props) sb.Append(Poly(r, "fill:#4a6fa5;fill-opacity:0.7"));
            foreach (var bed in plan.Beds) sb.Append(Poly(bed.Ring, "fill:#6b4f2a;fill-opacity:0.85"));
            foreach (var path in plan.Paths) sb.Append(Poly(path.Ring, "fill:#d8d2c0;fill-opacity:0.95;stroke:#998;stroke-width:0.1"));
            foreach (var bush in plan.Bushes) sb.Append($"<circle cx=\"{F(bush.Position.x - lo.x)}\" cy=\"{F(hi.y - bush.Position.y)}\" r=\"0.9\" fill=\"{(bush.Kind >= 5 ? "#777" : bush.Kind >= 2 && bush.Kind <= 3 ? "#c04d8a" : "#1e5a1e")}\"/>\n");
            foreach (var kv in plan.NewLawns) sb.Append(Poly(kv.Ring, "fill:#3fa34d;fill-opacity:0.75;stroke:#185;stroke-width:0.15"));
            foreach (var kv in plan.Plots)
            {
                int hue = (int)((ulong)(kv.Key * 2654435761L) % 360UL);
                sb.Append(Poly(kv.Value, $"fill:hsl({hue},70%,50%);fill-opacity:0.18;stroke:#1f4fd1;stroke-width:0.3;stroke-dasharray:2,1"));
                var h = houses.First(x => x.Id == kv.Key);
                sb.Append($"<text x=\"{F(h.Position.x - lo.x)}\" y=\"{F(hi.y - h.Position.y)}\" font-size=\"3\" fill=\"#000\">{kv.Key % 1000}</text>\n");
            }
            foreach (var h in houses)
                foreach (var l in h.Lines)
                {
                    if (plan.HideLines.Contains(l.Id)) { sb.Append(Line(l.A, l.B, "stroke:#d00;stroke-width:0.25;stroke-dasharray:0.6,0.6")); continue; }
                    var (a, b) = plan.MoveLines.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
                    int score = YardStitch.Score(l.Name);
                    string colour = score == 3 ? "#0b5d1e" : score == 2 ? "#8b5a2b" : score == 1 ? "#555" : "#c80";
                    sb.Append(Line(a, b, $"stroke:{colour};stroke-width:{(plan.MoveLines.ContainsKey(l.Id) ? "0.9" : "0.55")};stroke-linecap:round"));
                }
            sb.Append("</svg>\n");
            return sb.ToString();
        }
    }
}
