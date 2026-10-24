// Game runner lab (exploration, not a Placemaker feature): steps that try what a mod could do beyond Placemaker.
//   {"do":"lab","op":"materials"}  lists the vanilla building surfaces a generated shape could borrow
//   {"do":"lab","op":"mesh","shape":"wedge","size":[20,24],"height":10,"at":[0,0],"yaw":0,"material":"..."}
//       generates a mesh in code, makes it a new game object (SuiteLabShapeSystem) and places it like a vanilla prop.
// Every result is in the step's record and in Placemaker.log ("Placemaker lab: ...").

using System;
using System.Collections;
using System.Linq;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private int _labShapes;

        private IEnumerator StepLab(ScenarioState st, JObject step, JObject rec)
        {
            switch (((string)step["op"] ?? "").ToLowerInvariant())
            {
                case "street": yield return LabStreet(st, step, rec); yield break;
                case "streetjoins": LabStreetJoins(st, step, rec); yield break;
                case "pickpart": yield return LabPickPart(st, step, rec); yield break;
                case "toolstate":
                    rec["editingGroup"] = _tool.EditingGroup;
                    rec["stage"] = _tool.Stage.ToString();
                    rec["hasArea"] = _tool.Shape != null;
                    Mod.Log.Info($"Placemaker lab: tool state ({(string)step["label"] ?? ""}): editing group {_tool.EditingGroup}, stage {_tool.Stage}, area {(_tool.Shape != null ? "kept" : "none")}.");
                    yield break;
                case "yards": yield return LabYards(st, step, rec); yield break;
                case "outgrowcheck": yield return LabOutgrowCheck(st, step, rec); yield break;
                case "fencemap": yield return LabFenceMap(st, step, rec); yield break;
                case "outlineflicker": yield return LabOutlineFlicker(st, step, rec); yield break;
                case "zonerect": yield return LabZoneRect(st, step, rec); yield break;
                case "zonecells": yield return LabZoneCells(st, step, rec); yield break;
                case "zonesink": yield return LabZoneSink(step, rec); yield break;
                case "outgrowaudit": yield return LabOutgrowAudit(st, step, rec); yield break;
                case "outgrowframes": yield return LabOutgrowFrames(st, step, rec); yield break;
                case "outgrowrepaint": yield return LabOutgrowRepaint(st, step, rec); yield break;
                case "yardscheck": LabYardsCheck(rec); yield break;
                case "anatomy": LabAnatomy(st, rec); yield break;
                case "lineup": yield return LabLineup(st, step, rec); yield break;
                case "polishflags": yield return LabPolishFlags(st, rec); yield break;
                case "saveload": yield return LabSaveLoad(st, rec); yield break;
                case "olddecal": yield return LabOldDecal(st, step, rec); yield break;
                case "savecity": yield return LabSaveCity(step, rec); yield break;
                case "loadcity": yield return LabLoadCity(step, rec); yield break;
                case "grouprecords": LabGroupRecords(step, rec); yield break;
                case "oldduplicate": LabOldDuplicate(rec); yield break;
                case "olddecalcleanup":
                    Placemaker.Game.Build.OldDecalCleanupSystem.SuiteSkip = (bool?)step["on"] == false;
                    rec["oldDecalCleanup"] = !Placemaker.Game.Build.OldDecalCleanupSystem.SuiteSkip;
                    Mod.Log.Info("Placemaker lab: the old-decal clean-up after loads is " + (Placemaker.Game.Build.OldDecalCleanupSystem.SuiteSkip ? "off" : "on"));
                    yield break;
                case "removeallpolish": yield return LabRemoveAllPolish(st, rec); yield break;
                case "polishparts":
                {
                    // which parts neighbourhood polish edits: lines, lawns, both, none (isolates a crash to one kind of edit)
                    string parts = ((string)step["parts"] ?? "both").ToLowerInvariant();
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.EditLines = parts == "lines" || parts == "both";
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.EditLawns = parts == "lawns" || parts == "both";
                    rec["parts"] = parts;
                    Mod.Log.Info("Placemaker lab: neighbourhood polish edits " + parts);
                    yield break;
                }
                case "hotload":
                {
                    // a newer YardStitch (tools/hot/Build-Hot.ps1) becomes the neighbourhood-polish planner in this running game
                    string path = (string)step["path"];
                    try
                    {
                        var asm = System.Reflection.Assembly.LoadFile(path);
                        var type = asm.GetType("Placemaker.Core.Neighbours.YardStitch", true);
                        var method = type.GetMethod("Plan", new[] { typeof(System.Collections.Generic.IReadOnlyList<Placemaker.Core.Neighbours.StitchHouse>), typeof(Placemaker.Core.Neighbours.StitchSettings) });
                        if (method == null) { rec["error"] = "lab: hot planner has no Plan(houses, settings)"; yield break; }
                        Placemaker.Game.Neighbours.NeighbourPolishSystem.Planner = (Func<System.Collections.Generic.IReadOnlyList<Placemaker.Core.Neighbours.StitchHouse>, Placemaker.Core.Neighbours.StitchSettings, Placemaker.Core.Neighbours.StitchPlan>)
                            Delegate.CreateDelegate(typeof(Func<System.Collections.Generic.IReadOnlyList<Placemaker.Core.Neighbours.StitchHouse>, Placemaker.Core.Neighbours.StitchSettings, Placemaker.Core.Neighbours.StitchPlan>), method);
                        Placemaker.Game.Neighbours.NeighbourPolishSystem.Redo();
                        rec["loaded"] = asm.GetName().Name;
                        Mod.Log.Info("Placemaker lab: hot planner " + asm.GetName().Name + " loaded from " + path + ", every house again");
                    }
                    catch (Exception e) { rec["error"] = "lab: hotload failed: " + e.GetType().Name + ": " + e.Message; yield break; }
                    yield return Frames(20);
                    yield break;
                }
                case "polishsettings":
                {
                    // stitching rules changed in the running game (no rebuild, no relaunch), then every house again
                    var set = Placemaker.Game.Neighbours.NeighbourPolishSystem.Settings;
                    var applied = new JObject();
                    if (step["settings"] is JObject values)
                        foreach (var p in values.Properties())
                        {
                            var field = set.GetType().GetField(p.Name);
                            if (field == null || field.Name == "Extra") set.Extra[p.Name] = (float)p.Value;   // a knob for a hot planner
                            else field.SetValue(set, p.Value.ToObject(field.FieldType));
                            applied[p.Name] = p.Value;
                        }
                    rec["settings"] = applied;
                    // ("redo": false: the rules only, nothing planned again: a setting the game reads as it runs)
                    if ((bool?)step["redo"] == false) { Mod.Log.Info("Placemaker lab: stitching rules " + applied.ToString(Newtonsoft.Json.Formatting.None)); yield break; }
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.Redo();
                    Mod.Log.Info("Placemaker lab: stitching rules " + applied.ToString(Newtonsoft.Json.Formatting.None) + ", every house again");
                    yield return Frames(20);
                    yield break;
                }
                case "touchhouses":
                {
                    // the game rebuilds every house's fence lines in the land (as after a move or level-up): the polish must put
                    // its edits back in the same frame
                    var b = st.S.Block;
                    var em = _world.EntityManager;
                    int n = 0;
                    // inside the runner's tool update (ToolUpdate): the Modification systems of the same frame see the Updated
                    yield return ApplyWithFixtureTool(t =>
                    {
                        using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<global::Game.Common.Deleted>(), ComponentType.Exclude<global::Game.Tools.Temp>()))
                        using (var all = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                            foreach (var e in all)
                            {
                                var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                                if (p.x < b.X0 || p.x > b.X1 || p.z < b.Z0 || p.z > b.Z1) continue;
                                em.AddComponent<global::Game.Common.Updated>(e);
                                n++;
                            }
                        return n;
                    }, "touch houses");
                    rec["touched"] = n;
                    Mod.Log.Info($"Placemaker lab: {n} houses updated (their fence lines rebuilt by the game)");
                    yield return Frames(30);
                    yield break;
                }
                case "levelup":
                {
                    // every finished house in the land (or a share of them) goes up a level, as the simulation would: the
                    // polish must fit the new model's fences and lawn (run "grow" after it so the simulation builds them)
                    var labShapes = _world.GetExistingSystemManaged<SuiteLabShapeSystem>();
                    if (labShapes == null) { rec["error"] = "lab: the shape system is not registered"; yield break; }
                    var b = st.S.Block;
                    float share = (float?)step["share"] ?? 1f;
                    var em = _world.EntityManager;
                    var pick = new System.Collections.Generic.List<Entity>();
                    var rnd = new Unity.Mathematics.Random(0x5EEDu);
                    using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Buildings.BuildingCondition>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<global::Game.Objects.UnderConstruction>()))
                    using (var all = q.ToEntityArray(Allocator.Temp))
                        foreach (var e in all)
                        {
                            var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                            if (p.x < b.X0 || p.x > b.X1 || p.z < b.Z0 || p.z > b.Z1) continue;
                            if (rnd.NextFloat() <= share) pick.Add(e);
                        }
                    int n = labShapes.LevelUp(pick);
                    rec["levelUp"] = n;
                    Mod.Log.Info($"Placemaker lab: {n} houses asked to level up");
                    yield return Frames(10);
                    yield break;
                }
                case "polishstamp":
                {
                    // what the painter's Polish (yes) or Unpolish (no) does, on the lots in the land (or its west or east half)
                    bool yes = (bool?)step["yes"] ?? true;
                    string side = ((string)step["side"] ?? "all").ToLowerInvariant();
                    var b = st.S.Block;
                    float midX = (b.X0 + b.X1) * 0.5f;
                    var em = _world.EntityManager;
                    var lots = new System.Collections.Generic.List<Entity>();
                    using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                    using (var all = q.ToEntityArray(Allocator.Temp))
                        foreach (var e in all)
                        {
                            var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position;
                            if (p.x < b.X0 || p.x > b.X1 || p.z < b.Z0 || p.z > b.Z1) continue;
                            if (side == "west" && p.x > midX || side == "east" && p.x < midX) continue;
                            lots.Add(e);
                        }
                    string themeName = ((string)step["theme"] ?? "zone").ToLowerInvariant();
                    // "theme": "painter": the polish tool's look as its rows have it (what its Polish gives)
                    // "look": a look by its id with "changes" to its knobs ({"sideReach": 500, ...}), as the Outgrow brush stamps them
                    if (!string.IsNullOrEmpty((string)step["look"]))
                    {
                        var changes = new System.Collections.Generic.Dictionary<string, float>();
                        if (step["changes"] is JObject ch) foreach (var p in ch.Properties()) changes[p.Name] = (float)p.Value;
                        Placemaker.Game.Neighbours.NeighbourPolishSystem.Stamp(lots, yes, (string)step["look"], changes.Count > 0 ? changes : null);
                    }
                    else if (themeName == "painter") Placemaker.Game.Neighbours.NeighbourPolishSystem.Stamp(lots, yes, _tool.PainterTheme, yes ? _tool.PolishChanges : null);
                    else Placemaker.Game.Neighbours.NeighbourPolishSystem.Stamp(lots, yes, (byte)Math.Max(0, Array.IndexOf(Placemaker.Game.Neighbours.NeighbourPolishSystem.ThemeNames, themeName)));
                    rec["stamped"] = lots.Count;
                    Mod.Log.Info($"Placemaker lab: {lots.Count} lots stamped {(yes ? "polished" : "back as the game made them")} ({side})");
                    yield return Frames(10);
                    yield break;
                }
                case "bulldozehouses":
                {
                    // the player bulldozes some houses (every n-th in the land): the polish must give their neighbours their
                    // own fences back and leave nothing of theirs standing
                    // ("near": [x, z] with "radius": the houses that close instead, in a loaded city; "nearest": n — the n houses
                    // nearest to "near" (a row of neighbours))
                    var b = st.S.Block;
                    float bx0 = b.X0, bx1 = b.X1, bz0 = b.Z0, bz1 = b.Z1;
                    // ("rel": true — "near" from the land's middle, else in the map)
                    var nearC = step["near"] is JArray nearAt ? new float2((float)nearAt[0], (float)nearAt[1]) + ((bool?)step["rel"] == true ? b.Center : float2.zero) : float2.zero;
                    if (step["near"] is JArray)
                    {
                        float r = (float?)step["radius"] ?? 40f;
                        bx0 = nearC.x - r; bx1 = nearC.x + r; bz0 = nearC.y - r; bz1 = nearC.y + r;
                    }
                    int every = math.max(2, (int?)step["every"] ?? 4);
                    var em = _world.EntityManager;
                    var victims = new System.Collections.Generic.List<Entity>();
                    using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<global::Game.Buildings.Building>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>()))
                    using (var all = q.ToEntityArray(Allocator.Temp))
                    {
                        var inLand = all.Where(e => { var p = em.GetComponentData<global::Game.Objects.Transform>(e).m_Position; return p.x >= bx0 && p.x <= bx1 && p.z >= bz0 && p.z <= bz1; })
                            .OrderBy(e => em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.x).ThenBy(e => em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.z).ToList();
                        // "keep": n — only every n-th house stays (houses far apart); else every n-th goes
                        int keep = (int?)step["keep"] ?? 0;
                        int nearest = (int?)step["nearest"] ?? 0;
                        if (nearest > 0 && step["near"] is JArray)
                        {
                            var c = nearC;
                            victims.AddRange(inLand.Where(e => em.HasComponent<global::Game.Prefabs.SpawnableBuildingData>(em.GetComponentData<global::Game.Prefabs.PrefabRef>(e).m_Prefab))
                                .OrderBy(e => math.distance(em.GetComponentData<global::Game.Objects.Transform>(e).m_Position.xz, c)).Take(nearest));
                        }
                        else if (keep > 1) { for (int i = 0; i < inLand.Count; i++) if (i % keep != 0) victims.Add(inLand[i]); }
                        else for (int i = 1; i < inLand.Count; i += every) victims.Add(inLand[i]);
                    }
                    var dirty0 = (int[])Placemaker.Game.Neighbours.NeighbourPolishSystem.DirtyBy.Clone();
                    int kept0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalRemovalsKept;
                    // (what goes: outgrown or not, a growable or not)
                    var sysP = _world.GetExistingSystemManaged<Placemaker.Game.Neighbours.NeighbourPolishSystem>();
                    var what = string.Join(", ", victims.Select(v => $"{v.Index} at ({(em.GetComponentData<global::Game.Objects.Transform>(v).m_Position.x):F0}, {(em.GetComponentData<global::Game.Objects.Transform>(v).m_Position.z):F0}) ({(em.HasComponent<global::Game.Prefabs.SpawnableBuildingData>(em.GetComponentData<global::Game.Prefabs.PrefabRef>(v).m_Prefab) ? "growable" : "not growable")}, {(sysP != null && sysP.IsYes(v) ? "outgrown" : "not outgrown")})"));
                    yield return ApplyWithFixtureTool(t =>
                    {
                        foreach (var v in victims)
                        {
                            var def = t.NewDefinition();
                            em.AddComponentData(def, new CreationDefinition { m_Original = v, m_Flags = CreationFlags.Delete });
                            em.AddComponent<Updated>(def);
                            var tr = em.GetComponentData<global::Game.Objects.Transform>(v);
                            em.AddComponentData(def, new ObjectDefinition { m_Position = tr.m_Position, m_Rotation = tr.m_Rotation, m_LocalPosition = tr.m_Position, m_LocalRotation = tr.m_Rotation, m_ParentMesh = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_PrefabSubIndex = -1 });
                        }
                        return victims.Count;
                    }, "bulldoze houses");
                    rec["bulldozed"] = victims.Count;
                    yield return Frames(30);
                    // (the neighbours Outgrow put in line for it, and removals it took for the game's)
                    var why = new JObject();
                    for (int k = 0; k < dirty0.Length; k++)
                    {
                        int d = Placemaker.Game.Neighbours.NeighbourPolishSystem.DirtyBy[k] - dirty0[k];
                        if (d != 0) why[Placemaker.Game.Neighbours.NeighbourPolishSystem.DirtyByNames[k]] = d;
                    }
                    rec["putInLine"] = why;
                    rec["takenForTheGames"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalRemovalsKept - kept0;
                    Mod.Log.Info($"Placemaker lab: {victims.Count} houses bulldozed ({what}); Outgrow put in line {why.ToString(Newtonsoft.Json.Formatting.None)}, took {rec["takenForTheGames"]} for removals by the game");
                    yield break;
                }
                case "outgrowquiet":
                {
                    // after Outgrow is idle: nothing it made should change any more (made again, deleted, shown again, lawns
                    // rewritten, lines moved): that is flicker
                    float wait = (float?)step["seconds"] ?? 15f;
                    var N = typeof(Placemaker.Game.Neighbours.NeighbourPolishSystem);
                    int objects0 = Placemaker.Game.Neighbours.NeighbourObjectSystem.TotalMade, areas0 = Placemaker.Game.Neighbours.NeighbourAreaSystem.TotalMade;
                    int deleted0 = Placemaker.Game.Neighbours.NeighbourDeleteSystem.TotalDeleted, shown0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalShownAgain;
                    int lawns0 = Placemaker.Game.Neighbours.NeighbourLawnSystem.TotalWritten, passes0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPasses;
                    int moved0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalMoved;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (clock.Elapsed.TotalSeconds < wait) yield return null;
                    rec["seconds"] = wait;
                    rec["objectsMade"] = Placemaker.Game.Neighbours.NeighbourObjectSystem.TotalMade - objects0;
                    rec["surfacesMade"] = Placemaker.Game.Neighbours.NeighbourAreaSystem.TotalMade - areas0;
                    rec["deleted"] = Placemaker.Game.Neighbours.NeighbourDeleteSystem.TotalDeleted - deleted0;
                    rec["shownAgain"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalShownAgain - shown0;
                    rec["lawnsWritten"] = Placemaker.Game.Neighbours.NeighbourLawnSystem.TotalWritten - lawns0;
                    rec["passes"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPasses - passes0;
                    rec["linesMoved"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalMoved - moved0;
                    Mod.Log.Info("Placemaker lab: outgrow quiet " + new JObject { ["seconds"] = rec["seconds"], ["objectsMade"] = rec["objectsMade"], ["surfacesMade"] = rec["surfacesMade"], ["deleted"] = rec["deleted"], ["shownAgain"] = rec["shownAgain"], ["lawnsWritten"] = rec["lawnsWritten"], ["passes"] = rec["passes"], ["linesMoved"] = rec["linesMoved"] }.ToString(Newtonsoft.Json.Formatting.None));
                    yield break;
                }
                case "polishpassdump":
                {
                    // every pass Outgrow plans from now on written down (its houses, barriers, ground) for the offline benchmark
                    // ("dir": a folder of its own under passes/, one per scenario of a file: else they write over each other)
                    var dir = System.IO.Path.Combine(_out, "passes", (string)step["dir"] ?? "");
                    // "near": [[x, z], ...] with "radius": only the passes editing a house that close to one of the spots
                    System.Collections.Generic.List<float2> near = null;
                    // ("rel": true — the spots from the land's middle)
                    var spotOrigin = (bool?)step["rel"] == true ? st.S.Block.Center : float2.zero;
                    if (step["near"] is JArray spots) near = spots.Select(p => new float2((float)p[0], (float)p[1]) + spotOrigin).ToList();
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.StartPassDump(dir, (int?)step["max"] ?? 40, near, (float?)step["radius"] ?? 60f);
                    rec["dir"] = dir;
                    Mod.Log.Info("Placemaker lab: Outgrow passes will be written to " + dir);
                    yield break;
                }
                case "polishidle":
                {
                    // until the polish has looked at every house it was asked to (a city switched on): how long it took, how
                    // many passes; the runner logs the step's worst frame
                    float limit = (float?)step["seconds"] ?? 300f;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    int passes0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPasses;
                    double upd0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.UpdateMs, pass0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.PassMainMs, fin0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.FinishMainMs, plan0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.PlanWorkerMs;
                    int plans0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.PlansTimed;
                    int retries0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalRetries, taken0 = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalTaken;
                    Placemaker.Game.Neighbours.NeighbourPolishSystem.ResetWorst();
                    int frames = 0;
                    var ms = new System.Collections.Generic.List<float>();
                    yield return Frames(4);
                    while (Placemaker.Game.Neighbours.NeighbourPolishSystem.Busy && clock.Elapsed.TotalSeconds < limit) { frames++; yield return null; ms.Add(UnityEngine.Time.unscaledDeltaTime * 1000f); }
                    rec["seconds"] = Math.Round(clock.Elapsed.TotalSeconds, 1);
                    rec["frames"] = frames;
                    rec["passes"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPasses - passes0;
                    rec["stillWaiting"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.Waiting;
                    {
                        int plans = Placemaker.Game.Neighbours.NeighbourPolishSystem.PlansTimed - plans0;
                        rec["outgrowMs"] = new JObject
                        {
                            ["frameTotal"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.UpdateMs - upd0, 1), ["frameWorst"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.UpdateMaxMs, 1),
                            ["passGather"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.PassMainMs - pass0, 1), ["passGatherWorst"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.PassMainMaxMs, 1),
                            ["write"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.FinishMainMs - fin0, 1), ["writeWorst"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.FinishMainMaxMs, 1),
                            ["plans"] = plans, ["planWorker"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.PlanWorkerMs - plan0, 1), ["planWorst"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.PlanWorkerMaxMs, 1),
                            ["retries"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalRetries - retries0, ["asked"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalTaken - taken0,
                        };
                        var parts = new JObject();
                        for (int k = 0; k < Placemaker.Game.Neighbours.NeighbourPolishSystem.PassPartMs.Length; k++) parts[Placemaker.Game.Neighbours.NeighbourPolishSystem.PassPartNames[k]] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.PassPartMs[k], 1);
                        for (int k = 0; k < Placemaker.Game.Neighbours.NeighbourPolishSystem.BarrierPartMs.Length; k++) parts["barriers." + Placemaker.Game.Neighbours.NeighbourPolishSystem.BarrierPartNames[k]] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.BarrierPartMs[k], 1);
                        rec["outgrowMs"]["gatherParts"] = parts;
                        var why = new JObject();
                        for (int k = 0; k < Placemaker.Game.Neighbours.NeighbourPolishSystem.DirtyBy.Length; k++) why[Placemaker.Game.Neighbours.NeighbourPolishSystem.DirtyByNames[k]] = Placemaker.Game.Neighbours.NeighbourPolishSystem.DirtyBy[k];
                        why["linesUnmovedSkipped"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.DirtySkipped;
                        var how = Placemaker.Game.Neighbours.NeighbourPolishSystem.LinesChangedHow;
                        why["linesHow"] = $"new {how[0]}, ours moved {how[1]}, game's moved {how[2]}, other model {how[3]}";
                        rec["outgrowMs"]["dirtyBy"] = why;
                        Mod.Log.Info("Placemaker lab: Outgrow time " + rec["outgrowMs"].ToString(Newtonsoft.Json.Formatting.None));
                    }
                    ms.Sort();
                    float worst = ms.Count > 0 ? ms[ms.Count - 1] : 0f, p95 = ms.Count > 0 ? ms[(int)(ms.Count * 0.95f)] : 0f, median = ms.Count > 0 ? ms[ms.Count / 2] : 0f;
                    int over33 = ms.Count(x => x > 33.4f), over50 = ms.Count(x => x > 50f), over100 = ms.Count(x => x > 100f);
                    rec["frameMs"] = new JObject { ["median"] = Math.Round(median, 1), ["p95"] = Math.Round(p95, 1), ["worst"] = Math.Round(worst, 1), ["over33"] = over33, ["over50"] = over50, ["over100"] = over100 };
                    Mod.Log.Info($"Placemaker lab: polish idle after {clock.Elapsed.TotalSeconds:F1} s, {frames} frames ({(frames > 0 ? clock.Elapsed.TotalMilliseconds / frames : 0):F1} ms a frame; median {median:F1}, 95% under {p95:F1}, worst {worst:F1} ms; {over33} over 33 ms, {over50} over 50, {over100} over 100), {rec["passes"]} passes, {rec["stillWaiting"]} still waiting");
                    yield break;
                }
                case "polishdump":
                {
                    // every house in the land as the polish planner sees it (tools: Tests NeighbourReplay draws plans offline)
                    var sys = _world.GetExistingSystemManaged<Placemaker.Game.Neighbours.NeighbourPolishSystem>();
                    if (sys == null) { rec["error"] = "lab: neighbourhood polish is not registered"; yield break; }
                    var b = st.S.Block;
                    var arr = new JArray();
                    JArray P(float2 p) => new JArray(Math.Round(p.x, 3), Math.Round(p.y, 3));
                    JArray Ring(float2[] r) => r == null ? null : new JArray(r.Select(p => (object)P(p)).ToArray());
                    foreach (var h in sys.DescribeIn(new float2(b.X0, b.Z0), new float2(b.X1, b.Z1)))
                        arr.Add(new JObject
                        {
                            ["id"] = h.Id, ["pos"] = P(h.Position), ["fwd"] = P(h.Forward), ["half"] = P(h.HalfLot),
                            ["lines"] = new JArray(h.Lines.Select(l => (object)new JObject { ["id"] = l.Id, ["name"] = l.Name, ["a"] = P(l.A), ["b"] = P(l.B) }).ToArray()),
                            ["lawns"] = new JArray(h.Lawns.Select(l => (object)new JObject { ["id"] = l.Id, ["ring"] = Ring(l.Ring) }).ToArray()),
                            ["hard"] = new JArray(h.Hard.Select(r => (object)Ring(r)).ToArray()),
                            ["footprint"] = Ring(h.Footprint),
                            ["props"] = new JArray(h.Props.Select(r => (object)Ring(r)).ToArray()),
                            ["main"] = h.MainSurface,
                            ["zone"] = h.Zone,
                        });
                    rec["houses"] = arr;
                    Mod.Log.Info($"Placemaker lab: polish dump of {arr.Count} houses");
                    yield break;
                }
                case "polishstats":
                {
                    // neighbourhood polish totals since the game started (Game/Neighbours/NeighbourPolishSystem)
                    yield return Frames(30);
                    rec["enabled"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.FeatureEnabled;
                    rec["paused"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.Paused;
                    rec["newBuildings"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.PolishNew;
                    rec["passes"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPasses;
                    rec["pairs"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalPairs;
                    rec["linesMoved"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalMoved;
                    rec["linesHidden"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalHidden;
                    rec["lawnsGrown"] = Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalLawns;
                    rec["lawnM2"] = Math.Round(Placemaker.Game.Neighbours.NeighbourPolishSystem.TotalLawnM2);
                    rec["paintStamps"] = Placemaker.Game.Neighbours.NeighbourPaintSystem.TotalStamps;
                    rec["surfacesMade"] = Placemaker.Game.Neighbours.NeighbourAreaSystem.TotalMade;
                    Mod.Log.Info("Placemaker lab: polish stats " + new JObject { ["passes"] = rec["passes"], ["pairs"] = rec["pairs"], ["moved"] = rec["linesMoved"], ["hidden"] = rec["linesHidden"], ["lawns"] = rec["lawnsGrown"], ["m2"] = rec["lawnM2"], ["paintStamps"] = rec["paintStamps"], ["surfaces"] = rec["surfacesMade"] }.ToString(Newtonsoft.Json.Formatting.None));
                    yield break;
                }
            }
            var shapes = _world.GetExistingSystemManaged<SuiteLabShapeSystem>();
            if (shapes == null) { rec["error"] = "lab: the shape system is not registered"; yield break; }
            yield return WaitFor(rec, "lab database", () => shapes.DatabaseReady || shapes.DatabaseState.StartsWith("failed"), 90f);
            rec["database"] = shapes.DatabaseState;
            if (!shapes.DatabaseReady) { rec["error"] = "lab: shape database " + shapes.DatabaseState; yield break; }
            string op = ((string)step["op"] ?? "mesh").ToLowerInvariant();
            if (op == "materials") { LabMaterials(shapes, rec); yield break; }
            yield return LabMesh(st, step, rec, shapes);
        }

        private void LabMaterials(SuiteLabShapeSystem shapes, JObject rec)
        {
            var all = shapes.Candidates();
            var arr = new JArray();
            foreach (var c in all.Take(60))
                arr.Add(new JObject { ["surface"] = c.Label, ["template"] = c.Template, ["vt"] = c.Vt, ["octahedral"] = c.Octo, ["suitable"] = SuiteLabShapeSystem.Suitable(c), ["keywords"] = string.Join(" ", c.Keywords) });
            rec["materials"] = arr;
            rec["materialCount"] = all.Count;
            rec["suitableCount"] = all.Count(SuiteLabShapeSystem.Suitable);
            var templates = all.GroupBy(c => c.Template).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()}");
            Mod.Log.Info($"Placemaker lab: {all.Count} building surfaces, {all.Count(SuiteLabShapeSystem.Suitable)} suitable; templates: {string.Join(", ", templates)}");
            foreach (var c in all.Take(25))
                Mod.Log.Info($"Placemaker lab: surface {c.Label} template {c.Template} VT {c.Vt} octahedral {c.Octo} suitable {SuiteLabShapeSystem.Suitable(c)} keywords [{string.Join(" ", c.Keywords)}]");
        }

        private IEnumerator LabMesh(ScenarioState st, JObject step, JObject rec, SuiteLabShapeSystem shapes)
        {
            var b = st.S.Block;
            string shape = (string)step["shape"] ?? "box";
            var size = step["size"] is JArray sz && sz.Count >= 2 ? new float2((float)sz[0], (float)sz[1]) : new float2(16f, 20f);
            float height = (float?)step["height"] ?? 12f;
            float yaw = (float?)step["yaw"] ?? 0f;
            var at = Pt(step["at"], b, false);
            var ring = SuiteLabMesh.Footprint(shape, size, step["points"] as JArray);
            var req = new SuiteLabShapeSystem.Request
            {
                Name = $"Placemaker Lab {shape} {++_labShapes}", Ring = ring, Height = height, Material = (string)step["material"],
            };
            shapes.Enqueue(req);
            yield return WaitFor(rec, "lab shape made", () => req.Done, 30f);
            foreach (var kv in req.Info) rec[kv.Key] = JToken.FromObject(kv.Value);
            rec["prefabName"] = req.Name;
            if (!req.Done || req.Error != null || req.Prefab == Entity.Null) { rec["error"] = "lab: shape not made: " + (req.Error ?? "timed out"); yield break; }
            var em = _world.EntityManager;
            yield return WaitFor(rec, "lab prefab ready", () => UnityEngine.Time.frameCount >= req.ReadyFrame && em.HasComponent<ObjectGeometryData>(req.Prefab), 15f);
            if (!em.HasComponent<ObjectGeometryData>(req.Prefab)) { rec["error"] = "lab: the new prefab never got its geometry data (not initialised)"; yield break; }
            var geom = em.GetComponentData<ObjectGeometryData>(req.Prefab);
            rec["geometrySize"] = new JArray(Math.Round(geom.m_Size.x, 2), Math.Round(geom.m_Size.y, 2), Math.Round(geom.m_Size.z, 2));
            rec["geometryFlags"] = geom.m_Flags.ToString();

            var rot = quaternion.RotateY(math.radians(yaw));
            var pos = new float3(at.x, _fixture.H(at.x, at.y), at.y);
            yield return ApplyWithFixtureTool(t =>
            {
                var def = t.NewDefinition();
                em.AddComponentData(def, new CreationDefinition { m_Prefab = req.Prefab, m_RandomSeed = 4242 + _labShapes });
                em.AddComponent<Updated>(def);
                em.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = pos, m_LocalPosition = pos, m_Rotation = rot, m_LocalRotation = rot, m_Scale = new float3(1f),
                    m_Intensity = 1f, m_Age = 0.5f, m_Probability = 100, m_PrefabSubIndex = -1, m_ParentMesh = -1, m_Elevation = 0f,
                });
                return 1;
            }, "lab shape " + req.Name);
            yield return Frames(30);

            // the placed object, and whether the renderer took it (a MeshBatch entry = it is in a draw batch)
            Entity placed = Entity.Null;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Objects.Transform>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                    if (em.GetComponentData<PrefabRef>(e).m_Prefab == req.Prefab) { placed = e; break; }
            rec["placed"] = placed != Entity.Null;
            if (placed == Entity.Null) { rec["error"] = "lab: the definition applied but no object of the new prefab exists"; yield break; }
            int batches = em.HasBuffer<global::Game.Rendering.MeshBatch>(placed) ? em.GetBuffer<global::Game.Rendering.MeshBatch>(placed, true).Length : -1;
            rec["meshBatches"] = batches;
            rec["components"] = new JArray(em.GetComponentTypes(placed, Allocator.Temp).ToArray().Select(c => c.GetManagedType()?.Name ?? c.ToString()).OrderBy(x => x));

            // the footprint in the world, so the camera's "area"/"build" target frames the shape
            var world = ring.Select(p => { var r3 = math.mul(rot, new float3(p.x, 0f, p.y)); return new float2(pos.x + r3.x, pos.z + r3.z); }).ToArray();
            st.Ring = st.Ring == null ? world : st.Ring.Concat(world).ToArray();
            Event("build");
            Mod.Log.Info($"Placemaker lab: placed '{req.Name}' at ({pos.x:F0}, {pos.z:F0}), {batches} mesh batch(es), geometry {geom.m_Size}");
        }
    }
}
