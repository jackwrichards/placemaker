// (zoning speed) Where a plan's time goes, for the log: each layer (with its own steps) and each step the composer runs after
// them. The game logs PlanTimings.Describe for every preview plan; the tests print it.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Placemaker.Core
{
    /// <summary>Laps into a list of (step, ms): each lap the time since the one before (or since the clock started).</summary>
    public sealed class PlanClock
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly List<KeyValuePair<string, double>> _into;
        private double _last;

        public PlanClock(List<KeyValuePair<string, double>> into) { _into = into; }

        /// <summary>The time since the last lap as <paramref name="what"/>; <paramref name="inner"/>: its own steps, in brackets.</summary>
        public void Lap(string what, List<KeyValuePair<string, double>> inner = null)
        {
            double now = _sw.Elapsed.TotalMilliseconds;
            if (_into != null)
            {
                string name = inner != null && inner.Count > 0 ? what + " [" + PlanTimings.Join(inner) + "]" : what;
                _into.Add(new KeyValuePair<string, double>(name, now - _last));
            }
            _last = now;
        }
    }

    public static class PlanTimings
    {
        /// <summary>"zoningIndustrial 85 [lots 12, level 60 […]], decals 1, …" (ms): every step of 1 ms or more.</summary>
        public static string Describe(ComposedPlan plan) => plan?.Timings == null ? "" : Join(plan.Timings);

        internal static string Join(List<KeyValuePair<string, double>> t)
        {
            var shown = new List<string>();
            foreach (var kv in t)
            {
                int b = kv.Key.IndexOf(" [", System.StringComparison.Ordinal);
                if (kv.Value < 1.0 && b < 0) continue;
                string ms = kv.Value.ToString("0", CultureInfo.InvariantCulture);
                shown.Add(b < 0 ? kv.Key + " " + ms : kv.Key.Substring(0, b) + " " + ms + kv.Key.Substring(b));
            }
            return shown.Count > 0 ? string.Join(", ", shown) : "all under 1 ms";
        }
    }
}
