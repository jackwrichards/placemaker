// Game runner lab: where the frames go while a city runs with Outgrow (the user, 22 Oct: stutters in a city of 15 000 with
// every building outgrown, gone the moment Outgrow is paused):
//   {"do":"lab","op":"outgrowFrames","seconds":60,"speed":1,"outgrow":true,"levelUps":false}
//       the simulation at that speed for that long (outgrow true or false: the city's Outgrow switched on or paused first;
//       levelUps: "Outgrow again when a building levels up" set first);
//       every frame's time with what Outgrow did around it (each of its systems' main thread time, the polish's parts, plans
//       written, pieces made and deleted, lawns written) and whether the garbage collector ran; the worst frames listed with
//       all of that for the frame before, the frame and the frame after (a frame's work and its time are counted one frame
//       apart, depending on where the step runs).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Neighbours;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private sealed class OutgrowCounters
        {
            public double[] Sys = (double[])NeighbourPolishSystem.SystemMs.Clone();
            public double[] Parts = (double[])NeighbourPolishSystem.UpdatePartMs.Clone();
            public int[] DirtyBy = (int[])NeighbourPolishSystem.DirtyBy.Clone();
            public int[] How = (int[])NeighbourPolishSystem.LinesChangedHow.Clone();
            public int OwnerUpdated = NeighbourPolishSystem.LinesOwnerUpdated, PolishedUpdated = NeighbourPolishSystem.PolishedUpdated, LevelUpsKept = NeighbourPolishSystem.TotalLevelUpsKept, Carried = NeighbourPolishSystem.TotalFencesCarried,
                Hidden = NeighbourPolishSystem.TotalFencesHidden, Laid = NeighbourPolishSystem.TotalFencesLaid,
                SurfacesKept = NeighbourPolishSystem.TotalSurfacesKept, LawnsKept = NeighbourPolishSystem.TotalLawnsKept, SurfacePieces = NeighbourPolishSystem.TotalSurfacePiecesKept,
                LostAreas = NeighbourPolishSystem.TotalLostAreas;
            public int Gc = GC.CollectionCount(0);
            public int Passes = NeighbourPolishSystem.TotalPasses;
            public int Objects = NeighbourObjectSystem.TotalMade;
            public int Surfaces = NeighbourAreaSystem.TotalMade;
            public int Deleted = NeighbourDeleteSystem.TotalDeleted;
            public int Lawns = NeighbourLawnSystem.TotalWritten;
            public int Shown = NeighbourPolishSystem.TotalShownAgain;
            public long Heap = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        }

        private sealed class OutgrowFrame
        {
            public float Ms;
            public double[] Sys, Parts;
            public int Gc, Passes, Objects, Surfaces, Deleted, Lawns, Shown;

            public double Outgrow => Sys.Sum();
        }

        private static OutgrowFrame FrameBetween(OutgrowCounters a, OutgrowCounters b, float ms) => new OutgrowFrame
        {
            Ms = ms,
            Sys = b.Sys.Select((v, k) => v - a.Sys[k]).ToArray(),
            Parts = b.Parts.Select((v, k) => v - a.Parts[k]).ToArray(),
            Gc = b.Gc - a.Gc, Passes = b.Passes - a.Passes, Objects = b.Objects - a.Objects, Surfaces = b.Surfaces - a.Surfaces,
            Deleted = b.Deleted - a.Deleted, Lawns = b.Lawns - a.Lawns, Shown = b.Shown - a.Shown,
        };

        private static JObject FrameJson(OutgrowFrame f)
        {
            var o = new JObject { ["ms"] = Math.Round(f.Ms, 1), ["outgrowMs"] = Math.Round(f.Outgrow, 2) };
            for (int k = 0; k < f.Sys.Length; k++) if (f.Sys[k] >= 0.05) o[NeighbourPolishSystem.SystemNames[k]] = Math.Round(f.Sys[k], 2);
            var parts = new JObject();
            for (int k = 0; k < f.Parts.Length; k++) if (f.Parts[k] >= 0.05) parts[NeighbourPolishSystem.UpdatePartNames[k]] = Math.Round(f.Parts[k], 2);
            if (parts.Count > 0) o["parts"] = parts;
            if (f.Gc > 0) o["gc"] = f.Gc;
            if (f.Passes > 0) o["plansWritten"] = f.Passes;
            if (f.Objects > 0) o["objectsMade"] = f.Objects;
            if (f.Surfaces > 0) o["surfacesMade"] = f.Surfaces;
            if (f.Deleted > 0) o["deleted"] = f.Deleted;
            if (f.Lawns > 0) o["lawnsWritten"] = f.Lawns;
            if (f.Shown > 0) o["shownAgain"] = f.Shown;
            return o;
        }

        private IEnumerator LabOutgrowFrames(ScenarioState st, JObject step, JObject rec)
        {
            float seconds = (float?)step["seconds"] ?? 60f;
            float speed = (float?)step["speed"] ?? 1f;
            if (step["outgrow"] != null) NeighbourPolishSystem.SetRunning((bool)step["outgrow"]);
            if (step["levelUps"] != null) NeighbourPolishSystem.SetLevelUps((bool)step["levelUps"]);
            var sim = _world.GetOrCreateSystemManaged<global::Game.Simulation.SimulationSystem>();
            float speedWas = sim.selectedSpeed;
            sim.selectedSpeed = speed;
            uint simFrame0 = sim.frameIndex;
            yield return null;
            yield return null;
            var frames = new List<OutgrowFrame>();
            var start = new OutgrowCounters();
            int keptSpots0 = NeighbourPolishSystem.KeptLevelUpSpots.Count;
            var prev = start;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                yield return null;
                var now = new OutgrowCounters();
                frames.Add(FrameBetween(prev, now, Time.unscaledDeltaTime * 1000f));
                prev = now;
            }
            sim.selectedSpeed = speedWas;
            var all = FrameBetween(start, prev, (float)clock.Elapsed.TotalMilliseconds);
            var ms = frames.Select(f => f.Ms).OrderBy(x => x).ToList();
            float P(float q) => ms.Count == 0 ? 0f : ms[Math.Min(ms.Count - 1, (int)(ms.Count * q))];
            rec["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1);
            rec["levelUps"] = NeighbourPolishSystem.PolishLevelUps;
            rec["outgrow"] = !NeighbourPolishSystem.Paused;
            rec["simFrames"] = sim.frameIndex - simFrame0;
            rec["frames"] = frames.Count;
            rec["frameMs"] = new JObject
            {
                ["median"] = Math.Round(P(0.5f), 1), ["p95"] = Math.Round(P(0.95f), 1), ["p99"] = Math.Round(P(0.99f), 1), ["worst"] = Math.Round(ms.Count > 0 ? ms[ms.Count - 1] : 0f, 1),
                ["over33"] = ms.Count(x => x > 33.4f), ["over50"] = ms.Count(x => x > 50f), ["over100"] = ms.Count(x => x > 100f),
            };
            // what Outgrow did in all, and a frame on average
            var total = FrameJson(all);
            total.Remove("ms");
            rec["outgrowInAll"] = total;
            rec["outgrowMsPerFrame"] = frames.Count > 0 ? Math.Round(all.Outgrow / frames.Count, 2) : 0;
            var why = new JObject();
            for (int k = 0; k < NeighbourPolishSystem.DirtyBy.Length; k++)
                if (prev.DirtyBy[k] != start.DirtyBy[k]) why[NeighbourPolishSystem.DirtyByNames[k]] = prev.DirtyBy[k] - start.DirtyBy[k];
            // fence lanes the game changed: how, and whether their building was marked Updated (every lane fitted again)
            why["linesHow"] = $"new {prev.How[0] - start.How[0]}, ours moved {prev.How[1] - start.How[1]}, game's moved {prev.How[2] - start.How[2]}, other model {prev.How[3] - start.How[3]}; on a building marked Updated {prev.OwnerUpdated - start.OwnerUpdated}";
            why["polishedBuildingsUpdated"] = prev.PolishedUpdated - start.PolishedUpdated;
            why["levelUpsKept"] = prev.LevelUpsKept - start.LevelUpsKept;
            why["fencesCarried"] = prev.Carried - start.Carried;
            why["newFencesHidden"] = prev.Hidden - start.Hidden;
            why["oldFencePiecesLaid"] = prev.Laid - start.Laid;
            why["surfacesLost"] = prev.LostAreas - start.LostAreas;
            why["surfacesKept"] = prev.SurfacesKept - start.SurfacesKept;
            why["ownLawnsKept"] = prev.LawnsKept - start.LawnsKept;
            why["surfacePiecesMade"] = prev.SurfacePieces - start.SurfacePieces;
            rec["putInLine"] = why;
            // the garbage collector: how often, and how long the frames it ran in (or next to) took
            var gcFrames = new List<int>();
            for (int i = 0; i < frames.Count; i++) if (frames[i].Gc > 0) gcFrames.Add(i);
            float GcFrameMs(int i) => Math.Max(frames[i].Ms, i + 1 < frames.Count ? frames[i + 1].Ms : 0f);
            rec["gc"] = new JObject
            {
                ["collections"] = all.Gc, ["frames"] = gcFrames.Count,
                ["frameMsMean"] = gcFrames.Count > 0 ? Math.Round(gcFrames.Average(GcFrameMs), 1) : 0,
                ["frameMsWorst"] = gcFrames.Count > 0 ? Math.Round(gcFrames.Max(GcFrameMs), 1) : 0,
                ["heapMB"] = Math.Round(prev.Heap / 1048576.0, 0), ["heapGrowthMB"] = Math.Round((prev.Heap - start.Heap) / 1048576.0, 0),
            };
            // the worst frames, each with the one before and after
            var worst = new JArray();
            foreach (int i in Enumerable.Range(0, frames.Count).OrderByDescending(i => frames[i].Ms).Take((int?)step["worst"] ?? 12).OrderBy(i => i))
            {
                var o = new JObject { ["frame"] = i, ["this"] = FrameJson(frames[i]) };
                if (i > 0) o["before"] = FrameJson(frames[i - 1]);
                if (i + 1 < frames.Count) o["after"] = FrameJson(frames[i + 1]);
                worst.Add(o);
            }
            rec["worstFrames"] = worst;
            Mod.Log.Info("Placemaker lab: outgrow frames " + new JObject { ["outgrow"] = rec["outgrow"], ["levelUps"] = rec["levelUps"], ["seconds"] = rec["seconds"], ["simFrames"] = rec["simFrames"], ["frameMs"] = rec["frameMs"], ["outgrowMsPerFrame"] = rec["outgrowMsPerFrame"], ["outgrowInAll"] = rec["outgrowInAll"], ["putInLine"] = rec["putInLine"], ["gc"] = rec["gc"] }.ToString(Newtonsoft.Json.Formatting.None));
            foreach (var w in worst) Mod.Log.Info("Placemaker lab: outgrow frames, a worst frame " + w.ToString(Newtonsoft.Json.Formatting.None));
            // "shots": close-ups of that many buildings that levelled up and kept their gardens in this stretch
            int shots = (int?)step["shots"] ?? 0;
            var spots = NeighbourPolishSystem.KeptLevelUpSpots.Skip(keptSpots0).Take(shots).ToList();
            for (int i = 0; i < spots.Count; i++)
            {
                var sr = new JObject { ["at"] = new JArray(Math.Round(spots[i].x, 1), Math.Round(spots[i].y, 1)) };
                var cam = new JObject { ["target"] = "point", ["at"] = new JArray(spots[i].x, spots[i].y), ["abs"] = true, ["view"] = "oblique", ["pitch"] = 60, ["distance"] = 65 };
                yield return StepShot(st, new JObject { ["camera"] = cam, ["shotMode"] = "render" }, sr, $"levelup-kept-{i + 1}");
            }
        }
    }
}
