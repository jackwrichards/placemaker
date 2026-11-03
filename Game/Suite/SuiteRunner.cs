// Game runner: boot → main menu → window at the shot size → new game on a vanilla map → the readable, deterministic
// look (SuiteLook) → the set is read, checked against the schema and the grid sized for it → fixture (heightmap, road
// grid, zoning, plops, pre-grow only when a scenario wants grown housing) → exposure and noise-floor calibration →
// pinned Placemaker settings → every scenario's steps on its own block, with its pictures → summary.json / summary.md /
// contact.html → the queue (optional) → the game's own clean quit. It does what the steps say and takes the pictures;
// it judges nothing (the logs are the evidence). If the game is closed early, the quit hook writes what is known,
// marked incomplete. A MonoBehaviour with coroutines; exists only when SuiteGate opened (command-line token + armed
// run.json).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Core.Suite;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner : MonoBehaviour
    {
        private SuiteRunConfig _cfg;
        private string _out, _shots, _results;
        private string _phase = "boot", _scenario = "";
        private int _index, _total;
        private DateTime _processStart;
        private float _lastBeat = -10f;
        private bool _started;
        private readonly JObject _timeline = new JObject();
        private readonly JObject _summary = new JObject();
        private readonly List<ScenarioState> _states = new List<ScenarioState>();
        private StreamWriter _log;
        private SuiteLook _look;
        private SuiteSchema Schema;
        private bool _pregrown;

        // frame times: each frame is charged to the step that was running when it started, minus the suite's own work
        private readonly List<(float Ms, float Suite, int Frame)> _window = new List<(float, float, int)>(4096);
        private bool _windowOpen, _openAtFrameStart, _tracing;
        private float _lastFrameMs, _worstRecentMs, _worstRecentAt;
        private float _suiteMsThisFrame;
        private string _windowStep = "";

        // errors (UnityLogger events can come from worker threads)
        private readonly object _errLock = new object();
        private int _errors, _exceptions, _otherErrors;
        private readonly List<string> _errorLines = new List<string>();
        private readonly List<string> _otherLines = new List<string>();

        /// <summary>Errors are Placemaker's when its log, message or stack names it; every other error is counted too.</summary>
        private static bool Ours(ILog log, string message, Exception e) =>
            (log?.name ?? "").IndexOf("Placemaker", StringComparison.OrdinalIgnoreCase) >= 0
            || (message ?? "").IndexOf("Placemaker", StringComparison.OrdinalIgnoreCase) >= 0
            || (e?.StackTrace ?? "").IndexOf("Placemaker", StringComparison.Ordinal) >= 0;

        /// <summary>Known noise of a clean profile that no scenario causes.</summary>
        private static bool Whitelisted(string line) =>
            line.IndexOf("ModsSettings", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("C2VM", StringComparison.OrdinalIgnoreCase) >= 0;

        private World _world;
        private PlacemakerToolSystem _tool;
        private SuiteFixture _fixture;
        private SuiteLookup _lookup;

        // ───────────── lifecycle ─────────────

        private void Start()
        {
            _cfg = SuiteGate.Config;
            _out = _cfg.outDir;
            _shots = Path.Combine(_out, "shots");
            _results = Path.Combine(_out, "scenarios");
            Directory.CreateDirectory(_shots);
            Directory.CreateDirectory(_results);
            try { _log = new StreamWriter(Path.Combine(_out, "suite.log"), true) { AutoFlush = true }; } catch (Exception) { }
            try { _processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch (Exception) { _processStart = DateTime.UtcNow; }
            _timeline["processStartUtc"] = _processStart.ToString("o");
            _timeline["runnerStartSec"] = Since();
            UnityLogger.OnErrorOrHigher += OnLogError;
            UnityLogger.OnException += OnLogException;
            Application.wantsToQuit += OnWantsToQuit;
            Application.quitting += OnQuitting;
            Schema = SuiteSchema.Load(_cfg.schemaFile);
            if (Schema.LoadError != null) Say("schema: " + Schema.LoadError);
            OwnDllHash();   // read at boot: the file the game loaded
            Say($"runner up: run {_cfg.runId}, set {_cfg.set}, git {_cfg.git}, assembly {AssemblyVersion()}, dll {OwnDllHash()}");
        }

        private void OnDestroy()
        {
            UnityLogger.OnErrorOrHigher -= OnLogError;
            UnityLogger.OnException -= OnLogException;
            Application.wantsToQuit -= OnWantsToQuit;
            Application.quitting -= OnQuitting;
            try { _log?.Dispose(); } catch (Exception) { }
        }

        private bool OnWantsToQuit()
        {
            if (_phase != "quit")
            {
                string fg = "";
                try { fg = System.Diagnostics.Process.GetCurrentProcess().MainWindowTitle; } catch (Exception) { }
                Say($"QUIT REQUESTED FROM OUTSIDE the suite in phase {_phase} (scenario '{_scenario}'), window '{fg}': {Environment.StackTrace.Replace("\n", " | ")}");
            }
            return true;
        }

        /// <summary>The game is going away: if the suite did not ask for it, record what is known as an incomplete run.</summary>
        private void OnQuitting()
        {
            if (_phase == "quit") return;
            try
            {
                _summary["aborted"] = new JObject { ["phase"] = _phase, ["scenario"] = _scenario, ["utc"] = DateTime.UtcNow.ToString("o"), ["why"] = "the game quit without the suite asking (window closed, Alt+F4, crash handler)" };
                foreach (var st in _states)
                {
                    if ((string)st.Result["status"] == "running") st.Result["status"] = "incomplete";
                    WriteScenario(st, st.FileIndex);
                }
                _phase = "aborted";
                WriteSummary();
                Beat();
            }
            catch (Exception) { }
        }

        private void OnLogError(ILog log, Level level, string message, Exception e, UnityEngine.Object context)
        {
            lock (_errLock)
            {
                string line = $"[{_scenario}] {log?.name}: {Trim(message)}{(e != null ? " | " + e.GetType().Name + ": " + Trim(e.Message) : "")}";
                if (Ours(log, message, e)) { _errors++; if (_errorLines.Count < 400) _errorLines.Add(line); }
                else if (!Whitelisted(line)) { _otherErrors++; if (_otherLines.Count < 400) _otherLines.Add(line); }
            }
        }

        private void OnLogException(Exception e, UnityEngine.Object context)
        {
            lock (_errLock)
            {
                string line = $"[{_scenario}] EXCEPTION {e?.GetType().Name}: {Trim(e?.Message)} @ {Trim((e?.StackTrace ?? "").Split('\n').FirstOrDefault())}";
                if (Ours(null, null, e)) { _exceptions++; if (_errorLines.Count < 400) _errorLines.Add(line); }
                else if (!Whitelisted(line)) { _otherErrors++; if (_otherLines.Count < 400) _otherLines.Add(line); }
            }
        }

        private static string Trim(string s) => s == null ? "" : s.Length > 300 ? s.Substring(0, 300) : s.Replace("\n", " | ");

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;   // the frame that just ended
            _lastFrameMs = ms;
            // the suite work done in the coroutines of the frame that just ended belongs to that frame
            float overhead = _suiteMsThisFrame;
            _suiteMsThisFrame = 0f;
            if (_openAtFrameStart) _window.Add((ms, overhead, Time.frameCount - 1));
            _openAtFrameStart = _windowOpen;
            if (_tracing) Say($"frame {Time.frameCount} {ms:F1} ms sim {_world?.GetExistingSystemManaged<SimulationSystem>()?.frameIndex}");
            if (ms > _worstRecentMs || Time.realtimeSinceStartup - _worstRecentAt > 10f) { _worstRecentMs = ms; _worstRecentAt = Time.realtimeSinceStartup; }
            if (!_started && GameManager.instance != null)
            {
                _started = true;
                StartCoroutine(Guard(Main(), e => Fatal("main", e)));
            }
            if (Time.realtimeSinceStartup - _lastBeat >= 2f) { _lastBeat = Time.realtimeSinceStartup; Beat(); }
        }

        private float Since() => (float)Math.Round((DateTime.UtcNow - _processStart).TotalSeconds, 1);

        private void Mark(string key) { _timeline[key] = Since(); Say($"timeline {key} = {_timeline[key]} s"); }

        private void Say(string s)
        {
            Mod.Log.Info("Placemaker suite: " + s);
            try { _log?.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} [{_phase}/{_scenario}] {s}"); } catch (Exception) { }
        }

        /// <summary>heartbeat.json, written whole (temp file, then swapped) so a reader never sees half of it.</summary>
        private void Beat()
        {
            try
            {
                int err, exc;
                lock (_errLock) { err = _errors; exc = _exceptions; }
                var o = new JObject
                {
                    ["runId"] = _cfg.runId, ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id, ["phase"] = _phase, ["scenario"] = _scenario, ["index"] = _index, ["total"] = _total,
                    ["utc"] = DateTime.UtcNow.ToString("o"), ["sinceStart"] = Since(), ["frame"] = Time.frameCount,
                    ["frameMs"] = Math.Round(_lastFrameMs, 1), ["worstFrameMs10s"] = Math.Round(_worstRecentMs, 1), ["errors"] = err, ["exceptions"] = exc, ["otherErrors"] = _otherErrors,
                    ["gameMode"] = GameManager.instance != null ? GameManager.instance.gameMode.ToString() : "",
                };
                AtomicWrite(Path.Combine(_out, "heartbeat.json"), o.ToString(Formatting.None));
            }
            catch (Exception) { }
        }

        private static void AtomicWrite(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private static string AssemblyVersion()
        {
            try
            {
                var a = typeof(SuiteRunner).Assembly;
                var info = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(a, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                return info?.InformationalVersion ?? a.GetName().Version.ToString();
            }
            catch (Exception) { return "?"; }
        }

        private static string _dllHash;

        /// <summary>The Placemaker.dll the game loaded at boot (Mods/Placemaker; the assembly itself has no file location in CS2).</summary>
        private static string DllPath()
        {
            string loc = typeof(SuiteRunner).Assembly.Location;
            if (!string.IsNullOrEmpty(loc) && File.Exists(loc)) return loc;
            return Path.Combine(Colossal.PSI.Environment.EnvPath.kUserDataPath, "Mods", "Placemaker", "Placemaker.dll");
        }

        /// <summary>First 8 hex digits of the SHA-256 of the Placemaker.dll this game loaded.</summary>
        private static string OwnDllHash()
        {
            if (_dllHash != null) return _dllHash;
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var f = File.OpenRead(DllPath()))
                    _dllHash = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").Substring(0, 8).ToLowerInvariant();
            }
            catch (Exception) { _dllHash = "?"; }
            return _dllHash;
        }

        // ───────────── coroutine helpers ─────────────

        /// <summary>Runs a coroutine tree (nested IEnumerators), turning an exception into onError instead of a dead coroutine.</summary>
        private IEnumerator Guard(IEnumerator root, Action<Exception> onError)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool moved;
                object current = null;
                try
                {
                    moved = top.MoveNext();
                    if (moved) current = top.Current;
                }
                catch (Exception e)
                {
                    onError(e);
                    yield break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private static IEnumerator Seconds(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private bool _timedOut;

        private IEnumerator Until(Func<bool> cond, float timeout, string what)
        {
            _timedOut = false;
            float until = Time.realtimeSinceStartup + timeout;
            while (true)
            {
                bool ok;
                try { ok = cond(); } catch (Exception) { ok = false; }
                if (ok) yield break;
                if (Time.realtimeSinceStartup > until)
                {
                    _timedOut = true;
                    Say($"timeout after {timeout:F0} s waiting for {what}");
                    yield break;
                }
                yield return null;
            }
        }

        private void OpenWindow(int step = -1, string what = "")
        {
            _window.Clear();
            _windowOpen = true;
            _windowStep = step + ":" + what;
        }

        /// <summary>Frame stats of the step: each frame minus the suite's own synchronous work (captures) in it.</summary>
        private JObject CloseWindow()
        {
            _windowOpen = false;
            var net = _window.Select(f => math.max(0f, f.Ms - f.Suite)).ToList();
            var (max, p95, n) = SuiteStats.FrameStats(net);
            int over50 = net.Count(f => f > 50f), over100 = net.Count(f => f > 100f), over250 = net.Count(f => f > 250f), over1000 = net.Count(f => f > 1000f);
            int maxFrame = -1;
            float maxSuite = 0f;
            for (int i = 0; i < _window.Count; i++) if (net[i] >= max - 1e-3f) { maxFrame = _window[i].Frame; maxSuite = _window[i].Suite; break; }
            var top = new JArray(_window.Select((f, i) => (f, net[i])).OrderByDescending(x => x.Item2).Take(3).Select(x => new JArray(x.f.Frame, Math.Round(x.f.Ms, 1), Math.Round(x.f.Suite, 1))));
            return new JObject
            {
                ["frames"] = n, ["maxMs"] = Math.Round(max, 1), ["p95Ms"] = Math.Round(p95, 1), ["over50"] = over50, ["over100"] = over100, ["over250"] = over250, ["over1000"] = over1000,
                ["maxFrame"] = maxFrame, ["maxSuiteMs"] = Math.Round(maxSuite, 1), ["suiteMsTotal"] = Math.Round(_window.Sum(f => f.Suite)), ["top"] = top,
            };
        }

        private (int Errors, int Exceptions, int Other, int Lines, int OtherLines) ErrorMark()
        {
            lock (_errLock) return (_errors, _exceptions, _otherErrors, _errorLines.Count, _otherLines.Count);
        }

        private JObject ErrorsSince((int Errors, int Exceptions, int Other, int Lines, int OtherLines) mark)
        {
            lock (_errLock)
            {
                var lines = new JArray();
                for (int i = mark.Lines; i < _errorLines.Count && lines.Count < 15; i++) lines.Add(_errorLines[i]);
                for (int i = mark.OtherLines; i < _otherLines.Count && lines.Count < 25; i++) lines.Add("(other) " + _otherLines[i]);
                return new JObject { ["errors"] = _errors - mark.Errors, ["exceptions"] = _exceptions - mark.Exceptions, ["otherErrors"] = _otherErrors - mark.Other, ["lines"] = lines };
            }
        }

        private void Fatal(string where, Exception e)
        {
            Say($"FATAL in {where}: {e}");
            _summary["fatal"] = where + ": " + e.GetType().Name + ": " + e.Message;
            WriteSummary();
            if (_cfg.quit) StartCoroutine(Quit());
        }

        // ───────────── main ─────────────

        private IEnumerator Main()
        {
            _phase = "menu";
            yield return Until(() => GameManager.instance.gameMode == GameMode.MainMenu && !GameManager.instance.isGameLoading, 900f, "main menu");
            Mark("mainMenuSec");
            RecordBuildIdentity();
            try
            {
                var gm = GameManager.instance;
                _summary["commandLine"] = string.Join(" ", (gm.cmdLine ?? new string[0]).Skip(1).Where(a => a.IndexOf("token", StringComparison.OrdinalIgnoreCase) < 0 && a.IndexOf("userid", StringComparison.OrdinalIgnoreCase) < 0));
                _summary["disableModding"] = gm.configuration.disableModding;
                _summary["disableCodeModding"] = gm.configuration.disableCodeModding;
                var mods = new JArray();
                if (gm.modManager != null) foreach (var m in gm.modManager) mods.Add(m.name + (m.isLoaded ? "" : " (not loaded)"));
                _summary["codeMods"] = mods;
                Say($"code mods loaded: {mods.Count}: {string.Join(", ", mods.Take(10))}{(mods.Count > 10 ? " ..." : "")}");
            }
            catch (Exception e) { Say("mod list: " + e.Message); }
            yield return Frames(10);
            _world = World.DefaultGameObjectInjectionWorld;
            _tool = _world.GetExistingSystemManaged<PlacemakerToolSystem>();
            if (_tool == null) throw new Exception("PlacemakerToolSystem missing");
            _look = new SuiteLook(_world, _cfg);
            _summary["look"] = _look.Record;
            yield return _look.Window(Say);

            if (_cfg.playground && _cfg.menuOnly)
            {
                // the user's playground at the main menu: nothing started, nothing run, never quit
                _phase = "playground";
                PlacemakerToolSystem.SuiteInputOnly = false;
                _summary["playground"] = new JObject { ["menu"] = true };
                Say("playground ready at the main menu: the controls are yours; the suite does nothing more");
                WriteSummary();
                while (true)
                {
                    Beat();
                    yield return new WaitForSecondsRealtime(5f);
                }
            }

            _phase = "newgame";
            yield return NewGame();
            if (GameManager.instance.gameMode != GameMode.Game) throw new Exception("new game did not load (mode " + GameManager.instance.gameMode + ")");
            if (!_look.WindowOk) yield return _look.Window(Say);   // loading can re-apply the graphics settings
            _look.Apply(Say);

            // the set first: the grid is sized for it
            ScenarioSet set = null;
            List<Scenario> scenarios = null;
            if (!_cfg.fixtureOnly && _cfg.set != "queue")
            {
                set = LoadSet();
                scenarios = Expand(set);
                if (_cfg.maxScenarios > 0 && scenarios.Count > _cfg.maxScenarios) scenarios = scenarios.Take(_cfg.maxScenarios).ToList();
            }

            _phase = "fixture";
            PlacemakerToolSystem.SuiteInputOnly = true;   // from here the tool hears only the suite's scripted mouse and keys
            int nx = _cfg.nx, nz = _cfg.nz;
            if (scenarios != null && nx == 0) nx = GridFor(scenarios, set, nz);
            _fixture = new SuiteFixture(_world, nx, nz) { Spacing = _cfg.spacing ?? "checker" };
            _lookup = new SuiteLookup(_world);
            if (scenarios != null)
                foreach (var s in scenarios.Where(x => x.Plot != null)) _fixture.AddPlot(s.Plot);
            if (_fixture.Layout.Plots.Count > 0)
                _summary["plots"] = new JArray(_fixture.Layout.Plots.Select(p => $"{p.Name}: {p.Shape} {p.Size.x:F0} x {p.Size.y:F0} m, ground {p.Ground}, roads {(p.RoadsAround ? "around" : "none")}"));
            _summary["grid"] = $"{_fixture.Layout.NX} x {_fixture.Layout.NZ} blocks, spacing {_fixture.Spacing}";
            _summary["capacity"] = new JObject { ["flat"] = _fixture.Capacity(SuiteBlockKind.Flat), ["zoned"] = _fixture.Capacity(SuiteBlockKind.Zoned), ["slope"] = _fixture.Capacity(SuiteBlockKind.Slope), ["rough"] = _fixture.Capacity(SuiteBlockKind.Rough), ["survive"] = _fixture.Capacity(SuiteBlockKind.Survive) };
            // a queue-only session grows the housing when its first zoned scenario arrives (RunOne), not at launch
            bool wantsZoned = scenarios == null ? _cfg.set != "queue" : scenarios.Any(s => string.Equals(s.Site, "zoned", StringComparison.OrdinalIgnoreCase));
            yield return Guard(BuildFixture(wantsZoned), e => { Say("fixture failed: " + e); _summary["fixtureError"] = e.Message; });
            yield return Guard(LookCalibrate(), e => Say("look calibration failed: " + e.Message));
            PinSettings(set);
            WriteSummary();

            if (_cfg.playground)
            {
                yield return Playground();
                yield break;
            }

            if (scenarios != null)
            {
                _total = scenarios.Count;
                _summary["scenarioCount"] = scenarios.Count;
                Say($"{scenarios.Count} scenarios: {string.Join(", ", scenarios.Select(s => s.Id))}");
                _phase = "scenarios";
                for (int i = 0; i < scenarios.Count; i++)
                {
                    _index = i + 1;
                    if (_abortBatch != null) { Say("batch stopped: " + _abortBatch); _summary["batchStopped"] = _abortBatch; break; }
                    yield return RunOne(scenarios[i]);
                }
                _scenario = "";
                Mark("scenariosDoneSec");
                yield return Guard(CloseTool(), e => Say("close tool: " + e.Message));
                WriteSummary();
            }
            if (_cfg.queue || _cfg.set == "queue")
            {
                yield return QueueLoop();
                Mark("queueDoneSec");
            }
            _phase = "done";
            Mark("doneSec");
            WriteSummary();
            if (_cfg.quit) yield return Quit();
        }

        private void RecordBuildIdentity()
        {
            string own = OwnDllHash();
            _summary["dllHash"] = own;
            _summary["hostDllHash"] = _cfg.dllHash;
            if (!string.IsNullOrEmpty(_cfg.dllHash) && !string.Equals(_cfg.dllHash, own, StringComparison.OrdinalIgnoreCase))
            {
                _summary["wrongBuild"] = $"the game loaded Placemaker.dll {own}, the host deployed {_cfg.dllHash}";
                _abortBatch = "wrong build: " + _summary["wrongBuild"];
                Say("WRONG BUILD: " + _summary["wrongBuild"]);
            }
            string asm = AssemblyVersion();
            int plus = asm.IndexOf('+');
            string asmHash = plus >= 0 ? asm.Substring(plus + 1) : "";
            string git = (_cfg.git ?? "").Replace("-dirty", "");
            if (asmHash.Length > 0 && git.Length > 0 && !asmHash.StartsWith(git, StringComparison.OrdinalIgnoreCase) && !git.StartsWith(asmHash, StringComparison.OrdinalIgnoreCase))
                _summary["gitMismatch"] = $"the run is labelled git {_cfg.git} but the assembly says {asm}";
        }

        /// <summary>
        /// The dev menu's Test Mode as the user sets it every time (Game.Debug.DebugSystem "Test Mode"): full crime test mode,
        /// superfast building and area-prop spawning, superfast leveling, superfast enter school, and Unlimited demands.
        /// </summary>
        private string PlaygroundTestMode()
        {
            var done = new List<string>();
            void Try(string what, Action set)
            {
                try { set(); done.Add(what); }
                catch (Exception e) { Say("playground test mode: " + what + " failed: " + e.Message); }
            }
            Try("full crime", () => _world.GetOrCreateSystemManaged<CrimeCheckSystem>().debugFullCrimeMode = true);
            Try("building spawning", () => _world.GetOrCreateSystemManaged<ZoneSpawnSystem>().debugFastSpawn = true);
            Try("area-prop spawning", () => _world.GetOrCreateSystemManaged<AreaSpawnSystem>().debugFastSpawn = true);
            Try("leveling", () => _world.GetOrCreateSystemManaged<BuildingUpkeepSystem>().debugFastLeveling = true);
            Try("enter school", () =>
            {
                _world.GetOrCreateSystemManaged<ApplyToSchoolSystem>().debugFastApplySchool = true;
                _world.GetOrCreateSystemManaged<FindSchoolSystem>().debugFastFindSchool = true;
            });
            Try("unlimited demands", () =>
            {
                _world.GetOrCreateSystemManaged<ResidentialDemandSystem>().SetUnlimitedDemand(true);
                _world.GetOrCreateSystemManaged<CommercialDemandSystem>().SetUnlimitedDemand(true);
                _world.GetOrCreateSystemManaged<IndustrialDemandSystem>().SetUnlimitedDemand(true);
            });
            string line = string.Join(", ", done);
            Say("playground test mode on: " + line);
            return line;
        }

        /// <summary>The user's playground: frame the grid at 45°, hand the mouse and keys back, unpause, and stay out of the way.</summary>
        private IEnumerator Playground()
        {
            _phase = "playground";
            var L = _fixture.Layout;
            var pivot = new float3((L.LineX(0) + L.LineX(L.NX)) * 0.5f, L.Base, (L.LineZ(0) + L.LineZ(L.NZ)) * 0.5f);
            float span = math.max(math.abs(L.LineX(L.NX) - L.LineX(0)), math.abs(L.LineZ(L.NZ) - L.LineZ(0)));
            var rec = new JObject();
            yield return Guard(CameraTo(pivot, 25f, 45f, math.max(400f, span * 0.9f), rec, null), e => Say("playground camera: " + e.Message));
            PlacemakerToolSystem.SuiteInputOnly = false;   // the user's mouse and keys work again
            try { _world.GetOrCreateSystemManaged<SimulationSystem>().selectedSpeed = 1f; } catch (Exception e) { Say("playground speed: " + e.Message); }
            rec["testMode"] = PlaygroundTestMode();
            _summary["playground"] = rec;
            Say("playground ready: the grid is framed, the controls are yours; the suite does nothing more");
            WriteSummary();
            while (true)
            {
                Beat();
                yield return new WaitForSecondsRealtime(5f);
            }
        }

        private IEnumerator Quit()
        {
            _phase = "quit";
            Mark("quitRequestedSec");
            WriteSummary();
            Beat();
            yield return Frames(2);
            GameManager.QuitGame();
        }

        /// <summary>The grid's width so every scenario gets a block of the kind it asked for (rows stay the layout's).</summary>
        private int GridFor(List<Scenario> scenarios, ScenarioSet set, int nz)
        {
            var need = new Dictionary<SuiteBlockKind, int>();
            foreach (var s in scenarios.Where(x => x.On == null && x.Plot == null && x.LandError == null))
            {
                var k = SuiteFixture.KindOf(s.Site) ?? SuiteBlockKind.Flat;
                need[k] = (need.TryGetValue(k, out int n) ? n : 0) + 1;
            }
            for (int nx = 9; nx <= 40; nx++)
            {
                var probe = new SuiteFixture(_world, nx, nz) { Spacing = _cfg.spacing ?? "checker" };
                bool ok = need.All(kv => kv.Key == SuiteBlockKind.Survive || probe.Capacity(kv.Key) >= kv.Value);
                if (ok)
                {
                    _summary["blocksNeeded"] = JObject.FromObject(need.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
                    return nx;
                }
            }
            _summary["blocksNeeded"] = JObject.FromObject(need.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
            return 40;
        }

        // ───────────── new game on a vanilla map ─────────────

        /// <summary>
        /// A new game on a vanilla map, set up the way the main menu's New Game does (MenuUISystem.NewGame: map name and
        /// prefab references, the starting year, city options). The map-less world is not usable (runs boot2-boot5).
        /// </summary>
        private IEnumerator NewGame()
        {
            string world = _cfg.world ?? "";
            if (!world.StartsWith("map:", StringComparison.OrdinalIgnoreCase)) throw new Exception("world must be map:<name>, not '" + world + "'");
            string want = world.Substring(4);
            global::Game.Assets.MapMetadata map = null;
            var names = new List<string>();
            foreach (var m in AssetDatabase.global.GetAssets<global::Game.Assets.MapMetadata>())
            {
                string name;
                try { name = m.target?.displayName ?? m.name; } catch (Exception) { name = m.name; }
                names.Add(name);
                if (map == null && string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) map = m;
            }
            if (map == null) throw new Exception($"map '{want}' not found (maps: {string.Join(", ", names)})");
            var info = map.target;

            var city = _world.GetOrCreateSystemManaged<CityConfigurationSystem>();
            city.overrideLoadedOptions = true;
            city.overrideCityName = "Placemaker Suite";
            city.overrideThemeName = null;
            city.overrideLeftHandTraffic = false;
            city.overrideNaturalDisasters = false;
            city.overrideUnlockAll = true;
            city.overrideUnlimitedMoney = true;
            city.overrideUnlockMapTiles = true;
            var meta = _world.GetOrCreateSystemManaged<global::Game.UI.MapMetadataSystem>();
            meta.mapName = info.displayName;
            meta.prefabReferences = info.prefabReferences;
            _world.GetOrCreateSystemManaged<TimeSystem>().startingYear = info.startingYear != -1 ? info.startingYear : DateTime.Now.Year;
            try { SharedSettingsTweaks(); } catch (Exception e) { Say("settings tweaks: " + e.Message); }

            float t0 = Time.realtimeSinceStartup;
            Say($"loading a new game on map {info.displayName}");
            var task = GameManager.instance.Load(GameMode.Game, Purpose.NewGame, map);
            yield return Until(() => task.IsCompleted, 600f, "new game load task");
            bool ok = task.IsCompleted && !task.IsFaulted && task.Result;
            yield return Until(() => GameManager.instance.gameMode == GameMode.Game && !GameManager.instance.isGameLoading, 300f, "game mode");
            _timeline["newGameLoadSec"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            Mark("worldReadySec");
            _summary["map"] = info.displayName;
            Say($"new game load: ok={ok} mode={GameManager.instance.gameMode}");
            yield return Frames(30);
            var sim = _world.GetOrCreateSystemManaged<SimulationSystem>();
            sim.selectedSpeed = 0f;
            try
            {
                var theme = city.defaultTheme;
                _summary["theme"] = theme != Entity.Null ? _world.GetOrCreateSystemManaged<global::Game.Prefabs.PrefabSystem>().GetPrefab<global::Game.Prefabs.PrefabBase>(theme)?.name : "none";
            }
            catch (Exception e) { _summary["theme"] = "? " + e.Message; }
            _summary["leftHandTraffic"] = city.leftHandTraffic;
            _summary["unlimitedMoney"] = true;
            _summary["unlockAll"] = true;
            Event("load");
        }

        private static void SharedSettingsTweaks()
        {
            var s = global::Game.Settings.SharedSettings.instance;
            if (s == null) return;
            s.gameplay.showTutorials = false;
            s.gameplay.pausedAfterLoading = true;
        }

        /// <summary>Exposure calibrated on a flat block (target brightness), the sun recorded, the capture noise floor measured.</summary>
        private IEnumerator LookCalibrate()
        {
            var flat = _fixture.Blocks.FirstOrDefault(b => b.Kind == SuiteBlockKind.Flat) ?? _fixture.Blocks[0];
            var zoned = _fixture.Blocks.FirstOrDefault(b => b.Kind == SuiteBlockKind.Zoned) ?? flat;
            var rec = new JObject();
            yield return CameraTo(FocusOf(flat), 25f, 60f, 320f, rec, null);
            yield return Seconds(1f);
            if (rec["timeouts"] != null) { _look.Record["calibrationCameraTimeout"] = rec["timeouts"]; Say("look calibration: the camera did not arrive; exposure and noise floor may be off"); }
            if (_look.Enabled && _look.HasExposure) yield return _look.CalibrateExposure(Say);
            _look.RecordSun();
            yield return CalibrateNoise(FocusOf(zoned));
            Mark("lookDoneSec");
        }

        /// <summary>Placemaker's settings for the run: its defaults (unless -UserModSettings), the set's pins, all recorded.</summary>
        private void PinSettings(ScenarioSet set)
        {
            var s = ToolRegistration.Settings;
            if (s == null) { _summary["placemakerSettings"] = "unavailable"; return; }
            var user = new JObject();
            var props = s.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum || p.PropertyType == typeof(string))).ToList();
            foreach (var p in props) { try { user[p.Name] = JToken.FromObject(p.GetValue(s) ?? ""); } catch (Exception) { } }
            if (!_cfg.userModSettings)
            {
                try { s.SetDefaults(); } catch (Exception e) { Say("settings defaults: " + e.Message); }
            }
            if (set?.settings != null)
                foreach (var kv in set.settings)
                {
                    var p = s.GetType().GetProperty(kv.Key);
                    if (p == null) { Say("set setting unknown: " + kv.Key); continue; }
                    try { p.SetValue(s, JToken.FromObject(kv.Value).ToObject(p.PropertyType)); } catch (Exception e) { Say($"set setting {kv.Key}: {e.Message}"); }
                }
            try { s.ApplyAndSave(); } catch (Exception) { }
            var eff = new JObject();
            var differs = new JObject();
            foreach (var p in props)
            {
                try
                {
                    var v = JToken.FromObject(p.GetValue(s) ?? "");
                    eff[p.Name] = v;
                    if (user[p.Name] != null && !JToken.DeepEquals(user[p.Name], v)) differs[p.Name] = $"user {user[p.Name]} -> run {v}";
                }
                catch (Exception) { }
            }
            _summary["placemakerSettings"] = new JObject { ["source"] = _cfg.userModSettings ? "the user's" : "Placemaker defaults (pinned)", ["effective"] = eff, ["differsFromUser"] = differs };
            _tool.ReloadRecipes();
        }

        // ───────────── fixture ─────────────

        private SuiteFixtureToolSystem FixtureTool => _world.GetExistingSystemManaged<SuiteFixtureToolSystem>();

        /// <summary>Activates the fixture tool, runs <paramref name="emit"/> inside its update, applies, gives the slot back.</summary>
        private IEnumerator ApplyWithFixtureTool(Func<SuiteFixtureToolSystem, int> emit, string what)
        {
            var ft = FixtureTool;
            var ts = ft.Tools;
            ts.activeTool = ft;
            yield return Until(() => ts.activeTool == ft && ft.Updates > 0, 10f, "fixture tool active");
            yield return Frames(2);
            ft.Requested = ApplyMode.Clear;
            yield return Frames(3);
            int emitted = 0;
            ft.Enqueue(() => emitted = emit(ft));
            yield return Frames(6);
            ft.Requested = ApplyMode.Apply;
            yield return Frames(6);
            int destroyed = ft.DestroyDefinitions();
            yield return Frames(2);
            ts.activeTool = ft.DefaultTool;
            yield return Frames(30);
            Say($"{what}: {emitted} definitions applied ({destroyed} cleaned up)");
        }

        private IEnumerator BuildFixture(bool pregrow)
        {
            float t0 = Time.realtimeSinceStartup;
            _fixture.IndexPrefabs();
            var fx = new JObject();
            _summary["fixture"] = fx;

            yield return Settle("after load");
            bool onMap = !string.Equals(_cfg.world ?? "", "null", StringComparison.OrdinalIgnoreCase);
            if (onMap)
            {
                _fixture.ConfigureFromWorld();
                fx["base"] = Math.Round(_fixture.Layout.Base, 2);
                fx["waterSources"] = _fixture.RemoveWaterSources();
                string cleared = "";
                yield return ApplyWithFixtureTool(t => _fixture.EmitClear(t, out cleared), "clear fixture area");
                fx["cleared"] = cleared;
                fx["staticObjectsLeft"] = _fixture.CountInFixture();
            }
            string err = _cfg.noTerrain ? "skipped" : _fixture.ApplyHeightmap();
            fx["heightmap"] = err ?? "replaced";
            if (onMap) _fixture.ResetWater();
            yield return Frames(40);
            float worst = _fixture.CheckTerrain(out var detail);
            if (worst > 1f) { yield return Frames(60); worst = _fixture.CheckTerrain(out detail); }
            fx["terrainWorstErrorM"] = Math.Round(worst, 2);
            fx["terrainProbes"] = detail;
            Mark("heightmapDoneSec");

            yield return Settle("before roads");
            _tracing = _cfg.trace;
            string roads = (_cfg.roads ?? "single").ToLowerInvariant();
            if (roads == "single")
                yield return ApplyWithFixtureTool(t => _fixture.EmitRoads(t, null), "roads all");
            else if (roads != "none")
            {
                for (int i0 = 0; i0 <= _fixture.Layout.NX; i0 += 4)
                {
                    int a = i0, b = i0 + 3;
                    yield return ApplyWithFixtureTool(t => _fixture.EmitRoads(t, s => s.Vertical && s.Line >= a && s.Line <= b), $"roads x{a}-{b}");
                }
                if (roads != "vertical")
                    for (int j0 = 0; j0 <= _fixture.Layout.NZ; j0 += 3)
                    {
                        int a = j0, b = j0 + 2;
                        yield return ApplyWithFixtureTool(t => _fixture.EmitRoads(t, s => !s.Vertical && s.Line >= a && s.Line <= b), $"roads z{a}-{b}");
                    }
            }
            yield return Frames(30);
            // (area tools) the plots' extra roads and paths, onto the ring roads now in
            var extra = _fixture.Layout.Plots.SelectMany(p => p.NetSegments()).ToList();
            if (extra.Count > 0) { yield return ApplyWithFixtureTool(t => _fixture.EmitNets(t, extra), "plot nets"); yield return Frames(30); }
            _tracing = false;
            yield return Settle("after roads");
            int edges = _fixture.CountEdges(out int temps);
            fx["roadEdges"] = edges;
            fx["roadTemps"] = temps;
            Mark("roadsDoneSec");

            yield return Frames(30);
            yield return ApplyWithFixtureTool(t => _fixture.EmitZoning(t), "zoning");
            yield return Frames(40);
            fx["zonedCells"] = _fixture.CountZonedCells();
            fx["zoning"] = SuiteFixture.ZonePrefab + " (low-rise housing) on the zoned row's south street";

            yield return ApplyWithFixtureTool(t => _fixture.EmitBuildings(t), "plops");
            yield return Frames(20);
            fx["buildingsAfterPlop"] = _fixture.CountAllBuildings();
            Mark("plopsDoneSec");
            yield return FillWater(_fixture.Layout.Plots);   // (rf-beach) the lakes beside land made to order

            if (pregrow && _cfg.pregrowSeconds > 0 && (int)fx["zonedCells"] > 0) yield return Pregrow();
            else fx["pregrow"] = "skipped: no scenario asks for a zoned block";
            fx["fixtureSec"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            Mark("fixtureDoneSec");
            fx["log"] = new JArray(_fixture.Log);
        }

        private IEnumerator Pregrow()
        {
            if (_pregrown) yield break;
            _pregrown = true;
            yield return Grow(_cfg.pregrowSeconds, "pregrow", true, 3f);
            if (_summary["fixture"] is JObject fx) fx["buildingsAfterPregrow"] = _fixture.CountAllBuildings();
        }

        /// <summary>Waits until frames are quick again (other mods index new prefabs, textures stream in after a load).</summary>
        private IEnumerator Settle(string what, float maxMs = 120f, int count = 30, float timeout = 240f)
        {
            float t0 = Time.realtimeSinceStartup;
            int ok = 0;
            float worst = 0f;
            while (ok < count && Time.realtimeSinceStartup - t0 < timeout)
            {
                yield return null;
                worst = math.max(worst, _lastFrameMs);
                if (_lastFrameMs < maxMs) ok++; else ok = 0;
            }
            float sec = Time.realtimeSinceStartup - t0;
            var settle = _timeline["settle"] as JObject ?? new JObject();
            settle[what] = Math.Round(sec, 1);
            _timeline["settle"] = settle;
            Say($"settle {what}: {sec:F1} s (worst frame {worst:F0} ms){(ok < count ? " — still slow" : "")}");
        }

        /// <summary>
        /// Grows the city: fast spawn at the configured speed (or plain speed 1 with real demand), until the building count
        /// has not changed for <paramref name="stableSec"/> s, at most <paramref name="seconds"/>. Notes it when the
        /// simulation did not run (fewer sim frames than the minimum).
        /// </summary>
        private IEnumerator Grow(float seconds, string what, bool fast = true, float stableSec = 8f)
        {
            var sim = _world.GetOrCreateSystemManaged<SimulationSystem>();
            var spawn = _world.GetOrCreateSystemManaged<ZoneSpawnSystem>();
            int start = _fixture.CountAllBuildings();
            spawn.debugFastSpawn = fast;
            sim.selectedSpeed = fast ? _cfg.speed : 1f;
            float t0 = Time.realtimeSinceStartup, lastChange = t0;
            int last = start;
            uint f0 = sim.frameIndex;
            int polls = 0;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return Seconds(0.5f);
                int n = _fixture.CountAllBuildings();
                if (polls++ % 10 == 0) Say($"{what}: sim frame {sim.frameIndex} (+{sim.frameIndex - f0}), speed {sim.selectedSpeed}/{sim.smoothSpeed:F2}, buildings {n}");
                if (n != last) { last = n; lastChange = Time.realtimeSinceStartup; }
                if (Time.realtimeSinceStartup - lastChange > stableSec && Time.realtimeSinceStartup - t0 > stableSec && sim.frameIndex - f0 >= (uint)Math.Max(0, _cfg.growMinSimFrames)) break;
            }
            sim.selectedSpeed = 0f;
            spawn.debugFastSpawn = false;
            yield return Frames(10);
            Event("grow");
            uint frames = sim.frameIndex - f0;
            var g = new JObject
            {
                ["seconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1), ["buildingsBefore"] = start, ["buildingsAfter"] = _fixture.CountAllBuildings(),
                ["simFrames"] = frames, ["speed"] = fast ? _cfg.speed : 1f, ["fastSpawn"] = fast, ["demand"] = fast ? "bypassed (debug fast spawn: every zone grows regardless of demand)" : "real",
            };
            if (frames < (uint)Math.Max(0, _cfg.growMinSimFrames)) g["failed"] = $"the simulation advanced only {frames} frames (minimum {_cfg.growMinSimFrames})";
            if (_fixture.CountAllBuildings() == start) g["nothingGrew"] = true;
            _summary[what] = g;
            Say($"{what}: {g.ToString(Formatting.None)}");
        }

        // ───────────── scenario sets ─────────────

        private ScenarioSet LoadSet()
        {
            if (string.IsNullOrEmpty(_cfg.scenarioFile) || !File.Exists(_cfg.scenarioFile)) throw new Exception("scenario file missing: " + _cfg.scenarioFile);
            var doc = JObject.Parse(File.ReadAllText(_cfg.scenarioFile));
            CheckSetSchema(doc);
            return doc.ToObject<ScenarioSet>();
        }

        private void CheckSetSchema(JObject doc)
        {
            var bad = new JArray();
            foreach (var sc in (doc["scenarios"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string e = Schema.CheckScenario(sc);
                if (e != null) bad.Add((string)sc["id"] + ": " + e);
                foreach (var step in (sc["steps"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    e = Schema.CheckStep(step);
                    if (e != null) bad.Add((string)sc["id"] + ": " + e);
                }
            }
            if (bad.Count > 0) { _summary["schemaErrors"] = bad; Say("schema errors: " + bad.ToString(Formatting.None)); }
        }

        private List<Scenario> Expand(ScenarioSet set)
        {
            var r = new List<Scenario>();
            var catalog = new JArray();
            var shelf = Placemaker.Game.Recipes.RecipeStore.EnsureShelf();
            foreach (var g in GeneratorRegistry.All)
            {
                if (GeneratorRegistry.IsHidden(g)) continue;
                var grp = GeneratorGroups.Of(g);
                if (string.IsNullOrEmpty(grp)) continue;
                var options = new JArray();
                try
                {
                    foreach (var c in g.Controls ?? new List<ControlSchema>())
                    {
                        var o = new JObject { ["id"] = c.Id, ["kind"] = c.Kind.ToString() };
                        if (c.Kind == ControlKind.Slider) { o["min"] = c.Min; o["max"] = c.Max; o["step"] = c.Step; if (!string.IsNullOrEmpty(c.Unit)) o["unit"] = c.Unit; }
                        if (c.Options != null && c.Options.Count > 0) o["options"] = new JArray(c.Options.Select(x => x.Value));
                        if (c.Default != null) { try { o["default"] = JToken.FromObject(c.Default); } catch (Exception) { } }
                        options.Add(o);
                    }
                }
                catch (Exception) { }
                // (core) The central shelf (Recipes/shelf/*.json): the type's Make-row category, and what the picker hides. Hidden types
                // and styles can still be built here by id; "expand" (types, presets) leaves them out.
                var entry = new JObject { ["layer"] = g.Id, ["group"] = grp, ["category"] = shelf.CategoryOf(g.Id, grp), ["presets"] = new JArray(PresetIds(g)), ["options"] = options };
                if (shelf.IsTypeShelved(g.Id, grp)) entry["shelved"] = true;
                var hiddenStyles = PresetIds(g).Where(p => shelf.IsStyleShelved(g.Id, grp, p)).ToList();
                if (hiddenStyles.Count > 0) entry["shelvedStyles"] = new JArray(hiddenStyles);
                catalog.Add(entry);
            }
            _summary["catalogFile"] = "catalog.json";
            var categories = new JArray();
            foreach (var c in shelf.Categories)
            {
                var co = new JObject { ["id"] = c.Id, ["name"] = c.Name, ["order"] = c.Order, ["groups"] = new JArray(c.Groups) };
                if (c.Hidden) co["hidden"] = true;
                if (c.Types.Count > 0) co["types"] = new JArray(c.Types);
                categories.Add(co);
            }
            try { File.WriteAllText(Path.Combine(_out, "catalog.json"), new JObject { ["generatedBy"] = "the in-game suite from GeneratorRegistry", ["assembly"] = AssemblyVersion(), ["categories"] = categories, ["types"] = catalog }.ToString(Formatting.Indented)); } catch (Exception) { }
            foreach (var spec in set.scenarios)
            {
                string landError = null;
                Scenario Make(string id, string layer, string preset) => new Scenario
                {
                    Id = id, Layer = layer ?? "", Preset = preset ?? "", Site = spec.site ?? "any", BlockId = spec.block, Streets = spec.streets, SiteFallback = spec.siteFallback,
                    Draw = spec.draw, Values = spec.values ?? new List<ValueOverride>(), Seed = spec.seed, Note = spec.note ?? "", Steps = spec.steps, On = spec.on,
                    Pace = spec.pace, BuildWhen = spec.buildWhen, PreviewMode = spec.previewMode,
                    // every scenario with land gets its own plot
                    Plot = spec.land != null ? SuiteLand.Parse(spec.land, out landError) : null, LandError = landError,
                };
                if (spec.steps != null && string.IsNullOrEmpty(spec.expand))
                {
                    string sid = spec.id ?? ("script" + (r.Count + 1));
                    while (r.Any(x => x.Id == sid) || _byId.ContainsKey(sid)) sid += "+";
                    r.Add(Make(sid, spec.layer, spec.preset));
                    continue;
                }
                var items = new List<(string Layer, string Preset)>();
                switch ((spec.expand ?? "").ToLowerInvariant())
                {
                    case "presets":
                        // limit: the first N styles of EACH type
                        foreach (var g in Generators(spec))
                        {
                            var grpOf = GeneratorGroups.Of(g);
                            var ids = PresetIds(g).Where(p => (spec.skip == null || !spec.skip.Contains(p)) && !shelf.IsStyleShelved(g.Id, grpOf, p)).ToList();
                            if (spec.limit > 0) ids = ids.Take(spec.limit).ToList();
                            foreach (var p in ids) items.Add((g.Id, p));
                        }
                        break;
                    case "types":
                        foreach (var g in Generators(spec)) items.Add((g.Id, spec.preset ?? ""));
                        break;
                    default:
                        items.Add((spec.layer, spec.preset ?? ""));
                        break;
                }
                if (spec.limit > 0 && (spec.expand ?? "") != "presets" && items.Count > spec.limit) items = items.Take(spec.limit).ToList();
                foreach (var (layer, preset) in items)
                {
                    if (string.IsNullOrEmpty(layer)) continue;
                    string id = spec.id != null && items.Count == 1 ? spec.id : layer + "-" + (string.IsNullOrEmpty(preset) ? "default" : preset);
                    if (spec.draw != null && spec.id == null) id += "-small";
                    while (r.Any(x => x.Id == id) || _byId.ContainsKey(id)) id += "+";
                    var sc = Make(id, layer, preset);
                    if (spec.steps != null)
                    {
                        // a template: "type" without a layer takes the expanded type and preset
                        sc.Steps = spec.steps.Select(x => (JObject)x.DeepClone()).ToList();
                        foreach (var st in sc.Steps.Where(x => (string)x["do"] == "type" && x["layer"] == null)) { st["layer"] = layer; if (st["preset"] == null) st["preset"] = preset; }
                    }
                    r.Add(sc);
                }
            }
            return r;
        }

        private static IEnumerable<IAreaGenerator> Generators(ScenarioSpec spec)
        {
            foreach (var g in GeneratorRegistry.All)
            {
                if (GeneratorRegistry.IsHidden(g)) continue;
                var grp = GeneratorGroups.Of(g);
                if (!string.IsNullOrEmpty(spec.layer)) { if (g.Id == spec.layer) yield return g; continue; }
                if (!string.IsNullOrEmpty(spec.group)) { if (grp == spec.group) yield return g; continue; }   // a group named on purpose: hidden types too
                if (!string.IsNullOrEmpty(grp) && !Placemaker.Core.Tool.Shelf.Current.IsTypeShelved(g.Id, grp)) yield return g;   // "types": every type offered in the Make row (the shelf's hidden ones left out)
            }
        }

        private static List<string> PresetIds(IAreaGenerator g)
        {
            var r = new List<string>();
            try { if (g.Presets != null) foreach (var p in g.Presets) if (p != null && !string.IsNullOrEmpty(p.Id) && !r.Contains(p.Id)) r.Add(p.Id); }
            catch (Exception) { }
            return r;
        }

        // ───────────── the tool ─────────────

        private IEnumerator OpenTool()
        {
            if (!_tool.IsActive) _tool.Toggle();
            yield return Until(() => _tool.IsActive, 10f, "tool open");
            yield return Frames(3);
        }

        private IEnumerator CloseTool()
        {
            if (_tool.IsActive) _tool.Close();
            yield return Until(() => !_tool.IsActive, 5f, "tool closed");
            if (_tool.IsActive) _tool.Close();
            yield return Frames(5);
        }

        private static string LayerAliasesCanonical(string layer)
        {
            try { return Placemaker.Core.Layers.Common.LayerAliases.Canonical(layer); } catch (Exception) { return layer; }
        }
    }
}
