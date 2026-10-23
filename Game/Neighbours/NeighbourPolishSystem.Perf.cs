// Placemaker neighbourhood polish — what it costs (GPL-3.0).
//
// Times every part: the system's whole frame (UpdateMs), a pass gathering its houses on the frame (PassMainMs), the plan
// on its worker (PlanWorkerMs) and writing a finished plan into the houses (FinishMainMs), with the worst of each. The game
// runner's lab ops polishIdle and polishStats report them. Lab op "polishPassDump" writes every pass's input to a file
// (Core StitchDump) for the offline benchmark (Tests OutgrowBench).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Placemaker.Core.Neighbours;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        /// <summary>Main thread time (ms) since the game started, and the worst single one.</summary>
        public static double UpdateMs, PassMainMs, FinishMainMs, PlanWorkerMs;
        public static double UpdateMaxMs, PassMainMaxMs, FinishMainMaxMs, PlanWorkerMaxMs;
        public static int PlansTimed;
        /// <summary>Why houses were put in line to be planned (since the game started): 0 a fence line the game changed, 1 a
        /// building gone nearby, 2 something built nearby, 3 a garden over a new lot, 4 a stamp (Polish), 5 the game rebuilt
        /// the building (built, levelled up: another model), 6 the game took away surfaces of its own, 7 found empty after a load (NeighbourPolishSystem.Rebuilt),
        /// 8 its outgrow type saved with new settings (or put back as it ships).</summary>
        public static readonly int[] DirtyBy = new int[9];
        public static readonly string[] DirtyByNames = { "linesChanged", "goneNearby", "builtNearby", "clash", "stamped", "rebuilt", "piecesTaken", "emptyAfterLoad", "typeSaved" };
        /// <summary>Fence lanes the game changed that put their house in line again, by how: 0 a lane not seen before (made
        /// again), 1 one of ours the game moved, 2 one of the game's own it moved, 3 where it was but on a building that
        /// changed (another model).</summary>
        public static readonly int[] LinesChangedHow = new int[4];
        /// <summary>Of those, lanes whose building the game marked Updated that frame (it then fits every lane of the building
        /// again); and polished buildings the game marked Updated at all (since the game started).</summary>
        public static int LinesOwnerUpdated, PolishedUpdated;
        public static int DirtySkippedTotal => DirtySkipped;
        /// <summary>A pass's main thread time by part (ms since the game started): 0 grid, 1 picking the patch, 2 describing
        /// houses, 3 looks, gardens and fences of the neighbours (with 2: describing counted apart), 4 barriers, 5 starting the
        /// ground and water jobs, 6 taking their samples and starting the plan.</summary>
        public static readonly double[] PassPartMs = new double[7];
        public static readonly string[] PassPartNames = { "grid", "pick", "describe", "looks", "barriers", "sampling", "start" };
        private double _passPartAt;
        /// <summary>Gathering barriers by part (ms since the game started): roads, buildings, lot areas and builds, water.</summary>
        public static readonly double[] BarrierPartMs = new double[4];
        public static readonly string[] BarrierPartNames = { "roads", "buildings", "lotsAndBuilds", "water" };

        /// <summary>The time since the last part (the pass's clock), added to part k; "looks" (3) leaves out describing.</summary>
        private void PassPart(int k, System.Diagnostics.Stopwatch sw)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            if (k == 0) _passPartAt = 0;
            double ms = now - _passPartAt;
            // describing is timed on its own (part 2) inside the looks loop: left out of part 3
            if (k == 3) ms -= PassPartMs[2] - _describeBefore;
            PassPartMs[k] += ms;
            _passPartAt = now;
            if (k == 1) _describeBefore = PassPartMs[2];
        }
        private double _describeBefore;

        private static void NoteMain(double ms, ref double total, ref double max)
        {
            total += ms;
            if (ms > max) max = ms;
        }

        /// <summary>Main thread time (ms since the game started) of each Outgrow system: the polish itself, its lawn writes, ground
        /// paint, surfaces, piece deletes, objects and hidden pieces shown again (the game runner's outgrowFrames reads them
        /// every frame).</summary>
        public static readonly double[] SystemMs = new double[7];
        public static readonly string[] SystemNames = { "polish", "lawns", "paint", "surfaces", "deletes", "objects", "show" };
        /// <summary>The polish's own update by part (ms since the game started).</summary>
        public static readonly double[] UpdatePartMs = new double[9];
        public static readonly string[] UpdatePartNames = { "switches", "barrierCells", "lots", "collect", "heal", "startPlans", "finish", "pass", "report" };
        private long _partAt;

        public static void NoteSystem(int k, long since) => SystemMs[k] += (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

        /// <summary>The time since the last part of this update, added to part k.</summary>
        private void UpdatePart(int k)
        {
            long now = Stopwatch.GetTimestamp();
            UpdatePartMs[k] += (now - _partAt) * 1000.0 / Stopwatch.Frequency;
            _partAt = now;
        }

        /// <summary>The worst times back to zero (the game runner measures one stretch).</summary>
        public static void ResetWorst() => UpdateMaxMs = PassMainMaxMs = FinishMainMaxMs = PlanWorkerMaxMs = 0;

        /// <summary>Game runner: write each pass's input here (up to PassDumpMax files); null: off.</summary>
        public static string PassDumpDir;
        public static int PassDumpMax = 40;
        private static int s_passDumps;
        // (only the passes editing a house within PassDumpRadius of one of these spots; null: every pass)
        private static List<Unity.Mathematics.float2> s_passDumpNear;
        private static float s_passDumpRadius;

        public static void StartPassDump(string dir, int max, List<Unity.Mathematics.float2> near = null, float radius = 60f)
        {
            PassDumpDir = dir;
            PassDumpMax = max;
            s_passDumpNear = near;
            s_passDumpRadius = radius;
            s_passDumps = 0;
            Directory.CreateDirectory(dir);
        }

        private static bool DumpWanted(List<StitchHouse> stitch)
        {
            if (s_passDumpNear == null) return true;
            foreach (var h in stitch)
                if (h.Edit)
                    foreach (var p in s_passDumpNear)
                        if (Unity.Mathematics.math.distance(h.Position, p) <= s_passDumpRadius) return true;
            return false;
        }

        private void DumpPass(List<StitchHouse> stitch, StitchSettings settings, HeightRaster raster, int number)
        {
            try
            {
                var path = Path.Combine(PassDumpDir, $"pass-{number:000}.json");
                File.WriteAllText(path, StitchDump.Write(stitch, settings, raster));
                Mod.Log.Info($"Placemaker neighbourhood polish: pass input written to {path} ({stitch.Count} houses, {settings.Barriers?.Count ?? 0} barriers)");
            }
            catch (Exception e) { Mod.Log.Warn("Placemaker neighbourhood polish: pass dump failed: " + e.Message); PassDumpDir = null; }
        }
    }
}
