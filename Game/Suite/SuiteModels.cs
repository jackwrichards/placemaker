// Game runner: run configuration (written by tools/suite/Run-Suite.ps1), scenario sets (suite/scenarios/*.json) and
// the scenario list the runner expands from them. What happened is written as JObjects (SuiteRunner).

using System;
using System.Collections.Generic;

namespace Placemaker.Game.Suite
{
    public sealed class SuiteRunConfig
    {
        public string runId = "";
        public string set = "smoke";
        /// <summary>Absolute folder the runner writes heartbeat, results and PNGs into.</summary>
        public string outDir = "";
        /// <summary>Absolute path of the scenario set JSON.</summary>
        public string scenarioFile = "";
        /// <summary>Absolute path of suite/schema.json (step and scenario keys); empty = no schema check in game.</summary>
        public string schemaFile = "";
        public string git = "";
        /// <summary>First 8 hex digits of the SHA-256 of the Placemaker.dll the host deployed (the runner compares its own).</summary>
        public string dllHash = "";
        public string expiresUtc = "";
        /// <summary>Simulation speed while growing (4 reaches the 8 steps/frame cap; 8 is no faster).</summary>
        public float speed = 4f;
        public float pregrowSeconds = 75f;
        /// <summary>Simulation frames a grow runs at least (fewer is noted); 0 = no minimum.</summary>
        public int growMinSimFrames = 1000;
        /// <summary>Only the first N scenarios (0 = all).</summary>
        public int maxScenarios;
        /// <summary>Stop after the fixture (bootstrap check).</summary>
        public bool fixtureOnly;
        public bool quit = true;
        /// <summary>Playground for the user: build the fixture, frame the grid, give the mouse and keys back, run nothing, never quit.</summary>
        public bool playground;
        /// <summary>Playground at the main menu: the game launched the same way, no new game, no grid; the controls are the user's.</summary>
        public bool menuOnly;
        /// <summary>Keep the map's terrain as it is (no heightmap replacement: no flat, slope or rough blocks).</summary>
        public bool noTerrain;
        /// <summary>"map:&lt;display name&gt;": the vanilla map the new game starts on (the fixture area is reshaped in place).</summary>
        public string world = "map:Plains";
        /// <summary>Road fixture: "single" (one apply; ends merge on exact positions), "batches" (vertical lines, then cross streets onto their nodes), "vertical", "none".</summary>
        public string roads = "single";
        /// <summary>Seconds to idle after the roads (diagnostics).</summary>
        public float idleAfterRoads;
        /// <summary>Log every frame while the roads apply (diagnostics).</summary>
        public bool trace;
        /// <summary>Window and shot size (both shot modes are this size; the runner sets the window to it and checks).</summary>
        public int shotWidth = 1600, shotHeight = 900;
        /// <summary>After the set: keep the game open and run scenario files dropped into &lt;run&gt;/inbox until {"command":"quit"}.</summary>
        public bool queue;
        public float queueIdleMinutes = 15f;
        /// <summary>Grid size in blocks (0 = sized for the set, at least the layout default).</summary>
        public int nx, nz;
        /// <summary>Block spacing: "checker" (scenarios on every other block, so neighbours do not hide or shade a target) or "dense".</summary>
        public string spacing = "checker";
        /// <summary>Keep the user's Placemaker settings (false = Placemaker's defaults are pinned for the run).</summary>
        public bool userModSettings;
        /// <summary>Readability look: "suite" (noon, fixed exposure, no weather or wind, no icons) or "game" (as the map has it).</summary>
        public string look = "suite";
        /// <summary>Target mean brightness (0-255) the fixed exposure is calibrated to.</summary>
        public float lookLuma = 122f;
        /// <summary>Latitude for the sun (the Plains map is at -43.9: midwinter in June). 0 = keep the map's.</summary>
        public float lookLatitude = 45f;
        public float lookTime = 12f;
        public int lookDay = 172;
        /// <summary>Default pace of scenarios: "human" (cursor glides, clicks at least 0.4 s apart), "settled" (no glide), "user" (no readiness waits).</summary>
        public string pace = "human";
        /// <summary>Shot settle gate timeout (s); a shot that does not settle by then is taken anyway and marked.</summary>
        public float settleTimeout = 10f;
        /// <summary>Profile the game runs with: "clean" (only Placemaker) or "full" (the user's playset).</summary>
        public string profile = "clean";
    }

    public sealed class ValueOverride
    {
        public string path = "0";
        public string id = "";
        public string json = "";
    }

    public sealed class ScenarioSpec
    {
        public string id;
        /// <summary>Type (generator) id; with expand = "presets" every built-in preset of it.</summary>
        public string layer;
        /// <summary>Preset id; "" or null = the type's defaults.</summary>
        public string preset;
        /// <summary>"presets" (all presets of layer or group), "types" (every visible type, default preset).</summary>
        public string expand;
        /// <summary>Make-row group for expand ("commercial", "parks"...).</summary>
        public string group;
        /// <summary>Block kind: flat, zoned, slope, rough, survive, any. Never silently replaced by another kind.</summary>
        public string site = "any";
        /// <summary>A given block id (e.g. "B3-2") instead of the next free block of the kind.</summary>
        public string block;
        /// <summary>Street signature the block must have, e.g. {"south":"Medium Road","north":"Small Road"}.</summary>
        public Dictionary<string, string> streets;
        /// <summary>Allow another block kind when the wanted one has run out (recorded in the scenario's block note).</summary>
        public bool siteFallback;
        /// <summary>Draw a rectangle of this size (m) at the block centre instead of picking the whole block.</summary>
        public float[] draw;
        public List<ValueOverride> values;
        public uint seed = 1;
        public string note;
        /// <summary>Skip these preset ids when expanding.</summary>
        public List<string> skip;
        /// <summary>Limit an expansion to the first N items (0 = all).</summary>
        public int limit;
        /// <summary>A step script (suite/README.md); without one the default steps run for layer/preset/draw/values.</summary>
        public List<Newtonsoft.Json.Linq.JObject> steps;
        /// <summary>Run on an earlier scenario's block, outline, build and entrances (its id) instead of a fresh block.</summary>
        public string on;
        /// <summary>Pace: human | settled | user (null = the run's default).</summary>
        public string pace;
        /// <summary>One-line scenarios: when the build is pressed: settled (ghost shown) | planReady | ghostPending; default settled.</summary>
        public string buildWhen;
        /// <summary>One-line scenarios: the preview mode set in Review before building (sketch | ghost).</summary>
        public string previewMode;
        /// <summary>Test land made to order for this scenario (shape, size, ground, roads; suite/schema.json "land"), instead of a grid block.</summary>
        public Newtonsoft.Json.Linq.JObject land;
    }

    public sealed class ScenarioSet
    {
        public string set = "";
        public string description = "";
        public List<ScenarioSpec> scenarios = new List<ScenarioSpec>();
        /// <summary>Pin Placemaker options for the run (property name = value), applied through the settings object.</summary>
        public Dictionary<string, object> settings;
    }

    /// <summary>One concrete scenario after expansion.</summary>
    public sealed class Scenario
    {
        public string Id = "";
        public string Layer = "";
        public string Preset = "";
        public string Site = "any";
        public string BlockId;
        public Dictionary<string, string> Streets;
        public bool SiteFallback;
        public float[] Draw;
        public List<ValueOverride> Values = new List<ValueOverride>();
        public uint Seed = 1;
        public string Note = "";
        /// <summary>The step script; null = the default steps for Layer/Preset/Draw/Values.</summary>
        public List<Newtonsoft.Json.Linq.JObject> Steps;
        public string On;
        public string Pace;
        public string BuildWhen;
        public string PreviewMode;
        public Placemaker.Core.Suite.SuiteBlock Block;
        /// <summary>Its own test land (from "land"), laid out at launch; null = a grid block.</summary>
        public Placemaker.Core.Suite.SuitePlot Plot;
        public string LandError;
        /// <summary>What the block assignment did: requested kind, the kind it got, and why.</summary>
        public string SiteNote;
    }
}
