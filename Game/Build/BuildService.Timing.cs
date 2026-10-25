// (perf) Where a build's main-thread time goes: our own work per frame (the state machine's step), summed per step, and the
// worst frames. One line when the build ends, so the game runner and the player's log show which step froze the game
// (teardown 14: no build step logged a time before; the freezes were found from the gaps between log lines).

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Placemaker.Game.Build
{
    public sealed partial class BuildService
    {
        private sealed class StepTime { public int Frames; public double Total, Max; }

        private readonly Dictionary<Step, StepTime> _stepTimes = new Dictionary<Step, StepTime>();
        private readonly List<(int frame, Step step, double ms)> _worstFrames = new List<(int, Step, double)>();
        private readonly Stopwatch _frameClock = new Stopwatch();
        private int _timingStartFrame, _timingGc0;
        private readonly Dictionary<string, double> _subBuild = new Dictionary<string, double>(), _subGhost = new Dictionary<string, double>();

        private static long Now => Stopwatch.GetTimestamp();

        /// <summary>Adds the time since <paramref name="t0"/> (Now) to the named part of this build's (or, idle, the ghost's) frames.</summary>
        private void Sub(string name, long t0)
        {
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            var d = _step != Step.Idle ? _subBuild : _subGhost;
            d[name] = (d.TryGetValue(name, out var v) ? v : 0) + ms;
        }

        private static void AppendSubs(StringBuilder sb, Dictionary<string, double> subs)
        {
            var list = new List<KeyValuePair<string, double>>(subs);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            sb.Append("; parts");
            int n = 0;
            foreach (var kv in list) { if (kv.Value < 1.0 || n++ >= 14) break; sb.Append(' ').Append(kv.Key).Append(' ').Append(F(kv.Value)).Append(','); }
            if (n == 0) sb.Append(" all under 1 ms"); else sb.Length--;
            subs.Clear();
        }

        // the ghost (preview temporaries): its frames from a new revision until it shows
        private int _ghostFrames, _ghostRevision = -1;
        private double _ghostTotal, _ghostWorst;

        private void GhostFrame(double ms)
        {
            if (_gEmitting != _ghostRevision) { _ghostRevision = _gEmitting; _ghostFrames = 0; _ghostTotal = _ghostWorst = 0; }
            if (ms <= 0.05) return;
            _ghostFrames++;
            _ghostTotal += ms;
            if (ms > _ghostWorst) _ghostWorst = ms;
        }

        private void GhostTimingLog()
        {
            var sb = new StringBuilder();
            sb.Append("Placemaker ghost timing r").Append(_gEmitting).Append(": ").Append(F(_ghostTotal)).Append(" ms of ours over ").Append(_ghostFrames)
              .Append(" frames, worst frame ").Append(F(_ghostWorst)).Append(" ms, ").Append(_work?.DefinitionCount ?? 0).Append(" definitions");
            AppendSubs(sb, _subGhost);
            Mod.Log.Info(sb.ToString());
            _ghostRevision = -1;
        }

        private void TimingBegin()
        {
            _stepTimes.Clear();
            _worstFrames.Clear();
            _timingStartFrame = UnityEngine.Time.frameCount;
            _timingGc0 = System.GC.CollectionCount(0);
        }

        /// <summary>One frame of the state machine: <paramref name="step"/> is the step that ran (before it moved on).</summary>
        private void TimingFrame(Step step, int frame, double ms)
        {
            if (!_stepTimes.TryGetValue(step, out var t)) _stepTimes[step] = t = new StepTime();
            t.Frames++;
            t.Total += ms;
            if (ms > t.Max) t.Max = ms;
            if (ms < 5.0) return;
            _worstFrames.Add((frame - _timingStartFrame, step, ms));
            _worstFrames.Sort((a, b) => b.ms.CompareTo(a.ms));
            if (_worstFrames.Count > 6) _worstFrames.RemoveAt(_worstFrames.Count - 1);
        }

        /// <summary>"Placemaker build timing: 412 ms of ours over 97 frames; worst frames 21 EmitNets 145 ms, ...; per step Prepare 3, ..."</summary>
        private void TimingLog(string outcome)
        {
            if (_stepTimes.Count == 0) { _subBuild.Clear(); return; }
            double total = 0; int frames = 0;
            foreach (var t in _stepTimes.Values) { total += t.Total; frames += t.Frames; }
            var sb = new StringBuilder();
            sb.Append("Placemaker build timing (").Append(outcome).Append("): ").Append(F(total)).Append(" ms of ours over ")
              .Append(UnityEngine.Time.frameCount - _timingStartFrame).Append(" frames, ")
              .Append(System.GC.CollectionCount(0) - _timingGc0).Append(" GC; worst frames");
            if (_worstFrames.Count == 0) sb.Append(" all under 5 ms");
            foreach (var (frame, step, ms) in _worstFrames) sb.Append(' ').Append(frame).Append(' ').Append(step).Append(' ').Append(F(ms)).Append(" ms,");
            sb.Append(" per step (ms total / worst frame / frames):");
            foreach (var kv in _stepTimes)
                sb.Append(' ').Append(kv.Key).Append(' ').Append(F(kv.Value.Total)).Append('/').Append(F(kv.Value.Max)).Append('/').Append(kv.Value.Frames).Append(',');
            sb.Length--;
            AppendSubs(sb, _subBuild);
            Mod.Log.Info(sb.ToString());
            _stepTimes.Clear();
            _worstFrames.Clear();
        }

        private static string F(double ms) => ms.ToString(ms < 10 ? "0.0" : "0", CultureInfo.InvariantCulture);
    }
}
