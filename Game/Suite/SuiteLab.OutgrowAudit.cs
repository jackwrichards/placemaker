// Game runner lab: outgrown buildings that have nothing (the lens shows them outgrown, no garden, yard or car park was built
// for them, or what was built went later), counted from data:
//   {"do":"lab","op":"outgrowAudit"}    every growable building in the land: outgrown or not, ever planned, its garden past
//                                       its lot, the pieces it owns; the outgrown ones with nothing, by kind and look
//   {"do":"lab","op":"outgrowRepaint","only":"empty","wait":120}
//       the user's work-around (painting over them again fixes them): the outgrown buildings with no pieces (or "all") are
//       stamped again with the look they have, Outgrow runs until idle, and the record says how many of them now have
//       pieces (they were empty by a fault, not by their land) and how many others lost theirs.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Neighbours;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        /// <summary>Every finished growable building in the land with what the polish gave it.</summary>
        private Dictionary<Entity, NeighbourPolishSystem.Audit> OutgrowAudits(ScenarioState st, NeighbourPolishSystem sys)
        {
            var em = _world.EntityManager;
            var b = st.S.Block;
            var map = new Dictionary<Entity, NeighbourPolishSystem.Audit>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                {
                    var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                    if (p.x < b.X0 || p.x > b.X1 || p.z < b.Z0 || p.z > b.Z1) continue;
                    var prefab = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!em.HasComponent<SpawnableBuildingData>(prefab) || !em.HasComponent<BuildingData>(prefab)) continue;
                    map[e] = sys.AuditOf(e);
                }
            return map;
        }

        private JObject OutgrowAuditRecord(ScenarioState st, Dictionary<Entity, NeighbourPolishSystem.Audit> map, string label)
        {
            var em = _world.EntityManager;
            int yes = 0, building = 0, planned = 0, lotOnly = 0, withPieces = 0, pieces = 0;
            var problems = new Dictionary<string, int>();
            var byLook = new Dictionary<string, (int Yes, int Pieces, int Empty)>();
            var worst = new JArray();
            foreach (var kv in map)
            {
                var a = kv.Value;
                if (!a.Yes) continue;
                yes++;
                if (a.Building) { building++; continue; }
                if (a.Planned) planned++;
                if (a.Planned && a.GardenM2 <= a.LotM2 + 5f) lotOnly++;
                int n = a.Areas + a.Objects;
                pieces += n;
                if (n > 0) withPieces++;
                string key = (a.Kind ?? "?") + "/" + (string.IsNullOrEmpty(a.Look) ? "-" : a.Look);
                var bl = byLook.TryGetValue(key, out var x) ? x : default;
                bl.Yes++;
                if (n > 0) bl.Pieces++; else bl.Empty++;
                byLook[key] = bl;
                var why = NeighbourPolishSystem.EmptyReason(a);
                if (why == null) continue;
                problems[why] = (problems.TryGetValue(why, out var c) ? c : 0) + 1;
                if (worst.Count < 40)
                {
                    var p = em.GetComponentData<global::Game.Objects.Transform>(kv.Key).m_Position;
                    worst.Add(new JObject
                    {
                        ["house"] = $"{kv.Key.Index}:{kv.Key.Version}", ["at"] = new JArray(Math.Round(p.x, 1), Math.Round(p.z, 1)), ["kind"] = a.Kind, ["look"] = a.Look, ["why"] = why,
                        ["lotM2"] = Math.Round(a.LotM2), ["gardenM2"] = Math.Round(a.GardenM2), ["areas"] = a.Areas, ["objects"] = a.Objects, ["lawns"] = a.LawnsChanged, ["lines"] = a.LinesMoved,
                    });
                }
            }
            var rec = new JObject
            {
                ["outgrown"] = yes, ["underConstruction"] = building, ["planned"] = planned, ["plotOnlyItsLot"] = lotOnly, ["withPieces"] = withPieces, ["pieces"] = pieces,
                ["neverPlanned"] = problems.TryGetValue("neverPlanned", out var np) ? np : 0,
                ["landButNothing"] = problems.TryGetValue("landButNothing", out var ln) ? ln : 0,
                ["carParkLookNoPaving"] = problems.TryGetValue("carParkLookNoPaving", out var cp) ? cp : 0,
                ["emptiedByAPlan"] = new JObject { ["asked"] = NeighbourPolishSystem.TotalEmptiedAsked, ["neighbour"] = NeighbourPolishSystem.TotalEmptiedNeighbour },
                ["droppedBeforePlan"] = NeighbourPolishSystem.TotalDropped,
                ["gameTookPieces"] = new JObject { ["objects"] = NeighbourPolishSystem.TotalLostObjects, ["surfaces"] = NeighbourPolishSystem.TotalLostAreas, ["objectsMadeAgain"] = NeighbourPolishSystem.TotalRemade },
                ["rebuiltPlannedAgain"] = NeighbourPolishSystem.TotalRebuilt,
                ["emptyAfterLoadPlannedAgain"] = NeighbourPolishSystem.TotalHealed,
                ["byKindLook"] = new JObject(byLook.OrderBy(k => k.Key).Select(k => new JProperty(k.Key, $"{k.Value.Yes} outgrown, {k.Value.Pieces} with pieces, {k.Value.Empty} without"))),
                ["problems"] = worst,
            };
            Mod.Log.Info($"Placemaker lab: outgrow audit{(label != null ? " (" + label + ")" : "")}: {yes} outgrown ({building} being built), {planned} planned, {lotOnly} with a plot of only their lot, "
                + $"{withPieces} with pieces ({pieces} pieces); never planned {rec["neverPlanned"]}, land past the lot but nothing on it {rec["landButNothing"]}, "
                + $"car park look with land and no paving {rec["carParkLookNoPaving"]}; plans that took every piece away: {NeighbourPolishSystem.TotalEmptiedAsked} asked, "
                + $"{NeighbourPolishSystem.TotalEmptiedNeighbour} as a neighbour; {NeighbourPolishSystem.TotalDropped} dirty houses dropped before a plan; "
                + $"the game took {NeighbourPolishSystem.TotalLostObjects} objects and {NeighbourPolishSystem.TotalLostAreas} surfaces of ours from standing buildings ({NeighbourPolishSystem.TotalRemade} objects made again), "
                + $"{NeighbourPolishSystem.TotalRebuilt} rebuilt buildings planned again, {NeighbourPolishSystem.TotalHealed} found empty after a load");
            foreach (var kv in byLook.OrderBy(k => k.Key)) Mod.Log.Info($"Placemaker lab: outgrow audit: {kv.Key}: {kv.Value.Yes} outgrown, {kv.Value.Pieces} with pieces, {kv.Value.Empty} without");
            foreach (var w in worst.Take(20)) Mod.Log.Info("Placemaker lab: outgrow audit: " + w.ToString(Newtonsoft.Json.Formatting.None));
            return rec;
        }

        private IEnumerator LabOutgrowAudit(ScenarioState st, JObject step, JObject rec)
        {
            yield return Frames(20);
            var sys = _world.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (sys == null) { rec["error"] = "lab: Outgrow is not registered"; yield break; }
            var r = OutgrowAuditRecord(st, OutgrowAudits(st, sys), (string)step["label"]);
            foreach (var p in r.Properties()) rec[p.Name] = p.Value;
        }

        private IEnumerator LabOutgrowRepaint(ScenarioState st, JObject step, JObject rec)
        {
            yield return Frames(20);
            var sys = _world.GetExistingSystemManaged<NeighbourPolishSystem>();
            if (sys == null) { rec["error"] = "lab: Outgrow is not registered"; yield break; }
            var em = _world.EntityManager;
            bool onlyEmpty = ((string)step["only"] ?? "empty").ToLowerInvariant() != "all";
            var before = OutgrowAudits(st, sys);
            // the look each one has (a look the lot took from its zone is stamped "zone" again, as the brush's Set to defaults)
            var byLook = new Dictionary<string, List<Entity>>();
            int asked = 0;
            foreach (var kv in before)
            {
                var a = kv.Value;
                if (!a.Yes || a.Building || onlyEmpty && a.Areas + a.Objects > 0) continue;
                var f = em.GetComponentData<PolishedLot>(kv.Key);
                string look = f.Picked && !string.IsNullOrEmpty(a.Look) ? a.Look : "zone";
                if (!byLook.TryGetValue(look, out var list)) byLook[look] = list = new List<Entity>();
                list.Add(kv.Key);
                asked++;
            }
            foreach (var kv in byLook) NeighbourPolishSystem.Stamp(kv.Value, true, kv.Key, null);
            Mod.Log.Info($"Placemaker lab: outgrow repaint: {asked} outgrown buildings{(onlyEmpty ? " with no pieces" : "")} stamped again with their look");
            yield return PolishIdle(rec, (float?)step["wait"] ?? 120f);
            var after = OutgrowAudits(st, sys);
            int gained = 0, lost = 0, stillEmpty = 0, piecesBefore = 0, piecesAfter = 0;
            var gainedList = new JArray();
            foreach (var kv in before)
            {
                if (!kv.Value.Yes || kv.Value.Building || !after.TryGetValue(kv.Key, out var b)) continue;
                int n0 = kv.Value.Areas + kv.Value.Objects, n1 = b.Areas + b.Objects;
                piecesBefore += n0;
                piecesAfter += n1;
                if (n0 == 0 && n1 > 0)
                {
                    gained++;
                    if (gainedList.Count < 30)
                    {
                        var p = em.GetComponentData<global::Game.Objects.Transform>(kv.Key).m_Position;
                        gainedList.Add(new JObject { ["house"] = $"{kv.Key.Index}:{kv.Key.Version}", ["at"] = new JArray(Math.Round(p.x, 1), Math.Round(p.z, 1)), ["kind"] = b.Kind, ["look"] = b.Look, ["wasPlanned"] = kv.Value.Planned, ["pieces"] = n1, ["paved"] = b.Paved });
                    }
                }
                else if (n0 > 0 && n1 == 0) lost++;
                else if (n0 == 0 && n1 == 0) stillEmpty++;
            }
            rec["stamped"] = asked;
            rec["gained"] = gained;
            rec["lost"] = lost;
            rec["stillEmpty"] = stillEmpty;
            rec["piecesBefore"] = piecesBefore;
            rec["piecesAfter"] = piecesAfter;
            rec["gainedLots"] = gainedList;
            Mod.Log.Info($"Placemaker lab: outgrow repaint: {asked} stamped again; {gained} of them got pieces now (they were empty by a fault), {stillEmpty} still have none, {lost} others lost all theirs; pieces {piecesBefore} before, {piecesAfter} after");
            foreach (var g in gainedList.Take(20)) Mod.Log.Info("Placemaker lab: outgrow repaint: gained " + g.ToString(Newtonsoft.Json.Formatting.None));
            var r = OutgrowAuditRecord(st, after, "after repaint");
            rec["after"] = r;
        }
    }
}
