// Placemaker — runs the area fill (Core AreaFill) for the Paint bucket and Paint area without freezing the game.
//
// The user (area overhaul): "and then lag, of course, we're trying to minimize that as well". A fill draws a window of
// walls into a grid and measures it (tens of milliseconds, more for a big reach), so hovering runs it on a worker
// thread: the walls are read on the main thread (ECS), handed over as a snapshot, and when the fill needs a bigger
// window than the snapshot covers it stops (FillNeedsWalls) and is started again with the bigger box next frame.
// The newest hover wins; a click waits for the job of its own spot or fills right away. Windows (grids) are kept
// for further hovers nearby while the walls and the rules stay the same.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public sealed class AreaFillRunner
    {
        private readonly AreaWalls _walls;
        private readonly FillWindow[] _cache = new FillWindow[3];
        private readonly object _lock = new object();

        private sealed class Job
        {
            public float2 Seed;
            public BrushRules Rules;
            public int Group;
            public float2 Min, Max;
            public List<BrushObstacle> Walls;
            public Func<float2, bool> Water;
            public Task<FillResult> Task;
            public FillNeedsWalls Needs;
        }

        private Job _running, _pending;
        private string _cacheKey = "";

        public AreaFillRunner(AreaWalls walls) { _walls = walls; }

        /// <summary>The newest finished fill (hover), or null.</summary>
        public FillResult Latest { get; private set; }
        /// <summary>The rules and group <see cref="Latest"/> was made with.</summary>
        public BrushRules LatestRules { get; private set; }
        public int LatestGroup { get; private set; }
        /// <summary>Bumped whenever Latest changes.</summary>
        public int Version { get; private set; }
        public bool Busy => _running != null || _pending != null;

        /// <summary>Forget results and grids (the city or the rules changed).</summary>
        public void Invalidate()
        {
            lock (_lock) { for (int i = 0; i < _cache.Length; i++) _cache[i] = null; }
            _cacheKey = "";
            Latest = null;
            Version++;
        }

        public bool Matches(FillResult r, float2 seed, BrushRules rules, int group) =>
            r != null && math.distance(r.Seed, seed) < 0.01f && LatestRules != null && Same(LatestRules, rules) && LatestGroup == group;

        private static bool Same(BrushRules a, BrushRules b) => a.SameWalls(b) && a.Gap == b.Gap && a.Reach == b.Reach;

        /// <summary>Asks for a fill at <paramref name="seed"/> in the background (replaces any older request not started yet).</summary>
        public void Request(float2 seed, BrushRules rules, int group)
        {
            rules = rules.ForOutline();
            if (_running != null && math.distance(_running.Seed, seed) < 0.01f && Same(_running.Rules, rules) && _running.Group == group) { _pending = null; return; }
            if (Latest != null && Matches(Latest, seed, rules, group) && _running == null) return;
            _pending = new Job { Seed = seed, Rules = rules, Group = group };
            if (_running == null) StartPending();
        }

        /// <summary>Call every frame: hands back finished jobs, restarts those that need more walls, starts the pending one.</summary>
        public void Pump()
        {
            var job = _running;
            if (job == null) { if (_pending != null) StartPending(); return; }
            if (!job.Task.IsCompleted) return;
            _running = null;
            if (job.Needs != null)
            {
                // The fill grew past the walls it was given: read the bigger box now and run it again (unless superseded).
                if (_pending == null) { job.Min = job.Needs.Min; job.Max = job.Needs.Max; job.Needs = null; Start(job); }
                else StartPending();
                return;
            }
            if (job.Task.Status == TaskStatus.RanToCompletion && job.Task.Result != null)
            {
                Latest = job.Task.Result;
                LatestRules = job.Rules;
                LatestGroup = job.Group;
                Version++;
            }
            if (_pending != null) StartPending();
        }

        /// <summary>
        /// The fill at <paramref name="seed"/> now (a click): the finished hover result when it is for this spot, the running
        /// job's when it is (waits up to a second), else filled here on the main thread.
        /// </summary>
        public FillResult Now(float2 seed, BrushRules rules, int group)
        {
            rules = rules.ForOutline();
            if (_running != null && math.distance(_running.Seed, seed) < 0.01f && Same(_running.Rules, rules) && _running.Group == group)
            {
                try { _running.Task.Wait(1000); } catch (Exception) { }
                Pump();
            }
            if (Matches(Latest, seed, rules, group)) return Latest;
            if (_running != null) { try { _running.Task.Wait(1500); } catch (Exception) { } }
            EnsureCacheKey(rules, group);
            FillResult r;
            lock (_lock)
            {
                r = AreaFill.Fill(seed, (mn, mx, into) => _walls.Counted(mn, mx, group, rules, into), Water(seed, rules), rules.Fill(), _cache);
            }
            Latest = r;
            LatestRules = rules;
            LatestGroup = group;
            Version++;
            return r;
        }

        private Func<float2, bool> Water(float2 seed, BrushRules rules)
        {
            float h = math.min(rules.Reach, 400f) + 40f;
            return _walls.Water(seed - h, seed + h, 2f, rules);
        }

        /// <summary>Grids older than this (s) are drawn again: buildings grow, zoning changes.</summary>
        private const float CacheAge = 10f;
        private float _cacheAt = -100f;

        private void EnsureCacheKey(BrushRules rules, int group)
        {
            string key = rules.GetHashCode() + "/" + group;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (key == _cacheKey && now - _cacheAt < CacheAge && now >= _cacheAt) return;
            lock (_lock) { for (int i = 0; i < _cache.Length; i++) _cache[i] = null; }
            _cacheKey = key;
            _cacheAt = now;
        }

        private void StartPending()
        {
            var job = _pending;
            _pending = null;
            if (job == null) return;
            var o = job.Rules.Fill();
            float half = math.min(o.FirstWindow, o.Reach + 2f) * FillWindow.Grow + 4f;
            job.Min = job.Seed - half;
            job.Max = job.Seed + half;
            Start(job);
        }

        private void Start(Job job)
        {
            EnsureCacheKey(job.Rules, job.Group);
            // Walls and water are read here, on the main thread; the worker only sees the snapshot.
            job.Walls = new List<BrushObstacle>();
            try { _walls.Counted(job.Min, job.Max, job.Group, job.Rules, job.Walls); }
            catch (Exception e) { Mod.Log.Warn("Placemaker: fill walls failed: " + e.Message); }
            job.Water = _walls.Water(job.Min, job.Max, 2f, job.Rules);
            var walls = job.Walls;
            float2 bmin = job.Min, bmax = job.Max;
            var water = job.Water;
            var rules = job.Rules;
            var seed = job.Seed;
            _running = job;
            job.Task = Task.Run(() =>
            {
                try
                {
                    lock (_lock)
                    {
                        return AreaFill.Fill(seed, (mn, mx, into) =>
                        {
                            if (mn.x < bmin.x - 0.01f || mn.y < bmin.y - 0.01f || mx.x > bmax.x + 0.01f || mx.y > bmax.y + 0.01f)
                                throw new FillNeedsWalls(math.min(mn, bmin) - 10f, math.max(mx, bmax) + 10f);
                            foreach (var w in walls)
                            {
                                Poly.Bounds(w.Ring, out var a, out var b);
                                if (b.x < mn.x || b.y < mn.y || a.x > mx.x || a.y > mx.y) continue;
                                into.Add(w);
                            }
                        }, water, rules.Fill(), _cache);
                    }
                }
                catch (FillNeedsWalls n) { job.Needs = n; return null; }
                catch (Exception e) { return new FillResult { Seed = seed, Status = FillStatus.Failed, Error = e.Message }; }
            });
        }
    }
}
