// Placemaker neighbourhood polish — gardens remembered with their houses, fences as they stand, and fences kept visible
// (GPL-3.0).
//
//   * Each polished house keeps its garden's outline (PolishGarden, saved): the plans of its neighbours made later keep off
//     it and meet it on its real edges, also after a load. Before, the outline lived in memory only, and a city loaded
//     again had its gardens planned through each other (hedges crossing, strips nobody fenced).
//   * A neighbour a plan does not edit shows the plan its fences and hedges as they stand (its lines where they are, the
//     closing pieces it has): an edited garden closes its edges against those, not against where the plan would have put
//     that neighbour's lines (it never writes them), which left gaps.
//   * The game hides (Game.Objects.OverrideSystem: Overridden, and the object's mask zeroed in the static search tree) any
//     overridable object that touches another one or a building's lot volume. A closing piece on a garden's edge touches
//     the bushes, trees and lots beside it, so whole stretches of fence stood there unseen ("invisible until I hover over
//     the asset": hovering draws a copy of the house with its pieces, which the game does not hide). Our own pieces are
//     shown again in the frame the game hid them (NeighbourShowSystem), as Placemaker's builds do it (BuildService.Audit),
//     unless the game is right to hide them: a fence or hedge piece lying along another fence or hedge (a doubled border:
//     one is enough), or a piece standing on a road or inside another building's lot.

using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Placemaker.Game.Selection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        // ───────────── gardens kept with their houses ─────────────

        /// <summary>The house's garden as its last plan wrote it (this session, else the one saved with it); null: unknown.</summary>
        private float2[] GardenOf(Entity house)
        {
            if (_gardenOf.TryGetValue(house, out var g)) return g;
            if (!EntityManager.HasBuffer<PolishGarden>(house)) return null;
            var buf = EntityManager.GetBuffer<PolishGarden>(house, true);
            if (buf.Length < 3) return null;
            g = new float2[buf.Length];
            for (int i = 0; i < g.Length; i++) g[i] = buf[i].Position;
            _gardenOf[house] = g;
            return g;
        }

        /// <summary>The garden a plan wrote for the house, kept in memory and with the house (saved).</summary>
        private void KeepGarden(Entity house, float2[] garden)
        {
            _gardenOf[house] = garden;
            var buf = EntityManager.HasBuffer<PolishGarden>(house) ? EntityManager.GetBuffer<PolishGarden>(house) : EntityManager.AddBuffer<PolishGarden>(house);
            buf.Clear();
            foreach (var p in garden) buf.Add(new PolishGarden { Position = p });
        }

        private static float2[] LotRing(StitchHouse h) => h == null ? null : new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };

        // every garden's bounds (worked out again when its ring changes), and whether the gardens a loaded city saved are all
        // read (a pass must see a far house's garden that reaches into it, not only the ones planned this session)
        private readonly Dictionary<Entity, (float2[] Ring, float2 Lo, float2 Hi)> _gardenBox = new Dictionary<Entity, (float2[], float2, float2)>();
        private bool _gardensRead;
        private EntityQuery _savedGardens;
        /// <summary>For the game runner: houses outside a pass's neighbourhood whose gardens reach into it, planned around.</summary>
        public static int FarGardens;

        /// <summary>Every polished garden known (this session's and the ones saved with the city) with its bounds.</summary>
        private IEnumerable<(Entity House, float2 Lo, float2 Hi)> GardenBoxes()
        {
            if (!_gardensRead)
            {
                _gardensRead = true;
                if (_savedGardens == default) _savedGardens = GetEntityQuery(ComponentType.ReadOnly<PolishGarden>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                using (var all = _savedGardens.ToEntityArray(Allocator.Temp))
                    foreach (var e in all) GardenOf(e);
            }
            foreach (var kv in _gardenOf)
            {
                if (kv.Value == null || kv.Value.Length < 3) continue;
                if (!_gardenBox.TryGetValue(kv.Key, out var box) || !ReferenceEquals(box.Ring, kv.Value))
                {
                    Placemaker.Core.Geometry.Poly.Bounds(kv.Value, out var lo, out var hi);
                    _gardenBox[kv.Key] = box = (kv.Value, lo, hi);
                }
                yield return (kv.Key, box.Lo, box.Hi);
            }
        }

        // each house's standing fences as last read, until one of its fence lanes or pieces changes (StandingChanged): read
        // again for every neighbour of every plan, they were most of a plan's time on the frame (10 to 36 ms in a city of 15 000)
        private readonly Dictionary<Entity, List<(float2 A, float2 B)>> _standing = new Dictionary<Entity, List<(float2, float2)>>();

        /// <summary>A fence lane or piece of the house was made, moved, hidden or taken away: its standing fences are read
        /// again when next asked for.</summary>
        private void StandingChanged(Entity house) => _standing.Remove(house);

        /// <summary>The house's fence and hedge lines where they stand now (a line collapsed to nothing left out) and the closing
        /// pieces it has, as segments (a list never changed once given: plans on other threads read it).</summary>
        private List<(float2 A, float2 B)> StandingOf(Entity house)
        {
            if (_standing.TryGetValue(house, out var known))
            {
                // knob checkStanding (the game runner): read again anyway and compare (a change no StandingChanged saw)
                if (Settings.Knob("checkStanding", 0f) < 0.5f) return known;
                var fresh = ReadStanding(house);
                StandingChecked++;
                if (SameSegments(known, fresh)) return known;
                StandingMismatches++;
                if (s_standingLogged++ < 10) Mod.Log.Warn($"Placemaker neighbourhood polish: house {house.Index}'s standing fences changed unseen ({known.Count} remembered, {fresh.Count} standing; {StandingDiff(known, fresh)})");
                _standing[house] = fresh;
                return fresh;
            }
            var list = ReadStanding(house);
            if (_standing.Count > 20000) _standing.Clear();
            _standing[house] = list;
            return list;
        }

        /// <summary>For the game runner (knob checkStanding): remembered standing fences read again, and how many had changed.</summary>
        public static int StandingChecked, StandingMismatches;
        private static int s_standingLogged;

        /// <summary>For the check's log: the same segments in another order, or how far the furthest one moved.</summary>
        private static string StandingDiff(List<(float2 A, float2 B)> a, List<(float2 A, float2 B)> b)
        {
            string Key((float2 A, float2 B) x) => $"{x.A.x:R},{x.A.y:R},{x.B.x:R},{x.B.y:R}";
            var sa = a.Select(Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var sb = b.Select(Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (sa.SequenceEqual(sb)) return "the same, in another order";
            float worst = 0f;
            foreach (var x in b)
            {
                float best = float.MaxValue;
                foreach (var y in a) best = math.min(best, math.distance(x.A, y.A) + math.distance(x.B, y.B));
                worst = math.max(worst, best);
            }
            return $"moved up to {worst:F4} m";
        }

        private static bool SameSegments(List<(float2 A, float2 B)> a, List<(float2 A, float2 B)> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!a[i].A.Equals(b[i].A) || !a[i].B.Equals(b[i].B)) return false;
            return true;
        }

        private List<(float2 A, float2 B)> ReadStanding(Entity house)
        {
            var list = new List<(float2 A, float2 B)>();
            if (EntityManager.HasBuffer<global::Game.Net.SubLane>(house))
            {
                var lanes = EntityManager.GetBuffer<global::Game.Net.SubLane>(house, true);
                for (int i = 0; i < lanes.Length; i++)
                {
                    var lane = lanes[i].m_SubLane;
                    if (!EntityManager.Exists(lane) || EntityManager.HasComponent<Deleted>(lane) || !EntityManager.HasComponent<global::Game.Net.UtilityLane>(lane) || !EntityManager.HasComponent<Curve>(lane)) continue;
                    if (!IsFence(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab)) continue;
                    var c = EntityManager.GetComponentData<Curve>(lane);
                    if (c.m_Length < 0.3f) continue;
                    // a bent line as three straight pieces
                    bool straight = c.m_Length - math.distance(c.m_Bezier.a.xz, c.m_Bezier.d.xz) < 0.05f;
                    int n = straight ? 1 : 3;
                    for (int k = 0; k < n; k++) list.Add((MathUtils.Position(c.m_Bezier, k / (float)n).xz, MathUtils.Position(c.m_Bezier, (k + 1) / (float)n).xz));
                }
            }
            foreach (var (piece, _, _) in OwnPieces(house, PolishPieceKind.Garden))
                if (PieceSegment(piece, out var a, out var b, out bool fence) && fence) list.Add((a, b));
            return list;
        }

        /// <summary>The look (colour, mesh variation) of a garden's fence or hedge pieces: one for the whole garden, its 2 m
        /// pieces the same as its 4 m ones (they finish its runs).</summary>
        private static int PieceSeed(Entity owner, string name) =>
            (int)math.hash(new int2(owner.Index, (name ?? "").Replace("Short", "").GetHashCode())) | 1;

        // ───────────── a piece as a segment ─────────────

        private struct PieceShape
        {
            public bool Fence;
            public float3 Mid, Axis;
            public float Half;
            public Bounds3 Bounds;
        }

        private readonly Dictionary<Entity, PieceShape> _pieceShapes = new Dictionary<Entity, PieceShape>();

        private PieceShape ShapeOf(Entity prefab)
        {
            if (_pieceShapes.TryGetValue(prefab, out var shape)) return shape;
            var n = NameOf(prefab);
            shape.Fence = n.IndexOf("Piece", StringComparison.OrdinalIgnoreCase) >= 0
                && (n.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0);
            if (EntityManager.HasComponent<ObjectGeometryData>(prefab))
            {
                var gb = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
                var size = gb.max - gb.min;
                shape.Bounds = gb;
                shape.Mid = (gb.min + gb.max) * 0.5f;
                shape.Axis = size.z >= size.x ? new float3(0f, 0f, 1f) : new float3(1f, 0f, 0f);
                shape.Half = math.max(size.x, size.z) * 0.5f;
            }
            _pieceShapes[prefab] = shape;
            return shape;
        }

        /// <summary>An object's long axis on the ground (a fence piece: the stretch it covers).</summary>
        private bool PieceSegment(Entity o, out float2 a, out float2 b, out bool fence)
        {
            a = b = default;
            fence = false;
            if (!EntityManager.HasComponent<global::Game.Objects.Transform>(o) || !EntityManager.HasComponent<PrefabRef>(o)) return false;
            var shape = ShapeOf(EntityManager.GetComponentData<PrefabRef>(o).m_Prefab);
            if (shape.Half <= 0f) return false;
            var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(o);
            a = global::Game.Objects.ObjectUtils.LocalToWorld(t, shape.Mid - shape.Axis * shape.Half).xz;
            b = global::Game.Objects.ObjectUtils.LocalToWorld(t, shape.Mid + shape.Axis * shape.Half).xz;
            fence = shape.Fence;
            return true;
        }

        // ───────────── our pieces the game hid ─────────────

        private EntityQuery _hiddenPieces;
        // pieces left hidden on purpose, when it was decided and where (looked at again later: what was in the way may be gone)
        private readonly Dictionary<Entity, (int Frame, float2 At)> _keptHidden = new Dictionary<Entity, (int, float2)>();
        // a kept piece is looked at again once a plan is written around it (LookAgainNear), else after about 30 s, each at its own
        // frame and a few a frame: all of them on one frame every 2 s was 150 to 200 ms (the user's city: 1 100 kept hidden)
        private const int KeptLookAgain = 1800, KeptLookSpread = 600, KeptLooksPerFrame = 24;
        // how often each piece was shown again (the log only)
        private readonly Dictionary<Entity, int> _shownTimes = new Dictionary<Entity, int>();
        // when each of our pieces was placed (TagNewPieces): the ones a plan just placed are judged after the ones that stood
        private readonly Dictionary<Entity, int> _placedAt = new Dictionary<Entity, int>();
        /// <summary>For the game runner: our pieces shown again after the game hid them, and the ones left hidden; hidden pieces
        /// left for the next frame (the frame's budget spent).</summary>
        public static int TotalShownAgain, TotalKeptHidden, ShowLeftOver;
        private int _shownSinceLog, _keptSinceLog, _againSinceLog;
        // pieces queued for deletion (NeighbourDeleteSystem, next frame's PostTool)
        private readonly HashSet<Entity> _goingSoon = new HashSet<Entity>();

        private void CreateHiddenQuery()
        {
            // tags only: reading it never waits for a job moving objects
            _hiddenPieces = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PolishPiece>(), ComponentType.ReadOnly<Overridden>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
        }

        /// <summary>
        /// Our pieces the game hid, shown again in the same frame (NeighbourShowSystem, ModificationEnd: after
        /// Objects.OverrideSystem hid them in Modification5, before culling and rendering): Overridden removed, the object redrawn
        /// (BatchesUpdated) and put back in the static search tree with the mask a visible one has (OverrideSystem zeroes it, and
        /// the renderer culls through that tree). The game hides one again whenever something beside it changes; it is shown
        /// again before it is ever drawn hidden. Before, this ran every 16th frame and ahead of the game's check (a piece was
        /// drawn hidden for up to 16 frames, so after three times it was left hidden for minutes: in a growing town most
        /// fences stood unseen until the player hovered their house).
        /// </summary>
        internal void ShowHiddenPieces()
        {
            if (_hiddenPieces.IsEmptyIgnoreFilter) return;
            if (_keptHidden.Count > 20000) _keptHidden.Clear();
            _goingSoon.Clear();
            foreach (var q in NeighbourDeleteSystem.Queue) _goingSoon.Add(q);
            var show = new List<Entity>();
            int looks = 0;
            // a few ms a frame (knob showBudgetMs, 0: all in one frame, as before): a plan written makes the game hide hundreds
            // of pieces at once, and judging them all was 20 ms bursts. The ones shown before (they stood visible) first, so
            // they never stay hidden; ones a plan just placed left over wait a frame or two still hidden (never seen yet). The
            // ones not judged stay hidden, and the query finds them again next frame
            float budget = Settings.Knob("showBudgetMs", 3f);
            long stop = budget > 0f ? System.Diagnostics.Stopwatch.GetTimestamp() + (long)(budget * 0.001 * System.Diagnostics.Stopwatch.Frequency) : long.MaxValue;
            using (var all = _hiddenPieces.ToEntityArray(Allocator.Temp))
                for (int round = 0; round < 2; round++)
                    foreach (var e in all)
                    {
                        bool fresh = _placedAt.TryGetValue(e, out int placed) && LotFrame - placed < 120;
                        if (fresh != (round == 1)) continue;
                        // one left hidden: looked at again later (what it doubled may be gone), a few a frame
                        if (_keptHidden.TryGetValue(e, out var kept) && (LotFrame - kept.Frame < KeptLookAgain + (int)((uint)e.Index % KeptLookSpread) || looks++ >= KeptLooksPerFrame)) continue;
                        if (System.Diagnostics.Stopwatch.GetTimestamp() > stop) { ShowLeftOver++; continue; }
                        if (!EntityManager.HasComponent<global::Game.Objects.Object>(e) || !PieceSegment(e, out var a, out var b, out bool fence)) continue;
                        if ((fence && Doubled(e, a, b)) || OnRoadOrBuilding(e, (a + b) * 0.5f))
                        {
                            if (!_keptHidden.ContainsKey(e)) { TotalKeptHidden++; _keptSinceLog++; }
                            _keptHidden[e] = (LotFrame, (a + b) * 0.5f);
                            continue;
                        }
                        show.Add(e);
                    }
            if (show.Count == 0) return;
            if (_shownTimes.Count > 50000) _shownTimes.Clear();
            foreach (var e in show)
            {
                _keptHidden.Remove(e);
                int times = _shownTimes.TryGetValue(e, out int t) ? t + 1 : 1;
                _shownTimes[e] = times;
                if (times > 1) _againSinceLog++;
                EntityManager.RemoveComponent<Overridden>(e);
                EntityManager.AddComponent<BatchesUpdated>(e);
            }
            _objectSearch ??= World.GetExistingSystemManaged<global::Game.Objects.SearchSystem>();
            if (_objectSearch != null)
            {
                var tree = _objectSearch.GetStaticSearchTree(false, out var deps);
                deps.Complete();
                foreach (var e in show)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<ObjectGeometryData>(prefab) || !tree.TryGet(e, out var bounds)) continue;
                    var mask = BoundsMask.NotOverridden | CommonUtils.GetBoundsMask(EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Layers);
                    bounds.m_Mask = (bounds.m_Mask & ~(BoundsMask.AllLayers | BoundsMask.NotOverridden)) | mask;
                    tree.Update(e, bounds);
                }
                _objectSearch.AddStaticSearchTreeWriter(default);
            }
            TotalShownAgain += show.Count;
            _shownSinceLog += show.Count;
            if (_shownSinceLog >= 500 || (LotFrame & 4095) == 0)
            {
                Mod.Log.Info($"Placemaker neighbourhood polish: {_shownSinceLog} of its own pieces the game hid shown again in the same frame ({_againSinceLog} of them hidden before), {_keptSinceLog} left hidden (along another fence, on a road or in another building's lot)");
                _shownSinceLog = _keptSinceLog = _againSinceLog = 0;
            }
        }

        /// <summary>The pieces left hidden in a box looked at again next (a plan was written there: what doubled them or stood
        /// under them may be gone).</summary>
        private void LookAgainNear(float2 lo, float2 hi)
        {
            if (_keptHidden.Count == 0) return;
            List<Entity> again = null;
            foreach (var kv in _keptHidden)
                if (kv.Value.At.x >= lo.x && kv.Value.At.y >= lo.y && kv.Value.At.x <= hi.x && kv.Value.At.y <= hi.y) (again ??= new List<Entity>()).Add(kv.Key);
            if (again != null) foreach (var e in again) _keptHidden[e] = (int.MinValue / 2, _keptHidden[e].At);
        }

        /// <summary>Other fences or hedges lie along this piece (nearly parallel, within 0.7 m) over nearly all of its length,
        /// drawn: a doubled border, where the game is right to show only one. Covering only part of it is no double: hiding
        /// the piece then left the rest of its stretch open (gaps seen in the game runner).</summary>
        private bool Doubled(Entity e, float2 a, float2 b, List<string> why = null)
        {
            float len = math.distance(a, b);
            if (len < 0.5f) return false;
            var d = (b - a) / len;
            var spans = new List<(float From, float To)>();
            bool Covered()
            {
                spans.Sort((x, y) => x.From.CompareTo(y.From));
                float reach = 0f, open = 0f;
                foreach (var (from, to) in spans)
                {
                    if (from > reach) open += from - reach;
                    reach = math.max(reach, to);
                }
                open += math.max(0f, len - reach);
                return open <= math.max(0.4f, len * 0.1f);
            }
            bool Along(float2 p, float2 q, Entity by)
            {
                float l = math.distance(p, q);
                if (l < 0.5f) return false;
                if (math.abs(math.dot((q - p) / l, d)) < 0.95f) return false;
                var n = new float2(-d.y, d.x);
                if (math.abs(math.dot((p + q) * 0.5f - a, n)) > 0.7f) return false;
                float t0 = math.clamp(math.min(math.dot(p - a, d), math.dot(q - a, d)), 0f, len);
                float t1 = math.clamp(math.max(math.dot(p - a, d), math.dot(q - a, d)), 0f, len);
                if (t1 - t0 < 0.05f) return false;
                spans.Add((t0, t1));
                why?.Add($"{NameOf(EntityManager.GetComponentData<PrefabRef>(by).m_Prefab)}{(EntityManager.HasComponent<Overridden>(by) ? " (hidden)" : "")} {math.abs(math.dot((p + q) * 0.5f - a, n)):F2} m off, {t0:F1}-{t1:F1} of {len:F1} m");
                return Covered();
            }
            var lo = math.min(a, b) - 1.5f;
            var hi = math.max(a, b) + 1.5f;
            var owner = EntityManager.HasComponent<Owner>(e) ? EntityManager.GetComponentData<Owner>(e).m_Owner : Entity.Null;
            var found = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                _objectSearch ??= World.GetExistingSystemManaged<global::Game.Objects.SearchSystem>();
                if (_objectSearch != null)
                {
                    var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BoundsIterator { Bounds = new Bounds2(lo, hi), Results = found };
                    tree.Iterate(ref it);
                    for (int i = 0; i < found.Length; i++)
                    {
                        var o = found[i];
                        if (o == e || !EntityManager.Exists(o) || EntityManager.HasComponent<Deleted>(o) || EntityManager.HasComponent<Temp>(o)) continue;
                        // a piece its garden no longer wants (deleted next frame) doubles nothing: the new piece taking its
                        // place was kept hidden for good
                        if (_goingSoon.Contains(o)) continue;
                        // the same garden's own pieces: a run's last piece is laid over the one before it so it ends exactly
                        // where the run does (YardStitch.LayRun); hidden as a double, it left a hole at the end of the run
                        if (owner != Entity.Null && EntityManager.HasComponent<PolishPiece>(o) && EntityManager.HasComponent<Owner>(o) && EntityManager.GetComponentData<Owner>(o).m_Owner == owner) continue;
                        if (!PieceSegment(o, out var p, out var q, out bool fence) || !fence) continue;
                        // one that is hidden too only counts when it is one of ours that will be shown: it comes first (of two
                        // hidden ones, one is shown) and is not left hidden itself. A hidden fence of a house's own model, or one
                        // of ours left hidden for its own reason, is never drawn: counted, ours was left hidden as its double and
                        // neither stood there, ours only seen when the house was hovered (the user, 23 Oct: "there's no fence at
                        // all there, and then I hover and then there's a fence")
                        if (EntityManager.HasComponent<Overridden>(o) && (o.Index > e.Index || !EntityManager.HasComponent<PolishPiece>(o) || _keptHidden.ContainsKey(o))) continue;
                        if (Along(p, q, o)) return true;
                    }
                }
                found.Clear();
                _netSearch ??= World.GetExistingSystemManaged<global::Game.Net.SearchSystem>();
                if (_netSearch != null)
                {
                    var tree = _netSearch.GetLaneSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BoundsIterator { Bounds = new Bounds2(lo, hi), Results = found };
                    tree.Iterate(ref it);
                    for (int i = 0; i < found.Length; i++)
                    {
                        var l = found[i];
                        if (!EntityManager.Exists(l) || EntityManager.HasComponent<Deleted>(l) || EntityManager.HasComponent<Overridden>(l) || !EntityManager.HasComponent<Curve>(l)) continue;
                        if (!EntityManager.HasComponent<global::Game.Net.UtilityLane>(l) || !IsFence(EntityManager.GetComponentData<PrefabRef>(l).m_Prefab)) continue;
                        var c = EntityManager.GetComponentData<Curve>(l);
                        if (c.m_Length < 0.3f) continue;
                        for (int k = 0; k < 4; k++)
                            if (Along(MathUtils.Position(c.m_Bezier, k / 4f).xz, MathUtils.Position(c.m_Bezier, (k + 1) / 4f).xz, l)) return true;
                    }
                }
            }
            finally { found.Dispose(); }
            return false;
        }

        /// <summary>For the game runner: why one of our pieces is hidden now (what lies along it, or a road or lot under it).</summary>
        internal string WhyHidden(Entity e)
        {
            if (!PieceSegment(e, out var a, out var b, out bool fence)) return "no shape";
            var why = new List<string>();
            bool doubled = fence && Doubled(e, a, b, why);
            var under = new List<string>();
            bool road = OnRoadOrBuilding(e, (a + b) * 0.5f, under);
            string name = NameOf(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab);
            var owner = EntityManager.HasComponent<Owner>(e) ? EntityManager.GetComponentData<Owner>(e).m_Owner : Entity.Null;
            string whose = owner == Entity.Null || !EntityManager.Exists(owner) ? "no owner" : $"owner #{owner.Index} {NameOf(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab)}";
            return $"{name} ({math.distance(a, b):F1} m) at ({(a.x + b.x) * 0.5f:F1}, {(a.y + b.y) * 0.5f:F1}), {whose}: {(_keptHidden.ContainsKey(e) ? "kept hidden" : "not judged yet")}, "
                + $"{(doubled ? "doubled" : "not doubled")}{(why.Count > 0 ? " [" + string.Join("; ", why) + "]" : "")}{(road ? ", on " + string.Join(", ", under) : "")}";
        }

        // (a building's lot a metre in, and a road piece's surfaces, kept while the building stands where it was and the road's
        // geometry is the same: looked up for every piece the game hides)
        private readonly Dictionary<Entity, (float3 At, Entity Prefab, List<float2[]> Inner)> _lotInsets = new Dictionary<Entity, (float3, Entity, List<float2[]>)>();
        private readonly Dictionary<Entity, RoadShape> _surfaceShapes = new Dictionary<Entity, RoadShape>();

        private List<float2[]> LotInset(Entity building)
        {
            var at = EntityManager.HasComponent<global::Game.Objects.Transform>(building) ? EntityManager.GetComponentData<global::Game.Objects.Transform>(building).m_Position : default;
            var prefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
            if (_lotInsets.TryGetValue(building, out var c) && c.At.Equals(at) && c.Prefab == prefab) return c.Inner;
            BuildingFootprints.Of(EntityManager, building, out var lot, out _);
            var inner = lot == null || lot.Length < 3 ? new List<float2[]>() : Poly.Offset(Poly.EnsureCcw(lot), 1f);
            if (_lotInsets.Count > 20000) _lotInsets.Clear();
            _lotInsets[building] = (at, prefab, inner);
            return inner;
        }

        private List<float2[]> SurfaceRings(Entity road, List<(float2[] Ring, bool Round)> pieces)
        {
            if (!EntityManager.HasComponent<EdgeGeometry>(road))
            {
                pieces.Clear();
                RoadSurfaces.Pieces(EntityManager, road, pieces);
                return pieces.Select(x => x.Ring).ToList();
            }
            var c = Cached(_surfaceShapes, road, out var now);
            if (c != null) return c.Rings;
            pieces.Clear();
            RoadSurfaces.Pieces(EntityManager, road, pieces);
            now.Rings = pieces.Select(x => x.Ring).ToList();
            return now.Rings;
        }

        /// <summary>The point is on a road, path or track surface, or more than a metre inside the lot of a building that is
        /// not the piece's own house.</summary>
        private bool OnRoadOrBuilding(Entity e, float2 p, List<string> what = null)
        {
            var owner = EntityManager.HasComponent<Owner>(e) ? EntityManager.GetComponentData<Owner>(e).m_Owner : Entity.Null;
            var found = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                var box = new Bounds2(p - 1f, p + 1f);
                _objectSearch ??= World.GetExistingSystemManaged<global::Game.Objects.SearchSystem>();
                if (_objectSearch != null)
                {
                    var tree = _objectSearch.GetStaticSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BoundsIterator { Bounds = box, Results = found };
                    tree.Iterate(ref it);
                    for (int i = 0; i < found.Length; i++)
                    {
                        var o = found[i];
                        if (o == owner || !EntityManager.Exists(o) || EntityManager.HasComponent<Deleted>(o) || !BuildingFootprints.IsBuilding(EntityManager, o)) continue;
                        foreach (var r in LotInset(o))
                            if (Poly.Contains(r, p))
                            {
                                what?.Add($"the lot of #{o.Index} {NameOf(EntityManager.GetComponentData<PrefabRef>(o).m_Prefab)}{(IsYes(o) ? " (outgrown)" : "")}");
                                return true;
                            }
                    }
                }
                found.Clear();
                _netSearch ??= World.GetExistingSystemManaged<global::Game.Net.SearchSystem>();
                if (_netSearch != null)
                {
                    var tree = _netSearch.GetNetSearchTree(true, out var deps);
                    deps.Complete();
                    var it = new BoundsIterator { Bounds = box, Results = found };
                    tree.Iterate(ref it);
                    var pieces = new List<(float2[] Ring, bool Round)>();
                    for (int i = 0; i < found.Length; i++)
                    {
                        var n = found[i];
                        if (!EntityManager.Exists(n) || !EntityManager.HasComponent<Edge>(n) || EntityManager.HasComponent<Deleted>(n) || !EntityManager.HasComponent<PrefabRef>(n)) continue;
                        // walked, driven or ridden on (not a pipe or a power line under or over it)
                        var np = EntityManager.GetComponentData<PrefabRef>(n).m_Prefab;
                        if (!EntityManager.HasComponent<Road>(n) && !EntityManager.HasComponent<PathwayData>(np) && !EntityManager.HasComponent<TrainTrack>(n) && !EntityManager.HasComponent<TramTrack>(n)) continue;
                        foreach (var ring in SurfaceRings(n, pieces))
                            if (Poly.Contains(ring, p))
                            {
                                what?.Add($"the road #{n.Index} {NameOf(np)}");
                                return true;
                            }
                    }
                }
            }
            finally { found.Dispose(); }
            return false;
        }
    }
}
