// Game runner — a city saved in one game and loaded in a fresh one (GitHub issue #2, white boxes after loading). GPL-3.0.
//   {"do":"lab","op":"oldDecal","decal":"ChirperDecalLarge01","tier":25,"at":[0,0],"yaw":0}
//       an object the way builds up to 0.1.1 left decorative decals: a runtime copy named like a see-through copy
//       ("Placemaker Decal 25 - ChirperDecalLarge01"), placed on the block. Only this game knows that prefab, so a save
//       made now names a prefab the next game does not have (what players met when the copies were not ready in time).
//   {"do":"lab","op":"saveCity","name":"PlacemakerSuiteReload"}     the city saved in the player's saves
//   {"do":"lab","op":"loadCity","name":"PlacemakerSuiteReload","delete":true}
//       that save loaded (in a fresh game: Send-Suite -Fresh); records how many objects stand on a missing prefab and
//       what the old-decal clean-up removed; "delete" removes the save afterwards. Only camera and shot steps make sense
//       after it (the test grid of this game is gone): frame with {"do":"camera","target":"point","at":[x,z],"abs":true}.
//   {"do":"lab","op":"oldDecalCleanup","on":false}   the clean-up after loads off (the "before" picture) or on again

using System;
using System.Collections;
using System.Linq;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Newtonsoft.Json.Linq;
using Placemaker.Game.Build;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private const string ReloadSaveName = "PlacemakerSuiteReload";

        private IEnumerator LabOldDecal(ScenarioState st, JObject step, JObject rec)
        {
            var shapes = _world.GetExistingSystemManaged<SuiteLabShapeSystem>();
            if (shapes == null) { rec["error"] = "lab: the shape system is not registered"; yield break; }
            yield return WaitFor(rec, "lab database", () => shapes.DatabaseReady || shapes.DatabaseState.StartsWith("failed"), 90f);
            if (!shapes.DatabaseReady) { rec["error"] = "lab: shape database " + shapes.DatabaseState; yield break; }
            string decal = (string)step["decal"] ?? "ChirperDecalLarge01";
            int tier = (int?)step["tier"] ?? 25;
            var req = new SuiteLabShapeSystem.Request { Name = Placemaker.Core.Decals.DecalLook.CloneName(decal, tier), CopyOf = decal };
            shapes.Enqueue(req);
            yield return WaitFor(rec, "old decal made", () => req.Done, 30f);
            rec["prefabName"] = req.Name;
            if (!req.Done || req.Error != null || req.Prefab == Entity.Null) { rec["error"] = "lab: old decal not made: " + (req.Error ?? "timed out"); yield break; }
            var em = _world.EntityManager;
            yield return WaitFor(rec, "old decal ready", () => UnityEngine.Time.frameCount >= req.ReadyFrame && em.HasComponent<ObjectGeometryData>(req.Prefab), 15f);
            if (!em.HasComponent<ObjectGeometryData>(req.Prefab)) { rec["error"] = "lab: the copy never got its geometry data"; yield break; }
            var size = em.GetComponentData<ObjectGeometryData>(req.Prefab).m_Size;

            var at = Pt(step["at"], st.S.Block, false);
            var rot = quaternion.RotateY(math.radians((float?)step["yaw"] ?? 0f));
            var pos = new float3(at.x, _fixture.H(at.x, at.y), at.y);
            yield return ApplyWithFixtureTool(t =>
            {
                var def = t.NewDefinition();
                em.AddComponentData(def, new CreationDefinition { m_Prefab = req.Prefab, m_RandomSeed = 4242 });
                em.AddComponent<Updated>(def);
                em.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = pos, m_LocalPosition = pos, m_Rotation = rot, m_LocalRotation = rot, m_Scale = new float3(1f),
                    m_Intensity = 1f, m_Age = 0.5f, m_Probability = 100, m_PrefabSubIndex = -1, m_ParentMesh = -1, m_Elevation = 0f,
                });
                return 1;
            }, "old decal " + req.Name);
            yield return Frames(30);
            int placed = 0;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var refs = q.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                for (int i = 0; i < refs.Length; i++) if (refs[i].m_Prefab == req.Prefab) placed++;
            rec["placed"] = placed;
            rec["atAbs"] = new JArray(Math.Round(pos.x, 1), Math.Round(pos.z, 1));
            rec["size"] = new JArray(Math.Round(size.x, 1), Math.Round(size.y, 1), Math.Round(size.z, 1));
            if (placed == 0) { rec["error"] = "lab: the definition applied but no object of the copy exists"; yield break; }
            float r = math.max(math.max(size.x, size.z) * 0.5f, 4f);
            var ring = new[] { new float2(pos.x - r, pos.z - r), new float2(pos.x + r, pos.z - r), new float2(pos.x + r, pos.z + r), new float2(pos.x - r, pos.z + r) };
            st.Ring = st.Ring == null ? ring : st.Ring.Concat(ring).ToArray();
            Event("build");
            Mod.Log.Info($"Placemaker lab: placed old decal '{req.Name}' at ({pos.x:F1}, {pos.z:F1}), size {size}");
        }

        private IEnumerator LabSaveCity(JObject step, JObject rec)
        {
            string name = (string)step["name"] ?? ReloadSaveName;
            var menu = _world.GetExistingSystemManaged<global::Game.UI.Menu.MenuUISystem>();
            if (menu == null || GameManager.instance == null) { rec["error"] = "lab: saveCity needs the game's menu system"; yield break; }
            System.Threading.Tasks.Task<bool> save = null;
            try { save = GameManager.instance.Save(name, menu.GetSaveInfo(false), Colossal.IO.AssetDatabase.AssetDatabase.user, (global::Game.UI.ScreenCaptureHelper.AsyncRequest)null); }
            catch (Exception e) { rec["error"] = "lab: save failed: " + e.Message; yield break; }
            yield return Until(() => save.IsCompleted, 120f, "save");
            if (!save.IsCompleted || save.IsFaulted) { rec["error"] = "lab: save failed: " + save.Exception?.GetBaseException().Message; yield break; }
            yield return Frames(10);
            rec["saved"] = ScratchSave(name) != null;
            rec["name"] = name;
            Mod.Log.Info($"Placemaker lab: city saved as '{name}' ({(ScratchSave(name) != null ? "found in the saves" : "NOT found in the saves")})");
        }

        private IEnumerator LabLoadCity(JObject step, JObject rec)
        {
            string name = (string)step["name"] ?? ReloadSaveName;
            var meta = ScratchSave(name);
            if (meta == null) { rec["error"] = "lab: no save named '" + name + "'"; yield break; }
            float t0 = Time.realtimeSinceStartup;
            Say("loading the save " + name);
            var load = GameManager.instance.Load(GameMode.Game, Colossal.Serialization.Entities.Purpose.LoadGame, meta);
            yield return Until(() => load.IsCompleted, 600f, "load task");
            yield return Until(() => GameManager.instance.gameMode == GameMode.Game && !GameManager.instance.isGameLoading, 300f, "game mode after load");
            rec["loadSeconds"] = Math.Round(Time.realtimeSinceStartup - t0, 1);
            rec["loaded"] = load.IsCompleted && !load.IsFaulted && load.Result;
            yield return Frames(60);
            _world.GetOrCreateSystemManaged<global::Game.Simulation.SimulationSystem>().selectedSpeed = 0f;
            // the save brings its own time of day (the pictures came out at dusk): the runner's light again
            _look?.Apply(Say);
            Event("load");
            rec["oldDecalsRemoved"] = OldDecalCleanupSystem.LastRemoved;
            rec["oldDecalCleanup"] = !OldDecalCleanupSystem.SuiteSkip;
            var (missing, ours) = ObjectsOnMissingPrefabs();
            rec["objectsOnMissingPrefabs"] = missing;
            rec["objectsOnMissingPlacemakerDecals"] = ours;
            Mod.Log.Info($"Placemaker lab: loaded '{name}' in {rec["loadSeconds"]} s: {missing} object(s) stand on a missing prefab ({ours} on a missing 'Placemaker Decal' copy); the clean-up "
                + (OldDecalCleanupSystem.SuiteSkip ? "was off" : $"removed {OldDecalCleanupSystem.LastRemoved}"));
            if ((bool?)step["delete"] == true)
            {
                try { var again = ScratchSave(name); if (again != null) SaveHelpers.DeleteSaveGame(again); rec["deleted"] = true; }
                catch (Exception e) { rec["deleteNote"] = "the save stays: " + e.Message; }
            }
        }

        /// <summary>Objects whose prefab is an obsolete placeholder (the save named a prefab this game does not have).</summary>
        private (int all, int ours) ObjectsOnMissingPrefabs()
        {
            var em = _world.EntityManager;
            var prefabs = _world.GetExistingSystemManaged<PrefabSystem>();
            int all = 0, ours = 0;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<global::Game.Objects.Object>(), ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var refs = q.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                var kindOf = new System.Collections.Generic.Dictionary<Entity, int>();   // 0 there, 1 missing, 2 a missing copy of ours
                for (int i = 0; i < refs.Length; i++)
                {
                    var p = refs[i].m_Prefab;
                    if (!kindOf.TryGetValue(p, out int kind))
                    {
                        kind = 0;
                        if (em.HasComponent<PrefabData>(p))
                        {
                            var pd = em.GetComponentData<PrefabData>(p);   // read whatever its enabled state
                            if (pd.m_Index < 0)
                            {
                                kind = 1;
                                string n = null;
                                try { n = prefabs.GetObsoleteID(pd).GetName(); } catch (Exception) { }
                                if (n != null && n.StartsWith(Placemaker.Core.Decals.DecalLook.ClonePrefix, StringComparison.Ordinal)) kind = 2;
                            }
                        }
                        kindOf[p] = kind;
                    }
                    if (kind > 0) all++;
                    if (kind == 2) ours++;
                }
            }
            return (all, ours);
        }
    }
}
