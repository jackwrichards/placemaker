// Outgrow benchmark: the boot camp score offline (--bootcamp <passes dir> [--rounds N] [--knob NAME=VALUE]... [--scenario
// <file>] [--game <folder>] [--way <label>] [--out <dir>]).
//
// The recorded passes of one round of "every house again" (lab op polishPassDump in suite/scenarios/outgrow-bootcamp.json)
// replayed IN THE GAME'S ORDER: each pass's houses that it does not edit are given the garden and fences an earlier replayed
// pass made for them (as the game hands a pass its neighbours as they stand), and the whole round is replayed N times (default
// 2: the second round meets only this planner's own gardens, as the game's second round does). Then the town as the replay left
// it is measured as the game runner's lab op gardenReport measures the city (GardenReport: coverage of the land Outgrow should
// cover, edges per garden, corner angles) and its fences checked (Fences.CheckBits); the planner timed; the headline score:
//   score = coverage % + plain gardens % (6 edges or fewer) - 5 x (spikes + deep notches per 100 gardens)
//           - 2 x (fence gaps + crossings per 100 gardens)                                          (higher is better)
// --game <folder>: the game's own report of the same run (gardens-<way>.json, coverage-<way>.json, fencemap-*-<way>.json),
// side by side: how far the replay is from the game. Writes gardens-offline.json, coverage-offline.json (tools/suite/
// GardenMap.js <out> offline draws them) and bootcamp-offline.txt to --out (default: the passes dir's parent).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    internal static class Bootcamp
    {
        public static int Run(List<string> args)
        {
            string input = args[0], scenario = null, game = null, way = "now", outDir = null, placesFile = null, roadsFile = null;
            bool quiet = false;
            int rounds = 2;
            var knobs = new Dictionary<string, float>();
            for (int i = 1; i < args.Count; i++)
                switch (args[i])
                {
                    case "--rounds": rounds = int.Parse(args[++i]); break;
                    case "--knob": { var kv = args[++i].Split('='); knobs[kv[0]] = float.Parse(kv[1], CultureInfo.InvariantCulture); break; }
                    case "--scenario": scenario = args[++i]; break;
                    case "--game": game = args[++i]; break;
                    case "--way": way = args[++i]; break;
                    case "--out": outDir = args[++i]; break;
                    case "--places": placesFile = args[++i]; break;
                    case "--roads": roadsFile = args[++i]; break;
                    // (only the score: the black box)
                    case "--quiet": quiet = true; break;
                }
            scenario ??= FindScenario();
            outDir ??= Path.GetDirectoryName(Path.GetFullPath(input.TrimEnd('\\', '/')));
            var files = Directory.GetFiles(input, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            var texts = files.Select(File.ReadAllText).ToList();
            var log = quiet ? TextWriter.Null : Console.Out;
            log.WriteLine($"{files.Count} passes from {input}, replayed in order {rounds}x{(knobs.Count > 0 ? ", knobs " + string.Join(" ", knobs.Select(k => k.Key + "=" + k.Value.ToString(CultureInfo.InvariantCulture))) : "")}");

            // what each house has as the replay goes: its garden and its fences as they stand (name: piece width)
            var garden = new Dictionary<long, float2[]>();
            var fences = new Dictionary<long, List<(float2 A, float2 B, string Name, bool Piece)>>();
            var lastSeen = new Dictionary<long, StitchHouse>();
            var barriers = new Dictionary<string, (float2[] Ring, int Kind)>();
            var times = new List<double>();
            for (int round = 0; round < rounds; round++)
            {
                double roundMs = 0;
                foreach (var text in texts)
                {
                    var (houses, settings) = StitchDump.Read(text);
                    foreach (var kv in knobs) { settings.Extra ??= new Dictionary<string, float>(); settings.Extra[kv.Key] = kv.Value; }
                    // neighbours as an earlier replayed pass left them
                    foreach (var h in houses)
                    {
                        if (h.Edit || !h.Polished || !garden.TryGetValue(h.Id, out var g)) continue;
                        h.Garden = g;
                        h.Standing = fences.TryGetValue(h.Id, out var f) ? f.Select(x => (x.A, x.B)).ToList() : new List<(float2, float2)>();
                    }
                    var clock = Stopwatch.StartNew();
                    var plan = YardStitch.Plan(houses, settings);
                    double ms = clock.Elapsed.TotalMilliseconds;
                    roundMs += ms;
                    if (round == rounds - 1) times.Add(ms);
                    foreach (var h in houses)
                    {
                        if (!lastSeen.ContainsKey(h.Id) || h.Edit) lastSeen[h.Id] = h;
                        if (!h.Edit || !h.Polished) continue;
                        if (plan.Plots.TryGetValue(h.Id, out var p) && p != null && p.Length >= 3) garden[h.Id] = p;
                        var mine = new List<(float2, float2, string, bool)>();
                        foreach (var l in h.Lines)
                        {
                            if (plan.HideLines.Contains(l.Id)) continue;
                            var (a, b) = plan.MoveLines.TryGetValue(l.Id, out var mv) ? mv : (l.A, l.B);
                            if (math.distance(a, b) >= 0.3f) mine.Add((a, b, l.Name, false));
                        }
                        fences[h.Id] = mine;
                    }
                    foreach (var (house, at, yaw, name) in plan.FencePieces)
                    {
                        if (!fences.TryGetValue(house, out var list)) fences[house] = list = new List<(float2, float2, string, bool)>();
                        var d = new float2(math.sin(yaw), math.cos(yaw)) * ((name != null && name.Contains("Short") ? 2f : 4f) * 0.5f);
                        list.Add((at - d, at + d, name, true));
                    }
                    if (settings.Barriers != null)
                        for (int k = 0; k < settings.Barriers.Count; k++)
                        {
                            var r = settings.Barriers[k];
                            if (r == null || r.Length < 3) continue;
                            string key = $"{r.Length}:{r[0].x:F2}:{r[0].y:F2}:{r[r.Length / 2].x:F2}";
                            if (!barriers.ContainsKey(key)) barriers[key] = (r, settings.BarrierKinds != null && k < settings.BarrierKinds.Count ? settings.BarrierKinds[k] : 0);
                        }
                }
                log.WriteLine($"  round {round + 1}: {roundMs:F0} ms planning");
            }

            // ── the town as the replay left it ──
            var places = placesFile != null ? ReadPlacesFile(placesFile) : FindPlaces(game, way, input) ?? ReadPlaces(scenario);
            string PlaceOf(float2 p) { foreach (var pl in places) if (p.x >= pl.Lo.x && p.y >= pl.Lo.y && p.x <= pl.Hi.x && p.y <= pl.Hi.y) return pl.Name; return null; }
            var input2 = new List<GardenReport.Input>();
            foreach (var h in lastSeen.Values)
            {
                if (!h.Polished) continue;
                var g = garden.TryGetValue(h.Id, out var mine) ? mine : h.Garden;
                if (g == null || g.Length < 3) continue;
                input2.Add(new GardenReport.Input { Id = h.Id, Position = h.Position, Forward = h.Forward, HalfLot = h.HalfLot, Ring = g, Place = PlaceOf(h.Position) });
            }
            var placed = input2.Where(g => g.Place != null).ToList();
            // the road surfaces the game's report measured from (roads-<way>.json beside it), else the passes' own (they miss pieces
            // at the junctions: the land to cover comes out larger)
            var roads = (roadsFile != null ? ReadRoadsFile(roadsFile) : ReadRoads(game, way, input)) ?? barriers.Values.Where(b => b.Kind == 0).Select(b => b.Ring).ToList();
            var gardens = GardenReport.Measure(placed, null, roads);
            // (every house's lot in the places, polished or not: "a house near" for the land to cover)
            var lots = lastSeen.Values.Where(h => PlaceOf(h.Position) != null).Select(g => { var r = new float2(g.Forward.y, -g.Forward.x); return new[] { g.Position - r * g.HalfLot.x - g.Forward * g.HalfLot.y, g.Position + r * g.HalfLot.x - g.Forward * g.HalfLot.y, g.Position + r * g.HalfLot.x + g.Forward * g.HalfLot.y, g.Position - r * g.HalfLot.x + g.Forward * g.HalfLot.y }; }).ToList();
            var covers = GardenReport.Coverage(places, roads, input2.Select(g => g.Ring).ToList(), lots);
            // fences as they stand: what the replay made, else as recorded (a house never edited: its standing; an unpolished
            // lot: its own lines)
            var bits = new List<Fences.Bit>();
            foreach (var h in lastSeen.Values)
            {
                if (fences.TryGetValue(h.Id, out var f)) { foreach (var (a, b, name, piece) in f) bits.Add(new Fences.Bit { House = h.Id, A = a, B = b, Piece = piece, W = Fences.W(name) }); continue; }
                if (h.Polished && h.Standing != null) { foreach (var (a, b) in h.Standing) bits.Add(new Fences.Bit { House = h.Id, A = a, B = b, W = 0.2f }); continue; }
                foreach (var l in h.Lines) bits.Add(new Fences.Bit { House = h.Id, A = l.A, B = l.B, W = Fences.W(l.Name) });
            }
            var placedIds = new HashSet<long>(placed.Select(g => g.Id));
            var town = lastSeen.Values.ToList();
            var fenceTally = Fences.CheckBits(town, bits, town.Where(h => placedIds.Contains(h.Id)).Select(h => (h, garden.TryGetValue(h.Id, out var g) ? g : h.Garden)).Where(x => x.Item2 != null).ToList(), barriers.Values.Select(b => b.Ring).ToList());
            var m = Metrics(gardens, covers);
            float n = math.max(1, gardens.Count);
            float score = m.Coverage + m.Plain - 5f * (m.Spikes + m.Notches) / n * 100f - 2f * (fenceTally.Gaps + fenceTally.Crossings) / n * 100f;

            var sb = new StringBuilder();
            sb.AppendLine($"SCORE {score:F1}   (coverage {m.Coverage:F1}% + plain {m.Plain:F1}% - 5 x {(m.Spikes + m.Notches) / n * 100f:F1} spikes+notches per 100 - 2 x {(fenceTally.Gaps + fenceTally.Crossings) / n * 100f:F1} gaps+crossings per 100)");
            sb.AppendLine($"performance: {times.Count} plans, {times.Sum():F0} ms in all, mean {times.Average():F1} ms, slowest {times.Max():F0} ms (one thread, .NET; the game is about 2-3x slower)");
            sb.AppendLine($"gardens {gardens.Count}: coverage {m.Coverage:F1}%, plain (<=6 edges) {m.Plain:F1}%, edges mean {m.MeanEdges:F2}, over 8 edges {m.Over8}, spikes {m.Spikes}, deep notches {m.Notches}, slight bends {m.Slight}, edges under 2 m {m.Short}, extra turning {m.ExtraTurn:F0} deg");
            sb.AppendLine($"fences (whole town): gaps {fenceTally.Gaps}, crossings {fenceTally.Crossings}, doubles {fenceTally.Doubles}, inside a garden {fenceTally.Inside}, stubs {fenceTally.Stubs}, open garden edge {fenceTally.Open:F0} m");
            sb.Append(GardenReport.CoverText(covers));
            if (game != null)
            {
                var gm = GameMetrics(game, way);
                if (gm != null)
                {
                    sb.AppendLine($"the game's own report of the same run ({way}) -> the replay (difference):");
                    void Row(string name, float g, float o, string f = "F1") => sb.AppendLine($"  {name,-28} {g.ToString(f, CultureInfo.InvariantCulture),8} -> {o.ToString(f, CultureInfo.InvariantCulture),8}   ({(o - g).ToString("+0.0;-0.0;0", CultureInfo.InvariantCulture)})");
                    Row("gardens", gm.Value.Gardens, gardens.Count, "F0");
                    Row("coverage %", gm.Value.M.Coverage, m.Coverage);
                    Row("plain gardens %", gm.Value.M.Plain, m.Plain);
                    Row("edges mean", gm.Value.M.MeanEdges, m.MeanEdges, "F2");
                    Row("over 8 edges", gm.Value.M.Over8, m.Over8, "F0");
                    Row("spikes", gm.Value.M.Spikes, m.Spikes, "F0");
                    Row("deep notches", gm.Value.M.Notches, m.Notches, "F0");
                    Row("slight bends", gm.Value.M.Slight, m.Slight, "F0");
                    Row("edges under 2 m", gm.Value.M.Short, m.Short, "F0");
                    if (gm.Value.Gaps >= 0) sb.AppendLine($"  fences (the game's fence map counts differently): gaps {gm.Value.Gaps}, crossings {gm.Value.Crossings}, doubles {gm.Value.Doubles}");
                }
            }
            LastScore = score;
            LastMs = times.Sum();
            if (quiet) return 0;
            Console.Write(sb.ToString());
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "bootcamp-offline.txt"), sb.ToString() + "\n" + GardenReport.Text(gardens));
            File.WriteAllText(Path.Combine(outDir, "gardens-offline.json"), GardenReport.Json(gardens));
            File.WriteAllText(Path.Combine(outDir, "coverage-offline.json"), GardenReport.CoverJson(covers));
            Console.WriteLine(Path.Combine(outDir, "bootcamp-offline.txt"));
            return 0;
        }

        public static float LastScore;
        public static double LastMs;

        /// <summary>
        /// The black box: every town recorded under <paramref name="dir"/> (town-*/passes, roads.json, places.json; tools/suite/
        /// blackbox.js) scored, the score alone printed: what is in them is not to be known (a planner is not tuned on them).
        /// </summary>
        public static int Blackbox(List<string> args)
        {
            var dir = args[0];
            var towns = Directory.GetDirectories(dir, "town-*").OrderBy(d => d, StringComparer.Ordinal).ToList();
            if (towns.Count == 0) { Console.WriteLine("no black-box towns in " + dir); return 1; }
            var rest = args.Skip(1).ToList();
            float sum = 0f;
            foreach (var town in towns)
            {
                var a = new List<string> { Path.Combine(town, "passes"), "--quiet", "--places", Path.Combine(town, "places.json"), "--roads", Path.Combine(town, "roads.json") };
                a.AddRange(rest);
                Run(a);
                Console.WriteLine($"black box {Path.GetFileName(town)}: score {LastScore:F1} ({LastMs:F0} ms planning)");
                sum += LastScore;
            }
            Console.WriteLine($"black box mean: {sum / towns.Count:F1}");
            return 0;
        }

        private static List<float2[]> ReadRoadsFile(string f) => File.Exists(f) ? JsonNode.Parse(File.ReadAllText(f)).AsArray().Select(r => r.AsArray().Select(p => new float2(p[0].GetValue<float>(), p[1].GetValue<float>())).ToArray()).ToList() : null;

        private static List<GardenReport.Place> ReadPlacesFile(string f)
        {
            var list = new List<GardenReport.Place>();
            if (!File.Exists(f)) return list;
            foreach (var p in JsonNode.Parse(File.ReadAllText(f)).AsArray())
            {
                var at = new float2(p["at"][0].GetValue<float>(), p["at"][1].GetValue<float>());
                float half = p["size"].GetValue<float>() * 0.5f;
                list.Add(new GardenReport.Place { Name = p["name"].GetValue<string>(), Lo = at - half, Hi = at + half });
            }
            return list;
        }

        /// <summary>The places the game's report measured (places-<way>.json beside it or beside the passes).</summary>
        private static List<GardenReport.Place> FindPlaces(string game, string way, string passes)
        {
            foreach (var dir in new[] { game, Path.GetDirectoryName(Path.GetFullPath(passes.TrimEnd('\\', '/'))) })
            {
                if (dir == null) continue;
                var f = Path.Combine(dir, $"places-{way}.json");
                if (File.Exists(f)) return ReadPlacesFile(f);
            }
            return null;
        }

        public struct M { public float Coverage, Plain, MeanEdges, ExtraTurn; public int Over8, Spikes, Notches, Slight, Short; }

        private static M Metrics(List<GardenReport.Garden> gardens, List<GardenReport.Cover> covers)
        {
            var angles = gardens.SelectMany(g => g.Corners.Select(c => c.Angle)).ToList();
            long e = covers.Sum(c => (long)c.Expected), v = covers.Sum(c => (long)c.Covered);
            return new M
            {
                Coverage = e > 0 ? 100f * v / e : 0f,
                Plain = gardens.Count > 0 ? 100f * gardens.Count(g => g.Edges.Count <= 6) / gardens.Count : 0f,
                MeanEdges = gardens.Count > 0 ? (float)gardens.Average(g => g.Edges.Count) : 0f,
                Over8 = gardens.Count(g => g.Edges.Count > 8),
                Spikes = angles.Count(a => a < 45f),
                Notches = angles.Count(a => a > 315f),
                Slight = angles.Count(a => math.abs(180f - a) < 15f),
                Short = gardens.Sum(g => g.Edges.Count(x => x.Length < 2f)),
                ExtraTurn = gardens.Count > 0 ? (float)gardens.Average(g => math.max(0f, g.Turning - 360f)) : 0f,
            };
        }

        private static (int Gardens, M M, int Gaps, int Crossings, int Doubles)? GameMetrics(string dir, string way)
        {
            var gf = Path.Combine(dir, $"gardens-{way}.json");
            var cf = Path.Combine(dir, $"coverage-{way}.json");
            if (!File.Exists(gf) || !File.Exists(cf)) return null;
            var gs = JsonNode.Parse(File.ReadAllText(gf))["gardens"].AsArray().Where(g => g["place"] != null).ToList();
            var cs = JsonNode.Parse(File.ReadAllText(cf)).AsArray();
            float Fv(JsonNode x) => x.GetValue<float>();
            int Iv(JsonNode x) => x.GetValue<int>();
            var angles = gs.SelectMany(g => g["corners"].AsArray().Select(c => Fv(c["angle"]))).ToList();
            long e = cs.Sum(c => (long)Iv(c["expected"])), v = cs.Sum(c => (long)Iv(c["covered"]));
            var m = new M
            {
                Coverage = e > 0 ? 100f * v / e : 0f,
                Plain = gs.Count > 0 ? 100f * gs.Count(g => Iv(g["edges"]) <= 6) / gs.Count : 0f,
                MeanEdges = gs.Count > 0 ? (float)gs.Average(g => Iv(g["edges"])) : 0f,
                Over8 = gs.Count(g => Iv(g["edges"]) > 8),
                Spikes = angles.Count(a => a < 45f),
                Notches = angles.Count(a => a > 315f),
                Slight = angles.Count(a => math.abs(180f - a) < 15f),
                Short = gs.Sum(g => g["edgeList"].AsArray().Count(x => Fv(x["length"]) < 2f)),
            };
            int gaps = -1, crossings = -1, doubles = -1;
            var ff = Directory.GetFiles(dir, $"fencemap-*-{way}.json").FirstOrDefault();
            if (ff != null)
            {
                var j = JsonNode.Parse(File.ReadAllText(ff));
                int Count(string key) => j[key] is JsonArray a ? a.Count : j[key] is JsonValue jv && jv.TryGetValue<int>(out int c) ? c : -1;
                gaps = Count("gaps");
                crossings = Count("crossings");
                doubles = Count("doubles");
            }
            return (gs.Count, m, gaps, crossings, doubles);
        }

        private static List<float2[]> ReadRoads(string game, string way, string passes)
        {
            foreach (var dir in new[] { game, Path.GetDirectoryName(Path.GetFullPath(passes.TrimEnd('\\', '/'))) })
            {
                if (dir == null) continue;
                var f = Path.Combine(dir, $"roads-{way}.json");
                if (!File.Exists(f)) f = Directory.GetFiles(dir, "roads-*.json").FirstOrDefault();
                if (f == null || !File.Exists(f)) continue;
                Console.WriteLine("  roads from " + f);
                return JsonNode.Parse(File.ReadAllText(f)).AsArray().Select(r => r.AsArray().Select(p => new float2(p[0].GetValue<float>(), p[1].GetValue<float>())).ToArray()).ToList();
            }
            return null;
        }

        private static List<GardenReport.Place> ReadPlaces(string scenario)
        {
            var list = new List<GardenReport.Place>();
            if (scenario == null || !File.Exists(scenario)) return list;
            foreach (var sc in JsonNode.Parse(File.ReadAllText(scenario))["scenarios"].AsArray())
                foreach (var step in sc["steps"].AsArray())
                {
                    if (step["op"]?.GetValue<string>() != "gardenReport" || !(step["places"] is JsonArray ps)) continue;
                    foreach (var p in ps)
                    {
                        var at = new float2(p["at"][0].GetValue<float>(), p["at"][1].GetValue<float>());
                        float half = (p["size"]?.GetValue<float>() ?? 500f) * 0.5f;
                        list.Add(new GardenReport.Place { Name = p["name"].GetValue<string>(), Lo = at - half, Hi = at + half });
                    }
                    return list;
                }
            return list;
        }

        private static string FindScenario()
        {
            var d = new DirectoryInfo(Environment.CurrentDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "suite", "scenarios", "outgrow-bootcamp.json"))) d = d.Parent;
            return d == null ? null : Path.Combine(d.FullName, "suite", "scenarios", "outgrow-bootcamp.json");
        }
    }
}
