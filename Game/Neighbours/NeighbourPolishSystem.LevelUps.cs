// Placemaker neighbourhood polish — buildings that level up, and what a loaded city remembers (GPL-3.0).
//
// A level-up swaps the building for another model of the next level at once (BuildingUpkeepSystem, then
// BuildingConstructionSystem): its own fences, props and surfaces are made again from the new model. Each was planned again
// with its whole neighbourhood (150 to 260 houses); in the user's city of 15 000 that was most of what Outgrow planned while
// nothing was built, and the garbage of those plans stopped the game (about 50 ms each time).
//
// "Auto outgrow when a building levels up" (this city's switch, off by default; on, each level-up is planned) keeps that. Off, the building keeps the
// garden it has and nothing is planned for it: its pieces, lawn, beds and paths are made again where they stood (what lay
// inside the lot is the new model's ground), and its fences stand as they stood. Each of the new model's fences that lies where
// one of the old model's stood takes that one's place (where the plan had moved it, hidden, or where the model has it); a new
// fence the old garden had nowhere is hidden; an old fence no new one lines up with is laid in pieces where it stood.
//
// "Auto outgrow when a building changes" (off by default): off, a building the game marks Updated without another
// model (abandoned, condemned, its condition) gets its fences back where the last plan put them (the game fits them again to
// its model), without a plan. "Auto outgrow when a building nearby is removed" (off by default): off, the gardens
// around a building the game removes (collapsed, condemned, demolished) keep theirs. The player's own bulldozer, painting,
// types and builds always plan; so does a building site on garden land (else a fence would run through the new building).
//
// After a load every outgrown house's model and fences are remembered from where they stand (SeedLaneMemory): before, every
// fence the game touched in a loaded city was taken for a change and planned again, and a level-up was not told from a fence
// the game only looked at.

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
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
        /// <summary>"Outgrow again when a building levels up" (this city's; saved with it).</summary>
        public static bool PolishLevelUps;
        /// <summary>"Outgrow again when the game changes a building" otherwise (its model the same: abandoned, condemned, its
        /// condition; the game fits its fences again): off, its fences are put back where they were (PutBack).</summary>
        public static bool PolishGameChanges;
        /// <summary>"Outgrow again when the game removes a building nearby" (collapsed, condemned, demolished; not the player's
        /// bulldozer): off, the gardens around keep theirs.</summary>
        public static bool PolishGameRemovals;
        /// <summary>For the game runner: fences put back without a plan, removals by the game the gardens around kept theirs.</summary>
        public static int TotalFencesPutBack, TotalRemovalsKept, TotalNeighbourChangesKept;

        // where a plan put each fence of an outgrown house (moved, or hidden), by the lane's place in the model it was put for
        private readonly Dictionary<Entity, (Entity Prefab, Dictionary<int, Bezier4x3> Wants)> _wantOf = new Dictionary<Entity, (Entity, Dictionary<int, Bezier4x3>)>();
        /// <summary>A fence of the old model of a building that levelled up: where the model had it, where it stood (null: there),
        /// its line, and whether a fence of the new model took its place.</summary>
        private sealed class OldFence
        {
            public Bezier4x3 Original;
            public Bezier4x3? Want;
            public string Name;
            public bool Taken;
        }

        // level-ups kept lately (the new model's fences, pieces and surfaces come in the same frame or soon after), with the old
        // model's fences, and whether the old ones nothing took the place of were laid in pieces yet
        private readonly Dictionary<Entity, (int Frame, List<OldFence> Old, bool Laid)> _keptLevelUps = new Dictionary<Entity, (int, List<OldFence>, bool)>();
        private const int KeptLevelUpFrames = 30;

        /// <summary>For the game runner: level-ups kept without a plan, fences carried over to the new model, fences an earlier
        /// session moved remembered after a load.</summary>
        public static int TotalLevelUpsKept, TotalFencesCarried, TotalFencesHidden, TotalFencesLaid, TotalSeeded;
        /// <summary>For the game runner: surfaces of kept level-ups made again (the house's own lawn apart), and their pieces.</summary>
        public static int TotalSurfacesKept, TotalLawnsKept, TotalSurfacePiecesKept;
        /// <summary>For the game runner: where the last level-ups kept stand.</summary>
        public static readonly List<float2> KeptLevelUpSpots = new List<float2>();

        /// <summary>The lane's place in its building's model (LaneSystem: middle node = 65535 - index).</summary>
        private int LaneIndex(Entity lane) => 65535 - EntityManager.GetComponentData<Lane>(lane).m_MiddleNode.GetLaneIndex();

        /// <summary>Where a model has its k-th lane on a house standing at t (as LaneSystem places it: every control point
        /// through the house's transform).</summary>
        private bool OriginalOf(Entity prefab, global::Game.Objects.Transform t, int k, out Bezier4x3 original)
        {
            original = default;
            if (!EntityManager.HasBuffer<global::Game.Prefabs.SubLane>(prefab)) return false;
            var lanes = EntityManager.GetBuffer<global::Game.Prefabs.SubLane>(prefab, true);
            if (k < 0 || k >= lanes.Length) return false;
            var c = lanes[k].m_Curve;
            original = new Bezier4x3(global::Game.Objects.ObjectUtils.LocalToWorld(t, c.a), global::Game.Objects.ObjectUtils.LocalToWorld(t, c.b),
                global::Game.Objects.ObjectUtils.LocalToWorld(t, c.c), global::Game.Objects.ObjectUtils.LocalToWorld(t, c.d));
            return true;
        }

        /// <summary>The model of each house a pass edits, as it gathers them.</summary>
        private Dictionary<long, Entity> PrefabsOf(HashSet<long> coreIds, Dictionary<long, Entity> houseOf)
        {
            var of = new Dictionary<long, Entity>();
            foreach (var id in coreIds)
                if (houseOf.TryGetValue(id, out var house) && EntityManager.HasComponent<PrefabRef>(house)) of[id] = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            return of;
        }

        /// <summary>The house a lane of a plan belongs to.</summary>
        private static Entity LaneHouse(Pending pend, long laneId) =>
            pend.OwnerOf.TryGetValue(laneId, out var houseId) && pend.HouseOf.TryGetValue(houseId, out var house) ? house : Entity.Null;

        /// <summary>A plan put this lane of the house here (moved or hidden), or left it where the model has it (want null).</summary>
        private void NoteWant(Entity house, Entity lane, Bezier4x3? want)
        {
            if (house == Entity.Null || !EntityManager.HasComponent<Lane>(lane) || !EntityManager.HasComponent<PrefabRef>(house)) return;
            var prefab = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            if (!_wantOf.TryGetValue(house, out var w) || w.Prefab != prefab) _wantOf[house] = w = (prefab, new Dictionary<int, Bezier4x3>());
            if (want.HasValue) w.Wants[LaneIndex(lane)] = want.Value;
            else w.Wants.Remove(LaneIndex(lane));
        }

        /// <summary>After a load: every outgrown house's model, and its fences as they stand (the ones an earlier session moved
        /// or hid are its plan's).</summary>
        private void SeedLaneMemory()
        {
            int seeded = 0;
            using (var all = _flaggedLots.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    if (!EntityManager.GetComponentData<PolishedLot>(e).Yes || !EntityManager.HasComponent<global::Game.Buildings.Building>(e) || !EntityManager.HasBuffer<global::Game.Net.SubLane>(e)) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(e);
                    _prefabSeen[e] = prefab;
                    _plannedModel[e] = prefab;
                    // (every outgrown house remembered, also one whose fences all stand where its model has them)
                    if (!_wantOf.ContainsKey(e)) _wantOf[e] = (prefab, new Dictionary<int, Bezier4x3>());
                    foreach (var sl in EntityManager.GetBuffer<global::Game.Net.SubLane>(e, true))
                    {
                        var lane = sl.m_SubLane;
                        if (!EntityManager.Exists(lane) || !EntityManager.HasComponent<global::Game.Net.UtilityLane>(lane) || !EntityManager.HasComponent<Lane>(lane) || !EntityManager.HasComponent<Curve>(lane)) continue;
                        if (!IsFence(EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab)) continue;
                        var curve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                        _laneCurve[lane] = curve;
                        if (!OriginalOf(prefab, t, LaneIndex(lane), out var original) || IsOriginal(curve, original)) continue;
                        _written[lane] = curve;
                        NoteWant(e, lane, curve);
                        seeded++;
                    }
                }
            TotalSeeded += seeded;
            Mod.Log.Info($"Placemaker neighbourhood polish: {seeded} fences an earlier session moved or hid remembered where they stand");
        }

        /// <summary>The game fitted a fence of the house again (marked Updated) without changing its model, and changes of the
        /// game plan nothing in this city: the fence goes back where the last plan put it (or stays where the model has it, when
        /// the plan left it there); true when it is accounted for (no plan). False: nothing is known of it (planned again).</summary>
        private bool PutBack(Entity lane, Entity house, Bezier4x3 now)
        {
            if (PolishGameChanges || !IsYes(house) || !SameModel(house) || !EntityManager.HasComponent<Lane>(lane)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            if (!_wantOf.TryGetValue(house, out var w) || w.Prefab != prefab) return false;
            int k = LaneIndex(lane);
            if (!w.Wants.TryGetValue(k, out var want))
            {
                // the plan left it where the model has it: is it there?
                if (!EntityManager.HasComponent<global::Game.Objects.Transform>(house)) return false;
                if (!OriginalOf(prefab, EntityManager.GetComponentData<global::Game.Objects.Transform>(house), k, out var original) || !IsOriginal(now, original)) return false;
                _laneCurve[lane] = now;
                return true;
            }
            StandingChanged(house);
            if (!Same(now, want))
            {
                var cur = EntityManager.GetComponentData<Curve>(lane);
                cur.m_Bezier = want;
                cur.m_Length = MathUtils.Length(want);
                EntityManager.SetComponentData(lane, cur);
                EntityManager.AddComponent<Updated>(lane);
                TotalFencesPutBack++;
            }
            _laneCurve[lane] = want;
            _written[lane] = want;
            return true;
        }

        /// <summary>A fence of a building that is not outgrown changed: the game levelled it up (another model than it was seen
        /// with) or changed it otherwise, and this city plans nothing for that (true: the outgrown ones around keep theirs). A
        /// new building (never seen, its lane just made) is not the game changing one: its neighbours are planned.</summary>
        private bool NeighbourChangeKept(Entity lane, Entity building, Bezier4x3 now)
        {
            if (!EntityManager.HasComponent<PrefabRef>(building)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
            bool seen = _prefabSeen.TryGetValue(building, out var was);
            if (!seen && EntityManager.HasComponent<Created>(lane)) return false;
            if (seen && was != prefab ? PolishLevelUps : PolishGameChanges) return false;
            _prefabSeen[building] = prefab;
            _laneCurve[lane] = now;
            TotalNeighbourChangesKept++;
            return true;
        }

        /// <summary>The house's model is the one it was last planned with (or remembered with after a load).</summary>
        private bool SameModel(Entity house) =>
            _prefabSeen.TryGetValue(house, out var seen) && EntityManager.HasComponent<PrefabRef>(house) && seen == EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;

        /// <summary>The house levelled up (another model than it was planned with) and levelling up plans nothing in this city:
        /// it keeps its garden (true; also for a while after, while the new model's pieces come). Its old model's fences, as they
        /// stood, are kept for the new ones to take their places (CarryFence, LayOldFences).</summary>
        private bool KeepLevelUp(Entity house)
        {
            if (PolishLevelUps || !IsYes(house) || !_prefabSeen.TryGetValue(house, out var old) || !EntityManager.HasComponent<PrefabRef>(house)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(house).m_Prefab;
            if (old == prefab) return _keptLevelUps.TryGetValue(house, out var kept) && LotFrame - kept.Frame < KeptLevelUpFrames;
            if (EntityManager.HasComponent<global::Game.Objects.UnderConstruction>(house) || !EntityManager.HasComponent<global::Game.Objects.Transform>(house)) return false;
            var t = EntityManager.GetComponentData<global::Game.Objects.Transform>(house);
            var fences = new List<OldFence>();
            var wants = _wantOf.TryGetValue(house, out var w) && w.Prefab == old ? w.Wants : null;
            if (EntityManager.HasBuffer<global::Game.Prefabs.SubLane>(old))
            {
                var lanes = EntityManager.GetBuffer<global::Game.Prefabs.SubLane>(old, true);
                for (int k = 0; k < lanes.Length; k++)
                {
                    if (!IsFence(lanes[k].m_Prefab) || !OriginalOf(old, t, k, out var original)) continue;
                    fences.Add(new OldFence { Original = original, Want = wants != null && wants.TryGetValue(k, out var want) ? want : (Bezier4x3?)null, Name = NameOf(lanes[k].m_Prefab) });
                }
            }
            _wantOf.Remove(house);
            _prefabSeen[house] = prefab;
            _described.Remove(house);
            if (_keptLevelUps.Count > 4096) _keptLevelUps.Clear();
            _keptLevelUps[house] = (LotFrame, fences, false);
            TotalLevelUpsKept++;
            if (KeptLevelUpSpots.Count > 200) KeptLevelUpSpots.RemoveRange(0, 100);
            KeptLevelUpSpots.Add(t.m_Position.xz);
            return true;
        }

        /// <summary>A fence of a kept level-up's new model (where the model has it): where one of the old model's stood (either
        /// way round), it takes that one's place: where the plan had moved or hidden it, or left where it is. A fence the old
        /// garden had nowhere is hidden.</summary>
        private void CarryFence(Entity lane, Entity house, Bezier4x3 now)
        {
            StandingChanged(house);
            _laneCurve[lane] = now;
            _written.Remove(lane);
            if (!_keptLevelUps.TryGetValue(house, out var kept)) return;
            var reversed = new Bezier4x3(now.d, now.c, now.b, now.a);
            Bezier4x3 put;
            var old = kept.Old.Find(f => !f.Taken && (IsOriginal(now, f.Original) || IsOriginal(reversed, f.Original)));
            if (old != null)
            {
                old.Taken = true;
                TotalFencesCarried++;
                if (!old.Want.HasValue) { NoteWant(house, lane, null); return; }
                put = old.Want.Value;
            }
            else
            {
                // as FinishPlan hides one: a 2 cm stub 8 m under the ground
                var mid = (now.a + now.d) * 0.5f - new float3(0f, 8f, 0f);
                var dir = math.normalizesafe(now.d - now.a, new float3(1f, 0f, 0f)) * 0.01f;
                put = NetUtils.StraightCurve(mid - dir, mid + dir);
                TotalFencesHidden++;
            }
            var cur = EntityManager.GetComponentData<Curve>(lane);
            cur.m_Bezier = put;
            cur.m_Length = MathUtils.Length(put);
            EntityManager.SetComponentData(lane, cur);
            EntityManager.AddComponent<Updated>(lane);
            _laneCurve[lane] = put;
            _written[lane] = put;
            NoteWant(house, lane, put);
        }

        /// <summary>The old fences of the level-ups kept this frame that no new fence took the place of, laid in pieces where they
        /// stood (a hidden one stays gone). After the new model's fences came (CollectChanged), before its pieces are refilled.</summary>
        private void LayOldFences()
        {
            if (_keptLevelUps.Count == 0) return;
            List<Entity> laid = null;
            foreach (var kv in _keptLevelUps)
            {
                if (kv.Value.Laid) continue;
                (laid ??= new List<Entity>()).Add(kv.Key);
                if (!EntityManager.Exists(kv.Key) || EntityManager.HasComponent<Deleted>(kv.Key)) continue;
                foreach (var f in kv.Value.Old)
                {
                    if (f.Taken) continue;
                    var at = f.Want ?? f.Original;
                    if (MathUtils.Length(at) < 0.1f) continue;   // hidden
                    foreach (var (p, yaw, name) in YardStitch.PiecesAlong(at.a.xz, at.d.xz, f.Name))
                    {
                        NeighbourObjectSystem.Queue.Add(new NeighbourObjectSystem.Request
                        {
                            Name = NeighbourObjectSystem.PieceName(name), Position = p, Yaw = yaw, Owner = kv.Key, Seed = PieceSeed(kv.Key, name),
                        });
                        TotalFencesLaid++;
                    }
                }
            }
            if (laid != null) foreach (var e in laid) { var k = _keptLevelUps[e]; _keptLevelUps[e] = (k.Frame, k.Old, true); }
        }

        private bool Kept(Entity house) => _keptLevelUps.TryGetValue(house, out var kept) && LotFrame - kept.Frame < KeptLevelUpFrames;

        /// <summary>The house's lot (its model's lot size around where it stands).</summary>
        private float2[] LotQuad(Entity house)
        {
            BuildingFootprints.Of(EntityManager, house, out var lot, out _);
            return lot;
        }

        /// <summary>A piece of a kept level-up's that stood inside its lot (half a metre in): the new model's ground, not made
        /// again.</summary>
        private bool InsideKeptLot(Entity house, float2 at)
        {
            if (!Kept(house)) return false;
            var lot = LotQuad(house);
            if (lot == null || lot.Length < 3) return false;
            foreach (var inner in Poly.Offset(Poly.EnsureCcw(lot), 0.5f)) if (Poly.Contains(inner, at)) return true;
            return false;
        }

        /// <summary>A surface of a kept level-up's that the game deleted with the old model (our beds, paths and lawns, and the
        /// house's own lawn as Outgrow grew it): what of it lay past the lot made again where it stood, as one of the house's
        /// garden pieces (inside the lot is the new model's ground).</summary>
        private bool KeepSurface(Entity area, Entity house, PolishPieceKind kind)
        {
            if (!Kept(house)) return false;
            if (!EntityManager.HasBuffer<global::Game.Areas.Node>(area) || !EntityManager.HasComponent<PrefabRef>(area)) return false;
            var nodes = EntityManager.GetBuffer<global::Game.Areas.Node>(area, true);
            if (nodes.Length < 3) return false;
            var ring = new float2[nodes.Length];
            for (int i = 0; i < ring.Length; i++) ring[i] = nodes[i].m_Position.xz;
            if (ring.Length > 3 && math.all(math.abs(ring[0] - ring[ring.Length - 1]) < 0.001f)) System.Array.Resize(ref ring, ring.Length - 1);
            var prefab = EntityManager.GetComponentData<PrefabRef>(area).m_Prefab;
            var lot = LotQuad(house);
            var parts = lot == null || lot.Length < 3
                ? new List<Region> { new Region(ring, new List<float2[]>()) }
                : Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(ring) }, new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(lot) }, BoolOp.Difference, FillRule.NonZero);
            foreach (var part in parts)
            {
                if (part.Area < 1f) continue;
                foreach (var piece in part.HasHoles ? Poly.CutHoles(part) : new List<float2[]> { part.Outer })
                    if (piece != null && piece.Length >= 3 && Poly.Area(piece) >= 1f)
                    {
                        NeighbourAreaSystem.Queue.Add(new NeighbourAreaSystem.Request { Prefab = prefab, Ring = piece, Owner = house, Kind = kind });
                        TotalSurfacePiecesKept++;
                    }
            }
            if (EntityManager.HasComponent<PolishPiece>(area)) TotalSurfacesKept++;
            else TotalLawnsKept++;
            return true;
        }
    }
}
