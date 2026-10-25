// (perf) A slow frame of the tool (design, preview), named: every frame the tool's own work over SlowFrameMs logs which of its
// calls took the time ("Placemaker slow tool frame: 322 ms: GhostFrame 290, PumpPreview 21"). Teardown 14: the game runner saw
// 270–320 ms frames while entrances were placed and the preview came up, and nothing said whose they were.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Placemaker.Game.Tool
{
    public sealed partial class PlacemakerToolSystem
    {
        private const double SlowFrameMs = 30.0;
        private readonly List<KeyValuePair<string, double>> _frameParts = new List<KeyValuePair<string, double>>();
        private long _frameStart, _partStart;
        private int _slowFramesLogged;
        private float _slowFrameLogAt;

        private void FrameBegin()
        {
            _frameParts.Clear();
            _frameStart = _partStart = Stopwatch.GetTimestamp();
        }

        /// <summary>The time since the last part (or the frame's start) as <paramref name="name"/>.</summary>
        private void FramePart(string name)
        {
            long now = Stopwatch.GetTimestamp();
            double ms = (now - _partStart) * 1000.0 / Stopwatch.Frequency;
            _partStart = now;
            if (ms >= 1.0) _frameParts.Add(new KeyValuePair<string, double>(name, ms));
        }

        private void FrameEnd()
        {
            double total = (Stopwatch.GetTimestamp() - _frameStart) * 1000.0 / Stopwatch.Frequency;
            if (total < SlowFrameMs) return;
            // at most a few lines a second: a drag of slow frames should not flood the log
            float t = UnityEngine.Time.realtimeSinceStartup;
            if (t - _slowFrameLogAt < 1f && _slowFramesLogged >= 3) return;
            if (t - _slowFrameLogAt >= 1f) { _slowFrameLogAt = t; _slowFramesLogged = 0; }
            _slowFramesLogged++;
            _frameParts.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder("Placemaker slow tool frame: ");
            sb.Append(total.ToString("0", CultureInfo.InvariantCulture)).Append(" ms (stage ").Append(_stage).Append("):");
            foreach (var kv in _frameParts) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value.ToString("0", CultureInfo.InvariantCulture)).Append(',');
            sb.Length--;
            Mod.Log.Info(sb.ToString());
        }
    }
}
