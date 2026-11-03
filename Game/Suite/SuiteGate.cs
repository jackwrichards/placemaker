// In-game test suite gate (docs/teardown/10-ingame-suite.md §4.1). The suite exists only when BOTH hold:
//   * the command line (or the one-shot runOnce.txt the game merges into it) carries -placemakerSuite=<runId>, and
//   * ModsData/Placemaker/suite/run.json is armed for exactly that run id and has not expired.
// Otherwise nothing is registered: no system, no GameObject, no file access beyond one string scan of the command line.
// On activation run.json is renamed to run.active.json so a crash can never leave it armed for the user's next launch.

using System;
using System.IO;
using Colossal.PSI.Environment;
using Game;
using Game.SceneFlow;
using Newtonsoft.Json;

namespace Placemaker.Game.Suite
{
    public static class SuiteGate
    {
        public const string TokenName = "placemakerSuite=";

        public static bool Active { get; private set; }
        public static SuiteRunConfig Config { get; private set; }

        public static string SuiteFolder => Path.Combine(EnvPath.kUserDataPath, "ModsData", "Placemaker", "suite");

        public static void Register(UpdateSystem updateSystem)
        {
            string id = TokenValue();
            if (id == null) return;   // the normal game
            var armed = Path.Combine(SuiteFolder, "run.json");
            if (!File.Exists(armed)) { Mod.Log.Warn("Placemaker suite: token " + id + " but no armed run.json: staying inert"); return; }
            SuiteRunConfig cfg;
            try { cfg = JsonConvert.DeserializeObject<SuiteRunConfig>(File.ReadAllText(armed)); }
            catch (Exception e) { Mod.Log.Warn("Placemaker suite: run.json unreadable: " + e.Message); return; }
            if (cfg == null || !string.Equals(cfg.runId, id, StringComparison.Ordinal))
            {
                Mod.Log.Warn("Placemaker suite: run.json is for another run (" + cfg?.runId + "), token " + id + ": staying inert");
                return;
            }
            if (DateTime.TryParse(cfg.expiresUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var exp)
                && exp < DateTime.UtcNow)
            {
                Mod.Log.Warn("Placemaker suite: run.json expired at " + cfg.expiresUtc + ": staying inert");
                return;
            }
            try
            {
                var active = Path.Combine(SuiteFolder, "run.active.json");
                if (File.Exists(active)) File.Delete(active);
                File.Move(armed, active);
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker suite: could not disarm run.json: " + e.Message); return; }

            Active = true;
            Config = cfg;
            Directory.CreateDirectory(cfg.outDir);
            updateSystem.UpdateAt<SuiteFixtureToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<SuiteUiSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateBefore<SuiteLabShapeSystem, global::Game.Prefabs.PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            UnityEngine.Application.runInBackground = true;
            var go = new UnityEngine.GameObject("Placemaker Suite Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SuiteRunner>();
            Mod.Log.Info("Placemaker suite: ACTIVE for run " + cfg.runId + " (set " + cfg.set + ", out " + cfg.outDir + ")");
        }

        private static string TokenValue()
        {
            string[] args = null;
            try { args = GameManager.instance?.cmdLine; } catch (Exception) { }
            if (args == null) return null;
            foreach (var raw in args)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                int at = raw.IndexOf(TokenName, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                var v = raw.Substring(at + TokenName.Length).Trim().Trim('"');
                if (v.Length > 0) return v;
            }
            return null;
        }
    }
}
