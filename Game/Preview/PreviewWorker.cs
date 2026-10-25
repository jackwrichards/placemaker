// Placemaker — runs Composer.Compose off the main thread.
//
// Latest request wins: a new request cancels the running one (CancellationToken) and only a
// result whose revision matches the newest request is ever handed back (stale-revision guard),
// so a slow old plan can never overwrite a newer one. A watchdog gives up on a plan that runs
// too long and reports an issue instead of leaving "planning…" forever. Nothing thrown by a
// generator ever reaches the game loop. Idea from Grid Road Generator's SketchWorker, made
// generic over recipes.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;

namespace Placemaker.Game.Preview
{
    public sealed class PreviewWorker : IDisposable
    {
        public sealed class Result
        {
            public int Revision;
            public ComposedPlan Plan;
            public double Milliseconds;
            /// <summary>(perf) The after-step on the worker (assets), ms.</summary>
            public double AfterMs;
            /// <summary>Set when planning failed or timed out (plan is then an empty plan carrying the issue).</summary>
            public string FailureKey;
        }

        /// <summary>A plan running longer than this is abandoned (layers are budgeted at 250 ms for 5 ha).</summary>
        // (shops4) The user: "I don't care if it takes 12 seconds ... or a minute, as long as we show the player something's happening"
        // (the panel says "Planning…"): a big strip mall planned in 10-12 s in the game and hit the old 12 s limit. A runaway guard only.
        public double TimeoutSeconds = 60.0;

        private readonly object _lock = new object();
        private CancellationTokenSource _cts;
        private int _latestRevision = -1;
        private Result _finished;
        private Stopwatch _running;
        private int _runningRevision = -1;
        private bool _disposed;

        /// <summary>True while a requested plan has not come back yet.</summary>
        public bool Busy
        {
            get { lock (_lock) return _running != null; }
        }

        public int LatestRevision
        {
            get { lock (_lock) return _latestRevision; }
        }

        /// <summary>
        /// Plans <paramref name="recipe"/> over <paramref name="area"/>. The caller must hand over
        /// objects it will not mutate afterwards (a recipe clone, an immutable shape, a fresh site).
        /// </summary>
        public void Request(int revision, Recipe recipe, AreaShape area, SiteContext site, Action<ComposedPlan> after = null)
        {
            if (_disposed) return;
            CancellationTokenSource cts;
            lock (_lock)
            {
                _cts?.Cancel();
                _cts = cts = new CancellationTokenSource();
                _latestRevision = revision;
                _running = Stopwatch.StartNew();
                _runningRevision = revision;
            }
            var token = cts.Token;
            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                Result r;
                try
                {
                    var plan = Composer.Compose(recipe, area, site, token);
                    double planned = sw.Elapsed.TotalMilliseconds;
                    // (perf) what the main thread used to do with every plan (assets), still on the worker
                    if (after != null) { token.ThrowIfCancellationRequested(); after(plan); }
                    r = new Result { Revision = revision, Plan = plan, Milliseconds = planned, AfterMs = sw.Elapsed.TotalMilliseconds - planned };
                }
                catch (OperationCanceledException)
                {
                    return;   // superseded; the newer request reports
                }
                catch (Exception e)
                {
                    var plan = new ComposedPlan();
                    plan.Issues.Add(new PlanIssue
                    {
                        Severity = IssueSeverity.Error, Key = "Placemaker.Issue.PlanFailed",
                        Args = new object[] { e.GetType().Name + ": " + e.Message },
                    });
                    r = new Result { Revision = revision, Plan = plan, FailureKey = "Placemaker.Issue.PlanFailed", Milliseconds = sw.Elapsed.TotalMilliseconds };
                    try { Mod.Log.Warn("Placemaker: planning failed: " + e); } catch (Exception) { }
                }
                lock (_lock)
                {
                    if (revision != _latestRevision) return;     // stale
                    _finished = r;
                    _running = null;
                }
            }, token);
        }

        /// <summary>Main thread, once per frame: the newest finished result, or null.</summary>
        public Result Pump()
        {
            lock (_lock)
            {
                if (_running != null && _running.Elapsed.TotalSeconds > TimeoutSeconds)
                {
                    _cts?.Cancel();
                    var plan = new ComposedPlan();
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = "Placemaker.Issue.PlanTimeout", Args = new object[] { (int)TimeoutSeconds } });
                    _finished = new Result { Revision = _runningRevision, Plan = plan, FailureKey = "Placemaker.Issue.PlanTimeout", Milliseconds = _running.Elapsed.TotalMilliseconds };
                    _running = null;
                    // Any late result of this revision is now stale.
                    _latestRevision = int.MinValue;
                }
                var r = _finished;
                _finished = null;
                return r;
            }
        }

        /// <summary>Drops any running or finished plan (selection cleared, tool closed).</summary>
        public void Cancel()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts = null;
                _running = null;
                _finished = null;
                _latestRevision = int.MinValue;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Cancel();
        }
    }
}
