// Game runner: one scenario (batch or queue), the live queue, and the run's outputs (scenario JSON, summary.json,
// summary.md with what ran and where its pictures and logs are, contact.html).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Tool;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private static readonly HashSet<string> OnSafeSteps = new HashSet<string>(StringComparer.Ordinal)
        { "camera", "shot", "terrain", "select", "delete", "bulldoze", "infoEdit", "infoRemove", "infoUngroup", "wait", "grow" };

        private IEnumerator RunOne(Scenario s)
        {
            ScenarioState st;
            ScenarioState host = null;
            string refused = null;
            if (s.On != null)
            {
                // runs on an earlier scenario's block and build — only while that build stands
                if (!_byId.TryGetValue(s.On, out host)) refused = $"'on' target {s.On} has not run in this session";
                else if (host.Deleted) refused = $"'on' target {s.On} was deleted (put this scenario before the delete)";
                else if (!host.Built) refused = $"'on' target {s.On} has no build (its status: {host.Result["status"]})";
                else if (s.Steps != null)
                {
                    // design steps need the host's design loaded the way a person edits a build
                    bool editFirst = s.Steps.Count > 0 && (string)s.Steps[0]["do"] == "edit" && ((bool?)s.Steps[0]["build"] ?? false);
                    var bad = s.Steps.Select(x => (string)x["do"]).FirstOrDefault(d => !OnSafeSteps.Contains(d));
                    if (bad != null && !editFirst) refused = $"'on' scenarios may only look at or remove the build (camera, shot, select, delete...); '{bad}' needs {{\"do\":\"edit\",\"build\":true}} first";
                }
                st = new ScenarioState { S = s };
                if (host != null)
                {
                    st.Ring = host.Ring; st.Group = host.Group; st.Built = host.Built; st.Focus = host.Focus;
                    st.Markers.AddRange(host.Markers);
                    s.Block = host.S.Block;
                }
            }
            else
            {
                st = new ScenarioState { S = s };
                if (s.Plot != null || s.LandError != null)
                {
                    // its own land, laid out at launch, or now when it came to a running session (SuiteRunner.Land.cs)
                    s.Block = s.Plot != null ? _fixture.PlotBlock(s.Plot) : null;
                    if (s.Plot != null && s.Block == null) yield return LayOutPlotNow(s);
                    s.SiteNote = s.LandError ?? (s.Block == null ? (s.SiteNote ?? "the land could not be laid out") : "plot " + s.Block.Id);
                }
                else
                {
                    if (string.Equals(s.Site, "zoned", StringComparison.OrdinalIgnoreCase) && !_pregrown) yield return Pregrow();
                    s.Block = _fixture.Take(s.Site, s.BlockId, s.Streets, s.SiteFallback, s.Id, out var note);
                    s.SiteNote = note;
                }
            }
            st.FileIndex = _states.Count;
            _states.Add(st);
            _scenario = s.Id;
            float t0 = Time.realtimeSinceStartup;
            if (refused != null)
            {
                st.Result["id"] = s.Id;
                st.Result["status"] = "refused";
                st.Result["refused"] = refused;
                Say($"scenario {s.Id} refused: {refused}");
            }
            else if (_abortBatch != null)
            {
                st.Result["id"] = s.Id;
                st.Result["status"] = "refused";
                st.Result["refused"] = "batch stopped: " + _abortBatch;
            }
            else
                yield return Guard(RunScenario(st), e =>
                {
                    Say($"scenario {s.Id} threw: {e}");
                    st.Result["status"] = "error";
                    st.Result["exception"] = e.GetType().Name + ": " + e.Message;
                });
            if (host != null && refused == null)
            {
                // what the steps did to the build belongs to its scenario too
                host.Built = st.Built; host.Deleted = st.Deleted; host.Group = st.Group; host.Ring = st.Ring ?? host.Ring;
                host.Markers.Clear();
                host.Markers.AddRange(st.Markers);
            }
            st.Result["scenarioSec"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            LogScenario(st);
            WriteScenario(st, st.FileIndex);
            WriteSummary();
            _lastRun = st;
        }

        /// <summary>One line per scenario in suite.log (each step already logged its own line).</summary>
        private void LogScenario(ScenarioState st)
        {
            var r = st.Result;
            int shots = (r["shots"] as JObject)?.Count ?? 0;
            int errors = (r["steps"] as JArray)?.OfType<JObject>().Count(x => x["error"] != null) ?? 0;
            Say($"scenario {st.S.Id}: {r["status"]} in {r["scenarioSec"]} s on {st.S.Block?.Id}{(r["refused"] != null ? ": " + r["refused"] : "")}{(r["failure"] != null ? ": " + r["failure"] : "")}; {shots} shots, {errors} step errors");
        }

        private ScenarioState _lastRun;
        private bool _queuePinned;

        // ───────────── live queue ─────────────

        /// <summary>
        /// Waits for scenario files in &lt;run&gt;/inbox (a scenario, a set {"scenarios": [...]}, {"command":"grow"} or
        /// {"command":"quit"}), runs them in name order, writes &lt;file&gt;.result.json beside them and moves each
        /// processed file to inbox/done.
        /// </summary>
        private IEnumerator QueueLoop()
        {
            _phase = "queue";
            string inbox = Path.Combine(_out, "inbox"), done = Path.Combine(inbox, "done");
            Directory.CreateDirectory(done);
            _summary["inbox"] = inbox;
            WriteSummary();
            Say("queue: waiting for scenario files in " + inbox);
            float idleSince = Time.realtimeSinceStartup;
            bool quit = false;
            while (!quit)
            {
                _scenario = "";
                var files = Directory.GetFiles(inbox, "*.json").Where(f => !f.EndsWith(".result.json", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal).ToList();
                if (files.Count == 0)
                {
                    if (Time.realtimeSinceStartup - idleSince > _cfg.queueIdleMinutes * 60f) { Say("queue: idle timeout"); break; }
                    yield return Seconds(0.4f);
                    continue;
                }
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    JObject doc;
                    try
                    {
                        if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds < 0.3) continue;   // still being written
                        doc = JObject.Parse(File.ReadAllText(file));
                    }
                    catch (Exception e)
                    {
                        AtomicWrite(Path.Combine(inbox, name + ".result.json"), new JObject { ["file"] = name, ["error"] = "unreadable: " + e.Message }.ToString(Formatting.Indented));
                        MoveDone(file, done);
                        continue;
                    }
                    var report = new JObject { ["file"] = name, ["startedUtc"] = DateTime.UtcNow.ToString("o") };
                    string cmd = (string)doc["command"];
                    if (cmd == "quit") { report["command"] = "quit"; quit = true; }
                    else if (cmd == "grow")
                    {
                        _phase = "grow";
                        yield return Guard(CloseTool(), e => { });
                        yield return Guard(Grow((float?)doc["seconds"] ?? 30f, "grow-" + name, (bool?)doc["fast"] ?? true), e => report["error"] = e.Message);
                        report["grow"] = _summary["grow-" + name];
                        _phase = "queue";
                    }
                    else
                    {
                        ScenarioSet set = null;
                        try
                        {
                            CheckSetSchema(doc["scenarios"] != null ? doc : new JObject { ["scenarios"] = new JArray(doc) });
                            set = doc["scenarios"] != null ? doc.ToObject<ScenarioSet>() : new ScenarioSet { scenarios = new List<ScenarioSpec> { doc.ToObject<ScenarioSpec>() } };
                        }
                        catch (Exception e) { report["error"] = "bad scenario file: " + e.Message; }
                        var rows = new JArray();
                        if (set != null)
                        {
                            _phase = "queue-run";
                            // a file's own "settings" hold for that file only (as at a launch); the next file starts from the defaults again
                            bool pins = set.settings != null && set.settings.Count > 0;
                            if (pins || _queuePinned) { PinSettings(set); _queuePinned = pins; }
                            foreach (var s in Expand(set))
                            {
                                yield return RunOne(s);
                                rows.Add(Row(_lastRun));
                                WriteSummary();   // after every build: the game queue follows a file's progress (its ETA)
                            }
                            _phase = "queue";
                        }
                        report["scenarios"] = rows;
                    }
                    report["finishedUtc"] = DateTime.UtcNow.ToString("o");
                    report["contact"] = Path.Combine(_out, "contact.html");
                    AtomicWrite(Path.Combine(inbox, name + ".result.json"), report.ToString(Formatting.Indented));
                    MoveDone(file, done);
                    WriteSummary();
                    idleSince = Time.realtimeSinceStartup;
                    if (quit) break;
                }
            }
            yield return Guard(CloseTool(), e => Say("close tool: " + e.Message));
        }

        private JObject Row(ScenarioState st) => new JObject
        {
            ["id"] = st.S.Id, ["status"] = st.Result["status"], ["refused"] = st.Result["refused"], ["failure"] = st.Result["failure"],
            ["block"] = st.S.Block?.Id, ["kind"] = st.S.Block?.Kind.ToString(), ["sec"] = st.Result["scenarioSec"],
            ["stepErrors"] = new JArray((st.Result["steps"] as JArray ?? new JArray()).OfType<JObject>().Where(x => x["error"] != null).Select(x => $"step {x["i"]} {x["do"]}: {x["error"]}")),
            ["resultFile"] = Path.Combine(_results, $"{st.FileIndex + 1:D2}-{Safe(st.S.Id)}.json"), ["shots"] = st.Result["shots"],
        };

        private static void MoveDone(string file, string done)
        {
            try
            {
                var to = Path.Combine(done, Path.GetFileName(file));
                if (File.Exists(to)) File.Delete(to);
                File.Move(file, to);
            }
            catch (Exception) { try { File.Delete(file); } catch (Exception) { } }
        }

        // ───────────── output ─────────────

        private void WriteScenario(ScenarioState st, int i)
        {
            try { AtomicWrite(Path.Combine(_results, $"{i + 1:D2}-{Safe(st.S.Id)}.json"), st.Result.ToString(Formatting.Indented)); }
            catch (Exception e) { Say("write scenario failed: " + e.Message); }
        }

        private string Headline()
        {
            string state = _summary["aborted"] != null ? $"INCOMPLETE: the game quit in phase {_summary["aborted"]["phase"]} without the runner asking"
                : _summary["fatal"] != null ? "FATAL: " + _summary["fatal"]
                : _phase == "done" || _phase == "quit" ? "finished"
                : _phase.StartsWith("queue") ? "queue session (open)" : "in progress (phase " + _phase + ")";
            var byStatus = _states.GroupBy(st => (string)st.Result["status"] ?? "?").Select(g => $"{g.Count()} {g.Key}");
            int stepErrors = _states.Sum(st => (st.Result["steps"] as JArray)?.OfType<JObject>().Count(x => x["error"] != null) ?? 0);
            return $"{state}. {_states.Count} scenarios ({string.Join(", ", byStatus)}), {stepErrors} step errors. Shots: {_contact.Count}.";
        }

        private void WriteSummary()
        {
            try
            {
                _summary["runId"] = _cfg.runId;
                _summary["set"] = _cfg.set;
                _summary["git"] = _cfg.git;
                _summary["assembly"] = AssemblyVersion();
                _summary["phase"] = _phase;
                _summary["headline"] = Headline();
                _summary["timeline"] = _timeline;
                _summary["profile"] = _cfg.profile;
                lock (_errLock)
                {
                    _summary["errorsTotal"] = _errors;
                    _summary["exceptionsTotal"] = _exceptions;
                    _summary["errorLinesFirst"] = new JArray(_errorLines.Take(60));
                    _summary["otherErrors"] = _otherErrors;
                    _summary["otherErrorLinesFirst"] = new JArray(_otherLines.Take(40));
                }
                var rows = new JArray();
                foreach (var st in _states)
                {
                    var r = st.Result;
                    var b = st.S.Block;
                    rows.Add(new JObject
                    {
                        ["id"] = st.S.Id, ["status"] = r["status"], ["block"] = b?.Id, ["kind"] = b?.Kind.ToString(), ["siteRequested"] = st.S.Site,
                        ["streets"] = b == null ? null : $"W {b.West} / E {b.East} / S {b.South} / N {b.North}",
                        ["sec"] = r["scenarioSec"], ["failure"] = r["failure"], ["unready"] = r["unready"], ["refused"] = r["refused"],
                        ["buildPath"] = r["buildPath"], ["pace"] = r["pace"], ["planComputeMs"] = r["waitsMs"]?["planComputeMs"], ["shots"] = r["shots"], ["waitsMs"] = r["waitsMs"], ["timeouts"] = r["timeouts"],
                    });
                }
                _summary["scenarios"] = rows;
                _summary["fidelity"] = Fidelity();
                AtomicWrite(Path.Combine(_out, "summary.json"), _summary.ToString(Formatting.Indented));
                AtomicWrite(Path.Combine(_out, "summary.md"), SummaryMarkdown());
                WriteContact();
            }
            catch (Exception e) { Say("write summary failed: " + e.Message); }
        }

        private JObject Fidelity()
        {
            int noPacks = _states.Count(st => (st.Result["planIssues"] as JArray)?.Any(i => ((string)i["key"] ?? "").Contains("NoPacks")) ?? false);
            int items = -1;
            try { items = Services.Assets?.Ready == true ? Services.Assets.Items("").Count : -1; } catch (Exception) { }
            return new JObject
            {
                ["profile"] = _cfg.profile, ["codeMods"] = (_summary["codeMods"] as JArray)?.Count ?? 0, ["catalogItems"] = items, ["plansWithoutAssetPacks"] = noPacks,
                ["caveat"] = _cfg.profile == "full" ? "full playset" :
                    "clean profile: no asset packs or other code mods are loaded. Decals, surfaces, centerpieces/fountains and park dressing differ from the user's game (Run-Suite -AllMods loads the user's playset).",
                ["readabilityMode"] = _look?.Enabled ?? false,
            };
        }

        private string SummaryMarkdown()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Game run {_cfg.runId} ({_cfg.set})");
            sb.AppendLine();
            sb.AppendLine("**" + Headline() + "**");
            sb.AppendLine();
            sb.AppendLine("Read the logs: `logs/Placemaker.log` (what Placemaker did: plans, the build audit, left-out pieces, errors), `logs/Player.log` (the game's own errors), `suite.log` (what the runner did, step by step, with timings).");
            sb.AppendLine("Pictures in order: `contact.html`. Each scenario's steps: `scenarios/NN-<id>.json`.");
            sb.AppendLine();
            sb.AppendLine($"Build: git `{_cfg.git}`, assembly `{AssemblyVersion()}`, dll `{OwnDllHash()}`" + (_summary["wrongBuild"] != null ? $" — **WRONG BUILD: {_summary["wrongBuild"]}**" : "") + (_summary["gitMismatch"] != null ? $" — {_summary["gitMismatch"]}" : ""));
            var fid = _summary["fidelity"] as JObject ?? Fidelity();
            sb.AppendLine($"Profile **{fid["profile"]}**, {fid["codeMods"]} code mods, catalog {fid["catalogItems"]} items. {fid["caveat"]}");
            if (_summary["look"] is JObject look) sb.AppendLine("Look (readability mode, not the user's look): " + LookLine(look));
            if (_summary["placemakerSettings"] is JObject ps) sb.AppendLine($"Placemaker settings: {ps["source"]}; differs from the user's: {(ps["differsFromUser"] as JObject)?.Count ?? 0} ({Trunc(ps["differsFromUser"]?.ToString(Formatting.None), 300)})");
            sb.AppendLine($"Grid: {_summary["grid"]}");
            lock (_errLock) sb.AppendLine($"Errors in the game log during the run: {_errors} Placemaker errors, {_exceptions} exceptions, {_otherErrors} others.");
            sb.AppendLine($"Timeline (s since process start): {_timeline.ToString(Formatting.None)}");
            if (_summary["fatal"] != null) sb.AppendLine($"\n**FATAL:** {_summary["fatal"]}");
            if (_summary["batchStopped"] != null) sb.AppendLine($"\n**BATCH STOPPED:** {_summary["batchStopped"]}");
            if (_summary["schemaErrors"] is JArray se && se.Count > 0) sb.AppendLine($"\n**Schema errors:** {se.ToString(Formatting.None)}");
            sb.AppendLine();
            sb.AppendLine("| # | scenario | block (asked) | status | build path | s | plan ms | shots |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < _states.Count; i++)
            {
                var st = _states[i];
                var r = st.Result;
                int shots = (r["shots"] as JObject)?.Count ?? 0;
                sb.AppendLine($"| {i + 1} | {st.S.Id} | {st.S.Block?.Id} {st.S.Block?.Kind} ({st.S.Site}) | {r["status"]}{(r["failure"] != null ? ": " + r["failure"] : "")} | {r["buildPath"]} | {r["scenarioSec"]} | {r["waitsMs"]?["planComputeMs"]} | {shots} |");
            }
            sb.AppendLine();
            var withErrors = _states.Where(st => (st.Result["steps"] as JArray)?.OfType<JObject>().Any(x => x["error"] != null) ?? false).ToList();
            if (withErrors.Count > 0)
            {
                sb.AppendLine("## Steps the runner could not do");
                foreach (var st in withErrors)
                    foreach (var x in ((JArray)st.Result["steps"]).OfType<JObject>().Where(x => x["error"] != null))
                        sb.AppendLine($"- **{st.S.Id}** step {x["i"]} {x["do"]}: {Trunc((string)x["error"], 300)}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
