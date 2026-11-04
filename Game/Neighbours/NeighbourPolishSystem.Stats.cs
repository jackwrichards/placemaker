// Placemaker neighbourhood polish — what Global outgrow shows: the numbers, their last two minutes, and the status lens
// (GPL-3.0).
//
// Worked out with the status line, about once a second: growable buildings and how many are outgrown by kind and by zone
// type, outgrown buildings by the look they have, the work in line (houses to fit, stamps, plans being made, lots being
// put back, pieces being made), how fast houses are fitted and put in line, the game's frames per second and Outgrow's main
// thread time per frame. The lens (Global outgrow open, its switch on): every growable building near the camera by its
// state: outgrown, not, being planned now, just put in line, or waiting (by how long: the line's heat map).

using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Placemaker.Core.Neighbours;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        /// <summary>What the Global outgrow window shows.</summary>
        public sealed class OutgrowStats
        {
            /// <summary>By kind (0 homes, 1 shops, 2 offices, 3 industry): growable buildings and how many are outgrown.</summary>
            public readonly int[] KindTotal = new int[4], KindPolished = new int[4];
            /// <summary>By zone prefab name: growable buildings and how many are outgrown.</summary>
            public readonly Dictionary<string, (int Total, int Polished)> Zones = new Dictionary<string, (int, int)>();
            /// <summary>Outgrown buildings by the look they have (its id; "" a lot outgrown before looks or with one deleted).</summary>
            public readonly Dictionary<string, int> Looks = new Dictionary<string, int>();
            /// <summary>The work in line: houses to fit, stamps to give, plans being made, lots being put back, pieces being made.</summary>
            public int ToFit, Stamps, Planning, PuttingBack, Pieces;
            /// <summary>Houses taken to be fitted and houses put in line, per second (smoothed); frames per second; Outgrow's
            /// main thread time per frame (ms).</summary>
            public float FittedPerSecond, AddedPerSecond, Fps, MsPerFrame;
        }

        public static OutgrowStats Stats { get; private set; } = new OutgrowStats();
        /// <summary>The last two minutes, one sample a status update: houses in line, fitted and added per second, frames per second.</summary>
        public static readonly List<float> HistLine = new List<float>(), HistFitted = new List<float>(), HistAdded = new List<float>(), HistFps = new List<float>();
        private const int HistMax = 120;
        /// <summary>Houses taken off the line to be planned (since the game started).</summary>
        public static int TotalTaken;
        private float _statAt = -1f;
        private int _statFrame, _statTaken, _statLine;
        private double _statMs;
        private readonly Dictionary<Entity, (int Kind, string Zone)> _prefabKindZone = new Dictionary<Entity, (int, string)>();
        private static readonly string[] KindNames = { "home", "shop", "office", "industry" };

        private (int Kind, string Zone) KindZone(Entity prefab)
        {
            if (_prefabKindZone.TryGetValue(prefab, out var kz)) return kz;
            kz = (System.Array.IndexOf(KindNames, ZoneKind(prefab)), ZoneName(prefab));
            if (kz.Kind < 0) kz.Kind = 0;
            _prefabKindZone[prefab] = kz;
            return kz;
        }

        /// <summary>The numbers again (with the status line).</summary>
        private void UpdateStats()
        {
            var stats = new OutgrowStats();
            using (var refs = _growables.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                foreach (var r in refs)
                {
                    if (!_growablePrefab.TryGetValue(r.m_Prefab, out var g) || !g) continue;
                    var (kind, zone) = KindZone(r.m_Prefab);
                    stats.KindTotal[kind]++;
                    stats.Zones[zone] = stats.Zones.TryGetValue(zone, out var z) ? (z.Total + 1, z.Polished) : (1, 0);
                }
            using (var flags = _flaggedLots.ToComponentDataArray<PolishedLot>(Allocator.Temp))
            using (var refs = _flaggedLots.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                for (int i = 0; i < flags.Length; i++)
                {
                    if (!flags[i].Yes || !_growablePrefab.TryGetValue(refs[i].m_Prefab, out var g) || !g) continue;
                    var (kind, zone) = KindZone(refs[i].m_Prefab);
                    stats.KindPolished[kind]++;
                    if (stats.Zones.TryGetValue(zone, out var z)) stats.Zones[zone] = (z.Total, z.Polished + 1);
                    string look = flags[i].Look != 0 ? LookByHash(flags[i].Look)?.Id ?? "" : "";
                    stats.Looks[look] = stats.Looks.TryGetValue(look, out var n) ? n + 1 : 1;
                }
            stats.ToFit = FeatureEnabled ? _dirty.Count : 0;
            stats.Stamps = s_stamps.Count;
            stats.Planning = _inFlight.Count;
            stats.PuttingBack = _restoreQueue.Count;
            stats.Pieces = NeighbourObjectSystem.Queue.Count + NeighbourAreaSystem.Queue.Count + NeighbourLawnSystem.Queue.Count + NeighbourDeleteSystem.Queue.Count + NeighbourPaintSystem.Queue.Count;
            // rates over the time since the last update: taken off the line, and put in (the line's change plus what was taken)
            float now = UnityEngine.Time.realtimeSinceStartup;
            int line = stats.ToFit + stats.Stamps;
            var old = Stats;
            if (_statAt >= 0f && now - _statAt > 0.05f)
            {
                float dt = now - _statAt;
                float fitted = (TotalTaken - _statTaken) / dt;
                float added = math.max(0f, (line - _statLine + TotalTaken - _statTaken) / dt);
                stats.FittedPerSecond = math.lerp(old.FittedPerSecond, fitted, 0.35f);
                stats.AddedPerSecond = math.lerp(old.AddedPerSecond, added, 0.35f);
                stats.Fps = (LotFrame - _statFrame) / dt;
                stats.MsPerFrame = (float)((UpdateMs - _statMs) / math.max(1, LotFrame - _statFrame));
                Push(HistLine, line);
                Push(HistFitted, stats.FittedPerSecond);
                Push(HistAdded, stats.AddedPerSecond);
                Push(HistFps, stats.Fps);
            }
            _statAt = now;
            _statFrame = LotFrame;
            _statTaken = TotalTaken;
            _statLine = line;
            _statMs = UpdateMs;
            Stats = stats;
        }

        private static void Push(List<float> list, float v)
        {
            list.Add(v);
            if (list.Count > HistMax) list.RemoveAt(0);
        }

        private static void ForgetStats()
        {
            Stats = new OutgrowStats();
            HistLine.Clear();
            HistFitted.Clear();
            HistAdded.Clear();
            HistFps.Clear();
        }

        // ───────────── the lens's lot rings ─────────────

        // a lot's ring as the lens draws it, kept per building (buildings do not move): the lens refreshing a few times a
        // second used to make thousands of new rings each time, garbage the game paused to collect ("a big lag spike every
        // half second")
        private readonly Dictionary<Entity, (float3 At, float3[] Ring)> _lensRing = new Dictionary<Entity, (float3, float3[])>();
        private readonly HashSet<Entity> _lensStamping = new HashSet<Entity>(), _lensPlanning = new HashSet<Entity>();

        private float3[] LensRing(Entity e, global::Game.Objects.Transform t, Entity prefab)
        {
            if (_lensRing.TryGetValue(e, out var c) && math.all(c.At == t.m_Position)) return c.Ring;
            if (_lensRing.Count > 100000) _lensRing.Clear();
            var half = (float2)EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize * 4f - 0.4f;
            var ring = new[]
            {
                global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(-half.x, 0f, -half.y)), global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(half.x, 0f, -half.y)),
                global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(half.x, 0f, half.y)), global::Game.Objects.ObjectUtils.LocalToWorld(t, new float3(-half.x, 0f, half.y)),
            };
            _lensRing[e] = (t.m_Position, ring);
            return ring;
        }

        /// <summary>Work in line (houses to fit, stamps, plans): the status lens's waiting colours change while there is.</summary>
        public bool LensBusy => _dirty.Count > 0 || s_stamps.Count > 0 || _inFlight.Count > 0;

        // ───────────── the types map ─────────────

        /// <summary>Buildings the types map showed last time, by look id ("" outgrown with no look; "-" not outgrown).</summary>
        public static readonly Dictionary<string, int> LensTypeCounts = new Dictionary<string, int>();

        /// <summary>Every growable building near the camera by its outgrow type: into[look id] ("-": not outgrown).</summary>
        public void TypeRingsNear(float2 centre, float radius, Dictionary<string, List<float3[]>> into, int max = 8000)
        {
            foreach (var l in into.Values) l.Clear();
            LensTypeCounts.Clear();
            int shown = 0;
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (shown >= max) break;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    if (math.distance(t.m_Position.xz, centre) > radius) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!_growablePrefab.TryGetValue(prefab, out var g) || !g || !EntityManager.HasComponent<BuildingData>(prefab)) continue;
                    string key = "-";
                    if (EntityManager.HasComponent<PolishedLot>(e))
                    {
                        var f = EntityManager.GetComponentData<PolishedLot>(e);
                        if (f.Yes) key = f.Look != 0 ? LookByHash(f.Look)?.Id ?? "" : "";
                    }
                    if (!into.TryGetValue(key, out var list)) into[key] = list = new List<float3[]>();
                    list.Add(LensRing(e, t, prefab));
                    LensTypeCounts[key] = LensTypeCounts.TryGetValue(key, out var n) ? n + 1 : 1;
                    shown++;
                }
        }

        // ───────────── the status lens ─────────────

        /// <summary>The lens's layers: outgrown, not outgrown, being planned now, just put in line, then waiting by how long
        /// (under 10 s, 30 s, 90 s, longer).</summary>
        public const int LensPolished = 0, LensNot = 1, LensPlanning = 2, LensAdded = 3, LensWait0 = 4, LensLayers = 8;
        public static readonly float[] LensWaitLimits = { 10f, 30f, 90f };
        /// <summary>When each house waiting in line was put in it (seen by the lens).</summary>
        private readonly Dictionary<Entity, float> _lineSince = new Dictionary<Entity, float>();
        /// <summary>Buildings the lens showed last time, by layer (Global outgrow's legend).</summary>
        public static readonly int[] LensCounts = new int[LensLayers];

        /// <summary>Every growable building within <paramref name="radius"/> of <paramref name="centre"/>, its lot by its state
        /// (into[layer], at most <paramref name="max"/> in all).</summary>
        public void LensRingsNear(float2 centre, float radius, List<float3[]>[] into, int max = 8000)
        {
            foreach (var l in into) l.Clear();
            System.Array.Clear(LensCounts, 0, LensCounts.Length);
            float now = UnityEngine.Time.realtimeSinceStartup;
            // when each house in line came in (one gone from it is forgotten)
            foreach (var e in _dirty) if (!_lineSince.ContainsKey(e)) _lineSince[e] = now;
            if (_lineSince.Count > _dirty.Count) foreach (var e in _lineSince.Keys.Where(k => !_dirty.Contains(k)).ToList()) _lineSince.Remove(e);
            var stamping = _lensStamping;
            var planning = _lensPlanning;
            stamping.Clear();
            planning.Clear();
            foreach (var st in s_stamps) stamping.Add(st.Lot);
            foreach (var pend in _inFlight)
                if (pend.CoreIds != null && pend.HouseOf != null)
                    foreach (var id in pend.CoreIds) if (pend.HouseOf.TryGetValue(id, out var he)) planning.Add(he);
            int shown = 0;
            using (var all = _growables.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (shown >= max) break;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    if (math.distance(t.m_Position.xz, centre) > radius) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!_growablePrefab.TryGetValue(prefab, out var g) || !g || !EntityManager.HasComponent<BuildingData>(prefab)) continue;
                    int layer;
                    if (planning.Contains(e)) layer = LensPlanning;
                    else if (_lineSince.TryGetValue(e, out var since) || stamping.Contains(e))
                    {
                        float waited = stamping.Contains(e) && !_lineSince.ContainsKey(e) ? 0f : now - since;
                        if (waited < 1.5f) layer = LensAdded;
                        else
                        {
                            layer = LensWait0 + LensWaitLimits.Length;
                            for (int k = 0; k < LensWaitLimits.Length; k++) if (waited < LensWaitLimits[k]) { layer = LensWait0 + k; break; }
                        }
                    }
                    else layer = IsYes(e) ? LensPolished : LensNot;
                    into[layer].Add(LensRing(e, t, prefab));
                    LensCounts[layer]++;
                    shown++;
                }
        }
    }
}
